// The app-owned ambient probe (E1 as changed after package H's H1; Phase 3.3 package S).
//
// What HS2 renders (H1, measured in HS2 Studio): Trilight / Flat scenes (no map: the Studio level; all vanilla maps) are lit
// by an SH computed from the three ambient colours — RenderSettings.ambientProbe reads the scene's serialized zero, but
// LightProbes.GetInterpolatedProbe gives the colours' SH — and the ambient intensity is not applied; Skybox-mode scenes
// (most mod maps) keep the probe saved in the scene, and the Studio colours change nothing there.
// Unity 6 (U1 / U2, SkyAmbientTest.MeasureAmbient / MeasureCustom): Trilight colours give the same SH formula as HS2's, but
// HS2 quantizes each linear colour to 8 bits first (round(linear × 255) / 255: the 27 numbers of H1 match to 1e-6 only
// that way); Skybox-mode scenes are recomputed from the skybox at arrival (bushitsu 0.192 instead of the saved 1.074) and
// again on every skybox change; an app-written RenderSettings.ambientProbe is ignored in Trilight / Skybox mode but kept
// in AmbientMode.Custom (through colour and skybox changes, UpdateEnvironment and frames).
//
// Rule hs2 (render.ambient = hs2, default): after every environment change the active scene gets AmbientMode.Custom and
// RenderSettings.ambientProbe = the intended SH:
//   Trilight scene (no map, vanilla maps, Trilight mods)  AmbientSh.Trilight of the state's colours, quantized as HS2
//   Flat scene                                            AmbientSh.Flat of the state's sky colour, quantized ([U]: no Flat
//                                                         map was recorded in HS2; Unity's Flat formula)
//   Skybox / Custom scene (mods)                          the probe saved in the map scene file (MapSavedProbe: the scene's
//                                                         RenderSettings read from its bundle); unreadable → Unity's own
//                                                         value stays (finding map.ambientProbeRecomputed)
// and a guard in LateApply re-applies it when the mode or the probe changed (effects.ambientReasserted, counted).
// Exception (Phase 3.2 package A): a scene with light probes stays in Skybox mode (Source "probes"): Unity 6 gives a renderer
// outside the probes' hull the ambient probe in Custom / Flat / Trilight mode but the probes' extrapolation in Skybox mode.

