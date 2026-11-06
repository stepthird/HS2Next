// The shaders a material edit can pick: the replacements of ShaderMap whose name starts with
// HS2Next/, without the mosaic shaders, the hidden and UI ones, and the ones that need inputs only a hook fills. An item stores the
// picked name itself. The list is computed once, on first use, from what Shader.Find returns; a graphics device must be able to
// run the shader, and a run without one takes what it finds.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Chara.Mosaic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next
{
    public static class MaterialShaders
    {
        public const string Prefix = "HS2Next/";

        /// <summary>One shader of the list.</summary>
        public sealed class Candidate
        {
            /// <summary>The shader's name, as an item stores it.</summary>
            public string Name;
            /// <summary>The name without HS2Next/.</summary>
            public string Label;
            /// <summary>The compatible family's label, else "HS2Next".</summary>
            public string Family;
            /// <summary>The shader's default render queue.</summary>
            public int Queue;
            /// <summary>The local keywords it declares, in the shader's order.</summary>
            public string[] Keywords;
            public Shader Shader;
        }

        /// <summary>Shaders that sample inputs only a hook fills (water, mirror reflections, the opaque texture).</summary>
        public static readonly string[] NeedsHook =
        {
            ShaderMap.WaterShader, "HS2Next/Compatible/FX/MirrorReflection*", "HS2Next/Compatible/KriptoFX/ME/Distortion",
        };

        static List<Candidate> all;
        static Dictionary<string, Candidate> byName;

        /// <summary>The list, ordered by name.</summary>
        public static IReadOnlyList<Candidate> All { get { Build(); return all; } }

        /// <summary>The names <see cref="Build"/> saw and left out, by reason; for the report of the list.</summary>
        public static readonly SortedDictionary<string, string> LeftOut = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>A name under HS2Next/: stored as the shader itself, not resolved through the shader map.</summary>
        public static bool IsDirectName(string name) => name != null && name.StartsWith(Prefix, StringComparison.Ordinal);

        public static bool IsCandidate(string name) { Build(); return name != null && byName.ContainsKey(name); }

        /// <summary>The entry of a name, or null.</summary>
        public static Candidate Find(string name) { Build(); return name != null && byName.TryGetValue(name, out var c) ? c : null; }

        /// <summary>Forgets the list (the shader map's supplies changed; tests).</summary>
        public static void Reset() { all = null; byName = null; LeftOut.Clear(); }

        static bool Hooked(string name)
        {
            foreach (var n in NeedsHook)
                if (n.EndsWith("*", StringComparison.Ordinal) ? name.StartsWith(n.Substring(0, n.Length - 1), StringComparison.Ordinal) : name == n) return true;
            return false;
        }

        static void Build()
        {
            if (all != null) return;
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var v in ShaderMap.Fitted.Values) if (v != null) names.Add(v);
            foreach (var l in ShaderMap.CopyRoutes.Values) foreach (var (_, replacement) in l) if (replacement != null) names.Add(replacement);
            var list = new List<Candidate>();
            bool device = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            foreach (var name in names)
            {
                string why = null;
                if (!IsDirectName(name)) continue;
                if (MosaicSet.IsMosaicShaderName(name)) why = "mosaic";
                else if (name.StartsWith("HS2Next/Hidden/", StringComparison.Ordinal) || name.StartsWith("HS2Next/UI/", StringComparison.Ordinal)) why = "hidden";
                else if (Hooked(name)) why = "hook";
                var sh = why == null ? Shader.Find(name) : null;
                if (why == null && !sh) why = "not found";
                if (why == null && device && !sh.isSupported) why = "not supported";
                if (why != null) { LeftOut[name] = why; continue; }
                var family = ShaderMap.CompatibleFamiliesOf(name);
                list.Add(new Candidate
                {
                    Name = name, Label = name.Substring(Prefix.Length), Family = family.Count > 0 ? family[0] : "HS2Next", Queue = sh.renderQueue, Shader = sh,
                    Keywords = sh.keywordSpace.keywords.Where(k => !k.isOverridable).Select(k => k.name).ToArray(),
                });
            }
            all = list;
            byName = list.ToDictionary(c => c.Name, StringComparer.Ordinal);
        }
    }
}
