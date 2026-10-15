using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>PEXPIREAT key unix-time-milliseconds</c></summary>
[Command("PEXPIREAT", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key unix-time-milliseconds")]
internal sealed class PExpireAtHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => value;
}
