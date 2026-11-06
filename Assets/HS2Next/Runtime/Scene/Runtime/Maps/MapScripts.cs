// The map scripts HS2 Studio runs that change the look, reproduced natively (M12).
// The compat stubs carry the serialized fields and never run; this class reads them at the load and drives the effect from
// the scene runtime's frame (map time t = seconds since the map finished loading):
//   ME_LightCurves  (KriptoFX) per light: at start v = curve(0); each step t' += dt, while running v = curve(t' / timeMult)
//                   × intensityMult; then when t' >= timeMult: restart (t' = 0) when IsLoop, else stop (v stays). The light
//                   is folded: colour = original colour × v, intensity 1; the recorded original intensity becomes v (HS2
//                   writes Light.intensity). A light under an inactive object is not touched (it keeps its saved
//                   intensity) until it becomes active; becoming active (again) starts its clock (HS2's Awake / OnEnable).
//   UVScroll        _MainTex offset (repeat(t × speedX, 1), repeat(t × speedY, 1)) on the renderer's first material, which is
//                   the map's owned copy shared by every user of the bundle material (HS2 writes the shared material, Q-k);
//                   at start 0. A URP/Lit fallback gets the same offset on _BaseMap.
//   AnimatedFountainUVs  flip-book on a renderer-owned copy (HS2: renderer.material): tile i = floor(t × fps) mod (X × Y),
//                   offset ((i mod X) / X, 1 − 1/Y − floor(i / Y) / Y) (HS2's row term divides by the row count), scale
//                   (1/X, 1/Y); written when i changes.
// Everything else stays inert (MapStructure.InertScripts, finding map.scriptsInert).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class MapScripts
    {
        sealed class Curve
        {
            public ME_LightCurves Stub;
            public Light Light;
            public BundleLightOriginal Original;
            public float T;
            public bool Running = true, WasActive;
            public float Value;
        }

        sealed class Scroll
        {
            public UVScroll Stub;
            public Material Mat;
        }

        sealed class Fountain
        {
            public AnimatedFountainUVs Stub;
            public Material Mat;
            public int Last = -1;
        }

        readonly List<Curve> curves = new List<Curve>();
        readonly List<Scroll> scrolls = new List<Scroll>();
        readonly List<Fountain> fountains = new List<Fountain>();
        readonly HashSet<Material> driven = new HashSet<Material>();

        /// <summary>Map time in seconds since <see cref="Start"/>.</summary>
        public float Time { get; private set; }
        public bool Running { get; private set; }
        public int LightCurves => curves.Count;
        public int UVScrolls => scrolls.Count;
        public int FountainUVs => fountains.Count;
        /// <summary>Stubs that could not run (no Light / Renderer / material on their object).</summary>
        public int Skipped { get; private set; }

        /// <summary>Captures the stubs of the structure (after the lights were folded, post-load step 10).</summary>
        public void Start(MapStructure s, MapMaterials mats)
        {
            Stop();
            if (s == null) return;
            foreach (var c in s.LightCurves)
            {
                var l = c ? c.GetComponent<Light>() : null;
                if (!l || c.LightCurve == null) { Skipped++; continue; }
                var o = BundleLights.OriginalOf(l);
                if (!o) { BundleLights.Prepare(l.gameObject, fold: true); o = BundleLights.OriginalOf(l); }
                var e = new Curve { Stub = c, Light = l, Original = o, WasActive = c.enabled && c.gameObject.activeInHierarchy };
                // HS2's Awake: intensity = curve(0) (without the multiplier; the first Update applies it). Unity runs Awake /
                // OnEnable / Update only on active objects: a curve under an inactive object keeps the light's saved intensity
                // until the object becomes active (Step then starts it; package C's comparison, maps 32 / 37: 39 lights)
                if (e.WasActive) SetValue(e, c.LightCurve.Evaluate(0f));
                curves.Add(e);
            }
            foreach (var u in s.UVScrolls)
            {
                var r = u ? u.GetComponent<Renderer>() : null;
                var m = r ? r.sharedMaterial : null;
                if (!m) { Skipped++; continue; }
                if (mats != null && !mats.IsOwned(m))
                {
                    m = mats.CopyOf(m) ?? mats.Copy(m);
                    var ms = r.sharedMaterials;
                    ms[0] = m;
                    r.sharedMaterials = ms;
                }
                var e = new Scroll { Stub = u, Mat = m };
                SetOffset(m, Vector2.zero);
                scrolls.Add(e);
                driven.Add(m);
            }
            foreach (var f in s.FountainUVs)
            {
                var r = f ? f.GetComponent<Renderer>() : null;
                var m = r ? r.sharedMaterial : null;
                if (!m || f._uvTieX <= 0 || f._uvTieY <= 0) { Skipped++; continue; }
                // a renderer-owned copy (HS2: renderer.material makes an instance)
                var own = mats != null ? mats.Copy(m) : MaterialCopies.Copy(m);
                var ms = r.sharedMaterials;
                ms[0] = own;
                r.sharedMaterials = ms;
                fountains.Add(new Fountain { Stub = f, Mat = own });
                driven.Add(own);
            }
            Time = 0f;
            Running = true;
            Step(0f);
        }

        /// <summary>Advances the map time by <paramref name="dt"/> and drives every script.</summary>
        public void Step(float dt)
        {
            if (!Running) return;
            Time += dt;
            foreach (var e in curves)
            {
                if (!e.Stub || !e.Light) continue;
                bool active = e.Stub.enabled && e.Stub.gameObject.activeInHierarchy;
                if (!active) { e.WasActive = false; continue; }
                if (!e.WasActive) { e.WasActive = true; e.T = 0f; e.Running = true; }   // OnEnable
                else e.T += dt;
                float tm = e.Stub.GraphTimeMultiplier;
                if (e.Running) SetValue(e, CurveValue(e.Stub.LightCurve, e.T, tm, e.Stub.GraphIntensityMultiplier));
                if (e.T >= tm)
                {
                    if (e.Stub.IsLoop != 0) e.T = 0f;
                    else e.Running = false;
                }
            }
            foreach (var e in scrolls)
            {
                if (!e.Mat || !e.Stub) continue;
                SetOffset(e.Mat, ScrollOffset(Time, e.Stub.scrollSpeedX, e.Stub.scrollSpeedY));
            }
            foreach (var e in fountains)
            {
                if (!e.Mat || !e.Stub) continue;
                int x = e.Stub._uvTieX, y = e.Stub._uvTieY;
                int i = FountainTile(Time, e.Stub._fps, x, y);
                if (i == e.Last) continue;
                e.Last = i;
                var (off, scale) = FountainUv(i, x, y);
                e.Mat.SetTextureOffset("_MainTex", off);
                e.Mat.SetTextureScale("_MainTex", scale);
                if (e.Mat.HasProperty("_BaseMap")) { e.Mat.SetTextureOffset("_BaseMap", off); e.Mat.SetTextureScale("_BaseMap", scale); }
            }
        }

        /// <summary>Forgets the captured scripts (the scene is being unloaded; its objects go with it).</summary>
        public void Stop()
        {
            curves.Clear();
            scrolls.Clear();
            fountains.Clear();
            driven.Clear();
            Running = false;
            Time = 0f;
            Skipped = 0;
        }

        /// <summary>True when a script rewrites the offset or scale of this texture property of <paramref name="m"/>.</summary>
        public bool Drives(Material m, string property) => (property == "_MainTex" || property == "_BaseMap") && driven.Contains(m);

        /// <summary>The intensity values of the curve lights (bridge, tests), in structure order.</summary>
        public IEnumerable<(Light light, float value, float t, bool running)> CurveStates()
        {
            foreach (var e in curves) yield return (e.Light, e.Value, e.T, e.Running);
        }

        // ------------------------------------------------------------------ formulas

        /// <summary>ME_LightCurves: curve(t / timeMult) × intensityMult.</summary>
        public static float CurveValue(AnimationCurve c, float t, float timeMult, float intensityMult) =>
            c == null ? 0f : c.Evaluate(timeMult != 0f ? t / timeMult : 0f) * intensityMult;

        /// <summary>UVScroll: (repeat(t × sx, 1), repeat(t × sy, 1)).</summary>
        public static Vector2 ScrollOffset(float t, float sx, float sy) => new Vector2(Mathf.Repeat(t * sx, 1f), Mathf.Repeat(t * sy, 1f));

        /// <summary>AnimatedFountainUVs: the tile index at time t.</summary>
        public static int FountainTile(float t, int fps, int x, int y) => x * y <= 0 ? 0 : (int)(t * fps) % (x * y);

        /// <summary>AnimatedFountainUVs: offset and scale of tile i (HS2's formula, row = i / tilesY).</summary>
        public static (Vector2 offset, Vector2 scale) FountainUv(int i, int x, int y)
        {
            var size = new Vector2(1f / x, 1f / y);
            int col = i % x, row = i / y;
            return (new Vector2(col * size.x, 1f - size.y - row * size.y), size);
        }

        static void SetValue(Curve e, float v)
        {
            e.Value = v;
            if (e.Original) e.Original.Intensity = v;
            var c = e.Original ? e.Original.Color : e.Light.color;
            e.Light.color = BundleLights.Folded(c, v);
            e.Light.intensity = 1f;
        }

        static void SetOffset(Material m, Vector2 o)
        {
            if (m.HasProperty("_MainTex")) m.SetTextureOffset("_MainTex", o);
            if (m.HasProperty("_BaseMap")) m.SetTextureOffset("_BaseMap", o);
        }
    }
}
