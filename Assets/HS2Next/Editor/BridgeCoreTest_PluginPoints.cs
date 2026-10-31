// The engine-side Api points inside BridgeCoreTest's plugin group (C37 runs every PX<n> method of this partial class),
// headless, through the Api interfaces of one plugin's services (PluginEngineServices with a test GUID, as the plugin host
// hands them out): PX1 the typed overrides refuse every mosaic member (Protected, a scene finding api.mosaicProtected on the
// character, one plugin-log line per renderer, counted) and apply on the body; PX2 AddMosaic adds a member, which the
// plugin's unload keeps; PX3 the shader supply refuses the mosaic shaders as original and as replacement; PX4 a Final hook
// that swaps a member's material is re-asserted (every member slot a mosaic material again, the warning names the hook and
// the member, the plugin is told), the hook sees the character (node, root, renderers, bones), AfterMosaic cannot be
// registered through the Api, FK node rows are joined, and the unload removes the hooks (no re-assert after it); PX5 the
// supply of an existing HS2Next shader maps later loads, a second plugin stacks on it, withdrawing restores the one below and
// the mapping from before, the unload withdraws; PX6 the UI registration (off-panel UI context): panels, sections, actions
// and strings under "<guid>/", a throwing factory gives an error label and a counted fault, the unload removes everything and
// leaves the app's string table as it was. Numbers only: no image is written or shown.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.App.Plugins.Engine;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.UI;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using OverrideResult = HS2Next.Api.Unity.OverrideResult;

public static partial class BridgeCoreTest
{
    const string PxGuid = "stp3.test.points", PxOther = "stp3.test.other";

    static PluginEngineServices PxServices(Ctx c, string guid, List<string> log, Func<UiContext> ui = null) =>
        new PluginEngineServices(guid, new PluginGuard(guid, l => log.Add(l)), l => log.Add(l), l => log.Add(l), () => c.App?.Runtime, ui);

    /// <summary>BR-Chan loaded (into a fresh host unless <paramref name="sameHost"/>); its built context (null when the load
    /// failed: reported).</summary>
    static CharaContext PxFemale(Ctx c, string what, out string key, bool sameHost = false)
    {
        if (!sameHost || c.App == null) c.Host();
        key = C30Load(c, c.FemaleRel, "female", what + ": chara.load BR-Chan");
        if (key == null) return null;
        c.App.Runtime.ApplyNow();
        var ctx = c.App.Runtime.FindChara(key)?.Context;
        c.Check(ctx != null && ctx.Root, what + ": the character is built", "");
        return ctx;
    }

