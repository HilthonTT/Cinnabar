using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Hashes;

/// <summary>
/// <c>HINCRBYFLOAT key field increment</c>: adds to a floating-point field (missing fields
/// start at 0) and returns the new value.
/// </summary>
/// <remarks>
/// Replicated as <c>HSET key field result</c>, so replicas store exactly the master's value
/// rather than repeating the floating-point arithmetic.
/// </remarks>
[Command("HINCRBYFLOAT", MinArgs = 3, MaxArgs = 3, Flags = CommandFlags.Write, Group = "hash", Syntax = "key field increment")]
internal sealed class HIncrByFloatHandler(Keyspace keyspace) : ICommandHandler
{
    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        double increment = Arguments.ParseDouble(command[2]);
        if (double.IsInfinity(increment))
        {
            throw new StorageException("ERR increment would produce NaN or Infinity");
        }

        string result = keyspace.HashIncrementByFloat(command[0], command[1], increment);
        return ValueTask.FromResult(Reply.BulkString(result).WithPropagation(new Command("HSET", command[0], command[1], result)));
    }
}
