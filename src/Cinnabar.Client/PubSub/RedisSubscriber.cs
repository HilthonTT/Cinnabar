using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Cinnabar.Client.Internal;
using Cinnabar.Protocol;

namespace Cinnabar.Client.PubSub;

/// <summary>
/// A pub/sub subscription on its own dedicated connection (a subscribed connection can't
/// run normal commands, so it never comes from the pool). Dispose it to unsubscribe.
/// </summary>
/// <example>
/// <code>
/// await using RedisSubscriber subscriber = await client.SubscribeAsync("news");
/// await foreach (RedisMessage message in subscriber.Messages(cancellationToken))
/// {
///     Console.WriteLine($"{message.Channel}: {message.Message}");
/// }
/// </code>
/// </example>
public sealed class RedisSubscriber : IAsyncDisposable
{
    private readonly RedisConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<RedisMessage> _messages =
        Channel.CreateUnbounded<RedisMessage>(new UnboundedChannelOptions { SingleWriter = true });

    private Task? _readLoop;
    private bool _disposed;

    private RedisSubscriber(RedisConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// Subscribes and waits until the server has confirmed every channel and pattern, so
    /// messages published after this returns are guaranteed to be delivered.
    /// </summary>
    internal static async Task<RedisSubscriber> StartAsync(
        RedisConnection connection,
        IReadOnlyCollection<string> channels,
        IReadOnlyCollection<string> patterns,
        CancellationToken cancellationToken)
    {
        var subscriber = new RedisSubscriber(connection);
        try
        {
            if (channels.Count > 0)
            {
                await connection.SendAsync(["SUBSCRIBE", .. channels], cancellationToken).ConfigureAwait(false);
            }

            if (patterns.Count > 0)
            {
                await connection.SendAsync(["PSUBSCRIBE", .. patterns], cancellationToken).ConfigureAwait(false);
            }

            for (int confirmed = 0; confirmed < channels.Count + patterns.Count;)
            {
                RespValue reply = (await connection.ReadAsync(cancellationToken).ConfigureAwait(false)).ThrowIfError();
                if (Kind(reply) is "subscribe" or "psubscribe")
                {
                    confirmed++;
                }
                else
                {
                    // A message on an already-confirmed channel can arrive before the
                    // remaining confirmations; keep it.
                    subscriber.TryDeliver(reply);
                }
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        subscriber._readLoop = Task.Run(subscriber.ReadLoopAsync);
        return subscriber;
    }

    /// <summary>
    /// Messages as they arrive. Completes when the subscriber is disposed or the
    /// connection drops.
    /// </summary>
    public async IAsyncEnumerable<RedisMessage> Messages([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (RedisMessage message in _messages.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return message;
        }
    }

    /// <summary>Subscribes to more channels. Confirmation is asynchronous.</summary>
    public Task SubscribeAsync(params string[] channels) => SendAsync("SUBSCRIBE", channels);

    /// <summary>Unsubscribes from channels; with no arguments, from all of them.</summary>
    public Task UnsubscribeAsync(params string[] channels) => SendAsync("UNSUBSCRIBE", channels);

    /// <summary>Subscribes to more glob patterns. Confirmation is asynchronous.</summary>
    public Task PSubscribeAsync(params string[] patterns) => SendAsync("PSUBSCRIBE", patterns);

    /// <summary>Removes pattern subscriptions; with no arguments, all of them.</summary>
    public Task PUnsubscribeAsync(params string[] patterns) => SendAsync("PUNSUBSCRIBE", patterns);

    private async Task SendAsync(string command, string[] channels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writeLock.WaitAsync(_stop.Token).ConfigureAwait(false);
        try
        {
            await _connection.SendAsync([command, .. channels], _stop.Token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                RespValue push = await _connection.ReadAsync(_stop.Token).ConfigureAwait(false);
                TryDeliver(push);
            }
        }
        catch (Exception e) when (e is RedisConnectionException || _stop.IsCancellationRequested)
        {
            // The connection dropped, or we are being disposed.
        }
        finally
        {
            _messages.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Queues a <c>message</c>/<c>pmessage</c> push; (p)subscribe/(p)unsubscribe
    /// confirmations need no action.
    /// </summary>
    private void TryDeliver(RespValue push)
    {
        string? kind = Kind(push);
        if (kind == "message" && push.Elements is [_, var channel, var payload] &&
            channel.AsString() is { } channelName && payload.AsString() is { } text)
        {
            _messages.Writer.TryWrite(new RedisMessage(channelName, text));
        }
        else if (kind == "pmessage" && push.Elements is [_, var pattern, var matched, var body] &&
                 pattern.AsString() is { } patternText && matched.AsString() is { } matchedChannel &&
                 body.AsString() is { } bodyText)
        {
            _messages.Writer.TryWrite(new RedisMessage(matchedChannel, bodyText, patternText));
        }
    }

    private static string? Kind(RespValue push) =>
        push.Type == RespType.Array && push.Elements is { Count: > 0 } elements ? elements[0].AsString() : null;

    /// <summary>Closes the dedicated connection, which ends every subscription.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);

        // Let the read loop observe cancellation and leave ReadAsync before the connection's
        // PipeReader is completed; completing it underneath a pending read throws.
        if (_readLoop is not null)
        {
            await _readLoop.ConfigureAwait(false);
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        _stop.Dispose();
        _writeLock.Dispose();
    }
}
