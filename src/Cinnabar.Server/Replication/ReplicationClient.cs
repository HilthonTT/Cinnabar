using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using Cinnabar.Protocol;
using Cinnabar.Server.Commands;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Diagnostics;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;
using Cinnabar.Storage.Persistence;
using Microsoft.Extensions.Logging;

namespace Cinnabar.Server.Replication;

/// <summary>
/// The replica side of replication: connects to the master, performs the handshake, loads
/// the snapshot, then applies the command stream. Reconnects with back-off if the link drops.
/// </summary>
/// <remarks>
/// Handshake: <c>AUTH</c> (if <c>masterauth</c> is set), <c>PING</c>,
/// <c>REPLCONF listening-port</c>, <c>REPLCONF capa psync2</c>, <c>PSYNC ? -1</c>.
/// </remarks>
internal sealed class ReplicationClient(
    ServerSettings settings,
    Keyspace keyspace,
    Lazy<CommandDispatcher> dispatcher,
    ServerMetrics metrics,
    ILogger<ReplicationClient> logger) : IAsyncDisposable
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stop = new();
    private int _disposeState;
    private Task? _loop;
    private long _offset;
    private volatile bool _linkUp;

    /// <summary><see langword="true"/> while connected and streaming.</summary>
    public bool IsLinkUp => _linkUp;

    /// <summary>Master offset this replica has applied up to.</summary>
    public long ProcessedOffset => Interlocked.Read(ref _offset);

    /// <summary>Replication ID announced by the master in <c>FULLRESYNC</c>.</summary>
    public string? MasterReplicationId { get; private set; }

    /// <summary>Starts the background replication loop.</summary>
    /// <param name="listeningPort">This server's port, announced to the master.</param>
    public void Start(int listeningPort) => _loop ??= Task.Run(() => RunAsync(listeningPort, _stop.Token));

    private async Task RunAsync(int listeningPort, CancellationToken cancellationToken)
    {
        TimeSpan backoff = TimeSpan.FromMilliseconds(250);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SyncAsync(listeningPort, () => backoff = TimeSpan.FromMilliseconds(250), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    "Replication link to {Host}:{Port} failed: {Reason}. Retrying in {Delay} ms.",
                    settings.Replication.MasterHost,
                    settings.Replication.MasterPort,
                    e.Message,
                    (int)backoff.TotalMilliseconds);
            }
            finally
            {
                _linkUp = false;
            }

            try
            {
                await Task.Delay(backoff, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private async Task SyncAsync(int listeningPort, Action onConnected, CancellationToken cancellationToken)
    {
        string host = settings.Replication.MasterHost!;
        int port = settings.Replication.MasterPort;

        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(host, port, cancellationToken);
        NetworkStream stream = tcp.GetStream();
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));

        try
        {
            async Task<RespValue> RequestAsync(params string[] parts)
            {
                await stream.WriteAsync(RespEncoder.Command(parts), cancellationToken);
                RespValue reply = await RespReader.ReadAsync(reader, RespReaderOptions.Default, cancellationToken)
                    ?? throw new EndOfStreamException("Master closed the connection during the handshake.");

                return reply.IsError
                    ? throw new IOException($"Master rejected {parts[0]} {string.Join(' ', parts[1..])}: {reply.Text}")
                    : reply;
            }

            if (!string.IsNullOrEmpty(settings.Replication.MasterAuth))
            {
                await RequestAsync("AUTH", settings.Replication.MasterAuth);
            }

            await RequestAsync("PING");
            await RequestAsync("REPLCONF", "listening-port", listeningPort.ToString(CultureInfo.InvariantCulture));
            await RequestAsync("REPLCONF", "capa", "psync2");
            RespValue fullResync = await RequestAsync("PSYNC", "?", "-1");

            string[] parts = (fullResync.Text ?? "").Split(' ');
            if (parts is not ["FULLRESYNC", var replicationId, var offsetText] ||
                !long.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture, out long startOffset))
            {
                throw new IOException($"Unexpected PSYNC reply '{fullResync.Text}'.");
            }

            byte[] rdb = await ReadRdbPayloadAsync(reader, cancellationToken);
            RdbSnapshot snapshot = RdbReader.Read(rdb);
            keyspace.Clear();
            int loaded = keyspace.Load(snapshot.Databases.GetValueOrDefault(0) ?? []);

            MasterReplicationId = replicationId;
            Interlocked.Exchange(ref _offset, startOffset);
            _linkUp = true;
            onConnected();
            logger.LogInformation(
                "Replicating from {Host}:{Port} (replid {ReplicationId}, offset {Offset}); loaded {Keys} key(s).",
                host,
                port,
                replicationId,
                startOffset,
                loaded);

            await StreamCommandsAsync(reader, stream, cancellationToken);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    /// <summary>Applies commands from the master until the connection closes.</summary>
    private async Task StreamCommandsAsync(PipeReader reader, NetworkStream stream, CancellationToken cancellationToken)
    {
        ClientSession link = ClientSession.ForReplicationLink();

        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken);
            ReadOnlySequence<byte> buffer = result.Buffer;

            while (true)
            {
                long before = buffer.Length;
                if (!RespReader.TryRead(ref buffer, out RespValue? value))
                {
                    break;
                }

                long frameLength = before - buffer.Length;
                await ApplyAsync(value!, frameLength, link, stream, cancellationToken);
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            if (result.IsCompleted)
            {
                throw new EndOfStreamException("Master closed the replication stream.");
            }
        }
    }

    private async Task ApplyAsync(
        RespValue value,
        long frameLength,
        ClientSession link,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        if (Command.TryCreate(value, out Command? command))
        {
            if (command.Name.Equals("REPLCONF", StringComparison.OrdinalIgnoreCase) &&
                command.Count > 0 &&
                command[0].Equals("GETACK", StringComparison.OrdinalIgnoreCase))
            {
                // Report the offset before this GETACK, as Redis does.
                string offset = ProcessedOffset.ToString(CultureInfo.InvariantCulture);
                await stream.WriteAsync(RespEncoder.Command("REPLCONF", "ACK", offset), cancellationToken);
            }
            else if (!command.Name.Equals("PING", StringComparison.OrdinalIgnoreCase))
            {
                Reply reply = await dispatcher.Value.DispatchAsync(command, link, cancellationToken);
                if (reply.IsError)
                {
                    logger.LogWarning("Replicated command '{Command}' failed: {Error}", command.Name, reply.ToString().Trim());
                }
            }
        }

        long newOffset = Interlocked.Add(ref _offset, frameLength);
        metrics.ReplicationOffset.Set(newOffset);
    }

    /// <summary>Reads <c>$&lt;length&gt;\r\n&lt;bytes&gt;</c> (no trailing CRLF).</summary>
    private static async Task<byte[]> ReadRdbPayloadAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken);
            ReadOnlySequence<byte> buffer = result.Buffer;
            var sequence = new SequenceReader<byte>(buffer);

            if (sequence.TryReadTo(out ReadOnlySequence<byte> header, "\r\n"u8, advancePastDelimiter: true))
            {
                string headerText = Encoding.ASCII.GetString(header.ToArray());
                if (!headerText.StartsWith('$') ||
                    !int.TryParse(headerText.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int length))
                {
                    throw new IOException($"Expected an RDB bulk header, got '{headerText}'.");
                }

                if (sequence.Remaining >= length)
                {
                    byte[] payload = sequence.UnreadSequence.Slice(0, length).ToArray();
                    reader.AdvanceTo(sequence.UnreadSequence.GetPosition(length));
                    return payload;
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                throw new EndOfStreamException("Master closed the connection during the RDB transfer.");
            }
        }
    }

    /// <summary>Stops replicating.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }

        _stop.Dispose();
    }
}
