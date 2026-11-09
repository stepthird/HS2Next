// BakeTest's Cutter parts (Phase 6.8 package X): BM6, BM7, BU1–BU10 on hand-made meshes, a
// hand-made skeleton and a hand-made source bake, judged by numbers; nothing is rendered or written but the report.
//   Headless_Cut1_Surfaces   BM6 the ragged function against the formula written again from the text, both sides of a face, each
//                            face its own pattern, the surface moves (Present); BM7 the faces at listed points worked by hand, 10^5
//                            random points each in exactly one of inside and the outside pieces (box, tear, hole; clean and ragged)
//   Headless_Cut2_Clip       BU1 a closed skinned sphere cut by a plane: areas add up, every new vertex's attributes against its
//                            edge's ends; BU2 a box's inside and outside cover sampled surface points once; BU3 tear and hole
//                            outlines on the profile, a ragged tear's tips pointed; BU4 a ragged plane's two outlines coincide; BU5
//                            refinement bounds and the 400,000-vertex limit
//   Headless_Cut3_Pieces     BU6 clothes only; BU7 FK pruned, an FK-less prop's own list, the origin at the marker; BU8 sections, one
//                            colour, own fabric, a re-cut recolours; BU10 the chain from the first source against cutting the first
//                            result again, and the same inputs giving the same bits
//   Headless_Cut4_Bakes      BU9 the procedure from its stored block: equal bits, a missing result computed again equal to the stored
//                            one (the counter, cut.recomputed), both missing, an edit, a bad step; BU7 and BU8 in the piece's main
//   Headless_Cut5_Markers    XM the marker objects of the four rows, kinds, hidden for captures and put back, a marker's shape in a
//                            source's root space and a piece placed back at it
//   Headless_Cut6_Load       BU9 in a host: the pieces as item nodes with their bake and cut blocks build from the stored result,
//                            a save and a load in a fresh host build them again; the procedure runs 0 times
//   Headless_Cut7_Recompute  BU9 at a build: a piece whose result's main is missing builds from its procedure run again (the hook X
//                            installs in the bake source), equal in bits, cut.recomputed; saved, a fresh load runs nothing
//   Headless_Cut8_PoseChain  BU10 at another pose (spec 8.2 per-step pose): a piece cut at P1, its FK turned (Present: the first cut's
//                            edge moves >= 1 unit) and cut again at P2: (a) equal to cutting the stored first result again at P2, (b) the
//                            first cut's edge unchanged in bind space, (c) recomputed equal in bits, (d) step 2 holds the pose, step 1 none
//   Headless_Cut9_Finish     YC1 a synthetic source of 26 meshes as a bake item cut through CutPrepare + CutCommit (the path of cut.make):
//                            CutPlan.Finish makes at most the source's renderers + the pieces + 3 MoveNext calls while the outcome's work
//                            is pending at least once (Present), the loop that only turns printed beside; YC2 the pieces' mains equal
//                            CutPieces.Run's from their stored source and steps (Present: the pieces hold new mesh blobs)
//   Headless_A0_CopyCheck,   YI5 (the lead's gate on the structural copy): the first part turns CutPieces.CheckCopies on, the last reports that every
//   Headless_zCopyCheck      piece of the run's cuts had its copy of the source main written equal to a parse of that main's JSON, byte for byte
//   Render_Cut1_Live         BU12 BR-Chan baked as drawn and cut by a plane: the pieces built from their bakes draw the renderers the
//                            cut left whole where the character drew them, one reversed copy per cut renderer that may have one, the
//                            character moved on (Present: its bones moved >= 1 unit) and the pieces did not; BU11 cut times on both
//                            targets in the Editor (informative: the accepted numbers are the player's, BW5)
//   Render_Cut2_Finish       YC1 on BR-Chan captured and cut through CutPrepare + CutCommit, YC2's comparison beside it
// MosaicTest's Cutter part (MosaicTest.render, R3), the partial method CutGroups that MosaicTest_Bake.cs declares, on BV3's fixtures:
//   BV4  a plane across a member through its region's centroid: both pieces hold part of it, every member renderer of both pieces is a
//        member before its first frame, BV3's (a) and (b) on each piece's members (front and 30 deg up)
//   BV5  no inside and no section of a member in the pieces' mains and their built copies; "clothes only" across HERO107's member (his
//        clothes on) leaves it whole in one piece while a garment over it is cut
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Cut;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.IO;
using Object = UnityEngine.Object;
using static MakerTestKit;

public static partial class BakeTest
{
    // ------------------------------------------------------------------ fixtures

    /// <summary>splitmix64: test data from a seed.</summary>
    sealed class CutRng
    {
        ulong s;
        public CutRng(ulong seed) { s = seed; }
        public ulong Next()
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public double U() => (Next() >> 11) * (1.0 / (1UL << 53));
        public double R(double a, double b) => a + (b - a) * U();
        public int I(int a, int b) => a + (int)(Next() % (ulong)(b - a + 1));
    }

    const ulong CutSeed = 0x68c07a11UL;

    static float[] CutColumnMajor(CutXf m) => new float[]
    {
        (float)m.M00, (float)m.M10, (float)m.M20, 0, (float)m.M01, (float)m.M11, (float)m.M21, 0,
        (float)m.M02, (float)m.M12, (float)m.M22, 0, (float)m.M03, (float)m.M13, (float)m.M23, 1,
    };

    /// <summary>A watertight sphere (single poles, the seam shared) with every channel: normals, tangents (w ±1), UV0 (2),
    /// UV2 (3), colours, 4 weights per vertex over the given bones by height; bind poses from the bones' rest matrices.</summary>
    static CutMesh CutSphere(int rings, int segs, double radius, CutVec centre, CutXf[] restBones)
    {
        var pos = new List<CutVec>();
        pos.Add(centre + new CutVec(0, radius, 0));
        for (int r = 1; r < rings; r++)
        {
            double th = Math.PI * r / rings;
            for (int s = 0; s < segs; s++)
            {
                double ph = 2 * Math.PI * s / segs;
                pos.Add(centre + new CutVec(radius * Math.Sin(th) * Math.Cos(ph), radius * Math.Cos(th), radius * Math.Sin(th) * Math.Sin(ph)));
            }
        }
        pos.Add(centre + new CutVec(0, -radius, 0));
        int n = pos.Count, south = n - 1;
        var idx = new List<int>();
        for (int s = 0; s < segs; s++) { idx.Add(0); idx.Add(1 + (s + 1) % segs); idx.Add(1 + s); }
        for (int r = 1; r < rings - 1; r++)
            for (int s = 0; s < segs; s++)
            {
                int a = 1 + (r - 1) * segs + s, b = 1 + (r - 1) * segs + (s + 1) % segs, c = 1 + r * segs + s, d = 1 + r * segs + (s + 1) % segs;
                idx.Add(a); idx.Add(b); idx.Add(c);
                idx.Add(b); idx.Add(d); idx.Add(c);
            }
        int lastRing = 1 + (rings - 2) * segs;
        for (int s = 0; s < segs; s++) { idx.Add(lastRing + s); idx.Add(lastRing + (s + 1) % segs); idx.Add(south); }
        var m = new CutMesh { VertexCount = n };
        m.Positions = new float[3 * n]; m.Normals = new float[3 * n]; m.Tangents = new float[4 * n];
        m.UvDims[0] = 2; m.Uvs[0] = new float[2 * n];
        m.UvDims[2] = 3; m.Uvs[2] = new float[3 * n];
        m.Colors = new byte[4 * n];
        m.BoneIndices = new ushort[4 * n]; m.BoneWeights = new float[4 * n];
        int nb = restBones.Length;
        for (int i = 0; i < n; i++)
        {
            var p = pos[i]; var d = (p - centre).Normalized;
            m.Positions[3 * i] = (float)p.X; m.Positions[3 * i + 1] = (float)p.Y; m.Positions[3 * i + 2] = (float)p.Z;
            m.Normals[3 * i] = (float)d.X; m.Normals[3 * i + 1] = (float)d.Y; m.Normals[3 * i + 2] = (float)d.Z;
            var tg = CutVec.Cross(new CutVec(0, 1, 0), d); if (tg.Length < 1e-6) tg = new CutVec(1, 0, 0); tg = tg.Normalized;
            m.Tangents[4 * i] = (float)tg.X; m.Tangents[4 * i + 1] = (float)tg.Y; m.Tangents[4 * i + 2] = (float)tg.Z; m.Tangents[4 * i + 3] = i % 3 == 0 ? -1f : 1f;
            m.Uvs[0][2 * i] = (float)(0.5 + 0.5 * d.X); m.Uvs[0][2 * i + 1] = (float)(0.5 + 0.5 * d.Z);
            m.Uvs[2][3 * i] = (float)d.X; m.Uvs[2][3 * i + 1] = (float)(d.Y * 2); m.Uvs[2][3 * i + 2] = (float)(d.Z - 1);
            m.Colors[4 * i] = (byte)(127 + 127 * d.Y); m.Colors[4 * i + 1] = (byte)(i * 37 % 256); m.Colors[4 * i + 2] = (byte)(255 - i % 256); m.Colors[4 * i + 3] = 255;
            // 4 influences over the bones by height: the two nearest bones strong, two more weak (merges meet up to 8 bones)
            double h = (d.Y + 1) * 0.5 * (nb - 1);
            int b0 = Math.Max(0, Math.Min(nb - 1, (int)Math.Floor(h))), b1 = Math.Min(nb - 1, b0 + 1);
            double f = h - Math.Floor(h);
            int b2 = (b0 + 2) % nb, b3 = (b0 + 3 + i % 2) % nb;
            if (b1 == b0) b1 = (b0 + nb - 1) % nb;
            if (b2 == b0 || b2 == b1) b2 = (b2 + 1) % nb;
            while (b3 == b0 || b3 == b1 || b3 == b2) b3 = (b3 + 1) % nb;
            var ws = new (int b, double w)[] { (b0, 0.8 * (1 - f) + 0.05), (b1, 0.8 * f + 0.05), (b2, 0.07), (b3, 0.03) };
            double sum = ws.Sum(x => x.w);
            var sorted = ws.Select(x => (x.b, w: x.w / sum)).OrderByDescending(x => x.w).ThenBy(x => x.b).ToArray();
            for (int k = 0; k < 4; k++) { m.BoneIndices[4 * i + k] = (ushort)sorted[k].b; m.BoneWeights[4 * i + k] = (float)sorted[k].w; }
        }
        m.BindPoses = new float[16 * nb];
        for (int j = 0; j < nb; j++) Array.Copy(CutColumnMajor(restBones[j].Inverse()), 0, m.BindPoses, 16 * j, 16);
        m.SubMeshes.Add(new CutSubMesh { Topology = CutTopology.Triangles, Indices = idx.ToArray() });
        return m;
    }

