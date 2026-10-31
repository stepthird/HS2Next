// The owned material copies of a loaded map (post-load steps 4 and 6):
//   Apply     every distinct bundle material on a renderer of the map (inactive included) gets one tracked copy (copies stay
//             per material, not per renderer: UVScroll's shared-material write then scrolls all its users, HS2 quirk Q-k),
//             then ShaderMap.ApplyUrp(root, copy: false) swaps the copies to their URP replacements (and gives the renderers
//             their rendering layers, RenderingLayers.ApplyToRenderers).
//   Adopt     materials other steps put on a renderer (ShaderMap.RouteLightmapped's URP/Lit copies) become tracked too.
//   Copy / Track  for the sea, the map scripts and package B (mirror copies, terrain templates, render targets).
//   Skybox    RenderSettings.skybox (a bundle material) → a tracked copy swapped through ShaderMap.Fitted when an entry
//             exists for its shader (package B's Skybox/* entries); called once the map scene is active.
//   Release   destroys every tracked object; called after the scene unload completed (so no renderer still uses them).
// Before 3.2 ShaderMap.ApplyUrp(root) copied and never destroyed (one leak per map load). For the bridge each copy keeps
// its bundle original's shader name and the keywords it had at the load (sorted).
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class MapMaterials
    {
        /// <summary>What a copy came from.</summary>
        public sealed class Origin
        {
            public Material Source;
            public Shader SourceShader;
            public string ShaderOriginal;
            public string[] Keywords;   // sorted, as loaded
        }

        readonly List<Object> owned = new List<Object>();
        readonly HashSet<Object> ownedSet = new HashSet<Object>();
        readonly Dictionary<Material, Material> copyOf = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Origin> origins = new Dictionary<Material, Origin>();

        /// <summary>Tracked objects (materials and whatever else was handed to <see cref="Track"/>).</summary>
        public int Count => owned.Count;
        /// <summary>Distinct bundle materials copied by <see cref="Apply"/>.</summary>
        public int Copied { get; private set; }
        /// <summary>Copies ShaderMap swapped (fitted or the URP/Lit fallback).</summary>
        public int Mapped { get; private set; }
        /// <summary>The skybox copy (null when the map scene has none or no ShaderMap entry exists for it).</summary>
        public Material Skybox { get; private set; }
        public string SkyboxShaderOriginal { get; private set; }

        public bool IsOwned(Object o) => o && ownedSet.Contains(o);

        /// <summary>Tracks <paramref name="o"/>: destroyed at <see cref="Release"/>.</summary>
        public T Track<T>(T o) where T : Object
        {
            if (o && ownedSet.Add(o)) owned.Add(o);
            return o;
        }

        /// <summary>A tracked copy of <paramref name="source"/> (keywords as loaded, MaterialCopies.Copy), with its origin recorded.</summary>
        public Material Copy(Material source)
        {
            if (!source) return null;
            var o = OriginOf(source) ?? new Origin { Source = source, SourceShader = source.shader, ShaderOriginal = source.shader ? source.shader.name : null, Keywords = Sorted(MaterialCopies.KeywordsOf(source)) };
            var c = MaterialCopies.Copy(source);
            origins[c] = o;
            return Track(c);
        }

        /// <summary>The origin of a tracked copy (or of a copy of a copy), else null.</summary>
        public Origin OriginOf(Material m) => m && origins.TryGetValue(m, out var o) ? o : null;

        /// <summary>The copy <see cref="Apply"/> made of a bundle material, else null.</summary>
        public Material CopyOf(Material bundleMaterial) => bundleMaterial && copyOf.TryGetValue(bundleMaterial, out var c) ? c : null;

        static string[] Sorted(string[] k)
        {
            if (k == null) return Array.Empty<string>();
            var s = (string[])k.Clone();
            Array.Sort(s, StringComparer.Ordinal);
            return s;
        }

        /// <summary>Copies (once per distinct material) and maps every material of the renderers under <paramref name="roots"/>;
        /// returns the copies ShaderMap swapped.</summary>
        public int Apply(IReadOnlyList<GameObject> roots)
        {
            if (roots == null) return 0;
            foreach (var root in roots)
            {
                if (!root) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r) continue;
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (!m || ownedSet.Contains(m)) continue;
                        if (!copyOf.TryGetValue(m, out var c))
                        {
                            c = Copy(m);
                            copyOf[m] = c;
                            Copied++;
                        }
                        mats[i] = c;
                        changed = true;
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            int n = 0;
            foreach (var root in roots)
                if (root) n += ShaderMap.ApplyUrp(root, copy: false);
            Mapped += n;
            return n;
        }

        /// <summary>ShaderMap.RouteLightmapped on <paramref name="r"/> with its new URP/Lit copies tracked (origin = the slot's
        /// previous material's origin). Returns the slots moved.</summary>
        public int Route(Renderer r)
        {
            if (!r) return 0;
            var before = r.sharedMaterials;
            int n = ShaderMap.RouteLightmapped(r, m => OriginOf(m)?.SourceShader);   // originals without LIGHTMAP_ON keep their slot (3.7 L)
            if (n == 0) return 0;
            var after = r.sharedMaterials;
            for (int i = 0; i < after.Length && i < before.Length; i++)
            {
                if (!after[i] || after[i] == before[i] || ownedSet.Contains(after[i])) continue;
                Track(after[i]);
                var o = OriginOf(before[i]);
                if (o != null) origins[after[i]] = o;
            }
            return n;
        }

        /// <summary>Tracks the materials on <paramref name="r"/> that are not tracked yet (copies made by other steps).
        /// Returns how many were adopted.</summary>
        public int Adopt(Renderer r)
        {
            if (!r) return 0;
            int n = 0;
            foreach (var m in r.sharedMaterials)
            {
                if (!m || ownedSet.Contains(m) || copyOf.ContainsKey(m)) continue;
                // a material that is no bundle material: a copy another step made from one of ours
                Track(m);
                n++;
            }
            return n;
        }

        /// <summary>
        /// RenderSettings.skybox of the active (map) scene → a tracked copy swapped through ShaderMap.Fitted, when an entry
        /// exists for its shader; otherwise the skybox stays as the scene brings it (reported by its shader name).
        /// </summary>
        public void ApplySkybox()
        {
            var sk = RenderSettings.skybox;
            SkyboxShaderOriginal = sk && sk.shader ? sk.shader.name : null;
            if (!sk || ownedSet.Contains(sk)) return;
            if (SkyboxShaderOriginal == null || !ShaderMap.TryResolve(sk.shader, out var target)) return;
            var s = Shader.Find(target);
            if (!s) return;
            var c = Copy(sk);
            ShaderMap.Swap(c, s);
            RenderSettings.skybox = c;
            Skybox = c;
        }

        /// <summary>True when the slot still renders with the bundle's own (Built-in) shader: the copy was not swapped.</summary>
        public bool IsBuiltIn(Material m)
        {
            if (!m || !m.shader) return false;
            string n = m.shader.name;
            if (n.StartsWith("HS2Next/", StringComparison.Ordinal) || n.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)) return false;
            var o = OriginOf(m);
            return o == null ? !ShaderMap.IsReplacementName(n) : m.shader == o.SourceShader;
        }

        /// <summary>Destroys every tracked object (call after the scene unload completed).</summary>
        public void Release()
        {
            foreach (var o in owned)
            {
                if (!o) continue;
                if (Application.isPlaying) Object.Destroy(o);
                else Object.DestroyImmediate(o);
            }
            owned.Clear();
            ownedSet.Clear();
            copyOf.Clear();
            origins.Clear();
            Skybox = null;
            Copied = Mapped = 0;
        }
    }
}
