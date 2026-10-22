using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary>The options shared by SCAN, HSCAN, SSCAN and ZSCAN.</summary>
/// <param name="Count">How many items to examine (<c>COUNT</c>, default 10).</param>
/// <param name="Pattern">Only return items matching this glob (<c>MATCH</c>).</param>
/// <param name="Type">Only return keys of this type (<c>TYPE</c>, SCAN only).</param>
internal sealed record ScanOptions(int Count, string? Pattern, KeyType? Type)
{
    private const int DefaultCount = 10;

    /// <summary>Parses <c>[MATCH pattern] [COUNT count] [TYPE type]</c> starting at <paramref name="start"/>.</summary>
    public static ScanOptions Parse(Command command, int start, bool allowType)
    {
        int count = DefaultCount;
        string? pattern = null;
        KeyType? type = null;

        for (int i = start; i < command.Count; i += 2)
        {
            if (i + 1 >= command.Count)
            {
                throw new StorageException(Arguments.SyntaxError);
            }

            string option = command[i];
            string value = command[i + 1];
            if (Arguments.Is(option, "MATCH"))
            {
                pattern = value == "*" ? null : value;
            }
            else if (Arguments.Is(option, "COUNT"))
            {
                count = Arguments.ParseInt(value);
                if (count < 1)
                {
                    throw new StorageException(Arguments.SyntaxError);
                }
            }
            else if (allowType && Arguments.Is(option, "TYPE"))
            {
                type = KeyTypeNames.TryParse(value, out KeyType parsed)
                    ? parsed
                    : throw new StorageException("ERR unknown type name");
            }
            else
            {
                throw new StorageException(Arguments.SyntaxError);
            }
        }

        return new ScanOptions(count, pattern, type);
    }

    /// <summary>Formats a page as <c>[cursor, [items...]]</c>.</summary>
    public static Reply Page(ulong cursor, IEnumerable<string> items) =>
        Reply.Array([Reply.BulkString(cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)), Reply.BulkStringArray(items)]);
}
