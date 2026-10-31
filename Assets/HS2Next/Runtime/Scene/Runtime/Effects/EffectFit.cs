// Development overrides of the fitted effect constants (Phase 3.3 package B, the fit against package H's captures):
// in the Editor only, an optional JSON file <cache>/effects/effects_fit.json ({"ao.radius": 3, "bloom.scale": 0.5, ...})
// replaces a named constant; it is re-read when its write time changes, so a running Editor (BridgeServe) picks a new value
// up at the next application of the block. Players never read it (the shipped constants apply).
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Runtime
{
    public static class EffectFit
    {
#if UNITY_EDITOR
        static DateTime stamp;
        static Dictionary<string, float> values = new Dictionary<string, float>(StringComparer.Ordinal);

        static void Refresh()
        {
            try
            {
                string p = Path.Combine(HS2Next.Chara.TextureGuard.CacheRoot, "effects", "effects_fit.json");
                if (!File.Exists(p)) { if (values.Count > 0) values.Clear(); stamp = default; return; }
                var t = File.GetLastWriteTimeUtc(p);
                if (t == stamp) return;
                stamp = t;
                var d = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var kv in JObject.Parse(File.ReadAllText(p))) d[kv.Key] = (float)kv.Value;
                values = d;
                UnityEngine.Debug.Log("effects: fit overrides " + string.Join(", ", d));
            }
            catch (Exception e) when (e is IOException || e is Newtonsoft.Json.JsonException || e is FormatException || e is ArgumentException) { }
        }
#endif

        /// <summary>The override of <paramref name="name"/> (Editor, effects_fit.json), else <paramref name="value"/>.</summary>
        public static float Get(string name, float value)
        {
#if UNITY_EDITOR
            Refresh();
            if (values.TryGetValue(name, out var v)) return v;
#endif
            return value;
        }
    }
}
