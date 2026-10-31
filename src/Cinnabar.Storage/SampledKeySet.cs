namespace Cinnabar.Storage;

/// <summary>
/// The set of stored keys in a form that supports picking random keys in O(1), which
/// eviction needs and a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
/// cannot do. Thread-safe.
/// </summary>
/// <remarks>
/// Keys live in a dense array; removal swaps the last key into the hole. Callers add a key
/// when it is created and remove it when it is deleted, while holding the key's stripe, so
/// this lock is always taken after a stripe and never the other way round.
/// </remarks>
internal sealed class SampledKeySet
{
    private readonly Lock _lock = new();
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);

    public void Add(string key)
    {
        lock (_lock)
        {
            if (_positions.TryAdd(key, _keys.Count))
            {
                _keys.Add(key);
            }
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            if (!_positions.Remove(key, out int index))
            {
                return;
            }

            int last = _keys.Count - 1;
            if (index != last)
            {
                string moved = _keys[last];
                _keys[index] = moved;
                _positions[moved] = index;
            }

            _keys.RemoveAt(last);
        }
    }

    /// <summary>Up to <paramref name="count"/> random keys (possibly repeated).</summary>
    public string[] Sample(int count)
    {
        lock (_lock)
        {
            if (_keys.Count == 0)
            {
                return [];
            }

            var sample = new string[count];
            for (int i = 0; i < sample.Length; i++)
            {
                sample[i] = _keys[Random.Shared.Next(_keys.Count)];
            }

            return sample;
        }
    }
}
