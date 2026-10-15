using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary><c>DEL key [key ...]</c>: number of keys removed.</summary>
[Command("DEL", MinArgs = 1, Flags = CommandFlags.Write, Group = "generic", Syntax = "key [key ...]")]
internal sealed class DelHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(command.Arguments.Count(keyspace.Delete) is var removed and > 0
            ? Reply.Integer(removed)
            : Reply.Zero.WithoutPropagation());
}

/// <summary><c>EXISTS key [key ...]</c>: how many of the keys exist (repeats count again).</summary>
[Command("EXISTS", MinArgs = 1, Group = "generic", Syntax = "key [key ...]")]
internal sealed class ExistsHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(command.Arguments.Count(keyspace.Exists)));
}

/// <summary><c>TYPE key</c>: string, list, stream or none.</summary>
[Command("TYPE", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "key")]
internal sealed class TypeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.SimpleString(keyspace.GetKeyType(command[0]).ToString().ToLowerInvariant()));
}

/// <summary><c>KEYS pattern</c>: keys matching a glob (<c>*</c>, <c>?</c>, <c>[a-z]</c>).</summary>
[Command("KEYS", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "pattern")]
internal sealed class KeysHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.Keys(command[0])));
}

/// <summary><c>DBSIZE</c>: number of keys.</summary>
[Command("DBSIZE", MaxArgs = 0, Group = "server")]
internal sealed class DbSizeHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.Keys().Count));
}

/// <summary><c>FLUSHDB [ASYNC | SYNC]</c>: removes every key.</summary>
[Command("FLUSHDB", MaxArgs = 1, Flags = CommandFlags.Write, Group = "server", Syntax = "[ASYNC | SYNC]")]
internal sealed class FlushDbHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (command.Count == 1 && !Arguments.Is(command[0], "ASYNC") && !Arguments.Is(command[0], "SYNC"))
        {
            return ValueTask.FromResult(Reply.Error(Arguments.SyntaxError));
        }

        keyspace.Clear();
        return ValueTask.FromResult(Reply.Ok);
    }
}

/// <summary><c>FLUSHALL [ASYNC | SYNC]</c>: same as FLUSHDB (there is one database).</summary>
[Command("FLUSHALL", MaxArgs = 1, Flags = CommandFlags.Write, Group = "server", Syntax = "[ASYNC | SYNC]")]
internal sealed class FlushAllHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        new FlushDbHandler(keyspace).ExecuteAsync(command, session, cancellationToken);
}

/// <summary>
/// Shared logic for EXPIRE, PEXPIRE, EXPIREAT and PEXPIREAT. Everything is converted to an
/// absolute Unix time in milliseconds, which is also what gets replicated.
/// </summary>
internal abstract class ExpireHandlerBase(Keyspace keyspace, TimeProvider timeProvider) : ICommandHandler
{
    /// <summary>
    /// Converts the command's time argument to an absolute Unix time in milliseconds. Uses
    /// <see cref="Int128"/> so huge arguments are detected instead of overflowing.
    /// </summary>
    protected abstract Int128 ToAbsoluteMilliseconds(long value, long nowMs);

    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        long nowMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        Int128 absolute = ToAbsoluteMilliseconds(Arguments.ParseLong(command[1]), nowMs);

        if (absolute > long.MaxValue || absolute < long.MinValue)
        {
            return ValueTask.FromResult(Reply.Error($"ERR invalid expire time in '{command.Name.ToLowerInvariant()}' command"));
        }

        long expiresAt = (long)absolute;
        if (!keyspace.ExpireAt(key, expiresAt))
        {
            return ValueTask.FromResult(Reply.Zero.WithoutPropagation());
        }

        return ValueTask.FromResult(Reply.One.WithPropagation(new Command("PEXPIREAT", key, Arguments.Format(expiresAt))));
    }
}

/// <summary><c>EXPIRE key seconds</c>: 1 if the TTL was set, 0 if the key does not exist.</summary>
[Command("EXPIRE", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key seconds")]
internal sealed class ExpireHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => nowMs + ((Int128)value * 1000);
}

/// <summary><c>PEXPIRE key milliseconds</c></summary>
[Command("PEXPIRE", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key milliseconds")]
internal sealed class PExpireHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => nowMs + (Int128)value;
}

/// <summary><c>EXPIREAT key unix-time-seconds</c></summary>
[Command("EXPIREAT", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key unix-time-seconds")]
internal sealed class ExpireAtHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => (Int128)value * 1000;
}

/// <summary><c>PEXPIREAT key unix-time-milliseconds</c></summary>
[Command("PEXPIREAT", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "generic", Syntax = "key unix-time-milliseconds")]
internal sealed class PExpireAtHandler(Keyspace keyspace, TimeProvider timeProvider) : ExpireHandlerBase(keyspace, timeProvider)
{
    protected override Int128 ToAbsoluteMilliseconds(long value, long nowMs) => value;
}

/// <summary><c>PERSIST key</c>: removes the TTL. 1 if one was removed.</summary>
[Command("PERSIST", MinArgs = 1, MaxArgs = 1, Flags = CommandFlags.Write, Group = "generic", Syntax = "key")]
internal sealed class PersistHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(keyspace.Persist(command[0]) ? Reply.One : Reply.Zero.WithoutPropagation());
}

/// <summary><c>TTL key</c>: seconds left (rounded), -1 without TTL, -2 if missing.</summary>
[Command("TTL", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "key")]
internal sealed class TtlHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long ms = keyspace.GetTimeToLiveMilliseconds(command[0]);
        return ValueTask.FromResult(Reply.Integer(ms < 0 ? ms : (ms + 500) / 1000));
    }
}

/// <summary><c>PTTL key</c>: milliseconds left, -1 without TTL, -2 if missing.</summary>
[Command("PTTL", MinArgs = 1, MaxArgs = 1, Group = "generic", Syntax = "key")]
internal sealed class PTtlHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.GetTimeToLiveMilliseconds(command[0])));
}
