namespace Cinnabar.Storage;

/// <summary>A consumer in a <see cref="StreamConsumerGroup"/>.</summary>
internal sealed class StreamConsumer
{
    /// <summary>When the consumer last ran a command, in Unix milliseconds.</summary>
    public long SeenTimeMs { get; set; }

    /// <summary>The IDs pending for this consumer.</summary>
    public SortedSet<StreamId> Pending { get; } = [];
}
