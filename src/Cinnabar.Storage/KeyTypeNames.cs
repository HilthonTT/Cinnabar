namespace Cinnabar.Storage;

/// <summary>The names Redis uses for key types, as in <c>TYPE</c> and <c>SCAN ... TYPE</c>.</summary>
public static class KeyTypeNames
{
    /// <summary>Returns the Redis name: <c>string</c>, <c>list</c>, <c>zset</c>, <c>none</c>, ...</summary>
    public static string ToRedisName(this KeyType type) => type switch
    {
        KeyType.String => "string",
        KeyType.List => "list",
        KeyType.Stream => "stream",
        KeyType.Hash => "hash",
        KeyType.Set => "set",
        KeyType.SortedSet => "zset",
        _ => "none",
    };

    /// <summary>Parses a Redis type name, ignoring case.</summary>
    public static bool TryParse(string name, out KeyType type)
    {
        foreach (KeyType candidate in Enum.GetValues<KeyType>())
        {
            if (candidate != KeyType.None && string.Equals(candidate.ToRedisName(), name, StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = KeyType.None;
        return false;
    }
}
