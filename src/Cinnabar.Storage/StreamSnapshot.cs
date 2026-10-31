namespace Cinnabar.Storage;

/// <summary>
/// A point-in-time copy of a stream key: its entries and its consumer groups. This is the
/// <see cref="KeySnapshot.Value"/> of a <see cref="KeyType.Stream"/> key.
/// </summary>
/// <param name="Entries">The entries, in increasing ID order.</param>
/// <param name="Groups">The consumer groups, with their pending entries and consumers.</param>
public sealed record StreamSnapshot(IReadOnlyList<StreamEntry> Entries, IReadOnlyList<StreamGroupSnapshot> Groups)
{
    /// <summary>
    /// Reads a stream snapshot value: a <see cref="StreamSnapshot"/>, or (for callers written
    /// before consumer groups were persisted) a plain sequence of <see cref="StreamEntry"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is neither.</exception>
    public static StreamSnapshot From(object value) => value switch
    {
        StreamSnapshot snapshot => snapshot,
        IEnumerable<StreamEntry> entries => new StreamSnapshot([.. entries], []),
        _ => throw new ArgumentException($"A stream snapshot cannot be built from {value.GetType().Name}.", nameof(value)),
    };
}
