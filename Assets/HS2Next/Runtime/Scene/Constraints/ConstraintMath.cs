// The arithmetic of a constraint, in double and without the engine: the world value a target takes from a source, the offset taken
// at creation, world values turned into the local values a target's channel holds, and the blend of a weight. The caller reads and
// writes transforms; nothing here keeps time, so a value is a function of the poses it is given.
//   position   p = p_s + o_pos                    world axes
//   rotation   q = q_s · o_rot
//   scale      l = l_s ⊙ o_scale                  per component
//   lookAt     q = Look(p_s − p_t, up) · o_rot    +Z of the target points at the source
//   parent     q = q_s · o_rot, p = p_s + q_s · (l_s ⊙ o_pos)
// A channel holds what one frame's stack of constraints on it comes to: the first blends over the base, each next one over the
// value so far, and the slot is set once, with the result, so a stack at rest writes nothing.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Routes;

namespace HS2Next.Scenes.Constraints
{
    /// <summary>The components of a constraint, as a set.</summary>
    [Flags]
    public enum ConstraintParts
    {
        None = 0,
        Position = 1,
        Rotation = 2,
        Scale = 4,
        LookAt = 8,
        Parent = 16,
    }

    /// <summary>What a transform shows in the world: position, rotation, lossy scale.</summary>
    public struct ConstraintPose
    {
        public Vec3d Pos;
        public Quatd Rot;
        public Vec3d Scale;

        public ConstraintPose(Vec3d pos, Quatd rot, Vec3d scale) { Pos = pos; Rot = rot; Scale = scale; }
    }

    /// <summary>The offsets of a constraint: a position, a rotation, a scale. Identity when no component uses them.</summary>
    public struct ConstraintOffsetValue
    {
        public Vec3d Pos;
        public Quatd Rot;
        public Vec3d Scale;

        public ConstraintOffsetValue(Vec3d pos, Quatd rot, Vec3d scale) { Pos = pos; Rot = rot; Scale = scale; }

        public static readonly ConstraintOffsetValue Identity = new ConstraintOffsetValue(Vec3d.Zero, Quatd.Identity, new Vec3d(1, 1, 1));
    }

    /// <summary>The world values of one evaluation: each channel the components gave.</summary>
    public struct ConstraintWorld
    {
        public bool HasPos, HasRot, HasScale;
        public Vec3d Pos;
        public Quatd Rot;
        public Vec3d Scale;
    }

    /// <summary>The space a local value lives in: a parent transform's matrix, rotation and lossy scale.</summary>
    public struct ConstraintFrame
    {
        // the rows of the local-to-world matrix
        public double M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23;
        public Quatd Rot;
        public Vec3d Scale;

        /// <summary>The frame of a translation, a rotation and a scale, with nothing above them.</summary>
        public static ConstraintFrame Of(Vec3d pos, Quatd rot, Vec3d scale)
        {
            var x = rot.Rotate(new Vec3d(scale.X, 0, 0));
            var y = rot.Rotate(new Vec3d(0, scale.Y, 0));
            var z = rot.Rotate(new Vec3d(0, 0, scale.Z));
            return new ConstraintFrame
            {
                M00 = x.X, M01 = y.X, M02 = z.X, M03 = pos.X,
                M10 = x.Y, M11 = y.Y, M12 = z.Y, M13 = pos.Y,
                M20 = x.Z, M21 = y.Z, M22 = z.Z, M23 = pos.Z,
                Rot = rot,
                Scale = scale,
            };
        }

        /// <summary>The world space itself.</summary>
        public static readonly ConstraintFrame World = Of(Vec3d.Zero, Quatd.Identity, new Vec3d(1, 1, 1));

        /// <summary>A local point in the world.</summary>
        public Vec3d Point(Vec3d p) => new Vec3d(
            M00 * p.X + M01 * p.Y + M02 * p.Z + M03,
            M10 * p.X + M11 * p.Y + M12 * p.Z + M13,
            M20 * p.X + M21 * p.Y + M22 * p.Z + M23);

