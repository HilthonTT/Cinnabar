using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>Shared logic for SINTERSTORE, SUNIONSTORE and SDIFFSTORE.</summary>
internal abstract class SetCombineStoreHandlerBase(Keyspace keyspace, SetOperation operation) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(
            keyspace.SetCombineAndStore(operation, command[0], [.. command.Arguments.Skip(1)])));
}
