// Studio default pose of a built character for character thumbnails:
// "Studio defaults: no animation, expression defaults, eyes and neck forward". Enter saves and
// replaces, Dispose restores in reverse order:
//   animation   CharaAnimation.Save(), then Stop() (post-build pose, option items released)
//   face        status, mouthOpen, lipSync → the FaceStatus code defaults with blink off (eyes open), mouth open 0,
//               lip sync on, and the non-Studio members (eyebrow open max, mouth open min/max, mouth fixed,
//               eye highlight) at their defaults
//   look        eye / neck patterns, eye rotations, neck fix angles → eye pattern 0 and neck pattern 0 (forward)
//   hands       both hand patterns → 0
//   pose        drivers (FK/IK, Phase 2.3+) suspended through SuspendPoseDrivers (default CharaPoseDrivers.SuspendAll)
//   physics     DynamicBone and bust stages disabled
//   settle      CharaMotion reset (every stage's history dropped), then stepped SettleSteps times with dt 1/30
//               (unpaused, time scale 1) so look and expression settle
// Dispose: stages re-enabled, pose drivers resumed, hands, look, face restored, the animation restored with
// CharaAnimation.Restore (re-creates option items through the option-items-loaded hook, so the scene layer maps
// them to URP again), motion switches restored, CharaMotion.ResetAll(). A character that had no motion before keeps
// none (Restore would fall back to the 0/0/0 row). Main thread only; the caller renders between Enter and Dispose.
// After 1.1.0: in a paused scene Dispose asks the motion for a settle (the physics come to rest on the restored pose).
using System;
using System.Collections.Generic;
using HS2Next.Content.Studio;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Face;
using HS2Next.Motion.Look;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class StudioDefaultPoseScope : IDisposable
    {
        /// <summary>Settling steps (dt 1/30) after the defaults are applied.</summary>
        public const int SettleSteps = 30;
        public const float SettleDt = 1f / 30f;

        /// <summary>
        /// Seam for the pose drivers (FK writer 2.3, IK 3.4): suspends them for <c>charaRoot</c> and returns the handle that
        /// resumes them on Dispose. Null (default): CharaPoseDrivers.SuspendAll (2.3).
        /// </summary>
        public static Func<GameObject, IDisposable> SuspendPoseDrivers;

        readonly GameObject root;
        readonly StudioDb db;
        readonly CharaMotion motion;
        readonly CharaAnimation anim;
        readonly CharaExpression face;
        readonly CharaLook look;

        AnimationSceneState animSaved;
        bool hadMotion;
        FaceStatus faceSaved;
        float mouthOpenSaved;
        bool lipSyncSaved;
        int eyePtnSaved, neckPtnSaved;
        Quaternion[] eyeRotSaved, neckFixSaved;
        int[] handsSaved;
        IDisposable poseHandle;
        readonly List<(IMotionStage stage, bool enabled)> stagesSaved = new List<(IMotionStage, bool)>();
        bool pausedSaved;
        float timeScaleSaved;
        bool disposed;

        StudioDefaultPoseScope(GameObject charaRoot, StudioDb db)
        {
            root = charaRoot;
            this.db = db;
            motion = charaRoot ? charaRoot.GetComponent<CharaMotion>() : null;
            anim = CharaAnimation.Of(charaRoot);
            face = CharaExpression.Of(charaRoot);
            look = CharaLook.Of(charaRoot);
        }

        /// <summary>Puts a built character node into the Studio default pose (the runtime's Studio lists restore its motion).</summary>
        public static StudioDefaultPoseScope Enter(CharaRealization r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return Enter(r.Root, r.Runtime?.Services?.Db);
        }

        /// <summary>Puts the character built under <paramref name="charaRoot"/> into the Studio default pose. <paramref name="db"/>
        /// resolves the saved animation on Dispose (null: the catalog's last Studio lists).</summary>
        public static StudioDefaultPoseScope Enter(GameObject charaRoot, StudioDb db)
        {
            if (!charaRoot) throw new ArgumentNullException(nameof(charaRoot));
            var s = new StudioDefaultPoseScope(charaRoot, db ?? CharaAnimation.Of(charaRoot)?.Context?.Catalog?.StudioLists);
            s.Apply();
            return s;
        }

        void Apply()
        {
            // animation
            if (anim != null)
            {
                animSaved = anim.Save();
                hadMotion = anim.Current != null;
                anim.Stop();
            }

            // face
            if (face != null)
            {
                faceSaved = face.Status.Clone();
                mouthOpenSaved = face.MouthOpen;
                lipSyncSaved = face.LipSync;
                ApplyFace(new FaceStatus { EyesBlink = false }, 0f, true);
            }

            // look
            if (look != null)
            {
                eyePtnSaved = look.EyePattern;
                neckPtnSaved = look.NeckPattern;
                eyeRotSaved = new Quaternion[2];
                look.SaveEyes(eyeRotSaved);
                neckFixSaved = new Quaternion[look.Neck?.Bones?.Length ?? 0];
                look.SaveNeck(neckFixSaved);
                look.SetEyePattern(0, force: true);
                look.SetNeckPattern(0);
            }

            // hands
            var hands = anim != null ? anim.Hands : null;
            if (hands != null)
            {
                handsSaved = new[] { hands[0], hands[1] };
                hands.SetPattern(0, 0);
                hands.SetPattern(1, 0);
            }

            // pose drivers (2.3+)
            poseHandle = (SuspendPoseDrivers ?? CharaPoseDrivers.SuspendAll)(root);

            // physics stages
            if (motion != null)
            {
                foreach (var st in motion.Stages)
                    if (st is DynamicBoneStage || st is BustStage)
                    {
                        stagesSaved.Add((st, st.Enabled));
                        st.Enabled = false;
                    }

                pausedSaved = motion.Paused;
                timeScaleSaved = motion.TimeScale;
                motion.Paused = false;
                motion.TimeScale = 1f;
                // every stage drops its history (chain bones at their rest pose, blink and smoothing states), so the shot
                // does not depend on what ran before it (an earlier capture's restore, physics of a paused scene)
                motion.ResetAll();
                for (int i = 0; i < SettleSteps; i++) motion.Step(SettleDt);
            }
        }

        void ApplyFace(FaceStatus s, float mouthOpen, bool lipSync)
        {
            face.ApplyStudioState(s, mouthOpen, lipSync);
            face.ChangeEyebrowOpenMax(s.EyebrowOpenMax);
            face.ChangeMouthOpenMin(s.MouthOpenMin);
            face.ChangeMouthOpenMax(s.MouthOpenMax);
            face.ChangeMouthFixed(s.MouthFixed);
            face.HideEyeHighlight(s.HideEyesHighlight);
            // As CharaRealization.ApplyFace: the Studio mouth open decides FixedRate last.
            face.ApplyStudioMouthOpen();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!root) return;   // destroyed meanwhile: nothing to restore

            foreach (var (stage, enabled) in stagesSaved) stage.Enabled = enabled;
            try { poseHandle?.Dispose(); }
            catch (Exception e) { Debug.LogWarning("StudioDefaultPoseScope: resuming the pose drivers threw " + e.Message); }

            var hands = anim != null ? anim.Hands : null;
            if (hands != null && handsSaved != null)
            {
                hands.SetPattern(0, handsSaved[0]);
                hands.SetPattern(1, handsSaved[1]);
            }

            if (look != null)
            {
                look.SetEyePattern(eyePtnSaved, force: true);
                look.LoadEyes(eyeRotSaved);
                if (neckFixSaved.Length > 0) look.LoadNeck(neckPtnSaved, neckFixSaved);
                look.SetNeckPattern(neckPtnSaved);
            }

            if (face != null && faceSaved != null) ApplyFace(faceSaved, mouthOpenSaved, lipSyncSaved);

            if (anim != null && animSaved != null)
            {
                if (hadMotion) anim.Restore(db, animSaved);
                else
                {
                    // Stop() kept speed, pattern, loop and option settings; only the hands came from the saved state.
                    anim.Speed = animSaved.Speed;
                    anim.ForceLoop = animSaved.ForceLoop;
                    anim.OptionVisible = animSaved.OptionVisible;
                }
            }

            if (motion != null)
            {
                motion.Paused = pausedSaved;
                motion.TimeScale = timeScaleSaved;
                motion.ResetAll();
                // the scene is paused (its clock gives no time): the chains dropped their history above, so they settle on the
                // restored pose at the next step instead of standing at their rest pose until the scene runs (after 1.1.0)
                bool still = (motion.DeltaSource != null && motion.DeltaSource() == 0f) || motion.Paused || motion.TimeScale <= 0f;
                if (still && CharaRealization.SettleWhilePaused) motion.RequestSettle();
            }
        }
    }
}
