// Tests of the mosaic (the mosaic set, method (a), method (b); package R of 5.M). Numbers only:
// every image stays in memory and is judged by pixel counts and run lengths of code-made test textures; nothing is written
// or shown. Targets: the SFW visual cards of Tools/targets.json (BR-Chan, HERO107), clothes hidden for the render rows.
//   Run (headless; rows MosaicTest, areas maker, chara-build):
//     MX1 members: BR-Chan (o_mnpa / o_mnpb texture mode from the body composite, cm_o_dan00 and the redirected cm_o_dan_f
//         procedural), HERO107 (cm_o_dan00 / cm_o_dan_f procedural), BR-Chan with its son object active (the stage changes
//         no activity), a renderer added through the additive API; every member renderer under the root is in the record
//         and every slot holds a material the mosaic unit made; N_mnpb stays active.
//     MX2 every member slot: HS2Next/Mosaic, queue 2500, no keyword; the shader has ForwardLit / DepthOnly / MotionVectors /
//         DepthNormals and no ShadowCaster pass, every pass with the constant-only polygon offset 0, -1
//         (HS2NextMosaicDepth.hlsl); ShaderMap maps AIT/mnpb to it.
//     MX3 the body-UV transfer of o_mnpb and o_mnpa: every vertex within 0.05 units of the body, the transferred UV equal to
//         the UV of the nearest point of the whole body surface (no candidate filter) within 1e-4, the "#bodyuv" copy drawn
//         with the transferred UVs; ZU1: the distances to the body and o_mnpb's / o_mnpa's own UV0 against the transfer.
//     MX4 a synthetic MaterialEditor document on BR-Chan: items on o_mnpa / o_mnpb / c_m_mnpb are not applied (skip reason
//         mosaicProtected, listed as me.mosaicProtected), an item on the body skin is.
//     MX5 patch guard: BR-Chan without N_mnpb gets o_mnpa / o_mnpb back from the vanilla body (bones of the body, texture
//         mode, finding mosaic.patchesRestored).
//     MX6 after a skin colour, a skin-overlay and a coordinate edit the texture members draw the body draw material's
//         current _MainTex and the character equals a fresh build of its documents (S2).
//     MX8 the typed overrides (E6) refuse members (Protected, api.mosaicProtected), accept the body; the shader supply (E8)
//         refuses AIT/mnpb and the mosaic shaders.
//     AM0-AM4 the rule of advanced pose (MosaicTest_AdvancedPose.cs).
//     MO0-MO5, MO8 the rule of material edits (MosaicTest_Materials.cs).
//   RunRender (graphics, own process; row MosaicTest.render): BR-Chan, clothes hidden, a code-made body composite whose
//     texels deep inside the patches' UV area are a one-texel marker checker (25 % pure green), the production URP asset:
//     MR1 0 marker pixels inside the members' footprint (eroded 4 px) from 0.3 to 30 m (3 to 300 units), three angles,
//         with SMAA, TAA and DOF; the positive control (patches off) shows the markers at 0.3 m.
//     MR2 the block floor at 504 x 704, 1920 x 1080 and a 2x capture of 1920 x 1080 (unlit test view, a gradient
//         composite): complete flat runs inside the footprint >= F - 1 px, F = max(4, ceil(L / 100)).
//     MR3 (b): the coverage contains the footprint dilated by 1.5 blocks, complete flat runs inside it >= B - 1, the option
//         off renders byte-identical to the renderer without the feature, a camera rendered inside another camera's render
//         (a map mirror) gets no wide pass.
//     MR4 the mosaic through ViewCapture, the scene thumbnail render and CharaThumbnail (view framing): 0 marker pixels, and
//         the markers visible with the patches off.
//     MR5 the procedural members' cells (the son object active) >= F - 1 px at 504 x 704 and 1920 x 1080.
//     MR7 the clothed targets (BR-Chan; HERO107 with its son object on): the first render after each build apart (the
//         members flat yellow into a target cleared to blue first): no more member pixels than a settled render of the same
//         view and than the members alone (every other renderer off); error-shader magenta or unrendered pixels are a WARN
//         (not the mosaic); it is also the camera's warm-up. Then five views: a member hidden by clothes gives no
//         pixel in (a) and no coverage / changed pixel in (b); a visible member is covered by (b). The same views with the
//         clothes hidden: no skin under a patch shows (the composite flat green inside the patches' UV area, the
//         patches flat (1, 0, 1): 0 green pixels), counted also for a plain material without the member's depth placement.
//     MO6 the skin on a transparent candidate and on one without _MainTex (MosaicTest_RenderMaterials.cs).
//   RunTiming (graphics, own process; row MosaicTest.timing): MR6 the camera step per character (ZU4, target 0.05 ms) and
//     the wide pass at 2560 x 1440 with two characters (ZU3, target 0.5 ms).
// Reports: Spike/out/maker/mosaic_report.txt, mosaic_render_report.txt, mosaic_timing_report.txt ("SUMMARY: ..." first);
// -only MX1,MR2 runs those groups. Exit code 1 on a FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Edit.Units;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Data.Ext;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static MakerTestKit;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

public static partial class MosaicTest
{
    static readonly string[] Patches = { "o_mnpa", "o_mnpb" };

    static MosaicMember Member(CharaContext ctx, string name) =>
        MosaicSet.StateOf(ctx)?.Members.FirstOrDefault(m => m.Renderer && m.Renderer.name == name);

