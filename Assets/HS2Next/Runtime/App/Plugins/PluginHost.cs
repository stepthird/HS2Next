// The plugin loader of the app: scans the run's plugin folder (PluginScan), loads each plugin in order and drives its
// lifecycle - Load with its own context, SceneLoaded after every new or loaded scene (and once for the initial scene),
// Update once per frame from the plugin pump, the typed events after the app's own handlers, and Unload in reverse order
// before the bridge and the scene runtime are disposed. Assemblies load with Unity's CurrentAssemblies.LoadFromPath (or
// Assembly.LoadFrom); one AssemblyResolve handler per domain gives a plugin the assembly of a name the domain already
// holds, else the DLL of that name in a loaded plugin's folder or its lib folder. A loaded image is remembered per path
// for the domain's lifetime, so an Editor play session without a domain reload reuses it (and refuses a plugin whose file
// changed since: enter play mode with a domain reload). Every call into plugin code runs inside the plugin's fault
// wrapper; at the threshold the plugin becomes faulted: its updates, events and other registrations stop, its Unload is
// tried once, and its commands stay and answer plugin_fault. Mono cannot unload assemblies, so an unload is logical: the
// plugin's registrations are removed by owner. While the app runs a plugin can be disabled (its Unload, then the removal of
// everything it registered: commands, ext handlers, events, frame actions, undo entries, engine registrations; no
// Quitting) and enabled again (the load steps on the loaded images, a new instance and context, the open scene's ext
// blocks of its namespaces read again, then SceneLoaded(Initial) and AppReady); a DLL that changed on disk loads only
// after a restart. The capture events of the app (CaptureEvents) reach the loaded plugins as their IAppEvents events.
// A plugin's runtime override slots (IPluginContext.Overrides) are released with its other registrations when it leaves
// loaded (disable, fault, unload at quit); plugin.list shows them per plugin and plugin.disable counts the released.
// The scene post stage: the host hands the attached runtime one call (SceneRuntime.PostStage) while a loaded plugin
// implements IPluginScenePost and takes it back when the last one leaves, so with none the runtime keeps nothing for it.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using CoreOrigin = HS2Next.Scenes.ChangeOrigin;
using ApiOrigin = HS2Next.Api.Plugins.ChangeOrigin;

namespace HS2Next.App.Plugins
{
    /// <summary>How plugin assemblies are loaded.</summary>
    public enum PluginLoadCall
    {
        /// <summary>UnityEngine.Assemblies.CurrentAssemblies.LoadFromPath (the default).</summary>
        LoadFromPath,
        /// <summary>System.Reflection.Assembly.LoadFrom.</summary>
        LoadFrom,
    }

    /// <summary>One assembly of a plugin as loaded.</summary>
    public sealed class PluginAssemblyInfo
    {
        public string Name, Path, Call;
        public double LoadMs;
        /// <summary>The image was already in the domain from an earlier play session (no domain reload).</summary>
        public bool Reused;
    }

    /// <summary>What the engine side of the app adds to a plugin's context (node objects, build hooks, shader supplies, UI).</summary>
    public sealed class PluginEngineParts
    {
        /// <summary>The object the context's GetService returns for the interface types it implements, or null.</summary>
        public object Services;
        /// <summary>The plugin's shader supply, or null (then supplies are refused).</summary>
        public IShaderSupply Shaders;
        /// <summary>Removes what the plugin registered through these parts (after its Unload, or when it faults).</summary>
        public Action Unload;
        /// <summary>The report members of these parts for plugin.list (hooks, supplies, ui), or null.</summary>
        public Func<JObject> Report;
        /// <summary>
        /// Queues a rebuild (at the next apply) of every built scene character whose last build ran one of the plugin's build
        /// hooks; returns how many were queued. Called by the runtime disable after the registrations are removed. Null = none.
        /// </summary>
        public Func<int> RebuildTouched;
        /// <summary>
        /// Queues a rebuild of every built scene character when the plugin has build hooks registered (0 and nothing queued
        /// without hooks); returns how many. Called by the runtime enable after Load. Null = none.
        /// </summary>
        public Func<int> RebuildIfHooked;
        /// <summary>The plugin's declarative forms (IPluginContext.Forms), or null (then the context's Forms is not available).</summary>
        public HS2Next.Api.Ui.IPluginForms Forms;
    }

    /// <summary>One candidate plugin: its scan result and, once loaded, its instance, context and counters.</summary>
    public sealed class PluginEntry
    {
        internal PluginEntry(PluginCandidate c)
        {
            Candidate = c;
            State = c.IsPending ? PluginStates.Pending : c.State;
            Code = c.Code;
            Reason = c.Reason;
        }

        public PluginCandidate Candidate { get; }
        public string Guid => Candidate.Manifest?.Guid ?? Candidate.Guid;
        public PluginManifest Manifest => Candidate.Manifest;
        /// <summary><see cref="PluginStates"/>.</summary>
        public string State { get; internal set; }
        public string Code { get; internal set; }
        public string Reason { get; internal set; }
        public List<PluginAssemblyInfo> Assemblies { get; } = new List<PluginAssemblyInfo>();
        public string EntryType { get; internal set; }
        public IPlugin Instance { get; internal set; }
        public PluginContext Context { get; internal set; }
        public PluginGuard Guard { get; internal set; }
        public PluginEvents Events { get; internal set; }
        public ScopedCommandRegistry Commands { get; internal set; }
        public PluginTimer UpdateTime { get; } = new PluginTimer();
        /// <summary>The time its ScenePost takes (a plugin with IPluginScenePost).</summary>
        public PluginTimer ScenePostTime { get; } = new PluginTimer();
        /// <summary>Loading the assemblies, creating the entry type and Load, ms.</summary>
        public double LoadMs { get; internal set; }
        public bool IsLoaded => State == PluginStates.Loaded;
        internal IPluginUpdate Updater;
        internal IPluginSceneLoaded SceneLoadedHandler;
        internal ScenePostCaller ScenePost;
        internal PluginEngineParts Engine;
        internal bool UnloadTried;
        /// <summary>The candidate was on the disabled list at the scan and has never loaded (an enable runs the skipped checks).</summary>
        internal bool NeverLoaded;
        /// <summary>The last failed runtime enable: "&lt;code&gt;: &lt;reason&gt;", or null.</summary>
        public string EnableError { get; internal set; }
        /// <summary>The runtime override slots released by the last removal of its registrations.</summary>
        public int OverridesReleased { get; internal set; }

        internal void Set(string state, string code, string reason)
        {
            State = state;
            Code = code;
            Reason = reason;
        }
    }

    /// <summary>The outcome of a runtime disable or enable (PluginHost.Disable / Enable).</summary>
    public sealed class PluginToggle
    {
        public string Guid, State;
        /// <summary>Disable: the GUIDs disabled, in that order (dependents first).</summary>
        public readonly List<string> Changed = new List<string>();
        /// <summary>Enable: the ext blocks read again.</summary>
        public int ExtBlocks;
        /// <summary>Characters queued for a rebuild.</summary>
        public int Rebuilt;
        /// <summary>Disable: the runtime override slots released (every plugin disabled).</summary>
        public int Overrides;
        public double Ms;
        /// <summary>Null when it succeeded, else the bridge error to answer.</summary>
        public BridgeError Error;
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Error == null;
    }

