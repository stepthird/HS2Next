// The Wet and stains panel (panel id "wet"): the stains of the selected character, the primary one when several are selected, which then edit nothing. From the top: the list of
// stains, the last one on top as it is drawn; the run buttons; the origin; the presets; the sections of values. Everything edits through StudioOps. Subscribed from attach to
// detach: a closed panel reads nothing. Element names (ui.query / ui.input): wet-none, wet-primary, wet-body, wet-stains, wet-add, wet-remove-all, wet-row-<i> (-name, -kept,
// -state, -up, -down, -remove; i is the place in the block), wet-run, wet-start, wet-full, wet-dry, wet-clear-puddles, wet-keep, wet-status, wet-part-changed, wet-origin,
// wet-pick-origin, wet-default-origin, wet-show-marker, wet-apply-again, wet-preset-water, wet-preset-blood, section-wet.<section>, wet-field-<path>[-<component>],
// wet-floor-heights.
using System;
using System.Collections.Generic;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class WetPanel : VisualElement
    {
        /// <summary>How often the state of a running stain is read, and how often the map's height is asked.</summary>
        public const float RefreshSeconds = 0.25f, HeightSeconds = 1f;
        const float MarkerSize = 0.6f;

        /// <summary>The panel on the shell now, or null.</summary>
        public static WetPanel Current { get; private set; }

        // the sections stay open or closed across rebuilds of the panel
        static readonly Dictionary<string, bool> opened = new Dictionary<string, bool>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => opened.Clear();

        readonly UiContext ctx;
        readonly Label none, primary, status, left, partChanged, heights;
        readonly VisualElement body, stainsList, valuesBox;
        readonly VisualElement runBar, originBar, presetBar;
        readonly Button addButton, removeAll, keep, pickButton, defaultOrigin, applyAgain, full, start, dry;
        readonly Toggle marker;
        readonly Foldout floorFold;
        readonly List<WetRow> rows = new List<WetRow>();
        readonly List<VisualElement> lines = new List<VisualElement>();
        WetRow heightRow;
        WetOriginPick pick;
        float nextRead, nextHeights;
        bool dirty = true, hidden;

        /// <summary>Reads of the block so far (tests: none while the panel is closed).</summary>
        public int Reads { get; private set; }
        /// <summary>The character shown, or null.</summary>
        public string Node { get; private set; }
        /// <summary>The id of the stain the rows show, or null.</summary>
        public string StainId { get; private set; }
        /// <summary>The stain the rows show, or null: the document's, do not hold it across an edit.</summary>
        public WetStain Stain => Node != null ? ctx.Ops.WetBlock(Node)?.Find(StainId) : null;
        /// <summary>The value rows (tests).</summary>
        public IReadOnlyList<WetRow> Rows => rows;

        public WetPanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "wet-panel";
            AddToClassList("hs-wet");
            style.flexGrow = 1;
            focusable = true;
            WetUiModule.Style(this);
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);

            none = InspectorUi.Note("wet.selectCharacter");
            none.name = "wet-none";
            Add(none);
            primary = InspectorUi.Note("wet.primaryOnly");
            primary.name = "wet-primary";
            Add(primary);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "wet-scroll" };
            scroll.AddToClassList("hs-wet__scroll");
            Add(scroll);
            body = new VisualElement { name = "wet-body" };
            scroll.Add(body);

            var bar = Bar("wet-stains-bar");
            addButton = Btn(bar, "wet-add", "wet.addStain", () => Apply(ctx.Ops.WetAdd(Node, WetPreset.Water, null, UiContext.User(OpLabels.WetAdd))));
            removeAll = Btn(bar, "wet-remove-all", "wet.removeAll", () => Apply(ctx.Ops.WetRemoveAll(Node, UiContext.User(OpLabels.WetRemove))));
            body.Add(bar);
            stainsList = new VisualElement { name = "wet-stains" };
            body.Add(stainsList);
            var noStain = InspectorUi.Note("wet.none");
            noStain.name = "wet-no-stain";
            body.Add(noStain);

            runBar = Bar("wet-run");
            start = Btn(runBar, "wet-start", "wet.start", () => Apply(ctx.Ops.WetStart(Node, StainId, null, UiContext.User(OpLabels.WetStart))));
            full = Btn(runBar, "wet-full", "wet.full", () => Apply(ctx.Ops.WetFull(Node, StainId, UiContext.User(OpLabels.WetEdit))));
            dry = Btn(runBar, "wet-dry", "wet.dry", () => Apply(ctx.Ops.WetDry(Node, StainId, false, UiContext.User(OpLabels.WetDry))));
            Btn(runBar, "wet-clear-puddles", "wet.clearPuddles", () => Apply(ctx.Ops.WetClearPuddles(Node, UiContext.User(OpLabels.WetClearPuddles))));
            keep = Btn(runBar, "wet-keep", "wet.keep", () => Apply(ctx.Ops.WetKeep(Node, UiContext.User(OpLabels.WetKeep))));
            body.Add(runBar);
            status = Text("wet-status", "hs-wet__status");
            left = Text("wet-left", "hs-wet__status");
            partChanged = Loc.Label("wet.status.partChanged");
            partChanged.name = "wet-part-changed";
            partChanged.AddToClassList("hs-wet__warning");
            body.Add(partChanged);

            presetBar = Bar("wet-presets");
            Btn(presetBar, "wet-preset-water", "wet.preset.water", () => Preset(WetPreset.Water));
            Btn(presetBar, "wet-preset-blood", "wet.preset.blood", () => Preset(WetPreset.Blood));
            body.Add(presetBar);

            originBar = Bar("wet-origin");
            pickButton = Btn(originBar, "wet-pick-origin", "wet.pickOrigin", BeginPick);
            defaultOrigin = Btn(originBar, "wet-default-origin", "wet.defaultOrigin", () => Apply(ctx.Ops.WetOriginDefault(Node, StainId, UiContext.User(OpLabels.WetOrigin))));
            applyAgain = Btn(originBar, "wet-apply-again", "wet.applyAgain", () => Apply(ctx.Ops.WetApplyAgain(Node, StainId, UiContext.User(OpLabels.WetEdit))));
            marker = new Toggle { name = "wet-show-marker" };
            Loc.Bind(marker, "wet.showMarker", (e, s) => ((Toggle)e).label = s);
            marker.RegisterValueChangedCallback(e => { if (e.target == marker) ctx.Ops.WetShowMarker = e.newValue; });
            originBar.Add(marker);
            body.Add(originBar);

            valuesBox = new VisualElement { name = "wet-values" };
            body.Add(valuesBox);
            BuildSections(out floorFold);
            heights = new Label { name = "wet-floor-heights" };
            heights.AddToClassList("hs-wet__status");
            floorFold.Add(heights);
        }

        Label Text(string id, string cls)
        {
            var l = new Label { name = id };
            l.AddToClassList(cls);
            body.Add(l);
            return l;
        }

        static VisualElement Bar(string id)
        {
            var b = new VisualElement { name = id };
            b.AddToClassList("hs-wet__bar");
            return b;
        }

        static Button Btn(VisualElement parent, string id, string key, Action click)
        {
            var b = Loc.Button(key, click);
            b.name = id;
            parent.Add(b);
            return b;
        }

        /// <summary>A row's default button.</summary>
        internal static Button ResetButton(Action click)
        {
            var b = new Button(click) { name = "reset", text = "↺" };
            b.AddToClassList("hs-wet__btn");
            Loc.Tooltip(b, "wet.reset");
            return b;
        }

        void Apply(OpResult r)
        {
            ctx.ShowResult(r);
            MarkDirty();
        }

        void Preset(string preset) => Apply(ctx.Ops.WetSet(Node, StainId, "preset", preset, UiContext.User(OpLabels.WetEdit)));

        /// <summary>The panel reads the block again in its next frame.</summary>
        public void MarkDirty() => dirty = true;

        // ------------------------------------------------------------ the sections

        Foldout Section(string titleKey, bool open = false)
        {
            bool shown = opened.TryGetValue(titleKey, out bool o) ? o : open;
            var fold = InspectorUi.Section(titleKey, shown, "section-" + titleKey);
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) opened[titleKey] = e.newValue; });
            valuesBox.Add(fold);
            return fold;
        }

        T On<T>(Foldout fold, T row) where T : WetRow
        {
            fold.Add(row);
            rows.Add(row);
            return row;
        }

        WetNumberRow Num(Foldout fold, string path, string key, WetRange? range, int component = -1) => On(fold, new WetNumberRow(ctx, this, path, key, range, component));
        WetSwitchRow Sw(Foldout fold, string path, string key) => On(fold, new WetSwitchRow(ctx, this, path, key));
        WetColourRow Col(Foldout fold, string path, string key) => On(fold, new WetColourRow(ctx, this, path, key));

        void BuildSections(out Foldout floor)
        {
            var run = Section("wet.run", true);
            Sw(run, "loop", "wet.loop");
            Num(run, "hold", "wet.hold", WetRanges.Hold);
            var origin = Section("wet.origin");
            Num(origin, "origin.down", "wet.origin.down", new WetRange(-5f, 5f));
            Num(origin, "origin.out", "wet.origin.out", new WetRange(0f, 2f));
            var shape = Section("wet.shape", true);
            Num(shape, "shape.radius", "wet.shape.radius", WetRanges.Radius);
            Num(shape, "shape.spread", "wet.shape.spread", WetRanges.Spread);
            Num(shape, "shape.up", "wet.shape.up", WetRanges.Up);
            Num(shape, "shape.down", "wet.shape.down", WetRanges.Down);
            Num(shape, "shape.front", "wet.shape.front", WetRanges.Axis);
            Num(shape, "shape.back", "wet.shape.back", WetRanges.Axis);
            Num(shape, "shape.side", "wet.shape.side", WetRanges.Axis);
            Num(shape, "shape.outer", "wet.shape.outer", WetRanges.Outer);
            Num(shape, "shape.noise", "wet.shape.noise", WetRanges.Noise);
            Num(shape, "shape.feather", "wet.shape.feather", WetRanges.Feather);
            Sw(shape, "targets.skin", "wet.targets.skin");
            Sw(shape, "targets.hair", "wet.targets.hair");
            Sw(shape, "targets.accessories", "wet.targets.accessories");
            Sw(shape, "targets.shoes", "wet.targets.shoes");
            Sw(shape, "targets.gloves", "wet.targets.gloves");
            var look = Section("wet.look", true);
            Num(look, "look.saturate", "wet.look.saturate", WetRanges.Saturate);
            Num(look, "look.tint", "wet.look.tint.r", WetRanges.Tint, 0);
            Num(look, "look.tint", "wet.look.tint.g", WetRanges.Tint, 1);
            Num(look, "look.tint", "wet.look.tint.b", WetRanges.Tint, 2);
            Col(look, "look.stain", "wet.look.stain");
            Num(look, "look.amount", "wet.look.amount", WetRanges.Unit);
            Num(look, "look.skin", "wet.look.skin", WetRanges.Unit);
            var film = Section("wet.film");
            Sw(film, "film.on", "wet.film.on");
            Num(film, "film.gloss", "wet.film.gloss", WetRanges.Unit);
            Num(film, "film.smooth", "wet.film.smooth", WetRanges.Unit);
            Num(film, "film.opacity", "wet.film.opacity", WetRanges.Unit);
            var drips = Section("wet.drips");
            Sw(drips, "drips.on", "wet.drips.on");
            Num(drips, "drips.rate", "wet.drips.rate", WetRanges.DripRate);
            Num(drips, "drips.tail", "wet.drips.tail", WetRanges.DripTail);
            Num(drips, "drips.size", "wet.drips.size", WetRanges.DripSize);
            Num(drips, "drips.growth", "wet.drips.growth", WetRanges.DripGrowth);
            Sw(drips, "drips.skin", "wet.drips.skin");
            Col(drips, "drips.color", "wet.drips.color");
            Num(drips, "drips.shine", "wet.drips.shine", WetRanges.Unit);
            var puddle = Section("wet.puddle");
            Col(puddle, "puddle.color", "wet.puddle.color");
            Num(puddle, "puddle.shine", "wet.puddle.shine", WetRanges.Unit);
            Num(puddle, "puddle.edge", "wet.puddle.edge", WetRanges.PuddleEdge);
            Num(puddle, "puddle.scale", "wet.puddle.scale", WetRanges.PuddleScale);
            var mirror = Section("wet.mirror");
            Sw(mirror, "mirror.on", "wet.mirror.on");
            Num(mirror, "mirror.strength", "wet.mirror.strength", WetRanges.Unit);
            Num(mirror, "mirror.scale", "wet.mirror.scale", WetRanges.MirrorScale);
            floor = Section("wet.floor");
            On(floor, new WetFloorModeRow(ctx, this));
            Num(floor, "floor.offset", "wet.floor.offset", WetRanges.FloorOffset);
            heightRow = Num(floor, "floor.height", "wet.floor.height", null);
        }

        // ------------------------------------------------------------ life

        void OnAttached(AttachToPanelEvent e)
        {
            Current = this;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneChanged += OnSceneChanged;
            ctx.SceneCleared -= MarkDirty;
            ctx.SceneCleared += MarkDirty;
            ctx.SceneLoaded -= OnLoaded;
            ctx.SceneLoaded += OnLoaded;
            ctx.SelectionChanged -= OnSelection;
            ctx.SelectionChanged += OnSelection;
            ctx.Tools.ViewDraw -= DrawMarker;
            ctx.Tools.ViewDraw += DrawMarker;
            dirty = true;
        }

        void OnDetached(DetachFromPanelEvent e)
        {
            if (Current == this) Current = null;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneCleared -= MarkDirty;
            ctx.SceneLoaded -= OnLoaded;
            ctx.SelectionChanged -= OnSelection;
            ctx.Tools.ViewDraw -= DrawMarker;
            if (pick != null) ctx.Tools.End(pick);
        }

        void OnLoaded(SceneLoadedArgs a) => dirty = true;
        void OnSelection(SelectionChangedArgs a) => dirty = true;

        void OnSceneChanged(SceneChangeBatch b)
        {
            if (Node == null || b.Touches(Node, null)) dirty = true;
        }

        bool ShownNow()
        {
            if (panel == null) return false;
            for (VisualElement e = this; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        void Tick()
        {
            float now = Time.unscaledTime;
            bool due = now >= nextRead;
            if (!due && (hidden || !dirty)) return;
            if (due) nextRead = now + RefreshSeconds;
            hidden = !ShownNow();
            if (hidden || (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen)) return;
            dirty = false;
            Read();
        }

        // ------------------------------------------------------------ reading

        string PrimaryCharacter()
        {
            string id = ctx.Selection.Primary;
            var n = id != null ? ctx.Model.Find(id) : null;
            return n != null && n.Kind == NodeKind.Chara ? id : null;
        }

        void Read()
        {
            Reads++;
            Node = PrimaryCharacter();
            bool has = Node != null;
            none.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            body.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            primary.style.display = has && ctx.Selection.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            body.SetEnabled(has && ctx.Selection.Count == 1);
            StainId = null;
            if (!has) return;
            var block = ctx.Ops.WetBlock(Node);
            var stain = block?.CurrentStain;
            StainId = stain?.Id;
            ReadStains(block);
            bool any = stain != null;
            body.Q("wet-no-stain").style.display = block == null || block.Stains.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            var shown = any ? DisplayStyle.Flex : DisplayStyle.None;
            runBar.style.display = originBar.style.display = presetBar.style.display = valuesBox.style.display = shown;
            removeAll.SetEnabled(block != null && !block.IsEmpty);
            addButton.SetEnabled(block == null || block.Stains.Count < WetLimits.MaxStains);
            var st = ctx.Ops.WetStatus(Node);
            status.style.display = left.style.display = partChanged.style.display = DisplayStyle.None;
            if (!any) return;
            foreach (var b in new[] { keep, start, full, dry, applyAgain, pickButton, defaultOrigin }) b.SetEnabled(!stain.Kept);
            foreach (var n in new[] { "wet-preset-water", "wet-preset-blood" }) presetBar.Q<Button>(n).SetEnabled(!stain.Kept);
            marker.SetValueWithoutNotify(ctx.Ops.WetShowMarker);
            foreach (var r in rows) r.Read(block, stain);
            heightRow.style.display = block.Floor.AppliedMode == WetFloorValues.ModeFixed ? DisplayStyle.Flex : DisplayStyle.None;
            if (st != null)
            {
                status.text = ctx.L10n.Format("wet.status.line", Num(st.T), Num(st.Radius), st.Targets, st.Puddles);
                status.style.display = DisplayStyle.Flex;
                int dryParts = st.Unreadable + st.Tiled;
                left.text = dryParts > 0 ? ctx.L10n.Format("wet.status.left", dryParts) : "";
                left.style.display = dryParts > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                partChanged.style.display = st.Dormant > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ReadHeights(block);
        }

        static string Num(double d) => NumberText.Of(d, "0.##");

        // what the floor would be by each mode; the map's height is a question to the device, so the section open and once a second at most
        void ReadHeights(CharaWet block)
        {
            if (!floorFold.value || Time.unscaledTime < nextHeights) return;
            nextHeights = Time.unscaledTime + HeightSeconds;
            var rig = ctx.Runtime?.GetPart<WetPart>()?.RigOf(Node);
            if (rig == null) { heights.text = ""; return; }
            float map = rig.FloorFor(new WetFloorValues { Mode = WetFloorValues.ModeMap }, out string source);
            float feet = rig.FloorFor(new WetFloorValues { Mode = WetFloorValues.ModeFeet }, out _);
            float now = rig.FloorFor(block.Floor, out _);
            heights.text = ctx.L10n.Format("wet.floor.shown", source == WetFloorSources.Map ? Num(map) : "—", Num(feet), Num(now));
        }

        void ReadStains(CharaWet block)
        {
            int n = block?.Stains.Count ?? 0;
            while (lines.Count > n) { lines[lines.Count - 1].RemoveFromHierarchy(); lines.RemoveAt(lines.Count - 1); }
            while (lines.Count < n)
            {
                var l = Line(lines.Count);
                lines.Add(l);
                stainsList.Insert(0, l);   // the last stain is drawn on top
            }
            for (int i = 0; i < n; i++)
            {
                var s = block.Stains[i];
                var l = lines[i];
                l.EnableInClassList("hs-wet__line--current", s.Id == block.Current);
                l.Q<Button>("wet-row-" + i + "-name").text = s.Name;
                l.Q<Label>("wet-row-" + i + "-kept").text = s.Kept ? ctx.L10n.Get("wet.kept") : "";
                l.Q<Label>("wet-row-" + i + "-state").text = ctx.L10n.Get(s.Running ? "wet.state.running" : s.T > 0 ? "wet.state.stopped" : "wet.state.dry");
                l.Q<Button>("wet-row-" + i + "-up").SetEnabled(i + 1 < n);
                l.Q<Button>("wet-row-" + i + "-down").SetEnabled(i > 0);
            }
        }

        string IdAt(int i)
        {
            var b = ctx.Ops.WetBlock(Node);
            return b != null && i < b.Stains.Count ? b.Stains[i].Id : null;
        }

        VisualElement Line(int i)
        {
            var l = new VisualElement { name = "wet-row-" + i };
            l.AddToClassList("hs-wet__line");
            var nm = new Button(() => Apply(ctx.Ops.WetUse(Node, IdAt(i), false, UiContext.User(OpLabels.WetEdit)))) { name = "wet-row-" + i + "-name" };
            nm.AddToClassList("hs-wet__name");
            l.Add(nm);
            var kept = new Label { name = "wet-row-" + i + "-kept" };
            kept.AddToClassList("hs-wet__state");
            l.Add(kept);
            var state = new Label { name = "wet-row-" + i + "-state" };
            state.AddToClassList("hs-wet__state");
            l.Add(state);
            Small(l, "wet-row-" + i + "-up", "▲", "wet.moveUp", () => Apply(ctx.Ops.WetMove(Node, IdAt(i), i + 1, UiContext.User(OpLabels.WetEdit))));
            Small(l, "wet-row-" + i + "-down", "▼", "wet.moveDown", () => Apply(ctx.Ops.WetMove(Node, IdAt(i), i - 1, UiContext.User(OpLabels.WetEdit))));
            Small(l, "wet-row-" + i + "-remove", "✕", "wet.remove", () => Apply(ctx.Ops.WetRemove(Node, IdAt(i), UiContext.User(OpLabels.WetRemove))));
            return l;
        }

        static void Small(VisualElement parent, string id, string glyph, string tip, Action click)
        {
            var b = new Button(click) { name = id, text = glyph };
            b.AddToClassList("hs-wet__btn");
            Loc.Tooltip(b, tip);
            parent.Add(b);
        }

        // ------------------------------------------------------------ the origin

        void BeginPick()
        {
            if (Node == null || StainId == null) return;
            var tool = new WetOriginPick(this, ctx, Node, StainId);
            pick = tool;
            if (ctx.Tools.Begin(tool, new ToolOptions { Filter = new ToolPickFilter { NodeId = Node }, Hint = "wet.pickOrigin.hint" })) return;
            pick = null;
            if (ctx.Tools.Refusal != null) ctx.Toasts?.Show(ctx.L10n.Get(ctx.Tools.Refusal), ToastKind.Warning);
        }

        internal void PickEnded(WetOriginPick tool)
        {
            if (pick == tool) pick = null;
        }

        // a cross at the current stain's origin: in the view only, never in a capture
        void DrawMarker(IToolDraw draw)
        {
            if (Node == null || !ctx.Ops.WetShowMarker || !OriginPoint(out var p)) return;
            draw.Marker(p, MarkerSize, Color.yellow);
        }

        bool OriginPoint(out Vector3 p)
        {
            p = default;
            var s = Stain;
            var rt = ctx.Runtime;
            var real = rt?.FindChara(Node);
            if (s == null || real == null || real.State != RealizationState.Built) return false;
            if (s.Origin.Mode == WetOriginValues.ModeBone)
            {
                var bone = AdvancedPoseResolver.Resolve(real, s.Origin.Bone);
                if (!bone) return false;
                p = bone.TransformPoint(new Vector3(s.Origin.Local.X, s.Origin.Local.Y, s.Origin.Local.Z));
                return true;
            }
            var rig = rt.GetPart<WetPart>()?.RigOf(Node);
            if (rig == null) return false;
            foreach (var run in rig.Runs)
            {
                if (run.Doc?.Id != s.Id || !run.OriginBone) continue;
                p = run.OriginBone.TransformPoint(run.OriginLocal);
                return true;
            }
            return false;
        }
    }

    /// <summary>«Pick origin»: one press on the character sets the origin of the stain; Esc leaves. A press that meets no bone of it does nothing, and the tool goes on.</summary>
    sealed class WetOriginPick : ViewportTool
    {
        readonly WetPanel owner;
        readonly UiContext ctx;
        readonly string node, stain;

        public WetOriginPick(WetPanel owner, UiContext ctx, string node, string stain)
        {
            this.owner = owner;
            this.ctx = ctx;
            this.node = node;
            this.stain = stain;
        }

        public override bool PointerDown(in ToolPointer p)
        {
            if (p.Button != 0) return false;
            var hit = p.Hit;
            if (!hit.Hit || string.IsNullOrEmpty(hit.BoneAddress)) return false;
            var r = ctx.Ops.WetOrigin(node, stain, hit.BoneAddress, new Vec3(hit.BoneLocal.x, hit.BoneLocal.y, hit.BoneLocal.z), UiContext.User(OpLabels.WetOrigin));
            ctx.ShowResult(r);
            ctx.Tools.End(this);
            owner.MarkDirty();
            return true;
        }

        public override void Ended(ToolEnd reason) => owner.PickEnded(this);
    }
}
