// The materialOverrides arrays of an item node and of scene.map (format revision 4). MF1 the schema round trip: place in the object,
// every type, unknown types and members, numbers kept as text, write -> read -> write byte-stable. MF2 a scene without them writes the
// frozen bytes (goldens, the v0.2 fixture, the Timeline and advanced-pose fixtures, an array emptied). MF3 revision 4 exactly when a
// Timeline, a non-empty block, a non-empty array or a constraint exists, the lower revisions otherwise, and clearing brings the next save back.
// MF4 the reader's rules: absent, JSON null, wrong types, an unknown type, a revision-1 file holding the member. MF5 the committed
// fixture (Tests/SaveFormat/fixtures/materialedit/scene_r4_materials.hs2nscene: what this writer writes for FixtureScene()), read by this
// reader silently and by a revision-3 registry with NewerFormat and the single finding, written back by that reader with both
// members as unknown ones (scene.json byte-equal, Downgraded) and read back here with the same arrays. MF7 a `res` entry named only by
// these arrays is written, one named by nothing is dropped. Synthetic ids and names only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_MaterialEdit
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string ItemA = Id(0xb1), ItemB = Id(0xb2);
        const string Member = "materialOverrides";

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
        static string Names(JToken o) => string.Join(",", ((JObject)o).Properties().Select(p => p.Name));

        /// <summary>The two fixture images: PNG and JPEG signatures, deterministic, not images.</summary>
        public static byte[] PngBytes() => InfraFixtures.FakePng(641);
        public static byte[] JpegBytes()
        {
            var b = new byte[256];
            new Random(642).NextBytes(b);
            b[0] = 0xff; b[1] = 0xd8; b[2] = 0xff;
            return b;
        }

        /// <summary>One item of every type, a queue-only shader, numbers kept as text, an unknown type and an unknown member.</summary>
        public static List<MaterialOverride> AllTypes(string image, string renderer)
        {
            var withMember = new MeFloat { Material = "mat_a", Property = "Glossiness", Value = 0.125f, Original = 0.5f };
            withMember.Unknown = new JsonLeftovers();
            withMember.Unknown.Add("future", JToken.Parse("{\"a\": 1}"));
            return new List<MaterialOverride>
            {
                new MeCopy { Material = "mat_a", CopyName = "mat_a.MECopy1" },
                new MeName { Renderer = renderer, Material = "mat_a", Value = "mat_z", Original = "mat_a" },
                new MeShader { Material = "mat_a", Shader = "HS2Next/AIT Clothes True", ShaderOriginal = "AIT/Clothes", RenderQueue = 3000, RenderQueueOriginal = 2450 },
                new MeShader { Material = "mat_b", RenderQueue = 2460, RenderQueueOriginal = 2450 },
                new MeRenderer { Renderer = renderer, Property = "Enabled", Value = 0, Original = 1 },
                new MeRenderer { Renderer = renderer, Property = "ReceiveShadows", ValueText = "yes", OriginalText = "no" },
                new MeFloat { Material = "mat_a", Property = "Metallic", Value = 0.1f, Original = 0.25f },
                new MeFloat { Material = "mat_a", Property = "_dirty", ValueText = "1,5", OriginalText = "2,5" },
                withMember,
                new MeKeyword { Material = "mat_a", Property = "DITHERSWITCH_ON", Value = true, Original = false },
                new MeColor { Material = "mat_a", Property = "Color", Value = new Color4(1.5f, -0.25f, 0f, 1f), Original = new Color4(1f, 1f, 1f, 1f) },
                new MeVector { Material = "mat_a", Property = "DetailUV", Value = new Vec4(1f, 2f, 3f, 4f), Original = new Vec4(0f, 0f, 0f, 0f) },
                new MeTexture { Material = "mat_a", Property = "MainTex", Image = image, Offset = new Vec2(0.5f, 0f), OffsetOriginal = new Vec2(0f, 0f), Scale = new Vec2(2f, 2f), ScaleOriginal = new Vec2(1f, 1f) },
                new MeTexture { Material = "mat_b", Property = "BumpMap", Scale = new Vec2(3f, 3f) },
                new MeCubemap { Material = "mat_a", Property = "Cube" },
                new MeProjector { Projector = "proj_a", Property = "FieldOfView", Value = "60", Original = "45" },
                new MeUnknown { TypeName = "future", Raw = JObject.Parse("{\"type\": \"future\", \"material\": \"mat_a\", \"number\": 1.50, \"list\": [1, 2]}") },
            };
        }

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            MF1(t);
            MF2(t);
            MF3(t);
            MF4(t);
            MF5(t);
            MF7(t);
        }

        static SceneDocument Doc(List<MaterialOverride> item, List<MaterialOverride> map)
        {
            var d = new SceneDocument();
            d.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "test.mod#1", MaterialOverrides = item ?? new List<MaterialOverride>() }));
            d.Scene.Map.Ref = "test.mod#2";
            d.Scene.Map.MaterialOverrides = map ?? new List<MaterialOverride>();
            return d;
        }

        static ItemNodeState ItemOf(SceneDocument d) => d.Nodes.Select(n => n.State).OfType<ItemNodeState>().First();

        // ---------------- MF1 ----------------

        static void MF1(TestRunner t)
        {
            string res = "res/" + new string('a', 64) + ".png";
            var doc = Doc(AllTypes(res, "."), AllTypes(res, "root/part[1]/mesh"));
            var b1 = DocIo.Bytes(doc);
            var f = new FindingList();
            var back = DocIo.Scene(b1, f);
            var b2 = DocIo.Bytes(back);
            t.Check(b1.SequenceEqual(b2), "MF1: write -> read -> write byte-stable\n    " + DocIo.FirstDiff(b1, b2));
            t.Equal(2, f.CountOf("json.unknownEnum"), "MF1: only the unknown type is reported, once per array (" + f + ")");
            t.Equal(AllTypes(res, ".").Count, ItemOf(back).MaterialOverrides.Count, "MF1: every item of the item's array is read");
            t.Equal(AllTypes(res, ".").Count, back.Scene.Map.MaterialOverrides.Count, "MF1: every item of the map's array is read");
            t.Check(ItemOf(back).MaterialOverrides.Last() is MeUnknown u && u.TypeName == "future", "MF1: the unknown type is kept as an unknown item");

            var j = DocReader.Parse(b1, SceneDocument.EntryName);
            t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options,materialOverrides", Names(j["nodes"][0]["item"]), "MF1: the last member of the item section");
            t.Equal("ref,pos,rot,options,lights,materialOverrides", Names(j["scene"]["map"]), "MF1: the last member of scene.map");
            var a = (JArray)j["scene"]["map"][Member];
            t.Equal("copy,name,shader,shader,renderer,renderer,float,float,float,keyword,color,vector,texture,texture,cubemap,projector,future", string.Join(",", a.Select(e => (string)e["type"])), "MF1: array order is model order");
            t.Equal("type,material,property,value,original,future", Names(a[8]), "MF1: an element's unknown member after the known ones");
            t.Equal("type,material,number,list", Names(a[16]), "MF1: an unknown element keeps its members in read order");
            t.Equal("root/part[1]/mesh", (string)a[4]["renderer"], "MF1: a renderer by path is plain text");
            t.Equal(".", (string)j["nodes"][0]["item"][Member][4]["renderer"], "MF1: the instance's own renderer is `.`");
            t.Check(a[7]["value"].Type == JTokenType.String && (string)a[7]["value"] == "1,5" && (string)a[7]["original"] == "2,5", "MF1: numbers kept as text are strings");
            t.Check(a[5]["value"].Type == JTokenType.String, "MF1: a renderer's text too");

            // An unsorted array stays in its order, on both.
            var mixed = new List<MaterialOverride>
            {
                new MeColor { Material = "m", Property = "b", Value = Color4.White },
                new MeFloat { Material = "m", Property = "a", Value = 1f },
                new MeShader { Material = "m", RenderQueue = 1 },
            };
            var mb = DocIo.Bytes(Doc(mixed, mixed));
            t.Equal("color,float,shader", string.Join(",", ((JArray)DocReader.Parse(mb, SceneDocument.EntryName)["nodes"][0]["item"][Member]).Select(e => (string)e["type"])), "MF1: neither reader nor writer sorts");
            t.Check(DocIo.Bytes(DocIo.Scene(mb)).SequenceEqual(mb), "MF1: and it is stable");

            // After them, in read order, come the object's own unknown members.
            var withUnknown = (JObject)j.DeepClone();
            ((JObject)withUnknown["nodes"][0]["item"]).Add("future", 1);
            ((JObject)withUnknown["scene"]["map"]).Add("future", 2);
            var d2 = DocIo.Scene(CanonicalJsonWriter.ToBytes(withUnknown, SceneDocument.EntryName));
            var j2 = DocReader.Parse(DocIo.Bytes(d2), SceneDocument.EntryName);
            t.Equal("ref,group,category,anime,colors,alpha,emission,lightCancel,panel,fk,dynamicBones,options,materialOverrides,future", Names(j2["nodes"][0]["item"]), "MF1: the item's unknown members follow it");
            t.Equal("ref,pos,rot,options,lights,materialOverrides,future", Names(j2["scene"]["map"]), "MF1: the map's too");

            // Other nodes and character files do not carry them.
            var c = new SceneDocument();
            c.Nodes.Add(new SceneNode(Id(1), new CharaNodeState()));
            c.Nodes.Add(new SceneNode(Id(2), new FolderNodeState()));
            t.Check(!Text(DocIo.Bytes(c)).Contains(Member), "MF1: no other node writes the member");

            // Random arrays.
            int stable = 0;
            const int seeds = 300;
            for (int seed = 0; seed < seeds; seed++)
            {
                var g = new Random(seed);
                var d = Doc(RandomArray(g), RandomArray(g));
                var rb = DocIo.Bytes(d);
                var rf = new FindingList();
                var again = DocIo.Bytes(DocIo.Scene(rb, rf));
                if (rb.SequenceEqual(again) && rf.Count == Unknowns(d)) stable++;
                else t.Info("seed " + seed + ": " + rf);
            }
            t.Equal(seeds, stable, "MF1: random arrays are byte-stable, with a finding per unknown item only");
        }

        static int Unknowns(SceneDocument d) => ItemOf(d).MaterialOverrides.Concat(d.Scene.Map.MaterialOverrides).Count(x => x is MeUnknown);

        static List<MaterialOverride> RandomArray(Random g)
        {
            float F() => (float)(g.NextDouble() * 20.0 - 10.0);
            float? FN() => g.Next(2) == 0 ? F() : (float?)null;
            Vec2? V() => g.Next(2) == 0 ? new Vec2(F(), F()) : (Vec2?)null;
            string S() => "n" + g.Next(50) + (g.Next(4) == 0 ? "é'\"\\" : "");
            string Res() => g.Next(2) == 0 ? null : "res/" + g.Next(1 << 30).ToString("x64") + (g.Next(2) == 0 ? ".png" : ".jpg");
            var r = new List<MaterialOverride>();
            for (int i = g.Next(12); i > 0; i--)
            {
                switch (g.Next(11))
                {
                    case 0: r.Add(new MeCopy { Material = S(), CopyName = S() }); break;
                    case 1: r.Add(new MeName { Renderer = S(), Material = S(), Value = S(), Original = g.Next(2) == 0 ? null : S() }); break;
                    case 2: r.Add(new MeShader { Material = S(), Shader = g.Next(2) == 0 ? null : S(), ShaderOriginal = g.Next(2) == 0 ? null : S(), RenderQueue = g.Next(2) == 0 ? (int?)null : g.Next(5001), RenderQueueOriginal = g.Next(2) == 0 ? (int?)null : g.Next(5001) }); break;
                    case 3: r.Add(g.Next(2) == 0 ? new MeRenderer { Renderer = S(), Property = "Enabled", Value = g.Next(2), Original = g.Next(2) } : new MeRenderer { Renderer = S(), Property = "Enabled", ValueText = S(), OriginalText = S() }); break;
                    case 4: r.Add(g.Next(3) == 0 ? new MeFloat { Material = S(), Property = S(), ValueText = S(), OriginalText = S() } : new MeFloat { Material = S(), Property = S(), Value = F(), Original = FN() }); break;
                    case 5: r.Add(new MeKeyword { Material = S(), Property = S(), Value = g.Next(2) == 0, Original = g.Next(2) == 0 }); break;
                    case 6: r.Add(new MeColor { Material = S(), Property = S(), Value = new Color4(F(), F(), F(), F()), Original = g.Next(2) == 0 ? new Color4(F(), F(), F(), F()) : (Color4?)null }); break;
                    case 7: r.Add(new MeVector { Material = S(), Property = S(), Value = new Vec4(F(), F(), F(), F()), Original = g.Next(2) == 0 ? new Vec4(F(), F(), F(), F()) : (Vec4?)null }); break;
                    case 8: r.Add(new MeTexture { Material = S(), Property = S(), Image = Res(), Offset = V(), OffsetOriginal = V(), Scale = V(), ScaleOriginal = V() }); break;
                    case 9: r.Add(new MeCubemap { Material = S(), Property = S(), Image = Res() }); break;
                    default: r.Add(new MeUnknown { TypeName = "future", Raw = new JObject { ["type"] = "future", ["x"] = g.Next(100) } }); break;
                }
            }
            return r;
        }

        // ---------------- MF2 ----------------

        static void MF2(TestRunner t)
        {
            var golden = DocTests_T1Golden.Golden("scene.json");
            var doc = DocIo.Scene(golden);
            var items = doc.Nodes.Select(n => n.State).OfType<ItemNodeState>().ToList();
            t.Check(items.Count > 0 && items.All(i => i.MaterialOverrides != null && i.MaterialOverrides.Count == 0), "MF2: the golden scene's items read with an empty array, never null");
            t.Check(doc.Scene.Map.MaterialOverrides != null && doc.Scene.Map.MaterialOverrides.Count == 0, "MF2: and the map");
            t.Check(DocIo.Same(golden, DocIo.Bytes(doc)) && !Text(golden).Contains(Member), "MF2: the golden scene is written byte for byte, without the member");
            t.Check(new ItemNodeState().MaterialOverrides != null && new MapSettings().MaterialOverrides != null, "MF2: a new object's array is never null");

            string v02 = System.IO.Path.Combine(FixtureWriter.V02Dir(t.FixturesDir ?? ""), "scene.hs2nscene");
            if (System.IO.File.Exists(v02))
            {
                var file = System.IO.File.ReadAllBytes(v02);
                var l = SceneContainer.Load(file, LOpts());
                var old = SceneContainer.Load(file, LOpts(Registry3()));
                t.Check(l.Ok && old.Ok && l.Findings.ToString() == old.Findings.ToString(), "MF2: the v0.2 fixture scene loads with the findings of a revision-3 reader (" + l + ")");
                if (l.Ok)
                {
                    var re = l.Value.ToBytes(Opts());
                    t.Check(FormatOf(re) == 1 && Same(EntryBytes(file, "scene.json"), EntryBytes(re, "scene.json")), "MF2: re-saved as format 1, scene.json byte-equal");
                }
            }
            else t.Check(false, "MF2: fixture " + v02 + " present");

            foreach (var (name, path, options) in new[]
            {
                ("the Timeline fixture", DocTests_Timeline.R4Path(t.FixturesDir), DocTests_Timeline.R4Options()),
                ("the advanced-pose fixture", DocTests_AdvancedPose.PosePath(t.FixturesDir), DocTests_AdvancedPose.PoseOptions()),
            })
            {
                if (!System.IO.File.Exists(path)) { t.Check(false, "MF2: fixture " + path + " present"); continue; }
                var file = System.IO.File.ReadAllBytes(path);
                var l = SceneContainer.Load(file, LOpts());
                t.Check(l.Ok && l.Findings.Count == 0 && l.Value.Scene.Scene.Map.MaterialOverrides.Count == 0, "MF2: " + name + " reads with no finding and no array");
                if (l.Ok)
                {
                    var re = l.Value.ToBytes(options);
                    t.Check(FormatOf(re) == 4 && Same(EntryBytes(file, "scene.json"), EntryBytes(re, "scene.json")), "MF2: " + name + " stays byte-equal");
                }
            }

            // A scene with arrays never set, given as empty, and emptied.
            var s = InfraFixtures.SmallScene(1, 2);
            var b0 = s.ToBytes(Opts());
            t.Check(FormatOf(b0) == 1 && !Text(EntryBytes(b0, "scene.json")).Contains(Member), "MF2: a scene without arrays has no member, format 1");
            var item = s.Scene.Nodes.Select(n => n.State).OfType<ItemNodeState>().First();
            item.MaterialOverrides = new List<MaterialOverride>();
            s.Scene.Scene.Map.MaterialOverrides = new List<MaterialOverride>();
            t.Check(Same(b0, s.ToBytes(Opts())), "MF2: explicit empty arrays change no byte");
            item.MaterialOverrides = AllTypes(null, ".");
            s.Scene.Scene.Map.MaterialOverrides = AllTypes(null, "root");
            t.Check(!Same(b0, s.ToBytes(Opts())), "MF2: arrays change the file");
            item.MaterialOverrides.Clear();
            s.Scene.Scene.Map.MaterialOverrides.Clear();
            t.Check(Same(b0, s.ToBytes(Opts())), "MF2: emptied, the file is byte-equal to one that never had them");

            // A hand-made empty member reads and is dropped silently.
            var hand = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "scene.json", j =>
            {
                ((JObject)j["nodes"].Last["item"]).Add(Member, new JArray());
                ((JObject)j["scene"]["map"]).Add(Member, new JArray());
            }), "scene.json");
            var lh = SceneContainer.Load(hand, LOpts());
            t.Check(lh.Ok && lh.Findings.Count == 0 && Same(EntryBytes(b0, "scene.json"), EntryBytes(lh.Value.ToBytes(Opts()), "scene.json")), "MF2: an empty member from a file reads silently and is not written");

            // Character and coordinate files: no change of format, an edited array is ordinary data.
            var chara = InfraFixtures.SmallChara();
            var cb = chara.ToBytes(Opts());
            chara.Chara.MaterialOverrides.Add(new MeShader { Material = "m", Shader = "HS2Next/AIT Clothes True" });
            chara.Coord.Clothes.Parts[0].MaterialOverrides.Add(new MeFloat { Material = "m", Property = "a", Value = 1f });
            chara.Coord.Accessories[0].MaterialOverrides.Add(new MeFloat { Material = "m", Property = "a", Value = 1f });
            chara.Chara.Hair.Back.MaterialOverrides.Add(new MeFloat { Material = "m", Property = "a", Value = 1f });
            var ce = chara.ToBytes(Opts());
            t.Check(FormatOf(cb) == 1 && FormatOf(ce) == 1, "MF2: an edited character file stays at its revision");
            var lc = CharaContainer.Load(ce, LOpts());
            t.Check(lc.Ok && lc.Findings.Count == 0, "MF2: and reads silently, an HS2Next shader name included (" + lc + ")");
            var coord = InfraFixtures.SmallCoord();
            coord.Coord.Clothes.Parts[1].MaterialOverrides.Add(new MeFloat { Material = "m", Property = "a", Value = 1f });
            t.Check(FormatOf(coord.ToBytes(Opts())) == 1, "MF2: a coordinate too");
        }

        // ---------------- MF3 ----------------

        static void MF3(TestRunner t)
        {
            t.Check(SaveFormats.MaterialEditVersion == 4 && SaveFormats.SupportedVersion == CoreMigrationRegistry.Default.Supported && SaveFormats.SupportedVersion >= 4 && SaveFormats.MinReaderVersion == 1,
                "MF3: the arrays at revision 4, no new revision (S = 4), minReaderVersion 1");
            var s = InfraFixtures.SmallScene(2, 1);
            var chara = (CharaNodeState)s.Scene.Nodes[0].State;
            var other = (CharaNodeState)s.Scene.Nodes[1].State;
            var item = s.Scene.Nodes.Select(n => n.State).OfType<ItemNodeState>().First();
            var map = s.Scene.Scene.Map;
            var one = new List<MaterialOverride> { new MeFloat { Material = "m", Property = "a", Value = 1f } };
            var none = new List<MaterialOverride>();
            var b1 = s.ToBytes(Opts());
            t.Check(FormatOf(b1) == 1, "MF3: nothing: 1");

            item.MaterialOverrides = one;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: an item's array alone: 4");
            var man = Entry(s.ToBytes(Opts()), "manifest.json");
            t.Check((int)man["formatVersion"] == 4 && (int)man["minReaderVersion"] == 1, "MF3: the file says revision 4, minReaderVersion 1");
            item.MaterialOverrides = none;
            t.Check(Same(s.ToBytes(Opts()), b1), "MF3: cleared: 1, byte-equal to the file before");
            map.MaterialOverrides = one;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: the map's array alone: 4");
            map.MaterialOverrides = none;
            t.Check(Same(s.ToBytes(Opts()), b1), "MF3: cleared: 1, byte-equal to the file before");
            item.MaterialOverrides = new List<MaterialOverride> { new MeUnknown { TypeName = "future", Raw = JObject.Parse("{\"type\": \"future\"}") } };
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: an array holding only an unknown item: 4");
            item.MaterialOverrides = none;

            // With the other optional members: the highest wins; clearing brings each back.
            other.State.NippleThroughClothes = false;
            var b3 = s.ToBytes(Opts());
            t.Check(FormatOf(b3) == 3, "MF3: a revision-3 member alone: 3");
            map.MaterialOverrides = one;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: with an array too: 4");
            map.MaterialOverrides = none;
            t.Check(Same(s.ToBytes(Opts()), b3), "MF3: the array cleared: back to 3");
            s.Scene.Timeline = new TimelineDocument();
            item.MaterialOverrides = one;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: a Timeline and an array: 4");
            item.MaterialOverrides = none;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: the Timeline alone: 4");
            s.Scene.Timeline = null;
            chara.AdvancedPose = DocTests_AdvancedPose.Block();
            item.MaterialOverrides = one;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: a block and an array: 4");
            item.MaterialOverrides = none;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "MF3: the block alone: 4");
            chara.AdvancedPose = new CharaAdvancedPose();
            t.Check(FormatOf(s.ToBytes(Opts())) == 3, "MF3: the block cleared: 3");
            other.State.NippleThroughClothes = true;
            t.Check(Same(s.ToBytes(Opts()), b1), "MF3: all cleared: 1, byte-equal to the file before");

            // Revision 2: a clothes part with inline ext; an array raises it, clearing brings it back.
            var withExt = InfraFixtures.SmallChara();
            withExt.Coord.Clothes.Parts[0].Ext.Set("stp3.fixture", JToken.Parse("1"));
            var s2 = InfraFixtures.SmallScene(1, 1, withExt);
            var b2 = s2.ToBytes(Opts());
            t.Check(FormatOf(b2) == 2, "MF3: a clothes part's ext alone: 2");
            s2.Scene.Scene.Map.MaterialOverrides = one;
            t.Check(FormatOf(s2.ToBytes(Opts())) == 4, "MF3: with an array: 4");
            s2.Scene.Scene.Map.MaterialOverrides = none;
            t.Check(Same(s2.ToBytes(Opts()), b2), "MF3: the array cleared: 2, byte-equal to the file before");

            // Nothing new in the manifest: the res entries are reached by the scan of scene.json.
            item.MaterialOverrides = one;
            var requires = Entry(s.ToBytes(Opts()), "manifest.json")["requires"]?.ToString();
            item.MaterialOverrides = none;
            t.Equal(Entry(s.ToBytes(Opts()), "manifest.json")["requires"]?.ToString(), requires, "MF3: an array adds no requirement");
        }

        // ---------------- MF4 ----------------

        // The same JSON. The bytes made here from a token have another layout than the writer's in one place: an array of nulls.
        static bool SameDocument(byte[] a, byte[] b) =>
            JToken.DeepEquals(DocReader.Parse(a, SceneDocument.EntryName), DocReader.Parse(b, SceneDocument.EntryName));

        static void MF4(TestRunner t)
        {
            byte[] Scene(Action<JObject> edit)
            {
                var j = DocReader.Parse(DocIo.Bytes(Doc(null, null)), SceneDocument.EntryName);
                edit((JObject)j);
                return CanonicalJsonWriter.ToBytes(j, SceneDocument.EntryName);
            }
            void Bad(Func<JObject, JObject> holder, JToken value, string what) =>
                t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(Scene(j => holder(j).Add(Member, value))), "MF4: " + what + " fails the document");
            JObject OnItem(JObject j) => (JObject)j["nodes"][0]["item"];
            JObject OnMap(JObject j) => (JObject)j["scene"]["map"];
            foreach (var (name, holder) in new (string, Func<JObject, JObject>)[] { ("the item's", OnItem), ("the map's", OnMap) })
            {
                Bad(holder, JValue.CreateNull(), name + " member as null");
                Bad(holder, 5, name + " member as a number");
                Bad(holder, "x", name + " member as a string");
                Bad(holder, new JObject(), name + " member as an object");
                Bad(holder, new JArray(1), name + " element as a number");
                Bad(holder, new JArray(JValue.CreateNull()), name + " element as null");
                Bad(holder, new JArray(new JObject()), name + " element without a type");
                Bad(holder, JArray.Parse("[{\"type\": \"float\", \"material\": \"m\"}]"), name + " element without its required members");
                Bad(holder, JArray.Parse("[{\"type\": \"float\", \"material\": \"m\", \"property\": \"a\", \"value\": true, \"original\": null}]"), name + " value of the wrong type");
                Bad(holder, JArray.Parse("[{\"type\": \"texture\", \"material\": \"m\", \"property\": \"a\", \"image\": \"folder/a.png\", \"offset\": null, \"offsetOriginal\": null, \"scale\": null, \"scaleOriginal\": null}]"), name + " image that is no res name");

                // An unknown type: kept, one warning, written back.
                var f = new FindingList();
                var bytes = Scene(j => holder(j).Add(Member, JArray.Parse("[{\"type\": \"future\", \"x\": 1.50}]")));
                var d = DocIo.Scene(bytes, f);
                t.Check(f.Count == 1 && f[0].Code == "json.unknownEnum" && f[0].Severity == FindingSeverity.Warning, "MF4: " + name + " unknown type is kept with one warning (" + f + ")");
                t.Check(SameDocument(bytes, DocIo.Bytes(d)), "MF4: and written back unchanged");

                // Lenient: the type error becomes a finding and the array is empty.
                var lf = new FindingList();
                var ld = DocIo.Scene(Scene(j => holder(j).Add(Member, 5)), lf, true);
                t.Check(lf.CountOf("json.typeError") == 1 && ItemOf(ld).MaterialOverrides.Count == 0 && ld.Scene.Map.MaterialOverrides.Count == 0, "MF4: a lenient reader reports " + name + " member and reads it empty (" + lf + ")");
            }
            t.Check(ItemOf(DocIo.Scene(Scene(j => { }))).MaterialOverrides.Count == 0, "MF4: absent is empty");

            // Repeated keys, values equal to their original and items that name nothing here: read and kept, no repair.
            var kept = JArray.Parse("[{\"type\": \"float\", \"material\": \"m\", \"property\": \"a\", \"value\": 1.0, \"original\": 1.0}, {\"type\": \"float\", \"material\": \"m\", \"property\": \"a\", \"value\": 2.0, \"original\": null}]");
            var kf = new FindingList();
            var kb = Scene(j => OnItem(j).Add(Member, kept));
            t.Check(SameDocument(kb, DocIo.Bytes(DocIo.Scene(kb, kf))) && kf.Count == 0, "MF4: a repeated key and a no-op item stay, silently (" + kf + ")");

            // A file of a lower revision that holds the member (an older app wrote it back): read silently, written as revision 4.
            foreach (int revision in new[] { 1, 2, 3 })
            {
                var s = InfraFixtures.SmallScene(1, 1);
                var b0 = s.ToBytes(Opts());
                var older = InfraFixtures.Repack(b0, m =>
                {
                    InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = revision);
                    InfraFixtures.EditJson(m, "scene.json", j =>
                    {
                        var a = JArray.Parse("[{\"type\": \"float\", \"material\": \"m\", \"property\": \"a\", \"value\": 1.0, \"original\": null}]");
                        ((JObject)j["nodes"].Last["item"]).Add(Member, a);
                        ((JObject)j["scene"]["map"]).Add(Member, a.DeepClone());
                    });
                }, "scene.json");
                var l = SceneContainer.Load(older, LOpts());
                t.Check(l.Ok && !l.NewerFormat && l.Findings.Count == 0, "MF4: a revision-" + revision + " file with the members reads with no finding (" + l + ")");
                if (!l.Ok) continue;
                var it = l.Value.Scene.Nodes.Select(n => n.State).OfType<ItemNodeState>().First();
                t.Check(it.MaterialOverrides.Count == 1 && l.Value.Scene.Scene.Map.MaterialOverrides.Count == 1 && (it.Unknown == null || it.Unknown.IsEmpty), "MF4: they are known members");
                t.Equal(4, FormatOf(l.Value.ToBytes(Opts())), "MF4: and its next save is revision 4");
            }
            t.Check(CoreMigrationRegistry.Default.Supported == SaveFormats.SupportedVersion, "MF4: no migration was added");
        }

        // ---------------- the fixture ----------------

        public const string MaterialsFile = "scene_r4_materials.hs2nscene";
        public const string MaterialsTitle = "Material edits fixture scene";
        public static readonly DateTime MaterialsClock = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The fixture's path under a save-format fixtures root.</summary>
        public static string MaterialsPath(string fixturesRoot) => System.IO.Path.Combine(fixturesRoot ?? "", "fixtures", "materialedit", MaterialsFile);

        /// <summary>The options the fixture was written with: fixed clock, placeholder thumbnail, no ext handlers, app 1.2.0.</summary>
        public static SaveOptions MaterialsOptions() => new SaveOptions
        {
            Clock = () => MaterialsClock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.2.0",
        };

        /// <summary>An item and the map with an array of every type, each naming an image only it names, and an item without one. No
        /// character, Timeline or block: the arrays alone make it revision 4.</summary>
        public static SceneContainer FixtureScene()
        {
            var s = new SceneContainer { Title = MaterialsTitle };
            string png = s.Res.Add(PngBytes()), jpeg = s.Res.Add(JpegBytes());
            s.Scene.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "test.mod#1", MaterialOverrides = AllTypes(png, ".") }) { Order = 0 });
            s.Scene.Nodes.Add(new SceneNode(ItemB, new ItemNodeState { Ref = "test.mod#2" }) { Order = 1 });
            s.Scene.Scene.Map.Ref = "test.mod#3";
            s.Scene.Scene.Map.MaterialOverrides = AllTypes(jpeg, "map_root/part[1]/mesh");
            return s;
        }

        /// <summary>Writes the fixture once (refuses an existing file); returns its size.</summary>
        public static long WriteFixture(string fixturesRoot)
        {
            string path = MaterialsPath(fixturesRoot);
            if (System.IO.File.Exists(path)) throw new InvalidOperationException("the fixture is never regenerated: " + path + " exists");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var r = FixtureScene().Save(path, MaterialsOptions());
            if (!r.Ok) throw new InvalidOperationException(r.Error.Message);
            return new System.IO.FileInfo(path).Length;
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

        static string Array(List<MaterialOverride> list)
        {
            var d = new SceneDocument();
            d.Scene.Map.MaterialOverrides = list;
            return DocReader.Parse(DocIo.Bytes(d), SceneDocument.EntryName)["scene"]["map"][Member]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
        }

        // ---------------- MF5 ----------------

        static void MF5(TestRunner t)
        {
            string path = MaterialsPath(t.FixturesDir);
            if (!System.IO.File.Exists(path)) { t.Check(false, "MF5: fixture " + path + " present"); return; }
            var fixture = System.IO.File.ReadAllBytes(path);
            var now = FixtureScene().ToBytes(MaterialsOptions());
            foreach (var e in ContainerReader.Read(fixture).Entries)
                t.Check(Same(e.Data, EntryBytes(now, e.Name)), "MF5: the fixture's " + e.Name + " is what this writer writes for it");
            t.Check(FormatOf(fixture) == 4 && (int)Entry(fixture, "manifest.json")["minReaderVersion"] == 1, "MF5: the fixture is revision 4, minReaderVersion 1");
            var sj = Entry(fixture, "scene.json");
            t.Check(sj.Property("timeline") == null && !sj["nodes"].Any(n => n["chara"] != null), "MF5: and has no Timeline and no character");
            string png = ResStore.NameOf(PngBytes()), jpeg = ResStore.NameOf(JpegBytes());
            t.Check(ContainerReader.Read(fixture).Get(png) != null && ContainerReader.Read(fixture).Get(jpeg) != null, "MF5: both images are in it");

            // This reader (S = 4): the arrays. Each holds one unknown item, and that is its only finding.
            var l4 = SceneContainer.Load(fixture, LOpts());
            t.Check(l4.Ok && !l4.NewerFormat && l4.Findings.Count == 2 && l4.Findings.CountOf("json.unknownEnum") == 2, "MF5: an S = 4 reader reads the fixture with only the two unknown items' findings (" + l4 + ")");
            if (!l4.Ok) return;
            string itemJson = Array(AllTypes(png, ".")), mapJson = Array(AllTypes(jpeg, "map_root/part[1]/mesh"));
            var a4 = (ItemNodeState)l4.Value.Scene.Find(ItemA).State;
            t.Equal(itemJson, Array(a4.MaterialOverrides), "MF5: and the item's array");
            t.Equal(mapJson, Array(l4.Value.Scene.Scene.Map.MaterialOverrides), "MF5: and the map's array");
            t.Equal(0, ((ItemNodeState)l4.Value.Scene.Find(ItemB).State).MaterialOverrides.Count, "MF5: the other item has none");

            // An S = 3 reader: NewerFormat and the one finding beside the two of the unknown items, which that reader holds as unknown members.
            var l3 = SceneContainer.Load(fixture, LOpts(Registry3()));
            t.Check(l3.Ok && l3.NewerFormat && l3.Findings.CountOf("manifest.newerFormat") == 1 && l3.Findings.Count == 3, "MF5: an S = 3 registry reads it with NewerFormat and manifest.newerFormat (" + l3 + ")");
            if (!l3.Ok) return;

            // What 1.2.0 holds: both members as unknown ones, written back at the end of their objects.
            var c = l3.Value;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            var held = (ItemNodeState)c.Scene.Find(ItemA).State;
            held.MaterialOverrides = new List<MaterialOverride>();
            held.Unknown = new JsonLeftovers();
            held.Unknown.Add(Member, raw["nodes"][0]["item"][Member].DeepClone());
            var map = c.Scene.Scene.Map;
            map.MaterialOverrides = new List<MaterialOverride>();
            map.Unknown = new JsonLeftovers();
            map.Unknown.Add(Member, raw["scene"]["map"][Member].DeepClone());
            string saved = System.IO.Path.Combine(t.TempDir, "mf5_downgraded.hs2nscene");
            var sr = c.Save(saved, MaterialsOptions());
            t.Check(sr.Ok && sr.Downgraded, "MF5: saving it reports Downgraded");
            var back = System.IO.File.ReadAllBytes(saved);
            t.Check(FormatOf(back) >= 1 && FormatOf(back) <= 3 && (int)Entry(back, "manifest.json")["minReaderVersion"] == 1, "MF5: written as its own lowest covering revision (" + FormatOf(back) + ")");
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "MF5: scene.json byte-equal (both arrays written back at the end of their objects)\n    " +
                DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));
            t.Check(Same(EntryBytes(fixture, png), EntryBytes(back, png)) && Same(EntryBytes(fixture, jpeg), EntryBytes(back, jpeg)), "MF5: the images the arrays name stay reachable, byte-equal");

            // This reader reads that file back: the same arrays, and writes revision 4 again.
            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && !lb.NewerFormat && lb.Findings.Count == 2 && lb.Findings.CountOf("json.unknownEnum") == 2, "MF5: an S = 4 reader then reads it with only the unknown items' findings (" + lb + ")");
            if (!lb.Ok) return;
            var ab = (ItemNodeState)lb.Value.Scene.Find(ItemA).State;
            t.Check(ab.Unknown == null || ab.Unknown.IsEmpty, "MF5: the array is a known member again");
            t.Equal(itemJson, Array(ab.MaterialOverrides), "MF5: with the item's array");
            t.Equal(mapJson, Array(lb.Value.Scene.Scene.Map.MaterialOverrides), "MF5: and the map's");
            t.Check(FormatOf(lb.Value.ToBytes(MaterialsOptions())) == 4, "MF5: and writes revision 4 again");
        }

        // ---------------- MF7 ----------------

        static void MF7(TestRunner t)
        {
            string Name(int seed) => ResStore.NameOf(InfraFixtures.FakePng(seed));
            var s = InfraFixtures.SmallScene(1, 1);
            var item = s.Scene.Nodes.Select(n => n.State).OfType<ItemNodeState>().First();
            var map = s.Scene.Scene.Map;
            string byItem = s.Res.Add(InfraFixtures.FakePng(1)), byMap = s.Res.Add(InfraFixtures.FakePng(2)), byNothing = s.Res.Add(InfraFixtures.FakePng(3)),
                byCubemap = s.Res.Add(InfraFixtures.FakePng(4)), byUnknown = s.Res.Add(InfraFixtures.FakePng(5));
            t.Equal(Name(1), byItem, "MF7: names are by content");
            item.MaterialOverrides = new List<MaterialOverride>
            {
                new MeTexture { Material = "m", Property = "a", Image = byItem },
                new MeCubemap { Material = "m", Property = "c", Image = byCubemap },
                new MeUnknown { TypeName = "future", Raw = new JObject { ["type"] = "future", ["image"] = byUnknown } },
            };
            map.MaterialOverrides = new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = byMap } };

            var b = s.ToBytes(Opts());
            var r = ContainerReader.Read(b);
            t.Check(r.Get(byItem) != null, "MF7: an image named only by an item's array is written");
            t.Check(r.Get(byMap) != null, "MF7: an image named only by the map's array is written");
            t.Check(r.Get(byCubemap) != null, "MF7: a kept cubemap item's image too");
            t.Check(r.Get(byUnknown) != null, "MF7: and an image an unknown item names");
            t.Check(r.Get(byNothing) == null, "MF7: an image nothing names is dropped");
            var l = SceneContainer.Load(b, LOpts());
            t.Check(l.Ok && !l.Findings.Has("res.dangling") && l.Value.Res.Contains(byItem) && l.Value.Res.Contains(byMap), "MF7: the file reads back with its images (" + l + ")");

            // Identical bytes on two targets: one entry.
            map.MaterialOverrides = new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = byItem } };
            var one = ContainerReader.Read(s.ToBytes(Opts()));
            t.Check(one.Get(byItem) != null && one.Get(byMap) == null, "MF7: an image no array names any more leaves the file");
            t.Equal(1, one.Entries.Count(e => e.Name == byItem), "MF7: one entry for two arrays");

            // The store keeps it, so an undo that names it again writes it again.
            t.Check(s.Res.Contains(byMap), "MF7: the store still holds the bytes after the save");
            map.MaterialOverrides = new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = byMap } };
            t.Check(ContainerReader.Read(s.ToBytes(Opts())).Get(byMap) != null, "MF7: named again, it is written again");

            // Hand-edited: a name with no bytes is reported as every res reference is, the array kept.
            item.MaterialOverrides = new List<MaterialOverride> { new MeTexture { Material = "m", Property = "a", Image = Name(99) } };
            var sf = new FindingList();
            var saved = s.Save(System.IO.Path.Combine(t.TempDir, "mf7_dangling.hs2nscene"), Opts());
            t.Check(saved.Ok && saved.Findings.Has("res.dangling"), "MF7: a name with no bytes is res.dangling at save (" + saved.Findings + ")");
            item.MaterialOverrides = new List<MaterialOverride>();
            map.MaterialOverrides = new List<MaterialOverride>();
            t.Equal(0, ContainerReader.Read(s.ToBytes(Opts())).Entries.Count(e => e.Name == byItem || e.Name == byMap), "MF7: arrays emptied, their images leave");
        }
    }
}
