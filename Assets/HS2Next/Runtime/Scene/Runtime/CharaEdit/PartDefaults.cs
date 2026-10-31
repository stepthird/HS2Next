// Prefab defaults of item changes (C7; package B's IPartDefaults): the colours an item
// starts with in HS2's Maker, read from the item's prefab in its (open) bundle without instantiating it, cached per row.
// Behaviour of HS2 (clean room, values as the game stores them):
//   clothes, per colour group j = 1..3: base = defMainColor0j, pattern = defPtnIndex0j (a vanilla row of category 348;
//     0 = none), patternColor = defPatternColor0j, gloss = defGloss0j, metallic = defMetallic0j, layout =
//     (InverseLerp(20, 1, x), InverseLerp(20, 1, y), InverseLerp(−1, 1, z), InverseLerp(−1, 1, w)) of defLayout0j,
//     rotation = InverseLerp(−1, 1, defRotation0j) (Unity's clamped InverseLerp, as the game)
//   accessories, slots 1..3 when useColor0j: color = defColor0j, gloss = defGlossPower0j, metallic = defMetallicPower0j;
//     slot 4 (the alpha renderers) when rendAlpha is not empty: defColor04 / defGlossPower04 / defMetallicPower04. Slots the
//     prefab does not use, and smoothness (no prefab default; only hair-type accessories show it), take the format
//     defaults (HS2 leaves them as they were; they have no visible effect on that prefab)
//   hair decoration colours: acsDefColor[i] for the colours the prefab has, white for the rest (format default)
//   hair bundles (4.3): boneInfo's limits, base pose and default rates (TryHairBundles; the male variant for males)
// The prefab is the row's MainAB / MainData (male clothes, categories 1xx: MainData02 when set, as the build). A row
// without a prefab component (the "none" rows' p_dummy, unknown rows) answers false: the op keeps the colours and warns.
using System;
using System.Collections.Generic;
using AIChara;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class PartDefaults : IPartDefaults
    {
        public ContentCatalog Catalog { get; }
        readonly Dictionary<(int, ContentId), object> cache = new Dictionary<(int, ContentId), object>();
        static readonly object None = new object();

        /// <summary>Prefab loads done (cache misses; tests).</summary>
        public int Loads { get; private set; }

        public PartDefaults(ContentCatalog catalog) { Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog)); }

        public bool TryClothesColors(int category, string itemRef, out ClothesColor[] colors)
        {
            colors = Get(category, itemRef, ReadClothes) as ClothesColor[];
            return colors != null;
        }

        public bool TryAccessoryColors(int category, string itemRef, out AccessoryColor[] colors)
        {
            colors = Get(category, itemRef, ReadAccessory) as AccessoryColor[];
            return colors != null;
        }

        // ---- accessory facts (Phase 4.5 package A): the CmpAccessory on the root of the
        //      row's prefab (MainAB / MainData, the copy the catalog serves), as the loader reads it — typeHair, the colour
        //      areas, the alpha renderers, the two transform sets —, the DynamicBone components and colliders inside the
        //      prefab, the row's Parent column and the prefab's default colours (as TryAccessoryColors). Cached per row;
        //      handed out as copies. A prefab without a CmpAccessory on its root gives facts with HasCmpAccessory false.
        readonly Dictionary<(int, ContentId), AccessoryFacts> accessoryFactsCache = new Dictionary<(int, ContentId), AccessoryFacts>();

        public bool TryAccessoryFacts(int category, string itemRef, out AccessoryFacts facts)
        {
            facts = null;
            if (!ContentId.TryParse(itemRef, out var key)) return false;
            if (!accessoryFactsCache.TryGetValue((category, key), out var f))
            {
                f = null;
                try
                {
                    var e = Catalog.GetEntry(category, key);
                    var prefab = e != null ? Prefab(category, key) : null;
                    if (prefab) f = ReadAccessoryFacts(prefab, e, category, key.ToString());
                }
                catch (Exception ex) { Debug.LogWarning("part defaults: accessory facts " + category + " " + key + ": " + ex.Message); }
                accessoryFactsCache[(category, key)] = f;
            }
            if (f == null) return false;
            facts = f.Clone();
            return true;
        }

        static AccessoryFacts ReadAccessoryFacts(GameObject prefab, ListEntry e, int category, string item)
        {
            var cmp = prefab.GetComponent<CmpAccessory>();
            var p = new HS2Next.Chara.Edit.AccessoryReport.PrefabFacts();
            HS2Next.Chara.Edit.AccessoryReport.FillFacts(p, cmp, prefab);
            var f = AccessoryFacts.None();
            f.Category = category;
            f.Item = item;
            f.PrefabName = e?["MainData"];
            f.ParentColumn = e?["Parent"];
            f.HasCmpAccessory = p.HasCmpAccessory;
            f.DynamicBones = p.DynamicBones;
            f.Colliders = p.Colliders;
            if (cmp == null) return f;
            f.TypeHair = p.TypeHair;
            f.UseColor = (bool[])p.UseColor.Clone();
            f.AlphaRenderers = p.AlphaRenderers;
            f.Move1 = p.Move1;
            f.Move2 = p.Move2;
            f.DefaultColors = HS2Next.Chara.Edit.AccessoryReport.DefaultsOf(cmp);
            return f;
        }

        public bool TryHairAccessoryColors(int category, string itemRef, out Color4[] colors)
        {
            var c = Get(category, itemRef, ReadHair) as Color4[];
            colors = c != null ? (Color4[])c.Clone() : null;
            return colors != null;
        }

        // ---- hair bundles (package B, 4.3): CmpHair.boneInfo of the row's prefab — MainData02
        //      for males when set (the build's DataKey) — in order: limits, base pose, default rates
        //      (HairBundleFacts: InverseLerp per axis, Unity's clamped Mathf.InverseLerp), shown = trfCorrect present. Cached per
        //      (category, row, male variant).
        readonly Dictionary<(int, ContentId, bool), HairBundleFacts[]> bundleCache = new Dictionary<(int, ContentId, bool), HairBundleFacts[]>();

        public bool TryHairBundles(int category, string itemRef, int sex, out HairBundleFacts[] facts)
        {
            facts = null;
            if (!ContentId.TryParse(itemRef, out var key)) return false;
            var e = Catalog.GetEntry(category, key);
            if (e == null) return false;
            var d2 = e["MainData02"];
            bool maleVariant = sex == 0 && !string.IsNullOrEmpty(d2) && d2 != "0";
            if (!bundleCache.TryGetValue((category, key, maleVariant), out facts))
            {
                facts = null;
                try
                {
                    string dk = maleVariant ? "MainData02" : "MainData";
                    if (!string.IsNullOrEmpty(e[dk]) && e[dk] != "p_dummy")
                    {
                        Loads++;
                        var prefab = Catalog.LoadTransient("maker:facts", () => Catalog.LoadAsset<GameObject>(e, "MainAB", dk));
                        var h = prefab ? prefab.GetComponentInChildren<CmpHair>(true) : null;
                        if (h) facts = ReadBundles(h);
                    }
                }
                catch (Exception ex) { Debug.LogWarning("part defaults: hair bundles " + category + " " + key + ": " + ex.Message); }
                bundleCache[(category, key, maleVariant)] = facts;
            }
            if (facts == null) return false;
            facts = (HairBundleFacts[])facts.Clone();   // HairBundleFacts are immutable; the cached array is handed out as a copy
            return true;
        }

        static HairBundleFacts[] ReadBundles(CmpHair h)
        {
            var info = new List<(bool, Vec3, Vec3, Vec3, Vec3, Vec3, Vec3)>();
            if (h.boneInfo != null)
                foreach (var b in h.boneInfo)
                {
                    if (b == null) { info.Add((false, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero)); continue; }
                    info.Add((b.trfCorrect != null, V(b.posMin), V(b.posMax), V(b.rotMin), V(b.rotMax), V(b.basePos), V(b.baseRot)));
                }
            return HairBundleFacts.Number(info);
        }

        static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);

        // ---- clothes facts (Phase 4.4 package B): the CmpClothes of the row's prefab (MainData02 for the male lists when set, as
        //      the build), its colour-set flags, useBreak, the option part object counts, the groups' renderers and material slots,
        //      the def / half objects, the defaults of the three sets (as TryClothesColors) and the row's Coordinate, NotBra and
        //      StateType columns. Texture sizes and creators exist on a realization only (ClothesInfo). Cached per row; handed out
        //      as copies. The sex does not change the prefab of a clothes row (the list decides), so it is not part of the key.
        readonly Dictionary<(int, ContentId), ClothesKindFacts> clothesFactsCache = new Dictionary<(int, ContentId), ClothesKindFacts>();

        public bool TryClothesFacts(int category, string itemRef, int sex, out ClothesKindFacts facts)
        {
            facts = null;
            if (!ContentId.TryParse(itemRef, out var key)) return false;
            if (!clothesFactsCache.TryGetValue((category, key), out var f))
            {
                f = null;
                try
                {
                    var e = Catalog.GetEntry(category, key);
                    var prefab = e != null ? Prefab(category, key) : null;
                    var c = prefab ? prefab.GetComponentInChildren<CmpClothes>(true) : null;
                    if (c) f = ReadClothesFacts(c, prefab, e, key.ToString());
                }
                catch (Exception ex) { Debug.LogWarning("part defaults: clothes facts " + category + " " + key + ": " + ex.Message); }
                clothesFactsCache[(category, key)] = f;
            }
            if (f == null) return false;
            facts = f.Clone();
            return true;
        }

        static ClothesKindFacts ReadClothesFacts(CmpClothes c, GameObject prefab, ListEntry e, string item)
        {
            var groups = new List<ClothesGroupFacts>();
            var lists = new[] { c.rendNormal01, c.rendNormal02, c.rendNormal03 };
            for (int j = 0; j < lists.Length; j++)
            {
                int rends = 0, slots = 0;
                if (lists[j] != null)
                    foreach (var r in lists[j])
                    {
                        if (!r) continue;
                        rends++;
                        slots += r.sharedMaterials?.Length ?? 0;
                    }
                if (rends > 0) groups.Add(new ClothesGroupFacts { Group = j + 1, Renderers = rends, Slots = slots });
            }
            string st = e?["StateType"];
            return new ClothesKindFacts
            {
                HasPrefab = true, Item = item, PrefabName = prefab.name,
                UseColorN01 = c.useColorN01 != 0, UseColorN02 = c.useColorN02 != 0, UseColorN03 = c.useColorN03 != 0,
                UseColorA01 = c.useColorA01 != 0, UseColorA02 = c.useColorA02 != 0, UseColorA03 = c.useColorA03 != 0,
                UseBreak = c.useBreak != 0, Option01Count = Objects(c.objOpt01), Option02Count = Objects(c.objOpt02),
                Coordinate = e?["Coordinate"], NotBra = e?["NotBra"] == "1",
                StateType = int.TryParse(st, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int stv) ? stv : -1,
                HasTopDef = c.objTopDef != null, HasTopHalf = c.objTopHalf != null, HasBotDef = c.objBotDef != null, HasBotHalf = c.objBotHalf != null,
                Groups = groups.ToArray(),
                Defaults = (ClothesColor[])ReadClothes(prefab),
            };
        }

        static int Objects(List<GameObject> l)
        {
            int n = 0;
            if (l != null) foreach (var o in l) if (o) n++;
            return n;
        }

        /// <summary>Forgets the cached values (after a catalog reload).</summary>
        public void Clear() { cache.Clear(); bundleCache.Clear(); clothesFactsCache.Clear(); accessoryFactsCache.Clear(); }

        object Get(int category, string itemRef, Func<GameObject, object> read)
        {
            if (!ContentId.TryParse(itemRef, out var key)) return null;
            if (!cache.TryGetValue((category, key), out var v))
            {
                v = None;
                try
                {
                    var prefab = Prefab(category, key);
                    if (prefab) v = read(prefab) ?? None;
                }
                catch (Exception e) { Debug.LogWarning("part defaults: " + category + " " + key + ": " + e.Message); }
                cache[(category, key)] = v;
            }
            return v == None ? null : v;   // callers clone (MakerEdits.CopyArray); the cached objects are never handed out for writing
        }

        GameObject Prefab(int category, ContentId key)
        {
            var e = Catalog.GetEntry(category, key);
            if (e == null) return null;
            string dk = "MainData";
            if (category >= 100 && category < 200)
            {
                var d2 = e["MainData02"];
                if (!string.IsNullOrEmpty(d2) && d2 != "0") dk = "MainData02";
            }
            if (string.IsNullOrEmpty(e[dk]) || e[dk] == "p_dummy") return null;
            Loads++;
            return Catalog.LoadTransient("maker:facts", () => Catalog.LoadAsset<GameObject>(e, "MainAB", dk));
        }

        static Color4 C(Color c) => new Color4(c.r, c.g, c.b, c.a);

        static string PatternRef(int id) => id == 0 ? DocDefaults.NoneRef : ContentId.Vanilla(id).ToString();

        static object ReadClothes(GameObject prefab)
        {
            var c = prefab.GetComponentInChildren<CmpClothes>(true);
            if (!c) return null;
            return new[]
            {
                Clothes(c.defMainColor01, c.defPtnIndex01, c.defPatternColor01, c.defGloss01, c.defMetallic01, c.defLayout01, c.defRotation01),
                Clothes(c.defMainColor02, c.defPtnIndex02, c.defPatternColor02, c.defGloss02, c.defMetallic02, c.defLayout02, c.defRotation02),
                Clothes(c.defMainColor03, c.defPtnIndex03, c.defPatternColor03, c.defGloss03, c.defMetallic03, c.defLayout03, c.defRotation03),
            };
        }

        static ClothesColor Clothes(Color main, int pattern, Color patternColor, float gloss, float metallic, Vector4 layout, float rotation) => new ClothesColor
        {
            Base = C(main), Pattern = PatternRef(pattern), PatternColor = C(patternColor), Gloss = gloss, Metallic = metallic,
            Layout = new Vec4(Mathf.InverseLerp(20f, 1f, layout.x), Mathf.InverseLerp(20f, 1f, layout.y), Mathf.InverseLerp(-1f, 1f, layout.z), Mathf.InverseLerp(-1f, 1f, layout.w)),
            Rotation = Mathf.InverseLerp(-1f, 1f, rotation),
        };

        static object ReadAccessory(GameObject prefab)
        {
            var a = prefab.GetComponentInChildren<CmpAccessory>(true);
            if (!a) return null;
            var r = new[] { new AccessoryColor(), new AccessoryColor(), new AccessoryColor(), new AccessoryColor() };
            void Set(int i, bool use, Color col, float gloss, float metallic)
            {
                if (!use) return;
                r[i].Color = C(col);
                r[i].Gloss = gloss;
                r[i].Metallic = metallic;
            }
            Set(0, a.useColor01 != 0, a.defColor01, a.defGlossPower01, a.defMetallicPower01);
            Set(1, a.useColor02 != 0, a.defColor02, a.defGlossPower02, a.defMetallicPower02);
            Set(2, a.useColor03 != 0, a.defColor03, a.defGlossPower03, a.defMetallicPower03);
            Set(3, a.rendAlpha != null && a.rendAlpha.Count > 0, a.defColor04, a.defGlossPower04, a.defMetallicPower04);
            return r;
        }

        static object ReadHair(GameObject prefab)
        {
            var h = prefab.GetComponentInChildren<CmpHair>(true);
            if (!h) return null;
            var r = new[] { Color4.White, Color4.White, Color4.White, Color4.White };
            if (h.acsDefColor != null)
                for (int i = 0; i < r.Length && i < h.acsDefColor.Count; i++) r[i] = C(h.acsDefColor[i]);
            return r;
        }
    }
}
