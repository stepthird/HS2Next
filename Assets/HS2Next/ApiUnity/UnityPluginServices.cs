// The engine-side points of the plugin Api (assembly HS2Next.Api.Unity, same version as HS2Next.Api): what needs engine
// types — the realized objects of scene nodes and characters, the build hooks of a character, typed material overrides,
// the additive mosaic and the experimental UI registration. A plugin opts into them by referencing this assembly; the
// engine-free contract (HS2Next.Api) hands them out through IPluginContext.GetService, wrapped by ctx.Unity().
//
// Engine objects are the live realization of the scene: reading them is supported; changing state the scene model owns
// (node transforms, visibility, materials outside IMaterialOverrides) is unsupported and may be overwritten by the next
// apply or build. None of these points can turn the mosaic off, change its look or remove a member: the overrides refuse
// the mosaic set, the build hooks offer no stage right after the mosaic, the host puts a member's mosaic material back
// after the final hooks of a build in which a plugin hook ran, and the additive mosaic only adds.
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
    }

    /// <summary>The entry from the engine-free context.</summary>
    public static class PluginContextUnity
    {
        /// <summary>The engine-side services of this plugin, or null when the host has no engine side (a plain test host).</summary>
        public static IUnityPluginServices Unity(this IPluginContext ctx) => ctx?.GetService(typeof(IUnityPluginServices)) as IUnityPluginServices;
    }
}
