namespace Cinnabar.Storage;

/// <summary>One page of a <c>SCAN</c>-family iteration.</summary>
/// <param name="Cursor">The cursor for the next call; 0 when the iteration is complete.</param>
/// <param name="Items">The items in this page (possibly none, even before the end).</param>
public sealed record ScanPage<T>(ulong Cursor, IReadOnlyList<T> Items);
