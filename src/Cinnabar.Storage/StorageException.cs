namespace Cinnabar.Storage;

/// <summary>
/// A storage operation was rejected. <see cref="Exception.Message"/> is already a
/// Redis-style error (for example <c>ERR value is not an integer or out of range</c>)
/// and can be sent to the client as-is.
/// </summary>
public class StorageException(string message) : Exception(message);
