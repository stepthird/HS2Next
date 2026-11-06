// Skin layers and body-part replacement inside BridgeCoreTest's plugin group (the KB<n> checks of the Phase 6.1 plugin
// group), headless, with the test plugin stp3.test.bodyprobe (Tests/Plugins/BodyProbe) as the only loaded plugin of the plugin
// group's staged folder Spike/out/plugins/core (the other candidates on the host's disabled list; staged with the set when the
// probe is missing or older than its source or the compiled Api, or with -kbStage). Every plugin-free reference is built
// before the probe's host loads (build hooks are process-wide). Targets: the clothed SFW visual targets of Tools/targets.json
// (BR-Chan, HERO107). Numbers, names and counts only: no image is written, read back or shown, and no check supplies or reads
// a part of the mosaic region.
//   KB1  a BodyMesh PNG layer (and a texture layer): the body creator's layers +1 (+2), its rebuilds and the editor's flushes
//        as the plugin-free build, one CharaBuilt (no rebuild), the layer in the build statistics and log as <guid>/<id>;
//        with the card's plugin data off the plugin's layer is still composited
//   KB2  stage and id rules: the BodyMesh points throw at Textures and Final; bytes that are no image -> false; removing an
//        id the plugin never set -> false
//   KB3  the body renderer replaced by the copy with the hands scaled by 1.05 (BR-Chan, HERO107): Replaced, the drawn
//        vertices are the copy's, the hand vertices at 1.05 of the prefab's around their bone (1e-5), bones bound by name,
//        BodyParts = the body object's renderer names, the refusals (one bone more without names, a bone that does not bind:
//        Mismatch and nothing changed; "nosuch": NotFound), ReplacedBodyMesh, the supplied-part record, slot 0 the body draw
//        material and the supplied materials owned copies
//   KB4  normals: FromAsset(null, chara/oo_base.unity3d, p_cf_body_00_Nml) -> source <guid>/<hook>:o_body_cf, applied;
//        None -> not blended, no finding
//   KB5  layer + body part in one hook: one build pass (CharaBuilt once, no rebuild of any kind, flushes and mosaic stage runs
//        as the plugin-free build)
//   KB6  the character's mosaic record with the supplied body equals the plugin-free build's (paths, count, modes) on both
//        targets
//   KB8  rebuild requests (exactly one rebuild for two requests, the hook ran again; refusals) and the headless numbers of
//        the layers set outside builds (a re-composite without a rebuild, joined to the next build, removed again; a
//        destroyed texture skipped with a log line)
//   KB9  plugin.disable: the characters the probe's hooks touched are rebuilt once; its layers and parts are gone
//   KB10 the replaced body sets ReplacedBodyMesh and the provider's FK node row joins at the first FK application
//   KB11 the MaterialEditor rule follows the membership: an item naming HS2's mosaic material by name (c_m_mnpb), with the
//        body renderer's second slot carrying that name, is applied to the supplied body copy (not a member) and kept, not
//        applied, on the plugin-free body (a member through that material); numbers only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data.Ext;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class BridgeCoreTest
{
    const string KbGuid = "stp3.test.bodyprobe";
    const string KbHook = KbGuid + "/parts";
    const float KbHandScale = 1.05f;

    /// <summary>The plugin folder of the KB checks: the plugin group's shared stage (a DLL of the same name from another path
    /// would be refused in this process).</summary>
    static string KbDir => C37Dir;

    /// <summary>Stages the probe into the plugin group's folder when it is missing or older than its source or the compiled
    /// Api, or on request (-kbStage), built against the Editor's compiled Api; the group's own staging runs as well.</summary>
    static bool KbStage(Ctx c)
    {
        string dll = Path.Combine(KbDir, KbGuid, "Stp3.Test.BodyProbe.dll");
        string src = Path.Combine(ContentSettings.ProjectRoot, "Tests", "Plugins", "BodyProbe", "BodyProbe.cs");
        string api = Path.Combine(ContentSettings.ProjectRoot, "Library", "ScriptAssemblies", "HS2Next.Api.Unity.dll");
        bool stale = !File.Exists(dll) || (File.Exists(src) && File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dll)) ||
                     (File.Exists(api) && File.GetLastWriteTimeUtc(api) > File.GetLastWriteTimeUtc(dll));
        if (!stale && Array.IndexOf(TestArgs.Get(), "-kbStage") < 0) return C37Stage(c);
        var sw = Stopwatch.StartNew();
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        string project = Path.Combine(ContentSettings.ProjectRoot, "Tests", "Plugins", "BodyProbe");
        string apiDir = Path.Combine(ContentSettings.ProjectRoot, "Library", "ScriptAssemblies");
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build \"" + project + "\" -Stage \"" + KbDir + "\" -ApiDir \"" + apiDir + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
        };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            bool exited = p.WaitForExit(300000);
            c.Info("KB: the body probe staged by the check: plugins.ps1 exit " + (exited ? p.ExitCode.ToString() : "timeout") + " in " + sw.Elapsed.TotalSeconds.ToString("F1") + " s");
            if (!exited || p.ExitCode != 0) { c.Check(false, "KB: plugins.ps1 -Build " + project + " -Stage " + KbDir, Ctx.Short(o)); return false; }
        }
        return C37Stage(c) && File.Exists(dll);
    }

    /// <summary>
    /// A fresh host with the probe as the only loaded plugin of the staged folder (every other candidate on the disabled list,
    /// so no other plugin's hook touches the builds); null when staging or loading failed (reported).
    /// </summary>
    static PluginHost KbHost(Ctx c, string what)
    {
        if (!KbStage(c)) return null;
        var others = new List<string>();
        foreach (var d in Directory.GetDirectories(KbDir))
        {
            string m = Path.Combine(d, "manifest.json");
            string guid = null;
            try { if (File.Exists(m)) guid = (string)JObject.Parse(File.ReadAllText(m))["guid"]; } catch (Exception) { }
            guid = guid ?? Path.GetFileName(d);
            if (!string.Equals(guid, KbGuid, StringComparison.OrdinalIgnoreCase)) others.Add(guid);
        }
        var app = c.Host();
        var host = app.LoadPlugins(KbDir, others.ToArray());
        var e = host?.Find(KbGuid);
        if (!c.Check(e != null && e.State == PluginStates.Loaded && host.Entries.Count(x => x.IsLoaded) == 1, what + ": the body probe is the one loaded plugin",
                e == null ? "no entry" : e.State + " " + e.Code + ": " + e.Reason + "; loaded " + string.Join(",", host.Entries.Where(x => x.IsLoaded).Select(x => x.Guid)))) return null;
        return host;
    }

    static JObject KbConfig(Ctx c, object cfg, string what)
    {
        var o = JObject.FromObject(cfg);
        o["cmd"] = "bodyprobe.config";
        return c.Ok(o, what + ": bodyprobe.config");
    }

    /// <summary>A target loaded into the current host and applied; its key (null when the load failed: reported).</summary>
    static string KbLoad(Ctx c, bool female, string what)
    {
        string key = C30Load(c, female ? c.FemaleRel : c.MaleRel, female ? "female" : "male", what + ": chara.load " + (female ? "BR-Chan" : "HERO107"));
        if (key != null) c.App.Runtime.ApplyNow();
        return key;
    }

    static CharaRealization KbChara(Ctx c, string key) => key == null ? null : c.App.Runtime.FindChara(key);

    static JObject KbCreator(JObject info, string name) => (info?["creators"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["name"] == name);

    /// <summary>The numbers of one built character the KB checks compare with the plugin-free build.</summary>
    sealed class KbNumbers
    {
        public int BodyLayers = -1, BodyRebuilds = -1, Flushes = -1, MosaicRuns = -1, EditRebuilds = -1, Vertices = -1;
        public string MosaicText;
        public override string ToString() => $"body layers {BodyLayers}, body rebuilds {BodyRebuilds}, flushes {Flushes}, mosaic runs {MosaicRuns}, edit rebuilds {EditRebuilds}, vertices {Vertices}";
    }

    static KbNumbers KbRead(Ctx c, string key)
    {
        var n = new KbNumbers();
        var info = c.Call(new { cmd = "chara.edit.info", key });
        var cr = KbCreator(info, "create_skin_body");
        n.BodyLayers = (int?)cr?["layers"] ?? -1;
        n.BodyRebuilds = (int?)cr?["rebuilds"] ?? -1;
        n.Flushes = (int?)info["flushes"] ?? -1;
        var r = KbChara(c, key);
        var st = r?.Context != null ? MosaicSet.StateOf(r.Context) : null;
        n.MosaicRuns = st?.Runs ?? -1;
        n.EditRebuilds = r?.EditRebuilds ?? -1;
        n.Vertices = r?.Context?.BodyRenderer && r.Context.BodyRenderer.sharedMesh ? r.Context.BodyRenderer.sharedMesh.vertexCount : -1;
        var mos = c.Call(new { cmd = "chara.mosaic", key });
        n.MosaicText = string.Join("; ", ((mos["members"] as JArray) ?? new JArray()).OfType<JObject>().Select(m => (string)m["path"] + " " + (string)m["mode"]))
                       + " | count " + ((mos["members"] as JArray)?.Count ?? -1);
        return n;
    }

    static JObject KbState(Ctx c) => c.Ok(new { cmd = "bodyprobe.state" }, "bodyprobe.state");

    static JObject KbLast(JObject state, string key) => state?["last"]?[key] as JObject;

    static int KbCount(JObject state, string member, string key) => (int?)state?[member]?[key] ?? 0;

    static JObject KbPluginList(Ctx c) => C37Plugin(c.Ok(new { cmd = "plugin.list" }, "plugin.list"), KbGuid);

    // ---------------------------------------------------------------- KB1

    /// <summary>KB1: a BodyMesh layer on BR-Chan in the one build.</summary>
    static void KB1(Ctx c)
    {
        c.Host();
        var k0 = KbLoad(c, true, "KB1 plugin-free");
        if (k0 == null) return;
        var free = KbRead(c, k0);
        c.Info("KB1 plugin-free BR-Chan: " + free);
        c.Check(free.BodyLayers >= 0 && free.BodyRebuilds >= 1, "KB1: the headless build fills creators[create_skin_body].layers and rebuilds", free.ToString());
        var cardBefore = KbChara(c, k0)?.Context?.SkinLayers.Count ?? -1;

        // the card's plugin data off: a plugin-free reference too
        c.Services.ExtOptions.Enabled = false;
        c.Host();
        var k0off = KbLoad(c, true, "KB1 plugin-free, plugin data off");
        var freeOff = k0off != null ? KbRead(c, k0off) : null;
        c.Services.ExtOptions.Enabled = true;

        if (KbHost(c, "KB1") == null) return;
        KbConfig(c, new { png = true }, "KB1");
        var k1 = KbLoad(c, true, "KB1 with the probe");
        if (k1 == null) return;
        var with = KbRead(c, k1);
        var st = KbState(c);
        var ctx = KbChara(c, k1).Context;
        c.Info("KB1 with a PNG layer: " + with);
        c.Check((bool?)KbLast(st, k1)?["png"] == true, "KB1: SetSkinLayer (PNG bytes) at BodyMesh returns true", Ctx.Short(st.ToString(Formatting.None)));
        c.Check(with.BodyLayers == free.BodyLayers + 1, "KB1: creators[create_skin_body].layers = the plugin-free build's + 1", with.BodyLayers + " vs " + free.BodyLayers);
        c.Check(with.BodyRebuilds == free.BodyRebuilds && with.Flushes == free.Flushes, "KB1: the body creator's rebuilds and the editor's flushes equal the plugin-free build's",
            $"rebuilds {with.BodyRebuilds} vs {free.BodyRebuilds}, flushes {with.Flushes} vs {free.Flushes}");
        c.Check(KbCount(st, "builds", k1) == 1 && KbCount(st, "rebuilds", k1) == 0, "KB1: one CharaBuilt, not a rebuild", Ctx.Short(st.ToString(Formatting.None)));
        c.Check(ctx.SkinLayers.Count(l => l.Owner == KbGuid) == 1 && ctx.SkinLayers.Any(l => l.Source == KbGuid + "/mark" && l.TexType == 1 && !l.Runtime) && ctx.SkinLayers.Count == cardBefore + 1,
            "KB1: the context holds the card's layers and the plugin's (source <guid>/<id>, BodyOver)", string.Join(", ", ctx.SkinLayers.Select(l => l.ToString())));
        c.Check(ctx.Stats.SkinLayersComposited.ContainsKey("BodyOver " + KbGuid + "/mark") && ctx.Log.Any(l => l.StartsWith("ksox:", StringComparison.Ordinal) && l.Contains(KbGuid + "/mark")),
            "KB1: the build statistics and the build log name the layer <guid>/<id>", string.Join(", ", ctx.Stats.SkinLayersComposited.Keys));
        var pl = KbPluginList(c);
        c.Check((int?)pl?["skinLayers"] == 1 && (int?)pl?["bodyParts"] == 0, "KB1: plugin.list skinLayers 1, bodyParts 0", Ctx.Short(pl?.ToString(Formatting.None)));

        // a texture layer besides
        KbConfig(c, new { png = true, texture = true }, "KB1");
        var k2 = KbLoad(c, true, "KB1 with two layers");
        if (k2 != null)
        {
            var two = KbRead(c, k2);
            var ctx2 = KbChara(c, k2).Context;
            c.Check(two.BodyLayers == free.BodyLayers + 2 && two.BodyRebuilds == free.BodyRebuilds && ctx2.SkinLayers.Any(l => l.Source == KbGuid + "/tex" && l.Texture && l.Image == null),
                "KB1: a texture layer besides: layers + 2, rebuilds as plugin-free, the texture kept as the plugin's", two.ToString());
            var ids = (KbLast(KbState(c), k2)?["layers"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            c.Check(ids.SequenceEqual(new[] { "mark", "tex" }), "KB1: IBuildHookArgs.SkinLayers lists the ids in the order set", string.Join(",", ids));
        }

        // the card's plugin data off: the plugin's layer is composited all the same
        if (freeOff != null)
        {
            c.Services.ExtOptions.Enabled = false;
            KbConfig(c, new { png = true }, "KB1");
            var k3 = KbLoad(c, true, "KB1 with the probe, plugin data off");
            c.Services.ExtOptions.Enabled = true;
            if (k3 != null)
            {
                var off = KbRead(c, k3);
                var ctx3 = KbChara(c, k3).Context;
                c.Check(off.BodyLayers == freeOff.BodyLayers + 1 && off.BodyRebuilds == freeOff.BodyRebuilds && ctx3.Stats.SkinLayersComposited.ContainsKey("BodyOver " + KbGuid + "/mark"),
                    "KB1: with the card's plugin data off, the plugin's layer is still composited (layers + 1)", off + " vs " + freeOff);
            }
        }
    }

    // ---------------------------------------------------------------- KB2

    /// <summary>KB2: the stage and id rules.</summary>
    static void KB2(Ctx c)
    {
        if (KbHost(c, "KB2") == null) return;
        KbConfig(c, new { checks = true }, "KB2");
        var k = KbLoad(c, true, "KB2");
        if (k == null) return;
        var st = KbState(c);
        var stage = (st["stageChecks"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        var want = new[] { "Textures SetSkinLayer", "Textures RemoveSkinLayer", "Textures ReplaceBodyPart", "Final SetSkinLayer", "Final RemoveSkinLayer", "Final ReplaceBodyPart" };
        c.Check(want.All(stage.Contains), "KB2: SetSkinLayer, RemoveSkinLayer and ReplaceBodyPart throw InvalidOperationException at Textures and Final", string.Join(", ", stage));
        var last = KbLast(st, k);
        c.Check((bool?)last?["badBytes"] == false, "KB2: SetSkinLayer with bytes that are neither PNG nor JPEG -> false", Ctx.Short(last?.ToString(Formatting.None)));
        c.Check((bool?)last?["removeOther"] == false, "KB2: RemoveSkinLayer of an id the plugin never set -> false (a card's or another plugin's layer is never removed)", "");
        var ctx = KbChara(c, k).Context;
        c.Check(ctx.SkinLayers.All(l => l.Owner == null) && ctx.SuppliedParts.Count == 0, "KB2: the refused calls left no plugin layer and no supplied part", "");
        c.Check(ctx.Stats.Hooks.TryGetValue(KbGuid + "/late@Textures", out var tr) && tr.Errors == 0 && ctx.Stats.Hooks.TryGetValue(KbGuid + "/final@Final", out var fr) && fr.Errors == 0,
            "KB2: the probe's hooks caught the refusals (no hook error)", ctx.Stats.HooksSummary());
    }

    // ---------------------------------------------------------------- KB3

    /// <summary>The prefab body of a plugin-free build: vertices, dominant bone per vertex, bind positions and bone names.</summary>
    sealed class KbBody
    {
        public Vector3[] Vertices;
        public int[] Dominant;
        public Vector3[] Centres;
        public string[] BoneNames;
        public string[] Parts;
        public string Mosaic;
    }

    static KbBody KbReadBody(Ctx c, string key)
    {
        var ctx = KbChara(c, key)?.Context;
        var smr = ctx?.BodyRenderer;
        if (!smr || !smr.sharedMesh) return null;
        var m = smr.sharedMesh;
        var w = m.boneWeights;
        var bind = m.bindposes;
        var b = new KbBody
        {
            Vertices = m.vertices,
            Dominant = w.Select(x =>
            {
                int i = x.boneIndex0; float best = x.weight0;
                if (x.weight1 > best) { best = x.weight1; i = x.boneIndex1; }
                if (x.weight2 > best) { best = x.weight2; i = x.boneIndex2; }
                if (x.weight3 > best) i = x.boneIndex3;
                return i;
            }).ToArray(),
            Centres = bind.Select(p => p.inverse.MultiplyPoint3x4(Vector3.zero)).ToArray(),
            BoneNames = smr.bones.Select(t => t ? t.name : null).ToArray(),
            Parts = ctx.Body.GetComponentsInChildren<Renderer>(true).Select(r => r.name).ToArray(),
            Mosaic = KbRead(c, key).MosaicText,
        };
        return b;
    }

    /// <summary>KB3: the hand-scaled body copy on BR-Chan and HERO107.</summary>
    static void KB3(Ctx c)
    {
        c.Host();
        var kf = KbLoad(c, true, "KB3 plugin-free");
        var km = KbLoad(c, false, "KB3 plugin-free");
        var ref0 = new Dictionary<bool, KbBody> { [true] = kf != null ? KbReadBody(c, kf) : null, [false] = km != null ? KbReadBody(c, km) : null };
        if (!c.Check(ref0[true] != null && ref0[false] != null, "KB3: plugin-free bodies read", "")) return;
        if (KbHost(c, "KB3") == null) return;
        KbConfig(c, new { body = true, checks = true, materials = true }, "KB3");
        foreach (bool female in new[] { true, false })
        {
            string who = female ? "BR-Chan" : "HERO107", body = female ? "o_body_cf" : "o_body_cm";
            var key = KbLoad(c, female, "KB3 " + who);
            if (key == null) continue;
            var st = KbState(c);
            var last = KbLast(st, key);
            var r = KbChara(c, key);
            var ctx = r.Context;
            var smr = ctx.BodyRenderer;
            var copies = AppDomain.CurrentDomain.GetData("stp3.test.bodyprobe.meshes") as Dictionary<string, Mesh>;
            Mesh copy = null;
            copies?.TryGetValue(key, out copy);
            var pre = ref0[female];
            c.Check((string)last?["replace"] == "Replaced" && copy, $"KB3 {who}: ReplaceBodyPart -> Replaced", Ctx.Short(last?.ToString(Formatting.None)));
            if (!copy || !smr) continue;
            var drawn = smr.sharedMesh;
            var dv = drawn.vertices;
            var cv = copy.vertices;
            double toCopy = dv.Length == cv.Length ? Enumerable.Range(0, dv.Length).Max(i => (double)(dv[i] - cv[i]).magnitude) : double.NaN;
            c.Check(drawn.vertexCount == pre.Vertices.Length && cv.Length == pre.Vertices.Length && toCopy <= 1e-5,
                $"KB3 {who}: the drawn mesh is the copy (vertex count equal, every vertex within 1e-5)", $"vertices {drawn.vertexCount} / {cv.Length} / {pre.Vertices.Length}, max {toCopy:G3}");
            int hand = 0, scaledOk = 0, other = 0, otherOk = 0;
            double worst = 0;
            for (int i = 0; i < cv.Length && i < pre.Vertices.Length; i++)
            {
                int b = pre.Dominant[i];
                bool isHand = b >= 0 && b < pre.BoneNames.Length && pre.BoneNames[b] != null && pre.BoneNames[b].StartsWith("cf_J_Hand", StringComparison.Ordinal);
                var want = isHand ? pre.Centres[b] + (pre.Vertices[i] - pre.Centres[b]) * KbHandScale : pre.Vertices[i];
                double d = (cv[i] - want).magnitude;
                worst = Math.Max(worst, d);
                if (isHand) { hand++; if (d <= 1e-5) scaledOk++; } else { other++; if (d <= 1e-5) otherOk++; }
            }
            c.Check(hand > 0 && scaledOk == hand && otherOk == other && (int?)last?["scaled"] == hand,
                $"KB3 {who}: the hand vertices at 1.05 around their bone, every other vertex unchanged (bind space, 1e-5)", $"hand {scaledOk}/{hand}, other {otherOk}/{other}, worst {worst:G3}");
            var names = smr.bones.Select(t => t ? t.name : null).ToArray();
            bool bound = names.SequenceEqual(pre.BoneNames) && smr.bones.All(t => t && (!ctx.BodyBones.TryGetValue(t.name, out var bb) || bb == t));
            c.Check(bound, $"KB3 {who}: the bones bound by name to the character's body bones", $"{names.Length} bones");
            var parts = (last?["parts"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            c.Check(parts.SequenceEqual(pre.Parts) && parts.Contains(body), $"KB3 {who}: BodyParts = the body object's renderer names (depth-first)", parts.Count + " parts");
            c.Check((string)last?["extraBone"] == "Mismatch" && (string)last?["unknownBone"] == "Mismatch" && (bool?)last?["unchanged"] == true,
                $"KB3 {who}: a mesh with one bone more and no names, and a bone name that does not bind -> Mismatch, nothing changed", Ctx.Short(last?.ToString(Formatting.None)));
            c.Check((string)last?["nosuch"] == "NotFound" && (bool?)last?["nullThrew"] == true, $"KB3 {who}: \"nosuch\" -> NotFound; a null replacement throws ArgumentNullException", "");
            c.Check(ctx.ReplacedBodyMesh == KbHook && ctx.SuppliedParts.Count == 1 && ctx.SuppliedParts[0].Part == body && ctx.SuppliedParts[0].HookId == KbHook &&
                    MosaicSet.IsSupplied(smr) && !MosaicSet.IsMember(smr),
                $"KB3 {who}: ReplacedBodyMesh <guid>/<hook>, the body renderer recorded as a supplied part (not a member)", ctx.ReplacedBodyMesh ?? "null");
            c.Check(ctx.Stats.Hooks.TryGetValue(KbHook + "@BodyMesh", out var run) && run.Renderers == 1 && run.Errors == 0, $"KB3 {who}: one changed renderer counted", run?.ToString());
            c.Check(ctx.Log.Any(l => l.StartsWith("body part " + body + " supplied by " + KbHook, StringComparison.Ordinal)), $"KB3 {who}: the build log names the part and <guid>/<hook>", "");
            var mats = smr.sharedMaterials;
            c.Check(mats.Length >= 1 && mats[0] == ctx.BodyDraw && mats.Skip(1).All(m => m && ctx.OwnedMaterials.Contains(m)),
                $"KB3 {who}: slot 0 stays the body draw material, the supplied materials fill the other slots as owned copies", $"{mats.Length} slots: {string.Join(", ", mats.Select(m => m ? m.name : "null"))}");
            c.Info($"KB3 {who}: body renderer slots {mats.Length}, bones {names.Length}, hand vertices {hand} of {cv.Length}, parts {parts.Count}");
            var pl = KbPluginList(c);
            c.Check((int?)pl?["bodyParts"] >= 1, $"KB3 {who}: plugin.list bodyParts counts it", Ctx.Short(pl?.ToString(Formatting.None)));
        }
    }

    // ---------------------------------------------------------------- KB4

    /// <summary>KB4: the normals reference of the supplied body.</summary>
    static void KB4(Ctx c)
    {
        if (KbHost(c, "KB4") == null) return;
        KbConfig(c, new { body = true, normals = "asset" }, "KB4");
        var k = KbLoad(c, true, "KB4 FromAsset");
        if (k == null) return;
        var bust = KbChara(c, k).Context.BustNormals;
        var reply = c.Ok(new { cmd = "chara.edit.bust", key = k }, "KB4: chara.edit.bust");
        c.Check(bust != null && bust.Source == KbHook + ":o_body_cf" && !bust.Missing && bust.AnyApplied && bust.Bundle == "chara/oo_base.unity3d" && bust.Asset == "p_cf_body_00_Nml",
            "KB4: FromAsset(null, chara/oo_base.unity3d, p_cf_body_00_Nml): the source <guid>/<hook>:o_body_cf, applied",
            bust == null ? "no state" : $"{bust.Source} missing {bust.Missing} applied {bust.AnyApplied}; {Ctx.Short(reply["normals"]?["source"]?.ToString(Formatting.None))}");
        c.Info("KB4 chara.edit.bust normals.source: " + reply["normals"]?["source"]?.ToString(Formatting.None) + ", applied " + reply["normals"]?["applied"]);

        KbConfig(c, new { body = true, normals = "none" }, "KB4");
        var k2 = KbLoad(c, true, "KB4 None");
        if (k2 == null) return;
        var b2 = KbChara(c, k2).Context.BustNormals;
        c.Check(b2 != null && b2.Missing && !b2.AnyApplied && b2.Blends == 0 && b2.Copies == 0 && b2.Source == KbHook + ":o_body_cf",
            "KB4: None: not blended (no blend, no mesh copy)", b2 == null ? "no state" : $"{b2.Source} missing {b2.Missing} blends {b2.Blends} copies {b2.Copies}");
        c.Check(!c.App.Runtime.Findings.Any(f => f.Code == "chara.bustNormalsMissing" && f.NodeId == k2), "KB4: None raises no chara.bustNormalsMissing", "");

        KbConfig(c, new { body = true }, "KB4");
        var k3 = KbLoad(c, true, "KB4 kept");
        var b3 = k3 != null ? KbChara(c, k3).Context.BustNormals : null;
        c.Check(b3 != null && b3.Source == "vanilla" && b3.AnyApplied, "KB4: Normals null keeps the current source (the vanilla normals apply to the copy)", b3?.Source ?? "no state");
    }

    // ---------------------------------------------------------------- KB5

    /// <summary>KB5: a layer and the body part in one hook, one build pass.</summary>
    static void KB5(Ctx c)
    {
        c.Host();
        var k0 = KbLoad(c, true, "KB5 plugin-free");
        if (k0 == null) return;
        var free = KbRead(c, k0);
        if (KbHost(c, "KB5") == null) return;
        KbConfig(c, new { png = true, body = true, normals = "asset" }, "KB5");
        var k = KbLoad(c, true, "KB5");
        if (k == null) return;
        var with = KbRead(c, k);
        var st = KbState(c);
        var r = KbChara(c, k);
        c.Info("KB5: plugin-free " + free + "; with the probe " + with);
        c.Check(KbCount(st, "builds", k) == 1 && KbCount(st, "rebuilds", k) == 0 && (int?)st["bodyMeshRuns"] == 1, "KB5: one build (CharaBuilt once, not a rebuild; the BodyMesh hook ran once)",
            Ctx.Short(st.ToString(Formatting.None)));
        c.Check(with.EditRebuilds == 0 && r.PluginRebuilds == 0 && with.Flushes == free.Flushes && with.MosaicRuns == free.MosaicRuns,
            "KB5: no edit or plugin rebuild, flushes and mosaic stage runs as the plugin-free build", $"edit rebuilds {with.EditRebuilds}, plugin rebuilds {r.PluginRebuilds}, flushes {with.Flushes}/{free.Flushes}, mosaic runs {with.MosaicRuns}/{free.MosaicRuns}");
        c.Check(with.BodyLayers == free.BodyLayers + 1 && with.BodyRebuilds == free.BodyRebuilds && r.Context.ReplacedBodyMesh == KbHook && r.Context.BustNormals?.AnyApplied == true,
            "KB5: the same build composited the layer, drew the copy and blended its normals", with.ToString());
    }

    // ---------------------------------------------------------------- KB6

    /// <summary>KB6: the mosaic record with the supplied body equals the plugin-free build's on both targets.</summary>
    static void KB6(Ctx c)
    {
        c.Host();
        var kf = KbLoad(c, true, "KB6 plugin-free");
        var km = KbLoad(c, false, "KB6 plugin-free");
        var free = new Dictionary<bool, string> { [true] = kf != null ? KbRead(c, kf).MosaicText : null, [false] = km != null ? KbRead(c, km).MosaicText : null };
        if (KbHost(c, "KB6") == null) return;
        KbConfig(c, new { body = true, normals = "asset" }, "KB6");
        foreach (bool female in new[] { true, false })
        {
            string who = female ? "BR-Chan" : "HERO107";
            var k = KbLoad(c, female, "KB6 " + who);
            if (k == null || free[female] == null) continue;
            var with = KbRead(c, k).MosaicText;
            var ctx = KbChara(c, k).Context;
            c.Check(ctx.ReplacedBodyMesh == KbHook && with == free[female], $"KB6 {who}: chara.mosaic members (paths, modes, count) equal the plugin-free build's with the supplied body",
                "with: " + Ctx.Short(with) + " | free: " + Ctx.Short(free[female]));
        }
    }

    // ---------------------------------------------------------------- KB8

    /// <summary>KB8: rebuild requests, and the headless numbers of the layers set outside builds.</summary>
    static void KB8(Ctx c)
    {
        c.Host();
        var k0 = KbLoad(c, true, "KB8 plugin-free");
        if (k0 == null) return;
        var free = KbRead(c, k0);
        if (KbHost(c, "KB8") == null) return;
        KbConfig(c, new { png = true }, "KB8");
        var k = KbLoad(c, true, "KB8");
        if (k == null) return;
        var r = KbChara(c, k);
        var ctx0 = r.Context;
        int runs0 = (int?)KbState(c)["bodyMeshRuns"] ?? -1;
        var q1 = c.Ok(new { cmd = "bodyprobe.rebuild", node = k, times = 2 }, "KB8: bodyprobe.rebuild (two requests in one call)");
        c.Check(((q1["each"] as JArray)?.Select(x => (bool)x).ToList() ?? new List<bool>()).SequenceEqual(new[] { true, true }), "KB8: RequestRebuild -> true for each request", Ctx.Short(q1.ToString(Formatting.None)));
        c.App.Runtime.ApplyNow();
        c.App.Runtime.ApplyNow();
        var st = KbState(c);
        c.Check(r.PluginRebuilds == 1 && !r.PluginRebuildPending && r.Context != ctx0 && KbCount(st, "rebuilds", k) == 1 && (int?)st["bodyMeshRuns"] == runs0 + 1 && r.EditRebuilds == 0,
            "KB8: two requests before the next apply -> exactly one rebuild there (CharaBuilt Rebuild true once), the hook ran again",
            $"plugin rebuilds {r.PluginRebuilds}, CharaBuilt rebuilds {KbCount(st, "rebuilds", k)}, BodyMesh runs {runs0} -> {st["bodyMeshRuns"]}, edit rebuilds {r.EditRebuilds}");
        c.Check(r.Context.SkinLayers.Count(l => l.Owner == KbGuid) == 1, "KB8: the rebuilt character has the hook's layer again", "");
        var q3 = c.Ok(new { cmd = "bodyprobe.rebuild", node = "no-such-node" }, "KB8: bodyprobe.rebuild of an unknown node");
        c.Check((bool?)q3["queued"] == false, "KB8: RequestRebuild of an unknown node -> false", "");

        // layers outside builds (the headless numbers; the drawn result is the play row's KB7)
        KbConfig(c, new { }, "KB8 (hooks inert)");
        c.Ok(new { cmd = "bodyprobe.rebuild", node = k }, "KB8: a rebuild without the hook's layer");
        c.App.Runtime.ApplyNow();
        var baseN = KbRead(c, k);
        int rebuildsBefore = r.PluginRebuilds;
        int flushBefore = baseN.Flushes;
        var set = c.Ok(new { cmd = "bodyprobe.layer", node = k, op = "set", layer = "outside", kind = 1, form = "png" }, "KB8: SkinLayers(node).Set (PNG)");
        c.Check((bool?)set["result"] == true && ((set["ids"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "outside" }) ?? false), "KB8: Set -> true, Ids [outside]", Ctx.Short(set.ToString(Formatting.None)));
        c.App.Runtime.ApplyNow();
        var on = KbRead(c, k);
        var lf = r.LastEditFlush;
        c.Check(on.BodyLayers == baseN.BodyLayers + 1 && on.Flushes == flushBefore + 1 && r.PluginRebuilds == rebuildsBefore && r.EditRebuilds == 0 && lf != null && lf.Units.Contains("ext.textures"),
            "KB8: the next flush re-composites (layers + 1, one flush running ext.textures), no rebuild", $"{on}; units {(lf == null ? "-" : string.Join(",", lf.Units))}");
        c.Check(r.Context.SkinLayers.Any(l => l.Source == KbGuid + "/outside" && l.Runtime) && r.PluginLayers.Layers.Count == 1, "KB8: the layer is on the character and in the node's record", "");
        var bad = c.Ok(new { cmd = "bodyprobe.layer", node = k, op = "set", layer = "bad", form = "bad" }, "KB8: Set with bytes that are no image");
        c.Check((bool?)bad["result"] == false, "KB8: Set with bytes that are no image -> false", "");
        c.Ok(new { cmd = "bodyprobe.rebuild", node = k }, "KB8: a rebuild keeps the layer set outside builds");
        c.App.Runtime.ApplyNow();
        var joined = KbRead(c, k);
        c.Check(joined.BodyLayers == baseN.BodyLayers + 1 && r.Context.SkinLayers.Any(l => l.Source == KbGuid + "/outside") && r.Context.Log.Any(l => l.Contains(KbGuid + "/outside") && l.Contains("joined")),
            "KB8: the next build joins it after the BodyMesh hooks (layers + 1)", joined.ToString());
        var rem = c.Ok(new { cmd = "bodyprobe.layer", node = k, op = "remove", layer = "outside" }, "KB8: SkinLayers(node).Remove");
        c.App.Runtime.ApplyNow();
        var off = KbRead(c, k);
        c.Check((bool?)rem["result"] == true && off.BodyLayers == baseN.BodyLayers && r.PluginLayers.Layers.Count == 0 && r.Context.SkinLayers.All(l => l.Owner == null),
            "KB8: Remove -> true, back to the layers before (re-composited)", off.ToString());
        var gone = c.Ok(new { cmd = "bodyprobe.layer", node = k, op = "set", layer = "gone", form = "gone" }, "KB8: Set with a texture destroyed right after");
        c.App.Runtime.ApplyNow();
        var g = KbRead(c, k);
        c.Check((bool?)gone["result"] == true && g.BodyLayers == baseN.BodyLayers && r.Context.Stats.SkinLayersSkipped.ContainsKey("BodyOver (texture destroyed)") &&
                r.Context.Log.Any(l => l.Contains("ksox: layer " + KbGuid + "/gone skipped: its texture was destroyed")),
            "KB8: a layer whose texture was destroyed is skipped at the composite with a log line", g.ToString());
        c.Ok(new { cmd = "bodyprobe.layer", node = k, op = "remove", layer = "gone" }, "KB8: remove it");
        c.App.Runtime.ApplyNow();
        var none = c.Ok(new { cmd = "bodyprobe.layer", node = "no-such-node", op = "ids" }, "KB8: SkinLayers of an unknown node");
        c.Check((bool?)none["available"] == false, "KB8: SkinLayers of a node that is not a built character -> null", "");
        c.Check(off.BodyLayers == free.BodyLayers, "KB8: the layer count is the plugin-free build's again", off.BodyLayers + " vs " + free.BodyLayers);
    }

    // ---------------------------------------------------------------- KB9

    /// <summary>KB9: plugin.disable rebuilds the characters the probe's hooks touched; its layers and parts leave them.</summary>
    static void KB9(Ctx c)
    {
        c.Host();
        var kf0 = KbLoad(c, true, "KB9 plugin-free");
        var km0 = KbLoad(c, false, "KB9 plugin-free");
        if (kf0 == null || km0 == null) return;
        var free = new Dictionary<bool, KbNumbers> { [true] = KbRead(c, kf0), [false] = KbRead(c, km0) };
        if (KbHost(c, "KB9") == null) return;
        KbConfig(c, new { png = true, body = true }, "KB9");
        var kf = KbLoad(c, true, "KB9");
        var km = KbLoad(c, false, "KB9");
        if (kf == null || km == null) return;
        var rf = KbChara(c, kf);
        var rm = KbChara(c, km);
        c.Ok(new { cmd = "bodyprobe.layer", node = kf, op = "set", layer = "outside", form = "png" }, "KB9: a layer outside the build on BR-Chan");
        c.App.Runtime.ApplyNow();
        var before = new[] { rf.Context, rm.Context };
        var dis = c.Ok(new { cmd = "plugin.disable", guid = KbGuid }, "KB9: plugin.disable");
        c.App.Runtime.ApplyNow();
        c.App.Runtime.ApplyNow();
        c.Info("KB9 plugin.disable: " + Ctx.Short(dis.ToString(Formatting.None)));
        c.Check((int?)dis["rebuilt"] == 2, "KB9: the disable queued a rebuild of exactly the two characters the probe's hooks ran on (their build statistics name its hooks)",
            "rebuilt " + dis["rebuilt"]);
        foreach (var (r, female) in new[] { (rf, true), (rm, false) })
        {
            string who = female ? "BR-Chan" : "HERO107";
            var ctx = r.Context;
            var n = KbRead(c, r.NodeId);
            c.Check(r.PluginRebuilds == 1 && ctx != before[female ? 0 : 1], $"KB9 {who}: rebuilt once at the next apply", $"plugin rebuilds {r.PluginRebuilds}");
            c.Check(ctx.SkinLayers.All(l => l.Owner == null) && r.PluginLayers.Layers.Count == 0 && ctx.SuppliedParts.Count == 0 && ctx.ReplacedBodyMesh == null &&
                    !ctx.Stats.Hooks.Keys.Any(h => h.StartsWith(KbGuid + "/", StringComparison.Ordinal)),
                $"KB9 {who}: no layer, part, replaced body or hook of the probe", ctx.Stats.HooksSummary());
            c.Check(n.BodyLayers == free[female].BodyLayers && n.Vertices == free[female].Vertices && n.MosaicText == free[female].MosaicText,
                $"KB9 {who}: the values of a plugin-free build (layers, vertices, mosaic record)", n + " vs " + free[female]);
        }
    }

    // ---------------------------------------------------------------- KB10

    /// <summary>KB10: the replaced body and the FK node rows.</summary>
    static void KB10(Ctx c)
    {
        c.Host();
        var k0 = KbLoad(c, true, "KB10 plugin-free");
        if (k0 == null) return;
        var r0 = KbChara(c, k0);
        var fk0 = FkDriver.Of(r0);
        if (!c.Check(fk0 != null, "KB10: the plugin-free character has its FK state", "")) return;
        string bone = r0.Context.BodyBones.Keys.Where(n => n.StartsWith("cf_J_", StringComparison.Ordinal) && fk0.Table.FindAny(n) == null)
            .OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
        if (!c.Check(bone != null && !fk0.Table.NodesJoined, "KB10: a body bone without an FK row exists; the plugin-free build joins no node rows", bone)) return;
        if (KbHost(c, "KB10") == null) return;
        KbConfig(c, new { fkBone = bone }, "KB10");
        var k1 = KbLoad(c, true, "KB10 without the body part");
        var fk1 = k1 != null ? FkDriver.Of(KbChara(c, k1)) : null;
        c.Check(fk1 != null && KbChara(c, k1).Context.ReplacedBodyMesh == null && !fk1.Table.NodesJoined, "KB10: without a replaced body the provider's rows do not join", "");
        KbConfig(c, new { body = true, fkBone = bone }, "KB10");
        var k2 = KbLoad(c, true, "KB10 with the body part");
        var r2 = k2 != null ? KbChara(c, k2) : null;
        var fk2 = r2 != null ? FkDriver.Of(r2) : null;
        c.Check(r2 != null && r2.Context.ReplacedBodyMesh == KbHook, "KB10: replacing the body renderer sets ReplacedBodyMesh to <guid>/<hook>", r2?.Context.ReplacedBodyMesh ?? "null");
        c.Check(fk2 != null && fk2.Table.NodesJoined && fk2.Table.Additional.Any(x => x.Bone && x.Bone.name == bone),
            "KB10: the provider's FK node row joins at the first FK application", fk2 == null ? "no FK state" : $"joined {fk2.Table.NodesJoined}, additional {fk2.Table.Additional.Count}");
    }

    // ---------------------------------------------------------------- KB11

    /// <summary>A float property of <paramref name="m"/>'s shader (the first one), or null.</summary>
    static string KbFloatProperty(Material m)
    {
        if (!m || !m.shader) return null;
        for (int i = 0; i < m.shader.GetPropertyCount(); i++)
        {
            var t = m.shader.GetPropertyType(i);
            if (t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) return m.shader.GetPropertyName(i);
        }
        return null;
    }

    /// <summary>One MaterialEditor float item on HS2's mosaic material name, the body renderer's second slot renamed to it;
    /// (applied, skipped as mosaicProtected, listed as protected, the slot's value after).</summary>
    static (bool applied, int skipped, bool listed, float value, float want, string prop) KbMeOnMosaicName(CharaContext ctx)
    {
        var smr = ctx.BodyRenderer;
        var mats = smr.sharedMaterials;
        var slot = mats.Length > 1 ? mats[1] : null;
        string prop = KbFloatProperty(slot);
        if (prop == null) return (false, -1, false, float.NaN, float.NaN, null);
        string oldName = slot.name;
        float before = slot.GetFloat(prop), want = before + 0.25f;
        slot.name = MosaicSet.OriginalMaterial + " (Instance)";
        var me = new MaterialEditorData();
        me.FloatProperties.Add(new MaterialEditorData.FloatProperty
        {
            ObjectType = MaterialEditorData.ObjectType.Character, MaterialName = MosaicSet.OriginalMaterial, Property = prop.TrimStart('_'),
            Value = want.ToString("R", System.Globalization.CultureInfo.InvariantCulture), ValueOriginal = before.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        });
        var prev = ctx.Ext.MaterialEditor;
        ctx.Ext.MaterialEditor = me;
        try
        {
            ctx.Stats.MeSkipped.TryGetValue(MosaicSet.MeSkipReason, out int s0);
            MaterialEditorApplier.Apply(ctx, me, ctx.Options ?? new CharaExtOptions());
            ctx.Stats.MeSkipped.TryGetValue(MosaicSet.MeSkipReason, out int s1);
            bool listed = MosaicSet.ProtectedItems(ctx).Any(x => x.Target == MosaicSet.OriginalMaterial);
            float after = slot.GetFloat(prop);
            return (Mathf.Approximately(after, want), s1 - s0, listed, after, want, prop);
        }
        finally
        {
            ctx.Ext.MaterialEditor = prev;
            slot.SetFloat(prop, before);
            slot.name = oldName;
        }
    }

    /// <summary>KB11: the MaterialEditor rule follows the mosaic set's membership for a supplied body part.</summary>
    static void KB11(Ctx c)
    {
        c.Host();
        var k0 = KbLoad(c, true, "KB11 plugin-free");
        if (k0 == null) return;
        var free = KbMeOnMosaicName(KbChara(c, k0).Context);
        c.Info($"KB11 plugin-free: property {free.prop}, applied {free.applied}, skipped {free.skipped}, listed {free.listed}, value {free.value} (wanted {free.want})");
        if (!c.Check(free.prop != null, "KB11: BR-Chan's body renderer has a second slot with a float property", "")) return;
        c.Check(!free.applied && free.skipped == 1 && free.listed, "KB11: plugin-free, the body holding a material of that name is a member: the item is kept, not applied (mosaicProtected)",
            $"applied {free.applied}, skipped {free.skipped}, listed {free.listed}");
        if (KbHost(c, "KB11") == null) return;
        KbConfig(c, new { body = true, materials = true }, "KB11");
        var k = KbLoad(c, true, "KB11 with the supplied body");
        if (k == null) return;
        var ctx = KbChara(c, k).Context;
        var smr = ctx.BodyRenderer;
        var members = MosaicSet.StateOf(ctx)?.Members.Select(m => m.Renderer).Where(r => r).ToList() ?? new List<Renderer>();
        var memberMats = members.SelectMany(r => r.sharedMaterials).ToArray();
        var with = KbMeOnMosaicName(ctx);
        c.Info($"KB11 supplied body: property {with.prop}, applied {with.applied}, skipped {with.skipped}, listed {with.listed}, value {with.value} (wanted {with.want})");
        c.Check(MosaicSet.IsSupplied(smr) && !MosaicSet.IsMember(smr) && with.applied && with.skipped == 0 && !with.listed,
            "KB11: the supplied body copy (not a member) gets the item on a material of that name; nothing skipped or listed as protected",
            $"applied {with.applied}, skipped {with.skipped}, listed {with.listed}");
        c.Check(members.Count > 0 && members.SelectMany(r => r.sharedMaterials).SequenceEqual(memberMats) && members.All(MosaicSet.IsMember),
            "KB11: the members keep their materials (still protected by membership)", members.Count + " members");
    }
}
