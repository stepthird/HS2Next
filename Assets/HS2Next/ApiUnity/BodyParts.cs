// Skin layers and body-part replacement (engine side of the plugin Api; experimental: it may change in any minor release).
// At the BodyMesh stage of a build a hook sets skin layers (images composited into the character's skin textures) and
// replaces body parts (the renderers under the character's body object: a mesh with its bone names, materials and a
// normals source); the build composites the layers, draws the parts and blends the normals in its one pass, with no second
// build. Outside builds, IUnityPluginServices.SkinLayers changes a character's layers with a re-composite at the next apply
// (no rebuild), and IUnityPluginServices.RequestRebuild runs the build, and its hooks, again. A body part supplied through
// body-part replacement is drawn as supplied; it is a mosaic member only when the plugin adds it with AddMosaic.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Api.Unity
{
    /// <summary>
    /// Where a skin layer is composited. "Over" layers are drawn onto the finished skin texture (the skin colour does not tint
    /// them); "Under" layers onto the skin's base before the paints and makeup (the skin colour tints them). The colour pass
    /// draws the skin texture, the detail pass the skin's detail texture.
    /// </summary>
    [Experimental("skin layers and body-part replacement may change in any minor release")]
    public enum SkinLayerKind
    {
        /// <summary>Over the body's colour texture.</summary>
        BodyOver = 1,
        /// <summary>Over the face's colour texture.</summary>
        FaceOver = 2,
        /// <summary>On the base of the body's colour texture, under its paints.</summary>
        BodyUnder = 3,
        /// <summary>On the base of the face's colour texture, under its paints and makeup.</summary>
        FaceUnder = 4,
        /// <summary>Over the body's detail texture.</summary>
        BodyDetailOver = 41,
        /// <summary>Over the face's detail texture.</summary>
        FaceDetailOver = 42,
        /// <summary>On the base of the body's detail texture.</summary>
        BodyDetailUnder = 43,
        /// <summary>On the base of the face's detail texture.</summary>
        FaceDetailUnder = 44,
    }

    /// <summary>
    /// The plugin's skin layers on one built scene character, outside builds (<see cref="IUnityPluginServices.SkinLayers"/>).
    /// Implemented by the host. A change marks the character's skin for a re-composite at the next apply (no rebuild). The
    /// host keeps these layers per character and plugin and joins them to every later build of the character after its
    /// BodyMesh hooks; they share one id space with the layers the plugin's hooks set (a later set of an id replaces the
    /// earlier one, whichever way it was set). They are dropped when the node is forgotten and when the plugin is unloaded,
    /// faults or is disabled. Main thread only.
    /// </summary>
    [Experimental("skin layers and body-part replacement may change in any minor release")]
    public interface ISkinLayers
    {
        /// <summary>
        /// Sets the layer <paramref name="id"/> from PNG or JPEG bytes (checked by their header: false otherwise), decoded for
        /// each composite and released afterwards. ArgumentException for an empty id.
        /// </summary>
        bool Set(string id, SkinLayerKind kind, byte[] png);

        /// <summary>
        /// Sets the layer <paramref name="id"/> from a texture that stays the plugin's: the host never destroys it, and a
        /// destroyed texture is skipped with a line in the character's build log. ArgumentException for an empty id.
        /// </summary>
        bool Set(string id, SkinLayerKind kind, Texture2D texture);

        /// <summary>Removes this plugin's layer <paramref name="id"/>; true when it had one.</summary>
        bool Remove(string id);

        /// <summary>This plugin's layer ids on the character, in the order set.</summary>
        IReadOnlyList<string> Ids { get; }
    }

    /// <summary>What <see cref="IBuildHookArgs.ReplaceBodyPart"/> did.</summary>
    [Experimental("skin layers and body-part replacement may change in any minor release")]
    public enum BodyPartResult
    {
        /// <summary>The part now draws the replacement.</summary>
        Replaced = 0,
        /// <summary>No body part of that name; nothing changed.</summary>
        NotFound = 1,
        /// <summary>
        /// The replacement does not fit: a bone name the host cannot bind, or a bone count that differs from the part's when no
        /// names are given; nothing changed.
        /// </summary>
        Mismatch = 2,
    }

    /// <summary>
    /// The source of a body part's normal data, named without host types: an asset in an asset bundle of an installed zipmod
    /// or of the game's own data, or <see cref="None"/>.
    /// </summary>
    [Experimental("skin layers and body-part replacement may change in any minor release")]
    public sealed class BodyNormals
    {
        BodyNormals(string zipmodGuid, string bundle, string asset)
        {
            ZipmodGuid = zipmodGuid;
            Bundle = bundle;
            Asset = asset;
        }

        /// <summary>No normal blending for the part (every member is null).</summary>
        public static readonly BodyNormals None = new BodyNormals(null, null, null);

        /// <summary>
        /// The normal-data asset <paramref name="asset"/> in the asset bundle <paramref name="bundle"/> (a path such as
        /// "chara/oo_base.unity3d") of the installed zipmod with the GUID <paramref name="zipmodGuid"/>, or of the game's own
        /// data when <paramref name="zipmodGuid"/> is null or empty. The host resolves it when it applies the character's
        /// normals; one it cannot find is an info finding of the character. ArgumentException for an empty bundle or asset.
        /// </summary>
        public static BodyNormals FromAsset(string zipmodGuid, string bundle, string asset)
        {
            if (string.IsNullOrEmpty(bundle)) throw new ArgumentException("a normals source needs a bundle", nameof(bundle));
            if (string.IsNullOrEmpty(asset)) throw new ArgumentException("a normals source needs an asset", nameof(asset));
            return new BodyNormals(string.IsNullOrEmpty(zipmodGuid) ? null : zipmodGuid, bundle, asset);
        }

        /// <summary>The zipmod's GUID, or null for the game's own data (and for <see cref="None"/>).</summary>
        public string ZipmodGuid { get; }

        /// <summary>The asset bundle's path, or null for <see cref="None"/>.</summary>
        public string Bundle { get; }

        /// <summary>The asset's name, or null for <see cref="None"/>.</summary>
        public string Asset { get; }

        /// <summary>"asset in bundle (zipmod)" or "none", for logs.</summary>
        public override string ToString() => Asset == null ? "none" : Asset + " in " + Bundle + " (" + (ZipmodGuid ?? "game data") + ")";
    }

    /// <summary>
    /// A replacement for one body part (<see cref="IBuildHookArgs.ReplaceBodyPart"/>): the mesh, its bones by name, the root
    /// bone, the materials and the normals source; a null member keeps the part's own.
    /// </summary>
    [Experimental("skin layers and body-part replacement may change in any minor release")]
    public sealed class BodyPart
    {
        /// <summary>The mesh to draw (required).</summary>
        public Mesh Mesh { get; set; }

        /// <summary>
        /// The names of the mesh's bones in the mesh's bone order, bound to the character's body bones by name; null = the
        /// part's current bones (the mesh must then have as many bones).
        /// </summary>
        public string[] BoneNames { get; set; }

        /// <summary>The root bone's name; null = keep the part's root bone.</summary>
        public string RootBone { get; set; }

        /// <summary>The materials (copied per character by the host); null = keep the part's materials.</summary>
        public Material[] Materials { get; set; }

        /// <summary>The source of the part's normal data; null = keep the current source.</summary>
        public BodyNormals Normals { get; set; }

        /// <summary>
        /// A replacement from a renderer the plugin built or loaded (e.g. from its own asset bundle): its shared mesh, the names
        /// of its bones, the name of its root bone and its shared materials. ArgumentNullException for a missing renderer.
        /// </summary>
        public static BodyPart From(SkinnedMeshRenderer source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var bones = source.bones;
            var names = new string[bones.Length];
            for (int i = 0; i < bones.Length; i++) names[i] = bones[i] != null ? bones[i].name : null;
            var root = source.rootBone;
            return new BodyPart
            {
                Mesh = source.sharedMesh,
                BoneNames = names,
                RootBone = root != null ? root.name : null,
                Materials = source.sharedMaterials,
            };
        }
    }
}
