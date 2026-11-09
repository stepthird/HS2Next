// Item motion (package B of Phase 3.1): animation, item FK and dynamic bones of
// one built item, built by the ItemMotionHooks.Built handler and stepped after the characters.
//
//   ItemMotion        one per built item that has a driven Animator, found FK bones or dynamic bones (else none: the
//                     stored anime / FK / dynamic-bone values are kept and nothing moves). It puts a CharaMotion component
//                     (the characters' orchestrator: phase order, exception isolation, Step(dt), TimeScale, Paused,
//                     MaxStep) on the instance root with AutoStep false and registers, in HS2's order inside an item:
//                       ItemAnimationStage (MotionPhase.Animation)   ItemMotionAnimation.cs
//                       ItemFkStage        (MotionPhase.Fk)          ItemMotionFk.cs
//                       DynamicBoneStage   (MotionPhase.DynamicBone) one per item, Context null, the characters' global
//                                          options (DynamicBoneSetup.Options: fixed 60 Hz with the cap, IllusionFixes chains,
//                                          the job-path colliders: plane colliders and FreezeAxis inert unless
//                                          IntendedBehaviour, T15); one chain per DynamicBone collected at the build
//                                          (active hierarchy), category Other, the serialized m_Colliders
//   stepping          SceneRuntime.StepItems(dt): every built item outside the graveyard in model pre-order (a parent item
//                     before its children); called by ItemMotionDriver (LateUpdate, order 1050: after CharaMotion 1000,
//                     before SceneRuntimeLateDriver 1100) in play mode and the player, by SceneRuntime.Tick with
//                     ManualStepping, and by AppCore.Frame of test hosts
//   build order       (the Built hook) FK bones and their base pose, the dynamic-bone chains from the fresh
//                     prefab pose, then the animation (pattern, speed, time; one evaluation with dt 0), then FK values and
//                     the dynamic-bone switch from the model
//   dynamic bones     effective = item.dynamicBones ∧ ¬(FK bones found ∧ item.fk.enabled), written into every collected
//                     component's `enabled` (HS2's ActiveDynamicBone / ActiveFK); the stage's chain switches then restore
//                     the Start pose (off) or reset the particles at the current bones (on)
//   no driven Animator  the first active Animator is disabled (HS2: enabled iff isAnime; without a controller it has
//                     nothing to play)
// Handlers are registered by Register() (from the SceneRuntime constructor, idempotent), so every runtime — the app's,
// the editor tests' — gets item motion.
using System;
using System.Collections.Generic;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ItemMotion : IDisposable
    {
        /// <summary>false: items get no motion (the 2.2 behaviour); read at the next build.</summary>
        public static bool Enabled = true;

        public readonly ItemRealization Item;
        /// <summary>The orchestrator on the instance root (AutoStep false).</summary>
        public CharaMotion Motion { get; private set; }
        public ItemAnimationStage Animation { get; private set; }
        public ItemFkStage Fk { get; private set; }
        public DynamicBoneStage Dynamic { get; private set; }
        /// <summary>The dynamic-bone components the chains were built from (the realization's collected list).</summary>
        public IReadOnlyList<global::DynamicBone> Components { get; private set; } = Array.Empty<global::DynamicBone>();
        /// <summary>The effective dynamic-bone state last applied.</summary>
        public bool DynamicActive { get; private set; }
        /// <summary>Global sequence number of this item's last step (frame-order tests: a parent steps before its children).</summary>
        public long LastStepSequence { get; private set; }
        /// <summary>Time.frameCount at the last step.</summary>
        public int LastStepFrame { get; private set; } = -1;
        /// <summary>The walk of the motion pass that stepped this item last (SceneRuntime.PassStamp): StepItems skips it in that frame.</summary>
        internal long LastPass = -1;

        static long sequence;
        /// <summary>The global step counter (every item step increments it).</summary>
        public static long Sequence => sequence;

        ItemMotion(ItemRealization item) { Item = item; }

        /// <summary>The motion of a built item, or null.</summary>
        public static ItemMotion Of(ItemRealization it) => it?.Motion as ItemMotion;

        ItemNodeState St => Item.Node?.State as ItemNodeState;

        // ------------------------------------------------------------ registration

        /// <summary>Registers the ItemMotionHooks handlers (idempotent).</summary>
        public static void Register()
        {
            ItemMotionHooks.Built = OnBuilt;
            ItemMotionHooks.Changed = OnChanged;
            ItemMotionHooks.Releasing = OnReleasing;
            ItemMotionHooks.FkBase = OnFkBase;
            ItemMotionHooks.Describe = OnDescribe;
            HS2Next.Scenes.Bake.BakeProvider.Register();
        }

        static void OnBuilt(ItemRealization it)
        {
            it.Motion = null;
            if (!it.Instance) return;
            if (!Enabled)
            {
                // the 2.2 behaviour without stepping
                if (it.Animator)
                {
                    it.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    it.Animator.enabled = it.Component != null && it.Component.isAnime != 0;
                }
                return;
            }
            var m = Build(it);
            if (m != null) it.Motion = m;
        }

        static void OnChanged(ItemRealization it, IReadOnlyCollection<string> paths) => Of(it)?.Apply(paths);

        static void OnReleasing(ItemRealization it)
        {
            var m = Of(it);
            if (m == null) return;
            m.Dispose();
        }

        static Vector3? OnFkBase(ItemRealization it, string bone) => Of(it)?.Fk?.BaseEuler(bone);

        static void OnDescribe(ItemRealization it, IDictionary<string, object> realized) => Describe(it, Of(it), realized);

        // ------------------------------------------------------------ build

        static ItemMotion Build(ItemRealization it)
        {
            var st = it.Node?.State as ItemNodeState;
            var go = it.Instance;
            var m = new ItemMotion(it);

            // 1. FK bones and their base pose (before anything moves them)
            var names = new List<string>();
            var bones = new List<Transform>();
            if (it.Features != null)
            {
                foreach (var n in it.Features.FkBones)
                {
                    var b = ItemDefaults.FindBone(go.transform, n);
                    if (b) { names.Add(n); bones.Add(b); }
                }
            }
            else if (it.Row?.Bones != null)
            {
                foreach (var n in it.Row.Bones)
                {
                    if (string.IsNullOrEmpty(n) || names.Contains(n)) continue;
                    var b = ItemDefaults.FindBone(go.transform, n);
                    if (b) { names.Add(n); bones.Add(b); }
                }
            }
            if (names.Count > 0) m.Fk = new ItemFkStage(names, bones);

            // 2. dynamic-bone chains from the fresh prefab pose
            if (DynamicBoneSetup.Enabled && it.DynamicBones.Count > 0)
            {
                var stage = new DynamicBoneStage(DynamicBoneSetup.Options.Clone());
                var comps = new List<global::DynamicBone>();
                foreach (var db in it.DynamicBones)
                {
                    if (!db) continue;
                    try
                    {
                        stage.Add(db, DynamicBoneCategory.Other);
                        comps.Add(db);
                    }
                    catch (Exception e)
                    {
                        it.Runtime.ReportOnce(FindingSeverity.Warning, "item.partFailed", it.NodeId, "dynamic bone " + db.name + " threw " + e.GetType().Name + ": " + e.Message);
                    }
                }
                if (stage.Chains.Count > 0) { m.Dynamic = stage; m.Components = comps; }
            }

            // 3. animation: the first active Animator, driven iff isAnime with a controller
            var a = it.Animator;
            if (a)
            {
                var ic = it.Component;
                var ctrl = a.runtimeAnimatorController;
                if (ic != null && ic.isAnime != 0 && ctrl != null)
                {
                    var pats = new List<(string, string, bool)>();
                    if (ic.animeInfos != null)
                        foreach (var ai in ic.animeInfos)
                            pats.Add((ai?.name, ai?.state, ai != null && !string.IsNullOrEmpty(ai.name) && !string.IsNullOrEmpty(ai.state)));
                    var s = new ItemAnimationStage(a, ctrl, pats, "HS2Next item " + it.NodeId);
                    if (s.Valid) m.Animation = s;
                    else
                    {
                        s.Dispose();
                        a.enabled = false;
                        it.Runtime.ReportOnce(FindingSeverity.Warning, "item.partFailed", it.NodeId, "the item's animation graph could not be created");
                    }
                }
                else a.enabled = false;
            }

            if (m.Animation == null && m.Fk == null && m.Dynamic == null) return null;

            var cm = go.GetComponent<CharaMotion>();
            m.Motion = cm ? cm : go.AddComponent<CharaMotion>();
            m.Motion.AutoStep = false;
            if (m.Animation != null) m.Motion.Add(m.Animation);
            if (m.Fk != null) m.Motion.Add(m.Fk);
            if (m.Dynamic != null) m.Motion.Add(m.Dynamic);
            HS2Next.Scenes.Bake.BakeFk.AddCorrection(it, m.Motion);

            // 4. the stored state
            if (st != null)
            {
                m.Animation?.Build(st.Anime.Pattern, st.Anime.Speed, st.Anime.Time);
                m.Fk?.Load(st.Fk);
            }
            m.ApplyDynamic();
            return m;
        }

        // ------------------------------------------------------------ changes

        /// <summary>Applies changed paths under item.anime / item.fk / item.dynamicBones.</summary>
        public void Apply(IReadOnlyCollection<string> paths)
        {
            var st = St;
            if (st == null || paths == null) return;
            bool pattern = false, speed = false, time = false, fk = false, dyn = false;
            foreach (var p in paths)
            {
                if (FieldPath.Overlaps(p, "item.anime.pattern")) pattern = true;
                if (FieldPath.Overlaps(p, "item.anime.speed")) speed = true;
                if (FieldPath.Overlaps(p, "item.anime.time")) time = true;
                if (FieldPath.Overlaps(p, "item.fk")) fk = true;
                if (FieldPath.Overlaps(p, "item.dynamicBones")) dyn = true;
            }
            var an = Animation;
            if (an != null && (pattern || speed || time))
            {
                if (speed) an.Speed = st.Anime.Speed;
                if (pattern) an.SetPattern(st.Anime.Pattern);
                if (time) an.SetTime(st.Anime.Time);
                if (pattern || time) an.EvaluateNow();
            }
            if (fk && Fk != null) Fk.Load(st.Fk);
            else if (an != null && (pattern || time) && Fk != null) Fk.Write();   // the evaluation above wrote the animated pose
            if (fk || dyn) ApplyDynamic();
        }

        /// <summary>The animation panel's restart (not a model change).</summary>
        public void Restart()
        {
            if (Animation == null) return;
            Animation.Restart();
            Fk?.Write();
        }

        /// <summary>Effective dynamic bones = item.dynamicBones ∧ ¬(FK bones ∧ FK on), into the components and the chains.</summary>
        public void ApplyDynamic()
        {
            if (Dynamic == null) return;
            var st = St;
            bool on = st == null || st.DynamicBones;
            if (Fk != null && Fk.Count > 0 && st != null && st.Fk.Enabled) on = false;
            DynamicActive = on;
            foreach (var c in Components) if (c && c.enabled != on) c.enabled = on;
            Dynamic.ApplySwitches();
        }

        // ------------------------------------------------------------ stepping

        /// <summary>One step of the item's stages (dt unscaled; the motion's TimeScale / Paused / MaxStep apply).</summary>
        public void Step(float dt)
        {
            if (!Motion) return;
            Motion.Step(dt);
            LastStepSequence = ++sequence;
            LastStepFrame = UnityEngine.Time.frameCount;
        }

        public void Dispose()
        {
            Animation?.Dispose();
            Animation = null;
        }

        // ------------------------------------------------------------ item.state

        /// <summary>item.state's realized members of the motion: animator (HS2's names), fkEnabled, dynamicBonesEnabled, motion.</summary>
        public static void Describe(ItemRealization it, ItemMotion m, IDictionary<string, object> r)
        {
            var a = it.Animator;
            var st = it.Node?.State as ItemNodeState;
            var an = new Dictionary<string, object>
            {
                ["present"] = (bool)a,
                ["enabled"] = a && a.enabled,
                ["driven"] = m?.Animation != null,
                ["name"] = a ? a.name : null,
                ["speed"] = st != null ? (double)st.Anime.Speed : 1.0,
                ["pattern"] = st?.Anime.Pattern ?? 0,
            };
            var s = m?.Animation;
            if (s != null && s.Valid && s.Driver.LayerCount > 0)
            {
                var info = s.StateInfo;
                an["stateHash"] = info.shortNameHash;
                an["fullPathHash"] = info.fullPathHash;
                an["state"] = s.StateName;
                an["time"] = (double)info.normalizedTime;
                an["length"] = (double)info.length;
                an["advanced"] = s.Advanced;
            }
            r["animator"] = an;
            r["fkEnabled"] = m?.Fk != null && m.Fk.On;
            int enabled = 0;
            if (m != null) foreach (var c in m.Components) if (c && c.enabled) enabled++;
            r["dynamicBonesEnabled"] = enabled;
            if (m == null || !m.Motion) { r["motion"] = null; return; }
            var stages = new List<object>();
            foreach (var x in m.Motion.Stages) stages.Add(x.Name);
            var mo = new Dictionary<string, object>
            {
                ["stages"] = stages,
                ["paused"] = m.Motion.Paused,
                ["timeScale"] = (double)m.Motion.TimeScale,
                ["time"] = (double)m.Motion.Time,
                ["steps"] = m.Motion.StepCount,
                ["errors"] = m.Motion.Errors,
            };
            if (m.Fk != null) mo["fkBones"] = m.Fk.Count;
            if (m.Dynamic != null)
            {
                mo["chains"] = m.Dynamic.Chains.Count;
                mo["activeChains"] = m.Dynamic.ActiveChains;
                mo["particles"] = m.Dynamic.ParticleCount;
            }
            r["motion"] = mo;
        }
    }
}