    public sealed class PluginHost
    {
        /// <summary>The readiness stage the app declares as required; set once the plugins stage ran (with or without plugins).</summary>
        public const string ReadinessStage = "plugins";

        /// <summary>
        /// The load call of plugin assemblies. The environment variable HS2NEXT_PLUGINS_LOADCALL=LoadFrom selects the other
        /// call for a process (a diagnostic: the loader's first measurement compared both).
        /// </summary>
        public static PluginLoadCall LoadCall = string.Equals(Environment.GetEnvironmentVariable("HS2NEXT_PLUGINS_LOADCALL"), "LoadFrom", StringComparison.OrdinalIgnoreCase)
            ? PluginLoadCall.LoadFrom : PluginLoadCall.LoadFromPath;

        /// <summary>The engine side of a plugin's context (set by the app's engine layer); null = no engine services.</summary>
        public static Func<PluginHost, PluginEntry, PluginEngineParts> EngineFactory;

        // ------------------------------------------------------------ domain-wide state (kept across play sessions without a domain reload)

        sealed class LoadedImage
        {
            public Assembly Assembly;
            public long Size;
            public DateTime WriteUtc;
            public string Call;
            public double Ms;
        }

        static readonly object domainGate = new object();
        static readonly Dictionary<string, LoadedImage> images = new Dictionary<string, LoadedImage>(StringComparer.OrdinalIgnoreCase);
        static readonly List<string> probeDirs = new List<string>();
        static bool resolverInstalled;

        sealed class PluginLoadException : Exception
        {
            public readonly string Code;
            public PluginLoadException(string code, string message) : base(message) { Code = code; }
        }

        // ------------------------------------------------------------ instance

        readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        readonly List<PluginEntry> entries = new List<PluginEntry>();
        readonly List<string> infos = new List<string>();
        readonly HashSet<string> hostGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, (string kind, string parent)> nodeCache = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        readonly HashSet<string> builtIds = new HashSet<string>(StringComparer.Ordinal);
        readonly ScenePostStage scenePost = new ScenePostStage();
        readonly Action<float> scenePostCall;
        AppCore attachedApp;
        SceneSession attachedSession;
        SceneRuntime attachedRuntime;
        bool shutDown, captureHooked;
        SceneLoadInfo lastSceneLoad;

        /// <param name="bridge">The bridge whose registry, frames, readiness and output plugins use (null: a private registry, no listener).</param>
        /// <param name="userData">HS2Next's UserData (plugin data folders live under Plugins/&lt;guid&gt;/).</param>
        /// <param name="app">The app whose scene events and clock plugins see, or null (an Editor host without a scene).</param>
        public PluginHost(BridgeServer bridge, string userData, AppCore app = null)
        {
            App = app;
            UserData = userData;
            Bridge = bridge ?? new BridgeServer(new BridgeOptions { Port = 0, RequiredStages = new string[0], OptionalStages = new string[0], AppVersion = Application.version });
            OwnsBridge = bridge == null;
            scenePostCall = RunScenePost;
        }

        public AppCore App { get; }
        public BridgeServer Bridge { get; }
        /// <summary>The host created its own in-process bridge (no app bridge was given).</summary>
        public bool OwnsBridge { get; }
        public string UserData { get; }
        public PluginLaunch Launch { get; private set; }
        public PluginScanResult Scan { get; private set; }
        /// <summary>Every candidate: the load order first, then the others by GUID.</summary>
        public IReadOnlyList<PluginEntry> Entries => entries;
        /// <summary>The loaded plugins in load order.</summary>
        public IReadOnlyList<PluginEntry> Loaded => entries.Where(e => e.IsLoaded).ToList();
        /// <summary>The whole stage: scan, loads, the initial SceneLoaded, ms.</summary>
        public double TotalMs { get; private set; }
        /// <summary>"N loaded, M refused, K disabled (&lt;root&gt;)" of the last load (the stage line's text).</summary>
        public string Summary { get; private set; } = "not run";
        public bool IsShutDown => shutDown;

        public PluginEntry Find(string guid) => entries.FirstOrDefault(e => string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase));

        /// <summary>The scene session plugins see (the attached app's, else this host's app's), or null.</summary>
        internal SceneSession Session => attachedSession ?? App?.Session;

        bool OnMainThread => Thread.CurrentThread.ManagedThreadId == mainThread;

        // ------------------------------------------------------------ load

        /// <summary>
        /// The plugins stage: scan, validate and order the plugins of <paramref name="launch"/>, load each in order, attach the
        /// event adapter, send SceneLoaded(Initial) and publish the list to bridge.version. Never throws for a plugin's
        /// problem (it is refused or failed and reported); returns the summary line.
        /// </summary>
        public string Load(PluginLaunch launch, bool logSummary = true)
        {
            var sw = Stopwatch.StartNew();
            Launch = launch ?? throw new ArgumentNullException(nameof(launch));
            Bridge.PluginInfo = Info;
            foreach (var s in Bridge.Commands.Specs)
            {
                int dot = s.Name.IndexOf('.');
                hostGroups.Add(dot > 0 ? s.Name.Substring(0, dot) : s.Name);
                if (!string.IsNullOrEmpty(s.Group)) hostGroups.Add(s.Group);
            }
            hostGroups.Add("bridge");
            hostGroups.Add("plugin");
            if (launch.Enabled)
            {
                Scan = PluginScan.Scan(launch.Root, new PluginScanOptions
                {
                    AppVersion = Application.version,
                    HostAssemblies = HostAssemblyNames(),
                    ReservedGroups = hostGroups.Where(g => g.IndexOf(' ') < 0 && g.IndexOf('/') < 0).ToList(),
                    Disabled = launch.Disabled,
                });
                infos.AddRange(Scan.Infos);
                foreach (var c in Scan.Candidates) entries.Add(new PluginEntry(c) { NeverLoaded = c.State == PluginStates.Disabled });
                foreach (var i in Scan.Infos) Debug.Log("[app] " + i);
                foreach (var e in entries.Where(x => !x.Candidate.IsPending))
                    Debug.Log("[app] plugin " + e.Guid + " " + e.State + ": " + e.Code + (e.Reason != null ? ": " + e.Reason : ""));
                if (Scan.LoadOrder.Count > 0) InstallResolver();
                foreach (var e in entries.Where(x => x.Candidate.IsPending).ToList()) LoadOne(e);
            }
            else Scan = new PluginScanResult { Root = launch.Root };
            if (App != null) Attach(App);
            HookCaptures();
            var initial = new SceneLoadInfo(SceneLoadKind.Initial, null, 0, App?.Session?.Model?.Count ?? 0);
            DeliverSceneLoaded(initial);
            TotalMs = sw.Elapsed.TotalMilliseconds;
            int loaded = entries.Count(e => e.IsLoaded), refused = entries.Count(e => e.State == PluginStates.Refused),
                disabled = entries.Count(e => e.State == PluginStates.Disabled), failed = entries.Count(e => e.State == PluginStates.Failed);
            Summary = loaded + " loaded, " + refused + " refused, " + disabled + " disabled" + (failed > 0 ? ", " + failed + " failed" : "") +
                      " (" + (launch.Enabled ? launch.Root : "off: " + launch.Source) + ")";
            if (logSummary) Debug.Log("[app] plugins: " + TotalMs.ToString("F0") + " ms; " + Summary);
            return Summary;
        }

