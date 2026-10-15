using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Cinnabar.Protocol;

namespace Cinnabar.Client.Tests.Infrastructure;

/// <summary>A command received by <see cref="FakeRedisServer"/>, with the index of the TCP connection it arrived on.</summary>
public sealed record ReceivedCommand(int Connection, IReadOnlyList<string> Parts);

/// <summary>
/// A tiny scripted RESP server on 127.0.0.1. Each command is passed to a handler that returns
/// the raw reply bytes (or <see langword="null"/> to send nothing).
/// </summary>
public sealed class FakeRedisServer : IAsyncDisposable
{
    public delegate Task<byte[]?> Handler(ReceivedCommand command, CancellationToken cancellationToken);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ConcurrentBag<TcpClient> _clients = [];
    private readonly Handler _handler;
    private readonly Task _acceptLoop;
    private int _accepted;

    public FakeRedisServer(Handler? handler = null)
    {
        _handler = handler ?? DefaultReply;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync();
    }

    public int Port { get; }

    /// <summary>Number of TCP connections accepted so far.</summary>
    public int AcceptedConnections => Volatile.Read(ref _accepted);

    public ConcurrentQueue<ReceivedCommand> Received { get; } = new();

    public RedisClientOptions Options(Action<RedisClientOptions>? configure = null)
    {
        var options = new RedisClientOptions
        {
            Host = "127.0.0.1",
            Port = Port,
            PoolTimeout = TimeSpan.FromSeconds(5),
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        configure?.Invoke(options);
        return options;
    }

    public static byte[] Reply(string wire) => Encoding.UTF8.GetBytes(wire);

    /// <summary>Answers PING, AUTH and ECHO like Redis; anything else gets +OK.</summary>
    public static Task<byte[]?> DefaultReply(ReceivedCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<byte[]?>(command.Parts[0].ToUpperInvariant() switch
        {
            "PING" => Reply("+PONG\r\n"),
            "ECHO" => RespEncoder.BulkString(command.Parts[1]),
            _ => Reply("+OK\r\n"),
        });

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_shutdown.Token);
                int index = Interlocked.Increment(ref _accepted) - 1;
                _clients.Add(client);
                _ = Task.Run(() => ServeAsync(client, index));
            }
        }
        catch (Exception) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    private async Task ServeAsync(TcpClient client, int index)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            PipeReader reader = PipeReader.Create(stream);
            while (!_shutdown.IsCancellationRequested)
            {
                RespValue? value = await RespReader.ReadAsync(reader, RespReaderOptions.Server, _shutdown.Token);
                if (value is null)
                {
                    break;
                }

                var command = new ReceivedCommand(index, value.AsStringList().Select(s => s ?? "").ToList());
                Received.Enqueue(command);

                byte[]? reply = await _handler(command, _shutdown.Token);
                if (reply is not null)
                {
                    await stream.WriteAsync(reply, _shutdown.Token);
                }
            }
        }
        catch (Exception)
        {
            // Connection closed or server shutting down.
        }
        finally
        {
            client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener.Stop();
        foreach (TcpClient client in _clients)
        {
            client.Dispose();
        }

        try
        {
            await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
        }

        _shutdown.Dispose();
    }
}
