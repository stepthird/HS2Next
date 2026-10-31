// HS2Next pose files: the golden
// pose.json loads and saves byte-identically (every part, an unknown part, unknown members at the root and inside a
// part, fk.hipsPosition); every subset of the seven parts round-trips and writes `parts` in order; the reading rules
// for a listed-but-absent part, an unlisted part member and an absent `parts`; FromNode / CopyTo copy deeply; the
// .hs2npose container (load → save, manifest title / sex / format, library read of the manifest alone, detection by
// manifest.format against the other kinds, missing document, orphan ext file, foreign entry, atomic save); version
// and prerelease rules; fk.hipsPosition in the scene golden and its absent value.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using static HS2Next.Save.Tests.InfraFixtures;

namespace HS2Next.Save.Tests
{
    public static class DocTests_Pose
    {
        static byte[] Bytes(PoseDocument d) => CanonicalJsonWriter.ToBytes(PoseDocument.EntryName, d.Write);

        static PoseDocument Read(byte[] b, FindingList f = null) =>
            PoseDocument.Read(DocReader.ForDocument(b, new DocReadContext(PoseDocument.EntryName, false, f ?? new FindingList())));

        static PoseDocument ReadText(string json, FindingList f = null) => Read(Encoding.UTF8.GetBytes(json), f);

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            var golden = DocTests_T1Golden.Golden("pose.json");
            var doc = GoldenRound(t, golden);
            if (doc == null) return;
            Subsets(t, doc);
            ReadingRules(t);
            NodeCopies(t, doc);
            Container(t, golden);
            Versions(t, doc);
            SceneHips(t);
        }

        static PoseDocument GoldenRound(TestRunner t, byte[] golden)
        {
            var f = new FindingList();
            PoseDocument d;
            try { d = Read(golden, f); }
            catch (Exception e) { t.Check(false, "golden pose.json reads (" + e.GetType().Name + ": " + e.Message + ")"); return null; }
            var back = Bytes(d);
            t.Check(DocIo.Same(golden, back), "golden pose.json load → save byte-identical" + (DocIo.Same(golden, back) ? "" : " (" + DocIo.FirstDiff(golden, back) + ")"));
            t.Check(PoseDocument.PartNames.All(d.Has), "golden: every part present");
            t.Equal("anime,hands,fk,ik,correction,face,look,futurePart", string.Join(",", d.Parts), "golden: parts (known in order, then the unknown one)");
            t.Check(d.UnknownParts.Count == 1 && f.Has("pose.unknownPart"), "golden: unknown part kept and reported");
            t.Check(d.Unknown?.Get("futurePart") != null && d.Unknown?.Get("futurePoseMember") != null, "golden: unknown members at the root kept (the unknown part's member among them)");
            t.Check(d.Fk.Unknown?.Get("futureFkMember") != null, "golden: unknown member inside a part kept");
            t.Check(d.Fk.HipsPosition.HasValue && d.Fk.HipsPosition.Value.Equals(new Vec3(0f, 11.25f, 0.5f)), "golden: fk.hipsPosition read");
            t.Check(d.Name == "立ちポーズ 1" && d.Sex == 1 && d.Hands.SequenceEqual(new[] { 0, 3 }) && d.Correction[0] && d.Correction[7], "golden: name, sex, hands, correction");
            t.Check(d.Ik.Targets[0] != null && d.Ik.Targets[3] != null && d.Ik.Targets[12] != null && d.Ik.Targets.Targets.Count(x => x != null) == 3, "golden: three IK targets, the others missing");
            t.Check(d.Look.EyeRotations != null && d.Look.NeckFix.Count == 2 && d.Anime.Ref != null, "golden: look and anime contents");
            t.Equal(0, f.CountOf("pose.partMissing") + f.CountOf("pose.unlistedPart"), "golden: no missing / unlisted part findings");
            return d;
        }

