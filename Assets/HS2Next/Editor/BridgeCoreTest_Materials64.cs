// C43: the material commands (Phase 6.4 package C) in an edit-mode test host on the clothed SFW
// female target (BR-Chan), a vanilla item and a character without a card. MC1 every command's arguments, replies and errors, one history
// entry with its label and undo, redo and `undo: false`, `merge`, a batch that stores all or nothing, `strict`, wrong_kind, a target that
// is not built, read-only types, a removal that needs no target, a card-backed character imported by its first edit and left alone by a
// no-op; a new item on a property only the mapping's replacement declares; the mosaic refusal with its warning, leaving no entry and no
// change; item.set under the array; MC2 the registration:
// Phase64Names, group, Since, flags, the convention; MC3 Tools/api/commands.surface.json against the registry. What the values do to the
// materials is MaterialEditTest's; the mosaic rule on its fixtures is MosaicTest's. A map loads only in play mode, so here the map
// commands answer for no map; the loaded map's rows are MaterialEditTest.play's.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

public static partial class BridgeCoreTest
{
    static readonly string[] C43Reads = { "materials.shaders", "chara.edit.materials.items", "item.materials.items", "map.materials", "map.materials.items", "chara.edit.boneMods" };
    static readonly string[] C43MapCommands = { "materials.shaders", "map.materials", "map.materials.items", "map.materials.set", "map.materials.clear" };
    const string C43Nope = "0123456789abcdef0123456789abcdef";
    const string C43MosaicText = "mosaic parts are not editable";

    /// <summary>What the checks edit on the character: a material of a clothes part with one property of each kind.</summary>
    sealed class C43Fix
    {
        public string Key, Target, Material, Float, Color, Texture, Keyword, Renderer;
        public Material Live;
        public float FloatValue;
    }

    static void C43(Ctx c)
    {
        C43Registration(c);
        C43Surface(c);
        var app = c.Host();
        string f = W1LoadFemale(c);
        string item = W1AddItem(c);
        if (!c.Check(f != null && item != null, "MC1: the character and the item")) return;
        var nb = app.Session.AddChara(null);
        if (!c.Check(nb != null && nb.Ok, "MC1: a character without a card (not built)", nb?.ToString())) return;
        app.Runtime.ApplyNow();
        string none = nb.NewId;
        var x = C43Pick(c, app, f);
        if (!c.Check(x != null, "MC1: a clothes material with a float, a colour and a texture")) return;
        C43Common(c, f, item, none, x);
        C43Shaders(c);
        C43NoOpLeavesTheCard(c, app);
        C43CharaSet(c, app, x);
        C43Errors(c, app, x);
        C43Clear(c, app, x);
        C43Reads_(c, app, x);
        C43Reach(c, app, f);
        C43Mosaic(c, app, x);
        C43Item(c, app, item);
        C43BoneMods(c, app, f);
        C43Map(c);
        c.Ok(new { cmd = "delete", keys = new[] { none } }, "delete the character without a card");
    }

    // ---------------------------------------------------------------- MC2

    static void C43Registration(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = CoreCommands.Phase64Names.ToList();
        var want = new[]
        {
            "materials.shaders", "chara.edit.materials.items", "chara.edit.materials.set", "chara.edit.materials.clear",
            "item.materials.items", "item.materials.set", "item.materials.clear",
            "map.materials", "map.materials.items", "map.materials.set", "map.materials.clear",
            "chara.edit.boneMods", "chara.edit.boneMods.set", "chara.edit.boneMods.clear",
        };
        c.Check(names.Count == 14 && names.SequenceEqual(want), "MC2: Phase64Names = the 14 commands", string.Join(", ", names));
        c.Check(names.All(CoreCommands.AllNames.Contains), "MC2: all in CoreCommands.AllNames");
        c.Check(CoreCommands.Groups.Contains("materials"), "MC2: the group \"materials\" is in CoreCommands.Groups", string.Join(", ", CoreCommands.Groups));
        c.Check(CkGroups.Contains("materials"), "MC2: materials is a convention group (CK5)");
        var bad = new List<string>();
        foreach (var n in names)
        {
            var s = reg.Find(n);
            if (s == null) { bad.Add(n + ": not registered"); continue; }
            bool read = C43Reads.Contains(n);
            if (s.Owner != BridgeOwners.Core) bad.Add(n + ": owner " + s.Owner);
            if (s.Group != "materials") bad.Add(n + ": group " + s.Group);
            if (s.Since != "1.2.0") bad.Add(n + ": since " + s.Since);
            if (s.Gate != ReadyGate.App) bad.Add(n + ": gate " + s.Gate);
            if ((s.Flags & CommandFlags.Experimental) == 0) bad.Add(n + ": not experimental");
            if (read != ((s.Flags & CommandFlags.ReadOnly) != 0)) bad.Add(n + ": readOnly flag");
            if (read == ((s.Flags & CommandFlags.Mutates) != 0)) bad.Add(n + ": mutates flag");
            if (s.Examples.Count == 0) bad.Add(n + ": no example");
            if (s.Returns.Count == 0) bad.Add(n + ": no returned field");
            if (!C43MapCommands.Contains(n) && s.Args.All(a => a.Name != "key" || !a.Required)) bad.Add(n + ": key is not required");
            if (s.Returns.Any(r => r.Name == "error")) bad.Add(n + ": a returned field named error");
            string help = s.Summary + string.Concat(s.Args.Select(a => a.Doc)) + string.Concat(s.Returns.Select(r => r.Doc));
            if (help.Contains("\u00a7")) bad.Add(n + ": a section mark in the help");
            var h = c.Call(new { cmd = "help", name = n });
            if (h.Value<bool?>("ok") != true || (h["command"]?["flags"] as JArray)?.Any(x => (string)x == "experimental") != true) bad.Add(n + ": help");
        }
        c.Check(bad.Count == 0, "MC2: each has owner core, the group, Since 1.2.0, gate app, experimental, readOnly or mutates, an example, returned fields, a required key, no member error, help without a section mark", string.Join("; ", bad));
        var im = reg.Find("item.materials");
        var allProps = im?.Args.FirstOrDefault(a => a.Name == "allProps");
        c.Check(allProps != null && !allProps.Required && allProps.Type == ArgTypes.Bool && (im.Flags & CommandFlags.Experimental) == 0 && im.Since == "1.0.0", "MC2: item.materials stays frozen and gains the optional allProps");
        c.Dispose(app);
    }

    // ---------------------------------------------------------------- MC3

