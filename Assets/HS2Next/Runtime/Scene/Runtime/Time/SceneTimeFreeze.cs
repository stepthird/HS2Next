// The global pause for the scene's time consumers that run on Unity time (R22;
// Phase 3.6 package K): particle systems of items and of the map, the map's native Animators and WindZones, item
// VideoPlayers. Best effort, outside the bit contract (random seeds, Unity's own update):
//   pause    every playing particle system of the built items and of the loaded map: Pause (remembered); map Animators
//            (enabled, speed ≠ 0): speed stored and set 0; WindZones: main / turbulence / pulse magnitude stored and set 0;
//            item VideoPlayers playing: Pause (remembered)
//   paused   each frame (tick dt 0): item particle systems that started while paused (an item shown or added, a rebuild)
//            are paused and remembered as well; a map loaded while paused is frozen when it is Loaded
//   step     (tick dt > 0 while paused) each remembered particle system: Simulate(dt, withChildren: false, restart: false,
//            fixedTimeStep: false) (each system is remembered on its own, so children are not simulated twice); each map
//            Animator: speed back to the stored value, Update(dt), speed 0; each remembered VideoPlayer: frame +=
//            round(dt × frameRate)
//   resume   remembered particle systems Play again unless their item is hidden now (an item's particles follow its
//            visibility, ItemRealization.ApplyVisibility); Animator speeds and wind values restored; videos Play
// Registered by SceneRuntime.AddStandardParts. Play mode only (particles and videos do not run in edit mode); the part does
// nothing in edit mode except the bookkeeping.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneTimeFreeze : ISceneRuntimePart, ISceneTickPart, IScenePauseListener
    {
        SceneRuntime rt;
        bool frozen;
        int mapGeneration = -1;
        bool mapFrozen;

        sealed class Ps { public ParticleSystem System; public ItemRealization Item; }
        readonly List<Ps> particles = new List<Ps>();
        readonly HashSet<ParticleSystem> known = new HashSet<ParticleSystem>();
        readonly List<(Animator a, float speed)> animators = new List<(Animator, float)>();
        readonly List<(WindZone w, float main, float turbulence, float pulse)> winds = new List<(WindZone, float, float, float)>();
        readonly List<VideoPlayer> videos = new List<VideoPlayer>();

        /// <summary>Counters of the current pause (tests, R-M4).</summary>
        public int PausedParticles => particles.Count;
        public int PausedAnimators => animators.Count;
        public int PausedWinds => winds.Count;
        public int PausedVideos => videos.Count;
        /// <summary>Milliseconds of the last freeze / thaw.</summary>
        public double LastFreezeMs { get; private set; }
        public double LastThawMs { get; private set; }

        public void Attach(SceneRuntime r) => rt = r;
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void LateApply() { }
        public void OnLoadedRealized() { }

        public void Clear()
        {
            if (frozen) Thaw();
            Forget();
        }

        public void PausedChanged(bool paused)
        {
            if (paused) Freeze(); else Thaw();
        }

        public void Tick(float dt)
        {
            if (!frozen || rt == null || !rt.Paused) return;
            if (!Application.isPlaying) return;
            CatchItems();
            CatchMap();
            if (dt > 0f) StepFrozen(dt);
        }

        // ------------------------------------------------------------ freeze / thaw

        void Freeze()
        {
            if (frozen) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            frozen = true;
            if (Application.isPlaying)
            {
                CatchItems();
                CatchMap();
            }
            LastFreezeMs = sw.Elapsed.TotalMilliseconds;
        }

        void Thaw()
        {
            if (!frozen) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            frozen = false;
            foreach (var p in particles)
            {
                if (!p.System) continue;
                bool visible = p.Item == null || (p.Item.Node != null && p.Item.Node.EffectiveVisible && !p.Item.InGraveyard && rt != null && rt.IsInModel(p.Item));
                try { if (visible) p.System.Play(false); }
                catch (Exception e) { Debug.LogWarning("scene time: resuming a particle system threw: " + e.Message); }
            }
            foreach (var (a, speed) in animators) if (a) a.speed = speed;
            foreach (var (w, main, turbulence, pulse) in winds)
                if (w) { w.windMain = main; w.windTurbulence = turbulence; w.windPulseMagnitude = pulse; }
            foreach (var v in videos)
                if (v)
                {
                    try { v.Play(); }
                    catch (Exception e) { Debug.LogWarning("scene time: resuming a video threw: " + e.Message); }
                }
            Forget();
            LastThawMs = sw.Elapsed.TotalMilliseconds;
        }

        void Forget()
        {
            particles.Clear();
            known.Clear();
            animators.Clear();
            winds.Clear();
            videos.Clear();
            mapFrozen = false;
            mapGeneration = -1;
        }

        void Hold(ParticleSystem p, ItemRealization owner)
        {
            if (!p || !p.isPlaying) return;
            p.Pause(false);
            if (known.Add(p)) particles.Add(new Ps { System = p, Item = owner });
        }

        /// <summary>Item particle systems playing now (also those that started while paused) and item videos.</summary>
        void CatchItems()
        {
            if (rt == null) return;
            foreach (var it in rt.Items())
            {
                var l = it.Particles;
                for (int i = 0; i < l.Count; i++) Hold(l[i], it);
                if (!it.Instance) continue;
                foreach (var v in it.Instance.GetComponentsInChildren<VideoPlayer>(false))
                    if (v && v.isPlaying && !videos.Contains(v)) { v.Pause(); videos.Add(v); }
            }
        }

        /// <summary>The loaded map's particle systems, Animators and WindZones (once per map load).</summary>
        void CatchMap()
        {
            var map = rt?.GetPart<MapRealization>();
            if (map == null || map.State != MapState.Loaded) return;
            if (mapFrozen && mapGeneration == map.Generation) return;
            mapFrozen = true;
            mapGeneration = map.Generation;
            foreach (var root in map.Roots)
            {
                if (!root) continue;
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(false)) Hold(p, null);
                foreach (var a in root.GetComponentsInChildren<Animator>(false))
                    if (a && a.enabled && a.speed != 0f) { animators.Add((a, a.speed)); a.speed = 0f; }
                foreach (var w in root.GetComponentsInChildren<WindZone>(false))
                    if (w)
                    {
                        winds.Add((w, w.windMain, w.windTurbulence, w.windPulseMagnitude));
                        w.windMain = 0f; w.windTurbulence = 0f; w.windPulseMagnitude = 0f;
                    }
            }
        }

        void StepFrozen(float dt)
        {
            foreach (var p in particles)
            {
                if (!p.System) continue;
                try { p.System.Simulate(dt, false, false, false); }
                catch (Exception e) { Debug.LogWarning("scene time: simulating a particle system threw: " + e.Message); }
            }
            for (int i = 0; i < animators.Count; i++)
            {
                var (a, speed) = animators[i];
                if (!a || !a.isActiveAndEnabled) continue;
                a.speed = speed;
                try { a.Update(dt); }
                finally { a.speed = 0f; }
            }
            foreach (var v in videos)
            {
                if (!v || !v.canStep || v.frameRate <= 0f) continue;
                long add = (long)Math.Round(dt * v.frameRate);
                if (add > 0) v.frame = Math.Min(v.frame + add, (long)v.frameCount - 1);
            }
        }
    }
}
