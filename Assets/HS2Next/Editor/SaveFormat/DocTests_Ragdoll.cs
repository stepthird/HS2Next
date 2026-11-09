// The ragdoll blocks of a scene (scene.json members `chara.ragdoll` and `ragdoll`, format revision 5). RF0 write -> read -> write byte-stable, the canonical text; RF1 a scene without a
// block keeps its bytes and its revision, on the committed files of every revision; RF2 the reader's types: every null and every wrong type of a known member fails; RF3 the order of
// the members, in a block, in the section and at the top level; RF4 what empty means, a member written exactly when it holds something, revision 5 exactly when a block is not empty,
// clearing gives the bytes before; RF5 limits, ranges, bad parts and unknown members kept and reported, and what acts; RF6's round trip as far as it runs here (a revision-4 registry over
// this reader, the members moved to the unknown members as 1.3 holds them; the run on 1.3's own save layer is recorded); RF7 save -> load -> save
// byte-identical with a settled pose; RF9 `values` holds only what differs from the default. The fixture Tests/SaveFormat/fixtures/ragdoll/scene_r5_ragdoll.hs2nscene is what this
// writer writes for FixtureScene(). Synthetic ids and values only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_Ragdoll
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string CharaA = Id(0xe1), CharaB = Id(0xe2), CharaC = Id(0xe3), ItemA = Id(0xe4), ItemB = Id(0xe5);

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
        static string Names(JToken o) => string.Join(",", ((JObject)o).Properties().Select(p => p.Name));

        // ---------------- blocks with every member ----------------

        /// <summary>A settled pose: the 16 parts, none at the identity, values a float does not hold exactly.</summary>
        public static RagdollPose Settled()
        {
            var p = new RagdollPose { Hips = new Vec3(1.1f, 9.3f, -2.7f) };
            for (int i = 0; i < RagdollParts.Keys.Count; i++)
            {
                float a = 0.1f + i / 17f, s = (float)Math.Sin(a), c = (float)Math.Cos(a);
                p.Parts[RagdollParts.Keys[i]] = new Quat4(s * 0.6f, s * 0.8f, 0f, c);
            }
            return p;
        }

        /// <summary>Every member present, every value away from its default: a plain pin, one on a node, one on a bone, a tie, two holds, a settled pose.</summary>
        public static CharaRagdoll Block()
        {
            var b = new CharaRagdoll { On = true, Limp = true };
            var v = b.Values;
            v.Muscle = 35f;
            v.MuscleDamping = 0.5f;
            v.Pin = 0.75f;
            v.PinFreq = 15f;
            v.PinFalloff = 4f;
            v.PinHipsOnly = true;
            v.GravityComp = false;
            v.KnockDistance = 6f;
            v.KnockHit = 12f;
            v.DownMuscle = 0.25f;
            v.MinDown = 2f;
            v.GetUp = 3f;
            v.Relocate = false;
            v.FaceCamera = true;
            v.LegCollision = true;
            v.Mass = 62.5f;
            b.Pins.Add(new RagdollPin { Id = "a", Part = "hand_R", Point = new Vec3(0.125f, 0.25f, -0.5f), At = new Vec3(1.5f, 12.25f, -3f) });
            b.Pins.Add(new RagdollPin { Id = "b", Part = "foot_L", Point = new Vec3(0f, 0.5f, 0f), Follow = ItemA, Offset = new Vec3(0f, 0.5f, 0.25f) });
            b.Pins.Add(new RagdollPin { Id = "c", Part = "chest", Follow = CharaB, Bone = "cf_J_Hand_R", Offset = Vec3.Zero });
            b.Ties.Add(new RagdollTie { A = "hand_L", B = "hand_R" });
            b.Holds.Add(new RagdollHold { Part = "forearm_L", Rot = new Quat4(0f, 0.6f, 0f, 0.8f) });
            b.Holds.Add(new RagdollHold { Part = "upperarm_R", Rot = new Quat4(0.28f, 0f, 0f, 0.96f) });
            b.Pose = Settled();
            return b;
        }

        /// <summary>A body and nothing else.</summary>
        public static CharaRagdoll Small() => new CharaRagdoll { On = true };

        /// <summary>Every member present, every value away from its default.</summary>
        public static SceneRagdollWorld World()
        {
            var b = new SceneRagdollWorld();
            var v = b.Values;
            v.Gravity = 50f;
            v.TimeScale = 0.5f;
            v.Hz = 120f;
            v.Friction = 6f;
            v.AngularDrag = 1f;
            v.PushSpeed = 30f;
            v.GrabStiffness = 90f;
            v.GrabMax = 8f;
            v.FollowStiffness = 500f;
            v.FollowMax = 25f;
            v.BlendOut = 0.5f;
            v.Transfer = 0.25f;
            v.MaxPartDv = 50f;
            v.HitWeaken = 40f;
            v.WeakenRegain = 2.5f;
            v.WeakMuscle = 0.5f;
            v.StaggerAt = 700f;
            v.Momentum = 400f;
            v.FireRate = 5f;
            v.PropKind = RagdollWorldValues.PropHeavy;
            v.PropMass = 4f;
            v.PropSpeed = 120f;
            v.PropSize = 2f;
            b.Room.Center = new Vec2(1.5f, -2.5f);
            b.Room.Size = new Vec3(60f, 40f, 80f);
            b.Floor = new RagdollFloorValues { Mode = RagdollFloorValues.ModeFixed, Offset = 0.25f, Height = 1.5f };
            b.Pushers.Add(new RagdollPusher { Id = "a", Key = CharaA });
            b.Pushers.Add(new RagdollPusher { Id = "b", Key = CharaB, A = "cf_J_ArmLow01_L", B = "cf_J_Hand_L", Radius = 0.3f });
            b.Obstacles.Add(ItemA);
            b.Obstacles.Add(ItemB);
            return b;
        }

        // ---------------- the fixture ----------------

        public const string FixtureFile = "scene_r5_ragdoll.hs2nscene";
        public const string FixtureTitle = "Ragdoll fixture scene";
        public static readonly DateTime FixtureClock = new DateTime(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The fixture's path under a save-format fixtures root.</summary>
        public static string FixturePath(string fixturesRoot) => Path.Combine(fixturesRoot ?? "", "fixtures", "ragdoll", FixtureFile);

        /// <summary>The options the fixture was written with: fixed clock, placeholder thumbnail, no ext handlers, app 1.3.1.</summary>
        public static SaveOptions FixtureOptions() => new SaveOptions
        {
            Clock = () => FixtureClock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.3.1",
        };

        /// <summary>Three synthetic characters (no card data): the first with an advanced-pose block, a wet block and <see cref="Block"/>, the second with <see cref="Small"/>,
        /// the third with no block; two items, a constraint and <see cref="World"/>.</summary>
        public static SceneContainer FixtureScene()
        {
            var s = new SceneContainer { Title = FixtureTitle };
            s.Scene.Nodes.Add(new SceneNode(CharaA, new CharaNodeState { AdvancedPose = DocTests_AdvancedPose.Block(), Wet = DocTests_Wet.Small(), Ragdoll = Block() }) { Order = 0 });
            s.PlaceCharacter(CharaA, InfraFixtures.SmallChara(1));
            s.Scene.Nodes.Add(new SceneNode(CharaB, new CharaNodeState { Ragdoll = Small() }) { Order = 1 });
            s.PlaceCharacter(CharaB, InfraFixtures.SmallChara(0));
            s.Scene.Nodes.Add(new SceneNode(CharaC, new CharaNodeState()) { Order = 2 });
            s.PlaceCharacter(CharaC, InfraFixtures.SmallChara(0));
            s.Scene.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "test.mod#1" }) { Order = 3 });
            s.Scene.Nodes.Add(new SceneNode(ItemB, new ItemNodeState { Ref = "test.mod#2" }) { Order = 4 });
            var constraint = new ConstraintEntry { Id = Id(0xe6), Source = new ConstraintEnd(ItemA), Target = new ConstraintEnd(ItemB), Components = new List<string> { ConstraintComponents.Position } };
            s.Scene.Constraints = new ConstraintList();
            s.Scene.Constraints.Entries.Add(constraint);
            s.Scene.Ragdoll = World();
            return s;
        }

        /// <summary>Writes the fixture once (refuses an existing file); returns its size.</summary>
        public static long WriteFixture(string fixturesRoot)
        {
            string path = FixturePath(fixturesRoot);
            if (File.Exists(path)) throw new InvalidOperationException("the fixture is never regenerated: " + path + " exists");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var r = FixtureScene().Save(path, FixtureOptions());
            if (!r.Ok) throw new InvalidOperationException(r.Error.Message);
            return new FileInfo(path).Length;
        }

        static int FormatOf(byte[] file) => (int)Entry(file, "manifest.json")["formatVersion"];
        static JObject Entry(byte[] file, string name) => JObject.Parse(Text(ContainerReader.Read(file).Get(name).Data));
        static byte[] EntryBytes(byte[] file, string name) => ContainerReader.Read(file).Get(name)?.Data;
        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
        static SaveOptions Opts() => InfraFixtures.Opts();
        static string TempOf(TestRunner t) => t.TempDir ?? Path.GetTempPath();
        static string Scene(byte[] file) => Text(EntryBytes(file, "scene.json"));

        static LoadOptions LOpts(CoreMigrationRegistry reg = null)
        {
            var o = InfraFixtures.LOpts();
            if (reg != null) o.Migrations = reg;
            return o;
        }

        static CharaNodeState CharaOf(SceneContainer c, string id) => (CharaNodeState)c.Scene.Find(id).State;

        /// <summary>A registry of revision 4, as 1.3 has.</summary>
        static CoreMigrationRegistry Registry4() => new CoreMigrationRegistry(4, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2), CoreMigrationRegistry.Empty(3));

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            RF0(t);
            RF1(t);
            RF2(t);
            RF3(t);
            RF4(t);
            RF5(t);
            RF6(t);
            RF7(t);
            RF9(t);
        }

        // ---------------- RF0 ----------------

        static void RF0(TestRunner t)
        {
            var b1 = Block().ToBytes();
            var f = new FindingList();
            var back = CharaRagdoll.Parse(Text(b1), f);
            t.Check(b1.SequenceEqual(back.ToBytes()), "RF0: a block: write -> read -> write byte-stable\n    " + DocIo.FirstDiff(b1, back.ToBytes()));
            t.Check(f.Count == 0, "RF0: it reads back with no finding (" + f + ")");
            t.Check(back.Clone().ToBytes().SequenceEqual(b1), "RF0: Clone gives the same bytes");
            var c = back.Clone();
            c.Pins[0].Part = "head";
            c.Values.Muscle = 1f;
            c.Pose.Parts.Clear();
            t.Check(back.ToBytes().SequenceEqual(b1), "RF0: a Clone shares nothing");

            var w1 = World().ToBytes();
            var fw = new FindingList();
            var wb = SceneRagdollWorld.Parse(Text(w1), fw);
            t.Check(w1.SequenceEqual(wb.ToBytes()) && fw.Count == 0, "RF0: the world block: byte-stable with no finding (" + fw + ")");
            var wc = wb.Clone();
            wc.Pushers[0].Radius = 9f;
            wc.Obstacles.Clear();
            wc.Room.Center = null;
            t.Check(wb.ToBytes().SequenceEqual(w1) && wb.Clone().ToBytes().SequenceEqual(w1), "RF0: a Clone shares nothing");

            // The exact canonical text.
            var small = new CharaRagdoll { On = true };
            small.Values.Muscle = 5f;
            small.Pins.Add(new RagdollPin { Id = small.NextPinId(), Part = "hand_R", Point = new Vec3(1f, 2f, 3f), At = new Vec3(4f, 5f, 6f) });
            small.Ties.Add(new RagdollTie { A = "hand_L", B = "hand_R" });
            small.Holds.Add(new RagdollHold { Part = "forearm_L", Rot = new Quat4(0f, 0f, 0f, 1f) });
            small.Pose.Hips = new Vec3(0.5f, 1.5f, 2.5f);
            small.Pose.Parts["abdomen"] = new Quat4(0f, 0f, 0.5f, 1f);
            small.Pose.Parts["pelvis"] = new Quat4(0f, 0f, 0f, 1f);
            const string smallText =
                "{\n  \"on\": true,\n  \"limp\": false,\n  \"values\": {\n    \"muscle\": 5.0\n  },\n" +
                "  \"pins\": [\n    {\n      \"id\": \"a\",\n      \"part\": \"hand_R\",\n      \"point\": [1.0, 2.0, 3.0],\n      \"at\": [4.0, 5.0, 6.0]\n    }\n  ],\n" +
                "  \"ties\": [\n    {\n      \"a\": \"hand_L\",\n      \"b\": \"hand_R\"\n    }\n  ],\n" +
                "  \"holds\": [\n    {\n      \"part\": \"forearm_L\",\n      \"rot\": [0.0, 0.0, 0.0, 1.0]\n    }\n  ],\n" +
                "  \"pose\": {\n    \"hips\": [0.5, 1.5, 2.5],\n    \"parts\": {\n      \"pelvis\": [0.0, 0.0, 0.0, 1.0],\n      \"abdomen\": [0.0, 0.0, 0.5, 1.0]\n    }\n  }\n}";
            t.Equal(smallText, small.ToJson(), "RF0: the exact canonical text of a block");
            var sw = new SceneRagdollWorld();
            sw.Values.Gravity = 50f;
            sw.Room.Center = new Vec2(1f, 2f);
            sw.Floor.Mode = RagdollFloorValues.ModeFeet;
            sw.Pushers.Add(new RagdollPusher { Id = "a", Key = CharaA });
            sw.Obstacles.Add(ItemA);
            string worldText =
                "{\n  \"values\": {\n    \"gravity\": 50.0\n  },\n  \"room\": {\n    \"center\": [1.0, 2.0],\n    \"size\": [40.0, 30.0, 40.0]\n  },\n" +
                "  \"floor\": {\n    \"mode\": \"feet\",\n    \"offset\": 0.0,\n    \"height\": 0.0\n  },\n" +
                "  \"pushers\": [\n    {\n      \"id\": \"a\",\n      \"key\": \"" + CharaA + "\",\n      \"a\": \"cf_J_LegLow01_R\",\n      \"b\": \"cf_J_Toes01_R\",\n      \"radius\": 0.45\n    }\n  ],\n" +
                "  \"obstacles\": [\n    \"" + ItemA + "\"\n  ]\n}";
            t.Equal(worldText, sw.ToJson(), "RF0: the exact canonical text of the world block");

            // Lookups and ids.
            t.Check(back.Find("b")?.Part == "foot_L" && back.Find("nope") == null && back.HoldOf("forearm_L") != null && back.HoldOf("head") == null, "RF0: Find, HoldOf");
            t.Equal("d", back.NextPinId(), "RF0: NextPinId after a, b, c");
            back.Pins.RemoveAt(0);
            t.Equal("a", back.NextPinId(), "RF0: it takes a free name");
            var many = new CharaRagdoll();
            for (int i = 0; i < 30; i++) many.Pins.Add(new RagdollPin { Id = many.NextPinId() });
            t.Equal(30, many.Pins.Select(p => p.Id).Distinct().Count(), "RF0: thirty names are thirty");
            t.Equal("aa", many.Pins[26].Id, "RF0: after z comes aa");
            t.Equal("c", wb.NextPusherId(), "RF0: NextPusherId after a, b");
            t.Check(wb.Find("b")?.Radius == 0.3f && wb.Find("z") == null, "RF0: Find a pusher");

            // Random blocks.
            int stable = 0;
            const int seeds = 300;
            for (int seed = 0; seed < seeds; seed++)
            {
                var g = new Random(seed);
                var r = RandomBlock(g);
                var w = RandomWorld(g);
                var rb = r.ToBytes();
                var wbytes = w.ToBytes();
                var rf = new FindingList();
                var ra = CharaRagdoll.Parse(Text(rb), rf);
                var wa = SceneRagdollWorld.Parse(Text(wbytes), rf);
                if (rb.SequenceEqual(ra.ToBytes()) && wbytes.SequenceEqual(wa.ToBytes()) && rf.Count == 0 && ra.Clone().ToBytes().SequenceEqual(rb)) stable++;
                else t.Info("seed " + seed + ": " + rf);
            }
            t.Equal(seeds, stable, "RF0: random blocks are byte-stable with no finding");
        }

        // Valid values only, so no finding.
        static CharaRagdoll RandomBlock(Random g)
        {
            float F(float min, float max) => (float)(min + g.NextDouble() * (max - min));
            Vec3 V() => new Vec3(F(-50f, 50f), F(-50f, 50f), F(-50f, 50f));
            bool B() => g.Next(2) == 0;
            string Part() => RagdollParts.Keys[g.Next(RagdollParts.Keys.Count)];
            Quat4 Q() => new Quat4(F(-1f, 1f), F(-1f, 1f), F(-1f, 1f), F(0.1f, 1f));
            var b = new CharaRagdoll { On = B(), Limp = B() };
            foreach (var name in RagdollBodyValues.Names)
            {
                var r = RagdollBodyValues.RangeOf(name);
                if (B()) continue;
                b.Values.Set(name, r.HasValue ? (JToken)F(r.Value.Min, r.Value.Max) : (JToken)B());
            }
            for (int i = g.Next(RagdollLimits.MaxPins + 1); i > 0; i--)
            {
                var p = new RagdollPin { Id = b.NextPinId(), Part = Part(), Point = V() };
                if (B()) p.At = V();
                else
                {
                    p.Follow = Id(g.Next(1 << 20));
                    p.Offset = V();
                    if (B()) p.Bone = "cf_J_Hand_R";
                }
                b.Pins.Add(p);
            }
            for (int i = g.Next(RagdollLimits.MaxTies + 1); i > 0; i--) b.Ties.Add(new RagdollTie { A = Part(), B = Part() });
            foreach (var key in RagdollParts.Keys)
            {
                if (g.Next(3) == 0) b.Holds.Add(new RagdollHold { Part = key, Rot = Q() });
                if (B()) b.Pose.Parts[key] = Q();
            }
            b.Pose.Hips = V();
            return b;
        }

        static SceneRagdollWorld RandomWorld(Random g)
        {
            float F(float min, float max) => (float)(min + g.NextDouble() * (max - min));
            bool B() => g.Next(2) == 0;
            var b = new SceneRagdollWorld();
            foreach (var name in RagdollWorldValues.Names)
            {
                var r = RagdollWorldValues.RangeOf(name);
                if (B()) continue;
                b.Values.Set(name, r.HasValue ? (JToken)F(r.Value.Min, r.Value.Max) : (JToken)RagdollWorldValues.PropKinds[g.Next(3)]);
            }
            if (B()) b.Room.Center = new Vec2(F(-50f, 50f), F(-50f, 50f));
            if (B()) b.Room.Size = new Vec3(F(10f, 200f), F(10f, 100f), F(10f, 200f));
            b.Floor = new RagdollFloorValues { Mode = RagdollFloorValues.Modes[g.Next(3)], Offset = F(-5f, 5f), Height = F(-50f, 50f) };
            for (int i = g.Next(RagdollLimits.MaxPushers + 1); i > 0; i--)
                b.Pushers.Add(new RagdollPusher { Id = b.NextPusherId(), Key = Id(g.Next(1 << 20)), A = "cf_J_Foot01_L", B = "cf_J_Toes01_L", Radius = F(0f, 2f) });
            for (int i = g.Next(RagdollLimits.MaxObstacles + 1); i > 0; i--) b.Obstacles.Add(Id(g.Next(1 << 20)));
            return b;
        }

        // ---------------- RF1 ----------------

        /// <summary>The committed files of every revision without a block, with the options each was written with (null: only its entries are compared).</summary>
        static IEnumerable<(string what, string path, SaveOptions options, int revision)> FreeScenes(string root)
        {
            yield return ("the Timeline fixture", DocTests_Timeline.R4Path(root), DocTests_Timeline.R4Options(), 4);
            yield return ("the advanced-pose fixture", DocTests_AdvancedPose.PosePath(root), DocTests_AdvancedPose.PoseOptions(), 4);
            yield return ("the material-edit fixture", DocTests_MaterialEdit.MaterialsPath(root), DocTests_MaterialEdit.MaterialsOptions(), 4);
            yield return ("the constraints fixture", DocTests_Constraints.FixturePath(root), DocTests_Constraints.FixtureOptions(), 4);
            yield return ("the wet fixture", DocTests_Wet.FixturePath(root), DocTests_Wet.FixtureOptions(), 5);
            yield return ("the v0.2 scene", Path.Combine(FixtureWriter.V02Dir(root), "scene.hs2nscene"), null, 1);
            yield return ("the plugin sample's scene", Path.Combine(root ?? "", "..", "Plugins", "fixtures", "sequence_scene.hs2nscene"), null, 1);
        }

        static void RF1(TestRunner t)
        {
            int files = 0;
            foreach (var (what, path, options, revision) in FreeScenes(t.FixturesDir))
            {
                if (!File.Exists(path)) { t.Check(false, "RF1: " + what + " present at " + path); continue; }
                var file = File.ReadAllBytes(path);
                var l = SceneContainer.Load(file, LOpts());
                t.Check(l.Ok && !l.NewerFormat && !l.Findings.Has(RagdollFindings.Limit) && !l.Findings.Has(RagdollFindings.BadPart), "RF1: " + what + " loads (" + l + ")");
                if (!l.Ok) continue;
                files++;
                t.Check(l.Value.Scene.Ragdoll == null, "RF1: " + what + ": no world block");
                t.Check(l.Value.Scene.Nodes.Select(n => n.State).OfType<CharaNodeState>().All(c => c.Ragdoll != null && c.Ragdoll.IsEmpty), "RF1: " + what + ": its characters read with an empty block, never null");
                t.Check(!Scene(file).Contains("\"ragdoll\""), "RF1: " + what + ": the file has no member");
                var re = l.Value.ToBytes(options ?? new SaveOptions { Clock = () => InfraFixtures.FixedTime, Thumbnail = ThumbnailMode.Keep, Registry = new ExtRegistry(), AppVersion = (string)Entry(file, "manifest.json")["app"]["version"] });
                t.Equal(revision, FormatOf(re), "RF1: " + what + " stays revision " + revision);
                t.Equal((int)Entry(file, "manifest.json")["minReaderVersion"], (int)Entry(re, "manifest.json")["minReaderVersion"], "RF1: " + what + ": minReaderVersion as it was");
                var names = ContainerReader.Read(file).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                t.Equal(string.Join(",", names), string.Join(",", ContainerReader.Read(re).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal)), "RF1: " + what + ": the same entries");
                foreach (var name in names)
                {
                    if (name == "manifest.json") continue;
                    t.Check(Same(EntryBytes(file, name), EntryBytes(re, name)), "RF1: " + what + ": " + name + " byte-equal\n    " + DocIo.FirstDiff(EntryBytes(file, name) ?? new byte[0], EntryBytes(re, name) ?? new byte[0]));
                }
                if (options != null) t.Check(Same(file, re), "RF1: " + what + ": the whole file byte-equal");
            }
            t.Info("RF1: committed files compared here: " + files + " (the corpus of real files runs on both save layers)");
            t.Check(files >= 7, "RF1: every committed revision was compared (" + files + ")");

            // The frozen golden document, and a character, a coordinate and a pose file: not a scene, no member anywhere.
            var golden = DocTests_T1Golden.Golden("scene.json");
            var doc = DocIo.Scene(golden);
            t.Check(doc.Nodes.Select(n => n.State).OfType<CharaNodeState>().Any() && doc.Ragdoll == null && DocIo.Same(golden, DocIo.Bytes(doc)), "RF1: the golden scene is written byte for byte");
            foreach (var name in new[] { "chara.hs2nchara", "coord.hs2ncoord", "pose.hs2npose" })
            {
                string path = Path.Combine(FixtureWriter.V02Dir(t.FixturesDir ?? ""), name);
                if (!File.Exists(path)) { t.Check(false, "RF1: fixture " + path + " present"); continue; }
                foreach (var e in ContainerReader.Read(File.ReadAllBytes(path)).Entries)
                    if (e.Name.EndsWith(".json", StringComparison.Ordinal)) t.Check(!Text(e.Data).Contains("\"ragdoll\""), "RF1: " + name + ": " + e.Name + " holds no block");
            }

            // Written by this writer: a character never given a block, an explicit empty one, one with defaults written out, an empty world.
            var s = InfraFixtures.SmallScene(2, 1);
            var b0 = s.ToBytes(Opts());
            t.Check(FormatOf(b0) == 1 && !Scene(b0).Contains("\"ragdoll\""), "RF1: a scene without a block has no member, format 1");
            var c0 = (CharaNodeState)s.Scene.Nodes[0].State;
            c0.Ragdoll = new CharaRagdoll();
            t.Check(Same(b0, s.ToBytes(Opts())), "RF1: an explicit empty block changes no byte");
            s.Scene.Ragdoll = new SceneRagdollWorld();
            t.Check(Same(b0, s.ToBytes(Opts())), "RF1: nor an explicit empty world block");
            s.Scene.Ragdoll = null;

            // A hand-made empty member reads silently and is not written.
            var hand = InfraFixtures.Repack(b0, m => InfraFixtures.EditJson(m, "scene.json", j =>
            {
                ((JObject)j["nodes"][0]["chara"]).Add("ragdoll", new JObject());
                j.Add("ragdoll", new JObject());
            }), "scene.json");
            var lh = SceneContainer.Load(hand, LOpts());
            t.Check(lh.Ok && lh.Findings.Count == 0 && lh.Value.Scene.Ragdoll == null && Same(EntryBytes(b0, "scene.json"), EntryBytes(lh.Value.ToBytes(Opts()), "scene.json")), "RF1: empty members from a file read silently and are not written");
        }

        // ---------------- RF2: types ----------------

        static IEnumerable<JObject> Objects(JToken t)
        {
            if (t is JObject o)
            {
                yield return o;
                foreach (var p in o.Properties()) foreach (var x in Objects(p.Value)) yield return x;
            }
            else if (t is JArray a) foreach (var e in a) foreach (var x in Objects(e)) yield return x;
        }

        static JToken WrongFor(JToken v)
        {
            switch (v.Type)
            {
                case JTokenType.Object: return new JArray();
                case JTokenType.Array: return new JObject();
                case JTokenType.String: return 5;
                case JTokenType.Boolean: return 1;
                default: return "x";
            }
        }

        static void RF2(TestRunner t)
        {
            int mutations = 0;
            void Each(JObject whole, Action<JObject> read, string what)
            {
                var paths = Objects(whole).SelectMany(o => o.Properties()).Select(p => p.Path).ToList();
                foreach (var path in paths)
                {
                    var j = (JObject)whole.DeepClone();
                    j.SelectToken(path).Replace(JValue.CreateNull());
                    t.ThrowsCode(SaveErrorCode.BadDocument, () => read(j), "RF2: " + what + ": a null fails: " + path);
                    mutations++;
                    var k = (JObject)whole.DeepClone();
                    k.SelectToken(path).Replace(WrongFor(whole.SelectToken(path)));
                    t.ThrowsCode(SaveErrorCode.BadDocument, () => read(k), "RF2: " + what + ": a wrong type fails: " + path);
                    mutations++;
                }
            }
            Each(Block().ToJObject(), j => CharaRagdoll.FromToken(j), "a block");
            Each(World().ToJObject(), j => SceneRagdollWorld.FromToken(j), "the world block");

            void Bad(string json, string what)
            {
                t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaRagdoll.Parse(json), "RF2: fails: " + what);
                mutations++;
            }
            void BadW(string json, string what)
            {
                t.ThrowsCode(SaveErrorCode.BadDocument, () => SceneRagdollWorld.Parse(json), "RF2: the world fails: " + what);
                mutations++;
            }
            Bad("[]", "the block is an array");
            Bad("{\"pins\": [1]}", "a pin is a number");
            Bad("{\"pins\": [null]}", "a pin is null");
            Bad("{\"pins\": [{\"point\": [1, 2]}]}", "a point has two numbers");
            Bad("{\"pins\": [{\"at\": [1, 2, 3, 4]}]}", "an anchor has four numbers");
            Bad("{\"pins\": [{\"offset\": [1, \"x\", 3]}]}", "an offset's element is a string");
            Bad("{\"pins\": [{\"follow\": \"not an id\"}]}", "a follow that is no node id");
            Bad("{\"ties\": [null]}", "a tie is null");
            Bad("{\"holds\": [{\"rot\": [0, 0, 1]}]}", "a rotation has three numbers");
            Bad("{\"pose\": {\"parts\": {\"pelvis\": [0, 0, 0]}}}", "a pose rotation has three numbers");
            Bad("{\"pose\": {\"parts\": {\"pelvis\": null}}}", "a pose rotation is null");
            Bad("{\"pose\": {\"parts\": []}}", "the pose's parts are an array");
            Bad("{\"pose\": {\"hips\": [0, 0]}}", "hips have two numbers");
            Bad("{\"pins\": [{\"id\": \"a\", \"id\": \"b\"}]}", "a repeated key");
            BadW("[]", "the block is an array");
            BadW("{\"room\": {\"center\": [1, 2, 3]}}", "a centre has three numbers");
            BadW("{\"room\": {\"size\": [1, 2]}}", "a size has two numbers");
            BadW("{\"pushers\": [{\"id\": \"a\"}]}", "a pusher without a key");
            BadW("{\"pushers\": [{\"key\": \"nope\"}]}", "a pusher whose key is no node id");
            BadW("{\"pushers\": [null]}", "a pusher is null");
            BadW("{\"obstacles\": [null]}", "an obstacle is null");
            BadW("{\"obstacles\": [1]}", "an obstacle is a number");
            BadW("{\"obstacles\": [\"nope\"]}", "an obstacle that is no node id");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => CharaRagdoll.Parse("{\"values\": {\"muscle\": 1e300}}"), "RF2: a number outside float32 is NonFinite");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => SceneRagdollWorld.Parse("{\"values\": {\"gravity\": 1e300}}"), "RF2: in the world too");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => CharaRagdoll.FromToken(null), "RF2: no token fails");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => SceneRagdollWorld.FromToken(null), "RF2: no token fails, the world");

            // Lenient: a type error is a finding and the absent value.
            var lf = new FindingList();
            var ld = CharaRagdoll.Parse("{\"on\": \"x\", \"limp\": true, \"values\": {\"muscle\": \"x\", \"mass\": 60}}", lf, "t", true);
            t.Check(lf.CountOf("json.typeError") == 2 && !ld.On && ld.Limp && ld.Values.Muscle == 20f && ld.Values.Mass == 60f, "RF2: a lenient reader reports and uses the absent value (" + lf + ")");

            // In the document: the same, through a node and at the top.
            var node = DocReader.Parse(DocIo.Bytes(OneNode()), SceneDocument.EntryName);
            ((JObject)node["nodes"][0]["chara"]).Add("ragdoll", 5);
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(node, SceneDocument.EntryName)), "RF2: the member as a number fails the document");
            node["nodes"][0]["chara"]["ragdoll"] = JValue.CreateNull();
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(node, SceneDocument.EntryName)), "RF2: the member as null fails the document");
            var top = DocReader.Parse(DocIo.Bytes(OneNode()), SceneDocument.EntryName);
            top.Add("ragdoll", 5);
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(top, SceneDocument.EntryName)), "RF2: the top-level member as a number fails the document");
            top["ragdoll"] = JValue.CreateNull();
            t.ThrowsCode(SaveErrorCode.BadDocument, () => DocIo.Scene(CanonicalJsonWriter.ToBytes(top, SceneDocument.EntryName)), "RF2: the top-level member as null fails the document");

            // A writer guard: what the reader would refuse is not written.
            void Refused(Action<CharaRagdoll> spoil, string what)
            {
                var b = Block();
                spoil(b);
                t.ThrowsCode(SaveErrorCode.BadDocument, () => b.ToBytes(), "RF2: the writer refuses " + what);
                mutations++;
            }
            Refused(b => b.Pins[0].Id = null, "a null pin id");
            Refused(b => b.Pins[0].Part = null, "a null pin part");
            Refused(b => b.Pins[0].Follow = "not an id", "a follow that is no id");
            Refused(b => b.Pins.Add(null), "a null pin");
            Refused(b => b.Ties[0].A = null, "a null tie end");
            Refused(b => b.Ties.Add(null), "a null tie");
            Refused(b => b.Holds[0].Part = null, "a null hold part");
            Refused(b => b.Holds.Add(null), "a null hold");
            void RefusedW(Action<SceneRagdollWorld> spoil, string what)
            {
                var b = World();
                spoil(b);
                t.ThrowsCode(SaveErrorCode.BadDocument, () => b.ToBytes(), "RF2: the writer refuses " + what);
                mutations++;
            }
            RefusedW(b => b.Pushers[0].Id = null, "a null pusher id");
            RefusedW(b => b.Pushers[0].Key = null, "a null pusher key");
            RefusedW(b => b.Pushers[0].Key = "nope", "a pusher key that is no id");
            RefusedW(b => b.Pushers[0].A = null, "a null bone");
            RefusedW(b => b.Pushers.Add(null), "a null pusher");
            RefusedW(b => b.Obstacles.Add(null), "a null obstacle");
            RefusedW(b => b.Obstacles[0] = "nope", "an obstacle that is no id");
            RefusedW(b => b.Values.PropKind = null, "a null prop kind");
            RefusedW(b => b.Floor.Mode = null, "a null floor mode");
            var nan = Block();
            nan.Values.Muscle = float.NaN;
            t.ThrowsCode(SaveErrorCode.NonFinite, () => nan.ToBytes(), "RF2: the writer refuses a value that is not finite");
            var nanPose = Block();
            nanPose.Pose.Parts["head"] = new Quat4(float.NaN, 0f, 0f, 1f);
            t.ThrowsCode(SaveErrorCode.NonFinite, () => nanPose.ToBytes(), "RF2: and a rotation that is not finite");

            t.Info("RF2: " + mutations + " mutations of the two blocks, each caught");
            t.Check(mutations >= 100, "RF2: a list of at least 100 mutations was tried (" + mutations + ")");
        }

        static SceneDocument OneNode()
        {
            var d = new SceneDocument();
            d.Nodes.Add(new SceneNode(CharaA, new CharaNodeState()));
            return d;
        }

        // ---------------- RF3: order ----------------

        static void RF3(TestRunner t)
        {
            var j = Block().ToJObject();
            t.Equal("on,limp,values,pins,ties,holds,pose", Names(j), "RF3: block members in schema order");
            t.Equal(string.Join(",", RagdollBodyValues.Names), Names(j["values"]), "RF3: every body value, in the order of the table");
            t.Equal("muscle,muscleDamping,pin,pinFreq,pinFalloff,pinHipsOnly,gravityComp,knockDistance,knockHit,downMuscle,minDown,getUp,relocate,faceCamera,legCollision,mass", string.Join(",", RagdollBodyValues.Names), "RF3: the table's order is the schema's");
            t.Equal("id,part,point,at", Names(j["pins"][0]), "RF3: a plain pin in schema order");
            t.Equal("id,part,point,follow,offset", Names(j["pins"][1]), "RF3: a pin on a node");
            t.Equal("id,part,point,follow,bone,offset", Names(j["pins"][2]), "RF3: a pin on a bone");
            t.Equal("a,b", Names(j["ties"][0]), "RF3: a tie");
            t.Equal("part,rot", Names(j["holds"][0]), "RF3: a hold");
            t.Equal("hips,parts", Names(j["pose"]), "RF3: the pose");
            t.Equal(string.Join(",", RagdollParts.Keys), Names(j["pose"]["parts"]), "RF3: the pose's parts in part order");
            var w = World().ToJObject();
            t.Equal("values,room,floor,pushers,obstacles", Names(w), "RF3: world members in schema order");
            t.Equal(string.Join(",", RagdollWorldValues.Names), Names(w["values"]), "RF3: every world value, in the order of the table");
            t.Equal("gravity,timeScale,hz,friction,angularDrag,pushSpeed,grabStiffness,grabMax,followStiffness,followMax,blendOut,transfer,maxPartDv,hitWeaken,weakenRegain,weakMuscle,staggerAt,momentum,fireRate,propKind,propMass,propSpeed,propSize",
                string.Join(",", RagdollWorldValues.Names), "RF3: the table's order is the schema's");
            t.Equal("center,size", Names(w["room"]), "RF3: the room");
            t.Equal("mode,offset,height", Names(w["floor"]), "RF3: the floor");
            t.Equal("id,key,a,b,radius", Names(w["pushers"][0]), "RF3: a pusher");

            // A key that is no part follows the parts, in ordinal order; the order of a file does not matter.
            var q = "[0.0, 0.0, 0.0, 1.0]";
            var read = CharaRagdoll.Parse("{\"pose\": {\"parts\": {\"zeta\": " + q + ", \"head\": " + q + ", \"alpha\": " + q + ", \"pelvis\": " + q + "}}}");
            t.Equal("pelvis,head,alpha,zeta", Names(read.ToJObject()["pose"]["parts"]), "RF3: parts in part order, then any other key");

            // In the section: last, before the section's own unknown members.
            var doc = new SceneDocument();
            doc.Nodes.Add(new SceneNode(CharaA, new CharaNodeState { AdvancedPose = DocTests_AdvancedPose.Block(), Wet = DocTests_Wet.Small(), Ragdoll = Block() }));
            var sj = DocReader.Parse(DocIo.Bytes(doc), SceneDocument.EntryName);
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose,wet,ragdoll", Names(sj["nodes"][0]["chara"]), "RF3: ragdoll is the last member of the section");
            var only = new SceneDocument();
            only.Nodes.Add(new SceneNode(CharaA, new CharaNodeState { Ragdoll = Block() }));
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,ragdoll", Names(DocReader.Parse(DocIo.Bytes(only), SceneDocument.EntryName)["nodes"][0]["chara"]), "RF3: after voice without the others");
            var withUnknown = (JObject)sj.DeepClone();
            ((JObject)withUnknown["nodes"][0]["chara"]).Add("future", 1);
            var doc2 = DocIo.Scene(CanonicalJsonWriter.ToBytes(withUnknown, SceneDocument.EntryName));
            t.Equal(Block().ToJson(), ((CharaNodeState)doc2.Find(CharaA).State).Ragdoll.ToJson(), "RF3: the section reads the block back");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose,wet,ragdoll,future", Names(DocReader.Parse(DocIo.Bytes(doc2), SceneDocument.EntryName)["nodes"][0]["chara"]), "RF3: the section's unknown members follow it");

            // At the top: after constraints, before the unknown members and ext.
            var fx = FixtureScene();
            fx.Scene.Timeline = new TimelineDocument();
            fx.Scene.Ext.Set("stp3.fixture", JToken.Parse("{\"a\": 1}"));
            fx.Scene.Unknown = new JsonLeftovers();
            fx.Scene.Unknown.Add("future", 1);
            var top = DocReader.Parse(DocIo.Bytes(fx.Scene), SceneDocument.EntryName);
            t.Equal("nodes,scene,timeline,constraints,ragdoll,future,ext", Names(top), "RF3: the top level: after constraints, before what is unknown and ext");
            var doc3 = DocIo.Scene(DocIo.Bytes(fx.Scene));
            t.Check(doc3.Ragdoll != null && doc3.Ragdoll.ToJson() == World().ToJson(), "RF3: the document reads the block back");
            t.Equal("nodes,scene,timeline,constraints,ragdoll,future,ext", Names(DocReader.Parse(DocIo.Bytes(doc3), SceneDocument.EntryName)), "RF3: and writes it in place");
            var plain = new SceneDocument { Ragdoll = World() };
            t.Equal("nodes,scene,ragdoll", Names(DocReader.Parse(DocIo.Bytes(plain), SceneDocument.EntryName)), "RF3: without the others: after scene");
        }

        // ---------------- RF4: what empty means ----------------

        static void RF4(TestRunner t)
        {
            t.Check(new CharaRagdoll().IsEmpty && new SceneRagdollWorld().IsEmpty, "RF4: new blocks are empty");
            t.Check(new CharaNodeState().Ragdoll != null && new CharaNodeState().Ragdoll.IsEmpty, "RF4: a node's block is never null, empty by default");
            t.Check(new SceneDocument().Ragdoll == null, "RF4: a document without a world block holds null");
            t.Check(CharaRagdoll.Parse("{}").IsEmpty && SceneRagdollWorld.Parse("{}").IsEmpty, "RF4: {} is empty");
            t.Check(CharaRagdoll.Parse("{\"on\": false, \"limp\": false, \"values\": {}, \"pins\": [], \"ties\": [], \"holds\": [], \"pose\": {\"parts\": {}}}").IsEmpty, "RF4: every member with nothing in it is empty");
            t.Check(CharaRagdoll.Parse("{\"values\": {\"muscle\": 20, \"pinHipsOnly\": false, \"relocate\": true}}").IsEmpty, "RF4: defaults written out are empty");
            t.Check(SceneRagdollWorld.Parse("{\"values\": {\"gravity\": 98.1, \"propKind\": \"ball\"}, \"room\": {\"size\": [40, 30, 40]}, \"floor\": {\"mode\": \"map\", \"offset\": 0, \"height\": 0}, \"pushers\": [], \"obstacles\": []}").IsEmpty, "RF4: the world likewise");

            void Holds(Action<CharaRagdoll> fill, string what)
            {
                var b = new CharaRagdoll();
                fill(b);
                t.Check(!b.IsEmpty, "RF4: not empty: " + what);
            }
            Holds(b => b.On = true, "on");
            Holds(b => b.Limp = true, "limp");
            Holds(b => b.Values.Muscle = 19f, "a value");
            Holds(b => b.Values.PinHipsOnly = true, "a switch");
            Holds(b => b.Pins.Add(new RagdollPin { Id = "a" }), "a pin");
            Holds(b => b.Ties.Add(new RagdollTie()), "a tie");
            Holds(b => b.Holds.Add(new RagdollHold()), "a hold");
            Holds(b => b.Pose.Parts["pelvis"] = Quat4.Identity, "a pose");
            foreach (var (name, json) in new[] { ("the block", "{\"future\": 1}"), ("values", "{\"values\": {\"future\": 1}}"), ("the pose", "{\"pose\": {\"future\": 1}}") })
                t.Check(!CharaRagdoll.Parse(json).IsEmpty, "RF4: an unknown member in " + name + " is not empty (never lost)");

            void HoldsW(Action<SceneRagdollWorld> fill, string what)
            {
                var b = new SceneRagdollWorld();
                fill(b);
                t.Check(!b.IsEmpty, "RF4: the world is not empty: " + what);
            }
            HoldsW(b => b.Values.Hz = 120f, "a value");
            HoldsW(b => b.Values.PropKind = RagdollWorldValues.PropBox, "a prop kind");
            HoldsW(b => b.Room.Center = new Vec2(0f, 0f), "a room's centre");
            HoldsW(b => b.Room.Size = new Vec3(41f, 30f, 40f), "a width");
            HoldsW(b => b.Room.Size = new Vec3(40f, 31f, 40f), "a height");
            HoldsW(b => b.Room.Size = new Vec3(40f, 30f, 41f), "a depth");
            HoldsW(b => b.Floor.Mode = RagdollFloorValues.ModeFeet, "a floor mode");
            HoldsW(b => b.Floor.Offset = 1f, "a floor offset");
            HoldsW(b => b.Floor.Height = 1f, "a floor height");
            HoldsW(b => b.Pushers.Add(new RagdollPusher { Id = "a", Key = CharaA }), "a pusher");
            HoldsW(b => b.Obstacles.Add(ItemA), "an obstacle");
            foreach (var (name, json) in new[] { ("the block", "{\"future\": 1}"), ("values", "{\"values\": {\"future\": 1}}"), ("room", "{\"room\": {\"future\": 1}}"), ("floor", "{\"floor\": {\"future\": 1}}") })
                t.Check(!SceneRagdollWorld.Parse(json).IsEmpty, "RF4: an unknown member in the world's " + name + " is not empty");

            // A member is written exactly when it holds something.
            var on = new CharaRagdoll { On = true };
            t.Equal("on,limp", Names(on.ToJObject()), "RF4: a body alone: on and limp");
            on.Values.Mass = 60f;
            t.Equal("on,limp,values", Names(on.ToJObject()), "RF4: a value adds values");
            on.Pose.Parts["pelvis"] = Quat4.Identity;
            t.Equal("on,limp,values,pose", Names(on.ToJObject()), "RF4: a rotation adds pose");
            var off = new SceneRagdollWorld();
            off.Floor.Offset = 1f;
            t.Equal("floor", Names(off.ToJObject()), "RF4: a floor alone");
            off.Floor.Offset = 0f;
            off.Obstacles.Add(ItemA);
            t.Equal("obstacles", Names(off.ToJObject()), "RF4: an obstacle alone");
            var room = new SceneRagdollWorld();
            room.Room.Size = new Vec3(50f, 30f, 40f);
            t.Equal("size", Names(room.ToJObject()["room"]), "RF4: a room nobody placed writes no centre");
            t.Check(SceneRagdollWorld.Parse(room.ToJson()).Room.Center == null, "RF4: and reads none");

            // A release takes a character with default values back to no member; values it changed stay.
            var released = Block();
            released.Release();
            t.Check(!released.On && !released.Limp && released.Pins.Count == 0 && released.Ties.Count == 0 && released.Holds.Count == 0 && released.Pose.IsEmpty, "RF4: a release clears on, limp, pins, ties, holds, pose");
            t.Check(!released.IsEmpty && released.Values.Muscle == 35f, "RF4: it keeps changed values");
            var plain = Block();
            plain.Values = new RagdollBodyValues();
            plain.Release();
            t.Check(plain.IsEmpty, "RF4: a character with default values goes back to no member");

            // The revision.
            var s = InfraFixtures.SmallScene(2, 1);
            var c0 = (CharaNodeState)s.Scene.Nodes[0].State;
            var b1 = s.ToBytes(Opts());
            t.Check(FormatOf(b1) == 1, "RF4: no block: 1");
            int Revision(Action<CharaRagdoll> fill)
            {
                c0.Ragdoll = new CharaRagdoll();
                fill(c0.Ragdoll);
                int r = FormatOf(s.ToBytes(Opts()));
                c0.Ragdoll = new CharaRagdoll();
                return r;
            }
            t.Equal(5, Revision(b => b.On = true), "RF4: a body alone: 5");
            t.Equal(5, Revision(b => b.Limp = true), "RF4: limp alone: 5");
            t.Equal(5, Revision(b => b.Values.Mass = 60f), "RF4: settings kept with no body: 5");
            t.Equal(5, Revision(b => b.Pose.Parts["pelvis"] = Quat4.Identity), "RF4: a pose alone: 5");
            t.Equal(5, Revision(b => { var u = CharaRagdoll.Parse("{\"future\": 1}"); b.Unknown = u.Unknown; }), "RF4: an unknown member alone: 5");
            t.Equal(5, Revision(b => { var u = CharaRagdoll.Parse("{\"values\": {\"future\": 1}}"); b.Values = u.Values; }), "RF4: an unknown member in values: 5");
            t.Equal(1, Revision(b => { }), "RF4: an empty block: 1");
            int RevisionW(Action<SceneRagdollWorld> fill)
            {
                s.Scene.Ragdoll = new SceneRagdollWorld();
                fill(s.Scene.Ragdoll);
                int r = FormatOf(s.ToBytes(Opts()));
                s.Scene.Ragdoll = null;
                return r;
            }
            t.Equal(5, RevisionW(b => b.Values.Gravity = 50f), "RF4: a world value alone: 5");
            t.Equal(5, RevisionW(b => b.Obstacles.Add(ItemA)), "RF4: an obstacle alone: 5");
            t.Equal(5, RevisionW(b => { var u = SceneRagdollWorld.Parse("{\"future\": 1}"); b.Unknown = u.Unknown; }), "RF4: an unknown member alone: 5");
            t.Equal(1, RevisionW(b => { }), "RF4: an empty world block: 1");
            t.Equal(5, SaveFormats.RagdollVersion, "RF4: the revision of both blocks");
            t.Check(SaveFormats.SupportedVersion == 5 && CoreMigrationRegistry.Default.Supported == 5, "RF4: revision 5 stays the reader's own: no revision 6");

            c0.Ragdoll = Block();
            s.Scene.Ragdoll = World();
            var with = s.ToBytes(Opts());
            var man = Entry(with, "manifest.json");
            t.Check((int)man["formatVersion"] == 5 && (int)man["minReaderVersion"] == 1, "RF4: the file says revision 5, minReaderVersion 1");
            t.Check(man["requires"].Count() == Entry(b1, "manifest.json")["requires"].Count(), "RF4: nothing new in requires");
            t.Equal(ContainerReader.Read(b1).Entries.Count(), ContainerReader.Read(with).Entries.Count(), "RF4: the blocks add no entry");
            foreach (var e in ContainerReader.Read(with).Entries)
                if (e.Name != "scene.json" && e.Name.EndsWith(".json", StringComparison.Ordinal)) t.Check(!Text(e.Data).Contains("\"ragdoll\""), "RF4: " + e.Name + " does not hold a block");

            // Clearing brings the bytes back; with other optional members the highest wins.
            c0.Ragdoll = new CharaRagdoll();
            s.Scene.Ragdoll = null;
            t.Check(Same(s.ToBytes(Opts()), b1), "RF4: cleared, the file is byte-equal to one that never had a block");
            var other = (CharaNodeState)s.Scene.Nodes[1].State;
            other.AdvancedPose = DocTests_AdvancedPose.Block();
            c0.Ragdoll = Small();
            t.Check(FormatOf(s.ToBytes(Opts())) == 5, "RF4: with a revision-4 member too: 5");
            c0.Ragdoll = new CharaRagdoll();
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "RF4: the block cleared: back to 4");
            other.AdvancedPose = new CharaAdvancedPose();
            other.State.NippleThroughClothes = false;
            s.Scene.Ragdoll = World();
            t.Check(FormatOf(s.ToBytes(Opts())) == 5, "RF4: a world block with a revision-3 member: 5");
            s.Scene.Ragdoll = null;
            t.Check(FormatOf(s.ToBytes(Opts())) == 3, "RF4: cleared: back to 3");
            other.State.NippleThroughClothes = true;
            t.Check(Same(s.ToBytes(Opts()), b1), "RF4: all cleared: 1, byte-equal to the file before");
            ((CharaNodeState)s.Scene.Nodes[1].State).Ragdoll = Small();
            t.Check(FormatOf(s.ToBytes(Opts())) == 5, "RF4: any character's block raises the file");
        }

        // ---------------- RF5: limits, ranges, bad parts, unknown members ----------------

        static void RF5(TestRunner t)
        {
            Unknown(t);
            Ranges(t);
            Limits(t);
            BadParts(t);
            Kept(t);
            Copies(t);
        }

        static void Unknown(TestRunner t)
        {
            // Unknown members at every level: every object of a whole block gains one; it comes back at its place, after the known members, and nothing is reported.
            void Levels(JObject whole, Func<JObject, FindingList, string> read, string what, int expected)
            {
                int levels = 0, kept = 0;
                for (int i = 0; ; i++)
                {
                    var j = (JObject)whole.DeepClone();
                    // The pose's parts are a map: a new key there is a part key, which BadParts takes.
                    var objects = Objects(j).Where(o => !o.Path.EndsWith("pose.parts", StringComparison.Ordinal)).ToList();
                    if (i >= objects.Count) break;
                    levels++;
                    objects[i].Add("zzFuture", JObject.Parse("{\"a\": [1, 2], \"b\": null}"));
                    var f = new FindingList();
                    string back = read(j, f);
                    if (f.Count == 0 && CanonicalJsonWriter.ToText(j) == back) kept++;
                    else t.Info("unknown member at " + objects[i].Path + " " + f);
                }
                t.Equal(expected, levels, "RF5: " + what + " has the objects expected");
                t.Equal(levels, kept, "RF5: " + what + ": an unknown member is kept at every level, in place, with no finding");
            }
            Levels(Block().ToJObject(), (j, f) => CharaRagdoll.FromToken(j, f).ToJson(), "a block", 9);
            Levels(World().ToJObject(), (j, f) => SceneRagdollWorld.FromToken(j, f).ToJson(), "the world block", 6);
        }

        // Every number with a range, from the panel's table, as numbers: a wrong end in the classes fails here.
        static readonly (string name, float min, float max)[] BodyRanges =
        {
            ("muscle", 0f, 60f), ("muscleDamping", 0f, 2f), ("pin", 0f, 1f), ("pinFreq", 2f, 60f), ("pinFalloff", 0f, 20f), ("knockDistance", 0f, 20f), ("knockHit", 0f, 100f),
            ("downMuscle", 0f, 1f), ("minDown", 0f, 10f), ("getUp", 0.2f, 5f), ("mass", 5f, 150f),
        };

        static readonly (string name, float min, float max)[] WorldRanges =
        {
            ("gravity", 0f, 200f), ("timeScale", 0f, 1f), ("hz", 60f, 1000f), ("friction", 0f, 30f), ("angularDrag", 0f, 5f), ("pushSpeed", 0f, 100f), ("grabStiffness", 5f, 300f), ("grabMax", 0.5f, 20f),
            ("followStiffness", 5f, 2000f), ("followMax", 0.5f, 60f), ("blendOut", 0f, 2f), ("transfer", 0.05f, 1f), ("maxPartDv", 5f, 200f), ("hitWeaken", 20f, 2000f), ("weakenRegain", 0.1f, 5f),
            ("weakMuscle", 0f, 1f), ("staggerAt", 50f, 3000f), ("momentum", 20f, 2000f), ("fireRate", 1f, 20f), ("propMass", 0.1f, 30f), ("propSpeed", 5f, 300f), ("propSize", 0.3f, 5f),
        };

        static string Num(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        static void Ranges(TestRunner t)
        {
            void Rows<T>((string name, float min, float max)[] rows, Func<string, FindingList, T> parse, Func<T, string, object> applied, Func<T, string, object> stored, Func<T, string> json, Func<string, WetRange?> range, string what)
            {
                foreach (var (name, min, max) in rows)
                {
                    var r = range(name);
                    t.Check(r.HasValue && r.Value.Min == min && r.Value.Max == max, "RF5: the range of " + name + " (" + r + ")");
                    foreach (var at in new[] { min, max })
                    {
                        var f = new FindingList();
                        parse("{\"values\": {\"" + name + "\": " + Num(at) + "}}", f);
                        t.Equal(0, f.Count, "RF5: " + name + " at " + at + " is within its range (" + f + ")");
                    }
                    foreach (var (value, clamped) in new[] { (min - 0.5f, min), (max + 0.5f, max) })
                    {
                        var f = new FindingList();
                        var b = parse("{\"values\": {\"" + name + "\": " + Num(value) + "}}", f);
                        t.Check(f.Count == 1 && f[0].Code == RagdollFindings.Range && f[0].Severity == FindingSeverity.Info && f[0].Path.Contains(name), "RF5: " + name + " " + value + ": one info (" + f + ")");
                        t.Equal((object)value, stored(b, name), "RF5: " + name + " " + value + ": kept in the document");
                        t.Equal((object)clamped, applied(b, name), "RF5: " + name + " " + value + ": clamped when applied");
                        t.Check(json(b).Contains("\"" + name + "\""), "RF5: " + name + " " + value + ": written back");
                    }
                }
            }
            Rows(BodyRanges, (j, f) => CharaRagdoll.Parse(j, f), (b, n) => b.Values.Applied().Get(n), (b, n) => b.Values.Get(n), b => b.ToJson(), RagdollBodyValues.RangeOf, "body");
            Rows(WorldRanges, (j, f) => SceneRagdollWorld.Parse(j, f), (b, n) => b.Values.Applied().Get(n), (b, n) => b.Values.Get(n), b => b.ToJson(), RagdollWorldValues.RangeOf, "world");
            t.Equal(RagdollBodyValues.Names.Count, BodyRanges.Length + 5, "RF5: every number of a body has a row; the rest are its five switches");
            t.Equal(RagdollWorldValues.Names.Count, WorldRanges.Length + 1, "RF5: every number of the world has a row; the rest is the prop kind");
            t.Check(RagdollBodyValues.RangeOf("relocate") == null && RagdollBodyValues.RangeOf("nope") == null && RagdollWorldValues.RangeOf("propKind") == null, "RF5: a switch, an unknown name and a kind have no range");

            // Every default lies in its range, so a new block reports nothing and applies as it is.
            var defaults = new FindingList();
            CharaRagdoll.Parse(Block().ToJson(), defaults);
            SceneRagdollWorld.Parse(World().ToJson(), defaults);
            t.Equal(0, defaults.Count, "RF5: the test blocks are in range");
            t.Equal(new RagdollBodyValues().Applied().Mass, 50f, "RF5: Applied of a default");
            var ab = Block();
            ab.Values = ab.Values.Applied();
            t.Equal(Block().ToJson(), ab.ToJson(), "RF5: body values in range are applied unchanged, with their switches");
            var aw = World();
            aw.Values = aw.Values.Applied();
            t.Equal(World().ToJson(), aw.ToJson(), "RF5: world values in range are applied unchanged, with the prop kind");

            // The room's size; a pusher's radius.
            var rf = new FindingList();
            var small = SceneRagdollWorld.Parse("{\"room\": {\"size\": [5, 300, 500]}}", rf);
            t.Check(rf.Count == 1 && rf[0].Code == RagdollFindings.Range && rf[0].Path.Contains("size"), "RF5: a room out of range: one info (" + rf + ")");
            t.Check(small.Room.Size.Equals(new Vec3(5f, 300f, 500f)) && small.Room.AppliedSize.Equals(new Vec3(10f, 100f, 200f)), "RF5: kept, clamped when applied: W 10-200, H 10-100, D 10-200");
            var r0 = new FindingList();
            t.Equal(new Vec3(10f, 100f, 200f), SceneRagdollWorld.Parse("{\"room\": {\"size\": [10, 100, 200]}}", r0).Room.AppliedSize, "RF5: the ends of the room's range");
            t.Equal(0, r0.Count, "RF5: and no finding");
            var r1 = new FindingList();
            t.Equal(new Vec3(200f, 10f, 10f), SceneRagdollWorld.Parse("{\"room\": {\"size\": [200, 10, 10]}}", r1).Room.AppliedSize, "RF5: the other ends");
            t.Equal(0, r1.Count, "RF5: and no finding");
            var pf = new FindingList();
            var neg = SceneRagdollWorld.Parse("{\"pushers\": [{\"id\": \"a\", \"key\": \"" + CharaA + "\", \"radius\": -1}]}", pf);
            t.Check(pf.Count == 1 && pf[0].Code == RagdollFindings.Range && neg.Pushers[0].Radius == -1f && neg.Pushers[0].AppliedRadius == 0f, "RF5: a radius below 0: kept, applied as 0 (" + pf + ")");

            // A world height, an anchor, an offset, a floor offset have no range.
            var free = new FindingList();
            CharaRagdoll.Parse("{\"pins\": [{\"id\": \"a\", \"part\": \"head\", \"point\": [1000, 0, 0], \"at\": [0, -5000, 0], \"offset\": [9000, 0, 0]}], \"pose\": {\"hips\": [1e6, 0, 0]}}", free);
            SceneRagdollWorld.Parse("{\"room\": {\"center\": [1e6, -1e6]}, \"floor\": {\"mode\": \"fixed\", \"offset\": -300, \"height\": 5000}}", free);
            t.Equal(0, free.Count, "RF5: places and heights have no range (" + free + ")");

            // By name.
            var v = new RagdollBodyValues();
            t.Check(v.Set("muscle", 70) == null && v.Muscle == 70f, "RF5: Set takes an integer, not clamped");
            t.Check(v.Set("muscle", 1.5) == null && v.Muscle == 1.5f, "RF5: Set takes a number");
            t.Check(v.Set("relocate", false) == null && !v.Relocate, "RF5: Set takes a switch");
            t.Check(v.Set("muscle", true)?.Contains("number") == true && v.Set("muscle", "x") != null && v.Muscle == 1.5f, "RF5: a number refuses another type, the value stays");
            t.Check(v.Set("relocate", 1)?.Contains("true or false") == true, "RF5: a switch refuses a number");
            t.Check(v.Set("nope", 1)?.Contains("nope") == true && v.Set("muscle", null) != null, "RF5: an unknown name and a null are refused");
            t.Check(v.Set("muscle", new JValue(double.NaN)) != null && v.Set("muscle", new JValue(double.PositiveInfinity)) != null && v.Muscle == 1.5f, "RF5: a number that is not finite is refused");
            t.Check(v.Get("muscle") is float m && m == 1.5f && v.Get("relocate") is bool sw && !sw && v.Get("nope") == null, "RF5: Get");
            t.Check(RagdollBodyValues.DefaultOf("muscle") is float d && d == 20f && RagdollBodyValues.DefaultOf("nope") == null, "RF5: DefaultOf");
            var w = new RagdollWorldValues();
            t.Check(w.Set("propKind", "box") == null && w.PropKind == "box" && w.Set("propKind", "nope")?.Contains("ball") == true && w.Set("propKind", 1) != null && w.PropKind == "box", "RF5: a kind takes a known word only");
            t.Check(w.Get("propKind") as string == "box" && RagdollWorldValues.DefaultOf("propKind") as string == "ball", "RF5: Get and DefaultOf a kind");
        }

        static JArray Rows(int n, Func<int, JObject> row)
        {
            var a = new JArray();
            for (int i = 0; i < n; i++) a.Add(row(i));
            return a;
        }

        static JObject PinRow(int i) => new JObject { ["id"] = "p" + i, ["part"] = "hand_R" };
        static JObject TieRow(int i) => new JObject { ["a"] = "hand_L", ["b"] = "hand_R" };
        static JObject PusherRow(int i) => new JObject { ["id"] = "p" + i, ["key"] = CharaA };

        static void Limits(TestRunner t)
        {
            t.Check(RagdollLimits.MaxPins == 16 && RagdollLimits.MaxTies == 16 && RagdollLimits.MaxPushers == 32 && RagdollLimits.MaxObstacles == 64, "RF5: the limits");

            void Block_(string member, int max, Func<int, JObject> row, Func<CharaRagdoll, int> count, Func<CharaRagdoll, int> applied)
            {
                var f0 = new FindingList();
                var at = CharaRagdoll.FromToken(new JObject { [member] = Rows(max, row) }, f0);
                t.Check(f0.Count == 0 && applied(at) == max, "RF5: " + max + " " + member + " are within the limit (" + f0 + ")");
                var f1 = new FindingList();
                var over = CharaRagdoll.FromToken(new JObject { [member] = Rows(max + 3, row) }, f1);
                t.Check(f1.Count == 1 && f1[0].Code == RagdollFindings.Limit && f1[0].Severity == FindingSeverity.Warning && f1[0].Message.Contains("3 ") && f1[0].Path.Contains(member), "RF5: " + (max + 3) + " " + member + ": one warning naming the excess (" + f1 + ")");
                t.Equal(max + 3, count(over), "RF5: the excess of " + member + " is kept in the document");
                t.Equal(max + 3, over.ToJObject()[member].Count(), "RF5: and written back");
                t.Equal(max, applied(over), "RF5: the first " + max + " act");
                t.Check(at.Problem() == null, "RF5: Problem: " + max + " " + member + " pass");
                var one = CharaRagdoll.FromToken(new JObject { [member] = Rows(max + 1, row) });
                var two = CharaRagdoll.FromToken(new JObject { [member] = Rows(max + 2, row) });
                t.Check(one.Problem()?.StartsWith(RagdollFindings.Limit, StringComparison.Ordinal) == true && one.Problem(at)?.Contains(member) == true, "RF5: Problem: one more is refused, with the limit's word");
                t.Check(one.Problem(one) == null && at.Problem(one) == null, "RF5: Problem: a file's excess may stay or shrink");
                t.Check(two.Problem(one) != null, "RF5: Problem: but not grow");
            }
            Block_("pins", RagdollLimits.MaxPins, PinRow, b => b.Pins.Count, b => b.AppliedPins().Count());
            Block_("ties", RagdollLimits.MaxTies, TieRow, b => b.Ties.Count, b => b.AppliedTies().Count());

            void World_(string member, int max, Func<int, JToken> row, Func<SceneRagdollWorld, int> count, Func<SceneRagdollWorld, int> applied)
            {
                JArray Many(int n)
                {
                    var a = new JArray();
                    for (int i = 0; i < n; i++) a.Add(row(i));
                    return a;
                }
                var f0 = new FindingList();
                var at = SceneRagdollWorld.FromToken(new JObject { [member] = Many(max) }, f0);
                t.Check(f0.Count == 0 && applied(at) == max, "RF5: " + max + " " + member + " are within the limit (" + f0 + ")");
                var f1 = new FindingList();
                var over = SceneRagdollWorld.FromToken(new JObject { [member] = Many(max + 3) }, f1);
                t.Check(f1.Count == 1 && f1[0].Code == RagdollFindings.Limit && f1[0].Severity == FindingSeverity.Warning && f1[0].Message.Contains("3 ") && f1[0].Path.Contains(member), "RF5: " + (max + 3) + " " + member + ": one warning (" + f1 + ")");
                t.Equal(max + 3, count(over), "RF5: the excess of " + member + " is kept in the document");
                t.Equal(max + 3, over.ToJObject()[member].Count(), "RF5: and written back");
                t.Equal(max, applied(over), "RF5: the first " + max + " act");
                t.Check(at.Problem() == null, "RF5: Problem: " + max + " " + member + " pass");
                var one = SceneRagdollWorld.FromToken(new JObject { [member] = Many(max + 1) });
                var two = SceneRagdollWorld.FromToken(new JObject { [member] = Many(max + 2) });
                t.Check(one.Problem()?.StartsWith(RagdollFindings.Limit, StringComparison.Ordinal) == true && one.Problem(at)?.Contains(member) == true, "RF5: Problem: one more is refused");
                t.Check(one.Problem(one) == null && at.Problem(one) == null && two.Problem(one) != null, "RF5: Problem: an excess may stay or shrink, not grow");
            }
            World_("pushers", RagdollLimits.MaxPushers, PusherRow, b => b.Pushers.Count, b => b.AppliedPushers().Count());
            World_("obstacles", RagdollLimits.MaxObstacles, i => Id(0x1000 + i), b => b.Obstacles.Count, b => b.AppliedObstacles().Count());
            var twice = new SceneRagdollWorld { Obstacles = { ItemA, ItemA, ItemB } };
            t.Equal(ItemA + "," + ItemB, string.Join(",", twice.AppliedObstacles()), "RF5: an obstacle named twice acts once");
            t.Equal(3, SceneRagdollWorld.Parse(twice.ToJson()).Obstacles.Count, "RF5: and stays in the document");

            // A second hold on a part.
            var hf = new FindingList();
            var holds = CharaRagdoll.Parse("{\"holds\": [{\"part\": \"head\", \"rot\": [0, 0, 0, 1]}, {\"part\": \"chest\", \"rot\": [0, 0, 0, 1]}, {\"part\": \"head\", \"rot\": [1, 0, 0, 0]}]}", hf);
            t.Check(hf.Count == 1 && hf[0].Code == RagdollFindings.Limit && hf[0].Severity == FindingSeverity.Warning && hf[0].Message.Contains("head"), "RF5: a second hold on a part: one warning (" + hf + ")");
            t.Check(holds.Holds.Count == 3 && holds.AppliedHolds().Count() == 2 && holds.AppliedHolds().First().Rot.Equals(Quat4.Identity), "RF5: kept, the first acts");
            t.Equal(3, holds.ToJObject()["holds"].Count(), "RF5: and written back");
            var oneHold = CharaRagdoll.Parse("{\"holds\": [{\"part\": \"head\"}, {\"part\": \"chest\"}]}");
            t.Check(oneHold.Problem() == null && holds.Problem(oneHold)?.Contains("second hold on head") == true && holds.Problem(holds) == null && oneHold.Problem(holds) == null, "RF5: Problem: a second hold is refused; a file's may stay or shrink");
            var sameCount = CharaRagdoll.Parse("{\"holds\": [{\"part\": \"head\"}, {\"part\": \"head\"}]}");
            t.Check(sameCount.Problem(oneHold)?.Contains("second hold on head") == true, "RF5: Problem: also when the number of holds stays");

            // The limit of the whole scene: reported once, kept.
            string Pins(int n) => string.Join(", ", Enumerable.Range(0, n).Select(i => "{\"id\": \"p" + i + "\", \"part\": \"head\"}"));
            string Ties(int n) => string.Join(", ", Enumerable.Range(0, n).Select(i => "{\"a\": \"head\", \"b\": \"chest\"}"));
            var s = InfraFixtures.SmallScene(2, 0);
            var clean = s.ToBytes(Opts());
            byte[] With(string a, string b) => InfraFixtures.Repack(clean, m => InfraFixtures.EditJson(m, "scene.json", j =>
            {
                ((JObject)j["nodes"][0]["chara"]).Add("ragdoll", JObject.Parse(a));
                ((JObject)j["nodes"][1]["chara"]).Add("ragdoll", JObject.Parse(b));
            }), "scene.json");
            var within = SceneContainer.Load(With("{\"pins\": [" + Pins(8) + "], \"ties\": [" + Ties(8) + "]}", "{\"pins\": [" + Pins(8) + "], \"ties\": [" + Ties(8) + "]}"), LOpts());
            t.Check(within.Ok && within.Findings.Count == 0, "RF5: 16 pins and 16 ties over two characters are within the scene's limit (" + within + ")");
            var overScene = SceneContainer.Load(With("{\"pins\": [" + Pins(9) + "], \"ties\": [" + Ties(10) + "]}", "{\"pins\": [" + Pins(8) + "], \"ties\": [" + Ties(8) + "]}"), LOpts());
            t.Check(overScene.Ok && overScene.Findings.CountOf(RagdollFindings.Limit) == 2 && overScene.Findings.Count == 2, "RF5: 17 pins and 18 ties in a scene: one warning each (" + overScene.Findings + ")");
            if (overScene.Ok) t.Equal(9, CharaOf(overScene.Value, overScene.Value.Scene.Nodes[0].Id).Ragdoll.Pins.Count, "RF5: and the entries are kept");
            var overBlock = SceneContainer.Load(With("{\"pins\": [" + Pins(17) + "]}", "{}"), LOpts());
            t.Check(overBlock.Ok && overBlock.Findings.CountOf(RagdollFindings.Limit) == 1, "RF5: a block over the limit reports itself once (" + overBlock.Findings + ")");
        }

        static void BadParts(TestRunner t)
        {
            void One(string json, string where, Func<CharaRagdoll, bool> inactive, string what)
            {
                var f = new FindingList();
                var b = CharaRagdoll.Parse(json, f);
                t.Check(f.Count == 1 && f[0].Code == RagdollFindings.BadPart && f[0].Severity == FindingSeverity.Warning && f[0].Path.Contains(where), "RF5: " + what + ": one warning (" + f + ")");
                t.Check(inactive(b), "RF5: " + what + ": inactive");
                t.Equal(b.ToJson(), CharaRagdoll.Parse(b.ToJson()).ToJson(), "RF5: " + what + ": kept and written back");
                t.Check(b.ToJson().Contains("tail"), "RF5: " + what + ": the key stays in the text");
            }
            One("{\"pins\": [{\"id\": \"a\", \"part\": \"tail\"}]}", "pins[0].part", b => b.AppliedPins().Count() == 0 && b.Pins.Count == 1, "a pin on no part");
            One("{\"ties\": [{\"a\": \"tail\", \"b\": \"head\"}]}", "ties[0].a", b => b.AppliedTies().Count() == 0 && b.Ties.Count == 1, "a tie's first end");
            One("{\"ties\": [{\"a\": \"head\", \"b\": \"tail\"}]}", "ties[0].b", b => b.AppliedTies().Count() == 0 && b.Ties.Count == 1, "a tie's second end");
            One("{\"holds\": [{\"part\": \"tail\"}]}", "holds[0].part", b => b.AppliedHolds().Count() == 0 && b.Holds.Count == 1, "a hold on no part");
            One("{\"pose\": {\"parts\": {\"tail\": [0, 0, 0, 1]}}}", "pose.parts", b => b.Pose.AppliedParts().Count() == 0 && b.Pose.Parts.Count == 1 && !b.IsEmpty, "a rotation of no part");
            var f0 = new FindingList();
            var absent = CharaRagdoll.Parse("{\"pins\": [{\"id\": \"a\"}]}", f0);
            t.Check(f0.Count == 1 && f0[0].Code == RagdollFindings.BadPart && absent.Pins[0].Part == "" && absent.AppliedPins().Count() == 0, "RF5: a pin without a part: the same (" + f0 + ")");
            var same = CharaRagdoll.Parse("{\"ties\": [{\"a\": \"head\", \"b\": \"head\"}]}");
            t.Check(same.AppliedTies().Count() == 0 && same.Ties.Count == 1, "RF5: a tie of a part to itself is kept and does not act");
            var fine = Block();
            t.Check(fine.AppliedPins().Count() == 3 && fine.AppliedTies().Count() == 1 && fine.AppliedHolds().Count() == 2, "RF5: a good block acts whole");
            t.Equal(string.Join(",", RagdollParts.Keys), string.Join(",", fine.Pose.AppliedParts().Select(p => p.Key)), "RF5: the pose acts whole, in part order");

            // A rotation of no length cannot act.
            var zf = new FindingList();
            var zero = CharaRagdoll.Parse("{\"holds\": [{\"part\": \"head\", \"rot\": [0, 0, 0, 0]}], \"pose\": {\"parts\": {\"chest\": [0, 0, 0, 0], \"head\": [0, 0, 0, 2]}}}", zf);
            t.Check(zf.Count == 2 && zf.CountOf(RagdollFindings.Range) == 2 && zf[0].Severity == FindingSeverity.Info, "RF5: a rotation of no length: an info each (" + zf + ")");
            t.Check(zero.AppliedHolds().Count() == 0 && zero.Pose.AppliedParts().Select(p => p.Key).SequenceEqual(new[] { "head" }) && zero.Holds.Count == 1 && zero.Pose.Parts.Count == 2, "RF5: kept, not applied; a long one is left to the runtime");
        }

        static void Kept(TestRunner t)
        {
            // An unknown prop kind and floor mode: kept, the default acts.
            var f = new FindingList();
            var d = SceneRagdollWorld.Parse("{\"values\": {\"propKind\": \"future\"}, \"floor\": {\"mode\": \"later\"}}", f);
            t.Check(f.CountOf("json.unknownEnum") == 2 && f.Count == 2, "RF5: unknown words: one warning each (" + f + ")");
            t.Check(d.Values.PropKind == "future" && d.Values.AppliedPropKind == RagdollWorldValues.PropBall && d.Values.Applied().PropKind == RagdollWorldValues.PropBall, "RF5: an unknown prop kind is kept and acts as a ball");
            t.Check(d.Floor.Mode == "later" && d.Floor.AppliedMode == RagdollFloorValues.ModeMap, "RF5: an unknown floor mode is kept and acts as the map's");
            t.Check(SceneRagdollWorld.Parse(d.ToJson()).ToJson() == d.ToJson() && d.ToJObject()["floor"]["mode"].ToString() == "later" && d.ToJObject()["values"]["propKind"].ToString() == "future", "RF5: and they are written back");
            t.Equal(RagdollFloorValues.ModeFeet, new RagdollFloorValues { Mode = RagdollFloorValues.ModeFeet }.AppliedMode, "RF5: a known mode acts");
            t.Equal(RagdollWorldValues.PropBox, new RagdollWorldValues { PropKind = RagdollWorldValues.PropBox }.AppliedPropKind, "RF5: a known kind acts");
            t.Equal("map,feet,fixed", string.Join(",", RagdollFloorValues.Modes), "RF5: the floor modes");
            t.Equal("ball,box,heavy", string.Join(",", RagdollWorldValues.PropKinds), "RF5: the prop kinds");

            // A repeated id: the later one derived, in order; none: derived.
            var rf = new FindingList();
            var r = CharaRagdoll.Parse("{\"pins\": [{\"id\": \"a\", \"part\": \"head\"}, {\"id\": \"a\", \"part\": \"head\"}, {\"id\": \"b\", \"part\": \"head\"}, {\"id\": \"a\", \"part\": \"head\"}, {\"part\": \"head\"}]}", rf);
            t.Equal(3, rf.CountOf(RagdollFindings.Id), "RF5: each repeat and the missing id has its finding (" + rf + ")");
            t.Check(rf[0].Severity == FindingSeverity.Info && rf[0].Path.Contains("pins[1]"), "RF5: an info at the pin");
            t.Equal("a," + JsonText.DerivedId("ragdoll/pin/1") + ",b," + JsonText.DerivedId("ragdoll/pin/3") + "," + JsonText.DerivedId("ragdoll/pin/4"), string.Join(",", r.Pins.Select(p => p.Id)), "RF5: each by its index");
            var clash = CharaRagdoll.Parse("{\"pins\": [{\"id\": \"a\", \"part\": \"head\"}, {\"id\": \"" + JsonText.DerivedId("ragdoll/pin/2") + "\", \"part\": \"head\"}, {\"id\": \"a\", \"part\": \"head\"}]}");
            t.Equal(3, clash.Pins.Select(p => p.Id).Distinct().Count(), "RF5: a derived id that is taken moves on");
            t.Check(CharaRagdoll.Parse(r.ToJson()).ToJson() == r.ToJson() && r.IdProblem() == null, "RF5: stable afterwards, IdProblem passes it");
            t.Check(new CharaRagdoll { Pins = { new RagdollPin { Id = "a" }, new RagdollPin { Id = "a" } } }.IdProblem()?.Contains("twice") == true, "RF5: IdProblem: an id used twice");
            t.Check(new CharaRagdoll { Pins = { new RagdollPin() } }.IdProblem()?.Contains("without") == true, "RF5: IdProblem: no id");
            var pf = new FindingList();
            var pushers = SceneRagdollWorld.Parse("{\"pushers\": [{\"id\": \"a\", \"key\": \"" + CharaA + "\"}, {\"id\": \"a\", \"key\": \"" + CharaA + "\"}, {\"key\": \"" + CharaA + "\"}]}", pf);
            t.Equal(2, pf.CountOf(RagdollFindings.Id), "RF5: the pushers likewise");
            t.Equal("a," + JsonText.DerivedId("ragdoll/pusher/1") + "," + JsonText.DerivedId("ragdoll/pusher/2"), string.Join(",", pushers.Pushers.Select(p => p.Id)), "RF5: by their index");
            t.Check(new SceneRagdollWorld { Pushers = { new RagdollPusher { Id = "a", Key = CharaA }, new RagdollPusher { Id = "a", Key = CharaA } } }.IdProblem()?.Contains("twice") == true, "RF5: IdProblem of the world: twice");
            t.Check(new SceneRagdollWorld { Pushers = { new RagdollPusher { Key = CharaA } } }.IdProblem()?.Contains("without") == true, "RF5: IdProblem of the world: no id");
            t.Check(World().IdProblem() == null && Block().IdProblem() == null, "RF5: IdProblem passes the good blocks");

            // A pin's forms, and a node the scene does not hold: kept with no finding, the runtime says so.
            var g = new FindingList();
            var dangling = CharaRagdoll.Parse("{\"pins\": [{\"id\": \"a\", \"part\": \"head\", \"at\": [1, 2, 3], \"follow\": \"" + Id(0x999) + "\", \"bone\": \"cf_J_Head\", \"offset\": [0, 0, 0]}]}", g);
            t.Check(g.Count == 0 && dangling.Pins[0].IsFollowing && dangling.Pins[0].At.HasValue && dangling.Pins[0].Offset.HasValue, "RF5: every member of a pin read together stays, with no finding");
            t.Equal("id,part,point,at,follow,bone,offset", Names(dangling.ToJObject()["pins"][0]), "RF5: and is written back");
            var plain = CharaRagdoll.Parse("{\"pins\": [{\"id\": \"a\", \"part\": \"head\"}]}");
            t.Check(!plain.Pins[0].IsFollowing && !plain.Pins[0].At.HasValue && !plain.Pins[0].Offset.HasValue && plain.Pins[0].Bone == "" && plain.Pins[0].Follow == "" && plain.Pins[0].Point.Equals(Vec3.Zero), "RF5: a pin with nothing but a part: absent members read as none");
            t.Equal("id,part,point", Names(plain.ToJObject()["pins"][0]), "RF5: and write as none");
            t.Check(new RagdollPin { Follow = null }.IsFollowing == false, "RF5: a null follow is none");
            var read = CharaRagdoll.Parse(Block().ToJson());
            t.Check(read.Pins[0].At.Value.Equals(new Vec3(1.5f, 12.25f, -3f)) && read.Pins[2].Offset.Value.Equals(Vec3.Zero) && read.Pins[2].Bone == "cf_J_Hand_R" && read.Pins[1].Follow == ItemA, "RF5: a pin's members read back");
            var wr = SceneRagdollWorld.Parse(World().ToJson());
            t.Check(wr.Room.Center.Value.Equals(new Vec2(1.5f, -2.5f)) && wr.Room.Size.Equals(new Vec3(60f, 40f, 80f)) && wr.Pushers[1].A == "cf_J_ArmLow01_L" && wr.Pushers[1].Radius == 0.3f && wr.Obstacles.Count == 2, "RF5: the world's members read back");
        }

        static void Copies(TestRunner t)
        {
            // A copied node: no body; the rest stays.
            var c = Block();
            c.PrepareCopy();
            t.Check(!c.On && !c.Limp && c.Pins.Count == 3 && c.Ties.Count == 1 && c.Holds.Count == 2 && !c.Pose.IsEmpty && c.Values.Muscle == 35f, "RF5: a copy has no body and keeps pins, ties, holds, the pose and its values");

            // An import: follows through the map; a follow that was not copied is dropped and the pin stays where it is.
            var map = new Dictionary<string, string> { [ItemA] = Id(0x7001) };
            var r = Block();
            r.RemapFollows(map);
            t.Check(r.Pins[1].Follow == Id(0x7001) && r.Pins[1].Offset.HasValue, "RF5: a follow on a copied node is remapped");
            t.Check(!r.Pins[2].IsFollowing && r.Pins[2].Bone == "" && !r.Pins[2].Offset.HasValue && r.Pins[2].Part == "chest", "RF5: a follow on a node not copied is dropped, the pin stays");
            t.Check(r.Pins[0].At.HasValue && !r.Pins[0].IsFollowing, "RF5: a plain pin is untouched");
            var nomap = Block();
            nomap.RemapFollows(null);
            t.Check(nomap.Pins.All(p => !p.IsFollowing), "RF5: no map drops every follow");

            // The world block of an import.
            var into = new SceneRagdollWorld();
            into.Values.Gravity = 10f;
            into.Pushers.Add(new RagdollPusher { Id = "a", Key = Id(0x7002) });
            into.Obstacles.Add(Id(0x7001));
            var keep = into.ToJson();
            var m2 = new Dictionary<string, string> { [CharaB] = Id(0x7003), [ItemA] = Id(0x7001), [ItemB] = Id(0x7004) };
            var carried = SceneRagdollWorld.Carried(into, World(), m2, out int left);
            t.Equal(0, left, "RF5: Carried: nothing left out");
            t.Check(carried.Values.Gravity == 10f && carried.Room.IsDefault && carried.Floor.IsDefault, "RF5: Carried: the receiving scene's values, room and floor stay");
            t.Equal("a,b", string.Join(",", carried.Pushers.Select(p => p.Id)), "RF5: Carried: a pusher whose node was copied, under a free id");
            t.Check(carried.Pushers[1].Key == Id(0x7003) && carried.Pushers[1].A == "cf_J_ArmLow01_L" && carried.Pushers[1].Radius == 0.3f, "RF5: Carried: with the new node id, its bones and its radius");
            t.Equal(Id(0x7001) + "," + Id(0x7004), string.Join(",", carried.Obstacles), "RF5: Carried: obstacles remapped, one already there not repeated");
            t.Equal(keep, into.ToJson(), "RF5: Carried: the block before is not changed");
            t.Check(SceneRagdollWorld.Carried(null, World(), new Dictionary<string, string>(), out _) == null, "RF5: Carried: nothing copied into a scene with none: none");
            t.Check(SceneRagdollWorld.Carried(null, null, m2, out _) == null, "RF5: Carried: nothing to carry: none");
            var full = new SceneRagdollWorld();
            for (int i = 0; i < RagdollLimits.MaxPushers; i++) full.Pushers.Add(new RagdollPusher { Id = "p" + i, Key = CharaA });
            for (int i = 0; i < RagdollLimits.MaxObstacles; i++) full.Obstacles.Add(Id(0x8000 + i));
            var over = SceneRagdollWorld.Carried(full, World(), m2, out int leftOut);
            t.Check(leftOut == 3 && over.Pushers.Count == RagdollLimits.MaxPushers && over.Obstacles.Count == RagdollLimits.MaxObstacles, "RF5: Carried: what a limit has no room for stays out and is counted (" + leftOut + ")");
        }

        // ---------------- RF6, as far as this reader can show it ----------------

        static void RF6(TestRunner t)
        {
            string path = FixturePath(t.FixturesDir);
            if (!File.Exists(path)) { t.Check(false, "RF6: fixture " + path + " present"); return; }
            var fixture = File.ReadAllBytes(path);
            var now = FixtureScene().ToBytes(FixtureOptions());
            foreach (var e in ContainerReader.Read(fixture).Entries)
                t.Check(Same(e.Data, EntryBytes(now, e.Name)), "RF6: the fixture's " + e.Name + " is what this writer writes for it");
            t.Check(FormatOf(fixture) == 5 && (int)Entry(fixture, "manifest.json")["minReaderVersion"] == 1, "RF6: the fixture is revision 5, minReaderVersion 1");
            var sj = Entry(fixture, "scene.json");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,advancedPose,wet,ragdoll", Names(sj["nodes"][0]["chara"]), "RF6: the first character holds the three blocks");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice,ragdoll", Names(sj["nodes"][1]["chara"]), "RF6: the second the ragdoll block alone");
            t.Equal("anime,hands,face,look,fk,ik,correction,state,voice", Names(sj["nodes"][2]["chara"]), "RF6: the third none");
            t.Equal("nodes,scene,constraints,ragdoll", Names(sj), "RF6: and the top level the world block");

            // This reader: the values, no finding.
            var l5 = SceneContainer.Load(fixture, LOpts());
            t.Check(l5.Ok && !l5.NewerFormat && l5.Findings.Count == 0, "RF6: this reader reads the fixture with no finding (" + l5 + ")");
            if (!l5.Ok) return;
            string blockJson = Block().ToJson(), smallJson = Small().ToJson(), worldJson = World().ToJson();
            t.Equal(blockJson, CharaOf(l5.Value, CharaA).Ragdoll.ToJson(), "RF6: and the block's values");
            t.Equal(smallJson, CharaOf(l5.Value, CharaB).Ragdoll.ToJson(), "RF6: and the second block's");
            t.Check(CharaOf(l5.Value, CharaC).Ragdoll.IsEmpty, "RF6: the third character has none");
            t.Equal(worldJson, l5.Value.Scene.Ragdoll?.ToJson(), "RF6: and the world block's values");

            // An S = 4 reader: NewerFormat and the one finding.
            var l4 = SceneContainer.Load(fixture, LOpts(Registry4()));
            t.Check(l4.Ok && l4.NewerFormat && l4.Findings.Count == 1 && l4.Findings[0].Code == "manifest.newerFormat" && l4.Findings[0].Message == "format 5 is newer than 4; unknown parts are kept",
                "RF6: an S = 4 registry reads it with NewerFormat and the single finding manifest.newerFormat (" + l4 + ")");
            if (!l4.Ok) return;

            // What that reader holds: the blocks as unknown members, written back in place.
            var c = l4.Value;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            for (int i = 0; i < 2; i++)
            {
                var held = (CharaNodeState)c.Scene.Nodes[i].State;
                held.Wet = new CharaWet();
                held.Ragdoll = new CharaRagdoll();
                held.Unknown = new JsonLeftovers();
                if (i == 0) held.Unknown.Add("wet", raw["nodes"][i]["chara"]["wet"].DeepClone());
                held.Unknown.Add("ragdoll", raw["nodes"][i]["chara"]["ragdoll"].DeepClone());
            }
            c.Scene.Ragdoll = null;
            c.Scene.Unknown = new JsonLeftovers();
            c.Scene.Unknown.Add("ragdoll", raw["ragdoll"].DeepClone());
            string saved = Path.Combine(TempOf(t), "rf6_downgraded.hs2nscene");
            var sr = c.Save(saved, FixtureOptions());
            t.Check(sr.Ok && sr.Downgraded && sr.Findings.Count == 0, "RF6: saving it reports Downgraded and nothing else (" + sr.Findings + ")");
            var back = File.ReadAllBytes(saved);
            t.Equal(4, FormatOf(back), "RF6: written as its own lowest covering revision: 4, for the advanced-pose block and the constraint");
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "RF6: scene.json byte-equal\n    " + DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));

            // This reader reads that file back: the same values, silently; and writes revision 5 again.
            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && !lb.NewerFormat && lb.Findings.Count == 0, "RF6: this reader then reads it silently (" + lb + ")");
            if (!lb.Ok) return;
            t.Equal(blockJson, CharaOf(lb.Value, CharaA).Ragdoll.ToJson(), "RF6: with the same values");
            t.Equal(worldJson, lb.Value.Scene.Ragdoll?.ToJson(), "RF6: the world too");
            t.Check(CharaOf(lb.Value, CharaA).Unknown == null && lb.Value.Scene.Unknown == null, "RF6: the members are known again");
            t.Check(FormatOf(lb.Value.ToBytes(FixtureOptions())) == 5, "RF6: and writes revision 5 again");
        }

        // ---------------- RF7 ----------------

        static void RF7(TestRunner t)
        {
            var cases = new List<(string what, Action<CharaRagdoll> shape, Action<SceneRagdollWorld> world)>
            {
                ("a body at rest", b => { }, w => { }),
                ("a limp body", b => { b.Limp = true; b.On = true; }, w => { }),
                ("settings kept with no body", b => { b.On = false; b.Limp = false; b.Pins.Clear(); }, w => { }),
                ("a pose alone", b => { b.Pins.Clear(); b.Ties.Clear(); b.Holds.Clear(); b.Values = new RagdollBodyValues(); }, w => { w.Pushers.Clear(); w.Obstacles.Clear(); }),
                ("a block with unknown members", b => { b.Unknown = new JsonLeftovers(); b.Unknown.Add("future", JObject.Parse("{\"a\": 0.1}")); }, w => { w.Unknown = new JsonLeftovers(); w.Unknown.Add("later", 1); }),
            };
            int poses = 0, moved = 0;
            foreach (var (what, shape, world) in cases)
            {
                var s = InfraFixtures.SmallScene(1, 0);
                var b = Block();
                shape(b);
                ((CharaNodeState)s.Scene.Nodes[0].State).Ragdoll = b;
                var w = World();
                world(w);
                s.Scene.Ragdoll = w;
                var first = s.ToBytes(Opts());
                var l = SceneContainer.Load(first, LOpts());
                t.Check(l.Ok && l.Findings.Count == 0, "RF7: " + what + " loads with no finding (" + l + ")");
                if (!l.Ok) continue;
                var second = l.Value.ToBytes(Opts());
                t.Check(Same(first, second), "RF7: " + what + ": save -> load -> save byte-identical");
                var again = SceneContainer.Load(second, LOpts());
                t.Check(again.Ok && Same(second, again.Value.ToBytes(Opts())), "RF7: " + what + ": and once more");
                var pose = Entry(first, "scene.json")["nodes"][0]["chara"]["ragdoll"]["pose"];
                t.Check(pose != null && pose["parts"].Count() == RagdollParts.Keys.Count, "RF7: " + what + ": the pose member is there, all 16 parts");
                var got = ((CharaNodeState)l.Value.Scene.Nodes[0].State).Ragdoll.Pose;
                foreach (var kv in Settled().Parts)
                {
                    poses++;
                    t.Check(got.Parts.TryGetValue(kv.Key, out var q) && q.Equals(kv.Value), "RF7: " + what + ": " + kv.Key + " read back in bits");
                    if (!kv.Value.Equals(Quat4.Identity)) moved++;
                }
                t.Check(got.Hips.Equals(Settled().Hips), "RF7: " + what + ": the hips in bits");
            }
            t.Info("RF7: " + poses + " rotations read back, " + moved + " of them not the identity");
            t.Check(poses > 0 && moved == poses, "RF7: the rotations compared are not the identity");

            // A saved block holds no state of a moving body: no velocity, no contact, no drive state, no weakness.
            var text = Block().ToJson() + World().ToJson();
            foreach (var word in new[] { "velocity", "speed\"", "contact", "state", "weak\"", "stagger\"", "prop\"" })
                t.Check(!text.Contains(word), "RF7: no " + word + " member in either block");
        }

        // ---------------- RF9 ----------------

        static void RF9(TestRunner t)
        {
            int written = 0;
            foreach (var name in RagdollBodyValues.Names)
            {
                var b = new CharaRagdoll { On = true };
                var whole = Block().Values;
                t.Equal(null, b.Values.Set(name, JToken.FromObject(whole.Get(name))), "RF9: " + name + " set");
                t.Equal(name, Names(b.ToJObject()["values"]), "RF9: a changed " + name + " is written, alone");
                written++;
                var back = CharaRagdoll.Parse(b.ToJson());
                t.Equal(whole.Get(name), back.Values.Get(name), "RF9: and read back");
                t.Check(!back.Values.IsDefault, "RF9: and it is no default");
                t.Equal(null, b.Values.Set(name, JToken.FromObject(RagdollBodyValues.DefaultOf(name))), "RF9: " + name + " set back");
                t.Check(b.ToJObject()["values"] == null, "RF9: back at the default, " + name + " leaves the file");
            }
            foreach (var name in RagdollWorldValues.Names)
            {
                var b = new SceneRagdollWorld();
                var whole = World().Values;
                t.Equal(null, b.Values.Set(name, JToken.FromObject(whole.Get(name))), "RF9: " + name + " set");
                t.Equal(name, Names(b.ToJObject()["values"]), "RF9: a changed " + name + " is written, alone");
                written++;
                var back = SceneRagdollWorld.Parse(b.ToJson());
                t.Equal(whole.Get(name), back.Values.Get(name), "RF9: and read back");
                t.Equal(null, b.Values.Set(name, JToken.FromObject(RagdollWorldValues.DefaultOf(name))), "RF9: " + name + " set back");
                t.Check(b.IsEmpty && b.ToJObject()["values"] == null, "RF9: back at the default, " + name + " leaves the file");
            }
            t.Info("RF9: " + written + " values each written alone when changed");
            t.Equal(RagdollBodyValues.Names.Count + RagdollWorldValues.Names.Count, written, "RF9: every value was tried");
            t.Check(written >= 39, "RF9: the 16 of a body and the 23 of the world (" + written + ")");

            // A default in a file reads as the default and is not written back; a value beside it is.
            var f = new FindingList();
            var mixed = CharaRagdoll.Parse("{\"on\": true, \"values\": {\"muscle\": 20, \"pin\": 0.5, \"relocate\": true, \"gravityComp\": false}}", f);
            t.Equal(0, f.Count, "RF9: a file's defaults read silently");
            t.Equal("pin,gravityComp", Names(mixed.ToJObject()["values"]), "RF9: only what differs is written back");
            var wf = SceneRagdollWorld.Parse("{\"values\": {\"gravity\": 98.1, \"hz\": 120, \"propKind\": \"ball\"}}");
            t.Equal("hz", Names(wf.ToJObject()["values"]), "RF9: in the world too");
            var unknown = CharaRagdoll.Parse("{\"values\": {\"muscle\": 20, \"future\": 1}}");
            t.Equal("future", Names(unknown.ToJObject()["values"]), "RF9: an unknown member stays beside them");
        }
    }
}
