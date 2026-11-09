// The ragdoll blocks on the bus (RagdollEditCommand, RagdollWorldEditCommand) and the model's numbers. RM1 the defaults of both `values`, the presets, the room and the pusher, the part table and its
// mass shares. RE0 each edit is one history entry that undo and redo restore to the byte, with one change record per apply and revert: NodeChanged(id, "chara.ragdoll"), SceneChanged("ragdoll"); a block in the
// model is replaced, never changed in place, and shares nothing with the caller or the history; a drag merges by key, an edit of another node does not; with `undo: false` nothing is recorded and the
// history stays; a batch is one entry; refusals: no node, not a character, an id, a limit of a block and of the whole scene; a file's excess may stay or shrink; the field paths a capture writes reach the
// block silently, unrecorded; a saved session holds both blocks and a cleared one does not; a new scene has neither. Synthetic ids and values only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Ragdoll
    {
        static string Id(int n) => n.ToString("x32");

        static string AddChara(TestRunner t, SceneSession s) => s.AddChara(new CharaAppearance { Stored = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone() }).NewIds[0];

        static SceneSession NewSession(TestRunner t, out string chara, out string item)
        {
            var s = Kit.Session();
            chara = AddChara(t, s);
            item = s.AddItem(new ItemNodeState { Ref = "test.mod#1" }).NewIds[0];
            return s;
        }

        static CharaNodeState St(SceneSession s, string id) => (CharaNodeState)s.Model.Find(id).State;

        static CharaRagdoll Block(int pins = 1, int ties = 0)
        {
            var b = new CharaRagdoll { On = true };
            for (int i = 0; i < pins; i++) b.Pins.Add(new RagdollPin { Id = b.NextPinId(), Part = "hand_R", At = new Vec3(i, 0f, 0f) });
            for (int i = 0; i < ties; i++) b.Ties.Add(new RagdollTie { A = "hand_L", B = "hand_R" });
            return b;
        }

        static SceneRagdollWorld World(string key)
        {
            var w = new SceneRagdollWorld();
            w.Pushers.Add(new RagdollPusher { Id = w.NextPusherId(), Key = key });
            return w;
        }

        static CommandResult Run(SceneSession s, ISceneCommand c, string key = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(c, new ExecuteOptions { MergeKey = key, Record = mode });

        public static void Run(TestRunner t)
        {
            RM1(t);
            Steps(t);
            WorldSteps(t);
            Copies(t);
            Merge(t);
            NoHistory(t);
            Batches(t);
            Refusals(t);
            SceneLimits(t);
            Capture(t);
            Saved(t);
        }

        // ---------------- RM1 ----------------

        static void RM1(TestRunner t)
        {
            var v = new RagdollBodyValues();
            t.Check(v.Muscle == 20f && v.MuscleDamping == 1f && v.Pin == 1f && v.PinFreq == 12f && v.PinFalloff == 3f && !v.PinHipsOnly && v.KnockDistance == 3.5f && v.KnockHit == 8f, "RM1: a new block's values are the Puppet preset");
            t.Check(v.GravityComp && v.DownMuscle == 0.15f && v.MinDown == 1.5f && v.GetUp == 2.5f && v.Relocate && !v.FaceCamera && !v.LegCollision && v.Mass == 50f, "RM1: and the other defaults of a body");
            t.Check(v.IsDefault, "RM1: IsDefault");
            var w = new RagdollWorldValues();
            t.Check(w.Gravity == 98.1f && w.TimeScale == 1f && w.Hz == 240f && w.Friction == 3f && w.AngularDrag == 0.5f && w.PushSpeed == 20f, "RM1: the world's motion");
            t.Check(w.GrabStiffness == 60f && w.GrabMax == 6f && w.FollowStiffness == 400f && w.FollowMax == 20f && w.BlendOut == 0.3f, "RM1: grabs, follow pins, the blend");
            t.Check(w.Transfer == 0.18f && w.MaxPartDv == 40f && w.HitWeaken == 20f && w.WeakenRegain == 1.93f && w.WeakMuscle == 0.27f && w.StaggerAt == 600f && w.Momentum == 181f && w.FireRate == 10f, "RM1: shots");
            t.Check(w.PropKind == "ball" && w.PropMass == 2f && w.PropSpeed == 80f && w.PropSize == 1.2f, "RM1: props");
            t.Check(w.IsDefault, "RM1: IsDefault of the world");
            var room = new RagdollRoomValues();
            t.Check(!room.Center.HasValue && room.Size.Equals(new Vec3(40f, 30f, 40f)) && room.IsDefault && room.AppliedSize.Equals(room.Size), "RM1: a room nobody placed: 40 x 30 x 40");
            var floor = new RagdollFloorValues();
            t.Check(floor.Mode == "map" && floor.Offset == 0f && floor.Height == 0f && floor.IsDefault, "RM1: the floor comes from the map");
            var pusher = new RagdollPusher();
            t.Check(pusher.A == "cf_J_LegLow01_R" && pusher.B == "cf_J_Toes01_R" && pusher.Radius == 0.45f, "RM1: a pusher's defaults");

            // The presets: each writes its row, and leaves a value of an empty cell.
            t.Equal("limp,stiff,puppet,hipsPin", string.Join(",", RagdollPreset.Names), "RM1: the four presets");
            t.Check(RagdollPreset.IsKnown("puppet") && !RagdollPreset.IsKnown("Puppet") && !RagdollPreset.IsKnown("") && !RagdollPreset.IsKnown(null), "RM1: names are exact");
            RagdollBodyValues Away() => new RagdollBodyValues { Muscle = 55f, MuscleDamping = 1.7f, Pin = 0.3f, PinFreq = 40f, PinFalloff = 9f, PinHipsOnly = true, KnockDistance = 11f, KnockHit = 44f, GravityComp = false, DownMuscle = 0.7f, MinDown = 7f, GetUp = 4f, Relocate = false, FaceCamera = true, LegCollision = true, Mass = 99f };
            void Rest(RagdollBodyValues x, string what) => t.Check(!x.GravityComp && x.DownMuscle == 0.7f && x.MinDown == 7f && x.GetUp == 4f && !x.Relocate && x.FaceCamera && x.LegCollision && x.Mass == 99f, what + ": no value outside the table moves");
            var limp = Away();
            t.Check(RagdollPreset.ApplyTo(limp, RagdollPreset.Limp) && limp.Muscle == 0f && limp.Pin == 0f, "RM1: Limp: muscle 0, pin 0");
            t.Check(limp.MuscleDamping == 1.7f && limp.PinFreq == 40f && limp.PinFalloff == 9f && limp.PinHipsOnly && limp.KnockDistance == 11f && limp.KnockHit == 44f, "RM1: Limp: its empty cells stay");
            Rest(limp, "RM1: Limp");
            var stiff = Away();
            t.Check(RagdollPreset.ApplyTo(stiff, RagdollPreset.Stiff) && stiff.Muscle == 20f && stiff.MuscleDamping == 1f && stiff.Pin == 0f, "RM1: Stiff: muscle 20, damping 1, pin 0");
            t.Check(stiff.PinFreq == 40f && stiff.PinFalloff == 9f && stiff.PinHipsOnly && stiff.KnockDistance == 11f && stiff.KnockHit == 44f, "RM1: Stiff: its empty cells stay");
            Rest(stiff, "RM1: Stiff");
            var puppet = Away();
            t.Check(RagdollPreset.ApplyTo(puppet, RagdollPreset.Puppet) && puppet.Muscle == 20f && puppet.MuscleDamping == 1f && puppet.Pin == 1f && puppet.PinFreq == 12f && puppet.PinFalloff == 3f && !puppet.PinHipsOnly && puppet.KnockDistance == 3.5f && puppet.KnockHit == 8f, "RM1: Puppet: 20, 1, 1, 12, 3, false, 3.5, 8");
            Rest(puppet, "RM1: Puppet");
            var hips = Away();
            t.Check(RagdollPreset.ApplyTo(hips, RagdollPreset.HipsPin) && hips.Muscle == 20f && hips.MuscleDamping == 1f && hips.Pin == 1f && hips.PinFreq == 15f && hips.PinFalloff == 0f && hips.PinHipsOnly && hips.KnockDistance == 0f && hips.KnockHit == 0f, "RM1: Hips pin: 20, 1, 1, 15, 0, true, 0, 0");
            Rest(hips, "RM1: Hips pin");
            var unknown = Away();
            t.Check(!RagdollPreset.ApplyTo(unknown, "tense") && !RagdollPreset.ApplyTo(unknown, null) && unknown.Muscle == 55f, "RM1: an unknown preset changes nothing");
            t.Throws<ArgumentNullException>(() => RagdollPreset.ApplyTo(null, RagdollPreset.Limp), "RM1: no values");
            var fresh = new RagdollBodyValues();
            RagdollPreset.ApplyTo(fresh, RagdollPreset.Puppet);
            t.Check(fresh.IsDefault, "RM1: Puppet on a new block is the default");

            // The parts.
            var rows = new (string key, string bone, string parent, float share)[]
            {
                ("pelvis", "cf_J_Hips", null, 0.14f), ("abdomen", "cf_J_Spine01", "pelvis", 0.12f), ("chest", "cf_J_Spine02", "abdomen", 0.2f), ("head", "cf_J_Neck", "chest", 0.08f),
                ("upperarm_L", "cf_J_ArmUp00_L", "chest", 0.03f), ("upperarm_R", "cf_J_ArmUp00_R", "chest", 0.03f), ("forearm_L", "cf_J_ArmLow01_L", "upperarm_L", 0.018f), ("forearm_R", "cf_J_ArmLow01_R", "upperarm_R", 0.018f),
                ("hand_L", "cf_J_Hand_L", "forearm_L", 0.007f), ("hand_R", "cf_J_Hand_R", "forearm_R", 0.007f), ("thigh_L", "cf_J_LegUp00_L", "pelvis", 0.1f), ("thigh_R", "cf_J_LegUp00_R", "pelvis", 0.1f),
                ("shin_L", "cf_J_LegLow01_L", "thigh_L", 0.045f), ("shin_R", "cf_J_LegLow01_R", "thigh_R", 0.045f), ("foot_L", "cf_J_Foot01_L", "shin_L", 0.015f), ("foot_R", "cf_J_Foot01_R", "shin_R", 0.015f),
            };
            t.Equal(16, RagdollParts.All.Count, "RM1: 16 parts");
            for (int i = 0; i < rows.Length; i++)
            {
                var p = RagdollParts.All[i];
                t.Check(p.Key == rows[i].key && p.Bone == rows[i].bone && p.Parent == rows[i].parent && p.Share == rows[i].share, "RM1: part " + rows[i].key);
                t.Check(p.Parent == null || RagdollParts.IndexOf(p.Parent) < i, "RM1: " + rows[i].key + "'s parent comes first");
                t.Check(RagdollParts.IsKnown(p.Key) && RagdollParts.IndexOf(p.Key) == i && ReferenceEquals(RagdollParts.Find(p.Key), p) && RagdollParts.Keys[i] == p.Key, "RM1: " + rows[i].key + " is found");
            }
            t.Equal(15, RagdollParts.All.Count(p => p.Parent != null), "RM1: one root, 15 joints");
            t.Check(!RagdollParts.IsKnown("Pelvis") && !RagdollParts.IsKnown("") && !RagdollParts.IsKnown(null) && RagdollParts.Find("tail") == null && RagdollParts.IndexOf("tail") == -1, "RM1: keys are exact");
            t.Check(RagdollParts.List().Contains("foot_R") && RagdollParts.List().StartsWith("pelvis", StringComparison.Ordinal), "RM1: the list for a message");
            double shares = RagdollParts.All.Sum(p => (double)p.Share);
            t.Info("RM1: the shares add to " + shares.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + ", so a body of mass 50 weighs " + (shares * 50).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            t.Check(Math.Abs(shares - 0.97) < 1e-6, "RM1: the shares add to 0.97");
            t.Check(Math.Abs(shares * v.Mass - 48.5) < 1e-4, "RM1: so a body weighs 48.5");
            t.Equal(16, RagdollParts.All.Select(p => p.Bone).Distinct().Count(), "RM1: every part writes a bone of its own");
        }

        // ---------------- a character's block ----------------

        static void Steps(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            int n0 = s.Commands.UndoList.Count;
            var states = new List<string> { St(s, id).Ragdoll.ToJson() };
            var labels = new List<string>();
            var rec = new EventRecorder(s);
            void Step(CharaRagdoll after, string label)
            {
                rec.Clear();
                Kit.Ok(t, Run(s, new RagdollEditCommand(id, after, label)), "RE0: " + label);
                states.Add(St(s, id).Ragdoll.ToJson());
                labels.Add(label);
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Kind == SceneChangeKind.NodeChanged && rec.Batches[0].Changes[0].NodeId == id &&
                        rec.Batches[0].Changes[0].Path == RagdollEditCommand.ChangePath, "RE0: " + label + " reports one NodeChanged(id, \"chara.ragdoll\")");
                t.Equal(label, s.Commands.UndoList[0].Label, "RE0: its history label");
            }
            t.Equal("chara.ragdoll", RagdollEditCommand.ChangePath, "RE0: the change path");
            Step(new CharaRagdoll { On = true }, RagdollEditCommand.LabelRagdoll);
            var b = St(s, id).Ragdoll.Clone();
            b.Pins.Add(new RagdollPin { Id = b.NextPinId(), Part = "hand_R", At = new Vec3(1f, 2f, 3f) });
            Step(b, RagdollEditCommand.LabelPin);
            b = St(s, id).Ragdoll.Clone();
            b.Ties.Add(new RagdollTie { A = "hand_L", B = "hand_R" });
            Step(b, RagdollEditCommand.LabelTie);
            b = St(s, id).Ragdoll.Clone();
            b.Holds.Add(new RagdollHold { Part = "forearm_L", Rot = new Quat4(0f, 0.6f, 0f, 0.8f) });
            Step(b, RagdollEditCommand.LabelHold);
            b = St(s, id).Ragdoll.Clone();
            b.Values.Muscle = 35f;
            Step(b, RagdollEditCommand.LabelSettings);
            b = St(s, id).Ragdoll.Clone();
            b.Pins.Clear();
            Step(b, RagdollEditCommand.LabelUnpin);
            b = St(s, id).Ragdoll.Clone();
            b.Release();
            Step(b, RagdollEditCommand.LabelRelease);
            t.Check(!St(s, id).Ragdoll.On && St(s, id).Ragdoll.Values.Muscle == 35f, "RE0: a release keeps the values");
            Step(new CharaRagdoll(), RagdollEditCommand.LabelBake);
            t.Check(St(s, id).Ragdoll.IsEmpty, "RE0: an empty block as the after empties the block");
            t.Equal(n0 + 8, s.Commands.UndoList.Count, "RE0: one entry per edit");

            for (int i = labels.Count - 1; i >= 0; i--)
            {
                rec.Clear();
                Kit.Ok(t, s.Commands.Undo(), "RE0: undo " + labels[i]);
                t.Equal(states[i], St(s, id).Ragdoll.ToJson(), "RE0: undo " + labels[i] + " restores the block to the byte");
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Path == RagdollEditCommand.ChangePath, "RE0: undo " + labels[i] + " reports it");
            }
            for (int i = 0; i < labels.Count; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "RE0: redo " + labels[i]);
                t.Equal(states[i + 1], St(s, id).Ragdoll.ToJson(), "RE0: redo " + labels[i]);
            }

            // Size of an entry, a no-op, the labels.
            var top = s.Commands.UndoList[0];
            long expect = RagdollEditCommand.BaseBytes + 2L * (states[labels.Count - 1].Length + states[labels.Count].Length);
            t.Check(top.Label == RagdollEditCommand.LabelBake && top.EstimatedBytes == expect, "RE0: entry size = 256 + 2 x (before + after JSON lengths) (" + top.EstimatedBytes + ", expected " + expect + ")");
            int entries = s.Commands.UndoList.Count;
            Kit.NoOp(t, Run(s, new RagdollEditCommand(id, St(s, id).Ragdoll)), "RE0: an edit to the same block");
            Kit.NoOp(t, Run(s, new RagdollEditCommand(id, null)), "RE0: null on an empty block");
            t.Equal(entries, s.Commands.UndoList.Count, "RE0: a no-op records nothing");
            t.Equal("Ragdoll settings", new RagdollEditCommand(id, null).Label, "RE0: the default label");
            t.Equal("Ragdoll settings", new RagdollEditCommand(id, null, null).Label, "RE0: a null label is the default");
            t.Equal("Ragdoll|Release ragdoll|Pin|Unpin|Tie|Hold|Ragdoll settings|Bake pose to FK", string.Join("|", RagdollEditCommand.LabelRagdoll, RagdollEditCommand.LabelRelease, RagdollEditCommand.LabelPin, RagdollEditCommand.LabelUnpin,
                RagdollEditCommand.LabelTie, RagdollEditCommand.LabelHold, RagdollEditCommand.LabelSettings, RagdollEditCommand.LabelBake), "RE0: the history labels");
        }

        // ---------------- the scene's block ----------------

        static void WorldSteps(TestRunner t)
        {
            var s = NewSession(t, out string id, out string item);
            int n0 = s.Commands.UndoList.Count;
            t.Check(s.Model.Ragdoll == null, "RE0 world: a new scene has none");
            var states = new List<string> { null };
            var labels = new List<string>();
            var rec = new EventRecorder(s);
            void Step(SceneRagdollWorld after, string label)
            {
                rec.Clear();
                Kit.Ok(t, Run(s, new RagdollWorldEditCommand(after, label)), "RE0 world: " + label);
                states.Add(s.Model.Ragdoll?.ToJson());
                labels.Add(label);
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Kind == SceneChangeKind.SceneChanged && rec.Batches[0].Changes[0].Path == RagdollWorldEditCommand.ChangePath, "RE0 world: " + label + " reports one SceneChanged(\"ragdoll\")");
                t.Equal(label, s.Commands.UndoList[0].Label, "RE0 world: its history label");
            }
            t.Equal("ragdoll", RagdollWorldEditCommand.ChangePath, "RE0 world: the change path");
            Step(World(id), RagdollWorldEditCommand.LabelPusher);
            var b = s.Model.Ragdoll.Clone();
            b.Obstacles.Add(item);
            Step(b, RagdollWorldEditCommand.LabelObstacle);
            b = s.Model.Ragdoll.Clone();
            b.Values.Gravity = 50f;
            b.Room.Center = new Vec2(1f, 2f);
            b.Floor.Mode = RagdollFloorValues.ModeFeet;
            Step(b, RagdollWorldEditCommand.LabelSettings);
            Step(new SceneRagdollWorld(), RagdollWorldEditCommand.LabelSettings);
            t.Check(s.Model.Ragdoll == null, "RE0 world: an empty block leaves the scene with none, never an empty one");
            t.Equal(n0 + 4, s.Commands.UndoList.Count, "RE0 world: one entry per edit");

            for (int i = labels.Count - 1; i >= 0; i--)
            {
                rec.Clear();
                Kit.Ok(t, s.Commands.Undo(), "RE0 world: undo " + labels[i]);
                t.Equal(states[i], s.Model.Ragdoll?.ToJson(), "RE0 world: undo " + labels[i] + " restores the block to the byte");
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Path == RagdollWorldEditCommand.ChangePath, "RE0 world: undo reports it");
            }
            t.Check(s.Model.Ragdoll == null, "RE0 world: undone to none");
            for (int i = 0; i < labels.Count; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "RE0 world: redo " + labels[i]);
                t.Equal(states[i + 1], s.Model.Ragdoll?.ToJson(), "RE0 world: redo " + labels[i]);
            }
            int entries = s.Commands.UndoList.Count;
            Kit.NoOp(t, Run(s, new RagdollWorldEditCommand(null)), "RE0 world: null with none in the scene");
            Kit.NoOp(t, Run(s, new RagdollWorldEditCommand(new SceneRagdollWorld())), "RE0 world: an empty block with none in the scene");
            Kit.Ok(t, Run(s, new RagdollWorldEditCommand(World(id))), "RE0 world: a pusher again");
            Kit.NoOp(t, Run(s, new RagdollWorldEditCommand(s.Model.Ragdoll)), "RE0 world: an edit to the same block");
            t.Equal(entries + 1, s.Commands.UndoList.Count, "RE0 world: a no-op records nothing");

            // Size, labels, what the command gives back; a block is replaced and shares nothing.
            var mine = World(id);
            mine.Values.Hz = 120f;
            var cmd = new RagdollWorldEditCommand(mine);
            mine.Values.Hz = 60f;
            Kit.Ok(t, Run(s, cmd), "RE0 world: an edit");
            t.Equal(120f, s.Model.Ragdoll.Values.Hz, "RE0 world: the command took the block as it stood");
            t.Check(!ReferenceEquals(s.Model.Ragdoll, mine), "RE0 world: the caller's object is not the model's");
            var after = cmd.After;
            after.Values.Hz = 1f;
            t.Equal(120f, cmd.After.Values.Hz, "RE0 world: After is a new copy each time");
            t.Check(new RagdollWorldEditCommand(null).After == null && new RagdollWorldEditCommand(new SceneRagdollWorld()).After == null, "RE0 world: After of an edit that leaves none");
            var held = s.Model.Ragdoll;
            var next = held.Clone();
            next.Values.Hz = 100f;
            Kit.Ok(t, Run(s, new RagdollWorldEditCommand(next)), "RE0 world: a second edit");
            t.Check(!ReferenceEquals(s.Model.Ragdoll, held) && held.Values.Hz == 120f, "RE0 world: an edit replaces the block, the old object stays as it was");
            long expect = RagdollWorldEditCommand.BaseBytes + 2L * (held.ToJson().Length + next.ToJson().Length);
            t.Equal(expect, s.Commands.UndoList[0].EstimatedBytes, "RE0 world: entry size = 256 + 2 x (before + after JSON lengths)");
            t.Equal("Ragdoll settings", new RagdollWorldEditCommand(null).Label, "RE0 world: the default label");
            t.Equal("Ragdoll settings", new RagdollWorldEditCommand(null, null).Label, "RE0 world: a null label is the default");
            t.Equal("Pusher|Obstacle|Ragdoll settings", string.Join("|", RagdollWorldEditCommand.LabelPusher, RagdollWorldEditCommand.LabelObstacle, RagdollWorldEditCommand.LabelSettings), "RE0 world: the history labels");

            // A drag merges; `undo: false` keeps the history.
            int n = s.Commands.UndoList.Count;
            string start = s.Model.Ragdoll.ToJson();
            for (int i = 1; i <= 40; i++)
            {
                var d = s.Model.Ragdoll.Clone();
                d.Values.Gravity = i;
                Kit.Ok(t, Run(s, new RagdollWorldEditCommand(d), "gravity"), "RE0 world merge: step " + i);
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Model.Ragdoll.Values.Gravity == 40f, "RE0 world merge: a drag of forty steps is one entry");
            Kit.Ok(t, s.Commands.Undo(), "RE0 world merge: undo");
            t.Equal(start, s.Model.Ragdoll.ToJson(), "RE0 world merge: undo goes back before the drag");
            t.Check(!new RagdollWorldEditCommand(null).TryMerge(new SetPropertyCommand(id, "name", "x")), "RE0 world merge: TryMerge refuses another command");
            n = s.Commands.UndoList.Count;
            long version = s.Commands.Version;
            var q = s.Model.Ragdoll.Clone();
            q.Values.Gravity = 7f;
            Kit.Ok(t, Run(s, new RagdollWorldEditCommand(q), null, HistoryMode.None), "RE0 world undo:false: an edit");
            t.Check(s.Model.Ragdoll.Values.Gravity == 7f && s.Commands.UndoList.Count == n && s.Commands.Version > version, "RE0 world undo:false: applied, nothing recorded, the version moves");

            // Refusals.
            var many = new SceneRagdollWorld();
            for (int i = 0; i <= RagdollLimits.MaxPushers; i++) many.Pushers.Add(new RagdollPusher { Id = many.NextPusherId(), Key = id });
            var r1 = Run(s, new RagdollWorldEditCommand(many));
            Kit.Fails(t, r1, CommandCodes.BadArgs, "RE0 world: a 33rd pusher");
            t.Check(r1.Message.Contains(RagdollFindings.Limit) && r1.Message.Contains("pushers"), "RE0 world: the refusal names the limit (" + r1.Message + ")");
            var obstacles = new SceneRagdollWorld();
            for (int i = 0; i <= RagdollLimits.MaxObstacles; i++) obstacles.Obstacles.Add(Id(0x100 + i));
            Kit.Fails(t, Run(s, new RagdollWorldEditCommand(obstacles)), CommandCodes.BadArgs, "RE0 world: a 65th obstacle");
            var twice = World(id);
            twice.Pushers.Add(new RagdollPusher { Id = "a", Key = id });
            Kit.Fails(t, Run(s, new RagdollWorldEditCommand(twice)), CommandCodes.BadArgs, "RE0 world: an id used twice");
            var noId = World(id);
            noId.Pushers[0].Id = "";
            Kit.Fails(t, Run(s, new RagdollWorldEditCommand(noId)), CommandCodes.BadArgs, "RE0 world: a pusher without an id");
            var noKey = World(null);
            Kit.Fails(t, Run(s, new RagdollWorldEditCommand(noKey)), CommandCodes.BadArgs, "RE0 world: a pusher without a node");
            var nan = World(id);
            nan.Values.Gravity = float.NaN;
            Kit.Fails(t, Run(s, new RagdollWorldEditCommand(nan)), CommandCodes.BadArgs, "RE0 world: a value that is not a number");
            t.Equal(7f, s.Model.Ragdoll.Values.Gravity, "RE0 world: a refused edit changes nothing");
        }

        static void Copies(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            var mine = Block();
            var cmd = new RagdollEditCommand(id, mine);
            mine.Pins[0].Part = "head";
            Kit.Ok(t, Run(s, cmd), "RE0: an edit");
            t.Equal("hand_R", St(s, id).Ragdoll.Pins[0].Part, "RE0: the command took the block as it stood");
            t.Check(!ReferenceEquals(St(s, id).Ragdoll, mine), "RE0: the caller's object is not the model's");
            var held = St(s, id).Ragdoll;
            t.Equal(cmd.NodeId, id, "RE0: NodeId");
            var after = cmd.After;
            after.Pins[0].Part = "x";
            t.Equal("hand_R", cmd.After.Pins[0].Part, "RE0: After is a new copy each time");

            var next = held.Clone();
            next.Values.Muscle = 5f;
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, next)), "RE0: a second edit");
            t.Check(!ReferenceEquals(St(s, id).Ragdoll, held) && held.Values.Muscle == 20f, "RE0: an edit replaces the block, the old object stays as it was");
            var before = St(s, id).Ragdoll;
            Kit.Ok(t, s.Commands.Undo(), "RE0: undo");
            t.Check(!ReferenceEquals(St(s, id).Ragdoll, before) && St(s, id).Ragdoll.Values.Muscle == 20f, "RE0: an undo too");
            St(s, id).Ragdoll.Pose.Hips = new Vec3(9f, 9f, 9f);   // a capture writes the model's object in place
            St(s, id).Ragdoll.Pose.Parts["pelvis"] = Quat4.Identity;
            Kit.Ok(t, s.Commands.Redo(), "RE0: redo");
            Kit.Ok(t, s.Commands.Undo(), "RE0: undo");
            t.Equal(9f, St(s, id).Ragdoll.Pose.Hips.X, "RE0: an entry's before is what the model held at apply");
        }

        static void Merge(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            string second = AddChara(t, s);
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, Block(), RagdollEditCommand.LabelRagdoll)), "RE0 merge: a body");
            int n = s.Commands.UndoList.Count;
            string start = St(s, id).Ragdoll.ToJson();
            s.Commands.MarkSaved();
            var rec = new EventRecorder(s);
            for (int i = 1; i <= 40; i++)
            {
                var b = St(s, id).Ragdoll.Clone();
                b.Values.Muscle = i;
                Kit.Ok(t, Run(s, new RagdollEditCommand(id, b), "muscle:" + id), "RE0 merge: step " + i);
            }
            t.Equal(40, rec.Batches.Count, "RE0 merge: each step reports");
            t.Check(s.Commands.UndoList.Count == n + 1 && St(s, id).Ragdoll.Values.Muscle == 40f, "RE0 merge: a drag of forty steps is one entry");
            long size = s.Commands.UndoList[0].EstimatedBytes;
            t.Check(size > RagdollEditCommand.BaseBytes && size < RagdollEditCommand.BaseBytes + 2L * 2 * (start.Length + 40), "RE0 merge: the entry's size counts two values, not forty (" + size + ")");
            string last = St(s, id).Ragdoll.ToJson();
            Kit.Ok(t, s.Commands.Undo(), "RE0 merge: undo");
            t.Equal(start, St(s, id).Ragdoll.ToJson(), "RE0 merge: undo goes back before the drag");
            Kit.Ok(t, s.Commands.Redo(), "RE0 merge: redo");
            t.Equal(last, St(s, id).Ragdoll.ToJson(), "RE0 merge: redo gives the drag's end");

            // Another key, another node, no key: new entries.
            n = s.Commands.UndoList.Count;
            var a = St(s, id).Ragdoll.Clone();
            a.Values.Pin = 0.5f;
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, a), "pin:" + id), "RE0 merge: another key");
            t.Equal(n + 1, s.Commands.UndoList.Count, "RE0 merge: another key is another entry");
            Kit.Ok(t, Run(s, new RagdollEditCommand(second, Block()), "pin:" + id), "RE0 merge: another node under the same key");
            t.Equal(n + 2, s.Commands.UndoList.Count, "RE0 merge: another node is another entry");
            t.Check(!St(s, second).Ragdoll.IsEmpty, "RE0 merge: and it reached that node");
            Kit.Ok(t, s.Commands.Undo(), "RE0 merge: undo of the other node");
            t.Check(St(s, second).Ragdoll.IsEmpty && St(s, id).Ragdoll.Values.Pin == 0.5f, "RE0 merge: undoes that node alone");
            var other = new RagdollEditCommand(id, Block());
            t.Check(!other.TryMerge(new RagdollEditCommand(second, Block())), "RE0 merge: TryMerge refuses another node");
            t.Check(!other.TryMerge(new SetPropertyCommand(id, "name", "x")), "RE0 merge: and another command");
        }

        static void NoHistory(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, Block(), RagdollEditCommand.LabelRagdoll)), "RE0 undo:false: a body");
            int n = s.Commands.UndoList.Count;
            long version = s.Commands.Version;
            var b = St(s, id).Ragdoll.Clone();
            b.Limp = true;
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, b), null, HistoryMode.None), "RE0 undo:false: an edit");
            t.Check(St(s, id).Ragdoll.Limp, "RE0 undo:false: it applied");
            t.Check(s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == RagdollEditCommand.LabelRagdoll, "RE0 undo:false: nothing recorded, the history kept");
            t.Check(s.Commands.Version > version && s.Commands.IsDirty, "RE0 undo:false: the scene's version moves");
            Kit.Ok(t, s.Commands.Undo(), "RE0 undo:false: the entry before it can still be undone");
            t.Check(St(s, id).Ragdoll.IsEmpty, "RE0 undo:false: the block as it was before that entry");
        }

        // A client batch: its edits are one entry; an unrecorded batch keeps the history; a cancelled one leaves the blocks as they were.
        static void Batches(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, Block(), RagdollEditCommand.LabelRagdoll)), "RE0 batch: a body");
            string start = St(s, id).Ragdoll.ToJson();
            int n = s.Commands.UndoList.Count;
            using (var tx = s.Commands.Begin("batch", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                var a = St(s, id).Ragdoll.Clone();
                a.Values.Muscle = 5f;
                Kit.Ok(t, tx.Execute(new RagdollEditCommand(id, a)), "RE0 batch: an edit inside");
                Kit.Ok(t, tx.Execute(new RagdollWorldEditCommand(World(id))), "RE0 batch: a world edit inside");
                Kit.Ok(t, tx.Commit(), "RE0 batch: commit");
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && St(s, id).Ragdoll.Values.Muscle == 5f && s.Model.Ragdoll != null, "RE0 batch: both edits in one entry");
            Kit.Ok(t, s.Commands.Undo(), "RE0 batch: undo");
            t.Check(St(s, id).Ragdoll.ToJson() == start && s.Model.Ragdoll == null, "RE0 batch: one undo takes both back");
            Kit.Ok(t, s.Commands.Redo(), "RE0 batch: redo");

            n = s.Commands.UndoList.Count;
            using (var tx = s.Commands.Begin("quiet", ExecuteOptions.NotRecorded))
            {
                var a = St(s, id).Ragdoll.Clone();
                a.Values.Pin = 0.5f;
                Kit.Ok(t, tx.Execute(new RagdollEditCommand(id, a)), "RE0 batch: an edit in an unrecorded batch");
                var w = s.Model.Ragdoll.Clone();
                w.Values.Hz = 120f;
                Kit.Ok(t, tx.Execute(new RagdollWorldEditCommand(w)), "RE0 batch: a world edit in an unrecorded batch");
                Kit.Ok(t, tx.Commit(), "RE0 batch: commit the unrecorded batch");
            }
            t.Check(s.Commands.UndoList.Count == n && St(s, id).Ragdoll.Values.Pin == 0.5f && s.Model.Ragdoll.Values.Hz == 120f, "RE0 batch: applied, nothing recorded, the history kept");

            string before = St(s, id).Ragdoll.ToJson(), beforeWorld = s.Model.Ragdoll.ToJson();
            using (var tx = s.Commands.Begin("cancelled", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                var a = St(s, id).Ragdoll.Clone();
                a.Limp = true;
                Kit.Ok(t, tx.Execute(new RagdollEditCommand(id, a)), "RE0 batch: an edit in a cancelled batch");
                Kit.Ok(t, tx.Execute(new RagdollWorldEditCommand(null)), "RE0 batch: a world edit in a cancelled batch");
                tx.Cancel();
            }
            t.Check(St(s, id).Ragdoll.ToJson() == before && s.Model.Ragdoll?.ToJson() == beforeWorld, "RE0 batch: a cancelled batch leaves the blocks as they were");
        }

        static void Refusals(TestRunner t)
        {
            var s = NewSession(t, out string id, out string item);
            Kit.Fails(t, Run(s, new RagdollEditCommand(Id(0xeeee), Block())), CommandCodes.NotFound, "RE0: no such node");
            Kit.Fails(t, Run(s, new RagdollEditCommand(item, Block())), CommandCodes.WrongKind, "RE0: an item is no character");
            var refused = Run(s, new RagdollEditCommand(id, Block(pins: RagdollLimits.MaxPins + 1)));
            Kit.Fails(t, refused, CommandCodes.BadArgs, "RE0: a 17th pin");
            t.Check(refused.Message.Contains(RagdollFindings.Limit) && refused.Message.Contains("pins"), "RE0: the refusal names the limit (" + refused.Message + ")");
            Kit.Fails(t, Run(s, new RagdollEditCommand(id, Block(pins: 0, ties: RagdollLimits.MaxTies + 1))), CommandCodes.BadArgs, "RE0: a 17th tie");
            t.Check(St(s, id).Ragdoll.IsEmpty, "RE0: a refused edit changes nothing");
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, Block(pins: RagdollLimits.MaxPins, ties: RagdollLimits.MaxTies))), "RE0: 16 pins and 16 ties");
            var second = new CharaRagdoll();
            second.Holds.Add(new RagdollHold { Part = "head" });
            second.Holds.Add(new RagdollHold { Part = "head" });
            var two = Run(s, new RagdollEditCommand(id, second));
            Kit.Fails(t, two, CommandCodes.BadArgs, "RE0: a second hold on a part");
            t.Check(two.Message.Contains(RagdollFindings.Limit) && two.Message.Contains("head"), "RE0: the refusal names the part (" + two.Message + ")");

            var twice = Block(pins: 2);
            twice.Pins[1].Id = twice.Pins[0].Id;
            Kit.Fails(t, Run(s, new RagdollEditCommand(id, twice)), CommandCodes.BadArgs, "RE0: an id used twice");
            var noId = Block();
            noId.Pins[0].Id = "";
            Kit.Fails(t, Run(s, new RagdollEditCommand(id, noId)), CommandCodes.BadArgs, "RE0: a pin without an id");
            var nan = Block();
            nan.Values.Muscle = float.NaN;
            Kit.Fails(t, Run(s, new RagdollEditCommand(id, nan)), CommandCodes.BadArgs, "RE0: a value that is not a number");
            var nul = Block();
            nul.Pins[0].Part = null;
            Kit.Fails(t, Run(s, new RagdollEditCommand(id, nul)), CommandCodes.BadArgs, "RE0: a null part");
            t.Check(float.IsNaN(nan.Values.Muscle), "RE0: (the caller's object is left as it was)");

            // A file's excess: the block may stay or shrink, and other values may change.
            var over = CharaRagdoll.FromToken(new JObject { ["pins"] = new JArray(Enumerable.Range(0, RagdollLimits.MaxPins + 2).Select(i => JObject.Parse("{\"id\": \"p" + i + "\", \"part\": \"head\"}"))) });
            St(s, id).Ragdoll = over;
            var edit = over.Clone();
            edit.Values.Muscle = 5f;
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, edit)), "RE0: a value of a block a file brought over the limit");
            var fewer = St(s, id).Ragdoll.Clone();
            fewer.Pins.RemoveAt(0);
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, fewer)), "RE0: one pin fewer");
            var more = St(s, id).Ragdoll.Clone();
            more.Pins.Add(new RagdollPin { Id = more.NextPinId(), Part = "head" });
            more.Pins.Add(new RagdollPin { Id = "zz", Part = "head" });
            Kit.Fails(t, Run(s, new RagdollEditCommand(id, more)), CommandCodes.BadArgs, "RE0: but not more than it held");
        }

        // The pins and the ties count over the whole scene.
        static void SceneLimits(TestRunner t)
        {
            var s = NewSession(t, out string a, out _);
            string b = AddChara(t, s);
            Kit.Ok(t, Run(s, new RagdollEditCommand(a, Block(pins: 10, ties: 10))), "RE0 scene: 10 pins and 10 ties on one character");
            Kit.Ok(t, Run(s, new RagdollEditCommand(b, Block(pins: 6, ties: 6))), "RE0 scene: 6 and 6 on another: 16 in the scene");
            var r = Run(s, new RagdollEditCommand(b, Block(pins: 7, ties: 6)));
            Kit.Fails(t, r, CommandCodes.BadArgs, "RE0 scene: a 17th pin in the scene");
            t.Check(r.Message.Contains(RagdollFindings.Limit) && r.Message.Contains("pins in the scene"), "RE0 scene: the refusal says so (" + r.Message + ")");
            var r2 = Run(s, new RagdollEditCommand(b, Block(pins: 6, ties: 7)));
            Kit.Fails(t, r2, CommandCodes.BadArgs, "RE0 scene: a 17th tie in the scene");
            t.Check(r2.Message.Contains("ties in the scene"), "RE0 scene: the refusal says so (" + r2.Message + ")");
            t.Equal(6, St(s, b).Ragdoll.Pins.Count, "RE0 scene: a refused edit changes nothing");

            // Moving one from a character to another in two steps works; a value's edit with the scene full works.
            Kit.Ok(t, Run(s, new RagdollEditCommand(a, Block(pins: 9, ties: 10))), "RE0 scene: one pin fewer on the first");
            Kit.Ok(t, Run(s, new RagdollEditCommand(b, Block(pins: 7, ties: 6))), "RE0 scene: so one more on the second");
            var edit = St(s, b).Ragdoll.Clone();
            edit.Values.Muscle = 5f;
            Kit.Ok(t, Run(s, new RagdollEditCommand(b, edit)), "RE0 scene: a value of a block in a full scene");

            // A file's excess over the scene may stay, and a change of another kind goes through; growing is refused.
            St(s, a).Ragdoll = Block(pins: 15, ties: 10);
            var fileOver = St(s, a).Ragdoll.Clone();
            fileOver.Values.Muscle = 3f;
            Kit.Ok(t, Run(s, new RagdollEditCommand(a, fileOver)), "RE0 scene: a value in a scene a file brought over the limit");
            var fewerTies = St(s, b).Ragdoll.Clone();
            fewerTies.Ties.RemoveAt(0);
            Kit.Ok(t, Run(s, new RagdollEditCommand(b, fewerTies)), "RE0 scene: one tie fewer there");
            Kit.Fails(t, Run(s, new RagdollEditCommand(a, Block(pins: 16, ties: 10))), CommandCodes.BadArgs, "RE0 scene: but not one more pin");
            var oneTie = St(s, b).Ragdoll.Clone();
            oneTie.Ties.Add(new RagdollTie { A = "hand_L", B = "hand_R" });
            Kit.Ok(t, Run(s, new RagdollEditCommand(b, oneTie)), "RE0 scene: an excess of pins does not stop a tie");
            St(s, a).Ragdoll = Block(pins: 1, ties: 20);
            var onePin = St(s, b).Ragdoll.Clone();
            onePin.Pins.Add(new RagdollPin { Id = onePin.NextPinId(), Part = "head" });
            Kit.Ok(t, Run(s, new RagdollEditCommand(b, onePin)), "RE0 scene: nor an excess of ties a pin");
        }

        static void Capture(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, Block(), RagdollEditCommand.LabelRagdoll)), "RE0 capture: a body");
            int n = s.Commands.UndoList.Count;
            var rec = new EventRecorder(s);
            var pose = new RagdollPose { Hips = new Vec3(1f, 2f, 3f) };
            pose.Parts["chest"] = new Quat4(0f, 0.6f, 0f, 0.8f);
            pose.Parts["pelvis"] = new Quat4(0.28f, 0f, 0f, 0.96f);
            var results = new List<CommandResult>();
            s.WriteSilently(ChangeOrigin.Capture, w => results.Add(w.Set(id, "chara.ragdoll.pose", pose)));
            foreach (var r in results) t.Check(r.Status == CommandStatus.Ok, "RE0 capture: a field write lands (" + r + ")");
            var b = St(s, id).Ragdoll;
            t.Check(b.Pose.Hips.Equals(new Vec3(1f, 2f, 3f)) && b.Pose.Parts.Count == 2 && b.Pose.Parts["chest"].Equals(new Quat4(0f, 0.6f, 0f, 0.8f)), "RE0 capture: the pose is in the model");
            t.Check(!ReferenceEquals(b.Pose, pose) && !ReferenceEquals(b.Pose.Parts, pose.Parts), "RE0 capture: a copy of the pose");
            t.Equal(n, s.Commands.UndoList.Count, "RE0 capture: not recorded");
            t.Check(rec.Batches.Count == 1 && rec.Batches[0].Origin == ChangeOrigin.Capture, "RE0 capture: one batch of origin Capture");
            t.Check(rec.Batches[0].Changes.All(c => c.Kind == SceneChangeKind.NodeChanged && FieldPath.Overlaps(RagdollEditCommand.ChangePath, c.Path)), "RE0 capture: under chara.ragdoll");
            s.WriteSilently(ChangeOrigin.Capture, w =>
            {
                results.Add(w.Set(id, "chara.ragdoll.pose.hips", new Vec3(4f, 5f, 6f)));
                results.Add(w.Set(id, "chara.ragdoll.pose.parts[\"head\"]", new Quat4(0f, 0f, 0.6f, 0.8f)));
            });
            foreach (var r in results) t.Check(r.Status == CommandStatus.Ok, "RE0 capture: a member's write lands (" + r + ")");
            t.Check(St(s, id).Ragdoll.Pose.Hips.Equals(new Vec3(4f, 5f, 6f)) && St(s, id).Ragdoll.Pose.Parts.Count == 3, "RE0 capture: the members by path");
            t.Equal("pelvis,chest,head", string.Join(",", ((JObject)St(s, id).Ragdoll.ToJObject()["pose"]["parts"]).Properties().Select(p => p.Name)), "RE0 capture: written in part order, whatever was set first");

            string captured = St(s, id).Ragdoll.ToJson();
            var next = St(s, id).Ragdoll.Clone();
            next.Values.Muscle = 5f;
            Kit.Ok(t, Run(s, new RagdollEditCommand(id, next)), "RE0 capture: an edit after the capture");
            Kit.Ok(t, s.Commands.Undo(), "RE0 capture: undo");
            t.Equal(captured, St(s, id).Ragdoll.ToJson(), "RE0 capture: an undo restores what was captured");
        }

        static void Saved(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            var opts = Kit.FixedSave(ThumbnailMode.Placeholder, new ExtRegistry());
            var b0 = s.ToBytes(opts);
            int FormatOf(byte[] f) => (int)JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(f).Get("manifest.json").Data))["formatVersion"];
            string Scene(byte[] f) => Encoding.UTF8.GetString(ContainerReader.Read(f).Get("scene.json").Data);
            t.Check(FormatOf(b0) == 1 && !Scene(b0).Contains("\"ragdoll\""), "RE0 file: no block: revision 1, no member");

            Kit.Ok(t, Run(s, new RagdollEditCommand(id, Block())), "RE0 file: a body");
            Kit.Ok(t, Run(s, new RagdollWorldEditCommand(World(id))), "RE0 file: a pusher");
            var b1 = s.ToBytes(opts);
            t.Check(FormatOf(b1) == 5 && Scene(b1).Contains("\"ragdoll\": {"), "RE0 file: revision 5, with the members");
            t.Equal(2, JObject.Parse(Scene(b1)).Descendants().OfType<JProperty>().Count(p => p.Name == "ragdoll"), "RE0 file: both members");

            var s2 = Kit.Session();
            var l = s2.Load(b1, new LoadOptions { Registry = new ExtRegistry() });
            t.Check(l.Ok, "RE0 file: it loads (" + l + ")");
            if (l.Ok)
            {
                t.Equal(St(s, id).Ragdoll.ToJson(), St(s2, id).Ragdoll.ToJson(), "RE0 file: the block comes back");
                t.Equal(s.Model.Ragdoll.ToJson(), s2.Model.Ragdoll?.ToJson(), "RE0 file: the world block comes back");
                t.Check(!s2.Commands.IsDirty, "RE0 file: a load is not an edit");
                t.Check(Kit.Same(b1, s2.ToBytes(opts)), "RE0 file: and a save gives the same file");
                s2.NewScene();
                t.Check(s2.Model.Ragdoll == null, "RE0 file: a new scene has no world block");
            }

            Kit.Ok(t, Run(s, new RagdollEditCommand(id, null, RagdollEditCommand.LabelRelease)), "RE0 file: released");
            Kit.Ok(t, Run(s, new RagdollWorldEditCommand(null)), "RE0 file: the world block removed");
            var b2 = s.ToBytes(opts);
            t.Check(FormatOf(b2) == 1 && Scene(b2) == Scene(b0) && Kit.Same(b2, b0), "RE0 file: back to revision 1, the file as before");
            Kit.Ok(t, s.Commands.Undo(), "RE0 file: undo");
            Kit.Ok(t, s.Commands.Undo(), "RE0 file: undo");
            t.Check(!St(s, id).Ragdoll.IsEmpty && s.Model.Ragdoll != null, "RE0 file: the blocks are back");
        }
    }
}
