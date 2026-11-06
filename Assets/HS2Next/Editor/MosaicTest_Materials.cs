// MosaicTest, material edits (Phase 6.4 package R). The rule is what MaterialGuard and the applier's rule by name say; these
// checks read it from the outside, by numbers. Items are synthetic, on the targets of Tools/targets.json. A command that is not registered yet is left out with an
// information line, so the groups run before the commands land and judge them after.
//   MO0  census: the members by target class on the two targets; AIT/mnpb in the item and map shader censuses.
//   MO1  every type on o_mnpa, o_mnpb, cm_o_dan00 and on c_m_mnpb: the status is mosaicProtected; a command answers unsupported with me.mosaicProtected; no entry, the array unchanged.
//   MO2  shader to each of the three mosaic names on a clothes material and on the body skin: refused, no candidate, the shader stays.
//   MO3  hand-edited arrays with such items, through chara.edit.set and in a file: byte round trip, materials and renderers unchanged, the members' materials intact, listed.
//   MO4  undo and redo around a change that makes a renderer a member: nothing is written to the member.
//   MO5  a renderer added to the set: refused; the other renderers of the part stay editable.
//   MO8  the body skin: MainTex, AlphaMask, a shader swap, Enabled 0 on o_body_cf are allowed; the members keep their mosaic materials; after a MainTex value pass the member's source is the new texture.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;
using static MaterialEditTest;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

public static partial class MosaicTest
{
    static void MaterialGroups()
    {
        if (!Want("MO0") && !Want("MO1") && !Want("MO2") && !Want("MO3") && !Want("MO4") && !Want("MO5") && !Want("MO8")) return;
        var seed = HS2Next.Motion.Face.FaceSetup.Seed;
        var h = NewHost();
        try
        {
            if (Want("MO0")) Guard("MO0 census", () => Mo0(h));
            if (Want("MO1")) Guard("MO1 members and mosaic materials refused", () => Mo1(h));
            if (Want("MO2")) Guard("MO2 swaps to the mosaic shaders", () => Mo2(h));
            if (Want("MO3")) Guard("MO3 hand-edited arrays", () => Mo3(h));
            if (Want("MO3") || Want("MO5")) Guard("MO3, MO5 a member on an item", () => MoItem(h));
            if (Want("MO4")) Guard("MO4 undo around membership", () => Mo4(h));
            if (Want("MO5")) Guard("MO5 an added renderer", () => Mo5(h));
            if (Want("MO5")) Guard("MO5 the writer on a synthetic prefab", Mo5Writer);
            if (Want("MO8")) Guard("MO8 the body skin", () => Mo8(h));
        }
        finally { h.Dispose(); HS2Next.Motion.Face.FaceSetup.Seed = seed; }
    }

    static string Scene(McHost h, bool male = false)
    {
        h.Ok(new { cmd = "scene.new" }, "scene.new");
        return LoadTarget(h, male);
    }

    static int History(McHost h) => (h.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? 0;

    static void Mo0(McHost h)
    {
        foreach (var (label, male) in new[] { ("BR-Chan", false), ("HERO107", true) })
        {
            string key = Scene(h, male);
            var ctx = Ctx(h, key);
            var all = ctx.Root.GetComponentsInChildren<Renderer>(true);
            var classes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in all.Where(MosaicSet.IsMember))
            {
                string cls = ctx.Hair.Any(o => o && r.transform.IsChildOf(o.transform)) ? "hair"
                    : ctx.Clothes.Any(o => o && r.transform.IsChildOf(o.transform)) ? "clothes"
                    : ctx.Accessories.Any(a => a?.Object && r.transform.IsChildOf(a.Object.transform)) ? "accessory" : "body";
                classes[cls] = classes.TryGetValue(cls, out int n) ? n + 1 : 1;
            }
            Info($"MO0 {label}: {all.Length} renderers, members " + string.Join(", ", classes.Select(kv => kv.Key + " " + kv.Value)) + ": " +
                 string.Join(", ", all.Where(MosaicSet.IsMember).Select(r => r.name)));
            Check($"MO0 {label}: at least one member", classes.Count > 0, "");
        }
        foreach (var f in new[] { "item_shader_census.tsv", "map_shader_census.tsv" })
        {
            string p = Path.Combine(ContentSettings.ProjectRoot, "Tools", "survey", f);
            if (!File.Exists(p)) { Warn("MO0", f + " is not here"); continue; }
            Info($"MO0 {f}: lines with mnpb {File.ReadLines(p).Count(l => l.Contains("mnpb"))}");
        }
    }

