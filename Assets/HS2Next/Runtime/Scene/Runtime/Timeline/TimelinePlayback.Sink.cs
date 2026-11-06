// The Timeline playback's sink: the evaluator's values become override slots of the scene's layer (owner "timeline", priority
// 0), one per channel, acquired when a channel first shows a value and kept (with the last value set, for the bake) in the
// channel's Tag until the channel is released. The layer's rules for the camera controller and gizmos see the Timeline as
// driving only while it plays; the layer's model-change hook suspends a channel while the Timeline is paused or ended.
using System;
using System.Collections.Generic;
using HS2Next.Api.Plugins;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Timeline;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class TimelinePlayback
    {
        /// <summary>A channel's slot and the last value the Timeline set on it.</summary>
        internal sealed class Held
        {
            public OverrideSlot Slot;
            /// <summary>The slot holds the value below (set, not cleared since).</summary>
            public bool Has;
            /// <summary>Number / integer (X), vector (X, Y, Z), rotation as a quaternion and colour (X, Y, Z, W).</summary>
            public double X, Y, Z, W;
            public TimelineCameraValue Camera;
            public TimelineDofValue Dof;
            public TimelineAnimeRow Anime;
        }

        readonly List<TimelineChannel> bound = new List<TimelineChannel>();
        OverrideLayer layerHooked;
        Func<string, bool> previousDriving;

        OverrideLayer Layer => rt?.Overrides;

        void AttachSink()
        {
            var l = Layer;
            if (l == null) return;
            layerHooked = l;
            l.SetOwnerName(Owner, "Timeline");
            previousDriving = l.IsDriving;
            l.IsDriving = Driving;
            l.ModelChanged += OnSlotModelChanged;
        }

        void DetachSink()
        {
            ReleaseAll();
            var l = layerHooked;
            if (l == null) return;
            l.ModelChanged -= OnSlotModelChanged;
            if (l.IsDriving == (Func<string, bool>)Driving) l.IsDriving = previousDriving;
            layerHooked = null;
        }

        /// <summary>The layer's driving rule: the Timeline's slots drive only while it plays; other owners as before.</summary>
        bool Driving(string owner) => owner == Owner ? player.State == TimelinePlayState.Playing : previousDriving == null || previousDriving(owner);

        /// <summary>A model edit of a channel one of the Timeline's slots stands on: suspended while paused or ended.</summary>
        void OnSlotModelChanged(OverrideSlot s)
        {
            if (s == null || s.Owner != Owner) return;
            foreach (var ch in bound)
                if (ch.Tag is Held h && ReferenceEquals(h.Slot, s))
                {
                    player.Suspend(ch);
                    return;
                }
        }

        // ------------------------------------------------------------ ITimelineSink

        public NodeKind? KindOf(string nodeId) => rt?.Model.Find(nodeId)?.Kind;

        Held Hold(TimelineChannel ch)
        {
            var h = ch.Tag as Held;
            if (h != null && h.Slot != null && !h.Slot.IsReleased) return h;
            var l = Layer;
            if (l == null) return null;
            OverrideSlot s;
            try { s = l.Acquire(Owner, ch.NodeId, ch.Path, 0); }
            catch (Exception e)
            {
                Fault("Acquire " + ch.Path, e);
                return null;
            }
            if (s == null) return null;
            if (h == null)
            {
                h = new Held();
                ch.Tag = h;
                bound.Add(ch);
            }
            h.Slot = s;
            h.Has = false;
            return h;
        }

        bool SetFailed(TimelineChannel ch, Exception e)
        {
            Fault("Set " + ch.Path, e);
            return false;
        }

        public void SetNumber(TimelineChannel ch, double value)
        {
            var h = Hold(ch);
            if (h == null) return;
            try { h.Slot.SetNumber(value); } catch (Exception e) { SetFailed(ch, e); return; }
            h.X = value;
            h.Has = true;
        }

        public void SetInteger(TimelineChannel ch, int value)
        {
            var h = Hold(ch);
            if (h == null) return;
            try { h.Slot.SetNumber(value); } catch (Exception e) { SetFailed(ch, e); return; }
            h.X = value;
            h.Has = true;
        }

        public void SetVector(TimelineChannel ch, in TimelineVec3 v)
        {
            var h = Hold(ch);
            if (h == null) return;
            try { h.Slot.SetVector(v.X, v.Y, v.Z); } catch (Exception e) { SetFailed(ch, e); return; }
            h.X = v.X;
            h.Y = v.Y;
            h.Z = v.Z;
            h.Has = true;
        }

        public void SetRotation(TimelineChannel ch, in TimelineQuat q)
        {
            var h = Hold(ch);
            if (h == null) return;
            try { h.Slot.SetQuaternion(q.X, q.Y, q.Z, q.W); } catch (Exception e) { SetFailed(ch, e); return; }
            h.X = q.X;
            h.Y = q.Y;
            h.Z = q.Z;
            h.W = q.W;
            h.Has = true;
        }

        public void SetColor(TimelineChannel ch, in TimelineColor c)
        {
            var h = Hold(ch);
            if (h == null) return;
            try { h.Slot.SetColor(c.R, c.G, c.B, c.A); } catch (Exception e) { SetFailed(ch, e); return; }
            h.X = c.R;
            h.Y = c.G;
            h.Z = c.B;
            h.W = c.A;
            h.Has = true;
        }

        public void SetCamera(TimelineChannel ch, in TimelineCameraValue v)
        {
            var h = Hold(ch);
            if (h == null) return;
            var q = v.Rotation;
            try { h.Slot.SetCamera(v.Target.X, v.Target.Y, v.Target.Z, q.X, q.Y, q.Z, q.W, v.Dist, v.Fov); } catch (Exception e) { SetFailed(ch, e); return; }
            h.Camera = v;
            h.Has = true;
        }

        public void SetDepthOfField(TimelineChannel ch, in TimelineDofValue v)
        {
            var h = Hold(ch);
            if (h == null) return;
            var d = new OverrideDof(v.Enabled, v.FocalSize, v.Aperture, v.HasBlur ? v.Blur : 0, FocusOf(v.Focus), v.FocusTo != null ? FocusOf(v.FocusTo) : null, v.FocusBlend);
            try { h.Slot.SetDof(d); } catch (Exception e) { SetFailed(ch, e); return; }
            h.Dof = v;
            h.Has = true;
        }

        public void SetAnimation(TimelineChannel ch, in TimelineAnimeRow row)
        {
            var h = Hold(ch);
            if (h == null) return;
            try { h.Slot.SetAnimation(row.Source, row.Group, row.Category, row.No); } catch (Exception e) { SetFailed(ch, e); return; }
            h.Anime = row;
            h.Has = true;
        }

        public void Clear(TimelineChannel ch)
        {
            if (!(ch.Tag is Held h)) return;
            try { h.Slot?.Clear(); } catch (Exception e) { Fault("Clear " + ch.Path, e); }
            h.Has = false;
        }

        public void Release(TimelineChannel ch)
        {
            if (ch.Tag is Held h)
            {
                try { h.Slot?.Release(); } catch (Exception e) { Fault("Release " + ch.Path, e); }
                h.Has = false;
            }
            ch.Tag = null;
            bound.Remove(ch);
        }

        public void ReleaseAll()
        {
            try { Layer?.ReleaseOwner(Owner); } catch (Exception e) { Fault("ReleaseAll", e); }
            foreach (var ch in bound)
            {
                if (ch.Tag is Held h) h.Has = false;
                ch.Tag = null;
            }
            bound.Clear();
        }

        static OverrideFocus FocusOf(DofFocus f)
        {
            if (f == null) return OverrideFocus.Pivot;
            switch (f.Kind)
            {
                case DofFocusKind.Node:
                    if (f.Bone == null && !f.Offset.HasValue) return OverrideFocus.OfNode(f.NodeId);
                    var o = f.Offset ?? TimelineVec3.Zero;
                    return OverrideFocus.OfBone(f.NodeId, f.Bone, o.X, o.Y, o.Z);
                case DofFocusKind.Point: return OverrideFocus.OfPoint(f.Point.X, f.Point.Y, f.Point.Z);
                case DofFocusKind.Dist: return OverrideFocus.OfDist(f.Dist);
                default: return OverrideFocus.Pivot;
            }
        }

        static DofFocus FocusOf(OverrideFocus f)
        {
            if (f == null) return DofFocus.Pivot;
            switch (f.Kind)
            {
                case OverrideFocusKind.Node: return DofFocus.OnNode(f.Node);
                case OverrideFocusKind.NodeBone: return DofFocus.OnNode(f.Node, f.Bone, new TimelineVec3(f.X, f.Y, f.Z));
                case OverrideFocusKind.Point: return DofFocus.AtPoint(new TimelineVec3(f.X, f.Y, f.Z));
                case OverrideFocusKind.Dist: return DofFocus.AtDistance(f.Dist);
                default: return DofFocus.Pivot;
            }
        }

        /// <summary>The winning slot's value of (node, path) as a key of <paramref name="type"/>, when a slot with a value wins there.</summary>
        bool TryWinnerKey(string nodeId, string path, TimelineTrackType type, out TimelineKey key)
        {
            key = null;
            var l = Layer;
            if (l == null || !l.Any) return false;
            OverrideSlot s;
            try { s = l.Winner(nodeId, path); }
            catch (Exception) { return false; }
            if (s == null || !s.HasValue) return false;
            var h = new Held { Has = true };
            switch (s.Kind)
            {
                case OverrideValueKind.Number:
                case OverrideValueKind.Integer: h.X = s[0]; break;
                case OverrideValueKind.Vector3: h.X = s[0]; h.Y = s[1]; h.Z = s[2]; break;
                case OverrideValueKind.Rotation:
                case OverrideValueKind.Color: h.X = s[0]; h.Y = s[1]; h.Z = s[2]; h.W = s[3]; break;
                case OverrideValueKind.Camera:
                    h.Camera = new TimelineCameraValue(new TimelineVec3(s[0], s[1], s[2]), new TimelineQuat(s[3], s[4], s[5], s[6]), s[7], s[8]);
                    break;
                case OverrideValueKind.Animation:
                {
                    string src = s.AnimeSource;
                    int g = (int)s[0], c = (int)s[1], n = (int)s[2];
                    if (src == null) src = rt?.Services?.Db?.FindAnime(g, c, n)?.Guid ?? DocDefaults.VanillaSource;
                    h.Anime = new TimelineAnimeRow(src, g, c, n);
                    break;
                }
                case OverrideValueKind.DepthOfField:
                {
                    var d = s.Dof;
                    if (d == null) return false;
                    h.Dof = new TimelineDofValue(d.Enabled, d.FocalSize, d.Aperture, d.Blur > 0 ? d.Blur : double.NaN, FocusOf(d.Focus),
                        d.FocusTo != null ? FocusOf(d.FocusTo) : null, d.FocusBlend);
                    break;
                }
                default: return false;
            }
            key = KeyOf(type, h);
            return key != null;
        }
    }
}
