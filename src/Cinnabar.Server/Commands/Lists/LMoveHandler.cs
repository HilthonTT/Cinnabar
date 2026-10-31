using Cinnabar.Server.Blocking;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Lists;

/// <summary>
/// <c>LMOVE source destination LEFT|RIGHT LEFT|RIGHT</c>: atomically pops from one list and
/// pushes onto another (or the same) list; returns the element, or null if the source is empty.
/// </summary>
[Command("LMOVE", MinArgs = 4, MaxArgs = 4, Flags = CommandFlags.Write, Group = "list", Syntax = "source destination LEFT|RIGHT LEFT|RIGHT")]
internal sealed class LMoveHandler(Keyspace keyspace, KeyNotifier notifier) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        ListEnd from = ParseEnd(command[2]);
        ListEnd to = ParseEnd(command[3]);
        string? value = Move(keyspace, notifier, command[0], command[1], from, to);
        return ValueTask.FromResult(value is null
            ? Reply.NullBulkString.WithoutPropagation()
            : Reply.BulkString(value).WithPropagation(AsLMove(command[0], command[1], from, to)));
    }

    /// <summary>Parses <c>LEFT</c> or <c>RIGHT</c>.</summary>
    internal static ListEnd ParseEnd(string text) =>
        Arguments.Is(text, "LEFT") ? ListEnd.Left
        : Arguments.Is(text, "RIGHT") ? ListEnd.Right
        : throw new StorageException(Arguments.SyntaxError);

    /// <summary>Moves one element and wakes clients blocked on the destination.</summary>
    internal static string? Move(Keyspace keyspace, KeyNotifier notifier, string source, string destination, ListEnd from, ListEnd to)
    {
        string? value = keyspace.ListMove(source, destination, from, to);
        if (value is not null)
        {
            notifier.Notify(destination);
        }

        return value;
    }

    /// <summary>The command every list move (LMOVE, RPOPLPUSH and their blocking forms) is replicated as.</summary>
    internal static Command AsLMove(string source, string destination, ListEnd from, ListEnd to) =>
        new("LMOVE", source, destination, Name(from), Name(to));

    private static string Name(ListEnd end) => end == ListEnd.Left ? "LEFT" : "RIGHT";
}