// Rule environment (render.ambient = environment): the scene's own mode is restored and DynamicGI.UpdateEnvironment
// recomputes (Studio's colours light everything on Trilight scenes, unquantized; Skybox mods from the skybox).
// The scene's own ambient mode is recorded when a map arrives (before the rule switches it); without a map it is Trilight
// (the Studio level). AmbientSh is pure (tests).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HS2Next.Content;
using HS2Next.Content.Studio;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>HS2's ambient SH from the Studio colours (pure; H1 / U1).</summary>
    public static class AmbientSh
    {
        /// <summary>L1 (y) weight of sky − ground (Unity's Trilight projection; U1: 0.272165).</summary>
        public static readonly float K1 = Mathf.Sqrt(2f / 27f);
        /// <summary>L2 weights of 2 equator − sky − ground in coefficients 6 and 8 (U1: 0.026042, 0.078125).</summary>
        public const float K6 = 5f / 192f, K8 = 5f / 64f;

        /// <summary>A gamma colour channel as HS2's SH sees it: linear, quantized to 8 bits (H1).</summary>
        public static float Hs2Linear(float gamma) => Mathf.Round(Mathf.Clamp01(Mathf.GammaToLinearSpace(Mathf.Clamp01(gamma))) * 255f) / 255f;

        static float Lin(float g, bool quantize) => quantize ? Hs2Linear(g) : Mathf.GammaToLinearSpace(g);

        /// <summary>
        /// The Trilight SH of gamma colours: per channel c0 = (S + 4E + G) / 6, c1 (y) = K1 (S − G), c6 = K6 (2E − S − G),
        /// c8 = K8 (2E − S − G), the rest 0 (Unity's coefficient order c × 9 + k). <paramref name="quantize"/>: HS2's 8-bit
        /// linear colours (true) or Unity 6's unquantized ones (false).
        /// </summary>
        public static SphericalHarmonicsL2 Trilight(Color sky, Color equator, Color ground, bool quantize = true)
        {
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++)
            {
                float s = Lin(sky[c], quantize), e = Lin(equator[c], quantize), g = Lin(ground[c], quantize);
                sh[c, 0] = (s + 4f * e + g) / 6f;
                sh[c, 1] = K1 * (s - g);
                float l2 = 2f * e - s - g;
                sh[c, 6] = K6 * l2;
                sh[c, 8] = K8 * l2;
            }
            return sh;
        }

        /// <summary>The Flat SH of a gamma colour (c0 only).</summary>
        public static SphericalHarmonicsL2 Flat(Color sky, bool quantize = true)
        {
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++) sh[c, 0] = Lin(sky[c], quantize);
            return sh;
        }

        /// <summary>The 27 coefficients in Unity's order c × 9 + k.</summary>
        public static double[] ToArray(SphericalHarmonicsL2 sh)
        {
            var a = new double[27];
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) a[c * 9 + k] = sh[c, k];
            return a;
        }

        public static SphericalHarmonicsL2 FromArray(IReadOnlyList<float> a)
        {
            var sh = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) sh[c, k] = a[c * 9 + k];
            return sh;
        }

        public static float MaxDiff(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            float m = 0f;
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) m = Mathf.Max(m, Mathf.Abs(a[c, k] - b[c, k]));
            return m;
        }
    }

    /// <summary>A map scene's RenderSettings as saved in its file (the fields the ambient rule needs).</summary>
    public sealed class SavedRenderSettings
    {
        public bool Ok;
        public string Error;
        public AmbientMode Mode;
        public float Intensity;
        public Color Sky, Equator, Ground;
        public SphericalHarmonicsL2 Probe;
        /// <summary>The bundle node read and the Unity version of its serialized file.</summary>
        public string Node, UnityVersion;
        public double Ms;
        public int BlocksRead;
    }

    /// <summary>
    /// Reads a map scene's saved RenderSettings from its bundle (U1 showed that Unity 6
    /// has already replaced the probe when the scene arrives): the map's bundle file (the user's abdata, or the zipmod entry,
    /// stored or deflated) → UnityFS node "BuildPlayer-&lt;scene&gt;" → the RenderSettings object (class 104) in Unity 2018.4's
    /// layout, which must end exactly at the object's size (fail closed). Cached per bundle source and scene (per process).
    /// </summary>
    public static class MapSavedProbe
    {
        public const int ClassRenderSettings = 104;
        static readonly Dictionary<string, SavedRenderSettings> cache = new Dictionary<string, SavedRenderSettings>(StringComparer.OrdinalIgnoreCase);

        public static void ForgetCache() => cache.Clear();

        /// <summary>The saved settings of a loaded map (never throws; Ok false with the reason).</summary>
        public static SavedRenderSettings Read(MapRealization m, RuntimeServices s)
        {
            var sw = Stopwatch.StartNew();
            SavedRenderSettings r;
            string key = null;
            try
            {
                var row = m?.Row;
                if (row == null || string.IsNullOrEmpty(row.BundlePath)) return Fail("no map row / bundle", sw);
                string scene = !string.IsNullOrEmpty(m.ScenePath) ? Path.GetFileNameWithoutExtension(m.ScenePath) : row.FileName;
                var b = OpenBundle(row, s, out key);
                if (b == null) return Fail("bundle file not found: " + row.BundlePath, sw);
                key += "|" + scene;
                if (cache.TryGetValue(key, out var hit)) return hit;
                r = ReadBundle(b, scene);
                r.BlocksRead = b.BlocksRead;
            }
            catch (Exception e) { r = new SavedRenderSettings { Error = e.GetType().Name + ": " + e.Message }; }
            r.Ms = sw.Elapsed.TotalMilliseconds;
            if (key != null) cache[key] = r;
            return r;
        }

        static SavedRenderSettings Fail(string why, Stopwatch sw) => new SavedRenderSettings { Error = why, Ms = sw.Elapsed.TotalMilliseconds };

        /// <summary>
        /// The bundle of a map row in the catalog's order (ContentCatalog.LoadBundle: an abdata file of the install first,
        /// then the row's zipmod, then any zipmod shipping the path).
        /// </summary>
        static Hs2BundleFile OpenBundle(StudioMap row, RuntimeServices s, out string key)
        {
            string path = row.BundlePath.Replace('\\', '/');
            var cat = s?.Catalog;
            string root = s?.Hs2Root;
            if (!string.IsNullOrEmpty(root) && (cat == null || cat.HasRealBundle(path)))
            {
                string f = Path.Combine(root, "abdata", path);
                if (File.Exists(f))
                {
                    key = f;
                    return Hs2BundleFile.OpenFile(f, 0, new FileInfo(f).Length, f);
                }
            }
            var z = row.Zipmod != null && row.Zipmod.HasBundle(path) ? row.Zipmod : cat?.FindZipmodShipping(path);
            if (z != null && z.Index.Entries.TryGetValue("abdata/" + path, out var ze))
            {
                key = z.Path + "!" + path;
                return ze.Method == 0
                    ? Hs2BundleFile.OpenFile(z.Path, ze.DataOffset, ze.Size, key)
                    : Hs2BundleFile.FromBytes(z.Index.ReadAll(ze), key);
            }
            key = path;
            return null;
        }

        /// <summary>The RenderSettings of the scene node of <paramref name="b"/> (the node of <paramref name="scene"/>).</summary>
        public static SavedRenderSettings ReadBundle(Hs2BundleFile b, string scene)
        {
            Hs2BundleNode? node = b.Find("BuildPlayer-" + scene);
            if (node == null)
            {
                Hs2BundleNode? only = null;
                int n = 0;
                foreach (var x in b.Nodes)
                    if (x.Path.StartsWith("BuildPlayer-", StringComparison.Ordinal) && !x.Path.EndsWith(".sharedAssets", StringComparison.Ordinal)) { only = x; n++; }
                if (n == 1) node = only;
            }
            if (node == null) return new SavedRenderSettings { Error = "no scene node for " + scene };
            var nd = node.Value;
            var f = Hs2SerializedFile.FromStream(() => b.OpenNode(nd), b.Label + "!" + nd.Path);
            Hs2SerializedObject? rs = null;
            foreach (var o in f.Objects)
                if (o.ClassId == ClassRenderSettings)
                {
                    if (rs != null) return new SavedRenderSettings { Error = "several RenderSettings objects", Node = nd.Path };
                    rs = o;
                }
            if (rs == null) return new SavedRenderSettings { Error = "no RenderSettings object", Node = nd.Path, UnityVersion = f.UnityVersion };
            var res = Parse2018(f.Reader(rs.Value));
            res.Node = nd.Path;
            res.UnityVersion = f.UnityVersion;
            return res;
        }

        /// <summary>RenderSettings in Unity 2018.4's layout (333 bytes; the parse must end exactly at the object's end).</summary>
        public static SavedRenderSettings Parse2018(Hs2FieldReader r)
        {
            var s = new SavedRenderSettings();
            Color C() => new Color(r.F32(), r.F32(), r.F32(), r.F32());
            r.Bool();                 // m_Fog
            C();                      // m_FogColor
            r.I32();                  // m_FogMode
            r.F32(); r.F32(); r.F32(); // m_FogDensity, m_LinearFogStart, m_LinearFogEnd
            s.Sky = C(); s.Equator = C(); s.Ground = C();
            s.Intensity = r.F32();
            int mode = r.I32();
            s.Mode = (AmbientMode)mode;
            C();                      // m_SubtractiveShadowColor
            r.PPtr();                 // m_SkyboxMaterial
            r.F32(); r.F32(); r.F32(); // halo, flare strength, flare fade speed
            r.PPtr(); r.PPtr();       // m_HaloTexture, m_SpotCookie
            r.I32(); r.I32(); r.I32(); // reflection mode, resolution, bounces
            r.F32();                  // m_ReflectionIntensity
            r.PPtr();                 // m_CustomReflection
            s.Probe = AmbientSh.FromArray(r.F32s(27));
            r.PPtr(); r.PPtr();       // m_GeneratedSkyboxReflection, m_Sun
            C();                      // m_IndirectSpecularColor
            r.U8();                   // m_UseRadianceAmbientProbe (the last field: no alignment after it)
            r.ExpectEnd("RenderSettings (Unity 2018.4 layout)");
            if (mode != 0 && mode != 1 && mode != 3 && mode != 4) throw new Hs2SerializedFileException("ambient mode " + mode);
            s.Ok = true;
            return s;
        }
    }

    /// <summary>The ambient rule applied to the active scene (owned by SceneSky).</summary>
    public sealed class AmbientProbe
    {
        public const float Tolerance = 1e-6f;

        /// <summary>The rule in effect ("hs2" or "environment").</summary>
        public string Rule { get; private set; } = "hs2";
        /// <summary>What the probe came from: "colours", "flat", "saved", "unity" (rule environment or no saved probe), "probes" (a
        /// map with light probes: Skybox mode kept, see Apply).</summary>
        public string Source { get; private set; }
        /// <summary>The active scene's own ambient mode (map file; Trilight without a map).</summary>
        public AmbientMode SceneMode { get; private set; } = AmbientMode.Trilight;
        public bool HasIntended { get; private set; }
        public SphericalHarmonicsL2 Intended { get; private set; }
        /// <summary>Times the guard found the probe or the mode replaced and wrote it again.</summary>
        public int Reasserted { get; private set; }
        /// <summary>The saved settings of the current map (null without a map).</summary>
        public SavedRenderSettings Saved { get; private set; }
        public int Applied { get; private set; }

        /// <summary>The active scene holds light probes (a map that ships them; Phase 3.2 package A).</summary>
        public static bool MapHasLightProbes => LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0;

        /// <summary>The active scene is a map with these saved settings and this mode as Unity loaded it (null: no map).</summary>
        public void SetScene(AmbientMode? loadedMode, SavedRenderSettings saved)
        {
            Saved = saved;
            SceneMode = saved != null && saved.Ok ? saved.Mode : loadedMode ?? AmbientMode.Trilight;
        }

        /// <summary>Applies the rule to the active scene for the state's colours; returns the finding to report (or null).</summary>
        public string Apply(string rule, Color sky, Color equator, Color ground)
        {
            Rule = rule == "environment" ? "environment" : "hs2";
            Applied++;
            string finding = null;
            if (Rule == "environment")
            {
                HasIntended = false;
                if (RenderSettings.ambientMode != SceneMode) RenderSettings.ambientMode = SceneMode;
                DynamicGI.UpdateEnvironment();
                Source = "unity";
                return null;
            }
            if (MapHasLightProbes)
            {
                // A map with light probes (Phase 3.2 package A, measured on hooh.minimap.bushitsu): in Unity 6 a renderer
                // outside the probes' hull gets the ambient probe in AmbientMode.Custom / Flat / Trilight but the probes'
                // extrapolation in Skybox mode (HS2's Unity 2018 extrapolates in every mode). Characters and items stand
                // below the hull (the probes start above the floor), so the scene stays in Skybox mode: every probe-lit
                // renderer gets the map's probes as in HS2; only renderers with probes off see Unity's skybox ambient.
                HasIntended = false;
                Source = "probes";
                if (RenderSettings.ambientMode != AmbientMode.Skybox)
                {
                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    DynamicGI.UpdateEnvironment();
                }
                return null;
            }
            switch (SceneMode)
            {
                case AmbientMode.Trilight:
                    Intended = AmbientSh.Trilight(sky, equator, ground);
                    HasIntended = true;
                    Source = "colours";
                    break;
                case AmbientMode.Flat:
                    Intended = AmbientSh.Flat(sky);
                    HasIntended = true;
                    Source = "flat";
                    break;
                default:
                    if (Saved != null && Saved.Ok)
                    {
                        Intended = Saved.Probe;
                        HasIntended = true;
                        Source = "saved";
                    }
                    else
                    {
                        HasIntended = false;
                        Source = "unity";
                        if (RenderSettings.ambientMode != SceneMode) RenderSettings.ambientMode = SceneMode;
                        finding = "the map's saved ambient probe could not be read (" + (Saved?.Error ?? "no map data") + "): Unity's recomputed probe stays";
                    }
                    break;
            }
            if (HasIntended) Write();
            return finding;
        }

        void Write()
        {
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = Intended;
        }

        /// <summary>True when the active scene still holds the intended probe in Custom mode (or nothing is intended).</summary>
        public bool Holds() =>
            !HasIntended || (RenderSettings.ambientMode == AmbientMode.Custom && AmbientSh.MaxDiff(RenderSettings.ambientProbe, Intended) <= Tolerance);

        /// <summary>The LateApply guard: writes the probe again when Unity replaced it; true when it did.</summary>
        public bool Guard()
        {
            if (Holds()) return false;
            Write();
            Reasserted++;
            return true;
        }
    }
}
