using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary>
/// <c>SCAN cursor [MATCH pattern] [COUNT count] [TYPE type]</c>: iterates the keyspace a page
/// at a time. Start with cursor 0; the iteration is complete when the returned cursor is 0.
/// Keys that exist for the whole iteration are returned exactly once.
/// </summary>
[Command("SCAN", MinArgs = 1, Group = "generic", Syntax = "cursor [MATCH pattern] [COUNT count] [TYPE type]")]
internal sealed class ScanHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        ulong cursor = Arguments.ParseCursor(command[0]);
        ScanOptions options = ScanOptions.Parse(command, 1, allowType: true);

        ScanPage<string> page = keyspace.Scan(cursor, options.Count, options.Pattern, options.Type);
        return ValueTask.FromResult(ScanOptions.Page(page.Cursor, page.Items));
    }
}
