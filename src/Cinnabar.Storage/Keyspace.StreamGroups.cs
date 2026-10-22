namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    // ───────────────────────────── Stream consumer groups ─────────────────────────────

    /// <summary>
    /// Creates a consumer group (<c>XGROUP CREATE key group id [MKSTREAM]</c>). The group
    /// delivers entries after <paramref name="id"/>; <c>$</c> means "after the current last entry".
    /// </summary>
    /// <returns>The resolved starting ID.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">
    /// The key does not exist (and <paramref name="makeStream"/> is not set), the ID is invalid,
    /// or the group already exists (<c>BUSYGROUP</c>).
    /// </exception>
    public StreamId StreamGroupCreate(string key, string group, string id, bool makeStream = false)
    {
        lock (StripeFor(key))
        {
            StreamLog? log = GetLive<StreamLog>(key);
            if (log is null && !makeStream)
            {
                throw RequiresKey();
            }

            StreamLog target = log ?? new StreamLog();
            StreamId start = ResolveGroupId(target, id);
            if (!target.Groups.TryAdd(group, new StreamConsumerGroup(start)))
            {
                throw new StorageException("BUSYGROUP Consumer Group name already exists");
            }

            if (log is null)
            {
                Store(key, target, NoExpiry);
            }
            else
            {
                Touch(key);
            }

            return start;
        }
    }

    /// <summary>Moves a group's last delivered ID (<c>XGROUP SETID key group id</c>).</summary>
    /// <returns>The resolved ID.</returns>
    /// <exception cref="StorageException">The key or group does not exist, or the ID is invalid.</exception>
    public StreamId StreamGroupSetId(string key, string group, string id)
    {
        lock (StripeFor(key))
        {
            (StreamLog log, StreamConsumerGroup consumerGroup) = RequireGroup(key, group, NoGroupForKey(key, group));
            consumerGroup.LastDeliveredId = ResolveGroupId(log, id);
            Touch(key);
            return consumerGroup.LastDeliveredId;
        }
    }

    /// <summary>Deletes a group and its pending entries (<c>XGROUP DESTROY</c>).</summary>
    /// <returns><see langword="true"/> if the group existed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">The key does not exist.</exception>
    public bool StreamGroupDestroy(string key, string group)
    {
        lock (StripeFor(key))
        {
            StreamLog log = GetLive<StreamLog>(key) ?? throw RequiresKey();
            if (!log.Groups.Remove(group))
            {
                return false;
            }

            Touch(key);
            return true;
        }
    }

    /// <summary>Adds a consumer to a group (<c>XGROUP CREATECONSUMER</c>).</summary>
    /// <returns><see langword="true"/> if it was created, <see langword="false"/> if it existed.</returns>
    /// <exception cref="StorageException">The key or group does not exist.</exception>
    public bool StreamGroupCreateConsumer(string key, string group, string consumer)
    {
        lock (StripeFor(key))
        {
            (_, StreamConsumerGroup consumerGroup) = RequireGroup(key, group, NoGroupForKey(key, group));
            if (consumerGroup.Consumers.ContainsKey(consumer))
            {
                return false;
            }

            consumerGroup.Consumer(consumer, NowMs);
            Touch(key);
            return true;
        }
    }

    /// <summary>
    /// Removes a consumer and its pending entries (<c>XGROUP DELCONSUMER</c>).
    /// </summary>
    /// <returns>How many pending entries the consumer had.</returns>
    /// <exception cref="StorageException">The key or group does not exist.</exception>
    public long StreamGroupDeleteConsumer(string key, string group, string consumer)
    {
        lock (StripeFor(key))
        {
            (_, StreamConsumerGroup consumerGroup) = RequireGroup(key, group, NoGroupForKey(key, group));
            if (!consumerGroup.Consumers.Remove(consumer, out StreamConsumer? removed))
            {
                return 0;
            }

            foreach (StreamId id in removed.Pending)
            {
                consumerGroup.Pending.Remove(id);
            }

            Touch(key);
            return removed.Pending.Count;
        }
    }

    /// <summary>
    /// Reads as a group consumer (<c>XREADGROUP</c>). With <paramref name="after"/> null
    /// (<c>&gt;</c>), delivers entries no consumer has seen, adds them to the consumer's pending
    /// list (unless <paramref name="noAck"/>) and advances the group. With an ID, re-delivers the
    /// consumer's own pending entries after that ID.
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">The key or group does not exist (<c>NOGROUP</c>).</exception>
    public IReadOnlyList<StreamEntry> StreamReadGroup(
        string key,
        string group,
        string consumer,
        StreamId? after,
        int? count = null,
        bool noAck = false)
    {
        lock (StripeFor(key))
        {
            (StreamLog log, StreamConsumerGroup consumerGroup) = RequireGroup(
                key, group, $"NOGROUP No such key '{key}' or consumer group '{group}' in XREADGROUP with GROUP option");
            long now = NowMs;
            bool newConsumer = !consumerGroup.Consumers.ContainsKey(consumer);
            StreamConsumer reader = consumerGroup.Consumer(consumer, now);

            List<StreamEntry> entries;
            if (after is null)
            {
                entries = log.ReadAfter(consumerGroup.LastDeliveredId, count);
                if (entries.Count > 0)
                {
                    consumerGroup.LastDeliveredId = entries[^1].Id;
                }

                if (!noAck)
                {
                    foreach (StreamEntry entry in entries)
                    {
                        consumerGroup.Assign(entry.Id, consumer, now).DeliveryCount = 1;
                    }
                }
            }
            else
            {
                entries = [];
                if (after.Value.Next() is { } from)
                {
                    foreach (StreamId id in reader.Pending.GetViewBetween(from, StreamId.Max))
                    {
                        if (count is { } limit && entries.Count >= limit)
                        {
                            break;
                        }

                        if (log.TryGet(id, out StreamEntry entry))
                        {
                            entries.Add(entry);
                            StreamConsumerGroup.PendingEntry pending = consumerGroup.Pending[id];
                            pending.DeliveryTimeMs = now;
                            pending.DeliveryCount++;
                        }
                    }
                }
            }

            if (entries.Count > 0 || newConsumer)
            {
                Touch(key);
            }

            return entries;
        }
    }

    /// <summary>Whether the key holds a stream with this consumer group.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public bool StreamGroupExists(string key, string group)
    {
        lock (StripeFor(key))
        {
            return GetLive<StreamLog>(key)?.Groups.ContainsKey(group) ?? false;
        }
    }

    /// <summary>Acknowledges entries (<c>XACK</c>), removing them from the pending list.</summary>
    /// <returns>How many were pending. A missing key or group acknowledges nothing.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long StreamAck(string key, string group, IEnumerable<StreamId> ids)
    {
        lock (StripeFor(key))
        {
            StreamConsumerGroup? consumerGroup = GetLive<StreamLog>(key)?.Groups.GetValueOrDefault(group);
            if (consumerGroup is null)
            {
                return 0;
            }

            long acknowledged = ids.Count(consumerGroup.Acknowledge);
            if (acknowledged > 0)
            {
                Touch(key);
            }

            return acknowledged;
        }
    }

    /// <summary>The summary form of <c>XPENDING key group</c>.</summary>
    /// <exception cref="StorageException">The key or group does not exist.</exception>
    public StreamPendingSummary StreamPending(string key, string group)
    {
        lock (StripeFor(key))
        {
            (_, StreamConsumerGroup consumerGroup) = RequireGroup(key, group, NoGroupForKey(key, group));
            SortedDictionary<StreamId, StreamConsumerGroup.PendingEntry> pending = consumerGroup.Pending;
            if (pending.Count == 0)
            {
                return new StreamPendingSummary(0, null, null, []);
            }

            KeyValuePair<string, long>[] consumers = consumerGroup.Consumers
                .Where(c => c.Value.Pending.Count > 0)
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new KeyValuePair<string, long>(c.Key, c.Value.Pending.Count))
                .ToArray();

            return new StreamPendingSummary(pending.Count, pending.Keys.First(), pending.Keys.Last(), consumers);
        }
    }

    /// <summary>
    /// The extended form of <c>XPENDING key group [IDLE min-idle] start end count [consumer]</c>.
    /// </summary>
    /// <exception cref="StorageException">The key or group does not exist.</exception>
    public IReadOnlyList<StreamPendingEntry> StreamPendingRange(
        string key,
        string group,
        StreamId start,
        StreamId end,
        int count,
        string? consumer = null,
        long minIdleMilliseconds = 0)
    {
        lock (StripeFor(key))
        {
            (_, StreamConsumerGroup consumerGroup) = RequireGroup(key, group, NoGroupForKey(key, group));
            long now = NowMs;
            var result = new List<StreamPendingEntry>();
            if (start.CompareTo(end) > 0)
            {
                return result;
            }

            foreach ((StreamId id, StreamConsumerGroup.PendingEntry entry) in consumerGroup.Pending)
            {
                if (result.Count >= count || id.CompareTo(end) > 0)
                {
                    break;
                }

                long idle = Math.Max(now - entry.DeliveryTimeMs, 0);
                if (id.CompareTo(start) >= 0 &&
                    (consumer is null || entry.Consumer == consumer) &&
                    idle >= minIdleMilliseconds)
                {
                    result.Add(new StreamPendingEntry(id, entry.Consumer, idle, entry.DeliveryCount));
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Takes pending entries over for <paramref name="consumer"/> if they have been idle for at
    /// least <paramref name="minIdleMilliseconds"/> (<c>XCLAIM</c>).
    /// </summary>
    /// <exception cref="StorageException">The key or group does not exist.</exception>
    public StreamClaimResult StreamClaim(
        string key,
        string group,
        string consumer,
        long minIdleMilliseconds,
        IReadOnlyList<StreamId> ids,
        StreamClaimOptions? options = null)
    {
        options ??= StreamClaimOptions.Default;

        lock (StripeFor(key))
        {
            (StreamLog log, StreamConsumerGroup consumerGroup) = RequireGroup(key, group, NoGroupForKey(key, group));
            long now = NowMs;
            long deliveryTime = options.TimeMilliseconds ?? now - (options.IdleMilliseconds ?? 0);

            if (options.LastId is { } lastId && lastId.CompareTo(consumerGroup.LastDeliveredId) > 0)
            {
                consumerGroup.LastDeliveredId = lastId;
            }

            consumerGroup.Consumer(consumer, now);
            var claimed = new List<StreamEntry>();
            foreach (StreamId id in ids)
            {
                if (TryClaim(log, consumerGroup, id, consumer, now, minIdleMilliseconds, deliveryTime, options) is { } entry)
                {
                    claimed.Add(entry);
                }
            }

            // Claiming also updates the consumer's seen time, so always counts as a change.
            Touch(key);
            return new StreamClaimResult(claimed, deliveryTime, StreamId.Min, consumerGroup.LastDeliveredId);
        }
    }

    /// <summary>
    /// Scans the pending list from <paramref name="start"/> and claims up to
    /// <paramref name="count"/> entries idle for at least <paramref name="minIdleMilliseconds"/>
    /// (<c>XAUTOCLAIM</c>). <see cref="StreamClaimResult.NextCursor"/> is where to continue.
    /// </summary>
    /// <exception cref="StorageException">The key or group does not exist.</exception>
    public StreamClaimResult StreamAutoClaim(
        string key,
        string group,
        string consumer,
        long minIdleMilliseconds,
        StreamId start,
        int count,
        bool justId = false)
    {
        lock (StripeFor(key))
        {
            (StreamLog log, StreamConsumerGroup consumerGroup) = RequireGroup(key, group, NoGroupForKey(key, group));
            long now = NowMs;
            consumerGroup.Consumer(consumer, now);
            var options = new StreamClaimOptions(JustId: justId);

            // Like Redis, look at no more than count × 10 pending entries per call.
            int budget = checked(count * 10);
            var claimed = new List<StreamEntry>();
            StreamId next = StreamId.Min;
            foreach (StreamId id in consumerGroup.Pending.Keys.Where(id => id.CompareTo(start) >= 0).ToList())
            {
                if (claimed.Count >= count || budget-- == 0)
                {
                    next = id;
                    break;
                }

                if (TryClaim(log, consumerGroup, id, consumer, now, minIdleMilliseconds, now, options) is { } entry)
                {
                    claimed.Add(entry);
                }
            }

            Touch(key);
            return new StreamClaimResult(claimed, now, next, consumerGroup.LastDeliveredId);
        }
    }

    /// <summary>Claims one ID if the rules allow it. Caller holds the key's stripe.</summary>
    private static StreamEntry? TryClaim(
        StreamLog log,
        StreamConsumerGroup consumerGroup,
        StreamId id,
        string consumer,
        long now,
        long minIdleMilliseconds,
        long deliveryTime,
        StreamClaimOptions options)
    {
        if (!log.TryGet(id, out StreamEntry entry))
        {
            return null;
        }

        bool pending = consumerGroup.Pending.TryGetValue(id, out StreamConsumerGroup.PendingEntry? existing);
        if (!pending && !options.Force)
        {
            return null;
        }

        if (pending && minIdleMilliseconds > 0 && now - existing!.DeliveryTimeMs < minIdleMilliseconds)
        {
            return null;
        }

        StreamConsumerGroup.PendingEntry claimed = consumerGroup.Assign(id, consumer, deliveryTime);
        if (options.RetryCount is { } retries)
        {
            claimed.DeliveryCount = retries;
        }
        else if (!options.JustId)
        {
            claimed.DeliveryCount++;
        }

        return entry;
    }

    /// <summary>Returns the stream and group, or throws <paramref name="noGroupMessage"/>. Caller holds the stripe.</summary>
    private (StreamLog Log, StreamConsumerGroup Group) RequireGroup(string key, string group, string noGroupMessage)
    {
        StreamLog? log = GetLive<StreamLog>(key);
        return log is not null && log.Groups.TryGetValue(group, out StreamConsumerGroup? consumerGroup)
            ? (log, consumerGroup)
            : throw new StorageException(noGroupMessage);
    }

    private static string NoGroupForKey(string key, string group) =>
        $"NOGROUP No such key '{key}' or consumer group '{group}'";

    private static StorageException RequiresKey() =>
        new("ERR The XGROUP subcommand requires the key to exist. Note that for CREATE you may want to use the MKSTREAM option to create an empty stream automatically.");

    /// <summary>Resolves <c>$</c> or an explicit ID for XGROUP CREATE/SETID.</summary>
    private static StreamId ResolveGroupId(StreamLog log, string id)
    {
        if (id == "$")
        {
            return log.LastId;
        }

        return StreamId.TryParse(id, 0, out StreamId parsed)
            ? parsed
            : throw new StorageException("ERR Invalid stream ID specified as stream command argument");
    }
}
