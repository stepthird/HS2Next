// The key editor of the Timeline panel (timeline-keyedit): the one selected key's time, ease and value with the host
// widgets of its shape — a number slider (animation speed and time, eyes and mouth openness; pattern ids as whole
// numbers), a vector field (position, scale, rotation in Euler degrees), a colour swatch with the picker, an animation row
// (group / category / no), the camera (target, rotation, distance, field of view) and the depth of field (on, focus: a node
// or the camera pivot, focal size, aperture, blur with 0 = the renderer's). A drag is one UI edit, one history entry; Esc takes
// it back and records nothing; a typed value is one entry and a second typed value of the same key merges with it. A new time
// moves the key («Move keys»); a new ease is «Key». Element names: timeline-keyedit-<part>-widget / -field / -slider /
// -c0..2 / -swatch / -dropdown / -toggle / -ref (parts t, ease, v, target, rot, dist, fov, on, focus, size, aperture, blur,
// group, category, no).
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Timeline;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Plugins;
using UnityEngine;
using UnityEngine.UIElements;
using ApiPhase = HS2Next.Api.Ui.EditPhase;

namespace HS2Next.UI.Panels
{
    public sealed class TimelineKeyEditor : VisualElement
    {
        const string Base = "timeline-keyedit-";
        readonly UiContext ctx;
        readonly TimelinePanel owner;
        string trackId, type;
        float time;
        bool dragging;
        int dragSeq;
        UiEdit dragEdit;
        readonly List<Action<TimelineKey>> shows = new List<Action<TimelineKey>>();

        public TimelineKeyEditor(UiContext ctx, TimelinePanel owner)
        {
            this.ctx = ctx;
            this.owner = owner;
            name = "timeline-keyedit";
            AddToClassList("hs-tl__keyedit");
            style.display = DisplayStyle.None;
        }

        /// <summary>The track id and time of the key shown, or null.</summary>
        public string TrackId => trackId;
        public float KeyTime => time;
        /// <summary>A value drag is running (its previews merge into one entry).</summary>
        public bool Dragging => dragging;

        TimelineUiService Svc => TimelineUiModule.ServiceOf(ctx);

        /// <summary>Shows a key (null hides the editor). The same key again only refreshes the values (not during a drag).</summary>
        public void Show(TimelineTrack track, TimelineKey key)
        {
            if (track == null || key == null)
            {
                if (dragging) return;
                trackId = null;
                type = null;
                Clear();
                shows.Clear();
                style.display = DisplayStyle.None;
                return;
            }
            string ty = track.Type + (track.Bone != null ? "+bone" : "") + (track.Slot.HasValue ? "+slot" : "");
            if (track.Id == trackId && key.T == time && ty == type)
            {
                if (!dragging) foreach (var s in shows) s(key);
                return;
            }
            if (dragging) return;
            trackId = track.Id;
            time = key.T;
            type = ty;
            Build(track, key);
        }

        void Build(TimelineTrack track, TimelineKey key)
        {
            Clear();
            shows.Clear();
            style.display = DisplayStyle.Flex;
            var title = new Label(TimelineText.Label(ctx.Model, track) + "  " + TimelineText.Time(key.T)) { name = Base + "title" };
            title.AddToClassList("hs-tl__keytitle");
            Add(title);

            var tw = PluginWidgetKit.Number(Base + "t", "timeline.keyedit.t", 0, ctx.Model.Timeline?.Duration ?? 10, false, "0.###", null, (v, p, typed) =>
            {
                if (p != ApiPhase.Commit) return;
                var doc = ctx.Model.Timeline;
                if (doc == null) return;
                float nt = (float)Math.Max(0, Math.Min(doc.Duration, v));
                if (nt == time) return;
                owner.MoveKeys(new[] { new TimelineKeyRef(trackId, time) }, nt - (double)time);
            });
            Add(tw.Element);
            shows.Add(k => tw.SetValue((double)k.T));

            if (!TimelineTypes.IsStepOnly(track.Type))
            {
                var labels = new List<(string, string)>();
                foreach (var e in TimelineEases.All) labels.Add((TimelineText.EaseKey(e), null));
                var ew = PluginWidgetKit.Choice(Base + "ease", "timeline.keyedit.ease", labels, i => owner.SetEase(TimelineEases.All[i]));
                Add(ew.Element);
                shows.Add(k => ew.SetValue(Math.Max(0, IndexOf(TimelineEases.All, TimelineEases.IsKnown(k.Ease) ? k.Ease : TimelineEases.InOut))));
            }

            switch (track.Shape)
            {
                case TimelineValueShape.Number: NumberRow(track.Type); break;
                case TimelineValueShape.Integer: IntegerRow(); break;
                case TimelineValueShape.Vector3:
                case TimelineValueShape.Euler: VectorRow(Base + "v", "timeline.keyedit.value", k => k.Vector, (k, v) => k.Vector = v); break;
                case TimelineValueShape.Color: ColorRow(); break;
                case TimelineValueShape.Anime: AnimeRows(); break;
                case TimelineValueShape.Camera: CameraRows(); break;
                case TimelineValueShape.Dof: DofRows(); break;
            }
            foreach (var s in shows) s(key);
        }

