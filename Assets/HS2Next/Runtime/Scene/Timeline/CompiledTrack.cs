// A track ready for evaluation: its target, its channel and its keys with the values converted once (times widened to
// double, Euler angles turned into quaternions). The segment rule: the left key is the last key with t ≤ now, the right
// key the first with t > now; with both, w = ease of the left key at u = (now − t_left) / (t_right − t_left) and the value
// is the blend of the two; with only a left key (now at or after the last key) the last value holds; with only a right key
// (now before the first key) the first value applies; with no key the track shows nothing.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Timeline
{
    /// <summary>The segment of a time in a sorted list of key times.</summary>
    public static class TimelineSegments
    {
        /// <summary>The index of the last time ≤ <paramref name="now"/> in the ascending <paramref name="times"/>, −1 when none.</summary>
        public static int LeftKey(double[] times, int count, double now)
        {
            int lo = 0, hi = count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] <= now)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            return found;
        }

        /// <summary>
        /// The keys of <paramref name="now"/>: false when there is no key. Otherwise <paramref name="left"/> is the key whose
        /// value holds (the first key before it, the last after it) and, between two keys, <paramref name="right"/> = left + 1
        /// with <paramref name="u"/> the fraction of the segment; else <paramref name="right"/> = −1 and u = 0.
        /// </summary>
        public static bool Find(double[] times, int count, double now, out int left, out int right, out double u)
        {
            right = -1;
            u = 0;
            left = -1;
            if (count <= 0) return false;
            int l = LeftKey(times, count, now);
            if (l < 0)
            {
                left = 0;
                return true;
            }
            left = l;
            if (l >= count - 1) return true;
            right = l + 1;
            u = (now - times[l]) / (times[right] - times[l]);
            return true;
        }
    }

    /// <summary>Why a track drives nothing.</summary>
    public enum TimelineTrackState
    {
        /// <summary>The track drives its channel (when its target exists).</summary>
        Active = 0,
        Disabled,
        /// <summary>A type this version does not know (kept as stored).</summary>
        UnknownType,
        /// <summary>A rotation of a bone without a bone, a colour slot outside 0–3, a node type without a node.</summary>
        BadTarget,
        /// <summary>A second track of a channel: the first in list order drives it.</summary>
        DuplicateChannel,
    }

    /// <summary>A track ready for evaluation (immutable once built).</summary>
    public sealed class CompiledTrack
    {
        readonly double[] times;
        readonly TimelineEase[] eases;
        readonly double[] numbers;
        readonly int[] integers;
        readonly TimelineVec3[] vectors;
        readonly TimelineQuat[] rotations;
        readonly TimelineColor[] colors;
        readonly TimelineCameraValue[] cameras;
        readonly TimelineDofKey[] dofs;
        readonly TimelineAnimeRow[] animes;

        CompiledTrack(string id, string typeName, TimelineTrackType type, string nodeId, string bone, int? slot, bool enabled, string label,
            double[] times, TimelineEase[] eases, double[] numbers = null, int[] integers = null, TimelineVec3[] vectors = null, TimelineQuat[] rotations = null,
            TimelineColor[] colors = null, TimelineCameraValue[] cameras = null, TimelineDofKey[] dofs = null, TimelineAnimeRow[] animes = null)
        {
            Id = id;
            TypeName = typeName ?? TimelineTrackTypes.StoredName(type);
            Type = type;
            NodeId = TimelineTrackTypes.IsSceneWide(type) ? null : nodeId;
            Bone = bone;
            Slot = slot;
            Enabled = enabled;
            Label = label;
            this.times = times ?? new double[0];
            this.eases = eases ?? new TimelineEase[this.times.Length];
            if (this.eases.Length != this.times.Length) throw new ArgumentException("one ease per key");
            this.numbers = numbers;
            this.integers = integers;
            this.vectors = vectors;
            this.rotations = rotations;
            this.colors = colors;
            this.cameras = cameras;
            this.dofs = dofs;
            this.animes = animes;
            ChannelPath = TimelineTrackTypes.ChannelPath(type, bone, slot);
            ModelPath = TimelineTrackTypes.ModelPath(type, bone, slot);
            for (int i = 1; i < this.times.Length; i++)
                if (!(this.times[i] >= this.times[i - 1])) throw new ArgumentException("key times must ascend");
            if (type == TimelineTrackType.Unknown) State = TimelineTrackState.UnknownType;
            else if (ChannelPath == null || (!TimelineTrackTypes.IsSceneWide(type) && nodeId == null)) State = TimelineTrackState.BadTarget;
            else if (!enabled) State = TimelineTrackState.Disabled;
        }

        /// <summary>The track's index in its timeline (set when the timeline is built).</summary>
        public int Index { get; internal set; }
        public string Id { get; }
        /// <summary>The stored type name (also for an unknown type).</summary>
        public string TypeName { get; }
        public TimelineTrackType Type { get; }
        /// <summary>The node (null for the scene-wide types).</summary>
        public string NodeId { get; }
        public string Bone { get; }
        public int? Slot { get; }
        public bool Enabled { get; }
        public string Label { get; }
        /// <summary>The channel path (null for an unknown type or a bad target).</summary>
        public string ChannelPath { get; }
        /// <summary>The model path whose edits suspend the channel while paused.</summary>
        public string ModelPath { get; }
        public TimelineTrackState State { get; internal set; }
        /// <summary>True when the track drives its channel (when its target exists).</summary>
        public bool Active => State == TimelineTrackState.Active;
        /// <summary>The channel object (set by the player that plays the timeline; null for an inactive track).</summary>
        public TimelineChannel Channel { get; internal set; }

        /// <summary>True when the track's type may drive a node of kind <paramref name="k"/> (its slot decides item or light for a colour).</summary>
        public bool Takes(NodeKind k) => TimelineTrackTypes.Takes(Type, k, Slot.HasValue);

        public int KeyCount => times.Length;
        public double KeyTime(int i) => times[i];
        /// <summary>The key's stored ease (the step-only types evaluate as step whatever it says).</summary>
        public TimelineEase KeyEase(int i) => eases[i];

        // ---------- factories (keys in ascending time; one ease and one value per key) ----------

        static double[] Widen(float[] t)
        {
            var d = new double[t.Length];
            for (int i = 0; i < t.Length; i++) d[i] = t[i];
            return d;
        }

        /// <summary>A track of a type this version does not know: kept, never evaluated.</summary>
        public static CompiledTrack Unknown(string id, string typeName, string nodeId, string bone, int? slot, bool enabled, string label) =>
            new CompiledTrack(id, typeName, TimelineTrackType.Unknown, nodeId, bone, slot, enabled, label, null, null);

        /// <summary><c>animeSpeed</c>, <c>animeTime</c>, <c>eyesOpen</c>, <c>mouthOpen</c>.</summary>
        public static CompiledTrack Numbers(string id, TimelineTrackType type, string nodeId, bool enabled, string label, float[] times, TimelineEase[] eases, double[] values)
        {
            Need(type == TimelineTrackType.AnimeSpeed || type == TimelineTrackType.AnimeTime || type == TimelineTrackType.EyesOpen || type == TimelineTrackType.MouthOpen, type);
            Same(times, values?.Length);
            return new CompiledTrack(id, null, type, nodeId, null, null, enabled, label, Widen(times), eases, numbers: values);
        }

        /// <summary><c>eyes</c>, <c>mouth</c> (pattern ids; step).</summary>
        public static CompiledTrack Integers(string id, TimelineTrackType type, string nodeId, bool enabled, string label, float[] times, TimelineEase[] eases, int[] values)
        {
            Need(type == TimelineTrackType.Eyes || type == TimelineTrackType.Mouth, type);
            Same(times, values?.Length);
            return new CompiledTrack(id, null, type, nodeId, null, null, enabled, label, Widen(times), eases, integers: values);
        }

        /// <summary><c>pos</c>, <c>scale</c>.</summary>
        public static CompiledTrack Vectors(string id, TimelineTrackType type, string nodeId, bool enabled, string label, float[] times, TimelineEase[] eases, TimelineVec3[] values)
        {
            Need(type == TimelineTrackType.Pos || type == TimelineTrackType.Scale, type);
            Same(times, values?.Length);
            return new CompiledTrack(id, null, type, nodeId, null, null, enabled, label, Widen(times), eases, vectors: values);
        }

        /// <summary><c>rot</c> of a node (<paramref name="bone"/> null) or of a character's FK bone; values as Euler degrees.</summary>
        public static CompiledTrack Rotations(string id, string nodeId, string bone, bool enabled, string label, float[] times, TimelineEase[] eases, TimelineVec3[] eulers)
        {
            Same(times, eulers?.Length);
            var q = new TimelineQuat[eulers.Length];
            for (int i = 0; i < q.Length; i++) q[i] = TimelineQuat.FromEuler(eulers[i]);
            return new CompiledTrack(id, null, bone != null ? TimelineTrackType.Bone : TimelineTrackType.Rot, nodeId, bone, null, enabled, label, Widen(times), eases, rotations: q);
        }

        /// <summary><c>color</c> of an item's slot (<paramref name="slot"/> 0–3) or of a light (<paramref name="slot"/> null).</summary>
        public static CompiledTrack Colors(string id, string nodeId, int? slot, bool enabled, string label, float[] times, TimelineEase[] eases, TimelineColor[] values)
        {
            Same(times, values?.Length);
            return new CompiledTrack(id, null, TimelineTrackType.Color, nodeId, null, slot, enabled, label, Widen(times), eases, colors: values);
        }

        /// <summary><c>camera</c>; each value's rotation from Euler degrees.</summary>
        public static CompiledTrack Cameras(string id, bool enabled, string label, float[] times, TimelineEase[] eases, TimelineVec3[] targets, TimelineVec3[] eulers, double[] dists, double[] fovs)
        {
            Same(times, targets?.Length);
            Same(times, eulers?.Length);
            Same(times, dists?.Length);
            Same(times, fovs?.Length);
            var v = new TimelineCameraValue[times.Length];
            for (int i = 0; i < v.Length; i++) v[i] = new TimelineCameraValue(targets[i], TimelineQuat.FromEuler(eulers[i]), dists[i], fovs[i]);
            return new CompiledTrack(id, null, TimelineTrackType.Camera, null, null, null, enabled, label, Widen(times), eases, cameras: v);
        }

        /// <summary><c>dof</c>.</summary>
        public static CompiledTrack Dofs(string id, bool enabled, string label, float[] times, TimelineEase[] eases, TimelineDofKey[] values)
        {
            Same(times, values?.Length);
            return new CompiledTrack(id, null, TimelineTrackType.Dof, null, null, null, enabled, label, Widen(times), eases, dofs: values);
        }

        /// <summary><c>anime</c> (rows; step).</summary>
        public static CompiledTrack Animes(string id, string nodeId, bool enabled, string label, float[] times, TimelineEase[] eases, TimelineAnimeRow[] values)
        {
            Same(times, values?.Length);
            return new CompiledTrack(id, null, TimelineTrackType.Anime, nodeId, null, null, enabled, label, Widen(times), eases, animes: values);
        }

        static void Need(bool ok, TimelineTrackType t)
        {
            if (!ok) throw new ArgumentException("this factory does not make " + t + " tracks");
        }

        static void Same(float[] times, int? n)
        {
            if (times == null) throw new ArgumentNullException(nameof(times));
            if (n == null || n.Value != times.Length) throw new ArgumentException("one value per key");
        }

        // ---------- evaluation ----------

        /// <summary>
        /// The segment of <paramref name="now"/>: false without keys; else the left key (whose value holds) and, between two
        /// keys, the right key (else −1) and the weight w of the left key's ease (0 for the step-only types).
        /// </summary>
        public bool Segment(double now, out int left, out int right, out double w)
        {
            w = 0;
            if (!TimelineSegments.Find(times, times.Length, now, out left, out right, out double u)) return false;
            if (right >= 0) w = TimelineTrackTypes.StepsOnly(Type) ? 0 : TimelineEaseCurves.Weight(eases[left], u);
            return true;
        }

        /// <summary>
        /// Evaluates the track at <paramref name="now"/> and hands the value to <paramref name="sink"/> for
        /// <paramref name="channel"/>; false (nothing handed) when the track has no key or no channel kind to give.
        /// </summary>
        public bool Evaluate(double now, ITimelineSink sink, TimelineChannel channel)
        {
            if (!Segment(now, out int l, out int r, out double w)) return false;
            // At w = 0 (a step segment, a key's own time) the left key's value is handed as stored, never blended.
            bool blend = r >= 0 && w != 0;
            switch (Type)
            {
                case TimelineTrackType.AnimeSpeed:
                case TimelineTrackType.AnimeTime:
                case TimelineTrackType.EyesOpen:
                case TimelineTrackType.MouthOpen:
                    sink.SetNumber(channel, blend ? numbers[l] + (numbers[r] - numbers[l]) * w : numbers[l]);
                    return true;
                case TimelineTrackType.Eyes:
                case TimelineTrackType.Mouth:
                    sink.SetInteger(channel, integers[l]);
                    return true;
                case TimelineTrackType.Pos:
                case TimelineTrackType.Scale:
                    if (blend) sink.SetVector(channel, TimelineVec3.Lerp(vectors[l], vectors[r], w));
                    else sink.SetVector(channel, vectors[l]);
                    return true;
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone:
                    if (blend) sink.SetRotation(channel, TimelineQuat.Interpolate(rotations[l], rotations[r], w));
                    else sink.SetRotation(channel, rotations[l]);
                    return true;
                case TimelineTrackType.Color:
                    if (blend) sink.SetColor(channel, TimelineColor.Lerp(colors[l], colors[r], w));
                    else sink.SetColor(channel, colors[l]);
                    return true;
                case TimelineTrackType.Camera:
                    if (blend) sink.SetCamera(channel, TimelineCameraValue.Blend(cameras[l], cameras[r], w));
                    else sink.SetCamera(channel, cameras[l]);
                    return true;
                case TimelineTrackType.Dof:
                    // The on / off fade rule applies at every weight between two keys (also 0).
                    if (r >= 0) sink.SetDepthOfField(channel, TimelineDofValue.Blend(dofs[l], dofs[r], w));
                    else sink.SetDepthOfField(channel, TimelineDofValue.Of(dofs[l]));
                    return true;
                case TimelineTrackType.Anime:
                    sink.SetAnimation(channel, animes[l]);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the two tracks hold the same keys (times, eases and values).</summary>
        public bool SameKeys(CompiledTrack o)
        {
            if (o == null || o.Type != Type || o.times.Length != times.Length) return false;
            for (int i = 0; i < times.Length; i++)
            {
                if (!times[i].Equals(o.times[i]) || eases[i] != o.eases[i]) return false;
                if (!SameValue(o, i)) return false;
            }
            return true;
        }

        bool SameValue(CompiledTrack o, int i)
        {
            switch (Type)
            {
                case TimelineTrackType.AnimeSpeed:
                case TimelineTrackType.AnimeTime:
                case TimelineTrackType.EyesOpen:
                case TimelineTrackType.MouthOpen: return numbers[i].Equals(o.numbers[i]);
                case TimelineTrackType.Eyes:
                case TimelineTrackType.Mouth: return integers[i] == o.integers[i];
                case TimelineTrackType.Pos:
                case TimelineTrackType.Scale: return vectors[i].Equals(o.vectors[i]);
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone: return rotations[i].Equals(o.rotations[i]);
                case TimelineTrackType.Color: return colors[i].Equals(o.colors[i]);
                case TimelineTrackType.Camera:
                    {
                        var a = cameras[i];
                        var b = o.cameras[i];
                        return a.Target.Equals(b.Target) && a.Rotation.Equals(b.Rotation) && a.Dist.Equals(b.Dist) && a.Fov.Equals(b.Fov);
                    }
                case TimelineTrackType.Dof:
                    {
                        var a = dofs[i];
                        var b = o.dofs[i];
                        return a.On == b.On && a.Focus.Equals(b.Focus) && a.Size.Equals(b.Size) && a.Aperture.Equals(b.Aperture) && a.Blur.Equals(b.Blur);
                    }
                case TimelineTrackType.Anime: return animes[i].Equals(o.animes[i]);
                default: return true;
            }
        }

        public override string ToString() => TypeName + (NodeId != null ? " " + NodeId : "") + (Bone != null ? " " + Bone : "") + (Slot != null ? " #" + Slot : "") + " (" + KeyCount + " keys)";
    }
}
