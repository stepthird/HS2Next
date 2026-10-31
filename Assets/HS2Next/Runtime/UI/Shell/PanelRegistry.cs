// The UI registries: panels, actions and
// inspector sections, internal in 3.5 and shaped so that 5.1 can expose them. Packages C and I (and later plugins) add
// their panels from an IUiModule: every non-abstract class implementing it with a public parameterless constructor is
// found by reflection over the app assembly when the UI starts (UiModules.Discover; edit-mode tests use the same list),
// ordered by Order then type name, and its Register(ctx) is called once after the shell's own actions exist and before
// the layout is built.
//
//   public sealed class WorkspaceModule : IUiModule
//   {
//       public int Order => 100;
//       public void Register(UiContext ctx) =>
//           ctx.Registry.Panels.Register("workspace", "panel.workspace", LayoutRegions.Left, c => new WorkspacePanel(c), ActionScope.Tree);
//   }
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI
{
    /// <summary>A UI extension (C's tree / browsers / files, I's inspectors / scene panel, plugins later).</summary>
    public interface IUiModule
    {
        /// <summary>Registration order (lower first); the shell's own module is 0.</summary>
        int Order { get; }
        void Register(UiContext ctx);
    }

    public static class UiModules
    {
        static List<Type> types;

        /// <summary>Module types of the app assembly in registration order.</summary>
        public static IReadOnlyList<Type> Types => types ??= Discover();

        static List<Type> Discover()
        {
            var l = new List<Type>();
            Type[] all;
            try { all = typeof(IUiModule).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { all = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in all)
                if (typeof(IUiModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null) l.Add(t);
            return l;
        }

        /// <summary>New instances of every module, ordered by Order then full type name.</summary>
        public static List<IUiModule> Create()
        {
            var l = new List<IUiModule>();
            foreach (var t in Types)
            {
                try { l.Add((IUiModule)Activator.CreateInstance(t)); }
                catch (Exception e) { Debug.LogError("[ui] module " + t.FullName + " could not be created: " + e.Message); }
            }
            return l.OrderBy(m => m.Order).ThenBy(m => m.GetType().FullName, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>One registered panel.</summary>
    public sealed class PanelDef
    {
        public string Id;
        /// <summary>l10n key of the tab title.</summary>
        public string TitleKey;
        /// <summary>LayoutRegions.Left / Right / Bottom.</summary>
        public string DefaultRegion;
        /// <summary>Builds the panel's content (called once, lazily when its tab is first shown, or by tests off-panel).</summary>
        public Func<UiContext, VisualElement> Factory;
        /// <summary>Shortcut scope while focus is inside the panel.</summary>
        public ActionScope Scope = ActionScope.Global;
        /// <summary>Order among the panels of the default region (registration order when equal).</summary>
        public int Order;
        public override string ToString() => Id + "@" + DefaultRegion;
    }

    public sealed class PanelRegistry
    {
        readonly List<PanelDef> order = new List<PanelDef>();
        readonly Dictionary<string, PanelDef> byId = new Dictionary<string, PanelDef>(StringComparer.Ordinal);
        public event Action<PanelDef> Registered;
        /// <summary>A panel was removed (a plugin's unload): the shell drops its tab.</summary>
        public event Action<PanelDef> Unregistered;

        /// <summary>
        /// Registers a panel (Register(id, titleKey, defaultRegion, factory)). A second registration of the same id
        /// replaces the first. Panels registered after the shell was built appear at once in their region.
        /// </summary>
        public PanelDef Register(string id, string titleKey, string defaultRegion, Func<UiContext, VisualElement> factory,
            ActionScope scope = ActionScope.Global, int sortOrder = 0)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("panel id");
            if (!LayoutRegions.IsRegion(defaultRegion)) throw new ArgumentException("not a region: " + defaultRegion);
            var d = new PanelDef { Id = id, TitleKey = titleKey ?? "panel." + id, DefaultRegion = defaultRegion, Factory = factory, Scope = scope, Order = sortOrder };
            if (byId.TryGetValue(id, out var old)) order.Remove(old);
            byId[id] = d;
            order.Add(d);
            UiContext.Guard(() => Registered?.Invoke(d));
            return d;
        }

        /// <summary>Removes a panel (a plugin's unload); true when it was registered.</summary>
        public bool Unregister(string id)
        {
            if (id == null || !byId.TryGetValue(id, out var d)) return false;
            byId.Remove(id);
            order.Remove(d);
            UiContext.Guard(() => Unregistered?.Invoke(d));
            return true;
        }

        public bool TryGet(string id, out PanelDef d) => byId.TryGetValue(id ?? "", out d);
        public IReadOnlyList<PanelDef> All => order;

        /// <summary>The defaults for LayoutDocument.Normalize: the spec's default assignment first, then every other panel.</summary>
        public List<PanelDefault> Defaults()
        {
            var l = new List<PanelDefault>();
            foreach (var p in LayoutDocument.DefaultPanels)
                l.Add(byId.TryGetValue(p.Id, out var d) ? new PanelDefault(p.Id, d.DefaultRegion) : p);
            foreach (var d in order.OrderBy(x => x.Order))
                if (!l.Any(x => x.Id == d.Id)) l.Add(new PanelDefault(d.Id, d.DefaultRegion));
            return l;
        }
    }

    /// <summary>An inspector section registered for a node kind (I's tabs; plugins in 5.1).</summary>
    public sealed class InspectorSectionDef
    {
        public string Id;
        /// <summary>Node kind name ("chara", "item", "light", "camera", "folder", "route", "scene").</summary>
        public string Kind;
        public string TitleKey;
        public int Order;
        public Func<UiContext, VisualElement> Factory;
    }

    public sealed class UiRegistry
    {
        public PanelRegistry Panels { get; } = new PanelRegistry();
        public ActionRegistry Actions { get; } = new ActionRegistry();
        public List<InspectorSectionDef> InspectorSections { get; } = new List<InspectorSectionDef>();

        public InspectorSectionDef RegisterInspectorSection(string kind, string id, string titleKey, Func<UiContext, VisualElement> factory, int order = 0)
        {
            InspectorSections.RemoveAll(s => s.Kind == kind && s.Id == id);
            var d = new InspectorSectionDef { Kind = kind, Id = id, TitleKey = titleKey, Factory = factory, Order = order };
            InspectorSections.Add(d);
            return d;
        }

        /// <summary>Removes the section <paramref name="id"/> of <paramref name="kind"/> (a plugin's unload); true when there was one.
        /// An inspector already showing it keeps it until it is rebuilt.</summary>
        public bool UnregisterInspectorSection(string kind, string id) => InspectorSections.RemoveAll(s => s.Kind == kind && s.Id == id) > 0;

        public IEnumerable<InspectorSectionDef> SectionsFor(string kind) => InspectorSections.Where(s => s.Kind == kind).OrderBy(s => s.Order);

        /// <summary>Runs an action by id (menus, toolbar, ui.action); a failure to run (unknown / unavailable) returns the status.</summary>
        public ActionRunStatus Run(string id, string source = "menu", IReadOnlyDictionary<string, object> args = null) =>
            RunWith(id, new ActionInvocation { Source = source, Args = args ?? ActionInvocation.NoArgs }, out _);

        public ActionRunStatus RunWith(string id, ActionInvocation call, out object result)
        {
            result = null;
            try { return Actions.Run(id, call, out result); }
            catch (Exception e)
            {
                Debug.LogError("[ui] action " + id + " threw: " + e);
                result = e.Message;
                return ActionRunStatus.Ran;
            }
        }
    }
}
