// The material edits of the loaded map: `scene.map.materialOverrides` on the map's owned copies, one instance per load. The load runs
// a step after the mirrors, before Loaded is raised, so map.load's reply and the thumbnails see the result; a change of the array runs
// a pass in the next Apply. An edit never reloads a map.
//   Values    the pass of MaterialTargets. A texture's offset or scale that a map script rewrites, and a mirror's _ReflectionTex, are kept
//             and read as `driven`; an image on a scrolled material is written.
//   Shaders   a reload is not an option, so a material gets a snapshot, a tracked copy, before its first swap. Any change of the array's
//             shader names first brings the whole map back: every value through the baseline, every swapped material from its snapshot
//             (shader, properties, keywords, queue). The array then applies as on a fresh load, so the result does not depend on history.
//   Release   MapMaterials.Release destroys the copies and the snapshots after the unload; the baseline and the images go with it. The
//             document stays, so a reload of the same row applies again.
using System;
using System.Collections.Generic;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class MapMaterialEdits
    {
        sealed class MapTarget : MaterialTargets
        {
            readonly MapRealization map;
            readonly MapStructure structure;
            readonly MapMaterials owned;
            readonly HashSet<Material> mirrored = new HashSet<Material>();

            public MapTarget(MapRealization map, MapStructure structure, MapMaterials owned, ContentCatalog catalog) : base(catalog)
            {
                this.map = map;
                this.structure = structure;
                this.owned = owned;
            }

            public override IReadOnlyList<Renderer> Renderers => structure.Renderers;
            protected override ResStore Store => map.ResourceStore;
            protected override bool Owns(Material m) => owned.IsOwned(m);
            public override string PathOf(Renderer r) => r ? structure.PathOf(r.transform) : null;

            protected override void Fill()
            {
                var slots = new List<Material>();
                foreach (var r in structure.Renderers)
                {
                    if (!r) continue;
                    r.GetSharedMaterials(slots);
                    foreach (var m in slots) Note(null, m);
                }
                foreach (var m in structure.Mirrors)
                {
                    if (!m.Renderer) continue;
                    m.Renderer.GetSharedMaterials(slots);
                    foreach (var x in slots) if (x) mirrored.Add(x);
                }
            }

            protected override void FillMembers()
            {
                var slots = new List<Material>();
                foreach (var r in structure.Renderers)
                {
                    if (!r || !MaterialGuard.Renderer(r)) continue;
                    r.GetSharedMaterials(slots);
                    foreach (var m in slots) NoteMember(m);
                }
            }

            protected override void FillPaths(Dictionary<string, Renderer> into)
            {
                foreach (var r in structure.Renderers)
                    if (r) into[PathOf(r)] = r;
            }

            public override bool Driven(Material m, string property, MaterialMember member) =>
                member == MaterialMember.Texture ? property == MapMirrors.ReflectionTex && mirrored.Contains(m) : map.Scripts.Drives(m, property);

            protected override bool Isolate(MaterialEntry e)
            {
                var old = e.Material;
                var c = owned.Copy(old);
                if (!c) return false;
                var slots = new List<Material>();
                foreach (var r in structure.Renderers)
                {
                    if (!r || MaterialGuard.Renderer(r)) continue;
                    r.GetSharedMaterials(slots);
                    bool changed = false;
                    for (int i = 0; i < slots.Count; i++)
                        if (slots[i] == old) { slots[i] = c; changed = true; }
                    if (changed) r.sharedMaterials = slots.ToArray();
                }
                Replace(e, c);
                if (e.MemberSlots > 0)
                {
                    var kept = AddEntry(e.Name, old);
                    kept.Slots = kept.MemberSlots = e.MemberSlots;
                    e.Slots -= e.MemberSlots;
                    e.MemberSlots = 0;
                }
                return true;
            }
        }

        readonly MapRealization map;
        readonly MapMaterials owned;
        readonly Dictionary<Material, Material> snapshots = new Dictionary<Material, Material>();
        readonly List<Material> scratch = new List<Material>();
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        MapTarget target;
        string structure = "";

        internal MapMaterialEdits(MapRealization map, MapMaterials owned)
        {
            this.map = map;
            this.owned = owned;
            owned.Releasing += Release;
        }

        IReadOnlyList<MaterialOverride> Items => map.DocumentEdits;

        /// <summary>What the edits reach, built on the first read; null without a loaded map.</summary>
        public MaterialTargets Target
        {
            get
            {
                if (target == null && map.Structure != null) target = new MapTarget(map, map.Structure, owned, map.ContentCatalog);
                return target;
            }
        }

        /// <summary>Materials with a snapshot (a shader edit reached them).</summary>
        public int Snapshots => snapshots.Count;

        /// <summary>The array onto the owned copies; nothing to do for a map that never had an item.</summary>
        internal void Apply()
        {
            var list = Items;
            if (target == null && (list == null || list.Count == 0)) return;
            var t = Target;
            if (t == null) return;
            string s = list != null ? MeStructure.Of(list) : "";
            if (s != structure)
            {
                structure = s;
                Reset(t);
                t.Pass(list, TakeSnapshot);
            }
            else t.Pass(list);
            Findings(t);
        }

        // back to the map as loaded: every value, then every material a swap reached
        void Reset(MaterialTargets t)
        {
            t.Pass(null);
            foreach (var kv in snapshots) Restore(kv.Key, kv.Value);
            if (!map.Behaviour.SeaDone || map.Structure == null) return;
            // the sea takes its keyword away a frame after the load, a snapshot of the load still holds it
            foreach (var r in map.Structure.SeaRenderers)
            {
                if (!r) continue;
                r.GetSharedMaterials(scratch);
                foreach (var m in scratch) if (m && snapshots.ContainsKey(m)) NoSeaKeyword(m);
            }
        }

        static void NoSeaKeyword(Material m)
        {
            m.DisableKeyword(MapBehaviour.SeaKeyword);
            if (MapBehaviour.HasKeyword(m, MapBehaviour.SeaKeyword)) m.shaderKeywords = Array.FindAll(m.shaderKeywords, k => k != MapBehaviour.SeaKeyword);
        }

        void TakeSnapshot(MaterialOverride item)
        {
            if (!(item is MeShader s) || s.Shader == null || MaterialGuard.Shader(s.Shader)) return;
            scratch.Clear();
            target.Select(s.Material, scratch, true, out _, out _);
            foreach (var m in scratch)
                if (!snapshots.ContainsKey(m) && !MaterialGuard.Material(m)) snapshots[m] = owned.Copy(m);
        }

        /// <summary>The material as the snapshot holds it: shader, properties, keywords, render queue.</summary>
        public static void Restore(Material m, Material snapshot)
        {
            MaterialCopies.SetShader(m, snapshot.shader);
            m.CopyPropertiesFromMaterial(snapshot);
            m.shaderKeywords = snapshot.shaderKeywords;
            m.renderQueue = snapshot.renderQueue;
        }

        void Findings(MaterialTargets t)
        {
            if (t.MosaicKept > 0) Once(MosaicSet.MeProtectedCode, t.MosaicKept + " material edit(s) name mosaic parts: kept, not applied");
            if (t.ShadersMissing.Count > 0) Once(CharaRealization.MeShaderUnavailableCode, "material edits name shaders that cannot be drawn: " + string.Join(", ", t.ShadersMissing));
            if (t.ImageTooLarge) Once(MaterialEditorApplier.TextureTooLargeKind, "a material edit's image is larger than the decoder takes");
        }

        void Once(string code, string message)
        {
            if (reported.Add(code)) map.EditFinding(FindingSeverity.Warning, code, message);
        }

        void Release()
        {
            target?.Release();
            target = null;
            snapshots.Clear();
        }
    }
}
