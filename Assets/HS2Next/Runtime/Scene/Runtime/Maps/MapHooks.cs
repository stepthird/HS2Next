// Seams of the map realization (Phase 3.2):
//   Mirrors   set by package B (MapMirrors): called at post-load step 11 with the loaded map (its Structure lists the
//             MirrorReflection stubs with their renderers, its Materials tracks the owned copies); the returned handle is
//             disposed when the map is dropped (before the scene unload), so B releases its cameras and targets there.
//             Null (until B lands): mirrors draw their fallback.
//   Terrains  set by package B (TerrainMap): called after the materials step (4); copies go to Materials.Track.
//   Loaded    raised after the post-load pipeline (step 13, after MainLight.Refresh): the map scene is active and its
//             RenderSettings apply; Phase 3.3 re-applies the Studio-owned RenderSettings (fog, ambient colours, skybox,
//             sun rotation, clear flags) here.
//   Unloaded  raised when a dropped map's scene unload has completed and its owned materials are destroyed; the
//             runtime's own scene is active again.
// Multicast delegates: subscribe with +=. A subscriber that throws is logged (finding map.stepFailed on the realization)
// and does not stop the others.
using System;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class MapHooks
    {
        /// <summary>Package B's mirror attachment (MapMirrors); the handle is disposed at unload. Null: no mirrors.</summary>
        public static Func<MapRealization, IDisposable> Mirrors;

        /// <summary>Package B's terrain template mapping (TerrainMap): called after the materials step (4) with the map
        /// (Structure.Terrains; track copies with Materials.Track). Null: terrains keep their templates.</summary>
        public static Action<MapRealization> Terrains;

        /// <summary>After a map finished loading (post-load pipeline done; the map scene is active).</summary>
        public static Action<MapRealization> Loaded;

        /// <summary>After a map's scene unload completed (owned copies destroyed; the runtime's scene active).</summary>
        public static Action<MapRealization> Unloaded;

        internal static void Raise(Action<MapRealization> a, MapRealization m, string what)
        {
            if (a == null) return;
            foreach (Action<MapRealization> d in a.GetInvocationList())
            {
                try { d(m); }
                catch (Exception e) { Debug.LogWarning($"map: a {what} subscriber threw {e.GetType().Name}: {e.Message}\n{e}"); }
            }
        }
    }
}