    static Renderer PxMember(CharaContext ctx, string name) =>
        ctx.Root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == name);

    static int PxProtectedFindings(Ctx c, string node) =>
        c.App.Runtime.Findings.Count(f => f.Code == MosaicSet.ProtectedCode && f.NodeId == node);

    /// <summary>PX1: the typed overrides through the Api.</summary>
    static void PX1(Ctx c)
    {
        var ctx = PxFemale(c, "PX1", out string key);
        if (ctx == null) return;
        var log = new List<string>();
        var s = PxServices(c, PxGuid, log);
        IUnityPluginServices u = s;
        var m = u.Materials;
        var patch = PxMember(ctx, "o_mnpb");
        if (!c.Check(patch && MosaicSet.IsMember(patch), "PX1: BR-Chan has the member o_mnpb", "")) return;
        var before = patch.sharedMaterials.ToArray();
        var res = new[]
        {
            m.SetFloat(patch, 0, "_WhiteOff", 0f),
            m.SetColor(patch, 0, "_Color", Color.red),
            m.SetTexture(patch, 0, "_MainTex", Texture2D.whiteTexture),
            m.SetShader(patch, 0, Shader.Find("HS2Next/AIT Item")),
            m.SetEnabled(patch, false),
        };
        c.Check(res.All(r => r == OverrideResult.Protected) && patch.enabled && patch.sharedMaterials.SequenceEqual(before) && before.All(MosaicUnit.IsUnitMaterial),
            "PX1: float, colour, texture, shader and enabled on a member are refused (Protected) and change nothing", string.Join(",", res));
        c.Check(s.MosaicProtected == 5, "PX1: every refusal is counted for the plugin", "" + s.MosaicProtected);
        c.Check(log.Count(l => l.Contains("refused") && l.Contains("[plugin " + PxGuid + "]")) == 1, "PX1: one plugin-log line for the renderer", string.Join(" | ", log));
        c.Check(PxProtectedFindings(c, key) == 1, "PX1: one scene finding api.mosaicProtected on the character", "" + PxProtectedFindings(c, key));
        var body = ctx.BodyRenderer;
        float g0 = ctx.BodyDraw.GetFloat("_Gloss");
        var ok = m.SetFloat(body, 0, "_Gloss", 0.25f);
        c.Check(ok == OverrideResult.Applied && Mathf.Approximately(ctx.BodyDraw.GetFloat("_Gloss"), 0.25f), "PX1: an override on the body skin is applied", ok.ToString());
        ctx.BodyDraw.SetFloat("_Gloss", g0);
        c.Check(m.SetFloat(null, 0, "_Gloss", 1f) == OverrideResult.Missing && m.SetFloat(body, 99, "_Gloss", 1f) == OverrideResult.Missing &&
                m.SetFloat(body, 0, "_NoSuchProperty", 1f) == OverrideResult.Missing, "PX1: no renderer, slot or property → Missing", "");
        c.Check(s.MosaicProtected == 5, "PX1: Missing and Applied are not counted", "" + s.MosaicProtected);
        s.Unload();
        bool threw = false;
        try { _ = u.Materials; } catch (ObjectDisposedException) { threw = true; }
        c.Check(threw, "PX1: after the unload the services refuse (ObjectDisposedException)", "");
    }

    /// <summary>PX2: the additive mosaic through the Api.</summary>
    static void PX2(Ctx c)
    {
        var ctx = PxFemale(c, "PX2", out _);
        if (ctx == null) return;
        var log = new List<string>();
        var s = PxServices(c, PxGuid, log);
        IUnityPluginServices u = s;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.name = "px_added";
        cube.transform.SetParent(ctx.Root.transform, false);
        var cr = cube.GetComponent<MeshRenderer>();
        try
        {
            bool ok = u.AddMosaic(cr);
            c.Check(ok && MosaicSet.IsMember(cr) && MosaicSet.IsAdded(cr) && cr.sharedMaterials.All(MosaicUnit.IsUnitMaterial), "PX2: AddMosaic makes the renderer a member with its mosaic material", "");
            c.Check(!u.AddMosaic(null), "PX2: AddMosaic of no renderer → false", "");
            c.Check(u.Materials.SetFloat(cr, 0, "_WhiteOff", 0f) == OverrideResult.Protected, "PX2: the added member is refused by the overrides", "");
            s.Unload();
            c.Check(MosaicSet.IsMember(cr) && cr.sharedMaterials.All(MosaicUnit.IsUnitMaterial), "PX2: the plugin's unload keeps the member", "");
        }
        finally { Object.DestroyImmediate(cube); }
    }

    /// <summary>PX3: the shader supply refuses the mosaic shaders.</summary>
    static void PX3(Ctx c)
    {
        c.Host();
        var log = new List<string>();
        var s = PxServices(c, PxGuid, log);
        IShaderSupply sup = s;
        var pairs = new[] { ("AIT/mnpb", "HS2Next/AIT Item"), ("PX/Plain", MosaicSet.ShaderName), (MosaicSet.ShaderName, "HS2Next/AIT Item"), (MosaicSet.LegacyShader, "HS2Next/AIT Item"), ("PX/Plain", MosaicSet.OriginalShader) };
        var refused = pairs.Select(p => (p, ok: sup.Supply(p.Item1, p.Item2, out string why), why)).ToList();
        c.Check(refused.All(x => !x.ok && x.why != null && x.why.StartsWith(MosaicSet.ProtectedCode, StringComparison.Ordinal)) && !ShaderMap.Fitted.ContainsKey("PX/Plain"),
            "PX3: AIT/mnpb and the mosaic shaders are refused as original and as replacement (api.mosaicProtected)",
            string.Join("; ", refused.Select(x => $"{x.p.Item1} -> {x.p.Item2}: {(x.ok ? "accepted" : x.why)}")));
        c.Check(ShaderMap.TryResolve("AIT/mnpb", null, out var rn) && rn == MosaicSet.ShaderName, "PX3: AIT/mnpb still maps to HS2Next/Mosaic", rn ?? "none");
        c.Check(s.MosaicProtected == pairs.Length && sup.Supplied.Count == 0 && s.SupplyCount == 0, "PX3: the refusals are counted, nothing is supplied", "" + s.MosaicProtected);
        c.Check(!sup.Supply("", "HS2Next/AIT Item", out string why2) && why2 != null && !sup.Supply("PX/x", null, out _), "PX3: empty names are refused", why2 ?? "");
        s.Unload();
        PluginEngine.Install();
        c.Check(PluginHost.EngineFactory != null && PluginHost.EngineFactory.Method.Name == nameof(PluginEngine.Create) && PluginHost.EngineFactory.Method.DeclaringType == typeof(PluginEngine),
            "PX3: the engine layer is the loader's engine factory", "");
    }

    /// <summary>PX4: build hooks, the re-assert after a plugin's Final hook, AfterMosaic not registrable, FK rows, unload.</summary>
    static void PX4(Ctx c)
    {
        var log = new List<string>();
        var s = PxServices(c, PxGuid, log);
        IUnityPluginServices u = s;
        var seen = new List<string>();
        int swapped = 0;
        Material plain = null;
        try
        {
            // not registrable: the stage after the mosaic
            bool refused = false;
            try { u.BuildHooks.Register("late", (BuildStage)2, a => { }); } catch (ArgumentException) { refused = true; }
            c.Check(refused && !Enum.IsDefined(typeof(BuildStage), 2) && CharaBuildHooks.OwnedBy(PxGuid).Count == 0,
                "PX4: AfterMosaic cannot be registered through the Api (no enum value; the cast value is refused)", "");

            plain = new Material(Shader.Find("HS2Next/AIT Item") ?? Shader.Find("Universal Render Pipeline/Lit")) { name = "px_plain", hideFlags = HideFlags.DontSave };
            u.BuildHooks.Register("swap", BuildStage.Final, a =>
            {
                seen.Add($"{a.HookId}|{a.Stage}|{a.InBuild}|{a.NodeId}|{a.IsMale}|{(a.Root ? "root" : "-")}|{a.Renderers.Count}|{(a.FindBone("cf_J_Hips") ? "hips" : "-")}");
                int n = 0;
                foreach (var r in a.Renderers)
                {
                    if (!a.IsMosaicMember(r)) continue;
                    r.sharedMaterials = r.sharedMaterials.Select(_ => plain).ToArray();
                    n++;
                }
                a.RenderersChanged(n);
                a.Log("swapped " + n);
                swapped = n;
            });
            u.BuildHooks.Register("look", BuildStage.Textures, a => seen.Add("textures " + a.HookId));
            u.BuildHooks.RegisterFkNodes("rows", () => new[] { new FkNodeRow(9901, "px_bone", 0, 1) });
            c.Check(s.Hooks.SequenceEqual(new[] { "look@Textures", "swap@Final" }) && CharaBuildHooks.OwnedBy(PxGuid).All(h => h.Id.StartsWith(PxGuid + "/", StringComparison.Ordinal)),
                "PX4: the hooks are registered under <guid>/<id> and reported without the prefix", string.Join(",", s.Hooks));
            var fk = CharaBuildHooks.FkNodes(null);
            c.Check(fk != null && fk.Rows.Any(r => r.No == 9901 && r.Name == "px_bone" && r.Additional), "PX4: the plugin's FK node rows are joined (marked additional)", "");

            var ctx = PxFemale(c, "PX4", out string key);
            if (ctx == null) return;
            var st = MosaicSet.StateOf(ctx);
            c.Check(seen.Any(x => x.StartsWith("swap|Final|True|" + key + "|False|root|", StringComparison.Ordinal) && x.EndsWith("|hips", StringComparison.Ordinal)) &&
                    seen.Any(x => x == "textures look"), "PX4: the hooks ran in the build and saw the character (id, stage, node, sex, root, renderers, bones)", string.Join(" | ", seen));
            c.Check(swapped > 0 && st != null && st.Members.Count >= swapped && st.Members.All(mm => mm.Renderer.sharedMaterials.All(MosaicUnit.IsUnitMaterial)),
                "PX4: the hook swapped member materials; afterwards every member slot holds its mosaic material again",
                $"swapped {swapped}; " + (st == null ? "no record" : string.Join(", ", st.Members.Select(mm => mm.Path))));
            var warn = st?.Findings.Where(f => f.Code == MosaicSet.ProtectedCode).ToList() ?? new List<HS2Next.Save.Finding>();
            c.Check(warn.Count == swapped && warn.All(f => f.Message.Contains(PxGuid + "/swap")), "PX4: one warning api.mosaicProtected per member swapped, naming the hook", string.Join(" | ", warn.Select(f => f.Path + " " + f.Message)));
            c.Check(s.MosaicProtected == swapped && log.Count(l => l.Contains("hook swap") && l.Contains("put back")) == swapped, "PX4: the plugin is told (counted, one log line per member)", "" + s.MosaicProtected);
            c.Check(ctx.Stats.Hooks.TryGetValue(PxGuid + "/swap@Final", out var run) && run.Ran && run.Renderers == swapped && run.Errors == 0,
                "PX4: the build statistics name the plugin's hook", run?.ToString() ?? "none");
            c.Check(ctx.Log.Any(l => l.Contains("mosaic: re-asserted")), "PX4: the build log records the re-assert", "");

            // a throwing hook is a fault of the plugin; the build goes on
            u.BuildHooks.Register("throw", BuildStage.BodyMesh, a => throw new InvalidOperationException("px fault"));
            var ctx2 = PxFemale(c, "PX4 with a throwing hook", out _, sameHost: true);
            c.Check(ctx2 != null && ctx2.Stats.Hooks.TryGetValue(PxGuid + "/throw@BodyMesh", out var tr) && tr.Errors == 1, "PX4: a throwing hook is counted by the build, which goes on", "");
            c.Check(u.BuildHooks.Unregister("throw", BuildStage.BodyMesh) && !u.BuildHooks.Unregister("throw", BuildStage.BodyMesh), "PX4: Unregister removes it once", "");

            s.Unload();
            c.Check(CharaBuildHooks.OwnedBy(PxGuid).Count == 0 && CharaBuildHooks.FkNodes(null) == null && s.Hooks.Count == 0, "PX4: the unload removes the hooks and the FK node provider", "");
            seen.Clear();
            var ctx3 = PxFemale(c, "PX4 after the unload", out _, sameHost: true);
            var st3 = ctx3 != null ? MosaicSet.StateOf(ctx3) : null;
            c.Check(ctx3 != null && seen.Count == 0 && st3 != null && !st3.Findings.Any(f => f.Code == MosaicSet.ProtectedCode) && !ctx3.Log.Any(l => l.Contains("re-asserted")),
                "PX4: a build after the unload runs no plugin hook and no re-assert (the official build)", "");
        }
        finally
        {
            s.Unload();
            if (plain) Object.DestroyImmediate(plain);
        }
    }

    /// <summary>PX5: supplies of an existing HS2Next shader, the stack of two plugins, withdrawal and the unload.</summary>
    static void PX5(Ctx c)
    {
        c.Host();
        var log = new List<string>();
        var s1 = PxServices(c, PxGuid, log);
        var s2 = PxServices(c, PxOther, log);
        IShaderSupply a = s1, b = s2;
        const string orig = "PX/Original";
        string repl1 = "HS2Next/AIT Item";
        string repl2 = ShaderMap.Fitted.Values.FirstOrDefault(v => v.StartsWith("HS2Next/", StringComparison.Ordinal) && v != repl1 && !MosaicSet.IsMosaicShaderName(v) && Shader.Find(v));
        var existing = ShaderMap.Fitted.First(kv => !MosaicSet.IsMosaicShaderName(kv.Key) && kv.Value != repl1);
        try
        {
            c.Check(Shader.Find(repl1) && repl2 != null && !ShaderMap.Fitted.ContainsKey(orig), "PX5: two existing HS2Next shaders as replacements", repl2 ?? "none");
            c.Check(a.Supply(orig, repl1, out _) && ShaderMap.TryResolve(orig, null, out var r1) && r1 == repl1 && a.Supplied.Count == 1 && a.Supplied[0].Value == repl1,
                "PX5: a supply maps the original for later loads and is listed", "");
            c.Check(b.Supply(orig, repl2, out _) && ShaderMap.Fitted[orig] == repl2 && b.Supplied.Count == 1, "PX5: a second plugin's supply of the same original wins", "");
            c.Check(b.Withdraw(orig) && ShaderMap.Fitted[orig] == repl1 && b.Supplied.Count == 0, "PX5: withdrawing the top restores the one below", "");
            c.Check(!b.Withdraw(orig), "PX5: a second withdrawal finds nothing", "");
            c.Check(b.Supply(orig, repl2, out _) && a.Withdraw(orig) && ShaderMap.Fitted[orig] == repl2, "PX5: withdrawing the one below keeps the top", "");
            c.Check(b.Withdraw(orig) && !ShaderMap.Fitted.ContainsKey(orig), "PX5: withdrawing the last restores the absence of a mapping", "");
            // an original that already had a mapping: the unload restores it
            c.Check(a.Supply(existing.Key, repl1, out _) && ShaderMap.Fitted[existing.Key] == repl1 && s1.SupplyCount == 1, "PX5: a supply over an existing mapping", existing.Key);
            s1.Unload();
            c.Check(ShaderMap.Fitted[existing.Key] == existing.Value && ShaderMap.SuppliedBy(PxGuid).Count == 0, "PX5: the unload withdraws it and restores the previous mapping", existing.Key + " -> " + ShaderMap.Fitted[existing.Key]);
        }
        finally
        {
            s1.Unload();
            s2.Unload();
            ShaderMap.Fitted[existing.Key] = existing.Value;
            ShaderMap.Fitted.Remove(orig);
        }
    }

    /// <summary>PX6: the experimental UI registration on an off-panel UI context.</summary>
    static void PX6(Ctx c)
    {
        var app = c.Host();
        var ui = UiHost.BuildOffPanel(app);
        var table = L10n.Default.Table;
        var log = new List<string>();
        var s = PxServices(c, PxGuid, log, () => ui);
        IUnityPluginServices u = s;
        try
        {
            var p = u.Ui;
            c.Check(p != null && PxServices(c, PxOther, log).Ui == null, "PX6: the UI services exist with a UI context, null without", "");
            if (p == null) return;
            p.RegisterPanel("main", "plugin." + PxGuid + ".panel", "left", () => new Label("px panel") { name = "px-panel" });
            p.RegisterPanel("broken", "plugin." + PxGuid + ".panel", "bottom", () => throw new InvalidOperationException("px factory"));
            p.RegisterInspectorSection("item", "sec", "plugin." + PxGuid + ".section", sc => new Label(sc.Kind + ":" + sc.NodeId) { name = "px-section" });
            int ran = 0;
            p.RegisterAction("act", "plugin." + PxGuid + ".action", () => ran++);
            p.AddStrings("key\ten\tja\tnote\nplugin." + PxGuid + ".panel\tPX panel\tPX パネル\t\n");
            var reg = ui.Registry;
            reg.Panels.TryGet(PxGuid + "/main", out var pd);
            reg.Panels.TryGet(PxGuid + "/broken", out var bd);
            string shownIn = ui.Shell?.RegionOf(PxGuid + "/main");
            c.Check(pd != null && pd.DefaultRegion == "left" && bd != null && bd.DefaultRegion == "bottom" && shownIn == "left",
                "PX6: panels under <guid>/<id> in their regions", "");
            var el = pd?.Factory(ui);
            var bad = bd?.Factory(ui);
            c.Check(el is Label l1 && l1.text == "px panel" && bad is Label l2 && l2.text.Contains("failed") && log.Any(x => x.Contains("panel broken")),
                "PX6: a factory builds the plugin's element; a throwing one gives an error label and a counted fault", "");
            var sec = reg.SectionsFor("item").FirstOrDefault(d => d.Id == PxGuid + "/sec");
            c.Check(sec != null && sec.Factory(ui) is Label l3 && l3.text.StartsWith("item:", StringComparison.Ordinal), "PX6: an inspector section for the node kind", "");
            c.Check(reg.Actions.Run(PxGuid + "/act", new ActionInvocation(), out _) == ActionRunStatus.Ran && ran == 1 && reg.Actions.TryGet(PxGuid + "/act", out var ad) && ad.LabelKey == "plugin." + PxGuid + ".action",
                "PX6: an action runs through the registry with the plugin's label key", "");
            c.Check(L10n.Default.Get("plugin." + PxGuid + ".panel") == "PX panel" || L10n.Default.Get("plugin." + PxGuid + ".panel") == "PX パネル", "PX6: the plugin's strings are found", L10n.Default.Get("plugin." + PxGuid + ".panel"));
            bool refusedKey = false;
            try { p.AddStrings("key\ten\nother.key\tx\n"); } catch (ArgumentException) { refusedKey = true; }
            c.Check(refusedKey && !L10n.Default.TryGet("other.key", out _), "PX6: a key outside plugin.<guid>. is refused, nothing added", "");
            c.Check(s.UiCounts == (2, 1, 1, 1), "PX6: the counts for the plugin report", s.UiCounts.ToString());
            bool refusedRegion = false;
            try { p.RegisterPanel("x", null, "top", () => new Label()); } catch (ArgumentException) { refusedRegion = true; }
            c.Check(refusedRegion, "PX6: another region is refused", "");
            s.Unload();
            c.Check(!reg.Panels.TryGet(PxGuid + "/main", out _) && !reg.Panels.TryGet(PxGuid + "/broken", out _) && !reg.SectionsFor("item").Any(d => d.Id.StartsWith(PxGuid, StringComparison.Ordinal)) &&
                    !reg.Actions.Contains(PxGuid + "/act") && reg.Actions.Run(PxGuid + "/act", null, out _) == ActionRunStatus.Unknown,
                "PX6: the unload removes the panels, the section and the action", "");
            c.Check(ui.Shell == null || ui.Shell.RegionOf(PxGuid + "/main") == null && !ui.Shell.PanelRoots.ContainsKey(PxGuid + "/main"),
                "PX6: the shell drops the removed panel's tab", ui.Shell?.RegionOf(PxGuid + "/main") ?? "");
            c.Check(ReferenceEquals(L10n.Default.Table, table) && !L10n.Default.TryGet("plugin." + PxGuid + ".panel", out _), "PX6: the unload leaves the app's string table as it was", "");
        }
        finally
        {
            s.Unload();
            if (!ReferenceEquals(L10n.Default.Table, table)) L10n.Default.Load(table);
        }
    }
}
