// Material edits on a built character (package R). The runner of the MaterialEditTest rows: the
// other packages add partial files, which the partial methods below reach. Headless, ManualStepping, the targets of
// Tools/targets.json; every item is synthetic, images are generated here, numbers only: nothing is rendered, shown or opened.
//   Run      (row MaterialEditTest, -Quick: round trips)
//     ML1  character, hair, clothes, accessory x float, color, texture, shader, renderer Enabled, keyword: set, the value on the
//          material, undo, redo; then save, scene.new, load, the values again.
//     MI1  the item, the same (QuickItems, package I).
//   RunFull  (row MaterialEditTest.full)
//     ML2  each editable type on each target: set, then remove: a snapshot equal to the one before, and to a fresh build.
//     ML3  an edited value through a hair and a clothes colour edit, a part swap of the part (items cleared), a head swap, a
//          coordinate load, a rebuild.
//     ML4  an edit on _Color of a clothes part, a colour change under it, then removal: the new colour shows.
//     ML5  Enabled 1 on a body renderer with the body hidden: hidden after the flush, the final stage ran.
//     ML6  120 drag steps on a float, a colour, a texture offset: one entry, no rebuild and no swap, flushes <= steps; a drag through
//          the baseline value on an imported item; a drag that ends where it began adds no entry.
//     ML8  a shader swap on a part and on the Character object: reload, rebuild; undo brings the shader and the values back.
//     ML9  a colour item on a vector-typed property: GetVector against the four stored numbers (reported either way).
//     ML10 the last item of the character removed: the baseline written, no plugin data left, statistics as a fresh build's.
//     ML11 what the URP mapping reads: a queue's band, an image on _BumpMap, an emission colour from black: a reload or, on the Character object, a
//          rebuild; a move that keeps it a pass; each step equal to a fresh build; a keyword item on the shadow switch decides it.
//     MV1  draft: the candidate list, printed with what it left out.
//     (package I: MI2, MI3, MI5, MI7, MI8 in FullItems; package C: MT1-MT7 in FullTextures; package U: MJ2, MJ4 in FullHair)
// -only ML1,ML6 picks groups. Reports Spike/out/maker/material_edit_report.txt, material_edit_full_report.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using static MakerTestKit;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

public static partial class MaterialEditTest
{
    // ---- what other packages add (partial files of this class)
    static partial void QuickItems(McHost h);
    static partial void FullItems(McHost h);
    static partial void FullTextures(McHost h);
    static partial void FullHair(McHost h);