        /// <summary>A world point in this frame, through the matrix, so a skewed parent is exact; false when the frame has no inverse.</summary>
        public bool InversePoint(Vec3d p, out Vec3d local)
        {
            double c00 = M11 * M22 - M12 * M21, c01 = M12 * M20 - M10 * M22, c02 = M10 * M21 - M11 * M20;
            double det = M00 * c00 + M01 * c01 + M02 * c02;
            if (Math.Abs(det) < 1e-18) { local = default; return false; }
            double x = p.X - M03, y = p.Y - M13, z = p.Z - M23, d = 1.0 / det;
            local = new Vec3d(
                (c00 * x + (M02 * M21 - M01 * M22) * y + (M01 * M12 - M02 * M11) * z) * d,
                (c01 * x + (M00 * M22 - M02 * M20) * y + (M02 * M10 - M00 * M12) * z) * d,
                (c02 * x + (M01 * M20 - M00 * M21) * y + (M00 * M11 - M01 * M10) * z) * d);
            return true;
        }
    }

    public static class ConstraintMath
    {
        /// <summary>A source scale component under this counts as 1 where an offset divides by it.</summary>
        public const double ScaleFloor = 1e-6;
        /// <summary>Source and target closer than this have no direction: a look holds its last value.</summary>
        public const double CoincidentDistance = 1e-6;
        /// <summary>A look this close to parallel with the world's up (the sine of the angle) takes the target's own up.</summary>
        public const double ParallelSine = 1e-4;

        const double RadToDeg = 180.0 / Math.PI;

        // ------------------------------------------------------------ what an entry holds

        /// <summary>The set of an entry's components; unknown strings count for nothing (such an entry is not active).</summary>
        public static ConstraintParts PartsOf(IReadOnlyList<string> components)
        {
            var p = ConstraintParts.None;
            if (components == null) return p;
            for (int i = 0; i < components.Count; i++)
                switch (components[i])
                {
                    case ConstraintComponents.Position: p |= ConstraintParts.Position; break;
                    case ConstraintComponents.Rotation: p |= ConstraintParts.Rotation; break;
                    case ConstraintComponents.Scale: p |= ConstraintParts.Scale; break;
                    case ConstraintComponents.LookAt: p |= ConstraintParts.LookAt; break;
                    case ConstraintComponents.Parent: p |= ConstraintParts.Parent; break;
                }
            return p;
        }

        /// <summary>A stored offset as the arithmetic takes it: the rotation from its Euler degrees.</summary>
        public static ConstraintOffsetValue ValueOf(ConstraintOffset o) => o == null ? ConstraintOffsetValue.Identity : new ConstraintOffsetValue(
            new Vec3d(o.Pos.X, o.Pos.Y, o.Pos.Z), Normalized(Quatd.Euler(o.Rot.X, o.Rot.Y, o.Rot.Z)), new Vec3d(o.Scale.X, o.Scale.Y, o.Scale.Z));

        /// <summary>The form an entry stores: the rotation as Euler degrees, every number as a float.</summary>
        public static ConstraintOffset StoredOf(in ConstraintOffsetValue o)
        {
            var e = ToEuler(o.Rot);
            return new ConstraintOffset(new Vec3((float)o.Pos.X, (float)o.Pos.Y, (float)o.Pos.Z), new Vec3((float)e.X, (float)e.Y, (float)e.Z),
                new Vec3((float)o.Scale.X, (float)o.Scale.Y, (float)o.Scale.Z));
        }

        // ------------------------------------------------------------ vectors, rotations