    static IEnumerable<MaterialOverride> EveryType(string material, string renderer)
    {
        yield return new MeFloat { Material = material, Property = "Gloss", Value = 0.4f };
        yield return new MeColor { Material = material, Property = "Color", Value = new Color4(1, 0, 0, 1) };
        yield return new MeTexture { Material = material, Property = "MainTex", Offset = new Vec2(0.5f, 0.5f) };
        yield return new MeShader { Material = material, RenderQueue = 3000 };
        yield return new MeKeyword { Material = material, Property = "DITHERSWITCH_ON", Value = true };
        yield return new MeRenderer { Renderer = renderer, Property = "Enabled", Value = 0 };
    }

    static JObject EditEdge(string type, string material, string renderer)
    {
        switch (type)
        {
            case "float": return JObject.FromObject(new { type, material, property = "Gloss", value = 0.4f });
            case "color": return JObject.FromObject(new { type, material, property = "Color", value = new[] { 1f, 0f, 0f, 1f } });
            case "texture": return JObject.FromObject(new { type, material, property = "MainTex", offset = new[] { 0.5f, 0.5f } });
            case "shader": return JObject.FromObject(new { type, material, renderQueue = 3000 });
            case "keyword": return JObject.FromObject(new { type, material, property = "DITHERSWITCH_ON", value = true });
            default: return JObject.FromObject(new { type = "renderer", renderer, property = "Enabled", value = 0 });
        }
    }

    static bool CommandsIn(McHost h) => h.Call(new { cmd = "chara.edit.materials.items", key = "?" }).Value<string>("code") != "unknown_command";

    static void Mo1(McHost h)
    {
        foreach (var male in new[] { false, true })
        {
            string key = Scene(h, male);
            var ctx = Ctx(h, key);
            var obj = (OT.Character, 0);
            var members = MaterialEditorApplier.ObjectRenderers(ctx, obj).Where(MosaicSet.IsMember).ToList();
            Check($"MO1 {(male ? "HERO107" : "BR-Chan")}: members in the Character object", members.Count > 0, string.Join(", ", members.Select(r => r.name)));
            bool cmds = CommandsIn(h);
            if (!cmds) Info("MO1 the commands are not registered yet: the status read stands for them");
            foreach (var r in members)
            {
                string rn = MaterialEditorApplier.Fmt(r.name);
                foreach (var name in r.sharedMaterials.Where(m => m).Select(m => MaterialEditorApplier.Fmt(m.name)).Distinct().Concat(new[] { MosaicSet.OriginalMaterial }))
                    foreach (var item in EveryType(name, rn))
                    {
                        // a name other renderers hold too stays editable there: the rule is about what only members hold
                        bool others = !(item is MeRenderer) && MaterialEditorApplier.MaterialsNamed(ctx, obj, name).Count > 0;
                        string what = $"MO1 {rn} {item.Type} {name}";
                        var st = MaterialEditorApplier.Status(ctx, obj, item);
                        Check(what + ": status", others || st.Status == MaterialStatus.MosaicProtected, st.Status);
                        Check(what + ": the guard", item is MeRenderer ? MaterialGuard.Renderer(r) : true, "");
                        if (!cmds || others) continue;
                        int n0 = History(h);
                        string json = Json(Stored(h, key)?.Chara.MaterialOverrides ?? new List<MaterialOverride>());
                        var q = EditEdge(item.Type, name, rn);
                        var call = new JObject { ["cmd"] = "chara.edit.materials.set", ["key"] = key, ["target"] = "character" };
                        foreach (var p in q.Properties()) call[p.Name] = p.Value;
                        var a = h.Call(call.ToString());
                        Check(what + ": refused", a.Value<bool?>("ok") == false && a.Value<string>("code") == "unsupported", Short(a.ToString()));
                        Check(what + ": warning me.mosaicProtected", a.ToString().Contains(MosaicSet.MeProtectedCode), Short(a.ToString()));
                        Check(what + ": no entry", History(h) == n0, "");
                        Check(what + ": array unchanged", Json(Stored(h, key)?.Chara.MaterialOverrides ?? new List<MaterialOverride>()) == json, "");
                    }
            }
            foreach (var m in members.SelectMany(r => r.sharedMaterials).Where(x => x)) Check("MO1 the guard knows a member's material", MaterialGuard.Material(m), m.name);
        }
    }

