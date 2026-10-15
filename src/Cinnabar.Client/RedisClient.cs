using System.Globalization;
using Cinnabar.Client.Internal;
using Cinnabar.Client.PubSub;
using Cinnabar.Protocol;

namespace Cinnabar.Client;

/// <summary>
/// An async client for Cinnabar and other Redis-compatible servers, backed by a connection
/// pool. Thread-safe: create one and share it (register it as a singleton).
/// </summary>
/// <example>
/// <code>
/// await using var client = new RedisClient("localhost", 6379);
/// await client.SetAsync("greeting", "hello", TimeSpan.FromMinutes(5));
/// string? greeting = await client.GetAsync("greeting");
/// </code>
/// </example>
/// <remarks>
/// Server error replies throw <see cref="RedisException"/>; network and protocol failures
/// throw <see cref="RedisConnectionException"/>.
/// </remarks>
public sealed class RedisClient : IAsyncDisposable
{
    private readonly ConnectionPool _pool;
    private readonly RedisClientOptions _options;
    private bool _disposed;

    /// <summary>Creates a client from options.</summary>
    public RedisClient(RedisClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _pool = new ConnectionPool(options);
    }

    /// <summary>Creates a client for <paramref name="host"/>:<paramref name="port"/> with default options.</summary>
    public RedisClient(string host = "localhost", int port = 6379)
        : this(new RedisClientOptions { Host = host, Port = port })
    {
    }

    /// <summary>The options this client uses.</summary>
    public RedisClientOptions Options => _options;

    // ─────────────────────────── Connection / server ───────────────────────────

    /// <summary><c>PING</c>: <see langword="true"/> if the server answers PONG.</summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["PING"], cancellationToken).ConfigureAwait(false)).AsString() == "PONG";

