namespace Cinnabar.Server.Commands;

/// <summary>Behaviour flags the dispatcher uses to route and validate a command.</summary>
[Flags]
internal enum CommandFlags
{
    /// <summary>A read-only command with no special handling.</summary>
    None = 0,

    /// <summary>
    /// Changes data. Rejected on replicas and propagated from masters to replicas.
    /// </summary>
    Write = 1 << 0,

    /// <summary>
    /// May wait (BLPOP, XREAD BLOCK, WAIT). Blocking writes propagate their own effect,
    /// because the dispatcher must not hold the replication barrier while they wait.
    /// </summary>
    Blocking = 1 << 1,

    /// <summary>Allowed while the connection is in subscribed (pub/sub) mode.</summary>
    PubSub = 1 << 2,

    /// <summary>Allowed before AUTH.</summary>
    NoAuth = 1 << 3,

    /// <summary>Runs immediately inside MULTI instead of being queued (MULTI, EXEC, DISCARD).</summary>
    Transaction = 1 << 4,
}

/// <summary>
/// Declares the command a handler implements. The dispatcher discovers handlers by this attribute.
/// </summary>
/// <param name="name">The command name, matched case-insensitively.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class CommandAttribute(string name) : Attribute
{
    /// <summary>The command name, in upper case.</summary>
    public string Name { get; } = name.ToUpperInvariant();

    /// <summary>Minimum number of arguments (not counting the name).</summary>
    public int MinArgs { get; init; }

    /// <summary>Maximum number of arguments; <c>-1</c> means unlimited.</summary>
    public int MaxArgs { get; init; } = -1;

    /// <summary>Routing and validation flags.</summary>
    public CommandFlags Flags { get; init; }

    /// <summary>The command family shown in documentation and <c>COMMAND</c> output.</summary>
    public string Group { get; init; } = "generic";

    /// <summary>The argument syntax, for example <c>key value [EX seconds]</c>.</summary>
    public string Syntax { get; init; } = "";

    /// <summary>Returns <see langword="true"/> if <paramref name="count"/> arguments are acceptable.</summary>
    public bool AcceptsArgumentCount(int count) => count >= MinArgs && (MaxArgs < 0 || count <= MaxArgs);
}
