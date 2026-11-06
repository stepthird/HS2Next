// Undo/redo history of the bus: entries with their command,
// label, merge key, seal flag and the selection before and after. Nodes held by entries are counted, so a
// node is forgotten (NodeForgotten) only when it is out of the model and no remaining entry can bring it back.
// An entry of a party outside the model (a plugin's own state) carries its owner and no selection.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    internal sealed class HistoryEntry
    {
        public ISceneCommand Command;
        public string Label;
        public CommandSource Source;
        public string MergeKey;
        /// <summary>No later command merges into a sealed entry (undo, redo, save, a transaction commit or any other command seals it).</summary>
        public bool Sealed;
        /// <summary>The selection before and after; null for an external entry (its undo and redo leave the selection as it is).</summary>
        public string[] SelectionBefore, SelectionAfter;
        public long Bytes;
        public TreeNode[] Held;
        /// <summary>The party of an external entry (a plugin's GUID), else null.</summary>
        public string Owner;

        public HistoryEntryInfo Info => new HistoryEntryInfo(Label, Source, Bytes, Owner);
    }

    internal sealed class History
    {
        /// <summary>Oldest first; the last entry is the next to undo.</summary>
        public readonly List<HistoryEntry> Undo = new List<HistoryEntry>();
        /// <summary>The last entry is the next to redo.</summary>
        public readonly List<HistoryEntry> Redo = new List<HistoryEntry>();
        readonly Dictionary<TreeNode, int> holds = new Dictionary<TreeNode, int>();
        readonly Dictionary<string, TreeNode> heldById = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
        public long Bytes;

        public HistoryEntry Top => Undo.Count > 0 ? Undo[Undo.Count - 1] : null;

        public TreeNode HeldWithId(string id) => id != null && heldById.TryGetValue(id, out var n) ? n : null;

        public bool IsHeld(TreeNode n) => holds.ContainsKey(n);

        public void Hold(HistoryEntry e)
        {
            e.Bytes = e.Command.EstimatedBytes;
            Bytes += e.Bytes;
            var list = new List<TreeNode>(e.Command.HeldNodes);
            e.Held = list.ToArray();
            foreach (var n in e.Held)
            {
                holds.TryGetValue(n, out int c);
                holds[n] = c + 1;
                heldById[n.Id] = n;
            }
        }

        /// <summary>Releases an entry's holds; nodes that become unheld while out of the model go to <paramref name="forgotten"/>.</summary>
        public void Release(HistoryEntry e, List<TreeNode> forgotten)
        {
            Bytes -= e.Bytes;
            if (e.Held == null) return;
            foreach (var n in e.Held)
            {
                if (!holds.TryGetValue(n, out int c)) continue;
                if (c > 1) { holds[n] = c - 1; continue; }
                holds.Remove(n);
                if (heldById.TryGetValue(n.Id, out var h) && h == n) heldById.Remove(n.Id);
                if (!n.InModel) forgotten.Add(n);
            }
        }

        /// <summary>Re-measures an entry after a merge.</summary>
        public void Resize(HistoryEntry e)
        {
            Bytes -= e.Bytes;
            e.Bytes = e.Command.EstimatedBytes;
            Bytes += e.Bytes;
        }

        /// <summary>Removes the entries of <paramref name="owner"/> from both lists (the others keep their order); the count removed.</summary>
        public int RemoveOwner(string owner, List<TreeNode> forgotten)
        {
            int n = 0;
            foreach (var list in new[] { Undo, Redo })
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    if (e.Owner == null || !string.Equals(e.Owner, owner, StringComparison.Ordinal)) continue;
                    list.RemoveAt(i);
                    Release(e, forgotten);
                    n++;
                }
            return n;
        }

        /// <summary>The entries of <paramref name="owner"/> in both lists.</summary>
        public int CountOwner(string owner)
        {
            int n = 0;
            foreach (var e in Undo) if (e.Owner != null && string.Equals(e.Owner, owner, StringComparison.Ordinal)) n++;
            foreach (var e in Redo) if (e.Owner != null && string.Equals(e.Owner, owner, StringComparison.Ordinal)) n++;
            return n;
        }

        public void ClearAll(List<TreeNode> forgotten)
        {
            foreach (var e in Undo) Release(e, forgotten);
            foreach (var e in Redo) Release(e, forgotten);
            Undo.Clear();
            Redo.Clear();
            holds.Clear();
            heldById.Clear();
            Bytes = 0;
        }
    }
}
