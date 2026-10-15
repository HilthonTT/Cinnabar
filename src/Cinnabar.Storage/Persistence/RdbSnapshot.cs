namespace Cinnabar.Storage.Persistence;

/// <summary>The decoded contents of an RDB file.</summary>
public sealed class RdbSnapshot
{
    /// <summary>The RDB format version from the file header.</summary>
    public int Version { get; init; }

    /// <summary>Auxiliary fields such as <c>redis-ver</c>, in file order.</summary>
    public List<KeyValuePair<string, string>> Metadata { get; } = [];

    /// <summary>Keys per database number.</summary>
    public Dictionary<int, List<KeySnapshot>> Databases { get; } = [];
}
