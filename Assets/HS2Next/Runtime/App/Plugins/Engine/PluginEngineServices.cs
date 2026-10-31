// The engine-side services of one plugin (the host side of HS2Next.Api.Unity, and the shader supply of HS2Next.Api): the
// realized objects of scene nodes and characters, build hooks, typed material overrides, the additive mosaic, shader
// supplies and the experimental UI registration. Everything a plugin registers here is owned by its GUID: build hooks and
// FK node providers under "<guid>/<id>" (so the build statistics and the build log name the plugin), supplies on the
// shader map's owner stack, UI entries under "<guid>/<id>"; Unload removes all of it (mosaic members a plugin added stay).
// Every call into plugin code goes through the plugin's fault wrapper. The mosaic is guarded as in the Core points: the
// overrides refuse members and mosaic materials, there is no hook stage right after the mosaic, the mosaic material of every
// member slot is put back after each plugin hook at Final, the supply refuses the mosaic shaders, and a refusal or a
// re-assert is a warning api.mosaicProtected, one line in the plugin's log and a count in the plugin's report (not a
// fault). Main thread only.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;
using ApiOverride = HS2Next.Api.Unity.OverrideResult;
using CoreOverride = HS2Next.Chara.Mosaic.OverrideResult;

namespace HS2Next.App.Plugins.Engine
{
    /// <summary>The engine-side services and the shader supply of one plugin (handed out by its context).</summary>
    public sealed class PluginEngineServices : IUnityPluginServices, IShaderSupply
    {
        readonly PluginGuard guard;
        readonly Action<string> logInfo, logWarning;
        readonly Func<SceneRuntime> runtime;
        readonly Func<UiContext> uiContext;
        readonly HookRegistry hooks;
        readonly OverrideAdapter materials;
        PluginUiServices ui;
        readonly HashSet<Renderer> loggedRefusals = new HashSet<Renderer>();
        int mosaicProtected;
        bool unloaded;

        /// <param name="guid">The plugin's GUID (the owner of everything registered here).</param>
        /// <param name="guard">The plugin's fault wrapper.</param>
        /// <param name="logInfo">The plugin's log (info lines).</param>
        /// <param name="logWarning">The plugin's log (warnings).</param>
        /// <param name="runtime">The current scene runtime, or null (node lookups then give null).</param>
        /// <param name="ui">The UI context, or null when the UI does not run (Ui is then null).</param>
        public PluginEngineServices(string guid, PluginGuard guard, Action<string> logInfo, Action<string> logWarning, Func<SceneRuntime> runtime, Func<UiContext> ui)
        {
            if (string.IsNullOrEmpty(guid)) throw new ArgumentException("a plugin GUID is needed", nameof(guid));
            Guid = guid;
            this.guard = guard ?? new PluginGuard(guid, logWarning);
            this.logInfo = logInfo ?? (_ => { });
            this.logWarning = logWarning ?? (_ => { });
            this.runtime = runtime ?? (() => null);
            uiContext = ui ?? (() => null);
            hooks = new HookRegistry(this);
            materials = new OverrideAdapter(this);
        }

        public string Guid { get; }

        /// <summary>The prefix of the ids registered for this plugin ("&lt;guid&gt;/").</summary>
        public string Prefix => Guid + "/";

        // ------------------------------------------------------------ report (plugin.list)

        /// <summary>Refusals of the overrides and mosaic re-asserts after this plugin's hooks.</summary>
        public int MosaicProtected => mosaicProtected;

        /// <summary>The plugin's hooks as "&lt;id&gt;@&lt;stage&gt;" in run order (ids without the GUID prefix).</summary>
        public List<string> Hooks
        {
            get
            {
                var l = new List<string>();
                foreach (var h in CharaBuildHooks.OwnedBy(Guid)) l.Add(Unprefix(h.Id) + "@" + h.Stage);
                return l;
            }
        }

