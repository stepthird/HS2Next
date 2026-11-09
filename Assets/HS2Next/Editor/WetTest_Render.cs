// WetTest.render, drawing and mirrors (package R: WD1-WD7, WD9, WO1-WO10). Images stay in memory and are judged by numbers; nothing is written as a picture. Targets: the
// clothed SFW cards, a character through a test host, a code-made stain: one map per target from a plain distance, no rig. The mirror's image is read from its target (WetMirrorRig).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using HS2Next.Tests.MakerCore;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static partial class WetTest
{
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    const float FilmUnits = -256f;

    /// <summary>A character in a test host, a plain camera on a render target, a light and an ambient level.</summary>
    sealed class Stage : IDisposable
    {
        public readonly Host H;
        public readonly CharaRealization Real;
        public readonly CharaContext Ctx;
        public readonly Camera Cam;
        public readonly UniversalAdditionalCameraData Urp;
        public readonly RenderTexture Rt;
        public readonly Transform Wet;
        public readonly Light Light;
        public readonly List<Painted> Paint = new List<Painted>();
        readonly GameObject camGo, lightGo, wetGo;
        readonly AmbientMode ambientMode;
        readonly Color ambient;

        public Stage(bool male = false, int w = 1280, int h = 960)
        {
            ambientMode = RenderSettings.ambientMode; ambient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.4f, 1f);
            H = NewHost();
            string key = LoadChara(H, male);
            Real = H.Rt.Realizations.OfType<CharaRealization>().First(x => x.NodeId == key);
            Ctx = Real.Context;
            camGo = new GameObject("wet_test_cam");
            Cam = camGo.AddComponent<Camera>();
            Cam.fieldOfView = 23f; Cam.nearClipPlane = 0.3f; Cam.farClipPlane = 2000f;
            Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            Cam.allowHDR = true; Cam.allowMSAA = false; Cam.enabled = false;
            Urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            Urp.renderPostProcessing = false; Urp.antialiasing = AntialiasingMode.None; Urp.renderShadows = false;
            Rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default) { name = "wet_test_target" };
            Rt.Create();
            Cam.targetTexture = Rt;
            lightGo = new GameObject("wet_test_light");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1f; l.color = Color.white; l.shadows = LightShadows.None;
            RenderingLayers.ApplyToLight(l);
            lightGo.transform.rotation = Quaternion.Euler(40f, 160f, 0f);
            Light = l;
            wetGo = new GameObject("wet_test_root");
            Wet = wetGo.transform;
        }

        public Vector3 Centre => Ctx.Root.transform.position + Vector3.up * 9f;

        public void Aim(Vector3 target, Vector3 dir, float dist)
        {
            Cam.transform.position = target + dir.normalized * dist;
            Cam.transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);
            lightGo.transform.rotation = Cam.transform.rotation * Quaternion.Euler(10f, 0f, 0f);
        }

        /// <summary>The target after a render; rows from the bottom.</summary>
        public Color32[] Shot()
        {
            Cam.Render();
            return Read(Rt);
        }

        public void Dispose()
        {
            foreach (var p in Paint) p.Dispose();
            Paint.Clear();
            Cam.targetTexture = null;
            Rt.Release(); Object.DestroyImmediate(Rt);
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(wetGo);
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
            H.Dispose();
        }
    }

    static Color32[] Read(RenderTexture rt)
    {
        var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
        req.WaitForCompletion();
        return req.hasError ? new Color32[rt.width * rt.height] : req.GetData<Color32>().ToArray();
    }

    /// <summary>A target with a code-made stain.</summary>
    sealed class Painted : IDisposable
    {
        public WetTarget T;
        public RenderTexture Map;
        public WetCompose C;
        public WetFilm Film;
        public WetLayer Layer;
        public void Dispose()
        {
            Film?.Dispose();
            C?.Dispose();
            WetBake.Free(Map);
        }
        public bool Compose() => C.Compose(new[] { Layer });
    }

    /// <summary>A target's map: distance from <paramref name="origin"/> (null: 0 everywhere) with the posed vertices of a baked copy.</summary>
    static RenderTexture MapOf(WetTarget t, Vector3? origin, float reach)
    {
        var baked = new Mesh { name = "wet_test_bake", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Vector3[] v; Matrix4x4 toWorld;
            if (t.Renderer is SkinnedMeshRenderer s)
            {
                s.BakeMesh(baked);
                toWorld = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
            }
            else
            {
                var src = t.Renderer.GetComponent<MeshFilter>().sharedMesh;
                baked.vertices = src.vertices; baked.uv = src.uv; baked.subMeshCount = src.subMeshCount;
                for (int i = 0; i < src.subMeshCount; i++) baked.SetTriangles(src.GetTriangles(i), i);
                toWorld = t.Renderer.localToWorldMatrix;
            }
            v = baked.vertices;
            var uv = baked.uv;
            if (v.Length == 0 || uv.Length != v.Length) return null;
            var d = new float[v.Length];
            if (origin is Vector3 o) for (int i = 0; i < v.Length; i++) d[i] = (toWorld.MultiplyPoint3x4(v[i]) - o).magnitude;
            int sub = Math.Min(t.Slot, baked.subMeshCount - 1);
            var tex = WetCompose.BaseOf(t.Material);
            var size = WetBake.MapSize(tex ? tex.width : 512, tex ? tex.height : 512);
            return WetBake.Bake(uv, baked.GetTriangles(sub), d, size.x, size.y, reach);
        }
        finally { Object.DestroyImmediate(baked); }
    }

    static WetLayer Solid(RenderTexture map, float reach, Color stain, float amount = 1f) => new WetLayer
    {
        Map = map, Radius = reach, Feather = 0.195f, Reach = reach, Saturate = 1f, Amount = amount, Tint = Color.white, Stain = stain, Skin = 1f,
    };

    /// <summary>Every shown target of an ok state of these kinds, stained everywhere (colour only, or with a film on the clothes).</summary>
    static List<Painted> PaintAll(Stage s, Func<WetTarget, bool> which, Color stain, bool film)
    {
        var l = new List<Painted>();
        foreach (var t in WetTargets.Collect(s.Real))
        {
            if (t.State != WetTargetStates.Ok || !t.Shown || !which(t)) continue;
            var map = MapOf(t, null, 3f);
            if (!map) continue;
            bool f = film && t.Kind == WetKinds.Clothes && WetFilm.FamilyOf(t.Material) != WetFilmFamily.None;
            var p = new Painted { T = t, Map = map, Layer = Solid(map, 3f, stain), C = new WetCompose(t.Renderer, t.Slot, t.CompositeCap, f) };
            p.Compose();
            if (f) p.Film = WetFilm.Create(s.Wet, p.C);
            l.Add(p);
            s.Paint.Add(p);
        }
        return l;
    }

    static void Unpaint(Stage s, List<Painted> l)
    {
        foreach (var p in l) { p.Dispose(); s.Paint.Remove(p); }
        l.Clear();
    }

    static int Diff(Color32[] a, Color32[] b, int over = 6)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > over) n++;
        return n;
    }

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;
    static bool Magenta(Color32 c) => c.r > 240 && c.g < 15 && c.b > 240;


    /// <summary>A flat skinned sheet in front of the camera: two bones, UV0 and UV1 over the sheet, a blend shape that lifts it.</summary>
    sealed class Sheet : IDisposable
    {
        public readonly GameObject Go;
        public readonly SkinnedMeshRenderer R;
        public readonly Material M;
        readonly Mesh mesh;
        readonly Transform b0, b1;
        readonly Texture2D tex;

        public Sheet(string shader, Color colour, Vector3 at)
        {
            const int n = 16;
            mesh = new Mesh { name = "wet_test_sheet" };
            var v = new Vector3[(n + 1) * (n + 1)]; var uv = new Vector2[v.Length]; var nr = new Vector3[v.Length]; var tg = new Vector4[v.Length]; var bw = new BoneWeight[v.Length];
            var delta = new Vector3[v.Length];
            var tri = new List<int>();
            for (int y = 0; y <= n; y++)
                for (int x = 0; x <= n; x++)
                {
                    int i = y * (n + 1) + x;
                    v[i] = new Vector3(x / (float)n * 8f - 4f, y / (float)n * 8f - 4f, 0f);
                    uv[i] = new Vector2(x / (float)n, y / (float)n);
                    nr[i] = Vector3.back; tg[i] = new Vector4(1f, 0f, 0f, 1f);
                    bw[i] = new BoneWeight { boneIndex0 = x < n / 2 ? 0 : 1, weight0 = 1f };
                    delta[i] = Vector3.up * 2f;
                    if (x < n && y < n) { tri.Add(i); tri.Add(i + n + 1); tri.Add(i + 1); tri.Add(i + 1); tri.Add(i + n + 1); tri.Add(i + n + 2); }
                }
            mesh.vertices = v; mesh.uv = uv; mesh.uv2 = uv; mesh.normals = nr; mesh.tangents = tg; mesh.boneWeights = bw; mesh.triangles = tri.ToArray();
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            mesh.AddBlendShapeFrame("lift", 100f, delta, null, null);
            mesh.RecalculateBounds();
            Go = new GameObject("wet_test_sheet") { layer = 10 };
            Go.transform.position = at;
            b0 = new GameObject("b0").transform; b1 = new GameObject("b1").transform;
            b0.SetParent(Go.transform, false); b1.SetParent(Go.transform, false);
            R = Go.AddComponent<SkinnedMeshRenderer>();
            R.sharedMesh = mesh; R.bones = new[] { b0, b1 }; R.rootBone = b0;
            R.localBounds = new Bounds(Vector3.zero, new Vector3(10f, 14f, 4f));
            R.updateWhenOffscreen = true;
            R.renderingLayerMask = uint.MaxValue;
            tex = new Texture2D(64, 64, TextureFormat.RGBA32, true, false) { name = "wet_test_sheet", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[64 * 64];
            for (int i = 0; i < px.Length; i++) px[i] = colour;
            tex.SetPixels32(px); tex.Apply(true);
            M = new Material(Shader.Find(shader)) { name = "wet_test_sheet" };
            M.SetTexture(MainTexId, tex);
            if (M.HasProperty("_Cutoff")) M.SetFloat("_Cutoff", 0.5f);
            if (M.HasProperty("_BaseColor")) M.SetColor("_BaseColor", Color.white);
            if (M.HasProperty("_Color")) M.SetColor("_Color", Color.white);
            if (M.HasProperty("_AlphaEx")) M.SetFloat("_AlphaEx", 1f);
            R.sharedMaterial = M;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(Go); Object.DestroyImmediate(M); Object.DestroyImmediate(mesh); Object.DestroyImmediate(tex);
        }
    }

    /// <summary>A map with a stain over the sheet's UVs: value 0 left of <paramref name="split"/> (or everywhere), nothing arrives right of it.</summary>
    static RenderTexture SheetMap(float split = 2f)
    {
        var bytes = new byte[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) bytes[y * 64 + x] = x / 64f < split ? (byte)0 : WetBake.Unreached;
        var map = WetBake.Decode(WetBake.Encode(bytes, 64, 64), out _);
        return map;
    }

    static WetLayer Identity(Texture map) => new WetLayer { Map = map, Radius = 3f, Feather = 0.195f, Reach = 3f, Saturate = 1f, Amount = 0f, Tint = Color.white, Stain = Color.black, Skin = 1f };

    static void FlatFilm(WetFilm f, bool on) => f.Material.SetFloat("_WetFilmFlat", on ? 1f : 0f);

    static int FlatPixels(Color32[] px, Func<int, bool> where = null)
    {
        int n = 0;
        for (int i = 0; i < px.Length; i++) if (Magenta(px[i]) && (where == null || where(i))) n++;
        return n;
    }

    static (int max, double mean) Spread(Color32[] a, Color32[] b)
    {
        int mx = 0; long sum = 0; long n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
            mx = Math.Max(mx, d); sum += d; n++;
        }
        return (mx, n > 0 ? sum / (double)n : 0);
    }

    static RenderTexture EmptyMap(int w, int h)
    {
        var map = WetBake.NewMap(w, h);
        var prev = RenderTexture.active;
        RenderTexture.active = map;
        GL.Clear(false, true, Color.white);
        RenderTexture.active = prev;
        return map;
    }

    static int WetObjects() =>
        Resources.FindObjectsOfTypeAll<RenderTexture>().Count(t => t.name.StartsWith("HS2Next wet", StringComparison.Ordinal)) +
        Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>().Count(r => r.name.StartsWith("wet film", StringComparison.Ordinal));


    static void FilmGroups()
    {
        Group("WD2", "no material changes", () =>
        {
            using (var s = new Stage())
            {
                s.Aim(s.Centre, s.Ctx.Root.transform.forward, 30f);
                s.Shot();
                s.Shot();
                var before = CharaSnapshot.Take(s.Ctx.Root, s.Ctx);
                var l = PaintAll(s, t => true, new Color(0.9f, 0.05f, 0.05f), true);
                s.Shot();
                var with = CharaSnapshot.Take(s.Ctx.Root, s.Ctx);
                var d = CharaSnapshot.Compare(before, with);
                Check("the character's digest is the same with stains and films on", d.Equal && l.Count > 0, d.Equal ? $"{l.Count} targets, {before.Lines.Count} lines" : d.ToString());
                Unpaint(s, l);
                var after = CharaSnapshot.Take(s.Ctx.Root, s.Ctx);
                Check("and after they are gone", CharaSnapshot.Compare(before, after).Equal, "");
            }
        });

        Group("WD4", "the film and the garment's alpha", () =>
        {
            using (var s = new Stage())
            {
                s.Ctx.Root.SetActive(false);
                var at = new Vector3(0f, 10f, -60f);
                s.Cam.transform.position = at + Vector3.back * 30f;
                s.Cam.transform.rotation = Quaternion.identity;
                int w = s.Rt.width;
                foreach (var shader in new[] { "HS2Next/AIT Clothes Alpha True", "HS2Next/AIT Clothes Alpha", "HS2Next/AIT Clothes True" })
                    using (var g = new Sheet(shader, new Color(0.5f, 0.6f, 0.7f, 1f), at))
                    {
                        var map = SheetMap(0.5f);
                        var c = new WetCompose(g.R, 0, WetCompose.ClothesCap, true);
                        c.Compose(new[] { Identity(map) });
                        var f = WetFilm.Create(s.Wet, c);
                        try
                        {
                            FlatFilm(f, true);
                            var px = s.Shot();
                            int left = FlatPixels(px, i => i % w < w / 2 - 12), right = FlatPixels(px, i => i % w > w / 2 + 12);
                            Check($"{shader}: over the garment where the mask says", left > 1000 && right == 0, $"{left} px on the stained half, {right} on the other");
                            // the garment's own alpha mask hides the film
                            string mask = shader.Contains("True") ? "_alpha_a" : "_alpha_a";
                            var black = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                            var bp = new Color32[16]; for (int i = 0; i < 16; i++) bp[i] = new Color32(0, 0, 0, 255);
                            black.SetPixels32(bp); black.Apply(false);
                            g.M.SetTexture("_AlphaMask", black); g.M.SetFloat(mask, 1f);
                            f.Refresh();
                            FlatFilm(f, true);
                            int hidden = FlatPixels(s.Shot());
                            if (shader.Contains("Alpha")) Info($"{shader}: under a black alpha mask the film draws {hidden} flat px (flat ignores a blended garment's alpha)");
                            else Check($"{shader}: a black alpha mask hides the film", hidden == 0, $"{hidden} px");
                            FlatFilm(f, false);
                            var covered = s.Shot();
                            f.Renderer.gameObject.SetActive(false);
                            var bare = s.Shot();
                            f.Renderer.gameObject.SetActive(true);
                            Check($"{shader}: under a black alpha mask nothing of the film shows", Diff(covered, bare, 0) == 0, $"{Diff(covered, bare, 0)} px differ from the same view with no film");
                            g.M.SetFloat(mask, 0f);
                            f.Refresh();
                            var open = s.Shot();
                            Check($"{shader}: without the mask the film shows", Diff(open, covered, 0) > 1000, $"{Diff(open, covered, 0)} px");
                            Object.DestroyImmediate(black);
                        }
                        finally { f?.Dispose(); c.Dispose(); WetBake.Free(map); }
                    }
                s.Ctx.Root.SetActive(true);
            }
        });

        Group("WD5", "film families", () =>
        {
            using (var s = new Stage())
            {
                s.Ctx.Root.SetActive(false);
                var at = new Vector3(0f, 10f, -60f);
                s.Cam.transform.position = at + Vector3.back * 30f;
                s.Cam.transform.rotation = Quaternion.identity;
                var map = SheetMap();
                foreach (var (shader, family, keyword) in new[]
                {
                    ("HS2Next/AIT Clothes True", WetFilmFamily.True, (string)null), ("HS2Next/AIT Clothes", WetFilmFamily.Clothes, "_WETFILM_ITEM"),
                    ("HS2Next/AIT Clothes Alpha", WetFilmFamily.ClothesAlpha, "_WETFILM_CALPHA"), ("HS2Next/AIT Clothes Alpha True", WetFilmFamily.TrueAlpha, "_WETFILM_TRUEALPHA"),
                    ("HS2Next/AIT Clothes Alpha True BackCull", WetFilmFamily.TrueAlpha, "_WETFILM_TRUEALPHA"),
                })
                    using (var g = new Sheet(shader, new Color(0.5f, 0.6f, 0.7f, 1f), at))
                    {
                        var c = new WetCompose(g.R, 0, WetCompose.ClothesCap, true);
                        c.Compose(new[] { Identity(map) });
                        var plain = s.Shot();
                        var f = WetFilm.Create(s.Wet, c);
                        try
                        {
                            if (!Check($"{shader}: a film of its family", f != null && f.Family == family, f?.Family.ToString() ?? "none")) continue;
                            var en = f.Material.enabledKeywords.Select(k => k.name).Where(k => k.StartsWith("_WETFILM", StringComparison.Ordinal)).ToArray();
                            Check($"{shader}: the keyword", keyword == null ? en.Length == 0 : en.Length == 1 && en[0] == keyword, string.Join(" ", en));
                            Check($"{shader}: the queue is above the garment's, at least 2650", f.Material.renderQueue == Math.Max(g.M.renderQueue + 1, 2650), f.Material.renderQueue.ToString());
                            // every value of the garment reaches the film
                            int same = 0, other = 0;
                            var sh = g.M.shader;
                            string differs = "";
                            for (int i = 0; i < sh.GetPropertyCount(); i++)
                            {
                                string n = sh.GetPropertyName(i);
                                if (n == "_ExGloss") continue;
                                if (!f.Material.HasProperty(n)) { other++; differs += " missing:" + n; continue; }
                                bool eq;
                                switch (sh.GetPropertyType(i))
                                {
                                    case UnityEngine.Rendering.ShaderPropertyType.Texture: eq = f.Material.GetTexture(n) == g.M.GetTexture(n); break;
                                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                                    case UnityEngine.Rendering.ShaderPropertyType.Vector: eq = f.Material.GetVector(n) == g.M.GetVector(n); break;
                                    default: eq = f.Material.GetFloat(n) == g.M.GetFloat(n); break;
                                }
                                if (eq) same++; else { other++; differs += " " + n; }
                            }
                            Check($"{shader}: the film carries the garment's values by name", other == 0 && same > 20, $"{same} equal{differs}");
                            FlatFilm(f, true);
                            int flat = FlatPixels(s.Shot());
                            FlatFilm(f, false);
                            Check($"{shader}: it draws", flat > 1000, $"{flat} px");
                            // the family's surface: with no gloss of its own and opacity 1 the film repeats the garment
                            f.Set(0f, 0f, 1f);
                            var over = s.Shot();
                            var (mx, mean) = Spread(over, plain);
                            f.Set(0.9f, 0.8f, 0.85f);
                            var glossy = s.Shot();
                            if (shader.EndsWith("BackCull", StringComparison.Ordinal)) Info($"{shader}: film over the garment against the garment: max {mx}, mean {mean:F3} (the Fresnel term is left out)");
                            else Check($"{shader}: the film repeats the garment's surface", mx <= 2, $"max {mx}, mean {mean:F3} of 255");
                            Check($"{shader}: the film's own values change the image", Diff(glossy, plain, 2) > 0, $"{Diff(glossy, plain, 2)} px");
                        }
                        finally { f?.Dispose(); c.Dispose(); }
                    }
                using (var g = new Sheet("HS2Next/Standard", new Color(0.5f, 0.6f, 0.7f, 1f), at))
                {
                    var c = new WetCompose(g.R, 0, WetCompose.ClothesCap, true);
                    c.Compose(new[] { Identity(map) });
                    Check("HS2Next/Standard: no film", WetFilm.Create(s.Wet, c) == null && WetFilm.FamilyOf(g.M) == WetFilmFamily.None, "");
                    Check("HS2Next/Standard: the colour still goes through the override", c.Bound, "");
                    c.Dispose();
                }
                WetBake.Free(map);
                s.Ctx.Root.SetActive(true);
            }
        });

        Group("WD6", "blend shapes", () =>
        {
            using (var s = new Stage())
            {
                s.Ctx.Root.SetActive(false);
                var at = new Vector3(0f, 10f, -60f);
                s.Cam.transform.position = at + Vector3.back * 30f;
                s.Cam.transform.rotation = Quaternion.identity;
                using (var g = new Sheet("HS2Next/AIT Clothes True", new Color(0.5f, 0.6f, 0.7f, 1f), at))
                {
                    var map = SheetMap();
                    var c = new WetCompose(g.R, 0, WetCompose.ClothesCap, true);
                    c.Compose(new[] { Identity(map) });
                    var f = WetFilm.Create(s.Wet, c);
                    FlatFilm(f, true);
                    int Top(Color32[] px) { for (int i = px.Length - 1; i >= 0; i--) if (Magenta(px[i])) return i / s.Rt.width; return -1; }
                    s.Shot();
                    int top0 = Top(s.Shot());
                    g.R.SetBlendShapeWeight(0, 100f);
                    bool follows = f.LateApply();
                    // an edit-mode render skins again only a renderer that was enabled since the last one
                    g.R.enabled = false; g.R.enabled = true;
                    f.Renderer.enabled = false; f.Renderer.enabled = true;
                    s.Shot();
                    int top1 = Top(s.Shot());
                    float High(SkinnedMeshRenderer r) { var bake = new Mesh(); r.BakeMesh(bake); float y = bake.vertices.Max(x => x.y) + r.transform.position.y; Object.DestroyImmediate(bake); return y; }
                    Check("the film's weight and baked mesh are the garment's", follows && f.Renderer.GetBlendShapeWeight(0) == 100f && High(f.Renderer) == High(g.R), $"weight {f.Renderer.GetBlendShapeWeight(0)}, top {High(f.Renderer)} against {High(g.R)}");
                    Check("the film's image follows the blend shape", top1 > top0 + 20, $"top row {top0} -> {top1}");
                    f.Dispose(); c.Dispose(); WetBake.Free(map);
                }
                s.Ctx.Root.SetActive(true);
            }
        });

        Group("WD7", "dry and removal", () =>
        {
            int before = WetObjects();
            using (var s = new Stage())
            {
                s.Aim(s.Centre, s.Ctx.Root.transform.forward, 30f);
                s.Shot();
                var dry = s.Shot();
                var l = PaintAll(s, t => true, new Color(0.9f, 0.05f, 0.05f), true);
                foreach (var p in l) p.Film?.LateApply();
                var wet = s.Shot();
                Check("a stain shows", Diff(wet, dry) > 1000, $"{Diff(wet, dry)} px");
                foreach (var p in l) { p.Layer.Radius = 0f; p.Compose(); p.Film?.LateApply(); }
                Check("t 0: no override, no composite", l.All(p => !p.C.Bound && !p.C.Wet && !p.C.Mask), "");
                var zero = s.Shot();
                Check("t 0 gives the bytes of no stain", Diff(zero, dry, 0) == 0, $"{Diff(zero, dry, 0)} px differ");
                var block = new MaterialPropertyBlock();
                int blocks = 0;
                foreach (var p in l) { p.T.Renderer.GetPropertyBlock(block, p.T.Slot); if (!block.isEmpty) blocks++; }
                Check("no block on a dry target", blocks == 0, $"{blocks} of {l.Count}");
                foreach (var p in l) { p.Layer.Radius = 3f; p.Compose(); p.Film?.LateApply(); }
                var again = s.Shot();
                Check("the same stain again: the same bytes", Diff(again, wet, 0) == 0, $"{Diff(again, wet, 0)} px differ");
                Unpaint(s, l);
                Check("after removal no render texture and no film are left", WetObjects() == before, $"{WetObjects()} against {before}");
                Check("after removal the bytes of no stain", Diff(s.Shot(), dry, 0) == 0, "");
            }
        });

        Group("WD9", "outside every stain the composite is the base", () =>
        {
            using (var s = new Stage(false, 1024, 768))
            {
                var fwd = s.Ctx.Root.transform.forward;
                void Case(string label, Renderer r, int slot, int cap, Action near, Action far)
                {
                    var c = new WetCompose(r, slot, cap, false);
                    var tex = WetCompose.BaseOf(c.MaterialNow());
                    var map = EmptyMap(64, 64);
                    try
                    {
                        near(); s.Shot();
                        var n0 = s.Shot();
                        far();
                        var f0 = s.Shot();
                        c.Compose(new[] { Identity(map) });
                        far();
                        var f1 = s.Shot();
                        near();
                        var n1 = s.Shot();
                        var (mx, mean) = Spread(n0, n1);
                        var (fmx, fmean) = Spread(f0, f1);
                        string kind = tex is RenderTexture rt ? $"render texture sRGB {rt.sRGB} mips {rt.useMipMap}" : $"{tex.graphicsFormat} isDataSRGB {tex.isDataSRGB} mips {tex.mipmapCount}";
                        Check($"{label}: near view at most 1 / 255", c.Bound && mx <= 1, $"max {mx}, mean {mean:F4}; {kind}, composite {c.Wet?.graphicsFormat}");
                        Check($"{label}: far view mean at most 0.5 / 255", fmean <= 0.5, $"max {fmx}, mean {fmean:F4}");
                    }
                    finally { c.Dispose(); WetBake.Free(map); }
                }
                var all = WetTargets.Collect(s.Real);
                var clothes = all.First(t => t.Kind == WetKinds.Clothes && t.State == WetTargetStates.Ok && t.Shown && WetCompose.BaseOf(t.Material) is RenderTexture);
                Case("a create_ target", clothes.Renderer, clothes.Slot, WetCompose.ClothesCap, () => s.Aim(s.Centre, fwd, 8f), () => s.Aim(s.Centre, fwd, 110f));
                var hair = all.FirstOrDefault(t => t.Kind == WetKinds.Hair && t.State == WetTargetStates.Ok && t.Shown && WetCompose.BaseOf(t.Material) is Texture2D);
                if (Check("a hair target with a bundle texture", hair != null, ""))
                    Case("a bundle texture", hair.Renderer, hair.Slot, WetCompose.ClothesCap, () => s.Aim(s.Centre + Vector3.up * 6f, fwd, 8f), () => s.Aim(s.Centre, fwd, 110f));
                s.Ctx.Root.SetActive(false);
                var at = new Vector3(0f, 10f, -60f);
                Action N = () => { s.Cam.transform.position = at + Vector3.back * 10f; s.Cam.transform.rotation = Quaternion.identity; };
                Action F = () => { s.Cam.transform.position = at + Vector3.back * 200f; s.Cam.transform.rotation = Quaternion.identity; };
                using (var g = new Sheet("HS2Next/AIT Clothes True", Color.white, at))
                {
                    foreach (var (label, linear, compress) in new[] { ("a block-compressed texture", false, true), ("a texture with isDataSRGB false", true, false) })
                    {
                        var t = new Texture2D(256, 256, TextureFormat.RGBA32, true, linear) { name = "wet_test_base", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
                        var px = new Color32[256 * 256];
                        var rnd = new System.Random(7);
                        for (int y = 0; y < 256; y++)
                            for (int x = 0; x < 256; x++)
                                px[y * 256 + x] = new Color32((byte)(x ^ (y / 2)), (byte)(y + rnd.Next(16)), (byte)(255 - x), 255);
                        t.SetPixels32(px); t.Apply(true);
                        if (compress) t.Compress(false);
                        g.M.SetTexture(MainTexId, t);
                        Case(label, g.R, 0, WetCompose.ClothesCap, N, F);
                        Object.DestroyImmediate(t);
                    }
                }
                s.Ctx.Root.SetActive(true);
            }
        });
    }


    /// <summary>Fog, depth of field and ambient occlusion on, for the length of the scope.</summary>
    sealed class Effects : IDisposable
    {
        readonly HS2FogFeature.Input fog; readonly HS2DofFeature.Input dof; readonly HS2AoFeature.Input ao;
        public Effects(Vector3 focus)
        {
            fog = HS2FogFeature.Inputs; dof = HS2DofFeature.Inputs; ao = HS2AoFeature.Inputs;
            HS2FogFeature.Inputs = new HS2FogFeature.Input { Enabled = true, Mode = 2, Density = 0.01f, Start = 0f, End = 300f, Height = 5f, HeightDensity = 0.05f, ColorLinear = new Color(0.4f, 0.45f, 0.5f, 1f) };
            HS2DofFeature.Inputs = new HS2DofFeature.Input { Enabled = true, FocusPoint = focus, FocalSize = 0.05f, Aperture = 0.6f };
            HS2AoFeature.Inputs = new HS2AoFeature.Input { Active = true, Radius = 0.05f, Intensity = 1f, Thickness = 1f, ScaleWeights = Vector4.one };
        }
        public void Dispose() { HS2FogFeature.Inputs = fog; HS2DofFeature.Inputs = dof; HS2AoFeature.Inputs = ao; }
    }

    /// <summary>The view above the floor, the hidden camera in front of the body and below the floor, looking at it.</summary>
    static void MirrorView(Stage s, WetMirrorRig rig, float dist = 30f)
    {
        var c = s.Centre;
        var fwd = s.Ctx.Root.transform.forward;
        rig.Place(s.Cam, c, c + (fwd * 0.75f - Vector3.up * 0.66f).normalized * dist);
    }

    static void MirrorGroups()
    {
        Group("WO2", "nested equals top level, with fog, DOF and AO on in the outer camera", () =>
        {
            using (var s = new Stage(false, 960, 720))
            using (var rig = new WetMirrorRig(-0.1f, 0.5f))
            using (new Effects(s.Centre))
            {
                bool wide = MosaicWide.Enabled;
                MosaicWide.Enabled = false;
                s.Urp.renderPostProcessing = true; s.Urp.renderShadows = true;
                s.Light.shadows = LightShadows.Soft;
                MirrorView(s, rig);
                int fog0 = HS2FogFeature.EnqueuedCount, dof0 = HS2DofFeature.EnqueuedCount, ao0 = HS2AoFeature.EnqueuedCount;
                s.Shot();
                s.Shot();
                var nested = rig.Bytes();
                Info($"outer camera's passes in two renders: fog {HS2FogFeature.EnqueuedCount - fog0}, DOF {HS2DofFeature.EnqueuedCount - dof0}, AO {HS2AoFeature.EnqueuedCount - ao0}");
                var go = new GameObject("wet_test_top");
                var top = go.AddComponent<Camera>();
                top.enabled = false;
                var d = go.AddComponent<UniversalAdditionalCameraData>();
                top.CopyFrom(s.Cam);
                d.renderPostProcessing = false; d.antialiasing = AntialiasingMode.None; d.requiresColorOption = CameraOverrideOption.Off; d.requiresDepthOption = CameraOverrideOption.Off; d.renderShadows = true;
                var rt = new RenderTexture(rig.W, rig.H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                top.targetTexture = rt; top.rect = new Rect(0f, 0f, 1f, 1f); top.allowMSAA = false;
                top.cullingMask = rig.Mask;
                top.worldToCameraMatrix = rig.View; top.projectionMatrix = rig.Projection;
                top.transform.SetPositionAndRotation(rig.CameraPosition, rig.CameraRotation);
                rig.Puddle.forceRenderingOff = true;
                bool inv = GL.invertCulling;
                GL.invertCulling = !inv;
                try { top.Render(); top.Render(); }
                finally { GL.invertCulling = inv; rig.Puddle.forceRenderingOff = false; }
                var plain = Read(rt);
                int differ = Diff(nested, plain, 0);
                int lit = nested.Count(c => c.r + c.g + c.b > 12);
                Check("the mirror's image equals the same camera at top level", differ == 0 && lit > 100, $"{differ} of {nested.Length} px differ, {lit} not black");
                top.targetTexture = null;
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
                MosaicWide.Enabled = wide;
                s.Light.shadows = LightShadows.None;
            }
        });

        Group("WO3", "nothing leaks into the outer camera", () =>
        {
            using (var s = new Stage(false, 960, 720))
            using (var rig = new WetMirrorRig(-0.1f, 0.5f))
            using (new Effects(s.Centre))
            {
                s.Urp.renderPostProcessing = true; s.Urp.renderShadows = true;
                s.Light.shadows = LightShadows.Soft;
                rig.Look(new Color(0f, 0f, 0f, 0f), 0f);
                MirrorView(s, rig);
                s.Shot();
                int r0 = PlanarMirrors.Renders;
                var a = s.Shot();
                var a2 = s.Shot();
                int renders = PlanarMirrors.Renders - r0;
                rig.Puddle.enabled = false;
                s.Shot();
                var b = s.Shot();
                rig.Puddle.enabled = true;
                Check("the main image with an invisible puddle's mirror equals the image with no mirror", renders == 2 && Diff(a, b, 0) == 0, $"{renders} mirror renders, {Diff(a, b, 0)} of {a.Length} px differ");
                Check("and repeats", Diff(a, a2, 0) == 0, $"{Diff(a, a2, 0)} px differ");
                s.Light.shadows = LightShadows.None;
            }
        });

        Group("WO4", "no puddle: nothing subscribed, no camera, no target", () =>
        {
            Check("at the start", !PlanarMirrors.Subscribed && PlanarMirrors.Cameras == 0 && PlanarMirrors.Targets == 0 && PlanarMirrors.Planes == 0, $"subscribed {PlanarMirrors.Subscribed}, cameras {PlanarMirrors.Cameras}, targets {PlanarMirrors.Targets}, planes {PlanarMirrors.Planes}");
            using (var s = new Stage(false, 640, 480))
            {
                int r0 = PlanarMirrors.Renders;
                s.Aim(s.Centre, Vector3.forward, 30f);
                s.Shot();
                Check("a render with no plane renders no mirror", PlanarMirrors.Renders == r0, "");
                using (var rig = new WetMirrorRig(-0.1f, 0.5f))
                {
                    Check("a plane: subscribed", PlanarMirrors.Subscribed && PlanarMirrors.Planes == 1, "");
                    rig.Lay(new Vector3(0f, 0f, 5000f), 1f);
                    s.Shot();
                    Check("a puddle out of view renders no mirror", PlanarMirrors.Renders == r0 && PlanarMirrors.Targets == 0, $"{PlanarMirrors.Renders - r0}");
                    MirrorView(s, rig);
                    s.Shot();
                    Check("a puddle in view renders one", PlanarMirrors.Renders == r0 + 1 && PlanarMirrors.Cameras == 1, $"{PlanarMirrors.Renders - r0}");
                    Check("the target is released at the camera's end", PlanarMirrors.Targets == 0, $"{PlanarMirrors.Targets}");
                }
                Check("the last handle gone: not subscribed, no camera, no target", !PlanarMirrors.Subscribed && PlanarMirrors.Cameras == 0 && PlanarMirrors.Targets == 0 && PlanarMirrors.Planes == 0, "");
            }
        });

        Group("WO5", "a puddle does not reflect itself; an exception leaves nothing behind", () =>
        {
            using (var s = new Stage(false, 960, 720))
            using (var rig = new WetMirrorRig(-0.1f, 0.5f))
            {
                rig.Look(new Color(1f, 0f, 1f, 1f), 0f);
                MirrorView(s, rig);
                var main = s.Shot();
                int inMain = main.Count(Magenta), inMirror = rig.Bytes().Count(Magenta);
                Check("the puddle shows in the view and not in its own mirror", inMain > 1000 && inMirror == 0, $"{inMain} px in the view, {inMirror} in the mirror");
                var stackField = typeof(MosaicCamera).GetField("stack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var stack = (System.Collections.IList)stackField.GetValue(null);
                int depth = stack.Count;
                bool inv = GL.invertCulling;
                VolumeStack inside = null;
                int f0 = PlanarMirrors.Failures;
                var keep = PlanarMirrors.BeforeRender;
                int inner = -1;
                PlanarMirrors.BeforeRender = rc => { inner = stack.Count; inside = VolumeManager.instance.stack; throw new InvalidOperationException("test"); };
                try { s.Shot(); }
                finally { PlanarMirrors.BeforeRender = keep; }
                Check("the exception is counted, the camera renders on", PlanarMirrors.Failures == f0 + 1, $"{PlanarMirrors.Failures - f0}");
                Check("the hidden camera's frame was on the stack and is gone", inner == depth + 2 && stack.Count == depth, $"inside {inner}, after {stack.Count}, before {depth}");
                Check("forceRenderingOff and GL.invertCulling are as before", !rig.Puddle.forceRenderingOff && GL.invertCulling == inv, "");
                Check("the mirror's volume stack was in place and is gone", inside != null && VolumeManager.instance.stack != inside, "");
                Check("the target went back", PlanarMirrors.Targets == 0, $"{PlanarMirrors.Targets}");
                Check("the next render works", rig.Bytes() != null && s.Shot() != null && PlanarMirrors.Failures == f0 + 1, "");
            }
        });

        Group("WO7", "two planes; a third is skipped and counted", () =>
        {
            using (var s = new Stage(false, 960, 720))
            using (var a = new WetMirrorRig(0f, 0.5f))
            using (var b = new WetMirrorRig(0.5f, 0.5f))
            using (var c = new WetMirrorRig(1f, 0.5f))
            {
                s.Ctx.Root.SetActive(false);
                s.Cam.backgroundColor = Color.white;
                s.Cam.transform.position = new Vector3(0f, 20f, -30f);
                s.Cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -20f, 30f), Vector3.up);
                a.Lay(new Vector3(-6f, 0f, 0f), 2f); b.Lay(new Vector3(0f, 0f, 0f), 2f); c.Lay(new Vector3(6f, 0f, 0f), 2f);
                foreach (var r in new[] { a, b, c }) r.Look(new Color(0.1f, 0.1f, 0.1f, 1f), 3.5f);
                s.Shot();
                int r0 = PlanarMirrors.Renders, k0 = PlanarMirrors.SkippedOverCap;
                var px = s.Shot();
                Check("two mirrors render, the third is counted", PlanarMirrors.Renders - r0 == 2 && PlanarMirrors.SkippedOverCap - k0 == 1, $"{PlanarMirrors.Renders - r0} rendered, {PlanarMirrors.SkippedOverCap - k0} skipped");
                Color32 At(Vector3 w) { var v = s.Cam.WorldToViewportPoint(w); return px[(int)(v.y * s.Rt.height) * s.Rt.width + (int)(v.x * s.Rt.width)]; }
                // the background is white: a puddle with an image shows it, the skipped one its disc
                var shown = new[] { At(new Vector3(-6f, 0.015f, 0f)), At(new Vector3(0f, 0.515f, 0f)), At(new Vector3(6f, 1.015f, 0f)) };
                int discs = shown.Count(x => x.r < 120);
                Info($"puddle colours: {string.Join(", ", shown.Select(x => $"{x.r} {x.g} {x.b}"))}");
                Check("one puddle shows its disc, two show their image", discs == 1, $"{discs} discs");
                s.Ctx.Root.SetActive(true);
                s.Cam.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            }
        });

        Group("WO9", "stains, film and drops appear in the mirror image", () =>
        {
            using (var s = new Stage(false, 960, 720))
            using (var rig = new WetMirrorRig(-0.1f, 0.5f))
            {
                bool wide = MosaicWide.Enabled;
                MosaicWide.Enabled = false;
                MirrorView(s, rig);
                s.Shot();
                s.Shot();
                var dry = rig.Bytes();
                var l = PaintAll(s, t => t.Kind == WetKinds.Clothes || t.Kind == WetKinds.Skin, new Color(0.1f, 0.9f, 0.1f), true);
                foreach (var p in l) p.Film?.Suspend(true);
                s.Shot();
                var stained = rig.Bytes();
                Check("the stain shows", Diff(dry, stained) > 500, $"{Diff(dry, stained)} of {dry.Length} px");
                foreach (var p in l) p.Film?.Suspend(false);
                s.Shot();
                var film = rig.Bytes();
                Check("the film shows", Diff(stained, film) > 500, $"{Diff(stained, film)} px");
                Unpaint(s, l);
                // a droplet: one mesh in world space
                var mesh = new Mesh { name = "wet_test_drops" };
                var at = s.Centre;
                mesh.vertices = new[] { at, at, at };
                mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
                mesh.uv = new[] { new Vector2(-3f, -3f), new Vector2(3f, -3f), new Vector2(0f, 3f) };
                mesh.colors32 = new[] { new Color32(255, 0, 255, 255), new Color32(255, 0, 255, 255), new Color32(255, 0, 255, 255) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bounds = new Bounds(at, Vector3.one * 100f);
                var go = new GameObject("wet_test_drops") { layer = 10 };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mat = new Material(Shader.Find("HS2Next/Hidden/WetDrops"));
                mat.renderQueue = 3000;
                mr.sharedMaterial = mat;
                s.Shot();
                int drops = rig.Bytes().Count(Magenta);
                Check("a droplet shows, oriented from the mirror's camera", drops > 50, $"{drops} px");
                Object.DestroyImmediate(go); Object.DestroyImmediate(mat); Object.DestroyImmediate(mesh);
                MosaicWide.Enabled = wide;
            }
        });

        Group("WO10", "a camera with no mirror of its own samples no released target", () =>
        {
            int m0 = Shader.PropertyToID("_HS2WetMirror0"), m1 = Shader.PropertyToID("_HS2WetMirror1"), pl = Shader.PropertyToID("_HS2WetMirrorPlanes");
            using (var s = new Stage(false, 640, 480))
            using (var rig = new WetMirrorRig(-0.1f, 0.5f))
            {
                rig.Look(new Color(0.8f, 0.2f, 0.2f, 1f), 1f);
                MirrorView(s, rig);
                int f0 = PlanarMirrors.Failures;
                s.Shot();
                Check("after the camera's end the globals read black", Shader.GetGlobalTexture(m0) == Texture2D.blackTexture && Shader.GetGlobalTexture(m1) == Texture2D.blackTexture && Shader.GetGlobalVector(pl) == Vector4.zero, "");
                // a reflection probe's camera and a thumbnail on another layer ask for nothing
                int r0 = PlanarMirrors.Renders;
                var go = new GameObject("wet_test_probe_camera");
                var other = go.AddComponent<Camera>();
                other.enabled = false;
                other.CopyFrom(s.Cam);
                var d = go.AddComponent<UniversalAdditionalCameraData>();
                d.renderPostProcessing = false;
                var rt = new RenderTexture(s.Rt.width, s.Rt.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                other.targetTexture = rt;
                other.cameraType = CameraType.Reflection;
                other.Render();
                Check("a reflection camera renders no mirror", PlanarMirrors.Renders == r0, $"{PlanarMirrors.Renders - r0}");
                var disc = Read(rt);
                other.cameraType = CameraType.Game;
                other.cullingMask = 1 << 24;
                other.Render();
                Check("a camera on an isolation layer renders no mirror", PlanarMirrors.Renders == r0, $"{PlanarMirrors.Renders - r0}");
                // a nested camera (a map mirror's) inside a view that has no mirror of its own: the disc
                other.cullingMask = s.Cam.cullingMask;
                var innerRt = new RenderTexture(s.Rt.width, s.Rt.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var inner = new GameObject("wet_test_nested").AddComponent<Camera>();
                inner.enabled = false; inner.CopyFrom(s.Cam);
                inner.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
                inner.targetTexture = innerRt;
                void Nest(ScriptableRenderContext c, Camera cm)
                {
                    if (cm != other) return;
#pragma warning disable 618
                    UniversalRenderPipeline.RenderSingleCamera(c, inner);
#pragma warning restore 618
                }
                RenderPipelineManager.beginCameraRendering += Nest;
                var below = s.Cam.transform.position;
                other.transform.position = new Vector3(below.x, -5f, below.z);
                try { other.Render(); }
                finally { RenderPipelineManager.beginCameraRendering -= Nest; }
                var nested = Read(innerRt);
                Check("a nested camera in a view with no mirror renders none and fails nothing", PlanarMirrors.Renders == r0 && PlanarMirrors.Failures == f0, $"{PlanarMirrors.Renders - r0}");
                var (mx, mean) = Spread(nested, disc);
                Check("it shows the puddle's disc, as the reflection camera does", mx <= 2, $"max {mx}, mean {mean:F3}");
                other.targetTexture = null; inner.targetTexture = null;
                rt.Release(); innerRt.Release();
                Object.DestroyImmediate(rt); Object.DestroyImmediate(innerRt); Object.DestroyImmediate(go); Object.DestroyImmediate(inner.gameObject);
            }
        });

        Group("WO6", "view, capture, screenshot and a recorded frame take the mirror", () =>
        {
            using (var s = new Stage(false, 960, 540))
            using (var rig = new WetMirrorRig(-0.1f, 0.5f))
            {
                bool wide = MosaicWide.Enabled;
                MosaicWide.Enabled = false;
                rig.Look(new Color(1f, 1f, 1f, 1f), 10f);
                MirrorView(s, rig);
                s.Shot();
                var one = s.Shot();
                void Against(string label, Func<byte[]> png)
                {
                    int r0 = PlanarMirrors.Renders;
                    var bytes = png();
                    int renders = PlanarMirrors.Renders - r0;
                    var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    t.LoadImage(bytes);
                    var px = t.GetPixels32();
                    Object.DestroyImmediate(t);
                    bool same = px.Length == one.Length;
                    int w = s.Rt.width, h = s.Rt.height;
                    // the puddle's middle: the image of the body
                    var v = s.Cam.WorldToViewportPoint(new Vector3(rig.Puddle.transform.position.x, rig.Floor, rig.Puddle.transform.position.z));
                    long sum = 0; int n = 0, cx = (int)(v.x * w), cy = (int)(v.y * h);
                    if (same)
                        for (int y = cy - 40; y < cy + 40; y++)
                            for (int x = cx - 40; x < cx + 40; x++)
                            {
                                var a = px[y * w + x]; var b = one[y * w + x];
                                sum += Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b); n++;
                            }
                    double mean = n > 0 ? sum / (3.0 * n) : -1;
                    Check($"{label}: the mirror renders in it, the puddle's pixels agree with one render of that camera", renders >= 1 && same && mean <= 2.0, $"{renders} mirror renders, mean {mean:F3} of 255 over {n} px");
                }
                Against("capture", () => HS2Next.App.Commands.ViewCapture.Render(s.Cam, s.Rt.width, s.Rt.height, CaptureService.None));
                Against("the screenshot's and a recorded frame's call", () => CaptureService.RenderSync(null, s.Cam, new CaptureRequest { Width = s.Rt.width, Height = s.Rt.height, Supersample = 1, Aa = CaptureService.None }).Png);
                MosaicWide.Enabled = wide;
            }
        });
    }


    /// <summary>Real frames on a map with mirrors: the app's own view camera, the map's own mirrors.</summary>
    static IEnumerator Play_R()
    {
        if (!POpen("WO8", "map mirrors are not touched")) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, "")) yield break;
        yield return PCall(app, new { cmd = "map.load", @ref = "illusion.hs2#29" });
        if (!Check("map.load illusion.hs2#29", POk && (string)pLast["state"] == "Loaded", Short(pLastText))) yield break;
        yield return PFrames(10);
        Check("the map has mirrors", MapMirrors.ActiveHandles > 0, $"{MapMirrors.ActiveHandles} handles");
        var view = app.Runtime.GetPart<SceneViewCamera>().Camera;
        var map = app.Runtime.GetPart<MapRealization>();
        if (!Check("a mirror renderer", map.Structure.Mirrors.Any(m => m.Renderer), "")) yield break;
        var mirror = map.Structure.Mirrors.First(m => m.Renderer);
        // the map's mirror in view: its renders per view frame, a plane on and off
        var c = mirror.Renderer.bounds.center;
        var nrm = mirror.Stub.transform.up;
        var pos = c + nrm * 2.5f + Vector3.up * 0.4f + Vector3.Cross(nrm, Vector3.up).normalized * 0.6f;
        var rot = Quaternion.LookRotation(c - pos).eulerAngles;
        yield return PCall(app, new { cmd = "camera.set", target = new[] { c.x, c.y, c.z }, rot = new[] { rot.x, rot.y, rot.z }, dist = (c - pos).magnitude });
        yield return PFrames(5);
        // a batch Editor shows no view: each camera render here is a capture of the view
        const int Renders = 5;
        int CameraRenders()
        {
            int m0 = MapMirrors.RendersTotal;
            for (int i = 0; i < Renders; i++) HS2Next.App.Commands.ViewCapture.Render(view, 640, 360, HS2Next.Screenshots.CaptureService.None);
            return MapMirrors.RendersTotal - m0;
        }
        int before = CameraRenders();
        using (var rig = new WetMirrorRig(view.transform.position.y - 1.5f, 0.5f))
        {
            rig.Lay(view.transform.position + view.transform.forward * 5f, 4f);
            int p0 = PlanarMirrors.Renders;
            int with = CameraRenders();
            int planar = PlanarMirrors.Renders - p0;
            Check("the view has the map's mirror and the plane's in it", before > 0 && planar > 0, $"{before} map mirror renders, {planar} planar mirror renders in {Renders * 2} camera renders");
            Check("map mirror renders per camera render are the same with a plane on", with == before, $"{with} against {before}");
        }
        // with the reflections on: what a map mirror's camera does (information for the plan; nothing of this phase touches it)
        int errors = 0;
        string first = null;
        Application.LogCallback log = (text, stack, type) =>
        {
            if ((type == LogType.Error || type == LogType.Exception) && (text.Contains("Render Graph") || text.Contains("NullReference"))) { errors++; first = first ?? text; }
        };
        Application.logMessageReceived += log;
        yield return PCall(app, new { cmd = "app.options", render = new { ssr = "on" } });
        yield return PFrames(5);
        int under = CameraRenders();
        yield return PFrames(2);
        Application.logMessageReceived -= log;
        Info($"a map mirror under render.ssr on: {under} map mirror renders, {errors} render errors, ssr drawn {SsrControl.Last.Drawn} ({SsrControl.Last.Reason}){(first != null ? ", first: " + Short(first) : "")}");
        // a planar mirror under the same option, on a map with no mirror of its own: the render is clean
        yield return PCall(app, new { cmd = "map.load", @ref = "illusion.hs2#10" });
        Check("map.load illusion.hs2#10", POk && (string)pLast["state"] == "Loaded", Short(pLastText));
        yield return PFrames(5);
        Check("that map has no mirror", MapMirrors.ActiveHandles == 0, $"{MapMirrors.ActiveHandles} handles");
        yield return PCall(app, new { cmd = "camera.frame", point = new[] { 0f, 8f, 0f }, dist = 25f, yaw = 180f, pitch = 20f });
        yield return PFrames(5);
        errors = 0; first = null;
        Application.logMessageReceived += log;
        using (var rig = new WetMirrorRig(view.transform.position.y - 6f, 0.5f))
        {
            rig.Lay(view.transform.position + view.transform.forward * 18f, 6f);
            int p0 = PlanarMirrors.Renders, f0 = PlanarMirrors.Failures;
            CameraRenders();
            Check("a planar mirror under render.ssr on: no render error", PlanarMirrors.Renders - p0 == Renders * 2 && PlanarMirrors.Failures == f0 && errors == 0, $"{PlanarMirrors.Renders - p0} planar mirror renders, {errors} render errors, ssr drawn {SsrControl.Last.Drawn}{(first != null ? ", first: " + Short(first) : "")}");
        }
        Application.logMessageReceived -= log;
        yield return PCall(app, new { cmd = "app.options", render = new { ssr = "off" } });
        yield return PCall(app, new { cmd = "map.load", clear = true });
    }


    /// <summary>The bake and the compose on code-made inputs: where a value lands, BlendOp Min, the map's numbers, the PNG, the colour against WetMath.</summary>
    static void BakeGroups()
    {
        Group("WD0", "the bake and the compose by numbers", () =>
        {
            const int N = 64;
            float reach = 3f;
            // two triangles over the lower left corner, the same one drawn again with a larger distance after it, and one in the upper right
            var uv = new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(0.75f, 1f), new Vector2(1f, 0.75f) };
            var d = new[] { 1f, 1f, 1f, 2f, 2f, 2f, 2.5f, 2.5f, 2.5f };
            var tri = new[] { 3, 4, 5, 0, 1, 2, 6, 7, 8 };
            var map = WetBake.Bake(uv, tri, d, N, N, reach);
            var r8 = WetBake.ReadBack(map);
            int V(float dist) => (int)Math.Round(254.0 * dist / reach, MidpointRounding.AwayFromZero);
            Check("BlendOp Min keeps the least of overlapping triangles", r8[8 * N + 8] == V(1f), $"texel (8, 8) = {r8[8 * N + 8]}, expected {V(1f)}");
            Check("v 0 is row 0, u 0 is column 0", r8[(N - 6) * N + (N - 6)] == V(2.5f) && r8[8 * N + (N - 8)] == WetBake.Unreached, $"upper right {r8[(N - 6) * N + (N - 6)]}, lower right {r8[8 * N + (N - 8)]}");
            int edge = -1;
            for (int x = 0; x < N; x++) if (r8[2 * N + x] == WetBake.Unreached) { edge = x; break; }
            Check("two dilation passes: the triangle's edge grows by two texels", edge >= 31 && edge <= 34, $"row 2 reaches column {edge}, the triangle ends at about 30.5");
            var png = WetBake.Encode(r8, N, N);
            Check("the picture is an 8-bit grey PNG", png.Length > 33 && png[24] == 8 && png[25] == 0, $"depth {png[24]}, colour type {png[25]}, {png.Length} bytes");
            var back = WetBake.Decode(png, out var r8b);
            var r8c = WetBake.ReadBack(back);
            Check("decode: the same bytes on the CPU and on the GPU", r8b != null && r8b.SequenceEqual(r8) && r8c.SequenceEqual(r8), "");
            var far = WetBake.Bake(uv, tri, new[] { 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f, 9f }, N, N, reach);
            Check("a distance at or past the reach is 255", WetBake.ReadBack(far).All(b => b == WetBake.Unreached), "");
            WetBake.Free(far); WetBake.Free(back); WetBake.Free(map);

            // the colour: a flat base through the shader against WetMath, on encoded values
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false, false) { name = "wet_test_base" };
            var bytes = new Color32(180, 96, 40, 200);
            tex.SetPixels32(Enumerable.Repeat(bytes, 256).ToArray());
            tex.Apply(false);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var mat = new Material(Shader.Find("HS2Next/AIT Clothes True"));
            mat.SetTexture(MainTexId, tex);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var half = new byte[16 * 16];
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) half[y * 16 + x] = x < 8 ? (byte)0 : WetBake.Unreached;
            var hm = WetBake.Decode(WetBake.Encode(half, 16, 16), out _);
            hm.filterMode = FilterMode.Point;
            var look = new HS2Next.Scenes.Wet.WetColourValues { Saturate = 1.15, Tint = new HS2Next.Scenes.Wet.WetRgb(1, 0.9, 0.8), Stain = new HS2Next.Scenes.Wet.WetRgb(0.42, 0.03, 0.04), Amount = 0.85, Skin = 0.5 };
            var c = new WetCompose(go.GetComponent<Renderer>(), 0, WetCompose.ClothesCap, true);
            var layer = new WetLayer { Map = hm, Radius = 3f, Feather = 0.195f, Reach = 3f, Saturate = 1.15f, Amount = 0.85f, Tint = new Color(1f, 0.9f, 0.8f), Stain = new Color(0.42f, 0.03f, 0.04f), Skin = 0.5f };
            c.Compose(new[] { layer });
            var px = Read(c.Wet);
            var m = WetMath_Over(bytes, look, 0.5);
            var got = px[8 * 16 + 2]; var off = px[8 * 16 + 13];
            Check("the composite's colour equals WetMath.Over within 1 / 255", Math.Abs(got.r - m.r) <= 1 && Math.Abs(got.g - m.g) <= 1 && Math.Abs(got.b - m.b) <= 1, $"{got.r} {got.g} {got.b}, expected {m.r} {m.g} {m.b}");
            Check("alpha is the base's", got.a == bytes.a, $"{got.a}");
            Check("outside the stain the composite is the base's bytes", off.r == bytes.r && off.g == bytes.g && off.b == bytes.b && off.a == bytes.a, $"{off.r} {off.g} {off.b} {off.a}");
            Check("the stain lies on the side of the map", got.r != off.r, "");
            var mask = WetBake.ReadBack(c.Mask);
            Check("the mask is the amount", mask[8 * 16 + 2] == 255 && mask[8 * 16 + 13] == 0, $"{mask[8 * 16 + 2]}, {mask[8 * 16 + 13]}");
            // two stains: the second over what the first left; the mask is the larger
            var layer2 = layer; layer2.Radius = 1.5f; layer2.Stain = new Color(0f, 0f, 0f); layer2.Amount = 0.3f; layer2.Saturate = 1f; layer2.Tint = Color.white; layer2.Skin = 1f;
            c.Compose(new[] { layer, layer2 });
            var two = Read(c.Wet)[8 * 16 + 2];
            var look2 = new HS2Next.Scenes.Wet.WetColourValues { Saturate = 1, Tint = new HS2Next.Scenes.Wet.WetRgb(1, 1, 1), Stain = new HS2Next.Scenes.Wet.WetRgb(0, 0, 0), Amount = 0.3, Skin = 1 };
            var m2 = WetMath_Over(got, look2, 1.0);
            Check("two stains compose in order", Math.Abs(two.r - m2.r) <= 2 && Math.Abs(two.g - m2.g) <= 2 && Math.Abs(two.b - m2.b) <= 2, $"{two.r} {two.g} {two.b}, expected {m2.r} {m2.g} {m2.b}");
            c.Dispose(); WetBake.Free(hm);
            Object.DestroyImmediate(go); Object.DestroyImmediate(mat); Object.DestroyImmediate(tex);
        });
    }

    static Color32 WetMath_Over(Color32 c, in HS2Next.Scenes.Wet.WetColourValues look, double factor)
    {
        var look1 = look;
        look1.Skin = factor;
        var w = HS2Next.Scenes.Wet.WetMath.Over(new HS2Next.Scenes.Wet.WetRgb(c.r / 255.0, c.g / 255.0, c.b / 255.0), look1, 1.0, true);
        byte B(double v) => (byte)Math.Round(Math.Min(1.0, Math.Max(0.0, v)) * 255.0);
        return new Color32(B(w.R), B(w.G), B(w.B), c.a);
    }

    static void Render_R()
    {
        BakeGroups();
        FilmGroups();
        MirrorGroups();
        Group("WD1", "the override reaches clothes, skin, face and hair", () =>
        {
            foreach (bool male in new[] { false, true })
                using (var s = new Stage(male))
                {
                    s.Aim(s.Centre, s.Ctx.Root.transform.forward, 40f);
                    s.Shot();
                    var basePx = s.Shot();
                    Check($"{(male ? "HERO107" : "BR-Chan")}: two renders of the dry character equal", Diff(s.Shot(), basePx, 0) == 0, "");
                    foreach (var kind in new[] { WetKinds.Clothes, WetKinds.Skin, WetKinds.Face, WetKinds.Hair })
                    {
                        var l = PaintAll(s, t => t.Kind == kind, new Color(0.9f, 0.05f, 0.9f), false);
                        var px = s.Shot();
                        int changed = Diff(px, basePx);
                        int bound = l.Count(p => p.C.Bound);
                        Check($"{(male ? "HERO107" : "BR-Chan")} {kind}: pixels change through the override", l.Count > 0 && bound == l.Count && changed > 0, $"{l.Count} targets, {bound} bound, {changed} px");
                        Unpaint(s, l);
                        Check($"{(male ? "HERO107" : "BR-Chan")} {kind}: off again gives the same bytes", Diff(s.Shot(), basePx, 0) == 0, "");
                    }
                }
        });

        Group("WD1f", "the override on a URP/Lit fallback", () =>
        {
            using (var s = new Stage())
            {
                s.Ctx.Root.SetActive(false);
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.position = new Vector3(0f, 10f, -60f); go.transform.localScale = Vector3.one * 8f;
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false, false) { name = "wet_test_base" };
                tex.SetPixels32(Enumerable.Repeat(new Color32(120, 130, 140, 255), 256).ToArray());
                tex.Apply(false);
                var src = new Material(Shader.Find("GUI/Text Shader"));
                src.SetTexture(MainTexId, tex);
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = src;
                ShaderMap.ApplyUrp(go, false);
                var m = r.sharedMaterial;
                s.Cam.transform.position = new Vector3(0f, 10f, -90f);
                s.Cam.transform.rotation = Quaternion.identity;
                s.Shot();
                var dry = s.Shot();
                if (Check("the material is a fallback", ShaderMap.IsFallback(m), m.shader.name))
                {
                    var map = SheetMap();
                    var c = new WetCompose(r, 0, WetCompose.ClothesCap, false);
                    c.Compose(new[] { Solid(map, 3f, new Color(0.9f, 0.05f, 0.05f)) });
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block, 0);
                    int changed = Diff(s.Shot(), dry);
                    Check("the block carries _BaseMap and the view changes", block.HasTexture("_BaseMap") && block.GetTexture("_BaseMap") == c.Wet && changed > 1000, $"{changed} px");
                    c.Dispose(); WetBake.Free(map);
                    Check("off again: the same bytes", Diff(s.Shot(), dry, 0) == 0, "");
                }
                Object.DestroyImmediate(go); Object.DestroyImmediate(src); Object.DestroyImmediate(tex);
                s.Ctx.Root.SetActive(true);
            }
        });

        Group("WD3", "the film over its garment", () =>
        {
            foreach (bool male in new[] { false, true })
                using (var s = new Stage(male))
                {
                    string who = male ? "HERO107" : "BR-Chan";
                    var l = PaintAll(s, t => t.Kind == WetKinds.Clothes, new Color(0.9f, 0.05f, 0.05f), true);
                    var films = l.Where(p => p.Film != null).ToList();
                    if (!Check($"{who}: films exist", films.Count > 0, $"{films.Count} of {l.Count} clothes targets")) continue;
                    var fwd = s.Ctx.Root.transform.forward;
                    var views = new (string name, Vector3 target, Vector3 dir, float dist)[]
                    {
                        ("near", s.Centre + Vector3.up * 3f, fwd, 8f), ("mid", s.Centre, fwd, 25f), ("3/4", s.Centre, Quaternion.AngleAxis(40f, Vector3.up) * fwd, 25f),
                        ("back", s.Centre, -fwd, 25f), ("far", s.Centre, fwd, 120f),
                    };
                    void Film(bool on) { foreach (var p in films) { p.Film.Renderer.gameObject.SetActive(on); p.Film.LateApply(); } }
                    void Offset(float f, float u) { foreach (var p in films) { p.Film.Material.SetFloat("_WetFilmOffsetFactor", f); p.Film.Material.SetFloat("_WetFilmOffsetUnits", u); } }
                    void Flat(float v) { foreach (var p in films) p.Film.Material.SetFloat("_WetFilmFlat", v); }
                    foreach (var v in views)
                    {
                        s.Aim(v.target, v.dir, v.dist);
                        Film(false);
                        s.Shot();
                        var with = s.Shot();
                        foreach (var p in films) { p.Layer.Stain = new Color(0.05f, 0.9f, 0.05f); p.Compose(); }
                        var without = s.Shot();
                        foreach (var p in films) { p.Layer.Stain = new Color(0.9f, 0.05f, 0.05f); p.Compose(); }
                        Film(true); Flat(1f); Offset(0f, FilmUnits);
                        var flat = s.Shot();
                        var flat2 = s.Shot();
                        Offset(0f, 0f);
                        var plain = s.Shot();
                        Offset(0f, FilmUnits); Flat(0f);
                        int garment = 0, film = 0, holes = 0, spill = 0, spill0 = 0, flick = 0;
                        for (int i = 0; i < with.Length; i++)
                        {
                            bool g = Math.Abs(with[i].r - without[i].r) + Math.Abs(with[i].g - without[i].g) + Math.Abs(with[i].b - without[i].b) > 6;
                            bool f = Magenta(flat[i]), f0 = Magenta(plain[i]);
                            if (g) garment++;
                            if (f) film++;
                            if (g && !f) holes++;
                            if (f && !g) spill++;
                            if (f0 && !g) spill0++;
                            if (!Same(flat[i], flat2[i])) flick++;
                        }
                        double pct = garment > 0 ? 100.0 * holes / garment : 100;
                        Check($"{who} {v.name}: holes <= 0.1 % of the garment's pixels", garment > 0 && pct <= 0.1, $"{holes} of {garment} px ({pct:F3} %), film {film} px");
                        double extra = garment > 0 ? 100.0 * (spill - spill0) / garment : 0;
                        if (spill > spill0) Warn($"{who} {v.name}: spill {spill} px, without the offset {spill0} px, {spill - spill0} more ({extra:F4} % of the garment's pixels)");
                        Check($"{who} {v.name}: spill at most 0.01 % of the garment's pixels above the spill without the offset", extra <= 0.01, $"{spill} px, without the offset {spill0} px");
                        Check($"{who} {v.name}: two renders equal", flick == 0, $"{flick} px differ");
                    }
                }
        });

        Group("WO1", "mirror geometry", () =>
        {
            using (var s = new Stage())
            using (var rig = new WetMirrorRig(0f, 0.5f))
            {
                s.Ctx.Root.SetActive(false);
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                GameObject Sphere(Vector3 p, Color c)
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.transform.position = p; g.transform.localScale = Vector3.one * 0.5f;
                    var m = new Material(unlit); m.SetColor("_BaseColor", c);
                    g.GetComponent<Renderer>().sharedMaterial = m;
                    return g;
                }
                var upPos = new Vector3(0f, 1.2f, -10f);
                var up = Sphere(upPos, new Color(1f, 0f, 1f, 1f));
                var dn = Sphere(new Vector3(3f, -1.2f, -10f), new Color(0f, 1f, 0f, 1f));
                try
                {
                    s.Cam.transform.position = new Vector3(0f, 6f, -40f);
                    s.Cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -1.9f, 30f), Vector3.up);
                    rig.Lay(new Vector3(0f, 0f, -10f), 30f);
                    rig.Look(new Color(0.1f, 0.097f, 0.097f, 1f), 1f);
                    var main = s.Shot();
                    var px = rig.Image();
                    int w = rig.W, h = rig.H, n = 0, low = 0; double sx = 0, sy = 0;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = px[y * w + x];
                            if (c.r > 0.7f && c.b > 0.7f && c.g < 0.2f) { sx += x + 0.5; sy += y + 0.5; n++; }
                            if (c.g > 0.7f && c.r < 0.2f && c.b < 0.2f) low++;
                        }
                    var vp = s.Cam.WorldToViewportPoint(rig.Reflect(upPos));
                    double ex = vp.x * w, ey = vp.y * h, cx = n > 0 ? sx / n : -1, cy = n > 0 ? sy / n : -1;
                    Check("the marker above the plane: within 1 px of where the view sees its reflection", n > 20 && Math.Abs(cx - ex) <= 1 && Math.Abs(cy - ey) <= 1, $"{n} px, centroid {cx:F2}, {cy:F2}, expected {ex:F2}, {ey:F2}, target {w}x{h}");
                    Check("the marker below the plane: 0 px", low == 0, $"{low} px");
                    // the image holds the main view's values: a grey marker through both
                    var gm = new Material(unlit); gm.SetColor("_BaseColor", new Color(0.35f, 0.5f, 0.2f, 1f));
                    up.GetComponent<Renderer>().sharedMaterial = gm;
                    var mainGrey = s.Shot();
                    var bytes = rig.Bytes();
                    var vm = s.Cam.WorldToViewportPoint(upPos);
                    var cm = mainGrey[(int)(vm.y * s.Rt.height) * s.Rt.width + (int)(vm.x * s.Rt.width)];
                    var cr = bytes[(int)(vp.y * h) * w + (int)(vp.x * w)];
                    Check("the mirror's target holds the view's values: one marker through both", Math.Abs(cm.r - cr.r) <= 2 && Math.Abs(cm.g - cr.g) <= 2 && Math.Abs(cm.b - cr.b) <= 2, $"view {cm.r} {cm.g} {cm.b}, mirror {cr.r} {cr.g} {cr.b}, target sRGB {rig.Srgb}, view target sRGB {s.Rt.sRGB}");
                    up.GetComponent<Renderer>().sharedMaterial.color = Color.magenta;
                    Object.DestroyImmediate(gm);
                    up.GetComponent<Renderer>().sharedMaterial = new Material(unlit) { color = new Color(1f, 0f, 1f, 1f) };
                    // the same point through the puddle on the final image
                    int fw = s.Rt.width, fh = s.Rt.height, fn = 0; double fx = 0, fy = 0;
                    for (int y = 0; y < fh; y++)
                        for (int x = 0; x < fw; x++)
                        {
                            var c = main[y * fw + x];
                            if (c.r > 90 && c.b > 90 && c.g < 60 && Math.Abs(c.r - c.b) < 40) { fx += x + 0.5; fy += y + 0.5; fn++; }
                        }
                    // the direct sphere is above the plane too: take the pixels below the plane's horizon of the reflection
                    var direct = s.Cam.WorldToViewportPoint(upPos);
                    Info($"final image: {fn} magenta px, centroid {(fn > 0 ? fx / fn : -1):F2}, {(fn > 0 ? fy / fn : -1):F2}; direct at {direct.x * fw:F1}, {direct.y * fh:F1}; reflection at {vp.x * fw:F1}, {vp.y * fh:F1}");
                    int near = 0;
                    for (int y = 0; y < fh; y++)
                        for (int x = 0; x < fw; x++)
                        {
                            var c = main[y * fw + x];
                            if (c.r > 60 && c.b > 60 && c.g < 60 && (x + 0.5 - vp.x * fw) * (x + 0.5 - vp.x * fw) + (y + 0.5 - vp.y * fh) * (y + 0.5 - vp.y * fh) < 400) near++;
                        }
                    Check("the puddle shows the reflection where it is on the screen", near > 30, $"{near} px within 20 px of it");
                }
                finally
                {
                    Object.DestroyImmediate(up); Object.DestroyImmediate(dn);
                    s.Ctx.Root.SetActive(true);
                }
            }
        });
    }
}
