// Chara to Item: building a bake's objects. A prototype is built once per bake
// (per main blob) from its data: the node tree with names, layers, local poses and active flags; textures (BakeTextures.Build);
// materials (BakeMaterials.Build); meshes (BakeMeshes.Build, readable); renderers with their bones, root bone, the stored bounds (V6),
// shadow and probe settings, anchor and the rendering layers of their layer; the inside copies (BakeInsides). The prototype sits under
// an inactive container and never draws; an item's instance is a copy of it (Instantiate), which shares the meshes and textures and
// (textures: built once per blob and sampler state in the runtime, BakeCache.Build with keys; the prototype holds references to them)
// whose materials the item path copies per slot (ItemMaterialWriter.Own). After the item path has run, the renderers the bake marks
// as members are added to the mosaic set (MosaicSet.Add), before the instance's first frame.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Bake
{
    /// <summary>A built bake: the objects every item of that bake copies.</summary>
    public sealed class BakePrototype
    {
        /// <summary>The main blob's SHA-256 (64 hex); null for a prototype built straight from a capture before it was encoded.</summary>
        public string Main;
        /// <summary>The bake's main data (meshes and textures as data are dropped once built).</summary>
        public BakeData Bake;
        /// <summary>The bake's root node (active) under the inactive container.</summary>
        public GameObject Root;
        /// <summary>Textures, meshes and materials made for it.</summary>
        public readonly List<Object> Owned = new List<Object>();
        /// <summary>The runtime cache whose shared textures it uses, and the key of each texture entry it took from it (released with it).</summary>
        public BakeCache SharedIn;
        public readonly List<string> TextureKeys = new List<string>();
        /// <summary>The member renderers: indices into Root.GetComponentsInChildren&lt;Renderer&gt;(true).</summary>
        public int[] Members = Array.Empty<int>();
        /// <summary>Inside copies made.</summary>
        public int Insides;
        /// <summary>Items using it.</summary>
        public int Users;
        public long VideoBytes, MeshBytes;
        public double BuildMs;
        public readonly Dictionary<string, double> Ms = new Dictionary<string, double>(StringComparer.Ordinal);
        public readonly FindingList Findings = new FindingList();

        public bool Alive => Root;

        public void Destroy()
        {
            if (Root) Object.DestroyImmediate(Root);
            foreach (var o in Owned) if (o) Object.DestroyImmediate(o);
            Owned.Clear();
            if (SharedIn != null) foreach (var k in TextureKeys) SharedIn.ReleaseTexture(k);
            TextureKeys.Clear();
            SharedIn = null;
            Root = null;
        }
    }

    public static class BakeBuild
    {
        /// <summary>Tests (BV3's positive control): the members of a built instance are not added to the mosaic set.</summary>
        public static bool SkipMembers;

        /// <summary>
        /// Builds a prototype under <paramref name="container"/>: meshes by index from <paramref name="meshOf"/> (data), textures by index from
        /// <paramref name="texOf"/> (built textures, owned by the caller unless <paramref name="ownTextures"/>).
        /// </summary>
        public static BakePrototype Prototype(BakeData b, Func<int, MeshData> meshOf, Func<int, Texture> texOf, Transform container, bool ownTextures)
        {
            var p = new BakePrototype { Bake = b };
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();
            var tr = new Transform[b.Nodes.Count];
            for (int i = 0; i < b.Nodes.Count; i++)
            {
                var n = b.Nodes[i];
                var go = new GameObject(n.Name) { layer = n.Layer };
                var t = go.transform;
                var parent = n.Parent >= 0 && n.Parent < i ? tr[n.Parent] : container;
                t.SetParent(parent, false);
                if (i == 0) { t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
                else { t.SetLocalPositionAndRotation(n.P, n.R); t.localScale = n.S; }
                tr[i] = t;
            }
            p.Root = tr.Length > 0 ? tr[0].gameObject : new GameObject("bake");
            if (tr.Length == 0) p.Root.transform.SetParent(container, false);
            p.Ms["nodes"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            // video memory of the distinct textures it draws (entries sharing a texture count once)
            var texs = new Texture[b.Textures.Count];
            var distinct = new HashSet<Texture>();
            for (int i = 0; i < texs.Length; i++)
            {
                texs[i] = texOf(i);
                b.Textures[i].Built = texs[i];
                if (!texs[i] || !distinct.Add(texs[i])) continue;
                p.VideoBytes += BakeTextures.VideoBytes(texs[i]);
                if (ownTextures) p.Owned.Add(texs[i]);
            }
            p.Ms["textures"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            var missing = new HashSet<string>(StringComparer.Ordinal);
            var mats = new Material[b.Materials.Count];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = BakeMaterials.Build(b.Materials[i], texs, missing, p.Findings);
                p.Owned.Add(mats[i]);
            }
            p.Ms["materials"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            var meshData = new MeshData[b.Meshes.Count];
            var meshes = new Mesh[b.Meshes.Count];
            for (int i = 0; i < meshes.Length; i++)
            {
                meshData[i] = meshOf(i);
                if (meshData[i] == null) { p.Findings.Warning("bake.dataMissing", b.Meshes[i].Name, "the mesh is missing: its renderer is not built"); continue; }
                meshes[i] = BakeMeshes.Build(meshData[i]);
                p.Owned.Add(meshes[i]);
                p.MeshBytes += (long)meshData[i].V.Length * 48;
            }
            p.Ms["meshes"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            var built = new List<(BakeData.RendData d, Renderer r, Material[] m)>();
            foreach (var r in b.Renderers)
            {
                if (r.Node < 0 || r.Node >= tr.Length || r.Mesh < 0 || r.Mesh >= meshes.Length || !meshes[r.Mesh]) continue;
                var go = tr[r.Node].gameObject;
                if (go.GetComponent<Renderer>()) { p.Findings.Warning("bake.rendererTwice", go.name, "a second renderer on one node is not built"); continue; }
                Renderer rend;
                if (r.Skinned)
                {
                    var s = go.AddComponent<SkinnedMeshRenderer>();
                    var bones = new Transform[r.Bones.Length];
                    for (int k = 0; k < bones.Length; k++) bones[k] = r.Bones[k] >= 0 && r.Bones[k] < tr.Length ? tr[r.Bones[k]] : tr[r.Node];
                    s.bones = bones;
                    s.rootBone = r.Root >= 0 && r.Root < tr.Length ? tr[r.Root] : null;
                    s.sharedMesh = meshes[r.Mesh];
                    s.localBounds = r.Bounds;
                    s.updateWhenOffscreen = r.UpdateOffscreen;
                    s.quality = (SkinQuality)r.Quality;
                    rend = s;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = meshes[r.Mesh];
                    rend = go.AddComponent<MeshRenderer>();
                }
                var sm = new Material[r.Materials.Length];
                for (int k = 0; k < sm.Length; k++) sm[k] = r.Materials[k] >= 0 && r.Materials[k] < mats.Length ? mats[r.Materials[k]] : null;
                rend.sharedMaterials = sm;
                rend.shadowCastingMode = (ShadowCastingMode)r.Shadow;
                rend.receiveShadows = r.Receive;
                rend.lightProbeUsage = (LightProbeUsage)r.LightProbe;
                rend.reflectionProbeUsage = (ReflectionProbeUsage)r.ReflProbe;
                if (r.Anchor >= 0 && r.Anchor < tr.Length) rend.probeAnchor = tr[r.Anchor];
                rend.renderingLayerMask = HS2Next.RenderingLayers.ForLayer(go.layer);
                built.Add((r, rend, sm));
            }
            p.Ms["renderers"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            // inside copies, made from the stored meshes at the bake's pose
            var flat = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var (d, r, m) in built)
                if (BakeInsides.Make(b, d, r, meshData[d.Mesh], m, p.Root.transform, p.Owned, flat)) p.Insides++;
            p.Ms["insides"] = sw.Elapsed.TotalMilliseconds; sw.Restart();

            for (int i = 1; i < tr.Length; i++) if (!b.Nodes[i].Active) tr[i].gameObject.SetActive(false);

            // the members by their order among the prototype's renderers (an instance keeps that order)
            var all = p.Root.GetComponentsInChildren<Renderer>(true);
            var memberSet = new HashSet<Renderer>();
            foreach (var (d, r, _) in built) if (d.Member) memberSet.Add(r);
            var members = new List<int>();
            for (int i = 0; i < all.Length; i++) if (memberSet.Contains(all[i])) members.Add(i);
            p.Members = members.ToArray();
            p.BuildMs = total.Elapsed.TotalMilliseconds;
            return p;
        }

        /// <summary>An item's copy of a prototype under <paramref name="holder"/> at local identity, active.</summary>
        public static GameObject Instantiate(BakePrototype p, Transform holder)
        {
            var go = Object.Instantiate(p.Root, holder, false);
            go.name = p.Root.name;
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            if (!go.activeSelf) go.SetActive(true);
            return go;
        }

        /// <summary>Adds the instance's member renderers to the mosaic set (R1); returns how many. Call after the item path has written the
        /// instance's materials and before its first frame.</summary>
        public static int AddMembers(BakePrototype p, GameObject instance)
        {
            if (p == null || !instance || p.Members.Length == 0 || SkipMembers) return 0;
            var all = instance.GetComponentsInChildren<Renderer>(true);
            int n = 0;
            foreach (int i in p.Members)
                if (i >= 0 && i < all.Length && all[i] && MosaicSet.Add(all[i])) n++;
            return n;
        }

        /// <summary>The member renderers of an instance by the prototype's record (tests and bake.status).</summary>
        public static List<Renderer> MembersOf(BakePrototype p, GameObject instance)
        {
            var l = new List<Renderer>();
            if (p == null || !instance) return l;
            var all = instance.GetComponentsInChildren<Renderer>(true);
            foreach (int i in p.Members) if (i >= 0 && i < all.Length) l.Add(all[i]);
            return l;
        }
    }
}