        /// <summary>Supplies in place.</summary>
        public int SupplyCount => ShaderMap.SuppliedBy(Guid).Count;

        /// <summary>UI registrations: panels, inspector sections, actions, strings (zero without the UI).</summary>
        public (int Panels, int Sections, int Actions, int Strings) UiCounts => ui == null ? (0, 0, 0, 0) : ui.Counts;

        public bool Unloaded => unloaded;

        /// <summary>
        /// Removes everything the plugin registered here: hooks and FK node providers, supplies (each withdrawn, restoring what
        /// was mapped below it), panels, sections, actions and strings. Mosaic members it added stay. Idempotent.
        /// </summary>
        public void Unload()
        {
            if (unloaded) return;
            unloaded = true;
            CharaBuildHooks.UnregisterOwner(Guid);
            ShaderMap.WithdrawAll(Guid);
            ui?.UnregisterAll();
        }

        void CheckLoaded()
        {
            if (unloaded) throw new ObjectDisposedException("plugin " + Guid + " is unloaded");
        }

        internal string Unprefix(string id) => id != null && id.StartsWith(Prefix, StringComparison.Ordinal) ? id.Substring(Prefix.Length) : id;

        // ------------------------------------------------------------ IUnityPluginServices

        public GameObject NodeObject(string nodeId)
        {
            CheckLoaded();
            var r = runtime()?.Find(nodeId);
            return r != null && r.HasHolder ? r.HolderObject : null;
        }

        public GameObject CharaRoot(string nodeId)
        {
            CheckLoaded();
            var c = runtime()?.FindChara(nodeId);
            return c != null && c.State == RealizationState.Built && c.Root ? c.Root : null;
        }

        public IBuildHooks BuildHooks { get { CheckLoaded(); return hooks; } }

        public IMaterialOverrides Materials { get { CheckLoaded(); return materials; } }

        public bool AddMosaic(Renderer renderer)
        {
            CheckLoaded();
            return MosaicSet.Add(renderer);
        }

        public IPluginUi Ui
        {
            get
            {
                CheckLoaded();
                var c = uiContext();
                if (c == null) return null;
                if (ui == null || ui.Context != c) ui = new PluginUiServices(this, c, ui);
                return ui;
            }
        }

        // ------------------------------------------------------------ IShaderSupply

        bool IShaderSupply.Supply(string original, string replacement, out string refusal)
        {
            CheckLoaded();
            bool ok = ShaderMap.SupplyOwned(Guid, original, replacement, out refusal);
            if (!ok && refusal != null && refusal.StartsWith(MosaicSet.ProtectedCode, StringComparison.Ordinal))
            {
                mosaicProtected++;
                logWarning("[plugin " + Guid + "] shader supply " + original + " -> " + replacement + " refused: " + refusal);
            }
            return ok;
        }

        bool IShaderSupply.Withdraw(string original)
        {
            CheckLoaded();
            return ShaderMap.Withdraw(Guid, original);
        }

        IReadOnlyList<KeyValuePair<string, string>> IShaderSupply.Supplied => ShaderMap.SuppliedBy(Guid);

        // ------------------------------------------------------------ shared helpers

        /// <summary>The scene node whose realization holds <paramref name="t"/> (the innermost holder), or null.</summary>
        internal string NodeIdOf(Transform t)
        {
            var rt = runtime();
            if (rt == null || !t) return null;
            string best = null;
            int depth = -1;
            foreach (var r in rt.Realizations)
            {
                if (!r.HasHolder || !t.IsChildOf(r.Holder)) continue;
                int d = Depth(r.Holder);
                if (d > depth) { depth = d; best = r.NodeId; }
            }
            return best;
        }

