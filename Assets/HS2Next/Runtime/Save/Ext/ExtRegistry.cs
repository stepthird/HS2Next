// Registry of ext block handlers: one handler per namespace,
// compared ignoring case; reserved (hs2.*, hs2next.*, illusion.*) and malformed namespaces are
// rejected, except that Core may register hs2next.* and (2.3) hs2.* other than hs2.kkex through RegisterCore. A plugin gets a view that
// only accepts its own GUID and <guid>.* (ForOwner). Every call into a handler goes through the
// dispatcher's fault isolation; the registry counts faults and marks a handler faulted after 10. A plugin host can also be
// told of each fault of the handlers registered through its view (to count them for the plugin), list them and remove them
// all when the plugin unloads (NamespacesOf, UnregisterOwner).
using System;
using System.Collections.Generic;
using HS2Next.Api.Save;

namespace HS2Next.Save
{
    public sealed class ExtRegistry : IExtRegistry
    {
        /// <summary>Faults after which a handler is no longer called.</summary>
        public const int MaxFaults = 10;

        /// <summary>The application-wide registry (containers use it unless the load/save options name another).</summary>
        public static readonly ExtRegistry Default = new ExtRegistry();

        /// <summary>Log sink for handler faults (set to a Unity logger at startup); null = findings only.</summary>
        public static Action<string> Log;

        sealed class Entry
        {
            public IExtBlockHandler Handler;
            /// <summary>Told of every fault (namespace, hook, exception); from the owner view; null = none.</summary>
            public Action<string, string, Exception> OnFault;
            public int Faults;
            public readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
        }

        readonly SortedDictionary<string, Entry> byNs = new SortedDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public void Register(IExtBlockHandler handler) => Add(handler, false, null, null);

        /// <summary>Registration for Core features: also accepts <c>hs2next.*</c> and (2.3, the core node block <c>hs2.pose</c>,
        /// 7) <c>hs2.*</c>; never <c>hs2.kkex</c> (core data, never dispatched) or <c>illusion.*</c>.</summary>
        public void RegisterCore(IExtBlockHandler handler) => Add(handler, true, null, null);

        public bool Unregister(string ns) => ns != null && byNs.Remove(ns);

        /// <summary>A view for one plugin: only <paramref name="ownerGuid"/> and <c>&lt;ownerGuid&gt;.*</c> may be registered or unregistered.</summary>
        public IExtRegistry ForOwner(string ownerGuid) => new OwnerView(this, ownerGuid, null);

        /// <summary>
        /// As <see cref="ForOwner(string)"/>; <paramref name="onFault"/> is told of every fault of a handler registered
        /// through the view (namespace, hook, exception), after the registry counted it. A throw from it is ignored.
        /// </summary>
        public IExtRegistry ForOwner(string ownerGuid, Action<string, string, Exception> onFault) => new OwnerView(this, ownerGuid, onFault);

        /// <summary>The registered namespaces that belong to <paramref name="ownerGuid"/> (the GUID and <c>&lt;guid&gt;.*</c>,
        /// ignoring case), in ordinal order of their spelling.</summary>
        public List<string> NamespacesOf(string ownerGuid)
        {
            var l = new List<string>();
            if (string.IsNullOrEmpty(ownerGuid)) return l;
            foreach (var e in byNs.Values)
                if (ExtNamespaces.IsOwnedBy(e.Handler.Namespace, ownerGuid)) l.Add(e.Handler.Namespace);
            l.Sort(string.CompareOrdinal);
            return l;
        }

        /// <summary>Removes every handler of <paramref name="ownerGuid"/>'s namespaces (a plugin's unload); returns how many.
        /// Blocks such a handler read stay in the loaded documents and are written back as they are.</summary>
        public int UnregisterOwner(string ownerGuid)
        {
            int n = 0;
            foreach (var ns in NamespacesOf(ownerGuid))
                if (byNs.Remove(ns)) n++;
            return n;
        }

        void Add(IExtBlockHandler handler, bool core, string owner, Action<string, string, Exception> onFault)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            string ns = handler.Namespace;
            if (!ExtNamespaces.IsValid(ns)) throw new ArgumentException("malformed ext namespace: \"" + ns + "\"");
            bool coreOk = core && (ns.StartsWith("hs2next.", StringComparison.OrdinalIgnoreCase)
                || ns.StartsWith("hs2.", StringComparison.OrdinalIgnoreCase) && !string.Equals(ns, ExtNamespaces.Kkex, StringComparison.OrdinalIgnoreCase));
            if (ExtNamespaces.IsReserved(ns) && !coreOk) throw new ArgumentException("reserved ext namespace: " + ns);
            if (owner != null && !ExtNamespaces.IsOwnedBy(ns, owner)) throw new ArgumentException("namespace " + ns + " does not belong to " + owner);
            if (byNs.ContainsKey(ns)) throw new ArgumentException("ext namespace already registered (ignoring case): " + ns);
            if ((handler.Scopes & (ExtScope.Chara | ExtScope.Coord | ExtScope.Scene | ExtScope.Node)) == 0)
                throw new ArgumentException("handler " + ns + " declares no scope");
            byNs[ns] = new Entry { Handler = handler, OnFault = onFault };
        }

        /// <summary>The handler of a namespace (case-insensitive), or null.</summary>
        public IExtBlockHandler Get(string ns) => ns != null && byNs.TryGetValue(ns, out var e) ? e.Handler : null;

        /// <summary>Registered handlers in namespace ordinal order (exact spelling).</summary>
        public List<IExtBlockHandler> Handlers
        {
            get
            {
                var l = new List<IExtBlockHandler>();
                foreach (var e in byNs.Values) l.Add(e.Handler);
                l.Sort((a, b) => string.CompareOrdinal(a.Namespace, b.Namespace));
                return l;
            }
        }

        public bool IsFaulted(string ns) => byNs.TryGetValue(ns, out var e) && e.Faults >= MaxFaults;

        public int FaultCount(string ns) => byNs.TryGetValue(ns, out var e) ? e.Faults : 0;

        /// <summary>Counts a fault; logs once per (namespace, hook); returns true when the handler just became faulted.</summary>
        internal bool Fault(string ns, string hook, Exception ex)
        {
            if (!byNs.TryGetValue(ns, out var e)) return false;
            e.Faults++;
            if (e.Logged.Add(hook)) Log?.Invoke("ext handler " + ns + " threw in " + hook + ": " + ex);
            if (e.OnFault != null)
            {
                try { e.OnFault(ns, hook, ex); }
                catch (Exception) { }
            }
            return e.Faults == MaxFaults;
        }

        sealed class OwnerView : IExtRegistry
        {
            readonly ExtRegistry reg;
            readonly string owner;
            readonly Action<string, string, Exception> onFault;

            public OwnerView(ExtRegistry reg, string owner, Action<string, string, Exception> onFault)
            {
                if (!ExtNamespaces.IsValid(owner)) throw new ArgumentException("malformed plugin GUID: " + owner);
                this.reg = reg;
                this.owner = owner;
                this.onFault = onFault;
            }

            public void Register(IExtBlockHandler handler) => reg.Add(handler, false, owner, onFault);

            public bool Unregister(string ns) => ns != null && ExtNamespaces.IsOwnedBy(ns, owner) && reg.Unregister(ns);
        }
    }
}
