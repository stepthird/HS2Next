// Unity-free checks of the first-run guide's parts (Runtime/App/Setup), plain Mono: Tools/mono_tests.ps1 -Suite content
// (fixtures = the project root; fixture trees are written to the run's temp folder):
//   SetupInstallTests      the install check's levels on fixture trees (error / warning / ok), the normalisation of an abdata
//                          folder, an exe and a parent folder ("did you mean"), the zipmod count; the reference install named by
//                          Config/settings.local.json when present (read only: ok, its zipmod count and the time of the walk)
//   SetupSuggestionTests   the registry output parsed (value present / key absent / value absent / expandable), the drive
//                          scan finding a planted install at depth 1 and 2 of a drive-like root, skipping system folders,
//                          de-duplicated against the settings' hs2Root, and the time budget respected
//   SetupStageTests        the stage's decision (not needed with a valid hs2Root in under 1 ms, needed without one or with a
//                          failing one, the skip of this guide version, -setup), setup.json round trip and the resume page
//   SettingsWriteTests     the settings file written keeping comments, unknown members and their order, blocks created,
//                          null removing a member; the key table's types as the settings stage reads them; unknown keys with
//                          the nearest known key
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.App.Setup;
using Newtonsoft.Json.Linq;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    static class SetupFixtures
    {
        /// <summary>A fake install under <paramref name="dir"/>: abdata\abdata, chara\oo_base, one list bundle, the data folder, mods.</summary>
        public static string Install(string dir, bool dataFolder = true, bool baseData = true, bool manifest = true, int zipmods = 0)
        {
            Directory.CreateDirectory(Path.Combine(dir, "abdata", "chara"));
            Directory.CreateDirectory(Path.Combine(dir, "abdata", "list", "characustom"));
            if (manifest) File.WriteAllBytes(Path.Combine(dir, "abdata", "abdata"), new byte[] { 1 });
            if (baseData)
            {
                File.WriteAllBytes(Path.Combine(dir, "abdata", "chara", "oo_base.unity3d"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(dir, "abdata", "list", "characustom", "00.unity3d"), new byte[] { 1 });
            }
            if (dataFolder) Directory.CreateDirectory(Path.Combine(dir, "HoneySelect2_Data"));
            File.WriteAllBytes(Path.Combine(dir, "HoneySelect2.exe"), new byte[] { 0x4d, 0x5a });
            var mods = Path.Combine(dir, "mods", "Sideloader Modpack", "sub");
            Directory.CreateDirectory(mods);
            for (int i = 0; i < zipmods; i++) File.WriteAllBytes(Path.Combine(i % 2 == 0 ? mods : Path.GetDirectoryName(mods), "m" + i + ".zipmod"), new byte[] { 0x50, 0x4b });
            File.WriteAllBytes(Path.Combine(mods, "readme.txt"), new byte[] { 1 });
            return dir;
        }

        /// <summary>An absolute path on a drive letter (built at run time, so the source names no drive path).</summary>
        public static string Abs(char drive, string rest) => drive + ":" + "\\" + rest;

        public static string Fresh(TestRunner t, string name)
        {
            string d = Path.Combine(t.TempDir, "setup_" + name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        /// <summary>The hs2Root of the project's settings file (null when absent).</summary>
        public static string ReferenceInstall(TestRunner t)
        {
            try
            {
                string f = Path.Combine(t.FixturesDir ?? "", "Config", "settings.local.json");
                if (!File.Exists(f)) return null;
                string r = (string)JObject.Parse(File.ReadAllText(f))["hs2Root"];
                return string.IsNullOrEmpty(r) ? null : Path.GetFullPath(Path.Combine(t.FixturesDir, r));
            }
            catch (Exception) { return null; }
        }
    }

    public static class SetupInstallTests
    {
        public static void Run(TestRunner t)
        {
            string root = SetupFixtures.Fresh(t, "install");
            string ok = SetupFixtures.Install(Path.Combine(root, "Game One"), zipmods: 5);
            var v = Hs2InstallCheck.Check(ok, countZipmods: true);
            t.Check(v.Level == InstallLevel.Ok && v.Code == "ok" && v.Zipmods == 5, $"a complete install is ok with its 5 zipmods ({v.LevelName} {v.Code} {v.Zipmods})");
            t.Check(v.Message.Contains("5 mods"), "the ok line names the zipmod count: " + v.Message);
            t.Check(v.Ms < 50, $"the check takes under 50 ms ({v.Ms:F2} ms)");

            var missing = Hs2InstallCheck.Check(Path.Combine(root, "nowhere"));
            t.Check(missing.Level == InstallLevel.Error && missing.Code == "unreadable", "a missing folder: error unreadable (" + missing.Code + ")");
            t.Check(Hs2InstallCheck.Check("").Code == "unreadable" && Hs2InstallCheck.Check(null).Code == "unreadable", "an empty path: error unreadable");

            string noAb = Path.Combine(root, "Empty");
            Directory.CreateDirectory(noAb);
            File.WriteAllText(Path.Combine(noAb, "x.txt"), "x");
            var na = Hs2InstallCheck.Check(noAb);
            t.Check(na.Level == InstallLevel.Error && na.Code == "noAbdata", "a folder without abdata\\abdata: error noAbdata (" + na.Code + ")");

            string inc = SetupFixtures.Install(Path.Combine(root, "Incomplete"), baseData: false);
            var iv = Hs2InstallCheck.Check(inc);
            t.Check(iv.Level == InstallLevel.Error && iv.Code == "incomplete", "no base character data: error incomplete (" + iv.Code + ")");

            string other = SetupFixtures.Install(Path.Combine(root, "Other Game"), dataFolder: false);
            var ov = Hs2InstallCheck.Check(other, countZipmods: true);
            t.Check(ov.Level == InstallLevel.Warning && ov.Code == "notHs2" && ov.Usable, "abdata without an HS2 data folder: warning notHs2, usable (" + ov.Code + ")");
            Directory.CreateDirectory(Path.Combine(other, "StudioNEOV2_Data"));
            t.Check(Hs2InstallCheck.Check(other).Level == InstallLevel.Ok, "StudioNEOV2_Data alone makes it ok");

            // normalisation
            t.Check(Hs2InstallCheck.Normalise(Path.Combine(ok, "abdata")) == ok, "a picked abdata folder stands for its parent");
            t.Check(Hs2InstallCheck.Normalise(Path.Combine(ok, "HoneySelect2.exe")) == ok, "a picked exe stands for its folder");
            t.Check(Hs2InstallCheck.Normalise("  \"" + ok + "\\\"  ") == ok, "quotes, blanks and a trailing separator are trimmed");
            t.Check(Hs2InstallCheck.Check(Path.Combine(ok, "abdata")).Level == InstallLevel.Ok, "checking the abdata folder checks the install");
            var parent = Hs2InstallCheck.Check(Path.GetDirectoryName(ok + "\\x"));
            t.Check(parent.Path == ok, "the folder itself after a trailing name");
            string holder = Path.Combine(root, "Holder");
            SetupFixtures.Install(Path.Combine(holder, "HS2"));
            Directory.CreateDirectory(Path.Combine(holder, "Other"));
            var hv = Hs2InstallCheck.Check(holder);
            t.Check(hv.Level == InstallLevel.Error && hv.DidYouMean == Path.Combine(holder, "HS2"), "a parent of one install proposes it (" + hv.DidYouMean + ")");
            SetupFixtures.Install(Path.Combine(holder, "HS2 copy"));
            t.Check(Hs2InstallCheck.Check(holder).DidYouMean == null, "a parent of two installs proposes none");
            t.Check(Hs2InstallCheck.CountZipmods(Path.Combine(root, "nowhere")) == 0, "no mods folder counts 0");

            // the reference install (read only)
            string reference = SetupFixtures.ReferenceInstall(t);
            if (reference == null || !Directory.Exists(reference)) { t.Info("no reference install in Config/settings.local.json: skipped"); return; }
            var sw = Stopwatch.StartNew();
            var rv = Hs2InstallCheck.Check(reference, countZipmods: false);
            double checkMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            int n = Hs2InstallCheck.CountZipmods(Path.Combine(rv.Path ?? reference, "mods"));
            double walkMs = sw.Elapsed.TotalMilliseconds;
            t.Check(rv.Level == InstallLevel.Ok, "the reference install is ok (" + rv.Code + ")");
            t.Check(n > 0, "the reference install has zipmods (" + n + ")");
            t.Check(checkMs < 50, $"the reference check takes under 50 ms ({checkMs:F1} ms)");
            t.Info($"FR2 reference install: ok, {n} zipmods, check {checkMs:F1} ms, zipmod walk {walkMs:F0} ms");
        }
    }

    public static class SetupSuggestionTests
    {
        public static void Run(TestRunner t)
        {
            // the registry output
            string games = SetupFixtures.Abs('G', "Games\\HoneySelect 2\\");
            string present = "\r\nHKEY_CURRENT_USER\\Software\\illusion\\HoneySelect2\\HoneySelect2\r\n    INSTALLDIR    REG_SZ    " + games + "\r\n\r\n";
            t.Check(InstallSuggestions.ParseRegOutput(present) == games, "a REG_SZ value is read with its spaces");
            t.Check(InstallSuggestions.ParseRegOutput("ERROR: The system was unable to find the specified registry key or value.\r\n") == null, "an absent key or value gives none");
            t.Check(InstallSuggestions.ParseRegOutput("\r\nHKEY_CURRENT_USER\\Software\\illusion\\HoneySelect2\\HoneySelect2\r\n    Screenmanager Resolution Width_h182942802    REG_DWORD    0x780\r\n") == null,
                "a key holding other values only gives none");
            t.Check(InstallSuggestions.ParseRegOutput("    INSTALLDIRX    REG_SZ    " + SetupFixtures.Abs('G', "x") + "\r\n    INSTALLDIR    REG_SZ    \r\n") == null, "a longer name or an empty value is not taken");
            Environment.SetEnvironmentVariable("HS2N_SETUP_TEST_ROOT", SetupFixtures.Abs('G', "Root"));
            t.Check(InstallSuggestions.ParseRegOutput("    INSTALLDIR    REG_EXPAND_SZ    %HS2N_SETUP_TEST_ROOT%\\HS2\r\n") == SetupFixtures.Abs('G', "Root\\HS2"), "REG_EXPAND_SZ is expanded");
            string reg = null;
            try { reg = InstallSuggestions.QueryRegistry(1500); } catch (Exception e) { t.Info("reg.exe query threw " + e.GetType().Name); }
            t.Info("the registry value on this machine: " + (reg == null ? "absent" : "present"));

            // the scan on a drive-like root
            string drive = SetupFixtures.Fresh(t, "drive");
            string d1 = SetupFixtures.Install(Path.Combine(drive, "HS2 level one"));
            string d2 = SetupFixtures.Install(Path.Combine(drive, "Games", "HS2 level two"));
            SetupFixtures.Install(Path.Combine(drive, "Games", "Deeper", "HS2 level three"));
            SetupFixtures.Install(Path.Combine(drive, "Windows", "HS2 in windows"));
            SetupFixtures.Install(Path.Combine(drive, "$Hidden", "HS2"));
            SetupFixtures.Install(Path.Combine(drive, "Other"), dataFolder: false);
            var r = InstallSuggestions.Gather(d1, 3000, _ => d2 + "\\", new[] { drive });
            var paths = r.Items.Select(i => i.Path + "|" + i.From).ToList();
            t.Check(r.Items.Count == 2, "settings + registry + scan de-duplicated to 2: " + string.Join("; ", paths));
            t.Check(r.Items.Count >= 2 && r.Items[0].Path == d1 && r.Items[0].From == "settings" && r.Items[1].Path == d2 && r.Items[1].From == "registry",
                "the settings' folder first, then the registry's");
            var scan = InstallSuggestions.Gather(null, 3000, _ => null, new[] { drive });
            var found = scan.Items.Select(i => i.Path).ToList();
            t.Check(found.Contains(d1) && found.Contains(d2) && found.Count == 2,
                "the scan finds the installs at depth 1 and 2 only, not under Windows or $ folders, not a game without the data folder: " + string.Join("; ", found));
            t.Check(scan.Items.All(i => i.From == "scan") && !scan.BudgetSpent, "found by the scan within the budget (" + scan.Visited + " folders)");
            var sw = Stopwatch.StartNew();
            var spent = InstallSuggestions.Gather(null, 0, _ => null, new[] { drive });
            t.Check(spent.BudgetSpent && sw.ElapsedMilliseconds < 500, $"a spent budget stops the scan ({sw.ElapsedMilliseconds} ms, budget spent {spent.BudgetSpent})");
            var wide = InstallSuggestions.Gather(null, 3000, _ => null, null);
            t.Check(wide.Ms < 3600, $"the fixed drives are scanned within the budget ({wide.Ms:F0} ms, {wide.Visited} folders, {wide.Items.Count} found{(wide.BudgetSpent ? ", budget spent" : "")})");
            string reference = SetupFixtures.ReferenceInstall(t);
            if (reference != null && Directory.Exists(reference) && Path.GetDirectoryName(reference)?.Length <= 3)
                t.Check(wide.Items.Any(i => string.Equals(i.Path, reference, StringComparison.OrdinalIgnoreCase)), "the drive scan finds the reference install one folder below its drive root");
        }
    }

    public static class SetupStageTests
    {
        public static void Run(TestRunner t)
        {
            string root = SetupFixtures.Fresh(t, "stage");
            string ok = SetupFixtures.Install(Path.Combine(root, "HS2"));
            Hs2InstallCheck.Check(ok);   // warm the file-system calls once
            var none = new string[0];
            var d = SetupStage.Decide(ok, none, null);
            t.Check(!d.Needed && d.Reason.StartsWith("HS2 folder ok"), "a valid hs2Root: not needed (" + d.Reason + ")");
            double best = double.MaxValue;
            for (int i = 0; i < 5; i++) best = Math.Min(best, SetupStage.Decide(ok, none, null).Ms);
            t.Check(best < 1.0, $"the decision takes under 1 ms ({best:F3} ms)");
            var e = SetupStage.Decide("", none, null);
            t.Check(e.Needed && e.Reason == "no HS2 folder is set", "no hs2Root: needed (" + e.Reason + ")");
            var bad = SetupStage.Decide(Path.Combine(root, "missing"), none, null);
            t.Check(bad.Needed && bad.Install?.Code == "unreadable", "a failing hs2Root: needed (" + bad.Reason + ")");
            var skipped = new SetupState { Skipped = true };
            t.Check(!SetupStage.Decide("", none, skipped).Needed, "skipped this guide version: not needed");
            t.Check(SetupStage.Decide("", none, new SetupState { Skipped = true, GuideVersion = SetupStage.GuideVersion - 1 }).Needed, "a skip of an older guide version does not hold");
            t.Check(SetupStage.Decide(ok, new[] { "x.exe", "-setup" }, null).Needed && SetupStage.Decide(ok, new[] { "-setup" }, skipped).Forced, "-setup: needed even with a valid folder or a skip");

            // setup.json
            string ud = Path.Combine(root, "UserData");
            var s0 = SetupState.Load(ud);
            t.Check(s0.LastPage == null && !s0.Skipped && s0.GuideVersion == SetupStage.GuideVersion, "no setup.json: a new state");
            var s = new SetupState { LastPage = "mods", Choices = new JObject { ["hs2Root"] = ok, ["modsFolders"] = new JArray(SetupFixtures.Abs('G', "mods")) } };
            s.Save(ud);
            var s1 = SetupState.Load(ud);
            t.Check(s1.LastPage == "mods" && (string)s1.Choices["hs2Root"] == ok && s1.Choices["modsFolders"]?.Count() == 1, "setup.json round trip (page, choices)");
            t.Check(SetupStage.ResumePage(s1) == "mods", "a resume opens the last page of 1-4 (" + SetupStage.ResumePage(s1) + ")");
            t.Check(SetupStage.ResumePage(new SetupState { LastPage = "index" }) == "welcome" && SetupStage.ResumePage(new SetupState { LastPage = "ready", CompletedAt = "x" }) == "welcome",
                "a resume after the index page or a finish starts at the welcome page");
            File.WriteAllText(SetupState.PathOf(ud), "{ not json");
            t.Check(SetupState.Load(ud).LastPage == null, "an unreadable setup.json counts as absent");
            t.Check(!Directory.EnumerateFiles(Path.Combine(ud, "config")).Any(f => f.Contains(".tmp-")), "no temporary file is left");
            t.Check(SetupStage.CanSkip("hs2") && SetupStage.CanSkip("cache") && !SetupStage.CanSkip("welcome") && !SetupStage.CanSkip("index"), "Skip setup is offered on pages 1-4 only");
        }
    }

    public static class SettingsWriteTests
    {
        public static void Run(TestRunner t)
        {
            string root = SetupFixtures.Fresh(t, "settings");
            string f = Path.Combine(root, "Config", "settings.local.json");
            string hs2 = SetupFixtures.Abs('G', "HS2");
            var written = SettingsFile.Write(f, new[] { new KeyValuePair<string, JToken>("hs2Root", hs2) });
            t.Check(File.Exists(f) && written.SequenceEqual(new[] { "hs2Root" }), "a new file with the one key (folder created)");
            const string text = "{\n  \"_comment\": \"mine\",\n  \"hs2Root\": \"Old\",\n  \"futureKey\": {\"a\": 1},\n  \"bridge\": {\"_note\": \"x\", \"port\": 17322},\n  \"cacheDir\": \"Cache2\"\n}\n";
            File.WriteAllText(f, text);
            written = SettingsFile.Write(f, new[]
            {
                new KeyValuePair<string, JToken>("hs2Root", hs2), new KeyValuePair<string, JToken>("bridge.port", 17400),
                new KeyValuePair<string, JToken>("app.frameRate", 60), new KeyValuePair<string, JToken>("cacheDir", null),
                new KeyValuePair<string, JToken>("modsFolders", null),
            });
            var o = JObject.Parse(File.ReadAllText(f));
            var names = o.Properties().Select(p => p.Name).ToList();
            t.Check(string.Join(",", names) == "_comment,hs2Root,futureKey,bridge,app", "members keep their order, a new block goes last, null removes: " + string.Join(",", names));
            t.Check((string)o["_comment"] == "mine" && (int)o["futureKey"]["a"] == 1 && (string)o["bridge"]["_note"] == "x", "comments and unknown members keep their values");
            t.Check((string)o["hs2Root"] == hs2 && (int)o["bridge"]["port"] == 17400 && (int)o["app"]["frameRate"] == 60, "the set values are written");
            t.Check(written.Count == 4 && !written.Contains("modsFolders"), "the changed keys are reported (an absent member removed is no change): " + string.Join(",", written));
            var again = SettingsFile.Write(f, new[] { new KeyValuePair<string, JToken>("hs2Root", hs2) });
            t.Check(again.Count == 0, "writing the same value changes nothing");
            t.Check(!Directory.EnumerateFiles(Path.Combine(root, "Config")).Any(x => x.Contains(".tmp-")), "no temporary file is left");
            File.WriteAllText(f, "{ broken");
            t.Throws<FormatException>(() => SettingsFile.Write(f, new[] { new KeyValuePair<string, JToken>("hs2Root", "x") }), "a file that does not parse is not overwritten");
            t.Check(File.ReadAllText(f) == "{ broken", "the broken file keeps its bytes");

            // the key table and its types (the settings stage's rules)
            var all = SettingsSchema.All.Select(d => d.Key).ToList();
            var missingRows = SettingsKeys.All.Where(k => !SettingsSchema.Rows.Any(r => r.Key == k)).ToList();
            t.Check(missingRows.Count == 0, "every key the runtime reads has a row in the key table" + (missingRows.Count > 0 ? ": " + string.Join(", ", missingRows) : ""));
            t.Check(SettingsSchema.All.All(d => d.Section == null || SettingsSchema.Sections.Contains(d.Section)), "rows are in the four sections or none");
            t.Info("settings page rows: " + string.Join(", ", SettingsSchema.Sections.Select(s => s + " " + SettingsSchema.InSection(s).Count())));
            SettingDef D(string k) { SettingsSchema.TryGet(k, out var d); return d; }
            bool Bad(string k, JToken v) { try { SettingsSchema.Validate(D(k), v); return false; } catch (FormatException) { return true; } }
            t.Check(D("bridge.port") != null && Bad("bridge.port", 80) && Bad("bridge.port", "17322") && Bad("bridge.port", 1.5) && !Bad("bridge.port", 17400), "bridge.port: an integer in 1024-65535");
            t.Check(Bad("bridge.enabled", "true") && Bad("bridge.enabled", 1) && !Bad("bridge.enabled", false), "a bool must be true or false");
            t.Check(!Bad("bridge.token", true) && !Bad("bridge.token", "abc") && Bad("bridge.token", 5), "bridge.token: true, false or a string");
            t.Check(Bad("maps.maxMirrors", 17) && !Bad("maps.maxMirrors", 16) && Bad("app.frameRate", -1), "ranges as the stage reads them (maps.maxMirrors 0-16, app.frameRate 0-1000)");
            t.Check(Bad("modsFolders", SetupFixtures.Abs('G', "mods")) && Bad("modsFolders", new JArray(1)) && !Bad("modsFolders", new JArray(SetupFixtures.Abs('G', "mods"))), "modsFolders: an array of strings");
            t.Check(SettingsSchema.Validate(D("cacheDir"), "") == null && SettingsSchema.Validate(D("modsFolders"), new JArray()) == null, "an empty path or list removes the member (the default)");
            t.Check(Bad("render.aa", "msaa") && !Bad("render.aa", "fxaa") && Bad("render.shadowDistance", 5) && !Bad("content.unloadUnusedMinSec", 12.5), "choices and number ranges");
            t.Check(SettingsSchema.All.Where(d => d.Key != "plugins.disabled").All(d => d.Restart) && !D("plugins.disabled").Restart,
                "every key applies after a restart (the file is read at start), except plugins.disabled, which the Options toggles apply at once (Phase 6.1)");
            t.Check(SettingsSchema.FromCommandLine(D("bridge.port"), new[] { "x", "-bridgePort", "17400" }) && !SettingsSchema.FromCommandLine(D("bridge.port"), new[] { "x" }), "a command-line flag locks its key");

            // unknown keys with the nearest known key
            var unk = SettingsSchema.UnknownKeys(JObject.Parse("{\"_c\":1, \"hs2root\":\"x\", \"bridge\": {\"tokn\": true, \"port\": 1}, \"cachDir\": \"\", \"zzzunknown\": 1, \"render\": {\"aa\": \"smaa\"}}"));
            string U(string k) => unk.FirstOrDefault(u => u.Key == k)?.Suggestion;
            t.Check(unk.Count == 4, "4 unknown keys (comments and known keys are not): " + string.Join("; ", unk));
            t.Check(U("bridge.tokn") == "bridge.token" && U("hs2root") == "hs2Root" && U("cachDir") == "cacheDir" && unk.Any(u => u.Key == "zzzunknown" && u.Suggestion == null),
                "suggestions: bridge.tokn -> bridge.token, hs2root -> hs2Root, cachDir -> cacheDir, none for zzzunknown");
            t.Check(unk.First(u => u.Key == "bridge.tokn").ToString() == "bridge.tokn (did you mean bridge.token?)", "the log form names the suggestion");
            t.Check(SettingsSchema.Distance("kitten", "sitting") == 3 && SettingsSchema.Distance("abc", "abc") == 0 && SettingsSchema.Distance("abcdef", "a", 2) == 3, "edit distance");
        }
    }
}
