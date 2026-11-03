// PoseStickTests: the Unity-free parts of the generated pose thumbnails on a synthetic
// skeleton (no game data): the Euler convention, forward kinematics, the rest-data file, the view and its fit (steady
// scale, bounds fit for a lying figure, margins, bottom line), the raster (coverage, anti-aliasing ramp, straight alpha,
// colours by side, determinism), the PNG writer (chunks, CRCs, zlib, a decode back to the pixels) and the cache key.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class PoseStickTests
    {
        public static void Run(TestRunner t)
        {
            Euler(t);
            Skeleton(t);
            RestFile(t);
            View(t);
            Raster(t);
            Png(t);
            Key(t);
        }

        static bool Near(StickVec a, StickVec b, double eps = 1e-9) => (a - b).Length <= eps;

        static void Euler(TestRunner t)
        {
            t.Check(Near(StickQuat.Euler(90, 0, 0).Rotate(new StickVec(0, 1, 0)), new StickVec(0, 0, 1), 1e-12), "euler: X 90 turns up to forward");
            t.Check(Near(StickQuat.Euler(0, 90, 0).Rotate(new StickVec(0, 0, 1)), new StickVec(1, 0, 0), 1e-12), "euler: Y 90 turns forward to right");
            t.Check(Near(StickQuat.Euler(0, 0, 90).Rotate(new StickVec(1, 0, 0)), new StickVec(0, 1, 0), 1e-12), "euler: Z 90 turns right to up");
            // order: Z, then X, then Y (forward: X 90 → down, then Y leaves it)
            t.Check(Near(StickQuat.Euler(90, 90, 0).Rotate(new StickVec(0, 0, 1)), new StickVec(0, -1, 0), 1e-12), "euler: applied Z, X, Y");
            t.Check(Near(StickQuat.Euler(0, 90, 90).Rotate(new StickVec(1, 0, 0)), new StickVec(0, 1, 0), 1e-12), "euler: Z before Y");
        }

        // a made-up humanoid: 10 units tall, facing +Z, left side at -X (the engine's convention)
        internal static StickRest Synthetic()
        {
            var r = new StickRest { Sex = 1, Stamp = "synthetic" };
            float[] Q() => new float[] { 0, 0, 0, 1 };
            float[] S() => new float[] { 1, 1, 1 };
            int root = r.Add("cf_J_Root", -1, new float[] { 0, 0, 0 }, Q(), S());
            int hips = r.Add("cf_J_Hips", root, new float[] { 0, 5, 0 }, Q(), S());
            int kosi = r.Add("cf_J_Kosi01", hips, new float[] { 0, -0.2f, 0 }, Q(), S());
            int s1 = r.Add("cf_J_Spine01", hips, new float[] { 0, 0.6f, 0 }, Q(), S());
            int s2 = r.Add("cf_J_Spine02", s1, new float[] { 0, 0.8f, 0 }, Q(), S());
            int s3 = r.Add("cf_J_Spine03", s2, new float[] { 0, 0.8f, 0 }, Q(), S());
            int neck = r.Add("cf_J_Neck", s3, new float[] { 0, 0.9f, 0 }, Q(), S());
            r.Add("cf_J_Head", neck, new float[] { 0, 0.5f, 0 }, Q(), S());
            foreach (var side in new[] { "L", "R" })
            {
                float sx = side == "L" ? -1 : 1;
                int sh = r.Add("cf_J_Shoulder_" + side, s3, new float[] { 0.3f * sx, 0.7f, 0 }, Q(), S());
                int up = r.Add("cf_J_ArmUp00_" + side, sh, new float[] { 0.7f * sx, 0, 0 }, Q(), S());
                int low = r.Add("cf_J_ArmLow01_" + side, up, new float[] { 1.4f * sx, 0, 0 }, Q(), S());
                int hand = r.Add("cf_J_Hand_" + side, low, new float[] { 1.2f * sx, 0, 0 }, Q(), S());
                r.Add("cf_J_Hand_Middle01_" + side, hand, new float[] { 0.4f * sx, 0, 0 }, Q(), S());
                int leg = r.Add("cf_J_LegUp00_" + side, kosi, new float[] { 0.45f * sx, -0.2f, 0 }, Q(), S());
                int knee = r.Add("cf_J_LegLow01_" + side, leg, new float[] { 0, -2.2f, 0 }, Q(), S());
                int foot = r.Add("cf_J_Foot01_" + side, knee, new float[] { 0, -2.2f, 0 }, Q(), S());
                r.Add("cf_J_Toes01_" + side, foot, new float[] { 0, -0.3f, 0.7f }, Q(), S());
            }
            return r;
        }

        static void Skeleton(TestRunner t)
        {
            var sk = new StickSkeleton(Synthetic());
            t.Equal(0, sk.Missing.Count, "skeleton: every joint bone found");
            var p = sk.Evaluate(null);
            t.Check(p.Complete && p.Finite, "skeleton: rest figure complete and finite");
            t.Check(Near(p.P[StickJoints.HandL], new StickVec(-(0.3 + 0.7 + 1.4 + 1.2), 5 + 0.6 + 0.8 + 0.8 + 0.7, 0), 1e-6), "skeleton: the left hand at the sum of the offsets (-X)");
            t.Check(Math.Abs(p.HeadUnit - 0.5) < 1e-6, "skeleton: head unit = neck → head at rest");
            t.Check(Math.Abs(p.P[StickJoints.HeadCentre].Y - (p.P[StickJoints.Head].Y + 0.5 * StickJoints.HeadCentreLocal.Y)) < 1e-6, "skeleton: the head centre above the head bone");
            t.Check(p.RestHeight > 9 && p.RestHeight < 11, $"skeleton: rest height {p.RestHeight:0.###}");
            // raise the left upper arm: Z -90 turns -X to +Y
            var up = sk.Evaluate(new Dictionary<string, StickVec> { ["cf_J_ArmUp00_L"] = new StickVec(0, 0, -90) });
            var shoulder = up.P[StickJoints.ArmUpL];
            t.Check(Near(up.P[StickJoints.HandL], shoulder + new StickVec(0, 2.6, 0), 1e-6), "skeleton: an override turns the chain below it");
            t.Check(Near(up.P[StickJoints.HandR], p.P[StickJoints.HandR], 1e-12), "skeleton: the other side keeps its rest");
            // scale of a parent scales the child offsets (world = parent · TRS)
            var r2 = Synthetic();
            r2.Scale[1] = new float[] { 2, 2, 2 };
            var big = new StickSkeleton(r2).Evaluate(null);
            t.Check(Math.Abs((big.P[StickJoints.Spine01] - big.P[StickJoints.Hips]).Length - 1.2) < 1e-6, "skeleton: a parent's scale scales its children's offsets");
        }

        static void RestFile(TestRunner t)
        {
            var r = Synthetic();
            r.Pos[3][0] = 0.1f + 1e-7f;   // a float that needs every digit
            string text = r.ToText();
            var back = StickRest.Parse(text, out string problem);
            t.Check(back != null && problem == null, "rest file: parses " + problem);
            t.Equal(text, back?.ToText(), "rest file: round trip");
            t.Equal("synthetic", back?.Stamp, "rest file: stamp");
            t.Check(StickRest.Parse("#hs2next-stick-rig\t2\n", out problem) == null && problem != null, "rest file: another version is refused");
            t.Check(StickRest.Parse(text.Replace("cf_J_Hips\t0\t", "cf_J_Hips\t9\t"), out problem) == null, "rest file: a parent after its child is refused");
            string file = Path.Combine(t.TempDir, "stick", "rig_f.tsv");
            t.Check(r.Write(file) == null && StickRest.Read(file, out _)?.ToText() == text, "rest file: write and read");
            t.Check(StickRest.Read(Path.Combine(t.TempDir, "none.tsv"), out problem) == null && problem == "missing", "rest file: missing");
        }

        static StickPose Lying()
        {
            // the whole figure turned 90° about Z (lying on its side) by an override of the hips
            return new StickSkeleton(Synthetic()).Evaluate(new Dictionary<string, StickVec> { ["cf_J_Hips"] = new StickVec(0, 0, 90) });
        }

        static void View(TestRunner t)
        {
            const int n = 256;
            var p = new StickSkeleton(Synthetic()).Evaluate(null);
            var pr = StickView.Fit(p, n, n);
            double m = StickView.Margin * n + StickStyle.Default.PadPx(n);
            t.Check(!pr.FitToBounds, "view: a standing figure keeps the steady scale");
            t.Check(Math.Abs(pr.MinY - m) < 1e-6, $"view: a standing figure stands on the bottom margin ({pr.MinY:0.##} / {m:0.##})");
            t.Check(pr.MaxY <= n - m + 1e-6, $"view: the head inside the top margin ({pr.MaxY:0.##})");
            t.Check(Math.Abs((pr.MinX + pr.MaxX) * 0.5 - n * 0.5) < 1e-6, "view: centred horizontally");
            // left side toward the viewer and on the right of the image (a front view mirrors)
            t.Check(pr.X[StickJoints.HandL] > pr.X[StickJoints.HandR], "view: the figure's left hand on the image's right (front view)");
            t.Check(pr.D[StickJoints.HandR] > pr.D[StickJoints.HandL], "view: the turn brings the right side nearer");
            var crouch = new StickSkeleton(Synthetic()).Evaluate(new Dictionary<string, StickVec>
            {
                ["cf_J_LegUp00_L"] = new StickVec(-90, 0, 0), ["cf_J_LegUp00_R"] = new StickVec(-90, 0, 0),
                ["cf_J_LegLow01_L"] = new StickVec(90, 0, 0), ["cf_J_LegLow01_R"] = new StickVec(90, 0, 0),
            });
            var pc = StickView.Fit(crouch, n, n);
            t.Check(!pc.FitToBounds && Math.Abs(pc.Scale - pr.Scale) < 1e-9, "view: a shorter pose keeps the same scale (steady)");
            var pl = StickView.Fit(Lying(), n, n);
            t.Check(pl.MinX >= m - 1e-6 && pl.MaxX <= n - m + 1e-6 && pl.MinY >= m - 1e-6 && pl.MaxY <= n - m + 1e-6, $"view: a lying figure inside the margins ({pl.MinX:0.#}–{pl.MaxX:0.#} × {pl.MinY:0.#}–{pl.MaxY:0.#})");
            t.Check(pl.Scale <= pr.Scale + 1e-9, "view: the fit never enlarges beyond the steady scale");
            // both arms raised: taller than the rest figure, so the bounds decide
            var raised = new StickSkeleton(Synthetic()).Evaluate(new Dictionary<string, StickVec> { ["cf_J_ArmUp00_L"] = new StickVec(0, 0, -90), ["cf_J_ArmUp00_R"] = new StickVec(0, 0, 90) });
            var pu = StickView.Fit(raised, n, n);
            t.Check(pu.FitToBounds && pu.Scale < pr.Scale && pu.MaxY <= n - m + 1e-6 && pu.MinY >= m - 1e-6, $"view: raised arms are fitted to the bounds ({pu.MinY:0.#}–{pu.MaxY:0.#}, scale {pu.Scale:0.##} / {pr.Scale:0.##})");
            var empty = StickView.Fit(new StickPose(), n, n);
            t.Check(empty.Scale == 0, "view: no joints, no scale");
        }

        static void Raster(TestRunner t)
        {
            var r = new StickRaster(32, 32);
            var white = new byte[] { 255, 255, 255, 255 };
            r.Capsule(8, 16, 24, 16, 3.3, white);
            r.Resolve();
            int A(int x, int y) => r.Pixels[(y * 32 + x) * 4 + 3];
            t.Equal(255, A(16, 15), "raster: inside a capsule opaque");
            t.Equal(0, A(16, 25), "raster: far outside transparent");
            // pixel centre 18.5 lies 2.5 from the axis (inside), 19.5 lies 3.5 (0.3 of the one-pixel ramp left)
            t.Check(A(16, 18) == 255 && Math.Abs(A(16, 19) - 77) <= 1 && A(16, 20) == 0, $"raster: an anti-aliased edge ({A(16, 18)}, {A(16, 19)}, {A(16, 20)})");
            int endWant = (int)Math.Round((3.3 + 0.5 - Math.Sqrt(3.5 * 3.5 + 0.5 * 0.5)) * 255);   // pixel centre (4.5, 16.5) from the end (8, 16)
            t.Check(Math.Abs(A(4, 16) - endWant) <= 1 && A(5, 16) == 255, $"raster: a round end ({A(4, 16)} / {endWant}, {A(5, 16)})");
            t.Equal(255, (int)r.Pixels[(15 * 32 + 16) * 4], "raster: straight alpha keeps the colour");
            // a figure: colours by side, inside the image, the same bytes twice
            var p = new StickSkeleton(Synthetic()).Evaluate(null);
            var a = StickDrawing.Draw(p, 256, 256);
            var b = StickDrawing.Draw(p, 256, 256);
            t.Check(a != null && a.Pixels.SequenceEqual(b.Pixels), "raster: deterministic");
            int cov = a.Covered(8, out int x0, out int y0, out int x1, out int y1);
            int m = (int)(StickView.Margin * 256) - 1;
            t.Check(cov > 1000 && x0 >= m && y0 >= m && x1 < 256 - m && y1 < 256 - m, $"raster: the figure inside the margin ({cov} px, {x0}–{x1} × {y0}–{y1})");
            int left = 0, right = 0, centre = 0;
            for (int i = 0; i < a.Pixels.Length; i += 4)
            {
                if (a.Pixels[i + 3] < 250) continue;
                int r0 = a.Pixels[i], b0 = a.Pixels[i + 2];
                if (b0 > r0 + 60) left++; else if (r0 > b0 + 60) right++; else if (Math.Abs(r0 - b0) < 20 && r0 > 120) centre++;
            }
            t.Check(left > 100 && right > 100 && centre > 100, $"raster: left, right and centre colours drawn ({left} / {right} / {centre} px)");
            t.Check(a.Pixels[3] == 0, "raster: transparent background");
        }

        static uint Be(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        static void Png(TestRunner t)
        {
            var img = StickDrawing.Draw(new StickSkeleton(Synthetic()).Evaluate(null), 64, 48);
            var png = StickPng.Encode(img.Pixels, img.Width, img.Height);
            t.Check(png.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "png: signature");
            int o = 8;
            var chunks = new List<string>();
            byte[] idat = null;
            bool crcOk = true;
            while (o + 12 <= png.Length)
            {
                int len = (int)Be(png, o);
                string type = new string(new[] { (char)png[o + 4], (char)png[o + 5], (char)png[o + 6], (char)png[o + 7] });
                uint crc = StickPng.Crc(png, o + 4, 4 + len) ^ 0xFFFFFFFFu;
                if (crc != Be(png, o + 8 + len)) crcOk = false;
                if (type == "IHDR")
                {
                    t.Check(Be(png, o + 8) == 64 && Be(png, o + 12) == 48 && png[o + 16] == 8 && png[o + 17] == 6, "png: IHDR 64 × 48, 8-bit RGBA");
                }
                if (type == "IDAT") { idat = new byte[len]; Buffer.BlockCopy(png, o + 8, idat, 0, len); }
                chunks.Add(type);
                o += 12 + len;
            }
            t.Equal("IHDR,IDAT,IEND", string.Join(",", chunks), "png: chunks");
            t.Check(crcOk, "png: chunk CRCs");
            t.Equal(png.Length, o, "png: nothing after IEND");
            // inflate and undo the Sub filter: the pixels come back (rows top first)
            byte[] raw;
            using (var ms = new MemoryStream(idat, 2, idat.Length - 6))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var outp = new MemoryStream()) { ds.CopyTo(outp); raw = outp.ToArray(); }
            uint a1 = 1, b1 = 0;
            foreach (var x in raw) { a1 = (a1 + x) % 65521; b1 = (b1 + a1) % 65521; }
            t.Check(((b1 << 16) | a1) == Be(idat, idat.Length - 4), "png: zlib adler-32");
            int stride = 64 * 4, differ = 0;
            for (int row = 0; row < 48; row++)
            {
                int src = row * (stride + 1);
                if (raw[src] != 1) differ++;
                int dst = (47 - row) * stride;
                for (int i = 0; i < stride; i++)
                {
                    byte v = (byte)(raw[src + 1 + i] + (i >= 4 ? img.Pixels[dst + i - 4] : 0));
                    if (v != img.Pixels[dst + i]) differ++;
                }
            }
            t.Equal(0, differ, "png: decodes to the pixels (bottom-up rows written top first)");
            t.Check(StickPng.Encode(img.Pixels, 64, 48).SequenceEqual(png), "png: deterministic");
        }

        static void Key(TestRunner t)
        {
            string k = PoseThumbKey.KeyText("pose/a.dat", 100, 5, 256, 256, "s");
            t.Equal(16, PoseThumbKey.Hash(k).Length, "key: 16 hex digits");
            t.Equal(PoseThumbKey.Hash(k), PoseThumbKey.Hash(PoseThumbKey.KeyText("Pose/A.DAT", 100, 5, 256, 256, "s")), "key: the path ignores case");
            var others = new[]
            {
                PoseThumbKey.KeyText("pose/a.dat", 101, 5, 256, 256, "s"), PoseThumbKey.KeyText("pose/a.dat", 100, 6, 256, 256, "s"),
                PoseThumbKey.KeyText("pose/a.dat", 100, 5, 128, 128, "s"), PoseThumbKey.KeyText("pose/a.dat", 100, 5, 256, 256, "t"),
                PoseThumbKey.KeyText("pose/b.dat", 100, 5, 256, 256, "s"),
            };
            t.Check(others.All(x => PoseThumbKey.Hash(x) != PoseThumbKey.Hash(k)), "key: size, time, image size, rig stamp and path change it");
            t.Check(k.Contains("|d" + StickDrawing.Version + "|"), "key: the drawing version is part of it");
            t.Equal("cbf29ce484222325", PoseThumbKey.Hash(""), "key: FNV-1a 64 of the empty text");
            string f = PoseThumbKey.FileOf("cache", k);
            t.Check(f != null && f.EndsWith(PoseThumbKey.Hash(k) + ".png") && f.Replace('\\', '/').Contains("/poses/stick/"), "key: the file under poses/stick");
            t.Check(PoseThumbKey.FileOf(null, k) == null, "key: no cache folder, no file");
        }
    }
}
