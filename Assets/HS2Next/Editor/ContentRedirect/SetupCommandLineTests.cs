// Unity-free check of the restart's command line (Runtime/App/Setup/CommandLine.cs), plain Mono: Tools/mono_tests.ps1 -Suite content.
//   SetupCommandLineTests  the arguments after the program, flags removed and added once, quoted with the Windows rules
using System.Linq;
using HS2Next.App.Setup;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    public static class SetupCommandLineTests
    {
        public static void Run(TestRunner t)
        {
            const char B = '\\', Q = '"';
            string bs = B.ToString(), q = Q.ToString();
            var argv = new[] { "HS2Next.exe", "-settings", "a b" + bs + "c.json", "-setup", "-bridgePort", "17322" };
            var r = CommandLine.Restart(argv, new[] { "-setup" }, new[] { "-noUi" });
            t.Check(string.Join("|", r) == "-settings|a b" + bs + "c.json|-bridgePort|17322|-noUi", "Restart drops the program and -setup, appends -noUi: " + string.Join("|", r));
            t.Check(CommandLine.Restart(argv, null, new[] { "-setup" }).Count(a => a == "-setup") == 1, "a flag already given is not added twice");
            t.Check(CommandLine.Quote("plain") == "plain" && CommandLine.Quote("") == q + q, "a plain argument is not quoted; an empty one is");
            t.Check(CommandLine.Quote("a b") == q + "a b" + q, "spaces are quoted");
            t.Check(CommandLine.Quote("x" + q + "y") == q + "x" + bs + q + "y" + q, "a quote is escaped with one backslash");
            string trailing = "dir" + bs + " x" + bs;
            t.Check(CommandLine.Quote(trailing) == q + "dir" + bs + " x" + bs + bs + q, "a trailing backslash run is doubled before the closing quote: " + CommandLine.Quote(trailing));
            t.Check(CommandLine.Quote("a" + bs + bs + q + "b") == q + "a" + bs + bs + bs + bs + bs + q + "b" + q, "backslashes before a quote are doubled, plus one for the quote");
            t.Check(CommandLine.Join(new[] { "-a", "b c" }) == "-a " + q + "b c" + q, "Join");
            var again = new[] { "HS2Next.exe", "-setup", "-waitPid", "4242", "-noUi" };
            t.Check(string.Join("|", CommandLine.Restart(again, new[] { "-setup" })) == "-noUi", "a restart drops -waitPid and its value (and -setup)");
            t.Check(CommandLine.WaitPid(again) == 4242 && CommandLine.WaitPid(new[] { "x", "-waitPid" }) == 0 && CommandLine.WaitPid(new[] { "x", "-waitPid", "z" }) == 0,
                "WaitPid reads the PID after -waitPid (0 without a number)");
        }
    }
}
