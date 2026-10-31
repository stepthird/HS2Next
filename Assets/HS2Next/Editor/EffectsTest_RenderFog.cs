// EffectsTest.render group FG (height fog; HS2FogFeature, HS2FogPass, Hidden/HS2Fog, FogMath):
//   FG1 the feature is in the production renderer after URP's SSAO and active, its shader resolves; the scene's fog block
//       reaches the feature's inputs (enabled, the scene's mode / start / end, density, height, colour in linear space) and
//       keeps RenderSettings.fog off; no pass and 0 / 255 change with the block off; a capture with post-processing off
//       enqueues no pass
//   FG2 synthetic stage (two rows of grey quads from 4 m to 900 m, above and below the camera, the background between them):
//       the GPU result = FogMath's opaque factor (both terms) applied to the fog-off capture, per pixel, within 2 / 255 max and
//       1.5 / 255 mean (the 8-bit read-back of the fog-off capture and the neutral grade's own 1 / 255), for
//       Exp2 / Exp / Linear, two value sets from a user's scenes (fog top 70 / height density 0.251; fog top 15 / height density 10 /
//       density 0.001), a camera above the fog top, and excludeFar on / off (far pixels: the fog colour / unchanged)
//   FG3 transparents in front of a fogged wall (fog Exp2): a fitted alpha-blended quad and a fitted additive quad with the
//       material's fog modes = the scene's mode get the forward fog of FogMath (alpha-blended: toward the fog colour; additive:
//       its contribution times the factor) within 1 / 255; with modes 0 or another mode's bit they are not fogged in the
//       shader (0 / 255 against modes 0); a fitted opaque quad is fogged once (the pass's opaque factor)
//   FG4 a URP-native opaque quad is fogged once (FG2's quads are URP-native); a URP-native transparent quad is not fogged in
//       its shader (only the wall behind it changes) and StudioFog counts it (fog.urpNativeTransparent)
//   FG5 no shader under Assets/HS2Next/Shaders declares a fog keyword (the program counts against HEAD's: the build report)
//   FG6 cost: 2560 x 1440 capture time with the fog on and off (reported)
// The group drives the pass through HS2FogFeature.Override (the scene keeps writing Inputs) and removes what it creates.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static partial class EffectsTest
{
    static IEnumerator RenderGroup_FG(Transform stage)
    {
        // the earlier groups' objects are hidden while this group runs (they would stand in front of its camera)
        var hidden = new List<GameObject>();
        foreach (Transform ch in stage) if (ch.gameObject.activeSelf) { hidden.Add(ch.gameObject); ch.gameObject.SetActive(false); }
        var root = new GameObject("fog_stage").transform;
        root.SetParent(stage, false);
        HS2FogFeature.Override = null;
        var grade0 = Fx.BaseGradeOverride;
        try
        {
            var e = FogBody(root);
            while (true)
            {
                object cur;
                try { if (!e.MoveNext()) break; cur = e.Current; }
                catch (Exception ex) { Check("FG group ran without an exception", false, ex.ToString()); break; }
                yield return cur;
            }
        }
        finally
        {
            HS2FogFeature.Override = null;
            Fx.BaseGradeOverride = grade0;
            Object.Destroy(root.gameObject);
            foreach (var g in hidden) if (g) g.SetActive(true);
        }
    }

    static IEnumerator FogBody(Transform root)
    {
        EffectsOff();
        // neutral grading (no base grade, no filter, sliders 0): the capture is the linear image sRGB-encoded, so the fog-off
        // capture gives the scene colour the pass blends with
        Fx.BaseGradeOverride = BaseGrade.Unavailable("render test FG: neutral");
        SetFx("effects.colorGrading", new HS2Next.Save.ColorGradingEffect { Saturation = 0, Brightness = 0, Contrast = 0 });
        Fx.ApplyAll();
        PostProcessing(true);
        yield return PlayModeHarness.WaitFrames(2);

        // ---------------- FG1
        var asset = GraphicsSettings_UrpAsset();
        var rd = asset ? asset.rendererDataList[0] as UniversalRendererData : null;
        int iFog = -1, iSsao = -1;
        if (rd)
            for (int i = 0; i < rd.rendererFeatures.Count; i++)
            {
                var f = rd.rendererFeatures[i];
                if (f is HS2FogFeature && f.isActive) iFog = i;
                if (f is ScreenSpaceAmbientOcclusion) iSsao = i;
            }
        var fogShader = Shader.Find(HS2FogFeature.ShaderName);
        Check($"FG1 HS2FogFeature in the production renderer after SSAO and active (index {iFog}, SSAO {iSsao}), shader {HS2FogFeature.ShaderName} resolves",
            iFog >= 0 && iFog > iSsao && fogShader);
        HS2FogFeature added = null;
        if (iFog < 0 && rd)
        {
            // until the renderer asset lists the feature: a run-time instance after SSAO (never saved) so FG2 - FG7 still run
            added = ScriptableObject.CreateInstance<HS2FogFeature>();
            added.name = "HS2FogFeature (FG run-time)";
            rd.rendererFeatures.Insert(Mathf.Min(iSsao + 1, rd.rendererFeatures.Count), added);
            rd.SetDirty();
            Info("FG: the renderer asset has no fog feature; a run-time instance is used for FG2 - FG7");
            yield return PlayModeHarness.WaitFrames(2);
        }

        // the scene's block reaches the inputs
        var mode0 = RenderSettings.fogMode;
        SetFx("effects.fog.enabled", true);
        SetFx("effects.fog.height", 15f);
        SetFx("effects.fog.heightDensity", 10f);
        SetFx("effects.fog.density", 0.001f);
        SetFx("effects.fog.color", new HS2Next.Save.Color4(27 / 255f, 29 / 255f, 33 / 255f, 1f));
        rr.Apply(); rr.LateApply();
        var inp = HS2FogFeature.Inputs;
        var lin = new Color(27 / 255f, 29 / 255f, 33 / 255f).linear;
        Check($"FG1 the fog block reaches the inputs: enabled {inp.Enabled}, mode {inp.Mode} (scene {(int)RenderSettings.fogMode}), density {inp.Density}, height {inp.Height}, " +
              $"height density {inp.HeightDensity}, colour {inp.ColorLinear} (linear {lin}); RenderSettings.fog {RenderSettings.fog}",
            inp.Enabled && inp.Mode == (int)RenderSettings.fogMode && inp.Density == 0.001f && inp.Height == 15f && inp.HeightDensity == 10f
            && Mathf.Abs(inp.ColorLinear.r - lin.r) < 1e-6f && Mathf.Abs(inp.ColorLinear.b - lin.b) < 1e-6f && !RenderSettings.fog);
        SetFx("effects.fog.enabled", false);
        rr.Apply(); rr.LateApply();
        Check("FG1 block off: inputs off", !HS2FogFeature.Inputs.Enabled);

        // the stage: two rows of quads (above / below the camera) at growing depths, the background between them
        var cam = View.Camera;
        SetView(new Vector3(0, 1.6f, 0), new Vector3(0, 180, 0), 3f, 40f);
        yield return PlayModeHarness.WaitFrames(2);
        var ct = cam.transform;
        Vector3 C = ct.position, fwd = ct.forward, right = ct.right, up = ct.up;
        float tanY = Mathf.Tan(0.5f * 40f * Mathf.Deg2Rad), aspect = RW / (float)RH, tanX = tanY * aspect;
        float[] depths = { 4f, 12f, 40f, 120f, 350f, 900f };
        var quads = new List<(Vector3 centre, float depth, float half)>();
        for (int k = 0; k < depths.Length; k++)
            for (int row = 0; row < 2; row++)
            {
                float d = depths[k];
                // slot k of 6 across the frame, rows at +0.45 / -0.45 of the half height; size 70 % of the slot
                float sx = (-1f + (2f * k + 1f) / depths.Length) * tanX * d;
                float sy = (row == 0 ? 0.45f : -0.45f) * tanY * d;
                float half = 0.7f * tanX * d / depths.Length;
                var c = C + fwd * d + right * sx + up * sy;
                float g = 0.25f + 0.1f * k + 0.05f * row;
                Prim(PrimitiveType.Quad, $"fog_q{k}_{row}", root, root.InverseTransformPoint(c), Quaternion.LookRotation(fwd, up).eulerAngles,
                    new Vector3(2f * half, Mathf.Min(2f * half, 0.5f * tanY * d), 1f), Unlit(new Color(g, g, g)));
                quads.Add((c, d, half));
            }
        yield return PlayModeHarness.WaitFrames(2);

        // sample pixels: 3 x 3 per quad well inside it, plus background pixels on the middle line between the rows
        var samples = new List<(int x, int y, bool far, Vector3 p, float depth)>();
        foreach (var (c, d, half) in quads)
        {
            var vp = CaptureViewport(c, C, fwd, right, up, tanX, tanY);
            int cx = (int)(vp.x * RW), cy = (int)(vp.y * RH);
            float pxPerM = RH / (2f * tanY * d);
            int r = Mathf.Max(0, Mathf.Min(4, (int)(half * pxPerM * 0.3f)));
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = cx + dx * r, y = cy + dy * r;
                    if (x < 3 || y < 3 || x >= RW - 3 || y >= RH - 3) continue;
                    // the pixel centre's ray onto the quad plane (perpendicular to the view direction at depth d)
                    float u = (x + 0.5f) / RW * 2f - 1f, v = (y + 0.5f) / RH * 2f - 1f;
                    var dir = fwd + right * (u * tanX) + up * (v * tanY);
                    samples.Add((x, y, false, C + dir * d, d));
                }
        }
        for (int x = 10; x < RW - 10; x += 40) samples.Add((x, RH / 2, true, Vector3.zero, 0f));
        Info($"FG2 stage: {quads.Count} quads, {samples.Count} sample pixels ({samples.Count(s => s.far)} background)");

        HS2FogFeature.Override = new HS2FogFeature.Input { Enabled = false };
        int e0 = HS2FogFeature.EnqueuedCount;
        var off = Capture(null);
        HS2FogFeature.Override = null;
        var off2 = Capture(null);
        double offMax = 0; for (int i = 0; i < off.Px.Length; i++) offMax = Math.Max(offMax, D(off.Px[i], off2.Px[i]));
        Check($"FG1 no pass with the block off (enqueued {HS2FogFeature.EnqueuedCount - e0}) and 0 / 255 change (max {offMax})", HS2FogFeature.EnqueuedCount == e0 && offMax == 0);

        float near = cam.nearClipPlane, far = cam.farClipPlane;
        var sets = new (string name, HS2FogFeature.Input i)[]
        {
            ("Exp2 owner set 2 (top 15, hd 10, d 0.001)", FogInput(3, 0.001f, 0, 300, 15f, 10f, 27, 29, 33, false)),
            ("Exp2 owner set 1 (top 70, hd 0.251, d 0.0005)", FogInput(3, 0.0005f, 0, 300, 70f, 0.251f, 124, 118, 111, false)),
            ("Exp2 owner set 1, excludeFar", FogInput(3, 0.0005f, 0, 300, 70f, 0.251f, 124, 118, 111, true)),
            ("Exp (d 0.002), camera above the top (0.5, hd 0.3)", FogInput(2, 0.002f, 0, 300, 0.5f, 0.3f, 138, 168, 203, false)),
            ("Linear 0-300, top 50, hd 0.01", FogInput(1, 0.0005f, 0, 300, 50f, 0.01f, 138, 168, 203, false)),
            ("Exp2 d 0.005, top 1.0 (between the rows), hd 1.85", FogInput(3, 0.005f, 0, 300, 1.0f, 1.85f, 23, 28, 38, false)),
        };
        foreach (var (name, fi) in sets)
        {
            HS2FogFeature.Override = fi;
            int en = HS2FogFeature.EnqueuedCount;
            var on = Capture(null);
            bool enq = HS2FogFeature.EnqueuedCount > en;
            var fogCol = fi.ColorLinear;
            double worst = 0, sum = 0; int n = 0, worstAt = -1; double changed = 0;
            for (int s = 0; s < samples.Count; s++)
            {
                var (x, y, isFar, p, depth) = samples[s];
                float f = isFar
                    ? FogMath.OpaqueFactor(fi.Mode, fi.Density, fi.Start, fi.End, near, far, 1f, C.x, C.y, C.z, C.x + fwd.x * far, C.y + fwd.y * far, C.z + fwd.z * far, fi.Height, fi.HeightDensity, fi.ExcludeFarPixels)
                    : FogMath.OpaqueFactor(fi.Mode, fi.Density, fi.Start, fi.End, near, far, depth / far, C.x, C.y, C.z, p.x, p.y, p.z, fi.Height, fi.HeightDensity, fi.ExcludeFarPixels);
                var b = off.At(x, y);
                var got = on.At(x, y);
                var exp = new Color32(
                    Code(FogMath.Apply(fogCol.r, S2L(b.r / 255f), f)),
                    Code(FogMath.Apply(fogCol.g, S2L(b.g / 255f), f)),
                    Code(FogMath.Apply(fogCol.b, S2L(b.b / 255f), f)), 255);
                double d = D(exp, got);
                if (d > worst) { worst = d; worstAt = s; }
                sum += d; n++;
                changed += D(b, got);
            }
            string w = worstAt >= 0 ? $" (worst at ({samples[worstAt].x}, {samples[worstAt].y}){(samples[worstAt].far ? " background" : $" depth {samples[worstAt].depth}")})" : "";
            // bound: the fog-off capture is read back at 8 bits and the neutral grade is the identity within 1 / 255 (R1), so a
            // per-pixel expectation built from it carries up to 1 code of its own; the pass itself is exact to float precision
            Check($"FG2 {name}: GPU = CPU over {n} pixels: max {worst} / 255, mean {F(sum / Math.Max(1, n))}{w}; mean change vs off {F(changed / Math.Max(1, n))}; pass enqueued {enq}",
                enq && worst <= 2 && sum / Math.Max(1, n) <= 1.5 && n > 50);
        }
        HS2FogFeature.Override = null;

        // a capture with post-processing off draws no fog pass
        HS2FogFeature.Override = FogInput(3, 0.001f, 0, 300, 15f, 10f, 27, 29, 33, false);
        PostProcessing(false);
        int ep = HS2FogFeature.EnqueuedCount;
        Capture(null);
        Check($"FG1 post-processing off: no fog pass (enqueued {HS2FogFeature.EnqueuedCount - ep})", HS2FogFeature.EnqueuedCount == ep);
        PostProcessing(true);
        HS2FogFeature.Override = null;
        RenderSettings.fogMode = mode0;
        foreach (Transform q in root) q.gameObject.SetActive(false);
        yield return FogTransparents(root);
        FogModesOfOriginals();
        if (added && rd) { rd.rendererFeatures.Remove(added); rd.SetDirty(); Object.Destroy(added); }
    }

    // FG7: the per-material fog modes come from the original's keyword space (a vanilla character copy: exponential only; a
    // vanilla Studio item copy: none) - the bundles are read directly (skipped when the HS2 install or the bundle is unavailable)
    static void FogModesOfOriginals()
    {
        string hs2 = Services()?.Hs2Root;
        if (string.IsNullOrEmpty(hs2)) { Info("FG7: no HS2 install: skipped"); return; }
        var cases = new[] { ("chara/00/fo_gloves_00.unity3d", "AIT/Clothes Alpha True", 2), ("studio/12.unity3d", "Legacy Shaders/Particles/Additive", 0) };
        int ok = 0, run = 0;
        foreach (var (bundle, name, expect) in cases)
        {
            AssetBundle ab = null;
            try
            {
                ab = AssetBundle.LoadFromFile(System.IO.Path.Combine(hs2, "abdata", bundle));
                if (!ab) { Info($"FG7 {bundle}: not loaded (already open?) - skipped"); continue; }
                Shader found = null;
                foreach (var o in ab.LoadAllAssets())
                {
                    if (o is Shader s && s.name == name) found = s;
                    else if (o is Material m && m.shader && m.shader.name == name) found = m.shader;
                    else if (o is GameObject g)
                        foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                            foreach (var m2 in r.sharedMaterials)
                                if (m2 && m2.shader && m2.shader.name == name) found = m2.shader;
                    if (found) break;
                }
                run++;
                int modes = HS2FogFeature.ModesOf(found);
                Info($"FG7 {bundle}: {name} {(found ? "found" : "NOT found")}, keyword space {(found ? found.keywordSpace.keywordCount : 0)} keywords, fog modes {modes} (expected {expect})");
                if (found && modes == expect) ok++;
            }
            catch (Exception e) { Info($"FG7 {bundle}: {e.GetType().Name}: {e.Message}"); }
            finally { if (ab) ab.Unload(true); }
        }
        if (run > 0) Check($"FG7 fog modes from the originals' keyword space: {ok} of {run} as expected (character copy: exponential; item copy: none)", ok == run);
    }

    // ---------------- FG3 - FG6
    static IEnumerator FogTransparents(Transform root)
    {
        var stage3 = new GameObject("fog_stage3").transform;
        stage3.SetParent(root, false);
        var cam = View.Camera;
        var ct = cam.transform;
        Vector3 C = ct.position, fwd = ct.forward, right = ct.right, up = ct.up;
        float tanY = Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad), tanX = tanY * RW / RH;
        const float wallD = 60f, qD = 30f;
        var wallMat = Unlit(new Color(0.5f, 0.5f, 0.5f));
        Prim(PrimitiveType.Quad, "fog_wall", stage3, stage3.InverseTransformPoint(C + fwd * wallD), Quaternion.LookRotation(fwd, up).eulerAngles,
            new Vector3(4f * tanX * wallD, 4f * tanY * wallD, 1f), wallMat);
        GameObject Front(string name, float sx, float sy, Material m)
        {
            var c = C + fwd * qD + right * (sx * tanX * qD) + up * (sy * tanY * qD);
            float s = 0.3f * tanY * qD;
            return Prim(PrimitiveType.Quad, name, stage3, stage3.InverseTransformPoint(c), Quaternion.LookRotation(fwd, up).eulerAngles, new Vector3(s, s, 1f), m);
        }
        // fitted alpha-blended (texture 0.8 / 0.3 / 0.2, alpha 128), fitted additive (white vertex colours), fitted opaque,
        // URP-native transparent
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[16];
        for (int k = 0; k < 16; k++) px[k] = new Color32(204, 77, 51, 128);
        tex.SetPixels32(px); tex.Apply(false);
        var shT = Shader.Find("HS2Next/Unlit/Transparent");
        var shA = Shader.Find("HS2Next/Legacy Shaders/Particles/Additive");
        var shO = Shader.Find("HS2Next/Unlit/Color");
        if (!shT || !shA || !shO || shT.FindPropertyIndex(HS2FogFeature.ModesProperty) < 0)
        {
            Info($"FG3: the fitted shaders have no fog modes property yet (HS2Next/Unlit/Transparent {(shT ? "found" : "missing")}): FG3 / FG4 skipped");
            Object.Destroy(stage3.gameObject);
            Object.Destroy(tex);
            yield break;
        }
        var mT = new Material(shT); mT.SetTexture("_MainTex", tex); mT.renderQueue = 3000;
        var mA = new Material(shA); mA.SetTexture("_MainTex", Texture2D.whiteTexture); mA.renderQueue = 3000;
        var mO = new Material(shO); mO.SetColor("_Color", new Color(0.7f, 0.6f, 0.4f));
        var mU = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mU.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.9f, 0.5f));
        mU.SetFloat("_Surface", 1f); mU.SetFloat("_Blend", 0f);
        mU.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mU.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mU.SetFloat("_ZWrite", 0f);
        mU.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mU.renderQueue = 3000;
        var qT = Front("fog_fitted_alpha", -0.5f, 0.3f, mT);
        var qA = Front("fog_fitted_additive", 0f, 0.3f, mA);
        var qO = Front("fog_fitted_opaque", 0.5f, 0.3f, mO);
        var qU = Front("fog_urp_transparent", -0.5f, -0.4f, mU);
        var mesh = Object.Instantiate(qA.GetComponent<MeshFilter>().sharedMesh);
        mesh.colors = Enumerable.Repeat(Color.white, mesh.vertexCount).ToArray();
        qA.GetComponent<MeshFilter>().sharedMesh = mesh;
        var fronts = new[] { qT, qA, qO, qU };
        yield return PlayModeHarness.WaitFrames(2);
        List<(int x, int y)> Pix(GameObject q)
        {
            var vp = CaptureViewport(q.transform.position, C, fwd, right, up, tanX, tanY);
            int cx = (int)(vp.x * RW), cy = (int)(vp.y * RH);
            var l = new List<(int, int)>();
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) l.Add((cx + dx * 3, cy + dy * 3));
            return l;
        }
        var fi = FogInput(3, 0.02f, 0, 300, -100f, 0.01f, 124, 118, 111, false);
        var Fl = fi.ColorLinear;
        float near = cam.nearClipPlane, far = cam.farClipPlane;
        float ff = FogMath.ForwardFactor(3, fi.Density, fi.Start, fi.End, qD, near, far);
        Shot Cap(bool fog, float modes, bool frontsOn)
        {
            foreach (var g in fronts) g.SetActive(frontsOn);
            mT.SetFloat(HS2FogFeature.ModesProperty, modes);
            mA.SetFloat(HS2FogFeature.ModesProperty, modes);
            HS2FogFeature.Override = fog ? fi : new HS2FogFeature.Input { Enabled = false };
            return Capture(null);
        }
        var bgOn = Cap(true, 0, false);
        var bgOff = Cap(false, 0, false);
        var m0 = Cap(true, 0, true);
        var m4 = Cap(true, 4, true);
        var m2 = Cap(true, 2, true);
        var off = Cap(false, 4, true);
        HS2FogFeature.Override = null;
        Info($"FG3 forward factor at {qD} m: {F(ff)}");
        float Lin(byte b) => S2L(b / 255f);
        byte Ch(Color32 c, int ch) => ch == 0 ? c.r : ch == 1 ? c.g : c.b;
        float Fc(int ch) => ch == 0 ? Fl.r : ch == 1 ? Fl.g : Fl.b;
        double Worst(List<(int x, int y)> ps, Func<int, int, int, float> expected, Shot got)
        {
            double w = 0;
            foreach (var (x, y) in ps)
                for (int ch = 0; ch < 3; ch++)
                    w = Math.Max(w, Math.Abs(Code(expected(x, y, ch)) - Ch(got.At(x, y), ch)));
            return w;
        }
        float a = 128 / 255f;
        // alpha-blended: lin(m4) = lin(m0) + (1 - ff) (a F - a cq), a cq = lin(m0) - (1 - a) lin(bg)
        var pT = Pix(qT);
        double wT = Worst(pT, (x, y, ch) =>
        {
            float l0 = Lin(Ch(m0.At(x, y), ch)), lb = Lin(Ch(bgOn.At(x, y), ch));
            return l0 + (1 - ff) * (a * Fc(ch) - (l0 - (1 - a) * lb));
        }, m4);
        double wTcq = Worst(pT, (x, y, ch) => a * S2L((ch == 0 ? 204 : ch == 1 ? 77 : 51) / 255f) + (1 - a) * Lin(Ch(bgOn.At(x, y), ch)), m0);
        Check($"FG3 fitted alpha-blended quad, modes = the scene's mode: forward fog = FogMath within 1 / 255 (max {wT}); modes 0: the unfogged quad over the fogged wall (max {wTcq})",
            wT <= 1 && wTcq <= 1);
        // additive: its contribution times ff
        var pA = Pix(qA);
        double wA = Worst(pA, (x, y, ch) =>
        {
            float lb = Lin(Ch(bgOn.At(x, y), ch));
            return lb + (Lin(Ch(m0.At(x, y), ch)) - lb) * ff;
        }, m4);
        double addRed = pA.Average(p => (double)(Lin(m0.At(p.x, p.y).r) - Lin(bgOn.At(p.x, p.y).r)));
        Check($"FG3 fitted additive quad: its contribution times the forward factor within 1 / 255 (max {wA}; contribution red {F(addRed)} linear)", wA <= 1 && addRed > 0.02);
        double d2 = 0;
        foreach (var (x, y) in pT.Concat(pA)) d2 = Math.Max(d2, D(m2.At(x, y), m0.At(x, y)));
        Check($"FG3 modes without the scene's mode (the exponential bit under Exp2): no forward fog (max {d2} / 255 against modes 0)", d2 == 0);
        // fitted opaque: fogged once, by the pass
        var pO = Pix(qO);
        double wO = Worst(pO, (x, y, ch) =>
        {
            float u = (x + 0.5f) / RW * 2f - 1f, v = (y + 0.5f) / RH * 2f - 1f;
            var p = C + (fwd + right * (u * tanX) + up * (v * tanY)) * qD;
            float f = FogMath.OpaqueFactor(3, fi.Density, 0, 300, near, far, qD / far, C.x, C.y, C.z, p.x, p.y, p.z, fi.Height, fi.HeightDensity, false);
            return FogMath.Apply(Fc(ch), Lin(Ch(off.At(x, y), ch)), f);
        }, m4);
        Check($"FG3 fitted opaque quad: fogged once, by the pass (max {wO} / 255 against the opaque factor)", wO <= 1);
        // FG4 URP-native transparent: lin(on) = lin(off) + (1 - a) (lin(bgOn) - lin(bgOff)), a = 0.5
        var pU = Pix(qU);
        double wU = Worst(pU, (x, y, ch) => Lin(Ch(off.At(x, y), ch)) + 0.5f * (Lin(Ch(bgOn.At(x, y), ch)) - Lin(Ch(bgOff.At(x, y), ch))), m4);
        int urp = StudioFog.UrpNativeTransparentCount(stage3.gameObject);
        Check($"FG4 URP-native transparent quad: not fogged in its shader, only the wall behind it (max {wU} / 255); counted for fog.urpNativeTransparent: {urp}",
            wU <= 1 && urp == 1);

        // FG5 no fog keyword in HS2Next's shaders
        var withFog = new List<string>();
        int nSh = 0;
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Shader", new[] { "Assets/HS2Next/Shaders" }))
        {
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (!s) continue;
            nSh++;
            foreach (var k in new[] { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" })
                if (s.keywordSpace.FindKeyword(k).isValid) { withFog.Add(s.name); break; }
        }
        Check($"FG5 no fog keyword in the {nSh} shaders under Assets/HS2Next/Shaders ({(withFog.Count == 0 ? "none" : string.Join(", ", withFog))})", withFog.Count == 0 && nSh > 100);

        // FG6 cost
        foreach (var g in fronts) g.SetActive(true);
        var tOn = new List<double>(); var tOff = new List<double>();
        HS2FogFeature.Override = fi;
        for (int k = 0; k < 3; k++) tOn.Add(Capture(null, 2560, 1440).Ms);
        HS2FogFeature.Override = new HS2FogFeature.Input { Enabled = false };
        for (int k = 0; k < 3; k++) tOff.Add(Capture(null, 2560, 1440).Ms);
        HS2FogFeature.Override = null;
        Info($"FG6 (rough): 2560 x 1440 capture (2 renders + readback) with the fog {F(tOn.Min())} ms, without {F(tOff.Min())} ms -> about {F((tOn.Min() - tOff.Min()) / 2)} ms per frame");
        Check("FG6 cost measured", tOn.Count == 3 && tOff.Count == 3);
        Object.Destroy(stage3.gameObject);
        Object.Destroy(tex); Object.Destroy(mesh);
        foreach (var m in new[] { mT, mA, mO, mU, wallMat }) Object.Destroy(m);
    }

    /// <summary>The viewport point of <paramref name="q"/> in a capture of RW x RH (the capture's aspect, not the view's).</summary>
    static Vector2 CaptureViewport(Vector3 q, Vector3 C, Vector3 fwd, Vector3 right, Vector3 up, float tanX, float tanY)
    {
        var d = q - C;
        float z = Vector3.Dot(d, fwd);
        return new Vector2(0.5f + 0.5f * Vector3.Dot(d, right) / (z * tanX), 0.5f + 0.5f * Vector3.Dot(d, up) / (z * tanY));
    }

    static UniversalRenderPipelineAsset GraphicsSettings_UrpAsset() =>
        UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

    static HS2FogFeature.Input FogInput(int mode, float density, float start, float end, float height, float hd, int r, int g, int b, bool exclFar) =>
        new HS2FogFeature.Input
        {
            Enabled = true, Mode = mode, Density = density, Start = start, End = end, Height = height, HeightDensity = hd,
            ExcludeFarPixels = exclFar, ColorLinear = new Color(r / 255f, g / 255f, b / 255f).linear,
        };
}
