// Discovery, validation and load order of the plugins in one plugin folder (Unity-free). Every direct subfolder that
// holds manifest.json (any letter case) is a candidate; a subfolder without one is only reported. The folder name must
// equal the manifest's guid ignoring case. Refusals are decided per candidate first (manifest, folder name, disabled
// list, Api and app versions, listed files, assembly names the host already has, command prefixes of the host's
// groups), then between candidates (duplicate GUIDs refuse both; a command prefix claimed by several plugins stays
// with the first in GUID order; missing, refused or too old dependencies refuse the dependent, repeatedly; dependency
// cycles refuse every member). The order is Kahn's topological order over the dependencies plus the loadAfter /
// loadBefore hints whose both ends load (a hint that would make a cycle is dropped and reported), ties broken by GUID
// (ordinal, ignoring case). Nothing in the folder is created, changed or deleted. A candidate on the disabled list skips
// the per-candidate checks after the folder name; enabling it at run time runs them then (Revalidate), with the
// prefixes of the plugins loaded at that moment in place of the other candidates'.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HS2Next.App.Plugins
{
    /// <summary>One subfolder with a manifest and what the scan decided about it.</summary>
    public sealed class PluginCandidate
    {
        /// <summary>The plugin folder (absolute).</summary>
        public string Dir;
        public string FolderName;
        /// <summary>The manifest's guid, or the folder name when the manifest could not be read.</summary>
        public string Guid;
        /// <summary>Null when the manifest could not be used.</summary>
        public PluginManifest Manifest;
        /// <summary><see cref="PluginStates"/>: pending (will load), refused or disabled.</summary>
        public string State = PluginStates.Pending;
        /// <summary>The <see cref="PluginCodes"/> code of a refusal or of disabled, else null.</summary>
        public string Code;
        /// <summary>The reason text of a refusal, else null.</summary>
        public string Reason;
        /// <summary>Position in the load order (0-based), or −1.</summary>
        public int Order = -1;

        public bool IsPending => State == PluginStates.Pending;

        public void Refuse(string code, string reason)
        {
            State = PluginStates.Refused;
            Code = code;
            Reason = reason;
        }

        public override string ToString() => Guid + " (" + State + (Code != null ? ", " + Code : "") + ")";
    }

    /// <summary>What the scan checks against.</summary>
    public sealed class PluginScanOptions
    {
        public int HostApiMajor = HS2Next.ApiVersion.Major;
        public int HostApiMinor = HS2Next.ApiVersion.Minor;
        /// <summary>The app's version (semver); null skips the minAppVersion check.</summary>
        public string AppVersion;
        /// <summary>Simple names of the assemblies the host has loaded or ships (compared ignoring case).</summary>
        public IEnumerable<string> HostAssemblies = Array.Empty<string>();
        /// <summary>Command groups a plugin prefix may not use (the host's groups when the plugins load, bridge, plugin).</summary>
        public IEnumerable<string> ReservedGroups = Array.Empty<string>();
        /// <summary>GUIDs of the disabled list (compared ignoring case).</summary>
        public IEnumerable<string> Disabled = Array.Empty<string>();
    }

    public sealed class PluginScanResult
    {
        /// <summary>The plugin folder (absolute), or null.</summary>
        public string Root;
        /// <summary>Every candidate: the load order first, then the others by GUID.</summary>
        public List<PluginCandidate> Candidates = new List<PluginCandidate>();
        /// <summary>The candidates that load, in load order.</summary>
        public List<PluginCandidate> LoadOrder = new List<PluginCandidate>();
        /// <summary>Information lines ("plugin.noManifest: &lt;folder&gt;", "plugin.orderHint: ...").</summary>
        public List<string> Infos = new List<string>();
    }

    public static class PluginScan
    {
        static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

        /// <summary>Scans <paramref name="root"/> (absent = no plugins).</summary>
        public static PluginScanResult Scan(string root, PluginScanOptions o)
        {
            o ??= new PluginScanOptions();
            var res = new PluginScanResult { Root = root };
            var list = new List<PluginCandidate>();
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                var dirs = Directory.GetDirectories(root);
                Array.Sort(dirs, Ci);
                foreach (var d in dirs)
                {
                    string folder = Path.GetFileName(d);
                    string manifest = FindFile(d, PluginManifest.FileName);
                    if (manifest == null)
                    {
                        res.Infos.Add(PluginCodes.NoManifest + ": " + folder);
                        continue;
                    }
                    var c = new PluginCandidate { Dir = Path.GetFullPath(d), FolderName = folder, Guid = folder };
                    list.Add(c);
                    string text;
                    try { text = File.ReadAllText(manifest); }
                    catch (Exception e)
                    {
                        c.Refuse(PluginCodes.BadManifest, "manifest.json not readable: " + e.Message);
                        continue;
                    }
                    var m = PluginManifest.Parse(text, out string code, out string error, out string guidSeen);
                    if (guidSeen != null && guidSeen.Length > 0) c.Guid = guidSeen;
                    if (m == null)
                    {
                        c.Refuse(code, error);
                        continue;
                    }
                    c.Manifest = m;
                    c.Guid = m.Guid;
                }
            }
            Validate(list, o);
            res.LoadOrder = Order(list, res.Infos);
            for (int i = 0; i < res.LoadOrder.Count; i++) res.LoadOrder[i].Order = i;
            res.Candidates = res.LoadOrder.Concat(list.Where(c => !c.IsPending).OrderBy(c => c.Guid, Ci).ThenBy(c => c.FolderName, Ci)).ToList();
            return res;
        }

        /// <summary>The file of that name in <paramref name="dir"/> ignoring letter case, or null.</summary>
        public static string FindFile(string dir, string name)
        {
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The per-candidate checks and the checks between candidates except the order.</summary>
        public static void Validate(List<PluginCandidate> list, PluginScanOptions o)
        {
            var hostAsm = new HashSet<string>(o.HostAssemblies ?? Array.Empty<string>(), Ci);
            var groups = new HashSet<string>(o.ReservedGroups ?? Array.Empty<string>(), Ci) { "bridge", "plugin" };
            var disabled = new HashSet<string>(o.Disabled ?? Array.Empty<string>(), Ci);
            SemVer app = null;
            if (o.AppVersion != null) SemVer.TryParse(o.AppVersion, out app);

            // per candidate
            foreach (var c in list)
            {
                if (!c.IsPending) continue;
                var m = c.Manifest;
                if (!string.Equals(c.FolderName, m.Guid, StringComparison.OrdinalIgnoreCase))
                {
                    c.Refuse(PluginCodes.FolderMismatch, "the folder \"" + c.FolderName + "\" is not named after the GUID " + m.Guid);
                    continue;
                }
            }
            // duplicate GUIDs (both refused), before the other checks so that a duplicate never loads
            foreach (var g in list.Where(c => c.Manifest != null).GroupBy(c => c.Manifest.Guid, Ci).Where(g => g.Count() > 1))
                foreach (var c in g)
                    if (c.IsPending || c.Code == PluginCodes.FolderMismatch)
                        c.Refuse(PluginCodes.DuplicateGuid, "the GUID " + c.Manifest.Guid + " is used by " + g.Count() + " folders: " + string.Join(", ", g.Select(x => x.FolderName)));
            foreach (var c in list)
            {
                if (!c.IsPending) continue;
                var m = c.Manifest;
                if (disabled.Contains(m.Guid))
                {
                    c.State = PluginStates.Disabled;
                    c.Code = PluginCodes.Disabled;
                    c.Reason = "on the disabled list (plugins.disabled)";
                    continue;
                }
                string code = CheckOne(c, o, app, hostAsm, groups, out string reason);
                if (code != null) c.Refuse(code, reason);
            }
            // prefixes between plugins: the first claimant in GUID order keeps a prefix (overlapping prefixes count as one)
            var claimed = new List<KeyValuePair<string, string>>();
            foreach (var c in list.Where(x => x.IsPending).OrderBy(x => x.Guid, Ci))
            {
                string taken = ClaimedPrefix(c.Manifest, claimed, out string by);
                if (taken != null)
                {
                    c.Refuse(PluginCodes.PrefixTaken, "the command prefix " + taken + " is claimed by " + by);
                    continue;
                }
                foreach (var p in c.Manifest.CommandPrefixes) claimed.Add(new KeyValuePair<string, string>(p, c.Guid));
            }
            // dependencies, until nothing changes
            bool changed = true;
            while (changed)
            {
                changed = false;
                var byGuid = list.Where(x => x.Manifest != null).GroupBy(x => x.Manifest.Guid, Ci).ToDictionary(g => g.Key, g => g.First(), Ci);
                foreach (var c in list)
                {
                    if (!c.IsPending) continue;
                    foreach (var d in c.Manifest.Dependencies)
                    {
                        string why = null;
                        if (string.Equals(d.Guid, c.Guid, StringComparison.OrdinalIgnoreCase)) continue;   // a self-dependency is a cycle of one (below)
                        if (!byGuid.TryGetValue(d.Guid, out var t)) why = "the dependency " + d.Guid + " is not installed";
                        else if (t.State == PluginStates.Disabled) why = "the dependency " + d.Guid + " is disabled";
                        else if (!t.IsPending) why = "the dependency " + d.Guid + " is " + t.State + (t.Code != null ? " (" + t.Code + ")" : "");
                        else if (d.MinVersion != null && SemVer.TryParse(d.MinVersion, out var need) && SemVer.TryParse(t.Manifest.Version, out var have) && have.CompareTo(need) < 0)
                            why = "the dependency " + d.Guid + " is " + t.Manifest.Version + ", older than " + d.MinVersion;
                        if (why != null)
                        {
                            c.Refuse(PluginCodes.Dependency, why);
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The per-candidate checks after the folder name and the disabled list: the Api and app versions, the listed
        /// assemblies present, no assembly name the host has, no command prefix of a host group. The refusal code (reason
        /// out), or null.
        /// </summary>
        static string CheckOne(PluginCandidate c, PluginScanOptions o, SemVer app, HashSet<string> hostAsm, HashSet<string> groups, out string reason)
        {
            var m = c.Manifest;
            reason = null;
            if (!SemVer.ApiCompatible(m.ApiVersion, o.HostApiMajor, o.HostApiMinor))
            {
                reason = "built against Api " + m.ApiVersion + "; the host implements " + o.HostApiMajor + "." + o.HostApiMinor +
                         (o.HostApiMajor == 0 ? " (0.x: the minor must match exactly)" : " (the major must match, the host's minor at least the plugin's)");
                return PluginCodes.ApiVersion;
            }
            if (m.MinAppVersion != null && app != null && SemVer.TryParse(m.MinAppVersion, out var need) && app.CompareTo(need) < 0)
            {
                reason = "needs app " + m.MinAppVersion + " or later; this app is " + o.AppVersion;
                return PluginCodes.AppVersion;
            }
            string missing = m.Assemblies.FirstOrDefault(a => FindFile(c.Dir, a) == null);
            if (missing != null)
            {
                reason = "the listed assembly " + missing + " is not in the plugin folder";
                return PluginCodes.MissingAssembly;
            }
            string clash = AssemblyNames(c).FirstOrDefault(n => hostAsm.Contains(n));
            if (clash != null)
            {
                reason = "the assembly " + clash + " has the name of an assembly the app already has (remove the plugin's copy)";
                return PluginCodes.AssemblyCollision;
            }
            string reserved = m.CommandPrefixes.FirstOrDefault(p => groups.Contains(p.Substring(0, p.IndexOf('.'))));
            if (reserved != null)
            {
                reason = "the command prefix " + reserved + " is a group of the app";
                return PluginCodes.PrefixTaken;
            }
            return null;
        }

        /// <summary>The first prefix of <paramref name="m"/> that overlaps a claimed one (either starts with the other), or null; <paramref name="by"/> = its claimant.</summary>
        static string ClaimedPrefix(PluginManifest m, IEnumerable<KeyValuePair<string, string>> claimed, out string by)
        {
            by = null;
            foreach (var p in m.CommandPrefixes)
                foreach (var kv in claimed)
                    if (p.StartsWith(kv.Key, StringComparison.Ordinal) || kv.Key.StartsWith(p, StringComparison.Ordinal))
                    {
                        by = kv.Value;
                        return p;
                    }
            return null;
        }

        /// <summary>
        /// The checks a candidate on the disabled list skipped at the scan, run when it is enabled at run time: the Api and
        /// app versions, the listed assemblies present, no assembly name the host has, no command prefix of a host group,
        /// and no prefix that overlaps one of <paramref name="loadedPrefixes"/> (prefix → the GUID of the loaded plugin that
        /// holds it; the plugin's own GUID is ignored). Returns the refusal code (<paramref name="reason"/> its text), or null
        /// when the candidate may load. The candidate itself is not changed.
        /// </summary>
        public static string Revalidate(PluginCandidate c, PluginScanOptions o, IEnumerable<KeyValuePair<string, string>> loadedPrefixes, out string reason)
        {
            reason = null;
            if (c?.Manifest == null)
            {
                reason = c?.Reason ?? "no usable manifest";
                return c?.Code ?? PluginCodes.BadManifest;
            }
            o ??= new PluginScanOptions();
            var hostAsm = new HashSet<string>(o.HostAssemblies ?? Array.Empty<string>(), Ci);
            var groups = new HashSet<string>(o.ReservedGroups ?? Array.Empty<string>(), Ci) { "bridge", "plugin" };
            SemVer app = null;
            if (o.AppVersion != null) SemVer.TryParse(o.AppVersion, out app);
            string code = CheckOne(c, o, app, hostAsm, groups, out reason);
            if (code != null) return code;
            var others = (loadedPrefixes ?? Array.Empty<KeyValuePair<string, string>>())
                .Where(kv => kv.Key != null && !string.Equals(kv.Value, c.Manifest.Guid, StringComparison.OrdinalIgnoreCase)).ToList();
            string taken = ClaimedPrefix(c.Manifest, others, out string by);
            if (taken != null)
            {
                reason = "the command prefix " + taken + " is claimed by " + by;
                return PluginCodes.PrefixTaken;
            }
            return null;
        }

        /// <summary>The simple names of a candidate's listed assemblies and of the DLLs in its lib folder.</summary>
        public static IEnumerable<string> AssemblyNames(PluginCandidate c)
        {
            foreach (var a in c.Manifest.Assemblies) yield return Path.GetFileNameWithoutExtension(a);
            string lib = Path.Combine(c.Dir, "lib");
            string[] files = Array.Empty<string>();
            try { if (Directory.Exists(lib)) files = Directory.GetFiles(lib, "*.dll"); }
            catch (Exception) { }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) yield return Path.GetFileNameWithoutExtension(f);
        }

        /// <summary>
        /// Orders the pending candidates (dependencies first, then the hints, ties by GUID) and refuses the members of
        /// dependency cycles (plugin.dependencyCycle) and the candidates that depend on them (plugin.dependency).
        /// </summary>
        public static List<PluginCandidate> Order(List<PluginCandidate> list, List<string> infos)
        {
            // cycles of the dependency graph first (they refuse their members and everything depending on them)
            while (true)
            {
                var pending = list.Where(x => x.IsPending).ToList();
                var byGuid = pending.ToDictionary(x => x.Manifest.Guid, Ci);
                var edges = pending.ToDictionary(x => x, x => x.Manifest.Dependencies.Where(d => byGuid.ContainsKey(d.Guid)).Select(d => byGuid[d.Guid]).Distinct().ToList());
                var cyc = CycleMembers(pending, edges);
                if (cyc.Count == 0) break;
                foreach (var c in cyc)
                    c.Refuse(PluginCodes.DependencyCycle, "a dependency cycle: " + string.Join(" -> ", CycleText(c, edges, cyc)));
                // dependents of refused plugins
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var c in list.Where(x => x.IsPending))
                    {
                        var bad = c.Manifest.Dependencies.FirstOrDefault(d => list.Any(t => t.Manifest != null && string.Equals(t.Manifest.Guid, d.Guid, StringComparison.OrdinalIgnoreCase) && !t.IsPending));
                        if (bad != null)
                        {
                            var t = list.First(x => x.Manifest != null && string.Equals(x.Manifest.Guid, bad.Guid, StringComparison.OrdinalIgnoreCase));
                            c.Refuse(PluginCodes.Dependency, "the dependency " + bad.Guid + " is " + t.State + (t.Code != null ? " (" + t.Code + ")" : ""));
                            changed = true;
                        }
                    }
                }
            }
            var nodes = list.Where(x => x.IsPending).OrderBy(x => x.Manifest.Guid, Ci).ToList();
            var index = nodes.ToDictionary(x => x.Manifest.Guid, Ci);
            // before[x] = the nodes that must load before x
            var before = nodes.ToDictionary(x => x, x => new HashSet<PluginCandidate>());
            foreach (var n in nodes)
                foreach (var d in n.Manifest.Dependencies)
                    if (index.TryGetValue(d.Guid, out var t) && t != n) before[n].Add(t);
            foreach (var n in nodes)
            {
                foreach (var g in n.Manifest.LoadAfter) AddHint(index, before, g, n, n.Manifest.Guid + " loadAfter " + g, infos);
                foreach (var g in n.Manifest.LoadBefore)
                    if (index.TryGetValue(g, out var t)) AddHint(index, before, n.Manifest.Guid, t, n.Manifest.Guid + " loadBefore " + g, infos);
            }
            var order = new List<PluginCandidate>();
            var done = new HashSet<PluginCandidate>();
            while (order.Count < nodes.Count)
            {
                var next = nodes.FirstOrDefault(n => !done.Contains(n) && before[n].All(done.Contains));
                if (next == null) break;   // cannot happen: hints never close a cycle and dependency cycles were refused
                order.Add(next);
                done.Add(next);
            }
            return order;
        }

        static void AddHint(Dictionary<string, PluginCandidate> index, Dictionary<PluginCandidate, HashSet<PluginCandidate>> before, string firstGuid, PluginCandidate second,
                            string what, List<string> infos)
        {
            if (!index.TryGetValue(firstGuid, out var first) || first == second) return;
            if (before[second].Contains(first)) return;
            // adding "first before second" closes a cycle when second already has to load before first
            if (Reaches(before, first, second))
            {
                infos?.Add(PluginCodes.OrderHint + ": " + what + " dropped (it would make a cycle)");
                return;
            }
            before[second].Add(first);
        }

        /// <summary>True when <paramref name="target"/> must load before <paramref name="from"/> (transitively).</summary>
        static bool Reaches(Dictionary<PluginCandidate, HashSet<PluginCandidate>> before, PluginCandidate from, PluginCandidate target)
        {
            var stack = new Stack<PluginCandidate>();
            var seen = new HashSet<PluginCandidate>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                foreach (var b in before[x])
                {
                    if (b == target) return true;
                    if (seen.Add(b)) stack.Push(b);
                }
            }
            return false;
        }

        /// <summary>Nodes on a cycle of the dependency graph (strongly connected components larger than one, or a self edge).</summary>
        static List<PluginCandidate> CycleMembers(List<PluginCandidate> nodes, Dictionary<PluginCandidate, List<PluginCandidate>> edges)
        {
            // Tarjan's algorithm, iterative-friendly sizes (a handful of plugins)
            int idx = 0;
            var index = new Dictionary<PluginCandidate, int>();
            var low = new Dictionary<PluginCandidate, int>();
            var onStack = new HashSet<PluginCandidate>();
            var stack = new Stack<PluginCandidate>();
            var result = new List<PluginCandidate>();
            void Strong(PluginCandidate v)
            {
                index[v] = low[v] = idx++;
                stack.Push(v);
                onStack.Add(v);
                foreach (var w in edges[v])
                {
                    if (!index.ContainsKey(w))
                    {
                        Strong(w);
                        low[v] = Math.Min(low[v], low[w]);
                    }
                    else if (onStack.Contains(w)) low[v] = Math.Min(low[v], index[w]);
                }
                if (low[v] != index[v]) return;
                var comp = new List<PluginCandidate>();
                PluginCandidate x;
                do
                {
                    x = stack.Pop();
                    onStack.Remove(x);
                    comp.Add(x);
                } while (x != v);
                if (comp.Count > 1 || edges[v].Contains(v) || v.Manifest.Dependencies.Any(d => string.Equals(d.Guid, v.Manifest.Guid, StringComparison.OrdinalIgnoreCase)))
                    result.AddRange(comp);
            }
            foreach (var n in nodes.OrderBy(n => n.Manifest.Guid, Ci))
                if (!index.ContainsKey(n)) Strong(n);
            return result;
        }

        static IEnumerable<string> CycleText(PluginCandidate start, Dictionary<PluginCandidate, List<PluginCandidate>> edges, List<PluginCandidate> members)
        {
            var path = new List<string> { start.Manifest.Guid };
            var cur = start;
            var seen = new HashSet<PluginCandidate> { start };
            while (true)
            {
                var next = edges[cur].Where(members.Contains).OrderBy(x => x.Manifest.Guid, Ci).FirstOrDefault();
                if (next == null && cur.Manifest.Dependencies.Any(d => string.Equals(d.Guid, cur.Manifest.Guid, StringComparison.OrdinalIgnoreCase))) next = cur;
                if (next == null) break;
                path.Add(next.Manifest.Guid);
                if (!seen.Add(next)) break;
                cur = next;
            }
            return path;
        }
    }
}
