namespace Cinnabar.Storage;

/// <summary>The outcome of <c>XCLAIM</c> or <c>XAUTOCLAIM</c>.</summary>
/// <param name="Entries">The claimed entries, in the order they were claimed.</param>
/// <param name="DeliveryTimeMilliseconds">The delivery time recorded for them, in Unix milliseconds.</param>
/// <param name="NextCursor">For <c>XAUTOCLAIM</c>: where to continue, or 0-0 when the scan is complete.</param>
/// <param name="LastDeliveredId">The group's last delivered ID afterwards (<c>LASTID</c> may move it).</param>
public sealed record StreamClaimResult(
    IReadOnlyList<StreamEntry> Entries,
    long DeliveryTimeMilliseconds,
    StreamId NextCursor,
    StreamId LastDeliveredId);
