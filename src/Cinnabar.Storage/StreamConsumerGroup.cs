namespace Cinnabar.Storage;

/// <summary>
/// A consumer group on a stream: the last entry it delivered, and the entries delivered but not
/// yet acknowledged (the pending entries list, PEL). Not thread-safe; <see cref="Keyspace"/>
/// guards it with the stream key's lock.
/// </summary>
internal sealed class StreamConsumerGroup(StreamId lastDeliveredId)
{
    /// <summary>New entries are those after this ID (<c>XREADGROUP ... &gt;</c>).</summary>
    public StreamId LastDeliveredId { get; set; } = lastDeliveredId;

    /// <summary>Delivered, unacknowledged entries, by ID.</summary>
    public SortedDictionary<StreamId, PendingEntry> Pending { get; } = [];

    /// <summary>Consumers by name.</summary>
    public Dictionary<string, StreamConsumer> Consumers { get; } = new(StringComparer.Ordinal);

    /// <summary>Returns the consumer, creating it if needed, and marks it as seen.</summary>
    public StreamConsumer Consumer(string name, long nowMs)
    {
        if (!Consumers.TryGetValue(name, out StreamConsumer? consumer))
        {
            consumer = new StreamConsumer();
            Consumers[name] = consumer;
        }

        consumer.SeenTimeMs = nowMs;
        return consumer;
    }

    /// <summary>Records <paramref name="id"/> as delivered to <paramref name="consumer"/>, moving it from any previous owner.</summary>
    public PendingEntry Assign(StreamId id, string consumer, long deliveryTimeMs)
    {
        if (Pending.TryGetValue(id, out PendingEntry? entry))
        {
            if (entry.Consumer != consumer)
            {
                Consumers[entry.Consumer].Pending.Remove(id);
                entry.Consumer = consumer;
            }
        }
        else
        {
            entry = new PendingEntry(consumer);
            Pending[id] = entry;
        }

        Consumers[consumer].Pending.Add(id);
        entry.DeliveryTimeMs = deliveryTimeMs;
        return entry;
    }

    /// <summary>Removes <paramref name="id"/> from the PEL. Returns <see langword="false"/> if it was not pending.</summary>
    public bool Acknowledge(StreamId id)
    {
        if (!Pending.Remove(id, out PendingEntry? entry))
        {
            return false;
        }

        Consumers[entry.Consumer].Pending.Remove(id);
        return true;
    }

    /// <summary>One PEL entry.</summary>
    internal sealed class PendingEntry(string consumer)
    {
        public string Consumer { get; set; } = consumer;

        public long DeliveryTimeMs { get; set; }

        public long DeliveryCount { get; set; }
    }
}