    /// <summary>An app without a window: commands, ops and the session, stepped by hand.</summary>
    public sealed class McHost : IDisposable
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public JObject Call(object q) => JObject.Parse(App.Call(q is string s ? s : JsonConvert.SerializeObject(q)));
        public JObject Ok(object q, string what)
        {
            var r = Call(q);
            Check(what, r.Value<bool?>("ok") == true, r.Value<bool?>("ok") == true ? "" : Short(r.ToString()));
            return r;
        }
        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); } }
    }

    static RuntimeServices services;
    static string HostDir => Path.Combine(OutDir, "materialedit");

    public static McHost NewHost()
    {
        services = services ?? SceneRuntimeTest.NewServices(new StringBuilder());
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = Path.Combine(HostDir, "userdata");
        s.Bridge.OutputDir = Path.Combine(HostDir, "out");
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(0x64aa0a5eedUL);
        return new McHost { App = AppCore.ForTests(services, s, null, o) };
    }

    /// <summary>Loads a visual target; its key. The runtime steps by hand, so nothing moves between reads.</summary>
    public static string LoadTarget(McHost h, bool male = false)
    {
        var t = TestTargets.Load().visual;
        string rel = male ? t.male : t.female;
        foreach (var pre in new[] { "chara/female/", "chara/male/" })
            if (rel.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(pre.Length);
        var r = h.Ok(new { cmd = "chara.load", path = rel, sex = male ? "male" : "female" }, "chara.load");
        string key = (string)r["key"] ?? (string)r["added"]?[0]?["key"];
        h.Rt.ApplyNow();
        return key;
    }

    public static CharaRealization Real(McHost h, string key) => h.Rt.FindChara(key);
    public static CharaContext Ctx(McHost h, string key) => Real(h, key).Context;
    public static CharaSubtree Stored(McHost h, string key) => h.Session.Model.Find(key)?.Appearance?.Stored;

    /// <summary>A snapshot of the character as it stands.</summary>
    public static CharaSnapshot Snap(McHost h, string key)
    {
        var r = Real(h, key);
        return CharaSnapshot.Take(r.Root, r.Context, null);
    }

    /// <summary>Two snapshots equal in what material edits reach: everything but the bones and the motion, which each step of a live
    /// character moves by its physics and its blinking.</summary>
    public static bool Same(CharaSnapshot a, CharaSnapshot b, string what)
    {
        var d = CharaSnapshot.Compare(a, b, 100000);
        var lines = d.Lines.Where(l => !l.StartsWith("~ bone|", StringComparison.Ordinal) && !l.StartsWith("~ motion|", StringComparison.Ordinal) &&
                                       !l.StartsWith("- bone|", StringComparison.Ordinal) && !l.StartsWith("+ bone|", StringComparison.Ordinal) &&
                                       !l.StartsWith("- motion|", StringComparison.Ordinal) && !l.StartsWith("+ motion|", StringComparison.Ordinal) &&
                                       !OnlyBounds(l)).ToList();
        return Check(what, lines.Count == 0, lines.Count == 0 ? "" : lines.Count + " lines:\n  " + string.Join("\n  ", lines.Take(12).Select(l => Short(l, 400))));
    }

    // the bounds of a skinned renderer follow its bones
    static bool OnlyBounds(string line)
    {
        int colon = line.IndexOf(": ", StringComparison.Ordinal);
        return line.StartsWith("~ rend|", StringComparison.Ordinal) && colon > 0 &&
               line.Substring(colon + 2).Split(new[] { "; " }, StringSplitOptions.None).All(t => t.StartsWith("bounds=", StringComparison.Ordinal));
    }

    // ---- targets

    /// <summary>One material array of a character: its object, its path and where the list lives.</summary>
    public sealed class Target
    {
        public string Name, Path;
        public (OT, int) Object;
        public Func<CharaSubtree, List<MaterialOverride>> List;
    }

    public static Target CharacterTarget() =>
        new Target { Name = "character", Path = "appearance.chara.materialOverrides", Object = (OT.Character, 0), List = s => s.Chara.MaterialOverrides };

    public static Target HairTarget(int k) =>
        new Target { Name = "hair:" + k, Path = "appearance.chara.hair." + HS2Next.Chara.Edit.UnitMap.HairParts[k] + ".materialOverrides", Object = (OT.Hair, k), List = s => s.Chara.Hair.Parts[k].MaterialOverrides };

    public static Target ClothesTarget(int k) =>
        new Target { Name = "clothes:" + k, Path = "appearance.coord.clothes." + HS2Next.Chara.Edit.UnitMap.ClothesKinds[k] + ".materialOverrides", Object = (OT.Clothing, k), List = s => s.Coord.Clothes.Parts[k].MaterialOverrides };

    public static Target AccessoryTarget(McHost h, string key, string id, int slot) =>
        new Target { Name = "acc:" + id, Path = "appearance.coord.accessories[\"" + id + "\"].materialOverrides", Object = (OT.Accessory, slot), List = s => s.Coord.FindAccessory(id).MaterialOverrides };

    /// <summary>The character's four kinds of target that have renderers on this card.</summary>
    static List<Target> Targets(McHost h, string key)
    {
        var l = new List<Target> { CharacterTarget() };
        var ctx = Ctx(h, key);
        for (int k = 0; k < 4; k++) if (ctx.Hair[k]) { l.Add(HairTarget(k)); break; }
        for (int k = 0; k < 8; k++) if (ctx.Clothes[k]) { l.Add(ClothesTarget(k)); break; }
        var slots = ctx.Edit.Input.Slots;
        foreach (var kv in slots.EntryBySlot)
            if (ctx.AccessoryObject(kv.Key)) { l.Add(AccessoryTarget(h, key, kv.Value.Id, kv.Key)); break; }
        return l;
    }

    /// <summary>The first material of the target that is no member's, with a property of <paramref name="type"/>; null when none.</summary>
    static Material PickMaterial(McHost h, string key, Target t, Func<Material, bool> ok)
    {
        var ctx = Ctx(h, key);
        var rends = MaterialEditorApplier.ObjectRenderers(ctx, t.Object);
        if (rends == null) return null;
        foreach (var r in rends)
        {
            if (MosaicSet.IsMember(r) || ctx.DrawsBodyMaterials(r) || ctx.DrawsBodySkin(r)) continue;
            foreach (var m in r.sharedMaterials) if (m && !MosaicSet.IsMosaicMaterial(m) && !ShaderMap.IsFallback(m) && ok(m)) return m;
        }
        return null;
    }

    /// <summary>The first colour property the material shows, or null.</summary>
    public static string FirstColor(Material m) => PickProperty(m, ShaderPropertyType.Color, ShaderPropertyType.Color);

    static string PickProperty(Material m, ShaderPropertyType a, ShaderPropertyType b)
    {
        var sh = m.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            var t = sh.GetPropertyType(i);
            if (t != a && t != b) continue;
            if ((sh.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
            if (t == ShaderPropertyType.Texture && sh.GetPropertyTextureDimension(i) != TextureDimension.Tex2D) continue;
            string n = sh.GetPropertyName(i);
            if (n.StartsWith("_", StringComparison.Ordinal) && n != "_Cutoff" && n != "_Surface" && n != "_ZWrite" && n != "_Cull") return n;
        }
        return null;
    }

    /// <summary>One edit on a target and what must then be on the material.</summary>
    public sealed class Case
    {
        public string Type, MaterialName, Property, RendererName;
        public Func<McHost, string, MaterialEdit> Make;
        /// <summary>The value on the target as text, by kind.</summary>
        public Func<McHost, string, string> Read;
        public string Before, After;
    }

    static string Text(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8");
    static string Text(Color c) => Text(c.r) + " " + Text(c.g) + " " + Text(c.b) + " " + Text(c.a);
    static string Text(Vector2 v) => Text(v.x) + " " + Text(v.y);

    static List<Material> Named(McHost h, string key, Target t, string name) => MaterialEditorApplier.MaterialsNamed(Ctx(h, key), t.Object, name);

    public static Color4 C4(Color c) => new Color4(c.r, c.g, c.b, c.a);

    /// <summary>The six edits on a target: what its materials offer. <paramref name="image"/>: a res name in the scene's store.</summary>
    static List<Case> Cases(McHost h, string key, Target t, string image)
    {
        var l = new List<Case>();
        var fm = PickMaterial(h, key, t, m => PickProperty(m, ShaderPropertyType.Float, ShaderPropertyType.Range) != null);
        if (fm)
        {
            string prop = PickProperty(fm, ShaderPropertyType.Float, ShaderPropertyType.Range), mat = MaterialEditorApplier.Fmt(fm.name);
            float was = fm.GetFloat(prop), to = was > 0.5f ? was - 0.25f : was + 0.25f;
            l.Add(new Case
            {
                Type = "float", MaterialName = mat, Property = prop,
                Make = (hh, k) => MaterialEdit.Float(mat, prop.Substring(1), to, Base(hh, k, t, mat, MaterialMember.Float, prop)?.Float),
                Read = (hh, k) => Text(Named(hh, k, t, mat)[0].GetFloat(prop)),
                After = Text(to),
            });
        }
        var cm = PickMaterial(h, key, t, m => PickProperty(m, ShaderPropertyType.Color, ShaderPropertyType.Color) != null);
        if (cm)
        {
            string prop = PickProperty(cm, ShaderPropertyType.Color, ShaderPropertyType.Color), mat = MaterialEditorApplier.Fmt(cm.name);
            var to = new Color(0.25f, 0.5f, 0.75f, 1f);
            l.Add(new Case
            {
                Type = "color", MaterialName = mat, Property = prop,
                Make = (hh, k) =>
                {
                    var b = Base(hh, k, t, mat, MaterialMember.Color, prop);
                    return MaterialEdit.ColorOf(mat, prop.Substring(1), C4(to), b.HasValue ? C4(b.Value.Color) : (Color4?)null);
                },
                Read = (hh, k) => Text(Near(Named(hh, k, t, mat)[0].GetColor(prop))),
                After = Text(Near(to)),
            });
        }
        var tm = PickMaterial(h, key, t, m => PickProperty(m, ShaderPropertyType.Texture, ShaderPropertyType.Texture) != null);
        if (tm)
        {
            string prop = PickProperty(tm, ShaderPropertyType.Texture, ShaderPropertyType.Texture), mat = MaterialEditorApplier.Fmt(tm.name);
            var off = new Vector2(0.125f, 0.25f);
            var sc = new Vector2(2f, 3f);
            l.Add(new Case
            {
                Type = "texture", MaterialName = mat, Property = prop,
                Make = (hh, k) => MaterialEdit.Texture(mat, prop.Substring(1)).WithImage(image).WithOffset(new Vec2(off.x, off.y)).WithScale(new Vec2(sc.x, sc.y)),
                Read = (hh, k) =>
                {
                    var m = Named(hh, k, t, mat)[0];
                    var tex = m.GetTexture(prop);
                    return (tex ? tex.width + "x" + tex.height : "none") + " " + Text(m.GetTextureOffset(prop)) + " " + Text(m.GetTextureScale(prop));
                },
                After = "8x8 " + Text(off) + " " + Text(sc),
            });
        }
        var rends = MaterialEditorApplier.ObjectRenderers(Ctx(h, key), t.Object);
        var rr = rends?.FirstOrDefault(r => r.enabled && !MosaicSet.IsMember(r));
        if (rr)
        {
            string name = MaterialEditorApplier.Fmt(rr.name);
            l.Add(new Case
            {
                Type = "renderer", RendererName = name,
                Make = (hh, k) => MaterialEdit.RendererEnabled(name, 0, MaterialEditorApplier.TryBaselineEnabled(Ctx(hh, k), t.Object, name, out var v) ? (v.Flag ? 1 : 0) : (int?)null),
                Read = (hh, k) => MaterialEditorApplier.ObjectRenderers(Ctx(hh, k), t.Object).First(x => MaterialEditorApplier.Fmt(x.name) == name).enabled ? "on" : "off",
                After = "off",
            });
        }
        var km = PickMaterial(h, key, t, m => m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal) && Keyword(m) != null);
        if (km)
        {
            string kw = Keyword(km), mat = MaterialEditorApplier.Fmt(km.name);
            bool was = km.IsKeywordEnabled(kw);
            l.Add(new Case
            {
                Type = "keyword", MaterialName = mat, Property = kw,
                Make = (hh, k) => MaterialEdit.Keyword(mat, kw.Substring(1), !was, Base(hh, k, t, mat, MaterialMember.Keyword, kw)?.Flag),
                Read = (hh, k) => Named(hh, k, t, mat)[0].IsKeywordEnabled(kw) ? "on" : "off",
                After = !was ? "on" : "off",
            });
        }
        var sm = PickMaterial(h, key, t, m => m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal));
        if (sm)
        {
            string mat = MaterialEditorApplier.Fmt(sm.name), cur = sm.shader.name;
            var cand = MaterialShaders.All.First(c => c.Name != cur && c.Name.Contains("AIT"));
            l.Add(new Case
            {
                Type = "shader", MaterialName = mat, Property = cand.Name,
                Make = (hh, k) => MaterialEdit.ShaderItem(mat).WithShader(cand.Name, cur),
                Read = (hh, k) => Named(hh, k, t, mat)[0].shader.name,
                After = cand.Name,
            });
        }
        return l;
    }

    // a colour written reads back within ulps of what was written: compared at seven decimals
    static Color Near(Color c) => new Color(Round(c.r), Round(c.g), Round(c.b), Round(c.a));
    static float Round(float f) => (float)Math.Round(f, 4);

    static string Keyword(Material m)
    {
        foreach (var k in m.shader.keywordSpace.keywords)
            if (!k.isOverridable && k.name.StartsWith("_", StringComparison.Ordinal) && k.name != ShaderMap.ReceiveShadowsOff && k.name.EndsWith("_ON", StringComparison.Ordinal)) return k.name;
        return null;
    }

    static MaterialBaseline.Value? Base(McHost h, string key, Target t, string mat, MaterialMember kind, string prop) =>
        MaterialEditorApplier.TryBaseline(Ctx(h, key), t.Object, mat, kind, prop, out var v) ? v : (MaterialBaseline.Value?)null;

    /// <summary>Sets the target's array through the Maker op, as one entry.</summary>
    public static void SetArray(McHost h, string key, Target t, List<MaterialOverride> next, string what = "Material edit")
    {
        var r = h.App.Ops.EditAppearance(new[] { key }, new (string, object)[] { (t.Path, next) }, new ExecuteOptions { Source = CommandSource.User, Label = what }, allowRebuild: true);
        if (!r.Ok) Check(what + " accepted", false, r.Code + " " + r.Message);
        h.Rt.ApplyNow();
    }

    /// <summary>One edit on the target's current array; false when it changed nothing.</summary>
    public static bool Edit(McHost h, string key, Target t, MaterialEdit e)
    {
        var list = t.List(Stored(h, key));
        var next = MaterialEdits.Set(list, e);
        if (ReferenceEquals(next, list)) return false;
        SetArray(h, key, t, next);
        return true;
    }

    static void Undo(McHost h, string what) { h.Ok(new { cmd = "undo" }, what); h.Rt.ApplyNow(); }
    static void Redo(McHost h, string what) { h.Ok(new { cmd = "redo" }, what); h.Rt.ApplyNow(); }

    /// <summary>An 8 x 8 test image in the scene's store; its res name.</summary>
    public static string TestImage(McHost h, Color c)
    {
        var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var px = new Color[64];
        for (int i = 0; i < 64; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        var png = t.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(t);
        return h.Session.Model.File.Res.Add(png);
    }

    // ---- the rows

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var seed = HS2Next.Motion.Face.FaceSetup.Seed;
        var h = NewHost();
        try
        {
            if (Want("ML1")) Guard("ML1 round trips on a character", () => Ml1(h));
            QuickItems(h);
        }
        finally { h.Dispose(); HS2Next.Motion.Face.FaceSetup.Seed = seed; }
        Finish("material_edit_report.txt", "MaterialEditTest: round trips (ML1, MI1)", sw, "MATERIAL EDIT TEST");
    }

    public static void RunFull()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var seed = HS2Next.Motion.Face.FaceSetup.Seed;
        var h = NewHost();
        try
        {
            CharaFull(h);
            FullItems(h);
            FullTextures(h);
            FullHair(h);
        }
        finally { h.Dispose(); HS2Next.Motion.Face.FaceSetup.Seed = seed; }
        Finish("material_edit_full_report.txt", "MaterialEditTest.full: ML2-ML6, ML8-ML10, MV1 and the groups of the other packages", sw, "MATERIAL EDIT TEST");
    }

    static void Ml1(McHost h)
    {
        h.Ok(new { cmd = "scene.new" }, "ML1 scene.new");
        string key = LoadTarget(h);
        string image = null;
        var all = new List<(Target t, Case c)>();
        var shaders = new List<(Target t, Case c)>();
        foreach (var t in Targets(h, key))
        {
            image = image ?? TestImage(h, new Color(0.1f, 0.9f, 0.2f, 1f));
            var cases = Cases(h, key, t, image);
            Info($"ML1 {t.Name}: " + string.Join(", ", cases.Select(c => c.Type + " " + (c.MaterialName ?? c.RendererName) + (c.Property != null ? " " + c.Property : ""))));
            foreach (var c in cases)
            {
                // a swap changes what the other items of its material find: its own round trip, after theirs
                if (c.Type == "shader") { shaders.Add((t, c)); continue; }
                if (RoundTripEdit(h, key, t, c)) all.Add((t, c));
            }
        }
        var types = all.Concat(shaders).Select(x => x.c.Type).Distinct().ToList();
        Check("ML1 every kind of target has its cases", all.Select(x => x.t.Name).Distinct().Count() >= 3, string.Join(", ", all.Select(x => x.t.Name).Distinct()));
        Check("ML1 the six types ran", types.Count == 6, string.Join(", ", types));
        SaveLoadHolds(h, key, all, "ml1");
        var done = new List<(Target t, Case c)>();
        foreach (var (t, c) in shaders)
        {
            // the array holds the value items of the same material: they go first, so the swap is judged alone
            foreach (var (t2, c2) in all.Where(x => x.t.Name == t.Name))
                SetArray(h, key, t2, MaterialEdits.Remove(t2.List(Stored(h, key)), c2.Make(h, key).Key));
            if (RoundTripEdit(h, key, t, c)) done.Add((t, c));
        }
        SaveLoadHolds(h, key, done, "ml1s");
        h.Ok(new { cmd = "scene.new" }, "ML1 scene.new");
    }

    // set, the value on the material, undo, redo
    static bool RoundTripEdit(McHost h, string key, Target t, Case c)
    {
        string what = $"ML1 {t.Name} {c.Type}";
        c.Before = c.Read(h, key);
        if (!Check(what + ": set makes an entry", Edit(h, key, t, c.Make(h, key)), "")) return false;
        Check(what + ": on the material", c.Read(h, key) == c.After, c.Read(h, key) + " want " + c.After);
        Undo(h, what + ": undo");
        Check(what + ": undo gives the value before", c.Read(h, key) == c.Before, c.Read(h, key) + " want " + c.Before);
        Redo(h, what + ": redo");
        Check(what + ": redo gives the value", c.Read(h, key) == c.After, c.Read(h, key) + " want " + c.After);
        return true;
    }

    // save, scene.new, load: the arrays and the values again
    static void SaveLoadHolds(McHost h, string key, List<(Target t, Case c)> cases, string name)
    {
        var save = h.Ok(new { cmd = "scene.save", name, overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "ML1 scene.save");
        string path = (string)save["path"];
        var stored = cases.Select(x => (x.t, x.c, json: Json(x.t.List(Stored(h, key))))).ToList();
        h.Ok(new { cmd = "scene.new" }, "ML1 scene.new");
        h.Ok(new { cmd = "scene.load", path }, "ML1 scene.load");
        h.Rt.ApplyNow();
        foreach (var (t, c, json) in stored)
        {
            string what = $"ML1 {t.Name} {c.Type}";
            Check(what + ": the array after load", Json(t.List(Stored(h, key))) == json, "");
            Check(what + ": on the material after load", c.Read(h, key) == c.After, c.Read(h, key) + " want " + c.After);
        }
    }

    public static string Json(List<MaterialOverride> list) => HS2Next.Chara.Edit.Units.Signature.Of(list);
}
