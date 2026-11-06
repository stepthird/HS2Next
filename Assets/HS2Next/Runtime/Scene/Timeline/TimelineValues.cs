// The structured values of the Timeline (camera view, depth of field with its focus forms, animation row) and the blend
// of two key values per type. Numbers, vectors and colours blend linearly and unclamped; rotations on the shorter arc
// (a normalised lerp below an arc of about 36.4°, a spherical interpolation beyond);
// the camera blends target, distance and field of view linearly and its rotation on the shorter arc; animation rows and
// face patterns do not blend. Depth of field: a side that is off counts as aperture 0 and lends the other side's focal
// size, so on / off fade; the blur blends when both keys set one; two different focus forms are both resolved at run
// time and their points blended by the weight (both targets keep moving).
using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Timeline
{
    public enum DofFocusKind
    {
        /// <summary>The pivot of the camera as shown.</summary>
        Pivot = 0,
        /// <summary>A node's point (a character: between the eyes), or a named bone of the node plus a world offset.</summary>
        Node = 1,
        /// <summary>A world position.</summary>
        Point = 2,
        /// <summary>A distance in front of the camera.</summary>
        Dist = 3,
    }

    /// <summary>A depth-of-field focus form (immutable; compared by value).</summary>
    public sealed class DofFocus : IEquatable<DofFocus>
    {
        public static readonly DofFocus Pivot = new DofFocus(DofFocusKind.Pivot, null, null, null, TimelineVec3.Zero, 0);

        public DofFocusKind Kind { get; }
        /// <summary>The node id (<see cref="DofFocusKind.Node"/>), else null.</summary>
        public string NodeId { get; }
        /// <summary>The bone's transform name under the node, or null for the node's own point.</summary>
        public string Bone { get; }
        /// <summary>The world offset added to the bone's position, or null (none).</summary>
        public TimelineVec3? Offset { get; }
        /// <summary>The world position (<see cref="DofFocusKind.Point"/>).</summary>
        public TimelineVec3 Point { get; }
        /// <summary>The distance in front of the camera (<see cref="DofFocusKind.Dist"/>).</summary>
        public double Dist { get; }

        DofFocus(DofFocusKind kind, string nodeId, string bone, TimelineVec3? offset, TimelineVec3 point, double dist)
        {
            Kind = kind;
            NodeId = nodeId;
            Bone = bone;
            Offset = offset;
            Point = point;
            Dist = dist;
        }

        public static DofFocus OnNode(string nodeId, string bone = null, TimelineVec3? offset = null)
        {
            if (nodeId == null) throw new ArgumentNullException(nameof(nodeId));
            return new DofFocus(DofFocusKind.Node, nodeId, bone, offset, TimelineVec3.Zero, 0);
        }

        public static DofFocus AtPoint(TimelineVec3 p) => new DofFocus(DofFocusKind.Point, null, null, null, p, 0);

        public static DofFocus AtDistance(double d) => new DofFocus(DofFocusKind.Dist, null, null, null, TimelineVec3.Zero, d);

        public bool Equals(DofFocus o)
        {
            if (ReferenceEquals(this, o)) return true;
            if (o == null || o.Kind != Kind) return false;
            switch (Kind)
            {
                case DofFocusKind.Node:
                    return string.Equals(NodeId, o.NodeId, StringComparison.Ordinal) && string.Equals(Bone, o.Bone, StringComparison.Ordinal) &&
                           Offset.HasValue == o.Offset.HasValue && (!Offset.HasValue || Offset.Value.Equals(o.Offset.Value));
                case DofFocusKind.Point: return Point.Equals(o.Point);
                case DofFocusKind.Dist: return Dist.Equals(o.Dist);
                default: return true;
            }
        }

        public override bool Equals(object obj) => obj is DofFocus o && Equals(o);
        public override int GetHashCode() => ((int)Kind * 397) ^ (NodeId?.GetHashCode() ?? 0) ^ Point.GetHashCode() ^ Dist.GetHashCode();

        /// <summary>
        /// The focus as JSON: <c>null</c> for the pivot, <c>{"node": id}</c> or <c>{"node": id, "bone": name, "offset": [x, y, z]}</c>
        /// (members present only when set), <c>{"point": [x, y, z]}</c>, <c>{"dist": d}</c>.
        /// </summary>
        public void WriteJson(JsonWriter w)
        {
            switch (Kind)
            {
                case DofFocusKind.Node:
                    w.WriteStartObject();
                    w.WritePropertyName("node");
                    w.WriteValue(NodeId);
                    if (Bone != null)
                    {
                        w.WritePropertyName("bone");
                        w.WriteValue(Bone);
                    }
                    if (Offset.HasValue)
                    {
                        w.WritePropertyName("offset");
                        TimelineJson.WriteVec(w, Offset.Value);
                    }
                    w.WriteEndObject();
                    break;
                case DofFocusKind.Point:
                    w.WriteStartObject();
                    w.WritePropertyName("point");
                    TimelineJson.WriteVec(w, Point);
                    w.WriteEndObject();
                    break;
                case DofFocusKind.Dist:
                    w.WriteStartObject();
                    w.WritePropertyName("dist");
                    w.WriteValue(Dist);
                    w.WriteEndObject();
                    break;
                default:
                    w.WriteNull();
                    break;
            }
        }

        public string ToJson() => TimelineJson.Text(WriteJson);

        public override string ToString() => ToJson();
    }

    /// <summary>A camera view: the pivot (target), the rotation, the distance from the pivot and the vertical field of view.</summary>
    public readonly struct TimelineCameraValue
    {
        public readonly TimelineVec3 Target;
        public readonly TimelineQuat Rotation;
        public readonly double Dist;
        public readonly double Fov;

        public TimelineCameraValue(TimelineVec3 target, TimelineQuat rotation, double dist, double fov)
        {
            Target = target;
            Rotation = rotation;
            Dist = dist;
            Fov = fov;
        }

        /// <summary>Target and distance and field of view linear, the rotation as the rotation tracks blend (<see cref="TimelineQuat.Interpolate"/>).</summary>
        public static TimelineCameraValue Blend(in TimelineCameraValue a, in TimelineCameraValue b, double w) => new TimelineCameraValue(
            TimelineVec3.Lerp(a.Target, b.Target, w), TimelineQuat.Interpolate(a.Rotation, b.Rotation, w), a.Dist + (b.Dist - a.Dist) * w, a.Fov + (b.Fov - a.Fov) * w);

        /// <summary>The view as JSON <c>{"target": [x, y, z], "rot": [x, y, z], "dist": d, "fov": f}</c> (rot as Euler degrees in [0, 360)).</summary>
        public void WriteJson(JsonWriter w)
        {
            w.WriteStartObject();
            w.WritePropertyName("target");
            TimelineJson.WriteVec(w, Target);
            w.WritePropertyName("rot");
            TimelineJson.WriteVec(w, Rotation.ToEuler());
            w.WritePropertyName("dist");
            w.WriteValue(Dist);
            w.WritePropertyName("fov");
            w.WriteValue(Fov);
            w.WriteEndObject();
        }

        public string ToJson() => TimelineJson.Text(WriteJson);
    }

    /// <summary>A depth-of-field key as stored (blur NaN = none: the renderer's).</summary>
    public readonly struct TimelineDofKey
    {
        public readonly bool On;
        public readonly DofFocus Focus;
        public readonly double Size;
        public readonly double Aperture;
        public readonly double Blur;

        public TimelineDofKey(bool on, DofFocus focus, double size, double aperture, double blur)
        {
            On = on;
            Focus = focus ?? DofFocus.Pivot;
            Size = size;
            Aperture = aperture;
            Blur = blur;
        }

        public bool HasBlur => !double.IsNaN(Blur);
    }

    /// <summary>
    /// The depth of field a track shows: on / off, focal size, aperture, the largest blur (NaN: the renderer's), the focus
    /// form and, while two different forms blend, the second form and the weight (<see cref="FocusTo"/> null: no blend).
    /// </summary>
    public readonly struct TimelineDofValue
    {
        public readonly bool Enabled;
        public readonly double FocalSize;
        public readonly double Aperture;
        public readonly double Blur;
        public readonly DofFocus Focus;
        public readonly DofFocus FocusTo;
        public readonly double FocusBlend;

        public TimelineDofValue(bool enabled, double focalSize, double aperture, double blur, DofFocus focus, DofFocus focusTo, double focusBlend)
        {
            Enabled = enabled;
            FocalSize = focalSize;
            Aperture = aperture;
            Blur = blur;
            Focus = focus ?? DofFocus.Pivot;
            FocusTo = focusTo;
            FocusBlend = focusTo == null ? 0 : focusBlend;
        }

        public bool HasBlur => !double.IsNaN(Blur);

        /// <summary>The value of one key as it stands.</summary>
        public static TimelineDofValue Of(in TimelineDofKey k) => new TimelineDofValue(k.On, k.Size, k.Aperture, k.Blur, k.Focus, null, 0);

        /// <summary>
        /// The blend of two keys at weight <paramref name="w"/>: apA = a.on ? a.aperture : 0, apB likewise; a side that is
        /// off takes the other side's focal size; enabled = a.on at w ≤ 0, b.on at w ≥ 1, else a.on or b.on; aperture and
        /// size linear; blur linear when both keys set one, else the one set (none when neither); equal focus forms stay,
        /// different ones give focus = a's, focusTo = b's, focusBlend = w.
        /// </summary>
        public static TimelineDofValue Blend(in TimelineDofKey a, in TimelineDofKey b, double w)
        {
            double apA = a.On ? a.Aperture : 0, apB = b.On ? b.Aperture : 0;
            double sizeA = a.On || !b.On ? a.Size : b.Size;
            double sizeB = b.On || !a.On ? b.Size : a.Size;
            bool enabled = w <= 0 ? a.On : w >= 1 ? b.On : a.On || b.On;
            double blur = a.HasBlur && b.HasBlur ? a.Blur + (b.Blur - a.Blur) * w : a.HasBlur ? a.Blur : b.Blur;
            bool sameFocus = a.Focus.Equals(b.Focus);
            return new TimelineDofValue(enabled, sizeA + (sizeB - sizeA) * w, apA + (apB - apA) * w, blur,
                a.Focus, sameFocus ? null : b.Focus, sameFocus ? 0 : w);
        }

        /// <summary>
        /// The value as JSON <c>{"enabled", "focus", "focalSize", "aperture", "blur"?, "focusTo"?, "focusBlend"?}</c> (blur only
        /// when set, the blend members only while two forms blend).
        /// </summary>
        public void WriteJson(JsonWriter w)
        {
            w.WriteStartObject();
            w.WritePropertyName("enabled");
            w.WriteValue(Enabled);
            w.WritePropertyName("focus");
            Focus.WriteJson(w);
            w.WritePropertyName("focalSize");
            w.WriteValue(FocalSize);
            w.WritePropertyName("aperture");
            w.WriteValue(Aperture);
            if (HasBlur)
            {
                w.WritePropertyName("blur");
                w.WriteValue(Blur);
            }
            if (FocusTo != null)
            {
                w.WritePropertyName("focusTo");
                FocusTo.WriteJson(w);
                w.WritePropertyName("focusBlend");
                w.WriteValue(FocusBlend);
            }
            w.WriteEndObject();
        }

        public string ToJson() => TimelineJson.Text(WriteJson);
    }

    /// <summary>An animation row (its content source, group, category and number).</summary>
    public readonly struct TimelineAnimeRow : IEquatable<TimelineAnimeRow>
    {
        public readonly string Source;
        public readonly int Group, Category, No;

        public TimelineAnimeRow(string source, int group, int category, int no)
        {
            Source = source;
            Group = group;
            Category = category;
            No = no;
        }

        public bool Equals(TimelineAnimeRow o) => string.Equals(Source, o.Source, StringComparison.Ordinal) && Group == o.Group && Category == o.Category && No == o.No;
        public override bool Equals(object obj) => obj is TimelineAnimeRow o && Equals(o);
        public override int GetHashCode() => ((Group * 397 + Category) * 397 + No) ^ (Source?.GetHashCode() ?? 0);

        /// <summary>The row as JSON <c>{"source", "group", "category", "no"}</c>.</summary>
        public string ToJson()
        {
            var r = this;
            return TimelineJson.Text(w =>
            {
                w.WriteStartObject();
                w.WritePropertyName("source");
                w.WriteValue(r.Source);
                w.WritePropertyName("group");
                w.WriteValue(r.Group);
                w.WritePropertyName("category");
                w.WriteValue(r.Category);
                w.WritePropertyName("no");
                w.WriteValue(r.No);
                w.WriteEndObject();
            });
        }

        public override string ToString() => (Source ?? "") + " " + Group + "/" + Category + "/" + No;
    }

    /// <summary>JSON helpers of the Timeline (invariant numbers, compact text).</summary>
    public static class TimelineJson
    {
        public static void WriteVec(JsonWriter w, in TimelineVec3 v)
        {
            w.WriteStartArray();
            w.WriteValue(v.X);
            w.WriteValue(v.Y);
            w.WriteValue(v.Z);
            w.WriteEndArray();
        }

        /// <summary>The compact JSON text <paramref name="write"/> produces.</summary>
        public static string Text(Action<JsonWriter> write)
        {
            var sb = new StringWriter(CultureInfo.InvariantCulture);
            using (var w = new JsonTextWriter(sb) { Formatting = Formatting.None, Culture = CultureInfo.InvariantCulture })
                write(w);
            return sb.ToString();
        }

        /// <summary>A JSON array of 3 numbers as a vector; false for another shape.</summary>
        public static bool TryVec(JToken t, out TimelineVec3 v)
        {
            v = TimelineVec3.Zero;
            if (!(t is JArray a) || a.Count != 3) return false;
            if (!TryNumber(a[0], out double x) || !TryNumber(a[1], out double y) || !TryNumber(a[2], out double z)) return false;
            v = new TimelineVec3(x, y, z);
            return true;
        }

        /// <summary>A finite JSON number (integer or float); false for another type.</summary>
        public static bool TryNumber(JToken t, out double v)
        {
            v = 0;
            if (t == null) return false;
            if (t.Type == JTokenType.Integer) { v = t.Value<double>(); return true; }
            if (t.Type == JTokenType.Float)
            {
                v = t.Value<double>();
                return !double.IsNaN(v) && !double.IsInfinity(v);
            }
            return false;
        }
    }
}
