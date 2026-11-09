// WetTest's file part: a map through the encoder the app uses. Mono.save holds the rest of WF9 and writes its pictures by hand, so only this row shows
// what Unity's encoder really writes for a map: an 8-bit gray PNG with nothing beside the image, read back by the app's way of reading a picture. Synthetic values only.
using System;
using System.Linq;
using HS2Next.Save.Tests;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

public static partial class WetTest
{
    static void Headless_File()
    {
        Group("WF9", "a map through the encoder the app uses", () =>
        {
            foreach (var (w, h) in new[] { (61, 37), (64, 32), (1, 1), (2048, 3) })
            {
                string size = w + " x " + h;
                var values = new byte[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) values[y * w + x] = (byte)((x * 7 + y * 13) % 256);
                var png = ImageConversion.EncodeArrayToPNG(values, GraphicsFormat.R8_UNorm, (uint)w, (uint)h);
                Check(size + ": an 8-bit gray PNG, nothing beside the image", DocTests_Wet.MapProblem(png) == null, DocTests_Wet.MapProblem(png) ?? "");
                var back = DocTests_Wet.MapValues(png, out int bw, out int bh);
                Check(size + ": its size", bw == w && bh == h, bw + " x " + bh);
                bool asGiven = back.SequenceEqual(values);
                var flipped = new byte[w * h];
                for (int y = 0; y < h; y++) Array.Copy(back, (h - 1 - y) * w, flipped, y * w, w);
                Check(size + ": every value kept", asGiven || flipped.SequenceEqual(values));
                Info(size + ": rows in the file " + (asGiven ? "in the array's order" : "from the array's last row") + ", " + png.Length + " bytes");

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    Check(size + ": the picture decodes", ImageConversion.LoadImage(tex, png, false));
                    var px = tex.GetPixels32();
                    Check(size + ": decoded in the array's order, the value in red", tex.width == w && tex.height == h && px.Select(p => p.r).SequenceEqual(values));
                }
                finally { Object.DestroyImmediate(tex); }
            }
        });
    }
}
