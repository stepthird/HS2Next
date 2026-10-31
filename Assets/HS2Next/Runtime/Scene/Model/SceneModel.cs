// The runtime scene model: the tree index over the 2.1 document
// objects (TreeNode per SceneNode), the scene-wide SceneSettings, the scene-scope inline ext, the unknown
// top-level members and title of the read document, and the SceneContainer carrier (resources, foreign
// entries, thumbnail, manifest, file-backed ext). Mutators are internal to the commands; everyone else reads.
// Every structural mutation keeps Doc.Parent / Doc.Order equal to Parent / Point / Index (siblings renumbered
// 0..n−1) and the cached effective visibility of the moved subtree up to date.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes
{
    public sealed class SceneModel
    {
        readonly Dictionary<string, TreeNode> byId = new Dictionary<string, TreeNode>(StringComparer.Ordinal);
        internal readonly ChildGroup RootGroup = new ChildGroup(null);
        List<TreeNode> preOrder;

        internal SceneModel(AttachPointTable attachPoints, TreeRules rules, ILabelProvider labels)
        {
            AttachPoints = attachPoints ?? AttachPointTable.Vanilla;
            Rules = rules ?? new TreeRules();
            LabelProvider = labels ?? DefaultLabelProvider.Instance;
            File = new SceneContainer();
        }

        /// <summary>Root nodes in order.</summary>
        public IReadOnlyList<TreeNode> Roots => RootGroup.Nodes;

        /// <summary>The node with this id that is in the model, or null.</summary>
        public TreeNode Find(string id) => id != null && byId.TryGetValue(id, out var n) ? n : null;

        /// <summary>Nodes in the model.</summary>
        public int Count => byId.Count;

        /// <summary>Every node in canonical pre-order (= the array order).</summary>
        public IReadOnlyList<TreeNode> PreOrder()
        {
            if (preOrder == null)
            {
                var l = new List<TreeNode>(byId.Count);
                var stack = new Stack<TreeNode>();
                for (int i = RootGroup.Nodes.Count - 1; i >= 0; i--) stack.Push(RootGroup.Nodes[i]);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    l.Add(n);
                    var kids = n.Children;
                    for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
                }
                preOrder = l;
            }
            return preOrder;
        }

        /// <summary><paramref name="node"/> and its descendants in pre-order (works for nodes out of the model too).</summary>
        public IReadOnlyList<TreeNode> Subtree(TreeNode node) => SubtreeOf(node);

        internal static List<TreeNode> SubtreeOf(TreeNode node)
        {
            var l = new List<TreeNode>();
            if (node == null) return l;
            var stack = new Stack<TreeNode>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                l.Add(n);
                var kids = n.Children;
                for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
            }
            return l;
        }

        /// <summary>The sibling group of (<paramref name="parent"/>, <paramref name="point"/>) in order; parent null = the roots.</summary>
        public IReadOnlyList<TreeNode> GroupOf(TreeNode parent, string point)
        {
            var g = FindGroup(parent, point, false);
            return g != null ? g.Nodes : (IReadOnlyList<TreeNode>)Array.Empty<TreeNode>();
        }

        /// <summary>The scene-wide state (`scene` of scene.json).</summary>
        public SceneSettings Settings { get; internal set; } = new SceneSettings();
        /// <summary>Scene-scope inline ext (scene.json top-level `ext`).</summary>
        public ExtInline SceneExt { get; internal set; } = new ExtInline();
        /// <summary>Unknown top-level members of the read scene.json (kept, written back).</summary>
        public JsonLeftovers RootUnknown { get; internal set; }
        /// <summary>Scene title (manifest.title).</summary>
        public string Title { get; internal set; } = "";
        /// <summary>The container carrier: resources, foreign entries, thumbnail, created time, loaded manifest, newer-format
        /// flag, scene-scope file ext. The model does not interpret it.</summary>
        public SceneContainer File { get; internal set; }
        public AttachPointTable AttachPoints { get; }
        public TreeRules Rules { get; }
        internal ILabelProvider LabelProvider { get; set; }
        internal SelectionModel Selection;
        /// <summary>A node with this id that history holds out of the model, or null (set by the session).</summary>
        internal Func<string, TreeNode> HeldLookup;

        /// <summary>The scene.json bytes of the current model, written canonically without any normalisation (tests, bridge).</summary>
        public byte[] CanonicalBytes() => CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, SceneDocumentConverter.ToDocument(this).Write);

        // ---------------- invariants ----------------

        /// <summary>Checks the invariants (tests); throws <see cref="InvalidOperationException"/> with the first violation.</summary>
        public void Validate()
        {
            void Fail(string what) => throw new InvalidOperationException("scene model invariant: " + what);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int reached = 0;

            void CheckGroup(TreeNode parent, ChildGroup g)
            {
                if (g.Nodes.Count == 0 && parent != null) Fail("empty child group kept under " + parent);
                for (int i = 0; i < g.Nodes.Count; i++)
                {
                    var n = g.Nodes[i];
                    reached++;
                    if (!seen.Add(n.Id)) Fail("node " + n.Id + " appears twice (cycle or duplicate id)");
                    if (!n.InModel) Fail(n + " is in the tree but not InModel");
                    if (Find(n.Id) != n) Fail("Find(" + n.Id + ") disagrees with the tree");
                    if (n.Parent != parent) Fail(n + ": Parent link differs from the tree");
                    if (!string.Equals(n.Point, g.Point, StringComparison.Ordinal)) Fail(n + ": Point differs from its group");
                    if (n.Index != i || n.Doc.Order != i) Fail(n + ": index " + n.Index + " / order " + n.Doc.Order + ", expected " + i);
                    var dp = n.Doc.Parent;
                    if (parent == null ? dp != null : dp == null || dp.Node != parent.Id || !string.Equals(dp.Point, n.Point, StringComparison.Ordinal))
                        Fail(n + ": Doc.Parent differs from the tree");
                    if (parent != null && (parent.Kind == NodeKind.Light || parent.Kind == NodeKind.Camera)) Fail(parent + " takes no children");
                    bool ev = n.Doc.Visible && (parent == null || parent.EffectiveVisible);
                    if (n.EffectiveVisible != ev) Fail(n + ": EffectiveVisible " + n.EffectiveVisible + ", expected " + ev);
                    if ((n.Kind == NodeKind.Chara) != (n.Appearance != null)) Fail(n + ": appearance object " + (n.Appearance == null ? "missing" : "on a non-character"));
                    if (n.Kind != NodeKind.Opaque && n.Doc.Ui.IsChara != (n.Kind == NodeKind.Chara)) Fail(n + ": ui character flag");
                    int last = -1;
                    string lastPoint = null;
                    foreach (var cg in n.Groups)
                    {
                        if (cg.Rank < last || (cg.Rank == last && string.CompareOrdinal(cg.Point, lastPoint) <= 0)) Fail(n + ": child groups out of order");
                        last = cg.Rank;
                        lastPoint = cg.Point;
                        CheckGroup(n, cg);
                    }
                }
            }

            CheckGroup(null, RootGroup);
            if (reached != byId.Count) Fail("index holds " + byId.Count + " nodes, the tree " + reached);
            if (preOrder != null)
            {
                var keep = preOrder;
                preOrder = null;
                var fresh = PreOrder();
                if (fresh.Count != keep.Count) Fail("stale pre-order cache");
                for (int i = 0; i < fresh.Count; i++) if (fresh[i] != keep[i]) Fail("stale pre-order cache");
            }
            if (Selection != null)
                foreach (var id in Selection.Ids)
                    if (Find(id) == null) Fail("selected id " + id + " is not in the model");
        }

        // ---------------- mutators (commands and the converter only) ----------------

        internal void Reset(SceneContainer file)
        {
            foreach (var n in byId.Values) n.inModel = false;
            byId.Clear();
            RootGroup.Nodes.Clear();
            preOrder = null;
            Settings = new SceneSettings();
            SceneExt = new ExtInline();
            RootUnknown = null;
            Title = "";
            File = file ?? new SceneContainer();
        }

        internal void TreeChanged() => preOrder = null;
        internal Dictionary<string, TreeNode> Index => byId;

        /// <summary>The group (parent, point); created in canonical position when <paramref name="create"/>.</summary>
        internal ChildGroup FindGroup(TreeNode parent, string point, bool create) => GroupIn(parent, point, create, RootGroup);

        internal static ChildGroup GroupIn(TreeNode parent, string point, bool create, ChildGroup roots)
        {
            if (parent == null) return point == null ? roots : null;
            var groups = parent.Groups;
            int i = 0;
            for (; i < groups.Count; i++)
            {
                int c = groups[i].CompareTo(point);
                if (c == 0) return groups[i];
                if (c > 0) break;
            }
            if (!create) return null;
            var g = new ChildGroup(point);
            groups.Insert(i, g);
            return g;
        }

        /// <summary>Links a node (with its subtree) under a parent of a node tree that is not in the model (built commands).</summary>
        internal static void LinkDetached(TreeNode parent, TreeNode child, string point, int index)
        {
            var g = GroupIn(parent, point, true, null);
            if (index < 0 || index > g.Nodes.Count) index = g.Nodes.Count;
            g.Nodes.Insert(index, child);
            child.parent = parent;
            child.point = point;
            SetParentLink(child);
            Renumber(g, index);
            parent.ChildrenChanged();
        }

        /// <summary>Inserts a node that is not in the model (with its subtree) at (parent, point, index); index −1 = end.</summary>
        internal void Attach(TreeNode node, TreeNode parent, string point, int index)
        {
            var g = FindGroup(parent, point, true);
            if (index < 0 || index > g.Nodes.Count) index = g.Nodes.Count;
            g.Nodes.Insert(index, node);
            node.parent = parent;
            node.point = point;
            SetParentLink(node);
            Renumber(g, index);
            parent?.ChildrenChanged();
            foreach (var n in SubtreeOf(node))
            {
                n.inModel = true;
                n.Owner = this;
                byId[n.Id] = n;
                if (n.Kind != NodeKind.Opaque) n.Doc.Ui.IsChara = n.Kind == NodeKind.Chara;
            }
            UpdateVisibility(node);
            preOrder = null;
        }

        /// <summary>Takes a node and its subtree out of the model; the node keeps its links for history.</summary>
        internal void Detach(TreeNode node)
        {
            var g = FindGroup(node.parent, node.point, false);
            int at = node.index;
            g.Nodes.RemoveAt(at);
            Renumber(g, at);
            if (g.Nodes.Count == 0 && node.parent != null) node.parent.Groups.Remove(g);
            node.parent?.ChildrenChanged();
            foreach (var n in SubtreeOf(node))
            {
                n.inModel = false;
                byId.Remove(n.Id);
            }
            preOrder = null;
        }

        /// <summary>Moves a node in the model to (parent, point, index): index is the final index after it left its old group.</summary>
        internal void Move(TreeNode node, TreeNode parent, string point, int index)
        {
            var from = FindGroup(node.parent, node.point, false);
            int at = node.index;
            from.Nodes.RemoveAt(at);
            Renumber(from, at);
            if (from.Nodes.Count == 0 && node.parent != null) node.parent.Groups.Remove(from);
            node.parent?.ChildrenChanged();
            var g = FindGroup(parent, point, true);
            if (index < 0 || index > g.Nodes.Count) index = g.Nodes.Count;
            g.Nodes.Insert(index, node);
            node.parent = parent;
            node.point = point;
            SetParentLink(node);
            Renumber(g, from == g ? Math.Min(at, index) : index);
            parent?.ChildrenChanged();
            UpdateVisibility(node);
            preOrder = null;
        }

        /// <summary>Doc.Parent follows the tree; an unchanged link keeps its object (and its unknown members).</summary>
        internal static void SetParentLink(TreeNode node)
        {
            var p = node.parent;
            var dp = node.Doc.Parent;
            if (p == null) { node.Doc.Parent = null; return; }
            if (dp != null && dp.Node == p.Id && string.Equals(dp.Point, node.point, StringComparison.Ordinal)) return;
            node.Doc.Parent = new NodeParent(p.Id, node.point);
        }

        /// <summary>Sibling numbers 0..n−1; an opaque node whose raw object lacks `order` gets one after `kind`.</summary>
        internal static void Renumber(ChildGroup g, int from = 0)
        {
            for (int i = Math.Max(0, from); i < g.Nodes.Count; i++)
            {
                var n = g.Nodes[i];
                n.index = i;
                if (n.Doc.Order == i) continue;
                n.Doc.Order = i;
                if (n.Doc.State is OpaqueNode o && o.Raw != null && o.Raw.Property("order") == null)
                {
                    var kind = o.Raw.Property("kind");
                    if (kind != null) kind.AddAfterSelf(new JProperty("order", i));
                    else o.Raw.Add("order", i);
                }
            }
        }

        /// <summary>Recomputes EffectiveVisible of a subtree from its parent.</summary>
        internal void UpdateVisibility(TreeNode top)
        {
            var stack = new Stack<TreeNode>();
            stack.Push(top);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                n.effectiveVisible = n.Doc.Visible && (n.parent == null || n.parent.effectiveVisible);
                foreach (var c in n.Children) stack.Push(c);
            }
        }
    }
}
