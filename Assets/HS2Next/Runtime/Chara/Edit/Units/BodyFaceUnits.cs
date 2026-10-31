// Body and face units: head (part swap), skin.body / skin.face (compositing passes),
// draw.body / draw.face (draw material values), eyes (eye, eye-shadow and eyelash materials). The writes are the
// pre-4.0 BuildBody / BuildHead writes, reading the documents.
using System.Collections.Generic;
using AIChara;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;
using EM = HS2Next.Chara.Edit.EditMaterials;

namespace HS2Next.Chara.Edit.Units
{
    public abstract class UnitBase : EditUnit
    {
        readonly string id;
        readonly EditStep step;
        readonly int order;
        readonly string[] paths;

        protected UnitBase(string id, EditStep step, int order, params string[] paths)
        {
            this.id = id; this.step = step; this.order = order; this.paths = paths ?? new string[0];
        }

        public override string Id => id;
        public override EditStep Step => step;
        public override int Order => order;
        public override IReadOnlyList<string> Paths => paths;

        protected static string N(int i) => i.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------------------------------------ skin.body

    public sealed class SkinBodyUnit : UnitBase
    {
        public override bool LoadsAllEachRun => true;
        public SkinBodyUnit() : base(UnitMap.SkinBody, EditStep.Composites, 0, "body.skin.item", "body.skin.color", "body.sunburn", "body.paint") { }

        public override void Apply(EditContext x)
        {
            var b = x.Doc.Body;
            var S = x.S;
            var skin = x.Row(S.SkinB, b.Skin.Item, "ChaFileBody.skinId", PartKeys.BodySkin, "body.skin.item");
            var sunburn = x.Row(S.Sunburn, b.Sunburn.Item, "ChaFileBody.sunburnId", PartKeys.BodySunburn, "body.sunburn.item");
            bool fresh = x.BodySkin == null;
            if (fresh)
            {
                x.BodySkin = new CompositeInputs("create_skin_body", new TexCreator(x.Catalog, "create_skin_body", 4096, 4096));
                x.BodyDetail = new CompositeInputs("create_skin detail_body", new TexCreator(x.Catalog, "create_skin detail_body", 4096, 4096));
                x.Ctx.BodySkin = x.BodySkin.Creator; x.Ctx.BodyDetail = x.BodyDetail.Creator;
                // the paint UV / rotation as the creators were made: written back for a region that does not resolve (GB5, Y11)
                foreach (var c in new[] { x.BodySkin, x.BodyDetail }) c.CaptureInitial(Paints.RegionVectors, Paints.RegionFloats);
            }
            var c0 = x.BodySkin.Creator; var c1 = x.BodyDetail.Creator;
            c0.Main = x.Tex(skin, "MainAB", "MainTex"); x.BodySkin.Ref("Main", skin, "MainAB", "MainTex");
            c1.Main = x.Catalog.LoadAsset<Texture2D>("chara/etc.unity3d", "black4096"); x.BodyDetail.Ref("Main", null, "chara/etc.unity3d", "black4096");
            c0.Mat.SetColor("_Color", EditContext.U(b.Skin.Color));
            c0.Mat.SetTexture("_Texture7", x.Tex(sunburn, "MainAB", "AddTex")); x.BodySkin.Ref("_Texture7", sunburn, "MainAB", "AddTex");
            c0.Mat.SetColor("_Color7", EditContext.U(b.Sunburn.Color));
            Paints.Apply(x, b.Paint, x.BodySkin, x.BodyDetail, face: false);
            var draw = x.Ctx.BodyDraw;
            // every input is written (above); only the creators whose inputs changed are rebuilt (
            // a creator's RT is a function of its own material, main texture and layers only)
            UnitMap.SkinCreators(Id, DirtyPaths, DirtyAll || fresh || x.InBuild, out bool col, out bool det);
            if (col) x.Rebuild(x.BodySkin, draw, "_MainTex", fresh);
            if (det) x.Rebuild(x.BodyDetail, draw, "_DetailMainTex", fresh);
        }
    }

