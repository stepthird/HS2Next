// The shell's element tree:
//   root.hs-root
//     toolbar (36 px)                                       UiToolbar
//     main: split(left | split(split(viewport / bottom) | right))   TwoPaneSplitViews; regions with a tab strip each
//     status bar (22 px)                                    UiStatusBar
//     overlay (absolute): toasts, popup menus, tooltips; dialog layer above it
// Regions hold the panels assigned to them (layout.json in <UserData>/config: sizes, collapsed, panel order, active tab,
// hidden panels; unknown ids kept, missing ones in their default region; written 1 s after a change and at quit;
// unreadable → defaults + finding ui.layoutInvalid; a file of an older layout version is replaced once by the current
// default, hidden panels kept, with one toast). The default is the sidebar layout: workspace and inspector as tabs on the
// left, the add browsers, the scene panel and the history as tabs on the right, the bottom region empty and collapsed (it
// opens when a panel is moved there). Tab strips wrap to more lines when their tabs do not fit. A panel's content is built
// when its tab is first shown (or by BuildAll for tests); its root carries the PanelDef in userData (the shortcut router's
// focus scope). Right-click on a tab: "Move to left / right / bottom", "Hide". Collapse per region (toolbar view menu,
// view.panels toggles all). While the workspace and the inspector share a region, a selection made in the viewport (a click
// pick, a handle) shows the inspector tab (AutoShowInspector); a click in the tree only selects. A window size or panel
// scale change rebuilds the splits with the regions re-clamped to the new size. Toasts stand at the viewport's bottom-right
// corner, left of an open right region (the sidebar's browsers keep their buttons at its bottom).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI
{
    public sealed class UiRegion
    {
        public string Name;
        public VisualElement Pane;
        public VisualElement Strip;
        public VisualElement Content;
        public TwoPaneSplitView Split;
        public int SplitIndex;
        public bool Collapsed;
        public readonly List<string> Panels = new List<string>();
        public string Active;
    }

    public sealed class UiShell
    {
        readonly UiContext ctx;
        public VisualElement Root { get; }
        public VisualElement Main { get; private set; }
        public VisualElement Overlay { get; private set; }
        public VisualElement DialogLayer { get; private set; }
        public UiToolbar Toolbar { get; private set; }
        public UiStatusBar StatusBar { get; private set; }
        public PopupMenus Menus { get; private set; }
        public Tooltips Tooltips { get; private set; }
        public TwoPaneSplitView OuterSplit { get; private set; }
        public TwoPaneSplitView InnerSplit { get; private set; }
        public TwoPaneSplitView CenterSplit { get; private set; }
        public readonly Dictionary<string, UiRegion> Regions = new Dictionary<string, UiRegion>(StringComparer.Ordinal);
        /// <summary>Built panel contents by id.</summary>
        public readonly Dictionary<string, VisualElement> PanelRoots = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        public LayoutDocument Layout { get; private set; }
        VisualElement toastStack;
        public string LayoutFile => Path.Combine(ctx.Options.ConfigDir, LayoutDocument.FileName);
        float saveAt = -1;
        bool applying;

        public UiShell(UiContext ctx, VisualElement root)
        {
            this.ctx = ctx;
            Root = root;
        }

        // ---------------- build

        /// <summary>
        /// A field's label is a Label, and setting its text (a language switch re-writes every label) sends a
        /// ChangeEvent&lt;string&gt; that bubbles to the field, whose own value callback took it for an edit (v0.2: each language
        /// switch renamed the selected node to "Name" / "名前" through the inspector's name field, and re-wrote the open
        /// dropdown rows). Stopped at the root before it reaches the field.
        /// </summary>
        static void StopLabelChange(ChangeEvent<string> e)
        {
            if (e.target is Label l && l.ClassListContains(BaseField<string>.labelUssClassName)) e.StopImmediatePropagation();
        }

        public void Build()
        {
            Root.Clear();
            Root.AddToClassList("hs-root");
            Root.style.flexGrow = 1;
            Root.RegisterCallback<ChangeEvent<string>>(StopLabelChange, TrickleDown.TrickleDown);
            Toolbar = new UiToolbar(ctx);
            Root.Add(Toolbar.Element);

            Region(LayoutRegions.Left);
            Region(LayoutRegions.Right);
            Region(LayoutRegions.Bottom);
            Main = new VisualElement { name = "main" };
            Main.AddToClassList("hs-main");
            Root.Add(Main);

            StatusBar = new UiStatusBar(ctx);
            Root.Add(StatusBar.Element);

            Overlay = new VisualElement { name = "overlay", pickingMode = PickingMode.Ignore };
            Overlay.AddToClassList("hs-overlay");
            Root.Add(Overlay);
            toastStack = new VisualElement { name = "toasts" };
            Overlay.Add(toastStack);
            ctx.Toasts = new Toasts(toastStack);
            Menus = new PopupMenus(Overlay);
            Tooltips = new Tooltips(Root, Overlay);
            DialogLayer = new VisualElement { name = "dialogs" };
            Root.Add(DialogLayer);
            ctx.Dialogs = new Dialogs(DialogLayer);
            RefPicker.PopupLayer = Overlay;

            foreach (var r in Regions.Values)
                r.Pane.RegisterCallback<GeometryChangedEvent>(_ => { if (!applying) ScheduleSave(); PlaceToasts(); });
            LoadLayout();
            ApplyLayout();
            ctx.Registry.Panels.Registered += OnPanelRegistered;
            ctx.Registry.Panels.Unregistered += OnPanelUnregistered;
        }

        UiRegion Region(string name)
        {
            var r = new UiRegion { Name = name };
            r.Pane = new VisualElement { name = "region-" + name };
            r.Pane.AddToClassList("hs-region");
            r.Pane.AddToClassList("hs-region--" + name);
            r.Strip = new VisualElement { name = "tabs-" + name };
            r.Strip.AddToClassList("hs-tabs__strip");
            r.Content = new VisualElement { name = "content-" + name };
            r.Content.AddToClassList("hs-tabs__content");
            r.Pane.Add(r.Strip);
            r.Pane.Add(r.Content);
            Regions[name] = r;
            return r;
        }

        // ---------------- layout document

        /// <summary>The version of the layout file replaced by the current default at the last load (-1 = none).</summary>
        public int MigratedFromVersion { get; private set; } = -1;

        void LoadLayout()
        {
            LayoutDocument d = null;
            var problems = new List<string>();
            MigratedFromVersion = -1;
            if (File.Exists(LayoutFile))
            {
                try { d = LayoutDocument.Parse(File.ReadAllText(LayoutFile, Encoding.UTF8), problems); }
                catch (Exception e) { problems.Add(e.Message); }
                if (d == null) ctx.Findings.Warning("ui.layoutInvalid", null, ctx.L10n.Get("msg.layoutInvalid") + " (" + string.Join("; ", problems) + ")");
            }
            if (d != null && d.IsOutdated)
            {
                // an older layout (the bottom-browser layout of version 1) is replaced once by the current default; the hidden
                // panels stay hidden; the new file is written and the user told once
                MigratedFromVersion = d.Version;
                d = d.MigrateToCurrent(ctx.Registry.Panels.Defaults());
                ScheduleSave();
                ctx.Toasts?.Show(ctx.L10n.Format("msg.layoutUpdated", ctx.L10n.Get("action.ui.resetLayout")), ToastKind.Info, 12f);
            }
            Layout = d ?? LayoutDocument.Default(ctx.Registry.Panels.Defaults());
            Layout.Normalize(ctx.Registry.Panels.Defaults());
        }

        /// <summary>Reads layout.json again and applies it (an older version is migrated as at start; tests).</summary>
        public void ReloadLayout()
        {
            LoadLayout();
            ApplyLayout();
            ctx.RaiseView("layout");
        }

        /// <summary>Puts every registered, not hidden panel into its region per the layout document.</summary>
        public void ApplyLayout()
        {
            applying = true;
            try
            {
                foreach (var r in Regions.Values) { r.Panels.Clear(); r.Strip.Clear(); }
                foreach (var name in LayoutRegions.All)
                {
                    var reg = Regions[name];
                    var lr = Layout.Region(name);
                    foreach (var id in lr.Panels)
                        if (ctx.Registry.Panels.TryGet(id, out _) && !Layout.Hidden.Contains(id)) reg.Panels.Add(id);
                    foreach (var id in reg.Panels) reg.Strip.Add(TabButton(reg, id));
                    string active = lr.Active != null && reg.Panels.Contains(lr.Active) ? lr.Active : reg.Panels.FirstOrDefault();
                    Activate(reg, active, save: false);
                    reg.Collapsed = lr.Collapsed || reg.Panels.Count == 0;
                }
                RebuildSplits(capture: false);
            }
            finally { applying = false; }
        }

        /// <summary>
        /// (Re)builds split(left | split(split(viewport / bottom) | right)) with the layout's sizes and collapse flags. The
        /// splits are rebuilt on every collapse change instead of TwoPaneSplitView.CollapseChild / UnCollapse on live splits:
        /// UnCollapse re-initialises the fixed pane to its initial dimension and leftover inline styles of a collapsed pane
        /// re-collapse it on the next setup (6.6), which left regions collapsed or mis-sized. Region panes and the viewport
        /// are reused (their inline split styles reset); the user's dragged sizes are captured first.
        /// </summary>
        void RebuildSplits(bool capture = true)
        {
            if (capture) CaptureSizes();
            var left = Regions[LayoutRegions.Left];
            var right = Regions[LayoutRegions.Right];
            var bottom = Regions[LayoutRegions.Bottom];
            var vp = ctx.Viewport.Element;
            foreach (var e in new[] { left.Pane, right.Pane, bottom.Pane, vp }) { e.RemoveFromHierarchy(); ResetSplitStyles(e); }
            Main.Clear();
            var (avW, avH) = Available();
            float want(UiRegion r) => (float)Math.Max(LayoutRegions.MinSize(r.Name), Layout.Region(r.Name)?.Size ?? LayoutRegions.DefaultSize(r.Name));
            // the regions never push the viewport below 320 × 240 or past the window: a small window shrinks them
            // proportionally (below their minimum sizes if it must), so a saved layout never hides a region
            float lw = left.Collapsed ? 0 : want(left), rw = right.Collapsed ? 0 : want(right), bh = bottom.Collapsed ? 0 : want(bottom);
            // the split lines take about 2 px each beside the viewport (two splits across, one below it)
            float maxSide = Math.Max(0, avW - ViewportMinW - 2 * SplitLine);
            if (lw + rw > maxSide && lw + rw > 0) { float k = maxSide / (lw + rw); lw *= k; rw *= k; }
            float maxBottom = Math.Max(0, avH - ViewportMinH - SplitLine);
            if (bh > maxBottom) bh = maxBottom;
            clamped = lw + rw >= maxSide - 0.5f && maxSide < want(left) + want(right) || bh >= maxBottom - 0.5f && maxBottom < want(bottom);
            float Size(UiRegion r) => r == left ? Math.Max(1, lw) : r == right ? Math.Max(1, rw) : r == bottom ? Math.Max(1, bh) : want(r);
            CenterSplit = new TwoPaneSplitView(1, Size(bottom), TwoPaneSplitViewOrientation.Vertical) { name = "split-center" };
            CenterSplit.Add(vp);
            CenterSplit.Add(bottom.Pane);
            InnerSplit = new TwoPaneSplitView(1, Size(right), TwoPaneSplitViewOrientation.Horizontal) { name = "split-inner" };
            InnerSplit.Add(CenterSplit);
            InnerSplit.Add(right.Pane);
            OuterSplit = new TwoPaneSplitView(0, Size(left), TwoPaneSplitViewOrientation.Horizontal) { name = "split-outer" };
            OuterSplit.Add(left.Pane);
            OuterSplit.Add(InnerSplit);
            OuterSplit.style.flexGrow = 1;
            left.Split = OuterSplit; left.SplitIndex = 0;
            right.Split = InnerSplit; right.SplitIndex = 1;
            bottom.Split = CenterSplit; bottom.SplitIndex = 1;
            foreach (var r in new[] { left, right, bottom })
                if (r.Collapsed) r.Split.CollapseChild(r.SplitIndex);   // pending until the split's first layout
            Main.Add(OuterSplit);
            PlaceToasts();
        }

        public const float ViewportMinW = 320, ViewportMinH = 240;
        /// <summary>The room a split's drag line takes between two panes (logical px, rounded up).</summary>
        const float SplitLine = 2;

        /// <summary>Keeps the toasts at the viewport's bottom-right corner, left of an open right region (the sidebar's
        /// browsers have their buttons at its bottom).</summary>
        void PlaceToasts()
        {
            if (toastStack == null || !Regions.TryGetValue(LayoutRegions.Right, out var right)) return;
            float w = right.Collapsed ? 0 : right.Pane.layout.width;
            toastStack.style.right = (float.IsNaN(w) || w < 0 ? 0 : w) + 12;
        }

        /// <summary>The logical size the main area can use (window ÷ panel scale minus toolbar and status bar).</summary>
        (float w, float h) Available()
        {
            float s = Root.panel != null ? Root.panel.scaledPixelsPerPoint : 0;
            float w = s > 0 ? Screen.width / s : Root.layout.width;
            float h = s > 0 ? Screen.height / s : Root.layout.height;
            if (float.IsNaN(w) || w <= 0) w = 1920;
            if (float.IsNaN(h) || h <= 0) h = 1080;
            return (w, h - 36 - 24);
        }

        int lastW, lastH;
        float lastPpp;
        /// <summary>The last build had to shrink regions to fit the window (their sizes are then not captured into layout.json).</summary>
        bool clamped;
        /// <summary>Rebuilds after a window size or panel scale change (tests, ui.state).</summary>
        public int ResizeRebuilds { get; private set; }

        /// <summary>Re-clamps the regions after a window size change or a panel scale change (the host changes the scale
        /// when the window size changes, possibly a frame later): once per change.</summary>
        public void OnScreenSize()
        {
            float ppp = Root.panel != null ? Root.panel.scaledPixelsPerPoint : 0;
            if (Screen.width == lastW && Screen.height == lastH && Mathf.Abs(ppp - lastPpp) < 1e-4f) return;
            bool first = lastW == 0;
            lastW = Screen.width; lastH = Screen.height; lastPpp = ppp;
            if (first) return;
            RebuildSplits(capture: !clamped);
            ResizeRebuilds++;
        }

        static void ResetSplitStyles(VisualElement e)
        {
            var st = e.style;
            st.display = StyleKeyword.Null; st.width = StyleKeyword.Null; st.height = StyleKeyword.Null;
            st.flexGrow = StyleKeyword.Null; st.flexShrink = StyleKeyword.Null; st.flexBasis = StyleKeyword.Null;
            st.left = StyleKeyword.Null; st.top = StyleKeyword.Null;
        }

        Button TabButton(UiRegion reg, string id)
        {
            ctx.Registry.Panels.TryGet(id, out var def);
            var b = new Button(() => Activate(reg, id, true)) { name = "tab-" + id };
            b.AddToClassList("hs-tab");
            Loc.Bind(b, def?.TitleKey ?? "panel." + id);
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 1) return;
                ShowPanelMenu(id, e.position);
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            return b;
        }

        /// <summary>The panel title bar menu: Move to left / right / bottom, Hide.</summary>
        public void ShowPanelMenu(string id, Vector2 at)
        {
            var items = new List<MenuItem>();
            string cur = RegionOf(id);
            foreach (var r in LayoutRegions.All)
                items.Add(new MenuItem { Id = "move-" + r, Text = ctx.L10n.Format("region.moveTo", ctx.L10n.Get("region." + r)), Disabled = r == cur, Run = () => MovePanel(id, r) });
            items.Add(MenuItem.Sep());
            items.Add(new MenuItem { Id = "hide", Text = ctx.L10n.Get("panel.hide"), Run = () => SetPanelVisible(id, false) });
            items.Add(new MenuItem { Id = "reset-layout", Text = ctx.L10n.Get("action.ui.resetLayout"), Run = ResetLayout });
            Menus.Show(items, null, at, "panel-menu");
        }

        // a removed panel (a plugin's unload): its tab and content go; the layout keeps the id for a later registration
        void OnPanelUnregistered(PanelDef d)
        {
            if (PanelRoots.TryGetValue(d.Id, out var root)) { root.RemoveFromHierarchy(); PanelRoots.Remove(d.Id); }
            ApplyLayout();
        }

        void OnPanelRegistered(PanelDef d)
        {
            Layout.Normalize(ctx.Registry.Panels.Defaults());
            if (PanelRoots.ContainsKey(d.Id)) { PanelRoots[d.Id].RemoveFromHierarchy(); PanelRoots.Remove(d.Id); }
            ApplyLayout();
        }

        public string RegionOf(string panelId)
        {
            foreach (var r in Regions.Values) if (r.Panels.Contains(panelId)) return r.Name;
            return null;
        }

        /// <summary>Shows a panel's tab in its region (builds the content on first show).</summary>
        public void Activate(UiRegion reg, string id, bool save)
        {
            reg.Active = id;
            foreach (var b in reg.Strip.Children()) b.EnableInClassList("hs-tab--active", b.name == "tab-" + id);
            foreach (var c in reg.Content.Children().ToList()) c.style.display = DisplayStyle.None;
            if (id != null)
            {
                var root = PanelRoot(id);
                if (root != null)
                {
                    if (root.parent != reg.Content) reg.Content.Add(root);
                    root.style.display = DisplayStyle.Flex;
                }
            }
            var lr = Layout.Region(reg.Name);
            if (lr != null) lr.Active = id;
            if (save) { ScheduleSave(); ctx.RaiseView("layout"); }
        }

        /// <summary>The built content of a panel (built now when needed); null when not registered.</summary>
        public VisualElement PanelRoot(string id)
        {
            if (PanelRoots.TryGetValue(id, out var r)) return r;
            if (!ctx.Registry.Panels.TryGet(id, out var def)) return null;
            r = BuildPanel(ctx, def);
            PanelRoots[id] = r;
            return r;
        }

        /// <summary>A panel's root element: class hs-panel, name panel-&lt;id&gt;, userData = the PanelDef (also used off-panel by UiCoreTest).</summary>
        public static VisualElement BuildPanel(UiContext ctx, PanelDef def)
        {
            var root = new VisualElement { name = "panel-" + def.Id, userData = def };
            root.AddToClassList("hs-panel");
            try
            {
                var content = def.Factory?.Invoke(ctx);
                if (content != null) { content.style.flexGrow = 1; root.Add(content); }
            }
            catch (Exception e)
            {
                Debug.LogError("[ui] panel " + def.Id + " failed to build: " + e);
                var l = new Label(def.Id + ": " + e.Message);
                l.AddToClassList("hs-panel__empty");
                root.Add(l);
            }
            return root;
        }

        public void MovePanel(string id, string region)
        {
            Layout.Move(id, region);
            Layout.Hidden.Remove(id);
            ApplyLayout();
            ScheduleSave();
            ctx.RaiseView("layout");
        }

        public void SetPanelVisible(string id, bool visible)
        {
            if (visible) Layout.Hidden.Remove(id);
            else if (!Layout.Hidden.Contains(id)) Layout.Hidden.Add(id);
            ApplyLayout();
            ScheduleSave();
            ctx.RaiseView("layout");
        }

        /// <summary>Shows and focuses a panel (ui.panel focus, "open the animation browser from the inspector").</summary>
        public bool FocusPanel(string id)
        {
            if (!ShowPanel(id)) return false;
            PanelRoot(id)?.Focus();
            return true;
        }

        /// <summary>Shows a panel (unhides it, opens its collapsed region, makes it the region's shown tab) without moving the
        /// keyboard focus (a double click or Enter on a tree row shows the inspector; the tree keeps the focus).</summary>
        public bool ShowPanel(string id)
        {
            if (Layout.Hidden.Contains(id)) SetPanelVisible(id, true);
            string r = RegionOf(id);
            if (r == null) return false;
            var reg = Regions[r];
            if (reg.Collapsed) SetCollapsed(reg, false, true);
            Activate(reg, id, true);
            return true;
        }

        public const string WorkspaceId = "workspace", InspectorId = "inspector";

        /// <summary>The workspace and the inspector are tabs of the same region (the default layout).</summary>
        public bool InspectorSharesWorkspaceRegion()
        {
            string ri = RegionOf(InspectorId);
            return ri != null && ri == RegionOf(WorkspaceId);
        }

        /// <summary>Times the inspector tab was shown by a selection made in the viewport (tests).</summary>
        public int AutoShows { get; private set; }

        /// <summary>
        /// A selection made in the viewport (a click pick, a handle): when the workspace and the inspector are tabs of one
        /// region, the inspector becomes the shown tab there. Nothing happens when they are in different regions (both stay
        /// visible), when the region is collapsed or when the inspector is already shown. Returns true when the tab changed.
        /// </summary>
        public bool AutoShowInspector()
        {
            if (!InspectorSharesWorkspaceRegion()) return false;
            var reg = Regions[RegionOf(InspectorId)];
            if (reg.Collapsed || reg.Active == InspectorId) return false;
            Activate(reg, InspectorId, true);
            AutoShows++;
            return true;
        }

        public void SetCollapsed(UiRegion reg, bool collapsed, bool save)
        {
            if (reg.Collapsed == collapsed && reg.Split != null) return;
            CaptureSizes();
            reg.Collapsed = collapsed;
            var lr = Layout.Region(reg.Name);
            if (lr != null && reg.Panels.Count > 0) lr.Collapsed = collapsed;
            RebuildSplits(capture: false);
            if (save) { ScheduleSave(); ctx.RaiseView("layout"); }
        }

        /// <summary>view.panels: all regions collapsed, or all restored.</summary>
        public void TogglePanels()
        {
            bool anyOpen = Regions.Values.Any(r => !r.Collapsed && r.Panels.Count > 0);
            CaptureSizes();
            foreach (var r in Regions.Values)
                if (r.Panels.Count > 0) { r.Collapsed = anyOpen; var lr = Layout.Region(r.Name); if (lr != null) lr.Collapsed = anyOpen; }
            RebuildSplits(capture: false);
            ScheduleSave();
            ctx.RaiseView("layout");
        }

        public void ResetLayout()
        {
            Layout = LayoutDocument.Default(ctx.Registry.Panels.Defaults());
            Layout.Normalize(ctx.Registry.Panels.Defaults());
            ApplyLayout();
            ScheduleSave();
            ctx.RaiseView("layout");
        }

        /// <summary>Builds every registered panel (tests).</summary>
        public int BuildAll()
        {
            int n = 0;
            foreach (var d in ctx.Registry.Panels.All) if (PanelRoot(d.Id) != null) n++;
            return n;
        }

        // ---------------- persistence

        void ScheduleSave() => saveAt = Time.realtimeSinceStartup + 1f;

        /// <summary>Sets a region's size (logical px, at least its minimum): the layout document and, when the region is open,
        /// the fixed pane's inline size (what the splitter's drag writes).</summary>
        public void SetSize(UiRegion r, float size)
        {
            size = (float)Math.Max(LayoutRegions.MinSize(r.Name), size);
            var lr = Layout.Region(r.Name);
            if (lr != null) lr.Size = size;
            // a rebuild applies it reliably (the split's setup re-applies its initial dimension; inline sizes written from
            // outside were seen to revert); the other regions keep their current sizes
            CaptureSizes();
            if (lr != null) lr.Size = size;
            RebuildSplits(capture: false);
            ScheduleSave();
        }

        /// <summary>A region's current size (logical px).</summary>
        public float SizeOf(UiRegion r)
        {
            var fp = r.Split?.fixedPane;
            if (fp == null) return 0;
            float s = r.Name == LayoutRegions.Bottom ? fp.resolvedStyle.height : fp.resolvedStyle.width;
            return float.IsNaN(s) ? 0 : s;
        }

        void CaptureSizes()
        {
            if (clamped) return;
            foreach (var r in Regions.Values)
            {
                var lr = Layout.Region(r.Name);
                if (lr == null || r.Split == null || r.Collapsed) continue;
                float s = SizeOf(r);
                if (s > 1) lr.Size = s;
            }
        }

        public void Tick()
        {
            OnScreenSize();
            if (saveAt >= 0 && Time.realtimeSinceStartup >= saveAt) SaveLayout();
            Tooltips?.Tick();
            ctx.Toasts?.Tick();
            StatusBar?.Tick();
            Toolbar?.Tick();
        }

        public void SaveLayout()
        {
            saveAt = -1;
            if (Layout == null) return;
            CaptureSizes();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile));
                File.WriteAllText(LayoutFile, Layout.ToJson(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[ui] layout.json not written: " + e.Message); }
        }

        internal void Flush() { if (saveAt >= 0) SaveLayout(); }
    }
}
