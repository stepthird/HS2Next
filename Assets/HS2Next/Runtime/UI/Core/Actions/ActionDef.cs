// Runtime action entries: an ActionDef = a manifest entry + behaviour. Package A's shell
// registers one ActionDef per manifest id (and C / I register panel actions); menus, toolbar, the key router, the
// key-binding overlay and the bridge's `ui.action` look actions up here. Unity-free.
using System;
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    /// <summary>One call of an action.</summary>
    public sealed class ActionInvocation
    {
        public static readonly IReadOnlyDictionary<string, object> NoArgs = new Dictionary<string, object>();

        /// <summary>Arguments (`ui.action {id, args}`; menus pass none).</summary>
        public IReadOnlyDictionary<string, object> Args = NoArgs;
        /// <summary>Where the call came from (key, menu, toolbar, bridge, …).</summary>
        public string Source;
        /// <summary>Held actions: seconds since the last poll and the speed factor of extra modifiers (Shift ×5, Ctrl ×0.2).</summary>
        public double DeltaTime, Speed = 1;
        /// <summary>Key repeat.</summary>
        public bool Repeat;

        public object Arg(string name) => Args != null && Args.TryGetValue(name, out var v) ? v : null;
    }

    /// <summary>An action with behaviour. Execute returns an optional result object (the `ui.action` reply's `result`).</summary>
    public sealed class ActionDef
    {
        public readonly string Id;
        public readonly string LabelKey;
        public readonly ActionScope Scope;
        public readonly ActionKind Kind;
        public readonly bool TextAllowed;
        public readonly bool Repeatable;
        public Func<ActionInvocation, object> Execute;
        /// <summary>Null = always available.</summary>
        public Func<bool> CanExecute;
        /// <summary>Toggle actions: the current state for menus / toolbar (null = not a toggle).</summary>
        public Func<bool> IsChecked;

        public ActionDef(ActionInfo info, Func<ActionInvocation, object> execute, Func<bool> canExecute = null)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            Id = info.Id;
            LabelKey = info.LabelKey;
            Scope = info.Scope;
            Kind = info.Kind;
            TextAllowed = info.TextAllowed;
            Repeatable = info.Repeatable;
            Execute = execute;
            CanExecute = canExecute;
        }

        /// <summary>An entry for a manifest id.</summary>
        public ActionDef(string manifestId, Func<ActionInvocation, object> execute, Func<bool> canExecute = null)
            : this(ActionManifest.Get(manifestId), execute, canExecute) { }

        /// <summary>An entry outside the manifest (a plugin's action, <see cref="ActionRegistry.RegisterPlugin"/>): a trigger
        /// with its own label key, available in every scope, not while a text field has focus.</summary>
        public ActionDef(string id, string labelKey, Func<ActionInvocation, object> execute, Func<bool> canExecute = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("an action needs an id", nameof(id));
            Id = id;
            LabelKey = labelKey ?? id;
            Scope = ActionScope.Global;
            Kind = ActionKind.Trigger;
            Execute = execute;
            CanExecute = canExecute;
        }

        public bool Available => CanExecute == null || CanExecute();

        public override string ToString() => Id;
    }

    /// <summary>Outcome of <see cref="ActionRegistry.Run"/>.</summary>
    public enum ActionRunStatus { Ran, Unknown, Unavailable, NoBehaviour }

    /// <summary>The registry of runtime actions (ids must be manifest ids unless <see cref="AllowUnlisted"/>).</summary>
    public sealed class ActionRegistry
    {
        readonly Dictionary<string, ActionDef> defs = new Dictionary<string, ActionDef>(StringComparer.Ordinal);
        readonly List<string> order = new List<string>();

        /// <summary>Accept ids outside the manifest (plugins in 5.1); false in 3.5.</summary>
        public bool AllowUnlisted;

        public event Action<ActionDef> Registered;
        /// <summary>An action was removed (a plugin's unload).</summary>
        public event Action<ActionDef> Unregistered;

        /// <summary>The separator of plugin action ids: "&lt;guid&gt;/&lt;id&gt;" (never in a manifest id).</summary>
        public const char PluginSeparator = '/';

        /// <summary>
        /// Registers a plugin's action: its id has the form "&lt;guid&gt;/&lt;id&gt;" (ArgumentException otherwise), which no
        /// manifest id has, so the manifest check of <see cref="Register(ActionDef)"/> does not apply; a second registration of
        /// the id replaces the first.
        /// </summary>
        public void RegisterPlugin(ActionDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            int slash = def.Id.IndexOf(PluginSeparator);
            if (slash <= 0 || slash == def.Id.Length - 1) throw new ArgumentException("a plugin action id is <guid>/<id>: " + def.Id);
            if (!defs.ContainsKey(def.Id)) order.Add(def.Id);
            defs[def.Id] = def;
            Registered?.Invoke(def);
        }

        /// <summary>Removes an action (a plugin's unload); true when it was registered.</summary>
        public bool Unregister(string id)
        {
            if (id == null || !defs.TryGetValue(id, out var d)) return false;
            defs.Remove(id);
            order.Remove(id);
            Unregistered?.Invoke(d);
            return true;
        }

        public void Register(ActionDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!AllowUnlisted && !ActionManifest.Contains(def.Id)) throw new ArgumentException("not a manifest action: " + def.Id);
            if (!defs.ContainsKey(def.Id)) order.Add(def.Id);
            defs[def.Id] = def;
            Registered?.Invoke(def);
        }

        /// <summary>Shorthand: registers a manifest id with its behaviour.</summary>
        public ActionDef Register(string id, Func<ActionInvocation, object> execute, Func<bool> canExecute = null)
        {
            var d = new ActionDef(id, execute, canExecute);
            Register(d);
            return d;
        }

        public bool TryGet(string id, out ActionDef def) => defs.TryGetValue(id ?? "", out def);

        public bool Contains(string id) => id != null && defs.ContainsKey(id);

        /// <summary>Registered actions in registration order.</summary>
        public IEnumerable<ActionDef> All
        {
            get { foreach (var id in order) yield return defs[id]; }
        }

        /// <summary>Runs an action: Unknown when not registered, Unavailable when CanExecute is false, NoBehaviour when it has
        /// no Execute delegate (registered for its label only).</summary>
        public ActionRunStatus Run(string id, ActionInvocation call, out object result)
        {
            result = null;
            if (!TryGet(id, out var d)) return ActionRunStatus.Unknown;
            if (!d.Available) return ActionRunStatus.Unavailable;
            if (d.Execute == null) return ActionRunStatus.NoBehaviour;
            result = d.Execute(call ?? new ActionInvocation());
            return ActionRunStatus.Ran;
        }
    }
}
