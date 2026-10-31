// The folder tree beside the card and pose grids (74 % of the female cards and nearly all poses sit in
// subfolders): a TreeView of folder keys ("root" / "root/sub/dir"); selecting a folder shows its files and those of its
// subfolders. Root labels are localised (HS2 / HS2Next); counts in parentheses.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class FolderTree : VisualElement
    {
        readonly TreeView tree;
        readonly Dictionary<int, string> keyOf = new Dictionary<int, string>();
        readonly Dictionary<string, int> idOf = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Func<string, string> rootLabel;
        public string SelectedKey { get; private set; }
        public event Action<string> Selected;
        public TreeView Tree => tree;

        public FolderTree(string name, Func<string, string> rootLabel)
        {
            this.name = name;
            this.rootLabel = rootLabel;
            AddToClassList("hs-folder-tree");
            tree = new TreeView
            {
                name = name + "-tree",
                fixedItemHeight = 20,
                selectionType = SelectionType.Single,
                makeItem = () => { var l = new Label(); l.AddToClassList("hs-folder-tree__row"); return l; },
                bindItem = (e, i) =>
                {
                    string k = tree.GetItemDataForIndex<string>(i);
                    int slash = k.LastIndexOf('/');
                    string label = slash < 0 ? rootLabel(k) : k.Substring(slash + 1);
                    ((Label)e).text = counts.TryGetValue(k, out int c) ? label + "  (" + c + ")" : label;
                    e.tooltip = k;
                },
            };
            tree.AddToClassList("hs-grow");
            tree.selectionChanged += sel =>
            {
                string k = null;
                foreach (var o in sel) { k = o as string; break; }
                if (k == SelectedKey) return;
                SelectedKey = k;
                Selected?.Invoke(k);
            };
            Add(tree);
        }

        /// <summary>Updates the file counts shown in parentheses (the folders stay; the pose browser while names fill in).</summary>
        public void SetCounts(IEnumerable<string> fileFolderKeys)
        {
            Count(fileFolderKeys);
            tree.RefreshItems();
        }

        void Count(IEnumerable<string> fileFolderKeys)
        {
            counts.Clear();
            foreach (var k0 in fileFolderKeys)
            {
                string k = k0;
                while (true)
                {
                    counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
                    int i = k.LastIndexOf('/');
                    if (i < 0) break;
                    k = k.Substring(0, i);
                }
            }
        }

        /// <summary>Sets the folders (roots first, then "root/a/b" keys) and the file counts per folder (with subfolders).</summary>
        public void SetFolders(IReadOnlyList<string> folders, IEnumerable<string> fileFolderKeys)
        {
            Count(fileFolderKeys);
            var nodes = new Dictionary<string, List<TreeViewItemData<string>>>(StringComparer.Ordinal);
            var roots = new List<TreeViewItemData<string>>();
            int next = 1;
            // children first (deepest last in ordinal order: build bottom-up)
            var sorted = new List<string>(folders);
            sorted.Sort((a, b) => Depth(b).CompareTo(Depth(a)));
            var built = new Dictionary<string, TreeViewItemData<string>>(StringComparer.Ordinal);
            foreach (var k in sorted)
            {
                if (!idOf.TryGetValue(k, out int id)) { id = idOf.Count + 1; idOf[k] = id; }
                keyOf[id] = k;
                nodes.TryGetValue(k, out var ch);
                ch?.Sort((a, b) => string.CompareOrdinal(a.data, b.data));
                var item = new TreeViewItemData<string>(id, k, ch);
                built[k] = item;
                int slash = k.LastIndexOf('/');
                if (slash < 0) continue;
                string parent = k.Substring(0, slash);
                if (!nodes.TryGetValue(parent, out var pl)) nodes[parent] = pl = new List<TreeViewItemData<string>>();
                pl.Add(item);
            }
            foreach (var k in folders) if (k.IndexOf('/') < 0 && built.TryGetValue(k, out var it)) roots.Add(it);
            next++;
            tree.SetRootItems(roots);
            tree.Rebuild();
            foreach (var r in roots) tree.ExpandItem(r.id, false, false);
            tree.RefreshItems();
        }

        static int Depth(string k)
        {
            int d = 0;
            foreach (char c in k) if (c == '/') d++;
            return d;
        }

        /// <summary>Selects a folder by key (null = none = all).</summary>
        public void Select(string key)
        {
            if (key != null && idOf.TryGetValue(key, out int id)) tree.SetSelectionById(id);
            else tree.ClearSelection();
        }
    }
}
