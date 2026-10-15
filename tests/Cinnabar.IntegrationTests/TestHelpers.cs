using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using Cinnabar.Client.PubSub;
using Cinnabar.Protocol;
using Cinnabar.Server.Blocking;
using Microsoft.Extensions.DependencyInjection;

namespace Cinnabar.IntegrationTests;

/// <summary>Polling and timeout helpers.</summary>
internal static class Wait
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Polls <paramref name="condition"/> until it is true or the timeout passes, then fails.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            if (await condition())
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            await Task.Delay(20);
        }
    }

    public static Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null) =>
        UntilAsync(() => Task.FromResult(condition()), because, timeout);

    /// <summary>Awaits <paramref name="task"/>, failing if it takes longer than the timeout.</summary>
    public static Task<T> WithTimeout<T>(this Task<T> task, TimeSpan? timeout = null) =>
        task.WaitAsync(timeout ?? DefaultTimeout);

    public static Task WithTimeout(this Task task, TimeSpan? timeout = null) =>
        task.WaitAsync(timeout ?? DefaultTimeout);

    /// <summary>Number of keys some blocked command (BLPOP, XREAD BLOCK) is currently waiting on.</summary>
    public static int WatchedKeyCount(this TestServer server) =>
        server.Server.Services.GetRequiredService<KeyNotifier>().WatchedKeyCount;

    /// <summary>Waits until blocked commands are watching exactly <paramref name="count"/> keys.</summary>
    public static Task UntilWatchedKeysAsync(this TestServer server, int count) =>
        UntilAsync(() => server.WatchedKeyCount() == count, $"blocked commands watch {count} key(s)");

    /// <summary>Reads the next pub/sub message, failing after a timeout.</summary>
    public static async Task<RedisMessage> NextAsync(this IAsyncEnumerator<RedisMessage> messages, TimeSpan? timeout = null)
    {
        bool moved = await messages.MoveNextAsync().AsTask().WaitAsync(timeout ?? DefaultTimeout);
        moved.Should().BeTrue("the subscriber should still be open");
        return messages.Current;
    }
}

/// <summary>A raw TCP connection for protocol-level tests.</summary>
internal sealed class RawConnection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;

    private RawConnection(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(leaveOpen: true));
    }

    public static async Task<RawConnection> ConnectAsync(int port)
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync("127.0.0.1", port);
        return new RawConnection(tcp);
    }

    public Task WriteAsync(string text) => WriteAsync(Encoding.UTF8.GetBytes(text));

    public async Task WriteAsync(byte[] bytes)
    {
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();
    }

    public Task SendAsync(params string[] command) => WriteAsync(RespEncoder.Command(command));

    /// <summary>Sends a command and reads one reply.</summary>
    public async Task<RespValue> CommandAsync(params string[] command)
    {
        await SendAsync(command);
        return await ReadAsync();
    }

    /// <summary>Reads one reply; fails if the connection closes or nothing arrives in time.</summary>
    public async Task<RespValue> ReadAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? Wait.DefaultTimeout);
        RespValue? value = await RespReader.ReadAsync(_reader, RespReaderOptions.Default, cts.Token);
        return value ?? throw new IOException("The server closed the connection.");
    }

    /// <summary>Reads exactly <paramref name="count"/> raw bytes.</summary>
    public async Task<byte[]> ReadBytesAsync(int count, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? Wait.DefaultTimeout);
        while (true)
        {
            ReadResult result = await _reader.ReadAsync(cts.Token);
            if (result.Buffer.Length >= count)
            {
                byte[] bytes = result.Buffer.Slice(0, count).ToArray();
                _reader.AdvanceTo(result.Buffer.GetPosition(count));
                return bytes;
            }

            _reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            if (result.IsCompleted)
            {
                throw new IOException("The server closed the connection.");
            }
        }
    }

    /// <summary>
    /// <see langword="true"/> if the server closes the connection (EOF or reset) within the
    /// timeout without sending anything else.
    /// </summary>
    public async Task<bool> IsClosedByServerAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? Wait.DefaultTimeout);
        try
        {
            ReadResult result = await _reader.ReadAsync(cts.Token);
            bool closed = result.IsCompleted && result.Buffer.IsEmpty;
            _reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            return closed;
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Closes the socket from the client side.</summary>
    public void Close() => _tcp.Close();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _reader.CompleteAsync();
        }
        catch (Exception)
        {
            // Best effort.
        }

        _tcp.Dispose();
    }
}
