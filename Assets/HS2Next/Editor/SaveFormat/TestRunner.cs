// Check collector of the save-format tests (shared by the plain-Mono harness Tests/TestMain.cs in
// staging and by Editor/SaveFormatTest.cs in Unity): counts, failure lines, a scratch directory.
using System;
using System.Collections.Generic;

namespace HS2Next.Save.Tests
{
    public sealed class TestRunner
    {
        public int Passed, Failed;
        public string Group;
        public readonly List<string> Failures = new List<string>();
        /// <summary>Every FAIL and Info line in order (the Unity runner writes them to its report).</summary>
        public readonly List<string> Lines = new List<string>();
        /// <summary>Scratch directory for files (fresh per run).</summary>
        public string TempDir;
        /// <summary>Fixture folder: staging `Fixtures\` (plain Mono), repository `Tests/SaveFormat/` (Unity).</summary>
        public string FixturesDir;

        public void Check(bool ok, string what)
        {
            if (ok) { Passed++; return; }
            Failed++;
            string msg = Group + ": FAIL " + what;
            Failures.Add(msg);
            Lines.Add(msg);
            Console.WriteLine("  " + msg);
        }

        public void Equal<T>(T expected, T actual, string what)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            Check(ok, ok ? what : what + " (expected " + Show(expected) + ", got " + Show(actual) + ")");
        }

        static string Show(object o) => o == null ? "null" : o is string s ? "\"" + (s.Length > 300 ? s.Substring(0, 300) + "…" : s) + "\"" : o.ToString();

        /// <summary>Runs <paramref name="a"/> and checks that it throws <typeparamref name="TEx"/>; returns the exception (or null).</summary>
        public TEx Throws<TEx>(Action a, string what) where TEx : Exception
        {
            try { a(); }
            catch (TEx e) { Check(true, what); return e; }
            catch (Exception e) { Check(false, what + " (threw " + e.GetType().Name + ": " + e.Message + ")"); return null; }
            Check(false, what + " (no exception)");
            return null;
        }

        /// <summary>Checks that <paramref name="a"/> throws a SaveFormatException with <paramref name="code"/>.</summary>
        public SaveFormatException ThrowsCode(SaveErrorCode code, Action a, string what)
        {
            try { a(); }
            catch (SaveFormatException e)
            {
                Check(e.Code == code, what + (e.Code == code ? "" : " (got " + e.Code + ": " + e.Message + ")"));
                return e;
            }
            catch (Exception e) { Check(false, what + " (threw " + e.GetType().Name + ": " + e.Message + ")"); return null; }
            Check(false, what + " (no exception, expected " + code + ")");
            return null;
        }

        public void Info(string line)
        {
            Lines.Add(Group + ": " + line);
            Console.WriteLine("  " + Group + ": " + line);
        }
    }
}
