using Cinnabar.Server.Concurrency;

namespace Cinnabar.Server.Replication;

/// <summary>
/// The gate between write commands (shared) and replica snapshots (exclusive).
/// </summary>
/// <remarks>
/// A full resync needs the snapshot and the replication offset to describe the same moment.
/// Every write command holds the barrier in shared mode from execution until it has been
/// propagated; taking a snapshot waits for in-flight writes to drain and briefly holds off
/// new ones. Without this, a write could be both in the snapshot and replayed afterwards,
/// or in neither.
/// </remarks>
internal sealed class WriteBarrier : SharedExclusiveGate;