    /// <summary>Body and face paints (Custom paint layers 5 / 6 of the skin passes). A body paint whose region row (category 8,
    /// `layoutItem`) resolves gets the layout's UV / rotation on both creators; one that does not resolve gets the create
    /// materials' initial values (Y11), so an edit equals a fresh build. The region row
    /// is recorded as an input of both creators (`_Texture5UV` / `_Texture6UV`, column "region"; "initial" when unresolved).</summary>
    static class Paints
    {
        public static readonly string[] RegionVectors = { "_Texture5UV", "_Texture6UV" };
        public static readonly string[] RegionFloats = { "_Texture5Rotator", "_Texture6Rotator" };

        public static void Apply(EditContext x, Paint[] paints, CompositeInputs c0i, CompositeInputs c1i, bool face)
        {
            var c0 = c0i.Creator; var c1 = c1i.Creator;
            string doc = face ? "face.makeup.paint" : "body.paint";
            for (int i = 0; i < 2; i++)
            {
                string pp = doc + "[" + i + "]";
                var p = paints != null && i < paints.Length ? paints[i] : null;
                if (p == null) continue;
                if (face) x.Register("chara:face.makeup.paint[" + i + "].layoutItem", p.LayoutItem, 7, pp + ".layoutItem");
                var e = x.Row(313, p.Item, null, face ? PartKeys.FacePaint(i) : PartKeys.BodyPaint(i), pp + ".item");
                string n = i == 0 ? "5" : "6";
                c0.Mat.SetTexture("_Texture" + n, x.Tex(e, "MainAB", "AddTex")); c0i.Ref("_Texture" + n, e, "MainAB", "AddTex");
                c1.Mat.SetTexture("_Texture" + n, x.Tex(e, "MainAB", "GlossTex")); c1i.Ref("_Texture" + n, e, "MainAB", "GlossTex");
                c0.Mat.SetColor("_Color" + n, EditContext.U(p.Color));
                c1.Mat.SetFloat("_Gloss" + n, p.Gloss);
                c1.Mat.SetFloat("_Metallic" + n, p.Metallic);
                var lay = EditContext.U(p.Layout);
                float rot = Mathf.Lerp(1, -1, p.Rotation);
                Vector4 uv;
                if (face)
                    uv = new Vector4(Mathf.Lerp(10, 1, lay.x), Mathf.Lerp(10, 1, lay.y), Mathf.Lerp(0.28f, -0.3f, lay.z), Mathf.Lerp(0.28f, -0.3f, lay.w));
                else
                {
                    var L = x.Row(8, p.LayoutItem, null, PartKeys.BodyPaintLayout(i), pp + ".layoutItem");
                    if (L == null)
                    {
                        // GB5: the creators' initial values (a fresh build's), not whatever the last resolving region wrote
                        foreach (var ci in new[] { c0i, c1i })
                        {
                            ci.RestoreVector("_Texture" + n + "UV");
                            ci.RestoreFloat("_Texture" + n + "Rotator");
                            ci.Ref("_Texture" + n + "UV", null, null, "initial");
                        }
                        continue;
                    }
                    foreach (var ci in new[] { c0i, c1i }) ci.Ref("_Texture" + n + "UV", L, null, "region");
                    float cx = EditContext.F(L["CenterX"]), mx = EditContext.F(L["MoveX"]), cy = EditContext.F(L["CenterY"]), my = EditContext.F(L["MoveY"]), cs = EditContext.F(L["CenterScale"]), a = EditContext.F(L["AddScale"]);
                    uv = new Vector4(Mathf.Lerp(cs + a, cs - a, lay.x), Mathf.Lerp(cs + a, cs - a, lay.y), Mathf.Lerp(cx + mx, cx - mx, lay.z), Mathf.Lerp(cy + my, cy - my, lay.w));
                }
                foreach (var c in new[] { c0, c1 }) { c.Mat.SetVector("_Texture" + n + "UV", uv); c.Mat.SetFloat("_Texture" + n + "Rotator", rot); }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ draw.body

    public sealed class DrawBodyUnit : UnitBase
    {
        public override bool LoadsAllEachRun => true;
        public DrawBodyUnit() : base(UnitMap.DrawBody, EditStep.Materials, 0, "body.detail", "body.nipple", "body.underhair", "body.nail", "body.skin.gloss", "body.skin.metallic") { }

        public override void Apply(EditContext x)
        {
            var b = x.Doc.Body;
            var d = x.Ctx.BodyDraw;
            if (!d) return;
            var detail = x.Row(x.S.DetailB, b.Detail.Item, "ChaFileBody.detailId", PartKeys.BodyDetail, "body.detail.item");
            EM.SetTexture(d, "_OcclusionMap", x.Tex(detail, "MainAB", "OcclusionMapTex"));
            EM.SetTexture(d, "_BumpMap2", x.Tex(detail, "MainAB", "NormalMapTex"));
            EM.SetFloat(d, "_Gloss", Mathf.Lerp(0, 0.8f, b.Skin.Gloss));
            EM.SetFloat(d, "_Metallic", b.Skin.Metallic);
            EM.SetFloat(d, "_BumpScale2", b.Detail.Power);
            EM.SetTexture(d, "_Texture2", x.Tex(x.Row(334, b.Nipple.Item, "ChaFileBody.nipId", PartKeys.BodyNipple, "body.nipple.item"), "MainAB", "AddTex"));
            EM.SetColor(d, "_Color2", EditContext.U(b.Nipple.Color));
            EM.SetFloat(d, "_NipGloss", b.Nipple.Gloss);
            EM.SetFloat(d, "_NipScale", b.Nipple.Areola);
            EM.SetTexture(d, "_Texture3", x.Tex(x.Row(335, b.Underhair.Item, "ChaFileBody.underhairId", PartKeys.BodyUnderhair, "body.underhair.item"), "MainAB", "AddTex"));
            EM.SetColor(d, "_Color3", EditContext.U(b.Underhair.Color));
            EM.SetColor(d, "_Color13", EditContext.U(b.Nail.Color));
            EM.SetFloat(d, "_NailGloss", b.Nail.Gloss);
            // _alpha_a.._d / _AlphaMask(2) are written by the masks unit after the clothes load.
            if (x.Report != null) x.Report.BodyDrawChanged = true;
            x.Touched(EditObject.Character);
        }
    }

    // ------------------------------------------------------------------------------------------------ head

    public sealed class HeadUnit : UnitBase
    {
        ListEntry appliedRow;
        string appliedAB;

        public HeadUnit() : base(UnitMap.Head, EditStep.Parts, 0, "face.head") { }

        public override void Apply(EditContext x)
        {
            var S = x.S;
            var f = x.Doc.Face;
            var he = x.Row(S.Head, f.Head, "ChaFileFace.headId", PartKeys.FaceHead, "face.head") ?? x.Lookup(S.Head, 0, null, null);
            string headAB = he["MainAB"];
            if (!x.Male && he.Key == ContentId.Vanilla(0)) headAB = "chara/35/fo_head_35.unity3d"; // Studio override, female 210 only
            if (x.Ctx.Head && he == appliedRow && headAB == appliedAB) return;
            var old = x.Ctx.Head;
            bool swapped = old;   // (the old head is destroyed below; a destroyed object compares to null)
            int sibling = old ? old.transform.GetSiblingIndex() : -1;
            // the old face draw material goes with the old head (it is the old o_head slot; ReleasePart keeps Ctx.FaceDraw)
            if (old) { x.Ctx.FaceDraw = null; x.ReleasePart(old, UnitMap.Head); }
            var gd = x.Catalog;
            var headBone = x.Ctx.HeadBone;
            x.BeginPartLoad();
            var prefab = gd.LoadAssetAt<GameObject>(he, headAB, he[x.DataKey(he)]);   // through the texture guard, like LoadPart
            var head = EditContext.Spawn(prefab, "ct_head", null);
            x.PartsLoaded++;
            if (swapped)
            {
                // bone modifiers off first (their bases were taken on the old head), then the head bones back to the
                // p_cf_head_bone rest pose (bones the new head prefab does not carry keep
                // the old head's values otherwise), then the new head's own values, as a fresh build
                ShapeBodyUnit.Bones(x)?.RestoreBases();
                var hbPrefab = gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_head_bone");
                if (hbPrefab) EditContext.CopySameNameTransforms(headBone.transform, hbPrefab.transform);
            }
            EditContext.CopySameNameTransforms(headBone.transform, head.transform);
            head.transform.SetParent(headBone.transform, false);
            if (sibling >= 0) head.transform.SetSiblingIndex(sibling);
            x.Rebind(head, x.Ctx.HeadBones, "cf_J_FaceRoot", null);

            var drawSrc = gd.LoadAssetAt<Material>(he, headAB, he["MatData"]);
            var faceDraw = x.Clone(drawSrc);
            var oHead = EditContext.Find(head.transform, "o_head")?.GetComponent<SkinnedMeshRenderer>();
            if (oHead) EditContext.SetBaseMaterial(oHead, faceDraw);
            x.Ctx.Head = head;
            x.Ctx.FaceDraw = faceDraw;
            DefaultExpression(head);

            // Face shape keys come from the head bundle's ShapeAnime.
            var keys = gd.LoadAssetAt<TextAsset>(he, headAB, he["ShapeAnime"]);
            var info = gd.LoadAsset<TextAsset>("list/customshape.unity3d", S.FaceSliderList);
            // The engine binds its rows by name under the head bone. In a fresh build the hair parts and accessories are not
            // loaded yet; in an edit they are taken out while it binds (a part may carry a bone of the same name).
            var outside = new List<(Transform t, Transform parent, int sibling)>();
            if (swapped)
            {
                var objs = new List<GameObject>(x.Ctx.Hair);
                foreach (var a in x.Ctx.Accessories) if (a != null) objs.Add(a.Object);
                foreach (var o in objs)
                    if (o && o.transform.IsChildOf(headBone.transform))
                    {
                        outside.Add((o.transform, o.transform.parent, o.transform.GetSiblingIndex()));
                        o.transform.SetParent(null, false);
                    }
            }
            try { x.Ctx.FaceShape = keys && info ? ShapeEngine.Face(headBone.transform, keys.bytes, info.text) : null; }
            finally
            {
                for (int i = outside.Count - 1; i >= 0; i--)
                {
                    var (t, parent, sib) = outside[i];
                    t.SetParent(parent, false);
                    t.SetSiblingIndex(sib);
                }
            }
            x.EndPartLoad(UnitMap.Head);
            x.Log.Add($"head: {x.Name(he)}");
            appliedRow = he; appliedAB = headAB;

            // The body shape rows bind their bones by name right after the head, before hair, clothes and accessories are
            // attached, as HS2 sets the body shape up when the body loads: a part that carries its own copy of the body rig
            // (an accessory suit with an Armature/p_cm_body_00, found in the v0.2 HS2 comparison) would otherwise take the
            // rows (depth-first search).
            if (x.Ctx.BodyShape == null)
            {
                var bk = gd.LoadAsset<TextAsset>("list/customshape.unity3d", "cf_anmShapeBody");
                var bi = gd.LoadAsset<TextAsset>("list/customshape.unity3d", "cf_custombody");
                if (bk && bi) x.Ctx.BodyShape = ShapeEngine.Body(EditContext.Find(x.Ctx.Anim.transform, "cf_J_Root"), bk.bytes, bi.text);
            }
            if (swapped)
            {
                // The face creators stay; the new draw material gets their RTs.
                if (x.FaceSkin != null) faceDraw.SetTexture("_MainTex", x.FaceSkin.Creator.Target);
                if (x.FaceDetail != null) faceDraw.SetTexture("_DetailMainTex", x.FaceDetail.Creator.Target);
                x.Editor.MarkUnit(UnitMap.SkinFace);
                x.Editor.MarkUnit(UnitMap.DrawFace);
                x.Editor.MarkUnit(UnitMap.Eyes);
                x.Editor.MarkUnit(UnitMap.ShapeFace);
                x.Editor.MarkUnit(UnitMap.ExtFinal);
                x.PartSwapped(head, EditObject.Character);
                if (x.Report != null) x.Report.HeadSwapped = true;
            }
        }

        // Static pre-stage default (eyebrow/eyes Open = 100, mouth Close = 100). Superseded by the expression stage
        // (Runtime/Motion/Face, attached by MotionSetup), which rewrites every table index at attach; this only remains
        // the face of characters built with MotionSetup.Enabled / FaceSetup.Enabled off. It reads the Assembly-CSharp
        // compat class only, so heads whose FaceBlendShape comes from the IL assembly keep their imported weights here.
        static void DefaultExpression(GameObject head)
        {
            var fbs = head.GetComponent<FaceBlendShape>();
            if (fbs == null) return;
            void Set(List<FaceBlendShape.FBSTargetInfo_S> targets, bool open)
            {
                if (targets == null) return;
                foreach (var t in targets)
                {
                    var smr = t.ObjTarget ? t.ObjTarget.GetComponent<SkinnedMeshRenderer>() : null;
                    if (!smr || t.PtnSet == null || t.PtnSet.Count == 0) continue;
                    foreach (var p in t.PtnSet)
                    {
                        if (p.Close >= 0 && p.Close < smr.sharedMesh.blendShapeCount) smr.SetBlendShapeWeight(p.Close, 0);
                        if (p.Open >= 0 && p.Open < smr.sharedMesh.blendShapeCount) smr.SetBlendShapeWeight(p.Open, 0);
                    }
                    int idx = open ? t.PtnSet[0].Open : t.PtnSet[0].Close;
                    if (idx >= 0 && idx < smr.sharedMesh.blendShapeCount) smr.SetBlendShapeWeight(idx, 100);
                }
            }
            Set(fbs.EyebrowCtrl?.FBSTarget, true);
            Set(fbs.EyesCtrl?.FBSTarget, true);
            Set(fbs.MouthCtrl?.FBSTarget, false);
        }
    }

    // ------------------------------------------------------------------------------------------------ skin.face

    public sealed class SkinFaceUnit : UnitBase
    {
        public override bool LoadsAllEachRun => true;
        public SkinFaceUnit() : base(UnitMap.SkinFace, EditStep.Composites, 10, "face.skin", "face.makeup", "face.mole", "body.skin.color") { }

        public override void Apply(EditContext x)
        {
            var f = x.Doc.Face;
            var faceDraw = x.Ctx.FaceDraw;
            var skin = x.Row(x.S.SkinF, f.Skin, "ChaFileFace.skinId", PartKeys.FaceSkin, "face.skin");
            bool fresh = x.FaceSkin == null;
            if (fresh)
            {
                x.FaceSkin = new CompositeInputs("create_skin_face", new TexCreator(x.Catalog, "create_skin_face", 2048, 2048));
                x.FaceDetail = new CompositeInputs("create_skin detail_face", new TexCreator(x.Catalog, "create_skin detail_face", 2048, 2048));
                x.Ctx.FaceSkin = x.FaceSkin.Creator; x.Ctx.FaceDetail = x.FaceDetail.Creator;
            }
            var c0i = x.FaceSkin; var c1i = x.FaceDetail;
            var c0 = c0i.Creator; var c1 = c1i.Creator;
            c0.Main = x.Tex(skin, "MainAB", "MainTex"); c0i.Ref("Main", skin, "MainAB", "MainTex");
            c1.Main = x.Catalog.LoadAsset<Texture2D>("chara/etc.unity3d", "black2048"); c1i.Ref("Main", null, "chara/etc.unity3d", "black2048");
            EM.SetTexture(faceDraw, "_OcclusionMap", x.Tex(skin, "MainAB", "OcclusionMapTex"));
            EM.SetTexture(faceDraw, "_BumpMap", x.Tex(skin, "MainAB", "NormalMapTex"));
            c0.Mat.SetColor("_Color", EditContext.U(x.Doc.Body.Skin.Color));
            var mk = f.Makeup;
            var es = x.Row(316, mk.Eyeshadow.Item, null, PartKeys.FaceEyeshadow, "face.makeup.eyeshadow.item");
            Layer(x, c0i, c1i, "11", es, mk.Eyeshadow);
            Paints.Apply(x, mk.Paint, c0i, c1i, face: true);
            var ch = x.Row(320, mk.Cheek.Item, null, PartKeys.FaceCheek, "face.makeup.cheek.item");
            Layer(x, c0i, c1i, "10", ch, mk.Cheek);
            var lp = x.Row(322, mk.Lip.Item, null, PartKeys.FaceLip, "face.makeup.lip.item");
            Layer(x, c0i, c1i, "9", lp, mk.Lip);
            var mo = x.Row(323, f.Mole.Item, null, PartKeys.FaceMole, "face.mole.item");
            c0.Mat.SetTexture("_Texture12", x.Tex(mo, "MainAB", "AddTex")); c0i.Ref("_Texture12", mo, "MainAB", "AddTex");
            c0.Mat.SetColor("_Color12", EditContext.U(f.Mole.Color));
            var ml = EditContext.U(f.Mole.Layout);
            c0.Mat.SetVector("_Texture12UV", new Vector4(Mathf.Lerp(5, 1, ml.x), Mathf.Lerp(5, 1, ml.y), Mathf.Lerp(0.3f, -0.3f, ml.z), Mathf.Lerp(0.3f, -0.3f, ml.w)));
            // every input is written (above); only the creators whose inputs changed are rebuilt
            // with HS2's per-layer dirty kinds (a colour drag rebuilds one 2048² RT)
            UnitMap.SkinCreators(Id, DirtyPaths, DirtyAll || fresh || x.InBuild, out bool col, out bool det);
            if (col) x.Rebuild(c0i, faceDraw, "_MainTex", fresh);
            if (det) x.Rebuild(c1i, faceDraw, "_DetailMainTex", fresh);
        }

        static void Layer(EditContext x, CompositeInputs c0i, CompositeInputs c1i, string n, ListEntry e, MakeupLayer l)
        {
            c0i.Creator.Mat.SetTexture("_Texture" + n, x.Tex(e, "MainAB", "AddTex")); c0i.Ref("_Texture" + n, e, "MainAB", "AddTex");
            c1i.Creator.Mat.SetTexture("_Texture" + n, x.Tex(e, "MainAB", "GlossTex")); c1i.Ref("_Texture" + n, e, "MainAB", "GlossTex");
            c0i.Creator.Mat.SetColor("_Color" + n, EditContext.U(l.Color));
            c1i.Creator.Mat.SetFloat("_Gloss" + n, l.Gloss);
        }
    }

    // ------------------------------------------------------------------------------------------------ draw.face

    public sealed class DrawFaceUnit : UnitBase
    {
        public override bool LoadsAllEachRun => true;
        public DrawFaceUnit() : base(UnitMap.DrawFace, EditStep.Materials, 10, "face.detail", "face.eyebrow", "face.beard", "body.skin.gloss", "body.skin.metallic") { }

        public override void Apply(EditContext x)
        {
            var f = x.Doc.Face;
            var b = x.Doc.Body;
            var faceDraw = x.Ctx.FaceDraw;
            if (!x.Male) x.Register(PartKeys.FaceBeard, f.Beard.Item, 121, "face.beard.item");
            if (!faceDraw) return;
            EM.SetFloat(faceDraw, "_Gloss", Mathf.Lerp(0, 0.8f, b.Skin.Gloss));
            EM.SetFloat(faceDraw, "_Metallic", b.Skin.Metallic);
            EM.SetTexture(faceDraw, "_BumpMap2", x.Tex(x.Row(x.S.DetailF, f.Detail.Item, "ChaFileFace.detailId", PartKeys.FaceDetail, "face.detail.item"), "MainAB", "AddTex"));
            if (x.Male)
            {
                // the beard is a draw-material texture (not a compositing layer);
                // _Texture5UV keeps the material's stored value. id 0 = none -> null texture.
                var be = x.Row(x.S.Beard, f.Beard.Item, "ChaFileFace.beardId", PartKeys.FaceBeard, "face.beard.item");
                EM.SetTexture(faceDraw, "_Texture5", x.Tex(be, "MainAB", "AddTex"));
                EM.SetColor(faceDraw, "_Color5", EditContext.U(f.Beard.Color));
                x.Log.Add($"beard: {x.Name(be)}{be?["AddTex"]}");
            }
            EM.SetFloat(faceDraw, "_BumpScale2", f.Detail.Power);
            EM.SetTexture(faceDraw, "_Texture3", x.Tex(x.Row(314, f.Eyebrow.Item, null, PartKeys.FaceEyebrow, "face.eyebrow.item"), "MainAB", "AddTex"));
            EM.SetColor(faceDraw, "_Color3", EditContext.U(f.Eyebrow.Color));
            var el = EditContext.U(f.Eyebrow.Layout);
            var cur = faceDraw.HasProperty("_Texture3UV") ? faceDraw.GetVector("_Texture3UV") : Vector4.zero;
            EM.SetVector(faceDraw, "_Texture3UV", new Vector4(Mathf.Lerp(-0.2f, 0.2f, el.x), Mathf.Lerp(0.16f, 0, el.y), Mathf.Lerp(2, 0.5f, el.z), Mathf.Lerp(2, 0.5f, el.w)));
            EM.SetFloat(faceDraw, "_Texture3Rotator", Mathf.Lerp(-0.15f, 0.15f, f.Eyebrow.Tilt));
            // _Gloss was rewritten: the scene's gloss state goes on top again (CharaStateRealizer)
            if (x.Report != null) x.Report.FaceDrawChanged = true;
            x.Touched(EditObject.Character);
        }
    }

    // ------------------------------------------------------------------------------------------------ eyes

    public sealed class EyesUnit : UnitBase
    {
        public override bool LoadsAllEachRun => true;
        public EyesUnit() : base(UnitMap.Eyes, EditStep.Materials, 20, "face.eyes", "face.eyelashes") { }

        public override void Apply(EditContext x)
        {
            var f = x.Doc.Face;
            var head = x.Ctx.Head;
            // every reference of the group is registered, whether or not the head has the renderer
            for (int i = 0; i < 2; i++)
            {
                var e = i == 0 ? f.Eyes.Left : f.Eyes.Right;
                string p = i == 0 ? "face.eyes.left" : "face.eyes.right";
                x.Register(PartKeys.EyeIris(i), e.Iris.Item, 317, p + ".iris.item");
                x.Register(PartKeys.EyePupil(i), e.Pupil.Item, 318, p + ".pupil.item");
            }
            x.Register(PartKeys.FaceHighlight, f.Eyes.Highlight.Item, 319, "face.eyes.highlight.item");
            x.Register(PartKeys.FaceEyelashes, f.Eyelashes.Item, 315, "face.eyelashes.item");
            if (!head) return;
            for (int i = 0; i < 2; i++)
            {
                var eye = EditContext.Find(head.transform, i == 0 ? "o_eyebase_L" : "o_eyebase_R")?.GetComponent<Renderer>();
                var m = eye ? x.OwnMaterial(eye) : null;
                if (!m) continue;
                var p = i == 0 ? f.Eyes.Left : f.Eyes.Right;
                string pp = i == 0 ? "face.eyes.left" : "face.eyes.right";
                EM.SetColor(m, "_Color", EditContext.U(p.White));
                EM.SetTexture(m, "_Texture2", x.Tex(x.Row(317, p.Iris.Item, null, PartKeys.EyeIris(i), pp + ".iris.item"), "MainAB", "AddTex"));
                var uv2 = m.GetVector("_texture2uv");
                EM.SetVector(m, "_texture2uv", new Vector4(Mathf.Lerp(2, 0.5f, p.Iris.Size.X), Mathf.Lerp(2, 0.5f, p.Iris.Size.Y), uv2.z, Mathf.Lerp(0.5f, -0.5f, f.Eyes.OffsetY)));
                EM.SetColor(m, "_Color2", EditContext.U(p.Iris.Color));
                EM.SetFloat(m, "_Emission", p.Iris.Emission);
                EM.SetTexture(m, "_Texture3", x.Tex(x.Row(318, p.Pupil.Item, null, PartKeys.EyePupil(i), pp + ".pupil.item"), "MainAB", "AddTex"));
                EM.SetColor(m, "_Color3", EditContext.U(p.Pupil.Color));
                var uv3 = m.GetVector("_texture3uv");
                EM.SetVector(m, "_texture3uv", new Vector4(Mathf.Lerp(4, 0.4f, p.Pupil.Size.X), Mathf.Lerp(4, 0.4f, p.Pupil.Size.Y), uv3.z, uv3.w));
                EM.SetTexture(m, "_Texture4", x.Tex(x.Row(319, f.Eyes.Highlight.Item, null, PartKeys.FaceHighlight, "face.eyes.highlight.item"), "MainAB", "AddTex"));
                EM.SetColor(m, "_Color4", EditContext.U(f.Eyes.Highlight.Color));
                var hl = EditContext.U(f.Eyes.Highlight.Layout);
                EM.SetVector(m, "_Texture4UV", new Vector4(Mathf.Lerp(1.8f, 0.2f, hl.x), Mathf.Lerp(1.8f, 0.2f, hl.y), Mathf.Lerp(-0.3f, 0.3f, hl.z), Mathf.Lerp(-0.3f, 0.3f, hl.w)));
                EM.SetFloat(m, "_Texture4Rotator", Mathf.Lerp(-1, 1, f.Eyes.Highlight.Tilt));
                // _Smoothness is the expression stage's (FaceMaterials: highlight hide); the build writes its start value.
                if (x.InBuild || (x.Report != null && x.Report.HeadSwapped)) EM.SetFloat(m, "_Smoothness", 1);
            }
            var shadow = EditContext.Find(head.transform, "o_eyeshadow")?.GetComponent<Renderer>();
            var shadowMat = shadow ? x.OwnMaterial(shadow) : null;
            if (shadowMat) EM.SetFloat(shadowMat, "_ShadowScale", Mathf.Lerp(0.1f, 0.9f, f.Eyes.ShadowScale));
            var lashes = EditContext.Find(head.transform, "o_eyelashes")?.GetComponent<Renderer>();
            var lashMat = lashes ? x.OwnMaterial(lashes) : null;
            if (lashMat)
            {
                EM.SetTexture(lashMat, "_MainTex", x.Tex(x.Row(315, f.Eyelashes.Item, null, PartKeys.FaceEyelashes, "face.eyelashes.item"), "MainAB", "AddTex"));
                EM.SetColor(lashMat, "_Color", EditContext.U(f.Eyelashes.Color));
            }
            if (x.Report != null) x.Report.EyesChanged = true;
            x.Touched(EditObject.Character);
        }
    }
}
