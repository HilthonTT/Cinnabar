using Cinnabar.Protocol;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.PubSub;

/// <summary>
/// Publish/subscribe by channel name and by glob pattern. Every subscriber of a channel
/// receives every message published to it (fan-out) as a <c>["message", channel, payload]</c>
/// push, and every session subscribed to a matching pattern receives it as
/// <c>["pmessage", pattern, channel, payload]</c>.
/// </summary>
/// <remarks>
/// A session subscribed to a channel and to a pattern matching it receives the message twice,
/// once per subscription, as in Redis. Patterns use the same glob syntax as <c>KEYS</c>.
/// </remarks>
internal sealed class PubSubBroker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<ClientSession>> _channels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<ClientSession>> _patterns = new(StringComparer.Ordinal);

    /// <summary>Subscribes <paramref name="session"/> to <paramref name="channel"/>.</summary>
    /// <returns>How many channels and patterns the session is now subscribed to.</returns>
    public int Subscribe(ClientSession session, string channel)
    {
        lock (_lock)
        {
            Add(_channels, channel, session);
            session.Channels.Add(channel);
            return session.SubscriptionCount;
        }
    }

    /// <summary>Unsubscribes <paramref name="session"/> from <paramref name="channel"/>.</summary>
    /// <returns>How many channels and patterns the session is still subscribed to.</returns>
    public int Unsubscribe(ClientSession session, string channel)
    {
        lock (_lock)
        {
            Remove(_channels, channel, session);
            session.Channels.Remove(channel);
            return session.SubscriptionCount;
        }
    }

    /// <summary>Removes every channel subscription of <paramref name="session"/> (UNSUBSCRIBE without arguments, or disconnect).</summary>
    /// <returns>The channels that were unsubscribed, in name order.</returns>
    public IReadOnlyList<string> UnsubscribeAll(ClientSession session)
    {
        lock (_lock)
        {
            string[] channels = [.. session.Channels.Order(StringComparer.Ordinal)];
            foreach (string channel in channels)
            {
                Remove(_channels, channel, session);
            }

            session.Channels.Clear();
            return channels;
        }
    }

    /// <summary>Subscribes <paramref name="session"/> to every channel matching <paramref name="pattern"/>.</summary>
    /// <returns>How many channels and patterns the session is now subscribed to.</returns>
    public int PatternSubscribe(ClientSession session, string pattern)
    {
        lock (_lock)
        {
            Add(_patterns, pattern, session);
            session.Patterns.Add(pattern);
            return session.SubscriptionCount;
        }
    }

    /// <summary>Removes the pattern subscription <paramref name="pattern"/> (matched literally, not as a glob).</summary>
    /// <returns>How many channels and patterns the session is still subscribed to.</returns>
    public int PatternUnsubscribe(ClientSession session, string pattern)
    {
        lock (_lock)
        {
            Remove(_patterns, pattern, session);
            session.Patterns.Remove(pattern);
            return session.SubscriptionCount;
        }
    }

    /// <summary>Removes every pattern subscription of <paramref name="session"/> (PUNSUBSCRIBE without arguments, or disconnect).</summary>
    /// <returns>The patterns that were unsubscribed, in ordinal order.</returns>
    public IReadOnlyList<string> PatternUnsubscribeAll(ClientSession session)
    {
        lock (_lock)
        {
            string[] patterns = [.. session.Patterns.Order(StringComparer.Ordinal)];
            foreach (string pattern in patterns)
            {
                Remove(_patterns, pattern, session);
            }

            session.Patterns.Clear();
            return patterns;
        }
    }

    /// <summary>
    /// Delivers <paramref name="message"/> to every subscriber of <paramref name="channel"/> and
    /// to every pattern subscription that matches it.
    /// </summary>
    /// <returns>The number of deliveries (a session can receive a message more than once).</returns>
    public int Publish(string channel, string message)
    {
        ClientSession[] subscribers;
        var patternMatches = new List<(string Pattern, ClientSession[] Sessions)>();
        lock (_lock)
        {
            subscribers = _channels.TryGetValue(channel, out HashSet<ClientSession>? set) ? [.. set] : [];
            foreach ((string pattern, HashSet<ClientSession> sessions) in _patterns)
            {
                if (KeyPattern.IsMatch(pattern, channel))
                {
                    patternMatches.Add((pattern, [.. sessions]));
                }
            }
        }

        int delivered = 0;
        if (subscribers.Length > 0)
        {
            byte[] payload = RespEncoder.BulkStringArray(["message", channel, message], ByteString.Encoding);
            delivered += subscribers.Count(subscriber => subscriber.TryPush(payload));
        }

        foreach ((string pattern, ClientSession[] sessions) in patternMatches)
        {
            byte[] payload = RespEncoder.BulkStringArray(["pmessage", pattern, channel, message], ByteString.Encoding);
            delivered += sessions.Count(subscriber => subscriber.TryPush(payload));
        }

        return delivered;
    }

    /// <summary>Number of channels with at least one subscriber.</summary>
    public int ChannelCount
    {
        get
        {
            lock (_lock)
            {
                return _channels.Count;
            }
        }
    }

    /// <summary>Number of patterns with at least one subscriber (<c>PUBSUB NUMPAT</c>).</summary>
    public int PatternCount
    {
        get
        {
            lock (_lock)
            {
                return _patterns.Count;
            }
        }
    }

    /// <summary>Channels with at least one subscriber, optionally filtered by a glob (<c>PUBSUB CHANNELS</c>), in ordinal order.</summary>
    public IReadOnlyList<string> ActiveChannels(string? pattern = null)
    {
        lock (_lock)
        {
            return [.. _channels.Keys
                .Where(channel => pattern is null || KeyPattern.IsMatch(pattern, channel))
                .Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>Number of channel subscribers (pattern subscriptions not counted) (<c>PUBSUB NUMSUB</c>).</summary>
    public int SubscriberCount(string channel)
    {
        lock (_lock)
        {
            return _channels.TryGetValue(channel, out HashSet<ClientSession>? set) ? set.Count : 0;
        }
    }

    private static void Add(Dictionary<string, HashSet<ClientSession>> map, string name, ClientSession session)
    {
        if (!map.TryGetValue(name, out HashSet<ClientSession>? sessions))
        {
            sessions = [];
            map[name] = sessions;
        }

        sessions.Add(session);
    }

    private static void Remove(Dictionary<string, HashSet<ClientSession>> map, string name, ClientSession session)
    {
        if (map.TryGetValue(name, out HashSet<ClientSession>? sessions) && sessions.Remove(session) && sessions.Count == 0)
        {
            map.Remove(name);
        }
    }
}