    static void Mo2(McHost h)
    {
        string key = Scene(h);
        var ctx = Ctx(h, key);
        int ck = Enumerable.Range(0, 8).First(k => ctx.Clothes[k]);
        var cloth = ClothesTarget(ck);
        var clothMat = MaterialEditorApplier.ObjectRenderers(ctx, cloth.Object).SelectMany(r => r.sharedMaterials).First(x => x && !MaterialGuard.Material(x));
        var skinName = MaterialEditorApplier.Fmt(ctx.BodyDraw.name);
        var clothName = MaterialEditorApplier.Fmt(clothMat.name);
        foreach (var name in new[] { MosaicSet.OriginalShader, MosaicSet.ShaderName, MosaicSet.LegacyShader })
        {
            Check($"MO2 {name}: the guard", MaterialGuard.Shader(name), "");
            Check($"MO2 {name}: not a candidate", !MaterialShaders.IsCandidate(name) && MaterialShaders.All.All(c => c.Name != name), "");
            foreach (var (t, mat) in new[] { (cloth, clothName), (CharacterTarget(), skinName) })
            {
                var item = new MeShader { Material = mat, Shader = name };
                Check($"MO2 {name} on {mat}: status", MaterialEditorApplier.Status(Ctx(h, key), t.Object, item).Status == MaterialStatus.MosaicProtected, MaterialEditorApplier.Status(Ctx(h, key), t.Object, item).Status);
                string shader = MaterialEditorApplier.MaterialsNamed(Ctx(h, key), t.Object, mat)[0].shader.name;
                var list = new List<MaterialOverride>(t.List(Stored(h, key)) ?? new List<MaterialOverride>()) { item };
                SetArray(h, key, t, list);
                Check($"MO2 {name} on {mat}: a file's item keeps the shader", MaterialEditorApplier.MaterialsNamed(Ctx(h, key), t.Object, mat)[0].shader.name == shader, MaterialEditorApplier.MaterialsNamed(Ctx(h, key), t.Object, mat)[0].shader.name);
                SetArray(h, key, t, new List<MaterialOverride>());
            }
        }
        Check("MO2 a name that resolves to a mosaic shader counts too", ShaderMap.Fitted.Where(kv => MosaicSet.IsMosaicShaderName(kv.Value)).All(kv => MaterialGuard.Shader(kv.Key)), "");
        Check("MO2 another shader is no mosaic shader", !MaterialGuard.Shader("HS2Next/AIT Clothes True") && !MaterialGuard.Shader(null), "");
    }

