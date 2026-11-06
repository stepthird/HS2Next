// MaterialEditTest.full, the character groups (package R): ML2-ML6, ML8-ML10 and the draft of MV1. See MaterialEditTest.cs.
// RunValues (opt-in, about 3.5 min, -hash a+b, -limit N, -from N): every installed character card with items: its value items leave through the value pass and come back, and
// after each step the node equals a fresh build of the same documents. Report material_edit_values_report.txt, cards by hash.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;
using Debug = UnityEngine.Debug;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

public static partial class MaterialEditTest
{
    static void CharaFull(McHost h)
    {
        if (Want("ML2")) Guard("ML2 set, then remove", () => Ml2(h));
        if (Want("ML3")) Guard("ML3 through every rebuild path", () => Ml3(h));
        if (Want("ML4")) Guard("ML4 a colour change under an edit", () => Ml4(h));
        if (Want("ML5")) Guard("ML5 Enabled under a hidden body", () => Ml5(h));
        if (Want("ML6")) Guard("ML6 a drag", () => Ml6(h));
        if (Want("ML8")) Guard("ML8 a shader swap", () => Ml8(h));
        if (Want("ML9")) Guard("ML9 a colour item on a vector property", () => Ml9(h));
        if (Want("ML10")) Guard("ML10 the last item", () => Ml10(h));
        if (Want("ML11")) Guard("ML11 the queue and the shadow switch", () => Ml11(h));
        if (Want("MV1")) Guard("MV1 the candidate list (draft)", Mv1);
    }

    static string NewScene(McHost h, bool male = false)
    {
        h.Ok(new { cmd = "scene.new" }, "scene.new");
        return LoadTarget(h, male);
    }

