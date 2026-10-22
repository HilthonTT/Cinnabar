namespace Cinnabar.Storage;

/// <summary>How <see cref="Keyspace.SetCombine"/> combines sets.</summary>
public enum SetOperation
{
    /// <summary>Members in every set (<c>SINTER</c>).</summary>
    Intersect,

    /// <summary>Members in any set (<c>SUNION</c>).</summary>
    Union,

    /// <summary>Members of the first set that are in none of the others (<c>SDIFF</c>).</summary>
    Difference,
}
