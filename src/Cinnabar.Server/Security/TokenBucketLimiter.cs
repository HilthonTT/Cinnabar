namespace Cinnabar.Server.Security;

/// <summary>
/// Token-bucket rate limiter: holds up to <c>capacity</c> tokens, refills continuously at
/// <c>refillPerSecond</c>, and each command spends one token.
/// </summary>
internal sealed class TokenBucketLimiter
{
    private readonly Lock _lock = new();
    private readonly double _capacity;
    private readonly double _refillPerSecond;
    private readonly TimeProvider _time;

    private double _tokens;
    private long _lastRefillTimestamp;

    public TokenBucketLimiter(int capacity, double refillPerSecond, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegative(refillPerSecond);

        _time = timeProvider ?? TimeProvider.System;
        _capacity = capacity;
        _refillPerSecond = refillPerSecond;
        _tokens = capacity;
        _lastRefillTimestamp = _time.GetTimestamp();
    }

    /// <summary>Takes one token. Returns <see langword="false"/> if the bucket is empty.</summary>
    public bool TryConsume()
    {
        lock (_lock)
        {
            long now = _time.GetTimestamp();
            double elapsedSeconds = _time.GetElapsedTime(_lastRefillTimestamp, now).TotalSeconds;
            _lastRefillTimestamp = now;
            _tokens = Math.Min(_capacity, _tokens + (elapsedSeconds * _refillPerSecond));

            // Tolerate floating-point drift so exactly one refilled token counts.
            if (_tokens < 1 - 1e-9)
            {
                return false;
            }

            _tokens = Math.Max(_tokens - 1, 0);
            return true;
        }
    }
}