        /// <summary>The scene node of a character, or null (a character outside a scene): the realization that holds the
        /// context, or — during its build, before the realization has the context — the one whose stored documents the build
        /// reads.</summary>
        internal string NodeIdOf(CharaContext ctx)
        {
            var rt = runtime();
            if (rt == null || ctx == null) return null;
            foreach (var r in rt.Realizations)
                if (r is CharaRealization c && c.Context == ctx) return c.NodeId;
            if (ctx.Chara != null)
                foreach (var r in rt.Realizations)
                    if (r is CharaRealization c && c.Node?.Appearance?.Stored?.Chara == ctx.Chara) return c.NodeId;
            return null;
        }

        static int Depth(Transform t)
        {
            int d = 0;
            for (; t; t = t.parent) d++;
            return d;
        }

        /// <summary>A refusal or a re-assert: counted, one log line per renderer, a finding on the node (once per node).</summary>
        internal void Protected(Renderer r, string what, string nodeId)
        {
            mosaicProtected++;
            if (r && loggedRefusals.Add(r))
                logWarning("[plugin " + Guid + "] " + what + " on " + (r ? r.name : "a renderer") + " refused: the mosaic set is protected");
            runtime()?.ReportOnce(FindingSeverity.Warning, MosaicSet.ProtectedCode, nodeId, "plugin " + Guid + ": " + what + " on a mosaic member refused");
        }

        internal void Reasserted(string hookId, string memberPath)
        {
            mosaicProtected++;
            logWarning("[plugin " + Guid + "] hook " + Unprefix(hookId) + " left the mosaic member " + memberPath + " without its mosaic material; put back");
        }

        internal T Run<T>(string hook, Func<T> f, T fallback) => guard.Run(hook, f, fallback);

        internal bool Run(string hook, Action a) => guard.Run(hook, a);

        internal void Fault(string hook, Exception e) => guard.Count(hook, e);

        internal bool Faulted => guard.Faulted;

        internal void Info(string line) => logInfo("[plugin " + Guid + "] " + line);

        // ============================================================ build hooks

        sealed class HookRegistry : IBuildHooks
        {
            readonly PluginEngineServices s;
            public HookRegistry(PluginEngineServices s) { this.s = s; }

            static HookStage StageOf(BuildStage stage)
            {
                switch (stage)
                {
                    case BuildStage.BodyMesh: return HookStage.BodyMesh;
                    case BuildStage.Textures: return HookStage.Textures;
                    case BuildStage.Final: return HookStage.Final;
                    default: throw new ArgumentException("not a build stage of the Api: " + (int)stage, nameof(stage));
                }
            }

            public void Register(string id, BuildStage stage, Action<IBuildHookArgs> run, int order = 0)
            {
                s.CheckLoaded();
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("a hook needs an id", nameof(id));
                if (run == null) throw new ArgumentNullException(nameof(run));
                var st = StageOf(stage);
                string full = s.Prefix + id;
                string hookName = "hook " + id + "@" + stage;
                CharaBuildHooks.RegisterOwned(s.Guid, full, st, a =>
                {
                    if (s.Faulted || s.unloaded) return;
                    try { run(new HookArgsView(s, a, id)); }
                    catch (Exception e)
                    {
                        s.Fault(hookName, e);
                        throw;      // the build counts it in its statistics and goes on
                    }
                }, order, path => s.Reasserted(full, path));
            }

            public bool Unregister(string id, BuildStage stage)
            {
                s.CheckLoaded();
                return id != null && CharaBuildHooks.Unregister(s.Prefix + id, StageOf(stage));
            }

            public void RegisterFkNodes(string id, Func<IReadOnlyList<FkNodeRow>> provider)
            {
                s.CheckLoaded();
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("a provider needs an id", nameof(id));
                if (provider == null) throw new ArgumentNullException(nameof(provider));
                CharaBuildHooks.RegisterFkNodesOwned(s.Guid, s.Prefix + id, catalog =>
                {
                    if (s.Faulted || s.unloaded) return null;
                    var rows = s.Run("fk nodes " + id, () => provider(), null);
                    if (rows == null) return null;
                    var set = new FkNodeSet();
                    var l = new List<FkRow>(rows.Count);
                    foreach (var r in rows)
                        l.Add(new FkRow { No = r.No, Name = r.Name, GroupColumn = r.GroupColumn, Level = r.Level, SyncNo = r.SyncNo, Additional = true });
                    set.Rows = l;
                    return set;
                });
            }
        }

