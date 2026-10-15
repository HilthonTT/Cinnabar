using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary><c>INCR key</c>: adds 1 and returns the new value.</summary>
[Command("INCR", MinArgs = 1, MaxArgs = 1, Flags = CommandFlags.Write, Group = "string", Syntax = "key")]
internal sealed class IncrHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.IncrementBy(command[0], 1)));
}

/// <summary><c>DECR key</c>: subtracts 1 and returns the new value.</summary>
[Command("DECR", MinArgs = 1, MaxArgs = 1, Flags = CommandFlags.Write, Group = "string", Syntax = "key")]
internal sealed class DecrHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.IncrementBy(command[0], -1)));
}

/// <summary><c>INCRBY key increment</c></summary>
[Command("INCRBY", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "string", Syntax = "key increment")]
internal sealed class IncrByHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.IncrementBy(command[0], Arguments.ParseLong(command[1]))));
}

/// <summary><c>DECRBY key decrement</c></summary>
[Command("DECRBY", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "string", Syntax = "key decrement")]
internal sealed class DecrByHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        long decrement = Arguments.ParseLong(command[1]);
        if (decrement == long.MinValue)
        {
            // -long.MinValue does not fit in a long.
            throw new StorageException("ERR decrement would overflow");
        }

        return ValueTask.FromResult(Reply.Integer(keyspace.IncrementBy(command[0], -decrement)));
    }
}