        /// <summary>Per-component product.</summary>
        public static Vec3d Mul(Vec3d a, Vec3d b) => new Vec3d(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        /// <summary>This rotation at unit length (the identity for a zero quaternion).</summary>
        public static Quatd Normalized(Quatd q)
        {
            double l = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            return l > 0 ? new Quatd(q.X / l, q.Y / l, q.Z / l, q.W / l) : Quatd.Identity;
        }

        /// <summary>Lerp of a weight: exact at 0 and 1.</summary>
        public static Vec3d Lerp(Vec3d a, Vec3d b, double w) =>
            w >= 1 ? b : w <= 0 ? a : new Vec3d(a.X + (b.X - a.X) * w, a.Y + (b.Y - a.Y) * w, a.Z + (b.Z - a.Z) * w);

        /// <summary>Slerp of a weight on the shorter arc: exact at 0 and 1.</summary>
        public static Quatd Slerp(Quatd a, Quatd b, double w)
        {
            if (w >= 1) return b;
            if (w <= 0) return a;
            double d = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            double s = 1;
            if (d < 0) { d = -d; s = -1; }
            double ka, kb;
            if (d > 0.9999999)
            {
                ka = 1 - w;
                kb = w;
            }
            else
            {
                double angle = Math.Acos(d), sin = Math.Sin(angle);
                ka = Math.Sin((1 - w) * angle) / sin;
                kb = Math.Sin(w * angle) / sin;
            }
            kb *= s;
            return Normalized(new Quatd(ka * a.X + kb * b.X, ka * a.Y + kb * b.Y, ka * a.Z + kb * b.Z, ka * a.W + kb * b.W));
        }

        /// <summary>
        /// The rotation that turns +Z to <paramref name="forward"/> with +Y as near <paramref name="up"/> as it can be. An up parallel
        /// to the direction gives way to another axis: the result is always a rotation.
        /// </summary>
        public static Quatd Look(Vec3d forward, Vec3d up)
        {
            var f = forward * (1.0 / forward.Magnitude);
            var r = Vec3d.Cross(up, f);
            double l = r.Magnitude;
            if (l < 1e-12)
            {
                r = Vec3d.Cross(Math.Abs(f.Y) < 0.9 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0), f);
                l = r.Magnitude;
            }
            r = r * (1.0 / l);
            var u = Vec3d.Cross(f, r);
            return FromAxes(r, u, f);
        }

        // The rotation whose columns are the three orthonormal axes.
        static Quatd FromAxes(Vec3d x, Vec3d y, Vec3d z)
        {
            double t = x.X + y.Y + z.Z;
            if (t > 0)
            {
                double s = Math.Sqrt(t + 1.0) * 2.0;
                return Normalized(new Quatd((y.Z - z.Y) / s, (z.X - x.Z) / s, (x.Y - y.X) / s, 0.25 * s));
            }
            if (x.X >= y.Y && x.X >= z.Z)
            {
                double s = Math.Sqrt(1.0 + x.X - y.Y - z.Z) * 2.0;
                return Normalized(new Quatd(0.25 * s, (y.X + x.Y) / s, (z.X + x.Z) / s, (y.Z - z.Y) / s));
            }
            if (y.Y >= z.Z)
            {
                double s = Math.Sqrt(1.0 + y.Y - x.X - z.Z) * 2.0;
                return Normalized(new Quatd((y.X + x.Y) / s, 0.25 * s, (z.Y + y.Z) / s, (z.X - x.Z) / s));
            }
            double w = Math.Sqrt(1.0 + z.Z - x.X - y.Y) * 2.0;
            return Normalized(new Quatd((z.X + x.Z) / w, (z.Y + y.Z) / w, 0.25 * w, (x.Y - y.X) / w));
        }

