// The Cutter's shapes, modes and steps, Unity-free: every cut is decided in double
// precision on plain numbers, so the same code runs in the app, in the batch tests and in a console harness.
//   Plane  the local XZ plane through the origin, front = +Y; infinite. Keep front | back; mode both = two pieces.
//   Box    a cube of 4 units sized per axis. Keep inside | outside; mode both = two pieces.
//   Tear   a pointed lens: length X 1.5 units, width Z 0.4, depth Y 0.3 (± 0.15). Keep open: one piece, "torn".
//   Hole   an ellipse: X 0.6, Z 0.6, depth Y 0.3. Keep open: one piece, "holed".
// A step's `size` is the shape's full extent in units along its own X, Y, Z (the marker's scale times the base size);
// `pos` and `rot` place it in the source's root space at the cut. A plane's size is carried and not used.
// Ragged values have ranges (amplitude 0–0.5 units, tooth 0.05–2, randomness 0–1, seed 1–9999); an out-of-range value is kept
// as stored and clamped when applied. A step may carry its own FK pose of the source; without one it is decided at the previous
// step's pose (the first step at the run's pose).
// Math: CutVec / CutQuat / CutXf follow Unity's conventions (left-handed, Quaternion.Euler applies Z, then X, then Y;
// matrices act on column vectors, T × R × S; bind poses are stored column-major).
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Cut
{
    /// <summary>The four shapes; the numbers are the marker rows' ids.</summary>
    public enum CutKind { None = 0, Plane = 1, Box = 2, Tear = 3, Hole = 4 }

    public static class CutShapeInfo
    {
        public const string Plane = "plane", Box = "box", Tear = "tear", Hole = "hole";
        public const string Front = "front", Back = "back", Inside = "inside", Outside = "outside", Open = "open", Both = "both";
        /// <summary>The section colour presets by name; DarkRed is the default.</summary>
        public static readonly (string name, float r, float g, float b)[] SectionPresets =
        {
            ("darkRed", 0.35f, 0.03f, 0.03f), ("flesh", 0.85f, 0.55f, 0.47f), ("bone", 0.89f, 0.85f, 0.75f),
            ("black", 0.05f, 0.05f, 0.05f), ("grey", 0.45f, 0.45f, 0.45f),
        };
        public static readonly float[] DefaultSection = { 0.35f, 0.03f, 0.03f };

        public static string Name(CutKind k) => k switch { CutKind.Plane => Plane, CutKind.Box => Box, CutKind.Tear => Tear, CutKind.Hole => Hole, _ => null };

        public static CutKind KindOf(string name) => name switch { Plane => CutKind.Plane, Box => CutKind.Box, Tear => CutKind.Tear, Hole => CutKind.Hole, _ => CutKind.None };

        /// <summary>The base size of a shape (its full extent at marker scale 1).</summary>
        public static CutVec BaseSize(CutKind k) => k switch
        {
            CutKind.Plane => new CutVec(10, 0, 10),
            CutKind.Box => new CutVec(4, 4, 4),
            CutKind.Tear => new CutVec(1.5, 0.3, 0.4),
            CutKind.Hole => new CutVec(0.6, 0.3, 0.6),
            _ => new CutVec(1, 1, 1),
        };

        /// <summary>The modes a shape takes; the first is the default.</summary>
        public static string[] Modes(CutKind k) => k switch
        {
            CutKind.Plane => new[] { Both, Front, Back },
            CutKind.Box => new[] { Both, Inside, Outside },
            CutKind.Tear => new[] { Open },
            CutKind.Hole => new[] { Open },
            _ => Array.Empty<string>(),
        };

        /// <summary>The `keep` value of each piece a mode makes, in piece order (both: front, back / inside, outside).</summary>
        public static string[] Keeps(CutKind k, string mode)
        {
            if (string.IsNullOrEmpty(mode)) mode = Modes(k).Length > 0 ? Modes(k)[0] : null;
            switch (k)
            {
                case CutKind.Plane: return mode == Both ? new[] { Front, Back } : mode == Front || mode == Back ? new[] { mode } : null;
                case CutKind.Box: return mode == Both ? new[] { Inside, Outside } : mode == Inside || mode == Outside ? new[] { mode } : null;
                case CutKind.Tear:
                case CutKind.Hole: return mode == Open ? new[] { Open } : null;
                default: return null;
            }
        }

        /// <summary>True when the keep value belongs to the shape.</summary>
        public static bool KeepValid(CutKind k, string keep) => k switch
        {
            CutKind.Plane => keep == Front || keep == Back,
            CutKind.Box => keep == Inside || keep == Outside,
            CutKind.Tear or CutKind.Hole => keep == Open,
            _ => false,
        };

        /// <summary>The piece's side word: front, back, inside, outside, torn, holed.</summary>
        public static string PieceName(CutKind k, string keep) => k == CutKind.Tear ? "torn" : k == CutKind.Hole ? "holed" : keep;

        /// <summary>Plane and box pieces are re-rooted at the marker; tear and hole keep the source's origin.</summary>
        public static bool ReRoots(CutKind k) => k == CutKind.Plane || k == CutKind.Box;
    }

    /// <summary>A ragged edge's values. Stored as given; Applied() clamps to the ranges.</summary>
    public sealed class CutRagParams
    {
        public const double AmpMax = 0.5, ToothMin = 0.05, ToothMax = 2, RandomMax = 1;
        public const int SeedMin = 1, SeedMax = 9999;
        public double Amp, Tooth = 0.2, Random = 0.5;
        public int Seed = 1;

        public CutRagParams Clone() => (CutRagParams)MemberwiseClone();

        /// <summary>The values clamped to their ranges; outOfRange is true when one was changed.</summary>
        public CutRagParams Applied(out bool outOfRange)
        {
            var c = new CutRagParams
            {
                Amp = Clamp(Amp, 0, AmpMax),
                Tooth = Clamp(Tooth, ToothMin, ToothMax),
                Random = Clamp(Random, 0, RandomMax),
                Seed = Math.Min(SeedMax, Math.Max(SeedMin, Seed)),
            };
            outOfRange = c.Amp != Amp || c.Tooth != Tooth || c.Random != Random || c.Seed != Seed || double.IsNaN(Amp) || double.IsNaN(Tooth) || double.IsNaN(Random);
            return c;
        }

        static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>One step of a cut's procedure (the `cut.steps[]` entry), in the source's root space.</summary>
    public sealed class CutStepDef
    {
        public string Shape = CutShapeInfo.Plane;
        public CutVec Pos;
        public CutQuat Rot = CutQuat.Identity;
        public CutVec Size = new CutVec(10, 0, 10);
        public string Keep = CutShapeInfo.Front;
        /// <summary>Null: a clean cut.</summary>
        public CutRagParams Ragged;
        public bool ClothesOnly;
        /// <summary>The source's FK values at this step (local Euler degrees by bone); null: the previous step's pose, for the first
        /// step the run's pose.</summary>
        public IDictionary<string, CutVec> Pose;

        public CutKind Kind => CutShapeInfo.KindOf(Shape);
        /// <summary>A step the cut code can run (known shape and keep).</summary>
        public bool Runnable => Kind != CutKind.None && CutShapeInfo.KeepValid(Kind, Keep);

        public CutStepDef Clone()
        {
            var c = (CutStepDef)MemberwiseClone();
            c.Ragged = Ragged?.Clone();
            return c;
        }
    }

    // ------------------------------------------------------------------ math (double)

    public readonly struct CutVec
    {
        public readonly double X, Y, Z;
        public CutVec(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static readonly CutVec Zero = new CutVec(0, 0, 0);
        public static CutVec operator +(CutVec a, CutVec b) => new CutVec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static CutVec operator -(CutVec a, CutVec b) => new CutVec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static CutVec operator -(CutVec a) => new CutVec(-a.X, -a.Y, -a.Z);
        public static CutVec operator *(CutVec a, double s) => new CutVec(a.X * s, a.Y * s, a.Z * s);
        public static CutVec operator *(double s, CutVec a) => new CutVec(a.X * s, a.Y * s, a.Z * s);
        public static double Dot(CutVec a, CutVec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static CutVec Cross(CutVec a, CutVec b) => new CutVec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double SqrLength => X * X + Y * Y + Z * Z;
        public CutVec Normalized { get { double l = Length; return l > 0 ? this * (1 / l) : Zero; } }
        public static CutVec Lerp(CutVec a, CutVec b, double t) => new CutVec(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        public double this[int i] => i == 0 ? X : i == 1 ? Y : Z;
        public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
    }

    public readonly struct CutQuat
    {
        public readonly double X, Y, Z, W;
        public CutQuat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public static readonly CutQuat Identity = new CutQuat(0, 0, 0, 1);

        public static CutQuat operator *(CutQuat a, CutQuat b) => new CutQuat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public CutQuat Normalized
        {
            get
            {
                double l = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
                return l > 0 ? new CutQuat(X / l, Y / l, Z / l, W / l) : Identity;
            }
        }

        public CutQuat Inverse => new CutQuat(-X, -Y, -Z, W);   // unit quaternions

        /// <summary>Unity's Quaternion.Euler(x, y, z) in degrees: Z first, then X, then Y.</summary>
        public static CutQuat Euler(double x, double y, double z)
        {
            const double d2r = Math.PI / 180.0;
            double hx = x * d2r * 0.5, hy = y * d2r * 0.5, hz = z * d2r * 0.5;
            var qx = new CutQuat(Math.Sin(hx), 0, 0, Math.Cos(hx));
            var qy = new CutQuat(0, Math.Sin(hy), 0, Math.Cos(hy));
            var qz = new CutQuat(0, 0, Math.Sin(hz), Math.Cos(hz));
            return qy * qx * qz;
        }

        public CutVec Rotate(CutVec v)
        {
            // v' = v + 2 w (q × v) + 2 q × (q × v)
            var q = new CutVec(X, Y, Z);
            var t = CutVec.Cross(q, v) * 2;
            return v + t * W + CutVec.Cross(q, t);
        }

        /// <summary>The rotation part of an affine matrix with no shear (columns normalized).</summary>
        public static CutQuat FromMatrix(in CutXf m)
        {
            var c0 = new CutVec(m.M00, m.M10, m.M20).Normalized;
            var c1 = new CutVec(m.M01, m.M11, m.M21).Normalized;
            var c2 = new CutVec(m.M02, m.M12, m.M22).Normalized;
            double m00 = c0.X, m10 = c0.Y, m20 = c0.Z, m01 = c1.X, m11 = c1.Y, m21 = c1.Z, m02 = c2.X, m12 = c2.Y, m22 = c2.Z;
            double tr = m00 + m11 + m22;
            if (tr > 0)
            {
                double s = Math.Sqrt(tr + 1.0) * 2;
                return new CutQuat((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s).Normalized;
            }
            if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2;
                return new CutQuat(0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s).Normalized;
            }
            if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2;
                return new CutQuat((m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s).Normalized;
            }
            {
                double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2;
                return new CutQuat((m02 + m20) / s, (m12 + m21) / s, 0.25 * s, (m10 - m01) / s).Normalized;
            }
        }

        public override string ToString() => $"({X:R}, {Y:R}, {Z:R}, {W:R})";
    }

    /// <summary>An affine 4 × 4 matrix (bottom row 0, 0, 0, 1) acting on column vectors, in double.</summary>
    public readonly struct CutXf
    {
        public readonly double M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23;

        public CutXf(double m00, double m01, double m02, double m03, double m10, double m11, double m12, double m13, double m20, double m21, double m22, double m23)
        {
            M00 = m00; M01 = m01; M02 = m02; M03 = m03;
            M10 = m10; M11 = m11; M12 = m12; M13 = m13;
            M20 = m20; M21 = m21; M22 = m22; M23 = m23;
        }

        public static readonly CutXf Identity = new CutXf(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);

        /// <summary>T × R × S.</summary>
        public static CutXf TRS(CutVec p, CutQuat q, CutVec s)
        {
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
            return new CutXf(
                (1 - 2 * (yy + zz)) * s.X, 2 * (xy - wz) * s.Y, 2 * (xz + wy) * s.Z, p.X,
                2 * (xy + wz) * s.X, (1 - 2 * (xx + zz)) * s.Y, 2 * (yz - wx) * s.Z, p.Y,
                2 * (xz - wy) * s.X, 2 * (yz + wx) * s.Y, (1 - 2 * (xx + yy)) * s.Z, p.Z);
        }

        /// <summary>A matrix from 16 floats in column-major order (Unity's Matrix4x4 layout); the bottom row is ignored.</summary>
        public static CutXf FromColumnMajor(float[] a, int o) => new CutXf(
            a[o + 0], a[o + 4], a[o + 8], a[o + 12],
            a[o + 1], a[o + 5], a[o + 9], a[o + 13],
            a[o + 2], a[o + 6], a[o + 10], a[o + 14]);

        public static CutXf operator *(in CutXf a, in CutXf b) => new CutXf(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20, a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21, a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22, a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03,
            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20, a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21, a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22, a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13,
            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20, a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21, a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22, a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23);

        public CutVec Point(CutVec v) => new CutVec(
            M00 * v.X + M01 * v.Y + M02 * v.Z + M03,
            M10 * v.X + M11 * v.Y + M12 * v.Z + M13,
            M20 * v.X + M21 * v.Y + M22 * v.Z + M23);

        public CutVec Point(double x, double y, double z) => new CutVec(
            M00 * x + M01 * y + M02 * z + M03,
            M10 * x + M11 * y + M12 * z + M13,
            M20 * x + M21 * y + M22 * z + M23);

        public CutVec Direction(CutVec v) => new CutVec(M00 * v.X + M01 * v.Y + M02 * v.Z, M10 * v.X + M11 * v.Y + M12 * v.Z, M20 * v.X + M21 * v.Y + M22 * v.Z);

        public CutVec Translation => new CutVec(M03, M13, M23);
        /// <summary>The length of each column (the scale of a matrix without shear).</summary>
        public CutVec ColumnScale => new CutVec(new CutVec(M00, M10, M20).Length, new CutVec(M01, M11, M21).Length, new CutVec(M02, M12, M22).Length);

        public double Determinant => M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20);

        /// <summary>The inverse of the affine matrix (identity when singular).</summary>
        public CutXf Inverse()
        {
            double det = Determinant;
            if (det == 0 || double.IsNaN(det)) return Identity;
            double id = 1 / det;
            double a00 = (M11 * M22 - M12 * M21) * id, a01 = (M02 * M21 - M01 * M22) * id, a02 = (M01 * M12 - M02 * M11) * id;
            double a10 = (M12 * M20 - M10 * M22) * id, a11 = (M00 * M22 - M02 * M20) * id, a12 = (M02 * M10 - M00 * M12) * id;
            double a20 = (M10 * M21 - M11 * M20) * id, a21 = (M01 * M20 - M00 * M21) * id, a22 = (M00 * M11 - M01 * M10) * id;
            return new CutXf(
                a00, a01, a02, -(a00 * M03 + a01 * M13 + a02 * M23),
                a10, a11, a12, -(a10 * M03 + a11 * M13 + a12 * M23),
                a20, a21, a22, -(a20 * M03 + a21 * M13 + a22 * M23));
        }
    }
}
