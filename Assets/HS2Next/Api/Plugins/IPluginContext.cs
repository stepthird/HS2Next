// What a plugin works with: one context per plugin, handed to IPlugin.Load. Through it the plugin registers commands
// (names under its manifest prefixes, owned by its GUID), ext blocks of its namespace, event handlers and shader
// supplies, reads the host and the scene clock, keeps its settings, and calls bridge commands in-process (as envelope text
// with Call / CallAsync, or with Request / RequestAsync, which build the envelope from a name and an argument object and
// return a typed reply). Experimental: undo entries in the scene's history (History), declarative panels and inspector
// sections drawn by the host (Forms) and runtime overrides, values laid over the scene for display and never saved
// (Overrides). The host removes everything registered through the context when the plugin unloads or is disabled. Engine-side points (node objects, build hooks, material overrides, skin layers, UI) are reached through
// the services of the HS2Next.Api.Unity assembly (GetService, wrapped there as an extension method). Part of the stable API
// (assembly HS2Next.Api).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Save;
using HS2Next.Api.Ui;

namespace HS2Next.Api.Plugins
{
    /// <summary>
    /// The plugin's view of the host. Implemented by the host, one instance per plugin instance. Valid from Load until Unload
    /// returns; afterwards every member throws ObjectDisposedException except <see cref="Guid"/>, <see cref="Log"/> and
    /// <see cref="IsActive"/>.
    /// <see cref="IServiceProvider.GetService"/> returns host services by interface type (null when the host has none of
    /// that type, e.g. the engine-side services in a host without an engine).
    /// </summary>
    public interface IPluginContext : IServiceProvider
    {
        /// <summary>The manifest's guid: the command owner, the ext namespace root.</summary>
        string Guid { get; }
        /// <summary>The manifest's name.</summary>
        string Name { get; }
        /// <summary>The manifest's version.</summary>
        string Version { get; }
        /// <summary>The plugin folder (read-only for the plugin by convention: an update may replace it).</summary>
        string Directory { get; }
        /// <summary>&lt;UserData&gt;/Plugins/&lt;guid&gt;/: the plugin's own data; created on first use.</summary>
        string DataDirectory { get; }

        /// <summary>The host application: versions and where it runs.</summary>
        IHostInfo Host { get; }
        /// <summary>The plugin's log; usable after Unload.</summary>
        IPluginLog Log { get; }
        /// <summary>The plugin's settings file (&lt;UserData&gt;/Plugins/&lt;guid&gt;/settings.json).</summary>
        IPluginSettings Settings { get; }

        /// <summary>
        /// The bridge's command registry, scoped to this plugin: the owner is forced to the GUID, the group defaults to the
        /// GUID, the version shown by help defaults to the plugin version, and a name outside the manifest's
        /// commandPrefixes is refused with ArgumentException; otherwise the registry's own rules apply (collisions and
        /// reserved names are refused).
        /// </summary>
        ICommandRegistry Commands { get; }
        /// <summary>
        /// The bridge's frame clock (scheduled actions, waits, late actions). Actions the plugin scheduled through it are dropped,
        /// not run, once the plugin is no longer loaded (unloaded, faulted or disabled).
        /// </summary>
        IBridgeFrames Frames { get; }
        /// <summary>The bridge's readiness stages; a plugin may declare stages of its own that are not required for the app.</summary>
        IBridgeReadiness Readiness { get; }
        /// <summary>The bridge output root (captures and dumps).</summary>
        IOutputRoot Output { get; }

        /// <summary>Ext block handlers of the plugin's own namespaces (the GUID and &lt;guid&gt;.*).</summary>
        IExtRegistry Ext { get; }
        /// <summary>Typed main-thread events, delivered after the host's own handlers.</summary>
        IAppEvents Events { get; }
        /// <summary>The current scene's clock (read-only).</summary>
        ISceneClock Clock { get; }
        /// <summary>Shader replacements by name for materials loaded later.</summary>
        IShaderSupply Shaders { get; }

