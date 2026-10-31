namespace Cinnabar.Storage;

public sealed partial class Keyspace
{
    /// <summary>
    /// Pushes values onto an existing list only (<c>LPUSHX</c>, <c>RPUSHX</c>).
    /// </summary>
    /// <returns>The list length after the push, or 0 if the key does not exist.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long ListPushIfExists(string key, IEnumerable<string> values, ListEnd end)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            if (list is null)
            {
                return 0;
            }

            int before = list.Count;
            foreach (string value in values)
            {
                AddAt(list, value, end);
            }

            if (list.Count != before)
            {
                Touch(key);
            }

            return list.Count;
        }
    }

    /// <summary>
    /// The element at <paramref name="index"/> (<c>LINDEX</c>); negative indexes count from the
    /// end. <see langword="null"/> if the key is missing or the index is out of range.
    /// </summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public string? ListIndex(string key, long index)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            return list is not null && NodeAt(list, index) is { } node ? node.Value : null;
        }
    }

    /// <summary>Replaces the element at <paramref name="index"/> (<c>LSET</c>).</summary>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    /// <exception cref="StorageException">The key does not exist, or the index is out of range.</exception>
    public void ListSet(string key, long index, string value)
    {
        lock (StripeFor(key))
        {
            LinkedList<string> list = GetLive<LinkedList<string>>(key)
                ?? throw new StorageException("ERR no such key");

            LinkedListNode<string> node = NodeAt(list, index)
                ?? throw new StorageException("ERR index out of range");

            node.Value = value;
            Touch(key);
        }
    }

    /// <summary>
    /// Inserts <paramref name="value"/> before or after the first element equal to
    /// <paramref name="pivot"/> (<c>LINSERT</c>).
    /// </summary>
    /// <returns>The new length; <c>-1</c> if the pivot was not found; 0 if the key does not exist.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long ListInsert(string key, bool before, string pivot, string value)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            if (list is null)
            {
                return 0;
            }

            LinkedListNode<string>? node = list.Find(pivot);
            if (node is null)
            {
                return -1;
            }

            if (before)
            {
                list.AddBefore(node, value);
            }
            else
            {
                list.AddAfter(node, value);
            }

            Touch(key);
            return list.Count;
        }
    }

    /// <summary>
    /// Removes elements equal to <paramref name="value"/> (<c>LREM</c>): the first
    /// <paramref name="count"/> from the head if positive, the first <c>|count|</c> from the
    /// tail if negative, or all of them if 0. A list that becomes empty is deleted.
    /// </summary>
    /// <returns>The number of elements removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public long ListRemove(string key, long count, string value)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            if (list is null)
            {
                return 0;
            }

            bool fromTail = count < 0;
            long limit = count == 0 ? long.MaxValue : Math.Abs(count);
            long removed = 0;

            LinkedListNode<string>? node = fromTail ? list.Last : list.First;
            while (node is not null && removed < limit)
            {
                LinkedListNode<string>? next = fromTail ? node.Previous : node.Next;
                if (string.Equals(node.Value, value, StringComparison.Ordinal))
                {
                    list.Remove(node);
                    removed++;
                }

                node = next;
            }

            RemoveIfEmptyOrTouch(key, list.Count, removed > 0);
            return removed;
        }
    }

    /// <summary>
    /// Keeps only the elements between two inclusive indexes (<c>LTRIM</c>), with the same
    /// index rules as <see cref="ListRange"/>. A list that becomes empty is deleted.
    /// </summary>
    /// <returns><see langword="true"/> if anything was removed.</returns>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public bool ListTrim(string key, long start, long stop)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            if (list is null)
            {
                return false;
            }

            long length = list.Count;
            if (start < 0)
            {
                start = Math.Max(length + start, 0);
            }

            if (stop < 0)
            {
                stop = length + stop;
            }

            stop = Math.Min(stop, length - 1);

            long removeFromHead;
            long removeFromTail;
            if (start > stop || start >= length)
            {
                removeFromHead = length;
                removeFromTail = 0;
            }
            else
            {
                removeFromHead = start;
                removeFromTail = length - 1 - stop;
            }

            for (long i = 0; i < removeFromHead; i++)
            {
                list.RemoveFirst();
            }

            for (long i = 0; i < removeFromTail; i++)
            {
                list.RemoveLast();
            }

            bool changed = removeFromHead + removeFromTail > 0;
            RemoveIfEmptyOrTouch(key, list.Count, changed);
            return changed;
        }
    }

    /// <summary>
    /// Indexes of elements equal to <paramref name="element"/> (<c>LPOS</c>).
    /// </summary>
    /// <param name="key">The list key.</param>
    /// <param name="element">The value to look for.</param>
    /// <param name="rank">
    /// Which match to start from: 1 is the first from the head, -1 the first from the tail
    /// (the search then runs backwards). Never 0.
    /// </param>
    /// <param name="count">How many matches to return; 0 means all.</param>
    /// <param name="maxLength">Compare at most this many elements; 0 means no limit.</param>
    /// <exception cref="WrongTypeException">The key holds another type.</exception>
    public IReadOnlyList<long> ListPositions(string key, string element, long rank, long count, long maxLength)
    {
        lock (StripeFor(key))
        {
            LinkedList<string>? list = GetLive<LinkedList<string>>(key);
            if (list is null)
            {
                return [];
            }

            bool fromTail = rank < 0;
            long skip = Math.Abs(rank) - 1;
            var matches = new List<long>();

            long index = fromTail ? list.Count - 1 : 0;
            long compared = 0;
            LinkedListNode<string>? node = fromTail ? list.Last : list.First;

            while (node is not null && (maxLength == 0 || compared < maxLength))
            {
                compared++;
                if (string.Equals(node.Value, element, StringComparison.Ordinal))
                {
                    if (skip > 0)
                    {
                        skip--;
                    }
                    else
                    {
                        matches.Add(index);
                        if (count != 0 && matches.Count >= count)
                        {
                            break;
                        }
                    }
                }

                node = fromTail ? node.Previous : node.Next;
                index += fromTail ? -1 : 1;
            }

            return matches;
        }
    }

    /// <summary>
    /// Atomically pops an element from one end of <paramref name="source"/> and pushes it onto
    /// one end of <paramref name="destination"/> (<c>LMOVE</c>, <c>RPOPLPUSH</c>). The two keys
    /// may be the same, which rotates the list.
    /// </summary>
    /// <returns>The moved element, or <see langword="null"/> if the source does not exist.</returns>
    /// <exception cref="WrongTypeException">Either key holds another type.</exception>
    public string? ListMove(string source, string destination, ListEnd from, ListEnd to)
    {
        using StripeSet locks = LockKeys([source, destination]);

        LinkedList<string>? sourceList = GetLive<LinkedList<string>>(source);
        if (sourceList is null)
        {
            return null;
        }

        // Check the destination's type before changing anything, as Redis does.
        LinkedList<string>? destinationList = GetLive<LinkedList<string>>(destination);

        string value;
        if (from == ListEnd.Left)
        {
            value = sourceList.First!.Value;
            sourceList.RemoveFirst();
        }
        else
        {
            value = sourceList.Last!.Value;
            sourceList.RemoveLast();
        }

        if (source == destination)
        {
            AddAt(sourceList, value, to);
            Touch(source);
            return value;
        }

        RemoveIfEmptyOrTouch(source, sourceList.Count, changed: true);

        if (destinationList is null)
        {
            var created = new LinkedList<string>();
            created.AddFirst(value);
            Store(destination, created, NoExpiry);
        }
        else
        {
            AddAt(destinationList, value, to);
            Touch(destination);
        }

        return value;
    }

    private static void AddAt(LinkedList<string> list, string value, ListEnd end)
    {
        if (end == ListEnd.Left)
        {
            list.AddFirst(value);
        }
        else
        {
            list.AddLast(value);
        }
    }

    /// <summary>The node at a Redis-style index (negative counts from the end), or null if out of range.</summary>
    private static LinkedListNode<string>? NodeAt(LinkedList<string> list, long index)
    {
        if (index < 0)
        {
            index += list.Count;
        }

        if (index < 0 || index >= list.Count)
        {
            return null;
        }

        // Walk from whichever end is closer.
        if (index < list.Count / 2)
        {
            LinkedListNode<string> node = list.First!;
            for (long i = 0; i < index; i++)
            {
                node = node.Next!;
            }

            return node;
        }
        else
        {
            LinkedListNode<string> node = list.Last!;
            for (long i = list.Count - 1; i > index; i--)
            {
                node = node.Previous!;
            }

            return node;
        }
    }
}
