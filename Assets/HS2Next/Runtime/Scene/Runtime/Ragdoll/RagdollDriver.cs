// The body as one of a character's pose drivers: it takes part in what every driver takes part in, the suspend of a thumbnail or the default pose, and in what a reader that stores
// a base sees. Nothing exists for a character without a body.
//   Bare        what a reader sees: nested inside the driven bones' bare read, so a capture stores what the pose sources left, under the body
//   RestoreNow  a flush, a revive, a controller's bind: the bones back; the caller takes off what lies on top first
//   Suspend     the bones back, the stages off, the world goes on; the end of it drops the targets
using System;
using System.Runtime.CompilerServices;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class RagdollDriver : ICharaPoseDriver
    {
        public static readonly RagdollDriver Instance = new RagdollDriver();

        static readonly ConditionalWeakTable<GameObject, RagdollBody> bodies = new ConditionalWeakTable<GameObject, RagdollBody>();

        /// <summary>The body of a built character, or null.</summary>
        public static RagdollBody Of(CharaRealization r)
        {
            var root = r?.Root;
            return root && bodies.TryGetValue(root, out var b) ? b : null;
        }

        internal static void Bind(RagdollBody b)
        {
            var root = b.Realization.Root;
            if (!root) return;
            bodies.Remove(root);
            bodies.Add(root, b);
        }

        internal static void Unbind(RagdollBody b)
        {
            var root = b.Realization?.Root;
            if (root) bodies.Remove(root);
        }

        /// <summary>Runs <paramref name="read"/> with the bones as they are without the body.</summary>
        public static void Bare(CharaRealization r, Action read)
        {
            var set = Of(r)?.Set;
            if (set != null) set.Bare(read);
            else read();
        }

        /// <summary>The bones back to what the pose left; the caller takes off what lies on top first. The next step writes again.</summary>
        public static void RestoreNow(CharaRealization r) => Of(r)?.RestoreBones();

        public void Apply(CharaRealization r, CharaNodeState state, PoseApplyReason reason) { }
        public void OnChanged(CharaRealization r, string path) { }

        public IDisposable Suspend(GameObject charaRoot)
        {
            if (!charaRoot || !bodies.TryGetValue(charaRoot, out var b) || b.Suspended) return null;
            b.Suspended = true;
            b.RestoreBones();
            if (b.Set != null) b.Set.Suspended = true;
            b.SetStagesEnabled(false);
            return new Resume(b);
        }

        sealed class Resume : IDisposable
        {
            RagdollBody b;
            public Resume(RagdollBody b) { this.b = b; }
            public void Dispose()
            {
                var x = b;
                b = null;
                if (x == null) return;
                x.Suspended = false;
                if (x.Set != null) x.Set.Suspended = false;
                x.SetStagesEnabled(true);
                x.DropTargets();
            }
        }
    }
}
