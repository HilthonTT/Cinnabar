using Cinnabar.Server.Commands.Keys;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Sets;

/// <summary>
/// <c>SSCAN key cursor [MATCH pattern] [COUNT count]</c>: iterates a set's members. See SCAN.
/// </summary>
[Command("SSCAN", MinArgs = 2, Group = "set", Syntax = "key cursor [MATCH pattern] [COUNT count]")]
internal sealed class SScanHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        ulong cursor = Arguments.ParseCursor(command[1]);
        ScanOptions options = ScanOptions.Parse(command, 2, allowType: false);

        ScanPage<string> page = keyspace.SetScan(command[0], cursor, options.Count, options.Pattern);
        return ValueTask.FromResult(ScanOptions.Page(page.Cursor, page.Items));
    }
}