        static int IndexOf(IReadOnlyList<string> l, string s)
        {
            for (int i = 0; i < l.Count; i++) if (l[i] == s) return i;
            return -1;
        }

        // ------------------------------------------------------------ writing a value

        /// <summary>Writes the key's value through <paramref name="set"/>: the previews of a drag lie in one edit, Cancel takes that edit
        /// back (no entry), a typed value merges with a typed value of the same key.</summary>
        void Write(Action<TimelineKey> set, ApiPhase phase, bool typed)
        {
            if (phase == ApiPhase.Cancel && dragEdit != null)
            {
                var open = dragEdit;
                dragEdit = null;
                dragging = false;
                open.Cancel();
                return;
            }
            // the edit was cancelled from elsewhere: the drag's last values write nothing
            if (dragEdit != null && !dragEdit.IsOpen)
            {
                if (phase != ApiPhase.Preview) { dragEdit = null; dragging = false; }
                return;
            }
            var doc = ctx.Model.Timeline;
            var tr = doc?.FindTrack(trackId);
            if (tr == null) return;
            if (phase == ApiPhase.Preview && !dragging)
            {
                dragging = true;
                dragSeq++;
                dragEdit = ctx.BeginEdit(TimelineEditCommand.LabelKey);
            }
            string merge = typed ? "timeline.typed:" + trackId + ":" + time.ToString("R", CultureInfo.InvariantCulture)
                : "timeline.drag:" + trackId + ":" + time.ToString("R", CultureInfo.InvariantCulture) + ":" + dragSeq.ToString(CultureInfo.InvariantCulture);
            bool end = phase != ApiPhase.Preview;
            var c = tr.Clone();
            int i = c.IndexOfTime(time);
            if (i < 0) { dragging = false; return; }
            set(c.Keys[i]);
            var r = Svc.ReplaceTrack(c, TimelineEditCommand.LabelKey, dragging || typed ? merge : null);
            if (end)
            {
                dragging = false;
                var e = dragEdit;
                dragEdit = null;
                if (e != null) ctx.ShowResult(e.Commit());
            }
            if (!r.Ok) owner.Show(r);
        }

        void NumberRow(string t)
        {
            double? min = 0, max = t == TimelineTypes.AnimeSpeed ? 3 : 1;
            var w = PluginWidgetKit.Number(Base + "v", "timeline.keyedit.value", min, max, false, "0.###", null, (v, p, typed) => Write(k => k.Number = (float)v, p, typed));
            Add(w.Element);
            shows.Add(k => w.SetValue((double)k.Number));
        }

        void IntegerRow()
        {
            var w = PluginWidgetKit.Number(Base + "v", "timeline.keyedit.value", null, null, true, "0", null, (v, p, typed) => Write(k => k.Integer = (int)Math.Round(v), p, typed));
            Add(w.Element);
            shows.Add(k => w.SetValue((double)k.Integer));
        }

        void VectorRow(string name, string labelKey, Func<TimelineKey, Vec3> get, Action<TimelineKey, Vec3> put)
        {
            var w = PluginWidgetKit.Vector(name, labelKey, 3, "0.####", (v, p, typed) => Write(k => put(k, new Vec3(v.x, v.y, v.z)), p, typed));
            Add(w.Element);
            shows.Add(k => { var v = get(k); w.SetValue(new Vector4(v.X, v.Y, v.Z, 0)); });
        }

        void ColorRow()
        {
            var w = PluginWidgetKit.Color(() => ctx, Base + "v", "timeline.keyedit.value", true, false, (c, i, p) => Write(k => k.Color = new Color4(c.r, c.g, c.b, c.a), p, false));
            Add(w.Element);
            shows.Add(k => w.SetValue(new Color(k.Color.R, k.Color.G, k.Color.B, k.Color.A)));
        }

        void AnimeRows()
        {
            void Int(string part, Func<AnimeRef, int> get, Action<AnimeRef, int> put)
            {
                var w = PluginWidgetKit.Number(Base + part, "timeline.anime." + part, null, null, true, "0", null, (v, p, typed) =>
                {
                    if (p != ApiPhase.Commit) return;
                    Write(k => { var a = k.Anime ?? new AnimeRef(); k.Anime = new AnimeRef(a.Source, a.Group, a.Category, a.No); put(k.Anime, (int)Math.Round(v)); }, ApiPhase.Commit, true);
                });
                Add(w.Element);
                shows.Add(k => w.SetValue((double)(k.Anime != null ? get(k.Anime) : 0)));
            }
            Int("group", a => a.Group, (a, v) => a.Group = v);
            Int("category", a => a.Category, (a, v) => a.Category = v);
            Int("no", a => a.No, (a, v) => a.No = v);
            var src = new Label { name = Base + "source" };
            src.AddToClassList("hs-tl__hint");
            Add(src);
            shows.Add(k => src.text = k.Anime?.Source ?? "");
        }

