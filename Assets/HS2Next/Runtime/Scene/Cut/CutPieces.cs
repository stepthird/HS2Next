// The pieces of a cut as bakes, and the procedure run from its stored block (
// 8), Unity-free. A cut's procedure is the item's `cut` block: the source bake (identity and blobs), the source's FK pose at
// the cut, the steps from that first source and the section. It is always run from the block as stored (floats as the file
// holds them), so the result of a new cut and of every later run of the same block are equal bit for bit.
//   A piece is a bake of kind "piece": the source main copied (unknown members kept) with the piece's nodes (re-rooted for a
//   plane or box), its FK list, the renderers that are left (node, bone, root and anchor indices moved with a new root; inside
//   "section" on a renderer the cut changed, never on a member), the meshes they draw (a changed mesh as a new mesh blob, an
//   unchanged one as the source's blob), only the materials those renderers use and the textures those materials use, the
//   insides with the piece's section colour and "own fabric inside". Its identity is given: new for a new piece, the piece's
//   own when its procedure runs again; its time is the source's, so a run again gives the same main.
//   A section draws its renderer's own material for garments and hair when "own fabric inside" is on, else the section colour.
//   Each step is decided at its own pose (its `pose`, else the previous step's, else the block's): a re-cut appends its step with
//   the pose the piece shows when that differs from its last step's (Chain, ReCutPose).
//   A run decodes the meshes its source's renderers draw once, at the first need, in parallel on workers (the blobs fetched on the
//   calling thread). On workers, the new meshes of every piece are encoded in one parallel loop, then each piece's main is made from its
//   own copy of the source main (a member-by-member deep copy, what a parse of the main's canonical JSON gives) and written after its
//   meshes: the coroutine yields while they run and Run waits for them; the result is assembled in a fixed order, so the bits do not
//   depend on the threads.
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using HS2Next.Save;
using HS2Next.Scenes.Bake;

namespace HS2Next.Scenes.Cut
{
    /// <summary>One piece of a run: its procedure block, its result as a main and blobs, what the cut changed.</summary>
    public sealed class CutPieceResult
    {
        public string Keep, Side;
        /// <summary>The piece's procedure (the source, the pose, the steps with this piece's keep, the section).</summary>
        public ItemCut Cut;
        /// <summary>The piece's bake block (its id, main and data).</summary>
        public ItemBake Bake;
        public BakeMain Main;
        public byte[] MainBlob;
        public string MainSha;
        /// <summary>The blobs this piece made (its main and its changed meshes) by SHA-256; the rest are the source's.</summary>
        public readonly Dictionary<string, byte[]> NewBlobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>The source's renderer indices the cut changed (logged).</summary>
        public List<int> CutRenderers = new List<int>();
        /// <summary>No renderer is left.</summary>
        public bool Empty;
        /// <summary>The piece's root in the source's root space (identity when it keeps the source's origin).</summary>
        public CutXf RootOffset = CutXf.Identity;
        public bool ReRooted;
    }

    public sealed class CutOutcome
    {
        public readonly List<CutPieceResult> Pieces = new List<CutPieceResult>();
        /// <summary>Null when the run is good; else a finding code (cut.badStep, bake.missing, bake.blobCorrupt).</summary>
        public string Error, ErrorText;
        /// <summary>Codes met on the way (cut.range, cut.meshMissing, cut.limit).</summary>
        public readonly List<string> Findings = new List<string>();
        public CutRun Run;
        public double Milliseconds;
        /// <summary>Diagnostics: the source's meshes decoded (milliseconds on the calling thread, meshes), and the pieces' blobs encoded on
        /// workers (milliseconds from their start to their end); -1 when that part did not run.</summary>
        public double DecodeMilliseconds = -1, EncodeMilliseconds = -1;
        public int MeshesDecoded;
        /// <summary>While the pieces' blobs are encoded on workers: the work a caller may wait for instead of stepping again.</summary>
        public Task Pending;
    }

    public static class CutPieces
    {
        // ------------------------------------------------------------------ the block and the run's values

        /// <summary>A stored step as the procedure runs it (the floats exactly).</summary>
        public static CutStepDef StepOf(CutStep s)
        {
            var d = new CutStepDef
            {
                Shape = s.Shape,
                Pos = new CutVec(s.Pos.X, s.Pos.Y, s.Pos.Z),
                Rot = new CutQuat(s.Rot.X, s.Rot.Y, s.Rot.Z, s.Rot.W),
                Size = new CutVec(s.Size.X, s.Size.Y, s.Size.Z),
                Keep = s.Keep,
                ClothesOnly = s.ClothesOnly,
            };
            if (s.Ragged != null) d.Ragged = new CutRagParams { Amp = s.Ragged.Amp, Tooth = s.Ragged.Tooth, Random = s.Ragged.Random, Seed = s.Ragged.Seed };
            d.Pose = PoseOf(s.Pose);
            return d;
        }

