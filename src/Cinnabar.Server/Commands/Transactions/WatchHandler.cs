using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Transactions;

/// <summary>
/// <c>WATCH key [key ...]</c>: makes the next EXEC fail (reply nil) if any of the keys is
/// modified, deleted or expires before it runs. Watches end at EXEC, DISCARD or UNWATCH.
/// </summary>
/// <remarks>
/// Flagged as a transaction command so that, inside MULTI, it is rejected instead of queued.
/// </remarks>
[Command("WATCH", MinArgs = 1, Flags = CommandFlags.Transaction, Group = "transactions", Syntax = "key [key ...]")]
internal sealed class WatchHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (session.InTransaction)
        {
            return ValueTask.FromResult(Reply.Error("ERR WATCH inside MULTI is not allowed"));
        }

        foreach (string key in command.Arguments)
        {
            // Watching a key twice keeps the first version, as in Redis.
            if (!session.WatchedKeys.ContainsKey(key))
            {
                session.WatchedKeys[key] = keyspace.Watch(key);
            }
        }

        return ValueTask.FromResult(Reply.Ok);
    }
}
