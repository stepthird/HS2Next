// The edits of a character's wet block on a session, as the operations layer makes them: WetEdits on a copy, then one WetEditCommand. WE1 add, the names and seeds of new stains;
// WE2 remove, a stain's puddles with it, who is current then; WE3 members by path and by the names scripts use, every path written and read back, refusals, a drag merged into
// one entry, `undo: false`; WE4 keep; WE5 start and full; WE6 dry, a kept stain and frozen puddles staying; each with undo and redo to the byte; a duplicated node's block.
// Synthetic ids and names only.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes.Wet;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_WetOps
    {
        static CharaNodeState St(SceneSession s, string id) => (CharaNodeState)s.Model.Find(id).State;

        static SceneSession NewSession(TestRunner t, out string chara)
        {
            var s = Kit.Session();
            chara = s.AddChara(new CharaAppearance { Stored = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone() }).NewIds[0];
            return s;
        }

        static CommandResult Run(SceneSession s, string id, CharaWet after, string label, string key = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(new WetEditCommand(id, after, label), new ExecuteOptions { MergeKey = key, Record = mode });

        // One edit of the block: one entry with its label, undo and redo to the byte.
        static void Entry(TestRunner t, SceneSession s, string id, CharaWet after, string label, string what)
        {
            string before = St(s, id).Wet.ToJson();
            int n = s.Commands.UndoList.Count;
            Kit.Ok(t, Run(s, id, after, label), what);
            string done = St(s, id).Wet.ToJson();
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == label, what + ": one entry «" + label + "»");
            Kit.Ok(t, s.Commands.Undo(), what + ": undo");
            t.Equal(before, St(s, id).Wet.ToJson(), what + ": undo restores the block to the byte");
            Kit.Ok(t, s.Commands.Redo(), what + ": redo");
            t.Equal(done, St(s, id).Wet.ToJson(), what + ": redo restores it");
        }

        static CharaWet Two(out WetStain first, out WetStain second)
        {
            var b = new CharaWet();
            first = WetEdits.NewStain(b, "water", WetPreset.Water);
            WetEdits.Add(b, first);
            second = WetEdits.NewStain(b, "blood", WetPreset.Blood);
            WetEdits.Add(b, second, true);
            return b;
        }

        static WetMapEntry Map(string res) => new WetMapEntry { Renderer = "clothes:1/o_bot_a", Slot = 0, Res = res, Reach = 2.9f, Mesh = "o_bot_a|4|00000000" };

        public static void Run(TestRunner t)
        {
            Add(t);
            Remove(t);
            Members(t);
            Refusals(t);
            Drag(t);
            NoHistory(t);
            Keep(t);
            Start(t);
            Dry(t);
            Duplicate(t);
        }

        // ---------- WE1 ----------

        static void Add(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = St(s, id).Wet.Clone();
            var w = WetEdits.NewStain(b, "water", WetPreset.Water);
            t.Check(w.T == 0f && !w.Running && !w.Kept && w.Maps.Count == 0, "WE1: a new stain is dry, stopped, not kept, with no map");
            t.Equal(WetOriginValues.ModeDefault, w.Origin.Mode, "WE1: with the default origin");
            t.Equal(WetPreset.Water, w.Preset, "WE1: its preset");
            t.Check(w.Id == "s1" && w.Seed == 1, "WE1: the first stain's id and seed");
            t.Check(WetEdits.NewStain(b, "x", "wine") == null, "WE1: an unknown preset makes none");
            WetEdits.Add(b, w);
            t.Equal(w.Id, b.Current, "WE1: the first stain becomes current");
            Entry(t, s, id, b, WetEditCommand.LabelAdd, "WE1: add");

            b = St(s, id).Wet.Clone();
            var blood = WetEdits.NewStain(b, WetEdits.UniqueName(b, "blood"), WetPreset.Blood);
            t.Check(blood.Id == "s2" && blood.Seed == 2, "WE1: the next id and seed");
            WetEdits.Add(b, blood);
            t.Equal("s1", b.Current, "WE1: a later stain does not take over the current one unasked");
            WetEdits.Add(b, WetEdits.NewStain(b, WetEdits.UniqueName(b, "blood"), WetPreset.Blood), true);
            t.Equal("s3", b.Current, "WE1: asked, it does");
            t.Equal("blood 2", b.Stains[2].Name, "WE1: a taken name gets a number");
            t.Equal("blood 3", WetEdits.UniqueName(b, "blood"), "WE1: and the next one the next number");
            t.Equal(WetEdits.DefaultName, WetEdits.UniqueName(b, ""), "WE1: no name is the default name");
            string longName = new string('n', WetLimits.MaxName + 5);
            t.Equal(WetLimits.MaxName, WetEdits.UniqueName(b, longName).Length, "WE1: a long name is cut to the limit");
            b.Stains[0].Name = new string('n', WetLimits.MaxName);
            t.Equal(WetLimits.MaxName, WetEdits.UniqueName(b, longName).Length, "WE1: with its number too");
            t.Check(WetEdits.UniqueName(b, longName).EndsWith(" 2", StringComparison.Ordinal), "WE1: the number stays");

            // a replaced stain keeps its id, seed and place; its puddles go
            b = St(s, id).Wet.Clone();
            b.Puddles.Add(new WetPuddleEntry { Stain = "s1", Area = 1f });
            var fresh = WetEdits.NewStain(b, "water", WetPreset.Blood);
            WetEdits.Replace(b, b.Stains[0], fresh);
            t.Check(b.Stains[0] == fresh && fresh.Id == "s1" && fresh.Seed == 1 && b.Puddles.Count == 0, "WE1: a replaced stain keeps id, seed and place, and its puddles go");
        }

        // ---------- WE2 ----------

        static void Remove(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = Two(out var first, out var second);
            b.Puddles.Add(new WetPuddleEntry { Stain = first.Id, Area = 1f });
            b.Puddles.Add(new WetPuddleEntry { Stain = second.Id, Area = 2f });
            Kit.Ok(t, Run(s, id, b, WetEditCommand.LabelAdd), "WE2: two stains with puddles");

            b = St(s, id).Wet.Clone();
            WetEdits.Remove(b, b.Find(second.Id));
            t.Check(b.Stains.Count == 1 && b.Puddles.Count == 1 && b.Puddles[0].Stain == first.Id, "WE2: a stain's puddles go with it, others stay");
            t.Equal(first.Id, b.Current, "WE2: the current stain was it: the last one left is current");
            Entry(t, s, id, b, WetEditCommand.LabelRemove, "WE2: remove");

            b = St(s, id).Wet.Clone();
            WetEdits.Remove(b, b.Find(first.Id));
            t.Check(b.IsEmpty && b.Current == "", "WE2: the last stain gone, the block is empty and nothing is current");
            Entry(t, s, id, b, WetEditCommand.LabelRemove, "WE2: remove the last");
            t.Check(St(s, id).Wet.IsEmpty, "WE2: the node's block is empty");

            // a kept stain is never made current again by a removal
            var k = Two(out first, out second);
            first.Kept = true;
            WetEdits.Remove(k, k.Find(second.Id));
            t.Equal("", k.Current, "WE2: with only kept stains left nothing is current");
            k = Two(out first, out second);
            k.Current = first.Id;
            WetEdits.Remove(k, second);
            t.Equal(first.Id, k.Current, "WE2: removing another stain leaves the current one");

            // the order
            k = Two(out first, out second);
            t.Check(WetEdits.Move(k, second, 0) && k.Stains[0] == second, "WE2: a stain moves in the order");
            t.Check(!WetEdits.Move(k, second, 0) && !WetEdits.Move(k, second, 2) && !WetEdits.Move(k, second, -1), "WE2: its own place and a place outside are no move");
        }

        // ---------- WE3 ----------

        static object Sample(string path)
        {
            switch (WetEdits.KindOf(path))
            {
                case WetValueKind.Number: return 0.625f;
                case WetValueKind.Whole: return 7;
                case WetValueKind.Switch: return !(bool)WetEdits.Default(path);
                case WetValueKind.Vector: return new Vec3(0.25f, 0.5f, 0.75f);
                case WetValueKind.Colour: return new Color4(0.25f, 0.5f, 0.75f, 0.125f);
                default:
                    switch (path)
                    {
                        case "name": return "renamed";
                        case "preset": return WetPreset.Blood;
                        case "origin.mode": return WetOriginValues.ModeBone;
                        case "origin.bone": return "cf_J_Hips";
                        default: return WetFloorValues.ModeFeet;
                    }
            }
        }

        static void Members(TestRunner t)
        {
            t.Check(WetEdits.Paths.Count == 52 && WetEdits.Paths.Distinct().Count() == 52, "WE3: 52 distinct paths (" + WetEdits.Paths.Count + ")");
            var none = new WetTargetSwitches { Skin = false, Hair = false };
            foreach (var (name, on) in new (string, WetTargetSwitches)[]
            {
                ("skin", new WetTargetSwitches { Hair = false }), ("hair", new WetTargetSwitches { Skin = false }),
                ("accessories", new WetTargetSwitches { Skin = false, Hair = false, Accessories = true }),
                ("shoes", new WetTargetSwitches { Skin = false, Hair = false, Shoes = true }),
                ("gloves", new WetTargetSwitches { Skin = false, Hair = false, Gloves = true }),
            })
                t.Check(on.Widens(none) && !none.Widens(on) && !on.Widens(on), "WE3: " + name + " on widens the reach, off and unchanged do not");
            var pairs = new Dictionary<string, string>
            {
                ["maxRadius"] = "shape.radius", ["spreadTime"] = "shape.spread", ["dripTail"] = "drips.tail", ["dripRate"] = "drips.rate", ["puddleGrowth"] = "drips.growth", ["puddleScale"] = "puddle.scale",
            };
            foreach (var kv in pairs) t.Equal(kv.Value, WetEdits.PathOf(kv.Key), "WE3: " + kv.Key + " stands for " + kv.Value);
            t.Equal(pairs.Count, WetEdits.ScriptNames.Count, "WE3: the names scripts use");
            t.Equal("look.amount", WetEdits.PathOf("look.amount"), "WE3: a path stands for itself");
            t.Check(WetEdits.PathOf("look.nothing") == null && WetEdits.PathOf(null) == null, "WE3: an unknown name has no path");
            foreach (string n in new[] { "t", "running", "kept", "id", "maps", "current", "puddles", "stains" })
                t.Check(WetEdits.PathOf(n) == null && WetEdits.IsNoMember(n), "WE3: " + n + " is set by no value");

            foreach (string path in WetEdits.Paths)
            {
                var b = new CharaWet();
                var st = WetEdits.NewStain(b, "water", WetPreset.Water);
                WetEdits.Add(b, st);
                string before = b.ToJson();
                object v = Sample(path);
                string wrong = WetEdits.Set(b, st, path, v, out bool changed);
                t.Check(wrong == null && changed, "WE3: " + path + " is written (" + wrong + ")");
                if (path != "preset") t.Check(Equals(v, WetEdits.Get(b, st, path)), "WE3: " + path + " reads back");
                t.Check(b.ToJson() != before, "WE3: " + path + " reaches the block");
                wrong = WetEdits.Set(b, st, path, v, out changed);
                t.Check(wrong == null && !changed, "WE3: " + path + ": the same value is no change");
                var back = CharaWet.Parse(b.ToJson());
                if (path != "preset") t.Check(Equals(v, WetEdits.Get(back, back.Stains[0], path)), "WE3: " + path + " survives the text");
            }

            // the defaults are the block's
            t.Equal(2.2f, (float)WetEdits.Default("shape.radius"), "WE3: a default: shape.radius");
            t.Equal(0.85f, (float)WetEdits.Default("mirror.strength"), "WE3: a default: mirror.strength");
            t.Check((bool)WetEdits.Default("film.on"), "WE3: a default: film.on");

            // number forms, vectors and colours as arrays, a value outside its range is kept
            var x = new CharaWet();
            var y = WetEdits.NewStain(x, "water", WetPreset.Water);
            WetEdits.Add(x, y);
            t.Check(WetEdits.Set(x, y, "shape.up", 2, out _) == null && y.Shape.Up == 2f, "WE3: an int is a number");
            t.Check(WetEdits.Set(x, y, "shape.up", 2.5d, out _) == null && y.Shape.Up == 2.5f, "WE3: a double");
            t.Check(WetEdits.Set(x, y, "shape.up", 3L, out _) == null && y.Shape.Up == 3f, "WE3: a long");
            t.Check(WetEdits.Set(x, y, "seed", 9L, out _) == null && y.Seed == 9, "WE3: a whole number as a long");
            t.Check(WetEdits.Set(x, y, "look.tint", new List<object> { 1L, 0.5d, 0.25f }, out _) == null && y.Look.Tint.Equals(new Vec3(1f, 0.5f, 0.25f)), "WE3: a vector as an array");
            t.Check(WetEdits.Set(x, y, "puddle.color", new double[] { 0.1, 0.2, 0.3, 0.4 }, out _) == null && y.Puddle.Color.Equals(new Color4(0.1f, 0.2f, 0.3f, 0.4f)), "WE3: a colour as an array");
            t.Check(WetEdits.Set(x, y, "shape.radius", 500f, out _) == null && y.Shape.Radius == 500f && y.Shape.Applied().Radius == 10f, "WE3: a value outside its range is stored as given and clamped when applied");
            t.Check(WetEdits.Set(x, y, "origin.bone", "cf_J_Spine03", out _) == null && y.Origin.Bone == "cf_J_Spine03", "WE3: a bone address");
            t.Check(WetEdits.Set(x, y, "preset", WetPreset.Blood, out bool preset) == null && preset && y.Preset == WetPreset.Blood && y.Look.Amount == 0.85f, "WE3: a preset writes its values");
            t.Equal(1.15f, new WetLookValues().Saturate, "WE3: a default stays what it was");

            // what a path is
            t.Check(WetEdits.IsFloor("floor.mode") && !WetEdits.IsFloor("film.on"), "WE3: IsFloor");
            t.Check(WetEdits.IsOrigin("origin.down") && !WetEdits.IsOrigin("shape.up"), "WE3: IsOrigin");
            t.Check(WetEdits.IsShape("shape.up") && WetEdits.IsShape("origin.out") && !WetEdits.IsShape("look.amount"), "WE3: IsShape");
            t.Check(WetEdits.OnStain("look.amount") && WetEdits.OnStain("hold") && !WetEdits.OnStain("film.gloss") && !WetEdits.OnStain("floor.offset"), "WE3: OnStain");
        }

        static void Refusals(TestRunner t)
        {
            var b = Two(out var w, out var blood);
            string before = b.ToJson();
            void No(string path, object value, string what)
            {
                string wrong = WetEdits.Set(b, w, path, value, out bool changed);
                t.Check(wrong != null && !changed, "WE3: refused: " + what + " (" + wrong + ")");
            }
            No("nothing.here", 1f, "an unknown path");
            No("shape.up", "tall", "a text for a number");
            No("shape.up", true, "a switch for a number");
            No("shape.up", null, "null for a number");
            No("shape.up", float.NaN, "NaN");
            No("shape.up", double.PositiveInfinity, "infinity");
            No("seed", 1.5, "a fraction for a whole number");
            No("seed", 1L << 40, "a whole number too large");
            No("drips.on", 1, "a number for a switch");
            No("name", 7, "a number for a text");
            No("name", "", "an empty name");
            No("name", "blood", "another stain's name");
            No("preset", "wine", "an unknown preset");
            No("origin.mode", "somewhere", "an unknown origin mode");
            No("origin.bone", "a/b", "a text that is no address");
            No("floor.mode", "ceiling", "an unknown floor mode");
            No("look.tint", new[] { 1f, 2f }, "a vector of two");
            No("look.tint", new object[] { 1f, "a", 3f }, "a vector with a text");
            No("puddle.color", new[] { 1f, 2f, 3f }, "a colour of three");
            t.Equal(before, b.ToJson(), "WE3: a refusal leaves the block as it was");
            t.Check(WetEdits.Set(b, w, "name", "water", out bool same) == null && !same, "WE3: a stain's own name is no refusal");
            t.Check(blood.Name == "blood", "WE3: the other stain is untouched");
        }

        static void Drag(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var start = Two(out _, out _);
            Kit.Ok(t, Run(s, id, start, WetEditCommand.LabelAdd), "WE3: a block");
            string before = St(s, id).Wet.ToJson();
            int n = s.Commands.UndoList.Count;
            foreach (float v in new[] { 0.5f, 0.6f, 0.7f })
            {
                var b = St(s, id).Wet.Clone();
                t.Check(WetEdits.Set(b, b.CurrentStain, "look.amount", v, out _) == null, "WE3: a step of a drag");
                Kit.Ok(t, Run(s, id, b, WetEditCommand.LabelEdit, "wet:look.amount"), "WE3: its edit");
            }
            t.Equal(n + 1, s.Commands.UndoList.Count, "WE3: a drag is one entry");
            t.Equal(0.7f, St(s, id).Wet.CurrentStain.Look.Amount, "WE3: the last value stands");
            Kit.Ok(t, s.Commands.Undo(), "WE3: undo of the drag");
            t.Equal(before, St(s, id).Wet.ToJson(), "WE3: one undo takes the whole drag");
            Kit.Ok(t, s.Commands.Redo(), "WE3: redo of the drag");
            t.Equal(0.7f, St(s, id).Wet.CurrentStain.Look.Amount, "WE3: redo brings the last value");
            var c = St(s, id).Wet.Clone();
            WetEdits.Set(c, c.CurrentStain, "look.skin", 0.5f, out _);
            Kit.Ok(t, Run(s, id, c, WetEditCommand.LabelEdit, "wet:look.skin"), "WE3: another value");
            t.Equal(n + 2, s.Commands.UndoList.Count, "WE3: another key is another entry");
        }

        static void NoHistory(TestRunner t)
        {
            var s = NewSession(t, out string id);
            Kit.Ok(t, Run(s, id, Two(out _, out _), WetEditCommand.LabelAdd), "WE3: a block");
            int undo = s.Commands.UndoList.Count;
            string label = s.Commands.UndoList[0].Label;
            void Timed(Action<CharaWet> edit, string what)
            {
                var b = St(s, id).Wet.Clone();
                edit(b);
                Kit.Ok(t, Run(s, id, b, WetEditCommand.LabelEdit, null, HistoryMode.None), what);
                t.Check(s.Commands.UndoList.Count == undo && s.Commands.UndoList[0].Label == label && s.Commands.CanUndo, what + ": undo false leaves the history alone");
            }
            Timed(b => WetEdits.Start(b.CurrentStain), "WE3: start");
            t.Check(St(s, id).Wet.CurrentStain.Running, "WE3: and it is done");
            Timed(b => WetEdits.Dry(b, null, true), "WE3: dry");
            Timed(b => WetEdits.Set(b, b.CurrentStain, "look.amount", 0.5f, out _), "WE3: a value");
            Timed(b => WetEdits.Keep(b, b.CurrentStain), "WE3: keep");
            t.Equal(3, St(s, id).Wet.Stains.Count, "WE3: keep with no entry made its stain");
        }

        // ---------- WE4 ----------

        static void Keep(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = Two(out var w, out var blood);
            blood.T = 3.25f;
            blood.Running = true;
            blood.Maps.Add(Map("res/" + new string('a', 64) + ".png"));
            blood.Origin = new WetOriginValues { Mode = WetOriginValues.ModeBone, Bone = "cf_J_Hips", Local = new Vec3(0f, 0.1f, 0.2f) };
            b.Puddles.Add(new WetPuddleEntry { Stain = blood.Id, Area = 1f });
            b.Puddles.Add(new WetPuddleEntry { Stain = w.Id, Area = 2f });
            Kit.Ok(t, Run(s, id, b, WetEditCommand.LabelAdd), "WE4: a block");

            b = St(s, id).Wet.Clone();
            var kept = b.CurrentStain;
            var next = WetEdits.Keep(b, kept);
            t.Check(kept.Kept && !kept.Running && kept.T == 3.25f, "WE4: the clock stops where it is");
            t.Check(!next.Kept && !next.Running && next.T == 0f, "WE4: the new stain starts dry");
            t.Equal(next.Id, b.Current, "WE4: and is current");
            t.Check(b.Stains[b.Stains.Count - 1] == next, "WE4: on top");
            t.Check(next.Id != kept.Id && next.Seed != kept.Seed, "WE4: with its own id and seed");
            t.Equal("blood 2", next.Name, "WE4: and a name of its own");
            t.Check(next.Origin.Bone == "cf_J_Hips" && next.Origin.Local.Equals(kept.Origin.Local) && next.Look.Amount == kept.Look.Amount && next.Shape.Radius == kept.Shape.Radius, "WE4: the same origin and values");
            t.Check(next.Maps.Count == 1 && next.Maps[0].Res == kept.Maps[0].Res, "WE4: the same maps, so no bake");
            t.Check(next != kept && next.Look != kept.Look, "WE4: nothing shared");
            var mine = b.Puddles.First(p => p.Stain == kept.Id);
            t.Check(mine.Color != null && mine.Color.Value.Equals(kept.Puddle.Color), "WE4: its puddle keeps the colour");
            t.Check(b.Puddles.First(p => p.Stain == w.Id).Color == null, "WE4: another stain's puddle does not");
            Entry(t, s, id, b, WetEditCommand.LabelKeep, "WE4: keep");

            // the limit, as a command sees it
            var full = new CharaWet();
            for (int i = 0; i < WetLimits.MaxStains; i++) WetEdits.Add(full, WetEdits.NewStain(full, "n" + i, WetPreset.Water));
            Kit.Ok(t, Run(s, id, full, WetEditCommand.LabelAdd), "WE4: eight stains");
            var more = St(s, id).Wet.Clone();
            WetEdits.Keep(more, more.CurrentStain);
            var r = Run(s, id, more, WetEditCommand.LabelKeep);
            t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs && r.Message.Contains(WetFindings.Limit), "WE4: keep at the limit is bad_args with wet.limit in the message (" + r.Message + ")");
            t.Equal(8, St(s, id).Wet.Stains.Count, "WE4: and changes nothing");
        }

        // ---------- WE5 ----------

        static void Start(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = Two(out _, out var blood);
            blood.T = 2f;
            Kit.Ok(t, Run(s, id, b, WetEditCommand.LabelAdd), "WE5: a block");

            b = St(s, id).Wet.Clone();
            WetEdits.Start(b.CurrentStain);
            t.Check(b.CurrentStain.T == 0f && b.CurrentStain.Running && !b.CurrentStain.Loop, "WE5: from 0, running, the loop as it was");
            Entry(t, s, id, b, WetEditCommand.LabelStart, "WE5: start");
            t.Check(St(s, id).Wet.CurrentStain.Running, "WE5: stands after redo");

            b = St(s, id).Wet.Clone();
            WetEdits.Start(b.CurrentStain, true);
            t.Check(b.CurrentStain.Loop, "WE5: a loop given is set");
            WetEdits.Start(b.CurrentStain);
            t.Check(b.CurrentStain.Loop, "WE5: none given keeps it");
            WetEdits.Start(b.CurrentStain, false);
            t.Check(!b.CurrentStain.Loop, "WE5: false ends it");

            b = St(s, id).Wet.Clone();
            b.CurrentStain.Shape.Spread = 4f;
            WetEdits.Full(b.CurrentStain);
            t.Check(b.CurrentStain.T == 4f && !b.CurrentStain.Running, "WE5: full: at the end of the spread, not running");
            b.CurrentStain.Shape.Spread = 500f;
            WetEdits.Full(b.CurrentStain);
            t.Equal(120f, b.CurrentStain.T, "WE5: full reads the spread as it is applied");
            Entry(t, s, id, b, WetEditCommand.LabelEdit, "WE5: full");
        }

        // ---------- WE6 ----------

        static void Dry(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = Two(out var w, out var blood);
            var k = WetEdits.NewStain(b, "kept", WetPreset.Blood);
            WetEdits.Add(b, k);
            k.Kept = true;
            k.T = 5f;
            w.T = 1f;
            w.Running = true;
            blood.T = 2f;
            b.Puddles.Add(new WetPuddleEntry { Stain = w.Id, Area = 1f });
            b.Puddles.Add(new WetPuddleEntry { Stain = blood.Id, Area = 2f });
            b.Puddles.Add(new WetPuddleEntry { Stain = k.Id, Area = 3f, Color = new Color4(0.1f, 0.2f, 0.3f, 0.4f) });
            Kit.Ok(t, Run(s, id, b, WetEditCommand.LabelAdd), "WE6: a block");

            b = St(s, id).Wet.Clone();
            WetEdits.Dry(b, null, false);
            t.Check(b.Stains[0].T == 0f && !b.Stains[0].Running && b.Stains[1].T == 0f, "WE6: every stain that is not kept is dry");
            t.Check(b.Stains[2].T == 5f, "WE6: the kept one stands");
            t.Equal(3, b.Puddles.Count, "WE6: without puddles they stay");
            Entry(t, s, id, b, WetEditCommand.LabelDry, "WE6: dry");
            Kit.Ok(t, s.Commands.Undo(), "WE6: back");

            b = St(s, id).Wet.Clone();
            t.Equal(1f, b.Stains[0].T, "WE6: undo brings the dried stain back");
            WetEdits.Dry(b, null, true);
            t.Check(b.Puddles.Count == 1 && b.Puddles[0].Stain == k.Id, "WE6: with puddles those go and the kept one's stays");
            Entry(t, s, id, b, WetEditCommand.LabelDry, "WE6: dry with puddles");
            Kit.Ok(t, s.Commands.Undo(), "WE6: back");

            b = St(s, id).Wet.Clone();
            WetEdits.Dry(b, b.Find(w.Id), true);
            t.Check(b.Stains[0].T == 0f && b.Stains[1].T == 2f && b.Puddles.Count == 2 && b.Puddles.All(p => p.Stain != w.Id), "WE6: one stain alone, and only its puddles");

            b = St(s, id).Wet.Clone();
            t.Equal(2, WetEdits.ClearPuddles(b), "WE6: clear puddles takes those not frozen");
            t.Check(b.Puddles.Count == 1 && b.Puddles[0].Color != null, "WE6: the frozen one stays");
            t.Check(b.Stains[0].T == 1f, "WE6: and no stain dries");
            Entry(t, s, id, b, WetEditCommand.LabelClearPuddles, "WE6: clear puddles");
        }

        // ---------- a duplicated node ----------

        static void Duplicate(TestRunner t)
        {
            var s = NewSession(t, out string id);
            var b = Two(out _, out var blood);
            blood.T = 2f;
            blood.Running = true;
            blood.Maps.Add(Map("res/" + new string('b', 64) + ".png"));
            b.Puddles.Add(new WetPuddleEntry { Stain = blood.Id, Area = 2f });
            Kit.Ok(t, Run(s, id, b, WetEditCommand.LabelAdd), "duplicate: a block");
            string before = St(s, id).Wet.ToJson();
            var r = s.Duplicate(new[] { id });
            Kit.Ok(t, r, "duplicate: a stained character");
            var copy = St(s, r.NewIds[0]).Wet;
            t.Check(copy.Stains.Count == 2 && copy.Stains[1].Maps.Count == 1 && copy.Stains[1].Maps[0].Res == blood.Maps[0].Res, "duplicate: stains and maps come along, with the same pictures");
            t.Equal(2f, copy.Stains[1].T, "duplicate: the clock is kept");
            t.Check(!copy.Stains[1].Running, "duplicate: not running");
            t.Equal(0, copy.Puddles.Count, "duplicate: puddles stay behind");
            t.Equal(before, St(s, id).Wet.ToJson(), "duplicate: the source is untouched");
            t.Check(!ReferenceEquals(copy, St(s, id).Wet), "duplicate: nothing shared");
            Kit.Ok(t, s.Commands.Undo(), "duplicate: undo");
            t.Check(s.Model.Find(r.NewIds[0]) == null, "duplicate: one undo takes the copy");
        }
    }
}
