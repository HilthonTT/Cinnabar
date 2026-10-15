using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>PEXPIRE key milliseconds</c></summary>
[Command("PEXPIRE", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key milliseconds")]
internal sealed class PExpireHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => nowMs + (Int128)value;
}
