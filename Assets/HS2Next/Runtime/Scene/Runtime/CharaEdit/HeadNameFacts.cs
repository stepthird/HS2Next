// The head names of the partial card load (Phase 4.6; package B's IHeadNames): which transform,
// renderer and material names belong to a character's head, so a face-only or body-only load can split the plugin data
// that shapes both (ABMX bone modifiers, MaterialEditor Character items). Of(sex, headRef) = the names of every transform
// (renderers included: a renderer's name is its GameObject's) and every material (a `_create` suffix removed, as the build
// draws the material without it) of the head row's prefab (list fo_head 210 / mo_head 110, MainAB / MainData, the copy
// the catalog serves, read without instantiating it), plus the transforms of the shared head skeleton p_cf_head_bone
// (chara/oo_base). Cached per (sex, head ref) while the catalog lives. A row the catalog cannot load answers
// HeadNames.Vanilla's lists (the census names). Main thread (the catalog is).
using System;
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class HeadNameFacts : IHeadNames
    {
        public const int FemaleHeads = 210, MaleHeads = 110;
        const string HeadBoneBundle = "chara/oo_base.unity3d", HeadBonePrefab = "p_cf_head_bone";

        public ContentCatalog Catalog { get; }
        readonly Dictionary<(int, string), IReadOnlyCollection<string>> cache = new Dictionary<(int, string), IReadOnlyCollection<string>>();
        HashSet<string> headBone;

        /// <summary>Prefab reads done (cache misses; tests).</summary>
        public int Loads { get; private set; }

        public HeadNameFacts(ContentCatalog catalog) { Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }

        public IReadOnlyCollection<string> Of(int sex, string headRef)
        {
            int s = sex == 0 ? 0 : 1;
            var key = (s, headRef ?? "");
            if (cache.TryGetValue(key, out var hit)) return hit;
            IReadOnlyCollection<string> names = null;
            try { names = Read(s, headRef); }
            catch (Exception) { names = null; }   // an unreadable prefab: the census names
            names ??= HeadNames.Vanilla.Of(s, headRef);
            cache[key] = names;
            return names;
        }

        HashSet<string> Read(int sex, string headRef)
        {
            if (!ContentId.TryParse(headRef, out var id) || id.IsEmpty) return null;
            var e = Catalog.GetEntry(sex == 0 ? MaleHeads : FemaleHeads, id);
            if (e == null) return null;
            Loads++;
            var prefab = Catalog.LoadTransient("maker:facts", () => Catalog.LoadAsset<GameObject>(e, "MainAB", "MainData"));
            if (!prefab) return null;
            var set = new HashSet<string>(StringComparer.Ordinal);
            Collect(prefab, set);
            foreach (var n in HeadBone()) set.Add(n);
            return set;
        }

        IEnumerable<string> HeadBone()
        {
            if (headBone != null) return headBone;
            headBone = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var p = Catalog.LoadAsset<GameObject>(HeadBoneBundle, HeadBonePrefab);
                if (p) foreach (var t in p.GetComponentsInChildren<Transform>(true)) headBone.Add(t.name);
            }
            catch (Exception) { }
            return headBone;
        }

        /// <summary>The transform names and the material names (`_create` removed) under <paramref name="root"/>.</summary>
        public static void Collect(GameObject root, ISet<string> into)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) into.Add(t.name);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    string n = m.name;
                    if (n.EndsWith("_create", StringComparison.Ordinal)) n = n.Substring(0, n.Length - "_create".Length);
                    into.Add(n);
                }
        }
    }
}
