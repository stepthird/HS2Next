// GPU compositing of overlay images into skin and clothes render textures (
// 4): ApplyOverlay = copy the target, then draw the copy blended with the overlay back onto the target
// (Hidden/HS2Next/OverlayComposite, Default or LinearAlpha mode, optional override); Rebuild = one skin pass with
// under-layers composited into a copy of the pass input and over-layers composited onto the result
// (TexCreator.Rebuild calls it when a pass has layers). Without a graphics device (-nographics) nothing is drawn:
// the layers are still decoded (and counted by the callers), the pass runs as before.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Chara.Ext
{
    public static class OverlayCompositor
    {
        public const int ModeDefault = 0, ModeLinearAlpha = 1;
        static Material mat;

        public static bool HasGpu => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>Largest texture side the device allows (the cap).</summary>
        public static int MaxSize => SystemInfo.maxTextureSize > 0 ? SystemInfo.maxTextureSize : 16384;
        /// <summary>Without a graphics device render textures above 4096 fail to create: such growth is only recorded.</summary>
        public static bool CanCreate(int w, int h) => HasGpu || (w <= 4096 && h <= 4096);

        static Material Mat
        {
            get
            {
                if (mat) return mat;
                var sh = Resources.Load<Shader>("HS2NextOverlayComposite");
                if (!sh) throw new System.Exception("shader Hidden/HS2Next/OverlayComposite not found (Resources/HS2NextOverlayComposite)");
                return mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, name = "HS2Next overlay composite" };
            }
        }

        /// <summary>Composites <paramref name="overlay"/> onto <paramref name="target"/>. A null overlay draws transparent black.</summary>
        public static void ApplyOverlay(RenderTexture target, Texture overlay, bool overrideTarget, int mode)
        {
            if (!HasGpu || !target) return;
            var d = target.descriptor;
            d.useMipMap = false; d.autoGenerateMips = false; d.msaaSamples = 1; d.depthBufferBits = 0;
            var tmp = RenderTexture.GetTemporary(d);
            if (SystemInfo.copyTextureSupport != CopyTextureSupport.None) Graphics.CopyTexture(target, 0, 0, tmp, 0, 0);
            else Graphics.Blit(target, tmp);
            var m = Mat;
            bool linear = mode == ModeLinearAlpha;
            m.SetTexture("_Overlay", overlay ? overlay : Texture2D.blackTexture);
            m.SetFloat("_Override", overrideTarget ? 1 : 0);
            m.SetFloat("_LinearAlpha", linear ? 1 : 0);
            m.SetFloat("_SrcBlend", linear ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", linear ? (float)BlendMode.Zero : (float)BlendMode.OneMinusSrcAlpha);
            bool srgb = GL.sRGBWrite;
            GL.sRGBWrite = true;
            Graphics.Blit(tmp, target, m, 0);
            GL.sRGBWrite = srgb;
            RenderTexture.ReleaseTemporary(tmp);
        }

        public static void Clear(RenderTexture target, Color c)
        {
            if (!HasGpu || !target) return;
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, c);
            RenderTexture.active = prev;
        }

        /// <summary>A new render texture like <paramref name="old"/> at another size; with <paramref name="copy"/> the old contents
        /// are scaled into it. The caller rebinds materials and releases the old one.</summary>
        public static RenderTexture Resized(RenderTexture old, int w, int h, bool copy)
        {
            var d = old.descriptor;
            d.width = w; d.height = h;
            var rt = new RenderTexture(d) { name = old.name, wrapMode = old.wrapMode, filterMode = old.filterMode, anisoLevel = old.anisoLevel };
            rt.Create();
            if (copy && HasGpu)
            {
                bool srgb = GL.sRGBWrite;
                GL.sRGBWrite = true;
                Graphics.Blit(old, rt);
                GL.sRGBWrite = srgb;
            }
            else Clear(rt, new Color(0, 0, 0, 0));
            return rt;
        }

        /// <summary>One skin compositing pass with layers: under-layers alpha-composited onto a copy of the
        /// pass input, the pass run with that copy, then the over-layers composited onto <paramref name="rt"/>. Layers without a
        /// texture are decoded from their image for the duration of the pass. Returns the number of layers drawn.</summary>
        public static int Rebuild(Texture main, RenderTexture rt, Material pass, IList<SkinLayer> under, IList<SkinLayer> over)
        {
            int drawn = 0;
            Texture src = main ? main : Texture2D.whiteTexture;   // Create/skin color: _MainTex default white
            RenderTexture tmp = null;
            if (under != null && under.Count > 0)
            {
                if (HasGpu)
                {
                    tmp = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(src, tmp);
                }
                foreach (var l in under) if (WithLayer(l, t => ApplyOverlay(tmp, t, false, ModeDefault))) drawn++;
                if (tmp) src = tmp;
            }
            Graphics.Blit(src, rt, pass, 0);
            if (tmp) RenderTexture.ReleaseTemporary(tmp);
            if (over != null)
                foreach (var l in over) if (WithLayer(l, t => ApplyOverlay(rt, t, false, ModeDefault))) drawn++;
            return drawn;
        }

        // Runs `draw` with the layer's texture (decoded from its image and released afterwards when it has none).
        static bool WithLayer(SkinLayer l, System.Action<Texture> draw)
        {
            if (l == null) return false;
            if (l.Texture) { draw(l.Texture); return true; }
            if (l.Image == null) return false;
            var t = ExtTextures.Acquire(l.Image, false, false, "overlay " + l.Source, out string key);
            if (!t) return false;
            try { draw(t); }
            finally { ExtTextures.Release(key); }
            return true;
        }
    }
}
