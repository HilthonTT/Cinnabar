using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary><c>XLEN key</c>: number of entries.</summary>
[Command("XLEN", MinArgs = 1, MaxArgs = 1, Group = "stream", Syntax = "key")]
internal sealed class XLenHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Reply.Integer(keyspace.StreamLength(command[0])));
}
