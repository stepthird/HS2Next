// The unowned bundles of the catalog by recency, with their bytes (engine-free; BundleOwners.cs keeps it): the head is the
// bundle released last, the tail the next one to evict. A key is in it at most once.
using System;
using System.Collections.Generic;

namespace HS2Next.Content
{
    public sealed class BundleLru
    {
        readonly LinkedList<string> order = new LinkedList<string>();
        readonly Dictionary<string, (LinkedListNode<string> Node, long Bytes)> map =
            new Dictionary<string, (LinkedListNode<string>, long)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The sum of the bytes of every key in it.</summary>
        public long Bytes { get; private set; }

        public int Count => map.Count;

        public bool Contains(string key) => key != null && map.ContainsKey(key);

        /// <summary>The bytes recorded for <paramref name="key"/> (0 when it is not in the LRU).</summary>
        public long BytesOf(string key) => key != null && map.TryGetValue(key, out var e) ? e.Bytes : 0;

        /// <summary>Puts <paramref name="key"/> at the head (most recent) with <paramref name="bytes"/>; a key already in it moves.</summary>
        public void AddHead(string key, long bytes)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (bytes < 0) bytes = 0;
            if (map.TryGetValue(key, out var e))
            {
                order.Remove(e.Node);
                Bytes -= e.Bytes;
            }
            var node = order.AddFirst(key);
            map[key] = (node, bytes);
            Bytes += bytes;
        }

        /// <summary>Removes <paramref name="key"/>; false when it was not in the LRU.</summary>
        public bool Remove(string key)
        {
            if (key == null || !map.TryGetValue(key, out var e)) return false;
            order.Remove(e.Node);
            map.Remove(key);
            Bytes -= e.Bytes;
            return true;
        }

        /// <summary>Changes the bytes of a key in the LRU (its place stays); nothing when it is not in it.</summary>
        public void SetBytes(string key, long bytes)
        {
            if (key == null || !map.TryGetValue(key, out var e)) return;
            if (bytes < 0) bytes = 0;
            Bytes += bytes - e.Bytes;
            map[key] = (e.Node, bytes);
        }

        /// <summary>The keys from the tail (the next to evict) to the head, as a copy.</summary>
        public List<string> FromTail()
        {
            var l = new List<string>(map.Count);
            for (var n = order.Last; n != null; n = n.Previous) l.Add(n.Value);
            return l;
        }

        /// <summary>The keys from the head (the most recent) to the tail, as a copy.</summary>
        public List<string> FromHead() => new List<string>(order);

        public void Clear()
        {
            order.Clear();
            map.Clear();
            Bytes = 0;
        }
    }
}
