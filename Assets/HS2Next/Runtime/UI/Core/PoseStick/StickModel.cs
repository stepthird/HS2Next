// Stick-figure thumbnails of pose files: the joints a figure is drawn from, the rest
// skeleton of one sex as plain data (read from the user's install by the app and cached under its Cache folder; never
// shipped), and a forward-kinematics evaluation of that skeleton with local-rotation overrides (the fallback where the
// bones-only rig of the app cannot be built, and the plain-Mono tests). Unity-free (System only).
//
// Conventions as the engine's: Y up, the character faces +Z (its left side at -X), Euler angles in degrees applied about Z, then X, then Y; a
// bone's world matrix is its parent's times its local translation · rotation · scale.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HS2Next.UI.Core
{
    /// <summary>A double-precision 3-vector.</summary>
    public struct StickVec
    {
        public double X, Y, Z;
        public StickVec(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static StickVec operator +(StickVec a, StickVec b) => new StickVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static StickVec operator -(StickVec a, StickVec b) => new StickVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static StickVec operator *(StickVec a, double s) => new StickVec(a.X * s, a.Y * s, a.Z * s);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public static double Dot(StickVec a, StickVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static StickVec Lerp(StickVec a, StickVec b, double t) => new StickVec(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####}, {2:0.####})", X, Y, Z);
    }

    /// <summary>A double-precision rotation (x, y, z, w) with the engine's Euler convention.</summary>
    public struct StickQuat
    {
        public double X, Y, Z, W;
        public StickQuat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public static readonly StickQuat Identity = new StickQuat(0, 0, 0, 1);

        public static StickQuat operator *(StickQuat a, StickQuat b) => new StickQuat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        static StickQuat Axis(double deg, int axis)
        {
            double h = deg * Math.PI / 360.0, s = Math.Sin(h), c = Math.Cos(h);
            return axis == 0 ? new StickQuat(s, 0, 0, c) : axis == 1 ? new StickQuat(0, s, 0, c) : new StickQuat(0, 0, s, c);
        }

        /// <summary>Euler degrees: Z first, then X, then Y (the engine's order), i.e. qY · qX · qZ.</summary>
        public static StickQuat Euler(double x, double y, double z) => Axis(y, 1) * Axis(x, 0) * Axis(z, 2);

        public StickQuat Normalised()
        {
            double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            return n > 0 ? new StickQuat(X / n, Y / n, Z / n, W / n) : Identity;
        }

        /// <summary>The rotated vector.</summary>
        public StickVec Rotate(StickVec v)
        {
            double tx = 2 * (Y * v.Z - Z * v.Y), ty = 2 * (Z * v.X - X * v.Z), tz = 2 * (X * v.Y - Y * v.X);
            return new StickVec(v.X + W * tx + (Y * tz - Z * ty), v.Y + W * ty + (Z * tx - X * tz), v.Z + W * tz + (X * ty - Y * tx));
        }
    }

    /// <summary>Which side of the body a joint or segment belongs to (drawn in two colours).</summary>
    public enum StickSide : byte { Centre = 0, Left = 1, Right = 2 }

    /// <summary>What a segment draws (width and depth cue by part).</summary>
    public enum StickPart : byte { Spine = 0, Girdle = 1, Limb = 2, End = 3 }

    /// <summary>The joints of a stick figure: skeleton bones by name, plus three head points derived from the head bone.</summary>
    public static class StickJoints
    {
        public const int Hips = 0, Waist = 1, Spine01 = 2, Spine02 = 3, Spine03 = 4, Neck = 5, Head = 6,
            HeadCentre = 7, HeadTop = 8, Face = 9,
            ShoulderL = 10, ArmUpL = 11, ArmLowL = 12, HandL = 13, HandTipL = 14,
            ShoulderR = 15, ArmUpR = 16, ArmLowR = 17, HandR = 18, HandTipR = 19,
            LegUpL = 20, LegLowL = 21, FootL = 22, ToesL = 23,
            LegUpR = 24, LegLowR = 25, FootR = 26, ToesR = 27,
            Count = 28;

        /// <summary>The bone of each joint (null for the head points, which the head bone's frame gives).</summary>
        public static readonly string[] Bones =
        {
            "cf_J_Hips", "cf_J_Kosi01", "cf_J_Spine01", "cf_J_Spine02", "cf_J_Spine03", "cf_J_Neck", "cf_J_Head",
            null, null, null,
            "cf_J_Shoulder_L", "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L", "cf_J_Hand_Middle01_L",
            "cf_J_Shoulder_R", "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R", "cf_J_Hand_Middle01_R",
            "cf_J_LegUp00_L", "cf_J_LegLow01_L", "cf_J_Foot01_L", "cf_J_Toes01_L",
            "cf_J_LegUp00_R", "cf_J_LegLow01_R", "cf_J_Foot01_R", "cf_J_Toes01_R",
        };

        /// <summary>
        /// The head points in the head bone's local frame, in units of the neck → head distance at rest (the skeleton has
        /// no head geometry): the centre of the head circle, the top of the head and a point on the face (the face mark).
        /// </summary>
        public static readonly StickVec HeadCentreLocal = new StickVec(0, 1.35, 0.15), HeadTopLocal = new StickVec(0, 2.65, 0.15), FaceLocal = new StickVec(0, 1.25, 1.10);

        /// <summary>The head circle's radius in units of the neck → head distance at rest.</summary>
        public const double HeadRadius = 1.30;

        public static StickSide SideOf(int joint)
        {
            if (joint >= ShoulderL && joint <= HandTipL || joint >= LegUpL && joint <= ToesL) return StickSide.Left;
            if (joint >= ShoulderR && joint <= HandTipR || joint >= LegUpR && joint <= ToesR) return StickSide.Right;
            return StickSide.Centre;
        }
    }

    /// <summary>A segment between two joints, in one of the figure's chains (spine, two arms, two legs).</summary>
    public struct StickSegment
    {
        public int A, B;
        public StickSide Side;
        public StickPart Part;
        /// <summary>The chain (StickSegments.Spine, ArmL, ArmR, LegL, LegR): a chain is drawn as one line (no gaps at its joints).</summary>
        public int Chain;
        public StickSegment(int a, int b, StickSide side, StickPart part, int chain) { A = a; B = b; Side = side; Part = part; Chain = chain; }
    }

    public static class StickSegments
    {
        public const int Spine = 0, ArmL = 1, ArmR = 2, LegL = 3, LegR = 4, ChainCount = 5;

        /// <summary>The spine to the head bone; each arm from the top of the spine through the clavicle to the hand's end; each
        /// leg from the waist through the thigh to the toes. Hands and feet are short end segments.</summary>
        public static readonly StickSegment[] All =
        {
            new StickSegment(StickJoints.Waist, StickJoints.Spine01, StickSide.Centre, StickPart.Spine, Spine),
            new StickSegment(StickJoints.Spine01, StickJoints.Spine02, StickSide.Centre, StickPart.Spine, Spine),
            new StickSegment(StickJoints.Spine02, StickJoints.Spine03, StickSide.Centre, StickPart.Spine, Spine),
            new StickSegment(StickJoints.Spine03, StickJoints.Neck, StickSide.Centre, StickPart.Spine, Spine),
            new StickSegment(StickJoints.Neck, StickJoints.Head, StickSide.Centre, StickPart.Spine, Spine),
            new StickSegment(StickJoints.Spine03, StickJoints.ShoulderL, StickSide.Left, StickPart.Girdle, ArmL),
            new StickSegment(StickJoints.ShoulderL, StickJoints.ArmUpL, StickSide.Left, StickPart.Girdle, ArmL),
            new StickSegment(StickJoints.ArmUpL, StickJoints.ArmLowL, StickSide.Left, StickPart.Limb, ArmL),
            new StickSegment(StickJoints.ArmLowL, StickJoints.HandL, StickSide.Left, StickPart.Limb, ArmL),
            new StickSegment(StickJoints.HandL, StickJoints.HandTipL, StickSide.Left, StickPart.End, ArmL),
            new StickSegment(StickJoints.Spine03, StickJoints.ShoulderR, StickSide.Right, StickPart.Girdle, ArmR),
            new StickSegment(StickJoints.ShoulderR, StickJoints.ArmUpR, StickSide.Right, StickPart.Girdle, ArmR),
            new StickSegment(StickJoints.ArmUpR, StickJoints.ArmLowR, StickSide.Right, StickPart.Limb, ArmR),
            new StickSegment(StickJoints.ArmLowR, StickJoints.HandR, StickSide.Right, StickPart.Limb, ArmR),
            new StickSegment(StickJoints.HandR, StickJoints.HandTipR, StickSide.Right, StickPart.End, ArmR),
            new StickSegment(StickJoints.Waist, StickJoints.LegUpL, StickSide.Left, StickPart.Girdle, LegL),
            new StickSegment(StickJoints.LegUpL, StickJoints.LegLowL, StickSide.Left, StickPart.Limb, LegL),
            new StickSegment(StickJoints.LegLowL, StickJoints.FootL, StickSide.Left, StickPart.Limb, LegL),
            new StickSegment(StickJoints.FootL, StickJoints.ToesL, StickSide.Left, StickPart.End, LegL),
            new StickSegment(StickJoints.Waist, StickJoints.LegUpR, StickSide.Right, StickPart.Girdle, LegR),
            new StickSegment(StickJoints.LegUpR, StickJoints.LegLowR, StickSide.Right, StickPart.Limb, LegR),
            new StickSegment(StickJoints.LegLowR, StickJoints.FootR, StickSide.Right, StickPart.Limb, LegR),
            new StickSegment(StickJoints.FootR, StickJoints.ToesR, StickSide.Right, StickPart.End, LegR),
        };
    }

    /// <summary>The joint positions of one posed figure in the character root's space (Y up, facing +Z, left at -X).</summary>
    public sealed class StickPose
    {
        public readonly StickVec[] P = new StickVec[StickJoints.Count];
        public readonly bool[] Has = new bool[StickJoints.Count];
        /// <summary>The neck → head distance at rest (the unit of the head points and the head radius).</summary>
        public double HeadUnit;
        /// <summary>The rest figure's height (head top to the lowest foot point) of this skeleton (the steady scale of the view).</summary>
        public double RestHeight;

        public void Set(int j, StickVec v) { P[j] = v; Has[j] = true; }

        /// <summary>Every joint is known.</summary>
        public bool Complete { get { foreach (var h in Has) if (!h) return false; return true; } }

        /// <summary>True when every coordinate is a finite number.</summary>
        public bool Finite
        {
            get
            {
                for (int i = 0; i < P.Length; i++)
                    if (Has[i] && (double.IsNaN(P[i].X) || double.IsNaN(P[i].Y) || double.IsNaN(P[i].Z) || double.IsInfinity(P[i].X) || double.IsInfinity(P[i].Y) || double.IsInfinity(P[i].Z))) return false;
                return true;
            }
        }
    }

    /// <summary>
    /// The rest skeleton of one sex: every transform from the skeleton prefab's root down (name, parent, local position,
    /// rotation, scale) after the default body shape of that sex. File: `#hs2next-stick-rig\t1`, a `stamp\t…` line (the
    /// source it was read from; another stamp means read it again), a `sex\t0|1` line, then one line per transform
    /// `name \t parent index \t px py pz \t qx qy qz qw \t sx sy sz` (round-trip floats, invariant culture).
    /// </summary>
    public sealed class StickRest
    {
        public const string Header = "#hs2next-stick-rig\t1";

        public string Stamp = "";
        public int Sex = 1;
        public readonly List<string> Names = new List<string>();
        public readonly List<int> Parents = new List<int>();
        public readonly List<float[]> Pos = new List<float[]>(), Rot = new List<float[]>(), Scale = new List<float[]>();
        readonly Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count => Names.Count;

        /// <summary>Adds a transform (parents before children); returns its index. A repeated name keeps its first index for
        /// lookups (as the app's bone dictionaries do).</summary>
        public int Add(string name, int parent, float[] pos, float[] rot, float[] scale)
        {
            if (parent >= Names.Count) throw new ArgumentException("a parent must come before its children");
            Names.Add(name ?? "");
            Parents.Add(parent);
            Pos.Add(pos); Rot.Add(rot); Scale.Add(scale);
            int i = Names.Count - 1;
            if (!index.ContainsKey(Names[i])) index[Names[i]] = i;
            return i;
        }

        /// <summary>The first transform of that name, or -1.</summary>
        public int IndexOf(string name) => name != null && index.TryGetValue(name, out int i) ? i : -1;

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        public string ToText()
        {
            var sb = new StringBuilder(Count * 96);
            sb.Append(Header).Append('\n');
            sb.Append("stamp\t").Append((Stamp ?? "").Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
            sb.Append("sex\t").Append(Sex.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < Count; i++)
            {
                sb.Append(Names[i]).Append('\t').Append(Parents[i].ToString(CultureInfo.InvariantCulture)).Append('\t');
                sb.Append(F(Pos[i][0])).Append(' ').Append(F(Pos[i][1])).Append(' ').Append(F(Pos[i][2])).Append('\t');
                sb.Append(F(Rot[i][0])).Append(' ').Append(F(Rot[i][1])).Append(' ').Append(F(Rot[i][2])).Append(' ').Append(F(Rot[i][3])).Append('\t');
                sb.Append(F(Scale[i][0])).Append(' ').Append(F(Scale[i][1])).Append(' ').Append(F(Scale[i][2])).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Parses the file text; null with <paramref name="problem"/> set when it is not a rig file of this version.</summary>
        public static StickRest Parse(string text, out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(text)) { problem = "empty"; return null; }
            var lines = text.Split('\n');
            if (lines.Length < 3 || lines[0].TrimEnd('\r') != Header) { problem = "not a rig file of this version"; return null; }
            var r = new StickRest();
            for (int li = 1; li < lines.Length; li++)
            {
                string line = lines[li].TrimEnd('\r');
                if (line.Length == 0) continue;
                var f = line.Split('\t');
                if (f[0] == "stamp" && f.Length == 2 && r.Count == 0) { r.Stamp = f[1]; continue; }
                if (f[0] == "sex" && f.Length == 2 && r.Count == 0 && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sx)) { r.Sex = sx; continue; }
                if (f.Length != 5 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parent) || parent >= r.Count || parent < -1)
                { problem = "line " + (li + 1) + " unreadable"; return null; }
                var p = Floats(f[2], 3); var q = Floats(f[3], 4); var s = Floats(f[4], 3);
                if (p == null || q == null || s == null) { problem = "line " + (li + 1) + " unreadable"; return null; }
                r.Add(f[0], parent, p, q, s);
            }
            if (r.Count == 0) { problem = "no transforms"; return null; }
            return r;
        }

        static float[] Floats(string s, int n)
        {
            var parts = s.Split(' ');
            if (parts.Length != n) return null;
            var a = new float[n];
            for (int i = 0; i < n; i++) if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out a[i])) return null;
            return a;
        }

        /// <summary>Reads a rig file (null when missing or unreadable; <paramref name="problem"/> says why).</summary>
        public static StickRest Read(string file, out string problem)
        {
            problem = null;
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) { problem = "missing"; return null; }
                return Parse(File.ReadAllText(file, new UTF8Encoding(false)), out problem);
            }
            catch (Exception x) { problem = x.GetType().Name + ": " + x.Message; return null; }
        }

        /// <summary>Writes the file through a temporary file; returns null or the error.</summary>
        public string Write(string file)
        {
            string tmp = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                tmp = file + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                File.WriteAllText(tmp, ToText(), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(tmp, file, null, true);
                else File.Move(tmp, file);
                tmp = null;
                return null;
            }
            catch (Exception x) { return x.GetType().Name + ": " + x.Message; }
            finally { if (tmp != null) try { File.Delete(tmp); } catch (Exception) { } }
        }
    }

    /// <summary>
    /// Forward kinematics of a <see cref="StickRest"/> in double precision: local rotations may be overridden by bone
    /// name (Euler degrees, as the FK writer writes them); world matrices are composed parent first. The figure is read in
    /// the space of transform 0's parent (the character root).
    /// </summary>
    public sealed class StickSkeleton
    {
        readonly StickRest rest;
        readonly int[] jointBone = new int[StickJoints.Count];
        // world affine matrices, row-major 3 × 4
        readonly double[][] world;
        readonly StickQuat[] local;
        readonly double headUnit, restHeight;

        public StickRest Rest => rest;
        /// <summary>The neck → head distance at rest.</summary>
        public double HeadUnit => headUnit;
        public double RestHeight => restHeight;
        /// <summary>Joint bones the skeleton lacks (names).</summary>
        public readonly List<string> Missing = new List<string>();

        public StickSkeleton(StickRest rest)
        {
            this.rest = rest ?? throw new ArgumentNullException(nameof(rest));
            world = new double[rest.Count][];
            local = new StickQuat[rest.Count];
            for (int j = 0; j < StickJoints.Count; j++)
            {
                string b = StickJoints.Bones[j];
                jointBone[j] = b == null ? -1 : rest.IndexOf(b);
                if (b != null && jointBone[j] < 0) Missing.Add(b);
            }
            var p = Evaluate(null);
            headUnit = p.HeadUnit;
            restHeight = p.RestHeight;
        }

        /// <summary>The figure with the given local rotations (bone name → Euler degrees); other bones keep their rest rotation.</summary>
        public StickPose Evaluate(IReadOnlyDictionary<string, StickVec> localEuler)
        {
            int n = rest.Count;
            for (int i = 0; i < n; i++)
            {
                var r = rest.Rot[i];
                local[i] = new StickQuat(r[0], r[1], r[2], r[3]).Normalised();
            }
            if (localEuler != null)
                foreach (var kv in localEuler)
                {
                    int i = rest.IndexOf(kv.Key);
                    if (i >= 0) local[i] = StickQuat.Euler(kv.Value.X, kv.Value.Y, kv.Value.Z);
                }
            for (int i = 0; i < n; i++)
            {
                var m = Trs(rest.Pos[i], local[i], rest.Scale[i]);
                int parent = rest.Parents[i];
                world[i] = parent < 0 ? m : Mul(world[parent], m);
            }
            var pose = new StickPose();
            for (int j = 0; j < StickJoints.Count; j++)
                if (jointBone[j] >= 0) pose.Set(j, Origin(world[jointBone[j]]));
            int head = jointBone[StickJoints.Head], neck = jointBone[StickJoints.Neck];
            double unit = headUnit;
            if (unit <= 0 && head >= 0 && neck >= 0) unit = (Origin(world[head]) - Origin(world[neck])).Length;
            pose.HeadUnit = unit;
            if (head >= 0 && unit > 0)
            {
                var hm = world[head];
                pose.Set(StickJoints.HeadCentre, Point(hm, StickJoints.HeadCentreLocal, unit));
                pose.Set(StickJoints.HeadTop, Point(hm, StickJoints.HeadTopLocal, unit));
                pose.Set(StickJoints.Face, Point(hm, StickJoints.FaceLocal, unit));
            }
            if (restHeight > 0) pose.RestHeight = restHeight;
            else
            {
                double top = double.MinValue, bottom = double.MaxValue;
                for (int j = 0; j < StickJoints.Count; j++)
                {
                    if (!pose.Has[j]) continue;
                    top = Math.Max(top, pose.P[j].Y);
                    bottom = Math.Min(bottom, pose.P[j].Y);
                }
                pose.RestHeight = top > bottom ? top - bottom : 0;
            }
            return pose;
        }

        // the head points: a direction in the head bone's frame scaled by the rest neck → head distance (the bone's own
        // scale is left out, so the head keeps its size when a shape scales the head bone)
        static StickVec Point(double[] m, StickVec local, double unit)
        {
            double lx = Nz(Math.Sqrt(Col(m, 0))), ly = Nz(Math.Sqrt(Col(m, 1))), lz = Nz(Math.Sqrt(Col(m, 2)));
            var d = new StickVec(
                m[0] / lx * local.X + m[1] / ly * local.Y + m[2] / lz * local.Z,
                m[4] / lx * local.X + m[5] / ly * local.Y + m[6] / lz * local.Z,
                m[8] / lx * local.X + m[9] / ly * local.Y + m[10] / lz * local.Z);
            return Origin(m) + d * unit;
        }

        static double Nz(double v) => v > 1e-12 ? v : 1;
        static double Col(double[] m, int c) => m[c] * m[c] + m[4 + c] * m[4 + c] + m[8 + c] * m[8 + c];

        static StickVec Origin(double[] m) => new StickVec(m[3], m[7], m[11]);

        static double[] Trs(float[] p, StickQuat q, float[] s)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
            return new[]
            {
                (1 - 2 * (yy + zz)) * s[0], 2 * (xy - wz) * s[1], 2 * (xz + wy) * s[2], p[0],
                2 * (xy + wz) * s[0], (1 - 2 * (xx + zz)) * s[1], 2 * (yz - wx) * s[2], p[1],
                2 * (xz - wy) * s[0], 2 * (yz + wx) * s[1], (1 - 2 * (xx + yy)) * s[2], p[2],
            };
        }

        static double[] Mul(double[] a, double[] b)
        {
            var r = new double[12];
            for (int row = 0; row < 3; row++)
            {
                int o = row * 4;
                for (int c = 0; c < 4; c++)
                    r[o + c] = a[o] * b[c] + a[o + 1] * b[4 + c] + a[o + 2] * b[8 + c] + (c == 3 ? a[o + 3] : 0);
            }
            return r;
        }

        /// <summary>The world matrix (row-major 3 × 4) of transform <paramref name="i"/> after the last Evaluate.</summary>
        public double[] WorldOf(int i) => world[i];
    }
}
