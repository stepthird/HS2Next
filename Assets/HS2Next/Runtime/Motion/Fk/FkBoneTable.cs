// FK bone discovery for one character (Studio's behaviour
// 1). Input: the FK rows (Bone lists in row order, optionally the rows of the FK node
// tables), the character's transforms and its top / bottom clothes renderers. Rules:
//   group        from the group column (FkGroupInfo.OfColumn); other columns → no group (never applied, counted)
//   level 2      skipped for females (Bone-list rows only; node rows are not level-filtered)
//   transform    hair rows (columns 7–9): depth-first search under the head parent, children in order, first match by
//                normalised name (c_J_hairF<CLRU…>[ab]_<n> / c_J_hairB… match as c_J_hair_F… / c_J_hair_B…; transforms
//                are never renamed); every other row: the body skeleton dictionary, else a depth-first search from the
//                character root. Searches never enter a transform for which Stop returns true (scene node holders).
//   sync pairs   a row whose sync partner already became an FK bone is that bone's mirror (Partner); otherwise the
//                row is an FK bone under its own key. Node rows have no partners.
//   skirt usage  a Skirt bone is Used when a vertex of a SkinnedMeshRenderer of the top / bottom clothes
//                (rendNormal01/02/03) weights it in one of its four influences (first index of the bone in the
//                renderer's bones, as Studio); an unreadable mesh makes every skirt bone Used (counted).
//   rest         local rotation of the bone and the partner at discovery.
// Node rows join later (JoinNodes) when their table is first needed; a key that a Bone list defines keeps its row.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HS2Next.Motion.Fk
{
    /// <summary>Input of <see cref="FkBoneTable.Discover"/>.</summary>
    public sealed class FkDiscovery
    {
        /// <summary>The character root (depth-first fallback search).</summary>
        public Transform Root;
        /// <summary>Body skeleton by name (first occurrence from cf_J_Root, uncensor implants included), or null.</summary>
        public IReadOnlyDictionary<string, Transform> BodyBones;
        /// <summary>The head parent (cf_J_Head_s, the parent of p_cf_head_bone): hair rows are searched under it.</summary>
        public Transform HeadParent;
        /// <summary>Level-2 rows are skipped for females.</summary>
        public bool Female;
        /// <summary>Bone-list rows in row order (one per no).</summary>
        public IReadOnlyList<FkRow> Rows;
        /// <summary>Node rows that join at discovery (optional; usually joined later through JoinNodes).</summary>
        public IReadOnlyList<FkRow> NodeRows;
        /// <summary>Renderers of rendNormal01/02/03 of the clothes top and bottom (skirt usage).</summary>
        public IReadOnlyList<Renderer> SkirtRenderers;
        /// <summary>Never search below a transform for which this returns true (null: no stop).</summary>
        public Func<Transform, bool> Stop;
    }

    /// <summary>Discovery counts (FkWriterTest F1, the fk.* findings).</summary>
    public sealed class FkDiscoveryStats
    {
        public readonly int[] Rows = new int[FkGroupInfo.Count], Found = new int[FkGroupInfo.Count], NotFound = new int[FkGroupInfo.Count];
        public int NoGroup, Level2Skipped, Partners, PartnersMissing, HairNormalised, SkirtUsed, SkirtUnused, UnreadableMeshes, Renderers;
        public int NodeRows, NodesFound, NodesNotFound, NodesShadowed;
        public readonly List<string> NotFoundNames = new List<string>();
        public double Milliseconds, NodeMilliseconds;

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            for (int g = 0; g < FkGroupInfo.Count; g++)
                sb.Append((FkGroup)g).Append(' ').Append(Found[g]).Append('/').Append(Rows[g]).Append(g < FkGroupInfo.Count - 1 ? ", " : "");
            sb.Append($"; partners {Partners} (+{PartnersMissing} missing); hair normalised {HairNormalised}; skirt used {SkirtUsed}, unused {SkirtUnused}");
            if (UnreadableMeshes > 0) sb.Append($", unreadable meshes {UnreadableMeshes}");
            sb.Append($"; level 2 skipped {Level2Skipped}; no group {NoGroup}; nodes {NodesFound}/{NodeRows} (shadowed {NodesShadowed})");
            return sb.ToString();
        }
    }

    public sealed class FkBoneTable
    {
        static readonly Regex HairLegacy = new Regex(@"^c_J_hair([FB])([CLRU]+[a-b]_\d*)$", RegexOptions.CultureInvariant);

        readonly List<FkBone> bones = new List<FkBone>();
        readonly Dictionary<string, FkBone> byKey = new Dictionary<string, FkBone>(StringComparer.Ordinal);
        readonly Dictionary<string, FkBone> byPartnerKey = new Dictionary<string, FkBone>(StringComparer.Ordinal);
        readonly List<FkBone>[] groups = new List<FkBone>[FkGroupInfo.Count];
        readonly List<FkBone> additional = new List<FkBone>();
        readonly HashSet<int> rowNos = new HashSet<int>();
        readonly HashSet<int> nodeNos = new HashSet<int>();
        readonly FkDiscovery input;
        Dictionary<string, Transform> hairMap, rootMap;
        readonly Dictionary<Renderer, (Transform[] bones, HashSet<int> used, bool readable)> weights = new Dictionary<Renderer, (Transform[], HashSet<int>, bool)>();

        public IReadOnlyList<FkBone> Bones => bones;
        /// <summary>The additional nodes found on the character.</summary>
        public IReadOnlyList<FkBone> Additional => additional;
        public FkDiscoveryStats Stats { get; } = new FkDiscoveryStats();
        /// <summary>The node rows joined so far (JoinNodes ran).</summary>
        public bool NodesJoined { get; private set; }

        FkBoneTable(FkDiscovery input)
        {
            this.input = input;
            for (int g = 0; g < groups.Length; g++) groups[g] = new List<FkBone>();
        }

        /// <summary>The bone stored under <paramref name="key"/> (its primary key), or null.</summary>
        public FkBone Find(string key) => key != null && byKey.TryGetValue(key, out var b) ? b : null;

        /// <summary>The bone whose primary or partner key is <paramref name="key"/>, or null.</summary>
        public FkBone FindAny(string key) => key == null ? null : byKey.TryGetValue(key, out var b) ? b : byPartnerKey.TryGetValue(key, out b) ? b : null;

        public IReadOnlyList<FkBone> Of(FkGroup g) => groups[(int)g];

        /// <summary>True when the key names a row of the Bone lists given to discovery.</summary>
        public bool HasRow(int no) => rowNos.Contains(no) || nodeNos.Contains(no);

        /// <summary>Normalised hair name (transforms are never renamed).</summary>
        public static string NormaliseHairName(string name)
        {
            if (name == null || !name.StartsWith("c_J_hair", StringComparison.Ordinal)) return name;
            var m = HairLegacy.Match(name);
            return m.Success ? "c_J_hair_" + m.Groups[1].Value + m.Groups[2].Value : name;
        }

        /// <summary>Discovers the FK bones of one character.</summary>
        public static FkBoneTable Discover(FkDiscovery input, List<string> log)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t = new FkBoneTable(input ?? throw new ArgumentNullException(nameof(input)));
            var created = new Dictionary<int, FkBone>();
            if (input.Rows != null)
                foreach (var row in input.Rows)
                {
                    t.rowNos.Add(row.No);
                    t.AddRow(row, created, log);
                }
            if (input.NodeRows != null) t.JoinNodes(input.NodeRows, log);
            sw.Stop();
            t.Stats.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return t;
        }

        /// <summary>Adds the node rows whose key no Bone-list row defines. Idempotent per key.</summary>
        public void JoinNodes(IReadOnlyList<FkRow> nodeRows, List<string> log)
        {
            if (nodeRows == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            NodesJoined = true;
            foreach (var r in nodeRows)
            {
                if (rowNos.Contains(r.No)) { Stats.NodesShadowed++; continue; }
                if (!nodeNos.Add(r.No)) continue;
                var row = r;
                row.Additional = true;
                row.SyncNo = -1;
                Stats.NodeRows++;
                AddRow(row, null, log);
            }
            sw.Stop();
            Stats.NodeMilliseconds += sw.Elapsed.TotalMilliseconds;
        }

        void AddRow(FkRow row, Dictionary<int, FkBone> created, List<string> log)
        {
            if (string.IsNullOrEmpty(row.Name)) return;
            int g = FkGroupInfo.OfColumn(row.GroupColumn);
            if (g < 0) { Stats.NoGroup++; return; }
            if (!row.Additional && input.Female && row.Level == 2) { Stats.Level2Skipped++; return; }
            Stats.Rows[g]++;
            bool hair = g == (int)FkGroup.Hair;
            string key = hair ? NormaliseHairName(row.Name) : row.Name;
            var tr = hair ? FindHair(key) : FindBody(row.Name);
            if (!row.Additional && created != null && row.SyncNo >= 0 && created.TryGetValue(row.SyncNo, out var primary))
            {
                // Studio: the partner row of an existing FK bone becomes its mirror (the last one wins).
                if (tr)
                {
                    primary.Partner = tr;
                    primary.PartnerRest = tr.localRotation;
                    primary.PartnerKey = key;
                    byPartnerKey[key] = primary;
                    Stats.Partners++;
                    Stats.Found[g]++;
                }
                else { Stats.PartnersMissing++; Stats.NotFound[g]++; }
                return;
            }
            if (!tr)
            {
                Stats.NotFound[g]++;
                if (row.Additional) Stats.NodesNotFound++;
                if (Stats.NotFoundNames.Count < 400) Stats.NotFoundNames.Add(row.Name);
                return;
            }
            if (byKey.ContainsKey(key)) { log?.Add($"fk: duplicate key {key} (row {row.No}) ignored"); return; }
            if (hair && key != tr.name) Stats.HairNormalised++;
            var b = new FkBone { Key = key, Group = (FkGroup)g, Additional = row.Additional, Row = row, Bone = tr, Rest = tr.localRotation };
            if (g == (int)FkGroup.Skirt)
            {
                b.Used = SkirtUsed(tr);
                if (b.Used) Stats.SkirtUsed++; else Stats.SkirtUnused++;
            }
            bones.Add(b);
            byKey[key] = b;
            groups[g].Add(b);
            if (row.Additional) { additional.Add(b); Stats.NodesFound++; }
            Stats.Found[g]++;
            if (created != null && row.SyncNo >= 0) created[row.No] = b;
        }

        // ------------------------------------------------------------ transforms

        Transform FindHair(string normalisedName)
        {
            if (!input.HeadParent) return null;
            if (hairMap == null)
            {
                hairMap = new Dictionary<string, Transform>(StringComparer.Ordinal);
                Walk(input.HeadParent, t =>
                {
                    string n = NormaliseHairName(t.name);
                    if (!hairMap.ContainsKey(n)) hairMap[n] = t;
                });
            }
            return hairMap.TryGetValue(normalisedName, out var r) ? r : null;
        }

        Transform FindBody(string name)
        {
            if (input.BodyBones != null && input.BodyBones.TryGetValue(name, out var t) && t && !InsideStop(t)) return t;
            if (!input.Root) return null;
            if (rootMap == null)
            {
                rootMap = new Dictionary<string, Transform>(StringComparer.Ordinal);
                Walk(input.Root, x => { if (!rootMap.ContainsKey(x.name)) rootMap[x.name] = x; });
            }
            return rootMap.TryGetValue(name, out var r) ? r : null;
        }

        bool InsideStop(Transform t)
        {
            if (input.Stop == null) return false;
            for (var p = t; p && p != input.Root; p = p.parent)
                if (input.Stop(p)) return true;
            return false;
        }

        // depth-first, children in order, never entering a stop transform
        void Walk(Transform root, Action<Transform> visit)
        {
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (!t) continue;
                if (t != root && input.Stop != null && input.Stop(t)) continue;
                visit(t);
                for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
            }
        }

        // ------------------------------------------------------------ skirt usage

        bool SkirtUsed(Transform bone)
        {
            if (input.SkirtRenderers == null) return false;
            bool used = false;
            foreach (var r in input.SkirtRenderers)
            {
                var smr = r as SkinnedMeshRenderer;
                if (!smr) continue;
                var w = Weights(smr);
                if (!w.readable) { used = true; continue; }
                int idx = Array.IndexOf(w.bones, bone);
                if (idx >= 0 && w.used.Contains(idx)) used = true;
            }
            return used;
        }

        (Transform[] bones, HashSet<int> used, bool readable) Weights(SkinnedMeshRenderer smr)
        {
            if (weights.TryGetValue(smr, out var w)) return w;
            Stats.Renderers++;
            var mesh = smr.sharedMesh;
            var used = new HashSet<int>();
            bool readable = true;
            if (mesh)
            {
                BoneWeight[] bw = null;
                if (mesh.isReadable || Application.isEditor)
                {
                    try { bw = mesh.boneWeights; }
                    catch (Exception) { bw = null; }
                }
                if (bw == null || (bw.Length == 0 && mesh.vertexCount > 0)) { readable = false; Stats.UnreadableMeshes++; }
                else
                    foreach (var v in bw)
                    {
                        if (v.weight0 != 0f) used.Add(v.boneIndex0);
                        if (v.weight1 != 0f) used.Add(v.boneIndex1);
                        if (v.weight2 != 0f) used.Add(v.boneIndex2);
                        if (v.weight3 != 0f) used.Add(v.boneIndex3);
                    }
            }
            w = (smr.bones ?? new Transform[0], used, readable);
            weights[smr] = w;
            return w;
        }
    }
}
