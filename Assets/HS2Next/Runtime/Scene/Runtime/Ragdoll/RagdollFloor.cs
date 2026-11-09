// The ground under the ragdolls. The real colliders of a map are not used: many maps have none that holds a body. What stops a body is the room's box and, above its floor, a patch of
// the map's heights, read from the map query, so it works on every map, also one whose meshes cannot be read.
//   the floor   by the scene's floor mode. map: the lowest answer in a 5 x 5 grid around the character, or the lowest height of a patch when that is lower; no answer at all falls to
//               feet, with one finding per map. feet: the body's lowest collider less a margin, at the build. fixed: the block's height. The offset comes on top
//   a patch     one per body: a mesh collider over 65 x 65 points half a unit apart, asked in one call. A point with no answer lies 2 units under the lowest answer and a quad with no
//               answer at any corner is left out, so a patch can end. Only heights become ground: a table is a step in the patch, a wall is nothing
//   re-centre   a pelvis more than 10 units (in x, z) from its patch's centre gets a new patch around it, at most once per half second of scene time. The rule reads the body and the
//               scene clock alone, so a step repeats. The grid stays on multiples of a cell: the points two patches share have the same height, so a body feels no change
//   a new map   every patch is read anew and a map floor is read again. A map moved to another pose counts as a new map, on the first frame it holds still: a drag asks the map once,
//               not in every frame. The rule counts frames, so a step repeats
// HeightAt is what effects land on: bilinear in a patch, else the room's floor.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HS2Next.Api.Unity;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class RagdollWorld
    {
        /// <summary>The ground of this world: patches, the height under a point.</summary>
        public RagdollFloor Floor { get; private set; }

        partial void AttachGround()
        {
            Floor = new RagdollFloor(this);
            Ground = Floor;
        }
    }

    /// <summary>The map's heights around one body, as a mesh collider.</summary>
    public sealed class RagdollPatch
    {
        /// <summary>Points along a side.</summary>
        public const int Side = 65;
        /// <summary>Units between two points.</summary>
        public const float Cell = 0.5f;
        /// <summary>How far under the lowest answer a point with no answer lies.</summary>
        public const float NoAnswerDrop = 2f;
        const float Half = (Side - 1) * Cell * 0.5f;

        readonly float[] heights = new float[Side * Side];
        readonly bool[] answered = new bool[Side * Side];
        float originX, originZ;
        GameObject go;
        MeshCollider col;
        Mesh mesh;

        internal RagdollPatch(RagdollBody body) { Body = body; }

        public RagdollBody Body { get; }
        /// <summary>The grid's centre in x, z: on a multiple of a cell.</summary>
        public Vector2 Center { get; private set; }
        /// <summary>The height the query starts from.</summary>
        public float Top { get; private set; }
        /// <summary>Points with an answer.</summary>
        public int Found { get; private set; }
        /// <summary>Quads with a corner answered: what the collider has, two triangles each.</summary>
        public int Quads { get; private set; }
        /// <summary>The lowest answer; positive infinity with none.</summary>
        public float Lowest { get; private set; } = float.PositiveInfinity;
        /// <summary>How many times it was read.</summary>
        public int Reads { get; private set; }
        /// <summary>Seconds of the scene since the last read.</summary>
        public float Age { get; internal set; }
        /// <summary>Answers of each source in the last read.</summary>
        public int FromGpu { get; private set; }
        public int FromMesh { get; private set; }
        public int FromTerrain { get; private set; }
        public int FromCollider { get; private set; }
        /// <summary>Milliseconds of the last read: the query, then the mesh and its collider.</summary>
        public double QueryMs { get; private set; }
        public double MeshMs { get; private set; }
        /// <summary>The collider; null before the first answer.</summary>
        public MeshCollider Collider => col;

        /// <summary>The world place of point (<paramref name="row"/> along z, <paramref name="column"/> along x) of the grid, at the height it has in the mesh.</summary>
        public Vector3 Point(int row, int column) => new Vector3(originX + column * Cell, heights[row * Side + column], originZ + row * Cell);
        /// <summary>True when the map answered at this point of the grid.</summary>
        public bool Answered(int row, int column) => answered[row * Side + column];

        /// <summary>The grid around (<paramref name="x"/>, <paramref name="z"/>) as points for the query.</summary>
        internal void Lay(float x, float z, float top, Vector3[] points)
        {
            float cx = Mathf.Round(x / Cell) * Cell, cz = Mathf.Round(z / Cell) * Cell;
            Center = new Vector2(cx, cz);
            Top = top;
            originX = cx - Half;
            originZ = cz - Half;
            for (int i = 0; i < Side; i++)
                for (int k = 0; k < Side; k++)
                    points[i * Side + k] = new Vector3(originX + k * Cell, top, originZ + i * Cell);
        }

        /// <summary>The answers into heights, the heights into the mesh. <paramref name="verts"/> and <paramref name="tris"/> are shared buffers.</summary>
        internal void Fill(RagdollWorld world, MapSurfaceHit[] hits, double queryMs, Vector3[] verts, int[] tris)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            QueryMs = queryMs;
            Reads++;
            Age = 0f;
            int found = 0, gpu = 0, tri = 0, terrain = 0, collider = 0;
            float low = float.PositiveInfinity;
            for (int i = 0; i < heights.Length; i++)
            {
                string s = hits[i].Source;
                answered[i] = s != null;
                if (s == null) continue;
                found++;
                low = Mathf.Min(low, hits[i].Height);
                if (s == MapSurface.SourceGpu) gpu++;
                else if (s == MapSurface.SourceMesh) tri++;
                else if (s == MapSurface.SourceTerrain) terrain++;
                else if (s == MapSurface.SourceCollider) collider++;
            }
            Found = found;
            Lowest = low;
            FromGpu = gpu;
            FromMesh = tri;
            FromTerrain = terrain;
            FromCollider = collider;
            for (int i = 0; i < heights.Length; i++) heights[i] = answered[i] ? hits[i].Height : low - NoAnswerDrop;

            int n = 0;
            for (int i = 0; i + 1 < Side; i++)
                for (int k = 0; k + 1 < Side; k++)
                {
                    int a = i * Side + k, b = a + 1, d = a + Side, e = d + 1;
                    if (!answered[a] && !answered[b] && !answered[d] && !answered[e]) continue;
                    tris[n++] = a; tris[n++] = d; tris[n++] = b;
                    tris[n++] = b; tris[n++] = d; tris[n++] = e;
                }
            Quads = n / 6;
            if (n == 0)
            {
                if (col) col.enabled = false;
                MeshMs = Since(t0);
                return;
            }

            for (int i = 0; i < Side; i++)
                for (int k = 0; k < Side; k++)
                    verts[i * Side + k] = new Vector3((k * Cell) - Half, heights[i * Side + k], (i * Cell) - Half);
            if (!go)
            {
                go = world.NewRoot("Patch");
                col = go.AddComponent<MeshCollider>();
                col.sharedMaterial = world.Material;
                mesh = new Mesh { name = "Patch", indexFormat = IndexFormat.UInt32 };
            }
            col.sharedMesh = null;
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetIndices(tris, 0, n, MeshTopology.Triangles, 0, true);
            go.transform.position = new Vector3(Center.x, 0f, Center.y);
            col.sharedMesh = mesh;
            col.enabled = true;
            MeshMs = Since(t0);
        }

        static double Since(long t0) => (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>The height of the patch at a point, bilinear in its quad; false outside the grid and in a quad that was left out.</summary>
        public bool TryHeight(float x, float z, out float y)
        {
            y = 0f;
            if (Found == 0) return false;
            float fx = (x - originX) / Cell, fz = (z - originZ) / Cell;
            int k = Mathf.FloorToInt(fx), i = Mathf.FloorToInt(fz);
            if (k == Side - 1 && fx == k) k--;
            if (i == Side - 1 && fz == i) i--;
            if (k < 0 || i < 0 || k >= Side - 1 || i >= Side - 1) return false;
            int a = i * Side + k, b = a + 1, d = a + Side, e = d + 1;
            if (!answered[a] && !answered[b] && !answered[d] && !answered[e]) return false;
            float u = fx - k, v = fz - i;
            y = Mathf.Lerp(Mathf.Lerp(heights[a], heights[b], u), Mathf.Lerp(heights[d], heights[e], u), v);
            return true;
        }

        internal void Destroy()
        {
            if (go) Object.DestroyImmediate(go);
            if (mesh) Object.DestroyImmediate(mesh);
            go = null;
            col = null;
            mesh = null;
        }
    }

    public sealed class RagdollFloor : IRagdollGround
    {
        /// <summary>A pelvis further than this from its patch's centre, in x and z, gets a new patch.</summary>
        public const float RecentreDistance = 10f;
        /// <summary>Seconds of the scene a patch stays at least.</summary>
        public const float RecentreEvery = 0.5f;
        /// <summary>The query starts this far over the character's root and ends this far under its start.</summary>
        public const float PatchTop = 8f, PatchBelow = 60f;
        /// <summary>The grid of the floor: its points on a side, how far it reaches to each side, and the heights it reads around the character.</summary>
        public const int GridSide = 5;
        public const float GridReach = 2f, GridAbove = 6f, GridBelow = 20f;
        // the floor of mode feet lies this far under the body's lowest collider
        const float FeetMargin = 0.1f;

        /// <summary>What the floor asks about the map. A test's door: a stand-in map for a world with none.</summary>
        public static Func<SceneRuntime, IMapQuery> QueryOf = rt => MapSurface.Of(rt);

        // the map a runtime last reported as giving no ground: a world goes with its last body, a map stays
        sealed class Reported { public string Map; }
        static readonly ConditionalWeakTable<SceneRuntime, Reported> reported = new ConditionalWeakTable<SceneRuntime, Reported>();

        readonly RagdollWorld world;
        readonly SceneRuntime rt;
        readonly IMapQuery query;
        readonly MapRealization map;
        readonly List<RagdollPatch> patches = new List<RagdollPatch>();
        readonly Vector3[] grid = new Vector3[GridSide * GridSide];
        readonly MapSurfaceHit[] gridHits = new MapSurfaceHit[GridSide * GridSide];
        Vector3[] points, verts;
        MapSurfaceHit[] hits;
        int[] tris;
        MapStructure seen;
        // the pose last read, and the pose of the last frame
        Vector3 seenPos, seenRot, lastPos, lastRot;

        internal RagdollFloor(RagdollWorld world)
        {
            this.world = world;
            rt = world.Runtime;
            query = QueryOf(rt);
            map = rt.GetPart<MapRealization>();
            seen = StructureNow;
            if (map == null) return;
            seenPos = lastPos = map.Pos;
            seenRot = lastRot = map.Rot;
        }

        /// <summary>The patches, in the order the bodies came.</summary>
        public IReadOnlyList<RagdollPatch> Patches => patches;
        /// <summary>How many patches were read anew around a body that moved.</summary>
        public int Recentres { get; private set; }
        /// <summary>How many times a new map was taken up.</summary>
        public int MapChanges { get; private set; }

        /// <summary>The patch of a body, or null.</summary>
        public RagdollPatch PatchOf(RagdollBody body)
        {
            for (int i = 0; i < patches.Count; i++) if (patches[i].Body == body) return patches[i];
            return null;
        }

        /// <summary>The ground's height under a point: bilinear in a patch, else the room's floor. A patch's points with no answer lie under the floor, so the floor wins there.</summary>
        public float HeightAt(float x, float z)
        {
            float floor = world.FloorY;
            for (int i = 0; i < patches.Count; i++)
                if (patches[i].TryHeight(x, z, out float y)) return Mathf.Max(y, floor);
            return floor;
        }

        string Mode => rt.Model.Ragdoll?.Floor?.AppliedMode ?? RagdollFloorValues.ModeMap;
        float Offset => rt.Model.Ragdoll?.Floor?.Offset ?? 0f;
        MapStructure StructureNow => map != null && map.State == MapState.Loaded ? map.Structure : null;

        static Vector3 RootOf(RagdollBody b)
        {
            var r = b.Realization;
            if (r != null && r.Root) return r.Root.transform.position;
            return r != null && r.HasHolder ? r.Holder.position : b.Parts[0].Tr.position;
        }

        // ------------------------------------------------------------ IRagdollGround

        public bool FloorFor(RagdollBody body, out float height, out string source)
        {
            source = RagdollFloorSources.Map;
            height = 0f;
            float low = float.PositiveInfinity;
            if (query.Loaded)
            {
                low = GridLowest(RootOf(body));
                if (PatchOf(body) == null) Make(body);
                for (int i = 0; i < patches.Count; i++) low = Mathf.Min(low, patches[i].Lowest);
            }
            if (float.IsInfinity(low))
            {
                source = RagdollFloorSources.Feet;
                NoGround();
                return false;
            }
            height = low;
            return true;
        }

        public void BodyBuilt(RagdollBody body)
        {
            if (Mode == RagdollFloorValues.ModeMap && query.Loaded && PatchOf(body) == null) Make(body);
        }

        public void BodyGone(RagdollBody body)
        {
            var p = PatchOf(body);
            if (p == null) return;
            patches.Remove(p);
            p.Destroy();
        }

        public void BeforeSteps(float dt)
        {
            WatchMap();
            if (Mode != RagdollFloorValues.ModeMap)
            {
                if (patches.Count > 0) DropAll();
                return;
            }
            var bodies = world.Bodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b.Buried) continue;
                var p = PatchOf(b);
                if (p == null)
                {
                    if (!query.Loaded) continue;
                    Lower(Make(b));
                    continue;
                }
                p.Age += dt;
                var at = b.Parts[0].Tr.position;
                float dx = at.x - p.Center.x, dz = at.z - p.Center.y;
                if (dx * dx + dz * dz <= RecentreDistance * RecentreDistance || p.Age < RecentreEvery) continue;
                Read(p, at.x, at.z);
                Recentres++;
                Lower(p);
            }
        }

        public void BlockChanged(SceneRagdollWorld block) => world.Obstacles?.Sync();

        public void WorldClosing() => DropAll();

        void DropAll()
        {
            for (int i = 0; i < patches.Count; i++) patches[i].Destroy();
            patches.Clear();
        }

        // ------------------------------------------------------------ the map

        void WatchMap()
        {
            if (map == null || map.IsBusy) return;
            var now = StructureNow;
            var pos = map.Pos;
            var rot = map.Rot;
            bool still = pos.Equals(lastPos) && rot.Equals(lastRot);
            lastPos = pos;
            lastRot = rot;
            bool moved = !pos.Equals(seenPos) || !rot.Equals(seenRot);
            if (ReferenceEquals(now, seen) && !(moved && still)) return;
            seen = now;
            seenPos = pos;
            seenRot = rot;
            MapChanged();
        }

        /// <summary>Another map stands, or the map stands somewhere else: every patch is read anew where its body is, and a map floor is read again. A floor of the other modes stays.</summary>
        public void MapChanged()
        {
            MapChanges++;
            if (Mode != RagdollFloorValues.ModeMap) return;
            for (int i = 0; i < patches.Count; i++)
            {
                var b = patches[i].Body;
                if (b.Buried) continue;
                var at = b.Parts[0].Tr.position;
                Read(patches[i], at.x, at.z);
            }
            rt.GetPart<RagdollPart>()?.RefloorNow();
        }

        // a new patch can reach lower than the floor stands: the floor follows it down. It never rises here
        void Lower(RagdollPatch p)
        {
            if (float.IsInfinity(p.Lowest) || !world.HasRoom) return;
            float y = p.Lowest + Offset;
            if (y < world.FloorY || world.FloorSource != RagdollFloorSources.Map) world.SetRoom(world.RoomCenter, world.RoomSize, Mathf.Min(y, world.FloorY), RagdollFloorSources.Map);
        }

        void NoGround()
        {
            string key = rt.Model.Settings.Map.Ref ?? "";
            var said = reported.GetOrCreateValue(rt);
            bool listed = false;
            var findings = rt.Findings;
            for (int i = 0; i < findings.Count; i++) listed |= findings[i].Code == RagdollFindings.NoGround;
            if (listed && said.Map == key) return;
            said.Map = key;
            rt.Report(FindingSeverity.Warning, RagdollFindings.NoGround, null, "the map gives no ground here: the floor lies at the character's feet");
        }

        // ------------------------------------------------------------ asking the map

        float GridLowest(Vector3 at)
        {
            float step = 2f * GridReach / (GridSide - 1);
            for (int i = 0; i < GridSide; i++)
                for (int k = 0; k < GridSide; k++)
                    grid[i * GridSide + k] = new Vector3(at.x - GridReach + k * step, at.y, at.z - GridReach + i * step);
            float low = float.PositiveInfinity;
            if (query.Surfaces(grid, gridHits, GridAbove, GridBelow) == 0) return low;
            for (int i = 0; i < gridHits.Length; i++)
                if (gridHits[i].Source != null) low = Mathf.Min(low, gridHits[i].Height);
            return low;
        }

        RagdollPatch Make(RagdollBody body)
        {
            var p = new RagdollPatch(body);
            patches.Add(p);
            var root = RootOf(body);
            Read(p, root.x, root.z);
            return p;
        }

        void Read(RagdollPatch p, float x, float z)
        {
            const int n = RagdollPatch.Side * RagdollPatch.Side;
            if (points == null)
            {
                points = new Vector3[n];
                verts = new Vector3[n];
                hits = new MapSurfaceHit[n];
                tris = new int[(RagdollPatch.Side - 1) * (RagdollPatch.Side - 1) * 6];
            }
            p.Lay(x, z, RootOf(p.Body).y + PatchTop, points);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            query.Surfaces(points, hits, 0f, PatchBelow);
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            p.Fill(world, hits, ms, verts, tris);
        }

        // ------------------------------------------------------------ a room with no body

        /// <summary>A world that has no room yet gets one, at the block's centre or at <paramref name="near"/>: what a prop thrown into a world without a body needs.</summary>
        public void EnsureRoom(Vector3 near)
        {
            if (world.HasRoom) return;
            var block = rt.Model.Ragdoll;
            var size = UnityObjects.V(block?.Room?.AppliedSize ?? RagdollRoomValues.DefaultSize);
            var center = block?.Room?.Center is Vec2 c ? new Vector2(c.X, c.Y) : new Vector2(near.x, near.z);
            float y = 0f;
            string source = RagdollFloorSources.Feet;
            string mode = Mode;
            if (mode == RagdollFloorValues.ModeFixed)
            {
                y = block.Floor.Height;
                source = RagdollFloorSources.Fixed;
            }
            else if (mode == RagdollFloorValues.ModeMap)
            {
                float low = query.Loaded ? GridLowest(new Vector3(center.x, near.y, center.y)) : float.PositiveInfinity;
                if (float.IsInfinity(low)) NoGround();
                else
                {
                    y = low;
                    source = RagdollFloorSources.Map;
                }
            }
            world.SetRoom(center, size, y + Offset, source);
        }
    }
}
