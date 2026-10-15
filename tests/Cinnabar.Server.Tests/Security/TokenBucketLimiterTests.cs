using Cinnabar.Server.Security;
using Microsoft.Extensions.Time.Testing;

namespace Cinnabar.Server.Tests.Security;

public sealed class TokenBucketLimiterTests
{
    private readonly FakeTimeProvider _time = new();

    private static int ConsumeAll(TokenBucketLimiter limiter, int max = 10_000)
    {
        int taken = 0;
        while (taken < max && limiter.TryConsume())
        {
            taken++;
        }

        return taken;
    }

    [Fact]
    public void Allows_a_full_burst_then_refuses()
    {
        var limiter = new TokenBucketLimiter(capacity: 5, refillPerSecond: 1, _time);

        ConsumeAll(limiter).Should().Be(5);
        limiter.TryConsume().Should().BeFalse();
    }

    [Fact]
    public void Refills_over_time()
    {
        var limiter = new TokenBucketLimiter(capacity: 5, refillPerSecond: 2, _time);
        ConsumeAll(limiter);

        _time.Advance(TimeSpan.FromSeconds(1));

        ConsumeAll(limiter).Should().Be(2);
    }

    [Fact]
    public void Refills_fractional_tokens_across_calls()
    {
        // Regression: a refill rate that rounded to zero per call never refilled the bucket.
        var limiter = new TokenBucketLimiter(capacity: 1, refillPerSecond: 10, _time);
        limiter.TryConsume().Should().BeTrue();

        for (int i = 0; i < 9; i++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(10));
            limiter.TryConsume().Should().BeFalse();
        }

        // 90 ms = 0.9 tokens so far; a little over 10 ms more completes the token. (Exactly
        // 10 ms would sum ten floating-point 0.1s, which lands just below 1.)
        _time.Advance(TimeSpan.FromMilliseconds(11));
        limiter.TryConsume().Should().BeTrue();
    }

    [Fact]
    public void Slow_refill_rate_still_refills()
    {
        var limiter = new TokenBucketLimiter(capacity: 1, refillPerSecond: 0.5, _time);
        limiter.TryConsume().Should().BeTrue();

        _time.Advance(TimeSpan.FromSeconds(1));
        limiter.TryConsume().Should().BeFalse();

        _time.Advance(TimeSpan.FromSeconds(1));
        limiter.TryConsume().Should().BeTrue();
    }

    [Fact]
    public void Refill_never_exceeds_capacity()
    {
        var limiter = new TokenBucketLimiter(capacity: 3, refillPerSecond: 100, _time);

        _time.Advance(TimeSpan.FromHours(1));

        ConsumeAll(limiter).Should().Be(3);
    }

    [Fact]
    public void Zero_refill_rate_never_refills()
    {
        var limiter = new TokenBucketLimiter(capacity: 2, refillPerSecond: 0, _time);
        ConsumeAll(limiter);

        _time.Advance(TimeSpan.FromDays(1));

        limiter.TryConsume().Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void Invalid_arguments_throw(int capacity, double refill)
    {
        Action act = () => _ = new TokenBucketLimiter(capacity, refill, _time);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
