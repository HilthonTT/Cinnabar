using System.Globalization;

namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    // ───────────────────────────── Hashes ─────────────────────────────

    /// <summary>
    /// Sets fields of a hash (<c>HSET</c>), creating it if needed. Later pairs win when a field
    /// repeats.
    /// </summary>
    /// <returns>The number of fields that were added (not updated).</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long HashSetFields(string key, IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        lock (StripeFor(key))
        {
            Dictionary<string, string>? existing = GetLive<Dictionary<string, string>>(key);
            Dictionary<string, string> hash = existing ?? new Dictionary<string, string>(StringComparer.Ordinal);

            long added = 0;
            foreach ((string field, string value) in fields)
            {
                if (hash.TryAdd(field, value))
                {
                    added++;
                }
                else
                {
                    hash[field] = value;
                }
            }

            if (existing is null)
            {
                if (hash.Count > 0)
                {
                    Store(key, hash, NoExpiry);
                }
            }
            else if (fields.Count > 0)
            {
                Touch(key);
            }

            return added;
        }
    }

    /// <summary>Sets a field only if it does not exist (<c>HSETNX</c>).</summary>
    /// <returns><see langword="true"/> if the field was set.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public bool HashSetIfNotExists(string key, string field, string value)
    {
        lock (StripeFor(key))
        {
            Dictionary<string, string>? hash = GetLive<Dictionary<string, string>>(key);
            if (hash is null)
            {
                Store(key, new Dictionary<string, string>(StringComparer.Ordinal) { [field] = value }, NoExpiry);
                return true;
            }

            if (!hash.TryAdd(field, value))
            {
                return false;
            }

            Touch(key);
            return true;
        }
    }

    /// <summary>The value of a field, or <see langword="null"/>.</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public string? HashGet(string key, string field)
    {
        lock (StripeFor(key))
        {
            return GetLive<Dictionary<string, string>>(key)?.GetValueOrDefault(field);
        }
    }

    /// <summary>The values of several fields; missing ones are <see langword="null"/> (<c>HMGET</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<string?> HashGetMany(string key, IReadOnlyList<string> fields)
    {
        lock (StripeFor(key))
        {
            Dictionary<string, string>? hash = GetLive<Dictionary<string, string>>(key);
            return fields.Select(field => hash?.GetValueOrDefault(field)).ToArray();
        }
    }

    /// <summary>Every field and value, in no particular order (<c>HGETALL</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<KeyValuePair<string, string>> HashGetAll(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<Dictionary<string, string>>(key)?.ToArray() ?? [];
        }
    }

    /// <summary>
    /// Removes fields (<c>HDEL</c>). A hash that becomes empty is deleted.
    /// </summary>
    /// <returns>The number of fields removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long HashDelete(string key, IEnumerable<string> fields)
    {
        lock (StripeFor(key))
        {
            Dictionary<string, string>? hash = GetLive<Dictionary<string, string>>(key);
            if (hash is null)
            {
                return 0;
            }

            long removed = fields.Count(hash.Remove);
            RemoveIfEmptyOrTouch(key, hash.Count, removed > 0);
            return removed;
        }
    }

    /// <summary>Whether the field exists (<c>HEXISTS</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public bool HashExists(string key, string field)
    {
        lock (StripeFor(key))
        {
            return GetLive<Dictionary<string, string>>(key)?.ContainsKey(field) ?? false;
        }
    }

    /// <summary>Number of fields, or 0 if the key does not exist (<c>HLEN</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long HashLength(string key)
    {
        lock (StripeFor(key))
        {
            return GetLive<Dictionary<string, string>>(key)?.Count ?? 0;
        }
    }

    /// <summary>Length of a field's value in bytes, or 0 (<c>HSTRLEN</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long HashStringLength(string key, string field)
    {
        lock (StripeFor(key))
        {
            return GetLive<Dictionary<string, string>>(key)?.GetValueOrDefault(field)?.Length ?? 0;
        }
    }

    /// <summary>
    /// Adds to the integer stored in a field, creating the hash and field from 0 if needed
    /// (<c>HINCRBY</c>).
    /// </summary>
    /// <returns>The new value.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">The field is not an integer, or the result would overflow.</exception>
    public long HashIncrementBy(string key, string field, long delta)
    {
        lock (StripeFor(key))
        {
            Dictionary<string, string> hash = GetOrCreateHash(key, out bool created);
            long current = 0;
            if (hash.TryGetValue(field, out string? text) &&
                !long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out current))
            {
                throw new StorageException("ERR hash value is not an integer");
            }

            long result;
            try
            {
                result = checked(current + delta);
            }
            catch (OverflowException)
            {
                throw new StorageException("ERR increment or decrement would overflow");
            }

            hash[field] = result.ToString(CultureInfo.InvariantCulture);
            CommitHash(key, hash, created);
            return result;
        }
    }

    /// <summary>
    /// Adds to the floating-point number stored in a field (<c>HINCRBYFLOAT</c>).
    /// </summary>
    /// <returns>The new value, formatted as stored.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">The field is not a number, or the result is not finite.</exception>
    public string HashIncrementByFloat(string key, string field, double delta)
    {
        lock (StripeFor(key))
        {
            Dictionary<string, string> hash = GetOrCreateHash(key, out bool created);
            double current = 0;
            if (hash.TryGetValue(field, out string? text) &&
                (!DoubleText.TryParse(text, out current) || double.IsInfinity(current)))
            {
                throw new StorageException("ERR hash value is not a float");
            }

            double result = current + delta;
            if (!double.IsFinite(result))
            {
                throw new StorageException("ERR increment would produce NaN or Infinity");
            }

            string formatted = DoubleText.Format(result);
            hash[field] = formatted;
            CommitHash(key, hash, created);
            return formatted;
        }
    }

    /// <summary>Returns the live hash, or a new empty one (not stored yet). Caller holds the stripe.</summary>
    private Dictionary<string, string> GetOrCreateHash(string key, out bool created)
    {
        Dictionary<string, string>? hash = GetLive<Dictionary<string, string>>(key);
        created = hash is null;
        return hash ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private void CommitHash(string key, Dictionary<string, string> hash, bool created)
    {
        if (created)
        {
            Store(key, hash, NoExpiry);
        }
        else
        {
            Touch(key);
        }
    }

    /// <summary>
    /// After removing items from a collection: deletes the key if it is now empty (Redis never
    /// stores empty collections), otherwise records the change. Caller holds the stripe.
    /// </summary>
    private void RemoveIfEmptyOrTouch(string key, int remaining, bool changed)
    {
        if (remaining == 0)
        {
            Remove(key);
        }
        else if (changed)
        {
            Touch(key);
        }
    }
}
