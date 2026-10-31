// App rendering settings of Phase 3.3 (seam of package A): the `render` block of the
// settings file, read by AppSettings, changeable at run time by `app.options {render: {...}}` (never saved in a scene;
// reset by a restart to the file's values). Process-wide, main thread. Owners read them and subscribe to Changed:
//   aa              smaa (default, High) | taa (experimental until U9) | fxaa | none           (package S, ViewSetup)
//   taaQuality      high (default) | medium | low | veryHigh | veryLow                          (S)
//   grading         hs2 (default: LDR LUT bake with the base grade) | hdr (ACES, approximate)   (B)
//   ambient         hs2 (default: the app owns the ambient probe, E1) | environment             (S)
//   lightFalloff    hs2 (default: Built-in-compatible point / spot falloff, E5) | urp           (S)
//   shadowDistance  100 (10–400, scene units)                                                   (S)
//   backgroundColor [0, 0, 0, 1] (the view's SolidColor background; HS2's config default)       (S, ViewSetup)
//   ssr             off (default) | on: screen-space reflections, opt-in (HS2 draws them by default; HS2Next keeps the
//                   preview reflections off unless asked; drawn only while this is on and the scene's SSR flag is on)
// Apply(JObject) validates every member first and applies nothing when one is wrong (FormatException naming it).
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next
{
    public static class RenderOptions
    {
        public static readonly string[] AaValues = { "smaa", "taa", "fxaa", "none" };
        public static readonly string[] TaaQualityValues = { "veryLow", "low", "medium", "high", "veryHigh" };
        public static readonly string[] GradingValues = { "hs2", "hdr" };
        public static readonly string[] AmbientValues = { "hs2", "environment" };
        public static readonly string[] LightFalloffValues = { "hs2", "urp" };
        public static readonly string[] SsrValues = { "off", "on" };
        public static readonly string[] Keys = { "aa", "taaQuality", "grading", "ambient", "lightFalloff", "shadowDistance", "backgroundColor", "ssr" };
        public const float MinShadowDistance = 10f, MaxShadowDistance = 400f;

        public static string Aa { get; private set; } = "smaa";
        public static string TaaQuality { get; private set; } = "high";
        public static string Grading { get; private set; } = "hs2";
        public static string Ambient { get; private set; } = "hs2";
        public static string LightFalloff { get; private set; } = "hs2";
        public static float ShadowDistance { get; private set; } = 100f;
        public static Color BackgroundColor { get; private set; } = new Color(0f, 0f, 0f, 1f);
        public static string Ssr { get; private set; } = "off";

        /// <summary>Raised after an Apply that changed at least one value, with the changed keys.</summary>
        public static event Action<IReadOnlyCollection<string>> Changed;

        /// <summary>
        /// Applies the members of a `render` object (absent members keep their values). Validates all first; throws
        /// FormatException ("&lt;what&gt;.&lt;key&gt; ...") and applies nothing on a bad member. Returns the changed keys.
        /// </summary>
        public static IReadOnlyCollection<string> Apply(JObject o, string what = "render")
        {
            var changed = new List<string>();
            if (o == null) return changed;
            string aa = Aa, tq = TaaQuality, gr = Grading, am = Ambient, lf = LightFalloff, ssr = Ssr;
            float sd = ShadowDistance;
            Color bg = BackgroundColor;
            foreach (var p in o.Properties())
            {
                string w = what + "." + p.Name;
                switch (p.Name)
                {
                    case "aa": aa = Enum(p.Value, w, AaValues); break;
                    case "taaQuality": tq = Enum(p.Value, w, TaaQualityValues); break;
                    case "grading": gr = Enum(p.Value, w, GradingValues); break;
                    case "ambient": am = Enum(p.Value, w, AmbientValues); break;
                    case "lightFalloff": lf = Enum(p.Value, w, LightFalloffValues); break;
                    case "shadowDistance":
                        if (p.Value.Type != JTokenType.Integer && p.Value.Type != JTokenType.Float) throw new FormatException(w + " must be a number");
                        sd = (float)p.Value;
                        if (!(sd >= MinShadowDistance && sd <= MaxShadowDistance)) throw new FormatException(w + " must be in " + MinShadowDistance + "–" + MaxShadowDistance);
                        break;
                    case "backgroundColor": bg = ColorOf(p.Value, w); break;
                    case "ssr": ssr = Enum(p.Value, w, SsrValues); break;
                    default: throw new FormatException(what + ": unknown member \"" + p.Name + "\" (one of " + string.Join(", ", Keys) + ")");
                }
            }
            if (aa != Aa) { Aa = aa; changed.Add("aa"); }
            if (tq != TaaQuality) { TaaQuality = tq; changed.Add("taaQuality"); }
            if (gr != Grading) { Grading = gr; changed.Add("grading"); }
            if (am != Ambient) { Ambient = am; changed.Add("ambient"); }
            if (lf != LightFalloff) { LightFalloff = lf; changed.Add("lightFalloff"); }
            if (sd != ShadowDistance) { ShadowDistance = sd; changed.Add("shadowDistance"); }
            if (bg != BackgroundColor) { BackgroundColor = bg; changed.Add("backgroundColor"); }
            if (ssr != Ssr) { Ssr = ssr; changed.Add("ssr"); }
            if (changed.Count > 0)
            {
                var h = Changed;
                if (h != null)
                    foreach (Action<IReadOnlyCollection<string>> d in h.GetInvocationList())
                    {
                        try { d(changed); }
                        catch (Exception e) { Debug.LogWarning("RenderOptions: a Changed subscriber threw " + e); }
                    }
            }
            return changed;
        }

        /// <summary>The defaults (tests; a restart without a settings file).</summary>
        public static void ResetToDefaults() =>
            Apply(JObject.Parse("{\"aa\":\"smaa\",\"taaQuality\":\"high\",\"grading\":\"hs2\",\"ambient\":\"hs2\",\"lightFalloff\":\"hs2\",\"shadowDistance\":100,\"backgroundColor\":[0,0,0,1],\"ssr\":\"off\"}"));

        /// <summary>The current values as a JSON object (the `render` member of app.options' reply).</summary>
        public static JObject ToJson() => new JObject
        {
            ["aa"] = Aa,
            ["taaQuality"] = TaaQuality,
            ["grading"] = Grading,
            ["ambient"] = Ambient,
            ["lightFalloff"] = LightFalloff,
            ["shadowDistance"] = ShadowDistance,
            ["backgroundColor"] = new JArray(BackgroundColor.r, BackgroundColor.g, BackgroundColor.b, BackgroundColor.a),
            ["ssr"] = Ssr,
            ["taaExperimental"] = true,
        };

        static string Enum(JToken t, string what, string[] values)
        {
            if (t.Type == JTokenType.String)
            {
                string v = (string)t;
                if (Array.IndexOf(values, v) >= 0) return v;
            }
            throw new FormatException(what + " must be one of " + string.Join(", ", values));
        }

        static Color ColorOf(JToken t, string what)
        {
            if (t is JArray a && (a.Count == 3 || a.Count == 4))
            {
                var c = new float[4] { 0f, 0f, 0f, 1f };
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Type != JTokenType.Integer && a[i].Type != JTokenType.Float) throw new FormatException(what + " must be [r, g, b] or [r, g, b, a] numbers in 0–1");
                    c[i] = (float)a[i];
                    if (!(c[i] >= 0f && c[i] <= 1f)) throw new FormatException(what + " components must be in 0–1");
                }
                return new Color(c[0], c[1], c[2], c[3]);
            }
            throw new FormatException(what + " must be [r, g, b] or [r, g, b, a] numbers in 0–1");
        }
    }
}
