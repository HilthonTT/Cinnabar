using Cinnabar.Protocol;
using Cinnabar.Server.Networking;
using Cinnabar.Storage;

namespace Cinnabar.Server.PubSub;

/// <summary>
/// Channel-based publish/subscribe. Every subscriber of a channel receives every message
/// published to it (fan-out), as a <c>["message", channel, payload]</c> push.
/// </summary>
internal sealed class PubSubBroker
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, HashSet<ClientSession>> _channels = new(StringComparer.Ordinal);

    /// <summary>Subscribes <paramref name="session"/> to <paramref name="channel"/>.</summary>
    /// <returns>How many channels the session is now subscribed to.</returns>
    public int Subscribe(ClientSession session, string channel)
    {
        lock (_lock)
        {
            if (!_channels.TryGetValue(channel, out HashSet<ClientSession>? subscribers))
            {
                subscribers = [];
                _channels[channel] = subscribers;
            }

            subscribers.Add(session);
            session.Channels.Add(channel);
            return session.Channels.Count;
        }
    }

    /// <summary>Unsubscribes <paramref name="session"/> from <paramref name="channel"/>.</summary>
    /// <returns>How many channels the session is still subscribed to.</returns>
    public int Unsubscribe(ClientSession session, string channel)
    {
        lock (_lock)
        {
            if (_channels.TryGetValue(channel, out HashSet<ClientSession>? subscribers) &&
                subscribers.Remove(session) && subscribers.Count == 0)
            {
                _channels.Remove(channel);
            }

            session.Channels.Remove(channel);
            return session.Channels.Count;
        }
    }

    /// <summary>Removes every subscription of <paramref name="session"/> (on UNSUBSCRIBE without arguments or disconnect).</summary>
    /// <returns>The channels that were unsubscribed, in name order.</returns>
    public IReadOnlyList<string> UnsubscribeAll(ClientSession session)
    {
        lock (_lock)
        {
            string[] channels = [.. session.Channels.Order(StringComparer.Ordinal)];
            foreach (string channel in channels)
            {
                if (_channels.TryGetValue(channel, out HashSet<ClientSession>? subscribers) &&
                    subscribers.Remove(session) && subscribers.Count == 0)
                {
                    _channels.Remove(channel);
                }
            }

            session.Channels.Clear();
            return channels;
        }
    }

    /// <summary>Delivers <paramref name="message"/> to every subscriber of <paramref name="channel"/>.</summary>
    /// <returns>The number of subscribers that received it.</returns>
    public int Publish(string channel, string message)
    {
        ClientSession[] subscribers;
        lock (_lock)
        {
            if (!_channels.TryGetValue(channel, out HashSet<ClientSession>? set))
            {
                return 0;
            }

            subscribers = [.. set];
        }

        byte[] payload = RespEncoder.BulkStringArray(["message", channel, message], ByteString.Encoding);
        int delivered = 0;
        foreach (ClientSession subscriber in subscribers)
        {
            if (subscriber.TryPush(payload))
            {
                delivered++;
            }
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
}
