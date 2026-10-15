using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>EXPIREAT key unix-time-seconds</c></summary>
[Command("EXPIREAT", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key unix-time-seconds")]
internal sealed class ExpireAtHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => (Int128)value * 1000;
}
