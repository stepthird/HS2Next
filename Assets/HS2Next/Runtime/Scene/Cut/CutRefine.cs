// The refinement before a ragged cut or an opening, Unity-free. An edge longer (at
// its posed length) than the target length (tooth / 3, at least 0.02; for a tear or a hole at most a quarter of the smaller
// half size of its outline) is split at its midpoint when one of its ends lies within the band (1.5 × amplitude + the edge's
// length) of the unragged surface or the edge crosses it; a midpoint is made once per edge (all attributes at t = 0.5, weights
// merged) and the triangles are split by how many of their edges were marked (1 → 2, 2 → 3, 3 → 4, winding kept). Up to 6
// passes; a pass that would take the mesh over 400,000 vertices is not made.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Cut
{
    public sealed class CutRefineStats
    {
        public int Passes, Added;
        public double Target;
        /// <summary>A pass was left out at the vertex limit.</summary>
        public bool Limited;
    }

    public static class CutRefine
    {
        public const int MaxPasses = 6, MaxVertices = 400000;

        /// <summary>Ragged cuts and openings are refined; a clean plane or box is not.</summary>
        public static bool Applies(CutSurface s) => s.Ragged || s.IsOpening;

        public static double TargetLength(CutSurface s)
        {
            double l = Math.Max(s.Tooth / 3, 0.02);
            if (s.IsOpening)
            {
                double q = 0.25 * Math.Min(s.Hx, s.Hz);
                l = s.Ragged ? Math.Min(l, q) : q;
            }
            return l;
        }

        static ulong Key(int lo, int hi) => ((ulong)(uint)lo << 32) | (uint)hi;

        /// <summary>Refines the triangle sub-meshes in place (new index arrays); other topologies are left as they are.</summary>
        public static CutRefineStats Run(CutVerts v, List<CutSubMesh> subs, CutSurface s)
        {
            var st = new CutRefineStats { Target = TargetLength(s) };
            double target = st.Target, amp15 = 1.5 * s.Amp;
            var region = new List<double>();
            double Reg(int i)
            {
                while (region.Count <= i) region.Add(s.Region(v.PosedAt(region.Count)));
                return region[i];
            }
            var seen = new HashSet<ulong>(CutKeyComparer.Instance);
            var mid = new Dictionary<ulong, int>(CutKeyComparer.Instance);
            var marked = new List<ulong>();
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                seen.Clear(); mid.Clear(); marked.Clear();
                foreach (var sub in subs)
                {
                    if (sub.Topology != CutTopology.Triangles) continue;
                    var ix = sub.Indices;
                    for (int t = 0; t + 2 < ix.Length; t += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            int a = ix[t + e], b = ix[t + (e + 1) % 3];
                            int lo = Math.Min(a, b), hi = Math.Max(a, b);
                            ulong key = Key(lo, hi);
                            if (!seen.Add(key)) continue;
                            double len = (v.PosedAt(lo) - v.PosedAt(hi)).Length;
                            if (!(len > target)) continue;
                            double ra = Reg(lo), rb = Reg(hi), band = amp15 + len;
                            if (Math.Abs(ra) <= band || Math.Abs(rb) <= band || (ra > 0) != (rb > 0)) marked.Add(key);
                        }
                }
                if (marked.Count == 0) break;
                if (v.Count + marked.Count > MaxVertices) { st.Limited = true; break; }
                foreach (var key in marked)
                {
                    int lo = (int)(key >> 32), hi = (int)(key & 0xffffffffUL);
                    mid[key] = v.AddLerp(lo, hi, 0.5);
                }
                st.Added += marked.Count;
                st.Passes++;
                var tri = new List<int>();
                foreach (var sub in subs)
                {
                    if (sub.Topology != CutTopology.Triangles) continue;
                    tri.Clear();
                    var ix = sub.Indices;
                    for (int t = 0; t + 2 < ix.Length; t += 3)
                    {
                        int a = ix[t], b = ix[t + 1], c = ix[t + 2];
                        int mab = Mid(mid, a, b), mbc = Mid(mid, b, c), mca = Mid(mid, c, a);
                        int n = (mab >= 0 ? 1 : 0) + (mbc >= 0 ? 1 : 0) + (mca >= 0 ? 1 : 0);
                        if (n == 0) Add(tri, a, b, c);
                        else if (n == 3)
                        {
                            Add(tri, a, mab, mca); Add(tri, mab, b, mbc); Add(tri, mca, mbc, c); Add(tri, mab, mbc, mca);
                        }
                        else if (n == 1)
                        {
                            if (mab >= 0) { Add(tri, a, mab, c); Add(tri, mab, b, c); }
                            else if (mbc >= 0) { Add(tri, a, b, mbc); Add(tri, a, mbc, c); }
                            else { Add(tri, a, b, mca); Add(tri, b, c, mca); }
                        }
                        else
                        {
                            if (mca < 0) { Add(tri, mab, b, mbc); Add(tri, a, mab, mbc); Add(tri, a, mbc, c); }
                            else if (mab < 0) { Add(tri, mca, mbc, c); Add(tri, a, b, mbc); Add(tri, a, mbc, mca); }
                            else { Add(tri, a, mab, mca); Add(tri, mab, b, c); Add(tri, mab, c, mca); }
                        }
                    }
                    sub.Indices = tri.ToArray();
                }
            }
            return st;
        }

        static int Mid(Dictionary<ulong, int> mid, int a, int b) => mid.TryGetValue(Key(Math.Min(a, b), Math.Max(a, b)), out int m) ? m : -1;

        static void Add(List<int> l, int a, int b, int c) { l.Add(a); l.Add(b); l.Add(c); }
    }
}
