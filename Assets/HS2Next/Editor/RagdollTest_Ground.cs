// RagdollTest, the ground, the props and the obstacles (the ground and the props).
//   headless, a part of RagdollTest.Run
//     RL0   the patch and the floor on a stand-in map: heights, quads left out, the height for effects, the re-centre, a new map, a map that gives nothing
//     RL7   an item as an obstacle: a readable mesh, a box, follows its item
//     RP15  props: the arithmetic, the caps, the lifetime, a hit's status, the knock hit, a grab, a world with no body
//   play mode, a part of RagdollTest.RunPlay (a map loads only there; the query's source is the device's)
//     RL9   no map: feet, one finding
//     RL1   the patch against the query on a real map, as a collider
//     RL2   floor mode map and the offset
//     RL3   floor modes feet and fixed
//     RL5   the re-centre of a thrown body
//     RL6   the room stops a body; a floor under the furniture at the origin
//     RL8   a map with no readable mesh answers through the device
//     RB8   a map change
//     RB8m  a map moved by map.set: the patch and the floor follow it
//     RP15d a prop shows in a capture: images in memory only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class RagdollTest
{
    // a flat map that answers on every point of a patch, one that stands higher, the two with furniture at the origin, one with no readable mesh
    const string GFlatMap = "illusion.hs2#26", GHighMap = "illusion.hs2#17", GTableMap = "illusion.hs2#27", GTableMod = "2155x.map.phakikoroom#0", GNoMeshMap = "2155x.map.hangar#0";

    static void Headless_G_Ground()
    {
        Group("RL0", "the patch and the floor on a stand-in map", RL0);
        Group("RL7", "items as obstacles", RL7);
        Group("RP15", "props", RP15);
    }

    // ------------------------------------------------------------ a stand-in map

    sealed class StandInMap : IMapQuery
    {
        /// <summary>The surface under a point; NaN for none.</summary>
        public Func<float, float, float> HeightOf;
        public int Calls;
        public bool Loaded => true;
        public IReadOnlyList<GameObject> Roots => Array.Empty<GameObject>();

        public bool Surface(Vector3 point, out MapSurfaceHit hit, float above = 0f, float below = 60f)
        {
            var hits = new MapSurfaceHit[1];
            int n = Surfaces(new[] { point }, hits, above, below);
            hit = hits[0];
            return n > 0;
        }

        public int Surfaces(IReadOnlyList<Vector3> points, MapSurfaceHit[] hits, float above = 0f, float below = 60f)
        {
            Calls++;
            int n = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                float y = HeightOf(p.x, p.z);
                bool ok = !float.IsNaN(y) && y <= p.y + above && y >= p.y - below;
                hits[i] = ok ? new MapSurfaceHit { Height = y, Normal = Vector3.up, Source = MapSurface.SourceMesh } : default;
                if (ok) n++;
            }
            return n;
        }
    }

    static void SetAllVelocities(RagdollBody b, Vector3 v)
    {
        foreach (var p in b.Parts)
        {
            p.Rb.WakeUp();
            p.Rb.linearVelocity = v;
        }
    }

    static int NoGroundCount(SceneRuntime rt) => rt.Findings.Where(f => f.Code == RagdollFindings.NoGround).Sum(f => f.Count);

    static Vector3 Pelvis(RagdollBody b) => b.Parts[0].Tr.position;

    static float Flat(Vector3 a, Vector2 c) => new Vector2(a.x - c.x, a.z - c.y).magnitude;

    /// <summary>The re-centre under a body thrown along <paramref name="dir"/> with the clock paused, in steps of a frame: at rest none, moving none within 10 units and at most one per half second.</summary>
    static void Thrown(SceneRuntime rt, RagdollBody b, Vector3 velocity, string where)
    {
        var f = b.World.Floor;
        var p = f.PatchOf(b);
        if (!Check(where + ": the body has a patch", p != null)) return;
        int reads = p.Reads;
        rt.Step(30, Dt);
        Check(where + ": at rest no patch is read", p.Reads == reads, p.Reads + " reads against " + reads);
        var start = Pelvis(b);
        SetAllVelocities(b, velocity);
        double t = 0, last = double.NegativeInfinity, leastGap = double.PositiveInfinity, leastDistance = double.PositiveInfinity, far = 0;
        int re = 0, near = 0, compared = 0;
        double worstStep = 0;
        for (int i = 0; i < 300; i++)
        {
            var at = Pelvis(b);
            var centre = p.Center;
            int before = p.Reads;
            bool had = p.TryHeight(at.x, at.z, out float under);
            rt.Step(1, Dt);
            t += Dt;
            far = Math.Max(far, Flat(Pelvis(b), new Vector2(start.x, start.z)));
            if (p.Reads == before) continue;
            re++;
            double d = Flat(at, centre);
            leastDistance = Math.Min(leastDistance, d);
            if (d <= RagdollFloor.RecentreDistance) near++;
            if (last > double.NegativeInfinity) leastGap = Math.Min(leastGap, t - last);
            last = t;
            // no jump: the ground under the pelvis has the height it had
            if (!had || !p.TryHeight(at.x, at.z, out float then)) continue;
            compared++;
            worstStep = Math.Max(worstStep, Math.Abs(then - under));
        }
        Check(where + ": Present: the body went at least 20 units", far >= 20.0, far.ToString("F2"));
        Check(where + ": it was re-centred", re >= 1, re + " reads in " + t.ToString("F1") + " s of the scene");
        Check(where + ": none within 10 units", near == 0, "least distance at a read " + leastDistance.ToString("F2"));
        Check(where + ": at most one per half second", re < 2 || leastGap >= RagdollFloor.RecentreEvery - 1e-4, "least gap " + leastGap.ToString("F3") + " s");
        Check(where + ": no jump: the ground under the pelvis keeps its height through a read", worstStep < 1e-3 && compared >= 1, $"largest change {worstStep:G4} at {compared} reads");
        Measure($"RL5 {where}: {re} reads, the body went {far:F1} units, least gap {leastGap:F3} s, least distance {leastDistance:F2}, query {p.QueryMs:F2} ms, mesh {p.MeshMs:F2} ms");
    }

    // ------------------------------------------------------------ RL0

    static void RL0()
    {
        var door = RagdollFloor.QueryOf;
        // a floor at 1, a pit at 0.25 at one end, a table at 3.5, nothing past x = 8
        float Ground(float x, float z) => x > 8f ? float.NaN : x >= 5f && x <= 7f && Mathf.Abs(z) <= 3f ? 3.5f : x < -14f ? 0.25f : 1f;
        var map = new StandInMap { HeightOf = Ground };
        RagdollFloor.QueryOf = _ => map;
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "RL0");
            Idle(h, k);
            Place(h, k, new Vector3(0f, 5f, 0f));
            h.Frames(2);
            Preset(h, k, RagdollPreset.Limp);
            var body = Body(h, k, "RL0");
            if (!Check("a body", body != null)) return;
            var w = h.Part.World;
            var f = w.Floor;
            Check("the world's ground is the floor", ReferenceEquals(w.Ground, f));
            var p = f.PatchOf(body);
            if (!Check("the body has a patch, read once", p != null && p.Reads == 1 && f.Patches.Count == 1, (p?.Reads ?? -1).ToString())) return;
            Check("around the character's root", p.Center == Vector2.zero, p.Center.ToString());
            Check("the query starts 8 over the root", Mathf.Abs(p.Top - 13f) < 1e-3f, p.Top.ToString("F3"));

            const int S = RagdollPatch.Side;
            int found = 0, wrong = 0, wrongFill = 0;
            for (int i = 0; i < S; i++)
                for (int c = 0; c < S; c++)
                {
                    var at = p.Point(i, c);
                    float want = Ground(at.x, at.z);
                    bool has = !float.IsNaN(want);
                    if (has) found++;
                    if (has != p.Answered(i, c) || (has && at.y != want)) wrong++;
                    if (!has && at.y != 0.25f - RagdollPatch.NoAnswerDrop) wrongFill++;
                }
            Check("every vertex is the map's answer", wrong == 0 && found == p.Found && found > 0, $"{wrong} wrong of {p.Found} answers, {S * S} points");
            Check("a point with no answer lies 2 under the lowest answer", wrongFill == 0 && found < S * S, $"{wrongFill} wrong of {S * S - found}");
            int quads = 0;
            for (int i = 0; i + 1 < S; i++)
                for (int c = 0; c + 1 < S; c++)
                    if (p.Answered(i, c) || p.Answered(i, c + 1) || p.Answered(i + 1, c) || p.Answered(i + 1, c + 1)) quads++;
            var mesh = p.Collider.sharedMesh;
            Check("a quad with no answer at any corner is left out", quads == p.Quads && mesh.triangles.Length == quads * 6 && quads < (S - 1) * (S - 1), $"{p.Quads} quads, {mesh.triangles.Length / 3} triangles");
            Check("one mesh with 32-bit indices, not convex, the world's material, in the world's scene",
                mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 && !p.Collider.convex && p.Collider.sharedMaterial == w.Material && p.Collider.gameObject.scene == w.Scene);
            Check("the room's floor is the lowest height of the patch, under the grid's 1", Mathf.Abs(w.FloorY - 0.25f) < 1e-5f && w.FloorSource == RagdollFloorSources.Map, w.FloorY + " " + w.FloorSource);

            Check("HeightAt on the table", f.HeightAt(6f, 0.1f) == 3.5f, f.HeightAt(6f, 0.1f).ToString("G6"));
            float edge = f.HeightAt(4.75f, 0.25f);
            Check("HeightAt is bilinear between two heights", Mathf.Abs(edge - 2.25f) < 1e-4f, edge.ToString("G6"));
            float outer = f.HeightAt(8.25f, 0.25f);
            Check("HeightAt over a corner with no answer stays above the floor", outer >= w.FloorY && outer < 1f, outer.ToString("G6"));
            Check("HeightAt where the patch has no quad is the room's floor", f.HeightAt(12f, 0f) == w.FloorY);
            Check("HeightAt outside the patch is the room's floor", f.HeightAt(0f, 40f) == w.FloorY);

            Physics.SyncTransforms();
            bool hit = w.Physics.Raycast(new Vector3(6f, 9f, 0.1f), Vector3.down, out var rh, 20f, ~0, QueryTriggerInteraction.Ignore);
            Check("a ray stops on the table in the world's physics", hit && rh.collider == p.Collider && Mathf.Abs(rh.point.y - 3.5f) < 1e-3f, hit ? rh.collider.name + " " + rh.point.y.ToString("F3") : "no hit");
            hit = w.Physics.Raycast(new Vector3(12f, 9f, 0.1f), Vector3.down, out rh, 20f, ~0, QueryTriggerInteraction.Ignore);
            Check("a ray past the patch's end stops on the room's floor", hit && rh.collider != p.Collider && Mathf.Abs(rh.point.y - w.FloorY) < 1e-3f, hit ? rh.collider.name + " " + rh.point.y.ToString("F3") : "no hit");

            float top = body.Lowest();
            h.Frames(240);
            float lowest = body.Lowest();
            Check("Present: the body started at least 3 above the ground", top >= 4f, top.ToString("F3"));
            Check("a limp drop lands on the patch, over the room's floor", Mathf.Abs(lowest - 1f) < 0.1f, lowest.ToString("F3"));

            h.Rt.SetPaused(true);
            World(h, b => b.Room.Size = new Vec3(120f, 40f, 120f));
            Check("a change of the room keeps the floor and the patch", Mathf.Abs(w.FloorY - 0.25f) < 1e-5f && f.PatchOf(body) == p && w.RoomSize.x == 120f, w.FloorY + " " + w.RoomSize);
            Thrown(h.Rt, body, new Vector3(-70f, 30f, 0f), "stand-in");

            // a new map
            int reads = p.Reads;
            map.HeightOf = (x, z) => 3f;
            f.MapChanged();
            Check("a new map: the patch is read again where the body is", p.Reads == reads + 1 && Flat(Pelvis(body), p.Center) < 1f && p.Lowest == 3f, p.Reads + " " + p.Lowest);
            Check("a new map: the map floor is read again, also upwards", w.FloorY == 3f && w.FloorSource == RagdollFloorSources.Map, w.FloorY + " " + w.FloorSource);

            // a map that gives nothing
            Check("no finding so far", NoGroundCount(h.Rt) == 0, NoGroundCount(h.Rt).ToString());
            map.HeightOf = (x, z) => float.NaN;
            f.MapChanged();
            Check("a map that gives nothing: feet", w.FloorSource == RagdollFloorSources.Feet && Mathf.Abs(w.FloorY - (body.Lowest() - 0.1f)) < 1e-4f, w.FloorY + " " + w.FloorSource);
            Check("no quad is left of it", p.Found == 0 && p.Quads == 0 && !p.Collider.enabled);
            Check("one finding", NoGroundCount(h.Rt) == 1, NoGroundCount(h.Rt).ToString());
            f.MapChanged();
            Check("the same map says so once", NoGroundCount(h.Rt) == 1, NoGroundCount(h.Rt).ToString());
            map.HeightOf = Ground;
            f.MapChanged();
            Check("answers again: the floor is the map's", w.FloorSource == RagdollFloorSources.Map, w.FloorY + " " + w.FloorSource);

            // the modes
            FixedFloor(h, -3f);
            h.Rt.Step(1, Dt);
            Check("floor mode fixed has no patch", f.Patches.Count == 0 && w.FloorY == -3f && w.FloorSource == RagdollFloorSources.Fixed, f.Patches.Count + " " + w.FloorY);
            Check("the patch's collider is gone from the world's scene", !w.Scene.GetRootGameObjects().Any(g => g.name == "Patch"));
            World(h, b => { b.Floor.Mode = RagdollFloorValues.ModeMap; b.Floor.Height = 0f; });
            h.Rt.Step(1, Dt);
            Check("floor mode map brings the patch back", f.Patches.Count == 1 && w.FloorSource == RagdollFloorSources.Map, f.Patches.Count + " " + w.FloorSource);
            h.Part.Release(k, false, ExecuteOptions.NotRecorded);
            Check("the body gone, its world is gone", h.Part.World == null);

            // a body that comes to lie on the patch's end: the points with no answer lie under the floor, nothing holds it there
            h.Rt.SetPaused(false);
            Place(h, k, new Vector3(8f, 3f, 0f));
            h.Frames(2);
            var edge2 = Body(h, k, "RL0 the end of a patch");
            h.Frames(240);
            var f2 = h.Part.World.Floor;
            var ends = f2.PatchOf(edge2);
            double nearest = double.PositiveInfinity;
            bool finite = true;
            foreach (var x in edge2.Parts)
            {
                var at = x.Rb.worldCenterOfMass;
                finite &= float.IsFinite(at.x + at.y + at.z);
                nearest = Math.Min(nearest, Math.Abs(RagdollBody.LowestOf(x) - f2.HeightAt(at.x, at.z)));
            }
            Check("at the patch's end: Present: quads are left out beside the body", ends != null && ends.Quads < (S - 1) * (S - 1) && !ends.TryHeight(12f, 0f, out _), ends?.Quads.ToString());
            Check("at the patch's end the body lies on the ground, at rest", finite && nearest <= 1.0 && edge2.Lowest() >= h.Part.World.FloorY - 0.05f && edge2.Parts[0].Rb.linearVelocity.magnitude < 3f,
                $"nearest {nearest:F3}, lowest {edge2.Lowest():F3}, the floor {h.Part.World.FloorY:F3}, speed {edge2.Parts[0].Rb.linearVelocity.magnitude:F2}");
            h.Part.Release(k, false, ExecuteOptions.NotRecorded);
        }
        finally
        {
            RagdollFloor.QueryOf = door;
            h.Dispose();
        }
    }

    // ------------------------------------------------------------ RL7

    /// <summary>What an item draws: readable static meshes, and what has no readable mesh; the most vertices of a readable mesh.</summary>
    static (int readable, int other, Bounds bounds) ItemMeshes(ItemRealization r) => ItemMeshes(r, out _);

    static (int readable, int other, Bounds bounds) ItemMeshes(ItemRealization r, out int vertices)
    {
        int readable = 0, other = 0;
        vertices = 0;
        Bounds b = default;
        bool any = false;
        foreach (var x in VisibilityApplier.OwnRenderers(r.Holder))
        {
            if (!x.enabled || !x.gameObject.activeInHierarchy) continue;
            if (x is MeshRenderer)
            {
                var m = x.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                if (!m) continue;
                if (m.isReadable)
                {
                    readable++;
                    vertices = Math.Max(vertices, m.vertexCount);
                }
                else other++;
            }
            else if (x is SkinnedMeshRenderer) other++;
            else continue;
            if (!any) b = x.bounds;
            else b.Encapsulate(x.bounds);
            any = true;
        }
        return (readable, other, b);
    }

    /// <summary>The cube of the base game: one readable mesh of 24 vertices.</summary>
    const string GCube = "illusion.hs2#400", GNoMeshItem = "illusion.hs2#906";

    static string AddItem(Host h, string reference, out Bounds bounds)
    {
        bounds = default;
        string key = Key(h.Call(new { cmd = "item.add", @ref = reference }));
        var r = key == null ? null : h.Rt.FindItem(key);
        if (r == null || !r.Instance) return null;
        var (meshes, other, b) = ItemMeshes(r, out int vertices);
        bounds = b;
        Info($"item {reference}: {meshes} readable meshes of at most {vertices} vertices, {other} renderers with none, size {b.size}");
        return key;
    }

    /// <summary>An item of the base game with one renderer and no readable mesh, or the first such item when that row has changed; null when none is among the first rows.</summary>
    static string ItemWithNoMesh(Host h, out Bounds bounds)
    {
        string known = AddItem(h, GNoMeshItem, out bounds);
        if (known != null)
        {
            var (meshes, other, _) = ItemMeshes(h.Rt.FindItem(known));
            if (meshes == 0 && other > 0) return known;
        }
        int tried = 0;
        foreach (var row in services.Db.Items.Enumerate().OrderBy(x => x.Key.ToString().StartsWith("illusion.hs2#", StringComparison.Ordinal) ? 0 : 1))
        {
            if (tried >= 200) break;
            tried++;
            string key = Key(h.Call(new { cmd = "item.add", @ref = row.Key.ToString() }));
            var r = key == null ? null : h.Rt.FindItem(key);
            if (r == null || !r.Instance) continue;
            var (meshes, other, b) = ItemMeshes(r);
            if (meshes > 0 || other == 0 || b.size.x < 0.3f || b.size.z < 0.3f) continue;
            bounds = b;
            Info($"no readable mesh: item {row.Key} after {tried} tries, {other} renderers, size {b.size}");
            return key;
        }
        Info($"no readable mesh: none among the first {tried} items");
        return null;
    }

    static void RL7()
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "RL7");
            Idle(h, k);
            h.Frames(2);
            World(h, b =>
            {
                b.Floor.Mode = RagdollFloorValues.ModeFixed;
                b.Floor.Height = -30f;
                b.Room.Size = new Vec3(120f, 100f, 120f);
            });
            Preset(h, k, RagdollPreset.Limp);
            ObstacleOf(h, k, AddItem(h, GCube, out var meshBounds), meshBounds, true);
            ObstacleOf(h, k, ItemWithNoMesh(h, out var boxBounds), boxBounds, false);
            var none = RagdollObstacles.Set(h.Rt, k, true, out int count);
            Check("a character is no item here", !none.Ok && count == 0, none.Code + " " + none.Message);
        }
        finally { h.Dispose(); }
    }

    static void ObstacleOf(Host h, string k, string item, Bounds b, bool mesh)
    {
        string kind = mesh ? "mesh" : "box";
        if (!Check(kind + ": an item of that kind exists", item != null)) return;
        // the footprint 16 units wide, under the origin, its top at 0
        float s = 16f / Mathf.Max(0.01f, Mathf.Min(b.size.x, b.size.z));
        h.Session.Set(item, "transform.scale", new Vec3(s, s, s));
        Place(h, item, Vector3.zero);
        h.Frames(1);
        var bounds = ItemMeshes(h.Rt.FindItem(item)).bounds;
        Place(h, item, new Vector3(-bounds.center.x, -bounds.max.y, -bounds.center.z));
        h.Frames(1);
        bounds = ItemMeshes(h.Rt.FindItem(item)).bounds;

        // without it the body falls past
        Place(h, k, new Vector3(0f, 2f, 0f));
        h.Frames(1);
        var body = Body(h, k, kind);
        h.Frames(150);
        float past = Pelvis(body).y;
        h.Part.Release(k, false, ExecuteOptions.NotRecorded);
        Check(kind + ": Present: without the obstacle the body falls past the item", past < bounds.min.y - 1f || past < -20f, $"pelvis {past:F2}, the item {bounds.min.y:F2} to {bounds.max.y:F2}");

        // with it the body stops
        Place(h, k, new Vector3(0f, 2f, 0f));
        h.Frames(1);
        var r = RagdollObstacles.Set(h.Rt, item, true, out int count);
        Check(kind + ": one entry in the block", r.Ok && count == 1 && h.Rt.Model.Ragdoll.Obstacles.Contains(item), r.Message);
        body = Body(h, k, kind);
        h.Frames(150);
        float on = Pelvis(body).y;
        var w = h.Part.World;
        var o = w.Obstacles.Of(item);
        if (!Check(kind + ": it stands in the world's scene", o != null && o.Root && o.Root.scene == w.Scene)) return;
        Check(kind + (mesh ? ": a mesh collider per readable mesh, no box" : ": one box from the bounds, no mesh collider"), mesh ? o.Meshes >= 1 && o.Box == null : o.Meshes == 0 && o.Box != null, o.Meshes + " meshes, box " + (o.Box != null));
        Check(kind + ": with it the body stops on the item", on > past + 5f && on > bounds.min.y, $"pelvis {on:F2} against {past:F2}");
        Check(kind + ": an obstacle is a static collider", o.Root.GetComponentsInChildren<Rigidbody>().Length == 0 && o.Root.GetComponentsInChildren<Collider>().All(c => w.IsStatic(c)));
        if (!mesh)
        {
            var world = o.Box.bounds;
            Check("box: its size is the bounds of what draws", (world.size - bounds.size).magnitude < 0.05f * bounds.size.magnitude, world.size + " against " + bounds.size);
        }

        // it follows its item
        Physics.SyncTransforms();
        int covered = 0;
        for (int i = -2; i <= 2; i++)
            for (int c = -2; c <= 2; c++)
                if (w.Physics.Raycast(new Vector3(i * 2f, bounds.max.y + 40f, c * 2f), Vector3.down, out var row, 90f, ~0, QueryTriggerInteraction.Ignore) && !row.rigidbody && row.point.y > bounds.min.y - 0.1f) covered++;
        Info($"{kind}: {covered} of 25 rays over the middle stop on the item");
        var over = new Vector3(0f, bounds.max.y + 3f, 0f);
        var moved = new Vector3(40f, 0f, 0f);
        Place(h, item, UnityObjects3(h.Rt.HolderOf(item).localPosition) + moved);
        h.Frames(2);
        bool old = w.Physics.Raycast(over, Vector3.down, out var rh, 10f, ~0, QueryTriggerInteraction.Ignore) && !rh.rigidbody;
        bool next = w.Physics.Raycast(over + moved, Vector3.down, out rh, 10f, ~0, QueryTriggerInteraction.Ignore) && !rh.rigidbody;
        Check(kind + ": the collider follows its item", !old && next, $"old place {old}, new place {next}");
        Check(kind + ": a body on it is awake to feel it", !body.Parts[0].Rb.IsSleeping());

        // a deleted item collides no more, and comes back with an undo
        h.Ok(new { cmd = "delete", key = item }, kind + ": delete");
        h.Frames(2);
        Check(kind + ": a deleted item collides no more", !o.Root.activeSelf && h.Rt.Model.Ragdoll.Obstacles.Contains(item));
        h.Ok(new { cmd = "undo" }, kind + ": undo");
        h.Frames(2);
        Check(kind + ": an undo brings it back", o.Root.activeSelf);

        // and it goes
        r = RagdollObstacles.Set(h.Rt, item, false, out count);
        Check(kind + ": off again", r.Ok && count == 0 && w.Obstacles.Of(item) == null && !o.Root, r.Message);
        h.Part.Release(k, false, ExecuteOptions.NotRecorded);
        Check(kind + ": an obstacle holds no world", h.Part.World == null);
    }

    static Vector3 UnityObjects3(Vector3 v) => v;

    // ------------------------------------------------------------ RP15

    static Ray RayAt(Vector3 target, Vector3 from) => new Ray(from, (target - from).normalized);

    static void RP15()
    {
        // where a prop starts
        var min = new Vector3(-10f, 0f, -10f);
        var max = new Vector3(10f, 20f, 10f);
        var enter = RagdollProps.StartOf(new Ray(new Vector3(0f, 5f, -30f), Vector3.forward), min, max, 1.8f);
        Check("a ray from outside starts inside the room's face by the margin", (enter - new Vector3(0f, 5f, -8.2f)).magnitude < 1e-4f, enter.ToString("F3"));
        var inside = RagdollProps.StartOf(new Ray(new Vector3(0f, 5f, 0f), Vector3.forward), min, max, 1.8f);
        Check("a camera inside the room: 2 ahead", (inside - new Vector3(0f, 5f, 2f)).magnitude < 1e-4f, inside.ToString("F3"));
        var wall = RagdollProps.StartOf(new Ray(new Vector3(0f, 5f, 9f), Vector3.forward), min, max, 1.8f);
        Check("a camera by the wall: half the way to it", (wall - new Vector3(0f, 5f, 9.5f)).magnitude < 1e-4f, wall.ToString("F3"));
        var miss = RagdollProps.StartOf(new Ray(new Vector3(0f, 50f, -30f), Vector3.forward), min, max, 1.8f);
        Check("a ray that misses the room: 2 ahead", (miss - new Vector3(0f, 50f, -28f)).magnitude < 1e-4f, miss.ToString("F3"));
        var away = RagdollProps.StartOf(new Ray(new Vector3(0f, 5f, 30f), Vector3.forward), min, max, 1.8f);
        Check("a room behind the ray: 2 ahead", (away - new Vector3(0f, 5f, 32f)).magnitude < 1e-4f, away.ToString("F3"));
        var thin = RagdollProps.StartOf(new Ray(new Vector3(0f, 5f, -30f), Vector3.forward), min, new Vector3(10f, 20f, -9f), 1.8f);
        Check("a thin room: half way through, not past it", (thin - new Vector3(0f, 5f, -9.5f)).magnitude < 1e-4f, thin.ToString("F3"));
        Check("the margin: 1.5 sizes, heavy 2.5", RagdollProps.MarginOf(RagdollWorldValues.PropBall, 2f) == 3f && RagdollProps.MarginOf(RagdollWorldValues.PropBox, 2f) == 3f && RagdollProps.MarginOf(RagdollWorldValues.PropHeavy, 2f) == 5f);

        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "RP15");
            Idle(h, k);
            h.Frames(2);
            FixedFloor(h);
            var body = Body(h, k, "RP15");
            if (!Check("a body", body != null)) return;
            h.Frames(30);
            var w = h.Part.World;
            var props = w.Props;
            Check("no prop at the start, and a world that a body keeps", props.Count == 0 && !props.HoldsWorld);

            // the kinds
            var from = new Vector3(0f, 12f, -8f);
            var ball = props.Throw(new Ray(from, Vector3.back), RagdollWorldValues.PropBall);
            var box = props.Throw(new Ray(from, Vector3.back), RagdollWorldValues.PropBox);
            var heavy = props.Throw(new Ray(from, Vector3.back), RagdollWorldValues.PropHeavy);
            Check("a ball: a sphere of the prop size, the prop mass", ball.Root.GetComponent<SphereCollider>()?.radius == 0.6f && ball.Rb.mass == 2f && ball.Diameter == 1.2f, ball.Diameter + " " + ball.Rb.mass);
            Check("a box: a cube of 1.5 sizes", box.Root.GetComponent<BoxCollider>()?.size == Vector3.one * 1.8f && box.Rb.mass == 2f, box.Diameter.ToString("F3"));
            Check("heavy: a sphere of 2.5 sizes, 10 masses", Mathf.Abs(heavy.Root.GetComponent<SphereCollider>().radius - 1.5f) < 1e-4f && Mathf.Abs(heavy.Rb.mass - 20f) < 1e-4f, heavy.Diameter + " " + heavy.Rb.mass);
            foreach (var p in props.All)
            {
                var rb = p.Rb;
                Check(p.Kind + ": speculative collision, the bodies' solver, no engine gravity, in the world's scene",
                    rb.collisionDetectionMode == CollisionDetectionMode.ContinuousSpeculative && rb.solverIterations == 20 && rb.solverVelocityIterations == 8 && !rb.useGravity && p.Root.scene == w.Scene);
                Check(p.Kind + ": thrown along the ray at the prop speed", (rb.linearVelocity - Vector3.back * 80f).magnitude < 1e-3f, rb.linearVelocity.ToString("F2"));
                var r = p.Root.GetComponentInChildren<MeshRenderer>();
                var c = r?.sharedMaterial;
                Check(p.Kind + ": drawn with the unlit colour of its kind", c && c.shader.name == "HS2Next/Unlit/Color" && c.GetColor("_Color") == RagdollProps.ColorOf(p.Kind), c ? c.shader.name : "no material");
            }
            Check("each comes with its own rotation", Deg(ball.Root.transform.rotation, box.Root.transform.rotation) > 1.0 && Deg(box.Root.transform.rotation, heavy.Root.transform.rotation) > 1.0);
            var firstTurn = ball.Root.transform.rotation;
            Check("a prop is held by the world", props.HoldsWorld && w.IsProp(ball.Rb) && w.PartOf(ball.Rb) == null);

            // a grab, and the pick
            var slow = props.Throw(new Ray(new Vector3(30f, 5f, 0f), Vector3.right), RagdollWorldValues.PropBall, speed: 5f);
            Physics.SyncTransforms();
            var pickRay = new Ray(slow.Root.transform.position + Vector3.right * 4f, Vector3.left);
            Check("the pick finds a prop", w.Pick(pickRay, out var pick) && pick.rigidbody == slow.Rb);
            var grab = w.Grab(slow.Rb, slow.Root.transform.position);
            Check("a prop can be grabbed", grab != null && grab.Part == null && w.Grabs.Contains(grab));
            props.Clear();
            Check("a prop that goes takes its grab with it", props.Count == 0 && !w.Grabs.Contains(grab));

            // the cap
            var thrown = new List<RagdollProp>();
            for (int i = 0; i < RagdollProps.MaxProps + 5; i++) thrown.Add(props.Throw(new Ray(new Vector3(25f, 10f, 0f), Vector3.up), speed: 5f));
            Check("at most 40, the oldest goes", props.Count == RagdollProps.MaxProps && !thrown[4].Root && thrown[5].Root && props.All[0] == thrown[5], $"{props.Count} of {thrown.Count}, thrown {props.Thrown}");
            props.Clear();

            // a hit
            var chest = body.Part("chest");
            float Hit(string kind, float speed)
            {
                int hits = props.Hits;
                var c = chest.Rb.worldCenterOfMass;
                props.Throw(RayAt(c, c + new Vector3(0f, 0.3f, 9f)), kind, speed: speed);
                h.Frames(20);
                return props.Hits > hits ? props.StrongestHit : 0f;
            }
            float small = Hit(RagdollWorldValues.PropBall, 80f);
            Check("a ball's hit is in the status", small > 0f && props.LastHitNode == k && props.Hits >= 1, $"{small:F3} on {props.LastHitNode}, {props.Hits} hits");
            Measure($"RP15 a ball at 80: change of speed {small:F3} of a body of mass {body.Mass:F1}, {props.Hits} hits, the last {props.LastHit:F3}");
            Check("under the knock hit the body stays up", body.State == RagdollState.Up, body.State.ToString());
            props.Clear();
            h.Part.Release(k, false, ExecuteOptions.NotRecorded);

            // the bar, by the same throw in a fresh world
            float Again(float knockHit, out RagdollState state)
            {
                Block(h, k, b => { b.Values.KnockHit = knockHit; b.Values.KnockDistance = 0f; });
                body = Body(h, k, "RP15 again");
                h.Frames(30);
                w = h.Part.World;
                props = w.Props;
                chest = body.Part("chest");
                float dv = Hit(RagdollWorldValues.PropBall, 80f);
                state = body.State;
                props.Clear();
                h.Part.Release(k, false, ExecuteOptions.NotRecorded);
                return dv;
            }
            float above = Again(small * 0.5f, out var down);
            Check("from the knock hit up the body goes down", down == RagdollState.Down && above > 0f, $"{above:F3} against a bar of {small * 0.5f:F3}: {down}");
            float under = Again(small * 2f, out var up);
            Check("under the bar it stays up, by the same hit", up == RagdollState.Up && under > 0f, $"{under:F3} against a bar of {small * 2f:F3}: {up}");
            float off = Again(0f, out var stays);
            Check("a knock hit of 0 knocks nothing down", stays == RagdollState.Up && off > 0f, $"{off:F3}: {stays}");
            Check("the same throw in a fresh world is about the same hit", Mathf.Abs(above - small) < 0.1f * small && Mathf.Abs(under - small) < 0.1f * small && Mathf.Abs(off - small) < 0.1f * small, $"{small:G6} {above:G6} {under:G6} {off:G6}");

            // a heavy prop, by the default bar
            Block(h, k, b => { b.Values.KnockHit = 8f; b.Values.KnockDistance = 3.5f; });
            body = Body(h, k, "RP15 heavy");
            h.Frames(30);
            w = h.Part.World;
            props = w.Props;
            chest = body.Part("chest");
            float big = Hit(RagdollWorldValues.PropHeavy, 80f);
            Check("a heavy prop's hit takes the body down", big >= 8f && body.State == RagdollState.Down, $"{big:F2}: {body.State}");
            Measure($"RP15 a heavy prop at 80: change of speed {big:F2}");

            // the rotation repeats in a fresh world
            h.Part.Release(k, false, ExecuteOptions.NotRecorded);
            int leftAfter = props.Count;
            Check("a prop in the air keeps the world without a body", h.Part.World == w && leftAfter > 0, leftAfter + " props");

            // the lifetime
            props.Clear();
            var late = props.Throw(new Ray(new Vector3(0f, 5f, 0f), Vector3.forward), speed: 5f);
            float age = 0f;
            for (; age < RagdollProps.Lifetime - 0.5f; age += 0.1f) h.Frames(1, 0.1f);
            Check("a prop lives until its time", late.Root && Mathf.Abs(late.Age - age) < 0.05f, $"{late.Age:F2} at {age:F2}");
            h.Frames(10, 0.1f);
            Check("after 20 seconds it is gone", !late.Root && props.Count == 0);
            Check("with its last prop a world without a body goes", h.Part.World == null);

            // a world made for a prop; a fresh one starts its rotations again
            var made = h.Part.EnsureWorld();
            Check("a prop's first throw makes a world with a room", made != w && made.Props.Count == 0 && !made.HasRoom);
            var again = made.Props.Throw(new Ray(from, Vector3.back), RagdollWorldValues.PropBall);
            Check("the first rotation of a fresh world repeats", Deg(again.Root.transform.rotation, firstTurn) == 0.0, Deg(again.Root.transform.rotation, firstTurn).ToString("G4"));
            Check("the room stands, on the block's fixed floor", made.HasRoom && made.FloorY == 0f && made.FloorSource == RagdollFloorSources.Fixed, made.FloorY + " " + made.FloorSource);
            h.Frames(120);
            Check("the prop lies on the floor", Mathf.Abs(again.Root.transform.position.y - 0.6f) < 0.1f, again.Root.transform.position.ToString("F3"));
            var fast = made.Props.Throw(new Ray(new Vector3(3f, 15f, 3f), Vector3.down), RagdollWorldValues.PropBall, speed: 300f);
            h.Frames(30);
            Check("a throw at the top speed does not pass the floor", fast.Root.transform.position.y >= 0.3f, fast.Root.transform.position.ToString("F3"));
            h.Part.CloseNow();
            Check("the world goes with what is left in it", h.Part.World == null && !fast.Root);
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ play mode

    static string gKey;

    static IEnumerator GScene(AppCore app, string map, bool chara = true)
    {
        yield return PCall(app, new { cmd = "scene.new" });
        if (map != null)
        {
            yield return PCall(app, new { cmd = "map.load", @ref = map, wait = true }, 240);
            Check("the map " + map + " loads", POk && (string)pLast["state"] == "Loaded", Short(pLastText));
        }
        if (chara)
        {
            yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" });
            gKey = Key(pLast);
            yield return PCall(app, new { cmd = "chara.motion", key = gKey, physics = false });
            yield return PCall(app, new { cmd = "chara.anime", key = gKey, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f });
        }
        yield return PFrames(3);
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
    }

    static void GWorld(SceneRuntime rt, Action<SceneRagdollWorld> change)
    {
        var b = rt.Model.Ragdoll?.Clone() ?? new SceneRagdollWorld();
        change(b);
        var r = rt.Session.Commands.Execute(new RagdollWorldEditCommand(b), ExecuteOptions.NotRecorded);
        if (!r.Ok) Check("the world's block", false, r.Code + ": " + r.Message);
        rt.ApplyNow();
    }

    static void GLimp(SceneRuntime rt, string k)
    {
        var st = (CharaNodeState)rt.Model.Find(k).State;
        var b = st.Ragdoll.Clone();
        RagdollPreset.ApplyTo(b.Values, RagdollPreset.Limp);
        var r = rt.Session.Commands.Execute(new RagdollEditCommand(k, b), ExecuteOptions.NotRecorded);
        if (!r.Ok) Check("the character's block", false, r.Code + ": " + r.Message);
        rt.ApplyNow();
    }

    static void GPlace(SceneRuntime rt, string id, Vector3 pos)
    {
        rt.Session.Set(id, "transform.pos", new Vec3(pos.x, pos.y, pos.z));
        rt.ApplyNow();
    }

    static RagdollBody GBody(SceneRuntime rt, string k)
    {
        var part = rt.GetPart<RagdollPart>();
        var r = part.Build(k, ExecuteOptions.NotRecorded);
        if (!r.Ok) Check("build", false, r.Code + ": " + r.Message);
        return part.BodyOf(k);
    }

    /// <summary>The lowest answer of the 5 x 5 grid around a point, by the query itself.</summary>
    static float GridLowest(SceneRuntime rt, Vector3 root)
    {
        var pts = new Vector3[25];
        for (int i = 0; i < 5; i++) for (int c = 0; c < 5; c++) pts[i * 5 + c] = new Vector3(root.x - 2f + c, root.y, root.z - 2f + i);
        var hits = new MapSurfaceHit[25];
        MapSurface.Of(rt).Surfaces(pts, hits, 6f, 20f);
        return hits.Where(x => x.Source != null).Select(x => x.Height).DefaultIfEmpty(float.PositiveInfinity).Min();
    }

    static IEnumerator Play_G_Ground()
    {
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!ready[0] || app == null || app.Stage != AppStage.Ready || app.Runtime == null) yield break;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        yield return GGuard("RL9", GRl9(app));
        yield return GGuard("RL1", GRl1(app));
        yield return GGuard("RL5", GRl5(app));
        yield return GGuard("RB8", GRb8(app));
        yield return GGuard("RB8m", GRb8m(app));
        yield return GGuard("RL6", GRl6(app));
        yield return GGuard("RL8", GRl8(app));
        yield return GGuard("RP15d", GDrawn(app));
        yield return PCall(app, new { cmd = "scene.new" });
    }

    /// <summary>Runs a group's coroutine and what it yields here, so that an exception is a FAIL of the group and the rest of the row still runs.</summary>
    static IEnumerator GGuard(string group, IEnumerator body)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            object cur;
            try
            {
                if (!stack.Peek().MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                cur = stack.Peek().Current;
            }
            catch (Exception e)
            {
                current = group;
                Check("ran without an exception", false, e.ToString());
                yield break;
            }
            if (cur is IEnumerator inner) stack.Push(inner);
            else yield return cur;
        }
    }

    static IEnumerator GRl9(AppCore app)
    {
        if (!POpen("RL9", "no map: feet, one finding")) yield break;
        yield return GScene(app, null);
        var rt = app.Runtime;
        var part = rt.GetPart<RagdollPart>();
        Check("no map is loaded", !MapSurface.Of(rt).Loaded);
        Check("no finding before", NoGroundCount(rt) == 0);
        var b = GBody(rt, gKey);
        var w = part.World;
        if (!Check("a body", b != null && w != null)) yield break;
        Check("the floor is at the body's feet", w.FloorSource == RagdollFloorSources.Feet && Mathf.Abs(w.FloorY - (b.Lowest() - 0.1f)) < 1e-5f, w.FloorY + " " + w.FloorSource + ", lowest " + b.Lowest());
        Check("no patch", w.Floor.Patches.Count == 0);
        Check("one finding", NoGroundCount(rt) == 1, NoGroundCount(rt).ToString());
        yield return PCall(app, new { cmd = "chara.load", path = maleRel, sex = "male" });
        string m = Key(pLast);
        yield return PCall(app, new { cmd = "chara.motion", key = m, physics = false });
        var other = GBody(rt, m);
        rt.Step(2, Dt);
        Check("a second body says nothing more", other != null && NoGroundCount(rt) == 1, NoGroundCount(rt).ToString());
        part.Release(gKey, false, ExecuteOptions.NotRecorded);
        part.Release(m, false, ExecuteOptions.NotRecorded);
        Check("the world went with its last body", part.World == null);
        b = GBody(rt, gKey);
        Check("a new world on the same map says nothing more", b != null && NoGroundCount(rt) == 1, NoGroundCount(rt).ToString());
        part.Release(gKey, false, ExecuteOptions.NotRecorded);
    }

    static IEnumerator GRl1(AppCore app)
    {
        if (!Want("RL1") && !Want("RL2") && !Want("RL3")) yield break;
        POpen("RL1", "the patch against the query");
        yield return GScene(app, GFlatMap);
        var rt = app.Runtime;
        var part = rt.GetPart<RagdollPart>();
        var b = GBody(rt, gKey);
        var w = part.World;
        var f = w?.Floor;
        var p = f?.PatchOf(b);
        if (!Check("a body with a patch", p != null)) yield break;
        const int S = RagdollPatch.Side;
        var pts = new Vector3[S * S];
        for (int i = 0; i < S; i++) for (int c = 0; c < S; c++) { var at = p.Point(i, c); pts[i * S + c] = new Vector3(at.x, p.Top, at.z); }
        var hits = new MapSurfaceHit[S * S];
        int found = MapSurface.Of(rt).Surfaces(pts, hits, 0f, RagdollFloor.PatchBelow);
        int wrong = 0;
        double worst = 0;
        for (int i = 0; i < S; i++)
            for (int c = 0; c < S; c++)
            {
                var h = hits[i * S + c];
                if ((h.Source != null) != p.Answered(i, c)) { wrong++; continue; }
                if (h.Source == null) continue;
                double d = Math.Abs(h.Height - p.Point(i, c).y);
                worst = Math.Max(worst, d);
                if (d > 1e-4) wrong++;
            }
        Check("Present: the query found at least 90 % of the points", found >= 0.9 * S * S && found == p.Found, $"{found} of {S * S}, the patch {p.Found}");
        Check("every vertex equals the query", wrong == 0, $"{wrong} differ, largest {worst:G4}");
        Check("the source is the device", p.FromGpu + p.FromTerrain == p.Found && p.FromGpu > 0 && p.FromMesh == 0 && p.FromCollider == 0, $"gpu {p.FromGpu}, terrain {p.FromTerrain}, mesh {p.FromMesh}, collider {p.FromCollider}");
        var mesh = p.Collider.sharedMesh;
        var v = mesh.vertices;
        var origin = p.Collider.transform.position;
        int off = 0;
        for (int i = 0; i < S; i++) for (int c = 0; c < S; c++) if ((v[i * S + c] + origin - p.Point(i, c)).sqrMagnitude > 1e-8f) off++;
        Check("the collider's mesh holds those heights", off == 0 && v.Length == S * S && mesh.triangles.Length == p.Quads * 6, $"{off} of {v.Length} off, {p.Quads} quads");
        Physics.SyncTransforms();
        int rays = 0, onPatch = 0, onOther = 0, atHeight = 0, atFloor = 0, none = 0;
        double worstHeight = 0;
        var others = new HashSet<string>();
        for (int i = 8; i < S; i += 8)
            for (int c = 8; c < S; c += 8)
            {
                if (!p.Answered(i, c)) continue;
                var at = p.Point(i, c);
                rays++;
                if (w.Physics.Raycast(new Vector3(at.x, at.y + 3f, at.z), Vector3.down, out var rh, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    double dy = Math.Abs(rh.point.y - at.y);
                    worstHeight = Math.Max(worstHeight, dy);
                    if (dy < 0.01) atHeight++;
                    if (Mathf.Abs(at.y - w.FloorY) < 1e-3f) atFloor++;
                    if (rh.collider == p.Collider && dy < 0.01) onPatch++;
                    if (rh.collider != p.Collider) { onOther++; others.Add(rh.collider.name); }
                }
                else none++;
            }
        Info($"rays: {rays}, stopped at the vertex's height {atHeight} (worst {worstHeight:G4}), on the patch {onPatch}, on another collider {onOther} ({string.Join(", ", others)}), vertices at the room's floor {atFloor}, no hit {none}");
        // where the patch lies at the room's floor the two surfaces are one: the floor may answer there, and nowhere else
        Check("rays stop at the heights of its vertices in the world's physics", rays >= 20 && atHeight == rays && none == 0, $"{atHeight} of {rays}, worst {worstHeight:G4}");
        Check("Present: above the room's floor they stop on the patch", onPatch >= rays - atFloor && rays - atFloor >= 1 && onOther <= atFloor, $"{onPatch} on the patch, {rays - atFloor} over the floor, {onOther} on the room's floor of {atFloor} there");
        Measure($"RL1 {GFlatMap}: {found} of {S * S} points, query {p.QueryMs:F2} ms, mesh and collider {p.MeshMs:F2} ms, {p.Quads} quads (Editor)");

        if (POpen("RL2", "floor mode map"))
        {
            var root = rt.FindChara(gKey).Root.transform.position;
            float grid = GridLowest(rt, root);
            float want = Mathf.Min(grid, p.Lowest);
            Check("the room's floor is the lowest of the grid and the patch", w.FloorY == want && w.FloorSource == RagdollFloorSources.Map, $"{w.FloorY:G7}, grid {grid:G7}, patch {p.Lowest:G7}, {w.FloorSource}");
            GWorld(rt, x => x.Floor.Offset = 1.5f);
            Check("the offset comes on top", Mathf.Abs(w.FloorY - (want + 1.5f)) < 1e-5f, w.FloorY.ToString("G7"));
            GWorld(rt, x => x.Floor.Offset = 0f);
            Check("and goes", w.FloorY == want, w.FloorY.ToString("G7"));
            float mid = f.HeightAt(root.x, root.z);
            Check("HeightAt under the character is the surface there", Mathf.Abs(mid - p.Point(S / 2, S / 2).y) < 0.05f, mid.ToString("G6"));
        }
        if (POpen("RL3", "floor modes feet and fixed"))
        {
            GWorld(rt, x => x.Floor.Mode = RagdollFloorValues.ModeFeet);
            Check("feet: the lowest collider less the margin", w.FloorSource == RagdollFloorSources.Feet && Mathf.Abs(w.FloorY - (b.Lowest() - 0.1f)) < 1e-5f, $"{w.FloorY:G7}, lowest {b.Lowest():G7}");
            rt.Step(1, Dt);
            Check("feet: no patch", f.Patches.Count == 0, f.Patches.Count.ToString());
            GWorld(rt, x => { x.Floor.Mode = RagdollFloorValues.ModeFixed; x.Floor.Height = 2.5f; });
            rt.Step(1, Dt);
            Check("fixed: the block's height, no patch", w.FloorY == 2.5f && w.FloorSource == RagdollFloorSources.Fixed && f.Patches.Count == 0, w.FloorY + " " + w.FloorSource);
            Check("HeightAt with no patch is the room's floor", f.HeightAt(0f, 0f) == 2.5f);
            GWorld(rt, x => x.Floor.Offset = 0.5f);
            Check("the offset comes on top", w.FloorY == 3f, w.FloorY.ToString("G7"));
            GWorld(rt, x => { x.Floor.Mode = RagdollFloorValues.ModeMap; x.Floor.Offset = 0f; x.Floor.Height = 0f; });
            rt.Step(1, Dt);
            Check("back to map: a patch, the map's floor", f.Patches.Count == 1 && w.FloorSource == RagdollFloorSources.Map, f.Patches.Count + " " + w.FloorSource);
        }
        part.Release(gKey, false, ExecuteOptions.NotRecorded);
    }

    static IEnumerator GRl5(AppCore app)
    {
        if (!POpen("RL5", "the re-centre of a thrown body")) yield break;
        // an open yard: a room's furniture stops a thrown body after a different way on each run
        yield return GScene(app, GHighMap);
        var rt = app.Runtime;
        GWorld(rt, x => x.Room.Size = new Vec3(160f, 60f, 160f));
        GLimp(rt, gKey);
        GPlace(rt, gKey, new Vector3(0f, 6f, 0f));
        yield return PFrames(2);
        var b = GBody(rt, gKey);
        if (!Check("a body", b != null)) yield break;
        rt.Step(90, Dt);
        Thrown(rt, b, new Vector3(110f, 30f, 0f), GHighMap);
        var p = b.World.Floor.PatchOf(b);
        Check("the source stayed the device", p.FromMesh == 0 && p.FromCollider == 0, $"gpu {p.FromGpu}, mesh {p.FromMesh}, collider {p.FromCollider}");
        rt.GetPart<RagdollPart>().Release(gKey, false, ExecuteOptions.NotRecorded);
    }

    static IEnumerator GRb8(AppCore app)
    {
        if (!POpen("RB8", "a map change")) yield break;
        yield return GScene(app, GFlatMap);
        var rt = app.Runtime;
        var part = rt.GetPart<RagdollPart>();
        GLimp(rt, gKey);
        var b = GBody(rt, gKey);
        var w = part.World;
        var f = w?.Floor;
        var p = f?.PatchOf(b);
        if (!Check("a body with a patch", p != null)) yield break;
        var scene = w.Scene;
        var room = w.RoomObject;
        int reads = p.Reads;
        float h0 = p.Point(RagdollPatch.Side / 2, RagdollPatch.Side / 2).y, floor0 = w.FloorY;
        int roots0 = scene.GetRootGameObjects().Length;
        Check("Present: the world holds its objects before", roots0 >= 3 && b.Parts.Count == 16, roots0 + " roots, " + b.Parts.Count + " parts");
        yield return PCall(app, new { cmd = "map.load", @ref = GHighMap, wait = true }, 240);
        Check("the other map loads", POk && (string)pLast["state"] == "Loaded", Short(pLastText));
        yield return PFrames(5);
        Check("the same world, its scene alive", part.World == w && scene.IsValid() && scene.isLoaded, scene.name);
        int alive = b.Parts.Count(x => x.Tr && x.Rb && x.Col);
        Check("every proxy alive, in the world's scene", alive == 16 && b.Parts.All(x => x.Tr.gameObject.scene == scene), alive + " of 16");
        Check("every root of the world in its scene", scene.GetRootGameObjects().Length == roots0 && w.RoomObject && p.Collider && p.Collider.gameObject.scene == scene, scene.GetRootGameObjects().Length + " roots");
        Check("the patch is new", p.Reads == reads + 1, p.Reads + " against " + reads);
        float h1 = p.Point(RagdollPatch.Side / 2, RagdollPatch.Side / 2).y;
        Check("Present: heights differ by at least 1 between the maps", Math.Abs(h1 - h0) >= 1.0, $"{h0:F3} to {h1:F3}");
        float want = Mathf.Min(GridLowest(rt, rt.FindChara(gKey).Root.transform.position), p.Lowest);
        Check("the floor is the new map's", w.FloorY == want && w.FloorY != floor0 && w.FloorSource == RagdollFloorSources.Map, $"{floor0:F3} to {w.FloorY:F3}, want {want:F3}");
        Check("the map change was seen once", f.MapChanges == 1, f.MapChanges.ToString());
        rt.Step(30, Dt);
        Check("the world goes on after it", b.Parts.All(x => x.Tr) && float.IsFinite(Pelvis(b).y), Pelvis(b).ToString("F2"));
        part.Release(gKey, false, ExecuteOptions.NotRecorded);
    }

    // flat at the origin; a static batch cannot follow a map's pose, so the row takes the first of them that has none
    static readonly string[] GMovableMaps = { GFlatMap, "illusion.hs2#21", "illusion.hs2#25", "illusion.hs2#33", "illusion.hs2#15", "illusion.hs2#31" };

    static IEnumerator GRb8m(AppCore app)
    {
        if (!POpen("RB8m", "a map moved by map.set")) yield break;
        string map = null;
        foreach (var candidate in GMovableMaps)
        {
            yield return GScene(app, candidate);
            int batched = app.Runtime.GetPart<MapRealization>().StaticBatchedRenderers;
            Info($"{candidate}: {batched} static-batched renderers");
            if (batched > 0) continue;
            map = candidate;
            break;
        }
        if (!Check("a map with no static batch", map != null)) yield break;
        var rt = app.Runtime;
        var part = rt.GetPart<RagdollPart>();
        GLimp(rt, gKey);
        var b = GBody(rt, gKey);
        var w = part.World;
        var f = w?.Floor;
        var p = f?.PatchOf(b);
        if (!Check("a body with a patch", p != null)) yield break;
        rt.Step(90, Dt);
        var at = Pelvis(b);
        var scene = w.Scene;
        int reads = p.Reads, changes = f.MapChanges, roots = scene.GetRootGameObjects().Length;
        float floor0 = w.FloorY;
        bool before = p.TryHeight(at.x, at.z, out float h0);
        const float Rise = 3f;
        yield return PCall(app, new { cmd = "map.set", pos = new[] { 0f, Rise, 0f } });
        Check("map.set moves the map", POk, Short(pLastText));
        yield return PFrames(3);
        bool after = p.TryHeight(at.x, at.z, out float h1);
        bool any = MapSurface.Of(rt).Surface(new Vector3(at.x, p.Top, at.z), out var under, 0f, RagdollFloor.PatchBelow);
        Check("Present: the height under the pelvis changes by at least 1", before && after && Math.Abs(h1 - h0) >= 1.0, $"{h0:F3} to {h1:F3}, the map moved {Rise}");
        Check("the patch's height under the pelvis is the map's new surface within 0.01", any && after && Math.Abs(h1 - under.Height) <= 0.01, $"patch {h1:F4}, surface {under.Height:F4}, {under.Source}");
        Check("the patch was read once more, the move seen once", p.Reads == reads + 1 && f.MapChanges == changes + 1, $"{p.Reads} reads against {reads}, {f.MapChanges} changes against {changes}");
        float want = Mathf.Min(GridLowest(rt, rt.FindChara(gKey).Root.transform.position), p.Lowest);
        Check("the floor is read again, at the new surface", w.FloorY == want && Math.Abs(w.FloorY - floor0 - Rise) <= 0.01 && w.FloorSource == RagdollFloorSources.Map, $"{floor0:F3} to {w.FloorY:F3}, want {want:F3}");
        int alive = b.Parts.Count(x => x.Tr && x.Rb && x.Col);
        Check("the same world, every proxy alive in its scene", part.World == w && scene.isLoaded && alive == 16 && scene.GetRootGameObjects().Length == roots, $"{alive} of 16, {scene.GetRootGameObjects().Length} roots against {roots}");
        // at rest, from step 120: the map rose through the body and the floor throws it out, so an earlier step is a moment inside a fall
        rt.Step(120, Dt);
        int more = 0;
        for (; more < 360 && b.Parts.Any(x => x.Rb.linearVelocity.magnitude >= 3f); more++) rt.Step(1, Dt);
        Check("the world goes on after it", b.Parts.All(x => x.Tr) && float.IsFinite(Pelvis(b).y) && b.Lowest() >= w.FloorY - 0.05f, $"at rest at step {120 + more}: pelvis {Pelvis(b):F2}, lowest {b.Lowest():F3}, the floor {w.FloorY:F3}");
        Measure($"RB8m {map}: height under the pelvis {h0:F3} to {h1:F3}, surface {under.Height:F3}, floor {floor0:F3} to {w.FloorY:F3}, query {p.QueryMs:F2} ms (Editor)");

        // a drag: the map moves in each of 10 frames, each of them a step, then holds still
        var mapPart = rt.GetPart<MapRealization>();
        int dragReads = p.Reads, dragChanges = f.MapChanges, changed = 0, whileMoving = 0;
        for (int i = 1; i <= 10; i++)
        {
            var was = mapPart.Pos;
            app.Ops.SetMap(new Vector3(0f, Rise + 0.1f * i, 0f), null, null, null, ExecuteOptions.NotRecorded);
            rt.Step(1, Dt);
            if (!mapPart.Pos.Equals(was)) changed++;
            whileMoving += p.Reads - dragReads;
        }
        Check("Present: the pose changed on all 10 frames", changed == 10, changed + " of 10, to " + mapPart.Pos.y.ToString("F2"));
        rt.Step(1, Dt);
        int stopped = p.Reads - dragReads;
        rt.Step(5, Dt);
        Check("a map moved over 10 consecutive frames re-reads once", whileMoving == 0 && stopped == 1 && p.Reads == dragReads + 1 && f.MapChanges == dragChanges + 1, $"{whileMoving} reads while it moved, {stopped} on the first frame it held still, {p.Reads - dragReads} five frames later");

        yield return PCall(app, new { cmd = "map.set", pos = new[] { 0f, 0f, 0f } });
        part.Release(gKey, false, ExecuteOptions.NotRecorded);
    }

    static IEnumerator GRl6(AppCore app)
    {
        if (!POpen("RL6", "the room stops a body; a floor under the furniture")) yield break;
        yield return GScene(app, GFlatMap);
        var rt = app.Runtime;
        var part = rt.GetPart<RagdollPart>();
        GLimp(rt, gKey);

        // the same throw in a wide room and in a small one
        float Reach(float size)
        {
            GWorld(rt, x => x.Room.Size = new Vec3(size, 40f, size));
            var b = GBody(rt, gKey);
            var c = part.World.RoomCenter;
            SetAllVelocities(b, new Vector3(150f, 10f, 0f));
            float far = 0f;
            for (int i = 0; i < 120; i++)
            {
                rt.Step(1, Dt);
                foreach (var q in b.Parts) far = Mathf.Max(far, Mathf.Abs(q.Tr.position.x - c.x));
            }
            part.Release(gKey, false, ExecuteOptions.NotRecorded);
            return far;
        }
        float wide = Reach(160f), small = Reach(20f);
        Check("Present: in a wide room the body goes past where the small room's wall stands", wide > 12f, wide.ToString("F2"));
        Check("the room stops it", small <= 10.5f, $"furthest part {small:F2} from the centre, the wall at 10");

        foreach (var map in new[] { GTableMap, GTableMod })
        {
            yield return GScene(app, map);
            rt = app.Runtime;
            part = rt.GetPart<RagdollPart>();
            GLimp(rt, gKey);
            var q = MapSurface.Of(rt);
            bool any = q.Surface(new Vector3(0f, 8f, 0f), out var under, 0f, 60f);
            Check(map + ": Present: the origin stands on furniture", any && under.Height >= 2f, any ? under.Height.ToString("F3") + " " + under.Source : "no answer");
            GPlace(rt, gKey, new Vector3(0f, under.Height + 4f, 0f));
            yield return PFrames(2);
            var b = GBody(rt, gKey);
            var w = part.World;
            var f = w.Floor;
            var p = f.PatchOf(b);
            Check(map + ": the floor is not the table's", w.FloorY < under.Height - 1f && w.FloorSource == RagdollFloorSources.Map, $"{w.FloorY:F3} under a surface of {under.Height:F3}");
            Check(map + ": the patch comes from the device", p.FromGpu > 0 && p.FromMesh == 0 && p.FromCollider == 0, $"{p.Found} points, gpu {p.FromGpu}");
            rt.Step(300, Dt);
            float lowest = b.Lowest();
            double nearest = double.PositiveInfinity;
            foreach (var x in b.Parts)
            {
                var at = x.Rb.worldCenterOfMass;
                nearest = Math.Min(nearest, Math.Abs(RagdollBody.LowestOf(x) - f.HeightAt(at.x, at.z)));
            }
            Check(map + ": nothing lies under the room's floor", lowest >= w.FloorY - 0.05f, $"{lowest:F3} over a floor of {w.FloorY:F3}");
            Check(map + ": the body lies within 1 of a surface under a part", nearest <= 1.0, nearest.ToString("F3"));
            Check(map + ": it lands at the map's floor, not on the table", lowest <= w.FloorY + 1f, $"{lowest:F3}, the floor {w.FloorY:F3}, the table {under.Height:F3}");
            Measure($"RL6 {map}: lowest {lowest:F3}, floor {w.FloorY:F3}, table {under.Height:F3}, speed {b.Parts[0].Rb.linearVelocity.magnitude:F2}, {p.Found} points");
            part.Release(gKey, false, ExecuteOptions.NotRecorded);
        }
    }

    static IEnumerator GRl8(AppCore app)
    {
        if (!POpen("RL8", "a map with no readable mesh")) yield break;
        yield return GScene(app, GNoMeshMap);
        var rt = app.Runtime;
        var part = rt.GetPart<RagdollPart>();
        int readable = 0, unreadable = 0;
        foreach (var r in rt.GetPart<MapRealization>().Structure.Renderers)
        {
            if (!(r is MeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy || !r.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
            if (mf.sharedMesh.isReadable) readable++;
            else unreadable++;
        }
        Check("Present: of what the map draws no mesh is readable", readable == 0 && unreadable > 0, $"{readable} readable, {unreadable} not");
        GLimp(rt, gKey);
        GPlace(rt, gKey, new Vector3(0f, 4f, 0f));
        yield return PFrames(2);
        var b = GBody(rt, gKey);
        var w = part.World;
        var p = w.Floor.PatchOf(b);
        if (!Check("a patch", p != null)) yield break;
        Check("the source is the device", p.FromGpu == p.Found && p.FromGpu >= 15 && p.FromMesh == 0 && p.FromCollider == 0, $"{p.Found} answers, gpu {p.FromGpu}, mesh {p.FromMesh}, collider {p.FromCollider}");
        Check("the floor is the map's", w.FloorSource == RagdollFloorSources.Map, w.FloorY + " " + w.FloorSource);
        rt.Step(240, Dt);
        Check("a limp drop lands on it", Mathf.Abs(b.Lowest() - w.FloorY) < 1f, $"{b.Lowest():F3} over {w.FloorY:F3}");
        Measure($"RL8 {GNoMeshMap}: {p.Found} answers, gpu {p.FromGpu}, query {p.QueryMs:F2} ms, mesh {p.MeshMs:F2} ms (Editor)");
        part.Release(gKey, false, ExecuteOptions.NotRecorded);
    }

    /// <summary>The number of pixels two images differ in by more than a little, and the mean colour of those pixels in the second.</summary>
    static (int count, Color mean) GDiffer(byte[] a, byte[] b)
    {
        var ta = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var tb = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            ta.LoadImage(a);
            tb.LoadImage(b);
            var pa = ta.GetPixels32();
            var pb = tb.GetPixels32();
            int n = 0;
            double r = 0, g = 0, bl = 0;
            for (int i = 0; i < pa.Length && i < pb.Length; i++)
            {
                int d = Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b);
                if (d < 24) continue;
                n++;
                r += pb[i].r;
                g += pb[i].g;
                bl += pb[i].b;
            }
            return n == 0 ? (0, Color.black) : (n, new Color((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(bl / n / 255.0)));
        }
        finally
        {
            Object.DestroyImmediate(ta);
            Object.DestroyImmediate(tb);
        }
    }

    static IEnumerator GDrawn(AppCore app)
    {
        if (!POpen("RP15d", "a prop shows in a capture")) yield break;
        yield return GScene(app, GFlatMap, false);
        var rt = app.Runtime;
        var part = rt.GetPart<RagdollPart>();
        var cam = rt.GetPart<SceneViewCamera>()?.Camera;
        if (!Check("the view's camera", cam)) yield break;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        // the first render after a build is not the second: the one kept is warm
        HS2Next.App.Commands.ViewCapture.Render(cam, 320, 180, HS2Next.App.Commands.ViewCapture.None);
        yield return PFrames(2);
        var none = HS2Next.App.Commands.ViewCapture.Render(cam, 320, 180, HS2Next.App.Commands.ViewCapture.None);
        foreach (var kind in RagdollWorldValues.PropKinds)
        {
            // a world with nothing in it goes in the next tick: each prop asks for one
            var w = part.EnsureWorld();
            w.Props.Throw(ray, kind);
            var with = HS2Next.App.Commands.ViewCapture.Render(cam, 320, 180, HS2Next.App.Commands.ViewCapture.None);
            var (count, mean) = GDiffer(none, with);
            var want = RagdollProps.ColorOf(kind);
            bool shade = kind == RagdollWorldValues.PropHeavy ? mean.r < 0.6f && mean.g < 0.6f : mean.r > mean.g && mean.g > mean.b;
            Check(kind + ": the capture shows it, in its colour", count >= 100 && shade, $"{count} px, mean {mean}, the colour {want}");
            w.Props.Clear();
            yield return null;
        }
        var after = HS2Next.App.Commands.ViewCapture.Render(cam, 320, 180, HS2Next.App.Commands.ViewCapture.None);
        var (left, _) = GDiffer(none, after);
        Check("gone, the capture is the one before", left == 0, left + " px");
        part.CloseNow();

        // a hit in a local physics scene: the engine's contact event is not the preview scene's alone
        yield return GScene(app, GFlatMap);
        rt = app.Runtime;
        part = rt.GetPart<RagdollPart>();
        var b = GBody(rt, gKey);
        var world = part.World;
        var chest = b.Part("chest").Rb.worldCenterOfMass;
        world.Props.Throw(RayAt(chest, chest + new Vector3(0f, 0.3f, 9f)), RagdollWorldValues.PropHeavy);
        rt.Step(20, Dt);
        Check("a heavy prop's hit in the world's scene", world.Props.Hits >= 1 && world.Props.StrongestHit >= 8f && world.Props.LastHitNode == gKey && b.State == RagdollState.Down, $"{world.Props.StrongestHit:F2}, {world.Props.Hits} hits, {b.State}");
        Measure($"RP15d a heavy prop at 80 in a local scene: change of speed {world.Props.StrongestHit:F2}");
        world.Props.Clear();
        part.Release(gKey, false, ExecuteOptions.NotRecorded);
    }
}
