// The arithmetic of a ragdoll, with no engine: RM6 the curves of a getting up at u = 0, .25, .3, .5, .8, 1, the drives by hand, the substeps of a frame.
using System;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.Scenes.Ragdoll;

    public static class SceneTests_RagdollMath
    {
        static bool Near(double a, double b) => Math.Abs(a - b) < 1e-12;

        public static void Run(TestRunner t)
        {
            t.Group = "RagdollMath";
            const double down = 0.15;
            var rows = new[]
            {
                (u: 0.0, muscle: 0.15, blend: 0.0, pin: 0.0, weight: 1.0),
                (u: 0.25, muscle: 1.0, blend: 0.0, pin: 0.0, weight: 1.0),
                (u: 0.3, muscle: 1.0, blend: 0.0, pin: 0.0, weight: 1.0),
                (u: 0.5, muscle: 1.0, blend: 0.0, pin: 0.0, weight: 1.0),
                (u: 0.8, muscle: 1.0, blend: 1.0, pin: 0.0, weight: 1.0),
                (u: 1.0, muscle: 1.0, blend: 1.0, pin: 1.0, weight: 0.0),
            };
            foreach (var r in rows)
            {
                t.Check(Near(RagdollMath.GetUpMuscle(r.u, down), r.muscle), "RM6: muscle at u = " + r.u);
                t.Check(Near(RagdollMath.GetUpBlend(r.u), r.blend), "RM6: blend at u = " + r.u);
                t.Check(Near(RagdollMath.GetUpPin(r.u), r.pin), "RM6: pin at u = " + r.u);
                t.Check(Near(RagdollMath.RiseWeight(r.u), r.weight), "RM6: rise weight at u = " + r.u);
            }
            t.Check(Near(RagdollMath.Smooth(0.5), 0.5) && Near(RagdollMath.GetUpBlend(0.65), 0.5) && Near(RagdollMath.GetUpPin(0.9), 0.5), "RM6: the middle of each curve is a half");
            t.Check(Near(RagdollMath.RiseHeight(0.3, 4, 10), 4) && Near(RagdollMath.RiseHeight(0.75, 4, 10), 10), "RM6: the rise runs from the crouch to the stand");
            t.Check(Near(RagdollMath.GetUpU(1.25, 2.5), 0.5) && Near(RagdollMath.GetUpU(0.1, 0.0), 0.5), "RM6: u, with a get up of at least .2");

            t.Check(Near(RagdollMath.Spring(20, 3), 1200) && Near(RagdollMath.Damper(20, 3, 1, 2, 3), 126) && Near(RagdollMath.Damper(0, 3, 1, 2, 3), 6), "the drive by hand; friction stays with a muscle of 0");
            t.Check(Near(RagdollMath.Muscle(20, 1, 0.27, 1), 14.6) && Near(RagdollMath.HoldMuscle(0), 80) && Near(RagdollMath.HoldMuscle(50), 100), "a muscle with weakness; a hold");
            t.Check(Near(RagdollMath.PinK(1, 1, 0, 3, 3, true), 0.5) && Near(RagdollMath.PinK(1, 1, 0, 3, 3, false), 1) && Near(RagdollMath.PinK(1, 1, 0.5, 3, 0, true), 0.5), "a pin's strength: the falloff acts while up");

            double acc = 0;
            t.Equal(12, RagdollMath.Substeps(ref acc, 0.2, 1, 240, 12, false), "a hitch runs 12 substeps");
            t.Check(acc == 0, "and carries nothing over");
            acc = 0;
            int all = RagdollMath.Substeps(ref acc, 0.2, 1, 240, 12, true);
            t.Check(Math.Abs(all + acc * 240 - 48) < 1e-9, "a fixed step drops none: what did not run is carried over");
            acc = 0;
            t.Equal(2, RagdollMath.Substeps(ref acc, 1.0 / 60.0, 0.5, 240, 12, false), "half the time scale, half the substeps");
            t.Check(RagdollMath.AngleDeg(0, 0, 0, 1, 0, 0, 0, -1) == 0, "q and -q are one rotation");
        }
    }
}
