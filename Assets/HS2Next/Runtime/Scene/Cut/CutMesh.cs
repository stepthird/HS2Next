// The mesh a cut works on, Unity-free: the bake's mesh body as plain arrays
// (bind-space positions, normals, tangents, up to 8 UV channels, colours, 4 weights per vertex, bind poses, sub-meshes) and
// the working copy the cut grows (CutVerts): new vertices are made on an edge at a parameter t, every attribute in bind space:
// position lerped, normal lerped and normalized, tangent xyz lerped and normalized with the w of the edge's first end, UVs
// lerped, colours lerped and rounded, weights merged (both ends' influences scaled by 1 − t and t, summed per bone, the four
// largest renormalized). Each vertex also has its posed position in double (CutPoser): skinned on the CPU by the linear
// blend of its four weights divided by their sum (bone matrix = the bone's root-space matrix × its bind pose; float weights
// sum to 1 only within 1e-7, and dividing keeps the pose the same in any root frame, so a re-rooted piece decides as its
// source did), or by its node for an unskinned mesh.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Cut
{
    /// <summary>Edge keys pack two vertex indices; ulong's own hash (low half xor high half) collides on meshes, so the edge
    /// tables mix the bits first.</summary>
    public sealed class CutKeyComparer : IEqualityComparer<ulong>
    {
        public static readonly CutKeyComparer Instance = new CutKeyComparer();
        public bool Equals(ulong a, ulong b) => a == b;
        public int GetHashCode(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return (int)(z ^ (z >> 31));
            }
        }
    }

    public static class CutTopology
    {
        // Unity's MeshTopology values
        public const byte Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5;
    }

    public sealed class CutSubMesh
    {
        public byte Topology;
        public int[] Indices = Array.Empty<int>();
        public CutSubMesh Clone() => new CutSubMesh { Topology = Topology, Indices = (int[])Indices.Clone() };
    }

    /// <summary>A mesh body as plain arrays (null: the channel is absent).</summary>
    public sealed class CutMesh
    {
        public const int Channels = 8;
        public int VertexCount;
        public float[] Positions;        // 3n
        public float[] Normals;          // 3n
        public float[] Tangents;         // 4n
        public readonly int[] UvDims = new int[Channels];
        public readonly float[][] Uvs = new float[Channels][];
        public byte[] Colors;            // 4n
        public ushort[] BoneIndices;     // 4n
        public float[] BoneWeights;      // 4n
        public float[] BindPoses;        // 16 per pose, column-major
        public List<CutSubMesh> SubMeshes = new List<CutSubMesh>();

        public int BindPoseCount => BindPoses == null ? 0 : BindPoses.Length / 16;
        public bool HasWeights => BoneWeights != null && BoneIndices != null;

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (var s in SubMeshes) if (s.Topology == CutTopology.Triangles) n += s.Indices.Length / 3;
                return n;
            }
        }

        public CutVec Position(int i) => new CutVec(Positions[3 * i], Positions[3 * i + 1], Positions[3 * i + 2]);
    }

    /// <summary>The growing vertex store of one mesh during a cut.</summary>
    public sealed class CutVerts
    {
        public readonly CutMesh Source;
        public readonly List<float> Pos, Nrm, Tan, Bw;
        public readonly List<float>[] Uv = new List<float>[CutMesh.Channels];
        public readonly int[] UvDims = new int[CutMesh.Channels];
        public readonly List<byte> Col;
        public readonly List<ushort> Bi;
        /// <summary>Posed positions in double, 3 per vertex.</summary>
        public readonly List<double> Posed = new List<double>();
        /// <summary>Per added vertex (index − source count): the edge's first end, its second end and t.</summary>
        public readonly List<int> FromA = new List<int>(), FromB = new List<int>();
        public readonly List<double> FromT = new List<double>();
        readonly CutPoser poser;

        public CutVerts(CutMesh m, CutPoser poser)
        {
            Source = m;
            this.poser = poser;
            int n = m.VertexCount;
            Pos = new List<float>(m.Positions);
            Nrm = m.Normals != null ? new List<float>(m.Normals) : null;
            Tan = m.Tangents != null ? new List<float>(m.Tangents) : null;
            for (int c = 0; c < CutMesh.Channels; c++)
            {
                UvDims[c] = m.Uvs[c] != null ? m.UvDims[c] : 0;
                if (UvDims[c] > 0) Uv[c] = new List<float>(m.Uvs[c]);
            }
            Col = m.Colors != null ? new List<byte>(m.Colors) : null;
            if (m.HasWeights)
            {
                Bi = new List<ushort>(m.BoneIndices);
                Bw = new List<float>(m.BoneWeights);
            }
            Posed.Capacity = n * 3;
            for (int i = 0; i < n; i++)
            {
                var p = poser.Pose(Pos, Bi, Bw, i);
                Posed.Add(p.X); Posed.Add(p.Y); Posed.Add(p.Z);
            }
        }

        public int Count => Pos.Count / 3;
        public CutVec PosedAt(int i) => new CutVec(Posed[3 * i], Posed[3 * i + 1], Posed[3 * i + 2]);
        public bool Added(int i) => i >= Source.VertexCount;

        // scratch of one interpolated vertex (position and weights), for the posed value at a trial t
        readonly float[] sPos = new float[3];
        readonly ushort[] sBi = new ushort[4];
        readonly float[] sBw = new float[4];
        readonly ushort[] mBone = new ushort[8];
        readonly double[] mW = new double[8];

        /// <summary>The posed position of the vertex an edge (a, b) would get at t, without adding it.</summary>
        public CutVec PosedLerp(int a, int b, double t)
        {
            for (int c = 0; c < 3; c++) sPos[c] = (float)(Pos[3 * a + c] + (Pos[3 * b + c] - (double)Pos[3 * a + c]) * t);
            if (Bi != null) MergeWeights(a, b, t, sBi, sBw);
            return poser.Pose(sPos, Bi != null ? sBi : null, sBw);
        }

        /// <summary>Adds the vertex of the edge (a, b) at t (a is the edge's first end); returns its index.</summary>
        public int AddLerp(int a, int b, double t)
        {
            int idx = Count;
            for (int c = 0; c < 3; c++) Pos.Add((float)(Pos[3 * a + c] + (Pos[3 * b + c] - (double)Pos[3 * a + c]) * t));
            if (Nrm != null)
            {
                var n = new CutVec(
                    Nrm[3 * a] + (Nrm[3 * b] - (double)Nrm[3 * a]) * t,
                    Nrm[3 * a + 1] + (Nrm[3 * b + 1] - (double)Nrm[3 * a + 1]) * t,
                    Nrm[3 * a + 2] + (Nrm[3 * b + 2] - (double)Nrm[3 * a + 2]) * t);
                double l = n.Length;
                if (l > 0) n = n * (1 / l);
                else n = new CutVec(Nrm[3 * a], Nrm[3 * a + 1], Nrm[3 * a + 2]);
                Nrm.Add((float)n.X); Nrm.Add((float)n.Y); Nrm.Add((float)n.Z);
            }
            if (Tan != null)
            {
                var g = new CutVec(
                    Tan[4 * a] + (Tan[4 * b] - (double)Tan[4 * a]) * t,
                    Tan[4 * a + 1] + (Tan[4 * b + 1] - (double)Tan[4 * a + 1]) * t,
                    Tan[4 * a + 2] + (Tan[4 * b + 2] - (double)Tan[4 * a + 2]) * t);
                double l = g.Length;
                if (l > 0) g = g * (1 / l);
                else g = new CutVec(Tan[4 * a], Tan[4 * a + 1], Tan[4 * a + 2]);
                Tan.Add((float)g.X); Tan.Add((float)g.Y); Tan.Add((float)g.Z); Tan.Add(Tan[4 * a + 3]);
            }
            for (int c = 0; c < CutMesh.Channels; c++)
            {
                int d = UvDims[c];
                if (d == 0) continue;
                var l = Uv[c];
                for (int k = 0; k < d; k++) l.Add((float)(l[d * a + k] + (l[d * b + k] - (double)l[d * a + k]) * t));
            }
            if (Col != null)
                for (int k = 0; k < 4; k++)
                {
                    double v = Col[4 * a + k] + (Col[4 * b + k] - (double)Col[4 * a + k]) * t;
                    Col.Add((byte)Math.Max(0, Math.Min(255, Math.Round(v, MidpointRounding.AwayFromZero))));
                }
            if (Bi != null)
            {
                MergeWeights(a, b, t, sBi, sBw);
                for (int k = 0; k < 4; k++) { Bi.Add(sBi[k]); Bw.Add(sBw[k]); }
            }
            var p = poser.Pose(Pos, Bi, Bw, idx);
            Posed.Add(p.X); Posed.Add(p.Y); Posed.Add(p.Z);
            FromA.Add(a); FromB.Add(b); FromT.Add(t);
            return idx;
        }

        /// <summary>Both ends' influences scaled by (1 − t) and t, summed per bone, the four largest (ties: the lower bone)
        /// renormalized, sorted by weight and zero-padded.</summary>
        public void MergeWeights(int a, int b, double t, ushort[] outBone, float[] outW)
        {
            int n = 0;
            for (int e = 0; e < 2; e++)
            {
                int v = e == 0 ? a : b;
                double s = e == 0 ? 1 - t : t;
                for (int k = 0; k < 4; k++)
                {
                    float w = Bw[4 * v + k];
                    if (!(w > 0)) continue;
                    double ws = w * s;
                    if (!(ws > 0)) continue;
                    ushort bone = Bi[4 * v + k];
                    int j = 0;
                    while (j < n && mBone[j] != bone) j++;
                    if (j < n) mW[j] += ws;
                    else { mBone[n] = bone; mW[n] = ws; n++; }
                }
            }
            // insertion sort: weight descending, bone ascending
            for (int i = 1; i < n; i++)
            {
                ushort bb = mBone[i]; double ww = mW[i]; int j = i - 1;
                while (j >= 0 && (mW[j] < ww || (mW[j] == ww && mBone[j] > bb))) { mBone[j + 1] = mBone[j]; mW[j + 1] = mW[j]; j--; }
                mBone[j + 1] = bb; mW[j + 1] = ww;
            }
            int m = Math.Min(4, n);
            double sum = 0;
            for (int i = 0; i < m; i++) sum += mW[i];
            for (int i = 0; i < 4; i++)
            {
                if (i < m && sum > 0) { outBone[i] = mBone[i]; outW[i] = (float)(mW[i] / sum); }
                else { outBone[i] = 0; outW[i] = 0; }
            }
        }

        /// <summary>A mesh of the vertices the index lists use, in their order in the store, with these sub-meshes.</summary>
        public CutMesh Compact(List<CutSubMesh> subs)
        {
            int count = Count;
            var map = new int[count];
            for (int i = 0; i < count; i++) map[i] = -1;
            foreach (var s in subs) foreach (int i in s.Indices) map[i] = 0;
            int n = 0;
            for (int i = 0; i < count; i++) if (map[i] == 0) map[i] = n++;
            var m = new CutMesh { VertexCount = n, BindPoses = Source.BindPoses };
            m.Positions = Gather(Pos, 3, map, n);
            if (Nrm != null) m.Normals = Gather(Nrm, 3, map, n);
            if (Tan != null) m.Tangents = Gather(Tan, 4, map, n);
            for (int c = 0; c < CutMesh.Channels; c++)
                if (UvDims[c] > 0) { m.UvDims[c] = UvDims[c]; m.Uvs[c] = Gather(Uv[c], UvDims[c], map, n); }
            if (Col != null)
            {
                m.Colors = new byte[4 * n];
                for (int i = 0; i < count; i++) if (map[i] >= 0) for (int k = 0; k < 4; k++) m.Colors[4 * map[i] + k] = Col[4 * i + k];
            }
            if (Bi != null)
            {
                m.BoneIndices = new ushort[4 * n];
                m.BoneWeights = Gather(Bw, 4, map, n);
                for (int i = 0; i < count; i++) if (map[i] >= 0) for (int k = 0; k < 4; k++) m.BoneIndices[4 * map[i] + k] = Bi[4 * i + k];
            }
            foreach (var s in subs)
            {
                var idx = new int[s.Indices.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = map[s.Indices[i]];
                m.SubMeshes.Add(new CutSubMesh { Topology = s.Topology, Indices = idx });
            }
            return m;
        }

        static float[] Gather(List<float> src, int dim, int[] map, int n)
        {
            var a = new float[dim * n];
            for (int i = 0; i < map.Length; i++)
            {
                int j = map[i];
                if (j < 0) continue;
                for (int k = 0; k < dim; k++) a[dim * j + k] = src[dim * i + k];
            }
            return a;
        }
    }

    /// <summary>Poses a vertex in the source's root space: skinned by its weights, or by one node matrix.</summary>
    public sealed class CutPoser
    {
        readonly CutXf node;
        readonly CutXf[] bones;   // bone's root-space matrix × its bind pose; null entries for bones not found

        /// <summary>An unskinned mesh hung on a node with this root-space matrix.</summary>
        public CutPoser(CutXf nodeMatrix) { node = nodeMatrix; }

        /// <summary>A skinned mesh: one matrix per bind pose (the bone's root-space matrix × bind pose).</summary>
        public CutPoser(CutXf[] boneMatrices) { bones = boneMatrices; node = CutXf.Identity; }

        public bool Skinned => bones != null;

        public CutVec Pose(List<float> pos, List<ushort> bi, List<float> bw, int i)
        {
            double x = pos[3 * i], y = pos[3 * i + 1], z = pos[3 * i + 2];
            if (bones == null || bi == null) return node.Point(x, y, z);
            double px = 0, py = 0, pz = 0, sw = 0;
            for (int k = 0; k < 4; k++)
            {
                float w = bw[4 * i + k];
                if (!(w > 0)) continue;
                int b = bi[4 * i + k];
                if (b >= bones.Length) continue;
                var p = bones[b].Point(x, y, z);
                px += w * p.X; py += w * p.Y; pz += w * p.Z; sw += w;
            }
            return sw > 0 ? new CutVec(px / sw, py / sw, pz / sw) : node.Point(x, y, z);
        }

        public CutVec Pose(float[] pos, ushort[] bi, float[] bw)
        {
            double x = pos[0], y = pos[1], z = pos[2];
            if (bones == null || bi == null) return node.Point(x, y, z);
            double px = 0, py = 0, pz = 0, sw = 0;
            for (int k = 0; k < 4; k++)
            {
                float w = bw[k];
                if (!(w > 0)) continue;
                int b = bi[k];
                if (b >= bones.Length) continue;
                var p = bones[b].Point(x, y, z);
                px += w * p.X; py += w * p.Y; pz += w * p.Z; sw += w;
            }
            return sw > 0 ? new CutVec(px / sw, py / sw, pz / sw) : node.Point(x, y, z);
        }
    }
}
