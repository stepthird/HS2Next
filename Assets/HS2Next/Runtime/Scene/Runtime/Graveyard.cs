// Deleted holders kept for an instant undo: the top holder of each removed
// subtree is moved under the inactive Graveyard object with its whole realized subtree (inactive objects run
// nothing). Revive (undo of delete, redo of add) is done by the runtime's structural pass, which reparents the
// holder to its current attach transform; this class keeps the entries in age order and evicts the oldest ones
// beyond the limits (characters and other nodes counted per node). Correctness never depends on it: a revive
// whose objects are gone rebuilds from the model.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class Graveyard
    {
        public sealed class Entry
        {
            /// <summary>The removed subtree's top realization.</summary>
            public NodeRealization Top;
            /// <summary>Realizations under the top holder when it was buried (top included).</summary>
            public readonly List<NodeRealization> Members = new List<NodeRealization>();
            public int Charas, Nodes;
        }

        readonly LinkedList<Entry> entries = new LinkedList<Entry>();

        public Graveyard(Transform root) { Root = root; }

        /// <summary>The inactive parent of buried holders.</summary>
        public Transform Root { get; }
        public int EntryCount => entries.Count;
        public int CharaCount { get { int n = 0; foreach (var e in entries) n += e.Charas; return n; } }
        public int NodeCount { get { int n = 0; foreach (var e in entries) n += e.Nodes; return n; } }
        public IEnumerable<Entry> Entries => entries;

        /// <summary>Moves a removed subtree's top holder under the graveyard (world pose kept).</summary>
        internal Entry Bury(NodeRealization top, IReadOnlyDictionary<string, NodeRealization> reals)
        {
            var e = new Entry { Top = top };
            foreach (var h in top.Holder.GetComponentsInChildren<SceneNodeHolder>(true))
                if (h.NodeId != null && reals.TryGetValue(h.NodeId, out var r) && r.Holder == h.transform)
                {
                    e.Members.Add(r);
                    if (r.Kind == NodeKind.Chara) e.Charas++; else e.Nodes++;
                }
            top.Holder.SetParent(Root, true);
            entries.AddLast(e);
            return e;
        }

        /// <summary>The entry whose top is <paramref name="r"/> leaves the graveyard (revived).</summary>
        internal void Unbury(NodeRealization r)
        {
            for (var n = entries.First; n != null; n = n.Next)
                if (n.Value.Top == r) { entries.Remove(n); return; }
        }

        /// <summary>The oldest entries while the limits are exceeded (the caller destroys them).</summary>
        internal List<Entry> TakeOverLimit(int maxCharas, int maxNodes)
        {
            var l = new List<Entry>();
            int c = CharaCount, k = NodeCount;
            while (entries.Count > 0 && (c > maxCharas || k > maxNodes))
            {
                var e = entries.First.Value;
                entries.RemoveFirst();
                c -= e.Charas; k -= e.Nodes;
                l.Add(e);
            }
            return l;
        }

        /// <summary>Removes and returns the entries that contain <paramref name="r"/> as their top.</summary>
        internal Entry TakeTop(NodeRealization r)
        {
            for (var n = entries.First; n != null; n = n.Next)
                if (n.Value.Top == r) { entries.Remove(n); return n.Value; }
            return null;
        }

        internal void Clear() => entries.Clear();
    }
}
