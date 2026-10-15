namespace Cinnabar.Storage;

/// <summary>When <c>SET</c> is allowed to write.</summary>
public enum SetCondition
{
    /// <summary>Always write.</summary>
    Always,

    /// <summary>Only write if the key does not exist (<c>NX</c>).</summary>
    IfNotExists,

    /// <summary>Only write if the key already exists (<c>XX</c>).</summary>
    IfExists,
}