        // Every subset of the seven parts: FromNode → bytes → read → bytes identical; `parts` = the subset in order;
        // absent parts have no member.
        static void Subsets(TestRunner t, PoseDocument golden)
        {
            var node = new CharaNodeState();
            golden.CopyTo(node);
            int bad = 0, n = 1 << PoseDocument.PartNames.Length;
            for (int mask = 0; mask < n; mask++)
            {
                var parts = new List<string>();
                for (int i = 0; i < PoseDocument.PartNames.Length; i++) if ((mask & 1 << i) != 0) parts.Add(PoseDocument.PartNames[i]);
                // Ask in reverse order: the written order is always PartNames order.
                var asked = new List<string>(parts);
                asked.Reverse();
                var d = PoseDocument.FromNode(node, "p" + mask, mask & 1, asked);
                var b1 = Bytes(d);
                var f = new FindingList();
                var r = Read(b1, f);
                var b2 = Bytes(r);
                var j = JObject.Parse(Encoding.UTF8.GetString(b1));
                bool ok = DocIo.Same(b1, b2) && string.Join(",", r.Parts) == string.Join(",", parts) && f.Count == 0 &&
                          ((JArray)j["parts"]).Select(x => (string)x).SequenceEqual(parts) &&
                          PoseDocument.PartNames.All(p => (j[p] != null) == parts.Contains(p) && r.Has(p) == parts.Contains(p));
                if (!ok)
                {
                    bad++;
                    if (bad <= 3) t.Info("subset " + string.Join(",", parts) + " failed: " + (DocIo.Same(b1, b2) ? "" : DocIo.FirstDiff(b1, b2)) + " findings " + f.Count);
                }
            }
            t.Equal(0, bad, "all " + n + " part subsets round-trip with parts in order and no other members");
            var empty = Encoding.UTF8.GetString(Bytes(new PoseDocument()));
            t.Equal("{\n  \"name\": \"\",\n  \"sex\": 1,\n  \"parts\": []\n}\n", empty.Replace("\r\n", "\n"), "an empty pose document");
        }

