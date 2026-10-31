namespace Cinnabar.Storage;

/// <summary>A copy of one consumer group, as part of a <see cref="StreamSnapshot"/>.</summary>
/// <param name="Name">The group name.</param>
/// <param name="LastDeliveredId">New entries for the group are those after this ID.</param>
/// <param name="Consumers">The group's consumers.</param>
/// <param name="Pending">The pending entries list (delivered, not yet acknowledged), in ID order.</param>
public sealed record StreamGroupSnapshot(
    string Name,
    StreamId LastDeliveredId,
    IReadOnlyList<StreamConsumerSnapshot> Consumers,
    IReadOnlyList<StreamPendingSnapshot> Pending);