    static void Mo3(McHost h)
    {
        string key = Scene(h);
        var ch = CharacterTarget();
        var ctx = Ctx(h, key);
        var st0 = MosaicSet.StateOf(ctx);
        var bytes0 = st0.Members.Select(m => m.Renderer.sharedMaterials.Select(x => x.GetEntityId()).ToArray()).ToList();
        var member = st0.Members.First(m => m.Renderer.name == "o_mnpb").Renderer;
        var raw = new JArray(
            JObject.FromObject(new { type = "float", material = MosaicSet.OriginalMaterial, property = "Gloss", value = 0.25, original = 0.5 }),
            JObject.FromObject(new { type = "renderer", renderer = "o_mnpb", property = "Enabled", value = 0, original = 1 }));
        var q = new JObject { ["cmd"] = "chara.edit.set", ["key"] = key, ["set"] = new JObject { ["chara.materialOverrides"] = raw } };
        var a = h.Call(q.ToString());
        Check("MO3 chara.edit.set takes the raw array", a.Value<bool?>("ok") == true, Short(a.ToString()));
        h.Rt.ApplyNow();
        ctx = Ctx(h, key);
        Check("MO3 the member stays drawn", member && member.enabled, "");
        var st = MosaicSet.StateOf(ctx);
        Check("MO3 the members' materials are intact", st.Members.All(m => m.Renderer.sharedMaterials.All(MosaicUnit.IsUnitMaterial)), "");
        var list = ch.List(Stored(h, key));
        Check("MO3 kept in the document", list.Count == 2, list.Count + " items");
        foreach (var it in list)
            Check($"MO3 status of {it.Type}", MaterialEditorApplier.Status(ctx, ch.Object, it).Status == MaterialStatus.MosaicProtected, MaterialEditorApplier.Status(ctx, ch.Object, it).Status);
        var mosaic = h.Call(new { cmd = "chara.mosaic", key });
        Check("MO3 chara.mosaic lists them", (mosaic["protected"] as JArray)?.Count == 2, Short(mosaic.ToString()));
        string json = Json(list);
        var save = h.Ok(new { cmd = "scene.save", name = "mo3", overwrite = true, thumb = "placeholder", clock = "2026-11-05T00:00:00Z" }, "MO3 scene.save");
        h.Ok(new { cmd = "scene.new" }, "MO3 scene.new");
        h.Ok(new { cmd = "scene.load", path = (string)save["path"] }, "MO3 scene.load");
        h.Rt.ApplyNow();
        Check("MO3 the array round-trips", Json(ch.List(Stored(h, key))) == json, "");
        var ctx2 = Ctx(h, key);
        var st2 = MosaicSet.StateOf(ctx2);
        Check("MO3 loaded: the members' materials are intact", st2.Members.All(m => m.Renderer.sharedMaterials.All(MosaicUnit.IsUnitMaterial)), "");
        Check("MO3 loaded: the member stays drawn", st2.Members.First(m => m.Renderer.name == "o_mnpb").Renderer.enabled, "");
    }

    static void Mo4(McHost h)
    {
        string key = Scene(h);
        var ctx = Ctx(h, key);
        int ck = Enumerable.Range(0, 8).First(k => ctx.Clothes[k]);
        var cloth = ClothesTarget(ck);
        var r = MaterialEditorApplier.ObjectRenderers(ctx, cloth.Object).First(x => !MosaicSet.IsMember(x) && x.sharedMaterials.Any(m => m && FirstColor(m) != null));
        var m0 = r.sharedMaterials.First(m => m && FirstColor(m) != null);
        string mat = MaterialEditorApplier.Fmt(m0.name), prop = FirstColor(m0);
        var was = m0.GetColor(prop);
        Edit(h, key, cloth, MaterialEdit.ColorOf(mat, prop.Substring(1), new Color4(0.1f, 0.2f, 0.3f, 1), C4(was)));
        var written = MaterialEditorApplier.MaterialsNamed(Ctx(h, key), cloth.Object, mat)[0].GetColor(prop);
        h.Ok(new { cmd = "undo" }, "MO4 undo");
        h.Rt.ApplyNow();
        // membership comes between the undo and the redo: the entry is there, the writer skips
        MosaicSet.Add(r);
        h.Rt.ApplyNow();
        var mats = r.sharedMaterials;
        var before = mats.Where(x => x && x.HasProperty(prop)).Select(x => x.GetColor(prop)).ToList();
        h.Ok(new { cmd = "redo" }, "MO4 redo");
        h.Rt.ApplyNow();
        var after = r.sharedMaterials.Where(x => x && x.HasProperty(prop)).Select(x => x.GetColor(prop)).ToList();
        Check("MO4 the array holds the item", cloth.List(Stored(h, key)).Count == 1, "");
        Check("MO4 nothing is written to the member", before.SequenceEqual(after) && !after.Contains(written), string.Join(" ", after));
        bool shared = MaterialEditorApplier.MaterialsNamed(Ctx(h, key), cloth.Object, mat).Count > 0;
        var st = MaterialEditorApplier.Status(Ctx(h, key), cloth.Object, cloth.List(Stored(h, key))[0]);
        Check("MO4 the status says so" + (shared ? " (the name is on other renderers too, which the item reaches)" : ""), st.Status == (shared ? MaterialStatus.Applied : MaterialStatus.MosaicProtected), st.Status);
    }

