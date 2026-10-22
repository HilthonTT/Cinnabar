using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using Cinnabar.Server.Concurrency;
using Cinnabar.Server.Configuration;
using Cinnabar.Server.Diagnostics;
using Cinnabar.Server.Networking;
using Cinnabar.Server.Replication;
using Cinnabar.Storage;
using Microsoft.Extensions.Logging;

namespace Cinnabar.Server.Commands;

/// <summary>
/// Routes commands to handlers and applies the rules every command shares.
/// </summary>
/// <remarks>
/// Checks, in order: command exists → authenticated → argument count → allowed in
/// subscribed mode → queue if inside MULTI → replica is read-only. It then runs the handler
/// while holding the <see cref="TransactionGate"/> in shared mode, converts storage errors
/// into replies, propagates successful writes to replicas, and records metrics.
/// </remarks>
internal sealed class CommandDispatcher
{
    private readonly FrozenDictionary<string, RegisteredCommand> _commands;
    private readonly ServerSettings _settings;
    private readonly ReplicationMaster _master;
    private readonly Keyspace _keyspace;
    private readonly TransactionGate _gate;
    private readonly ServerMetrics _metrics;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(
        IEnumerable<ICommandHandler> handlers,
        ServerSettings settings,
        ReplicationMaster master,
        Keyspace keyspace,
        TransactionGate gate,
        ServerMetrics metrics,
        ILogger<CommandDispatcher> logger)
    {
        _settings = settings;
        _master = master;
        _keyspace = keyspace;
        _gate = gate;
        _metrics = metrics;
        _logger = logger;

        var commands = new Dictionary<string, RegisteredCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (ICommandHandler handler in handlers)
        {
            CommandAttribute spec = handler.GetType().GetCustomAttribute<CommandAttribute>()
                ?? throw new InvalidOperationException($"{handler.GetType().Name} is missing [Command].");

            if (!commands.TryAdd(spec.Name, new RegisteredCommand(spec, handler)))
            {
                throw new InvalidOperationException($"Command {spec.Name} is registered twice.");
            }
        }

        _commands = commands.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every registered command, for <c>COMMAND</c> and documentation.</summary>
    public IEnumerable<CommandAttribute> Commands => _commands.Values.Select(c => c.Spec).OrderBy(s => s.Name);

    /// <summary>Validates and runs <paramref name="command"/>.</summary>
    public async ValueTask<Reply> DispatchAsync(Command command, ClientSession session, CancellationToken cancellationToken)
    {
        if (!_commands.TryGetValue(command.Name, out RegisteredCommand? registered))
        {
            return Reject(session, Reply.Error(UnknownCommandMessage(command)));
        }

        CommandAttribute spec = registered.Spec;

        if (!session.IsAuthenticated && !spec.Flags.HasFlag(CommandFlags.NoAuth))
        {
            return Reject(session, Reply.Error("NOAUTH Authentication required."));
        }

        if (!spec.AcceptsArgumentCount(command.Count))
        {
            return Reject(session, Reply.Error($"ERR wrong number of arguments for '{command.Name.ToLowerInvariant()}' command"));
        }

        if (session.InSubscribedMode && !spec.Flags.HasFlag(CommandFlags.PubSub))
        {
            return Reply.Error(
                $"ERR Can't execute '{command.Name.ToLowerInvariant()}': only (P|S)SUBSCRIBE / (P|S)UNSUBSCRIBE / PING / QUIT / RESET are allowed in this context");
        }

        if (session.InTransaction && !spec.Flags.HasFlag(CommandFlags.Transaction))
        {
            session.Enqueue(command);
            return Reply.Queued;
        }

        if (spec.Flags.HasFlag(CommandFlags.Write) && _settings.Replication.IsReplica && !session.IsReplicationLink)
        {
            return Reply.Error("READONLY You can't write against a read only replica.");
        }

        return await ExecuteAsync(registered, command, session, cancellationToken);
    }

    /// <summary>
    /// Runs the commands queued since MULTI and returns their replies as one array.
    /// </summary>
    /// <remarks>
    /// The transaction holds the <see cref="TransactionGate"/> exclusively, so no other
    /// client's command runs until it finishes. If a key watched with WATCH changed since, the
    /// transaction is not run and the reply is the null array. If any command was rejected
    /// while queueing (unknown name, bad arity), nothing runs and <c>EXECABORT</c> is
    /// returned. Either way the connection's watches end, as in Redis.
    /// </remarks>
    public async ValueTask<Reply> ExecuteTransactionAsync(ClientSession session, CancellationToken cancellationToken)
    {
        bool failed = session.TransactionFailed;
        IReadOnlyList<Command> queued = session.EndTransaction();

        if (failed)
        {
            session.UnwatchAll(_keyspace);
            return Reply.Error("EXECABORT Transaction discarded because of previous errors.");
        }

        using (await _gate.EnterExclusiveAsync(cancellationToken))
        {
            bool watchedKeyChanged = session.WatchedKeys.Any(watch => !_keyspace.IsUnchanged(watch.Key, watch.Value));
            session.UnwatchAll(_keyspace);
            if (watchedKeyChanged)
            {
                return Reply.NullArray;
            }

            var replies = new List<Reply>(queued.Count);
            session.IsExecuting = true;
            try
            {
                foreach (Command command in queued)
                {
                    // DispatchAsync re-checks the read-only rule; the command can't be re-queued
                    // because the transaction has already ended.
                    replies.Add(await DispatchAsync(command, session, cancellationToken));
                }
            }
            finally
            {
                session.IsExecuting = false;
            }

            return Reply.Array(replies);
        }
    }

    private async ValueTask<Reply> ExecuteAsync(
        RegisteredCommand registered,
        Command command,
        ClientSession session,
        CancellationToken cancellationToken)
    {
        CommandAttribute spec = registered.Spec;
        bool propagates = spec.Flags.HasFlag(CommandFlags.Write) &&
                          !spec.Flags.HasFlag(CommandFlags.Blocking) &&
                          !_settings.Replication.IsReplica;

        long started = Stopwatch.GetTimestamp();
        Reply reply;

        // Inside EXEC the transaction already holds the gate exclusively. Blocking commands
        // take it themselves, only while they look at data. MULTI/EXEC/DISCARD/WATCH manage
        // transactions rather than data, and EXEC must not hold it in shared mode.
        bool ungated = session.IsExecuting ||
                       (spec.Flags & (CommandFlags.Blocking | CommandFlags.Transaction | CommandFlags.NoGate)) != 0;
        SharedExclusiveGate.Releaser? gate = ungated ? null : await _gate.EnterSharedAsync(cancellationToken);

        // Writes hold the barrier until they are propagated, so a replica snapshot never
        // sees a write without also seeing (or never seeing) its propagation.
        WriteBarrier.Releaser? barrier = null;

        try
        {
            if (propagates)
            {
                barrier = await _master.Barrier.EnterSharedAsync(cancellationToken);
            }

            reply = await InvokeAsync(registered.Handler, command, session, cancellationToken);

            if (propagates && !reply.IsError && !reply.SkipPropagation)
            {
                session.LastWriteOffset = _master.Propagate(reply.PropagateAs ?? command);
            }
        }
        finally
        {
            barrier?.Dispose();
            gate?.Dispose();
        }

        _metrics.RecordCommand(spec.Name, Stopwatch.GetElapsedTime(started), !reply.IsError);
        return reply;
    }

    private async ValueTask<Reply> InvokeAsync(
        ICommandHandler handler,
        Command command,
        ClientSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            return await handler.ExecuteAsync(command, session, cancellationToken);
        }
        catch (StorageException e)
        {
            return Reply.Error(e.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Command {Command} failed.", command.Name);
            return Reply.Error($"ERR {e.Message}");
        }
    }

    /// <summary>Inside MULTI, a rejected command makes EXEC abort.</summary>
    private static Reply Reject(ClientSession session, Reply error)
    {
        if (session.InTransaction)
        {
            session.TransactionFailed = true;
        }

        return error;
    }

    private static string UnknownCommandMessage(Command command)
    {
        // Redis leaves a trailing space after each argument; match it exactly.
        string args = string.Concat(command.Arguments.Take(5).Select(a => $"'{a}' "));
        return $"ERR unknown command '{command.Name}', with args beginning with: {args}";
    }

    private sealed record RegisteredCommand(CommandAttribute Spec, ICommandHandler Handler);
}
