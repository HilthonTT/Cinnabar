namespace Cinnabar.Server.Concurrency;

/// <summary>
/// Makes <c>EXEC</c> isolated: ordinary commands hold the gate in shared mode while they run,
/// and <c>EXEC</c> runs its whole transaction, including the <c>WATCH</c> check, in exclusive
/// mode. No other client's command can interleave with a transaction.
/// </summary>
/// <remarks>
/// Blocking commands (BLPOP, XREAD BLOCK, WAIT) hold the gate only while they look at data,
/// never while they wait, or a client blocked forever would stall every EXEC. Lock order is
/// this gate first, then the replication <see cref="Replication.WriteBarrier"/>.
/// </remarks>
internal sealed class TransactionGate : SharedExclusiveGate;
