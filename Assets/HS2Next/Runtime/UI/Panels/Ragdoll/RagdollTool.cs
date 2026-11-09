// The ragdoll's viewport tool, begun by the panel's «Grab» and «Shoot». It lets through what it does not claim, so the camera, a click and the shortcuts go on as without it.
//   grab mode   a press on a part or a prop takes it at the hit point; the pointer moves the goal at the grab's depth, the wheel changes that depth; Space pins it, T pins it to the
//               selected node, N makes a follower of the part. Ctrl + press pushes a part along the ray. Shift + press throws a prop along the ray, anywhere in the view; the first prop
//               brings the world with it. A press that meets nothing, or that holds Alt, is not claimed
//   shoot mode  every press fires a bullet from the camera through the pointer, hit or miss, and held it repeats at the world's rate, in real time; Alt + press is let through
// The pick asks the physics world, not the surfaces of the view. A plain grab, a push, a throw and a shot change no model value and make no history entry; a pin and a follower are one entry
// each, through StudioOps. Esc during a press drops it, Esc after that ends the tool; the end drops what is held. Element of the view only: what Draw shows never reaches a capture.
using System;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;

namespace HS2Next.UI.Panels
{
    public sealed class RagdollTool : ViewportTool
    {
        /// <summary>The key of the tool's hint in the status line.</summary>
        public const string HintKey = "ragdoll.tool.hint";
        /// <summary>A shot's spread in degrees.</summary>
        public const float Spread = 0.6f;
        /// <summary>The nearest a grab comes to the view.</summary>
        public const float MinDepth = 1f;
        /// <summary>What a wheel notch takes from the depth, as a share.</summary>
        public const float DepthPerNotch = 0.1f;
        /// <summary>The muzzle against the camera: right, up, forward.</summary>
        public static readonly Vector3 Muzzle = new Vector3(0.8f, -0.7f, 1.5f);

        /// <summary>Wire lines of the colliders and of the room in the view (drawn by the panel while it is shown, by the tool when the panel is closed); a shot's stain. They last for the session.</summary>
        public static bool ShowColliders, ShowRoom, StainOnHit = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ShowColliders = ShowRoom = false;
            StainOnHit = true;
        }

        readonly UiContext ctx;
        readonly Action<RagdollTool> ended;
        readonly Action repeat;
        RagdollGrab grab;
        Ray ray;
        bool firing, shooting;
        float depth, nextShot;

        public RagdollTool(UiContext ctx, Action<RagdollTool> ended = null)
        {
            this.ctx = ctx;
            this.ended = ended;
            repeat = Repeat;
        }

        /// <summary>True in shoot mode. A change lets go of what the press holds.</summary>
        public bool Shooting
        {
            get => shooting;
            set
            {
                if (shooting == value) return;
                shooting = value;
                Let();
            }
        }

        /// <summary>The grab of the press, or null.</summary>
        public RagdollGrab Held => grab != null && grab.Live ? grab : null;
        /// <summary>The distance of the grab's goal along the ray through the pointer.</summary>
        public float Depth => depth;
        /// <summary>Bullets fired since the tool began.</summary>
        public int Shots { get; private set; }
        /// <summary>The ray of the last bullet before its spread.</summary>
        public Ray LastRay { get; private set; }
        /// <summary>Where the last bullet's tracer started and ended.</summary>
        public Vector3 LastMuzzle { get; private set; }
        public Vector3 LastEnd { get; private set; }

        RagdollWorld World => ctx.Ops?.Ragdoll?.World;

        RagdollWorldValues Values => World?.Values ?? (ctx.Ops?.RagdollWorldBlock?.Values ?? new RagdollWorldValues()).Applied();

        // ------------------------------------------------------------ the press

        public override bool PointerDown(in ToolPointer p)
        {
            if (p.Button != 0 || (p.Mods & ToolMods.Alt) != 0) return false;
            ray = p.Ray;
            if (shooting)
            {
                Fire();
                StartRepeat();
                return true;
            }
            if ((p.Mods & ToolMods.Shift) != 0)
            {
                ctx.ShowResult(ctx.Ops.RagdollThrow(ray));
                return true;
            }
            var w = World;
            if (w == null || !w.Pick(ray, out var hit)) return false;
            var part = w.PartOf(hit.rigidbody);
            if (part != null && part.Body.Blending) return false;
            if ((p.Mods & ToolMods.Ctrl) != 0)
            {
                if (part == null) return false;
                ctx.ShowResult(ctx.Ops.RagdollPushAt(part.Body.NodeId, part.Key, hit.point, ray.direction));
                return true;
            }
            grab = w.Grab(hit.rigidbody, hit.point);
            depth = Mathf.Max(MinDepth, hit.distance);
            return grab != null;
        }