        /// <summary>A stored pose as the procedure takes it (null stays null).</summary>
        public static Dictionary<string, CutVec> PoseOf(IDictionary<string, Vec3> pose)
        {
            if (pose == null) return null;
            var d = new Dictionary<string, CutVec>(StringComparer.Ordinal);
            foreach (var kv in pose) d[kv.Key] = new CutVec(kv.Value.X, kv.Value.Y, kv.Value.Z);
            return d;
        }

        /// <summary>A step as the file stores it (floats).</summary>
        public static CutStep BlockOf(CutStepDef d)
        {
            var s = new CutStep
            {
                Shape = d.Shape,
                Pos = new Vec3((float)d.Pos.X, (float)d.Pos.Y, (float)d.Pos.Z),
                Rot = new Quat4((float)d.Rot.X, (float)d.Rot.Y, (float)d.Rot.Z, (float)d.Rot.W),
                Size = new Vec3((float)d.Size.X, (float)d.Size.Y, (float)d.Size.Z),
                Keep = d.Keep,
                ClothesOnly = d.ClothesOnly,
            };
            if (d.Ragged != null) s.Ragged = new CutRagged { Amp = (float)d.Ragged.Amp, Tooth = (float)d.Ragged.Tooth, Random = (float)d.Ragged.Random, Seed = d.Ragged.Seed };
            if (d.Pose != null)
            {
                s.Pose = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
                foreach (var kv in d.Pose) s.Pose[kv.Key] = new Vec3((float)kv.Value.X, (float)kv.Value.Y, (float)kv.Value.Z);
            }
            return s;
        }

        /// <summary>A new procedure: the source, the pose, the steps (the last one's keep is set per piece) and the section.</summary>
        public static ItemCut NewBlock(CutSource source, IDictionary<string, Vec3> pose, IEnumerable<CutStep> steps, Vec3 color, bool ownInside)
        {
            var c = new ItemCut { Source = source, Section = new CutSection { Color = color, OwnInside = ownInside } };
            if (pose != null) c.Pose = new SortedDictionary<string, Vec3>(pose, StringComparer.Ordinal);
            foreach (var s in steps) c.Steps.Add(s);
            return c;
        }

        /// <summary>A re-cut of a piece: a copy of its procedure with the new step appended (a re-cut stays a
        /// procedure on the first source). <paramref name="pose"/> is the pose the piece shows now in the source's names
        /// (<see cref="ReCutPose"/>); the step carries it only when it differs from the pose the piece's last step was decided at
        /// (null counts as the source's baked pose, an empty map). The block's own pose and the earlier steps' stay, so posing a piece
        /// and cutting it again never moves an earlier cut. A section given replaces the piece's (one colour per piece).</summary>
        public static ItemCut Chain(ItemCut piece, CutStep step, IDictionary<string, Vec3> pose, CutSection section)
        {
            var c = piece.Clone();
            var s = step;
            s.Pose = null;
            var before = c.Steps.Count > 0 ? c.PoseOf(c.Steps.Count - 1) : c.Pose;
            if (!SamePose(before, pose))
                s.Pose = new SortedDictionary<string, Vec3>(pose ?? new SortedDictionary<string, Vec3>(), StringComparer.Ordinal);
            c.Steps.Add(s);
            if (section != null) c.Section = new CutSection { Color = section.Color, OwnInside = section.OwnInside };
            return c;
        }

        /// <summary>The pose a piece shows now, in the source's bone names: the pose its last step was decided at (its nodes hold it),
        /// with the piece's own FK values over it when its FK is on. A piece keeps the source's bone names (its nodes and FK list are the
        /// source's, renamed only at the bake), so nothing is renamed back.</summary>
        public static SortedDictionary<string, Vec3> ReCutPose(ItemCut piece, bool fkOn, IDictionary<string, Vec3> fkValues)
        {
            var last = piece == null ? null : piece.Steps.Count > 0 ? piece.PoseOf(piece.Steps.Count - 1) : piece.Pose;
            var p = last != null ? new SortedDictionary<string, Vec3>(last, StringComparer.Ordinal) : new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
            if (fkOn && fkValues != null) foreach (var kv in fkValues) p[kv.Key] = kv.Value;
            return p.Count == 0 && last == null ? null : p;
        }

