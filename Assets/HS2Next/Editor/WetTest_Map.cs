// WetTest, the map query (the map's surface), judged against the map's triangles read apart from MapSurface, in double. A map loads only in play mode.
//   WG7   headless, no device: the same synthetic map on the mesh path, then the colliders, the source says which
//   WG5   play: no map, no runtime, the arguments
//   WG1   play: the floor around BR-Chan on a vanilla map equals the readable triangles; several points with one wait
//   WG2   play: a mod map with no readable mesh answers, inside the bounds of what draws, and a static batch stays with its answer when the map moves; a map whose only
//         collider is a ceiling, reported against physics
//   WG4   play: the synthetic map through the device (several roots, a root with no renderer, a terrain, what is off, a move, a static batch); a real map's terrain
//   WG3   play: all 30 vanilla maps, the 49 points of the census at a top of 12 and of 30, the device against the triangles; every difference is named
//   WG6   play: the bounds read and the scan on the largest vanilla map, reported, over a bar a WARN. -wg6 <ref> names another map: the one run on the largest map of the
//         list is -only WG6 -wg6 "[Fouetty]Fantasy Cemetery Enviroment#0" -wg6LoadSec 600; it does not belong to the row
//   WG10  play: MapSurface.Ray against the triangles on a vanilla map, on a map with no readable mesh, and on the synthetic map
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class WetTest
{
    const string MqFloorMap = "illusion.hs2#10", MqNoMeshMap = "2155x.map.hangar#0", MqColliderMap = "2155x.map.interrogationroom#0", MqTerrainMap = "hooh.map.school#1", MqLargestVanilla = "illusion.hs2#0";
    const float MqTolerance = 0.01f;
    // a range reaches this far past its two heights, so a surface exactly at the point counts
    const double MqMargin = 1e-3;

    // ------------------------------------------------------------ the map's triangles, read apart

    sealed class MqPart
    {
        public Renderer R;
        public Vector3[] V;
        public int[] T;
        public Bounds B;
    }

    sealed class MqCensus
    {
        public int Drawn, Readable, Unreadable, Skinned, NoMesh, Batched, Shadows;
        public override string ToString() => $"{Drawn} drawn: {Readable} readable, {Unreadable} unreadable, {Skinned} skinned, {NoMesh} no mesh; {Batched} static-batched; {Shadows} shadows only";
    }

    static bool MqDraws(Renderer r) =>
        r && r.enabled && !r.forceRenderingOff && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly && r.gameObject.activeInHierarchy &&
        !(r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer);

    static bool MqShadowsOnly(Renderer r) =>
        r && r.enabled && !r.forceRenderingOff && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly && r.gameObject.activeInHierarchy;

    /// <summary>The readable triangles of the renderers that draw; with <paramref name="shadows"/>, of those that only cast a shadow.</summary>
    static List<MqPart> MqParts(MapStructure s, MqCensus c, bool shadows = false)
    {
        var l = new List<MqPart>();
        foreach (var r in s.Renderers)
        {
            if (!shadows && MqShadowsOnly(r)) c.Shadows++;
            if (!(shadows ? MqShadowsOnly(r) : MqDraws(r))) continue;
            c.Drawn++;
            if (r.isPartOfStaticBatch) c.Batched++;
            if (r is SkinnedMeshRenderer) { c.Skinned++; continue; }
            var m = r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
            if (!m) { c.NoMesh++; continue; }
            if (!m.isReadable) { c.Unreadable++; continue; }
            c.Readable++;
            var to = r.localToWorldMatrix;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = to.MultiplyPoint3x4(v[i]);
            int first = 0, count = m.subMeshCount;
            if (r.isPartOfStaticBatch && r is MeshRenderer mr) { first = mr.subMeshStartIndex; count = Math.Min(r.sharedMaterials.Length, m.subMeshCount - first); }
            var t = new List<int>();
            for (int k = first; k < first + count; k++)
                if (m.GetTopology(k) == MeshTopology.Triangles) t.AddRange(m.GetTriangles(k));
            l.Add(new MqPart { R = r, V = v, T = t.ToArray(), B = r.bounds });
        }
        return l;
    }

    /// <summary>The highest surface under (x, z) between two heights: a triangle over the point in plan, interpolated; walls left out.</summary>
    static bool MqBelow(List<MqPart> parts, double x, double z, double top, double bottom, out double height, out Vector3 normal, out Renderer by)
    {
        height = double.NegativeInfinity;
        normal = default;
        by = null;
        foreach (var p in parts)
        {
            if (x < p.B.min.x - 0.01 || x > p.B.max.x + 0.01 || z < p.B.min.z - 0.01 || z > p.B.max.z + 0.01) continue;
            var v = p.V;
            var t = p.T;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                double d = (double)(b.z - c.z) * (a.x - c.x) + (double)(c.x - b.x) * (a.z - c.z);
                if (d == 0) continue;
                double l1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, l2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d, l3 = 1 - l1 - l2;
                if (l1 < 0 || l2 < 0 || l3 < 0) continue;
                double y = l1 * a.y + l2 * b.y + l3 * c.y;
                if (y > top || y < bottom || y <= height) continue;
                var n = Vector3.Cross(b - a, c - a).normalized;
                if (Mathf.Abs(n.y) < MapSurface.MinNormalY) continue;
                height = y;
                normal = n.y < 0 ? -n : n;
                by = p.R;
            }
        }
        return by != null;
    }

    /// <summary>The nearest triangle along a ray, walls included.</summary>
    static bool MqAlong(List<MqPart> parts, Ray ray, double max, out double distance, out Vector3 normal)
    {
        distance = max;
        normal = default;
        bool any = false;
        foreach (var p in parts)
        {
            if (!p.B.IntersectRay(ray) && !p.B.Contains(ray.origin)) { var e = p.B; e.Expand(0.02f); if (!e.IntersectRay(ray)) continue; }
            var v = p.V;
            var t = p.T;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                if (!MqTriangle(ray, a, b, c, out double d) || d >= distance) continue;
                distance = d;
                var n = Vector3.Cross(b - a, c - a).normalized;
                normal = Vector3.Dot(n, ray.direction) > 0 ? -n : n;
                any = true;
            }
        }
        return any;
    }

    static bool MqTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out double t)
    {
        t = 0;
        double ox = ray.origin.x - a.x, oy = ray.origin.y - a.y, oz = ray.origin.z - a.z, dx = ray.direction.x, dy = ray.direction.y, dz = ray.direction.z;
        double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        double den = -(dx * nx + dy * ny + dz * nz);
        if (den == 0) return false;
        double px = oy * dz - oz * dy, py = oz * dx - ox * dz, pz = ox * dy - oy * dx;
        double s = (vx * px + vy * py + vz * pz) / den, w = -(ux * px + uy * py + uz * pz) / den;
        t = (ox * nx + oy * ny + oz * nz) / den;
        return s >= 0 && w >= 0 && s + w <= 1 && t >= 0;
    }

    static Vector3[] MqCorners(Bounds b)
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++) c[i] = b.center + new Vector3((i & 1) == 0 ? -b.extents.x : b.extents.x, (i & 2) == 0 ? -b.extents.y : b.extents.y, (i & 4) == 0 ? -b.extents.z : b.extents.z);
        return c;
    }

    static IEnumerable<(float x, float z)> MqGrid()
    {
        for (int ix = -3; ix <= 3; ix++)
            for (int iz = -3; iz <= 3; iz++) yield return (ix * 2.5f + 0.37f, iz * 2.5f + 0.61f);
    }

    static string MqText(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string MqText(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string MqText(Vector3 v) => $"({MqText(v.x)}, {MqText(v.y)}, {MqText(v.z)})";

    static MapStructure MqStructure(AppCore app) => app.Runtime.GetPart<MapRealization>().Structure;

    static IEnumerator MqLoad(AppCore app, string mapRef, double timeoutSec = 240)
    {
        yield return PCall(app, new { cmd = "map.load", @ref = mapRef, wait = true }, timeoutSec);
        Check(mapRef + " loads", POk && (string)pLast["state"] == "Loaded", Short(pLastText));
    }

    /// <summary>The renderer whose absence changes the answer at a point; null when no single one does.</summary>
    static Renderer MqSeenBy(MapStructure s, MapSurface q, Vector3 p, float above, float below, float height)
    {
        const float pad = 0.05f;
        foreach (var r in s.Renderers)
        {
            if (!MqDraws(r)) continue;
            var b = r.bounds;
            if (p.x < b.min.x - pad || p.x > b.max.x + pad || p.z < b.min.z - pad || p.z > b.max.z + pad) continue;
            r.forceRenderingOff = true;
            bool ok = q.Surface(p, out var h, above, below);
            r.forceRenderingOff = false;
            if (!ok || Mathf.Abs(h.Height - height) > 1e-4f) return r;
        }
        return null;
    }

    /// <summary>On a map of several roots: a renderer of a later root answers from above its own bounds.</summary>
    static void MqLaterRoot(MapStructure s, MapSurface q)
    {
        Info("roots: " + s.Roots.Length);
        if (s.Roots.Length < 2) return;
        var first = s.Roots[0].transform;
        foreach (var r in s.Renderers)
        {
            if (!MqDraws(r) || r.transform.root == first) continue;
            var b = r.bounds;
            if (b.size.x < 0.1f || b.size.z < 0.1f) continue;
            var p = new Vector3(b.center.x, b.max.y + 0.5f, b.center.z);
            bool ok = q.Surface(p, out var h, 0, b.size.y + 1);
            Info("a later root's renderer " + MqAbout(s, r) + ": " + (ok ? MqText(h.Height) : "no answer at its centre"));
            if (ok) { Check("a later root answers", h.Height >= b.min.y - MqTolerance && h.Height <= p.y, MqText(h.Height)); return; }
        }
    }

    static string MqAbout(MapStructure s, Renderer r)
    {
        if (!r) return "(no single renderer)";
        var m = r is SkinnedMeshRenderer k ? k.sharedMesh : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
        var b = r.bounds;
        return $"{r.GetType().Name} {s.PathOf(r.transform)} mesh {(m ? m.name : "none")} readable {(m && m.isReadable)} batched {r.isPartOfStaticBatch} layer {r.gameObject.layer} shader {(r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "none")} y {MqText(b.min.y)}..{MqText(b.max.y)}";
    }

    // ------------------------------------------------------------ a synthetic map, through whichever path this process has

    sealed class MqScene : IDisposable
    {
        public GameObject First, Second, Empty, Land;
        public GameObject OffObject;
        public Renderer OffRenderer;
        public Terrain Terrain;
        public MapStructure Structure;
        public MapSurface Query;
        readonly List<Object> owned = new List<Object>();
        readonly Material material = new Material(Shader.Find(MapSurface.ShaderName));

        public MqScene()
        {
            owned.Add(material);
            First = Root("mq first root");
            Second = Root("mq second root");
            Empty = Root("mq root with no renderer");
            Land = Root("mq land");
            Flat(First, "floor", 0, 0, 0, 40, false);
            Flat(First, "ceiling", 0, 30, 0, 40, true);
            Flat(Second, "table", 5, 3, 5, 4, false);
            Flat(First, "upside down", -5, 1, 5, 4, true);
            Quad(First, "steep", new Vector3(-7, 0.5f, -7), new Vector3(-3, 14.5f, -7), new Vector3(-3, 14.5f, -3), new Vector3(-7, 0.5f, -3));
            Quad(First, "slope", new Vector3(3, 1, -7), new Vector3(7, 5, -7), new Vector3(7, 5, -3), new Vector3(3, 1, -3));
            Quad(First, "long slope", new Vector3(100, 0, 90), new Vector3(1124, 512, 90), new Vector3(1124, 512, 110), new Vector3(100, 0, 110));
            Quad(First, "wall", new Vector3(-10, 0, -19), new Vector3(10, 0, -19), new Vector3(10, 20, -19), new Vector3(-10, 20, -19));
            OffObject = Flat(First, "off object", 12, 4, 12, 3, false);
            OffObject.SetActive(false);
            OffRenderer = Flat(First, "off renderer", -12, 4, 12, 3, false).GetComponent<Renderer>();
            OffRenderer.enabled = false;
            var line = new GameObject("line").AddComponent<LineRenderer>();
            line.transform.SetParent(First.transform, false);
            line.SetPositions(new[] { new Vector3(-1, 6, 0), new Vector3(1, 6, 0) });
            line.sharedMaterial = material;
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(64, 10, 64) };
            var heights = new float[33, 33];
            for (int y = 0; y < 33; y++) for (int x = 0; x < 33; x++) heights[y, x] = 0.5f;
            data.SetHeights(0, 0, heights);
            owned.Add(data);
            var land = Terrain.CreateTerrainGameObject(data);
            land.transform.SetParent(Land.transform, false);
            land.transform.position = new Vector3(200, 0, 200);
            Terrain = land.GetComponent<Terrain>();
            Flat(Land, "above the terrain", 210, 7, 210, 4, false);
            Flat(Land, "under the terrain", 220, 2, 220, 4, false);
            Structure = MapStructure.Build(new[] { First, Second, Empty, Land });
            Query = new MapSurface(() => Structure);
        }

        GameObject Root(string name)
        {
            var g = new GameObject(name);
            owned.Add(g);
            return g;
        }

        GameObject Flat(GameObject parent, string name, float x, float y, float z, float size, bool down)
        {
            float h = size / 2;
            Vector3 a = new Vector3(x - h, y, z - h), b = new Vector3(x - h, y, z + h), c = new Vector3(x + h, y, z + h), d = new Vector3(x + h, y, z - h);
            return down ? Quad(parent, name, d, c, b, a) : Quad(parent, name, a, b, c, d);
        }

        // no normals on purpose: the triangles decide
        public GameObject Quad(GameObject parent, string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            var m = new Mesh { name = name, vertices = new[] { a, b, c, d }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            m.RecalculateBounds();
            owned.Add(m);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().sharedMaterial = material;
            return g;
        }

        public void Dispose()
        {
            foreach (var o in owned) if (o) Object.DestroyImmediate(o);
        }
    }

    static void MqExpect(MapSurface q, string what, Vector3 p, float above, float below, float? height, string source, Vector3? normal = null)
    {
        bool ok = q.Surface(p, out var h, above, below);
        if (height == null)
        {
            Check(what + ": no answer", !ok && h.Source == null, ok ? MqText(h.Height) + " from " + h.Source : "");
            return;
        }
        Check(what, ok && Mathf.Abs(h.Height - height.Value) <= MqTolerance && h.Source == source, ok ? MqText(h.Height) + " from " + h.Source : "no answer");
        if (ok && normal != null) Check(what + ": its normal", (h.Normal - normal.Value).magnitude <= MqTolerance, MqText(h.Normal));
    }

    static void MqExpectRay(MapSurface q, string what, Ray ray, float max, float? distance, Vector3? normal = null)
    {
        bool ok = q.Ray(ray, max, out var point, out var n);
        if (distance == null)
        {
            Check(what + ": no answer", !ok, ok ? MqText(point) : "");
            return;
        }
        var want = ray.origin + ray.direction * distance.Value;
        Check(what, ok && (point - want).magnitude <= MqTolerance, ok ? MqText(point) + " wanted " + MqText(want) : "no answer");
        if (ok && normal != null) Check(what + ": its normal", (n - normal.Value).magnitude <= MqTolerance, MqText(n));
    }

    /// <summary>What every path answers the same on the synthetic map; <paramref name="source"/> is the source of a mesh's answer.</summary>
    static void MqCommon(MqScene m, string source)
    {
        var q = m.Query;
        var up = Vector3.up;
        MqExpect(q, "the floor under a point", new Vector3(0, 2, 0), 0, 60, 0, source, up);
        MqExpect(q, "a later root's table", new Vector3(5, 10, 5), 0, 60, 3, source, up);
        MqExpect(q, "a surface exactly at the point counts", new Vector3(5, 3, 5), 0, 60, 3, source);
        MqExpect(q, "under the table, the floor", new Vector3(5, 2.9f, 5), 0, 60, 0, source);
        MqExpect(q, "above reaches the table", new Vector3(5, 2.9f, 5), 0.2f, 60, 3, source);
        MqExpect(q, "from above the roof, the roof", new Vector3(0, 40, 0), 0, 60, 30, source, up);
        MqExpect(q, "under the roof, the floor", new Vector3(0, 29, 0), 0, 60, 0, source);
        MqExpect(q, "below ends the search", new Vector3(0, 10, 0), 0, 5, null, null);
        MqExpect(q, "a face turned down reads up", new Vector3(-5, 5, 5), 0, 60, 1, source, up);
        MqExpect(q, "a wall does not stop a height", new Vector3(-5, 20, -5), -10, 60, 0, source, up);
        MqExpect(q, "a slope, at the point itself", new Vector3(4, 8, -5), 0, 60, 2, source, new Vector3(-0.7071f, 0.7071f, 0));
        MqExpect(q, "a line is no surface", new Vector3(0, 8, 0), 0, 60, 0, source);
        MqExpect(q, "outside the map", new Vector3(-500, 8, 0), 0, 60, null, null);
        MqExpect(q, "a wrong range", new Vector3(0, 8, 0), -5, -2, null, null);
        MqExpect(q, "not a number", new Vector3(float.NaN, 8, 0), 0, 60, null, null);

        Info("bounds of an object that is off, standing at (12, 4, 12): " + MqText(m.OffObject.GetComponent<Renderer>().bounds.center) + " size " + MqText(m.OffObject.GetComponent<Renderer>().bounds.size) +
            "; of a renderer that is off, at (-12, 4, 12): " + MqText(m.OffRenderer.bounds.center) + " size " + MqText(m.OffRenderer.bounds.size));
        MqExpect(q, "an object that is off", new Vector3(12, 8, 12), 0, 60, 0, source);
        MqExpect(q, "a renderer that is off", new Vector3(-12, 8, 12), 0, 60, 0, source);
        m.OffObject.SetActive(true);
        m.OffRenderer.enabled = true;
        MqExpect(q, "the object on again, with the bounds already read", new Vector3(12, 8, 12), 0, 60, 4, source);
        MqExpect(q, "the renderer on again", new Vector3(-12, 8, 12), 0, 60, 4, source);
        m.OffObject.SetActive(false);
        m.OffRenderer.enabled = false;
        MqExpect(q, "off again", new Vector3(12, 8, 12), 0, 60, 0, source);

        var far = new Vector3(0, 0, 500);
        foreach (var g in new[] { m.First, m.Second, m.Empty, m.Land }) g.transform.position += far;
        Physics.SyncTransforms();
        MqExpect(q, "the map moved: there now", new Vector3(5, 10, 505), 0, 60, 3, source);
        MqExpect(q, "the map moved: nothing here", new Vector3(5, 10, 5), 0, 60, null, null);
        m.OffObject.SetActive(true);
        MqExpect(q, "an object that was off when the map moved, on now", new Vector3(12, 8, 512), 0, 60, 4, source);
        m.OffObject.SetActive(false);
        foreach (var g in new[] { m.First, m.Second, m.Empty, m.Land }) g.transform.position -= far;
        Physics.SyncTransforms();
        MqExpect(q, "the map back", new Vector3(5, 10, 5), 0, 60, 3, source);

        // terrain: the higher answer wins
        MqExpect(q, "a terrain", new Vector3(240, 20, 240), 0, 60, 5, MapSurface.SourceTerrain, up);
        MqExpect(q, "a mesh above the terrain", new Vector3(210, 20, 210), 0, 60, 7, source);
        MqExpect(q, "a mesh under the terrain", new Vector3(220, 20, 220), 0, 60, 5, MapSurface.SourceTerrain);
        MqExpect(q, "under the mesh, the terrain", new Vector3(210, 6, 210), 0, 60, 5, MapSurface.SourceTerrain);

        // several points: each answer at its own index, a point with none keeps nothing of what the array held
        var points = new List<Vector3>();
        for (int i = 0; i < 1500; i++) points.Add(new Vector3(100.5f + i * 0.5f, 600, 100));
        points[700] = new Vector3(-500, 8, 0);
        var hits = Enumerable.Repeat(new MapSurfaceHit { Height = 99, Source = "old" }, 1600).ToArray();
        int waits = MapSurface.Waits;
        int found = q.Surfaces(points, hits, 0, 700);
        int wrong = 0;
        for (int i = 0; i < 1500; i++)
        {
            if (i == 700) continue;
            float y = (points[i].x - 100f) / 2f;
            if (Mathf.Abs(hits[i].Height - y) > MqTolerance || hits[i].Source != source) wrong++;
        }
        Check("1,500 points, each at its index", found == 1499 && wrong == 0, found + " found, " + wrong + " wrong");
        Check("a point with no answer holds the default", hits[700].Source == null && hits[700].Height == 0f, hits[700].Source + " " + hits[700].Height);
        Check("hits past the points stay as they were", hits[1500].Source == "old");
        Info("1,500 points took " + (MapSurface.Waits - waits) + " waits");
        Check("hits shorter than the points", MqThrows<ArgumentException>(() => q.Surfaces(points, new MapSurfaceHit[3])));
        Check("no points", MqThrows<ArgumentNullException>(() => q.Surfaces(null, hits)));
        Check("no hits", MqThrows<ArgumentNullException>(() => q.Surfaces(points, null)));
        Check("nothing asked", q.Surfaces(new Vector3[0], new MapSurfaceHit[0]) == 0);

        // a ray: walls count, the normal looks back along it
        MqExpectRay(q, "a ray down to the floor", new Ray(new Vector3(0, 5, 0), Vector3.down), 100, 5, up);
        MqExpectRay(q, "a ray along z meets the wall", new Ray(new Vector3(0, 5, 0), Vector3.back), 100, 19, Vector3.forward);
        MqExpectRay(q, "the wall from behind", new Ray(new Vector3(0, 5, -25), Vector3.forward), 100, 6, Vector3.back);
        MqExpectRay(q, "a slanted ray", new Ray(new Vector3(0, 5, 0), new Vector3(0.6f, -0.8f, 0)), 100, 6.25f, up);
        MqExpectRay(q, "up to the roof, the normal down", new Ray(new Vector3(0, 5, 0), Vector3.up), 100, 25, Vector3.down);
        MqExpectRay(q, "short of the wall", new Ray(new Vector3(0, 5, 0), Vector3.back), 10, null);
        MqExpectRay(q, "no reach", new Ray(new Vector3(0, 5, 0), Vector3.down), 0, null);
        MqExpectRay(q, "no end to the reach", new Ray(new Vector3(0, 5, 0), Vector3.down), float.PositiveInfinity, 5, up);
        MqExpectRay(q, "out of the map", new Ray(new Vector3(0, 5, 0), Vector3.right), 2000, null);
        MqExpectRay(q, "a slanted ray to the terrain", new Ray(new Vector3(240, 8, 240), new Vector3(0.6f, -0.8f, 0)), 100, 3.75f, up);
    }

    /// <summary>A static batch under a root that does not stand at the origin: through the device each renderer answers for its own part of the batch's mesh; with no device its mesh is not read.</summary>
    static void MqBatched(bool device)
    {
        string source = device ? MapSurface.SourceGpu : null;
        var material = new Material(Shader.Find(MapSurface.ShaderName));
        var root = new GameObject("mq batched map");
        var meshes = new List<Mesh>();
        try
        {
            root.transform.position = new Vector3(300, 1, 300);
            foreach (var (name, x, y) in new[] { ("near", 0f, 1f), ("far", 10f, 2f) })
            {
                var g = new GameObject(name);
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = new Vector3(x, y, 0);
                var m = new Mesh { name = name, vertices = new[] { new Vector3(-2, 0, -2), new Vector3(-2, 0, 2), new Vector3(2, 0, 2), new Vector3(2, 0, -2) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
                m.RecalculateBounds();
                meshes.Add(m);
                g.AddComponent<MeshFilter>().sharedMesh = m;
                g.AddComponent<MeshRenderer>().sharedMaterial = material;
            }
            StaticBatchingUtility.Combine(root);
            var s = MapStructure.Build(new[] { root });
            var q = new MapSurface(() => s);
            bool batched = s.Renderers.All(r => r.isPartOfStaticBatch);
            Info("a static batch made here: " + batched + ", mesh " + (s.Renderers[0].GetComponent<MeshFilter>().sharedMesh?.name ?? "none") + ", readable " + s.Renderers[0].GetComponent<MeshFilter>().sharedMesh?.isReadable);
            Check("the renderers are in a static batch", batched);
            MqExpect(q, "a static batch: one renderer", new Vector3(300, 8, 300), 0, 60, device ? 2 : (float?)null, source);
            MqExpect(q, "a static batch: the other", new Vector3(310, 8, 300), 0, 60, device ? 3 : (float?)null, source);
            MqExpect(q, "a static batch: between them", new Vector3(305, 8, 300), 0, 60, null, null);
            s.Renderers[0].enabled = false;
            MqExpect(q, "a static batch: a renderer off", new Vector3(300, 8, 300), 0, 60, null, null);
            MqExpect(q, "a static batch: the other stays", new Vector3(310, 8, 300), 0, 60, device ? 3 : (float?)null, source);
        }
        finally
        {
            Object.DestroyImmediate(root);
            foreach (var m in meshes) Object.DestroyImmediate(m);
            Object.DestroyImmediate(material);
        }
    }

    static bool MqThrows<T>(Action a) where T : Exception
    {
        try { a(); return false; }
        catch (T) { return true; }
    }

    // ------------------------------------------------------------ headless (WetTest.Run)

    static void Headless_Map()
    {
        Group("WG7", "with no device: meshes, then colliders", () =>
        {
            bool device = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (device) Warn("this process has a device: the row runs with -nographics");
            using (var m = new MqScene())
            {
                MqCommon(m, device ? MapSurface.SourceGpu : MapSurface.SourceMesh);
                Check("no draw, no wait", device || MapSurface.LastDraws == 0);
            }
            MqBatched(device);
            var root = new GameObject("mq collider map");
            var stranger = new GameObject("mq not of the map");
            var hidden = new GameObject("mq unreadable");
            var mesh = new Mesh { name = "mq unreadable", vertices = new[] { new Vector3(-1, 4, -1), new Vector3(-1, 4, 1), new Vector3(1, 4, 1) }, triangles = new[] { 0, 1, 2 } };
            try
            {
                var solid = new GameObject("solid");
                solid.transform.SetParent(root.transform, false);
                solid.transform.position = new Vector3(0, 1, 0);
                solid.AddComponent<BoxCollider>().size = new Vector3(20, 2, 20);
                var steep = new GameObject("steep");
                steep.transform.SetParent(root.transform, false);
                steep.transform.position = new Vector3(8, 6, 8);
                steep.transform.rotation = Quaternion.Euler(0, 0, 85);
                steep.AddComponent<BoxCollider>().size = new Vector3(4, 0.2f, 4);
                stranger.transform.position = new Vector3(0, 6, 0);
                stranger.AddComponent<BoxCollider>().size = new Vector3(4, 1, 4);
                var s = MapStructure.Build(new[] { root });
                var q = new MapSurface(() => s);
                Physics.SyncTransforms();
                if (!device)
                {
                    MqExpect(q, "a collider answers", new Vector3(3, 12, 3), 0, 60, 2, MapSurface.SourceCollider, Vector3.up);
                    MqExpect(q, "a collider outside the map's roots does not", new Vector3(0, 12, 0), 0, 60, 2, MapSurface.SourceCollider);
                    MqExpect(q, "a steep collider does not stop a height", new Vector3(8, 12, 8), 0, 60, 2, MapSurface.SourceCollider);
                    MqExpect(q, "nothing under the point", new Vector3(50, 12, 0), 0, 60, null, null);
                    MqExpectRay(q, "a ray to a collider", new Ray(new Vector3(3, 12, 3), Vector3.down), 100, 10, Vector3.up);
                }
                // an unreadable mesh is not walked; its collider answers
                hidden.transform.SetParent(root.transform, false);
                hidden.AddComponent<MeshFilter>().sharedMesh = mesh;
                hidden.AddComponent<MeshRenderer>();
                hidden.transform.position = new Vector3(40, 0, 40);
                mesh.UploadMeshData(true);
                Info("a mesh after UploadMeshData(true): readable " + mesh.isReadable);
                var box = hidden.AddComponent<BoxCollider>();
                box.center = new Vector3(0, 2, 0);
                box.size = new Vector3(2, 1, 2);
                var s2 = MapStructure.Build(new[] { root });
                var q2 = new MapSurface(() => s2);
                Physics.SyncTransforms();
                if (!device && !mesh.isReadable) MqExpect(q2, "an unreadable mesh leaves it to its collider", new Vector3(40.2f, 12, 40.4f), 0, 60, 2.5f, MapSurface.SourceCollider);
                else if (!device) MqExpect(q2, "the mesh is above its collider", new Vector3(40.2f, 12, 40.4f), 0, 60, 4, MapSurface.SourceMesh);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(stranger);
                Object.DestroyImmediate(mesh);
            }
        });
    }

    // ------------------------------------------------------------ play (WetTest.RunPlay)

    static IEnumerator Play_Map()
    {
        current = "Map";
        if (!new[] { "WG1", "WG2", "WG3", "WG4", "WG5", "WG6", "WG10" }.Any(Want)) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        yield return PCall(app, new { cmd = "scene.new" });
        if (POpen("WG5", "no map")) yield return MqWg5(app);
        if (POpen("WG1", "the floor around a character equals the readable triangles")) yield return MqWg1(app);
        if (POpen("WG2", "a map with no readable mesh")) yield return MqWg2(app);
        if (POpen("WG4", "several roots, a root with no renderer, a terrain")) yield return MqWg4(app);
        if (POpen("WG10", "a ray against the map")) yield return MqWg10(app);
        if (POpen("WG3", "the 30 vanilla maps against their triangles")) yield return MqWg3(app);
        if (POpen("WG6", "the scan on the largest map")) yield return MqWg6(app);
        yield return PCall(app, new { cmd = "scene.new" });
    }

    static IEnumerator MqWg5(AppCore app)
    {
        var q = MapSurface.Of(app.Runtime);
        var none = MapSurface.Of((SceneRuntime)null);
        var noFunc = MapSurface.Of((Func<SceneRuntime>)(() => null));
        foreach (var (name, v) in new[] { ("no map", q), ("no runtime", none), ("a runtime that is gone", noFunc) })
        {
            Check(name + ": not loaded", !v.Loaded);
            Check(name + ": no roots", v.Roots != null && v.Roots.Count == 0);
            Check(name + ": no surface", !v.Surface(Vector3.up, out var h) && h.Source == null);
            var hits = new[] { new MapSurfaceHit { Height = 5, Source = "old" } };
            Check(name + ": no surfaces", v.Surfaces(new[] { Vector3.up }, hits) == 0 && hits[0].Source == null);
            Check(name + ": no ray", !v.Ray(new Ray(Vector3.up, Vector3.down), 10, out _, out _));
        }
        yield return MqLoad(app, MqFloorMap);
        Check("a map: loaded, roots", q.Loaded && q.Roots.Count > 0, q.Roots.Count.ToString());
        Check("a map: a view made before it answers", q.Surface(new Vector3(0.37f, 10, 0.61f), out var y), y.Source);
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PFrames(3);
        Check("the map gone: not loaded", !q.Loaded && q.Roots.Count == 0 && !q.Surface(new Vector3(0.37f, 10, 0.61f), out _));
    }

    static IEnumerator MqWg1(AppCore app)
    {
        yield return MqLoad(app, MqFloorMap);
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" }, 240);
        if (!Check("chara.load", POk, Short(pLastText))) yield break;
        string key = Key(pLast);
        yield return PFrames(3);
        var hips = app.Runtime.FindChara(key).Context.BodyBones["cf_J_Hips"].position;
        var s = MqStructure(app);
        var census = new MqCensus();
        var parts = MqParts(s, census);
        Info(MqFloorMap + ": " + census);
        var q = MapSurface.Of(app.Runtime);
        var points = new List<Vector3>();
        for (int ix = -2; ix <= 2; ix++)
            for (int iz = -2; iz <= 2; iz++) points.Add(hips + new Vector3(ix * 1.3f, 0, iz * 1.3f));
        Info("the character's hips at " + MqText(hips));
        double worst = 0;
        var single = new List<float>();
        foreach (var p in points)
        {
            bool cpu = MqBelow(parts, p.x, p.z, p.y, p.y - 60, out double y, out var normal, out var by);
            bool gpu = q.Surface(p, out var hit);
            single.Add(gpu ? hit.Height : float.NaN);
            if (!Check("a surface under " + MqText(p), cpu && gpu, $"triangles {cpu}, device {gpu}")) continue;
            double e = Math.Abs(hit.Height - y);
            worst = Math.Max(worst, e);
            Check("its height", e <= MqTolerance && hit.Source == MapSurface.SourceGpu, $"{MqText(hit.Height)} against {MqText(y)}, {hit.Source}");
            Check("its normal", (hit.Normal - normal).magnitude <= MqTolerance, MqText(hit.Normal) + " against " + MqText(normal));
        }
        Measure($"WG1 {points.Count} points, largest difference {worst:G4}");
        var hits = new MapSurfaceHit[points.Count];
        int waits = MapSurface.Waits;
        int found = q.Surfaces(points, hits);
        Check("several points, one wait", found == points.Count && MapSurface.Waits == waits + 1, found + " found, " + (MapSurface.Waits - waits) + " waits");
        Check("the same answers as one by one", Enumerable.Range(0, points.Count).All(i => hits[i].Height == single[i]));
        yield return PCall(app, new { cmd = "scene.new" });
    }

    static IEnumerator MqWg2(AppCore app)
    {
        yield return MqLoad(app, MqNoMeshMap);
        var s = MqStructure(app);
        var census = new MqCensus();
        var parts = MqParts(s, census);
        Info(MqNoMeshMap + ": " + census);
        Check("the map has no readable mesh", census.Readable == 0, census.ToString());
        var q = MapSurface.Of(app.Runtime);
        int answers = 0, device = 0, inside = 0;
        foreach (var (x, z) in MqGrid())
        {
            if (!q.Surface(new Vector3(x, 30, z), out var h, 0, 60)) continue;
            answers++;
            if (h.Source == MapSurface.SourceGpu) device++;
            var at = new Vector3(x, h.Height, z);
            if (s.Renderers.Any(r => { if (!MqDraws(r)) return false; var b = r.bounds; b.Expand(0.02f); return b.Contains(at); })) inside++;
        }
        Check("every point of the grid answers, from the device", answers == 49 && device == 49, answers + " answers, " + device + " from the device");
        Check("every answer lies in the bounds of a renderer that draws", inside == answers, inside + " of " + answers);
        MqLaterRoot(s, q);

        // a static batch does not follow the map's pose: the answer stays where it is drawn
        var batched = s.Renderers.First(r => MqDraws(r) && r.isPartOfStaticBatch);
        var boxBefore = batched.bounds;
        var at0 = new Vector3(0.37f, 30, 0.61f);
        bool ok0 = q.Surface(at0, out var h0, 0, 60);
        yield return PCall(app, new { cmd = "map.set", pos = new[] { 0f, 5f, 0f } });
        yield return PFrames(3);
        bool ok1 = q.Surface(at0, out var h1, 0, 60);
        var boxMoved = batched.bounds;
        Info($"the map moved 5 up: a batched renderer's bounds {MqText(boxBefore.center)} to {MqText(boxMoved.center)}; the height {MqText(h0.Height)} to {MqText(h1.Height)}");
        Check("the batch's bounds and the answer stay together", ok0 && ok1 && Mathf.Abs((boxMoved.center.y - boxBefore.center.y) - (h1.Height - h0.Height)) <= MqTolerance);
        yield return PCall(app, new { cmd = "map.set", pos = new[] { 0f, 0f, 0f } });
        yield return PFrames(3);

        yield return MqLoad(app, MqColliderMap);
        s = MqStructure(app);
        census = new MqCensus();
        parts = MqParts(s, census);
        Info(MqColliderMap + ": " + census);
        q = MapSurface.Of(app.Runtime);
        MqLaterRoot(s, q);
        foreach (float top in new[] { 30f, 12f, 5f })
        {
            int both = 0, agree = 0, onlyDevice = 0, onlyColliders = 0, shown = 0;
            double worst = 0;
            foreach (var (x, z) in MqGrid())
            {
                var p = new Vector3(x, top, z);
                bool has = q.Surface(p, out var h, 0, 60);
                bool hit = Physics.Raycast(p, Vector3.down, out var rh, 60);
                if (has && !hit) onlyDevice++;
                else if (hit && !has) onlyColliders++;
                if (!has || !hit) continue;
                both++;
                double e = Math.Abs(h.Height - rh.point.y);
                worst = Math.Max(worst, e);
                if (e <= MqTolerance) agree++;
                else if (shown++ < 6) Info($"top {top} at ({MqText(x)}, {MqText(z)}): device {MqText(h.Height)}, collider {MqText(rh.point.y)} on {s.PathOf(rh.collider.transform)} ({rh.collider.GetType().Name}), seen {MqAbout(s, MqSeenBy(s, q, p, 0, 60, h.Height))}");
            }
            Info($"top {top}, against the map's colliders: {both} points with both, {onlyDevice} only the device, {onlyColliders} only the colliders, {agree} within {MqTolerance}, largest difference {worst:G4}");
        }
    }

    static IEnumerator MqWg4(AppCore app)
    {
        using (var m = new MqScene()) MqCommon(m, MapSurface.SourceGpu);
        MqBatched(true);

        // a real terrain
        yield return MqLoad(app, MqTerrainMap);
        var s = MqStructure(app);
        Check("a map with a terrain", s.Terrains.Count > 0, s.Terrains.Count.ToString());
        var q = MapSurface.Of(app.Runtime);
        MqLaterRoot(s, q);
        int fromTerrain = 0, fromMesh = 0, wrong = 0;
        foreach (var t in s.Terrains.Where(x => x && x.enabled && x.drawHeightmap && x.gameObject.activeInHierarchy))
        {
            Vector3 o = t.GetPosition(), size = t.terrainData.size;
            for (int i = 1; i < 8; i++)
                for (int k = 1; k < 8; k++)
                {
                    var p = new Vector3(o.x + size.x * i / 8f, o.y + size.y + 1, o.z + size.z * k / 8f);
                    float y = o.y + t.SampleHeight(p);
                    if (!q.Surface(p, out var h, 0, size.y + 2)) { if (t.terrainData.GetInterpolatedNormal(i / 8f, k / 8f).y >= MapSurface.MinNormalY) wrong++; continue; }
                    if (h.Source == MapSurface.SourceTerrain) { fromTerrain++; if (Mathf.Abs(h.Height - y) > 0.001f) wrong++; }
                    else { fromMesh++; if (h.Height < y - 0.001f) wrong++; }
                }
        }
        Info($"{MqTerrainMap}: {s.Terrains.Count} terrains, {fromTerrain} points from a terrain, {fromMesh} from a mesh over it");
        Check("the terrain answers, a mesh over it wins", fromTerrain > 0 && wrong == 0, $"{fromTerrain} from a terrain, {wrong} wrong");
        yield return PCall(app, new { cmd = "scene.new" });
    }

    static IEnumerator MqWg10(AppCore app)
    {
        yield return MqLoad(app, MqFloorMap);
        var s = MqStructure(app);
        var census = new MqCensus();
        var parts = MqParts(s, census);
        Info(MqFloorMap + ": " + census);
        var q = MapSurface.Of(app.Runtime);
        var dirs = new[] { new Vector3(0.3f, -0.9f, 0.2f), new Vector3(-0.5f, -0.5f, 0.7f), new Vector3(0.9f, -0.1f, -0.4f), new Vector3(0, -1, 0), new Vector3(0.2f, 0.9f, 0.3f) };
        int both = 0, onlyDevice = 0, onlyTriangles = 0, none = 0, differ = 0;
        double worst = 0;
        foreach (var (x, z) in MqGrid())
            foreach (var d in dirs)
            {
                var ray = new Ray(new Vector3(x, 8, z), d);
                bool gpu = q.Ray(ray, 200, out var point, out var normal);
                bool cpu = MqAlong(parts, ray, 200, out double t, out var want);
                if (gpu && cpu)
                {
                    both++;
                    double e = Math.Abs((point - ray.origin).magnitude - t);
                    if (e > worst) worst = e;
                    if (e > MqTolerance || (normal - want).magnitude > 0.02f)
                    {
                        differ++;
                        Info($"ray from {MqText(ray.origin)} along {MqText(ray.direction)}: device {MqText((point - ray.origin).magnitude)} {MqText(normal)}, triangles {MqText(t)} {MqText(want)}");
                    }
                }
                else if (gpu) onlyDevice++;
                else if (cpu) onlyTriangles++;
                else none++;
            }
        Measure($"WG10 {both + onlyDevice + onlyTriangles + none} rays on {MqFloorMap}: {both} both, {onlyDevice} only the device, {onlyTriangles} only the triangles, {none} neither, {differ} differ, largest {worst:G4}");
        Check("the rays agree with the triangles", both > 0 && differ == 0 && onlyDevice == 0 && onlyTriangles == 0);

        yield return MqLoad(app, MqNoMeshMap);
        q = MapSurface.Of(app.Runtime);
        int rays = 0, agree = 0;
        foreach (var (x, z) in MqGrid())
        {
            var p = new Vector3(x, 12, z);
            if (!q.Surface(p, out var h, 0, 60)) continue;
            rays++;
            if (q.Ray(new Ray(p, Vector3.down), 80, out var point, out _) && Mathf.Abs(point.y - h.Height) <= MqTolerance) agree++;
        }
        Check("a map with no readable mesh: a ray down lands on the height", rays > 0 && agree == rays, $"{agree} of {rays}");
        yield return PCall(app, new { cmd = "scene.new" });
    }

    static IEnumerator MqWg3(AppCore app)
    {
        yield return PCall(app, new { cmd = "maps" }, 120);
        var rows = pLast["maps"]?.Select(x => (string)x["id"]).Where(x => x != null && x.StartsWith("illusion.hs2#", StringComparison.Ordinal)).ToList() ?? new List<string>();
        Check("30 vanilla maps", rows.Count == 30, rows.Count.ToString());
        var sw = Stopwatch.StartNew();
        const float bottom = -30f;
        var tops = new[] { 12f, 30f };
        var differences = new int[tops.Length];
        var named = new int[tops.Length];
        var surfaces = new int[tops.Length];
        var worst = new double[tops.Length];
        int total = 0, atTop = 0, shadowsOnly = 0, unanswered = 0;
        var ceilings = new SortedSet<string>(StringComparer.Ordinal);
        int off = 0, offElsewhere = 0;
        foreach (var mapRef in rows)
        {
            yield return PCall(app, new { cmd = "map.load", @ref = mapRef, wait = true }, 240);
            if (!Check(mapRef + " loads", POk && (string)pLast["state"] == "Loaded", Short(pLastText))) continue;
            var s = MqStructure(app);
            var census = new MqCensus();
            var parts = MqParts(s, census);
            var shadowParts = MqParts(s, new MqCensus(), true);
            var q = MapSurface.Of(app.Runtime);
            // the bounds of what is off must be where it would draw: the read does not look at on or off
            foreach (var r in s.Renderers)
            {
                if (!r || (r.enabled && r.gameObject.activeInHierarchy) || r is SkinnedMeshRenderer || r.isPartOfStaticBatch) continue;
                var mesh = r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                if (!mesh) continue;
                off++;
                var want = GeometryUtility.CalculateBounds(MqCorners(mesh.bounds), r.transform.localToWorldMatrix);
                var got = r.bounds;
                if ((got.center - want.center).magnitude > MqTolerance || (got.size - want.size).magnitude > MqTolerance)
                {
                    offElsewhere++;
                    Info($"{mapRef}: off, {s.PathOf(r.transform)}: bounds {MqText(got.center)} {MqText(got.size)}, by its mesh {MqText(want.center)} {MqText(want.size)}");
                }
            }
            for (int k = 0; k < tops.Length; k++)
            {
                float top = tops[k];
                int both = 0, onlyDevice = 0, onlyTriangles = 0, none = 0, differ = 0, shadows = 0;
                double largest = 0;
                foreach (var (x, z) in MqGrid())
                {
                    var p = new Vector3(x, top, z);
                    bool gpu = q.Surface(p, out var hit, 0, top - bottom);
                    bool cpu = MqBelow(parts, x, z, top + MqMargin, bottom - MqMargin, out double y, out var normal, out var by);
                    total++;
                    if (!gpu && !cpu)
                    {
                        none++;
                        if (MqBelow(shadowParts, x, z, top + MqMargin, bottom - MqMargin, out _, out _, out _)) shadows++;
                        continue;
                    }
                    if (gpu && cpu)
                    {
                        both++;
                        double e = Math.Abs(hit.Height - y);
                        largest = Math.Max(largest, e);
                        if (top - hit.Height <= MqTolerance) { atTop++; ceilings.Add(mapRef + " (" + (by ? by.name : "") + ")"); }
                        if (e <= MqTolerance) continue;
                        differ++;
                    }
                    else if (gpu) onlyDevice++;
                    else onlyTriangles++;
                    differences[k]++;
                    var seen = gpu ? MqSeenBy(s, q, p, 0, top - bottom, hit.Height) : null;
                    // the triangles cannot show a mesh that cannot be read, or a skinned one as it is drawn
                    var mesh = seen is SkinnedMeshRenderer sk ? sk.sharedMesh : seen && seen.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
                    bool cause = seen && (seen is SkinnedMeshRenderer || !mesh || !mesh.isReadable);
                    if (cause) named[k]++;
                    Info($"{mapRef} top {top} at ({MqText(x)}, {MqText(z)}): device {(gpu ? MqText(hit.Height) + " " + hit.Source : "none")}, triangles {(cpu ? MqText(y) : "none")} ({(by ? s.PathOf(by.transform) : "")}); seen {MqAbout(s, seen)}");
                }
                surfaces[k] += both;
                worst[k] = Math.Max(worst[k], largest);
                shadowsOnly += shadows;
                unanswered += none;
                Info($"{mapRef} top {top}: {both} both, {onlyDevice} only the device, {onlyTriangles} only the triangles, {none} neither ({shadows} of them over a renderer that only casts a shadow), {differ} differ, largest {largest:G4}; {census}");
            }
        }
        Measure($"WG3 {rows.Count} maps, {total} questions, {sw.Elapsed.TotalSeconds:F0} s (Editor)");
        Measure($"WG3 {off} renderers with a mesh that are off when the map loads, {offElsewhere} of them with bounds that are not their mesh's");
        Check("what is off has its true bounds", offElsewhere == 0, offElsewhere + " of " + off);
        for (int k = 0; k < tops.Length; k++)
        {
            Measure($"WG3 top {tops[k]}: {surfaces[k]} points with a surface on both sides, {differences[k]} differences, {named[k]} of them on a mesh the triangles cannot show, largest gap {worst[k]:G4}");
            Check($"top {tops[k]}: the device and the triangles agree, or the difference is named", differences[k] == named[k], differences[k] + " differences, " + named[k] + " named");
        }
        Measure($"WG3 {atTop} points answered a surface at the top itself: a ceiling. {unanswered} points with no surface on either side, {shadowsOnly} of them over a renderer that only casts a shadow");
        Info("ceilings at the top: " + string.Join(", ", ceilings));
        yield return PCall(app, new { cmd = "scene.new" });
    }

    /// <summary>256 questions; reports them and answers their median in milliseconds.</summary>
    static double MqTimed(MapSurface q, string what, Func<int, int, Vector3> at, float below)
    {
        var scan = new List<double>();
        var all = new List<double>();
        var draws = new List<int>();
        int answers = 0;
        var sw = new Stopwatch();
        for (int i = 0; i < 16; i++)
            for (int k = 0; k < 16; k++)
            {
                var p = at(i, k);
                sw.Restart();
                if (q.Surface(p, out _, 0, below)) answers++;
                all.Add(sw.Elapsed.TotalMilliseconds);
                scan.Add(MapSurface.LastScanMs);
                draws.Add(MapSurface.LastDraws);
            }
        scan.Sort();
        all.Sort();
        draws.Sort();
        Measure($"WG6 {what}: {all.Count} questions, {answers} answers: the scan median {scan[scan.Count / 2]:F3} ms, largest {scan[scan.Count - 1]:F3} ms; a question median {all[all.Count / 2]:F3} ms, p95 {all[all.Count * 95 / 100]:F3} ms, largest {all[all.Count - 1]:F3} ms; draws median {draws[draws.Count / 2]}, largest {draws[draws.Count - 1]} (Editor)");
        return all[all.Count / 2];
    }

    static IEnumerator MqWg6(AppCore app)
    {
        string mapRef = Arg("-wg6") ?? MqLargestVanilla;
        double loadSec = double.Parse(Arg("-wg6LoadSec") ?? "240", CultureInfo.InvariantCulture);
        yield return PCall(app, new { cmd = "scene.new" });
        var sw = Stopwatch.StartNew();
        yield return MqLoad(app, mapRef, loadSec);
        if (app.Runtime.GetPart<MapRealization>().State != MapState.Loaded) yield break;
        var s = MqStructure(app);
        Info($"{mapRef}: load {sw.Elapsed.TotalSeconds:F1} s, {s.Renderers.Count} renderers, {s.Roots.Length} roots");
        var q = MapSurface.Of(app.Runtime);
        var box = new Bounds();
        bool first = true;
        foreach (var r in s.Renderers)
        {
            if (!MqDraws(r)) continue;
            if (first) { box = r.bounds; first = false; }
            else box.Encapsulate(r.bounds);
        }
        Info("the drawn renderers lie within " + MqText(box.min) + " to " + MqText(box.max));
        var parts = Stopwatch.StartNew();
        float sum = 0;
        foreach (var r in s.Renderers) sum += r.bounds.min.x;
        double boundsMs = parts.Elapsed.TotalMilliseconds;
        parts.Restart();
        int on = 0;
        foreach (var r in s.Renderers) if (r.enabled && r.gameObject.activeInHierarchy) on++;
        Measure($"WG6 {mapRef}: {on} of {s.Renderers.Count} renderers on; a pass of bounds alone {boundsMs:F2} ms, a pass of on or off alone {parts.Elapsed.TotalMilliseconds:F2} ms (Editor; {sum:F0})");
        sw.Restart();
        bool any = q.Surface(new Vector3(box.center.x, box.max.y + 1, box.center.z), out var hit, 0, box.size.y + 2);
        double firstMs = sw.Elapsed.TotalMilliseconds, indexMs = MapSurface.LastIndexMs;
        Measure($"WG6 {mapRef}: the first question {firstMs:F2} ms (Editor), of which the bounds read {indexMs:F2} ms");
        double median = MqTimed(q, mapRef + " over the whole map", (i, k) => new Vector3(box.min.x + box.size.x * (i + 0.5f) / 16f, box.max.y + 1, box.min.z + box.size.z * (k + 0.5f) / 16f), box.size.y + 2);
        double near = MqTimed(q, mapRef + " around the origin, where a character stands", (i, k) => new Vector3((i - 7.5f) * 5f, 20, (k - 7.5f) * 5f), 60);
        var points = new List<Vector3>();
        for (int i = 0; i < 256; i++) points.Add(new Vector3(box.min.x + box.size.x * (i % 16 + 0.5f) / 16f, box.max.y + 1, box.min.z + box.size.z * (i / 16 + 0.5f) / 16f));
        var hits = new MapSurfaceHit[256];
        sw.Restart();
        q.Surfaces(points, hits, 0, box.size.y + 2);
        Measure($"WG6 {mapRef}: 256 points with one wait {sw.Elapsed.TotalMilliseconds:F2} ms, the scan {MapSurface.LastScanMs:F2} ms (Editor)");
        for (int i = 0; i < 2; i++)
        {
            yield return PCall(app, new { cmd = "map.set", pos = new[] { 0f, i == 0 ? 1f : 0f, 0f } });
            yield return PFrames(3);
            q.Surface(new Vector3(box.center.x, box.max.y + 2, box.center.z), out hit, 0, box.size.y + 3);
            Measure($"WG6 {mapRef}: the map moved, the bounds read again {MapSurface.LastIndexMs:F2} ms (Editor)");
        }
        Check("the scan was measured", !first && median > 0 && near > 0);
        if (Math.Max(median, near) > 1) Warn($"a question's median {Math.Max(median, near):F3} ms is over 1 ms");
        if (firstMs > 60) Warn($"the first question {firstMs:F2} ms is over 60 ms");
        yield return PCall(app, new { cmd = "scene.new" });
    }
}
