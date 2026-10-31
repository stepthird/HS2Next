// The selection of a scene session: an ordered set of node ids, the
// first one primary. Not document state and not in history, but command context: the bus restores the
// selection before an operation on undo and the one after it on redo. Ids that are not in the model are never
// selected; nodes leaving the model leave the selection. Changes raise SceneEvents.SelectionChanged.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    public sealed class SelectionModel
    {
        readonly SceneSession session;
        readonly List<string> ids = new List<string>();

        internal SelectionModel(SceneSession session) { this.session = session; }

        /// <summary>Selected ids in selection order.</summary>
        public IReadOnlyList<string> Ids => ids;
        /// <summary>The first selected id, or null.</summary>
        public string Primary => ids.Count > 0 ? ids[0] : null;
        public int Count => ids.Count;

        public bool Contains(string id) => id != null && ids.Contains(id);

        /// <summary>Replaces the selection; ids not in the model and repeats are skipped.</summary>
        public void Set(IEnumerable<string> newIds)
        {
            var old = Snapshot();
            ids.Clear();
            if (newIds != null)
                foreach (var id in newIds)
                    if (Selectable(id) && !ids.Contains(id)) ids.Add(id);
            Changed(old);
        }

        /// <summary>Appends a node; false when it is not in the model or already selected.</summary>
        public bool Add(string id)
        {
            if (!Selectable(id) || ids.Contains(id)) return false;
            var old = Snapshot();
            ids.Add(id);
            Changed(old);
            return true;
        }

        public bool Remove(string id)
        {
            int i = id == null ? -1 : ids.IndexOf(id);
            if (i < 0) return false;
            var old = Snapshot();
            ids.RemoveAt(i);
            Changed(old);
            return true;
        }

        /// <summary>Removes a selected node, adds an unselected one.</summary>
        public void Toggle(string id)
        {
            if (!Remove(id)) Add(id);
        }

        public void Clear()
        {
            if (ids.Count == 0) return;
            var old = Snapshot();
            ids.Clear();
            Changed(old);
        }

        bool Selectable(string id)
        {
            if (id == null) return false;
            var n = session.Model.Find(id);
            return n != null && n.InModel;
        }

        internal string[] Snapshot() => ids.ToArray();

        /// <summary>Sets the ids without raising (the bus raises once per operation); unknown ids are skipped.</summary>
        internal void SetSilently(IEnumerable<string> newIds)
        {
            ids.Clear();
            if (newIds != null)
                foreach (var id in newIds)
                    if (Selectable(id) && !ids.Contains(id)) ids.Add(id);
        }

        /// <summary>Drops ids that left the model (without raising).</summary>
        internal void PruneSilently() => ids.RemoveAll(id => !Selectable(id));

        internal static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        void Changed(string[] old)
        {
            if (Same(old, ids)) return;
            session.Events.RaiseSelectionChanged(old, Snapshot());
        }
    }
}
