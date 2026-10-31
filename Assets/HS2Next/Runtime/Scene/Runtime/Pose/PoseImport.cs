// HS2 pose-file import into a scene character (Studio and plugin behaviour: Docs/
// 4). Order; one bus transaction "Load pose" (one history entry; opened
// with CommandBus.BeginOrJoin, so inside an open transaction such as a bridge batch it becomes steps of that one):
//   1 read        payload and trailer (package B's PoseFile; .dat and PNG); gameName from the ExtendedSave entry (absent 0);
//                 gameName 1 / 6 → pose.koikatsuFingers (the plugin's finger rewrite is not reproduced)
//   2 animation   chara.anime.ref + chara.anime.time (raw normalizedTime) when the record resolves (StudioDb.FindAnime;
//                 plain and H rows alike); the same record → only the time; else the current motion stays (pose.animeMissing)
//   3 keys        package B's PoseKeyMap: Bone lists, the AI breast remap (326–330 / 332–336 → 53–57 / 59–63 when the file
//                 has 326; PoseImportOptions.AiBreastRemap), the FK node rows of the build hooks; other keys skipped, reported
//                 (pose.unresolvedKey: info for the named families, warning otherwise) and kept raw in hs2.pose
//   4 values      rot → a copy of fk.bones (file wins; two keys on one name → the later; bit for bit); key 0 pos.y →
//                 chara.fk.hipsOffset (×10 for gameName 1, 2, 5, 6; set also when 0); other pos / scale counted
//   5 modes       fk.enabled / fk.groups / ik.enabled / ik.groups (both modes → fk.groups.body false); Skirt only
//                 with imported skirt values (SkirtNeedsData). IK (3.4, PoseImport_Ik.cs):
//                 files with enableIK get their 13 targets converted on this character (scale ×1 / ×10 / rejected, Body
//                 Spine01 → hips) and written to chara.ik.targets; the raw targets stay in hs2.pose.ik with scale and
//                 bodyShift. PoseImportOptions.Ik false keeps 2.3's behaviour (raw only, pose.ikPending)
//   6 PoseTools   gameName 3 / 4: face ids, eyes / mouth open (Expression), skirt values 400–447; correction[4..7] from
//                 JointCorrectionData (any gameName), [0..3] from the payload
//   7 capture     missing values of the groups the import leaves applied (FkOps.MissingValueSets; CaptureMissing)
//   8 one set of chara.fk.bones with the merged map
//   9 provenance  the node's hs2.pose block through SetNodeExtCommand (Provenance)
// Plan() is pure (no Unity object): PoseImportTest plans the whole corpus without building characters.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content.Studio;
using HS2Next.Import.Pose;
using HS2Next.Motion.Fk;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed class PoseImportOptions
    {
        public bool Anime = true, Fk = true, Correction = true;
        /// <summary>PoseToolsData face.</summary>
        public bool Expression = true;
        /// <summary>Skirt group only with imported skirt values ("PoseImportSkirtNeedsData"); false = vanilla.</summary>
        public bool SkirtNeedsData = true;
        /// <summary>326–330 / 332–336 → 53–57 / 59–63 as HS2_Fix_PoseLoad (I11); false = AI keys 0–51 only (Q16).</summary>
        public bool AiBreastRemap = true;
        /// <summary>Key 0 pos.y → fk.hipsOffset.</summary>
        public bool HipsOffset = true;
        /// <summary>Capture the missing values of the groups the import leaves applied.</summary>
        public bool CaptureMissing = true;
        /// <summary>Write the hs2.pose block.</summary>
        public bool Provenance = true;
        /// <summary>Convert and write the IK targets of files with enableIK (3.4); false = raw only (pose.ikPending, 2.3).</summary>
        public bool Ik = true;
        public string Label = "Load pose";
    }

    public sealed class PoseImportReport
    {
        /// <summary>Error: nothing was applied.</summary>
        public bool Applied; public string Error;
        /// <summary>Codes of Appendix A.</summary>
        public FindingList Findings = new FindingList();
        public int Keys, Mapped, AiRemapped, Unresolved, NotOnCharacter, GameName;
        /// <summary>"guid g/c/n", or null (current motion kept).</summary>
        public string AnimeResolved;
        /// <summary>enableIK with PoseImportOptions.Ik false: targets kept raw only (convertible later with ik.copy from:"import").</summary>
        public bool IkPending;
        /// <summary>IK targets were converted and written.</summary>
        public bool IkApproximated;
        /// <summary>1, 10, or 0 = rejected / not converted.</summary>
        public int IkScale;
        /// <summary>The median bend-goal ratio the scale was detected from (0 when not computed).</summary>
        public float IkRatio;
        /// <summary>T_s − T_h of the Body conversion (root space), or null.</summary>
        public Vec3? IkBodyShift;
        /// <summary>End targets whose predicted reach exceeds 0.99999 × (L1 + L2) on this character.</summary>
        public IReadOnlyList<string> IkUnreachable = new string[0];
        /// <summary>Non-zero stored rotations of targets that use none (dropped).</summary>
        public int IkRotDropped;
        /// <summary>What a third Body pass would still move (U5).</summary>
        public float IkBodyPassResidual;
        public bool SkirtGroupForcedOff;
        /// <summary>Keys written by the import in groups the import leaves applied (compare).</summary>
        public IReadOnlyList<string> AppliedBones = new string[0];
        /// <summary>Keys added by the missing-value capture.</summary>
        public IReadOnlyList<string> Captured = new string[0];
        /// <summary>Unresolved keys per family (PoseKeyFamily name → count).</summary>
        public readonly SortedDictionary<string, int> UnresolvedFamilies = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int SkirtValues, IgnoredPos, IgnoredScale;
        /// <summary>Whole Apply; the plan (map, values, capture); the bus transaction (without the realization's Apply, i.e. without the animation load).</summary>
        public double Milliseconds, PlanMilliseconds, TransactionMilliseconds;

        public override string ToString() =>
            Error != null ? "error: " + Error :
            $"keys {Keys}, mapped {Mapped} (AI remap {AiRemapped}), unresolved {Unresolved}, not on character {NotOnCharacter}, anime {AnimeResolved ?? "kept"}, gameName {GameName}{(IkPending ? ", IK pending" : "")}{(IkApproximated ? $", IK ×{IkScale}" : "")}{(SkirtGroupForcedOff ? ", skirt group off" : "")}";
    }

    /// <summary>How an anime record of a pose resolved.</summary>
    public sealed class PoseAnimeResolution
    {
        /// <summary>The record to play, or null.</summary>
        public StudioAnime Row;
        /// <summary>pose.animeMissing when Row is null; pose.animeHidden (with Row) for a row the lists hide.</summary>
        public string Code;
        public string Detail;
    }

    public sealed class PosePlanInput
    {
        public PoseFile Pose;
        /// <summary>Bone-list rows (FkRows.From).</summary>
        public IReadOnlyList<FkRow> Rows;
        /// <summary>FK node rows (CharaBuildHooks.FkNodes), or null.</summary>
        public IReadOnlyList<FkRow> NodeRows;
        /// <summary>A prebuilt key map for Rows + NodeRows with the same AiBreastRemap option (corpus planning); null = built here.</summary>
        public PoseKeyMap Map;
        /// <summary>The character's sex (0 male, 1 female, -1 unknown).</summary>
        public int Sex = -1;
        /// <summary>Keys present on the character (FkCharacter table: primary and partner keys); null = all.</summary>
        public ICollection<string> Present;
        public CharaNodeState Current;
        public Func<int, int, int, PoseAnimeResolution> ResolveAnime;
        public PoseImportOptions Options;
        /// <summary>The block's `source` ('/'-separated, relative to UserData/studio/pose, or the file name).</summary>
        public string Source;
        /// <summary>Values to add for the bones of the given groups that the map lacks (FkOps.MissingValueSets); null = no capture.</summary>
        public Func<IReadOnlyList<FkGroup>, IDictionary<string, Vec3>, IDictionary<string, Vec3>> CaptureMissing;
        /// <summary>The character's geometry for the IK conversion (PoseIkBody.Of); null = IK not converted (rejected).</summary>
        public PoseIkBody IkBody;
    }

    /// <summary>The writes of one import (Plan) and its report.</summary>
    public sealed class PosePlan
    {
        public PoseImportReport Report = new PoseImportReport();
        public AnimeRef AnimeRef;            // non-null: set chara.anime.ref
        public bool SetAnimeTime; public float AnimeTime;
        public bool SetFk;
        public SortedDictionary<string, Vec3> Bones;
        public bool SetHipsOffset; public float HipsOffset;
        public bool FkEnabled; public readonly bool[] FkGroups = new bool[FkGroupInfo.Count];
        public bool IkEnabled; public readonly bool[] IkGroups = new bool[5];
        /// <summary>Set the 13 chara.ik.targets.* (Targets; null entries clear a target).</summary>
        public bool SetIkTargets; public IkTarget[] IkTargets;
        /// <summary>The IK conversion (null when the file has no enableIK or Options.Ik is false).</summary>
        public PoseIkResult Ik;
        public int? Eyebrow, Eyes, Mouth; public float? EyesOpen, MouthOpen;
        public bool[] Correction;            // non-null: set chara.correction
        public string ProvenanceJson;        // non-null: set the hs2.pose block
        public string PayloadSha256;
    }

    public static partial class PoseImport
    {
        public const string ProvenanceNamespace = "hs2.pose";
        static readonly string[] IkGroupMembers = { "body", "rightLeg", "leftLeg", "rightArm", "leftArm" };

        // ------------------------------------------------------------ apply

        public static PoseImportReport ApplyFile(SceneRuntime rt, string nodeId, string fullPath, PoseImportOptions o = null, ExecuteOptions eo = default)
        {
            PoseFile pose;
            try { pose = PoseFile.ReadFile(fullPath); }
            catch (PoseFormatException e) { return new PoseImportReport { Error = "pose file: " + e.Message }; }
            return Apply(rt, nodeId, pose, o, eo, SourceOf(rt, fullPath));
        }

        public static PoseImportReport Apply(SceneRuntime rt, string nodeId, PoseFile pose, PoseImportOptions o = null, ExecuteOptions eo = default) =>
            Apply(rt, nodeId, pose, o, eo, null);

        /// <summary>The `source` of a file: relative to &lt;HS2&gt;/UserData/studio/pose with '/', else the file name.</summary>
        public static string SourceOf(SceneRuntime rt, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            string root = rt?.Services?.Hs2Root;
            if (!string.IsNullOrEmpty(root))
            {
                string poseDir = Path.GetFullPath(Path.Combine(root, "UserData", "studio", "pose")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(fullPath);
                if (full.StartsWith(poseDir, StringComparison.OrdinalIgnoreCase)) return full.Substring(poseDir.Length).Replace('\\', '/');
            }
            return Path.GetFileName(fullPath);
        }

        static PoseImportReport Apply(SceneRuntime rt, string nodeId, PoseFile pose, PoseImportOptions o, ExecuteOptions eo, string source)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            o = o ?? new PoseImportOptions();
            if (rt == null) throw new ArgumentNullException(nameof(rt));
            if (pose == null) return new PoseImportReport { Error = "no pose" };
            var n = rt.Model.Find(nodeId);
            if (n == null) return new PoseImportReport { Error = CommandCodes.NotFound + ": no node " + nodeId };
            if (n.Kind != NodeKind.Chara || !(n.State is CharaNodeState st)) return new PoseImportReport { Error = CommandCodes.WrongKind + ": " + nodeId + " is not a character" };
            rt.ApplyNow();
            var r = rt.FindChara(nodeId);
            if (r == null || r.State != RealizationState.Built) return new PoseImportReport { Error = "not built" };
            var db = rt.Services.Db;
            var rows = FkRows.From(db);
            // keys the Bone lists lack → the node table joins first (so the plan knows which node bones exist)
            IReadOnlyList<FkRow> nodeRows = null;
            var rowNos = new HashSet<int>(rows.Select(x => x.No));
            if (pose.FK.Any(e => !rowNos.Contains(e.Key)))
            {
                var nodes = HS2Next.Chara.Ext.CharaBuildHooks.FkNodes(rt.Services.Catalog);   // the build hooks' node rows; null in the official build
                nodeRows = nodes?.Rows;
                if (nodes != null) FkDriver.Of(r)?.JoinNodes(nodes);
            }
            var table = FkDriver.TableFor(r);
            HashSet<string> present = null;
            if (table != null)
            {
                present = new HashSet<string>(StringComparer.Ordinal);
                foreach (var b in table.Bones) { present.Add(b.Key); if (b.PartnerKey != null) present.Add(b.PartnerKey); }
            }
            var ops = new FkOps(rt);
            var swPlan = System.Diagnostics.Stopwatch.StartNew();
            var plan = Plan(new PosePlanInput
            {
                Pose = pose, Rows = rows, NodeRows = nodeRows, Sex = r.Sex, Present = present, Current = st,
                ResolveAnime = (g, c, no) => ResolveAnime(db, g, c, no), Options = o, Source = source,
                CaptureMissing = o.CaptureMissing ? (groups, map) => ops.MissingValueSets(nodeId, groups, map) : (Func<IReadOnlyList<FkGroup>, IDictionary<string, Vec3>, IDictionary<string, Vec3>>)null,
                IkBody = pose.EnableIK && o.Ik ? PoseIkBody.Of(r) : null,
            });
            var rep = plan.Report;
            rep.PlanMilliseconds = swPlan.Elapsed.TotalMilliseconds;
            if (rep.Error != null) return rep;
            if (eo.Label == null) eo.Label = o.Label;
            var swTx = System.Diagnostics.Stopwatch.StartNew();
            using (var tx = rt.Session.Commands.BeginOrJoin(eo.Label, eo))
            {
                if (!tx.IsOpen) { rep.Error = tx.Failure?.ToString() ?? "busy"; return rep; }
                string err = null;
                void Do(CommandResult res, string what) { if (err == null && res != null && !res.Ok) err = what + ": " + res; }
                void Set(string path, object v) { if (err == null) Do(tx.Set(nodeId, path, v), path); }
                if (plan.AnimeRef != null) Set("chara.anime.ref", plan.AnimeRef);
                if (plan.SetAnimeTime) Set("chara.anime.time", plan.AnimeTime);
                if (plan.SetFk)
                {
                    Set("chara.fk.bones", plan.Bones);
                    if (plan.SetHipsOffset) Set("chara.fk.hipsOffset", plan.HipsOffset);
                    Set("chara.fk.enabled", plan.FkEnabled);
                    for (int g = 0; g < FkGroupInfo.Count; g++) Set("chara.fk.groups." + FkGroupInfo.MemberNames[g], plan.FkGroups[g]);
                    Set("chara.ik.enabled", plan.IkEnabled);
                    for (int g = 0; g < 5; g++) Set("chara.ik.groups." + IkGroupMembers[g], plan.IkGroups[g]);
                    if (plan.SetIkTargets)
                        for (int i = 0; i < IkTargets.Count; i++) Set("chara.ik.targets." + IkTargets.Names[i], plan.IkTargets[i]);
                }
                if (plan.Eyebrow.HasValue) Set("chara.face.eyebrow", plan.Eyebrow.Value);
                if (plan.Eyes.HasValue) Set("chara.face.eyes", plan.Eyes.Value);
                if (plan.Mouth.HasValue) Set("chara.face.mouth", plan.Mouth.Value);
                if (plan.EyesOpen.HasValue) Set("chara.face.eyesOpen", plan.EyesOpen.Value);
                if (plan.MouthOpen.HasValue) Set("chara.face.mouthOpen", plan.MouthOpen.Value);
                if (plan.Correction != null) Set("chara.correction", plan.Correction);
                if (plan.ProvenanceJson != null && err == null) Do(tx.Execute(new SetNodeExtCommand(nodeId, ProvenanceNamespace, plan.ProvenanceJson, eo.Label)), ProvenanceNamespace);
                if (err != null)
                {
                    tx.Cancel();
                    rt.ApplyNow();
                    rep.Error = err;
                    return rep;
                }
                var c = tx.Commit();
                if (!c.Ok) { rep.Error = "commit: " + c; rt.ApplyNow(); return rep; }
            }
            rep.TransactionMilliseconds = swTx.Elapsed.TotalMilliseconds;
            rt.ApplyNow();
            rep.Applied = true;
            sw.Stop();
            rep.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return rep;
        }

        /// <summary>
        /// StudioDb.FindAnime with the cases. A record that the Studio lists hide (the AI-Shoujo gate, an unknown
        /// category) is still played, as HS2's pose load does (it looks the record up directly; recorded on the reference
        /// install: 0/2/11 plays); it is noted (pose.animeHidden, info). (2.3 impl)
        /// </summary>
        public static PoseAnimeResolution ResolveAnime(StudioDb db, int group, int category, int no)
        {
            var row = db?.FindAnime(group, category, no);
            if (row == null) return new PoseAnimeResolution { Code = "pose.animeMissing", Detail = "no record" };
            if (!row.Visible) return new PoseAnimeResolution { Row = row, Code = "pose.animeHidden", Detail = row.AiContent ? "AI-gated in the lists" : "hidden in the lists" };
            return new PoseAnimeResolution { Row = row };
        }

        // ------------------------------------------------------------ plan

        /// <summary>Builds the key map of the Bone rows and node rows (PoseKeyMap of package B).</summary>
        public static PoseKeyMap MapFor(IReadOnlyList<FkRow> rows, IReadOnlyList<FkRow> nodeRows, bool aiBreastRemap) =>
            new PoseKeyMap(FkRows.ToPoseBones(rows ?? new FkRow[0], "bone list"), nodeRows != null ? FkRows.ToPoseBones(nodeRows, "node table") : null,
                           new PoseKeyMapOptions { AiBreastRemap = aiBreastRemap });

        static bool InfoFamily(PoseKeyFamily f) => f == PoseKeyFamily.AiShoujo || f == PoseKeyFamily.AdditionalFkNodes;

        /// <summary>The writes and the report of one import; touches no Unity object.</summary>
        public static PosePlan Plan(PosePlanInput input)
        {
            var plan = new PosePlan();
            var rep = plan.Report;
            var o = input?.Options ?? new PoseImportOptions();
            var pose = input?.Pose;
            if (pose == null) { rep.Error = "no pose"; return plan; }
            var f = rep.Findings;
            var st = input.Current ?? new CharaNodeState();
            int gameName = pose.GameName ?? 0;
            rep.GameName = gameName;
            rep.Keys = pose.FK.Count;
            if (gameName == 1 || gameName == 6) f.Info("pose.koikatsuFingers", "", $"gameName {gameName}: the plugin's Koikatsu finger rewrite is not reproduced");
            if (input.Sex >= 0 && pose.Sex != input.Sex) f.Info("pose.sexMismatch", "", $"pose for {(pose.Sex == 0 ? "male" : "female")} imported into a {(input.Sex == 0 ? "male" : "female")} character");

            // 2 animation
            if (o.Anime)
            {
                var res = input.ResolveAnime?.Invoke(pose.AnimeGroup, pose.AnimeCategory, pose.AnimeNo);
                if (res?.Row != null)
                {
                    var row = res.Row;
                    var cur = st.Anime?.Ref;
                    bool same = cur != null && cur.Source == row.Guid && cur.Group == pose.AnimeGroup && cur.Category == pose.AnimeCategory && cur.No == pose.AnimeNo;
                    if (!same) plan.AnimeRef = new AnimeRef(row.Guid, pose.AnimeGroup, pose.AnimeCategory, pose.AnimeNo);
                    plan.SetAnimeTime = true;
                    plan.AnimeTime = pose.NormalizedTime;
                    rep.AnimeResolved = $"{row.Guid} {pose.AnimeGroup}/{pose.AnimeCategory}/{pose.AnimeNo}";
                    if (res.Code != null) f.Info(res.Code, "", $"anime {pose.AnimeGroup}/{pose.AnimeCategory}/{pose.AnimeNo} ({res.Detail}) played, as HS2's pose load plays it");
                }
                else
                {
                    string code = res?.Code ?? "pose.animeMissing";
                    f.Info(code, "", $"anime {pose.AnimeGroup}/{pose.AnimeCategory}/{pose.AnimeNo}: {res?.Detail ?? "no resolver"}; the current motion is kept");
                }
            }

            // 3–4 keys and values
            var map = input.Map ?? MapFor(input.Rows, input.NodeRows, o.AiBreastRemap);
            var mapping = map.MapFk(pose, input.Sex);
            rep.IgnoredPos = mapping.IgnoredPos;
            rep.IgnoredScale = mapping.IgnoredScale;
            var bones = new SortedDictionary<string, Vec3>(st.Fk?.Bones ?? new SortedDictionary<string, Vec3>(), StringComparer.Ordinal);
            var written = new Dictionary<string, int>(StringComparer.Ordinal);   // name → group of keys written by the import
            var unresolved = new SortedDictionary<int, PoseEntry>();
            int notOn = 0, dupKeys = 0, dupTargets = 0, remapped = 0;
            foreach (var e in mapping.Entries)
            {
                switch (e.Skip)
                {
                    case PoseKeySkipReason.None:
                    {
                        string name = e.Bone.TransformName;
                        var rot = e.Entry.Rot;
                        bones[name] = new Vec3(rot.X, rot.Y, rot.Z);
                        written[name] = FkGroupInfo.OfColumn(e.Bone.Column);
                        rep.Mapped++;
                        if (e.Remapped) remapped++;
                        if (e.MaleOnlyOnFemale || (input.Present != null && !input.Present.Contains(name))) notOn++;
                        break;
                    }
                    case PoseKeySkipReason.DuplicateKey: dupKeys++; break;
                    case PoseKeySkipReason.Superseded: dupTargets++; break;
                    default:
                    {
                        if (!unresolved.ContainsKey(e.Key)) unresolved[e.Key] = e.Entry;
                        string fam = e.Family.ToString();
                        rep.UnresolvedFamilies[fam] = (rep.UnresolvedFamilies.TryGetValue(fam, out var k) ? k : 0) + 1;
                        f.Add(InfoFamily(e.Family) ? FindingSeverity.Info : FindingSeverity.Warning, "pose.unresolvedKey", fam,
                              $"FK key {e.Key} ({fam}): {e.Reason}; kept raw in hs2.pose");
                        break;
                    }
                }
            }
            rep.Unresolved = unresolved.Count;
            rep.AiRemapped = remapped;
            rep.NotOnCharacter = notOn;
            if (remapped > 0) f.Info("pose.aiBreastRemap", "", $"{remapped} AI breast keys applied as HS2's 53–57 / 59–63 (HS2_Fix_PoseLoad)");
            if (dupKeys > 0) f.Warning("pose.duplicateKey", "", $"{dupKeys} FK keys occur twice; the first record is used");
            if (dupTargets > 0) f.Info("pose.duplicateTarget", "", $"{dupTargets} FK keys map to a bone that a later key also sets; the later key wins");
            if (notOn > 0) f.Info("pose.boneNotOnCharacter", "", $"{notOn} mapped bones are not on this character (values kept in the model)");
            if (mapping.IgnoredPos > 0) f.Info("pose.fkPosIgnored", "", $"{mapping.IgnoredPos} FK positions other than the hips offset ignored");
            if (mapping.IgnoredScale > 0) f.Info("pose.fkScaleIgnored", "", $"{mapping.IgnoredScale} FK scales ≠ 1 ignored");

            // 6 PoseToolsData (skirt values first: they decide the Skirt group)
            var tools = pose.PoseTools;
            bool expressionGame = gameName == 3 || gameName == 4;
            int skirt = 0;
            if (tools != null && expressionGame && o.Fk && tools.SkirtFK != null)
                foreach (var s in tools.SkirtFK)
                {
                    if (!map.TryGetBone(s.Key, out var b) || b == null) continue;
                    var rot = s.Rot;
                    bones[b.TransformName] = new Vec3(rot.X, rot.Y, rot.Z);
                    written[b.TransformName] = FkGroupInfo.OfColumn(b.Column);
                    skirt++;
                }
            rep.SkirtValues = skirt;
            if (tools != null && expressionGame && o.Expression)
            {
                plan.Eyebrow = tools.EyebrowPattern; plan.Eyes = tools.EyesPattern; plan.Mouth = tools.MouthPattern;
                plan.EyesOpen = tools.EyeOpen; plan.MouthOpen = tools.MouthOpen;
            }
            if (o.Correction)
            {
                var corr = (bool[])(st.Correction ?? new bool[8]).Clone();
                if (corr.Length != 8) Array.Resize(ref corr, 8);
                for (int i = 0; i < 4 && i < pose.Expression.Length; i++) corr[i] = pose.Expression[i];
                var jc = tools?.JointCorrection;
                if (jc != null) for (int i = 4; i < 8 && i < jc.Length; i++) corr[i] = jc[i];
                plan.Correction = corr;
            }

            // 5 modes and groups
            if (o.Fk)
            {
                plan.SetFk = true;
                if (o.HipsOffset && mapping.HipsPos.HasValue) { plan.SetHipsOffset = true; plan.HipsOffset = mapping.HipsOffset; }
                plan.FkEnabled = pose.EnableFK;
                for (int g = 0; g < FkGroupInfo.Count && g < pose.ActiveFK.Length; g++) plan.FkGroups[g] = pose.ActiveFK[g];
                plan.IkEnabled = pose.EnableIK;
                for (int g = 0; g < 5 && g < pose.ActiveIK.Length; g++) plan.IkGroups[g] = pose.ActiveIK[g];
                if (pose.EnableIK && pose.EnableFK) plan.FkGroups[(int)FkGroup.Body] = false;   // HS2_FKIK
                if (pose.EnableIK && !o.Ik)
                {
                    rep.IkPending = true;
                    f.Info("pose.ikPending", "", "IK targets kept raw in hs2.pose (IK import off; convert with ik.copy from:\"import\")");
                }
                else if (pose.EnableIK)
                {
                    var ik = PlanIk(RawIk(pose), pose.ActiveIK, input.IkBody);
                    plan.Ik = ik;
                    plan.SetIkTargets = true;
                    plan.IkTargets = ik.Targets ?? new IkTarget[IkTargets.Count];   // rejected: every target cleared (effective = the pose you see)
                    ReportIk(rep, ik);
                }
                if (o.SkirtNeedsData && skirt == 0 && plan.FkGroups[(int)FkGroup.Skirt])
                {
                    plan.FkGroups[(int)FkGroup.Skirt] = false;
                    rep.SkirtGroupForcedOff = true;
                    f.Info("pose.skirtGroupOff", "", "the Skirt group is on in the file but no skirt values were imported: switched off (PoseTools)");
                }
                // 7 capture
                if (plan.FkEnabled && input.CaptureMissing != null)
                {
                    var groups = new List<FkGroup>();
                    for (int g = 0; g < FkGroupInfo.Count; g++) if (plan.FkGroups[g]) groups.Add((FkGroup)g);
                    var add = groups.Count > 0 ? input.CaptureMissing(groups, bones) : null;
                    if (add != null && add.Count > 0)
                    {
                        foreach (var kv in add) bones[kv.Key] = kv.Value;
                        rep.Captured = add.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                    }
                }
                plan.Bones = bones;
                var applied = new List<string>();
                if (plan.FkEnabled)
                    foreach (var kv in written)
                        if (kv.Value >= 0 && plan.FkGroups[kv.Value]) applied.Add(kv.Key);
                applied.Sort(StringComparer.Ordinal);
                rep.AppliedBones = applied;
            }

            // trailer
            var trailerKept = new List<KKExEntry>();
            if (pose.Ext != null)
                foreach (var e in pose.Ext.Entries)
                    if (e.Guid != KKExTrailer.ExtendedSaveGuid && e.Guid != PoseToolsData.Guid) trailerKept.Add(e);
            if (trailerKept.Count > 0) f.Info("pose.trailerKept", "", $"{trailerKept.Count} unknown trailer entries kept raw in hs2.pose ({string.Join(", ", trailerKept.Select(x => x.Guid))})");

            // 9 provenance
            byte[] payload = pose.WritePayload(true);
            using (var h = SHA256.Create()) plan.PayloadSha256 = Hex(h.ComputeHash(payload));
            if (o.Provenance) plan.ProvenanceJson = ProvenanceJson(input.Source, plan.PayloadSha256, gameName, unresolved, pose, trailerKept, plan.Ik);
            return plan;
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static float[] Nine(PoseEntry e)
        {
            var a = new float[PoseProvenance.FloatsPerKey];
            for (int i = 0; i < a.Length; i++) a[i] = e[i];
            return a;
        }

        /// <summary>The hs2.pose block through package C's PoseProvenance (canonical JSON, every member written).</summary>
        public static string ProvenanceJson(string source, string sha256, int gameName, SortedDictionary<int, PoseEntry> unresolved, PoseFile pose, IReadOnlyList<KKExEntry> trailer, PoseIkResult ik = null)
        {
            var p = new PoseProvenance { Source = source ?? "", Sha256 = sha256 ?? "", GameName = gameName };
            foreach (var kv in unresolved) p.Unresolved[kv.Key] = Nine(kv.Value);
            if (pose.EnableIK)
            {
                p.Ik = new PoseProvenance.PoseIkBlock { Enabled = true };
                for (int i = 0; i < 5 && i < pose.ActiveIK.Length; i++) p.Ik.Groups[i] = pose.ActiveIK[i];
                foreach (var e in pose.IK) if (!p.Ik.Targets.ContainsKey(e.Key)) p.Ik.Targets[e.Key] = Nine(e);   // duplicates: the first (Studio)
                SetIkConversion(p.Ik, ik);
            }
            foreach (var e in trailer) if (e.Guid != null && !p.Trailer.ContainsKey(e.Guid)) p.Trailer[e.Guid] = Convert.ToBase64String(e.RawValue ?? new byte[0]);
            return p.ToJson();
        }
    }
}
