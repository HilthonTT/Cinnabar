using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// <c>XACK key group id [id ...]</c>: acknowledges entries, removing them from the group's
/// pending list. Returns how many were pending.
/// </summary>
[Command("XACK", MinArgs = 3, Flags = CommandFlags.Write, Group = "stream", Syntax = "key group id [id ...]")]
internal sealed class XAckHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        StreamId[] ids = [.. command.Arguments.Skip(2).Select(StreamClaims.ParseId)];
        long acknowledged = keyspace.StreamAck(command[0], command[1], ids);
        return ValueTask.FromResult(acknowledged > 0 ? Reply.Integer(acknowledged) : Reply.Zero.WithoutPropagation());
    }
}
