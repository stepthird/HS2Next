// The panel «Cutter» (panel id "cut"). From the top: the target (a character or an item that is no marker, taken by «Use selected»); the marker: «Add plane», «Add box», «Add tear»,
// «Add hole», the scene's markers, the marker of the selection when one is selected, and «Place on surface» (the CutTool); the mode of the marker's shape (plane «Both sides»,
// «Front», «Back»; box «Both», «Inside», «Outside»); the section colour (the presets and an RGB swatch) and «Own fabric inside»; «Ragged edge» and «Tooth size» in cm,
// «Randomness», «Pattern #» with «New pattern»; «Clothes only», «Hide the target», «Name»; «Cut» with its progress line, and for a piece «Apply to piece». A cut is made ready
// (CutPrepare: a character or a plain item is captured then), stepped one mesh per frame and committed in one entry «Cut»; «Apply to piece» likewise in one entry «Edit cut».
// Everything goes through StudioOps, so the panel works with the bridge off; the panel's values are panel state, kept for the session, never in the scene. Subscribed from attach
// to detach: a closed panel reads nothing, and it reads four times a second only while it is shown.
// Element names: cut-none, cut-target, cut-use-selected, cut-add-<plane|box|tear|hole>, cut-marker, cut-place, cut-hint, cut-mode-<mode>, cut-section-<preset>, cut-section-rgb,
// cut-own-inside, cut-ragged, cut-tooth, cut-random, cut-pattern, cut-new-pattern, cut-clothes-only, cut-hide, cut-name, cut-make, cut-apply, cut-progress, section-<title key>.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Cut;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class CutPanel : VisualElement
    {
        public const float RefreshSeconds = 0.25f;
        /// <summary>Ranges of the ragged edge as the panel shows them: amplitude and tooth size in cm (1 unit = 10 cm).</summary>
        public const double AmpMaxCm = 5, ToothMinCm = 0.5, ToothMaxCm = 20;
        public static readonly string[] Kinds = { CutShapeInfo.Plane, CutShapeInfo.Box, CutShapeInfo.Tear, CutShapeInfo.Hole };

        /// <summary>The panel on the shell now, or null.</summary>
        public static CutPanel Current { get; private set; }

        // panel state: kept across rebuilds of the panel for the session, never in the scene
        static string target, marker;
        static readonly Dictionary<CutKind, string> modeOf = new Dictionary<CutKind, string>();
        static Color section = new Color(CutShapeInfo.DefaultSection[0], CutShapeInfo.DefaultSection[1], CutShapeInfo.DefaultSection[2]);
        static bool ownInside = true, clothesOnly, hide = true;
        static double ampCm, toothCm = 2, randomness = 0.5;
        static int seed = 1;
        static readonly Dictionary<string, bool> opened = new Dictionary<string, bool>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            target = marker = null;
            modeOf.Clear();
            section = new Color(CutShapeInfo.DefaultSection[0], CutShapeInfo.DefaultSection[1], CutShapeInfo.DefaultSection[2]);
            ownInside = hide = true;
            clothesOnly = false;
            ampCm = 0;
            toothCm = 2;
            randomness = 0.5;
            seed = 1;
            opened.Clear();
        }

        readonly UiContext ctx;
        readonly VisualElement body, modeBar;
        readonly Label none, targetLine, hint, progress;
        readonly DropdownField markers;
        readonly Toggle placeToggle, ownToggle, clothesToggle, hideToggle;
        readonly ColorSwatch rgb;
        readonly NumberSlider ragged, tooth, random, pattern;
        readonly TextField nameField;
        readonly Button makeButton, applyButton;
        readonly Dictionary<string, Button> sectionButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        CutTool tool;
        CutPlan plan;
        IEnumerator<bool> steps;
        bool editing, pending;
        string modesShown, markersShown;
        float nextRead;
        bool dirty = true, hidden;

        /// <summary>Reads of the scene so far (tests: none while the panel is closed).</summary>
        public int Reads { get; private set; }
        /// <summary>The target node, or null.</summary>
        public string Target => target;
        /// <summary>The marker the cut takes, or null.</summary>
        public string Marker => marker;
        /// <summary>The tool the panel began, while it runs.</summary>
        public CutTool Tool => tool;
        /// <summary>A cut is being stepped.</summary>
        public bool Cutting => plan != null || pending;
        /// <summary>Meshes stepped by the cut under way, or by the last one.</summary>
        public int Stepped { get; private set; }
        /// <summary>Frames the last cut was stepped in (one mesh per frame).</summary>
        public int SteppedFrames { get; private set; }
        int firstFrame;

        public CutPanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "cut-panel";
            AddToClassList("hs-bake");
            style.flexGrow = 1;
            focusable = true;
            BakeUiModule.Style(this);
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "cut-scroll" };
            scroll.AddToClassList("hs-bake__scroll");
            Add(scroll);
            body = new VisualElement { name = "cut-content" };
            scroll.Add(body);

            var targetBar = BakeUi.Bar("cut-target-bar");
            targetLine = BakeUi.Text(targetBar, "cut-target", "hs-bake__grow");
            BakeUi.Btn(targetBar, "cut-use-selected", "cut.useSelected", UseSelected);
            body.Add(targetBar);
            none = BakeUi.Text(body, "cut-none");

            // the marker
            var add = BakeUi.Bar("cut-add");
            foreach (string k in Kinds)
            {
                string kind = k;
                BakeUi.Btn(add, "cut-add-" + k, "cut.add." + k, () => AddMarker(kind));
            }
            body.Add(add);
            var markerBar = BakeUi.Bar("cut-marker-bar");
            markers = new DropdownField { name = "cut-marker", choices = new List<string>() };
            markers.AddToClassList("hs-bake__grow");
            markers.formatListItemCallback = MarkerName;
            markers.formatSelectedValueCallback = MarkerName;
            Loc.Bind(markers, "cut.marker", (e, s) => ((DropdownField)e).label = s);
            markers.RegisterValueChangedCallback(e => { if (e.target == markers && !string.IsNullOrEmpty(e.newValue)) { marker = e.newValue; dirty = true; } });
            markerBar.Add(markers);
            placeToggle = BakeUi.Switch(markerBar, "cut-place", "cut.place", PlaceChanged);
            body.Add(markerBar);
            hint = BakeUi.Text(body, "cut-hint");

            // the mode
            modeBar = BakeUi.Bar("cut-mode");
            body.Add(modeBar);

            // the section
            var sec = Section("cut.section", true);
            var presets = BakeUi.Bar("cut-section-presets");
            foreach (var p in CutShapeInfo.SectionPresets)
            {
                var c = new Color(p.r, p.g, p.b);
                sectionButtons[p.name] = BakeUi.Btn(presets, "cut-section-" + p.name, "cut.section." + p.name, () => { section = c; ShowValues(); });
            }
            sec.Add(presets);
            rgb = new ColorSwatch(L10n.Get("cut.rgb")) { name = "cut-section-rgb", Alpha = false };
            Loc.Bind(rgb.LabelElement, "cut.rgb");
            rgb.Clicked += OpenColour;
            sec.Add(rgb);
            ownToggle = BakeUi.Switch(sec, "cut-own-inside", "cut.ownInside", on => ownInside = on);

            // the ragged edge
            var rag = Section("cut.ragged", true);
            ragged = Number(rag, "cut-ragged", "cut.ragged", true, 0, AmpMaxCm, false, v => ampCm = v);
            tooth = Number(rag, "cut-tooth", "cut.tooth", true, ToothMinCm, ToothMaxCm, false, v => toothCm = v);
            random = Number(rag, "cut-random", "cut.random", false, 0, 1, false, v => randomness = v);
            pattern = Number(rag, "cut-pattern", "cut.pattern", false, CutRagParams.SeedMin, CutRagParams.SeedMax, true, v => seed = (int)Math.Round(v));
            var patBar = BakeUi.Bar("cut-pattern-bar");
            BakeUi.Btn(patBar, "cut-new-pattern", "cut.newPattern", NewPattern);
            rag.Add(patBar);

            var opts = new VisualElement { name = "cut-options" };
            clothesToggle = BakeUi.Switch(opts, "cut-clothes-only", "cut.clothesOnly", on => clothesOnly = on);
            hideToggle = BakeUi.Switch(opts, "cut-hide", "cut.hide", on => hide = on);
            nameField = new TextField { name = "cut-name" };
            Loc.Bind(nameField, "cut.name", (e, s) => ((TextField)e).label = s);
            opts.Add(nameField);
            body.Add(opts);

            var run = BakeUi.Bar("cut-run");
            makeButton = BakeUi.Btn(run, "cut-make", "cut.make", Cut);
            makeButton.AddToClassList("hs-bake__primary");
            applyButton = BakeUi.Btn(run, "cut-apply", "cut.apply", ApplyToPiece);
            body.Add(run);
            progress = BakeUi.Text(body, "cut-progress");
        }

        Foldout Section(string titleKey, bool open)
        {
            bool shown = opened.TryGetValue(titleKey, out bool o) ? o : open;
            var fold = InspectorUi.Section(titleKey, shown, "section-" + titleKey);
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) opened[titleKey] = e.newValue; });
            body.Add(fold);
            return fold;
        }

        // a number of the panel's own: every phase of an edit sets the value, none writes the scene
        static NumberSlider Number(VisualElement parent, string id, string key, bool cm, double min, double max, bool whole, Action<double> set)
        {
            var s = new NumberSlider(L10n.Get(key), min, max) { name = id, TypedMin = min, TypedMax = max, Whole = whole, Format = whole ? "0" : "0.##" };
            Loc.Bind(s.LabelElement, key, (e, t) => ((Label)e).text = cm ? t + " (cm)" : t);
            s.Edited += (phase, v) => { if (phase != EditPhase.Begin && phase != EditPhase.End) set(Math.Max(min, Math.Min(max, v))); };
            parent.Add(s);
            return s;
        }

        /// <summary>The panel reads the scene again in its next frame.</summary>
        public void MarkDirty() => dirty = true;

        // ------------------------------------------------------------ target, marker, values

        static ItemNodeState ItemState(TreeNode n) => n?.State as ItemNodeState;

        bool IsTarget(string id)
        {
            var n = id != null ? ctx.Model.Find(id) : null;
            if (n == null) return false;
            if (n.Kind == NodeKind.Chara) return true;
            return n.Kind == NodeKind.Item && ItemState(n) != null && !CutMarkers.IsMarker(ItemState(n));
        }

        CutKind KindOf(string id) => id != null ? CutMarkers.KindOf(ItemState(ctx.Model.Find(id))) : CutKind.None;

        bool IsPiece(string id) => id != null && ItemState(ctx.Model.Find(id)) is ItemNodeState s && s.Cut != null && s.Bake != null;

        /// <summary>Makes <paramref name="id"/> the target when it is a character or an item that is no marker.</summary>
        public void UseTarget(string id)
        {
            if (!IsTarget(id)) return;
            target = id;
            dirty = true;
        }

        void UseSelected()
        {
            string id = ctx.Selection.Primary;
            if (!IsTarget(id)) { Note("cut.selectTarget"); return; }
            UseTarget(id);
        }

        void AddMarker(string kind)
        {
            var r = ctx.ShowResult(ctx.Ops.CutMarker(kind, IsTarget(target) ? target : null, UiContext.User(OpLabels.AddItem)));
            if (r.Ok && r.Value is string id) marker = id;
            dirty = true;
        }

        string MarkerName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var n = ctx.Model.Find(id);
            string kind = CutShapeInfo.Name(KindOf(id));
            return (n?.Label ?? id) + (kind != null ? " (" + L10n.Get("cut.marker." + kind) + ")" : "");
        }

        string ModeNow(CutKind kind)
        {
            var modes = CutShapeInfo.Modes(kind);
            if (modes.Length == 0) return null;
            return modeOf.TryGetValue(kind, out var m) && Array.IndexOf(modes, m) >= 0 ? m : modes[0];
        }

        static string ModeKey(CutKind kind, string mode) => kind == CutKind.Box && mode == CutShapeInfo.Both ? "cut.mode.boxBoth" : "cut.mode." + mode;

        CutRagParams RaggedNow() => ampCm > 0 ? new CutRagParams { Amp = ampCm / 10, Tooth = toothCm / 10, Random = randomness, Seed = seed } : null;

        CutSection SectionNow() => new CutSection { Color = new Vec3(section.r, section.g, section.b), OwnInside = ownInside };

        void NewPattern()
        {
            int s;
            do s = UnityEngine.Random.Range(CutRagParams.SeedMin, CutRagParams.SeedMax + 1); while (s == seed);
            seed = s;
            ShowValues();
        }

        void OpenColour()
        {
            var layer = ctx.Shell?.Overlay ?? panel?.visualTree;
            if (layer == null) return;
            var before = section;
            var picker = new ColorPicker(rgb.Value, false) { name = "cut-colour-picker" };
            var blocker = new VisualElement { name = "color-blocker" };
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
            blocker.RegisterCallback<PointerDownEvent>(e => { picker.Close(true); e.StopPropagation(); });
            picker.Changed += (c, _) => { section = new Color(c.r, c.g, c.b); rgb.SetDisplay(section, false); };
            picker.Closed += commit => { blocker.RemoveFromHierarchy(); if (!commit) section = before; ShowValues(); };
            picker.style.position = Position.Absolute;
            var wb = rgb.Button.worldBound;
            picker.style.left = Mathf.Max(0, wb.xMin - 260);
            picker.style.top = wb.yMax;
            layer.Add(blocker);
            layer.Add(picker);
            UiDefer.Frames(2, () => { if (picker.panel != null) picker.Focus(); });
        }

        /// <summary>«Edit cut»: the target is <paramref name="piece"/>, the values those of its last step, and a marker stands where that step is (one entry «Add item»).</summary>
        public void EditPiece(string piece)
        {
            if (!IsPiece(piece)) return;
            target = piece;
            if (ctx.Ops.CutStatus(piece).Value is CutStatusInfo st && st.Steps.Count > 0)
            {
                var last = st.Steps[st.Steps.Count - 1];
                var kind = CutShapeInfo.KindOf(last.Shape);
                if (Array.IndexOf(CutShapeInfo.Modes(kind), last.Keep) >= 0) modeOf[kind] = last.Keep;
                ampCm = (last.Ragged?.Amp ?? 0) * 10.0;
                toothCm = (last.Ragged?.Tooth ?? 0.2f) * 10.0;
                randomness = last.Ragged?.Random ?? 0.5f;
                seed = last.Ragged?.Seed ?? 1;
                clothesOnly = last.ClothesOnly;
                if (st.Section != null)
                {
                    section = new Color(st.Section.Color.X, st.Section.Color.Y, st.Section.Color.Z);
                    ownInside = st.Section.OwnInside;
                }
            }
            var r = ctx.ShowResult(ctx.Ops.CutMarkerAtLastStep(piece, UiContext.User(OpLabels.AddItem)));
            if (r.Ok && r.Value is string id) marker = id;
            dirty = true;
            ShowValues();
        }

        // ------------------------------------------------------------ the tool

        void PlaceChanged(bool on)
        {
            if (!on)
            {
                if (tool != null) ctx.Tools.End(tool);
                ShowTool();
                return;
            }
            var t = new CutTool(ctx, ToolEnded);
            if (ctx.Tools.Begin(t, new ToolOptions { PassThrough = true, Hint = CutTool.HintKey })) tool = t;
            else if (ctx.Tools.Refusal != null) ctx.Toasts?.Show(ctx.L10n.Get(ctx.Tools.Refusal), ToastKind.Warning);
            ShowTool();
        }

        void ToolEnded(CutTool t)
        {
            if (tool != t) return;
            tool = null;
            ShowTool();
        }

        void ShowTool()
        {
            bool on = tool != null;
            placeToggle.SetValueWithoutNotify(on);
            hint.text = on ? ctx.L10n.Get(CutTool.HintKey) : "";
            BakeUi.Show(hint, on);
        }

        // ------------------------------------------------------------ cut, apply

        void Cut()
        {
            if (plan != null || pending) return;
            if (!IsTarget(target)) { Note("cut.selectTarget"); return; }
            if (KindOf(marker) == CutKind.None) { Note("cut.markerFirst"); return; }
            var req = new CutMakeRequest
            {
                Target = target, Marker = marker, Mode = ModeNow(KindOf(marker)), Ragged = RaggedNow(), ClothesOnly = clothesOnly, Section = SectionNow(), Hide = hide,
                Name = nameField.value,
            };
            var n = ctx.Model.Find(target);
            bool capture = n.Kind == NodeKind.Chara || ItemState(n)?.Bake == null;
            Begin(capture);
            // the line shows before a capture holds the frame
            UiDefer.Frames(2, () => Prepared(ctx.Ops.CutPrepare(req), false));
        }

        void ApplyToPiece()
        {
            if (plan != null || pending || !IsPiece(target)) return;
            var kind = KindOf(marker);
            if (kind == CutKind.None && ctx.Ops.CutStatus(target).Value is CutStatusInfo st && st.Steps.Count > 0) kind = CutShapeInfo.KindOf(st.Steps[st.Steps.Count - 1].Shape);
            var rag = RaggedNow();
            var req = new CutSetRequest
            {
                Marker = KindOf(marker) != CutKind.None ? marker : null, Mode = ModeNow(kind), Ragged = rag, ClearRagged = rag == null, ClothesOnly = clothesOnly, Section = SectionNow(),
            };
            Begin(false);
            UiDefer.Frames(2, () => Prepared(ctx.Ops.CutSetPrepare(target, req), true));
        }

        void Begin(bool capture)
        {
            makeButton.SetEnabled(false);
            applyButton.SetEnabled(false);
            progress.text = ctx.L10n.Get(capture ? "bake.capturing" : "cut.cutting0");
            BakeUi.Show(progress, true);
            Stepped = 0;
            plan = null;
            pending = true;
        }

        void Prepared(OpResult r, bool edit)
        {
            pending = false;
            ctx.ShowResult(r);
            if (!r.Ok || !(r.Value is CutPlan p))
            {
                progress.text = "";
                dirty = true;
                return;
            }
            plan = p;
            editing = edit;
            steps = p.Steps;
            firstFrame = Time.frameCount;
            progress.text = ctx.L10n.Get("cut.cutting0");
        }

        // one mesh per frame; the end commits in one entry
        void StepCut()
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception e)
            {
                Debug.LogError("[ui] the cut threw: " + e);
                plan.Dispose();
                plan = null;
                steps = null;
                progress.text = "";
                dirty = true;
                return;
            }
            if (more)
            {
                Stepped++;
                progress.text = ctx.L10n.Format("cut.cutting", Stepped);
                return;
            }
            var p = plan;
            plan = null;
            steps = null;
            SteppedFrames = Time.frameCount - firstFrame;
            var r = ctx.ShowResult(editing ? ctx.Ops.CutSetCommit(p, UiContext.User(OpLabels.CutEdit)) : ctx.Ops.CutCommit(p, UiContext.User(OpLabels.CutMake)));
            if (!r.Ok) progress.text = "";
            else if (r.Value is CutMakeResult made)
            {
                progress.text = ctx.L10n.Format("cut.made", made.Pieces.Count);
                nameField.SetValueWithoutNotify("");   // the name is used once
            }
            else if (r.Value is CutSetResult set) progress.text = ctx.L10n.Get(set.Changed ? "cut.applied" : "cut.unchanged");
            dirty = true;
        }

        void Note(string key) => ctx.Toasts?.Show(ctx.L10n.Get(key), ToastKind.Info);

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
            dirty = true;
        }

        void OnDetached(DetachFromPanelEvent e)
        {
            if (Current == this) Current = null;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneCleared -= MarkDirty;
            ctx.SceneLoaded -= OnLoaded;
            ctx.SelectionChanged -= OnSelection;
            if (tool != null) ctx.Tools.End(tool);
            plan?.Dispose();
            plan = null;
            steps = null;
        }

        void OnLoaded(SceneLoadedArgs a) => dirty = true;
        void OnSceneChanged(SceneChangeBatch b) => dirty = true;

        // a marker selected becomes the panel's marker
        void OnSelection(SelectionChangedArgs a)
        {
            string id = ctx.Selection.Primary;
            if (KindOf(id) != CutKind.None) marker = id;
            dirty = true;
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
            if (plan != null) StepCut();
            float now = Time.unscaledTime;
            bool due = now >= nextRead;
            if (!due && (hidden || !dirty)) return;
            if (due) nextRead = now + RefreshSeconds;
            hidden = !ShownNow();
            if (hidden || (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen) || !dirty) return;
            dirty = false;
            Reads++;
            ReadState();
        }

        // ------------------------------------------------------------ reading

        void ReadState()
        {
            if (target != null && !IsTarget(target)) target = null;
            if (marker != null && KindOf(marker) == CutKind.None) marker = null;
            bool has = target != null;
            targetLine.text = has ? ctx.L10n.Format("cut.target", ctx.Model.Find(target)?.Label ?? target) : "";
            none.text = has ? "" : ctx.L10n.Get("cut.selectTarget");
            BakeUi.Show(none, !has);
            // the scene's markers
            var list = ctx.Ops.CutMarkerNodes();
            var sb = new StringBuilder();
            foreach (var id in list) sb.Append(id).Append('=').Append(ctx.Model.Find(id)?.Label).Append(';');
            sb.Append(ctx.L10n.Language);
            if (sb.ToString() != markersShown)
            {
                markersShown = sb.ToString();
                markers.choices = list;
            }
            markers.SetValueWithoutNotify(marker ?? "");
            ShowModes();
            ShowValues();
            bool idle = plan == null && !pending;
            makeButton.SetEnabled(idle && has && marker != null);
            applyButton.SetEnabled(idle && IsPiece(target));
            BakeUi.Show(applyButton, IsPiece(target));
            ShowTool();
        }

        void ShowModes()
        {
            var kind = KindOf(marker);
            var modes = CutShapeInfo.Modes(kind);
            string now = kind + "/" + ctx.L10n.Language;
            if (now != modesShown)
            {
                modesShown = now;
                modeBar.Clear();
                if (modes.Length > 1)
                    foreach (var m in modes)
                    {
                        string mode = m;
                        BakeUi.Btn(modeBar, "cut-mode-" + m, ModeKey(kind, m), () => { modeOf[kind] = mode; ShowModes(); });
                    }
            }
            string cur = ModeNow(kind);
            foreach (var b in modeBar.Children()) b.EnableInClassList("hs-bake__on", b.name == "cut-mode-" + cur);
            BakeUi.Show(modeBar, modes.Length > 1);
        }

        void ShowValues()
        {
            foreach (var p in CutShapeInfo.SectionPresets)
                sectionButtons[p.name].EnableInClassList("hs-bake__on", Mathf.Approximately(p.r, section.r) && Mathf.Approximately(p.g, section.g) && Mathf.Approximately(p.b, section.b));
            rgb.SetDisplay(section, false);
            ownToggle.SetValueWithoutNotify(ownInside);
            if (!ragged.Dragging) ragged.SetDisplay(ampCm, false);
            if (!tooth.Dragging) tooth.SetDisplay(toothCm, false);
            if (!random.Dragging) random.SetDisplay(randomness, false);
            if (!pattern.Dragging) pattern.SetDisplay(seed, false);
            clothesToggle.SetValueWithoutNotify(clothesOnly);
            hideToggle.SetValueWithoutNotify(hide);
        }

        /// <summary>The panel's values as a cut would take them (tests).</summary>
        public CutMakeRequest RequestNow() => new CutMakeRequest
        {
            Target = target, Marker = marker, Mode = ModeNow(KindOf(marker)), Ragged = RaggedNow(), ClothesOnly = clothesOnly, Section = SectionNow(), Hide = hide, Name = nameField.value,
        };
    }
}
