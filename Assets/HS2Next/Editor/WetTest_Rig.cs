// WetTest: a stain through every path that rebuilds what it is drawn on, on the targets of Tools/targets.json, in edit mode with the production URP asset. Objects are read again
// after anything that may rebuild; images stay in memory, as digests of the composites' bytes.
//   WR0  the rig: frame, targets, a first stain, its status
//   WF8  a stain shows the same bytes before a save and after a load
//   WF7  save, load, save: byte-identical, with a stopped stain and with a running one under the global pause
//   WB1  a clothes colour change: the composite is drawn again, the stain stays, the map is the same
//   WB2  a part swap and back, and undo; the state of a part (on, half, off)
//   WB3  a coordinate applied
//   WB4  chara.change, a plugin's rebuild, a skin edit, a shape, a head swap
//   WB6  two characters: a rebuild of one leaves the other's composites byte-equal
//   WB7  cut bodies and parts that draw the body's material
//   WB8  a removed material edit under a stain
//   WB9  character thumbnails are dry
//   WB11 a deleted node and its end, a hidden node, a new scene
//   WB14 a copy: duplicate and scene import carry the stains, not the puddles, and do not run
//   WB15 a stain's clock by itself: it stops at its end, a loop dries and goes on, a kept stain stands
//   WD8  two runs of the same steps: bytes equal, drops included
//   WD8a the same run after a save and a load emits the same drops
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HS2Next.App.Commands;
using HS2Next.Chara.Edit;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Wet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class WetTest
{
    // ------------------------------------------------------------ helpers

    static WetPart Part(Host h) => h.Rt.GetPart<WetPart>();
    static CharaNodeState St(Host h, string key) => (CharaNodeState)h.Session.Model.Find(key).State;
    static WetRig RigOf(Host h, string key) => Part(h).RigOf(key);

    /// <summary>A scene with one target in it, and a default-origin stain of the preset at its full spread (stopped); the stain's id.</summary>
    static string Stain(Host h, string key, string preset = WetPreset.Blood, float? t = null, bool running = false, Action<WetStain> edit = null)
    {
        var block = St(h, key).Wet.Clone();
        var s = new WetStain { Id = block.NextId(), Name = "stain " + block.Stains.Count, Seed = 7 + block.Stains.Count };
        WetPreset.ApplyTo(s, preset);
        edit?.Invoke(s);
        s.T = t ?? s.Shape.Spread;
        s.Running = running;
        block.Stains.Add(s);
        block.Current = s.Id;
        string why = Part(h).Bake(key, block, s);
        Check("the bake ran", why == null, why ?? "");
        var r = h.Session.Commands.Execute(new WetEditCommand(key, block, WetEditCommand.LabelAdd));
        Check("the edit went in", r.Status == CommandStatus.Ok, r.ToString());
        h.Rt.ApplyNow();
        return s.Id;
    }

    /// <summary>A block change through the one command (the capture first, as ops do it).</summary>
    static void Edit(Host h, string key, string label, Action<CharaWet> change)
    {
        Part(h).CaptureNow(key);
        var block = St(h, key).Wet.Clone();
        change(block);
        var r = h.Session.Commands.Execute(new WetEditCommand(key, block, label));
        Check("edit: " + label, r.Status == CommandStatus.Ok, r.ToString());
        h.Rt.ApplyNow();
    }

    static byte[] ReadBytes(RenderTexture rt)
    {
        var q = AsyncGPUReadback.Request(rt, 0);
        q.WaitForCompletion();
        return q.hasError ? null : q.GetData<byte>().ToArray();
    }

    static string Digest(byte[] b)
    {
        if (b == null) return "unread";
        using (var s = SHA1.Create()) return Convert.ToBase64String(s.ComputeHash(b));
    }

    /// <summary>The digest of each drawn composite and mask, by target key.</summary>
    static SortedDictionary<string, string> Digests(Host h, string key)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var rig = RigOf(h, key);
        if (rig == null) return d;
        foreach (var kv in rig.Slots)
        {
            var c = kv.Value.Compose;
            if (c.Wet) d[kv.Key] = Digest(ReadBytes(c.Wet));
            if (c.Mask) d[kv.Key + "|mask"] = Digest(ReadBytes(c.Mask));
        }
        return d;
    }

    static string Differs(SortedDictionary<string, string> a, SortedDictionary<string, string> b)
    {
        var keys = a.Keys.Union(b.Keys).ToList();
        var diff = keys.Where(k => !a.TryGetValue(k, out var x) || !b.TryGetValue(k, out var y) || x != y).ToList();
        return diff.Count == 0 ? null : diff.Count + " of " + keys.Count + ": " + string.Join(", ", diff.Take(4));
    }

    static void Same(string what, SortedDictionary<string, string> a, SortedDictionary<string, string> b)
    {
        string d = Differs(a, b);
        Check(what, d == null && a.Count > 0, d ?? a.Count + " images");
    }

    static string OtherRow(Host h, string key, string path)
    {
        string cur = (string)h.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];
        var l = h.Call(new { cmd = "chara.edit.list", key, path, limit = 10000 });
        return (l["rows"] as JArray)?.Select(x => (string)x["ref"]).FirstOrDefault(x => x != cur && x != null && x.StartsWith("illusion.hs2#", StringComparison.Ordinal));
    }

    /// <summary>Swaps a clothes kind to the first other row whose part draws something; the row, or null with the part as it was.</summary>
    static string SwapToPart(Host h, string key, string path, int kind)
    {
        string cur = (string)h.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];
        var l = h.Call(new { cmd = "chara.edit.list", key, path, limit = 400 });
        var rows = (l["rows"] as JArray)?.Select(x => (string)x["ref"]).Where(x => x != cur && x != null && x.StartsWith("illusion.hs2#", StringComparison.Ordinal)).Take(20).ToList();
        if (rows == null) return null;
        foreach (var row in rows)
        {
            h.Ok(new { cmd = "chara.edit.item", key, path, @ref = row }, "the part swapped to " + row);
            h.Rt.ApplyNow();
            var part = h.Rt.FindChara(key).Context.Clothes[kind];
            if (part && part.GetComponentsInChildren<Renderer>(false).Any(r => r.enabled && r is SkinnedMeshRenderer)) return row;
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
        }
        return null;
    }

    static void SetEdit(Host h, string key, string path, object value, string what) =>
        h.Ok(new { cmd = "chara.edit.set", key, set = new Dictionary<string, object> { [path] = value } }, what);

    /// <summary>The kinds of a live clothes slot: the slot, and the clothes kind it sits on; null when none.</summary>
    static (WetRig.Slot slot, int kind)? ClothesSlot(Host h, string key)
    {
        var rig = RigOf(h, key);
        foreach (var kv in rig.Slots)
        {
            var t = kv.Value.Target;
            if (t.Kind != WetKinds.Clothes) continue;
            if (kv.Value.Compose.Wet && t.Address.StartsWith("clothes:", StringComparison.Ordinal))
                return (kv.Value, int.Parse(t.Address.Substring(8, 1)));
        }
        return null;
    }

    static string SaveScene(Host h, string name)
    {
        var r = h.Ok(new { cmd = "scene.save", name, overwrite = true, thumb = "placeholder", clock = "2026-11-06T00:00:00Z" }, "scene.save");
        return (string)r["path"];
    }

    // the pause is not saved: it is set in the load's own event, after the runtime reset the clock, so no frame moves anything
    static void LoadPaused(Host h, string path)
    {
        h.Ok(new { cmd = "scene.new" }, "scene.new");
        Action<SceneLoadedArgs> pause = _ => h.Rt.SetPaused(true);
        h.Session.Events.Loaded += pause;
        try { h.Ok(new { cmd = "scene.load", path }, "scene.load"); }
        finally { h.Session.Events.Loaded -= pause; }
        h.Rt.ApplyNow();
    }

    static string FirstDifference(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        if (i == n && a.Length == b.Length) return "";
        int from = Math.Max(0, i - 80);
        return "at " + i + " of " + a.Length + " / " + b.Length + ": " + System.Text.Encoding.UTF8.GetString(a, from, Math.Min(160, a.Length - from)).Replace('\n', ' ') + " || " +
               System.Text.Encoding.UTF8.GetString(b, from, Math.Min(160, b.Length - from)).Replace('\n', ' ');
    }

    static void LoadScene(Host h, string path)
    {
        h.Ok(new { cmd = "scene.new" }, "scene.new");
        h.Ok(new { cmd = "scene.load", path }, "scene.load");
        h.Rt.ApplyNow();
    }

    // ------------------------------------------------------------ the parts

    static void Render_Rig()
    {
        Group("WR0", "the rig on a real character", Wr0);
        Group("WF8", "the same pixels before a save and after a load", Wf8);
        Group("WF7", "save, load, save", Wf7);
        Group("WB1", "a clothes colour change", Wb1);
        Group("WB2", "a part swap and back", Wb2);
        Group("WB3", "a coordinate applied", Wb3);
        Group("WB4", "rebuilds and body edits", Wb4);
        Group("WB6", "two characters", Wb6);
        Group("WB8", "a removed material edit under a stain", Wb8);
        Group("WB9", "thumbnails are dry", Wb9);
        Group("WB11", "a deleted node, a hidden node, a new scene", Wb11);
        Group("WB14", "duplicate and scene import", Wb14);
        Group("WB15", "a stain's clock by itself", Wb15);
        Group("WD8", "two runs of the same steps", Wd8);
        Group("WD8a", "the same run across a save and a load", Wd8a);
        Group("WT1", "accessories, shoes and gloves by their switches", Wt1);
    }

    static void Wr0()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            var real = h.Rt.FindChara(key);
            var ok = WetTargets.Frame(real.Context, out var f);
            Check("a frame from the body's bones", ok, "");
            var hips = real.Context.Hips;
            double along = HS2Next.Scenes.Routes.Vec3d.Dot(f.Forward, WetTargets.D(hips.forward));
            Check("the frame's forward is the body's front", along > .8, along.ToString("F3"));
            Check("the frame's up is the body's up", HS2Next.Scenes.Routes.Vec3d.Dot(f.Up, WetTargets.D(hips.up)) > .8, "");
            Check("the frame's scale is about one", f.Scale > .5 && f.Scale < 2, f.Scale.ToString("F3"));
            var targets = WetTargets.Collect(real);
            Info("targets: " + string.Join(", ", targets.GroupBy(t => t.Kind).Select(g => g.Key + " " + g.Count())) + "; shown " + targets.Count(t => t.Shown) + "; tiled " + targets.Count(t => t.State == WetTargetStates.Tiled));
            Check("clothes, skin, face and hair have targets", new[] { WetKinds.Clothes, WetKinds.Skin, WetKinds.Face, WetKinds.Hair }.All(k => targets.Any(t => t.Kind == k)), "");
            var known = new Dictionary<Renderer, string>();
            foreach (var t in targets) WetTargets.ReadSignature(t, known);
            Check("a target has a signature", targets.All(t => t.Signature != null || t.State == WetTargetStates.Unreadable), "");
            Check("addresses are canonical", targets.All(t => PoseAddress.IsCanonical(PoseAddressForm.Blend, t.Address)), string.Join(", ", targets.Take(3).Select(t => t.Address)));
            Check("no rig before a stain", Part(h).Rigs == 0, "");

            string id = Stain(h, key);
            var st = St(h, key).Wet;
            var rig = RigOf(h, key);
            Check("a rig", rig != null, "");
            Info("maps: " + string.Join(", ", st.Stains[0].Maps.Select(m => m.Renderer + "#" + m.Slot + " " + m.Reach.ToString("F3"))));
            Check("the stain reaches something", st.Stains[0].Maps.Count > 0, st.Stains[0].Maps.Count.ToString());
            Check("every map's picture is in the store", st.Stains[0].Maps.All(m => h.Session.Model.File.Res.Contains(m.Res)), "");
            var s = Part(h).StatusOf(key);
            Check("status: every map live", s.Live == st.Stains[0].Maps.Count && s.Dormant == 0, $"{s.Live} live, {s.Dormant} dormant");
            Check("status: slots", s.Targets > 0 && rig.Slots.Values.All(x => x.Compose.Wet), s.Targets.ToString());
            Check("a film on the clothes it fits", rig.Slots.Values.Any(x => x.Film != null), "");
            Check("every composite is on its renderer", rig.Slots.Values.All(x => x.Compose.Bound), "");
            Check("no material was written: a slot's material is not the composite", rig.Slots.Values.All(x => WetCompose.BaseOf(x.Compose.MaterialNow()) != x.Compose.Wet), "");
            Check("no member has a slot", rig.Slots.Values.All(x => !HS2Next.Chara.Mosaic.MosaicSet.IsMember(x.Target.Renderer)), "");

            Edit(h, key, WetEditCommand.LabelDry, b => b.Stains[0].T = 0f);
            Check("dry: no composite and no override", RigOf(h, key).Slots.Values.All(x => !x.Compose.Wet && !x.Compose.Bound), "");
            Edit(h, key, WetEditCommand.LabelRemove, b => { b.Stains.Clear(); b.Current = ""; b.Puddles.Clear(); });
            Check("the block empty: no rig", Part(h).Rigs == 0, "");
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            Check("undo brings the rig", Part(h).Rigs == 1, "");
        }
    }

    static void Wf8()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            string a = Stain(h, key, WetPreset.Blood, 2.5f);
            string b = Stain(h, key, WetPreset.Water, 1.5f, false, s => s.Shape.Radius = 1.4f);
            var before = Digests(h, key);
            Check("composites before the save", before.Count > 0, before.Count.ToString());
            var res = St(h, key).Wet.Stains.SelectMany(s => s.Maps.Select(m => m.Res)).ToList();
            string path = SaveScene(h, "wet_wf8");
            LoadScene(h, path);
            string again = h.Session.Model.PreOrder().First(n => n.Kind == NodeKind.Chara).Id;
            h.Rt.ApplyNow();
            var after = Digests(h, again);
            Same("the composites and masks after the load, byte for byte", before, after);
            var rig = RigOf(h, again);
            Check("the maps were decoded", rig.Decoded > 0, rig.Decoded.ToString());
            Check("the block came back", St(h, again).Wet.Stains.Count == 2, "");
            foreach (var r in res)
            {
                var png = h.Session.Model.File.Res.Get(r);
                Check("a picture is a PNG of 8-bit gray", png != null && png.Length > 26 && png[24] == 8 && png[25] == 0, png == null ? "absent" : png[24] + " " + png[25]);
            }
        }
    }

    static void Wf7()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key, WetPreset.Water, 2f);
            string p1 = SaveScene(h, "wet_wf7a");
            LoadPaused(h, p1);
            string p2 = SaveScene(h, "wet_wf7b");
            Check("a stopped stain: scene.json byte-equal", SceneJson(p1).SequenceEqual(SceneJson(p2)), FirstDifference(SceneJson(p1), SceneJson(p2)));

            string k2 = h.Session.Model.PreOrder().First(n => n.Kind == NodeKind.Chara).Id;
            Edit(h, k2, WetEditCommand.LabelStart, b => { b.Stains[0].T = 1f; b.Stains[0].Running = true; });
            string p3 = SaveScene(h, "wet_wf7c");
            LoadPaused(h, p3);
            h.Frames(2);
            string p4 = SaveScene(h, "wet_wf7d");
            Check("a running stain under the pause: scene.json byte-equal", SceneJson(p3).SequenceEqual(SceneJson(p4)), FirstDifference(SceneJson(p3), SceneJson(p4)));
            var w = St(h, h.Session.Model.PreOrder().First(n => n.Kind == NodeKind.Chara).Id).Wet;
            Check("it is still running after the load", w.Stains[0].Running && Math.Abs(w.Stains[0].T - 1f) < 1e-6, w.Stains[0].T.ToString());
        }
    }

    static byte[] SceneJson(string path)
    {
        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        using (var e = zip.GetEntry("scene.json").Open())
        using (var ms = new MemoryStream())
        {
            e.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static void Wb1()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key);
            var c = ClothesSlot(h, key);
            if (!Check("a clothes slot with a stain", c != null, "")) return;
            var t = c.Value.slot.Target;
            string name = UnitMap.ClothesKinds[c.Value.kind];
            var maps = St(h, key).Wet.Stains[0].Maps.Select(m => m.Res).ToList();
            var before = Digests(h, key);
            int composed = RigOf(h, key).Composed;
            SetEdit(h, key, "coord.clothes." + name + ".colors[0].base", new[] { 0.1f, 0.7f, 0.2f, 1f }, "a clothes colour edit");
            h.Rt.ApplyNow();
            var after = Digests(h, key);
            Check("the composite was drawn again", RigOf(h, key).Composed > composed, "");
            Check("the garment's composite changed", before[t.Key] != after[t.Key], t.Key);
            Check("its mask did not", !before.ContainsKey(t.Key + "|mask") || before[t.Key + "|mask"] == after[t.Key + "|mask"], "");
            Check("the stain is on the same renderer", RigOf(h, key).Slots[t.Key].Compose.Bound, "");
            Check("the same maps", St(h, key).Wet.Stains[0].Maps.Select(m => m.Res).SequenceEqual(maps), "");
            Check("no rebuild ran", h.Rt.FindChara(key).EditRebuilds == 0, "");
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            Same("undo of the colour: the composites of before", before, Digests(h, key));

            var rows = h.Call(new { cmd = "chara.edit.list", category = 348, sex = "female", limit = 40 })["rows"] as JArray;
            string pattern = rows?.Select(x => (string)x["ref"]).FirstOrDefault(x => x != null && x.StartsWith("illusion.hs2#", StringComparison.Ordinal) && x != "illusion.hs2#0");
            if (pattern == null) Warn("no pattern row");
            else
            {
                composed = RigOf(h, key).Composed;
                h.Ok(new { cmd = "chara.edit.item", key, path = "coord.clothes." + name + ".colors[0].pattern", @ref = pattern }, "a pattern on the garment");
                h.Rt.ApplyNow();
                var patterned = Digests(h, key);
                Check("a pattern: the composite was drawn again", RigOf(h, key).Composed > composed, "");
                Check("a pattern: its mask did not change", !before.ContainsKey(t.Key + "|mask") || before[t.Key + "|mask"] == patterned[t.Key + "|mask"], "");
                Info("a pattern: the garment's image " + (patterned[t.Key] != before[t.Key] ? "changed" : "stayed: this part shows no pattern"));
                h.Session.Commands.Undo();
                h.Rt.ApplyNow();
                Same("undo of the pattern: the composites of before", before, Digests(h, key));
            }
        }
    }

    static void Wb2()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key);
            var c = ClothesSlot(h, key);
            if (!Check("a clothes slot with a stain", c != null, "")) return;
            string tkey = c.Value.slot.Target.Key, name = UnitMap.ClothesKinds[c.Value.kind], path = "coord.clothes." + name + ".item";
            var oldRenderer = c.Value.slot.Target.Renderer;
            var filmed = c.Value.slot.Film?.Renderer;
            var before = Digests(h, key);
            var s0 = Part(h).StatusOf(key);
            int docMaps = St(h, key).Wet.Stains[0].Maps.Count;
            string row = SwapToPart(h, key, path, c.Value.kind);
            if (!Check("another row of that kind, with a part that draws", row != null, name)) return;
            var s1 = Part(h).StatusOf(key);
            Check("the old renderer is gone", !oldRenderer, "");
            Check("the old film is gone", filmed == null || !filmed, "");
            Check("its maps went dormant", s1.Dormant > 0 && s1.Live < s0.Live, $"live {s0.Live} -> {s1.Live}, dormant {s1.Dormant}");
            Check("the document keeps them", St(h, key).Wet.Stains[0].Maps.Count == docMaps, "");
            Check("no slot on that address", !RigOf(h, key).Slots.ContainsKey(tkey), "");
            var fresh = h.Rt.FindChara(key).Context.Clothes[c.Value.kind];
            Check("the new part is dry: no block on its renderers", fresh == null || fresh.GetComponentsInChildren<Renderer>(true).All(r => !r.HasPropertyBlock()), "");

            // apply again: the new part gets a map of its own, the dormant one stays in the document
            var block = St(h, key).Wet.Clone();
            string why = Part(h).Bake(key, block, block.Stains[0], true);
            Check("apply again: the bake ran", why == null, why ?? "");
            bool reached = block.Stains[0].Maps.Count > docMaps;
            Info("apply again: maps " + docMaps + " -> " + block.Stains[0].Maps.Count);
            var ra = h.Session.Commands.Execute(new WetEditCommand(key, block, WetEditCommand.LabelEdit));
            Check("apply again: one edit", ra.Status == CommandStatus.Ok, ra.ToString());
            h.Rt.ApplyNow();
            var sa = Part(h).StatusOf(key);
            Check("apply again: what was live stays, the part gets its map if the stain reaches it", sa.Live == s1.Live + (reached ? block.Stains[0].Maps.Count - docMaps : 0) && sa.Dormant == s1.Dormant, $"live {sa.Live}, dormant {sa.Dormant}");
            if (reached)
                Check("apply again: the new part shows the stain", RigOf(h, key).Slots.Values.Any(x => x.Target.Address.StartsWith("clothes:" + c.Value.kind + "/", StringComparison.Ordinal) && x.Compose.Bound), "");
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            var sb = Part(h).StatusOf(key);
            Check("undo of apply again: the new part is dry", sb.Live == s1.Live && sb.Dormant == s1.Dormant, $"live {sb.Live}, dormant {sb.Dormant}");

            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            var s2 = Part(h).StatusOf(key);
            Check("undo: live again", s2.Live == s0.Live && s2.Dormant == 0, $"live {s2.Live}, dormant {s2.Dormant}");
            Same("undo: the composites of before", before, Digests(h, key));

            h.Ok(new { cmd = "chara.edit.item", key, path, @ref = row }, "swapped again");
            h.Rt.ApplyNow();
            string cur = (string)h.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            string back = (string)h.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];
            h.Session.Commands.Redo();
            h.Rt.ApplyNow();
            h.Ok(new { cmd = "chara.edit.item", key, path, @ref = back }, "the same part put back by hand");
            h.Rt.ApplyNow();
            // a swap by hand takes the row's default colours, so the garment's image may be another one: the stain on it is the same
            var again = Digests(h, key);
            var s3 = Part(h).StatusOf(key);
            Check("the same part back by hand: every map live", s3.Live == s0.Live && s3.Dormant == 0, $"live {s3.Live}, dormant {s3.Dormant}");
            Check("the same part back by hand: the same mask", !before.ContainsKey(tkey + "|mask") || before[tkey + "|mask"] == again[tkey + "|mask"], "");
            Check("the same part back by hand: every other image as before", before.Keys.Where(k => k != tkey).All(k => again.TryGetValue(k, out var v) && v == before[k]), Differs(before, again) ?? "");
            Info("rows " + back + " and " + cur);

            // the state of a part
            var slot = RigOf(h, key).Slots[tkey];
            h.Ok(new { cmd = "chara.clothes", key, states = new Dictionary<string, object> { [c.Value.kind.ToString()] = 2 } }, "the part off");
            h.Frames(1);
            slot = RigOf(h, key).Slots[tkey];
            bool hidden = !slot.Target.Renderer.gameObject.activeInHierarchy || !slot.Target.Renderer.enabled;
            Check("off: the garment is hidden", hidden, "");
            Check("off: the film follows", slot.Film == null || !slot.Film.Renderer.enabled, "");
            h.Ok(new { cmd = "chara.clothes", key, states = new Dictionary<string, object> { [c.Value.kind.ToString()] = 0 } }, "the part on");
            h.Frames(1);
            slot = RigOf(h, key).Slots[tkey];
            Check("on: the film follows", slot.Film == null || slot.Film.Renderer.enabled, "");
            int halves = 0;
            foreach (int k in RigOf(h, key).Slots.Values.Where(x => x.Target.Kind == WetKinds.Clothes).Select(x => int.Parse(x.Target.Address.Substring(8, 1))).Distinct().ToList())
            {
                h.Ok(new { cmd = "chara.clothes", key, states = new Dictionary<string, object> { [k.ToString()] = 1 } }, "kind " + k + " to half");
                h.Frames(1);
                var live = h.Rt.FindChara(key);
                if (live.StateRealizer.Realized[k] == 1)
                {
                    halves++;
                    foreach (var x in RigOf(h, key).Slots.Values.Where(x => x.Film != null))
                    {
                        var r = x.Target.Renderer;
                        Check("half: a film follows its garment: " + x.Target.Key, x.Film.Renderer.enabled == (r.enabled && r.gameObject.activeInHierarchy), "");
                    }
                }
                h.Ok(new { cmd = "chara.clothes", key, states = new Dictionary<string, object> { [k.ToString()] = 0 } }, "kind " + k + " back on");
                h.Frames(1);
            }
            Info("kinds with a half state: " + halves);
            if (halves == 0) Warn("no stained kind of this card has a half state: the state ran as off and on only");
            Same("on: the composites of before it went off", again, Digests(h, key));
        }
    }

    const string SfwCoord = "ガーリーブラウス.png";

    static void Wb3()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key);
            var before = Digests(h, key);
            var s0 = Part(h).StatusOf(key);
            string coord = Path.Combine(h.App.Paths.Hs2UserData ?? "", "coordinate", "female", SfwCoord);
            if (!File.Exists(coord)) { Warn("the SFW coordinate card is missing: not run"); return; }
            h.Ok(new { cmd = "chara.edit.coordinate", key, file = SfwCoord, mode = "clothes" }, "a coordinate applied");
            h.Rt.ApplyNow();
            var s1 = Part(h).StatusOf(key);
            Check("parts of another signature go dormant, none is lost", s1.Live + s1.Dormant == s0.Live, $"{s1.Live} live, {s1.Dormant} dormant of {s0.Live}");
            Check("the character's real parts answer: no slot on a dead renderer", RigOf(h, key).Slots.Values.All(x => x.Target.Renderer), "");
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            Same("undo: the composites of before", before, Digests(h, key));
        }
    }

    static void Wb4()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key);
            var before = Digests(h, key);
            var s0 = Part(h).StatusOf(key);

            h.Ok(new { cmd = "chara.change", key, path = femaleRel, sex = "female" }, "chara.change to the same card");
            h.Rt.ApplyNow();
            var s1 = Part(h).StatusOf(key);
            Check("chara.change: every map live", s1.Live == s0.Live && s1.Dormant == 0, $"{s1.Live} / {s0.Live}");
            Same("chara.change: the same composites", before, Digests(h, key));

            var real = h.Rt.FindChara(key);
            Check("a rebuild is taken", real.RequestPluginRebuild("test.wet"), "");
            h.Rt.ApplyNow();
            Same("a plugin's rebuild: the same composites", before, Digests(h, key));
            Check("after it the overrides are on the new renderers", RigOf(h, key).Slots.Values.All(x => x.Compose.Bound && x.Target.Renderer), "");

            // a skin edit: the composite takes the new image
            var skin = RigOf(h, key).Slots.Values.FirstOrDefault(x => x.Target.Kind == WetKinds.Skin);
            if (skin != null)
            {
                string skey = skin.Target.Key;
                SetEdit(h, key, "chara.body.skin.color", new[] { 0.9f, 0.6f, 0.5f, 1f }, "a skin colour edit");
                h.Rt.ApplyNow();
                Check("a skin edit: the skin's composite is drawn from the new image", Digests(h, key)[skey] != before[skey], skey);
                h.Session.Commands.Undo();
                h.Rt.ApplyNow();
                Same("undo of the skin edit", before, Digests(h, key));
            }
            else Warn("the stain reached no skin");

            // a shape and the bust: the same mesh, the stain stays live
            var snap = Digests(h, key);
            SetEdit(h, key, "chara.body.bust.softness", 0.9, "a bust edit");
            h.Rt.ApplyNow();
            var s2 = Part(h).StatusOf(key);
            Check("a body edit: every map live", s2.Live == s0.Live && s2.Dormant == 0, $"{s2.Live} / {s0.Live}");
            Same("a body edit: the same composites", snap, Digests(h, key));

            // a head swap: the signature decides
            string head = OtherRow(h, key, "chara.face.head");
            if (head == null) Warn("no other head row");
            else
            {
                h.Ok(new { cmd = "chara.edit.item", key, path = "chara.face.head", @ref = head }, "a head swap");
                h.Rt.ApplyNow();
                var s3 = Part(h).StatusOf(key);
                Check("a head swap: nothing lost", s3.Live + s3.Dormant == s0.Live, $"{s3.Live} live, {s3.Dormant} dormant of {s0.Live}");
                Check("a head swap: no slot on a dead renderer", RigOf(h, key).Slots.Values.All(x => x.Target.Renderer), "");
                h.Session.Commands.Undo();
                h.Rt.ApplyNow();
                var s4 = Part(h).StatusOf(key);
                Check("undo of the head swap: every map live", s4.Live == s0.Live && s4.Dormant == 0, $"{s4.Live} / {s0.Live}");
            }
        }
    }

    static void Wb6()
    {
        using (var h = NewHost())
        {
            string a = LoadChara(h);
            string b = LoadChara(h, true);
            Stain(h, a);
            Stain(h, b, WetPreset.Water);
            var da = Digests(h, a);
            var db = Digests(h, b);
            Check("both are stained", da.Count > 0 && db.Count > 0, da.Count + " and " + db.Count);
            int composedA = RigOf(h, a).Composed;
            Check("a rebuild of the second", h.Rt.FindChara(b).RequestPluginRebuild("test.wet"), "");
            h.Rt.ApplyNow();
            Same("the other's composites, byte for byte", da, Digests(h, a));
            Check("the other was not drawn again", RigOf(h, a).Composed == composedA, "");
            Same("the rebuilt one's composites", db, Digests(h, b));
        }
    }

    static void Wb8()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key);
            var c = ClothesSlot(h, key);
            if (!Check("a clothes slot with a stain", c != null, "")) return;
            string tkey = c.Value.slot.Target.Key, name = UnitMap.ClothesKinds[c.Value.kind];
            var before = Digests(h, key);
            var mat = c.Value.slot.Target.Material;
            string matName = HS2Next.Chara.Ext.MaterialEditorApplier.Fmt(mat.name);
            int rebuilds = h.Rt.FindChara(key).EditRebuilds;

            var image = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pix = Enumerable.Repeat(new Color32(30, 60, 200, 255), 64).ToArray();
            image.SetPixels32(pix);
            image.Apply();
            var png = image.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(image);
            string res = h.Session.Model.File.Res.Add(png);
            var set = h.Ok(new
            {
                cmd = "chara.edit.materials.set", key, target = "clothes:" + c.Value.kind, material = matName, property = "MainTex", type = "texture", image = res,
            }, "a material edit of the main texture");
            h.Rt.ApplyNow();
            var edited = Digests(h, key);
            Check("above the edit: the composite shows the edited image under the stain", edited[tkey] != before[tkey], "");
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            Same("the edit removed: the baseline image with the stain on it", before, Digests(h, key));
            Check("no replacement ran", h.Rt.FindChara(key).EditRebuilds == rebuilds, "");
            Check("the override is still on", RigOf(h, key).Slots[tkey].Compose.Bound, "");
        }
    }

    static void Wb9()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            h.Frames(2);
            var s = new HS2Next.Save.ThumbnailSettings();
            var real = h.Rt.FindChara(key);
            var dry = CharaThumbnail.Capture(real, s);
            var dry2 = CharaThumbnail.Capture(real, s);
            Check("the thumbnail is stable", dry != null && dry.SequenceEqual(dry2), "");
            Stain(h, key);
            real = h.Rt.FindChara(key);
            var wet = CharaThumbnail.Capture(real, s);
            Check("the stained character's thumbnail equals the unstained bytes", wet != null && wet.SequenceEqual(dry), "");
            Check("after it the overrides are back", RigOf(h, key).Slots.Values.All(x => x.Compose.Bound), "");
            Check("and the films", RigOf(h, key).Slots.Values.All(x => x.Film == null || x.Film.Renderer.enabled), "");
        }
    }

    static void Wb11()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key, WetPreset.Water, 3f, true);
            h.Frames(120);
            var rig = RigOf(h, key);
            float t0 = Part(h).TimeOf(key, "s1");
            Check("the clock runs", t0 > 3f, t0.ToString());

            h.Ok(new { cmd = "obj.visible", key, visible = false }, "hidden");
            h.Frames(3);
            var film = rig.Slots.Values.FirstOrDefault(x => x.Film != null)?.Film?.Renderer;
            Check("hidden: the film with it", film == null || film.forceRenderingOff, "");
            Check("hidden: drops and puddles", rig.Drops.Hidden && rig.Puddles.Hidden, "");
            Check("hidden: the clock runs", Part(h).TimeOf(key, "s1") > t0, "");
            h.Ok(new { cmd = "obj.visible", key, visible = true }, "shown");
            h.Frames(2);

            h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
            float t1 = Part(h).TimeOf(key, "s1");
            h.Frames(5);
            Check("paused: the clock stands", Part(h).TimeOf(key, "s1") == t1, "");
            h.Ok(new { cmd = "scene.step", n = 6, dt = 0.05 }, "six steps");
            Check("a step moves it by its dt", Math.Abs(Part(h).TimeOf(key, "s1") - (t1 + .3f)) < 1e-4, (Part(h).TimeOf(key, "s1") - t1).ToString());

            h.Ok(new { cmd = "scene.pause", paused = false }, "running");
            float before = Part(h).TimeOf(key, "s1");
            h.Ok(new { cmd = "delete", key }, "deleted");
            h.Frames(2);
            var buried = RigOf(h, key);
            Check("deleted: the rig waits", buried != null, "");
            Check("deleted: drops and puddles hidden", buried != null && buried.Drops.Hidden && buried.Puddles.Hidden, "");
            Check("deleted: the film is off", buried == null || buried.Slots.Values.All(x => x.Film == null || !x.Film.Renderer.enabled), "");
            h.Session.Commands.Undo();
            h.Frames(2);
            Check("undo: shown again", !RigOf(h, key).Drops.Hidden, "");
            Check("undo: the clock is where it was", Part(h).TimeOf(key, "s1") >= before, Part(h).TimeOf(key, "s1").ToString());

            h.Ok(new { cmd = "delete", key }, "deleted again");
            h.Frames(2);
            h.Session.Commands.ClearHistory();
            h.Frames(2);
            Check("forgotten: the rig is gone", RigOf(h, key) == null, "");
            Check("forgotten: nothing is left under the wet root", Part(h).Root == null || Part(h).Root.childCount == 0, Part(h).Root ? Part(h).Root.childCount.ToString() : "");
            key = LoadChara(h);
            Stain(h, key, WetPreset.Water, 3f, true);
            h.Frames(120);

            h.Ok(new { cmd = "scene.new" }, "scene.new");
            h.Rt.ApplyNow();
            Check("a new scene: no rig", Part(h).Rigs == 0, "");
            Check("a new scene: no plane, not subscribed", HS2Next.PlanarMirrors.Planes == 0 && !HS2Next.PlanarMirrors.Subscribed, "");
        }
    }

    static void Wb15()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
            string id = Stain(h, key, WetPreset.Water, 0f, true, s => { s.Shape.Spread = .2f; s.Drips.Tail = .1f; });
            h.Ok(new { cmd = "scene.step", n = 30, dt = Dt }, "half a second");
            float t = Part(h).TimeOf(key, id);
            var run = RigOf(h, key).Runs[0];
            Check("it stops by itself at the end of its tail", !run.Running && t >= .3f && t < .3f + 2 * Dt, t.ToString());
            h.Ok(new { cmd = "scene.step", n = 30, dt = Dt }, "half a second more");
            Check("stopped, its clock stands", Part(h).TimeOf(key, id) == t, "");
            Part(h).CaptureNow(key);
            Check("the capture says it stopped", !St(h, key).Wet.Stains[0].Running, "");

            Edit(h, key, WetEditCommand.LabelStart, b =>
            {
                var s = b.Stains[0];
                s.T = 0f; s.Running = true; s.Loop = true; s.Hold = .1f;
                b.Puddles.Add(new WetPuddleEntry { Stain = id, Center = new Vec3(1f, 0f, 1f), Area = .3f, Seed = 3f });
            });
            Check("the loop's puddle is there", Part(h).StatusOf(key).Puddles == 1, "");
            h.Ok(new { cmd = "scene.step", n = 20, dt = Dt }, "up to the hold");
            Check("in its hold it goes on", RigOf(h, key).Runs[0].Running && Part(h).TimeOf(key, id) > .3f, Part(h).TimeOf(key, id).ToString());
            h.Ok(new { cmd = "scene.step", n = 10, dt = Dt }, "past the hold");
            float wrapped = Part(h).TimeOf(key, id);
            Check("past its hold it starts over", RigOf(h, key).Runs[0].Running && wrapped < .2f, wrapped.ToString());
            Check("the loop dried its puddle", Part(h).StatusOf(key).Puddles == 0, Part(h).StatusOf(key).Puddles.ToString());

            Edit(h, key, WetEditCommand.LabelKeep, b => { b.Stains[0].Kept = true; b.Stains[0].Running = false; b.Stains[0].Loop = false; b.Stains[0].T = .1f; });
            string second = Stain(h, key, WetPreset.Blood, 0f, true);
            h.Ok(new { cmd = "scene.step", n = 12, dt = Dt }, "a fifth of a second");
            Check("a kept stain stands", Part(h).TimeOf(key, id) == .1f, Part(h).TimeOf(key, id).ToString());
            Check("the next one runs on top of it", Part(h).TimeOf(key, second) > .15f, Part(h).TimeOf(key, second).ToString());
        }
    }

    static void Wb14()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
            string id = Stain(h, key, WetPreset.Blood, 2f, true);
            Edit(h, key, WetEditCommand.LabelEdit, b => b.Puddles.Add(new WetPuddleEntry { Stain = id, Center = new Vec3(1f, 0f, 1f), Area = .3f, Seed = 3f }));
            var before = Digests(h, key);
            var res = St(h, key).Wet.Stains[0].Maps.Select(m => m.Res).ToList();
            Check("the source has a puddle", Part(h).StatusOf(key).Puddles == 1, "");

            h.Rt.Capture.CaptureSubtrees(new[] { key });
            var r = h.Session.Duplicate(new[] { key });
            Check("a duplicate", r.Status == CommandStatus.Ok, r.ToString());
            h.Rt.ApplyNow();
            string copy = h.Session.Model.PreOrder().First(n => n.Kind == NodeKind.Chara && n.Id != key).Id;
            void Copied(string k, string what)
            {
                var w = St(h, k).Wet;
                Check(what + ": the stain and its maps come along", w.Stains.Count == 1 && w.Stains[0].Maps.Select(m => m.Res).SequenceEqual(res), "");
                Check(what + ": its moment too", w.Stains[0].T == 2f, w.Stains[0].T.ToString());
                Check(what + ": not running", !w.Stains[0].Running, "");
                Check(what + ": no puddle", w.Puddles.Count == 0 && Part(h).StatusOf(k).Puddles == 0, "");
                Check(what + ": every map live", Part(h).StatusOf(k).Dormant == 0 && Part(h).StatusOf(k).Live == res.Count, "");
                Same(what + ": the same composites", before, Digests(h, k));
            }
            Copied(copy, "duplicate");
            Check("the source is untouched", St(h, key).Wet.Stains[0].Running && St(h, key).Wet.Puddles.Count == 1, "");

            h.Ok(new { cmd = "delete", key = copy }, "the copy deleted");
            string path = SaveScene(h, "wet_wb14");
            var ri = h.Ok(new { cmd = "scene.import", path }, "scene.import");
            h.Rt.ApplyNow();
            string imported = h.Session.Model.PreOrder().First(n => n.Kind == NodeKind.Chara && n.Id != key).Id;
            Copied(imported, "an imported scene");
        }
    }

    /// <summary>A character standing still under the pause, a running stain, and a fixed number of steps; the digests and the drops' hash.</summary>
    static (SortedDictionary<string, string> images, ulong emitted, int count, List<WetPuddleEntry> puddles) RunSteps(Host h, int n)
    {
        h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
        string key = LoadChara(h);
        h.Ok(new { cmd = "chara.motion", key, paused = true }, "the character stands");
        Stain(h, key, WetPreset.Blood, 0f, true, s => { s.Shape.Spread = 1.5f; s.Drips.Tail = 1f; s.Drips.Rate = 40f; });
        h.Ok(new { cmd = "scene.step", n, dt = Dt }, "the steps");
        h.Frames(1);
        var rig = RigOf(h, key);
        var entries = rig.Puddles.ToEntries();
        return (Digests(h, key), rig.Drops.EmitHash, rig.Drops.Emitted, entries);
    }

    static void Wd8()
    {
        (SortedDictionary<string, string> images, ulong emitted, int count, List<WetPuddleEntry> puddles) a, b;
        using (var h = NewHost()) a = RunSteps(h, 150);
        using (var h = NewHost()) b = RunSteps(h, 150);
        Check("drops left", a.count > 0, a.count.ToString());
        Check("the same number of drops", a.count == b.count, a.count + " / " + b.count);
        Check("the same drops, step by step", a.emitted == b.emitted, a.emitted.ToString("x") + " / " + b.emitted.ToString("x"));
        Same("the same composites", a.images, b.images);
        Check("puddles formed", a.puddles.Count > 0, a.puddles.Count.ToString());
        Check("the same puddles", a.puddles.Count == b.puddles.Count && a.puddles.Zip(b.puddles, (x, y) => x.Area == y.Area && x.Center.Equals(y.Center)).All(x => x), "");
    }

    static void Wd8a()
    {
        using (var h = NewHost())
        {
            h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
            string key = LoadChara(h);
            h.Ok(new { cmd = "chara.motion", key, paused = true }, "the character stands");
            Stain(h, key, WetPreset.Blood, 0f, true, s => { s.Shape.Spread = 1.5f; s.Drips.Tail = 1f; s.Drips.Rate = 40f; });
            h.Ok(new { cmd = "scene.step", n = 60, dt = Dt }, "the first steps");

            // what the second half emits straight on
            string path = SaveScene(h, "wet_wd8a");
            RigOf(h, key).Drops.ResetHash();
            h.Ok(new { cmd = "scene.step", n = 60, dt = Dt }, "the second half");
            var rig = RigOf(h, key);
            ulong straight = rig.Drops.EmitHash;
            int count = rig.Drops.Emitted;
            Check("the second half emits", count > 0, count.ToString());

            LoadScene(h, path);
            h.Ok(new { cmd = "scene.pause", paused = true }, "paused after the load");
            string k2 = h.Session.Model.PreOrder().First(n => n.Kind == NodeKind.Chara).Id;
            h.Ok(new { cmd = "chara.motion", key = k2, paused = true }, "the character stands");
            h.Frames(1);
            h.Ok(new { cmd = "scene.step", n = 60, dt = Dt }, "the second half after a load");
            var again = RigOf(h, k2);
            Check("the same number of drops", again.Drops.Emitted == count, again.Drops.Emitted + " / " + count);
            Check("the same drops, step by step", again.Drops.EmitHash == straight, again.Drops.EmitHash.ToString("x") + " / " + straight.ToString("x"));
        }
    }
}
