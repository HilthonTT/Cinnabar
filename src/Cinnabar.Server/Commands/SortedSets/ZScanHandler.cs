using Cinnabar.Server.Commands.Keys;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.SortedSets;

/// <summary>
/// <c>ZSCAN key cursor [MATCH pattern] [COUNT count]</c>: iterates a sorted set; each page is
/// <c>[cursor, [member, score, ...]]</c>. See SCAN.
/// </summary>
[Command("ZSCAN", MinArgs = 2, Group = "sorted-set", Syntax = "key cursor [MATCH pattern] [COUNT count]")]
internal sealed class ZScanHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        ulong cursor = Arguments.ParseCursor(command[1]);
        ScanOptions options = ScanOptions.Parse(command, 2, allowType: false);

        ScanPage<ScoredMember> page = keyspace.SortedSetScan(command[0], cursor, options.Count, options.Pattern);
        return ValueTask.FromResult(ScanOptions.Page(
            page.Cursor, page.Items.SelectMany(m => new[] { m.Member, Arguments.Format(m.Score) })));
    }
}
