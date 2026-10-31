// What a plugin works with: one context per plugin, handed to IPlugin.Load. Through it the plugin registers commands
// (names under its manifest prefixes, owned by its GUID), ext blocks of its namespace, event handlers and shader
// supplies, reads the host and the scene clock, keeps its settings, and calls bridge commands in-process. The host
// removes everything registered through the context when the plugin unloads. Engine-side points (node objects, build
// hooks, material overrides, UI) are reached through the services of the HS2Next.Api.Unity assembly
// (GetService, wrapped there as an extension method). Part of the stable API (assembly HS2Next.Api).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Save;

namespace HS2Next.Api.Plugins
{
    /// <summary>
    /// The plugin's view of the host. Implemented by the host, one instance per plugin. Valid from Load until Unload
    /// returns; afterwards every member throws ObjectDisposedException except <see cref="Guid"/> and <see cref="Log"/>.
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

        IHostInfo Host { get; }
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
        /// <summary>The bridge's frame clock (scheduled actions, waits, late actions).</summary>
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
        /// transaction is open (a drag, a running batch) or while a scene event is being delivered, so plugin writes never
        /// join someone else's undo step. InvalidOperationException off the main thread.
        /// </summary>
        string Call(string requestJson);

        /// <summary>
        /// Queues one bridge request behind the requests already queued, as an HTTP request without the safety headers;
        /// every command is allowed. The task completes (on the main thread or a worker) when the reply is written. Any
        /// thread.
        /// </summary>
        Task<string> CallAsync(string requestJson);
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
        void Info(string message);
        void Warning(string message);
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
