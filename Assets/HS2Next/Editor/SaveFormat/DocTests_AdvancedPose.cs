// The advanced-pose block of a character node (scene.json member `chara.advancedPose`, format revision 4). AS1 the schema round
// trip: member order, absent members, map order, write -> read -> write byte-stable, unknown members kept at every level; AS2 a
// character without a block writes the frozen bytes (goldens, the v0.2 fixture, the Timeline fixture, a block cleared); AS3 revision 4
// exactly when a Timeline, a non-empty block, a non-empty material array or a constraint exists, the lower revisions otherwise, and clearing brings the next save back; AS4 the
// committed fixture (Tests/SaveFormat/fixtures/advancedpose/scene_r4_pose.hs2nscene: what this writer writes for FixtureScene()), read
// by this reader silently and by a revision-3 registry with NewerFormat and the single finding, written back by that reader with the
// member kept as an unknown one (scene.json byte-equal, Downgraded) and read back here with the same values; AS5 the reader's rules: keys
// that are not addresses, values out of range, identity entries, the limits, type errors. Synthetic ids and names only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_AdvancedPose
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string CharaA = Id(0xc1), CharaB = Id(0xc2);
        /// <summary>The accessory of <see cref="InfraFixtures.SmallChara"/>'s first slot.</summary>
        public static readonly string Accessory = InfraFixtures.Id(100);

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
        static string Names(JToken o) => string.Join(",", ((JObject)o).Properties().Select(p => p.Name));

        /// <summary>A block with every member and every address form.</summary>
        public static CharaAdvancedPose Block()
        {
            var b = new CharaAdvancedPose();
            b.Bones["cf_J_Hand_Index02_L"] = new BoneCorrection(new Vec3(0.01f, 0f, -0.02f), Vec3.Zero, Vec3.One);
            b.Bones["cf_J_MouthMove"] = new BoneCorrection(Vec3.Zero, new Vec3(0f, 12.5f, 370f), Vec3.One);
            b.Bones["cf_J_NoseBase_s"] = new BoneCorrection(Vec3.Zero, Vec3.Zero, new Vec3(1.25f, 0.8f, 1f));
            b.Bones["hair:1/bone_a"] = new BoneCorrection(new Vec3(0f, 0.5f, 0f), new Vec3(5f, 0f, 0f), new Vec3(2f, 2f, 2f));
            b.Bones["clothes:0/bone_b#2"] = new BoneCorrection(Vec3.Zero, new Vec3(0f, 0f, -40f), Vec3.One);
            b.Bones["acc:" + Accessory + "/N_move#2"] = new BoneCorrection(new Vec3(0f, 0f, 1f), Vec3.Zero, Vec3.One);
            b.Chains["hair:0/root_a"] = new ChainValues { Enabled = false };
            b.Chains["clothes:2/root_b#1"] = new ChainValues { Weight = 0.5f, Damping = 0.1f, Elasticity = 0.2f, Stiffness = 0.3f, Inert = 0.4f, Radius = 0.05f };
            b.Chains["acc:" + Accessory + "/root_c"] = new ChainValues { Gravity = new Vec3(0f, -0.01f, 0f), Force = new Vec3(0.002f, 0f, 0f), FreezeAxis = 2 };
            b.Chains["other/root_d"] = new ChainValues
            {
                Enabled = true, Weight = 1f, Damping = 0f, Elasticity = 1f, Stiffness = 0.25f, Inert = 0.75f, Radius = 2f,
                Gravity = new Vec3(1f, 2f, 3f), Force = new Vec3(4f, 5f, 6f), FreezeAxis = 0,
            };
            b.Bust.BreastL = new BustValues { Enabled = true, Gravity = new Vec3(0f, -0.02f, 0f), Force = new Vec3(0f, 0f, 0.01f), Damping = 0.2f, Elasticity = 0.3f, Stiffness = 0.4f, Inert = 0.5f };
            b.Bust.HipR = new BustValues { Enabled = false };
            b.BlendShapes["head/o_head"] = new BlendValues();
            b.BlendShapes["head/o_head"].Weights["shape_b"] = 12.5f;
            b.BlendShapes["head/o_head"].Weights["shape_a"] = 100f;
            b.BlendShapes["body/o_body"] = new BlendValues();
            b.BlendShapes["body/o_body"].Weights["shape_c"] = -50f;
            b.BlendShapes["acc:" + Accessory + "/mesh_a#1"] = new BlendValues();
            b.BlendShapes["acc:" + Accessory + "/mesh_a#1"].Weights["shape_d"] = 0f;
            return b;
        }

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            AS1(t);
            AS2(t);
            AS3(t);
            AS4(t);
            AS5(t);
        }

        // ---------------- AS1 ----------------

        static void AS1(TestRunner t)
        {
            var d = Block();
            var b1 = d.ToBytes();
            var f = new FindingList();
            var back = CharaAdvancedPose.Parse(Text(b1), f);
            var b2 = back.ToBytes();
            t.Check(b1.SequenceEqual(b2), "AS1: write -> read -> write byte-stable\n    " + DocIo.FirstDiff(b1, b2));
            t.Check(f.Count == 0, "AS1: the written block reads back with no finding (" + f + ")");
            t.Check(back.Clone().ToBytes().SequenceEqual(b1) && d.Clone().ToBytes().SequenceEqual(b1), "AS1: Clone gives the same bytes");

            var j = back.ToJObject();
            t.Equal("bones,chains,bust,blendShapes", Names(j), "AS1: members in schema order");
            t.Equal("pos,rot,scale", Names(j["bones"]["cf_J_MouthMove"]), "AS1: all three written on each bone entry, in order");
            t.Equal("enabled", Names(j["chains"]["hair:0/root_a"]), "AS1: a chain writes only the members set");
            t.Equal("enabled,weight,damping,elasticity,stiffness,inert,radius,gravity,force,freezeAxis", Names(j["chains"]["other/root_d"]), "AS1: chain members in schema order");
            t.Equal("breastL,hipR", Names(j["bust"]), "AS1: bust writes only the chains with a member, in order");
            t.Equal("enabled,gravity,force,damping,elasticity,stiffness,inert", Names(j["bust"]["breastL"]), "AS1: bust members in schema order");
            t.Equal("weights", Names(j["blendShapes"]["head/o_head"]), "AS1: a renderer holds weights");
            t.Equal("shape_a,shape_b", Names(j["blendShapes"]["head/o_head"]["weights"]), "AS1: weights in ordinal order");
            t.Equal("acc:" + Accessory + "/N_move#2,cf_J_Hand_Index02_L,cf_J_MouthMove,cf_J_NoseBase_s,clothes:0/bone_b#2,hair:1/bone_a", Names(j["bones"]), "AS1: bone keys in ordinal order");

            // The exact canonical text of a small block.
            var small = new CharaAdvancedPose();
            small.Bones["cf_J_Hips"] = new BoneCorrection(new Vec3(0.1f, 0f, -1f), Vec3.Zero, Vec3.One);
            small.Chains["other/root"] = new ChainValues { Weight = 0.5f, FreezeAxis = 1 };
            small.Bust.HipL = new BustValues { Damping = 0.25f };
            small.BlendShapes["head/o_tooth"] = new BlendValues();
            small.BlendShapes["head/o_tooth"].Weights["s"] = 30f;
            const string smallText =
                "{\n  \"bones\": {\n    \"cf_J_Hips\": {\n      \"pos\": [0.1, 0.0, -1.0],\n      \"rot\": [0.0, 0.0, 0.0],\n      \"scale\": [1.0, 1.0, 1.0]\n    }\n  },\n" +
                "  \"chains\": {\n    \"other/root\": {\n      \"weight\": 0.5,\n      \"freezeAxis\": 1\n    }\n  },\n" +
                "  \"bust\": {\n    \"hipL\": {\n      \"damping\": 0.25\n    }\n  },\n" +
                "  \"blendShapes\": {\n    \"head/o_tooth\": {\n      \"weights\": {\n        \"s\": 30.0\n      }\n    }\n  }\n}";
            t.Equal(smallText, small.ToJson(), "AS1: the exact canonical text of a small block");

            // Absent members.
            var empty = CharaAdvancedPose.Parse("{}");
            t.Check(empty.IsEmpty && empty.Bones.Count == 0 && empty.Chains.Count == 0 && empty.BlendShapes.Count == 0 && empty.Bust.IsEmpty, "AS1: {} is an empty block");
            t.Equal("{\n  \"bones\": {},\n  \"chains\": {},\n  \"blendShapes\": {}\n}", empty.ToJson(), "AS1: a block written alone holds the three maps; bust only with a member");
            var e1 = CharaAdvancedPose.Parse("{\"bones\": {\"a\": {}}, \"chains\": {\"other/b\": {}}, \"blendShapes\": {\"head/c\": {}}}");
            t.Check(e1.Bones["a"].Pos.Equals(Vec3.Zero) && e1.Bones["a"].Rot.Equals(Vec3.Zero) && e1.Bones["a"].Scale.Equals(Vec3.One) && e1.Bones["a"].IsIdentity,
                "AS1: an entry's absent members are identity");
            t.Check(e1.Chains["other/b"].IsEmpty && !e1.Chains["other/b"].Weight.HasValue && e1.BlendShapes["head/c"].Weights.Count == 0 && !e1.IsEmpty, "AS1: an empty chain or renderer entry is kept");
            t.Equal("pos,rot,scale", Names(e1.ToJObject()["bones"]["a"]), "AS1: and a bone entry is written whole");
            var s1 = CharaAdvancedPose.Parse("{\"bust\": {\"breastL\": {}, \"hipR\": {}}}");
            t.Check(s1.IsEmpty && s1.ToJObject().Property("bust") == null, "AS1: bust chains without a member are not written");

            // Map order: read and written ordinal, whatever the file's order.
            var mixed = CharaAdvancedPose.Parse("{\"bones\": {\"b\": {}, \"a\": {}, \"B\": {}}, \"blendShapes\": {\"head/x\": {\"weights\": {\"z\": 1, \"Z\": 2, \"a\": 3}}}}");
            t.Equal("B,a,b", Names(mixed.ToJObject()["bones"]), "AS1: keys ordinal (uppercase first)");
            t.Equal("Z,a,z", Names(mixed.ToJObject()["blendShapes"]["head/x"]["weights"]), "AS1: shapes ordinal");

            // Unknown members at every level, written after the known ones, in read order.
            const string unknown = "{\"zz\": 1, \"bones\": {\"a\": {\"pos\": [1, 0, 0], \"yy\": \"keep\"}}, \"chains\": {\"other/r\": {\"weight\": 0.5, \"xx\": [1, 2]}}, " +
                                   "\"bust\": {\"breastR\": {\"vv\": {\"n\": 1}}, \"uu\": true}, \"blendShapes\": {\"head/h\": {\"tt\": null, \"weights\": {\"s\": 1}}}, \"aa\": 2}";
            var uf = new FindingList();
            var u = CharaAdvancedPose.Parse(unknown, uf);
            var uj = u.ToJObject();
            t.Check(uf.Count == 0, "AS1: unknown members raise no finding (" + uf + ")");
            t.Equal("bones,chains,bust,blendShapes,zz,aa", Names(uj), "AS1: the block's unknown members after the known ones, in read order");
            t.Equal("pos,rot,scale,yy", Names(uj["bones"]["a"]), "AS1: a bone entry's unknown member kept");
            t.Equal("weight,xx", Names(uj["chains"]["other/r"]), "AS1: a chain's unknown member kept");
            t.Equal("breastR,uu", Names(uj["bust"]), "AS1: bust's unknown member kept");
            t.Equal("vv", Names(uj["bust"]["breastR"]), "AS1: a bust chain with only an unknown member is kept");
            t.Equal("weights,tt", Names(uj["blendShapes"]["head/h"]), "AS1: a shape object's unknown member kept");
            t.Check(u.ToBytes().SequenceEqual(CharaAdvancedPose.Parse(u.ToJson()).ToBytes()), "AS1: and stable");
            var only = CharaAdvancedPose.Parse("{\"future\": {\"a\": 1}}");
            t.Check(!only.IsEmpty, "AS1: a block with only an unknown member is not empty (never lost)");
            var ob = CharaAdvancedPose.Parse("{\"bust\": {\"future\": 1}}");
            t.Check(!ob.IsEmpty && !ob.Bust.IsEmpty && ob.ToJObject()["bust"]["future"] != null, "AS1: nor one with an unknown member in bust");

            // In the section: after voice, before the section's own unknown members.
            var node = new SceneNode(CharaA, new CharaNodeState { AdvancedPose = Block() });
            var doc = new SceneDocument();
            doc.Nodes.Add(node);
            var sj = DocReader.Parse(DocIo.Bytes(doc), SceneDocument.EntryName);
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose", Names(sj["nodes"][0]["chara"]), "AS1: advancedPose is the last member of the section");
            var withUnknown = (JObject)sj.DeepClone();
            ((JObject)withUnknown["nodes"][0]["chara"]).Add("future", 1);
            var doc2 = DocIo.Scene(CanonicalJsonWriter.ToBytes(withUnknown, SceneDocument.EntryName));
            var cs = (CharaNodeState)doc2.Find(CharaA).State;
            t.Equal(Block().ToJson(), cs.AdvancedPose.ToJson(), "AS1: the section reads the block back");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose,future", Names(DocReader.Parse(DocIo.Bytes(doc2), SceneDocument.EntryName)["nodes"][0]["chara"]),
                "AS1: the section's unknown members follow it");

            // Random blocks.
            int stable = 0;
            const int seeds = 300;
            for (int seed = 0; seed < seeds; seed++)
            {
                var r = RandomBlock(new Random(seed));
                var bb = r.ToBytes();
                var rf = new FindingList();
                var rb = CharaAdvancedPose.Parse(Text(bb), rf);
                if (bb.SequenceEqual(rb.ToBytes()) && rf.Count == 0 && rb.Clone().ToBytes().SequenceEqual(bb)) stable++;
                else t.Info("seed " + seed + ": " + rf);
            }
            t.Equal(seeds, stable, "AS1: random blocks are byte-stable with no finding");
        }

        // Valid values only, so no finding.
        static CharaAdvancedPose RandomBlock(Random g)
        {
            float F(float min, float max) => (float)(min + g.NextDouble() * (max - min));
            Vec3 V(float min, float max) => new Vec3(F(min, max), F(min, max), F(min, max));
            string Name() => "n" + g.Next(50) + (g.Next(4) == 0 ? "é'\"" : "");
            string Part()
            {
                switch (g.Next(3))
                {
                    case 0: return "hair:" + g.Next(4);
                    case 1: return "clothes:" + g.Next(8);
                    default: return "acc:" + Id(g.Next(1, 99));
                }
            }
            string Mark() => g.Next(3) == 0 ? "#" + g.Next(1, 9) : "";
            var b = new CharaAdvancedPose();
            for (int i = g.Next(8); i > 0; i--)
            {
                string key = g.Next(2) == 0 ? Name() : Part() + "/" + Name() + Mark();
                b.Bones[key] = new BoneCorrection(V(-1000f, 1000f), V(-720f, 720f), V(0.001f, 1000f));
            }
            for (int i = g.Next(6); i > 0; i--)
            {
                var c = new ChainValues();
                if (g.Next(2) == 0) c.Enabled = g.Next(2) == 0;
                if (g.Next(2) == 0) c.Weight = F(0f, 1f);
                if (g.Next(2) == 0) c.Damping = F(0f, 1f);
                if (g.Next(2) == 0) c.Elasticity = F(0f, 1f);
                if (g.Next(2) == 0) c.Stiffness = F(0f, 1f);
                if (g.Next(2) == 0) c.Inert = F(0f, 1f);
                if (g.Next(2) == 0) c.Radius = F(0f, 9f);
                if (g.Next(2) == 0) c.Gravity = V(-1f, 1f);
                if (g.Next(2) == 0) c.Force = V(-1f, 1f);
                if (g.Next(2) == 0) c.FreezeAxis = g.Next(4);
                b.Chains[(g.Next(4) == 0 ? "other" : Part()) + "/" + Name() + Mark()] = c;
            }
            for (int k = 0; k < 4; k++)
            {
                if (g.Next(2) == 0) continue;
                var v = b.Bust[k];
                if (g.Next(2) == 0) v.Enabled = g.Next(2) == 0;
                if (g.Next(2) == 0) v.Gravity = V(-1f, 1f);
                if (g.Next(2) == 0) v.Force = V(-1f, 1f);
                if (g.Next(2) == 0) v.Damping = F(0f, 1f);
                if (g.Next(2) == 0) v.Elasticity = F(0f, 1f);
                if (g.Next(2) == 0) v.Stiffness = F(0f, 1f);
                if (g.Next(2) == 0) v.Inert = F(0f, 1f);
            }
            for (int i = g.Next(5); i > 0; i--)
            {
                var v = new BlendValues();
                for (int n = g.Next(6); n > 0; n--) v.Weights[Name()] = F(-200f, 300f);
                string part = g.Next(3) == 0 ? (g.Next(2) == 0 ? "head" : "body") : Part();
                b.BlendShapes[part + "/" + Name() + Mark()] = v;
            }
            return b;
        }

        // ---------------- AS2 ----------------

        static void AS2(TestRunner t)
        {
            var golden = DocTests_T1Golden.Golden("scene.json");
            var doc = DocIo.Scene(golden);
            var chara = doc.Nodes.Select(n => n.State).OfType<CharaNodeState>().ToList();
            t.Check(chara.Count > 0 && chara.All(c => c.AdvancedPose != null && c.AdvancedPose.IsEmpty), "AS2: the golden scene's characters read with an empty block, never null");
            t.Check(DocIo.Same(golden, DocIo.Bytes(doc)) && !Text(golden).Contains("advancedPose"), "AS2: the golden scene is written byte for byte, without the member");

            string v02 = System.IO.Path.Combine(FixtureWriter.V02Dir(t.FixturesDir ?? ""), "scene.hs2nscene");
            if (System.IO.File.Exists(v02))
            {
                var file = System.IO.File.ReadAllBytes(v02);
                var l = SceneContainer.Load(file, LOpts());
                var old = SceneContainer.Load(file, LOpts(new CoreMigrationRegistry(3, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2))));
                t.Check(l.Ok && old.Ok && !l.Findings.Has(AdvancedPoseFindings.BadAddress) && l.Findings.ToString() == old.Findings.ToString(), "AS2: the v0.2 fixture scene loads with the findings of a revision-3 reader (" + l + ")");
                if (l.Ok)
                {
                    var re = l.Value.ToBytes(Opts());
                    t.Check(FormatOf(re) == 1 && Same(EntryBytes(file, "scene.json"), EntryBytes(re, "scene.json")), "AS2: re-saved as format 1, scene.json byte-equal");
                }
            }
            else t.Check(false, "AS2: fixture " + v02 + " present");

            string r4 = DocTests_Timeline.R4Path(t.FixturesDir);
            if (System.IO.File.Exists(r4))
            {
                var file = System.IO.File.ReadAllBytes(r4);
                var l = SceneContainer.Load(file, LOpts());
                t.Check(l.Ok && l.Findings.Count == 0 && l.Value.Scene.Nodes.Select(n => n.State).OfType<CharaNodeState>().All(c => c.AdvancedPose.IsEmpty), "AS2: the Timeline fixture's character has an empty block");
                if (l.Ok)
                {
                    var re = l.Value.ToBytes(DocTests_Timeline.R4Options());
                    t.Check(FormatOf(re) == 4 && Same(EntryBytes(file, "scene.json"), EntryBytes(re, "scene.json")), "AS2: the Timeline fixture stays byte-equal");
                }
            }
            else t.Check(false, "AS2: fixture " + r4 + " present");

            // A scene with a character never given a block, one given an explicit empty block, one whose block was cleared.
            var s = InfraFixtures.SmallScene(2, 1);
            var b0 = s.ToBytes(Opts());
            t.Check(FormatOf(b0) == 1 && !Text(EntryBytes(b0, "scene.json")).Contains("advancedPose"), "AS2: a scene without a block has no member, format 1");
            var c0 = (CharaNodeState)s.Scene.Nodes[0].State;
            c0.AdvancedPose = new CharaAdvancedPose();
            t.Check(Same(b0, s.ToBytes(Opts())), "AS2: an explicit empty block changes no byte");
            c0.AdvancedPose = Block();
            t.Check(!Same(b0, s.ToBytes(Opts())), "AS2: a block changes the file");
            c0.AdvancedPose.Bones.Clear();
            c0.AdvancedPose.Chains.Clear();
            c0.AdvancedPose.BlendShapes.Clear();
            c0.AdvancedPose.Bust = new BustSet();
            t.Check(c0.AdvancedPose.IsEmpty && Same(b0, s.ToBytes(Opts())), "AS2: cleared, the file is byte-equal to one that never had a block");

            // A hand-made empty member reads and is dropped silently.
            var hand = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "scene.json", j => ((JObject)j["nodes"][0]["chara"]).Add("advancedPose", new JObject())), "scene.json");
            var lh = SceneContainer.Load(hand, LOpts());
            t.Check(lh.Ok && lh.Findings.Count == 0 && Same(EntryBytes(b0, "scene.json"), EntryBytes(lh.Value.ToBytes(Opts()), "scene.json")), "AS2: an empty member from a file reads silently and is not written");

            // No other file carries it.
            c0.AdvancedPose = Block();
            var full = s.ToBytes(Opts());
            foreach (var e in ContainerReader.Read(full).Entries)
                if (e.Name != "scene.json" && e.Name.EndsWith(".json", StringComparison.Ordinal))
                    t.Check(!Text(e.Data).Contains("advancedPose"), "AS2: " + e.Name + " does not hold the block");
        }

        // ---------------- AS3 ----------------

        static void AS3(TestRunner t)
        {
            t.Check(SaveFormats.AdvancedPoseVersion == 4 && SaveFormats.SupportedVersion == CoreMigrationRegistry.Default.Supported && SaveFormats.SupportedVersion >= 4 && SaveFormats.MinReaderVersion == 1,
                "AS3: the block at revision 4, minReaderVersion 1");
            var s = InfraFixtures.SmallScene(2, 1);
            var c0 = (CharaNodeState)s.Scene.Nodes[0].State;
            var b1 = s.ToBytes(Opts());
            t.Check(FormatOf(b1) == 1, "AS3: no block, no Timeline: 1");

            int Revision(Action<CharaAdvancedPose> fill)
            {
                c0.AdvancedPose = new CharaAdvancedPose();
                fill(c0.AdvancedPose);
                int r = FormatOf(s.ToBytes(Opts()));
                c0.AdvancedPose = new CharaAdvancedPose();
                return r;
            }
            t.Equal(4, Revision(a => a.Bones["cf_J_Hips"] = new BoneCorrection(new Vec3(0f, 1f, 0f), Vec3.Zero, Vec3.One)), "AS3: a bone correction alone: 4");
            t.Equal(4, Revision(a => a.Chains["other/r"] = new ChainValues { Weight = 0.5f }), "AS3: a chain's values alone: 4");
            t.Equal(4, Revision(a => a.Bust.HipL.Damping = 0.5f), "AS3: a bust member alone: 4");
            t.Equal(4, Revision(a => { var v = new BlendValues(); v.Weights["s"] = 1f; a.BlendShapes["head/h"] = v; }), "AS3: a blend weight alone: 4");
            t.Equal(4, Revision(a => a.Bones["a"] = new BoneCorrection()), "AS3: an identity entry from a hand-made file is kept, so 4");
            t.Equal(1, Revision(a => { }), "AS3: an empty block: 1");

            var man = Entry(WithBlock(s, c0), "manifest.json");
            t.Check((int)man["formatVersion"] == 4 && (int)man["minReaderVersion"] == 1, "AS3: the block's file says revision 4, minReaderVersion 1");

            // With the other optional members: the highest wins; clearing brings each back.
            c0.AdvancedPose = Block();
            var other = (CharaNodeState)s.Scene.Nodes[1].State;
            other.State.NippleThroughClothes = false;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "AS3: with a revision-3 member too: 4");
            c0.AdvancedPose = new CharaAdvancedPose();
            t.Check(FormatOf(s.ToBytes(Opts())) == 3, "AS3: the block cleared: back to 3");
            s.Scene.Timeline = new TimelineDocument();
            c0.AdvancedPose = Block();
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "AS3: a Timeline and a block: 4");
            c0.AdvancedPose = new CharaAdvancedPose();
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "AS3: the Timeline alone: 4");
            s.Scene.Timeline = null;
            other.State.NippleThroughClothes = true;
            t.Check(Same(s.ToBytes(Opts()), b1), "AS3: all cleared: 1, byte-equal to the file before");

            // Revision 2: a clothes part with inline ext; the block raises it, clearing brings it back.
            var withExt = InfraFixtures.SmallChara();
            withExt.Coord.Clothes.Parts[0].Ext.Set("stp3.fixture", JToken.Parse("1"));
            var s2 = InfraFixtures.SmallScene(1, 0, withExt);
            var c2 = (CharaNodeState)s2.Scene.Nodes[0].State;
            var b2 = s2.ToBytes(Opts());
            t.Check(FormatOf(b2) == 2, "AS3: a clothes part's ext alone: 2");
            c2.AdvancedPose = Block();
            t.Check(FormatOf(s2.ToBytes(Opts())) == 4, "AS3: with a block: 4");
            c2.AdvancedPose = new CharaAdvancedPose();
            t.Check(Same(s2.ToBytes(Opts()), b2), "AS3: the block cleared: 2, byte-equal to the file before");
        }

        static byte[] WithBlock(SceneContainer s, CharaNodeState c)
        {
            c.AdvancedPose = Block();
            var b = s.ToBytes(Opts());
            c.AdvancedPose = new CharaAdvancedPose();
            return b;
        }

        // ---------------- the fixture ----------------

        public const string PoseFile = "scene_r4_pose.hs2nscene";
        public const string PoseTitle = "Advanced pose fixture scene";
        public static readonly DateTime PoseClock = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The fixture's path under a save-format fixtures root.</summary>
        public static string PosePath(string fixturesRoot) => System.IO.Path.Combine(fixturesRoot ?? "", "fixtures", "advancedpose", PoseFile);

        /// <summary>The options the fixture was written with: fixed clock, placeholder thumbnail, no ext handlers, app 1.2.0.</summary>
        public static SaveOptions PoseOptions() => new SaveOptions
        {
            Clock = () => PoseClock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.2.0",
        };

        /// <summary>Two synthetic characters (no card data), the first with <see cref="Block"/>, and an item. No Timeline: the block alone makes it revision 4.</summary>
        public static SceneContainer FixtureScene()
        {
            var s = new SceneContainer { Title = PoseTitle };
            s.Scene.Nodes.Add(new SceneNode(CharaA, new CharaNodeState { AdvancedPose = Block() }) { Order = 0 });
            s.PlaceCharacter(CharaA, InfraFixtures.SmallChara(1));
            s.Scene.Nodes.Add(new SceneNode(CharaB, new CharaNodeState()) { Order = 1 });
            s.PlaceCharacter(CharaB, InfraFixtures.SmallChara(0));
            s.Scene.Nodes.Add(new SceneNode(Id(0xa1), new ItemNodeState { Ref = "test.mod#1" }) { Order = 2 });
            return s;
        }

        /// <summary>Writes the fixture once (refuses an existing file); returns its size.</summary>
        public static long WriteFixture(string fixturesRoot)
        {
            string path = PosePath(fixturesRoot);
            if (System.IO.File.Exists(path)) throw new InvalidOperationException("the fixture is never regenerated: " + path + " exists");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var r = FixtureScene().Save(path, PoseOptions());
            if (!r.Ok) throw new InvalidOperationException(r.Error.Message);
            return new System.IO.FileInfo(path).Length;
        }

        static int FormatOf(byte[] file) => (int)Entry(file, "manifest.json")["formatVersion"];
        static JObject Entry(byte[] file, string name) => JObject.Parse(Text(ContainerReader.Read(file).Get(name).Data));
        static byte[] EntryBytes(byte[] file, string name) => ContainerReader.Read(file).Get(name)?.Data;
        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
        static SaveOptions Opts() => InfraFixtures.Opts();

        static LoadOptions LOpts(CoreMigrationRegistry reg = null)
        {
            var o = InfraFixtures.LOpts();
            if (reg != null) o.Migrations = reg;
            return o;
        }

        static CharaNodeState CharaOf(SceneContainer c, string id) => (CharaNodeState)c.Scene.Find(id).State;

        // ---------------- AS4 ----------------

        static void AS4(TestRunner t)
        {
            string path = PosePath(t.FixturesDir);
            if (!System.IO.File.Exists(path)) { t.Check(false, "AS4: fixture " + path + " present"); return; }
            var fixture = System.IO.File.ReadAllBytes(path);
            var now = FixtureScene().ToBytes(PoseOptions());
            foreach (var e in ContainerReader.Read(fixture).Entries)
                t.Check(Same(e.Data, EntryBytes(now, e.Name)), "AS4: the fixture's " + e.Name + " is what this writer writes for it");
            t.Check(FormatOf(fixture) == 4 && (int)Entry(fixture, "manifest.json")["minReaderVersion"] == 1, "AS4: the fixture is revision 4, minReaderVersion 1");
            t.Check(Entry(fixture, "scene.json").Property("timeline") == null, "AS4: and has no Timeline");

            // This reader (S = 4): the values, no finding.
            var l4 = SceneContainer.Load(fixture, LOpts());
            t.Check(l4.Ok && !l4.NewerFormat && l4.Findings.Count == 0, "AS4: an S = 4 reader reads the fixture with no finding (" + l4 + ")");
            if (!l4.Ok) return;
            string blockJson = Block().ToJson();
            t.Equal(blockJson, CharaOf(l4.Value, CharaA).AdvancedPose.ToJson(), "AS4: and the block's values");
            t.Check(CharaOf(l4.Value, CharaB).AdvancedPose.IsEmpty, "AS4: the other character has none");

            // An S = 3 reader: NewerFormat and the one finding.
            var reg3 = new CoreMigrationRegistry(3, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2));
            var l3 = SceneContainer.Load(fixture, LOpts(reg3));
            t.Check(l3.Ok && l3.NewerFormat && l3.Findings.Count == 1 && l3.Findings[0].Code == "manifest.newerFormat" && l3.Findings[0].Message.Contains("format 4 is newer than 3"),
                "AS4: an S = 3 registry reads it with NewerFormat and the single finding manifest.newerFormat (" + l3 + ")");
            if (!l3.Ok) return;

            // What that reader holds: `advancedPose` as an unknown member of the section, written back after `voice`.
            var c = l3.Value;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            var held = CharaOf(c, CharaA);
            held.AdvancedPose = new CharaAdvancedPose();
            held.Unknown = new JsonLeftovers();
            held.Unknown.Add("advancedPose", raw["nodes"][0]["chara"]["advancedPose"].DeepClone());
            string saved = System.IO.Path.Combine(t.TempDir, "as4_downgraded.hs2nscene");
            var sr = c.Save(saved, PoseOptions());
            t.Check(sr.Ok && sr.Downgraded, "AS4: saving it reports Downgraded");
            var back = System.IO.File.ReadAllBytes(saved);
            t.Check(FormatOf(back) >= 1 && FormatOf(back) <= 3 && (int)Entry(back, "manifest.json")["minReaderVersion"] == 1, "AS4: written as its own lowest covering revision (" + FormatOf(back) + ")");
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "AS4: scene.json byte-equal (the block written back after voice)\n    " +
                DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));

            // This reader reads that file back: the same values, silently; and writes revision 4 again.
            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && !lb.NewerFormat && lb.Findings.Count == 0, "AS4: an S = 4 reader then reads it silently (" + lb + ")");
            if (!lb.Ok) return;
            t.Equal(blockJson, CharaOf(lb.Value, CharaA).AdvancedPose.ToJson(), "AS4: with the same values");
            t.Check(FormatOf(lb.Value.ToBytes(PoseOptions())) == 4, "AS4: and writes revision 4 again");
        }

        // ---------------- AS5 ----------------

        static void AS5(TestRunner t)
        {
            BadAddresses(t);
            UnknownAndTypes(t);
            Ranges(t);
            Identity(t);
            Limits(t);
            ThroughTheFile(t);
        }

        static List<string> Codes(FindingList f, string code)
        {
            var r = new List<string>();
            for (int i = 0; i < f.Count; i++) if (f[i].Code == code) r.Add(f[i].Path);
            return r;
        }

        static void BadAddresses(TestRunner t)
        {
            string acc = "acc:" + Id(7);
            var boneBad = new[] { "hair:4/a", "hair:01/a", "hair:x/a", "hair:1", "hair:1/", "clothes:8/a", "clothes:-1/a", "acc:zz/a", "acc:" + Id(7).ToUpperInvariant().Replace('0', 'A') + "/a", "a/b", "head/a", "body/a", "other/a", "hair:1/a/b", "hair:1/a#0", "acc:" };
            var boneGood = new[] { "cf_J_Hips", "a#2", "name with space", "n:x", "hair:0/a", "hair:3/a#1", "clothes:0/a", "clothes:7/b#12", acc + "/N_move#2" };
            var o = new JObject();
            foreach (var k in boneBad.Concat(boneGood)) o[k] = JObject.Parse("{\"pos\": [1, 0, 0]}");
            var f = new FindingList();
            var d = CharaAdvancedPose.FromToken(new JObject { ["bones"] = o }, f);
            t.Equal(boneBad.Length, f.CountOf(AdvancedPoseFindings.BadAddress), "AS5: each bone key that is not an address has its finding");
            t.Check(Codes(f, AdvancedPoseFindings.BadAddress).All(p => p.Contains("bones")), "AS5: located under bones");
            t.Check(f.Count == boneBad.Length && Enumerable.Range(0, f.Count).All(i => f[i].Severity == FindingSeverity.Warning), "AS5: warnings, and nothing else reported (" + f + ")");
            t.Equal(boneBad.Length + boneGood.Length, d.Bones.Count, "AS5: every bad key is kept");
            t.Equal(string.Join(",", boneGood.OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", d.AppliedBones().Select(kv => kv.Key)), "AS5: only the good keys act");
            t.Check(d.ToJObject()["bones"]["hair:4/a"]["pos"][0].Value<float>() == 1f, "AS5: a bad key is written back with its value");
            t.Check(d.Clone().ToBytes().SequenceEqual(d.ToBytes()), "AS5: and stays");
            var ef = new FindingList();
            var ed = CharaAdvancedPose.Parse("{\"bones\": {\"\": {\"pos\": [1, 0, 0]}}}", ef);
            t.Check(ef.CountOf(AdvancedPoseFindings.BadAddress) == 1 && ed.Bones.Count == 1 && !ed.AppliedBones().Any(), "AS5: an empty key is reported, kept and does not act");

            var chainBad = new[] { "root", "head/a", "body/a", "other", "other/", "hair:9/a", "other/a/b", "other/a#0" };
            var chainGood = new[] { "other/a", "other/a#1", "hair:2/a", "clothes:1/a#3", acc + "/a" };
            var co = new JObject();
            foreach (var k in chainBad.Concat(chainGood)) co[k] = JObject.Parse("{\"weight\": 0.5}");
            var cf = new FindingList();
            var cd = CharaAdvancedPose.FromToken(new JObject { ["chains"] = co }, cf);
            t.Equal(chainBad.Length, cf.CountOf(AdvancedPoseFindings.BadAddress), "AS5: each chain key that is not an address has its finding");
            t.Equal(chainBad.Length + chainGood.Length, cd.Chains.Count, "AS5: kept");
            t.Equal(string.Join(",", chainGood.OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", cd.AppliedChains().Select(kv => kv.Key)), "AS5: only the good chain keys act");

            var blendBad = new[] { "o_head", "other/a", "head", "head/", "clothes:9/a", "head/a/b", "head/a#0" };
            var blendGood = new[] { "head/o_head", "body/o_body#1", "hair:0/m", "clothes:7/m", acc + "/m#2" };
            var bo = new JObject();
            foreach (var k in blendBad.Concat(blendGood)) bo[k] = JObject.Parse("{\"weights\": {\"s\": 1}}");
            var bf = new FindingList();
            var bd = CharaAdvancedPose.FromToken(new JObject { ["blendShapes"] = bo }, bf);
            t.Equal(blendBad.Length, bf.CountOf(AdvancedPoseFindings.BadAddress), "AS5: each renderer key that is not an address has its finding");
            t.Equal(blendBad.Length + blendGood.Length, bd.BlendShapes.Count, "AS5: kept");
            t.Equal(string.Join(",", blendGood.OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", bd.AppliedWeights().Select(w => w.Renderer)), "AS5: only the good renderer keys act");
        }

        static void UnknownAndTypes(TestRunner t)
        {
            void Bad(string json, string what) => t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaAdvancedPose.Parse(json), "AS5: a type error fails: " + what);
            Bad("[]", "the block is an array");
            Bad("{\"bones\": []}", "bones is an array");
            Bad("{\"bones\": null}", "bones is null");
            Bad("{\"bones\": {\"a\": 1}}", "a bone entry is a number");
            Bad("{\"bones\": {\"a\": null}}", "a bone entry is null");
            Bad("{\"bones\": {\"a\": {\"pos\": \"x\"}}}", "pos is a string");
            Bad("{\"bones\": {\"a\": {\"rot\": [1, 2]}}}", "rot has two numbers");
            Bad("{\"bones\": {\"a\": {\"scale\": null}}}", "scale is null");
            Bad("{\"chains\": 1}", "chains is a number");
            Bad("{\"chains\": {\"other/a\": []}}", "a chain is an array");
            Bad("{\"chains\": {\"other/a\": {\"enabled\": 1}}}", "enabled is a number");
            Bad("{\"chains\": {\"other/a\": {\"enabled\": null}}}", "enabled is null (no null form in a file)");
            Bad("{\"chains\": {\"other/a\": {\"weight\": true}}}", "weight is a boolean");
            Bad("{\"chains\": {\"other/a\": {\"weight\": null}}}", "weight is null");
            Bad("{\"chains\": {\"other/a\": {\"freezeAxis\": 1.5}}}", "freezeAxis is not an integer");
            Bad("{\"chains\": {\"other/a\": {\"gravity\": [0, 0]}}}", "gravity has two numbers");
            Bad("{\"bust\": []}", "bust is an array");
            Bad("{\"bust\": null}", "bust is null");
            Bad("{\"bust\": {\"breastL\": 1}}", "a bust chain is a number");
            Bad("{\"bust\": {\"hipR\": {\"force\": 1}}}", "force is a number");
            Bad("{\"blendShapes\": {\"head/h\": []}}", "a renderer is an array");
            Bad("{\"blendShapes\": {\"head/h\": {\"weights\": []}}}", "weights is an array");
            Bad("{\"blendShapes\": {\"head/h\": {\"weights\": {\"s\": \"x\"}}}}", "a weight is a string");
            Bad("{\"blendShapes\": {\"head/h\": {\"weights\": {\"s\": null}}}}", "a weight is null");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => CharaAdvancedPose.Parse("{\"bones\": {\"a\": {\"pos\": [1e300, 0, 0]}}}"), "AS5: a number outside float32 is NonFinite");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaAdvancedPose.Parse("{\"bones\": {\"a\": {}, \"a\": {}}}"), "AS5: a repeated key fails as everywhere");

            // Lenient: the type error becomes a finding and the absent value.
            var lf = new FindingList();
            var ld = CharaAdvancedPose.Parse("{\"chains\": {\"other/a\": {\"weight\": \"x\", \"enabled\": null, \"damping\": 0.5}}}", lf, "t", true);
            t.Check(lf.CountOf("json.typeError") == 2 && !ld.Chains["other/a"].Weight.HasValue && !ld.Chains["other/a"].Enabled.HasValue && ld.Chains["other/a"].Damping == 0.5f, "AS5: a lenient reader reports and uses the absent value (" + lf + ")");

            // In the section: the same, through a node.
            var node = DocReader.Parse(DocIo.Bytes(OneNode()), SceneDocument.EntryName);
            ((JObject)node["nodes"][0]["chara"]).Add("advancedPose", 5);
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(node, SceneDocument.EntryName)), "AS5: the member as a number fails the document");
            node["nodes"][0]["chara"]["advancedPose"] = JValue.CreateNull();
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(node, SceneDocument.EntryName)), "AS5: the member as null fails the document");
        }

        static SceneDocument OneNode()
        {
            var d = new SceneDocument();
            d.Nodes.Add(new SceneNode(CharaA, new CharaNodeState()));
            return d;
        }

        static void Ranges(TestRunner t)
        {
            const string json = "{\"bones\": {\"a\": {\"pos\": [1001, -2000, 5], \"rot\": [100000, -7, 0], \"scale\": [0, 2000, 1]}, \"b\": {\"pos\": [1000, -1000, 0], \"scale\": [0.001, 1000, 1]}}, " +
                                "\"chains\": {\"other/r\": {\"weight\": 1.5, \"damping\": -0.5, \"elasticity\": 2, \"stiffness\": -1, \"inert\": 3, \"radius\": -1, \"gravity\": [99, 99, 99], \"freezeAxis\": 7}}, " +
                                "\"bust\": {\"breastL\": {\"damping\": 2, \"inert\": -2, \"gravity\": [9, 9, 9]}}, " +
                                "\"blendShapes\": {\"head/h\": {\"weights\": {\"low\": -201, \"high\": 301, \"ok\": 300}}}}";
            var f = new FindingList();
            var d = CharaAdvancedPose.Parse(json, f);
            // Bone a: pos, scale. The chain: weight, damping, elasticity, stiffness, inert, radius, freezeAxis. Bust: damping, inert. Blend: low, high.
            t.Equal(2 + 7 + 2 + 2, f.CountOf(AdvancedPoseFindings.Range), "AS5: each value out of range has its finding (" + f + ")");
            t.Check(Enumerable.Range(0, f.Count).All(i => f[i].Code == AdvancedPoseFindings.Range && f[i].Severity == FindingSeverity.Info), "AS5: info, and nothing else");
            t.Check(d.Bones["a"].Pos.Equals(new Vec3(1001f, -2000f, 5f)) && d.Bones["a"].Scale.Equals(new Vec3(0f, 2000f, 1f)) && d.Chains["other/r"].Weight == 1.5f && d.BlendShapes["head/h"].Weights["high"] == 301f, "AS5: the values are kept");
            t.Check(CharaAdvancedPose.Parse(d.ToJson()).ToBytes().SequenceEqual(d.ToBytes()), "AS5: and written back");
            t.Check(d.Bones["a"].AppliedPos.Equals(new Vec3(1000f, -1000f, 5f)) && d.Bones["a"].AppliedScale.Equals(new Vec3(0.001f, 1000f, 1f)) && d.Bones["a"].Rot.X == 100000f, "AS5: a bone's values clamp when applied; a rotation has no range");
            var c = d.Chains["other/r"].Applied();
            t.Check(c.Weight == 1f && c.Damping == 0f && c.Elasticity == 1f && c.Stiffness == 0f && c.Inert == 1f && c.Radius == 0f && c.FreezeAxis == 3 && c.Gravity.Value.Equals(new Vec3(99f, 99f, 99f)) && !c.Enabled.HasValue && !c.Force.HasValue, "AS5: a chain's values clamp when applied");
            var b = d.Bust.BreastL.Applied();
            t.Check(b.Damping == 1f && b.Inert == 0f && b.Gravity.Value.Equals(new Vec3(9f, 9f, 9f)) && !b.Force.HasValue, "AS5: a bust chain's values clamp when applied");
            var w = d.AppliedWeights().ToDictionary(x => x.Shape, x => x.Weight);
            t.Check(w["low"] == -200f && w["high"] == 300f && w["ok"] == 300f, "AS5: blend weights clamp when applied");
            t.Check(d.Bones["b"].AppliedPos.Equals(d.Bones["b"].Pos) && d.Bones["b"].AppliedScale.Equals(d.Bones["b"].Scale), "AS5: the range's ends are in range");
            var ok = new FindingList();
            CharaAdvancedPose.Parse("{\"chains\": {\"other/r\": {\"weight\": 0, \"damping\": 1, \"radius\": 0, \"freezeAxis\": 3}}, \"blendShapes\": {\"head/h\": {\"weights\": {\"a\": -200, \"b\": 300}}}}", ok);
            t.Equal(0, ok.Count, "AS5: the ends raise no finding");
        }

        static void Identity(TestRunner t)
        {
            var f = new FindingList();
            var d = CharaAdvancedPose.Parse("{\"bones\": {\"a\": {\"pos\": [0, 0, 0], \"rot\": [0, 0, 0], \"scale\": [1, 1, 1]}, \"b\": {}, \"c\": {\"rot\": [0, -0.0, 0]}, \"d\": {\"scale\": [1, 1, 2]}}}", f);
            t.Check(f.Count == 0, "AS5: an identity entry raises no finding (" + f + ")");
            t.Check(d.Bones.Count == 4 && !d.IsEmpty && d.Bones["a"].IsIdentity && d.Bones["b"].IsIdentity && d.Bones["c"].IsIdentity && !d.Bones["d"].IsIdentity, "AS5: it is kept");
            t.Equal("d", string.Join(",", d.AppliedBones().Select(kv => kv.Key)), "AS5: and does nothing");
            t.Check(d.Bones["d"].IsPosIdentity && d.Bones["d"].IsRotIdentity && !d.Bones["d"].IsScaleIdentity, "AS5: identity by channel");
        }

        static void Limits(TestRunner t)
        {
            JObject Bones(int n)
            {
                var o = new JObject();
                for (int i = 0; i < n; i++) o["b" + i.ToString("d5")] = JObject.Parse("{\"rot\": [1, 0, 0]}");
                return o;
            }
            var f0 = new FindingList();
            var at = CharaAdvancedPose.FromToken(new JObject { ["bones"] = Bones(AdvancedPoseLimits.MaxBones) }, f0);
            t.Check(f0.Count == 0 && at.AppliedBones().Count() == AdvancedPoseLimits.MaxBones, "AS5: 1,024 bones are within the limit");
            var f1 = new FindingList();
            var over = CharaAdvancedPose.FromToken(new JObject { ["bones"] = Bones(AdvancedPoseLimits.MaxBones + 3) }, f1);
            t.Check(f1.CountOf(AdvancedPoseFindings.Limit) == 1 && f1.Has(AdvancedPoseFindings.Limit) && f1[0].Severity == FindingSeverity.Warning && f1[0].Message.Contains("3 "), "AS5: 1,027 bones: one warning naming the excess (" + f1 + ")");
            t.Equal(AdvancedPoseLimits.MaxBones + 3, over.Bones.Count, "AS5: the excess is kept in the document");
            t.Equal(AdvancedPoseLimits.MaxBones + 3, over.ToJObject()["bones"].Count(), "AS5: and written back");
            var applied = over.AppliedBones().ToList();
            t.Check(applied.Count == AdvancedPoseLimits.MaxBones && applied[applied.Count - 1].Key == "b" + (AdvancedPoseLimits.MaxBones - 1).ToString("d5"), "AS5: the first 1,024 in key order act");

            var chains = new JObject();
            for (int i = 0; i < AdvancedPoseLimits.MaxChains + 1; i++) chains["other/c" + i.ToString("d4")] = JObject.Parse("{\"weight\": 1}");
            var f2 = new FindingList();
            var cd = CharaAdvancedPose.FromToken(new JObject { ["chains"] = chains }, f2);
            t.Check(f2.CountOf(AdvancedPoseFindings.Limit) == 1 && cd.Chains.Count == AdvancedPoseLimits.MaxChains + 1 && cd.AppliedChains().Count() == AdvancedPoseLimits.MaxChains, "AS5: 257 chains: kept, one warning, 256 act");

            var renderers = new JObject();
            for (int i = 0; i < AdvancedPoseLimits.MaxRenderers + 1; i++) renderers["head/r" + i.ToString("d3")] = JObject.Parse("{\"weights\": {\"s\": 1}}");
            var f3 = new FindingList();
            var rd = CharaAdvancedPose.FromToken(new JObject { ["blendShapes"] = renderers }, f3);
            t.Check(f3.CountOf(AdvancedPoseFindings.Limit) == 1 && rd.BlendShapes.Count == AdvancedPoseLimits.MaxRenderers + 1 && rd.AppliedWeights().Count() == AdvancedPoseLimits.MaxRenderers, "AS5: 65 renderers: kept, one warning, 64 act");

            var many = new JObject();
            for (int r = 0; r < 2; r++)
            {
                var w = new JObject();
                for (int i = 0; i < 600; i++) w["s" + i.ToString("d4")] = 1;
                many["head/r" + r] = new JObject { ["weights"] = w };
            }
            var f4 = new FindingList();
            var wd = CharaAdvancedPose.FromToken(new JObject { ["blendShapes"] = many }, f4);
            t.Check(f4.CountOf(AdvancedPoseFindings.Limit) == 1 && f4[0].Message.Contains("176 ") && wd.BlendShapes["head/r1"].Weights.Count == 600 && wd.AppliedWeights().Count() == AdvancedPoseLimits.MaxWeights, "AS5: 1,200 weights: kept, one warning, 1,024 act (" + f4 + ")");

            // Keys that do not act and identity entries do not count.
            var o2 = Bones(AdvancedPoseLimits.MaxBones);
            o2["hair:9/x"] = JObject.Parse("{\"rot\": [1, 0, 0]}");
            o2["zz"] = new JObject();
            var f5 = new FindingList();
            var d5 = CharaAdvancedPose.FromToken(new JObject { ["bones"] = o2 }, f5);
            t.Check(f5.CountOf(AdvancedPoseFindings.Limit) == 0 && f5.CountOf(AdvancedPoseFindings.BadAddress) == 1 && d5.AppliedBones().Count() == AdvancedPoseLimits.MaxBones, "AS5: a bad key and an identity entry do not count for the limit (" + f5 + ")");
        }

        static void ThroughTheFile(TestRunner t)
        {
            // A whole scene with such a block: it loads, with the findings at their paths, and is written back unchanged.
            const string json = "{\"bones\": {\"hair:9/x\": {\"pos\": [1, 0, 0]}, \"y\": {\"pos\": [2000, 0, 0]}}}";
            var s = InfraFixtures.SmallScene(1, 0);
            var b0 = s.ToBytes(Opts());
            var hand = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "scene.json", j => ((JObject)j["nodes"][0]["chara"]).Add("advancedPose", JObject.Parse(json))), "scene.json");
            var l = SceneContainer.Load(hand, LOpts());
            t.Check(l.Ok && !l.NewerFormat, "AS5: the scene loads (" + l + ")");
            if (!l.Ok) return;
            var bad = Codes(l.Findings, AdvancedPoseFindings.BadAddress);
            var range = Codes(l.Findings, AdvancedPoseFindings.Range);
            t.Check(bad.Count == 1 && bad[0].StartsWith("scene.json", StringComparison.Ordinal) && bad[0].Contains("nodes[0].chara.advancedPose.bones") && bad[0].Contains("hair:9/x"), "AS5: the address finding names entry, path and key (" + string.Join(";", bad) + ")");
            t.Check(range.Count == 1 && range[0].Contains("nodes[0].chara.advancedPose.bones.y.pos"), "AS5: the range finding names the member (" + string.Join(";", range) + ")");
            var re = l.Value.ToBytes(Opts());
            t.Check(FormatOf(re) == 4, "AS5: such a block makes the file revision 4");
            var j2 = Entry(re, "scene.json")["nodes"][0]["chara"]["advancedPose"];
            t.Check(j2["bones"]["hair:9/x"] != null && (float)j2["bones"]["y"]["pos"][0] == 2000f, "AS5: both are written back");
        }
    }
}
