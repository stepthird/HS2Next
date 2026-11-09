// The arithmetic of a ragdoll, in double and without the engine: the parts and their shares of the mass, the joints' axes and limits, what a muscle's frequency
// becomes as a spring and a damper, the curves of a getting up, a pin's strength, the substeps of a frame. Nothing here keeps time or state: a value is a function of
// what it is given.
//   muscle     w = muscle · state scale · (1 − weak muscle · weakness); spring = w² · subtree inertia, damper = friction · pivot inertia + 2 · damping · w · subtree inertia
//   pin        k = pin · pin scale · (1 − weakness), while up with a falloff k / (1 + (distance / falloff)²)
//   get up     u = time / max(.2, get up); muscle lerp(down, 1, S(u / .25)); blend S((u − .5) / .3); pin S((u − .8) / .2); height lerp(crouch, stand, S((u − .3) / .45))
//   grab       spring = stiffness · m, damper = 2 sqrt(spring · m) · .6, force = max · m · gravity
// A distance is in units (1 unit = 10 cm), a time in seconds of the world.
using System;
using HS2Next.Save;

namespace HS2Next.Scenes.Ragdoll
{
    /// <summary>The directions a joint's row names, in the rest pose of the character.</summary>
    public enum RagdollAxis
    {
        /// <summary>The character's right.</summary>
        Right,
        /// <summary>The character's front.</summary>
        Front,
        /// <summary>The character's up.</summary>
        Up,
        /// <summary>Behind the character.</summary>
        Back,
        /// <summary>Below the character.</summary>
        Down,
        /// <summary>From the upper arm to the forearm.</summary>
        Arm,
        /// <summary>From the thigh to the shin.</summary>
        Leg,
        /// <summary>Along the foot part.</summary>
        Foot,
    }

    /// <summary>One joint's axes and limits, in degrees; <see cref="Crouch"/> is how far it folds in a crouch.</summary>
    public sealed class RagdollJointRow
    {
        public readonly RagdollAxis Primary, Secondary, Child, Flexion;
        public readonly double MaxFlex, MaxExt, Y, Z, Crouch;

        public RagdollJointRow(RagdollAxis primary, RagdollAxis secondary, RagdollAxis child, RagdollAxis flexion, double maxFlex, double maxExt, double y, double z, double crouch)
        {
            Primary = primary; Secondary = secondary; Child = child; Flexion = flexion;
            MaxFlex = maxFlex; MaxExt = maxExt; Y = y; Z = z; Crouch = crouch;
        }
    }

    public static class RagdollMath
    {
        // ---------- the parts ----------

        /// <summary>The bones a character must have beside the parts' own; of the two toe bones of a side one is enough.</summary>
        public static readonly string[] OtherBones = { "cf_J_Root", "cf_J_Spine03", "cf_J_Head" };
        /// <summary>The bones that end a foot, the first that exists.</summary>
        public static readonly string[] Toes = { "cf_J_Toes01_", "cf_J_Foot02_" };
        /// <summary>The most substeps of a frame of a running clock.</summary>
        public const int MaxSubsteps = 12;

        /// <summary>The row of a part's joint, or null for the pelvis.</summary>
        public static RagdollJointRow JointOf(string key)
        {
            if (key == RagdollParts.Pelvis) return null;
            int cut = key.IndexOf('_');
            switch (cut < 0 ? key : key.Substring(0, cut))
            {
                case RagdollParts.Abdomen: return new RagdollJointRow(RagdollAxis.Right, RagdollAxis.Front, RagdollAxis.Up, RagdollAxis.Front, 30, 25, 20, 20, 25);
                case RagdollParts.Chest: return new RagdollJointRow(RagdollAxis.Right, RagdollAxis.Front, RagdollAxis.Up, RagdollAxis.Front, 25, 20, 15, 20, 15);
                case RagdollParts.Head: return new RagdollJointRow(RagdollAxis.Right, RagdollAxis.Front, RagdollAxis.Up, RagdollAxis.Front, 40, 40, 30, 50, 10);
                case "upperarm": return new RagdollJointRow(RagdollAxis.Front, RagdollAxis.Up, RagdollAxis.Arm, RagdollAxis.Down, 100, 75, 90, 60, 70);
                case "forearm": return new RagdollJointRow(RagdollAxis.Up, RagdollAxis.Front, RagdollAxis.Arm, RagdollAxis.Front, 140, 5, 5, 30, 70);
                case "hand": return new RagdollJointRow(RagdollAxis.Front, RagdollAxis.Up, RagdollAxis.Arm, RagdollAxis.Down, 70, 60, 25, 10, 10);
                case "thigh": return new RagdollJointRow(RagdollAxis.Right, RagdollAxis.Front, RagdollAxis.Leg, RagdollAxis.Front, 100, 20, 40, 30, 100);
                case "shin": return new RagdollJointRow(RagdollAxis.Right, RagdollAxis.Front, RagdollAxis.Leg, RagdollAxis.Back, 140, 2, 3, 5, 120);
                case "foot": return new RagdollJointRow(RagdollAxis.Right, RagdollAxis.Up, RagdollAxis.Foot, RagdollAxis.Up, 30, 50, 25, 25, 15);
                default: return null;
            }
        }

