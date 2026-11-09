// The edits of the two ragdoll blocks on a session, as the operations layer makes them: RagdollEdits on copies, then one edit command per block. RE1 each operation of the blocks is one entry
// with its label, undo and redo to the byte; RE3 a value dragged merges into one entry, of a character and of the scene; RE4 `undo: false` applies with no entry and keeps the history; RE5 what
// an undo gives back: a build's and a release's block. What a body does with them runs in BridgeCoreTest C47. Synthetic ids and names only.
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes.Ragdoll;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_RagdollOps
    {
        static CharaNodeState St(SceneSession s, string id) => (CharaNodeState)s.Model.Find(id).State;

        static SceneSession NewSession(TestRunner t, out string chara)
        {
            var s = Kit.Session();
            chara = s.AddChara(new CharaAppearance { Stored = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone() }).NewIds[0];
            return s;
        }

        static CommandResult Edit(SceneSession s, string id, CharaRagdoll after, string label, string key = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(new RagdollEditCommand(id, after, label), new ExecuteOptions { MergeKey = key, Record = mode });

        static CommandResult EditWorld(SceneSession s, SceneRagdollWorld after, string label, string key = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(new RagdollWorldEditCommand(after, label), new ExecuteOptions { MergeKey = key, Record = mode });

        static string Set(CharaRagdoll body, SceneRagdollWorld world, string name, object value, out bool changed) =>
            RagdollEdits.Set(body, world, name, value == null ? JValue.CreateNull() : JToken.FromObject(value), out changed);

        // One edit of the character's block: one entry with its label, undo and redo to the byte.
        static void Entry(TestRunner t, SceneSession s, string id, CharaRagdoll after, string label, string what)
        {
            string before = St(s, id).Ragdoll.ToJson();
            int n = s.Commands.UndoList.Count;
            Kit.Ok(t, Edit(s, id, after, label), what);
            string done = St(s, id).Ragdoll.ToJson();
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == label, what + ": one entry «" + label + "»");
            Kit.Ok(t, s.Commands.Undo(), what + ": undo");
            t.Equal(before, St(s, id).Ragdoll.ToJson(), what + ": undo restores the block to the byte");
            Kit.Ok(t, s.Commands.Redo(), what + ": redo");
            t.Equal(done, St(s, id).Ragdoll.ToJson(), what + ": redo restores it");
        }

        public static void Run(TestRunner t)
        {
            Names(t);
            Values(t);
            Room(t);
            Floor(t);
            Entries(t);
            Merge(t);
            NoHistory(t);
            UndoOfBuildAndRelease(t);
        }

        static void Names(TestRunner t)
        {
            foreach (string n in RagdollBodyValues.Names) t.Check(RagdollEdits.BlockOf(n) == RagdollValueBlock.Body, "a body's value: " + n);
            foreach (string n in RagdollWorldValues.Names) t.Check(RagdollEdits.BlockOf(n) == RagdollValueBlock.World, "a world's value: " + n);
            foreach (string n in new[] { "room", "room.center", "room.size", "floor", "floor.mode", "floor.offset", "floor.height" }) t.Check(RagdollEdits.BlockOf(n) == RagdollValueBlock.World, "of the scene: " + n);
            foreach (string n in new[] { "", "nope", "Muscle", "room.nope", "floor.nope", "pins", "on", "limp" }) t.Check(RagdollEdits.BlockOf(n) == RagdollValueBlock.None, "no value: \"" + n + "\"");
            t.Equal(RagdollBodyValues.Names.Count + RagdollWorldValues.Names.Count + 7, RagdollEdits.Names().Count, "the names listed");
            t.Equal(RagdollEdits.Names().Count, RagdollEdits.Names().Distinct().Count(), "no name in two blocks");
        }

        static void Values(TestRunner t)
        {
            var body = new CharaRagdoll();
            var world = new SceneRagdollWorld();
            t.Check(Set(body, world, "muscle", 30, out bool ch) == null && ch && body.Values.Muscle == 30f, "a body's number");
            t.Check(Set(body, world, "muscle", 30, out ch) == null && !ch, "the same number changes nothing");
            t.Check(Set(body, world, "relocate", false, out ch) == null && ch && !body.Values.Relocate, "a body's switch");
            t.Check(Set(body, world, "muscle", 1000, out ch) == null && ch && body.Values.Muscle == 1000f, "a number beyond its range is stored as given");
            t.Check(Set(body, world, "gravity", 50, out ch) == null && ch && world.Values.Gravity == 50f, "a world's number");
            t.Check(Set(body, world, "propKind", "heavy", out ch) == null && ch && world.Values.PropKind == "heavy", "a world's kind");
            t.Check(Set(null, world, "timeScale", 0.5, out ch) == null && ch, "a world's value needs no character");
            t.Check(Set(null, world, "muscle", 1, out ch) != null && !ch, "a body's value needs one");
            t.Check(Set(body, world, "muscle", "x", out ch) != null && !ch && body.Values.Muscle == 1000f, "text is no number, nothing changes");
            t.Check(Set(body, world, "relocate", 1, out ch) != null && !ch, "a number is no switch");
            t.Check(Set(body, world, "propKind", "nope", out ch) != null && !ch && world.Values.PropKind == "heavy", "an unknown kind");
            t.Check(Set(body, world, "nope", 1, out ch) != null && !ch, "an unknown name");
        }

        static void Room(TestRunner t)
        {
            var world = new SceneRagdollWorld();
            t.Check(Set(null, world, "room", new[] { -30f, 10f, -5f, 15f }, out bool ch) == null && ch, "a strip");
            t.Check(world.Room.Center?.Equals(new Vec2(-10f, 5f)) == true && world.Room.Size.Equals(new Vec3(40f, RagdollEdits.BuildRoomHeight, 20f)), "the middle, the two lengths, a build's height");
            t.Check(Set(null, world, "room", new[] { -30f, 10f, -5f, 15f }, out ch) == null && !ch, "the same strip changes nothing");
            foreach (var bad in new object[] { new[] { 1f, 0f, 0f, 1f }, new[] { 0f, 1f, 1f, 1f }, new[] { 1f, 2f }, 3, "x", new[] { 0f, float.NaN, 0f, 1f } })
            {
                var c0 = world.Room.Center;
                var s0 = world.Room.Size;
                t.Check(Set(null, world, "room", bad, out ch) != null && !ch && Equals(c0, world.Room.Center) && s0.Equals(world.Room.Size), "a room that is none leaves it: " + JToken.FromObject(bad).ToString(Newtonsoft.Json.Formatting.None));
            }
            t.Check(Set(null, world, "room", new { center = new[] { 1, 2 }, size = new[] { 30, 20, 50 } }, out ch) == null && ch && world.Room.Center?.Equals(new Vec2(1f, 2f)) == true && world.Room.Size.Equals(new Vec3(30f, 20f, 50f)), "{center, size}");
            t.Check(Set(null, world, "room", new { size = new[] { 31, 21, 51 } }, out ch) == null && ch && world.Room.Center?.Equals(new Vec2(1f, 2f)) == true && world.Room.Size.Equals(new Vec3(31f, 21f, 51f)), "a member left out stays");
            t.Check(Set(null, world, "room", new { center = new[] { 9, 9 }, nope = 1 }, out ch) != null && !ch && world.Room.Center?.Equals(new Vec2(1f, 2f)) == true, "a member that is none leaves the room as it was");
            t.Check(Set(null, world, "room.center", new[] { 3, 4 }, out ch) == null && ch && world.Room.Center?.Equals(new Vec2(3f, 4f)) == true, "room.center");
            t.Check(Set(null, world, "room.size", new[] { 60, 30, 70 }, out ch) == null && ch && world.Room.Size.Equals(new Vec3(60f, 30f, 70f)), "room.size");
            t.Check(Set(null, world, "room.center", null, out ch) == null && ch && world.Room.Center == null, "room.center null: nobody placed it");
            t.Check(Set(null, world, "room.center", new[] { 1, 2, 3 }, out ch) != null, "room.center of three numbers");
            t.Check(Set(null, world, "room.size", new[] { 1, 2 }, out ch) != null, "room.size of two numbers");
        }

        static void Floor(TestRunner t)
        {
            var world = new SceneRagdollWorld();
            t.Check(Set(null, world, "floor.mode", "fixed", out bool ch) == null && ch && world.Floor.Mode == "fixed", "floor.mode");
            t.Check(Set(null, world, "floor.height", -1.5, out ch) == null && ch && world.Floor.Height == -1.5f, "floor.height");
            t.Check(Set(null, world, "floor.offset", 0.25, out ch) == null && ch && world.Floor.Offset == 0.25f, "floor.offset");
            t.Check(Set(null, world, "floor", new { mode = "feet", offset = 1, height = 2 }, out ch) == null && ch && world.Floor.Mode == "feet" && world.Floor.Offset == 1f && world.Floor.Height == 2f, "floor as an object");
            t.Check(Set(null, world, "floor", new { mode = "map", nope = 1 }, out ch) != null && !ch && world.Floor.Mode == "feet", "a member that is none leaves the floor as it was");
            t.Check(Set(null, world, "floor.mode", "nope", out ch) != null && !ch && world.Floor.Mode == "feet", "a mode that is none");
            t.Check(Set(null, world, "floor.height", "x", out ch) != null && !ch && world.Floor.Height == 2f, "text is no height, it stays");
            t.Check(Set(null, world, "floor", 3, out ch) != null, "a number is no floor");
        }

        static void Entries(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = St(s, id).Ragdoll.Clone();
            b.On = true;
            Entry(t, s, id, b, RagdollEditCommand.LabelRagdoll, "RE1: a build");
            b = St(s, id).Ragdoll.Clone();
            b.Holds.Add(new RagdollHold { Part = "hand_R", Rot = new Quat4(0f, 0f, 0.6f, 0.8f) });
            Entry(t, s, id, b, RagdollEditCommand.LabelHold, "RE1: a hold");
            b = St(s, id).Ragdoll.Clone();
            b.Ties.Add(new RagdollTie { A = "hand_L", B = "hand_R" });
            Entry(t, s, id, b, RagdollEditCommand.LabelTie, "RE1: a tie");
            b = St(s, id).Ragdoll.Clone();
            b.Pins.Add(new RagdollPin { Id = b.NextPinId(), Part = "hand_R", At = new Vec3(1f, 2f, 3f) });
            Entry(t, s, id, b, RagdollEditCommand.LabelPin, "RE1: a pin");
            b = St(s, id).Ragdoll.Clone();
            b.Pins.Clear();
            Entry(t, s, id, b, RagdollEditCommand.LabelUnpin, "RE1: an unpin");
            b = St(s, id).Ragdoll.Clone();
            t.Check(RagdollPreset.ApplyTo(b.Values, RagdollPreset.Limp), "a preset");
            Entry(t, s, id, b, RagdollEditCommand.LabelSettings, "RE1: a preset");
            b = St(s, id).Ragdoll.Clone();
            b.Release();
            Entry(t, s, id, b, RagdollEditCommand.LabelRelease, "RE1: a release");
            t.Check(!St(s, id).Ragdoll.On && St(s, id).Ragdoll.Holds.Count == 0 && St(s, id).Ragdoll.Ties.Count == 0 && St(s, id).Ragdoll.Values.Muscle == 0f, "RE1: a release leaves the values");

            int n = s.Commands.UndoList.Count;
            var w = new SceneRagdollWorld();
            t.Check(Set(null, w, "gravity", 80, out _) == null, "a value");
            Kit.Ok(t, EditWorld(s, w, RagdollWorldEditCommand.LabelSettings), "RE1: the scene's settings");
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == "Ragdoll settings", "RE1: one entry");
            w = s.Model.Ragdoll.Clone();
            w.Pushers.Add(new RagdollPusher { Id = w.NextPusherId(), Key = id });
            Kit.Ok(t, EditWorld(s, w, RagdollWorldEditCommand.LabelPusher), "RE1: a pusher");
            t.Check(s.Commands.UndoList[0].Label == "Pusher", "RE1: its label");
            w = s.Model.Ragdoll.Clone();
            w.Obstacles.Add(id);
            Kit.Ok(t, EditWorld(s, w, RagdollWorldEditCommand.LabelObstacle), "RE1: an obstacle");
            t.Check(s.Commands.UndoList[0].Label == "Obstacle" && s.Commands.UndoList.Count == n + 3, "RE1: its label, one entry each");
            for (int i = 0; i < 3; i++) Kit.Ok(t, s.Commands.Undo(), "RE1: undo");
            t.Check(s.Model.Ragdoll == null, "RE1: undone to no block");
        }

        static void Merge(TestRunner t)
        {
            var s = NewSession(t, out string id);
            int n = s.Commands.UndoList.Count;
            string before = St(s, id).Ragdoll.ToJson();
            foreach (float v in new[] { 21f, 22f, 23f })
            {
                var b = St(s, id).Ragdoll.Clone();
                t.Check(Set(b, null, "muscle", v, out bool ch) == null && ch, "a step");
                Kit.Ok(t, Edit(s, id, b, RagdollEditCommand.LabelSettings, "drag"), "RE3: a step of a drag");
            }
            t.Equal(n + 1, s.Commands.UndoList.Count, "RE3: three steps of a key are one entry");
            t.Check(St(s, id).Ragdoll.Values.Muscle == 23f, "RE3: the last value");
            Kit.Ok(t, s.Commands.Undo(), "RE3: undo");
            t.Equal(before, St(s, id).Ragdoll.ToJson(), "RE3: one undo takes it all");
            Kit.Ok(t, s.Commands.Redo(), "RE3: redo");
            t.Check(St(s, id).Ragdoll.Values.Muscle == 23f, "RE3: redo gives the last value");
            foreach (float v in new[] { 90f, 80f })
            {
                var w = s.Model.Ragdoll?.Clone() ?? new SceneRagdollWorld();
                t.Check(Set(null, w, "gravity", v, out bool ch) == null && ch, "a step");
                Kit.Ok(t, EditWorld(s, w, RagdollWorldEditCommand.LabelSettings, "world"), "RE3: a step of a drag of the scene's value");
            }
            t.Equal(n + 2, s.Commands.UndoList.Count, "RE3: one entry for the scene's block");
            Kit.Ok(t, s.Commands.Undo(), "RE3: undo");
            t.Check(s.Model.Ragdoll == null, "RE3: back to no block");
        }

        static void NoHistory(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = St(s, id).Ragdoll.Clone();
            b.On = true;
            Kit.Ok(t, Edit(s, id, b, RagdollEditCommand.LabelRagdoll), "RE4: something on the history");
            int n = s.Commands.UndoList.Count;
            string top = s.Commands.UndoList[0].Label;
            b = St(s, id).Ragdoll.Clone();
            b.Ties.Add(new RagdollTie { A = "hand_L", B = "hand_R" });
            Kit.Ok(t, Edit(s, id, b, RagdollEditCommand.LabelTie, null, HistoryMode.None), "RE4: an edit with no entry");
            var w = new SceneRagdollWorld();
            w.Pushers.Add(new RagdollPusher { Id = w.NextPusherId(), Key = id });
            Kit.Ok(t, EditWorld(s, w, RagdollWorldEditCommand.LabelPusher, null, HistoryMode.None), "RE4: an edit of the scene's block with no entry");
            t.Check(s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == top, "RE4: the history kept");
            t.Check(St(s, id).Ragdoll.Ties.Count == 1 && s.Model.Ragdoll?.Pushers.Count == 1, "RE4: both applied");
        }

        static void UndoOfBuildAndRelease(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = St(s, id).Ragdoll.Clone();
            b.On = true;
            Kit.Ok(t, Edit(s, id, b, RagdollEditCommand.LabelRagdoll), "RE5: a build");
            Kit.Ok(t, s.Commands.Undo(), "RE5: its undo");
            t.Check(St(s, id).Ragdoll.IsEmpty, "RE5: an undo of a build leaves no block");
            Kit.Ok(t, s.Commands.Redo(), "RE5: its redo");
            t.Check(St(s, id).Ragdoll.On, "RE5: a redo sets it on");
            // the capture writes the pose silently, then the release replaces the block
            s.WriteSilently(ChangeOrigin.Capture, w => w.Set(id, "chara.ragdoll.pose", new RagdollPose { Hips = new Vec3(1f, 2f, 3f), Parts = PoseOf() }));
            string held = St(s, id).Ragdoll.ToJson();
            b = St(s, id).Ragdoll.Clone();
            b.Release();
            Kit.Ok(t, Edit(s, id, b, RagdollEditCommand.LabelRelease), "RE5: a release");
            t.Check(St(s, id).Ragdoll.Pose.IsEmpty && !St(s, id).Ragdoll.On, "RE5: the pose goes with the body");
            Kit.Ok(t, s.Commands.Undo(), "RE5: its undo");
            t.Equal(held, St(s, id).Ragdoll.ToJson(), "RE5: an undo of a release brings the pose back");
            t.Check(St(s, id).Ragdoll.On && !St(s, id).Ragdoll.Pose.IsEmpty, "RE5: on, with its pose");
        }

        static SortedDictionary<string, Quat4> PoseOf()
        {
            var d = new SortedDictionary<string, Quat4>(System.StringComparer.Ordinal);
            d["pelvis"] = new Quat4(0f, 0.6f, 0f, 0.8f);
            return d;
        }
    }
}