    /// <summary>A flat grid of n × n quads over [−half, half]² in XZ at y = 0, normals +Y, UV0, unskinned.</summary>
    static CutMesh CutGrid(int n, double half)
    {
        int vn = (n + 1) * (n + 1);
        var m = new CutMesh { VertexCount = vn, Positions = new float[3 * vn], Normals = new float[3 * vn] };
        m.UvDims[0] = 2; m.Uvs[0] = new float[2 * vn];
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                int v = j * (n + 1) + i;
                m.Positions[3 * v] = (float)(-half + 2 * half * i / n); m.Positions[3 * v + 2] = (float)(-half + 2 * half * j / n);
                m.Normals[3 * v + 1] = 1;
                m.Uvs[0][2 * v] = (float)i / n; m.Uvs[0][2 * v + 1] = (float)j / n;
            }
        var idx = new int[6 * n * n];
        int o = 0;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                idx[o++] = a; idx[o++] = c; idx[o++] = b;
                idx[o++] = b; idx[o++] = c; idx[o++] = d;
            }
        m.SubMeshes.Add(new CutSubMesh { Topology = CutTopology.Triangles, Indices = idx });
        return m;
    }

    /// <summary>A tube along a chain of nodes (rest positions), skinned by linear blend between consecutive joints.</summary>
    static CutMesh CutTube(IReadOnlyList<int> chain, CutXf[] rest, double radius, int segs, int ringsPerBone, int[] boneSlots)
    {
        // boneSlots: for each chain entry, its bind-pose index in the renderer's bone list
        var pts = new List<CutVec>(); var bones = new List<(int, int, double)>();
        for (int c = 0; c + 1 < chain.Count; c++)
        {
            var a = rest[chain[c]].Translation; var b = rest[chain[c + 1]].Translation;
            for (int r = 0; r < ringsPerBone; r++)
            {
                double t = (double)r / ringsPerBone;
                pts.Add(CutVec.Lerp(a, b, t));
                bones.Add((c, c + 1, t));
            }
        }
        pts.Add(rest[chain[chain.Count - 1]].Translation); bones.Add((chain.Count - 1, chain.Count - 1, 0));
        int rings = pts.Count, n = rings * segs + 2;
        var m = new CutMesh { VertexCount = n, Positions = new float[3 * n], Normals = new float[3 * n], BoneIndices = new ushort[4 * n], BoneWeights = new float[4 * n] };
        m.UvDims[0] = 2; m.Uvs[0] = new float[2 * n];
        void Put(int v, CutVec p, CutVec nn, int b0, int b1, double t)
        {
            m.Positions[3 * v] = (float)p.X; m.Positions[3 * v + 1] = (float)p.Y; m.Positions[3 * v + 2] = (float)p.Z;
            m.Normals[3 * v] = (float)nn.X; m.Normals[3 * v + 1] = (float)nn.Y; m.Normals[3 * v + 2] = (float)nn.Z;
            m.Uvs[0][2 * v] = (float)(v % segs) / segs; m.Uvs[0][2 * v + 1] = (float)(v / segs) / rings;
            if (b0 == b1 || t <= 0) { m.BoneIndices[4 * v] = (ushort)boneSlots[b0]; m.BoneWeights[4 * v] = 1; }
            else
            {
                bool first = 1 - t >= t;
                m.BoneIndices[4 * v] = (ushort)boneSlots[first ? b0 : b1]; m.BoneWeights[4 * v] = (float)(first ? 1 - t : t);
                m.BoneIndices[4 * v + 1] = (ushort)boneSlots[first ? b1 : b0]; m.BoneWeights[4 * v + 1] = (float)(first ? t : 1 - t);
            }
        }
        for (int r = 0; r < rings; r++)
        {
            var axis = r + 1 < rings ? (pts[r + 1] - pts[r]).Normalized : (pts[r] - pts[r - 1]).Normalized;
            var side = Math.Abs(axis.Y) < 0.9 ? CutVec.Cross(axis, new CutVec(0, 1, 0)).Normalized : CutVec.Cross(axis, new CutVec(1, 0, 0)).Normalized;
            var up = CutVec.Cross(side, axis).Normalized;
            for (int s = 0; s < segs; s++)
            {
                double ph = 2 * Math.PI * s / segs;
                var d = side * Math.Cos(ph) + up * Math.Sin(ph);
                Put(r * segs + s, pts[r] + d * radius, d, bones[r].Item1, bones[r].Item2, bones[r].Item3);
            }
        }
        int capA = rings * segs, capB = capA + 1;
        var ax0 = (pts[1] - pts[0]).Normalized; var ax1 = (pts[rings - 1] - pts[rings - 2]).Normalized;
        Put(capA, pts[0], -ax0, bones[0].Item1, bones[0].Item2, 0);
        Put(capB, pts[rings - 1], ax1, bones[rings - 1].Item1, bones[rings - 1].Item2, 0);
        var idx = new List<int>();
        for (int r = 0; r + 1 < rings; r++)
            for (int s = 0; s < segs; s++)
            {
                int a = r * segs + s, b = r * segs + (s + 1) % segs, c = (r + 1) * segs + s, d = (r + 1) * segs + (s + 1) % segs;
                idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
            }
        for (int s = 0; s < segs; s++) { idx.Add(capA); idx.Add(s); idx.Add((s + 1) % segs); }
        int lr = (rings - 1) * segs;
        for (int s = 0; s < segs; s++) { idx.Add(capB); idx.Add(lr + (s + 1) % segs); idx.Add(lr + s); }
        m.SubMeshes.Add(new CutSubMesh { Topology = CutTopology.Triangles, Indices = idx.ToArray() });
        m.BindPoses = Array.Empty<float>();
        return m;
    }

    /// <summary>A hand-made character as a source bake: a skeleton with ≥ 50 FK bones, a body of tubes, a top (clothes0), a
    /// member at the hips, hair and an unskinned accessory on the head; and an FK-less rope prop.</summary>
    sealed class CutFixture
    {
        public CutInput Src = new CutInput { Origin = "chara" };
        public List<CutMesh> Meshes = new List<CutMesh>();
        public CutXf[] Rest;
        public Dictionary<string, int> Node = new Dictionary<string, int>();
        public int Body, Arm, Top, Member, Hair, Acc;

        public int AddNode(string name, string parent, CutVec p, bool fk = true)
        {
            int pi = parent == null ? -1 : Node[parent];
            Src.Nodes.Add(new CutNode { Name = name, Parent = pi, P = p, R = CutQuat.Identity, S = new CutVec(1, 1, 1) });
            Node[name] = Src.Nodes.Count - 1;
            if (fk) Src.Fk.Add(name);
            return Src.Nodes.Count - 1;
        }

        public void Finish()
        {
            Rest = CutProcedure.NodeMatrices(Src, null);
            Src.MeshOf = i => i >= 0 && i < Meshes.Count ? Meshes[i] : null;
        }

        public int AddTube(string[] chain, double radius, string part, bool member = false, int ringsPerBone = 6, int segs = 12)
        {
            var nodes = chain.Select(c => Node[c]).ToList();
            var slots = Enumerable.Range(0, nodes.Count).ToArray();
            var m = CutTube(nodes, Rest, radius, segs, ringsPerBone, slots);
            m.BindPoses = new float[16 * nodes.Count];
            for (int j = 0; j < nodes.Count; j++) Array.Copy(CutColumnMajor(Rest[nodes[j]].Inverse()), 0, m.BindPoses, 16 * j, 16);
            Meshes.Add(m);
            // each renderer on a node of its own under the root, as a capture gives it
            int own = AddNode("o_fixture_" + (part ?? "prop") + "_" + Src.Renderers.Count, Src.Nodes[0].Name, CutVec.Zero, false);
            Src.Renderers.Add(new CutRenderer { Node = own, Skinned = true, Mesh = Meshes.Count - 1, Bones = nodes.ToArray(), Part = part, Member = member });
            return Src.Renderers.Count - 1;
        }

        public static CutFixture Character()
        {
            var f = new CutFixture();
            f.AddNode("chaF_test", null, CutVec.Zero, false);
            f.AddNode("cf_J_Root", "chaF_test", CutVec.Zero, false);
            f.AddNode("cf_J_Hips", "cf_J_Root", new CutVec(0, 10, 0));
            f.AddNode("cf_J_Spine01", "cf_J_Hips", new CutVec(0, 1, 0));
            f.AddNode("cf_J_Spine02", "cf_J_Spine01", new CutVec(0, 1.5, 0));
            f.AddNode("cf_J_Neck", "cf_J_Spine02", new CutVec(0, 2, 0));
            f.AddNode("cf_J_Head", "cf_J_Neck", new CutVec(0, 1, 0));
            f.AddNode("cf_J_HeadTop", "cf_J_Head", new CutVec(0, 2, 0), false);
            foreach (var side in new[] { "L", "R" })
            {
                double sx = side == "L" ? 1 : -1;
                f.AddNode("cf_J_Shoulder_" + side, "cf_J_Spine02", new CutVec(0.8 * sx, 1.5, 0));
                f.AddNode("cf_J_ArmUp00_" + side, "cf_J_Shoulder_" + side, new CutVec(0.8 * sx, 0, 0));
                f.AddNode("cf_J_ArmLow01_" + side, "cf_J_ArmUp00_" + side, new CutVec(3 * sx, 0, 0));
                f.AddNode("cf_J_Hand_" + side, "cf_J_ArmLow01_" + side, new CutVec(3 * sx, 0, 0));
                for (int fi = 0; fi < 5; fi++)
                {
                    string prev = "cf_J_Hand_" + side;
                    for (int j = 0; j < 3; j++)
                    {
                        string nm = $"cf_J_Hand_Finger{fi}{j}_{side}";
                        f.AddNode(nm, prev, j == 0 ? new CutVec(0.6 * sx, 0, (fi - 2) * 0.15) : new CutVec(0.25 * sx, 0, 0));
                        prev = nm;
                    }
                }
                f.AddNode("cf_J_LegUp00_" + side, "cf_J_Hips", new CutVec(0.6 * sx, -0.3, 0));
                f.AddNode("cf_J_LegLow01_" + side, "cf_J_LegUp00_" + side, new CutVec(0, -4, 0));
                f.AddNode("cf_J_Foot01_" + side, "cf_J_LegLow01_" + side, new CutVec(0, -4, 0));
                f.AddNode("cf_J_Toes01_" + side, "cf_J_Foot01_" + side, new CutVec(0, -0.5, 0.8));
            }
            string hp = "cf_J_Head";
            for (int i = 0; i < 6; i++) { string nm = "cf_J_hair_b" + i; f.AddNode(nm, hp, i == 0 ? new CutVec(0, 1, -0.8) : new CutVec(0, -0.6, -0.1)); hp = nm; }
            f.Finish();
            f.Body = f.AddTube(new[] { "cf_J_Hips", "cf_J_Spine01", "cf_J_Spine02", "cf_J_Neck", "cf_J_Head", "cf_J_HeadTop" }, 0.7, "body");
            f.Arm = f.AddTube(new[] { "cf_J_Shoulder_L", "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L", "cf_J_Hand_Finger20_L", "cf_J_Hand_Finger21_L", "cf_J_Hand_Finger22_L" }, 0.35, "body", false, 8);
            f.Top = f.AddTube(new[] { "cf_J_Hips", "cf_J_Spine01", "cf_J_Spine02", "cf_J_Neck" }, 0.85, "clothes0");
            f.Member = f.AddTube(new[] { "cf_J_Hips", "cf_J_Spine01", "cf_J_Spine02" }, 0.2, "body", true);
            f.Hair = f.AddTube(new[] { "cf_J_hair_b0", "cf_J_hair_b1", "cf_J_hair_b2", "cf_J_hair_b3", "cf_J_hair_b4", "cf_J_hair_b5" }, 0.2, "hair0");
            // an unskinned accessory hung on the head: a small grid
            var acc = CutGrid(4, 0.3);
            f.Meshes.Add(acc);
            f.Src.Renderers.Add(new CutRenderer { Node = f.Node["cf_J_Head"], Skinned = false, Mesh = f.Meshes.Count - 1, Part = "acc0" });
            f.Acc = f.Src.Renderers.Count - 1;
            return f;
        }

        /// <summary>An FK-less rope prop: a chain of 10 bones under the prop's root, one tube skinned along it.</summary>
        public static CutFixture Rope()
        {
            var f = new CutFixture { Src = { Origin = "item" } };
            f.AddNode("prop_root", null, CutVec.Zero, false);
            string prev = "prop_root";
            for (int i = 0; i < 10; i++) { string nm = "rope_" + i.ToString("00"); f.AddNode(nm, prev, i == 0 ? new CutVec(0, 1, 0) : new CutVec(0.5, 0, 0), false); prev = nm; }
            f.AddNode("rope_00_dup", "prop_root", new CutVec(0, 0, 1), false);
            f.Finish();
            f.AddTube(Enumerable.Range(0, 10).Select(i => "rope_" + i.ToString("00")).ToArray(), 0.1, null);
            return f;
        }
    }

    static CutStepDef CutPlaneStep(CutVec pos, CutQuat rot, string keep, CutRagParams rag = null, bool clothesOnly = false) =>
        new CutStepDef { Shape = CutShapeInfo.Plane, Pos = pos, Rot = rot, Size = CutShapeInfo.BaseSize(CutKind.Plane), Keep = keep, Ragged = rag, ClothesOnly = clothesOnly };

    static CutStepDef CutShapeStep(CutKind k, CutVec pos, CutQuat rot, CutVec size, string keep, CutRagParams rag = null) =>
        new CutStepDef { Shape = CutShapeInfo.Name(k), Pos = pos, Rot = rot, Size = size, Keep = keep, Ragged = rag };

    static double CutArea(CutMesh m)
    {
        double a = 0;
        foreach (var s in m.SubMeshes)
            if (s.Topology == CutTopology.Triangles)
                for (int t = 0; t + 2 < s.Indices.Length; t += 3)
                    a += 0.5 * CutVec.Cross(m.Position(s.Indices[t + 1]) - m.Position(s.Indices[t]), m.Position(s.Indices[t + 2]) - m.Position(s.Indices[t])).Length;
        return a;
    }

    static double CutArea(CutVerts v, IEnumerable<CutSubMesh> subs)
    {
        double a = 0;
        CutVec P(int i) => new CutVec(v.Pos[3 * i], v.Pos[3 * i + 1], v.Pos[3 * i + 2]);
        foreach (var s in subs)
            if (s.Topology == CutTopology.Triangles)
                for (int t = 0; t + 2 < s.Indices.Length; t += 3)
                    a += 0.5 * CutVec.Cross(P(s.Indices[t + 1]) - P(s.Indices[t]), P(s.Indices[t + 2]) - P(s.Indices[t])).Length;
        return a;
    }

    /// <summary>Vertices on edges used by one triangle only.</summary>
    static HashSet<int> CutBoundary(IEnumerable<CutSubMesh> subs)
    {
        var count = new Dictionary<long, int>();
        foreach (var s in subs)
            if (s.Topology == CutTopology.Triangles)
                for (int t = 0; t + 2 < s.Indices.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = s.Indices[t + e], b = s.Indices[t + (e + 1) % 3];
                        long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                        count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
        var set = new HashSet<int>();
        foreach (var kv in count) if (kv.Value == 1) { set.Add((int)(kv.Key >> 32)); set.Add((int)(kv.Key & 0xffffffff)); }
        return set;
    }

    // ------------------------------------------------------------------ the ragged formula, written again from the text

    static double CutRefSmooth(double t) => t * t * (3 - 2 * t);
    static double CutRefNoise(double x, double y, int seed)
    {
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y);
        double sx = CutRefSmooth(x - i), sy = CutRefSmooth(y - j);
        double h00 = CutRag.Hash(i, j, seed), h10 = CutRag.Hash(i + 1, j, seed), h01 = CutRag.Hash(i, j + 1, seed), h11 = CutRag.Hash(i + 1, j + 1, seed);
        double lo = h00 * (1 - sx) + h10 * sx, hi = h01 * (1 - sx) + h11 * sx;
        return lo * (1 - sy) + hi * sy;
    }
    static double CutRefTri(double t) { double fr = t - Math.Floor(t); return 1 - 4 * Math.Abs(fr - 0.5); }
    static double CutRefRag(double a, double b, int seed, int k, double amp, double tooth, double r)
    {
        int fs = seed * 131 + k * 7919;
        double s = Math.Max(0.001, tooth), x = a / s, y = b / s;
        double wob = (CutRefNoise(0.31 * x, 0.31 * y, fs) - 0.5) * 1.4 * r;
        double zig = 0.5 * (CutRefTri(x + wob) + CutRefTri(y - 0.7 * wob));
        double h = 1 * (1 - r) + (0.3 + 1.4 * CutRag.Hash((int)Math.Floor(x + wob), (int)Math.Floor(y - wob), fs)) * r;
        double fine = 2 * CutRefNoise(2.3 * x, 2.3 * y, fs) - 1;
        return amp * (zig * h * (1 - 0.4 * r) + 0.4 * r * fine);
    }

    /// <summary>The face values written again from the text, for listed points.</summary>
    static double[] CutRefFaces(CutKind kind, CutVec c, CutQuat q, CutVec size, CutVec p, CutRagParams rag)
    {
        var ex = q.Rotate(new CutVec(1, 0, 0)); var ey = q.Rotate(new CutVec(0, 1, 0)); var ez = q.Rotate(new CutVec(0, 0, 1));
        var d = p - c;
        double u = CutVec.Dot(d, ex), w = CutVec.Dot(d, ey), v = CutVec.Dot(d, ez);
        double hx = size.X / 2, hy = size.Y / 2, hz = size.Z / 2;
        double amp = rag?.Amp ?? 0, tooth = rag?.Tooth ?? 0, rr = rag?.Random ?? 0; int seed = rag?.Seed ?? 0;
        double R(double a, double b, int k) => amp > 0 ? CutRefRag(a, b, seed, k, amp, tooth, rr) : 0;
        switch (kind)
        {
            case CutKind.Plane: return new[] { w + R(u, v, 0) };
            case CutKind.Box:
                return new[] { u - hx + R(w, v, 0), -u - hx + R(w, v, 1), w - hy + R(u, v, 2), -w - hy + R(u, v, 3), v - hz + R(u, w, 4), -v - hz + R(u, w, 5) };
            case CutKind.Tear:
                {
                    double prof = 1 - (u / hx) * (u / hx), taper = Math.Min(1, Math.Max(0, 3 * prof));
                    return new[] { v - hz * prof + taper * R(u, w, 0), -v - hz * prof + taper * R(u, w, 1), w - hy + taper * R(u, w, 2), -w - hy + taper * R(u, w, 3) };
                }
            case CutKind.Hole:
                {
                    double ang = Math.Atan2(v, u) * (hx + hz) / 2;
                    return new[] { (Math.Sqrt((u / hx) * (u / hx) + (v / hz) * (v / hz)) - 1) * Math.Min(hx, hz) + R(ang, w, 0), w - hy + R(ang, w, 1), -w - hy + R(ang, w, 2) };
                }
        }
        return Array.Empty<double>();
    }

    // ------------------------------------------------------------------ BM6, BM7

    static void Headless_Cut1_Surfaces()
    {
        Group("BM6", "ragged function (spec 10.4): 64 listed points per shape equal the formula; both sides of a face equal", () =>
        {
            var rng = new CutRng(CutSeed);
            var kinds = new[] { CutKind.Plane, CutKind.Box, CutKind.Tear, CutKind.Hole };
            int listed = 0; double worst = 0; string worstAt = "";
            foreach (var kind in kinds)
            {
                for (int i = 0; i < 64; i++)
                {
                    var rag = new CutRagParams { Amp = rng.R(0.001, 0.5), Tooth = rng.R(0.05, 2), Random = i % 4 == 0 ? 0 : i % 4 == 1 ? 1 : rng.U(), Seed = rng.I(1, 9999) };
                    var q = CutQuat.Euler(rng.R(-180, 180), rng.R(-180, 180), rng.R(-180, 180));
                    var c = new CutVec(rng.R(-3, 3), rng.R(-3, 3), rng.R(-3, 3));
                    var size = kind == CutKind.Plane ? CutShapeInfo.BaseSize(kind) : new CutVec(rng.R(0.2, 5), rng.R(0.2, 5), rng.R(0.2, 5));
                    var p = c + new CutVec(rng.R(-2, 2), rng.R(-2, 2), rng.R(-2, 2));
                    var surf = new CutSurface(kind, c, q, size, rag);
                    var want = CutRefFaces(kind, c, q, size, p, rag);
                    for (int k = 0; k < want.Length; k++)
                    {
                        double got = surf.Face(k, p), d = Math.Abs(got - want[k]);
                        listed++;
                        if (d > worst || double.IsNaN(d)) { worst = double.IsNaN(d) ? double.PositiveInfinity : d; worstAt = $"{kind} point {i} face {k}: {got:R} against {want[k]:R}"; }
                    }
                }
            }
            Check("formula", worst <= 1e-12, $"{listed} face values at 4 x 64 listed points, largest difference {worst:G3} (bar 1e-12){(worst > 0 ? "; at " + worstAt : "")}");
            // both sides of one face: a surface made for the front and one for the back (inside / outside) give one value
            int sides = 0, differ = 0;
            foreach (var kind in kinds)
            {
                var keeps = CutShapeInfo.Keeps(kind, CutShapeInfo.Modes(kind)[0]);
                for (int i = 0; i < 64; i++)
                {
                    var rag = new CutRagParams { Amp = 0.2, Tooth = 0.3, Random = 0.7, Seed = 1 + i };
                    var pos = new CutVec(rng.R(-1, 1), rng.R(-1, 1), rng.R(-1, 1));
                    var st0 = new CutStepDef { Shape = CutShapeInfo.Name(kind), Pos = pos, Rot = CutQuat.Euler(10, 20, 30), Size = new CutVec(1, 1, 1), Keep = keeps[0], Ragged = rag };
                    var st1 = st0.Clone(); st1.Keep = keeps[keeps.Length - 1];
                    var a = CutSurface.Of(st0); var b = CutSurface.Of(st1);
                    var p = pos + new CutVec(rng.R(-1, 1), rng.R(-1, 1), rng.R(-1, 1));
                    for (int k = 0; k < a.FaceCount; k++) { sides++; if (a.Face(k, p) != b.Face(k, p)) differ++; }
                }
            }
            Check("both sides", differ == 0, $"{sides} face values of 4 x 64 points made for one side and for the other: {differ} differ");
            // each face its own pattern: at one in-plane point the 6 faces of a box differ
            {
                var s = new CutSurface(CutKind.Box, CutVec.Zero, CutQuat.Identity, new CutVec(2, 2, 2), new CutRagParams { Amp = 0.3, Tooth = 0.4, Random = 0.6, Seed = 7 });
                var vals = Enumerable.Range(0, 6).Select(k => s.RagAt(k, 0.37, 0.37, 0.37)).ToArray();
                Check("own pattern per face", vals.Distinct().Count() == 6, "the 6 faces' ragged terms at (0.37, 0.37, 0.37): " + string.Join(", ", vals.Select(x => x.ToString("F5"))));
            }
            // Present: amplitude > 0 moves the surface by at least half the amplitude somewhere
            {
                double amp = 0.1, maxMove = 0;
                var s = new CutSurface(CutKind.Plane, CutVec.Zero, CutQuat.Identity, new CutVec(10, 0, 10), new CutRagParams { Amp = amp, Tooth = 0.2, Random = 0.5, Seed = 3 });
                for (int i = 0; i < 200; i++) for (int j = 0; j < 200; j++) maxMove = Math.Max(maxMove, Math.Abs(s.RagAt(0, -2 + 0.02 * i, 0, -2 + 0.02 * j)));
                Check("Present: the surface moves", maxMove >= 0.5 * amp, $"amplitude {amp}: the largest move over 200 x 200 points {maxMove:F4} (floor {0.5 * amp})");
            }
            // the hash: in [0, 1], seeds differ
            {
                double lo = 1, hi = 0; int same = 0;
                for (int i = -50; i < 50; i++) for (int j = -50; j < 50; j++)
                    {
                        double h = CutRag.Hash(i, j, 12345); lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                        if (h == CutRag.Hash(i, j, 12346)) same++;
                    }
                Check("hash range", lo >= 0 && hi <= 1 && hi - lo > 0.99, $"10,000 lattice values in [{lo:F5}, {hi:F5}], {same} equal under the next seed");
            }
        });

        Group("BM7", "shape faces (spec 10.4): values at listed points; box and opening outsides: 1e5 random points each in exactly one of inside and the outside pieces", () =>
        {
            // listed points, expected values worked by hand
            var q = CutQuat.Euler(0, 90, 0);   // X -> -Z, Z -> X
            var c = new CutVec(1, 2, 3);
            var listed = new List<(string what, CutSurface s, CutVec p, double[] want)>
            {
                ("plane above", new CutSurface(CutKind.Plane, c, q, new CutVec(10, 0, 10), null), c + new CutVec(0, 0.3, 0), new[] { 0.3 }),
                ("plane below, aside", new CutSurface(CutKind.Plane, c, q, new CutVec(10, 0, 10), null), c + new CutVec(5, -0.2, 7), new[] { -0.2 }),
                ("box centre", new CutSurface(CutKind.Box, c, q, new CutVec(2, 4, 6), null), c, new[] { -1.0, -1, -2, -2, -3, -3 }),
                // box X axis is world -Z: a point 1.5 along world -Z has u = 1.5
                ("box beyond +X", new CutSurface(CutKind.Box, c, q, new CutVec(2, 4, 6), null), c + new CutVec(0, 0, -1.5), new[] { 0.5, -2.5, -2, -2, -3, -3 }),
                ("tear centre", new CutSurface(CutKind.Tear, CutVec.Zero, CutQuat.Identity, CutShapeInfo.BaseSize(CutKind.Tear), null), CutVec.Zero, new[] { -0.2, -0.2, -0.15, -0.15 }),
                ("tear half length", new CutSurface(CutKind.Tear, CutVec.Zero, CutQuat.Identity, CutShapeInfo.BaseSize(CutKind.Tear), null), new CutVec(0.375, 0.05, 0.1), new[] { 0.1 - 0.2 * 0.75, -0.1 - 0.2 * 0.75, 0.05 - 0.15, -0.05 - 0.15 }),
                ("tear beyond tip", new CutSurface(CutKind.Tear, CutVec.Zero, CutQuat.Identity, CutShapeInfo.BaseSize(CutKind.Tear), null), new CutVec(1.5, 0, 0), new[] { 0.2 * 3, 0.2 * 3, -0.15, -0.15 }),
                ("hole centre", new CutSurface(CutKind.Hole, CutVec.Zero, CutQuat.Identity, CutShapeInfo.BaseSize(CutKind.Hole), null), CutVec.Zero, new[] { -0.3, -0.15, -0.15 }),
                ("hole twice the radius", new CutSurface(CutKind.Hole, CutVec.Zero, CutQuat.Identity, CutShapeInfo.BaseSize(CutKind.Hole), null), new CutVec(0, 0.2, 0.6), new[] { 0.3, 0.05, -0.35 }),
                ("hole ellipse", new CutSurface(CutKind.Hole, CutVec.Zero, CutQuat.Identity, new CutVec(1.2, 0.3, 0.6), null), new CutVec(0.6, 0, 0), new[] { 0.0, -0.15, -0.15 }),
            };
            int n = 0; double worst = 0; string at = "";
            foreach (var (what, s, p, want) in listed)
                for (int k = 0; k < want.Length; k++)
                {
                    double d = Math.Abs(s.Face(k, p) - want[k]);
                    n++;
                    if (d > worst) { worst = d; at = $"{what} face {k}: {s.Face(k, p):R} against {want[k]:R}"; }
                }
            Check("listed points", worst <= 1e-12 && n == 35, $"{n} values at {listed.Count} points, largest difference {worst:G3}{(worst > 1e-12 ? "; " + at : "")}");
            // exactly one region, clean and ragged
            var rng = new CutRng(CutSeed + 7);
            foreach (var kind in new[] { CutKind.Box, CutKind.Tear, CutKind.Hole })
                foreach (bool ragged in new[] { false, true })
                {
                    var size = kind == CutKind.Box ? new CutVec(2, 1, 3) : CutShapeInfo.BaseSize(kind);
                    var rot = CutQuat.Euler(15, -40, 70);
                    var rag = ragged ? new CutRagParams { Amp = 0.05, Tooth = 0.1, Random = 0.8, Seed = 41 } : null;
                    var s = new CutSurface(kind, new CutVec(0.5, -0.25, 1), rot, size, rag);
                    int bad = 0, inside = 0, mismatch = 0;
                    var perPiece = new int[s.FaceCount];
                    double reach = Math.Max(size.X, Math.Max(size.Y, size.Z));
                    for (int i = 0; i < 100000; i++)
                    {
                        var p = new CutVec(0.5, -0.25, 1) + new CutVec(rng.R(-reach, reach), rng.R(-reach, reach), rng.R(-reach, reach)) * 0.75;
                        var f = Enumerable.Range(0, s.FaceCount).Select(k => s.Face(k, p)).ToArray();
                        int count = f.All(x => x <= 0) ? 1 : 0;
                        int which = count == 1 ? -1 : -2;
                        for (int k = 0; k < f.Length; k++)
                        {
                            bool inPiece = f[k] > 0;
                            for (int j = 0; j < k && inPiece; j++) if (!(f[j] <= 0)) inPiece = false;
                            if (inPiece) { count++; which = k; perPiece[k]++; }
                        }
                        if (count != 1) bad++;
                        if (which == -1) inside++;
                        if (s.Classify(p) != which) mismatch++;
                    }
                    Check($"{kind}{(ragged ? " ragged" : "")} regions", bad == 0 && mismatch == 0 && inside > 0,
                        $"100,000 points: {bad} not in exactly one region, {mismatch} classified otherwise; inside {inside}, outside pieces {string.Join("/", perPiece)}");
                }
        });
    }

    /// <summary>Six bones up a column, bent: the rest matrices and the posed ones.</summary>
    static (CutXf[] rest, CutXf[] posed) CutColumn(double bendDeg)
    {
        var rest = new CutXf[6]; var posed = new CutXf[6];
        var pr = CutXf.Identity; var pp = CutXf.Identity;
        for (int j = 0; j < 6; j++)
        {
            var off = new CutVec(0, j == 0 ? -1.0 : 0.4, 0);
            pr = pr * CutXf.TRS(off, CutQuat.Identity, new CutVec(1, 1, 1));
            pp = pp * CutXf.TRS(off, j >= 3 ? CutQuat.Euler(0, 0, bendDeg / 3) * CutQuat.Euler(bendDeg / 7, 0, 0) : CutQuat.Identity, new CutVec(1, 1, 1));
            rest[j] = pr; posed[j] = pp;
        }
        return (rest, posed);
    }

    static CutPoser CutPoserOf(CutMesh m, CutXf[] posed)
    {
        var b = new CutXf[m.BindPoseCount];
        for (int j = 0; j < b.Length; j++) b[j] = posed[j] * CutXf.FromColumnMajor(m.BindPoses, 16 * j);
        return new CutPoser(b);
    }

    static void Headless_Cut2_Clip()
    {
        Group("BU1", "clip (spec 10.6): a closed skinned mesh cut by a plane: areas add to the whole, attributes interpolated linearly, weights merged and renormalized", () =>
        {
            var (rest, posed) = CutColumn(50);
            var m = CutSphere(24, 32, 1.0, CutVec.Zero, rest);
            var v = new CutVerts(m, CutPoserOf(m, posed));
            var surf = new CutSurface(CutKind.Plane, new CutVec(0.1, 0.15, 0), CutQuat.Euler(20, 0, 35), new CutVec(10, 0, 10), null);
            var subs = CutProcedure.Normalized(m.SubMeshes);
            var res = CutClip.Run(v, subs, surf);
            double whole = CutArea(m), a0 = CutArea(v, res.Regions[0]), a1 = CutArea(v, res.Regions[1]);
            double rel = Math.Abs(a0 + a1 - whole) / whole;
            int added = v.Count - m.VertexCount;
            Check("Present: triangles split", res.Crossings >= 1, $"{res.Crossings} new vertices on {res.Triangles} triangles, {added} added");
            Check("areas add up", rel <= 1e-5, $"front {a0:F6} + back {a1:F6} = {a0 + a1:F6} against {whole:F6} (relative {rel:G3}, bar 1e-5); both pieces hold area: {a0 > 0 && a1 > 0}");
            // every added vertex against the attributes of its edge's ends at its t
            double dPos = 0, dNrm = 0, dTan = 0, dUv = 0, dW = 0; int colBad = 0, tanW = 0, sorted = 0, many = 0;
            for (int i = 0; i < added; i++)
            {
                int x = m.VertexCount + i, a = v.FromA[i], b = v.FromB[i]; double t = v.FromT[i];
                for (int c = 0; c < 3; c++) dPos = Math.Max(dPos, Math.Abs(v.Pos[3 * x + c] - (v.Pos[3 * a + c] + (v.Pos[3 * b + c] - (double)v.Pos[3 * a + c]) * t)));
                var nl = new CutVec(v.Nrm[3 * a] + (v.Nrm[3 * b] - (double)v.Nrm[3 * a]) * t, v.Nrm[3 * a + 1] + (v.Nrm[3 * b + 1] - (double)v.Nrm[3 * a + 1]) * t, v.Nrm[3 * a + 2] + (v.Nrm[3 * b + 2] - (double)v.Nrm[3 * a + 2]) * t).Normalized;
                dNrm = Math.Max(dNrm, (new CutVec(v.Nrm[3 * x], v.Nrm[3 * x + 1], v.Nrm[3 * x + 2]) - nl).Length);
                var tl = new CutVec(v.Tan[4 * a] + (v.Tan[4 * b] - (double)v.Tan[4 * a]) * t, v.Tan[4 * a + 1] + (v.Tan[4 * b + 1] - (double)v.Tan[4 * a + 1]) * t, v.Tan[4 * a + 2] + (v.Tan[4 * b + 2] - (double)v.Tan[4 * a + 2]) * t).Normalized;
                dTan = Math.Max(dTan, (new CutVec(v.Tan[4 * x], v.Tan[4 * x + 1], v.Tan[4 * x + 2]) - tl).Length);
                if (v.Tan[4 * x + 3] != v.Tan[4 * a + 3]) tanW++;
                foreach (int ch in new[] { 0, 2 })
                {
                    int d = v.UvDims[ch];
                    for (int k = 0; k < d; k++) dUv = Math.Max(dUv, Math.Abs(v.Uv[ch][d * x + k] - (v.Uv[ch][d * a + k] + (v.Uv[ch][d * b + k] - (double)v.Uv[ch][d * a + k]) * t)));
                }
                for (int k = 0; k < 4; k++)
                {
                    double want = Math.Round(v.Col[4 * a + k] + (v.Col[4 * b + k] - (double)v.Col[4 * a + k]) * t, MidpointRounding.AwayFromZero);
                    if (v.Col[4 * x + k] != want) colBad++;
                }
                // weights: merged here again by bone, the four largest renormalized
                var acc = new Dictionary<int, double>();
                for (int k = 0; k < 4; k++)
                {
                    if (v.Bw[4 * a + k] > 0) acc[v.Bi[4 * a + k]] = (acc.TryGetValue(v.Bi[4 * a + k], out var q0) ? q0 : 0) + v.Bw[4 * a + k] * (1 - t);
                    if (v.Bw[4 * b + k] > 0) acc[v.Bi[4 * b + k]] = (acc.TryGetValue(v.Bi[4 * b + k], out var q1) ? q1 : 0) + v.Bw[4 * b + k] * t;
                }
                if (acc.Count > 4) many++;
                var top = acc.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(4).ToList();
                double s = top.Sum(kv => kv.Value);
                double ws = 0;
                for (int k = 0; k < 4; k++)
                {
                    double want = k < top.Count ? top[k].Value / s : 0;
                    int wantB = k < top.Count ? top[k].Key : 0;
                    dW = Math.Max(dW, Math.Abs(v.Bw[4 * x + k] - want));
                    if (k < top.Count && v.Bi[4 * x + k] != wantB) dW = Math.Max(dW, 1);
                    ws += v.Bw[4 * x + k];
                    if (k > 0 && v.Bw[4 * x + k] > v.Bw[4 * x + k - 1]) sorted++;
                }
                dW = Math.Max(dW, Math.Abs(ws - 1) > 1e-6 ? Math.Abs(ws - 1) : 0);
            }
            Check("positions linear", dPos <= 1e-6, $"{added} added vertices: largest distance from the lerp of the ends {dPos:G3} (float)");
            Check("UVs linear", dUv <= 1e-6, $"UV0 (2) and UV2 (3): largest difference {dUv:G3}");
            Check("normals", dNrm <= 1e-6, $"normalized lerp: largest difference {dNrm:G3}");
            Check("tangents", dTan <= 1e-6 && tanW == 0, $"xyz normalized lerp: largest difference {dTan:G3}; w not the first end's: {tanW}");
            Check("colours", colBad == 0, $"rounded lerp: {colBad} channels differ");
            Check("weights", dW <= 1e-6 && sorted == 0, $"merged per bone, four largest, renormalized: largest difference {dW:G3}, unsorted {sorted}; merges that met more than 4 bones: {many}");
            Info($"search: {res.SearchSteps} evaluations for {res.Crossings} crossings, worst |f| at a new vertex {res.WorstCrossing:G3}");
        });

        Group("BU2", "box (spec 10.4, 10.6): inside and outside cover each sampled surface point once", () =>
        {
            var (rest, posed) = CutColumn(35);
            var m = CutSphere(16, 24, 1.0, CutVec.Zero, rest);
            foreach (bool ragged in new[] { false, true })
            {
                var v = new CutVerts(m, CutPoserOf(m, posed));
                var surf = new CutSurface(CutKind.Box, new CutVec(0.6, 0.3, 0.2), CutQuat.Euler(10, 25, -15), new CutVec(1.2, 1.0, 1.4),
                    ragged ? new CutRagParams { Amp = 0.04, Tooth = 0.15, Random = 0.6, Seed = 9 } : null);
                var subs = CutProcedure.Normalized(m.SubMeshes);
                var rs = CutRefine.Applies(surf) ? CutRefine.Run(v, subs, surf) : null;
                var res = CutClip.Run(v, subs, surf);
                // the output triangles in bind space
                var tris = new List<(CutVec a, CutVec b, CutVec c)>();
                foreach (var g in res.Regions) foreach (var s in g)
                        for (int t = 0; t + 2 < s.Indices.Length; t += 3)
                            tris.Add((CutPos(v, s.Indices[t]), CutPos(v, s.Indices[t + 1]), CutPos(v, s.Indices[t + 2])));
                var rng = new CutRng(CutSeed + 11);
                var src = m.SubMeshes[0].Indices;
                int samples = 3000, gaps = 0, overlaps = 0;
                for (int i = 0; i < samples; i++)
                {
                    int t = 3 * (int)(rng.Next() % (ulong)(src.Length / 3));
                    double r1 = rng.U(), r2 = rng.U(); if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
                    var a = m.Position(src[t]); var b = m.Position(src[t + 1]); var c = m.Position(src[t + 2]);
                    var q = a + (b - a) * r1 + (c - a) * r2;
                    int strict = 0, loose = 0;
                    foreach (var (ta, tb, tc) in tris)
                    {
                        var n = CutVec.Cross(tb - ta, tc - ta); double nn = n.SqrLength;
                        if (nn < 1e-24) continue;
                        if (Math.Abs(CutVec.Dot(q - ta, n)) / Math.Sqrt(nn) > 1e-5) continue;
                        double u0 = CutVec.Dot(CutVec.Cross(tb - q, tc - q), n) / nn, u1 = CutVec.Dot(CutVec.Cross(tc - q, ta - q), n) / nn, u2 = 1 - u0 - u1;
                        if (u0 > 1e-7 && u1 > 1e-7 && u2 > 1e-7) strict++;
                        if (u0 >= -1e-6 && u1 >= -1e-6 && u2 >= -1e-6) loose++;
                    }
                    if (loose == 0) gaps++;
                    if (strict > 1) overlaps++;
                }
                double a0 = CutArea(v, res.Regions[0]), a1 = CutArea(v, res.Regions[1]), whole = CutArea(m);
                Check(ragged ? "ragged box" : "clean box", gaps == 0 && overlaps == 0 && a0 > 0 && a1 > 0,
                    $"{samples} surface points: {gaps} in no piece, {overlaps} in two; inside {a0:F5} + outside {a1:F5} against {whole:F5} (relative {Math.Abs(a0 + a1 - whole) / whole:G3}); {tris.Count} triangles out{(rs != null ? $", refined +{rs.Added} in {rs.Passes} passes" : "")}");
            }
        });

        Group("BU3", "tear and hole (spec 10.2, 10.4): the outline within 1e-4 of the profile; tear tips pointed with a ragged edge", () =>
        {
            foreach (var kind in new[] { CutKind.Tear, CutKind.Hole })
            {
                var m = CutGrid(60, 1.0);
                var v = new CutVerts(m, new CutPoser(CutXf.Identity));
                var surf = new CutSurface(kind, new CutVec(0.05, 0, -0.03), CutQuat.Euler(0, 20, 0), CutShapeInfo.BaseSize(kind), null);
                var subs = CutProcedure.Normalized(m.SubMeshes);
                var rs = CutRefine.Run(v, subs, surf);
                var res = CutClip.Run(v, subs, surf);
                var outline = CutBoundary(res.Regions[1]).Where(i => Math.Abs(v.Pos[3 * i]) < 0.999 && Math.Abs(v.Pos[3 * i + 2]) < 0.999).ToList();
                double worst = outline.Count == 0 ? double.NaN : outline.Max(i => Math.Abs(surf.Region(v.PosedAt(i))));
                Check($"{kind} outline", outline.Count >= 20 && worst <= 1e-4, $"{outline.Count} outline vertices, largest distance from the profile {worst:G3} (bar 1e-4); refined +{rs.Added} in {rs.Passes} passes, target {rs.Target:F4}");
            }
            // ragged tear: the ragged term fades to 0 at the tips
            {
                double amp = 0.03;
                var m = CutGrid(60, 1.0);
                var v = new CutVerts(m, new CutPoser(CutXf.Identity));
                var surf = new CutSurface(CutKind.Tear, CutVec.Zero, CutQuat.Identity, CutShapeInfo.BaseSize(CutKind.Tear), new CutRagParams { Amp = amp, Tooth = 0.06, Random = 0.5, Seed = 5 });
                var subs = CutProcedure.Normalized(m.SubMeshes);
                var rs = CutRefine.Run(v, subs, surf);
                var res = CutClip.Run(v, subs, surf);
                var outline = CutBoundary(res.Regions[1]).Where(i => Math.Abs(v.Pos[3 * i]) < 0.999 && Math.Abs(v.Pos[3 * i + 2]) < 0.999).ToList();
                double hx = surf.Hx, hz = surf.Hz, devMid = 0, tipExcess = 0, tipMaxU = 0; int tips = 0;
                foreach (int i in outline)
                {
                    surf.Local(v.PosedAt(i), out double u, out double w, out double vv);
                    double prof = 1 - (u / hx) * (u / hx);
                    double dev = Math.Abs(Math.Abs(vv) - hz * Math.Max(0, prof));
                    if (Math.Abs(u) < 0.5 * hx) devMid = Math.Max(devMid, dev);
                    if (Math.Abs(u) >= 0.9 * hx)
                    {
                        tips++;
                        tipMaxU = Math.Max(tipMaxU, Math.Abs(u));
                        double allowed = hz * Math.Max(0, prof) + 1.42 * amp * Math.Min(1, Math.Max(0, 3 * prof)) + 1e-4;
                        tipExcess = Math.Max(tipExcess, Math.Abs(vv) - allowed);
                    }
                }
                Check("Present: ragged edge", devMid >= 0.25 * amp, $"amplitude {amp}: the outline leaves the clean profile by up to {devMid:F4} in the middle half (floor {0.25 * amp})");
                Check("tips pointed", tips >= 2 && tipExcess <= 0 && tipMaxU >= 0.95 * hx, $"{tips} outline vertices in the outer tenth of the length, reaching |u| {tipMaxU:F4} of {hx}; width beyond the profile plus the tapered amplitude {tipExcess:G3} (≤ 0); refined +{rs.Added}");
            }
        });

        Group("BU4", "ragged both sides (spec 10.4): the cut vertices of the two pieces coincide", () =>
        {
            var (rest, posed) = CutColumn(40);
            var m = CutSphere(24, 32, 1.0, CutVec.Zero, rest);
            var v = new CutVerts(m, CutPoserOf(m, posed));
            var surf = new CutSurface(CutKind.Plane, new CutVec(0, 0.2, 0), CutQuat.Euler(15, 0, 10), new CutVec(10, 0, 10), new CutRagParams { Amp = 0.06, Tooth = 0.12, Random = 0.7, Seed = 77 });
            var subs = CutProcedure.Normalized(m.SubMeshes);
            var rs = CutRefine.Run(v, subs, surf);
            var res = CutClip.Run(v, subs, surf);
            var front = v.Compact(res.Regions[0]); var back = v.Compact(res.Regions[1]);
            var bf = CutBoundary(front.SubMeshes); var bb = CutBoundary(back.SubMeshes);
            var pf = CutPoserOf(front, posed); var pb = CutPoserOf(back, posed);
            List<CutVec> Posed(CutMesh mm, CutPoser p, IEnumerable<int> ids) => ids.Select(i => p.Pose(new[] { mm.Positions[3 * i], mm.Positions[3 * i + 1], mm.Positions[3 * i + 2] }, new[] { mm.BoneIndices[4 * i], mm.BoneIndices[4 * i + 1], mm.BoneIndices[4 * i + 2], mm.BoneIndices[4 * i + 3] }, new[] { mm.BoneWeights[4 * i], mm.BoneWeights[4 * i + 1], mm.BoneWeights[4 * i + 2], mm.BoneWeights[4 * i + 3] })).ToList();
            var a = Posed(front, pf, bf); var b = Posed(back, pb, bb);
            double Nearest(CutVec p, List<CutVec> l) => l.Min(q => (p - q).Length);
            double worst = 0;
            foreach (var p in a) worst = Math.Max(worst, Nearest(p, b));
            foreach (var p in b) worst = Math.Max(worst, Nearest(p, a));
            Check("outlines coincide", a.Count > 0 && a.Count == b.Count && worst <= 1e-5, $"front outline {a.Count} vertices, back {b.Count}; largest distance to the nearest of the other piece {worst:G3} (bar 1e-5); refined +{rs.Added}, {res.Crossings} crossings");
        });

        Group("BU5", "refinement (spec 10.6 step 3): at most 400,000 vertices and 6 passes, edges near the surface at most the target length", () =>
        {
            var m = CutGrid(10, 2.0);   // edges 0.4
            var v = new CutVerts(m, new CutPoser(CutXf.Identity));
            var surf = new CutSurface(CutKind.Plane, new CutVec(0.13, 0, 0), CutQuat.Euler(0, 0, -90) * CutQuat.Euler(0, 30, 0), new CutVec(10, 0, 10), new CutRagParams { Amp = 0.05, Tooth = 0.06, Random = 0.5, Seed = 2 });
            var subs = CutProcedure.Normalized(m.SubMeshes);
            var rs = CutRefine.Run(v, subs, surf);
            double target = rs.Target, worst = 0; int near = 0, longNear = 0;
            var seen = new HashSet<long>();
            foreach (var s in subs)
                for (int t = 0; t + 2 < s.Indices.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = s.Indices[t + e], b = s.Indices[t + (e + 1) % 3];
                        if (!seen.Add(((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b))) continue;
                        double len = (v.PosedAt(a) - v.PosedAt(b)).Length, ra = surf.Region(v.PosedAt(a)), rb = surf.Region(v.PosedAt(b)), band = 1.5 * surf.Amp + len;
                        if (Math.Abs(ra) <= band || Math.Abs(rb) <= band || (ra > 0) != (rb > 0))
                        {
                            near++;
                            if (len > target) { longNear++; worst = Math.Max(worst, len); }
                        }
                    }
            Check("Present: vertices added", rs.Added >= 100, $"{rs.Added} added in {rs.Passes} passes (target {target:F4}, edges 0.4 before)");
            Check("passes and size", rs.Passes <= 6 && v.Count <= 400000 && !rs.Limited, $"{rs.Passes} passes, {v.Count} vertices");
            Check("edges near the surface", longNear == 0, $"{near} edges within the band or crossing, {longNear} longer than {target:F4} (longest {worst:F4})");
            // the vertex limit: a pass that would pass 400,000 vertices is not made
            var big = CutGrid(630, 31.5);   // 397,488 vertices, edges 0.1
            var vb = new CutVerts(big, new CutPoser(CutXf.Identity));
            var sb = CutProcedure.Normalized(big.SubMeshes);
            var rb2 = CutRefine.Run(vb, sb, new CutSurface(CutKind.Plane, CutVec.Zero, CutQuat.Euler(0, 0, -90), new CutVec(10, 0, 10), new CutRagParams { Amp = 0.05, Tooth = 0.06, Random = 0.5, Seed = 2 }));
            Check("vertex limit", rb2.Limited && vb.Count <= 400000 && rb2.Added == 0, $"{big.VertexCount} vertices before, {vb.Count} after, limited {rb2.Limited}, passes {rb2.Passes}");
        });
    }

    static CutVec CutPos(CutVerts v, int i) => new CutVec(v.Pos[3 * i], v.Pos[3 * i + 1], v.Pos[3 * i + 2]);

    /// <summary>The posed positions (source root space) of a piece renderer's mesh, with the piece's own nodes.</summary>
    static List<CutVec> CutPiecePosed(CutPiece piece, CutPieceRenderer pr, CutInput src, IDictionary<string, CutVec> pose)
    {
        var ps = new CutInput { Origin = piece.Origin, MeshOf = src.MeshOf };
        ps.Nodes.AddRange(piece.Nodes); ps.Fk.AddRange(src.Fk);
        var w = CutProcedure.NodeMatrices(ps, pose);
        var mesh = pr.NewMesh ?? src.MeshOf(pr.Renderer.Mesh);
        var poser = CutProcedure.PoserOf(pr.Renderer, mesh, w);
        var l = new List<CutVec>(mesh.VertexCount);
        var bi = new ushort[4]; var bw = new float[4]; var p3 = new float[3];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            p3[0] = mesh.Positions[3 * i]; p3[1] = mesh.Positions[3 * i + 1]; p3[2] = mesh.Positions[3 * i + 2];
            if (mesh.HasWeights) for (int k = 0; k < 4; k++) { bi[k] = mesh.BoneIndices[4 * i + k]; bw[k] = mesh.BoneWeights[4 * i + k]; }
            // a piece's nodes are in its own root space: back to the source's root space by the piece's root offset
            l.Add(piece.RootOffset.Point(poser.Pose(p3, mesh.HasWeights ? bi : null, bw)));
        }
        return l;
    }

    static CutRun CutMake(CutInput src, IDictionary<string, CutVec> pose, string[] keeps, params CutStepDef[] steps)
    {
        var run = new CutRun { Source = src, Pose = pose, Steps = steps, LastKeeps = keeps };
        CutProcedure.Run(run);
        return run;
    }

    static void Headless_Cut3_Pieces()
    {
        var waist = new CutVec(0, 11.2, 0);   // through the spine tube, the top and the member at the hips

        Group("BU6", "clothes only (spec 10.2, V16): only garments cut, members never, the others whole by the centre of their bounds", () =>
        {
            var f = CutFixture.Character();
            // the member reaches across the plane, so a plain cut cuts it and clothes only must not
            var plain = CutMake(f.Src, null, new[] { CutShapeInfo.Front, CutShapeInfo.Back }, CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front));
            var co = CutMake(f.Src, null, new[] { CutShapeInfo.Front, CutShapeInfo.Back }, CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front, null, true));
            var cutPlain = plain.Pieces[0].CutRenderers.Union(plain.Pieces[1].CutRenderers).OrderBy(i => i).ToList();
            var cutCo = co.Pieces[0].CutRenderers.Union(co.Pieces[1].CutRenderers).OrderBy(i => i).ToList();
            Check("Present: the body crossed the plane", cutPlain.Contains(f.Body) && cutPlain.Contains(f.Member), $"a plain cut cuts renderers {string.Join(",", cutPlain)} (body {f.Body}, top {f.Top}, member {f.Member})");
            Check("only the garment cut", cutCo.SequenceEqual(new[] { f.Top }), $"clothes only cut renderers {string.Join(",", cutCo)}");
            // every other renderer is in exactly one piece, whole: the one its bounds' centre is on
            int wrong = 0; var where = new List<string>();
            var w = CutProcedure.NodeMatrices(f.Src, null);
            var surf = CutSurface.Of(CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front));
            for (int i = 0; i < f.Src.Renderers.Count; i++)
            {
                if (i == f.Top) continue;
                var r = f.Src.Renderers[i]; var mesh = f.Meshes[r.Mesh];
                var vs = new CutVerts(mesh, CutProcedure.PoserOf(r, mesh, w));
                double lo = double.MaxValue, hi = double.MinValue; var mn = new double[3] { 1e9, 1e9, 1e9 }; var mx = new double[3] { -1e9, -1e9, -1e9 };
                for (int v = 0; v < mesh.VertexCount; v++) { var p = vs.PosedAt(v); for (int c = 0; c < 3; c++) { mn[c] = Math.Min(mn[c], p[c]); mx[c] = Math.Max(mx[c], p[c]); } double fv = surf.Clean(0, p); lo = Math.Min(lo, fv); hi = Math.Max(hi, fv); }
                var centre = new CutVec(0.5 * (mn[0] + mx[0]), 0.5 * (mn[1] + mx[1]), 0.5 * (mn[2] + mx[2]));
                int want = surf.Clean(0, centre) > 0 ? 0 : 1;
                var inFront = co.Pieces[0].Renderers.FirstOrDefault(x => x.SourceIndex == i);
                var inBack = co.Pieces[1].Renderers.FirstOrDefault(x => x.SourceIndex == i);
                bool ok = (want == 0 ? inFront != null && inBack == null : inBack != null && inFront == null) && (inFront?.Changed ?? false) == false && (inBack?.Changed ?? false) == false;
                if (!ok) wrong++;
                where.Add($"{i}{(f.Src.Renderers[i].Member ? " member" : "")} {(want == 0 ? "front" : "back")}{(lo < 0 && hi > 0 ? " (crossed)" : "")}");
            }
            Check("others whole by centre", wrong == 0, $"{where.Count} renderers: {string.Join("; ", where)}; {wrong} wrong");
            Check("member kept a member", co.Pieces.SelectMany(p => p.Renderers).Where(x => x.SourceIndex == f.Member).All(x => x.Renderer.Member && x.Renderer.Inside == CutInside.None), "the member's renderer in its piece: member true, no section");
            // a source without a garment ignores the switch
            var noClothes = new CutInput { Origin = f.Src.Origin, MeshOf = f.Src.MeshOf };
            noClothes.Nodes.AddRange(f.Src.Nodes); noClothes.Fk.AddRange(f.Src.Fk);
            for (int i = 0; i < f.Src.Renderers.Count; i++) if (i != f.Top) noClothes.Renderers.Add(f.Src.Renderers[i]);
            var nc = CutMake(noClothes, null, new[] { CutShapeInfo.Front, CutShapeInfo.Back }, CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front, null, true));
            Check("no garment: the switch ignored", nc.Pieces[0].CutRenderers.Count > 0, $"a source with no garment, clothes only: {nc.Pieces[0].CutRenderers.Count} renderers cut");
        });

        Group("BU7", "pieces (spec 10.7, V18, V20): FK pruned, an FK-less prop's own list, origin at the marker", () =>
        {
            var f = CutFixture.Character();
            int fkCount = f.Src.Fk.Count;
            var elbow = f.Rest[f.Node["cf_J_ArmLow01_L"]].Translation; var hand = f.Rest[f.Node["cf_J_Hand_L"]].Translation;
            var mid = CutVec.Lerp(elbow, hand, 0.5);
            var box = CutShapeStep(CutKind.Box, mid, CutQuat.Euler(0, 0, 0), new CutVec(2.2, 1.2, 1.2), CutShapeInfo.Inside);
            var run = CutMake(f.Src, null, null, box);
            var piece = run.Pieces[0];
            Check("Present: the source's FK", fkCount >= 50, $"{fkCount} FK bones in the source");
            Check("box around a forearm", piece.Fk.Count >= 1 && piece.Fk.Count <= 30 && piece.Fk.Contains("cf_J_ArmLow01_L"),
                $"the inside piece keeps {piece.Fk.Count} FK bones: {string.Join(", ", piece.Fk)}; renderers {string.Join(",", piece.Renderers.Select(r => r.SourceIndex))}");
            // the list is what the kept vertices weigh on, each walked up to its FK bone, in the source's order
            var used = new HashSet<int>();
            foreach (var pr in piece.Renderers) CutRules.UsedNodes(f.Src.Renderers[pr.SourceIndex], pr.NewMesh ?? f.Meshes[f.Src.Renderers[pr.SourceIndex].Mesh], used);
            var want = new List<string>();
            var fkSet = new HashSet<string>(f.Src.Fk);
            var hit = new HashSet<string>();
            foreach (int u in used) { int i = u; while (i >= 0 && !fkSet.Contains(f.Src.Nodes[i].Name)) i = f.Src.Nodes[i].Parent; if (i >= 0) hit.Add(f.Src.Nodes[i].Name); }
            foreach (var n in f.Src.Fk) if (hit.Contains(n)) want.Add(n);
            Check("pruned list", want.SequenceEqual(piece.Fk), $"expected {string.Join(", ", want)}");
            // origin at the marker: the new root "cut", the source's root under it at the marker's inverse pose
            var n0 = piece.Nodes[0]; var n1 = piece.Nodes[1];
            bool rootOk = n0.Name == CutRules.RootName && n0.Parent == -1 && n0.P.Length == 0 && n1.Parent == 0 && piece.Origin == "cut" && piece.ReRooted;
            double d = 0;
            foreach (var pr in piece.Renderers)
            {
                var a = CutPiecePosed(piece, pr, f.Src, null);
                var mesh = pr.NewMesh ?? f.Meshes[pr.Renderer.Mesh];
                var w = CutProcedure.NodeMatrices(f.Src, null);
                var poser = CutProcedure.PoserOf(f.Src.Renderers[pr.SourceIndex], mesh, w);
                var vs = new CutVerts(mesh, poser);
                for (int i = 0; i < a.Count; i++) d = Math.Max(d, (a[i] - vs.PosedAt(i)).Length);
            }
            Check("origin at the marker", rootOk && d <= 1e-6, $"root '{n0.Name}' at identity, the source's root under it at {n1.P} {n1.R}; the piece's vertices posed through the new root land on the source's within {d:G3} (bar 1e-6)");
            // a plane piece is re-rooted, a tear keeps the source's origin
            var tear = CutMake(f.Src, null, null, CutShapeStep(CutKind.Tear, f.Rest[f.Node["cf_J_Spine01"]].Translation + new CutVec(0, 0, 0.8), CutQuat.Euler(90, 0, 0), CutShapeInfo.BaseSize(CutKind.Tear), CutShapeInfo.Open));
            Check("tear keeps the origin", !tear.Pieces[0].ReRooted && tear.Pieces[0].Nodes[0].Name == f.Src.Nodes[0].Name && tear.Pieces[0].Side == "torn", $"tear piece root '{tear.Pieces[0].Nodes[0].Name}', origin {tear.Pieces[0].Origin}, side {tear.Pieces[0].Side}, cut {string.Join(",", tear.Pieces[0].CutRenderers)}");
            // V20: an FK-less prop
            var rope = CutFixture.Rope();
            var rp = CutMake(rope.Src, null, null, CutPlaneStep(new CutVec(2.3, 1, 0), CutQuat.Euler(0, 0, 90), CutShapeInfo.Front)).Pieces[0];
            var rUsed = new HashSet<int>();
            foreach (var pr in rp.Renderers) CutRules.UsedNodes(rope.Src.Renderers[pr.SourceIndex], pr.NewMesh ?? rope.Meshes[0], rUsed);
            var counts = rope.Src.Nodes.GroupBy(n => n.Name).ToDictionary(g => g.Key, g => g.Count());
            var rWant = rUsed.Where(i => i != 0 && counts[rope.Src.Nodes[i].Name] == 1).OrderBy(i => i).Select(i => rope.Src.Nodes[i].Name).ToList();
            Check("FK-less prop's own list", rope.Src.Fk.Count == 0 && rp.Fk.Count > 0 && rp.Fk.Count < 10 && rWant.SequenceEqual(rp.Fk),
                $"the rope has no FK; its piece lists {rp.Fk.Count}: {string.Join(", ", rp.Fk)} (expected {string.Join(", ", rWant)})");
        });

        Group("BU8", "sections (spec 10.5, V14): a section per cut renderer, one colour per piece, own fabric for garments and hair, a re-cut recolours", () =>
        {
            var f = CutFixture.Character();
            var red = new[] { 0.35f, 0.03f, 0.03f }; var bone = new[] { 0.89f, 0.85f, 0.75f };
            var r1 = new CutRun { Source = f.Src, Steps = new[] { CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front) }, LastKeeps = new[] { CutShapeInfo.Front, CutShapeInfo.Back }, SectionColor = red };
            CutProcedure.Run(r1);
            int cut = 0, sections = 0, wrongInside = 0, memberSections = 0;
            foreach (var p in r1.Pieces)
                foreach (var pr in p.Renderers)
                {
                    bool changed = p.CutRenderers.Contains(pr.SourceIndex);
                    if (changed) cut++;
                    if (pr.Renderer.Inside == CutInside.Section) sections++;
                    if (changed && !pr.Renderer.Member && pr.Renderer.Inside != CutInside.Section) wrongInside++;
                    if (!changed && pr.Renderer.Inside != f.Src.Renderers[pr.SourceIndex].Inside) wrongInside++;
                    if (pr.Renderer.Member && pr.Renderer.Inside != CutInside.None) memberSections++;
                }
            Check("Present: a cut renderer", cut >= 1, $"{cut} cut renderers over both pieces");
            Check("a section per cut renderer", wrongInside == 0 && memberSections == 0, $"{sections} sections; {wrongInside} renderers with the wrong inside; {memberSections} sections on members");
            Check("one colour per piece", r1.Pieces.All(p => p.SectionColor.SequenceEqual(red)), "both pieces: " + string.Join(" / ", r1.Pieces.Select(p => string.Join(",", p.SectionColor))));
            Check("own fabric for garments and hair", CutRules.SectionOwnMaterial("clothes0", true) && CutRules.SectionOwnMaterial("hair0", true) && !CutRules.SectionOwnMaterial("body", true) && !CutRules.SectionOwnMaterial("clothes0", false) && !CutRules.SectionOwnMaterial(null, true),
                "clothes0 and hair0 with own fabric on: own material; body, an item, own fabric off: the colour");
            // a re-cut in another colour: one chain from the first source; the earlier sections take the new colour
            var elbow = f.Rest[f.Node["cf_J_ArmLow01_L"]].Translation;
            var r2 = new CutRun
            {
                Source = f.Src,
                Steps = new[] { CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front), CutPlaneStep(elbow, CutQuat.Euler(0, 0, 90), CutShapeInfo.Front) },
                SectionColor = bone,
            };
            CutProcedure.Run(r2);
            var p2 = r2.Pieces[0];
            var first = r1.Pieces[0].CutRenderers.Where(i => p2.Renderers.Any(x => x.SourceIndex == i)).ToList();
            bool recolour = first.Count > 0 && p2.SectionColor.SequenceEqual(bone) && first.All(i => p2.Renderers.First(x => x.SourceIndex == i).Renderer.Inside == CutInside.Section || f.Src.Renderers[i].Member);
            Check("a re-cut recolours", recolour && p2.CutRenderers.Count > first.Count, $"chain of 2: {p2.CutRenderers.Count} cut renderers ({first.Count} from the first step still there), every section in the piece's one colour {string.Join(",", p2.SectionColor)}");
        });

        Group("BU10", "re-cut chain (spec 10.1, 10.8): the chain from the first source equals cutting the first result again", () =>
        {
            var f = CutFixture.Character();
            var pose = new Dictionary<string, CutVec> { ["cf_J_ArmLow01_L"] = new CutVec(0, -35, 10), ["cf_J_Spine02"] = new CutVec(8, 0, 0) };
            var s1 = CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front, new CutRagParams { Amp = 0.08, Tooth = 0.3, Random = 0.5, Seed = 12 });
            var s2 = CutShapeStep(CutKind.Box, f.Rest[f.Node["cf_J_Spine02"]].Translation + new CutVec(0.4, 0.5, 0), CutQuat.Euler(10, 20, 30), new CutVec(1.5, 1.2, 2.5), CutShapeInfo.Outside);
            var chain = CutMake(f.Src, pose, null, s1, s2).Pieces[0];
            // the first result as a source of its own; step 2 moved into its root space
            var first = CutMake(f.Src, pose, null, s1).Pieces[0];
            var fs = new CutInput { Origin = first.Origin };
            fs.Nodes.AddRange(first.Nodes);
            fs.Fk.AddRange(first.Fk.Count > 0 ? f.Src.Fk : first.Fk);
            var meshes = new List<CutMesh>();
            foreach (var pr in first.Renderers)
            {
                var r = pr.Renderer.Clone(); r.Mesh = meshes.Count; meshes.Add(pr.NewMesh ?? f.Meshes[f.Src.Renderers[pr.SourceIndex].Mesh]);
                fs.Renderers.Add(r);
            }
            fs.MeshOf = i => meshes[i];
            var inv = first.RootOffset.Inverse();
            var s2b = s2.Clone(); s2b.Pos = inv.Point(s2.Pos); s2b.Rot = (first.RootRot.Inverse * s2.Rot).Normalized;
            var again = CutMake(fs, pose, null, s2b).Pieces[0];
            int n = 0; double worst = 0; int countDiff = 0; string worstAt = "";
            var srcOf = again.Renderers.Select(x => first.Renderers[x.SourceIndex].SourceIndex).ToList();
            Check("Present: steps", chain.Renderers.Count > 0 && again.Renderers.Count == chain.Renderers.Count && chain.CutRenderers.Count > 0,
                $"chain: 2 steps from the first source, {chain.Renderers.Count} renderers, {chain.CutRenderers.Count} cut; again: {again.Renderers.Count} renderers");
            for (int i = 0; i < chain.Renderers.Count && i < again.Renderers.Count; i++)
            {
                var a = chain.Renderers[i]; var b = again.Renderers[i];
                if (srcOf[i] != a.SourceIndex) { countDiff++; continue; }
                var ma = a.NewMesh ?? f.Meshes[f.Src.Renderers[a.SourceIndex].Mesh];
                var mb = b.NewMesh ?? meshes[b.Renderer.Mesh];
                if (ma.VertexCount != mb.VertexCount) { countDiff++; continue; }
                for (int v = 0; v < 3 * ma.VertexCount; v++)
                {
                    double dd = Math.Abs(ma.Positions[v] - mb.Positions[v]);
                    if (dd > worst) { worst = dd; worstAt = $"bind: renderer {a.SourceIndex} vertex {v / 3} of {ma.VertexCount} coord {v % 3}: {ma.Positions[v]:R} against {mb.Positions[v]:R}"; }
                }
                var pa = CutPiecePosed(chain, a, f.Src, pose);
                var srcB = new CutInput { Origin = again.Origin, MeshOf = fs.MeshOf }; srcB.Fk.AddRange(fs.Fk);
                var pb = CutPiecePosed(again, b, fs, pose).Select(p => first.RootOffset.Point(p)).ToList();
                for (int v = 0; v < pa.Count; v++)
                {
                    double dd = (pa[v] - pb[v]).Length;
                    if (dd > worst) { worst = dd; worstAt = $"posed: renderer {a.SourceIndex} vertex {v} of {pa.Count}: {pa[v]} against {pb[v]}; weights {string.Join(",", Enumerable.Range(0, 4).Select(k => ma.BoneIndices[4 * v + k] + ":" + ma.BoneWeights[4 * v + k].ToString("R")))} against {string.Join(",", Enumerable.Range(0, 4).Select(k => mb.BoneIndices[4 * v + k] + ":" + mb.BoneWeights[4 * v + k].ToString("R")))}; changed {a.Changed}/{b.Changed}"; }
                }
                n += ma.VertexCount;
            }
            Check("equal", countDiff == 0 && worst <= 1e-6, $"{n} vertices compared (bind and posed in the first source's root space): largest difference {worst:G3} (bar 1e-6){(worst > 1e-6 ? " at " + worstAt : "")}; renderers or counts differing {countDiff}");
            // the same inputs give the same bits (what a recompute relies on)
            var twice = CutMake(f.Src, pose, null, s1, s2).Pieces[0];
            bool bits = twice.Renderers.Count == chain.Renderers.Count && twice.Renderers.Zip(chain.Renderers, (x, y) => x.NewMesh == null ? y.NewMesh == null : y.NewMesh != null && x.NewMesh.Positions.SequenceEqual(y.NewMesh.Positions) && (x.NewMesh.BoneWeights ?? new float[0]).SequenceEqual(y.NewMesh.BoneWeights ?? new float[0]) && x.NewMesh.SubMeshes[0].Indices.SequenceEqual(y.NewMesh.SubMeshes[0].Indices)).All(z => z)
                && twice.Fk.SequenceEqual(chain.Fk);
            Check("run twice: same bits", bits, $"positions, weights, indices and the FK list of a second run equal bit for bit: {bits}");
        });
    }

    static Vec3 CutV3(CutVec v) => new Vec3((float)v.X, (float)v.Y, (float)v.Z);
    static Quat4 CutQ4(CutQuat q) => new Quat4((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);

    /// <summary>The hand-made character as a source bake: a main and its blobs (meshes, two small textures used by the top's and
    /// the hair's materials, a material per renderer on HS2Next/Standard).</summary>
    static BakeMain CutFixtureMain(CutFixture f, Dictionary<string, byte[]> blobs)
    {
        var m = new BakeMain
        {
            Id = JsonText.NewId(), Kind = BakeKinds.Chara, Created = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc), Origin = BakeKinds.OriginChara,
            Source = new BakeMainSource { Kind = BakeKinds.SourceChara, Name = "cut fixture", Sex = 1 },
        };
        foreach (var n in f.Src.Nodes) m.Nodes.Add(new BakeMainNode { Name = n.Name, Parent = n.Parent, P = CutV3(n.P), R = CutQ4(n.R), S = CutV3(n.S), Active = true, Layer = 10 });
        m.Fk.AddRange(f.Src.Fk);
        foreach (var mesh in f.Meshes)
        {
            var blob = CutPieces.DataOf(mesh).ToBlob();
            string sha = BakeBlob.Sha256(blob);
            blobs[sha] = blob;
            m.Meshes.Add(new BakeMainMesh { Blob = sha, Name = "fixture mesh " + m.Meshes.Count });
        }
        for (int t = 0; t < 2; t++)
        {
            var levels = new List<byte[]> { Enumerable.Repeat((byte)(60 + 100 * t), 4 * 4 * 4).ToArray(), Enumerable.Repeat((byte)(70 + 100 * t), 2 * 2 * 4).ToArray(), Enumerable.Repeat((byte)(80 + 100 * t), 4).ToArray() };
            var blob = BakeTextureData.Encode(4, 4, "R8G8B8A8_UNorm", "RGBA32", levels);
            string sha = BakeBlob.Sha256(blob);
            blobs[sha] = blob;
            m.Textures.Add(new BakeMainTexture { Blob = sha, Name = "fixture texture " + t });
        }
        for (int i = 0; i < f.Src.Renderers.Count; i++)
        {
            var r = f.Src.Renderers[i];
            int tex = i == f.Top ? 0 : i == f.Hair ? 1 : -1;
            var mat = new BakeMainMaterial { Name = "fixture material " + i, Shader = "HS2Next/Standard", Queue = 2000 };
            mat.Props["_Color"] = BakeMainProp.Color(new Vec4(0.8f, 0.7f, 0.6f, 1));
            mat.Props["_Glossiness"] = BakeMainProp.Float(0.3f);
            mat.Props["_MainTex"] = BakeMainProp.Texture(tex, new Vec4(1, 1, 0, 0));
            m.Materials.Add(mat);
            m.Renderers.Add(new BakeMainRenderer
            {
                Node = r.Node, Skinned = r.Skinned, Mesh = r.Mesh, Materials = new[] { i }, Bones = (int[])r.Bones.Clone(),
                Root = r.Bones.Length > 0 ? r.Bones[0] : -1, Bounds = new float[] { 0, 12, 0, 10, 10, 10 }, Part = r.Part ?? "other", Member = r.Member,
                Inside = r.Inside, Anchor = r.Bones.Length > 0 ? r.Bones[0] : -1,
            });
        }
        return m;
    }

    static ItemCut CutBlock(BakeMain source, Dictionary<string, byte[]> blobs, string sourceMainSha, IDictionary<string, Vec3> pose, Vec3 color, params CutStepDef[] steps)
    {
        var src = new CutSource { Id = source.Id, Main = BakeBlob.ResName(sourceMainSha) };
        foreach (var sha in source.DataBlobs()) src.Data.Add(BakeBlob.ResName(sha));
        return CutPieces.NewBlock(src, pose, steps.Select(CutPieces.BlockOf), color, true);
    }

    static void Headless_Cut4_Bakes()
    {
        var waist = new CutVec(0, 11.2, 0);

        Group("BU9", "procedure and stored result (spec 10.8): the same block gives the same bits; a missing result computed again equal to the stored one; an edit computes again", () =>
        {
            var f = CutFixture.Character();
            var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var source = CutFixtureMain(f, blobs);
            Check("the source main", source.Problem() == null, source.Problem() ?? $"{source.Nodes.Count} nodes, {source.Renderers.Count} renderers, {source.Meshes.Count} meshes, {source.Textures.Count} textures");
            var sourceBlob = source.ToBlob(); string sourceSha = BakeBlob.Sha256(sourceBlob); blobs[sourceSha] = sourceBlob;
            var pose = new Dictionary<string, Vec3> { ["cf_J_Spine02"] = new Vec3(6, 0, 0) };
            var cut = CutBlock(source, blobs, sourceSha, pose, CutSectionColors.DarkRed,
                CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front, new CutRagParams { Amp = 0.05, Tooth = 0.25, Random = 0.5, Seed = 3 }));
            var ids = new[] { JsonText.NewId(), JsonText.NewId() };
            int runs0 = CutProcedure.Runs;
            var a = CutPieces.Run(cut, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, new[] { CutShapeInfo.Front, CutShapeInfo.Back }, ids);
            var b2 = CutPieces.Run(cut, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, new[] { CutShapeInfo.Front, CutShapeInfo.Back }, ids);
            int runs1 = CutProcedure.Runs;
            bool ok = a.Error == null && a.Pieces.Count == 2 && b2.Pieces.Count == 2;
            Check("two pieces", ok, a.Error != null ? a.Error + ": " + a.ErrorText : string.Join("; ", a.Pieces.Select(p => $"{p.Side}: {p.Main.Renderers.Count} renderers, cut {p.CutRenderers.Count}, {p.NewBlobs.Count} new blobs, main {p.MainSha.Substring(0, 12)}, problem {p.Main.Problem() ?? "none"}")));
            if (!ok) return;
            bool bits = a.Pieces.Zip(b2.Pieces, (x, y) => x.MainBlob.SequenceEqual(y.MainBlob) && x.NewBlobs.Count == y.NewBlobs.Count && x.NewBlobs.All(kv => y.NewBlobs.TryGetValue(kv.Key, out var yb) && yb.SequenceEqual(kv.Value))).All(z => z);
            Check("the same block, the same bits", bits && runs1 - runs0 == 2, $"two runs of one block: main and new blobs equal byte for byte {bits}; runs counted {runs1 - runs0}");
            // each piece of its own block, run alone, equals the piece of the two-piece run
            bool alone = true;
            for (int p = 0; p < 2; p++)
            {
                var one = CutPieces.Run(a.Pieces[p].Cut, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, null, new[] { ids[p] });
                alone &= one.Pieces.Count == 1 && one.Pieces[0].MainSha == a.Pieces[p].MainSha;
            }
            Check("a piece's own block gives its result", alone, "each piece's block (its keep in the last step), run alone, gives the main of the two-piece run");
            // a missing result: the scene holds the source and the piece's other blobs, not the piece's main
            var piece = a.Pieces[0];
            var res = new ResStore();
            foreach (var kv in blobs) res.AddBlob(kv.Value);
            foreach (var kv in piece.NewBlobs) if (kv.Key != piece.MainSha) res.AddBlob(kv.Value);
            var findings = new FindingList();
            int r0 = CutProcedure.Runs;
            var set = CutPieces.Recompute(piece.Cut, piece.Bake, res, null, findings, "test");
            int r1 = CutProcedure.Runs;
            Check("Present: the counter counts a recompute", r1 - r0 == 1, $"runs before {r0}, after {r1}");
            Check("recomputed equal in bits", set != null && set.MainSha == piece.MainSha && res.Contains(piece.Bake.Main) && res.Get(piece.Bake.Main).SequenceEqual(piece.MainBlob) && set.Absent.Count == 0,
                set == null ? "null" : $"recomputed main {set.MainSha.Substring(0, 12)} against stored {piece.MainSha.Substring(0, 12)}; the main is back in the scene's res: {res.Contains(piece.Bake.Main)}; absent {set.Absent.Count}");
            Check("cut.recomputed reported", findings.Has(BakeFindings.CutRecomputed) && findings.First(x => x.Code == BakeFindings.CutRecomputed).Severity == FindingSeverity.Info, string.Join("; ", findings.Select(x => x.Code + " " + x.Severity)));
            // both missing: nothing built, bake.missing
            var bare = new ResStore();
            var f2 = new FindingList();
            var none = CutPieces.Recompute(piece.Cut, piece.Bake, bare, null, f2, "test");
            Check("both missing", none == null && f2.Has(BakeFindings.Missing), $"result {(none == null ? "none" : "built")}; findings {string.Join(", ", f2.Select(x => x.Code))}");
            // an edit computes again: another section colour and another seed change the result
            var edited = piece.Cut.Clone();
            edited.Section.Color = CutSectionColors.Bone;
            edited.Steps[0].Ragged.Seed = 4;
            int e0 = CutProcedure.Runs;
            var e = CutPieces.Run(edited, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, null, new[] { piece.Bake.Id });
            Check("an edit computes again", CutProcedure.Runs - e0 == 1 && e.Pieces.Count == 1 && e.Pieces[0].MainSha != piece.MainSha && e.Pieces[0].Main.Insides.Section.Equals(CutSectionColors.Bone),
                $"edited block: main {e.Pieces[0].MainSha.Substring(0, 12)} against {piece.MainSha.Substring(0, 12)}, section {e.Pieces[0].Main.Insides.Section}");
            // a bad step is refused
            var bad = piece.Cut.Clone(); bad.Steps[0].Keep = CutShapeInfo.Inside;
            var br = CutPieces.Run(bad, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, null, new[] { piece.Bake.Id });
            Check("a bad step", br.Error == BakeFindings.CutBadStep && br.Pieces.Count == 0, $"a plane keeping inside: {br.Error} ({br.ErrorText})");
        });

        Group("BU7", "pieces as bakes (spec 10.7): re-rooted nodes, indices moved, FK pruned, only the materials and textures that are used", () =>
        {
            var f = CutFixture.Character();
            var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var source = CutFixtureMain(f, blobs);
            var sb = source.ToBlob(); string ssha = BakeBlob.Sha256(sb); blobs[ssha] = sb;
            var elbow = f.Rest[f.Node["cf_J_ArmLow01_L"]].Translation; var hand = f.Rest[f.Node["cf_J_Hand_L"]].Translation;
            var cut = CutBlock(source, blobs, ssha, null, CutSectionColors.DarkRed, CutShapeStep(CutKind.Box, CutVec.Lerp(elbow, hand, 0.5), CutQuat.Identity, new CutVec(2.2, 1.2, 1.2), CutShapeInfo.Inside));
            var o = CutPieces.Run(cut, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, null, new[] { JsonText.NewId() });
            var p = o.Pieces[0]; var m = p.Main;
            Check("the piece main", m.Problem() == null && m.Kind == BakeKinds.Piece && m.Origin == BakeKinds.OriginCut && m.Created == source.Created,
                $"problem {m.Problem() ?? "none"}, kind {m.Kind}, origin {m.Origin}, created {m.Created:o}");
            Check("re-rooted", m.Nodes[0].Name == "cut" && m.Nodes[0].Parent == -1 && m.Nodes[1].Name == source.Nodes[0].Name && m.Nodes[1].Parent == 0 && m.Nodes.Count == source.Nodes.Count + 1
                && m.Nodes.Skip(2).Zip(source.Nodes.Skip(1), (x, y) => x.Name == y.Name && x.Parent == y.Parent + 1).All(z => z),
                $"{m.Nodes.Count} nodes: '{m.Nodes[0].Name}' then the source's {source.Nodes.Count}, parents moved by one");
            var r = m.Renderers.Single();
            var sr = source.Renderers[f.Arm];
            Check("indices moved", r.Node == sr.Node + 1 && r.Root == sr.Root + 1 && r.Anchor == sr.Anchor + 1 && r.Bones.SequenceEqual(sr.Bones.Select(x => x + 1)),
                $"node {sr.Node}->{r.Node}, root {sr.Root}->{r.Root}, anchor {sr.Anchor}->{r.Anchor}, bones +1");
            Check("FK pruned", m.Fk.Count >= 1 && m.Fk.Count <= 30 && source.Fk.Count >= 50, $"{source.Fk.Count} -> {m.Fk.Count}: {string.Join(", ", m.Fk)}");
            Check("materials and textures used", m.Materials.Count == 1 && m.Materials[0].Name == source.Materials[f.Arm].Name && m.Textures.Count == 0 && r.Materials.SequenceEqual(new[] { 0 }) && m.Materials[0].Props["_MainTex"].T == -1,
                $"{source.Materials.Count} materials -> {m.Materials.Count}, {source.Textures.Count} textures -> {m.Textures.Count}");
            // a piece that keeps the top keeps its texture, renumbered
            var plane = CutBlock(source, blobs, ssha, null, CutSectionColors.DarkRed, CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Back));
            var q = CutPieces.Run(plane, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, null, new[] { JsonText.NewId() }).Pieces[0].Main;
            var top = q.Renderers.FirstOrDefault(x => x.Part == "clothes0");
            var topMat = top != null ? q.Materials[top.Materials[0]] : null;
            Check("a used texture kept and renumbered", q.Problem() == null && topMat != null && topMat.Props["_MainTex"].T >= 0 && q.Textures[topMat.Props["_MainTex"].T].Blob == source.Textures[0].Blob && q.Textures.Count == 1,
                $"back piece: {q.Renderers.Count} renderers, {q.Materials.Count} materials, {q.Textures.Count} textures; the top's texture {(topMat == null ? "none" : q.Textures[topMat.Props["_MainTex"].T].Name)}");
        });

        Group("BU8", "sections in the piece's main (spec 10.5): cut renderers inside section, the piece's colour and own fabric switch", () =>
        {
            var f = CutFixture.Character();
            var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var source = CutFixtureMain(f, blobs);
            var sb = source.ToBlob(); string ssha = BakeBlob.Sha256(sb); blobs[ssha] = sb;
            var cut = CutBlock(source, blobs, ssha, null, CutSectionColors.Flesh, CutPlaneStep(waist, CutQuat.Euler(0, 0, 8), CutShapeInfo.Front));
            cut.Section.OwnInside = false;
            var o = CutPieces.Run(cut, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, new[] { CutShapeInfo.Front, CutShapeInfo.Back }, new[] { JsonText.NewId(), JsonText.NewId() });
            int sections = 0, wrong = 0;
            var sourceMeshes = new HashSet<string>(source.Meshes.Select(x => x.Blob));
            foreach (var p in o.Pieces)
            {
                for (int i = 0; i < p.Main.Renderers.Count; i++)
                {
                    var r = p.Main.Renderers[i];
                    bool cutOne = !sourceMeshes.Contains(p.Main.Meshes[r.Mesh].Blob);
                    if (r.Inside == BakeKinds.InsideSection) sections++;
                    if (cutOne != (r.Inside == BakeKinds.InsideSection) && !r.Member) wrong++;
                    if (r.Member && r.Inside != BakeKinds.InsideNone) wrong++;
                }
            }
            Check("Present: a cut renderer", sections > 0, $"{sections} section renderers over two pieces");
            Check("sections on the cut renderers only", wrong == 0, $"{wrong} renderers whose inside does not match whether its mesh changed (members: none)");
            Check("colour and switch", o.Pieces.All(p => p.Main.Insides.Section.Equals(CutSectionColors.Flesh) && !p.Main.Insides.OwnInside && p.Main.Insides.Fill == source.Insides.Fill),
                string.Join(" / ", o.Pieces.Select(p => $"{p.Side}: {p.Main.Insides.Section} own {p.Main.Insides.OwnInside}")));
        });
    }

    /// <summary>The marker objects (spec 10.3), headless: made per row, kinds, the helper layer, hidden for captures and put back,
    /// a marker's shape in a source's root space and a piece placed back at the marker.</summary>
    static void Headless_Cut5_Markers()
    {
        Group("XM", "markers (spec 10.3; the captures' 0 px is U's BI2): objects of the four rows, kinds, captures hide them and put them back, shape in a root space", () =>
        {
            var before = AppItemRows.MarkerObject;
            var made = new List<GameObject>();
            try
            {
                CutMarkers.Install();
                Check("rows build here", AppItemRows.MarkerObject != null && AppItemRows.MarkerObject.Method.Name == nameof(CutMarkers.MakeFor), AppItemRows.MarkerObject?.Method.Name ?? "null");
                var lines = new List<string>();
                bool shapesOk = true;
                for (int id = 1; id <= 5; id++)
                {
                    var row = new StudioItem { Key = new ContentId(AppItemRows.CutGuid, id), Group = AppItemRows.Group, Category = AppItemRows.MarkersCategory, Name = "marker " + id };
                    var go = AppItemRows.MarkerObject(new GeneratedRowRequest(row, "test"));
                    if (id == 5) { shapesOk &= go == null; lines.Add("id 5: " + (go == null ? "none" : "made")); continue; }
                    if (go == null) { shapesOk = false; lines.Add($"id {id}: none"); continue; }
                    made.Add(go);
                    var rs = go.GetComponentsInChildren<MeshRenderer>(true);
                    var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                    bool layer = go.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == ViewSetup.HelperLayer);
                    bool shader = rs.All(r => r.sharedMaterial && r.sharedMaterial.shader && r.sharedMaterial.shader.name == CutMarkers.ShaderName);
                    bool topo = mfs.Length == 2 && mfs[0].sharedMesh.GetTopology(0) == MeshTopology.Triangles && mfs[1].sharedMesh.GetTopology(0) == MeshTopology.Lines;
                    bool noShadow = rs.All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off && !r.receiveShadows);
                    var b = mfs[0].sharedMesh.bounds.size; var want = CutShapeInfo.BaseSize((CutKind)id);
                    bool size = Mathf.Abs(b.x - (float)want.X) < 1e-4f && Mathf.Abs(b.z - (float)want.Z) < 1e-4f;
                    shapesOk &= rs.Length == 2 && layer && shader && topo && noShadow && size;
                    lines.Add($"{CutShapeInfo.Name((CutKind)id)}: {rs.Length} renderers, helper layer {layer}, shader {shader}, faces + lines {topo}, no shadow {noShadow}, face {b.x:F2} x {b.z:F2}");
                }
                Check("the four markers", shapesOk, string.Join("; ", lines));
                // kinds of item states
                var kinds = new[] { ("hs2next.cut#1", 1), ("hs2next.cut#3", 1), ("HS2NEXT.CUT#4", 1), ("hs2next.cut#9", 1), ("hs2next.bake#73100001", 0), ("hs2next.cut#2", 0) }
                    .Select(x => CutMarkers.KindOf(new ItemNodeState { Ref = x.Item1, Group = AppItemRows.Group, Category = x.Item2 })).ToArray();
                Check("kinds", kinds.SequenceEqual(new[] { CutKind.Plane, CutKind.Tear, CutKind.Hole, CutKind.None, CutKind.None, CutKind.None }), string.Join(", ", kinds));
                // captures hide them and put them back; a renderer already off stays off
                var all = made.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToList();
                all[0].forceRenderingOff = true;
                bool inside, nested, after;
                using (CutMarkers.HideForCapture())
                {
                    inside = all.All(r => r.forceRenderingOff);
                    using (CutMarkers.HideForCapture()) nested = all.All(r => r.forceRenderingOff);
                    nested &= all.All(r => r.forceRenderingOff);
                }
                after = all[0].forceRenderingOff && all.Skip(1).All(r => !r.forceRenderingOff);
                all[0].forceRenderingOff = false;
                Check("hidden for captures", inside && nested && after, $"{all.Count} marker renderers: off inside {inside}, still off after a nested scope {nested}, back as they were after {after}");
                // a marker's shape in a source's root space, and a piece placed back at the marker
                var rootW = Matrix4x4.TRS(new Vector3(1.5f, -0.25f, 3f), Quaternion.Euler(0, 40, 0), new Vector3(1.2f, 1.2f, 1.2f));
                var markerW = Matrix4x4.TRS(new Vector3(2f, 11f, 4f), Quaternion.Euler(10, 20, 30), new Vector3(2f, 1f, 0.5f));
                var step = CutMarkers.StepOf(markerW, rootW, CutKind.Box, CutShapeInfo.Inside, null, false);
                var local = rootW.inverse * markerW;
                var lp = (Vector3)local.GetColumn(3);
                var lq = Quaternion.LookRotation(local.GetColumn(2), local.GetColumn(1));
                var ls = new Vector3(local.GetColumn(0).magnitude, local.GetColumn(1).magnitude, local.GetColumn(2).magnitude);
                var sq = new Quaternion(step.Rot.X, step.Rot.Y, step.Rot.Z, step.Rot.W);
                double angle = CutAngleDeg(lq, sq);
                var sz = new Vector3(step.Size.X, step.Size.Y, step.Size.Z);
                Check("the shape in the root space", (new Vector3(step.Pos.X, step.Pos.Y, step.Pos.Z) - lp).magnitude < 1e-4f && angle < 1e-3 && (sz - ls * 4).magnitude < 1e-4f && step.Shape == "box" && step.Keep == "inside",
                    $"pos {step.Pos} against {lp}, rotation {angle:G3} deg apart, size {step.Size} against {ls * 4}");
                var piece = new CutPieceResult { RootOffset = CutXf.TRS(new CutVec(step.Pos.X, step.Pos.Y, step.Pos.Z), new CutQuat(step.Rot.X, step.Rot.Y, step.Rot.Z, step.Rot.W).Normalized, new CutVec(1, 1, 1)), ReRooted = true };
                CutMarkers.PiecePose(rootW, piece, out var pp, out var pq, out var ps);
                var mq = Quaternion.LookRotation(markerW.GetColumn(2), markerW.GetColumn(1));
                Check("a piece at the marker", (pp - (Vector3)markerW.GetColumn(3)).magnitude < 1e-4f && CutAngleDeg(pq, mq) < 1e-3 && (ps - new Vector3(1.2f, 1.2f, 1.2f)).magnitude < 1e-4f,
                    $"piece at {pp} against the marker's {(Vector3)markerW.GetColumn(3)}, rotation {CutAngleDeg(pq, mq):G3} deg apart, scale {ps} (the root's)");
            }
            finally
            {
                foreach (var g in made) if (g) UnityEngine.Object.DestroyImmediate(g);
                AppItemRows.MarkerObject = before;
            }
        });
    }

    /// <summary>The angle between two rotations in degrees, in double, the sign of q aligned (Quaternion.Angle reads 0 under 0.16 deg).</summary>
    static double CutAngleDeg(Quaternion a, Quaternion b)
    {
        double d = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
        double na = Math.Sqrt((double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w), nb = Math.Sqrt((double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w);
        d = Math.Min(1, Math.Abs(d) / (na * nb));
        return 2 * Math.Acos(d) * 180 / Math.PI;
    }

    /// <summary>A cut of a bake item in a host: the procedure run from its block, the pieces' blobs put in the scene, a node per
    /// piece (bake and cut blocks) placed at the source's root times the piece's root offset.</summary>
    internal sealed class CutMade
    {
        public CutOutcome Outcome;
        public readonly List<string> Keys = new List<string>();
        public readonly List<ItemRealization> Items = new List<ItemRealization>();
    }

    internal static CutMade CutBakeItem(Host h, string bakeKey, BakeMain sourceMain, Func<string, byte[]> blobOf, IDictionary<string, Vec3> pose, CutStepDef step, string[] keeps, Vec3 color, bool ownInside = true)
    {
        var st = (ItemNodeState)h.Rt.Model.Find(bakeKey).State;
        var cut = CutPieces.NewBlock(CutSource.Of(st.Bake), pose, new[] { CutPieces.BlockOf(step) }, color, ownInside);
        var ids = keeps.Select(_ => JsonText.NewId()).ToArray();
        var o = CutPieces.Run(cut, sourceMain, blobOf, keeps, ids);
        var made = new CutMade { Outcome = o };
        if (o.Error != null) return made;
        var holder = h.Rt.Realizations.OfType<ItemRealization>().First(x => x.NodeId == bakeKey).Holder;
        var res = h.Rt.Session.Model.File.Res;
        foreach (var p in o.Pieces)
        {
            if (p.Empty) continue;
            foreach (var kv in p.NewBlobs) res.AddBlob(kv.Value);
            var ps = BakeState(p.Bake, null, "piece " + p.Side);
            ps.Cut = p.Cut;
            var r = h.Session.AddItem(ps);
            if (!Check("the piece's node is added (" + p.Side + ")", r.Ok, r.Code + " " + r.Message)) continue;
            CutMarkers.PiecePose(holder.localToWorldMatrix, p, out var pos, out var rot, out var scale);
            var e = rot.eulerAngles;
            h.Session.Set(r.NewId, "transform.pos", new Vec3(pos.x, pos.y, pos.z));
            h.Session.Set(r.NewId, "transform.rot", new Vec3(e.x, e.y, e.z));
            h.Session.Set(r.NewId, "transform.scale", new Vec3(scale.x, scale.y, scale.z));
            made.Keys.Add(r.NewId);
        }
        h.Rt.ApplyNow();
        foreach (var k in made.Keys) made.Items.Add(h.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == k));
        return made;
    }

    /// <summary>World positions of every vertex a renderer draws (skinned: BakeMesh at the current pose).</summary>
    static Vector3[] CutWorldVerts(Renderer r)
    {
        if (r is SkinnedMeshRenderer s)
        {
            var m = new Mesh();
            try
            {
                s.BakeMesh(m, true);   // scaled, relative to the renderer's position and rotation
                var v = m.vertices;
                var l2w = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);
                for (int i = 0; i < v.Length; i++) v[i] = l2w.MultiplyPoint3x4(v[i]);
                return v;
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }
        var mf = r.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh) return Array.Empty<Vector3>();
        var w = mf.sharedMesh.vertices;
        var t = r.transform.localToWorldMatrix;
        for (int i = 0; i < w.Length; i++) w[i] = t.MultiplyPoint3x4(w[i]);
        return w;
    }

    static void Render_Cut1_Live()
    {
        Group("BU12", "a live character (spec 10.9): the cut takes the drawn frame; later motion of the character moves nothing in the pieces; sections built per cut renderer", Bu12);
        Group("BU11", "cut time on a whole character (Editor numbers, informative; the accepted numbers are T's player rows, BW5)", Bu11);
    }

    static void Bu12()
    {
        ClearStore();
        var h = NewHost();
        var before = AppItemRows.MarkerObject;
        try
        {
            CutMarkers.Install();
            string ck = LoadChara(h, false);
            var c = Chara(h, ck);
            EyesOpen(h, c);
            var drawn = c.Root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToDictionary(r => r, CutWorldVerts);
            var bi = BakeToItem(h, c, new BakeOptions(), false, "BU12");
            if (!Check("the source bake item", bi.It != null && bi.It.Source == "bake" && !bi.It.IsFallback, bi.It?.Source)) return;
            var job = bi.Job;
            float height = c.Root.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).Max() - c.Root.transform.position.y;
            var step = CutPlaneStep(new CutVec(0, 0.55 * height, 0), CutQuat.Euler(0, 0, 6), CutShapeInfo.Front);
            var made = CutBakeItem(h, bi.Key, job.Main, sha => job.Blobs.TryGetValue(sha, out var b) ? b : h.Rt.Session.Model.File.Res.Get(BakeBlob.ResName(sha)), null, step,
                new[] { CutShapeInfo.Front, CutShapeInfo.Back }, CutSectionColors.DarkRed);
            var o = made.Outcome;
            if (!Check("two pieces built from their bakes", o.Error == null && made.Items.Count == 2 && made.Items.All(it => it != null && it.Source == "bake" && !it.IsFallback),
                o.Error != null ? o.Error + " " + o.ErrorText : $"{made.Items.Count} items; cut renderers {string.Join(",", o.Pieces.Select(p => p.CutRenderers.Count))}; procedure {o.Run.Milliseconds:F1} ms, with the blobs {o.Milliseconds:F1} ms")) return;
            // the drawn frame: a renderer the cut left whole draws in its piece exactly where the character drew it
            int whole = 0, compared = 0; double worst = 0;
            var byName = drawn.Keys.GroupBy(r => r.name).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
            var pieceRenderers = made.Items.SelectMany(it => it.Instance.GetComponentsInChildren<Renderer>(true)).Where(r => !r.name.EndsWith(BakeInsides.Suffix, StringComparison.Ordinal)).ToList();
            foreach (var pr in pieceRenderers)
            {
                if (!byName.TryGetValue(pr.name, out var src)) continue;
                var pv = CutWorldVerts(pr); var sv = drawn[src];
                if (pv.Length != sv.Length) continue;
                whole++;
                for (int i = 0; i < pv.Length; i++) { worst = Math.Max(worst, (pv[i] - sv[i]).magnitude); compared++; }
            }
            Check("the drawn frame", whole >= 5 && worst <= 1e-3, $"{whole} renderers left whole drawn by the pieces, {compared} vertices: largest distance from the character's drawn vertex {worst:G3} units (bar 1e-3)");
            // sections: a reversed copy per cut renderer that may have one (members never), in the piece's colour or its own material
            int expected = 0, copies = 0, wrongMat = 0;
            foreach (var (p, it) in o.Pieces.Where(p => !p.Empty).Zip(made.Items, (p, it) => (p, it)))
            {
                var b = BakeBlobs.FromMain(p.Main);
                foreach (var r in b.Renderers) if (r.Inside == BakeData.InsideSection && BakeInsides.Eligible(b, r)) expected++;
                foreach (var r in it.Instance.GetComponentsInChildren<Renderer>(true).Where(x => x.name.EndsWith(BakeInsides.Suffix, StringComparison.Ordinal)))
                {
                    copies++;
                    var outside = r.transform.parent ? r.transform.parent.GetComponent<Renderer>() : null;
                    foreach (var m in r.sharedMaterials) if (m && m.shader.name != "HS2Next/Unlit/Color" && (outside == null || !outside.sharedMaterials.Any(om => om && om.shader == m.shader))) wrongMat++;
                }
            }
            Check("a section per cut renderer", expected > 0 && copies == expected && wrongMat == 0, $"{expected} cut renderers that may have one, {copies} reversed copies built, {wrongMat} copies in neither the colour nor their own material");
            // later motion of the character: the pieces do not move
            var pieceBefore = pieceRenderers.ToDictionary(r => r, CutWorldVerts);
            var bones = c.Root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => t.position);
            h.Session.Set(ck, "transform.pos", new Vec3(3, 0, 1));
            h.Ok(new { cmd = "chara.anime", key = ck, group = 19, category = 0, no = 0, time = 0.8f, speed = 1f }, "the character moves on");
            h.Rt.ApplyNow();
            h.Frames(30);
            double moved = bones.Where(kv => kv.Key).Max(kv => (kv.Key.position - kv.Value).magnitude);
            Check("Present: the character moved", moved >= 1, $"its bones moved up to {moved:F2} units");
            double drift = 0;
            foreach (var kv in pieceBefore)
            {
                if (!kv.Key) continue;
                var now = CutWorldVerts(kv.Key);
                for (int i = 0; i < now.Length && i < kv.Value.Length; i++) drift = Math.Max(drift, (now[i] - kv.Value[i]).magnitude);
            }
            Check("the pieces stay", drift <= 1e-6, $"{pieceBefore.Count} piece renderers: largest vertex move {drift:G3} units (bar 1e-6)");
            job.Dispose();
        }
        finally { AppItemRows.MarkerObject = before; h.Dispose(); ClearStore(); }
    }

    static void Bu11()
    {
        ClearStore();
        var h = NewHost();
        try
        {
            foreach (bool male in new[] { false, true })
            {
                string ck = LoadChara(h, male);
                var c = Chara(h, ck);
                EyesOpen(h, c);
                var job = new BakeJob();
                job.Capture = BakeCapture.Chara(c, new BakeOptions(), job);
                job.Finish();
                var main = job.Main;
                Func<string, byte[]> blobOf = sha => job.Blobs.TryGetValue(sha, out var b) ? b : null;
                float height = c.Root.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).Max() - c.Root.transform.position.y;
                var src = new CutSource { Id = main.Id, Main = BakeBlob.ResName(job.MainSha) };
                foreach (var sha in main.DataBlobs()) src.Data.Add(BakeBlob.ResName(sha));
                string who = male ? "HERO107" : "BR-Chan";
                var arm = c.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "cf_J_ArmLow01_L");
                var armLocal = arm ? c.Root.transform.InverseTransformPoint(arm.position) : new Vector3(3, 0.62f * height, 0);
                var cases = new (string what, CutStepDef step, string[] keeps, double bar)[]
                {
                    ("plane at 55 % of the height, clean, both sides", CutPlaneStep(new CutVec(0, 0.55 * height, 0), CutQuat.Identity, CutShapeInfo.Front), new[] { CutShapeInfo.Front, CutShapeInfo.Back }, 100),
                    ("box of 4 units around the left forearm, clean, both", CutShapeStep(CutKind.Box, new CutVec(armLocal.x, armLocal.y, armLocal.z), CutQuat.Identity, new CutVec(4, 4, 4), CutShapeInfo.Inside), new[] { CutShapeInfo.Inside, CutShapeInfo.Outside }, 100),
                    ("plane at 55 %, ragged 3 cm, tooth 1.5 cm, both sides", CutPlaneStep(new CutVec(0, 0.55 * height, 0), CutQuat.Identity, CutShapeInfo.Front, new CutRagParams { Amp = 0.3, Tooth = 0.15, Random = 0.5, Seed = 1 }), new[] { CutShapeInfo.Front, CutShapeInfo.Back }, 3000),
                };
                foreach (var (what, step, keeps, bar) in cases)
                {
                    var cut = CutPieces.NewBlock(src, null, new[] { CutPieces.BlockOf(step) }, CutSectionColors.DarkRed, true);
                    var sw = Stopwatch.StartNew();
                    var o = CutPieces.Run(cut, main, blobOf, keeps, keeps.Select(_ => JsonText.NewId()).ToArray());
                    double all = sw.Elapsed.TotalMilliseconds;
                    if (o.Error != null) { Check($"{who}: {what}", false, o.Error + " " + o.ErrorText); continue; }
                    var r = o.Run;
                    Measure($"BU11 Editor {who}: {what}: procedure {r.Milliseconds:F1} ms ({r.MeshesWorked} meshes cut, {r.MeshesWhole} whole, {r.Crossings} new vertices on the surfaces, refinement +{r.RefineAdded}{(r.RefineLimited ? " limited" : "")}), with the piece blobs {all:F1} ms; BW5's bar {bar} ms is the player's");
                    if (r.Milliseconds > bar) Warn($"BU11 Editor {who}: {what}: procedure {r.Milliseconds:F1} ms over the player's bar {bar} ms (Editor number, the lead decides)");
                }
                job.Dispose();
                h.Session.Delete(new[] { ck });
                h.Rt.ApplyNow();
            }
        }
        finally { h.Dispose(); ClearStore(); }
    }

    /// <summary>BU9 in a host (headless): pieces as item nodes with their bake and cut blocks build from the stored result; a save and a
    /// load in a fresh host build them again with no run of the procedure.</summary>
    static void Headless_Cut6_Load()
    {
        Group("BU9", "procedure and stored result in a scene (spec 10.8): pieces build from their stored result; a save and a load run the procedure 0 times", () =>
        {
            ClearStore();
            string path = null;
            var keys = new List<string>();
            var mains = new List<string>();
            var h = NewHost();
            try
            {
                var f = CutFixture.Character();
                var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var source = CutFixtureMain(f, blobs);
                var sb = source.ToBlob(); string ssha = BakeBlob.Sha256(sb); blobs[ssha] = sb;
                var res = h.Rt.Session.Model.File.Res;
                foreach (var kv in blobs) res.AddBlob(kv.Value);
                var cut = CutBlock(source, blobs, ssha, new Dictionary<string, Vec3> { ["cf_J_Spine02"] = new Vec3(6, 0, 0) }, CutSectionColors.DarkRed,
                    CutPlaneStep(new CutVec(0, 11.2, 0), CutQuat.Euler(0, 0, 8), CutShapeInfo.Front, new CutRagParams { Amp = 0.05, Tooth = 0.25, Random = 0.5, Seed = 3 }));
                var o = CutPieces.Run(cut, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, new[] { CutShapeInfo.Front, CutShapeInfo.Back }, new[] { JsonText.NewId(), JsonText.NewId() });
                if (!Check("two pieces", o.Error == null && o.Pieces.Count == 2, o.Error ?? "")) return;
                int runs0 = CutProcedure.Runs;
                foreach (var p in o.Pieces)
                {
                    foreach (var kv in p.NewBlobs) res.AddBlob(kv.Value);
                    var st = BakeState(p.Bake, null, "piece " + p.Side);
                    st.Cut = p.Cut;
                    var r = h.Session.AddItem(st);
                    if (!Check("the piece's node (" + p.Side + ")", r.Ok, r.Code + " " + r.Message)) return;
                    keys.Add(r.NewId); mains.Add(p.MainSha);
                }
                h.Rt.ApplyNow();
                var items = keys.Select(k => h.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == k)).ToList();
                Check("built from the stored result", items.All(it => it != null && it.Source == "bake" && !it.IsFallback) && CutProcedure.Runs == runs0,
                    $"{items.Count(it => it != null && it.Source == "bake" && !it.IsFallback)} of 2 drawn from their bakes; procedure runs {CutProcedure.Runs - runs0}");
                var sr = h.Ok(new { cmd = "scene.save", name = "bu9_cut", overwrite = true, thumb = "placeholder" }, "BU9 scene.save");
                path = (string)sr["path"];
            }
            finally { h.Dispose(); }
            if (path == null) return;
            var h2 = NewHost();
            try
            {
                int runs0 = CutProcedure.Runs;
                var lr = h2.Ok(new { cmd = "scene.load", path }, "BU9 scene.load");
                h2.Rt.ApplyNow();
                var items = keys.Select(k => h2.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == k)).ToList();
                var states = keys.Select(k => h2.Rt.Model.Find(k)?.State as ItemNodeState).ToList();
                bool blocks = states.All(s => s?.Bake != null && s.Cut != null) && states.Select(s => BakeBlob.ShaOf(s.Bake.Main)).SequenceEqual(mains);
                var findings = h2.Rt.Findings.Where(x => x.Code != null && (x.Code.StartsWith("bake.") || x.Code.StartsWith("cut."))).Select(x => x.Code).ToList();
                Check("loaded: built from the result, the procedure not run", items.All(it => it != null && it.Source == "bake" && !it.IsFallback) && CutProcedure.Runs == runs0 && blocks && findings.Count == 0,
                    $"{items.Count(it => it != null && it.Source == "bake" && !it.IsFallback)} of 2 drawn from their bakes, procedure runs {CutProcedure.Runs - runs0} (counted 0 is the rule), blocks kept {blocks}, findings {string.Join(", ", findings)}");
            }
            finally { h2.Dispose(); try { File.Delete(path); } catch (Exception) { } ClearStore(); }
        });
    }

    /// <summary>BU9 at a load (headless): a piece whose stored result's main is missing from the scene builds from its procedure run again
    /// (the source present): equal bits, cut.recomputed; saved again, a fresh load runs nothing.</summary>
    static void Headless_Cut7_Recompute()
    {
        Group("BU9", "a missing result at the build (spec 10.8): computed again from the source, equal to the stored one; saved, a load runs nothing", () =>
        {
            ClearStore();
            var beforeHook = BakeProvider.Recompute;
            var beforeRows = AppItemRows.MarkerObject;
            string path = null, key = null, mainSha = null, mainRes = null;
            byte[] mainBlob = null;
            var h = NewHost();
            try
            {
                CutMarkers.Install();
                Check("the hook is X's", BakeProvider.Recompute != null && BakeProvider.Recompute.Method.Name == nameof(CutPieces.Recompute), BakeProvider.Recompute?.Method.Name ?? "null");
                var f = CutFixture.Character();
                var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var source = CutFixtureMain(f, blobs);
                var sb = source.ToBlob(); string ssha = BakeBlob.Sha256(sb); blobs[ssha] = sb;
                var res = h.Rt.Session.Model.File.Res;
                foreach (var kv in blobs) res.AddBlob(kv.Value);
                var cut = CutBlock(source, blobs, ssha, null, CutSectionColors.DarkRed, CutPlaneStep(new CutVec(0, 11.2, 0), CutQuat.Euler(0, 0, 8), CutShapeInfo.Back));
                var o = CutPieces.Run(cut, source, sha => blobs.TryGetValue(sha, out var b) ? b : null, null, new[] { JsonText.NewId() });
                if (!Check("the piece", o.Error == null && o.Pieces.Count == 1, o.Error ?? "")) return;
                var p = o.Pieces[0];
                mainSha = p.MainSha; mainBlob = p.MainBlob; mainRes = p.Bake.Main;
                foreach (var kv in p.NewBlobs) if (kv.Key != p.MainSha) res.AddBlob(kv.Value);
                Check("Present: the result's main is not in the scene, the source is", !res.Contains(p.Bake.Main) && res.Contains(cut.Source.Main), $"main {p.Bake.Main} in res {res.Contains(p.Bake.Main)}, source main in res {res.Contains(cut.Source.Main)}");
                int runs0 = CutProcedure.Runs;
                var st = BakeState(p.Bake, null, "piece recomputed");
                st.Cut = p.Cut;
                var r = h.Session.AddItem(st);
                if (!Check("the piece's node", r.Ok, r.Code + " " + r.Message)) return;
                key = r.NewId;
                h.Rt.ApplyNow();
                var it = h.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == key);
                var rec = h.Rt.Findings.Where(x => x.NodeId == key && x.Code == BakeFindings.CutRecomputed).ToList();
                var missing = h.Rt.Findings.Where(x => x.NodeId == key && x.Code == BakeFindings.Missing).ToList();
                Check("built from the procedure run again", it != null && it.Source == "bake" && !it.IsFallback && CutProcedure.Runs - runs0 == 1 && rec.Count == 1 && rec[0].Severity == FindingSeverity.Info && missing.Count == 0,
                    it == null ? "no item" : $"source {it.Source}, fallback {it.IsFallback}, procedure runs {CutProcedure.Runs - runs0}, cut.recomputed {rec.Count} ({rec.FirstOrDefault()?.Severity}), bake.missing {missing.Count}");
                Check("equal in bits to the stored one", res.Contains(mainRes) && res.Get(mainRes).SequenceEqual(mainBlob), $"the main {mainRes} back in the scene's res {res.Contains(mainRes)}, equal {res.Contains(mainRes) && res.Get(mainRes).SequenceEqual(mainBlob)}");
                var sr = h.Ok(new { cmd = "scene.save", name = "bu9_recompute", overwrite = true, thumb = "placeholder" }, "BU9 scene.save after the recompute");
                path = (string)sr["path"];
            }
            finally { h.Dispose(); BakeProvider.Recompute = beforeHook; AppItemRows.MarkerObject = beforeRows; }
            if (path == null) return;
            var h2 = NewHost();
            try
            {
                CutMarkers.Install();
                int runs0 = CutProcedure.Runs;
                h2.Ok(new { cmd = "scene.load", path }, "BU9 scene.load");
                h2.Rt.ApplyNow();
                var it = h2.Rt.Realizations.OfType<ItemRealization>().FirstOrDefault(x => x.NodeId == key);
                var codes = h2.Rt.Findings.Where(x => x.Code != null && (x.Code.StartsWith("bake.") || x.Code.StartsWith("cut."))).Select(x => x.Code).ToList();
                Check("saved with the result: a load runs nothing", it != null && it.Source == "bake" && !it.IsFallback && CutProcedure.Runs == runs0 && codes.Count == 0,
                    it == null ? "no item" : $"source {it.Source}, fallback {it.IsFallback}, procedure runs {CutProcedure.Runs - runs0}, findings {string.Join(", ", codes)}");
            }
            finally { h2.Dispose(); BakeProvider.Recompute = beforeHook; AppItemRows.MarkerObject = beforeRows; try { File.Delete(path); } catch (Exception) { } ClearStore(); }
        });
    }

    /// <summary>The rotation that takes +Y to a direction (double).</summary>
    static CutQuat CutUpTo(CutVec n)
    {
        n = n.Normalized;
        var up = new CutVec(0, 1, 0);
        double d = CutVec.Dot(up, n);
        if (d < -0.999999) return new CutQuat(1, 0, 0, 0);
        var c = CutVec.Cross(up, n);
        return new CutQuat(c.X, c.Y, c.Z, 1 + d).Normalized;
    }

    /// <summary>A renderer's mesh of a main (decoded from its blob), and its vertices posed by the main's own nodes (no pose over them),
    /// in the main's root space.</summary>
    static (CutMesh mesh, List<CutVec> posed) CutPosedOfMain(BakeMain main, Func<string, byte[]> blobOf, int renderer)
    {
        var inp = CutPieces.InputOf(main, blobOf);
        var r = inp.Renderers[renderer];
        var mesh = inp.MeshOf(r.Mesh);
        var w = CutProcedure.NodeMatrices(inp, null);
        var v = new CutVerts(mesh, CutProcedure.PoserOf(r, mesh, w));
        var l = new List<CutVec>(mesh.VertexCount);
        for (int i = 0; i < mesh.VertexCount; i++) l.Add(v.PosedAt(i));
        return (mesh, l);
    }

    /// <summary>BU10 at another pose (spec 8.2 "Per-step pose", the lead's ruling): a piece posed again and cut again keeps its first cut.</summary>
    static void Headless_Cut8_PoseChain()
    {
        Group("BU10", "a re-cut at another pose (spec 8.2 per-step pose): each step decided at its own pose; the first cut's edge stays", () =>
        {
            var f = CutFixture.Character();
            var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var source = CutFixtureMain(f, blobs);
            var sb = source.ToBlob(); string ssha = BakeBlob.Sha256(sb); blobs[ssha] = sb;
            Func<string, byte[]> blobOf = sha => blobs.TryGetValue(sha, out var b) ? b : null;
            var srcIn = CutPieces.InputOf(source, blobOf);
            // step 1 at P1: the arm beyond x = 4 (an elbow bent)
            var p1 = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) { ["cf_J_ArmLow01_L"] = new Vec3(0, -25, 0) };
            var s1 = CutPlaneStep(new CutVec(4, 14, 0), CutQuat.Euler(0, 0, 90), CutShapeInfo.Back);
            var block1 = CutBlock(source, blobs, ssha, p1, CutSectionColors.DarkRed, s1);
            var o1 = CutPieces.Run(block1, source, blobOf, null, new[] { JsonText.NewId() });
            if (!Check("the first piece", o1.Error == null && o1.Pieces.Count == 1 && o1.Pieces[0].CutRenderers.Contains(f.Arm), o1.Error ?? $"cut renderers {string.Join(",", o1.Pieces[0].CutRenderers)}")) return;
            var piece1 = o1.Pieces[0];
            foreach (var kv in piece1.NewBlobs) blobs[kv.Key] = kv.Value;
            int armIn1 = piece1.Main.Renderers.FindIndex(r => r.Part == "body" && piece1.Main.Meshes[r.Mesh].Blob != source.Meshes[source.Renderers[f.Arm].Mesh].Blob);
            // the piece's FK as it shows (the pose's values of its own FK bones), then its upper arm turned 30 deg
            var shown = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
            foreach (var bone in piece1.Main.Fk) if (p1.TryGetValue(bone, out var v)) shown[bone] = v;
            bool hasUpper = piece1.Main.Fk.Contains("cf_J_ArmUp00_L");
            shown["cf_J_ArmUp00_L"] = new Vec3(0, 0, 30);
            var p2 = CutPieces.ReCutPose(piece1.Cut, true, shown);
            // Present: the first cut's region moves >= 1 unit from P1 to P2
            var mesh1 = CutPieces.MeshOf(BakeMeshData.FromBlob(blobOf(piece1.Main.Meshes[piece1.Main.Renderers[armIn1].Mesh].Blob)));
            var edge = CutBoundary(mesh1.SubMeshes).ToList();
            var srcArm = srcIn.Renderers[f.Arm];
            var vP1 = new CutVerts(mesh1, CutProcedure.PoserOf(srcArm, mesh1, CutProcedure.NodeMatrices(srcIn, CutPieces.PoseOf(p1))));
            var vP2 = new CutVerts(mesh1, CutProcedure.PoserOf(srcArm, mesh1, CutProcedure.NodeMatrices(srcIn, CutPieces.PoseOf(p2))));
            double moved = edge.Count == 0 ? 0 : edge.Max(i => (vP2.PosedAt(i) - vP1.PosedAt(i)).Length);
            Check("Present: the first cut's region moves", hasUpper && edge.Count > 0 && moved >= 1, $"the piece's FK {string.Join(", ", piece1.Main.Fk)}; its {edge.Count} edge vertices move up to {moved:F3} units from P1 to P2 (floor 1)");
            // step 2 at P2: a plane across the posed forearm, keeping the elbow's side (the first cut's edge)
            var w2 = CutProcedure.NodeMatrices(srcIn, CutPieces.PoseOf(p2));
            var elbow = w2[f.Node["cf_J_ArmLow01_L"]].Translation; var hand = w2[f.Node["cf_J_Hand_L"]].Translation;
            var mid = CutVec.Lerp(elbow, hand, 0.6);
            var s2 = CutPlaneStep(mid, CutUpTo(hand - elbow), CutShapeInfo.Back);
            var block2 = CutPieces.Chain(piece1.Cut, CutPieces.BlockOf(s2), p2, null);
            var same = CutPieces.Chain(piece1.Cut, CutPieces.BlockOf(s2), CutPieces.ReCutPose(piece1.Cut, true, new SortedDictionary<string, Vec3>(StringComparer.Ordinal)), null);
            // (d) step 2 holds a pose, step 1 does not; an unchanged pose adds none
            Check("(d) the poses in the block", block2.Steps.Count == 2 && block2.Steps[0].Pose == null && block2.Steps[1].Pose != null && CutPieces.SamePose(block2.Steps[1].Pose, p2) && CutPieces.SamePose(block2.Pose, p1)
                && same.Steps[1].Pose == null,
                $"block pose {Fmt(block2.Pose)}; step 1 {Fmt(block2.Steps[0].Pose)}; step 2 {Fmt(block2.Steps[1].Pose)}; a re-cut at the unchanged pose: step 2 {Fmt(same.Steps[1].Pose)}");
            var o2 = CutPieces.Run(block2, source, blobOf, null, new[] { JsonText.NewId() });
            if (!Check("the chained piece", o2.Error == null && o2.Pieces.Count == 1 && !o2.Pieces[0].Empty, o2.Error ?? "")) return;
            var pieceA = o2.Pieces[0];
            foreach (var kv in pieceA.NewBlobs) blobs[kv.Key] = kv.Value;
            // (a) cutting the stored first result again at P2 (its own FK values; step 2 in its root space)
            var inv1 = piece1.RootOffset.Inverse();
            var s2b = s2.Clone(); s2b.Pos = inv1.Point(s2.Pos); s2b.Rot = (CutQuat.FromMatrix(inv1) * s2.Rot).Normalized;
            var blockB = CutPieces.NewBlock(CutSource.Of(piece1.Bake), shown, new[] { CutPieces.BlockOf(s2b) }, CutSectionColors.DarkRed, true);
            var oB = CutPieces.Run(blockB, piece1.Main, blobOf, null, new[] { JsonText.NewId() });
            if (!Check("the first result cut again", oB.Error == null && oB.Pieces.Count == 1, oB.Error ?? "")) return;
            var pieceB = oB.Pieces[0];
            foreach (var kv in pieceB.NewBlobs) blobs[kv.Key] = kv.Value;
            double worst = 0; int n = 0, differ = 0;
            if (pieceA.Main.Renderers.Count != pieceB.Main.Renderers.Count) differ++;
            for (int i = 0; i < Math.Min(pieceA.Main.Renderers.Count, pieceB.Main.Renderers.Count); i++)
            {
                var (ma, pa) = CutPosedOfMain(pieceA.Main, blobOf, i);
                var (mb, pb) = CutPosedOfMain(pieceB.Main, blobOf, i);
                if (ma.VertexCount != mb.VertexCount) { differ++; continue; }
                for (int k = 0; k < 3 * ma.VertexCount; k++) worst = Math.Max(worst, Math.Abs(ma.Positions[k] - mb.Positions[k]));
                for (int k = 0; k < pa.Count; k++)
                    worst = Math.Max(worst, (pieceA.RootOffset.Point(pa[k]) - piece1.RootOffset.Point(pieceB.RootOffset.Point(pb[k]))).Length);
                n += ma.VertexCount;
            }
            Check("(a) equal to cutting the stored first result again at P2", differ == 0 && n > 0 && worst <= 1e-6,
                $"{pieceA.Main.Renderers.Count} renderers, {n} vertices (bind, and posed in the source's root space): largest difference {worst:G3} (bar 1e-6); renderers or counts differing {differ}");
            // (b) the first cut's edge, where step 2 keeps it, is unchanged in bind space
            var surf2 = CutSurface.Of(s2);
            int armA = pieceA.Main.Renderers.FindIndex(r => r.Node == piece1.Main.Renderers[armIn1].Node);   // both pieces re-rooted once from the source
            var meshA = armA >= 0 ? CutPieces.MeshOf(BakeMeshData.FromBlob(blobOf(pieceA.Main.Meshes[pieceA.Main.Renderers[armA].Mesh].Blob))) : null;
            int kept = 0, matched = 0; double edgeWorst = 0;
            foreach (int i in edge)
            {
                if (surf2.Face(0, vP2.PosedAt(i)) > 0) continue;   // step 2 keeps the back
                kept++;
                if (meshA == null) continue;
                var pp = mesh1.Position(i);
                double best = double.MaxValue;
                for (int j = 0; j < meshA.VertexCount; j++) best = Math.Min(best, (meshA.Position(j) - pp).Length);
                if (best <= 1e-6) matched++;
                edgeWorst = Math.Max(edgeWorst, best);
            }
            Check("(b) the first cut's edge unchanged in bind space", meshA != null && kept > 0 && matched == kept,
                $"{edge.Count} edge vertices of the first cut, {kept} on the side step 2 keeps, {matched} found in the new piece within 1e-6 (largest distance {edgeWorst:G3})");
            // (c) recomputed from the stored data: equal in bits
            var res = new ResStore();
            foreach (var kv in blobs) if (kv.Key != pieceA.MainSha) res.AddBlob(kv.Value);
            var findings = new FindingList();
            var set = CutPieces.Recompute(pieceA.Cut, pieceA.Bake, res, null, findings, "test");
            Check("(c) recomputed from the stored data equals the stored result", set != null && set.MainSha == pieceA.MainSha && res.Get(pieceA.Bake.Main).SequenceEqual(pieceA.MainBlob),
                set == null ? "null: " + string.Join(", ", findings.Select(x => x.Code)) : $"main {set.MainSha.Substring(0, 12)} against {pieceA.MainSha.Substring(0, 12)}");
        });
    }

    static string Fmt(IDictionary<string, Vec3> p) => p == null ? "none" : "{" + string.Join(", ", p.Select(kv => $"{kv.Key} ({kv.Value.X:R}, {kv.Value.Y:R}, {kv.Value.Z:R})")) + "}";

    // ------------------------------------------------------------------ YC1, YC2: the cut's finish

    /// <summary>A cut through CutPrepare + CutCommit (the path of cut.make) and what its finish did.</summary>
    sealed class YcCut
    {
        public string Error;
        public int Renderers, Pieces, FinishSteps, FinishWaits, OldLoopSteps;
        public double OldLoopMs, CommitMs;
        public List<string> PieceKeys = new List<string>();
        /// <summary>The pieces' main SHA-256 of today's loop run on the same steps (the plan's block, source and ids).</summary>
        public List<string> OldLoopShas = new List<string>();
    }

    static T YcPlanField<T>(CutPlan plan, string name)
    {
        var f = typeof(CutPlan).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (f == null) throw new MissingFieldException(nameof(CutPlan), name);
        return (T)f.GetValue(plan);
    }

    /// <summary>Prepares a cut by a plane (both sides), runs the same steps once through the loop that only turns (Finish before 6.10:
    /// <c>while (e.MoveNext()) { }</c>) and counts its MoveNext calls, then commits the plan (Finish inside) and reads its counters.</summary>
    static YcCut YcCutThrough(Host h, string target, Vector3 pos, Quaternion rot, string name)
    {
        var y = new YcCut();
        var p = h.App.Ops.CutPrepare(new CutMakeRequest { Target = target, Shape = new CutShapeGiven { Kind = CutShapeInfo.Plane, Pos = pos, Rot = rot }, Mode = CutShapeInfo.Both, Hide = false, Name = name });
        if (!p.Ok || !(p.Value is CutPlan plan)) { y.Error = "CutPrepare: " + p.Code + " " + p.Message; return y; }
        var block = YcPlanField<ItemCut>(plan, "Block");
        var source = YcPlanField<BakeMain>(plan, "SourceMain");
        var blobOf = YcPlanField<Func<string, byte[]>>(plan, "BlobOf");
        var keeps = YcPlanField<string[]>(plan, "Keeps");
        var ids = YcPlanField<string[]>(plan, "Ids");
        y.Renderers = source.Renderers.Count;
        y.Pieces = keeps?.Length ?? 1;
        // today's loop on the same steps
        var sw = Stopwatch.StartNew();
        var e = CutPieces.Steps(block, source, blobOf, keeps, ids, out var old);
        int n = 1;
        while (e.MoveNext()) n++;
        y.OldLoopSteps = n;
        y.OldLoopMs = sw.Elapsed.TotalMilliseconds;
        if (old.Error == null) y.OldLoopShas.AddRange(old.Pieces.Select(x => x.MainSha));
        sw.Restart();
        var r = h.App.Ops.CutCommit(plan);
        y.CommitMs = sw.Elapsed.TotalMilliseconds;
        y.FinishSteps = YcPlanField<int>(plan, "FinishSteps");
        y.FinishWaits = YcPlanField<int>(plan, "FinishWaits");
        if (!r.Ok || !(r.Value is CutMakeResult made)) { y.Error = "CutCommit: " + r.Code + " " + r.Message; return y; }
        y.PieceKeys.AddRange(made.Pieces);
        return y;
    }

    /// <summary>YC1: Finish waits on the pending work: its MoveNext calls at most the source's renderers + the pieces + 3, with the work pending at
    /// least once (Present); today's loop's count printed beside.</summary>
    static void YcFinishChecks(YcCut y, string what)
    {
        if (!Check($"{what}: the cut through CutPrepare + CutCommit", y.Error == null, y.Error ?? $"{y.PieceKeys.Count} pieces")) return;
        int bar = y.Renderers + y.Pieces + 3;
        Check($"Present: {what}: the outcome's work was pending during Finish", y.FinishWaits >= 1, $"Finish waited {y.FinishWaits} times on the pending work");
        Check($"{what}: Finish's MoveNext calls <= the source's renderers + the pieces + 3", y.FinishSteps <= bar,
            $"{y.FinishSteps} calls (bar {bar} = {y.Renderers} renderers + {y.Pieces} pieces + 3); the loop that only turns made {y.OldLoopSteps} calls on the same steps ({y.OldLoopMs:F1} ms); the commit {y.CommitMs:F1} ms");
        Measure($"YC1 {what}: Finish {y.FinishSteps} MoveNext calls, {y.FinishWaits} waits, commit {y.CommitMs:F1} ms; the loop that only turns {y.OldLoopSteps} calls, {y.OldLoopMs:F1} ms (Editor numbers)");
    }

    /// <summary>YC2: the pieces' stored mains equal CutPieces.Run's from their stored source and steps (BU9's form); Present: they hold a new mesh blob.</summary>
    static void YcSameBits(Host h, YcCut y, string what)
    {
        if (!Check($"{what}: the cut", y.Error == null && y.PieceKeys.Count > 0, y.Error ?? $"{y.PieceKeys.Count} pieces")) return;
        var res = h.Rt.Session.Model.File.Res;
        var states = y.PieceKeys.Select(k => h.Rt.Model.Find(k)?.State as ItemNodeState).ToList();
        if (!Check($"{what}: the pieces' blocks", states.All(s => s?.Bake != null && s.Cut != null), string.Join(", ", states.Select(s => s?.Bake != null && s.Cut != null)))) return;
        var src = BakeBlobSet.Gather(states[0].Cut.Source, res, null, null, y.PieceKeys[0]);
        if (!Check($"{what}: the source gathers from the scene", src.Status == BakeBlobSet.Result.Ok, src.Status.ToString())) return;
        var srcData = new HashSet<string>(src.Main.DataBlobs(), StringComparer.Ordinal);
        int newMeshes = 0;
        var stored = new List<string>();
        foreach (var s in states)
        {
            stored.Add(BakeBlob.ShaOf(s.Bake.Main));
            var own = BakeBlobSet.Gather(s.Bake, res, null, null, "yc2");
            if (own.Status == BakeBlobSet.Result.Ok) newMeshes += own.Main.Meshes.Count(m => !srcData.Contains(m.Blob));
        }
        Check($"Present: {what}: the pieces hold new mesh blobs", newMeshes >= 1, $"{newMeshes} mesh blobs of the pieces are not the source's");
        var keeps = states.Select(s => s.Cut.Steps[s.Cut.Steps.Count - 1].Keep).ToArray();
        var ids = states.Select(s => s.Bake.Id).ToArray();
        var o = CutPieces.Run(states[0].Cut, src.Main, src.Get, keeps, ids);
        var again = o.Error == null ? o.Pieces.Select(x => x.MainSha).ToList() : new List<string>();
        Check($"{what}: the pieces' mains equal CutPieces.Run's from the same source and steps", o.Error == null && again.SequenceEqual(stored),
            o.Error != null ? o.Error + " " + o.ErrorText : string.Join("; ", stored.Zip(again, (a, b) => $"{a.Substring(0, 12)} / {b.Substring(0, 12)}")) +
            $"; the loop that only turns on the same steps gave the same mains: {y.OldLoopShas.SequenceEqual(stored)}");
    }

    /// <summary>YC1, YC2 headless: a synthetic source of 26 meshes (the hand-made character and 20 tubes over its trunk, crossing the waist)
    /// as a bake item, cut by a plane at the waist through CutPrepare + CutCommit, so the pieces' blobs are encoded while Finish runs.</summary>
    static void Headless_Cut9_Finish()
    {
        if (!Want("YC1") && !Want("YC2")) return;
        ClearStore();
        var h = NewHost();
        YcCut y = null;
        try
        {
            YcCut Make()
            {
                var f = CutFixture.Character();
                for (int i = 0; i < 20; i++)
                    f.AddTube(new[] { "cf_J_Hips", "cf_J_Spine01", "cf_J_Spine02", "cf_J_Neck" }, 0.95 + 0.03 * i, "clothes" + (1 + i % 7), false, 24, 48);
                var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var source = CutFixtureMain(f, blobs);
                var sb = source.ToBlob(); string ssha = BakeBlob.Sha256(sb); blobs[ssha] = sb;
                var res = h.Rt.Session.Model.File.Res;
                foreach (var kv in blobs) res.AddBlob(kv.Value);
                var add = h.Session.AddItem(BakeState(source.ToItemBake(ssha), null, "yc source"));
                if (!add.Ok) return new YcCut { Error = "the source item: " + add.Code + " " + add.Message };
                h.Rt.ApplyNow();
                var holder = h.Rt.HolderOf(add.NewId);
                if (!holder) return new YcCut { Error = "the source item has no holder" };
                Info($"YC source: {source.Meshes.Count} meshes, {source.Renderers.Count} renderers, {blobs.Values.Sum(b => (long)b.Length) >> 10} KiB of blobs");
                return YcCutThrough(h, add.NewId, holder.TransformPoint(new Vector3(0, 11.2f, 0)), holder.rotation * Quaternion.Euler(0, 0, 8), "yc");
            }
            Group("YC1", "CutPlan.Finish waits on the outcome's pending work (a synthetic source of 26 meshes as a bake item, CutPrepare + CutCommit)", () =>
            {
                y = Make();
                YcFinishChecks(y, "the synthetic source");
            });
            Group("YC2", "the pieces of that cut equal CutPieces.Run's from the same source and steps", () =>
            {
                y = y ?? Make();
                YcSameBits(h, y, "the synthetic source");
            });
        }
        finally { h.Dispose(); ClearStore(); }
    }

    /// <summary>YC1 on BR-Chan (graphics): the live character captured and cut by a plane at 55 % of its height through CutPrepare + CutCommit
    /// (the path of cut.make); YC2's comparison beside it.</summary>
    static void Render_Cut2_Finish()
    {
        Group("YC1", "CutPlan.Finish waits on the outcome's pending work: BR-Chan through CutPrepare + CutCommit", () =>
        {
            ClearStore();
            var h = NewHost();
            try
            {
                string ck = LoadChara(h, false);
                var c = Chara(h, ck);
                if (!Check("BR-Chan built", c != null && c.Root, ck)) return;
                float height = c.Root.GetComponentsInChildren<Renderer>().Select(r => r.bounds.max.y).Max() - c.Root.transform.position.y;
                var y = YcCutThrough(h, ck, c.Root.transform.position + Vector3.up * (0.55f * height), Quaternion.identity, "yc1");
                YcFinishChecks(y, "BR-Chan");
                if (y.Error == null) YcSameBits(h, y, "BR-Chan");
            }
            finally { h.Dispose(); ClearStore(); }
        });
    }

    // ------------------------------------------------------------------ YI5: each piece's copy of its source main is the parse's (the lead's gate)

    static System.Reflection.FieldInfo YiField(string name) =>
        typeof(CutPieces).GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
        ?? throw new MissingFieldException(nameof(CutPieces), name);

    static int yiEncoded0;

    /// <summary>First of the headless parts (by name): the copy check on for every cut of the run, its counters at 0.</summary>
    static void Headless_A0_CopyCheck()
    {
        YiField("CopiesCompared").SetValue(null, 0);
        YiField("CopiesDiffered").SetValue(null, 0);
        YiField("FirstDifferencePiece").SetValue(null, -1);
        YiField("FirstDifferenceCopy").SetValue(null, null);
        YiField("FirstDifferenceParse").SetValue(null, null);
        yiEncoded0 = (int)YiField("PiecesEncoded").GetValue(null);
        YiField("CheckCopies").SetValue(null, true);
        Info("the copy check is on for every cut of this run (CutPieces.CheckCopies)");
    }

    /// <summary>The first differing member of two JSON texts: its path and both values.</summary>
    static string YiFirstDifference(string a, string b)
    {
        string Walk(Newtonsoft.Json.Linq.JToken x, Newtonsoft.Json.Linq.JToken y)
        {
            if (x.Type != y.Type) return $"{x.Path}: {Short(x.ToString(Newtonsoft.Json.Formatting.None))} / {Short(y.ToString(Newtonsoft.Json.Formatting.None))}";
            if (x is Newtonsoft.Json.Linq.JObject ox && y is Newtonsoft.Json.Linq.JObject oy)
            {
                var nx = ox.Properties().Select(p => p.Name).ToList(); var ny = oy.Properties().Select(p => p.Name).ToList();
                if (!nx.SequenceEqual(ny)) return $"{x.Path}: members {string.Join(",", nx)} / {string.Join(",", ny)}";
                foreach (var n in nx) { var d = Walk(ox[n], oy[n]); if (d != null) return d; }
                return null;
            }
            if (x is Newtonsoft.Json.Linq.JArray ax && y is Newtonsoft.Json.Linq.JArray ay)
            {
                if (ax.Count != ay.Count) return $"{x.Path}: {ax.Count} / {ay.Count} elements";
                for (int i = 0; i < ax.Count; i++) { var d = Walk(ax[i], ay[i]); if (d != null) return d; }
                return null;
            }
            var sx = x.ToString(Newtonsoft.Json.Formatting.None); var sy = y.ToString(Newtonsoft.Json.Formatting.None);
            return sx == sy ? null : $"{x.Path}: {sx} / {sy}";
        }
        try { return Walk(Newtonsoft.Json.Linq.JToken.Parse(a), Newtonsoft.Json.Linq.JToken.Parse(b)) ?? "the same JSON values, other bytes (text: first differing line " + YiLine(a, b) + ")"; }
        catch (Exception e) { return "not JSON (" + e.Message + "); first differing line " + YiLine(a, b); }
    }

    static string YiLine(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            string x = i < la.Length ? la[i] : "(none)", y = i < lb.Length ? lb[i] : "(none)";
            if (x != y) return $"{i + 1}: {Short(x.Trim())} / {Short(y.Trim())}";
        }
        return "none";
    }

    /// <summary>Last of the headless parts (by name): YI5 over every cut of the run, then the copy check off.</summary>
    static void Headless_zCopyCheck()
    {
        try
        {
            Group("YI5", "every piece's copy of its source main, written, equals a parse of the source main's canonical JSON written again (every cut of this run)", () =>
            {
                // unknown members (BF2's form): a source main with unknown members at every level; its copy, and its pieces as the 6.8 order (a Clone per
                // piece) makes them
                var f = CutFixture.Character();
                var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                var plain = CutFixtureMain(f, blobs);
                var jo = Newtonsoft.Json.Linq.JObject.Parse(System.Text.Encoding.UTF8.GetString(plain.ToJsonBytes()));
                var names = new[] { "zUnknownMain", "zUnknownSource", "zUnknownNode", "zUnknownMesh", "zUnknownTexture", "zUnknownMaterial", "zUnknownProp", "zUnknownRenderer", "zUnknownInsides" };
                jo["zUnknownMain"] = new Newtonsoft.Json.Linq.JObject { ["a"] = 1.5, ["b"] = new Newtonsoft.Json.Linq.JArray(1, 2) };
                jo["source"]["zUnknownSource"] = "s";
                jo["nodes"][1]["zUnknownNode"] = 7;
                jo["meshes"][0]["zUnknownMesh"] = true;
                jo["textures"][0]["zUnknownTexture"] = "t";
                jo["materials"][0]["zUnknownMaterial"] = 0.25;
                jo["materials"][0]["props"]["_Color"]["zUnknownProp"] = "p";
                jo["renderers"][0]["zUnknownRenderer"] = new Newtonsoft.Json.Linq.JArray("r");
                jo["insides"]["zUnknownInsides"] = 3;
                var src = BakeMain.Parse(System.Text.Encoding.UTF8.GetBytes(jo.ToString()));
                var copyText = System.Text.Encoding.UTF8.GetString(CutPieces.CopyOf(src).ToJsonBytes());
                var parseText = System.Text.Encoding.UTF8.GetString(BakeMain.Parse(src.ToJsonBytes()).ToJsonBytes());
                int named = names.Count(n => copyText.Contains("\"" + n + "\""));
                Check("Present: unknown members at every level of a source main", named == names.Length, $"{named} of {names.Length} unknown members written by the copy");
                Check("a copy with unknown members equals the parse", copyText == parseText, copyText == parseText ? $"{copyText.Length} chars equal" : YiFirstDifference(copyText, parseText));
                var sb = src.ToBlob(); string ssha = BakeBlob.Sha256(sb); blobs[ssha] = sb;
                Func<string, byte[]> get = sha => blobs.TryGetValue(sha, out var b) ? b : null;
                var cut = CutBlock(src, blobs, ssha, null, CutSectionColors.DarkRed, CutPlaneStep(new CutVec(0, 11.2, 0), CutQuat.Euler(0, 0, 8), CutShapeInfo.Front));
                var keeps = new[] { CutShapeInfo.Front, CutShapeInfo.Back };
                var ids = new[] { JsonText.NewId(), JsonText.NewId() };
                var o = CutPieces.Run(cut, src, get, keeps, ids);
                var color = cut.Section.Color;
                var run = new CutRun { Source = CutPieces.InputOf(src, get, new List<string>()), Steps = cut.AppliedSteps.Select(CutPieces.StepOf).ToList(), LastKeeps = keeps, SectionColor = new[] { color.X, color.Y, color.Z }, OwnInside = cut.Section.OwnInside };
                CutProcedure.Run(run);
                var old = run.Pieces.Select((pc, p) => BakeBlob.Sha256(CutPieces.PieceMain(src, pc, ids[p], new Dictionary<string, byte[]>(StringComparer.Ordinal)).ToBlob())).ToList();
                var kept = o.Error == null ? o.Pieces.Select(pc => names.Where(n => System.Text.Encoding.UTF8.GetString(pc.Main.ToJsonBytes()).Contains("\"" + n + "\"")).ToList()).ToList() : new List<List<string>>();
                Check("Present: the pieces keep unknown members", kept.Count == 2 && kept.All(k => k.Count >= 1), string.Join(" / ", kept.Select(k => string.Join(",", k))));
                Check("the pieces of a source with unknown members equal the 6.8 order's (a Clone per piece)", o.Error == null && o.Pieces.Select(pc => pc.MainSha).SequenceEqual(old),
                    o.Error != null ? o.Error + " " + o.ErrorText : string.Join("; ", o.Pieces.Select(pc => pc.MainSha.Substring(0, 12)).Zip(old, (a, b) => a + " / " + b.Substring(0, 12))));
                int compared = (int)YiField("CopiesCompared").GetValue(null), differed = (int)YiField("CopiesDiffered").GetValue(null);
                int encoded = (int)YiField("PiecesEncoded").GetValue(null) - yiEncoded0;
                Check("Present: every piece of the run's cuts compared", compared >= 1 && compared == encoded, $"{compared} copies compared of {encoded} pieces whose blobs this run made");
                var piece = (int)YiField("FirstDifferencePiece").GetValue(null);
                var a = (string)YiField("FirstDifferenceCopy").GetValue(null); var b = (string)YiField("FirstDifferenceParse").GetValue(null);
                Check("the copies equal the parse byte for byte", differed == 0, differed == 0 ? $"{compared} of {compared} equal" :
                    $"{differed} differ; the first at piece {piece}: {(a != null && b != null ? YiFirstDifference(a, b) : "no text")} (copy / parse)");
            });
        }
        finally { YiField("CheckCopies").SetValue(null, false); }
    }
}

