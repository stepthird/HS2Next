// Pose drivers of character nodes: the seam where the FK writer
// (Phase 2.3) and the IK solver (Phase 3.4) plug in, both registered by default. At build the IK driver applies
// before the FK driver; afterwards each gets the changed paths under its block.
// Drivers can be suspended for a character (StudioDefaultPoseScope);
// SuspendAll combines the registered drivers.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public enum PoseApplyReason
    {
        /// <summary>The character was (re)built: apply the whole stored state.</summary>
        Build,
        /// <summary>A command changed the block.</summary>
        Changed,
    }

    public interface ICharaPoseDriver
    {
        /// <summary>Applies the stored block of <paramref name="state"/> to the realized character.</summary>
        void Apply(CharaRealization r, CharaNodeState state, PoseApplyReason reason);
        /// <summary>A field under the driver's block changed (<paramref name="path"/> = the canonical field path).</summary>
        void OnChanged(CharaRealization r, string path);
        /// <summary>Stops the driver's writes on the character built under <paramref name="charaRoot"/> until the handle is
        /// disposed; null when there is nothing to suspend.</summary>
        IDisposable Suspend(GameObject charaRoot);
    }

    public static class CharaPoseDrivers
    {
        /// <summary>IK solver driver (Phase 3.4): IkDriver by default.</summary>
        public static ICharaPoseDriver Ik = IkDriver.Instance;
        /// <summary>FK writer (Phase 2.3): FkDriver by default.</summary>
        public static ICharaPoseDriver Fk = FkDriver.Instance;
        /// <summary>The advanced pose of a character (its `chara.advancedPose` block): AdvancedPoseDriver by default.</summary>
        public static ICharaPoseDriver AdvancedPose = AdvancedPoseDriver.Instance;
        /// <summary>The driven bones of a character: only their suspend is called from here.</summary>
        public static ICharaPoseDriver Driven = DrivenPoseDriver.Instance;
        /// <summary>A character's ragdoll: only its suspend is called from here.</summary>
        public static ICharaPoseDriver Ragdoll = RagdollDriver.Instance;

        /// <summary>Suspends every registered driver on <paramref name="charaRoot"/>; null when none had anything to suspend.</summary>
        public static IDisposable SuspendAll(GameObject charaRoot)
        {
            List<IDisposable> handles = null;
            // the corrections come off first, then the driven bones under them, then the body: the other drivers' writes and latches never see any of them
            foreach (var d in new[] { AdvancedPose, Driven, Ragdoll, Ik, Fk })
            {
                var h = d?.Suspend(charaRoot);
                if (h != null) (handles ??= new List<IDisposable>()).Add(h);
            }
            return handles == null ? null : new Combined(handles);
        }

        sealed class Combined : IDisposable
        {
            List<IDisposable> handles;
            public Combined(List<IDisposable> h) { handles = h; }
            public void Dispose()
            {
                var h = handles;
                handles = null;
                if (h == null) return;
                for (int i = h.Count - 1; i >= 0; i--) h[i].Dispose();
            }
        }
    }
}
