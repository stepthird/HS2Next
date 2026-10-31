// Builds a character GameObject, (female) and
// (male differences); behavioural specs, no game code is copied. Since Phase 4.0
// the build is orchestration only: context + skeleton + body prefab + the BodyMesh build
// hooks, then every edit unit (Runtime/Chara/Edit) marked and flushed once in the build order (parts, masks,
// composites, materials, plugin textures, the mosaic stage, shapes, plugin final, ownership), then the motion stages. The
// mosaic stage (MosaicStageUnit, Runtime/Chara/Ext/CharaBuildHooks.cs) runs the Textures hooks, the mosaic and the
// AfterMosaic hooks; the generic build hooks (CharaBuildHooks) run at BodyMesh, Textures, AfterMosaic and Final. The same units
// apply every later edit of the character's documents (CharaContext.Editor). The units read the documents: the node's
// stored CharaDocument / CoordDocument, or for a card build the documents converted from the card (CardDocuments, values
// as the pre-4.0 code read them).
// The sex comes from the card's Parameter block; CharaSex holds the per-sex list categories and assets.
// Male only: MainData02 prefab column, id range check (an id outside its list replaced by a default), bust/hip physics colliders off,
// N_dan hidden, beard on the face draw material (_Texture5/_Color5), optional height pin 0.75 (MaleHeightPin075).
// Card plugin data is applied by Runtime/Chara/Ext (CharaExtApplier) in the accessory unit
// (MoreAccessories slots >= 20), in the plugin texture step (KSOX + the skin layers build hooks registered, KCOX,
// MaterialEditor) and in the plugin final step (pushUp stored, AAC, ABMX, DBE, InvisibleBody).
// Textures whose mip chain Unity 6 cannot read are swapped for a neutral fallback by TextureGuard (catalog loads) and
// listed in Context.Missing as "texture.badmip".
// Input: every list row comes from the input's ICharaPartResolver with a part key
// (CardPartResolver = the Phase 1 lookup for cards, DocumentPartResolver for stored characters, Runtime/Chara/Documents).
// Test oracle: Build(Card) (the card input with CardDocuments and CardPartResolver) has no runtime caller since Phase 4.6
// (a scene's card is imported and built from documents); the Editor's card rows and the smoke player keep it until the
// test consolidation.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Edit.Units;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Chara
{
    public class CharaAssembler
    {
        public readonly List<string> Log = new List<string>();
        readonly ContentCatalog gd;

        /// <summary>Extension modules to run (null or Enabled = false: plugin data is neither decoded nor applied).</summary>
        public CharaExtOptions ExtOptions = new CharaExtOptions();
        /// <summary>Vanilla parity: evaluate the male height slider at 0.75 regardless of the card. Default off: the card value is used, as with the HS2_UnlockPlayerHeight plugin.</summary>
        public static bool MaleHeightPin075 = false;
        /// <summary>After the extension stage every renderer slot gets a material of this build (OwnAll). false: the
        /// Phase 1 behaviour, parts nothing wrote keep the shared bundle material (measurements only).</summary>
        public static bool OwnAllMaterials = true;
        /// <summary>The last Build's objects, bones, materials, accessory slots, extension results, documents and editor.</summary>
        public CharaContext Context { get; private set; }
        /// <summary>Part prefabs instantiated by the last Build (body, head, hair, clothes, accessories; "none" rows excluded).</summary>
        public int PartsLoaded => Context?.Edit?.PartsLoaded ?? 0;
        /// <summary>True when the last Build used the male path (card Parameter.sex == 0).</summary>
        public bool IsMale => Context != null && Context.IsMale;
        public ShapeEngine BodyShape => Context?.BodyShape;
        public ShapeEngine FaceShape => Context?.FaceShape;

        public CharaAssembler(ContentCatalog gd) { this.gd = gd; }

        /// <summary>The catalog the assembler loads parts from.</summary>
        public ContentCatalog Catalog => gd;

        /// <summary>The units of a character in the build order (the editor sorts them by step and order).</summary>
        public static IEnumerable<EditUnit> StandardUnits()
        {
            yield return new ProfileUnit();
            yield return new HeadUnit();
            for (int k = 0; k < 4; k++) yield return new HairUnit(k);
            for (int k = 0; k < 8; k++) yield return new ClothesPartUnit(k);
            yield return new AccessoryListUnit();
            yield return new MasksUnit();
            yield return new SkinBodyUnit();
            yield return new SkinFaceUnit();
            for (int k = 0; k < 8; k++) yield return new ClothesColorsUnit(k);
            yield return new DrawBodyUnit();
            yield return new DrawFaceUnit();
            yield return new EyesUnit();
            for (int k = 0; k < 8; k++) yield return new ClothesBreakUnit(k);
            yield return new ExtTexturesUnit();
            yield return new MosaicStageUnit();   // step 5 after ext.textures: Textures hooks, the mosaic, AfterMosaic hooks
            yield return new ShapeBodyUnit();
            yield return new ShapeFaceUnit();
            yield return new BustNormalsUnit();   // Phase 4.2: the breast normals (step 6, after shape.body)
            yield return new ExtFinalUnit();
            yield return new OwnershipUnit();
            yield return new BustUnit();
            yield return new DynBoneUnit();
            yield return new AnimRefreshUnit();
        }

        /// <summary>Builds from the input's documents, typed plugin data and part resolver (CharaDocumentInput.From).</summary>
        public GameObject Build(CharaBuildInput input)
        {
            if (input?.Chara == null || input.Coord == null || input.Parts == null || input.Card == null)
                throw new ArgumentException("a build needs the documents' input (CharaDocumentInput.From): documents, part resolver and vanilla trees");
            var c = input.Card;
            var parts = input.Parts;
            TextureGuard.BeginBuild();   // bad-mip textures replaced during this build (TextureGuard.cs)
            bool male = c.Parameter.Get("sex").Int(1) == 0;
            var S = male ? CharaSex.Male : CharaSex.Female;
            var chara = input.Chara; var coord = input.Coord;
            var root = new GameObject(S.RootPrefix + EditContext.Safe(c.Name));
            var bodyTop = new GameObject("BodyTop");
            bodyTop.transform.SetParent(root.transform, false);
            var motion = root.AddComponent<HS2Next.Motion.CharaMotion>();   // stages register during the build (Runtime/Motion)
            motion.Log = Log;
            var ctx = Context = new CharaContext
            {
                Card = c, Catalog = gd, Log = Log, IsMale = male, Options = ExtOptions, Input = input, Chara = chara, Coord = coord,
                Root = root, BodyTop = bodyTop, Motion = motion,
            };
            if (ExtOptions != null && ExtOptions.Enabled)
            {
                if (input.Ext != null) ctx.Ext = input.Ext;
                else
                {
                    try { ctx.Ext = CardExtensions.Decode(c); }
                    catch (Exception e) { Log.Add($"ext: decode failed ({e.GetType().Name}: {e.Message})"); }
                }
            }
            var x = new EditContext
            {
                Ctx = ctx, Catalog = gd, Log = Log, Male = male, S = S, Parts = parts,
                Input = new CharaEditInput { Chara = chara, Coord = coord, Res = input.Res, Parts = parts, Slots = input.Slots, Ext = ctx.Ext },
            };
            var editor = x.Editor = new CharaEditor(x) { Factory = id => UnitMap.IsAccessory(id, out string accId) ? new AccessoryUnit(accId) : null };
            ctx.Edit = x;
            ctx.Editor = editor;
            ctx.LoadAccessory = x.LoadAccessory;
            foreach (var u in StandardUnits()) editor.Register(u);

            BuildSkeleton(x);
            BuildBody(x);
            x.DrainBadMip("build");
            editor.MarkAll();
            editor.Flush(build: true);
            HS2Next.Motion.MotionSetup.Attach(ctx);   // motion stages (Runtime/Motion): corrections, look, expression, dynamic bones
            x.DrainBadMip("build.end");
            x.UpdateBadMipMissing();   // per (source, texture) the first incident in build order, as one bracket over the build
            return root;
        }

        // ---------------------------------------------------------------- skeleton
        void BuildSkeleton(EditContext x)
        {
            var ctx = x.Ctx;
            var animPrefab = gd.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim")
                             ?? gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_anim");
            var anim = ctx.Anim = EditContext.Spawn(animPrefab, "p_cf_anim", ctx.BodyTop.transform);
            var animator = anim.GetComponent<Animator>();
            if (animator) animator.enabled = false;   // rest pose for now
            var jRoot = EditContext.Find(anim.transform, "cf_J_Root");
            var bodyDict = ctx.BodyBones = EditContext.BoneDict(jRoot);
            ctx.Hips = bodyDict.TryGetValue("cf_J_Hips", out var h) ? h : null;
            var headBone = ctx.HeadBone = EditContext.Spawn(gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_head_bone"), "p_cf_head_bone", bodyDict["cf_J_Head_s"]);
            ctx.HeadBones = EditContext.BoneDict(headBone.transform);
            Log.Add($"skeleton: body bones {bodyDict.Count}, head bones {ctx.HeadBones.Count}");
            if (x.Male) DisableBustPhysics(ctx);
        }

        // as the game really runs it: the loader's four
        // enableDynamicBoneBustAndHip flags are never read, so the bust/hip DynamicBone_Ver02 stay enabled and
        // simulate with the male card's values (Motion/DynamicBones BustStage; DynamicBoneSetup.Bust.MaleBustPhysics
        // = false turns them off); only the CmpBoneBody.dbcBust colliders are disabled. They stay in every part's
        // collider list as zero records.
        void DisableBustPhysics(CharaContext ctx)
        {
            int db = 0, col = 0;
            foreach (var d in ctx.Anim.GetComponentsInChildren<DynamicBone_Ver02>(true))
                if (d.Comment == "Mune_L" || d.Comment == "Mune_R" || d.Comment == "Siri_L" || d.Comment == "Siri_R") db++;
            var cbb = ctx.Anim.GetComponentInChildren<AIChara.CmpBoneBody>(true);
            if (cbb != null && cbb.dbcBust != null)
                foreach (var c in cbb.dbcBust.Where(c => c)) { c.enabled = false; col++; }
            Log.Add($"male: bust colliders off {col} (CmpBoneBody.dbcBust); bust/hip DynamicBone_Ver02 kept enabled {db} (the game's flags unused)");
        }

        // ---------------------------------------------------------------- body prefab (not a unit: loaded before the flush)
        void BuildBody(EditContext x)
        {
            var ctx = x.Ctx; var S = x.S;
            var body = ctx.Body = EditContext.Spawn(gd.LoadAsset<GameObject>("chara/oo_base.unity3d", S.BodyPrefab), S.BodyPrefab, ctx.BodyTop.transform);
            ctx.BodyDraw = x.Clone(gd.LoadAsset<Material>("chara/mm_base.unity3d", S.BodyMaterial));
            var bodyRend = EditContext.Find(body.transform, S.BodyMesh).GetComponent<SkinnedMeshRenderer>();
            EditContext.SetBaseMaterial(bodyRend, ctx.BodyDraw);   // male: the only slot (no c_m_liquid_body)
            x.Rebind(body, ctx.BodyBones, "cf_J_Root", ctx.Hips);
            x.PartsLoaded++;
            if (x.Male)
            {
                // genital objects stay hidden after "add from card" (OICharInfo.visibleSon = false).
                var dan = EditContext.Find(body.transform, "N_dan");
                if (dan) dan.gameObject.SetActive(false);
                Log.Add($"male: N_dan {(dan ? "inactive" : "not found")}");
            }
            x.Hook(ExtStage.BodyMesh);   // the BodyMesh build hooks (may replace the body mesh, keep the draw material), before the skin is composited
        }

        // ---------------------------------------------------------------- clothes masks (state-dependent part, Phase 3.5)

        /// <summary>"&lt;part&gt;/&lt;renderer&gt;" of the inferred reference renderers (o_bra_a, o_shorts_a, o_panst_a) a worn part lacks.</summary>
        public IReadOnlyList<string> MaskRenderersMissing => (IReadOnlyList<string>)Context?.Edit?.MaskMissing ?? Array.Empty<string>();

        /// <summary>The mask targets with the materials their renderers hold now: body (the draw material), mask_braA,
        /// mask_innerTB, mask_innerB, mask_panst. Every slot holds a material of this character (OwnAll; the URP mapping is in
        /// place since 4.0, C2), so the current slots are the ones the URP shader reads.</summary>
        public List<(string target, Material[] mats)> ClothesMaskTargets()
        {
            var r = Context?.Edit?.MaskRenderers ?? new Renderer[4];
            Material[] Own(Renderer x)
            {
                if (!x) return new Material[0];
                var l = new List<Material>();
                foreach (var m in x.sharedMaterials) if (m) l.Add(m);
                return l.ToArray();
            }
            var body = Live(Context?.BodyRenderer ? new Renderer[] { Context.BodyRenderer } : null, Context?.BodyDraw);
            return new List<(string, Material[])>
            {
                ("body", body ? new[] { body } : new Material[0]),
                ("mask_braA", Own(r[0])), ("mask_innerTB", Own(r[1])),
                ("mask_innerB", Own(r[2])), ("mask_panst", Own(r[3])),
            };
        }

        /// <summary>The body draw material the body renderer holds now. Since 4.0 (C2) the scene's URP mapping works in place,
        /// so this is Context.BodyDraw; the renderer lookup by name stays for builds mapped with copies (OwnAllMaterials off).</summary>
        [Obsolete("Context.BodyDraw is the drawn material since 4.0 (C2)")]
        public Material LiveBodyDraw()
        {
            var stale = Context?.BodyDraw;
            return Live(Context?.BodyRenderer ? new Renderer[] { Context.BodyRenderer } : null, stale);
        }

        /// <summary>The face draw material a head renderer holds now (see <see cref="LiveBodyDraw"/>).</summary>
        [Obsolete("Context.FaceDraw is the drawn material since 4.0 (C2)")]
        public Material LiveFaceDraw()
        {
            var stale = Context?.FaceDraw;
            var h = Context?.Head;
            return Live(h ? h.GetComponentsInChildren<Renderer>(true) : null, stale);
        }

        static Material Live(Renderer[] scope, Material stale)
        {
            if (!stale || scope == null) return stale;
            foreach (var r in scope)
            {
                if (!r) continue;
                var mats = r.sharedMaterials;
                foreach (var m in mats) if (m == stale) return m;
                foreach (var m in mats) if (m && m.name == stale.name) return m;
            }
            return stale;
        }

        /// <summary>
        /// the state-dependent mask values for the clothes states <paramref name="topState"/> / <paramref name="botState"/>
        /// (0 on, 1 half, 2 off), written where the property exists: `_alpha_a` / `_alpha_b` = (1, 1) on, (0, 1) half, (0, 0) off
        /// on the body and bra materials; `_alpha_d` (the lower mask) on body, inner top / bottom and pantyhose from the lower
        /// mask's source as at the build. Returns the number of property writes. The build itself writes the "on" values.
        /// </summary>
        public int ApplyClothesMasks(int topState, int botState)
        {
            var x = Context?.Edit;
            if (x == null) return 0;
            var t = ClothesMaskTargets();
            float a = topState == 0 ? 1 : 0, b = topState == 2 ? 0 : 1;
            bool d = x.MaskLowerType == 0 && x.MaskLowerOk0 && botState == 0 || x.MaskLowerType == 1 && x.MaskLowerOk1 && botState == 0 && !x.MaskNotBot;
            int n = 0;
            void F(Material[] mats, string p, float v) { foreach (var m in mats) if (m && m.HasProperty(p)) { m.SetFloat(p, v); n++; } }
            for (int i = 0; i < 2; i++) { F(t[i].mats, "_alpha_a", a); F(t[i].mats, "_alpha_b", b); }
            foreach (int i in new[] { 0, 2, 3, 4 }) F(t[i].mats, "_alpha_d", d ? 1 : 0);
            return n;
        }

        public static Transform Find(Transform t, string name) => EditContext.Find(t, name);
    }
}
