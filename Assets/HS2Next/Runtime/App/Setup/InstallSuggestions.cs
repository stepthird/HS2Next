// Suggestions for the HS2 folder of the first-run guide (Unity-free; never accepted by themselves: the user picks one or types
// a path, and the install check judges it). In order, de-duplicated (case-insensitive): the folder of the current settings'
// hs2Root (a re-run of the guide); the registry value INSTALLDIR under HKCU\Software\illusion\HoneySelect2\HoneySelect2 (the
// game's own value, which repack helpers write and which is often absent), read with "reg.exe query" (the runtime's class
// library has no registry API); a shallow scan of the fixed drives, <drive>:\* and <drive>:\*\*, skipping Windows, $* and
// System Volume Information, links and unreadable folders, a candidate being a folder with abdata\abdata and HoneySelect2_Data
// or StudioNEOV2_Data. Everything shares one time budget (3 s by default); the scan stops when it is spent. Gather runs on a
// worker thread.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2Next.App.Setup
{
    public sealed class InstallSuggestion
    {
        public string Path;
        /// <summary>settings | registry | scan.</summary>
        public string From;
    }

    public sealed class SuggestionResult
    {
        public readonly List<InstallSuggestion> Items = new List<InstallSuggestion>();
        /// <summary>Folders the scan looked at (levels 1 and 2).</summary>
        public int Visited;
        /// <summary>The budget ran out before the scan finished.</summary>
        public bool BudgetSpent;
        /// <summary>The registry value (null when absent or unreadable).</summary>
        public string Registry;
        public double Ms;
    }

    public static class InstallSuggestions
    {
        public const string RegistryKey = @"HKCU\Software\illusion\HoneySelect2\HoneySelect2";
        public const string RegistryValue = "INSTALLDIR";
        public const int DefaultBudgetMs = 3000;

        static readonly string[] SkipNames = { "Windows", "System Volume Information", "$Recycle.Bin", "Recovery", "PerfLogs" };

        /// <summary>
        /// The suggestions: <paramref name="currentRoot"/> (may be null), the registry (<paramref name="registry"/> replaces the
        /// reg.exe query in tests; null = the real query), the scan over <paramref name="roots"/> (null = the fixed drives).
        /// </summary>
        public static SuggestionResult Gather(string currentRoot, int budgetMs = DefaultBudgetMs, Func<int, string> registry = null, IEnumerable<string> roots = null)
        {
            var sw = Stopwatch.StartNew();
            var r = new SuggestionResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string p, string from)
            {
                string n = Hs2InstallCheck.Normalise(p);
                if (string.IsNullOrEmpty(n) || !seen.Add(n)) return;
                r.Items.Add(new InstallSuggestion { Path = n, From = from });
            }
            if (!string.IsNullOrWhiteSpace(currentRoot)) Add(currentRoot, "settings");
            try
            {
                int left = Math.Max(200, budgetMs - (int)sw.ElapsedMilliseconds);
                r.Registry = (registry ?? QueryRegistry)(Math.Min(left, 1500));
            }
            catch (Exception) { r.Registry = null; }
            if (!string.IsNullOrWhiteSpace(r.Registry)) Add(r.Registry, "registry");
            foreach (var p in Scan(roots ?? FixedDrives(), sw, budgetMs, r)) Add(p, "scan");
            r.Ms = sw.Elapsed.TotalMilliseconds;
            return r;
        }

        /// <summary>The root folders of the fixed drives that are ready, one per drive letter.</summary>
        public static List<string> FixedDrives()
        {
            var l = new List<string>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) l.Add(d.RootDirectory.FullName); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return l;
        }

        /// <summary>The candidates at depth 1 and 2 below each root, within the budget (shared with <paramref name="sw"/>).</summary>
        public static List<string> Scan(IEnumerable<string> roots, Stopwatch sw, int budgetMs, SuggestionResult stats = null)
        {
            var found = new List<string>();
            var level2 = new List<string>();
            bool Spent() => sw.ElapsedMilliseconds >= budgetMs;
            foreach (var root in roots)
            {
                foreach (var d1 in Children(root))
                {
                    if (Spent()) { if (stats != null) stats.BudgetSpent = true; return found; }
                    if (stats != null) stats.Visited++;
                    if (IsCandidate(d1)) found.Add(d1);
                    level2.Add(d1);
                }
            }
            // level 2 after every level 1 of every drive: the usual place is one folder below a drive root
            foreach (var d1 in level2)
            {
                foreach (var d2 in Children(d1))
                {
                    if (Spent()) { if (stats != null) stats.BudgetSpent = true; return found; }
                    if (stats != null) stats.Visited++;
                    if (IsCandidate(d2)) found.Add(d2);
                }
            }
            return found;
        }

        /// <summary>The subfolders worth looking into: no links, no system folders, readable.</summary>
        static IEnumerable<string> Children(string dir)
        {
            List<string> l;
            try { l = Directory.EnumerateDirectories(dir).ToList(); }
            catch (Exception) { yield break; }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var d in l)
            {
                string name = Path.GetFileName(d);
                if (string.IsNullOrEmpty(name) || name.StartsWith("$", StringComparison.Ordinal)) continue;
                if (SkipNames.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase))) continue;
                FileAttributes a;
                try { a = File.GetAttributes(d); }
                catch (Exception) { continue; }
                if ((a & FileAttributes.ReparsePoint) != 0) continue;
                if ((a & FileAttributes.System) != 0 && (a & FileAttributes.Hidden) != 0) continue;
                yield return d;
            }
        }

        /// <summary>A folder holding abdata\abdata and one of the game's data folders.</summary>
        public static bool IsCandidate(string dir)
        {
            try
            {
                if (!File.Exists(Path.Combine(dir, "abdata", "abdata"))) return false;
                return Hs2InstallCheck.DataFolders.Any(f => Directory.Exists(Path.Combine(dir, f)));
            }
            catch (Exception) { return false; }
        }

        /// <summary>The value of INSTALLDIR from the output of "reg.exe query &lt;key&gt; /v INSTALLDIR", or null when the output
        /// names no such value (the key or the value is absent: reg.exe then prints an error and no value line).</summary>
        public static string ParseRegOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return null;
            foreach (var raw in output.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith(RegistryValue, StringComparison.OrdinalIgnoreCase)) continue;
                string rest = line.Substring(RegistryValue.Length);
                if (rest.Length == 0 || !char.IsWhiteSpace(rest[0])) continue;
                rest = rest.TrimStart();
                int t = rest.IndexOf("REG_", StringComparison.OrdinalIgnoreCase);
                if (t != 0) continue;
                int sp = 0;
                while (sp < rest.Length && !char.IsWhiteSpace(rest[sp])) sp++;
                string type = rest.Substring(0, sp);
                if (!type.Equals("REG_SZ", StringComparison.OrdinalIgnoreCase) && !type.Equals("REG_EXPAND_SZ", StringComparison.OrdinalIgnoreCase)) continue;
                string value = rest.Substring(sp).Trim();
                if (type.Equals("REG_EXPAND_SZ", StringComparison.OrdinalIgnoreCase)) value = Environment.ExpandEnvironmentVariables(value);
                return value.Length > 0 ? value : null;
            }
            return null;
        }

        /// <summary>Runs "reg.exe query" for the value (read only), at most <paramref name="timeoutMs"/>; null when absent or when
        /// reg.exe cannot run (not Windows).</summary>
        public static string QueryRegistry(int timeoutMs)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string exe = string.IsNullOrEmpty(sys) ? "reg.exe" : Path.Combine(sys, "reg.exe");
            var psi = new ProcessStartInfo(exe, "query \"" + RegistryKey + "\" /v " + RegistryValue)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return null;
                var outTask = p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(Math.Max(100, timeoutMs)))
                {
                    try { p.Kill(); } catch (Exception) { }
                    return null;
                }
                return p.ExitCode == 0 ? ParseRegOutput(outTask.Result) : null;
            }
        }
    }
}
