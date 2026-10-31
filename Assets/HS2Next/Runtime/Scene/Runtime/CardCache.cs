// Parsed HS2 cards and their bytes, keyed by the SHA-256 of the card file:
// rebuilds and duplicates of a card-backed character never re-read the file, and the importer (2.3) gets the
// bytes through CardAppearanceSource.ReadBytes. An entry is kept while any node id (in the model or held by the
// history) references it and released on the NodeForgotten of the last one; entries no node ever referenced are
// released when the scene is cleared.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Data;

namespace HS2Next.Scenes.Runtime
{
    public sealed class CardCache
    {
        public sealed class Entry
        {
            public string Sha256;
            public string FullPath;
            public byte[] Bytes;
            public Card Card;
            /// <summary>Node ids that reference this card (model or history).</summary>
            public readonly HashSet<string> Nodes = new HashSet<string>(StringComparer.Ordinal);
        }

        readonly Dictionary<string, Entry> bySha = new Dictionary<string, Entry>(StringComparer.Ordinal);
        // A node may reference several cards over its history (appearance changes): all are kept until it is forgotten.
        readonly Dictionary<string, HashSet<string>> shasOfNode = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public int Count => bySha.Count;
        public IEnumerable<Entry> Entries => bySha.Values;

        /// <summary>Adds (or refreshes the path of) an entry; the first parsed card of a hash is kept.</summary>
        public Entry Put(string sha256, string fullPath, byte[] bytes, Card card)
        {
            if (!bySha.TryGetValue(sha256, out var e))
            {
                e = new Entry { Sha256 = sha256, FullPath = fullPath, Bytes = bytes, Card = card };
                bySha[sha256] = e;
            }
            else
            {
                if (e.Bytes == null) e.Bytes = bytes;
                if (e.Card == null) e.Card = card;
                if (fullPath != null) e.FullPath = fullPath;
            }
            return e;
        }

        public Entry Get(string sha256) => sha256 != null && bySha.TryGetValue(sha256, out var e) ? e : null;

        /// <summary>
        /// The parsed card of a source: from the cache, else re-read from <see cref="CardAppearanceSource.FullPath"/> (after a
        /// release) when the file still has the same hash. Null when it cannot be had.
        /// </summary>
        public Card CardOf(CardAppearanceSource src)
        {
            if (src == null) return null;
            var e = Get(src.Sha256);
            if (e?.Card != null) return e.Card;
            var bytes = BytesOf(src);
            if (bytes == null) return null;
            var card = Card.Load(bytes, src.FullPath);
            Put(src.Sha256, src.FullPath, bytes, card);
            return card;
        }

        /// <summary>The card bytes of a source (cache, else the file when its hash still matches), or null.</summary>
        public byte[] BytesOf(CardAppearanceSource src)
        {
            if (src == null) return null;
            var e = Get(src.Sha256);
            if (e?.Bytes != null) return e.Bytes;
            if (string.IsNullOrEmpty(src.FullPath) || !File.Exists(src.FullPath)) return null;
            var bytes = File.ReadAllBytes(src.FullPath);
            if (!string.Equals(CardSourceFactory.Sha256Hex(bytes), src.Sha256, StringComparison.Ordinal)) return null;
            Put(src.Sha256, src.FullPath, bytes, null);
            return bytes;
        }

        /// <summary>Records that node <paramref name="nodeId"/> references the card <paramref name="sha256"/> (in the model or
        /// in history; references accumulate until the node is forgotten).</summary>
        public void Reference(string nodeId, string sha256)
        {
            if (nodeId == null || sha256 == null) return;
            if (!shasOfNode.TryGetValue(nodeId, out var set)) shasOfNode[nodeId] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(sha256);
            if (bySha.TryGetValue(sha256, out var e)) e.Nodes.Add(nodeId);
        }

        /// <summary>Drops the entry of <paramref name="sha256"/> when no node references it (a card imported at add,
        /// 2); true when released.</summary>
        public bool ReleaseIfUnreferenced(string sha256)
        {
            if (sha256 == null || !bySha.TryGetValue(sha256, out var e) || e.Nodes.Count > 0) return false;
            foreach (var set in shasOfNode.Values) if (set.Contains(sha256)) return false;
            bySha.Remove(sha256);
            return true;
        }

        /// <summary>A node can never come back (NodeForgotten): drops its references and releases cards no node references.</summary>
        public void Unreference(string nodeId)
        {
            if (nodeId == null || !shasOfNode.TryGetValue(nodeId, out var set)) return;
            shasOfNode.Remove(nodeId);
            foreach (var sha in set)
                if (bySha.TryGetValue(sha, out var e))
                {
                    e.Nodes.Remove(nodeId);
                    if (e.Nodes.Count == 0) bySha.Remove(sha);
                }
        }

        /// <summary>The scene was cleared: every node reference is dropped and every entry released.</summary>
        public void Clear()
        {
            shasOfNode.Clear();
            bySha.Clear();
        }
    }
}
