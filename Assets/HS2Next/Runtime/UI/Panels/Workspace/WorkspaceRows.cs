// The rows of the workspace tree (U12): node rows in SceneModel order; a character's children
// sit under non-selectable attach-group and attach-point rows that exist only when the point has children (vanilla's 64 rows
// per character are not reproduced; while a drag hovers a character row its attach points are offered in a pop-up list
// instead). Row ids are ints (TreeView ids), stable for the session per node id / group key; the search filter keeps the
// matching rows and their ancestors. Pure logic (no UI Toolkit): the panel turns the rows into TreeViewItemData. The drop
// check mirrors the move command's validation (NodeRules) so the tree can show the reason before the drop.
// Routes (Phase 3.6, R26): a route row shows its riders as children, then a collapsible
// group row "Points (n)" with one row per point ("Start", "Point 1" …, renumbered by index); point rows take no children
// and cannot be dragged.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.UI.Core;

namespace HS2Next.UI.Panels
{
    public enum TreeRowKind { Node, Group, Point, RoutePoints, RoutePoint }

    public sealed class TreeRow
    {
        public int Id;
        public TreeRowKind Kind;
        /// <summary>The node (Node rows) or the character (Group / Point rows).</summary>
        public string NodeId;
        /// <summary>Group rows: the AccessoryPointGroup number; else -1.</summary>
        public int GroupNo = -1;
        /// <summary>Point rows: the attach point name; RoutePoint rows: the route point's id.</summary>
        public string Point;
        /// <summary>RoutePoint rows: the point's index in route.points (0 = the start point); RoutePoints rows: the count.</summary>
        public int Index = -1;
        public int Depth;
        public readonly List<TreeRow> Children = new List<TreeRow>();
        public TreeRow Parent;
        /// <summary>The text filter matched this row itself (not only a descendant).</summary>
        public bool Matched;

        public override string ToString() => Kind + " " + (Kind == TreeRowKind.Node ? NodeId : NodeId + "/" + (Point ?? (Kind == TreeRowKind.RoutePoints ? "points" : GroupNo.ToString())));
    }

    public sealed class WorkspaceRows
    {
        readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int nextId = 1;
        readonly Dictionary<int, TreeRow> byId = new Dictionary<int, TreeRow>();
        readonly Dictionary<string, TreeRow> byNode = new Dictionary<string, TreeRow>(StringComparer.Ordinal);

        public readonly List<TreeRow> Roots = new List<TreeRow>();
        /// <summary>Rows built in the last <see cref="Build"/>.</summary>
        public int RowCount => byId.Count;
        public string Filter { get; private set; } = "";

        public static string GroupKey(string charaId, int groupNo) => charaId + "/g/" + groupNo;
        public static string PointKey(string charaId, string point) => charaId + "/p/" + point;
        public static string RoutePointsKey(string routeId) => routeId + "/rp";
        public static string RoutePointKey(string routeId, string pointId) => routeId + "/rp/" + pointId;

        int IdOf(string key)
        {
            if (!ids.TryGetValue(key, out int id)) ids[key] = id = nextId++;
            return id;
        }

        /// <summary>The int id of a node's row (allocated when the node had none yet).</summary>
        public int RowIdOfNode(string nodeId) => IdOf(nodeId);

        public TreeRow Row(int id) => byId.TryGetValue(id, out var r) ? r : null;
        public TreeRow RowOfNode(string nodeId) => nodeId != null && byNode.TryGetValue(nodeId, out var r) ? r : null;

        /// <summary>The row of a route point (null when the route's points group is not built, e.g. filtered out).</summary>
        public TreeRow RowOfRoutePoint(string routeId, string pointId) =>
            routeId != null && pointId != null && ids.TryGetValue(RoutePointKey(routeId, pointId), out int id) ? Row(id) : null;

