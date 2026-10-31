// The remaining units: profile (root name), the plugin
// texture stage (KSOX, KCOX, MaterialEditor), the plugin final stage (pushUp stored, AAC, ABMX, DBE, InvisibleBody),
// ownership (every renderer slot holds a material of the character; in an edit only the new subtrees, then the URP
// mapping in place) and the dynamic bones (an edit rebuilds the chains in the order of a full build).
using System.Linq;
using HS2Next.Chara.Ext;
using HS2Next.Motion.DynamicBones;
using UnityEngine;

namespace HS2Next.Chara.Edit.Units
{
    public sealed class ProfileUnit : UnitBase
    {
        public ProfileUnit() : base(UnitMap.Profile, EditStep.Parts, -10, "profile.name") { }

        public override void Apply(EditContext x)
        {
            if (!x.Root) return;
            string name = x.S.RootPrefix + EditContext.Safe(x.Doc.Profile?.Name ?? "");
            if (x.Root.name != name) x.Root.name = name;
        }
    }

    /// <summary>Step 5: the build runs the texture stage of Runtime/Chara/Ext as one hook; an edit re-applies the plugin
    /// data of the objects the flush touched.</summary>
    public sealed class ExtTexturesUnit : UnitBase
    {
        public ExtTexturesUnit() : base(UnitMap.ExtTextures, EditStep.PluginTextures, 0) { }

        public override void Apply(EditContext x)
        {
            if (x.InBuild) { x.BuildTextures(); return; }
            x.ReapplyTextures();
        }
    }

    /// <summary>Step 7: pushUp (stored), AAC startup rules, ABMX, DBE, InvisibleBody.</summary>
    public sealed class ExtFinalUnit : UnitBase
    {
        public ExtFinalUnit() : base(UnitMap.ExtFinal, EditStep.PluginFinal, 0, "body.boneMods", "body.visible", "ruleOverrides") { }

        public override void Apply(EditContext x)
        {
            if (x.InBuild) { x.BeforeBuildFinal(); x.Hook(ExtStage.Final); x.AfterBuildFinal(); return; }
            x.ReapplyFinal();
            if (x.Report != null) x.Report.ExtFinalRan = true;
        }
    }

    /// <summary>Step 8: no renderer keeps a shared bundle material; in an edit the new subtrees are owned and
    /// URP-mapped in place.</summary>
    public sealed class OwnershipUnit : UnitBase
    {
        public OwnershipUnit() : base(UnitMap.Ownership, EditStep.Ownership, 0) { }

        public override void Apply(EditContext x)
        {
            if (x.InBuild)
            {
                if (!x.Root || !CharaAssembler.OwnAllMaterials) return;
                int before = x.Owned.Count;
                int made = x.OwnAll(x.Root);
                x.Log.Add($"materials: {x.Owned.Count} own ({made} copied after the extension stage)");
                return;
            }
            foreach (var go in x.NewSubtrees.Where(g => g))
            {
                if (CharaAssembler.OwnAllMaterials) x.OwnAll(go);
                if (x.UrpMapped) ShaderMap.ApplyUrp(go, copy: !CharaAssembler.OwnAllMaterials);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (x.ForceMatrixRecalculation) smr.forceMatrixRecalculationPerRender = true;
            }
            foreach (var go in x.RemapRoots.Where(g => g && !x.NewSubtrees.Contains(g)))
                ShaderMap.ApplyUrp(go, copy: false);
        }
    }

    /// <summary>Step 9 (edits): the dynamic-bone stage rebuilt from the parts in the order of a full build (chains of
    /// destroyed parts gone, new parts' chains in place, colliders from the body; new chains start from the bones).
    /// Phase 4.3: the light re-derivation restores the chains, re-applies the hair
    /// bundle corrections of the correction transforms that are chain transforms (a chain root or inside a chain, 212 mod
    /// bundles: the restore returned them to the old rest pose), then captures — so an edit equals a fresh build also there;
    /// the full form re-applies them after the reset (the reset returns a chain-root correction transform to the old rest
    /// pose when the simulation wrote it after the unit, e.g. drag frames in play mode). FlushReport.DynBone records the form.</summary>
    public sealed class DynBoneUnit : UnitBase
    {
        public DynBoneUnit() : base(UnitMap.DynBone, EditStep.Motion, 10) { }

        /// <summary>Diagnostic (UH3, MakerHairTest): false = the chain step of before 4.3 (no bundle re-application after the
        /// reset / restore, light and full forms). Default true.</summary>
        public static bool ReapplyBundlesAfterRestore = true;

        public override void Apply(EditContext x)
        {
            if (x.InBuild) return;
            var stage = x.Ctx.Motion != null ? x.Ctx.Motion.Get<DynamicBoneStage>() : null;
            if (stage == null) return;
            // physics history from the old shape / parts goes: the chain bones back to their rest pose first, the chains
            // re-derived (or rebuilt in full-build order), then every chain starts with its particles at the bones
            // (chains of destroyed parts are skipped: their component and bones are gone)
            foreach (var c in stage.Chains) if (c.Source && c.Root) c.ResetAll();
            bool full = x.DynBoneFull;
            x.DynBoneFull = false;
            if (full)
            {
                if (ReapplyBundlesAfterRestore) ReapplyHairBundles(x, ChainTransforms(stage));
                stage.Chains.Clear();
                stage.Rebuild();
            }
            else if (ReapplyBundlesAfterRestore) stage.Rebuild(() => ReapplyHairBundles(x, ChainTransforms(stage)));
            else stage.Rebuild();
            foreach (var c in stage.Chains) c.ResetAll();
            if (x.Report != null) x.Report.DynBone = full ? "full" : "light";
        }

        /// <summary>The transforms of the stage's live chains (the bones a chain restore writes).</summary>
        static System.Collections.Generic.HashSet<Transform> ChainTransforms(DynamicBoneStage stage)
        {
            var s = new System.Collections.Generic.HashSet<Transform>();
            foreach (var c in stage.Chains)
                for (int i = 0; i < c.ParticleCount; i++) { var t = c.ParticleTransform(i); if (t) s.Add(t); }
            return s;
        }

        /// <summary>The four hair parts' bundle corrections (HairUnit's lerps of the effective bundles) on the correction
        /// transforms in <paramref name="only"/>.</summary>
        static void ReapplyHairBundles(EditContext x, System.Collections.Generic.HashSet<Transform> only)
        {
            var hair = x.Doc?.Hair;
            if (hair == null || only.Count == 0) return;
            for (int k = 0; k < 4 && k < (x.Ctx.Hair?.Length ?? 0); k++)
            {
                var go = x.Ctx.Hair[k];
                var cmp = go ? go.GetComponent<AIChara.CmpHair>() : null;
                if (cmp != null) HairUnit.ApplyBundles(cmp, hair.Parts[k], only);
            }
        }
    }
}
