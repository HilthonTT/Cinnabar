using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary><c>LSET key index element</c>: replaces the element at an index.</summary>
[Command("LSET", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "list", Syntax = "key index element")]
internal sealed class LSetHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        keyspace.ListSet(command[0], Arguments.ParseLong(command[1]), command[2]);
        return ValueTask.FromResult(Reply.Ok);
    }
}
