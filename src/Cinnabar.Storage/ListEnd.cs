namespace Cinnabar.Storage;

/// <summary>Which end of a list an operation works on.</summary>
public enum ListEnd
{
    /// <summary>The head (index 0), used by LPUSH and LPOP.</summary>
    Left,

    /// <summary>The tail (index -1), used by RPUSH and RPOP.</summary>
    Right,
}
