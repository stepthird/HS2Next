// The command registry (HS2Next.Api.Bridge.ICommandRegistry): names and
// deprecated aliases share one namespace, collisions are rejected, the bridge's own commands and the "bridge."
// group are reserved, and every entry remembers its owner so a plugin's commands can be removed together. Specs are
// frozen on registration. Thread-safe: the listener threads read it (help) while the main thread may register.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;

namespace HS2Next.Bridge
{
    /// <summary>A registered command: its spec and one of the two handler kinds.</summary>
    public sealed class CommandEntry
    {
        internal CommandEntry(CommandSpec spec, CommandHandler sync, AsyncCommandHandler async, bool inline)
        {
            Spec = spec;
            Sync = sync;
            Async = async;
            Inline = inline;
        }

        public CommandSpec Spec { get; }
        public CommandHandler Sync { get; }
        public AsyncCommandHandler Async { get; }
        /// <summary>Answered on the listener thread when sent alone (ping, help, bridge.version, log.tail).</summary>
        internal bool Inline { get; }
    }

    public sealed class CommandRegistry : ICommandRegistry
    {
        /// <summary>Names only the bridge registers.</summary>
        public static readonly IReadOnlyList<string> Reserved = new[] { "ping", "help", "bridge.version", "batch", "log.tail" };

        readonly object gate = new object();
        readonly Dictionary<string, CommandEntry> byName = new Dictionary<string, CommandEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, CommandEntry> byAlias = new Dictionary<string, CommandEntry>(StringComparer.Ordinal);
        IReadOnlyList<CommandSpec> snapshot;

        public void Register(CommandSpec spec, CommandHandler handler) => Add(spec, handler, null, false, false);
        public void RegisterAsync(CommandSpec spec, AsyncCommandHandler handler) => Add(spec, null, handler, false, false);

        /// <summary>The bridge's own registrations (reserved names, optionally answered on the listener thread).</summary>
        internal void RegisterBuiltin(CommandSpec spec, CommandHandler handler, bool inline) => Add(spec, handler, null, inline, true);

        static bool IsReservedName(string n) =>
            n.StartsWith("bridge.", StringComparison.Ordinal) || ((IList<string>)Reserved).Contains(n);

        void Add(CommandSpec spec, CommandHandler sync, AsyncCommandHandler async, bool inline, bool builtin)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (sync == null && async == null) throw new ArgumentNullException("handler");
            if (spec.IsFrozen) throw new ArgumentException("command spec " + spec.Name + " is already registered", nameof(spec));
            var all = new List<string> { spec.Name };
            foreach (var a in spec.Aliases)
            {
                if (all.Contains(a)) throw new ArgumentException("command " + spec.Name + " names " + a + " twice", nameof(spec));
                all.Add(a);
            }
            lock (gate)
            {
                foreach (var n in all)
                {
                    if (!builtin && IsReservedName(n)) throw new ArgumentException("the command name " + n + " is reserved for the bridge", nameof(spec));
                    if (byName.TryGetValue(n, out var e) || byAlias.TryGetValue(n, out e))
                        throw new ArgumentException("the command name " + n + " is taken by " + e.Spec.Name + " (owner " + e.Spec.Owner + ")", nameof(spec));
                }
                if (builtin) spec.Owner = BridgeOwners.Bridge;
                else if (spec.Owner == BridgeOwners.Bridge) throw new ArgumentException("owner \"bridge\" is reserved", nameof(spec));
                spec.Freeze();
                var entry = new CommandEntry(spec, sync, async, inline);
                byName[spec.Name] = entry;
                foreach (var a in spec.Aliases) byAlias[a] = entry;
                snapshot = null;
            }
        }

        public bool Unregister(string name)
        {
            if (name == null) return false;
            lock (gate)
            {
                if (!byName.TryGetValue(name, out var e) || e.Spec.Owner == BridgeOwners.Bridge) return false;
                Remove(e);
                return true;
            }
        }

        public int UnregisterOwner(string owner)
        {
            if (string.IsNullOrEmpty(owner) || owner == BridgeOwners.Bridge) return 0;
            lock (gate)
            {
                var list = new List<CommandEntry>();
                foreach (var e in byName.Values) if (e.Spec.Owner == owner) list.Add(e);
                foreach (var e in list) Remove(e);
                return list.Count;
            }
        }

        void Remove(CommandEntry e)
        {
            byName.Remove(e.Spec.Name);
            foreach (var a in e.Spec.Aliases) byAlias.Remove(a);
            snapshot = null;
        }

        public bool Contains(string name) => Lookup(name, out _) != null;

        public CommandSpec Find(string name) => Lookup(name, out _)?.Spec;

        /// <summary>The entry of a name or alias; <paramref name="viaAlias"/> tells which.</summary>
        public CommandEntry Lookup(string name, out bool viaAlias)
        {
            viaAlias = false;
            if (name == null) return null;
            lock (gate)
            {
                if (byName.TryGetValue(name, out var e)) return e;
                if (byAlias.TryGetValue(name, out e)) { viaAlias = true; return e; }
                return null;
            }
        }

