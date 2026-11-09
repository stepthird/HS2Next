// The map query: the height of the map's surface under a point, and the first surface along a ray. Nothing is built when a map loads. The first question of a map reads
// every renderer's world bounds into one array, shared by every view of that map and dropped when the map moves or another map comes; a question then scans the array
// and looks only at what lies under it.
//   With a device  the renderers under the point are drawn into one pixel of a float target through a thin orthographic view along the question, one pixel per point, and
//                  the pixels come back in one wait. Any mesh answers, readable or not. The pixel's centre is the point itself.
//   Terrains       sampled at the point; the higher answer wins (for a ray, the nearer).
//   With no device the renderers' readable meshes, triangle by triangle; with no answer from them, the colliders under the map's roots.
// What counts: every renderer of the map that draws (a particle, trail or line does not; alpha-cut and transparent surfaces do), under the point and between the two
// heights. Characters and items are not in the map. A surface whose normal has a y below 0.3, up or down, is a wall: a height never stops there. A ray stops at walls too.
// Main thread only. The call waits for its answer, so it suits a click or a few points, not many points on every frame.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HS2Next.Api.Unity;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The surface of one loaded map, as a view: it holds nothing of its own, so a view may outlive a map.</summary>
    public sealed class MapSurface : IMapQuery
    {
        /// <summary>A surface whose normal has a smaller |y| is a wall.</summary>
        public const float MinNormalY = 0.3f;
        /// <summary>What <see cref="MapSurfaceHit.Source"/> says.</summary>
        public const string SourceGpu = "gpu", SourceMesh = "mesh", SourceTerrain = "terrain", SourceCollider = "collider";
        public const string ShaderName = "HS2Next/Hidden/MapSurface";

        // Half the view's width. The answer is the pixel's centre, so this only sets how finely a triangle's corners are placed.
        const float Window = 0.02f;
        // A height range starts and ends this far outside its two heights, so that a surface exactly at the point counts.
        const float Margin = 1e-3f;
        // Points per wait.
        const int MaxPoints = 1024;

        const GraphicsFormat ColorFormat = GraphicsFormat.R32G32B32A32_SFloat, DepthFormat = GraphicsFormat.D32_SFloat;

        readonly Func<MapStructure> structure;

        /// <param name="structure">The loaded map's structure, or null while none is loaded.</param>
        public MapSurface(Func<MapStructure> structure) { this.structure = structure ?? (() => null); }

        /// <summary>The map of a runtime; no runtime is no map.</summary>
        public static MapSurface Of(SceneRuntime runtime) => new MapSurface(() => LoadedOf(runtime));

        /// <summary>The map of whatever runtime is current at each question.</summary>
        public static MapSurface Of(Func<SceneRuntime> runtime) => new MapSurface(() => LoadedOf(runtime?.Invoke()));

        static MapStructure LoadedOf(SceneRuntime rt)
        {
            var map = rt?.GetPart<MapRealization>();
            return map != null && map.State == MapState.Loaded ? map.Structure : null;
        }

        // ------------------------------------------------------------ what the last question did (tests, time rows)

        /// <summary>Renderers drawn or walked for the last question.</summary>
        public static int LastCandidates { get; private set; }
        /// <summary>Draws of the last question.</summary>
        public static int LastDraws { get; private set; }
        /// <summary>Milliseconds spent in the bounds array by the last question.</summary>
        public static double LastScanMs { get; private set; }
        /// <summary>Milliseconds the last read of a map's bounds took.</summary>
        public static double LastIndexMs { get; private set; }
        /// <summary>Waits for the device so far.</summary>
        public static int Waits { get; private set; }

        // ------------------------------------------------------------ IMapQuery

        public bool Loaded => structure() != null;

        public IReadOnlyList<GameObject> Roots => (IReadOnlyList<GameObject>)structure()?.Roots ?? Array.Empty<GameObject>();

        static readonly Vector3[] onePoint = new Vector3[1];
        static readonly MapSurfaceHit[] oneHit = new MapSurfaceHit[1];

        public bool Surface(Vector3 point, out MapSurfaceHit hit, float above = 0f, float below = 60f)
        {
            onePoint[0] = point;
            int n = Surfaces(onePoint, oneHit, above, below);
            hit = oneHit[0];
            return n > 0;
        }

        public int Surfaces(IReadOnlyList<Vector3> points, MapSurfaceHit[] hits, float above = 0f, float below = 60f)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (hits == null) throw new ArgumentNullException(nameof(hits));
            int count = points.Count;
            if (hits.Length < count) throw new ArgumentException("hits is shorter than points", nameof(hits));
            for (int i = 0; i < count; i++) hits[i] = default;
            var s = structure();
            if (s == null || count == 0) return 0;
            var index = BoundsIndex.Of(s);
            int found = 0;
            LastCandidates = LastDraws = 0;
            LastScanMs = 0;
            for (int start = 0; start < count; start += MaxPoints)
            {
                int n = Math.Min(MaxPoints, count - start);
                EnsureLooks(n);
                for (int i = 0; i < n; i++) looks[i] = Look.Down(points[start + i], above, below);
                Answer(index, n, Vector3.zero, Vector3.up, Vector3.down, MinNormalY);
                for (int i = 0; i < n; i++)
                {
                    if (!looks[i].Valid) continue;
                    var seen = answers[i];
                    string source = seen.Hit ? seen.Source : null;
                    if (TerrainBelow(s, looks[i], out float y, out var normal) && (!seen.Hit || y > seen.Value))
                    {
                        seen = new Seen { Hit = true, Value = y, Normal = normal };
                        source = SourceTerrain;
                    }
                    if (!seen.Hit) continue;
                    hits[start + i] = new MapSurfaceHit { Height = seen.Value, Normal = seen.Normal, Source = source };
                    found++;
                }
            }
            return found;
        }

        // ------------------------------------------------------------ a ray

        /// <summary>
        /// The first surface of the map along a ray, walls included, within <paramref name="maxDistance"/>. The normal is the surface's, turned towards the ray's origin.
        /// </summary>
        public bool Ray(Ray ray, float maxDistance, out Vector3 point, out Vector3 normal)
        {
            point = default;
            normal = default;
            var s = structure();
            if (s == null || !(maxDistance > 0f)) return false;
            var index = BoundsIndex.Of(s);
            LastCandidates = LastDraws = 0;
            LastScanMs = 0;
            var dir = ray.direction;
            looks[0] = new Look(ray.origin, dir, maxDistance, false);
            if (!looks[0].Valid) return false;
            Answer(index, 1, ray.origin, dir, dir, 0f);
            var seen = answers[0];
            if (TerrainAlong(s, looks[0], out float t, out var n) && (!seen.Hit || t < seen.Value)) seen = new Seen { Hit = true, Value = t, Normal = n };
            if (!seen.Hit) return false;
            point = ray.origin + dir * seen.Value;
            normal = seen.Normal;
            return true;
        }

        // ------------------------------------------------------------ one question

        /// <summary>A straight view: from its origin, along its direction, up to its reach.</summary>
        readonly struct Look
        {
            public readonly Vector3 Origin, Direction;
            public readonly float Reach;
            public readonly bool Vertical;

            public Look(Vector3 origin, Vector3 direction, float reach, bool vertical)
            {
                Origin = origin;
                Direction = direction;
                Reach = reach;
                Vertical = vertical;
            }

            public static Look Down(Vector3 p, float above, float below)
            {
                float top = p.y + above, bottom = p.y - below;
                return new Look(new Vector3(p.x, top + Margin, p.z), Vector3.down, top - bottom + 2f * Margin, true);
            }

            public bool Valid =>
                Reach > 0f && Direction.sqrMagnitude > 0.5f && Finite(Origin.x) && Finite(Origin.y) && Finite(Origin.z) && !float.IsNaN(Reach);

            static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        }

        /// <summary>One answer: <see cref="Value"/> is the height of a look down and the distance of a ray.</summary>
        struct Seen
        {
            public bool Hit;
            public float Value;
            public Vector3 Normal;
            public string Source;
        }

        static Look[] looks = new Look[1];
        static Seen[] answers = new Seen[1];
        static readonly List<Renderer> picked = new List<Renderer>();

        static void EnsureLooks(int n)
        {
            if (looks.Length >= n) return;
            looks = new Look[MaxPoints];
            answers = new Seen[MaxPoints];
        }

        // Fills answers[0..n) for looks[0..n): measured from `from` along `axis`, seen along `view`; walls below minNormalY do not count.
        static void Answer(BoundsIndex index, int n, Vector3 from, Vector3 axis, Vector3 view, float minNormalY)
        {
            Array.Clear(answers, 0, n);
            if (DeviceReady() && Draw(index, n, from, axis, view, minNormalY)) return;
            Array.Clear(answers, 0, n);
            for (int i = 0; i < n; i++)
            {
                if (!looks[i].Valid) continue;
                Candidates(index, looks[i]);
                if (Walk(index, looks[i], minNormalY, out float t, out var normal)) Keep(i, looks[i], from, axis, t, normal, SourceMesh);
                else if (Collide(index, looks[i], minNormalY, out t, out normal)) Keep(i, looks[i], from, axis, t, normal, SourceCollider);
            }
        }

        static void Keep(int i, in Look l, Vector3 from, Vector3 axis, float t, Vector3 normal, string source) =>
            answers[i] = new Seen { Hit = true, Value = Vector3.Dot(l.Origin + l.Direction * t - from, axis), Normal = normal, Source = source };

        static float Candidates(BoundsIndex index, in Look l)
        {
            long start = Stopwatch.GetTimestamp();
            float far = index.Under(l, picked);
            LastScanMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            LastCandidates += picked.Count;
            return far;
        }

        // ------------------------------------------------------------ the device

        static Material material;
        static CommandBuffer commands;
        static bool warned;
        static readonly int FromId = Shader.PropertyToID("_MapSurfaceFrom"), AxisId = Shader.PropertyToID("_MapSurfaceAxis"), ViewId = Shader.PropertyToID("_MapSurfaceView");
        static readonly List<Material> slots = new List<Material>();

        static bool DeviceReady()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsAsyncGPUReadback) return false;
            if (material) return true;
            var shader = Shader.Find(ShaderName);
            if (!shader || !shader.isSupported)
            {
                Warn("the map query's shader is not available; it reads meshes and colliders");
                return false;
            }
            material = new Material(shader) { name = "map surface", hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        static void Warn(string text)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[MapSurface] " + text);
        }

        static bool Draw(BoundsIndex index, int n, Vector3 from, Vector3 axis, Vector3 view, float minNormalY)
        {
            int width = n <= 1 ? 1 : n <= 32 ? 32 : MaxPoints;
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, 1, ColorFormat, DepthFormat) { msaaSamples = 1, useMipMap = false, sRGB = false });
            var cmd = commands ?? (commands = new CommandBuffer { name = "map surface" });
            var active = RenderTexture.active;
            try
            {
                material.SetVector(FromId, from);
                material.SetVector(AxisId, axis);
                material.SetVector(ViewId, new Vector4(view.x, view.y, view.z, minNormalY));
                cmd.Clear();
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(true, true, Color.clear);
                int draws = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!looks[i].Valid) continue;
                    float far = Candidates(index, looks[i]);
                    if (picked.Count == 0) continue;
                    cmd.SetViewport(new Rect(i, 0, 1, 1));
                    cmd.SetViewProjectionMatrices(ViewOf(looks[i].Origin, looks[i].Direction), Matrix4x4.Ortho(-Window, Window, -Window, Window, 0f, far + Margin));
                    foreach (var r in picked) draws += DrawRenderer(cmd, r);
                }
                LastDraws += draws;
                if (draws == 0) return true;
                Graphics.ExecuteCommandBuffer(cmd);
                var request = AsyncGPUReadback.Request(rt, 0, 0, n, 0, 1, 0, 1, ColorFormat);
                request.WaitForCompletion();
                Waits++;
                if (request.hasError)
                {
                    Warn("a read-back of the map query failed; it reads meshes and colliders");
                    return false;
                }
                var data = request.GetData<float>();
                for (int i = 0; i < n; i++)
                {
                    var normal = new Vector3(data[i * 4 + 1], data[i * 4 + 2], data[i * 4 + 3]);
                    if (!(normal.sqrMagnitude > 0.5f)) continue;
                    answers[i] = new Seen { Hit = true, Value = data[i * 4], Normal = normal.normalized, Source = SourceGpu };
                }
                return true;
            }
            finally
            {
                cmd.Clear();
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static int DrawRenderer(CommandBuffer cmd, Renderer r)
        {
            r.GetSharedMaterials(slots);
            int n = slots.Count;
            var mesh = MeshOf(r);
            if (mesh && !r.isPartOfStaticBatch) n = Math.Min(n, mesh.subMeshCount);
            for (int i = 0; i < n; i++) cmd.DrawRenderer(r, material, i, 0);
            return n;
        }

        // A view whose third axis looks back along the direction, as a camera's does.
        static Matrix4x4 ViewOf(Vector3 eye, Vector3 direction)
        {
            var helper = Mathf.Abs(direction.y) > 0.9f ? Vector3.forward : Vector3.up;
            var right = Vector3.Cross(direction, helper).normalized;
            var up = Vector3.Cross(right, direction);
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, eye)));
            m.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, eye)));
            m.SetRow(2, new Vector4(-direction.x, -direction.y, -direction.z, Vector3.Dot(direction, eye)));
            return m;
        }

        static Mesh MeshOf(Renderer r) =>
            r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;

        // ------------------------------------------------------------ no device

        // The nearest triangle of the picked renderers' readable meshes. A skinned mesh is not read, its vertices are not where it is drawn; nor a static batch's, which
        // many renderers share.
        static bool Walk(BoundsIndex index, in Look l, float minNormalY, out float best, out Vector3 bestNormal)
        {
            bool any = false;
            best = float.MaxValue;
            bestNormal = default;
            foreach (var r in picked)
            {
                if (r is SkinnedMeshRenderer || r.isPartOfStaticBatch) continue;
                var mesh = MeshOf(r);
                if (!mesh || !mesh.isReadable) continue;
                var tris = index.Read(mesh);
                var toWorld = r.transform.localToWorldMatrix;
                var toLocal = toWorld.inverse;
                Vector3 origin = toLocal.MultiplyPoint3x4(l.Origin), direction = toLocal.MultiplyVector(l.Direction);
                var v = tris.Vertices;
                foreach (var t in tris.Submeshes)
                {
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]], e1 = v[t[i + 1]] - a, e2 = v[t[i + 2]] - a;
                        var p = Vector3.Cross(direction, e2);
                        float det = Vector3.Dot(e1, p);
                        if (det == 0f) continue;
                        float inv = 1f / det;
                        var q = origin - a;
                        float u = Vector3.Dot(q, p) * inv;
                        if (u < 0f || u > 1f) continue;
                        var c = Vector3.Cross(q, e1);
                        float w = Vector3.Dot(direction, c) * inv;
                        if (w < 0f || u + w > 1f) continue;
                        float d = Vector3.Dot(e2, c) * inv;
                        if (d < 0f || d > l.Reach || d >= best) continue;
                        var normal = Vector3.Cross(toWorld.MultiplyVector(e1), toWorld.MultiplyVector(e2)).normalized;
                        if (Mathf.Abs(normal.y) < minNormalY) continue;
                        if (Vector3.Dot(normal, l.Direction) > 0f) normal = -normal;
                        best = d;
                        bestNormal = normal;
                        any = true;
                    }
                }
            }
            return any;
        }

        static readonly RaycastHit[] rayHits = new RaycastHit[128];

        // The nearest collider under the map's roots: a character's or an item's is not the map. A terrain's is left to the terrain, which answers for itself.
        static bool Collide(BoundsIndex index, in Look l, float minNormalY, out float best, out Vector3 bestNormal)
        {
            best = float.MaxValue;
            bestNormal = default;
            Physics.SyncTransforms();
            int n = Physics.RaycastNonAlloc(l.Origin, l.Direction, rayHits, l.Reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var h = rayHits[i];
                if (h.distance >= best || h.collider is TerrainCollider || !index.Holds(h.collider.transform) || Mathf.Abs(h.normal.y) < minNormalY) continue;
                best = h.distance;
                bestNormal = h.normal;
                any = true;
            }
            return any;
        }

        // ------------------------------------------------------------ terrains

        static bool Draws(Terrain t) => t && t.enabled && t.drawHeightmap && t.gameObject.activeInHierarchy && t.terrainData;

        // The height of a terrain's surface over (x, z), its normal; false outside it or over a hole.
        static bool TerrainAt(Terrain t, float x, float z, out float y, out Vector3 normal)
        {
            y = 0f;
            normal = default;
            var data = t.terrainData;
            Vector3 o = t.GetPosition(), size = data.size;
            float u = (x - o.x) / size.x, v = (z - o.z) / size.z;
            if (!(u >= 0f && u <= 1f && v >= 0f && v <= 1f)) return false;
            int holes = data.holesResolution;
            if (holes > 0 && data.IsHole(Math.Min(holes - 1, (int)(u * holes)), Math.Min(holes - 1, (int)(v * holes)))) return false;
            y = o.y + t.SampleHeight(new Vector3(x, o.y, z));
            normal = data.GetInterpolatedNormal(u, v);
            return true;
        }

        static bool TerrainBelow(MapStructure s, in Look l, out float height, out Vector3 normal)
        {
            bool any = false;
            height = float.MinValue;
            normal = default;
            float top = l.Origin.y, bottom = top - l.Reach;
            foreach (var t in s.Terrains)
            {
                if (!Draws(t) || !TerrainAt(t, l.Origin.x, l.Origin.z, out float y, out var n)) continue;
                if (y > top || y < bottom || y <= height || Mathf.Abs(n.y) < MinNormalY) continue;
                height = y;
                normal = n;
                any = true;
            }
            return any;
        }

        // The nearest crossing of a terrain's surface along a ray: steps of one height sample, then halving.
        static bool TerrainAlong(MapStructure s, in Look l, out float distance, out Vector3 normal)
        {
            bool any = false;
            distance = float.MaxValue;
            normal = default;
            foreach (var t in s.Terrains)
            {
                if (!Draws(t)) continue;
                var data = t.terrainData;
                var box = new Bounds(t.GetPosition() + data.size * 0.5f, data.size);
                if (!box.IntersectRay(new Ray(l.Origin, l.Direction), out float enter)) continue;
                float step = Mathf.Max(0.01f, Mathf.Min(data.heightmapScale.x, data.heightmapScale.z)), end = Mathf.Min(l.Reach, distance);
                float last = Mathf.Max(0f, enter);
                bool wasAbove = true;
                for (int i = 0; i < 8192 && last <= end; i++, last += step)
                {
                    var p = l.Origin + l.Direction * last;
                    if (!TerrainAt(t, p.x, p.z, out float y, out var n)) continue;
                    bool above = p.y > y;
                    if (above) { wasAbove = true; continue; }
                    if (!wasAbove) continue;
                    float lo = Mathf.Max(0f, last - step), hi = last;
                    for (int k = 0; k < 12; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        var q = l.Origin + l.Direction * mid;
                        if (TerrainAt(t, q.x, q.z, out float ym, out n) && q.y <= ym) hi = mid; else lo = mid;
                    }
                    if (hi < distance)
                    {
                        distance = hi;
                        normal = Vector3.Dot(n, l.Direction) > 0f ? -n : n;
                        any = true;
                    }
                    break;
                }
            }
            return any;
        }

        // ------------------------------------------------------------ the bounds of one map

        /// <summary>A map's renderers that can be a surface and their world boxes. On or off is read at each question, so a map's option blocks need no event.</summary>
        sealed class BoundsIndex
        {
            const float Pad = 1e-3f;
            static readonly ConditionalWeakTable<MapStructure, BoundsIndex> all = new ConditionalWeakTable<MapStructure, BoundsIndex>();

            readonly Renderer[] renderers;
            readonly float[] box;       // min x, y, z, max x, y, z per renderer
            readonly Vector3 position;
            readonly Quaternion rotation;
            readonly Vector3 scale;
            readonly HashSet<Transform> roots = new HashSet<Transform>();
            readonly Dictionary<Mesh, Triangles> meshes = new Dictionary<Mesh, Triangles>();

            public static BoundsIndex Of(MapStructure s)
            {
                PoseOf(s, out var p, out var r, out var k);
                if (all.TryGetValue(s, out var index))
                {
                    if (index.position == p && index.rotation == r && index.scale == k) return index;
                    all.Remove(s);
                }
                index = new BoundsIndex(s, p, r, k);
                all.Add(s, index);
                return index;
            }

            // The roots follow the first one when the map moves.
            static void PoseOf(MapStructure s, out Vector3 position, out Quaternion rotation, out Vector3 scale)
            {
                position = default;
                rotation = Quaternion.identity;
                scale = Vector3.one;
                foreach (var g in s.Roots)
                {
                    if (!g) continue;
                    var t = g.transform;
                    position = t.position;
                    rotation = t.rotation;
                    scale = t.lossyScale;
                    return;
                }
            }

            BoundsIndex(MapStructure s, Vector3 p, Quaternion r, Vector3 k)
            {
                long start = Stopwatch.GetTimestamp();
                position = p;
                rotation = r;
                scale = k;
                var list = new List<Renderer>(s.Renderers.Count);
                foreach (var x in s.Renderers)
                    if (x && !(x is ParticleSystemRenderer || x is TrailRenderer || x is LineRenderer)) list.Add(x);
                renderers = list.ToArray();
                box = new float[renderers.Length * 6];
                for (int i = 0, b = 0; i < renderers.Length; i++, b += 6)
                {
                    var bounds = renderers[i].bounds;       // true also for what is off
                    Vector3 min = bounds.min, max = bounds.max;
                    box[b] = min.x - Pad; box[b + 1] = min.y - Pad; box[b + 2] = min.z - Pad;
                    box[b + 3] = max.x + Pad; box[b + 4] = max.y + Pad; box[b + 5] = max.z + Pad;
                }
                foreach (var g in s.Roots) if (g) roots.Add(g.transform);
                LastIndexMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }

            static bool Draws(Renderer r) =>
                r && r.enabled && !r.forceRenderingOff && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly && r.gameObject.activeInHierarchy;

            /// <summary>Fills <paramref name="into"/> with the renderers that can lie along the look; the distance the view must reach.</summary>
            public float Under(in Look l, List<Renderer> into)
            {
                into.Clear();
                var b = box;
                if (l.Vertical)
                {
                    float x = l.Origin.x, z = l.Origin.z, top = l.Origin.y, bottom = top - l.Reach;
                    for (int i = 0, k = 0; i < renderers.Length; i++, k += 6)
                    {
                        if (x < b[k] || x > b[k + 3] || z < b[k + 2] || z > b[k + 5] || b[k + 1] > top || b[k + 4] < bottom) continue;
                        if (Draws(renderers[i])) into.Add(renderers[i]);
                    }
                    return l.Reach;
                }
                Vector3 o = l.Origin, d = l.Direction;
                float ix = 1f / d.x, iy = 1f / d.y, iz = 1f / d.z, far = 0f;
                for (int i = 0, k = 0; i < renderers.Length; i++, k += 6)
                {
                    float lo = 0f, hi = l.Reach;
                    if (!Slab(o.x, ix, b[k], b[k + 3], ref lo, ref hi) || !Slab(o.y, iy, b[k + 1], b[k + 4], ref lo, ref hi) || !Slab(o.z, iz, b[k + 2], b[k + 5], ref lo, ref hi)) continue;
                    if (!Draws(renderers[i])) continue;
                    into.Add(renderers[i]);
                    if (hi > far) far = hi;
                }
                return far;
            }

            // The part of the ray between two planes of a box; a ray along the planes (an infinite reciprocal) stays inside or misses by its origin.
            static bool Slab(float o, float inverse, float min, float max, ref float lo, ref float hi)
            {
                if (float.IsInfinity(inverse)) return o >= min && o <= max;
                float a = (min - o) * inverse, b = (max - o) * inverse;
                if (a > b) { float c = a; a = b; b = c; }
                if (a > lo) lo = a;
                if (b < hi) hi = b;
                return lo <= hi;
            }

            /// <summary>True for an object under one of the map's roots.</summary>
            public bool Holds(Transform t) => roots.Contains(t.root);

            public Triangles Read(Mesh mesh)
            {
                if (!meshes.TryGetValue(mesh, out var t)) meshes[mesh] = t = new Triangles(mesh);
                return t;
            }
        }

        /// <summary>A readable mesh's vertices and its submeshes' triangles.</summary>
        sealed class Triangles
        {
            public readonly Vector3[] Vertices;
            public readonly int[][] Submeshes;

            public Triangles(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Submeshes = new int[mesh.subMeshCount][];
                for (int i = 0; i < Submeshes.Length; i++) Submeshes[i] = mesh.GetTopology(i) == MeshTopology.Triangles ? mesh.GetTriangles(i) : Array.Empty<int>();
            }
        }
    }
}
