// Pose library corpus test: scans an installed pose library
// with PoseLibrary, checks that every payload parses and is rewritten byte for byte (and every KKEx trailer
// re-encodes identically), measures the census and compares it with the
// checked-in baseline Tests/Pose/corpus_census.json (the spec's numbers; aggregate numbers only, no file names).
// With a PoseKeyMap (Unity: the Studio list DB's Bone lists) it also reports the key coverage per family.
// The measured census is written as JSON next to the report. Unity-free; the plain-Mono group PoseCorpusGroup runs
// it when HS2N_POSE_ROOT is set (Tools/mono_tests.ps1 -Suite pose -PoseRoot <dir>).
namespace HS2Next.Import.Pose.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using HS2Next.Import.Pose;
    using HS2Next.Save.Tests;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>Plain-Mono entry: runs the corpus test when HS2N_POSE_ROOT names a pose folder (else skipped).</summary>
    public static class PoseCorpusGroup
    {
        public static void Run(TestRunner t)
        {
            string root = Environment.GetEnvironmentVariable("HS2N_POSE_ROOT");
            if (string.IsNullOrEmpty(root)) { t.Info("skipped: no pose root (Tools/mono_tests.ps1 -Suite pose -PoseRoot <dir>)"); return; }
            PoseCorpus.Run(t, root, null, null, Environment.GetEnvironmentVariable("HS2N_POSE_OUT"));
        }
    }

    public static class PoseCorpus
    {
        public const string BaselineFile = "Pose/corpus_census.json";
        public const string MeasuredFile = "corpus_census_measured.json";

        static string Ranges(IEnumerable<int> keys)
        {
            var k = keys.Distinct().OrderBy(x => x).ToList();
            if (k.Count == 0) return "-";
            var parts = new List<string>();
            int s = k[0], p = k[0];
            foreach (var x in k.Skip(1))
            {
                if (x == p + 1) { p = x; continue; }
                parts.Add(s == p ? s.ToString(CultureInfo.InvariantCulture) : s + "-" + p);
                s = p = x;
            }
            parts.Add(s == p ? s.ToString(CultureInfo.InvariantCulture) : s + "-" + p);
            return string.Join(",", parts);
        }

        sealed class Counter : Dictionary<string, int>
        {
            public void Add(string k) { TryGetValue(k, out int n); this[k] = n + 1; }
            public JObject Json(IEnumerable<string> order = null)
            {
                var o = new JObject();
                foreach (var k in order ?? Keys.OrderBy(x => x, StringComparer.Ordinal)) o[k] = TryGetValue(k, out int n) ? n : 0;
                return o;
            }
        }

        sealed class FamilyStats
        {
            public int Files, Keys, Applied, Remapped;
            public readonly Counter Skips = new Counter();
            public readonly SortedSet<int> SkippedKeys = new SortedSet<int>();
            public readonly SortedSet<int> AppliedPlain = new SortedSet<int>();
        }

        /// <summary>
        /// Runs the corpus test. <paramref name="map"/> null = no key coverage; <paramref name="vanillaSource"/> = the
        /// Source string of vanilla Bone rows (for the Bone-list cross-check); <paramref name="outDir"/> receives the
        /// measured census JSON (null = not written).
        /// </summary>
        public static void Run(TestRunner t, string root, PoseKeyMap map, string vanillaSource, string outDir)
        {
            var sw = Stopwatch.StartNew();
            var m = new JObject();
            int payloads = 0, rt = 0, encodeOk = 0, pngPayloads = 0;
            var rtFail = new List<string>();
            var version = new Counter(); var sex = new Counter(); var ikfk = new Counter(); var activeIk = new Counter();
            var activeFkAll = new Counter(); var anime = new Counter(); var families = new Counter(); var sizes = new Counter();
            var errorCodes = new Counter(); var flags = new Counter();
            int ikStd = 0, ikScaleOne = 0, exprAll = 0, hairOn = 0, skirtOn = 0;
            long fkEntries = 0; int nan = 0, scaleNotOne = 0, posNonZero = 0, hipsFiles = 0, hipsXZ = 0;
            float rotMin = float.MaxValue, rotMax = float.MinValue, hipsMin = float.MaxValue, hipsMax = float.MinValue;
            var ikRotFiles = new int[13];
            int trailers = 0, kkex3 = 0, game4 = 0, poseTools = 0, skirt = 0, skirtFull = 0, joint8 = 0, notKKEx = 0, malformed = 0, ptProblems = 0;
            var guids = new Counter();
            var hashes = new List<string>();
            var fam = new Dictionary<PoseKeyFamily, FamilyStats>();
            foreach (PoseKeyFamily f in Enum.GetValues(typeof(PoseKeyFamily))) fam[f] = new FamilyStats();
            int aiFiles = 0, aiSharedNotApplied = 0, remapFiles = 0;
            // plugin data
            int aiNumbered = 0, key326 = 0, key326Male = 0, key1001 = 0, key1003 = 0, key386 = 0, key52 = 0, kkyFiles = 0;
            int hTrailer = 0, hNoTrailer = 0, hF = 0, hM = 0, hAi = 0, hRec000 = 0, hApplied = 0;
            var hBins = new Counter();
            int nodeFiles = 0, toeFiles = 0, toeMale = 0, danFiles = 0, danF = 0, danM = 0, k900Files = 0, k900M = 0, k900F = 0, dan11001Rot = 0;
            var toeRot = new int[20];
            int both = 0, bothBodyOff = 0, bothAllIk = 0, bothTrailer = 0, bothF = 0, bothM = 0;
            var brow = new Counter(); var eyes = new Counter(); var mouth = new Counter();
            int eyeOne = 0, eyeZero = 0, mouthZero = 0, skirtDataOn = 0, ptFkMode = 0, skirtNoData = 0, skirtNoDataFk = 0;
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            var sha = SHA256.Create();

            var scan = PoseLibrary.Scan(root, (e, bytes, p) =>
            {
                if (bytes != null) hashes.Add(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""));
                if (p == null)
                {
                    if (e.Status == PoseScanStatus.Error) errorCodes.Add(e.Error?.Code.ToString() ?? "?");
                    return;
                }
                payloads++;
                if (p.Kind == PoseContainerKind.Png) pngPayloads++;
                var w = p.Write();
                if (PoseKit.Same(bytes, w)) rt++;
                else if (rtFail.Count < 10) rtFail.Add($"payload #{payloads}: first difference at {PoseKit.Diff(bytes, w)}");
                flags.Add(p.Flags.ToString());
                version.Add(p.Version.ToString(CultureInfo.InvariantCulture));
                sex.Add(p.Sex == 1 ? "female" : p.Sex == 0 ? "male" : "other" + p.Sex);
                if (p.IK.Count == 13 && p.IK.Select(x => x.Key).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 13))) ikStd++;
                if (p.IK.All(x => x.GetBits(6) == 0x3F800000u && x.GetBits(7) == 0x3F800000u && x.GetBits(8) == 0x3F800000u)) ikScaleOne++;
                ikfk.Add((p.EnableIK ? "true" : "false") + "," + (p.EnableFK ? "true" : "false"));
                activeIk.Add(p.ActiveIK.All(b => b) ? "allOn" : p.ActiveIK.All(b => !b) ? "allOff" : "other");
                activeFkAll.Add(string.Join("+", Enumerable.Range(0, 7).Where(i => p.ActiveFK[i]).Select(i => PoseFile.FkGroupNames[i])));
                if (p.ActiveFK[0]) hairOn++;
                if (p.ActiveFK[6]) skirtOn++;
                if (p.Expression.All(b => b)) exprAll++;
                anime.Add(p.AnimeGroup + "/" + p.AnimeCategory + "/" + p.AnimeNo);

                // FK families
                string trailerKind = p.Trailer == null ? "none" : p.Ext.Status == KKExStatus.Ok ? "KKEx" : "other";
                var extra = p.FK.Select(x => x.Key).Where(k => !PoseKeyMap.IsVanillaHs2Key(k));
                families.Add($"{(p.Sex == 1 ? "F" : p.Sex == 0 ? "M" : "?")} {trailerKind} {p.FK.Count} {Ranges(extra)}");

                // values
                fkEntries += p.FK.Count;
                foreach (var x in p.FK)
                {
                    bool n = false;
                    for (int k = 0; k < 9; k++) n |= FloatBits.IsNaN(x.GetBits(k));
                    if (n) { nan++; continue; }
                    var r = x.Rot;
                    rotMin = Math.Min(rotMin, Math.Min(r.X, Math.Min(r.Y, r.Z)));
                    rotMax = Math.Max(rotMax, Math.Max(r.X, Math.Max(r.Y, r.Z)));
                    if (!x.Scale.Equals(PoseVector3.One)) scaleNotOne++;
                    var pos = x.Pos;
                    if (pos.X != 0 || pos.Y != 0 || pos.Z != 0) posNonZero++;
                }
                var hips = p.FindFk(0);
                if (hips != null)
                {
                    var hp = hips.Pos;
                    if (hp.X != 0 || hp.Y != 0 || hp.Z != 0)
                    {
                        hipsFiles++;
                        if (hp.X != 0 || hp.Z != 0) hipsXZ++;
                        hipsMin = Math.Min(hipsMin, hp.Y); hipsMax = Math.Max(hipsMax, hp.Y);
                    }
                }
                foreach (var x in p.IK)
                    if (x.Key >= 0 && x.Key < 13 && (x.Rot.X != 0 || x.Rot.Y != 0 || x.Rot.Z != 0)) ikRotFiles[x.Key]++;

                // trailer
                if (p.Trailer != null)
                {
                    trailers++;
                    sizes.Add(p.Trailer.Length.ToString(CultureInfo.InvariantCulture));
                    var x = p.Ext;
                    if (x.Status == KKExStatus.NotKKEx) notKKEx++;
                    if (x.Status == KKExStatus.Malformed) malformed++;
                    if (x.Status == KKExStatus.Ok && x.Version == 3) kkex3++;
                    if (PoseKit.Same(x.Encode(), p.Trailer)) encodeOk++;
                    foreach (var en in x.Entries) guids.Add(en.Guid);
                    if (p.GameName == 4) game4++;
                    var pt = p.PoseTools;
                    if (pt != null)
                    {
                        poseTools++;
                        if (pt.Problems.Count > 0 || pt.UnknownFields.Count > 0) ptProblems++;
                        if (pt.JointCorrection != null && pt.JointCorrection.Length == 8) joint8++;
                        if (pt.SkirtFK != null)
                        {
                            skirt++;
                            if (pt.SkirtFK.Select(s => s.Key).SequenceEqual(Enumerable.Range(400, 48))) skirtFull++;
                        }
                    }
                }

                // plugin data
                distinct.Add(BitConverter.ToString(sha.ComputeHash(bytes, p.PayloadOffset, bytes.Length - p.PayloadOffset)));
                var keys = new HashSet<int>(p.FK.Select(x => x.Key));
                bool ai = PoseKeyMap.LooksAiNumbered(p);
                if (ai) aiNumbered++;
                if (keys.Contains(326)) { key326++; if (p.Sex == 0) key326Male++; }
                if (keys.Contains(1001)) key1001++;
                if (keys.Contains(1003)) key1003++;
                if (keys.Any(k => k >= 386 && k <= 399)) key386++;
                if (keys.Contains(52) || keys.Any(k => k >= 2001 && k <= 2005)) key52++;
                if (keys.Any(k => k >= 460 && k <= 463)) kkyFiles++;
                float hy = hips != null ? hips.Pos.Y : 0;
                if (hy != 0)
                {
                    if (p.Trailer != null) hTrailer++; else hNoTrailer++;
                    if (p.Sex == 1) hF++; else hM++;
                    if (ai) hAi++;
                    if (p.AnimeGroup == 0 && p.AnimeCategory == 0 && p.AnimeNo == 0) hRec000++;
                    if (p.EnableFK && p.ActiveFK[3]) hApplied++;
                    hBins.Add(hy <= -11.435f ? "<=-11.435" : hy <= -8 ? "(-11.435,-8]" : hy <= -4 ? "(-8,-4]" : hy <= -1 ? "(-4,-1]" : hy < 0 ? "(-1,0)" : hy <= 1 ? "(0,1]" : ">1");
                }
                bool toe = keys.Any(k => k >= 10000 && k <= 10019), dan = keys.Any(k => k >= 11000 && k <= 11003), k900 = keys.Contains(900) || keys.Contains(901);
                if (toe || dan || k900 || keys.Any(k => k >= 12000 && k <= 12025)) nodeFiles++;
                if (toe) { toeFiles++; if (p.Sex == 0) toeMale++; }
                if (dan) { danFiles++; if (p.Sex == 1) danF++; else danM++; }
                if (k900) { k900Files++; if (p.Sex == 1) k900F++; else k900M++; }
                foreach (var x in p.FK)
                {
                    bool rotNz = x.Rot.X != 0 || x.Rot.Y != 0 || x.Rot.Z != 0;
                    if (rotNz && x.Key >= 10000 && x.Key <= 10019) toeRot[x.Key - 10000]++;
                    if (rotNz && x.Key == 11001) dan11001Rot++;
                }
                if (p.EnableIK && p.EnableFK)
                {
                    both++;
                    if (!p.ActiveFK[3]) bothBodyOff++;
                    if (p.ActiveIK.All(b => b)) bothAllIk++;
                    if (p.Trailer != null) bothTrailer++;
                    if (p.Sex == 1) bothF++; else bothM++;
                }
                var ptv = p.PoseTools;
                if (ptv != null)
                {
                    brow.Add(ptv.EyebrowPattern?.ToString(CultureInfo.InvariantCulture) ?? "-");
                    eyes.Add(ptv.EyesPattern?.ToString(CultureInfo.InvariantCulture) ?? "-");
                    mouth.Add(ptv.MouthPattern?.ToString(CultureInfo.InvariantCulture) ?? "-");
                    if (ptv.EyeOpen == 1f) eyeOne++;
                    if (ptv.EyeOpen == 0f) eyeZero++;
                    if (ptv.MouthOpen == 0f) mouthZero++;
                    if (ptv.SkirtFK != null && p.ActiveFK[6]) skirtDataOn++;
                    if (p.EnableFK && !p.EnableIK) ptFkMode++;
                }
                if (p.ActiveFK[6] && ptv?.SkirtFK == null) { skirtNoData++; if (p.EnableFK && !p.EnableIK) skirtNoDataFk++; }

                // key coverage
                if (map != null)
                {
                    var fm = map.MapFk(p, p.Sex);
                    if (fm.AiNumbered) aiFiles++;
                    if (fm.Entries.Any(x => x.Remapped && x.Applied)) remapFiles++;
                    foreach (var g in fm.Entries.GroupBy(x => x.Family))
                    {
                        var st = fam[g.Key];
                        st.Files++;
                        foreach (var x in g)
                        {
                            st.Keys++;
                            if (x.Applied) { st.Applied++; if (x.Remapped) st.Remapped++; else st.AppliedPlain.Add(x.Key); }
                            else { st.Skips.Add(x.Skip.ToString()); st.SkippedKeys.Add(x.Key); }
                            if (fm.AiNumbered && x.Key <= PoseKeyMap.AiSharedLast && !x.Applied) aiSharedNotApplied++;
                        }
                    }
                }
            });
            sha.Dispose();
            long ms = sw.ElapsedMilliseconds;

            // ---------------------------------------------------------------- measured census
            var otherRecords = anime.Keys.Where(k => k != "0/0/0" && k != "0/2/11").ToList();
            string[] fkTop = { "Neck+Body+RightHand+LeftHand", "Neck+Body", "Neck+Breast+Body+RightHand+LeftHand", "Neck" };
            var activeFk = new JObject();
            foreach (var k in fkTop) activeFk[k] = activeFkAll.TryGetValue(k, out int n) ? n : 0;
            activeFk["other"] = payloads - fkTop.Sum(k => activeFkAll.TryGetValue(k, out int n) ? n : 0);
            activeFk["hairOn"] = hairOn;
            activeFk["skirtOn"] = skirtOn;
            m["files"] = new JObject
            {
                ["dat"] = scan.DatFiles, ["png"] = scan.PngFiles, ["pngWithPayload"] = pngPayloads, ["pngWithoutPayload"] = scan.NotAPose,
                ["payloads"] = payloads, ["errors"] = scan.Errors, ["roundTripIdentical"] = rt, ["otherFilesIgnored"] = scan.IgnoredFiles,
            };
            m["version"] = version.Json();
            m["sex"] = sex.Json();
            m["ik"] = new JObject { ["count13Keys0to12"] = ikStd, ["scaleOne"] = ikScaleOne };
            m["enableIkFk"] = ikfk.Json();
            m["activeIk"] = activeIk.Json();
            m["activeFk"] = activeFk;
            m["expression"] = new JObject { ["allTrue"] = exprAll };
            m["anime"] = new JObject
            {
                ["0/0/0"] = anime.TryGetValue("0/0/0", out int a0) ? a0 : 0, ["0/2/11"] = anime.TryGetValue("0/2/11", out int a1) ? a1 : 0,
                ["otherFiles"] = otherRecords.Sum(k => anime[k]), ["otherRecords"] = otherRecords.Count,
            };
            m["fkFamilies"] = families.Json();
            m["fkValues"] = new JObject
            {
                ["entries"] = fkEntries, ["nan"] = nan, ["rotMin"] = Math.Round(rotMin, 4), ["rotMax"] = Math.Round(rotMax, 4), ["scaleNotOne"] = scaleNotOne,
                ["posNonZeroEntries"] = posNonZero, ["hipsPosFiles"] = hipsFiles, ["hipsPosXZNonZero"] = hipsXZ,
                ["hipsYMin"] = hipsFiles > 0 ? Math.Round(hipsMin, 3) : 0, ["hipsYMax"] = hipsFiles > 0 ? Math.Round(hipsMax, 3) : 0,
            };
            var other = new[] { 0, 1, 2, 4, 5, 7, 8, 10, 11 };
            m["ikRot"] = new JObject
            {
                ["filesWithRotOnOtherKeysAtMost"] = other.Max(k => ikRotFiles[k]),
                ["filesWithRotPerKey"] = new JArray(ikRotFiles),
            };
            m["trailer"] = new JObject
            {
                ["files"] = trailers, ["kkexVersion3"] = kkex3, ["gameName4"] = game4, ["sizes"] = sizes.Json(), ["poseToolsData"] = poseTools,
                ["jointCorrection8"] = joint8, ["skirtFkData"] = skirt, ["skirtKeys400to447"] = skirtFull, ["encodeIdentical"] = encodeOk,
                ["notKKEx"] = notKKEx, ["malformed"] = malformed, ["poseToolsProblems"] = ptProblems, ["guids"] = guids.Json(),
            };
            m["reader"] = new JObject
            {
                ["flagged"] = payloads - (flags.TryGetValue("None", out int nf) ? nf : 0),
                ["payloadFoundByScan"] = flags.Where(kv => kv.Key.Contains(nameof(PoseFileFlags.PayloadFoundByScan))).Sum(kv => kv.Value),
                ["errorCodes"] = errorCodes.Json(), ["flags"] = flags.Json(),
            };
            m["plugins"] = new JObject
            {
                ["aiNumbered"] = aiNumbered, ["key326Files"] = key326, ["key326Male"] = key326Male, ["key1001Files"] = key1001, ["key1003Files"] = key1003,
                ["key386to399Files"] = key386, ["key52Or2001Files"] = key52, ["kkyFiles"] = kkyFiles,
                ["hips"] = new JObject
                {
                    ["files"] = hipsFiles, ["fkModeBodyOn"] = hApplied, ["withTrailer"] = hTrailer, ["withoutTrailer"] = hNoTrailer, ["female"] = hF, ["male"] = hM,
                    ["inAiFiles"] = hAi, ["record000"] = hRec000, ["yMin"] = hipsFiles > 0 ? Math.Round(hipsMin, 3) : 0, ["yMax"] = hipsFiles > 0 ? Math.Round(hipsMax, 3) : 0,
                    ["bins"] = hBins.Json(),
                },
                ["nodes"] = new JObject
                {
                    ["files"] = nodeFiles, ["toeFiles"] = toeFiles, ["toeFilesMale"] = toeMale, ["danFiles"] = danFiles, ["danFemale"] = danF, ["danMale"] = danM,
                    ["key900Files"] = k900Files, ["key900Male"] = k900M, ["key900Female"] = k900F, ["toeRotFilesPerKeyAtMost"] = toeRot.Max(), ["dan11001RotFiles"] = dan11001Rot,
                },
                ["bothModes"] = new JObject { ["files"] = both, ["bodyFkOff"] = bothBodyOff, ["allIkOn"] = bothAllIk, ["withTrailer"] = bothTrailer, ["female"] = bothF, ["male"] = bothM },
                ["poseTools"] = new JObject
                {
                    ["eyebrow"] = brow.Json(), ["eyes"] = eyes.Json(), ["mouth"] = mouth.Json(), ["eyeOpenOne"] = eyeOne, ["eyeOpenZero"] = eyeZero,
                    ["mouthOpenZero"] = mouthZero, ["skirtDataWithSkirtOn"] = skirtDataOn, ["fkMode"] = ptFkMode,
                },
                ["skirtOnWithoutData"] = skirtNoData, ["skirtOnWithoutDataFkMode"] = skirtNoDataFk,
                ["distinctPayloads"] = distinct.Count,
            };
            m["activeFkAll"] = activeFkAll.Json();
            hashes.Sort(StringComparer.Ordinal);
            using (var h = SHA256.Create())
                m["_digest"] = BitConverter.ToString(h.ComputeHash(Encoding.ASCII.GetBytes(string.Join("\n", hashes)))).Replace("-", "").ToLowerInvariant();
            m["_note"] = "measured by PoseFileTest; aggregate numbers only; _digest = SHA-256 over the sorted per-file SHA-256 of every .dat/.png (order- and name-free)";

            t.Info($"scanned {scan.DatFiles} .dat + {scan.PngFiles} .png ({scan.IgnoredFiles} other files ignored, {scan.Directories} folders) in {ms} ms: " +
                   $"{payloads} payloads ({pngPayloads} in PNG), {scan.NotAPose} PNGs without payload, {scan.Errors} errors");
            foreach (var pr in scan.Problems) t.Info("scan: " + pr);

            // ---------------------------------------------------------------- hard checks (independent of the baseline)
            t.Check(scan.Problems.Count == 0, "scan without directory problems");
            t.Check(payloads > 0, "the library has payloads");
            t.Check(scan.Errors == 0, "every .dat and PNG payload parses (errors " + scan.Errors + (errorCodes.Count > 0 ? ": " + string.Join(", ", errorCodes.Select(kv => kv.Key + " " + kv.Value)) : "") + ")");
            t.Check(rt == payloads, $"Write is byte-identical for every payload ({rt} of {payloads})" + (rtFail.Count > 0 ? "; " + string.Join("; ", rtFail) : ""));
            t.Check(encodeOk == trailers, $"KKExTrailer.Encode reproduces every trailer ({encodeOk} of {trailers})");

            // ---------------------------------------------------------------- baseline
            string baselinePath = Path.Combine(t.FixturesDir ?? "", BaselineFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(baselinePath)) t.Check(false, "baseline " + BaselineFile + " not found under the fixtures folder");
            else
            {
                var b = JObject.Parse(File.ReadAllText(baselinePath, Encoding.UTF8));
                int before = t.Failed;
                Compare(t, b, m, "census");
                t.Info(t.Failed == before ? "census equals the baseline (" + BaselineFile + ")" : "census differs from the baseline in " + (t.Failed - before) + " values (the install differs from the spec's reference census, or a reader defect)");
                if (map != null) Coverage(t, map, vanillaSource, fam, aiFiles, aiSharedNotApplied, remapFiles, b);
            }
            if (map != null && !File.Exists(baselinePath)) Coverage(t, map, vanillaSource, fam, aiFiles, aiSharedNotApplied, remapFiles, null);

            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, MeasuredFile), m.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
                t.Info("measured census written: " + MeasuredFile);
            }
        }

        /// <summary>Compares the baseline with the measurement. Objects with "_closed": true also fail on measured keys absent from the baseline. Keys ending in "AtMost": measured ≤ expected. Numbers with a fraction: ± half a unit of the last decimal.</summary>
        static void Compare(TestRunner t, JToken e, JToken m, string path)
        {
            if (e is JObject eo)
            {
                var mo = m as JObject;
                foreach (var p in eo.Properties())
                {
                    if (p.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    Compare(t, p.Value, mo?[p.Name], path + "." + p.Name);
                }
                if (mo != null && eo["_closed"]?.Value<bool>() == true)
                    foreach (var p in mo.Properties())
                        if (eo[p.Name] == null && !p.Name.StartsWith("_", StringComparison.Ordinal))
                            t.Check(false, $"{path}.{p.Name}: measured {p.Value.ToString(Formatting.None)}, absent from the baseline");
                return;
            }
            if (m == null || m.Type == JTokenType.Null) { t.Check(false, path + ": not measured"); return; }
            if (e.Type == JTokenType.Integer || e.Type == JTokenType.Float)
            {
                double ev = e.Value<double>(), mv = m.Value<double>();
                bool ok;
                if (path.EndsWith("AtMost", StringComparison.Ordinal)) ok = mv <= ev;
                else if (e.Type == JTokenType.Float)
                {
                    string s = e.ToString(Formatting.None);
                    int dec = s.Contains('.') ? s.Length - s.IndexOf('.') - 1 : 0;
                    ok = Math.Abs(ev - mv) <= 0.5 * Math.Pow(10, -dec) + 1e-9;
                }
                else ok = ev == mv;
                t.Check(ok, $"{path}: baseline {e.ToString(Formatting.None)}, measured {m.ToString(Formatting.None)}");
                return;
            }
            t.Check(JToken.DeepEquals(e, m), $"{path}: baseline {e.ToString(Formatting.None)}, measured {m.ToString(Formatting.None)}");
        }

        static void Coverage(TestRunner t, PoseKeyMap map, string vanillaSource, Dictionary<PoseKeyFamily, FamilyStats> fam, int aiFiles, int aiSharedNotApplied, int remapFiles, JObject baseline)
        {
            // the Bone list itself
            if (vanillaSource != null)
            {
                var vanilla = map.Bones.Where(b => string.Equals(b.Source, vanillaSource, StringComparison.OrdinalIgnoreCase)).Select(b => b.No).ToList();
                var expected = Enumerable.Range(-1, 20001).Where(PoseKeyMap.IsVanillaHs2Key).ToList();
                t.Check(vanilla.SequenceEqual(expected), $"Bone list: vanilla rows = the vanilla key set of PoseKeyMap ({vanilla.Count} rows, expected {expected.Count})"
                    + (vanilla.SequenceEqual(expected) ? "" : "; only in the list: " + Ranges(vanilla.Except(expected)) + "; only in PoseKeyMap: " + Ranges(expected.Except(vanilla))));
            }
            string[] kky = { "cf_J_Spine03", "cf_J_Kosi02", "cf_J_Foot02_R", "cf_J_Foot02_L" };
            bool kkyOk = Enumerable.Range(0, 4).All(i => map.TryGetBone(460 + i, out var b) && b.TransformName == kky[i] && !string.Equals(b.Source, vanillaSource, StringComparison.OrdinalIgnoreCase));
            t.Check(kkyOk, "Bone list: 460–463 come from a zipmod Bone list (cf_J_Spine03, cf_J_Kosi02, cf_J_Foot02_R, cf_J_Foot02_L)");
            t.Info($"Bone list: {map.Count} rows ({map.Bones.Count(b => string.Equals(b.Source, vanillaSource, StringComparison.OrdinalIgnoreCase))} vanilla, sources: {string.Join(", ", map.Bones.GroupBy(b => string.Equals(b.Source, vanillaSource, StringComparison.OrdinalIgnoreCase) ? "vanilla" : "mod").Select(g => g.Key + " " + g.Count()))})");

            t.Info("key coverage per family (file-context families; files / key occurrences / applied / skipped by reason / skipped keys):");
            foreach (var kv in fam)
            {
                var s = kv.Value;
                if (s.Files == 0) { t.Info($"  {kv.Key,-18} files 0"); continue; }
                t.Info($"  {kv.Key,-18} files {s.Files}, keys {s.Keys}, applied {s.Applied}{(s.Remapped > 0 ? " (" + s.Remapped + " by the AI breast remap)" : "")}, skipped {s.Keys - s.Applied}" +
                       (s.Skips.Count > 0 ? " (" + string.Join(", ", s.Skips.OrderBy(x => x.Key).Select(x => x.Key + " " + x.Value)) + ")" : "") +
                       (s.SkippedKeys.Count > 0 ? "; skipped keys " + Ranges(s.SkippedKeys) : ""));
            }
            var v = fam[PoseKeyFamily.Hs2Vanilla];
            t.Check(v.Files > 0 && v.Applied == v.Keys, $"Hs2Vanilla family: every key applied ({v.Applied} of {v.Keys})" +
                (v.Skips.Count > 0 ? "; skipped " + string.Join(", ", v.Skips.Select(x => x.Key + " " + x.Value)) + ", keys " + Ranges(v.SkippedKeys) : ""));
            var k4 = fam[PoseKeyFamily.KkyBonusBones];
            t.Check(k4.Files > 0 && k4.Applied == k4.Keys, $"KkyBonusBones family: every key resolved through the zipmod Bone list ({k4.Applied} of {k4.Keys} in {k4.Files} files)");
            t.Check(fam[PoseKeyFamily.Unknown].Keys == 0, "no keys outside the known families (" + fam[PoseKeyFamily.Unknown].Keys + ")");
            t.Check(aiSharedNotApplied == 0, "AI-numbered files: every key 0–51 applied (" + aiSharedNotApplied + " not)");
            var ai = fam[PoseKeyFamily.AiShoujo];
            t.Info($"AiShoujo keys applied without the remap (through the Bone list): {(ai.AppliedPlain.Count > 0 ? Ranges(ai.AppliedPlain) : "none")}");
            if (map.Options.AiBreastRemap)
            {
                int want = baseline?["plugins"]?["key326Files"]?.Value<int>() ?? -1;
                t.Check(want < 0 || remapFiles == want, $"breast remap applied in every file with key 326 ({remapFiles} files, baseline {want})");
                t.Check(ai.Remapped > 0 && ai.Remapped % 10 == 0, $"10 remapped breast keys per file ({ai.Remapped})");
            }
            if (!map.Options.AiSharedKeysOnly && map.Options.AiBreastRemap && map.Options.PoseMaskOnly)
            {
                // unresolved AI keys 331, 337, 386–399, 1001–1005, 52 / 2001 / 2002 / 2005;
                // payload skirt keys 400–401 of the 80-key files not applied; AI male 53–55 through the Bone list
                t.Check(Ranges(ai.SkippedKeys) == "52,331,337,386-401,1001-1005,2001-2002,2005", "AiShoujo keys left unresolved: " + Ranges(ai.SkippedKeys) + " (expected 52,331,337,386-401,1001-1005,2001-2002,2005)");
                t.Check(Ranges(ai.AppliedPlain) == "53-55", "AiShoujo keys applied through the Bone list: " + Ranges(ai.AppliedPlain) + " (expected the AI male files' 53-55)");
            }
            var add = fam[PoseKeyFamily.AdditionalFkNodes];
            if (map.NodeCount > 0)
                t.Check(Ranges(add.SkippedKeys) == "900-901" && Ranges(add.AppliedPlain) == "10000-10019,11000-11003",
                    $"AdditionalFkNodes: every key resolved through the node tables except 900-901: applied {(add.AppliedPlain.Count > 0 ? Ranges(add.AppliedPlain) : "none")}, unresolved {(add.SkippedKeys.Count > 0 ? Ranges(add.SkippedKeys) : "none")}");
            else
                t.Info($"AdditionalFkNodes: {add.Keys - add.Applied} of {add.Keys} keys unresolved (no node tables given; reported, not applied)");
            if (baseline?["fkFamilies"] is JObject rows)
            {
                // rows of the spec table without trailer whose extra keys are AI ranges = the AI-numbered files
                int expectedAi = 0;
                foreach (var p in rows.Properties())
                {
                    if (p.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    var parts = p.Name.Split(' ');
                    if (parts.Length == 4 && parts[1] == "none" && parts[3] != "-" && parts[3].Split(',').Any(r => PoseKeyMap.FamilyOf(int.Parse(r.Split('-')[0], CultureInfo.InvariantCulture)) == PoseKeyFamily.AiShoujo))
                        expectedAi += p.Value.Value<int>();
                }
                t.Check(aiFiles == expectedAi, $"AI-numbered files = the AI rows of the family table ({aiFiles}, table {expectedAi})");
            }
            else t.Info("AI-numbered files: " + aiFiles);
        }
    }
}
