// Item motion commands of Phase 3.1 (package B):
//   item.anime    the item's animation: pattern, speed and time are model fields (item.anime.*, one history entry "Item
//                 animation" for all of them); restart replays the current state from 0 (runtime only, not recorded).
//                 Without any argument it only reports. The reply has the live state: the playing anime-info state name, its
//                 hash and the layer-0 normalized time of the driver.
//   item.motion   the runtime switches of the item's motion (CharaMotion.Paused / TimeScale on the instance root; not saved,
//                 not recorded, gone after a rebuild) and resetPhysics (the dynamic-bone chains' history dropped).
// Also the item forms used by FkPoseCommands (fk.list / fk.set / fk.enable / fk.capture) and DumpCommands (bones.dump):
// BuiltItem, the FK capture rule (enabling FK first writes the missing bones' base rotations).
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class ItemMotionCommands
    {
        /// <summary>The commands of this file.</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "item.anime", "item.motion" };

        public const float MaxAnimeSpeed = 3f;
        public const float MaxTimeScale = 10f;

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("item.anime", "An item's animation: pattern, speed (0–3) and normalized time as one undo entry, restart (not recorded); without arguments the current state.", "item motion")
                    .Arg("key", ArgTypes.Node, "the item node", true)
                    .Arg("pattern", ArgTypes.Int, "item.anime.pattern: index into the item's anime patterns (a pattern without a valid entry only stores the value)")
                    .Arg("speed", ArgTypes.Float, "item.anime.speed (0–3)")
                    .Arg("time", ArgTypes.Float, "item.anime.time: layer-0 normalized time of the current state (of the new one when pattern is given too)")
                    .Arg("restart", ArgTypes.Bool, "replay the current state from 0 (after the other arguments; not recorded)", false, false)
                    .Return("key", ArgTypes.String, "node id")
                    .Return("pattern", ArgTypes.Int, "stored item.anime.pattern").Return("speed", ArgTypes.Float, "stored item.anime.speed")
                    .Return("time", ArgTypes.Float, "the driver's layer-0 normalized time; the stored item.anime.time without a driven Animator")
                    .Return("storedTime", ArgTypes.Float, "item.anime.time in the model (written by captures)")
                    .Return("driven", ArgTypes.Bool, "the item has a driven Animator (isAnime with a controller)")
                    .Return("state", ArgTypes.String, "the anime-info state name of the playing layer-0 state, or null")
                    .Return("stateHash", ArgTypes.Int, "short name hash of the playing layer-0 state", false)
                    .Return("patterns", ArgTypes.ArrayOf(ArgTypes.Object), "[{name, state, valid}] of the ItemComponent's animeInfos")
                    .Example("{\"cmd\":\"item.anime\",\"key\":\"<id>\",\"pattern\":1,\"speed\":0.5}")
                    .WithFlags(CommandFlags.Mutates).WithTimeout(60),
                Anime);
            CommandKit.Register(r, app, CommandKit.Spec("item.motion", "Runtime switches of an item's motion (pause, time scale) and a physics reset; not saved, not recorded.", "item motion")
                    .Arg("key", ArgTypes.Node, "the item node", true)
                    .Arg("paused", ArgTypes.Bool, "CharaMotion.Paused of the item (animation, FK and dynamic bones step with dt 0)")
                    .Arg("timeScale", ArgTypes.Float, "CharaMotion.TimeScale of the item (0–10)")
                    .Arg("resetPhysics", ArgTypes.Bool, "drop the dynamic-bone chains' history (particles at the current bones)", false, false)
                    .Return("key", ArgTypes.String, "node id")
                    .Return("motion", ArgTypes.Bool, "the item has motion (a driven Animator, FK bones or dynamic bones)")
                    .Return("paused", ArgTypes.Bool).Return("timeScale", ArgTypes.Float)
                    .Return("time", ArgTypes.Float, "scaled seconds stepped").Return("steps", ArgTypes.Int).Return("errors", ArgTypes.Int)
                    .Return("stages", ArgTypes.ArrayOf(ArgTypes.Object), "[{name, phase, enabled}] in step order")
                    .Return("animation", ArgTypes.Object, "{state, time, speed} when driven", false)
                    .Return("fk", ArgTypes.Object, "{on, bones}", false)
                    .Return("dynamicBones", ArgTypes.Object, "{active, chains, activeChains, particles}", false)
                    .WithFlags(CommandFlags.Mutates).WithTimeout(60),
                Motion);
        }

        // ------------------------------------------------------------ helpers (also FkPoseCommands, DumpCommands)

        /// <summary>The built item of node <paramref name="n"/> (unavailable otherwise).</summary>
        public static ItemRealization BuiltItem(CoreContext c, TreeNode n, string arg = "key")
        {
            c.Runtime.ApplyNow();
            var r = c.Runtime.FindItem(n.Id);
            if (r == null || r.State != RealizationState.Built || r.InGraveyard || !r.Instance)
                throw CommandKit.NotRealized("item " + n.Id + " is not built (" + (r?.State.ToString() ?? "NotRealized") + (r?.Detail != null ? ": " + r.Detail : "") + ")",
                    arg, r?.State.ToString() ?? "NotRealized");
            return r;
        }

        /// <summary>An item node (wrong_kind otherwise).</summary>
        public static TreeNode ItemNode(CoreContext c, string arg = "key")
        {
            var n = CommandKit.Node(c, arg);
            if (n.Kind != NodeKind.Item) throw new BridgeException(BridgeError.WrongKind("node " + n.Id + " is a " + CommandKit.KindName(n) + ", not an item", arg, "item"));
            return n;
        }

        public static ItemNodeState StateOf(TreeNode n) => n.State as ItemNodeState ?? throw new BridgeException(BridgeCodes.Internal, "item node without item state");

        /// <summary>The found FK bone names of a built item (ItemFeatures.FkBones; the motion's FK stage order when built).</summary>
        public static IReadOnlyList<string> FkBones(ItemRealization r)
        {
            var fk = ItemMotion.Of(r)?.Fk;
            if (fk != null) return fk.Names;
            return r.Features != null ? (IReadOnlyList<string>)r.Features.FkBones : Array.Empty<string>();
        }

        /// <summary>The base rotation (local Euler) of a found item FK bone: the motion's base, else the bone's current one.</summary>
        public static Vector3? FkBase(ItemRealization r, string name)
        {
            var e = ItemMotion.Of(r)?.Fk?.BaseEuler(name);
            if (e != null) return e;
            var b = r.Instance ? ItemDefaults.FindBone(r.Instance.transform, name) : null;
            return b ? b.localEulerAngles : (Vector3?)null;
        }

        /// <summary>The FK capture rule: every found bone without a stored entry gets its base rotation, inside <paramref name="tx"/>.</summary>
        public static int CaptureMissingFk(Transaction tx, ItemRealization r, string arg)
        {
            var st = StateOf(r.Node);
            int k = 0;
            foreach (var name in FkBones(r))
            {
                if (st.Fk.Bones.ContainsKey(name)) continue;
                var e = FkBase(r, name);
                if (e == null) continue;
                CommandKit.Set(tx, r.NodeId, "item.fk.bones[" + FieldPath.Quote(name) + "]", UnityObjects.V(e.Value), arg);
                k++;
            }
            return k;
        }

        // ------------------------------------------------------------ item.anime

        static object Anime(CoreContext c)
        {
            var n = ItemNode(c);
            int? pattern = null;
            if (c.Args.Has("pattern"))
            {
                if (c.Args.Kind("pattern") != ArgKind.Integer) throw CommandKit.BadArgs("pattern", ArgTypes.Int);
                pattern = c.Args.Get<int>("pattern");
                if (pattern < 0) throw CommandKit.BadArgs("pattern", "an index ≥ 0");
            }
            float? speed = CharaCommands.Float(c, "speed", 0f);
            if (speed > MaxAnimeSpeed) throw CommandKit.BadArgs("speed", "0–" + MaxAnimeSpeed);
            float? time = CharaCommands.Float(c, "time");
            bool restart = c.Args.Get("restart", false);

            string id = n.Id;
            if (pattern != null || speed != null || time != null)
            {
                c.Runtime.ApplyNow();
                using (var tx = CommandKit.Tx(c, "Item animation"))
                {
                    if (pattern != null) CommandKit.Set(tx, id, "item.anime.pattern", pattern.Value, "pattern");
                    if (speed != null) CommandKit.Set(tx, id, "item.anime.speed", speed.Value, "speed");
                    if (time != null) CommandKit.Set(tx, id, "item.anime.time", time.Value, "time");
                    CommandKit.Commit(tx);
                }
            }
            c.Runtime.ApplyNow();
            var real = c.Runtime.FindItem(id);
            var m = real != null && real.State == RealizationState.Built && !real.InGraveyard ? ItemMotion.Of(real) : null;
            if (restart)
            {
                if (m?.Animation != null) m.Restart();
                else c.Warn("restart: the item has no driven Animator");
            }
            else if ((pattern != null || speed != null || time != null) && m?.Animation == null)
                c.Warn("the item has no driven Animator: the values are stored only");
            return AnimeReply(c, n, real, m);
        }

        static BridgeReply AnimeReply(CoreContext c, TreeNode n, ItemRealization real, ItemMotion m)
        {
            var cur = c.Session.Model.Find(n.Id) ?? n;
            var st = StateOf(cur);
            var a = m?.Animation;
            var rep = new BridgeReply
            {
                ["key"] = cur.Id,
                ["pattern"] = st.Anime.Pattern,
                ["speed"] = (double)st.Anime.Speed,
                ["storedTime"] = (double)st.Anime.Time,
                ["driven"] = a != null,
            };
            if (a != null && a.Valid && a.Driver.LayerCount > 0)
            {
                var info = a.StateInfo;
                rep["time"] = (double)info.normalizedTime;
                rep["state"] = a.StateName;
                rep["stateHash"] = info.shortNameHash;
            }
            else
            {
                rep["time"] = (double)st.Anime.Time;
                rep["state"] = null;
            }
            var pats = new List<object>();
            var comp = real != null && real.State == RealizationState.Built ? real.Component : null;
            if (comp?.animeInfos != null)
                foreach (var ai in comp.animeInfos)
                    pats.Add(new BridgeReply
                    {
                        ["name"] = ai?.name, ["state"] = ai?.state,
                        ["valid"] = ai != null && !string.IsNullOrEmpty(ai.name) && !string.IsNullOrEmpty(ai.state),
                    });
            rep["patterns"] = pats;
            return rep;
        }

        // ------------------------------------------------------------ item.motion

        static object Motion(CoreContext c)
        {
            var n = ItemNode(c);
            var real = BuiltItem(c, n);
            bool? paused = c.Args.TryGet<bool>("paused", out var pv) ? pv : (bool?)null;
            float? timeScale = CharaCommands.Float(c, "timeScale", 0f);
            if (timeScale > MaxTimeScale) throw CommandKit.BadArgs("timeScale", "0–" + MaxTimeScale);
            bool reset = c.Args.Get("resetPhysics", false);
            var m = ItemMotion.Of(real);
            var rep = new BridgeReply { ["key"] = n.Id, ["motion"] = m != null && m.Motion };
            if (m == null || !m.Motion)
            {
                if (paused != null || timeScale != null || reset) c.Warn("the item has no motion (no driven Animator, FK bones or dynamic bones): nothing changed");
                rep["paused"] = false;
                rep["timeScale"] = 1.0;
                rep["time"] = 0.0;
                rep["steps"] = 0;
                rep["errors"] = 0;
                rep["stages"] = new List<object>();
                return rep;
            }
            var cm = m.Motion;
            if (paused != null) cm.Paused = paused.Value;
            if (timeScale != null) cm.TimeScale = timeScale.Value;
            if (reset) m.Dynamic?.Reset();
            rep["paused"] = cm.Paused;
            rep["timeScale"] = (double)cm.TimeScale;
            rep["time"] = (double)cm.Time;
            rep["steps"] = cm.StepCount;
            rep["errors"] = cm.Errors;
            var stages = new List<object>();
            foreach (var s in cm.Stages) stages.Add(new BridgeReply { ["name"] = s.Name, ["phase"] = (int)s.Phase, ["enabled"] = s.Enabled });
            rep["stages"] = stages;
            if (m.Animation != null)
                rep["animation"] = new BridgeReply { ["state"] = m.Animation.StateName, ["time"] = (double)m.Animation.NormalizedTime, ["speed"] = (double)m.Animation.Speed };
            if (m.Fk != null) rep["fk"] = new BridgeReply { ["on"] = m.Fk.On, ["bones"] = m.Fk.Count };
            if (m.Dynamic != null)
                rep["dynamicBones"] = new BridgeReply
                {
                    ["active"] = m.DynamicActive, ["chains"] = m.Dynamic.Chains.Count, ["activeChains"] = m.Dynamic.ActiveChains, ["particles"] = m.Dynamic.ParticleCount,
                };
            return rep;
        }
    }
}
