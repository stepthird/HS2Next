// The procedure of a cut, Unity-free: a source bake, its FK pose at the cut and
// the steps from the first source give the pieces. The same inputs always give the same bits, so a result that is missing
// is computed again equal to the one that was stored.
//   1. Pose: the source's nodes at their stored local pose, the pose's values (local Euler degrees) on the source's FK bones
//      by name, root-space matrices from the root at identity; each mesh posed on the CPU (CutPoser). Each step is decided at its
//      own pose (the step's, else the previous step's, else the run's), so posing a piece and cutting it again never moves an
//      earlier cut. A piece's nodes hold the pose of its last step (it shows as the cut saw it).
//   2. Whole meshes: a renderer all of whose points lie on one side of a plane, inside a box or beyond one of its faces, or
//      outside an opening's box, each widened by 2 × amplitude + 0.02, goes whole to that side with its mesh unchanged.
//      "Clothes only": a renderer that is not a garment, and every member, goes whole to the side its bounds' centre is on
//      (an opening leaves it as it is); a source with no garment ignores the switch.
//   3. Refine (ragged cuts and openings), 4. clip, 5. vertices no triangle uses dropped; a renderer left with no triangle is
//      dropped. A renderer that the clip left whole keeps its mesh unchanged.
// Steps run in order on what the earlier steps kept; the last step may keep both sides (two pieces from one clip). One
// renderer is worked per MoveNext of Steps() (the panel's coroutine); Run() runs to the end (the commands).
//   FK      the source's FK list pruned to the bones that move what is left: for each kept renderer the bones its kept vertices
//           weigh on (weights > 0), for an unskinned renderer its node; from each the nearest FK bone at or above it; in the
//           source's order. A source with no FK gives the piece its own list: the bones its kept vertices weigh on whose names
//           occur once among the nodes, not the root, in node order.
//   Origin  a plane or box piece is re-rooted at the last plane or box step's marker: a new root node "cut" (identity) and
//           the source's root under it at the marker's inverse pose (scale 1); tear and hole keep the source's origin.
//   Section a renderer the cut changed gets inside "section"; a member renderer gets no inside and no section, and is never cut
//           by "clothes only".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace HS2Next.Scenes.Cut
{
    public sealed class CutNode
    {
        public string Name;
        public int Parent = -1;
        public CutVec P;
        public CutQuat R = CutQuat.Identity;
        public CutVec S = new CutVec(1, 1, 1);
        /// <summary>The adapter's own node (the main's node object), carried through to the piece.</summary>
        public object Tag;
        public CutNode Clone() => (CutNode)MemberwiseClone();
    }

    public sealed class CutRenderer
    {
        public int Node = -1;
        public bool Skinned;
        public int Mesh = -1;
        /// <summary>Node index per bind pose (skinned).</summary>
        public int[] Bones = Array.Empty<int>();
        public string Part;
        public bool Member;
        /// <summary>none | fill | section.</summary>
        public string Inside = CutInside.None;
        /// <summary>The adapter's own renderer (the main's renderer object), carried through to the piece.</summary>
        public object Tag;
        public CutRenderer Clone() => (CutRenderer)MemberwiseClone();
    }

    public static class CutInside
    {
        public const string None = "none", Fill = "fill", Section = "section";
    }

    /// <summary>A source bake as the procedure reads it.</summary>
    public sealed class CutInput
    {
        public readonly List<CutNode> Nodes = new List<CutNode>();
        public readonly List<CutRenderer> Renderers = new List<CutRenderer>();
        public readonly List<string> Fk = new List<string>();
        /// <summary>chara | centre | cut | item | …: the main's origin.</summary>
        public string Origin;
        /// <summary>A mesh by its index in the main (decoded on demand; null when its blob is missing).</summary>
        public Func<int, CutMesh> MeshOf;
    }

    /// <summary>One renderer of a piece: the source renderer, and either its source mesh unchanged or a new mesh.</summary>
    public sealed class CutPieceRenderer
    {
        public int SourceIndex;
        public CutRenderer Renderer;
        /// <summary>Null: the source mesh (Renderer.Mesh) is used unchanged.</summary>
        public CutMesh NewMesh;
        public bool Changed => NewMesh != null;
    }

    /// <summary>One piece: the result of a procedure for one keep value of its last step.</summary>
    public sealed class CutPiece
    {
        /// <summary>The keep value of the last step (front, back, inside, outside, open).</summary>
        public string Keep;
        /// <summary>The side word: front, back, inside, outside, torn, holed.</summary>
        public string Side;
        public List<CutNode> Nodes = new List<CutNode>();
        /// <summary>The source renderers kept, in source order (node indices already shifted for a re-rooted piece).</summary>
        public List<CutPieceRenderer> Renderers = new List<CutPieceRenderer>();
        public List<string> Fk = new List<string>();
        public string Origin;
        /// <summary>The piece's root in the source's root space (the marker's pose, scale 1); identity when not re-rooted.</summary>
        public CutXf RootOffset = CutXf.Identity;
        /// <summary>The pose the last step was decided at (null: the source's baked pose); written into the piece's nodes.</summary>
        public IDictionary<string, CutVec> Pose;
        public CutVec RootPos = CutVec.Zero;
        public CutQuat RootRot = CutQuat.Identity;
        public bool ReRooted;
        /// <summary>The index of the source's root among the piece's nodes (1 when re-rooted, else 0).</summary>
        public int SourceRoot;
        /// <summary>Source renderer indices the procedure changed (logged, V16).</summary>
        public List<int> CutRenderers = new List<int>();
        public float[] SectionColor;
        public bool OwnInside = true;
    }

    public static class CutRules
    {
        public const string RootName = "cut";

        /// <summary>A garment's part (clothes0–clothes7).</summary>
        public static bool IsClothes(string part) => part != null && part.StartsWith("clothes", StringComparison.Ordinal);
        /// <summary>A hair part (hair0–hair3).</summary>
        public static bool IsHair(string part) => part != null && part.StartsWith("hair", StringComparison.Ordinal);
        /// <summary>A section of this renderer uses its own material (garments and hair with own fabric inside on).</summary>
        public static bool SectionOwnMaterial(string part, bool ownInside) => ownInside && (IsClothes(part) || IsHair(part));

        /// <summary>The piece's FK list (V18, V20).</summary>
        public static List<string> PruneFk(IReadOnlyList<CutNode> nodes, IReadOnlyList<string> fk, IEnumerable<int> usedNodes, int root)
        {
            var result = new List<string>();
            var used = new HashSet<int>(usedNodes);
            if (fk != null && fk.Count > 0)
            {
                var byName = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < nodes.Count; i++) if (nodes[i].Name != null && !byName.ContainsKey(nodes[i].Name)) byName[nodes[i].Name] = i;
                var fkNode = new HashSet<int>();
                foreach (var n in fk) if (n != null && byName.TryGetValue(n, out int i)) fkNode.Add(i);
                var hit = new HashSet<int>();
                foreach (int u in used)
                {
                    int i = u, guard = 0;
                    while (i >= 0 && i < nodes.Count && guard++ <= nodes.Count)
                    {
                        if (fkNode.Contains(i)) { hit.Add(i); break; }
                        i = nodes[i].Parent;
                    }
                }
                foreach (var n in fk) if (n != null && byName.TryGetValue(n, out int i) && hit.Contains(i)) result.Add(n);
                return result;
            }
            // V20: the bones the kept vertices weigh on, names that occur once, not the root
            var count = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var n in nodes) if (n.Name != null) count[n.Name] = count.TryGetValue(n.Name, out int c) ? c + 1 : 1;
            var order = new List<int>(used);
            order.Sort();
            foreach (int i in order)
            {
                if (i < 0 || i >= nodes.Count || i == root || i == 0) continue;
                var name = nodes[i].Name;
                if (name != null && count[name] == 1) result.Add(name);
            }
            return result;
        }

        /// <summary>The nodes a kept renderer moves with: the bones its mesh's vertices weigh on (weights > 0), or its node.</summary>
        public static void UsedNodes(CutRenderer r, CutMesh mesh, HashSet<int> into, bool skinnedOnly = false)
        {
            if (r.Skinned && mesh != null && mesh.HasWeights)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < mesh.BoneWeights.Length; i++)
                    if (mesh.BoneWeights[i] > 0)
                    {
                        int b = mesh.BoneIndices[i];
                        if (seen.Add(b) && b < r.Bones.Length && r.Bones[b] >= 0) into.Add(r.Bones[b]);
                    }
            }
            else if (!skinnedOnly && r.Node >= 0) into.Add(r.Node);
        }

        /// <summary>The nodes of a piece re-rooted at a pose of the source's root space (scale 1).</summary>
        public static List<CutNode> ReRoot(IReadOnlyList<CutNode> nodes, CutVec pos, CutQuat rot, IReadOnlyList<string> fk)
        {
            var list = new List<CutNode>(nodes.Count + 1);
            string rootName = RootName;
            if (fk != null) foreach (var n in fk) if (n == RootName) rootName = RootName + "~0";
            list.Add(new CutNode { Name = rootName, Parent = -1, P = CutVec.Zero, R = CutQuat.Identity, S = new CutVec(1, 1, 1), Tag = null });
            var inv = rot.Normalized.Inverse;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i].Clone();
                n.Parent = nodes[i].Parent < 0 ? 0 : nodes[i].Parent + 1;
                if (i == 0)
                {
                    // the source's root under the marker: T_m⁻¹ × TRS(root)
                    n.P = inv.Rotate(nodes[i].P - pos);
                    n.R = (inv * nodes[i].R).Normalized;
                }
                list.Add(n);
            }
            return list;
        }
    }

    public sealed class CutRun
    {
        public CutInput Source;
        /// <summary>FK values at the cut (local Euler degrees) by bone name; null: the baked pose.</summary>
        public IDictionary<string, CutVec> Pose;
        public IReadOnlyList<CutStepDef> Steps;
        /// <summary>The keep values of the last step's pieces; null: the last step's own keep.</summary>
        public string[] LastKeeps;
        public float[] SectionColor = CutShapeInfo.DefaultSection;
        public bool OwnInside = true;

        // results
        public readonly List<CutPiece> Pieces = new List<CutPiece>();
        /// <summary>Finding codes met (cut.range, cut.meshMissing); a refusal is Error.</summary>
        public readonly List<string> Findings = new List<string>();
        /// <summary>cut.badStep when a step cannot be run; null when the run is good.</summary>
        public string Error;
        public int MeshesWorked, MeshesWhole, Crossings, RefineAdded;
        public bool RefineLimited;
        public double WorstCrossing, Milliseconds;
    }

    public static class CutProcedure
    {
        static int runs;
        /// <summary>Procedures run in this process (every Run or Steps to its end): a load builds from the stored result
        /// and runs none.</summary>
        public static int Runs => Volatile.Read(ref runs);

        /// <summary>Runs the procedure to the end; false with run.Error when a step cannot be run.</summary>
        public static bool Run(CutRun run)
        {
            var e = Steps(run);
            while (e.MoveNext()) { }
            return run.Error == null;
        }

        /// <summary>Root-space matrices of the source's nodes in a pose.</summary>
        public static CutXf[] NodeMatrices(CutInput src, IDictionary<string, CutVec> pose)
        {
            var fk = new HashSet<string>(src.Fk, StringComparer.Ordinal);
            var w = new CutXf[src.Nodes.Count];
            var done = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < src.Nodes.Count; i++)
            {
                var n = src.Nodes[i];
                var r = n.R;
                // the first node of a name is the one item FK drives (names of FK bones are unique in a bake)
                if (pose != null && n.Name != null && fk.Contains(n.Name) && done.Add(n.Name) && pose.TryGetValue(n.Name, out var e))
                    r = CutQuat.Euler(e.X, e.Y, e.Z);
                var local = CutXf.TRS(n.P, r.Normalized, n.S);
                w[i] = n.Parent >= 0 && n.Parent < i ? w[n.Parent] * local : local;
            }
            return w;
        }

        /// <summary>The poser of a renderer's mesh in the node matrices.</summary>
        public static CutPoser PoserOf(CutRenderer r, CutMesh m, CutXf[] w)
        {
            var nodeM = r.Node >= 0 && r.Node < w.Length ? w[r.Node] : CutXf.Identity;
            if (!r.Skinned || m == null || !m.HasWeights || m.BindPoseCount == 0) return new CutPoser(nodeM);
            var bones = new CutXf[m.BindPoseCount];
            for (int j = 0; j < bones.Length; j++)
            {
                int b = j < r.Bones.Length ? r.Bones[j] : -1;
                var bm = b >= 0 && b < w.Length ? w[b] : nodeM;
                bones[j] = bm * CutXf.FromColumnMajor(m.BindPoses, 16 * j);
            }
            return new CutPoser(bones);
        }

        /// <summary>Region index of a keep value: plane front 0, back 1; box inside 0, outside 1; opening open 1.</summary>
        public static int RegionOf(CutKind k, string keep) => k == CutKind.Plane ? (keep == CutShapeInfo.Front ? 0 : 1) : keep == CutShapeInfo.Inside ? 0 : 1;

        sealed class RState
        {
            public bool Alive = true;
            public CutMesh Mesh;      // null: the source mesh
            public bool Changed;
        }

        enum Outcome { Unchanged, Dropped, New }

        /// <summary>The procedure as a coroutine: one renderer worked per MoveNext.</summary>
        public static IEnumerator<bool> Steps(CutRun run)
        {
            var sw = Stopwatch.StartNew();
            var src = run.Source;
            var steps = run.Steps;
            if (steps == null || steps.Count == 0) { run.Error = "cut.badStep"; yield break; }
            foreach (var s in steps)
                if (s == null || !s.Runnable) { run.Error = "cut.badStep"; yield break; }
            var lastKeeps = run.LastKeeps ?? new[] { steps[steps.Count - 1].Keep };
            foreach (var k in lastKeeps)
                if (!CutShapeInfo.KeepValid(steps[steps.Count - 1].Kind, k)) { run.Error = "cut.badStep"; yield break; }
            foreach (var s in steps)
                if (s.Ragged != null) { s.Ragged.Applied(out bool oor); if (oor && !run.Findings.Contains("cut.range")) run.Findings.Add("cut.range"); }
            Interlocked.Increment(ref runs);

            // each step is decided at its own pose: the step's, else the previous step's, else the run's
            IDictionary<string, CutVec> pose = run.Pose;
            CutXf[] w = null;
            IDictionary<string, CutVec> wPose = null;
            int nr = src.Renderers.Count;
            var state = new RState[nr];
            for (int i = 0; i < nr; i++) state[i] = new RState();
            var sourceMeshes = new Dictionary<int, CutMesh>();
            CutMesh SourceMesh(int idx)
            {
                if (idx < 0) return null;
                if (!sourceMeshes.TryGetValue(idx, out var m)) { m = src.MeshOf?.Invoke(idx); sourceMeshes[idx] = m; }
                return m;
            }

            // the last step's per-keep outcome of each renderer
            int nk = lastKeeps.Length;
            var lastOut = new Outcome[nk][];
            var lastMesh = new CutMesh[nk][];
            for (int p = 0; p < nk; p++) { lastOut[p] = new Outcome[nr]; lastMesh[p] = new CutMesh[nr]; }

            for (int si = 0; si < steps.Count; si++)
            {
                var step = steps[si];
                var surf = CutSurface.Of(step);
                if (step.Pose != null) pose = step.Pose;
                if (w == null || !ReferenceEquals(pose, wPose)) { w = NodeMatrices(src, pose); wPose = pose; }
                bool last = si == steps.Count - 1;
                var keeps = last ? lastKeeps : new[] { step.Keep };
                bool clothesOnly = step.ClothesOnly;
                if (clothesOnly)
                {
                    bool any = false;
                    for (int i = 0; i < nr; i++) if (state[i].Alive && CutRules.IsClothes(src.Renderers[i].Part)) { any = true; break; }
                    clothesOnly = any;
                }
                for (int i = 0; i < nr; i++)
                {
                    if (!state[i].Alive)
                    {
                        if (last) for (int p = 0; p < nk; p++) lastOut[p][i] = Outcome.Dropped;
                        continue;
                    }
                    var r = src.Renderers[i];
                    var mesh = state[i].Mesh ?? SourceMesh(r.Mesh);
                    var outs = new Outcome[2];
                    var news = new CutMesh[2];
                    bool worked = false;
                    if (mesh == null)
                    {
                        if (!run.Findings.Contains("cut.meshMissing")) run.Findings.Add("cut.meshMissing");
                        outs[0] = outs[1] = Outcome.Unchanged;
                    }
                    else
                    {
                        var poser = PoserOf(r, mesh, w);
                        var verts = new CutVerts(mesh, poser);
                        int whole = clothesOnly && (r.Member || !CutRules.IsClothes(r.Part)) ? WholeByCentre(verts, surf) : WholeByPoints(verts, surf);
                        if (whole >= 0)
                        {
                            outs[whole] = Outcome.Unchanged;
                            outs[1 - whole] = Outcome.Dropped;
                            run.MeshesWhole++;
                        }
                        else
                        {
                            worked = true;
                            run.MeshesWorked++;
                            var subs = Normalized(mesh.SubMeshes);
                            if (CutRefine.Applies(surf))
                            {
                                var rs = CutRefine.Run(verts, subs, surf);
                                run.RefineAdded += rs.Added;
                                run.RefineLimited |= rs.Limited;
                            }
                            var res = CutClip.Run(verts, subs, surf);
                            run.Crossings += res.Crossings;
                            if (res.WorstCrossing > run.WorstCrossing) run.WorstCrossing = res.WorstCrossing;
                            for (int g = 0; g < 2; g++)
                            {
                                bool need = false;
                                foreach (var kp in keeps) if (RegionOf(step.Kind, kp) == g) need = true;
                                if (!need) { outs[g] = Outcome.Dropped; continue; }
                                if (res.Whole[g]) { outs[g] = Outcome.Unchanged; continue; }
                                bool empty = true;
                                foreach (var sm in res.Regions[g]) if (sm.Indices.Length > 0) { empty = false; break; }
                                if (empty) { outs[g] = Outcome.Dropped; continue; }
                                outs[g] = Outcome.New;
                                news[g] = verts.Compact(res.Regions[g]);
                            }
                        }
                    }
                    if (last)
                        for (int p = 0; p < nk; p++)
                        {
                            int g = RegionOf(step.Kind, keeps[p]);
                            lastOut[p][i] = outs[g];
                            lastMesh[p][i] = outs[g] == Outcome.New ? news[g] : null;
                        }
                    else
                    {
                        int g = RegionOf(step.Kind, step.Keep);
                        if (outs[g] == Outcome.Dropped) state[i].Alive = false;
                        else if (outs[g] == Outcome.New) { state[i].Mesh = news[g]; state[i].Changed = true; }
                    }
                    if (worked) yield return true;
                }
            }

            // re-rooting: the last plane or box step
            CutStepDef rootStep = null;
            for (int si = steps.Count - 1; si >= 0; si--) if (CutShapeInfo.ReRoots(steps[si].Kind)) { rootStep = steps[si]; break; }
            var lastStep = steps[steps.Count - 1];
            for (int p = 0; p < nk; p++)
            {
                var piece = new CutPiece
                {
                    Keep = lastKeeps[p],
                    Side = CutShapeInfo.PieceName(lastStep.Kind, lastKeeps[p]),
                    SectionColor = run.SectionColor ?? CutShapeInfo.DefaultSection,
                    OwnInside = run.OwnInside,
                    Origin = src.Origin,
                };
                var used = new HashSet<int>();
                for (int i = 0; i < nr; i++)
                {
                    var o = lastOut[p][i];
                    if (o == Outcome.Dropped) continue;
                    var r = src.Renderers[i];
                    var finalMesh = o == Outcome.New ? lastMesh[p][i] : state[i].Mesh;
                    bool changed = o == Outcome.New || state[i].Changed;
                    var pr = new CutPieceRenderer { SourceIndex = i, Renderer = r.Clone(), NewMesh = finalMesh };
                    if (changed)
                    {
                        piece.CutRenderers.Add(i);
                        if (!r.Member) pr.Renderer.Inside = CutInside.Section;
                    }
                    CutRules.UsedNodes(r, finalMesh ?? SourceMesh(r.Mesh), used);
                    piece.Renderers.Add(pr);
                }
                piece.Fk = CutRules.PruneFk(src.Nodes, src.Fk, used, 0);
                if (rootStep != null)
                {
                    piece.ReRooted = true;
                    piece.SourceRoot = 1;
                    piece.RootPos = rootStep.Pos;
                    piece.RootRot = rootStep.Rot.Normalized;
                    piece.RootOffset = CutXf.TRS(piece.RootPos, piece.RootRot, new CutVec(1, 1, 1));
                    piece.Nodes = CutRules.ReRoot(src.Nodes, piece.RootPos, piece.RootRot, piece.Fk);
                    piece.Origin = CutRules.RootName;
                    foreach (var pr in piece.Renderers)
                    {
                        var r = pr.Renderer;
                        if (r.Node >= 0) r.Node++;
                        var b = new int[r.Bones.Length];
                        for (int j = 0; j < b.Length; j++) b[j] = r.Bones[j] >= 0 ? r.Bones[j] + 1 : r.Bones[j];
                        r.Bones = b;
                    }
                }
                else
                {
                    foreach (var n in src.Nodes) piece.Nodes.Add(n.Clone());
                }
                // the piece shows the pose its last step was decided at: the pose's values written into its nodes (the source's FK bones,
                // the first node of a name, as the pose itself acts), so a bone it does not keep as FK stays where the cut saw it
                if (pose != null)
                {
                    var fk = new HashSet<string>(src.Fk, StringComparer.Ordinal);
                    var done = new HashSet<string>(StringComparer.Ordinal);
                    int shiftN = piece.ReRooted ? 1 : 0;
                    for (int i = 0; i < src.Nodes.Count; i++)
                    {
                        var name = src.Nodes[i].Name;
                        if (name == null || !fk.Contains(name) || !done.Add(name) || !pose.TryGetValue(name, out var e)) continue;
                        piece.Nodes[i + shiftN].R = CutQuat.Euler(e.X, e.Y, e.Z).Normalized;
                    }
                    piece.Pose = pose;
                }
                run.Pieces.Add(piece);
            }
            run.Milliseconds = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>The region a renderer goes to whole by its points (−1: it must be clipped).</summary>
        static int WholeByPoints(CutVerts v, CutSurface s)
        {
            int n = v.Source.VertexCount;
            if (n == 0) return s.IsPlane ? 0 : 1;
            double m = s.Margin;
            if (s.IsPlane)
            {
                bool allFront = true, allBack = true;
                for (int i = 0; i < n && (allFront || allBack); i++)
                {
                    double f = s.Clean(0, v.PosedAt(i));
                    if (!(f > m)) allFront = false;
                    if (!(f < -m)) allBack = false;
                }
                return allFront ? 0 : allBack ? 1 : -1;
            }
            // the box itself, or the box that holds an opening (half sizes hx, hy, hz)
            bool allInside = s.Kind == CutKind.Box;
            var beyond = new bool[6] { true, true, true, true, true, true };
            for (int i = 0; i < n; i++)
            {
                s.Local(v.PosedAt(i), out double u, out double w, out double vv);
                double f0 = u - s.Hx, f1 = -u - s.Hx, f2 = w - s.Hy, f3 = -w - s.Hy, f4 = vv - s.Hz, f5 = -vv - s.Hz;
                if (allInside && !(f0 <= -m && f1 <= -m && f2 <= -m && f3 <= -m && f4 <= -m && f5 <= -m)) allInside = false;
                if (!(f0 > m)) beyond[0] = false;
                if (!(f1 > m)) beyond[1] = false;
                if (!(f2 > m)) beyond[2] = false;
                if (!(f3 > m)) beyond[3] = false;
                if (!(f4 > m)) beyond[4] = false;
                if (!(f5 > m)) beyond[5] = false;
            }
            if (allInside) return 0;
            foreach (bool b in beyond) if (b) return 1;
            return -1;
        }

        /// <summary>"Clothes only": the region of the centre of the renderer's posed bounds; an opening leaves it as it is.</summary>
        static int WholeByCentre(CutVerts v, CutSurface s)
        {
            if (s.IsOpening) return 1;
            int n = v.Source.VertexCount;
            if (n == 0) return s.IsPlane ? 0 : 1;
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                var p = v.PosedAt(i);
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
                x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
            }
            int c = s.Classify(new CutVec(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)));
            return s.IsPlane ? c : c < 0 ? 0 : 1;
        }

        /// <summary>The sub-meshes as the clip takes them: quads as two triangles each, a line strip as lines.</summary>
        public static List<CutSubMesh> Normalized(List<CutSubMesh> subs)
        {
            var l = new List<CutSubMesh>(subs.Count);
            foreach (var s in subs)
            {
                if (s.Topology == CutTopology.Quads)
                {
                    var ix = new List<int>(s.Indices.Length / 4 * 6);
                    for (int q = 0; q + 3 < s.Indices.Length; q += 4)
                    {
                        ix.Add(s.Indices[q]); ix.Add(s.Indices[q + 1]); ix.Add(s.Indices[q + 2]);
                        ix.Add(s.Indices[q]); ix.Add(s.Indices[q + 2]); ix.Add(s.Indices[q + 3]);
                    }
                    l.Add(new CutSubMesh { Topology = CutTopology.Triangles, Indices = ix.ToArray() });
                }
                else if (s.Topology == CutTopology.LineStrip)
                {
                    var ix = new List<int>();
                    for (int q = 0; q + 1 < s.Indices.Length; q++) { ix.Add(s.Indices[q]); ix.Add(s.Indices[q + 1]); }
                    l.Add(new CutSubMesh { Topology = CutTopology.Lines, Indices = ix.ToArray() });
                }
                else l.Add(s.Clone());
            }
            return l;
        }
    }
}