        static void ReadingRules(TestRunner t)
        {
            // Listed but absent → absent value of the member, info finding.
            var f = new FindingList();
            var d = ReadText("{\"name\":\"x\",\"sex\":0,\"parts\":[\"fk\",\"hands\",\"look\"]}", f);
            t.Check(d.Fk != null && !d.Fk.Enabled && d.Fk.Groups.Body && d.Fk.Bones.Count == 0 && d.Fk.HipsPosition == null && d.Hands.SequenceEqual(new[] { 0, 0 }) && d.Look != null && d.Look.Neck == 3,
                "listed but absent parts read as the absent values");
            t.Equal(3, f.CountOf("pose.partMissing"), "pose.partMissing per absent listed part");
            t.Equal("hands,fk,look", string.Join(",", d.Parts), "parts re-ordered on write");
            // Unlisted member → kept as unknown, not read, warning.
            f = new FindingList();
            d = ReadText("{\"name\":\"x\",\"sex\":1,\"parts\":[\"hands\"],\"hands\":[1,2],\"fk\":{\"enabled\":true}}", f);
            t.Check(d.Fk == null && d.Unknown?.Get("fk") != null && f.Has("pose.unlistedPart"), "an unlisted part member is not read, kept as an unknown member, reported");
            var text = Encoding.UTF8.GetString(Bytes(d));
            t.Check(text.Contains("\"fk\"") && text.IndexOf("\"fk\"", StringComparison.Ordinal) > text.IndexOf("\"hands\"", StringComparison.Ordinal), "the unlisted member is written back after the known members");
            d.Fk = new CharaFkState { Enabled = false };
            var j = JObject.Parse(Encoding.UTF8.GetString(Bytes(d)));
            t.Check((bool)j["fk"]["enabled"] == false && j.Properties().Count(p => p.Name == "fk") == 1 && ((JArray)j["parts"]).Count == 2, "setting that part writes it once (the kept unknown copy is dropped)");
            // Absent `parts` → the known part members present.
            f = new FindingList();
            d = ReadText("{\"face\":{\"eyes\":2},\"correction\":[true,true,true,true,true,true,true,true],\"other\":1}", f);
            t.Check(d.Face != null && d.Face.Eyes == 2 && d.Correction != null && d.Correction.All(x => x) && d.Anime == null && f.Count == 0 && d.Unknown?.Get("other") != null,
                "absent parts: the known members present are the parts");
            t.Check(d.Name == "" && d.Sex == 1, "absent name / sex: \"\" / 1");
            // Duplicate and unknown names in parts.
            f = new FindingList();
            d = ReadText("{\"parts\":[\"ik\",\"ik\",\"future\",\"future\"],\"ik\":{\"enabled\":true},\"future\":[1]}", f);
            t.Check(d.Ik != null && d.Ik.Enabled && string.Join(",", d.Parts) == "ik,future" && f.CountOf("pose.unknownPart") == 1, "duplicate part names collapse; an unknown name is kept once");
            // Type errors are the member schemas'.
            t.ThrowsCode(SaveErrorCode.BadDocument, () => ReadText("{\"parts\":[\"hands\"],\"hands\":[1,2,3]}"), "hands needs exactly 2 elements");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => ReadText("{\"parts\":[\"ik\"],\"ik\":{\"targets\":{\"Body\":null}}}"), "a null IK target is a type error, as in scenes");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => ReadText("{\"parts\":\"fk\"}"), "parts must be an array of strings");
            // Write guards of the member schemas.
            t.ThrowsCode(SaveErrorCode.BadDocument, () => Bytes(new PoseDocument { Hands = new[] { 1 } }), "writer refuses hands of length 1");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => Bytes(new PoseDocument { Correction = new bool[7] }), "writer refuses correction of length 7");
        }

        static void NodeCopies(TestRunner t, PoseDocument golden)
        {
            var node = new CharaNodeState();
            var copied = golden.CopyTo(node, new[] { "fk", "hands", "nope" });
            t.Equal("hands,fk", string.Join(",", copied), "CopyTo with a filter copies the held parts asked for, in order");
            t.Check(node.Fk.HipsPosition.Equals(golden.Fk.HipsPosition) && node.Hands.SequenceEqual(golden.Hands) && node.Face.Eyes == 0, "CopyTo copied fk and hands only");
            node.Fk.Bones["cf_J_Hips"] = new Vec3(1, 2, 3);
            node.Hands[0] = 9;
            t.Check(golden.Fk.Bones["cf_J_Hips"].Equals(new Vec3(0, 15, 0)) && golden.Hands[0] == 0, "CopyTo copies deeply");
            var d = PoseDocument.FromNode(node, "n", 0, new[] { "fk" });
            node.Fk.Bones["cf_J_Hips"] = new Vec3(4, 5, 6);
            t.Check(d.Fk.Bones["cf_J_Hips"].Equals(new Vec3(1, 2, 3)) && d.Hands == null && d.Sex == 0, "FromNode copies deeply and only the parts asked for");
            var all = PoseDocument.FromNode(node, null, 1);
            t.Check(PoseDocument.PartNames.All(all.Has) && all.Name == "", "FromNode without parts takes all seven");
            var golden2 = new CharaNodeState();
            t.Equal(7, golden.CopyTo(golden2).Count, "CopyTo without a filter copies every held part");
            t.Check(golden2.Fk.Unknown?.Get("futureFkMember") != null, "unknown members of a part travel with it");
        }

        static PoseContainer GoldenContainer(byte[] golden) => new PoseContainer { Pose = Read(golden) };

        static void Container(TestRunner t, byte[] golden)
        {
            var c = GoldenContainer(golden);
            var b1 = c.ToBytes(Opts());
            var l = PoseContainer.Load(b1, LOpts());
            t.Check(l.Ok, "container: .hs2npose loads (" + l + ")");
            if (!l.Ok) return;
            var b2 = l.Value.ToBytes(Opts());
            t.Check(DocIo.Same(b1, b2), ".hs2npose load → save byte-identical");
            var raw = ContainerReader.Read(b1);
            t.Check(DocIo.Same(raw.Get("pose.json").Data, golden), "pose.json inside the container = golden bytes");
            t.Equal("manifest.json,pose.json", string.Join(",", raw.Entries.Select(e => e.Name).Take(2)), "entry order: manifest, then the main document");
            var m = raw.ReadManifest();
            t.Check(m.Format == "hs2next.pose" && m.Title == "立ちポーズ 1" && m.Sex == 1 && m.ThumbnailWidth.HasValue, "manifest: format hs2next.pose, title = name, sex, thumbnail");
            t.Check(SaveFormats.Pose == "hs2next.pose" && SaveFormats.PoseExtension == ".hs2npose" && c.Format == SaveFormats.Pose, "format and extension constants");
            var lib = ContainerReader.Read(b1, null, n => n == "manifest.json");
            t.Check(lib.ReadManifest().Format == SaveFormats.Pose && lib.Get("pose.json").Skipped, "library list: the manifest alone (pose.json not inflated)");

            // Detection by manifest.format, never by extension.
            var scene = new SceneContainer { Title = "s" }.ToBytes(Opts());
            t.Equal(SaveErrorCode.BadManifest, PoseContainer.Load(scene, LOpts()).ErrorCode, "PoseContainer.Load of a scene → BadManifest");
            t.Equal(SaveErrorCode.BadManifest, SceneContainer.Load(b1, LOpts()).ErrorCode, "SceneContainer.Load of a pose → BadManifest");
            t.Equal(SaveErrorCode.BadManifest, CharaContainer.Load(b1, LOpts()).ErrorCode, "CharaContainer.Load of a pose → BadManifest");
            t.Equal(SaveErrorCode.BadManifest, CoordContainer.Load(b1, LOpts()).ErrorCode, "CoordContainer.Load of a pose → BadManifest");
            string renamed = Path.Combine(t.TempDir, "pose_renamed.png");
            File.WriteAllBytes(renamed, b1);
            var lr = PoseContainer.Load(renamed, LOpts());
            t.Check(lr.Ok && lr.Value.Pose.Name == "立ちポーズ 1", "a copy renamed to .png loads the same way");

            // Missing document, orphan ext file, foreign entry, a pose.json in another kind.
            var noDoc = Repack(b1, map => map["pose.json"] = null, "manifest.json");
            t.Equal(SaveErrorCode.MissingDocument, PoseContainer.Load(noDoc, LOpts()).ErrorCode, "missing pose.json → MissingDocument");
            var extra = Repack(b1, map => { map["ext/some.plugin/data.json"] = Utf8("{}"); map["notes/readme.txt"] = Utf8("hello"); }, "pose.json");
            var le = PoseContainer.Load(extra, LOpts());
            t.Check(le.Ok && le.Findings.Has("container.orphan") && le.Value.ForeignEntries.ContainsKey("notes/readme.txt"), "ext file = orphan (no ext in v1), foreign entry kept");
            var re = ContainerReader.Read(le.Value.ToBytes(Opts()));
            t.Check(re.Get("ext/some.plugin/data.json") == null && re.Get("notes/readme.txt") != null, "save drops the orphan and keeps the foreign entry");
            var chara = SmallChara(1).ToBytes(Opts());
            var withPose = Repack(chara, map => map["pose.json"] = golden);
            var lc = CharaContainer.Load(withPose, LOpts());
            t.Check(lc.Ok && lc.Findings.Has("container.orphan") && !lc.Value.ForeignEntries.ContainsKey("pose.json"), "pose.json in a character file is an orphan (core area)");

            // Atomic save to a path.
            string path = Path.Combine(t.TempDir, "pose" + SaveFormats.PoseExtension);
            var sr = c.Save(path, Opts());
            var lp = PoseContainer.Load(path, LOpts());
            t.Check(sr.Ok && lp.Ok && DocIo.Same(File.ReadAllBytes(path), b1), "Save(path) writes the same bytes and loads (" + lp + ")");
            using (var fs = File.OpenRead(path)) t.Check(PoseContainer.Load(fs, LOpts()).Ok, "Load(stream)");
            t.Check(!new PoseContainer { Pose = null }.Save(Path.Combine(t.TempDir, "null.hs2npose"), Opts()).Ok, "a null document is refused, not thrown");
        }

        static void Versions(TestRunner t, PoseDocument doc)
        {
            int S = SaveFormats.SupportedVersion;
            var orig = new PoseContainer { Pose = doc }.ToBytes(Opts());
            var tooNew = Repack(orig, m => EditJson(m, "manifest.json", j => { j["minReaderVersion"] = S + 1; j["formatVersion"] = S + 1; }), "pose.json");
            t.Equal(SaveErrorCode.ReaderTooOld, PoseContainer.Load(tooNew, LOpts()).ErrorCode, "minReaderVersion S+1 → ReaderTooOld");
            var newer = Repack(orig, m =>
            {
                EditJson(m, "manifest.json", j => j["formatVersion"] = S + 1);
                EditJson(m, "pose.json", j => { j["futureTop"] = 1; ((JArray)j["parts"]).Add("expression"); j["expression"] = new JObject { ["x"] = 1 }; });
            }, "pose.json");
            var ln = PoseContainer.Load(newer, LOpts());
            t.Check(ln.Ok && ln.NewerFormat && ln.Findings.Has("manifest.newerFormat") && ln.Value.Pose.UnknownParts.Contains("expression"), "formatVersion S+1 loads with NewerFormat; a newer part is kept");
            if (ln.Ok)
            {
                var back = ContainerReader.Read(ln.Value.ToBytes(Opts()));
                var pj = JObject.Parse(Encoding.UTF8.GetString(back.Get("pose.json").Data));
                var mj = JObject.Parse(Encoding.UTF8.GetString(back.Get("manifest.json").Data));
                t.Check(pj["futureTop"] != null && pj["expression"] != null && ((JArray)pj["parts"]).Last().ToString() == "expression" && (int)mj["formatVersion"] == SaveFormats.FormatVersion,
                    "saved back with this app's lowest covering formatVersion (the base), the newer part and member kept");
            }
            var pre = Repack(orig, m => EditJson(m, "manifest.json", j => j["prerelease"] = "dev.0"), "pose.json");
            var lp = PoseContainer.Load(pre, LOpts());
            t.Check(lp.Ok && lp.Findings.Has("manifest.prerelease"), "another prerelease is read best effort with a finding");
            var rel = Repack(orig, m => EditJson(m, "manifest.json", j => j["prerelease"] = null), "pose.json");
            var lrel = PoseContainer.Load(rel, LOpts());
            t.Check(lrel.Ok && !lrel.Findings.Has("manifest.prerelease"), "a release file (prerelease null) reads without a finding");
            var written = JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(orig).Get("manifest.json").Data))["prerelease"];
            t.Check(written != null && written.Type == JTokenType.Null, "writers put prerelease null since the format v1 freeze");
        }

        static void SceneHips(TestRunner t)
        {
            var scene = DocIo.Scene(DocTests_T1Golden.Golden("scene.json"));
            var chara = (CharaNodeState)scene.Nodes.First(n => n.State is CharaNodeState).State;
            t.Check(chara.Fk.HipsPosition.HasValue && chara.Fk.HipsPosition.Value.Equals(new Vec3(0f, 11.5f, 0.25f)), "scene golden: fk.hipsPosition read");
            // The fk part of a pose document is the same CharaFkState (public surface only: Editor tests cannot see internals).
            var d = ReadText("{\"parts\":[\"fk\"],\"fk\":{\"enabled\":true,\"hipsOffset\":0.5}}");
            t.Check(d.Fk.HipsPosition == null && d.Fk.HipsOffset == 0.5f, "fk.hipsPosition absent (files before 3.4) → null");
            d = ReadText("{\"parts\":[\"fk\"],\"fk\":{\"hipsPosition\":null}}");
            t.Check(d.Fk.HipsPosition == null, "fk.hipsPosition null → null");
            t.ThrowsCode(SaveErrorCode.BadDocument, () => ReadText("{\"parts\":[\"fk\"],\"fk\":{\"hipsPosition\":[1,2]}}"), "fk.hipsPosition needs 3 numbers");
            var fk = new PoseDocument { Fk = new CharaFkState() };
            string text = Encoding.UTF8.GetString(Bytes(fk)).Replace("\r\n", "\n");
            t.Check(text.Contains("\"hipsOffset\": 0.0,\n    \"hipsPosition\": null\n"), "fk.hipsPosition null written right after hipsOffset");
            fk.Fk.HipsPosition = new Vec3(0.5f, 10f, -0.25f);
            text = Encoding.UTF8.GetString(Bytes(fk)).Replace("\r\n", "\n");
            t.Check(text.Contains("\"hipsOffset\": 0.0,\n    \"hipsPosition\": [0.5, 10.0, -0.25]\n"), "fk.hipsPosition value written right after hipsOffset");
        }
    }
}