        /// <summary>Two poses are the same: the same bones with the same float values (null and an empty map both are the baked pose).</summary>
        public static bool SamePose(IDictionary<string, Vec3> a, IDictionary<string, Vec3> b)
        {
            int na = a?.Count ?? 0, nb = b?.Count ?? 0;
            if (na != nb) return false;
            if (na == 0) return true;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out var v) || !v.Equals(kv.Value)) return false;
            return true;
        }

        /// <summary>The local matrix of the source's root under a piece's root (identity unless the piece was re-rooted): the
        /// source's root space of a piece item is its node's world × this.</summary>
        public static CutXf SourceRootLocal(BakeMain piece)
        {
            if (piece == null || piece.Origin != BakeKinds.OriginCut || piece.Nodes.Count < 2) return CutXf.Identity;
            var n = piece.Nodes[1];
            return CutXf.TRS(new CutVec(n.P.X, n.P.Y, n.P.Z), new CutQuat(n.R.X, n.R.Y, n.R.Z, n.R.W).Normalized, new CutVec(n.S.X, n.S.Y, n.S.Z));
        }

        // ------------------------------------------------------------------ the source

        public static CutMesh MeshOf(BakeMeshData d)
        {
            var m = new CutMesh
            {
                VertexCount = d.VertexCount,
                Positions = d.Positions,
                Normals = d.Normals,
                Tangents = d.Tangents,
                Colors = d.Colors,
                BoneIndices = d.BoneIndices,
                BoneWeights = d.BoneWeights,
                BindPoses = d.BindPoses ?? Array.Empty<float>(),
            };
            for (int c = 0; c < CutMesh.Channels; c++)
                if (d.Uv[c] != null && d.UvDims[c] > 0) { m.UvDims[c] = d.UvDims[c]; m.Uvs[c] = d.Uv[c]; }
            foreach (var s in d.SubMeshes) m.SubMeshes.Add(new CutSubMesh { Topology = s.Topology, Indices = s.Indices });
            return m;
        }

        public static BakeMeshData DataOf(CutMesh m)
        {
            var d = new BakeMeshData
            {
                VertexCount = m.VertexCount,
                Positions = m.Positions,
                Normals = m.Normals,
                Tangents = m.Tangents,
                Colors = m.Colors,
                BoneIndices = m.BoneIndices,
                BoneWeights = m.BoneWeights,
                BindPoses = m.BindPoses ?? Array.Empty<float>(),
            };
            for (int c = 0; c < CutMesh.Channels; c++) if (m.UvDims[c] > 0 && m.Uvs[c] != null) d.SetUv(c, m.UvDims[c], m.Uvs[c]);
            foreach (var s in m.SubMeshes) d.SubMeshes.Add(new BakeSubMesh { Topology = s.Topology, Indices = s.Indices });
            return d;
        }

        /// <summary>The procedure's view of a source main; meshes decoded on demand from blobOf (SHA-256 → blob, null when absent).</summary>
        public static CutInput InputOf(BakeMain main, Func<string, byte[]> blobOf, List<string> findings = null)
        {
            var inp = new CutInput { Origin = main.Origin };
            for (int i = 0; i < main.Nodes.Count; i++)
            {
                var n = main.Nodes[i];
                inp.Nodes.Add(new CutNode
                {
                    Name = n.Name, Parent = n.Parent, Tag = i,
                    P = new CutVec(n.P.X, n.P.Y, n.P.Z), R = new CutQuat(n.R.X, n.R.Y, n.R.Z, n.R.W), S = new CutVec(n.S.X, n.S.Y, n.S.Z),
                });
            }
            inp.Fk.AddRange(main.Fk);
            for (int i = 0; i < main.Renderers.Count; i++)
            {
                var r = main.Renderers[i];
                inp.Renderers.Add(new CutRenderer
                {
                    Node = r.Node, Skinned = r.Skinned, Mesh = r.Mesh, Bones = (int[])(r.Bones ?? Array.Empty<int>()).Clone(),
                    Part = r.Part, Member = r.Member, Inside = r.Inside ?? CutInside.None, Tag = i,
                });
            }
            inp.MeshOf = idx =>
            {
                if (idx < 0 || idx >= main.Meshes.Count) return null;
                var b = blobOf?.Invoke(main.Meshes[idx].Blob);
                if (b == null) return null;
                try { return MeshOf(BakeMeshData.FromBlob(b)); }
                catch (BakeFormatException)
                {
                    if (findings != null && !findings.Contains(BakeFindings.BlobCorrupt)) findings.Add(BakeFindings.BlobCorrupt);
                    return null;
                }
            };
            return inp;
        }

        /// <summary><see cref="InputOf"/> whose meshes are decoded once, at the first need: every mesh a renderer of the main draws, in parallel
        /// on workers, the blobs fetched on the calling thread in renderer order; a mesh no renderer draws is not decoded. A missing blob gives
        /// null and a blob that does not read gives null and bake.blobCorrupt once, as the decode on demand does.</summary>
        static CutInput InputDecodedOnce(BakeMain main, Func<string, byte[]> blobOf, List<string> findings, CutOutcome o)
        {
            var inp = InputOf(main, blobOf, findings);
            CutMesh[] decoded = null;
            inp.MeshOf = idx =>
            {
                if (idx < 0 || idx >= main.Meshes.Count) return null;
                if (decoded == null) decoded = DecodeDrawn(main, blobOf, findings, o);
                return decoded[idx];
            };
            return inp;
        }

        static CutMesh[] DecodeDrawn(BakeMain main, Func<string, byte[]> blobOf, List<string> findings, CutOutcome o)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var decoded = new CutMesh[main.Meshes.Count];
            var order = new List<int>();
            var seen = new bool[main.Meshes.Count];
            foreach (var r in main.Renderers)
                if (r != null && r.Mesh >= 0 && r.Mesh < seen.Length && !seen[r.Mesh]) { seen[r.Mesh] = true; order.Add(r.Mesh); }
            var blobs = new byte[order.Count][];
            for (int i = 0; i < order.Count; i++) blobs[i] = blobOf?.Invoke(main.Meshes[order[i]].Blob);
            var corrupt = new bool[order.Count];
            var thrown = new Exception[order.Count];
            Parallel.For(0, order.Count, i =>
            {
                if (blobs[i] == null) return;
                try { decoded[order[i]] = MeshOf(BakeMeshData.FromBlob(blobs[i])); }
                catch (BakeFormatException) { corrupt[i] = true; }
                catch (Exception e) { thrown[i] = e; }
            });
            for (int i = 0; i < order.Count; i++)
            {
                if (thrown[i] != null) ExceptionDispatchInfo.Capture(thrown[i]).Throw();
                if (corrupt[i] && findings != null && !findings.Contains(BakeFindings.BlobCorrupt)) findings.Add(BakeFindings.BlobCorrupt);
            }
            if (o != null) { o.DecodeMilliseconds = sw.Elapsed.TotalMilliseconds; o.MeshesDecoded = order.Count; }
            return decoded;
        }

        // ------------------------------------------------------------------ runs

        /// <summary>Runs a procedure block on its source (the main and a blob getter by SHA-256): one piece per keep value of
        /// the last step (null: the block's own keep). Ids: one per piece, new for a new cut, the piece's own when run again.</summary>
        public static CutOutcome Run(ItemCut cut, BakeMain source, Func<string, byte[]> blobOf, string[] lastKeeps, IReadOnlyList<string> ids)
        {
            var e = Steps(cut, source, blobOf, lastKeeps, ids, out var outcome);
            while (e.MoveNext()) outcome.Pending?.Wait();
            return outcome;
        }

        /// <summary>The run as a coroutine (one mesh per MoveNext); the outcome is complete when it ends.</summary>
        public static IEnumerator<bool> Steps(ItemCut cut, BakeMain source, Func<string, byte[]> blobOf, string[] lastKeeps, IReadOnlyList<string> ids, out CutOutcome outcome)
        {
            outcome = new CutOutcome();
            return StepsCore(cut, source, blobOf, lastKeeps, ids, outcome);
        }

        static IEnumerator<bool> StepsCore(ItemCut cut, BakeMain source, Func<string, byte[]> blobOf, string[] lastKeeps, IReadOnlyList<string> ids, CutOutcome o)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (cut == null || source == null) { o.Error = BakeFindings.Missing; o.ErrorText = "no source"; yield break; }
            string problem = cut.Problem();
            if (problem != null || cut.Steps.Count == 0) { o.Error = BakeFindings.CutBadStep; o.ErrorText = problem ?? "no step"; yield break; }
            if (cut.Steps.Count > BakeLimits.MaxSteps) o.Findings.Add(BakeFindings.CutLimit);
            var steps = new List<CutStepDef>();
            foreach (var s in cut.AppliedSteps) steps.Add(StepOf(s));
            var keeps = lastKeeps ?? new[] { steps[steps.Count - 1].Keep };
            if (ids == null || ids.Count < keeps.Length) { o.Error = BakeFindings.CutBadStep; o.ErrorText = "an id per piece"; yield break; }
            Dictionary<string, CutVec> pose = null;
            if (cut.Pose != null)
            {
                pose = new Dictionary<string, CutVec>(StringComparer.Ordinal);
                foreach (var kv in cut.Pose) pose[kv.Key] = new CutVec(kv.Value.X, kv.Value.Y, kv.Value.Z);
            }
            var color = cut.Section?.Color ?? CutSectionColors.DarkRed;
            var run = new CutRun
            {
                Source = InputDecodedOnce(source, blobOf, o.Findings, o),
                Pose = pose,
                Steps = steps,
                LastKeeps = keeps,
                SectionColor = new[] { color.X, color.Y, color.Z },
                OwnInside = cut.Section?.OwnInside ?? true,
            };
            o.Run = run;
            var e = CutProcedure.Steps(run);
            while (e.MoveNext()) yield return true;
            if (run.Error != null) { o.Error = run.Error; o.ErrorText = "a step cannot be run"; yield break; }
            foreach (var f in run.Findings) if (!o.Findings.Contains(f)) o.Findings.Add(f);
            // the pieces' blobs are encoded on workers; assembled in a fixed order, so the bits do not depend on the threads
            var results = new CutPieceResult[run.Pieces.Count];
            var pieceIds = new string[run.Pieces.Count];
            for (int p = 0; p < run.Pieces.Count; p++)
            {
                var piece = run.Pieces[p];
                var r = new CutPieceResult
                {
                    Keep = piece.Keep, Side = piece.Side, CutRenderers = piece.CutRenderers, Empty = piece.Renderers.Count == 0,
                    RootOffset = piece.RootOffset, ReRooted = piece.ReRooted,
                };
                r.Cut = cut.Clone();
                r.Cut.Steps[r.Cut.Steps.Count - 1].Keep = piece.Keep;
                results[p] = r;
                pieceIds[p] = ids[p];
            }
            var pieces = run.Pieces.ToArray();
            var all = Task.Run(() => EncodePieces(source, pieces, results, pieceIds, o));
            o.Pending = all;
            while (!all.IsCompleted) yield return true;
            o.Pending = null;
            if (all.IsFaulted) ExceptionDispatchInfo.Capture(all.Exception.InnerException ?? all.Exception).Throw();
            o.Pieces.AddRange(results);
            o.Milliseconds = sw.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------------ the source main's copies

        /// <summary>A deep copy of a main, member by member: what <see cref="BakeMain.Clone"/> (a parse of the main's canonical JSON) gives, without the JSON.
        /// Every member is copied (lists in their order, arrays and the material values as new objects, unknown members cloned); the main is not
        /// checked (a piece run checks its source's indices once).</summary>
        public static BakeMain CopyOf(BakeMain s)
        {
            if (s == null) return null;
            var m = new BakeMain
            {
                Format = s.Format, Version = s.Version, Id = s.Id, Kind = s.Kind, Created = s.Created, Source = CopyOf(s.Source),
                Parts = s.Parts == null ? null : new List<string>(s.Parts), Origin = s.Origin, Fk = s.Fk == null ? null : new List<string>(s.Fk),
                Correction = s.Correction == null ? null : (bool[])s.Correction.Clone(), Insides = CopyOf(s.Insides), Unknown = s.Unknown?.Clone(),
            };
            m.Nodes = CopyList(s.Nodes, n => new BakeMainNode { Name = n.Name, Parent = n.Parent, P = n.P, R = n.R, S = n.S, Active = n.Active, Layer = n.Layer, Unknown = n.Unknown?.Clone() });
            m.Meshes = CopyList(s.Meshes, x => new BakeMainMesh { Blob = x.Blob, Name = x.Name, Unknown = x.Unknown?.Clone() });
            m.Textures = CopyList(s.Textures, x => new BakeMainTexture
            {
                Blob = x.Blob, Name = x.Name, Wrap = x.Wrap == null ? null : (int[])x.Wrap.Clone(), Filter = x.Filter, Aniso = x.Aniso, Bias = x.Bias, Unknown = x.Unknown?.Clone(),
            });
            m.Materials = CopyList(s.Materials, x =>
            {
                var c = new BakeMainMaterial
                {
                    Name = x.Name, Shader = x.Shader, Queue = x.Queue, Keywords = x.Keywords == null ? null : new List<string>(x.Keywords),
                    PassesOff = x.PassesOff == null ? null : new List<string>(x.PassesOff), RenderType = x.RenderType,
                    Inside = x.Inside == null ? null : (float[])x.Inside.Clone(), Unknown = x.Unknown?.Clone(),
                };
                if (x.Props == null) c.Props = null;
                else
                    foreach (var kv in x.Props)
                        c.Props[kv.Key] = kv.Value == null ? null : new BakeMainProp
                        {
                            Type = kv.Value.Type, F = kv.Value.F, I = kv.Value.I, V = kv.Value.V, T = kv.Value.T, St = kv.Value.St, Unknown = kv.Value.Unknown?.Clone(),
                        };
                return c;
            });
            m.Renderers = CopyList(s.Renderers, x => new BakeMainRenderer
            {
                Node = x.Node, Skinned = x.Skinned, Mesh = x.Mesh, Materials = x.Materials == null ? null : (int[])x.Materials.Clone(),
                Bones = x.Bones == null ? null : (int[])x.Bones.Clone(), Root = x.Root, Bounds = x.Bounds == null ? null : (float[])x.Bounds.Clone(),
                Shadow = x.Shadow, Receive = x.Receive, LightProbe = x.LightProbe, ReflProbe = x.ReflProbe, Anchor = x.Anchor, UpdateOffscreen = x.UpdateOffscreen,
                Quality = x.Quality, Part = x.Part, Member = x.Member, Inside = x.Inside, Unknown = x.Unknown?.Clone(),
            });
            return m;
        }

        static BakeMainSource CopyOf(BakeMainSource x) => x == null ? null : new BakeMainSource
        {
            Kind = x.Kind, Name = x.Name, Sex = x.Sex, Ref = x.Ref, Group = x.Group, Category = x.Category, Unknown = x.Unknown?.Clone(),
        };

        static BakeMainInsides CopyOf(BakeMainInsides x) => x == null ? null : new BakeMainInsides
        {
            Fill = x.Fill, Section = x.Section, OwnInside = x.OwnInside, Unknown = x.Unknown?.Clone(),
        };

        static List<T> CopyList<T>(List<T> l, Func<T, T> copy) where T : class
        {
            if (l == null) return null;
            var c = new List<T>(l.Count);
            foreach (var x in l) c.Add(x == null ? null : copy(x));
            return c;
        }

        /// <summary>Test diagnostics (internal, off by default, not saved): with <see cref="CheckCopies"/> on, each piece's copy of its source main is
        /// also written and compared byte for byte with a parse of the source main's canonical JSON, written again (what the copy stands for).
        /// <see cref="CopiesCompared"/> and <see cref="CopiesDiffered"/> count them; the first difference keeps the piece's index and both texts.
        /// <see cref="PiecesEncoded"/> counts every piece whose blobs were made.</summary>
        internal static bool CheckCopies;
        internal static int CopiesCompared, CopiesDiffered, PiecesEncoded, FirstDifferencePiece = -1;
        internal static string FirstDifferenceCopy, FirstDifferenceParse;
        static readonly object differenceLock = new object();

        static void CompareCopy(BakeMain source, BakeMain copy, int piece)
        {
            var a = copy.ToJsonBytes();
            var b = BakeMain.Parse(source.ToJsonBytes()).ToJsonBytes();
            System.Threading.Interlocked.Increment(ref CopiesCompared);
            bool same = a.Length == b.Length;
            for (int i = 0; same && i < a.Length; i++) if (a[i] != b[i]) same = false;
            if (same) return;
            lock (differenceLock)
            {
                CopiesDiffered++;
                if (FirstDifferenceCopy != null) return;
                FirstDifferencePiece = piece;
                FirstDifferenceCopy = System.Text.Encoding.UTF8.GetString(a);
                FirstDifferenceParse = System.Text.Encoding.UTF8.GetString(b);
            }
        }

        /// <summary>The pieces' blobs, on a worker: the new meshes of every piece encoded in one parallel loop, then each piece's main made from its
        /// copy of the source main (written after its meshes, as one piece alone writes it) and encoded. A failure throws its first exception.</summary>
        static void EncodePieces(BakeMain source, CutPiece[] pieces, CutPieceResult[] results, string[] ids, CutOutcome o)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // the check a parse of the source makes (Clone's): its indices hold
                string problem = source.Problem();
                if (problem != null) throw new BakeFormatException("the main's indices do not hold: " + problem);
                var encoded = new byte[pieces.Length][][];
                var work = new List<(int p, int k)>();
                for (int p = 0; p < pieces.Length; p++)
                {
                    encoded[p] = new byte[pieces[p].Renderers.Count][];
                    for (int k = 0; k < pieces[p].Renderers.Count; k++) if (pieces[p].Renderers[k].NewMesh != null) work.Add((p, k));
                }
                Parallel.For(0, work.Count, i =>
                {
                    var (p, k) = work[i];
                    encoded[p][k] = DataOf(pieces[p].Renderers[k].NewMesh).ToBlob();
                });
                bool check = CheckCopies;
                Parallel.For(0, pieces.Length, p =>
                {
                    var r = results[p];
                    var copy = CopyOf(source);
                    if (check) CompareCopy(source, copy, p);
                    r.Main = PieceMainOf(source, copy, pieces[p], ids[p], encoded[p], r.NewBlobs);
                    r.MainBlob = r.Main.ToBlob();
                    r.MainSha = BakeBlob.Sha256(r.MainBlob);
                    r.NewBlobs[r.MainSha] = r.MainBlob;
                    r.Bake = r.Main.ToItemBake(r.MainSha);
                    System.Threading.Interlocked.Increment(ref PiecesEncoded);
                });
            }
            catch (AggregateException a) when (a.InnerExceptions.Count > 0)
            {
                ExceptionDispatchInfo.Capture(a.InnerExceptions[0]).Throw();
            }
            o.EncodeMilliseconds = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>A piece's main: the source main copied with the piece's nodes, FK, renderers, meshes, materials, textures and
        /// insides; new mesh blobs go into newBlobs by SHA-256.</summary>
        public static BakeMain PieceMain(BakeMain source, CutPiece piece, string id, Dictionary<string, byte[]> newBlobs)
        {
            var encoded = new byte[piece.Renderers.Count][];
            Parallel.For(0, piece.Renderers.Count, k =>
            {
                var nm = piece.Renderers[k].NewMesh;
                if (nm != null) encoded[k] = DataOf(nm).ToBlob();
            });
            return PieceMainOf(source, source.Clone(), piece, id, encoded, newBlobs);
        }

        /// <summary><see cref="PieceMain"/> on a copy of the source main made by <see cref="BakeMain.Clone"/> (or a parse of the same canonical
        /// JSON) and the piece's new meshes already encoded (<paramref name="encoded"/>[k] for the piece's renderer k, null when unchanged).</summary>
        static BakeMain PieceMainOf(BakeMain source, BakeMain copy, CutPiece piece, string id, byte[][] encoded, Dictionary<string, byte[]> newBlobs)
        {
            var m = copy;
            m.Id = id;
            m.Kind = BakeKinds.Piece;
            m.Created = source.Created;
            m.Origin = piece.Origin ?? source.Origin;
            // nodes
            var nodes = new List<BakeMainNode>(piece.Nodes.Count);
            int rootLayer = m.Nodes.Count > 0 ? m.Nodes[0].Layer : 0;
            foreach (var n in piece.Nodes)
            {
                var bn = n.Tag is int si ? m.Nodes[si] : new BakeMainNode { Name = n.Name, Active = true, Layer = rootLayer };
                bn.Name = n.Name;
                bn.Parent = n.Parent;
                bn.P = new Vec3((float)n.P.X, (float)n.P.Y, (float)n.P.Z);
                bn.R = new Quat4((float)n.R.X, (float)n.R.Y, (float)n.R.Z, (float)n.R.W);
                bn.S = new Vec3((float)n.S.X, (float)n.S.Y, (float)n.S.Z);
                nodes.Add(bn);
            }
            m.Nodes = nodes;
            m.Fk = new List<string>(piece.Fk);
            int shift = piece.ReRooted ? 1 : 0;
            // renderers, their meshes and materials
            var meshes = new List<BakeMainMesh>();
            var meshOfSource = new Dictionary<int, int>();
            var meshOfSha = new Dictionary<string, int>(StringComparer.Ordinal);
            var matMap = new Dictionary<int, int>();
            var usedMats = new SortedSet<int>();
            foreach (var pr in piece.Renderers)
                foreach (int mi in source.Renderers[pr.SourceIndex].Materials ?? Array.Empty<int>()) usedMats.Add(mi);
            var materials = new List<BakeMainMaterial>();
            foreach (int mi in usedMats) { matMap[mi] = materials.Count; materials.Add(m.Materials[mi]); }
            var usedTex = new SortedSet<int>();
            foreach (var mat in materials)
                foreach (var kv in mat.Props)
                    if (kv.Value != null && kv.Value.Type == BakePropType.Texture && kv.Value.T >= 0) usedTex.Add(kv.Value.T);
            var texMap = new Dictionary<int, int>();
            var textures = new List<BakeMainTexture>();
            foreach (int ti in usedTex) { texMap[ti] = textures.Count; textures.Add(m.Textures[ti]); }
            foreach (var mat in materials)
                foreach (var kv in mat.Props)
                    if (kv.Value != null && kv.Value.Type == BakePropType.Texture && kv.Value.T >= 0) kv.Value.T = texMap[kv.Value.T];
            var renderers = new List<BakeMainRenderer>(piece.Renderers.Count);
            for (int k = 0; k < piece.Renderers.Count; k++)
            {
                var pr = piece.Renderers[k];
                var src = source.Renderers[pr.SourceIndex];
                var r = m.Renderers[pr.SourceIndex];
                r.Node = pr.Renderer.Node;
                r.Bones = (int[])pr.Renderer.Bones.Clone();
                if (shift != 0)
                {
                    if (r.Root >= 0) r.Root += shift;
                    if (r.Anchor >= 0) r.Anchor += shift;
                }
                r.Inside = pr.Renderer.Inside;
                var mats = src.Materials ?? Array.Empty<int>();
                r.Materials = new int[mats.Length];
                for (int j = 0; j < mats.Length; j++) r.Materials[j] = matMap[mats[j]];
                if (pr.NewMesh != null)
                {
                    var blob = encoded[k];
                    string sha = BakeBlob.Sha256(blob);
                    newBlobs[sha] = blob;
                    if (!meshOfSha.TryGetValue(sha, out int mi))
                    {
                        mi = meshes.Count;
                        meshOfSha[sha] = mi;
                        meshes.Add(new BakeMainMesh { Blob = sha, Name = src.Mesh >= 0 && src.Mesh < source.Meshes.Count ? source.Meshes[src.Mesh].Name : "" });
                    }
                    r.Mesh = mi;
                }
                else if (src.Mesh >= 0)
                {
                    if (!meshOfSource.TryGetValue(src.Mesh, out int mi))
                    {
                        mi = meshes.Count;
                        meshOfSource[src.Mesh] = mi;
                        meshes.Add(m.Meshes[src.Mesh]);
                    }
                    r.Mesh = mi;
                }
                renderers.Add(r);
            }
            m.Meshes = meshes;
            m.Materials = materials;
            m.Textures = textures;
            m.Renderers = renderers;
            m.Insides.Section = new Vec3(piece.SectionColor[0], piece.SectionColor[1], piece.SectionColor[2]);
            m.Insides.OwnInside = piece.OwnInside;
            return m;
        }

        /// <summary>
        /// A piece whose stored result (its bake's main or a data blob) is missing while its source is present: the procedure
        /// runs again from the block with the piece's own id, its new blobs are added to <paramref name="res"/> (the next save writes
        /// them) and cut.recomputed is reported (info). Returns the result as a blob set to build from, or null when the source is
        /// missing too or the block cannot be run (bake.missing / cut.badStep reported; the caller builds the fallback, blocks kept).
        /// </summary>
        public static BakeBlobSet Recompute(ItemCut cut, ItemBake bake, ResStore res, BakeStore store, FindingList findings, string path)
        {
            if (cut == null || bake == null || res == null) return null;
            var src = BakeBlobSet.Gather(cut.Source, res, store, findings, path);
            if (src.Status != BakeBlobSet.Result.Ok) return null;
            var o = Run(cut, src.Main, src.Get, null, new[] { bake.Id });
            if (o.Error != null || o.Pieces.Count == 0)
            {
                findings?.Warning(o.Error ?? BakeFindings.CutBadStep, path, "the piece's procedure cannot be run (" + (o.ErrorText ?? "no piece") + "); its blocks are kept");
                return null;
            }
            var r = o.Pieces[0];
            foreach (var kv in r.NewBlobs) res.AddBlob(kv.Value);
            bool same = r.MainSha == BakeBlob.ShaOf(bake.Main);
            findings?.Info(BakeFindings.CutRecomputed, path, "the piece's stored result was missing: computed again from its source" + (same ? ", equal to the stored one" : ", not equal to the stored one"));
            var set = new BakeBlobSet { Status = BakeBlobSet.Result.Ok, Main = r.Main, MainSha = r.MainSha };
            foreach (var sha in r.Main.DataBlobs())
            {
                var b = r.NewBlobs.TryGetValue(sha, out var nb) ? nb : src.Get(sha);
                if (b != null) set.Blobs[sha] = b; else set.Absent.Add(sha);
            }
            return set;
        }

        // ------------------------------------------------------------------ names and sections

        /// <summary>A section draws the renderer's own material (garments and hair, own fabric inside on), else the colour.</summary>
        public static bool SectionOwnMaterial(string part, bool ownInside) => CutRules.SectionOwnMaterial(part, ownInside);

        /// <summary>A piece's node name: the given name (with the side when the cut makes two pieces), else
        /// "&lt;target&gt; &lt;side&gt; &lt;MM-dd HH:mm&gt;".</summary>
        public static string PieceName(string given, string target, string side, bool twoPieces, DateTime now)
        {
            if (!string.IsNullOrWhiteSpace(given)) return twoPieces ? given.Trim() + " " + side : given.Trim();
            return (string.IsNullOrEmpty(target) ? "" : target + " ") + side + " " + now.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