        /// <summary>
        /// Rebuilds the rows. <paramref name="label"/> gives a node's tree label (null = TreeNode.Label);
        /// <paramref name="filter"/> = search text (empty = no filter).
        /// </summary>
        public void Build(SceneModel model, string filter = null, Func<TreeNode, string> label = null)
        {
            Roots.Clear();
            byId.Clear();
            byNode.Clear();
            Filter = filter ?? "";
            var q = SearchQuery.Parse(Filter);
            label = label ?? (n => n.Label);
            foreach (var n in model.Roots)
            {
                var r = BuildNode(model, n, 0, null, q, label);
                if (r != null) Roots.Add(r);
            }
        }

        TreeRow BuildNode(SceneModel model, TreeNode n, int depth, TreeRow parent, SearchQuery q, Func<TreeNode, string> label)
        {
            var row = new TreeRow { Id = IdOf(n.Id), Kind = TreeRowKind.Node, NodeId = n.Id, Depth = depth, Parent = parent };
            row.Matched = q.IsEmpty || q.Matches(SearchText.Normalize(label(n)), null, null);
            if (n.Kind == NodeKind.Chara) BuildCharaChildren(model, n, row, q, label);
            else
                foreach (var c in n.Children)
                {
                    var cr = BuildNode(model, c, depth + 1, row, q, label);
                    if (cr != null) row.Children.Add(cr);
                }
            if (n.Kind == NodeKind.Route && (q.IsEmpty || row.Matched)) BuildRoutePoints(n, row);
            if (!row.Matched && row.Children.Count == 0) return null;
            Register(row);
            return row;
        }

        void BuildCharaChildren(SceneModel model, TreeNode n, TreeRow row, SearchQuery q, Func<TreeNode, string> label)
        {
            var table = model.AttachPoints ?? AttachPointTable.Vanilla;
            var groupRows = new Dictionary<int, TreeRow>();
            var pointRows = new Dictionary<string, TreeRow>(StringComparer.Ordinal);
            foreach (var c in n.Children)
            {
                if (c.Point == null)
                {
                    var direct = BuildNode(model, c, row.Depth + 1, row, q, label);
                    if (direct != null) row.Children.Add(direct);
                    continue;
                }
                var g = table.GroupOf(c.Point);
                int gno = g?.No ?? -1;   // -1 = a stored point no group offers (kept verbatim)
                if (!groupRows.TryGetValue(gno, out var gr))
                {
                    gr = new TreeRow { Id = IdOf(GroupKey(n.Id, gno)), Kind = TreeRowKind.Group, NodeId = n.Id, GroupNo = gno, Depth = row.Depth + 1, Parent = row };
                    groupRows[gno] = gr;
                }
                if (!pointRows.TryGetValue(c.Point, out var pr))
                {
                    pr = new TreeRow { Id = IdOf(PointKey(n.Id, c.Point)), Kind = TreeRowKind.Point, NodeId = n.Id, GroupNo = gno, Point = c.Point, Depth = row.Depth + 2, Parent = gr };
                    pointRows[c.Point] = pr;
                    gr.Children.Add(pr);
                }
                var cr = BuildNode(model, c, row.Depth + 3, pr, q, label);
                if (cr != null) pr.Children.Add(cr);
            }
            // groups in table order (canonical child order already sorts the points by rank)
            var order = new List<int>(groupRows.Keys);
            order.Sort((a, b) => (a < 0 ? int.MaxValue : a).CompareTo(b < 0 ? int.MaxValue : b));
            foreach (int gno in order)
            {
                var gr = groupRows[gno];
                gr.Children.RemoveAll(p => p.Children.Count == 0);
                if (gr.Children.Count == 0) continue;
                foreach (var p in gr.Children) Register(p);
                Register(gr);
                row.Children.Add(gr);
            }
        }

        /// <summary>The "Points (n)" group row and its point rows under a route row (after the riders).</summary>
        void BuildRoutePoints(TreeNode n, TreeRow row)
        {
            if (!(n.State is HS2Next.Save.RouteNodeState st) || st.Points == null) return;
            var g = new TreeRow { Id = IdOf(RoutePointsKey(n.Id)), Kind = TreeRowKind.RoutePoints, NodeId = n.Id, Depth = row.Depth + 1, Parent = row, Index = st.Points.Count };
            for (int i = 0; i < st.Points.Count; i++)
            {
                var p = st.Points[i];
                if (p?.Id == null) continue;
                var pr = new TreeRow { Id = IdOf(RoutePointKey(n.Id, p.Id)), Kind = TreeRowKind.RoutePoint, NodeId = n.Id, Point = p.Id, Index = i, Depth = row.Depth + 2, Parent = g };
                g.Children.Add(pr);
                Register(pr);
            }
            Register(g);
            row.Children.Add(g);
        }

