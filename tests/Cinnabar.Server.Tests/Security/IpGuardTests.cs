using Cinnabar.Server.Configuration;
using Cinnabar.Server.Security;
using Microsoft.Extensions.Time.Testing;

namespace Cinnabar.Server.Tests.Security;

public sealed class IpGuardTests
{
    private readonly FakeTimeProvider _time = new();

    private IpGuard Create(Action<SecuritySettings> configure)
    {
        var settings = new ServerSettings();
        configure(settings.Security);
        return new IpGuard(settings, _time);
    }

    [Fact]
    public void Accepts_connections_within_limits()
    {
        IpGuard guard = Create(_ => { });

        guard.TryAcceptConnection("1.1.1.1").Should().BeNull();
        guard.TotalConnections.Should().Be(1);
    }

    [Fact]
    public void Rejects_over_per_ip_limit()
    {
        IpGuard guard = Create(s => s.MaxConnectionsPerIp = 2);

        guard.TryAcceptConnection("1.1.1.1").Should().BeNull();
        guard.TryAcceptConnection("1.1.1.1").Should().BeNull();
        guard.TryAcceptConnection("1.1.1.1").Should().Be("ERR max number of clients per IP address reached");

        guard.TryAcceptConnection("2.2.2.2").Should().BeNull("the limit is per IP");
        guard.TotalConnections.Should().Be(3);
    }

    [Fact]
    public void Rejects_over_total_limit()
    {
        IpGuard guard = Create(s => s.MaxConnections = 2);

        guard.TryAcceptConnection("1.1.1.1").Should().BeNull();
        guard.TryAcceptConnection("2.2.2.2").Should().BeNull();
        guard.TryAcceptConnection("3.3.3.3").Should().Be("ERR max number of clients reached");

        guard.TotalConnections.Should().Be(2);
    }

    [Fact]
    public void Release_frees_a_per_ip_slot()
    {
        IpGuard guard = Create(s => s.MaxConnectionsPerIp = 1);
        guard.TryAcceptConnection("1.1.1.1").Should().BeNull();

        guard.ReleaseConnection("1.1.1.1");

        guard.TotalConnections.Should().Be(0);
        guard.TryAcceptConnection("1.1.1.1").Should().BeNull();
    }

    [Fact]
    public void Release_frees_a_total_slot()
    {
        IpGuard guard = Create(s => s.MaxConnections = 1);
        guard.TryAcceptConnection("1.1.1.1");

        guard.ReleaseConnection("1.1.1.1");

        guard.TryAcceptConnection("2.2.2.2").Should().BeNull();
    }

    [Fact]
    public void Rejected_per_ip_connection_does_not_leak_total_slot()
    {
        IpGuard guard = Create(s =>
        {
            s.MaxConnections = 2;
            s.MaxConnectionsPerIp = 1;
        });
        guard.TryAcceptConnection("1.1.1.1");
        guard.TryAcceptConnection("1.1.1.1").Should().NotBeNull();

        guard.TryAcceptConnection("2.2.2.2").Should().BeNull();
    }

    [Fact]
    public void Rate_limit_allows_burst_then_refuses()
    {
        IpGuard guard = Create(s =>
        {
            s.RateLimitBurst = 3;
            s.RateLimitPerSecond = 1;
        });

        Enumerable.Range(0, 3).Select(_ => guard.TryConsumeCommand("1.1.1.1")).ToList().Should().AllSatisfy(b => b.Should().BeTrue());
        guard.TryConsumeCommand("1.1.1.1").Should().BeFalse();
    }

    [Fact]
    public void Rate_limit_is_per_ip()
    {
        IpGuard guard = Create(s =>
        {
            s.RateLimitBurst = 1;
            s.RateLimitPerSecond = 1;
        });
        guard.TryConsumeCommand("1.1.1.1");

        guard.TryConsumeCommand("1.1.1.1").Should().BeFalse();
        guard.TryConsumeCommand("2.2.2.2").Should().BeTrue();
    }

    [Fact]
    public void Rate_limit_refills_with_time()
    {
        IpGuard guard = Create(s =>
        {
            s.RateLimitBurst = 1;
            s.RateLimitPerSecond = 4;
        });
        guard.TryConsumeCommand("1.1.1.1");
        guard.TryConsumeCommand("1.1.1.1").Should().BeFalse();

        _time.Advance(TimeSpan.FromMilliseconds(250));

        guard.TryConsumeCommand("1.1.1.1").Should().BeTrue();
    }

    [Fact]
    public void Rate_limit_bucket_survives_reconnect()
    {
        IpGuard guard = Create(s =>
        {
            s.RateLimitBurst = 1;
            s.RateLimitPerSecond = 1;
        });
        guard.TryAcceptConnection("1.1.1.1");
        guard.TryConsumeCommand("1.1.1.1");
        guard.ReleaseConnection("1.1.1.1");
        guard.TryAcceptConnection("1.1.1.1");

        guard.TryConsumeCommand("1.1.1.1").Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rate_limit_disabled_when_rate_not_positive(double rate)
    {
        IpGuard guard = Create(s =>
        {
            s.RateLimitPerSecond = rate;
            s.RateLimitBurst = 1;
        });

        Enumerable.Range(0, 1000).Select(_ => guard.TryConsumeCommand("1.1.1.1")).ToList().Should().AllSatisfy(b => b.Should().BeTrue());
    }

    [Fact]
    public void Burst_below_one_is_treated_as_one()
    {
        IpGuard guard = Create(s =>
        {
            s.RateLimitBurst = 0;
            s.RateLimitPerSecond = 1;
        });

        guard.TryConsumeCommand("1.1.1.1").Should().BeTrue();
        guard.TryConsumeCommand("1.1.1.1").Should().BeFalse();
    }
}
