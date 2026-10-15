using System.Collections.Concurrent;
using Cinnabar.Server.Configuration;

namespace Cinnabar.Server.Security;

/// <summary>
/// Per-IP connection limits and command rate limiting.
/// </summary>
/// <remarks>
/// State is kept per IP address for the lifetime of the process, so reconnecting does not
/// reset a client's rate-limit bucket. Memory grows with the number of distinct client IPs.
/// </remarks>
internal sealed class IpGuard(ServerSettings settings, TimeProvider? timeProvider = null)
{
    private readonly SecuritySettings _security = settings.Security;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, PerIpState> _states = new();
    private int _totalConnections;

    /// <summary>Current number of accepted connections.</summary>
    public int TotalConnections => Volatile.Read(ref _totalConnections);

    /// <summary>
    /// Reserves a connection slot for <paramref name="ip"/>. Every successful call must be
    /// paired with <see cref="ReleaseConnection"/>.
    /// </summary>
    /// <returns>A reason to reject the connection, or <see langword="null"/> if it is accepted.</returns>
    public string? TryAcceptConnection(string ip)
    {
        if (Interlocked.Increment(ref _totalConnections) > _security.MaxConnections)
        {
            Interlocked.Decrement(ref _totalConnections);
            return "ERR max number of clients reached";
        }

        PerIpState state = GetState(ip);
        if (Interlocked.Increment(ref state.ActiveConnections) > _security.MaxConnectionsPerIp)
        {
            Interlocked.Decrement(ref state.ActiveConnections);
            Interlocked.Decrement(ref _totalConnections);
            return "ERR max number of clients per IP address reached";
        }

        return null;
    }

    /// <summary>Releases a slot reserved by <see cref="TryAcceptConnection"/>.</summary>
    public void ReleaseConnection(string ip)
    {
        if (_states.TryGetValue(ip, out PerIpState? state))
        {
            Interlocked.Decrement(ref state.ActiveConnections);
        }

        Interlocked.Decrement(ref _totalConnections);
    }

    /// <summary>Spends one command token for <paramref name="ip"/>.</summary>
    /// <returns><see langword="false"/> when the IP is over its rate limit.</returns>
    public bool TryConsumeCommand(string ip) =>
        _security.RateLimitPerSecond <= 0 || GetState(ip).RateLimiter.TryConsume();

    private PerIpState GetState(string ip) =>
        _states.GetOrAdd(ip, _ => new PerIpState(
            new TokenBucketLimiter(Math.Max(_security.RateLimitBurst, 1), _security.RateLimitPerSecond, _time)));

    private sealed class PerIpState(TokenBucketLimiter rateLimiter)
    {
        public int ActiveConnections;

        public TokenBucketLimiter RateLimiter { get; } = rateLimiter;
    }
}
