using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.Commands.Strings;

/// <summary>
/// <c>SET key value [NX | XX] [EX seconds | PX milliseconds | EXAT unix-seconds | PXAT unix-milliseconds | KEEPTTL]</c>
/// </summary>
/// <remarks>
/// Replies OK, or nil when NX/XX prevented the write. SET replaces a value of any type.
/// Expiries are replicated as absolute <c>PXAT</c> times so replicas expire keys at the same moment.
/// </remarks>
[Command("SET", MinArgs = 2, Flags = CommandFlags.Write, Group = "string",
    Syntax = "key value [NX | XX] [EX seconds | PX milliseconds | EXAT unix-time-seconds | PXAT unix-time-milliseconds | KEEPTTL]")]
internal sealed class SetHandler(Keyspace keyspace, TimeProvider timeProvider) : ICommandHandler
{
    private const string InvalidExpireTime = "ERR invalid expire time in 'set' command";

    public ValueTask<Reply> ExecuteAsync(Command command, ClientSession session, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Execute(command));

    private Reply Execute(Command command)
    {
        string key = command[0];
        string value = command[1];
        SetCondition condition = SetCondition.Always;
        long? expiresAtMs = null;
        bool keepTtl = false;
        long nowMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

        for (int i = 2; i < command.Count; i++)
        {
            string option = command[i];
            bool hasValue = i + 1 < command.Count;

            if (Arguments.Is(option, "NX") && condition == SetCondition.Always)
            {
                condition = SetCondition.IfNotExists;
            }
            else if (Arguments.Is(option, "XX") && condition == SetCondition.Always)
            {
                condition = SetCondition.IfExists;
            }
            else if (Arguments.Is(option, "KEEPTTL") && expiresAtMs is null && !keepTtl)
            {
                keepTtl = true;
            }
            else if (hasValue && expiresAtMs is null && !keepTtl &&
                     (Arguments.Is(option, "EX") || Arguments.Is(option, "PX") ||
                      Arguments.Is(option, "EXAT") || Arguments.Is(option, "PXAT")))
            {
                long amount = Arguments.ParseLong(command[++i]);
                if (amount <= 0)
                {
                    return Reply.Error(InvalidExpireTime);
                }

                Int128 absolute = option.ToUpperInvariant() switch
                {
                    "EX" => nowMs + ((Int128)amount * 1000),
                    "PX" => nowMs + (Int128)amount,
                    "EXAT" => (Int128)amount * 1000,
                    _ => amount,
                };

                if (absolute > long.MaxValue)
                {
                    return Reply.Error(InvalidExpireTime);
                }

                expiresAtMs = (long)absolute;
            }
            else
            {
                return Reply.Error(Arguments.SyntaxError);
            }
        }

        if (expiresAtMs is { } at && at <= nowMs)
        {
            // An absolute time in the past: the key is written and immediately gone, like Redis.
            bool exists = keyspace.Exists(key);
            if ((condition == SetCondition.IfNotExists && exists) || (condition == SetCondition.IfExists && !exists))
            {
                return Reply.NullBulkString.WithoutPropagation();
            }

            keyspace.Delete(key);
            return Reply.Ok.WithPropagation(new Command("DEL", key));
        }

        if (!keyspace.SetStringExpiringAt(key, value, expiresAtMs, keepTtl, condition))
        {
            return Reply.NullBulkString.WithoutPropagation();
        }

        Command propagated = expiresAtMs is { } absoluteMs
            ? new Command("SET", key, value, "PXAT", Arguments.Format(absoluteMs))
            : keepTtl ? new Command("SET", key, value, "KEEPTTL") : new Command("SET", key, value);

        return Reply.Ok.WithPropagation(propagated);
    }
}
