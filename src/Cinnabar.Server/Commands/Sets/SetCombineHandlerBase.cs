using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>Shared logic for SINTER, SUNION and SDIFF.</summary>
internal abstract class SetCombineHandlerBase(Keyspace keyspace, SetOperation operation) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.BulkStringArray(keyspace.SetCombine(operation, command.Arguments)));
}
