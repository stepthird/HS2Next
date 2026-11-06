// The Timeline panel's body: the track list beside the key strip. Ruler: a click or a drag scrubs (each move one seek, no
// history); ticks by zoom; the wheel over the strip zooms around the pointer, Shift + wheel scrolls. Lanes: keys drawn by
// ease (◆ inout, ■ step, ▲ linear, ◢ in, ◣ out); a click selects (Shift / Ctrl add); a drag moves the selected keys,
// snapped to 0.01 s (Alt: free), committed on release as one entry «Move keys», Esc cancels; a double click on a lane seeks
// there and keys that track; right click on a key: Ease ▸, Set to the shown value, Delete. Track rows: the label, the
// enabled toggle, a state mark (target missing, bone missing, FK off for the bone, suspended by an edit, inactive); right
// click: Rename, Use the selection, Remove; rows reorder by drag (one entry). The events lane shows a marker per event
// (tooltip: time, command, note; ⚠ on a tree-changing command run without undo).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class TimelinePanel
    {
        VisualElement headLine;
        readonly List<Label> keyGlyphs = new List<Label>();

        // key drag
        List<TimelineKeyRef> dragKeys;
        float dragStartX, dragGrabT;
        double dragDelta;
        bool dragMoving;
        int dragPointer = -1;
        EventCallback<KeyDownEvent> dragEsc;
        VisualElement dragEscRoot;

        // ruler scrub, row drag
        bool scrubbing;
        int rowDragFrom = -1;
        float rowDragStartY;
        bool rowDragMoving;
        int rowDragPointer = -1;

        void BuildBody(VisualElement parent)
        {
            rulerRow = new VisualElement { name = "timeline-rulerrow" };
            rulerRow.AddToClassList("hs-tl__rulerrow");
            var corner = new VisualElement();
            corner.AddToClassList("hs-tl__corner");
            corner.style.width = TrackColumnWidth;
            ruler = new VisualElement { name = "timeline-ruler" };
            ruler.AddToClassList("hs-tl__ruler");
            head = new VisualElement { name = "timeline-head", pickingMode = PickingMode.Ignore };
            head.AddToClassList("hs-tl__head");
            ruler.Add(head);
            rulerRow.Add(corner);
            rulerRow.Add(ruler);
            ruler.RegisterCallback<PointerDownEvent>(OnRulerDown);
            ruler.RegisterCallback<PointerMoveEvent>(OnRulerMove);
            ruler.RegisterCallback<PointerUpEvent>(OnRulerUp);
            ruler.RegisterCallback<WheelEvent>(OnWheel);
            ruler.RegisterCallback<GeometryChangedEvent>(_ => DrawRuler());

            scroll = new ScrollView(ScrollViewMode.Vertical) { name = "timeline-scroll" };
            scroll.AddToClassList("hs-tl__scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var content = new VisualElement();
            content.AddToClassList("hs-tl__content");
            tracksCol = new VisualElement { name = "timeline-tracks" };
            tracksCol.AddToClassList("hs-tl__tracks");
            tracksCol.style.width = TrackColumnWidth;
            tracksCol.RegisterCallback<PointerMoveEvent>(OnRowsMove);
            tracksCol.RegisterCallback<PointerUpEvent>(OnRowsUp);
            lanesCol = new VisualElement { name = "timeline-lanes" };
            lanesCol.AddToClassList("hs-tl__lanes");
            headLine = new VisualElement { name = "timeline-head-line", pickingMode = PickingMode.Ignore };
            headLine.AddToClassList("hs-tl__headline");
            lanesCol.RegisterCallback<PointerDownEvent>(OnLanesDown);
            lanesCol.RegisterCallback<PointerMoveEvent>(OnLanesMove);
            lanesCol.RegisterCallback<PointerUpEvent>(OnLanesUp);
            lanesCol.RegisterCallback<WheelEvent>(OnWheel);
            lanesCol.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (fitted) FitView(false);
                DrawKeys();
                DrawEvents();
                DrawRuler();
                UpdatePlayHead();
            });
            content.Add(tracksCol);
            content.Add(lanesCol);
            scroll.Add(content);
            parent.Add(rulerRow);
            parent.Add(scroll);
        }

        float LaneWidth
        {
            get
            {
                float w = lanesCol.layout.width;
                return float.IsNaN(w) ? 0 : w;
            }
        }

        public float X(double t) => (float)((t - ViewStart) * Pps);
        public double T(float x) => ViewStart + x / Pps;

        /// <summary>Shows the whole length in the strip (⋯ ▸ Fit; also while the user has not zoomed).</summary>
        public void FitView(bool redraw)
        {
            var doc = Doc;
            float w = LaneWidth;
            fitted = true;
            if (doc == null || w < 20) return;
            Pps = Math.Max(1e-3, (w - 14) / Math.Max(0.1, doc.Duration));
            ViewStart = -7 / Pps;
            if (redraw) { DrawKeys(); DrawEvents(); DrawRuler(); UpdatePlayHead(); }
        }

        // ------------------------------------------------------------ rows

        void BuildRows(TimelineDocument doc)
        {
            tracksCol.Clear();
            trackRows.Clear();
            lanesCol.Clear();
            lanes.Clear();
            for (int i = 0; i < doc.Tracks.Count; i++)
            {
                var tr = doc.Tracks[i];
                int index = i;
                var row = new VisualElement { name = "timeline-track-" + i };
                row.AddToClassList("hs-tl__track");
                row.EnableInClassList("hs-tl__track--sel", SelectedTracks.Contains(tr.Id));
                var en = new Toggle { name = "timeline-track-" + i + "-enabled", value = tr.Enabled };
                en.AddToClassList("hs-tl__enabled");
                en.RegisterValueChangedCallback(e => SetTrackEnabled(index, e.newValue));
                var label = new Label(TimelineText.Label(ctx.Model, tr)) { name = "timeline-track-" + i + "-label" };
                label.AddToClassList("hs-tl__tracklabel");
                label.tooltip = tr.Describe();
                var mark = new Label { name = "timeline-track-" + i + "-mark" };
                mark.AddToClassList("hs-tl__mark");
                row.Add(en);
                row.Add(label);
                row.Add(mark);
                row.RegisterCallback<PointerDownEvent>(e => OnRowDown(index, e));
                tracksCol.Add(row);
                trackRows.Add(row);
                var lane = new VisualElement { name = "timeline-lane-" + i, pickingMode = PickingMode.Position };
                lane.AddToClassList("hs-tl__lane");
                lane.EnableInClassList("hs-tl__lane--sel", SelectedTracks.Contains(tr.Id));
                lanesCol.Add(lane);
                lanes.Add(lane);
            }
            var evLabel = Loc.Label("timeline.events");
            evLabel.name = "timeline-events-label";
            evLabel.AddToClassList("hs-tl__track");
            evLabel.AddToClassList("hs-tl__track--events");
            tracksCol.Add(evLabel);
            eventsLane = new VisualElement { name = "timeline-events" };
            eventsLane.AddToClassList("hs-tl__lane");
            eventsLane.AddToClassList("hs-tl__lane--events");
            lanesCol.Add(eventsLane);
            lanesCol.Add(headLine);
        }

        /// <summary>The state marks of the rows: target missing, bone missing, FK off for the bone, suspended, inactive.</summary>
        void UpdateMarks()
        {
            var doc = Doc;
            if (doc == null || trackRows.Count != doc.Tracks.Count) return;
            var svc = Svc;
            for (int i = 0; i < doc.Tracks.Count; i++)
            {
                var tr = doc.Tracks[i];
                var row = trackRows[i];
                var mark = row.Q<Label>("timeline-track-" + i + "-mark");
                string text = "", tip = null;
                bool grey = false, warn = false;
                if (!tr.IsKnownType || doc.IsShadowed(i)) { grey = true; text = "–"; tip = L10n.Get("timeline.inactive"); }
                else if (!TimelineUiService.TargetExists(ctx.Model, tr)) { grey = true; text = "?"; tip = L10n.Get("timeline.missing"); }
                else
                {
                    var bone = BoneState(tr);
                    if (bone == BoneMark.Missing) { grey = true; text = "?"; tip = L10n.Get("timeline.boneMissing"); }
                    else if (bone == BoneMark.FkOff) { warn = true; text = "⚠"; tip = L10n.Get("timeline.fkOff"); }
                    else if (svc.Suspended(tr.Id)) { text = "•"; tip = L10n.Get("timeline.suspended"); }
                }
                if (!tr.Enabled) grey = true;
                row.EnableInClassList("hs-tl__track--grey", grey);
                row.EnableInClassList("hs-tl__track--warn", warn);
                if (i < lanes.Count) lanes[i].EnableInClassList("hs-tl__lane--grey", grey);
                if (mark != null)
                {
                    mark.text = text;
                    mark.tooltip = tip;
                }
                row.tooltip = tip ?? "";
            }
        }

        enum BoneMark { None, Missing, FkOff }

        /// <summary>For a bone rotation track: the bone missing on the built character, or its FK group not applied.</summary>
        BoneMark BoneState(TimelineTrack tr)
        {
            if (tr.Type != TimelineTypes.Rot || tr.Bone == null || tr.Key == null) return BoneMark.None;
            var n = ctx.Model.Find(tr.Key);
            var st = n?.State as CharaNodeState;
            if (st == null) return BoneMark.None;
            var real = ctx.Runtime?.FindChara(tr.Key);
            if (real == null || real.State != RealizationState.Built) return BoneMark.None;
            var table = FkDriver.TableFor(real);
            if (table == null) return BoneMark.None;
            foreach (FkGroup g in Enum.GetValues(typeof(FkGroup)))
                foreach (var b in table.Of(g))
                    if (b?.Key == tr.Bone) return st.Fk.Enabled && Applied(st.Fk.Groups, g) ? BoneMark.None : BoneMark.FkOff;
            return BoneMark.Missing;
        }

        static bool Applied(FkGroups g, FkGroup x)
        {
            switch (x)
            {
                case FkGroup.Hair: return g.Hair;
                case FkGroup.Neck: return g.Neck;
                case FkGroup.Breast: return g.Breast;
                case FkGroup.Body: return g.Body;
                case FkGroup.RightHand: return g.RightHand;
                case FkGroup.LeftHand: return g.LeftHand;
                default: return g.Skirt;
            }
        }

        void SetTrackEnabled(int index, bool on)
        {
            var doc = Doc;
            if (doc == null || index >= doc.Tracks.Count) return;
            var c = doc.Tracks[index].Clone();
            if (c.Enabled == on) return;
            c.Enabled = on;
            if (!Show(Svc.ReplaceTrack(c, TimelineEditCommand.LabelTrack))) Rebuild();
        }

        void OnRowDown(int index, PointerDownEvent e)
        {
            var doc = Doc;
            if (doc == null || index >= doc.Tracks.Count) return;
            if (e.target is Toggle || (e.target as VisualElement)?.GetFirstAncestorOfType<Toggle>() != null) return;
            string id = doc.Tracks[index].Id;
            if (e.button == 1)
            {
                if (!SelectedTracks.Contains(id)) { SelectedTracks.Clear(); SelectedTracks.Add(id); RefreshSelection(); }
                TrackMenu(index, e.position);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            bool add = e.shiftKey || e.ctrlKey || e.commandKey;
            if (add) { if (!SelectedTracks.Remove(id)) SelectedTracks.Add(id); }
            else { SelectedTracks.Clear(); SelectedTracks.Add(id); }
            RefreshSelection();
            Focus();
            rowDragFrom = index;
            rowDragStartY = tracksCol.WorldToLocal(e.position).y;
            rowDragMoving = false;
            rowDragPointer = e.pointerId;
            tracksCol.CapturePointer(e.pointerId);
            HookDragEsc();
            e.StopPropagation();
        }

        void OnRowsMove(PointerMoveEvent e)
        {
            if (rowDragFrom < 0 || !tracksCol.HasPointerCapture(e.pointerId)) return;
            float y = tracksCol.WorldToLocal(e.position).y;
            if (!rowDragMoving && Math.Abs(y - rowDragStartY) > 4) rowDragMoving = true;
            if (rowDragMoving)
            {
                int to = RowAt(y);
                for (int i = 0; i < trackRows.Count; i++) trackRows[i].EnableInClassList("hs-tl__track--drop", i == to && to != rowDragFrom);
            }
        }

        void OnRowsUp(PointerUpEvent e)
        {
            if (rowDragFrom < 0) return;
            if (tracksCol.HasPointerCapture(e.pointerId)) tracksCol.ReleasePointer(e.pointerId);
            UnhookDragEsc();
            int from = rowDragFrom;
            rowDragFrom = -1;
            foreach (var r in trackRows) r.RemoveFromClassList("hs-tl__track--drop");
            if (!rowDragMoving) return;
            rowDragMoving = false;
            int to = RowAt(tracksCol.WorldToLocal(e.position).y);
            MoveTrack(from, to);
        }

        /// <summary>Esc during a track row's drag: the order stays, nothing is recorded, the release after it does nothing.</summary>
        void CancelRowDrag()
        {
            if (rowDragFrom < 0) return;
            if (rowDragPointer >= 0 && tracksCol.HasPointerCapture(rowDragPointer)) tracksCol.ReleasePointer(rowDragPointer);
            UnhookDragEsc();
            rowDragFrom = -1;
            rowDragMoving = false;
            rowDragPointer = -1;
            foreach (var r in trackRows) r.RemoveFromClassList("hs-tl__track--drop");
        }

        int RowAt(float y) => Mathf.Clamp(Mathf.FloorToInt(y / RowHeight), 0, Math.Max(0, trackRows.Count - 1));

        /// <summary>Moves track <paramref name="from"/> to index <paramref name="to"/>: one entry «Track».</summary>
        public void MoveTrack(int from, int to)
        {
            var doc = Doc;
            if (doc == null || from < 0 || from >= doc.Tracks.Count || to < 0 || to >= doc.Tracks.Count || from == to) return;
            var after = doc.Clone();
            var t = after.Tracks[from];
            after.Tracks.RemoveAt(from);
            after.Tracks.Insert(to, t);
            Show(Svc.ReplaceDocument(after, TimelineEditCommand.LabelTrack));
        }

        void TrackMenu(int index, Vector2 at)
        {
            var doc = Doc;
            var tr = doc?.Tracks[index];
            if (tr == null) return;
            var items = new List<MenuItem>
            {
                new MenuItem { Id = "timeline-rename", Text = L10n.Get("timeline.rename"), Run = () => RenameTrack(tr.Id) },
                new MenuItem { Id = "timeline-retarget", Text = L10n.Get("timeline.retarget"), Disabled = RetargetOf(tr) == null, Run = () => RetargetTrack(tr.Id) },
                new MenuItem { Id = "timeline-remove", Text = L10n.Get("timeline.removeTrack"), Run = RemoveSelectedTracks },
            };
            ctx.Menus?.Show(items, null, at, "timeline-track-menu");
        }

        void RenameTrack(string id)
        {
            var tr = Doc?.FindTrack(id);
            if (tr == null || ctx.Dialogs == null) return;
            ctx.Dialogs.Prompt("timeline.rename", tr.Label ?? TimelineText.Label(ctx.Model, tr), s =>
            {
                if (s == null) return;
                var cur = Doc?.FindTrack(id);
                if (cur == null) return;
                var c = cur.Clone();
                s = s.Trim();
                c.Label = s.Length == 0 || s == TimelineText.Label(ctx.Model, WithoutLabel(cur)) ? null : s.Length > TimelineLimits.MaxText ? s.Substring(0, TimelineLimits.MaxText) : s;
                if (c.Label != cur.Label) Show(Svc.ReplaceTrack(c, TimelineEditCommand.LabelTrack));
            });
        }

        static TimelineTrack WithoutLabel(TimelineTrack t)
        {
            var c = t.Clone();
            c.Label = null;
            return c;
        }

        /// <summary>The track retargeted to the selection (the selected node, or the FK bone handle for a bone track), or null.</summary>
        TimelineTrack RetargetOf(TimelineTrack tr)
        {
            if (tr == null || !tr.IsKnownType || TimelineTypes.IsSceneWide(tr.Type)) return null;
            var c = tr.Clone();
            if (tr.Type == TimelineTypes.Rot && tr.Bone != null)
            {
                if (!StudioOps.TryParseSub(ctx.SubSelection, out var sid, out var kind, out var bone) || kind != "fk" || bone == null) return null;
                c.Key = sid;
                c.Bone = bone;
            }
            else
            {
                string id = ctx.Selection.Primary;
                if (id == null) return null;
                c.Key = id;
            }
            if (c.Key == tr.Key && c.Bone == tr.Bone) return null;
            return TimelineUiService.TargetExists(ctx.Model, c) ? c : null;
        }

        void RetargetTrack(string id)
        {
            var c = RetargetOf(Doc?.FindTrack(id));
            if (c != null) Show(Svc.ReplaceTrack(c, TimelineEditCommand.LabelTrack));
        }

        /// <summary>Removes the selected tracks: one entry «Remove track».</summary>
        public void RemoveSelectedTracks()
        {
            var doc = Doc;
            if (doc == null || SelectedTracks.Count == 0) return;
            var after = doc.Clone();
            int n = after.Tracks.RemoveAll(t => SelectedTracks.Contains(t.Id));
            if (n == 0) return;
            if (Show(Svc.ReplaceDocument(after, TimelineEditCommand.LabelRemoveTrack))) { SelectedTracks.Clear(); SelectedKeys.Clear(); }
        }

        void RefreshSelection()
        {
            var doc = Doc;
            if (doc == null) return;
            for (int i = 0; i < trackRows.Count && i < doc.Tracks.Count; i++)
            {
                bool sel = SelectedTracks.Contains(doc.Tracks[i].Id);
                trackRows[i].EnableInClassList("hs-tl__track--sel", sel);
                if (i < lanes.Count) lanes[i].EnableInClassList("hs-tl__lane--sel", sel);
            }
            foreach (var g in keyGlyphs) g.EnableInClassList("hs-tl__key--sel", g.userData is TimelineKeyRef r && SelectedKeys.Contains(r));
            UpdateHeaderEnabled();
            ShowKeyEditor();
        }

        // ------------------------------------------------------------ keys, events, ruler, play head

        void DrawKeys()
        {
            var doc = Doc;
            keyGlyphs.Clear();
            if (doc == null) return;
            float w = LaneWidth;
            for (int i = 0; i < lanes.Count && i < doc.Tracks.Count; i++)
            {
                var lane = lanes[i];
                lane.Clear();
                var tr = doc.Tracks[i];
                if (!tr.IsKnownType || tr.Keys == null) continue;
                for (int k = 0; k < tr.Keys.Count; k++)
                {
                    var key = tr.Keys[k];
                    var r = new TimelineKeyRef(tr.Id, key.T);
                    double t = key.T;
                    if (dragKeys != null && dragMoving && dragKeys.Contains(r)) t += dragDelta;
                    float x = X(t);
                    if (w > 0 && (x < -8 || x > w + 8)) continue;
                    string ease = TimelineTypes.IsStepOnly(tr.Type) ? TimelineEases.Step : key.Ease;
                    var g = new Label(TimelineText.Glyph(ease)) { name = "timeline-key-" + i + "-" + k, pickingMode = PickingMode.Ignore, userData = r };
                    g.AddToClassList("hs-tl__key");
                    g.EnableInClassList("hs-tl__key--sel", SelectedKeys.Contains(r));
                    g.style.left = x - 6;
                    g.tooltip = TimelineText.Time(key.T);
                    lane.Add(g);
                    keyGlyphs.Add(g);
                }
            }
        }

        void DrawEvents()
        {
            var doc = Doc;
            if (eventsLane == null) return;
            eventsLane.Clear();
            if (doc == null) return;
            float w = LaneWidth;
            for (int j = 0; j < doc.Events.Count; j++)
            {
                var ev = doc.Events[j];
                float x = X(ev.T);
                if (w > 0 && (x < -8 || x > w + 8)) continue;
                bool clears = EventClears(ev);
                var m = new Label(clears ? "⚠" : "│") { name = "timeline-event-" + j };
                m.AddToClassList("hs-tl__event");
                if (clears) m.AddToClassList("hs-tl__event--clears");
                if (!ev.Enabled) m.AddToClassList("hs-tl__event--off");
                m.style.left = x - 4;
                m.tooltip = EventLine(ev) + (clears ? "\n" + L10n.Get("timeline.eventClears") : "");
                eventsLane.Add(m);
            }
        }

        void DrawRuler()
        {
            if (ruler == null) return;
            for (int i = ruler.childCount - 1; i >= 0; i--) if (ruler[i] != head) ruler.RemoveAt(i);
            var doc = Doc;
            float w = ruler.layout.width;
            if (doc == null || float.IsNaN(w) || w <= 0 || Pps <= 0) return;
            double[] steps = { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };
            double major = steps[steps.Length - 1];
            foreach (var s in steps) if (s * Pps >= 64) { major = s; break; }
            double minor = major / 5 * Pps >= 8 ? major / 5 : major / 2 * Pps >= 8 ? major / 2 : 0;
            double t0 = Math.Max(0, T(0)), t1 = Math.Min(doc.Duration, T(w));
            if (minor > 0)
                for (double t = Math.Ceiling(t0 / minor - 1e-9) * minor; t <= t1 + 1e-9; t += minor)
                {
                    var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                    tick.AddToClassList("hs-tl__tick");
                    tick.style.left = X(t);
                    ruler.Add(tick);
                }
            int n = 0;
            for (double t = Math.Ceiling(t0 / major - 1e-9) * major; t <= t1 + 1e-9 && n < 200; t += major, n++)
            {
                var tick = new VisualElement { pickingMode = PickingMode.Ignore };
                tick.AddToClassList("hs-tl__tick");
                tick.AddToClassList("hs-tl__tick--major");
                tick.style.left = X(t);
                ruler.Add(tick);
                var lab = new Label(TimelineText.Seconds(Math.Round(t, 3))) { pickingMode = PickingMode.Ignore };
                lab.AddToClassList("hs-tl__ticklabel");
                lab.style.left = X(t) + 2;
                ruler.Add(lab);
            }
            var end = new VisualElement { pickingMode = PickingMode.Ignore };
            end.AddToClassList("hs-tl__end");
            end.style.left = X(doc.Duration);
            ruler.Add(end);
            head.BringToFront();
        }

        void UpdatePlayHead()
        {
            if (head == null) return;
            var doc = Doc;
            double t = Svc.Time;
            float x = X(t);
            float w = LaneWidth;
            bool show = doc != null && x >= -1 && (w <= 0 || x <= w + 1);
            head.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            headLine.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            head.style.left = x - 1;
            headLine.style.left = x - 1;
            if (timeField != null && timeField.panel?.focusController?.focusedElement != timeField) timeField.SetValueWithoutNotify(TimelineText.Time(t));
        }

        // ------------------------------------------------------------ ruler: scrub

        void OnRulerDown(PointerDownEvent e)
        {
            if (e.button != 0 || Doc == null) return;
            scrubbing = true;
            ruler.CapturePointer(e.pointerId);
            ScrubTo(ruler.WorldToLocal(e.position).x);
            e.StopPropagation();
        }

        void OnRulerMove(PointerMoveEvent e)
        {
            if (!scrubbing || !ruler.HasPointerCapture(e.pointerId)) return;
            ScrubTo(ruler.WorldToLocal(e.position).x);
        }

        void OnRulerUp(PointerUpEvent e)
        {
            if (!scrubbing) return;
            scrubbing = false;
            if (ruler.HasPointerCapture(e.pointerId)) ruler.ReleasePointer(e.pointerId);
        }

        void ScrubTo(float x)
        {
            var doc = Doc;
            if (doc == null) return;
            double t = Math.Max(0, Math.Min(doc.Duration, T(x)));
            if (Show(Svc.Seek(t))) UpdatePlayHead();
        }

        // ------------------------------------------------------------ zoom / scroll

        void OnWheel(WheelEvent e)
        {
            var doc = Doc;
            if (doc == null) return;
            float d = e.delta.y;
            if (Mathf.Approximately(d, 0)) return;
            float x = lanesCol.WorldToLocal(e.mousePosition).x;
            if (e.shiftKey)
            {
                double visible = LaneWidth > 0 ? LaneWidth / Pps : doc.Duration;
                ViewStart += Math.Sign(d) * visible * 0.1;
            }
            else
            {
                double at = T(x);
                double f = Math.Pow(1.15, -Math.Sign(d));
                Pps = Math.Max(1e-3, Math.Min(20000, Pps * f));
                ViewStart = at - x / Pps;
            }
            double margin = 7 / Pps;
            ViewStart = Math.Max(-margin, Math.Min(doc.Duration, ViewStart));
            fitted = false;
            DrawKeys();
            DrawEvents();
            DrawRuler();
            UpdatePlayHead();
            e.StopPropagation();
        }

        // ------------------------------------------------------------ lanes: select, drag, double click, menu

        (int lane, int key) Hit(Vector2 local)
        {
            var doc = Doc;
            int i = Mathf.FloorToInt(local.y / RowHeight);
            if (doc == null || i < 0) return (-1, -1);
            if (i >= doc.Tracks.Count) return (i == doc.Tracks.Count ? i : -1, -1);
            var tr = doc.Tracks[i];
            int best = -1;
            float bestD = 7;
            if (tr.IsKnownType && tr.Keys != null)
                for (int k = 0; k < tr.Keys.Count; k++)
                {
                    float dx = Math.Abs(X(tr.Keys[k].T) - local.x);
                    if (dx <= bestD) { bestD = dx; best = k; }
                }
            return (i, best);
        }

        void OnLanesDown(PointerDownEvent e)
        {
            var doc = Doc;
            if (doc == null) return;
            var local = lanesCol.WorldToLocal(e.position);
            var (lane, key) = Hit(local);
            if (lane < 0 || lane >= doc.Tracks.Count) return;
            var tr = doc.Tracks[lane];
            bool add = e.shiftKey || e.ctrlKey || e.commandKey;
            Focus();
            if (e.button == 1)
            {
                if (key >= 0)
                {
                    var r = new TimelineKeyRef(tr.Id, tr.Keys[key].T);
                    if (!SelectedKeys.Contains(r)) { SelectedKeys.Clear(); SelectedKeys.Add(r); SelectTrackOnly(tr.Id); RefreshSelection(); }
                    KeyMenu(tr, r, e.position);
                }
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            if (key < 0)
            {
                if (e.clickCount >= 2 && tr.IsKnownType)
                {
                    double t = Snap(T(local.x), e.altKey);
                    t = Math.Max(0, Math.Min(doc.Duration, t));
                    SelectTrackOnly(tr.Id);
                    if (Show(Svc.Seek(t)) && Show(Svc.KeyShown(new[] { tr.Id }, t)))
                    {
                        SelectedKeys.Clear();
                        SelectedKeys.Add(new TimelineKeyRef(tr.Id, (float)t));
                    }
                    RefreshSelection();
                }
                else
                {
                    if (!add) SelectedKeys.Clear();
                    if (add) { if (!SelectedTracks.Contains(tr.Id)) SelectedTracks.Add(tr.Id); }
                    else SelectTrackOnly(tr.Id);
                    RefreshSelection();
                }
                e.StopPropagation();
                return;
            }
            var hit = new TimelineKeyRef(tr.Id, tr.Keys[key].T);
            if (add)
            {
                if (!SelectedKeys.Remove(hit)) SelectedKeys.Add(hit);
                if (!SelectedTracks.Contains(tr.Id)) SelectedTracks.Add(tr.Id);
            }
            else if (!SelectedKeys.Contains(hit))
            {
                SelectedKeys.Clear();
                SelectedKeys.Add(hit);
                SelectTrackOnly(tr.Id);
            }
            RefreshSelection();
            if (SelectedKeys.Contains(hit))
            {
                dragKeys = SelectedKeys.ToList();
                dragStartX = local.x;
                dragGrabT = hit.T;
                dragDelta = 0;
                dragMoving = false;
                dragPointer = e.pointerId;
                lanesCol.CapturePointer(e.pointerId);
                HookDragEsc();
            }
            e.StopPropagation();
        }

        void SelectTrackOnly(string id)
        {
            SelectedTracks.Clear();
            SelectedTracks.Add(id);
        }

        static double Snap(double t, bool free) => free ? t : Math.Round(t / SnapSeconds) * SnapSeconds;

        void OnLanesMove(PointerMoveEvent e)
        {
            if (dragKeys == null || e.pointerId != dragPointer || !lanesCol.HasPointerCapture(e.pointerId)) return;
            float x = lanesCol.WorldToLocal(e.position).x;
            if (!dragMoving && Math.Abs(x - dragStartX) <= 3) return;
            dragMoving = true;
            double t = dragGrabT + (x - dragStartX) / Pps;
            dragDelta = Snap(t, e.altKey) - dragGrabT;
            DrawKeys();
        }

        void OnLanesUp(PointerUpEvent e)
        {
            if (dragKeys == null || e.pointerId != dragPointer) return;
            if (lanesCol.HasPointerCapture(e.pointerId)) lanesCol.ReleasePointer(e.pointerId);
            var keys = dragKeys;
            bool moved = dragMoving && Math.Abs(dragDelta) > 1e-9;
            double delta = dragDelta;
            EndKeyDrag();
            if (moved) MoveKeys(keys, delta);
            else DrawKeys();
        }

        void HookDragEsc()
        {
            UnhookDragEsc();
            dragEscRoot = panel?.visualTree;
            if (dragEscRoot == null) return;
            dragEsc = ev =>
            {
                if (ev.keyCode != KeyCode.Escape || (dragKeys == null && rowDragFrom < 0)) return;
                ev.StopPropagation();
                if (dragKeys != null) CancelKeyDrag();
                else CancelRowDrag();
            };
            dragEscRoot.RegisterCallback(dragEsc, TrickleDown.TrickleDown);
        }

        void UnhookDragEsc()
        {
            if (dragEscRoot != null && dragEsc != null) dragEscRoot.UnregisterCallback(dragEsc, TrickleDown.TrickleDown);
            dragEscRoot = null;
            dragEsc = null;
        }

        void EndKeyDrag()
        {
            if (dragPointer >= 0 && lanesCol.HasPointerCapture(dragPointer)) lanesCol.ReleasePointer(dragPointer);
            dragKeys = null;
            dragMoving = false;
            dragDelta = 0;
            dragPointer = -1;
            UnhookDragEsc();
        }

        /// <summary>Esc during a key drag: the keys go back, nothing is recorded.</summary>
        public void CancelKeyDrag()
        {
            if (dragKeys == null) return;
            EndKeyDrag();
            DrawKeys();
        }

        /// <summary>A key drag is running (tests).</summary>
        public bool KeyDragActive => dragKeys != null;

        /// <summary>Moves <paramref name="keys"/> by <paramref name="delta"/> seconds: one entry «Move keys»; refused (toast) when a key
        /// would leave [0, length] or meet another key of its track.</summary>
        public bool MoveKeys(IReadOnlyList<TimelineKeyRef> keys, double delta)
        {
            var doc = Doc;
            if (doc == null || keys == null || keys.Count == 0) return false;
            var changed = new List<TimelineTrack>();
            var moved = new List<TimelineKeyRef>();
            foreach (var g in keys.GroupBy(k => k.Track))
            {
                var tr = doc.FindTrack(g.Key);
                if (tr == null || !tr.IsKnownType) continue;
                var c = tr.Clone();
                var times = new HashSet<float>(g.Select(k => k.T));
                foreach (var k in c.Keys)
                {
                    if (!times.Contains(k.T)) continue;
                    float nt = (float)(k.T + delta);
                    if (nt < 0 || nt > doc.Duration)
                    {
                        ctx.Toasts?.Show(L10n.Format("timeline.moveOutside", TimelineText.Time(nt)), ToastKind.Error);
                        DrawKeys();
                        return false;
                    }
                    moved.Add(new TimelineKeyRef(c.Id, nt));
                    k.T = nt;
                }
                var dup = c.Keys.GroupBy(k => k.T).FirstOrDefault(x => x.Count() > 1);
                if (dup != null)
                {
                    ctx.Toasts?.Show(L10n.Format("timeline.moveMeets", TimelineText.Time(dup.Key)), ToastKind.Error);
                    DrawKeys();
                    return false;
                }
                changed.Add(c);
            }
            if (!Commit(changed, TimelineEditCommand.LabelMoveKeys)) { DrawKeys(); return false; }
            SelectedKeys.Clear();
            SelectedKeys.AddRange(moved);
            return true;
        }

        void KeyMenu(TimelineTrack tr, TimelineKeyRef r, Vector2 at)
        {
            bool stepOnly = TimelineTypes.IsStepOnly(tr.Type);
            var eases = new List<MenuItem>();
            foreach (var ease in TimelineEases.All)
            {
                string es = ease;
                eases.Add(new MenuItem { Id = "timeline-key-ease-" + es, Text = L10n.Get(TimelineText.EaseKey(es)), Checked = SelectedEases().Count == 1 && SelectedEases().Contains(es), Run = () => SetEase(es) });
            }
            var items = new List<MenuItem>
            {
                new MenuItem { Id = "timeline-key-ease", Text = L10n.Get("timeline.easeMenu"), Submenu = eases, Disabled = stepOnly },
                new MenuItem { Id = "timeline-key-shown", Text = L10n.Get("timeline.setShown"), Run = () => Show(Svc.KeyShown(new[] { r.Track }, r.T)) },
                new MenuItem { Id = "timeline-key-delete", Text = L10n.Get("timeline.deleteKeys"), Run = DeleteSelectedKeys },
            };
            ctx.Menus?.Show(items, null, at, "timeline-key-menu");
        }
    }
}
