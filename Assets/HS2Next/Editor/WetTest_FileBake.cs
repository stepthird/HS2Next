// WetTest: WF9's last clause, a stored map equals a bake from the same inputs. Synthetic inputs: a grid over the UV square and a distance per vertex from a point, on the GPU; no
// character, and no picture leaves memory.
using System;
using System.Linq;
using HS2Next.Save.Tests;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class WetTest
{
    static void Render_FileBake()
    {
        Group("WF9b", "a stored map equals a bake from the same inputs", () =>
        {
            if (!Check("a device and the bake's shader", WetBake.Available)) return;
            const int n = 16, w = 96, h = 64;
            const float reach = 2.895f;
            var uv = new Vector2[(n + 1) * (n + 1)];
            var distance = new float[uv.Length];
            for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                {
                    var p = new Vector2(x / (float)n, y / (float)n);
                    uv[y * (n + 1) + x] = p;
                    distance[y * (n + 1) + x] = (p - new Vector2(.25f, .5f)).magnitude * 4f;
                }
            var tris = new int[n * n * 6];
            for (int y = 0, k = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * (n + 1) + x;
                    tris[k++] = i; tris[k++] = i + n + 1; tris[k++] = i + 1;
                    tris[k++] = i + 1; tris[k++] = i + n + 1; tris[k++] = i + n + 2;
                }
            var first = WetBake.Bake(uv, tris, distance, w, h, reach);
            var second = WetBake.Bake(uv, tris, distance, w, h, reach);
            RenderTexture decoded = null;
            try
            {
                var a = WetBake.ReadBack(first);
                var b = WetBake.ReadBack(second);
                Check("a bake's bytes", a != null && a.Length == w * h);
                Check("the same inputs bake the same bytes", a.SequenceEqual(b));
                Check("some texels are reached and some are not", a.Contains((byte)255) && a.Any(v => v < 254));
                var png = WetBake.Encode(a, w, h);
                Check("the stored picture is an 8-bit gray PNG", DocTests_Wet.MapProblem(png) == null, DocTests_Wet.MapProblem(png) ?? "");
                var inFile = DocTests_Wet.MapValues(png, out int fw, out int fh);
                var rows = new byte[w * h];
                for (int y = 0; y < h; y++) Array.Copy(inFile, (h - 1 - y) * w, rows, y * w, w);
                Check("the file holds the bake's values", fw == w && fh == h && (rows.SequenceEqual(a) || inFile.SequenceEqual(a)));
                decoded = WetBake.Decode(png, out var r8);
                Check("decoded, the bytes are the bake's", r8 != null && r8.SequenceEqual(a));
                Check("and so is the map on the GPU", decoded && WetBake.ReadBack(decoded).SequenceEqual(a));
            }
            finally
            {
                WetBake.Free(first);
                WetBake.Free(second);
                if (decoded) WetBake.Free(decoded);
            }
        });
    }
}