        void CameraRows()
        {
            TimelineCamera Cam(TimelineKey k) => k.Camera ?? new TimelineCamera();
            VectorRow(Base + "target", "timeline.cam.target", k => Cam(k).Target, (k, v) => { var c = Cam(k).Clone(); c.Target = v; k.Camera = c; });
            VectorRow(Base + "rot", "timeline.cam.rot", k => Cam(k).Rot, (k, v) => { var c = Cam(k).Clone(); c.Rot = v; k.Camera = c; });
            var dist = PluginWidgetKit.Number(Base + "dist", "timeline.cam.dist", 0, 20, false, "0.###", null, (v, p, typed) => Write(k => { var c = Cam(k).Clone(); c.Dist = (float)v; k.Camera = c; }, p, typed));
            Add(dist.Element);
            shows.Add(k => dist.SetValue((double)Cam(k).Dist));
            var fov = PluginWidgetKit.Number(Base + "fov", "timeline.cam.fov", 1, 60, false, "0.#", null, (v, p, typed) => Write(k => { var c = Cam(k).Clone(); c.Fov = (float)Math.Max(1, Math.Min(179, v)); k.Camera = c; }, p, typed));
            Add(fov.Element);
            shows.Add(k => fov.SetValue((double)Cam(k).Fov));
        }

        void DofRows()
        {
            TimelineDof Dof(TimelineKey k) => k.Dof ?? new TimelineDof();
            var on = PluginWidgetKit.Toggle(Base + "on", "timeline.dof.on", b => Write(k => { var d = Dof(k).Clone(); d.On = b; k.Dof = d; }, ApiPhase.Commit, false));
            Add(on.Element);
            shows.Add(k => on.SetValue(Dof(k).On));
            var focus = PluginWidgetKit.Ref(Base + "focus", "timeline.dof.focus", FocusChoices, FocusLabel, v => Write(k =>
            {
                var d = Dof(k).Clone();
                d.Focus = string.IsNullOrEmpty(v) || v == PivotValue ? null : TimelineFocus.OfNode(v);
                k.Dof = d;
            }, ApiPhase.Commit, false));
            Add(focus.Element);
            shows.Add(k => focus.SetValue(FocusValue(Dof(k).Focus)));
            var size = PluginWidgetKit.Number(Base + "size", "timeline.dof.size", 0, 2, false, "0.###", null, (v, p, typed) => Write(k => { var d = Dof(k).Clone(); d.Size = (float)Math.Max(0, Math.Min(2, v)); k.Dof = d; }, p, typed));
            Add(size.Element);
            shows.Add(k => size.SetValue((double)Dof(k).Size));
            var ap = PluginWidgetKit.Number(Base + "aperture", "timeline.dof.aperture", 0, 0.99, false, "0.###", null, (v, p, typed) => Write(k => { var d = Dof(k).Clone(); d.Aperture = (float)Math.Max(0, Math.Min(0.99, v)); k.Dof = d; }, p, typed));
            Add(ap.Element);
            shows.Add(k => ap.SetValue((double)Dof(k).Aperture));
            var blur = PluginWidgetKit.Number(Base + "blur", "timeline.dof.blur", 0, 10, false, "0.##", null, (v, p, typed) => Write(k => { var d = Dof(k).Clone(); d.Blur = v > 0 ? (float?)(float)v : null; k.Dof = d; }, p, typed));
            Add(blur.Element);
            shows.Add(k => blur.SetValue((double)(Dof(k).Blur ?? 0f)));
        }

        const string PivotValue = "pivot";

        static string FocusValue(TimelineFocus f)
        {
            if (f == null) return PivotValue;
            switch (f.Kind)
            {
                case TimelineFocusKind.Node: return f.Node;
                case TimelineFocusKind.Point: return "point:" + f.Point.X.ToString("0.###", CultureInfo.InvariantCulture) + ", " + f.Point.Y.ToString("0.###", CultureInfo.InvariantCulture) + ", " + f.Point.Z.ToString("0.###", CultureInfo.InvariantCulture);
                default: return "dist:" + f.Dist.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }

        IReadOnlyList<(string value, string label)> FocusChoices()
        {
            var l = new List<(string, string)> { (PivotValue, L10n.Get("timeline.dof.pivot")) };
            var model = ctx.Model;
            if (model != null)
                foreach (var n in model.PreOrder())
                    if (n.Kind == NodeKind.Chara || n.Kind == NodeKind.Item) l.Add((n.Id, n.Label));
            return l;
        }

        string FocusLabel(string v)
        {
            if (v == null || v == PivotValue) return L10n.Get("timeline.dof.pivot");
            if (v.StartsWith("point:", StringComparison.Ordinal)) return L10n.Format("timeline.dof.point", v.Substring(6));
            if (v.StartsWith("dist:", StringComparison.Ordinal)) return L10n.Format("timeline.dof.dist", v.Substring(5));
            return ctx.Model?.Find(v)?.Label ?? v;
        }
    }
}
