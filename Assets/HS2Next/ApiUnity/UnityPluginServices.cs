// The engine-side points of the plugin Api (assembly HS2Next.Api.Unity, same version as HS2Next.Api): what needs engine
// types — the realized objects of scene nodes and characters, the build hooks of a character (with skin layers and
// body-part replacement), typed material overrides, the additive mosaic, skin layers and rebuild requests outside builds,
// the experimental UI registration with the host's widgets, the experimental viewport tools, the experimental map query, the
// experimental character stages and the experimental item rows.
// A plugin opts into them by referencing this assembly; the
// engine-free contract (HS2Next.Api) hands them out through IPluginContext.GetService, wrapped by ctx.Unity().
//
// Engine objects are the live realization of the scene: reading them is supported; changing state the scene model owns
// (node transforms, visibility, materials outside IMaterialOverrides) is unsupported and may be overwritten by the next
// apply or build. The mosaic guards of these points: the overrides refuse the mosaic set, the build hooks offer no stage
// right after the mosaic, the host puts a member's mosaic material back after the final hooks of a build in which a plugin
// hook ran, and the additive mosaic only adds. A body part supplied through body-part replacement is drawn as supplied (a
// mosaic member only when the plugin adds it).
using System.Reflection;
using HS2Next.Api.Plugins;
using UnityEngine;

// MAJOR.0.0.0 as HS2Next.Api; the full version is informational.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion(HS2Next.ApiVersion.Semver)]

namespace HS2Next.Api.Unity
{
    /// <summary>The engine-side services of one plugin. Implemented by the host.</summary>
    public interface IUnityPluginServices
    {
        /// <summary>The realized holder object of a scene node, or null (unknown id, not realized yet).</summary>
        GameObject NodeObject(string nodeId);
        /// <summary>The root of a scene character's built body, or null (not a character, not built yet).</summary>
        GameObject CharaRoot(string nodeId);
        /// <summary>Hooks into every character build and the edits that redo a stage.</summary>
        IBuildHooks BuildHooks { get; }
        /// <summary>Typed material and renderer overrides; the mosaic set is refused.</summary>
        IMaterialOverrides Materials { get; }
        /// <summary>
        /// Makes <paramref name="renderer"/> a mosaic member from now on (on a character the mosaic stage runs again; elsewhere
        /// it gets the mosaic material at once). False when the renderer does not exist. Nothing removes a member, also not
        /// the plugin's unload.
        /// </summary>
        bool AddMosaic(Renderer renderer);
        /// <summary>Panels, inspector sections, actions and strings of the Studio UI; null when the UI does not run.</summary>
        [Experimental("the UI registration may change in any minor release")]
        IPluginUi Ui { get; }

        /// <summary>
        /// The plugin's skin layers on a built scene character, changed outside builds (<see cref="ISkinLayers"/>); null when
        /// the node is not a built character.
        /// </summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        ISkinLayers SkinLayers(string nodeId);

        /// <summary>
        /// Rebuilds a scene character at the next apply, so its BodyMesh hooks run again (the way to change a replaced body part
        /// after the build); requests for one character in one frame merge into one rebuild. False when the node is not a built
        /// character.
        /// </summary>
        [Experimental("skin layers and body-part replacement may change in any minor release")]
        bool RequestRebuild(string nodeId);

        /// <summary>
        /// A plugin wrote a bone's transform after the character's step (in the scene post stage): the next step takes that write
        /// off where the host's own bookkeeping would otherwise keep it as new input. False when the node is no built character
        /// or the bone is null. A plugin that shows a value through a runtime override slot needs no such call.
        /// </summary>
        [Experimental("the scene post stage may change in any minor release")]
        bool BoneWritten(string nodeId, Transform bone);

        /// <summary>
        /// Tools of the viewport, which take its pointer, wheel and keys, and surface picks; null when the UI does not run, so read
        /// it when a tool starts rather than keeping it from Load.
        /// </summary>
        [Experimental("the viewport tools may change in any minor release")]
        IViewportTools Tools { get; }

        /// <summary>The loaded map's roots and the height of its surface under a point; never null, also with no map loaded.</summary>
        [Experimental("the map query may change in any minor release")]
        IMapQuery Map { get; }

        /// <summary>Motion stages on scene characters; never null.</summary>
        [Experimental("the character stages may change in any minor release")]
        ICharaStages Stages { get; }

        /// <summary>Item groups of the Studio list whose rows the plugin makes; never null.</summary>
        [Experimental("generated item rows may change in any minor release")]
        IItemRows ItemRows { get; }
    }

    /// <summary>The entry from the engine-free context.</summary>
    public static class PluginContextUnity
    {
        /// <summary>The engine-side services of this plugin, or null when the host has no engine side (a plain test host).</summary>
        public static IUnityPluginServices Unity(this IPluginContext ctx) => ctx?.GetService(typeof(IUnityPluginServices)) as IUnityPluginServices;
    }
}