    static Renderer Find(CharaContext ctx, string name) =>
        ctx?.Root ? ctx.Root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == name) : null;

    static FlushReport RunStage(CharaContext ctx)
    {
        ctx.Editor.MarkUnit(MosaicStageUnit.UnitId);
        return ctx.Editor.Flush();
    }

    // ================================================================ headless

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var h = MakerCoreTest.Harness.Open();
        try
        {
            if (Want("MX1")) Guard("MX1 members", () => Mx1(h));
            if (Want("MX2")) Guard("MX2 materials", () => Mx2(h));
            if (Want("MX3")) Guard("MX3 body-UV transfer (ZU1)", () => Mx3(h));
            if (Want("MX4")) Guard("MX4 MaterialEditor on members", () => Mx4(h));
            if (Want("MX5")) Guard("MX5 patch guard", () => Mx5(h));
            if (Want("MX6")) Guard("MX6 edits = fresh build", () => Mx6(h));
            if (Want("MX8")) Guard("MX8 typed overrides and shader supply", () => Mx8(h));
            AdvancedPoseGroups(h);
            ModifierGroups();
            MaterialGroups();
            ConstraintGroups();
        }
        finally { h.Dispose(); }
        Finish("mosaic_report.txt", "MosaicTest (headless): MX1-MX6, MX8, AM0-AM4, MO0-MO5, MO8", sw, "MOSAIC TEST");
    }

    static void Mx1(MakerCoreTest.Harness h)
    {
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"MX1 {label} builds", f.B.Ok, f.B.Error ?? "")) continue;
                var ctx = f.B.Context;
                var st = MosaicSet.StateOf(ctx);
                Check($"MX1 {label}: the mosaic stage ran", st != null && st.Runs >= 1, st == null ? "no record" : $"runs {st.Runs}, {st.LastMilliseconds:F2} ms");
                if (st == null) continue;
                var all = ctx.Root.GetComponentsInChildren<Renderer>(true).Where(MosaicSet.IsMember).ToList();
                var rec = new HashSet<Renderer>(st.Members.Select(m => m.Renderer));
                Check($"MX1 {label}: every member renderer under the root is in the record", all.All(rec.Contains) && rec.Count == all.Count,
                    $"{all.Count} members: " + string.Join(", ", st.Members.Select(m => $"{m.Path} {m.Mode}{(m.SourceKind != null ? "/" + m.SourceKind : "")}")));
                Check($"MX1 {label}: every member slot holds a mosaic-unit material", st.Members.All(m => m.Renderer.sharedMaterials.All(MosaicUnit.IsUnitMaterial)), "");
                var dan = Member(ctx, "cm_o_dan00");
                Check($"MX1 {label}: cm_o_dan00 is a procedural member", dan != null && dan.Mode == MosaicMode.Procedural, dan == null ? "missing" : dan.Mode.ToString());
                var danF = Find(ctx, "cm_o_dan_f");
                if (danF) Check($"MX1 {label}: cm_o_dan_f (redirected copy) is a procedural member", Member(ctx, "cm_o_dan_f")?.Mode == MosaicMode.Procedural, "");
                else Info($"MX1 {label}: no cm_o_dan_f (the redirect serves none)");
                if (male)
                {
                    Check($"MX1 {label}: no patches on the male body", Patches.All(n => !Find(ctx, n)), "");
                    continue;
                }
                var draw = ctx.BodyDraw ? ctx.BodyDraw.GetTexture("_MainTex") : null;
                foreach (var n in Patches)
                {
                    var m = Member(ctx, n);
                    Check($"MX1 {label}: {n} draws the body composite at body UVs", m != null && m.Mode == MosaicMode.Texture && m.SourceKind == "body" && m.BodySurface && m.Source == draw &&
                        m.Materials.All(x => x && x.GetTexture("_MainTex") == draw),
                        m == null ? "missing" : $"mode {m.Mode}, source {(m.Source ? m.Source.name : "none")}, body {(draw ? draw.name : "none")}");
                }
                var nm = ctx.Body ? HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_mnpb") : null;
                Check($"MX1 {label}: N_mnpb stays active", nm && nm.gameObject.activeSelf, "");

                // the son object on (the scene's son toggle activates N_dan): the stage changes no activity, members unchanged
                var nDan = ctx.Body ? HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan") : null;
                if (nDan)
                {
                    bool was = nDan.gameObject.activeSelf;
                    var before = st.Members.SelectMany(m => m.Renderer.sharedMaterials.Select(x => (m.Renderer, x))).ToList();
                    nDan.gameObject.SetActive(true);
                    var rep = RunStage(ctx);
                    var after = st.Members.SelectMany(m => m.Renderer.sharedMaterials.Select(x => (m.Renderer, x))).ToList();
                    Check($"MX1 {label} son object active: the stage keeps the activity and the same members / materials", nDan.gameObject.activeSelf && rep.Errors.Count == 0 && before.Count == after.Count && before.Zip(after, (u, v) => u.Item1 == v.Item1 && u.Item2 == v.Item2).All(z => z),
                        $"units {string.Join(",", rep.Units)}");
                    nDan.gameObject.SetActive(was);
                }

                // the additive API (E7): a cube under the character root becomes a member
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.name = "mosaic_test_added";
                cube.transform.SetParent(ctx.Root.transform, false);
                var cr = cube.GetComponent<MeshRenderer>();
                bool ok = MosaicSet.Add(cr);
                var am = Member(ctx, "mosaic_test_added");
                Check($"MX1 {label}: an added renderer is a member at once", ok && am != null && am.Added && cr.sharedMaterials.All(MosaicUnit.IsUnitMaterial), am == null ? "not in the record" : am.Mode.ToString());
                RunStage(ctx);
                am = Member(ctx, "mosaic_test_added");
                Check($"MX1 {label}: an added renderer stays a member after the next stage run", am != null && am.Added && MosaicSet.IsMember(cr), "");
                Object.DestroyImmediate(cube);
            }
    }

    static void Mx2(MakerCoreTest.Harness h)
    {
        var sh = Shader.Find(MosaicSet.ShaderName);
        Check("MX2 HS2Next/Mosaic exists and compiles", sh && !ShaderUtil.ShaderHasError(sh), sh ? "" : "not found");
        Check("MX2 ShaderMap maps AIT/mnpb to HS2Next/Mosaic", ShaderMap.TryResolve("AIT/mnpb", null, out var rn) && rn == MosaicSet.ShaderName, rn ?? "none");
        if (sh)
        {
            var probe = new Material(sh);
            var passes = new[] { "ForwardLit", "DepthOnly", "MotionVectors", "DepthNormals" };
            Check("MX2 passes ForwardLit / DepthOnly / MotionVectors / DepthNormals, no ShadowCaster", passes.All(p => probe.FindPass(p) >= 0) && probe.FindPass("ShadowCaster") < 0,
                string.Join(", ", Enumerable.Range(0, probe.passCount).Select(probe.GetPassName)));
            Check("MX2 default queue 2500", probe.renderQueue == MosaicSet.Queue, "" + probe.renderQueue);
            Object.DestroyImmediate(probe);
            string path = AssetDatabase.GetAssetPath(sh);
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            int nPass = System.Text.RegularExpressions.Regex.Matches(text, @"^\s*Pass\s*$", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
            int nOffset = System.Text.RegularExpressions.Regex.Matches(text, @"^\s*Offset 0, -1\s*$", System.Text.RegularExpressions.RegexOptions.Multiline).Count;
            Check("MX2 every pass has the constant polygon offset 0, -1", nPass > 0 && nOffset == nPass, $"{nPass} passes, {nOffset} offsets ({path})");
        }
        foreach (var (label, file) in new[] { ("BR-Chan", FemaleCard), ("HERO107", MaleCard) })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"MX2 {label} builds", f.B.Ok, f.B.Error ?? "")) continue;
                var st = MosaicSet.StateOf(f.B.Context);
                var bad = new List<string>();
                int slots = 0;
                foreach (var m in st.Members)
                    foreach (var mat in m.Renderer.sharedMaterials)
                    {
                        slots++;
                        if (!mat || !mat.shader || mat.shader.name != MosaicSet.ShaderName || mat.renderQueue != MosaicSet.Queue || mat.shaderKeywords.Length > 0)
                            bad.Add($"{m.Path}: {(mat && mat.shader ? mat.shader.name : "none")} {(mat ? mat.renderQueue : 0)} [{(mat ? string.Join(" ", mat.shaderKeywords) : "")}]");
                    }
                Check($"MX2 {label}: every member slot is HS2Next/Mosaic, queue 2500, no keyword ({slots} slots)", slots > 0 && bad.Count == 0, string.Join("; ", bad));
            }
    }

    static void Mx3(MakerCoreTest.Harness h)
    {
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("MX3 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            var body = ctx.BodyRenderer;
            var bm = body ? body.sharedMesh : null;
            Check("MX3 the body mesh is readable", bm && bm.isReadable, bm ? bm.name : "none");
            if (!bm || !bm.isReadable) return;
            foreach (var n in Patches)
            {
                var smr = Find(ctx, n) as SkinnedMeshRenderer;
                if (!Check($"MX3 {n} exists", smr, "")) continue;
                int before = MosaicUvTransfer.Computed;
                var t = MosaicUvTransfer.Transfer(smr, body);
                Check($"MX3 {n}: the transfer is cached (no new computation)", t != null && MosaicUvTransfer.Computed == before, $"computed {before} -> {MosaicUvTransfer.Computed}");
                if (t == null) continue;
                Check($"MX3 {n}: every vertex within 0.05 units of the body", t.BodySurface && t.MaxDistance <= MosaicUvTransfer.SurfaceDistance,
                    $"distance median {t.MedianDistance:F5}, max {t.MaxDistance:F5} units; matched through {t.SharedBone ?? "identity"}; {t.Milliseconds:F1} ms");
                var src = t.Source;
                var toBody = MosaicUvTransfer.MemberToBody(src, smr.bones, bm, body.bones, out _);
                var v = src.vertices;
                float maxErr = 0f, maxD = 0f;
                for (int i = 0; i < v.Length; i++)
                {
                    var uv = MosaicUvTransfer.NearestBodyUv(bm, toBody.MultiplyPoint3x4(v[i]), out float d);
                    maxErr = Mathf.Max(maxErr, (uv - t.Uv[i]).magnitude);
                    maxD = Mathf.Max(maxD, d);
                }
                Check($"MX3 {n}: transferred UV = the whole body surface's nearest UV within 1e-4", maxErr <= 1e-4f, $"max error {maxErr:E2} UV over {v.Length} vertices; nearest distance max {maxD:F5}");
                var own = src.uv;
                var diffs = Enumerable.Range(0, Math.Min(own.Length, t.Uv.Length)).Select(i => (double)(own[i] - t.Uv[i]).magnitude).OrderBy(x => x).ToList();
                if (diffs.Count > 0) Info($"ZU1 {n}: own UV0 vs transferred: median {diffs[diffs.Count / 2]:F4}, max {diffs[diffs.Count - 1]:F4} UV; region UV per unit {t.Region?.UvPerUnit:F4}, radius {t.Region?.Radius:F4} units");
                if (n == "o_mnpb" && diffs.Count > 0) Check("MX3 o_mnpb: its own UV0 lies on the body's UVs (median within 0.005)", diffs[diffs.Count / 2] <= 0.005, $"median {diffs[diffs.Count / 2]:F4}");
                var drawn = smr.sharedMesh;
                Check($"MX3 {n}: the drawn mesh is the \"#bodyuv\" copy with the transferred UVs", drawn == t.Copy && drawn.name == src.name + MosaicUvTransfer.CopySuffix &&
                    drawn.vertexCount == src.vertexCount && drawn.uv.SequenceEqual(t.Uv), drawn ? drawn.name : "none");
            }
        }
    }

    static void Mx4(MakerCoreTest.Harness h)
    {
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("MX4 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            var draw = ctx.BodyDraw;
            string drawName = MaterialEditorApplier.Fmt(draw ? draw.name : "");
            var me = new MaterialEditorData();
            foreach (var n in new[] { "o_mnpa", "o_mnpb", "cm_o_dan00" })
                me.RendererProperties.Add(new MaterialEditorData.RendererProperty { ObjectType = OT.Character, RendererName = n, Property = MaterialEditorData.RendererPropertyType.Enabled, Value = "0", ValueOriginal = "1" });
            me.FloatProperties.Add(new MaterialEditorData.FloatProperty { ObjectType = OT.Character, MaterialName = MosaicSet.OriginalMaterial, Property = "WhiteOff", Value = "0", ValueOriginal = "1" });
            me.FloatProperties.Add(new MaterialEditorData.FloatProperty { ObjectType = OT.Character, MaterialName = drawName, Property = "Gloss", Value = "0.123", ValueOriginal = "0.5" });
            ctx.Ext ??= new CardExtData();
            var prev = ctx.Ext.MaterialEditor;
            ctx.Ext.MaterialEditor = me;
            try
            {
                ctx.Stats.MeSkipped.TryGetValue(MosaicSet.MeSkipReason, out int skip0);
                MaterialEditorApplier.Apply(ctx, me, ctx.Options ?? new CharaExtOptions());
                ctx.Stats.MeSkipped.TryGetValue(MosaicSet.MeSkipReason, out int skip1);
                Check("MX4 the four member items are skipped (mosaicProtected)", skip1 - skip0 == 4, $"skipped {skip1 - skip0}");
                Check("MX4 the patches and cm_o_dan00 stay enabled", new[] { "o_mnpa", "o_mnpb", "cm_o_dan00" }.All(n => Find(ctx, n) && Find(ctx, n).enabled), "");
                var st = MosaicSet.StateOf(ctx);
                Check("MX4 the members' _WhiteOff stays 1", st.Members.SelectMany(m => m.Renderer.sharedMaterials).All(x => x && Mathf.Approximately(x.GetFloat("_WhiteOff"), 1f)), "");
                Check("MX4 the item on the body skin is applied", draw && Mathf.Approximately(draw.GetFloat("_Gloss"), 0.123f), draw ? "" + draw.GetFloat("_Gloss") : "no body draw");
                var prot = MosaicSet.ProtectedItems(ctx);
                Check("MX4 the read model lists the four items (me.mosaicProtected)", prot.Count == 4, string.Join("; ", prot));
                Check("MX4 the document keeps the items", me.ItemCount == 5, "" + me.ItemCount);
            }
            finally { ctx.Ext.MaterialEditor = prev; }
        }
    }

    static void Mx5(MakerCoreTest.Harness h)
    {
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("MX5 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            var n = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_mnpb");
            if (!Check("MX5 N_mnpb exists before", n, "")) return;
            Object.DestroyImmediate(n.gameObject);
            var rep = RunStage(ctx);
            var st = MosaicSet.StateOf(ctx);
            var n2 = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_mnpb");
            Check("MX5 N_mnpb is back under the body with o_mnpa / o_mnpb", n2 && Patches.All(p => HS2Next.Chara.Edit.EditContext.Find(n2, p)?.GetComponent<Renderer>()) && rep.Errors.Count == 0,
                $"units {string.Join(",", rep.Units)}; errors {string.Join("; ", rep.Errors)}");
            var bones = new HashSet<Transform>(ctx.BodyBones.Values);
            foreach (var p in Patches)
            {
                var m = Member(ctx, p);
                var smr = m?.Renderer as SkinnedMeshRenderer;
                Check($"MX5 {p}: restored, bound to the body bones, texture mode", m != null && m.Restored && smr && smr.bones.All(b => b && bones.Contains(b)) && m.Mode == MosaicMode.Texture,
                    m == null ? "missing" : $"restored {m.Restored}, mode {m.Mode}, bones {smr?.bones.Length}");
            }
            Check("MX5 finding mosaic.patchesRestored", st.Findings.Has(MosaicSet.RestoredCode) && st.Restored == 2, string.Join("; ", st.Findings.Select(x => x.ToString())));
            var rep2 = RunStage(ctx);
            Check("MX5 a second run restores nothing and keeps the restored patches as members", MosaicSet.StateOf(ctx).Restored == 0 && Patches.All(p => Member(ctx, p)?.Restored == true), "");
        }
    }

    static void Mx6(MakerCoreTest.Harness h)
    {
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("MX6 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var live = f.B;
            var ctx = live.Context;
            void Judge(string what, FlushReport r)
            {
                var draw = ctx.BodyDraw ? ctx.BodyDraw.GetTexture("_MainTex") : null;
                bool src = Patches.All(p => Member(ctx, p) is MosaicMember m && m.Source == draw && m.Materials.All(x => x.GetTexture("_MainTex") == draw));
                Check($"MX6 {what}: the stage ran and the patches draw the body draw material's _MainTex", r.Units.Contains(MosaicStageUnit.UnitId) && r.Errors.Count == 0 && src,
                    $"units {string.Join(",", r.Units)}; source {(draw ? draw.name : "none")}");
                var d = VsFresh(h, live, out string err);
                Check($"MX6 {what}: equal to a fresh build of the documents", d != null && d.Equal, err ?? (d == null ? "" : d.ToString()));
            }
            var b = ctx.Chara.Body;
            Judge("skin colour", Set(live, "chara.body.skin.color", () => b.Skin.Color = (Color4)AltC(b.Skin.Color)));
            Judge("skin overlays", Set(live, "chara.body.skinOverlays", () => { }));
            var part = ctx.Coord.Clothes.Parts[0];
            string next = NextRow(ctx.Catalog, 240, part.Item);
            if (next != null)
                Judge("coordinate (top item)", Set(live, "coord.clothes.top.item", () => { part.Item = next; part.MaterialOverrides.Clear(); }));
            else Warn("MX6 coordinate", "no other top row");
        }
    }

    static void Mx8(MakerCoreTest.Harness h)
    {
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("MX8 BR-Chan builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            var r = Find(ctx, "o_mnpb");
            var fl = new FindingList();
            var mat = r.sharedMaterials[0];
            float w0 = mat.GetFloat("_WhiteOff");
            var res = new[]
            {
                MaterialOverrides.SetFloat(r, 0, "_WhiteOff", 0f, fl),
                MaterialOverrides.SetColor(r, 0, "_Color", Color.red, fl),
                MaterialOverrides.SetTexture(r, 0, "_MainTex", Texture2D.whiteTexture, fl),
                MaterialOverrides.SetShader(r, 0, Shader.Find("HS2Next/AIT Item"), fl),
                MaterialOverrides.SetEnabled(r, false, fl),
            };
            Check("MX8 E6: every typed override on a member is Protected (api.mosaicProtected)", res.All(x => x == OverrideResult.Protected) && fl.CountOf(MosaicSet.ProtectedCode) == res.Length &&
                r.enabled && r.sharedMaterials[0] == mat && mat.shader.name == MosaicSet.ShaderName && mat.GetFloat("_WhiteOff") == w0, string.Join(", ", res));
            var body = ctx.BodyRenderer;
            float g0 = ctx.BodyDraw.GetFloat("_Gloss");
            var ok = MaterialOverrides.SetFloat(body, 0, "_Gloss", 0.25f, fl);
            Check("MX8 E6: an override on the body skin is applied", ok == OverrideResult.Applied && Mathf.Approximately(ctx.BodyDraw.GetFloat("_Gloss"), 0.25f), ok.ToString());
            ctx.BodyDraw.SetFloat("_Gloss", g0);
            var refused = new[] { ("AIT/mnpb", "HS2Next/AIT Item"), ("MosaicTest/Plain", MosaicSet.ShaderName), (MosaicSet.ShaderName, "HS2Next/AIT Item"), (MosaicSet.LegacyShader, "HS2Next/AIT Item") }
                .Select(p => (p, ok: ShaderMap.Supply(p.Item1, p.Item2, out string why), why)).ToList();
            Check("MX8 E8: the shader supply refuses AIT/mnpb and the mosaic shaders", refused.All(x => !x.ok) && !ShaderMap.Fitted.ContainsKey("MosaicTest/Plain"),
                string.Join("; ", refused.Select(x => $"{x.p.Item1} -> {x.p.Item2}: {(x.ok ? "accepted" : x.why)}")));
            Check("MX8 E8: AIT/mnpb still maps to HS2Next/Mosaic", ShaderMap.TryResolve("AIT/mnpb", null, out var rn) && rn == MosaicSet.ShaderName, rn ?? "none");
            bool accepted = ShaderMap.Supply("MosaicTest/Other", "HS2Next/AIT Item", out _);
            Check("MX8 E8: another shader is accepted", accepted && ShaderMap.Fitted.TryGetValue("MosaicTest/Other", out var v) && v == "HS2Next/AIT Item", "");
            ShaderMap.Fitted.Remove("MosaicTest/Other");
        }
    }

    // ================================================================ graphics: helpers

    const float Fov = 23f;
    static readonly int UnlitId = Shader.PropertyToID("_HS2MosaicUnlit");
    static readonly List<Object> trash = new List<Object>();
    static T Keep<T>(T o) where T : Object { trash.Add(o); return o; }
    static void Cleanup() { foreach (var o in trash) if (o) Object.DestroyImmediate(o); trash.Clear(); }

    static Camera NewCam(bool post, AntialiasingMode aa)
    {
        var go = Keep(new GameObject("mosaic_test_cam"));
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = Fov; cam.nearClipPlane = 0.3f; cam.farClipPlane = 2000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 1f);
        cam.allowHDR = true; cam.allowMSAA = false;
        var urp = go.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = post;
        urp.antialiasing = aa;
        urp.antialiasingQuality = AntialiasingQuality.High;
        urp.renderShadows = false;
        return cam;
    }

    /// <summary>Renders <paramref name="frames"/> times into a linear float target and returns the last frame (in memory).</summary>
    static Color[] Render(Camera cam, int w, int h, int frames = 1, Color? preClear = null)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        if (preClear is Color pc)
        {
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt; GL.Clear(true, true, pc); RenderTexture.active = prev;
        }
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        for (int i = 0; i < frames; i++) cam.Render();
        cam.targetTexture = null;
        // read back through the GPU read-back queue (a CPU texture of a 4K float frame overflows the D3D12 upload buffer)
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAHalf);
        req.WaitForCompletion();
        var px = new Color[w * h];
        if (!req.hasError)
        {
            var d = req.GetData<ushort>();
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(Mathf.HalfToFloat(d[4 * i]), Mathf.HalfToFloat(d[4 * i + 1]), Mathf.HalfToFloat(d[4 * i + 2]), Mathf.HalfToFloat(d[4 * i + 3]));
        }
        rt.Release(); Object.DestroyImmediate(rt);
        return px;
    }

    static Texture2D magenta;

    /// <summary>The members' footprint (post-processing and AA off): every member slot draws a flat (1, 0, 1) texture in
    /// texture mode with the unlit test view, then its material values come back.</summary>
    static bool[] Footprint(Camera cam, int w, int h, CharaContext ctx) => FootprintPixels(cam, w, h, ctx, out _);

    static bool[] FootprintPixels(Camera cam, int w, int h, CharaContext ctx, out Color[] px)
    {
        if (!magenta) magenta = Flat("mosaic_test_flat", new Color32(255, 0, 255, 255));
        px = MarkerRender(cam, w, h, ctx, magenta);
        return px.Select(c => c.r > 0.9f && c.g < 0.1f && c.b > 0.9f).ToArray();
    }

    static Texture2D Flat(string name, Color32 c)
    {
        var t = Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name });
        t.SetPixels32(Enumerable.Repeat(c, 16).ToArray());
        t.Apply();
        return t;
    }

    /// <summary>The members drawn flat with <paramref name="marker"/> (texture mode, the unlit test view; post-processing,
    /// AA and the wide pass off), everything else as it is; the members' material values come back.</summary>
    static Color[] MarkerRender(Camera cam, int w, int h, CharaContext ctx, Texture2D marker, Color? preClear = null)
    {
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();
        bool post = urp.renderPostProcessing; var aa = urp.antialiasing; bool wide = MosaicWide.Enabled;
        urp.renderPostProcessing = false; urp.antialiasing = AntialiasingMode.None; MosaicWide.Enabled = false;
        var saved = new List<(Material m, Texture t, Vector2 sc, Vector2 of, float mode, Color c)>();
        foreach (var mm in MosaicSet.StateOf(ctx).Members)
            foreach (var mat in mm.Renderer.sharedMaterials)
            {
                if (!mat || saved.Exists(x => x.m == mat)) continue;
                saved.Add((mat, mat.GetTexture("_MainTex"), mat.GetTextureScale("_MainTex"), mat.GetTextureOffset("_MainTex"), mat.GetFloat("_MosaicMode"), mat.GetColor("_Color")));
                mat.SetTexture("_MainTex", marker); mat.SetTextureScale("_MainTex", Vector2.one); mat.SetTextureOffset("_MainTex", Vector2.zero);
                mat.SetFloat("_MosaicMode", 1f); mat.SetColor("_Color", Color.white);
            }
        Shader.SetGlobalFloat(UnlitId, 1f);
        try { return Render(cam, w, h, 1, preClear); }
        finally
        {
            foreach (var x in saved)
            {
                x.m.SetTexture("_MainTex", x.t); x.m.SetTextureScale("_MainTex", x.sc); x.m.SetTextureOffset("_MainTex", x.of);
                x.m.SetFloat("_MosaicMode", x.mode); x.m.SetColor("_Color", x.c);
            }
            Shader.SetGlobalFloat(UnlitId, 0f); urp.renderPostProcessing = post; urp.antialiasing = aa; MosaicWide.Enabled = wide;
        }
    }

    static bool[] Erode(bool[] m, int w, int h, int r)
    {
        var o = new bool[m.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool all = true;
                for (int dy = -r; dy <= r && all; dy++)
                    for (int dx = -r; dx <= r && all; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        all = xx >= 0 && yy >= 0 && xx < w && yy < h && m[yy * w + xx];
                    }
                o[y * w + x] = all;
            }
        return o;
    }

    static bool IsMarker(Color c) => c.g > 0.08f && c.g > 1.5f * Mathf.Max(c.r, c.b);
    static int Markers(Color[] px, bool[] mask) { int n = 0; for (int i = 0; i < px.Length; i++) if ((mask == null || mask[i]) && IsMarker(px[i])) n++; return n; }

    /// <summary>Lengths of the complete flat runs (rows and columns) inside the mask: maximal runs of one colour whose both
    /// neighbours are inside the mask and of another colour.</summary>
    static List<int> Runs(Color[] px, bool[] mask, int w, int h)
    {
        var l = new List<int>();
        bool Same(int a, int b) => px[a] == px[b];
        void Line(int count, Func<int, int> at)
        {
            int i = 0;
            while (i < count)
            {
                if (!mask[at(i)]) { i++; continue; }
                int j = i;
                while (j + 1 < count && mask[at(j + 1)] && Same(at(j + 1), at(i))) j++;
                bool left = i > 0 && mask[at(i - 1)], right = j + 1 < count && mask[at(j + 1)];
                if (left && right) l.Add(j - i + 1);
                i = j + 1;
            }
        }
        for (int y = 0; y < h; y++) Line(w, x => y * w + x);
        for (int x = 0; x < w; x++) Line(h, y => y * w + x);
        return l;
    }

    static Color[] Downscale(Color[] px, int w, int h, int k)
    {
        int ow = w / k, oh = h / k;
        var o = new Color[ow * oh];
        for (int y = 0; y < oh; y++)
            for (int x = 0; x < ow; x++)
            {
                Color s = Color.clear;
                for (int dy = 0; dy < k; dy++) for (int dx = 0; dx < k; dx++) s += px[(y * k + dy) * w + x * k + dx];
                o[y * ow + x] = s / (k * k);
            }
        return o;
    }

    /// <summary>The body composite of the render rows: skin colour, and in the texels at least <paramref name="inset"/>
    /// texels inside the patches' transferred UV triangles <paramref name="inner"/> (marker checker or gradient).</summary>
    static Texture2D Composite(CharaContext ctx, int size, int inset, Func<int, int, Color32> inner, Color32 skin)
    {
        var inside = new bool[size * size];
        foreach (var n in Patches)
        {
            var smr = Find(ctx, n) as SkinnedMeshRenderer;
            var t = smr ? MosaicUvTransfer.Transfer(smr, ctx.BodyRenderer) : null;
            if (t == null) continue;
            var tris = t.Source.triangles;
            for (int k = 0; k + 2 < tris.Length; k += 3)
            {
                Vector2 a = t.Uv[tris[k]] * size, b = t.Uv[tris[k + 1]] * size, c = t.Uv[tris[k + 2]] * size;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (InTri(new Vector2(x + 0.5f, y + 0.5f), a, b, c)) inside[y * size + x] = true;
            }
        }
        var eroded = Erode(inside, size, size, inset);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = eroded[y * size + x] ? inner(x, y) : skin;
        var tex = Keep(new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "mosaic_test_composite", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear });
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static readonly Color32 Skin = new Color32(214, 168, 142, 255);
    static Color32 MarkerTexel(int x, int y) => (x & 1) == 0 && (y & 1) == 0 ? new Color32(0, 255, 0, 255) : Skin;
    // a smooth gradient over the texture (block means differ from block to block)
    static Func<int, int, Color32> Gradient(int size) => (x, y) => new Color32((byte)(x * 255 / size), (byte)(y * 255 / size), (byte)(255 - (x + y) * 127 / size), 255);

    /// <summary>The body draw material's _MainTex replaced by <paramref name="tex"/> and the mosaic stage run again.</summary>
    static void UseComposite(CharaContext ctx, Texture tex)
    {
        ctx.BodyDraw.SetTexture("_MainTex", tex);
        RunStage(ctx);
    }

    /// <summary>The world centre and mean normal of o_mnpb now.</summary>
    static (Vector3 c, Vector3 n) PatchPose(CharaContext ctx)
    {
        var m = Member(ctx, "o_mnpb");
        var M = MosaicCamera.RegionMatrix(m);
        var n = M.MultiplyVector(m.Region.Normal).normalized;
        if (n == Vector3.zero) n = ctx.Root.transform.forward;
        return (M.MultiplyPoint3x4(m.Region.Centroid), n);
    }

    static Light headLight;

    /// <summary>The camera at <paramref name="dist"/> from <paramref name="target"/> along <paramref name="dir"/>, looking at it;
    /// the test light (when there is one) shines along the view, slightly from above (HS2's chara light under the camera).</summary>
    static void Aim(Camera cam, Vector3 target, Vector3 dir, float dist)
    {
        cam.transform.position = target + dir.normalized * dist;
        cam.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
        if (headLight) headLight.transform.rotation = cam.transform.rotation * Quaternion.Euler(10f, 0f, 0f);
    }

    static void HideClothes(CharaContext ctx)
    {
        foreach (var c in ctx.Clothes) if (c) c.SetActive(false);
        foreach (var a in ctx.Accessories) if (a?.Object) a.Object.SetActive(false);
        // the body's clothes cut-outs off too (the skin under the patches is the positive control)
        foreach (var p in new[] { "_alpha_a", "_alpha_b", "_alpha_c", "_alpha_d" })
            if (ctx.BodyDraw && ctx.BodyDraw.HasProperty(p)) ctx.BodyDraw.SetFloat(p, p == "_alpha_c" ? 1f : 0f);
    }

    /// <summary>The flat-run judgement of MR2 / MR5: the median complete run at least F and the lower quartile at least
    /// F - 1 (runs cut by the patch's own folds or by another part are partial blocks and pull the minimum down).</summary>
    static bool RunsOk(List<int> runs, float floor, out string detail)
    {
        runs.Sort();
        int p25 = runs[runs.Count / 4], med = runs[runs.Count / 2];
        detail = $"min {runs[0]}, p25 {p25}, median {med}, {runs.Count} runs";
        return med >= floor && p25 >= floor - 1;
    }

    static Light NewLight()
    {
        var go = Keep(new GameObject("mosaic_test_light"));
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional; l.intensity = 1f; l.color = Color.white; l.shadows = LightShadows.None;
        RenderingLayers.ApplyToLight(l);   // every layer (the characters' renderers are on their own rendering layers)
        go.transform.rotation = Quaternion.Euler(40f, 160f, 0f);
        headLight = l;
        return l;
    }

    sealed class Pipeline : IDisposable
    {
        readonly RenderPipelineAsset d, q; readonly bool async, wide; readonly HS2DofFeature.Input dof;
        readonly AmbientMode ambientMode; readonly Color ambient;
        public readonly bool Ok;
        public Pipeline()
        {
            d = GraphicsSettings.defaultRenderPipeline; q = QualitySettings.renderPipeline; async = ShaderUtil.allowAsyncCompilation; wide = MosaicWide.Enabled; dof = HS2DofFeature.Inputs;
            ambientMode = RenderSettings.ambientMode; ambient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.35f, 1f);
            var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
            Ok = prod && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!Ok) return;
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = prod; QualitySettings.renderPipeline = prod;
        }
        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline = d; QualitySettings.renderPipeline = q; ShaderUtil.allowAsyncCompilation = async;
            MosaicWide.Enabled = wide; MosaicWide.DebugView = false; HS2DofFeature.Inputs = dof;
            Shader.SetGlobalFloat(UnlitId, 0f);
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
            headLight = null;
        }
    }

    static MosaicWideFeature WideFeature()
    {
        var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
        var rd = prod && prod.rendererDataList.Length > 0 ? prod.rendererDataList[0] as UniversalRendererData : null;
        return rd ? rd.rendererFeatures.OfType<MosaicWideFeature>().FirstOrDefault() : null;
    }

    // ================================================================ graphics: MR1-MR5

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        using (var p = new Pipeline())
        {
            if (!Check("MR0 production URP asset and a graphics device", p.Ok, SystemInfo.graphicsDeviceType.ToString())) { Finish("mosaic_render_report.txt", "MosaicTest.render", sw, "MOSAIC TEST"); return; }
            var wf = WideFeature();
            Check("MR0 the renderer has MosaicWideFeature after HS2DofFeature, active, with its shader", wf && wf.isActive && wf.Shader && !ShaderUtil.ShaderHasError(wf.Shader), wf ? wf.name : "missing");
            var h = MakerCoreTest.Harness.Open();
            try
            {
                using (var f = new Disposer(h.Build(FemaleCard)))
                {
                    if (Check("MR0 BR-Chan builds", f.B.Ok, f.B.Error ?? ""))
                    {
                        var ctx = f.B.Context;
                        HideClothes(ctx);
                        NewLight();
                        if (Want("MR1")) Guard("MR1 no marker pixel in the footprint", () => Mr1(ctx));
                        if (Want("MR2")) Guard("MR2 block floor", () => Mr2(ctx));
                        if (Want("MR3")) Guard("MR3 wide mosaic", () => Mr3(ctx, wf));
                        if (Want("MR5")) Guard("MR5 procedural cells", () => Mr5(ctx));
                    }
                }
                if (Want("MR4")) Guard("MR4 capture paths", () => Mr4(h));
                if (Want("MR7")) Guard("MR7 members under clothes", () => Mr7(h));
                if (Want("MO6")) Guard("MO6 the skin on any candidate", () => Mo6(h));
                if (OnlyNamed("MO6all")) Guard("MO6all the skin on every candidate", () => Mo6All(h));
                WetGroups(h);
                RagdollGroups(h);
                BakeGroups(h);
            }
            finally { Cleanup(); h.Dispose(); }
        }
        Finish("mosaic_render_report.txt", "MosaicTest.render: MR1-MR5, MR7, MO6 (images in memory only)", sw, "MOSAIC TEST");
    }

    static void Mr1(CharaContext ctx)
    {
        UseComposite(ctx, Composite(ctx, 2048, 6, MarkerTexel, Skin));
        MosaicWide.Enabled = false;
        var (c, n) = PatchPose(ctx);
        var right = Vector3.Cross(Vector3.up, n).normalized;
        var dirs = new[] { ("front", n), ("30 deg up", Quaternion.AngleAxis(-30f, right) * n), ("45 deg side", Quaternion.AngleAxis(45f, Vector3.up) * n) };
        float[] dists = { 3f, 10f, 30f, 100f, 300f };
        const int W = 1280, H = 720;
        // positive control: the same camera, post-processing and detector see the marker checker when it is drawn without
        // the mosaic (an unlit test quad textured with the composite's marker texels, in front of the patch at 0.3 m; the
        // skin under the patches renders black in this view, so it shows no marker either way)
        foreach (var (cmode, caa, cframes) in new[] { ("SMAA", AntialiasingMode.SubpixelMorphologicalAntiAliasing, 1), ("TAA", AntialiasingMode.TemporalAntiAliasing, 8) })
        {
            var control = NewCam(true, caa);
            Aim(control, c, n, 3f);
            var ctex = Keep(new Texture2D(64, 64, TextureFormat.RGBA32, true, false) { name = "mosaic_test_marker", filterMode = FilterMode.Trilinear });
            var cpx32 = new Color32[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) cpx32[y * 64 + x] = MarkerTexel(x, y);
            ctex.SetPixels32(cpx32); ctex.Apply(true);
            var cm = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            cm.SetTexture("_BaseMap", ctex); cm.SetColor("_BaseColor", Color.white);
            var cq = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
            Object.DestroyImmediate(cq.GetComponent<Collider>());
            cq.GetComponent<MeshRenderer>().sharedMaterial = cm;
            // 0.1 units in front of the patch, 0.6 units wide: one marker texel is about 6 pixels, as the composite's at 0.3 m
            cq.transform.position = c + n.normalized * 0.1f;
            cq.transform.rotation = Quaternion.LookRotation(-n.normalized, Vector3.up);   // the quad's front faces local -z: the camera
            cq.transform.localScale = Vector3.one * 0.6f;
            int ctrl = Markers(Render(control, W, H, cframes), null);
            Object.DestroyImmediate(cq);
            Check($"MR1 control ({cmode}): the marker checker drawn without the mosaic shows at 0.3 m", ctrl > 50, $"{ctrl} marker pixels");
        }
        foreach (var (mode, aa, dofOn, frames) in new[] { ("SMAA", AntialiasingMode.SubpixelMorphologicalAntiAliasing, false, 1), ("TAA", AntialiasingMode.TemporalAntiAliasing, false, 8), ("DOF", AntialiasingMode.SubpixelMorphologicalAntiAliasing, true, 1) })
        {
            var cam = NewCam(true, aa);
            int worst = 0, fpTotal = 0, lumN = 0;
            double lum = 0;
            var lines = new List<string>();
            foreach (var (dn, dir) in dirs)
                foreach (var d in dists)
                {
                    Aim(cam, c, dir, d);
                    HS2DofFeature.Inputs = dofOn ? new HS2DofFeature.Input { Enabled = true, FocusPoint = c + dir.normalized * (d * 0.5f), FocalSize = 0.05f, Aperture = 0.6f } : default;
                    var fp = Erode(Footprint(cam, W, H, ctx), W, H, 4);
                    var px = Render(cam, W, H, frames);
                    int fpn = fp.Count(x => x), mk = Markers(px, fp);
                    worst = Math.Max(worst, mk); fpTotal += fpn;
                    for (int i = 0; i < px.Length; i++) if (fp[i]) { lum += px[i].r + px[i].g + px[i].b; lumN++; }
                    lines.Add($"{dn} {d / 10f:0.#} m: footprint {fpn} px, markers {mk}, block {Member(ctx, "o_mnpb")?.BlockPx:F1} px / {Member(ctx, "o_mnpb")?.BlockUv:0.######} UV");
                }
            HS2DofFeature.Inputs = default;
            double meanLum = lumN > 0 ? lum / (3 * lumN) : 0;
            Check($"MR1 {mode}: 0 marker pixels inside the footprint (15 views; the patches lit)", worst == 0 && fpTotal > 0 && meanLum > 0.05,
                $"worst {worst}, footprint total {fpTotal} px, mean level {meanLum:F3}");
            foreach (var l in lines) Info($"MR1 {mode} {l}");
        }
        // with the wide mosaic on as well
        MosaicWide.Enabled = true;
        var camB = NewCam(true, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
        Aim(camB, c, n, 3f);
        var fpB = Erode(Footprint(camB, W, H, ctx), W, H, 4);
        Check("MR1 (a) and (b): 0 marker pixels at 0.3 m", Markers(Render(camB, W, H), fpB) == 0, "");
        MosaicWide.Enabled = false;
    }

    static void Mr2(CharaContext ctx)
    {
        MosaicWide.Enabled = false;
        float[] dists = { 3f, 10f, 30f, 100f };
        var sizes = new[] { ("504 x 704", 504, 704, 1), ("1920 x 1080", 1920, 1080, 1), ("2x capture of 1920 x 1080", 1920, 1080, 2) };
        Shader.SetGlobalFloat(UnlitId, 1f);
        try
        {
            // (1) the patch: the rule's block (pixels along the densest UV direction) >= F and >= HS2's cell, and the image
            // quantized (few distinct colours in the footprint against a texel-level gradient composite)
            UseComposite(ctx, Composite(ctx, 2048, 0, Gradient(2048), Skin));
            var (c, n) = PatchPose(ctx);
            var cam = NewCam(false, AntialiasingMode.None);
            foreach (var (label, w, hh, k) in sizes)
            {
                float floor = Mathf.Max(4f, Mathf.Ceil(Mathf.Max(w, hh) / 100f));
                foreach (var d in dists.Take(3))
                {
                    Aim(cam, c, n, d);
                    Color[] px; bool[] fp;
                    using (MosaicCamera.OutputScope(cam, k))
                    {
                        fp = Footprint(cam, w * k, hh * k, ctx);
                        Shader.SetGlobalFloat(UnlitId, 1f);
                        px = Render(cam, w * k, hh * k);
                    }
                    var m = Member(ctx, "o_mnpb");
                    var colours = new HashSet<Color>();
                    int area = 0;
                    for (int i = 0; i < px.Length; i++) if (fp[i]) { colours.Add(px[i]); area++; }
                    int bound = area / Mathf.Max(1, (int)(floor * k * floor * k / 2)) + 16;
                    Check($"MR2 patch {label} {d / 10f:0.#} m: block >= F and >= HS2's cell; the footprint quantized",
                        m != null && m.BlockPx / k >= floor - 1e-3f && m.BlockPx >= m.CellPx - 1e-3f && colours.Count <= bound,
                        $"block {m?.BlockPx / k:F1} output px ({m?.BlockUv:0.######} UV), HS2 cell {m?.CellPx / k:F1} px, F {floor}; {colours.Count} colours in {area} px (bound {bound})");
                }
            }
            // (2) a frontal test quad added to the character (E7, its own gradient texture at its own UV0): complete blocks
            // on screen are at least F (the 2x capture judged after its 2 x 2 box downscale, as the capture writes it)
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.name = "mosaic_test_quad";
            quad.transform.SetParent(ctx.Root.transform, false);
            var qtex = Keep(new Texture2D(1024, 1024, TextureFormat.RGBA32, true, false) { name = "mosaic_test_quad_tex", wrapMode = TextureWrapMode.Clamp });
            var qpx = new Color32[1024 * 1024];
            var grad = Gradient(1024);
            for (int y = 0; y < 1024; y++) for (int x = 0; x < 1024; x++) qpx[y * 1024 + x] = grad(x, y);
            qtex.SetPixels32(qpx); qtex.Apply(true);
            var qm = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            qm.SetTexture("_MainTex", qtex); qm.SetTexture("_BaseMap", qtex);
            var qr = quad.GetComponent<MeshRenderer>();
            qr.sharedMaterial = qm;
            MosaicSet.Add(qr);
            var qmem = Member(ctx, "mosaic_test_quad");
            Check("MR2 the test quad is a texture-mode member with its own texture", qmem != null && qmem.Mode == MosaicMode.Texture && qmem.Source == qtex, qmem?.Mode.ToString() ?? "missing");
            var qcam = NewCam(false, AntialiasingMode.None);
            try
            {
                foreach (var (label, w, hh, k) in sizes)
                {
                    float floor = Mathf.Max(4f, Mathf.Ceil(Mathf.Max(w, hh) / 100f));
                    foreach (var d in dists)
                    {
                        // the quad fills 80 % of the view height at distance d, facing the camera
                        float size = 0.8f * 2f * d * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
                        quad.transform.position = new Vector3(0f, 500f, 0f);
                        quad.transform.rotation = Quaternion.identity;
                        quad.transform.localScale = new Vector3(size, size, 1f);
                        qcam.transform.position = quad.transform.position - new Vector3(0f, 0f, d);
                        qcam.transform.rotation = Quaternion.identity;
                        Color[] px; bool[] fp;
                        using (MosaicCamera.OutputScope(qcam, k))
                        {
                            fp = Footprint(qcam, w * k, hh * k, ctx);
                            Shader.SetGlobalFloat(UnlitId, 1f);
                            px = Render(qcam, w * k, hh * k);
                        }
                        if (k > 1)
                        {
                            px = Downscale(px, w * k, hh * k, k);
                            fp = Downscale(fp.Select(x => x ? Color.white : Color.black).ToArray(), w * k, hh * k, k).Select(x => x.r > 0.99f).ToArray();
                        }
                        // the block edges of the downscaled frame may fall inside a pixel (one blended pixel per edge)
                        var runs = Runs(px, fp, w, hh).Where(x => k == 1 || x > 1).ToList();
                        runs.Sort();
                        if (runs.Count == 0) { Info($"MR2 quad {label} {d / 10f:0.#} m: no complete block ({fp.Count(x => x)} px)"); continue; }
                        Check($"MR2 quad {label} {d / 10f:0.#} m: complete flat runs >= F - {k} (F {floor})", runs[0] >= floor - k,
                            $"min {runs[0]}, median {runs[runs.Count / 2]}, {runs.Count} runs; block {qmem?.BlockPx / k:F1} output px ({qmem?.BlockUv:0.######} UV), HS2 cell {qmem?.CellPx / k:F1} px");
                    }
                }
            }
            finally { Object.DestroyImmediate(quad); RunStage(ctx); }
        }
        finally { Shader.SetGlobalFloat(UnlitId, 0f); }
    }

    static void Mr3(CharaContext ctx, MosaicWideFeature wf)
    {
        UseComposite(ctx, Composite(ctx, 2048, 0, Gradient(2048), Skin));
        var (c, n) = PatchPose(ctx);
        const int W = 1280, H = 720;
        var cam = NewCam(true, AntialiasingMode.None);
        foreach (var d in new[] { 3f, 10f, 30f })
        {
            Aim(cam, c, n, d);
            var fp = Footprint(cam, W, H, ctx);
            MosaicWide.Enabled = true;
            MosaicWide.DebugView = true;
            int e0 = MosaicWideFeature.EnqueuedCount;
            var cov = Render(cam, W, H);
            bool enq = MosaicWideFeature.EnqueuedCount > e0;
            var g = MosaicWideFeature.LastGroups.FirstOrDefault();
            MosaicWide.DebugView = false;
            float B = g.Block;
            int miss = 0, fpn = 0;
            float r = 1.5f * B;
            int ir = Mathf.CeilToInt(r);
            var covered = cov.Select(x => x.r > 0.5f).ToArray();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (!fp[y * W + x]) continue;
                    fpn++;
                    for (int dy = -ir; dy <= ir; dy++)
                        for (int dx = -ir; dx <= ir; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= W || yy >= H || dx * dx + dy * dy > r * r) continue;
                            if (!covered[yy * W + xx]) { miss++; covered[yy * W + xx] = true; }
                        }
                }
            float expectB = MosaicWideFeature.BlockPx(Mathf.Max(W, H), g.Diameter);
            Check($"MR3 {d / 10f:0.#} m: the wide pass ran with B = max(4, ceil(L / 100), round(D / 12)) and covers the footprint dilated by 1.5 B",
                enq && fpn > 0 && miss == 0 && Mathf.Approximately(B, expectB) && wf && wf.Pass.LastMaskByDepthBuffer,
                $"B {B} (expected {expectB}, D {g.Diameter:F1} px), footprint {fpn} px, uncovered {miss} px; mask by the camera's depth buffer {(wf && wf.Pass.LastMaskByDepthBuffer)}");
            var px = Render(cam, W, H);
            var runs = Runs(px, cov.Select(x => x.r > 0.5f).ToArray(), W, H);
            runs.Sort();
            if (runs.Count > 0) Check($"MR3 {d / 10f:0.#} m: complete flat runs in the covered area >= B - 1", runs[0] >= B - 1, $"min {runs[0]}, median {runs[runs.Count / 2]}, {runs.Count} runs");
            else Info($"MR3 {d / 10f:0.#} m: no complete block run");
        }
        // off = byte-identical to the renderer without the feature
        Aim(cam, c, n, 3f);
        MosaicWide.Enabled = false;
        var off = Render(cam, W, H);
        bool was = wf && wf.isActive;
        if (wf) wf.SetActive(false);
        Color[] none;
        try { MosaicWide.Enabled = true; none = Render(cam, W, H); }
        finally { if (wf) wf.SetActive(was); MosaicWide.Enabled = false; }
        Check("MR3 the option off renders byte-identical to the renderer without the feature", off.SequenceEqual(none), "");
        // a camera rendered inside another camera's render (map mirrors) gets no wide pass
        MosaicWide.Enabled = true;
        var inner = NewCam(true, AntialiasingMode.None);
        Aim(inner, c, n, 5f);
        inner.targetTexture = Keep(new RenderTexture(256, 256, 24));
        int nested = 0, outerCount = 0;
        void Begin(ScriptableRenderContext rc, Camera cm)
        {
            if (cm != cam) return;
#pragma warning disable 618
            UniversalRenderPipeline.RenderSingleCamera(rc, inner);
#pragma warning restore 618
            var fr = MosaicCamera.FrameOf(inner);
            if (fr == null) nested++;   // the inner frame ended inside the callback
        }
        RenderPipelineManager.beginCameraRendering += Begin;
        int e1 = MosaicWideFeature.EnqueuedCount;
        try { Render(cam, W, H); outerCount = MosaicWideFeature.EnqueuedCount - e1; }
        finally { RenderPipelineManager.beginCameraRendering -= Begin; MosaicWide.Enabled = false; }
        Check("MR3 a nested camera (map mirror) gets no wide pass; the outer camera does (ZU3)", outerCount == 1, $"enqueued {outerCount} for 2 camera renders");
        Info("ZU3 map mirror cameras: CameraType.Game rendered inside the view camera's callback (MapMirrors); the wide pass skips nested camera renders");
    }

    static void Mr5(CharaContext ctx)
    {
        var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
        if (!Check("MR5 N_dan exists", nDan, "")) return;
        bool was = nDan.gameObject.activeSelf;
        nDan.gameObject.SetActive(true);
        foreach (var p in Patches) Find(ctx, p).enabled = false;
        MosaicWide.Enabled = false;
        var dan = Member(ctx, "cm_o_dan00");
        var M = MosaicCamera.RegionMatrix(dan);
        var c = M.MultiplyPoint3x4(dan.Region.Centroid);
        var n = ctx.Root.transform.forward;
        var cam = NewCam(false, AntialiasingMode.None);
        try
        {
            foreach (var (label, w, hh) in new[] { ("504 x 704", 504, 704), ("1920 x 1080", 1920, 1080) })
            {
                float floor = Mathf.Max(4f, Mathf.Ceil(Mathf.Max(w, hh) / 100f));
                foreach (var d in new[] { 3f, 10f, 30f })
                {
                    Aim(cam, c, n, d);
                    var fp = Footprint(cam, w, hh, ctx);
                    var px = Render(cam, w, hh);
                    var runs = Runs(px, fp, w, hh);
                    if (runs.Count == 0) { Info($"MR5 {label} {d / 10f:0.#} m: no complete cell (footprint {fp.Count(x => x)} px)"); continue; }
                    bool ok = RunsOk(runs, floor, out string det);
                    Check($"MR5 {label} {d / 10f:0.#} m: procedural cells: median >= F, p25 >= F - 1 (F {floor})", ok && dan.CellPx >= floor, $"{det}; cell {dan.CellPx:F1} px");
                }
            }
        }
        finally
        {
            nDan.gameObject.SetActive(was);
            foreach (var p in Patches) Find(ctx, p).enabled = true;
        }
    }

    /// <summary>The members' footprint drawn by a plain unlit (1, 0, 1) material without the polygon offset (post-processing
    /// and AA off): where a member wins the depth test without the offset.</summary>
    static bool[] FootprintPlain(Camera cam, int w, int h, CharaContext ctx) => FootprintPlain(cam, w, h, ctx, out _);

    static bool[] FootprintPlain(Camera cam, int w, int h, CharaContext ctx, out Color[] px)
    {
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();
        bool post = urp.renderPostProcessing; var aa = urp.antialiasing; bool wide = MosaicWide.Enabled;
        urp.renderPostProcessing = false; urp.antialiasing = AntialiasingMode.None; MosaicWide.Enabled = false;
        var plain = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        plain.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
        var saved = new List<(Renderer r, Material[] m)>();
        foreach (var mm in MosaicSet.StateOf(ctx).Members)
        {
            saved.Add((mm.Renderer, mm.Renderer.sharedMaterials));
            mm.Renderer.sharedMaterials = Enumerable.Repeat(plain, mm.Renderer.sharedMaterials.Length).ToArray();
        }
        try { px = Render(cam, w, h); return px.Select(c => c.r > 0.9f && c.g < 0.1f && c.b > 0.9f).ToArray(); }
        finally
        {
            foreach (var (r, m) in saved) r.sharedMaterials = m;
            urp.renderPostProcessing = post; urp.antialiasing = aa; MosaicWide.Enabled = wide;
        }
    }

    /// <summary>
    /// MR7: the clothed targets (BR-Chan; HERO107 with its son object on) in five views. Per view: the members' visible
    /// pixels with the mosaic material (constant polygon offset 0, -1) and with a plain material without it, then the wide
    /// pass: its coverage (debug view) and the pixels it changes against the option off. A member hidden by clothes must give
    /// 0 pixels in (a) and no coverage / no changed pixel in (b); where a member is visible, (b) covers it.
    /// </summary>
    static Texture2D yellow;
    static readonly ShaderTagId LightMode = new ShaderTagId("LightMode");
    static readonly HashSet<string> BuiltInModes = new HashSet<string> { "Always", "ForwardBase", "ForwardAdd", "PrepassBase", "PrepassFinal", "Vertex", "VertexLMRGBM", "VertexLM", "Deferred" };

    /// <summary>The shader's first subshader has only built-in light modes (the pipeline would draw it in its error colour).</summary>
    static bool LegacyOnly(Shader sh)
    {
        int n = sh.GetPassCountInSubshader(0);
        if (n == 0) return false;
        for (int p = 0; p < n; p++)
        {
            var t = sh.FindPassTagValue(0, p, LightMode).name;
            if (!BuiltInModes.Contains(t) && t != "ShadowCaster" && t != "Meta") return false;
        }
        return true;
    }

    /// <summary>
    /// The first render of a freshly built character, measured apart from a settled one (same camera, same state, rendered
    /// right after) and from the members alone (every other renderer off: the most member pixels the view can hold): the
    /// members flat yellow (1, 1, 0) - so the error shader's magenta is told apart - into a target cleared to blue first (a
    /// render that does not run shows blue, not stale memory). Before it: shaders still compiling, slots without a working
    /// shader, the members' meshes and bones. It is also the camera's warm-up: the views after it start on a settled state.
    /// </summary>
    static (int first, int settled) FirstRender(string label, Camera cam, int w, int h, CharaContext ctx)
    {
        if (!yellow) yellow = Flat("mosaic_test_flat_yellow", new Color32(255, 255, 0, 255));
        int badSlots = 0, slots = 0;
        var bad = new List<string>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
            foreach (var m in r.sharedMaterials)
            {
                slots++;
                if (m && m.shader && m.shader.isSupported && !ShaderUtil.ShaderHasError(m.shader) && m.shader.name != "Hidden/InternalErrorShader" && !LegacyOnly(m.shader)) continue;
                badSlots++;
                if (bad.Count < 6) bad.Add($"{r.name}:{(m ? m.shader ? m.shader.name : "no shader" : "no material")}");
            }
        }
        var mem = MosaicSet.StateOf(ctx).Members.Select(m =>
        {
            var smr = m.Renderer as SkinnedMeshRenderer;
            var mesh = smr ? smr.sharedMesh : m.Renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return $"{m.Renderer.name} {(m.Renderer.enabled && m.Renderer.gameObject.activeInHierarchy ? "on" : "off")} {(mesh ? mesh.name : "no mesh")}" +
                   (smr ? $" v{(mesh ? mesh.vertexCount : 0)} bones {smr.bones.Length}/{(mesh ? mesh.bindposes.Length : 0)}" : "");
        });
        Info($"MR7 {label} before the first render: shaders compiling {ShaderUtil.anythingCompiling}; {badSlots} of {slots} drawn slots in the scene without a working pipeline shader (error, unsupported, or only built-in passes, which the pipeline draws in its error colour)" +
             (bad.Count > 0 ? $" ({string.Join(", ", bad)})" : "") + $"; members: {string.Join("; ", mem)}");
        var blue = new Color(0f, 0f, 1f, 1f);
        (int member, int error, int unrendered, string box) Count(Color[] px)
        {
            int n = 0, e = 0, u = 0, x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.r > 0.9f && c.g > 0.9f && c.b < 0.1f)
                {
                    n++; int x = i % w, y = i / w;
                    x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                }
                else if (c.r > 0.9f && c.g < 0.1f && c.b > 0.9f) e++;
                else if (c.r < 0.01f && c.g < 0.01f && c.b > 0.99f) u++;
            }
            return (n, e, u, n > 0 ? $"x {x0}-{x1}, y {y0}-{y1}" : "none");
        }
        var firstPx = MarkerRender(cam, w, h, ctx, yellow, blue);
        var a = Count(firstPx);
        var settledPx = MarkerRender(cam, w, h, ctx, yellow, blue);
        var b = Count(settledPx);
        // the members alone (every other renderer of the scene off): the most member pixels this view can hold
        var members = new HashSet<Renderer>(MosaicSet.StateOf(ctx).Members.Select(m => m.Renderer));
        var others = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => !members.Contains(r) && !r.forceRenderingOff).ToList();
        foreach (var r in others) r.forceRenderingOff = true;
        (int member, int error, int unrendered, string box) alone;
        try { alone = Count(MarkerRender(cam, w, h, ctx, yellow, blue)); }
        finally { foreach (var r in others) r.forceRenderingOff = false; }
        // the character's drawn pixels (not the camera's background, the settled render's corner)
        int Drawn(Color[] px) { var bg = settledPx[0]; int n = 0; foreach (var c in px) if (Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 0.004f) n++; return n; }
        Info($"MR7 {label} not the background: first render {Drawn(firstPx)} px, settled {Drawn(settledPx)} px");
        Info($"MR7 {label} first render after the build: member px {a.member} ({a.box}), error-shader px {a.error}, unrendered px {a.unrendered}; " +
             $"settled render: member px {b.member} ({b.box}), error-shader px {b.error}, unrendered px {b.unrendered}; " +
             $"the members alone: {alone.member} px ({alone.box})");
        Check($"MR7 {label}: the first render after the build draws the members as a settled one (no more, within the members alone)",
            a.member == b.member && a.member <= alone.member,
            $"member px {a.member} first / {b.member} settled / {alone.member} alone");
        if (a.error > 0)
        {
            // which renderers drew the error colour: each one's visible pixels in the settled frame (the pixels that change
            // when it alone is off), and how many of them were error-coloured in the first render
            bool Magenta(Color c) => c.r > 0.9f && c.g < 0.1f && c.b > 0.9f;
            var rows = new List<(string name, int visible, int error)>();
            foreach (var r in others)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                r.forceRenderingOff = true;
                Color[] without;
                try { without = MarkerRender(cam, w, h, ctx, yellow, blue); }
                finally { r.forceRenderingOff = false; }
                int vis = 0, err = 0;
                for (int i = 0; i < without.Length; i++)
                    if (without[i] != settledPx[i]) { vis++; if (Magenta(firstPx[i])) err++; }
                if (err > 0) rows.Add(($"{r.name} [{string.Join(", ", r.sharedMaterials.Where(m => m).Select(m => m.shader ? m.shader.name : "no shader").Distinct())}]", vis, err));
            }
            Info($"MR7 {label} the first render's error-colour pixels by renderer (its visible pixels in the settled frame): " +
                 (rows.Count == 0 ? "none attributed" : string.Join("; ", rows.OrderByDescending(x => x.error).Take(8).Select(x => $"{x.name} {x.error} of its {x.visible} px"))));
        }
        if (a.error + b.error + a.unrendered + b.unrendered > 0)
            Warn($"MR7 {label}: a frame not drawn as the scene is (not the mosaic: the error shader's magenta or the clear colour)",
                $"error-shader px {a.error} / {b.error}, unrendered px {a.unrendered} / {b.unrendered}");
        return (a.member, b.member);
    }

    static void Mr7(MakerCoreTest.Harness h)
    {
        const int W = 600, H = 800;
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"MR7 {label} builds (clothed)", f.B.Ok, f.B.Error ?? "")) continue;
                var ctx = f.B.Context;
                var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
                if (male && nDan) nDan.gameObject.SetActive(true);   // the son object on: its parts are under the trousers
                var focus = Member(ctx, male ? "cm_o_dan00" : "o_mnpb");
                if (!Check($"MR7 {label}: the focus member exists", focus != null, "")) continue;
                var c = MosaicCamera.RegionMatrix(focus).MultiplyPoint3x4(focus.Region.Centroid);
                var fwd = ctx.Root.transform.forward; var right = ctx.Root.transform.right;
                var chest = ctx.Root.transform.position + Vector3.up * 9f;
                var views = new (string name, Vector3 target, Vector3 dir, float dist)[]
                {
                    ("front full body", chest, fwd, 30f), ("front hips", c, fwd, 12f), ("front close", c, fwd, 5f),
                    ("3/4", c, Quaternion.AngleAxis(40f, Vector3.up) * fwd, 12f), ("from below", c, Quaternion.AngleAxis(35f, right) * fwd, 10f),
                };
                var cam = NewCam(true, AntialiasingMode.None);
                Aim(cam, views[0].target, views[0].dir, views[0].dist);
                FirstRender(label, cam, W, H, ctx);
                int hiddenViews = 0, badViews = 0;
                foreach (var v in views)
                {
                    Aim(cam, v.target, v.dir, v.dist);
                    int a = Footprint(cam, W, H, ctx).Count(x => x);
                    int plain = FootprintPlain(cam, W, H, ctx).Count(x => x);
                    MosaicWide.Enabled = false;
                    var off = Render(cam, W, H);
                    MosaicWide.Enabled = true;
                    int e0 = MosaicWideFeature.EnqueuedCount;
                    var on = Render(cam, W, H);
                    bool enq = MosaicWideFeature.EnqueuedCount > e0;
                    var g = MosaicWideFeature.LastGroups.FirstOrDefault();
                    MosaicWide.DebugView = true;
                    int cov = Render(cam, W, H).Count(x => x.r > 0.5f);
                    MosaicWide.DebugView = false;
                    MosaicWide.Enabled = false;
                    int changed = 0; float maxd = 0f;
                    for (int i = 0; i < on.Length; i++)
                    {
                        float d = Mathf.Max(Mathf.Abs(on[i].r - off[i].r), Mathf.Max(Mathf.Abs(on[i].g - off[i].g), Mathf.Abs(on[i].b - off[i].b)));
                        if (d > 0f) { changed++; maxd = Mathf.Max(maxd, d); }
                    }
                    Info($"MR7 {label} {v.name}: (a) visible member px {a} (plain, no offset: {plain}); (b) enqueued {enq}" +
                         (enq ? $" (block {g.Block:F1} px, diameter {g.Diameter:F1} px)" : "") + $", coverage {cov} px, changed {changed} px (max |d| {maxd:F3})");
                    if (a == 0)
                    {
                        hiddenViews++;
                        if (cov > 0 || changed > 0) badViews++;
                    }
                    else if (cov == 0) badViews++;
                }
                Check($"MR7 {label} clothed: members hidden by clothes give no (a) pixel and no (b) coverage; visible ones are covered",
                    badViews == 0, $"{hiddenViews} of {views.Length} views with the members hidden, {badViews} views wrong");
                // the same views with the clothes hidden: the composite flat green inside the patches' UV area (1 texel in); the members
                // drawn flat (1, 0, 1) - with their own depth placement and with a plain material - so a green pixel is skin from
                // under a patch that shows (the female patches lie on the skin; the male parts have no composite under them)
                HideClothes(ctx);
                if (!male) UseComposite(ctx, Composite(ctx, 2048, 1, (x, y) => new Color32(0, 255, 0, 255), Skin));
                int lossTotal = 0, fpTotal = 0, leakTotal = 0, leakPlain = 0;
                foreach (var v in views)
                    foreach (var dist in new[] { v.dist, v.dist * 0.5f })
                    {
                        Aim(cam, v.target, v.dir, dist);
                        var fo = FootprintPixels(cam, W, H, ctx, out var foPx);
                        var fpl = FootprintPlain(cam, W, H, ctx, out var plPx);
                        int no = fo.Count(x => x), np = fpl.Count(x => x), loss = 0;
                        for (int i = 0; i < fo.Length; i++) if (fo[i] && !fpl[i]) loss++;
                        int leak = male ? 0 : Markers(foPx, null), leakP = male ? 0 : Markers(plPx, null);
                        lossTotal += loss; fpTotal += no; leakTotal += leak; leakPlain += leakP;
                        Info($"MR7 {label} unclothed {v.name} at {dist / 10f:0.##} m: member px {no} (plain {np}, only with the depth placement {loss}); skin from under the patches: {leak} px (plain {leakP})");
                    }
                Check($"MR7 {label} unclothed: no skin from under the patches shows", leakTotal == 0, $"{leakTotal} px (a plain material without the depth placement: {leakPlain} px); {lossTotal} of {fpTotal} member pixels win only by the placement");
            }
    }

    static void Mr4(MakerCoreTest.Harness h)
    {
        var node = NodeOf(h, FemaleCard);
        try
        {
            var real = node.Real;
            var ctx = real.Context;
            HideClothes(ctx);
            UseComposite(ctx, Composite(ctx, 2048, 6, MarkerTexel, Skin));
            var (c, n) = PatchPose(ctx);
            var view = NewCam(true, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
            Aim(view, c, n, 3f);
            int Count(byte[] png)
            {
                if (png == null) return -1;
                var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
                t.LoadImage(png);
                int k = Markers(t.GetPixels(), null);
                Object.DestroyImmediate(t);
                return k;
            }
            int Tex(Texture2D t) { if (!t) return -1; int k = Markers(t.GetPixels(), null); Object.DestroyImmediate(t); return k; }
            var paths = new (string name, Func<int> run)[]
            {
                ("ViewCapture", () => Count(ViewCapture.Render(view, 1280, 720))),
                ("scene thumbnail render", () => Count(HS2Next.Save.ThumbnailCapture.CaptureView(view, new ThumbnailSettings { CharaLighting = false }))),
                ("CharaThumbnail (view framing)", () => Tex(CharaThumbnail.CaptureTexture(real, CharaThumbnailFraming.View, view, null, out _))),
            };
            foreach (var (name, run) in paths)
            {
                foreach (var p in Patches) Find(ctx, p).enabled = false;
                int without = run();
                foreach (var p in Patches) Find(ctx, p).enabled = true;
                int with = run();
                Check($"MR4 {name}: the mosaic is drawn (0 marker pixels; {without} with the patches off)", with == 0 && without > 20, $"with {with}, without {without}");
            }
        }
        finally { node.Dispose(); }
    }

    // ================================================================ timing: MR6 (ZU3, ZU4)

    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        using (var p = new Pipeline())
        {
            if (!Check("MR6 production URP asset and a graphics device", p.Ok, SystemInfo.graphicsDeviceType.ToString())) { Finish("mosaic_timing_report.txt", "MosaicTest.timing", sw, "MOSAIC TEST"); return; }
            var h = MakerCoreTest.Harness.Open();
            try
            {
                using (var a = new Disposer(h.Build(FemaleCard)))
                using (var b = new Disposer(h.Build(FemaleCard)))
                {
                    if (Check("MR6 two BR-Chan build", a.B.Ok && b.B.Ok, (a.B.Error ?? "") + (b.B.Error ?? "")))
                    {
                        HideClothes(a.B.Context); HideClothes(b.B.Context);
                        b.B.Root.transform.position = new Vector3(6f, 0f, 0f);
                        NewLight();
                        var (c, n) = PatchPose(a.B.Context);
                        var cam = NewCam(true, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
                        Aim(cam, c + new Vector3(3f, 0f, 0f), n, 30f);
                        // ZU4: the camera step per character (property blocks of the drawn members)
                        var cpu = new List<double>();
                        for (int i = 0; i < 200; i++) { var t = Stopwatch.StartNew(); MosaicCamera.Apply(cam, false); cpu.Add(t.Elapsed.TotalMilliseconds); }
                        var fr = MosaicCamera.Last;
                        double per = Median(cpu) / Math.Max(1, fr.Groups.Count);
                        Target($"MR6 ZU4 (a) camera step per character ({fr.Groups.Count} characters, {fr.DrawnMembers} drawn members)", per, 0.05);
                        // ZU3: the wide pass at 2560 x 1440 with two characters (wall clock of synchronized renders, on - off)
                        var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                        cam.targetTexture = rt;
                        cam.aspect = 2560f / 1440f;
                        double On() { MosaicWide.Enabled = true; return GpuMs(() => cam.Render(), rt, 30); }
                        double Off() { MosaicWide.Enabled = false; return GpuMs(() => cam.Render(), rt, 30); }
                        Off(); On();
                        double off1 = Off(), on1 = On(), off2 = Off(), on2 = On();
                        double delta = Math.Max(0, (on1 + on2 - off1 - off2) / 2);
                        Info($"MR6 frame at 2560 x 1440: off {F(off1)} / {F(off2)}, on {F(on1)} / {F(on2)} ms; groups {MosaicWideFeature.LastGroups.Count}, B {string.Join(", ", MosaicWideFeature.LastGroups.Select(g => g.Block))}");
                        Target("MR6 ZU3 (b) wide pass at 2560 x 1440, two characters", delta, 0.5);
                        cam.targetTexture = null;
                        rt.Release(); Object.DestroyImmediate(rt);
                        ReleaseFence();
                    }
                }
            }
            finally { Cleanup(); h.Dispose(); }
        }
        Finish("mosaic_timing_report.txt", "MosaicTest.timing: MR6 (ZU3, ZU4)", sw, "MOSAIC TEST");
    }
}