        /// <summary>A part's mass; masses do not scale with the character.</summary>
        public static double PartMass(double share, double mass) => Math.Max(0.05, share * mass);

        // ---------- curves ----------

        /// <summary>x clamped to 0..1, then x²(3 − 2x).</summary>
        public static double Smooth(double x)
        {
            x = Clamp01(x);
            return x * x * (3.0 - 2.0 * x);
        }

        public static double Clamp01(double x) => x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>Where a getting up stands after <paramref name="time"/>: 1 is done.</summary>
        public static double GetUpU(double time, double getUp) => time / Math.Max(0.2, getUp);
        /// <summary>The muscle's scale at <paramref name="u"/>.</summary>
        public static double GetUpMuscle(double u, double downMuscle) => Lerp(downMuscle, 1.0, Smooth(u / 0.25));
        /// <summary>The blend from the crouch to the pose at <paramref name="u"/>.</summary>
        public static double GetUpBlend(double u) => Smooth((u - 0.5) / 0.3);
        /// <summary>The pin's scale at <paramref name="u"/>.</summary>
        public static double GetUpPin(double u) => Smooth((u - 0.8) / 0.2);
        /// <summary>From this <paramref name="u"/> on the body rises.</summary>
        public const double RiseFrom = 0.3;
        /// <summary>The share of the height a rise starts from.</summary>
        public const double CrouchHeight = 0.45;
        /// <summary>The pelvis' height over the feet at <paramref name="u"/>.</summary>
        public static double RiseHeight(double u, double crouchH, double standH) => Lerp(crouchH, standH, Smooth((u - RiseFrom) / 0.45));
        /// <summary>The weight of the rise's forces at <paramref name="u"/>.</summary>
        public static double RiseWeight(double u) => 1.0 - Smooth((u - 0.8) / 0.2);

        // ---------- muscles and pins ----------

        /// <summary>The frequency of a muscle in a state, with what weakness takes.</summary>
        public static double Muscle(double muscle, double stateScale, double weakMuscle, double weakness) => muscle * stateScale * (1.0 - weakMuscle * weakness);
        /// <summary>The frequency that holds a joint as it is, whatever the state.</summary>
        public static double HoldMuscle(double muscle) => Math.Max(2.0 * muscle, 80.0);
        /// <summary>A drive's spring.</summary>
        public static double Spring(double w, double subInertia) => w * w * subInertia;
        /// <summary>A drive's damper: the friction stays with a muscle of 0.</summary>
        public static double Damper(double w, double friction, double muscleDamping, double pivotInertia, double subInertia) =>
            friction * pivotInertia + 2.0 * muscleDamping * w * subInertia;

        /// <summary>How strongly a part follows its target; <paramref name="falloff"/> acts only while up.</summary>
        public static double PinK(double pin, double pinScale, double weakness, double distance, double falloff, bool up)
        {
            double k = pin * pinScale * (1.0 - weakness);
            if (up && falloff > 0.0)
            {
                double r = distance / falloff;
                k /= 1.0 + r * r;
            }
            return k;
        }

        /// <summary>The spring, damper and largest force of a grab or a pin.</summary>
        public static void GrabDrive(double stiffness, double maxW, double mass, double gravity, out double spring, out double damper, out double force)
        {
            spring = stiffness * mass;
            damper = 2.0 * Math.Sqrt(spring * mass) * 0.6;
            force = maxW * mass * gravity;
        }

        // ---------- the clock ----------

        /// <summary>
        /// The substeps a frame runs: <paramref name="accum"/> takes dt · timeScale and gives back whole substeps. In a running frame more than <paramref name="maxSubsteps"/> are not run and
        /// nothing is carried over, so a hitch cannot snowball; a fixed-step run (<paramref name="keepAll"/>) drops none.
        /// </summary>
        public static int Substeps(ref double accum, double dt, double timeScale, int hz, int maxSubsteps, bool keepAll)
        {
            accum += dt * timeScale;
            double h = 1.0 / hz;
            int n = 0;
            while (accum >= h)
            {
                accum -= h;
                n++;
                if (!keepAll && n == maxSubsteps) { accum = 0.0; break; }
            }
            return n;
        }

        /// <summary>The angle between two unit quaternions in degrees, in double: it does not read 0 for a small one.</summary>
        public static double AngleDeg(double ax, double ay, double az, double aw, double bx, double by, double bz, double bw)
        {
            double cx = -ax, cy = -ay, cz = -az, cw = aw;
            double x = cw * bx + cx * bw + cy * bz - cz * by;
            double y = cw * by - cx * bz + cy * bw + cz * bx;
            double z = cw * bz + cx * by - cy * bx + cz * bw;
            double w = cw * bw - cx * bx - cy * by - cz * bz;
            return 2.0 * Math.Atan2(Math.Sqrt(x * x + y * y + z * z), Math.Abs(w)) * 180.0 / Math.PI;
        }
    }
}