    static void Mo5(McHost h)
    {
        string key = Scene(h);
        var ctx = Ctx(h, key);
        int ck = Enumerable.Range(0, 8).First(k => ctx.Clothes[k] && MaterialEditorApplier.ObjectRenderers(ctx, (OT.Clothing, k)).Count(r => !MosaicSet.IsMember(r)) >= 1);
        var cloth = ClothesTarget(ck);
        var rends = MaterialEditorApplier.ObjectRenderers(ctx, cloth.Object).Where(r => !MosaicSet.IsMember(r)).ToList();
        var added = rends[0];
        string rn = MaterialEditorApplier.Fmt(added.name);
        var names = added.sharedMaterials.Where(m => m).Select(m => MaterialEditorApplier.Fmt(m.name)).ToList();
        Check("MO5 the set takes the renderer", MosaicSet.Add(added), "");
        h.Rt.ApplyNow();
        Check("MO5 it is a member now", MaterialGuard.Renderer(added), "");
        ctx = Ctx(h, key);
        var st = MaterialEditorApplier.Status(ctx, cloth.Object, new MeRenderer { Renderer = rn, Property = "Enabled", Value = 0 });
        Check("MO5 Enabled on it is refused", st.Status == MaterialStatus.MosaicProtected, st.Status);
        foreach (var n in names)
        {
            bool others = rends.Skip(1).Any(r => r.sharedMaterials.Any(m => m && MaterialEditorApplier.Fmt(m.name) == n));
            var s2 = MaterialEditorApplier.Status(ctx, cloth.Object, new MeFloat { Material = n, Property = "Gloss", Value = 0.1f });
            if (!others) Check($"MO5 a material only it holds: {n}", s2.Status == MaterialStatus.MosaicProtected, s2.Status);
        }
        var other = rends.Skip(1).FirstOrDefault();
        if (other == null) { Info("MO5 the part has one renderer: no other slot to look at"); return; }
        var om = other.sharedMaterials.FirstOrDefault(m => m && FirstColor(m) != null);
        if (!om) return;
        var s3 = MaterialEditorApplier.Status(ctx, cloth.Object, new MeColor { Material = MaterialEditorApplier.Fmt(om.name), Property = FirstColor(om).Substring(1), Value = new Color4(1, 1, 0, 1) });
        Check("MO5 the part's other renderer stays editable", s3.Status == MaterialStatus.Applied, s3.Status);
    }

