// Lightmaps and light probes of a loaded map (post-load step 6). Unity's additive scene load
// merges the scene's LightmapSettings lightmaps and probes into the global set and offsets its renderers' lightmap indices;
// nothing is copied here.
//   1. LightmapPrefab (HS2's Start → Resetup, active components only, HS2's rule): for each entry with a renderer and
//      lightmapIndex >= 0, the renderer's lightmapScaleOffset and lightmapIndex (+ the scene's first lightmap index in the
//      merged set); entries with an index >= the scene's lightmap count or without a renderer are skipped and counted
//      (map.lightmapPrefabDead). No entry is effective on the reference install (vanilla: null renderers; mods: no entries).
//   2. ShaderMap.RouteLightmapped (package S) for every lightmapped renderer (MapMaterials.Route: the URP/Lit copies are
//      tracked; one copy per routed material, shared by the renderers that used it).
//   2b. Bakery renderer data: renderers a storage lists (bakedRenderers / bakedIDs / bakedScaleOffset) that carry no lightmap at
//      run time get it from the storage, as Bakery's runtime does in HS2 (e.g. hooh.map.resdstreet#0: 977 renderers).
//   3. Bakery: per ftLightmapsStorage, storage maps that are not in LightmapSettings → map.bakeryOnlyLightmaps (warning; the
//      map renders without them, M5: the fallback is deferred); RNM / SH modes or volumes → map.bakeryMode (info).
//   4. LightProbes.TetrahedralizeAsync() (also after every unload, MapRealization).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class MapLightmaps
    {
        public struct Result
        {
            /// <summary>Lightmaps in the merged set after the load, the scene's share and the first index of it.</summary>
            public int LightmapsTotal, SceneLightmaps, FirstIndex;
            public LightmapsMode Mode;
            public int LightmappedRenderers, RoutedSlots;
            public int PrefabComponents, PrefabEntries, PrefabApplied, PrefabDead;
            public int BakeryStorages, BakeryStorageMaps, BakeryOnlyMaps, BakeryModeStorages;
            /// <summary>Renderers given their lightmap from a Bakery storage (bakedRenderers / bakedIDs); entries whose map is not in
            /// LightmapSettings.</summary>
            public int BakeryApplied, BakeryUnresolved;
            public int Probes;
        }

        /// <param name="lightmapsBefore">LightmapSettings.lightmaps.Length when the load started (used when no renderer is lightmapped).</param>
        public static Result Apply(MapStructure s, MapMaterials mats, int lightmapsBefore)
        {
            var r = new Result();
            var lms = LightmapSettings.lightmaps;
            r.LightmapsTotal = lms.Length;
            r.Mode = LightmapSettings.lightmapsMode;
            // The scene's range in the merged set (V4): the map is the scene loaded last, so its lightmaps are the tail of the
            // set; the first index is the smaller of the count at the request (clamped; it is stale-high when a previous map's
            // asynchronous unload completed between the request and the arrival) and the smallest lightmap index of its
            // renderers (too high when the scene's first lightmaps serve no renderer).
            int min = int.MaxValue;
            if (s != null)
                foreach (var rd in s.Renderers)
                    if (ShaderMap.IsLightmapped(rd) && rd.lightmapIndex < min) min = rd.lightmapIndex;
            // the lowest lightmaps of a scene may be used by no renderer (lightmapped terrains, LightmapPrefab data, unused
            // slots): the count at the request bounds it from below when that count is not stale-high
            int atRequest = Mathf.Clamp(lightmapsBefore, 0, lms.Length);
            r.FirstIndex = min != int.MaxValue ? Mathf.Min(min, atRequest) : atRequest;
            r.SceneLightmaps = Mathf.Max(0, lms.Length - r.FirstIndex);
            if (s == null) return r;

            // 1. LightmapPrefab
            foreach (var lp in s.LightmapPrefabs)
            {
                if (!lp) continue;
                r.PrefabComponents++;
                if (lp.lightmapParameters == null) continue;
                bool active = lp.enabled && lp.gameObject.activeInHierarchy;
                foreach (var e in lp.lightmapParameters)
                {
                    if (e == null) continue;
                    r.PrefabEntries++;
                    if (!active) continue;   // HS2: Start runs only for active components
                    if (!e.renderer || e.lightmapIndex < 0 || e.lightmapIndex >= r.SceneLightmaps) { r.PrefabDead++; continue; }
                    e.renderer.lightmapScaleOffset = e.scaleOffset;
                    e.renderer.lightmapIndex = e.lightmapIndex + r.FirstIndex;
                    r.PrefabApplied++;
                }
            }

            // 1b. Bakery's renderer data (its runtime ftLightmapsStorage applies it in HS2 when the scene does not carry it through
            // the lighting data): a renderer without a lightmap that the storage lists gets the global index of the storage map
            // bakedIDs[i] names (found by texture in LightmapSettings) and bakedScaleOffset[i]
            var globalOf = new Dictionary<Texture2D, int>();
            for (int i = 0; i < lms.Length; i++)
                if (lms[i] != null && lms[i].lightmapColor && !globalOf.ContainsKey(lms[i].lightmapColor)) globalOf[lms[i].lightmapColor] = i;
            foreach (var st in s.BakeryStorages)
            {
                if (!st || st.bakedRenderers == null || st.bakedIDs == null || st.maps == null) continue;
                for (int i = 0; i < st.bakedRenderers.Count && i < st.bakedIDs.Count; i++)
                {
                    var rd = st.bakedRenderers[i];
                    int id = st.bakedIDs[i];
                    if (!rd || id < 0 || id >= st.maps.Count || ShaderMap.IsLightmapped(rd)) continue;
                    var t = st.maps[id];
                    if (!t || !globalOf.TryGetValue(t, out int g)) { r.BakeryUnresolved++; continue; }
                    rd.lightmapIndex = g;
                    if (st.bakedScaleOffset != null && i < st.bakedScaleOffset.Count) rd.lightmapScaleOffset = st.bakedScaleOffset[i];
                    r.BakeryApplied++;
                }
            }

            // 2. routing (one URP/Lit copy per routed material)
            var routed = new Dictionary<Material, Material>();
            foreach (var rd in s.Renderers)
            {
                if (!ShaderMap.IsLightmapped(rd)) continue;
                r.LightmappedRenderers++;
                var ms = rd.sharedMaterials;
                bool pre = false;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] && routed.TryGetValue(ms[i], out var c)) { ms[i] = c; pre = true; r.RoutedSlots++; }
                if (pre) rd.sharedMaterials = ms;
                var before = rd.sharedMaterials;
                int n = mats != null ? mats.Route(rd) : ShaderMap.RouteLightmapped(rd);
                if (n == 0) continue;
                r.RoutedSlots += n;
                var after = rd.sharedMaterials;
                for (int i = 0; i < after.Length && i < before.Length; i++)
                    if (before[i] && after[i] != before[i]) routed[before[i]] = after[i];
            }

            // 3. Bakery
            var inSettings = new HashSet<Texture2D>();
            foreach (var d in lms) if (d != null && d.lightmapColor) inSettings.Add(d.lightmapColor);
            foreach (var st in s.BakeryStorages)
            {
                if (!st) continue;
                r.BakeryStorages++;
                int maps = 0, missing = 0;
                if (st.maps != null)
                    foreach (var t in st.maps)
                    {
                        if (!t) continue;
                        maps++;
                        if (!inSettings.Contains(t)) missing++;
                    }
                r.BakeryStorageMaps += maps;
                if (maps > 0 && missing == maps) r.BakeryOnlyMaps += maps;
                bool mode = st.anyVolumes != 0 || Any(st.rnmMaps0) || Any(st.rnmMaps1) || Any(st.rnmMaps2);
                if (st.mapsMode != null) foreach (var m in st.mapsMode) if (m >= 3) mode = true;
                if (mode) r.BakeryModeStorages++;
            }

            // 4. probes
            // the scene's own probes (LightmapSettings.lightProbes shows them only once the map scene is the active one)
            var lp2 = s.Roots.Length > 0 && s.Roots[0] ? LightProbes.GetSharedLightProbesForScene(s.Roots[0].scene) : null;
            r.Probes = lp2 != null ? lp2.count : 0;
            return r;
        }

        static bool Any(List<Texture2D> l)
        {
            if (l == null) return false;
            foreach (var t in l) if (t) return true;
            return false;
        }
    }
}