    static void C43Surface(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var specs = reg.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).ToList();
        var experimental = specs.Where(s => (s.Flags & CommandFlags.Experimental) != 0).Select(s => s.Name).OrderBy(s => s, StringComparer.Ordinal).ToList();
        c.Check(specs.Count == 202, "MC3: 202 commands", specs.Count + "");
        c.Check(specs.Count(s => s.Owner == BridgeOwners.Core) == 197 && specs.Count(s => s.Owner == BridgeOwners.Bridge) == 5, "MC3: 197 core, 5 bridge");
        c.Check(experimental.Count == 44, "MC3: 44 experimental", experimental.Count + "");
        c.Check(CoreCommands.ParityNames.Count == 89, "MC3: 89 parity");
        var listing = JObject.Parse(File.ReadAllText(Path.Combine(ApiSurfaceTest.ListingDir, ApiSurfaceTest.CommandsFile)));
        var frozen = (listing["commands"] as JArray).OfType<JObject>().Select(x => (string)x["name"]).ToList();
        var live = specs.Where(s => (s.Flags & CommandFlags.Experimental) == 0).Select(s => s.Name).ToList();
        c.Check(frozen.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(live.OrderBy(s => s, StringComparer.Ordinal)), "MC3: the listing's frozen commands are the registry's: none added, removed or moved", frozen.Count + " / " + live.Count);
        var old = (listing["experimental"] as JArray).Select(x => x is JObject o ? (string)o["name"] : (string)x).ToList();
        c.Check(old.All(experimental.Contains) && (old.Count == 37 || old.Count == 44), "MC3: the listing's experimental commands are in the registry (37 before the integrator's rewrite, 44 after)", old.Count + "");
        c.Check(experimental.Except(old).All(CoreCommands.Phase65Names.Contains), "MC3: what the listing lacks is the 7 of Phase 6.5", string.Join(", ", experimental.Except(old).Take(16)));
        var entry = (listing["commands"] as JArray).OfType<JObject>().First(x => (string)x["name"] == "item.materials");
        var args = (entry["args"] as JArray).OfType<JObject>().Select(x => (string)x["name"]).ToList();
        var now = reg.Find("item.materials").Args.Select(a => a.Name).ToList();
        c.Check(now.Except(args).All(a => a == "allProps") && args.All(now.Contains), "MC3: item.materials differs from the listing by allProps at most", string.Join(", ", args));
        c.Dispose(app);
    }

    // ---------------------------------------------------------------- fixtures

    static C43Fix C43Pick(Ctx c, AppCore app, string f)
    {
        var ctx = app.Runtime.FindChara(f).Context;
        for (int k = 0; k < ctx.Clothes.Length; k++)
        {
            var go = ctx.Clothes[k];
            if (!go) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (MosaicSet.IsMember(r)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.shader || !m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal)) continue;
                    string mat = MaterialEditorApplier.Fmt(m.name);
                    bool Reach(MaterialOverride item) => MaterialEditorApplier.Status(ctx, (OT.Clothing, k), item).Applied;
                    string fl = C43Prop(m, ShaderPropertyType.Float, ShaderPropertyType.Range, p => Reach(new MeFloat { Material = mat, Property = p, Value = 0.5f })),
                        co = C43Prop(m, ShaderPropertyType.Color, ShaderPropertyType.Color, p => Reach(new MeColor { Material = mat, Property = p })),
                        te = C43Prop(m, ShaderPropertyType.Texture, ShaderPropertyType.Texture, p => Reach(new MeTexture { Material = mat, Property = p, Offset = new Vec2(0, 0) }));
                    if (fl == null || co == null || te == null) continue;
                    string kw = null;
                    foreach (var w in m.shader.keywordSpace.keywords)
                        if (!w.isOverridable && w.name.StartsWith("_", StringComparison.Ordinal) && w.name.EndsWith("_ON", StringComparison.Ordinal)) { kw = w.name.Substring(1); break; }
                    return new C43Fix
                    {
                        Key = f, Target = "clothes:" + k, Material = MaterialEditorApplier.Fmt(m.name), Float = fl.Substring(1), Color = co.Substring(1), Texture = te.Substring(1), Keyword = kw,
                        Renderer = MaterialEditorApplier.Fmt(r.name), Live = m, FloatValue = m.GetFloat(fl),
                    };
                }
            }
        }
        return null;
    }

    // the first property of those types an item reaches on this material
    static string C43Prop(Material m, ShaderPropertyType a, ShaderPropertyType b, Func<string, bool> reached)
    {
        var sh = m.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            var t = sh.GetPropertyType(i);
            if ((t != a && t != b) || (sh.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
            if (t == ShaderPropertyType.Texture && sh.GetPropertyTextureDimension(i) != TextureDimension.Tex2D) continue;
            string n = sh.GetPropertyName(i);
            if (n.StartsWith("_", StringComparison.Ordinal) && n != "_Cutoff" && n != "_Surface" && n != "_ZWrite" && n != "_Cull" && reached(n.Substring(1))) return n;
        }
        return null;
    }

    // the request of a set on the fixture's array, with its members
    static JObject C43Set(C43Fix x, object members)
    {
        var o = JObject.FromObject(members);
        o["cmd"] = "chara.edit.materials.set";
        o["key"] = x.Key;
        o["target"] = x.Target;
        return o;
    }

    static string C43Text(JObject o) => o.ToString(Formatting.None);

    static JObject C43Float(C43Fix x, float value, bool merge = false)
    {
        var o = C43Set(x, new { type = "float", material = x.Material, property = x.Float, value });
        if (merge) o["merge"] = true;
        return o;
    }

    static List<MaterialOverride> C43Array(AppCore app, C43Fix x)
    {
        MaterialEditTarget.TryParse(x.Key, x.Target, out var t, out _);
        return app.Ops.MaterialArray(t);
    }

    static string C43Json(AppCore app, C43Fix x) => HS2Next.Chara.Edit.Units.Signature.Of(C43Array(app, x));

    // ---------------------------------------------------------------- every command

    static void C43Common(Ctx c, string f, string item, string none, C43Fix x)
    {
        foreach (var n in CoreCommands.Phase64Names)
        {
            if (C43MapCommands.Contains(n)) continue;
            bool onItem = n.StartsWith("item.", StringComparison.Ordinal);
            string own = onItem ? item : f, other = onItem ? f : item;
            string text(string key, string more = "") => "{\"cmd\":\"" + n + "\",\"key\":\"" + key + "\"" + more + "}";
            string req = C43Required(n, x);
            c.Fails("{\"cmd\":\"" + n + "\"}", "bad_args", n + ": no key -> bad_args");
            c.Fails(text(other, req), "wrong_kind", n + ": the key of another kind -> wrong_kind");
            c.Fails(text(C43Nope, req), "not_found", n + ": an unknown key -> not_found");
            c.Fails("{\"cmd\":\"" + n + "\",\"key\":5}", "bad_args", n + ": a number as the key -> bad_args");
            var strict = c.Call(text(own, req + ",\"nope\":1,\"strict\":true"));
            c.Check(strict.Value<string>("code") == "bad_args" && (string)strict["detail"]?["arg"] == "nope", n + ": strict: an unknown argument -> bad_args naming it", Ctx.Short(c.LastText));
            if (!onItem) c.Fails(text(none, req), "unavailable", n + ": a character that is not built -> unavailable");
        }
        var w = c.Ok("{\"cmd\":\"chara.edit.materials.items\",\"key\":\"" + f + "\",\"target\":\"character\",\"nope\":1}", "an unknown argument without strict");
        c.Check((w["warnings"] as JArray)?.Any(y => ((string)y).Contains("nope")) == true, "... a warning names it", Ctx.Short(c.LastText));
    }

    // the required arguments besides key, so that the key is what a request fails on
    static string C43Required(string n, C43Fix x)
    {
        string edit = ",\"type\":\"float\",\"material\":\"" + x.Material + "\",\"property\":\"" + x.Float + "\",\"value\":0.5";
        switch (n)
        {
            case "chara.edit.materials.set": return ",\"target\":\"" + x.Target + "\"" + edit;
            case "chara.edit.materials.clear": return ",\"target\":\"" + x.Target + "\",\"all\":true";
            case "item.materials.set": return edit;
            case "item.materials.clear": case "chara.edit.boneMods.clear": return ",\"all\":true";
            case "chara.edit.boneMods.set": return ",\"bone\":\"N_hair_Root\",\"length\":1.1";
            default: return "";
        }
    }

    static void C43Err(Ctx c, object req, string code, string arg, string what)
    {
        string before = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Call(req);
        c.Check(r.Value<bool?>("ok") == false && r.Value<string>("code") == code && (arg == null || (string)r["detail"]?["arg"] == arg), what + " -> " + code + (arg != null ? " naming " + arg : ""), Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == n0 && W1DocSha(c) == before, what + ": no entry, no change");
    }

    // ---------------------------------------------------------------- materials.shaders

    static void C43Shaders(Ctx c)
    {
        var r = c.Ok(new { cmd = "materials.shaders" }, "materials.shaders");
        var rows = (r["shaders"] as JArray).OfType<JObject>().ToList();
        c.Check(rows.Count > 40 && (int)r["count"] == rows.Count && (long)r["resBytes"] >= 0, "materials.shaders: the list, count, resBytes", rows.Count + "");
        var first = rows[0];
        c.Check(first.Properties().Select(p => p.Name).SequenceEqual(new[] { "name", "family", "label", "queue", "keywords" }), "materials.shaders: a row's members", C43Text(first));
        c.Check(rows.All(x => ((string)x["name"]).StartsWith("HS2Next/", StringComparison.Ordinal) && !MosaicSet.IsMosaicShaderName((string)x["name"]) && (string)x["label"] == ((string)x["name"]).Substring(8)), "materials.shaders: every name under HS2Next/, no mosaic shader, the label without the prefix");
        c.Check(rows.All(x => !((string)x["name"]).Contains("/Hidden/") && !((string)x["name"]).StartsWith("HS2Next/UI/", StringComparison.Ordinal)), "materials.shaders: no hidden or UI shader");
        c.Check(!rows.Any(x => (string)x["name"] == ShaderMap.WaterShader), "materials.shaders: no shader that needs a hook");
        var fil = c.Ok(new { cmd = "materials.shaders", filter = "clothes" }, "materials.shaders filter");
        c.Check((fil["shaders"] as JArray).Count > 0 && (fil["shaders"] as JArray).Count < rows.Count && (fil["shaders"] as JArray).OfType<JObject>().All(x => ((string)x["name"]).IndexOf("clothes", StringComparison.OrdinalIgnoreCase) >= 0), "materials.shaders filter: by name, ignoring case", Ctx.Short(c.LastText));
        C43Err(c, new { cmd = "materials.shaders", filter = 1 }, "bad_args", "filter", "materials.shaders a number as the filter");
        var strict = c.Call(new { cmd = "materials.shaders", nope = 1, strict = true });
        c.Check(strict.Value<string>("code") == "bad_args" && (string)strict["detail"]?["arg"] == "nope", "materials.shaders: strict", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- a card-backed character

    static void C43NoOpLeavesTheCard(Ctx c, AppCore app)
    {
        var add = app.Session.AddChara(new CharaAppearance { Card = app.Services.CardSources.FromFile(c.FemaleCard) });
        if (!c.Check(add != null && add.Ok, "a character that only holds its card", add?.ToString())) return;
        app.Runtime.ApplyNow();
        string key = add.NewId;
        bool Card() => app.Session.Model.Find(key).Appearance?.Stored == null;
        var x = C43Pick(c, app, key);
        if (!c.Check(Card() && x != null, "it is built and card-backed")) return;
        int n0 = W1UndoCount(c);
        var same = c.Ok(C43Text(C43Float(x, x.FloatValue)), "set a value equal to the material's, no item");
        c.Check((same["changed"] as JArray)?.Count == 0 && (same["removed"] as JArray)?.Count == 0 && (same["items"] as JArray)?.Count == 0 && W1UndoCount(c) == n0, "... nothing changed, no entry", Ctx.Short(c.LastText));
        c.Check(Card(), "... and the character stays card-backed");
        var refused = c.Call(C43Text(C43Set(x, new { type = "float", material = "no_such_material", property = x.Float, value = 1 })));
        c.Check(refused.Value<string>("code") == "not_found" && Card(), "a refusal leaves it card-backed too", Ctx.Short(c.LastText));
        var nothing = c.Ok(new { cmd = "chara.edit.materials.clear", key, target = x.Target, all = true }, "a clear with nothing to clear");
        c.Check((int?)nothing["removed"] == 0 && Card() && W1UndoCount(c) == n0, "... changes nothing either", Ctx.Short(c.LastText));
        var mods = c.Ok(new { cmd = "chara.edit.boneMods", key }, "the bone modifiers of a card-backed character");
        c.Check(mods["mods"] is JArray && (mods["bones"] as JArray)?.Count > 0, "... read from what it was built from", Ctx.Short(c.LastText));
        var items = c.Ok(new { cmd = "chara.edit.materials.items", key }, "items of a card-backed character");
        c.Check((items["targets"] as JArray).Count >= 13, "... every array: the character, four hair parts, eight clothes kinds, the accessories", (items["targets"] as JArray).Count + "");
        float to = x.FloatValue > 0.5f ? x.FloatValue - 0.25f : x.FloatValue + 0.25f;
        c.Ok(C43Text(C43Float(x, to)), "the first edit");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Material edit" && !Card(), "... imports the character in the same entry", W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "undo the first edit");
        c.Check(Card(), "undo puts the card back");
        c.Ok(new { cmd = "redo" }, "redo");
        c.Check(C43Array(app, x).Count == 1, "redo the import and the item", C43Json(app, x));
        c.Ok(new { cmd = "delete", keys = new[] { key } }, "delete the character");
    }

    // the material after a rebuild or an import
    static Material C43Live(AppCore app, C43Fix x)
    {
        MaterialEditTarget.TryParse(x.Key, x.Target, out var t, out _);
        return C43Live(app, x, t);
    }

    static Material C43Live(AppCore app, C43Fix x, MaterialEditTarget t)
    {
        var ctx = app.Runtime.FindChara(x.Key).Context;
        var obj = t.Kind == MaterialTargetKind.Clothes ? (OT.Clothing, t.Slot) : t.Kind == MaterialTargetKind.Hair ? (OT.Hair, t.Slot) : (OT.Character, 0);
        foreach (var r in MaterialEditorApplier.ObjectRenderers(ctx, obj))
            foreach (var m in r.sharedMaterials)
                if (m && MaterialEditorApplier.Fmt(m.name) == x.Material) return m;
        return null;
    }

    // ---------------------------------------------------------------- set

    static void C43CharaSet(Ctx c, AppCore app, C43Fix x)
    {
        float baseline = x.FloatValue, a = baseline > 0.5f ? baseline - 0.25f : baseline + 0.25f, b = baseline > 0.5f ? baseline - 0.125f : baseline + 0.125f;
        var one = W1History1(c, C43Text(C43Float(x, a)), "Material edit", "set a float", () => C43Text(C43Float(x, b)).Replace("\"cmd\"", "\"undo\":false,\"cmd\""));
        c.Check((string)one["target"] == x.Target && (one["changed"] as JArray)?.Single().ToString() == "float/" + x.Material + "/" + x.Float && (one["removed"] as JArray)?.Count == 0, "set: {target, changed, removed}", Ctx.Short(one.ToString(Formatting.None)));
        var row = (one["items"] as JArray)?.OfType<JObject>().SingleOrDefault();
        c.Check(row != null && row.Properties().Select(p => p.Name).Take(6).SequenceEqual(new[] { "index", "type", "key", "editable", "status", "why" }), "set: an item's first members", row?.ToString(Formatting.None));
        c.Check(row != null && (string)row["status"] == "applied" && row["why"].Type == JTokenType.Null && (bool)row["editable"] && (float)row["value"] == a, "set: the item applied, with its value", row?.ToString(Formatting.None));
        c.Check(row != null && (float)row["original"] == baseline, "set: a new item's original is what the material held", row?.ToString(Formatting.None));
        c.Check(row != null && (string)row["key"]?["material"] == x.Material && (string)row["key"]?["property"] == x.Float && (string)row["key"]?["type"] == "float", "set: the item's key", row?.ToString(Formatting.None));
        x.Live = C43Live(app, x);
        c.Check(x.Live && Mathf.Approximately(x.Live.GetFloat("_" + x.Float), b), "the value is on the material at once", x.Live ? x.Live.GetFloat("_" + x.Float) + "" : "no material");

        // the same value: nothing; another value: the item, one entry; the baseline: the item leaves
        int n0 = W1UndoCount(c);
        var same = c.Ok(C43Text(C43Float(x, b)), "set the same value");
        c.Check((same["changed"] as JArray)?.Count == 0 && W1UndoCount(c) == n0, "the same value: nothing changed, no entry", Ctx.Short(c.LastText));
        var back = c.Ok(C43Text(C43Float(x, baseline)), "set the baseline");
        c.Check((back["removed"] as JArray)?.Single().ToString() == "float/" + x.Material + "/" + x.Float && (back["changed"] as JArray)?.Count == 0 && (back["items"] as JArray)?.Count == 0 && W1UndoCount(c) == n0 + 1, "a value equal to the baseline removes the item, in one entry", Ctx.Short(c.LastText));
        c.Check(C43Array(app, x).Count == 0 && x.Live.GetFloat("_" + x.Float) == baseline, "... and the material holds the baseline");

        // merge: one entry for the edits of one item; a removal stands alone
        n0 = W1UndoCount(c);
        c.Ok(C43Text(C43Float(x, a, true)), "merge 1");
        c.Ok(C43Text(C43Float(x, b, true)), "merge 2");
        c.Check(W1UndoCount(c) == n0 + 1, "merge: two edits of one item, one entry", W1UndoCount(c) + "");
        c.Ok(C43Text(C43Float(x, baseline, true)), "merge the baseline");
        c.Check(W1UndoCount(c) == n0 + 2, "merge: a removal stands alone", W1UndoCount(c) + "");
        c.Ok(new { cmd = "undo" }, "undo the removal");
        c.Check(C43Array(app, x).Count == 1, "... the item is back");
        c.Ok(new { cmd = "undo" }, "undo the merged edits");
        c.Check(C43Array(app, x).Count == 0, "... one undo takes both merged edits");

        // the other types
        var color = c.Ok(C43Text(C43Set(x, new { type = "color", material = x.Material, property = x.Color, value = new[] { 0.25, 0.5, 0.75, 1 } })), "set a colour");
        c.Check((color["items"] as JArray)?.OfType<JObject>().Single()["value"]?.Count() == 4, "set a colour: the value", Ctx.Short(c.LastText));
        var tex = c.Ok(C43Text(C43Set(x, new { type = "texture", material = x.Material, property = x.Texture, offset = new[] { 0.5, 0.25 }, scale = new[] { 2, 3 } })), "set a texture's offset and scale");
        var trow = (tex["items"] as JArray)?.OfType<JObject>().Single();
        c.Check(trow?["offset"]?.Count() == 2 && trow["scale"]?.Count() == 2 && trow["offsetOriginal"]?.Count() == 2, "set a texture: offset, scale and the original of each", trow?.ToString(Formatting.None));
        c.Ok(new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, item = new { type = "color", material = x.Material, property = x.Color } }, "the colour leaves");
        var nul = c.Ok(C43Text(C43Set(x, new { type = "texture", material = x.Material, property = x.Texture, offset = (object)null })), "a member given as null");
        trow = (nul["items"] as JArray)?.OfType<JObject>().Single();
        c.Check(trow != null && (trow["offset"] == null || trow["offset"].Type == JTokenType.Null) && trow["scale"]?.Count() == 2, "... clears that member", trow?.ToString(Formatting.None));
        var empty = c.Ok(C43Text(C43Set(x, new { type = "texture", material = x.Material, property = x.Texture, scale = (object)null })), "the last member given as null");
        c.Check((empty["removed"] as JArray)?.Count == 1 && (empty["items"] as JArray)?.Count == 0, "... removes the item", Ctx.Short(c.LastText));
        var shaders = c.Ok(new { cmd = "materials.shaders" }, "materials.shaders")["shaders"] as JArray;
        var queue = c.Ok(C43Text(C43Set(x, new { type = "shader", material = x.Material, renderQueue = 3000 })), "set a queue");
        x.Live = C43Live(app, x);   // a queue is what the URP mapping reads: the part reloaded
        c.Check(x.Live && x.Live.renderQueue == 3000, "the queue is on the material", x.Live ? x.Live.renderQueue + "" : "no material");
        var qrow = (queue["items"] as JArray)?.OfType<JObject>().Single();
        c.Check(qrow != null && (int)qrow["renderQueue"] == 3000 && qrow["renderQueueOriginal"].Type == JTokenType.Integer, "set a queue: the original", qrow?.ToString(Formatting.None));
        c.Ok(C43Text(C43Set(x, new { type = "shader", material = x.Material, renderQueue = (object)null })), "a queue given as null");
        x.Live = C43Live(app, x);
        var en = c.Ok(C43Text(C43Set(x, new { type = "renderer", renderer = x.Renderer, property = "Enabled", value = 0 })), "renderer Enabled 0");
        var rrow = (en["items"] as JArray)?.OfType<JObject>().Single();
        c.Check(rrow != null && (int)rrow["value"] == 0 && (int)rrow["original"] == 1 && (string)rrow["key"]?["renderer"] == x.Renderer, "set Enabled: value, original, key", rrow?.ToString(Formatting.None));
        c.Ok(C43Text(C43Set(x, new { type = "renderer", renderer = x.Renderer, property = "Enabled", value = 1 })), "renderer Enabled back");
        if (x.Keyword != null)
        {
            bool was = x.Live.IsKeywordEnabled("_" + x.Keyword);
            var kw = c.Ok(C43Text(C43Set(x, new { type = "keyword", material = x.Material, property = x.Keyword, value = !was })), "set a keyword");
            c.Check(x.Live.IsKeywordEnabled("_" + x.Keyword) == !was && (bool?)(kw["items"] as JArray)?.OfType<JObject>().Single()["value"] == !was, "the keyword is on the material", Ctx.Short(c.LastText));
            c.Ok(C43Text(C43Set(x, new { type = "keyword", material = x.Material, property = x.Keyword, value = was })), "the keyword back");
        }
        c.Check(C43Array(app, x).Count == 0, "every edit gone again", C43Json(app, x));

        // a batch: one entry, or nothing
        n0 = W1UndoCount(c);
        var batch = C43Set(x, new { });
        batch["items"] = JArray.FromObject(new object[] { new { type = "float", material = x.Material, property = x.Float, value = a }, new { type = "color", material = x.Material, property = x.Color, value = new[] { 0.1, 0.2, 0.3, 1 } } });
        var bi = c.Ok(C43Text(batch), "items: two edits");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Material edit" && (bi["changed"] as JArray)?.Count == 2 && (bi["items"] as JArray)?.Count == 2, "items: one entry for both", Ctx.Short(c.LastText));
        string json = C43Json(app, x);
        batch["items"] = JArray.FromObject(new object[] { new { type = "float", material = x.Material, property = x.Float, value = b }, new { type = "float", material = "no_such_material", property = x.Float, value = 1 } });
        C43Err(c, C43Text(batch), "not_found", "items[1].material", "items: the second edit names no material");
        c.Check(C43Json(app, x) == json, "... the first edit is not stored");
        batch["items"] = new JArray();
        C43Err(c, C43Text(batch), "bad_args", "items", "items: empty");
        batch["items"] = JArray.FromObject(new object[] { 1 });
        C43Err(c, C43Text(batch), "bad_args", "items[0]", "items: a number");
        batch["items"] = JArray.FromObject(new object[] { new { type = "float", material = x.Material, property = x.Float, value = a, nope = 1 } });
        batch["strict"] = true;
        C43Err(c, C43Text(batch), "bad_args", "items[0].nope", "items: strict: an unknown member");
        batch.Remove("strict");
        var bw = c.Ok(C43Text(batch), "items: an unknown member without strict");
        c.Check((bw["warnings"] as JArray)?.Any(y => ((string)y).Contains("nope")) == true, "... a warning names it", Ctx.Short(c.LastText));
        batch["merge"] = true;
        C43Err(c, C43Text(batch), "bad_args", "merge", "merge with items");
        batch.Remove("merge");
        batch["type"] = "float";
        C43Err(c, C43Text(batch), "bad_args", "type", "items with the arguments of one edit");
        c.Ok(new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, all = true }, "clear");

        // a client's batch: the edit joins it
        n0 = W1UndoCount(c);
        var joined = c.Call(new { cmd = "batch", cmds = new object[] { C43Float(x, a), C43Set(x, new { type = "color", material = x.Material, property = x.Color, value = new[] { 0.9, 0.8, 0.7, 1 } }) } });
        c.Check(joined.Value<bool?>("ok") == true && W1UndoCount(c) == n0 + 1 && C43Array(app, x).Count == 2, "a client's batch: one entry", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, all = true }, "clear");
    }

    // ---------------------------------------------------------------- errors

    static void C43Errors(Ctx c, AppCore app, C43Fix x)
    {
        var e = new Func<object, string>(m => C43Text(C43Set(x, m)));
        C43Err(c, new { cmd = "chara.edit.materials.set", key = x.Key, type = "float" }, "bad_args", "target", "set without a target");
        C43Err(c, new { cmd = "chara.edit.materials.set", key = x.Key, target = 1, type = "float" }, "bad_args", "target", "a number as the target");
        foreach (var bad in new[] { "nope", "hair:9", "hair:nope", "clothes:8", "clothes:nope", "acc:1", "acc:" + C43Nope })
            C43Err(c, new { cmd = "chara.edit.materials.set", key = x.Key, target = bad, type = "float", material = x.Material, property = x.Float, value = 1 }, "bad_args", "target", "target " + bad);
        C43Err(c, new { cmd = "chara.edit.materials.items", key = x.Key, target = "nope" }, "bad_args", "target", "items: target nope");
        C43Err(c, new { cmd = "chara.edit.materials.set", key = x.Key, target = x.Target }, "bad_args", "type", "no edit");
        C43Err(c, e(new { type = 1 }), "bad_args", "type", "a number as the type");
        C43Err(c, e(new { type = "nope", material = x.Material }), "bad_args", "type", "an unknown type");
        foreach (var ro in new[] { "vector", "copy", "name", "cubemap", "projector" })
            C43Err(c, e(new { type = ro, material = x.Material, property = x.Float }), "unsupported", null, "the read-only type " + ro);
        C43Err(c, e(new { type = "renderer", renderer = x.Renderer, property = "ShadowCastingMode", value = 1 }), "unsupported", null, "another renderer property");
        C43Err(c, e(new { type = "float", property = x.Float, value = 1 }), "bad_args", "material", "no material");
        C43Err(c, e(new { type = "float", material = x.Material, value = 1 }), "bad_args", "property", "no property");
        C43Err(c, e(new { type = "float", material = x.Material, property = x.Float }), "bad_args", "value", "no value");
        C43Err(c, e(new { type = "float", material = 1, property = x.Float, value = 1 }), "bad_args", "material", "a number as the material");
        C43Err(c, e(new { type = "float", material = x.Material, property = x.Float, value = "x" }), "bad_args", "value", "a text as a float");
        C43Err(c, "{\"cmd\":\"chara.edit.materials.set\",\"key\":\"" + x.Key + "\",\"target\":\"" + x.Target + "\",\"type\":\"float\",\"material\":\"" + x.Material + "\",\"property\":\"" + x.Float + "\",\"value\":1e39}", "bad_args", "value", "a float beyond the range");
        C43Err(c, e(new { type = "color", material = x.Material, property = x.Color, value = new[] { 1, 1, 1 } }), "bad_args", "value", "three colour numbers");
        C43Err(c, e(new { type = "color", material = x.Material, property = x.Color, value = new object[] { 1, 1, 1, "x" } }), "bad_args", "value[3]", "a text in a colour");
        C43Err(c, e(new { type = "keyword", material = x.Material, property = x.Keyword ?? "X", value = 1 }), "bad_args", "value", "a number as a keyword");
        C43Err(c, e(new { type = "renderer", renderer = x.Renderer, property = "Enabled", value = 2 }), "bad_args", "value", "Enabled 2");
        C43Err(c, e(new { type = "renderer", renderer = x.Renderer, property = "Enabled", value = true }), "bad_args", "value", "Enabled true");
        C43Err(c, e(new { type = "shader", material = x.Material }), "bad_args", "shader", "a shader edit that names nothing");
        C43Err(c, e(new { type = "shader", material = x.Material, renderQueue = 5001 }), "bad_args", "renderQueue", "queue 5001");
        C43Err(c, e(new { type = "shader", material = x.Material, renderQueue = -1 }), "bad_args", "renderQueue", "queue -1");
        C43Err(c, e(new { type = "shader", material = x.Material, renderQueue = 1.5 }), "bad_args", "renderQueue", "a fraction as the queue");
        C43Err(c, e(new { type = "shader", material = x.Material, shader = "HS2Next/no such shader" }), "bad_args", "shader", "a shader that is no candidate");
        C43Err(c, e(new { type = "shader", material = x.Material, shader = "Standard" }), "bad_args", "shader", "an old name");
        C43Err(c, e(new { type = "texture", material = x.Material, property = x.Texture }), "bad_args", "file", "a texture edit that names nothing");
        C43Err(c, e(new { type = "texture", material = x.Material, property = x.Texture, offset = new[] { 1 } }), "bad_args", "offset", "one offset number");
        C43Err(c, e(new { type = "texture", material = x.Material, property = x.Texture, image = "nope" }), "bad_args", "image", "not a res name");
        C43Err(c, e(new { type = "texture", material = x.Material, property = x.Texture, image = "res/" + new string('0', 64) + ".png" }), "not_found", "image", "an image the scene lacks");
        C43Err(c, e(new { type = "float", material = x.Material, property = x.Float, value = 1, renderQueue = 1 }), "bad_args", "renderQueue", "a member of another type");
        C43Err(c, e(new { type = "float", material = "no_such_material", property = x.Float, value = 1 }), "not_found", "material", "a material the target lacks");
        C43Err(c, e(new { type = "float", material = x.Material, property = "NoSuchProperty", value = 1 }), "not_found", "property", "a property the shader lacks");
        C43Err(c, e(new { type = "renderer", renderer = "no_such_renderer", property = "Enabled", value = 0 }), "not_found", "renderer", "a renderer the target lacks");
        C43Err(c, e(new { type = "keyword", material = x.Material, property = "NO_SUCH_KEYWORD", value = true }), "not_found", "property", "a keyword the shader lacks");
        C43Err(c, e(new { type = "float", material = x.Material, property = x.Color, value = 1 }), "bad_args", "type", "a float on a colour property");
        C43Err(c, e(new { type = "color", material = x.Material, property = x.Float, value = new[] { 1, 1, 1, 1 } }), "bad_args", "type", "a colour on a float property");

        // a file: the path's own refusals, and no path in any reply
        string outside = Path.Combine(Path.GetTempPath(), "c43_no_such_image.png");
        string text = Path.Combine(c.Out, "c43_note.txt");
        File.WriteAllText(text, "not an image");
        foreach (var (file, code, what) in new[] { (outside, "not_found", "a missing file"), (text, "io_denied", "an absolute path of another extension"), ("../c43_outside.png", "io_denied", "a relative path outside the root"), ("no_such_image.png", "not_found", "a relative path that is missing") })
        {
            C43Err(c, e(new { type = "texture", material = x.Material, property = x.Texture, file }), code, "file", what);
            c.Check(!c.LastText.Contains("c43_") && !c.LastText.Contains("no_such_image"), what + ": the reply holds no path", Ctx.Short(c.LastText));
        }
        C43Err(c, e(new { type = "texture", material = x.Material, property = x.Texture, file = 1 }), "bad_args", "file", "a number as the file");
        string png = Path.Combine(c.Out, "c43_image.png");
        File.WriteAllBytes(png, C43Png());
        C43Err(c, e(new { type = "texture", material = x.Material, property = x.Texture, file = png, image = (object)null }), "bad_args", "image", "file and image");
        File.Delete(text);
        File.Delete(png);
    }

    static byte[] C43Png()
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = new Color(0.2f, 0.4f, 0.6f, 1f);
        t.SetPixels(px);
        t.Apply();
        var b = t.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(t);
        return b;
    }

    // ---------------------------------------------------------------- clear

    static void C43Clear(Ctx c, AppCore app, C43Fix x)
    {
        object All(string cmd = "chara.edit.materials.clear") => new { cmd, key = x.Key, target = x.Target, all = true };
        float to = x.FloatValue > 0.5f ? x.FloatValue - 0.25f : x.FloatValue + 0.25f;
        void Two()
        {
            c.Ok(C43Text(C43Float(x, to)), "a float");
            c.Ok(C43Text(C43Set(x, new { type = "color", material = x.Material, property = x.Color, value = new[] { 0.4, 0.5, 0.6, 1 } })), "a colour");
        }
        Two();
        var r = W1History1(c, All(), "Clear material edits", "clear all");
        c.Check((int?)r["removed"] == 2 && C43Array(app, x).Count == 0, "clear all: both items", Ctx.Short(c.LastText));
        Two();
        r = c.Ok(new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, item = new { type = "float", material = x.Material, property = x.Float } }, "clear one item");
        c.Check((int?)r["removed"] == 1 && C43Array(app, x).Single() is MeColor, "clear item: that item", Ctx.Short(c.LastText));
        r = c.Ok(new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, material = x.Material }, "clear a material");
        c.Check((int?)r["removed"] == 1 && C43Array(app, x).Count == 0, "clear material: its items", Ctx.Short(c.LastText));
        int n0 = W1UndoCount(c);
        r = c.Ok(All(), "clear nothing");
        c.Check((int?)r["removed"] == 0 && W1UndoCount(c) == n0, "nothing to clear: no entry", Ctx.Short(c.LastText));
        C43Err(c, new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target }, "bad_args", "all", "clear without what");
        C43Err(c, new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, all = false }, "bad_args", "all", "all: false");
        C43Err(c, new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, all = true, material = x.Material }, "bad_args", "all", "all and a material");
        C43Err(c, new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, item = 1 }, "bad_args", "item", "a number as the item");
        C43Err(c, new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, item = new { type = "nope" } }, "bad_args", "item.type", "an item of an unknown type");
        C43Err(c, new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, item = new { type = "float" } }, "bad_args", "item.material", "an item without a material");
        C43Err(c, new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, item = new { type = "vector", material = x.Material, property = x.Float } }, "unsupported", null, "a read-only item");

        // what a file left: an item nothing acts on is no reason to refuse its removal, and read-only items stay
        string path = "coord.clothes." + ClothesSet.Names[C43Slot(x)] + ".materialOverrides";
        var stale = JArray.Parse("[{\"type\":\"vector\",\"material\":\"" + x.Material + "\",\"property\":\"Nope\",\"value\":[1,2,3,4],\"original\":null},{\"type\":\"float\",\"material\":\"gone\",\"property\":\"Gone\",\"value\":1,\"original\":null},{\"type\":\"texture\",\"material\":\"gone\",\"property\":\"Gone\",\"image\":null,\"offset\":[1,1],\"offsetOriginal\":null,\"scale\":null,\"scaleOriginal\":null}]");
        var raw = c.Ok(new { cmd = "chara.edit.set", key = x.Key, set = new Dictionary<string, object> { [path] = stale } }, "a raw array with leftovers");
        var items = c.Ok(new { cmd = "chara.edit.materials.items", key = x.Key, target = x.Target }, "items with leftovers");
        var rows = (items["targets"] as JArray)?.OfType<JObject>().Single()["items"] as JArray;
        c.Check(rows?.Count == 3 && (string)rows[1]["status"] == "materialMissing" && (string)rows[1]["why"] != null && !(bool)rows[0]["editable"] && (bool)rows[1]["editable"], "items: a stale item says why, a vector is not editable", Ctx.Short(c.LastText));
        C43Err(c, C43Text(C43Set(x, new { type = "float", material = "gone", property = "Gone", value = 2 })), "not_found", "material", "an edit of a stale item that would keep it");
        var gone = c.Ok(C43Text(C43Set(x, new { type = "texture", material = "gone", property = "Gone", offset = (object)null })), "an edit that leaves a stale item empty");
        c.Check((gone["removed"] as JArray)?.Count == 1, "... removes it, with no material to look at", Ctx.Short(c.LastText));
        r = c.Ok(new { cmd = "chara.edit.materials.clear", key = x.Key, target = x.Target, item = new { type = "float", material = "gone", property = "Gone" } }, "clear a stale item");
        c.Check((int?)r["removed"] == 1, "... goes", Ctx.Short(c.LastText));
        r = c.Ok(All(), "clear all with a read-only item");
        c.Check((int?)r["removed"] == 0 && C43Array(app, x).Single() is MeVector, "the read-only item stays", C43Json(app, x));
        c.Ok(new { cmd = "chara.edit.set", key = x.Key, set = new Dictionary<string, object> { [path] = new JArray() } }, "the array empty again");
    }

    static int C43Slot(C43Fix x)
    {
        MaterialEditTarget.TryParse(x.Key, x.Target, out var t, out _);
        return t.Slot;
    }

    // ---------------------------------------------------------------- reads

    static void C43Reads_(Ctx c, AppCore app, C43Fix x)
    {
        var all = c.Ok(new { cmd = "chara.edit.materials.items", key = x.Key }, "items, every array");
        var targets = (all["targets"] as JArray).OfType<JObject>().ToList();
        c.Check((string)targets[0]["target"] == "character" && (string)targets[1]["target"] == "hair:0" && (string)targets[5]["target"] == "clothes:0" && targets.Count >= 13 && targets.All(t => t["items"] is JArray), "items: character, hair:0.., clothes:0.., then the accessories", string.Join(", ", targets.Select(t => (string)t["target"])));
        var one = c.Ok(new { cmd = "chara.edit.materials.items", key = x.Key, target = "hair:front" }, "items hair:front");
        c.Check((one["targets"] as JArray).Count == 1 && (string)one["targets"][0]["target"] == "hair:1", "target by a part's name: the canonical form", Ctx.Short(c.LastText));
        var top = c.Ok(new { cmd = "chara.edit.materials.items", key = x.Key, target = "clothes:top" }, "items clothes:top");
        c.Check((string)top["targets"][0]["target"] == "clothes:0", "target by a kind's name", Ctx.Short(c.LastText));
        string acc = targets.Select(t => (string)t["target"]).FirstOrDefault(t => t.StartsWith("acc:", StringComparison.Ordinal));
        if (acc != null) c.Ok(new { cmd = "chara.edit.materials.items", key = x.Key, target = acc }, "items of an accessory");
    }

    // ---------------------------------------------------------------- a property only the replacement declares

    // the fixture of such a property: a value property of a mapped material of the character's clothes, hair or body, on a shader an item can name
    static C43Fix C43OnlyReplacement(AppCore app, string f)
    {
        var ctx = app.Runtime.FindChara(f).Context;
        var objects = new List<((OT, int) obj, string target)> { ((OT.Character, 0), "character") };
        for (int k = 0; k < ClothesSet.Count; k++) objects.Add(((OT.Clothing, k), "clothes:" + k));
        for (int k = 0; k < HairData.PartNames.Length; k++) objects.Add(((OT.Hair, k), "hair:" + k));
        foreach (var (obj, target) in objects)
        {
            var rends = MaterialEditorApplier.ObjectRenderers(ctx, obj);
            if (rends == null) continue;
            foreach (var r in rends)
            {
                if (MosaicSet.IsMember(r) || ctx.DrawsBodyMaterials(r) || ctx.DrawsBodySkin(r)) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !m.shader || !ShaderMap.MappedFrom(m) || !MaterialShaders.IsCandidate(m.shader.name)) continue;
                    var sh = m.shader;
                    for (int i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        var t = sh.GetPropertyType(i);
                        string n = sh.GetPropertyName(i);
                        if ((t != ShaderPropertyType.Float && t != ShaderPropertyType.Range) || !n.StartsWith("_", StringComparison.Ordinal) || MaterialWrites.Declared(m, n)) continue;
                        return new C43Fix { Key = f, Target = target, Material = MaterialEditorApplier.Fmt(m.name), Float = n.Substring(1), Live = m, FloatValue = m.GetFloat(n) };
                    }
                }
            }
        }
        return null;
    }

    static void C43Reach(Ctx c, AppCore app, string f)
    {
        var x = C43OnlyReplacement(app, f);
        if (!c.Check(x != null, "the character has a property only the mapping's replacement declares")) return;
        c.Info("the replacement's own property: " + x.Target + " " + x.Material + " " + x.Float + " on " + x.Live.shader.name + ", mapped from " + ShaderMap.MappedFrom(x.Live).name);
        MaterialEditTarget.TryParse(f, x.Target, out var t, out _);
        string shader = x.Live.shader.name, prop = "_" + x.Float;
        float to = x.FloatValue > 0.5f ? x.FloatValue - 0.25f : x.FloatValue + 0.25f;
        string json = C43Json(app, x);

        // a new item: the shader the build writes on does not have it
        C43Err(c, C43Text(C43Float(x, to)), "not_found", "property", "a new item on a property only the replacement declares");
        c.Check(C43Json(app, x) == json && C43Array(app, x).Count == 0, "... the array unchanged");
        c.Check(x.Live.GetFloat(prop) == x.FloatValue, "... the material unchanged");
        var batch = C43Set(x, new { });
        batch["items"] = JArray.FromObject(new object[] { new { type = "shader", material = x.Material, renderQueue = 2500 }, new { type = "float", material = x.Material, property = x.Float, value = to } });
        C43Err(c, C43Text(batch), "not_found", "items[1].property", "the same in a batch with a queue: nothing of the batch stored");
        int n0 = W1UndoCount(c);
        var same = c.Ok(C43Text(C43Float(x, x.FloatValue)), "the value the material holds");
        c.Check((same["changed"] as JArray)?.Count == 0 && W1UndoCount(c) == n0, "... makes no item, so nothing is refused", Ctx.Short(c.LastText));

        // a shader item naming the replacement itself: the build writes on that shader, and the property is within reach
        var named = c.Ok(C43Text(C43Set(x, new { type = "shader", material = x.Material, shader })), "a shader item naming the replacement itself");
        c.Check((string)(named["items"] as JArray)?.OfType<JObject>().Single()["status"] == "applied", "... applies", Ctx.Short(c.LastText));
        x.Live = C43Live(app, x, t);
        c.Check(x.Live && !ShaderMap.MappedFrom(x.Live) && MaterialWrites.Declared(x.Live, prop), "... the material is no longer the mapping's", x.Live ? x.Live.shader.name : "no material");
        var one = W1History1(c, C43Text(C43Float(x, to)), "Material edit", "the same property now");
        var row = (one["items"] as JArray)?.OfType<JObject>().Single();
        c.Check(row != null && (string)row["status"] == "applied" && (float)row["value"] == to, "... is accepted and applies", row?.ToString(Formatting.None));
        x.Live = C43Live(app, x, t);
        c.Check(x.Live.GetFloat(prop) == to, "... on the material", x.Live.GetFloat(prop) + "");
        var rebuilt = app.Runtime.FindChara(f);
        c.Check(rebuilt.RequestPluginRebuild("test.material"), "a rebuild is taken");
        app.Runtime.ApplyNow();
        x.Live = C43Live(app, x, t);
        c.Check(x.Live && x.Live.GetFloat(prop) == to, "... and outlives a rebuild", x.Live ? x.Live.GetFloat(prop) + "" : "no material");
        c.Ok(new { cmd = "chara.edit.materials.clear", key = f, target = x.Target, all = true }, "clear");
        x.Live = C43Live(app, x, t);
        c.Check(C43Array(app, x).Count == 0 && ShaderMap.MappedFrom(x.Live) && !MaterialWrites.Declared(x.Live, prop), "the character is the mapping's again", x.Live.shader.name);

        // the shader item and the value in one call: the item's shader counts
        batch["items"] = JArray.FromObject(new object[] { new { type = "shader", material = x.Material, shader }, new { type = "float", material = x.Material, property = x.Float, value = to } });
        var both = c.Ok(C43Text(batch), "the shader item and the value in one call");
        c.Check((both["changed"] as JArray)?.Count == 2, "... both stored", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "chara.edit.materials.clear", key = f, target = x.Target, all = true }, "clear");

        // a stored item is not refused by this, and it can always go
        string path = AppearancePaths.ToRelative(t.ArrayPath);
        var stored = JArray.Parse("[{\"type\":\"float\",\"material\":\"" + x.Material + "\",\"property\":\"" + x.Float + "\",\"value\":" + to.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"original\":null}]");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [path] = stored } }, "a stored item on that property");
        var edit = c.Ok(C43Text(C43Float(x, to + 0.125f)), "an edit of the stored item");
        c.Check((edit["changed"] as JArray)?.Count == 1 && (float)C43Array(app, x).OfType<MeFloat>().Single().Value == to + 0.125f, "... is not refused", Ctx.Short(c.LastText));
        var gone = c.Ok(new { cmd = "chara.edit.materials.clear", key = f, target = x.Target, item = new { type = "float", material = x.Material, property = x.Float } }, "its removal");
        c.Check((int?)gone["removed"] == 1 && C43Array(app, x).Count == 0, "... is not refused", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- the mosaic

    static bool C43Warned(JObject r) => (r["warnings"] as JArray)?.Any(y => ((string)y).Contains(StudioOps.MaterialMosaicWarning)) == true;

    static void C43Refused(Ctx c, AppCore app, C43Fix x, string request, string what)
    {
        int n0 = W1UndoCount(c);
        string sha = W1DocSha(c), json = C43Json(app, x);
        var r = c.Call(request);
        c.Check(r.Value<string>("code") == "unsupported" && r.Value<string>("error") == C43MosaicText, what + ": unsupported, \"" + C43MosaicText + "\"", Ctx.Short(c.LastText));
        c.Check(C43Warned(r), what + ": the warning " + StudioOps.MaterialMosaicWarning, Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == n0 && W1DocSha(c) == sha && C43Json(app, x) == json, what + ": no entry, no change");
    }

    static void C43Mosaic(Ctx c, AppCore app, C43Fix x)
    {
        var ctx = app.Runtime.FindChara(x.Key).Context;
        var member = MaterialEditorApplier.ObjectRenderers(ctx, (OT.Character, 0)).FirstOrDefault(MosaicSet.IsMember);
        if (!c.Check(member, "the target has a member of the mosaic set")) return;
        string rn = MaterialEditorApplier.Fmt(member.name);
        var whole = new Func<object, JObject>(m =>
        {
            var o = JObject.FromObject(m);
            o["cmd"] = "chara.edit.materials.set";
            o["key"] = x.Key;
            o["target"] = "character";
            return o;
        });
        // 1. one edit on a member
        C43Refused(c, app, x, C43Text(whole(new { type = "renderer", renderer = rn, property = "Enabled", value = 0 })), "Enabled on a member");
        // 2. the material of a member, and a swap to a mosaic shader on any material
        var mat = member.sharedMaterials.FirstOrDefault(m => m);
        if (mat) C43Refused(c, app, x, C43Text(whole(new { type = "float", material = MaterialEditorApplier.Fmt(mat.name), property = "Gloss", value = 0.5 })), "a float on a member's material");
        foreach (var name in new[] { "AIT/mnpb", "HS2Next/Mosaic", "HS2Next/AIT mnpb" })
            C43Refused(c, app, x, C43Text(C43Set(x, new { type = "shader", material = x.Material, shader = name })), "a swap to " + name);
        // 3. a batch with one such edit: all or nothing
        var batch = C43Set(x, new { });
        batch["items"] = JArray.FromObject(new object[] { new { type = "float", material = x.Material, property = x.Float, value = 0.25 }, new { type = "shader", material = x.Material, shader = "HS2Next/Mosaic" } });
        C43Refused(c, app, x, C43Text(batch), "a batch with a refused edit");
    }

    // ---------------------------------------------------------------- an item

    static void C43Item(Ctx c, AppCore app, string item)
    {
        // item.set has no way past the op
        foreach (var path in new[] { "materialOverrides", "item.materialOverrides", "materialOverrides[0]" })
        {
            var r = c.Call(new { cmd = "item.set", key = item, set = new Dictionary<string, object> { [path] = new object[0] } });
            c.Check(r.Value<string>("code") == "bad_args" && r.Value<string>("error") == "use item.materials.set" && ((string)r["detail"]?["arg"]) == "set." + path, "item.set " + path + ": bad_args, \"use item.materials.set\"", Ctx.Short(c.LastText));
        }
        var op = app.Ops.SetItem(new[] { item }, new (string, object)[] { ("materialOverrides", new List<MaterialOverride>()) });
        c.Check(op.Code == "bad_args" && op.Message == "use item.materials.set", "StudioOps.SetItem: the same refusal", op.ToString());

        var all = c.Ok(new { cmd = "item.materials", key = item, allProps = true }, "item.materials allProps");
        var some = c.Ok(new { cmd = "item.materials", key = item }, "item.materials");
        var slot = all["renderers"]?[0]?["slots"]?[0] as JObject;
        var slot0 = some["renderers"]?[0]?["slots"]?[0] as JObject;
        c.Check(slot != null && slot0 != null && (slot["props"] as JObject).Count > (slot0["props"] as JObject).Count, "allProps: every property the shader declares", (slot?["props"] as JObject)?.Count + " / " + (slot0?["props"] as JObject)?.Count);
        C43Err(c, new { cmd = "item.materials", key = item, allProps = 1 }, "bad_args", "allProps", "a number as allProps");

        string material = (string)slot?["material"];
        var props = slot?["props"] as JObject;
        string colour = props?.Properties().FirstOrDefault(p => p.Value is JArray a && a.Count == 4 && p.Name.StartsWith("_", StringComparison.Ordinal))?.Name;
        if (!c.Check(material != null && colour != null, "the item has a material with a colour", Ctx.Short(c.LastText))) return;
        string fmt = MeKey.Format(material);
        var set = new { cmd = "item.materials.set", key = item, type = "color", material = fmt, property = colour.Substring(1), value = new[] { 0.3, 0.2, 0.1, 1 } };
        var one = W1History1(c, set, "Material edit", "item.materials.set");
        c.Check((string)one["target"] == "item" && (one["changed"] as JArray)?.Count == 1 && (string)(one["items"] as JArray)?.OfType<JObject>().Single()["status"] == "applied", "item.materials.set: {target, changed, removed, items}", Ctx.Short(c.LastText));
        var items = c.Ok(new { cmd = "item.materials.items", key = item }, "item.materials.items");
        c.Check((items["targets"] as JArray).Count == 1 && (string)items["targets"][0]["target"] == "item" && (items["targets"][0]["items"] as JArray).Count == 1, "item.materials.items: the one array", Ctx.Short(c.LastText));
        C43Err(c, new { cmd = "item.materials.set", key = item, type = "float", material = "no_such_material", property = "Metallic", value = 1 }, "not_found", "material", "item: a material the item lacks");
        C43Err(c, new { cmd = "item.materials.set", key = item, type = "renderer", renderer = "no/such/path", property = "Enabled", value = 0 }, "not_found", "renderer", "item: a path the item lacks");
        var clear = W1History1(c, new { cmd = "item.materials.clear", key = item, all = true }, "Clear material edits", "item.materials.clear");
        c.Check((int?)clear["removed"] == 1, "item.materials.clear: removed", Ctx.Short(c.LastText));

        // an item is written after its mapping: what its shader declares is within reach, whatever the mapping replaced
        var real = app.Runtime.FindItem(item);
        foreach (var e in real.Writer.Renderers)
            foreach (var s in e.Slots)
            {
                var m = s?.Material;
                if (!m || !m.shader || !ShaderMap.MappedFrom(m)) continue;
                var sh = m.shader;
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string n = sh.GetPropertyName(i);
                    var ty = sh.GetPropertyType(i);
                    if ((ty != ShaderPropertyType.Float && ty != ShaderPropertyType.Range) || !n.StartsWith("_", StringComparison.Ordinal) || MaterialWrites.Declared(m, n) || MaterialWrites.FallbackFixed(m, n)) continue;
                    float was = m.GetFloat(n), to = was > 0.5f ? was - 0.25f : was + 0.25f;
                    var ok = c.Ok(new { cmd = "item.materials.set", key = item, type = "float", material = MeKey.Format(m.name), property = n.Substring(1), value = to }, "item: a property only the replacement declares");
                    c.Check((ok["changed"] as JArray)?.Count == 1 && m.GetFloat(n) == to, "... is accepted: the item keeps what its shader declares", Ctx.Short(c.LastText));
                    c.Ok(new { cmd = "item.materials.clear", key = item, all = true }, "clear");
                    return;
                }
            }
        c.Info("the item's materials have no property only the replacement declares: nothing to show on it");
    }

    // ---------------------------------------------------------------- hair bone modifiers

    static void C43BoneMods(Ctx c, AppCore app, string f)
    {
        var read = c.Ok(new { cmd = "chara.edit.boneMods", key = f }, "chara.edit.boneMods");
        var bones = (read["bones"] as JArray)?.Select(x => (string)x).ToList();
        c.Check(bones != null && bones.Count > 0 && bones[0] == "N_hair_Root" && read["mods"] is JArray, "boneMods: the hair class, the hair root first", Ctx.Short(c.LastText));
        string bone = bones?.FirstOrDefault(b => b != "N_hair_Root") ?? "N_hair_Root";
        var one = W1History1(c, new { cmd = "chara.edit.boneMods.set", key = f, bone, length = 1.25 }, "Hair bone adjustment", "boneMods.set length");
        c.Check((string)one["bone"] == bone && (bool?)one["removed"] == false, "boneMods.set: {bone, removed}", Ctx.Short(c.LastText));
        var mods = (c.Ok(new { cmd = "chara.edit.boneMods", key = f }, "boneMods")["mods"] as JArray).OfType<JObject>().ToList();
        var mod = mods.FirstOrDefault(m => (string)m["bone"] == bone);
        c.Check(mod != null && mod.Properties().Select(p => p.Name).SequenceEqual(new[] { "bone", "location", "scale", "length", "position", "rotation", "found" }) && (string)mod["location"] == "bodyTop" && (float)mod["length"] == 1.25f && (bool)mod["found"], "boneMods: a row's members, location bodyTop", mod?.ToString(Formatting.None));
        var keep = c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, scale = new[] { 1.1, 1, 1 } }, "boneMods.set scale");
        mod = (c.Ok(new { cmd = "chara.edit.boneMods", key = f }, "boneMods")["mods"] as JArray).OfType<JObject>().First(m => (string)m["bone"] == bone);
        c.Check((float)mod["length"] == 1.25f && (float)mod["scale"][0] == 1.1f, "boneMods.set: members left out keep their value", mod.ToString(Formatting.None));
        int n0 = W1UndoCount(c);
        c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, length = 1.3, merge = true }, "merge 1");
        c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, length = 1.35, merge = true }, "merge 2");
        c.Check(W1UndoCount(c) == n0 + 1, "boneMods.set merge: one entry", W1UndoCount(c) + "");
        n0 = W1UndoCount(c);
        var same = c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, length = 1.35 }, "the same value");
        c.Check(W1UndoCount(c) == n0 && (bool?)same["removed"] == false, "the same value: no entry", Ctx.Short(c.LastText));
        var nul = c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, length = (object)null }, "length null");
        c.Check((bool?)nul["removed"] == false, "a member given as null: back to no effect, the entry stays for the scale", Ctx.Short(c.LastText));
        var gone = c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, scale = (object)null }, "scale null");
        c.Check((bool?)gone["removed"] == true && app.Ops.BoneModArray(f).All(m => m.Bone != bone), "a modifier with no effect leaves the array", Ctx.Short(c.LastText));
        C43Err(c, new { cmd = "chara.edit.boneMods.set", key = f, bone }, "bad_args", "bone", "boneMods.set with no member");
        C43Err(c, new { cmd = "chara.edit.boneMods.set", key = f, length = 1.1 }, "bad_args", "bone", "boneMods.set without a bone");
        C43Err(c, new { cmd = "chara.edit.boneMods.set", key = f, bone = "cf_J_Spine01", length = 1.1 }, "not_found", "bone", "a bone that is no hair bone");
        C43Err(c, new { cmd = "chara.edit.boneMods.set", key = f, bone = "no_such_bone", length = 1.1 }, "not_found", "bone", "a bone this character lacks");
        C43Err(c, new { cmd = "chara.edit.boneMods.set", key = f, bone, scale = new[] { 1, 1 } }, "bad_args", "scale", "two scale numbers");
        C43Err(c, new { cmd = "chara.edit.boneMods.set", key = f, bone, length = "x" }, "bad_args", "length", "a text as the length");
        c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, length = 1.2 }, "a modifier again");
        var cl = W1History1(c, new { cmd = "chara.edit.boneMods.clear", key = f, bone }, "Hair bone adjustment", "boneMods.clear bone");
        c.Check((int?)cl["removed"] == 1, "boneMods.clear: removed", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "chara.edit.boneMods.set", key = f, bone, length = 1.2 }, "a modifier again");
        var ca = c.Ok(new { cmd = "chara.edit.boneMods.clear", key = f, all = true }, "boneMods.clear all");
        c.Check((int?)ca["removed"] == 1, "boneMods.clear all: the hair bones", Ctx.Short(c.LastText));
        C43Err(c, new { cmd = "chara.edit.boneMods.clear", key = f }, "bad_args", "all", "boneMods.clear without what");
        C43BoneModsFound(c, app, f);
    }

    // found: where the applier would look. An entry without a location goes on to the accessories when the body lacks its bone.
    static void C43BoneModsFound(Ctx c, AppCore app, string f)
    {
        var ctx = app.Runtime.FindChara(f).Context;
        var names = HairBones.Names(ctx);
        var body = AbmxApplier.BodyDictionary(ctx);
        var accessories = HairBones.AccessoryBones(ctx);
        string bone = accessories.Keys.FirstOrDefault(n => !names.Contains(n) && !AbmxApplier.Find(body, n));
        if (!c.Check(bone != null, "the character has a bone only an accessory holds", accessories.Count + " on its accessories")) return;
        c.Info("the accessory's bone: " + bone);
        var keep = new List<BodyBoneMod>(app.Ops.BoneModArray(f));
        var list = new List<BodyBoneMod>(keep)
        {
            new BodyBoneMod { Bone = bone, Location = BodyBoneMod.LocationUnknown, Length = 1.1f },
            new BodyBoneMod { Bone = "no_such_bone", Location = BodyBoneMod.LocationUnknown, Length = 1.1f },
        };
        var set = app.Ops.EditAppearance(new[] { f }, new (string, object)[] { (BoneModEdits.Path, list) }, new ExecuteOptions { Source = CommandSource.Bridge, Label = "test" }, allowRebuild: true);
        c.Check(set.Ok, "entries of a hand-made file stand", set.ToString());
        app.Runtime.ApplyNow();
        var all = (c.Ok(new { cmd = "chara.edit.boneMods", key = f, hair = false }, "boneMods hair false")["mods"] as JArray).OfType<JObject>().ToList();
        var row = all.FirstOrDefault(m => (string)m["bone"] == bone);
        c.Check(row != null && (bool)row["found"] && (string)row["location"] == "unknown", "an entry without a location on an accessory's bone reads found: true", row?.ToString(Formatting.None));
        var lost = all.FirstOrDefault(m => (string)m["bone"] == "no_such_bone");
        c.Check(lost != null && !(bool)lost["found"], "an entry found nowhere reads found: false", lost?.ToString(Formatting.None));
        var hair = (c.Ok(new { cmd = "chara.edit.boneMods", key = f }, "boneMods")["mods"] as JArray).OfType<JObject>().Select(m => (string)m["bone"]).ToList();
        c.Check(!hair.Contains(bone) && hair.Contains("no_such_bone"), "hair: true leaves out the accessory's entry and keeps the lost one", string.Join(", ", hair));

        // an entry stored on the body looks at the body only
        list[list.Count - 2] = new BodyBoneMod { Bone = bone, Location = BodyBoneMod.LocationBodyTop, Length = 1.1f };
        set = app.Ops.EditAppearance(new[] { f }, new (string, object)[] { (BoneModEdits.Path, list) }, new ExecuteOptions { Source = CommandSource.Bridge, Label = "test" }, allowRebuild: true);
        c.Check(set.Ok, "the same bone stored on the body", set.ToString());
        app.Runtime.ApplyNow();
        row = (c.Ok(new { cmd = "chara.edit.boneMods", key = f, hair = false }, "boneMods hair false")["mods"] as JArray).OfType<JObject>().FirstOrDefault(m => (string)m["bone"] == bone);
        c.Check(row != null && !(bool)row["found"], "... reads found: false: the body lacks it", row?.ToString(Formatting.None));
        set = app.Ops.EditAppearance(new[] { f }, new (string, object)[] { (BoneModEdits.Path, keep) }, new ExecuteOptions { Source = CommandSource.Bridge, Label = "test" }, allowRebuild: true);
        c.Check(set.Ok, "the array as it was", set.ToString());
        app.Runtime.ApplyNow();
    }

    // ---------------------------------------------------------------- the map, none loaded

    static void C43Map(Ctx c)
    {
        var list = c.Ok(new { cmd = "map.materials" }, "map.materials with no map");
        c.Check((list["materials"] as JArray)?.Count == 0 && (int?)list["count"] == 0, "no map: no materials", Ctx.Short(c.LastText));
        var items = c.Ok(new { cmd = "map.materials.items" }, "map.materials.items with no map");
        c.Check((items["targets"] as JArray)?.Count == 1 && (string)items["targets"][0]["target"] == "map" && (items["targets"][0]["items"] as JArray)?.Count == 0, "no map: the empty array", Ctx.Short(c.LastText));
        C43Err(c, new { cmd = "map.materials.set", type = "float", material = "m", property = "Glossiness", value = 1 }, "unavailable", null, "map.materials.set with no map");
        C43Err(c, new { cmd = "map.materials.clear", all = true }, "unavailable", null, "map.materials.clear with no map");
        C43Err(c, new { cmd = "map.materials", limit = -1 }, "bad_args", "limit", "map.materials limit -1");
        C43Err(c, new { cmd = "map.materials", props = 1 }, "bad_args", "props", "map.materials a number as props");
        var strict = c.Call(new { cmd = "map.materials.items", nope = 1, strict = true });
        c.Check(strict.Value<string>("code") == "bad_args" && (string)strict["detail"]?["arg"] == "nope", "map.materials.items: strict", Ctx.Short(c.LastText));
    }
}
