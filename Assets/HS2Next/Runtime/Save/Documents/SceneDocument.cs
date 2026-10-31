// scene.json: a flat array of nodes (parents referenced by id) and the scene-wide
// state. A file model only; the Phase 2.2 runtime model converts to and from it. The reader repairs the tree
// (missing parent, a parent kind that takes no children, cycles → scene root, `scene.badParent`), renumbers
// siblings by (order, array position) and keeps the nodes in the canonical array order the writer emits:
// depth-first pre-order, siblings by order, the children of a character grouped by attach point in
// AccessoryParentKey value order (unknown point names last, ordinal). Character appearance is not here: it is
// the subtree charas/<nodeId>/ (CharaSubtree).
using System;
using System.Collections.Generic;

namespace HS2Next.Save
{
    public sealed class SceneDocument : DocObject
    {
        public const string EntryName = "scene.json";

        /// <summary>Every node, in canonical array order after reading (any order is accepted on write).</summary>
        public List<SceneNode> Nodes = new List<SceneNode>();
        public SceneSettings Scene = new SceneSettings();
        /// <summary>Inline ext of the scene scope.</summary>
        public ExtInline Ext = new ExtInline();

        /// <summary>Reads a document from a reader over its root object. A duplicate node id fails with BadDocument.</summary>
        public static SceneDocument Read(DocReader r)
        {
            var d = new SceneDocument();
            d.ReadFrom(r);
            return d;
        }

        /// <summary>Writes the root object (the caller finishes the document).</summary>
        public void Write(CanonicalJsonWriter w) => WriteTo(w);

        internal override ExtInline InlineExt => Ext;

        /// <summary>The node with id <paramref name="id"/>, or null.</summary>
        public SceneNode Find(string id) => Nodes.Find(n => n.Id == id);

        internal override void ReadMembers(DocReader r)
        {
            var items = r.ObjArray("nodes");
            var nodes = new List<SceneNode>(items.Count);
            var byId = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
            foreach (var e in items)
            {
                var n = SceneNode.Read(e);
                if (byId.ContainsKey(n.Id))
                    throw new SaveFormatException(r.Context.ErrorCode, "duplicate node id " + n.Id, r.Context.Entry, DocReader.Join(e.Path, "id"));
                byId[n.Id] = n;
                nodes.Add(n);
            }
            Scene.ReadFrom(r.Obj("scene"));
            Ext = r.Ext();

            RepairParents(nodes, byId, r);
            Nodes = CanonicalOrder(nodes, out var order);
            foreach (var n in Nodes) n.Order = order[n];
            CheckNodeRef(r, byId, Scene.Effects.DepthOfField.Focus, "scene.effects.depthOfField.focus");
            CheckNodeRef(r, byId, Scene.Effects.SunShafts.Caster, "scene.effects.sunShafts.caster");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            // Write guards before the tree is sorted (paths name the index in the model's list).
            var nodes = Nodes ?? new List<SceneNode>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                string at = "nodes[" + i + "]";
                if (n == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "node is null", w.Entry, at);
                if (n.Id == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "required id is null", w.Entry, at + ".id");
                if (n.Parent != null && n.Parent.Node == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "required id is null", w.Entry, at + ".parent.node");
                if (!ids.Add(n.Id)) throw new SaveFormatException(SaveErrorCode.BadDocument, "duplicate node id " + n.Id, w.Entry, "nodes");
            }
            var sorted = CanonicalOrder(nodes, out var order);
            w.StartArray("nodes");
            foreach (var n in sorted) n.Write(w, order[n]);
            w.EndArray();
            Scene.WriteTo(w, "scene");
        }

        static void CheckNodeRef(DocReader r, Dictionary<string, SceneNode> byId, string id, string path)
        {
            if (id != null && !byId.ContainsKey(id))
                r.Context.Findings.Warning("scene.unknownNodeRef", r.Context.Where(path), "node " + id + " does not exist (treated as null at run time)");
        }

