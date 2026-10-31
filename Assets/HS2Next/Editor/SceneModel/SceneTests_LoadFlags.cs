// The partial character load (Phase 4.6; SetCharaSectionsCommand, LoadFlags): on the golden character (with a head-bone
// modifier added) loading from a golden-derived source changed in every section, with plugin entries of both classes on
// both sides (face / body skin overlays, head / body bone modifiers, head / body MaterialEditor character items), for each
// flag and every pair of the four document sections (and «コーデ»'s display switch): the merged document equals the merge
// computed here by the rule (the target's entries of the classes not loaded in their order, then the source's of the loaded
// classes); the character's sex, chara-scope inline ext, unknown members, ext data and provenance stay; the reported paths
// are second-level members, never the whole document; one history entry; undo / redo byte-identical. Also: a source of the
// other sex is refused; the head names are the own side's (the target's entries by the target's head, the source's by the
// source's); the whole-appearance swap (all five flags) replaces the ext data and the provenance, a partial load keeps them;
// a card-backed target imported in the same entry (one entry; undo restores the card-backed node and the carrier); the
// flag names.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_LoadFlags
    {
        public static void Run(TestRunner t)
        {
            Names(t);
            var target = TargetJson(t);
            var source = SourceJson(target);
            var cases = new List<LoadFlags> { LoadFlags.Face, LoadFlags.Body, LoadFlags.Hair, LoadFlags.Profile, LoadFlags.Coordinate };
            var four = new[] { LoadFlags.Face, LoadFlags.Body, LoadFlags.Hair, LoadFlags.Profile };
            for (int i = 0; i < four.Length; i++)
                for (int j = i + 1; j < four.Length; j++) cases.Add(four[i] | four[j]);
            cases.Add(LoadFlags.Face | LoadFlags.Body | LoadFlags.Hair | LoadFlags.Profile | LoadFlags.Coordinate);
            foreach (var f in cases) Case(t, target, source, f);
            OtherSex(t, target, source);
            OwnSideHeads(t);
            WholeAndKept(t, target, source);
            CardBacked(t, target, source);
        }

        // ---------------------------------------------------------------- documents

        const string Res5 = "res/5555555555555555555555555555555555555555555555555555555555555555.png";
        const string Res6 = "res/6666666666666666666666666666666666666666666666666666666666666666.png";

        static JObject Parse(byte[] b) => DocReader.Parse(b, CharaDocument.EntryName);
        static byte[] Bytes(JObject j) => CanonicalJsonWriter.ToBytes(j, CharaDocument.EntryName);
        static CharaDocument Doc(JObject j) => CharaDocument.Read(DocReader.ForDocument(Bytes(j), new DocReadContext(CharaDocument.EntryName)));
        static byte[] Canonical(JObject j) => CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, Doc(j).Write);
        static JObject Bone(string bone) => new JObject
        {
            ["bone"] = bone, ["location"] = "unknown", ["scale"] = new JArray(1.0, 1.0, 1.0), ["length"] = 1.0,
            ["position"] = new JArray(0.0, 0.0, 0.0), ["rotation"] = new JArray(0.0, 0.0, 0.0),
        };

        // The golden character plus a head-bone modifier (the target).
        static JObject TargetJson(TestRunner t)
        {
            var j = Parse(Kit.Golden(t, "chara.json"));
            ((JArray)j["body"]["boneMods"]).Add(Bone("cf_J_Mayu_L"));
            return j;
        }

        // A source changed in every section, with face and body entries of the three plugin lists.
        static JObject SourceJson(JObject target)
        {
            var j = (JObject)target.DeepClone();
            j["profile"]["name"] = "Source chara";
            j["profile"]["futanari"] = true;
            j["profile"]["hs2"]["personality"] = 9;
            ((JArray)j["face"]["shape"])[0] = 0.125;
            j["face"]["skin"] = "illusion.hs2#5";
            ((JArray)j["body"]["shape"])[0] = 0.375;
            j["body"]["skin"]["gloss"] = 0.25;
            j["body"]["visible"] = false;
            j["body"]["skinOverlays"] = new JArray(
                new JObject { ["target"] = "FaceOver", ["image"] = Res5 },
                new JObject { ["target"] = "BodyUnder", ["image"] = Res6 });
            j["body"]["boneMods"] = new JArray(Bone("cf_J_FaceBase"), Bone("cf_J_Hips"));
            j["materialOverrides"] = new JArray(
                JObject.Parse("{\"type\": \"renderer\", \"renderer\": \"o_head\", \"property\": \"Enabled\", \"value\": 0, \"original\": 1}"),
                JObject.Parse("{\"type\": \"float\", \"material\": \"cf_m_skin_body_00\", \"property\": \"Glossiness\", \"value\": 0.5, \"original\": 0.75}"),
                JObject.Parse("{\"type\": \"shader\", \"material\": \"cf_m_skin_head_01\", \"shader\": null, \"shaderOriginal\": null, \"renderQueue\": 2000, \"renderQueueOriginal\": null}"));
            j["hair"]["back"]["baseColor"] = new JArray(0.875, 0.125, 0.125, 1.0);
            j["hair"]["shaderType"] = 1;
            j["studioOverlays"] = new JObject { ["skin"] = false, ["eyes"] = true, ["clothes"] = false };
            j["ext"] = new JObject { ["source.plugin"] = new JObject { ["x"] = 1 } };
            j["futureCharaMember"] = "source";
            return j;
        }

        // The classes as this test knows them (independent of HeadNames): face overlays by target, face bones and face
        // MaterialEditor items by the names used above.
        static readonly HashSet<string> FaceTargets = new HashSet<string> { "FaceOver", "EyeOverL", "EyeOverR", "EyeUnderL", "EyeUnderR", "FaceUnder", "FaceDetailOver", "FaceDetailUnder", "EyebrowUnder", "EyelineUnder" };
        static bool FaceOverlay(JToken o) => FaceTargets.Contains((string)o["target"]);
        static bool FaceBone(JToken b) => (string)b["bone"] == "cf_J_FaceBase" || (string)b["bone"] == "cf_J_Mayu_L";
        static bool FaceItem(JToken m) => (string)m["renderer"] == "o_head" || ((string)m["material"] ?? "").StartsWith("cf_m_skin_head", StringComparison.Ordinal);

        static JArray Merge(JToken tgt, JToken src, Func<JToken, bool> isFace, bool face, bool body)
        {
            var r = new JArray();
            foreach (var x in (JArray)tgt) if (!(isFace(x) ? face : body)) r.Add(x.DeepClone());
            foreach (var x in (JArray)src) if (isFace(x) ? face : body) r.Add(x.DeepClone());
            return r;
        }

        /// <summary>The expected merged document by the sections rule, computed on the JSON.</summary>
        static JObject Expected(JObject tgt, JObject src, LoadFlags f)
        {
            bool face = (f & LoadFlags.Face) != 0, body = (f & LoadFlags.Body) != 0;
            var e = (JObject)tgt.DeepClone();
            if (face)
            {
                e["face"] = src["face"].DeepClone();
                e["studioOverlays"]["eyes"] = src["studioOverlays"]["eyes"];
            }
            if (body)
            {
                foreach (var p in ((JObject)src["body"]).Properties())
                    if (p.Name != "skinOverlays" && p.Name != "boneMods") e["body"][p.Name] = p.Value.DeepClone();
                e["studioOverlays"]["skin"] = src["studioOverlays"]["skin"];
            }
            e["body"]["skinOverlays"] = Merge(tgt["body"]["skinOverlays"], src["body"]["skinOverlays"], FaceOverlay, face, body);
            e["body"]["boneMods"] = Merge(tgt["body"]["boneMods"], src["body"]["boneMods"], FaceBone, face, body);
            e["materialOverrides"] = Merge(tgt["materialOverrides"], src["materialOverrides"], FaceItem, face, body);
            if ((f & LoadFlags.Hair) != 0) e["hair"] = src["hair"].DeepClone();
            if ((f & LoadFlags.Coordinate) != 0) e["studioOverlays"]["clothes"] = src["studioOverlays"]["clothes"];
            if ((f & LoadFlags.Profile) != 0)
            {
                e["profile"]["name"] = src["profile"]["name"];
                e["profile"]["futanari"] = src["profile"]["futanari"];
                e["profile"]["hs2"] = src["profile"]["hs2"].DeepClone();
            }
            return e;
        }

        static SceneSession Session(TestRunner t, JObject target, out string id, SessionOptions o = null)
        {
            var s = Kit.Session(1, o ?? new SessionOptions { History = new HistoryLimits { MaxEntries = int.MaxValue, MaxBytes = long.MaxValue } });
            var sub = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone();
            sub.Chara = Doc(target);
            sub.Import = new ImportProvenance { From = "hs2.chara", FileName = "target.png", Sha256 = new string('7', 64), FileSize = 7, Importer = "test", Hs2Version = "1.0.0" };
            id = s.AddChara(new CharaAppearance { Stored = sub }).NewIds[0];
            s.Commands.ClearHistory();
            return s;
        }

        static byte[] CharaBytes(SceneSession s, string id) => SceneTests_Appearance.CharaBytes(s, id);
        static byte[] CoordBytes(SceneSession s, string id) => SceneTests_Appearance.CoordBytes(s, id);

        static CommandResult Tx(SceneSession s, Func<Transaction, CommandResult> body)
        {
            using (var tx = s.Commands.BeginOrJoin("Load character"))
            {
                var r = body(tx);
                if (r == null || r.Status == CommandStatus.Error) return r;
                var c = tx.Commit();
                return c.Status == CommandStatus.Error ? c : r;
            }
        }

        // ---------------------------------------------------------------- cases

        static void Case(TestRunner t, JObject target, JObject source, LoadFlags f)
        {
            string w = "load " + string.Join("+", LoadFlagNames.ToNames(f)) + ": ";
            var s = Session(t, target, out string id);
            var st = s.Model.Find(id).Appearance.Stored;
            var docObj = st.Chara;
            byte[] c0 = CharaBytes(s, id), o0 = CoordBytes(s, id);
            string ext0 = SetCoordinateCommand.ExtFingerprint(st.CharaExt), imp0 = st.Import.FileName + "/" + st.Import.Sha256;
            var rec = new EventRecorder(s);
            var cmd = new SetCharaSectionsCommand(id, Doc(source), f, HeadNames.Vanilla, "Load character");
            var r = Tx(s, tx => tx.Execute(cmd));
            t.Check(r != null && r.Ok, w + "applied (" + r + ")");
            byte[] c1 = CharaBytes(s, id);
            var want = Canonical(Expected(target, source, f));
            t.Check(Kit.Same(want, c1), w + "the document = the merge by the rule" + (Kit.Same(want, c1) ? "" : " (" + Kit.Diff(want, c1) + ")"));
            var j1 = Parse(c1);
            t.Check((int)j1["profile"]["sex"] == 1 && JToken.DeepEquals(j1["ext"], target["ext"]) && (string)j1["futureCharaMember"] == (string)target["futureCharaMember"],
                w + "the sex, the chara-scope inline ext and the unknown members stay the character's");
            t.Check(ReferenceEquals(s.Model.Find(id).Appearance.Stored.Chara, docObj), w + "the same document object (contents replaced in place)");
            t.Check(Kit.Same(o0, CoordBytes(s, id)) && SetCoordinateCommand.ExtFingerprint(st.CharaExt) == ext0 && st.Import.FileName + "/" + st.Import.Sha256 == imp0,
                w + "the coordinate, the ext data and the provenance are untouched");
            var paths = rec.Batches.SelectMany(b => b.Changes).Where(c => c.NodeId == id).Select(c => c.Path).Distinct().ToList();
            bool second = paths.Count > 0 && paths.All(p => p == AppearancePaths.Chara("materialOverrides") || p == AppearancePaths.Chara("studioOverlays") ||
                (p.StartsWith(AppearancePaths.CharaPrefix + ".", StringComparison.Ordinal) && p.Substring(AppearancePaths.CharaPrefix.Length + 1).Split('.').Length == 2));
            t.Check(second && paths.SequenceEqual(cmd.Reported), w + "reported = second-level members (" + string.Join(", ", paths) + ")");
            bool faceReported = paths.Any(p => p.StartsWith(AppearancePaths.Chara("face."), StringComparison.Ordinal));
            bool hairReported = paths.Any(p => p.StartsWith(AppearancePaths.Chara("hair."), StringComparison.Ordinal));
            t.Check(faceReported == ((f & LoadFlags.Face) != 0) && hairReported == ((f & LoadFlags.Hair) != 0), w + "face / hair members reported exactly when loaded");
            t.Equal(1, s.Commands.UndoList.Count, w + "one history entry");
            s.Commands.Undo();
            t.Check(Kit.Same(c0, CharaBytes(s, id)) && ReferenceEquals(s.Model.Find(id).Appearance.Stored.Chara, docObj), w + "undo: the previous bytes, the same object");
            s.Commands.Redo();
            t.Check(Kit.Same(c1, CharaBytes(s, id)), w + "redo: the merged bytes again");
        }

        static void OtherSex(TestRunner t, JObject target, JObject source)
        {
            var s = Session(t, target, out string id);
            var male = (JObject)source.DeepClone();
            male["profile"]["sex"] = 0;
            var before = CharaBytes(s, id);
            var r = Tx(s, tx => tx.Execute(new SetCharaSectionsCommand(id, Doc(male), LoadFlags.Face, HeadNames.Vanilla)));
            Kit.Fails(t, r, CommandCodes.BadArgs, "a male source on the female character");
            t.Check(r != null && r.Message.Contains("same sex") && Kit.Same(before, CharaBytes(s, id)) && s.Commands.UndoList.Count == 0, "other sex: refused, nothing changed, no entry");
            var none = Tx(s, tx => tx.Execute(new SetCharaSectionsCommand(id, Doc(source), LoadFlags.None)));
            Kit.Fails(t, none, CommandCodes.BadArgs, "no section");
            var bad = Tx(s, tx => tx.Execute(new SetCharaSectionsCommand(id, Doc(source), (LoadFlags)64)));
            Kit.Fails(t, bad, CommandCodes.BadArgs, "an unknown section bit");
            var nul = Tx(s, tx => tx.Execute(new SetCharaSectionsCommand(id, null, LoadFlags.Face)));
            Kit.Fails(t, nul, CommandCodes.BadArgs, "no source");
            var same = Tx(s, tx => tx.Execute(new SetCharaSectionsCommand(id, Doc(target), LoadFlags.Face | LoadFlags.Body | LoadFlags.Hair | LoadFlags.Profile)));
            t.Check(same != null && same.Status == CommandStatus.NoOp && s.Commands.UndoList.Count == 0, "loading the character's own document: no change, no entry (" + same + ")");
        }

        sealed class TwoHeads : IHeadNames
        {
            public IReadOnlyCollection<string> Of(int sex, string headRef) =>
                headRef == "test.head#1" ? new[] { "boneA", "o_rendA" } : headRef == "test.head#2" ? new[] { "boneB", "o_rendB" } : Array.Empty<string>();
        }

        // The target's entries are classified by the target's head, the source's by the source's (a head change moves the class).
        static void OwnSideHeads(TestRunner t)
        {
            var tj = Parse(Canonical(new JObject()));
            tj["face"]["head"] = "test.head#1";
            tj["body"]["boneMods"] = new JArray(Bone("boneA"), Bone("boneB"));
            tj["materialOverrides"] = new JArray(JObject.Parse("{\"type\": \"renderer\", \"renderer\": \"o_rendA\", \"property\": \"Enabled\", \"value\": 0, \"original\": 1}"),
                JObject.Parse("{\"type\": \"renderer\", \"renderer\": \"o_rendB\", \"property\": \"Enabled\", \"value\": 0, \"original\": 1}"));
            var sj = (JObject)tj.DeepClone();
            sj["face"]["head"] = "test.head#2";
            ((JObject)sj["body"]["boneMods"][0])["length"] = 2.0;
            ((JObject)sj["body"]["boneMods"][1])["length"] = 2.0;
            ((JObject)sj["materialOverrides"][0])["value"] = 5;
            ((JObject)sj["materialOverrides"][1])["value"] = 5;
            var merged = Parse(SetCharaSectionsCommand.Merge(Canonical(tj), Canonical(sj), LoadFlags.Face, new TwoHeads()));
            var bones = ((JArray)merged["body"]["boneMods"]).Select(b => (string)b["bone"] + ":" + (double)b["length"]).ToList();
            t.Check(bones.SequenceEqual(new[] { "boneB:1", "boneB:2" }), "own heads: face only keeps the target's body-class bone (boneB by head 1), takes the source's face-class bone (boneB by head 2) (" + string.Join(", ", bones) + ")");
            var items = ((JArray)merged["materialOverrides"]).Select(m => (string)m["renderer"] + ":" + (int)m["value"]).ToList();
            t.Check(items.SequenceEqual(new[] { "o_rendB:0", "o_rendB:5" }), "own heads: the MaterialEditor items likewise (" + string.Join(", ", items) + ")");
            var body = Parse(SetCharaSectionsCommand.Merge(Canonical(tj), Canonical(sj), LoadFlags.Body, new TwoHeads()));
            var bb = ((JArray)body["body"]["boneMods"]).Select(b => (string)b["bone"] + ":" + (double)b["length"]).ToList();
            t.Check(bb.SequenceEqual(new[] { "boneA:1", "boneA:2" }) && (string)body["face"]["head"] == "test.head#1", "own heads: body only keeps the target's face bone and takes the source's body bone, the head stays");
        }

        // All five flags swap the whole appearance (SetAppearanceCommand, as Change character): ext data and provenance are the
        // source's; a partial load of all four sections keeps the character's.
        static void WholeAndKept(TestRunner t, JObject target, JObject source)
        {
            var s = Session(t, target, out string id);
            var src = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone();
            src.Chara = Doc(source);
            src.Import = new ImportProvenance { From = "hs2.chara", FileName = "source.png", Sha256 = new string('8', 64), FileSize = 8, Importer = "test", Hs2Version = "1.0.0" };
            src.CharaExt.Blocks["source.blocks"] = new ExtBlock("source.blocks") { IsUnknown = true };
            var st0 = s.Model.Find(id).Appearance.Stored;
            string ext0 = SetCoordinateCommand.ExtFingerprint(st0.CharaExt);
            var r = Tx(s, tx => tx.Execute(new SetCharaSectionsCommand(id, src.Chara, LoadFlags.Face | LoadFlags.Body | LoadFlags.Hair | LoadFlags.Profile | LoadFlags.Coordinate)));
            t.Check(r != null && r.Ok && s.Model.Find(id).Appearance.Stored == st0 && st0.Import.FileName == "target.png" && SetCoordinateCommand.ExtFingerprint(st0.CharaExt) == ext0,
                "partial (every section): the stored subtree, its provenance and its chara-scope ext data stay the character's");
            var app = new CharaAppearance { Stored = src.Clone() };
            var w = Tx(s, tx => tx.Execute(new SetAppearanceCommand(id, app, "Load character")));
            var st1 = s.Model.Find(id).Appearance.Stored;
            t.Check(w != null && w.Ok && st1.Import.FileName == "source.png" && st1.CharaExt.Blocks.ContainsKey("source.blocks") && Kit.Same(Canonical(source), CharaBytes(s, id)),
                "all five: the whole appearance, the provenance and the ext data are the source's");
        }

        sealed class FakeImporter : ICharaImporter
        {
            public CharaSubtree Subtree;
            public int Calls;
            public CharaContainer Import(CardAppearanceSource source, FindingList findings)
            {
                Calls++;
                return new CharaContainer { Subtree = Subtree.Clone() };
            }
        }

        // A card-backed character is imported in the same entry as the load; undo leaves it card-backed with no subtree in the
        // scene carrier, redo gives the loaded document again.
        static void CardBacked(TestRunner t, JObject target, JObject source)
        {
            var golden = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone();
            golden.Chara = Doc(target);
            var fake = new FakeImporter { Subtree = golden };
            var s = Kit.Session(5, new SessionOptions { Importer = fake });
            var card = Kit.Card("load target", "UserData/chara/female/load.png", 2);
            var id = s.AddChara(new CharaAppearance { Card = card }).NewIds[0];
            s.Commands.ClearHistory();
            var r = Tx(s, tx =>
            {
                var imp = MakerEdits.EnsureImported(tx, s.Model, s.Model.Find(id));
                return imp.Status == CommandStatus.Error ? imp : tx.Execute(new SetCharaSectionsCommand(id, Doc(source), LoadFlags.Face, HeadNames.Vanilla));
            });
            var a = s.Model.Find(id).Appearance;
            var loaded = a?.Stored != null ? CharaBytes(s, id) : null;
            t.Check(r != null && r.Ok && fake.Calls == 1 && a.Stored != null && s.Model.File.Charas.ContainsKey(id) && s.Commands.UndoList.Count == 1,
                "card-backed target: imported and loaded in one entry (" + r + ")");
            t.Check(loaded != null && Kit.Same(Canonical(Expected(target, source, LoadFlags.Face)), loaded), "card-backed target: the face is the source's");
            s.Commands.Undo();
            var u = s.Model.Find(id).Appearance;
            t.Check(u.Stored == null && u.Card == card && !s.Model.File.Charas.ContainsKey(id), "undo: card-backed again, no subtree in the scene carrier");
            s.Commands.Redo();
            t.Check(s.Model.Find(id).Appearance.Stored != null && Kit.Same(loaded, CharaBytes(s, id)) && fake.Calls == 1, "redo: the same loaded document, no second import");
        }

        // ---------------------------------------------------------------- names

        static void Names(TestRunner t)
        {
            t.Check((int)LoadFlags.Face == 1 && (int)LoadFlags.Body == 2 && (int)LoadFlags.Hair == 4 && (int)LoadFlags.Coordinate == 8 && (int)LoadFlags.Profile == 16 && (int)LoadFlags.All == 31,
                "flags: HS2's bits (face 1, body 2, hair 4, coordinate 8, profile 16)");
            t.Check(LoadFlagNames.Names.SequenceEqual(new[] { "face", "body", "hair", "coordinate", "profile" }), "flag names in bit order");
            t.Check(LoadFlagNames.TryParse("Face", out var f1) && f1 == LoadFlags.Face && LoadFlagNames.TryParse("all", out var fa) && fa == LoadFlags.All && !LoadFlagNames.TryParse("hands", out _),
                "TryParse: case-insensitive names, \"all\", unknown refused");
            t.Check(LoadFlagNames.TryParse(new[] { "hair", "profile" }, out var fl, out var unk) && fl == (LoadFlags.Hair | LoadFlags.Profile) && unk == null, "TryParse of a list");
            t.Check(!LoadFlagNames.TryParse(new[] { "face", "x" }, out _, out var unk2) && unk2 == "x", "TryParse of a list names the unknown entry");
            t.Check(LoadFlagNames.ToNames(LoadFlags.Body | LoadFlags.Coordinate).SequenceEqual(new[] { "body", "coordinate" }) && LoadFlagNames.IsAll(LoadFlags.All) && !LoadFlagNames.IsAll(LoadFlags.Face | LoadFlags.Body),
                "ToNames, IsAll");
        }
    }
}
