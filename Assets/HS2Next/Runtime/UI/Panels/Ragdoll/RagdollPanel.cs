// The Ragdoll panel (panel id "ragdoll"): the selected character, the primary one when several are selected, which then edit nothing of it, and the scene. From the top: the body's buttons, the
// presets and the state; the tool; the pins, ties and holds; the sections of values. With an item or a folder selected the panel stays on the last character, so that a pin can follow what is
// selected. Everything edits through StudioOps, so the panel works with the bridge off. Subscribed from attach to detach: a closed panel reads nothing, and the state is read four times a second
// only while the panel is shown. Element names (ui.query / ui.input): ragdoll-none, ragdoll-primary, ragdoll-node, ragdoll-body, ragdoll-make, ragdoll-release, ragdoll-release-all, ragdoll-bake,
// ragdoll-snap, ragdoll-preset-<preset>, ragdoll-state, ragdoll-tool, ragdoll-mode (one of ragdoll-mode-off, ragdoll-grab, ragdoll-shoot), ragdoll-outlines, ragdoll-colliders, ragdoll-room (the outlines show
// in the view while the panel is shown and a body exists, with or without the tool, never in a capture), ragdoll-hint, ragdoll-pins, ragdoll-pin-<i> (-follow, -stay,
// -unpin), ragdoll-tie-<i>-remove, ragdoll-hold-<i>-remove, ragdoll-unpin-all, ragdoll-part-a, ragdoll-part-b, ragdoll-add-pin, ragdoll-add-tie, ragdoll-add-hold, section-<title key>,
// ragdoll-field-<name>[-<component>], ragdoll-knock, ragdoll-reseat, ragdoll-clear-props, ragdoll-last-hit, ragdoll-stain, ragdoll-blood, ragdoll-room-at-selection, ragdoll-obstacle.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class RagdollPanel : VisualElement
    {
        /// <summary>How often the state is read, and how long after the last change a room's slider writes.</summary>
        public const float RefreshSeconds = 0.25f, RoomDelay = 0.3f;

        /// <summary>The panel on the shell now, or null.</summary>
        public static RagdollPanel Current { get; private set; }

        // the sections stay open or closed across rebuilds of the panel
        static readonly Dictionary<string, bool> opened = new Dictionary<string, bool>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => opened.Clear();

        static readonly RagdollWorldValues worldDefaults = new RagdollWorldValues();

        readonly UiContext ctx;
        readonly Label none, primary, nodeName, stateLine, hint, lastHit;
        readonly VisualElement body, pinsList, pinsBox;
        readonly Button makeButton, releaseButton, bakeButton, snapButton, unpinAll;
        // the tool's mode: 0 off, 1 grab, 2 shoot (one of three)
        public const int ModeOff = 0, ModeGrab = 1, ModeShoot = 2;
        static readonly string[] modeKeys = { "ragdoll.mode.off", "ragdoll.grab", "ragdoll.shoot" };
        static readonly string[] modeNames = { "ragdoll-mode-off", "ragdoll-grab", "ragdoll-shoot" };
        readonly SegmentedButtons modeButtons;
        readonly Toggle collidersToggle, roomToggle, stainToggle, bloodToggle, obstacleToggle;
        readonly DropdownField partA, partB;
        readonly List<VisualElement> characterOnly = new List<VisualElement>();
        readonly List<RagdollRow> rows = new List<RagdollRow>();
        RagdollPlaceRow heightRow;
        RagdollTool tool;
        string listed, lastCharacter;
        float nextRead, delayDue;
        bool dirty = true, hidden;
        // what a stopped slider has yet to write
        readonly Dictionary<string, JToken> delayed = new Dictionary<string, JToken>(StringComparer.Ordinal);

        /// <summary>Reads of the blocks so far (tests: none while the panel is closed).</summary>
        public int Reads { get; private set; }
        /// <summary>The character shown, or null.</summary>
        public string Node { get; private set; }
        /// <summary>The value rows (tests).</summary>
        public IReadOnlyList<RagdollRow> Rows => rows;
        /// <summary>The tool the panel began, while it runs.</summary>
        public RagdollTool Tool => tool;
        /// <summary>The merge key of what a slider writes when it stops.</summary>
        public string DelayKey { get; set; }
        /// <summary>A stopped slider has yet to write.</summary>
        public bool Delayed => delayed.Count > 0;

        public RagdollPanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "ragdoll-panel";
            AddToClassList("hs-ragdoll");
            style.flexGrow = 1;
            focusable = true;
            RagdollUiModule.Style(this);
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);

            none = InspectorUi.Note("ragdoll.selectCharacter");
            none.name = "ragdoll-none";
            Add(none);
            primary = InspectorUi.Note("ragdoll.primaryOnly");
            primary.name = "ragdoll-primary";
            Add(primary);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "ragdoll-scroll" };
            scroll.AddToClassList("hs-ragdoll__scroll");
            Add(scroll);
            body = new VisualElement { name = "ragdoll-content" };
            scroll.Add(body);

            nodeName = Text("ragdoll-node", "hs-ragdoll__name");
            var bar = Bar("ragdoll-body");
            makeButton = Btn(bar, "ragdoll-make", "ragdoll.make", () => Apply(ctx.Ops.RagdollBuild(Node, true, null, UiContext.User(OpLabels.RagdollBuild))));
            releaseButton = Btn(bar, "ragdoll-release", "ragdoll.release", () => Apply(ctx.Ops.RagdollRelease(Node, true, UiContext.User(OpLabels.RagdollRelease))));
            Btn(bar, "ragdoll-release-all", "ragdoll.releaseAll", () => Apply(ctx.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease))));
            bakeButton = Btn(bar, "ragdoll-bake", "ragdoll.bake", () => Apply(ctx.Ops.RagdollBake(Node, UiContext.User(OpLabels.RagdollBake))));
            snapButton = Btn(bar, "ragdoll-snap", "ragdoll.snap", () => Apply(ctx.Ops.RagdollSnap(Node, UiContext.User(OpLabels.RagdollSettings))));
            body.Add(bar);
            var presets = Bar("ragdoll-presets");
            foreach (string p in RagdollPreset.Names)
            {
                string preset = p;
                Btn(presets, "ragdoll-preset-" + preset, "ragdoll.preset." + preset, () => Apply(ctx.Ops.RagdollUsePreset(Node, preset, UiContext.User(OpLabels.RagdollSettings))));
            }
            body.Add(presets);
            characterOnly.Add(presets);
            stateLine = Text("ragdoll-state", "hs-ragdoll__status");
            characterOnly.Add(stateLine);

            var toolBar = Bar("ragdoll-tool");
            modeButtons = new SegmentedButtons(new[] { "", "", "" }) { name = "ragdoll-mode" };
            for (int i = 0; i < modeKeys.Length; i++)
            {
                var b = modeButtons.Buttons[i];
                b.name = modeNames[i];
                Loc.Bind(b, modeKeys[i], (e, s) => { ((Button)e).text = s; e.tooltip = s; });
            }
            modeButtons.Selected += ModeChosen;
            toolBar.Add(modeButtons);
            body.Add(toolBar);
            var outlineBar = Bar("ragdoll-outlines");   // a line of its own: the three choices above take the width
            collidersToggle = Switch(outlineBar, "ragdoll-colliders", "ragdoll.colliders", on => RagdollTool.ShowColliders = on);
            roomToggle = Switch(outlineBar, "ragdoll-room", "ragdoll.room", on => RagdollTool.ShowRoom = on);
            Loc.Tooltip(collidersToggle, "ragdoll.outlines.hint");
            Loc.Tooltip(roomToggle, "ragdoll.outlines.hint");
            body.Add(outlineBar);
            hint = Text("ragdoll-hint", "hs-ragdoll__status");

            pinsBox = new VisualElement { name = "ragdoll-pins" };
            body.Add(pinsBox);
            characterOnly.Add(pinsBox);
            pinsList = new VisualElement { name = "ragdoll-pins-list" };
            pinsBox.Add(pinsList);
            var add = Bar("ragdoll-pins-add");
            partA = Parts("ragdoll-part-a", "hand_L");
            partB = Parts("ragdoll-part-b", "hand_R");
            add.Add(partA);
            add.Add(partB);
            Btn(add, "ragdoll-add-pin", "ragdoll.pin", () => Apply(ctx.Ops.RagdollPin(Node, PartOf(partA), null, null, null, false, UiContext.User(OpLabels.RagdollPin))));
            Btn(add, "ragdoll-add-tie", "ragdoll.tie", () => Apply(ctx.Ops.RagdollTie(Node, PartOf(partA), PartOf(partB), UiContext.User(OpLabels.RagdollTie))));
            Btn(add, "ragdoll-add-hold", "ragdoll.hold", () => Apply(ctx.Ops.RagdollHold(Node, PartOf(partA), UiContext.User(OpLabels.RagdollHold))));
            unpinAll = Btn(add, "ragdoll-unpin-all", "ragdoll.unpinAll", () => Apply(ctx.Ops.RagdollUnpin(Node, null, UiContext.User(OpLabels.RagdollUnpin))));
            pinsBox.Add(add);

            BuildSections(out lastHit, out stainToggle, out bloodToggle, out obstacleToggle);
        }

        // ------------------------------------------------------------ pieces

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
            b.AddToClassList("hs-ragdoll__bar");
            return b;
        }

        static Button Btn(VisualElement parent, string id, string key, Action click)
        {
            var b = Loc.Button(key, click);
            b.name = id;
            parent.Add(b);
            return b;
        }

        static Toggle Switch(VisualElement parent, string id, string key, Action<bool> changed)
        {
            var t = new Toggle { name = id };
            Loc.Bind(t, key, (e, s) => ((Toggle)e).label = s);
            t.RegisterValueChangedCallback(e => { if (e.target == t) changed(e.newValue); });
            parent.Add(t);
            return t;
        }

        /// <summary>A row's default button.</summary>
        internal static Button ResetButton(Action click)
        {
            var b = new Button(click) { name = "reset", text = "↺" };
            b.AddToClassList("hs-ragdoll__btn");
            Loc.Tooltip(b, "ragdoll.reset");
            return b;
        }

        DropdownField Parts(string id, string first)
        {
            var d = new DropdownField { name = id, choices = new List<string>(RagdollParts.Keys), value = first };
            d.formatListItemCallback = PartName;
            d.formatSelectedValueCallback = PartName;
            d.AddToClassList("hs-ragdoll__part");
            Loc.Bind(d, RagdollUiModule.TitleKey, (e, _) => { var x = (DropdownField)e; x.SetValueWithoutNotify(x.value); });
            return d;
        }

        string PartName(string key) => string.IsNullOrEmpty(key) ? "" : ctx.L10n.Get("ragdoll.part." + key);

        static string PartOf(DropdownField d) => d.value;

        void Apply(OpResult r)
        {
            ctx.ShowResult(r);
            MarkDirty();
        }

        /// <summary>The panel reads the blocks again in its next frame.</summary>
        public void MarkDirty() => dirty = true;

        // ------------------------------------------------------------ the sections

        Foldout Section(string titleKey, bool open = false)
        {
            bool shown = opened.TryGetValue(titleKey, out bool o) ? o : open;
            var fold = InspectorUi.Section(titleKey, shown, "section-" + titleKey);
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) opened[titleKey] = e.newValue; });
            body.Add(fold);
            return fold;
        }

        T On<T>(Foldout fold, T row) where T : RagdollRow
        {
            fold.Add(row);
            rows.Add(row);
            if (row.OfBody) characterOnly.Add(row);
            return row;
        }

        void Num(Foldout f, string value) => On(f, new RagdollNumberRow(ctx, this, value, "ragdoll.value." + value));
        void Sw(Foldout f, string value) => On(f, new RagdollSwitchRow(ctx, this, value, "ragdoll.value." + value));

        void BuildSections(out Label hitLine, out Toggle stain, out Toggle blood, out Toggle obstacle)
        {
            var muscles = Section("ragdoll.section.muscles", true);
            foreach (string v in new[] { "muscle", "muscleDamping", "pin", "pinFreq", "pinFalloff" }) Num(muscles, v);
            Sw(muscles, "pinHipsOnly");
            Sw(muscles, "gravityComp");
            Num(muscles, "blendOut");
            characterOnly.Add(muscles);

            var knock = Section("ragdoll.section.knockdown");
            foreach (string v in new[] { "knockDistance", "knockHit", "downMuscle", "minDown", "getUp" }) Num(knock, v);
            foreach (string v in new[] { "relocate", "faceCamera", "legCollision" }) Sw(knock, v);
            var knockBar = Bar("ragdoll-knock-bar");
            Btn(knockBar, "ragdoll-knock", "ragdoll.knock", () => Apply(ctx.Ops.RagdollKnock(Node)));
            Btn(knockBar, "ragdoll-reseat", "ragdoll.reseat", () => Apply(ctx.Ops.RagdollReseat(Node)));
            knock.Add(knockBar);
            characterOnly.Add(knock);

            var props = Section("ragdoll.section.props");
            On(props, new RagdollChoiceRow(ctx, this, "propKind", RagdollWorldValues.PropKinds, "ragdoll.prop."));
            foreach (string v in new[] { "propMass", "propSpeed", "propSize" }) Num(props, v);
            var propBar = Bar("ragdoll-props-bar");
            Btn(propBar, "ragdoll-clear-props", "ragdoll.clearProps", () => Apply(ctx.Ops.RagdollClearProps()));
            props.Add(propBar);
            hitLine = new Label { name = "ragdoll-last-hit" };
            hitLine.AddToClassList("hs-ragdoll__status");
            props.Add(hitLine);

            var shots = Section("ragdoll.section.shots");
            foreach (string v in new[] { "momentum", "transfer", "maxPartDv", "hitWeaken", "weakenRegain", "weakMuscle", "staggerAt", "fireRate" }) Num(shots, v);
            stain = Switch(shots, "ragdoll-stain", "ragdoll.stainOnHit", on => RagdollTool.StainOnHit = on);
            blood = Switch(shots, "ragdoll-blood", "options.blood", on => ctx.Options.Set(OptionDefs.StudioBlood, on));

            var world = Section("ragdoll.section.world");
            foreach (string v in new[] { "timeScale", "gravity", "pushSpeed", "grabStiffness", "grabMax", "followStiffness", "followMax", "angularDrag", "friction", "hz", "mass" }) Num(world, v);

            var room = Section("ragdoll.section.room");
            var size = RagdollRoomValues.DefaultSize;
            On(room, new RagdollPlaceRow(ctx, this, RagdollEdits.RoomSize, "ragdoll.value.roomW", RagdollRanges.RoomWidth, size.X, 0));
            On(room, new RagdollPlaceRow(ctx, this, RagdollEdits.RoomSize, "ragdoll.value.roomH", RagdollRanges.RoomHeight, size.Y, 1));
            On(room, new RagdollPlaceRow(ctx, this, RagdollEdits.RoomSize, "ragdoll.value.roomD", RagdollRanges.RoomWidth, size.Z, 2));
            On(room, new RagdollChoiceRow(ctx, this, RagdollEdits.FloorMode, RagdollFloorValues.Modes, "ragdoll.floor."));
            On(room, new RagdollPlaceRow(ctx, this, RagdollEdits.FloorOffset, "ragdoll.value.floorOffset", null, 0));
            heightRow = On(room, new RagdollPlaceRow(ctx, this, RagdollEdits.FloorHeight, "ragdoll.value.floorHeight", null, 0));
            var roomBar = Bar("ragdoll-room-bar");
            Btn(roomBar, "ragdoll-room-at-selection", "ragdoll.roomAtSelection", RoomAtSelection);
            room.Add(roomBar);
            obstacle = Switch(room, "ragdoll-obstacle", "ragdoll.obstacle", ObstacleChanged);
        }

        // ------------------------------------------------------------ the room and the floor

        /// <summary>What a place row shows: the block's value, or the default with no block; a component of room.size with <paramref name="component"/> 0 to 2.</summary>
        public double PlaceNow(string value, int component, SceneRagdollWorld block)
        {
            if (value == RagdollEdits.FloorOffset) return block?.Floor.Offset ?? 0f;
            if (value == RagdollEdits.FloorHeight) return block?.Floor.Height ?? 0f;
            var s = block?.Room.AppliedSize ?? RagdollRoomValues.DefaultSize;
            return component == 0 ? s.X : component == 1 ? s.Y : s.Z;
        }

        /// <summary>A slider changed: the value goes in at its time, 0.3 s of real time after the last change.</summary>
        public void Delay(string value, int component, float v)
        {
            delayDue = Time.unscaledTime + RoomDelay;
            if (component < 0) { delayed[value] = new JValue(v); return; }
            if (!delayed.TryGetValue(value, out var t))
            {
                var s = ctx.Ops.RagdollWorldBlock?.Room.AppliedSize ?? RagdollRoomValues.DefaultSize;
                delayed[value] = t = new JArray(s.X, s.Y, s.Z);
            }
            ((JArray)t)[component] = v;
        }

        /// <summary>What a drag holds goes away: Esc.</summary>
        public void DropDelayed()
        {
            delayed.Clear();
            MarkDirty();
        }

        /// <summary>Writes what the sliders hold, at once.</summary>
        public void Flush()
        {
            if (delayed.Count == 0) return;
            var list = new List<KeyValuePair<string, JToken>>(delayed);
            delayed.Clear();
            ctx.ShowResult(ctx.Ops.RagdollSet(null, list, null, false, UiContext.User(OpLabels.RagdollSettings, DelayKey)));
            MarkDirty();
        }

        void RoomAtSelection()
        {
            if (!SelectedPlace(out var p))
            {
                Note("ragdoll.selectFirst");
                return;
            }
            Apply(ctx.Ops.RagdollValue(null, RagdollEdits.RoomCenter, new JArray(p.x, p.z), UiContext.User(OpLabels.RagdollSettings)));
        }

        // where the selection stands: a body's pelvis, else its holder
        bool SelectedPlace(out Vector3 p)
        {
            p = default;
            string id = ctx.Selection.Primary;
            if (id == null) return false;
            var part = ctx.Ops.RagdollBodyOf(id)?.Part(RagdollParts.Pelvis);
            if (part != null)
            {
                p = part.Tr.position;
                return true;
            }
            var t = ctx.Runtime?.HolderOf(id);
            if (!t) return false;
            p = t.position;
            return true;
        }

        void ObstacleChanged(bool on)
        {
            string id = ctx.Selection.Primary;
            if (id != null) Apply(ctx.Ops.RagdollObstacle(id, on, UiContext.User(OpLabels.RagdollObstacle)));
        }

        void Note(string key) => ctx.Toasts?.Show(ctx.L10n.Get(key), ToastKind.Info);

        // ------------------------------------------------------------ the tool

        // one of three: off ends the tool; grab or shoot begins it in that mode, or switches the running tool's mode
        void ModeChosen(int mode)
        {
            if (mode == ModeOff)
            {
                if (tool != null) ctx.Tools.End(tool);
                ShowTool();
                return;
            }
            bool shooting = mode == ModeShoot;
            if (tool != null)
            {
                tool.Shooting = shooting;
                ShowTool();
            }
            else BeginTool(shooting);
        }

        /// <summary>The mode the panel shows: <see cref="ModeOff"/>, <see cref="ModeGrab"/> or <see cref="ModeShoot"/>.</summary>
        public int Mode => modeButtons.Index;

        // the outlines of the colliders and of the room, with or without the tool, while the panel is shown (ViewDraw is never called for a capture)
        void DrawOutlines(IToolDraw d)
        {
            if (!RagdollTool.ShowColliders && !RagdollTool.ShowRoom) return;
            var w = ctx.Ops?.Ragdoll?.World;
            if (w != null) RagdollOverlay.DrawOutlines(d, w, RagdollTool.ShowColliders, RagdollTool.ShowRoom);
        }

        void BeginTool(bool shooting)
        {
            var t = new RagdollTool(ctx, ToolEnded) { Shooting = shooting };
            if (ctx.Tools.Begin(t, new ToolOptions { PassThrough = true, Hint = RagdollTool.HintKey })) tool = t;
            else if (ctx.Tools.Refusal != null) ctx.Toasts?.Show(ctx.L10n.Get(ctx.Tools.Refusal), ToastKind.Warning);
            ShowTool();
        }

        void ToolEnded(RagdollTool t)
        {
            if (tool != t) return;
            tool = null;
            ShowTool();
        }

        void ShowTool()
        {
            bool on = tool != null, shoot = on && tool.Shooting;
            modeButtons.SetDisplay(!on ? ModeOff : shoot ? ModeShoot : ModeGrab, false);
            hint.text = !on ? "" : ctx.L10n.Get(shoot ? "ragdoll.tool.shootHint" : RagdollTool.HintKey);
            hint.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
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
            ctx.Options.Changed -= OnOption;
            ctx.Options.Changed += OnOption;
            ctx.Tools.ViewDraw -= DrawOutlines;
            ctx.Tools.ViewDraw += DrawOutlines;
            dirty = true;
        }

        void OnDetached(DetachFromPanelEvent e)
        {
            if (Current == this) Current = null;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneCleared -= MarkDirty;
            ctx.SceneLoaded -= OnLoaded;
            ctx.SelectionChanged -= OnSelection;
            ctx.Options.Changed -= OnOption;
            ctx.Tools.ViewDraw -= DrawOutlines;
            Flush();
            if (tool != null) ctx.Tools.End(tool);
        }

        void OnLoaded(SceneLoadedArgs a) => dirty = true;
        void OnSelection(SelectionChangedArgs a) => dirty = true;
        void OnSceneChanged(SceneChangeBatch b) => dirty = true;

        void OnOption(string key)
        {
            if (key == OptionDefs.StudioBlood) bloodToggle.SetValueWithoutNotify(ctx.Options.Bool(key));
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
            if (delayed.Count > 0 && now >= delayDue) Flush();
            bool due = now >= nextRead;
            if (!due && (hidden || !dirty)) return;
            if (due) nextRead = now + RefreshSeconds;
            hidden = !ShownNow();
            if (hidden || (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen)) return;
            Reads++;
            bool values = dirty;
            dirty = false;
            if (values) ReadValues();
            ReadState();
        }

        // ------------------------------------------------------------ reading

        string PanelCharacter()
        {
            string id = ctx.Selection.Primary;
            var n = id != null ? ctx.Model.Find(id) : null;
            if (n == null) return null;
            if (n.Kind == NodeKind.Chara) return id;
            return lastCharacter != null && ctx.Model.Find(lastCharacter)?.Kind == NodeKind.Chara ? lastCharacter : null;
        }

        void ReadValues()
        {
            Node = PanelCharacter();
            if (Node != null) lastCharacter = Node;
            bool has = Node != null, alone = has && ctx.Selection.Count <= 1;
            none.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            primary.style.display = has && ctx.Selection.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
            nodeName.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            nodeName.text = has ? ctx.Model.Find(Node)?.Label ?? "" : "";
            var shown = has ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var e in characterOnly)
            {
                e.style.display = shown;
                e.SetEnabled(alone);
            }
            var block = has ? ctx.Ops.RagdollBlock(Node) : null;
            var world = ctx.Ops.RagdollWorldBlock;
            var values = world?.Values ?? worldDefaults;
            foreach (var r in rows)
                if (!r.OfBody || block != null) r.Show(block?.Values, values, world);
            heightRow.style.display = (world?.Floor.AppliedMode ?? RagdollFloorValues.ModeMap) == RagdollFloorValues.ModeFixed ? DisplayStyle.Flex : DisplayStyle.None;
            stainToggle.SetValueWithoutNotify(RagdollTool.StainOnHit);
            bloodToggle.SetValueWithoutNotify(ctx.Options.Bool(OptionDefs.StudioBlood));
            collidersToggle.SetValueWithoutNotify(RagdollTool.ShowColliders);
            roomToggle.SetValueWithoutNotify(RagdollTool.ShowRoom);
            ReadOutlines();
            ShowTool();
            ReadObstacle(world);
        }

        // nothing to outline until the world runs (a body or a thrown prop, which come without a model change): the switches wait, their tooltip says when they show
        void ReadOutlines()
        {
            var live = ctx.Ops?.Ragdoll?.World;
            collidersToggle.SetEnabled(live != null);
            roomToggle.SetEnabled(live != null && live.HasRoom);
        }

        // the switch of a selected item
        void ReadObstacle(SceneRagdollWorld world)
        {
            string id = ctx.Selection.Primary;
            bool item = id != null && ctx.Model.Find(id)?.Kind == NodeKind.Item;
            obstacleToggle.style.display = item ? DisplayStyle.Flex : DisplayStyle.None;
            if (item) obstacleToggle.SetValueWithoutNotify(world != null && world.Obstacles.Contains(id));
        }

        // what moves without the model: read while the panel shows
        void ReadState()
        {
            ReadOutlines();
            var w = ctx.Ops.RagdollWorldStatus();
            lastHit.text = w.LastHit.HasValue ? ctx.L10n.Format("ragdoll.lastHit", NumberText.Of(w.LastHit.Value, "0.##")) : "";
            lastHit.style.display = w.LastHit.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (Node == null)
            {
                ReadBar(null);
                return;
            }
            var row = ctx.Ops.RagdollStatus(Node);
            ReadBar(row);
            ReadLists(row);
        }

        void ReadBar(RagdollCharaRow row)
        {
            bool on = row?.State != null, has = row != null, alone = ctx.Selection.Count <= 1;
            stateLine.text = ctx.L10n.Get(on ? "ragdoll.state." + row.State : "ragdoll.state.none");
            makeButton.SetEnabled(has && !on && alone);
            releaseButton.SetEnabled(on && alone);
            bakeButton.SetEnabled(on && alone);
            snapButton.SetEnabled(on && alone);
        }

        void ReadLists(RagdollCharaRow row)
        {
            var block = ctx.Ops.RagdollBlock(Node);
            if (block == null) return;
            var sb = new StringBuilder();
            foreach (var p in row.Pins) sb.Append(p.Pin).Append('/').Append(p.Part).Append('/').Append(p.Follow).Append('/').Append(p.State).Append(';');
            foreach (var t in block.Ties) sb.Append(t.A).Append('+').Append(t.B).Append(';');
            foreach (var h in block.Holds) sb.Append(h.Part).Append(';');
            string now = sb.ToString();
            unpinAll.SetEnabled(row.Pins.Count > 0);
            if (now == listed) return;
            listed = now;
            pinsList.Clear();
            for (int i = 0; i < row.Pins.Count; i++) pinsList.Add(PinLine(i, row.Pins[i]));
            for (int i = 0; i < block.Ties.Count; i++)
            {
                var t = block.Ties[i];
                var l = Line("ragdoll-tie-" + i, ctx.L10n.Format("ragdoll.tieRow", PartName(t.A), PartName(t.B)));
                Btn(l, "ragdoll-tie-" + i + "-remove", "ragdoll.remove", () => Apply(ctx.Ops.RagdollUntie(Node, t.A, t.B, UiContext.User(OpLabels.RagdollTie))));
                pinsList.Add(l);
            }
            for (int i = 0; i < block.Holds.Count; i++)
            {
                string part = block.Holds[i].Part;
                var l = Line("ragdoll-hold-" + i, ctx.L10n.Format("ragdoll.holdRow", PartName(part)));
                Btn(l, "ragdoll-hold-" + i + "-remove", "ragdoll.remove", () => Apply(ctx.Ops.RagdollUnhold(Node, part, UiContext.User(OpLabels.RagdollHold))));
                pinsList.Add(l);
            }
        }

        static VisualElement Line(string id, string text)
        {
            var l = new VisualElement { name = id };
            l.AddToClassList("hs-ragdoll__line");
            var t = new Label(text) { name = id + "-name" };
            t.AddToClassList("hs-ragdoll__text");
            l.Add(t);
            return l;
        }

        VisualElement PinLine(int i, RagdollPinRow pin)
        {
            string id = "ragdoll-pin-" + i, node = Node;
            string followed = pin.Follow != null ? ctx.Model.Find(pin.Follow)?.Label ?? pin.Follow : null;
            var l = Line(id, followed != null ? ctx.L10n.Format("ragdoll.pinFollows", PartName(pin.Part), followed) : PartName(pin.Part));
            if (pin.Follow != null) Btn(l, id + "-stay", "ragdoll.stay", () => Apply(ctx.Ops.RagdollStay(node, pin.Pin, UiContext.User(OpLabels.RagdollPin))));
            else Btn(l, id + "-follow", "ragdoll.followSelected", () => FollowSelected(node, pin));
            Btn(l, id + "-unpin", "ragdoll.unpin", () => Apply(ctx.Ops.RagdollUnpin(node, pin.Pin, UiContext.User(OpLabels.RagdollUnpin))));
            return l;
        }

        // the pin goes and a following pin takes its place, where the held point is, in one entry
        void FollowSelected(string node, RagdollPinRow pin)
        {
            string follow = ctx.Selection.Primary;
            var grab = ctx.Ops.RagdollBodyOf(node)?.PinOf(pin.Pin)?.Grab;
            if (follow == null || follow == node || grab == null || !grab.Live)
            {
                Note("ragdoll.tool.noSelection");
                return;
            }
            var at = grab.Point;
            var edit = ctx.BeginEdit(OpLabels.RagdollPin);
            var r = ctx.Ops.RagdollUnpin(node, pin.Pin, UiContext.User(OpLabels.RagdollPin));
            if (r.Ok) r = ctx.Ops.RagdollPin(node, pin.Part, at, follow, null, false, UiContext.User(OpLabels.RagdollPin));
            if (r.Ok) ctx.ShowResult(edit.Commit());
            else
            {
                edit.Cancel();
                ctx.ShowResult(r);
            }
            MarkDirty();
        }
    }
}
