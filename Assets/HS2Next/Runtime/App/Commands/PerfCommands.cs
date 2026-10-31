// app.perf (group "app", read-only, owner core, Since 1.0.0, experimental; CoreCommands.Phase52Names): the frame times of play mode and the
// player since the last reset (FrameStats, one sample per frame), the motion stages' times per phase summed over the
// characters of a frame (collected only from the first request with motion: a Stopwatch around each stage of CharaMotion),
// the dynamic-bone stages (bust and dynamic bones) with their chain and particle counts, the process memory, the catalog's
// UnloadUnusedAssets calls and, on request, the engine object counts. What the one-hour Maker session samples with
// content.stats.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Profiling;

namespace HS2Next.App.Commands
{
    public static class PerfCommands
    {
        /// <summary>The commands of this file (in CoreCommands.Phase52Names).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "app.perf" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("app.perf", "Performance numbers of the running app (read-only): frame times since the last reset, the motion stages' times per phase (from the first request with motion on), the dynamic-bone stages, process memory, the UnloadUnusedAssets calls and, on request, the engine object counts.", "app")
                .Arg("reset", ArgTypes.Bool, "after this reply, start the frame and motion samples anew", false, false)
                .Arg("motion", ArgTypes.Bool, "time the motion stages from now on (they are not timed until a request asks once) and add the motion section", false, false)
                .Arg("objects", ArgTypes.Bool, "add the engine object counts (gameObjects, materials, meshes, textures, renderTextures)", false, false)
                .Arg("census", ArgTypes.Bool, "with objects: add the GameObjects and Materials by name (leak diagnosis)", false, false)
                .Return("frames", ArgTypes.Int, "frames sampled since the last reset (play mode and the player; 0 in edit-mode hosts)")
                .Return("frameMs", ArgTypes.Object, "{median, p95, max} of the frame times since the last reset")
                .Return("motion", ArgTypes.Object, "with motion: {timed (bool), frames, stages {<phase>: {medianMs, p95Ms}} (per frame, summed over the characters), dynamicBone {chains, particles, medianMs, p95Ms} (the bust and dynamic-bone stages)}", false)
                .Return("memory", ArgTypes.Object, "{managedBytes, unityAllocatedBytes, graphicsDriverBytes (-1 when the player is not a development build), monoHeapBytes}")
                .Return("unloadUnused", ArgTypes.Object, "{calls, started, medianMs, maxMs}: Resources.UnloadUnusedAssets calls the catalog made or counted (calls: ended; started: counted at their start)")
                .Return("objects", ArgTypes.Object, "with objects: {gameObjects, materials, meshes, textures, renderTextures, census? {gameObjects {name: count}, materials {name: count}} (with census; a GameObject outside every scene is named with \" [asset]\")}", false)
                .Example("{\"cmd\":\"app.perf\",\"motion\":true,\"reset\":true}")
                .WithFlags(CommandFlags.ReadOnly | CommandFlags.Experimental), Perf);
        }

        static object Perf(CoreContext c)
        {
            bool reset = c.Args.Get("reset", false), motion = c.Args.Get("motion", false), objects = c.Args.Get("objects", false),
                census = c.Args.Get("census", false);
            var (med, p95, max) = FrameStats.Summary();
            var r = new BridgeReply().With("frames", FrameStats.Frames)
                .With("frameMs", new BridgeReply().With("median", Round(med)).With("p95", Round(p95)).With("max", Round(max)));
            if (motion)
            {
                bool timed = MotionTimings.Active;
                MotionTimings.Start();
                r["motion"] = MotionTimings.Reply(timed);
            }
            r["memory"] = new BridgeReply().With("managedBytes", GC.GetTotalMemory(false))
                .With("unityAllocatedBytes", Profiler.GetTotalAllocatedMemoryLong())
                .With("graphicsDriverBytes", Debug.isDebugBuild ? Profiler.GetAllocatedMemoryForGraphicsDriver() : -1L)
                .With("monoHeapBytes", Profiler.GetMonoHeapSizeLong());
            var cat = c.Services?.Catalog;
            r["unloadUnused"] = new BridgeReply().With("calls", cat?.UnloadUnusedCalls ?? 0).With("started", cat?.UnloadUnusedStarted ?? 0)
                .With("medianMs", Round(cat?.UnloadUnusedMedianMs ?? 0)).With("maxMs", Round(cat?.UnloadUnusedMaxMs ?? 0));
            if (objects)
                r["objects"] = new BridgeReply().With("gameObjects", Resources.FindObjectsOfTypeAll<GameObject>().Length)
                    .With("materials", Resources.FindObjectsOfTypeAll<Material>().Length).With("meshes", Resources.FindObjectsOfTypeAll<Mesh>().Length)
                    .With("textures", Resources.FindObjectsOfTypeAll<Texture>().Length).With("renderTextures", Resources.FindObjectsOfTypeAll<RenderTexture>().Length);
            if (objects && census)
            {
                var gos = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (g) { string n = (string.IsNullOrEmpty(g.name) ? "(unnamed)" : g.name) + (g.scene.IsValid() ? "" : " [asset]"); gos.TryGetValue(n, out int k); gos[n] = k + 1; }
                var mats = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m) { string n = string.IsNullOrEmpty(m.name) ? "(unnamed)" : m.name; mats.TryGetValue(n, out int k); mats[n] = k + 1; }
                var cg = new BridgeReply(); foreach (var kv in gos) cg[kv.Key] = kv.Value;
                var cm = new BridgeReply(); foreach (var kv in mats) cm[kv.Key] = kv.Value;
                ((BridgeReply)r["objects"])["census"] = new BridgeReply().With("gameObjects", cg).With("materials", cm);
            }
            if (reset)
            {
                FrameStats.Reset();
                MotionTimings.Reset();
            }
            return r;
        }

        static double Round(double v) => Math.Round(v, 3);
    }

    /// <summary>
    /// The motion stages' step times per frame (Time.frameCount), summed over the characters stepped in that frame, by phase;
    /// the bust and dynamic-bone phases also as one "dynamicBone" figure with the frame's chain and particle counts. Collects
    /// only while it is CharaMotion's timing sink (from the first app.perf with motion on to the end of the process).
    /// </summary>
    public sealed class MotionTimings : IMotionTimingSink
    {
        static MotionTimings instance;
        const int Cap = 1 << 16;

        /// <summary>True once the motion stages are timed.</summary>
        public static bool Active => instance != null && ReferenceEquals(CharaMotion.TimingSink, instance);

        public static void Start()
        {
            instance ??= new MotionTimings();
            CharaMotion.TimingSink = instance;
        }

        public static void Reset() => instance?.Clear();

        readonly Dictionary<MotionPhase, List<double>> perPhase = new Dictionary<MotionPhase, List<double>>();
        readonly List<double> dynamicBone = new List<double>();
        readonly Dictionary<MotionPhase, long> current = new Dictionary<MotionPhase, long>();
        int currentFrame = -1, frames, chains, particles, lastChains, lastParticles;
        long currentDb;
        readonly HashSet<DynamicBoneStage> counted = new HashSet<DynamicBoneStage>();

        public void StageStepped(CharaMotion motion, IMotionStage stage, long ticks)
        {
            int f = Time.frameCount;
            if (f != currentFrame) { Flush(); currentFrame = f; }
            current.TryGetValue(stage.Phase, out var t);
            current[stage.Phase] = t + ticks;
            if (stage.Phase == MotionPhase.DynamicBone || stage.Phase == MotionPhase.BustPhysics) currentDb += ticks;
            if (stage is DynamicBoneStage db && counted.Add(db)) { chains += db.Chains.Count; particles += db.ParticleCount; }
        }

        // the finished frame's sums into the samples
        void Flush()
        {
            if (current.Count == 0) return;
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            foreach (var kv in current)
            {
                if (!perPhase.TryGetValue(kv.Key, out var l)) perPhase[kv.Key] = l = new List<double>();
                if (l.Count >= Cap) l.RemoveRange(0, Cap / 2);
                l.Add(kv.Value * toMs);
            }
            if (dynamicBone.Count >= Cap) dynamicBone.RemoveRange(0, Cap / 2);
            dynamicBone.Add(currentDb * toMs);
            lastChains = chains; lastParticles = particles;
            frames++;
            current.Clear();
            counted.Clear();
            currentDb = 0; chains = particles = 0;
        }

        void Clear()
        {
            perPhase.Clear(); dynamicBone.Clear(); current.Clear(); counted.Clear();
            currentDb = 0; chains = particles = 0; frames = 0; currentFrame = -1;
        }

        public static BridgeReply Reply(bool timed)
        {
            var m = instance;
            var stages = new BridgeReply();
            var r = new BridgeReply().With("timed", timed).With("frames", m?.frames ?? 0).With("stages", stages);
            if (m == null) return r.With("dynamicBone", new BridgeReply().With("chains", 0).With("particles", 0).With("medianMs", 0.0).With("p95Ms", 0.0));
            foreach (var kv in m.perPhase.OrderBy(kv => (int)kv.Key))
            {
                var (med, p95, _) = FrameStats.Percentiles(kv.Value);
                stages[kv.Key.ToString()] = new BridgeReply().With("medianMs", Math.Round(med, 4)).With("p95Ms", Math.Round(p95, 4));
            }
            var (dm, dp, _) = FrameStats.Percentiles(m.dynamicBone);
            return r.With("dynamicBone", new BridgeReply().With("chains", m.lastChains).With("particles", m.lastParticles)
                .With("medianMs", Math.Round(dm, 4)).With("p95Ms", Math.Round(dp, 4)));
        }
    }
}
