using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;

namespace Cinnabar.Server.Commands.Replication;

/// <summary>
/// <c>REPLCONF listening-port port | capa capability | ACK offset</c>: sent by replicas
/// during the handshake and to acknowledge progress. ACK gets no reply.
/// </summary>
[Command("REPLCONF", MinArgs = 2, Group = "replication", Syntax = "option value [option value ...]")]
internal sealed class ReplConfHandler(ReplicationMaster master) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (Arguments.Is(command[0], "ACK"))
        {
            master.Acknowledge(session, Arguments.ParseLong(command[1]));
            return ValueTask.FromResult(Reply.None);
        }

        return ValueTask.FromResult(Reply.Ok);
    }
}
