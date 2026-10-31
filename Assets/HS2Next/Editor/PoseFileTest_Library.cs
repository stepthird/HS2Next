// Pose library scan and FK key map tests on synthetic data (
// item 2): recursive scan of a temporary folder (statuses, counts, ordering, no sex filter), key families, the
// AI-numbering heuristic, mapping with skip reasons, group-column tables.
namespace HS2Next.Import.Pose.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using HS2Next.Import.Pose;
    using HS2Next.Save.Tests;

    public static class PoseLibraryScanTests
    {
        public static void Run(TestRunner t)
        {
            string root = Path.Combine(t.TempDir ?? Path.GetTempPath(), "poselib_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                void Put(string rel, byte[] b)
                {
                    var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllBytes(p, b);
                }
                var female = PoseSpec.Standard(51); female.Name = "female pose";
                var male = PoseSpec.Standard(52); male.Sex = 0; male.Name = "male pose";
                var bad = PoseSpec.Standard(53); bad.Magic = "【other】";
                Put("b/x.dat", female.Build());
                Put("a/y.PNG", PoseKit.Concat(PoseKit.Png(), male.Build()));
                Put("a/plain.png", PoseKit.Png());
                Put("c/z.dat", bad.Build());
                Put("c/trunc.DAT", PoseKit.Prefix(female.Build(), 100));
                Put("notes.txt", new byte[] { 1, 2 });
                Put("a/y.jpg", new byte[] { 0xFF, 0xD8 });
                Put("d1/d2/d3/deep.dat", male.Build());
                Put("top.dat", female.Build());
                int visits = 0, withPose = 0;
                var scan = PoseLibrary.Scan(root, (e, bytes, pose) => { visits++; if (pose != null) { withPose++; if (!PoseKit.Same(bytes, pose.Write())) throw new Exception("rewrite"); } });
                t.Check(scan.Problems.Count == 0, "no scan problems");
                t.Equal(5, scan.DatFiles, ".dat files (case-insensitive)");
                t.Equal(2, scan.PngFiles, ".png files (case-insensitive)");
                t.Equal(4, scan.Poses, "poses");
                t.Equal(1, scan.PngPoses, "PNG poses");
                t.Equal(3, scan.DatPoses, ".dat poses");
                t.Equal(1, scan.NotAPose, "PNG without payload = not a pose");
                t.Equal(2, scan.Errors, "errors (bad magic, truncated)");
                t.Equal(2, scan.IgnoredFiles, "other files ignored");
                t.Equal(7, visits, "visitor called for every .dat/.png");
                t.Equal(4, withPose, "visitor gets the parsed pose");
                t.Check(scan.Entries.Select(e => e.RelativePath).SequenceEqual(new[] { "a/plain.png", "a/y.PNG", "b/x.dat", "c/trunc.DAT", "c/z.dat", "d1/d2/d3/deep.dat", "top.dat" }.Where(p => p != "x").OrderBy(p => p, StringComparer.Ordinal)),
                    "entries ordered by relative path with '/' separators: " + string.Join(", ", scan.Entries.Select(e => e.RelativePath)));
                var y = scan.Entries.First(e => e.RelativePath == "a/y.PNG");
                t.Check(y.Kind == PoseContainerKind.Png && y.Status == PoseScanStatus.Pose && y.Sex == 0 && y.Name == "male pose" && y.Version == 101 && !y.HasTrailer, "PNG pose entry (male listed: no sex filter)");
                var plain = scan.Entries.First(e => e.RelativePath == "a/plain.png");
                t.Check(plain.Status == PoseScanStatus.NotAPose && plain.Error.Code == PoseErrorCode.NoPayload, "plain PNG entry");
                t.Check(scan.Entries.First(e => e.RelativePath == "c/z.dat").Error.Code == PoseErrorCode.BadMagic, "bad magic entry");
                t.Check(scan.Entries.First(e => e.RelativePath == "c/trunc.DAT").Error.Code == PoseErrorCode.CountOutOfRange, "truncated entry (cut inside the IK block)");
                t.Check(scan.Entries.First(e => e.RelativePath == "d1/d2/d3/deep.dat").Load().Name == "male pose", "entry.Load() rereads");
                t.Check(scan.Entries.All(e => !e.RelativePath.Contains('\\') && !Path.IsPathRooted(e.RelativePath)), "relative paths only");
                var none = PoseLibrary.Scan(Path.Combine(root, "missing"));
                t.Check(none.Entries.Count == 0 && none.Problems.Count == 1, "missing root → problem, no entries");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static class PoseKeyMapTests
    {
        public const string Vanilla = "illusion.hs2";

        /// <summary>A synthetic Bone list: vanilla-like rows (names) plus a zipmod list.</summary>
        public static List<PoseBoneInfo> SyntheticBones()
        {
            var l = new List<PoseBoneInfo>();
            void Add(int no, string name, int col, int level, string src = Vanilla, int sync = -1) => l.Add(new PoseBoneInfo { No = no, TransformName = name, DisplayName = name, Column = col, Level = level, Source = src, SyncNo = sync });
            Add(0, "cf_J_Hips", 0, 0); Add(1, "cf_J_Head", 10, 0); Add(2, "cf_J_Neck", 10, 0); Add(3, "cf_J_Spine02", 0, 0);
            for (int k = 4; k <= 51; k++) Add(k, "bone_" + k, k < 6 ? 0 : k < 10 ? 1 : k < 14 ? 2 : k < 18 ? 3 : k < 22 ? 4 : k < 37 ? 5 : 6, k >= 22 ? 1 : 0);
            for (int k = 53; k <= 57; k++) Add(k, "cf_J_Mune_L_" + k, 11, 1);
            for (int k = 59; k <= 63; k++) Add(k, "cf_J_Mune_R_" + k, 12, 1);
            Add(65, "cm_J_dan101_00", 0, 0); Add(66, "cm_J_dan109_00", 0, 0);
            Add(67, "cm_J_dan_f_top", 0, 2); Add(68, "cm_J_dan_f_L", 0, 2); Add(69, "cm_J_dan_f_R", 0, 2);
            Add(100, "c_J_hair_FCa_01", 7, 1, sync: 101); Add(101, "c_J_hair_FCb_01", 7, 1, sync: 100);
            Add(400, "cf_J_sk_00_00", 13, 1);
            Add(460, "cf_J_Spine03", 0, 0, "kky.bonusbones"); Add(461, "cf_J_Kosi02", 0, 0, "kky.bonusbones");
            Add(462, "cf_J_Foot02_R", 1, 0, "kky.bonusbones"); Add(463, "cf_J_Foot02_L", 2, 0, "kky.bonusbones");
            Add(3, "cf_J_Spine02_redefined", 0, 0, "later.list");     // last row wins per no
            l.Add(new PoseBoneInfo { No = 52, TransformName = "", Column = 11 }); // empty transform: skipped like Studio
            return l;
        }

        static PoseFile Pose(int sex, bool trailer, IEnumerable<int> keys)
        {
            var f = new PoseFile { Sex = sex };
            foreach (var k in keys) f.FK.Add(new PoseEntry(k));
            if (trailer) f.Trailer = PoseKit.KKEx(PoseKit.ExtendedSaveEntry(new Mp().Map(1)).ToArray());
            return f;
        }

        public static void Run(TestRunner t)
        {
            // families
            var fam = new (int key, PoseKeyFamily f)[]
            {
                (0, PoseKeyFamily.Hs2Vanilla), (51, PoseKeyFamily.Hs2Vanilla), (52, PoseKeyFamily.AiShoujo), (58, PoseKeyFamily.Unknown),
                (64, PoseKeyFamily.Unknown), (69, PoseKeyFamily.Hs2Vanilla), (135, PoseKeyFamily.Hs2Vanilla), (136, PoseKeyFamily.Unknown),
                (311, PoseKeyFamily.Hs2Vanilla), (326, PoseKeyFamily.AiShoujo), (337, PoseKeyFamily.AiShoujo), (350, PoseKeyFamily.Hs2Vanilla),
                (386, PoseKeyFamily.AiShoujo), (399, PoseKeyFamily.AiShoujo), (400, PoseKeyFamily.Hs2Vanilla), (447, PoseKeyFamily.Hs2Vanilla),
                (460, PoseKeyFamily.KkyBonusBones), (463, PoseKeyFamily.KkyBonusBones), (464, PoseKeyFamily.Unknown),
                (900, PoseKeyFamily.AdditionalFkNodes), (901, PoseKeyFamily.AdditionalFkNodes), (902, PoseKeyFamily.Unknown),
                (1001, PoseKeyFamily.AiShoujo), (1005, PoseKeyFamily.AiShoujo), (2001, PoseKeyFamily.AiShoujo), (2005, PoseKeyFamily.AiShoujo),
                (10000, PoseKeyFamily.AdditionalFkNodes), (10019, PoseKeyFamily.AdditionalFkNodes), (11000, PoseKeyFamily.AdditionalFkNodes),
                (11003, PoseKeyFamily.AdditionalFkNodes), (-1, PoseKeyFamily.Unknown),
            };
            t.Check(fam.All(x => PoseKeyMap.FamilyOf(x.key) == x.f), "numeric families: " + string.Join(", ", fam.Where(x => PoseKeyMap.FamilyOf(x.key) != x.f).Select(x => x.key + "→" + PoseKeyMap.FamilyOf(x.key))));
            t.Check(PoseKeyMap.FamilyOf(400, true) == PoseKeyFamily.AiShoujo && PoseKeyMap.FamilyOf(53, true) == PoseKeyFamily.AiShoujo && PoseKeyMap.FamilyOf(51, true) == PoseKeyFamily.Hs2Vanilla, "AI context: only 0–51 coincide");
            int vanillaCount = 0;
            for (int k = -10; k < 20000; k++) if (PoseKeyMap.IsVanillaHs2Key(k)) vanillaCount++;
            t.Equal(271, vanillaCount, "vanilla HS2 key set has 271 numbers (Bone_00/_10/_35/_50)");

            // AI heuristic
            var aiF67 = Enumerable.Range(0, 52).Concat(Enumerable.Range(326, 12)).Concat(new[] { 1001, 1002, 1005 });
            var aiF80 = Enumerable.Range(0, 52).Concat(Enumerable.Range(326, 12)).Concat(Enumerable.Range(386, 16));
            var aiM59 = Enumerable.Range(0, 56).Concat(new[] { 2001, 2002, 2005 });
            var hsF64 = PoseSpec.FemaleVanillaKeys();
            var hsM73 = PoseSpec.FemaleVanillaKeys().Concat(new[] { 67, 68, 69, 460, 461, 462, 463, 900, 901 });
            t.Check(PoseKeyMap.LooksAiNumbered(Pose(1, false, aiF67)) && PoseKeyMap.LooksAiNumbered(Pose(1, false, aiF80)) && PoseKeyMap.LooksAiNumbered(Pose(0, false, aiM59)), "AI rows (F 67, F 80 with 400–401, M 59 with 53–55) look AI-numbered");
            t.Check(!PoseKeyMap.LooksAiNumbered(Pose(1, true, aiF67)), "a trailer means HS2");
            t.Check(!PoseKeyMap.LooksAiNumbered(Pose(1, false, hsF64)) && !PoseKeyMap.LooksAiNumbered(Pose(0, false, hsM73)), "HS2 rows (with all breast keys) are not AI-numbered");
            t.Check(!PoseKeyMap.LooksAiNumbered(Pose(1, false, Enumerable.Range(0, 52))), "no AI keys → not AI-numbered");

            // mapping
            var map = new PoseKeyMap(SyntheticBones());
            t.Equal(74, map.Count, "rows with a transform name, last wins per no");
            t.Check(map.TryGetBone(3, out var b3) && b3.TransformName == "cf_J_Spine02_redefined" && b3.Source == "later.list", "last row wins per no");
            t.Check(!map.TryGetBone(52, out _), "row without a transform name skipped");

            var hs = Pose(1, true, hsM73.Concat(new[] { 5, 777 }));
            var m = map.MapFk(hs, targetSex: 1);
            t.Check(!m.AiNumbered && m.Entries.Count == hs.FK.Count, "HS2 file: every record listed");
            t.Check(m.Entries.Where(e => e.Key <= 66).All(e => e.Applied || e.Index == hs.FK.Count - 2), "vanilla keys applied");
            t.Check(m.Entries.Where(e => e.Key >= 67 && e.Key <= 69).All(e => e.Applied && e.MaleOnlyOnFemale && e.Bone.Level == 2), "level-2 bones on a female target: mapped and marked MaleOnlyOnFemale (value kept)");
            t.Check(map.MapFk(hs, 0).Entries.All(e => !e.MaleOnlyOnFemale) && map.MapFk(hs, 0).Entries.Where(e => e.Key >= 67 && e.Key <= 69).All(e => e.Applied), "level-2 bones on a male target: applied, not marked");
            t.Check(m.Entries.Where(e => e.Key >= 460 && e.Key <= 463).All(e => e.Applied && e.Family == PoseKeyFamily.KkyBonusBones && e.Bone.Source == "kky.bonusbones"), "460–463 resolved through the zipmod rows");
            t.Check(m.Entries.Where(e => e.Key == 900 || e.Key == 901).All(e => e.Skip == PoseKeySkipReason.NotInBoneList && e.Family == PoseKeyFamily.AdditionalFkNodes), "plugin nodes without rows: NotInBoneList");
            var dup = m.Entries[m.Entries.Count - 2];
            t.Check(dup.Key == 5 && dup.Skip == PoseKeySkipReason.DuplicateKey && dup.Bone != null && m.Entries[5].Applied, "duplicate key: the first record is used, the later one skipped");
            var unk = m.Entries.Last();
            t.Check(unk.Key == 777 && unk.Skip == PoseKeySkipReason.NotInBoneList && unk.Family == PoseKeyFamily.Unknown && unk.Reason.Contains("777"), "unknown key: skipped with a reason");
            t.Check(m.Entries.First(e => e.Key == 1).FkGroup == 1 && m.Entries.First(e => e.Key == 30).FkGroup == 4 && m.Entries.First(e => e.Key == 40).FkGroup == 5 && m.Entries.First(e => e.Key == 55).FkGroup == 2 && m.Entries.First(e => e.Key == 8).FkGroup == 3, "FK groups of resolved bones");
            t.Check(m.HipsPos.HasValue && m.HipsPos.Value.Equals(PoseVector3.Zero), "hips position exposed");

            var ai = Pose(0, false, aiM59);
            ai.FK[0].Pos = new PoseVector3(0, -3.5f, 0);
            var am = map.MapFk(ai, 0);
            t.Check(am.AiNumbered && am.Entries.Where(e => e.Key <= 51).All(e => e.Applied && e.Family == PoseKeyFamily.Hs2Vanilla), "AI file: keys 0–51 map like HS2");
            t.Check(am.Entries.Where(e => e.Key >= 53 && e.Key <= 55).All(e => e.Applied && e.Bone.Column == 11 && e.Family == PoseKeyFamily.AiShoujo), "AI male 53–55 land on the HS2 breast rows (Bone list, as HS2_Fix_PoseLoad)");
            t.Check(am.Entries.Where(e => e.Key == 52 || e.Key > 2000).All(e => e.Skip == PoseKeySkipReason.NotInBoneList && e.Family == PoseKeyFamily.AiShoujo && e.Bone == null), "AI male 52 (row without a transform) and 2001–2005 unresolved");
            t.Check(am.AppliedCount == 55 && am.SkippedCount == 4 && am.Applied.Count() == 55 && am.Skipped.Count() == 4, "AI male file counts (55 applied, 4 skipped)");
            t.Check(am.HipsPos.Value.Y == -3.5f && am.HipsOffset == -3.5f && am.GameName == 0, "hips height read from key 0 pos.y (gameName 0: ×1)");

            // strict mode (option): AI files keep 0–51 and the breast remap only
            var strict = new PoseKeyMap(SyntheticBones(), null, new PoseKeyMapOptions { AiSharedKeysOnly = true }).MapFk(ai, 0);
            t.Check(strict.AppliedCount == 52 && strict.Entries.Where(e => e.Key > 51).All(e => e.Skip == PoseKeySkipReason.AiKeyNotImported && e.Bone == null), "AiSharedKeysOnly: AI male 52–55 and 2001–2005 not imported");

            // AI breast remap: file with 326
            var aiF = Pose(1, false, aiF67);
            var rm = map.MapFk(aiF, 1);
            t.Check(rm.AiNumbered && rm.BreastRemap, "AI female file with 326: breast remap on");
            var remapped = rm.Entries.Where(e => e.Remapped).ToList();
            t.Check(remapped.Select(e => e.Key + ">" + e.TargetKey).SequenceEqual(new[] { "326>53", "327>54", "328>55", "329>56", "330>57", "332>59", "333>60", "334>61", "335>62", "336>63" }) && remapped.All(e => e.Applied && e.Bone.Column == (e.TargetKey < 58 ? 11 : 12) && e.Family == PoseKeyFamily.AiShoujo), "326–330 → 53–57, 332–336 → 59–63, applied");
            t.Check(rm.Entries.Where(e => e.Key == 331 || e.Key == 337 || e.Key > 1000).All(e => e.Skip == PoseKeySkipReason.NotInBoneList), "331, 337, 1001.. unresolved");
            t.Check(rm.AppliedCount == 62 && rm.SkippedCount == 5, "AI female 67-key file: 52 + 10 applied, 5 skipped");
            var rmStrict = new PoseKeyMap(SyntheticBones(), null, new PoseKeyMapOptions { AiSharedKeysOnly = true }).MapFk(aiF, 1);
            t.Check(rmStrict.AppliedCount == 62 && rmStrict.Entries.Where(e => e.Key == 331 || e.Key == 337 || e.Key > 1000).All(e => e.Skip == PoseKeySkipReason.AiKeyNotImported), "strict Q16 keeps the breast remap (10 keys) and reports the rest as AI keys");
            var noRemap = new PoseKeyMap(SyntheticBones(), null, new PoseKeyMapOptions { AiBreastRemap = false }).MapFk(aiF, 1);
            t.Check(!noRemap.BreastRemap && noRemap.AppliedCount == 52 && noRemap.Entries.All(e => !e.Remapped), "AiBreastRemap off: 0–51 only");
            t.Check(PoseKeyMap.AiBreastTarget(331) == -1 && PoseKeyMap.AiBreastTarget(337) == -1 && PoseKeyMap.AiBreastTarget(325) == -1 && PoseKeyMap.AiBreastTarget(336) == 63, "AiBreastTarget table");
            var dupRemap = Pose(1, false, aiF67.Concat(new[] { 326 }));
            var dr = map.MapFk(dupRemap);
            t.Check(dr.Entries.Last().Skip == PoseKeySkipReason.DuplicateKey && dr.Entries.First(e => e.Key == 326).Applied, "a second 326: duplicate key, the first is used");
            var both53 = Pose(1, false, Enumerable.Range(0, 52).Concat(new[] { 53, 326 }));
            var sm = map.MapFk(both53);
            t.Check(sm.Entries.First(e => e.Key == 53).Skip == PoseKeySkipReason.Superseded && sm.Entries.Last().Applied && sm.Entries.Last().TargetKey == 53, "53 then 326 (→ 53): the later record wins, the earlier is Superseded");
            var maskPose = Pose(1, true, PoseSpec.FemaleVanillaKeys().Concat(new[] { 400, 100 }));
            var mk = map.MapFk(maskPose);
            t.Check(mk.Entries.Where(e => e.Key == 400 || e.Key == 100).All(e => e.Skip == PoseKeySkipReason.NotInPoseMask && e.Bone != null), "skirt / hair keys in a payload: NotInPoseMask (skirt values come from PoseToolsData)");
            t.Check(new PoseKeyMap(SyntheticBones(), null, new PoseKeyMapOptions { PoseMaskOnly = false }).MapFk(maskPose).Entries.Where(e => e.Key == 400 || e.Key == 100).All(e => e.Applied), "PoseMaskOnly off: applied");

            // node tables
            string table = "10000\tcf_J_Toes_Hallux1_R\tright big toe\t5\t1\textra\r\n10010\tcf_J_Toes_Hallux1_L\tleft\t6\t1\n\nshort\tline\n11000\tcm_J_dan103_00\t\t0\t0\r\n460\tnot_used\t\t0\t0\n";
            var rows = PoseKeyMap.ParseNodeTable(table, "body.test", out string prob);
            t.Check(prob == null && rows.Count == 4 && rows.All(r => r.IsNode && r.SyncNo == -1 && r.Source == "body.test") && rows[0].TransformName == "cf_J_Toes_Hallux1_R" && rows[0].Column == 5 && rows[0].Level == 1, "node table parsed (CRLF/LF, short lines and extra cells ignored)");
            t.Check(PoseKeyMap.ParseNodeTable("p_cf_body_00\tx\ty\t0\t0\n10000\ta\tb\t5\t1", "x", out string prob2) == null && prob2 != null, "a non-integer key rejects the whole table");
            t.Check(PoseKeyMap.ParseNodeTable("10000\ta\tb\tfive\t1", "x", out _) == null && PoseKeyMap.ParseNodeTable("", "x", out _).Count == 0, "non-integer group rejects; empty text gives no rows");
            var later = PoseKeyMap.ParseNodeTable("10000\tother_name\t\t5\t1", "later.table", out _);
            var nmap = new PoseKeyMap(SyntheticBones(), rows.Concat(later));
            nmap.TryGetBone(10000, out var toe);
            t.Check(nmap.NodeCount == 4 && toe != null && toe.TransformName == "cf_J_Toes_Hallux1_R", "node rows: first table wins");
            t.Check(nmap.TryGetBone(460, out var k460) && k460.TransformName == "cf_J_Spine03" && !k460.IsNode, "a node row never replaces a Bone-list row");
            t.Check(toe.FkGroup == 4 && toe.BoneGroupFlags == PoseKeyMap.GroupRightHand && nmap.Nodes.First(n => n.No == 11000).BoneGroupFlags == PoseKeyMap.GroupBody, "node groups: toes in the hand groups, column 0–4 → Body without limb bits");
            var withNodes = nmap.MapFk(hs, 0);
            t.Check(withNodes.Entries.Where(e => e.Key == 900 || e.Key == 901).All(e => e.Skip == PoseKeySkipReason.NotInBoneList), "900–901 stay unresolved without a table");
            var toeFile = Pose(1, true, PoseSpec.FemaleVanillaKeys().Concat(new[] { 10000, 10010, 11000, 10005 }));
            var tm = nmap.MapFk(toeFile, 1);
            t.Check(tm.Entries.Where(e => e.Key == 10000 || e.Key == 10010 || e.Key == 11000).All(e => e.Applied && e.Bone.IsNode && e.Family == PoseKeyFamily.AdditionalFkNodes) && tm.Entries.Last().Skip == PoseKeySkipReason.NotInBoneList, "node keys resolved through the tables, a key without a row skipped");

            // hips offset scale and ignored values
            var kk = Pose(1, false, PoseSpec.FemaleVanillaKeys());
            kk.FK[0].Pos = new PoseVector3(0, 0.5f, 0);
            kk.FK[6].Pos = new PoseVector3(1, 0, 0);
            kk.FK[7].Scale = new PoseVector3(1, 2, 1);
            kk.Trailer = PoseKit.KKEx(PoseKit.ExtendedSaveEntry(new Mp().Map(1), game: 1).ToArray());
            var km = map.MapFk(kk);
            t.Check(km.GameName == 1 && km.HipsOffset == 5f && km.IgnoredPos == 1 && km.IgnoredScale == 1, "gameName 1 (Koikatsu): hips offset ×10; other pos / scale counted as ignored");
            var factors = new List<string>();
            foreach (int g in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            {
                kk.Trailer = PoseKit.KKEx(PoseKit.ExtendedSaveEntry(new Mp().Map(1), game: g).ToArray());
                factors.Add(g + ":" + (map.MapFk(kk).HipsOffset / 0.5f).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            t.Equal("0:1 1:10 2:10 3:1 4:1 5:10 6:10 7:1", string.Join(" ", factors), "hips offset factor per gameName (×10 for 1, 2, 5, 6)");

            // group tables
            var fkg = Enumerable.Range(-1, 16).Select(PoseKeyMap.FkGroupOfColumn).ToArray();
            t.Check(fkg.SequenceEqual(new[] { -1, 3, 3, 3, 3, 3, 4, 5, 0, 0, 0, 1, 2, 2, 6, -1 }), "group column → ActiveFK index: " + string.Join(",", fkg));
            var flags = Enumerable.Range(0, 15).Select(PoseKeyMap.BoneGroupFlagsOfColumn).ToArray();
            t.Check(flags.SequenceEqual(new[] { 1, 3, 5, 9, 17, 32, 64, 128, 128, 128, 256, 512, 512, 1024, 1 << 14 }), "group column → BoneGroup flags: " + string.Join(",", flags));
            int mask = PoseKeyMap.PoseFileGroupMask;
            t.Check(Enumerable.Range(0, 14).Where(c => (PoseKeyMap.BoneGroupFlagsOfColumn(c) & mask) == 0).SequenceEqual(new[] { 7, 8, 9, 13 }), "mask 895 excludes exactly hair and skirt columns");
            t.Check(PoseKeyMap.IkTargetName(0) == "Body" && PoseKeyMap.IkTargetName(12) == "RightFoot" && PoseKeyMap.IkTargetName(13) == null, "IK target names");
            t.Check(PoseFile.FkGroupNames.Length == 7 && PoseFile.IkGroupNames.Length == 5 && PoseFile.IkTargetNames.Length == 13, "group name tables");
        }
    }
}
