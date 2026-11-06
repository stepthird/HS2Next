// Values of the runtime override layer that are more than numbers: the depth-of-field value with its focus forms, and the
// rotation arithmetic in double (Euler angles in the engine's convention: about Z, then X, then Y). Unity-free.
using System;
using System.Globalization;

namespace HS2Next.Scenes.Overrides
{
    /// <summary>How a depth-of-field focus is found.</summary>
    public enum OverrideFocusKind
    {
        /// <summary>The pivot of the camera as shown.</summary>
        Pivot,
        /// <summary>A node's point (a character between the eyes, else its head, else its transform).</summary>
        Node,
        /// <summary>The world position of a transform of that name under the node, plus an offset in world axes.</summary>
        NodeBone,
        /// <summary>A world position.</summary>
        Point,
        /// <summary>The camera position plus a distance along its forward axis.</summary>
        Dist,
    }

    /// <summary>A depth-of-field focus form (immutable).</summary>
    public sealed class OverrideFocus : IEquatable<OverrideFocus>
    {
        public static readonly OverrideFocus Pivot = new OverrideFocus(OverrideFocusKind.Pivot, null, null, 0, 0, 0, 0);

        public OverrideFocus(OverrideFocusKind kind, string node, string bone, double x, double y, double z, double dist)
        {
            Kind = kind; Node = node; Bone = bone; X = x; Y = y; Z = z; Dist = dist;
        }

        public OverrideFocusKind Kind { get; }
        /// <summary>The node id (Node, NodeBone).</summary>
        public string Node { get; }
        /// <summary>The transform name (NodeBone).</summary>
        public string Bone { get; }
        /// <summary>The point (Point) or the offset (NodeBone).</summary>
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        /// <summary>The distance along the camera's forward axis (Dist).</summary>
        public double Dist { get; }

        public static OverrideFocus OfNode(string node) => new OverrideFocus(OverrideFocusKind.Node, node, null, 0, 0, 0, 0);
        public static OverrideFocus OfBone(string node, string bone, double ox, double oy, double oz) => new OverrideFocus(OverrideFocusKind.NodeBone, node, bone, ox, oy, oz, 0);
        public static OverrideFocus OfPoint(double x, double y, double z) => new OverrideFocus(OverrideFocusKind.Point, null, null, x, y, z, 0);
        public static OverrideFocus OfDist(double d) => new OverrideFocus(OverrideFocusKind.Dist, null, null, 0, 0, 0, d);

        public bool Equals(OverrideFocus o) =>
            o != null && o.Kind == Kind && o.Node == Node && o.Bone == Bone && Bits.Same(o.X, X) && Bits.Same(o.Y, Y) && Bits.Same(o.Z, Z) && Bits.Same(o.Dist, Dist);
        public override bool Equals(object obj) => Equals(obj as OverrideFocus);
        public override int GetHashCode() => (int)Kind ^ (Node?.GetHashCode() ?? 0);

        /// <summary>The JSON form (null for the pivot).</summary>
        public string ToJson()
        {
            var c = CultureInfo.InvariantCulture;
            switch (Kind)
            {
                case OverrideFocusKind.Node: return "{\"node\":" + Q(Node) + "}";
                case OverrideFocusKind.NodeBone: return "{\"node\":" + Q(Node) + ",\"bone\":" + Q(Bone) + ",\"offset\":[" + X.ToString("R", c) + "," + Y.ToString("R", c) + "," + Z.ToString("R", c) + "]}";
                case OverrideFocusKind.Point: return "{\"point\":[" + X.ToString("R", c) + "," + Y.ToString("R", c) + "," + Z.ToString("R", c) + "]}";
                case OverrideFocusKind.Dist: return "{\"dist\":" + Dist.ToString("R", c) + "}";
                default: return "null";
            }
        }

        static string Q(string s) => Newtonsoft.Json.JsonConvert.ToString(s ?? "");

        public override string ToString() => ToJson();
    }

