// UI1's v0.3 trial 2 silhouette check (package R; Editor play and the app player with -ui, at the end of UI1 with its
// character framed): chara.state sets the silhouette in an opaque colour; chara.clothesDump reports it realized and every
// clothes part hidden; a render of the view in memory (no AA, no post-processing; numbers only, nothing saved or viewed)
// holds the figure in that colour; switched off, the dump's part activity is as before. In the player this is the proof
// that the silhouette draws there (before the fix the player had no shader for it: only the clothes disappeared).
using System.Collections;
using System.Linq;
using HS2Next.App.Commands;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        static string R2Parts(JObject dump) =>
            string.Join(",", (dump?["kinds"] as JArray ?? new JArray()).Select(k => (string)k["kind"] + "=" + (k["active"]?.Type == JTokenType.Boolean ? ((bool)k["active"] ? "on" : "off") : "-")));

        static int R2Magenta(Camera cam)
        {
            var png = ViewCapture.Render(cam, 192, 320, ViewCapture.None);
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(png);
            int n = 0;
            foreach (var p in t.GetPixels32()) if (p.r >= 240 && p.g <= 20 && p.b >= 240) n++;
            Object.Destroy(t);
            return n;
        }

        static IEnumerator UiR2Silhouette(AcceptanceCtx c, string id)
        {
            var ctx = UiHost.Current?.Context;
            var view = ctx?.Runtime?.GetPart<SceneViewCamera>();
            if (id == null || view == null || !view.Camera) { c.Check(false, "UI1 v0.3 trial 2 silhouette: a character and a view camera"); yield break; }
            yield return c.Call(new { cmd = "chara.clothesDump", key = id });
            string partsBefore = R2Parts(c.J);
            bool pp = view.Setup.PostProcessing;
            view.Setup.PostProcessing = false;
            int before = R2Magenta(view.Camera);
            yield return c.Call(new { cmd = "chara.state", key = id, set = new { silhouette = new { enabled = true, color = new[] { 1f, 0f, 1f, 1f } } } });
            c.CheckOk("UI1 v0.3 trial 2 chara.state silhouette on");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "chara.clothesDump", key = id });
            bool realized = (bool?)c.J["silhouette"] == true;
            var present = (c.J["kinds"] as JArray ?? new JArray()).Where(k => (bool?)k["present"] == true).ToList();
            bool partsHidden = present.Count > 0 && present.All(k => (bool?)k["active"] == false);
            int on = R2Magenta(view.Camera);
            view.Setup.PostProcessing = pp;
            c.Check(realized && partsHidden && on > 1000 && before < 50,
                $"UI1 v0.3 trial 2 the silhouette draws: realized {realized}, the {present.Count} clothes parts hidden {partsHidden}, {on} px in its colour of 192 x 320 (before {before})");
            yield return c.Call(new { cmd = "chara.state", key = id, set = new { silhouette = new { enabled = false, color = new[] { 1f, 0f, 1f, 1f } } } });
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "chara.clothesDump", key = id });
            c.Check((bool?)c.J["silhouette"] == false && R2Parts(c.J) == partsBefore && partsBefore.Contains("=on"), "UI1 v0.3 trial 2 silhouette off: the parts as before (" + partsBefore + ")");
        }
    }
}
