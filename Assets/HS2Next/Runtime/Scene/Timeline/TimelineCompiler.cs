// The stored Timeline (the scene's document) turned into a timeline ready for playback: times widened, eases parsed (an
// unknown name evaluates as inout), Euler angles turned into quaternions once, focus forms and animation rows copied. A
// track of an unknown type is kept and inactive; a key whose value does not fit its type is left out; keys out of order
// are taken in time order (stable) and a second key at one time is left out (the first stays), as the reader repairs a
// file. Events that may not run (disabled, an invalid request) are not scheduled.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Timeline
{
    public static class TimelineCompiler
    {
        /// <summary>The playable form of <paramref name="doc"/> (null for null: the scene has no Timeline).</summary>
        public static CompiledTimeline Compile(TimelineDocument doc)
        {
            if (doc == null) return null;
            var tracks = new List<CompiledTrack>(doc.Tracks?.Count ?? 0);
            if (doc.Tracks != null) foreach (var t in doc.Tracks) tracks.Add(CompileTrack(t));
            var events = new List<TimelineEventInput>(doc.Events?.Count ?? 0);
            if (doc.Events != null)
                foreach (var e in doc.Events)
                    events.Add(e == null ? null : new TimelineEventInput { Id = e.Id, T = e.T, Enabled = e.Enabled && e.Problem() == null, Do = e.Do });
            double duration = doc.Duration > 0 && !float.IsInfinity(doc.Duration) ? doc.Duration : TimelineLimits.RepairDuration;
            return new CompiledTimeline(duration, doc.Loop, doc.Autoplay, tracks, events);
        }

        /// <summary>The playable form of one stored track.</summary>
        public static CompiledTrack CompileTrack(TimelineTrack t)
        {
            if (t == null) return CompiledTrack.Unknown(null, null, null, null, null, false, null);
            var type = t.IsKnownType ? TimelineTrackTypes.FromStored(t.Type, t.Bone) : TimelineTrackType.Unknown;
            if (type == TimelineTrackType.Unknown) return CompiledTrack.Unknown(t.Id, t.Type, t.Key, t.Bone, t.Slot, t.Enabled, t.Label);
            var keys = UsableKeys(t);
            int n = keys.Count;
            var times = new float[n];
            var eases = new TimelineEase[n];
            for (int i = 0; i < n; i++)
            {
                times[i] = keys[i].T;
                eases[i] = TimelineEaseCurves.Parse(keys[i].Ease);
            }
            switch (type)
            {
                case TimelineTrackType.AnimeSpeed:
                case TimelineTrackType.AnimeTime:
                case TimelineTrackType.EyesOpen:
                case TimelineTrackType.MouthOpen:
                    {
                        var v = new double[n];
                        for (int i = 0; i < n; i++) v[i] = keys[i].Number;
                        return CompiledTrack.Numbers(t.Id, type, t.Key, t.Enabled, t.Label, times, eases, v);
                    }
                case TimelineTrackType.Eyes:
                case TimelineTrackType.Mouth:
                    {
                        var v = new int[n];
                        for (int i = 0; i < n; i++) v[i] = keys[i].Integer;
                        return CompiledTrack.Integers(t.Id, type, t.Key, t.Enabled, t.Label, times, eases, v);
                    }
                case TimelineTrackType.Pos:
                case TimelineTrackType.Scale:
                    {
                        var v = new TimelineVec3[n];
                        for (int i = 0; i < n; i++) v[i] = Of(keys[i].Vector);
                        return CompiledTrack.Vectors(t.Id, type, t.Key, t.Enabled, t.Label, times, eases, v);
                    }
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone:
                    {
                        var v = new TimelineVec3[n];
                        for (int i = 0; i < n; i++) v[i] = Of(keys[i].Vector);
                        return CompiledTrack.Rotations(t.Id, t.Key, t.Bone, t.Enabled, t.Label, times, eases, v);
                    }
                case TimelineTrackType.Color:
                    {
                        var v = new TimelineColor[n];
                        for (int i = 0; i < n; i++)
                        {
                            var c = keys[i].Color;
                            v[i] = new TimelineColor(c.R, c.G, c.B, c.A);
                        }
                        return CompiledTrack.Colors(t.Id, t.Key, t.Slot, t.Enabled, t.Label, times, eases, v);
                    }
                case TimelineTrackType.Camera:
                    {
                        var targets = new TimelineVec3[n];
                        var rots = new TimelineVec3[n];
                        var dists = new double[n];
                        var fovs = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            var c = keys[i].Camera;
                            targets[i] = Of(c.Target);
                            rots[i] = Of(c.Rot);
                            dists[i] = c.Dist;
                            fovs[i] = c.Fov;
                        }
                        return CompiledTrack.Cameras(t.Id, t.Enabled, t.Label, times, eases, targets, rots, dists, fovs);
                    }
                case TimelineTrackType.Dof:
                    {
                        var v = new TimelineDofKey[n];
                        for (int i = 0; i < n; i++)
                        {
                            var d = keys[i].Dof;
                            v[i] = new TimelineDofKey(d.On, FocusOf(d.Focus), d.Size, d.Aperture, d.Blur.HasValue ? d.Blur.Value : double.NaN);
                        }
                        return CompiledTrack.Dofs(t.Id, t.Enabled, t.Label, times, eases, v);
                    }
                case TimelineTrackType.Anime:
                    {
                        var v = new TimelineAnimeRow[n];
                        for (int i = 0; i < n; i++)
                        {
                            var a = keys[i].Anime;
                            v[i] = new TimelineAnimeRow(a.Source, a.Group, a.Category, a.No);
                        }
                        return CompiledTrack.Animes(t.Id, t.Key, t.Enabled, t.Label, times, eases, v);
                    }
                default:
                    return CompiledTrack.Unknown(t.Id, t.Type, t.Key, t.Bone, t.Slot, t.Enabled, t.Label);
            }
        }

        /// <summary>The keys that can be evaluated, in time order (stable), the first of equal times only.</summary>
        static List<TimelineKey> UsableKeys(TimelineTrack t)
        {
            var shape = t.Shape;
            var list = new List<(TimelineKey k, int i)>(t.Keys?.Count ?? 0);
            if (t.Keys != null)
                for (int i = 0; i < t.Keys.Count; i++)
                {
                    var k = t.Keys[i];
                    if (k == null || float.IsNaN(k.T) || float.IsInfinity(k.T) || k.ValueProblem(shape) != null) continue;
                    list.Add((k, i));
                }
            list.Sort((a, b) => a.k.T != b.k.T ? a.k.T.CompareTo(b.k.T) : a.i.CompareTo(b.i));
            var keys = new List<TimelineKey>(list.Count);
            foreach (var (k, _) in list)
                if (keys.Count == 0 || keys[keys.Count - 1].T != k.T) keys.Add(k);
            return keys;
        }

        static TimelineVec3 Of(Vec3 v) => new TimelineVec3(v.X, v.Y, v.Z);

        /// <summary>The playable focus form of a stored one (null: the camera pivot).</summary>
        public static DofFocus FocusOf(TimelineFocus f)
        {
            if (f == null) return DofFocus.Pivot;
            switch (f.Kind)
            {
                case TimelineFocusKind.Node:
                    return f.Node == null ? DofFocus.Pivot : DofFocus.OnNode(f.Node, f.Bone, f.Offset.HasValue ? Of(f.Offset.Value) : (TimelineVec3?)null);
                case TimelineFocusKind.Point: return DofFocus.AtPoint(Of(f.Point));
                case TimelineFocusKind.Dist: return DofFocus.AtDistance(f.Dist);
                default: return DofFocus.Pivot;
            }
        }
    }
}
