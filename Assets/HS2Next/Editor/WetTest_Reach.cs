// WetTest (Phase 6.6a): accessories, shoes and gloves take a stain only by their switches, on BR-Chan with real rows of the base game.
//   WT1  off by default; on, their parts are targets with canonical addresses, a member never is; a kind switched on takes the stains there are in one history entry, undo
//        takes it back; off and on again brings the same maps back with no new bake; a static accessory has no film, a skinned one of a film family has; the switches and the
//        maps come through a save, a load and a rebuild; a removed accessory goes dormant; a kept stain takes nothing new
using System.Collections.Generic;
using System.Linq;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class WetTest
{
    const int WtGloves = 4, WtShoes = 7;
    const float WtRadius = 10f;

    static bool WtDraws(GameObject part) => part && part.GetComponentsInChildren<Renderer>(false).Any(r => r.enabled);

    // a kind on the character that draws, swapped to a row of the base game when the card has none
    static bool WtWear(Host h, string key, int kind)
    {
        if (WtDraws(h.Rt.FindChara(key).Context.Clothes[kind])) return true;
        return SwapToPart(h, key, "coord.clothes." + UnitMap.ClothesKinds[kind] + ".item", kind) != null;
    }

    // the first accessory of the base game drawn by such a renderer with a readable mesh and a usable slot; its id, or null
    static string WtAccessory(Host h, string key, bool skinned)
    {
        foreach (int category in new[] { 353, 352, 351, 354, 355, 356, 357, 358, 359, 360, 361, 362, 363 })
        {
            var list = h.Call(new { cmd = "chara.edit.list", category, sex = "female", limit = 60 })["rows"] as JArray;
            if (list == null) continue;
            foreach (string row in list.Select(x => (string)x["ref"]).Where(x => x != null && x.StartsWith("illusion.hs2#", System.StringComparison.Ordinal)).Take(30))
            {
                string id = (string)h.Call(new { cmd = "chara.edit.accessory", key, op = "add", category, @ref = row })["accessory"];
                if (id == null) continue;
                h.Rt.ApplyNow();
                bool fit = WetTargets.Collect(h.Rt.FindChara(key), false, false, true).Any(t =>
                    t.Address.StartsWith("acc:" + id + "/", System.StringComparison.Ordinal) && t.State == WetTargetStates.Ok && t.Shown &&
                    t.Renderer is SkinnedMeshRenderer == skinned && WetTargets.MeshOf(t.Renderer) && WetTargets.MeshOf(t.Renderer).isReadable &&
                    (!skinned || WetFilm.FamilyOf(t.Material) != WetFilmFamily.None));
                if (fit) return id;
                h.Session.Commands.Undo();
                h.Rt.ApplyNow();
            }
        }
        return null;
    }

    static void WtSet(Host h, string key, string path, bool value) =>
        h.Ok(new { cmd = "wet.set", key, values = new Dictionary<string, object> { [path] = value } }, path + " " + value);

    static List<WetRig.Slot> WtSlots(Host h, string key, string prefix) =>
        RigOf(h, key).Slots.Values.Where(s => s.Target.Address.StartsWith(prefix, System.StringComparison.Ordinal)).ToList();

    static int WtMaps(Host h, string key, string prefix) =>
        St(h, key).Wet.Stains.Sum(s => s.Maps.Count(m => m.Renderer.StartsWith(prefix, System.StringComparison.Ordinal)));

    static string WtRes(Host h, string key) => string.Join(",", St(h, key).Wet.Stains.SelectMany(s => s.Maps).Select(m => m.Res).OrderBy(x => x, System.StringComparer.Ordinal));

    static void Wt1()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            if (!Check("the character wears gloves", WtWear(h, key, WtGloves), "")) return;
            if (!Check("the character wears shoes", WtWear(h, key, WtShoes), "")) return;
            string fixedOne = WtAccessory(h, key, false), skinnedOne = WtAccessory(h, key, true);
            if (!Check("an accessory on a static renderer", fixedOne != null, "")) return;
            if (!Check("an accessory on a skinned renderer of a film family", skinnedOne != null, "")) return;
            string pf = "acc:" + fixedOne + "/", ps = "acc:" + skinnedOne + "/", pg = "clothes:" + WtGloves + "/", pb = "clothes:" + WtShoes + "/";
            var real = h.Rt.FindChara(key);
            Info("accessories worn " + real.Context.Accessories.Count(s => s != null && s.Object));

            // what the switches let in
            var off = WetTargets.Collect(real);
            Check("off by default: no accessory is a target", off.All(t => t.Kind != WetKinds.Accessory), "");
            Check("off by default: gloves and shoes are no targets", !off.Any(t => t.Address.StartsWith(pg) || t.Address.StartsWith(pb)), "");
            var all = WetTargets.Collect(real, new WetTargetSwitches { Accessories = true, Shoes = true, Gloves = true });
            Info("targets with the switches on: " + string.Join(", ", all.GroupBy(t => t.Kind).Select(g => g.Key + " " + g.Count())));
            Check("on: both accessories, gloves and shoes are targets", new[] { pf, ps, pg, pb }.All(p => all.Any(t => t.Address.StartsWith(p))), "");
            Check("on: the addresses are canonical", all.All(t => PoseAddress.IsCanonical(PoseAddressForm.Blend, t.Address)), "");
            Check("on: each switch lets in its own kind alone",
                !WetTargets.Collect(real, new WetTargetSwitches { Shoes = true }).Any(t => t.Address.StartsWith(pg) || t.Kind == WetKinds.Accessory) &&
                !WetTargets.Collect(real, new WetTargetSwitches { Gloves = true }).Any(t => t.Address.StartsWith(pb) || t.Kind == WetKinds.Accessory) &&
                !WetTargets.Collect(real, new WetTargetSwitches { Accessories = true }).Any(t => t.Address.StartsWith(pg) || t.Address.StartsWith(pb)), "");

            // a stain made with them off stays off them; the default shape stretches up and sideways, so this one is drawn to reach the whole body
            string id = Stain(h, key, WetPreset.Water, null, false, s => { s.Shape.Radius = WtRadius; s.Shape.Up = 0.5f; s.Shape.Down = 0.1f; s.Shape.Front = s.Shape.Back = s.Shape.Side = 0.3f; s.Shape.Outer = 0f; s.Shape.Noise = 0f; s.Name = "wide"; });
            Check("the stain lies on the body", St(h, key).Wet.Stains[0].Maps.Count > 0, "");
            Check("with the switches off no map lies on them", new[] { pf, ps, pg, pb }.All(p => WtMaps(h, key, p) == 0), "");

            // switched on: the stain takes them in one entry
            int mapsOff = St(h, key).Wet.Stains[0].Maps.Count;
            string resOff = WtRes(h, key);
            WtSet(h, key, "targets.accessories", true);
            h.Rt.ApplyNow();
            Info("maps after accessories on: static " + WtMaps(h, key, pf) + ", skinned " + WtMaps(h, key, ps) + "; all " + St(h, key).Wet.Stains[0].Maps.Count);
            Check("switched on, the static accessory has a map", WtMaps(h, key, pf) > 0, "");
            Check("switched on, the skinned accessory has a map", WtMaps(h, key, ps) > 0, "");
            Check("what lay before keeps its maps", resOff.Split(',').All(r => WtRes(h, key).Contains(r)), "");
            var fixedSlots = WtSlots(h, key, pf);
            var skinnedSlots = WtSlots(h, key, ps);
            Check("the static accessory shows the stain", fixedSlots.Count > 0 && fixedSlots.All(s => s.Compose.Wet && s.Target.Renderer.HasPropertyBlock()), fixedSlots.Count.ToString());
            Check("the skinned accessory shows the stain", skinnedSlots.Count > 0 && skinnedSlots.All(s => s.Compose.Wet && s.Target.Renderer.HasPropertyBlock()), skinnedSlots.Count.ToString());
            Check("a static accessory has no film", fixedSlots.All(s => s.Film == null), "");
            Check("a skinned accessory of a film family has one", skinnedSlots.Any(s => s.Film != null), "");
            Check("wet.status counts the static one under noFilm", Part(h).StatusOf(key).NoFilm >= fixedSlots.Count, Part(h).StatusOf(key).NoFilm.ToString());
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            Check("undo: one entry took the switch and its maps back", !St(h, key).Wet.Targets.Accessories && WtRes(h, key) == resOff && WtSlots(h, key, "acc:").Count == 0, "");
            Check("undo: nothing shows on the accessory", WetTargets.Collect(real, false, false, true).Where(t => t.Address.StartsWith(pf)).All(t => !t.Renderer.HasPropertyBlock()), "");
            h.Session.Commands.Redo();
            h.Rt.ApplyNow();
            Check("redo brings them back", St(h, key).Wet.Targets.Accessories && WtSlots(h, key, pf).Count == fixedSlots.Count, "");

            // off and on again: the maps come back, no bake
            string resOn = WtRes(h, key);
            var s0 = Part(h).StatusOf(key);
            WtSet(h, key, "targets.accessories", false);
            h.Rt.ApplyNow();
            var s1 = Part(h).StatusOf(key);
            Check("switched off: the accessories are dry, their maps stay in the document", WtSlots(h, key, "acc:").Count == 0 && WtRes(h, key) == resOn && s1.Dormant > 0 && s1.Live < s0.Live, $"live {s0.Live} -> {s1.Live}, dormant {s1.Dormant}");
            WtSet(h, key, "targets.accessories", true);
            h.Rt.ApplyNow();
            var s2 = Part(h).StatusOf(key);
            Check("switched on again: the same maps, none baked twice", WtRes(h, key) == resOn && s2.Live == s0.Live && s2.Dormant == s0.Dormant, $"live {s2.Live} / {s0.Live}, dormant {s2.Dormant}");

            // shoes and gloves
            WtSet(h, key, "targets.shoes", true);
            WtSet(h, key, "targets.gloves", true);
            h.Rt.ApplyNow();
            Info("maps: shoes " + WtMaps(h, key, pb) + ", gloves " + WtMaps(h, key, pg));
            Check("shoes take the stain", WtMaps(h, key, pb) > 0 && WtSlots(h, key, pb).Count > 0, "");
            Check("gloves take the stain", WtMaps(h, key, pg) > 0 && WtSlots(h, key, pg).Count > 0, "");

            // a save, a load and a rebuild
            var live = Part(h).StatusOf(key).Live;
            var digests = Digests(h, key);
            string path = SaveScene(h, "wet_wt1");
            LoadScene(h, path);
            string k2 = h.Session.Model.PreOrder().First(n => n.Kind == NodeKind.Chara).Id;
            var t2 = St(h, k2).Wet.Targets;
            Check("load: the switches came through", t2.Accessories && t2.Shoes && t2.Gloves, "");
            var s3 = Part(h).StatusOf(k2);
            Check("load: every map live", s3.Live == live && s3.Dormant == 0, $"{s3.Live} / {live}, dormant {s3.Dormant}");
            Same("load: the same composites", digests, Digests(h, k2));
            var real2 = h.Rt.FindChara(k2);
            Check("a rebuild is taken", real2.RequestPluginRebuild("test.wet"), "");
            h.Rt.ApplyNow();
            var s4 = Part(h).StatusOf(k2);
            Check("rebuild: every map live", s4.Live == live && s4.Dormant == 0, $"{s4.Live} / {live}, dormant {s4.Dormant}");

            // a removed accessory
            h.Ok(new { cmd = "chara.edit.accessory", key = k2, op = "remove", accessory = fixedOne }, "the accessory removed");
            h.Rt.ApplyNow();
            var s5 = Part(h).StatusOf(k2);
            Check("a removed accessory: its maps go dormant, the rest stays", s5.Dormant > 0 && s5.Live < live && WtSlots(h, k2, pf).Count == 0, $"live {s5.Live}, dormant {s5.Dormant}");
            h.Session.Commands.Undo();
            h.Rt.ApplyNow();
            var s6 = Part(h).StatusOf(k2);
            Check("undo: it shows the stain again", s6.Live == live && s6.Dormant == 0, $"live {s6.Live}, dormant {s6.Dormant}");

            // a kept stain takes no further edit
            h.Ok(new { cmd = "wet.keep", key = k2 }, "the stain kept");
            h.Rt.ApplyNow();
            WtSet(h, k2, "targets.shoes", false);
            string resKept = WtRes(h, k2);
            WtSet(h, k2, "targets.shoes", true);
            Check("a kept stain takes nothing new", WtRes(h, k2) == resKept, "");

            // a member is never a target
            var r = WetTargets.Collect(h.Rt.FindChara(k2), false, false, true).First(t => t.Address.StartsWith(ps)).Renderer;
            Check("the accessory's renderer can be a member", MosaicSet.Add(r), "");
            Check("a member is no target, whatever the switches say", !WetTargets.Collect(h.Rt.FindChara(k2), false, false, true).Any(t => t.Renderer == r), "");
        }
    }
}
