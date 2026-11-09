// StudioOps, ragdolls: what the commands, the panel and the viewport tool do to a body, so all of them work with the bridge off. What lasts is an edit of a block, one history entry each; what
// a running clock does and what acts once (a push, a turn, a settle, a knock, a shot, a throw) changes no model value and has none. A request is checked first; a refusal stores nothing and
// answers:
//   not_found    a node, a pin, a bone, nothing to follow, no character when none was named, a part by the follower's rule
//   wrong_kind   a node that is no character, or no item for an obstacle
//   bad_args     a part that is no part (the message lists them), a value that is not one, an unknown preset, a limit (the word ragdoll.limit in the message)
//   unavailable  a character that is not built; a character with no body, for what needs one
//   busy         as every edit
// Before an edit the pose at rest is captured, so an undo brings back what was at rest. An undo of a release makes a new body at that pose; an undo of a build releases at once.
// Options: a MergeKey chains the edits of one value into one history entry; Record None applies without an entry and keeps the history; an open transaction (a UI drag, a bridge batch) takes
// the edit as a step.
// Result: Value = a RagdollBuildResult (build), the number held, dropped, thrown, of pushers, of obstacles or of bones (hold, unpin, throw, pusher, obstacle, bake), a RagdollFollowerResult
// (follower), the state's name (knock), true when the character has a body (turn, settle), the ShotResult (shoot). Changed = the names of a set that changed something.
using System;
using System.Collections.Generic;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Scenes.Ragdoll;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What a build did: a new body, and the height of the map's floor when it has one.</summary>
    public sealed class RagdollBuildResult
    {
        public bool Built;
        public float? Floor;
    }

    /// <summary>The folder and the constraint of a follower.</summary>
    public sealed class RagdollFollowerResult
    {
        public string Folder, Constraint;
    }

    /// <summary>A pin as the status says it.</summary>
    public sealed class RagdollPinRow
    {
        public string Pin, Part;
        /// <summary>The node it follows; null for a plain pin.</summary>
        public string Follow;
        /// <summary>1 when what it follows is moved by a constraint of the same frame.</summary>
        public int Lag;
        /// <summary>ok, missing, or off with no body.</summary>
        public string State;
    }

    /// <summary>How one character stands.</summary>
    public sealed class RagdollCharaRow
    {
        public string Key;
        /// <summary>up, down, gettingUp or limp; null with no body.</summary>
        public string State;
        /// <summary>A part is awake.</summary>
        public bool Awake;
        public int Parts, Ties, Holds;
        public readonly List<RagdollPinRow> Pins = new List<RagdollPinRow>();
        /// <summary>The parts whose bone a driven bone writes over the body.</summary>
        public readonly List<string> Overridden = new List<string>();
        /// <summary>The parts that drive nothing.</summary>
        public readonly List<string> Protected = new List<string>();
        /// <summary>A pose at rest is kept.</summary>
        public bool Settled;
        /// <summary>What hits took from the weakest part, 0..1.</summary>
        public float Weakest;
    }

    /// <summary>How the world stands.</summary>
    public sealed class RagdollWorldRow
    {
        public int Bodies, Props, Pushers;
        public float Hz;
        public long Steps;
        /// <summary>The room's floor; null with no room.</summary>
        public float? Floor;
        /// <summary>map, feet or fixed; null with no room.</summary>
        public string FloorSource;
        public bool HasRoom;
        public Vector2 RoomCenter;
        public Vector3 RoomSize;
        /// <summary>What the last hit of a prop gave a body in speed; null before the first.</summary>
        public float? LastHit;
    }

    public sealed partial class StudioOps
    {
        const string RagdollNoBody = "this character has no ragdoll";
        string ragdollLastBuilt;

        /// <summary>The part that runs the bodies; null on a runtime without the standard parts.</summary>
        public RagdollPart Ragdoll => rt.GetPart<RagdollPart>();

        // ------------------------------------------------------------ reads

        /// <summary>The character a command without a key means: the last one built, else the selection's first character; null when none.</summary>
        public string RagdollDefaultCharacter()
        {
            if (ragdollLastBuilt != null && Model.Find(ragdollLastBuilt)?.Kind == NodeKind.Chara) return ragdollLastBuilt;
            foreach (string id in Session.Selection.Ids)
                if (Model.Find(id)?.Kind == NodeKind.Chara) return id;
            return null;
        }

        /// <summary>The block of a character as it stands in the document, not to be changed; null for anything else. An edit replaces the object.</summary>
        public CharaRagdoll RagdollBlock(string nodeId) => (Model.Find(nodeId)?.State as CharaNodeState)?.Ragdoll;

        /// <summary>The scene's block as it stands in the document, not to be changed; null when the scene has none.</summary>
        public SceneRagdollWorld RagdollWorldBlock => Model.Ragdoll;

        /// <summary>The body of a character, or null.</summary>
        public RagdollBody RagdollBodyOf(string nodeId) => Ragdoll?.BodyOf(nodeId);

        /// <summary>The name of a body's state.</summary>
        public static string RagdollStateName(RagdollState s)
        {
            switch (s)
            {
                case RagdollState.Up: return "up";
                case RagdollState.Down: return "down";
                case RagdollState.GettingUp: return "gettingUp";
                default: return "limp";
            }
        }

        /// <summary>How the world stands; a scene with no world answers its block's rate and zeros.</summary>
        public RagdollWorldRow RagdollWorldStatus()
        {
            var w = Ragdoll?.World;
            if (w == null) return new RagdollWorldRow { Hz = (Model.Ragdoll?.Values ?? new RagdollWorldValues()).Applied().Hz };
            var row = new RagdollWorldRow
            {
                Bodies = w.Bodies.Count, Props = w.Props?.Count ?? 0, Pushers = w.Pushers.Count, Hz = w.Values.Hz, Steps = w.Steps,
                HasRoom = w.HasRoom, RoomCenter = w.RoomCenter, RoomSize = w.RoomSize,
            };
            if (w.HasRoom)
            {
                row.Floor = w.FloorY;
                row.FloorSource = w.FloorSource;
            }
            if (w.Props != null && w.Props.Hits > 0) row.LastHit = w.Props.LastHit;
            return row;
        }

        /// <summary>How a character stands now; null for a node that is no character. With no body it answers from its block.</summary>
        public RagdollCharaRow RagdollStatus(string nodeId)
        {
            var block = RagdollBlock(nodeId);
            if (block == null) return null;
            var row = new RagdollCharaRow { Key = nodeId };
            var b = RagdollBodyOf(nodeId);
            if (b == null)
            {
                row.Ties = block.Ties.Count;
                row.Holds = block.Holds.Count;
                foreach (var p in block.Pins)
                    row.Pins.Add(new RagdollPinRow { Pin = p.Id, Part = p.Part, Follow = p.IsFollowing ? p.Follow : null, State = "off" });
                return row;
            }
            row.State = RagdollStateName(b.State);
            row.Parts = b.Parts.Count;
            row.Ties = b.TieCount;
            row.Settled = b.SettledParts != null;
            row.Protected.AddRange(b.Protected);
            var driven = DrivenPoseDriver.Of(b.Realization)?.Set;
            foreach (var p in b.Parts)
            {
                if (!p.Rb.IsSleeping()) row.Awake = true;
                if (p.Held) row.Holds++;
                row.Weakest = Mathf.Max(row.Weakest, p.Weakness);
                var e = driven?.Of(p.Bone);
                if (e != null && e.On != 0) row.Overridden.Add(p.Key);
            }
            foreach (var pin in b.Pins)
                row.Pins.Add(new RagdollPinRow { Pin = pin.Id, Part = pin.Doc.Part, Follow = pin.Doc.IsFollowing ? pin.Doc.Follow : null, Lag = RagdollLag(pin.Doc), State = pin.State });
            return row;
        }

        // 1 when a constraint of the same frame moves what the pin follows: the pin reads it a frame late
        int RagdollLag(RagdollPin pin)
        {
            var list = Model.Constraints;
            var part = Constraints;
            if (!pin.IsFollowing || list == null || part == null) return 0;
            foreach (var e in list.Entries)
            {
                if (e == null || !e.On || e.Target?.Node != pin.Follow) continue;
                if (part.RowOf(e.Id)?.State == ConstraintStates.Active) return 1;
            }
            return 0;
        }

        // ------------------------------------------------------------ what every operation starts with

        RagdollPart RagdollChara(string nodeId, out CharaNodeState state, out OpResult fail)
        {
            state = null;
            var n = Find(nodeId, "key", out fail);
            if (n == null) return null;
            state = n.State as CharaNodeState;
            if (state == null)
            {
                fail = OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", not a character", "key", "chara");
                return null;
            }
            var part = Ragdoll;
            if (part == null) fail = OpResult.Unsupported("this runtime has no ragdolls");
            return part;
        }

        // the body of a character; a body on its way out by a release's blend is none
        RagdollBody RagdollLive(string nodeId, out OpResult fail)
        {
            var part = RagdollChara(nodeId, out _, out fail);
            if (part == null) return null;
            rt.ApplyNow();
            var b = part.BodyOf(nodeId);
            if (b != null && !b.Blending) return b;
            fail = OpResult.Unavailable(RagdollNoBody, "key", "NoBody");
            return null;
        }

        static OpResult RagdollBadPart(string part, string arg = "part") =>
            OpResult.BadArgs(arg, "a part", "no part \"" + part + "\"; the parts are " + RagdollParts.List());

        // the limits count over the whole scene; asked before an edit's capture, so that a refusal leaves the document as it was
        int RagdollCount(Func<CharaRagdoll, int> of)
        {
            int n = 0;
            foreach (var t in Model.PreOrder())
                if (t.State is CharaNodeState c) n += of(c.Ragdoll);
            return n;
        }

        static OpResult RagdollOverLimit(string arg, int limit, string what) => OpResult.BadArgs(arg, null, RagdollFindings.Limit + ": more than " + limit + " " + what + " in the scene");

        // one history entry for what takes several commands; a step that fails takes the steps before it back
        OpResult RagdollEntry(string label, ExecuteOptions o, Func<ExecuteOptions, OpResult> steps)
        {
            o = Labelled(o, label);
            var tx = Open(o.Label, o, out var fail);
            if (tx == null) return fail;
            OpResult r;
            using (tx)
            {
                r = steps(new ExecuteOptions { Record = HistoryMode.Record, Source = o.Source, Label = o.Label });
                if (r.Ok) r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        // the two blocks in one entry, or the one that changed by itself, so that a dragged value merges
        OpResult RagdollCommit(string nodeId, CharaRagdoll block, SceneRagdollWorld world, string label, ExecuteOptions o)
        {
            OpResult r;
            if (block != null && world != null)
                return RagdollEntry(label, o, step =>
                {
                    var c = Session.Commands.Execute(new RagdollWorldEditCommand(world, label), step);
                    if (c.Ok) c = Session.Commands.Execute(new RagdollEditCommand(nodeId, block, label), step);
                    return OpResult.From(c);
                });
            o = Labelled(o, label);
            if (block != null) r = OpResult.From(Session.Commands.Execute(new RagdollEditCommand(nodeId, block, label), o));
            else if (world != null) r = OpResult.From(Session.Commands.Execute(new RagdollWorldEditCommand(world, label), o));
            else return OpResult.NothingToDo();
            if (r.Ok) rt.ApplyNow();
            return r;
        }

        // ------------------------------------------------------------ a body and its end

        /// <summary>
        /// A body on a character (label «Ragdoll»); Value = a <see cref="RagdollBuildResult"/>, with Built false when it has one already. <paramref name="room"/> [x0, x1, z0, z1] goes to the
        /// scene's block; <paramref name="mapFloor"/> false sets the floor to the feet. The character is then what a command without a key means.
        /// </summary>
        public OpResult RagdollBuild(string nodeId, bool mapFloor = true, float[] room = null, ExecuteOptions o = default)
        {
            var part = RagdollChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            fail = WetBuilt(nodeId);
            if (fail != null) return fail;
            var world = Model.Ragdoll?.Clone() ?? new SceneRagdollWorld();
            bool edit = false;
            if (room != null)
            {
                string why = room.Length == 4 ? RagdollEdits.SetStrip(world.Room, room[0], room[1], room[2], room[3]) : "a room is [x0, x1, z0, z1]";
                if (why != null) return OpResult.BadArgs("room", "[x0, x1, z0, z1]", why);
                edit = true;
            }
            if (!mapFloor && world.Floor.Mode != RagdollFloorValues.ModeFeet)
            {
                world.Floor.Mode = RagdollFloorValues.ModeFeet;
                edit = true;
            }
            var had = part.BodyOf(nodeId);
            bool make = had == null || had.Blending;
            OpResult r;
            if (edit)
                r = RagdollEntry(Labels.RagdollBuild, o, step =>
                {
                    var c = Session.Commands.Execute(new RagdollWorldEditCommand(world, Labels.RagdollBuild), step);
                    if (c.Ok && make) c = part.Build(nodeId, step);
                    return OpResult.From(c);
                });
            else r = make ? OpResult.From(part.Build(nodeId, Labelled(o, Labels.RagdollBuild))) : OpResult.NothingToDo();
            if (!r.Ok) return r;
            ragdollLastBuilt = nodeId;
            var w = part.World;
            r.Value = new RagdollBuildResult
            {
                Built = make && part.BodyOf(nodeId) != null,
                Floor = mapFloor && w != null && w.HasRoom && w.FloorSource == RagdollFloorSources.Map ? w.FloorY : (float?)null,
            };
            return r;
        }

        /// <summary>The body of a character goes (label «Release ragdoll»), its pins, ties, holds and pose with it, its values stay. With <paramref name="blend"/>, a blend time and a running clock the bones go to the pose over that time; else at once.</summary>
        public OpResult RagdollRelease(string nodeId, bool blend = false, ExecuteOptions o = default)
        {
            var part = RagdollChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            return OpResult.From(part.Release(nodeId, blend, Labelled(o, Labels.RagdollRelease)));
        }

        /// <summary>
        /// Every body, prop, pusher and effect goes and the world with them, so the next body starts in a fresh one (label «Release ragdoll», one entry; with nothing to edit none). The room's
        /// values stay in the scene's block.
        /// </summary>
        public OpResult RagdollReleaseAll(ExecuteOptions o = default)
        {
            var part = Ragdoll;
            if (part == null) return OpResult.Unsupported("this runtime has no ragdolls");
            rt.ApplyNow();
            var ids = new List<string>();
            foreach (var n in Model.PreOrder())
                if (n.State is CharaNodeState c && RagdollHoldsAny(c.Ragdoll)) ids.Add(n.Id);
            var world = Model.Ragdoll;
            bool pushers = world != null && world.Pushers.Count > 0;
            var r = OpResult.NothingToDo();
            if (ids.Count > 0 || pushers)
                r = RagdollEntry(Labels.RagdollRelease, o, step =>
                {
                    foreach (string id in ids)
                    {
                        var c = part.Release(id, false, step);
                        if (!c.Ok) return OpResult.From(c);
                    }
                    if (!pushers) return OpResult.Success();
                    var after = world.Clone();
                    after.Pushers.Clear();
                    return OpResult.From(Session.Commands.Execute(new RagdollWorldEditCommand(after, Labels.RagdollRelease), step));
                });
            if (!r.Ok) return r;
            part.CloseNow();
            RagdollShots.Of(rt)?.Clear();
            return r;
        }

        // what a release has to take away; values stay
        static bool RagdollHoldsAny(CharaRagdoll b) => b.On || b.Limp || b.Pins.Count > 0 || b.Ties.Count > 0 || b.Holds.Count > 0 || !b.Pose.IsEmpty;

        /// <summary>
        /// The body's pose goes to FK and the body goes, in one entry (label «Bake pose to FK»): the bones as the body writes them, read with nothing on top, into the groups Body and Neck, the
        /// hips' place into the hips position, the hips offset to 0; FK and those groups on, IK off, the animation stopped. The hands keep what they have. Value = the bones written. What the corrections lay on top
        /// stays on top, so the view does not move.
        /// </summary>
        public OpResult RagdollBake(string nodeId, ExecuteOptions o = default)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            var part = Ragdoll;
            var r = body.Realization;
            var st = (CharaNodeState)Model.Find(nodeId).State;
            var table = FkDriver.TableFor(r);
            if (table == null || r.Context?.BodyBones == null || !r.Context.BodyBones.TryGetValue("cf_J_Hips", out var hips) || !hips)
                return OpResult.Unavailable("character " + nodeId + " has no FK bones", "key", "NoFk");
            var bones = new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal);
            int count = 0;
            var at = Vector3.zero;
            body.ReadBodyPose(() =>
            {
                foreach (var g in new[] { FkGroup.Body, FkGroup.Neck })
                    foreach (var b in table.Of(g))
                    {
                        if (!b.Bone) continue;
                        var e = b.Bone.localEulerAngles;
                        bones[b.Key] = new Vec3(e.x, e.y, e.z);
                        count++;
                    }
                at = hips.localPosition;
            });
            var result = RagdollEntry(Labels.RagdollBake, o, step =>
            {
                var sets = new (string path, object value)[]
                {
                    ("chara.ik.enabled", false),
                    ("chara.fk.bones", bones),
                    ("chara.fk.hipsPosition", new Vec3(at.x, at.y, at.z)),
                    ("chara.fk.hipsOffset", 0f),
                    ("chara.fk.groups.body", true),
                    ("chara.fk.groups.neck", true),
                    ("chara.fk.enabled", true),
                    ("chara.anime.speed", 0f),
                };
                foreach (var (path, value) in sets)
                {
                    var c = Session.Set(nodeId, path, value, step);
                    if (!c.Ok) return OpResult.From(c);
                }
                return OpResult.From(part.Release(nodeId, false, step));
            });
            if (result.Ok) result.Value = count;
            return result;
        }

        // ------------------------------------------------------------ pins, ties, holds

        /// <summary>
        /// A pin of a part (label «Pin»), at <paramref name="point"/> (world) or at its centre of mass. With <paramref name="follow"/> it follows that node, or its <paramref name="bone"/>; with
        /// <paramref name="snap"/> it goes onto it, else it keeps the distance it has.
        /// </summary>
        public OpResult RagdollPin(string nodeId, string part, Vector3? point = null, string follow = null, string bone = null, bool snap = false, ExecuteOptions o = default)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            if (!RagdollParts.IsKnown(part)) return RagdollBadPart(part);
            if (RagdollCount(c => c.Pins.Count) >= RagdollLimits.MaxPins) return RagdollOverLimit("part", RagdollLimits.MaxPins, "pins");
            var r = Ragdoll.PinPart(nodeId, part, point, follow, bone, snap, Labelled(o, Labels.RagdollPin));
            return OpResult.From(r, r.Code == CommandCodes.NotFound ? "follow" : null);
        }

        /// <summary>Drops a pin of a character, or all of them with <paramref name="pin"/> null (label «Unpin»); Value = how many.</summary>
        public OpResult RagdollUnpin(string nodeId, string pin = null, ExecuteOptions o = default)
        {
            var part = RagdollChara(nodeId, out var st, out var fail);
            if (part == null) return fail;
            if (pin != null && st.Ragdoll.Find(pin) == null) return OpResult.NotFound("no pin " + pin, "pin");
            var r = OpResult.From(part.Unpin(nodeId, pin, out int dropped, Labelled(o, Labels.RagdollUnpin)));
            r.Value = dropped;
            return r;
        }

        /// <summary>Drops the pins of every character (label «Unpin», one entry); Value = how many.</summary>
        public OpResult RagdollUnpinAll(ExecuteOptions o = default)
        {
            var part = Ragdoll;
            if (part == null) return OpResult.Unsupported("this runtime has no ragdolls");
            var ids = new List<string>();
            foreach (var n in Model.PreOrder())
                if (n.State is CharaNodeState c && c.Ragdoll.Pins.Count > 0) ids.Add(n.Id);
            int total = 0;
            var r = ids.Count == 0 ? OpResult.NothingToDo() : RagdollEntry(Labels.RagdollUnpin, o, step =>
            {
                foreach (string id in ids)
                {
                    var c = part.Unpin(id, null, out int dropped, step);
                    if (!c.Ok) return OpResult.From(c);
                    total += dropped;
                }
                return OpResult.Success();
            });
            r.Value = r.Ok ? total : 0;
            return r;
        }

        /// <summary>A following pin becomes a plain one where it is, with the grab's spring (label «Pin»).</summary>
        public OpResult RagdollStay(string nodeId, string pin, ExecuteOptions o = default)
        {
            var part = RagdollChara(nodeId, out var st, out var fail);
            if (part == null) return fail;
            if (st.Ragdoll.Find(pin) == null) return OpResult.NotFound("no pin " + pin, "pin");
            return OpResult.From(part.Stay(nodeId, pin, Labelled(o, Labels.RagdollPin)));
        }

        /// <summary>Two parts tied where they are now (label «Tie»).</summary>
        public OpResult RagdollTie(string nodeId, string a, string b, ExecuteOptions o = default)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            if (!RagdollParts.IsKnown(a)) return RagdollBadPart(a, "a");
            if (!RagdollParts.IsKnown(b)) return RagdollBadPart(b, "b");
            if (a == b) return OpResult.BadArgs("b", "another part", "a part is not tied to itself");
            bool tied = false;
            foreach (var t in RagdollBlock(nodeId).Ties) tied |= (t.A == a && t.B == b) || (t.A == b && t.B == a);
            if (!tied && RagdollCount(c => c.Ties.Count) >= RagdollLimits.MaxTies) return RagdollOverLimit("b", RagdollLimits.MaxTies, "ties");
            return OpResult.From(Ragdoll.Tie(nodeId, a, b, Labelled(o, Labels.RagdollTie)));
        }

        /// <summary>A tie goes (label «Tie»).</summary>
        public OpResult RagdollUntie(string nodeId, string a, string b, ExecuteOptions o = default)
        {
            var part = RagdollChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            return OpResult.From(part.Untie(nodeId, a, b, Labelled(o, Labels.RagdollTie)));
        }

        /// <summary>Each part whose key starts with one of <paramref name="prefixes"/> (comma separated) holds its rotation as it is now (label «Hold»); Value = how many joints.</summary>
        public OpResult RagdollHold(string nodeId, string prefixes, ExecuteOptions o = default)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            foreach (string x in (prefixes ?? "").Split(','))
            {
                string prefix = x.Trim();
                bool any = false;
                foreach (string key in RagdollParts.Keys) any |= prefix.Length > 0 && key.StartsWith(prefix, StringComparison.Ordinal);
                if (!any) return RagdollBadPart(prefix, "parts");
            }
            var r = OpResult.From(Ragdoll.Hold(nodeId, prefixes, out int held, Labelled(o, Labels.RagdollHold)));
            r.Value = held;
            return r;
        }

        /// <summary>A hold goes (label «Hold»).</summary>
        public OpResult RagdollUnhold(string nodeId, string part, ExecuteOptions o = default)
        {
            var p = RagdollChara(nodeId, out _, out var fail);
            if (p == null) return fail;
            return OpResult.From(p.Unhold(nodeId, part, Labelled(o, Labels.RagdollHold)));
        }

        /// <summary>
        /// A folder that follows a part's bone, by a constraint with no offset (label «Follower», one entry); Value = a <see cref="RagdollFollowerResult"/>. It goes on following the bone after a
        /// release.
        /// </summary>
        public OpResult RagdollFollower(string nodeId, string part, ExecuteOptions o = default)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            var row = RagdollParts.Find(part);
            if (row == null) return OpResult.NotFound("no part \"" + part + "\"; the parts are " + RagdollParts.List(), "part");
            string name = Model.Find(nodeId).Label + " " + part;
            var made = new RagdollFollowerResult();
            var r = RagdollEntry(Labels.RagdollFollower, o, step =>
            {
                var add = Session.AddFolder(null, null, -1, step);
                if (!add.Ok) return OpResult.From(add);
                made.Folder = add.NewIds[0];
                var named = Session.Set(made.Folder, "name", name, step);
                if (!named.Ok) return OpResult.From(named);
                rt.ApplyNow();
                var c = AddConstraint(new ConstraintAddRequest
                {
                    Source = new ConstraintEnd(nodeId, row.Bone),
                    Target = new ConstraintEnd(made.Folder),
                    Components = new[] { ConstraintComponents.Parent },
                    KeepOffset = false,
                }, step);
                if (c.Ok) made.Constraint = (c.Value as ConstraintInfo)?.Entry.Id;
                return c;
            });
            if (r.Ok)
            {
                r.Value = made;
                r.NewIds = new[] { made.Folder };
            }
            return r;
        }

        // ------------------------------------------------------------ what acts once: no history

        /// <summary>An impulse on a part, at its centre; every part wakes.</summary>
        public OpResult RagdollPush(string nodeId, string part, Vector3 impulse)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            var p = body.Part(part);
            if (p == null) return RagdollBadPart(part);
            body.Push(p, impulse);
            return OpResult.Success();
        }

        /// <summary>The tool's push: along <paramref name="direction"/> at a point of a part, by the world's push speed and the body's mass.</summary>
        public OpResult RagdollPushAt(string nodeId, string part, Vector3 point, Vector3 direction)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            var p = body.Part(part);
            if (p == null) return RagdollBadPart(part);
            body.PushAt(p, point, direction);
            return OpResult.Success();
        }

        /// <summary>Once, the trunk turns towards <paramref name="dir"/>; Value = true when the character has a body.</summary>
        public OpResult RagdollTurn(string nodeId, Vector3 dir, float strength)
        {
            var part = RagdollChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            rt.ApplyNow();
            var body = part.BodyOf(nodeId);
            body?.Turn(dir, strength);
            var r = OpResult.Success();
            r.Value = body != null;
            return r;
        }

        /// <summary>For a time the trunk turns towards <paramref name="dir"/>, as a spring with a damper; Value = true when the character has a body.</summary>
        public OpResult RagdollSettle(string nodeId, Vector3 dir, float seconds, float gain, float damp)
        {
            var part = RagdollChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            rt.ApplyNow();
            var body = part.BodyOf(nodeId);
            body?.Settle(dir, seconds, gain, damp);
            var r = OpResult.Success();
            r.Value = body != null;
            return r;
        }

        /// <summary>The body goes down; Value = the state's name after it.</summary>
        public OpResult RagdollKnock(string nodeId)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            body.Knock();
            var r = OpResult.Success();
            r.Value = RagdollStateName(body.State);
            return r;
        }

        /// <summary>A joint further than <paramref name="degrees"/> from its target goes back to its place.</summary>
        public OpResult RagdollReseat(string nodeId, float degrees = 45f)
        {
            var body = RagdollLive(nodeId, out var fail);
            if (body == null) return fail;
            body.Reseat(degrees);
            return OpResult.Success();
        }

        /// <summary>A prop along a ray, its kind, mass, speed and size by the world's values unless given; the world comes with it. Value = the props there are then.</summary>
        public OpResult RagdollThrow(Ray ray, string kind = null, float? mass = null, float? speed = null, float? size = null)
        {
            var part = Ragdoll;
            if (part == null) return OpResult.Unsupported("this runtime has no ragdolls");
            rt.ApplyNow();
            var props = part.EnsureWorld().Props;
            props.Throw(ray, kind, mass, speed, size);
            var r = OpResult.Success();
            r.Value = props.Count;
            return r;
        }

        /// <summary>Every prop goes, and the world with the last of them when nothing else keeps it.</summary>
        public OpResult RagdollClearProps()
        {
            var part = Ragdoll;
            part?.World?.Props?.Clear();
            part?.CloseIfEmpty();
            return OpResult.Success();
        }

        /// <summary>
        /// One bullet from <paramref name="origin"/> along <paramref name="dir"/> turned by a random offset in a disc of <paramref name="spread"/> degrees, its tracer from
        /// <paramref name="muzzle"/>; Value = the <see cref="ShotResult"/>, reused by the next shot. With <paramref name="kill"/> the first character hit goes limp, with <paramref name="stain"/>
        /// it takes a stain.
        /// </summary>
        public OpResult RagdollShoot(Vector3 origin, Vector3 dir, Vector3 muzzle, float spread, float momentum, bool kill, bool stain = false)
        {
            rt.ApplyNow();
            var shots = RagdollShots.For(rt);
            var r = OpResult.Success();
            r.Value = shots.Fire(origin, shots.Spread(dir, spread), muzzle, momentum, kill, stain);
            return r;
        }

        // ------------------------------------------------------------ the scene's block

        /// <summary>A kinematic capsule along two bones of a character, with or without a body (label «Pusher»); Value = how many there are then.</summary>
        public OpResult RagdollPusher(string nodeId, string a, string b, float radius, ExecuteOptions o = default)
        {
            var part = RagdollChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            fail = WetBuilt(nodeId);
            if (fail != null) return fail;
            var r = rt.FindChara(nodeId);
            if (!AdvancedPoseResolver.Resolve(r, a)) return OpResult.NotFound("no bone \"" + a + "\"", "a");
            if (!AdvancedPoseResolver.Resolve(r, b)) return OpResult.NotFound("no bone \"" + b + "\"", "b");
            if (!Finite(radius) || radius < 0f) return OpResult.BadArgs("radius", "a number not below 0");
            var res = OpResult.From(part.AddPusher(nodeId, a, b, radius, out int count, Labelled(o, Labels.RagdollPusher)));
            if (res.Ok) res.Value = count;
            return res;
        }

        /// <summary>An item takes part as a static collider that follows it, or stops (label «Obstacle»); Value = how many there are then.</summary>
        public OpResult RagdollObstacle(string nodeId, bool on = true, ExecuteOptions o = default)
        {
            var n = Find(nodeId, "key", out var fail);
            if (n == null) return fail;
            if (n.Kind != NodeKind.Item) return OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", not an item", "key", "item");
            var r = OpResult.From(RagdollObstacles.Set(rt, nodeId, on, out int count, Labelled(o, Labels.RagdollObstacle)));
            r.Value = count;
            return r;
        }

        // ------------------------------------------------------------ values

        /// <summary>
        /// Sets values by name in one entry (label «Ragdoll settings»): the members of a character's and of the scene's <c>values</c>, room, room.center, room.size, floor, floor.mode,
        /// floor.offset, floor.height. <paramref name="preset"/> comes first, so a value given with it wins; with <paramref name="snap"/> every part goes to its target. Numbers are stored as
        /// given; the ranges act when a value is applied. What belongs to a character needs <paramref name="nodeId"/>: a value of a character without a body waits in its block. Changed = the
        /// names that changed something.
        /// </summary>
        public OpResult RagdollSet(string nodeId, IReadOnlyList<KeyValuePair<string, JToken>> values, string preset = null, bool snap = false, ExecuteOptions o = default)
        {
            var part = Ragdoll;
            if (part == null) return OpResult.Unsupported("this runtime has no ragdolls");
            bool chara = preset != null || snap;
            if (values != null)
                foreach (var kv in values)
                {
                    var block = RagdollEdits.BlockOf(kv.Key);
                    if (block == RagdollValueBlock.None) return OpResult.Fail(CommandCodes.BadArgs, "no value " + kv.Key, "values." + kv.Key, null, RagdollEdits.Names());
                    chara |= block == RagdollValueBlock.Body;
                }
            CharaNodeState st = null;
            if (chara)
            {
                if (nodeId == null) return OpResult.NotFound("no character: give a key or select one", "key");
                RagdollChara(nodeId, out st, out var fail);
                if (fail != null) return fail;
                rt.ApplyNow();
                if (snap && part.BodyOf(nodeId) == null) return OpResult.Unavailable(RagdollNoBody, "key", "NoBody");
                if (preset != null && !RagdollPreset.IsKnown(preset)) return OpResult.BadArgs("preset", string.Join(", ", RagdollPreset.Names));
            }
            // on copies first: a refusal leaves the document as it was, the capture included
            var refused = RagdollWrite(st?.Ragdoll.Clone(), Model.Ragdoll?.Clone() ?? new SceneRagdollWorld(), values, preset, new List<string>(), out _, out _);
            if (refused != null) return refused;
            if (chara) part.CaptureNow(nodeId);
            var body = st?.Ragdoll.Clone();
            var world = Model.Ragdoll?.Clone() ?? new SceneRagdollWorld();
            var changed = new List<string>();
            RagdollWrite(body, world, values, preset, changed, out bool bodyEdit, out bool worldEdit);
            if (snap && body.Limp)
            {
                body.Limp = false;
                bodyEdit = true;
            }
            var r = RagdollCommit(nodeId, bodyEdit ? body : null, worldEdit ? world : null, Labels.RagdollSettings, o);
            if (!r.Ok) return r;
            if (snap)
            {
                part.BodyOf(nodeId)?.Snap();
                changed.Add("snap");
            }
            r.Changed.AddRange(changed);
            return r;
        }

        // A preset, then the values, into the copies of the two blocks. What stops it, or null.
        static OpResult RagdollWrite(CharaRagdoll body, SceneRagdollWorld world, IReadOnlyList<KeyValuePair<string, JToken>> values, string preset, List<string> changed, out bool bodyEdit, out bool worldEdit)
        {
            bodyEdit = worldEdit = false;
            if (preset != null)
            {
                string before = body.ToJson();
                RagdollPreset.ApplyTo(body.Values, preset);
                if (body.ToJson() != before)
                {
                    bodyEdit = true;
                    changed.Add("preset");
                }
            }
            if (values == null) return null;
            foreach (var kv in values)
            {
                string why = RagdollEdits.Set(body, world, kv.Key, kv.Value, out bool moved);
                if (why != null) return OpResult.BadArgs("values." + kv.Key, null, why);
                if (!moved) continue;
                changed.Add(kv.Key);
                if (RagdollEdits.BlockOf(kv.Key) == RagdollValueBlock.Body) bodyEdit = true;
                else worldEdit = true;
            }
            return null;
        }

        /// <summary>One value by name, as <see cref="RagdollSet"/>: what a row of the panel calls.</summary>
        public OpResult RagdollValue(string nodeId, string name, JToken value, ExecuteOptions o = default) =>
            RagdollSet(nodeId, new[] { new KeyValuePair<string, JToken>(name, value) }, null, false, o);

        /// <summary>A preset of the character's values (label «Ragdoll settings»).</summary>
        public OpResult RagdollUsePreset(string nodeId, string preset, ExecuteOptions o = default) => RagdollSet(nodeId, null, preset, false, o);

        /// <summary>Every part goes to its target, speeds 0, state up.</summary>
        public OpResult RagdollSnap(string nodeId, ExecuteOptions o = default) => RagdollSet(nodeId, null, null, true, o);
    }
}