    /// <summary><c>ECHO message</c></summary>
    public async Task<string?> EchoAsync(string message, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["ECHO", message], cancellationToken).ConfigureAwait(false)).AsString();

    /// <summary><c>CONFIG GET parameter</c>: matching name/value pairs, flattened.</summary>
    public async Task<List<string?>> ConfigGetAsync(string parameter, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["CONFIG", "GET", parameter], cancellationToken).ConfigureAwait(false)).AsStringList();

    /// <summary><c>INFO [section]</c>: the raw INFO text.</summary>
    public async Task<string?> InfoAsync(string? section = null, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(section is null ? ["INFO"] : ["INFO", section], cancellationToken).ConfigureAwait(false)).AsString();

    /// <summary><c>DBSIZE</c>: number of keys.</summary>
    public async Task<long> DbSizeAsync(CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["DBSIZE"], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>FLUSHDB</c>: deletes every key.</summary>
    public Task FlushDbAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(["FLUSHDB"], cancellationToken);

    // ─────────────────────────── Keys ───────────────────────────

    /// <summary><c>DEL key [key ...]</c>: number of keys removed.</summary>
    public async Task<long> DeleteAsync(params string[] keys) =>
        (await ExecuteAsync(["DEL", .. keys]).ConfigureAwait(false)).AsLong();

    /// <summary><c>EXISTS key [key ...]</c>: how many of the keys exist.</summary>
    public async Task<long> ExistsAsync(params string[] keys) =>
        (await ExecuteAsync(["EXISTS", .. keys]).ConfigureAwait(false)).AsLong();

    /// <summary><c>PEXPIRE key ms</c>: <see langword="true"/> if the key exists and the TTL was set.</summary>
    public async Task<bool> ExpireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["PEXPIRE", key, Format((long)ttl.TotalMilliseconds)], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>PERSIST key</c>: <see langword="true"/> if a TTL was removed.</summary>
    public async Task<bool> PersistAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["PERSIST", key], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary>
    /// <c>PTTL key</c>: remaining time to live, or <see langword="null"/> if the key does not
    /// exist or has no TTL.
    /// </summary>
    public async Task<TimeSpan?> TimeToLiveAsync(string key, CancellationToken cancellationToken = default)
    {
        long ms = (await ExecuteAsync(["PTTL", key], cancellationToken).ConfigureAwait(false)).AsLong();
        return ms < 0 ? null : TimeSpan.FromMilliseconds(ms);
    }

    /// <summary><c>TYPE key</c>: <c>string</c>, <c>list</c>, <c>stream</c> or <c>none</c>.</summary>
    public async Task<string> TypeAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["TYPE", key], cancellationToken).ConfigureAwait(false)).AsString() ?? "none";

    /// <summary><c>KEYS pattern</c>: keys matching a glob such as <c>user:*</c>.</summary>
    public async Task<List<string?>> KeysAsync(string pattern = "*", CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["KEYS", pattern], cancellationToken).ConfigureAwait(false)).AsStringList();

    // ─────────────────────────── Strings ───────────────────────────

    /// <summary><c>GET key</c>: the value, or <see langword="null"/> if the key does not exist.</summary>
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["GET", key], cancellationToken).ConfigureAwait(false)).AsString();

    /// <summary><c>SET key value</c></summary>
    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
        ExecuteAsync(["SET", key, value], cancellationToken);

    /// <summary><c>SET key value PX ms</c>: stores a value that expires after <paramref name="expiry"/>.</summary>
    public Task SetAsync(string key, string value, TimeSpan expiry, CancellationToken cancellationToken = default) =>
        ExecuteAsync(["SET", key, value, "PX", Format(Math.Max((long)expiry.TotalMilliseconds, 1))], cancellationToken);

    /// <summary>
    /// <c>SET key value NX [PX ms]</c>: stores the value only if the key does not exist.
    /// Returns <see langword="true"/> if it was stored. Handy for simple locks.
    /// </summary>
    public async Task<bool> SetIfNotExistsAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        string[] command = expiry is { } ttl
            ? ["SET", key, value, "NX", "PX", Format(Math.Max((long)ttl.TotalMilliseconds, 1))]
            : ["SET", key, value, "NX"];
        return !(await ExecuteAsync(command, cancellationToken).ConfigureAwait(false)).IsNull;
    }

    /// <summary><c>INCR key</c>: the new value.</summary>
    public async Task<long> IncrementAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["INCR", key], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>DECR key</c>: the new value.</summary>
    public async Task<long> DecrementAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["DECR", key], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>INCRBY key delta</c>: the new value.</summary>
    public async Task<long> IncrementByAsync(string key, long delta, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["INCRBY", key, Format(delta)], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>DECRBY key delta</c>: the new value.</summary>
    public async Task<long> DecrementByAsync(string key, long delta, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["DECRBY", key, Format(delta)], cancellationToken).ConfigureAwait(false)).AsLong();

    // ─────────────────────────── Lists ───────────────────────────

    /// <summary><c>LPUSH key value [value ...]</c>: the new length.</summary>
    public async Task<long> LPushAsync(string key, params string[] values) =>
        (await ExecuteAsync(["LPUSH", key, .. values]).ConfigureAwait(false)).AsLong();

    /// <summary><c>RPUSH key value [value ...]</c>: the new length.</summary>
    public async Task<long> RPushAsync(string key, params string[] values) =>
        (await ExecuteAsync(["RPUSH", key, .. values]).ConfigureAwait(false)).AsLong();

    /// <summary><c>LPOP key</c>: the first element, or <see langword="null"/>.</summary>
    public async Task<string?> LPopAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["LPOP", key], cancellationToken).ConfigureAwait(false)).AsString();

    /// <summary><c>LPOP key count</c>: up to <paramref name="count"/> elements (empty if the key does not exist).</summary>
    public async Task<List<string?>> LPopAsync(string key, int count, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["LPOP", key, Format(count)], cancellationToken).ConfigureAwait(false)).AsStringList();

    /// <summary><c>RPOP key</c>: the last element, or <see langword="null"/>.</summary>
    public async Task<string?> RPopAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["RPOP", key], cancellationToken).ConfigureAwait(false)).AsString();

    /// <summary>
    /// <c>BLPOP key [key ...] timeout</c>: waits up to <paramref name="timeout"/> (forever if
    /// <see langword="null"/>) for an element. Returns the key it came from and the element,
    /// or <see langword="null"/> on timeout.
    /// </summary>
    /// <remarks>Holds a pooled connection while waiting.</remarks>
    public async Task<(string Key, string Value)?> BLPopAsync(
        IReadOnlyCollection<string> keys,
        TimeSpan? timeout,
        CancellationToken cancellationToken = default)
    {
        string seconds = timeout is { } t ? t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) : "0";
        RespValue reply = await ExecuteAsync(["BLPOP", .. keys, seconds], cancellationToken).ConfigureAwait(false);

        return reply.IsNull || reply.AsArray() is not [var key, var value]
            ? null
            : (key.AsString()!, value.AsString()!);
    }

    /// <summary><c>BLPOP key timeout</c> with the timeout in seconds; 0 waits forever.</summary>
    public Task<(string Key, string Value)?> BLPopAsync(string key, double timeoutSeconds = 0, CancellationToken cancellationToken = default) =>
        BLPopAsync([key], timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null, cancellationToken);

    /// <summary><c>LLEN key</c></summary>
    public async Task<long> LLenAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["LLEN", key], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>LRANGE key start end</c>: inclusive; negative indexes count from the end.</summary>
    public async Task<List<string?>> LRangeAsync(string key, long start = 0, long end = -1, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["LRANGE", key, Format(start), Format(end)], cancellationToken).ConfigureAwait(false)).AsStringList();

    // ─────────────────────────── Streams ───────────────────────────

    /// <summary>
    /// <c>XADD key id field value [...]</c>: appends an entry and returns its ID.
    /// Use <c>*</c> as <paramref name="id"/> to let the server generate it.
    /// </summary>
    public async Task<string> XAddAsync(
        string key,
        string id,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken = default)
    {
        List<string> command = ["XADD", key, id];
        foreach ((string field, string value) in fields)
        {
            command.Add(field);
            command.Add(value);
        }

        if (command.Count == 3)
        {
            throw new ArgumentException("A stream entry needs at least one field.", nameof(fields));
        }

        return (await ExecuteAsync([.. command], cancellationToken).ConfigureAwait(false)).AsString()!;
    }

    /// <summary><c>XADD key * field value [...]</c>: appends with a server-generated ID.</summary>
    public Task<string> XAddAsync(string key, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken = default) =>
        XAddAsync(key, "*", fields, cancellationToken);

    /// <summary><c>XLEN key</c></summary>
    public async Task<long> XLenAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["XLEN", key], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>XRANGE key start end [COUNT n]</c>: entries between two IDs, inclusive (<c>-</c>/<c>+</c> for min/max).</summary>
    public async Task<List<RedisStreamEntry>> XRangeAsync(
        string key,
        string start = "-",
        string end = "+",
        int? count = null,
        CancellationToken cancellationToken = default)
    {
        string[] command = count is { } n ? ["XRANGE", key, start, end, "COUNT", Format(n)] : ["XRANGE", key, start, end];
        return ParseEntries(await ExecuteAsync(command, cancellationToken).ConfigureAwait(false));
    }

    // ─────────────────────────── Pub/Sub ───────────────────────────

    /// <summary><c>PUBLISH channel message</c>: number of subscribers that received it.</summary>
    public async Task<long> PublishAsync(string channel, string message, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["PUBLISH", channel, message], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary>
    /// Subscribes on a new dedicated connection. Returns once the server has confirmed every
    /// channel. Dispose the subscriber to unsubscribe and close the connection.
    /// </summary>
    public async Task<RedisSubscriber> SubscribeAsync(IReadOnlyCollection<string> channels, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (channels.Count == 0)
        {
            throw new ArgumentException("Subscribe to at least one channel.", nameof(channels));
        }

        RedisConnection connection = await RedisConnection.OpenAsync(_options, cancellationToken).ConfigureAwait(false);
        return await RedisSubscriber.StartAsync(connection, channels, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Subscribes to <paramref name="channels"/>; see <see cref="SubscribeAsync(IReadOnlyCollection{string}, CancellationToken)"/>.</summary>
    public Task<RedisSubscriber> SubscribeAsync(params string[] channels) => SubscribeAsync(channels, CancellationToken.None);

    // ─────────────────────────── Transactions ───────────────────────────

    /// <summary>
    /// Runs commands as a transaction (<c>MULTI</c> … <c>EXEC</c>) on one connection and
    /// returns each command's reply. Replies may be errors (inspect them or call
    /// <see cref="RespValueExtensions.ThrowIfError"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// IReadOnlyList&lt;RespValue&gt; results = await client.ExecuteTransactionAsync(
    /// [
    ///     ["INCR", "visits"],
    ///     ["EXPIRE", "visits", "60"],
    /// ]);
    /// </code>
    /// </example>
    /// <exception cref="RedisException">The server rejected the transaction (for example EXECABORT).</exception>
    public async Task<IReadOnlyList<RespValue>> ExecuteTransactionAsync(
        IReadOnlyList<string[]> commands,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        List<IReadOnlyList<string>> batch = [["MULTI"], .. commands, ["EXEC"]];
        await using ConnectionPool.Lease lease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RespValue> replies = await lease.Connection.ExecuteManyAsync(batch, cancellationToken).ConfigureAwait(false);

        RespValue exec = replies[^1].ThrowIfError();
        if (exec.IsNull)
        {
            throw new RedisException("EXECABORT Transaction aborted.");
        }

        return exec.AsArray();
    }

    // ─────────────────────────── Raw ───────────────────────────

    /// <summary>
    /// Runs any command and returns the raw reply. Error replies throw <see cref="RedisException"/>.
    /// </summary>
    public Task<RespValue> ExecuteAsync(params string[] command) => ExecuteAsync(command, CancellationToken.None);

    /// <summary>
    /// Runs any command and returns the raw reply. Error replies throw <see cref="RedisException"/>.
    /// </summary>
    public async Task<RespValue> ExecuteAsync(string[] command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfZero(command.Length);

        await using ConnectionPool.Lease lease = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
        RespValue reply = await lease.Connection.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        return reply.ThrowIfError();
    }

    /// <summary>Closes every pooled connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _pool.DisposeAsync().ConfigureAwait(false);
    }

    private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static List<RedisStreamEntry> ParseEntries(RespValue reply)
    {
        var entries = new List<RedisStreamEntry>();
        foreach (RespValue entry in reply.AsArray())
        {
            if (entry.AsArray() is not [var id, var fieldList])
            {
                continue;
            }

            IReadOnlyList<RespValue> flat = fieldList.AsArray();
            var fields = new List<KeyValuePair<string, string>>(flat.Count / 2);
            for (int i = 0; i + 1 < flat.Count; i += 2)
            {
                fields.Add(new KeyValuePair<string, string>(flat[i].AsString() ?? "", flat[i + 1].AsString() ?? ""));
            }

            entries.Add(new RedisStreamEntry(id.AsString() ?? "", fields));
        }

        return entries;
    }
}