        public IReadOnlyList<CommandSpec> Specs
        {
            get
            {
                lock (gate)
                {
                    if (snapshot != null) return snapshot;
                    var l = new List<CommandSpec>();
                    foreach (var e in byName.Values) l.Add(e.Spec);
                    l.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    snapshot = l.AsReadOnly();
                    return snapshot;
                }
            }
        }

        /// <summary>Every name and alias (for candidate suggestions).</summary>
        public List<string> AllNames()
        {
            lock (gate)
            {
                var l = new List<string>(byName.Keys);
                l.AddRange(byAlias.Keys);
                return l;
            }
        }
    }

    /// <summary>Readiness stages (HS2Next.Api.Bridge.IBridgeReadiness). Thread-safe.</summary>
    public sealed class Readiness : IBridgeReadiness
    {
        readonly object gate = new object();
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, (bool ready, bool required)> stages = new Dictionary<string, (bool, bool)>(StringComparer.Ordinal);
        readonly Dictionary<string, string> details = new Dictionary<string, string>(StringComparer.Ordinal);

        public void SetDetail(string stage, string detail)
        {
            if (string.IsNullOrEmpty(stage)) throw new ArgumentException("empty stage name", nameof(stage));
            lock (gate)
            {
                if (!stages.ContainsKey(stage)) { stages[stage] = (false, false); order.Add(stage); }
                if (detail == null) details.Remove(stage);
                else details[stage] = detail;
            }
        }

        public string Detail(string stage)
        {
            if (stage == null) return null;
            lock (gate) return details.TryGetValue(stage, out var d) ? d : null;
        }

        /// <summary>Every stage that has a progress text, in declaration order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Details()
        {
            lock (gate)
            {
                var l = new List<KeyValuePair<string, string>>();
                foreach (var n in order) if (details.TryGetValue(n, out var d)) l.Add(new KeyValuePair<string, string>(n, d));
                return l;
            }
        }

        public void Declare(string stage, bool requiredForApp)
        {
            if (string.IsNullOrEmpty(stage)) throw new ArgumentException("empty stage name", nameof(stage));
            lock (gate)
            {
                if (stages.TryGetValue(stage, out var s)) stages[stage] = (s.ready, requiredForApp);
                else { stages[stage] = (false, requiredForApp); order.Add(stage); }
            }
        }

        public void Set(string stage, bool ready)
        {
            if (string.IsNullOrEmpty(stage)) throw new ArgumentException("empty stage name", nameof(stage));
            lock (gate)
            {
                if (stages.TryGetValue(stage, out var s)) stages[stage] = (ready, s.required);
                else { stages[stage] = (ready, false); order.Add(stage); }
            }
        }

        public bool IsReady(string stage)
        {
            if (stage == null) return false;
            lock (gate) return stages.TryGetValue(stage, out var s) && s.ready;
        }

        public bool AppReady
        {
            get
            {
                lock (gate)
                {
                    foreach (var s in stages.Values) if (s.required && !s.ready) return false;
                    return true;
                }
            }
        }

        public IReadOnlyList<KeyValuePair<string, bool>> Snapshot()
        {
            lock (gate)
            {
                var l = new List<KeyValuePair<string, bool>>(order.Count);
                foreach (var n in order) l.Add(new KeyValuePair<string, bool>(n, stages[n].ready));
                return l;
            }
        }

        /// <summary>Stages declared as required and not ready.</summary>
        public List<string> Missing(IReadOnlyList<string> extra)
        {
            var l = new List<string>();
            lock (gate)
            {
                foreach (var n in order) if (stages[n].required && !stages[n].ready) l.Add(n);
                if (extra != null)
                    foreach (var n in extra)
                        if (!l.Contains(n) && !(stages.TryGetValue(n, out var s) && s.ready)) l.Add(n);
            }
            return l;
        }
    }

    /// <summary>Nearest-name suggestions for unknown commands and arguments (detail.candidates).</summary>
    public static class NameMatch
    {
        /// <summary>Up to <paramref name="max"/> names close to <paramref name="name"/>: prefix and substring matches first, then by edit distance.</summary>
        public static List<string> Candidates(string name, IEnumerable<string> pool, int max = 3)
        {
            var scored = new List<(int score, string n)>();
            if (string.IsNullOrEmpty(name) || pool == null) return new List<string>();
            string lower = name.ToLowerInvariant();
            foreach (var n in pool)
            {
                if (n == null || n == name) continue;
                string nl = n.ToLowerInvariant();
                int d = Distance(lower, nl);
                int limit = Math.Max(2, Math.Min(lower.Length, nl.Length) / 3);
                int score;
                if (nl == lower) score = 0;
                else if (nl.StartsWith(lower, StringComparison.Ordinal) || lower.StartsWith(nl, StringComparison.Ordinal)) score = 1;
                else if (d <= limit) score = 2 + d;
                else if (lower.Length >= 3 && nl.Contains(lower)) score = 2 + limit + 1;
                else continue;
                scored.Add((score, n));
            }
            scored.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : string.CompareOrdinal(a.n, b.n));
            var result = new List<string>();
            foreach (var s in scored) { if (result.Count >= max) break; if (!result.Contains(s.n)) result.Add(s.n); }
            return result;
        }

        /// <summary>Levenshtein distance.</summary>
        public static int Distance(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }
    }
}
