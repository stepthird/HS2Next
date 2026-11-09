// Version of the HS2Next.Api assembly, semver. Frozen at 1.0.0: within a major version nothing public is removed or
// changed (a minor release may add, a patch release only fixes), and a plugin built against MAJOR.MINOR loads on a host
// with the same major and a minor at least as high. Interfaces are marked in their documentation as
// implemented by the host (a later minor release may add members: plugins only call them) or implemented by plugins
// (they never change: a new capability is a new optional interface). Types marked [Experimental] are outside the
// promise until the mark is removed.
//
// History:
//   0.1.0  Phase 2.1: ContentId, ContentIds, MotionPhase, MotionFrame, IMotionStage; the save-format ext-block
//          contract (HS2Next.Api.Save).
//   0.2.0  Phase 2.4: the automation-bridge command contract (HS2Next.Api.Bridge): protocol constants and stable
//          error codes, CommandSpec / FieldSpec / ArgTypes / [BridgeArg], handler delegates with deferred and async
//          replies (Register / RegisterAsync, Defer, AfterReply), the argument view, BridgeError / BridgeException /
//          RawJson / BridgeReply, the command registry, readiness stages with progress details, the frame clock with
//          update and late-frame hooks, the output root, batch scopes and hooks.
//   0.3.0  Phase 5.1: the plugin contract (HS2Next.Api.Plugins): IPlugin with the optional IPluginUpdate and
//          IPluginSceneLoaded, the plugin context (host info, log, settings, the owner-scoped command registry, ext
//          handlers, the scene clock, shader supplies, in-process Call / CallAsync), typed main-thread events;
//          CommandFlags.Defers; [Experimental]; the save additions for clothes-part ext data and the read-only kkex
//          window (HS2Next.Api.Save). The assembly has no engine references; the engine-side points live in the
//          HS2Next.Api.Unity assembly of the same version.
//   1.0.0  Phase 5.4: the freeze; experimental commands marked (CommandFlags.Experimental). The public surface of both
//          Api assemblies, the bridge protocol, the commands and the plugin manifest schema are listed under Tools/api/
//          and compared with the code by the ApiSurfaceTest row.
//   1.1.0  Phase 6.1: request helpers with typed replies; capture events; undo entries of plugins; declarative forms and the
//          host's widgets (experimental); skin layers, body-part replacement and character rebuild requests on the engine
//          side (experimental); every public member documented (XML documentation in the distribution).
//   1.2.0  Phase 6.2: runtime overrides — values laid over the scene for display, never saved (experimental); the record
//          capture kind; the scene post stage and bone and IK target paths of the overrides (experimental).
//   1.3.0  Phase 6.6: on the engine side, the viewport tools (a tool takes the viewport's pointer, wheel and keys, with
//          surface picks that name the part, the UV and the bone) and the map query, both experimental. Phase 6.7: the
//          character stages (a motion stage on a scene character, kept across its rebuilds), experimental. Phase 6.8:
//          generated item rows (item groups of the Studio list whose rows a plugin makes), experimental.
using System.Reflection;

// MAJOR.0.0.0 keeps assembly binding stable across minor and patch releases; the full version is informational.
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion(HS2Next.ApiVersion.Semver)]

namespace HS2Next
{
    /// <summary>Version of the stable API surface (assembly HS2Next.Api).</summary>
    public static class ApiVersion
    {
        /// <summary>The major version: it rises when public surface is removed or changed.</summary>
        public const int Major = 1;
        /// <summary>The minor version: it rises when public surface is added.</summary>
        public const int Minor = 3;
        /// <summary>The patch version: it rises for fixes that change no public surface.</summary>
        public const int Patch = 0;

        /// <summary>"MAJOR.MINOR.PATCH".</summary>
        public const string Semver = "1.3.0";
    }
}
