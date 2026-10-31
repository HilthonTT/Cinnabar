namespace Cinnabar.Storage.Persistence;

/// <summary>Outcome of an RDB write.</summary>
/// <param name="WrittenKeys">Keys written to the file.</param>
/// <param name="SkippedKeys">Keys left out because their type cannot be written (only <see cref="KeyType.None"/>, which a keyspace snapshot never contains).</param>
public sealed record RdbWriteResult(int WrittenKeys, int SkippedKeys);
