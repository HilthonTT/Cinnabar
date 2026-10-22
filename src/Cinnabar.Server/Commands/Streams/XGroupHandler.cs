using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>
/// <c>XGROUP CREATE | SETID | DESTROY | CREATECONSUMER | DELCONSUMER ...</c>: manages consumer
/// groups.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>CREATE key group id|$ [MKSTREAM]</c>: OK, or BUSYGROUP if it exists.</item>
///   <item><c>SETID key group id|$</c>: moves the group's last delivered ID.</item>
///   <item><c>DESTROY key group</c>: 1 if the group existed.</item>
///   <item><c>CREATECONSUMER key group consumer</c>: 1 if the consumer was created.</item>
///   <item><c>DELCONSUMER key group consumer</c>: the number of pending entries it had.</item>
/// </list>
/// <c>ENTRIESREAD</c> is accepted and ignored. <c>$</c> is replicated as the ID it resolved to.
/// </remarks>
[Command("XGROUP", MinArgs = 1, Flags = CommandFlags.Write, Group = "stream",
    Syntax = "CREATE key group id|$ [MKSTREAM] | SETID key group id|$ | DESTROY key group | CREATECONSUMER key group consumer | DELCONSUMER key group consumer")]
internal sealed class XGroupHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Execute(command));

    private Reply Execute(Command command)
    {
        string subcommand = command[0].ToUpperInvariant();
        switch (subcommand)
        {
            case "CREATE":
            {
                RequireArity(command, subcommand, 4, 7);
                bool makeStream = false;
                for (int i = 4; i < command.Count; i++)
                {
                    if (Arguments.Is(command[i], "MKSTREAM"))
                    {
                        makeStream = true;
                    }
                    else if (Arguments.Is(command[i], "ENTRIESREAD") && i + 1 < command.Count)
                    {
                        Arguments.ParseLong(command[++i]);
                    }
                    else
                    {
                        return Reply.Error(Arguments.SyntaxError);
                    }
                }

                StreamId start = keyspace.StreamGroupCreate(command[1], command[2], command[3], makeStream);
                Command propagated = makeStream
                    ? new Command("XGROUP", "CREATE", command[1], command[2], start.ToString(), "MKSTREAM")
                    : new Command("XGROUP", "CREATE", command[1], command[2], start.ToString());
                return Reply.Ok.WithPropagation(propagated);
            }

            case "SETID":
            {
                RequireArity(command, subcommand, 4, 6);
                if (command.Count > 4 && !(command.Count == 6 && Arguments.Is(command[4], "ENTRIESREAD")))
                {
                    return Reply.Error(Arguments.SyntaxError);
                }

                StreamId id = keyspace.StreamGroupSetId(command[1], command[2], command[3]);
                return Reply.Ok.WithPropagation(new Command("XGROUP", "SETID", command[1], command[2], id.ToString()));
            }

            case "DESTROY":
                RequireArity(command, subcommand, 3, 3);
                return keyspace.StreamGroupDestroy(command[1], command[2]) ? Reply.One : Reply.Zero.WithoutPropagation();

            case "CREATECONSUMER":
                RequireArity(command, subcommand, 4, 4);
                return keyspace.StreamGroupCreateConsumer(command[1], command[2], command[3])
                    ? Reply.One
                    : Reply.Zero.WithoutPropagation();

            case "DELCONSUMER":
                RequireArity(command, subcommand, 4, 4);
                return Reply.Integer(keyspace.StreamGroupDeleteConsumer(command[1], command[2], command[3]));

            default:
                return Reply.Error($"ERR unknown subcommand '{command[0]}'. Try XGROUP HELP.");
        }
    }

    private static void RequireArity(Command command, string subcommand, int min, int max)
    {
        if (command.Count < min || command.Count > max)
        {
            throw new StorageException($"ERR wrong number of arguments for 'xgroup|{subcommand.ToLowerInvariant()}' command");
        }
    }
}
