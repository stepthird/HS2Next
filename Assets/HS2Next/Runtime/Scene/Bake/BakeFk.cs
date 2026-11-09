// Chara to Item: the FK list and the joint correction of a bake (V18, V20).
//   list      a character's FK bones (its FK table) of the chosen groups, a Skirt bone only when used, partners with their bones; an
//             item's FK names found in its instance (first match in node order, as item FK finds them)
//   pruned    to the bones that move what is baked: for each renderer the nodes its stored weights (> 0) name, for an unskinned
//             renderer its node; from each, the nearest FK bone at or above it (Prune, Unity-free)
//   names     an FK bone keeps its name unless an earlier FK bone of the list has it (then "<name>~<node index>"); every other node
//             named like an FK bone becomes "<name>~<node index>" (Rename, Unity-free); renderer bone lists use indices
//   own list  a prop without FK: the nodes its kept vertices weigh on whose names occur once, not the root (OwnList)
//   correction  a bake holding the body skeleton (cf_J_Root and every bone of the 26 rows) stores the character's 8 joint-correction
//             categories when one is on; its item gets a joint-correction stage after its FK stage, bound by name under the
//             instance's cf_J_Root, with those categories (AddCorrection, from ItemMotion)
using System;
using System.Collections.Generic;
using HS2Next.Motion;
using HS2Next.Motion.Corrective;
using HS2Next.Motion.Fk;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.Scenes.Bake
{
    public static class BakeFk
    {
        public const string SkeletonRoot = "cf_J_Root";

        // ------------------------------------------------------------ Unity-free rules

        /// <summary>
        /// The FK nodes that move what is baked: for each used node, the nearest node at or above it that is in <paramref name="fkNodes"/>.
        /// Returns those FK nodes in the order of <paramref name="fkNodes"/>. <paramref name="parents"/>: each node's parent (−1 the root).
        /// </summary>
        public static List<int> Prune(IReadOnlyList<int> parents, IReadOnlyList<int> fkNodes, IEnumerable<int> used)
        {
            var fk = new HashSet<int>(fkNodes);
            var keep = new HashSet<int>();
            foreach (int u in used)
            {
                for (int n = u, guard = 0; n >= 0 && n < parents.Count && guard <= parents.Count; n = parents[n], guard++)
                    if (fk.Contains(n)) { keep.Add(n); break; }
            }
            var o = new List<int>();
            var seen = new HashSet<int>();
            foreach (int n in fkNodes) if (keep.Contains(n) && seen.Add(n)) o.Add(n);
            return o;
        }

        /// <summary>
        /// Makes the FK names unique (in place): an FK node keeps its name unless an earlier one of the list has it (then
        /// "&lt;name&gt;~&lt;index&gt;"); every other node named like an FK node becomes "&lt;name&gt;~&lt;index&gt;". Returns the FK names in list order.
        /// </summary>
        public static List<string> Rename(IList<string> names, IReadOnlyList<int> fkNodes)
        {
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            var isFk = new HashSet<int>();
            var o = new List<string>();
            foreach (int i in fkNodes)
            {
                if (!isFk.Add(i)) continue;
                string n = names[i];
                if (taken.ContainsKey(n)) { n = n + "~" + i; names[i] = n; }
                taken[n] = i;
                o.Add(n);
            }
            for (int j = 0; j < names.Count; j++)
            {
                if (isFk.Contains(j)) continue;
                if (taken.ContainsKey(names[j])) names[j] = names[j] + "~" + j;
            }
            return o;
        }

        /// <summary>A prop's own FK list (V20): the weighted nodes whose names occur once among the nodes, not the root, in node order.</summary>
        public static List<int> OwnList(IReadOnlyList<string> names, IEnumerable<int> weighted)
        {
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in names) { count.TryGetValue(n, out int c); count[n] = c + 1; }
            var w = new HashSet<int>(weighted);
            var o = new List<int>();
            for (int i = 1; i < names.Count; i++) if (w.Contains(i) && count[names[i]] == 1) o.Add(i);
            return o;
        }

        // ------------------------------------------------------------ the bake's nodes

        static List<int> Parents(BakeData b)
        {
            var p = new List<int>(b.Nodes.Count);
            foreach (var n in b.Nodes) p.Add(n.Parent);
            return p;
        }

        /// <summary>The nodes that move what is baked: the nodes a skinned renderer's stored weights (&gt; 0) name, an unskinned
        /// renderer's node. <paramref name="weightedOnly"/>: only the weighted ones.</summary>
        public static HashSet<int> UsedNodes(BakeData b, bool weightedOnly = false)
        {
            var used = new HashSet<int>();
            foreach (var r in b.Renderers)
            {
                if (r.Mesh < 0 || r.Mesh >= b.Meshes.Count) continue;
                var md = b.Meshes[r.Mesh].Data;
                if (!r.Skinned || md?.W == null)
                {
                    if (!weightedOnly) used.Add(r.Node);
                    continue;
                }
                var hit = new bool[r.Bones.Length];
                foreach (var w in md.W)
                {
                    if (w.weight0 > 0f && w.boneIndex0 >= 0 && w.boneIndex0 < hit.Length) hit[w.boneIndex0] = true;
                    if (w.weight1 > 0f && w.boneIndex1 >= 0 && w.boneIndex1 < hit.Length) hit[w.boneIndex1] = true;
                    if (w.weight2 > 0f && w.boneIndex2 >= 0 && w.boneIndex2 < hit.Length) hit[w.boneIndex2] = true;
                    if (w.weight3 > 0f && w.boneIndex3 >= 0 && w.boneIndex3 < hit.Length) hit[w.boneIndex3] = true;
                }
                for (int i = 0; i < hit.Length; i++) if (hit[i] && r.Bones[i] >= 0) used.Add(r.Bones[i]);
            }
            return used;
        }

        /// <summary>Prunes and renames the FK nodes of a bake and writes its FK list.</summary>
        public static void Apply(BakeData b, IReadOnlyList<int> fkNodes)
        {
            var kept = Prune(Parents(b), fkNodes, UsedNodes(b));
            var names = new List<string>(b.Nodes.Count);
            foreach (var n in b.Nodes) names.Add(n.Name);
            var fk = Rename(names, kept);
            for (int i = 0; i < names.Count; i++) b.Nodes[i].Name = names[i];
            b.Fk.Clear();
            b.Fk.AddRange(fk);
        }

        // ------------------------------------------------------------ captures

        /// <summary>A character's FK: its FK table's bones of the chosen groups (all seven when null) found among the captured
        /// nodes, pruned and renamed.</summary>
        public static void ForChara(BakeData b, BakeCaptureResult res, CharaRealization c, bool[] groups) => ForChara(b, res, FkDriver.TableFor(c), groups);

        /// <summary>As above with the character's FK table given (null: no FK).</summary>
        public static void ForChara(BakeData b, BakeCaptureResult res, FkBoneTable table, bool[] groups)
        {
            if (table == null) { b.Fk.Clear(); return; }
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < res.LiveNodes.Count; i++) if (res.LiveNodes[i]) index[res.LiveNodes[i]] = i;
            var fkNodes = new List<int>();
            void Take(Transform t) { if (t && index.TryGetValue(t, out int i) && !fkNodes.Contains(i)) fkNodes.Add(i); }
            foreach (var fb in table.Bones)
            {
                int g = (int)fb.Group;
                if (groups != null && (g >= groups.Length || !groups[g])) continue;
                if (fb.Group == FkGroup.Skirt && !fb.Used) continue;
                Take(fb.Bone);
                Take(fb.Partner);
            }
            int before = fkNodes.Count;
            Apply(b, fkNodes);
            res.Inc("fk.candidates", before);
            res.Inc("fk.kept", b.Fk.Count);
        }

        /// <summary>A prop's FK: the names given, found in node order and pruned; none given: its own list (V20).</summary>
        public static void ForProp(BakeData b, BakeCaptureResult res, IReadOnlyList<string> fkNames)
        {
            var fkNodes = new List<int>();
            if (fkNames != null)
                foreach (var name in fkNames)
                {
                    int i = b.NodeIndex(name);
                    if (i >= 0 && !fkNodes.Contains(i)) fkNodes.Add(i);
                }
            if (fkNodes.Count > 0) { Apply(b, fkNodes); res?.Inc("fk.kept", b.Fk.Count); return; }
            var names = new List<string>(b.Nodes.Count);
            foreach (var n in b.Nodes) names.Add(n.Name);
            b.Fk.Clear();
            foreach (int i in OwnList(names, UsedNodes(b, true))) b.Fk.Add(names[i]);
            res?.Inc("fk.own", b.Fk.Count);
        }

        // ------------------------------------------------------------ joint correction

        /// <summary>The character's joint-correction categories when the bake holds the body skeleton and one category is on; else null.</summary>
        public static bool[] CorrectionOf(CharaRealization c, BakeData b) => CorrectionOf(c?.Correction, b);

        /// <summary>As above with the character's joint correction given (null: none).</summary>
        public static bool[] CorrectionOf(CharaJointCorrection jc, BakeData b)
        {
            if (jc == null) return null;
            var flags = jc.SceneFlags();
            bool any = false;
            foreach (bool f in flags) any |= f;
            if (!any || !HoldsSkeleton(b)) return null;
            return flags;
        }

        /// <summary>The nodes hold cf_J_Root and every bone the 26 rows name.</summary>
        public static bool HoldsSkeleton(BakeData b)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in b.Nodes) names.Add(n.Name);
            if (!names.Contains(SkeletonRoot)) return false;
            foreach (var r in JointCorrectionTable.Vanilla().Rows)
                foreach (var n in new[] { r.LookBone, r.TargetBone, r.UpBone, r.CorrectBone, r.ReferenceBone })
                    if (!string.IsNullOrEmpty(n) && !names.Contains(n)) return false;
            return true;
        }

        /// <summary>The joint-correction stage of a bake item (BQ9): after its FK stage, bound by name under the instance's cf_J_Root,
        /// with the bake's categories. Called by ItemMotion when the item's motion is built; nothing for any other item.</summary>
        public static void AddCorrection(ItemRealization it, CharaMotion motion)
        {
            if (it == null || !motion || !it.Instance) return;
            var flags = BakeCache.BakeOf(it)?.Correction;
            if (flags == null) return;
            var root = ItemDefaults.FindBone(it.Instance.transform, SkeletonRoot);
            if (!root) return;
            var table = Table(it.Runtime?.Services?.Catalog);
            var stage = new JointCorrectionStage(table, n => ItemDefaults.FindBone(root, n));
            for (int k = 0; k < flags.Length && k < JointCorrectionTable.CategoryCount; k++) stage.EnableCategory(k, flags[k]);
            motion.Add(stage);
        }

        static JointCorrectionTable Table(HS2Next.Content.ContentCatalog catalog)
        {
            try
            {
                var ta = catalog?.LoadAsset<TextAsset>(JointCorrectionTable.Bundle, JointCorrectionTable.FemaleAsset);
                if (ta != null)
                {
                    var t = JointCorrectionTable.Parse(ta.text, out _, JointCorrectionTable.FemaleAsset);
                    if (t != null) return t;
                }
            }
            catch (Exception) { }
            return JointCorrectionTable.Vanilla();
        }
    }
}
