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

    /// <summary>
    /// A stream with its consumer groups, in Cinnabar's own encoding. Redis encodes streams as
    /// listpacks in a radix tree (types 15, 19 and 21); Cinnabar uses a simpler layout under a
    /// type number Redis does not use, so Redis refuses such a file rather than misreading it.
    /// </summary>
    public const byte CinnabarStream = 200;

    /// <summary>The version of the <see cref="CinnabarStream"/> layout, written first in each value.</summary>
    public const byte CinnabarStreamVersion = 1;
}
