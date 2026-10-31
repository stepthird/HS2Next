// Expression (eyebrow / eyes / mouth patterns, blink, tears, cheek).
// MotionSetup.Attach calls Attach for every built character (after the look stages, so the eye-look
// outputs can be found). Attach reads the head's FaceBlendShape fields, builds the stage,
// applies the start state (by default Studio "add from card": the ChaFileStatus code defaults),
// registers the stage on ctx.Motion (MotionPhase.Face) and runs one computation synchronously so that
// no neutral-face frame is shown. The first Step then starts a blink, as in the game.
using HS2Next.Chara;

namespace HS2Next.Motion.Face
{
    public static class FaceSetup
    {
        public enum StartMode
        {
            /// <summary>Studio "add from card": the card's Status block is ignored; code defaults.</summary>
            StudioDefaults,
            /// <summary>Studio semantics with the card's Status block (as a scene load would apply it, mouthOpen 0, lip sync on).</summary>
            StudioCardStatus,
            /// <summary>Game / Maker semantics: only the head-load sequence with the card's Status block.</summary>
            GameCardStatus,
        }

        /// <summary>Master switch (false: no stage; the assembler's static default expression stays).</summary>
        public static bool Enabled = true;
        public static StartMode Mode = StartMode.StudioDefaults;
        /// <summary>Random seed for the next characters (null = a different seed per character). Tests set it.</summary>
        public static int? Seed;
        /// <summary>Default of the eyelid correction for new characters (false = vanilla).</summary>
        public static bool EyelidCorrectionDefault;

        static readonly System.Random seeder = new System.Random();

        public static void Attach(CharaContext ctx)
        {
            if (!Enabled || ctx?.Motion == null || !ctx.Head) return;
            var comp = FaceRig.FindComponent(ctx.Head);
            if (!comp) { ctx.Log?.Add("face: no FaceBlendShape on the head; expression stage not attached"); return; }
            var rig = FaceRig.Read(comp);
            int seed;
            lock (seeder) seed = Seed ?? seeder.Next();
            var stage = new ExpressionStage(rig, new SystemFaceRandom(seed));
            if (ctx.HeadBones != null && ctx.HeadBones.TryGetValue("cf_J_MouthMove", out var mouthMove)) stage.MouthWidthBone = mouthMove;
            foreach (var s in ctx.Motion.GetAll<IMotionStage>())
                if (s is IEyeLookOutputs look) { stage.EyeLook = look; break; }
            stage.EyelidCorrection = EyelidCorrectionDefault;

            var mats = new FaceMaterials(ctx.Head, ctx.FaceDraw, ctx.OwnedMaterials);
            var lists = ExpressionLists.For(ctx.Catalog, ctx.IsMale);
            var expr = new CharaExpression(stage, mats, lists, ctx);
            var status = Mode == StartMode.StudioDefaults ? new FaceStatus() : FaceStatus.FromCard(ctx.Card?.Status);
            if (Mode == StartMode.GameCardStatus) expr.ApplyHeadLoad(status);
            else expr.StartStudio(status);

            ctx.Motion.Add(stage);
            stage.Evaluate();

            var b = stage.Blink;
            ctx.Log?.Add($"face: {rig.ComponentType}, patterns eyebrow {stage.Eyebrow.PatternCount} / eyes {stage.Eyes.PatternCount} / mouth {stage.Mouth.PatternCount}, " +
                         $"targets {stage.Eyebrow.TargetCount}/{stage.Eyes.TargetCount}/{stage.Mouth.TargetCount}, blink F {b.BlinkFrequency} speed {b.BaseSpeed:0.###}, " +
                         $"lists {lists.Source} {lists.Eyebrow.Length}/{lists.Eyes.Length}/{lists.Mouth.Length}, start {Mode}" +
                         $"{(stage.EyeLook != null ? ", eye look found" : "")}{(stage.MouthWidthBone ? "" : ", no cf_J_MouthMove")}");
        }
    }
}
