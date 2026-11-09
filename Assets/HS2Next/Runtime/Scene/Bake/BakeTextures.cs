// Chara to Item: textures. Every texture is stored as texels, every level of its
// own chain, lossless (no re-compression):
//   a render texture (the skin and clothes composites, overlays, stains) and an uncompressed four-channel Texture2D: read back by
//     AsyncGPUReadback in their own format, bit-exact, every level requested before the first is waited for;
//   a three-channel Texture2D (RGB24): read back and stored as four channels with alpha 255, the sRGB flag kept;
//   a compressed Texture2D (DXT1, DXT5, BC7, …): each level copied into a same-format texture flagged linear, drawn into an 8-bit
//     linear target, read back, stored as R8G8B8A8 with the sRGB flag of Texture.isDataSRGB (the texels as sampled); levels down to
//     the last whole block, the smaller levels made from the last one by a 2 × 2 box (BC6H decodes into half floats); every level is
//     copied, drawn and requested before the first is waited for, the copies and targets released after the last;
//   a texture whose own format this device cannot build as a sampled texture: level 0 drawn as the engine samples it into an 8-bit
//     target of its sRGB flag, the smaller levels made from it by a 2 × 2 box;
//   cube, 3D and array textures: not stored (bake.textureKind).
// The capture hands each texture's levels to a sink at once and keeps none (one texture's texels at a time). The waits and the
// copies into managed arrays go in level order, so the levels and the findings are those of a read-back one level at a time, which
// BakeJob.Legacy (tests) still takes.
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Bake
{
    public static class BakeTextures
    {
        public const string RouteOwn = "readback", RouteThree = "readback+rgba", RouteDecode = "decode", RouteSampled = "sampled";
        /// <summary>Counts of a capture (tests): the levels read back from the device, and of them those requested and waited for one at a time
        /// (the old read-back, <see cref="BakeJob.Legacy"/>).</summary>
        public const string LevelsRead = "tex.levels.read", WaitsByLevel = "tex.waits.levelByLevel";

        /// <summary>
        /// One texture's levels (largest first) as stored; null for a texture that is not two-dimensional (finding) or cannot be read.
        /// <paramref name="route"/> names the route; <paramref name="mean"/> is the mean of a 16 × 16 sample of level 0 in
        /// linear rgb (for an inside's flat colour), or null when the format has no 8-bit colour.
        /// </summary>
        public static TexData Capture(Texture t, BakeCaptureResult res, out string route, out Vector3? mean)
        {
            route = null; mean = null;
            if (!t) return null;
            if (t.dimension != TextureDimension.Tex2D)
            {
                res?.Findings.Warning("bake.textureKind", t.name, "a " + t.dimension + " texture is not stored; the slot keeps none");
                res?.Inc("tex.notStored");
                return null;
            }
            var rt = t as RenderTexture;
            var fmt = rt ? rt.graphicsFormat : t.graphicsFormat;
            int mips = rt ? (rt.useMipMap ? Math.Max(1, rt.mipmapCount) : 1) : Math.Max(1, t.mipmapCount);
            bool srgb = t.isDataSRGB;
            var d = new TexData { W = t.width, H = t.height, Format = fmt, SourceFormat = fmt.ToString() };
            bool compressed = GraphicsFormatUtility.IsCompressedFormat(fmt);
            bool msaa = rt && rt.antiAliasing > 1;
            if (!compressed && !msaa)
            {
                if (ReadOwn(t, mips, d, res))
                {
                    if (Channels(fmt) == 3) { Expand3(d, srgb); route = RouteThree; }
                    else route = RouteOwn;
                    if (SystemInfo.IsFormatSupported(d.Format, GraphicsFormatUsage.Sample))
                    {
                        res?.Inc(route == RouteThree ? "tex.route.rgba" : "tex.route.own");
                        mean = Mean(d, srgb);
                        return d;
                    }
                    // a format this device cannot build as a sampled texture (the engine draws a converted copy): level 0 drawn as sampled,
                    // the smaller levels made from it
                    d.Levels.Clear();
                    d.Format = fmt;
                    if (!Sampled(t, mips, srgb, d, res)) return null;
                    route = RouteSampled;
                    res?.Inc("tex.route.sampled");
                    mean = Mean(d, srgb);
                    return d;
                }
                d.Levels.Clear();
            }
            if (!Decode(t, fmt, mips, srgb, d, res)) return null;
            route = RouteDecode;
            res?.Inc("tex.route.decode");
            mean = Mean(d, srgb);
            return d;
        }

        // ------------------------------------------------------------ read back in the own format

        /// <summary>Every level in its own format: all requested, then each waited for and copied in level order; false when one cannot be read.</summary>
        static bool ReadOwn(Texture t, int mips, TexData d, BakeCaptureResult res)
        {
            if (BakeJob.Legacy) return ReadOwnByLevel(t, mips, d, res);
            var rq = new AsyncGPUReadbackRequest[mips];
            int asked = 0;
            bool ok = true;
            try
            {
                for (; asked < mips; asked++) rq[asked] = AsyncGPUReadback.Request(t, asked);
            }
            catch (Exception) { ok = false; }
            // every request made is waited for, also after a failure
            for (int mip = 0; mip < asked; mip++)
            {
                try
                {
                    rq[mip].WaitForCompletion();
                    if (!ok) continue;
                    if (rq[mip].hasError) { ok = false; continue; }
                    d.Levels.Add(rq[mip].GetData<byte>().ToArray());
                }
                catch (Exception) { ok = false; }
            }
            if (!ok || d.Levels.Count != mips) return false;
            res?.Inc(LevelsRead, mips);
            return true;
        }

        /// <summary>The old read-back (tests, <see cref="BakeJob.Legacy"/>): each level requested and waited for before the next.</summary>
        static bool ReadOwnByLevel(Texture t, int mips, TexData d, BakeCaptureResult res)
        {
            for (int mip = 0; mip < mips; mip++)
            {
                try
                {
                    var rq = AsyncGPUReadback.Request(t, mip);
                    rq.WaitForCompletion();
                    res?.Inc(WaitsByLevel);
                    if (rq.hasError) return false;
                    d.Levels.Add(rq.GetData<byte>().ToArray());
                }
                catch (Exception) { return false; }
            }
            if (d.Levels.Count != mips) return false;
            res?.Inc(LevelsRead, mips);
            return true;
        }

        /// <summary>Colour channels of an uncompressed format (3 for the three-channel 8-bit formats), else 4.</summary>
        static int Channels(GraphicsFormat f)
        {
            switch (f)
            {
                case GraphicsFormat.R8G8B8_UNorm: case GraphicsFormat.R8G8B8_SRGB: case GraphicsFormat.B8G8R8_UNorm: case GraphicsFormat.B8G8R8_SRGB:
                    return 3;
                default: return 4;
            }
        }

        /// <summary>A three-channel texture as four channels, alpha 255 (a three-channel Texture2D cannot be built); a level the GPU already
        /// returned with four channels is kept with its alpha set.</summary>
        static void Expand3(TexData d, bool srgb)
        {
            bool bgr = d.Format == GraphicsFormat.B8G8R8_UNorm || d.Format == GraphicsFormat.B8G8R8_SRGB;
            for (int l = 0; l < d.Levels.Count; l++)
            {
                int w = Math.Max(1, d.W >> l), h = Math.Max(1, d.H >> l), n = w * h;
                var src = d.Levels[l];
                var dst = new byte[n * 4];
                if (src.Length >= n * 4)
                {
                    for (int i = 0; i < n; i++)
                    {
                        dst[4 * i] = src[4 * i + (bgr ? 2 : 0)]; dst[4 * i + 1] = src[4 * i + 1]; dst[4 * i + 2] = src[4 * i + (bgr ? 0 : 2)]; dst[4 * i + 3] = 255;
                    }
                }
                else
                {
                    for (int i = 0; i < n && 3 * i + 2 < src.Length; i++)
                    {
                        dst[4 * i] = src[3 * i + (bgr ? 2 : 0)]; dst[4 * i + 1] = src[3 * i + 1]; dst[4 * i + 2] = src[3 * i + (bgr ? 0 : 2)]; dst[4 * i + 3] = 255;
                    }
                }
                d.Levels[l] = dst;
            }
            d.Format = srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
        }

        // ------------------------------------------------------------ decoded (compressed or not readable)

        static bool IsHdr(GraphicsFormat f) =>
            f == GraphicsFormat.RGB_BC6H_UFloat || f == GraphicsFormat.RGB_BC6H_SFloat || GraphicsFormatUtility.IsHalfFormat(f) || GraphicsFormatUtility.IsIEEE754Format(f);

        /// <summary>Each level drawn into an 8-bit (or half for HDR) linear target through a linear-flagged copy of the same format, read back:
        /// every whole level copied, drawn and requested, then each waited for and copied in level order; the copies and targets released after
        /// the last wait. A failure gives the finding of the first level that failed.</summary>
        static bool Decode(Texture t, GraphicsFormat fmt, int mips, bool srgb, TexData d, BakeCaptureResult res)
        {
            if (BakeJob.Legacy) return DecodeByLevel(t, fmt, mips, srgb, d, res);
            bool hdr = IsHdr(fmt);
            var target = hdr ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
            var linFmt = GraphicsFormatUtility.GetLinearFormat(fmt);
            bool compressed = GraphicsFormatUtility.IsCompressedFormat(fmt);
            int bw = compressed ? (int)GraphicsFormatUtility.GetBlockWidth(fmt) : 1, bh = compressed ? (int)GraphicsFormatUtility.GetBlockHeight(fmt) : 1;
            bool msaa = t is RenderTexture mrt && mrt.antiAliasing > 1;
            var lins = new List<Texture2D>();
            var tmps = new List<RenderTexture>();
            var rqs = new List<AsyncGPUReadbackRequest>();
            int failedAt = -1;
            Exception failure = null;
            int whole = 0;
            try
            {
                for (int mip = 0; mip < mips; mip++)
                {
                    int w = Math.Max(1, t.width >> mip), h = Math.Max(1, t.height >> mip);
                    if (compressed && (w % bw != 0 || h % bh != 0)) break;
                    try
                    {
                        Texture src = t;
                        if (!msaa)
                        {
                            // the level as raw texels: a copy flagged linear, so the draw neither decodes nor encodes sRGB
                            var lin = new Texture2D(w, h, linFmt, TextureCreationFlags.None) { name = "bake_decode" };
                            lins.Add(lin);
                            Graphics.CopyTexture(t, 0, mip, lin, 0, 0);
                            src = lin;
                        }
                        var tmp = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, target, GraphicsFormat.None) { msaaSamples = 1, sRGB = false });
                        tmps.Add(tmp);
                        Graphics.Blit(src, tmp);
                        rqs.Add(AsyncGPUReadback.Request(tmp, 0, target));
                    }
                    catch (Exception e) { failedAt = mip; failure = e; break; }
                }
                // the waits in level order: the first level that fails gives the finding; every request made is waited for
                string finding = null;
                for (int mip = 0; mip < rqs.Count; mip++)
                {
                    var rq = rqs[mip];
                    try
                    {
                        rq.WaitForCompletion();
                        if (finding != null) continue;
                        if (rq.hasError) { finding = "level " + mip + " could not be read back: the texture is not stored"; continue; }
                        d.Levels.Add(rq.GetData<byte>().ToArray());
                        whole++;
                    }
                    catch (Exception e)
                    {
                        if (finding == null) finding = "level " + mip + ": " + e.GetType().Name + " " + e.Message + "; the texture is not stored";
                    }
                }
                if (finding == null && failure != null) finding = "level " + failedAt + ": " + failure.GetType().Name + " " + failure.Message + "; the texture is not stored";
                if (finding != null) { res?.Findings.Warning("bake.textureUnreadable", t.name, finding); return false; }
            }
            finally
            {
                foreach (var x in tmps) if (x) RenderTexture.ReleaseTemporary(x);
                foreach (var x in lins) if (x) Object.DestroyImmediate(x);
            }
            return Decoded(t, mips, srgb, hdr, whole, d, res);
        }

        /// <summary>The old decode (tests, <see cref="BakeJob.Legacy"/>): each level copied, drawn, requested and waited for before the next.</summary>
        static bool DecodeByLevel(Texture t, GraphicsFormat fmt, int mips, bool srgb, TexData d, BakeCaptureResult res)
        {
            bool hdr = IsHdr(fmt);
            var target = hdr ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;
            var linFmt = GraphicsFormatUtility.GetLinearFormat(fmt);
            bool compressed = GraphicsFormatUtility.IsCompressedFormat(fmt);
            int bw = compressed ? (int)GraphicsFormatUtility.GetBlockWidth(fmt) : 1, bh = compressed ? (int)GraphicsFormatUtility.GetBlockHeight(fmt) : 1;
            int whole = 0;
            for (int mip = 0; mip < mips; mip++)
            {
                int w = Math.Max(1, t.width >> mip), h = Math.Max(1, t.height >> mip);
                if (compressed && (w % bw != 0 || h % bh != 0)) break;
                Texture src = t;
                Texture2D lin = null;
                RenderTexture tmp = null;
                try
                {
                    bool msaa = t is RenderTexture mrt && mrt.antiAliasing > 1;
                    if (!msaa)
                    {
                        // the level as raw texels: a copy flagged linear, so the draw neither decodes nor encodes sRGB
                        lin = new Texture2D(w, h, linFmt, TextureCreationFlags.None) { name = "bake_decode" };
                        Graphics.CopyTexture(t, 0, mip, lin, 0, 0);
                        src = lin;
                    }
                    tmp = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, target, GraphicsFormat.None) { msaaSamples = 1, sRGB = false });
                    Graphics.Blit(src, tmp);
                    var rq = AsyncGPUReadback.Request(tmp, 0, target);
                    rq.WaitForCompletion();
                    res?.Inc(WaitsByLevel);
                    if (rq.hasError) { res?.Findings.Warning("bake.textureUnreadable", t.name, "level " + mip + " could not be read back: the texture is not stored"); return false; }
                    d.Levels.Add(rq.GetData<byte>().ToArray());
                    whole++;
                }
                catch (Exception e)
                {
                    res?.Findings.Warning("bake.textureUnreadable", t.name, "level " + mip + ": " + e.GetType().Name + " " + e.Message + "; the texture is not stored");
                    return false;
                }
                finally
                {
                    if (tmp) RenderTexture.ReleaseTemporary(tmp);
                    if (lin) Object.DestroyImmediate(lin);
                }
            }
            return Decoded(t, mips, srgb, hdr, whole, d, res);
        }

        /// <summary>After the decoded levels are read back: the stored format, and the levels below the last whole block made from it.</summary>
        static bool Decoded(Texture t, int mips, bool srgb, bool hdr, int whole, TexData d, BakeCaptureResult res)
        {
            if (whole == 0) { res?.Findings.Warning("bake.textureUnreadable", t.name, "no level could be decoded: the texture is not stored"); return false; }
            res?.Inc(LevelsRead, whole);
            d.Format = hdr ? GraphicsFormat.R16G16B16A16_SFloat : srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            if (whole < mips && !hdr)
            {
                // the levels below the last whole block, made from it
                for (int mip = whole; mip < mips; mip++) d.Levels.Add(Half(d.Levels[mip - 1], Math.Max(1, t.width >> (mip - 1)), Math.Max(1, t.height >> (mip - 1))));
                res?.Inc("tex.levelsMade", mips - whole);
            }
            return true;
        }

        /// <summary>A texture whose own format the device does not sample: level 0 drawn as the engine samples it into an 8-bit target of its
        /// sRGB flag, read back; the smaller levels made from it by a 2 × 2 box.</summary>
        static bool Sampled(Texture t, int mips, bool srgb, TexData d, BakeCaptureResult res)
        {
            var fmt = srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            RenderTexture tmp = null;
            try
            {
                tmp = RenderTexture.GetTemporary(new RenderTextureDescriptor(t.width, t.height, fmt, GraphicsFormat.None) { msaaSamples = 1 });
                Graphics.Blit(t, tmp);
                var rq = AsyncGPUReadback.Request(tmp, 0, fmt);
                rq.WaitForCompletion();
                if (rq.hasError) { res?.Findings.Warning("bake.textureUnreadable", t.name, "the texture could not be read back: it is not stored"); return false; }
                d.Levels.Add(rq.GetData<byte>().ToArray());
            }
            catch (Exception e)
            {
                res?.Findings.Warning("bake.textureUnreadable", t.name, e.GetType().Name + " " + e.Message + "; the texture is not stored");
                return false;
            }
            finally { if (tmp) RenderTexture.ReleaseTemporary(tmp); }
            res?.Inc(LevelsRead);
            d.Format = fmt;
            for (int mip = 1; mip < mips; mip++) d.Levels.Add(Half(d.Levels[mip - 1], Math.Max(1, t.width >> (mip - 1)), Math.Max(1, t.height >> (mip - 1))));
            return true;
        }

        /// <summary>The next smaller level of an RGBA8 level by a 2 × 2 box.</summary>
        static byte[] Half(byte[] src, int w, int h)
        {
            int nw = Math.Max(1, w >> 1), nh = Math.Max(1, h >> 1);
            var dst = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(2 * x, w - 1), x1 = Math.Min(2 * x + 1, w - 1), y0 = Math.Min(2 * y, h - 1), y1 = Math.Min(2 * y + 1, h - 1);
                    for (int c = 0; c < 4; c++)
                    {
                        int s = src[(y0 * w + x0) * 4 + c] + src[(y0 * w + x1) * 4 + c] + src[(y1 * w + x0) * 4 + c] + src[(y1 * w + x1) * 4 + c];
                        dst[(y * nw + x) * 4 + c] = (byte)((s + 2) / 4);
                    }
                }
            return dst;
        }

        // ------------------------------------------------------------ the mean colour

        /// <summary>The mean of a 16 × 16 sample of level 0 in linear rgb (8-bit four-channel formats only), else null.</summary>
        static Vector3? Mean(TexData d, bool srgb)
        {
            if (d.Levels.Count == 0) return null;
            var f = d.Format;
            bool bgra = f == GraphicsFormat.B8G8R8A8_SRGB || f == GraphicsFormat.B8G8R8A8_UNorm;
            if (!(f == GraphicsFormat.R8G8B8A8_SRGB || f == GraphicsFormat.R8G8B8A8_UNorm || bgra)) return null;
            var l0 = d.Levels[0];
            if (l0.Length < d.W * d.H * 4) return null;
            double r = 0, g = 0, b = 0;
            for (int j = 0; j < 16; j++)
                for (int i = 0; i < 16; i++)
                {
                    int x = Math.Min(d.W - 1, (int)((i + 0.5) * d.W / 16)), y = Math.Min(d.H - 1, (int)((j + 0.5) * d.H / 16));
                    int o = (y * d.W + x) * 4;
                    float cr = l0[o + (bgra ? 2 : 0)] / 255f, cg = l0[o + 1] / 255f, cb = l0[o + (bgra ? 0 : 2)] / 255f;
                    if (srgb) { cr = Mathf.GammaToLinearSpace(cr); cg = Mathf.GammaToLinearSpace(cg); cb = Mathf.GammaToLinearSpace(cb); }
                    r += cr; g += cg; b += cb;
                }
            return new Vector3((float)(r / 256), (float)(g / 256), (float)(b / 256));
        }

        // ------------------------------------------------------------ build

        /// <summary>A texture from its stored levels, every level set, applied non-readable; sampler state from the main.</summary>
        public static Texture2D Build(TexData d, BakeData.TexRef r)
        {
            int mips = Math.Max(1, d.Levels.Count);
            var flags = mips > 1 ? TextureCreationFlags.MipChain : TextureCreationFlags.None;
            // a stored format the device cannot sample builds no texture (the slot keeps none)
            if (!SystemInfo.IsFormatSupported(d.Format, GraphicsFormatUsage.Sample)) return null;
            Texture2D tex;
            try { tex = new Texture2D(d.W, d.H, d.Format, mips, flags) { name = r?.Name ?? "" }; }
            catch (Exception) { return null; }
            if (!tex) return null;
            for (int l = 0; l < d.Levels.Count; l++) tex.SetPixelData(d.Levels[l], l);
            if (r != null)
            {
                tex.wrapModeU = (TextureWrapMode)r.WrapU;
                tex.wrapModeV = (TextureWrapMode)r.WrapV;
                tex.filterMode = (FilterMode)r.Filter;
                tex.anisoLevel = r.Aniso;
                tex.mipMapBias = r.Bias;
            }
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Bytes of video memory of a built texture, computed from its format and size (the driver's counter is not read).</summary>
        public static long VideoBytes(Texture t)
        {
            if (!t) return 0;
            long n = 0;
            int mips = Math.Max(1, t.mipmapCount);
            for (int l = 0; l < mips; l++) n += (long)GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, t.width >> l), Math.Max(1, t.height >> l), t.graphicsFormat);
            return n;
        }

        /// <summary>The sampler state of a texture into its main entry.</summary>
        public static void Sampler(Texture t, BakeData.TexRef r)
        {
            r.Name = t.name;
            r.WrapU = (int)t.wrapModeU; r.WrapV = (int)t.wrapModeV;
            r.Filter = (int)t.filterMode; r.Aniso = t.anisoLevel; r.Bias = t.mipMapBias;
        }
    }
}
