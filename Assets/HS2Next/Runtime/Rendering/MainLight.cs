// The main (shadowed) directional light (decision M4). URP shadows one directional light:
// RenderSettings.sun when it is set, enabled and visible, else the brightest directional light. HS2 Studio sets the sun to
// its "Sky Dummy Light" (intensity 0) after every map load; HS2Next does not (that would make a zero-intensity light the
// shadowed main light). Rule:
//   1. candidates = enabled, active, directional lights with shadows != None that light at run time (not baked-only),
//      among the chara light, the Studio lights (Phase 3.3 registers them as a source) and the map lights (Phase 3.2);
//   2. the chara light wins when it is a candidate;
//   3. else the candidate with the largest max(original rgb) × original intensity (gamma values: BundleLightOriginal of a
//      folded light, else the light's own colour and intensity, i.e. HS2's apparent brightness); ties → the source
//      registered first (map lights before Studio lights), then the lower order within the source;
//   4. no candidate → RenderSettings.sun = null (URP takes the brightest directional light, which has no shadows to lose).
// Refresh after a map load / unload, a MapComponent block change, a chara-light change (SceneCharaLight) and Studio-light
// changes (3.3). Current / Reason are reported by the bridge (map.state, scene.lights).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next
{
    public static class MainLight
    {
        public const string ReasonChara = "chara", ReasonBrightest = "brightest", ReasonNone = "none";

        /// <summary>The chosen light (null: none).</summary>
        public static Light Current { get; private set; }
        /// <summary>Why: "chara", "brightest" or "none".</summary>
        public static string Reason { get; private set; } = ReasonNone;
        /// <summary>The chara light (SceneCharaLight sets it; null when there is none).</summary>
        public static Light CharaLight { get; private set; }

        sealed class Source
        {
            public object Owner;
            public string Kind;
            public Func<IEnumerable<Light>> Lights;
        }

        static readonly List<Source> sources = new List<Source>();

        /// <summary>Sets the chara light and refreshes.</summary>
        public static void SetCharaLight(Light l)
        {
            CharaLight = l;
            Refresh();
        }

        /// <summary>
        /// Registers a source of candidate lights (<paramref name="kind"/>: "map", "studio"); an owner registers one source
        /// (a second call replaces it and keeps its place). Sources take part in registration order (the tie rule).
        /// </summary>
        public static void SetSource(object owner, string kind, Func<IEnumerable<Light>> lights)
        {
            if (owner == null) return;
            foreach (var s in sources)
                if (ReferenceEquals(s.Owner, owner)) { s.Kind = kind; s.Lights = lights; return; }
            sources.Add(new Source { Owner = owner, Kind = kind, Lights = lights });
            // map lights before Studio lights whatever the registration order
            sources.Sort((a, b) => Rank(a.Kind).CompareTo(Rank(b.Kind)));
        }

        static int Rank(string kind) => kind == "map" ? 0 : kind == "studio" ? 1 : 2;

        public static void RemoveSource(object owner)
        {
            sources.RemoveAll(s => ReferenceEquals(s.Owner, owner));
        }

        /// <summary>A light that URP can shadow as the main light: enabled, active, directional, shadowed, lit at run time.</summary>
        public static bool IsCandidate(Light l) =>
            l && l.enabled && l.gameObject.activeInHierarchy && l.type == LightType.Directional && l.shadows != LightShadows.None && !IsBakedOnly(l);

        /// <summary>A light whose contribution is only in the lightmaps (Unity does not light with it at run time).</summary>
        public static bool IsBakedOnly(Light l) => l && l.bakingOutput.isBaked && l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked;

        /// <summary>HS2's apparent brightness: max(rgb) × intensity of the light's original (gamma) values.</summary>
        public static float Brightness(Light l)
        {
            if (!l) return 0f;
            var o = BundleLights.OriginalOf(l);
            Color c = o ? o.Color : l.color;
            float i = o ? o.Intensity : l.intensity;
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b)) * i;
        }

        /// <summary>The rule (pure): the chara light when a candidate, else the brightest candidate (first wins ties).</summary>
        public static Light Choose(Light chara, IEnumerable<Light> others, out string reason)
        {
            if (IsCandidate(chara)) { reason = ReasonChara; return chara; }
            Light best = null;
            float bestB = float.NegativeInfinity;
            if (others != null)
                foreach (var l in others)
                {
                    if (!IsCandidate(l) || l == chara) continue;
                    float b = Brightness(l);
                    if (b > bestB) { best = l; bestB = b; }
                }
            reason = best ? ReasonBrightest : ReasonNone;
            return best;
        }

        static IEnumerable<Light> All()
        {
            foreach (var s in sources.ToArray())
            {
                IEnumerable<Light> ls = null;
                try { ls = s.Lights?.Invoke(); } catch (Exception e) { Debug.LogWarning("MainLight: a light source threw " + e.Message); }
                if (ls == null) continue;
                foreach (var l in ls) yield return l;
            }
        }

        /// <summary>Applies the rule and sets RenderSettings.sun of the active scene.</summary>
        public static Light Refresh()
        {
            var l = Choose(CharaLight, All(), out var reason);
            Current = l;
            Reason = reason;
            if (RenderSettings.sun != l) RenderSettings.sun = l;
            return l;
        }

        /// <summary>Forgets the chara light and every source (tests).</summary>
        public static void Reset()
        {
            sources.Clear();
            CharaLight = null;
            Current = null;
            Reason = ReasonNone;
        }
    }
}
