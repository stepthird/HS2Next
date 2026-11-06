// Build hooks of a character (engine side of the plugin Api). A hook runs at a fixed point of every character build and,
// for the later stages, again in the edit flushes that redo that point:
//   BodyMesh  after the body is spawned and rebound, before the skin is composited and before any edit runs (builds only):
//             replace part meshes or materials, implant bones, set skin layers, replace body parts (experimental);
//   Textures  after the plugin texture stage (overlays, material edits), in the build and in every edit that redoes it;
//   Final     after the plugin final stage (shape corrections, accessory rules, bone modifiers), in the build and whenever
//             that stage runs again.
// There is no stage right after the mosaic. After the Final hooks of a build or edit in which a hook of a plugin ran, the
// host gives every mosaic member slot its mosaic material back (warning api.mosaicProtected with the hook id and the member,
// one line in the plugin's log). Hooks must be idempotent for the stages that re-run: their result must be a function of the
// character's state. Main thread only; a throw is a fault of the plugin and does not stop the build.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Api.Unity
{
    /// <summary>The points of a character build where a plugin hook runs (see the file header).</summary>
    public enum BuildStage
    {
        /// <summary>After the body is spawned and rebound, before the skin is composited and before any edit runs (builds only).</summary>
        BodyMesh = 0,
        /// <summary>After the plugin texture stage, in the build and in every edit that redoes it.</summary>
        Textures = 1,
        /// <summary>After the plugin final stage, in the build and whenever that stage runs again.</summary>
        Final = 3,
    }

    /// <summary>Registration of build hooks and FK node rows; everything is removed when the plugin unloads. Implemented by the host.</summary>
    public interface IBuildHooks
    {
        /// <summary>
        /// Registers <paramref name="run"/> for <paramref name="stage"/> under <paramref name="id"/> (a second registration of the
        /// same id and stage replaces the first; lower <paramref name="order"/> runs first). ArgumentException for an empty id
        /// or a stage the enum does not name.
        /// </summary>
        void Register(string id, BuildStage stage, Action<IBuildHookArgs> run, int order = 0);
        /// <summary>Removes the hook <paramref name="id"/> of <paramref name="stage"/>; true when one was registered.</summary>
        bool Unregister(string id, BuildStage stage);
        /// <summary>
        /// Registers a provider of FK node rows beyond the stock lists (for bones a hook implants); rows of a node number another
        /// provider already gave are reported and skipped. A second registration of the same id replaces the first.
        /// </summary>
        void RegisterFkNodes(string id, Func<IReadOnlyList<FkNodeRow>> provider);
    }

    /// <summary>What a hook gets for one run (valid during the call). Implemented by the host.</summary>
    public interface IBuildHookArgs
    {
        /// <summary>The id the plugin registered the hook under.</summary>
        string HookId { get; }
        /// <summary>The stage this run belongs to.</summary>
        BuildStage Stage { get; }
        /// <summary>True during the build; false in an edit that redoes the stage.</summary>
        bool InBuild { get; }
        /// <summary>The scene node of the character, or null outside a scene.</summary>
        string NodeId { get; }
        /// <summary>True for a male character.</summary>
        bool IsMale { get; }
        /// <summary>The character's root object.</summary>
        GameObject Root { get; }
        /// <summary>The renderers as built: body, head, clothes, hair, accessories.</summary>
        IReadOnlyList<Renderer> Renderers { get; }
        /// <summary>A body or head bone by name, or null.</summary>
        Transform FindBone(string name);
        /// <summary>
        /// Adds a bone under <paramref name="parent"/> with the given local pose and makes it findable by name (the existing body
        /// bone of that name is returned when there is one); destroyed with the character.
        /// </summary>
        Transform ImplantBone(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale);
        /// <summary>True when <paramref name="renderer"/> is a mosaic member (its materials are refused by <see cref="Materials"/>).</summary>
        bool IsMosaicMember(Renderer renderer);
        /// <summary>Typed overrides on this character's renderers; refusals go to the character's findings.</summary>
        IMaterialOverrides Materials { get; }
        /// <summary>Counts renderers whose mesh, bones or materials the hook changed (shown in the build statistics).</summary>
        void RenderersChanged(int n = 1);
        /// <summary>A line in the character's build log, prefixed with the hook id.</summary>
        void Log(string line);

        /// <summary>
        /// Sets this plugin's skin layer <paramref name="id"/> on the character from PNG or JPEG bytes (checked by their header:
        /// false otherwise), decoded for each composite and released afterwards. The layer's source is "&lt;guid&gt;/&lt;id&gt;";
        /// a second set of the same id replaces it; the plugin's layers are composited after the card's own, in the order set.
        /// BodyMesh only (InvalidOperationException at another stage); ArgumentException for an empty id.
        /// </summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        bool SetSkinLayer(string id, SkinLayerKind kind, byte[] png);

        /// <summary>
        /// As the byte form, from a texture that stays the plugin's: the host never destroys it, and a destroyed texture is
        /// skipped with a line in the build log.
        /// </summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        bool SetSkinLayer(string id, SkinLayerKind kind, Texture2D texture);

        /// <summary>
        /// Removes this plugin's skin layer <paramref name="id"/> (never a card's or another plugin's); true when it had one.
        /// BodyMesh only (InvalidOperationException at another stage).
        /// </summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        bool RemoveSkinLayer(string id);

        /// <summary>This plugin's skin layer ids on this character in this build, in the order set.</summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        IReadOnlyList<string> SkinLayers { get; }

        /// <summary>The names of the renderers under the character's body object (the parts <see cref="ReplaceBodyPart"/> takes), depth-first.</summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        IReadOnlyList<string> BodyParts { get; }

        /// <summary>
        /// Replaces the body part <paramref name="part"/> (a name of <see cref="BodyParts"/>; NotFound otherwise) with
        /// <paramref name="replacement"/>. The host sets the mesh, binds its bones by name to the character's body bones
        /// (implant missing ones first with <see cref="ImplantBone"/>; a name it cannot bind gives Mismatch and changes
        /// nothing), sets the root bone, and sets the materials as copies owned by the character (on the body renderer slot 0
        /// keeps the body's draw material, which carries the composited skin, and the supplied materials fill the other
        /// slots); the part's normals source becomes <see cref="BodyPart.Normals"/>. Replacing the body renderer marks the
        /// character's body mesh as replaced, so the FK node rows of the registered providers join at its first FK
        /// application. The part is recorded as supplied in this build: it is drawn as supplied, and it is a mosaic member only
        /// when the plugin adds it with <see cref="IUnityPluginServices.AddMosaic"/>. Counts one changed renderer. BodyMesh
        /// only (InvalidOperationException at another stage); ArgumentNullException for a null replacement or one without a
        /// mesh.
        /// </summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        BodyPartResult ReplaceBodyPart(string part, BodyPart replacement);
    }

    /// <summary>One FK node row: the node number, the bone name, the FK group column, the hierarchy level and the sync number
    /// (-1: none).</summary>
    public readonly struct FkNodeRow
    {
        /// <summary>A row with its values.</summary>
        public FkNodeRow(int no, string name, int groupColumn, int level, int syncNo = -1)
        {
            No = no;
            Name = name;
            GroupColumn = groupColumn;
            Level = level;
            SyncNo = syncNo;
        }

        /// <summary>The FK node number.</summary>
        public int No { get; }
        /// <summary>The bone's name.</summary>
        public string Name { get; }
        /// <summary>The FK group column the node belongs to.</summary>
        public int GroupColumn { get; }
        /// <summary>The node's level in the FK hierarchy.</summary>
        public int Level { get; }
        /// <summary>The sync number, or -1 for none.</summary>
        public int SyncNo { get; }
    }
}