        /// <summary>
        /// Runs one bridge request now, on the calling (main) thread, and returns the reply JSON exactly as HTTP would
        /// return it; command errors come back as error replies, never as exceptions. The request is a bridge envelope
        /// (<c>cmd</c>, arguments, <c>id</c>, <c>strict</c>, <c>undo</c>) without <c>timeout</c>. Refused before the
        /// handler runs: <c>batch</c>, a command that may defer its reply (<see cref="CommandFlags.Defers"/>), an
        /// asynchronous command and <c>waitFrames</c> &gt; 0 (<c>unsupported</c>: use <see cref="CallAsync"/>); calls nested
        /// deeper than 8 (<c>busy</c>). A command that changes the scene answers <c>busy</c> while another party's scene
        /// transaction is open (a drag, a running batch) or while a scene event or a capture event is being delivered, so
        /// plugin writes never join someone else's undo step. InvalidOperationException off the main thread.
        /// </summary>
        string Call(string requestJson);

        /// <summary>
        /// Queues one bridge request behind the requests already queued, as an HTTP request without the safety headers;
        /// every command is allowed. The task completes (on the main thread or a worker) when the reply is written. Any
        /// thread.
        /// </summary>
        Task<string> CallAsync(string requestJson);

        /// <summary>
        /// Builds the envelope <c>{"cmd": command, …args}</c> and runs it as <see cref="Call"/> does (now, on the main thread,
        /// with the same refusals), returning the reply; for the same envelope the reply text equals what Call returns.
        /// <paramref name="args"/> is null, an object whose public properties and fields become the arguments (an anonymous
        /// type, a plain class), an <c>IDictionary&lt;string, object&gt;</c>, or a Newtonsoft.Json <c>JObject</c> passed as
        /// object; it is serialized with the bridge's own settings (invariant culture, floats that round-trip, nulls written),
        /// so numbers arrive as an HTTP client would send them. The envelope members <c>id</c>, <c>strict</c> and <c>undo</c>
        /// may be members of <paramref name="args"/>. Command errors come back as a reply whose <see cref="ICommandReply.Ok"/>
        /// is false, never as exceptions. ArgumentException for an empty command, for <paramref name="args"/> that is a
        /// string, a number, a bool or an array ("arguments are an object; use Call for envelope text"), and for an argument
        /// named <c>cmd</c>; InvalidOperationException off the main thread.
        /// </summary>
        ICommandReply Request(string command, object args = null);

        /// <summary>
        /// As <see cref="Request(string, object)"/>, from a request built with <see cref="CommandRequest"/>, whose envelope
        /// members are sent as given (a <see cref="CommandRequest.WaitFrames"/> above 0 is refused as Call refuses it, a
        /// <see cref="CommandRequest.Timeout"/> is ignored with Call's warning). ArgumentNullException for a null request.
        /// </summary>
        ICommandReply Request(CommandRequest request);

        /// <summary>
        /// As <see cref="Request(string, object)"/>, queued as <see cref="CallAsync"/> queues (every command is allowed, and the
        /// envelope members <c>waitFrames</c> and <c>timeout</c> apply). Any thread; argument misuse throws at once, before
        /// anything is queued. The task completes when the reply is written.
        /// </summary>
        Task<ICommandReply> RequestAsync(string command, object args = null);

        /// <summary>
        /// As <see cref="Request(CommandRequest)"/>, queued as <see cref="CallAsync"/> queues;
        /// <see cref="CommandRequest.WaitFrames"/> and <see cref="CommandRequest.Timeout"/> apply. Any thread.
        /// </summary>
        Task<ICommandReply> RequestAsync(CommandRequest request);

        /// <summary>
        /// True from Load until Unload returns; false once the plugin is unloaded, faulted or disabled. Never throws, so a
        /// continuation or a callback that runs later can check it before it uses the context.
        /// </summary>
        bool IsActive { get; }

        /// <summary>The plugin's entries in the scene's undo history (experimental).</summary>
        [Experimental("plugin undo entries may change in any minor release")]
        IPluginHistory History { get; }

        /// <summary>
        /// Declarative panels and inspector sections that the host draws with its own widgets (experimental); usable without
        /// the HS2Next.Api.Unity assembly.
        /// </summary>
        [Experimental("the declarative forms may change in any minor release")]
        IPluginForms Forms { get; }