        /// <summary>One hook run as the plugin sees it (valid during the call).</summary>
        sealed class HookArgsView : IBuildHookArgs
        {
            readonly PluginEngineServices s;
            readonly HookArgs a;
            List<Renderer> renderers;
            string nodeId;
            bool nodeDone;

            public HookArgsView(PluginEngineServices s, HookArgs a, string id)
            {
                this.s = s;
                this.a = a;
                HookId = id;
            }

            CharaContext Ctx => a.Chara;

            public string HookId { get; }
            public BuildStage Stage => (BuildStage)(int)a.Stage;
            public bool InBuild => a.InBuild;

            public string NodeId
            {
                get
                {
                    if (!nodeDone) { nodeId = s.NodeIdOf(Ctx); nodeDone = true; }
                    return nodeId;
                }
            }

            public bool IsMale => Ctx != null && Ctx.IsMale;
            public GameObject Root => Ctx?.Root;

            public IReadOnlyList<Renderer> Renderers
            {
                get
                {
                    if (renderers != null) return renderers;
                    renderers = new List<Renderer>();
                    var seen = new HashSet<Renderer>();
                    void Add(GameObject go)
                    {
                        if (!go) return;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                            if (seen.Add(r)) renderers.Add(r);
                    }
                    if (Ctx != null)
                    {
                        Add(Ctx.Body);
                        Add(Ctx.Head);
                        foreach (var c in Ctx.Clothes) Add(c);
                        foreach (var h in Ctx.Hair) Add(h);
                        foreach (var slot in Ctx.Accessories) Add(slot?.Object);
                    }
                    return renderers;
                }
            }

            public Transform FindBone(string name)
            {
                if (name == null || Ctx == null) return null;
                if (Ctx.BodyBones != null && Ctx.BodyBones.TryGetValue(name, out var b) && b) return b;
                if (Ctx.HeadBones != null && Ctx.HeadBones.TryGetValue(name, out var h) && h) return h;
                return null;
            }

            public Transform ImplantBone(string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale) =>
                CharaBuildHooks.ImplantBone(Ctx, name, parent, localPosition, localRotation, localScale);

            public bool IsMosaicMember(Renderer renderer) => MosaicSet.IsMember(renderer);

            public IMaterialOverrides Materials => s.materials;

            public void RenderersChanged(int n = 1) => a.RenderersChanged(n);

            public void Log(string line) => a.Log(line);
        }

        // ============================================================ material overrides

        sealed class OverrideAdapter : IMaterialOverrides
        {
            readonly PluginEngineServices s;
            public OverrideAdapter(PluginEngineServices s) { this.s = s; }

            ApiOverride Result(CoreOverride res, Renderer r, string what)
            {
                if (res == CoreOverride.Protected) s.Protected(r, what, r ? s.NodeIdOf(r.transform) : null);
                return (ApiOverride)(int)res;
            }

            public ApiOverride SetFloat(Renderer renderer, int slot, string property, float value) =>
                Result(MaterialOverrides.SetFloat(renderer, slot, property, value), renderer, "float " + property);

            public ApiOverride SetColor(Renderer renderer, int slot, string property, Color value) =>
                Result(MaterialOverrides.SetColor(renderer, slot, property, value), renderer, "colour " + property);

            public ApiOverride SetTexture(Renderer renderer, int slot, string property, Texture value) =>
                Result(MaterialOverrides.SetTexture(renderer, slot, property, value), renderer, "texture " + property);

            public ApiOverride SetShader(Renderer renderer, int slot, Shader shader) =>
                Result(MaterialOverrides.SetShader(renderer, slot, shader), renderer, "shader");

