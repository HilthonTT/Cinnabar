namespace Cinnabar.Storage;

/// <summary>The options of <c>XCLAIM</c>.</summary>
/// <param name="IdleMilliseconds">Set the delivery time to this long ago (<c>IDLE</c>).</param>
/// <param name="TimeMilliseconds">Set the delivery time to this Unix time (<c>TIME</c>).</param>
/// <param name="RetryCount">Set the delivery count (<c>RETRYCOUNT</c>).</param>
/// <param name="Force">Create pending entries for IDs that exist in the stream but are not pending (<c>FORCE</c>).</param>
/// <param name="JustId">Do not count a delivery (<c>JUSTID</c>).</param>
/// <param name="LastId">Advance the group's last delivered ID to at least this (<c>LASTID</c>).</param>
public sealed record StreamClaimOptions(
    long? IdleMilliseconds = null,
    long? TimeMilliseconds = null,
    long? RetryCount = null,
    bool Force = false,
    bool JustId = false,
    StreamId? LastId = null)
{
    /// <summary>No options.</summary>
    public static StreamClaimOptions Default { get; } = new();
}
