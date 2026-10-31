// The character inspector's Edit tab (C14): the host of the Maker pages. The
// section list is the tree (InspectorSchemas.CharaEditTree: Face / Body / Hair / Clothes /
// Accessories / Character / File). Sections of InspectorSchemas.CharaEdit have ids "<tree node>.<page>.<section>"; a node
// whose sections span more than one page shows a second row of buttons (the sub-page strip, `edit-page-<node>-<page>`,
// F2) above the page body; the page shows its sections as foldouts (`edit-section-<id>`).
// Pages are built on first show and the page shown last per node is kept while the app runs (LastPage). Rows by kind:
// PartList / Push rows become the Maker list widgets, colour rows MakerColorRow, scaled floats MakerSliderRow (vector
// components included), the rest the 3.5 factory (InspectorUi.Row). Templates: `{k}` rows are expanded per instance of a
// repeated section (paint 1 / 2), `{side}` rows by the Eyes page's SideSelector (section `face.eyes.side`: side buttons,
// the link toggle and the copy buttons), re-expanded when the side changes. Gates are evaluated on the primary: a row whose
// gate fails is not built, a section without rows is not shown, a page without sections hides its button (Beard for
// females). Every tree node has sections (Phase 4.6: no "later" page any more, female and male). Rows write every selected
// character (U25). While the
// tab is shown the primary character has an edit session (StudioOps.BeginEditSession / EndEditSession inside
// CharaEditSessions.FromUi(): kept decoded layers, no expiry, C13; the first session captures the reference values of ↺).
// The open tree node is kept across characters. The action `edit.show {node, page?, section?}` shows a section of the
// primary's Edit tab (automation without pixel positions).
// 4.2: the Body node's pages in InspectorSchemas.BodyPages order (Shape · Skin ·
// Tan · Nipples · Underhair · Nails · Paint · Special; the strip follows InspectorSchemas.CharaEditPagesOf), Shape shown
// until another page is chosen; the female-only rows (gate chara.female) are not built for a male primary and the height
// row follows maker.bodyHeight (shown while CharaAssembler.MaleHeightPin075 is off; gates are evaluated when a page is
// built, so a pin change shows on the next Edit tab); appearance Bool rows are MakerToggleRows (the futanari toggle with its
// tooltip); colour push rows are PushGrid chips. The app options maker.keepColours and maker.bodyThrottle (auto / always /
// off → CharaEditFlush.BodyThrottle) are applied when the tab is first added and on every change.
// Pages of one node with a part selector live in partial files: the Hair page (CharaEditTab.Hair.cs) and, from 4.4, the
// Clothes page (CharaEditTab.Clothes.cs). Their sections whose rows follow the worn prefab or the document are LiveSections
// (one instance per page); this file keeps the generic host: tree, pages, sections, rows, gates, edit.show, the session.
// 4.5: the Accessories page (CharaEditTab.Accessories.cs; edit.show takes `slot`, the
// 0-based build slot) and the app option maker.copyAccessoryPluginData → MakerServices.CopyAccessoryPluginData (XQ11).
// 4.6: the Character page (CharaEditTab.Character.cs: the name and the read-only lines) and the File page
// (CharaEditTab.File.cs: character save with the thumbnail preview and framing, the character load with the five parts,
// coordinate save and load).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class CharaEditTab : VisualElement
    {
        public const string TabId = "edit";
        /// <summary>The tree node shown last (kept while the app runs).</summary>
        public static string LastNode = "face";
        /// <summary>The page shown last per tree node (kept while the app runs). Face and Body open on Shape (the 4.0 page; the
        /// Body node's first page) until another page was chosen.</summary>
        public static readonly Dictionary<string, string> LastPage = new Dictionary<string, string>(StringComparer.Ordinal) { ["face"] = "shape", ["body"] = "shape" };
        /// <summary>The Edit tab attached to a panel (null when none is shown).</summary>
        public static CharaEditTab Current { get; private set; }

        readonly UiContext ctx;
        readonly string primary;
        readonly TargetSelector sel;
        readonly VisualElement tree, page;
        readonly Dictionary<string, Button> nodeButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly Dictionary<string, VisualElement> pages = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        // per node: its page ids in order, the strip buttons, the page host and the built pages
        readonly Dictionary<string, List<string>> pageIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, Button> pageButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly Dictionary<string, VisualElement> pageHosts = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        readonly Dictionary<string, VisualElement> subPages = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        readonly Dictionary<string, Foldout> sectionFoldouts = new Dictionary<string, Foldout>(StringComparer.Ordinal);
        // sections holding {side} rows: (container, section) re-built when the eye side changes
        readonly List<(VisualElement into, SchemaSection section)> sideSections = new List<(VisualElement, SchemaSection)>();
        bool session;
        Dictionary<string, bool> gateMemo;

        /// <summary>Frames a marked live section stays watched for the flush that realizes the change.</summary>
        public const int LiveFrames = LiveSections.LiveFrames;

        /// <summary>Live-gate re-evaluations that rebuilt a section's rows / passes that compared signatures, over every page of
        /// this tab (tests).</summary>
        public int LiveRebuilds => (hairLive?.Rebuilds ?? 0) + LivePageRebuilds();
        public int LiveEvaluations => (hairLive?.Evaluations ?? 0) + LivePageEvaluations();

        public string CurrentNode { get; private set; }
        /// <summary>The page shown in the current node (null for a node without pages).</summary>
        public string CurrentPage { get; private set; }
        /// <summary>The page body of every tree node (built on first show).</summary>
        public IReadOnlyDictionary<string, VisualElement> Pages => pages;
        /// <summary>The built sub-pages ("&lt;node&gt;.&lt;page&gt;").</summary>
        public IReadOnlyDictionary<string, VisualElement> SubPages => subPages;
        /// <summary>First-show time of each sub-page ("&lt;node&gt;.&lt;page&gt;", ms; UF3).</summary>
        public readonly Dictionary<string, double> PageBuildMs = new Dictionary<string, double>(StringComparer.Ordinal);
        /// <summary>The eye side selector of the Eyes page (null until built).</summary>
        public SideSelector Side { get; private set; }
        /// <summary>True while this tab holds the primary character's edit session.</summary>
        public bool HasSession => session;
        public string PrimaryId => primary;

        /// <summary>
        /// Adds the Edit tab to the character inspector's tab strip (the one line of CharaInspector): its button, its body
        /// (registered with the other tabs so ShowTab, LastTab and the active mark work unchanged).
        /// </summary>
        public static void AddTo(UiContext ctx, string primaryId, VisualElement strip, IDictionary<string, Button> buttons,
            IDictionary<string, VisualElement> bodies, Action<string> show)
        {
            BindOptions(ctx);
            var b = new Button(() => show(TabId)) { name = "tab-" + TabId };
            b.AddToClassList("hs-inspector__tab");
            Loc.Bind(b, "inspector.tab.edit");
            strip.Add(b);
            buttons[TabId] = b;
            bodies[TabId] = new CharaEditTab(ctx, primaryId);
        }

        /// <summary>The app option "keep colours on item change" (C7; default off), when the option table defines it.</summary>
        public const string KeepColoursOption = "maker.keepColours";
        /// <summary>The app option of the body composite drag throttle (Y4): auto (default; weight
        /// ≥ 2 only) / always (4.0's rule) / off; view state, never history.</summary>
        public const string BodyThrottleOption = "maker.bodyThrottle";
        /// <summary>The app option "copy plugin data with an accessory slot" (XQ11;
        /// default off = vanilla).</summary>
        public const string CopyAccessoryPluginDataOption = "maker.copyAccessoryPluginData";
        static UiContext optionsBoundTo;

        /// <summary>MakerServices.KeepColoursOnItemChange and CharaEditFlush.BodyThrottle follow the app options (bound once per UI
        /// context).</summary>
        public static void BindOptions(UiContext ctx)
        {
            if (ctx?.Options == null || optionsBoundTo == ctx) return;
            bool keep = OptionDefs.TryGet(KeepColoursOption, out _), throttle = OptionDefs.TryGet(BodyThrottleOption, out _),
                copyPlugins = OptionDefs.TryGet(CopyAccessoryPluginDataOption, out _);
            if (!keep && !throttle && !copyPlugins) return;
            optionsBoundTo = ctx;
            void ApplyKeep() { if (keep) MakerServices.KeepColoursOnItemChange = ctx.Options.Get(KeepColoursOption) is bool b && b; }
            void ApplyCopyPlugins() { if (copyPlugins) MakerServices.CopyAccessoryPluginData = ctx.Options.Get(CopyAccessoryPluginDataOption) is bool b && b; }
            void ApplyThrottle()
            {
                if (throttle && CharaEditFlush.TryParseBodyThrottle(ctx.Options.Get(BodyThrottleOption) as string, out var mode)) CharaEditFlush.BodyThrottle = mode;
            }
            ApplyKeep();
            ApplyThrottle();
            ApplyCopyPlugins();
            ctx.Options.Changed += k =>
            {
                if (k == KeepColoursOption) ApplyKeep();
                else if (k == BodyThrottleOption) ApplyThrottle();
                else if (k == CopyAccessoryPluginDataOption) ApplyCopyPlugins();
            };
        }

        public CharaEditTab(UiContext ctx, string primaryId)
        {
            this.ctx = ctx;
            primary = primaryId;
            sel = TargetSelector.Selection(NodeKind.Chara);
            name = "tab-" + TabId + "-body";
            tree = new VisualElement { name = "edit-tree" };
            tree.AddToClassList("hs-inspector__tabs");
            tree.style.flexDirection = FlexDirection.Row;
            tree.style.flexWrap = Wrap.Wrap;
            page = new VisualElement { name = "edit-page" };
            Add(tree);
            Add(page);
            foreach (var id in InspectorSchemas.CharaEditTree)
            {
                string node = id;
                var b = new Button(() => ShowNode(node)) { name = "edit-tree-" + node };
                b.AddToClassList("hs-inspector__tab");
                Loc.Bind(b, "inspector.charaEdit.tree." + node);
                tree.Add(b);
                nodeButtons[node] = b;
            }
            ShowNode(nodeButtons.ContainsKey(LastNode) ? LastNode : "face");
            RegisterCallback<AttachToPanelEvent>(_ => { Current = this; BeginSession(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { if (Current == this) Current = null; EndSession(); });
        }

        public void ShowNode(string id)
        {
            if (!nodeButtons.ContainsKey(id)) return;
            CurrentNode = LastNode = id;
            if (!pages.TryGetValue(id, out var p)) pages[id] = p = BuildNode(id);
            page.Clear();
            page.Add(p);
            foreach (var kv in nodeButtons) kv.Value.EnableInClassList("hs-inspector__tab--active", kv.Key == id);
            CurrentPage = null;
            if (pageIds.TryGetValue(id, out var ids) && ids.Count > 0)
            {
                string want = LastPage.TryGetValue(id, out var lp) && ids.Contains(lp) && PageShown(id, lp) ? lp : ids.FirstOrDefault(x => PageShown(id, x)) ?? ids[0];
                ShowPage(want);
            }
        }

        bool PageShown(string node, string pg) => pageButtons.TryGetValue(node + "." + pg, out var b) && b.style.display != DisplayStyle.None;

        /// <summary>The sections of a tree node (ids "&lt;node&gt;.…"), in schema order.</summary>
        static IEnumerable<SchemaSection> SectionsOf(string node) => InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == node);

        /// <summary>The page of a section id ("face.eyes.iris" → "eyes"; a two-part id has no page: null).</summary>
        public static string PageOf(string sectionId)
        {
            if (sectionId == null) return null;
            var parts = sectionId.Split('.');
            return parts.Length >= 3 ? parts[1] : null;
        }

        VisualElement BuildNode(string node)
        {
            var c = new VisualElement { name = "edit-page-" + node };
            var n = ctx.Model.Find(primary);
            if (n?.Appearance?.Stored == null)
                c.Add(InspectorUi.Note(n?.Appearance?.Card != null ? "inspector.charaEdit.importOnEdit" : "inspector.charaEdit.noDocuments"));
            var sections = SectionsOf(node).ToList();
            if (sections.Count == 0) return c;   // every tree node has sections (4.6); none shows a "later" note
            var special = BuildPartPage(node, c, sections);
            if (special != null) return special;
            // the node's pages in their fixed order (FacePages, BodyPages; others as their sections name them), then any page a
            // section names that the order does not list
            var named = new List<string>();
            foreach (var s in sections)
            {
                string pg = PageOf(s.Id);
                if (pg != null && !named.Contains(pg)) named.Add(pg);
            }
            var ids = InspectorSchemas.CharaEditPagesOf(node).Where(named.Contains).ToList();
            foreach (var pg in named) if (!ids.Contains(pg)) ids.Add(pg);
            if (ids.Count <= 1)
            {
                // one page (or sections without a page segment): the sections as foldouts directly
                foreach (var s in sections) AddSection(c, s);
                return c;
            }
            pageIds[node] = ids;
            var strip = new VisualElement { name = "edit-pages-" + node };
            strip.AddToClassList("hs-inspector__tabs");
            strip.AddToClassList("hs-edit-pages");
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.flexWrap = Wrap.Wrap;
            foreach (var pg in ids)
            {
                string p = pg;
                var b = new Button(() => ShowPage(p)) { name = "edit-page-" + node + "-" + pg };
                b.AddToClassList("hs-inspector__tab");
                Loc.Bind(b, "inspector.charaEdit.page." + node + "." + pg);
                bool any = sections.Any(s => PageOf(s.Id) == pg && SectionVisible(s));
                b.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
                strip.Add(b);
                pageButtons[node + "." + pg] = b;
            }
            c.Add(strip);
            var host = new VisualElement { name = "edit-pagehost-" + node };
            c.Add(host);
            pageHosts[node] = host;
            return c;
        }

        /// <summary>Shows a page of the current node (built on first show).</summary>
        public void ShowPage(string pg)
        {
            string node = CurrentNode;
            if (node == null || !pageIds.TryGetValue(node, out var ids) || !ids.Contains(pg)) return;
            string key = node + "." + pg;
            if (!subPages.TryGetValue(key, out var body))
            {
                var sw = Stopwatch.StartNew();
                body = new VisualElement { name = "edit-subpage-" + node + "-" + pg };
                foreach (var s in SectionsOf(node)) if (PageOf(s.Id) == pg) AddSection(body, s);
                subPages[key] = body;
                PageBuildMs[key] = sw.Elapsed.TotalMilliseconds;
            }
            var host = pageHosts[node];
            host.Clear();
            host.Add(body);
            CurrentPage = pg;
            LastPage[node] = pg;
            foreach (var p in ids) if (pageButtons.TryGetValue(node + "." + p, out var b)) b.EnableInClassList("hs-inspector__tab--active", p == pg);
        }

        // ---------------- sections and rows

        bool SectionVisible(SchemaSection s) => s.Fields.Count == 0 || s.Fields.Any(f => GateHolds(f.Gate) && SexHasRow(f));

        // PartList rows of a list the primary's sex does not have (the beard for females) count as gated off.
        bool SexHasRow(FieldSpec f)
        {
            if (f.Kind != FieldKind.PartList) return true;
            int sex = StudioOps.SexOf(ctx.Model.Find(primary));
            return f.CategoryFor(sex < 0 ? 1 : sex) >= 0;
        }

        void AddSection(VisualElement into, SchemaSection s)
        {
            if (!SectionVisible(s)) return;
            if (s.IsRepeated)
            {
                foreach (var inst in s.Instances)
                {
                    var fo = InspectorUi.Section(inst.LabelKey, true, "edit-section-" + inst.Id);
                    sectionFoldouts[inst.Id] = fo;
                    AddRows(fo, s, inst.Value);
                    into.Add(fo);
                }
                return;
            }
            var f0 = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
            sectionFoldouts[s.Id] = f0;
            if (s.Fields.Any(f => f.Kind == FieldKind.Bool && f.Path != null && f.Path.EndsWith("face.eyes.linked", StringComparison.Ordinal)))
            {
                // the eye side selector: side buttons, the link toggle, the copy buttons
                var linkSpec = s.Fields.First(f => f.Kind == FieldKind.Bool && f.Path.EndsWith("face.eyes.linked", StringComparison.Ordinal));
                string lr = s.Fields.FirstOrDefault(f => f.Kind == FieldKind.Button && f.Action == InspectorSchemas.ActionCopyEyeLeftToRight)?.LabelKey;
                string rl = s.Fields.FirstOrDefault(f => f.Kind == FieldKind.Button && f.Action == InspectorSchemas.ActionCopyEyeRightToLeft)?.LabelKey;
                Side = new SideSelector(ctx, sel, linkSpec, lr, rl);
                Side.SideChanged += _ => RebuildSideRows();
                f0.Add(Side);
                foreach (var f in s.Fields)
                    if (f != linkSpec && f.Kind != FieldKind.Button && GateHolds(f.Gate)) f0.Add(BuildRow(f, null));
                into.Add(f0);
                return;
            }
            if (s.Fields.Any(f => f.Placeholder == InspectorSchemas.SidePlaceholder))
            {
                var holder = new VisualElement { name = "edit-rows-" + s.Id };
                f0.Add(holder);
                sideSections.Add((holder, s));
                AddRows(holder, s, null);
            }
            else AddRows(f0, s, null);
            into.Add(f0);
        }

        void AddRows(VisualElement into, SchemaSection s, string instance)
        {
            foreach (var f in s.Fields)
            {
                if (!GateHolds(f.Gate)) continue;
                string value = f.Placeholder == InspectorSchemas.SidePlaceholder ? SideSelector.EyeSide : f.IsTemplate ? instance : null;
                if (f.IsTemplate && value == null) continue;
                var row = BuildRow(f, value);
                if (row != null) into.Add(row);
            }
        }

        void RebuildSideRows()
        {
            foreach (var (holder, s) in sideSections)
            {
                holder.Clear();
                AddRows(holder, s, null);
            }
        }

        /// <summary>The widget of a Maker row (placeholder value for templates).</summary>
        VisualElement BuildRow(FieldSpec f, string placeholder)
        {
            string path = f.IsTemplate ? f.Expand(placeholder) : f.Path;
            var special = BuildPageRow(f, path);
            if (special != null) return special == NoRow ? null : special;
            switch (f.Kind)
            {
                case FieldKind.PartList: return new PartList(ctx, f, sel, path);
                case FieldKind.Push: return new PushGrid(ctx, f, sel, path);
                case FieldKind.Color:
                case FieldKind.ColorHdr: return new MakerColorRow(ctx, f, sel, path);
                case FieldKind.Float when f.Scale != 1:
                case FieldKind.Vec2 when f.Component >= 0 && f.Scale != 1:
                    return new MakerSliderRow(ctx, f, sel, path);
                case FieldKind.Bool when path != null && path.StartsWith("appearance.", StringComparison.Ordinal):
                    return new MakerToggleRow(ctx, f, sel, path);
                case FieldKind.Enum when path != null && path.StartsWith("appearance.", StringComparison.Ordinal):
                    return new MakerChoiceRow(ctx, f, sel, path);
                default: return InspectorUi.Row(ctx, f, sel, null, placeholder);
            }
        }

        /// <summary>The page of a node with its own widgets (Hair, Clothes, Accessories, Character, File), or null for the generic pages.</summary>
        VisualElement BuildPartPage(string node, VisualElement c, List<SchemaSection> sections) =>
            node == HairNode ? BuildHairPage(c, sections) : node == ClothesNode ? BuildClothesPage(c, sections)
            : node == AccessoriesNode ? BuildAccessoriesPage(c, sections)
            : node == CharacterNode ? BuildCharacterPage(c, sections) : node == FileNode ? BuildFilePage(c, sections) : null;

        /// <summary>A row the part pages draw themselves (HairBundles, the hair and clothes buttons, the clothes pattern rows);
        /// null = the generic widget; <see cref="NoRow"/> = nothing.</summary>
        VisualElement BuildPageRow(FieldSpec f, string path) => BuildHairRow(f, path) ?? BuildClothesRow(f, path);

        static readonly VisualElement NoRow = new VisualElement { name = "no-row" };

        /// <summary>Gates of the Maker rows, evaluated on the primary through the inspector's evaluator (InspectorGates.Holds:
        /// chara.* from the realization, maker.* through StudioOps.MakerGateHolds); maker.* directly when no evaluator is set.</summary>
        bool GateHolds(string gate)
        {
            if (string.IsNullOrEmpty(gate)) return true;
            if (gateMemo != null && gateMemo.TryGetValue(gate, out bool m)) return m;
            GateEvaluations++;
            bool r = FieldWidgets.GateEvaluator != null ? FieldWidgets.GateEvaluator(ctx, gate, new[] { primary })
                : !gate.StartsWith("maker.", StringComparison.Ordinal) || ctx.Ops.MakerGateHolds(gate, primary);
            if (gateMemo != null) gateMemo[gate] = r;
            return r;
        }

        /// <summary>Gate evaluations of this tab that were not answered by the gate memo (tests: the cost of a page build).</summary>
        public int GateEvaluations { get; private set; }

        /// <summary>Runs <paramref name="a"/> with the gate memo on (each gate evaluated once inside it); nested calls share the
        /// outer memo.</summary>
        void WithGateMemo(Action a)
        {
            if (gateMemo != null) { a(); return; }
            gateMemo = new Dictionary<string, bool>(StringComparer.Ordinal);
            try { a(); }
            finally { gateMemo = null; }
        }

        /// <summary>A LiveSections instance of a page of this tab (gates, visibility and rows of this host).</summary>
        LiveSections NewLiveSections()
        {
            return new LiveSections(ctx, primary)
            {
                Visible = SectionVisible,
                Fill = e => AddRows(e.Fold, e.Section, null),
                Gate = GateHolds,
                WithMemo = WithGateMemo,
            };
        }

        /// <summary>A section whose rows follow its gates (always created; hidden while every row is gated off).</summary>
        void AddLiveSection(LiveSections live, VisualElement into, SchemaSection s, int part)
        {
            var sw = Stopwatch.StartNew();
            var fo = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
            sectionFoldouts[s.Id] = fo;
            live.Add(fo, s, part);
            into.Add(fo);
            PageBuildMs["section." + s.Id] = sw.Elapsed.TotalMilliseconds;   // first build of the section (UH5 / WU5 breakdown)
        }

        static bool IsActionOf(FieldSpec f, string prefix) => f.Action != null && f.Action.StartsWith(prefix, StringComparison.Ordinal);

        /// <summary>A part / kind argument: a name of <paramref name="names"/> or its index; -1 when absent or unknown.</summary>
        static int NamedIndexArg(object v, IReadOnlyList<string> names)
        {
            if (v == null) return -1;
            if (v is string s)
            {
                for (int k = 0; k < names.Count; k++) if (names[k] == s) return k;
                return int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n >= 0 && n < names.Count ? n : -1;
            }
            try { int n = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture); return n >= 0 && n < names.Count ? n : -1; }
            catch (Exception) { return -1; }
        }

        // ---------------- edit.show

        /// <summary>Opens <paramref name="node"/> (and <paramref name="pg"/>) and scrolls <paramref name="section"/> into view.
        /// Returns the section foldout (or the page body) shown, or null. <paramref name="part"/>: the Hair page's part,
        /// <paramref name="kind"/>: the Clothes page's kind (−1 = the one a section id names, else the shown one), <paramref name="slot"/>:
        /// the Accessories page's build slot (0-based; −1 = the one a section id names, else the selected one).</summary>
        public VisualElement Reveal(string node, string pg = null, string section = null, int part = -1, int kind = -1, int slot = -1)
        {
            if (string.IsNullOrEmpty(node)) node = CurrentNode ?? "face";
            if (pg == null && section != null) pg = PageOf(section);
            // a part page: the part / kind first (before the first build the view state is set so that only that group is built)
            int hp = PrepareHairReveal(node, section, part);
            int ck = PrepareClothesReveal(node, section, kind);
            int asl = PrepareAccessoryReveal(node, section, slot);
            ShowNode(node);
            if (hp >= 0) hairParts?.Select(hp);
            if (ck >= 0) clothesKinds?.Select(ck);
            if (asl >= 0) SelectAccessorySlot(asl);
            if (pg != null) ShowPage(pg);
            VisualElement target = page;
            if (section != null && sectionFoldouts.TryGetValue(section, out var fo)) { fo.value = true; target = fo; }
            var scroll = GetFirstAncestorOfType<ScrollView>();
            if (scroll != null && target != null)
                UiDefer.Frames(2, () => { if (target.panel != null) scroll.ScrollTo(target); });
            return target;
        }

        /// <summary>Registers the action `edit.show {node, page?, section?, part?, kind?, slot?}` (the inspector module's one line).</summary>
        public static void RegisterActions(UiContext ctx)
        {
            if (ctx?.Registry?.Actions == null || !ActionManifest.Contains(ActionIds.EditShow)) return;
            ctx.Registry.Actions.Register(ActionIds.EditShow, call =>
            {
                string node = call.Arg("node") as string, pg = call.Arg("page") as string, section = call.Arg("section") as string;
                int part = NamedIndexArg(call.Arg("part"), InspectorSchemas.HairParts);
                int kind = NamedIndexArg(call.Arg("kind"), InspectorSchemas.ClothesEditKinds);
                int slot = IntArg(call.Arg("slot"));
                ctx.Shell?.FocusPanel("inspector");
                var ip = ctx.Shell?.PanelRoot("inspector") as InspectorPanel ?? ctx.Shell?.PanelRoot("inspector")?.Q<InspectorPanel>();
                var ch = ip?.Chara;
                if (ch == null) return "no character inspector";
                ch.ShowTab(TabId);
                var tab = ch.TabBodies.TryGetValue(TabId, out var b) ? b as CharaEditTab : null;
                if (tab == null) return "no edit tab";
                var shown = tab.Reveal(node, pg, section, part, kind, slot);
                var res = new Dictionary<string, object> { ["node"] = tab.CurrentNode, ["page"] = tab.CurrentPage, ["section"] = section };
                if (tab.CurrentNode == HairNode) res["part"] = InspectorSchemas.HairParts[PartSelectorState.Hair.Part];
                if (tab.CurrentNode == ClothesNode) res["kind"] = InspectorSchemas.ClothesEditKinds[PartSelectorState.Clothes.Part];
                if (tab.CurrentNode == AccessoriesNode) res["slot"] = tab.AccessoryShownSlot;
                return shown != null ? (object)res : "section not shown";
            }, () => ctx.Model != null && ctx.Selection.Primary != null && ctx.Model.Find(ctx.Selection.Primary)?.Kind == NodeKind.Chara);
        }

        // ---------------- edit session

        void BeginSession()
        {
            if (session || ctx.Model.Find(primary) == null) return;
            session = true;
            using (CharaEditSessions.FromUi()) ctx.Ops.BeginEditSession(primary);
        }

        void EndSession()
        {
            if (!session) return;
            session = false;
            if (ctx.Model.Find(primary) == null) { using (CharaEditSessions.FromUi()) CharaEditSessions.Instance.End(primary); return; }
            using (CharaEditSessions.FromUi()) ctx.Ops.EndEditSession(primary);
        }
    }
}
