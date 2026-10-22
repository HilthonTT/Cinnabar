namespace Cinnabar.Client;

public sealed partial class RedisClient
{
    // ─────────────────────────── Sets ───────────────────────────

    /// <summary><c>SADD key member [member ...]</c>: the number of members that were new.</summary>
    public async Task<long> SAddAsync(string key, params string[] members) =>
        (await ExecuteAsync(["SADD", key, .. members], CancellationToken.None).ConfigureAwait(false)).AsLong();

    /// <summary><c>SREM key member [member ...]</c>: the number of members removed.</summary>
    public async Task<long> SRemAsync(string key, params string[] members) =>
        (await ExecuteAsync(["SREM", key, .. members], CancellationToken.None).ConfigureAwait(false)).AsLong();

    /// <summary><c>SISMEMBER key member</c></summary>
    public async Task<bool> SIsMemberAsync(string key, string member, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["SISMEMBER", key, member], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>SMEMBERS key</c>: every member, in no particular order.</summary>
    public async Task<List<string?>> SMembersAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["SMEMBERS", key], cancellationToken).ConfigureAwait(false)).AsStringList();

    /// <summary><c>SCARD key</c>: the number of members.</summary>
    public async Task<long> SCardAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["SCARD", key], cancellationToken).ConfigureAwait(false)).AsLong();

    /// <summary><c>SPOP key</c>: removes and returns a random member, or <see langword="null"/>.</summary>
    public async Task<string?> SPopAsync(string key, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["SPOP", key], cancellationToken).ConfigureAwait(false)).AsString();

    /// <summary><c>SMOVE source destination member</c>: <see langword="true"/> if the member was moved.</summary>
    public async Task<bool> SMoveAsync(string source, string destination, string member, CancellationToken cancellationToken = default) =>
        (await ExecuteAsync(["SMOVE", source, destination, member], cancellationToken).ConfigureAwait(false)).AsLong() == 1;

    /// <summary><c>SINTER key [key ...]</c>: members in every set.</summary>
    public async Task<List<string?>> SInterAsync(params string[] keys) =>
        (await ExecuteAsync(["SINTER", .. keys], CancellationToken.None).ConfigureAwait(false)).AsStringList();

    /// <summary><c>SUNION key [key ...]</c>: members in any set.</summary>
    public async Task<List<string?>> SUnionAsync(params string[] keys) =>
        (await ExecuteAsync(["SUNION", .. keys], CancellationToken.None).ConfigureAwait(false)).AsStringList();

    /// <summary><c>SDIFF key [key ...]</c>: members of the first set that are in none of the others.</summary>
    public async Task<List<string?>> SDiffAsync(params string[] keys) =>
        (await ExecuteAsync(["SDIFF", .. keys], CancellationToken.None).ConfigureAwait(false)).AsStringList();
}
