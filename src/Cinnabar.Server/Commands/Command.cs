using System.Diagnostics.CodeAnalysis;
using Cinnabar.Protocol;

namespace Cinnabar.Server.Commands;

/// <summary>
/// A parsed client command: its name (for example <c>SET</c>) and its arguments.
/// </summary>
internal sealed record Command(string Name, IReadOnlyList<string> Arguments)
{
    /// <summary>Creates a command from a name and arguments.</summary>
    public Command(string name, params string[] arguments)
        : this(name, (IReadOnlyList<string>)arguments)
    {
    }

    /// <summary>Number of arguments, not counting the name.</summary>
    public int Count => Arguments.Count;

    /// <summary>The argument at <paramref name="index"/>.</summary>
    public string this[int index] => Arguments[index];

    /// <summary>
    /// Converts a RESP value into a command. Clients send commands as an array of bulk
    /// strings; integers and simple strings are accepted too.
    /// </summary>
    /// <returns><see langword="false"/> for anything that is not a non-empty array of scalars.</returns>
    public static bool TryCreate(RespValue value, [NotNullWhen(true)] out Command? command)
    {
        command = null;
        if (value.Type != RespType.Array || value.Elements is not { Count: > 0 } elements)
        {
            return false;
        }

        var parts = new string[elements.Count];
        for (int i = 0; i < parts.Length; i++)
        {
            if (elements[i].AsString() is not { } text || elements[i].Type == RespType.Error)
            {
                return false;
            }

            parts[i] = text;
        }

        command = new Command(parts[0], parts[1..]);
        return true;
    }

    /// <summary>Encodes the command the way a client sends it.</summary>
    public byte[] Encode() => RespEncoder.Command([Name, .. Arguments]);

    /// <summary>The command as space-separated text, for logs.</summary>
    public override string ToString() => Arguments.Count == 0 ? Name : $"{Name} {string.Join(' ', Arguments)}";
}
