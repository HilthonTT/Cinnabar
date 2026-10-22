using Cinnabar.Server.Commands.Keys;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary>
/// <c>HSCAN key cursor [MATCH pattern] [COUNT count]</c>: iterates a hash's fields; each page
/// is <c>[cursor, [field, value, ...]]</c>. See SCAN.
/// </summary>
[Command("HSCAN", MinArgs = 2, Group = "hash", Syntax = "key cursor [MATCH pattern] [COUNT count]")]
internal sealed class HScanHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        ulong cursor = Arguments.ParseCursor(command[1]);
        ScanOptions options = ScanOptions.Parse(command, 2, allowType: false);

        ScanPage<KeyValuePair<string, string>> page = keyspace.HashScan(command[0], cursor, options.Count, options.Pattern);
        return ValueTask.FromResult(ScanOptions.Page(page.Cursor, page.Items.SelectMany(pair => new[] { pair.Key, pair.Value })));
    }
}
