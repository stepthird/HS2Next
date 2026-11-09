// The moves of obj.moveTo: a scene part that holds one slot `transform.pos` per moving node (owner "move", priority 1: above the Timeline, below the
// constraints) and sets it every tick to lerp(from, end, min(1, elapsed / max(0.05, time))), elapsed advancing by the scene clock's delta (the pause holds
// a move, a step steps it, a recording at a fixed step steps it exactly). At its end a move writes the end into the model (transform.pos, no history) and
// releases its slot. A new move of the same node replaces the running one. A move released early (ReleaseAll, Release) writes the position it shows into
// the model when the node is in the scene; a slot the layer released (the scene cleared, the node forgotten) ends its move with no write, and a move whose
// node left the scene (deleted) releases its slot with no write.
// The arithmetic (Plan, StanceSpeed) is the automation vocabulary's: d = target - from with d.y = 0; the end is from + d^ (|d| - stop) when |d| > stop,
// else from; the yaw atan2(d.x, d.z) (none when |d|^2 <= 1e-6); a character's animation speed clamp(|end - from| / time / stanceSpeed, 0.2, 2).
// A scene with no move does nothing: the tick returns at its first branch; the part subscribes to nothing.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>
    /// A part that holds slots for the generic commands of scene scripts (moves, the camera follow, the dof command): the script player releases them all at a
    /// play's start, at its stop and when the scene is cleared. Releasing twice is harmless.
    /// </summary>
    public interface IScriptCommandSlots
    {
        /// <summary>Releases every slot of the part; the count released. <paramref name="writeShown"/>: a move cut short writes the position it shows into the model when its node is in the scene.</summary>
        int ReleaseAll(bool writeShown);
    }

    /// <summary>Where a move goes: its end, its yaw and the distance it covers.</summary>
    public struct MovePlan
    {
        public Vector3 End;
        /// <summary>The yaw in degrees; null when the target lies on the start (|d|^2 &lt;= 1e-6): the old yaw stays.</summary>
        public float? Yaw;
        /// <summary>|end - from|.</summary>
        public float Distance;
    }

    public sealed class ScriptedMoves : ISceneRuntimePart, ISceneTickPart, IScriptCommandSlots, IDisposable
    {
        /// <summary>The override owner of the moves' slots.</summary>
        public const string Owner = "move";
        /// <summary>The moves' priority: above the Timeline (0), below the constraints (10).</summary>
        public const int Priority = 1;
        /// <summary>The shortest duration of a move, in seconds.</summary>
        public const float MinTime = 0.05f;
        /// <summary>The bounds of a character's animation speed during a move.</summary>
        public const float MinStance = 0.2f, MaxStance = 2f;

        sealed class Move
        {
            public string NodeId;
            public OverrideSlot Slot;
            public Vector3 From, End;
            public float Time, Elapsed;
        }

        readonly Dictionary<string, Move> moves = new Dictionary<string, Move>(StringComparer.Ordinal);
        readonly List<Move> scratch = new List<Move>();
        SceneRuntime rt;

        /// <summary>The scene's part, added when a runtime has none.</summary>
        public static ScriptedMoves Of(SceneRuntime rt)
        {
            if (rt == null) return null;
            var p = rt.GetPart<ScriptedMoves>();
            if (p != null) return p;
            p = new ScriptedMoves();
            rt.AddPart(p);
            return p;
        }

        /// <summary>Moves running now.</summary>
        public int Count => moves.Count;
        /// <summary>The nodes that move now (no order).</summary>
        public IEnumerable<string> Nodes => moves.Keys;

        // ------------------------------------------------------------ the arithmetic

        /// <summary>The end, the yaw and the distance of a move from <paramref name="from"/> towards <paramref name="target"/>, stopping <paramref name="stop"/> units before it.</summary>
        public static MovePlan Plan(Vector3 from, Vector3 target, float stop)
        {
            double dx = (double)target.x - from.x, dz = (double)target.z - from.z;
            double len2 = dx * dx + dz * dz, len = Math.Sqrt(len2);
            var plan = new MovePlan { End = from, Yaw = null, Distance = 0f };
            if (len > stop && len > 0)
            {
                double k = (len - stop) / len;
                plan.End = new Vector3((float)(from.x + dx * k), from.y, (float)(from.z + dz * k));
            }
            if (len2 > 1e-6) plan.Yaw = (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
            plan.Distance = (plan.End - from).magnitude;
            return plan;
        }

        /// <summary>A character's animation speed for a move of <paramref name="distance"/> over <paramref name="time"/> seconds (at least <see cref="MinTime"/>).</summary>
        public static float StanceSpeed(float distance, float time, float stanceSpeed)
        {
            double v = distance / (double)Math.Max(MinTime, time) / stanceSpeed;
            if (double.IsNaN(v)) v = MinStance;
            return (float)Math.Max(MinStance, Math.Min(MaxStance, v));
        }

        // ------------------------------------------------------------ moves

        /// <summary>
        /// The position the node shows: the winning `transform.pos` slot's value, else the model's. Null for a node not in the scene.
        /// </summary>
        public static Vector3? Shown(SceneRuntime rt, string nodeId)
        {
            var n = rt?.Model.Find(nodeId);
            if (n == null) return null;
            var w = rt.Overrides.WinnerOf(nodeId, OverrideChannel.TransformPos);
            if (w != null && w.HasValue) return new Vector3((float)w[0], (float)w[1], (float)w[2]);
            var p = n.Doc.Transform?.Pos ?? default;
            return new Vector3(p.X, p.Y, p.Z);
        }

        /// <summary>
        /// Starts a move of <paramref name="nodeId"/> from <paramref name="from"/> to <paramref name="end"/> over max(0.05, <paramref name="time"/>) seconds of
        /// the scene clock, replacing a running move of that node; the slot shows <paramref name="from"/> at once. False when the layer refuses the node.
        /// </summary>
        public bool Start(string nodeId, Vector3 from, Vector3 end, float time)
        {
            if (rt == null || nodeId == null) return false;
            if (!moves.TryGetValue(nodeId, out var m) || m.Slot == null || m.Slot.IsReleased)
            {
                var slot = rt.Overrides.Acquire(Owner, nodeId, "transform.pos", Priority);
                if (slot == null) return false;
                m = new Move { NodeId = nodeId, Slot = slot };
                moves[nodeId] = m;
            }
            m.From = from;
            m.End = end;
            m.Time = Math.Max(MinTime, time);
            m.Elapsed = 0f;
            m.Slot.SetVector(from.x, from.y, from.z);
            return true;
        }

        /// <summary>Ends the move of a node early: its shown position goes into the model, its slot is released. False when it has none.</summary>
        public bool Release(string nodeId)
        {
            if (nodeId == null || !moves.TryGetValue(nodeId, out var m)) return false;
            EndEarly(m);
            moves.Remove(nodeId);
            return true;
        }

        /// <summary>Ends every move early (the script's stop): each shown position goes into the model, every slot is released. The count ended.</summary>
        public int ReleaseAll() => ReleaseAll(true);

        /// <summary>Ends every move early; with <paramref name="writeShown"/> each shown position goes into the model (no history). The count ended.</summary>
        public int ReleaseAll(bool writeShown)
        {
            if (moves.Count == 0) return 0;
            scratch.Clear();
            scratch.AddRange(moves.Values);
            moves.Clear();
            int k = 0;
            foreach (var m in scratch)
                if (EndEarly(m, writeShown)) k++;
            scratch.Clear();
            return k;
        }

        bool EndEarly(Move m, bool writeShown = true)
        {
            if (m.Slot == null || m.Slot.IsReleased) return false;
            if (writeShown && rt.Model.Find(m.NodeId) != null && m.Slot.HasValue) Write(m.NodeId, new Vector3((float)m.Slot[0], (float)m.Slot[1], (float)m.Slot[2]));
            m.Slot.Release();
            return true;
        }

        CommandResult Write(string nodeId, Vector3 p) =>
            rt.Session.Set(nodeId, "transform.pos", new Vec3(p.x, p.y, p.z), new ExecuteOptions { Record = HistoryMode.None, Source = CommandSource.Script });

        // ------------------------------------------------------------ ISceneTickPart

        public void Tick(float dt)
        {
            if (moves.Count == 0) return;
            if (!(dt >= 0f) || float.IsInfinity(dt)) dt = 0f;
            scratch.Clear();
            scratch.AddRange(moves.Values);
            foreach (var m in scratch)
            {
                if (m.Slot == null || m.Slot.IsReleased)   // the layer released it: the scene was cleared or the node forgotten
                {
                    moves.Remove(m.NodeId);
                    continue;
                }
                if (rt.Model.Find(m.NodeId) == null)   // deleted: nothing to write into
                {
                    m.Slot.Release();
                    moves.Remove(m.NodeId);
                    continue;
                }
                m.Elapsed += dt;
                float u = Math.Min(1f, m.Elapsed / m.Time);
                if (u >= 1f)
                {
                    var r = Write(m.NodeId, m.End);
                    if (r.Status == CommandStatus.Error && r.Code == CommandCodes.Busy)   // the bus cannot take it now: the end shows, the write waits for the next tick
                    {
                        m.Slot.SetVector(m.End.x, m.End.y, m.End.z);
                        continue;
                    }
                    m.Slot.Release();
                    moves.Remove(m.NodeId);
                    continue;
                }
                var p = Vector3.LerpUnclamped(m.From, m.End, u);
                m.Slot.SetVector(p.x, p.y, p.z);
            }
            scratch.Clear();
        }

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r) { rt = r; }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void LateApply() { }
        /// <summary>The realization was cleared: a move whose slot the layer released ends at the next tick; one that stays goes on (the node keeps its id).</summary>
        public void Clear() { }
        public void OnLoadedRealized() { }

        public void Dispose()
        {
            foreach (var m in moves.Values)
                if (m.Slot != null && !m.Slot.IsReleased) m.Slot.Release();
            moves.Clear();
        }
    }
}
