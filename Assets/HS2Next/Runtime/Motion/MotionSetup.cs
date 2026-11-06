// Attaches the motion stages of a freshly built character (Phase 1.5).
//
// CharaAssembler.Build calls MotionSetup.Attach once, after the extension modules of the Final
// stage. Each stage family owns its own *Setup class and file (Corrective/, Look/, Face/,
// DynamicBones/) so that the implementations can be developed independently; this file only fixes
// the call order. The sub-namespaces avoid the names of the compat data holders (`DynamicBone`,
// `Correct.*`), which would otherwise shadow them inside HS2Next.Motion.
using System;
using HS2Next.Chara;

namespace HS2Next.Motion
{
    public static class MotionSetup
    {
        /// <summary>Master switch (false: no stage is attached; the character stays in the rest pose / default expression).</summary>
        public static bool Enabled = true;

        public static void Attach(CharaContext ctx)
        {
            if (!Enabled || ctx?.Motion == null) return;
            Run(ctx, "anim", Animation.AnimationSetup.Attach);   // stage A (sampling) + hand patterns (Shape)
            Run(ctx, "correct", Corrective.CorrectSetup.Attach);
            Run(ctx, "look", Look.LookSetup.Attach);
            Run(ctx, "face", Face.FaceSetup.Attach);
            // after 1.0.0: the Heelz body lift and foot joints, before the dynamic bones so that their chains
            // start from the lifted body, as the game lifts it when the shoes load
            Run(ctx, "heels", Heels.HeelsSetup.Attach);
            Run(ctx, "dynbone", DynamicBones.DynamicBoneSetup.Attach);
            Run(ctx, "modifiers", Modifiers.BoneModifierSetup.Attach);   // last: a bind takes the modifiers off after the chains' reset
        }

        static void Run(CharaContext ctx, string name, Action<CharaContext> attach)
        {
            try { attach(ctx); }
            catch (Exception e) { ctx.Log?.Add($"motion: {name} setup failed ({e.GetType().Name}: {e.Message})"); }
        }
    }
}
