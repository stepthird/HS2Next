// Unity-free checks of the data root, the folder fallback, the one-app lock and the cache folder names
// (Runtime/App/Setup/AppRoots.cs, InstanceLock.cs), plain Mono: Tools/mono_tests.ps1 -Suite content (fixtures = the project root).
//   AppRootsTests  UP1 the order editor > portable.txt (any case) > HS2NEXT_DATA_ROOT (players only) > the per-user folder, and
//                  the line that names mode and root; UP2 the defaults under the root and the resolution of given and relative
//                  folders; UP3 the settings file to read (none is not an error, a missing -settings file is); UP4 the fallback
//                  of an unwritable folder outside the per-user folder, the failure under it, the probe file removed; UP5 the
//                  lock refused while another handle holds it, released and deleted on close; UP6 the thumbnail folder names
//                  (unchanged up to 40 characters, shortened deterministically, no collision over the GUIDs of the zipmod
//                  scan cache Cache/modlists-*.tsv when present).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.App.Setup;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    public static class AppRootsTests
    {
        public static void Run(TestRunner t)
        {
            string tmp = Path.Combine(t.TempDir ?? Path.GetTempPath(), "approots_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            Order(t, tmp);
            Defaults(t, tmp);
            SettingsFile(t, tmp);
            Fallback(t, tmp);
            Lock(t, tmp);
            ThumbNames(t);
        }

        static Func<string, string> Env(string value) => name => name == AppRoots.EnvDataRoot ? value : null;

        // UP1
        static void Order(TestRunner t, string tmp)
        {
            string exe = Path.Combine(tmp, "exe dir");
            string pdp = Path.Combine(tmp, "LocalLow", "stp3", "HS2Next");
            string over = Path.Combine(tmp, "データ root");
            Directory.CreateDirectory(exe);
            var r = AppRoots.Resolve(true, exe, pdp, Env(over));
            t.Check(r.Mode == DataRootMode.Editor && r.DataRoot == exe && r.ExeDir == exe, "UP1 editor: the project folder, HS2NEXT_DATA_ROOT ignored (" + r + ")");
            r = AppRoots.Resolve(false, exe, pdp, Env(null));
            t.Check(r.Mode == DataRootMode.PerUser && r.DataRoot == pdp, "UP1 player without marker or variable: the per-user folder (" + r + ")");
            r = AppRoots.Resolve(false, exe, pdp, Env("   "));
            t.Check(r.Mode == DataRootMode.PerUser, "UP1 a blank HS2NEXT_DATA_ROOT is not set");
            r = AppRoots.Resolve(false, exe, pdp, Env(over));
            t.Check(r.Mode == DataRootMode.Override && r.DataRoot == over && r.ExeDir == exe, "UP1 HS2NEXT_DATA_ROOT: that folder (" + r + ")");
            string marker = Path.Combine(exe, "PORTABLE.TXT");
            File.WriteAllText(marker, "");
            r = AppRoots.Resolve(false, exe, pdp, Env(over));
            t.Check(r.Mode == DataRootMode.Portable && r.DataRoot == exe, "UP1 portable.txt (any case) wins over HS2NEXT_DATA_ROOT: the exe folder (" + r + ")");
            r = AppRoots.Resolve(true, exe, pdp, Env(over));
            t.Check(r.Mode == DataRootMode.Editor, "UP1 the Editor ignores portable.txt");
            File.Delete(marker);
            Directory.CreateDirectory(Path.Combine(exe, "portable.txt"));   // a folder of that name is not the marker
            r = AppRoots.Resolve(false, exe, pdp, Env(null));
            t.Check(r.Mode == DataRootMode.PerUser, "UP1 a folder named portable.txt is not the marker");
            Directory.Delete(Path.Combine(exe, "portable.txt"));
            string line = AppRoots.Resolve(false, exe, pdp, Env(over)).ToString();
            t.Check(line.Contains("mode override") && line.Contains("data root " + over) && line.Contains("exe folder " + exe), "UP1 the logged line names mode and root: " + line);
            t.Equal("perUser", AppRoots.Resolve(false, exe, pdp, Env(null)).ModeName, "UP1 mode name perUser");
            r = AppRoots.Resolve(false, exe, pdp, Env(Path.Combine(tmp, "rel") + Path.DirectorySeparatorChar));
            t.Check(r.DataRoot == Path.Combine(tmp, "rel"), "UP1 the root is normalised without a trailing separator (" + r.DataRoot + ")");
        }

        // UP2
        static void Defaults(TestRunner t, string tmp)
        {
            string root = Path.Combine(tmp, "root with space");
            var r = new AppRoots(DataRootMode.Override, root, Path.Combine(tmp, "exe"), Path.Combine(tmp, "pdp"));
            t.Equal(Path.Combine(root, "Config", "settings.local.json"), r.DefaultSettingsFile, "UP2 default settings file");
            t.Equal(Path.Combine(root, "UserData"), r.UserDataOf(null), "UP2 default UserData");
            t.Equal(Path.Combine(root, "Cache"), r.CacheDirOf(""), "UP2 default cache");
            string abs = Path.Combine(tmp, "elsewhere", "ud");
            t.Equal(abs, r.UserDataOf(abs), "UP2 an absolute userData wins over the default");
            t.Equal(Path.Combine(root, "mine", "ud"), r.UserDataOf("mine/ud"), "UP2 a relative userData resolves against the data root");
            t.Equal(Path.GetFullPath(Path.Combine(root, "..", "c")), r.CacheDirOf("../c"), "UP2 a relative cacheDir resolves against the data root");
            t.Equal(Path.Combine(root, "hs2"), r.Resolve("hs2"), "UP2 a relative hs2Root resolves against the data root");
            t.Equal(Path.Combine(tmp, "pdp", "UserData"), r.FallbackUserData, "UP2 the UserData fallback lies in the per-user folder");
            t.Equal(Path.Combine(tmp, "pdp", "Cache"), r.FallbackCacheDir, "UP2 the cache fallback lies in the per-user folder");
            var longRoot = new AppRoots(DataRootMode.Override, Path.Combine(tmp, new string('x', 101)), tmp, tmp);
            t.Check(longRoot.IsLong && !r.IsLong, "UP2 a data root over 100 characters is marked long");
            t.Check(AppRoots.IsUnder(Path.Combine(tmp, "pdp", "UserData"), Path.Combine(tmp, "PDP")) && !AppRoots.IsUnder(Path.Combine(tmp, "pdp2"), Path.Combine(tmp, "pdp")),
                "UP2 IsUnder: case-insensitive, whole folder names");
        }

        // UP3
        static void SettingsFile(TestRunner t, string tmp)
        {
            string root = Path.Combine(tmp, "settings root");
            var r = new AppRoots(DataRootMode.Override, root, tmp, tmp);
            t.Check(r.SettingsFileToRead(null, false) == null, "UP3 no settings file: the defaults, no exception");
            Directory.CreateDirectory(r.ConfigDir);
            File.WriteAllText(r.DefaultSettingsFile, "{}");
            t.Equal(r.DefaultSettingsFile, r.SettingsFileToRead(null, false), "UP3 the default file is read when it exists");
            string other = Path.Combine(tmp, "other.json");
            t.Throws<FileNotFoundException>(() => r.SettingsFileToRead(other, false), "UP3 a missing file named by -settings is an error");
            t.Check(r.SettingsFileToRead(other, true) == null, "UP3 a missing -settings file with -gameRoot: the defaults");
            File.WriteAllText(other, "{}");
            t.Equal(other, r.SettingsFileToRead(other, false), "UP3 -settings wins over the default file");
        }

        // UP4
        static void Fallback(TestRunner t, string tmp)
        {
            string pdp = Path.Combine(tmp, "pdp");
            string blocker = Path.Combine(tmp, "blocker");
            File.WriteAllText(blocker, "a file where a folder should be");
            var fb = new List<FolderFallback>();
            string want = Path.Combine(blocker, "UserData");
            string used = AppFolders.Prepare("userData", want, Path.Combine(pdp, "UserData"), pdp, CreateUserData, fb);
            t.Equal(Path.Combine(pdp, "UserData"), used, "UP4 an unwritable UserData outside the per-user folder falls back to <per-user>/UserData");
            t.Check(fb.Count == 1 && fb[0].Kind == "userData" && fb[0].From == want && fb[0].To == used && fb[0].Reason.Length > 0, "UP4 the fallback is recorded: " + (fb.Count > 0 ? fb[0].ToString() : "none"));
            t.Check(Directory.Exists(Path.Combine(used, "scene")) && Directory.Exists(Path.Combine(used, "Bridge", "shots")), "UP4 the fallback folder gets its subfolders");
            t.Check(NoProbe(used), "UP4 the probe file is removed");
            // a writable folder: unchanged, no fallback, no probe left
            fb.Clear();
            string ok = Path.Combine(tmp, "writable", "UserData");
            t.Equal(ok, AppFolders.Prepare("userData", ok, Path.Combine(pdp, "UserData"), pdp, CreateUserData, fb), "UP4 a writable folder is used as it is");
            t.Check(fb.Count == 0 && NoProbe(ok), "UP4 no fallback and no probe file left for a writable folder");
            // under the per-user folder: the stage's message
            string pdpFile = Path.Combine(tmp, "pdp_is_a_file");
            File.WriteAllText(pdpFile, "x");
            var e = t.Throws<FolderException>(() => AppFolders.Prepare("userData", Path.Combine(pdpFile, "UserData"), Path.Combine(pdpFile, "UserData"), pdpFile, CreateUserData, fb),
                "UP4 an unwritable folder under the per-user folder fails");
            t.Check(e != null && e.Message.StartsWith("HS2Next cannot write its folder " + Path.Combine(pdpFile, "UserData") + ": ", StringComparison.Ordinal),
                "UP4 the failure names the folder and the reason: " + e?.Message);
            // the fallback fails too
            e = t.Throws<FolderException>(() => AppFolders.Prepare("cache", Path.Combine(blocker, "Cache"), Path.Combine(pdpFile, "Cache"), Path.Combine(tmp, "pdp_other"), null, fb),
                "UP4 a fallback that cannot be written fails too");
            t.Check(e != null && e.Folder == Path.Combine(pdpFile, "Cache") && e.Message.Contains("after " + Path.Combine(blocker, "Cache")), "UP4 the failure names the fallback and the first folder: " + e?.Message);
            // a folder the user may not write (an access rule that denies writing): the portable mode in a read-only place
            string ro = Path.Combine(tmp, "readonly");
            Directory.CreateDirectory(ro);
            if (Icacls("\"" + ro + "\" /deny \"" + Environment.UserDomainName + "\\" + Environment.UserName + ":(OI)(CI)(W,AD,WD)\"") == 0)
            {
                try
                {
                    fb.Clear();
                    used = AppFolders.Prepare("userData", Path.Combine(ro, "UserData"), Path.Combine(pdp, "UserData2"), pdp, CreateUserData, fb);
                    t.Check(used == Path.Combine(pdp, "UserData2") && fb.Count == 1, "UP4 a read-only folder falls back (" + (fb.Count > 0 ? fb[0].Reason : "no fallback") + ")");
                }
                finally { Icacls("\"" + ro + "\" /remove:d \"" + Environment.UserDomainName + "\\" + Environment.UserName + "\""); }
            }
            else t.Info("UP4 read-only case skipped: icacls could not set the deny rule");
        }

        static void CreateUserData(string ud)
        {
            foreach (var n in new[] { "scene", "chara", "coordinate", "pose", "Bridge", Path.Combine("Bridge", "shots"), "cap", "bg", "frame" })
                Directory.CreateDirectory(Path.Combine(ud, n));
        }

        static bool NoProbe(string dir) => !Directory.EnumerateFiles(dir, ".write-probe-*").Any();

        static int Icacls(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("icacls", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                return p.WaitForExit(15000) ? p.ExitCode : -1;
            }
            catch (Exception) { return -1; }
        }

        // UP5
        static void Lock(TestRunner t, string tmp)
        {
            string ud = Path.Combine(tmp, "lock UserData");
            Directory.CreateDirectory(ud);
            string file = Path.Combine(ud, InstanceLock.FileName);
            // another process's lock, as the operating system shows it to this one: an open handle without sharing
            using (var other = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose))
            {
                var l = InstanceLock.TryAcquire(ud, out string err);
                t.Check(l == null && err == InstanceLock.RefusedMessage(ud), "UP5 a second app on the same UserData is refused: " + err);
                t.Check(err != null && err.StartsWith("HS2Next is already running with this folder", StringComparison.Ordinal), "UP5 the refusal's text");
            }
            t.Check(!File.Exists(file), "UP5 the other handle's file is gone after it closed");
            var a = InstanceLock.TryAcquire(ud, out string e1);
            t.Check(a != null && a.IsHeld && e1 == null && File.Exists(file), "UP5 the lock is taken once the folder is free");
            bool refused = false;
            try { using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { } }
            catch (IOException) { refused = true; }
            t.Check(refused, "UP5 while held, nobody else can open the lock file");
            var b = InstanceLock.TryAcquire(ud, out string e2);
            t.Check(b != null && e2 == null && !a.IsHeld && b.IsHeld, "UP5 the same process takes it again (an Editor play session after one that did not end): the earlier handle is closed");
            b.Dispose();
            t.Check(!File.Exists(file), "UP5 released: the lock file is deleted");
            b.Dispose();
            a.Dispose();
            var c = InstanceLock.TryAcquire(ud, out _);
            t.Check(c != null, "UP5 taken again after the release");
            c?.Dispose();
            var missing = InstanceLock.TryAcquire(Path.Combine(tmp, "no such folder"), out string e3);
            t.Check(missing == null && e3 != null && e3.Contains("cannot be opened"), "UP5 a missing UserData is an error, not a refusal: " + e3);
        }

        // UP6
        static void ThumbNames(TestRunner t)
        {
            t.Equal("stp3.sample.guid", CacheNames.ThumbFolder("stp3.sample.guid"), "UP6 a short GUID is unchanged");
            string forty = new string('a', 40);
            t.Equal(forty, CacheNames.ThumbFolder(forty), "UP6 40 characters stay unchanged");
            t.Equal("a_b_c", CacheNames.ThumbFolder("a:b?c"), "UP6 characters invalid in a file name become _");
            string longGuid = "com.example.some.very.long.zipmod.guid.that.goes.on.and.on.for.many.characters";
            string s1 = CacheNames.ThumbFolder(longGuid);
            t.Check(s1.Length == 41 && s1.StartsWith(longGuid.Substring(0, 32) + "~", StringComparison.Ordinal), "UP6 a long GUID becomes 32 characters, ~ and 8 hex digits: " + s1);
            t.Equal("com.example.some.very.long.zipmo~2c7092c6", s1, "UP6 the shortened name is fixed across runs and machines");
            t.Check(CacheNames.ThumbFolder(longGuid + "x") != s1, "UP6 two long GUIDs with the same start differ");
            // every GUID of the zipmod scan cache
            var guids = new HashSet<string>(StringComparer.Ordinal);
            string cache = Path.Combine(t.FixturesDir ?? "", "Cache");
            if (Directory.Exists(cache))
                foreach (var f in Directory.GetFiles(cache, "modlists-*.tsv"))
                    foreach (var l in File.ReadLines(f))
                    {
                        if (!l.StartsWith("Z\t", StringComparison.Ordinal)) continue;
                        var p = l.Split('\t');
                        if (p.Length > 3 && p[3].Length > 0) guids.Add(p[3]);
                    }
            if (guids.Count == 0) { t.Info("UP6 corpus skipped: no Cache/modlists-*.tsv under the fixtures"); return; }
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var clashes = new List<string>();
            int shortened = 0, maxLen = 0;
            foreach (var g in guids)
            {
                string n = CacheNames.ThumbFolder(g);
                if (n != g) shortened++;
                maxLen = Math.Max(maxLen, n.Length);
                if (byName.TryGetValue(n, out var prev) && !string.Equals(prev, g, StringComparison.OrdinalIgnoreCase)) clashes.Add(prev + " / " + g);
                else byName[n] = g;
            }
            int longOnes = guids.Count(g => g.Length > CacheNames.MaxFolderChars);
            t.Check(clashes.Count == 0, "UP6 no two GUIDs of the scan cache share a folder (" + guids.Count + " GUIDs, " + longOnes + " over 40 characters)" +
                                        (clashes.Count > 0 ? ": " + string.Join("; ", clashes.Take(5)) : ""));
            t.Check(maxLen <= 41, "UP6 the longest folder name is " + maxLen + " characters (at most 41)");
            t.Info("UP6 corpus: " + guids.Count + " GUIDs, " + longOnes + " over 40 characters, " + shortened + " names changed, longest folder " + maxLen);
        }
    }
}
