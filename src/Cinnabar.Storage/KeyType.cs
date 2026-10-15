namespace Cinnabar.Storage;

/// <summary>The data type held by a key, as reported by the <c>TYPE</c> command.</summary>
public enum KeyType
{
    /// <summary>The key does not exist.</summary>
    None,

    /// <summary>A string value.</summary>
    String,

    /// <summary>A list of strings.</summary>
    List,

    /// <summary>An append-only stream of entries.</summary>
    Stream,
}
