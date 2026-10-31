// The workspace tree panel (U11, U12): a TreeView over WorkspaceRows (node rows,
// attach-group / attach-point rows only where a point has children), rows with expand arrow, eye toggle (lights: shown
// disabled, their visibility has no effect), kind glyph, label, FK / IK badges and a findings badge. Selection is the
// session's SelectionModel (the TreeView's own selection is off; the rows show the model's selection), with the click
// rules and, in the hs2studio preset, vanilla's P / O / X modifier clicks. A single click only selects; a double
// click on a node row selects it and shows the inspector (its tab becomes the shown one, also when the inspector shares
// the workspace's region), and so does Enter on the focused tree. Rename in place (F2, the context menu's Rename),
// drag to parent / reorder (drop on a row = parent, between rows = reorder, empty area = root; a character row asks for
// the attach point), context menu, search filter keeping ancestors, collapse / expand all. Every document change goes
// through StudioOps; expand state is view state written with Record = None.
// Routes (Phase 3.6 package R, R26): a route row has a "Points (n)" group with one row per
// point; clicking a point row selects the route and sub-selects the point (as FK rows), its context menu offers Add / Delete
// point, Reset aid, Play / Stop; point rows cannot be dragged or dropped on; the group's open state is view-only (not
// saved: `ui.expandedGroups` is a character member). CheckKind follows TreeRules.AllowRouteReparent.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class WorkspacePanel : VisualElement, IDropTarget
    {
        public const string PanelId = "workspace";
        const int RowHeight = 22;

        readonly UiContext ctx;
        readonly WorkspaceRows rows = new WorkspaceRows();
        readonly TreeView tree;
        readonly TextField search;
        readonly Label emptyHint;
        bool rebuildQueued, refreshQueued, syncingExpand;
        string renamingId;
        TextField renameField;
        HashSet<string> isolated;      // view-only isolate set (null = off)
        readonly HashSet<string> collapsedRoutePoints = new HashSet<string>(StringComparer.Ordinal);   // routes whose "Points (n)" row is closed (view only)
        readonly Dictionary<VisualElement, RowView> views = new Dictionary<VisualElement, RowView>();
        // drag-hover state
        int hoverRowId = -1;
        string hoverZone;

        /// <summary>Milliseconds of the last full rebuild (≤ 50 ms at 2,000 nodes).</summary>
        public double LastRebuildMs { get; private set; }
        public int RebuildCount { get; private set; }
        public WorkspaceRows Rows => rows;
        public TreeView Tree => tree;
        /// <summary>The visible node ids in row order (Shift ranges, arrows, Ctrl+A with tree focus).</summary>
        public List<string> VisibleNodeIds() => rows.VisibleNodes(r => tree.IsExpanded(r.Id));
        public static WorkspacePanel Current { get; private set; }

        sealed class RowView
        {
            public VisualElement Root;
            public Button Eye;
            public Label Glyph, Text, Fk, Ik, Warn;
            public TreeRow Row;
        }

        public WorkspacePanel(UiContext ctx)
        {
            this.ctx = ctx;
            Current = this;
            name = "workspace-panel"; PanelKit.Style(this);
            AddToClassList("hs-panel");
            AddToClassList("hs-workspace");
            focusable = true;

            var header = new VisualElement { name = "workspace-header" };
            header.AddToClassList("hs-panel__header");
            search = PanelKit.SearchField("workspace-search", q => { rows.Build(ctx.Model, q, LabelOf); Rebuild(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            header.Add(IconButton("workspace-collapse-all", "⊟", "workspace.collapseAll", () => { tree.CollapseAll(); WriteAllExpanded(false); }));
            header.Add(IconButton("workspace-expand-all", "⊞", "workspace.expandAll", () => { tree.ExpandAll(); WriteAllExpanded(true); }));
            header.Add(IconButton("workspace-add-folder", "▤+", "action.add.folder", () => ctx.Registry.Run(ActionIds.AddFolder, "panel")));
            header.Add(IconButton("workspace-add-camera", "◉+", "action.add.camera", () => ctx.Registry.Run(ActionIds.AddCamera, "panel")));
            Add(header);

            tree = new TreeView
            {
                name = "workspace-tree",
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                reorderable = false,
                horizontalScrollingEnabled = true,
                makeItem = MakeRow,
                bindItem = BindRow,
                unbindItem = UnbindRow,
            };
            tree.AddToClassList("hs-grow");
            tree.itemExpandedChanged += OnExpandedChanged;
            tree.RegisterCallback<PointerDownEvent>(OnEmptyPointerDown);
            Add(tree);
            // Enter on the focused tree (no rename open: the rename field takes its own Enter) shows the inspector
            RegisterCallback<KeyDownEvent>(e =>
            {
                if ((e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) || e.modifiers != EventModifiers.None || renamingId != null) return;
                if (ctx.Selection.Count == 0) return;
                ShowInspector();
                e.StopPropagation();
            });

            emptyHint = Loc.Label("workspace.empty");
            emptyHint.AddToClassList("hs-hint");
            Add(emptyHint);

            ctx.SceneChanged += OnSceneChanged;
            ctx.SceneCleared += QueueRebuild;
            ctx.SceneLoaded += _ => QueueRebuild();
            ctx.SelectionChanged += _ => { QueueRefresh(); ScrollToPrimary(); };
            ctx.SubSelectionChanged += s => { if (s != null && StudioOps.TryParseRouteSub(s, out var rid, out _, out _)) collapsedRoutePoints.Remove(rid); QueueRefresh(); };
            ctx.FindingAdded += f => { if (f.NodeId != null) QueueRefresh(); };
            RegisterCallback<AttachToPanelEvent>(_ => DragController.Register(this));
            PanelKit.EveryFrame(this, FlushQueued);
            RegisterCallback<DetachFromPanelEvent>(_ => DragController.Unregister(this));
            rows.Build(ctx.Model, "", LabelOf);
            Rebuild();
        }

        Button IconButton(string id, string glyph, string tipKey, Action click)
        {
            var b = new Button(click) { name = id, text = glyph };
            b.AddToClassList("hs-icon-button");
            Loc.Tooltip(b, tipKey);
            return b;
        }

        string LabelOf(TreeNode n) => n.Label;

        // ------------------------------------------------------------------ rebuild / refresh

        void OnSceneChanged(SceneChangeBatch b)
        {
            if (b.IsStructural) { QueueRebuild(); return; }
            foreach (var c in b.Changes)
            {
                // a route point added / removed (or the whole section replaced): the "Points (n)" rows change
                if (c.Kind == SceneChangeKind.NodeChanged && (c.Path == "route.points" || c.Path == "route")) { QueueRebuild(); return; }
                if (c.Kind == SceneChangeKind.AppearanceChanged) { QueueRebuild(); return; }   // labels may change (search)
                if (c.Kind == SceneChangeKind.NodeChanged && c.Path != null &&
                    (c.Path == "name" || c.Path.StartsWith("visible", StringComparison.Ordinal) || c.Path.StartsWith("ui", StringComparison.Ordinal) ||
                     c.Path.StartsWith("chara.fk", StringComparison.Ordinal) || c.Path.StartsWith("chara.ik", StringComparison.Ordinal) ||
                     c.Path.StartsWith("camera", StringComparison.Ordinal) || c.Path == "chara" || c.Path == "camera"))
                {
                    if (c.Path == "name" && rows.Filter.Length > 0) { QueueRebuild(); return; }
                    QueueRefresh();
                }
            }
            if (isolated != null) Isolate.Apply(ctx, isolated);
        }

        // rebuilds / refreshes run once per frame at most (PanelTicker; scene events may not call the UI back synchronously)
        void QueueRebuild() => rebuildQueued = true;

        void QueueRefresh() => refreshQueued = true;

        void FlushQueued()
        {
            if (rebuildQueued)
            {
                rebuildQueued = false;
                refreshQueued = false;
                rows.Build(ctx.Model, rows.Filter, LabelOf);
                Rebuild();
            }
            else if (refreshQueued)
            {
                refreshQueued = false;
                tree.RefreshItems();
            }
        }

        /// <summary>Rebuilds the TreeView from <see cref="rows"/> (the rows must be built) and re-applies the expand state.</summary>
        public void Rebuild()
        {
            var sw = Stopwatch.StartNew();
            var items = new List<TreeViewItemData<TreeRow>>(rows.Roots.Count);
            foreach (var r in rows.Roots) items.Add(Item(r));
            tree.SetRootItems(items);
            tree.Rebuild();
            SyncExpanded();
            emptyHint.style.display = ctx.Model.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            LastRebuildMs = sw.Elapsed.TotalMilliseconds;
            RebuildCount++;
        }

        /// <summary>Synchronous rebuild (tests, after a programmatic change).</summary>
        public void RebuildNow()
        {
            rebuildQueued = false;
            rows.Build(ctx.Model, rows.Filter, LabelOf);
            Rebuild();
        }

        static TreeViewItemData<TreeRow> Item(TreeRow r)
        {
            List<TreeViewItemData<TreeRow>> ch = null;
            if (r.Children.Count > 0)
            {
                ch = new List<TreeViewItemData<TreeRow>>(r.Children.Count);
                foreach (var c in r.Children) ch.Add(Item(c));
            }
            return new TreeViewItemData<TreeRow>(r.Id, r, ch);
        }

        bool StoredExpanded(TreeRow r)
        {
            var n = ctx.Model.Find(r.NodeId);
            if (n == null) return true;
            switch (r.Kind)
            {
                case TreeRowKind.Node: return n.Doc.Ui.Expanded;
                case TreeRowKind.Group: return n.Doc.Ui.ExpandedGroups.Contains(r.GroupNo);
                case TreeRowKind.RoutePoints: return !collapsedRoutePoints.Contains(r.NodeId);
                case TreeRowKind.RoutePoint: return false;
                default: return n.Doc.Ui.ExpandedPoints.Contains(r.Point);
            }
        }

        void SyncExpanded()
        {
            syncingExpand = true;
            try
            {
                bool all = rows.Filter.Length > 0;
                void Walk(TreeRow r)
                {
                    if (r.Children.Count == 0) return;
                    bool want = all || StoredExpanded(r);
                    if (want != tree.IsExpanded(r.Id))
                    {
                        if (want) tree.ExpandItem(r.Id, false, false); else tree.CollapseItem(r.Id, false, false);
                    }
                    foreach (var c in r.Children) Walk(c);
                }
                foreach (var r in rows.Roots) Walk(r);
                tree.RefreshItems();
            }
            finally { syncingExpand = false; }
        }

        void OnExpandedChanged(TreeViewExpansionChangedArgs a)
        {
            if (syncingExpand || rows.Filter.Length > 0) return;
            var r = rows.Row(a.id);
            if (r == null) return;
            WriteExpanded(r, a.isExpanded);
        }

        static ExecuteOptions ViewState => new ExecuteOptions { Record = HistoryMode.None, Source = CommandSource.User };

        void WriteExpanded(TreeRow r, bool open)
        {
            var n = ctx.Model.Find(r.NodeId);
            if (n == null) return;
            var ui = n.Doc.Ui;
            switch (r.Kind)
            {
                case TreeRowKind.RoutePoints:
                    if (open) collapsedRoutePoints.Remove(r.NodeId); else collapsedRoutePoints.Add(r.NodeId);
                    return;
                case TreeRowKind.RoutePoint:
                    return;
                case TreeRowKind.Node:
                    if (ui.Expanded != open) ctx.Session.Set(n.Id, "ui.expanded", open, ViewState);
                    break;
                case TreeRowKind.Group:
                {
                    var l = new List<int>(ui.ExpandedGroups);
                    if (open == l.Contains(r.GroupNo)) return;
                    if (open) l.Add(r.GroupNo); else l.Remove(r.GroupNo);
                    l.Sort();
                    ctx.Session.SetJson(n.Id, "ui.expandedGroups", "[" + string.Join(",", l) + "]", ViewState);
                    break;
                }
                default:
                {
                    var l = new List<string>(ui.ExpandedPoints);
                    if (open == l.Contains(r.Point)) return;
                    if (open) l.Add(r.Point); else l.Remove(r.Point);
                    l.Sort(StringComparer.Ordinal);
                    ctx.Session.SetJson(n.Id, "ui.expandedPoints", Newtonsoft.Json.JsonConvert.SerializeObject(l), ViewState);
                    break;
                }
            }
        }

        void WriteAllExpanded(bool open)
        {
            if (rows.Filter.Length > 0) return;
            void Walk(TreeRow r)
            {
                if (r.Children.Count > 0) WriteExpanded(r, open);
                foreach (var c in r.Children) Walk(c);
            }
            foreach (var r in rows.Roots) Walk(r);
        }

        /// <summary>Opens the rows above a node (after a drop or a selection from elsewhere).</summary>
        public void Reveal(string nodeId)
        {
            var r = rows.RowOfNode(nodeId);
            for (var p = r?.Parent; p != null; p = p.Parent)
                if (!tree.IsExpanded(p.Id)) { tree.ExpandItem(p.Id, false, false); WriteExpanded(p, true); }
            tree.RefreshItems();
        }

        void ScrollToPrimary()
        {
            string p = ctx.Selection.Primary;
            if (p == null || rows.RowOfNode(p) == null) return;
            schedule.Execute(() => { try { tree.ScrollToItemById(rows.RowOfNode(p)?.Id ?? -1); } catch (Exception) { } });
        }

        // ------------------------------------------------------------------ rows

        VisualElement MakeRow()
        {
            var v = new RowView { Root = new VisualElement() };
            v.Root.AddToClassList("hs-tree-row");
            v.Eye = new Button { text = "●" };
            v.Eye.AddToClassList("hs-tree-row__eye");
            v.Eye.clicked += () => OnEye(v);
            v.Glyph = PanelKit.Text("", "hs-tree-row__glyph");
            v.Text = PanelKit.Text("", "hs-tree-row__label");
            v.Fk = PanelKit.Text("FK", "hs-tree-row__badge");
            v.Ik = PanelKit.Text("IK", "hs-tree-row__badge");
            v.Warn = PanelKit.Text("⚠", "hs-tree-row__warn");
            v.Root.Add(v.Eye); v.Root.Add(v.Glyph); v.Root.Add(v.Text); v.Root.Add(v.Fk); v.Root.Add(v.Ik); v.Root.Add(v.Warn);
            v.Root.RegisterCallback<PointerDownEvent>(e => OnRowPointerDown(v, e));
            v.Root.RegisterCallback<PointerUpEvent>(e => OnRowPointerUp(v, e));
            DragController.MakeSource(v.Root, e => DragOf(v, e));
            views[v.Root] = v;
            return v.Root;
        }

        void BindRow(VisualElement el, int index)
        {
            var v = views[el];
            var r = tree.GetItemDataForIndex<TreeRow>(index);
            v.Row = r;
            el.name = "tree-row-" + (r.Kind == TreeRowKind.Node ? r.NodeId : r.Kind == TreeRowKind.Group ? r.NodeId + "-g" + r.GroupNo
                : r.Kind == TreeRowKind.RoutePoints ? r.NodeId + "-points" : r.Kind == TreeRowKind.RoutePoint ? r.NodeId + "-rp-" + r.Point : r.NodeId + "-p-" + r.Point);
            var n = ctx.Model.Find(r.NodeId);
            foreach (var c in new[] { "--selected", "--primary", "--group", "--point", "--folder", "--camera", "--active-camera", "--hidden", "--drop-on", "--drop-before", "--drop-after", "--refused" })
                el.RemoveFromClassList("hs-tree-row" + c);
            if (n == null) { v.Text.text = "?"; return; }
            if (r.Kind == TreeRowKind.RoutePoints || r.Kind == TreeRowKind.RoutePoint) { BindRouteRow(v, el, r); return; }
            if (r.Kind != TreeRowKind.Node)
            {
                el.AddToClassList(r.Kind == TreeRowKind.Group ? "hs-tree-row--group" : "hs-tree-row--point");
                v.Eye.style.visibility = Visibility.Hidden;
                v.Glyph.text = "";
                v.Text.text = r.Kind == TreeRowKind.Group
                    ? L10n.Format("workspace.groupRow", GroupName(r.GroupNo))
                    : L10n.Format("workspace.pointRow", PointName(r.Point));
                v.Fk.style.display = v.Ik.style.display = v.Warn.style.display = DisplayStyle.None;
                ApplyHover(el, r);
                return;
            }
            bool selected = ctx.Selection.Contains(n.Id);
            el.EnableInClassList("hs-tree-row--selected", selected);
            el.EnableInClassList("hs-tree-row--primary", selected && ctx.Selection.Primary == n.Id);
            el.EnableInClassList("hs-tree-row--folder", n.Kind == NodeKind.Folder);
            el.EnableInClassList("hs-tree-row--camera", n.Kind == NodeKind.Camera);
            el.EnableInClassList("hs-tree-row--active-camera", n.State is CameraNodeState cs && cs.Active);
            el.EnableInClassList("hs-tree-row--hidden", !n.EffectiveVisible);
            v.Glyph.text = PanelKit.Glyph(n);
            v.Text.text = renamingId == n.Id ? "" : n.Label;
            v.Text.tooltip = n.Label;
            v.Eye.style.visibility = Visibility.Visible;
            v.Eye.text = n.Doc.Visible ? "●" : "○";
            bool light = n.Kind == NodeKind.Light;
            bool parentHidden = n.Parent != null && !n.Parent.EffectiveVisible;
            v.Eye.SetEnabled(!light && !parentHidden);
            v.Eye.tooltip = light ? L10n.Get("workspace.lightEyeTip") : parentHidden ? L10n.Get("workspace.parentHiddenTip") : L10n.Get("workspace.eyeTip");
            var cn = n.State as CharaNodeState;
            v.Fk.style.display = cn != null && cn.Fk.Enabled ? DisplayStyle.Flex : DisplayStyle.None;
            v.Ik.style.display = cn != null && cn.Ik.Enabled ? DisplayStyle.Flex : DisplayStyle.None;
            bool warn = ctx.Findings.Any(f => f.NodeId == n.Id && f.Severity >= HS2Next.Save.FindingSeverity.Warning);
            v.Warn.style.display = warn ? DisplayStyle.Flex : DisplayStyle.None;
            if (warn) v.Warn.tooltip = string.Join("\n", ctx.Findings.Where(f => f.NodeId == n.Id).Select(f => f.Code + ": " + f.Message).Take(5));
            ApplyHover(el, r);
            if (renamingId == n.Id) AttachRename(v);
        }

        void UnbindRow(VisualElement el, int index)
        {
            if (views.TryGetValue(el, out var v)) v.Row = null;
            if (renameField != null && renameField.parent == el) renameField.RemoveFromHierarchy();
        }

        /// <summary>A route's "Points (n)" row or one of its point rows (Phase 3.6 package R).</summary>
        void BindRouteRow(RowView v, VisualElement el, TreeRow r)
        {
            el.AddToClassList(r.Kind == TreeRowKind.RoutePoints ? "hs-tree-row--group" : "hs-tree-row--point");
            v.Eye.style.visibility = Visibility.Hidden;
            v.Fk.style.display = v.Ik.style.display = v.Warn.style.display = DisplayStyle.None;
            if (r.Kind == TreeRowKind.RoutePoints)
            {
                v.Glyph.text = "";
                v.Text.text = L10n.Format("route.points", r.Index);
            }
            else
            {
                v.Glyph.text = r.Index == 0 ? "S" : r.Index.ToString();
                v.Text.text = RoutePointLabel(r.Index);
                string sub = ctx.SubSelection;
                bool on = sub != null && StudioOps.TryParseRouteSub(sub, out var rid, out var pid, out _) && rid == r.NodeId && pid == r.Point;
                el.EnableInClassList("hs-tree-row--selected", on);
                el.EnableInClassList("hs-tree-row--primary", on);
            }
            v.Text.tooltip = v.Text.text;
            ApplyHover(el, r);
        }

        /// <summary>"Start" / "Point n" (HS2's labels, renumbered by index).</summary>
        public static string RoutePointLabel(int index) => index == 0 ? L10n.Get("route.point.start") : L10n.Format("route.point.n", index);

        public static string GroupName(int no) => no < 0 ? L10n.Get("attach.group.unknown") : L10n.Get("attach.group." + no);
        public static string PointName(string point) => L10n.TryGet("attach.point." + point, out var t) ? t : point;

        // ------------------------------------------------------------------ clicks

        void OnEye(RowView v)
        {
            var n = v.Row == null ? null : ctx.Model.Find(v.Row.NodeId);
            if (n == null || v.Row.Kind != TreeRowKind.Node) return;
            ctx.ShowResult(ctx.Ops.SetVisible(new[] { n.Id }, !n.Doc.Visible, UiContext.User()));
        }

        bool pressSelected;      // the pressed row was selected before (selection applied on pointer up when no drag)
        string pressId;

        void OnRowPointerDown(RowView v, PointerDownEvent e)
        {
            Focus();
            var r = v.Row;
            if (r == null) return;
            if (e.button == 1) { e.StopPropagation(); return; }   // context menu on up
            if (e.button != 0) return;
            if (r.Kind == TreeRowKind.RoutePoint)
            {
                ctx.SubSelection = r.NodeId + "/points/" + r.Point;   // the route becomes the sole selection (as FK rows)
                e.StopPropagation();
                return;
            }
            if (r.Kind != TreeRowKind.Node)
            {
                if (tree.IsExpanded(r.Id)) tree.CollapseItem(r.Id); else tree.ExpandItem(r.Id);
                e.StopPropagation();
                return;
            }
            if (e.clickCount == 2 && !e.shiftKey && !e.ctrlKey && !e.commandKey)
            {
                // a double click selects the node and shows the inspector (rename: F2 or the context menu)
                ctx.Selection.Set(new[] { r.NodeId });
                pressId = null;
                ShowInspector();
                e.StopPropagation();
                return;
            }
            pressId = r.NodeId;
            pressSelected = ctx.Selection.Contains(r.NodeId);
            if (!pressSelected || e.shiftKey || e.ctrlKey || e.commandKey || Hs2Modifier() != null)
            {
                Click(r.NodeId, e.ctrlKey || e.commandKey, e.shiftKey);
                pressId = null;
            }
            e.StopPropagation();
        }

        void OnRowPointerUp(RowView v, PointerUpEvent e)
        {
            var r = v.Row;
            if (r == null) return;
            if (e.button == 1 && r.Kind == TreeRowKind.Node) { ContextMenu(r.NodeId, e.position); e.StopPropagation(); return; }
            if (e.button == 1 && (r.Kind == TreeRowKind.RoutePoint || r.Kind == TreeRowKind.RoutePoints)) { RouteContextMenu(r, e.position); e.StopPropagation(); return; }
            if (e.button == 0 && pressId != null && pressId == r.NodeId && DragController.Current == null)
                Click(r.NodeId, false, false);
            pressId = null;
        }

        /// <summary>Times a double click or Enter showed the inspector (tests).</summary>
        public int InspectorShows { get; private set; }

        /// <summary>Shows the inspector panel (its region opened, its tab shown; the keyboard focus stays in the tree).</summary>
        public void ShowInspector()
        {
            InspectorShows++;
            ctx.Shell?.ShowPanel(UiShell.InspectorId);
        }

        void OnEmptyPointerDown(PointerDownEvent e)
        {
            // a click below the last row (not on a row: rows stop the event) clears the selection
            if (e.button == 0 && !e.shiftKey && !e.ctrlKey) ctx.Selection.Clear();
            Focus();
        }

        /// <summary>The hs2studio click modifier held now (click.parent / parentAll / detach / detachAll / copyTransform), or null.</summary>
        string Hs2Modifier()
        {
            foreach (var id in new[] { ActionIds.ClickParentAll, ActionIds.ClickDetachAll, ActionIds.ClickCopyTransform, ActionIds.ClickParent, ActionIds.ClickDetach })
                if (CInput.ModifierHeld(ctx, id)) return id;
            return null;
        }

        /// <summary>
        /// A click on a node row with the rules (U11: clicking the sole selected node keeps it), Ctrl toggling,
        /// Shift ranges in visible order, and the hs2studio P / O / X clicks (K7) when their keys are held.
        /// </summary>
        public void Click(string id, bool ctrl, bool shift)
        {
            string mod = Hs2Modifier();
            var sel = ctx.Selection;
            if (mod != null && ModifierClick(mod, id)) return;
            if (shift && sel.Count > 0)
            {
                string primary = sel.Primary;
                var range = WorkspaceRows.Range(VisibleNodeIds(), primary, id);
                if (ctrl) { var l = new List<string>(sel.Ids); foreach (var x in range) if (!l.Contains(x)) l.Add(x); sel.Set(l); }
                else sel.Set(range);
                return;
            }
            if (ctrl) { sel.Toggle(id); return; }
            sel.Set(new[] { id });
        }

        bool ModifierClick(string mod, string id)
        {
            var sel = ctx.Selection;
            string primary = sel.Primary;
            var clicked = ctx.Model.Find(id);
            if (clicked == null) return false;
            switch (mod)
            {
                case ActionIds.ClickParent:
                    if (primary == null) return false;   // P+click with nothing selected is a plain click (K7)
                    if (primary != id) MoveUnderAsk(new List<string> { primary }, clicked);
                    sel.Set(new[] { id });
                    return true;
                case ActionIds.ClickParentAll:
                {
                    if (primary == null) return false;
                    var others = sel.Ids.Where(x => x != primary).ToList();
                    if (!others.Contains(id) && id != primary) others.Add(id);
                    var pnode = ctx.Model.Find(primary);
                    if (pnode != null && others.Count > 0) MoveUnderAsk(others, pnode);
                    sel.Set(new[] { id });
                    return true;
                }
                case ActionIds.ClickDetach:
                    MoveToRoot(new List<string> { id });
                    sel.Set(new[] { id });
                    return true;
                case ActionIds.ClickDetachAll:
                {
                    var l = sel.Ids.ToList();
                    if (!l.Contains(id)) l.Add(id);
                    MoveToRoot(l);
                    return true;
                }
                case ActionIds.ClickCopyTransform:
                {
                    if (primary == null) return false;
                    var to = sel.Ids.Where(x => x != primary).ToList();
                    if (!to.Contains(id) && id != primary) to.Add(id);
                    if (to.Count > 0) ctx.ShowResult(ctx.Ops.CopyTransform(primary, to, UiContext.User()));
                    sel.Set(new[] { id });
                    return true;
                }
            }
            return false;
        }

        /// <summary>Moves nodes under <paramref name="parent"/> (a character asks for the attach point first) in one entry "Parent".</summary>
        public void MoveUnderAsk(List<string> ids, TreeNode parent, int index = -1)
        {
            if (parent.Kind == NodeKind.Chara)
            {
                AskPoint(parent, pos: null, point => MoveUnderAt(ids, parent, point, index));
                return;
            }
            MoveUnderAt(ids, parent, null, index);
        }

        void MoveUnderAt(List<string> ids, TreeNode parent, string point, int index)
        {
            var top = WorkspaceRows.TopMost(ctx.Model, ids);
            string problem = null;
            foreach (var id in top) { problem = WorkspaceRows.MoveProblem(ctx.Model, id, parent?.Id, point); if (problem != null) break; }
            if (problem != null) { ctx.Toasts?.Show(L10n.Format("workspace.moveRefused", problem), ToastKind.Warning); return; }
            using (var edit = ctx.BeginEdit(OpLabels.Parent))
            {
                OpResult last = null;
                int i = index;
                foreach (var id in top)
                {
                    last = ctx.Ops.Move(id, parent?.Id, point, i, true, UiContext.User(OpLabels.Parent));
                    if (!last.Ok) { ctx.ShowResult(last); edit.Cancel(); return; }
                    if (i >= 0) i++;
                }
                ctx.ShowResult(edit.Commit());
            }
            foreach (var id in top) Reveal(id);
        }

        void MoveToRoot(List<string> ids)
        {
            var top = WorkspaceRows.TopMost(ctx.Model, ids).Where(id => ctx.Model.Find(id)?.Parent != null).ToList();
            if (top.Count == 0) return;
            MoveUnderAt(top, null, null, -1);
        }

        /// <summary>The attach-point list of a character (groups as submenus); <paramref name="done"/> gets the chosen point.</summary>
        public void AskPoint(TreeNode chara, Vector2? pos, Action<string> done)
        {
            var table = ctx.Model.AttachPoints ?? AttachPointTable.Vanilla;
            var items = new List<MenuItem> { new MenuItem { Text = L10n.Format("workspace.choosePoint", chara.Label), Disabled = true } };
            foreach (var g in table.Groups)
            {
                var sub = new List<MenuItem>();
                foreach (var p in g.Points)
                {
                    string point = p;
                    if (!table.IsOffered(point)) continue;
                    sub.Add(new MenuItem { Id = "point-" + point, Text = PointName(point), Run = () => done(point) });
                }
                if (sub.Count > 0) items.Add(new MenuItem { Id = "group-" + g.No, Text = GroupName(g.No), Submenu = sub });
            }
            var at = pos ?? (rows.RowOfNode(chara.Id) is TreeRow r && ElementOf(r) is VisualElement el ? new Vector2(el.worldBound.xMin + 40, el.worldBound.yMax) : worldBound.center);
            CInput.ShowMenu(ctx, items, at, "attach-point-menu");
        }

        VisualElement ElementOf(TreeRow r)
        {
            foreach (var kv in views) if (kv.Value.Row == r && kv.Key.panel != null) return kv.Key;
            return null;
        }

        // ------------------------------------------------------------------ keyboard actions (tree focus)

        /// <summary>Moves the primary to the previous / next visible node row (tree.prev / tree.next).</summary>
        public void Step(int dir)
        {
            var vis = VisibleNodeIds();
            if (vis.Count == 0) return;
            int i = ctx.Selection.Primary == null ? -1 : vis.IndexOf(ctx.Selection.Primary);
            int j = i < 0 ? (dir > 0 ? 0 : vis.Count - 1) : Mathf.Clamp(i + dir, 0, vis.Count - 1);
            ctx.Selection.Set(new[] { vis[j] });
        }

        /// <summary>Collapses / expands the primary's row (tree.collapse / tree.expand); collapsing a closed row moves to its parent.</summary>
        public void SetPrimaryExpanded(bool open)
        {
            var r = rows.RowOfNode(ctx.Selection.Primary);
            if (r == null) return;
            if (r.Children.Count > 0 && tree.IsExpanded(r.Id) != open)
            {
                if (open) tree.ExpandItem(r.Id); else tree.CollapseItem(r.Id);
                return;
            }
            if (!open)
            {
                var p = r.Parent;
                while (p != null && p.Kind != TreeRowKind.Node) p = p.Parent;
                if (p != null) ctx.Selection.Set(new[] { p.NodeId });
            }
        }

        // ------------------------------------------------------------------ rename

        /// <summary>Starts the in-place rename of a node (F2 / the context menu; Enter commits, Esc cancels, empty = default label).</summary>
        public void BeginRename(string id)
        {
            var n = ctx.Model.Find(id);
            if (n == null) return;
            CommitRename(false);
            renamingId = id;
            Reveal(id);
            var r = rows.RowOfNode(id);
            if (r != null) tree.ScrollToItemById(r.Id);
            renameField = new TextField { name = "workspace-rename", value = n.Doc.Name ?? n.Label };
            renameField.AddToClassList("hs-tree-row__rename");
            renameField.AddToClassList(HS2Next.UI.Input.ShortcutRouter.OwnEscapeClass);   // its own Esc cancels the rename
            renameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { CommitRename(true); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Escape) { CommitRename(false); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            renameField.RegisterCallback<FocusOutEvent>(_ => schedule.Execute(() => { if (renamingId != null) CommitRename(true); }));
            tree.RefreshItems();
        }

        void AttachRename(RowView v)
        {
            if (renameField == null) return;
            if (renameField.parent != v.Root) { v.Root.Add(renameField); renameField.BringToFront(); }
            // focus one frame later (Spike S1b: a programmatic focus loses the first IME keystroke; see notes)
            renameField.schedule.Execute(() => { renameField?.Focus(); renameField?.SelectAll(); });
        }

        /// <summary>The rename in progress: (node id, field) or null.</summary>
        public TextField RenameField => renameField;

        public void CommitRename(bool apply)
        {
            if (renamingId == null) return;
            string id = renamingId;
            var f = renameField;
            renamingId = null;
            renameField = null;
            f?.RemoveFromHierarchy();
            if (apply && f != null)
            {
                var n = ctx.Model.Find(id);
                string text = f.value?.Trim();
                string name = string.IsNullOrEmpty(text) ? null : text;
                if (n != null && !string.Equals(name, n.Doc.Name, StringComparison.Ordinal) && !(name != null && n.Doc.Name == null && name == n.Label))
                    ctx.ShowResult(ctx.Ops.Rename(id, name, UiContext.User()));
            }
            tree.RefreshItems();
            Focus();
        }

        // ------------------------------------------------------------------ context menu

        public void ContextMenu(string id, Vector2 pos)
        {
            if (!ctx.Selection.Contains(id)) ctx.Selection.Set(new[] { id });
            var n = ctx.Model.Find(id);
            if (n == null) return;
            var items = new List<MenuItem>
            {
                Act("ctx-rename", ActionIds.Rename),
                Act("ctx-duplicate", ActionIds.Duplicate),
                Act("ctx-delete", ActionIds.Delete),
                MenuItem.Sep(),
                new MenuItem { Id = "ctx-visibility", Text = L10n.Get(n.Doc.Visible ? "workspace.hide" : "workspace.show"),
                    Run = () => ctx.ShowResult(ctx.Ops.SetVisible(ctx.Selection.Ids.ToList(), !n.Doc.Visible, UiContext.User())) },
                new MenuItem { Id = "ctx-isolate", Text = L10n.Get("action.view.isolate"), Checked = isolated != null, Run = ToggleIsolate },
                Act("ctx-frame", ActionIds.CameraFrame),
                MenuItem.Sep(),
                Act("ctx-copy-transform", ActionIds.CopyTransform),
                Act("ctx-paste-transform", ActionIds.PasteTransform),
                new MenuItem { Id = "ctx-detach", Text = L10n.Get("workspace.detach"), Disabled = ctx.Selection.Ids.All(x => ctx.Model.Find(x)?.Parent == null),
                    Run = () => MoveToRoot(ctx.Selection.Ids.ToList()) },
                new MenuItem { Id = "ctx-select-children", Text = L10n.Get("workspace.selectChildren"), Disabled = n.Children.Count == 0, Run = () => SelectChildren(id) },
                new MenuItem { Id = "ctx-add-folder", Text = L10n.Get("workspace.addFolderHere"),
                    Disabled = !(n.Kind == NodeKind.Folder || n.Kind == NodeKind.Item), Run = () => AddFolderUnder(id) },
            };
            if (n.Kind == NodeKind.Route)
            {
                items.Add(MenuItem.Sep());
                items.Add(Act("ctx-route-add-point", ActionIds.RouteAddPoint));
                items.Add(Act("ctx-route-play", ActionIds.RoutePlay));
                items.Add(Act("ctx-route-stop", ActionIds.RouteStop));
            }
            if (n.Kind == NodeKind.Camera)
            {
                bool active = n.State is CameraNodeState c && c.Active;
                items.Add(MenuItem.Sep());
                items.Add(new MenuItem { Id = "ctx-camera-active", Text = L10n.Get(active ? "workspace.cameraRelease" : "workspace.cameraActivate"),
                    Shortcut = ctx.Keys.ChordText(ActionIds.CameraLookThrough),
                    Run = () => ctx.ShowResult(ctx.Ops.SetActiveCamera(active ? null : id, UiContext.User())) });
                // v0.3 trial 2: the camera to the current view (position, rotation, field of view; one entry)
                items.Add(new MenuItem { Id = "ctx-camera-update", Text = L10n.Get("workspace.cameraUpdate"),
                    Run = () => ctx.ShowResult(ctx.Ops.UpdateCameraFromView(id, UiContext.User())) });
            }
            CInput.ShowMenu(ctx, items, pos, "workspace-context");
        }

        /// <summary>The context menu of a route's point rows: Add point, Delete point, Reset aid, Play / Stop (Phase 3.6 package R).</summary>
        public void RouteContextMenu(TreeRow r, Vector2 pos)
        {
            if (r.Kind == TreeRowKind.RoutePoint) ctx.SubSelection = r.NodeId + "/points/" + r.Point;
            else ctx.Selection.Set(new[] { r.NodeId });
            var items = new List<MenuItem>
            {
                Act("ctx-route-add-point", ActionIds.RouteAddPoint),
                Act("ctx-route-delete-point", ActionIds.RouteDeletePoint),
                Act("ctx-route-reset-aid", ActionIds.RouteResetAid),
                MenuItem.Sep(),
                Act("ctx-route-play", ActionIds.RoutePlay),
                Act("ctx-route-stop", ActionIds.RouteStop),
            };
            CInput.ShowMenu(ctx, items, pos, "workspace-route-context");
        }

        MenuItem Act(string id, string actionId)
        {
            bool known = ctx.Registry.Actions.TryGet(actionId, out var d);
            return new MenuItem
            {
                Id = id, Text = L10n.Get(ActionIds.LabelKey(actionId)), Shortcut = CInput.ChordText(ctx, actionId),
                Disabled = !known || !d.Available, Run = () => ctx.Registry.Run(actionId, "menu"),
            };
        }

        void SelectChildren(string id)
        {
            var n = ctx.Model.Find(id);
            if (n == null) return;
            var l = new List<string>();
            foreach (var c in ctx.Model.Subtree(n)) if (c != n) l.Add(c.Id);
            if (l.Count > 0) ctx.Selection.Set(l);
        }

        void AddFolderUnder(string id) => ctx.ShowResult(ctx.Ops.AddFolder(id, null, -1, UiContext.User()));

        // ------------------------------------------------------------------ isolate (view only)

        public bool IsIsolated => isolated != null;

        /// <summary>view.isolate: shows only the selected nodes (and what is under them) in the viewport; not stored.</summary>
        public void ToggleIsolate()
        {
            if (isolated != null) { isolated = null; Isolate.Clear(ctx); return; }
            if (ctx.Selection.Count == 0) return;
            isolated = new HashSet<string>(ctx.Selection.Ids, StringComparer.Ordinal);
            Isolate.Apply(ctx, isolated);
        }

        // ------------------------------------------------------------------ drag and drop

        DragPayload DragOf(RowView v, PointerDownEvent e)
        {
            var r = v.Row;
            if (r == null || r.Kind != TreeRowKind.Node || renamingId != null) return null;
            var ids = ctx.Selection.Contains(r.NodeId) ? ctx.Selection.Ids.ToList() : new List<string> { r.NodeId };
            ids = WorkspaceRows.TopMost(ctx.Model, ids);
            ids.RemoveAll(x => { var n = ctx.Model.Find(x); return n == null || n.Kind == NodeKind.Opaque; });
            if (ids.Count == 0) return null;
            string label = ids.Count == 1 ? ctx.Model.Find(ids[0]).Label : L10n.Format("workspace.dragCount", ids.Count);
            return new NodesPayload(ids, label);
        }

        VisualElement IDropTarget.DropElement => tree;

        RowView RowAt(Vector2 panelPos, out string zone)
        {
            zone = null;
            foreach (var kv in views)
            {
                var el = kv.Key;
                if (kv.Value.Row == null || el.panel == null || el.resolvedStyle.display == DisplayStyle.None) continue;
                var b = el.worldBound;
                if (!b.Contains(panelPos)) continue;
                float t = (panelPos.y - b.yMin) / Math.Max(1f, b.height);
                zone = t < 0.25f ? "before" : t > 0.75f ? "after" : "on";
                return kv.Value;
            }
            return null;
        }

        /// <summary>What a drop at a row zone means: (parent, point, index) or a problem.</summary>
        bool Resolve(DragPayload p, RowView v, string zone, out string parentId, out string point, out int index, out bool askPoint, out string reason)
        {
            parentId = null; point = null; index = -1; askPoint = false; reason = null;
            if (v == null) return true;     // empty area: root, at the end
            var r = v.Row;
            var target = ctx.Model.Find(r.NodeId);
            if (target == null) { reason = "?"; return false; }
            if (r.Kind == TreeRowKind.Group) { reason = L10n.Get("workspace.dropOnPoint"); return false; }
            if (r.Kind == TreeRowKind.RoutePoints || r.Kind == TreeRowKind.RoutePoint) { reason = L10n.Get("workspace.dropOnRoutePoint"); return false; }
            if (r.Kind == TreeRowKind.Point) { parentId = target.Id; point = r.Point; return Check(p, parentId, point, out reason); }
            if (zone == "on")
            {
                if (p is ApplyPayload ap) { if (target.Kind != ap.Kind) { reason = L10n.Get("workspace.dropWrongKind"); return false; } return true; }
                parentId = target.Id;
                if (target.Kind == NodeKind.Chara) { askPoint = true; return CheckKind(p, target, out reason); }
                return Check(p, parentId, null, out reason);
            }
            if (p is ApplyPayload) { reason = L10n.Get("workspace.dropOnRow"); return false; }
            // between rows: the target's sibling group
            parentId = target.Parent?.Id;
            point = target.Point;
            index = zone == "before" ? target.Index : target.Index + 1;
            if (p is NodesPayload np && np.Ids.Count == 1)
            {
                var moving = ctx.Model.Find(np.Ids[0]);
                // within the same group the index counts without the moved node
                if (moving != null && moving.Parent?.Id == parentId && moving.Point == point && moving.Index < index) index--;
            }
            return Check(p, parentId, point, out reason);
        }

        bool CheckKind(DragPayload p, TreeNode parent, out string reason)
        {
            reason = null;
            if (p is NodesPayload np)
                foreach (var id in np.Ids)
                {
                    var n = ctx.Model.Find(id);
                    if (n == null) continue;
                    if (n == parent || NodeRules.IsInSubtree(parent, n)) { reason = L10n.Get("workspace.dropUnderItself"); return false; }
                    // the move command's route rules (2.2): routes stay unless TreeRules.AllowRouteReparent; never inside a route
                    if (n.Kind == NodeKind.Route && !ctx.Model.Rules.AllowRouteReparent) { reason = "routes cannot be re-parented"; return false; }
                    try { NodeRules.CheckRouteRule(n, parent); }
                    catch (CommandException e) { reason = e.Message; return false; }
                }
            if (p is AddPayload a && !a.Attachable) { reason = L10n.Get("workspace.dropWrongKind"); return false; }
            return true;
        }

        bool Check(DragPayload p, string parentId, string point, out string reason)
        {
            reason = null;
            if (p is NodesPayload np)
            {
                foreach (var id in np.Ids) { reason = WorkspaceRows.MoveProblem(ctx.Model, id, parentId, point); if (reason != null) return false; }
                return true;
            }
            if (p is AddPayload a)
            {
                if (parentId == null) return true;
                var parent = ctx.Model.Find(parentId);
                if (parent == null) { reason = "?"; return false; }
                if (!NodeRules.TakesChildren(parent.Kind)) { reason = L10n.Get("workspace.dropNoChildren"); return false; }
                if (parent.Kind == NodeKind.Chara && point == null) return a.Attachable;
                return true;
            }
            return false;
        }

        bool IDropTarget.Hover(DragPayload payload, Vector2 panelPos, out string reason)
        {
            var v = RowAt(panelPos, out var zone);
            bool ok = Resolve(payload, v, zone, out _, out _, out _, out _, out reason);
            int id = v?.Row?.Id ?? -1;
            if (id != hoverRowId || zone != hoverZone)
            {
                hoverRowId = id;
                hoverZone = zone;
                tree.RefreshItems();
            }
            hoverRefused = !ok;
            return ok;
        }

        bool hoverRefused;

        void ApplyHover(VisualElement el, TreeRow r)
        {
            if (r.Id != hoverRowId || DragController.Current == null) return;
            el.AddToClassList(hoverRefused ? "hs-tree-row--refused" : hoverZone == "on" ? "hs-tree-row--drop-on" : hoverZone == "before" ? "hs-tree-row--drop-before" : "hs-tree-row--drop-after");
        }

        void IDropTarget.Leave()
        {
            if (hoverRowId == -1) return;
            hoverRowId = -1;
            hoverZone = null;
            tree.RefreshItems();
        }

        void IDropTarget.Drop(DragPayload payload, Vector2 panelPos)
        {
            var v = RowAt(panelPos, out var zone);
            if (!Resolve(payload, v, zone, out var parentId, out var point, out var index, out bool ask, out var reason))
            {
                ctx.Toasts?.Show(reason, ToastKind.Warning);
                return;
            }
            var parent = parentId == null ? null : ctx.Model.Find(parentId);
            if (ask && parent != null) { AskPoint(parent, panelPos, pt => DropAt(payload, parentId, pt, -1)); return; }
            if (payload is ApplyPayload ap && v != null) { ctx.ShowResult(ap.Apply(v.Row.NodeId)); return; }
            DropAt(payload, parentId, point, index);
        }

        /// <summary>Performs a drop (also used by tests): moves the dragged nodes or adds the browser row under (parent, point) at index.</summary>
        public void DropAt(DragPayload payload, string parentId, string point, int index)
        {
            if (payload is NodesPayload np)
            {
                var parent = parentId == null ? null : ctx.Model.Find(parentId);
                bool reorder = np.Ids.Count == 1 && ctx.Model.Find(np.Ids[0]) is TreeNode m && m.Parent?.Id == parentId && m.Point == point;
                if (reorder && index >= 0) { ctx.ShowResult(ctx.Ops.Reorder(np.Ids[0], index, UiContext.User())); return; }
                MoveUnderAt(np.Ids, parent, point, index);
            }
            else if (payload is AddPayload a)
            {
                var r = ctx.ShowResult(a.Add(parentId, point));
                if (r != null && r.Ok) foreach (var id in r.NewIds) Reveal(id);
            }
        }
    }
}
