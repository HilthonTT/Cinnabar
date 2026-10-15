using Cinnabar.Server.Networking;

namespace Cinnabar.Server.Commands.Server;

/// <summary>
/// <c>COMMAND [COUNT | DOCS | INFO ...]</c>: COUNT returns the number of commands; other
/// forms return the command names. Enough for redis-cli and client libraries that probe it.
/// </summary>
[Command("COMMAND", Group = "server", Syntax = "[COUNT | DOCS | INFO [command ...]]")]
internal sealed class CommandHandler(Lazy<CommandDispatcher> dispatcher) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        List<CommandAttribute> commands = [.. dispatcher.Value.Commands];

        if (command.Count > 0 && Arguments.Is(command[0], "COUNT"))
        {
            return ValueTask.FromResult(Reply.Integer(commands.Count));
        }

        if (command.Count > 0 && Arguments.Is(command[0], "DOCS"))
        {
            // Name → empty doc map; redis-cli only needs the names for hints.
            return ValueTask.FromResult(Reply.Array(commands.SelectMany(c => new[]
            {
                Reply.BulkString(c.Name.ToLowerInvariant()),
                Reply.EmptyArray,
            })));
        }

        return ValueTask.FromResult(Reply.BulkStringArray(commands.Select(c => c.Name.ToLowerInvariant())));
    }
}
