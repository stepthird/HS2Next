// Shape units: every index of the document array is set, then the engine applies all
// rows (the output does not depend on which index changed). Body index 32 evaluates lerp(0, 0.8, v) (+ 0.2 · the
// scene's nipple stand rate, EditContext.NipStandRate, so the unit and CharaStateRealizer agree); males go through
// ShapeEngine.CardValue (the vanilla range check with SliderRange = Clamp only) and MaleHeightPin075. In an edit the
// body unit refreshes its consequences in this order: the bust stage (softness, weight, size + ResetPosition, HS2's
// reSetupDynamicBoneBust), the animator parameters BodyHeight / BodyBust, the shape-bone guard and the rest pose of the
// shape bones. Shapes are bones only: they never mark a composite.
using System;
using HS2Next.Motion.Animation;
using HS2Next.Motion.DynamicBones;
using UnityEngine;

namespace HS2Next.Chara.Edit.Units
{
    public sealed class ShapeBodyUnit : UnitBase
    {
        internal static HS2Next.Chara.Ext.BoneModifierApplier Bones(EditContext x) =>
            x.Ctx.Bones ? x.Ctx.Bones : x.Root ? x.Root.GetComponent<HS2Next.Chara.Ext.BoneModifierApplier>() : null;

        public ShapeBodyUnit() : base(UnitMap.ShapeBody, EditStep.Shapes, 0, "body.shape") { }

        public override void Apply(EditContext x)
        {
            var shape = x.Ctx.BodyShape;
            if (shape == null) return;
            var src = x.Doc.Body.Shape ?? new float[0];
            var bv = (float[])src.Clone();
            if (x.Male)
            {
                // The range check and the height pin (option MaleHeightPin075, default off = card value).
                // The slider extender skips the range check (v0.2 measurement): the clamp only with SliderRange = Clamp.
                for (int i = 0; i < bv.Length; i++) bv[i] = ShapeEngine.CardValue(bv[i], true);
                if (bv.Length > 0 && CharaAssembler.MaleHeightPin075) bv[0] = 0.75f;
            }
            float nip = x.InBuild ? 0f : x.NipStandRate;
            if (!x.InBuild) Bones(x)?.RestoreBases();   // the modifiers go on top of the new shape values (ApplyNow below)
            for (int i = 0; i < bv.Length; i++) shape.SetValue(i, i == 32 ? Mathf.Lerp(0, 0.8f, bv[i]) + 0.2f * nip : bv[i]);
            shape.Apply();
            x.Log.Add($"body shape: {shape.Summary}");
            if (x.InBuild) return;
            if (x.Report != null) x.Report.BodyShapeChanged = true;
            // consequences
            x.MarkBust();
            x.MarkDynBone(false);   // chain lengths and object scales follow the shape bones
            Bones(x)?.ApplyNow();
            // animator parameters, shape-bone guard and rest pose after the plugin final stage (step 9, AnimRefreshUnit):
            // the bone modifiers write on top of the shapes, and those captures hold the bones without them, as in a fresh build
            x.Editor.MarkUnit(UnitMap.AnimRefresh);
        }
    }

    public sealed class ShapeFaceUnit : UnitBase
    {
        public ShapeFaceUnit() : base(UnitMap.ShapeFace, EditStep.Shapes, 10, "face.shape") { }

        public override void Apply(EditContext x)
        {
            var shape = x.Ctx.FaceShape;
            if (shape == null) return;
            var fv = (float[])(x.Doc.Face.Shape ?? new float[0]).Clone();
            if (x.Male)
            {
                if (fv.Length > 59) Array.Resize(ref fv, 59);
                for (int i = 0; i < fv.Length; i++) fv[i] = ShapeEngine.CardValue(fv[i], true);
            }
            if (!x.InBuild) ShapeBodyUnit.Bones(x)?.RestoreBases();
            for (int i = 0; i < fv.Length; i++) shape.SetValue(i, fv[i]);
            if (!x.InBuild) HS2Next.Motion.Face.CharaExpression.Of(x.Root)?.OnFaceShapeValues(fv);
            shape.Apply();
            x.Log.Add($"face shape: {shape.Summary}");
            if (x.InBuild) return;
            if (x.Report != null) x.Report.FaceShapeChanged = true;
            ShapeBodyUnit.Bones(x)?.ApplyNow();
            x.Editor.MarkUnit(UnitMap.AnimRefresh);   // the expression stage writes the mouth width again
            x.MarkDynBone(false);   // hair chains hang on the head bones
        }
    }

    /// <summary>Step 9 (edits): CharaAnimation's body-shape captures (BodyHeight / BodyBust, the shape-bone guard, the rest
    /// pose of the shape bones) after shapes and bone modifiers, as CharaAnimation.Bind takes them after a build's final stage.</summary>
    public sealed class AnimRefreshUnit : UnitBase
    {
        public AnimRefreshUnit() : base(UnitMap.AnimRefresh, EditStep.Motion, -10) { }

        public override void Apply(EditContext x)
        {
            if (x.InBuild) return;
            var anim = CharaAnimation.Of(x.Root);
            if (anim) anim.RefreshBodyShape(x.Ctx);
            // shapes or bone modifiers wrote the mouth width bone: the expression stage writes its width again
            if (x.Ctx.Motion != null)
                foreach (var s in x.Ctx.Motion.GetAll<HS2Next.Motion.Face.ExpressionStage>()) s.InvalidateMouthWidth();
        }
    }

    /// <summary>Bust physics values (step 9; edits only: the build's MotionSetup creates the stage with them).</summary>
    public sealed class BustUnit : UnitBase
    {
        public BustUnit() : base(UnitMap.Bust, EditStep.Motion, 0, "body.bust", "body.shape") { }

        public override void Apply(EditContext x)
        {
            if (x.InBuild) return;
            var bust = x.Ctx.Motion != null ? x.Ctx.Motion.Get<BustStage>() : null;
            if (bust == null) return;
            var b = x.Doc.Body;
            var shape = b.Shape;
            float size = shape != null && shape.Length > 1 ? shape[1] : 0.5f;
            size = ShapeEngine.CardValue(size, x.Male);
            if (x.BustRederive)
            {
                // the chains re-derived from the shaped bones, as the stage is built after the shapes (then the card values)
                x.BustRederive = false;
                bust.BustSoftness = b.Bust.Softness; bust.BustWeight = b.Bust.Weight; bust.BustSize = size;
                // the chain bones back to their rest pose first (the bust physics moved them since the build), as the stage reads
                // them when it is created
                foreach (var c in bust.All) c?.InitTransforms();
                // re-derived as the stage's constructor does (hip chains take their rest offsets from the bind pose), then
                // ResetPosition with the card values as CreateBustStage ends; the stage object stays (FK holds it)
                GameObject prefab = null;
                if (bust.Options.HipRestFromBindPose && x.Catalog != null)
                    prefab = x.Catalog.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim") ?? x.Catalog.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_anim");
                var pcs = prefab ? prefab.GetComponentsInChildren<global::DynamicBone_Ver02>(true) : null;
                foreach (var c in bust.All)
                {
                    if (c == null) continue;
                    c.Build();
                    if (c.Kind >= 2 && pcs != null)
                        foreach (var pv in pcs) if (c.Source && pv.Comment == c.Source.Comment) { c.CaptureLocalPositionFrom(pv); break; }
                }
                bust.ResetPosition();
                bust.Reset();   // particles at the re-derived rest pose
            }
            else
            {
                bust.SetCardValues(b.Bust.Softness, b.Bust.Weight, size);
                bust.ResetPosition();
            }
        }
    }
}