    /// <summary>A renderer of an item joins the set (no row of this install has a mosaic slot): the commands refuse it, a hand-edited array is
    /// kept and not applied, the item's other renderers stay editable.</summary>
    static void MoItem(McHost h)
    {
        h.Ok(new { cmd = "scene.new" }, "MO3 scene.new");
        string id = null;
        foreach (var no in new[] { 89, 590, 975 })
        {
            string k = AddItem(h, no);
            if (ItemOf(h, k).MaterialEdits.Target.Renderers.Count >= 2) { id = k; break; }
            h.Ok(new { cmd = "undo" }, "MO3 take the item back");
            h.Rt.ApplyNow();
        }
        if (id == null) { id = AddItem(h, 89); Info("MO3 no row with two renderers: the other renderer's check is left out"); }
        var t = ItemOf(h, id).MaterialEdits.Target;
        var member = t.Renderers[0];
        var other = t.Renderers.Skip(1).FirstOrDefault(r => r && r.sharedMaterials.Any(m => m));
        string path = t.PathOf(member), mat = MaterialEditorApplier.Fmt(member.sharedMaterials[0].name);
        string otherMat = other ? MaterialEditorApplier.Fmt(other.sharedMaterials.First(m => m).name) : null;
        bool shared = otherMat == mat;
        Check("MO5 the set takes the item's renderer", MosaicSet.Add(member), path);
        h.Rt.ApplyNow();
        var slots = member.sharedMaterials.ToArray();
        int n0 = History(h);
        var a = h.Call(new JObject { ["cmd"] = "item.materials.set", ["key"] = id, ["type"] = "renderer", ["renderer"] = path, ["property"] = "Enabled", ["value"] = 0 }.ToString());
        Check("MO5 the command refuses Enabled", a.Value<bool?>("ok") == false && a.Value<string>("code") == "unsupported" && a.ToString().Contains(MosaicSet.MeProtectedCode), Short(a.ToString()));
        var b = h.Call(new JObject { ["cmd"] = "item.materials.set", ["key"] = id, ["type"] = "shader", ["material"] = other ? otherMat : mat, ["shader"] = MosaicSet.ShaderName }.ToString());
        Check("MO5 the command refuses a mosaic shader", b.Value<bool?>("ok") == false, Short(b.ToString()));
        Check("MO5 no entry", History(h) == n0, "");
        // (a value item first: a shader name rebuilds the item, and a rebuilt renderer is a new one, outside the set)
        SetItemArray(h, id, new List<MaterialOverride> { new MeRenderer { Renderer = path, Property = "Enabled", Value = 0 } });
        h.Rt.ApplyNow();
        var state = ItemState(h, id);
        Check("MO3 item: kept", state.MaterialOverrides.Count == 1, state.MaterialOverrides.Count + " items");
        Check("MO3 item: the member stays drawn with its materials", member && member.enabled && member.sharedMaterials.SequenceEqual(slots), "");
        var t2 = ItemOf(h, id).MaterialEdits.Target;
        Check("MO3 item: status of the renderer item", t2.Status(state.MaterialOverrides[0]).Status == MaterialStatus.MosaicProtected, t2.Status(state.MaterialOverrides[0]).Status);
        Check("MO3 item: the finding", h.Rt.Findings.Any(f => f.Code == MosaicSet.MeProtectedCode && f.NodeId == id), string.Join(", ", h.Rt.Findings.Select(f => f.Code).Distinct()));
        var set = h.Call(new JObject { ["cmd"] = "item.set", ["key"] = id, ["path"] = "item.materialOverrides", ["value"] = new JArray() }.ToString());
        Check("MO3 item.set under the array is refused", set.Value<bool?>("ok") == false && set.Value<string>("code") == "bad_args", Short(set.ToString()));
        var swap = new MeShader { Material = other ? otherMat : mat, Shader = MosaicSet.OriginalShader };
        Check("MO3 item: a swap to a mosaic shader reads protected", t2.Status(swap).Status == MaterialStatus.MosaicProtected, t2.Status(swap).Status);
        if (other && !shared)
        {
            var c = h.Call(new JObject { ["cmd"] = "item.materials.set", ["key"] = id, ["type"] = "renderer", ["renderer"] = t2.PathOf(other), ["property"] = "Enabled", ["value"] = 0 }.ToString());
            Check("MO5 the item's other renderer stays editable", c.Value<bool?>("ok") == true && !other.enabled, Short(c.ToString()));
        }
        h.Ok(new { cmd = "scene.new" }, "MO3 scene.new");
    }