        // Missing parent, a parent kind that takes no children, or a cycle → the node becomes a root. Linear:
        // each chain is walked once; a walk that meets a node of its own path detaches the node where it re-entered.
        static void RepairParents(List<SceneNode> nodes, Dictionary<string, SceneNode> byId, DocReader r)
        {
            void Detach(SceneNode n, string why)
            {
                n.Parent = null;
                r.Context.Findings.Warning("scene.badParent", r.Context.Where("nodes"), "node " + n.Id + " moved to the scene root: " + why);
            }

            foreach (var n in nodes)
            {
                if (n.Parent == null) continue;
                if (!byId.TryGetValue(n.Parent.Node, out var p)) Detach(n, "parent " + n.Parent.Node + " does not exist");
                else if (!p.State.TakesChildren) Detach(n, "a " + p.Kind + " node takes no children");
            }

            var reachesRoot = new HashSet<SceneNode>();
            var path = new HashSet<SceneNode>();
            foreach (var n in nodes)
            {
                path.Clear();
                for (var cur = n; cur != null && !reachesRoot.Contains(cur);)
                {
                    if (!path.Add(cur)) { Detach(cur, "parent cycle"); break; }
                    cur = cur.Parent == null ? null : byId[cur.Parent.Node];
                }
                reachesRoot.UnionWith(path);
            }
        }

        /// <summary>
        /// The canonical array order and each node's sibling number: depth-first pre-order from the roots;
        /// siblings sorted by (order, list position); the children of a node grouped by point: null first, then the
        /// AccessoryParentKey values 1..54, then unknown names ordinal. Nodes whose parent chain does not reach a root
        /// (only possible in a model that was not read through the repair) are written as roots after the others.
        /// </summary>
        public static List<SceneNode> CanonicalOrder(List<SceneNode> nodes, out Dictionary<SceneNode, int> order)
        {
            var pos = new Dictionary<SceneNode, int>();
            var byId = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
            {
                pos[nodes[i]] = i;
                byId[nodes[i].Id] = nodes[i];
            }
            var children = new Dictionary<string, List<SceneNode>>(StringComparer.Ordinal);
            var roots = new List<SceneNode>();
            foreach (var n in nodes)
            {
                if (n.Parent == null || !byId.ContainsKey(n.Parent.Node)) { roots.Add(n); continue; }
                if (!children.TryGetValue(n.Parent.Node, out var list)) children[n.Parent.Node] = list = new List<SceneNode>();
                list.Add(n);
            }

            order = new Dictionary<SceneNode, int>();
            var result = new List<SceneNode>(nodes.Count);
            var done = new HashSet<SceneNode>();
            var ord = order;

            void Number(List<SceneNode> sibs, bool grouped)
            {
                sibs.Sort((a, b) =>
                {
                    if (grouped)
                    {
                        int c = PointRank(a.Parent?.Point).CompareTo(PointRank(b.Parent?.Point));
                        if (c != 0) return c;
                        c = string.CompareOrdinal(a.Parent?.Point, b.Parent?.Point);
                        if (c != 0) return c;
                    }
                    int d = a.Order.CompareTo(b.Order);
                    return d != 0 ? d : pos[a].CompareTo(pos[b]);
                });
                // Sibling numbers count within one (parent, point) group.
                string group = null;
                int k = 0;
                for (int i = 0; i < sibs.Count; i++)
                {
                    string g = sibs[i].Parent?.Point;
                    if (i == 0 || !string.Equals(g, group, StringComparison.Ordinal)) { group = g; k = 0; }
                    ord[sibs[i]] = k++;
                }
            }

            // Pre-order with an explicit stack (a hostile file may hold a very deep chain).
            void Visit(SceneNode start)
            {
                var stack = new Stack<SceneNode>();
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    if (!done.Add(n)) continue;
                    result.Add(n);
                    if (!children.TryGetValue(n.Id, out var kids)) continue;
                    Number(kids, true);
                    for (int i = kids.Count - 1; i >= 0; i--) stack.Push(kids[i]);
                }
            }