    /// <summary>The character as a fresh build of its documents shows it: the scene saved and loaded.</summary>
    public static CharaSnapshot Fresh(McHost h, string key, string name)
    {
        var save = h.Call(new { cmd = "scene.save", name, overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" });
        Check(name + ": scene.save", save.Value<bool?>("ok") == true, Short(save.ToString()));
        h.Call(new { cmd = "scene.new" });
        var load = h.Call(new { cmd = "scene.load", path = (string)save["path"] });
        Check(name + ": scene.load", load.Value<bool?>("ok") == true, Short(load.ToString()));
        h.Rt.ApplyNow();
        return Snap(h, key);
    }

    static MeKey KeyOf(Target t, Case c, string key, McHost h)
    {
        var e = c.Make(h, key);
        return e.Key;
    }

    static void Ml2(McHost h)
    {
        string key = NewScene(h);
        string image = null;
        foreach (var t in Targets(h, key))
        {
            // (a save keeps only the images the documents name: each target stores its own after the last load)
            image = TestImage(h, new Color(0.9f, 0.1f, 0.2f, 1f));
            var cases = Cases(h, key, t, image);
            foreach (var c in cases)
            {
                string what = $"ML2 {t.Name} {c.Type}";
                var before = Snap(h, key);
                var e = c.Make(h, key);
                if (!Check(what + ": set", Edit(h, key, t, e), "")) continue;
                Check(what + ": on the material", c.Read(h, key) == c.After, c.Read(h, key) + " want " + c.After);
                var list = t.List(Stored(h, key));
                SetArray(h, key, t, MaterialEdits.Remove(list, e.Key));
                Same(before, Snap(h, key), what + ": removal = the state before");
            }
            // the value path's result = a fresh build of the same documents
            var values = cases.Where(c => c.Type != "shader" && c.Type != "renderer").ToList();
            foreach (var c in values) Edit(h, key, t, c.Make(h, key));
            var live = Snap(h, key);
            var fresh = Fresh(h, key, "ml2_" + t.Name.Replace(":", "_"));
            Same(fresh, live, $"ML2 {t.Name}: {values.Count} value items through the value pass = a fresh build");
            foreach (var c in values) Check($"ML2 {t.Name} {c.Type}: on the material after the build", c.Read(h, key) == c.After, c.Read(h, key) + " want " + c.After);
            foreach (var c in values) { var l = t.List(Stored(h, key)); SetArray(h, key, t, MaterialEdits.Remove(l, c.Make(h, key).Key)); }
            var after = Snap(h, key);
            Same(Fresh(h, key, "ml2b_" + t.Name.Replace(":", "_")), after, $"ML2 {t.Name}: every built item removed in place = a fresh build");
        }
        // two values on one object: the item that stays keeps writing, the one that left goes back
        var hair = HairTarget(Enumerable.Range(0, 4).First(k => Ctx(h, key).Hair[k]));
        var hc = Cases(h, key, hair, TestImage(h, new Color(0.9f, 0.1f, 0.2f, 1f))).Where(c => c.Type == "float" || c.Type == "color").ToList();
        foreach (var c in hc) Edit(h, key, hair, c.Make(h, key));
        SetArray(h, key, hair, MaterialEdits.Remove(hair.List(Stored(h, key)), hc[0].Make(h, key).Key));
        Check("ML2 the item that stays still acts", hc.Count < 2 || hc[1].Read(h, key) == hc[1].After, hc.Count < 2 ? "" : hc[1].Read(h, key));
    }

    static void Set(McHost h, string key, string path, object value, string what) =>
        h.Ok(new { cmd = "chara.edit.set", key, set = new Dictionary<string, object> { [path] = value } }, what);

    static string Other(McHost h, string key, string path)
    {
        string cur = (string)h.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];
        var l = h.Call(new { cmd = "chara.edit.list", key, path, limit = 10000 });
        return (l["rows"] as JArray)?.Select(x => (string)x["ref"]).FirstOrDefault(x => x != cur && x != null && x.StartsWith("illusion.hs2#", StringComparison.Ordinal));
    }

    static void Ml3(McHost h)
    {
        string key = NewScene(h);
        string image = TestImage(h, new Color(0.2f, 0.3f, 0.9f, 1f));
        var ctx = Ctx(h, key);
        int hk = Enumerable.Range(0, 4).First(k => ctx.Hair[k]);
        int ck = Enumerable.Range(0, 8).First(k => ctx.Clothes[k]);
        var hair = HairTarget(hk);
        var cloth = ClothesTarget(ck);
        var ch = CharacterTarget();
        var hf = Cases(h, key, hair, image).First(c => c.Type == "float");
        var cf = Cases(h, key, cloth, image).First(c => c.Type == "float");
        var bf = Cases(h, key, ch, image).First(c => c.Type == "float");
        Edit(h, key, hair, hf.Make(h, key));
        Edit(h, key, cloth, cf.Make(h, key));
        Edit(h, key, ch, bf.Make(h, key));
        void Holds(string when)
        {
            Check($"ML3 {when}: the hair value", hf.Read(h, key) == hf.After, hf.Read(h, key));
            Check($"ML3 {when}: the clothes value", cf.Read(h, key) == cf.After, cf.Read(h, key));
            Check($"ML3 {when}: the character value", bf.Read(h, key) == bf.After, bf.Read(h, key));
        }
        Holds("set");
        string hn = HS2Next.Chara.Edit.UnitMap.HairParts[hk], cn = HS2Next.Chara.Edit.UnitMap.ClothesKinds[ck];
        Set(h, key, "chara.hair." + hn + ".baseColor", new[] { 0.7f, 0.1f, 0.1f, 1f }, "ML3 a hair colour edit");
        h.Rt.ApplyNow();
        Holds("after a hair colour edit");
        Set(h, key, "coord.clothes." + cn + ".colors[0].base", new[] { 0.1f, 0.7f, 0.1f, 1f }, "ML3 a clothes colour edit");
        h.Rt.ApplyNow();
        Holds("after a clothes colour edit");
        // a head swap: the Character items run by name, the body's value stays and can still go back
        string head = Other(h, key, "chara.face.head");
        if (head == null) Warn("ML3", "no other head row");
        else
        {
            h.Ok(new { cmd = "chara.edit.item", key, path = "chara.face.head", @ref = head }, "ML3 a head swap");
            h.Rt.ApplyNow();
            Check("ML3 after a head swap: Character items kept", ch.List(Stored(h, key)).Count == 1, ch.List(Stored(h, key)).Count + " items");
            if (Named(h, key, ch, bf.MaterialName).Count > 0) Check("ML3 after a head swap: the character value", bf.Read(h, key) == bf.After, bf.Read(h, key));
            else Info("ML3 the Character float sat on the old head's material: no such material now");
            Undo(h, "ML3 undo the head swap");
            Holds("after undoing the head swap");
        }
        // a rebuild
        var real = Real(h, key);
        Check("ML3 a rebuild is taken", real.RequestPluginRebuild("test.material"), "");
        h.Rt.ApplyNow();
        Holds("after a rebuild");
        // a coordinate load: the source's items come with the parts; hair and Character items stay
        var coord = System.IO.Path.Combine(h.App.Paths.Hs2UserData ?? "", "coordinate", "female", "ガーリーブラウス.png");
        if (!System.IO.File.Exists(coord)) Warn("ML3", "the SFW coordinate card is missing: not run");
        else
        {
            h.Ok(new { cmd = "chara.edit.coordinate", key, file = "ガーリーブラウス.png", mode = "clothes" }, "ML3 a coordinate load");
            h.Rt.ApplyNow();
            Check("ML3 after a coordinate load: hair item stays", hf.Read(h, key) == hf.After, hf.Read(h, key));
            Check("ML3 after a coordinate load: Character item stays", ch.List(Stored(h, key)).Count == 1, "");
            Undo(h, "ML3 undo the coordinate load");
            Holds("after undoing the coordinate load");
        }
        // a part swap: the part's items are cleared in the same command, undo restores them
        string row = Other(h, key, "chara.hair." + hn + ".item");
        if (row == null) Warn("ML3", "no other hair row");
        else
        {
            h.Ok(new { cmd = "chara.edit.item", key, path = "chara.hair." + hn + ".item", @ref = row }, "ML3 a part swap");
            h.Rt.ApplyNow();
            Check("ML3 a part swap clears the part's items", hair.List(Stored(h, key)).Count == 0, hair.List(Stored(h, key)).Count + " items");
            Undo(h, "ML3 undo the part swap");
            Check("ML3 undo restores the items", hair.List(Stored(h, key)).Count == 1, "");
            Holds("after undoing the part swap");
        }
        var live = Snap(h, key);
        Same(Fresh(h, key, "ml3"), live, "ML3 after all of it = a fresh build");
    }

    static void Ml4(McHost h)
    {
        string key = NewScene(h);
        var ctx = Ctx(h, key);
        int ck = Enumerable.Range(0, 8).First(k => ctx.Clothes[k]);
        var cloth = ClothesTarget(ck);
        string cn = HS2Next.Chara.Edit.UnitMap.ClothesKinds[ck];
        var m = MaterialEditorApplier.ObjectRenderers(ctx, cloth.Object).SelectMany(r => r.sharedMaterials).First(x => x && !MosaicSet.IsMosaicMaterial(x) && FirstColor(x) != null);
        string mat = MaterialEditorApplier.Fmt(m.name), prop = m.HasProperty("_Color") ? "_Color" : FirstColor(m);
        Info($"ML4 {mat} {prop}");
        var was = m.GetColor(prop);
        var to = new Color(0.9f, 0.2f, 0.4f, 1f);
        Edit(h, key, cloth, MaterialEdit.ColorOf(mat, prop.Substring(1), C4(to), C4(was)));
        Check("ML4 the edit shows", Round(Named(h, key, cloth, mat)[0].GetColor(prop).g) == Round(to.g), Named(h, key, cloth, mat)[0].GetColor(prop).ToString());
        Set(h, key, "coord.clothes." + cn + ".colors[0].base", new[] { 0.2f, 0.4f, 0.8f, 1f }, "ML4 a colour change under it");
        h.Rt.ApplyNow();
        Check("ML4 the edit stays on top", Round(Named(h, key, cloth, mat)[0].GetColor(prop).g) == Round(to.g), "");
        SetArray(h, key, cloth, MaterialEdits.Remove(cloth.List(Stored(h, key)), MeKey.Property("color", mat, prop.Substring(1))));
        var live = Snap(h, key);
        Same(Fresh(h, key, "ml4"), live, "ML4 removal shows the new colour: = a fresh build");

        // the same on a hair part, whose colour pass writes _Color itself: the unit's write is new input
        key = NewScene(h);
        ctx = Ctx(h, key);
        int hk = Enumerable.Range(0, 4).First(k => ctx.Hair[k]);
        var hair = HairTarget(hk);
        string hn = HS2Next.Chara.Edit.UnitMap.HairParts[hk];
        var hm = MaterialEditorApplier.ObjectRenderers(ctx, hair.Object).SelectMany(r => r.sharedMaterials).First(x => x && x.HasProperty("_Color"));
        string hmat = MaterialEditorApplier.Fmt(hm.name);
        Edit(h, key, hair, MaterialEdit.ColorOf(hmat, "Color", C4(to), C4(hm.GetColor("_Color"))));
        Set(h, key, "chara.hair." + hn + ".baseColor", new[] { 0.2f, 0.8f, 0.3f, 1f }, "ML4 a hair colour under it");
        h.Rt.ApplyNow();
        Check("ML4 hair: the edit stays on top", Round(Named(h, key, hair, hmat)[0].GetColor("_Color").g) == Round(to.g), "");
        SetArray(h, key, hair, MaterialEdits.Remove(hair.List(Stored(h, key)), MeKey.Property("color", hmat, "Color")));
        Check("ML4 hair: removal shows the new colour", Round(Named(h, key, hair, hmat)[0].GetColor("_Color").g) == Round(0.8f), Named(h, key, hair, hmat)[0].GetColor("_Color").ToString());
        var liveH = Snap(h, key);
        Same(Fresh(h, key, "ml4h"), liveH, "ML4 hair: = a fresh build");
    }

    static void Ml5(McHost h)
    {
        string key = NewScene(h);
        Set(h, key, "chara.body.visible", false, "ML5 the body hidden");
        h.Rt.ApplyNow();
        var ctx = Ctx(h, key);
        var body = ctx.BodyRenderer;
        Check("ML5 the body renderer is hidden", body && !body.enabled, "");
        string name = MaterialEditorApplier.Fmt(body.name);
        var ch = CharacterTarget();
        Edit(h, key, ch, MaterialEdit.RendererEnabled(name, 1, 0));
        var real = Real(h, key);
        Check("ML5 hidden after the flush", !Ctx(h, key).BodyRenderer.enabled, "");
        Check("ML5 the final stage ran", real.LastEditFlush != null && real.LastEditFlush.ExtFinalRan, real.LastEditFlush?.ToString() ?? "no flush");
        var live = Snap(h, key);
        Same(Fresh(h, key, "ml5"), live, "ML5 = a fresh build");
        Set(h, key, "chara.body.visible", true, "ML5 the body shown");
        h.Rt.ApplyNow();
        Check("ML5 shown with the item at 1", Ctx(h, key).BodyRenderer.enabled, "");
        // an Enabled 0 under the hidden body stays off when the body comes back, as a build has it
        key = NewScene(h);
        Set(h, key, "chara.body.visible", false, "ML5 hidden again");
        h.Rt.ApplyNow();
        var r2 = Ctx(h, key).BodyRenderer;
        string n2 = MaterialEditorApplier.Fmt(r2.name);
        Edit(h, key, ch, MaterialEdit.RendererEnabled(n2, 0, 1));
        Set(h, key, "chara.body.visible", true, "ML5 shown");
        h.Rt.ApplyNow();
        Check("ML5 the item's 0 holds when the body comes back", !Ctx(h, key).BodyRenderer.enabled, "");
        SetArray(h, key, ch, MaterialEdits.Remove(ch.List(Stored(h, key)), MeKey.Enabled(n2)));
        Check("ML5 removal shows it", Ctx(h, key).BodyRenderer.enabled, "");
        var live2 = Snap(h, key);
        Same(Fresh(h, key, "ml5b"), live2, "ML5 removal = a fresh build");
    }

    /// <summary>Steps of a drag on the array at the press, as a row runs them: one open transaction, a flush per step.</summary>
    static (int flushes, int rebuilds, int entries, List<double> ms, bool swapped) Drag(McHost h, string key, Target t, int steps, Func<int, MaterialEdit> step, Action<int> perStep = null)
    {
        var real = Real(h, key);
        int f0 = real.EditFlushes, r0 = real.EditRebuilds;
        int e0 = (h.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? 0;
        var press = t.List(Stored(h, key));
        var ms = new List<double>();
        bool swapped = false;
        using (var tx = h.Session.Commands.BeginOrJoin("Material edit", new ExecuteOptions { Source = CommandSource.User, Label = "Material edit" }))
        {
            for (int i = 0; i < steps; i++)
            {
                var sw = Stopwatch.StartNew();
                tx.Set(key, t.Path, MaterialEdits.Set(press, step(i)));
                h.Rt.ApplyNow();
                ms.Add(sw.Elapsed.TotalMilliseconds);
                if (real.LastEditFlush != null && real.LastEditFlush.AnySwap) swapped = true;
                perStep?.Invoke(i);
            }
            tx.Commit();
        }
        h.Rt.ApplyNow();
        int e1 = (h.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? 0;
        return (real.EditFlushes - f0, real.EditRebuilds - r0, e1 - e0, ms, swapped);
    }

    static void Ml6(McHost h)
    {
        string key = NewScene(h);
        string image = TestImage(h, new Color(0.5f, 0.5f, 0.1f, 1f));
        var ctx = Ctx(h, key);
        int ck = Enumerable.Range(0, 8).First(k => ctx.Clothes[k]);
        var cloth = ClothesTarget(ck);
        var ch = CharacterTarget();
        var cases = Cases(h, key, cloth, image);
        var fc = cases.First(c => c.Type == "float");
        var cc = cases.First(c => c.Type == "color");
        var tc = cases.First(c => c.Type == "texture");
        // the card imports on its first edit, which may be a rebuild: taken out of the counts
        Edit(h, key, cloth, fc.Make(h, key));
        SetArray(h, key, cloth, MaterialEdits.Remove(cloth.List(Stored(h, key)), fc.Make(h, key).Key));
        var live0 = Snap(h, key);
        var baseF = Named(h, key, cloth, fc.MaterialName)[0].GetFloat(fc.Property);
        string fMat = fc.MaterialName, fProp = fc.Property, cMat = cc.MaterialName, cProp = cc.Property, tMat = tc.MaterialName, tProp = tc.Property;
        var d1 = Drag(h, key, cloth, 120, i => MaterialEdit.Float(fMat, fProp.Substring(1), baseF + 0.001f * (i + 1), baseF));
        Report("ML6 float", d1, 120);
        Check("ML6 float: the last value", Math.Abs(Named(h, key, cloth, fMat)[0].GetFloat(fProp) - (baseF + 0.12f)) < 1e-5f, "");
        var baseC = Named(h, key, cloth, cMat)[0].GetColor(cProp);
        var d2 = Drag(h, key, cloth, 120, i => MaterialEdit.ColorOf(cMat, cProp.Substring(1), new Color4(0.5f, i / 120f, 0.25f, 1f), C4(baseC)));
        Report("ML6 colour", d2, 120);
        var baseO = Named(h, key, cloth, tMat)[0].GetTextureOffset(tProp);
        var d3 = Drag(h, key, cloth, 120, i => MaterialEdit.Texture(tMat, tProp.Substring(1)).WithOffset(new Vec2(0.01f * i, 0.5f), new Vec2(baseO.x, baseO.y)));
        Report("ML6 offset", d3, 120);
        Check("ML6 offset: the last value", Math.Abs(Named(h, key, cloth, tMat)[0].GetTextureOffset(tProp).x - 1.19f) < 1e-5f, "");

        // an imported item: a drag through the baseline keeps its place and its stored original
        var list = new List<MaterialOverride>(cloth.List(Stored(h, key)));
        var imported = new MeFloat { Material = fMat, Property = fProp.Substring(1), Value = baseF + 0.3f, Original = 0.1234f };
        int place = list.FindIndex(x => x is MeFloat f && f.Material == fMat && f.Property == fProp.Substring(1));
        list[place] = imported;
        SetArray(h, key, cloth, list);
        var d4 = Drag(h, key, cloth, 21, i => MaterialEdit.Float(fMat, fProp.Substring(1), i == 10 ? baseF : baseF + 0.5f + 0.01f * i, baseF));
        Report("ML6 through the baseline", d4, 21);
        var now = cloth.List(Stored(h, key));
        Check("ML6 the imported item keeps its place", now.Count == list.Count && now[place] is MeFloat, $"{now.Count} items, was {list.Count}");
        Check("ML6 the imported item keeps its original", now[place] is MeFloat g && g.Original == 0.1234f, "");

        // a drag that ends where it began
        var at = cloth.List(Stored(h, key));
        string json = Json(at);
        float start = ((MeFloat)at[place]).Value;
        var d5 = Drag(h, key, cloth, 11, i => MaterialEdit.Float(fMat, fProp.Substring(1), i == 10 ? start : start + 0.01f * (i + 1), baseF));
        Check("ML6 a drag back to the start adds no entry", d5.entries == 0, d5.entries + " entries");
        Check("ML6 the array is equal", Json(cloth.List(Stored(h, key))) == json, "");

        // the Character object takes the same route, not the rebuild
        var bf = Cases(h, key, ch, image).First(c => c.Type == "float");
        Edit(h, key, ch, bf.Make(h, key));
        string bMat = bf.MaterialName, bProp = bf.Property;
        var d6 = Drag(h, key, ch, 120, i => MaterialEdit.Float(bMat, bProp.Substring(1), 0.001f * (i + 1), null));
        Report("ML6 character float", d6, 120);
    }

    static void Report(string what, (int flushes, int rebuilds, int entries, List<double> ms, bool swapped) d, int steps)
    {
        Check(what + ": one entry", d.entries == 1, d.entries + " entries");
        Check(what + ": no rebuild", d.rebuilds == 0, d.rebuilds + " rebuilds");
        Check(what + ": no part swap", !d.swapped, "");
        Check(what + ": flushes <= steps + the commit", d.flushes <= steps + 1, d.flushes + " flushes for " + steps + " steps");
        Info($"{what}: step median {Median(d.ms):F3} ms, max {d.ms.Max():F3} ms (Editor, headless; the accepted numbers are the player's)");
    }

    // What the URP mapping reads is an input a value pass does not replay, so on a character a change of it reloads the part or rebuilds the
    // Character object (MeStructure.OfMapped); a move that keeps it is a pass. Each step then equals a fresh build.
    static void Ml11(McHost h)
    {
        string key = NewScene(h);
        var cand = MaterialShaders.All.FirstOrDefault(c => c.Keywords.Contains(ShaderMap.ReceiveShadowsOff) && c.Queue <= 2500);
        if (!Check("ML11 a candidate with the shadow switch and a queue up to 2500", cand != null)) return;
        var ctx = Ctx(h, key);
        var t = ClothesTarget(Enumerable.Range(0, 8).First(k => ctx.Clothes[k]));
        var m0 = PickMaterial(h, key, t, m => m.shader && MaterialShaders.IsDirectName(m.shader.name));
        if (!Check("ML11 a clothes material", m0 != null)) return;
        string mat = MaterialEditorApplier.Fmt(m0.name);
        Info($"ML11 {cand.Name}, default queue {cand.Queue}");
        Ml11Step(h, key, t, "the swap", "reload", () => Edit(h, key, t, MaterialEdit.ShaderItem(mat).WithShader(cand.Name, m0.shader.name)));
        bool On() => Named(h, key, t, mat).All(m => m.IsKeywordEnabled(ShaderMap.ReceiveShadowsOff));
        void Queue(string what, int? queue, string route, bool on, int shown)
        {
            Ml11Step(h, key, t, what, route, () => Edit(h, key, t, MaterialEdit.ShaderItem(mat).WithQueue(queue)));
            Check($"ML11 {what}: the switch {(on ? "on" : "off")}, queue {shown}", On() == on && Named(h, key, t, mat)[0].renderQueue == shown, On() + " " + Named(h, key, t, mat)[0].renderQueue);
        }
        Queue("a first add over 2500", 3000, "reload", true, 3000);
        Queue("a move inside the band", 4000, "pass", true, 4000);
        Queue("a crossing under 2500", 2000, "reload", false, 2000);
        Queue("a move inside that band", 2100, "pass", false, 2100);
        Queue("the queue cleared", null, "reload", false, cand.Queue);
        Queue("a first add in the material's own band", 2100, "reload", false, 2100);
        Queue("the queue cleared again", null, "reload", false, cand.Queue);
        // a keyword item on the switch decides it, and stays when the queue goes
        Queue("a queue under the keyword item", 3000, "reload", true, 3000);
        Ml11Step(h, key, t, "the keyword item", "pass", () => Edit(h, key, t, MaterialEdit.Keyword(mat, ShaderMap.ReceiveShadowsOff.Substring(1), true)));
        Queue("the queue cleared under the keyword item", null, "reload", true, cand.Queue);

        // the Character object: a rebuild, not a reload
        key = NewScene(h);
        var ch = CharacterTarget();
        var body = PickMaterial(h, key, ch, m => m.shader && MaterialShaders.IsDirectName(m.shader.name));
        if (Check("ML11 a material of the Character object", body != null))
        {
            string bm = MaterialEditorApplier.Fmt(body.name);
            int q0 = body.renderQueue, q1 = q0 + 1 <= 2449 || (q0 >= 2450 && q0 < 2500) ? q0 + 1 : q0 - 1, q2 = q1 == q0 + 1 ? q1 + 1 : q1 - 1;
            Ml11Step(h, key, ch, "Character object, a first add", "rebuild", () => Edit(h, key, ch, MaterialEdit.ShaderItem(bm).WithQueue(q1)));
            Ml11Step(h, key, ch, "Character object, a move inside the band", "pass", () => Edit(h, key, ch, MaterialEdit.ShaderItem(bm).WithQueue(q2)));
            Ml11Step(h, key, ch, "Character object, the removal", "rebuild", () => SetArray(h, key, ch, MaterialEdits.Remove(ch.List(Stored(h, key)), MeKey.Shader(bm))));
        }

        // an emission colour from black, an image on _BumpMap
        key = NewScene(h);
        ctx = Ctx(h, key);
        t = ClothesTarget(Enumerable.Range(0, 8).First(k => ctx.Clothes[k]));
        string image = TestImage(h, new Color(0.5f, 0.5f, 1f, 1f)), other = TestImage(h, new Color(0.4f, 0.6f, 1f, 1f));
        var tc = t;
        var em = PickMaterial(h, key, t, m => MaterialWrites.Declared(m, "_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent == 0f);
        if (em != null)
        {
            string n = MaterialEditorApplier.Fmt(em.name);
            var ek = MeKey.Property("color", n, "EmissionColor");
            Ml11Step(h, key, t, "emission from black", "reload", () => Edit(h, key, tc, MaterialEdit.ColorOf(n, "EmissionColor", new Color4(0.5f, 0.2f, 0.1f, 1f))));
            Ml11Step(h, key, t, "emission above 0 to above 0", "pass", () => Edit(h, key, tc, MaterialEdit.ColorOf(n, "EmissionColor", new Color4(0.6f, 0.1f, 0.1f, 1f))));
            Ml11Step(h, key, t, "emission back to black", "reload", () => Edit(h, key, tc, MaterialEdit.ColorOf(n, "EmissionColor", new Color4(0f, 0f, 0f, 1f))));
            Ml11Step(h, key, t, "the emission item removed", "reload", () => SetArray(h, key, tc, MaterialEdits.Remove(tc.List(Stored(h, key)), ek)));
        }
        else Info("ML11 no clothes material with a black emission colour a build reaches: that case is not covered here");
        var bump = PickMaterial(h, key, t, m => MaterialWrites.Declared(m, "_BumpMap"));
        if (bump != null)
        {
            string n = MaterialEditorApplier.Fmt(bump.name);
            var bk = MeKey.Property("texture", n, "BumpMap");
            Ml11Step(h, key, t, "a first image on _BumpMap", "reload", () => Edit(h, key, tc, MaterialEdit.Texture(n, "BumpMap").WithImage(image)));
            Ml11Step(h, key, t, "another image", "pass", () => Edit(h, key, tc, MaterialEdit.Texture(n, "BumpMap").WithImage(other)));
            Ml11Step(h, key, t, "an offset beside it", "pass", () => Edit(h, key, tc, MaterialEdit.Texture(n, "BumpMap").WithOffset(new Vec2(0.25f, 0f))));
            Ml11Step(h, key, t, "the image item removed", "reload", () => SetArray(h, key, tc, MaterialEdits.Remove(tc.List(Stored(h, key)), bk)));
        }
        else Info("ML11 no clothes material with _BumpMap a build reaches: that case is not covered here");

        // a routed or fallback material, if a target has one
        int found = 0;
        foreach (bool male in new[] { false, true })
        {
            key = NewScene(h, male);
            foreach (var x in Targets(h, key))
                foreach (var r in MaterialEditorApplier.ObjectRenderers(Ctx(h, key), x.Object) ?? new List<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (m && (ShaderMap.IsFallback(m) || (ShaderMap.MappedFrom(m) is Shader s && ShaderMap.StandardRouted.Contains(s.name)))) found++;
        }
        Info($"ML11 routed or fallback materials on the two targets: {found}" + (found == 0 ? ": live against fresh on such a material is not covered here (MY3 counts the cards' items on them; MaterialEditTest.values builds those)" : ""));
    }

    // One step on a target: its route, and the node equal to a fresh build. Fresh loads the scene, so each step starts on a built node again.
    static void Ml11Step(McHost h, string key, Target t, string what, string route, Action step)
    {
        var real = Real(h, key);
        int r0 = real.EditRebuilds;
        step();
        var now = Real(h, key);
        bool rebuilt = now != real || now.EditRebuilds > r0;
        string got = rebuilt ? "rebuild" : now.LastEditFlush != null && now.LastEditFlush.AnySwap ? "reload" : "pass";
        Check($"ML11 {what}: a {route}", got == route, got);
        var live = Snap(h, key);
        Same(Fresh(h, key, "ml11"), live, $"ML11 {what} = a fresh build");
    }

    static void Ml8(McHost h)
    {
        string key = NewScene(h);
        string image = TestImage(h, new Color(0.3f, 0.3f, 0.3f, 1f));
        var ctx = Ctx(h, key);
        int hk = Enumerable.Range(0, 4).First(k => ctx.Hair[k]);
        foreach (var t in new[] { HairTarget(hk), CharacterTarget() })
        {
            var cases = Cases(h, key, t, image);
            var sc = cases.FirstOrDefault(c => c.Type == "shader");
            if (sc == null) { Warn("ML8", t.Name + ": no HS2Next shader to swap from"); continue; }
            // a value item on the same material
            var fl = cases.FirstOrDefault(c => c.Type == "float" && c.MaterialName == sc.MaterialName);
            if (fl != null) Edit(h, key, t, fl.Make(h, key));
            var before = Snap(h, key);
            string was = sc.Read(h, key);
            var real = Real(h, key);
            int r0 = real.EditRebuilds;
            var e = sc.Make(h, key);
            Edit(h, key, t, e);
            bool rebuilt = Real(h, key) != real || Real(h, key).EditRebuilds > r0;
            Check($"ML8 {t.Name}: {(t.Object.Item1 == OT.Character ? "a rebuild" : "a reload")}", t.Object.Item1 == OT.Character ? rebuilt : !rebuilt && Real(h, key).LastEditFlush != null && Real(h, key).LastEditFlush.AnySwap,
                Real(h, key).LastEditFlush?.ToString() ?? "");
            Check($"ML8 {t.Name}: the shader", sc.Read(h, key) == sc.After, sc.Read(h, key));
            Check($"ML8 {t.Name}: the default queue of the new shader", Named(h, key, t, sc.MaterialName)[0].renderQueue == MaterialShaders.Find(sc.After).Queue, Named(h, key, t, sc.MaterialName)[0].renderQueue.ToString());
            if (fl != null && Named(h, key, t, sc.MaterialName)[0].HasProperty(fl.Property)) Check($"ML8 {t.Name}: the value item after the swap", fl.Read(h, key) == fl.After, fl.Read(h, key));
            else if (fl != null)
            {
                var st = MaterialEditorApplier.Status(Ctx(h, key), t.Object, t.List(Stored(h, key)).First(x => x is MeFloat));
                Check($"ML8 {t.Name}: a value item the new shader lacks says so", st.Status == MaterialStatus.NotOnShader, st.Status);
            }
            Undo(h, $"ML8 {t.Name}: undo");
            Check($"ML8 {t.Name}: the original shader back", sc.Read(h, key) == was, sc.Read(h, key));
            if (fl != null) Check($"ML8 {t.Name}: the value back", fl.Read(h, key) == fl.After, fl.Read(h, key));
            Same(before, Snap(h, key), $"ML8 {t.Name}: undo = the state before");
        }
    }

    static void Ml9(McHost h)
    {
        var sh = Shader.Find("HS2Next/AIT Clothes Alpha True");
        if (!Check("ML9 the shader with a vector-typed _DetailUV", sh, "")) return;
        int i = sh.FindPropertyIndex("_DetailUV");
        Check("ML9 _DetailUV is a Vector", i >= 0 && sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Vector, i >= 0 ? sh.GetPropertyType(i).ToString() : "absent");
        Info($"ML9 colour space {QualitySettings.activeColorSpace}");
        var m = new Material(sh);
        var values = new[] { new Vector4(0.5f, 0.25f, 2f, 3f), new Vector4(1.7f, -0.3f, 0.1f, 4f), new Vector4(0.2159f, 0.5f, 0.7354f, 1f), new Vector4(0f, 1f, 0.01f, 100f) };
        int differ = 0;
        double worst = 0;
        foreach (var v in values)
        {
            MaterialWrites.SetColor(m, "_DetailUV", new Color(v.x, v.y, v.z, v.w), null);
            var got = m.GetVector("_DetailUV");
            double d = Math.Max(Math.Max(Math.Abs(got.x - v.x), Math.Abs(got.y - v.y)), Math.Max(Math.Abs(got.z - v.z), Math.Abs(got.w - v.w)));
            worst = Math.Max(worst, d);
            if (d != 0) differ++;
            Info($"ML9 stored {v.x:R} {v.y:R} {v.z:R} {v.w:R} -> GetVector {got.x:R} {got.y:R} {got.z:R} {got.w:R}");
        }
        UnityEngine.Object.DestroyImmediate(m);
        // the same through a stored item on a built part, in a build and in the value pass
        string key = NewScene(h);
        var ctx = Ctx(h, key);
        int ck = Enumerable.Range(0, 8).First(k => ctx.Clothes[k]);
        var cloth = ClothesTarget(ck);
        var part = MaterialEditorApplier.ObjectRenderers(ctx, cloth.Object).SelectMany(r => r.sharedMaterials).First(x => x && !MosaicSet.IsMosaicMaterial(x));
        string mat = MaterialEditorApplier.Fmt(part.name);
        var stored = new Color4(0.5f, 0.25f, 2f, 3f);
        SetArray(h, key, cloth, new List<MaterialOverride> { new MeShader { Material = mat, Shader = sh.name }, new MeColor { Material = mat, Property = "DetailUV", Value = stored } });
        var built = Named(h, key, cloth, mat)[0].GetVector("_DetailUV");
        Info($"ML9 a stored item through a part reload: {built.x:R} {built.y:R} {built.z:R} {built.w:R}");
        var stored2 = new Color4(0.2159f, 0.5f, 0.7354f, 1f);
        SetArray(h, key, cloth, new List<MaterialOverride> { new MeShader { Material = mat, Shader = sh.name }, new MeColor { Material = mat, Property = "DetailUV", Value = stored2 } });
        var pass = Named(h, key, cloth, mat)[0].GetVector("_DetailUV");
        Info($"ML9 a stored item through the value pass: {pass.x:R} {pass.y:R} {pass.z:R} {pass.w:R}");
        foreach (var (got, want) in new[] { (built, stored), (pass, stored2) })
        {
            double d = Math.Max(Math.Max(Math.Abs(got.x - want.R), Math.Abs(got.y - want.G)), Math.Max(Math.Abs(got.z - want.B), Math.Abs(got.w - want.A)));
            worst = Math.Max(worst, d);
            if (d != 0) differ++;
        }
        if (differ == 0) Check("ML9 a colour item on a vector property lands unchanged", true, $"{values.Length} values on a material, 2 stored items, largest difference 0");
        else Warn("ML9", $"a colour item on a vector property moves: {differ} of {values.Length} values, largest difference {worst:G6} (no fix here)");
    }

    static void Ml10(McHost h)
    {
        string key = NewScene(h);
        string image = TestImage(h, new Color(0.3f, 0.6f, 0.3f, 1f));
        var ch = CharacterTarget();
        var bf = Cases(h, key, ch, image).First(c => c.Type == "float");
        var before = Snap(h, key);
        string was = bf.Read(h, key);
        var e = bf.Make(h, key);
        Edit(h, key, ch, e);
        Check("ML10 the plugin data holds the item", Ctx(h, key).Ext.MaterialEditor != null, "");
        SetArray(h, key, ch, MaterialEdits.Remove(ch.List(Stored(h, key)), e.Key));
        Check("ML10 the array is empty", ch.List(Stored(h, key)).Count == 0, "");
        Check("ML10 no material items left in the plugin data", Ctx(h, key).Ext.MaterialEditor == null, "");
        Check("ML10 the baseline is written", bf.Read(h, key) == was, bf.Read(h, key) + " want " + was);
        Same(before, Snap(h, key), "ML10 the state before");
        var live = Snap(h, key);
        Same(Fresh(h, key, "ml10"), live, "ML10 statistics and all = a fresh build");
    }

    // ---- the value pass on the installed cards (opt-in; -limit N, -from N)

    /// <summary>
    /// Every installed character card with items: its value items leave through the value pass, then come back, and after each step
    /// the node equals a fresh build of the same documents. It proves on real items what ML2 proves on synthetic ones: the fallback
    /// materials, properties a replacement lacks, images, Enabled items. Cards by hash, numbers only.
    /// </summary>
    public static void RunValues()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString()));
        int from = int.Parse(Arg("-from", "0"));
        var only = Arg("-hash")?.Split('+');
        var seed = HS2Next.Motion.Face.FaceSetup.Seed;
        HS2Next.Motion.Face.FaceSetup.Seed = 12345;
        int cards = 0, seen = 0, strippedEqual = 0, backEqual = 0, rebuilt = 0, rebuiltForNothing = 0, items = 0, passArrays = 0, moved = 0;
        var classes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var head = new System.Text.StringBuilder();
        var t = TestTargets.Load();
        try
        {
            using (var h = MakerCoreTest.Harness.Open(head))
                foreach (var dir in new[] { TestTargets.Abs(t.cards.female), TestTargets.Abs(t.cards.male) })
                    foreach (var path in ImportTest.FilesOf(dir, int.MaxValue))
                    {
                        if (cards >= limit) break;
                        string rel = path.Substring(TestTargets.UserData.Length).TrimStart('\\', '/').Replace('\\', '/');
                        string hash = MaterialEditTest_Corpus.FileHash(rel);
                        if (only != null && !only.Contains(hash)) continue;
                        var c = MakerEditTest.Import(h, path, out _);
                        if (c == null || !Arrays(c.Subtree).Any(x => x.list.Count > 0)) continue;
                        if (seen++ < from) continue;
                        cards++;
                        try { ValueCard(h, c, hash, classes, ref strippedEqual, ref backEqual, ref rebuilt, ref rebuiltForNothing, ref items, ref passArrays, ref moved); }
                        catch (Exception e) { Check(hash + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                        if (cards % 10 == 0) { h.ReleaseBundles(); Debug.Log($"MaterialEditTest values: {cards} cards, {sw.Elapsed.TotalSeconds:F0} s"); }
                    }
        }
        finally { HS2Next.Motion.Face.FaceSetup.Seed = seed; }
        Check("values: every card's value items left in place = a fresh build", strippedEqual == cards && cards > 0, $"{strippedEqual} of {cards} cards, {items} items");
        Check("values: and came back = a fresh build", backEqual == cards && cards > 0, $"{backEqual} of {cards} cards");
        // what the mapping reads reloads or rebuilds by construction: a rebuild is right only where the Character array's text changes
        Check("values: a rebuild only where what the mapping reads changes on the Character object", rebuiltForNothing == 0, rebuiltForNothing + " cards");
        Info($"values: {rebuilt} of {cards} cards rebuilt; arrays that left through a pass {passArrays}, through a reload or rebuild {moved}");
        foreach (var kv in classes) Info($"class {kv.Key}: {kv.Value}");
        Finish("material_edit_values_report.txt", "MaterialEditTest values: the value pass on the installed cards", sw, "MATERIAL EDIT TEST");
    }

    static IEnumerable<(string path, List<MaterialOverride> list)> Arrays(CharaSubtree s)
    {
        yield return ("appearance.chara.materialOverrides", s.Chara.MaterialOverrides);
        for (int k = 0; k < 4; k++) yield return ("appearance.chara.hair." + HS2Next.Chara.Edit.UnitMap.HairParts[k] + ".materialOverrides", s.Chara.Hair.Parts[k].MaterialOverrides);
        for (int k = 0; k < 8; k++) yield return ("appearance.coord.clothes." + HS2Next.Chara.Edit.UnitMap.ClothesKinds[k] + ".materialOverrides", s.Coord.Clothes.Parts[k].MaterialOverrides);
        foreach (var a in s.Coord.Accessories) yield return ("appearance.coord.accessories[\"" + a.Id + "\"].materialOverrides", a.MaterialOverrides);
    }

    static void ValueCard(MakerCoreTest.Harness h, CharaContainer c, string hash, SortedDictionary<string, int> classes, ref int strippedEqual, ref int backEqual, ref int rebuilt, ref int rebuiltForNothing, ref int items, ref int passArrays, ref int moved)
    {
        var a = MakerEditTest.NewNode(h, c, hash);
        try
        {
            if (!Check(hash + ": built", a.Ok, a.Error ?? "")) return;
            var strip = new List<(string, object)>();
            var back = new List<(string, object)>();
            bool characterMoves = false;
            foreach (var (path, list) in Arrays(a.Stored))
            {
                if (list.Count == 0) continue;
                var keep = list.Where(MeStructure.IsStructural).ToList();
                items += list.Count - keep.Count;
                if (keep.Count == list.Count) continue;
                if (MeStructure.OfMapped(keep) == MeStructure.OfMapped(list)) passArrays++;
                else
                {
                    moved++;
                    if (path == "appearance.chara.materialOverrides") characterMoves = true;
                }
                strip.Add((path, keep));
                back.Add((path, new List<MaterialOverride>(list)));
            }
            int r0 = a.Real.EditRebuilds;
            var o = new ExecuteOptions { Source = CommandSource.User, Label = "Values" };
            if (strip.Count > 0)
            {
                var op = a.Ops.EditAppearance(new[] { a.Id }, strip, o, allowRebuild: true);
                if (!op.Ok) Check(hash + ": strip accepted", false, op.Code + " " + op.Message);
                a.Apply();
            }
            var live = a.Snapshot();
            CharaSnapshot fresh;
            using (var f = MakerEditTest.NewNode(h, a.Session.Model.File.ExtractCharacter(a.Id), hash + " fresh")) fresh = f.Snapshot();
            if (Compared(hash + " left", fresh, live, classes)) strippedEqual++;
            if (strip.Count > 0)
            {
                var op = a.Ops.EditAppearance(new[] { a.Id }, back, o, allowRebuild: true);
                if (!op.Ok) Check(hash + ": back accepted", false, op.Code + " " + op.Message);
                a.Apply();
            }
            var live2 = a.Snapshot();
            CharaSnapshot fresh2;
            using (var f = MakerEditTest.NewNode(h, a.Session.Model.File.ExtractCharacter(a.Id), hash + " fresh 2")) fresh2 = f.Snapshot();
            if (Compared(hash + " back", fresh2, live2, classes)) backEqual++;
            else
            {
                var st = a.Real.Context.Stats;
                Info($"{hash} absent properties: " + string.Join(", ", st.MeAbsentProperties.Select(kv => kv.Key + " " + kv.Value)));
                foreach (var (path, list) in Arrays(a.Stored))
                    foreach (var it in list.OfType<MeMaterialProperty>().Where(x => st.MeAbsentProperties.Keys.Any(k => k.EndsWith(" _" + x.Property, StringComparison.Ordinal))))
                    {
                        var mats = a.Real.Context.Root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m && MaterialEditorApplier.Fmt(m.name) == it.Material).Distinct().ToList();
                        Info($"{hash} {path} {it.Type} {it.Material} {it.Property}: " + string.Join("; ", mats.Select(m => m.shader.name + " from " + (ShaderMap.MappedFrom(m) ? ShaderMap.MappedFrom(m).name : "none") + " fallback " + ShaderMap.IsFallback(m))));
                    }
            }
            if (a.Real.EditRebuilds > r0)
            {
                rebuilt++;
                if (!characterMoves) rebuiltForNothing++;
            }
        }
        finally { a.Dispose(); }
    }

    // equal but for bones and motion, which a second step of a live node moves; the rest by class
    static bool Compared(string what, CharaSnapshot fresh, CharaSnapshot live, SortedDictionary<string, int> classes)
    {
        var d = CharaSnapshot.Compare(fresh, live, 100000);
        var lines = d.Lines.Where(l => !l.Contains(" bone|") && !l.Contains(" motion|") && !OnlyBounds(l)).ToList();
        if (lines.Count == 0) return true;
        foreach (var l in lines)
        {
            int bar = l.IndexOf('|'), colon = l.IndexOf(": ", StringComparison.Ordinal);
            string section = bar > 2 ? l.Substring(2, bar - 2) : l;
            string token = colon > 0 && !l.StartsWith("~ ctx", StringComparison.Ordinal) ? l.Substring(colon + 2).Split(new[] { '=', ' ' }, 2)[0] : "";
            string k = section + " " + token;
            classes[k] = classes.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        var ctx = fresh.Lines.Where(l => l.StartsWith("ctx", StringComparison.Ordinal)).Zip(live.Lines.Where(l => l.StartsWith("ctx", StringComparison.Ordinal)), (x, y) => (x, y)).Where(t => t.x != t.y).ToList();
        Check(what + " = a fresh build", false, lines.Count + " lines:\n  " + string.Join("\n  ", lines.Take(6).Select(Where)) +
              string.Join("", ctx.Select(t => "\n  " + Parted(t.x, t.y))));
        return false;
    }

    static string Parted(string x, string y)
    {
        int i = 0;
        while (i < x.Length && i < y.Length && x[i] == y[i]) i++;
        int from = Math.Max(0, i - 60);
        return "…" + x.Substring(from, Math.Min(240, x.Length - from)) + "\n   <> …" + y.Substring(from, Math.Min(240, y.Length - from));
    }

    // where two sides of a differing line part
    static string Where(string l)
    {
        int colon = l.IndexOf(": ", StringComparison.Ordinal), mid = l.IndexOf(" <> ", StringComparison.Ordinal);
        if (colon < 0 || mid < 0) return Short(l.Substring(Math.Max(0, l.LastIndexOf('/'))), 500);
        string x = l.Substring(colon + 2, mid - colon - 2), y = l.Substring(mid + 4);
        int i = 0;
        while (i < x.Length && i < y.Length && x[i] == y[i]) i++;
        int from = Math.Max(0, i - 40);
        return l.Substring(0, Math.Min(colon, 30)) + ": …" + x.Substring(from, Math.Min(200, x.Length - from)) + " <> …" + y.Substring(from, Math.Min(200, y.Length - from));
    }

    static void Mv1()
    {
        MaterialShaders.Reset();
        var all = MaterialShaders.All;
        Check("MV1 the list is not empty", all.Count > 0, all.Count + " candidates");
        Check("MV1 no mosaic shader, no hidden or UI shader, no shader of NeedsHook",
            all.All(c => !MosaicSet.IsMosaicShaderName(c.Name) && !c.Name.StartsWith("HS2Next/Hidden/") && !c.Name.StartsWith("HS2Next/UI/") && !c.Name.Contains("MirrorReflection") && c.Name != ShaderMap.WaterShader && !c.Name.EndsWith("ME/Distortion")), "");
        foreach (var c in all) Info($"MV1 {c.Name} | {c.Family} | {c.Queue} | {string.Join(" ", c.Keywords)}");
        foreach (var kv in MaterialShaders.LeftOut) Info($"MV1 left out {kv.Key}: {kv.Value}");
    }
}