        /// <summary>
        /// The plugin's runtime overrides (experimental): slots that lay values over the scene for display (node transforms,
        /// FK bone rotations, animation, face, colours, the camera, the depth of field), never saved and never in the undo
        /// history. The host releases the plugin's slots when the plugin is unloaded, faults or is disabled. In a host without a
        /// scene runtime <see cref="IRuntimeOverrides.Available"/> is false.
        /// </summary>
        [Experimental("runtime overrides may change in any minor release")]
        IRuntimeOverrides Overrides { get; }
    }

    /// <summary>The host application. Implemented by the host.</summary>
    public interface IHostInfo
    {
        /// <summary>The app's version (semver).</summary>
        string AppVersion { get; }
        /// <summary>The HS2Next.Api version the host implements (semver).</summary>
        string ApiVersion { get; }
        /// <summary>Running inside the Unity Editor (play mode or an Editor test).</summary>
        bool IsEditor { get; }
        /// <summary>Running without a window (Editor batch mode, a batch player).</summary>
        bool IsBatchMode { get; }
        /// <summary>Running in a built player.</summary>
        bool IsPlayer { get; }
        /// <summary>The Studio UI runs.</summary>
        bool UiAvailable { get; }
        /// <summary>HS2Next's UserData folder (absolute).</summary>
        string UserData { get; }
    }

    /// <summary>
    /// The plugin's log: Unity's log with the prefix "[plugin &lt;guid&gt;]" (visible to the bridge's log.tail).
    /// Implemented by the host. Usable after Unload.
    /// </summary>
    public interface IPluginLog
    {
        /// <summary>An information line.</summary>
        void Info(string message);
        /// <summary>A warning line.</summary>
        void Warning(string message);
        /// <summary>An error line.</summary>
        void Error(string message);
    }

    /// <summary>
    /// The plugin's settings file &lt;UserData&gt;/Plugins/&lt;guid&gt;/settings.json, as JSON text; the plugin owns its
    /// schema. Implemented by the host.
    /// </summary>
    public interface IPluginSettings
    {
        /// <summary>The absolute path of the file.</summary>
        string Path { get; }
        /// <summary>The file's text, or null when it does not exist.</summary>
        string Read();
        /// <summary>
        /// Replaces the file atomically with one JSON value (the folder is created when needed). ArgumentException when the
        /// text is not one JSON value.
        /// </summary>
        void Write(string json);
    }

    /// <summary>The current scene's clock, read-only. Implemented by the host.</summary>
    public interface ISceneClock
    {
        /// <summary>Scene seconds since the clock was last reset (a new or loaded scene resets it).</summary>
        double Time { get; }
        /// <summary>The seconds of the current frame (0 while paused, except inside a step).</summary>
        float FrameDt { get; }
        /// <summary>The scene is paused.</summary>
        bool Paused { get; }
        /// <summary>Frames begun since the last reset.</summary>
        long Frame { get; }
        /// <summary>Steps run since the last reset.</summary>
        long Steps { get; }
    }

    /// <summary>
    /// Shader replacements by name (engine-free): when a material whose shader is <c>original</c> is loaded later, the
    /// host gives it <c>replacement</c> (a shader the host can find by name, e.g. one the plugin loaded from its own
    /// asset bundle). The mosaic shaders are refused as original and as replacement. A second supply of the same original
    /// by another plugin wins and stacks; withdrawing the top one restores the one below, and the plugin's unload
    /// withdraws all of its supplies. Implemented by the host.
    /// </summary>
    public interface IShaderSupply
    {
        /// <summary>Maps <paramref name="original"/> to <paramref name="replacement"/>; false with the reason when refused.</summary>
        bool Supply(string original, string replacement, out string refusal);
        /// <summary>Removes this plugin's supply of <paramref name="original"/> and restores what was mapped before; false when it had none.</summary>
        bool Withdraw(string original);
        /// <summary>This plugin's supplies (original, replacement) in the order made.</summary>
        IReadOnlyList<KeyValuePair<string, string>> Supplied { get; }
    }
}