            Number(roots, false);
            foreach (var r in roots) Visit(r);
            // Unreachable nodes (a cycle in a hand-built model): roots after the rest, in list order.
            foreach (var n in nodes)
                if (!done.Contains(n)) { ord[n] = 0; Visit(n); }
            return result;
        }

        /// <summary>0 for no point, 1..54 for an AccessoryParentKey name, 55 for an unknown name.</summary>
        static int PointRank(string point) => point == null ? 0 : DocEnum.AttachPointValue(point) is int v && v > 0 ? v : 55;
    }

    /// <summary>A scene node: the envelope plus the kind section <see cref="State"/>.</summary>
    public sealed class SceneNode : DocObject
    {
        public const string KindChara = "chara", KindItem = "item", KindLight = "light", KindFolder = "folder", KindRoute = "route", KindCamera = "camera";

        /// <summary>Stable identity; required.</summary>
        public string Id;
        /// <summary>Tree label; null = default label (chara: profile.name; item, light: the list row name).</summary>
        public string Name;
        /// <summary>Null = scene root.</summary>
        public NodeParent Parent;
        /// <summary>Position among siblings (renumbered 0..n−1 on read and write).</summary>
        public int Order;
        public NodeTransform Transform = new NodeTransform();
        /// <summary>Own flag; effective visibility = own AND every ancestor's.</summary>
        public bool Visible = true;
        /// <summary>(R) view state.</summary>
        public NodeUi Ui = new NodeUi();
        /// <summary>The kind section; its <see cref="NodeState.Kind"/> is the node's `kind`. Required.</summary>
        public NodeState State;
        /// <summary>Node-scope inline ext.</summary>
        public ExtInline Ext = new ExtInline();

        public string Kind => State?.Kind;

        public SceneNode() { }
        public SceneNode(string id, NodeState state) { Id = id; State = state; }

        internal override ExtInline InlineExt => Ext;

        /// <summary>Reads one node; an unknown kind becomes an <see cref="OpaqueNode"/> (reported `scene.unknownKind`).</summary>
        internal static SceneNode Read(DocReader r)
        {
            var n = new SceneNode();
            n.ReadFrom(r);
            return n;
        }

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            string kind = r.Str("kind");
            State = NodeState.Create(kind);
            if (State == null)
            {
                Parent = ReadNew<NodeParent>(r.ObjN("parent"));
                Order = r.Int("order", 0);
                Ext = r.Ext();
                var raw = r.Detach();
                raw.Remove("ext");
                State = new OpaqueNode { KindName = kind, Raw = raw };
                r.Report(FindingSeverity.Warning, "scene.unknownKind", "kind", "node " + Id + " of unknown kind \"" + kind + "\" kept unchanged and not built");
                return;
            }
            Name = r.StrN("name");
            Parent = ReadNew<NodeParent>(r.ObjN("parent"));
            Order = r.Int("order", Order);
            Transform.ReadFrom(r.Obj("transform"));
            Visible = r.Bool("visible", Visible);
            Ui.IsChara = State is CharaNodeState;
            Ui.ReadFrom(r.Obj("ui"));
            if (State is RouteNodeState route) route.NodeId = Id;   // the start point a pointless route gets is derived from it
            State.ReadFrom(r.ObjReq(kind));
            Ext = r.Ext();
        }

        internal override void WriteMembers(CanonicalJsonWriter w) => WriteMembers(w, Order);

        /// <summary>Writes the node with sibling number <paramref name="order"/> (the document renumbers on write).</summary>
        internal void Write(CanonicalJsonWriter w, int order)
        {
            w.StartObject();
            WriteMembers(w, order);
            w.Leftovers(Unknown);
            w.Ext(Ext);
            w.EndObject();
        }

        void WriteMembers(CanonicalJsonWriter w, int order)
        {
            if (State == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "node " + Id + " has no kind section", w.Entry, w.Path);
            if (State is OpaqueNode o)
            {
                // The raw members in read order; only the envelope members the reader may have repaired are replaced.
                foreach (var p in o.Raw.Properties())
                {
                    switch (p.Name)
                    {
                        case "id": ReqId(w, "id", Id); break;
                        case "parent": WriteN(w, "parent", Parent); break;
                        case "order": w.Int("order", order); break;
                        default: w.Token(p.Name, p.Value); break;
                    }
                }
                return;
            }
            ReqId(w, "id", Id);
            ReqStr(w, "kind", Kind);
            w.Str("name", Name);
            WriteN(w, "parent", Parent);
            w.Int("order", order);
            Transform.WriteTo(w, "transform");
            w.Bool("visible", Visible);
            Ui.IsChara = State is CharaNodeState;
            Ui.WriteTo(w, "ui");
            State.WriteTo(w, Kind);
        }
    }

    /// <summary>`parent`: `point` names a character attach point (AccessoryParentKey name); null for other parents
    /// (the parent's attach transform). Unknown point names are kept.</summary>
    public sealed class NodeParent : DocObject
    {
        /// <summary>Required.</summary>
        public string Node;
        public string Point;

        public NodeParent() { }
        public NodeParent(string node, string point = null) { Node = node; Point = point; }

        internal override void ReadMembers(DocReader r)
        {
            Node = r.Id("node");
            Point = r.StrN("point");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "node", Node);
            w.Str("point", Point);
        }
    }

    /// <summary>`transform`: local pos (scene units, 0.1 m) and Euler rot in the parent's attach transform; with
    /// `inheritScale` false `scale` is the world scale. Kinds without a scale channel write (1, 1, 1).</summary>
    public sealed class NodeTransform : DocObject
    {
        public Vec3 Pos = Vec3.Zero;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rot = Vec3.Zero;
        public Vec3 Scale = Vec3.One;
        public bool InheritScale;

        internal override void ReadMembers(DocReader r)
        {
            Pos = r.V3("pos", Pos);
            Rot = r.Euler("rot", Rot);
            Scale = r.V3("scale", Scale);
            InheritScale = r.Bool("inheritScale", InheritScale);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("pos", Pos);
            w.Euler("rot", Rot);
            w.V3("scale", Scale);
            w.Bool("inheritScale", InheritScale);
        }
    }

    /// <summary>`ui` (R): tree view state. Character nodes add the expanded attach groups (0..9) and points (written
    /// ascending / ordinal).</summary>
    public sealed class NodeUi : DocObject
    {
        public bool Expanded;
        /// <summary>Character nodes only.</summary>
        public List<int> ExpandedGroups = new List<int>();
        /// <summary>Character nodes only.</summary>
        public List<string> ExpandedPoints = new List<string>();

        /// <summary>Set by the node before reading and writing: whether the character members belong to the schema.</summary>
        internal bool IsChara;

        internal override void ReadMembers(DocReader r)
        {
            Expanded = r.Bool("expanded", Expanded);
            if (!IsChara) return;
            ExpandedGroups = new List<int>(r.IntArray("expandedGroups", ExpandedGroups.ToArray()));
            ExpandedPoints = new List<string>(r.StrArray("expandedPoints", ExpandedPoints.ToArray()));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("expanded", Expanded);
            if (!IsChara) return;
            var groups = ExpandedGroups.ToArray();
            Array.Sort(groups);
            w.IntArray("expandedGroups", groups);
            var points = ExpandedPoints.ToArray();
            Array.Sort(points, StringComparer.Ordinal);
            w.StrArray("expandedPoints", points);
        }
    }
}
