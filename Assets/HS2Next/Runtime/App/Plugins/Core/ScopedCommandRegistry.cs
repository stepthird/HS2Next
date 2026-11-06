// A plugin's view of the bridge's command registry (Unity-free). A command name and each of its aliases must start with
// one of the manifest's command prefixes (a plugin without prefixes registers no command); the owner is forced to the
// plugin's GUID, the help group becomes the GUID unless the plugin chose a group that is not one of the host's, and
// the version shown by help defaults to the plugin's. Handlers are wrapped: a faulted plugin's command answers
// plugin_fault without running, and an exception other than a BridgeException counts as a fault of the plugin (the
// dispatcher answers plugin_fault for it). A handler's run counts in the guard's depth (the plugin's code is on the stack).
// Unregistering only reaches the plugin's own commands.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;

namespace HS2Next.App.Plugins
{
    public sealed class ScopedCommandRegistry : ICommandRegistry
    {
        readonly ICommandRegistry inner;
        readonly IReadOnlyList<string> prefixes;
        readonly PluginGuard guard;
        readonly Func<bool> faulted;
        readonly Func<string, bool> hostGroup;
        readonly List<string> names = new List<string>();
        readonly object gate = new object();

        /// <param name="hostGroup">True for a group name that belongs to the host (a plugin may not put its commands there).</param>
        public ScopedCommandRegistry(ICommandRegistry inner, string guid, string version, IReadOnlyList<string> prefixes, PluginGuard guard,
                                     Func<bool> faulted, Func<string, bool> hostGroup = null)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            Guid = guid;
            Version = version;
            this.prefixes = prefixes ?? Array.Empty<string>();
            this.guard = guard;
            this.faulted = faulted ?? (() => false);
            this.hostGroup = hostGroup ?? (_ => false);
        }

        public string Guid { get; }
        public string Version { get; }
        /// <summary>The names this plugin registered and has not unregistered, in registration order.</summary>
        public IReadOnlyList<string> Names { get { lock (gate) return names.ToArray(); } }

        public void Register(CommandSpec spec, CommandHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Prepare(spec);
            string name = spec.Name;
            inner.Register(spec, ctx =>
            {
                CheckFaulted();
                guard?.Enter();
                try { return handler(ctx); }
                catch (Exception e) when (Counts(e))
                {
                    guard?.Count("command " + name, e);
                    throw;
                }
                finally { guard?.Exit(); }
            });
            lock (gate) names.Add(name);
        }

        public void RegisterAsync(CommandSpec spec, AsyncCommandHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Prepare(spec);
            string name = spec.Name;
            inner.RegisterAsync(spec, ctx =>
            {
                CheckFaulted();
                Task<object> t;
                guard?.Enter();
                try { t = handler(ctx); }
                catch (Exception e) when (Counts(e))
                {
                    guard?.Count("command " + name, e);
                    throw;
                }
                finally { guard?.Exit(); }
                if (t != null)
                    t.ContinueWith(tk =>
                    {
                        var e = tk.Exception?.InnerException ?? tk.Exception;
                        if (e != null && Counts(e)) guard?.Count("command " + name, e);
                    }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                return t;
            });
            lock (gate) names.Add(name);
        }

        static bool Counts(Exception e) => !(e is BridgeException) && !(e is OperationCanceledException);

        void CheckFaulted()
        {
            if (!faulted()) return;
            throw new BridgeException(BridgeCodes.PluginFault, "the plugin " + Guid + " is faulted (disabled after repeated errors); its commands do not run",
                new BridgeErrorDetail { Extra = new Dictionary<string, object> { ["owner"] = Guid, ["state"] = PluginStates.Faulted } });
        }

        void Prepare(CommandSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (spec.IsFrozen) throw new ArgumentException("command spec " + spec.Name + " is already registered", nameof(spec));
            if (faulted()) throw new InvalidOperationException("the plugin " + Guid + " is faulted");
            CheckPrefix(spec.Name);
            foreach (var a in spec.Aliases) CheckPrefix(a);
            spec.Owner = Guid;
            string defaultGroup = spec.Name.IndexOf('.') > 0 ? spec.Name.Substring(0, spec.Name.IndexOf('.')) : spec.Name;
            if (spec.Group == defaultGroup || hostGroup(spec.Group) || string.IsNullOrEmpty(spec.Group)) spec.Group = Guid;
            if (spec.Since == null) spec.Since = Version;
        }

        void CheckPrefix(string name)
        {
            if (prefixes.Count == 0)
                throw new ArgumentException("the plugin " + Guid + " has no commandPrefixes in its manifest and cannot register " + name);
            foreach (var p in prefixes)
                if (name != null && name.StartsWith(p, StringComparison.Ordinal) && name.Length > p.Length) return;
            throw new ArgumentException("the command name " + name + " does not start with a prefix of the plugin " + Guid + " (" + string.Join(", ", prefixes) + ")");
        }

        public bool Unregister(string name)
        {
            lock (gate) if (!names.Contains(name)) return false;
            var spec = inner.Find(name);
            if (spec == null || spec.Owner != Guid || spec.Name != name) return false;
            bool ok = inner.Unregister(name);
            if (ok) lock (gate) names.Remove(name);
            return ok;
        }

        /// <summary>Removes the plugin's own commands (any other owner: 0).</summary>
        public int UnregisterOwner(string owner)
        {
            if (!string.Equals(owner, Guid, StringComparison.Ordinal)) return 0;
            return UnregisterAll();
        }

        /// <summary>Removes every command of the plugin (the host's unload).</summary>
        public int UnregisterAll()
        {
            int n = inner.UnregisterOwner(Guid);
            lock (gate) names.Clear();
            return n;
        }

        public bool Contains(string name) => inner.Contains(name);
        public CommandSpec Find(string name) => inner.Find(name);
        public IReadOnlyList<CommandSpec> Specs => inner.Specs;
    }
}
