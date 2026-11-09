// A character's wet block on the bus (WetEditCommand) and the presets. WE0 each edit is one history entry that undo and redo restore to the byte, with one change record
// NodeChanged(id, "chara.wet") per apply and revert; a block in the model is replaced, never changed in place, and shares nothing with the caller or the history; a drag merges
// by key, an edit of another node does not; with `undo: false` nothing is recorded and the history stays; refusals: no node, not a character, a limit, an id; a file's excess may
// stay or shrink; the field paths a capture writes reach the block silently, unrecorded; a saved session holds the block and a cleared one does not. WM6 every value of the
// presets. Synthetic ids and names only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_WetEdit
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

        static CharaWet Block(string name = "water", int stains = 1)
        {
            var b = new CharaWet();
            for (int i = 0; i < stains; i++)
            {
                var st = new WetStain { Id = b.NextId(), Name = name + i };
                WetPreset.ApplyTo(st, WetPreset.Water);
                b.Stains.Add(st);
            }
            b.Current = b.Stains.Count > 0 ? b.Stains[0].Id : "";
            return b;
        }

        static CommandResult Run(SceneSession s, ISceneCommand c, string key = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(c, new ExecuteOptions { MergeKey = key, Record = mode });

        public static void Run(TestRunner t)
        {
            Steps(t);
            Copies(t);
            Merge(t);
            NoHistory(t);
            Batches(t);
            Refusals(t);
            Capture(t);
            Presets(t);
            Saved(t);
        }

        static void Steps(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            int n0 = s.Commands.UndoList.Count;
            string none = St(s, id).Wet.ToJson();
            var states = new List<string> { none };
            var labels = new List<string>();
            var rec = new EventRecorder(s);
            void Step(CharaWet after, string label)
            {
                rec.Clear();
                Kit.Ok(t, Run(s, new WetEditCommand(id, after, label)), "WE0: " + label);
                states.Add(St(s, id).Wet.ToJson());
                labels.Add(label);
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Kind == SceneChangeKind.NodeChanged && rec.Batches[0].Changes[0].NodeId == id &&
                        rec.Batches[0].Changes[0].Path == WetEditCommand.ChangePath, "WE0: " + label + " reports one NodeChanged(id, \"chara.wet\")");
                t.Equal(label, s.Commands.UndoList[0].Label, "WE0: its history label");
            }
            t.Equal("chara.wet", WetEditCommand.ChangePath, "WE0: the change path");
            var b = Block();
            Step(b, WetEditCommand.LabelAdd);
            b = St(s, id).Wet.Clone();
            b.Stains[0].Look.Amount = 0.5f;
            Step(b, WetEditCommand.LabelEdit);
            b = St(s, id).Wet.Clone();
            b.Stains[0].Running = true;
            Step(b, WetEditCommand.LabelStart);
            b = St(s, id).Wet.Clone();
            b.Stains[0].Running = false;
            b.Stains[0].T = 0f;
            Step(b, WetEditCommand.LabelDry);
            b = St(s, id).Wet.Clone();
            b.Puddles.Add(new WetPuddleEntry { Stain = "s1", Area = 1f });
            Step(b, WetEditCommand.LabelFloor);
            b = St(s, id).Wet.Clone();
            b.Puddles.Clear();
            Step(b, WetEditCommand.LabelClearPuddles);
            b = St(s, id).Wet.Clone();
            b.Stains[0].Kept = true;
            Step(b, WetEditCommand.LabelKeep);
            b = St(s, id).Wet.Clone();
            b.Stains[0].Origin = new WetOriginValues { Mode = WetOriginValues.ModeBone, Bone = "cf_J_Hips" };
            Step(b, WetEditCommand.LabelOrigin);
            Step(new CharaWet(), WetEditCommand.LabelRemove);
            t.Check(St(s, id).Wet.IsEmpty, "WE0: an empty block as the after empties the block");
            t.Equal(n0 + 9, s.Commands.UndoList.Count, "WE0: one entry per edit");

            for (int i = labels.Count - 1; i >= 0; i--)
            {
                rec.Clear();
                Kit.Ok(t, s.Commands.Undo(), "WE0: undo " + labels[i]);
                t.Equal(states[i], St(s, id).Wet.ToJson(), "WE0: undo " + labels[i] + " restores the block to the byte");
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Path == WetEditCommand.ChangePath, "WE0: undo " + labels[i] + " reports it");
            }
            for (int i = 0; i < labels.Count; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "WE0: redo " + labels[i]);
                t.Equal(states[i + 1], St(s, id).Wet.ToJson(), "WE0: redo " + labels[i]);
            }

            // Size of an entry, a no-op, the labels.
            var top = s.Commands.UndoList[0];
            long expect = WetEditCommand.BaseBytes + 2L * (states[labels.Count - 1].Length + states[labels.Count].Length);
            t.Check(top.Label == WetEditCommand.LabelRemove && top.EstimatedBytes == expect, "WE0: entry size = 256 + 2 × (before + after JSON lengths) (" + top.EstimatedBytes + ", expected " + expect + ")");
            int entries = s.Commands.UndoList.Count;
            Kit.NoOp(t, Run(s, new WetEditCommand(id, St(s, id).Wet)), "WE0: an edit to the same block");
            Kit.NoOp(t, Run(s, new WetEditCommand(id, null)), "WE0: null on an empty block");
            t.Equal(entries, s.Commands.UndoList.Count, "WE0: a no-op records nothing");
            t.Equal("Stain", new WetEditCommand(id, null).Label, "WE0: the default label");
            t.Equal("Stain", new WetEditCommand(id, null, null).Label, "WE0: a null label is the default");
            t.Equal("Add stain|Remove stain|Stain|Keep layer|Start stain|Dry stain|Clear puddles|Floor|Move origin", string.Join("|", WetEditCommand.LabelAdd, WetEditCommand.LabelRemove, WetEditCommand.LabelEdit, WetEditCommand.LabelKeep,
                WetEditCommand.LabelStart, WetEditCommand.LabelDry, WetEditCommand.LabelClearPuddles, WetEditCommand.LabelFloor, WetEditCommand.LabelOrigin), "WE0: the history labels");
        }

        static void Copies(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            var mine = Block();
            var cmd = new WetEditCommand(id, mine);
            mine.Stains[0].Name = "changed after";
            Kit.Ok(t, Run(s, cmd), "WE0: an edit");
            t.Equal("water0", St(s, id).Wet.Stains[0].Name, "WE0: the command took the block as it stood");
            t.Check(!ReferenceEquals(St(s, id).Wet, mine), "WE0: the caller's object is not the model's");
            var held = St(s, id).Wet;
            t.Equal(cmd.NodeId, id, "WE0: NodeId");
            var after = cmd.After;
            after.Stains[0].Name = "x";
            t.Equal("water0", cmd.After.Stains[0].Name, "WE0: After is a new copy each time");

            var next = held.Clone();
            next.Stains[0].Look.Amount = 0.5f;
            Kit.Ok(t, Run(s, new WetEditCommand(id, next)), "WE0: a second edit");
            t.Check(!ReferenceEquals(St(s, id).Wet, held) && held.Stains[0].Look.Amount == 0.3f, "WE0: an edit replaces the block, the old object stays as it was");
            var before = St(s, id).Wet;
            Kit.Ok(t, s.Commands.Undo(), "WE0: undo");
            t.Check(!ReferenceEquals(St(s, id).Wet, before) && St(s, id).Wet.Stains[0].Look.Amount == 0.3f, "WE0: an undo too");
            St(s, id).Wet.Stains[0].Look.Amount = 0.9f;   // a capture may write the model's object in place
            Kit.Ok(t, s.Commands.Redo(), "WE0: redo");
            Kit.Ok(t, s.Commands.Undo(), "WE0: undo");
            t.Equal(0.9f, St(s, id).Wet.Stains[0].Look.Amount, "WE0: an entry's before is what the model held at apply");
        }

        static void Merge(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            string second = AddChara(t, s);
            Kit.Ok(t, Run(s, new WetEditCommand(id, Block(), WetEditCommand.LabelAdd)), "WE0 merge: a stain");
            int n = s.Commands.UndoList.Count;
            string start = St(s, id).Wet.ToJson();
            s.Commands.MarkSaved();
            var rec = new EventRecorder(s);
            for (int i = 1; i <= 40; i++)
            {
                var b = St(s, id).Wet.Clone();
                b.Stains[0].Look.Amount = i * 0.01f;
                Kit.Ok(t, Run(s, new WetEditCommand(id, b), "amount:" + id), "WE0 merge: step " + i);
            }
            t.Equal(40, rec.Batches.Count, "WE0 merge: each step reports");
            t.Check(s.Commands.UndoList.Count == n + 1 && St(s, id).Wet.Stains[0].Look.Amount == 40 * 0.01f, "WE0 merge: a drag of forty steps is one entry");
            long size = s.Commands.UndoList[0].EstimatedBytes;
            t.Check(size > WetEditCommand.BaseBytes && size < WetEditCommand.BaseBytes + 2L * 2 * (start.Length + 16), "WE0 merge: the entry's size counts two values, not forty (" + size + ")");
            string last = St(s, id).Wet.ToJson();
            Kit.Ok(t, s.Commands.Undo(), "WE0 merge: undo");
            t.Equal(start, St(s, id).Wet.ToJson(), "WE0 merge: undo goes back before the drag");
            Kit.Ok(t, s.Commands.Redo(), "WE0 merge: redo");
            t.Equal(last, St(s, id).Wet.ToJson(), "WE0 merge: redo gives the drag's end");

            // Another key, another node, no key: new entries.
            n = s.Commands.UndoList.Count;
            var a = St(s, id).Wet.Clone();
            a.Stains[0].Look.Skin = 0.5f;
            Kit.Ok(t, Run(s, new WetEditCommand(id, a), "skin:" + id), "WE0 merge: another key");
            t.Equal(n + 1, s.Commands.UndoList.Count, "WE0 merge: another key is another entry");
            Kit.Ok(t, Run(s, new WetEditCommand(second, Block()), "skin:" + id), "WE0 merge: another node under the same key");
            t.Equal(n + 2, s.Commands.UndoList.Count, "WE0 merge: another node is another entry");
            t.Check(!St(s, second).Wet.IsEmpty, "WE0 merge: and it reached that node");
            Kit.Ok(t, s.Commands.Undo(), "WE0 merge: undo of the other node");
            t.Check(St(s, second).Wet.IsEmpty && St(s, id).Wet.Stains[0].Look.Skin == 0.5f, "WE0 merge: undoes that node alone");
            var other = new WetEditCommand(id, Block("z"));
            t.Check(!other.TryMerge(new WetEditCommand(second, Block())), "WE0 merge: TryMerge refuses another node");
            t.Check(!other.TryMerge(new SetPropertyCommand(id, "name", "x")), "WE0 merge: and another command");
        }

        static void NoHistory(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            Kit.Ok(t, Run(s, new WetEditCommand(id, Block(), WetEditCommand.LabelAdd)), "WE0 undo:false: a stain");
            int n = s.Commands.UndoList.Count;
            long version = s.Commands.Version;
            var b = St(s, id).Wet.Clone();
            b.Stains[0].Running = true;
            Kit.Ok(t, Run(s, new WetEditCommand(id, b, WetEditCommand.LabelStart), null, HistoryMode.None), "WE0 undo:false: a start");
            t.Check(St(s, id).Wet.Stains[0].Running, "WE0 undo:false: it applied");
            t.Check(s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == WetEditCommand.LabelAdd, "WE0 undo:false: nothing recorded, the history kept");
            t.Check(s.Commands.Version > version && s.Commands.IsDirty, "WE0 undo:false: the scene's version moves");
            Kit.Ok(t, s.Commands.Undo(), "WE0 undo:false: the entry before it can still be undone");
            t.Check(St(s, id).Wet.IsEmpty, "WE0 undo:false: the block as it was before that entry");
        }

        // A client batch: its edits are one entry; an unrecorded batch keeps the history; a cancelled one leaves the block as it was.
        static void Batches(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            Kit.Ok(t, Run(s, new WetEditCommand(id, Block(), WetEditCommand.LabelAdd)), "WE0 batch: a stain");
            string start = St(s, id).Wet.ToJson();
            int n = s.Commands.UndoList.Count;
            using (var tx = s.Commands.Begin("batch", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                var a = St(s, id).Wet.Clone();
                a.Stains[0].Look.Amount = 0.5f;
                Kit.Ok(t, tx.Execute(new WetEditCommand(id, a)), "WE0 batch: an edit inside");
                var b = St(s, id).Wet.Clone();
                b.Stains[0].Look.Skin = 0.5f;
                Kit.Ok(t, tx.Execute(new WetEditCommand(id, b)), "WE0 batch: another edit inside");
                Kit.Ok(t, tx.Commit(), "WE0 batch: commit");
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && St(s, id).Wet.Stains[0].Look.Amount == 0.5f && St(s, id).Wet.Stains[0].Look.Skin == 0.5f, "WE0 batch: both edits in one entry");
            Kit.Ok(t, s.Commands.Undo(), "WE0 batch: undo");
            t.Equal(start, St(s, id).Wet.ToJson(), "WE0 batch: one undo takes both back");
            Kit.Ok(t, s.Commands.Redo(), "WE0 batch: redo");
            string done = St(s, id).Wet.ToJson();

            n = s.Commands.UndoList.Count;
            using (var tx = s.Commands.Begin("quiet", ExecuteOptions.NotRecorded))
            {
                var a = St(s, id).Wet.Clone();
                a.Film.Gloss = 0.5f;
                Kit.Ok(t, tx.Execute(new WetEditCommand(id, a)), "WE0 batch: an edit in an unrecorded batch");
                Kit.Ok(t, tx.Commit(), "WE0 batch: commit the unrecorded batch");
            }
            t.Check(s.Commands.UndoList.Count == n && St(s, id).Wet.Film.Gloss == 0.5f, "WE0 batch: applied, nothing recorded, the history kept");

            string before = St(s, id).Wet.ToJson();
            using (var tx = s.Commands.Begin("cancelled", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                var a = St(s, id).Wet.Clone();
                a.Stains[0].Name = "changed";
                Kit.Ok(t, tx.Execute(new WetEditCommand(id, a)), "WE0 batch: an edit in a cancelled batch");
                tx.Cancel();
            }
            t.Equal(before, St(s, id).Wet.ToJson(), "WE0 batch: a cancelled batch leaves the block as it was");
            t.Check(done != before, "WE0 batch: (the unrecorded edit stands)");
        }

        static void Refusals(TestRunner t)
        {
            var s = NewSession(t, out string id, out string item);
            Kit.Fails(t, Run(s, new WetEditCommand(Id(0xeeee), Block())), CommandCodes.NotFound, "WE0: no such node");
            Kit.Fails(t, Run(s, new WetEditCommand(item, Block())), CommandCodes.WrongKind, "WE0: an item is no character");
            Kit.Fails(t, Run(s, new WetEditCommand(id, Block(stains: WetLimits.MaxStains + 1))), CommandCodes.BadArgs, "WE0: a ninth stain");
            var refused = Run(s, new WetEditCommand(id, Block(stains: WetLimits.MaxStains + 1)));
            t.Check(refused.Message.Contains(WetFindings.Limit), "WE0: the refusal names the limit (" + refused.Message + ")");
            t.Check(St(s, id).Wet.IsEmpty, "WE0: a refused edit changes nothing");
            Kit.Ok(t, Run(s, new WetEditCommand(id, Block(stains: WetLimits.MaxStains))), "WE0: eight stains");
            var puddles = Block();
            for (int i = 0; i <= WetLimits.MaxPuddles; i++) puddles.Puddles.Add(new WetPuddleEntry { Stain = "s1" });
            Kit.Fails(t, Run(s, new WetEditCommand(id, puddles)), CommandCodes.BadArgs, "WE0: a 31st puddle");
            var named = Block();
            named.Stains[0].Name = new string('n', WetLimits.MaxName + 1);
            Kit.Fails(t, Run(s, new WetEditCommand(id, named)), CommandCodes.BadArgs, "WE0: a name of 65 characters");
            named.Stains[0].Name = new string('n', WetLimits.MaxName);
            Kit.Ok(t, Run(s, new WetEditCommand(id, named)), "WE0: a name of 64 characters");

            var twice = Block(stains: 2);
            twice.Stains[1].Id = twice.Stains[0].Id;
            Kit.Fails(t, Run(s, new WetEditCommand(id, twice)), CommandCodes.BadArgs, "WE0: an id used twice");
            var noId = Block();
            noId.Stains[0].Id = "";
            Kit.Fails(t, Run(s, new WetEditCommand(id, noId)), CommandCodes.BadArgs, "WE0: a stain without an id");
            var nan = Block();
            nan.Stains[0].T = float.NaN;
            Kit.Fails(t, Run(s, new WetEditCommand(id, nan)), CommandCodes.BadArgs, "WE0: a clock that is not a number");
            var nul = Block();
            nul.Stains[0].Name = null;
            Kit.Fails(t, Run(s, new WetEditCommand(id, nul)), CommandCodes.BadArgs, "WE0: a null name");
            t.Check(nan.Stains[0].T != 0f, "WE0: (the caller's object is left as it was)");

            // A file's excess: the block may stay or shrink, and other values may change.
            var over = CharaWet.FromToken(new JObject { ["stains"] = new JArray(Enumerable.Range(0, WetLimits.MaxStains + 2).Select(i => JObject.Parse("{\"id\": \"s" + i + "\"}"))) });
            St(s, id).Wet = over;
            var edit = over.Clone();
            edit.Film.Gloss = 0.5f;
            Kit.Ok(t, Run(s, new WetEditCommand(id, edit)), "WE0: a value of a block a file brought over the limit");
            var fewer = St(s, id).Wet.Clone();
            fewer.Stains.RemoveAt(0);
            Kit.Ok(t, Run(s, new WetEditCommand(id, fewer)), "WE0: one stain fewer");
            var more = St(s, id).Wet.Clone();
            more.Stains.Add(new WetStain { Id = more.NextId() });
            more.Stains.Add(new WetStain { Id = "zz" });
            Kit.Fails(t, Run(s, new WetEditCommand(id, more)), CommandCodes.BadArgs, "WE0: but not more than it held");
        }

        static void Capture(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            Kit.Ok(t, Run(s, new WetEditCommand(id, Block(stains: 2), WetEditCommand.LabelAdd)), "WE0 capture: two stains");
            int n = s.Commands.UndoList.Count;
            var rec = new EventRecorder(s);
            var list = new List<WetPuddleEntry>
            {
                new WetPuddleEntry { Stain = "s1", Center = new Vec3(1f, 0f, 2f), Area = 0.5f, Seed = 2.5f },
                new WetPuddleEntry { Stain = "s2", Area = 0.25f, Color = new Color4(0.1f, 0.2f, 0.3f, 0.4f) },
            };
            var results = new List<CommandResult>();
            s.WriteSilently(ChangeOrigin.Capture, w =>
            {
                results.Add(w.Set(id, "chara.wet.stains[1].t", 1.5f));
                results.Add(w.Set(id, "chara.wet.stains[1].running", true));
                results.Add(w.Set(id, "chara.wet.puddles", list));
            });
            foreach (var r in results) t.Check(r.Status == CommandStatus.Ok, "WE0 capture: a field write lands (" + r + ")");
            var wet = St(s, id).Wet;
            t.Check(wet.Stains[1].T == 1.5f && wet.Stains[1].Running && wet.Puddles.Count == 2 && wet.Puddles[0].Center.Equals(new Vec3(1f, 0f, 2f)) && !wet.Puddles[0].Color.HasValue && wet.Puddles[1].Color.HasValue, "WE0 capture: the values are in the model");
            t.Check(!ReferenceEquals(wet.Puddles, list) && !ReferenceEquals(wet.Puddles[0], list[0]), "WE0 capture: a copy of the list");
            t.Equal(n, s.Commands.UndoList.Count, "WE0 capture: not recorded");
            t.Check(rec.Batches.Count == 1 && rec.Batches[0].Origin == ChangeOrigin.Capture, "WE0 capture: one batch of origin Capture");
            t.Check(rec.Batches[0].Changes.All(c => c.Kind == SceneChangeKind.NodeChanged && FieldPath.Overlaps(WetEditCommand.ChangePath, c.Path)), "WE0 capture: under chara.wet");
            string captured = wet.ToJson();
            var next = wet.Clone();
            next.Stains[0].Look.Amount = 0.5f;
            Kit.Ok(t, Run(s, new WetEditCommand(id, next)), "WE0 capture: an edit after the capture");
            Kit.Ok(t, s.Commands.Undo(), "WE0 capture: undo");
            t.Equal(captured, St(s, id).Wet.ToJson(), "WE0 capture: an undo restores what was captured");
        }

        static void Presets(TestRunner t)
        {
            t.Equal("water,blood", string.Join(",", WetPreset.Names), "WM6: the two presets");
            t.Check(WetPreset.IsKnown("water") && WetPreset.IsKnown("blood") && !WetPreset.IsKnown("Water") && !WetPreset.IsKnown("") && !WetPreset.IsKnown(null), "WM6: names are exact");

            // A stain away from every default, so that a member the preset does not write shows.
            WetStain Away()
            {
                var s = new WetStain { Id = "a", Name = "n", Kept = true, T = 2f, Running = true, Loop = true, Hold = 3f, Seed = 9 };
                s.Origin = new WetOriginValues { Mode = WetOriginValues.ModeBone, Bone = "b", Local = new Vec3(1f, 2f, 3f), Down = 0.5f, Out = 0.25f };
                s.Shape = new WetShapeValues { Radius = 5f, Spread = 7f, Up = 2f, Down = 1f, Front = 2f, Back = 2f, Side = 2f, Outer = 2f, Noise = 1f, Feather = 1f };
                s.Look = new WetLookValues { Saturate = 0.5f, Tint = new Vec3(2f, 2f, 2f), Stain = new Vec3(1f, 1f, 1f), Amount = 1f, Skin = 1f };
                s.Drips = new WetDripValues { On = false, Rate = 1f, Tail = 1f, Size = 0.1f, Growth = 2f, Skin = true, Color = new Color4(1f, 1f, 1f, 1f), Shine = 1f };
                s.Puddle = new WetPuddleLook { Color = new Color4(1f, 1f, 1f, 1f), Shine = 0.5f, Edge = 0.2f, Scale = 3f };
                s.Maps.Add(new WetMapEntry { Renderer = "body/b", Slot = 1, Res = "res/" + new string('0', 64) + ".png", Reach = 2f, Mesh = "m" });
                return s;
            }
            void Same(WetStain s, string what)
            {
                var o = Away();
                t.Check(s.Id == o.Id && s.Name == o.Name && s.Kept == o.Kept && s.T == o.T && s.Running == o.Running && s.Loop == o.Loop && s.Hold == o.Hold && s.Seed == o.Seed && s.Maps.Count == 1, what + ": the stain's own members stay");
                t.Check(s.Origin.Mode == o.Origin.Mode && s.Origin.Bone == o.Origin.Bone && s.Origin.Down == o.Origin.Down && s.Origin.Out == o.Origin.Out && s.Origin.Local.Equals(o.Origin.Local), what + ": the origin stays");
                t.Check(s.Shape.Radius == 5f && s.Shape.Spread == 7f && s.Shape.Up == 2f && s.Shape.Down == 1f && s.Shape.Front == 2f && s.Shape.Back == 2f && s.Shape.Side == 2f && s.Shape.Outer == 2f && s.Shape.Noise == 1f && s.Shape.Feather == 1f, what + ": the shape stays");
                t.Check(!s.Drips.On && s.Drips.Rate == 1f && s.Drips.Tail == 1f && s.Drips.Size == 0.1f && s.Drips.Growth == 2f && s.Drips.Skin, what + ": the drips' other members stay");
                t.Check(s.Puddle.Edge == 0.2f && s.Puddle.Scale == 3f, what + ": the puddle's other members stay");
            }

            var water = Away();
            t.Check(WetPreset.ApplyTo(water, WetPreset.Water) && water.Preset == "water", "WM6: water applies and names itself");
            t.Check(water.Look.Saturate == 1.15f && water.Look.Tint.Equals(Vec3.One) && water.Look.Stain.Equals(Vec3.Zero) && water.Look.Amount == 0.3f && water.Look.Skin == 0.15f, "WM6: water's look: saturate 1.15, tint 1, black, amount 0.3, skin 0.15");
            t.Check(water.Drips.Color.Equals(new Color4(0.85f, 0.88f, 0.75f, 0.2f)) && water.Drips.Shine == 0.6f, "WM6: water's drips: .85, .88, .75, .2, shine 0.6");
            t.Check(water.Puddle.Color.Equals(new Color4(0.1f, 0.097f, 0.097f, 0.8f)) && water.Puddle.Shine == 1f, "WM6: water's puddle: .1, .097, .097, .8, shine 1");
            Same(water, "WM6: water");

            var blood = Away();
            t.Check(WetPreset.ApplyTo(blood, WetPreset.Blood) && blood.Preset == "blood", "WM6: blood applies and names itself");
            t.Check(blood.Look.Saturate == 1f && blood.Look.Tint.Equals(Vec3.One) && blood.Look.Stain.Equals(new Vec3(0.42f, 0.03f, 0.04f)) && blood.Look.Amount == 0.85f && blood.Look.Skin == 0.85f, "WM6: blood's look: saturate 1, tint 1, .42, .03, .04, amount 0.85, skin 0.85");
            t.Check(blood.Drips.Color.Equals(new Color4(0.2f, 0.02f, 0.03f, 0.9f)) && blood.Drips.Shine == 0.35f, "WM6: blood's drips: .2, .02, .03, .9, shine 0.35");
            t.Check(blood.Puddle.Color.Equals(new Color4(0.1f, 0.01f, 0.02f, 0.9f)) && blood.Puddle.Shine == 0.3f, "WM6: blood's puddle: .1, .01, .02, .9, shine 0.3");
            Same(blood, "WM6: blood");

            var unknown = Away();
            t.Check(!WetPreset.ApplyTo(unknown, "lava") && !WetPreset.ApplyTo(unknown, null) && unknown.Preset == "" && unknown.Look.Saturate == 0.5f, "WM6: an unknown preset changes nothing");
            t.Throws<ArgumentNullException>(() => WetPreset.ApplyTo(null, WetPreset.Water), "WM6: no stain");

            // Water's values are the defaults of a new stain, as the table has them, and they survive a file.
            var fresh = new WetStain();
            var w2 = new WetStain();
            WetPreset.ApplyTo(w2, WetPreset.Water);
            var a = new CharaWet();
            a.Stains.Add(fresh);
            fresh.Preset = "water";
            fresh.Id = w2.Id = "s1";
            var bb = new CharaWet();
            bb.Stains.Add(w2);
            t.Equal(a.ToJson(), bb.ToJson(), "WM6: water on a new stain changes nothing but its name");
        }

        static void Saved(TestRunner t)
        {
            var s = NewSession(t, out string id, out _);
            var opts = Kit.FixedSave(ThumbnailMode.Placeholder, new ExtRegistry());
            var b0 = s.ToBytes(opts);
            int FormatOf(byte[] f) => (int)JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(f).Get("manifest.json").Data))["formatVersion"];
            string Scene(byte[] f) => Encoding.UTF8.GetString(ContainerReader.Read(f).Get("scene.json").Data);
            t.Check(FormatOf(b0) == 1 && !Scene(b0).Contains("\"wet\": {"), "WE0 file: no block: revision 1, no member");

            Kit.Ok(t, Run(s, new WetEditCommand(id, Block())), "WE0 file: a stain");
            var b1 = s.ToBytes(opts);
            t.Check(FormatOf(b1) == 5 && Scene(b1).Contains("\"wet\": {"), "WE0 file: revision 5, with the member");

            var s2 = Kit.Session();
            var l = s2.Load(b1, new LoadOptions { Registry = new ExtRegistry() });
            t.Check(l.Ok, "WE0 file: it loads (" + l + ")");
            if (l.Ok)
            {
                t.Equal(St(s, id).Wet.ToJson(), St(s2, id).Wet.ToJson(), "WE0 file: the block comes back");
                t.Check(!s2.Commands.IsDirty, "WE0 file: a load is not an edit");
            }

            Kit.Ok(t, Run(s, new WetEditCommand(id, null, WetEditCommand.LabelRemove)), "WE0 file: removed");
            var b2 = s.ToBytes(opts);
            t.Check(FormatOf(b2) == 1 && Scene(b2) == Scene(b0) && Kit.Same(b2, b0), "WE0 file: back to revision 1, the file as before");
            Kit.Ok(t, s.Commands.Undo(), "WE0 file: undo of the removal");
            t.Check(!St(s, id).Wet.IsEmpty, "WE0 file: the block is back");
        }
    }
}
