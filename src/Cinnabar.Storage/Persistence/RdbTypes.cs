namespace Cinnabar.Storage.Persistence;

/// <summary>The RDB value-type bytes Cinnabar reads and writes.</summary>
internal static class RdbTypes
{
    public const byte String = 0;
    public const byte List = 1;
    public const byte Set = 2;

    /// <summary>Sorted set with scores as text (written by Redis before RDB version 8).</summary>
    public const byte SortedSet = 3;

    public const byte Hash = 4;

    /// <summary>Sorted set with scores as little-endian binary doubles.</summary>
    public const byte SortedSet2 = 5;
}
