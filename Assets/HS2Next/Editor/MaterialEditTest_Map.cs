// MaterialEditTest.play, the runner and the map groups (package I). Maps load only in play mode, so everything
// that needs one lives here. The app with the UI on port 17410; numbers only, nothing is rendered or shown.
//   MI4   the map: float, color, texture, keyword, renderer Enabled: set, undo, redo; a shader swap and its undo: the material back to the last
//         digit, `scene.json` byte-equal after the undo; save, scene.new, load: the values again.
//   MI6   an edit on a map, unload, the same row, another row of the same bundle: no value leaked; the bundle's materials are never in the index;
//         the map half of MT7: the decoded image goes with the map.
//   MI9   a texture offset on a scrolled material reads `driven`, kept; an image on it is written; a script's offset keeps running.
//   MI10  map.load of another row with an array, undo: empty, then back, one entry, the values again.
//   MW4   the map's index and path index on the largest vanilla map: reported, nothing judged. -mw4 <ref> names another map: the one opt-in run on the
//         86,512-renderer row is -only MW4 -mw4 "[Fouetty]Fantasy Cemetery Enviroment#0" -mw4LoadSec 600; it does not belong to the row.
// Other packages add groups through PlayGroups (a partial method, MaterialEditTest_Ui.cs). Run: & Tools\unity_batch.ps1 -Method MaterialEditTest.RunPlay
// -NoQuit [-MethodArgs @('-only','MI4+MI9')]. Report Spike/out/maker/material_edit_play_report.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using static MakerTestKit;

public static partial class MaterialEditTest
{
    public const int PlayPort = 17410;
    const string PlayMap = "illusion.hs2#35", SameBundleA = "hooh.map.private.club#2", SameBundleB = "hooh.map.private.club#7", LargestVanilla = "illusion.hs2#0";

    static string PlayReport => Path.Combine(OutDir, "material_edit_play_report.txt");

    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"Spike/out/maker/material_play_userdata\",\"bridge\":{\"port\":" + PlayPort +
        ",\"outputDir\":\"Spike/out/maker/material_play_out\",\"allowQuit\":false}}";

