// The surface pick of the viewport tools: PickRay's candidates and triangle test, answering what a tool needs of the surface.
//   candidates   the renderers of the realized nodes the filter allows whose world box the ray enters, not hidden; the nearest MaxSurfaceTests get a triangle test
//                on the posed mesh (a baked copy of a skinned part, the mesh of a readable part): a press asks where it landed, so more than a click tests, as a
//                character's parts have boxes that overlap; a mesh that cannot be read falls back to its box
//   the answer   triangle index, barycentric first UV set from the mesh the test ran on, the normal towards the ray, the material slot by submesh, the heaviest
//                bone of the triangle's nearest vertex (a mesh that keeps its weights: the nearest bone of the part) and the point in its space; on a character
//                the part's and the bone's address
//   Precise      false for a box answer and for a part with no first UV set
//   the map      with MapSurface, the map's surface along the ray when no node was hit
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Api.Unity;
using HS2Next.Chara;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.UI.Viewport
{
    public sealed partial class ViewportPicker
    {
        public const int MaxSurfaceTests = 32;
        const float AcrossMargin = 1e-3f;
        static float[] acrossX, acrossY;
        /// <summary>Time of the last surface pick (ms).</summary>
        public double LastSurfaceMs { get; private set; }

        /// <summary>The surface under <paramref name="ray"/> by <paramref name="filter"/>; never null, and <see cref="ToolPick.Hit"/> false on a miss.</summary>
        public ToolPick PickSurface(Ray ray, ToolPickFilter filter = default)
        {
            var sw = Stopwatch.StartNew();
            var pick = new ToolPick();
            var rt = ctx.Runtime;
            if (rt != null) PickNodes(rt, ray, filter, pick);
            if (!pick.Hit && filter.MapSurface && rt != null) PickMap(rt, ray, pick);
            LastSurfaceMs = sw.Elapsed.TotalMilliseconds;
            return pick;
        }

        void PickNodes(SceneRuntime rt, Ray ray, ToolPickFilter filter, ToolPick pick)
        {
            var holders = new Dictionary<Transform, string>();
            foreach (var r in rt.Realizations)
                if (r.Holder && rt.IsInModel(r)) holders[r.Holder] = r.NodeId;
            var cands = new List<(float d, Renderer r, string id)>();
            foreach (var kv in holders)
            {
                if (filter.NodeId != null && kv.Value != filter.NodeId) continue;
                if (filter.CharactersOnly && rt.FindChara(kv.Value) == null) continue;
                foreach (var r in kv.Key.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!r.bounds.IntersectRay(ray, out float d)) continue;
                    if (OwnerOf(r.transform, holders) != kv.Value) continue;   // counted once, for its deepest holder
                    cands.Add((Mathf.Max(0, d), r, kv.Value));
                }
            }
            cands.Sort((a, b) => a.d.CompareTo(b.d));
            Mesh baked = null;
            float best = float.PositiveInfinity;
            int near = -1;
            float boxD = float.PositiveInfinity, boxVol = float.PositiveInfinity;
            Renderer box = null;
            string boxId = null;
            for (int i = 0; i < cands.Count; i++)
            {
                var (d, r, id) = cands[i];
                if (d >= best) break;
                if (i < MaxSurfaceTests)
                {
                    if (SurfaceOf(r, ray, ref baked, ref best, pick, ref near, out bool readable))
                    {
                        pick.NodeId = id;
                        continue;
                    }
                    if (readable) continue;
                }
                if (r.bounds.Contains(ray.origin)) continue;   // the camera inside a box says nothing
                var sz = r.bounds.size;
                float vol = Mathf.Max(1e-6f, sz.x * sz.y * sz.z);
                if (d < boxD - 0.5f || (d < boxD + 0.5f && vol < boxVol)) { boxD = d; boxVol = vol; box = r; boxId = id; }
            }
            if (baked) UnityEngine.Object.Destroy(baked);
            if (pick.Hit)
            {
                if (pick.Renderer is SkinnedMeshRenderer skinned)
                {
                    pick.Bone = WetTargets.HeaviestBone(skinned, near) ?? NearestBone(skinned, pick.Point);
                    if (pick.Bone) pick.BoneLocal = pick.Bone.InverseTransformPoint(pick.Point);
                }
                Address(rt, pick);
                return;
            }
            if (box == null) return;
            pick.Hit = true;
            pick.NodeId = boxId;
            pick.Renderer = box;
            pick.Distance = boxD;
            pick.Point = ray.GetPoint(boxD);
            Address(rt, pick);
        }

        /// <summary>The triangle test of one renderer: true when it beats <paramref name="best"/>, and then <paramref name="pick"/> holds the surface and
        /// <paramref name="near"/> the triangle's vertex nearest to the point.</summary>
        static bool SurfaceOf(Renderer r, Ray ray, ref Mesh baked, ref float best, ToolPick pick, ref int near, out bool readable)
        {
            readable = false;
            Mesh mesh;
            Matrix4x4 m;
            var skinned = r as SkinnedMeshRenderer;
            if (skinned)
            {
                if (!skinned.sharedMesh) return false;
                if (!baked) baked = new Mesh { name = "pick bake" };
                try { skinned.BakeMesh(baked, true); }
                catch (Exception) { return false; }
                mesh = baked;
                m = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return false;
                mesh = mf.sharedMesh;
                m = r.transform.localToWorldMatrix;
            }
            Vector3[] v;
            int[] tris;
            try { v = mesh.vertices; tris = mesh.triangles; }
            catch (Exception) { return false; }
            if (v == null || v.Length == 0 || tris == null || tris.Length == 0) return false;
            readable = true;
            var inv = m.inverse;
            var o = inv.MultiplyPoint3x4(ray.origin);
            var dir = inv.MultiplyVector(ray.direction);
            Across(v, o, dir);
            float t = float.PositiveInfinity, bu = 0, bw = 0;
            int found = -1;
            var px = acrossX;
            var py = acrossY;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int ia = tris[i], ib = tris[i + 1], ic = tris[i + 2];
                float xa = px[ia], xb = px[ib], xc = px[ic];
                if ((xa > AcrossMargin && xb > AcrossMargin && xc > AcrossMargin) || (xa < -AcrossMargin && xb < -AcrossMargin && xc < -AcrossMargin)) continue;
                float ya = py[ia], yb = py[ib], yc = py[ic];
                if ((ya > AcrossMargin && yb > AcrossMargin && yc > AcrossMargin) || (ya < -AcrossMargin && yb < -AcrossMargin && yc < -AcrossMargin)) continue;
                if (TriUv(o, dir, v[ia], v[ib], v[ic], out float ti, out float u, out float w) && ti < t) { t = ti; bu = u; bw = w; found = i; }
            }
            if (found < 0) return false;
            var local = o + dir * t;
            var p = m.MultiplyPoint3x4(local);
            float dist = Vector3.Distance(ray.origin, p);
            if (dist >= best) return false;
            best = dist;
            int a = tris[found], b = tris[found + 1], c = tris[found + 2];
            Vector3 wa = m.MultiplyPoint3x4(v[a]), n = Vector3.Cross(m.MultiplyPoint3x4(v[b]) - wa, m.MultiplyPoint3x4(v[c]) - wa).normalized;
            if (Vector3.Dot(n, ray.direction) > 0) n = -n;
            pick.Hit = true;
            pick.Renderer = r;
            pick.Point = p;
            pick.Distance = dist;
            pick.Normal = n;
            pick.Triangle = found / 3;
            pick.Slot = SlotOf(mesh, found / 3, r);
            pick.Precise = false;
            pick.Uv = default;
            Vector2[] uv = null;
            try { uv = mesh.uv; }
            catch (Exception) { uv = null; }
            if (uv != null && uv.Length == v.Length)
            {
                pick.Uv = uv[a] * (1 - bu - bw) + uv[b] * bu + uv[c] * bw;
                pick.Precise = true;
            }
            float da = (v[a] - local).sqrMagnitude, db = (v[b] - local).sqrMagnitude, dc = (v[c] - local).sqrMagnitude;
            near = da <= db && da <= dc ? a : db <= dc ? b : c;
            return true;
        }

        /// <summary>The vertices' places across the ray, in <see cref="acrossX"/> and <see cref="acrossY"/>: a triangle with all three corners on one side of the ray cannot be hit, so
        /// the exact test runs on the few that are left. The margin keeps a triangle that rounding alone would put on one side.</summary>
        static void Across(Vector3[] v, Vector3 o, Vector3 dir)
        {
            if (acrossX == null || acrossX.Length < v.Length)
            {
                acrossX = new float[v.Length];
                acrossY = new float[v.Length];
            }
            var d = dir.normalized;
            var a = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var b = Vector3.Cross(d, a);
            float ax = a.x, ay = a.y, az = a.z, bx = b.x, by = b.y, bz = b.z, ox = o.x, oy = o.y, oz = o.z;
            var px = acrossX;
            var py = acrossY;
            for (int i = 0; i < v.Length; i++)
            {
                float x = v[i].x - ox, y = v[i].y - oy, z = v[i].z - oz;
                px[i] = x * ax + y * ay + z * az;
                py[i] = x * bx + y * by + z * bz;
            }
        }

        static int SlotOf(Mesh mesh, int triangle, Renderer r)
        {
            int first = 0, slot = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                first += (int)(mesh.GetIndexCount(s) / 3);
                if (triangle < first) { slot = s; break; }
            }
            return Mathf.Clamp(slot, 0, Mathf.Max(0, r.sharedMaterials.Length - 1));   // submeshes beyond the slots draw with the last one
        }

        static Transform NearestBone(SkinnedMeshRenderer s, Vector3 p)
        {
            Transform best = null;
            float bestD = float.PositiveInfinity;
            foreach (var b in s.bones)
            {
                if (!b) continue;
                float d = (b.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        /// <summary>Möller–Trumbore, both faces, with the barycentric weights of the second and third vertex.</summary>
        static bool TriUv(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float w)
        {
            t = u = w = 0;
            var e1 = b - a; var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            var s = o - a;
            u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1);
            w = Vector3.Dot(d, q) * inv;
            if (w < 0 || u + w > 1) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0;
        }

        static void PickMap(SceneRuntime rt, Ray ray, ToolPick pick)
        {
            if (!MapSurface.Of(rt).Ray(ray, float.PositiveInfinity, out var point, out var normal)) return;
            pick.Hit = true;
            pick.OnMap = true;
            pick.Point = point;
            pick.Normal = normal;
            pick.Distance = Vector3.Distance(ray.origin, point);
        }

        // ---------------- addresses on a character

        static void Address(SceneRuntime rt, ToolPick pick)
        {
            var c = rt.FindChara(pick.NodeId);
            if (c == null || c.Context == null) return;
            var cc = c.Context;
            var tops = new Dictionary<Transform, (PosePart part, int slot, string acc)>();
            if (cc.Head) tops[cc.Head.transform] = (PosePart.Head, -1, null);
            if (cc.Body) tops[cc.Body.transform] = (PosePart.Body, -1, null);
            for (int k = 0; k < cc.Hair.Length; k++) if (cc.Hair[k]) tops[cc.Hair[k].transform] = (PosePart.Hair, k, null);
            for (int k = 0; k < cc.Clothes.Length; k++) if (cc.Clothes[k]) tops[cc.Clothes[k].transform] = (PosePart.Clothes, k, null);
            if (cc.Accessories != null)
                foreach (var s in cc.Accessories)
                    if (s != null && s.Object) tops[s.Object.transform] = (PosePart.Accessory, -1, c.AccessoryIdOf(s));
            pick.Part = PartOf(pick.Renderer, tops);
            pick.BoneAddress = BoneAddressOf(c, pick.Bone, tops);
        }

        /// <summary>A bone's address: a bare name for what a bare name reaches, else its name and occurrence under its part; the list of every bone only for a bone found neither way; null
        /// for a bone with no address. A pick waits for this, so the list of a character's bones is the last way, not the first.</summary>
        static string BoneAddressOf(CharaRealization c, Transform bone, Dictionary<Transform, (PosePart part, int slot, string acc)> tops)
        {
            if (!bone) return null;
            var cc = c.Context;
            if (cc.BodyBones != null && cc.BodyBones.TryGetValue(bone.name, out var b) && b == bone || cc.HeadBones != null && cc.HeadBones.TryGetValue(bone.name, out b) && b == bone)
            {
                string text = PoseAddress.Bone(bone.name).Text;
                if (AdvancedPoseResolver.Resolve(c, text) == bone) return text;
            }
            for (var t = bone; t != null; t = t.parent)
            {
                if (!tops.TryGetValue(t, out var top) || top.part == PosePart.Head || top.part == PosePart.Body) continue;
                int n = 0;
                if (!BoneOccurrence(t, bone, ref n)) break;
                string text = PoseAddress.Bone(top.part, top.slot, top.acc, bone.name, n).Text;
                if (AdvancedPoseResolver.Resolve(c, text) == bone) return text;
                break;
            }
            foreach (var row in AdvancedPoseResolver.Bones(c))
                if (row.Bone == bone) return row.Address;
            return null;
        }

        // the same name before it, depth first in child order, never entering a holder
        static bool BoneOccurrence(Transform t, Transform bone, ref int n)
        {
            if (t == bone) return true;
            if (t.name == bone.name) n++;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (!SceneNodeHolder.IsHolder(c) && BoneOccurrence(c, bone, ref n)) return true;
            }
            return false;
        }

        /// <summary>A renderer's address on its character: its part, its name and the occurrence of that name in the part, depth first in child order.</summary>
        static string PartOf(Renderer r, Dictionary<Transform, (PosePart part, int slot, string acc)> tops)
        {
            for (var t = r.transform; t != null; t = t.parent)
            {
                if (!tops.TryGetValue(t, out var top)) continue;
                int n = 0;
                bool found = false;
                foreach (var other in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (other == r) { found = true; break; }
                    if (other.name == r.name && (other is SkinnedMeshRenderer || other is MeshRenderer) && !Elsewhere(other.transform, t, tops)) n++;
                }
                if (!found) return null;
                string text = PoseAddress.Blend(top.part, top.slot, top.acc, r.name, n).Text;
                return PoseAddress.IsCanonical(PoseAddressForm.Blend, text) ? text : null;
            }
            return null;
        }

        // under another part or another node's holder: not of this part
        static bool Elsewhere(Transform t, Transform top, Dictionary<Transform, (PosePart part, int slot, string acc)> tops)
        {
            for (var x = t; x != null && x != top; x = x.parent)
                if (SceneNodeHolder.IsHolder(x) || tops.ContainsKey(x)) return true;
            return false;
        }
    }
}
