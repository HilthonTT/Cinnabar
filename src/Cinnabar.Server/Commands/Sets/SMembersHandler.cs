using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary><c>SMEMBERS key</c>: every member, in no particular order.</summary>
[Command("SMEMBERS", MinArgs = 1, MaxArgs = 1, Group = "set", Syntax = "key")]
internal sealed class SMembersHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.SetMembers(command[0])));
}