    /// <summary>Further groups of the play row: (id, title, run).</summary>
    static partial void PlayGroups(List<(string id, string title, Func<IEnumerator> run)> groups);

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MaterialEditTest) + "." + nameof(PlayMain), PlayReport, 25, true, PlayLaunch);
    }

    // A queue means the same on the map as on a character: the shadow switch and the fog stamp follow it, and a clear gives them back.
    static IEnumerator MiqMap()
    {
        yield return LoadMap(LargestVanilla, "MIQ");
        var entries = MapTarget.Materials.Where(e => e.Material && e.Material.shader && e.Material.renderQueue <= 2500 && MaterialShaders.IsDirectName(e.Material.shader.name)).ToList();
        var e0 = entries.FirstOrDefault(e => e.Material.shader.keywordSpace.FindKeyword(ShaderMap.ReceiveShadowsOff).isValid && ShaderMap.MappedFrom(e.Material) && e.Material.HasProperty(HS2FogFeature.ModesProperty) && HS2FogFeature.ModesOf(ShaderMap.MappedFrom(e.Material)) != 0)
              ?? entries.FirstOrDefault(e => e.Material.shader.keywordSpace.FindKeyword(ShaderMap.ReceiveShadowsOff).isValid);
        if (!Check("MIQ a map material with the shadow switch", e0 != null, entries.Count + " under 2500 on HS2Next shaders")) yield break;
        string name = e0.Name;
        var from = ShaderMap.MappedFrom(MapMat(name));
        int modes = from && MapMat(name).HasProperty(HS2FogFeature.ModesProperty) ? HS2FogFeature.ModesOf(from) : -1;
        Info($"MIQ {MapMat(name).shader.name}, queue {MapMat(name).renderQueue}, fog modes of the original {modes}");
        if (modes <= 0) Info("MIQ this map has no such material with fog variants: the stamp is shown on a character's material in MIQ of .full");
        int q0 = MapMat(name).renderQueue;
        bool on0 = MapMat(name).IsKeywordEnabled(ShaderMap.ReceiveShadowsOff);
        float f0 = MapMat(name).HasProperty(HS2FogFeature.ModesProperty) ? MapMat(name).GetFloat(HS2FogFeature.ModesProperty) : 0f;
        yield return OkCall(new { cmd = "map.materials.set", type = "shader", material = name, renderQueue = 3000 }, "MIQ a queue over 2500");
        yield return Settled();
        Check("MIQ over 2500: the switch on", MapMat(name).IsKeywordEnabled(ShaderMap.ReceiveShadowsOff) && MapMat(name).renderQueue == 3000, MapMat(name).renderQueue.ToString());
        if (modes > 0) Check("MIQ over 2500: the fog stamp is the original's", (int)MapMat(name).GetFloat(HS2FogFeature.ModesProperty) == modes, MapMat(name).GetFloat(HS2FogFeature.ModesProperty).ToString());
        yield return OkCall(new { cmd = "map.materials.clear", all = true }, "MIQ the clear");
        yield return Settled();
        Check("MIQ the clear: queue and switch back", MapMat(name).renderQueue == q0 && MapMat(name).IsKeywordEnabled(ShaderMap.ReceiveShadowsOff) == on0, MapMat(name).renderQueue + " " + MapMat(name).IsKeywordEnabled(ShaderMap.ReceiveShadowsOff));
        if (modes > 0) Check("MIQ the clear: the fog stamp back", MapMat(name).GetFloat(HS2FogFeature.ModesProperty) == f0, MapMat(name).GetFloat(HS2FogFeature.ModesProperty).ToString());
    }

    static AppCore App => AppCore.Current;
    static MapRealization MapPart => App.Runtime.GetPart<MapRealization>();
    static MaterialTargets MapTarget => MapPart.MaterialEdits?.Target;
    static List<MaterialOverride> MapArray => App.Session.Model.Settings.Map.MaterialOverrides;

    static JObject last;
    static string lastText;
    static bool LastOk => last?.Value<bool?>("ok") == true;

    static IEnumerator Call(object request, double timeoutSec = 180)
    {
        string json = request is string s ? s : JsonConvert.SerializeObject(request);
        last = null;
        lastText = null;
        var t = App.Bridge.Execute(json);
        var sw = Stopwatch.StartNew();
        while (!t.IsCompleted && sw.Elapsed.TotalSeconds < timeoutSec) yield return PlayModeHarness.WaitFrames(1);
        lastText = t.IsCompleted && !t.IsFaulted ? t.Result : "{\"ok\":false,\"code\":\"timeout\"}";
        try { last = JObject.Parse(lastText); }
        catch (Exception) { last = new JObject { ["ok"] = false }; }
    }

    static IEnumerator OkCall(object request, string what, double timeoutSec = 180)
    {
        yield return Call(request, timeoutSec);
        Check(what, LastOk, LastOk ? "" : Short(lastText));
    }

    static IEnumerator LoadMap(string mapRef, string what, double timeoutSec = 240)
    {
        yield return Call(new { cmd = "map.load", @ref = mapRef, wait = true }, timeoutSec);
        Check(what + ": " + mapRef + " loaded", LastOk && (string)last["state"] == "Loaded", Short(lastText));
    }

    static IEnumerator Settled()
    {
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => !MapPart.IsBusy, 240, ok);
        Check("the map settled", ok[0], MapPart.State.ToString());
        yield return PlayModeHarness.WaitFrames(2);
    }

    static IEnumerator PlayMain()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"MaterialEditTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var groups = new List<(string id, string title, Func<IEnumerator> run)>
        {
            ("MI4", "a map's values, a shader swap, a reload", Mi4),
            ("MI6", "no leak through a map's bundle", Mi6),
            ("MI9", "what a map script drives", Mi9),
            ("MI10", "another map", Mi10),
            ("MW4", "the index of the largest map", Mw4),
            ("MO3", "a member on the map", Mo3Map),
            ("MIQ", "a queue on the map", MiqMap),
        };
        PlayGroups(groups);
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        bool up = Check("app ready", ready[0] && App?.Stage == AppStage.Ready, App?.Stage.ToString());
        if (up)
            foreach (var (id, title, run) in groups)
            {
                if (!Want(id)) continue;
                Rep.AppendLine();
                Rep.AppendLine($"== {id} {title}");
                var stack = new Stack<IEnumerator>();
                stack.Push(run());
                while (stack.Count > 0)
                {
                    object cur;
                    try
                    {
                        if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                        cur = stack.Peek().Current;
                    }
                    catch (Exception e) { Check(id + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace, 1200)); break; }
                    if (cur is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return cur;
                }
            }
        Rep.Insert(0, $"SUMMARY: {Passes} PASS, {Fails} FAIL{(Warns > 0 ? ", " + Warns + " WARN" : "")} ({sw.Elapsed.TotalSeconds:F0} s)\nMaterialEditTest.play: MI4, MI6, MI9, MI10, MW4 and the groups of the other packages\n");
        File.WriteAllText(PlayReport, Rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        UnityEngine.Debug.Log($"MATERIAL EDIT PLAY TEST DONE: {Passes} PASS, {Fails} FAIL, {Warns} WARN");
        PlayModeHarness.ExitCode = Fails > 0 ? 1 : 0;
    }

    // ---- helpers

    public static void SetMapArray(List<MaterialOverride> next)
    {
        var r = App.Session.Set(null, "map.materialOverrides", next, UserEdit);
        if (r.Status == CommandStatus.Error) Check("the array is accepted", false, r.ToString());
        App.Runtime.ApplyNow();
    }

    public static bool MapEdit(MaterialEdit e)
    {
        var next = MaterialEdits.Set(MapArray, e);
        if (ReferenceEquals(next, MapArray)) return false;
        SetMapArray(next);
        return true;
    }

    static Material MapMat(string name) => MapTarget.Named(name).Select(e => e.Material).FirstOrDefault();

    static Material PickMapMaterial(Func<Material, bool> ok)
    {
        foreach (var e in MapTarget.Materials)
            if (e.Material && !ShaderMap.IsFallback(e.Material) && !MapPart.Scripts.Drives(e.Material, "_MainTex") && ok(e.Material)) return e.Material;
        return null;
    }

    static MaterialBaseline.Value? MapBase(Material m, MaterialMember kind, string prop)
    {
        if (MapTarget.Baseline.TryGet(m, kind, prop, out var v)) return v;
        return MaterialBaseline.TryRead(m, kind, prop, out v) ? v : (MaterialBaseline.Value?)null;
    }

    static List<ItemCase> MapCases(string image)
    {
        var l = new List<ItemCase>();
        var fm = PickMapMaterial(m => PickProperty(m, ShaderPropertyType.Float, ShaderPropertyType.Range) != null);
        if (fm)
        {
            string prop = PickProperty(fm, ShaderPropertyType.Float, ShaderPropertyType.Range), mat = MatName(fm);
            float was = fm.GetFloat(prop), to = was > 0.5f ? was - 0.25f : was + 0.25f;
            l.Add(new ItemCase
            {
                Type = "float", What = mat + " " + prop, Material = mat, Property = prop, After = Text(to),
                Make = () => MaterialEdit.Float(mat, prop.Substring(1), to, MapBase(MapMat(mat), MaterialMember.Float, prop)?.Float),
                Read = () => Text(MapMat(mat).GetFloat(prop)),
            });
        }
        var cm = PickMapMaterial(m => PickProperty(m, ShaderPropertyType.Color, ShaderPropertyType.Color) != null);
        if (cm)
        {
            string prop = PickProperty(cm, ShaderPropertyType.Color, ShaderPropertyType.Color), mat = MatName(cm);
            var to = new Color(0.25f, 0.5f, 0.75f, 1f);
            l.Add(new ItemCase
            {
                Type = "color", What = mat + " " + prop, Material = mat, Property = prop, After = Text(Near(to)),
                Make = () =>
                {
                    var b = MapBase(MapMat(mat), MaterialMember.Color, prop);
                    return MaterialEdit.ColorOf(mat, prop.Substring(1), C4(to), b.HasValue ? C4(b.Value.Color) : (Color4?)null);
                },
                Read = () => Text(Near(MapMat(mat).GetColor(prop))),
            });
        }
        var tm = PickMapMaterial(m => PickProperty(m, ShaderPropertyType.Texture, ShaderPropertyType.Texture) != null);
        if (tm)
        {
            string prop = PickProperty(tm, ShaderPropertyType.Texture, ShaderPropertyType.Texture), mat = MatName(tm);
            var off = new Vector2(0.125f, 0.25f);
            var sc = new Vector2(2f, 3f);
            l.Add(new ItemCase
            {
                Type = "texture", What = mat + " " + prop, Material = mat, Property = prop, After = "8x8 " + Text(off) + " " + Text(sc),
                Make = () => MaterialEdit.Texture(mat, prop.Substring(1)).WithImage(image).WithOffset(new Vec2(off.x, off.y)).WithScale(new Vec2(sc.x, sc.y)),
                Read = () =>
                {
                    var m = MapMat(mat);
                    var tex = m.GetTexture(prop);
                    return (tex ? tex.width + "x" + tex.height : "none") + " " + Text(m.GetTextureOffset(prop)) + " " + Text(m.GetTextureScale(prop));
                },
            });
        }
        var rr = MapTarget.Renderers.FirstOrDefault(r => r && r.enabled);
        if (rr)
        {
            string path = MapTarget.PathOf(rr);
            l.Add(new ItemCase
            {
                Type = "renderer", What = path, After = "off",
                Make = () => MaterialEdit.RendererEnabled(path, 0, 1),
                Read = () => { var x = new List<Renderer>(); MapTarget.SelectRenderers(path, x); return x[0].enabled ? "on" : "off"; },
            });
        }
        var km = PickMapMaterial(m => m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal) && Keyword(m) != null);
        if (km)
        {
            string kw = Keyword(km), mat = MatName(km);
            bool was = km.IsKeywordEnabled(kw);
            l.Add(new ItemCase
            {
                Type = "keyword", What = mat + " " + kw, Material = mat, Property = kw, After = !was ? "on" : "off",
                Make = () => MaterialEdit.Keyword(mat, kw.Substring(1), !was, was),
                Read = () => MapMat(mat).IsKeywordEnabled(kw) ? "on" : "off",
            });
        }
        return l;
    }

    static IEnumerator SceneJson(string name, byte[][] into)
    {
        yield return OkCall(new { cmd = "scene.save", name, overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "scene.save " + name);
        if (!LastOk) yield break;
        into[1] = Encoding.UTF8.GetBytes((string)last["path"]);
        using (var zip = ZipFile.OpenRead((string)last["path"]))
        using (var s = zip.GetEntry(SceneDocument.EntryName).Open())
        using (var ms = new MemoryStream())
        {
            s.CopyTo(ms);
            into[0] = ms.ToArray();
        }
    }

    static string MapPrint() => string.Join("\n", MapTarget.Materials.Select(e => Print(e.Material)).OrderBy(x => x, StringComparer.Ordinal));

    // ---- MI4

    static IEnumerator Mi4()
    {
        yield return OkCall(new { cmd = "scene.new" }, "MI4 scene.new");
        yield return LoadMap(PlayMap, "MI4");
        if (!LastOk) yield break;
        string image = App.Session.Model.File.Res.Add(TestPng(new Color(0.1f, 0.9f, 0.2f, 1f)));
        var cases = MapCases(image);
        Info("MI4 cases: " + string.Join(", ", cases.Select(c => c.Type + " " + c.What)));
        Info($"MI4 index {MapTarget.IndexMilliseconds:F2} ms, {MapTarget.Materials.Count} materials, {MapTarget.Renderers.Count} renderers");
        Check("MI4 the five value types ran", cases.Select(c => c.Type).Distinct().Count() == 5, string.Join(", ", cases.Select(c => c.Type)));
        var before = new Dictionary<ItemCase, string>();
        foreach (var c in cases)
        {
            string what = "MI4 " + c.Type;
            before[c] = c.Read();
            if (!Check(what + ": set makes an entry", MapEdit(c.Make()))) continue;
            Check(what + ": on the material", c.Read() == c.After, c.Read() + " want " + c.After);
            yield return OkCall(new { cmd = "undo" }, what + ": undo");
            App.Runtime.ApplyNow();
            Check(what + ": undo gives the value before", c.Read() == before[c], c.Read() + " want " + before[c]);
            yield return OkCall(new { cmd = "redo" }, what + ": redo");
            App.Runtime.ApplyNow();
            Check(what + ": redo gives the value", c.Read() == c.After, c.Read() + " want " + c.After);
        }
        // a shader swap and its undo
        var bytes = new byte[2][];
        yield return SceneJson("mi4_before", bytes);
        var jsonBefore = bytes[0];
        var sm = PickMapMaterial(m => m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal));
        if (Check("MI4 a material to swap", sm))
        {
            string mat = MatName(sm), cur = sm.shader.name;
            string printBefore = Print(sm);
            // a shader that keeps what the cases on this material name, so they still act after the swap
            var mine = cases.Where(x => x.Material == mat).ToList();
            var cand = MaterialShaders.All.First(c => c.Name != cur && mine.All(x => x.Type == "keyword" ? c.Keywords.Contains(x.Property) : c.Shader.FindPropertyIndex(x.Property) >= 0));
            Info("MI4 shader: " + mat + " " + cur + " -> " + cand.Name);
            int undos = App.Session.Commands.UndoList.Count;
            Check("MI4 shader: set", MapEdit(MaterialEdit.ShaderItem(mat).WithShader(cand.Name, cur)));
            Check("MI4 shader: on the material", MapMat(mat).shader.name == cand.Name, MapMat(mat).shader.name);
            Check("MI4 shader: no reload", MapPart.State == MapState.Loaded && MapTarget != null);
            Check("MI4 shader: a snapshot", MapPart.MaterialEdits.Snapshots > 0, MapPart.MaterialEdits.Snapshots.ToString());
            Check("MI4 shader: the other edits still apply", cases.All(c => c.Read() == c.After), string.Join(" | ", cases.Where(c => c.Read() != c.After).Select(c => c.Type + " " + c.Read())));
            yield return OkCall(new { cmd = "undo" }, "MI4 shader: undo");
            App.Runtime.ApplyNow();
            Check("MI4 shader: undo restores every property, keyword and the queue", Print(MapMat(mat)) == printBefore, FirstDiff(Print(MapMat(mat)), printBefore));
            Check("MI4 shader: one entry", App.Session.Commands.UndoList.Count == undos);
            Check("MI4 shader: the other edits after the undo", cases.All(c => c.Read() == c.After), string.Join(" | ", cases.Where(c => c.Read() != c.After).Select(c => c.Type + " " + c.Read())));
            yield return SceneJson("mi4_after", bytes);
            Check("MI4 scene.json byte-equal after the undo", bytes[0] != null && bytes[0].SequenceEqual(jsonBefore));
            MapEdit(MaterialEdit.ShaderItem(mat).WithShader(cand.Name, cur));
        }
        // save, new, load
        yield return SceneJson("mi4", bytes);
        string path = Encoding.UTF8.GetString(bytes[1]);
        string json = Json(MapArray);
        yield return OkCall(new { cmd = "scene.new" }, "MI4 scene.new");
        yield return OkCall(new { cmd = "scene.load", path }, "MI4 scene.load", 240);
        yield return Settled();
        Check("MI4 the array after load", Json(MapArray) == json);
        foreach (var c in cases) Check("MI4 " + c.Type + ": on the material after load", c.Read() == c.After, c.Read() + " want " + c.After);
        // every edit gone: the map as loaded
        SetMapArray(new List<MaterialOverride>());
        foreach (var c in cases) Check("MI4 " + c.Type + ": the array emptied: the value before", c.Read() == before[c], c.Read() + " want " + before[c]);
        yield return OkCall(new { cmd = "scene.new" }, "MI4 scene.new");
    }

    static string FirstDiff(string a, string b)
    {
        var x = a.Split(' ');
        var y = b.Split(' ');
        for (int i = 0; i < x.Length && i < y.Length; i++) if (x[i] != y[i]) return x[i] + " want " + y[i];
        return x.Length == y.Length ? "" : "lengths " + x.Length + " " + y.Length;
    }

    static byte[] TestPng(Color c)
    {
        var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var px = new Color[64];
        for (int i = 0; i < 64; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        var png = t.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(t);
        return png;
    }

    // ---- MI6

    static IEnumerator Mi6()
    {
        yield return OkCall(new { cmd = "scene.new" }, "MI6 scene.new");
        yield return LoadMap(SameBundleB, "MI6");
        string other = MapPrint();
        yield return LoadMap(SameBundleA, "MI6");
        string clean = MapPrint();
        var bundleOf = new HashSet<Material>();
        foreach (var e in MapTarget.Materials)
        {
            var o = MapPart.Materials.OriginOf(e.Material);
            if (o?.Source) bundleOf.Add(o.Source);
        }
        Check("MI6 no bundle material in the index", MapTarget.Materials.All(e => !bundleOf.Contains(e.Material)) && bundleOf.Count > 0, bundleOf.Count + " bundle materials");
        Check("MI6 every material of the index is owned", MapTarget.Materials.All(e => MapPart.Materials.IsOwned(e.Material)));
        // (the first read of a bundle material runs the Editor's property drawers, which switch its keywords: a player has none)
        foreach (var m in bundleOf) Print(m);
        string bundlePrint = string.Join("\n", bundleOf.Select(Print).OrderBy(x => x, StringComparer.Ordinal));
        int live = HS2Next.Chara.Ext.ExtTextures.Live;
        var cases = MapCases(App.Session.Model.File.Res.Add(TestPng(new Color(0.8f, 0.2f, 0.9f, 1f))));
        foreach (var c in cases) MapEdit(c.Make());
        foreach (var c in cases) Check("MI6 " + c.Type + " applied", c.Read() == c.After, c.Read());
        Check("MT7 the map holds its image", HS2Next.Chara.Ext.ExtTextures.Live == live + 1, (HS2Next.Chara.Ext.ExtTextures.Live - live).ToString());
        string bundleNow = string.Join("\n", bundleOf.Select(Print).OrderBy(x => x, StringComparer.Ordinal));
        Check("MI6 the bundle materials are unchanged", bundleNow == bundlePrint, FirstDiff(bundleNow, bundlePrint));
        yield return OkCall(new { cmd = "map.load", clear = true, wait = true }, "MI6 unload");
        yield return Settled();
        Check("MI6 the unload clears the array", MapArray.Count == 0, MapArray.Count.ToString());
        var gone = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => HS2Next.Chara.Ext.ExtTextures.Live == live, 30, gone);
        Check("MT7 the map released: the decoded images go", gone[0], (HS2Next.Chara.Ext.ExtTextures.Live - live).ToString());
        yield return LoadMap(SameBundleA, "MI6 the same row again");
        Check("MI6 the same row: no value leaked", MapPrint() == clean, FirstDiff(MapPrint(), clean));
        foreach (var c in cases) MapEdit(c.Make());
        yield return LoadMap(SameBundleB, "MI6 the other row of the bundle");
        Check("MI6 the other row: no value leaked", MapPrint() == other, FirstDiff(MapPrint(), other));
        yield return OkCall(new { cmd = "scene.new" }, "MI6 scene.new");
        yield return Settled();
    }

    // ---- MI9

    static IEnumerator Mi9()
    {
        yield return OkCall(new { cmd = "scene.new" }, "MI9 scene.new");
        yield return LoadMap(PlayMap, "MI9");
        if (!Check("MI9 the map scrolls", MapPart.Scripts.UVScrolls > 0, MapPart.Scripts.UVScrolls.ToString())) yield break;
        var r = MapPart.Structure.UVScrolls.Select(u => u.GetComponent<Renderer>()).First(x => x);
        var m = r.sharedMaterial;
        string mat = MatName(m);
        Check("MI9 the script drives the material", MapPart.Scripts.Drives(m, "_MainTex"));
        MapEdit(MaterialEdit.Texture(mat, "MainTex").WithOffset(new Vec2(0.5f, 0.5f)));
        var item = MapArray.Last();
        var st = MapTarget.Status(item);
        Check("MI9 an offset on a scrolled material reads driven", st.Status == MaterialStatus.Driven, st.Status);
        Check("MI9 the item is kept", MapArray.Count == 1);
        var a = m.GetTextureOffset("_MainTex");
        yield return PlayModeHarness.WaitFrames(10);
        var b = m.GetTextureOffset("_MainTex");
        Check("MI9 the script's offset keeps running", a != b, a + " " + b);
        SetMapArray(new List<MaterialOverride>());
        string image = App.Session.Model.File.Res.Add(TestPng(new Color(0.2f, 0.8f, 0.9f, 1f)));
        MapEdit(MaterialEdit.Texture(mat, "MainTex").WithImage(image));
        var tex = m.GetTexture("_MainTex");
        Check("MI9 an image on a scrolled material is written", tex && tex.width == 8, tex ? tex.width.ToString() : "none");
        Check("MI9 and reads applied", MapTarget.Status(MapArray.Last()).Applied, MapTarget.Status(MapArray.Last()).Status);
        MapEdit(MaterialEdit.Texture(mat, "MainTex").WithOffset(new Vec2(0.25f, 0.25f)));
        Check("MI9 an image with an offset: the image stays written, the item reads driven", m.GetTexture("_MainTex") == tex && MapTarget.Status(MapArray.Last()).Status == MaterialStatus.Driven, MapTarget.Status(MapArray.Last()).Status);
        yield return OkCall(new { cmd = "scene.new" }, "MI9 scene.new");
        yield return Settled();
    }

    // ---- MI10

    static IEnumerator Mi10()
    {
        yield return OkCall(new { cmd = "scene.new" }, "MI10 scene.new");
        yield return LoadMap(SameBundleA, "MI10");
        var cases = MapCases(App.Session.Model.File.Res.Add(TestPng(new Color(0.3f, 0.6f, 0.9f, 1f)))).Where(c => c.Type != "renderer").ToList();
        foreach (var c in cases) MapEdit(c.Make());
        string json = Json(MapArray);
        int undos = App.Session.Commands.UndoList.Count;
        yield return LoadMap(PlayMap, "MI10 another row");
        Check("MI10 another row: the array is empty", MapArray.Count == 0, MapArray.Count.ToString());
        Check("MI10 one entry", App.Session.Commands.UndoList.Count == undos + 1, App.Session.Commands.UndoList.Count + " after " + undos);
        yield return OkCall(new { cmd = "undo" }, "MI10 undo");
        yield return Settled();
        Check("MI10 undo: the row is back", App.Session.Model.Settings.Map.Ref == SameBundleA, App.Session.Model.Settings.Map.Ref);
        Check("MI10 undo: the array is back", Json(MapArray) == json);
        foreach (var c in cases) Check("MI10 " + c.Type + " after the undo", c.Read() == c.After, c.Read() + " want " + c.After);
        yield return OkCall(new { cmd = "map.load", @ref = SameBundleA, wait = true }, "MI10 re-selecting the loaded map");
        Check("MI10 re-selecting keeps the array", Json(MapArray) == json);
        yield return OkCall(new { cmd = "scene.new" }, "MI10 scene.new");
        yield return Settled();
    }

    // ---- MW4

    static IEnumerator Mw4()
    {
        yield return OkCall(new { cmd = "scene.new" }, "MW4 scene.new");
        string mapRef = Arg("-mw4", LargestVanilla);
        var sw = Stopwatch.StartNew();
        yield return LoadMap(mapRef, "MW4", double.Parse(Arg("-mw4LoadSec", "240"), System.Globalization.CultureInfo.InvariantCulture));
        if (MapPart.State != MapState.Loaded) yield break;
        Info($"MW4 {mapRef}: load {sw.Elapsed.TotalSeconds:F1} s, {MapPart.Structure.Renderers.Count} renderers");
        var t = MapTarget;
        t.Index();
        Info($"MW4 name index {t.IndexMilliseconds:F1} ms, {t.Materials.Count} materials");
        t.Members();
        Info($"MW4 the members' walk {t.MemberMilliseconds:F1} ms");
        var l = new List<Renderer>();
        t.SelectRenderers("(none)", l);
        Info($"MW4 path index {t.PathMilliseconds:F1} ms");
        var mats = new List<Material>();
        var pass = Stopwatch.StartNew();
        const int reps = 2000;
        for (int i = 0; i < reps; i++) { mats.Clear(); t.Select(t.Materials[i % t.Materials.Count].Name, mats, false, out _, out _); }
        Info($"MW4 a name's lookup {pass.Elapsed.TotalMilliseconds * 1000 / reps:F2} us");
        Check("MW4 the index is built", t.Materials.Count > 0);
        yield return OkCall(new { cmd = "scene.new" }, "MW4 scene.new");
        yield return Settled();
    }
}