public static partial class MosaicTest
{
    /// <summary>A piece of a BvBake drawn as an item draws it: its prototype from the piece's main (meshes from its blobs, textures the
    /// source's by blob), materials copied per slot and mapped, the members added unless told not to; at the piece's world pose.</summary>
    sealed class CutPieceCopy : IDisposable
    {
        public BakePrototype Proto;
        public GameObject Container, Holder, Instance;
        public List<Renderer> Members = new List<Renderer>();
        public int Added;
        readonly List<Material> copies = new List<Material>();

        public static CutPieceCopy Of(BvBake source, CutPieceResult piece, Func<string, byte[]> blobOf, bool addMembers)
        {
            var c = new CutPieceCopy();
            var b = BakeBlobs.FromMain(piece.Main);
            var srcTex = new Dictionary<string, Texture>(StringComparer.Ordinal);
            var sm = source.Job.Main;
            for (int i = 0; i < sm.Textures.Count && i < source.Job.Textures.Count; i++) srcTex[sm.Textures[i].Blob] = source.Job.Textures[i];
            c.Container = new GameObject("cut_piece_container");
            c.Container.SetActive(false);
            c.Proto = BakeBuild.Prototype(b, i => BakeBlobs.FromMesh(BakeMeshData.FromBlob(blobOf(piece.Main.Meshes[i].Blob)), piece.Main.Meshes[i].Name),
                i => srcTex.TryGetValue(piece.Main.Textures[i].Blob, out var t) ? t : null, c.Container.transform, false);
            c.Holder = new GameObject("cut_piece_item");
            CutMarkers.PiecePose(source.Holder.transform.localToWorldMatrix, piece, out var pos, out var rot, out var scale);
            c.Holder.transform.SetPositionAndRotation(pos, rot);
            c.Holder.transform.localScale = scale;
            c.Instance = BakeBuild.Instantiate(c.Proto, c.Holder.transform);
            foreach (var r in c.Instance.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) if (ms[i]) { ms[i] = MaterialCopies.Copy(ms[i]); c.copies.Add(ms[i]); }
                r.sharedMaterials = ms;
            }
            ShaderMap.ApplyUrp(c.Instance, copy: false);
            bool skip = BakeBuild.SkipMembers;
            BakeBuild.SkipMembers = !addMembers;
            try { c.Added = BakeBuild.AddMembers(c.Proto, c.Instance); }
            finally { BakeBuild.SkipMembers = skip; }
            c.Members = BakeBuild.MembersOf(c.Proto, c.Instance);
            return c;
        }

