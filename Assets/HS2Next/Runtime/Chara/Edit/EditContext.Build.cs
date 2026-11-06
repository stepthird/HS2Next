// The build state and the shared helpers of the edit units: moved out of CharaAssembler
// (Phase 1-3 code, behaviour unchanged) so that the units can run after the build, for the female build and its male
// differences; no game code is copied.
using System;
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Edit
{
    /// <summary>
    /// Per-sex list categories and assets. Clothes kinds 2/3/5/6 (inner_t, inner_b, panst, socks)
    /// use the female lists for both sexes.
    /// </summary>
    public sealed class CharaSex
    {
        public string RootPrefix, BodyPrefab, BodyMesh, BodyMaterial, FaceSliderList;
        public int Head, SkinF, DetailF, Beard, SkinB, DetailB, Sunburn, HairFrontDefault;
        public int[] Clothes;   // kinds 0..7

        public static readonly CharaSex Female = new CharaSex
        {
            RootPrefix = "chaF_", BodyPrefab = "p_cf_body_00", BodyMesh = "o_body_cf", BodyMaterial = "cf_m_skin_body_00", FaceSliderList = "cf_customhead",
            Head = 210, SkinF = 211, DetailF = 212, Beard = -1, SkinB = 231, DetailB = 232, Sunburn = 233, HairFrontDefault = 1,
            Clothes = new[] { 240, 241, 242, 243, 244, 245, 246, 247 },
        };
        public static readonly CharaSex Male = new CharaSex
        {
            RootPrefix = "chaM_", BodyPrefab = "p_cm_body_00", BodyMesh = "o_body_cm", BodyMaterial = "cm_m_skin_body_00", FaceSliderList = "cm_customhead",
            Head = 110, SkinF = 111, DetailF = 112, Beard = 121, SkinB = 131, DetailB = 132, Sunburn = 133, HairFrontDefault = 2,
            Clothes = new[] { 140, 141, 242, 243, 144, 245, 246, 147 },
        };
    }

    public sealed partial class EditContext
    {
        static readonly Bounds PartBounds = new Bounds(new Vector3(0, -2, 0), new Vector3(20, 20, 20));

        // ---------------------------------------------------------------- state (was CharaAssembler's private fields)
        public bool Male;
        public CharaSex S;
        /// <summary>The part resolver of the input (documents: DocumentPartResolver; cards: CardPartResolver).</summary>
        public ICharaPartResolver Parts;
        /// <summary>Part prefabs instantiated (body, head, hair, clothes, accessories; "none" rows excluded).</summary>
        public int PartsLoaded;
        /// <summary>The character was URP-mapped by the scene builder (CharaBuilders.Run): new subtrees are mapped in place.</summary>
        public bool UrpMapped;

        public GameObject Root => Ctx.Root;
        public HashSet<Material> Owned => Ctx.OwnedMaterials;

        /// <summary>The four skin compositing passes and the clothes colour passes.</summary>
        public CompositeInputs BodySkin, BodyDetail, FaceSkin, FaceDetail;
        public readonly CompositeInputs[,] ClothesColor = new CompositeInputs[8, 3], ClothesGloss = new CompositeInputs[8, 3];

        // mask records (Phase 3.5)
        public Renderer[] MaskRenderers = new Renderer[4];
        public int MaskLowerType = -1;
        public bool MaskLowerOk0, MaskLowerOk1, MaskNotBot;
        public readonly List<string> MaskMissing = new List<string>();

        // ---------------------------------------------------------------- reads

        public CharaDocument Doc => Input.Chara;
        public CoordDocument CoordDoc => Input.Coord;

        /// <summary>The raw id of a reference (the long id the card / adapter tree carried).</summary>
        public static int RawId(string refText) => ContentId.TryParse(refText, out var id) ? id.Id : 0;

        /// <summary>
        /// The list row of a document reference: registers the reference under its part key (document builds; the same key,
        /// category and path as VanillaTrees) and resolves it like the build did (resolver, then the male range check).
        /// <paramref name="property"/>: the Sideloader property of the card resolver (null when the build had none).
        /// </summary>
        public ListEntry Row(int category, string refText, string property, string partKey, string path)
        {
            if (Parts is DocumentPartResolver d && partKey != null) d.Register(partKey, refText, category, path);
            return Lookup(category, RawId(refText), property, partKey);
        }

        /// <summary>Registers a reference the units do not resolve (hair mesh items, female beard, face paint layouts) so the
        /// resolver's part table equals the document's (document builds only).</summary>
        public void Register(string partKey, string refText, int category, string path)
        {
            if (Parts is DocumentPartResolver d) d.Register(partKey, refText, category, path);
        }

        // part: the PartKeys value of the reference (null for the assembler's fixed default rows); property: the card
        // resolver's Sideloader property. The male range check (RangeDefault) applies to both paths.
        public ListEntry Lookup(int cat, int id, string property, string part)
        {
            var e = Parts.Resolve(cat, id, property, part);
            if (e == null && Male && RangeDefault(cat) is int def && id != def)
            {
                e = Catalog.GetEntry(cat, ContentId.Vanilla(def));
                Log.Add($"range check: cat {cat} id {id} -> {def}");
            }
            return e;
        }

        // The range check: an id that is not in its list is replaced by this default.
        // Applied on the male path only (the female path keeps its earlier per-part fallbacks). Not
        // checked for males: inner_t/inner_b/panst/socks (242/243/245/246); accessories (350..363)
        // with a missing row are dropped (MemberInit), which the accessory loader already does.
        static int? RangeDefault(int cat)
        {
            switch (cat)
            {
                case 301: return 2;                                         // front hair (female: 1)
                case 110: case 111: case 112: case 121:                     // mo_head, mt_skin_f, mt_detail_f, mt_beard
                case 131: case 132: case 133:                               // mt_skin_b, mt_detail_b, mt_sunburn
                case 140: case 141: case 144: case 147:                     // mo_top, mo_bot, mo_gloves, mo_shoes
                case 300: case 302: case 303:                               // so_hair_b/s/o
                case 313: case 314: case 315: case 316: case 317: case 318: // paint, eyebrow, eyelash, eyeshadow, eye, eyeblack
                case 319: case 320: case 322: case 323: case 334: case 335: // eye_hl, cheek, lip, mole, nip, underhair
                case 348:                                                   // st_pattern
                    return 0;
                default: return null;
            }
        }

        /// <summary>Prefab column of a list row: males use MainData02 whenever it is set (not "0").</summary>
        public string DataKey(ListEntry e)
        {
            if (!Male || e == null) return "MainData";
            var d2 = e["MainData02"];
            return !string.IsNullOrEmpty(d2) && d2 != "0" ? "MainData02" : "MainData";
        }

        public Texture2D Tex(ListEntry e, string abKey, string texKey) => e == null ? null : Catalog.LoadAsset<Texture2D>(e, abKey, texKey);

        public string Name(ListEntry e) => e == null ? "null" : $"{e.Category}:{e.Key.Id}{(e.IsVanilla ? "" : "@" + e.Key.SourceGuid)} {e[DataKey(e)] ?? e["MainTex"]}";

        // ---------------------------------------------------------------- objects

        public GameObject LoadPart(ListEntry e, string name, Transform parent)
        {
            if (e == null) return null;
            string dk = DataKey(e);
            var prefab = Catalog.LoadAsset<GameObject>(e, "MainAB", dk);
            if (!prefab) { Log.Add($"{name}: prefab not found ({Name(e)} {e["MainAB"]}/{e[dk]})"); return null; }
            if (e[dk] != "p_dummy") PartsLoaded++;
            var go = Spawn(prefab, name, parent);
            RegisterBodyMaterialTargets(go);
            RegisterBodyCuts(go);
            RegisterAdditionalBones(go, e, e["MainAB"], e[dk]);
            return go;
        }

        public static GameObject Spawn(GameObject prefab, string name, Transform parent)
        {
            if (!prefab) throw new Exception("missing prefab for " + name);
            var go = Object.Instantiate(prefab);
            go.name = name;
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }

        public static Dictionary<string, Transform> BoneDict(Transform root)
        {
            var d = new Dictionary<string, Transform>();
            void Walk(Transform t)
            {
                if (!d.ContainsKey(t.name)) d[t.name] = t;
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            Walk(root);
            return d;
        }

        // but keeping index alignment when a bone is missing (the original bone stays in its slot). A part with bone
        // implants (after 1.0.0) first moves those bones under the skeleton; its renderers then
        // keep the implanted bones and get null for a bone that is neither implanted nor in the skeleton. A part with
        // additional bones (after 1.0.0) moves them under the skeleton too; a renderer bone the skeleton lacks by name
        // keeps an additional bone.
        public void Rebind(GameObject obj, Dictionary<string, Transform> dict, string delTop, Transform rootBone)
        {
            var implanted = ImplantBones(obj, dict);
            var added = MoveAdditionalBones(obj);
            int missing = 0;
            foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (!bones[i] || (implanted != null && implanted.Contains(bones[i]))) continue;
                    if (dict.TryGetValue(bones[i].name, out var t)) bones[i] = t;
                    else if (added != null && added.Contains(bones[i])) continue;
                    else { missing++; if (implanted != null) bones[i] = null; }
                }
                smr.bones = bones;
                smr.localBounds = PartBounds;
                if (rootBone && !smr.GetComponent<Cloth>()) smr.rootBone = rootBone;
                else if (smr.rootBone && dict.TryGetValue(smr.rootBone.name, out var rb)) smr.rootBone = rb;
            }
            var del = Find(obj.transform, delTop);
            if (del) { del.SetParent(null); Object.DestroyImmediate(del.gameObject); }
            if (missing > 0) Log.Add(implanted != null ? $"{obj.name}: {missing} bones neither implanted nor in the skeleton (set to null)"
                                                       : $"{obj.name}: {missing} bones kept (not in skeleton)");
        }

        // (after 1.0.0): a part's bone implants, each a pair (trfSrc, trfDst) of transforms of its
        // skeleton copy. trfSrc and everything under it move under the skeleton bone named like trfDst with trfSrc's local
        // transform; the moved roots are kept per part and destroyed with it (ReleasePart). Colliders on the moved bones are
        // found by the dynamic-bone stage's scan of the body skeleton.
        readonly Dictionary<GameObject, List<Transform>> implants = new Dictionary<GameObject, List<Transform>>();

        /// <summary>The bones implanted for <paramref name="part"/> (their roots), or null.</summary>
        public IReadOnlyList<Transform> ImplantsOf(GameObject part) => part && implants.TryGetValue(part, out var l) ? l : null;

        /// <summary>Implants the part's bones; returns every implanted transform, or null when the part has no implant.</summary>
        HashSet<Transform> ImplantBones(GameObject obj, Dictionary<string, Transform> dict)
        {
            var procs = obj.GetComponentsInChildren<ModBoneImplantor.BoneImplantProcess>(true);
            if (procs.Length == 0) return null;
            var all = new HashSet<Transform>();
            var roots = new List<Transform>();
            foreach (var p in procs)
            {
                if (!p.trfSrc || !p.trfDst || p.trfSrc == p.trfDst) { Log.Add($"{obj.name}: a bone implant without two different transforms"); continue; }
                if (!dict.TryGetValue(p.trfDst.name, out var dst) || !dst) { Log.Add($"{obj.name}: bone implant target {p.trfDst.name} is not in the skeleton"); continue; }
                p.trfSrc.SetParent(dst, false);
                roots.Add(p.trfSrc);
                foreach (var t in p.trfSrc.GetComponentsInChildren<Transform>(true)) all.Add(t);
            }
            if (roots.Count > 0)
            {
                implants[obj] = roots;
                Log.Add($"{obj.name}: {all.Count} bones implanted under {string.Join(", ", roots.Select(r => r.parent.name).Distinct())}");
            }
            return all;
        }

        /// <summary>Destroys the bones implanted for <paramref name="part"/> (they live under the skeleton, not under the part).</summary>
        public int DestroyImplants(GameObject part)
        {
            if (!part || !implants.TryGetValue(part, out var roots)) return 0;
            implants.Remove(part);
            int n = 0;
            foreach (var r in roots) if (r) { r.SetParent(null, false); Object.DestroyImmediate(r.gameObject); n++; }
            return n;
        }

        // (after 1.0.0): BonesFramework's additional bones. The part loader reads the line of
        // the part bundle's additional_bones for the prefab; the rebind walks the part from its root's children, depth first:
        // a transform the line names moves, with everything under it and its local transform, under the first transform named
        // like its parent in a depth-first search from p_cf_anim (else p_cm_anim) under the part's parent, else from the part's
        // parent (a head: p_cf_head_bone), the part itself left out; it is not descended into. The moved roots are kept per
        // part and destroyed with it (ReleasePart). G6 as Place does for parts: a full build has a bone's own children first,
        // then the moved roots in the build order of their parts, then what is made after the parts (the look targets); an
        // edit puts a moved root where the build would.
        readonly Dictionary<GameObject, HashSet<string>> additionalNames = new Dictionary<GameObject, HashSet<string>>();
        readonly Dictionary<GameObject, List<Transform>> additionalBones = new Dictionary<GameObject, List<Transform>>();
        // the part's build rank: its unit's place in the flush order (unknown: after every part)
        readonly Dictionary<GameObject, int> additionalRank = new Dictionary<GameObject, int>();

        /// <summary>The additional bones moved for <paramref name="part"/> (their roots), or null.</summary>
        public IReadOnlyList<Transform> AdditionalBonesOf(GameObject part) => part && additionalBones.TryGetValue(part, out var l) ? l : null;

        /// <summary>The live parts with moved additional bones.</summary>
        public IEnumerable<GameObject> PartsWithAdditionalBones => additionalBones.Keys.Where(p => p);

        /// <summary>Registers the bone names the additional_bones of <paramref name="bundlePath"/> lists for a part just made from
        /// its prefab <paramref name="prefab"/> (the part loader, the head); returns how many.</summary>
        public int RegisterAdditionalBones(GameObject part, ListEntry e, string bundlePath, string prefab)
        {
            if (!part) return 0;
            var text = Catalog.LoadOptionalAt<TextAsset>(e, bundlePath, AdditionalBones.Asset);
            var names = text ? AdditionalBones.Parse(text.text, prefab) : null;
            if (names == null) return 0;
            additionalNames[part] = names;
            additionalRank[part] = UnitRank();
            return names.Count;
        }

        /// <summary>Registers the additional bone names of a part directly (tests).</summary>
        public void RegisterAdditionalBones(GameObject part, IEnumerable<string> names)
        {
            if (!part) return;
            additionalNames[part] = new HashSet<string>(names, StringComparer.Ordinal);
            additionalRank[part] = UnitRank();
        }

        int UnitRank()
        {
            var units = Editor?.Units;
            if (units != null && CurrentUnit != null)
                for (int i = 0; i < units.Count; i++) if (units[i].Id == CurrentUnit) return i;
            return int.MaxValue;
        }

        /// <summary>Moves the part's registered additional bones; returns every moved transform, or null when none moved.</summary>
        HashSet<Transform> MoveAdditionalBones(GameObject obj)
        {
            if (!obj || !additionalNames.TryGetValue(obj, out var names)) return null;
            additionalNames.Remove(obj);
            var parent = obj.transform.parent;
            var from = parent ? parent.Find("p_cf_anim") ?? parent.Find("p_cm_anim") ?? parent : null;
            int rank = additionalRank.TryGetValue(obj, out var k) ? k : int.MaxValue;
            // in an edit: the build rank of every moved root, for the placement
            var ranks = InBuild ? null : new Dictionary<Transform, int>();
            if (ranks != null)
                foreach (var kv in additionalBones)
                {
                    int r = additionalRank.TryGetValue(kv.Key, out var kr) ? kr : int.MaxValue;
                    foreach (var t in kv.Value) if (t) ranks[t] = r;
                }
            var roots = new List<Transform>();
            var all = new HashSet<Transform>();
            var lost = new List<string>();
            void Walk(Transform t)
            {
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (!names.Contains(c.name)) { Walk(c); continue; }
                    // (the plugin parents a bone whose parent name it does not find to nothing, at the scene root)
                    var dst = from ? FindOutside(from, t.name, obj.transform) : null;
                    if (!dst) { lost.Add($"{c.name} (parent {t.name})"); continue; }
                    c.SetParent(dst, false);
                    if (ranks != null) { PlaceMoved(c, dst, rank, ranks); ranks[c] = rank; }
                    i--;
                    roots.Add(c);
                    foreach (var b in c.GetComponentsInChildren<Transform>(true)) all.Add(b);
                }
            }
            Walk(obj.transform);
            if (lost.Count > 0) Log.Add($"{obj.name}: additional bones left in the part (no transform named like their parent): {string.Join(", ", lost)}");
            if (roots.Count == 0) return null;
            additionalBones[obj] = roots;
            Log.Add($"{obj.name}: {all.Count} additional bones moved under {string.Join(", ", roots.Select(r => r.parent.name).Distinct())}");
            return all;
        }

        // Depth first from t (t itself first) without entering skip: the first transform named name.
        static Transform FindOutside(Transform t, string name, Transform skip)
        {
            if (t == skip) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindOutside(t.GetChild(i), name, skip); if (r) return r; }
            return null;
        }

        // An edit's moved root goes after the parent's own skeleton children and the moved roots of parts built no later,
        // before anything else (a part object directly under the same transform is not ranked).
        void PlaceMoved(Transform root, Transform dst, int rank, Dictionary<Transform, int> ranks)
        {
            int at = -1, me = root.GetSiblingIndex();
            for (int i = 0; i < dst.childCount; i++)
            {
                var c = dst.GetChild(i);
                if (c != root && (SkeletonOwn(c) || ranks.TryGetValue(c, out int r) && r <= rank)) at = i;
            }
            root.SetSiblingIndex(at < me ? at + 1 : at);
        }

        bool SkeletonOwn(Transform t) =>
            Ctx.BodyBones != null && Ctx.BodyBones.TryGetValue(t.name, out var b) && b == t
            || Ctx.HeadBones != null && Ctx.HeadBones.TryGetValue(t.name, out var h) && h == t;

        /// <summary>Destroys the additional bones moved for <paramref name="part"/> (they live under the skeleton, not under the part).</summary>
        public int DestroyAdditionalBones(GameObject part)
        {
            if (!part) return 0;
            additionalNames.Remove(part);
            additionalRank.Remove(part);
            if (!additionalBones.TryGetValue(part, out var roots)) return 0;
            additionalBones.Remove(part);
            int n = 0;
            foreach (var r in roots) if (r) { r.SetParent(null, false); Object.DestroyImmediate(r.gameObject); n++; }
            return n;
        }

        // (copyDynamicBone): m_Root, each m_Exclusions[i] and each m_notRolls[i] of every DynamicBone in the
        // part go to the body bone of the same name (a name the body lacks keeps the part's transform). Runs before Rebind
        // destroys the part's own skeleton copy, while the names are still readable. The collider lists are set with the
        // dynamic-bone stage (Runtime/Motion/DynamicBones/DynamicBoneSetup.cs).
        public global::DynamicBone[] RepointDynamicBones(GameObject part)
        {
            var bodyDict = Ctx.BodyBones;
            Transform Body(Transform t) => t && bodyDict.TryGetValue(t.name, out var b) ? b : t;
            var dbs = part.GetComponentsInChildren<global::DynamicBone>(true);
            foreach (var db in dbs)
            {
                db.m_Root = Body(db.m_Root);
                if (db.m_Exclusions != null) db.m_Exclusions = db.m_Exclusions.Select(Body).ToList();
                if (db.m_notRolls != null) db.m_notRolls = db.m_notRolls.Select(Body).ToList();
            }
            return dbs;
        }

        // After Rebind: references left on the destroyed skeleton copy become plain nulls, and components that sat inside it
        // (destroyed with it) are skipped. Returns the components that remain.
        public static int DropDestroyedBones(global::DynamicBone[] dbs)
        {
            void Clean(List<Transform> l) { if (l != null) for (int i = 0; i < l.Count; i++) if (!l[i]) l[i] = null; }
            int n = 0;
            foreach (var db in dbs)
            {
                if (!db) continue;
                n++;
                if (!db.m_Root) db.m_Root = null;
                Clean(db.m_Exclusions);
                Clean(db.m_notRolls);
            }
            return n;
        }

        public static void CopySameNameTransforms(Transform dst, Transform src)
        {
            var d = BoneDict(dst);
            void Walk(Transform t)
            {
                if (d.TryGetValue(t.name, out var x) && x != dst)
                { x.localPosition = t.localPosition; x.localRotation = t.localRotation; x.localScale = t.localScale; }
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            Walk(src);
        }

        public static Transform Find(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r) return r; }
            return null;
        }

        // ---------------------------------------------------------------- materials

        // The explicit form of renderer.material / renderer.materials, which copy implicitly and leak in edit mode (
        // 10): a slot holding a material this character has not copied yet gets a per-character copy, assigned through
        // sharedMaterials; later calls return that copy. Bundle materials are shared by every character using the asset and
        // are never written. As with Unity's instancing, an empty slot gets a copy of the default material.
        // Ownership is by identity (Context.OwnedMaterials: these copies, the draw materials, the extension modules' copies).
        // A body-material target has no material of its own: null / none, nothing is written
        // (the game's writes to it are discarded on the next frame).
        public Material OwnMaterial(Renderer r)
        {
            if (Ctx.DrawsBodyMaterials(r)) return null;
            var mats = r.sharedMaterials;
            if (mats.Length == 0) return null;
            if (!mats[0] || !Owned.Contains(mats[0])) { mats[0] = Instance(mats[0]); r.sharedMaterials = mats; }
            return mats[0];
        }

        public Material[] OwnMaterials(Renderer r)
        {
            if (Ctx.DrawsBodyMaterials(r)) return new Material[0];
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
                if (!mats[i] || !Owned.Contains(mats[i])) { mats[i] = Instance(mats[i]); changed = true; }
            if (changed) r.sharedMaterials = mats;
            return mats;
        }

        public Material[] Mats(Renderer r) => r ? OwnMaterials(r) : new Material[0];

        // A copy that matches Unity's renderer instance: named "<name> (Instance)" (MaterialEditorApplier matches material
        // names with the suffix removed), and with the shader re-assigned, which resolves keywords a freshly loaded bundle
        // material still holds unresolved (e.g. _EMISSION on the embedded Standard "Default-Material") as Unity's instancing
        // does; the render queue is kept. Keywords are the source's loaded ones (Runtime/Rendering/MaterialCopies.cs: the
        // Editor's property drawers do not switch them).
        public Material Instance(Material m)
        {
            if (!m) m = DefaultMaterial();
            var c = MaterialCopies.Copy(m, m.name + " (Instance)");
            if (c.shader)
            {
                int q = c.rawRenderQueue;
                MaterialCopies.SetShader(c, c.shader);
                c.renderQueue = q;
            }
            Owned.Add(c);
            return c;
        }

        // After the extension stage: every renderer slot still holding a material this character did not make (bundle
        // materials of parts nothing wrote, e.g. hair-type accessories, uncoloured clothes parts, eyes' sub-meshes) gets its
        // copy, so no character renders a shared material. Returns the number of copies made. Body-material targets
        // are skipped (they get the body's materials in the same step).
        public int OwnAll(GameObject scope)
        {
            if (!scope) return 0;
            int n = Owned.Count;
            foreach (var r in scope.GetComponentsInChildren<Renderer>(true))
            {
                if (Ctx.DrawsBodyMaterials(r)) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && !Owned.Contains(mats[i])) { mats[i] = Instance(mats[i]); LateCopies.Add(mats[i]); changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
            return Owned.Count - n;
        }

        /// <summary>The copies OwnAll made because nothing before it asked for them. An object with material items has plain copies there
        /// instead (MaterialEditorApplier), which differ in the name alone: a value edit moves a material between the two.</summary>
        public readonly HashSet<Material> LateCopies = new HashSet<Material>();

        // The material Unity gives an empty slot: the default material of a new primitive (Built-in: Default-Material;
        // with a render pipeline asset: the pipeline's default material).
        static Material DefaultMaterial()
        {
            var prim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var m = prim.GetComponent<Renderer>().sharedMaterial;
            Object.DestroyImmediate(prim);
            return m;
        }

        public static void SetBaseMaterial(Renderer r, Material m)
        {
            var mats = r.sharedMaterials;
            if (mats.Length == 0) mats = new Material[1];
            mats[0] = m;
            r.sharedMaterials = mats;
        }

        // A per-character copy that keeps the source's name (the draw materials).
        public Material Clone(Material m)
        {
            if (!m) return null;
            var c = MaterialCopies.Copy(m);
            Owned.Add(c);
            return c;
        }

        /// <summary>Destroys the owned materials of a subtree's renderers and forgets them (a part swap); the
        /// body's materials a body-material target draws are not its own and stay.</summary>
        public int DestroyOwnedMaterials(GameObject scope)
        {
            if (!scope) return 0;
            var dead = new HashSet<Material>();
            foreach (var r in scope.GetComponentsInChildren<Renderer>(true))
            {
                if (Ctx.DrawsBodyMaterials(r)) continue;
                foreach (var m in r.sharedMaterials)
                    if (m && Owned.Contains(m) && m != Ctx.BodyDraw && m != Ctx.FaceDraw) dead.Add(m);
            }
            foreach (var m in dead) { Owned.Remove(m); Object.DestroyImmediate(m); }
            return dead.Count;
        }

        // (after 1.0.0): a part's ChaCopyBodyMaterial components name renderers that draw the
        // body renderer's materials. The part loader registers them (enabled components only, as only those start in the
        // game; non-null renderers inside the part); the unit copy.bodyMaterial then runs in this flush and, while the
        // character has targets, after every earlier unit of a flush.
        /// <summary>Registers the body-material targets of a part just instantiated (the part loader); returns how many.</summary>
        public int RegisterBodyMaterialTargets(GameObject part)
        {
            if (!part) return 0;
            int n = 0;
            foreach (var c in part.GetComponentsInChildren<ChaCopyBodyMaterial>(true))
            {
                if (!c.enabled || c.RendTarget == null) continue;
                foreach (var r in c.RendTarget)
                    if (r && r.transform.IsChildOf(part.transform) && Ctx.BodyMaterialTargets.Add(r)) n++;
            }
            if (n == 0) return 0;
            if (Editor?.Unit(UnitMap.BodyMaterialCopy) is Units.BodyMaterialCopyUnit u) u.Watching = true;
            Editor?.MarkUnit(UnitMap.BodyMaterialCopy);
            Log.Add($"{part.name}: {n} renderers draw the body's materials");
            return n;
        }

        /// <summary>Gives every live body-material target the body renderer's material array (the same objects) and drops the
        /// destroyed ones; returns how many targets are live (the unit copy.bodyMaterial).</summary>
        public int CopyBodyMaterials()
        {
            Ctx.BodyMaterialTargets.RemoveWhere(r => !r);
            if (Ctx.BodyMaterialTargets.Count == 0) return 0;
            // (in the build the context's body renderer is set by the first extension stage: looked up the same way here)
            var rend = Ctx.BodyRenderer ? Ctx.BodyRenderer : Ctx.Body ? Find(Ctx.Body.transform, S.BodyMesh)?.GetComponent<SkinnedMeshRenderer>() : null;
            if (!rend) { Log.Add($"body materials: no body renderer, {Ctx.BodyMaterialTargets.Count} targets kept"); return Ctx.BodyMaterialTargets.Count; }
            var body = rend.sharedMaterials;
            int set = 0;
            foreach (var r in Ctx.BodyMaterialTargets)
                if (!r.sharedMaterials.SequenceEqual(body)) { r.sharedMaterials = body; set++; }
            if (set > 0) Log.Add($"body materials: {set} of {Ctx.BodyMaterialTargets.Count} targets set ({body.Length} slots)");
            return Ctx.BodyMaterialTargets.Count;
        }

        // (after 1.0.0): a clothes part's BodyCut components each name a switch object
        // (TopA[0]) and a cut body (TopA[1], a skinned copy of the body; TopA[2] a second one). While the component is live and
        // its switch object active, the body renderer is off and the cut body on; otherwise the cut body is off (with TopA[2]:
        // the second one on instead, the body staying off). The cut bodies draw the body renderer's first material in their
        // first slot. Inert on a male body (the game looks for the female body renderer by name).
        bool bodyCutsSwitched;

        /// <summary>Registers the body cuts of a part just instantiated (the part loader); returns how many.</summary>
        public int RegisterBodyCuts(GameObject part)
        {
            if (!part) return 0;
            int n = 0;
            foreach (var c in part.GetComponentsInChildren<MdgqBodyChange.BodyCut>(true))
            {
                if (!c.enabled || c.TopA == null || c.TopA.Count < 2 || !c.TopA[0] || !c.TopA[1]) continue;
                var cuts = c.TopA.Skip(1).Take(2).Select(g => g ? g.GetComponent<SkinnedMeshRenderer>() : null).ToList();
                if (cuts.Any(r => !r)) { Log.Add($"{part.name}: a body cut without its cut-body renderer is inert"); continue; }
                if (Ctx.IsMale) { Log.Add($"{part.name}: a body cut is inert on a male body"); continue; }
                Ctx.BodyCuts.Add(c);
                foreach (var r in cuts) Ctx.BodyCutRenderers.Add(r);
                n++;
            }
            if (n == 0) return 0;
            if (Editor?.Unit(UnitMap.BodyCut) is Units.BodyCutUnit u) u.Watching = true;
            Editor?.MarkUnit(UnitMap.BodyCut);
            Log.Add($"{part.name}: {n} body cuts");
            return n;
        }

        /// <summary>Switches the body and the cut bodies by the cuts' switch objects (the unit body.cut, and the scene after the
        /// clothes state) and drops the destroyed cuts; returns how many cuts are live.</summary>
        public int ApplyBodyCuts()
        {
            Ctx.BodyCuts.RemoveAll(c => !c);
            Ctx.BodyCutRenderers.RemoveWhere(r => !r);
            if (Ctx.BodyCuts.Count == 0 && !bodyCutsSwitched) return 0;
            var body = Ctx.BodyRenderer ? Ctx.BodyRenderer : Ctx.Body ? Find(Ctx.Body.transform, S.BodyMesh)?.GetComponent<SkinnedMeshRenderer>() : null;
            if (!body) { Log.Add($"body cuts: no body renderer, {Ctx.BodyCuts.Count} cuts kept"); return Ctx.BodyCuts.Count; }
            var skin = body.sharedMaterials.Length > 0 ? body.sharedMaterials[0] : null;
            // a body the card hides (Invisible Body) stays hidden, and so do the cut bodies
            bool shown = !Ctx.Stats.InvisibleBody;
            bool bodyOff = false;
            foreach (var c in Ctx.BodyCuts)
            {
                var cut = c.TopA[1].GetComponent<SkinnedMeshRenderer>();
                var second = c.TopA.Count == 3 ? c.TopA[2].GetComponent<SkinnedMeshRenderer>() : null;
                SetFirstSlot(cut, skin);
                if (second) SetFirstSlot(second, skin);
                if (!c.enabled || !c.gameObject.activeInHierarchy) { cut.enabled = false; if (second) second.enabled = false; continue; }
                if (c.TopA.Count > 3) continue;   // the game switches nothing then (its first-slot copy only)
                bool on = c.TopA[0] && c.TopA[0].activeInHierarchy;
                cut.enabled = shown && on;
                if (second) second.enabled = shown && !on;
                bodyOff |= on || second;
            }
            body.enabled = shown && !bodyOff;
            bodyCutsSwitched = Ctx.BodyCuts.Count > 0;
            return Ctx.BodyCuts.Count;
        }

        // The cut body's first slot gets the body's first material; the part's own copy that held the slot is destroyed.
        void SetFirstSlot(SkinnedMeshRenderer r, Material skin)
        {
            var mats = r.sharedMaterials;
            if (mats.Length == 0) mats = new Material[1];
            if (mats[0] == skin) return;
            var old = mats[0];
            mats[0] = skin;
            r.sharedMaterials = mats;
            if (old && old != Ctx.BodyDraw && old != Ctx.FaceDraw && Owned.Contains(old) && !mats.Contains(old)) { Owned.Remove(old); Object.DestroyImmediate(old); }
        }

        // ---------------------------------------------------------------- small helpers
        public static Color A1(Color c) { c.a = 1; return c; }
        public static Color U(Color4 c) => new Color(c.R, c.G, c.B, c.A);
        public static Vector4 U(Vec4 v) => new Vector4(v.X, v.Y, v.Z, v.W);
        public static Vector3 U(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static float F(string s) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        public static float Inv(float a, float b, float v) => Mathf.Approximately(a, b) ? 0 : Mathf.InverseLerp(a, b, v);
        public static string Safe(string s) => string.Concat((s ?? "").Select(c => char.IsLetterOrDigit(c) ? c : '_'));

        static readonly Dictionary<(int, int), Texture2D> opaque = new Dictionary<(int, int), Texture2D>();
        public static Texture2D Opaque(int w, int h)
        {
            if (opaque.TryGetValue((w, h), out var t) && t) return t;
            t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            t.SetPixels(Enumerable.Repeat(new Color(0, 0, 0, 1), 16).ToArray()); t.Apply();
            return opaque[(w, h)] = t;
        }

        /// <summary>Runs one extension stage (Runtime/Chara/Ext/CharaExtApplier.cs) with the context brought up to date.</summary>
        public void Hook(ExtStage stage)
        {
            Ctx.BodyRenderer = Ctx.Body ? Find(Ctx.Body.transform, S.BodyMesh)?.GetComponent<SkinnedMeshRenderer>() : null;
            CharaExtApplier.Apply(Ctx, Ctx.Ext, Ctx.Options, stage);
        }
    }
}
