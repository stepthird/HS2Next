// Stick-figure thumbnails of pose files: the fixed view, the fit, the CPU raster and the PNG writer.
// Unity-free (System only); every step is deterministic (same figure → same bytes).
//   view    orthographic, the figure turned by Yaw about the vertical (front with a slight turn: the figure's right side
//           comes toward the viewer) and seen from Pitch above
//   fit     a steady scale per sex (the rest figure's height fills the image less the margin) unless the figure's bounds
//           do not fit at that scale (lying, arms raised, wide stances): then the bounds fit; centred horizontally, the
//           lowest point on the bottom margin's line when the figure is shorter than the image (a standing figure stands
//           on the same line in every cell), else centred
//   raster  straight-alpha RGBA32, rows bottom-up (the engine's texture order), transparent background; every segment a
//           capsule (round ends) with an analytic one-pixel anti-aliasing ramp, drawn far to near; each with a dark halo
//           first, so a near limb cuts a thin gap across a far one and the lines read on light and dark backgrounds; the
//           head a disc with a face mark when the face turns toward the viewer; left and right limbs in two colours, the
//           spine and head neutral; far parts dimmer and thinner (depth cue); a faint ground line under the feet
//   png     8-bit RGBA, filter Sub per row, zlib (deflate) in one IDAT
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace HS2Next.UI.Core
{
    /// <summary>A projected figure: pixel coordinates (y up), depth toward the viewer, the scale used.</summary>
    public sealed class StickProjection
    {
        public readonly double[] X = new double[StickJoints.Count], Y = new double[StickJoints.Count], D = new double[StickJoints.Count];
        public readonly bool[] Has = new bool[StickJoints.Count];
        /// <summary>Pixels per skeleton unit.</summary>
        public double Scale;
        /// <summary>The head radius in pixels.</summary>
        public double HeadRadius;
        /// <summary>The figure's depth range (min, max) toward the viewer.</summary>
        public double DepthMin, DepthMax;
        /// <summary>The scale was reduced to fit the bounds (not the steady scale).</summary>
        public bool FitToBounds;
        /// <summary>The y of the ground line (the lowest foot point), pixels.</summary>
        public double Ground;
        /// <summary>Bounds of the drawn figure (pixels, before the margin).</summary>
        public double MinX, MaxX, MinY, MaxY;
    }

    public static class StickView
    {
        /// <summary>The figure's turn about the vertical, degrees (positive: its right side toward the viewer).</summary>
        public const double Yaw = 25;
        /// <summary>The view from above, degrees.</summary>
        public const double Pitch = 8;
        /// <summary>The margin on every side, as a fraction of the image size.</summary>
        public const double Margin = 0.07;
        /// <summary>The steady scale fits this many rest heights into the image's height (room for the view's tilt and the head
        /// circle, so a figure standing at rest keeps the steady scale).</summary>
        public const double StandHeadroom = 1.04;

        /// <summary>Projects and fits a figure into a <paramref name="w"/> × <paramref name="h"/> image.</summary>
        public static StickProjection Fit(StickPose pose, int w, int h) => Fit(pose, w, h, Yaw, Pitch, Margin, StickStyle.Default.PadPx(h));

        /// <param name="padPx">Pixels kept free inside the margin for the line widths (the joints are the bounds).</param>
        public static StickProjection Fit(StickPose pose, int w, int h, double yawDeg, double pitchDeg, double margin, double padPx)
        {
            var pr = new StickProjection();
            double cy = Math.Cos(yawDeg * Math.PI / 180), sy = Math.Sin(yawDeg * Math.PI / 180);
            double cp = Math.Cos(pitchDeg * Math.PI / 180), sp = Math.Sin(pitchDeg * Math.PI / 180);
            // camera on the +Z side looking toward -Z: screen right = the figure's -X (its right side on screen left, a mirror);
            // the yaw turns the figure about Y, the pitch tilts the view down
            var sx = new double[StickJoints.Count]; var syv = new double[StickJoints.Count]; var d = new double[StickJoints.Count];
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            double dMin = double.MaxValue, dMax = double.MinValue;
            for (int j = 0; j < StickJoints.Count; j++)
            {
                if (!pose.Has[j]) continue;
                var p = pose.P[j];
                // turn about Y by yaw (the right side, +X, toward the viewer at +Z for a positive yaw)
                double x1 = p.X * cy - p.Z * sy, z1 = p.X * sy + p.Z * cy;
                // tilt: view from above
                double y2 = p.Y * cp - z1 * sp, z2 = p.Y * sp + z1 * cp;
                sx[j] = -x1; syv[j] = y2; d[j] = z2;
                pr.Has[j] = true;
                if (j == StickJoints.Face) continue;   // the face mark lies inside the head circle
                minX = Math.Min(minX, sx[j]); maxX = Math.Max(maxX, sx[j]);
                minY = Math.Min(minY, syv[j]); maxY = Math.Max(maxY, syv[j]);
                dMin = Math.Min(dMin, d[j]); dMax = Math.Max(dMax, d[j]);
            }
            if (minX > maxX) return pr;
            double r = StickJoints.HeadRadius * Math.Max(pose.HeadUnit, 1e-6);
            if (pr.Has[StickJoints.HeadCentre])
            {
                int c = StickJoints.HeadCentre;
                minX = Math.Min(minX, sx[c] - r); maxX = Math.Max(maxX, sx[c] + r);
                minY = Math.Min(minY, syv[c] - r); maxY = Math.Max(maxY, syv[c] + r);
            }
            double bw = Math.Max(maxX - minX, 1e-6), bh = Math.Max(maxY - minY, 1e-6);
            double availW = Math.Max(1, w * (1 - 2 * margin) - 2 * padPx), availH = Math.Max(1, h * (1 - 2 * margin) - 2 * padPx);
            double steady = pose.RestHeight > 0 ? availH / (pose.RestHeight * StandHeadroom) : double.MaxValue;
            double fit = Math.Min(availW / bw, availH / bh);
            double s = Math.Min(steady, fit);
            pr.FitToBounds = fit < steady;
            pr.Scale = s;
            pr.HeadRadius = r * s;
            double ox = w * 0.5 - (minX + maxX) * 0.5 * s;
            double figH = bh * s;
            double bottom = h * margin + padPx;
            double oy = figH < availH - 1e-9 ? bottom - minY * s : h * 0.5 - (minY + maxY) * 0.5 * s;
            pr.MinX = minX * s + ox; pr.MaxX = maxX * s + ox; pr.MinY = minY * s + oy; pr.MaxY = maxY * s + oy;
            double ground = double.MaxValue;
            for (int j = 0; j < StickJoints.Count; j++)
            {
                if (!pr.Has[j]) continue;
                pr.X[j] = sx[j] * s + ox;
                pr.Y[j] = syv[j] * s + oy;
                pr.D[j] = d[j] * s;
                if (j == StickJoints.FootL || j == StickJoints.FootR || j == StickJoints.ToesL || j == StickJoints.ToesR) ground = Math.Min(ground, pr.Y[j]);
            }
            pr.Ground = ground == double.MaxValue ? pr.MinY : ground;
            pr.DepthMin = dMin * s; pr.DepthMax = dMax * s;
            return pr;
        }
    }

    /// <summary>Colours and widths of the drawing (straight RGBA 0–255; widths as fractions of the image height).</summary>
    public sealed class StickStyle
    {
        public byte[] Centre = { 222, 225, 232, 255 }, Left = { 77, 163, 255, 255 }, Right = { 255, 146, 64, 255 };
        public byte[] Halo = { 16, 18, 22, 150 }, Ground = { 150, 155, 165, 70 }, FaceMark = { 34, 37, 44, 255 };
        /// <summary>Line widths (diameters) of a limb, the spine, a girdle and an end segment.</summary>
        public double Limb = 0.034, Spine = 0.034, Girdle = 0.024, End = 0.028;
        /// <summary>The halo's width beyond a line on each side, pixels.</summary>
        public double HaloPx = 1.4;
        /// <summary>The farthest parts: width factor and the brightness kept (the depth cue).</summary>
        public double FarWidth = 0.78, FarBright = 0.58;
        /// <summary>Draw the ground line.</summary>
        public bool DrawGround = true;

        /// <summary>The widest line's half width with its halo and the anti-aliasing pixel (the fit keeps it inside the margin).</summary>
        public double PadPx(int h) => Math.Max(Math.Max(Limb, Spine), Math.Max(Girdle, End)) * h * 0.5 + HaloPx + 1;

        public static readonly StickStyle Default = new StickStyle();
    }

    /// <summary>An RGBA32 image (straight alpha, rows bottom-up) and the drawing primitives.</summary>
    public sealed class StickRaster
    {
        public readonly int Width, Height;
        /// <summary>RGBA32, row 0 = the bottom row.</summary>
        public readonly byte[] Pixels;
        // premultiplied accumulation in floats
        readonly float[] acc;

        public StickRaster(int w, int h)
        {
            Width = w; Height = h;
            Pixels = new byte[w * h * 4];
            acc = new float[w * h * 4];
        }

        /// <summary>Blends a capsule from (x0, y0) to (x1, y1) with radius <paramref name="r"/> (pixel centres at +0.5).</summary>
        public void Capsule(double x0, double y0, double x1, double y1, double r, byte[] rgba, double alpha = 1)
        {
            if (r <= 0) return;
            int xa = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - r - 1)), xb = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(x0, x1) + r + 1));
            int ya = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - r - 1)), yb = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + r + 1));
            double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
            float cr = rgba[0] / 255f, cg = rgba[1] / 255f, cb = rgba[2] / 255f;
            double ca = rgba[3] / 255.0 * alpha;
            for (int y = ya; y <= yb; y++)
            {
                double py = y + 0.5;
                for (int x = xa; x <= xb; x++)
                {
                    double px = x + 0.5;
                    double t = len2 > 0 ? ((px - x0) * dx + (py - y0) * dy) / len2 : 0;
                    if (t < 0) t = 0; else if (t > 1) t = 1;
                    double ex = px - (x0 + t * dx), ey = py - (y0 + t * dy);
                    double dist = Math.Sqrt(ex * ex + ey * ey);
                    double cov = r + 0.5 - dist;
                    if (cov <= 0) continue;
                    if (cov > 1) cov = 1;
                    Blend(x, y, cr, cg, cb, (float)(cov * ca));
                }
            }
        }

        /// <summary>Blends a disc.</summary>
        public void Disc(double cx, double cy, double r, byte[] rgba, double alpha = 1) => Capsule(cx, cy, cx, cy, r, rgba, alpha);

        void Blend(int x, int y, float r, float g, float b, float a)
        {
            int i = (y * Width + x) * 4;
            float k = 1 - a;
            acc[i] = r * a + acc[i] * k;
            acc[i + 1] = g * a + acc[i + 1] * k;
            acc[i + 2] = b * a + acc[i + 2] * k;
            acc[i + 3] = a + acc[i + 3] * k;
        }

        /// <summary>Writes the accumulated image into <see cref="Pixels"/> (straight alpha, rounded).</summary>
        public void Resolve()
        {
            for (int i = 0; i < acc.Length; i += 4)
            {
                float a = acc[i + 3];
                if (a <= 0) { Pixels[i] = Pixels[i + 1] = Pixels[i + 2] = Pixels[i + 3] = 0; continue; }
                Pixels[i] = Byte(acc[i] / a);
                Pixels[i + 1] = Byte(acc[i + 1] / a);
                Pixels[i + 2] = Byte(acc[i + 2] / a);
                Pixels[i + 3] = Byte(a);
            }
        }

        static byte Byte(float v) => v <= 0 ? (byte)0 : v >= 1 ? (byte)255 : (byte)(v * 255f + 0.5f);

        /// <summary>Pixels with alpha above <paramref name="threshold"/> and their bounds (tests: the figure lies inside).</summary>
        public int Covered(byte threshold, out int minX, out int minY, out int maxX, out int maxY)
        {
            int n = 0; minX = minY = int.MaxValue; maxX = maxY = int.MinValue;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (Pixels[(y * Width + x) * 4 + 3] <= threshold) continue;
                    n++;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            return n;
        }
    }

    public static class StickDrawing
    {
        /// <summary>The drawing's version (part of the cache key: a change of the view or the style makes new files).</summary>
        public const int Version = 1;

        struct Item { public double Depth; public int Chain; }

        /// <summary>Draws a figure into a new <paramref name="w"/> × <paramref name="h"/> image (null when it has no joints).</summary>
        public static StickRaster Draw(StickPose pose, int w, int h, StickStyle style = null)
        {
            style = style ?? StickStyle.Default;
            var pr = StickView.Fit(pose, w, h, StickView.Yaw, StickView.Pitch, StickView.Margin, style.PadPx(h));
            if (pr.Scale <= 0) return null;
            var img = new StickRaster(w, h);
            double depthRange = Math.Max(pr.DepthMax - pr.DepthMin, 1e-6);
            double mid = (pr.DepthMax + pr.DepthMin) * 0.5;
            // the ground first (behind everything), under the lowest foot
            if (style.DrawGround)
            {
                double gy = pr.Ground - style.End * h * 0.5 - 1;
                double half = Math.Max(pr.MaxX - pr.MinX, h * 0.25) * 0.5 + h * 0.04;
                double cx = (pr.MinX + pr.MaxX) * 0.5;
                img.Capsule(cx - half, gy, cx + half, gy, Math.Max(0.6, h * 0.004), style.Ground);
            }
            // the chains and the head, far to near (a chain's depth: its limb segments; the spine: all of it); ties keep the
            // chain order
            var items = new List<Item>(StickSegments.ChainCount + 1);
            var sum = new double[StickSegments.ChainCount];
            var cnt = new int[StickSegments.ChainCount];
            foreach (var sg in StickSegments.All)
            {
                if (!pr.Has[sg.A] || !pr.Has[sg.B]) continue;
                if (sg.Part == StickPart.Girdle && sg.Chain != StickSegments.Spine) continue;
                sum[sg.Chain] += (pr.D[sg.A] + pr.D[sg.B]) * 0.5;
                cnt[sg.Chain]++;
            }
            for (int c = 0; c < StickSegments.ChainCount; c++) if (cnt[c] > 0) items.Add(new Item { Depth = sum[c] / cnt[c], Chain = c });
            if (pr.Has[StickJoints.HeadCentre]) items.Add(new Item { Depth = pr.D[StickJoints.HeadCentre], Chain = -1 });
            items.Sort((a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Chain.CompareTo(b.Chain));
            foreach (var it in items)
            {
                if (it.Chain < 0) { Head(img, pr, style, Far(pr.D[StickJoints.HeadCentre], mid, depthRange)); continue; }
                // the whole chain's halo, then its lines: one continuous line, cut only by chains in front of it
                for (int pass = 0; pass < 2; pass++)
                    foreach (var sg in StickSegments.All)
                    {
                        if (sg.Chain != it.Chain || !pr.Has[sg.A] || !pr.Has[sg.B]) continue;
                        double far = Far((pr.D[sg.A] + pr.D[sg.B]) * 0.5, mid, depthRange);
                        double width = sg.Part == StickPart.Limb ? style.Limb : sg.Part == StickPart.Spine ? style.Spine : sg.Part == StickPart.Girdle ? style.Girdle : style.End;
                        double rad = width * h * 0.5 * (1 - (1 - style.FarWidth) * far);
                        if (pass == 0) img.Capsule(pr.X[sg.A], pr.Y[sg.A], pr.X[sg.B], pr.Y[sg.B], rad + style.HaloPx, style.Halo);
                        else
                        {
                            var col = sg.Side == StickSide.Left ? style.Left : sg.Side == StickSide.Right ? style.Right : style.Centre;
                            img.Capsule(pr.X[sg.A], pr.Y[sg.A], pr.X[sg.B], pr.Y[sg.B], rad, Dim(col, 1 - (1 - style.FarBright) * far));
                        }
                    }
            }
            img.Resolve();
            return img;
        }

        // the depth cue: 0 in front of the figure's middle plane, up to 1 at its farthest point
        static double Far(double depth, double mid, double range) => Math.Max(0, Math.Min(1, (mid - depth) / (range * 0.5)));

        // the head: a disc; the face mark where the face points, shown as the face turns toward the viewer
        static void Head(StickRaster img, StickProjection pr, StickStyle style, double far)
        {
            int c = StickJoints.HeadCentre, f = StickJoints.Face;
            double r = pr.HeadRadius;
            img.Disc(pr.X[c], pr.Y[c], r + style.HaloPx, style.Halo);
            img.Disc(pr.X[c], pr.Y[c], r, Dim(style.Centre, 1 - (1 - style.FarBright) * far));
            if (!pr.Has[f]) return;
            double toward = (pr.D[f] - pr.D[c]) / Math.Max(r, 1e-6);
            if (toward <= 0.05) return;
            double fx = pr.X[c] + (pr.X[f] - pr.X[c]) * 0.85, fy = pr.Y[c] + (pr.Y[f] - pr.Y[c]) * 0.85;
            img.Disc(fx, fy, r * 0.24, style.FaceMark, Math.Min(1, toward * 1.6));
        }

        static byte[] Dim(byte[] c, double k) => k >= 1 ? c : new[] { (byte)(c[0] * k + 0.5), (byte)(c[1] * k + 0.5), (byte)(c[2] * k + 0.5), c[3] };
    }

    /// <summary>A minimal PNG writer: 8-bit RGBA, filter Sub, one zlib IDAT. Input rows bottom-up (written top first).</summary>
    public static class StickPng
    {
        static readonly uint[] CrcTable = MakeCrcTable();

        static uint[] MakeCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        public static uint Crc(byte[] b, int offset, int count, uint crc = 0xFFFFFFFFu)
        {
            for (int i = 0; i < count; i++) crc = CrcTable[(crc ^ b[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        public static byte[] Encode(byte[] rgba, int w, int h, bool bottomUp = true)
        {
            if (rgba == null || rgba.Length < w * h * 4) throw new ArgumentException("pixels");
            int stride = w * 4;
            var raw = new byte[(stride + 1) * h];
            for (int row = 0; row < h; row++)
            {
                int src = (bottomUp ? h - 1 - row : row) * stride, dst = row * (stride + 1);
                raw[dst] = 1;   // Sub
                for (int i = 0; i < stride; i++)
                    raw[dst + 1 + i] = (byte)(rgba[src + i] - (i >= 4 ? rgba[src + i - 4] : 0));
            }
            byte[] z;
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);
                using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
                uint a = 1, b = 0;
                foreach (var x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                z = ms.ToArray();
            }
            using (var o = new MemoryStream())
            {
                o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13];
                Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h);
                ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
                Chunk(o, "IHDR", ihdr);
                Chunk(o, "IDAT", z);
                Chunk(o, "IEND", new byte[0]);
                return o.ToArray();
            }
        }

        static void Be(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; Be(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            var td = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            var crc = new byte[4]; Be(crc, 0, Crc(td, 0, td.Length) ^ 0xFFFFFFFFu);
            s.Write(crc, 0, 4);
        }
    }

    /// <summary>The disk cache key of a generated pose thumbnail.</summary>
    public static class PoseThumbKey
    {
        /// <summary>FNV-1a 64 of the text as 16 hex digits.</summary>
        public static string Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            if (s != null) foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x16");
        }

        /// <summary>The key text: the pose file (path, size, modification time), the image size, the drawing's version
        /// and the rig's stamp (another install or skeleton → other files).</summary>
        public static string KeyText(string path, long size, long ticksUtc, int w, int h, string rigStamp) =>
            (path ?? "").ToLowerInvariant() + "|" + size + "|" + ticksUtc + "|" + w + "x" + h + "|d" + StickDrawing.Version + "|" + (rigStamp ?? "");

        /// <summary>The PNG of a key under a cache folder: `poses/stick/&lt;hash&gt;.png` (null without a cache folder).</summary>
        public static string FileOf(string cacheDir, string keyText) =>
            string.IsNullOrEmpty(cacheDir) ? null : Path.Combine(cacheDir, "poses", "stick", Hash(keyText) + ".png");
    }
}
