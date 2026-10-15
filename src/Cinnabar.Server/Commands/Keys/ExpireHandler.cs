using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>EXPIRE key seconds</c>: 1 if the TTL was set, 0 if the key does not exist.</summary>
[Command("EXPIRE", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key seconds")]
internal sealed class ExpireHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => nowMs + ((Int128)value * 1000);
}
