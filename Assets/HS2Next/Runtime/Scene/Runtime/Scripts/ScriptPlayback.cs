// Scene scripts in the scene runtime: a scene part (registered by SceneRuntime.AddStandardParts) that keeps what a play leaves behind - the
// play in progress, the last play's result and the script it played (whose cast a stop resets) - pauses the Timeline when a recorded pass
// ends (so that no timed command fires after the last frame), and releases the slots of the script commands (moves, the follow, the depth
// of field) for a play, a stop and a new or loaded scene. A scene without scripts costs nothing: no subscription, and Apply returns at once
// while no recording is watched.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Scripts;
using HS2Next.Scenes.Timeline;
using HS2Next.Screenshots;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ScriptPlayback : ISceneRuntimePart, IDisposable
    {
        /// <summary>The override owners of the script commands: moves, the camera follow, the depth-of-field command.</summary>
        public const string OwnerMove = "move", OwnerFollow = "follow", OwnerDof = "dof";

        SceneRuntime rt;
        FrameRecorder watched;
        bool subscribed;

        /// <summary>The scene runtime (null before Attach).</summary>
        public SceneRuntime Runtime => rt;
        /// <summary>The play in progress (a wait or a load holds it), or null.</summary>
        public ScriptPlayRun Running { get; private set; }
        /// <summary>The last play that ended (its result, or its refusal), or null.</summary>
        public ScriptPlayResult LastPlay { get; private set; }
        /// <summary>The scene script the last play that ran played: a stop with reset resets its cast. Null before the first.</summary>
        public string LastScriptId { get; private set; }
        /// <summary>True while a recording a play started records.</summary>
        public bool Recording => watched != null && watched.IsActive;
        /// <summary>Recorded passes whose end paused the Timeline since the scene was opened.</summary>
        public int PassesPaused { get; private set; }

        /// <summary>
        /// Releases the slots of the script commands. The parts that hold them (moves, follow, depth of field) release their own, so that a move
        /// cut short writes the position it shows into the model (<paramref name="writeShown"/>); a slot of one of the three owners that no part
        /// holds is released through the layer. Returns the slots released.
        /// </summary>
        public int ReleaseCommandSlots(bool writeShown)
        {
            if (rt == null) return 0;
            int n = 0;
            foreach (var p in rt.Parts)
                if (p is IScriptCommandSlots s)
                {
                    try { n += s.ReleaseAll(writeShown); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("[script] " + p.GetType().Name + ".ReleaseAll threw: " + e); }
                }
            var layer = rt.Overrides;
            if (layer != null && layer.Any)
                foreach (var owner in new[] { OwnerMove, OwnerFollow, OwnerDof })
                    try { n += layer.ReleaseOwner(owner); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("[script] releasing the slots of " + owner + " threw: " + e); }
            return n;
        }

        /// <summary>The slots the script commands hold now: moves, follow, depth of field.</summary>
        public (int moves, int follow, int dof) SlotCounts()
        {
            var layer = rt?.Overrides;
            if (layer == null || !layer.Any) return (0, 0, 0);
            return (layer.Slots(null, OwnerMove).Count, layer.Slots(null, OwnerFollow).Count, layer.Slots(null, OwnerDof).Count);
        }

        // ------------------------------------------------------------ the player's calls

        internal void Begin(ScriptPlayRun run, string scriptId)
        {
            Running = run;
            Subscribe();
        }

        internal void End(ScriptPlayRun run)
        {
            if (ReferenceEquals(Running, run)) Running = null;
            LastPlay = run.Result;
            if (run.Result.Ok) LastScriptId = run.Result.Script;
        }

        /// <summary>Watches the recording a play started: when it stops (its pass recorded), the Timeline is paused.</summary>
        internal void WatchRecording(FrameRecorder rec)
        {
            watched = rec;
            Subscribe();
        }

        /// <summary>Stops watching (a stop of the script).</summary>
        internal void StopWatching() => watched = null;

        void Subscribe()
        {
            if (subscribed || rt == null) return;
            subscribed = true;
            rt.Session.Events.Cleared += OnSceneCleared;
        }

        void Unsubscribe()
        {
            if (!subscribed || rt == null) return;
            subscribed = false;
            rt.Session.Events.Cleared -= OnSceneCleared;
        }

        void OnSceneCleared()
        {
            watched = null;
            Running = null;
            LastPlay = null;
            LastScriptId = null;
            PassesPaused = 0;
            ReleaseCommandSlots(false);
            Unsubscribe();
        }

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r) { rt = r; }

        public void OnChanged(SceneChangeBatch b) { }

        public void Apply()
        {
            if (watched == null) return;
            if (watched.IsActive) return;
            // the recorded pass ended at the last late point: the Timeline is paused before it ticks again
            watched = null;
            var tlp = rt?.GetPart<TimelinePlayback>();
            if (tlp != null && tlp.Player.State == TimelinePlayState.Playing)
            {
                tlp.Player.Pause();
                PassesPaused++;
            }
        }

        public void LateApply() { }

        public void Clear() { watched = null; }

        public void OnLoadedRealized() { }

        public void Dispose()
        {
            Unsubscribe();
            watched = null;
            Running = null;
        }
    }
}
