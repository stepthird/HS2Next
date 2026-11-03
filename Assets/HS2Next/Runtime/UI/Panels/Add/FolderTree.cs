// The folder tree beside the card and pose grids (74 % of the female cards and nearly all poses sit in
// subfolders): a TreeView of folder keys ("root" / "root/sub/dir"); selecting a folder shows its files and those of its
// subfolders. Root labels are localised (HS2 / HS2Next); counts in parentheses. v1.2: the views
// «★ お気に入り» and «最近追加» stand above the roots (keys of BrowserViews, with their counts and a tooltip that says what they
// list); selecting one lists the matching files of every folder.
using System;
using System.Collections.Generic;
using HS2Next.UI.Core;
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
        readonly List<string> views = new List<string>();
        readonly Dictionary<string, int> viewCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<string> lastFolders = Array.Empty<string>();
        public string SelectedKey { get; private set; }
        public event Action<string> Selected;
        public TreeView Tree => tree;
        /// <summary>The views shown above the roots (BrowserViews keys).</summary>
        public IReadOnlyList<string> Views => views;

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
                    var l = (Label)e;
                    l.EnableInClassList("hs-folder-tree__row--view", BrowserViews.Is(k));
                    if (BrowserViews.Is(k))
                    {
                        l.name = "folder-view-" + (k == BrowserViews.Favorites ? "favorites" : "recent");
                        string label = L10n.Get(BrowserViews.LabelKey(k));
                        l.text = viewCounts.TryGetValue(k, out int vc) ? label + "  (" + vc + ")" : label;
                        l.tooltip = L10n.Get(BrowserViews.TooltipKey(k, true));
                        return;
                    }
                    l.name = string.Empty;
                    int slash = k.LastIndexOf('/');
                    string text = slash < 0 ? rootLabel(k) : k.Substring(slash + 1);
                    l.text = counts.TryGetValue(k, out int c) ? text + "  (" + c + ")" : text;
                    l.tooltip = k;
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

        /// <summary>Shows these views above the roots (BrowserViews.Favorites / Recent; empty = none). Takes effect at the next
        /// SetFolders (or at once when folders are shown).</summary>
        public void SetViews(params string[] keys)
        {
            views.Clear();
            if (keys != null) foreach (var k in keys) if (BrowserViews.Is(k)) views.Add(k);
            if (idOf.Count > 0) Rebuild(lastFolders, false);
        }

        /// <summary>The counts shown after the views' labels (without a count the label stands alone).</summary>
        public void SetViewCounts(int favorites, int recent)
        {
            bool same = viewCounts.TryGetValue(BrowserViews.Favorites, out int f) && f == favorites && viewCounts.TryGetValue(BrowserViews.Recent, out int r) && r == recent;
            viewCounts[BrowserViews.Favorites] = favorites;
            viewCounts[BrowserViews.Recent] = recent;
            if (!same) tree.RefreshItems();
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
            Rebuild(folders, true);
        }

        void Rebuild(IReadOnlyList<string> folders, bool expandRoots)
        {
            lastFolders = folders ?? Array.Empty<string>();
            string keep = SelectedKey;
            var nodes = new Dictionary<string, List<TreeViewItemData<string>>>(StringComparer.Ordinal);
            var roots = new List<TreeViewItemData<string>>();
            int Id(string k)
            {
                if (!idOf.TryGetValue(k, out int id)) { id = idOf.Count + 1; idOf[k] = id; }
                keyOf[id] = k;
                return id;
            }
            foreach (var v in views) roots.Add(new TreeViewItemData<string>(Id(v), v));
            // children first (deepest last in ordinal order: build bottom-up)
            var sorted = new List<string>(lastFolders);
            sorted.Sort((a, b) => Depth(b).CompareTo(Depth(a)));
            var built = new Dictionary<string, TreeViewItemData<string>>(StringComparer.Ordinal);
            foreach (var k in sorted)
            {
                int id = Id(k);
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
            foreach (var k in lastFolders) if (k.IndexOf('/') < 0 && built.TryGetValue(k, out var it)) roots.Add(it);
            tree.SetRootItems(roots);
            tree.Rebuild();
            if (expandRoots) foreach (var r in roots) if (!BrowserViews.Is(r.data)) tree.ExpandItem(r.id, false, false);
            tree.RefreshItems();
            if (!expandRoots && keep != null && idOf.TryGetValue(keep, out int kid)) tree.SetSelectionByIdWithoutNotify(new[] { kid });
        }

        static int Depth(string k)
        {
            int d = 0;
            foreach (char c in k) if (c == '/') d++;
            return d;
        }

        /// <summary>Selects a folder or a view by key (null = none = all).</summary>
        public void Select(string key)
        {
            if (key != null && idOf.TryGetValue(key, out int id)) tree.SetSelectionById(id);
            else tree.ClearSelection();
        }
    }
}
