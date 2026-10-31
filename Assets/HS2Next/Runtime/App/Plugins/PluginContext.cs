// A plugin's context (IPluginContext): one per plugin, built by the loader before the plugin's Load. It hands out the
// owner-scoped command registry, the bridge's frames and output root, a readiness view whose stages are never required
// for the app, the plugin's ext registry view (faults count for the plugin), its events, the scene clock, its settings
// file and log, the engine-side services (GetService) and the in-process calls: Call runs a request inline through the
// bridge (BridgeServer.RunNow), CallAsync queues it like an HTTP request. After the plugin unloads every member except
// Guid and Log throws ObjectDisposedException.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Save;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.App.Plugins
{
    public sealed class PluginContext : IPluginContext
    {
        readonly PluginHost host;
        readonly PluginEntry entry;
        readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        readonly HostInfo hostInfo;
        readonly PluginLogger log;
        readonly PluginSettingsFile settings;
        readonly PluginReadiness readiness;
        readonly PluginClock clock;
        readonly IExtRegistry ext;
        volatile bool valid = true;

        internal PluginContext(PluginHost host, PluginEntry entry)
        {
            this.host = host;
            this.entry = entry;
            Guid = entry.Guid;
            var m = entry.Manifest;
            Name = m.Name;
            Version = m.Version;
            Directory = entry.Candidate.Dir;
            DataDirectory = Path.Combine(host.UserData ?? Path.GetTempPath(), "Plugins", Guid);
            hostInfo = new HostInfo(host);
            log = new PluginLogger(Guid);
            settings = new PluginSettingsFile(DataDirectory, () => valid);
            readiness = new PluginReadiness(host.Bridge.Readiness);
            clock = new PluginClock(host);
            ext = ExtRegistry.Default.ForOwner(Guid, (ns, hook, e) => entry.Guard?.Count("ext " + ns + " " + hook, e));
        }

        /// <summary>Ends the context: later member access throws (Guid and Log keep working).</summary>
        internal void Invalidate() => valid = false;
        public bool IsValid => valid;

        T Check<T>(T value)
        {
            if (!valid) throw new ObjectDisposedException("plugin context " + Guid, "the plugin has unloaded");
            return value;
        }

        public string Guid { get; }
        public string Name { get; }
        public string Version { get; }
        public string Directory { get; }
        public string DataDirectory { get; }
        public IHostInfo Host => Check(hostInfo);
        public IPluginLog Log => log;
        public IPluginSettings Settings => Check(settings);
        public ICommandRegistry Commands => Check(entry.Commands);
        public IBridgeFrames Frames => Check(host.Bridge.Frames);
        public IBridgeReadiness Readiness => Check(readiness);
        public IOutputRoot Output => Check(host.Bridge.Output);
        public IExtRegistry Ext => Check(ext);
        public IAppEvents Events => Check(entry.Events);
        public ISceneClock Clock => Check(clock);
        public IShaderSupply Shaders => Check(entry.Engine?.Shaders ?? NoShaderSupply.Instance);

        public object GetService(Type serviceType)
        {
            Check(this);
            if (serviceType == null) return null;
            var s = entry.Engine?.Services;
            if (s != null && serviceType.IsInstanceOfType(s)) return s;
            if (serviceType.IsInstanceOfType(this)) return this;
            return null;
        }

        public string Call(string requestJson)
        {
            Check(this);
            if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("Call must be made on the main thread (use CallAsync)");
            return host.Bridge.RunNow(requestJson);
        }

        public Task<string> CallAsync(string requestJson)
        {
            Check(this);
            return host.Bridge.SubmitAsync(requestJson);
        }

        public override string ToString() => "plugin context " + Guid + (valid ? "" : " (unloaded)");

        // ------------------------------------------------------------ parts

        sealed class HostInfo : IHostInfo
        {
            readonly PluginHost host;
            public HostInfo(PluginHost host) { this.host = host; }
            public string AppVersion => Application.version;
            public string ApiVersion => HS2Next.ApiVersion.Semver;
            public bool IsEditor => Application.isEditor;
            public bool IsBatchMode => Application.isBatchMode;
            public bool IsPlayer => !Application.isEditor;
            public bool UiAvailable => HS2Next.UI.UiHost.Current != null;
            public string UserData => host.UserData;
        }

        sealed class PluginLogger : IPluginLog
        {
            readonly string prefix;
            public PluginLogger(string guid) { prefix = "[plugin " + guid + "] "; }
            public void Info(string message) => Debug.Log(prefix + message);
            public void Warning(string message) => Debug.LogWarning(prefix + message);
            public void Error(string message) => Debug.LogError(prefix + message);
        }

        /// <summary>The bridge's readiness, except that a plugin's own stages are never required for the app.</summary>
        sealed class PluginReadiness : IBridgeReadiness
        {
            readonly IBridgeReadiness inner;
            public PluginReadiness(IBridgeReadiness inner) { this.inner = inner; }
            public void Declare(string stage, bool requiredForApp) => inner.Declare(stage, false);
            public void Set(string stage, bool ready) => inner.Set(stage, ready);
            public bool IsReady(string stage) => inner.IsReady(stage);
            public bool AppReady => inner.AppReady;
            public void SetDetail(string stage, string detail) => inner.SetDetail(stage, detail);
            public string Detail(string stage) => inner.Detail(stage);
            public IReadOnlyList<KeyValuePair<string, bool>> Snapshot() => inner.Snapshot();
        }

        /// <summary>The current scene's clock (zeros without a scene).</summary>
        sealed class PluginClock : ISceneClock
        {
            readonly PluginHost host;
            public PluginClock(PluginHost host) { this.host = host; }
            HS2Next.Scenes.SceneClock C => host.App?.Runtime?.Clock;
            public double Time => C?.Time ?? 0;
            public float FrameDt => C?.FrameDt ?? 0f;
            public bool Paused => C?.Paused ?? false;
            public long Frame => C?.Frame ?? 0;
            public long Steps => C?.Steps ?? 0;
        }

        /// <summary>The shader supply of a host without an engine side: every supply is refused.</summary>
        sealed class NoShaderSupply : IShaderSupply
        {
            public static readonly NoShaderSupply Instance = new NoShaderSupply();
            public bool Supply(string original, string replacement, out string refusal)
            {
                refusal = "shader supplies are not available in this host";
                return false;
            }
            public bool Withdraw(string original) => false;
            public IReadOnlyList<KeyValuePair<string, string>> Supplied => Array.Empty<KeyValuePair<string, string>>();
        }
    }
}
