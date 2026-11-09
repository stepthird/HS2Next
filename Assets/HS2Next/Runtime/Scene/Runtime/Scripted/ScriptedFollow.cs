// The camera follow of camera.follow: a scene part that holds the scene's `camera` slot (owner "follow", priority 1: above the Timeline's camera track) from
// the call until its seconds have passed on the scene clock, then releases it (the Timeline's camera or the scene's view shows again). Each late apply:
//   u = smoothstep(elapsed / seconds) = u^2 (3 - 2u)
//   p = the followed transform's world position + offset (a bone of a character, else the node's holder)
//   s = p on the first frame, else lerp(s, p, 1 - e^(-4 dt)), dt the scene clock's delta of that frame (0 while paused: the camera holds)
//   the slot's value: target s, rotation Euler(lerp(rot, rotTo, u)) componentwise, distance lerp(dist, distTo, u) (at least 1 with above), the field of
//   view given, else the one shown when the follow started
// The view camera places itself from the slot: position = s + rotation . (0, 0, -distance). When the view camera's own late apply has already run in this
// late apply, the part places the view again after it set or released the slot, so the camera never shows a frame late. The followed transform is read again when it
// went (a rebuilt character); while the node is not built the last value stays. A node that left the scene ends the follow; a slot the layer released
// (the scene cleared) ends it. A scene with no follow does nothing: the tick and the late apply return at their first branch; nothing is subscribed.
using System;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What a follow follows and how the camera moves meanwhile.</summary>
    public sealed class FollowSpec
    {
        public string NodeId;
        /// <summary>A bone (transform name) under a character or an item; null: the node's holder.</summary>
        public string Bone;
        /// <summary>Added to the followed position, in world axes.</summary>
        public Vector3 Offset;
        /// <summary>The camera's Euler angles at the start and at the end.</summary>
        public Vector3 Rot, RotTo;
        /// <summary>The distance at the start and at the end.</summary>
        public float Dist, DistTo;
        /// <summary>The field of view (> 0), else the one shown when the follow starts.</summary>
        public float Fov;
        /// <summary>The duration in seconds of the scene clock (> 0).</summary>
        public float Seconds;
        /// <summary>The distance never goes below 1.</summary>
        public bool Above;
    }

    public sealed class ScriptedFollow : ISceneRuntimePart, ISceneTickPart, IScriptCommandSlots, IDisposable
    {
        /// <summary>The override owner of the follow's slot.</summary>
        public const string Owner = "follow";
        /// <summary>Above the Timeline's camera track (0), below the constraints (10).</summary>
        public const int Priority = 1;
        /// <summary>The rate of the smoothing of the followed point, per second.</summary>
        public const double Rate = 4.0;

        SceneRuntime rt;
        OverrideSlot slot;
        FollowSpec spec;
        Transform followed;
        float fov, elapsed, pendingDt;
        Vector3 s;
        bool hasS;

        /// <summary>The scene's part, added when a runtime has none.</summary>
        public static ScriptedFollow Of(SceneRuntime rt)
        {
            if (rt == null) return null;
            var p = rt.GetPart<ScriptedFollow>();
            if (p != null) return p;
            p = new ScriptedFollow();
            rt.AddPart(p);
            return p;
        }

        /// <summary>True while a follow holds the camera.</summary>
        public bool Active => slot != null && !slot.IsReleased;
        /// <summary>The scene clock's seconds since the follow started.</summary>
        public float Elapsed => elapsed;
        /// <summary>The follow running now, or null.</summary>
        public FollowSpec Spec => Active ? spec : null;
        /// <summary>The smoothed followed point of the last late apply.</summary>
        public Vector3 Point => s;

        /// <summary>u^2 (3 - 2u) of u clamped to [0, 1].</summary>
        public static double Smoothstep(double u)
        {
            u = u < 0 ? 0 : u > 1 ? 1 : u;
            return u * u * (3 - 2 * u);
        }

        /// <summary>
        /// Starts a follow (replacing a running one) of <paramref name="followedNow"/>, the transform <paramref name="f"/> names, resolved by the caller;
        /// <paramref name="fovShown"/> is the field of view shown now. The slot shows the start at once. False when the layer refuses the slot.
        /// </summary>
        public bool Start(FollowSpec f, Transform followedNow, float fovShown)
        {
            if (rt == null || f == null || !followedNow) return false;
            if (slot == null || slot.IsReleased)
            {
                slot = rt.Overrides.Acquire(Owner, null, "camera", Priority);
                if (slot == null) return false;
            }
            spec = f;
            followed = followedNow;
            fov = f.Fov > 0 ? f.Fov : fovShown;
            elapsed = pendingDt = 0f;
            s = followedNow.position + f.Offset;
            hasS = false;   // the first late apply takes the point of its frame
            Set(0.0);
            return true;
        }

        /// <summary>Ends the follow; true when one held the camera.</summary>
        public bool Stop()
        {
            bool had = Active;
            if (slot != null && !slot.IsReleased) slot.Release();
            Reset();
            return had;
        }

        /// <summary>Ends the follow (IScriptCommandSlots); 1 when one held the camera.</summary>
        public int ReleaseAll(bool writeShown) => Stop() ? 1 : 0;

        void Reset()
        {
            slot = null;
            spec = null;
            followed = null;
            hasS = false;
            elapsed = pendingDt = 0f;
        }

        void Set(double u)
        {
            var f = spec;
            double rx = f.Rot.x + (f.RotTo.x - (double)f.Rot.x) * u;
            double ry = f.Rot.y + (f.RotTo.y - (double)f.Rot.y) * u;
            double rz = f.Rot.z + (f.RotTo.z - (double)f.Rot.z) * u;
            double dist = f.Dist + (f.DistTo - (double)f.Dist) * u;
            if (f.Above && dist < 1) dist = 1;
            Bits.Euler(rx, ry, rz, out double qx, out double qy, out double qz, out double qw);
            slot.SetCamera(s.x, s.y, s.z, qx, qy, qz, qw, dist, fov);
        }

        // ------------------------------------------------------------ ISceneTickPart, ISceneRuntimePart

        public void Tick(float dt)
        {
            if (slot == null) return;
            if (!(dt >= 0f) || float.IsInfinity(dt)) dt = 0f;
            elapsed += dt;
            pendingDt += dt;
        }

        public void LateApply()
        {
            if (slot == null) return;
            if (slot.IsReleased || rt.Model.Find(spec.NodeId) == null || elapsed >= spec.Seconds)
            {
                bool held = !slot.IsReleased;
                Stop();
                if (held) PlaceView();   // the view shows what lies below in this frame already
                return;
            }
            float dt = pendingDt;
            pendingDt = 0f;
            if (!followed) followed = Resolve();
            if (!followed) return;   // not built now: the last value stays
            var p = followed.position + spec.Offset;
            if (!hasS)
            {
                s = p;
                hasS = true;
            }
            else s = Vector3.LerpUnclamped(s, p, (float)(1.0 - Math.Exp(-Rate * dt)));
            Set(Smoothstep(elapsed / (double)spec.Seconds));
            PlaceView();
        }

        Transform Resolve()
        {
            var end = ConstraintResolver.Source(rt, new ConstraintEnd(spec.NodeId, spec.Bone));
            return end.State == ConstraintEndState.Ok ? end.Transform : null;
        }

        // The view camera's late apply, again when it ran before this part's in this late apply.
        void PlaceView()
        {
            var cam = rt.GetPart<SceneViewCamera>();
            if (cam == null) return;
            var parts = rt.Parts;
            int mine = -1, its = -1;
            for (int i = 0; i < parts.Count; i++)
            {
                if (ReferenceEquals(parts[i], this)) mine = i;
                else if (ReferenceEquals(parts[i], cam)) its = i;
            }
            if (its >= 0 && its < mine) cam.LateApply();
        }

        public void Attach(SceneRuntime r) { rt = r; }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void Clear() { }
        public void OnLoadedRealized() { }

        public void Dispose() => Stop();
    }
}
