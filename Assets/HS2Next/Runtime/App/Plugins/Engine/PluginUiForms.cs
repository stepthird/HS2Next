// The plugin host's declarative forms (IPluginForms of the plugin API, experimental): a registration is checked and copied
// (PluginFormRules: an ArgumentException registers nothing), kept under the plugin's GUID ("<guid>/<id>") and, while the
// Studio UI of this app runs, added to the UI registry as a panel of its region or as a section of the inspector of its node
// kind, whose content is a PluginFormView; without the UI the forms are kept and counted (plugin.list ui.forms) but not
// drawn. The drawn forms call the plugin's commands inline through the app's bridge (each command handler runs in the
// plugin's fault wrapper), record their undo entries through the plugin's history and read again after a new or loaded
// scene. Unload (the plugin leaves the loaded state) removes every form: a panel loses its tab, a drawn section leaves the
// inspector. Main thread only.
using System;
using System.Collections.Generic;
using System.Threading;
using HS2Next.Api.Plugins;
using HS2Next.Api.Ui;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Plugins;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.App.Plugins.Engine
{
    public sealed class PluginUiForms : IPluginForms, IPluginFormHost
    {
        readonly PluginHost host;
        readonly PluginEntry entry;
        readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        readonly Dictionary<string, FormDef> forms = new Dictionary<string, FormDef>(StringComparer.Ordinal);
        readonly List<string> order = new List<string>();
        readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        UiContext subscribed;
        bool unloaded;

        public PluginUiForms(PluginHost host, PluginEntry entry)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
        }

        public string Guid => entry.Guid;

        /// <summary>The registered forms (drawn or not).</summary>
        public int Count => forms.Count;

        /// <summary>The registered forms in registration order (tests, reports).</summary>
        public IReadOnlyList<FormDef> Forms
        {
            get
            {
                var l = new List<FormDef>();
                foreach (var id in order) l.Add(forms[id]);
                return l;
            }
        }

        public PluginFormRegistry Registry { get; } = new PluginFormRegistry();

        /// <summary>The UI context of this host's app (null: no Studio UI, or another app's).</summary>
        UiContext Ui
        {
            get
            {
                var app = host.App;
                var h = UiHost.Current;
                return app != null && h != null && h.Context != null && h.Context.App == app ? h.Context : null;
            }
        }

        public bool Available => !unloaded && Ui != null;

        void Check()
        {
            if (unloaded) throw new ObjectDisposedException("plugin " + Guid + " is unloaded");
            if (Thread.CurrentThread.ManagedThreadId != mainThread) throw new InvalidOperationException("the plugin forms are used on the main thread only");
        }

        public void Panel(string id, string titleKey, string region, UiForm form, int order = 0) => Add(true, region, id, titleKey, form, order);

        public void Section(string kind, string id, string titleKey, UiForm form, int order = 0) => Add(false, kind, id, titleKey, form, order);

        void Add(bool panel, string place, string id, string titleKey, UiForm form, int sortOrder)
        {
            Check();
            var def = PluginFormRules.Validate(Guid, entry.Manifest?.CommandPrefixes, panel, place, id, titleKey, form, sortOrder, Taken);
            forms[def.Id] = def;
            order.Add(def.Id);
            Draw(def);
        }

        /// <summary>A full id is taken by a form of this plugin or by a panel or section of the UI registry.</summary>
        bool Taken(string full)
        {
            string prefix = Guid + "/";
            if (full.StartsWith(prefix, StringComparison.Ordinal) && forms.ContainsKey(full.Substring(prefix.Length))) return true;
            var ui = Ui;
            if (ui == null) return false;
            if (ui.Registry.Panels.TryGet(full, out _)) return true;
            foreach (var s in ui.Registry.InspectorSections) if (s.Id == full) return true;
            return false;
        }

        void Draw(FormDef def)
        {
            var ui = Ui;
            if (ui == null) return;
            Subscribe(ui);
            if (def.IsPanel)
                ui.Registry.Panels.Register(def.FullId, def.TitleKey ?? def.FullId, def.Place, c => Scrolled(def, new PluginFormView(c, this, def, null)), ActionScope.Global, def.Order);
            else
                ui.Registry.RegisterInspectorSection(def.Place, def.FullId, def.TitleKey ?? def.FullId, c => new PluginFormView(c, this, def, c.Selection.Primary), def.Order);
        }

        /// <summary>A panel's form in its own scroll view (a section scrolls with the inspector).</summary>
        static VisualElement Scrolled(FormDef def, PluginFormView view)
        {
            var sv = new ScrollView { name = def.ElementName + "-scroll" };
            sv.AddToClassList("hs-grow");
            sv.Add(view);
            return sv;
        }

        void Undraw(FormDef def)
        {
            var ui = subscribed ?? Ui;
            if (ui != null)
            {
                if (def.IsPanel) ui.Registry.Panels.Unregister(def.FullId);
                else ui.Registry.UnregisterInspectorSection(def.Place, def.FullId);
            }
            Registry.Drop(def.FullId);
        }

        void Subscribe(UiContext ui)
        {
            if (subscribed == ui) return;
            Unsubscribe();
            subscribed = ui;
            ui.SceneLoaded += OnSceneLoaded;
            ui.SceneCleared += OnSceneCleared;
        }

        void Unsubscribe()
        {
            if (subscribed == null) return;
            subscribed.SceneLoaded -= OnSceneLoaded;
            subscribed.SceneCleared -= OnSceneCleared;
            subscribed = null;
        }

        void OnSceneLoaded(HS2Next.Scenes.SceneLoadedArgs a) => Registry.RefreshAll();

        void OnSceneCleared() => Registry.RefreshAll();

        public bool Remove(string id)
        {
            Check();
            if (id == null || !forms.TryGetValue(id, out var def)) return false;
            forms.Remove(id);
            order.Remove(id);
            Undraw(def);
            return true;
        }

        public void Refresh(string id, string nodeId = null)
        {
            Check();
            if (id != null && forms.TryGetValue(id, out var def)) Registry.Refresh(def.FullId, nodeId);
        }

        /// <summary>Removes every form (the plugin leaves the loaded state). Idempotent.</summary>
        public void Unload()
        {
            if (unloaded) return;
            unloaded = true;
            foreach (var id in order.ToArray())
            {
                try { Undraw(forms[id]); }
                catch (Exception e) { Debug.LogWarning("[plugin " + Guid + "] removing the form " + id + " threw " + e.GetType().Name + ": " + e.Message); }
            }
            forms.Clear();
            order.Clear();
            Unsubscribe();
        }

        // ------------------------------------------------------------ IPluginFormHost

        public bool Live => !unloaded && entry.IsLoaded && entry.State != PluginStates.Faulted;

        public string Call(string envelope) => host.Bridge.RunNow(envelope);

        public HistoryRecordResult Record(string label, Action undo, Action redo, string mergeKey)
        {
            if (!Live) return HistoryRecordResult.Busy;
            try { return entry.Context.History.Record(label, undo, redo, mergeKey); }
            catch (ObjectDisposedException) { return HistoryRecordResult.Busy; }
        }

        public void Warn(string onceKey, string message)
        {
            if (onceKey != null && !warned.Add(onceKey)) return;
            Debug.LogWarning("[plugin " + Guid + "] " + message);
        }

        public void Toast(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Ui?.Toasts?.Show(L10n.Format("plugins.form.failed", entry.Manifest?.Name ?? Guid, text), ToastKind.Error);
        }
    }
}