    /// <summary>A target of the writer over plain renderers, as an item's or a map's: the shape the rule meets outside a character.</summary>
    sealed class PlainTarget : MaterialWriteTarget
    {
        public readonly List<Renderer> Rends = new List<Renderer>();
        public override void Select(string material, List<Material> into, bool forWrite, out bool heldByMember, out string note)
        {
            heldByMember = false; note = null;
            foreach (var r in Rends)
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || MaterialEditorApplier.Fmt(m.name) != material) continue;
                    if (MaterialGuard.Renderer(r)) heldByMember = true; else if (!into.Contains(m)) into.Add(m);
                }
        }
        public override void SelectRenderers(string renderer, List<Renderer> into) => into.AddRange(Rends.Where(r => r.name == renderer));
        public override Texture2D Image(string res, bool linear) => null;
        public override bool ImageReadable(string res) => false;
    }

    /// <summary>The writer's side of MO5, on a synthetic prefab: a renderer with a mosaic slot, a renderer added to the set, a plain one.</summary>
    static void Mo5Writer()
    {
        var plainShader = Shader.Find("HS2Next/AIT Clothes True");
        var mosaic = Shader.Find(MosaicSet.ShaderName);
        if (!Check("MO5 the two shaders exist", plainShader && mosaic, "")) return;
        var objects = new List<UnityEngine.Object>();
        Renderer New(string name, params Material[] mats)
        {
            var go = new GameObject(name);
            objects.Add(go);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            return r;
        }
        Material Mat(string name, Shader sh)
        {
            var m = new Material(sh) { name = name };
            objects.Add(m);
            return m;
        }
        try
        {
            var a = Mat("slot_a", plainShader);
            var withSlot = New("with_mosaic_slot", a, Mat("slot_m", mosaic));
            var b = Mat("plain_b", plainShader);
            var plain = New("plain", b);
            var c = Mat("added_c", plainShader);
            var added = New("added", c);
            var t = new PlainTarget { Baseline = new MaterialBaseline() };
            t.Rends.AddRange(new[] { withSlot, plain, added });
            Check("MO5 a mosaic slot makes its renderer a member", MaterialGuard.Renderer(withSlot), "");
            MosaicSet.Add(added);
            Check("MO5 the added renderer is a member", MaterialGuard.Renderer(added), "");
            float was = a.GetFloat("_alpha_a"), wasB = b.GetFloat("_alpha_a");
            foreach (var (name, what) in new[] { ("slot_a", "the other slot of the member"), ("slot_m", "the mosaic slot"), ("added_c", "the added renderer's slot") })
            {
                var item = new MeFloat { Material = name, Property = "alpha_a", Value = 0.25f };
                Check($"MO5 {what}: status", MaterialWrites.StatusOf(item, t).Status == MaterialStatus.MosaicProtected, MaterialWrites.StatusOf(item, t).Status);
                Check($"MO5 {what}: refused", MaterialWrites.One(item, t) == MaterialStatus.MosaicProtected, "");
                var sw = new MeShader { Material = name, Shader = "HS2Next/AIT Clothes Alpha True" };
                Check($"MO5 {what}: a shader item refused", MaterialWrites.One(sw, t) == MaterialStatus.MosaicProtected, "");
            }
            Check("MO5 the member's slot is untouched", a.GetFloat("_alpha_a") == was && a.shader == plainShader, "");
            foreach (var r in new[] { withSlot, added })
                Check($"MO5 Enabled on {r.name}", MaterialWrites.One(new MeRenderer { Renderer = r.name, Property = "Enabled", Value = 0 }, t) == MaterialStatus.MosaicProtected && r.enabled, "");
            foreach (var n in new[] { MosaicSet.OriginalShader, MosaicSet.ShaderName, MosaicSet.LegacyShader })
                Check($"MO5 a swap of a plain material to {n}", MaterialWrites.One(new MeShader { Material = "plain_b", Shader = n }, t) == MaterialStatus.MosaicProtected && b.shader == plainShader, "");
            Check("MO5 the plain renderer takes the edit", MaterialWrites.One(new MeFloat { Material = "plain_b", Property = "alpha_a", Value = 0.25f }, t) == null && b.GetFloat("_alpha_a") == 0.25f, wasB + " -> " + b.GetFloat("_alpha_a"));
            Check("MO5 and takes Enabled", MaterialWrites.One(new MeRenderer { Renderer = "plain", Property = "Enabled", Value = 0 }, t) == null && !plain.enabled, "");
            t.Baseline.Begin();
            t.Baseline.Sweep(MaterialMember.All);
            Check("MO5 the sweep gives the plain renderer back", b.GetFloat("_alpha_a") == wasB && plain.enabled, "");
        }
        finally
        {
            foreach (var o in objects) if (o) UnityEngine.Object.DestroyImmediate(o);
            MosaicSet.Standalone();
        }
    }

    static void Mo8(McHost h)
    {
        string key = Scene(h);
        var ctx = Ctx(h, key);
        var ch = CharacterTarget();
        string skin = MaterialEditorApplier.Fmt(ctx.BodyDraw.name);
        string image = TestImage(h, new Color(0.8f, 0.6f, 0.5f, 1f));
        bool Holding() => MosaicSet.StateOf(Ctx(h, key)).Members.All(m => m.Renderer.sharedMaterials.All(MosaicUnit.IsUnitMaterial));
        // MainTex: the patches read the new texture in the same flush
        Edit(h, key, ch, MaterialEdit.Texture(skin, "MainTex").WithImage(image));
        var draw = Ctx(h, key).BodyDraw.GetTexture("_MainTex");
        Check("MO8 MainTex is allowed and lands", draw && draw.width == 8, draw ? draw.width + "x" + draw.height : "none");
        Check("MO8 the members hold their mosaic materials", Holding(), "");
        var src = MosaicSet.StateOf(Ctx(h, key)).Members.Where(m => m.SourceKind == "body").ToList();
        Check("MO8 a body-surface member exists", src.Count > 0, "");
        Check("MO8 after the value pass the member's source is the new texture", src.All(m => m.Source == draw), string.Join(", ", src.Select(m => m.Source ? m.Source.name : "none")));
        SetArray(h, key, ch, MaterialEdits.Remove(ch.List(Stored(h, key)), MeKey.Property("texture", skin, "MainTex")));
        var back = Ctx(h, key).BodyDraw.GetTexture("_MainTex");
        Check("MO8 removal brings the composite back", back is RenderTexture, back ? back.GetType().Name : "none");
        Check("MO8 and the members read it", MosaicSet.StateOf(Ctx(h, key)).Members.Where(m => m.SourceKind == "body").All(m => m.Source == back), "");
        // AlphaMask
        if (Ctx(h, key).BodyDraw.HasProperty("_AlphaMask"))
        {
            Edit(h, key, ch, MaterialEdit.Texture(skin, "AlphaMask").WithImage(image));
            Check("MO8 AlphaMask is allowed", Ctx(h, key).BodyDraw.GetTexture("_AlphaMask")?.width == 8, "");
            Check("MO8 the members hold their mosaic materials", Holding(), "");
            SetArray(h, key, ch, MaterialEdits.Remove(ch.List(Stored(h, key)), MeKey.Property("texture", skin, "AlphaMask")));
        }
        else Info("MO8 the skin has no _AlphaMask on this shader");
        // a shader swap of the skin rebuilds, and the unit runs in that build
        string cand = MaterialShaders.All.First(c => c.Name != Ctx(h, key).BodyDraw.shader.name && c.Name.Contains("AIT")).Name;
        Edit(h, key, ch, MaterialEdit.ShaderItem(skin).WithShader(cand, Ctx(h, key).BodyDraw.shader.name));
        Check("MO8 a shader swap on the skin is allowed", Ctx(h, key).BodyDraw.shader.name == cand, Ctx(h, key).BodyDraw.shader.name);
        Check("MO8 the members hold their mosaic materials", Holding(), "");
        SetArray(h, key, ch, MaterialEdits.Remove(ch.List(Stored(h, key)), MeKey.Shader(skin)));
        // Enabled 0 on the body
        var body = Ctx(h, key).BodyRenderer;
        string bn = MaterialEditorApplier.Fmt(body.name);
        Edit(h, key, ch, MaterialEdit.RendererEnabled(bn, 0, 1));
        Check("MO8 Enabled 0 on the body is allowed", !Ctx(h, key).BodyRenderer.enabled, "");
        Check("MO8 the members stay members and stay drawn", MosaicSet.StateOf(Ctx(h, key)).Members.All(m => m.Renderer.enabled || m.Renderer.name.StartsWith("cm_")) && Holding(), "");
        SetArray(h, key, ch, MaterialEdits.Remove(ch.List(Stored(h, key)), MeKey.Enabled(bn)));
    }
}
