using Cinnabar.Protocol;

namespace Cinnabar.Client;

public sealed partial class RedisClient
{
    // ─────────────────────────── Hashes ───────────────────────────

    /// <summary><c>HSET key field value</c>: <see langword="true"/> if the field is new.</summary>
    public async Task<bool> HSetAsync(string key, string field, string value, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HSET", key, field, value], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>HSET key field value [field value ...]</c>: the number of fields that were new.</summary>
    public async Task<long> HSetAsync(string key, IEnumerable<KeyValuePair<string, string>> fields, CancellationToken cancellationToken = default)
    {
        string[] command = ["HSET", key, .. Flatten(fields)];
        if (command.Length == 2)
        {
            throw new ArgumentException("Set at least one field.", nameof(fields));
        }

        return (await ExecuteAsync(command, cancellationToken).ConfigureAwait(false)).AsLong();
    }

    /// <summary><c>HSETNX key field value</c>: sets the field only if it does not exist.</summary>
    public async Task<bool> HSetIfNotExistsAsync(string key, string field, string value, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HSETNX", key, field, value], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>HGET key field</c>: the value, or <see langword="null"/>.</summary>
    public async Task<string?> HGetAsync(string key, string field, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HGET", key, field], cancellationToken).ConfigureAwait(false)).AsString();

    /// <summary><c>HMGET key field [field ...]</c>: the values; <see langword="null"/> for missing fields.</summary>
    public async Task<List<string?>> HMGetAsync(string key, params string[] fields) =>
        (await ExecuteAsync(["HMGET", key, .. fields], CancellationToken.None).ConfigureAwait(false)).AsStringList();

    /// <summary><c>HGETALL key</c>: every field and value; empty if the key does not exist.</summary>
    public async Task<Dictionary<string, string>> HGetAllAsync(string key, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RespValue> flat = (await ExecuteAsync(["HGETALL", key], cancellationToken).ConfigureAwait(false)).AsArray();
        var hash = new Dictionary<string, string>(flat.Count / 2, StringComparer.Ordinal);
        for (int i = 0; i + 1 < flat.Count; i += 2)
        {
            hash[flat[i].AsString() ?? ""] = flat[i + 1].AsString() ?? "";
        }

        return hash;
    }

    /// <summary><c>HKEYS key</c>: every field name.</summary>
    public async Task<List<string?>> HKeysAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HKEYS", key], cancellationToken).ConfigureAwait(false)).AsStringList();

    /// <summary><c>HVALS key</c>: every value.</summary>
    public async Task<List<string?>> HValsAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HVALS", key], cancellationToken).ConfigureAwait(false)).AsStringList();

    /// <summary><c>HDEL key field [field ...]</c>: the number of fields removed.</summary>
    public async Task<long> HDelAsync(string key, params string[] fields) =>
        (await ExecuteAsync(["HDEL", key, .. fields], CancellationToken.None).ConfigureAwait(false)).AsLong();

    /// <summary><c>HEXISTS key field</c></summary>
    public async Task<bool> HExistsAsync(string key, string field, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HEXISTS", key, field], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>HLEN key</c>: the number of fields.</summary>
    public async Task<long> HLenAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HLEN", key], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>HINCRBY key field increment</c>: the new value.</summary>
    public async Task<long> HIncrByAsync(string key, string field, long increment, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["HINCRBY", key, field, Format(increment)], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>HINCRBYFLOAT key field increment</c>: the new value.</summary>
    public async Task<double> HIncrByFloatAsync(string key, string field, double increment, CancellationToken cancellationToken = default) =>
        ParseScore((await ExecuteAsync(["HINCRBYFLOAT", key, field, FormatScore(increment)], cancellationToken).ConfigureAwait(false)).AsString())!.Value;
}
