namespace Cinnabar.Storage;

/// <summary>
/// A storage operation was rejected. <see cref="Exception.Message"/> is already a
/// Redis-style error (for example <c>ERR value is not an integer or out of range</c>)
/// and can be sent to the client as-is.
/// </summary>
public class StorageException(string message) : Exception(message);

/// <summary>The key exists but holds a different type than the operation needs.</summary>
public sealed class WrongTypeException()
    : StorageException("WRONGTYPE Operation against a key holding the wrong kind of value");
