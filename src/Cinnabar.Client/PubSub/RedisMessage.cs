namespace Cinnabar.Client.PubSub;

/// <summary>A message received on a subscribed channel or pattern.</summary>
/// <param name="Channel">The channel it was published to.</param>
/// <param name="Message">The payload.</param>
/// <param name="Pattern">
/// The pattern subscription it matched (<c>PSUBSCRIBE</c>), or <see langword="null"/> for a
/// channel subscription.
/// </param>
public sealed record RedisMessage(string Channel, string Message, string? Pattern = null);
