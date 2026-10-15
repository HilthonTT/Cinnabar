namespace Cinnabar.Storage;

/// <summary>The key exists but holds a different type than the operation needs.</summary>
public sealed class WrongTypeException()
    : StorageException("WRONGTYPE Operation against a key holding the wrong kind of value");