        /// <summary>Simple names of the assemblies in the domain, and in a player those its Managed folder ships.</summary>
        static List<string> HostAssemblyNames()
        {
            var l = new List<string>();
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsPluginImage(a)) continue;
                try { l.Add(a.GetName().Name); }
                catch (Exception) { }
            }
            if (!Application.isEditor)
            {
                try
                {
                    string managed = Path.Combine(Application.dataPath, "Managed");
                    if (Directory.Exists(managed)) l.AddRange(Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension));
                }
                catch (Exception) { }
            }
            return l;
        }

        static bool IsPluginImage(Assembly a)
        {
            lock (domainGate) return images.Values.Any(i => i.Assembly == a);
        }

        void LoadOne(PluginEntry e)
        {
            var sw = Stopwatch.StartNew();
            var m = e.Manifest;
            e.Set(PluginStates.Pending, null, null);
            foreach (var d in m.Dependencies)
            {
                var dep = Find(d.Guid);
                if (dep == null || !dep.IsLoaded)
                {
                    Refuse(e, PluginCodes.Dependency, "the dependency " + d.Guid + " is " + (dep?.State ?? "absent") + (dep?.Code != null ? " (" + dep.Code + ")" : ""));
                    return;
                }
            }
            lock (domainGate) if (!probeDirs.Contains(e.Candidate.Dir, StringComparer.OrdinalIgnoreCase)) probeDirs.Add(e.Candidate.Dir);
            var assemblies = new List<Assembly>();
            try
            {
                foreach (var a in m.Assemblies)
                {
                    string path = PluginScan.FindFile(e.Candidate.Dir, a) ?? Path.Combine(e.Candidate.Dir, a);
                    assemblies.Add(LoadImage(path, e));
                }
            }
            catch (PluginLoadException ex)
            {
                if (ex.Code == PluginCodes.LoadFailed) Fail(e, ex.Code, ex.Message);
                else Refuse(e, ex.Code, ex.Message);
                return;
            }
            var types = new List<Type>();
            foreach (var asm in assemblies)
            {
                try { types.AddRange(asm.GetTypes()); }
                catch (ReflectionTypeLoadException rtle)
                {
                    var msgs = rtle.LoaderExceptions?.Where(x => x != null).Select(x => x.Message).Distinct().Take(3).ToList() ?? new List<string>();
                    Refuse(e, PluginCodes.TypeLoad, asm.GetName().Name + ": types could not load: " + string.Join(" | ", msgs));
                    return;
                }
                catch (Exception ex)
                {
                    Refuse(e, PluginCodes.TypeLoad, asm.GetName().Name + ": " + ex.GetType().Name + ": " + ex.Message);
                    return;
                }
            }
            Type entry;
            if (!string.IsNullOrEmpty(m.EntryType))
            {
                entry = types.FirstOrDefault(t => t.FullName == m.EntryType);
                if (entry == null || !typeof(IPlugin).IsAssignableFrom(entry) || entry.IsAbstract || entry.GetConstructor(Type.EmptyTypes) == null)
                {
                    Refuse(e, PluginCodes.NoEntry, "entryType " + m.EntryType + (entry == null ? " is not in the listed assemblies" : " is not a creatable IPlugin (public, not abstract, a public parameterless constructor)"));
                    return;
                }
            }
            else
            {
                var found = types.Where(t => t.IsPublic && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null).ToList();
                if (found.Count != 1)
                {
                    Refuse(e, PluginCodes.NoEntry, found.Count == 0 ? "no public IPlugin type with a public parameterless constructor" :
                        "several IPlugin types (" + string.Join(", ", found.Select(t => t.FullName)) + "): name one as entryType");
                    return;
                }
                entry = found[0];
            }
            e.EntryType = entry.FullName;
            IPlugin instance;
            try { instance = (IPlugin)Activator.CreateInstance(entry); }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                Fail(e, PluginCodes.LoadFailed, "creating " + entry.FullName + " threw " + inner.GetType().Name + ": " + inner.Message);
                return;
            }
            e.Instance = instance;
            e.Guard = new PluginGuard(e.Guid, s => Debug.LogWarning(s));
            e.Guard.BecameFaulted += () => OnFaulted(e);
            e.Events = new PluginEvents(e.Guard);
            e.Commands = new ScopedCommandRegistry(Bridge.Commands, e.Guid, m.Version, m.CommandPrefixes, e.Guard, () => e.State == PluginStates.Faulted, g => hostGroups.Contains(g));
            e.Context = new PluginContext(this, e);
            try { e.Engine = EngineFactory?.Invoke(this, e); }
            catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": the engine services could not be created: " + ex); }
            // an optional strings.tsv in the plugin folder goes to the UI's string table (keys plugin.<guid>.*) before Load, so the
            // labels the plugin registers resolve at once
            string strings = PluginScan.FindFile(e.Candidate.Dir, "strings.tsv");
            if (strings != null && e.Engine?.Services is HS2Next.Api.Unity.IUnityPluginServices us && us.Ui != null)
                e.Guard.Run("strings", () => us.Ui.AddStrings(File.ReadAllText(strings)));
            try
            {
                e.Guard.Enter();
                try { instance.Load(e.Context); }
                finally { e.Guard.Exit(); }
            }
            catch (Exception ex)
            {
                Fail(e, PluginCodes.LoadFailed, "Load threw " + ex.GetType().Name + ": " + ex.Message);
                Debug.LogWarning("[plugin " + e.Guid + "] Load threw " + ex);
                RemoveRegistrations(e, removeCommands: true);
                e.Context.Invalidate();
                return;
            }
            e.Updater = instance as IPluginUpdate;
            e.SceneLoadedHandler = instance as IPluginSceneLoaded;
            e.ScenePost = instance is IPluginScenePost post ? new ScenePostCaller(post, e.Guard, e.ScenePostTime) : null;
            e.Set(PluginStates.Loaded, null, null);
            SyncScenePost();
            e.LoadMs = sw.Elapsed.TotalMilliseconds;
            Debug.Log("[app] plugin " + e.Guid + " " + m.Version + " loaded in " + e.LoadMs.ToString("F1") + " ms (" + string.Join(", ", e.Assemblies.Select(a => a.Name + " " + a.Call + (a.Reused ? " reused" : "") + " " + a.LoadMs.ToString("F1") + " ms")) +
                      "; " + e.Commands.Names.Count + " commands, " + e.Events.HandlerCount + " event handlers)");
        }

        void Refuse(PluginEntry e, string code, string reason)
        {
            e.Set(PluginStates.Refused, code, reason);
            e.LoadMs = 0;
            Debug.Log("[app] plugin " + e.Guid + " refused: " + code + ": " + reason);
        }

        void Fail(PluginEntry e, string code, string reason)
        {
            e.Set(PluginStates.Failed, code, reason);
            Debug.LogWarning("[app] plugin " + e.Guid + " failed: " + code + ": " + reason);
        }

        /// <summary>Loads one image (or reuses the one this loader loaded from that path in this domain).</summary>
        Assembly LoadImage(string path, PluginEntry e)
        {
            string full = Path.GetFullPath(path);
            var fi = new FileInfo(full);
            if (!fi.Exists) throw new PluginLoadException(PluginCodes.MissingAssembly, "the assembly " + full + " does not exist");
            lock (domainGate)
            {
                if (images.TryGetValue(full, out var img))
                {
                    if (img.Size != fi.Length || img.WriteUtc != fi.LastWriteTimeUtc)
                        throw new PluginLoadException(PluginCodes.StaleAssembly, Path.GetFileName(full) + " changed since this domain loaded it (enter play mode with a domain reload)");
                    e?.Assemblies.Add(new PluginAssemblyInfo { Name = img.Assembly.GetName().Name, Path = full, Call = img.Call, LoadMs = 0, Reused = true });
                    return img.Assembly;
                }
            }
            string simple;
            try { simple = AssemblyName.GetAssemblyName(full).Name; }
            catch (Exception ex) { throw new PluginLoadException(PluginCodes.LoadFailed, Path.GetFileName(full) + " is not a managed assembly: " + ex.Message); }
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n;
                try { n = a.GetName().Name; }
                catch (Exception) { continue; }
                if (string.Equals(n, simple, StringComparison.OrdinalIgnoreCase))
                    throw new PluginLoadException(PluginCodes.AssemblyCollision, "an assembly named " + simple + " is already loaded" + (SafeLocation(a) is string loc && loc.Length > 0 ? " (" + loc + ")" : ""));
            }
            var call = LoadCall;
            var sw = Stopwatch.StartNew();
            Assembly asm;
            try { asm = call == PluginLoadCall.LoadFromPath ? UnityEngine.Assemblies.CurrentAssemblies.LoadFromPath(full) : Assembly.LoadFrom(full); }
            catch (Exception ex) { throw new PluginLoadException(PluginCodes.LoadFailed, Path.GetFileName(full) + ": " + call + " threw " + ex.GetType().Name + ": " + ex.Message); }
            if (asm == null) throw new PluginLoadException(PluginCodes.LoadFailed, Path.GetFileName(full) + ": " + call + " returned no assembly");
            double ms = sw.Elapsed.TotalMilliseconds;
            lock (domainGate) images[full] = new LoadedImage { Assembly = asm, Size = fi.Length, WriteUtc = fi.LastWriteTimeUtc, Call = call.ToString(), Ms = ms };
            e?.Assemblies.Add(new PluginAssemblyInfo { Name = simple, Path = full, Call = call.ToString(), LoadMs = ms });
            return asm;
        }

        static string SafeLocation(Assembly a)
        {
            try { return a.Location; }
            catch (Exception) { return null; }
        }

        static void InstallResolver()
        {
            lock (domainGate)
            {
                if (resolverInstalled) return;
                resolverInstalled = true;
            }
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        /// <summary>The domain's resolver: an assembly of that name already loaded, else &lt;dir&gt;/&lt;name&gt;.dll or &lt;dir&gt;/lib/&lt;name&gt;.dll of the loaded plugins.</summary>
        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name;
            try { name = new AssemblyName(args.Name).Name; }
            catch (Exception) { return null; }
            if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return a; }
                catch (Exception) { }
            }
            List<string> dirs;
            lock (domainGate) dirs = probeDirs.ToList();
            foreach (var d in dirs)
                foreach (var f in new[] { Path.Combine(d, name + ".dll"), Path.Combine(d, "lib", name + ".dll") })
                {
                    if (!File.Exists(f)) continue;
                    lock (domainGate) if (images.TryGetValue(Path.GetFullPath(f), out var img)) return img.Assembly;
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        var asm = LoadCall == PluginLoadCall.LoadFromPath ? UnityEngine.Assemblies.CurrentAssemblies.LoadFromPath(Path.GetFullPath(f)) : Assembly.LoadFrom(Path.GetFullPath(f));
                        var fi = new FileInfo(f);
                        lock (domainGate) images[Path.GetFullPath(f)] = new LoadedImage { Assembly = asm, Size = fi.Length, WriteUtc = fi.LastWriteTimeUtc, Call = LoadCall.ToString(), Ms = sw.Elapsed.TotalMilliseconds };
                        Debug.Log("[app] plugin dependency " + name + " resolved from " + f + " (" + LoadCall + ", " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms)");
                        return asm;
                    }
                    catch (Exception e) { Debug.LogWarning("[app] plugin dependency " + name + " from " + f + " could not load: " + e.Message); }
                }
            return null;
        }

        // ------------------------------------------------------------ frames and events

        /// <summary>
        /// One frame of the plugins (the pump at execution order −7500 in play mode and players; AppCore.Frame in edit-mode
        /// test hosts): each loaded plugin's Update in load order, timed and inside its fault wrapper.
        /// </summary>
        public void UpdateFrame(int frame, float unscaledDt, float sceneDt, double sceneTime, bool paused)
        {
            if (shutDown) return;
            var f = new PluginFrame(frame, unscaledDt, sceneDt, sceneTime, paused);
            foreach (var e in entries.ToArray())
            {
                if (!e.IsLoaded || e.Updater == null) continue;
                long t0 = Stopwatch.GetTimestamp();
                e.Guard.Run("update", () => e.Updater.Update(in f));
                e.UpdateTime.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            }
        }

        /// <summary>
        /// The scene post stage of one frame or one scene step (SceneRuntime.PostStage): every loaded plugin's ScenePost in load
        /// order, timed and inside its fault wrapper. <paramref name="dt"/> is the scene delta of that frame or step, which the
        /// clock has already advanced by, so the frame's SceneTime is the one Update saw.
        /// </summary>
        public void RunScenePost(float dt)
        {
            if (shutDown) return;
            var rt = attachedRuntime;
            bool play = Application.isPlaying;
            scenePost.Run(new PluginFrame(play ? Time.frameCount : Bridge.Frames.Frame, play ? Time.unscaledDeltaTime : dt, dt, rt != null ? rt.Clock.Time - dt : 0, rt != null && rt.Paused));
        }

        /// <summary>The post stage's callers from the plugins now loaded, in load order, and the runtime's call after them.</summary>
        void SyncScenePost()
        {
            scenePost.Set(entries.Where(e => e.IsLoaded && e.ScenePost != null).Select(e => e.ScenePost));
            SetPostStage(scenePost.Any ? scenePostCall : null);
        }

        /// <summary>Hands the attached runtime its post stage; null takes it back.</summary>
        void SetPostStage(Action<float> stage)
        {
            var rt = attachedRuntime;
            if (rt == null) return;
            rt.PostStage = stage;
        }

        /// <summary>Delivers an event to every loaded plugin in load order.</summary>
        public void RaiseAll(PluginEventKind kind, object arg = null)
        {
            if (shutDown && kind != PluginEventKind.Quitting) return;
            foreach (var e in entries.ToArray())
                if (e.IsLoaded) e.Events.Raise(kind, arg);
        }

        /// <summary>Once when the app is ready.</summary>
        public void RaiseAppReady() => RaiseAll(PluginEventKind.AppReady);

        void DeliverSceneLoaded(SceneLoadInfo info)
        {
            lastSceneLoad = info;
            foreach (var e in entries.ToArray())
            {
                if (!e.IsLoaded) continue;
                if (e.SceneLoadedHandler != null) e.Guard.Run("sceneLoaded", () => e.SceneLoadedHandler.SceneLoaded(info));
                if (e.IsLoaded) e.Events.Raise(PluginEventKind.SceneLoaded, info);
            }
        }

        /// <summary>
        /// Delivers the scene and runtime events of <paramref name="app"/> to the loaded plugins (a test entry's host; the app's
        /// own loader attaches itself when it loads). Replaces an earlier attachment.
        /// </summary>
        public void AttachTo(AppCore app)
        {
            Detach();
            if (app != null) Attach(app);
        }

        /// <summary>Subscribes to the app's scene and runtime events (after the app's own handlers, which subscribed earlier).</summary>
        void Attach(AppCore app)
        {
            attachedApp = app;
            attachedSession = app.Session;
            attachedRuntime = app.Runtime;
            if (attachedSession != null)
            {
                FillNodeCache();
                var ev = attachedSession.Events;
                ev.Cleared += OnCleared;
                ev.Loaded += OnLoaded;
                ev.Saving += OnSaving;
                ev.Saved += OnSaved;
                ev.Changed += OnChanged;
                ev.NodeForgotten += OnForgotten;
            }
            if (attachedRuntime != null)
            {
                attachedRuntime.CharaBuilt += OnCharaBuilt;
                attachedRuntime.PausedChanged += OnPaused;
                attachedRuntime.Stepped += OnStepped;
                SyncScenePost();
            }
        }

        void Detach()
        {
            if (attachedSession != null)
            {
                var ev = attachedSession.Events;
                ev.Cleared -= OnCleared;
                ev.Loaded -= OnLoaded;
                ev.Saving -= OnSaving;
                ev.Saved -= OnSaved;
                ev.Changed -= OnChanged;
                ev.NodeForgotten -= OnForgotten;
            }
            if (attachedRuntime != null)
            {
                attachedRuntime.CharaBuilt -= OnCharaBuilt;
                attachedRuntime.PausedChanged -= OnPaused;
                attachedRuntime.Stepped -= OnStepped;
                SetPostStage(null);
            }
            attachedSession = null;
            attachedRuntime = null;
            attachedApp = null;
        }

        static string KindName(NodeKind k) => k.ToString().ToLowerInvariant();

        void FillNodeCache()
        {
            nodeCache.Clear();
            var model = attachedSession?.Model;
            if (model == null) return;
            foreach (var n in model.PreOrder()) nodeCache[n.Id] = (KindName(n.Kind), n.Parent?.Id);
        }

        void OnCleared()
        {
            builtIds.Clear();
            RaiseAll(PluginEventKind.SceneCleared);
        }

        void OnLoaded(SceneLoadedArgs a)
        {
            FillNodeCache();
            string path = a.Path;
            var sceneDir = (attachedApp ?? App)?.Paths?.SceneDir;
            if (path != null && sceneDir != null)
            {
                try
                {
                    string full = Path.GetFullPath(path), dir = Path.GetFullPath(sceneDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    path = full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ? full.Substring(dir.Length).Replace('\\', '/') : full;
                }
                catch (Exception) { }
            }
            DeliverSceneLoaded(new SceneLoadInfo(a.IsNew ? SceneLoadKind.New : SceneLoadKind.Load, path, a.Findings?.Count ?? 0, attachedSession?.Model?.Count ?? 0));
        }

        void OnSaving() => RaiseAll(PluginEventKind.SceneSaving);

        void OnSaved(SaveResult r) => RaiseAll(PluginEventKind.SceneSaved, new SceneSaveResult(r != null && r.Ok, r?.Size ?? 0, r?.Error?.Message));

        static ApiOrigin OriginOf(CoreOrigin o) => o switch
        {
            CoreOrigin.Undo => ApiOrigin.Undo,
            CoreOrigin.Redo => ApiOrigin.Redo,
            CoreOrigin.Preview => ApiOrigin.Preview,
            CoreOrigin.Cancel => ApiOrigin.Cancel,
            CoreOrigin.Capture => ApiOrigin.Capture,
            CoreOrigin.Normalize => ApiOrigin.Normalize,
            _ => ApiOrigin.Command,
        };

        void OnChanged(SceneChangeBatch b)
        {
            List<NodeRef> added = null, removed = null, moved = null;
            var model = attachedSession?.Model;
            foreach (var c in b.Changes)
            {
                switch (c.Kind)
                {
                    case SceneChangeKind.NodeAdded:
                    {
                        var n = model?.Find(c.NodeId);
                        if (n != null) nodeCache[c.NodeId] = (KindName(n.Kind), n.Parent?.Id);
                        nodeCache.TryGetValue(c.NodeId, out var k);
                        (added ??= new List<NodeRef>()).Add(new NodeRef(c.NodeId, k.kind, k.parent));
                        break;
                    }
                    case SceneChangeKind.NodeRemoved:
                    {
                        nodeCache.TryGetValue(c.NodeId, out var k);
                        (removed ??= new List<NodeRef>()).Add(new NodeRef(c.NodeId, k.kind, c.OldParentId ?? k.parent));
                        break;
                    }
                    case SceneChangeKind.NodeMoved:
                    {
                        var n = model?.Find(c.NodeId);
                        if (n != null) nodeCache[c.NodeId] = (KindName(n.Kind), n.Parent?.Id);
                        nodeCache.TryGetValue(c.NodeId, out var k);
                        (moved ??= new List<NodeRef>()).Add(new NodeRef(c.NodeId, k.kind, k.parent));
                        break;
                    }
                }
            }
            var origin = OriginOf(b.Origin);
            if (added != null) RaiseAll(PluginEventKind.NodesAdded, new NodeChangeInfo(added, origin));
            if (removed != null) RaiseAll(PluginEventKind.NodesRemoved, new NodeChangeInfo(removed, origin));
            if (moved != null) RaiseAll(PluginEventKind.NodesMoved, new NodeChangeInfo(moved, origin));
        }

        void OnForgotten(string id)
        {
            nodeCache.Remove(id);
            builtIds.Remove(id);
            RaiseAll(PluginEventKind.NodeForgotten, id);
        }

        void OnCharaBuilt(CharaRealization c) => RaiseAll(PluginEventKind.CharaBuilt, new CharaBuiltInfo(c.NodeId, !builtIds.Add(c.NodeId)));

        void OnPaused(bool p) => RaiseAll(PluginEventKind.PausedChanged, p);

        void OnStepped(int n, float dt) => RaiseAll(PluginEventKind.SceneStepped, new StepInfo(n, dt));

        // ------------------------------------------------------------ faults and unload

        void OnFaulted(PluginEntry e)
        {
            if (!OnMainThread)
            {
                Bridge.Frames.AtUpdate(1, () => OnFaulted(e));
                return;
            }
            if (e.State != PluginStates.Loaded) return;
            e.Set(PluginStates.Faulted, null, "faulted after " + e.Guard.Threshold + " faults (last: " + e.Guard.LastError + ")");
            Debug.LogWarning("[app] plugin " + e.Guid + " faulted: its updates, events and registrations stop; its commands answer plugin_fault");
            e.Events.Clear();
            TryUnload(e);
            RemoveRegistrations(e, removeCommands: false);
        }

        void TryUnload(PluginEntry e)
        {
            if (e.UnloadTried || e.Instance == null) return;
            e.UnloadTried = true;
            try { e.Instance.Unload(); }
            catch (Exception ex) { Debug.LogWarning("[plugin " + e.Guid + "] Unload threw " + ex); }
        }

        /// <summary>Removes what the plugin registered through its context (commands only when asked: a faulted plugin's stay).</summary>
        void RemoveRegistrations(PluginEntry e, bool removeCommands)
        {
            if (removeCommands)
            {
                try { e.Commands?.UnregisterAll(); }
                catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": removing its commands threw " + ex.Message); }
            }
            try
            {
                ExtRegistry.Default.UnregisterOwner(e.Guid);
            }
            catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": removing its ext handlers threw " + ex.Message); }
            e.Events?.Clear();
            // handlers the plugin added to the frame clock's events (its scheduled actions are dropped by its context's view)
            e.Context?.FramesView?.Clear();
            // the plugin's undo entries leave both lists at once (the others keep their order)
            try { Session?.Commands.RemoveOwner(e.Guid); }
            catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": removing its undo entries threw " + ex.Message); }
            if (e.Engine?.Unload != null)
            {
                try { e.Engine.Unload(); }
                catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": removing its engine registrations threw " + ex.Message); }
            }
            e.Updater = null;
            e.SceneLoadedHandler = null;
            e.ScenePost?.Close();
            e.ScenePost = null;
            SyncScenePost();
            // its runtime override slots: the model values show again in this frame
            try { e.OverridesReleased = App?.Runtime != null && !App.Runtime.IsDisposed ? App.Runtime.Overrides.ReleaseOwner(e.Guid) : 0; }
            catch (Exception ex) { e.OverridesReleased = 0; Debug.LogWarning("[app] plugin " + e.Guid + ": releasing its runtime overrides threw " + ex.Message); }
        }

        /// <summary>
        /// The app quits (or an Editor play session ends, or a test host is disposed): Quitting to every loaded plugin, then
        /// in reverse load order Unload and the removal of its registrations (a faulted plugin's commands too). Idempotent.
        /// </summary>
        public void Shutdown()
        {
            if (shutDown) return;
            RaiseAll(PluginEventKind.Quitting);
            shutDown = true;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.State != PluginStates.Loaded && e.State != PluginStates.Faulted) continue;
                TryUnload(e);
                RemoveRegistrations(e, removeCommands: true);
                e.Context?.Invalidate();
                e.Set(PluginStates.Unloaded, null, e.State == PluginStates.Faulted ? e.Reason : null);
                Debug.Log("[app] plugin " + e.Guid + " unloaded");
            }
            Detach();
            UnhookCaptures();
            if (OwnsBridge) Bridge.Dispose();
        }

        // ------------------------------------------------------------ capture events

        void HookCaptures()
        {
            if (captureHooked) return;
            captureHooked = true;
            CaptureEvents.Starting += OnCaptureStarting;
            CaptureEvents.Rendered += OnCaptureRendered;
            CaptureEvents.Finished += OnCaptureFinished;
        }

        void UnhookCaptures()
        {
            if (!captureHooked) return;
            captureHooked = false;
            CaptureEvents.Starting -= OnCaptureStarting;
            CaptureEvents.Rendered -= OnCaptureRendered;
            CaptureEvents.Finished -= OnCaptureFinished;
        }

        void OnCaptureStarting(CaptureInfo i) => RaiseAll(PluginEventKind.CaptureStarting, i);
        void OnCaptureRendered(CaptureInfo i) => RaiseAll(PluginEventKind.CaptureRendered, i);
        void OnCaptureFinished(CaptureResultInfo r) => RaiseAll(PluginEventKind.CaptureFinished, r);

        // ------------------------------------------------------------ runtime disable and enable

        static bool Active(PluginEntry e) => e.State == PluginStates.Loaded || e.State == PluginStates.Faulted;

        /// <summary>The loaded (or faulted) plugins that depend on <paramref name="e"/>, directly or through each other, in reverse load order.</summary>
        List<PluginEntry> DependentsOf(PluginEntry e)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { e.Guid };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var x in entries)
                {
                    if (x == e || set.Contains(x.Guid) || !Active(x) || x.Manifest == null) continue;
                    if (x.Manifest.Dependencies.Any(d => set.Contains(d.Guid)))
                    {
                        set.Add(x.Guid);
                        grew = true;
                    }
                }
            }
            var l = new List<PluginEntry>();
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i] != e && set.Contains(entries[i].Guid)) l.Add(entries[i]);
            return l;
        }

        static BridgeError Unsupported(string message, IDictionary<string, object> extra = null) =>
            new BridgeError(BridgeCodes.Unsupported, message, extra == null ? null : new BridgeErrorDetail { Extra = extra });

        static BridgeError Busy(PluginEntry e) =>
            BridgeError.Busy("the plugin " + e.Guid + "'s code is running now (e.g. its own command asked this); try again later, e.g. from a scheduled frame action");

        /// <summary>
        /// Disables a loaded or faulted plugin while the app runs: its Unload (not for a faulted plugin, whose Unload was tried
        /// when it faulted; no Quitting), then everything it registered is removed as at quit (commands, ext handlers, events,
        /// frame clock handlers and scheduled actions, undo entries, hooks, providers, supplies, UI and forms, runtime skin
        /// layers, strings), its context closes and its state becomes disabled; characters its build hooks touched are queued
        /// for a rebuild. With <paramref name="withDependents"/> the loaded plugins that depend on it are disabled first, in
        /// reverse load order; without it they refuse the disable. Busy while the plugin's (or a dependent's) code runs. Main
        /// thread.
        /// </summary>
        public PluginToggle Disable(string guid, bool withDependents = false)
        {
            var sw = Stopwatch.StartNew();
            var r = new PluginToggle { Guid = guid };
            if (!OnMainThread) throw new InvalidOperationException("plugins are disabled on the main thread");
            var e = Find(guid);
            if (e == null) { r.Error = BridgeError.NotFound("no plugin " + guid + " in this run's plugin folder", "guid"); return r; }
            r.Guid = e.Guid;
            r.State = e.State;
            if (shutDown || !Active(e))
            {
                r.Error = Unsupported("the plugin " + e.Guid + " is " + (shutDown ? "unloaded (the app is quitting)" : e.State) + "; only a loaded or faulted plugin can be disabled",
                    new Dictionary<string, object> { ["state"] = shutDown ? PluginStates.Unloaded : e.State });
                return r;
            }
            var deps = DependentsOf(e);
            if (deps.Count > 0 && !withDependents)
            {
                var names = deps.Select(x => x.Guid).ToList();
                r.Error = Unsupported("loaded plugins depend on " + e.Guid + ": " + string.Join(", ", names) + " (pass withDependents to disable them too)",
                    new Dictionary<string, object> { ["state"] = e.State, ["dependents"] = names });
                return r;
            }
            var all = new List<PluginEntry>(deps) { e };
            var running = all.FirstOrDefault(x => (x.Guard?.Depth ?? 0) > 0);
            if (running != null) { r.Error = Busy(running); return r; }
            foreach (var x in all) r.Rebuilt += DisableOne(x, r);
            r.State = e.State;
            r.Ms = sw.Elapsed.TotalMilliseconds;
            Debug.Log("[app] plugin " + e.Guid + " disabled at run time (" + r.Ms.ToString("F1") + " ms" +
                      (r.Changed.Count > 1 ? "; with " + string.Join(", ", r.Changed.Take(r.Changed.Count - 1)) : "") + ")");
            return r;
        }

        /// <summary>The disable steps for one plugin; the number of characters queued for a rebuild.</summary>
        int DisableOne(PluginEntry e, PluginToggle r)
        {
            if (e.State == PluginStates.Loaded) TryUnload(e);
            RemoveRegistrations(e, removeCommands: true);
            e.Context?.Invalidate();
            e.Set(PluginStates.Disabled, PluginCodes.Disabled, "disabled at run time");
            r?.Changed.Add(e.Guid);
            if (r != null) r.Overrides += e.OverridesReleased;
            int n = 0;
            try { n = e.Engine?.RebuildTouched?.Invoke() ?? 0; }
            catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": queuing the rebuilds of its characters threw " + ex.Message); }
            return n;
        }

        /// <summary>
        /// Enables a disabled plugin while the app runs (also one disabled at start, a faulted one after the rest of the
        /// disable steps, and one whose Load threw): its dependencies must be loaded; a plugin that never loaded first runs the
        /// scan's checks it skipped; then the load steps on the images already in the process (a DLL that changed on disk is
        /// refused with plugin.staleAssembly and the plugin stays disabled: it loads after a restart), a new instance with a
        /// new context, Load; then its ext handlers read the open scene's blocks of their namespaces again, it gets
        /// SceneLoaded(Initial) with the open scene's path and node count and then AppReady, and when it registered build
        /// hooks every built scene character is queued for a rebuild. The entry keeps its place in the order. Main thread.
        /// </summary>
        public PluginToggle Enable(string guid)
        {
            var sw = Stopwatch.StartNew();
            var r = new PluginToggle { Guid = guid };
            if (!OnMainThread) throw new InvalidOperationException("plugins are enabled on the main thread");
            var e = Find(guid);
            if (e == null) { r.Error = BridgeError.NotFound("no plugin " + guid + " in this run's plugin folder", "guid"); return r; }
            r.Guid = e.Guid;
            r.State = e.State;
            bool accepted = e.State == PluginStates.Disabled || e.State == PluginStates.Faulted ||
                            (e.State == PluginStates.Failed && e.Code == PluginCodes.LoadFailed);
            if (shutDown || !accepted || e.Manifest == null)
            {
                r.Error = Unsupported("the plugin " + e.Guid + " is " + (shutDown ? "unloaded (the app is quitting)" : e.State + (e.Code != null ? " (" + e.Code + ")" : "")) +
                    "; only a disabled, faulted or failed (plugin.loadFailed) plugin can be enabled", new Dictionary<string, object> { ["state"] = shutDown ? PluginStates.Unloaded : e.State });
                return r;
            }
            if ((e.Guard?.Depth ?? 0) > 0) { r.Error = Busy(e); return r; }
            if (e.State == PluginStates.Faulted) DisableOne(e, null);
            string code = null, reason = null;
            foreach (var d in e.Manifest.Dependencies)
            {
                var dep = Find(d.Guid);
                if (dep == null || !dep.IsLoaded)
                {
                    code = PluginCodes.Dependency;
                    reason = "the dependency " + d.Guid + " is " + (dep?.State ?? "absent") + (dep?.Code != null ? " (" + dep.Code + ")" : "");
                    break;
                }
            }
            if (code == null && e.NeverLoaded)
            {
                var held = entries.Where(x => x != e && Active(x) && x.Manifest != null)
                    .SelectMany(x => x.Manifest.CommandPrefixes.Select(p => new KeyValuePair<string, string>(p, x.Guid))).ToList();
                code = PluginScan.Revalidate(e.Candidate, new PluginScanOptions
                {
                    AppVersion = Application.version,
                    HostAssemblies = HostAssemblyNames(),
                    ReservedGroups = hostGroups.Where(g => g.IndexOf(' ') < 0 && g.IndexOf('/') < 0).ToList(),
                }, held, out reason);
            }
            if (code == null)
            {
                InstallResolver();
                e.UnloadTried = false;
                e.Assemblies.Clear();
                e.EntryType = null;
                LoadOne(e);
                if (!e.IsLoaded)
                {
                    code = e.Code ?? PluginCodes.LoadFailed;
                    reason = code == PluginCodes.StaleAssembly ? "the DLL changed since it was loaded: restart HS2Next to load it (" + e.Reason + ")" : e.Reason;
                    // a refusal leaves the plugin disabled; a Load that threw leaves it failed (plugin.loadFailed)
                    if (e.State != PluginStates.Failed) e.Set(PluginStates.Disabled, PluginCodes.Disabled, e.NeverLoaded ? "on the disabled list (plugins.disabled)" : "disabled at run time");
                }
            }
            if (code != null)
            {
                e.EnableError = code + ": " + reason;
                r.State = e.State;
                r.Error = new BridgeError(BridgeCodes.Unavailable, "the plugin " + e.Guid + " cannot be enabled: " + code + ": " + reason,
                    new BridgeErrorDetail { Extra = new Dictionary<string, object> { ["code"] = code, ["reason"] = reason, ["state"] = e.State } });
                r.Ms = sw.Elapsed.TotalMilliseconds;
                Debug.Log("[app] plugin " + e.Guid + " not enabled: " + code + ": " + reason);
                return r;
            }
            e.EnableError = null;
            e.NeverLoaded = false;
            r.ExtBlocks = ReadExtAgain(e);
            var info = new SceneLoadInfo(SceneLoadKind.Initial, lastSceneLoad?.Path, lastSceneLoad?.Findings ?? 0, Session?.Model?.Count ?? 0);
            if (e.IsLoaded && e.SceneLoadedHandler != null) e.Guard.Run("sceneLoaded", () => e.SceneLoadedHandler.SceneLoaded(info));
            if (e.IsLoaded) e.Events.Raise(PluginEventKind.SceneLoaded, info);
            if (e.IsLoaded) e.Events.Raise(PluginEventKind.AppReady);
            try { r.Rebuilt = e.IsLoaded ? e.Engine?.RebuildIfHooked?.Invoke() ?? 0 : 0; }
            catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": queuing the rebuilds of the characters threw " + ex.Message); }
            if (SemVer.TryParseMajorMinor(e.Manifest.ApiVersion, out int maj, out int min) && maj == 1 && min == 0)
                r.Warnings.Add("built against Api 1.0, whose plugins expect one Load per process: check that it starts cleanly");
            r.State = e.State;
            r.Ms = sw.Elapsed.TotalMilliseconds;
            Debug.Log("[app] plugin " + e.Guid + " enabled at run time (" + r.Ms.ToString("F1") + " ms; " + r.ExtBlocks + " ext blocks read again, " + r.Rebuilt + " characters to rebuild)");
            return r;
        }

        /// <summary>The open scene's blocks of the plugin's namespaces, read again by its new handlers; the count read.</summary>
        int ReadExtAgain(PluginEntry e)
        {
            var session = Session;
            if (session == null) return 0;
            var ns = ExtRegistry.Default.NamespacesOf(e.Guid);
            if (ns.Count == 0) return 0;
            var findings = new FindingList();
            int n;
            try { n = session.ReadExtAgain(ns.ToList(), ExtRegistry.Default, findings); }
            catch (Exception ex)
            {
                Debug.LogWarning("[app] plugin " + e.Guid + ": reading the scene's ext blocks again threw " + ex);
                return 0;
            }
            foreach (var f in findings)
                if (f.Severity != FindingSeverity.Info) Debug.LogWarning("[app] plugin " + e.Guid + " ext read: " + f.Code + " " + f.Path + ": " + f.Message);
            return n;
        }

        // ------------------------------------------------------------ reports

        /// <summary>bridge.version's plugins: every candidate with its version and state.</summary>
        public IReadOnlyList<BridgePluginInfo> Info() =>
            entries.Select(e => new BridgePluginInfo { Guid = e.Guid, Version = e.Manifest?.Version, State = e.State }).ToList();

        /// <summary>The plugin.list reply.</summary>
        /// <summary>A plugin's runtime override slots for plugin.list: {key, path, priority, hasValue, winning, effective, targetExists}.</summary>
        JArray OverridesJson(string guid)
        {
            var a = new JArray();
            var rt = App?.Runtime;
            if (rt == null || rt.IsDisposed) return a;
            foreach (var s in rt.Overrides.Slots(null, guid))
                a.Add(new JObject
                {
                    ["key"] = s.NodeId, ["path"] = s.Path, ["priority"] = s.Priority, ["hasValue"] = s.HasValue,
                    ["winning"] = s.IsWinning, ["effective"] = s.IsEffective, ["targetExists"] = s.TargetExists,
                });
            return a;
        }

        public JObject ListJson(bool diagnostics = false)
        {
            var arr = new JArray();
            foreach (var e in entries)
            {
                var m = e.Manifest;
                var o = new JObject
                {
                    ["guid"] = e.Guid,
                    ["name"] = m?.Name,
                    ["version"] = m?.Version,
                    ["apiVersion"] = m?.ApiVersion,
                    ["dir"] = e.Candidate.Dir,
                    ["state"] = e.State,
                };
                if (e.Code != null || e.Reason != null) o["reason"] = (e.Code ?? "") + (e.Code != null && e.Reason != null ? ": " : "") + (e.Reason ?? "");
                o["assemblies"] = new JArray(e.Assemblies.Select(a => new JObject { ["name"] = a.Name, ["path"] = a.Path, ["call"] = a.Call, ["loadMs"] = Math.Round(a.LoadMs, 3), ["reused"] = a.Reused }));
                o["entryType"] = e.EntryType;
                o["commands"] = new JArray((e.Commands?.Names ?? (IReadOnlyList<string>)Array.Empty<string>()).ToArray());
                o["extNamespaces"] = new JArray(ExtRegistry.Default.NamespacesOf(e.Guid).ToArray());
                o["events"] = e.Events?.HandlerCount ?? 0;
                o["history"] = Session?.Commands.CountOwner(e.Guid) ?? 0;
                o["overrides"] = OverridesJson(e.Guid);
                JObject eng = null;
                try { eng = e.Engine?.Report?.Invoke(); }
                catch (Exception ex) { Debug.LogWarning("[app] plugin " + e.Guid + ": engine report threw " + ex.Message); }
                o["hooks"] = eng?["hooks"] ?? new JArray();
                o["supplies"] = eng?["supplies"] ?? 0;
                o["ui"] = eng?["ui"] ?? new JObject { ["panels"] = 0, ["sections"] = 0, ["actions"] = 0 };
                if (eng != null)
                    foreach (var p in eng.Properties())
                        if (o[p.Name] == null) o[p.Name] = p.Value;
                var faults = new JObject();
                if (e.Guard != null)
                    foreach (var kv in e.Guard.ByHook().OrderBy(k => k.Key, StringComparer.Ordinal)) faults[kv.Key] = kv.Value;
                o["faults"] = faults;
                if (e.Guard?.LastError != null) o["lastError"] = e.Guard.LastError;
                if (e.EnableError != null) o["enableError"] = e.EnableError;
                o["updateMs"] = new JObject { ["median"] = Math.Round(e.UpdateTime.Median, 4), ["max"] = Math.Round(e.UpdateTime.Max, 4), ["frames"] = e.UpdateTime.Count };
                if (e.Instance is IPluginScenePost)
                    o["scenePostMs"] = new JObject { ["median"] = Math.Round(e.ScenePostTime.Median, 4), ["max"] = Math.Round(e.ScenePostTime.Max, 4), ["frames"] = e.ScenePostTime.Count };
                o["loadMs"] = Math.Round(e.LoadMs, 3);
                if (m != null && m.Unknown.Count > 0) o["unknown"] = m.Unknown.DeepClone();
                arr.Add(o);
            }
            var r = new JObject
            {
                ["root"] = Launch?.Root,
                ["enabled"] = Launch?.Enabled ?? false,
                ["source"] = Launch?.Source,
                ["loadCall"] = LoadCall.ToString(),
                ["loadMs"] = Math.Round(TotalMs, 3),
                ["plugins"] = arr,
            };
            if (infos.Count > 0) r["infos"] = new JArray(infos.ToArray());
            if (diagnostics) r["diagnostics"] = Diagnostics();
            return r;
        }

        /// <summary>Counts for leak rows: managed bytes after a full collection and the engine objects of each kind.</summary>
        public static JObject Diagnostics()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return new JObject
            {
                ["managedBytes"] = GC.GetTotalMemory(true),
                ["gameObjects"] = Resources.FindObjectsOfTypeAll<GameObject>().Length,
                ["materials"] = Resources.FindObjectsOfTypeAll<Material>().Length,
                ["meshes"] = Resources.FindObjectsOfTypeAll<Mesh>().Length,
                ["textures"] = Resources.FindObjectsOfTypeAll<Texture>().Length,
                ["renderTextures"] = Resources.FindObjectsOfTypeAll<RenderTexture>().Length,
            };
        }
    }
}