    /// <summary>A depth-of-field value (immutable): the model block's members plus the blur and a blend of two foci.</summary>
    public sealed class OverrideDof : IEquatable<OverrideDof>
    {
        public OverrideDof(bool enabled, double focalSize, double aperture, double blur, OverrideFocus focus, OverrideFocus focusTo = null, double focusBlend = 0)
        {
            Enabled = enabled;
            FocalSize = focalSize;
            Aperture = aperture;
            Blur = blur;
            Focus = focus ?? OverrideFocus.Pivot;
            FocusTo = focusTo;
            FocusBlend = focusTo == null ? 0 : focusBlend;
        }

        public bool Enabled { get; }
        public double FocalSize { get; }
        public double Aperture { get; }
        /// <summary>The largest blur radius on a 1080-line frame; 0 or less = the renderer's own.</summary>
        public double Blur { get; }
        public OverrideFocus Focus { get; }
        /// <summary>A second focus whose resolved point is blended with the first by <see cref="FocusBlend"/>; null = none.</summary>
        public OverrideFocus FocusTo { get; }
        /// <summary>0 = the first focus, 1 = the second.</summary>
        public double FocusBlend { get; }

        public bool Equals(OverrideDof o) =>
            o != null && o.Enabled == Enabled && Bits.Same(o.FocalSize, FocalSize) && Bits.Same(o.Aperture, Aperture) && Bits.Same(o.Blur, Blur) &&
            Focus.Equals(o.Focus) && (FocusTo == null ? o.FocusTo == null : FocusTo.Equals(o.FocusTo)) && Bits.Same(o.FocusBlend, FocusBlend);
        public override bool Equals(object obj) => Equals(obj as OverrideDof);
        public override int GetHashCode() => Enabled ? 1 : 0;

        public override string ToString()
        {
            var c = CultureInfo.InvariantCulture;
            return "{\"enabled\":" + (Enabled ? "true" : "false") + ",\"focus\":" + Focus.ToJson() + ",\"focalSize\":" + FocalSize.ToString("R", c) +
                   ",\"aperture\":" + Aperture.ToString("R", c) + ",\"blur\":" + Blur.ToString("R", c) +
                   (FocusTo != null ? ",\"focusTo\":" + FocusTo.ToJson() + ",\"focusBlend\":" + FocusBlend.ToString("R", c) : "") + "}";
        }
    }

    /// <summary>Double arithmetic of the layer.</summary>
    public static class Bits
    {
        /// <summary>Equal bit for bit (NaN equals the same NaN; 0 and -0 differ).</summary>
        public static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        /// <summary>
        /// The quaternion (x, y, z, w) of Euler angles in degrees in the engine's convention (rotation about Z, then X, then Y:
        /// q = qy · qx · qz).
        /// </summary>
        public static void Euler(double ex, double ey, double ez, out double qx, out double qy, out double qz, out double qw)
        {
            const double h = Math.PI / 360.0;
            double sx = Math.Sin(ex * h), cx = Math.Cos(ex * h);
            double sy = Math.Sin(ey * h), cy = Math.Cos(ey * h);
            double sz = Math.Sin(ez * h), cz = Math.Cos(ez * h);
            // qy · qx = (cy sx, sy cx, -sy sx, cy cx); then · qz
            double ax = cy * sx, ay = sy * cx, az = -sy * sx, aw = cy * cx;
            qx = aw * 0 + ax * cz + ay * sz - az * 0;
            qy = aw * 0 - ax * sz + ay * cz + az * 0;
            qz = aw * sz + ax * 0 - ay * 0 + az * cz;
            qw = aw * cz - ax * 0 - ay * 0 - az * sz;
        }

        /// <summary>Normalises (x, y, z, w); false for a length that is 0 or not finite.</summary>
        public static bool Normalise(ref double x, ref double y, ref double z, ref double w)
        {
            double l = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (!(l > 0) || double.IsInfinity(l) || double.IsNaN(l)) return false;
            x /= l; y /= l; z /= l; w /= l;
            return true;
        }

        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
