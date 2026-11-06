// A card's bone modifiers in the frame. Both stages exist exactly while the character has the modifiers' component, so a character
// without modifiers keeps its stage list.
//   bones.modifiers      phase 120  after the animation, the hand patterns and the heel lift, before the hips offset, the neck gate,
//                                   IK, FK and every other stage: the pose sources' value of this frame, then the modifier. Runs inside a
//                                   probe as well, without keeping any state, so the animation copies see the modified pose.
//   bones.modifiers.end  phase 960  after everything that writes bones: what the frame ended on (BoneModifierApplier's E)
// The phases are casts: the numbers lie between the enum's names. The stages read ctx.Bones when they run, so an applier replaced by an
// edit needs nothing. A writer of a bone after phase 960 becomes the pass's next input on a modified channel.
using HS2Next.Chara;
using HS2Next.Motion.Animation;

namespace HS2Next.Motion.Modifiers
{
    public sealed class BoneModifierStage : IMotionStage
    {
        readonly CharaContext ctx;
        public BoneModifierStage(CharaContext ctx) { this.ctx = ctx; }
        public MotionPhase Phase => (MotionPhase)120;
        public string Name => "bones.modifiers";
        public bool Enabled { get; set; } = true;

        public void Step(in MotionFrame frame)
        {
            var b = ctx.Bones;
            if (b) b.Pass(!(ctx.Motion && ctx.Motion.Probing));
        }

        public void Reset() { }
    }

    public sealed class BoneModifierEndStage : IMotionStage
    {
        readonly CharaContext ctx;
        public BoneModifierEndStage(CharaContext ctx) { this.ctx = ctx; }
        public MotionPhase Phase => (MotionPhase)960;
        public string Name => "bones.modifiers.end";
        public bool Enabled { get; set; } = true;

        public void Step(in MotionFrame frame)
        {
            var b = ctx.Bones;
            if (b && !(ctx.Motion && ctx.Motion.Probing)) b.End();
        }

        public void Reset() { }
    }

    public static class BoneModifierSetup
    {
        /// <summary>At a build, after the other stage families: a controller's bind sees the pose without the modifiers, and the stages if the character has modifiers.</summary>
        public static void Attach(CharaContext ctx)
        {
            if (ctx?.Motion == null) return;
            var anim = CharaAnimation.Of(ctx.Root);
            // after the chains' handler: their rest pose holds the modifiers, and this takes them off
            if (anim) anim.BeforeBind += _ => { var b = ctx.Bones; if (b) b.ToBase(); };
            Sync(ctx);
        }

        /// <summary>Both stages present exactly when the character has the modifiers' component. Called at a build, after an edit that
        /// made or removed the component, and by a test that adds one by hand.</summary>
        public static void Sync(CharaContext ctx)
        {
            var m = ctx?.Motion;
            if (m == null) return;
            var pass = m.Get<BoneModifierStage>();
            var end = m.Get<BoneModifierEndStage>();
            if (ctx.Bones)
            {
                if (pass == null) m.Add(new BoneModifierStage(ctx));
                if (end == null) m.Add(new BoneModifierEndStage(ctx));
            }
            else
            {
                if (pass != null) m.Remove(pass);
                if (end != null) m.Remove(end);
            }
        }
    }
}
