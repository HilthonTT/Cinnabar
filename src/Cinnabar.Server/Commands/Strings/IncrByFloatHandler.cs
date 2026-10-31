using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary>
/// <c>INCRBYFLOAT key increment</c>: adds to a floating-point value (a missing key starts at
/// 0) and returns the new value. The TTL is kept.
/// </summary>
/// <remarks>
/// Replicated as <c>SET key result KEEPTTL</c>, as Redis does, so replicas store exactly the
/// master's value rather than repeating the floating-point arithmetic.
/// </remarks>
[Command("INCRBYFLOAT", MinArgs = 2, MaxArgs = 2, Flags = CommandFlags.Write, Group = "string", Syntax = "key increment")]
internal sealed class IncrByFloatHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        double increment = Arguments.ParseDouble(command[1]);
        if (double.IsInfinity(increment))
        {
            throw new StorageException("ERR increment would produce NaN or Infinity");
        }

        string result = keyspace.IncrementByFloat(command[0], increment);
        return ValueTask.FromResult(Reply.BulkString(result).WithPropagation(new Command("SET", command[0], result, "KEEPTTL")));
    }
}
