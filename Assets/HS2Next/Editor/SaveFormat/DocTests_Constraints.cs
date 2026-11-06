// The constraints of a scene (scene.json member `constraints`, format revision 4). CM1 an entry read and written: every member, the
// defaults, the order, unknown members at every level, Clone, the normal form, write -> read -> write byte-stable on random lists. CF1
// a scene with no list keeps its bytes (the golden scene, the v0.2 fixture, each revision-4 fixture, a list given as empty or emptied).
// CF2 revision 4 with an entry, the member after `timeline` and before `ext`. CF3 each rule of the reader with its finding, nothing
// dropped, and the type errors that fail a load. CF4 entries over the limit kept, inactive, written back. CF5 the committed fixture
// (Tests/SaveFormat/fixtures/constraints/scene_r4_constraints.hs2nscene: what this writer writes for FixtureScene()) read silently,
// written back byte-equal, and by a revision-3 registry with NewerFormat and the single finding, both top-level members held as
// unknown ones and written back in place (scene.json byte-equal, Downgraded), read back here with the same list and Timeline. CF7
// revision-1, -2, -3 files load with no new finding, also one holding the member. CF8 the last entry removed: no member, the
// lowest covering revision, the bytes of before the first add. Synthetic ids and neutral names only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_Constraints
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string ItemA = Id(0xc1), ItemB = Id(0xc2), Light = Id(0xc3), CharaA = Id(0xc4), CharaB = Id(0xc5), Accessory = Id(0xca);
        const string Member = ConstraintList.MemberName;

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
        static string Names(JToken o) => string.Join(",", ((JObject)o).Properties().Select(p => p.Name));

        static ConstraintEntry Entry(int id, ConstraintEnd source, ConstraintEnd target, params string[] components) =>
            new ConstraintEntry { Id = Id(0x100 + id), Source = source, Target = target, Components = components.ToList() };

        /// <summary>An entry of every target kind, every component and every form of address, none of which gives a finding. Node ids: two
        /// items, a light, two characters.</summary>
        public static List<ConstraintEntry> AllForms()
        {
            var nodeToNode = Entry(1, new ConstraintEnd(ItemA), new ConstraintEnd(ItemB), ConstraintComponents.Position);
            nodeToNode.Offset.Pos = new Vec3(0.25f, -1.5f, 3f);
            var boneToNode = Entry(2, new ConstraintEnd(CharaA, bone: "cf_J_Hand_R"), new ConstraintEnd(Light), ConstraintComponents.Scale, ConstraintComponents.Parent);
            boneToNode.Offset = new ConstraintOffset(new Vec3(1f, 2f, 3f), new Vec3(10f, 20f, 30f), new Vec3(2f, 0.5f, 1f));
            var nodeToBone = Entry(3, new ConstraintEnd(ItemA), new ConstraintEnd(CharaB, bone: "cf_J_Head"), ConstraintComponents.Rotation);
            nodeToBone.Offset.Rot = new Vec3(0f, 90f, 0f);
            nodeToBone.Label = "Head follows the item";
            var boneToIk = Entry(4, new ConstraintEnd(CharaA, bone: "cf_J_Hand_R"), new ConstraintEnd(CharaB, ik: "LeftHand"), ConstraintComponents.Position, ConstraintComponents.Rotation);
            boneToIk.Weight = 0.25f;
            var hairToClothes = Entry(5, new ConstraintEnd(CharaA, bone: "hair:0/bone_a"), new ConstraintEnd(CharaB, bone: "clothes:1/bone_b#2"), ConstraintComponents.LookAt);
            hairToClothes.On = false;
            var accToIk = Entry(6, new ConstraintEnd(CharaA, bone: "acc:" + Accessory + "/bone_c"), new ConstraintEnd(CharaB, ik: "RightFoot"), ConstraintComponents.Position, ConstraintComponents.LookAt);
            accToIk.Label = "Pied gauche \u00e9 \u8db3 \"q\" \\ \U0001F600";
            var itemBone = Entry(7, new ConstraintEnd(ItemB, bone: "item_bone#2"), new ConstraintEnd(ItemA), ConstraintComponents.Position, ConstraintComponents.Rotation, ConstraintComponents.Scale);
            itemBone.Offset = new ConstraintOffset(new Vec3(0.1f, 0.2f, 0.3f), new Vec3(359.5f, 0f, -45f), new Vec3(1.5f, 1.5f, 1.5f));
            var ikBody = Entry(8, new ConstraintEnd(ItemA), new ConstraintEnd(CharaA, ik: "Body"), ConstraintComponents.Parent);
            return new List<ConstraintEntry> { nodeToNode, boneToNode, nodeToBone, boneToIk, hairToClothes, accToIk, itemBone, ikBody };
        }

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            CM1(t);
            CF1(t);
            CF2(t);
            CF3(t);
            CF4(t);
            CF5(t);
            CF7(t);
            CF8(t);
        }

        static SceneDocument Doc(ConstraintList list)
        {
            var d = new SceneDocument { Constraints = list };
            d.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "test.mod#1" }));
            d.Nodes.Add(new SceneNode(ItemB, new ItemNodeState { Ref = "test.mod#2" }));
            d.Nodes.Add(new SceneNode(Light, new LightNodeState { Ref = "illusion.hs2#0" }));
            d.Nodes.Add(new SceneNode(CharaA, new CharaNodeState()));
            d.Nodes.Add(new SceneNode(CharaB, new CharaNodeState()));
            return d;
        }

        // ---------------- CM1 ----------------

        static void CM1(TestRunner t)
        {
            var doc = Doc(new ConstraintList(AllForms()));
            foreach (var e in AllForms()) t.Check(e.BadReason() == null && e.Problem() == null, "CM1: entry " + e.Id + " is usable (" + e.Problem() + ")");
            var b1 = DocIo.Bytes(doc);
            var f = new FindingList();
            var back = DocIo.Scene(b1, f);
            var b2 = DocIo.Bytes(back);
            t.Check(b1.SequenceEqual(b2), "CM1: write -> read -> write byte-stable\n    " + DocIo.FirstDiff(b1, b2));
            t.Check(f.Count == 0, "CM1: the written list reads back with no finding (" + f + ")");
            t.Equal(AllForms().Count, back.Constraints.Count, "CM1: every entry is read");
            t.Equal(string.Join(",", AllForms().Select(e => e.Id)), string.Join(",", back.Constraints.Entries.Select(e => e.Id)), "CM1: list order kept");

            var j = DocReader.Parse(b1, SceneDocument.EntryName);
            var a = (JArray)j[Member];
            foreach (var e in a) t.Equal("id,label,on,weight,source,target,components,offset", Names(e), "CM1: an entry's members in schema order, every member written");
            foreach (var e in a) t.Equal("pos,rot,scale", Names(e["offset"]), "CM1: every member of the offset written");
            t.Equal("node", Names(a[0]["source"]), "CM1: a node end is the node alone");
            t.Equal("node,bone", Names(a[1]["source"]), "CM1: a bone end");
            t.Equal("node,bone", Names(a[2]["target"]), "CM1: a bone target");
            t.Equal("node,ik", Names(a[3]["target"]), "CM1: an IK target");
            t.Check(a[3]["target"]["bone"] == null && a[1]["target"]["ik"] == null, "CM1: bone and ik only when present");
            t.Equal("scale,parent", string.Join(",", a[1]["components"].Select(c => (string)c)), "CM1: components in writing order");
            t.Check(!(bool)a[4]["on"] && (float)a[3]["weight"] == 0.25f && (string)a[2]["label"] == "Head follows the item", "CM1: on, weight, label kept");
            t.Equal("acc:" + Accessory + "/bone_c", (string)a[5]["source"]["bone"], "CM1: an accessory's address");
            t.Equal("clothes:1/bone_b#2", (string)a[4]["target"]["bone"], "CM1: a clothes address with an occurrence");
            t.Equal(AllForms()[5].Label, (string)a[5]["label"], "CM1: a label of any text");

            // The exact text of one entry.
            var small = new ConstraintEntry { Id = Id(1), Source = new ConstraintEnd(ItemA, bone: "cf_J_Hand_R"), Target = new ConstraintEnd(CharaA, ik: "LeftHand"), Components = { ConstraintComponents.Rotation, ConstraintComponents.Position } };
            string expected = "{\n  \"id\": \"" + Id(1) + "\",\n  \"label\": \"\",\n  \"on\": true,\n  \"weight\": 1.0,\n" +
                "  \"source\": {\n    \"node\": \"" + ItemA + "\",\n    \"bone\": \"cf_J_Hand_R\"\n  },\n" +
                "  \"target\": {\n    \"node\": \"" + CharaA + "\",\n    \"ik\": \"LeftHand\"\n  },\n" +
                "  \"components\": [\n    \"position\",\n    \"rotation\"\n  ],\n" +
                "  \"offset\": {\n    \"pos\": [0.0, 0.0, 0.0],\n    \"rot\": [0.0, 0.0, 0.0],\n    \"scale\": [1.0, 1.0, 1.0]\n  }\n}";
            t.Equal(expected, small.ToJson(), "CM1: the canonical text (schema order, one-line vectors, a component per line)");
            t.Equal("rotation,position", string.Join(",", small.Components), "CM1: the writer sorts a copy, the entry's list is not changed");
            t.Equal("[\n  " + expected.Replace("\n", "\n  ") + "\n]", new ConstraintList(new[] { small }).ToJson(), "CM1: the list's text is the array of them");

            // Defaults: only what is required, and a component.
            string min = "[{\"id\": \"" + Id(1) + "\", \"source\": {\"node\": \"" + ItemA + "\"}, \"target\": {\"node\": \"" + ItemB + "\"}, \"components\": [\"position\"]}]";
            var mf = new FindingList();
            var m = ConstraintList.Parse(min, mf).Entries[0];
            t.Check(mf.Count == 0 && m.Label == "" && m.On && m.Weight == 1f && m.Source.IsNode && m.Target.IsNode, "CM1: absent label, on, weight: \"\", true, 1 (" + mf + ")");
            t.Check(m.Offset.Pos.Equals(Vec3.Zero) && m.Offset.Rot.Equals(Vec3.Zero) && m.Offset.Scale.Equals(Vec3.One), "CM1: absent offset is the identity");
            var fresh = new ConstraintEntry();
            t.Check(fresh.Label == "" && fresh.On && fresh.Weight == 1f && fresh.Components.Count == 0 && fresh.Offset.Scale.Equals(Vec3.One), "CM1: a new entry holds the same defaults");
            t.Check(ConstraintList.Parse("[]") == null && new ConstraintList().Count == 0, "CM1: an empty array reads as no list");

            // Components as a file gives them: written in order, on the entry too.
            var c = ConstraintList.Parse("[{\"id\": \"" + Id(1) + "\", \"source\": {\"node\": \"" + ItemA + "\"}, \"target\": {\"node\": \"" + ItemB + "\"}, \"components\": [\"scale\", \"rotation\", \"position\"]}]").Entries[0];
            t.Equal("position,rotation,scale", string.Join(",", c.Components), "CM1: a file's components read into writing order");
            t.Equal("position,rotation,scale,lookAt,parent", string.Join(",", ConstraintComponents.All), "CM1: the writing order");

            // Unknown members at every level, after the known ones.
            var withUnknown = (JObject)j.DeepClone();
            var first = (JObject)withUnknown[Member][1];
            first.Add("future", JToken.Parse("{\"a\": 1.50}"));
            ((JObject)first["source"]).Add("futureSource", 1);
            ((JObject)first["source"]).Add("ik", "LeftHand");
            ((JObject)first["target"]).Add("futureTarget", 2);
            ((JObject)first["offset"]).Add("futureOffset", new JArray(1, 2));
            var uf = new FindingList();
            var ud = DocIo.Scene(CanonicalJsonWriter.ToBytes(withUnknown, SceneDocument.EntryName), uf);
            t.Check(uf.Count == 0, "CM1: unknown members give no finding (" + uf + ")");
            var ue = ud.Constraints.Entries[1];
            t.Check(ue.Source.Ik == null && ue.Source.Unknown.Get("ik") != null, "CM1: `ik` on a source is an unknown member, kept");
            var uj = (JObject)DocReader.Parse(DocIo.Bytes(ud), SceneDocument.EntryName)[Member][1];
            t.Equal("id,label,on,weight,source,target,components,offset,future", Names(uj), "CM1: an entry's unknown member after the known ones");
            t.Equal("node,bone,futureSource,ik", Names(uj["source"]), "CM1: a source's");
            t.Equal("node,futureTarget", Names(uj["target"]), "CM1: a target's");
            t.Equal("pos,rot,scale,futureOffset", Names(uj["offset"]), "CM1: the offset's");
            t.Check(JToken.DeepEquals(uj["future"], JToken.Parse("{\"a\": 1.50}")), "CM1: an unknown member's value kept");
            var cl = ue.Clone();
            t.Equal(ue.ToJson(), cl.ToJson(), "CM1: Clone keeps every member, the unknown ones too");

            // Clone shares nothing.
            var before = ue.ToJson();
            cl.Source.Node = Id(9);
            cl.Target.Bone = "x";
            cl.Components.Add(ConstraintComponents.Rotation);
            cl.Offset.Pos = new Vec3(9f, 9f, 9f);
            cl.Unknown.Remove("future");
            cl.Source.Unknown.Remove("ik");
            t.Equal(before, ue.ToJson(), "CM1: a clone shares nothing with the entry");
            var deep = ud.Constraints.Clone();
            deep.Entries[0].Label = "changed";
            t.Check(ud.Constraints.Entries[0].Label == "" && !ReferenceEquals(deep.Entries[1], ud.Constraints.Entries[1]), "CM1: nor does a clone of the list");
            var replaced = ud.Constraints.With(0, cl);
            t.Check(ReferenceEquals(replaced.Entries[0], cl) && ReferenceEquals(replaced.Entries[1], ud.Constraints.Entries[1]) && !ReferenceEquals(replaced.Entries[0], ud.Constraints.Entries[0]), "CM1: With gives a new list, sharing the others");

            // Queries.
            var list = new ConstraintList(AllForms());
            t.Check(list.IndexOf(Id(0x103)) == 2 && list.Find(Id(0x103)).Label != "" && list.IndexOf(Id(5)) < 0 && list.Find(null) == null, "CM1: IndexOf and Find");
            t.Check(list.Entries[0].UsesPos && !list.Entries[0].UsesRot && !list.Entries[0].UsesScale, "CM1: position uses pos");
            t.Check(!list.Entries[2].UsesPos && list.Entries[2].UsesRot && !list.Entries[2].UsesScale, "CM1: rotation uses rot");
            t.Check(list.Entries[4].UsesRot && !list.Entries[4].UsesPos, "CM1: lookAt uses rot");
            t.Check(list.Entries[1].UsesPos && list.Entries[1].UsesRot && list.Entries[1].UsesScale, "CM1: parent uses pos and rot, scale uses scale");
            t.Check(new ConstraintEnd(ItemA).SameAs(new ConstraintEnd(ItemA)) && !new ConstraintEnd(ItemA).SameAs(new ConstraintEnd(ItemA, bone: "a")) && !new ConstraintEnd(ItemA, ik: "Body").SameAs(new ConstraintEnd(ItemB, ik: "Body")) && !new ConstraintEnd(ItemA).SameAs(null), "CM1: SameAs");
            t.Check(new ConstraintEnd(ItemA).IsNode && !new ConstraintEnd(ItemA, bone: "a").IsNode && !new ConstraintEnd(ItemA, ik: "Body").IsNode, "CM1: IsNode");
            foreach (var (w, applied) in new[] { (0f, 0f), (1f, 1f), (0.5f, 0.5f), (-1f, 0f), (2f, 1f) })
                t.Equal(applied, new ConstraintEntry { Weight = w }.AppliedWeight, "CM1: AppliedWeight of " + w);
            t.Equal("LeftHand,RightHand,LeftFoot,RightFoot", string.Join(",", IkTargets.Names.Where(ConstraintEntry.IkTakesRotation).OrderBy(n => n.Contains("Foot")).ThenBy(n => n.StartsWith("Right"))), "CM1: hands and feet take a rotation");

            // Pairs.
            foreach (var x in ConstraintComponents.All)
                foreach (var y in ConstraintComponents.All)
                {
                    bool refused = (x == "parent" && (y == "position" || y == "rotation" || y == "lookAt")) || (y == "parent" && (x == "position" || x == "rotation" || x == "lookAt")) ||
                                   (x == "lookAt" && y == "rotation") || (x == "rotation" && y == "lookAt");
                    t.Equal(refused, ConstraintComponents.Refused(x, y), "CM1: " + x + " with " + y);
                }
            t.Check(ConstraintComponents.IsKnown("lookAt") && !ConstraintComponents.IsKnown("lookat") && !ConstraintComponents.IsKnown(null), "CM1: IsKnown is ordinal");
            t.Equal("position,parent,x,y", string.Join(",", ConstraintComponents.Ordered(new[] { "x", "parent", "y", "position" })), "CM1: Ordered: unknown ones last, in their order");

            // The normal form an edit stores.
            var normal = AllForms()[0];
            t.Check(ReferenceEquals(normal, normal.Normalized()), "CM1: an entry in its normal form is returned as it is");
            var off = AllForms()[0];
            off.Components = new List<string> { ConstraintComponents.Rotation, ConstraintComponents.Position };
            off.Offset = new ConstraintOffset(new Vec3(1f, 0f, 0f), new Vec3(5f, 0f, 0f), new Vec3(2f, 2f, 2f));
            string offText = off.ToJson();
            var nf = off.Normalized();
            t.Check(!ReferenceEquals(off, nf) && off.ToJson() == offText && off.Components[0] == ConstraintComponents.Rotation, "CM1: Normalized changes a copy, not the entry");
            t.Equal("position,rotation", string.Join(",", nf.Components), "CM1: components in writing order");
            t.Check(nf.Offset.Pos.X == 1f && nf.Offset.Rot.X == 5f && nf.Offset.Scale.Equals(Vec3.One), "CM1: an offset no component uses is the identity, a used one stays");
            var only = AllForms()[0];
            only.Components = new List<string> { ConstraintComponents.Scale };
            only.Offset = new ConstraintOffset(new Vec3(1f, 0f, 0f), new Vec3(5f, 0f, 0f), new Vec3(2f, 2f, 2f));
            var no = only.Normalized();
            t.Check(no.Offset.Pos.Equals(Vec3.Zero) && no.Offset.Rot.Equals(Vec3.Zero) && no.Offset.Scale.X == 2f, "CM1: scale alone keeps the scale only");
            var future = AllForms()[0];
            future.Components = new List<string> { "future", ConstraintComponents.Position };
            future.Offset.Rot = new Vec3(5f, 0f, 0f);
            var fn = future.Normalized();
            t.Check(fn.Offset.Rot.X == 5f && fn.Components[0] == ConstraintComponents.Position && fn.Components[1] == "future", "CM1: with an unknown component, which may use it, no offset is cleared");

            // Problem(): what an edit may not introduce. A file's weight and label are the reader's.
            ConstraintEntry Good() => AllForms()[3];
            void Bad(Action<ConstraintEntry> edit, string contains, string what)
            {
                var e = Good();
                edit(e);
                string p = e.Problem();
                t.Check(p != null && p.Contains(contains), "CM1: Problem: " + what + " (" + p + ")");
            }
            t.Check(Good().Problem() == null, "CM1: Problem: none on a good entry");
            Bad(e => e.Id = null, "id", "no id");
            Bad(e => e.Id = "abc", "id", "a bad id");
            Bad(e => e.Source = null, "source", "no source");
            Bad(e => e.Target = null, "target", "no target");
            Bad(e => e.Source.Node = "x", "source", "a bad source node");
            Bad(e => e.Target.Node = null, "target", "no target node");
            Bad(e => e.Source.Ik = "LeftHand", "IK", "an IK target as source");
            Bad(e => e.Weight = 1.5f, "weight", "a weight over 1");
            Bad(e => e.Weight = -0.1f, "weight", "a weight under 0");
            Bad(e => e.Weight = float.NaN, "weight", "a NaN weight");
            Bad(e => e.Label = null, "label", "a null label");
            Bad(e => e.Label = new string('x', 257), "label", "a label over the limit");
            Bad(e => e.Offset = null, "offset", "no offset");
            Bad(e => e.Offset.Pos = new Vec3(float.NaN, 0f, 0f), "pos", "a NaN offset");
            Bad(e => e.Offset.Rot = new Vec3(0f, float.PositiveInfinity, 0f), "rot", "an infinite offset");
            Bad(e => e.Offset.Scale = new Vec3(0f, 0f, 2e6f), "scale", "a very large offset");
            Bad(e => e.Components.Clear(), "no component", "no component");
            Bad(e => e.Components = null, "no component", "a null set");
            var edge = Good();
            edge.Label = new string('x', 256);
            edge.Weight = 0f;
            edge.Offset.Pos = new Vec3(1e6f, -1e6f, 0f);
            t.Check(edge.Problem() == null, "CM1: Problem: the limits themselves are allowed (" + edge.Problem() + ")");

            // Random lists.
            int stable = 0;
            const int seeds = 300;
            for (int seed = 0; seed < seeds; seed++)
            {
                var g = new Random(seed);
                var d = Doc(RandomList(g));
                var rb = DocIo.Bytes(d);
                var rf = new FindingList();
                var again = DocIo.Bytes(DocIo.Scene(rb, rf));
                if (rb.SequenceEqual(again) && rf.Count == 0) stable++;
                else t.Info("seed " + seed + ": " + rf);
            }
            t.Equal(seeds, stable, "CM1: random lists are byte-stable, with no finding");
        }

        static readonly string[][] Sets =
        {
            new[] { "position" }, new[] { "rotation" }, new[] { "scale" }, new[] { "lookAt" }, new[] { "parent" },
            new[] { "position", "rotation" }, new[] { "position", "lookAt" }, new[] { "position", "rotation", "scale" }, new[] { "position", "scale", "lookAt" },
            new[] { "rotation", "scale" }, new[] { "scale", "lookAt" }, new[] { "scale", "parent" },
        };

        static ConstraintList RandomList(Random g)
        {
            float F() => (float)(g.NextDouble() * 20.0 - 10.0);
            string[] nodes = { ItemA, ItemB, Light, CharaA, CharaB };
            string Address()
            {
                switch (g.Next(5))
                {
                    case 0: return "cf_J_Bone" + g.Next(50);
                    case 1: return "hair:" + g.Next(4) + "/b" + g.Next(9);
                    case 2: return "clothes:" + g.Next(8) + "/b" + g.Next(9) + "#" + (1 + g.Next(4));
                    case 3: return "acc:" + Id(g.Next(1 << 20)) + "/b" + g.Next(9);
                    default: return "name with space \u00e9#3";
                }
            }
            var l = new ConstraintList();
            for (int i = 1 + g.Next(12); i > 0; i--)
            {
                var e = new ConstraintEntry
                {
                    Id = Id(0x1000 + l.Count),
                    Label = g.Next(2) == 0 ? "" : "l" + g.Next(50) + (g.Next(4) == 0 ? "\u00e9'\"\\" : ""),
                    On = g.Next(2) == 0,
                    Weight = g.Next(3) == 0 ? 1f : (float)g.NextDouble(),
                    Source = new ConstraintEnd(nodes[g.Next(nodes.Length)], bone: g.Next(2) == 0 ? null : Address()),
                    Components = Sets[g.Next(Sets.Length)].ToList(),
                    Offset = new ConstraintOffset(new Vec3(F(), F(), F()), new Vec3(F() * 36f, F() * 36f, F() * 36f), new Vec3(F(), F(), F())),
                };
                bool ikUsable = !e.Has(ConstraintComponents.Scale);
                e.Target = g.Next(3) == 0 ? new ConstraintEnd(nodes[g.Next(nodes.Length)]) :
                           ikUsable && g.Next(2) == 0 ? new ConstraintEnd(nodes[g.Next(nodes.Length)], ik: IkTargets.Names.Where(ConstraintEntry.IkTakesRotation).ElementAt(g.Next(4))) :
                           new ConstraintEnd(nodes[g.Next(nodes.Length)], bone: Address());
                l.Entries.Add(e);
            }
            return l;
        }

        // ---------------- CF1 ----------------

        static void CF1(TestRunner t)
        {
            var golden = DocTests_T1Golden.Golden("scene.json");
            var doc = DocIo.Scene(golden);
            t.Check(doc.Constraints == null, "CF1: the golden scene reads with no list");
            t.Check(DocIo.Same(golden, DocIo.Bytes(doc)) && !Text(golden).Contains(Member), "CF1: the golden scene is written byte for byte, without the member");

            string v02 = System.IO.Path.Combine(FixtureWriter.V02Dir(t.FixturesDir ?? ""), "scene.hs2nscene");
            if (System.IO.File.Exists(v02))
            {
                var file = System.IO.File.ReadAllBytes(v02);
                var l = SceneContainer.Load(file, LOpts());
                var old = SceneContainer.Load(file, LOpts(Registry3()));
                t.Check(l.Ok && old.Ok && l.Findings.ToString() == old.Findings.ToString(), "CF1: the v0.2 fixture scene loads with the findings of a revision-3 reader (" + l + ")");
                if (l.Ok)
                {
                    var re = l.Value.ToBytes(Opts());
                    t.Check(FormatOf(re) == 1 && Same(EntryBytes(file, "scene.json"), EntryBytes(re, "scene.json")), "CF1: re-saved as format 1, scene.json byte-equal");
                }
            }
            else t.Check(false, "CF1: fixture " + v02 + " present");

            foreach (var (name, path, options, findings) in new[]
            {
                ("the Timeline fixture", DocTests_Timeline.R4Path(t.FixturesDir), DocTests_Timeline.R4Options(), 0),
                ("the advanced-pose fixture", DocTests_AdvancedPose.PosePath(t.FixturesDir), DocTests_AdvancedPose.PoseOptions(), 0),
                ("the material-edit fixture", DocTests_MaterialEdit.MaterialsPath(t.FixturesDir), DocTests_MaterialEdit.MaterialsOptions(), 2),
            })
            {
                if (!System.IO.File.Exists(path)) { t.Check(false, "CF1: fixture " + path + " present"); continue; }
                var file = System.IO.File.ReadAllBytes(path);
                var l = SceneContainer.Load(file, LOpts());
                t.Check(l.Ok && l.Findings.Count == findings && l.Value.Scene.Constraints == null, "CF1: " + name + " reads with no new finding and no list (" + l + ")");
                if (!l.Ok) continue;
                var re = l.Value.ToBytes(options);
                t.Check(FormatOf(re) == 4 && Same(EntryBytes(file, "scene.json"), EntryBytes(re, "scene.json")) && !Text(EntryBytes(re, "scene.json")).Contains("\"" + Member + "\""), "CF1: " + name + " stays byte-equal, at revision 4, without the member");
            }

            // Revisions 1, 2, 3 written as they were: the same bytes with no list, an empty one, one emptied.
            var s = InfraFixtures.SmallScene(1, 2);
            var b1 = s.ToBytes(Opts());
            t.Check(FormatOf(b1) == 1 && !Text(EntryBytes(b1, "scene.json")).Contains(Member), "CF1: a scene without a list has no member, format 1");
            s.Scene.Constraints = new ConstraintList();
            t.Check(Same(b1, s.ToBytes(Opts())), "CF1: an empty list changes no byte");
            s.Scene.Constraints = null;
            ((CharaNodeState)s.Scene.Nodes[0].State).State.NippleThroughClothes = false;
            var b3 = s.ToBytes(Opts());
            s.Scene.Constraints = new ConstraintList();
            t.Check(FormatOf(b3) == 3 && Same(b3, s.ToBytes(Opts())), "CF1: a revision-3 file too");
            var withExt = InfraFixtures.SmallChara();
            withExt.Coord.Clothes.Parts[0].Ext.Set("stp3.fixture", JToken.Parse("1"));
            var s2 = InfraFixtures.SmallScene(1, 1, withExt);
            var b2 = s2.ToBytes(Opts());
            s2.Scene.Constraints = new ConstraintList();
            t.Check(FormatOf(b2) == 2 && Same(b2, s2.ToBytes(Opts())), "CF1: a revision-2 file too");

            // A hand-made empty member reads and is dropped silently.
            var hand = InfraFixtures.Repack(b1, m => InfraFixtures.EditJson(m, "scene.json", j => j.Add(Member, new JArray())), "scene.json");
            var lh = SceneContainer.Load(hand, LOpts());
            t.Check(lh.Ok && lh.Findings.Count == 0 && lh.Value.Scene.Constraints == null, "CF1: an empty member from a file reads as no list, silently (" + lh + ")");
            if (lh.Ok)
            {
                var again = lh.Value.ToBytes(Opts());
                t.Check(!Text(EntryBytes(again, "scene.json")).Contains(Member) && FormatOf(again) == 1, "CF1: and is not written");
            }

            // Character and coordinate documents do not know the member: for them it is an unknown one, nothing reads it.
            t.Check(!Text(EntryBytes(InfraFixtures.SmallChara().ToBytes(Opts()), "chara.json")).Contains(Member), "CF1: a character file carries none");
        }

        // ---------------- CF2 ----------------

        static void CF2(TestRunner t)
        {
            t.Check(SaveFormats.ConstraintsVersion == 4 && SaveFormats.SupportedVersion == 4 && CoreMigrationRegistry.Default.Supported == 4 && SaveFormats.MinReaderVersion == 1,
                "CF2: the list at revision 4, no new revision (S = 4), minReaderVersion 1");
            var one = new ConstraintList(new[] { AllForms()[0] });
            var s = InfraFixtures.SmallScene(1, 1);
            var b1 = s.ToBytes(Opts());
            s.Scene.Constraints = one;
            var b = s.ToBytes(Opts());
            t.Check(FormatOf(b) == 4 && (int)Entry(b, "manifest.json")["minReaderVersion"] == 1, "CF2: an entry alone: revision 4, minReaderVersion 1");
            t.Equal("nodes,scene,constraints", Names(Entry(b, "scene.json")), "CF2: after scene");
            t.Equal(Entry(b1, "manifest.json")["requires"]?.ToString(), Entry(b, "manifest.json")["requires"]?.ToString(), "CF2: the list adds no requirement");
            s.Scene.Timeline = new TimelineDocument();
            t.Equal("nodes,scene,timeline,constraints", Names(Entry(s.ToBytes(Opts()), "scene.json")), "CF2: after timeline");
            s.Scene.Ext.Set("stp3.fixture", JToken.Parse("{\"a\": 1}"));
            t.Equal("nodes,scene,timeline,constraints,ext", Names(Entry(s.ToBytes(Opts()), "scene.json")), "CF2: before ext");
            s.Scene.Unknown = new JsonLeftovers();
            s.Scene.Unknown.Add("future", JToken.Parse("1"));
            t.Equal("nodes,scene,timeline,constraints,future,ext", Names(Entry(s.ToBytes(Opts()), "scene.json")), "CF2: a top-level unknown member after it, before ext");
            s.Scene.Timeline = null;
            t.Equal("nodes,scene,constraints,future,ext", Names(Entry(s.ToBytes(Opts()), "scene.json")), "CF2: without a Timeline");
            s.Scene.Unknown = null;
            s.Scene.Constraints = null;
            t.Equal("nodes,scene,ext", Names(Entry(s.ToBytes(Opts()), "scene.json")), "CF2: without the list, as before");

            // The highest member decides; clearing brings each back.
            var r = InfraFixtures.SmallScene(1, 1);
            var chara = (CharaNodeState)r.Scene.Nodes[0].State;
            chara.State.NippleThroughClothes = false;
            var b3 = r.ToBytes(Opts());
            r.Scene.Constraints = one;
            t.Check(FormatOf(b3) == 3 && FormatOf(r.ToBytes(Opts())) == 4, "CF2: a revision-3 member and an entry: 4");
            r.Scene.Constraints = null;
            t.Check(Same(r.ToBytes(Opts()), b3), "CF2: the list cleared: back to 3, byte-equal");
            r.Scene.Timeline = new TimelineDocument();
            r.Scene.Constraints = one;
            t.Equal(4, FormatOf(r.ToBytes(Opts())), "CF2: a Timeline and an entry: 4");
            r.Scene.Constraints = null;
            t.Equal(4, FormatOf(r.ToBytes(Opts())), "CF2: the Timeline alone: 4");

            // The writer's guards.
            var d = Doc(new ConstraintList(new ConstraintEntry[] { null }));
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(d), "CF2: a null entry fails the write");
            var noId = AllForms()[0];
            noId.Id = null;
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Bytes(Doc(new ConstraintList(new[] { noId }))), "CF2: an entry without an id fails the write");
            var nan = AllForms()[0];
            nan.Weight = float.NaN;
            t.ThrowsCode(SaveErrorCode.NonFinite, () => DocIo.Bytes(Doc(new ConstraintList(new[] { nan }))), "CF2: a NaN weight fails the write");
        }

        // ---------------- CF3 ----------------

        static string E(string body) => "{\"id\": \"" + Id(1) + "\", \"source\": {\"node\": \"" + ItemA + "\"}, \"target\": {\"node\": \"" + ItemB + "\"}, \"components\": [\"position\"]" + body + "}";

        static string Ends(string source, string target, string components) =>
            "{\"id\": \"" + Id(1) + "\", \"source\": " + source + ", \"target\": " + target + ", \"components\": " + components + "}";

        static byte[] Scene(string constraintsJson)
        {
            var j = DocReader.Parse(DocIo.Bytes(Doc(null)), SceneDocument.EntryName);
            ((JObject)j).Add(Member, DocReader.ParseText(constraintsJson, "test"));
            return CanonicalJsonWriter.ToBytes(j, SceneDocument.EntryName);
        }

        static bool SameDocument(byte[] a, byte[] b) =>
            JToken.DeepEquals(DocReader.Parse(a, SceneDocument.EntryName), DocReader.Parse(b, SceneDocument.EntryName));

        static void CF3(TestRunner t)
        {
            // One entry through the document reader: its findings, kept, written back.
            ConstraintEntry One(string entryJson, string code, FindingSeverity? severity, int findings, string what)
            {
                var f = new FindingList();
                var bytes = Scene("[" + entryJson + "]");
                var d = DocIo.Scene(bytes, f);
                t.Equal(1, d.Constraints?.Count ?? 0, "CF3: " + what + ": the entry is kept");
                t.Equal(findings, f.Count, "CF3: " + what + ": " + findings + " finding(s) (" + f + ")");
                if (code != null)
                {
                    t.Check(f.Has(code), "CF3: " + what + ": finding " + code + " (" + f + ")");
                    t.Check(severity == null || f.All(x => x.Code != code || x.Severity == severity), "CF3: " + what + ": severity " + severity);
                }
                return d.Constraints.Entries[0];
            }
            void WrittenBack(string entryJson, string what)
            {
                var bytes = Scene("[" + entryJson + "]");
                var d = DocIo.Scene(bytes);
                var read = DocReader.Parse(bytes, SceneDocument.EntryName)[Member][0]["components"].Select(c => (string)c).OrderBy(c => c, StringComparer.Ordinal);
                var written = DocReader.Parse(DocIo.Bytes(d), SceneDocument.EntryName)[Member][0]["components"].Select(c => (string)c).OrderBy(c => c, StringComparer.Ordinal);
                t.Equal(string.Join(",", read), string.Join(",", written), "CF3: " + what + ": every string written back");
                var again = DocIo.Bytes(d);
                t.Check(DocIo.Same(again, DocIo.Bytes(DocIo.Scene(again))), "CF3: " + what + ": and stable");
            }

            // A repeated id.
            var rf = new FindingList();
            var rep = DocIo.Scene(Scene("[" + E("") + ", " + E("") + ", " + E("") + "]"), rf);
            t.Equal(3, rep.Constraints.Count, "CF3: repeated ids: no entry dropped");
            t.Equal(2, rf.CountOf(ConstraintFindings.Id), "CF3: repeated ids: one finding each (" + rf + ")");
            t.Check(rf.All(x => x.Severity == FindingSeverity.Info), "CF3: repeated ids: info");
            t.Equal(Id(1), rep.Constraints.Entries[0].Id, "CF3: repeated ids: the first keeps its id");
            t.Equal(JsonText.DerivedId("constraint/1"), rep.Constraints.Entries[1].Id, "CF3: repeated ids: the later gets the derived id of its index");
            t.Equal(JsonText.DerivedId("constraint/2"), rep.Constraints.Entries[2].Id, "CF3: repeated ids: each by its index");
            var clash = E("").Replace(Id(1), JsonText.DerivedId("constraint/1"));
            var cf = new FindingList();
            var cd = DocIo.Scene(Scene("[" + clash + ", " + clash + "]"), cf);
            t.Check(cd.Constraints.Entries[0].Id != cd.Constraints.Entries[1].Id && cf.Count == 1, "CF3: repeated ids: a derived id that is taken is derived again (" + cf + ")");

            // Weight.
            foreach (var (text, stored, applied) in new[] { ("2.5", 2.5f, 1f), ("-0.25", -0.25f, 0f) })
            {
                var e = One(E(", \"weight\": " + text), ConstraintFindings.Range, FindingSeverity.Info, 1, "weight " + text);
                t.Equal(stored, e.Weight, "CF3: weight " + text + ": stored as read");
                t.Equal(applied, e.AppliedWeight, "CF3: weight " + text + ": evaluated clamped");
                t.Equal(stored, (float)DocReader.Parse(DocIo.Bytes(Doc(new ConstraintList(new[] { e }))), SceneDocument.EntryName)[Member][0]["weight"], "CF3: weight " + text + ": written as read");
                t.Check(e.BadReason() == null, "CF3: weight " + text + ": the entry is not bad");
            }
            One(E(", \"weight\": 0"), null, null, 0, "weight 0");
            One(E(", \"weight\": 1"), null, null, 0, "weight 1");

            // Label.
            var ll = One(E(", \"label\": \"" + new string('l', 300) + "\""), ConstraintFindings.Limit, FindingSeverity.Warning, 1, "a long label");
            t.Equal(ConstraintLimits.MaxLabel, ll.Label.Length, "CF3: a long label: shortened to the limit");
            One(E(", \"label\": \"" + new string('l', 256) + "\""), null, null, 0, "a label at the limit");
            var ls = One(E(", \"label\": \"" + new string('l', 255) + "\U0001F600\""), ConstraintFindings.Limit, FindingSeverity.Warning, 1, "a label cut in a surrogate pair");
            t.Check(ls.Label.Length == 255 && !char.IsHighSurrogate(ls.Label[ls.Label.Length - 1]), "CF3: a label is not cut inside a character");

            // Components.
            var unknown = One(Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[\"position\", \"future\"]"), "json.unknownEnum", FindingSeverity.Warning, 1, "an unknown component");
            t.Check(unknown.BadReason() != null && unknown.BadReason().Contains("future"), "CF3: an unknown component: state bad");
            WrittenBack(Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[\"position\", \"future\"]"), "an unknown component");
            var two = new FindingList();
            DocIo.Scene(Scene("[" + Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[\"a\", \"b\"]") + "]"), two);
            t.Equal(2, two.CountOf("json.unknownEnum"), "CF3: two unknown components: one finding each");
            var none = One(Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[]"), ConstraintFindings.BadEntry, FindingSeverity.Warning, 1, "no component");
            t.Check(none.BadReason() != null, "CF3: no component: state bad");
            WrittenBack(Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[]"), "no component");
            var absent = One("{\"id\": \"" + Id(1) + "\", \"source\": {\"node\": \"" + ItemA + "\"}, \"target\": {\"node\": \"" + ItemB + "\"}}", ConstraintFindings.BadEntry, FindingSeverity.Warning, 1, "an absent set");
            t.Check(absent.BadReason() != null, "CF3: an absent set: state bad");
            foreach (var set in new[] { "\"parent\", \"position\"", "\"parent\", \"rotation\"", "\"parent\", \"lookAt\"", "\"lookAt\", \"rotation\"", "\"position\", \"position\"" })
            {
                var e = One(Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[" + set + "]"), ConstraintFindings.BadEntry, FindingSeverity.Warning, 1, "the set " + set);
                t.Check(e.BadReason() != null, "CF3: the set " + set + ": state bad");
                WrittenBack(Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[" + set + "]"), "the set " + set);
            }
            foreach (var set in Sets)
            {
                var e = One(Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", new JArray(set.Cast<object>().ToArray()).ToString(Newtonsoft.Json.Formatting.None)), null, null, 0, "the set " + string.Join("+", set));
                t.Check(e.BadReason() == null, "CF3: the set " + string.Join("+", set) + " is allowed");
            }

            // Target forms.
            string Item = "{\"node\": \"" + ItemA + "\"}";
            var both = One(Ends(Item, "{\"node\": \"" + CharaA + "\", \"bone\": \"cf_J_Head\", \"ik\": \"LeftHand\"}", "[\"position\"]"), ConstraintFindings.BadEntry, FindingSeverity.Warning, 1, "bone and ik both");
            t.Check(both.BadReason() != null && both.Target.Bone != null && both.Target.Ik != null, "CF3: bone and ik both: both kept, state bad");
            t.Equal("node,bone,ik", Names(DocReader.Parse(DocIo.Bytes(Doc(new ConstraintList(new[] { both }))), SceneDocument.EntryName)[Member][0]["target"]), "CF3: bone and ik both: written back");
            var ik = One(Ends(Item, "{\"node\": \"" + CharaA + "\", \"ik\": \"LeftEar\"}", "[\"position\"]"), "json.unknownEnum", FindingSeverity.Warning, 1, "an unknown ik");
            t.Check(ik.BadReason() != null && ik.Target.Ik == "LeftEar", "CF3: an unknown ik: kept, state bad");
            foreach (var (name, set, allowed) in new[]
            {
                ("LeftHand", "\"position\", \"scale\"", false), ("Body", "\"scale\"", false), ("Body", "\"rotation\"", false), ("LeftThigh", "\"lookAt\"", false), ("RightShoulder", "\"position\", \"rotation\"", false),
                ("LeftArmChain", "\"lookAt\"", false), ("Body", "\"position\"", true), ("Body", "\"parent\"", true), ("LeftHand", "\"position\", \"rotation\"", true), ("RightFoot", "\"lookAt\"", true), ("LeftFoot", "\"parent\"", true),
            })
            {
                string json = Ends(Item, "{\"node\": \"" + CharaA + "\", \"ik\": \"" + name + "\"}", "[" + set + "]");
                var e = One(json, allowed ? null : ConstraintFindings.BadEntry, allowed ? (FindingSeverity?)null : FindingSeverity.Warning, allowed ? 0 : 1, name + " with " + set);
                t.Equal(allowed, e.BadReason() == null, "CF3: " + name + " with " + set + (allowed ? " is allowed" : " is bad"));
            }
            foreach (var name in IkTargets.Names)
                t.Check(One(Ends(Item, "{\"node\": \"" + CharaA + "\", \"ik\": \"" + name + "\"}", "[\"position\"]"), null, null, 0, "the IK target " + name).BadReason() == null, "CF3: the IK target " + name + " takes a position");

            // Addresses.
            foreach (var bad in new[] { "", "a/b", "hair:9/x", "hair:0/x#0", "clothes:8/x", "clothes:1/", "acc:1234/x", "hair:0/x/y", "acc:" + Accessory + "/x#0" })
            {
                string text = new JValue(bad).ToString(Newtonsoft.Json.Formatting.None);
                var ts = One(Ends(Item, "{\"node\": \"" + CharaA + "\", \"bone\": " + text + "}", "[\"position\"]"), ConstraintFindings.BadAddress, FindingSeverity.Warning, 1, "the target address \"" + bad + "\"");
                t.Check(ts.BadReason() != null && ts.Target.Bone == bad, "CF3: the target address \"" + bad + "\": kept, state bad");
                var ss = One(Ends("{\"node\": \"" + CharaA + "\", \"bone\": " + text + "}", Item, "[\"position\"]"), ConstraintFindings.BadAddress, FindingSeverity.Warning, 1, "the source address \"" + bad + "\"");
                t.Check(ss.BadReason() != null && ss.Source.Bone == bad, "CF3: the source address \"" + bad + "\": kept, state bad");
            }
            foreach (var good in new[] { "cf_J_Hand_R", "name with space", "hair:0/x", "hair:3/x#2", "clothes:7/x", "acc:" + Accessory + "/x", "joint#2", "item_bone#0" })
            {
                string text = new JValue(good).ToString(Newtonsoft.Json.Formatting.None);
                t.Check(One(Ends(Item, "{\"node\": \"" + CharaA + "\", \"bone\": " + text + "}", "[\"position\"]"), null, null, 0, "the address \"" + good + "\"").BadReason() == null, "CF3: the address \"" + good + "\" is canonical");
            }

            // A node the file does not have.
            string gone = Id(0x99);
            var us = One(Ends("{\"node\": \"" + gone + "\"}", Item, "[\"position\"]"), ConstraintFindings.UnknownNode, FindingSeverity.Info, 1, "an unknown source node");
            t.Equal(gone, us.Source.Node, "CF3: an unknown source node: kept");
            var ut = One(Ends(Item, "{\"node\": \"" + gone + "\"}", "[\"position\"]"), ConstraintFindings.UnknownNode, FindingSeverity.Info, 1, "an unknown target node");
            t.Check(ut.Target.Node == gone && ut.BadReason() == null, "CF3: an unknown target node: kept, not bad");
            One(Ends("{\"node\": \"" + gone + "\"}", "{\"node\": \"" + gone + "\"}", "[\"position\"]"), ConstraintFindings.UnknownNode, FindingSeverity.Info, 2, "both ends unknown");
            var path = new FindingList();
            DocIo.Scene(Scene("[" + E("") + ", " + Ends(Item, "{\"node\": \"" + gone + "\"}", "[\"position\"]").Replace(Id(1), Id(2)) + "]"), path);
            t.Check(path.Count == 1 && path[0].Path.Contains("constraints[1].target.node"), "CF3: the finding names the entry and the end (" + path[0].Path + ")");

            // Several at once: nothing dropped, the order kept.
            var all = "[" + string.Join(", ", new[]
            {
                E(", \"weight\": 2"),
                Ends(Item, "{\"node\": \"" + CharaA + "\", \"ik\": \"Nose\"}", "[\"future\"]").Replace(Id(1), Id(2)),
                Ends("{\"node\": \"" + gone + "\"}", Item, "[\"position\"]").Replace(Id(1), Id(3)),
                E(""),
            }) + "]";
            var af = new FindingList();
            var ad = DocIo.Scene(Scene(all), af);
            t.Equal(4, ad.Constraints.Count, "CF3: mixed: nothing dropped");
            t.Equal(Id(1) + "," + Id(2) + "," + Id(3) + "," + JsonText.DerivedId("constraint/3"), string.Join(",", ad.Constraints.Entries.Select(e => e.Id)), "CF3: mixed: in order");
            var ab = DocIo.Bytes(ad);
            t.Check(DocIo.Same(ab, DocIo.Bytes(DocIo.Scene(ab))), "CF3: mixed: stable after the first write");

            // The type errors that fail a load, and a lenient reader.
            void Bad(string json, string what) => t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(Scene(json)), "CF3: " + what + " fails the document");
            Bad("null", "the member as null");
            Bad("5", "the member as a number");
            Bad("\"x\"", "the member as a string");
            Bad("{}", "the member as an object");
            Bad("[1]", "an element as a number");
            Bad("[null]", "an element as null");
            Bad("[{}]", "an element without its members");
            Bad("[" + E("").Replace("\"id\": \"" + Id(1) + "\", ", "") + "]", "no id");
            Bad("[" + E("").Replace(Id(1), "abc") + "]", "a bad id");
            Bad("[" + E("").Replace("\"id\": \"" + Id(1) + "\"", "\"id\": 5") + "]", "an id as a number");
            Bad("[{\"id\": \"" + Id(1) + "\", \"target\": {\"node\": \"" + ItemB + "\"}, \"components\": [\"position\"]}]", "no source");
            Bad("[{\"id\": \"" + Id(1) + "\", \"source\": {\"node\": \"" + ItemB + "\"}, \"components\": [\"position\"]}]", "no target");
            Bad("[" + Ends("{}", Item, "[\"position\"]") + "]", "a source without a node");
            Bad("[" + Ends(Item, "{\"bone\": \"x\"}", "[\"position\"]") + "]", "a target without a node");
            Bad("[" + Ends("{\"node\": \"x\"}", Item, "[\"position\"]") + "]", "a source node that is no id");
            Bad("[" + Ends("null", Item, "[\"position\"]") + "]", "a source as null");
            Bad("[" + Ends(Item, "5", "[\"position\"]") + "]", "a target as a number");
            Bad("[" + Ends("{\"node\": \"" + CharaA + "\", \"bone\": 5}", Item, "[\"position\"]") + "]", "a bone as a number");
            Bad("[" + Ends("{\"node\": \"" + CharaA + "\", \"bone\": null}", Item, "[\"position\"]") + "]", "a bone as null");
            Bad("[" + Ends(Item, "{\"node\": \"" + CharaA + "\", \"ik\": 5}", "[\"position\"]") + "]", "an ik as a number");
            Bad("[" + E(", \"label\": 5") + "]", "a label as a number");
            Bad("[" + E(", \"label\": null") + "]", "a label as null");
            Bad("[" + E(", \"on\": 1") + "]", "on as a number");
            Bad("[" + E(", \"weight\": \"x\"") + "]", "a weight as a string");
            Bad("[" + E(", \"weight\": null") + "]", "a weight as null");
            Bad("[" + Ends(Item, Item, "\"position\"") + "]", "components as a string");
            Bad("[" + Ends(Item, Item, "[5]") + "]", "a component as a number");
            Bad("[" + Ends(Item, Item, "[null]") + "]", "a component as null");
            Bad("[" + E(", \"offset\": 5") + "]", "an offset as a number");
            Bad("[" + E(", \"offset\": null") + "]", "an offset as null");
            Bad("[" + E(", \"offset\": {\"pos\": [1, 2]}") + "]", "an offset vector of the wrong length");
            Bad("[" + E(", \"offset\": {\"rot\": \"x\"}") + "]", "an offset vector of the wrong type");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => ConstraintList.Parse("[" + E(", \"weight\": NaN") + "]"), "CF3: a NaN weight is a non-finite error");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => ConstraintList.Parse("[" + E(", \"offset\": {\"pos\": [Infinity, 0, 0]}") + "]"), "CF3: an infinite offset too");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => ConstraintList.Parse("{}"), "CF3: Parse takes an array");
            var lf = new FindingList();
            var ld = DocIo.Scene(Scene("5"), lf, true);
            t.Check(lf.CountOf("json.typeError") == 1 && ld.Constraints == null, "CF3: a lenient reader reports the member and reads no list (" + lf + ")");
            var lf2 = new FindingList();
            var ld2 = DocIo.Scene(Scene("[" + E(", \"weight\": \"x\"") + "]"), lf2, true);
            t.Check(lf2.CountOf("json.typeError") == 1 && ld2.Constraints.Entries[0].Weight == 1f, "CF3: a lenient reader reports a member and reads the default (" + lf2 + ")");
        }

        // ---------------- CF4 ----------------

        static void CF4(TestRunner t)
        {
            var big = new ConstraintList();
            for (int i = 0; i < 300; i++)
                big.Entries.Add(new ConstraintEntry { Id = Id(0x2000 + i), Source = new ConstraintEnd(ItemA), Target = new ConstraintEnd(ItemB), Components = { ConstraintComponents.Position }, Label = "n" + i });
            var bytes = DocIo.Bytes(Doc(big));
            var f = new FindingList();
            var d = DocIo.Scene(bytes, f);
            t.Equal(300, d.Constraints.Count, "CF4: every entry over the limit is kept");
            t.Check(f.Count == 1 && f[0].Code == ConstraintFindings.Limit && f[0].Severity == FindingSeverity.Warning && f[0].Message.Contains("44"), "CF4: one warning, with the count (" + f + ")");
            t.Check(DocIo.Same(bytes, DocIo.Bytes(d)), "CF4: written back byte-equal");
            t.Check(!ConstraintList.BeyondLimit(255) && ConstraintList.BeyondLimit(256) && ConstraintList.BeyondLimit(299), "CF4: an entry from index 256 on lies beyond the limit");
            var at = new ConstraintList(big.Entries.Take(256));
            var nf = new FindingList();
            t.Check(DocIo.Scene(DocIo.Bytes(Doc(at)), nf).Constraints.Count == 256 && nf.Count == 0, "CF4: 256 entries give no finding");

            // Edits: the limit binds what an edit adds, not what a file holds.
            t.Check(at.Problem() == null, "CF4: 256 is allowed");
            var over = new ConstraintList(big.Entries.Take(257));
            t.Check(over.Problem(at) != null && over.Problem(at).Contains("256"), "CF4: a 257th entry is refused (" + over.Problem(at) + ")");
            t.Check(over.Problem() != null, "CF4: and from nothing");
            t.Check(new ConstraintList(big.Entries.Take(299)).Problem(big) == null, "CF4: a file's list over the limit may shrink");
            t.Check(new ConstraintList(big.Entries.AsEnumerable().Reverse()).Problem(big) == null, "CF4: and be reordered");
            var more = new ConstraintList(big.Entries.Concat(new[] { new ConstraintEntry { Id = Id(0x3000), Source = new ConstraintEnd(ItemA), Target = new ConstraintEnd(ItemB), Components = { ConstraintComponents.Position } } }));
            t.Check(more.Problem(big) != null, "CF4: but not grow");
            var twice = new ConstraintList(new[] { big.Entries[0], big.Entries[0].Clone() });
            t.Check(twice.Problem() != null && twice.Problem().Contains("twice"), "CF4: an edit may not repeat an id");
            t.Check(new ConstraintList(new ConstraintEntry[] { null }).Problem() != null, "CF4: nor hold a null entry");

            // A problem the entry came with stays; a new one is refused.
            var file = ConstraintList.Parse("[" + E(", \"weight\": 5") + "]");
            var same = file.Entries[0].Clone();
            same.Label = "edited";
            t.Check(new ConstraintList(new[] { same }).Problem(file) == null, "CF4: a file's weight outside 0..1 does not block another member's edit");
            var worse = file.Entries[0].Clone();
            worse.Weight = 6f;
            t.Check(new ConstraintList(new[] { worse }).Problem(file) != null, "CF4: another such weight is refused");
        }

        // ---------------- the fixture ----------------

        public const string FixtureFile = "scene_r4_constraints.hs2nscene";
        public const string FixtureTitle = "Constraints fixture scene";
        public static readonly DateTime FixtureClock = new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The fixture's path under a save-format fixtures root.</summary>
        public static string FixturePath(string fixturesRoot) => System.IO.Path.Combine(fixturesRoot ?? "", "fixtures", "constraints", FixtureFile);

        /// <summary>The options the fixture was written with: fixed clock, placeholder thumbnail, no ext handlers, app 1.2.0.</summary>
        public static SaveOptions FixtureOptions() => new SaveOptions
        {
            Clock = () => FixtureClock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.2.0",
        };

        /// <summary>Two items, a light, two synthetic characters (no card data), a small Timeline and a list of every form, with unknown
        /// members at each level. Nothing in it gives a finding: an array and an object as top-level members, side by side.</summary>
        public static SceneContainer FixtureScene()
        {
            var s = new SceneContainer { Title = FixtureTitle };
            s.Scene.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "test.mod#1" }) { Order = 0 });
            s.Scene.Nodes.Add(new SceneNode(ItemB, new ItemNodeState { Ref = "test.mod#2" }) { Order = 1 });
            s.Scene.Nodes.Add(new SceneNode(Light, new LightNodeState { Ref = "illusion.hs2#0" }) { Order = 2 });
            s.Scene.Nodes.Add(new SceneNode(CharaA, new CharaNodeState()) { Order = 3 });
            s.Scene.Nodes.Add(new SceneNode(CharaB, new CharaNodeState()) { Order = 4 });
            s.PlaceCharacter(CharaA, InfraFixtures.SmallChara(0));
            s.PlaceCharacter(CharaB, InfraFixtures.SmallChara(1));
            var tl = new TimelineDocument { Duration = 6f };
            var track = new TimelineTrack(Id(0x200), TimelineTypes.Pos, ItemA);
            track.Keys.Add(TimelineKey.OfVector(0f, new Vec3(0f, 0f, 0f)));
            track.Keys.Add(TimelineKey.OfVector(3f, new Vec3(1f, 2f, 3f)));
            tl.Tracks.Add(track);
            s.Scene.Timeline = tl;
            var entries = AllForms();
            entries[1].Unknown = new JsonLeftovers();
            entries[1].Unknown.Add("futureEntry", JToken.Parse("{\"a\": [1, 2.5, null], \"b\": \"text\"}"));
            entries[2].Target.Unknown = new JsonLeftovers();
            entries[2].Target.Unknown.Add("futureTarget", JToken.Parse("[\"x\", \"y\"]"));
            entries[3].Offset.Unknown = new JsonLeftovers();
            entries[3].Offset.Unknown.Add("futureOffset", JToken.Parse("[0.1, 0.2]"));
            s.Scene.Constraints = new ConstraintList(entries);
            return s;
        }

        /// <summary>Writes the fixture once (refuses an existing file); returns its size.</summary>
        public static long WriteFixture(string fixturesRoot)
        {
            string path = FixturePath(fixturesRoot);
            if (System.IO.File.Exists(path)) throw new InvalidOperationException("the fixture is never regenerated: " + path + " exists");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var r = FixtureScene().Save(path, FixtureOptions());
            if (!r.Ok) throw new InvalidOperationException(r.Error.Message);
            return new System.IO.FileInfo(path).Length;
        }

        // ---------------- CF5 ----------------

        static void CF5(TestRunner t)
        {
            string path = FixturePath(t.FixturesDir);
            if (!System.IO.File.Exists(path)) { t.Check(false, "CF5: fixture " + path + " present"); return; }
            var fixture = System.IO.File.ReadAllBytes(path);
            var now = FixtureScene().ToBytes(FixtureOptions());
            foreach (var e in ContainerReader.Read(fixture).Entries)
                t.Check(Same(e.Data, EntryBytes(now, e.Name)), "CF5: the fixture's " + e.Name + " is what this writer writes for it");
            t.Equal(4, FormatOf(fixture), "CF5: the fixture is revision 4");
            t.Equal("nodes,scene,timeline,constraints", Names(Entry(fixture, "scene.json")), "CF5: the fixture holds an object and an array, in order");

            // This reader: the list, no finding, written back byte for byte.
            var l4 = SceneContainer.Load(fixture, LOpts());
            t.Check(l4.Ok && !l4.NewerFormat && l4.Findings.Count == 0 && l4.Value.Scene.Constraints?.Count == 8 && l4.Value.Scene.Timeline != null, "CF5: this reader reads the fixture with no finding (" + l4 + ")");
            if (!l4.Ok) return;
            string list = l4.Value.Scene.Constraints.ToJson(), timeline = l4.Value.Scene.Timeline.ToJson();
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(l4.Value.ToBytes(FixtureOptions()), "scene.json")), "CF5: read and written, scene.json byte-equal");

            // A revision-3 registry: NewerFormat and the one finding; both members held as 1.2.0 holds them.
            var l3 = SceneContainer.Load(fixture, LOpts(Registry3()));
            t.Check(l3.Ok && l3.NewerFormat && l3.Findings.Count == 1 && l3.Findings[0].Code == "manifest.newerFormat" && l3.Findings[0].Message.Contains("format 4 is newer than 3"),
                "CF5: an S = 3 registry reads it with NewerFormat and the single finding manifest.newerFormat (" + l3 + ")");
            if (!l3.Ok) return;
            var c = l3.Value;
            c.Scene.Timeline = null;
            c.Scene.Constraints = null;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            c.Scene.Unknown = new JsonLeftovers();
            c.Scene.Unknown.Add(TimelineDocument.MemberName, raw[TimelineDocument.MemberName].DeepClone());
            c.Scene.Unknown.Add(Member, raw[Member].DeepClone());
            string saved = System.IO.Path.Combine(t.TempDir, "cf5_downgraded.hs2nscene");
            var sr = c.Save(saved, FixtureOptions());
            t.Check(sr.Ok && sr.Downgraded, "CF5: saving it reports Downgraded");
            var back = System.IO.File.ReadAllBytes(saved);
            t.Check(FormatOf(back) >= 1 && FormatOf(back) <= 3 && (int)Entry(back, "manifest.json")["minReaderVersion"] == 1, "CF5: written as its own lowest covering revision (" + FormatOf(back) + ")");
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "CF5: scene.json byte-equal (both members written back in place)\n    " +
                DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));
            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && lb.Findings.Count == 0 && lb.Value.Scene.Constraints?.ToJson() == list && lb.Value.Scene.Timeline?.ToJson() == timeline && (lb.Value.Scene.Unknown == null || lb.Value.Scene.Unknown.IsEmpty),
                "CF5: this reader then reads the same list and Timeline, silently, as known members (" + lb + ")");
            t.Check(lb.Ok && FormatOf(lb.Value.ToBytes(FixtureOptions())) == 4, "CF5: and writes revision 4 again");

            // With a scene-scope ext block: unknown members go before ext.
            var se = FixtureScene();
            se.Scene.Ext.Set("stp3.fixture", JToken.Parse("{\"a\": 1}"));
            var withExt = se.ToBytes(FixtureOptions());
            var le = SceneContainer.Load(withExt, LOpts(Registry3()));
            if (le.Ok)
            {
                var er = DocReader.Parse(EntryBytes(withExt, "scene.json"), "scene.json");
                le.Value.Scene.Timeline = null;
                le.Value.Scene.Constraints = null;
                le.Value.Scene.Unknown = new JsonLeftovers();
                le.Value.Scene.Unknown.Add(TimelineDocument.MemberName, er[TimelineDocument.MemberName].DeepClone());
                le.Value.Scene.Unknown.Add(Member, er[Member].DeepClone());
                var wb = le.Value.ToBytes(FixtureOptions());
                t.Check(Same(EntryBytes(withExt, "scene.json"), EntryBytes(wb, "scene.json")) && Names(Entry(wb, "scene.json")) == "nodes,scene,timeline,constraints,ext",
                    "CF5: with a scene ext block the old writer's order is nodes, scene, timeline, constraints, ext (byte-equal)");
            }
            else t.Check(false, "CF5: the ext variant loads (" + le + ")");

            // Every kind of entry that a reader kept for itself comes back unchanged through the same path.
            var odd = "[" + string.Join(", ", new[]
            {
                E(", \"weight\": 2.5, \"label\": \"\\u00e9\""),
                Ends("{\"node\": \"" + ItemA + "\"}", "{\"node\": \"" + ItemB + "\"}", "[]").Replace(Id(1), Id(2)),
                Ends("{\"node\": \"" + ItemA + "\", \"bone\": \"hair:0/x#0\"}", "{\"node\": \"" + CharaA + "\", \"ik\": \"Nose\"}", "[\"future\", \"parent\"]").Replace(Id(1), Id(3)),
            }) + "]";
            var oddDoc = DocIo.Scene(Scene(odd));
            var ob = DocIo.Bytes(oddDoc);
            var asLeft = DocIo.Scene(ob);
            var oj = DocReader.Parse(ob, SceneDocument.EntryName);
            asLeft.Constraints = null;
            asLeft.Unknown = new JsonLeftovers();
            asLeft.Unknown.Add(Member, oj[Member].DeepClone());
            t.Check(DocIo.Same(ob, DocIo.Bytes(asLeft)), "CF5: an array holding an empty array, bad entries and text is the same bytes as a leftover\n    " + DocIo.FirstDiff(ob, DocIo.Bytes(asLeft)));
        }

        // ---------------- CF7 ----------------

        static void CF7(TestRunner t)
        {
            foreach (int revision in new[] { 1, 2, 3 })
            {
                var s = InfraFixtures.SmallScene(1, 1);
                var b0 = s.ToBytes(Opts());
                var plain = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = revision), "scene.json");
                var lp = SceneContainer.Load(plain, LOpts());
                t.Check(lp.Ok && !lp.NewerFormat && lp.Findings.Count == 0 && lp.Value.Scene.Constraints == null, "CF7: a revision-" + revision + " file loads with no finding and no list (" + lp + ")");

                // One an older app wrote back: the member is known here.
                var older = InfraFixtures.Repack(b0, m =>
                {
                    InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = revision);
                    InfraFixtures.EditJson(m, "scene.json", j => j.Add(Member, JArray.Parse("[" + Ends("{\"node\": \"" + ((string)j["nodes"][0]["id"]) + "\"}", "{\"node\": \"" + ((string)j["nodes"].Last["id"]) + "\"}", "[\"position\"]") + "]")));
                }, "scene.json");
                var l = SceneContainer.Load(older, LOpts());
                t.Check(l.Ok && !l.NewerFormat && l.Findings.Count == 0, "CF7: a revision-" + revision + " file holding the member reads with no finding (" + l + ")");
                if (!l.Ok) continue;
                t.Check(l.Value.Scene.Constraints?.Count == 1 && (l.Value.Scene.Unknown == null || l.Value.Scene.Unknown.IsEmpty), "CF7: it is a known member");
                t.Equal(4, FormatOf(l.Value.ToBytes(Opts())), "CF7: and its next save is revision 4");
            }
            t.Check(CoreMigrationRegistry.Default.Supported == SaveFormats.SupportedVersion, "CF7: no migration was added");
        }

        // ---------------- CF8 ----------------

        static void CF8(TestRunner t)
        {
            foreach (var (name, make, format) in new (string, Func<SceneContainer>, int)[]
            {
                ("a plain scene", () => InfraFixtures.SmallScene(1, 1), 1),
                ("a revision-3 scene", () => { var s = InfraFixtures.SmallScene(1, 1); ((CharaNodeState)s.Scene.Nodes[0].State).State.NippleThroughClothes = false; return s; }, 3),
                ("a scene with a Timeline", () => { var s = InfraFixtures.SmallScene(1, 1); s.Scene.Timeline = new TimelineDocument(); return s; }, 4),
            })
            {
                var s = make();
                var before = s.ToBytes(Opts());
                s.Scene.Constraints = new ConstraintList(new[] { Entry(1, new ConstraintEnd(s.Scene.Nodes[0].Id), new ConstraintEnd(s.Scene.Nodes[1].Id), ConstraintComponents.Position) });
                var with = s.ToBytes(Opts());
                t.Check(FormatOf(with) == 4 && !Same(before, with), "CF8: " + name + " with an entry is revision 4");

                // Through a file, as a session does: load, the entry removed, save.
                var l = SceneContainer.Load(with, LOpts());
                t.Check(l.Ok && l.Findings.Count == 0, "CF8: " + name + " loads (" + l + ")");
                if (!l.Ok) continue;
                l.Value.Scene.Constraints = null;
                var after = l.Value.ToBytes(Opts());
                t.Equal(format, FormatOf(after), "CF8: " + name + ": the lowest covering revision again");
                t.Check(!Text(EntryBytes(after, "scene.json")).Contains("\"" + Member + "\""), "CF8: " + name + ": the member is gone");
                t.Check(Same(EntryBytes(before, "scene.json"), EntryBytes(after, "scene.json")), "CF8: " + name + ": scene.json as before the first add");
                t.Check(Same(EntryBytes(before, "manifest.json"), EntryBytes(after, "manifest.json")), "CF8: " + name + ": the manifest too");
                t.Equal(ContainerReader.Read(before).Entries.Count(), ContainerReader.Read(after).Entries.Count(), "CF8: " + name + ": the same entries");
            }
        }

        static int FormatOf(byte[] file) => (int)Entry(file, "manifest.json")["formatVersion"];
        static JObject Entry(byte[] file, string name) => JObject.Parse(Text(ContainerReader.Read(file).Get(name).Data));
        static byte[] EntryBytes(byte[] file, string name) => ContainerReader.Read(file).Get(name)?.Data;
        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
        static SaveOptions Opts() => InfraFixtures.Opts();
        static CoreMigrationRegistry Registry3() => new CoreMigrationRegistry(3, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2));

        static LoadOptions LOpts(CoreMigrationRegistry reg = null)
        {
            var o = InfraFixtures.LOpts();
            if (reg != null) o.Migrations = reg;
            return o;
        }
    }
}
