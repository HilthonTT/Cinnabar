namespace Cinnabar.Client.PubSub;

/// <summary>A message received on a subscribed channel.</summary>
/// <param name="Channel">The channel it was published to.</param>
/// <param name="Message">The payload.</param>
public sealed record RedisMessage(string Channel, string Message);
