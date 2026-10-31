// Terrain material templates of map terrains (package B of Phase 3.2): a Unity Terrain draws
// with Terrain.materialTemplate; HS2's mod maps carry Built-in templates (Nature/Terrain/Standard, Nature/Terrain/Diffuse,
// Nature/Terrain/Specular, custom ones on Standard) or none (Built-in's default terrain material). URP draws terrains only
// with its own terrain shader, so every terrain gets a tracked copy of its template on "Universal Render Pipeline/Terrain/Lit"
// (splat layers, heightmap, holes are terrain data and need no conversion [D]); a template with a fitted ShaderMap entry
// that is not a terrain shader keeps that entry instead (a custom template on a plain surface shader draws the whole terrain
// with one material, as in Built-in). The terrain's tree prototypes and detail-mesh prototypes are bundle prefabs the terrain
// draws with their own materials (no renderer slot of the scene): their materials get tracked copies mapped through ShaderMap
// (MapMaterials.Apply on the prefabs, in memory only), then the terrains refresh their prototypes. Grass-texture details
// draw with URP's own detail shaders.
// Called through MapHooks.Terrains after the materials step (4) of the map post-load pipeline.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class TerrainMap
    {
        public const string TerrainLit = "Universal Render Pipeline/Terrain/Lit";

        /// <summary>Terrains converted by the last call (reports).</summary>
        public static int LastConverted { get; private set; }

        public static void Apply(MapRealization map)
        {
            LastConverted = 0; LastPrototypes = 0;
            var s = map?.Structure;
            if (s == null) return;
            var lit = Shader.Find(TerrainLit);
            foreach (var t in s.Terrains)
            {
                if (!t) continue;
                var tmpl = t.materialTemplate;
                if (tmpl && tmpl.shader && IsUrp(tmpl.shader.name)) continue;
                Material c;
                if (tmpl && tmpl.shader && !IsTerrainShader(tmpl.shader.name) && ShaderMap.TryResolve(tmpl.shader, out _))
                {
                    c = map.Materials.Copy(tmpl);
                    MapShaderHooks.MapOne(c);
                }
                else
                {
                    if (!lit) continue;
                    c = map.Materials.Track(new Material(lit) { name = (tmpl ? tmpl.name : "terrain") + " (URP Terrain Lit)" });
                    if (tmpl)
                    {
                        // the Built-in Standard terrain's metallic / smoothness defaults have no URP counterpart: layers carry them
                        if (tmpl.HasProperty("_Color") && c.HasProperty("_BaseColor")) c.SetColor("_BaseColor", tmpl.GetColor("_Color"));
                    }
                    // per-pixel normals from the layers' normal maps when any layer has one (as the Built-in Standard terrain)
                    bool normals = false;
                    var data = t.terrainData;
                    if (data)
                        foreach (var l in data.terrainLayers)
                            if (l && l.normalMapTexture) { normals = true; break; }
                    if (normals) c.EnableKeyword("_NORMALMAP");
                }
                t.materialTemplate = c;
                LastConverted++;
            }
            // tree and detail-mesh prototypes: prefabs of the bundle drawn by the terrain with their own materials (not renderer
            // slots of the scene): their materials get tracked copies mapped like the scene's (MapMaterials.Apply on the
            // prototype prefabs, in memory only; the bundle's assets go with the map)
            var protos = new List<GameObject>();
            foreach (var t in s.Terrains)
            {
                var data = t ? t.terrainData : null;
                if (!data) continue;
                foreach (var tp in data.treePrototypes) if (tp != null && tp.prefab && !protos.Contains(tp.prefab)) protos.Add(tp.prefab);
                foreach (var dp in data.detailPrototypes) if (dp != null && dp.usePrototypeMesh && dp.prototype && !protos.Contains(dp.prototype)) protos.Add(dp.prototype);
            }
            if (protos.Count > 0)
            {
                LastPrototypes = protos.Count;
                map.Materials.Apply(protos);
                foreach (var t in s.Terrains)
                    if (t && t.terrainData) { t.terrainData.RefreshPrototypes(); t.Flush(); }
            }
        }

        /// <summary>Tree / detail-mesh prototypes mapped by the last call.</summary>
        public static int LastPrototypes { get; private set; }

        static bool IsUrp(string n) => n.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) || n.StartsWith("HS2Next/", StringComparison.Ordinal);

        static bool IsTerrainShader(string n) => n.StartsWith("Nature/Terrain/", StringComparison.Ordinal) || n.StartsWith("Hidden/TerrainEngine/", StringComparison.Ordinal);
    }
}
