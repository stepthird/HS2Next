// WetTest: what draws the body without being the body. WB5 a body part replaced through the kit's probe, and a body of other UVs; WB7 cut bodies and parts that draw the body's material
// on the rows that have them (Tools/survey/body_cut_census.txt, the rows of MakerClothesTest). A row that is not installed is a WARN.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.App.Plugins;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class WetTest
{
    const string ProbeGuid = "stp3.test.bodyprobe";
    const string CutTop = "K044#725044";                 // a top with a body cut switched by its def object
    const string SkinSocks = "roy12.skindentations#1";   // socks whose o_socks_thigh2 draws the body's materials

    static string ProbeDir => Path.Combine(OutDir, "plugins");

    static bool StageProbe()
    {
        string dll = Path.Combine(ProbeDir, ProbeGuid, "Stp3.Test.BodyProbe.dll");
        string source = Path.Combine(ContentSettings.ProjectRoot, "Tests", "Plugins", "BodyProbe", "BodyProbe.cs");
        string api = Path.Combine(ContentSettings.ProjectRoot, "Library", "ScriptAssemblies", "HS2Next.Api.Unity.dll");
        bool stale = !File.Exists(dll) || (File.Exists(source) && File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(dll)) ||
                     (File.Exists(api) && File.GetLastWriteTimeUtc(api) > File.GetLastWriteTimeUtc(dll));
        if (!stale) return true;
        Directory.CreateDirectory(ProbeDir);
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        string project = Path.Combine(ContentSettings.ProjectRoot, "Tests", "Plugins", "BodyProbe");
        string apiDir = Path.Combine(ContentSettings.ProjectRoot, "Library", "ScriptAssemblies");
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build \"" + project + "\" -Stage \"" + ProbeDir + "\" -ApiDir \"" + apiDir + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
        };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            bool exited = p.WaitForExit(300000);
            if (!Check("the body probe is staged", exited && p.ExitCode == 0 && File.Exists(dll), Short(o))) return false;
        }
        return true;
    }

    static void Render_Parts()
    {
        Group("WB5", "a body part replaced", Wb5);
        Group("WB7", "cut bodies and parts that draw the body's material", Wb7);
    }

    static void Wb5()
    {
        // the kit's probe: a copy of the body mesh, its name and UVs kept: the same signature, so the stain stays on it
        if (StageProbe())
            using (var h = NewHost())
            {
                var host = h.App.LoadPlugins(ProbeDir, Array.Empty<string>());
                var e = host?.Find(ProbeGuid);
                if (Check("the probe is loaded", e != null && e.State == PluginStates.Loaded, e == null ? "no entry" : e.State + ": " + e.Reason))
                {
                    string key = LoadChara(h);
                    Stain(h, key);
                    var rig = RigOf(h, key);
                    int live = Part(h).StatusOf(key).Live;
                    string sig = rig.Slots.Values.First(s => s.Target.Kind == WetKinds.Skin).Target.Signature;
                    // a plugin's skin layer: a flush of the skin, the composite follows and goes back
                    string skinKey = rig.Slots.Values.First(s => s.Target.Kind == WetKinds.Skin).Target.Key;
                    var plain = Digests(h, key);
                    h.Ok(new { cmd = "bodyprobe.layer", node = key, op = "set", layer = "wet", kind = 1, form = "png" }, "a plugin's skin layer");
                    h.Frames(2);
                    Check("a plugin's skin layer: the skin's composite is drawn from the new image", Digests(h, key)[skinKey] != plain[skinKey], skinKey);
                    h.Ok(new { cmd = "bodyprobe.layer", node = key, op = "remove", layer = "wet" }, "the layer removed");
                    h.Frames(2);
                    Same("the layer removed: the composites of before", plain, Digests(h, key));
                    h.Ok(new { cmd = "bodyprobe.config", body = true }, "bodyprobe.config");
                    h.Ok(new { cmd = "bodyprobe.rebuild", node = key }, "bodyprobe.rebuild");
                    h.Frames(2);
                    var real = h.Rt.FindChara(key);
                    Check("the body was replaced", real.Context.ReplacedBodyMesh != null, real.Context.ReplacedBodyMesh ?? "");
                    var s1 = Part(h).StatusOf(key);
                    var slot = RigOf(h, key).Slots.Values.FirstOrDefault(s => s.Target.Kind == WetKinds.Skin);
                    Info("the replaced body's signature " + (slot?.Target.Signature ?? "none") + " against " + sig);
                    Check("a mesh with the body's UVs: the signature decides, nothing is lost", s1.Live + s1.Dormant == live, $"{s1.Live} live, {s1.Dormant} dormant of {live}");
                    Check("no slot on a dead renderer", RigOf(h, key).Slots.Values.All(s => s.Target.Renderer), "");
                    h.Ok(new { cmd = "bodyprobe.config", body = false }, "bodyprobe.config off");
                }
            }

        // a body of other UVs: dormant, reported, dry
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            Stain(h, key);
            var real = h.Rt.FindChara(key);
            var body = real.Context.BodyRenderer;
            int s0 = Part(h).StatusOf(key).Live;
            var old = body.sharedMesh;
            var copy = UnityEngine.Object.Instantiate(old);
            copy.name = old.name;
            var uv = copy.uv;
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uv[i].y, uv[i].x);
            copy.uv = uv;
            body.sharedMesh = copy;
            RigOf(h, key).Resolve();
            var s = Part(h).StatusOf(key);
            Check("other UVs: its map is dormant, reported", s.Dormant == 1 && s.Live == s0 - 1, $"{s.Live} live, {s.Dormant} dormant of {s0}");
            Check("other UVs: the body is dry", !body.HasPropertyBlock(), "");
            Check("other UVs: the document keeps the map", St(h, key).Wet.Stains[0].Maps.Count == s0, "");
            body.sharedMesh = old;
            RigOf(h, key).Resolve();
            s = Part(h).StatusOf(key);
            Check("the part returns: live again", s.Dormant == 0 && s.Live == s0, $"{s.Live} live, {s.Dormant} dormant");
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    static bool Installed(Host h, string key, string path, string row)
    {
        var l = h.Call(new { cmd = "chara.edit.list", key, path, search = row, limit = 20 });
        bool any = (l["rows"] as JArray)?.Any(x => (string)x["ref"] == row) == true;
        if (!any) Warn("row " + row + " is not installed: not run");
        return any;
    }

    static void Wb7()
    {
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            if (Installed(h, key, "coord.clothes.top.item", CutTop))
            {
                Stain(h, key);
                var before = Digests(h, key);
                h.Ok(new { cmd = "chara.edit.item", key, path = "coord.clothes.top.item", @ref = CutTop }, "a top with a body cut");
                h.Rt.ApplyNow();
                var real = h.Rt.FindChara(key);
                var ctx = real.Context;
                Check("one cut body", ctx.BodyCuts.Count == 1, ctx.BodyCuts.Count.ToString());
                var cut = ctx.BodyCuts.Count > 0 ? ctx.BodyCuts[0].TopA[1].GetComponent<SkinnedMeshRenderer>() : null;
                if (cut)
                {
                    Check("the body is off, the cut body on", !ctx.BodyRenderer.enabled && cut.enabled, "");
                    var targets = WetTargets.Collect(real);
                    var skin = targets.FirstOrDefault(t => t.Kind == WetKinds.Skin);
                    Check("the skin target lists the cut body", skin != null && skin.Followers.Any(f => f.Renderer == cut), "");
                    Check("the cut body is no target of its own in its first slot", !targets.Any(t => t.Renderer == cut && t.Slot == 0), "");
                    string a = WetTargets.SignatureOf(ctx.BodyRenderer), b = WetTargets.SignatureOf(cut);
                    bool sameUv = a != null && b != null && a.Substring(a.LastIndexOf('|')) == b.Substring(b.LastIndexOf('|'));
                    Info("body " + a + ", cut " + b);
                    var slot = RigOf(h, key).Slots.Values.FirstOrDefault(s => s.Target.Kind == WetKinds.Skin);
                    if (sameUv) Check("a cut body of the body's UV0 shows the composite", cut.HasPropertyBlock(), "");
                    else Check("a cut body of other UVs stays dry", !cut.HasPropertyBlock(), "");
                    Check("the body's own slot stays", slot == null || slot.Compose.Wet, "");
                    Check("the body, off, gets no map from a bake", SkinMapsOf(h, key) == 0, SkinMapsOf(h, key).ToString());
                }
                h.Session.Commands.Undo();
                h.Rt.ApplyNow();
                Same("undo: the composites of before", before, Digests(h, key));
            }
        }
        using (var h = NewHost())
        {
            string key = LoadChara(h);
            if (Installed(h, key, "coord.clothes.socks.item", SkinSocks))
            {
                Stain(h, key);
                h.Ok(new { cmd = "chara.edit.item", key, path = "coord.clothes.socks.item", @ref = SkinSocks }, "socks that draw the body's material");
                h.Rt.ApplyNow();
                var real = h.Rt.FindChara(key);
                var ctx = real.Context;
                Check("a part draws the body's material", ctx.BodyMaterialTargets.Count > 0, ctx.BodyMaterialTargets.Count.ToString());
                var targets = WetTargets.Collect(real);
                var skin = targets.FirstOrDefault(t => t.Kind == WetKinds.Skin);
                foreach (var r in ctx.BodyMaterialTargets)
                {
                    if (!r) continue;
                    Check(r.name + ": no target of its own", !targets.Any(t => t.Renderer == r), "");
                    Check(r.name + ": it follows the skin target", skin != null && skin.Followers.Any(f => f.Renderer == r), "");
                    string a = WetTargets.SignatureOf(ctx.BodyRenderer), b = WetTargets.SignatureOf(r);
                    bool sameUv = a != null && b != null && a.Substring(a.LastIndexOf('|')) == b.Substring(b.LastIndexOf('|'));
                    Info("body " + a + ", part " + b);
                    Check(r.name + (sameUv ? ": it shows the composite" : ": of other UVs it stays dry"), r.HasPropertyBlock() == sameUv, "");
                }
                string bake = FreshBake(h, key);
                Check("a new bake runs with such a part on", bake == null, bake ?? "");
            }
        }
    }

    // the skin maps a bake makes on what is shown now
    static int SkinMapsOf(Host h, string key)
    {
        var block = St(h, key).Wet.Clone();
        var s = block.Stains[0];
        Part(h).Bake(key, block, s);
        var real = h.Rt.FindChara(key);
        return s.Maps.Count(m => m.Renderer.StartsWith("body/", StringComparison.Ordinal));
    }

    static string FreshBake(Host h, string key)
    {
        var block = St(h, key).Wet.Clone();
        return Part(h).Bake(key, block, block.Stains[0]);
    }
}
