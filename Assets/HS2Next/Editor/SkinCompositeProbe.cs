// Probe the original "Create/skin color" shader with controlled inputs to find out how the
// card skin colour has to be fed in. Run: -executeMethod SkinCompositeProbe.Run
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using UnityEngine;

public static class SkinCompositeProbe
{
    public static void Run()
    {
        var gd = ContentCatalog.FromSettings(CardResolveTest.Settings);
        var sb = new StringBuilder();
        var skin = gd.LoadAsset<Texture2D>("chara/00/ft_skin_b_00.unity3d", "cf_body_02_t");
        var src = gd.LoadAsset<Material>("chara/mm_base.unity3d", "create_skin_body");
        sb.AppendLine("create_skin_body props: " + string.Join(" ", Enumerable.Range(0, src.shader.GetPropertyCount()).Select(i =>
        {
            var n = src.shader.GetPropertyName(i);
            var t = src.shader.GetPropertyType(i);
            return t == UnityEngine.Rendering.ShaderPropertyType.Color ? $"{n}={src.GetColor(n)}" :
                   t == UnityEngine.Rendering.ShaderPropertyType.Texture ? $"{n}={(src.GetTexture(n) ? src.GetTexture(n).name : "null")}" :
                   t == UnityEngine.Rendering.ShaderPropertyType.Vector ? $"{n}={src.GetVector(n)}" : $"{n}={src.GetFloat(n)}";
        })));
        var white = Tex(Color.white);
        foreach (var (label, main, col) in new (string, Texture, Color)[]
        {
            ("tex, color as stored in material", skin, src.GetColor("_Color")),
            ("tex, white", skin, Color.white),
            ("tex, gray .5", skin, new Color(0.5f, 0.5f, 0.5f, 1)),
            ("tex, card skin", skin, new Color(0.776f, 0.68f, 0.62f, 1)),
            ("white tex, card skin", white, new Color(0.776f, 0.68f, 0.62f, 1)),
            ("white tex, white", white, Color.white),
        })
        {
            var m = new Material(src);
            m.SetColor("_Color", col); m.SetTexture("_Texture5", null); m.SetTexture("_Texture6", null);
            var rt = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32);
            bool s = GL.sRGBWrite; GL.sRGBWrite = true;
            Graphics.Blit(main, rt, m, 0);
            GL.sRGBWrite = s;
            RenderTexture.active = rt;
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); t.Apply();
            RenderTexture.active = null;
            var px = t.GetPixels32();
            var avg = new Color((float)px.Average(p => p.r), (float)px.Average(p => p.g), (float)px.Average(p => p.b), (float)px.Average(p => p.a));
            sb.AppendLine($"{label,-36} color {col} -> 8bit avg ({avg.r:F0},{avg.g:F0},{avg.b:F0},{avg.a:F0})");
        }
        Directory.CreateDirectory(CardResolveTest.OutDir);
        File.WriteAllText(Path.Combine(CardResolveTest.OutDir, "skin_probe.txt"), sb.ToString());
        gd.UnloadAll();
        Debug.Log("PROBE DONE");
    }

    static Texture2D Tex(Color c)
    {
        var t = new Texture2D(4, 4); t.SetPixels(Enumerable.Repeat(c, 16).ToArray()); t.Apply(); return t;
    }
}
