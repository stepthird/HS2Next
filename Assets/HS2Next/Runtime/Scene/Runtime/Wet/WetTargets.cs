// What a stain can reach on a built character: the targets (a renderer and a material slot, by kind), the address and the mesh signature that
// name them across rebuilds, the pelvis frame of a moment and a ray against the posed surface. Nothing here writes anything.
//   clothes   the parts of the kinds top, bottom, inner top, inner bottom, tights and socks, and gloves and shoes when their switches are on; every slot
//   skin      the body renderer's first slot; the parts that draw the body's material and the cut bodies follow it, each with the UV0 of the body
//   face      the head's renderers that draw the face material
//   hair      every hair part; every slot
//   accessory every accessory, when its switch is on; every slot. Its address names the accessory by its id
// A renderer or a material of a mosaic member is never a target. A slot with no main texture property is left out; a tiled main texture
// has the state `tiled`, a mesh that gives no vertices or no UV0 the state `unreadable`: such targets stay dry.
// An address is a renderer's text as the file holds it: <part>/<name>[#n], n the occurrence of that name in the part, depth first in child
// order, hidden renderers included, so the same part has the same text whatever is shown. A walk never enters another node's holder, nor
// another part that hangs under its part.
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Wet;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The kinds of a target, as <c>wet.status</c> and the panel name them.</summary>
    public static class WetKinds
    {
        public const string Clothes = "clothes", Skin = "skin", Face = "face", Hair = "hair", Accessory = "accessory";
    }

    /// <summary>The states of a target.</summary>
    public static class WetTargetStates
    {
        public const string Ok = "ok", Tiled = "tiled", Unreadable = "unreadable";
    }

    /// <summary>A renderer slot that shows a target's composite without being a target of its own.</summary>
    public readonly struct WetFollower
    {
        public readonly Renderer Renderer;
        public readonly int Slot;
        public WetFollower(Renderer renderer, int slot) { Renderer = renderer; Slot = slot; }
    }

    /// <summary>One slot a stain can reach.</summary>
    public sealed class WetTarget
    {
        /// <summary>One of <see cref="WetKinds"/>.</summary>
        public string Kind;
        public Renderer Renderer;
        public int Slot;
        /// <summary>The renderer's address (a map's <c>renderer</c>).</summary>
        public string Address;
        /// <summary>One of <see cref="WetTargetStates"/>.</summary>
        public string State = WetTargetStates.Ok;
        /// <summary>The renderer draws now (it is enabled and active): a bake takes only such targets.</summary>
        public bool Shown;
        /// <summary>The mesh's signature (a map's <c>mesh</c>); null while unknown or when it cannot be read.</summary>
        public string Signature;
        /// <summary>What shows this target's composite beside it: the body's parts and cut bodies, on the skin target only.</summary>
        public readonly List<WetFollower> Followers = new List<WetFollower>();

        /// <summary>The kinds a film can lie on, when the renderer is skinned and its shader of the film's families.</summary>
        public bool Glossy => Kind == WetKinds.Clothes || Kind == WetKinds.Accessory;
        /// <summary>The kinds that have a skin factor (<c>look.skin</c>).</summary>
        public bool SkinFactor => Kind == WetKinds.Skin || Kind == WetKinds.Face;
        /// <summary>The largest side of this target's composite.</summary>
        public int CompositeCap => SkinFactor ? 4096 : 2048;
        /// <summary>The slot's material now, or null.</summary>
        public Material Material
        {
            get
            {
                if (!Renderer) return null;
                var m = Renderer.sharedMaterials;
                return Slot >= 0 && Slot < m.Length ? m[Slot] : null;
            }
        }
        /// <summary>The key of a target in a character: address and slot.</summary>
        public string Key => KeyOf(Address, Slot);
        public static string KeyOf(string address, int slot) => address + "|" + slot;
        public override string ToString() => Key + " " + Kind + " " + State;
    }

    /// <summary>A hit of a ray on a character's posed surface.</summary>
    public struct WetSurfaceHit
    {
        public Vector3 Point, Normal;
        public float Distance;
        public Renderer Renderer;
        /// <summary>The bone the nearest vertex of the hit triangle follows most, and the point in its space.</summary>
        public Transform Bone;
        public Vector3 BoneLocal;
    }

    public static class WetTargets
    {
        const string MainTex = "_MainTex";
        static readonly int[] ClothesKinds = { 0, 1, 2, 3, 5, 6 };
        const int GlovesKind = 4, ShoesKind = 7;

        // ------------------------------------------------------------ targets

        /// <summary>
        /// Every target of a built character, the parts in the order skin, face, clothes, hair, accessories; shown or not. With <paramref name="skin"/> false
        /// the skin and face kinds are left out, with <paramref name="hair"/> false the hair; accessories, shoes and gloves come only when asked for (the block's
        /// <c>targets</c>). Signatures are not read here.
        /// </summary>
        public static List<WetTarget> Collect(CharaRealization r, bool skin = true, bool hair = true, bool accessories = false, bool shoes = false, bool gloves = false)
        {
            var list = new List<WetTarget>();
            var cc = r?.Context;
            if (cc == null) return list;
            var tops = Tops(cc);
            var bodyMat = BodyMaterial(cc);
            if (skin)
            {
                AddSkin(cc, list, bodyMat, tops);
                AddFace(cc, list, tops);
            }
            foreach (int k in ClothesKinds)
                if (cc.Clothes[k]) AddPart(cc, list, cc.Clothes[k].transform, tops, PosePart.Clothes, k, WetKinds.Clothes, bodyMat);
            if (gloves && cc.Clothes[GlovesKind]) AddPart(cc, list, cc.Clothes[GlovesKind].transform, tops, PosePart.Clothes, GlovesKind, WetKinds.Clothes, bodyMat);
            if (shoes && cc.Clothes[ShoesKind]) AddPart(cc, list, cc.Clothes[ShoesKind].transform, tops, PosePart.Clothes, ShoesKind, WetKinds.Clothes, bodyMat);
            if (hair)
                for (int k = 0; k < cc.Hair.Length; k++)
                    if (cc.Hair[k]) AddPart(cc, list, cc.Hair[k].transform, tops, PosePart.Hair, k, WetKinds.Hair, bodyMat);
            if (accessories && cc.Accessories != null)
                foreach (var s in cc.Accessories)
                {
                    if (s == null || !s.Object) continue;
                    string id = r.AccessoryIdOf(s);
                    if (id != null) AddPart(cc, list, s.Object.transform, tops, PosePart.Accessory, -1, WetKinds.Accessory, bodyMat, id);
                }
            return list;
        }

        /// <summary>What a block's switches let a stain reach.</summary>
        public static List<WetTarget> Collect(CharaRealization r, WetTargetSwitches on) => Collect(r, on.Skin, on.Hair, on.Accessories, on.Shoes, on.Gloves);

        static Material BodyMaterial(CharaContext cc)
        {
            var b = cc.BodyRenderer;
            if (!b) return null;
            var m = b.sharedMaterials;
            return m.Length > 0 ? m[0] : null;
        }

        static void AddSkin(CharaContext cc, List<WetTarget> list, Material bodyMat, HashSet<Transform> tops)
        {
            var b = cc.BodyRenderer;
            if (!b || !bodyMat || !cc.Body) return;
            if (MosaicSet.IsMosaicMaterial(bodyMat)) return;
            string address = AddressIn(cc.Body.transform, tops, PosePart.Body, -1, b);
            if (address == null) return;
            var t = Make(WetKinds.Skin, b, 0, address, bodyMat);
            if (t == null) return;
            list.Add(t);
            var seen = new HashSet<Renderer> { b };
            foreach (var r in cc.BodyMaterialTargets) AddFollower(t, r, seen);
            foreach (var r in cc.BodyCutRenderers) AddFollower(t, r, seen);
        }

        // a body part or a cut body shows the body's first slot; a member never does
        static void AddFollower(WetTarget t, Renderer r, HashSet<Renderer> seen)
        {
            if (!r || !seen.Add(r) || MosaicSet.IsMember(r)) return;
            t.Followers.Add(new WetFollower(r, 0));
        }

        static void AddFace(CharaContext cc, List<WetTarget> list, HashSet<Transform> tops)
        {
            if (!cc.Head || !cc.FaceDraw) return;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            Walk(cc.Head.transform, cc.Head.transform, tops, r =>
            {
                seen.TryGetValue(r.name, out int n);
                seen[r.name] = n + 1;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != cc.FaceDraw || MosaicSet.IsMember(r)) continue;
                    string address = AddressText(PosePart.Head, -1, r.name, n);
                    var t = address != null ? Make(WetKinds.Face, r, i, address, mats[i]) : null;
                    if (t != null) list.Add(t);
                }
                return false;
            });
        }

        static void AddPart(CharaContext cc, List<WetTarget> list, Transform top, HashSet<Transform> tops, PosePart part, int slot, string kind, Material bodyMat, string accessoryId = null)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            Walk(top, top, tops, r =>
            {
                seen.TryGetValue(r.name, out int n);
                seen[r.name] = n + 1;
                if (MosaicSet.IsMember(r) || cc.DrawsBodyMaterials(r)) return false;
                string address = AddressText(part, slot, r.name, n, accessoryId);
                if (address == null) return false;
                bool cut = cc.DrawsBodySkin(r);
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || MosaicSet.IsMosaicMaterial(mats[i])) continue;
                    if (mats[i] == bodyMat || (cut && i == 0)) continue;   // the skin target's follower, never an own map
                    var t = Make(kind, r, i, address, mats[i]);
                    if (t != null) list.Add(t);
                }
                return false;
            });
        }

        static WetTarget Make(string kind, Renderer r, int slot, string address, Material m)
        {
            if (!m.HasProperty(MainTex)) return null;
            var t = new WetTarget
            {
                Kind = kind,
                Renderer = r,
                Slot = slot,
                Address = address,
                Shown = r.enabled && r.gameObject.activeInHierarchy,
            };
            if (m.GetTextureScale(MainTex) != Vector2.one || m.GetTextureOffset(MainTex) != Vector2.zero) t.State = WetTargetStates.Tiled;
            return t;
        }

        // ------------------------------------------------------------ the walk of a part

        static HashSet<Transform> Tops(CharaContext cc)
        {
            var set = new HashSet<Transform>();
            if (cc.Head) set.Add(cc.Head.transform);
            if (cc.Body) set.Add(cc.Body.transform);
            foreach (var h in cc.Hair) if (h) set.Add(h.transform);
            foreach (var c in cc.Clothes) if (c) set.Add(c.transform);
            if (cc.Accessories != null)
                foreach (var s in cc.Accessories) if (s != null && s.Object) set.Add(s.Object.transform);
            return set;
        }

        // depth first in child order; visit returns true to stop
        static bool Walk(Transform t, Transform top, HashSet<Transform> tops, Func<Renderer, bool> visit)
        {
            Renderer own = t.GetComponent<SkinnedMeshRenderer>();
            if (!own) own = t.GetComponent<MeshRenderer>();
            if (own && visit(own)) return true;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (SceneNodeHolder.IsHolder(c) || (c != top && tops.Contains(c))) continue;
                if (Walk(c, top, tops, visit)) return true;
            }
            return false;
        }

        static string AddressIn(Transform top, HashSet<Transform> tops, PosePart part, int slot, Renderer target)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            string found = null;
            Walk(top, top, tops, r =>
            {
                seen.TryGetValue(r.name, out int n);
                seen[r.name] = n + 1;
                if (r != target) return false;
                found = AddressText(part, slot, r.name, n);
                return true;
            });
            return found;
        }

        // a name that holds a '/' cannot be addressed
        static string AddressText(PosePart part, int slot, string name, int occurrence, string accessoryId = null)
        {
            string text = PoseAddress.Blend(part, slot, accessoryId, name, occurrence).Text;
            return PoseAddress.IsCanonical(PoseAddressForm.Blend, text) ? text : null;
        }

        // ------------------------------------------------------------ signature

        /// <summary>
        /// Reads a target's mesh signature into <see cref="WetTarget.Signature"/>; a mesh that gives no vertices or no UV0 turns the target
        /// <see cref="WetTargetStates.Unreadable"/>. <paramref name="known"/> keeps what was read per renderer within one pass.
        /// </summary>
        public static void ReadSignature(WetTarget t, Dictionary<Renderer, string> known = null)
        {
            if (t?.Renderer == null) return;
            if (known != null && known.TryGetValue(t.Renderer, out string s)) { Note(t, s); return; }
            s = SignatureOf(t.Renderer);
            if (known != null) known[t.Renderer] = s;
            Note(t, s);
        }

        static void Note(WetTarget t, string signature)
        {
            t.Signature = signature;
            if (signature == null) t.State = WetTargetStates.Unreadable;
        }

        /// <summary>The signature of what a renderer draws, or null when its mesh gives no vertices or no UV0.</summary>
        public static string SignatureOf(Renderer r)
        {
            var mesh = MeshOf(r);
            if (!mesh) return null;
            var uv = Uv0(r, mesh);
            if (uv == null) return null;
            var flat = new float[uv.Length * 2];
            for (int i = 0; i < uv.Length; i++) { flat[2 * i] = uv[i].x; flat[2 * i + 1] = uv[i].y; }
            return WetMath.Signature(mesh.name, uv.Length, WetMath.UvHash(flat));
        }

        /// <summary>The mesh a renderer draws, or null.</summary>
        public static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer s) return s.sharedMesh;
            return r && r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
        }

        // the mesh's own UV0 when it can be read, else that of a baked copy; a skinned part's copy keeps them
        static Vector2[] Uv0(Renderer r, Mesh mesh)
        {
            if (mesh.isReadable)
            {
                var uv = mesh.uv;
                return uv.Length > 0 ? uv : null;
            }
            if (!(r is SkinnedMeshRenderer s)) return null;
            var copy = new Mesh { name = "wet uv", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                s.BakeMesh(copy);
                var uv = copy.uv;
                return copy.vertexCount > 0 && uv.Length == copy.vertexCount ? uv : null;
            }
            catch (Exception) { return null; }
            finally { Object.DestroyImmediate(copy); }
        }

        // ------------------------------------------------------------ the pelvis frame

        /// <summary>The body's frame at this moment; false for a character without the bones it is made from.</summary>
        public static bool Frame(CharaContext cc, out WetFrame f)
        {
            f = default;
            var b = cc?.BodyBones;
            if (b == null) return false;
            if (!Bone(b, "cf_J_LegUp00_L", out var hipL) || !Bone(b, "cf_J_LegUp00_R", out var hipR) ||
                !Bone(b, "cf_J_LegLow01_L", out var kneeL) || !Bone(b, "cf_J_LegLow01_R", out var kneeR) || !Bone(b, "cf_J_Hips", out var hips)) return false;
            var right = hipR - hipL;
            if (right.SqrMagnitude <= 0) return false;
            right = right * (1 / right.Magnitude);
            var mid = (hipL + hipR) * .5;
            var up = Orthogonal(hips - mid, right);
            if (up.SqrMagnitude < 1e-6 && Bone(b, "cf_J_Neck", out var neck)) up = Orthogonal(neck - hips, right);
            if (up.SqrMagnitude <= 0) return false;
            up = up * (1 / up.Magnitude);
            f = new WetFrame
            {
                Right = right,
                Up = up,
                Forward = Vec3d.Cross(right, up),
                Mid = mid,
                Scale = (hipL - kneeL).Magnitude / 4.2,
                HipL = hipL,
                KneeL = kneeL,
                HipR = hipR,
                KneeR = kneeR,
            };
            return true;
        }

        static Vec3d Orthogonal(Vec3d v, Vec3d unit) => v - unit * Vec3d.Dot(v, unit);

        static bool Bone(Dictionary<string, Transform> bones, string name, out Vec3d p)
        {
            if (bones.TryGetValue(name, out var t) && t) { p = D(t.position); return true; }
            p = default;
            return false;
        }

        /// <summary>A world point as the math takes it.</summary>
        public static Vec3d D(Vector3 v) => new Vec3d(v.x, v.y, v.z);
        /// <summary>A point of the math as Unity takes it.</summary>
        public static Vector3 V(Vec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        // ------------------------------------------------------------ a ray on the surface

        /// <summary>
        /// The first hit of a ray on the shown targets of a character, within <paramref name="length"/>: posed triangles, by renderer bounds first.
        /// A part it cannot read is left out.
        /// </summary>
        public static bool Raycast(IReadOnlyList<WetTarget> targets, Ray ray, float length, out WetSurfaceHit hit)
        {
            hit = default;
            float best = length;
            bool any = false;
            var seen = new HashSet<Renderer>();
            var baked = new Mesh { name = "wet ray", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (var t in targets)
                {
                    var r = t.Renderer;
                    if (!t.Shown || !r || !seen.Add(r)) continue;
                    if (!r.bounds.IntersectRay(ray, out float enter) || enter > best) continue;
                    if (Against(r, baked, ray, ref best, ref hit)) any = true;
                }
            }
            finally { Object.DestroyImmediate(baked); }
            return any;
        }

        static bool Against(Renderer r, Mesh baked, Ray ray, ref float best, ref WetSurfaceHit hit)
        {
            Vector3[] v;
            int[] tris;
            Matrix4x4 toWorld;
            var s = r as SkinnedMeshRenderer;
            try
            {
                if (s)
                {
                    s.BakeMesh(baked, true);
                    v = baked.vertices;
                    tris = baked.triangles;
                    toWorld = Matrix4x4.TRS(s.transform.position, s.transform.rotation, Vector3.one);   // the scale is in the baked vertices
                }
                else
                {
                    var m = MeshOf(r);
                    if (!m || !m.isReadable) return false;
                    v = m.vertices;
                    tris = m.triangles;
                    toWorld = r.transform.localToWorldMatrix;
                }
            }
            catch (Exception) { return false; }
            var local = new Ray(toWorld.inverse.MultiplyPoint3x4(ray.origin), toWorld.inverse.MultiplyVector(ray.direction).normalized);
            float unit = toWorld.MultiplyVector(local.direction).magnitude;
            int found = -1;
            float bestLocal = unit > 0 ? best / unit : best;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                if (!Triangle(local, v[tris[i]], v[tris[i + 1]], v[tris[i + 2]], out float d) || d >= bestLocal) continue;
                bestLocal = d;
                found = i;
            }
            if (found < 0) return false;
            var p = local.GetPoint(bestLocal);
            var a = v[tris[found]];
            var n = Vector3.Cross(v[tris[found + 1]] - a, v[tris[found + 2]] - a);
            best = bestLocal * unit;
            hit.Point = toWorld.MultiplyPoint3x4(p);
            hit.Normal = toWorld.MultiplyVector(n).normalized;
            hit.Distance = best;
            hit.Renderer = r;
            NearestBone(r, s, v, tris, found, p, ref hit);
            return true;
        }

        // Möller-Trumbore, both faces
        static bool Triangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-9f && det < 1e-9f) return false;
            float inv = 1f / det;
            var s = ray.origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0 || u > 1) return false;
            var q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(ray.direction, q) * inv;
            if (w < 0 || u + w > 1) return false;
            distance = Vector3.Dot(e2, q) * inv;
            return distance > 0;
        }

        // the heaviest bone of the triangle's nearest vertex; a mesh that keeps its weights from us takes the nearest of its bones
        static void NearestBone(Renderer r, SkinnedMeshRenderer s, Vector3[] v, int[] tris, int tri, Vector3 p, ref WetSurfaceHit hit)
        {
            Transform bone = null;
            if (s)
            {
                int near = tris[tri];
                float bestD = (v[near] - p).sqrMagnitude;
                for (int k = 1; k < 3; k++)
                {
                    float d = (v[tris[tri + k]] - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; near = tris[tri + k]; }
                }
                bone = HeaviestBone(s, near) ?? NearestOf(s.bones, hit.Point);
            }
            if (!bone) bone = r.transform;
            hit.Bone = bone;
            hit.BoneLocal = bone.InverseTransformPoint(hit.Point);
        }

        /// <summary>The bone a vertex follows most, or null when the mesh's weights cannot be read.</summary>
        public static Transform HeaviestBone(SkinnedMeshRenderer s, int vertex)
        {
            var m = s ? s.sharedMesh : null;
            if (!m || !m.isReadable) return null;
            var w = m.boneWeights;
            if (vertex < 0 || vertex >= w.Length) return null;
            var bones = s.bones;
            int i = w[vertex].boneIndex0;
            return i >= 0 && i < bones.Length ? bones[i] : null;
        }

        static Transform NearestOf(Transform[] bones, Vector3 p)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var b in bones)
            {
                if (!b) continue;
                float d = (b.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }
    }
}
