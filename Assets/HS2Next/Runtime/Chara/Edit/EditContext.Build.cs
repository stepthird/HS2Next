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
            return Spawn(prefab, name, parent);
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

        // but keeping index alignment when a bone is missing (the original bone stays in its slot).
        public void Rebind(GameObject obj, Dictionary<string, Transform> dict, string delTop, Transform rootBone)
        {
            int missing = 0;
            foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bones = smr.bones;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] && dict.TryGetValue(bones[i].name, out var t)) bones[i] = t;
                    else if (bones[i]) missing++;
                }
                smr.bones = bones;
                smr.localBounds = PartBounds;
                if (rootBone && !smr.GetComponent<Cloth>()) smr.rootBone = rootBone;
                else if (smr.rootBone && dict.TryGetValue(smr.rootBone.name, out var rb)) smr.rootBone = rb;
            }
            var del = Find(obj.transform, delTop);
            if (del) { del.SetParent(null); Object.DestroyImmediate(del.gameObject); }
            if (missing > 0) Log.Add($"{obj.name}: {missing} bones kept (not in skeleton)");
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
        public Material OwnMaterial(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (mats.Length == 0) return null;
            if (!mats[0] || !Owned.Contains(mats[0])) { mats[0] = Instance(mats[0]); r.sharedMaterials = mats; }
            return mats[0];
        }

        public Material[] OwnMaterials(Renderer r)
        {
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
        // copy, so no character renders a shared material. Returns the number of copies made.
        public int OwnAll(GameObject scope)
        {
            if (!scope) return 0;
            int n = Owned.Count;
            foreach (var r in scope.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && !Owned.Contains(mats[i])) { mats[i] = Instance(mats[i]); changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
            return Owned.Count - n;
        }

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

        /// <summary>Destroys the owned materials of a subtree's renderers and forgets them (a part swap).</summary>
        public int DestroyOwnedMaterials(GameObject scope)
        {
            if (!scope) return 0;
            var dead = new HashSet<Material>();
            foreach (var r in scope.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m && Owned.Contains(m) && m != Ctx.BodyDraw && m != Ctx.FaceDraw) dead.Add(m);
            foreach (var m in dead) { Owned.Remove(m); Object.DestroyImmediate(m); }
            return dead.Count;
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
