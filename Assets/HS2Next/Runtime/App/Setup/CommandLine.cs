// The command line of a restart (Unity-free): the arguments of this process without some flags and with others added, joined
// into one string with the quoting rules of Windows programs (a backslash run before a quote is doubled, an argument with a
// space, a tab or a quote is quoted), so that the new process parses the same arguments.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HS2Next.App.Setup
{
    public static class CommandLine
    {
        /// <summary>A restarted app's flag naming the PID of the app it replaces (the new app waits for that process to end).</summary>
        public const string WaitPidFlag = "-waitPid";

        /// <summary>The PID after -waitPid, or 0.</summary>
        public static int WaitPid(IList<string> argv)
        {
            if (argv == null) return 0;
            for (int i = 0; i + 1 < argv.Count; i++)
                if (argv[i] == WaitPidFlag && int.TryParse(argv[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int pid) && pid > 0)
                    return pid;
            return 0;
        }

        /// <summary>The arguments after the program name, without <paramref name="remove"/> (flags without a value) and without
        /// -waitPid and its value, with <paramref name="add"/> appended when not present.</summary>
        public static List<string> Restart(IList<string> argv, IEnumerable<string> remove = null, IEnumerable<string> add = null)
        {
            var drop = new HashSet<string>(remove ?? Array.Empty<string>(), StringComparer.Ordinal);
            var src = (argv ?? Array.Empty<string>()).Skip(1).ToList();
            var l = new List<string>();
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i] == WaitPidFlag) { i++; continue; }
                if (!drop.Contains(src[i])) l.Add(src[i]);
            }
            foreach (var a in add ?? Array.Empty<string>()) if (!l.Contains(a)) l.Add(a);
            return l;
        }

        /// <summary>One argument quoted for a Windows command line.</summary>
        public static string Quote(string a)
        {
            if (a == null) a = "";
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\v' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in a)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string Join(IEnumerable<string> args) => string.Join(" ", (args ?? Array.Empty<string>()).Select(Quote));
    }
}
