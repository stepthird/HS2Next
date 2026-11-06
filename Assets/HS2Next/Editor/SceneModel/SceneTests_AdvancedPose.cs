// AS6: the address grammar of advanced pose (PoseAddress): every form, the occurrence mark, what is not an address, the canonical
// text. AS7: the block on the bus (AdvancedPoseEdits through transactions, as the operations do): each edit is one history entry with
// its label, undo and redo restore the block's JSON; merges; `undo: false`; a batch; an identity correction, an emptied chain and an
// emptied renderer leave their map; a set after a clear; the field paths through FieldAccessor. Synthetic names and ids only.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_AdvancedPose
    {
        static string Id(int n) => n.ToString("x32");

        public static void Run(TestRunner t)
        {
            AS6(t);
            AS7Entries(t);
            AS7Identity(t);
            AS7Merges(t);
            AS7NotRecordedAndBatch(t);
            AS7SetAfterClear(t);
            AS7Paths(t);
            AS7Scene(t);
        }

        // ---------------- AS6 ----------------

        static void Form(TestRunner t, PoseAddressForm form, string text, PosePart part, int slot, string id, string name, int n)
        {
            bool ok = PoseAddress.TryParse(form, text, out var a, out string error);
            t.Check(ok && a.Form == form && a.Part == part && a.Slot == slot && a.AccessoryId == id && a.Name == name && a.Occurrence == n && a.Text == text,
                "AS6: " + form + " \"" + text + "\" reads as " + part + " slot " + slot + " name " + name + " #" + n + (ok ? " (got " + a.Part + " " + a.Slot + " " + a.Name + " " + a.Occurrence + " \"" + a.Text + "\")" : " (" + error + ")"));
            t.Check(PoseAddress.IsCanonical(form, text) && PoseAddress.Canonical(form, text) == text, "AS6: \"" + text + "\" is canonical");
        }

        static void AS6(TestRunner t)
        {
            string acc = Id(0x77);
            var B = PoseAddressForm.Bone;
            var C = PoseAddressForm.Chain;
            var R = PoseAddressForm.Blend;

            // Every form.
            Form(t, B, "cf_J_Hand_Index02_L", PosePart.Named, -1, null, "cf_J_Hand_Index02_L", 0);
            Form(t, B, "cf_J_MouthMove", PosePart.Named, -1, null, "cf_J_MouthMove", 0);
            Form(t, B, "hair:0/bone", PosePart.Hair, 0, null, "bone", 0);
            Form(t, B, "hair:3/bone#1", PosePart.Hair, 3, null, "bone", 1);
            Form(t, B, "clothes:0/bone", PosePart.Clothes, 0, null, "bone", 0);
            Form(t, B, "clothes:7/bone#12", PosePart.Clothes, 7, null, "bone", 12);
            Form(t, B, "acc:" + acc + "/N_move#2", PosePart.Accessory, -1, acc, "N_move", 2);
            Form(t, C, "hair:1/root", PosePart.Hair, 1, null, "root", 0);
            Form(t, C, "clothes:2/root#1", PosePart.Clothes, 2, null, "root", 1);
            Form(t, C, "acc:" + acc + "/root", PosePart.Accessory, -1, acc, "root", 0);
            Form(t, C, "other/root#3", PosePart.Other, -1, null, "root", 3);
            Form(t, R, "head/o_head", PosePart.Head, -1, null, "o_head", 0);
            Form(t, R, "body/o_body#1", PosePart.Body, -1, null, "o_body", 1);
            Form(t, R, "hair:2/mesh", PosePart.Hair, 2, null, "mesh", 0);
            Form(t, R, "clothes:1/mesh#4", PosePart.Clothes, 1, null, "mesh", 4);
            Form(t, R, "acc:" + acc + "/mesh", PosePart.Accessory, -1, acc, "mesh", 0);

            // A bare bone name is taken whole: its own characters, a colon or a mark included.
            Form(t, B, "a#2", PosePart.Named, -1, null, "a#2", 0);
            Form(t, B, "name with space", PosePart.Named, -1, null, "name with space", 0);
            Form(t, B, "ns:bone", PosePart.Named, -1, null, "ns:bone", 0);

            // The occurrence mark: a number in canonical decimal, else part of the name.
            Form(t, C, "other/a#", PosePart.Other, -1, null, "a#", 0);
            Form(t, C, "other/a#01", PosePart.Other, -1, null, "a#01", 0);
            Form(t, C, "other/a#b", PosePart.Other, -1, null, "a#b", 0);
            Form(t, C, "other/a#-1", PosePart.Other, -1, null, "a#-1", 0);
            Form(t, C, "other/a#1234567890", PosePart.Other, -1, null, "a#1234567890", 0);
            Form(t, C, "other/a#b#2", PosePart.Other, -1, null, "a#b", 2);
            // A name that itself ends in a mark keeps the mark apart, so no two addresses read alike.
            Form(t, C, "other/joint#2#0", PosePart.Other, -1, null, "joint#2", 0);
            Form(t, C, "other/joint#2#1", PosePart.Other, -1, null, "joint#2", 1);
            t.Equal("other/joint#2#0", PoseAddress.Chain(PosePart.Other, -1, null, "joint#2").Text, "AS6: such a name is written with its mark");
            t.Equal("other/a#01", PoseAddress.Chain(PosePart.Other, -1, null, "a#01").Text, "AS6: a mark that is no number needs none");

            // Canonical text.
            void Canon(PoseAddressForm f, string text, string canonical)
            {
                t.Equal(canonical, PoseAddress.Canonical(f, text), "AS6: the canonical text of \"" + text + "\"");
                t.Check(!PoseAddress.IsCanonical(f, text), "AS6: \"" + text + "\" reads but acts only in its canonical form");
                t.Check(PoseAddress.TryParse(f, text, out var a, out _) && a.Text == canonical, "AS6: the value's text is the canonical one");
            }
            Canon(B, "hair:1/a#0", "hair:1/a");
            Canon(C, "other/a#0", "other/a");
            Canon(R, "head/a#0", "head/a");

            // What is not an address.
            void Bad(PoseAddressForm f, string text)
            {
                bool ok = PoseAddress.TryParse(f, text, out _, out string error);
                t.Check(!ok && !string.IsNullOrEmpty(error), "AS6: " + f + " \"" + text + "\" is not an address (" + error + ")");
                t.Check(PoseAddress.Canonical(f, text) == null && !PoseAddress.IsCanonical(f, text), "AS6: and has no canonical text");
                t.Throws<FormatException>(() => PoseAddress.Parse(f, text), "AS6: Parse says so");
            }
            foreach (var x in new[] { null, "", "a/b", "head/a", "body/a", "other/a", "hair:", "hair:1", "hair:1/", "hair:4/a", "hair:01/a", "hair:-1/a", "hair:x/a", "hair:1/a/b", "clothes:", "clothes:8/a", "clothes:1", "acc:", "acc:/a", "acc:12/a", "acc:" + acc, "acc:" + new string('A', 32) + "/a" })
                Bad(B, x);
            foreach (var x in new[] { null, "", "root", "head/a", "body/a", "other", "other/", "/a", "hair:9/a", "other/a/b", "other/#1" })
                Bad(C, x);
            foreach (var x in new[] { null, "", "o_head", "other/a", "head", "head/", "body/", "/a", "clothes:9/a", "head/a/b", "head/#2" })
                Bad(R, x);

            // Made from parts, read back equal.
            var made = new[]
            {
                PoseAddress.Bone("cf_J_Hips"),
                PoseAddress.Bone(PosePart.Hair, 2, null, "x"),
                PoseAddress.Bone(PosePart.Clothes, 5, null, "x", 3),
                PoseAddress.Bone(PosePart.Accessory, -1, acc, "x", 1),
                PoseAddress.Chain(PosePart.Other, -1, null, "x", 2),
                PoseAddress.Chain(PosePart.Hair, 0, null, "x"),
                PoseAddress.Blend(PosePart.Head, -1, null, "x"),
                PoseAddress.Blend(PosePart.Body, -1, null, "x", 1),
                PoseAddress.Blend(PosePart.Accessory, -1, acc, "x"),
            };
            foreach (var a in made)
            {
                t.Check(PoseAddress.TryParse(a.Form, a.Text, out var b, out _) && a.Equals(b) && a.GetHashCode() == b.GetHashCode() && b.PartText == a.PartText, "AS6: " + a.Text + " reads back equal");
                t.Equal(a.Text, PoseAddress.Format(a.Part, a.Slot, a.AccessoryId, a.Name, a.Occurrence), "AS6: Format gives the same text");
            }
            t.Check(!PoseAddress.Bone(PosePart.Hair, 2, null, "x").Equals(PoseAddress.Blend(PosePart.Hair, 2, null, "x")), "AS6: one text in two forms differ");
            t.Equal("hair:1", PoseAddress.Parse(B, "hair:1/a").PartText, "AS6: the part's text");
            t.Equal("acc:" + acc, PoseAddress.Parse(B, "acc:" + acc + "/a").PartText, "AS6: the accessory part's text");
            t.Equal("", PoseAddress.Parse(B, "a").PartText, "AS6: a bare name has no part");

            // The part argument of the read commands.
            t.Check(PoseAddressGrammar.TryParsePart("head", out var p1, out _, out _, out _) && p1 == PosePart.Head, "AS6: the part head");
            t.Check(PoseAddressGrammar.TryParsePart("clothes:3", out var p2, out int s2, out _, out _) && p2 == PosePart.Clothes && s2 == 3, "AS6: a part with a slot");
            t.Check(!PoseAddressGrammar.TryParsePart("nothing", out _, out _, out _, out _), "AS6: an unknown part");
        }

        // ---------------- AS7 ----------------

        const string LabelBone = "Bone correction", LabelClearBones = "Clear bone corrections", LabelJiggle = "Jiggle values",
            LabelClearJiggle = "Clear jiggle values", LabelBlend = "Blend shapes", LabelClearBlend = "Clear blend shapes";

        static readonly string Node = Kit.GoldenChara;
        static readonly Vec3 Up = new Vec3(0f, 1f, 0f);

        static CharaAdvancedPose Block(SceneSession s, string id = null) => ((CharaNodeState)s.Model.Find(id ?? Node).Doc.State).AdvancedPose;
        static string Json(SceneSession s, string id = null) => Block(s, id).ToJson();
        static KeyValuePair<string, float> W(string shape, float weight) => new KeyValuePair<string, float>(shape, weight);

        // One edit, as an operation runs it: a transaction with its label (it joins an open one), one write.
        static CommandResult Op(SceneSession s, string label, AdvancedPoseWrite? write, ExecuteOptions o = default)
        {
            using (var tx = s.Commands.BeginOrJoin(label, o))
            {
                if (write.HasValue)
                {
                    var r = tx.Set(Node, write.Value.Path, write.Value.Value);
                    if (r.Status == CommandStatus.Error) return r;
                }
                return tx.Commit();
            }
        }

        static void AS7Entries(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var rec = new EventRecorder(s);
            t.Check(Block(s).IsEmpty, "AS7: the block starts empty");
            var states = new List<string> { Json(s) };
            var labels = new List<string>();
            long v0 = s.Commands.Version;
            int n0 = s.Commands.UndoList.Count;

            void Step(string label, Func<AdvancedPoseWrite?> write)
            {
                long v = s.Commands.Version;
                int n = s.Commands.UndoList.Count;
                rec.Clear();
                Kit.Ok(t, Op(s, label, write()), "AS7: " + label);
                t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == label, "AS7: " + label + " is one entry labelled so");
                t.Check(s.Commands.Version > v, "AS7: " + label + " raises the version");
                var changes = rec.Batches.SelectMany(b => b.Changes).Where(c => c.Kind == SceneChangeKind.NodeChanged).ToList();
                t.Check(changes.Count > 0 && changes.All(c => c.NodeId == Node && AdvancedPosePaths.Touches(c.Path)), "AS7: " + label + " reports changes on the block's paths");
                states.Add(Json(s));
                labels.Add(label);
            }

            string acc = "acc:" + Id(0x77) + "/mesh#1";
            Step(LabelBone, () => AdvancedPoseEdits.SetBone(Block(s), "cf_J_Hips", Up, null, null));
            t.Check(Block(s).Bones["cf_J_Hips"].Pos.Equals(Up) && Block(s).Bones["cf_J_Hips"].IsRotIdentity && s.Commands.IsDirty, "AS7: stored; the scene is modified");
            Step(LabelBone, () => AdvancedPoseEdits.SetBone(Block(s), "cf_J_Hips", null, new Vec3(10f, 0f, 0f), null));
            t.Check(Block(s).Bones["cf_J_Hips"].Pos.Equals(Up) && Block(s).Bones["cf_J_Hips"].Rot.X == 10f, "AS7: an omitted channel keeps its value");
            Step(LabelBone, () => AdvancedPoseEdits.SetBone(Block(s), "hair:1/a#2", null, null, new Vec3(2f, 2f, 2f)));
            Step(LabelClearBones, () => AdvancedPoseEdits.RemoveBone(Block(s), "hair:1/a#2"));
            t.Check(!Block(s).Bones.ContainsKey("hair:1/a#2") && Block(s).Bones.Count == 1, "AS7: one bone removed, the other stays");
            Step(LabelJiggle, () => { var c = AdvancedPoseEdits.ChainOf(Block(s), "other/r"); c.Weight = 0.5f; c.Gravity = Up; return AdvancedPoseEdits.SetChain(Block(s), "other/r", c); });
            Step(LabelJiggle, () => { var c = AdvancedPoseEdits.ChainOf(Block(s), "other/r"); c.Weight = null; c.FreezeAxis = 2; return AdvancedPoseEdits.SetChain(Block(s), "other/r", c); });
            t.Check(!Block(s).Chains["other/r"].Weight.HasValue && Block(s).Chains["other/r"].Gravity.Value.Equals(Up) && Block(s).Chains["other/r"].FreezeAxis == 2, "AS7: a member removed, the others kept");
            Step(LabelClearJiggle, () => AdvancedPoseEdits.RemoveChain(Block(s), "other/r"));
            Step(LabelJiggle, () => AdvancedPoseEdits.SetChain(Block(s), "hair:0/r", new ChainValues { Enabled = false }));
            Step(LabelJiggle, () => { var b = AdvancedPoseEdits.BustOf(Block(s), "hipR"); b.Damping = 0.25f; return AdvancedPoseEdits.SetBust("hipR", b); });
            Step(LabelJiggle, () => { var b = AdvancedPoseEdits.BustOf(Block(s), "breastL"); b.Enabled = false; b.Force = Up; return AdvancedPoseEdits.SetBust("breastL", b); });
            t.Check(Block(s).Bust.HipR.Damping == 0.25f && Block(s).Bust.BreastL.Enabled == false && !Block(s).Bust.BreastR.Enabled.HasValue, "AS7: bust chains apart");
            Step(LabelClearJiggle, () => AdvancedPoseEdits.SetBust("hipR", new BustValues()));
            t.Check(Block(s).Bust.HipR.IsEmpty && Block(s).ToJObject()["bust"]["hipR"] == null && Block(s).ToJObject()["bust"]["breastL"] != null, "AS7: a bust chain emptied is not written");
            Step(LabelClearJiggle, () => AdvancedPoseEdits.ClearBust(Block(s)));
            t.Check(Block(s).Bust != null && Block(s).Bust.IsEmpty, "AS7: bust cleared, not null");
            Step(LabelBlend, () => AdvancedPoseEdits.SetWeights(Block(s), "head/o_head", new[] { W("a", 40f), W("b", 60f) }));
            Step(LabelBlend, () => AdvancedPoseEdits.SetWeights(Block(s), "head/o_head", new[] { W("b", 70f) }, new[] { "a" }));
            t.Check(Block(s).BlendShapes["head/o_head"].Weights.Count == 1 && Block(s).BlendShapes["head/o_head"].Weights["b"] == 70f, "AS7: one weight changed, one removed");
            Step(LabelBlend, () => AdvancedPoseEdits.SetWeights(Block(s), acc, new[] { W("s", 5f) }));
            Step(LabelClearBlend, () => AdvancedPoseEdits.RemoveRenderer(Block(s), acc));
            Step(LabelClearBlend, () => AdvancedPoseEdits.ClearWeights(Block(s)));
            t.Check(Block(s).BlendShapes.Count == 0, "AS7: blend shapes cleared");
            Step(LabelClearJiggle, () => AdvancedPoseEdits.ClearChains(Block(s)));
            Step(LabelClearBones, () => AdvancedPoseEdits.ClearBones(Block(s)));
            t.Check(Block(s).IsEmpty, "AS7: all cleared, the block is empty");
            t.Equal(n0 + labels.Count, s.Commands.UndoList.Count, "AS7: one entry per edit");
            Kit.Valid(t, s, "AS7: after the edits");

            // Undo walks back through every state byte-equal, redo forward.
            bool undoOk = true, redoOk = true;
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                Kit.Ok(t, s.Commands.Undo(), "AS7: undo " + labels[i]);
                if (Json(s) != states[i]) { undoOk = false; t.Info("undo " + i + " " + labels[i] + " differs"); }
            }
            t.Check(undoOk, "AS7: undo restores the block's JSON at every step");
            t.Check(Block(s).IsEmpty && s.Commands.UndoList.Count == n0, "AS7: all undone, the block is empty and the history as before");
            for (int i = 0; i < labels.Count; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "AS7: redo " + labels[i]);
                if (Json(s) != states[i + 1]) { redoOk = false; t.Info("redo " + i + " " + labels[i] + " differs"); }
            }
            t.Check(redoOk, "AS7: redo restores the block's JSON at every step");
            t.Check(s.Commands.Version > v0, "AS7: the version kept rising");
            Kit.Valid(t, s, "AS7: after undo and redo");

            // A write that changes nothing records nothing.
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "b", Up, null, null)), "AS7: set");
            int nn = s.Commands.UndoList.Count;
            var again = Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "b", Up, null, null));
            t.Check(again.Status != CommandStatus.Error && s.Commands.UndoList.Count == nn, "AS7: the same value again records nothing (" + again + ")");
            t.Check(AdvancedPoseEdits.RemoveBone(Block(s), "none") == null && AdvancedPoseEdits.RemoveChain(Block(s), "none") == null && AdvancedPoseEdits.RemoveRenderer(Block(s), "none") == null, "AS7: removing what is not there is no write");
            t.Check(AdvancedPoseEdits.ClearChains(Block(s)) == null && AdvancedPoseEdits.ClearWeights(Block(s)) == null && AdvancedPoseEdits.ClearBust(Block(s)) == null, "AS7: clearing what is empty is no write");
            t.Throws<ArgumentException>(() => AdvancedPoseEdits.SetBust("breastX", new BustValues()), "AS7: an unknown bust chain");
            t.Throws<ArgumentException>(() => AdvancedPoseEdits.BustOf(Block(s), "breastX"), "AS7: an unknown bust chain's values");
        }

        static void AS7Identity(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var bytes0 = Kit.Bytes(s);
            t.Check(!Kit.Text(bytes0).Contains("advancedPose"), "AS7 identity: no block in the file at first");
            t.Check(AdvancedPoseEdits.SetBone(Block(s), "a", Vec3.Zero, Vec3.Zero, Vec3.One) == null, "AS7 identity: an identity on no entry is no write");

            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "a", Up, null, null)), "AS7 identity: set");
            t.Check(Kit.Text(Kit.Bytes(s)).Contains("advancedPose"), "AS7 identity: the block is in the file");
            int n = s.Commands.UndoList.Count;
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "a", Vec3.Zero, null, null)), "AS7 identity: the channel back to identity");
            t.Check(!Block(s).Bones.ContainsKey("a") && Block(s).IsEmpty, "AS7 identity: the entry is removed");
            t.Check(s.Commands.UndoList.Count == n + 1, "AS7 identity: one entry");
            t.Check(Kit.Same(bytes0, Kit.Bytes(s)), "AS7 identity: the file is byte-equal to one that never had it");
            Kit.Ok(t, s.Commands.Undo(), "AS7 identity: undo");
            t.Check(Block(s).Bones["a"].Pos.Equals(Up), "AS7 identity: undo brings the entry back");
            Kit.Ok(t, s.Commands.Redo(), "AS7 identity: redo");
            t.Check(Block(s).IsEmpty, "AS7 identity: redo removes it again");

            // Identity on one channel keeps the entry of the others.
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "b", Up, Up, null)), "AS7 identity: two channels");
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "b", Vec3.Zero, null, null)), "AS7 identity: one back");
            t.Check(Block(s).Bones.ContainsKey("b") && Block(s).Bones["b"].IsPosIdentity && !Block(s).Bones["b"].IsRotIdentity, "AS7 identity: the entry stays for the other channel");
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "b", null, Vec3.Zero, null)), "AS7 identity: the other back");
            t.Check(Block(s).IsEmpty, "AS7 identity: now it leaves");

            // Two removals inside one batch, each computed from the block as it stands.
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "c", Up, null, null)), "AS7 identity: c");
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "d", Up, null, null)), "AS7 identity: d");
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "e", Up, null, null)), "AS7 identity: e");
            n = s.Commands.UndoList.Count;
            string three = Json(s);
            using (var tx = s.Commands.BeginOrJoin(LabelBone))
            {
                foreach (var k in new[] { "c", "d" })
                {
                    var w = AdvancedPoseEdits.SetBone(Block(s), k, Vec3.Zero, null, null).Value;
                    Kit.Ok(t, tx.Set(Node, w.Path, w.Value), "AS7 identity: remove " + k);
                }
                Kit.Ok(t, tx.Commit(), "AS7 identity: commit");
            }
            t.Check(Block(s).Bones.Count == 1 && Block(s).Bones.ContainsKey("e") && s.Commands.UndoList.Count == n + 1, "AS7 identity: both left, one entry");
            Kit.Ok(t, s.Commands.Undo(), "AS7 identity: undo the batch");
            t.Equal(three, Json(s), "AS7 identity: undo restores both");

            // A hand-made identity entry: kept until the next edit of it, then removed.
            var hand = Kit.Session(5);
            var hb = Kit.GoldenContainer(t, false);
            ((CharaNodeState)hb.Scene.Find(Node).State).AdvancedPose = CharaAdvancedPose.Parse("{\"bones\": {\"h\": {}, \"i\": {\"pos\": [0, 1, 0]}}}");
            hand.Bind(hb);
            t.Check(Block(hand).Bones.ContainsKey("h") && Block(hand).Bones["h"].IsIdentity, "AS7 identity: a file's identity entry is kept");
            Kit.Ok(t, Op(hand, LabelBone, AdvancedPoseEdits.SetBone(Block(hand), "i", new Vec3(0f, 2f, 0f), null, null)), "AS7 identity: an edit of another entry");
            t.Check(Block(hand).Bones.ContainsKey("h"), "AS7 identity: leaves it");
            Kit.Ok(t, Op(hand, LabelBone, AdvancedPoseEdits.SetBone(Block(hand), "h", null, null, Vec3.One)), "AS7 identity: the next edit of it");
            t.Check(!Block(hand).Bones.ContainsKey("h") && Block(hand).Bones.ContainsKey("i"), "AS7 identity: removes it");

            // Chains and renderers left with nothing leave their maps.
            var s2 = Kit.GoldenSession(t);
            Kit.Ok(t, Op(s2, LabelJiggle, AdvancedPoseEdits.SetChain(Block(s2), "other/r", new ChainValues { Weight = 0.5f })), "AS7 identity: a chain");
            var cv = AdvancedPoseEdits.ChainOf(Block(s2), "other/r");
            cv.Weight = null;
            Kit.Ok(t, Op(s2, LabelJiggle, AdvancedPoseEdits.SetChain(Block(s2), "other/r", cv)), "AS7 identity: its last member removed");
            t.Check(Block(s2).Chains.Count == 0 && Block(s2).IsEmpty, "AS7 identity: the chain leaves");
            t.Check(AdvancedPoseEdits.SetChain(Block(s2), "other/none", new ChainValues()) == null, "AS7 identity: an empty chain that is not there is no write");
            Kit.Ok(t, Op(s2, LabelBlend, AdvancedPoseEdits.SetWeights(Block(s2), "head/h", new[] { W("s", 1f) })), "AS7 identity: a weight");
            Kit.Ok(t, Op(s2, LabelBlend, AdvancedPoseEdits.SetWeights(Block(s2), "head/h", null, new[] { "s" })), "AS7 identity: the last weight removed");
            t.Check(Block(s2).BlendShapes.Count == 0 && Block(s2).IsEmpty, "AS7 identity: the renderer leaves");
            t.Check(AdvancedPoseEdits.SetWeights(Block(s2), "head/none", null, new[] { "s" }) == null, "AS7 identity: removing a weight of no renderer is no write");
        }

        static void AS7Merges(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            string before = Json(s);
            int n = s.Commands.UndoList.Count;

            // A chain of sets of one entry with one merge key: one entry, first old to last new. (A merge runs on the bus directly: the
            // entry of a committed transaction is sealed.)
            string key = "pose:" + Node + ":a";
            var o = new ExecuteOptions { MergeKey = key, Source = CommandSource.Bridge, Label = LabelBone };
            for (int i = 1; i <= 3; i++)
            {
                var w = AdvancedPoseEdits.SetBone(Block(s), "a", new Vec3(i, 0f, 0f), null, null).Value;
                Kit.Ok(t, s.Set(Node, w.Path, w.Value, o), "AS7 merge: set " + i);
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == LabelBone && Block(s).Bones["a"].Pos.X == 3f, "AS7 merge: three sets of one address are one entry");
            string merged = Json(s);
            Kit.Ok(t, s.Commands.Undo(), "AS7 merge: undo");
            t.Equal(before, Json(s), "AS7 merge: undo restores the block before the first");
            Kit.Ok(t, s.Commands.Redo(), "AS7 merge: redo");
            t.Equal(merged, Json(s), "AS7 merge: redo the last value");

            // Another address, another entry. A removal is not a set of that entry: a separate step.
            int m = s.Commands.UndoList.Count;
            var wb = AdvancedPoseEdits.SetBone(Block(s), "b", Up, null, null).Value;
            Kit.Ok(t, s.Set(Node, wb.Path, wb.Value, new ExecuteOptions { MergeKey = "pose:" + Node + ":b", Label = LabelBone }), "AS7 merge: another address");
            t.Equal(m + 1, s.Commands.UndoList.Count, "AS7 merge: another address is its own entry");
            var wr = AdvancedPoseEdits.SetBone(Block(s), "b", Vec3.Zero, null, null).Value;
            Kit.Ok(t, s.Set(Node, wr.Path, wr.Value, new ExecuteOptions { MergeKey = "pose:" + Node + ":b", Label = LabelBone }), "AS7 merge: the same address back to identity");
            t.Check(!Block(s).Bones.ContainsKey("b") && s.Commands.UndoList.Count == m + 2, "AS7 merge: a removal stands alone");
            Kit.Ok(t, s.Commands.Undo(), "AS7 merge: undo the removal");
            t.Check(Block(s).Bones["b"].Pos.Equals(Up), "AS7 merge: the entry is back");
            Kit.Ok(t, s.Commands.Undo(), "AS7 merge: undo its set");
            t.Check(!Block(s).Bones.ContainsKey("b") && Block(s).Bones.ContainsKey("a"), "AS7 merge: gone again");

            // A committed transaction's entry does not merge; a save seals the entry before it.
            var w1 = AdvancedPoseEdits.SetBone(Block(s), "a", new Vec3(5f, 0f, 0f), null, null).Value;
            Kit.Ok(t, s.Set(Node, w1.Path, w1.Value, o), "AS7 merge: a set");
            int k0 = s.Commands.UndoList.Count;
            s.Commands.MarkSaved();
            var w2 = AdvancedPoseEdits.SetBone(Block(s), "a", new Vec3(6f, 0f, 0f), null, null).Value;
            Kit.Ok(t, s.Set(Node, w2.Path, w2.Value, o), "AS7 merge: a set after a save");
            t.Equal(k0 + 1, s.Commands.UndoList.Count, "AS7 merge: a set after a save starts a new entry");
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "a", new Vec3(7f, 0f, 0f), null, null)), "AS7 merge: a transaction");
            var w3 = AdvancedPoseEdits.SetBone(Block(s), "a", new Vec3(8f, 0f, 0f), null, null).Value;
            Kit.Ok(t, s.Set(Node, w3.Path, w3.Value, o), "AS7 merge: a merging set after it");
            t.Equal(k0 + 3, s.Commands.UndoList.Count, "AS7 merge: does not merge into the transaction's entry");
        }

        static void AS7NotRecordedAndBatch(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "a", Up, null, null)), "AS7 undo:false: a recorded set");
            string recorded = Json(s);
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            Kit.Ok(t, Op(s, LabelJiggle, AdvancedPoseEdits.SetChain(Block(s), "other/r", new ChainValues { Weight = 0.5f }), ExecuteOptions.NotRecorded), "AS7 undo:false: through a transaction");
            var w = AdvancedPoseEdits.SetBone(Block(s), "b", Up, null, null).Value;
            Kit.Ok(t, s.Set(Node, w.Path, w.Value, ExecuteOptions.NotRecorded), "AS7 undo:false: on the bus");
            t.Check(Block(s).Chains.ContainsKey("other/r") && Block(s).Bones.ContainsKey("b"), "AS7 undo:false: applied");
            t.Check(s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == LabelBone, "AS7 undo:false: nothing recorded, the history kept");
            t.Check(s.Commands.Version > v && s.Commands.IsDirty, "AS7 undo:false: the version rises, the scene is modified");
            Kit.Ok(t, s.Commands.Undo(), "AS7 undo:false: the earlier entry still undoes");
            t.Check(!Block(s).Bones.ContainsKey("a") && Block(s).Bones.ContainsKey("b"), "AS7 undo:false: only its entry");
            Kit.Ok(t, s.Commands.Redo(), "AS7 undo:false: and redoes");
            t.Check(Block(s).Bones.ContainsKey("a") && Json(s) != recorded, "AS7 undo:false: the unrecorded values stay");

            // A client batch: the edits join its one entry, of every kind.
            var b = Kit.GoldenSession(t);
            string before = Json(b);
            int bn = b.Commands.UndoList.Count;
            using (var tx = b.Commands.Begin("batch", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                Kit.Ok(t, Op(b, LabelBone, AdvancedPoseEdits.SetBone(Block(b), "a", Up, null, null)), "AS7 batch: a bone");
                Kit.Ok(t, Op(b, LabelBone, AdvancedPoseEdits.SetBone(Block(b), "a", null, Up, null)), "AS7 batch: the same bone");
                Kit.Ok(t, Op(b, LabelJiggle, AdvancedPoseEdits.SetChain(Block(b), "other/r", new ChainValues { Weight = 0.5f })), "AS7 batch: a chain");
                Kit.Ok(t, Op(b, LabelJiggle, AdvancedPoseEdits.SetBust("hipL", new BustValues { Enabled = false })), "AS7 batch: a bust chain");
                Kit.Ok(t, Op(b, LabelBlend, AdvancedPoseEdits.SetWeights(Block(b), "head/h", new[] { W("s", 1f) })), "AS7 batch: a weight");
                Kit.Ok(t, Op(b, LabelBone, AdvancedPoseEdits.SetBone(Block(b), "z", Up, null, null)), "AS7 batch: another bone");
                Kit.Ok(t, Op(b, LabelClearBones, AdvancedPoseEdits.RemoveBone(Block(b), "z")), "AS7 batch: removed again");
                Kit.Ok(t, tx.Commit(), "AS7 batch: commit");
            }
            t.Check(b.Commands.UndoList.Count == bn + 1 && b.Commands.UndoList[0].Label == "batch", "AS7 batch: the edits join the batch's one entry");
            string after = Json(b);
            t.Check(Block(b).Bones.Count == 1 && Block(b).Bones["a"].Pos.Equals(Up) && Block(b).Bones["a"].Rot.Equals(Up) && !Block(b).Bones.ContainsKey("z"), "AS7 batch: its values");
            Kit.Ok(t, b.Commands.Undo(), "AS7 batch: undo");
            t.Equal(before, Json(b), "AS7 batch: undo restores the block");
            Kit.Ok(t, b.Commands.Redo(), "AS7 batch: redo");
            t.Equal(after, Json(b), "AS7 batch: redo restores the block");

            // A cancelled batch leaves nothing; an unrecorded batch keeps the history.
            string kept = Json(b);
            int cn = b.Commands.UndoList.Count;
            using (var tx = b.Commands.Begin("cancelled"))
            {
                Kit.Ok(t, Op(b, LabelBone, AdvancedPoseEdits.SetBone(Block(b), "q", Up, null, null)), "AS7 batch: inside one to cancel");
                tx.Cancel();
            }
            t.Check(Json(b) == kept && b.Commands.UndoList.Count == cn, "AS7 batch: a cancelled batch leaves nothing");
            using (var tx = b.Commands.Begin("quiet", ExecuteOptions.NotRecorded))
            {
                var w2 = AdvancedPoseEdits.SetBone(Block(b), "q", Up, null, null).Value;
                Kit.Ok(t, tx.Set(Node, w2.Path, w2.Value), "AS7 batch: inside an unrecorded one");
                Kit.Ok(t, tx.Commit(), "AS7 batch: commit the unrecorded one");
            }
            t.Check(Block(b).Bones.ContainsKey("q") && b.Commands.UndoList.Count == cn, "AS7 batch: applied, the history kept");
        }

        static void AS7SetAfterClear(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "a", Up, null, null)), "AS7 clear: a bone");
            Kit.Ok(t, Op(s, LabelJiggle, AdvancedPoseEdits.SetChain(Block(s), "other/r", new ChainValues { Weight = 0.5f })), "AS7 clear: a chain");
            Kit.Ok(t, Op(s, LabelJiggle, AdvancedPoseEdits.SetBust("hipL", new BustValues { Enabled = false })), "AS7 clear: a bust chain");
            Kit.Ok(t, Op(s, LabelBlend, AdvancedPoseEdits.SetWeights(Block(s), "head/h", new[] { W("s", 1f) })), "AS7 clear: a weight");
            Kit.Ok(t, Op(s, LabelClearBones, AdvancedPoseEdits.ClearBones(Block(s))), "AS7 clear: bones");
            Kit.Ok(t, Op(s, LabelClearJiggle, AdvancedPoseEdits.ClearChains(Block(s))), "AS7 clear: chains");
            Kit.Ok(t, Op(s, LabelClearJiggle, AdvancedPoseEdits.ClearBust(Block(s))), "AS7 clear: bust");
            Kit.Ok(t, Op(s, LabelClearBlend, AdvancedPoseEdits.ClearWeights(Block(s))), "AS7 clear: blend shapes");
            t.Check(Block(s).IsEmpty, "AS7 clear: empty");
            int n = s.Commands.UndoList.Count;
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "a", new Vec3(0f, 2f, 0f), null, null)), "AS7 clear: a set after it");
            Kit.Ok(t, Op(s, LabelJiggle, AdvancedPoseEdits.SetChain(Block(s), "other/r", new ChainValues { Damping = 0.1f })), "AS7 clear: a chain after it");
            var bv = AdvancedPoseEdits.BustOf(Block(s), "hipL");
            t.Check(!bv.Enabled.HasValue, "AS7 clear: a bust chain starts over");
            bv.Inert = 0.5f;
            Kit.Ok(t, Op(s, LabelJiggle, AdvancedPoseEdits.SetBust("hipL", bv)), "AS7 clear: a bust member after it");
            Kit.Ok(t, Op(s, LabelBlend, AdvancedPoseEdits.SetWeights(Block(s), "head/h", new[] { W("t", 2f) })), "AS7 clear: a weight after it");
            var d = Block(s);
            t.Check(d.Bones["a"].Pos.Y == 2f && d.Bones["a"].IsRotIdentity && d.Chains["other/r"].Damping == 0.1f && !d.Chains["other/r"].Weight.HasValue && d.Bust.HipL.Inert == 0.5f && !d.BlendShapes["head/h"].Weights.ContainsKey("s"),
                "AS7 clear: new values, none of the cleared ones");
            string fresh = d.ToJson();
            for (int i = 0; i < 4; i++) Kit.Ok(t, s.Commands.Undo(), "AS7 clear: undo after " + i);
            t.Check(Block(s).IsEmpty && s.Commands.UndoList.Count == n, "AS7 clear: undone to the cleared block");
            for (int i = 0; i < 4; i++) Kit.Ok(t, s.Commands.Redo(), "AS7 clear: redo " + i);
            t.Equal(fresh, Block(s).ToJson(), "AS7 clear: redone");
            for (int i = 0; i < 8; i++) Kit.Ok(t, s.Commands.Undo(), "AS7 clear: undo through the clears " + i);
            t.Check(Block(s).Bones["a"].Pos.Equals(Up) && Block(s).Chains["other/r"].Weight == 0.5f && Block(s).Bust.HipL.Enabled == false && Block(s).BlendShapes["head/h"].Weights["s"] == 1f, "AS7 clear: undoing the clears brings the old values back");
        }

        // The JSON text without layout, for comparisons.
        static string Canon(string json) => Newtonsoft.Json.Linq.JToken.Parse(json).ToString(Newtonsoft.Json.Formatting.None);

        static void AS7Paths(TestRunner t)
        {
            var s = Kit.GoldenSession(t);
            var m = s.Model;
            const string bone = "chara.advancedPose.bones[\"a\"]", chain = "chara.advancedPose.chains[\"other/r\"]", blend = "chara.advancedPose.blendShapes[\"head/h\"]";
            t.Equal(bone, AdvancedPosePaths.Bone("a"), "AS7 paths: a bone's path");
            t.Equal(chain, AdvancedPosePaths.Chain("other/r"), "AS7 paths: a chain's path");
            t.Equal(blend, AdvancedPosePaths.Blend("head/h"), "AS7 paths: a renderer's path");
            t.Equal("chara.advancedPose.bust.hipR", AdvancedPosePaths.BustChain("hipR"), "AS7 paths: a bust chain's path");
            t.Equal("chara.advancedPose.bones[\"q\\\"x\"]", AdvancedPosePaths.Bone("q\"x"), "AS7 paths: a key is quoted");
            t.Check(AdvancedPosePaths.Touches("chara.advancedPose") && AdvancedPosePaths.Touches(bone + ".pos") && AdvancedPosePaths.Touches("chara") && !AdvancedPosePaths.Touches("chara.advancedPoses") && !AdvancedPosePaths.Touches("chara.fk.bones"), "AS7 paths: Touches");

            // Reads of an empty block.
            t.Check(FieldAccessor.Get(m, Node, "chara.advancedPose") is CharaAdvancedPose, "AS7 paths: the block is reachable");
            t.Check(FieldValue.IsAbsent(FieldAccessor.Get(m, Node, bone)), "AS7 paths: a missing key reads as absent");
            t.Equal("{}", Canon(FieldAccessor.GetJson(m, Node, "chara.advancedPose.bones")), "AS7 paths: an empty map's JSON");
            t.Equal(typeof(BoneCorrection), FieldAccessor.LeafType(m, Node, bone), "AS7 paths: an entry's type");

            // Whole entries and maps, typed and as JSON.
            Kit.Ok(t, s.SetJson(Node, bone, "{\"pos\": [1, 2, 3], \"rot\": [0, 90, 0], \"scale\": [1, 1, 2]}"), "AS7 paths: a bone from JSON");
            t.Check(Block(s).Bones["a"].Pos.Equals(new Vec3(1f, 2f, 3f)) && Block(s).Bones["a"].Scale.Z == 2f, "AS7 paths: its values");
            t.Equal("[1.0,2.0,3.0]", Canon(FieldAccessor.GetJson(m, Node, bone + ".pos")), "AS7 paths: a channel's JSON");
            t.Equal(typeof(Vec3), FieldAccessor.LeafType(m, Node, bone + ".rot"), "AS7 paths: a channel's type");

            // A channel of an existing entry; undo restores it.
            Kit.Ok(t, s.Set(Node, bone + ".pos", new Vec3(9f, 0f, 0f)), "AS7 paths: a channel");
            t.Check(Block(s).Bones["a"].Pos.X == 9f, "AS7 paths: written");
            Kit.Ok(t, s.Commands.Undo(), "AS7 paths: undo the channel");
            t.Check(Block(s).Bones["a"].Pos.X == 1f, "AS7 paths: restored");
            Kit.Fails(t, s.Set(Node, bone + ".pos", 1f), CommandCodes.BadArgs, "AS7 paths: a float for a channel");
            Kit.Fails(t, s.Set(Node, bone + ".pos", new Vec3(float.NaN, 0f, 0f)), CommandCodes.BadArgs, "AS7 paths: a NaN");
            Kit.Fails(t, s.Set(Node, bone + ".rot", new Vec3(float.PositiveInfinity, 0f, 0f)), CommandCodes.BadArgs, "AS7 paths: an infinity");
            Kit.Fails(t, s.Set(Node, bone + ".nothing", 1f), CommandCodes.BadArgs, "AS7 paths: an unknown member");
            Kit.Fails(t, s.Set(Node, "chara.advancedPose.bones[\"b\"].pos", Up), CommandCodes.NotFound, "AS7 paths: through a missing key");
            Kit.Ok(t, s.Set(Node, bone + ".pos", new Vec3(-0f, 1f, 0f)), "AS7 paths: a minus zero");
            t.Check(BitConverter.ToInt32(BitConverter.GetBytes(Block(s).Bones["a"].Pos.X), 0) == 0, "AS7 paths: stored as plus zero");

            // Chains: each member, null removes it.
            Kit.Ok(t, s.Set(Node, chain, new ChainValues()), "AS7 paths: an empty chain entry");
            void Member(string name, object value, string json, Type type)
            {
                string p = chain + "." + name;
                t.Equal("null", Canon(FieldAccessor.GetJson(m, Node, p)), "AS7 paths: " + name + " is null at first");
                Kit.Ok(t, s.Set(Node, p, value), "AS7 paths: " + name + " typed");
                t.Equal(Canon(json), Canon(FieldAccessor.GetJson(m, Node, p)), "AS7 paths: " + name + "'s JSON");
                t.Check(Block(s).ToJObject()["chains"]["other/r"][name] != null, "AS7 paths: " + name + " is written");
                t.Equal(type, FieldAccessor.LeafType(m, Node, p), "AS7 paths: " + name + "'s type");
                Kit.Ok(t, s.Set(Node, p, null), "AS7 paths: " + name + " null");
                t.Check(Block(s).ToJObject()["chains"]["other/r"][name] == null, "AS7 paths: " + name + " is removed by a null");
                Kit.Ok(t, s.SetJson(Node, p, json), "AS7 paths: " + name + " from JSON");
                t.Equal(Canon(json), Canon(FieldAccessor.GetJson(m, Node, p)), "AS7 paths: " + name + " again");
                Kit.Ok(t, s.SetJson(Node, p, "null"), "AS7 paths: " + name + " null from JSON");
                t.Equal("null", Canon(FieldAccessor.GetJson(m, Node, p)), "AS7 paths: " + name + " removed");
            }
            Member("enabled", true, "true", typeof(bool?));
            Member("weight", 0.5f, "0.5", typeof(float?));
            Member("damping", 0.25f, "0.25", typeof(float?));
            Member("elasticity", 0.75f, "0.75", typeof(float?));
            Member("stiffness", 1f, "1.0", typeof(float?));
            Member("inert", 0f, "0.0", typeof(float?));
            Member("radius", 2f, "2.0", typeof(float?));
            Member("gravity", Up, "[0.0,1.0,0.0]", typeof(Vec3?));
            Member("force", Up, "[0.0,1.0,0.0]", typeof(Vec3?));
            Member("freezeAxis", 2, "2", typeof(int?));
            Kit.Fails(t, s.Set(Node, chain + ".weight", "x"), CommandCodes.BadArgs, "AS7 paths: a string for a weight");
            Kit.Fails(t, s.SetJson(Node, chain + ".enabled", "1"), CommandCodes.BadArgs, "AS7 paths: a number for enabled");
            Kit.Fails(t, s.SetJson(Node, chain + ".freezeAxis", "1.5"), CommandCodes.BadArgs, "AS7 paths: a fraction for an axis");

            // Bust: every chain, every member; never null.
            foreach (var name in BustSet.Names)
            {
                string p = AdvancedPosePaths.BustChain(name);
                t.Equal(typeof(BustValues), FieldAccessor.LeafType(m, Node, p), "AS7 paths: " + name);
                Kit.Ok(t, s.Set(Node, p + ".gravity", Up), "AS7 paths: " + name + ".gravity");
                Kit.Ok(t, s.Set(Node, p + ".enabled", false), "AS7 paths: " + name + ".enabled");
                Kit.Ok(t, s.Set(Node, p + ".inert", 0.5f), "AS7 paths: " + name + ".inert");
                t.Check(Block(s).Bust[BustSet.KindOf(name)].Gravity.Value.Equals(Up) && Block(s).Bust[BustSet.KindOf(name)].Enabled == false, "AS7 paths: " + name + " holds them");
                Kit.Fails(t, s.Set(Node, p, null), CommandCodes.BadArgs, "AS7 paths: " + name + " is never null");
                Kit.Fails(t, s.Set(Node, p + ".weight", 0.5f), CommandCodes.BadArgs, "AS7 paths: a bust chain has no weight");
            }
            Kit.Fails(t, s.Set(Node, "chara.advancedPose.bust.breastX.inert", 0.5f), CommandCodes.BadArgs, "AS7 paths: no such bust chain");
            Kit.Fails(t, s.Set(Node, "chara.advancedPose.bust", null), CommandCodes.BadArgs, "AS7 paths: bust is never null");

            // Blend weights: a key of a map inside an entry.
            Kit.Ok(t, s.SetJson(Node, blend, "{\"weights\": {\"s\": 10}}"), "AS7 paths: a renderer from JSON");
            Kit.Ok(t, s.Set(Node, blend + ".weights[\"t\"]", 20f), "AS7 paths: a new weight");
            t.Check(Block(s).BlendShapes["head/h"].Weights["t"] == 20f && Block(s).BlendShapes["head/h"].Weights["s"] == 10f, "AS7 paths: stored");
            Kit.Ok(t, s.Commands.Undo(), "AS7 paths: undo the new weight");
            t.Check(!Block(s).BlendShapes["head/h"].Weights.ContainsKey("t"), "AS7 paths: its key is gone");
            Kit.Ok(t, s.Commands.Redo(), "AS7 paths: redo the new weight");
            t.Equal("20.0", Canon(FieldAccessor.GetJson(m, Node, blend + ".weights[\"t\"]")), "AS7 paths: a weight's JSON");
            t.Equal(typeof(float), FieldAccessor.LeafType(m, Node, blend + ".weights[\"t\"]"), "AS7 paths: a weight's type");
            Kit.Fails(t, s.Set(Node, blend + ".weights[\"t\"]", "x"), CommandCodes.BadArgs, "AS7 paths: a string for a weight");
            Kit.Fails(t, s.Set(Node, "chara.advancedPose.blendShapes[\"head/none\"].weights[\"t\"]", 1f), CommandCodes.NotFound, "AS7 paths: a weight of no renderer");

            // The maps as wholes; the block as a whole; what may not be null.
            var map = new SortedDictionary<string, BoneCorrection>(StringComparer.Ordinal) { ["m1"] = new BoneCorrection(Up, Vec3.Zero, Vec3.One), ["m2"] = new BoneCorrection(Vec3.Zero, Up, Vec3.One) };
            Kit.Ok(t, s.Set(Node, AdvancedPosePaths.Bones, map), "AS7 paths: a whole map");
            t.Check(Block(s).Bones.Count == 2 && Block(s).Bones["m2"].Rot.Equals(Up) && !ReferenceEquals(Block(s).Bones["m1"], map["m1"]), "AS7 paths: stored as a copy");
            Kit.Fails(t, s.Set(Node, AdvancedPosePaths.Bones, null), CommandCodes.BadArgs, "AS7 paths: a map is never null");
            Kit.Fails(t, s.Set(Node, bone, null), CommandCodes.BadArgs, "AS7 paths: an entry is never null");
            string json0 = Json(s);
            Kit.Ok(t, s.Set(Node, "chara.advancedPose", new CharaAdvancedPose()), "AS7 paths: a whole block");
            t.Check(Block(s).IsEmpty, "AS7 paths: emptied");
            Kit.Ok(t, s.Commands.Undo(), "AS7 paths: undo");
            t.Equal(json0, Json(s), "AS7 paths: its JSON back");
            Kit.Fails(t, s.Set(Node, "chara.advancedPose", null), CommandCodes.BadArgs, "AS7 paths: the block is never null");
            Kit.Fails(t, s.SetJson(Node, "chara.advancedPose", "null"), CommandCodes.BadArgs, "AS7 paths: nor from JSON");

            // Unknown members: kept by whole-entry writes, not addressable.
            var unknown = CharaAdvancedPose.Parse("{\"future\": 1, \"bones\": {\"u\": {\"pos\": [1, 0, 0], \"keep\": true}}}");
            Kit.Ok(t, s.Set(Node, "chara.advancedPose", unknown), "AS7 paths: a block with unknown members");
            t.Equal(unknown.ToJson(), Json(s), "AS7 paths: stored with them");
            Kit.Fails(t, s.Set(Node, "chara.advancedPose.future", 1f), CommandCodes.BadArgs, "AS7 paths: an unknown member is not addressable");
            Kit.Ok(t, Op(s, LabelBone, AdvancedPoseEdits.SetBone(Block(s), "u", null, Up, null)), "AS7 paths: an edit of that entry");
            t.Check(Block(s).ToJObject()["bones"]["u"]["keep"] != null && Block(s).ToJObject()["future"] != null, "AS7 paths: the entry's and the block's unknown members survive it");

            // Another kind of node.
            Kit.Fails(t, s.Set(Kit.GoldenItem, bone, new BoneCorrection()), CommandCodes.WrongKind, "AS7 paths: an item has no block");
            t.Check(!FieldAccessor.TryGet(m, Kit.GoldenItem, "chara.advancedPose", out _, out string code) && code == CommandCodes.WrongKind, "AS7 paths: reading one answers wrong_kind");
            Kit.Valid(t, s, "AS7 paths: after all of it");
        }

        static void AS7Scene(TestRunner t)
        {
            // A save and a load keep the block; a duplicate carries it; a new scene drops it.
            var s = Kit.GoldenSession(t, true);
            var block = CharaAdvancedPose.Parse("{\"bones\": {\"cf_J_Hips\": {\"pos\": [0, 0.5, 0]}, \"hair:1/a#2\": {\"rot\": [0, 10, 0]}}, \"chains\": {\"other/r\": {\"weight\": 0.5}}, " +
                                                "\"bust\": {\"hipL\": {\"enabled\": false}}, \"blendShapes\": {\"head/h\": {\"weights\": {\"s\": 5}}}}");
            Kit.Ok(t, s.Set(Node, "chara.advancedPose", block), "AS7 scene: a whole block");
            var file = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var man = Newtonsoft.Json.Linq.JObject.Parse(Kit.Text(ContainerReader.Read(file).Get("manifest.json").Data));
            t.Check((int)man["formatVersion"] == 4, "AS7 scene: the save is revision 4");
            var l = SceneContainer.Load(file, new LoadOptions { Registry = new ExtRegistry() });
            t.Check(l.Ok && !l.Findings.Has(AdvancedPoseFindings.BadAddress), "AS7 scene: it loads (" + l + ")");
            if (!l.Ok) return;
            var s2 = Kit.Session(2);
            s2.Bind(l.Value);
            t.Equal(block.ToJson(), Json(s2), "AS7 scene: the block is back, byte-equal");
            t.Check(s2.Commands.UndoList.Count == 0, "AS7 scene: a load records nothing");
            Kit.Ok(t, s2.Duplicate(new[] { Node }), "AS7 scene: a duplicate");
            var copy = s2.Model.PreOrder().First(n => n.Kind == NodeKind.Chara && n.Id != Node);
            t.Equal(block.ToJson(), ((CharaNodeState)copy.Doc.State).AdvancedPose.ToJson(), "AS7 scene: the copy carries the block");
            t.Check(!ReferenceEquals(((CharaNodeState)copy.Doc.State).AdvancedPose, Block(s2)), "AS7 scene: and shares nothing");
            s2.NewScene();
            t.Check(s2.Model.Find(Node) == null, "AS7 scene: a new scene has no node, and no block");
        }
    }
}