            public ApiOverride SetEnabled(Renderer renderer, bool enabled) =>
                Result(MaterialOverrides.SetEnabled(renderer, enabled), renderer, "enabled");
        }

        // ============================================================ UI

        sealed class PluginUiServices : IPluginUi
        {
            readonly PluginEngineServices s;
            readonly List<string> panels = new List<string>(), actions = new List<string>();
            readonly List<(string kind, string id)> sections = new List<(string, string)>();
            readonly SortedDictionary<string, string[]> strings = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

            public PluginUiServices(PluginEngineServices s, UiContext ctx, PluginUiServices previous)
            {
                this.s = s;
                Context = ctx;
                if (previous != null) foreach (var kv in previous.strings) strings[kv.Key] = kv.Value;
            }

            public UiContext Context { get; }

            public (int Panels, int Sections, int Actions, int Strings) Counts => (panels.Count, sections.Count, actions.Count, strings.Count);

            VisualElement Failed(string what, Exception e)
            {
                var l = new Label("plugin " + s.Guid + ": " + what + " failed (" + (e?.GetType().Name ?? "no content") + ")");
                l.AddToClassList("hs-plugin-error");
                return l;
            }

            public void RegisterPanel(string id, string titleKey, string region, Func<VisualElement> factory, int order = 0)
            {
                s.CheckLoaded();
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("a panel needs an id", nameof(id));
                if (!LayoutRegions.IsRegion(region)) throw new ArgumentException("not a region (left, right, bottom): " + region, nameof(region));
                if (factory == null) throw new ArgumentNullException(nameof(factory));
                string full = s.Prefix + id;
                Context.Registry.Panels.Register(full, titleKey ?? full, region, c => Build("panel " + id, factory), ActionScope.Global, order);
                if (!panels.Contains(full)) panels.Add(full);
            }

            VisualElement Build(string what, Func<VisualElement> factory)
            {
                VisualElement v = null;
                Exception err = null;
                try { v = factory(); }
                catch (Exception e)
                {
                    err = e;
                    s.Fault(what, e);
                }
                return v ?? Failed(what, err);
            }

            public void RegisterInspectorSection(string kind, string id, string titleKey, Func<IUiSectionContext, VisualElement> factory, int order = 0)
            {
                s.CheckLoaded();
                if (string.IsNullOrEmpty(kind)) throw new ArgumentException("a section needs a node kind", nameof(kind));
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("a section needs an id", nameof(id));
                if (factory == null) throw new ArgumentNullException(nameof(factory));
                string full = s.Prefix + id;
                Context.Registry.RegisterInspectorSection(kind, full, titleKey ?? full,
                    c => Build("section " + id, () => factory(new SectionContext(c.Selection.Primary, kind))), order);
                if (!sections.Contains((kind, full))) sections.Add((kind, full));
            }

            public void RegisterAction(string id, string labelKey, Action run)
            {
                s.CheckLoaded();
                if (string.IsNullOrEmpty(id)) throw new ArgumentException("an action needs an id", nameof(id));
                if (run == null) throw new ArgumentNullException(nameof(run));
                string full = s.Prefix + id;
                Context.Registry.Actions.RegisterPlugin(new ActionDef(full, labelKey ?? full, inv =>
                {
                    if (!s.Faulted && !s.unloaded) s.Run("action " + id, run);
                    return null;
                }));
                if (!actions.Contains(full)) actions.Add(full);
            }

            public void AddStrings(string tsv)
            {
                s.CheckLoaded();
                var t = L10nTable.Parse(tsv ?? "");
                if (t.Problems.Count > 0) throw new ArgumentException("strings: " + string.Join("; ", t.Problems));
                string prefix = "plugin." + s.Guid + ".";
                foreach (var k in t.Keys)
                    if (!k.StartsWith(prefix, StringComparison.Ordinal)) throw new ArgumentException("string key " + k + " does not start with " + prefix);
                foreach (var k in t.Keys)
                {
                    var row = new string[L10nTable.CoreLanguages.Count];
                    for (int i = 0; i < row.Length; i++) row[i] = t.Cell(k, L10nTable.CoreLanguages[i]);
                    strings[k] = row;
                }
                PluginStrings.Set(s.Guid, strings);
            }

