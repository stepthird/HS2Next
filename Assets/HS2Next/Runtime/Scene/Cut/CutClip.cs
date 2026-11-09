// The clip of one mesh, Unity-free. Per triangle and per face in order, the part on
// each side is kept (Sutherland–Hodgman against one value per vertex: a vertex with f ≥ 0 belongs to the front part, f ≤ 0 to
// the back part, so a vertex on the surface belongs to both). Where the value changes sign along an edge a new vertex is made
// at the parameter where the face's value at the new vertex's own posed position is 0: first t = f(a) / (f(a) − f(b)), then a
// bracketing search (Illinois) on the posed value of the interpolated vertex, so the vertex lies on the curved and ragged
// surfaces too and not only between its ends. The new vertex is shared by the edge (keyed by its two ends and the face, the
// lower index first), so neighbouring triangles and both pieces of one face use the same vertex.
//   Plane: one split; region 0 the front (f > 0), region 1 the back (f ≤ 0).
//   Box, tear, hole: the part of a triangle behind faces 0 … k−1 is split by face k; its front part is labelled k and goes
//   to region 1 (the outside, the union: f_k > 0 and f_j ≤ 0 for every j < k), what lies behind every face to
//   region 0 (the inside). An edge is cut by face k exactly when both its ends lie behind every earlier face and f_k changes
//   sign along it, wherever the edge is: a part already labelled takes those vertices on its outline too (a chord it shares
//   with the part behind), so no piece has a T-junction and both sides of a chord hold the same vertices.
// Each part is fanned into triangles before the next face, so no polygon has more than two sign changes. A part with no
// vertex strictly on its side is dropped (a triangle lying on the surface goes to the back or inside). Points and lines go
// whole to the region of their centroid. A region that receives every triangle unsplit is marked Whole.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Cut
{
    public sealed class CutClipResult
    {
        /// <summary>Per region (plane: front, back; box and openings: inside, outside) the sub-meshes' index lists.</summary>
        public readonly List<CutSubMesh>[] Regions = { new List<CutSubMesh>(), new List<CutSubMesh>() };
        /// <summary>The region received every triangle of the input unsplit (and the other region none).</summary>
        public readonly bool[] Whole = new bool[2];
        public int Triangles, Crossings, SearchSteps;
        /// <summary>The largest |face value| at a new vertex after the search.</summary>
        public double WorstCrossing;
    }

    public static class CutClip
    {
        /// <summary>Halvings of an edge's parameter for a new vertex: t within 2^-25 of the edge.</summary>
        public const int Halvings = 24;

        sealed class Work
        {
            public CutVerts V;
            public CutSurface S;
            public int K;
            public readonly List<double> F = new List<double>();
            public readonly Dictionary<ulong, int> Edges = new Dictionary<ulong, int>(CutKeyComparer.Instance);
            public readonly double[] Buf = new double[8];
            public readonly int[] Pp = new int[4], Np = new int[4];
            public readonly List<int> Ins = new List<int>(16);
            public CutClipResult R;

            public double Fv(int v, int k)
            {
                while (F.Count < (v + 1) * K)
                {
                    int i = F.Count / K;
                    S.Faces(V.PosedAt(i), Buf, 0);
                    for (int j = 0; j < K; j++) F.Add(Buf[j]);
                }
                return F[v * K + k];
            }
        }

        static ulong Key(int lo, int hi, int k) => ((ulong)(uint)lo << 34) | ((ulong)(uint)hi << 4) | (uint)k;

        /// <summary>Clips the triangles (and assigns the points and lines) of the sub-meshes by the surface.</summary>
        public static CutClipResult Run(CutVerts v, IReadOnlyList<CutSubMesh> subs, CutSurface s)
        {
            var w = new Work { V = v, S = s, K = s.FaceCount, R = new CutClipResult() };
            var r = w.R;
            int total = 0;
            var unsplit = new int[2];
            var a = new List<int>(64);
            var b = new List<int>(64);
            var cur = new List<int>(64);
            var next = new List<int>(64);
            var polys = new List<List<int>>(16);
            var labels = new List<int>(16);
            var polyPool = new Stack<List<int>>();
            foreach (var sub in subs)
            {
                var out0 = new List<int>();
                var out1 = new List<int>();
                if (sub.Topology == CutTopology.Triangles)
                {
                    var ix = sub.Indices;
                    for (int t = 0; t + 2 < ix.Length; t += 3)
                    {
                        total++;
                        if (s.IsPlane)
                        {
                            a.Clear(); b.Clear();
                            bool split = Split(w, ix[t], ix[t + 1], ix[t + 2], 0, a, b);
                            out0.AddRange(a); out1.AddRange(b);
                            if (!split) { if (a.Count > 0) unsplit[0]++; else if (b.Count > 0) unsplit[1]++; }
                        }
                        else
                        {
                            // the parts behind every face so far are split by the next face; a part in front of face j
                            // keeps label j and only takes the next faces' vertices on its edges that the rule cuts
                            cur.Clear(); cur.Add(ix[t]); cur.Add(ix[t + 1]); cur.Add(ix[t + 2]);
                            polys.Clear(); labels.Clear();
                            bool modified = false;
                            for (int k = 0; k < w.K; k++)
                            {
                                for (int pi = 0; pi < polys.Count; pi++) modified |= Insert(w, polys[pi], k);
                                if (cur.Count == 0) continue;
                                next.Clear();
                                for (int q = 0; q + 2 < cur.Count; q += 3)
                                {
                                    a.Clear();
                                    modified |= Split(w, cur[q], cur[q + 1], cur[q + 2], k, a, next);
                                    for (int z = 0; z + 2 < a.Count; z += 3)
                                    {
                                        var poly = polyPool.Count > 0 ? polyPool.Pop() : new List<int>(8);
                                        poly.Clear(); poly.Add(a[z]); poly.Add(a[z + 1]); poly.Add(a[z + 2]);
                                        polys.Add(poly); labels.Add(k);
                                    }
                                }
                                var tmp = cur; cur = next; next = tmp;
                            }
                            out0.AddRange(cur);
                            foreach (var poly in polys)
                            {
                                for (int z = 1; z + 1 < poly.Count; z++) { out1.Add(poly[0]); out1.Add(poly[z]); out1.Add(poly[z + 1]); }
                                polyPool.Push(poly);
                            }
                            if (!modified) { if (cur.Count > 0) unsplit[0]++; else if (polys.Count > 0) unsplit[1]++; }
                        }
                    }
                }
                else
                {
                    int per = sub.Topology == CutTopology.Points ? 1 : sub.Topology == CutTopology.Lines ? 2 : 0;
                    var ix = sub.Indices;
                    if (per == 0) out0.AddRange(ix);   // other topologies are made Triangles, Lines or Points before the clip
                    else
                        for (int t = 0; t + per - 1 < ix.Length; t += per)
                        {
                            var c = CutVec.Zero;
                            for (int q = 0; q < per; q++) c = c + v.PosedAt(ix[t + q]);
                            c = c * (1.0 / per);
                            int cls = s.Classify(c);
                            bool reg0 = s.IsPlane ? cls == 0 : cls < 0;
                            var dst = reg0 ? out0 : out1;
                            for (int q = 0; q < per; q++) dst.Add(ix[t + q]);
                        }
                }
                r.Regions[0].Add(new CutSubMesh { Topology = sub.Topology, Indices = out0.ToArray() });
                r.Regions[1].Add(new CutSubMesh { Topology = sub.Topology, Indices = out1.ToArray() });
            }
            r.Triangles = total;
            r.Whole[0] = total > 0 && unsplit[0] == total;
            r.Whole[1] = total > 0 && unsplit[1] == total;
            return r;
        }

        /// <summary>Splits one triangle by face k: the front part (f ≥ 0, with a vertex f > 0) into pos, the back part (f ≤ 0,
        /// with a vertex f &lt; 0, or every vertex on the surface) into neg, as triangles. True when it made a new vertex.</summary>
        static bool Split(Work w, int i0, int i1, int i2, int k, List<int> pos, List<int> neg)
        {
            double f0 = w.Fv(i0, k), f1 = w.Fv(i1, k), f2 = w.Fv(i2, k);
            if (f0 >= 0 && f1 >= 0 && f2 >= 0 && (f0 > 0 || f1 > 0 || f2 > 0)) { pos.Add(i0); pos.Add(i1); pos.Add(i2); return false; }
            if (f0 <= 0 && f1 <= 0 && f2 <= 0) { neg.Add(i0); neg.Add(i1); neg.Add(i2); return false; }
            int[] pp = w.Pp, np = w.Np;
            int pn = 0, nn = 0;
            bool made = false;
            for (int e = 0; e < 3; e++)
            {
                int va = e == 0 ? i0 : e == 1 ? i1 : i2;
                int vb = e == 0 ? i1 : e == 1 ? i2 : i0;
                double fa = e == 0 ? f0 : e == 1 ? f1 : f2;
                double fb = e == 0 ? f1 : e == 1 ? f2 : f0;
                if (fa >= 0) pp[pn++] = va;
                if (fa <= 0) np[nn++] = va;
                if ((fa < 0 && fb > 0) || (fa > 0 && fb < 0))
                {
                    int x = Cross(w, va, vb, k);
                    pp[pn++] = x; np[nn++] = x;
                    made = true;
                }
            }
            Fan(pp, pn, pos);
            Fan(np, nn, neg);
            return made;
        }

        static void Fan(int[] p, int n, List<int> dst)
        {
            for (int i = 1; i + 1 < n; i++) { dst.Add(p[0]); dst.Add(p[i]); dst.Add(p[i + 1]); }
        }

        /// <summary>An edge is cut by face k when both its ends lie behind every earlier face (f_j ≤ 0, j &lt; k) and f_k
        /// changes sign along it: the rule the split of the parts behind applies, here put on the edges of a part already in
        /// front of an earlier face (a chord it shares with a part behind), as new vertices on its outline. True when one was put.</summary>
        static bool Insert(Work w, List<int> poly, int k)
        {
            bool any = false;
            int n = poly.Count;
            List<int> res = null;
            for (int e = 0; e < n; e++)
            {
                int va = poly[e], vb = poly[(e + 1) % n];
                double fa = w.Fv(va, k), fb = w.Fv(vb, k);
                bool cut = (fa < 0 && fb > 0) || (fa > 0 && fb < 0);
                for (int j = 0; j < k && cut; j++) if (!(w.Fv(va, j) <= 0 && w.Fv(vb, j) <= 0)) cut = false;
                if (cut && res == null) { res = w.Ins; res.Clear(); for (int z = 0; z < e; z++) res.Add(poly[z]); }
                if (res != null) { res.Add(va); if (cut) { res.Add(Cross(w, va, vb, k)); any = true; } }
            }
            if (any) { poly.Clear(); poly.AddRange(res); }
            return any;
        }

        /// <summary>The vertex where face k crosses 0 on the edge (a, b); made once per edge and face.</summary>
        static int Cross(Work w, int a, int b, int k)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            ulong key = Key(lo, hi, k);
            if (w.Edges.TryGetValue(key, out int x)) return x;
            // a fixed number of halvings decided by signs only: the same t for inputs that differ in the last bits (a re-cut
            // of a stored result, a recompute), so equal procedures give equal vertices
            bool loNeg = w.Fv(lo, k) < 0;
            double ta = 0, tb = 1;
            var r = w.R;
            for (int it = 0; it < Halvings; it++)
            {
                double tm = 0.5 * (ta + tb);
                double gm = w.S.Face(k, w.V.PosedLerp(lo, hi, tm));
                if ((gm < 0) == loNeg) ta = tm; else tb = tm;
            }
            r.SearchSteps += Halvings;
            double t = 0.5 * (ta + tb);
            double g = w.S.Face(k, w.V.PosedLerp(lo, hi, t));
            x = w.V.AddLerp(lo, hi, t);
            w.Fv(x, k);
            w.F[x * w.K + k] = 0;
            w.Edges[key] = x;
            r.Crossings++;
            double ag = Math.Abs(g);
            if (ag > r.WorstCrossing) r.WorstCrossing = ag;
            return x;
        }
    }
}
