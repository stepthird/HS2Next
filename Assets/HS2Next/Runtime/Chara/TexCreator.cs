// One compositing pass of the character build: an
// instanced HS2 "create" material blitted from Main into an ARGB32 RT with mips; skin layers (KSOX semantics) under / over.
// Rebuild clears the RT first, so a rebuild after an edit equals the first composite of the same inputs.
using System.Collections.Generic;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using UnityEngine;

namespace HS2Next.Chara
{
    /// <summary>One compositing pass: Blit(Main) through an instanced HS2 "create" material into an RT.
    /// Optional skin layers (Runtime/Chara/Ext, KSOX semantics): Under is composited into a copy of Main before the pass,
    /// Over onto the result.</summary>
    public class TexCreator
    {
        public static string DebugDir;   // when set, every rebuilt RT is also saved as PNG (sRGB)
        public readonly Material Mat;
        public Texture Main;
        RenderTexture rt;
        public readonly List<SkinLayer> Under = new List<SkinLayer>(), Over = new List<SkinLayer>();
        /// <summary>The output RT (bound to the draw material by the caller of Rebuild).</summary>
        public RenderTexture Target => rt;
        /// <summary>Layers drawn (or, without a graphics device, decoded) by the last Rebuild.</summary>
        public int LayersDrawn { get; private set; }

        public TexCreator(ContentCatalog gd, string createMaterial, int w, int h)
        {
            var src = gd.LoadAsset<Material>("chara/mm_base.unity3d", createMaterial);
            Mat = MaterialCopies.Copy(src);
            Main = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = true, name = createMaterial };
        }

        /// <summary>Replaces the output RT by one of another size (cleared) and returns the previous one, which the caller
        /// unbinds and releases.</summary>
        public RenderTexture Resize(int w, int h)
        {
            var old = rt;
            rt = OverlayCompositor.Resized(old, w, h, copy: false);
            return old;
        }

        public RenderTexture Rebuild()
        {
            bool srgb = GL.sRGBWrite;
            GL.sRGBWrite = true;
            var prev = RenderTexture.active;
            RenderTexture.active = rt; GL.Clear(true, true, new Color(0, 0, 0, 0)); RenderTexture.active = prev;
            LayersDrawn = 0;
            if (Under.Count > 0 || Over.Count > 0) LayersDrawn = OverlayCompositor.Rebuild(Main, rt, Mat, Under, Over);
            else Graphics.Blit(Main, rt, Mat, 0);
            GL.sRGBWrite = srgb;
            if (DebugDir != null)
            {
                var small = RenderTexture.GetTemporary(512, 512 * rt.height / rt.width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rt, small);
                RenderTexture.active = small;
                var t = new Texture2D(small.width, small.height, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, small.width, small.height), 0, 0); t.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(DebugDir, $"rt_{rt.name}_{System.DateTime.Now.Ticks % 100000}.png"), t.EncodeToPNG());
                RenderTexture.ReleaseTemporary(small);
                if (Main is Texture2D m2 && m2.isReadable == false)
                {
                    var tmp = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(Main, tmp); RenderTexture.active = tmp;
                    var t2 = new Texture2D(512, 512, TextureFormat.RGBA32, false); t2.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); t2.Apply();
                    RenderTexture.active = prev;
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(DebugDir, $"src_{rt.name}_{Main.name}.png"), t2.EncodeToPNG());
                    RenderTexture.ReleaseTemporary(tmp);
                }
            }
            return rt;
        }
    }
}