        public override void PointerMove(in ToolPointer p)
        {
            ray = p.Ray;
            MoveGoal();
        }

        public override void PointerUp(in ToolPointer p) => Let();

        public override void PointerCancel() => Let();

        public override bool Wheel(float notches, in ToolPointer p)
        {
            if (Held == null) return false;
            ray = p.Ray;
            depth = Mathf.Max(MinDepth, depth * (1f - DepthPerNotch * notches));
            MoveGoal();
            return true;
        }

        void MoveGoal()
        {
            var g = Held;
            if (g != null) g.Goal = ray.GetPoint(depth);
        }

        // what the press holds goes
        void Let()
        {
            grab?.Drop();
            grab = null;
            StopRepeat();
        }

        // ------------------------------------------------------------ the keys of a grab

        public override bool Key(string key, ToolMods mods)
        {
            if (mods != ToolMods.None || Held == null) return false;
            switch (key)
            {
                case "Space":
                    Pin(null);
                    return true;
                case "T":
                    string node = ctx.Selection.Primary;
                    if (node != null) Pin(node);
                    else Note("ragdoll.tool.noSelection");
                    return true;
                case "N":
                    Follower();
                    return true;
                default:
                    return false;
            }
        }

        // the grab becomes a pin where the held point is; a refused pin leaves the grab as it was
        void Pin(string follow)
        {
            var part = grab.Part;
            if (part == null)
            {
                Note("ragdoll.tool.propPin");
                return;
            }
            var r = ctx.ShowResult(ctx.Ops.RagdollPin(part.Body.NodeId, part.Key, grab.Point, follow, null, false, UiContext.User(OpLabels.RagdollPin)));
            if (!r.Ok) return;
            grab?.Drop();
            grab = null;
        }

        void Follower()
        {
            var part = grab.Part;
            if (part != null) ctx.ShowResult(ctx.Ops.RagdollFollower(part.Body.NodeId, part.Key, UiContext.User(OpLabels.RagdollFollower)));
        }

        void Note(string key) => ctx.Toasts?.Show(ctx.L10n.Get(key), ToastKind.Info);

        // ------------------------------------------------------------ shots

        void Fire()
        {
            var cam = ctx.Viewport?.ViewCamera;
            if (!cam) return;
            var t = cam.transform;
            var muzzle = t.position + t.right * Muzzle.x + t.up * Muzzle.y + t.forward * Muzzle.z;
            var r = ctx.ShowResult(ctx.Ops.RagdollShoot(ray.origin, ray.direction, muzzle, Spread, Values.Momentum, false, StainOnHit));
            if (!r.Ok) return;
            Shots++;
            LastRay = ray;
            if (r.Value is ShotResult s)
            {
                LastMuzzle = s.Muzzle;
                LastEnd = s.End;
            }
        }

        void StartRepeat()
        {
            if (firing) return;
            firing = true;
            nextShot = Time.unscaledTime + 1f / Values.FireRate;
            PanelTicker.Add(repeat);
        }

        void StopRepeat()
        {
            if (!firing) return;
            firing = false;
            PanelTicker.Remove(repeat);
        }

        void Repeat()
        {
            float now = Time.unscaledTime;
            if (now < nextShot) return;
            Fire();
            nextShot = Mathf.Max(nextShot + 1f / Values.FireRate, now);
        }

        // ------------------------------------------------------------ the view and the end

        public override void Draw(IToolDraw draw)
        {
            var w = World;
            if (w == null) return;
            RagdollOverlay.DrawGrabs(draw, w);
            if (RagdollPanel.Current == null) RagdollOverlay.DrawOutlines(draw, w, ShowColliders, ShowRoom);   // the panel draws them while it is shown
        }

        public override void Ended(ToolEnd reason)
        {
            Let();
            ended?.Invoke(this);
        }
    }
}
