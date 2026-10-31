// The structure of a loaded map scene, built once per load (post-load step 2):
// the roots in GetRootGameObjects order (HS2's "first root" is element 0), one pass over every component of every root
// (inactive objects included) sorting out renderers, lights, terrains, reflection probes, cameras, audio listeners and
// sources, the map scripts HS2 runs (ME_LightCurves, UVScroll, AnimatedFountainUVs, MirrorReflection), the lightmap data
// components (LightmapPrefab, Bakery's ftLightmapsStorage), the MonoBehaviours that stay inert (counted by class) and the
// missing scripts (null components: mod scripts without a stub, PPv2 volumes among them).
// MapComponent lookup (M2): the first active MapComponent in roots order (depth-first, active objects only, as Unity's
// GetComponentInChildren), HS2's rule extended from root 0 to every root; ComponentRoot says which root holds it.
// Paths (bridge map.state / map.renderers): root-relative names joined by '/', starting with the root's name; a name
// that occurs more than once among its siblings (or among the scene roots) gets "[k]" appended (k-th occurrence, from 0),
// on every occurrence. The same walk on HS2's side gives the same paths for the same map.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class MapStructure
    {
        /// <summary>A MirrorReflection stub with the renderer on its object (null when it has none).</summary>
        public readonly struct Mirror
        {
            public readonly MirrorReflection Stub;
            public readonly Renderer Renderer;
            public Mirror(MirrorReflection stub, Renderer renderer) { Stub = stub; Renderer = renderer; }
        }

        public GameObject[] Roots { get; private set; } = Array.Empty<GameObject>();
        /// <summary>Every renderer under the roots (inactive included), roots in order, depth-first.</summary>
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly List<Light> Lights = new List<Light>();
        public readonly List<Terrain> Terrains = new List<Terrain>();
        public readonly List<ReflectionProbe> ReflectionProbes = new List<ReflectionProbe>();
        public readonly List<Camera> Cameras = new List<Camera>();
        public readonly List<AudioListener> Listeners = new List<AudioListener>();
        public readonly List<AudioSource> AudioSources = new List<AudioSource>();
        public readonly List<Mirror> Mirrors = new List<Mirror>();
        public readonly List<ME_LightCurves> LightCurves = new List<ME_LightCurves>();
        public readonly List<UVScroll> UVScrolls = new List<UVScroll>();
        public readonly List<AnimatedFountainUVs> FountainUVs = new List<AnimatedFountainUVs>();
        public readonly List<LightmapPrefab> LightmapPrefabs = new List<LightmapPrefab>();
        public readonly List<ftLightmapsStorage> BakeryStorages = new List<ftLightmapsStorage>();
        public readonly List<Studio.MapComponent> AllComponents = new List<Studio.MapComponent>();
        /// <summary>MonoBehaviours HS2Next does not run, by class name (namespace-qualified), sorted.</summary>
        public readonly SortedDictionary<string, int> InertScripts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Every MonoBehaviour of the scene as loaded (before HS2Next adds its own components), by full class name;
        /// missing scripts as "(missing script)" (bridge map.state counts.scripts, compared with HS2).</summary>
        public readonly SortedDictionary<string, int> Scripts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Components whose script is missing (no stub; PPv2 volumes are among them).</summary>
        public int MissingScripts { get; private set; }
        public int GameObjects { get; private set; }

        /// <summary>The MapComponent by rule M2, or null.</summary>
        public Studio.MapComponent Component { get; private set; }
        /// <summary>Index of the root that holds <see cref="Component"/> (−1 none).</summary>
        public int ComponentRoot { get; private set; } = -1;
        /// <summary>Non-null sea renderers of the component (renderersSea).</summary>
        public readonly List<Renderer> SeaRenderers = new List<Renderer>();

        Dictionary<Transform, string> paths;

        /// <summary>Classes the map realization handles itself (not reported as inert).</summary>
        static readonly HashSet<Type> Handled = new HashSet<Type>
        {
            typeof(Studio.MapComponent), typeof(ME_LightCurves), typeof(UVScroll), typeof(AnimatedFountainUVs), typeof(MirrorReflection),
            typeof(LightmapPrefab), typeof(ftLightmapsStorage), typeof(BundleLightOriginal),
        };

        public static MapStructure Build(GameObject[] roots)
        {
            var s = new MapStructure { Roots = roots ?? Array.Empty<GameObject>() };
            var comps = new List<Component>();
            foreach (var root in s.Roots)
            {
                if (!root) continue;
                root.GetComponentsInChildren(true, comps);
                foreach (var c in comps)
                {
                    if (c is MonoBehaviour any) Bump(s.Scripts, FullName(any.GetType()));
                    switch (c)
                    {
                        case null: s.MissingScripts++; Bump(s.Scripts, MissingScript); break;
                        case Transform _: s.GameObjects++; break;
                        case Renderer r: s.Renderers.Add(r); break;
                        case Light l: s.Lights.Add(l); break;
                        case Terrain t: s.Terrains.Add(t); break;
                        case ReflectionProbe p: s.ReflectionProbes.Add(p); break;
                        case Camera cam: s.Cameras.Add(cam); break;
                        case AudioListener al: s.Listeners.Add(al); break;
                        case AudioSource a: s.AudioSources.Add(a); break;
                        case MirrorReflection m: s.Mirrors.Add(new Mirror(m, m.GetComponent<Renderer>())); break;
                        case ME_LightCurves lc: s.LightCurves.Add(lc); break;
                        case UVScroll u: s.UVScrolls.Add(u); break;
                        case AnimatedFountainUVs f: s.FountainUVs.Add(f); break;
                        case LightmapPrefab lp: s.LightmapPrefabs.Add(lp); break;
                        case ftLightmapsStorage st: s.BakeryStorages.Add(st); break;
                        case Studio.MapComponent mc: s.AllComponents.Add(mc); break;
                        case MonoBehaviour mb:
                        {
                            var t = mb.GetType();
                            if (Handled.Contains(t)) break;
                            string ns = t.Namespace ?? "";
                            if (ns.StartsWith("HS2Next", StringComparison.Ordinal) || IsUnityAssembly(t)) break;
                            string name = ns.Length > 0 ? ns + "." + t.Name : t.Name;
                            s.InertScripts.TryGetValue(name, out int n);
                            s.InertScripts[name] = n + 1;
                            break;
                        }
                    }
                }
            }
            s.Component = FindComponent(s.Roots, out int ri);
            s.ComponentRoot = ri;
            if (s.Component && s.Component.renderersSea != null)
                foreach (var r in s.Component.renderersSea)
                    if (r) s.SeaRenderers.Add(r);
            return s;
        }

        public const string MissingScript = "(missing script)";

        static void Bump(SortedDictionary<string, int> d, string k)
        {
            d.TryGetValue(k, out int n);
            d[k] = n + 1;
        }

        static string FullName(Type t) => t.FullName ?? t.Name;

        static readonly Dictionary<System.Reflection.Assembly, bool> unityAsm = new Dictionary<System.Reflection.Assembly, bool>();

        /// <summary>True for Unity's own assemblies (engine modules, URP): their components are not map scripts.</summary>
        static bool IsUnityAssembly(Type t)
        {
            var a = t.Assembly;
            if (!unityAsm.TryGetValue(a, out bool u))
            {
                string n = a.GetName().Name;
                unityAsm[a] = u = n.StartsWith("UnityEngine", StringComparison.Ordinal) || n.StartsWith("Unity.", StringComparison.Ordinal);
            }
            return u;
        }

        /// <summary>Rule M2: the first active MapComponent in roots order (active objects only), else null.</summary>
        public static Studio.MapComponent FindComponent(IReadOnlyList<GameObject> roots, out int rootIndex)
        {
            rootIndex = -1;
            if (roots == null) return null;
            for (int i = 0; i < roots.Count; i++)
            {
                if (!roots[i] || !roots[i].activeInHierarchy) continue;
                // depth-first, active objects only (Unity's GetComponentInChildren returns a component of an inactive
                // root in edit mode: filter explicitly)
                foreach (var c in roots[i].GetComponentsInChildren<Studio.MapComponent>(true))
                    if (c && c.gameObject.activeInHierarchy) { rootIndex = i; return c; }
            }
            return null;
        }

        /// <summary>Number of renderers that are part of a static batch.</summary>
        public int StaticBatched
        {
            get
            {
                int n = 0;
                foreach (var r in Renderers) if (r && r.isPartOfStaticBatch) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------ paths

        /// <summary>The name with its occurrence among same-named siblings ("[k]" only when the name repeats).</summary>
        public static string Occurrence(string name, int k, int count) => count > 1 ? name + "[" + k + "]" : name;

        /// <summary>
        /// Visits every transform under the roots in depth-first pre-order (roots in order) with its path.
        /// </summary>
        public void Walk(Action<Transform, string> visit) => Walk(Roots, visit);

        public static void Walk(IReadOnlyList<GameObject> roots, Action<Transform, string> visit)
        {
            if (roots == null) return;
            var ts = new List<Transform>(roots.Count);
            foreach (var g in roots) ts.Add(g ? g.transform : null);
            WalkLevel(ts, null, visit);
        }

        static void WalkLevel(List<Transform> level, string prefix, Action<Transform, string> visit)
        {
            Dictionary<string, int> counts = null, seen = null;
            if (level.Count > 1)
            {
                counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var t in level)
                {
                    if (!t) continue;
                    counts.TryGetValue(t.name, out int n);
                    counts[t.name] = n + 1;
                }
                seen = new Dictionary<string, int>(StringComparer.Ordinal);
            }
            foreach (var t in level)
            {
                if (!t) continue;
                string seg = t.name;
                if (counts != null && counts[seg] > 1)
                {
                    seen.TryGetValue(seg, out int k);
                    seen[seg] = k + 1;
                    seg = Occurrence(seg, k, 2);
                }
                string path = prefix == null ? seg : prefix + "/" + seg;
                visit(t, path);
                int cc = t.childCount;
                if (cc == 0) continue;
                var kids = new List<Transform>(cc);
                for (int i = 0; i < cc; i++) kids.Add(t.GetChild(i));
                WalkLevel(kids, path, visit);
            }
        }

        /// <summary>The path of a transform of this map, or its plain hierarchy path when it is not under a root.</summary>
        public string PathOf(Transform t)
        {
            if (!t) return null;
            if (paths == null)
            {
                paths = new Dictionary<Transform, string>();
                Walk((x, p) => paths[x] = p);
            }
            if (paths.TryGetValue(t, out var s)) return s;
            var sb = new StringBuilder(t.name);
            for (var x = t.parent; x; x = x.parent) sb.Insert(0, x.name + "/");
            return sb.ToString();
        }

        /// <summary>Forgets the cached paths (after objects were added or renamed).</summary>
        public void InvalidatePaths() => paths = null;
    }
}
