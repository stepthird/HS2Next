// FK/IK key → bone mapping for HS2 pose files: the Bone list and groups, key families, the AI breast remap, hips offset,
// additional FK nodes and import order.
// Unity-free (System only): the caller supplies the Bone list rows (Studio list DB `Bone_*` lists:
// no → transform name, group column, level, sync partner; vanilla and zipmod rows) and, optionally, the rows of the
// installed FK node tables (rows a build hook's provider supplies); the map resolves the FK records of a pose and classifies them.
//
// API
//   new PoseKeyMap(boneRows[, nodeRows][, options])   Bone rows in load order, last row wins per no (Studio's merge);
//                                         node rows after them, first row wins per key (HS2_AdditionalFKNodes); a node
//                                         row never replaces a Bone-list row.
//   PoseKeyMap.ParseNodeTable(text, source, out problem)   the FK node-table TextAsset format.
//   PoseKeyMap.FamilyOf(key[, aiNumbered]) key family (numeric; in an AI-numbered file every key
//                                         outside 0..51 is AiShoujo).
//   PoseKeyMap.LooksAiNumbered(pose)      no trailer, lacks some of HS2's breast / male-chain keys 53–57, 59–63, 65, 66,
//                                         and has keys of the AI-Shoujo ranges (52, 326–337, 386–399, 1001–1005, 2001–2005).
//   map.MapFk(pose[, targetSex])          every FK record in file order: family, resolved bone (and the HS2 key it was
//                                         applied as), or why it is skipped. Rules (import runtime
//                                         4): a repeated key → the first record (DuplicateKey); the AI breast remap
//                                         when the file has key 326 (326–330 → 53–57, 332–336 → 59–63, as HS2_Fix_PoseLoad;
//                                         option AiBreastRemap); then the Bone list, then the node tables; rows of the Hair
//                                         and Skirt groups are not applied from the payload (outside mask 895; skirt values
//                                         come only from PoseToolsData; option PoseMaskOnly); two keys mapping to one bone →
//                                         the later record wins (Superseded, HS2 applies in file order). Option
//                                         AiSharedKeysOnly = the strict mode (AI-numbered files: 0–51 and the remap only).
//                                         Studio would stop at the first unknown key; HS2Next skips and reports.
//                                         Level-2 rows on a female target are mapped and marked MaleOnlyOnFemale (Studio has
//                                         no such bone on a female; the value is kept). The mapping also carries the hips
//                                         offset (key 0 pos.y, ×10 for gameName 1, 2, 5, 6) and the counts
//                                         of ignored pos / scale values.
//   PoseKeyMap.FkGroupOfColumn / BoneGroupFlagsOfColumn   group column → ActiveFK index / OIBoneInfo.BoneGroup flags.
//   PoseKeyMap.IkTargetName(key)          IKTargetEN name.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HS2Next.Import.Pose
{
    /// <summary>Where an FK key comes from.</summary>
    public enum PoseKeyFamily
    {
        /// <summary>A row of the vanilla HS2 Bone list (0–51, 53–57, 59–63, 65–69, hair 100–135 / 200–311 / 350–357, skirt 400–447).</summary>
        Hs2Vanilla,
        /// <summary>460–463, the `Bone_KKYBonusBones` zipmod list (resolved through the caller's Bone list).</summary>
        KkyBonusBones,
        /// <summary>Entries added to the character's FK bones by HS2_AdditionalFKNodes: 10000–10019 / 12000–12025 toes,
        /// 11000–11003 penis chain (node tables of body mods), and 900–901 (origin unknown, no table on the reference install).</summary>
        AdditionalFkNodes,
        /// <summary>AI-Shoujo numbering: 52, 326–337, 386–399, 1001–1005, 2001–2005, and in an AI-numbered file every key outside 0–51.</summary>
        AiShoujo,
        /// <summary>None of the above.</summary>
        Unknown,
    }

    /// <summary>Why an FK record is not applied.</summary>
    public enum PoseKeySkipReason
    {
        None,
        /// <summary>The key is in neither the Bone list nor the node tables given to the map.</summary>
        NotInBoneList,
        /// <summary>AI-numbered file with the strict Q16 option: only keys 0–51 (and the breast remap) are imported.</summary>
        AiKeyNotImported,
        /// <summary>The row belongs to the Hair or Skirt group, which pose payloads never carry (mask 895); skirt values come from PoseToolsData.</summary>
        NotInPoseMask,
        /// <summary>The same key occurred earlier in the file (the first record is used; Studio's own reader would throw).</summary>
        DuplicateKey,
        /// <summary>A later record of another key maps to the same bone (e.g. 326 → 53 after 53) and wins, as HS2 applies in file order.</summary>
        Superseded,
    }

    /// <summary>Options of the mapping.</summary>
    public sealed class PoseKeyMapOptions
    {
        /// <summary>HS2_Fix_PoseLoad's remap of AI breast keys when the file has key 326. Default on (reference install, Q15).</summary>
        public bool AiBreastRemap = true;
        /// <summary>
        /// Strict mode: in an AI-numbered file map only keys 0–51 besides the breast remap and report the rest. Default
        /// off = the general rule: keys of the Bone list apply as HS2_Fix_PoseLoad
        /// applies them, e.g. the AI male files' 53–55 land on HS2 breast bones.
        /// </summary>
        public bool AiSharedKeysOnly;
        /// <summary>Rows of the Hair and Skirt groups (columns 7–9, 13) are not applied from the payload (pose files never
        /// carry them, mask 895; skirt values come only from SkirtFKData). Default on.</summary>
        public bool PoseMaskOnly = true;
    }

    /// <summary>One Bone-list row (Studio list `Bone_*`) or one row of a node table.</summary>
    public sealed class PoseBoneInfo
    {
        public int No;
        /// <summary>Transform name (`取得先`), searched by exact name on the character.</summary>
        public string TransformName;
        public string DisplayName;
        /// <summary>Group column 0–13 (`分類`).</summary>
        public int Column;
        /// <summary>0 trunk/limbs/neck, 1 fingers/hair/breast/skirt, 2 male-only.</summary>
        public int Level;
        /// <summary>Sync partner no (hair "a/b" pairs), -1 = none (always -1 for node rows).</summary>
        public int SyncNo = -1;
        /// <summary>Source of the row (list GUID, e.g. the vanilla namespace or a zipmod GUID; for node rows the table's source).</summary>
        public string Source;
        /// <summary>True for a row of an FK node table (group flags without limb bits).</summary>
        public bool IsNode;

        /// <summary>ActiveFK index of the per-frame writer group (0 Hair … 6 Skirt), -1 = none.</summary>
        public int FkGroup => PoseKeyMap.FkGroupOfColumn(Column);
        /// <summary>`OIBoneInfo.group` flags (scene / pose filter); node rows have no limb bits.</summary>
        public int BoneGroupFlags => IsNode && Column >= 0 && Column <= 4 ? PoseKeyMap.GroupBody : PoseKeyMap.BoneGroupFlagsOfColumn(Column);

        public override string ToString() => $"{No} {TransformName} (col {Column}, level {Level}{(SyncNo >= 0 ? ", sync " + SyncNo : "")}{(IsNode ? ", node" : "")}, {Source})";
    }

    /// <summary>One FK record of a pose with its resolution.</summary>
    public sealed class PoseKeyResult
    {
        /// <summary>Position in the file's FK list.</summary>
        public int Index;
        public PoseEntry Entry;
        /// <summary>The key as stored in the file.</summary>
        public int Key => Entry.Key;
        /// <summary>The HS2 key the record is applied as (differs from Key for the AI breast remap), -1 when unresolved.</summary>
        public int TargetKey = -1;
        public bool Remapped => TargetKey >= 0 && TargetKey != Key;
        public PoseKeyFamily Family;
        /// <summary>The resolved bone (also set for NotInPoseMask / DuplicateKey / Superseded), null when the key has no row.</summary>
        public PoseBoneInfo Bone;
        public PoseKeySkipReason Skip;
        /// <summary>Mapped to a bone and not overridden: the value goes to <see cref="Bone"/>.</summary>
        public bool Applied => Skip == PoseKeySkipReason.None;
        /// <summary>A level-2 (male-only) row mapped for a female target: kept, but Studio has no such FK bone on a female.</summary>
        public bool MaleOnlyOnFemale;
        /// <summary>ActiveFK index of the bone, -1 when unresolved.</summary>
        public int FkGroup => Bone?.FkGroup ?? -1;
        public string Reason
        {
            get
            {
                switch (Skip)
                {
                    case PoseKeySkipReason.None: return "";
                    case PoseKeySkipReason.NotInBoneList: return "key " + Key + " (" + Family + ") is in neither the Bone lists nor the node tables";
                    case PoseKeySkipReason.AiKeyNotImported: return "key " + Key + " of an AI-numbered pose (only 0–51 and the breast remap are imported)";
                    case PoseKeySkipReason.NotInPoseMask: return "key " + Key + " is a hair or skirt bone, which pose payloads do not carry";
                    case PoseKeySkipReason.DuplicateKey: return "key " + Key + " occurs again (first record used)";
                    case PoseKeySkipReason.Superseded: return "key " + Key + " maps to " + TargetKey + ", which a later record sets again (later record used)";
                    default: return Skip.ToString();
                }
            }
        }
        public override string ToString() => Applied ? $"{Key}{(Remapped ? "→" + TargetKey : "")} → {Bone.TransformName}" : "skip " + Reason;
    }

    /// <summary>The FK records of one pose, resolved.</summary>
    public sealed class PoseFkMapping
    {
        public bool AiNumbered;
        /// <summary>True when the file has key 326 and the breast remap was enabled.</summary>
        public bool BreastRemap;
        /// <summary>ExtendedSave gameName of the file (0 without a trailer).</summary>
        public int GameName;
        /// <summary>All FK records in file order.</summary>
        public readonly List<PoseKeyResult> Entries = new List<PoseKeyResult>();
        public IEnumerable<PoseKeyResult> Applied => Entries.Where(e => e.Applied);
        public IEnumerable<PoseKeyResult> Skipped => Entries.Where(e => !e.Applied);
        public int AppliedCount => Entries.Count(e => e.Applied);
        public int SkippedCount => Entries.Count(e => !e.Applied);
        /// <summary>The FK position of `cf_J_Hips` (key 0) as stored; null when key 0 is absent.</summary>
        public PoseVector3? HipsPos;
        /// <summary>HS2_FKHeightAdjustUI hips offset: key 0 pos.y, ×10 when gameName is 1, 2, 5 or 6; 0 without key 0.</summary>
        public float HipsOffset;
        /// <summary>Records with a non-zero pos other than key 0 (ignored by HS2 and every plugin) and with a scale other than 1.</summary>
        public int IgnoredPos, IgnoredScale;
    }

    public sealed class PoseKeyMap
    {
        readonly Dictionary<int, PoseBoneInfo> bones = new Dictionary<int, PoseBoneInfo>();
        readonly Dictionary<int, PoseBoneInfo> nodes = new Dictionary<int, PoseBoneInfo>();
        public PoseKeyMapOptions Options { get; }

        /// <summary>Bone rows in load order (last wins per no); node rows (first wins per key, never replacing a Bone row).</summary>
        public PoseKeyMap(IEnumerable<PoseBoneInfo> boneRows, IEnumerable<PoseBoneInfo> nodeRows = null, PoseKeyMapOptions options = null)
        {
            Options = options ?? new PoseKeyMapOptions();
            if (boneRows != null) foreach (var b in boneRows) if (b != null && !string.IsNullOrEmpty(b.TransformName)) bones[b.No] = b;
            if (nodeRows != null) foreach (var b in nodeRows) if (b != null && !string.IsNullOrEmpty(b.TransformName) && !nodes.ContainsKey(b.No)) nodes[b.No] = b;
        }

        /// <summary>Bone-list rows.</summary>
        public int Count => bones.Count;
        public int NodeCount => nodes.Count;
        public IEnumerable<PoseBoneInfo> Bones => bones.Values.OrderBy(b => b.No);
        public IEnumerable<PoseBoneInfo> Nodes => nodes.Values.OrderBy(b => b.No);
        /// <summary>The Bone-list row, else the node row of <paramref name="no"/>.</summary>
        public bool TryGetBone(int no, out PoseBoneInfo bone) => bones.TryGetValue(no, out bone) || nodes.TryGetValue(no, out bone);

        // ---------------------------------------------------------------- node tables

        /// <summary>
        /// Parses an FK node-table TextAsset: lines split on CRLF / LF (empty lines dropped), cells on TAB, lines
        /// with fewer than 5 cells ignored; cell 0 key, 1 transform name, 2 display name, 3 group column, 4 level (further
        /// cells ignored). A non-integer key, group or level rejects the whole table (null, <paramref name="problem"/> set),
        /// as the plugin does. Rows carry IsNode and no sync partner.
        /// </summary>
        public static List<PoseBoneInfo> ParseNodeTable(string text, string source, out string problem)
        {
            problem = null;
            var rows = new List<PoseBoneInfo>();
            if (string.IsNullOrEmpty(text)) return rows;
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                var c = lines[i].Split('\t');
                if (c.Length < 5) continue;
                if (!Int(c[0], out int key) || !Int(c[3], out int col) || !Int(c[4], out int level))
                {
                    problem = $"line {i + 1}: key, group or level is not an integer (table rejected)";
                    return null;
                }
                rows.Add(new PoseBoneInfo { No = key, TransformName = c[1], DisplayName = c[2], Column = col, Level = level, SyncNo = -1, Source = source, IsNode = true });
            }
            return rows;
        }

        static bool Int(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        // ---------------------------------------------------------------- families (numeric)

        /// <summary>Numbers of the vanilla HS2 Bone list (`Bone_00/_10/_35/_50`, 271 rows), as inclusive ranges.</summary>
        public static readonly (int First, int Last)[] VanillaHs2Ranges =
            { (0, 51), (53, 57), (59, 63), (65, 69), (100, 135), (200, 311), (350, 357), (400, 447) };
        public static readonly (int First, int Last)[] KkyBonusRanges = { (460, 463) };
        public static readonly (int First, int Last)[] AdditionalFkNodeRanges = { (900, 901), (10000, 10019), (11000, 11003), (12000, 12025) };
        public static readonly (int First, int Last)[] AiShoujoRanges = { (52, 52), (326, 337), (386, 399), (1001, 1005), (2001, 2005) };
        /// <summary>HS2 keys every HS2-written pose has and AI-numbered poses lack at least partly (breast 53–57 / 59–63, male-chain roots 65, 66).</summary>
        public static readonly int[] Hs2BreastAndChainKeys = { 53, 54, 55, 56, 57, 59, 60, 61, 62, 63, 65, 66 };
        /// <summary>The keys that coincide in AI-numbered files (body, neck, fingers).</summary>
        public const int AiSharedLast = 51;
        /// <summary>HS2_Fix_PoseLoad: the key whose presence switches the breast remap on.</summary>
        public const int AiBreastTrigger = 326;

        /// <summary>HS2 key of an AI breast key (326–330 → 53–57, 332–336 → 59–63), or -1 (331, 337 and others).</summary>
        public static int AiBreastTarget(int key) =>
            key >= 326 && key <= 330 ? key - 273 : key >= 332 && key <= 336 ? key - 273 : -1;

        static bool In((int First, int Last)[] r, int k) { foreach (var x in r) if (k >= x.First && k <= x.Last) return true; return false; }

        public static bool IsVanillaHs2Key(int key) => In(VanillaHs2Ranges, key);

        /// <summary>Family by number, HS2 numbering.</summary>
        public static PoseKeyFamily FamilyOf(int key)
        {
            if (In(VanillaHs2Ranges, key)) return PoseKeyFamily.Hs2Vanilla;
            if (In(KkyBonusRanges, key)) return PoseKeyFamily.KkyBonusBones;
            if (In(AdditionalFkNodeRanges, key)) return PoseKeyFamily.AdditionalFkNodes;
            if (In(AiShoujoRanges, key)) return PoseKeyFamily.AiShoujo;
            return PoseKeyFamily.Unknown;
        }

        /// <summary>Family in the context of a file: in an AI-numbered file keys 0–51 are Hs2Vanilla, all others AiShoujo.</summary>
        public static PoseKeyFamily FamilyOf(int key, bool aiNumbered) =>
            aiNumbered ? (key >= 0 && key <= AiSharedLast ? PoseKeyFamily.Hs2Vanilla : PoseKeyFamily.AiShoujo) : FamilyOf(key);

        /// <summary>True for a pose that looks like an AI-Shoujo Studio pose (see the file header).</summary>
        public static bool LooksAiNumbered(PoseFile f)
        {
            if (f == null || f.Trailer != null) return false;
            var keys = new HashSet<int>();
            foreach (var e in f.FK) keys.Add(e.Key);
            bool lacksHs2 = Hs2BreastAndChainKeys.Any(k => !keys.Contains(k));
            bool hasAi = keys.Any(k => In(AiShoujoRanges, k));
            return lacksHs2 && hasAi;
        }

        // ---------------------------------------------------------------- mapping

        /// <summary>
        /// Resolves every FK record (rules in the file header). <paramref name="targetSex"/>: 1 = female (level-2 rows are
        /// marked MaleOnlyOnFemale), 0 = male, -1 = unknown.
        /// </summary>
        public PoseFkMapping MapFk(PoseFile f, int targetSex = -1)
        {
            var m = new PoseFkMapping { AiNumbered = LooksAiNumbered(f), GameName = f.GameName ?? 0 };
            m.BreastRemap = Options.AiBreastRemap && f.FK.Any(e => e.Key == AiBreastTrigger);
            var seenKeys = new HashSet<int>();
            var winner = new Dictionary<int, PoseKeyResult>();
            for (int i = 0; i < f.FK.Count; i++)
            {
                var e = f.FK[i];
                var r = new PoseKeyResult { Index = i, Entry = e, Family = FamilyOf(e.Key, m.AiNumbered) };
                m.Entries.Add(r);
                if (e.Key == 0)
                {
                    if (m.HipsPos == null) m.HipsPos = e.Pos;
                }
                else if (e.Pos.X != 0 || e.Pos.Y != 0 || e.Pos.Z != 0) m.IgnoredPos++;
                if (!e.Scale.Equals(PoseVector3.One)) m.IgnoredScale++;

                if (!seenKeys.Add(e.Key))
                {
                    r.Skip = PoseKeySkipReason.DuplicateKey;
                    TryGetBone(m.BreastRemap && AiBreastTarget(e.Key) >= 0 ? AiBreastTarget(e.Key) : e.Key, out r.Bone);
                    continue;
                }
                int remap = m.BreastRemap ? AiBreastTarget(e.Key) : -1;
                if (remap < 0 && m.AiNumbered && Options.AiSharedKeysOnly && !(e.Key >= 0 && e.Key <= AiSharedLast))
                {
                    r.Skip = PoseKeySkipReason.AiKeyNotImported;
                    continue;
                }
                int target = remap >= 0 ? remap : e.Key;
                if (!TryGetBone(target, out r.Bone))
                {
                    r.Skip = PoseKeySkipReason.NotInBoneList;
                    continue;
                }
                r.TargetKey = target;
                if (Options.PoseMaskOnly && (BoneGroupFlagsOfColumn(r.Bone.Column) & PoseFileGroupMask) == 0)
                {
                    r.Skip = PoseKeySkipReason.NotInPoseMask;
                    continue;
                }
                if (targetSex == 1 && r.Bone.Level == 2) r.MaleOnlyOnFemale = true;
                if (winner.TryGetValue(target, out var earlier)) earlier.Skip = PoseKeySkipReason.Superseded;
                winner[target] = r;
            }
            if (m.HipsPos.HasValue)
            {
                float h = m.HipsPos.Value.Y;
                m.HipsOffset = m.GameName == 1 || m.GameName == 2 || m.GameName == 5 || m.GameName == 6 ? h * 10f : h;
            }
            return m;
        }

        // ---------------------------------------------------------------- groups

        /// <summary>Group column → ActiveFK index of the per-frame writer (0 Hair, 1 Neck, 2 Breast, 3 Body, 4 RightHand, 5 LeftHand, 6 Skirt), -1 = none.</summary>
        public static int FkGroupOfColumn(int column)
        {
            switch (column)
            {
                case 0: case 1: case 2: case 3: case 4: return 3;
                case 5: return 4;
                case 6: return 5;
                case 7: case 8: case 9: return 0;
                case 10: return 1;
                case 11: case 12: return 2;
                case 13: return 6;
                default: return -1;
            }
        }

        /// <summary>BoneGroup flag values.</summary>
        public const int GroupBody = 1, GroupRightLeg = 2, GroupLeftLeg = 4, GroupRightArm = 8, GroupLeftArm = 16, GroupRightHand = 32,
            GroupLeftHand = 64, GroupHair = 128, GroupNeck = 256, GroupBreast = 512, GroupSkirt = 1024;
        /// <summary>The pose-file FK filter (everything except Hair and Skirt).</summary>
        public const int PoseFileGroupMask = 895;

        /// <summary>Group column → `OIBoneInfo.group` flags of a Bone-list row (limb columns carry an extra limb bit; other values `1 << column`).</summary>
        public static int BoneGroupFlagsOfColumn(int column)
        {
            switch (column)
            {
                case 0: return GroupBody;
                case 1: return GroupRightLeg | GroupBody;
                case 2: return GroupLeftLeg | GroupBody;
                case 3: return GroupRightArm | GroupBody;
                case 4: return GroupLeftArm | GroupBody;
                case 5: return GroupRightHand;
                case 6: return GroupLeftHand;
                case 7: case 8: case 9: return GroupHair;
                case 10: return GroupNeck;
                case 11: case 12: return GroupBreast;
                case 13: return GroupSkirt;
                default: return column >= 0 && column < 31 ? 1 << column : 0;
            }
        }

        /// <summary>IKTargetEN name of an IK key, or null.</summary>
        public static string IkTargetName(int key) => key >= 0 && key < PoseFile.IkTargetNames.Length ? PoseFile.IkTargetNames[key] : null;
    }
}
