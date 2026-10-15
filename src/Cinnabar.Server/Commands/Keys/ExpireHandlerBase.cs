using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Keys;

/// <summary>
/// Shared logic for EXPIRE, PEXPIRE, EXPIREAT and PEXPIREAT. Everything is converted to an
/// absolute Unix time in milliseconds, which is also what gets replicated.
/// </summary>
internal abstract class ExpireHandlerBase(Keyspace keyspace, TimeProvider timeProvider) : ICommandHandler
{
    /// <summary>
    /// Converts the command's time argument to an absolute Unix time in milliseconds. Uses
    /// <see cref="Int128"/> so huge arguments are detected instead of overflowing.
    /// </summary>
    protected abstract Int128 ToAbsoluteMilliseconds(long value, long nowMs);

    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        string key = command[0];
        long nowMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        Int128 absolute = ToAbsoluteMilliseconds(Arguments.ParseLong(command[1]), nowMs);

        if (absolute > long.MaxValue || absolute < long.MinValue)
        {
            return ValueTask.FromResult(Reply.Error($"ERR invalid expire time in '{command.Name.ToLowerInvariant()}' command"));
        }

        long expiresAt = (long)absolute;
        if (!keyspace.ExpireAt(key, expiresAt))
        {
            return ValueTask.FromResult(Reply.Zero.WithoutPropagation());
        }

        return ValueTask.FromResult(Reply.One.WithPropagation(new Command("PEXPIREAT", key, Arguments.Format(expiresAt))));
    }
}
