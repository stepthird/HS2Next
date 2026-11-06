// Constraints of copied nodes, without the engine: what a duplicate and a scene import carry over. An entry means something in the
// copy only when both its ends were copied, so those entries come along with new ids and their ends on the copies; an entry with an
// end outside stays behind. Bone addresses and IK targets stay as they are: a copied character keeps its accessory ids.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Constraints
{
    public static class ConstraintRemap
    {
        /// <summary>
        /// Fresh copies of the entries whose source and target nodes are both keys of <paramref name="map"/> (old node id to new), in
        /// list order, each with an id from <paramref name="newId"/> and its ends remapped; <paramref name="leftOut"/> counts the rest.
        /// Everything else of an entry is kept, unknown members too. The originals are not changed.
        /// </summary>
        public static List<ConstraintEntry> Copy(IReadOnlyList<ConstraintEntry> entries, IReadOnlyDictionary<string, string> map, Func<string> newId, out int leftOut)
        {
            if (newId == null) throw new ArgumentNullException(nameof(newId));
            var copies = new List<ConstraintEntry>();
            leftOut = 0;
            if (entries == null || map == null) return copies;
            foreach (var e in entries)
            {
                if (e == null) continue;
                if (e.Source?.Node == null || e.Target?.Node == null || !map.TryGetValue(e.Source.Node, out string source) || !map.TryGetValue(e.Target.Node, out string target))
                {
                    leftOut++;
                    continue;
                }
                var c = e.Clone();
                c.Id = newId();
                c.Source.Node = source;
                c.Target.Node = target;
                copies.Add(c);
            }
            return copies;
        }

        /// <summary>
        /// The list a scene holds after the copies: <paramref name="list"/>'s entries, shared, then <paramref name="copies"/>. Null when
        /// there is nothing to add, so a scene with no such entry runs no edit at all.
        /// </summary>
        public static ConstraintList Appended(ConstraintList list, IReadOnlyList<ConstraintEntry> copies)
        {
            if (copies == null || copies.Count == 0) return null;
            var l = list != null ? new ConstraintList(list.Entries) : new ConstraintList();
            l.Entries.AddRange(copies);
            return l;
        }
    }
}
