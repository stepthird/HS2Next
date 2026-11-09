// Scene scripts in the save layer (scene.json member `scripts`, a Timeline's `script` marker, the document of a .hs2nscript file; format revision 5). JM1 the document:
// canonical writing, member order, unknown members kept, a store file round trip; JF1 a scene without scripts keeps its bytes and its revision on the committed files of
// every revision (the corpus of real files runs on both save layers); JF2 the reader's types (every null and every wrong type of a known member
// fails), `scripts` after `ragdoll`, written only when present, `timeline.script` after `events` only when set, unknown members kept; JF3 the revision; JF4 the reader's
// repairs, each with its finding and JSON path; JF6's round trip as far as it runs here (a revision-4 registry over this reader, the members moved to the unknown members
// as 1.3 holds them; the run on 1.3's own save layer is recorded); JF7 save -> load -> save byte-identical of a scene with two scripts, one
// loaded; JF8 the limits of an edit. The fixture Tests/SaveFormat/fixtures/script/scene_r5_script.hs2nscene is what this writer writes for FixtureScene(). Synthetic ids
// and names only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Scenes.Scripts;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_Script
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string CharaA = Id(0x5a1), CharaB = Id(0x5a2), ItemP = Id(0x5a3), ScriptDuel = Id(0x5c1), ScriptWalk = Id(0x5c2);
        public static readonly string TrackPos = Id(0x5d1), TrackRot = Id(0x5d2), TrackDof = Id(0x5d3), TrackAnime = Id(0x5d4), TrackCamera = Id(0x5d5), EventA = Id(0x5e1), EventB = Id(0x5e2);

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);
        static string Names(JToken o) => string.Join(",", ((JObject)o).Properties().Select(p => p.Name));
        static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
        static string TempOf(TestRunner t) => t.TempDir ?? Path.GetTempPath();
        static JObject J(string json) => (JObject)DocReader.ParseText(json, "test");

        // ---------------- hand-made scripts ----------------

        /// <summary>
        /// A script with every member present and every value away from its absent value: a map, a female, a male and a prop role (every role member), two set-up steps
        /// (a request and a wait), a preparation step, a Timeline with a pos track on "$a", a rot track with a bone on "$a", an anime track, a camera track, a dof track
        /// focused on "$b", and two timed commands naming roles.
        /// </summary>
        public static SceneScript HandScript(string id = null, string name = "Duel")
        {
            var s = new SceneScript { Id = id ?? ScriptDuel, Name = name, Description = "Two roles and a prop", Map = new ScriptMap("test.map#3", "Hand map") };
            s.Roles.Add(new ScriptRole("a", ScriptRole.KindChara) { Sex = ScriptRole.SexFemale, Hint = new ScriptHint("Hand A", "UserData/chara/hand_a.png"), Label = "Lead" });
            s.Roles.Add(new ScriptRole("b", ScriptRole.KindChara) { Sex = ScriptRole.SexMale, Hint = new ScriptHint("Hand B") });
            s.Roles.Add(new ScriptRole("p", ScriptRole.KindItem)
            {
                Ref = "test.mod#7", Group = 10, Category = 35, Parent = new ScriptRoleParent("a", "48"), Pos = new Vec3(0.1f, -0.2f, 0.3f), Rot = new Vec3(0f, 90f, -45.5f),
                Scale = new Vec3(1f, 1.5f, 2f), Hint = new ScriptHint("Hand prop"), Label = "Prop",
            });
            s.Setup.Add(J("{\"cmd\": \"obj.transform\", \"key\": \"$a\", \"pos\": [0, 0, 0], \"rot\": [0, 180, 0], \"note\": \"start\"}"));
            s.Setup.Add(J("{\"wait\": 0.5}"));
            s.Prepare.Add(J("{\"cmd\": \"wet.attach\", \"key\": \"$a\", \"preset\": \"water\", \"from\": {\"key\": \"$b\", \"accessory\": 2}}"));
            var tl = s.Timeline;
            tl.Duration = 12f;
            tl.Loop = false;
            tl.Autoplay = true;
            var pos = new TimelineTrack(TrackPos, TimelineTypes.Pos, "$a") { Label = "walk" };
            pos.Keys.Add(TimelineKey.OfVector(0f, new Vec3(1f, 2f, 3f)));
            pos.Keys.Add(TimelineKey.OfVector(2.5f, new Vec3(4f, 5f, 6.25f), TimelineEases.Linear));
            tl.Tracks.Add(pos);
            var rot = new TimelineTrack(TrackRot, TimelineTypes.Rot, "$a") { Bone = "cf_J_Neck", Enabled = false };
            rot.Keys.Add(TimelineKey.OfVector(1f, new Vec3(10f, 20f, 30f)));
            tl.Tracks.Add(rot);
            var anime = new TimelineTrack(TrackAnime, TimelineTypes.Anime, "$b");
            anime.Keys.Add(TimelineKey.OfAnime(0f, new AnimeRef(DocDefaults.VanillaSource, 0, 3, 0)));
            tl.Tracks.Add(anime);
            var cam = new TimelineTrack(TrackCamera, TimelineTypes.Camera);
            cam.Keys.Add(TimelineKey.OfCamera(0f, new TimelineCamera(new Vec3(0f, 10f, 0f), new Vec3(10f, 180f, 0f), 30f, 23f)));
            tl.Tracks.Add(cam);
            var dof = new TimelineTrack(TrackDof, TimelineTypes.Dof);
            dof.Keys.Add(TimelineKey.OfDof(0f, new TimelineDof { Focus = TimelineFocus.OfNode("$b", "cf_J_Head", new Vec3(0f, 0.5f, 0f)), Size = 0.5f, Aperture = 0.75f, Blur = 3f }));
            dof.Keys.Add(TimelineKey.OfDof(4f, new TimelineDof { Focus = TimelineFocus.OfNode("$a"), Size = 0.25f, Aperture = 0.5f }));
            tl.Tracks.Add(dof);
            tl.Events.Add(new TimelineEvent(EventA, 1f, J("{\"cmd\": \"chara.anime\", \"key\": \"$a\", \"group\": 0, \"category\": 3, \"no\": 0}")) { Note = "turn" });
            tl.Events.Add(new TimelineEvent(EventB, 6f, J("{\"cmd\": \"fx.shoot\", \"from\": {\"key\": \"$p\"}, \"target\": {\"key\": \"$b\", \"bone\": \"cf_J_Spine02\"}, \"text\": \"$$5\"}")) { Enabled = false });
            return s;
        }

        /// <summary>A second, smaller script: one character role, a pos track and a timed command.</summary>
        public static SceneScript WalkScript()
        {
            var s = new SceneScript { Id = ScriptWalk, Name = "Walk" };
            s.Roles.Add(new ScriptRole("w", ScriptRole.KindChara));
            var pos = new TimelineTrack(Id(0x5d9), TimelineTypes.Pos, "$w");
            pos.Keys.Add(TimelineKey.OfVector(0f, new Vec3(0f, 0f, 0f)));
            pos.Keys.Add(TimelineKey.OfVector(3f, new Vec3(0f, 0f, 30f)));
            s.Timeline.Tracks.Add(pos);
            s.Timeline.Duration = 3f;
            s.Timeline.Events.Add(new TimelineEvent(Id(0x5e9), 0f, J("{\"cmd\": \"chara.anime\", \"key\": \"$w\", \"group\": 0, \"category\": 0, \"no\": 1}")));
            return s;
        }

        /// <summary>The Timeline a script loads into a scene: every whole string "$role" of the stored form replaced by the cast's node id (a test's own substitution), the
        /// marker set to the result's hash.</summary>
        public static TimelineDocument Compiled(SceneScript s)
        {
            var o = s.Timeline.ToJObject();
            Replace(o, s.Cast);
            var d = TimelineDocument.FromToken(o);
            d.Script = new TimelineScriptMarker(s.Id, d.CompiledHash());
            return d;
        }

        static void Replace(JToken t, IDictionary<string, string> cast)
        {
            if (t is JObject o) foreach (var p in o.Properties().ToList()) { if (p.Value.Type == JTokenType.String) { string r = ScriptText.RoleOf((string)p.Value); if (r != null && cast.TryGetValue(r, out var n)) p.Value = n; } else Replace(p.Value, cast); }
            else if (t is JArray a) for (int i = 0; i < a.Count; i++) { if (a[i].Type == JTokenType.String) { string r = ScriptText.RoleOf((string)a[i]); if (r != null && cast.TryGetValue(r, out var n)) a[i] = n; } else Replace(a[i], cast); }
        }

        // ---------------- the fixture ----------------

        public const string FixtureFile = "scene_r5_script.hs2nscene";
        public const string FixtureTitle = "Script fixture scene";
        public static readonly DateTime FixtureClock = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

        public static string FixturePath(string fixturesRoot) => Path.Combine(fixturesRoot ?? "", "fixtures", "script", FixtureFile);

        public static SaveOptions FixtureOptions() => new SaveOptions
        {
            Clock = () => FixtureClock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.3.1",
        };

        /// <summary>
        /// Two synthetic characters and an item; two scene scripts: <see cref="HandScript"/> cast to both characters and the item, prepared, from store row 1, and loaded
        /// (the scene's Timeline is its compile with the marker); <see cref="WalkScript"/> uncast.
        /// </summary>
        public static SceneContainer FixtureScene()
        {
            var s = new SceneContainer { Title = FixtureTitle };
            s.Scene.Nodes.Add(new SceneNode(CharaA, new CharaNodeState()) { Order = 0, Name = "Hand A" });
            s.PlaceCharacter(CharaA, InfraFixtures.SmallChara(1));
            s.Scene.Nodes.Add(new SceneNode(CharaB, new CharaNodeState()) { Order = 1, Name = "Hand B" });
            s.PlaceCharacter(CharaB, InfraFixtures.SmallChara(0));
            s.Scene.Nodes.Add(new SceneNode(ItemP, new ItemNodeState { Ref = "test.mod#7", Group = 10, Category = 35 }) { Order = 2 });
            var duel = HandScript().ToScene(1);
            duel.Cast["a"] = CharaA;
            duel.Cast["b"] = CharaB;
            duel.Cast["p"] = ItemP;
            duel.Prepared = ScriptText.PreparationMark(duel.Prepare);
            s.Scene.Scripts = new SceneScriptList(new[] { duel, WalkScript().ToScene() });
            s.Scene.Timeline = Compiled(duel);
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
        static JObject Entry(byte[] file, string name) => DocReader.Parse(ContainerReader.Read(file).Get(name).Data, name);
        static byte[] EntryBytes(byte[] file, string name) => ContainerReader.Read(file).Get(name)?.Data;
        static string SceneText(byte[] file) => Text(EntryBytes(file, "scene.json"));
        static SaveOptions Opts() => InfraFixtures.Opts();
        static LoadOptions LOpts(CoreMigrationRegistry reg = null)
        {
            var o = InfraFixtures.LOpts();
            if (reg != null) o.Migrations = reg;
            return o;
        }
        static CoreMigrationRegistry Registry4() => new CoreMigrationRegistry(4, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2), CoreMigrationRegistry.Empty(3));

        /// <summary>Reads scene.json text with this reader (findings into <paramref name="f"/>).</summary>
        static SceneDocument ReadScene(string json, FindingList f) => SceneDocument.Read(new DocReader((JObject)DocReader.ParseText(json, "scene.json"), new DocReadContext("scene.json", false, f)));

        static string WriteScene(SceneDocument d)
        {
            var b = CanonicalJsonWriter.ToDocumentBytes("scene.json", d.Write);
            return Text(b);
        }

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            JM1(t);
            JF1(t);
            JF2(t);
            JF3(t);
            JF4(t);
            JF6(t);
            JF7(t);
            JF8(t);
        }

        // ---------------- JM1 ----------------

        static readonly (string what, string members)[] DocMembers =
        {
            ("the document", "format,version,id,name,description,map,roles,setup,prepare,timeline"),
            ("the scene script", "format,version,id,name,description,map,roles,setup,prepare,timeline,cast,prepared,store"),
            ("map", "ref,name"),
            ("roles[0] (a character)", "role,kind,sex,hint,label"),
            ("roles[2] (a prop)", "role,kind,ref,group,category,parent,pos,rot,scale,hint,label"),
            ("roles[0].hint", "name,card"),
            ("roles[2].parent", "role,point"),
            ("timeline", "duration,loop,autoplay,tracks,events"),
            ("the loaded Timeline", "duration,loop,autoplay,tracks,events,script"),
            ("the marker", "id,compiled"),
        };

        const string SmallText = "{\n  \"format\": \"hs2next.script\",\n  \"version\": 1,\n  \"id\": \"000000000000000000000000000005c7\",\n  \"name\": \"Small\",\n" +
            "  \"description\": \"\",\n  \"map\": null,\n  \"roles\": [\n    {\n      \"role\": \"a\",\n      \"kind\": \"chara\",\n      \"sex\": null\n    }\n  ],\n" +
            "  \"setup\": [\n    {\n      \"wait\": 0.25\n    }\n  ],\n  \"prepare\": [],\n  \"timeline\": {\n    \"duration\": 2.0,\n    \"loop\": true,\n" +
            "    \"autoplay\": false,\n    \"tracks\": [\n      {\n        \"id\": \"000000000000000000000000000005d7\",\n        \"type\": \"pos\",\n        \"key\": \"$a\",\n" +
            "        \"bone\": null,\n        \"slot\": null,\n        \"enabled\": true,\n        \"label\": null,\n        \"keys\": [\n          {\n            \"t\": 0.0,\n" +
            "            \"ease\": \"step\",\n            \"v\": [0.1, 0.0, -1.0]\n          }\n        ]\n      }\n    ],\n    \"events\": [\n      {\n" +
            "        \"id\": \"000000000000000000000000000005e7\",\n        \"t\": 0.5,\n        \"do\": {\n          \"cmd\": \"ping\",\n          \"text\": \"$$a\"\n        },\n" +
            "        \"enabled\": true,\n        \"note\": null\n      }\n    ]\n  }\n}\n";

        static void JM1(TestRunner t)
        {
            // Every member present in a hand-made document, in schema order.
            var hand = HandScript();
            t.Check(hand.Problem() == null, "JM1: the hand-made script breaks no rule (" + hand.Problem() + ")");
            var doc = DocReader.Parse(hand.ToDocumentBytes(), "script");
            var sc = HandScript().ToScene(4);
            sc.Cast["a"] = CharaA;
            sc.Cast["b"] = CharaB;
            sc.Cast["p"] = ItemP;
            sc.Prepared = "0123456789abcdef";
            var scene = DocReader.Parse(sc.ToBytes(), "scripts");
            var loaded = Compiled(sc).ToJObject();
            var objects = new JToken[] { doc, scene, doc["map"], doc["roles"][0], doc["roles"][2], doc["roles"][0]["hint"], doc["roles"][2]["parent"], doc["timeline"], loaded, loaded["script"] };
            int members = 0;
            for (int i = 0; i < DocMembers.Length; i++)
            {
                t.Equal(DocMembers[i].members, objects[i] == null ? "(absent)" : Names(objects[i]), "JM1: the members of " + DocMembers[i].what + " in order");
                members += DocMembers[i].members.Split(',').Length;
            }
            t.Info("JM1: present: every member of the hand-made document, " + members + " member names in " + DocMembers.Length + " objects");
            t.Check(members == 58, "JM1: every member present: the schema's 58 member names (" + members + ")");
            t.Check(doc["roles"][2]["pos"].ToString(Newtonsoft.Json.Formatting.None) == "[0.1,-0.2,0.3]" && (string)doc["timeline"]["tracks"][0]["key"] == "$a"
                && (string)doc["timeline"]["tracks"][4]["keys"][0]["v"]["focus"]["node"] == "$b", "JM1: values as made (a prop's pos, a role key, a role focus)");

            // The canonical text of a small document, exactly.
            var small = new SceneScript { Id = Id(0x5c7), Name = "Small" };
            small.Roles.Add(new ScriptRole("a", ScriptRole.KindChara));
            small.Setup.Add(J("{\"wait\": 0.25}"));
            small.Timeline.Duration = 2f;
            var tr = new TimelineTrack(Id(0x5d7), TimelineTypes.Pos, "$a");
            tr.Keys.Add(TimelineKey.OfVector(0f, new Vec3(0.1f, 0f, -1f), TimelineEases.Step));
            small.Timeline.Tracks.Add(tr);
            small.Timeline.Events.Add(new TimelineEvent(Id(0x5e7), 0.5f, J("{\"cmd\": \"ping\", \"text\": \"$$a\"}")));
            t.Equal(SmallText, Text(small.ToDocumentBytes()), "JM1: the canonical text of a small document");

            // write -> read -> write is a fixed point, in both forms; the reader reads them silently.
            foreach (var (what, bytes, read) in new (string, byte[], Func<byte[], FindingList, byte[]>)[]
            {
                ("the document", hand.ToDocumentBytes(), (b, f) => SceneScript.ParseDocument(b, f).ToDocumentBytes()),
                ("the scene form", sc.ToBytes(), (b, f) => SceneScript.ParseScene(Text(b), f).ToBytes()),
            })
            {
                var f = new FindingList();
                var again = read(bytes, f);
                t.Check(Same(bytes, again) && f.Count == 0, "JM1: " + what + ": write -> read -> write byte-equal, no finding (" + f + ")\n    " + DocIo.FirstDiff(bytes, again));
            }
            t.Check(Same(sc.Clone().ToBytes(), sc.ToBytes()) && !ReferenceEquals(sc.Clone().Roles[0], sc.Roles[0]), "JM1: Clone is equal and shares nothing");
            t.Check(Same(sc.ToDocument().ToDocumentBytes(), hand.ToDocumentBytes()), "JM1: ToDocument drops the cast, the mark and the store number");

            // Unknown members kept at every level of the document and of the scene form, values of every JSON type.
            var u = DocReader.Parse(sc.ToBytes(), "scripts");
            u["later"] = J("{\"a\": [1, 2.5, \"x\"], \"b\": null}");
            ((JObject)u["map"])["later"] = true;
            ((JObject)u["roles"][0])["later"] = 3;
            ((JObject)u["roles"][0]["hint"])["later"] = "h";
            ((JObject)u["roles"][2]["parent"])["later"] = 0.125;
            ((JObject)u["timeline"])["later"] = new JArray(1, 2);
            ((JObject)u["timeline"]["tracks"][0])["later"] = "track";
            ((JObject)u["timeline"]["events"][0])["later"] = "event";
            var fu = new FindingList();
            var ru = SceneScript.ParseScene(u.ToString(), fu);
            var wu = DocReader.Parse(ru.ToBytes(), "scripts");
            int kept = 0;
            foreach (var (path, tok) in new (string, JToken)[] { ("", wu), ("map", wu["map"]), ("roles[0]", wu["roles"][0]), ("roles[0].hint", wu["roles"][0]["hint"]),
                ("roles[2].parent", wu["roles"][2]["parent"]), ("timeline", wu["timeline"]), ("timeline.tracks[0]", wu["timeline"]["tracks"][0]), ("timeline.events[0]", wu["timeline"]["events"][0]) })
            {
                var o = tok as JObject;
                bool ok = o != null && o.Properties().Last().Name == "later";
                t.Check(ok, "JM1: an unknown member at " + (path.Length == 0 ? "the top" : path) + " is kept, after the known ones");
                if (ok) kept++;
            }
            t.Check(JToken.DeepEquals(u["later"], wu["later"]) && fu.Count == 0, "JM1: kept with its value, no finding (" + fu + ")");
            t.Check(Same(ru.ToBytes(), SceneScript.ParseScene(Text(ru.ToBytes())).ToBytes()), "JM1: and stable");
            t.Info("JM1: unknown members kept at " + kept + " places");
            var ud = DocReader.Parse(hand.ToDocumentBytes(), "script");
            ud["cast"] = J("{\"a\": \"" + CharaA + "\"}");
            var rd = SceneScript.ParseDocument(Encoding.UTF8.GetBytes(ud.ToString()));
            t.Check(rd.Cast.Count == 0 && DocReader.Parse(rd.ToDocumentBytes(), "script")["cast"] != null && rd.ToScene().Cast.Count == 0 && DocReader.Parse(rd.ToScene().ToBytes(), "s")["cast"].Type == JTokenType.Object
                && ((JObject)DocReader.Parse(rd.ToScene().ToBytes(), "s")["cast"]).Count == 0, "JM1: a document's `cast` is an unknown member there, dropped when it becomes a scene script");

            // A store file round trip: the file the store writes is the document's bytes and reads back equal.
            string dir = Path.Combine(TempOf(t), "jm1_store");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var store = ScriptStore.Open(dir);
            var row = store.Add(sc);
            var fileBytes = File.ReadAllBytes(store.PathOf(row.No));
            t.Check(Same(fileBytes, hand.ToDocumentBytes()), "JM1: the store's file " + row.File + " is the script's document (" + fileBytes.Length + " bytes)");
            var back = store.Read(row.No);
            t.Check(back != null && Same(back.ToDocumentBytes(), fileBytes), "JM1: and reads back byte-equal");
        }

        // ---------------- JF1 ----------------

        static IEnumerable<(string what, string path, SaveOptions options, int revision)> FreeScenes(string root)
        {
            yield return ("the Timeline fixture", DocTests_Timeline.R4Path(root), DocTests_Timeline.R4Options(), 4);
            yield return ("the advanced-pose fixture", DocTests_AdvancedPose.PosePath(root), DocTests_AdvancedPose.PoseOptions(), 4);
            yield return ("the material-edit fixture", DocTests_MaterialEdit.MaterialsPath(root), DocTests_MaterialEdit.MaterialsOptions(), 4);
            yield return ("the constraints fixture", DocTests_Constraints.FixturePath(root), DocTests_Constraints.FixtureOptions(), 4);
            yield return ("the wet fixture", DocTests_Wet.FixturePath(root), DocTests_Wet.FixtureOptions(), 5);
            yield return ("the ragdoll fixture", DocTests_Ragdoll.FixturePath(root), DocTests_Ragdoll.FixtureOptions(), 5);
            yield return ("the bake fixture", DocTests_Bake.FixturePath(root), DocTests_Bake.FixtureOptions(), 5);
            yield return ("the v0.2 scene", Path.Combine(FixtureWriter.V02Dir(root), "scene.hs2nscene"), null, 1);
            yield return ("the plugin sample's scene", Path.Combine(root ?? "", "..", "Plugins", "fixtures", "sequence_scene.hs2nscene"), null, 1);
        }

        static void JF1(TestRunner t)
        {
            int files = 0;
            foreach (var (what, path, options, revision) in FreeScenes(t.FixturesDir))
            {
                if (!File.Exists(path)) { t.Check(false, "JF1: " + what + " present at " + path); continue; }
                var file = File.ReadAllBytes(path);
                var l = SceneContainer.Load(file, LOpts());
                t.Check(l.Ok && !l.NewerFormat && !l.Findings.Has(ScriptFindings.Limit) && !l.Findings.Has(ScriptFindings.Id), "JF1: " + what + " loads (" + l + ")");
                if (!l.Ok) continue;
                files++;
                t.Check(l.Value.Scene.Scripts == null && l.Value.Scene.Timeline?.Script == null, "JF1: " + what + ": no scripts, no marker");
                t.Check(!SceneText(file).Contains("\"scripts\"") && !SceneText(file).Contains("\"script\""), "JF1: " + what + ": the file has no member");
                var re = l.Value.ToBytes(options ?? new SaveOptions { Clock = () => InfraFixtures.FixedTime, Thumbnail = ThumbnailMode.Keep, Registry = new ExtRegistry(), AppVersion = (string)Entry(file, "manifest.json")["app"]["version"] });
                t.Equal(revision, FormatOf(re), "JF1: " + what + " stays revision " + revision);
                var names = ContainerReader.Read(file).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
                t.Equal(string.Join(",", names), string.Join(",", ContainerReader.Read(re).Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal)), "JF1: " + what + ": the same entries");
                foreach (var name in names)
                {
                    if (name == "manifest.json") continue;
                    t.Check(Same(EntryBytes(file, name), EntryBytes(re, name)), "JF1: " + what + ": " + name + " byte-equal\n    " + DocIo.FirstDiff(EntryBytes(file, name) ?? new byte[0], EntryBytes(re, name) ?? new byte[0]));
                }
                if (options != null) t.Check(Same(file, re), "JF1: " + what + ": the whole file byte-equal");
            }
            t.Info("JF1: committed files compared here: " + files + " (the corpus of real files runs on both save layers)");
            t.Check(files >= 9, "JF1: every committed revision was compared (" + files + ")");

            var golden = DocTests_T1Golden.Golden("scene.json");
            var gdoc = DocIo.Scene(golden);
            t.Check(gdoc.Scripts == null && DocIo.Same(golden, DocIo.Bytes(gdoc)), "JF1: the golden scene is written byte for byte");

            // Written by this writer: scripts cleared give the bytes before; a Timeline without a marker stays revision 4.
            var s = InfraFixtures.SmallScene(1, 1);
            var b0 = s.ToBytes(Opts());
            s.Scene.Scripts = new SceneScriptList();
            t.Check(Same(b0, s.ToBytes(Opts())) && FormatOf(b0) == 1, "JF1: an empty script list changes no byte, format 1");
            s.Scene.Scripts = new SceneScriptList(new[] { WalkScript().ToScene() });
            var b1 = s.ToBytes(Opts());
            t.Check(FormatOf(b1) == 5 && SceneText(b1).Contains("\"scripts\""), "JF1: with a script: format 5 and the member");
            s.Scene.Scripts = null;
            t.Check(Same(b0, s.ToBytes(Opts())), "JF1: the scripts removed: the bytes before");
        }

        // ---------------- JF2: types, order, presence, unknown members ----------------

        static void JF2(TestRunner t)
        {
            var fx = FixtureScene();
            fx.Scene.Ragdoll = new SceneRagdollWorld { Obstacles = { ItemP } };
            var e = new ConstraintEntry { Id = Id(0x5f1), Source = new ConstraintEnd(CharaA), Target = new ConstraintEnd(ItemP), Components = new List<string> { ConstraintComponents.Position } };
            fx.Scene.Constraints = new ConstraintList(new[] { e });
            fx.Scene.Unknown = new JsonLeftovers();
            fx.Scene.Unknown.Add("later", "top");
            var bytes = fx.ToBytes(Opts());
            var sj = Entry(bytes, "scene.json");
            t.Equal("nodes,scene,timeline,constraints,ragdoll,scripts,later", Names(sj), "JF2: scripts after ragdoll, an unknown top-level member after them");
            t.Equal("duration,loop,autoplay,tracks,events,script", Names(sj["timeline"]), "JF2: the marker after events");
            var l = SceneContainer.Load(bytes, LOpts());
            t.Check(l.Ok && l.Findings.Count == 0 && Same(bytes, l.Value.ToBytes(Opts())), "JF2: the scene reads silently and writes back byte-equal (" + l + ")");

            // Written only when present.
            var plain = FixtureScene();
            plain.Scene.Timeline.Script = null;
            t.Equal("duration,loop,autoplay,tracks,events", Names(Entry(plain.ToBytes(Opts()), "scene.json")["timeline"]), "JF2: no marker: no member");
            plain.Scene.Scripts = null;
            t.Equal("nodes,scene,timeline", Names(Entry(plain.ToBytes(Opts()), "scene.json")), "JF2: no scripts: no member");

            // Every null and every wrong type of a known member fails the load; a missing required member too.
            string json = Text(EntryBytes(FixtureScene().ToBytes(Opts()), "scene.json"));
            var muts = Mutations(DocReader.Parse(Encoding.UTF8.GetBytes(json), "scene.json"));
            int caught = 0;
            var missed = new List<string>();
            foreach (var (what, mutated) in muts)
            {
                try
                {
                    ReadScene(mutated.ToString(), new FindingList());
                    missed.Add(what);
                }
                catch (SaveFormatException) { caught++; }
            }
            foreach (var m in missed.Take(12)) t.Check(false, "JF2: the mutation " + m + " is caught");
            t.Info("JF2: present: " + muts.Count + " mutations of the scripts and the marker, " + caught + " caught");
            t.Check(muts.Count >= 100 && caught == muts.Count, "JF2: each mutation of a list of at least 100 caught (" + caught + " of " + muts.Count + ")");

            // A focus node that is neither a node id nor a role makes its key's value unfit: the Timeline's rule drops the key (timeline.badKey), in a script's Timeline
            // ("$$b") and in the scene's ("$b": the scene's Timeline takes no role).
            foreach (var (what, edit, path) in new (string, Action<JObject>, string)[]
            {
                ("a script's focus node \"$$b\"", c => c["scripts"][0]["timeline"]["tracks"][4]["keys"][0]["v"]["focus"]["node"] = "$$b", "scripts[0].timeline.tracks[4].keys[0].v"),
                ("the scene's focus node \"$b\"", c => c["timeline"]["tracks"][4]["keys"][0]["v"]["focus"]["node"] = "$b", "timeline.tracks[4].keys[0].v"),
            })
            {
                var c = DocReader.Parse(Encoding.UTF8.GetBytes(json), "scene.json");
                edit(c);
                var fk = new FindingList();
                var dk = ReadScene(c.ToString(), fk);
                var tl = path.StartsWith("scripts", StringComparison.Ordinal) ? dk.Scripts.Scripts[0].Timeline : dk.Timeline;
                t.Check(fk.Any(x => x.Code == TimelineFindings.BadKey && x.Path == "scene.json:" + path) && tl.Tracks[4].Keys.Count == 1, "JF2: " + what + ": the key dropped with timeline.badKey (" + fk + ")");
            }

            // The values a member may hold as null read and write back as null.
            var nulls = DocReader.Parse(Encoding.UTF8.GetBytes(json), "scene.json");
            var s0 = (JObject)nulls["scripts"][0];
            s0["map"] = JValue.CreateNull();
            s0["prepared"] = JValue.CreateNull();
            s0["store"] = JValue.CreateNull();
            ((JObject)s0["roles"][0])["sex"] = JValue.CreateNull();
            ((JObject)s0["roles"][0])["hint"] = JValue.CreateNull();
            ((JObject)s0["roles"][2])["ref"] = JValue.CreateNull();
            ((JObject)s0["roles"][2]["parent"])["point"] = JValue.CreateNull();
            var fn = new FindingList();
            var dn = ReadScene(nulls.ToString(), fn);
            var sn = dn.Scripts.Scripts[0];
            t.Check(sn.Map == null && sn.Prepared == null && sn.Store == null && sn.Roles[0].Sex == null && sn.Roles[0].Hint == null && sn.Roles[2].Ref == null && sn.Roles[2].Parent.Point == null,
                "JF2: map, prepared, store, sex, hint, a prop's ref and a parent's point read as null (" + fn + ")");
            var wn = DocReader.Parse(Encoding.UTF8.GetBytes(WriteScene(dn)), "scene.json")["scripts"][0];
            t.Check(wn["map"].Type == JTokenType.Null && wn["prepared"].Type == JTokenType.Null && wn["store"].Type == JTokenType.Null && wn["roles"][2]["ref"].Type == JTokenType.Null,
                "JF2: and are written as null");
        }

        static List<(string what, JObject json)> Mutations(JObject sceneJson)
        {
            var l = new List<(string, JObject)>();
            void Mut(string what, Action<JObject> edit)
            {
                var c = (JObject)sceneJson.DeepClone();
                edit(c);
                l.Add((what, c));
            }
            JToken Num() => new JValue(7);
            JToken Str() => new JValue("x");
            JToken Arr() => new JArray(1);
            JToken Obj() => new JObject();
            JToken Nul() => JValue.CreateNull();
            for (int i = 0; i < 2; i++)
            {
                string at = "scripts[" + i + "]";
                JObject S(JObject c) => (JObject)c["scripts"][i];
                foreach (var m in new[] { "format", "version", "id", "name", "description", "roles", "setup", "prepare", "timeline", "cast" })
                {
                    string mm = m;
                    Mut(at + "." + mm + " null", c => S(c)[mm] = Nul());
                    Mut(at + "." + mm + " wrong type", c => S(c)[mm] = mm == "version" ? Str() : mm == "roles" || mm == "setup" || mm == "prepare" ? Obj() : mm == "timeline" || mm == "cast" ? Arr() : Num());
                }
                foreach (var m in new[] { "format", "version", "timeline" }) { string mm = m; Mut(at + "." + mm + " missing", c => S(c).Remove(mm)); }
                Mut(at + ".map wrong type", c => S(c)["map"] = Arr());
                Mut(at + ".prepared wrong type", c => S(c)["prepared"] = Num());
                Mut(at + ".prepared not 16 hex", c => S(c)["prepared"] = "0123456789ABCDEF");
                Mut(at + ".store wrong type", c => S(c)["store"] = Str());
                Mut(at + ".id not an id", c => S(c)["id"] = "abc");
                Mut(at + ".setup element null", c => ((JArray)S(c)["setup"]).Add(Nul()));
                Mut(at + ".setup element a number", c => ((JArray)S(c)["setup"]).Add(Num()));
                Mut(at + ".prepare element a string", c => ((JArray)S(c)["prepare"]).Add(Str()));
                Mut(at + ".setup wait a string", c => ((JArray)S(c)["setup"]).Add(J("{\"wait\": \"1\"}")));
                Mut(at + ".roles element a string", c => ((JArray)S(c)["roles"]).Add(Str()));
                Mut(at + ".cast entry null", c => ((JObject)S(c)["cast"])["zz"] = Nul());
                Mut(at + ".cast entry a number", c => ((JObject)S(c)["cast"])["zz"] = Num());
                Mut(at + ".cast entry not an id", c => ((JObject)S(c)["cast"])["zz"] = "node");
                Mut(at + ".timeline.duration missing", c => ((JObject)S(c)["timeline"]).Remove("duration"));
                Mut(at + ".timeline.tracks[0].key not an id or a role", c => S(c)["timeline"]["tracks"][0]["key"] = "$5");
                Mut(at + ".timeline.tracks[0].key a number", c => S(c)["timeline"]["tracks"][0]["key"] = Num());
                Mut(at + ".timeline.events[0].do a string", c => S(c)["timeline"]["events"][0]["do"] = Str());
            }
            // Roles: every member of the three roles of the first script.
            for (int r = 0; r < 3; r++)
            {
                string at = "scripts[0].roles[" + r + "]";
                int rr = r;
                JObject R(JObject c) => (JObject)c["scripts"][0]["roles"][rr];
                Mut(at + ".role null", c => R(c)["role"] = Nul());
                Mut(at + ".role a number", c => R(c)["role"] = Num());
                Mut(at + ".role not a role name", c => R(c)["role"] = "a b");
                Mut(at + ".role too long", c => R(c)["role"] = new string('r', 33));
                Mut(at + ".role missing", c => R(c).Remove("role"));
                Mut(at + ".kind null", c => R(c)["kind"] = Nul());
                Mut(at + ".kind a number", c => R(c)["kind"] = Num());
                Mut(at + ".kind missing", c => R(c).Remove("kind"));
                Mut(at + ".sex a number", c => R(c)["sex"] = Num());
                Mut(at + ".ref a number", c => R(c)["ref"] = Num());
                Mut(at + ".ref not a ref", c => R(c)["ref"] = "no-hash");
                Mut(at + ".group a string", c => R(c)["group"] = Str());
                Mut(at + ".category an object", c => R(c)["category"] = Obj());
                Mut(at + ".parent a string", c => R(c)["parent"] = Str());
                Mut(at + ".pos null", c => R(c)["pos"] = Nul());
                Mut(at + ".rot two numbers", c => R(c)["rot"] = new JArray(1, 2));
                Mut(at + ".scale a string", c => R(c)["scale"] = Str());
                Mut(at + ".hint an array", c => R(c)["hint"] = Arr());
                Mut(at + ".label a number", c => R(c)["label"] = Num());
            }
            Mut("scripts[0].roles[2].parent.role null", c => c["scripts"][0]["roles"][2]["parent"]["role"] = Nul());
            Mut("scripts[0].roles[2].parent.role missing", c => ((JObject)c["scripts"][0]["roles"][2]["parent"]).Remove("role"));
            Mut("scripts[0].roles[2].parent.point a number", c => c["scripts"][0]["roles"][2]["parent"]["point"] = Num());
            Mut("scripts[0].roles[0].hint.name a number", c => c["scripts"][0]["roles"][0]["hint"]["name"] = Num());
            Mut("scripts[0].roles[0].hint.card an array", c => c["scripts"][0]["roles"][0]["hint"]["card"] = Arr());
            Mut("scripts[0].map.ref a number", c => c["scripts"][0]["map"]["ref"] = Num());
            Mut("scripts[0].map.ref not a ref", c => c["scripts"][0]["map"]["ref"] = "map");
            Mut("scripts[0].map.name a number", c => c["scripts"][0]["map"]["name"] = Num());
            // The marker.
            Mut("timeline.script null", c => c["timeline"]["script"] = Num());
            Mut("timeline.script an array", c => c["timeline"]["script"] = Arr());
            Mut("timeline.script.id null", c => c["timeline"]["script"]["id"] = Nul());
            Mut("timeline.script.id not an id", c => c["timeline"]["script"]["id"] = "s");
            Mut("timeline.script.id missing", c => ((JObject)c["timeline"]["script"]).Remove("id"));
            Mut("timeline.script.compiled null", c => c["timeline"]["script"]["compiled"] = Nul());
            Mut("timeline.script.compiled a number", c => c["timeline"]["script"]["compiled"] = Num());
            Mut("timeline.script.compiled 15 hex", c => c["timeline"]["script"]["compiled"] = "0123456789abcde");
            Mut("timeline.script.compiled missing", c => ((JObject)c["timeline"]["script"]).Remove("compiled"));
            Mut("a role key in the scene's Timeline", c => c["timeline"]["tracks"][0]["key"] = "$a");
            Mut("scripts an object", c => c["scripts"] = Obj());
            Mut("scripts element a number", c => ((JArray)c["scripts"]).Add(Num()));
            return l;
        }

        // ---------------- JF3: the revision ----------------

        static void JF3(TestRunner t)
        {
            t.Equal(5, SaveFormats.ScriptVersion, "JF3: SaveFormats.ScriptVersion is 5 (revision 5 widened)");
            t.Equal(5, SaveFormats.SupportedVersion, "JF3: SupportedVersion stays 5");
            var fx = FixtureScene();
            t.Equal(5, FormatOf(fx.ToBytes(Opts())), "JF3: a scene with scripts and a loaded Timeline: revision 5");
            fx.Scene.Timeline.Script = null;
            t.Equal(5, FormatOf(fx.ToBytes(Opts())), "JF3: with scripts and a plain Timeline: revision 5");
            fx.Scene.Timeline = null;
            t.Equal(5, FormatOf(fx.ToBytes(Opts())), "JF3: with scripts alone: revision 5");
            var s = FixtureScene();
            s.Scene.Scripts = null;
            t.Equal(5, FormatOf(s.ToBytes(Opts())), "JF3: a marker without scripts (a hand-made state): revision 5, so no older reader reads it as current");
            s.Scene.Timeline.Script = null;
            t.Equal(4, FormatOf(s.ToBytes(Opts())), "JF3: without scripts and marker: its lowest covering revision, 4 for the Timeline");
            s.Scene.Timeline = null;
            t.Equal(1, FormatOf(s.ToBytes(Opts())), "JF3: and 1 with nothing later");
            t.Info("JF3: present: both cases, a scene with scripts (5) and scenes without (4, 1)");

            // requires: every ref of the core document joins it (save format 4.2, the lead's ruling): a script's animation source, a prop role's ref and the map's ref.
            var rq = InfraFixtures.SmallScene(0, 0);
            var sr = WalkScript().ToScene();
            var an = new TimelineTrack(Id(0x5da), TimelineTypes.Anime, "$w");
            an.Keys.Add(TimelineKey.OfAnime(0f, new AnimeRef("test.anime.pack", 1, 2, 3)));
            sr.Timeline.Tracks.Add(an);
            sr.Roles.Add(new ScriptRole("q", ScriptRole.KindItem) { Ref = "test.prop.pack#4" });
            sr.Map = new ScriptMap("test.map.pack#1", "M");
            rq.Scene.Scripts = new SceneScriptList(new[] { sr });
            var req = Entry(rq.ToBytes(Opts()), "manifest.json")["requires"] as JArray;
            var guids = req == null ? new List<string>() : req.Select(x => (string)x["guid"]).ToList();
            t.Check(guids.Contains("test.anime.pack") && guids.Contains("test.prop.pack") && guids.Contains("test.map.pack"),
                "JF3: requires holds a script's animation source, a prop role's ref and the map's ref (" + string.Join(",", guids) + ")");
        }

        // ---------------- JF4: the reader's repairs ----------------

        static (SceneDocument doc, FindingList f, string written) Repair(Action<JObject> edit)
        {
            var c = Entry(FixtureScene().ToBytes(Opts()), "scene.json");
            edit(c);
            var f = new FindingList();
            var d = ReadScene(c.ToString(), f);
            return (d, f, WriteScene(d));
        }

        static void Found(TestRunner t, FindingList f, string code, FindingSeverity sev, string path, string what)
        {
            var hit = f.FirstOrDefault(x => x.Code == code && x.Path == "scene.json:" + path);
            t.Check(hit != null && hit.Severity == sev, "JF4: " + what + ": " + sev.ToString().ToLowerInvariant() + " " + code + " at " + path + (hit == null ? " (got " + f + ")" : " (\"" + hit.Message + "\")"));
        }

        static void JF4(TestRunner t)
        {
            int files = 0;
            // 1. A "$x" naming no role: kept, warning.
            var r1 = Repair(c => { c["scripts"][0]["setup"][0]["key"] = "$zz"; c["scripts"][0]["timeline"]["tracks"][0]["key"] = "$yy"; });
            Found(t, r1.f, ScriptFindings.UnknownRole, FindingSeverity.Warning, "scripts[0].setup[0].key", "an undeclared role in a step");
            Found(t, r1.f, ScriptFindings.UnknownRole, FindingSeverity.Warning, "scripts[0].timeline.tracks[0].key", "an undeclared role as a track key");
            t.Check(r1.written.Contains("\"$zz\"") && r1.written.Contains("\"$yy\""), "JF4: both kept as written");
            t.Check(r1.doc.Scripts.Scripts[0].UnknownRoleRefs().Count == 2, "JF4: UnknownRoleRefs lists both");
            files++;
            // 2. A repeated role name: the later dropped.
            var r2 = Repair(c => ((JArray)c["scripts"][0]["roles"]).Insert(1, J("{\"role\": \"a\", \"kind\": \"item\", \"ref\": \"test.mod#9\"}")));
            Found(t, r2.f, ScriptFindings.DuplicateRole, FindingSeverity.Warning, "scripts[0].roles[1].role", "a repeated role name");
            t.Check(r2.doc.Scripts.Scripts[0].Roles.Count == 3 && r2.doc.Scripts.Scripts[0].Roles[0].Kind == "chara" && !r2.written.Contains("test.mod#9"), "JF4: the later role dropped, the first kept");
            files++;
            // 3. A cast entry naming no node: kept, info.
            string ghost = Id(0x5ff);
            var r3 = Repair(c => c["scripts"][0]["cast"]["b"] = ghost);
            Found(t, r3.f, ScriptFindings.CastMissing, FindingSeverity.Info, "scripts[0].cast.b", "a cast entry naming no node");
            t.Check(r3.doc.Scripts.Scripts[0].Cast["b"] == ghost && r3.written.Contains(ghost), "JF4: the entry kept");
            files++;
            // 4. Refused steps and events: kept, never sent.
            var r4 = Repair(c =>
            {
                var su = (JArray)c["scripts"][0]["setup"];
                su.Add(J("{\"cmd\": \"script.play\", \"script\": \"x\"}"));
                su.Add(J("{\"cmd\": \"timeline.build\", \"tracks\": []}"));
                su.Add(J("{\"cmd\": \"ping\", \"id\": \"7\"}"));
                su.Add(J("{\"key\": \"$a\"}"));
                ((JArray)c["scripts"][0]["prepare"]).Add(J("{\"cmd\": \"timeline.key.add\"}"));
                c["scripts"][0]["timeline"]["events"][0]["do"]["cmd"] = "script.stop";
            });
            Found(t, r4.f, ScriptFindings.BadStep, FindingSeverity.Warning, "scripts[0].setup[2]", "a step of the group script");
            Found(t, r4.f, ScriptFindings.BadStep, FindingSeverity.Warning, "scripts[0].setup[3]", "a step that edits the Timeline");
            Found(t, r4.f, ScriptFindings.BadStep, FindingSeverity.Warning, "scripts[0].setup[4]", "a step with an id member");
            Found(t, r4.f, ScriptFindings.BadStep, FindingSeverity.Warning, "scripts[0].setup[5]", "a step with neither cmd nor wait");
            Found(t, r4.f, ScriptFindings.BadStep, FindingSeverity.Warning, "scripts[0].prepare[1]", "a preparation step that edits keys");
            Found(t, r4.f, ScriptFindings.BadStep, FindingSeverity.Warning, "scripts[0].timeline.events[0].do", "a timed command of the group script");
            t.Check(r4.doc.Scripts.Scripts[0].Setup.Count == 6 && r4.written.Contains("script.play") && r4.written.Contains("script.stop"), "JF4: all kept");
            files++;
            // 5. A wait out of range: clamped.
            var r5 = Repair(c => { c["scripts"][0]["setup"][1]["wait"] = 0; ((JArray)c["scripts"][0]["setup"]).Add(J("{\"wait\": 99.5}")); });
            Found(t, r5.f, ScriptFindings.Limit, FindingSeverity.Warning, "scripts[0].setup[1].wait", "a wait of 0");
            Found(t, r5.f, ScriptFindings.Limit, FindingSeverity.Warning, "scripts[0].setup[2].wait", "a wait of 99.5 s");
            t.Check(ScriptSteps.WaitOf(r5.doc.Scripts.Scripts[0].Setup[1]) == ScriptLimits.MinWait && ScriptSteps.WaitOf(r5.doc.Scripts.Scripts[0].Setup[2]) == ScriptLimits.MaxWait,
                "JF4: clamped to " + ScriptLimits.MinWait + " and " + ScriptLimits.MaxWait + " s");
            files++;
            // 6. Over a limit: the excess dropped from the end.
            var r6 = Repair(c =>
            {
                var roles = (JArray)c["scripts"][1]["roles"];
                for (int i = 0; i < 33; i++) roles.Add(J("{\"role\": \"r" + i + "\", \"kind\": \"chara\"}"));
                var su = (JArray)c["scripts"][1]["setup"];
                for (int i = 0; i < 200; i++) su.Add(J("{\"cmd\": \"ping\", \"n\": " + i + "}"));
                var pr = (JArray)c["scripts"][1]["prepare"];
                for (int i = 0; i < 57; i++) pr.Add(J("{\"cmd\": \"ping\", \"p\": " + i + "}"));
                c["scripts"][1]["name"] = new string('n', 129);
                c["scripts"][1]["description"] = new string('d', 2049);
            });
            var w6 = r6.doc.Scripts.Scripts[1];
            Found(t, r6.f, ScriptFindings.Limit, FindingSeverity.Warning, "scripts[1].roles", "34 roles");
            Found(t, r6.f, ScriptFindings.Limit, FindingSeverity.Warning, "scripts[1].prepare", "257 steps");
            Found(t, r6.f, ScriptFindings.Limit, FindingSeverity.Warning, "scripts[1].name", "a name of 129 characters");
            Found(t, r6.f, ScriptFindings.Limit, FindingSeverity.Warning, "scripts[1].description", "a description of 2,049 characters");
            t.Check(w6.Roles.Count == 32 && w6.Roles[31].Role == "r30" && w6.Setup.Count == 200 && w6.Prepare.Count == 56 && (int)w6.Prepare[55]["p"] == 55 && w6.Name.Length == 128 && w6.Description.Length == 2048,
                "JF4: 32 roles, 256 steps (the preparation's last one dropped), 128 and 2,048 characters kept");
            files++;
            var r6b = Repair(c =>
            {
                var a = (JArray)c["scripts"];
                for (int i = 0; i < 64; i++) { var x = (JObject)a[1].DeepClone(); x["id"] = Id(0x6000 + i); a.Add(x); }
            });
            Found(t, r6b.f, ScriptFindings.Limit, FindingSeverity.Warning, "scripts", "66 scripts");
            t.Check(r6b.doc.Scripts.Count == 64 && r6b.doc.Scripts.Scripts[63].Id == Id(0x6000 + 61), "JF4: 64 scripts kept, the last two dropped");
            files++;
            // 7. Two scene scripts with one id: the later gets a derived id.
            var r7 = Repair(c => c["scripts"][1]["id"] = ScriptDuel);
            Found(t, r7.f, ScriptFindings.DuplicateId, FindingSeverity.Info, "scripts[1].id", "a repeated script id");
            t.Equal(JsonText.DerivedId(ScriptDuel + "/dup/1"), r7.doc.Scripts.Scripts[1].Id, "JF4: the derived id JsonText.DerivedId(\"<id>/dup/1\")");
            files++;
            // 8. A script without an id.
            var r8 = Repair(c => ((JObject)c["scripts"][1]).Remove("id"));
            Found(t, r8.f, ScriptFindings.Id, FindingSeverity.Info, "scripts[1]", "a script without an id");
            t.Equal(JsonText.DerivedId("scripts/1"), r8.doc.Scripts.Scripts[1].Id, "JF4: the given id");
            files++;
            // 9. Another format and a newer version in a scene: kept as read, never played.
            var r9 = Repair(c =>
            {
                c["scripts"][1]["format"] = "other.script";
                var n = J("{\"format\": \"hs2next.script\", \"version\": 2, \"id\": \"" + Id(0x5c9) + "\", \"name\": \"Later\", \"future\": {\"x\": [1, 2]}}");
                ((JArray)c["scripts"]).Add(n);
            });
            Found(t, r9.f, ScriptFindings.BadFormat, FindingSeverity.Warning, "scripts[1]", "a script of another format");
            Found(t, r9.f, ScriptFindings.NewerVersion, FindingSeverity.Warning, "scripts[2]", "a script of version 2");
            var raw9 = DocReader.Parse(Encoding.UTF8.GetBytes(r9.written), "scene.json");
            t.Check(r9.doc.Scripts.Scripts[1].IsOpaque && r9.doc.Scripts.Scripts[2].IsOpaque && r9.doc.Scripts.Scripts[2].Name == "Later" && r9.doc.Scripts.Scripts[2].Problem() == null
                && JToken.DeepEquals(raw9["scripts"][2], J("{\"format\": \"hs2next.script\", \"version\": 2, \"id\": \"" + Id(0x5c9) + "\", \"name\": \"Later\", \"future\": {\"x\": [1, 2]}}")),
                "JF4: both kept opaque and written back as read");
            t.Throws<ScriptRefusedException>(() => SceneScript.ParseDocument(Encoding.UTF8.GetBytes(raw9["scripts"][2].ToString())), "JF4: a standalone document of version 2 is refused");
            var nv = Assert<ScriptRefusedException>(() => SceneScript.ParseDocument(Encoding.UTF8.GetBytes(raw9["scripts"][2].ToString())));
            t.Check(nv != null && nv.Code == ScriptFindings.NewerVersion && nv.Version == 2, "JF4: with script.newerVersion and its number 2");
            var bf = Assert<ScriptRefusedException>(() => SceneScript.ParseDocument(Encoding.UTF8.GetBytes(raw9["scripts"][1].ToString())));
            t.Check(bf != null && bf.Code == ScriptFindings.BadFormat, "JF4: a standalone document of another format: script.badFormat");
            files++;
            // 10. The Timeline part follows the Timeline's rules (a second key at one time dropped).
            var r10 = Repair(c => ((JArray)c["scripts"][0]["timeline"]["tracks"][0]["keys"]).Add(J("{\"t\": 0.0, \"ease\": \"step\", \"v\": [9, 9, 9]}")));
            Found(t, r10.f, TimelineFindings.DuplicateKey, FindingSeverity.Warning, "scripts[0].timeline.tracks[0].keys[2]", "a script's Timeline: a second key at one time");
            t.Equal(2, r10.doc.Scripts.Scripts[0].Timeline.Tracks[0].Keys.Count, "JF4: the second key dropped");
            files++;
            // 11. A store document holding a node id where a role belongs.
            var d11 = DocReader.Parse(HandScript().ToDocumentBytes(), "script");
            d11["timeline"]["tracks"][0]["key"] = CharaA;
            var f11 = new FindingList();
            SceneScript.ParseDocument(Encoding.UTF8.GetBytes(d11.ToString()), f11);
            var h11 = f11.FirstOrDefault(x => x.Code == ScriptFindings.NodeKey);
            t.Check(h11 != null && h11.Severity == FindingSeverity.Info && h11.Path == "script:timeline.tracks[0].key", "JF4: a store document's node key: info script.nodeKey at timeline.tracks[0].key (" + f11 + ")");
            files++;
            t.Info("JF4: present: one file per repair, " + files + " files");
            t.Check(files >= 11, "JF4: every repair of the list has its file (" + files + ")");
        }

        static TEx Assert<TEx>(Action a) where TEx : Exception
        {
            try { a(); }
            catch (TEx e) { return e; }
            catch (Exception) { return null; }
            return null;
        }

        // ---------------- JF6 (as far as it runs here) ----------------

        static void JF6(TestRunner t)
        {
            string path = FixturePath(t.FixturesDir);
            if (!File.Exists(path)) { t.Check(false, "JF6: fixture " + path + " present"); return; }
            var fixture = File.ReadAllBytes(path);
            var fx = ContainerReader.Read(fixture);
            var now = FixtureScene().ToBytes(FixtureOptions());
            foreach (var e in fx.Entries)
                t.Check(Same(e.Data, EntryBytes(now, e.Name)), "JF6: the fixture's " + e.Name + " is what this writer writes for it");
            t.Check(FormatOf(fixture) == 5 && (int)Entry(fixture, "manifest.json")["minReaderVersion"] == 1, "JF6: the fixture is revision 5, minReaderVersion 1");
            var sj = Entry(fixture, "scene.json");
            t.Check(sj["scripts"] is JArray a && a.Count == 2 && sj["timeline"]["script"] != null, "JF6: present: both members are in the fixture (2 scripts, the marker)");

            var l5 = SceneContainer.Load(fixture, LOpts());
            t.Check(l5.Ok && !l5.NewerFormat && l5.Findings.Count == 0, "JF6: this reader reads the fixture with no finding (" + l5 + ")");
            if (!l5.Ok) return;
            t.Check(!l5.Value.Scene.Timeline.IsEditedSinceLoad && l5.Value.Scene.Timeline.Script.Id == ScriptDuel, "JF6: the loaded Timeline names its script and is not edited");

            var l4 = SceneContainer.Load(fixture, LOpts(Registry4()));
            t.Check(l4.Ok && l4.NewerFormat && l4.Findings.Count == 1 && l4.Findings[0].Code == "manifest.newerFormat" && l4.Findings[0].Message == "format 5 is newer than 4; unknown parts are kept",
                "JF6: an S = 4 registry reads it with NewerFormat and the single finding manifest.newerFormat (" + l4 + ")");
            if (!l4.Ok) return;
            var c = l4.Value;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            // As 1.3 holds them: `scripts` an unknown top-level member, `script` an unknown member of the Timeline.
            c.Scene.Scripts = null;
            c.Scene.Unknown = new JsonLeftovers();
            c.Scene.Unknown.Add("scripts", raw["scripts"].DeepClone());
            c.Scene.Timeline.Script = null;
            c.Scene.Timeline.Unknown = new JsonLeftovers();
            c.Scene.Timeline.Unknown.Add("script", raw["timeline"]["script"].DeepClone());
            string saved = Path.Combine(TempOf(t), "jf6_downgraded.hs2nscene");
            var sr = c.Save(saved, FixtureOptions());
            t.Check(sr.Ok && sr.Downgraded && sr.Findings.Count == 0, "JF6: saving it reports Downgraded and nothing else (" + sr.Findings + ")");
            var back = File.ReadAllBytes(saved);
            t.Equal(4, FormatOf(back), "JF6: written as its own lowest covering revision: 4 (the Timeline)");
            t.Check(Same(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "JF6: scene.json byte-equal\n    " + DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));
            var bx = ContainerReader.Read(back);
            foreach (var e in fx.Entries)
                if (e.Name != "manifest.json") t.Check(Same(e.Data, bx.Get(e.Name)?.Data), "JF6: " + e.Name + " byte-equal");
            t.Equal(fx.Entries.Count(), bx.Entries.Count(), "JF6: the same entries");
            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && !lb.NewerFormat && lb.Findings.Count == 0, "JF6: this reader then reads it silently (" + lb + ")");
            if (!lb.Ok) return;
            t.Check(lb.Value.Scene.Scripts?.Count == 2 && lb.Value.Scene.Timeline.Script?.Id == ScriptDuel && lb.Value.Scene.Unknown == null, "JF6: with the members known again");
            t.Check(FormatOf(lb.Value.ToBytes(FixtureOptions())) == 5 && Same(EntryBytes(lb.Value.ToBytes(FixtureOptions()), "scene.json"), EntryBytes(fixture, "scene.json")), "JF6: and writes revision 5 again, scene.json byte-equal");
        }

        // ---------------- JF7: save -> load -> save ----------------

        static void JF7(TestRunner t)
        {
            var cases = new List<(string what, Action<SceneContainer> shape)>
            {
                ("two scripts, one loaded", s => { }),
                ("the loaded Timeline edited after the load", s => s.Scene.Timeline.Tracks[0].Keys[1].Vector = new Vec3(7f, 7f, 7f)),
                ("unknown members, an opaque script, a script with no cast", s =>
                {
                    var x = s.Scene.Scripts.Scripts[0];
                    x.Unknown = new JsonLeftovers();
                    x.Unknown.Add("later", J("{\"q\": [0.1, 1e-7, -3.5e38]}"));
                    x.Roles[1].Unknown = new JsonLeftovers();
                    x.Roles[1].Unknown.Add("later", "role");
                    s.Scene.Timeline.Script.Unknown = new JsonLeftovers();
                    s.Scene.Timeline.Script.Unknown.Add("later", 1);
                    s.Scene.Scripts.Scripts.Add(SceneScript.ParseScene("{\"format\": \"hs2next.script\", \"version\": 9, \"id\": \"" + Id(0x5cb) + "\", \"x\": {}}"));
                    s.Scene.Scripts.Scripts[1].Cast.Clear();
                }),
                ("names that need escapes, steps of every value type", s =>
                {
                    var x = s.Scene.Scripts.Scripts[1];
                    x.Name = "Walk \"quoted\" \\ éあ\t";
                    x.Setup.Add(J("{\"cmd\": \"ping\", \"a\": null, \"b\": true, \"c\": [1, 2.5, \"s\", {\"d\": []}], \"e\": 340282346638528859811704183484516925440.0, \"f\": 1.401298464324817e-45}"));
                    x.Prepare.Add(J("{\"wait\": 10}"));
                }),
            };
            int markers = 0, casts = 0;
            foreach (var (what, shape) in cases)
            {
                var s = FixtureScene();
                shape(s);
                var first = s.ToBytes(Opts());
                var l = SceneContainer.Load(first, LOpts());
                t.Check(l.Ok, "JF7: " + what + ": loads (" + l + ")");
                if (!l.Ok) continue;
                var second = l.Value.ToBytes(Opts());
                t.Check(Same(first, second), "JF7: " + what + ": save -> load -> save byte-identical\n    " + DocIo.FirstDiff(first, second));
                var third = SceneContainer.Load(second, LOpts()).Value.ToBytes(Opts());
                t.Check(Same(first, third), "JF7: " + what + ": and once more");
                if (l.Value.Scene.Timeline?.Script != null) markers++;
                casts += l.Value.Scene.Scripts.Scripts.Sum(x => x.Cast.Count);
                if (what.StartsWith("the loaded Timeline edited", StringComparison.Ordinal))
                    t.Check(l.Value.Scene.Timeline.IsEditedSinceLoad, "JF7: " + what + ": reads as edited since the load");
            }
            t.Info("JF7: present: " + markers + " scenes with a marker, " + casts + " cast entries");
            t.Check(markers >= 1 && casts >= 3, "JF7: the scene holds a marker and casts");
        }

        // ---------------- JF8: the limits of an edit ----------------

        static void JF8(TestRunner t)
        {
            var baseList = FixtureScene().Scene.Scripts;
            t.Check(baseList.Problem() == null, "JF8: the fixture's scripts break no rule (" + baseList.Problem() + ")");
            string P(SceneScriptList after, SceneScriptList before = null) => after.Problem(before ?? baseList);
            SceneScriptList With0(Action<SceneScript> edit)
            {
                var x = baseList.Scripts[0].Clone();
                edit(x);
                return baseList.With(0, x);
            }
            var many = new SceneScriptList();
            for (int i = 0; i < 64; i++) { var x = WalkScript().ToScene(); x.Id = Id(0x7000 + i); many.Scripts.Add(x); }
            t.Check(many.Problem(baseList) == null, "JF8: 64 scripts pass");
            var tooMany = many.Adding(new Func<SceneScript>(() => { var x = WalkScript().ToScene(); x.Id = Id(0x7100); return x; })());
            t.Check(tooMany.Problem(many)?.Contains(ScriptFindings.Limit) == true, "JF8: a 65th refused, naming script.limit (" + tooMany.Problem(many) + ")");
            t.Check(tooMany.Problem(tooMany.Clone()) == null && tooMany.Without(64).Problem(tooMany) == null, "JF8: a file's 65 may stay and shrink");
            var cases = new (string what, Action<SceneScript> edit, bool refused)[]
            {
                ("33 roles", x => { for (int i = 0; i < 30; i++) x.Roles.Add(new ScriptRole("r" + i, ScriptRole.KindChara)); }, true),
                ("32 roles", x => { for (int i = 0; i < 29; i++) x.Roles.Add(new ScriptRole("r" + i, ScriptRole.KindChara)); }, false),
                ("257 steps", x => { for (int i = 0; i < 254; i++) x.Prepare.Add(J("{\"cmd\": \"ping\"}")); }, true),
                ("256 steps", x => { for (int i = 0; i < 253; i++) x.Prepare.Add(J("{\"cmd\": \"ping\"}")); }, false),
                ("a name of 129 characters", x => x.Name = new string('n', 129), true),
                ("a name of 128 characters", x => x.Name = new string('n', 128), false),
                ("a description of 2,049 characters", x => x.Description = new string('d', 2049), true),
                ("a wait of 0", x => x.Setup[1]["wait"] = 0, true),
                ("a wait of 10.5 s", x => x.Setup[1]["wait"] = 10.5, true),
                ("a wait of 10 s", x => x.Setup[1]["wait"] = 10, false),
                ("a step of the group script", x => x.Setup.Add(J("{\"cmd\": \"script.play\"}")), true),
                ("a step that edits the Timeline", x => x.Prepare.Add(J("{\"cmd\": \"timeline.set\"}")), true),
                ("a step with an id", x => x.Prepare.Add(J("{\"cmd\": \"ping\", \"id\": 1}")), true),
                ("a timed command of the group script", x => x.Timeline.Events[0].Do["cmd"] = "script.capture", true),
                ("a role name with a space", x => x.Roles[0].Role = "a b", true),
                ("two roles of one name", x => x.Roles[1].Role = "a", true),
                ("a parent naming a prop", x => x.Roles[2].Parent.Role = "p", true),
                ("an unknown kind", x => x.Roles[1].Kind = "light", true),
                ("a cast entry naming no id", x => x.Cast["a"] = "node", true),
                ("a preparation mark of 15 hex", x => x.Prepared = "0123456789abcde", true),
                ("a role key naming no declared role (a warning, not a refusal)", x => x.Timeline.Tracks[0].Key = "$zz", false),
            };
            foreach (var (what, edit, refused) in cases)
            {
                string p = P(With0(edit));
                t.Check(refused ? p != null : p == null, "JF8: " + what + (refused ? " refused" : " passes") + " (" + (p ?? "no problem") + ")");
            }
            // A file's bad step stays while another member is edited; a new one is refused.
            var file = SceneScriptList.Parse("[" + With0(x => x.Setup.Add(J("{\"cmd\": \"script.play\"}"))).Scripts[0].ToJson() + "]");
            var renamed = file.Scripts[0].Clone();
            renamed.Name = "Renamed";
            t.Check(file.With(0, renamed).Problem(file) == null, "JF8: a file's refused step stays while the name is edited");
            var another = renamed.Clone();
            another.Setup.Add(J("{\"cmd\": \"script.remove\"}"));
            t.Check(file.With(0, another).Problem(file) != null, "JF8: a second refused step is refused");
            t.Check(new SceneScriptList(new[] { baseList.Scripts[0], baseList.Scripts[0].Clone() }).Problem()?.Contains("twice") == true, "JF8: two scripts of one id refused");
            // A script kept as read (here a newer version without a valid id) does not block edits of the others; a new one of that kind is refused.
            var withOpaque = SceneScriptList.Parse("[" + baseList.Scripts[0].ToJson() + ", {\"format\": \"hs2next.script\", \"version\": 4, \"id\": \"later\"}]");
            var renamed0 = withOpaque.Scripts[0].Clone();
            renamed0.Name = "Renamed";
            t.Check(withOpaque.Scripts[1].IsOpaque && withOpaque.With(0, renamed0).Problem(withOpaque) == null, "JF8: an opaque script without a valid id does not block an edit of another script");
            t.Check(withOpaque.Clone().With(0, renamed0).Problem(withOpaque) == null, "JF8: nor when the list was copied with Clone");
            t.Check(withOpaque.Adding(SceneScript.ParseScene("{\"format\": \"hs2next.script\", \"version\": 5, \"id\": \"other\"}")).Problem(withOpaque) != null,
                "JF8: but a new opaque script without a valid id is refused");
        }
    }
}
