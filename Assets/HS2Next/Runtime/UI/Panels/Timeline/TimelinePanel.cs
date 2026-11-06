// The Timeline panel (the bottom region, panel id "timeline"): without a Timeline one button «New Timeline» and a line on
// what a Timeline is; with one, a header (play / pause, stop with «Stop and keep this pose» in its drop-down, the play head
// mm:ss.fff (typing a time seeks), the length, loop, play on load, Key, the ease of the selected keys, + (add a track for
// the selection), ⋯ (export frames, clear, the events, fit), a red dot with Stop while frames are recorded), the track
// list beside the key strip (ruler with the play head, one lane per track with its keys drawn by ease, the events lane)
// and the key editor of the one selected key. Every change goes through the Timeline service as one history entry; the
// panel keeps only its view state (selection, zoom, scroll). Element names (ui.query / ui.input): timeline-new,
// timeline-play, timeline-stop, timeline-stop-more (its menu item menu-item-timeline-stopbake), timeline-time,
// timeline-duration, timeline-loop, timeline-autoplay, timeline-key, timeline-ease (segments timeline-ease-step / -linear /
// -inout, timeline-ease-more with in / out), timeline-add, timeline-menu, timeline-recording(-stop), timeline-tracks,
// timeline-track-<i> (-enabled, -label, -mark), timeline-ruler, timeline-head, timeline-lane-<i> with keys
// timeline-key-<i>-<k>, timeline-events with timeline-event-<j>, timeline-keyedit.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Timeline;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>A key of a track by its time (as stored, float).</summary>
    public readonly struct TimelineKeyRef : IEquatable<TimelineKeyRef>
    {
        public readonly string Track;
        public readonly float T;
        public TimelineKeyRef(string track, float t) { Track = track; T = t; }
        public bool Equals(TimelineKeyRef o) => Track == o.Track && T == o.T;
        public override bool Equals(object obj) => obj is TimelineKeyRef o && Equals(o);
        public override int GetHashCode() => (Track?.GetHashCode() ?? 0) * 31 + T.GetHashCode();
        public override string ToString() => Track + "@" + T.ToString("R", CultureInfo.InvariantCulture);
    }

    public sealed partial class TimelinePanel : VisualElement
    {
        public const string StyleResource = "UI/Timeline/timeline";
        public const float TrackColumnWidth = 230, RowHeight = 22, RulerHeight = 20;
        const float SnapSeconds = 0.01f;

        /// <summary>The built panel (the actions and tests reach it), or null.</summary>
        public static TimelinePanel Current { get; private set; }

        readonly UiContext ctx;
        TimelineUiService Svc => TimelineUiModule.ServiceOf(ctx);
        TimelineDocument Doc => ctx.Model?.Timeline;

        // view state
        public readonly List<string> SelectedTracks = new List<string>();
        public readonly List<TimelineKeyRef> SelectedKeys = new List<TimelineKeyRef>();
        /// <summary>Seconds at the strip's left edge and pixels per second.</summary>
        public double ViewStart { get; private set; }
        public double Pps { get; private set; } = 60;
        bool fitted = true;

        // elements
        readonly VisualElement emptyBox, mainBox, header;
        Button playButton, stopButton, stopMore, keyButton, addButton, menuButton, easeMore, newButton, recStop;
        TextField timeField;
        FloatField durationField;
        Toggle loopToggle, autoplayToggle;
        SegmentedButtons easeButtons;
        VisualElement recBox;
        Label recLabel;
        VisualElement rulerRow, ruler, head, tracksCol, lanesCol, eventsLane;
        ScrollView scroll;
        TimelineKeyEditor keyEditor;
        readonly List<VisualElement> trackRows = new List<VisualElement>();
        readonly List<VisualElement> lanes = new List<VisualElement>();

        // change tracking
        object lastDoc;
        int lastStamp = int.MinValue;
        TimelinePlayState lastState = (TimelinePlayState)(-1);
        double lastTime = double.NaN;
        float nextMarks;
        bool ticking;

        public TimelinePanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "timeline-panel";
            AddToClassList("hs-tl");
            var sheet = Resources.Load<StyleSheet>(StyleResource);
            if (sheet != null) styleSheets.Add(sheet);
            style.flexGrow = 1;
            focusable = true;

            emptyBox = new VisualElement { name = "timeline-empty" };
            emptyBox.AddToClassList("hs-tl__empty");
            newButton = new Button(NewTimeline) { name = "timeline-new" };
            Loc.Bind(newButton, "timeline.new");
            newButton.AddToClassList("hs-tl__new");
            var what = Loc.Label("timeline.empty");
            what.AddToClassList("hs-tl__hint");
            what.style.whiteSpace = WhiteSpace.Normal;
            emptyBox.Add(newButton);
            emptyBox.Add(what);
            Add(emptyBox);

            mainBox = new VisualElement { name = "timeline-main" };
            mainBox.AddToClassList("hs-tl__main");
            header = BuildHeader();
            mainBox.Add(header);
            BuildBody(mainBox);
            keyEditor = new TimelineKeyEditor(ctx, this);
            mainBox.Add(keyEditor);
            Add(mainBox);

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Rebuild();
        }

        // ------------------------------------------------------------ life

        void Attach()
        {
            Current = this;
            ctx.SceneChanged += OnSceneChanged;
            ctx.SceneCleared += OnCleared;
            ctx.SceneLoaded += OnLoaded;
            ctx.SelectionChanged += OnSelection;
            ctx.SubSelectionChanged += OnSub;
            if (!ticking) { PanelTicker.Add(Tick); ticking = true; }
            Rebuild();
        }

        void Detach()
        {
            if (Current == this) Current = null;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneCleared -= OnCleared;
            ctx.SceneLoaded -= OnLoaded;
            ctx.SelectionChanged -= OnSelection;
            ctx.SubSelectionChanged -= OnSub;
            if (ticking) { PanelTicker.Remove(Tick); ticking = false; }
        }

        /// <summary>The document's own edits replace its objects (the stamp sees them); nodes added, removed, moved or renamed
        /// change the rows' labels and marks.</summary>
        void OnSceneChanged(SceneChangeBatch b)
        {
            if (Doc == null) return;
            if (b.IsStructural || b.Touches(null, TimelineEditCommand.ChangePath)) { lastStamp = int.MinValue; return; }
            foreach (var c in b.Changes)
                if (c.Path == "name") { lastStamp = int.MinValue; return; }
        }
        void OnCleared() { SelectedTracks.Clear(); SelectedKeys.Clear(); fitted = true; lastStamp = int.MinValue; }
        void OnLoaded(SceneLoadedArgs a) { fitted = true; lastStamp = int.MinValue; }
        void OnSelection(SelectionChangedArgs a) => UpdateHeaderEnabled();
        void OnSub(string s) => UpdateHeaderEnabled();

        /// <summary>One frame of the panel (the host's ticker): rebuild after a document change, follow the play head.</summary>
        public void Tick()
        {
            // nothing while the panel is not shown (another tab, a collapsed region): its next frame shown catches up
            if (panel == null || float.IsNaN(layout.width) || layout.width <= 0) return;
            int stamp = Stamp(Doc);
            if (!ReferenceEquals(Doc, lastDoc) || stamp != lastStamp) Rebuild();
            var svc = Svc;
            var st = svc.State;
            double t = svc.Time;
            if (st != lastState || t != lastTime)
            {
                lastState = st;
                lastTime = t;
                UpdatePlayHead();
                UpdateHeaderEnabled();
            }
            if (Time.unscaledTime >= nextMarks)
            {
                nextMarks = Time.unscaledTime + 0.25f;
                UpdateMarks();
                UpdateRecording();
            }
        }

        /// <summary>A number that changes when the document or one of its tracks is replaced (the bus replaces objects).</summary>
        static int Stamp(TimelineDocument d)
        {
            if (d == null) return 0;
            unchecked
            {
                int h = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d);
                h = h * 31 + d.Tracks.Count;
                foreach (var t in d.Tracks) h = h * 31 + (t == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t));
                h = h * 31 + d.Events.Count;
                return h;
            }
        }

        // ------------------------------------------------------------ build and refresh

        /// <summary>Draws the panel from the document (header values, rows, lanes, events, the key editor).</summary>
        public void Rebuild()
        {
            var doc = Doc;
            lastDoc = doc;
            lastStamp = Stamp(doc);
            emptyBox.style.display = doc == null ? DisplayStyle.Flex : DisplayStyle.None;
            mainBox.style.display = doc == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (doc == null)
            {
                SelectedTracks.Clear();
                SelectedKeys.Clear();
                keyEditor.Show(null, null);
                return;
            }
            PruneSelection(doc);
            if (fitted) FitView(false);
            durationField.SetValueWithoutNotify(doc.Duration);
            loopToggle.SetValueWithoutNotify(doc.Loop);
            autoplayToggle.SetValueWithoutNotify(doc.Autoplay);
            BuildRows(doc);
            DrawKeys();
            DrawEvents();
            UpdatePlayHead();
            UpdateHeaderEnabled();
            UpdateMarks();
            ShowKeyEditor();
        }

        void PruneSelection(TimelineDocument doc)
        {
            SelectedTracks.RemoveAll(id => doc.FindTrack(id) == null);
            SelectedKeys.RemoveAll(k => doc.FindTrack(k.Track)?.IndexOfTime(k.T) < 0 || doc.FindTrack(k.Track) == null);
        }

        void ShowKeyEditor()
        {
            var doc = Doc;
            if (doc != null && SelectedKeys.Count == 1)
            {
                var r = SelectedKeys[0];
                var tr = doc.FindTrack(r.Track);
                int i = tr?.IndexOfTime(r.T) ?? -1;
                if (tr != null && i >= 0 && tr.IsKnownType) { keyEditor.Show(tr, tr.Keys[i]); return; }
            }
            keyEditor.Show(null, null);
        }

        // ------------------------------------------------------------ header

        VisualElement BuildHeader()
        {
            var h = new VisualElement { name = "timeline-header" };
            h.AddToClassList("hs-tl__header");
            var row1 = new VisualElement();
            row1.AddToClassList("hs-tl__headrow");
            var row2 = new VisualElement();
            row2.AddToClassList("hs-tl__headrow");
            h.Add(row1);
            h.Add(row2);

            playButton = Btn("timeline-play", "▶", "timeline.play", PlayPause);
            stopButton = Btn("timeline-stop", "■", "timeline.stop", () => Show(Svc.Stop(false)));
            stopMore = Btn("timeline-stop-more", "▾", "timeline.stopBake", StopMenu);
            timeField = new TextField { name = "timeline-time", isDelayed = true };
            timeField.AddToClassList("hs-tl__time");
            timeField.RegisterValueChangedCallback(e =>
            {
                if (TimelineText.TryParseTime(e.newValue, out double t)) Show(Svc.Seek(t));
                timeField.SetValueWithoutNotify(TimelineText.Time(Svc.Time));
            });
            var slash = new Label("/");
            slash.AddToClassList("hs-tl__slash");
            durationField = new FloatField { name = "timeline-duration", isDelayed = true };
            durationField.AddToClassList("hs-tl__duration");
            Loc.Tooltip(durationField, "timeline.duration");
            durationField.RegisterValueChangedCallback(e => SetDuration(e.newValue));
            var unit = new Label("s");
            unit.AddToClassList("hs-tl__slash");
            recBox = new VisualElement { name = "timeline-recording" };
            recBox.AddToClassList("hs-tl__rec");
            var dot = new Label("●");
            dot.AddToClassList("hs-tl__recdot");
            recLabel = new Label { name = "timeline-recording-label" };
            recStop = new Button(() => HS2Next.Screenshots.FrameRecorder.Current?.Stop()) { name = "timeline-recording-stop" };
            Loc.Bind(recStop, "timeline.stop");
            recStop.AddToClassList("hs-tl__btn");
            recBox.Add(dot);
            recBox.Add(recLabel);
            recBox.Add(recStop);
            recBox.style.display = DisplayStyle.None;
            foreach (var e in new VisualElement[] { playButton, stopButton, stopMore, timeField, slash, durationField, unit, recBox }) row1.Add(e);

            loopToggle = new Toggle { name = "timeline-loop" };
            Loc.Bind(loopToggle, "timeline.loop", (e, s) => ((Toggle)e).text = s);
            loopToggle.RegisterValueChangedCallback(e => SetProps(null, e.newValue, null));
            autoplayToggle = new Toggle { name = "timeline-autoplay" };
            Loc.Bind(autoplayToggle, "timeline.autoplay", (e, s) => ((Toggle)e).text = s);
            autoplayToggle.RegisterValueChangedCallback(e => SetProps(null, null, e.newValue));
            keyButton = new Button(KeySelected) { name = "timeline-key" };
            Loc.Bind(keyButton, "timeline.key");
            keyButton.AddToClassList("hs-tl__btn");
            easeButtons = new SegmentedButtons(new[] { L10n.Get("timeline.ease.step"), L10n.Get("timeline.ease.linear"), L10n.Get("timeline.ease.inout") }) { name = "timeline-ease" };
            string[] segEases = { TimelineEases.Step, TimelineEases.Linear, TimelineEases.InOut };
            for (int i = 0; i < easeButtons.Buttons.Count; i++)
            {
                easeButtons.Buttons[i].name = "timeline-ease-" + segEases[i];
                Loc.Bind(easeButtons.Buttons[i], "timeline.ease." + segEases[i]);
            }
            easeButtons.Selected += i => SetEase(segEases[i]);
            easeMore = Btn("timeline-ease-more", "▾", "timeline.ease.in", EaseMenu);
            addButton = Btn("timeline-add", "+", "timeline.addTrack", AddMenu);
            menuButton = Btn("timeline-menu", "⋯", null, MainMenu);
            foreach (var e in new VisualElement[] { loopToggle, autoplayToggle, keyButton, easeButtons, easeMore, addButton, menuButton }) row2.Add(e);
            return h;
        }

        Button Btn(string name, string glyph, string tipKey, Action a)
        {
            var b = new Button(a) { name = name, text = glyph };
            b.AddToClassList("hs-tl__btn");
            if (tipKey != null) Loc.Tooltip(b, tipKey);
            return b;
        }

        void UpdateHeaderEnabled()
        {
            var doc = Doc;
            if (doc == null) return;
            var svc = Svc;
            var st = svc.State;
            bool pb = svc.PlaybackAvailable;
            playButton.text = st == TimelinePlayState.Playing ? "❚❚" : "▶";
            Loc.Tooltip(playButton, st == TimelinePlayState.Playing ? "timeline.pause" : "timeline.play");
            playButton.EnableInClassList("hs-tl__btn--on", st == TimelinePlayState.Playing);
            playButton.SetEnabled(pb);
            stopButton.SetEnabled(pb && st != TimelinePlayState.Stopped);
            stopMore.SetEnabled(pb);
            if (timeField.panel?.focusController?.focusedElement != timeField) timeField.SetValueWithoutNotify(TimelineText.Time(svc.Time));
            keyButton.SetEnabled(SelectedTracks.Count > 0);
            bool keys = SelectedKeys.Count > 0;
            var eases = SelectedEases();
            int lit = eases.Count == 1 ? Array.IndexOf(new[] { TimelineEases.Step, TimelineEases.Linear, TimelineEases.InOut }, eases.First()) : -1;
            easeButtons.SetDisplay(lit, eases.Count > 1);
            easeButtons.SetEnabled(keys);
            easeMore.SetEnabled(keys);
            easeMore.EnableInClassList("hs-tl__btn--on", eases.Count == 1 && (eases.First() == TimelineEases.In || eases.First() == TimelineEases.Out));
            addButton.SetEnabled(true);
        }

        HashSet<string> SelectedEases()
        {
            var s = new HashSet<string>(StringComparer.Ordinal);
            var doc = Doc;
            if (doc == null) return s;
            foreach (var r in SelectedKeys)
            {
                var tr = doc.FindTrack(r.Track);
                int i = tr?.IndexOfTime(r.T) ?? -1;
                if (i >= 0) s.Add(TimelineEases.IsKnown(tr.Keys[i].Ease) ? tr.Keys[i].Ease : TimelineEases.InOut);
            }
            return s;
        }

        void UpdateRecording()
        {
            var rec = HS2Next.Screenshots.FrameRecorder.Current;
            bool on = rec != null && rec.IsActive;
            recBox.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) recLabel.text = L10n.Format("timeline.recording", rec.Frames);
        }

        // ------------------------------------------------------------ header operations

        /// <summary>Shows a failed result as an error toast; returns its Ok.</summary>
        public bool Show(TimelineResult r)
        {
            if (!r.Ok) ctx.Toasts?.Show(L10n.Format("msg.opFailed", r.Code, r.Message), ToastKind.Error);
            return r.Ok;
        }

        void NewTimeline()
        {
            if (Doc != null) return;
            Show(Svc.ReplaceDocument(new TimelineDocument { Duration = 10f }, TimelineEditCommand.LabelTimeline));
            fitted = true;
        }

        /// <summary>Play, or pause while playing (the action timeline.playPause).</summary>
        public void PlayPause()
        {
            var svc = Svc;
            Show(svc.State == TimelinePlayState.Playing ? svc.Pause() : svc.Play());
        }

        void StopMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem { Id = "timeline-stop", Text = L10n.Get("timeline.stop"), Run = () => Show(Svc.Stop(false)) },
                new MenuItem { Id = "timeline-stopbake", Text = L10n.Get("timeline.stopBake"), Run = () => Show(Svc.Stop(true)) },
            };
            ctx.Menus?.Show(items, stopMore, null, "timeline-stop-menu");
        }

        void SetDuration(float v)
        {
            var doc = Doc;
            if (doc == null) return;
            if (float.IsNaN(v) || float.IsInfinity(v)) { durationField.SetValueWithoutNotify(doc.Duration); return; }
            v = Mathf.Clamp(v, 0.1f, TimelineLimits.MaxDuration);
            if (v == doc.Duration) { durationField.SetValueWithoutNotify(v); return; }
            SetProps(v, null, null);
        }

        void SetProps(float? duration, bool? loop, bool? autoplay)
        {
            var doc = Doc;
            if (doc == null) return;
            var after = doc.Clone();
            if (duration.HasValue) after.Duration = duration.Value;
            if (loop.HasValue) after.Loop = loop.Value;
            if (autoplay.HasValue) after.Autoplay = autoplay.Value;
            if (!Show(Svc.ReplaceDocument(after, TimelineEditCommand.LabelTimeline))) Rebuild();
        }

        /// <summary>Keys the selected tracks at the play head with the value each channel shows (the action timeline.key).</summary>
        public void KeySelected()
        {
            if (Doc == null || SelectedTracks.Count == 0) return;
            float t = (float)Svc.Time;
            if (Show(Svc.KeyShown(SelectedTracks.ToList(), t)))
            {
                SelectedKeys.Clear();
                foreach (var id in SelectedTracks) SelectedKeys.Add(new TimelineKeyRef(id, t));
            }
        }

        public bool CanKey => Doc != null && SelectedTracks.Count > 0;

        void EaseMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem { Id = "timeline-ease-in", Text = L10n.Get("timeline.ease.in"), Run = () => SetEase(TimelineEases.In) },
                new MenuItem { Id = "timeline-ease-out", Text = L10n.Get("timeline.ease.out"), Run = () => SetEase(TimelineEases.Out) },
            };
            ctx.Menus?.Show(items, easeMore, null, "timeline-ease-menu");
        }

        /// <summary>Sets the ease of the selected keys (step-only types keep step): one entry «Key».</summary>
        public void SetEase(string ease)
        {
            var doc = Doc;
            if (doc == null || SelectedKeys.Count == 0) return;
            var byTrack = SelectedKeys.GroupBy(k => k.Track).ToList();
            var changed = new List<TimelineTrack>();
            foreach (var g in byTrack)
            {
                var tr = doc.FindTrack(g.Key);
                if (tr == null || !tr.IsKnownType || TimelineTypes.IsStepOnly(tr.Type)) continue;
                var c = tr.Clone();
                bool any = false;
                foreach (var r in g)
                {
                    int i = c.IndexOfTime(r.T);
                    if (i >= 0 && c.Keys[i].Ease != ease) { c.Keys[i].Ease = ease; any = true; }
                }
                if (any) changed.Add(c);
            }
            Commit(changed, TimelineEditCommand.LabelKey);
            UpdateHeaderEnabled();
        }

        /// <summary>Hands changed tracks over as one entry: one track alone, several as the whole document.</summary>
        bool Commit(List<TimelineTrack> changed, string label)
        {
            if (changed.Count == 0) return true;
            if (changed.Count == 1) return Show(Svc.ReplaceTrack(changed[0], label));
            var after = Doc.Clone();
            foreach (var c in changed)
            {
                int i = after.IndexOfTrack(c.Id);
                if (i >= 0) after.Tracks[i] = c;
            }
            return Show(Svc.ReplaceDocument(after, label));
        }

        /// <summary>Deletes the selected keys (Delete / Backspace in the panel): one entry «Delete keys».</summary>
        public void DeleteSelectedKeys()
        {
            var doc = Doc;
            if (doc == null || SelectedKeys.Count == 0) return;
            var changed = new List<TimelineTrack>();
            foreach (var g in SelectedKeys.GroupBy(k => k.Track))
            {
                var tr = doc.FindTrack(g.Key);
                if (tr == null || !tr.IsKnownType) continue;
                var c = tr.Clone();
                int before = c.Keys.Count;
                var times = new HashSet<float>(g.Select(k => k.T));
                c.Keys.RemoveAll(k => times.Contains(k.T));
                if (c.Keys.Count != before) changed.Add(c);
            }
            if (Commit(changed, TimelineEditCommand.LabelDeleteKeys)) SelectedKeys.Clear();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (ShortcutRouterIsText(e.target as VisualElement)) return;
            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && e.modifiers == EventModifiers.None && SelectedKeys.Count > 0)
            {
                DeleteSelectedKeys();
                e.StopPropagation();
                panel?.focusController?.IgnoreEvent(e);
            }
            else if (e.keyCode == KeyCode.Escape && dragKeys != null)
            {
                CancelKeyDrag();
                e.StopPropagation();
                panel?.focusController?.IgnoreEvent(e);
            }
        }

        static bool ShortcutRouterIsText(VisualElement v) => HS2Next.UI.Input.ShortcutRouter.IsText(v);

        // ------------------------------------------------------------ menus

        void MainMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem { Id = "timeline-export", Text = L10n.Get("timeline.export"), Run = () => TimelineExportDialog.Show(ctx), Disabled = ctx.Dialogs == null || ctx.Dialogs.IsModalOpen },
                new MenuItem { Id = "timeline-events-list", Text = L10n.Get("timeline.eventsList"), Run = ShowEvents },
                new MenuItem { Id = "timeline-fit", Text = L10n.Get("timeline.fit"), Run = () => FitView(true) },
                MenuItem.Sep(),
                new MenuItem { Id = "timeline-clear", Text = L10n.Get("timeline.clear"), Run = ClearTimeline },
            };
            ctx.Menus?.Show(items, menuButton, null, "timeline-main-menu");
        }

        void ClearTimeline()
        {
            if (Doc == null) return;
            void Run() => Show(Svc.ReplaceDocument(null, TimelineEditCommand.LabelClear));
            if (ctx.Options.Bool(OptionDefs.ConfirmDelete) && ctx.Dialogs != null)
                ctx.Dialogs.Confirm("timeline.clear", "timeline.clearConfirm", ok => { if (ok) Run(); });
            else Run();
        }

        void ShowEvents()
        {
            var doc = Doc;
            if (doc == null || ctx.Dialogs == null) return;
            var list = new ScrollView { name = "timeline-events-list" };
            list.style.maxHeight = 360;
            if (doc.Events.Count == 0) list.Add(InspectorUi.Note("timeline.noEvents"));
            for (int j = 0; j < doc.Events.Count; j++)
            {
                var ev = doc.Events[j];
                var l = new Label(EventLine(ev)) { name = "timeline-events-row-" + j };
                l.AddToClassList("hs-tl__eventrow");
                if (!ev.Enabled) l.AddToClassList("hs-tl__eventrow--off");
                list.Add(l);
            }
            var spec = new DialogSpec { Id = "timeline-events", Title = L10n.Get("timeline.events"), Content = list };
            spec.Buttons.Add(DialogButton.Of("close", "common.close", true, true));
            ctx.Dialogs.Show(spec);
        }

        static string EventLine(TimelineEvent ev)
        {
            string cmd = ev.Do?["cmd"]?.ToString() ?? "?";
            return TimelineText.Time(ev.T) + "  " + cmd + (EventClears(ev) ? "  ⚠" : "") + (string.IsNullOrEmpty(ev.Note) ? "" : "  — " + ev.Note);
        }

        /// <summary>Commands that change the scene's tree: run with undo false (an event's default) they empty the history.</summary>
        static readonly HashSet<string> Structural = new HashSet<string>(StringComparer.Ordinal)
        {
            "chara.load", "item.add", "light.add", "camera.add", "route.add", "route.addPoint", "route.deletePoint", "item.parent",
            "delete", "obj.reorder", "scene.import", "scene.new", "scene.load",
        };

        /// <summary>The event's command changes the tree and does not ask for undo (it clears the undo history when it runs).</summary>
        public static bool EventClears(TimelineEvent ev)
        {
            var d = ev?.Do;
            if (d == null) return false;
            string cmd = d["cmd"]?.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)d["cmd"] : null;
            if (cmd == null || !Structural.Contains(cmd)) return false;
            var u = d["undo"];
            return u == null || u.Type != Newtonsoft.Json.Linq.JTokenType.Boolean || !(bool)u;
        }

        // ------------------------------------------------------------ add track (the selection's channels)

        void AddMenu()
        {
            var doc = Doc;
            if (doc == null) return;
            var entries = TimelineAddMenu.Entries(ctx, doc);
            var items = new List<MenuItem>();
            string sep = null;
            foreach (var en in entries)
            {
                if (sep != null && en.Group != sep) items.Add(MenuItem.Sep());
                sep = en.Group;
                var e = en;
                items.Add(new MenuItem { Id = "timeline-add-" + e.Id, Text = e.Text, Disabled = e.Disabled, Run = () => AddTrack(e) });
            }
            var box = ctx.Menus?.Show(items, addButton, null, "timeline-add-menu");
            if (box == null) return;
            foreach (var en in entries)
                if (en.Tooltip != null && box.Q("menu-item-timeline-add-" + en.Id) is VisualElement row) row.tooltip = en.Tooltip;
        }

        void AddTrack(TimelineAddMenu.Entry e)
        {
            var tr = new TimelineTrack(ctx.Session.NewId(), e.Type, e.Key) { Bone = e.Bone, Slot = e.Slot };
            double t = Svc.Time;
            if (Show(Svc.AddTrack(tr, null, t, out string id)))
            {
                SelectedTracks.Clear();
                SelectedTracks.Add(id ?? tr.Id);
                SelectedKeys.Clear();
                SelectedKeys.Add(new TimelineKeyRef(id ?? tr.Id, (float)t));
            }
        }
    }
}