        public void Dispose()
        {
            if (Holder) Object.DestroyImmediate(Holder);
            Proto?.Destroy();
            if (Container) Object.DestroyImmediate(Container);
            foreach (var m in copies) if (m) Object.DestroyImmediate(m);
            copies.Clear();
        }
    }

    /// <summary>BV4 and BV5 (the Cutter's part of MosaicTest.render, R3) on BV3's fixtures:
    ///   BV4  a plane through a member (across it, through its region's centroid): both pieces hold part of it; every member renderer of
    ///        both pieces is a member before its first frame; (a) and (b) of BV3 on each piece's members (front and 30 deg up per piece)
    ///   BV5  no inside and no section on a member (in the pieces' mains and in their built copies); "clothes only" through the member
    ///        leaves it whole in one piece while a garment over it is cut (HERO107, his clothes on).</summary>
    static partial void CutGroups(MakerCoreTest.Harness h)
    {
        if (!Want("BV4") && !Want("BV5")) return;
        Guard("BV4-BV5 a cut through the members", () => CutBv(h));
    }

    static void CutBv(MakerCoreTest.Harness h)
    {
        foreach (var (label, file, male) in new[] { ("BR-Chan", FemaleCard, false), ("HERO107", MaleCard, true) })
            using (var f = new Disposer(h.Build(file)))
            {
                if (!Check($"BV4 {label} builds", f.B.Ok, f.B.Error ?? "")) continue;
                var ctx = f.B.Context;
                Texture marker;
                var saved = new List<(Material m, Texture t)>();
                if (male)
                {
                    var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
                    if (nDan) nDan.gameObject.SetActive(true);
                    RunStage(ctx);
                    marker = Wv3mChecker(2048, 5);
                    foreach (var mm in MosaicSet.StateOf(ctx)?.Members ?? new List<MosaicMember>())
                        foreach (var mat in mm.Renderer.sharedMaterials)
                            if (mat && mat.HasProperty("_MainTex") && !saved.Exists(x => x.m == mat)) { saved.Add((mat, mat.GetTexture("_MainTex"))); mat.SetTexture("_MainTex", marker); }
                }
                else
                {
                    HideClothes(ctx);
                    marker = Composite(ctx, 2048, 6, MarkerTexel, Skin);
                    UseComposite(ctx, marker);
                }
                NewLight();
                var focus = Member(ctx, male ? "cm_o_dan00" : "o_mnpb");
                if (!Check($"BV4 {label} present: the member to cut", focus != null, male ? "cm_o_dan00" : "o_mnpb")) continue;
                BvBake bk = null;
                var pieces = new List<CutPieceCopy>();
                try
                {
                    bk = BvBake.Of(ctx, label, male, true);
                    bk.Job.Finish();
                    var job = bk.Job;
                    Func<string, byte[]> blobOf = sha => job.Blobs.TryGetValue(sha, out var by) ? by : null;
                    var src = new CutSource { Id = job.Main.Id, Main = BakeBlob.ResName(job.MainSha) };
                    foreach (var sha in job.Main.DataBlobs()) src.Data.Add(BakeBlob.ResName(sha));
                    var rootW = bk.Holder.transform.localToWorldMatrix;
                    var c = MosaicCamera.RegionMatrix(focus).MultiplyPoint3x4(focus.Region.Centroid);
                    var n = male ? ctx.Root.transform.forward : PatchPose(ctx).n;
                    // a plane across the member: through its centroid, its normal the root's right
                    var across = Matrix4x4.TRS(c, Quaternion.FromToRotation(Vector3.up, ctx.Root.transform.right), Vector3.one);
                    int focusIndex = job.Capture.Live.IndexOf(focus.Renderer);
                    // BV4: both pieces hold part of the member
                    {
                        var step = CutMarkers.StepOf(across, rootW, CutKind.Plane, CutShapeInfo.Front, null, false);
                        var o = CutPieces.Run(CutPieces.NewBlock(src, null, new[] { step }, CutSectionColors.DarkRed, true), job.Main, blobOf,
                            new[] { CutShapeInfo.Front, CutShapeInfo.Back }, new[] { JsonText.NewId(), JsonText.NewId() });
                        if (!Check($"BV4 {label}: the cut runs", o.Error == null && o.Pieces.Count == 2, o.Error + " " + o.ErrorText)) continue;
                        bool cutBoth = o.Pieces.All(p => p.CutRenderers.Contains(focusIndex) && p.Main.Renderers.Any(r => r.Member));
                        Check($"BV4 {label} present: the member was cut (both pieces hold some)", cutBoth,
                            string.Join("; ", o.Pieces.Select(p => $"{p.Side}: {p.Main.Renderers.Count(r => r.Member)} member renderers, the member cut {p.CutRenderers.Contains(focusIndex)}")));
                        foreach (var p in o.Pieces) pieces.Add(CutPieceCopy.Of(bk, p, sha => p.NewBlobs.TryGetValue(sha, out var nb) ? nb : blobOf(sha), true));
                        bool allMembers = pieces.All(pc => pc.Members.Count > 0 && pc.Members.All(MosaicSet.IsAdded) && pc.Added == pc.Members.Count);
                        Check($"BV4 {label}: every member renderer of both pieces is a member before its first frame", allMembers,
                            string.Join("; ", pieces.Select((pc, i) => $"{o.Pieces[i].Side}: {pc.Members.Count} member renderers, added {pc.Added}, each a member {pc.Members.All(MosaicSet.IsAdded)}")));
                        // BV3 on each piece
                        var lines = new List<string>();
                        int markers = 0, under = 0, uncovered = 0, judged = 0;
                        var cam = NewCam(true, AntialiasingMode.None);
                        var right = Vector3.Cross(Vector3.up, n).normalized;
                        var hide = new[] { ctx.Root, bk.Holder }.Concat(pieces.Select(pc => pc.Holder)).ToArray();
                        for (int i = 0; i < pieces.Count; i++)
                            foreach (var (vn, dir) in new[] { ("front", n), ("30 deg up", Quaternion.AngleAxis(-30f, right) * n) })
                            {
                                Aim(cam, c, dir, 10f);
                                BvJudge($"BV4 {label} {o.Pieces[i].Side} {vn}", cam, BvW, BvH, 1, pieces[i].Members, hide, pieces[i].Holder, lines, ref markers, ref under, ref uncovered, ref judged);
                            }
                        foreach (var l in lines) Info(l);
                        Check($"BV4 {label} present: >= {BvLeastJudged} piece views judged (footprint >= {BvLeastFootprint} px)", judged >= BvLeastJudged, judged + " judged");
                        Check($"BV4 {label} (a) on the pieces: 0 marker pixels in the footprint, every block >= F", judged > 0 && markers == 0 && under == 0, $"markers {markers}, views with a block under F {under}");
                        Check($"BV4 {label} (b) on the pieces: the wide pass covers the footprint", judged > 0 && uncovered == 0, $"uncovered {uncovered} px");
                        // BV5 (R3) on the same cut: no inside and no section on a member
                        int memberInside = 0, memberCopies = 0, sections = 0;
                        foreach (var p in o.Pieces)
                            foreach (var r in p.Main.Renderers)
                            {
                                if (r.Member && r.Inside != BakeKinds.InsideNone) memberInside++;
                                if (r.Inside == BakeKinds.InsideSection) sections++;
                            }
                        foreach (var pc in pieces)
                            foreach (var m in pc.Members)
                                if (m && m.GetComponentsInChildren<Renderer>(true).Any(x => x != m && x.name.EndsWith(BakeInsides.Suffix, StringComparison.Ordinal))) memberCopies++;
                        Check($"BV5 {label}: no inside and no section of a member", memberInside == 0 && memberCopies == 0 && sections > 0,
                            $"{sections} section renderers over the pieces; members marked inside {memberInside}, inside copies built under a member {memberCopies}");
                    }
                    // BV5: clothes only through the member leaves it whole while a garment over it is cut (a target whose clothes are on)
                    if (male)
                    {
                        var step = CutMarkers.StepOf(across, rootW, CutKind.Plane, CutShapeInfo.Front, null, true);
                        var o = CutPieces.Run(CutPieces.NewBlock(src, null, new[] { step }, CutSectionColors.DarkRed, true), job.Main, blobOf,
                            new[] { CutShapeInfo.Front, CutShapeInfo.Back }, new[] { JsonText.NewId(), JsonText.NewId() });
                        var garments = o.Error == null ? o.Pieces.SelectMany(p => p.CutRenderers).Distinct().Where(i => (job.Main.Renderers[i].Part ?? "").StartsWith("clothes", StringComparison.Ordinal)).ToList() : new List<int>();
                        var over = garments.Where(i => job.Capture.Live[i] && job.Capture.Live[i].bounds.Intersects(focus.Renderer.bounds)).Select(i => job.Capture.Live[i].name).ToList();
                        Check($"BV5 {label} present: a garment over the member was cut", o.Error == null && over.Count > 0, $"garments cut {garments.Count}, over the member: {string.Join(", ", over)}");
                        string srcBlob = job.Main.Meshes[job.Main.Renderers[focusIndex].Mesh].Blob;
                        int holding = 0; bool unchanged = true;
                        foreach (var p in o.Pieces ?? new List<CutPieceResult>())
                        {
                            if (p.CutRenderers.Contains(focusIndex)) unchanged = false;
                            foreach (var r in p.Main.Renderers)
                                if (r.Member && p.Main.Meshes[r.Mesh].Blob == srcBlob) holding++;
                        }
                        Check($"BV5 {label}: clothes only leaves the member whole, in one piece", o.Error == null && unchanged && holding == 1, $"the member cut {!unchanged}, pieces holding its whole mesh {holding}");
                    }
                }
                finally
                {
                    foreach (var pc in pieces) pc.Dispose();
                    bk?.Dispose();
                    bk?.Job?.Dispose();
                    foreach (var (m, t) in saved) if (m) m.SetTexture("_MainTex", t);
                    MosaicWide.Enabled = false;
                    MosaicWide.DebugView = false;
                }
            }
    }
}
