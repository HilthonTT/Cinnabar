using Prometheus;

namespace Cinnabar.Server.Diagnostics;

/// <summary>
/// Prometheus metrics, exported on <c>http://host:&lt;metrics-port&gt;/metrics</c>.
/// </summary>
/// <remarks>
/// prometheus-net registers metrics in a process-wide registry, so several servers in one
/// process (as in the integration tests) share these series.
/// </remarks>
internal sealed class ServerMetrics
{
    private const string Prefix = "cinnabar";

    public Counter ConnectionsTotal { get; } = Metrics.CreateCounter(
        $"{Prefix}_connections_total", "Client connections accepted.");

    public Counter ConnectionsRejectedTotal { get; } = Metrics.CreateCounter(
        $"{Prefix}_connections_rejected_total", "Client connections rejected by connection limits.");

    public Gauge ActiveConnections { get; } = Metrics.CreateGauge(
        $"{Prefix}_connections_active", "Currently open client connections.");

    public Counter CommandsTotal { get; } = Metrics.CreateCounter(
        $"{Prefix}_commands_total", "Commands processed, by command and outcome.",
        new CounterConfiguration { LabelNames = ["command", "status"] });

    public Histogram CommandDuration { get; } = Metrics.CreateHistogram(
        $"{Prefix}_command_duration_seconds", "Command execution time.",
        new HistogramConfiguration
        {
            LabelNames = ["command"],
            Buckets = Histogram.ExponentialBuckets(0.00001, 2, 16),
        });

    public Counter RateLimitedTotal { get; } = Metrics.CreateCounter(
        $"{Prefix}_rate_limited_total", "Commands rejected by the per-IP rate limiter.");

    public Gauge Keys { get; } = Metrics.CreateGauge(
        $"{Prefix}_keys", "Keys in the keyspace (may include expired keys not yet swept).");

    public Gauge ConnectedReplicas { get; } = Metrics.CreateGauge(
        $"{Prefix}_replication_connected_replicas", "Replicas connected to this master.");

    public Gauge ReplicationOffset { get; } = Metrics.CreateGauge(
        $"{Prefix}_replication_offset_bytes", "Replication offset (master: produced, replica: processed).");

    /// <summary>Records one executed command.</summary>
    public void RecordCommand(string name, TimeSpan duration, bool success)
    {
        string label = name.ToLowerInvariant();
        CommandsTotal.WithLabels(label, success ? "ok" : "error").Inc();
        CommandDuration.WithLabels(label).Observe(duration.TotalSeconds);
    }
}