        /// <summary>Euler degrees of a rotation in the engine's convention (about Z, then X, then Y), as a stored rotation holds them.</summary>
        public static Vec3d ToEuler(Quatd q)
        {
            q = Normalized(q);
            double m12 = 2 * (q.Y * q.Z - q.W * q.X);
            double x = Math.Asin(Math.Max(-1, Math.Min(1, -m12)));
            double y, z;
            if (Math.Abs(m12) < 0.9999999)
            {
                y = Math.Atan2(2 * (q.X * q.Z + q.W * q.Y), 1 - 2 * (q.X * q.X + q.Y * q.Y));
                z = Math.Atan2(2 * (q.X * q.Y + q.W * q.Z), 1 - 2 * (q.X * q.X + q.Z * q.Z));
            }
            else
            {
                y = Math.Atan2(-2 * (q.X * q.Z - q.W * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
                z = 0;
            }
            return new Vec3d(x * RadToDeg, y * RadToDeg, z * RadToDeg);
        }

        // ------------------------------------------------------------ offsets

        static double Divide(double a, double b) => Math.Abs(b) < ScaleFloor ? a : a / b;

        static double Ratio(double a, double b) => Math.Abs(b) < ScaleFloor ? 1 : a / b;

        /// <summary>The direction a look takes from a target at <paramref name="from"/> to a source, or false at one point.</summary>
        public static bool Direction(Vec3d from, Vec3d to, out Vec3d d)
        {
            d = to - from;
            return d.Magnitude >= CoincidentDistance;
        }

        /// <summary>The up a look takes: the world's, or <paramref name="targetUp"/> when the direction lies along the world's.</summary>
        public static Vec3d UpFor(Vec3d direction, Vec3d targetUp)
        {
            var up = Vec3d.Up;
            double sine = Vec3d.Cross(direction * (1.0 / direction.Magnitude), up).Magnitude;
            return sine < ParallelSine ? targetUp : up;
        }

        /// <summary>
        /// The offsets that keep the target where it stands: applied to the same source they give the target's values back. A
        /// component the set does not use stays at identity. A source scale component under <see cref="ScaleFloor"/> counts as 1.
        /// </summary>
        public static ConstraintOffsetValue Take(ConstraintParts parts, in ConstraintPose s, in ConstraintPose t)
        {
            var o = ConstraintOffsetValue.Identity;
            if ((parts & ConstraintParts.Parent) != 0)
            {
                o.Rot = Normalized(s.Rot.Inverse * t.Rot);
                var d = s.Rot.Inverse.Rotate(t.Pos - s.Pos);
                o.Pos = new Vec3d(Divide(d.X, s.Scale.X), Divide(d.Y, s.Scale.Y), Divide(d.Z, s.Scale.Z));
            }
            if ((parts & ConstraintParts.Position) != 0) o.Pos = t.Pos - s.Pos;
            if ((parts & ConstraintParts.Rotation) != 0) o.Rot = Normalized(s.Rot.Inverse * t.Rot);
            if ((parts & ConstraintParts.Scale) != 0)
                o.Scale = new Vec3d(Ratio(t.Scale.X, s.Scale.X), Ratio(t.Scale.Y, s.Scale.Y), Ratio(t.Scale.Z, s.Scale.Z));
            if ((parts & ConstraintParts.LookAt) != 0 && Direction(t.Pos, s.Pos, out var dir))
                o.Rot = Normalized(Look(dir, UpFor(dir, t.Rot.Rotate(Vec3d.Up))).Inverse * t.Rot);
            return o;
        }

        // ------------------------------------------------------------ the world value

        /// <summary>
        /// The world values the components give. <paramref name="targetPos"/> is where the target stands before this constraint; a
        /// <c>lookAt</c> aims from the position of this same constraint when it has one, from there otherwise.
        /// <paramref name="targetUp"/> is the target's up. A look at a source on the target itself gives no rotation.
        /// </summary>
        public static ConstraintWorld World(ConstraintParts parts, in ConstraintOffsetValue o, in ConstraintPose s, Vec3d targetPos, Vec3d targetUp)
        {
            var w = default(ConstraintWorld);
            if ((parts & ConstraintParts.Parent) != 0)
            {
                w.Rot = Normalized(s.Rot * o.Rot);
                w.HasRot = true;
                w.Pos = s.Pos + s.Rot.Rotate(Mul(s.Scale, o.Pos));
                w.HasPos = true;
            }
            if ((parts & ConstraintParts.Position) != 0)
            {
                w.Pos = s.Pos + o.Pos;
                w.HasPos = true;
            }
            if ((parts & ConstraintParts.Rotation) != 0)
            {
                w.Rot = Normalized(s.Rot * o.Rot);
                w.HasRot = true;
            }
            if ((parts & ConstraintParts.LookAt) != 0 && Direction(w.HasPos ? w.Pos : targetPos, s.Pos, out var dir))
            {
                w.Rot = Normalized(Look(dir, UpFor(dir, targetUp)) * o.Rot);
                w.HasRot = true;
            }
            if ((parts & ConstraintParts.Scale) != 0)
            {
                w.Scale = Mul(s.Scale, o.Scale);
                w.HasScale = true;
            }
            return w;
        }

        // ------------------------------------------------------------ world to local

        /// <summary>A world rotation against a frame.</summary>
        public static Quatd LocalRotation(Quatd world, in ConstraintFrame frame) => Normalized(frame.Rot.Inverse * world);

        /// <summary>
        /// A world scale against a frame: per component over the frame's lossy scale; a frame component under
        /// <see cref="ScaleFloor"/> leaves <paramref name="keep"/>'s.
        /// </summary>
        public static Vec3d LocalScale(Vec3d world, in ConstraintFrame frame, Vec3d keep) => new Vec3d(
            Math.Abs(frame.Scale.X) < ScaleFloor ? keep.X : world.X / frame.Scale.X,
            Math.Abs(frame.Scale.Y) < ScaleFloor ? keep.Y : world.Y / frame.Scale.Y,
            Math.Abs(frame.Scale.Z) < ScaleFloor ? keep.Z : world.Z / frame.Scale.Z);

        /// <summary>What a node's scale channel holds for a world scale: the world scale itself while the node does not inherit, else the local one.</summary>
        public static Vec3d NodeScale(Vec3d world, in ConstraintFrame parent, bool inheritScale, Vec3d keep) =>
            inheritScale ? LocalScale(world, parent, keep) : world;
    }

    /// <summary>
    /// One channel of a target in one frame: the value its stack comes to, and the slot that shows it. <see cref="Begin(Vec3d)"/> starts
    /// from the base, <see cref="Blend(Vec3d, double)"/> folds in each constraint in list order, <see cref="Flush"/> sets the slot once.
    /// A value equal bit for bit to the last one sets nothing.
    /// </summary>
    public sealed class ConstraintChannel
    {
        readonly bool rotation;
        Vec3d vec;
        Quatd rot;
        double w0, w1, w2, w3;
        bool written;

        public ConstraintChannel(OverrideSlot slot, bool rotation)
        {
            Slot = slot;
            this.rotation = rotation;
        }

        /// <summary>The slot this channel shows through (a vector or a rotation).</summary>
        public OverrideSlot Slot { get; }
        /// <summary>The value so far in this frame.</summary>
        public Vec3d Vector => vec;
        /// <summary>The value so far in this frame.</summary>
        public Quatd Rotation => rot;
        /// <summary>Slot writes so far (tests).</summary>
        public long Writes { get; private set; }
        /// <summary>The caller's number of the walk that began this channel's stack.</summary>
        public long Walk = -1;

        /// <summary>Starts a frame's stack from the base.</summary>
        public void Begin(Vec3d value) => vec = value;
        /// <summary>Starts a frame's stack from the base.</summary>
        public void Begin(Quatd value) => rot = value;

        /// <summary>Folds in a constraint's local value at a weight.</summary>
        public void Blend(Vec3d value, double weight) => vec = ConstraintMath.Lerp(vec, value, weight);
        /// <summary>Folds in a constraint's local value at a weight.</summary>
        public void Blend(Quatd value, double weight) => rot = ConstraintMath.Slerp(rot, value, weight);

        /// <summary>Sets the slot to the frame's value; false when it was already there.</summary>
        public bool Flush()
        {
            double a = rotation ? rot.X : vec.X, b = rotation ? rot.Y : vec.Y, c = rotation ? rot.Z : vec.Z, d = rotation ? rot.W : 0;
            if (written && Bits.Same(a, w0) && Bits.Same(b, w1) && Bits.Same(c, w2) && Bits.Same(d, w3)) return false;
            w0 = a; w1 = b; w2 = c; w3 = d;
            written = true;
            Writes++;
            if (rotation) Slot.Set(a, b, c, d);
            else Slot.Set(a, b, c);
            return true;
        }

        /// <summary>Takes the slot's value away; the next flush writes again.</summary>
        public void Clear()
        {
            written = false;
            Slot.Clear();
        }
    }
}
