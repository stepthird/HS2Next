// Ragdolls at run time: a scene part that owns the scene's physics world and its bodies. A scene without a body has no world: the frame's calls return at once.
//   who tells it   a change of `chara.ragdoll` (the body takes the block in the next Apply), a change of the scene's `ragdoll` block, a built character, a flush (AfterFlush), a node
//                  that left the tree or came back
//   the clock      Tick takes the scene clock's delta: a pause stops the world, a step and a recording run every substep, a capture only reads it, a seek moves nothing
//   the model      every lasting operation is an edit of a block, one history entry, and the body follows the block here; what a running clock does (a fall, a push, a getting up) changes
//                  no model value. Before an edit the settled pose is captured, so an undo brings back what was at rest
//   saved          the last pose at rest, written by the capture: never a moving state
// What operations call is at the end of this file.
using System;
using System.Collections.Generic;
using HS2Next.Chara.Edit;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class RagdollPart : ISceneRuntimePart, ISceneTickPart, IDisposable
    {
        const string BlockPath = RagdollEditCommand.ChangePath;
        /// <summary>The refusal for a character with no body.</summary>
        public const string Unavailable = "unavailable";

        SceneRuntime rt;
        RagdollWorld world;
        readonly HashSet<string> dirty = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> blendNext = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, bool> blockLimp = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly List<string> scratch = new List<string>();
        bool worldDirty, treeDirty;

        /// <summary>The world, null while there is none.</summary>
        public RagdollWorld World => world;

        /// <summary>The body of a character node, or null.</summary>
        public RagdollBody BodyOf(string nodeId) => world?.BodyOf(nodeId);

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            rt = r;
            r.CharaBuilt += OnCharaBuilt;
            r.Session.Events.NodeForgotten += OnForgotten;
        }

        public void OnChanged(SceneChangeBatch b)
        {
            if (b.Origin == ChangeOrigin.Capture) return;
            var l = b.Changes;
            for (int i = 0; i < l.Count; i++)
            {
                var c = l[i];
                switch (c.Kind)
                {
                    case SceneChangeKind.NodeChanged:
                        if (c.Path != null && FieldPath.Overlaps(c.Path, BlockPath)) dirty.Add(c.NodeId);
                        break;
                    case SceneChangeKind.SceneChanged:
                        if (c.Path == RagdollWorldEditCommand.ChangePath) worldDirty = true;
                        break;
                    case SceneChangeKind.NodeAdded:
                    case SceneChangeKind.NodeRemoved:
                        treeDirty = world != null;
                        break;
                }
            }
        }

        public void Apply()
        {
            if (!worldDirty && !treeDirty && dirty.Count == 0) return;
            if (worldDirty)
            {
                worldDirty = false;
                SyncWorld();
            }
            if (treeDirty)
            {
                treeDirty = false;
                SyncTree();
            }
            if (dirty.Count > 0)
            {
                scratch.Clear();
                scratch.AddRange(dirty);
                dirty.Clear();
                foreach (var id in scratch) Sync(id);
            }
        }

        public void LateApply() { }

        public void Tick(float dt)
        {
            var w = world;
            if (w == null) return;
            w.Tick(dt, rt.Stepping || Time.captureDeltaTime > 0f);
            var l = w.Bodies;
            for (int i = l.Count - 1; i >= 0; i--)
                if (l[i].BlendDone) End(l[i]);
            CloseIfEmpty();
        }

        public void Clear()
        {
            dirty.Clear();
            blendNext.Clear();
            blockLimp.Clear();
            worldDirty = treeDirty = false;
            CloseWorld();
        }

        public void OnLoadedRealized() => worldDirty = true;

        public void Dispose()
        {
            Clear();
            if (rt == null) return;
            rt.CharaBuilt -= OnCharaBuilt;
            rt.Session.Events.NodeForgotten -= OnForgotten;
        }

        // ------------------------------------------------------------ the world

        /// <summary>The world, made when there is none: what a body, a prop or a pusher needs first. Whatever made it for nothing calls <see cref="CloseIfEmpty"/>.</summary>
        public RagdollWorld EnsureWorld()
        {
            if (world != null) return world;
            world = new RagdollWorld(rt);
            world.Values = (rt.Model.Ragdoll?.Values ?? new RagdollWorldValues()).Applied();
            return world;
        }

        void CloseWorld()
        {
            var w = world;
            if (w == null) return;
            world = null;
            var l = w.Bodies;
            for (int i = l.Count - 1; i >= 0; i--) l[i].Destroy();
            w.Close();
        }

        /// <summary>The world goes when nothing keeps it.</summary>
        public void CloseIfEmpty()
        {
            if (world != null && world.Empty) CloseWorld();
        }

        void SyncWorld()
        {
            var block = rt.Model.Ragdoll;
            bool hasPushers = block != null && block.Pushers.Count > 0;
            if (world == null && !hasPushers) return;
            var w = EnsureWorld();
            w.Values = (block?.Values ?? new RagdollWorldValues()).Applied();
            var l = w.Bodies;
            for (int i = 0; i < l.Count; i++)
            {
                l[i].ForceDrives();
                l[i].Respring();
            }
            if (w.HasRoom && l.Count > 0) PlaceRoom(l[0], true);
            SyncPushers(block);
            w.Ground?.BlockChanged(block);
            CloseIfEmpty();
        }

        void SyncPushers(SceneRagdollWorld block)
        {
            var w = world;
            if (w == null) return;
            var live = w.Pushers;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var doc = block?.Find(live[i].Doc.Id);
                if (doc != null && doc.Key == live[i].Doc.Key && doc.A == live[i].Doc.A && doc.B == live[i].Doc.B && doc.AppliedRadius == live[i].Doc.AppliedRadius) continue;
                w.Remove(live[i]);
            }
            if (block == null) return;
            foreach (var d in block.AppliedPushers())
            {
                bool have = false;
                for (int i = 0; i < live.Count; i++) have |= live[i].Doc.Id == d.Id;
                if (have) continue;
                var p = new RagdollLivePusher(w, new RagdollPusher { Id = d.Id, Key = d.Key, A = d.A, B = d.B, Radius = d.Radius });
                if (p.Alive) w.Add(p);
                else p.Destroy();
            }
        }

        /// <summary>The room's floor for <paramref name="body"/> by the block's mode, with its offset.</summary>
        float FloorFor(RagdollBody body, out string source)
        {
            var f = rt.Model.Ragdoll?.Floor;
            string mode = f?.AppliedMode ?? RagdollFloorValues.ModeMap;
            float y;
            if (mode == RagdollFloorValues.ModeFixed)
            {
                y = f.Height;
                source = RagdollFloorSources.Fixed;
            }
            else if (mode != RagdollFloorValues.ModeMap || world.Ground == null || !world.Ground.FloorFor(body, out y, out source))
            {
                y = body.Lowest() - 0.1f;
                source = RagdollFloorSources.Feet;
            }
            return y + (f?.Offset ?? 0f);
        }

        /// <summary>The room's floor read again by the block's mode, from the first body (a map that changed).</summary>
        public void RefloorNow()
        {
            if (world != null && world.HasRoom && world.Bodies.Count > 0) PlaceRoom(world.Bodies[0], true);
        }

        // the room at the first body, a floor that must go lower for a later one, or what the block says now
        void PlaceRoom(RagdollBody body, bool fromBlock)
        {
            var w = world;
            var room = rt.Model.Ragdoll?.Room;
            var size = UnityObjects.V(room?.AppliedSize ?? RagdollRoomValues.DefaultSize);
            Vector2 center;
            if (room?.Center is Vec2 c) center = new Vector2(c.X, c.Y);
            else if (w.HasRoom) center = w.RoomCenter;
            else
            {
                var hips = body.Parts[0].Tr.position;
                center = new Vector2(hips.x, hips.z);
            }
            float y = FloorFor(body, out string source);
            if (!w.HasRoom || (fromBlock ? y != w.FloorY : y < w.FloorY) || center != w.RoomCenter || size != w.RoomSize || (fromBlock && source != w.FloorSource))
                w.SetRoom(center, size, y, source);
        }

        // ------------------------------------------------------------ bodies

        static CharaRagdoll BlockOf(TreeNode n) => (n?.State as CharaNodeState)?.Ragdoll;

        void Sync(string id)
        {
            var block = BlockOf(rt.Model.Find(id));
            var body = world?.BodyOf(id);
            if (block == null || !block.On)
            {
                blockLimp.Remove(id);
                if (body == null) return;
                bool blend = blendNext.Remove(id) && world.Values.BlendOut > 0f && !rt.Paused && !body.Buried;
                if (blend) body.StartBlend(world.Values.BlendOut);
                else End(body);
                return;
            }
            if (body != null && body.Blending)
            {
                End(body);
                body = null;
            }
            var r = rt.FindChara(id);
            if (r == null || r.State != RealizationState.Built) return;   // the build's event brings it
            if (body == null) { Start(r, block); return; }
            body.Values = block.Values.Applied();
            body.ForceDrives();
            body.ApplyLegCollision();
            body.SyncLinks(block);
            SyncLimp(body, block);
        }

        void SyncLimp(RagdollBody body, CharaRagdoll block)
        {
            blockLimp.TryGetValue(body.NodeId, out bool was);
            blockLimp[body.NodeId] = block.Limp;
            if (block.Limp == was) return;
            if (block.Limp) body.GoLimp();
            else if (body.State == RagdollState.Limp) body.SetState(RagdollState.Up);
        }

        RagdollBody Start(CharaRealization r, CharaRagdoll block)
        {
            string id = r.NodeId;
            string missing = RagdollBody.MissingBone(r.Context);
            if (missing != null)
            {
                rt.ReportOnce(FindingSeverity.Warning, RagdollFindings.NoBones, id, "this character has no bone " + missing + ": no ragdoll; the block is kept");
                CloseIfEmpty();
                return null;
            }
            var w = EnsureWorld();
            var body = new RagdollBody(w, r, block.Values.Applied());
            var pose = PoseOf(r, block);
            body.Build(pose);
            w.Add(body);
            if (!body.RestFromPrefab) rt.ReportOnce(FindingSeverity.Warning, RagdollFindings.NoRest, id, "the skeleton's rest pose could not be read: the ragdoll is sized from the pose of the moment");
            if (body.Protected.Count > 0) rt.ReportOnce(FindingSeverity.Warning, RagdollFindings.Protected, id, "a part drives nothing: " + string.Join(", ", body.Protected));
            body.AddStages();
            body.SyncLinks(block);
            SyncLimp(body, block);
            PlaceRoom(body, false);
            w.Ground?.BodyBuilt(body);
            if (pose != null)
            {
                body.SettledFrom(block.Pose);
                body.PrimeDrives();
                body.SleepAll();
                body.ChainsAfterNextWrite();
            }
            return body;
        }

        /// <summary>The block's pose in world space, or null when it has none.</summary>
        static RagdollWorldPose PoseOf(CharaRealization r, CharaRagdoll block)
        {
            if (block.Pose.IsEmpty) return null;
            var h = r.Holder;
            var pose = new RagdollWorldPose { Hips = h.TransformPoint(UnityObjects.V(block.Pose.Hips)) };
            var rot = h.rotation;
            foreach (var kv in block.Pose.AppliedParts()) pose.Parts[RagdollParts.IndexOf(kv.Key)] = rot * UnityObjects.Q(kv.Value);
            return pose;
        }

        void End(RagdollBody b)
        {
            world.Ground?.BodyGone(b);
            world.Remove(b);
            b.Destroy();
            blockLimp.Remove(b.NodeId);
            CloseIfEmpty();
        }

        void OnCharaBuilt(CharaRealization c)
        {
            var block = BlockOf(c.Node);
            if (block == null || !block.On) return;
            if (world?.BodyOf(c.NodeId) == null) dirty.Add(c.NodeId);
        }

        void OnForgotten(string id)
        {
            var b = world?.BodyOf(id);
            if (b != null && rt.Model.Find(id) == null) End(b);
        }

        void SyncTree()
        {
            if (world == null) return;
            var l = world.Bodies;
            for (int i = 0; i < l.Count; i++)
            {
                var b = l[i];
                var r = b.Realization;
                if (r == null) continue;
                if (r.InGraveyard) b.Bury();
                else if (b.Buried && r.State == RealizationState.Built) b.Revive();
            }
        }

        /// <summary>A flush of the character ran: one line in its realization. A body whose sizes went stale is built anew at its pose.</summary>
        internal void AfterFlush(CharaRealization c, FlushReport r)
        {
            var b = world?.BodyOf(c.NodeId);
            if (b != null && !b.Buried && b.SizesStale()) b.Rebuild(c, b.CurrentPose());
        }

        /// <summary>The capture: the pose at rest and a limp that came from a shot into the block, in the silent batch before a save, a duplicate and a delete.</summary>
        internal void Collect(CharaRealization c, List<(string id, string path, object value, Action after)> writes)
        {
            var b = world?.BodyOf(c.NodeId);
            var block = BlockOf(c.Node);
            if (b == null || block == null || !block.On) return;
            if (b.State == RagdollState.Limp && !block.Limp)
                writes.Add((c.NodeId, BlockPath + ".limp", true, () => blockLimp[c.NodeId] = true));
            if (b.SettledChanged) writes.Add((c.NodeId, BlockPath + ".pose", b.SettledPose(), b.SettledWritten));
        }

        // ------------------------------------------------------------ what ops call: edits of a block, one history entry each

        CommandResult Edit(string id, string label, ExecuteOptions o, Func<CharaRagdoll, string> change)
        {
            var st = rt.Model.Find(id)?.State as CharaNodeState;
            if (st == null) return CommandResult.Error(CommandCodes.NotFound, "no character " + id);
            CaptureNow(id);
            var block = st.Ragdoll.Clone();
            string why = change(block);
            if (why != null) return CommandResult.Error(CommandCodes.BadArgs, why);
            var res = rt.Session.Commands.Execute(new RagdollEditCommand(id, block, label), o);
            if (res.Ok) rt.ApplyNow();
            return res;
        }

        /// <summary>The settled pose of a node into the document, silently: what an edit that replaces the block starts from.</summary>
        public void CaptureNow(string nodeId)
        {
            var b = BodyOf(nodeId);
            if (b == null) return;
            b.CaptureSettled();
            rt.Capture.CaptureSubtrees(new[] { nodeId });
        }

        /// <summary>A body on a character: the block's <c>on</c>. The body exists when this returns, unless the character cannot have one.</summary>
        public CommandResult Build(string id, ExecuteOptions o = default) =>
            Edit(id, RagdollEditCommand.LabelRagdoll, o, b => { b.On = true; return null; });

        /// <summary>The body goes: pins, ties, holds and pose with it, the values stay. With <paramref name="blend"/>, a running clock and a blend time the bones go to the pose over that time; else at once.</summary>
        public CommandResult Release(string id, bool blend = false, ExecuteOptions o = default)
        {
            if (blend) blendNext.Add(id);
            var res = Edit(id, RagdollEditCommand.LabelRelease, o, b => { b.Release(); return null; });
            blendNext.Remove(id);
            return res;
        }

        /// <summary>The world goes, with every prop and effect left in it. The bodies and pushers go by their blocks first: this is what is left of a release without a key.</summary>
        public void CloseNow() => CloseWorld();

        /// <summary>
        /// A pin of <paramref name="part"/>, at its centre of mass or at <paramref name="point"/> (world). With <paramref name="follow"/> it follows that node, or that node's
        /// <paramref name="bone"/>; with <paramref name="snap"/> it goes onto it, else it keeps the distance it has.
        /// </summary>
        public CommandResult PinPart(string id, string part, Vector3? point = null, string follow = null, string bone = null, bool snap = false, ExecuteOptions o = default)
        {
            var b = BodyOf(id);
            if (b == null) return CommandResult.Error(Unavailable, "this character has no ragdoll");
            var p = b.Part(part);
            if (p == null) return CommandResult.Error(CommandCodes.BadArgs, "no part " + part + "; the parts are " + RagdollParts.List());
            var at = point ?? p.Rb.worldCenterOfMass;
            var row = new RagdollPin { Part = part, Point = UnityObjects.V(p.Tr.InverseTransformPoint(at)) };
            if (string.IsNullOrEmpty(follow)) row.At = UnityObjects.V(at);
            else
            {
                var t = FollowTransform(follow, bone);
                if (!t) return CommandResult.Error(CommandCodes.NotFound, "nothing to follow: " + follow + (string.IsNullOrEmpty(bone) ? "" : " " + bone));
                row.Follow = follow;
                row.Bone = bone ?? "";
                row.Offset = UnityObjects.V(snap ? Vector3.zero : t.InverseTransformPoint(at));
            }
            return Edit(id, RagdollEditCommand.LabelPin, o, block =>
            {
                row.Id = block.NextPinId();
                block.Pins.Add(row);
                return null;
            });
        }

        Transform FollowTransform(string node, string bone)
        {
            if (string.IsNullOrEmpty(bone)) return rt.HolderOf(node);
            var r = rt.FindChara(node);
            return r != null && r.State == RealizationState.Built ? AdvancedPoseResolver.Resolve(r, bone) : null;
        }

        /// <summary>Drops a pin, or every pin of the character with <paramref name="pin"/> null. Returns through <paramref name="dropped"/> how many.</summary>
        public CommandResult Unpin(string id, string pin, out int dropped, ExecuteOptions o = default)
        {
            int n = 0;
            var res = Edit(id, RagdollEditCommand.LabelUnpin, o, block =>
            {
                n = block.Pins.RemoveAll(p => pin == null || p.Id == pin);
                return null;
            });
            dropped = res.Ok ? n : 0;
            return res;
        }

        /// <summary>A following pin becomes a plain one where it is.</summary>
        public CommandResult Stay(string id, string pin, ExecuteOptions o = default)
        {
            var at = BodyOf(id)?.PinOf(pin)?.Grab?.Goal;
            return Edit(id, RagdollEditCommand.LabelPin, o, block =>
            {
                var p = block.Find(pin);
                if (p == null) return "no pin " + pin;
                p.Follow = "";
                p.Bone = "";
                p.Offset = null;
                if (at.HasValue) p.At = UnityObjects.V(at.Value);
                return null;
            });
        }

        /// <summary>Two parts tied where they are now.</summary>
        public CommandResult Tie(string id, string a, string b, ExecuteOptions o = default) =>
            Edit(id, RagdollEditCommand.LabelTie, o, block =>
            {
                if (!RagdollParts.IsKnown(a)) return "no part " + a + "; the parts are " + RagdollParts.List();
                if (!RagdollParts.IsKnown(b)) return "no part " + b + "; the parts are " + RagdollParts.List();
                if (a == b) return "a part is not tied to itself";
                block.Ties.RemoveAll(t => (t.A == a && t.B == b) || (t.A == b && t.B == a));
                block.Ties.Add(new RagdollTie { A = a, B = b });
                return null;
            });

        /// <summary>A tie goes.</summary>
        public CommandResult Untie(string id, string a, string b, ExecuteOptions o = default) =>
            Edit(id, RagdollEditCommand.LabelTie, o, block =>
            {
                block.Ties.RemoveAll(t => (t.A == a && t.B == b) || (t.A == b && t.B == a));
                return null;
            });

        /// <summary>
        /// Each part whose key starts with one of <paramref name="prefixes"/> (comma separated) holds its rotation relative to its parent as it is now. <paramref name="held"/> is how many.
        /// </summary>
        public CommandResult Hold(string id, string prefixes, out int held, ExecuteOptions o = default)
        {
            int n = 0;
            var body = BodyOf(id);
            if (body == null) { held = 0; return CommandResult.Error(Unavailable, "this character has no ragdoll"); }
            var names = prefixes.Split(',');
            var res = Edit(id, RagdollEditCommand.LabelHold, o, block =>
            {
                foreach (var p in body.Parts)
                {
                    if (p.Parent == null) continue;
                    bool match = false;
                    foreach (var x in names) match |= x.Length > 0 && p.Key.StartsWith(x.Trim(), StringComparison.Ordinal);
                    if (!match) continue;
                    block.Holds.RemoveAll(h => h.Part == p.Key);
                    block.Holds.Add(new RagdollHold { Part = p.Key, Rot = UnityObjects.Q(p.LocalRotation) });
                    n++;
                }
                return null;
            });
            held = res.Ok ? n : 0;
            return res;
        }

        /// <summary>A hold goes.</summary>
        public CommandResult Unhold(string id, string part, ExecuteOptions o = default) =>
            Edit(id, RagdollEditCommand.LabelHold, o, block =>
            {
                block.Holds.RemoveAll(h => h.Part == part);
                return null;
            });

        /// <summary>A pusher along two bones of a character; <paramref name="count"/> is how many there are then.</summary>
        public CommandResult AddPusher(string key, string a, string b, float radius, out int count, ExecuteOptions o = default)
        {
            var block = rt.Model.Ragdoll?.Clone() ?? new SceneRagdollWorld();
            block.Pushers.Add(new RagdollPusher { Id = block.NextPusherId(), Key = key, A = a, B = b, Radius = radius });
            count = block.Pushers.Count;
            var res = rt.Session.Commands.Execute(new RagdollWorldEditCommand(block, RagdollWorldEditCommand.LabelPusher), o);
            if (res.Ok) rt.ApplyNow();
            return res;
        }
    }
}
