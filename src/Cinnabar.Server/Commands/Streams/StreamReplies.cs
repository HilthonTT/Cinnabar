using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Streams;

/// <summary>Formats stream entries as RESP: <c>[[id, [field, value, ...]], ...]</c>.</summary>
internal static class StreamReplies
{
    public static Reply Entries(IEnumerable<StreamEntry> entries) =>
        Reply.Array(entries.Select(entry => Reply.Array(
        [
            Reply.BulkString(entry.Id.ToString()),
            Reply.BulkStringArray(entry.Fields.SelectMany(pair => new[] { pair.Key, pair.Value })),
        ])));
}