            public void UnregisterAll()
            {
                foreach (var p in panels) Context.Registry.Panels.Unregister(p);
                foreach (var (kind, id) in sections) Context.Registry.UnregisterInspectorSection(kind, id);
                foreach (var a in actions) Context.Registry.Actions.Unregister(a);
                panels.Clear();
                sections.Clear();
                actions.Clear();
                if (strings.Count > 0) PluginStrings.Set(s.Guid, null);
                strings.Clear();
            }
        }

        sealed class SectionContext : IUiSectionContext
        {
            public SectionContext(string nodeId, string kind) { NodeId = nodeId; Kind = kind; }
            public string NodeId { get; }
            public string Kind { get; }
        }
    }

    /// <summary>
    /// The plugins' strings in the app's localiser: the app table is rebuilt with every plugin's rows after its own (keys
    /// "plugin.&lt;guid&gt;.*" only, which no app key uses). The app's own rows are kept as the table holds them; nothing is
    /// changed while no plugin adds strings.
    /// </summary>
    public static class PluginStrings
    {
        static readonly SortedDictionary<string, SortedDictionary<string, string[]>> byPlugin =
            new SortedDictionary<string, SortedDictionary<string, string[]>>(StringComparer.Ordinal);
        static L10nTable appTable;

        /// <summary>Replaces the rows of plugin <paramref name="guid"/> (null removes them) and reloads the localiser.</summary>
        public static void Set(string guid, IDictionary<string, string[]> rows)
        {
            var current = L10n.Default.Table;
            if (appTable == null || !IsMerged(current)) appTable = current;
            if (rows == null || rows.Count == 0) byPlugin.Remove(guid);
            else byPlugin[guid] = new SortedDictionary<string, string[]>(rows, StringComparer.Ordinal);
            if (byPlugin.Count == 0)
            {
                L10n.Default.Load(appTable);
                merged = null;
                return;
            }
            merged = L10nTable.Parse(Text(appTable));
            L10n.Default.Load(merged);
        }

        static L10nTable merged;

        static bool IsMerged(L10nTable t) => merged != null && ReferenceEquals(t, merged);

        static string Text(L10nTable app)
        {
            var langs = new List<string>(app.Languages);
            foreach (var l in L10nTable.CoreLanguages) if (!langs.Contains(l)) langs.Add(l);
            var sb = new StringBuilder();
            sb.Append("key");
            foreach (var l in langs) sb.Append('\t').Append(l);
            sb.Append("\tnote\n");
            foreach (var k in app.Keys)
            {
                sb.Append(k);
                foreach (var l in langs) sb.Append('\t').Append(Cell(app.Cell(k, l)));
                sb.Append('\t').Append(app.Note(k)).Append('\n');
            }
            foreach (var p in byPlugin.Values)
                foreach (var kv in p)
                {
                    if (app.Has(kv.Key)) continue;
                    sb.Append(kv.Key);
                    foreach (var l in langs)
                    {
                        int i = IndexOf(L10nTable.CoreLanguages, l);
                        sb.Append('\t').Append(Cell(i >= 0 && i < kv.Value.Length ? kv.Value[i] : null));
                    }
                    sb.Append("\t\n");
                }
            return sb.ToString();
        }

        static int IndexOf(IReadOnlyList<string> l, string v)
        {
            for (int i = 0; i < l.Count; i++) if (l[i] == v) return i;
            return -1;
        }

        static string Cell(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            string c = L10nTable.Escape(v);
            return v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"' ? "\"" + c.Replace("\"", "\"\"") + "\"" : c;
        }
    }
}
