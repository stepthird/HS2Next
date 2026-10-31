// Maker core row U and the measurements U1-U4 of Phase 4.0 package A.
//   U   (RunU; headless): every edit unit alone on the visual targets (BR-Chan, HERO107): for each path family one
//       member is set to another value (another row of the category for references), the editor marks and flushes, and
//       the canonical snapshot equals a fresh build of the edited documents; then the member is set back, flushed, and the
//       snapshot equals the character's original snapshot. Head swaps get the scene's step-10 work of the realization
//       (the expression stage re-attached), as package C does it in the scene. Accessory list edits (insert, remove,
//       move) and the profile name are families too. A family whose change is rebuild-only is counted apart.
//   U1  (RunU1; graphics): GPU time of the skin.body / skin.face rebuilds (incl. mip generation) with 0, 1 and 2 KSOX-type
//       layers (synthetic noise images), and the decode time of a 4096² layer PNG.
//   U2  (RunU2; headless): ShapeEngine.Apply time face / body, the shape-bone guard and rest recapture, and the flush time
//       of the shape units (median of 50).
//   U3  (RunU3; graphics): clothes colour pass per group size 512² … 4096² (create_clothes + detail, synthetic main texture).
//   U4  (RunU4; headless): memory per character: render textures (count, bytes), owned materials, live Material objects
//       after the build with the URP mapping in place (C2) and with the pre-4.0 copies (CharaBuilders.UrpInPlace = false).
// Reports: Spike/out/maker/maker_u_report.txt, maker_u1_report.txt … maker_u4_report.txt; first line "SUMMARY: N PASS, M FAIL".
// Nothing is rendered or shown (numbers only); the targets are the SFW visual targets of Tools/targets.json.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Edit.Units;
using HS2Next.Content;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerUnitTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");
    static StringBuilder rep;
    static int passes, fails;

    static void Reset() { rep = new StringBuilder(); passes = fails = 0; }
    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
    }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Finish(string file, string title, Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\n{title}\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString());
        Debug.Log($"MAKER UNIT TEST DONE ({file}): {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }
    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }
    static double Median(List<double> l)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }
    static string Rel(string f) => Path.GetFileName(f);
    static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    // ================================================================ U

    /// <summary>One path family: the member's document path (relative to `appearance.`), how to read / write it, and the
    /// edited value.</summary>
    sealed class Family
    {
        public string Name, Path;
        public Func<object> Get;
        public Action<object> Set;
        public Func<object, object> Alt;
        public string Skip;   // why it is not applicable to this character (e.g. no accessory)
    }

    sealed class Env
    {
        public CharaDocument Chara;
        public CoordDocument Coord;
        public ContentCatalog Catalog;
        public bool Male;
    }

    /// <summary>The scene's step-10 work that the U row needs without a realization (package C does it in the scene).</summary>
    sealed class TestHooks : ICharaEditSceneHooks
    {
        public void AfterFlush(CharaEditor editor, FlushReport r)
        {
            var ctx = editor.Context?.Ctx;
            if (ctx == null || !r.HeadSwapped || ctx.Motion == null) return;
            foreach (var s in ctx.Motion.GetAll<HS2Next.Motion.IMotionStage>().ToList())
                if (s is ExpressionStage) ctx.Motion.Remove(s);
            FaceSetup.Attach(ctx);
        }
    }

    static string NextRow(Env e, int cat, string current, Func<ListEntry, bool> ok = null)
    {
        var rows = e.Catalog.EntriesOf(cat);
        ContentId.TryParse(current, out var cur);
        foreach (var r in rows)
            if (r.IsVanilla && r.Key != cur && (ok == null || ok(r))) return r.Key.ToString();
        foreach (var r in rows)
            if (r.Key != cur && (ok == null || ok(r))) return r.Key.ToString();
        return null;
    }

    static bool HasPrefab(ListEntry r) => !string.IsNullOrEmpty(r["MainData"]) && r["MainData"] != "0" && r["MainData"] != "p_dummy";
    static bool HasTex(ListEntry r, string col) => !string.IsNullOrEmpty(r[col]) && r[col] != "0";

    static float Fl(object v) => (float)v;
    static object AltF(object v) { float f = (float)v; return f < 0.6f ? f + 0.37f : f - 0.41f; }
    static object AltC(object v) { var c = (Color4)v; return new Color4(c.R * 0.5f + 0.13f, c.G * 0.25f + 0.31f, 1 - c.B * 0.5f, c.A); }
    static object AltV4(object v) { var c = (Vec4)v; return new Vec4(c.X * 0.5f + 0.1f, c.Y * 0.75f + 0.2f, 1 - c.Z, c.W * 0.5f + 0.3f); }
    static object AltV3(object v) { var c = (Vec3)v; return new Vec3(c.X + 0.75f, c.Y - 5f, c.Z * 0.5f + 0.25f); }
    static object AltV2(object v) { var c = (Vec2)v; return new Vec2(c.X * 0.5f + 0.2f, c.Y * 0.5f + 0.3f); }

    static List<Family> Families(Env e)
    {
        var l = new List<Family>();
        var ch = e.Chara; var co = e.Coord;
        var sex = e.Male ? CharaSex.Male : CharaSex.Female;
        void Add(string name, string path, Func<object> get, Action<object> set, Func<object, object> alt, string skip = null) =>
            l.Add(new Family { Name = name, Path = path, Get = get, Set = set, Alt = alt, Skip = skip });
        Func<object, object> Ref(int cat, Func<ListEntry, bool> ok = null) => v => NextRow(e, cat, (string)v, ok);
        var f = ch.Face; var b = ch.Body;
        // face
        Add("face.head", "chara.face.head", () => f.Head, v => f.Head = (string)v, Ref(sex.Head, HasPrefab));
        Add("face.skin", "chara.face.skin", () => f.Skin, v => f.Skin = (string)v, Ref(sex.SkinF, r => HasTex(r, "MainTex")));
        Add("face.detail.item", "chara.face.detail.item", () => f.Detail.Item, v => f.Detail.Item = (string)v, Ref(sex.DetailF));
        Add("face.detail.power", "chara.face.detail.power", () => f.Detail.Power, v => f.Detail.Power = Fl(v), AltF);
        foreach (int i in new[] { 0, 20, 58 })
            Add("face.shape[" + i + "]", "chara.face.shape[" + i + "]", () => f.Shape[i], v => f.Shape[i] = Fl(v), AltF);
        Add("face.eyebrow.item", "chara.face.eyebrow.item", () => f.Eyebrow.Item, v => f.Eyebrow.Item = (string)v, Ref(314));
        Add("face.eyebrow.color", "chara.face.eyebrow.color", () => f.Eyebrow.Color, v => f.Eyebrow.Color = (Color4)v, AltC);
        Add("face.eyebrow.layout", "chara.face.eyebrow.layout", () => f.Eyebrow.Layout, v => f.Eyebrow.Layout = (Vec4)v, AltV4);
        Add("face.eyebrow.tilt", "chara.face.eyebrow.tilt", () => f.Eyebrow.Tilt, v => f.Eyebrow.Tilt = Fl(v), AltF);
        foreach (var side in new[] { "left", "right" })
        {
            var eye = side == "left" ? f.Eyes.Left : f.Eyes.Right;
            string p = "chara.face.eyes." + side;
            Add("face.eyes." + side + ".white", p + ".white", () => eye.White, v => eye.White = (Color4)v, AltC);
            Add("face.eyes." + side + ".iris.item", p + ".iris.item", () => eye.Iris.Item, v => eye.Iris.Item = (string)v, Ref(317));
            Add("face.eyes." + side + ".iris.color", p + ".iris.color", () => eye.Iris.Color, v => eye.Iris.Color = (Color4)v, AltC);
            Add("face.eyes." + side + ".iris.size", p + ".iris.size", () => eye.Iris.Size, v => eye.Iris.Size = (Vec2)v, AltV2);
            Add("face.eyes." + side + ".iris.emission", p + ".iris.emission", () => eye.Iris.Emission, v => eye.Iris.Emission = Fl(v), AltF);
            Add("face.eyes." + side + ".pupil.item", p + ".pupil.item", () => eye.Pupil.Item, v => eye.Pupil.Item = (string)v, Ref(318));
            Add("face.eyes." + side + ".pupil.color", p + ".pupil.color", () => eye.Pupil.Color, v => eye.Pupil.Color = (Color4)v, AltC);
            Add("face.eyes." + side + ".pupil.size", p + ".pupil.size", () => eye.Pupil.Size, v => eye.Pupil.Size = (Vec2)v, AltV2);
        }
        Add("face.eyes.offsetY", "chara.face.eyes.offsetY", () => f.Eyes.OffsetY, v => f.Eyes.OffsetY = Fl(v), AltF);
        Add("face.eyes.shadowScale", "chara.face.eyes.shadowScale", () => f.Eyes.ShadowScale, v => f.Eyes.ShadowScale = Fl(v), AltF);
        Add("face.eyes.highlight.item", "chara.face.eyes.highlight.item", () => f.Eyes.Highlight.Item, v => f.Eyes.Highlight.Item = (string)v, Ref(319));
        Add("face.eyes.highlight.color", "chara.face.eyes.highlight.color", () => f.Eyes.Highlight.Color, v => f.Eyes.Highlight.Color = (Color4)v, AltC);
        Add("face.eyes.highlight.layout", "chara.face.eyes.highlight.layout", () => f.Eyes.Highlight.Layout, v => f.Eyes.Highlight.Layout = (Vec4)v, AltV4);
        Add("face.eyes.highlight.tilt", "chara.face.eyes.highlight.tilt", () => f.Eyes.Highlight.Tilt, v => f.Eyes.Highlight.Tilt = Fl(v), AltF);
        Add("face.eyelashes.item", "chara.face.eyelashes.item", () => f.Eyelashes.Item, v => f.Eyelashes.Item = (string)v, Ref(315));
        Add("face.eyelashes.color", "chara.face.eyelashes.color", () => f.Eyelashes.Color, v => f.Eyelashes.Color = (Color4)v, AltC);
        Add("face.mole.item", "chara.face.mole.item", () => f.Mole.Item, v => f.Mole.Item = (string)v, Ref(323));
        Add("face.mole.color", "chara.face.mole.color", () => f.Mole.Color, v => f.Mole.Color = (Color4)v, AltC);
        Add("face.mole.layout", "chara.face.mole.layout", () => f.Mole.Layout, v => f.Mole.Layout = (Vec4)v, AltV4);
        foreach (var (name, layer, cat) in new[] { ("eyeshadow", f.Makeup.Eyeshadow, 316), ("cheek", f.Makeup.Cheek, 320), ("lip", f.Makeup.Lip, 322) })
        {
            string p = "chara.face.makeup." + name;
            var m = layer;
            Add("face.makeup." + name + ".item", p + ".item", () => m.Item, v => m.Item = (string)v, Ref(cat));
            Add("face.makeup." + name + ".color", p + ".color", () => m.Color, v => m.Color = (Color4)v, AltC);
            Add("face.makeup." + name + ".gloss", p + ".gloss", () => m.Gloss, v => m.Gloss = Fl(v), AltF);
        }
        PaintFamilies(Add, Ref, f.Makeup.Paint[0], "chara.face.makeup.paint[0]", "face.makeup.paint[0]", 7);
        Add("face.makeup.paint[1].layoutItem", "chara.face.makeup.paint[1].layoutItem", () => f.Makeup.Paint[1].LayoutItem, v => f.Makeup.Paint[1].LayoutItem = (string)v, Ref(7));
        Add("face.beard.item", "chara.face.beard.item", () => f.Beard.Item, v => f.Beard.Item = (string)v, Ref(121));
        Add("face.beard.color", "chara.face.beard.color", () => f.Beard.Color, v => f.Beard.Color = (Color4)v, AltC);
        // body
        foreach (int i in new[] { 0, 1, 10, 32 })
            Add("body.shape[" + i + "]", "chara.body.shape[" + i + "]", () => b.Shape[i], v => b.Shape[i] = Fl(v), AltF);
        Add("body.bust.softness", "chara.body.bust.softness", () => b.Bust.Softness, v => b.Bust.Softness = Fl(v), AltF);
        Add("body.bust.weight", "chara.body.bust.weight", () => b.Bust.Weight, v => b.Bust.Weight = Fl(v), AltF);
        Add("body.skin.item", "chara.body.skin.item", () => b.Skin.Item, v => b.Skin.Item = (string)v, Ref(sex.SkinB, r => HasTex(r, "MainTex")));
        Add("body.skin.color", "chara.body.skin.color", () => b.Skin.Color, v => b.Skin.Color = (Color4)v, AltC);
        Add("body.skin.gloss", "chara.body.skin.gloss", () => b.Skin.Gloss, v => b.Skin.Gloss = Fl(v), AltF);
        Add("body.skin.metallic", "chara.body.skin.metallic", () => b.Skin.Metallic, v => b.Skin.Metallic = Fl(v), AltF);
        Add("body.detail.item", "chara.body.detail.item", () => b.Detail.Item, v => b.Detail.Item = (string)v, Ref(sex.DetailB));
        Add("body.detail.power", "chara.body.detail.power", () => b.Detail.Power, v => b.Detail.Power = Fl(v), AltF);
        Add("body.sunburn.item", "chara.body.sunburn.item", () => b.Sunburn.Item, v => b.Sunburn.Item = (string)v, Ref(sex.Sunburn));
        Add("body.sunburn.color", "chara.body.sunburn.color", () => b.Sunburn.Color, v => b.Sunburn.Color = (Color4)v, AltC);
        PaintFamilies(Add, Ref, b.Paint[0], "chara.body.paint[0]", "body.paint[0]", 8);
        Add("body.nipple.item", "chara.body.nipple.item", () => b.Nipple.Item, v => b.Nipple.Item = (string)v, Ref(334));
        Add("body.nipple.color", "chara.body.nipple.color", () => b.Nipple.Color, v => b.Nipple.Color = (Color4)v, AltC);
        Add("body.nipple.gloss", "chara.body.nipple.gloss", () => b.Nipple.Gloss, v => b.Nipple.Gloss = Fl(v), AltF);
        Add("body.nipple.areola", "chara.body.nipple.areola", () => b.Nipple.Areola, v => b.Nipple.Areola = Fl(v), AltF);
        Add("body.underhair.item", "chara.body.underhair.item", () => b.Underhair.Item, v => b.Underhair.Item = (string)v, Ref(335));
        Add("body.underhair.color", "chara.body.underhair.color", () => b.Underhair.Color, v => b.Underhair.Color = (Color4)v, AltC);
        Add("body.nail.color", "chara.body.nail.color", () => b.Nail.Color, v => b.Nail.Color = (Color4)v, AltC);
        Add("body.nail.gloss", "chara.body.nail.gloss", () => b.Nail.Gloss, v => b.Nail.Gloss = Fl(v), AltF);
        Add("body.visible", "chara.body.visible", () => b.Visible, v => b.Visible = (bool)v, v => !(bool)v);
        // a plugin section (ABMX): the plugin data is regenerated from the documents and the final stage re-runs
        Func<List<BodyBoneMod>, List<BodyBoneMod>> copyMods = src => src.Select(m => new BodyBoneMod { Bone = m.Bone, Scale = m.Scale, Length = m.Length, Position = m.Position, Rotation = m.Rotation, Location = m.Location }).ToList();
        Add("body.boneMods", "chara.body.boneMods", () => copyMods(b.BoneMods), v => b.BoneMods = (List<BodyBoneMod>)v,
            v => { var nl = copyMods((List<BodyBoneMod>)v); if (nl.Count > 0) nl[0].Scale = new Vec3(nl[0].Scale.X * 1.25f, nl[0].Scale.Y * 0.8f, nl[0].Scale.Z * 1.1f); return nl; },
            b.BoneMods.Count == 0 ? "no bone modifiers" : null);
        // hair
        var h = ch.Hair;
        Add("hair.shaderType", "chara.hair.shaderType", () => h.ShaderType, v => h.ShaderType = (int)v, v => (int)v == 0 ? 1 : 0);
        for (int k = 0; k < 4; k++)
        {
            var hp = h.Parts[k];
            string p = "chara.hair." + UnitMap.HairParts[k];
            Add("hair." + UnitMap.HairParts[k] + ".item", p + ".item", () => hp.Item, v => hp.Item = (string)v, Ref(300 + k, HasPrefab));
        }
        {
            var hp = h.Parts[1];
            const string p = "chara.hair.front";
            Add("hair.front.baseColor", p + ".baseColor", () => hp.BaseColor, v => hp.BaseColor = (Color4)v, AltC);
            Add("hair.front.topColor", p + ".topColor", () => hp.TopColor, v => hp.TopColor = (Color4)v, AltC);
            Add("hair.front.underColor", p + ".underColor", () => hp.UnderColor, v => hp.UnderColor = (Color4)v, AltC);
            Add("hair.front.specular", p + ".specular", () => hp.Specular, v => hp.Specular = (Color4)v, AltC);
            Add("hair.front.metallic", p + ".metallic", () => hp.Metallic, v => hp.Metallic = Fl(v), AltF);
            Add("hair.front.smoothness", p + ".smoothness", () => hp.Smoothness, v => hp.Smoothness = Fl(v), AltF);
            Add("hair.front.accessoryColors[0]", p + ".accessoryColors[0]", () => hp.AccessoryColors[0], v => hp.AccessoryColors[0] = (Color4)v, AltC);
            Add("hair.front.bundles", p + ".bundles", () => hp.Bundles.Select(x => new HairBundle { Index = x.Index, Move = x.Move, Rotate = x.Rotate, NoShake = x.NoShake }).ToList(),
                v => hp.Bundles = ((List<HairBundle>)v).ToList(),
                v => { var nl = ((List<HairBundle>)v).ToList(); nl.Add(new HairBundle { Index = 0, Move = new Vec3(0.2f, 0.7f, 0.4f), Rotate = new Vec3(0.6f, 0.1f, 0.9f), NoShake = true }); return nl; });
            Add("hair.front.mesh.item", p + ".mesh.item", () => hp.Mesh.Item, v => hp.Mesh.Item = (string)v, Ref(306));
        }
        // clothes
        var cats = co.Sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories;
        for (int k = 0; k < 8; k++)
        {
            var cp = co.Clothes.Parts[k];
            int cat = cats[k];
            string p = "coord.clothes." + ClothesSet.Names[k];
            Add("clothes." + ClothesSet.Names[k] + ".item", p + ".item", () => cp.Item, v => cp.Item = (string)v, Ref(cat, HasPrefab));
        }
        {
            var cp = co.Clothes.Parts[0];
            const string p = "coord.clothes.top";
            Add("clothes.top.colors[0].base", p + ".colors[0].base", () => cp.Colors[0].Base, v => cp.Colors[0].Base = (Color4)v, AltC);
            Add("clothes.top.colors[0].pattern", p + ".colors[0].pattern", () => cp.Colors[0].Pattern, v => cp.Colors[0].Pattern = (string)v, Ref(348, r => HasTex(r, "MainTex")));
            Add("clothes.top.colors[0].patternColor", p + ".colors[0].patternColor", () => cp.Colors[0].PatternColor, v => cp.Colors[0].PatternColor = (Color4)v, AltC);
            Add("clothes.top.colors[0].layout", p + ".colors[0].layout", () => cp.Colors[0].Layout, v => cp.Colors[0].Layout = (Vec4)v, AltV4);
            Add("clothes.top.colors[0].rotation", p + ".colors[0].rotation", () => cp.Colors[0].Rotation, v => cp.Colors[0].Rotation = Fl(v), AltF);
            Add("clothes.top.colors[0].gloss", p + ".colors[0].gloss", () => cp.Colors[0].Gloss, v => cp.Colors[0].Gloss = Fl(v), AltF);
            Add("clothes.top.colors[0].metallic", p + ".colors[0].metallic", () => cp.Colors[0].Metallic, v => cp.Colors[0].Metallic = Fl(v), AltF);
            Add("clothes.top.colors[1].base", p + ".colors[1].base", () => cp.Colors[1].Base, v => cp.Colors[1].Base = (Color4)v, AltC);
            Add("clothes.top.breakRate", p + ".breakRate", () => cp.BreakRate, v => cp.BreakRate = Fl(v), AltF);
            Add("clothes.top.hideOptions", p + ".hideOptions", () => (bool[])cp.HideOptions.Clone(), v => cp.HideOptions = (bool[])v, v => ((bool[])v).Select(x => !x).ToArray());
            var bp = co.Clothes.Parts[1];
            Add("clothes.bottom.breakRate", "coord.clothes.bottom.breakRate", () => bp.BreakRate, v => bp.BreakRate = Fl(v), AltF);
            var ip = co.Clothes.Parts[2];
            Add("clothes.innerTop.colors[0].base", "coord.clothes.innerTop.colors[0].base", () => ip.Colors[0].Base, v => ip.Colors[0].Base = (Color4)v, AltC);
        }
        // accessories (the first one in the list)
        var acc = co.Accessories.FirstOrDefault(a => a != null && a.Category >= 351 && a.Category <= 363);
        string none = acc == null ? "no accessory" : null;
        string ap = acc == null ? "coord.accessories" : "coord.accessories[\"" + acc.Id + "\"]";
        Add("accessory.item", ap + ".item", () => acc?.Item, v => acc.Item = (string)v, v => acc == null ? null : NextRow(e, acc.Category, (string)v, HasPrefab), none);
        Add("accessory.category", ap, () => acc == null ? null : (object)(acc.Category, acc.Item, acc.Parent), v => { var t = ((int, string, string))v; acc.Category = t.Item1; acc.Item = t.Item2; acc.Parent = t.Item3; },
            v =>
            {
                var t = ((int, string, string))v;
                int nc = t.Item1 == 351 ? 352 : 351;
                return ((object)(nc, NextRow(e, nc, "", HasPrefab), ""));
            }, none);
        Add("accessory.parent", ap + ".parent", () => acc?.Parent, v => acc.Parent = (string)v, v => (string)v == "N_Head" ? "N_Neck" : "N_Head", none);
        for (int j = 0; j < 2; j++)
        {
            int jj = j;
            Add("accessory.moves[" + j + "].position", ap + ".moves[" + j + "].position", () => acc?.Moves[jj].Position, v => acc.Moves[jj].Position = (Vec3)v, AltV3, none);
            if (j == 0)
            {
                Add("accessory.moves[0].rotation", ap + ".moves[0].rotation", () => acc?.Moves[0].Rotation, v => acc.Moves[0].Rotation = (Vec3)v, AltV3, none);
                Add("accessory.moves[0].scale", ap + ".moves[0].scale", () => acc?.Moves[0].Scale, v => acc.Moves[0].Scale = (Vec3)v, v => { var c = (Vec3)v; return new Vec3(c.X * 1.5f, c.Y * 0.5f, c.Z + 0.25f); }, none);
            }
        }
        Add("accessory.colors[0].color", ap + ".colors[0].color", () => acc?.Colors[0].Color, v => acc.Colors[0].Color = (Color4)v, AltC, none);
        Add("accessory.colors[0].gloss", ap + ".colors[0].gloss", () => acc?.Colors[0].Gloss, v => acc.Colors[0].Gloss = Fl(v), AltF, none);
        Add("accessory.colors[0].metallic", ap + ".colors[0].metallic", () => acc?.Colors[0].Metallic, v => acc.Colors[0].Metallic = Fl(v), AltF, none);
        Add("accessory.colors[3].color.a=0", ap + ".colors[3].color", () => acc?.Colors[3].Color, v => acc.Colors[3].Color = (Color4)v, v => { var c = (Color4)v; return new Color4(c.R, c.G, c.B, c.A == 0 ? 1 : 0); }, none);
        Add("accessory.noShake", ap + ".noShake", () => acc?.NoShake, v => acc.NoShake = (bool)v, v => !(bool)v, none);
        // accessory list
        Func<object> list = () => co.Accessories.ToList();
        Action<object> setList = v => { co.Accessories.Clear(); co.Accessories.AddRange((List<AccessoryEntry>)v); };
        Add("accessories.insert", "coord.accessories", list, setList, v =>
        {
            var nl = ((List<AccessoryEntry>)v).ToList();
            string item = NextRow(e, 352, "", HasPrefab);
            nl.Add(new AccessoryEntry { Id = "0123456789abcdef0123456789abcdef", Category = 352, Item = item, Parent = "" });
            return nl;
        });
        Add("accessories.remove", "coord.accessories", list, setList, v => { var nl = ((List<AccessoryEntry>)v).ToList(); if (acc != null) nl.Remove(acc); return nl; }, none);
        Add("accessories.move", "coord.accessories", list, setList, v =>
        {
            var nl = ((List<AccessoryEntry>)v).ToList();
            if (nl.Count >= 2) { var x = nl[0]; nl.RemoveAt(0); nl.Add(x); }
            return nl;
        }, co.Accessories.Count < 2 ? "fewer than two accessories" : null);
        Add("profile.name", "chara.profile.name", () => ch.Profile.Name, v => ch.Profile.Name = (string)v, v => (string)v + " U");
        return l;
    }

    static void PaintFamilies(Action<string, string, Func<object>, Action<object>, Func<object, object>, string> add, Func<int, Func<ListEntry, bool>, Func<object, object>> refOf,
                              Paint pt, string path, string name, int layoutCat)
    {
        add(name + ".item", path + ".item", () => pt.Item, v => pt.Item = (string)v, refOf(313, null), null);
        add(name + ".color", path + ".color", () => pt.Color, v => pt.Color = (Color4)v, AltC, null);
        add(name + ".gloss", path + ".gloss", () => pt.Gloss, v => pt.Gloss = Fl(v), AltF, null);
        add(name + ".metallic", path + ".metallic", () => pt.Metallic, v => pt.Metallic = Fl(v), AltF, null);
        add(name + ".layoutItem", path + ".layoutItem", () => pt.LayoutItem, v => pt.LayoutItem = (string)v, refOf(layoutCat, null), null);
        add(name + ".layout", path + ".layout", () => pt.Layout, v => pt.Layout = (Vec4)v, AltV4, null);
        add(name + ".rotation", path + ".rotation", () => pt.Rotation, v => pt.Rotation = Fl(v), AltF, null);
    }

    // -visual female|male: one of the two visual targets (the -Quick variant may run the female target only; the -Full row
    // runs both, the default)
    static IEnumerable<string> VisualTargets()
    {
        var t = TestTargets.Load();
        string v = Arg("-visual");
        if (v == "female") return new[] { TestTargets.Abs(t.visual.female) };
        if (v == "male") return new[] { TestTargets.Abs(t.visual.male) };
        return t.VisualCards;
    }

    public static void RunU()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string only = Arg("-family");
        rep.AppendLine($"maker core U (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}): every unit alone, both ways, on the visual targets");
        int fam = 0, famPass = 0, rebuilds = 0, skipped = 0;
        var flushMs = new List<double>();
        using (var h = MakerCoreTest.Harness.Open())
            foreach (var file in VisualTargets().Where(File.Exists))
            {
                var partTime = TestArgs.Get().Contains("-partTimes") ? Stopwatch.StartNew() : null;
                MakerCoreTest.Built live = null;
                try
                {
                    live = h.Build(file);
                    if (!live.Ok) { Check(Rel(file) + ": built", false, live.Error); continue; }
                    var ctx = live.Context;
                    ctx.Editor.SceneHooks = new TestHooks();
                    var s0 = live.Snapshot();
                    var env = new Env { Chara = ctx.Chara, Coord = ctx.Coord, Catalog = h.Catalog, Male = ctx.IsMale };
                    var fams = Families(env);
                    Info($"{Rel(file)}: {fams.Count} families, original snapshot {s0.Sha256.Substring(0, 16)} ({s0.Lines.Count} lines)");
                    foreach (var f in fams)
                    {
                        if (only != null && f.Name != only) continue;
                        string label = Rel(file) + " " + f.Name;
                        if (f.Skip != null) { skipped++; Info(label + ": skipped (" + f.Skip + ")"); continue; }
                        object old = f.Get();
                        object alt = f.Alt(old);
                        if (alt == null || Equals(alt, old)) { skipped++; Info(label + ": skipped (no other value)"); continue; }
                        fam++;
                        // 1. set, flush, compare with a fresh build of the edited documents
                        f.Set(alt);
                        bool marked = ctx.Editor.MarkDirty(f.Path);
                        var r = ctx.Editor.Flush();
                        if (r.RebuildRequested)
                        {
                            rebuilds++;
                            f.Set(old);
                            ctx.Editor.ClearRebuildRequest();
                            Info(label + ": rebuild-only (" + string.Join(", ", ctx.Editor.RebuildPaths) + ")");
                            continue;
                        }
                        flushMs.Add(r.Milliseconds);
                        var sA = live.Snapshot();
                        var container = new CharaContainer { Subtree = live.Result.Stored.Clone() };
                        container.Res.CopyFrom(live.Container.Res, container.Subtree.ReferencedResources());
                        var fresh = h.Build(container, "fresh " + f.Name);
                        bool ok1 = false; string d1 = "";
                        try
                        {
                            if (!fresh.Ok) d1 = "fresh build failed: " + fresh.Error;
                            else
                            {
                                var sF = fresh.Snapshot();
                                var d = CharaSnapshot.Compare(sF, sA, 12);
                                ok1 = d.Equal;
                                if (!ok1) d1 = "fresh vs edited: " + d;
                            }
                        }
                        finally { fresh.Dispose(); }
                        // 2. set back, flush, compare with the original
                        f.Set(old);
                        ctx.Editor.MarkDirty(f.Path);
                        var r2 = ctx.Editor.Flush();
                        flushMs.Add(r2.Milliseconds);
                        var sB = live.Snapshot();
                        var db = CharaSnapshot.Compare(s0, sB, 12);
                        bool ok2 = db.Equal;
                        string units = string.Join(",", r.Units);
                        Check(label, ok1 && ok2 && marked, $"units [{units}] {r.Milliseconds:F2} ms, back [{string.Join(",", r2.Units)}] {r2.Milliseconds:F2} ms" +
                              (marked ? "" : "; MarkDirty marked nothing") + (r.Errors.Count + r2.Errors.Count > 0 ? "; errors " + string.Join("; ", r.Errors.Concat(r2.Errors)) : "") +
                              (ok1 ? "" : "; " + d1) + (ok2 ? "" : "; original vs set back: " + db));
                        if (ok1 && ok2 && marked) famPass++;
                        if (!ok2)
                        {
                            // keep the next families independent: rebuild the live character from its (reverted) documents
                            live.Dispose();
                            live = h.Build(file);
                            ctx = live.Context;
                            ctx.Editor.SceneHooks = new TestHooks();
                            env = new Env { Chara = ctx.Chara, Coord = ctx.Coord, Catalog = h.Catalog, Male = ctx.IsMale };
                            var rest = Families(env);
                            int idx = fams.IndexOf(f);
                            fams = fams.Take(idx + 1).Concat(rest.Skip(idx + 1)).ToList();
                            s0 = live.Snapshot();
                        }
                    }
                }
                catch (Exception e) { Check(Rel(file) + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
                finally { live?.Dispose(); }
                h.ReleaseBundles();
                if (partTime != null) Info($"part time {Rel(file)}: {partTime.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s (-partTimes)");
            }
        Check("U: every path family both ways (edited = fresh build of the edited documents; set back = original)", fam > 0 && famPass == fam,
              $"{famPass} of {fam} family runs pass; {rebuilds} rebuild-only, {skipped} skipped");
        Info($"flush median {Median(flushMs):F2} ms, max {(flushMs.Count > 0 ? flushMs.Max() : 0):F1} ms over {flushMs.Count} flushes");
        Finish("maker_u_report.txt", "U every unit alone, both ways", sw);
    }

    // ================================================================ morph probe (diagnostics of S2 pairs, headless)

    /// <summary>Card -a built, its documents replaced member group by member group by those of card -b (no scene, no
    /// realization), one flush, snapshot vs a fresh build of -b. Prints the differences and the flush's log lines.</summary>
    public static void RunMorphProbe()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string Abs(string rel) => Path.Combine(TestTargets.UserData, rel);
        var pairs = new List<(string a, string b)>();
        if (Arg("-pairs") is string pf)
            foreach (var line in File.ReadAllLines(pf))
            {
                var c = line.Split((char)9);
                if (c.Length >= 2) pairs.Add((c[0].Trim(), c[1].Trim()));
            }
        else pairs.Add((Arg("-a"), Arg("-b")));
        bool verbose = TestArgs.Get().Contains("-log");
        rep.AppendLine($"morph probe, {pairs.Count} pairs (documents of A replaced by B's member groups, one flush, vs a fresh build of B made after)");
        int n = 0, eq = 0, rule = 0;
        using (var h = MakerCoreTest.Harness.Open())
            foreach (var (fa, fb) in pairs)
            {
                n++;
                string pair = fa + " -> " + fb;
                MakerCoreTest.Built a = null, b = null;
                try
                {
                    a = h.Build(Abs(fa));
                    if (!a.Ok) { Check(pair + ": A built", false, a.Error); continue; }
                    var ctx = a.Context;
                    ctx.Editor.SceneHooks = new TestHooks();
                    a.Snapshot();
                    CharaSubtree B;
                    {
                        var tmp = h.Build(Abs(fb));
                        if (!tmp.Ok) { Check(pair + ": B built", false, tmp.Error); tmp.Dispose(); continue; }
                        B = tmp.Result.Stored.Clone();
                        ctx.Edit.Input.Res.CopyFrom(tmp.Container.Res, B.ReferencedResources());
                        tmp.Dispose();
                    }
                    var ch = ctx.Chara; var co = ctx.Coord;
                    string Sig(DocObject o) => HS2Next.Chara.Edit.Units.Signature.Of(o);
                    if (Sig(ch.Body.Uncensor) != Sig(B.Chara.Body.Uncensor) || Sig(ch.StudioOverlays) != Sig(B.Chara.StudioOverlays) ||
                        HS2Next.Chara.Edit.Units.Signature.Of(ch.MaterialOverrides) != HS2Next.Chara.Edit.Units.Signature.Of(B.Chara.MaterialOverrides))
                    { rule++; Info(pair + ": rebuild by rule (uncensor / studioOverlays / Character materialOverrides differ), skipped"); continue; }
                    ch.Face = B.Chara.Face; ch.Hair = B.Chara.Hair;
                    var bb = B.Chara.Body; bb.Uncensor = ch.Body.Uncensor; ch.Body = bb;
                    ch.Profile.Name = B.Chara.Profile.Name;
                    co.Clothes = B.Coord.Clothes; co.Accessories.Clear(); co.Accessories.AddRange(B.Coord.Accessories);
                    co.RuleOverrides = B.Coord.RuleOverrides;
                    int log0 = ctx.Log.Count;
                    foreach (var p in new[] { "chara.face", "chara.hair", "chara.profile.name", "chara.body.shape", "chara.body.bust", "chara.body.skin", "chara.body.detail",
                                              "chara.body.sunburn", "chara.body.paint", "chara.body.nipple", "chara.body.underhair", "chara.body.nail", "chara.body.visible",
                                              "chara.body.skinOverlays", "chara.body.boneMods", "coord.clothes", "coord.accessories", "coord.ruleOverrides" })
                        ctx.Editor.MarkDirty(p);
                    var r = ctx.Editor.Flush();
                    var sa = a.Snapshot();
                    var container = new CharaContainer { Subtree = a.Result.Stored.Clone() };
                    container.Res.CopyFrom(ctx.Edit.Input.Res, container.Subtree.ReferencedResources());
                    b = h.Build(container, "fresh B");
                    if (!b.Ok) { Check(pair + ": fresh B built", false, b.Error); continue; }
                    var sb = b.Snapshot();
                    if (sa.Sha256 == sb.Sha256) { eq++; Info(pair + ": equal"); continue; }
                    var la = new HashSet<string>(sa.Lines); var lb = new HashSet<string>(sb.Lines);
                    var onlyB = sb.Lines.Where(l => !la.Contains(l)).Take(8).ToList();
                    var onlyA = sa.Lines.Where(l => !lb.Contains(l)).Take(8).ToList();
                    var sbx = new StringBuilder();
                    sbx.Append("flush ").Append(r.ToString().Length > 300 ? r.ToString().Substring(0, 300) : r.ToString());
                    foreach (var l in onlyB) sbx.Append(Environment.NewLine + "      fresh: ").Append(l.Length > 1500 ? l.Substring(0, 1500) : l);
                    foreach (var l in onlyA) sbx.Append(Environment.NewLine + "      morph: ").Append(l.Length > 1500 ? l.Substring(0, 1500) : l);
                    if (verbose) foreach (var l in ctx.Log.Skip(log0)) sbx.Append(Environment.NewLine + "      log: ").Append(l);
                    Check(pair + ": morph = fresh", false, sbx.ToString());
                }
                catch (Exception e) { Check(pair + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
                finally { a?.Dispose(); b?.Dispose(); }
                if (n % 8 == 0) h.ReleaseBundles();
            }
        Check($"morph probe: equal on every pair", eq + rule == pairs.Count, $"{eq} equal, {rule} rule, {pairs.Count - eq - rule} differ of {pairs.Count}");
        Finish("maker_morph_probe.txt", "morph probe", sw);
    }

    // ================================================================ fuzz probe (diagnostics of package C's F seeds, headless)

    /// <summary>
    /// One seed of package C's realization fuzz (MakerEditTest.RunFuzz, its editor driven by reflection, run only): the same
    /// node, operations and checks every 10 operations; with -from K also a check after every operation from K on (the
    /// extra snapshots step the motion at dt 0 too). Each differing check prints the operations and the full differing lines.
    /// </summary>
    public static void RunFuzzProbe()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int seed = int.Parse(Arg("-seed", "0"), CultureInfo.InvariantCulture);
        int ops = int.Parse(Arg("-ops", "40"), CultureInfo.InvariantCulture);
        int from = int.Parse(Arg("-from", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                                                   System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var tEd = typeof(MakerEditTest).GetNestedType("Editor", Any);
        var tPools = typeof(MakerEditTest).GetNestedType("Pools", Any);
        rep.AppendLine($"fuzz probe: seed {seed}, {ops} operations, a check every 10 and after every operation from {(from == int.MaxValue ? "-" : from.ToString(CultureInfo.InvariantCulture))}");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                var targets = new List<(string file, CharaContainer c)>();
                foreach (var f in TestTargets.Load().VisualCards.Where(File.Exists))
                {
                    var ct = MakerEditTest.Import(h, f, out string err);
                    if (ct != null) targets.Add((f, ct));
                }
                var pools = Activator.CreateInstance(tPools, Any, null, new object[] { h.Catalog, targets.Select(t => t.c.Subtree) }, null);
                var (file, c) = targets[seed % targets.Count];
                var n = MakerEditTest.NewNode(h, c, Path.GetFileName(file));
                try
                {
                    if (!n.Ok) { Check("built", false, n.Error); return; }
                    var ed = Activator.CreateInstance(tEd, Any, null, new object[] { n, 1000 + seed, pools }, null);
                    var step = (Func<OpResult>)Delegate.CreateDelegate(typeof(Func<OpResult>), ed, tEd.GetMethod("Any", Any));
                    var tx = tEd.GetMethod("Transaction", Any);
                    var log = (List<string>)tEd.GetField("Log", Any).GetValue(ed);
                    int shown = 0, diffs = 0;
                    for (int done = 1; done <= ops; done++)
                    {
                        tx.Invoke(ed, new object[] { step });
                        if (done % 10 != 0 && done < from) continue;
                        var sm = n.Snapshot();
                        using (var fresh = MakerEditTest.NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), "fresh"))
                        {
                            if (!fresh.Ok) { Check($"after {done}: fresh built", false, fresh.Error); continue; }
                            var sf = fresh.Snapshot();
                            if (sf.Sha256 == sm.Sha256) { Info($"after {done}: equal"); continue; }
                            diffs++;
                            var lm = new HashSet<string>(sm.Lines); var lf = new HashSet<string>(sf.Lines);
                            var sbx = new StringBuilder();
                            sbx.Append("operations ").Append(shown).Append("..").Append(log.Count - 1).Append(':');
                            for (int i = shown; i < log.Count; i++) sbx.Append(Environment.NewLine + "      op " + i.ToString(CultureInfo.InvariantCulture) + ": ").Append(log[i]);
                            shown = log.Count;
                            foreach (var l in sf.Lines.Where(l => !lm.Contains(l)).Take(16)) sbx.Append(Environment.NewLine + "      fresh: ").Append(l.Length > 1500 ? l.Substring(0, 1500) : l);
                            foreach (var l in sm.Lines.Where(l => !lf.Contains(l)).Take(16)) sbx.Append(Environment.NewLine + "      edit:  ").Append(l.Length > 1500 ? l.Substring(0, 1500) : l);
                            Check($"after {done}: realized = fresh", false, sbx.ToString());
                        }
                    }
                    if (diffs == 0) Check("every check equal", true, string.Join(" | ", log));
                }
                finally { n.Dispose(); }
            }
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_fuzz_probe.txt", "fuzz probe", sw);
    }

    // ================================================================ accessory probe (diagnostics, headless)

    /// <summary>Card -card built; an accessory added (-cat, -item), flushed; its item changed to -item2, flushed; snapshot vs
    /// a fresh build of the documents, full differing lines.</summary>
    public static void RunAccProbe()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string card = Arg("-card"), item = Arg("-item"), item2 = Arg("-item2");
        int cat = int.Parse(Arg("-cat", "351"), CultureInfo.InvariantCulture);
        rep.AppendLine($"accessory probe {card}: add {cat} {item}, then item {item2}");
        using (var h = MakerCoreTest.Harness.Open())
        {
            var a = h.Build(Path.Combine(TestTargets.UserData, card));
            MakerCoreTest.Built b = null;
            try
            {
                var ctx = a.Context;
                ctx.Editor.SceneHooks = new TestHooks();
                a.Snapshot();
                var e = new AccessoryEntry { Id = "0123456789abcdef0123456789abcde1", Category = cat, Item = item, Parent = "" };
                ctx.Coord.Accessories.Add(e);
                ctx.Editor.MarkDirty("coord.accessories");
                Info("flush 1: " + ctx.Editor.Flush());
                a.Snapshot();
                if (item2 != null)
                {
                    e.Item = item2;
                    ctx.Editor.MarkDirty("coord.accessories[\"" + e.Id + "\"].item");
                    Info("flush 2: " + ctx.Editor.Flush());
                }
                var sa = a.Snapshot();
                var container = new CharaContainer { Subtree = a.Result.Stored.Clone() };
                container.Res.CopyFrom(ctx.Edit.Input.Res, container.Subtree.ReferencedResources());
                b = h.Build(container, "fresh");
                var sb = b.Snapshot();
                var la = new HashSet<string>(sa.Lines); var lb = new HashSet<string>(sb.Lines);
                foreach (var l in sb.Lines.Where(l => !la.Contains(l)).Take(12)) Info("fresh: " + l);
                foreach (var l in sa.Lines.Where(l => !lb.Contains(l)).Take(12)) Info("edit:  " + l);
                foreach (var l in ctx.Log.Where(l => l.StartsWith("acc"))) Info("log: " + l);
                Check("edit = fresh", sa.Sha256 == sb.Sha256);
            }
            finally { a.Dispose(); b?.Dispose(); }
        }
        Finish("maker_acc_probe.txt", "accessory probe", sw);
    }

    // ================================================================ U2 (headless)

    public static void RunU2()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "50"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker core U2 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}): shape engine and shape units, median of {reps}");
        using (var h = MakerCoreTest.Harness.Open())
            foreach (var file in TestTargets.Load().VisualCards.Where(File.Exists))
            {
                MakerCoreTest.Built b = null;
                try
                {
                    b = h.Build(file);
                    if (!b.Ok) { Check(Rel(file) + ": built", false, b.Error); continue; }
                    var ctx = b.Context;
                    b.Snapshot();   // stages stepped once
                    var tf = new List<double>(); var tb = new List<double>(); var tg = new List<double>(); var uf = new List<double>(); var ub = new List<double>(); var none = new List<double>();
                    var anim = HS2Next.Motion.Animation.CharaAnimation.Of(ctx.Root);
                    for (int i = 0; i < reps; i++)
                    {
                        var t = Stopwatch.StartNew(); ctx.FaceShape?.Apply(); tf.Add(t.Elapsed.TotalMilliseconds);
                        t.Restart(); ctx.BodyShape?.Apply(); tb.Add(t.Elapsed.TotalMilliseconds);
                        t.Restart(); if (anim) anim.RefreshBodyShape(ctx); tg.Add(t.Elapsed.TotalMilliseconds);
                        ctx.Editor.MarkDirty("chara.face.shape[3]"); uf.Add(ctx.Editor.Flush().Milliseconds);
                        ctx.Editor.MarkDirty("chara.body.shape[3]"); ub.Add(ctx.Editor.Flush().Milliseconds);
                        t.Restart(); ctx.Editor.Flush(); none.Add(t.Elapsed.TotalMilliseconds);
                    }
                    Info($"{Rel(file)}: ShapeEngine.Apply face {F(Median(tf))} ms, body {F(Median(tb))} ms; guard + rest recapture (CharaAnimation.RefreshBodyShape) {F(Median(tg))} ms");
                    Info($"{Rel(file)}: flush shape.face {F(Median(uf))} ms (target 0.3), shape.body incl. bust, animator parameters, guard, rest {F(Median(ub))} ms (target 0.6), nothing dirty {F(Median(none))} ms (target 0.02)");
                    Check(Rel(file) + ": U2 measured", true);
                }
                catch (Exception e) { Check(Rel(file) + ": no exception", false, e.GetType().Name + ": " + e.Message); }
                finally { b?.Dispose(); }
            }
        Finish("maker_u2_report.txt", "U2 shape engine timing", sw);
    }

    // ================================================================ U4 (headless)

    public static void RunU4()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"maker core U4 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}): memory per character, URP mapping in place (C2) vs copies");
        using (var h = MakerCoreTest.Harness.Open())
            foreach (var file in TestTargets.Load().VisualCards.Where(File.Exists))
                foreach (bool inPlace in new[] { true, false })
                {
                    MakerCoreTest.Built b = null;
                    bool keep = CharaBuilders.UrpInPlace;
                    try
                    {
                        CharaBuilders.UrpInPlace = inPlace;
                        EditorUtility.UnloadUnusedAssetsImmediate(true);
                        int mats0 = Resources.FindObjectsOfTypeAll<Material>().Length;
                        int rts0 = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
                        b = h.Build(file);
                        if (!b.Ok) { Check(Rel(file) + ": built", false, b.Error); continue; }
                        int mats1 = Resources.FindObjectsOfTypeAll<Material>().Length;
                        var rts = new HashSet<RenderTexture>();
                        foreach (var r in b.Root.GetComponentsInChildren<Renderer>(true))
                            foreach (var m in r.sharedMaterials)
                                if (m) foreach (var id in m.GetTexturePropertyNameIDs()) if (m.GetTexture(id) is RenderTexture rt && rt) rts.Add(rt);
                        foreach (var c in b.Context.Edit.Composites()) if (c.Creator.Target) rts.Add(c.Creator.Target);
                        long bytes = rts.Sum(rt => (long)rt.width * rt.height * 4 * (rt.useMipMap ? 4 : 3) / 3);
                        int drawn = b.Root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().Count();
                        Info($"{Rel(file)} {(inPlace ? "in place (C2)" : "copies (pre-4.0)")}: render textures {rts.Count} ({bytes / (1024.0 * 1024.0):F1} MiB), " +
                             $"owned materials {b.Context.OwnedMaterials.Count}, drawn materials {drawn}, Material objects added by the build {mats1 - mats0}, RT objects added {Resources.FindObjectsOfTypeAll<RenderTexture>().Length - rts0}");
                        Check(Rel(file) + (inPlace ? " C2" : " copies") + ": measured", true);
                    }
                    catch (Exception e) { Check(Rel(file) + ": no exception", false, e.GetType().Name + ": " + e.Message); }
                    finally { CharaBuilders.UrpInPlace = keep; b?.Dispose(); }
                }
        Finish("maker_u4_report.txt", "U4 memory per character", sw);
    }

    // ================================================================ U1 + U3 (graphics)

    static double GpuMs(Action draw, RenderTexture rt, int reps, List<double> cpu = null)
    {
        var l = new List<double>();
        for (int i = 0; i < reps; i++)
        {
            AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();   // idle GPU
            var t = Stopwatch.StartNew();
            draw();
            double c = t.Elapsed.TotalMilliseconds;
            AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            l.Add(t.Elapsed.TotalMilliseconds);
            cpu?.Add(c);
        }
        return Median(l);
    }

    static Texture2D Noise(int w, int h, int seed, bool alpha)
    {
        var rnd = new System.Random(seed);
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), alpha ? (byte)rnd.Next(256) : (byte)255);
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }

    public static void RunU1()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "20"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker core U1 + U3 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}): GPU time of the compositing passes (rebuild + a 1-pixel read-back fence, idle-fence first; median of {reps}); synthetic noise layers");
        bool device = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        if (device)
            using (var h = MakerCoreTest.Harness.Open())
            {
                var file = TestTargets.Load().VisualCards.FirstOrDefault(File.Exists);
                var b = h.Build(file);
                try
                {
                    var x = b.Context.Edit;
                    var layer = Noise(4096, 4096, 7, true);
                    foreach (var (name, c) in new[] { ("skin.body colour", x.BodySkin), ("skin.body detail", x.BodyDetail), ("skin.face colour", x.FaceSkin), ("skin.face detail", x.FaceDetail) })
                    {
                        if (c == null) continue;
                        var keepU = c.Creator.Under.ToList(); var keepO = c.Creator.Over.ToList();
                        for (int n = 0; n <= 2; n++)
                        {
                            c.Creator.Under.Clear(); c.Creator.Over.Clear();
                            for (int i = 0; i < n; i++) c.Creator.Over.Add(new SkinLayer { TexType = 1, Texture = layer, Source = "u1" });
                            var cpu = new List<double>();
                            double ms = GpuMs(() => c.Creator.Rebuild(), c.Creator.Target, reps, cpu);
                            Info($"{name} {c.Creator.Target.width}x{c.Creator.Target.height}, {n} layer(s): rebuild incl. mips {F(ms)} ms (CPU submit {F(Median(cpu))} ms)");
                        }
                        c.Creator.Under.Clear(); c.Creator.Under.AddRange(keepU); c.Creator.Over.Clear(); c.Creator.Over.AddRange(keepO);
                        c.Creator.Rebuild();
                    }
                    var png = ImageConversion.EncodeToPNG(Noise(4096, 4096, 11, true));
                    var dec = new List<double>();
                    for (int i = 0; i < 5; i++)
                    {
                        var t = Stopwatch.StartNew();
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                        ImageConversion.LoadImage(tex, png, false);
                        dec.Add(t.Elapsed.TotalMilliseconds);
                        Object.DestroyImmediate(tex);
                    }
                    Info($"decode of a 4096² layer PNG ({png.Length / (1024.0 * 1024.0):F1} MiB, noise): {F(Median(dec))} ms (median of 5)");
                    Object.DestroyImmediate(layer);
                    Check("U1 measured", true);

                    // U3: clothes colour passes per group size
                    foreach (int size in new[] { 512, 1024, 2048, 4096 })
                    {
                        var main = Noise(size, size, size, false);
                        var col = new TexCreator(h.Catalog, "create_clothes", size, size) { Main = main };
                        var gls = new TexCreator(h.Catalog, "create_clothes detail", size, size) { Main = main };
                        col.Mat.SetTexture("_ColorMask", main); gls.Mat.SetTexture("_ColorMask", main);
                        double ms = GpuMs(() => { col.Rebuild(); gls.Rebuild(); }, gls.Target, reps);
                        Info($"clothes colour pass, one group {size}x{size} (create_clothes + detail): {F(ms)} ms");
                        col.Target.Release(); gls.Target.Release();
                        Object.DestroyImmediate(col.Target); Object.DestroyImmediate(gls.Target);
                        Object.DestroyImmediate(col.Mat); Object.DestroyImmediate(gls.Mat); Object.DestroyImmediate(main);
                    }
                    var cl = new List<double>();
                    for (int k = 0; k < 8; k++)
                    {
                        if (x.ClothesColor[k, 0] == null) continue;
                        string id = ClothesNames.ColorsUnit(k);
                        var l = new List<double>();
                        for (int i = 0; i < reps; i++)
                        {
                            b.Context.Editor.MarkUnit(id);
                            var t = Stopwatch.StartNew();
                            b.Context.Editor.Flush();
                            AsyncGPUReadback.Request(x.ClothesColor[k, 0].Creator.Target, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                            l.Add(t.Elapsed.TotalMilliseconds);
                        }
                        Info($"{Rel(file)} clothes kind {k} colour unit ({x.ClothesColor[k, 0].Creator.Target.width}x{x.ClothesColor[k, 0].Creator.Target.height}): {F(Median(l))} ms incl. GPU");
                    }
                    Check("U3 measured", true);
                }
                catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
                finally { b.Dispose(); }
            }
        Finish("maker_u1_report.txt", "U1 / U3 compositing GPU time", sw);
    }
}