        void Register(TreeRow r)
        {
            byId[r.Id] = r;
            if (r.Kind == TreeRowKind.Node) byNode[r.NodeId] = r;
        }

        /// <summary>
        /// The node rows in visible order (pre-order, collapsed rows' descendants skipped). <paramref name="expanded"/> says whether
        /// a row is open (a filter shows everything open).
        /// </summary>
        public List<string> VisibleNodes(Func<TreeRow, bool> expanded)
        {
            var l = new List<string>();
            void Walk(TreeRow r)
            {
                if (r.Kind == TreeRowKind.Node) l.Add(r.NodeId);
                if (r.Children.Count == 0) return;
                if (Filter.Length == 0 && !expanded(r)) return;
                foreach (var c in r.Children) Walk(c);
            }
            foreach (var r in Roots) Walk(r);
            return l;
        }

        /// <summary>Shift+click range: every visible node from the primary to the clicked one, inclusive, primary first.</summary>
        public static List<string> Range(IReadOnlyList<string> visible, string primary, string clicked)
        {
            var l = new List<string>();
            int a = primary == null ? -1 : IndexOf(visible, primary), b = IndexOf(visible, clicked);
            if (b < 0) return l;
            if (a < 0) { l.Add(clicked); return l; }
            l.Add(primary);
            int step = b >= a ? 1 : -1;
            for (int i = a + step; i != b + step; i += step) l.Add(visible[i]);
            return l;
        }

        static int IndexOf(IReadOnlyList<string> l, string id)
        {
            for (int i = 0; i < l.Count; i++) if (string.Equals(l[i], id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// Why <paramref name="nodeId"/> cannot move under <paramref name="parentId"/> (null = root) at <paramref name="point"/>, or
        /// null when the move is allowed (the move command's rules: unknown kinds and routes stay, no cycles, parent kind and
        /// attach point, the route rule). A reorder within the same group is always allowed.
        /// </summary>
        public static string MoveProblem(SceneModel model, string nodeId, string parentId, string point)
        {
            var n = model.Find(nodeId);
            if (n == null) return "not in the scene";
            var parent = parentId == null ? null : model.Find(parentId);
            if (parentId != null && parent == null) return "parent not in the scene";
            bool reorder = string.Equals(n.Parent?.Id, parentId, StringComparison.Ordinal) && string.Equals(n.Point, point, StringComparison.Ordinal);
            if (reorder) return null;
            if (n.Kind == NodeKind.Opaque) return "nodes of an unknown kind cannot be re-parented";
            if (n.Kind == NodeKind.Route && !model.Rules.AllowRouteReparent) return "routes cannot be re-parented";
            if (parent != null && NodeRules.IsInSubtree(parent, n)) return "a node cannot move under itself";
            try
            {
                NodeRules.CheckParent(model, parent, point);
                NodeRules.CheckRouteRule(n, parent);
            }
            catch (CommandException e) { return e.Message; }
            return null;
        }

        /// <summary>The selected nodes whose ancestors are not selected too (a drag of a parent and its child moves the parent).</summary>
        public static List<string> TopMost(SceneModel model, IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids, StringComparer.Ordinal);
            var l = new List<string>();
            foreach (var id in set)
            {
                var n = model.Find(id);
                if (n == null) continue;
                bool covered = false;
                for (var p = n.Parent; p != null; p = p.Parent) if (set.Contains(p.Id)) { covered = true; break; }
                if (!covered) l.Add(id);
            }
            // keep tree order
            var pre = model.PreOrder();
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < pre.Count; i++) rank[pre[i].Id] = i;
            l.Sort((a, b) => rank[a].CompareTo(rank[b]));
            return l;
        }
    }
}
