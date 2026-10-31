using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary>
/// <c>UNLINK key [key ...]</c>: number of keys removed. The same as DEL here; Redis frees the
/// memory in the background, which the .NET garbage collector does anyway.
/// </summary>
[Command("UNLINK", MinArgs = 1, Flags = CommandFlags.Write | CommandFlags.FreesMemory, Group = "generic", Syntax = "key [key ...]")]
internal sealed class UnlinkHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(command.Arguments.Count(keyspace.Delete) is var removed and > 0
            ? Reply.Integer(removed)
            : Reply.Zero.WithoutPropagation());
}
