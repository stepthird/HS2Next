// Unity-free checks of the one migration (Runtime/App/Setup/UserDataMigration.cs), plain Mono: Tools/mono_tests.ps1 -Suite content.
//   UserDataMigrationTests  UM1 a fixture legacy UserData copies into an empty one (counts per kind, bytes, migration.json); UM2 a
//                           second run copies nothing (all same), a changed destination file is a conflict and keeps its bytes;
//                           UM3 a junction inside the source is refused and not entered; UM4 the endpoint, the token, the crash
//                           marker, temporary files and the bridge's output are not copied; UM5 the old settings file is copied
//                           only when the new root has none, relative paths made absolute against the old root, a userData naming
//                           the migrated folder dropped, comments and other members kept; UM6 the source is byte-identical before
//                           and after; UM7 not enough free space: a clear error and nothing copied; the offer's conditions
//                           (per-user and override roots offer, portable and the Editor do not).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HS2Next.App.Setup;
using Newtonsoft.Json.Linq;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    public static class UserDataMigrationTests
    {
        public static void Run(TestRunner t)
        {
            string tmp = Path.Combine(t.TempDir ?? Path.GetTempPath(), "migration_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string oldRoot = Path.Combine(tmp, "old player");
            string src = Path.Combine(oldRoot, "UserData");
            string outside = Path.Combine(tmp, "outside target");
            string link = Path.Combine(src, "chara", "linked");
            try
            {
                Fixture(src, oldRoot);
                Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "secret.hs2nchara"), "must not be copied");
                bool junction = Cmd("/c mklink /J \"" + link + "\" \"" + outside + "\"") == 0 && Directory.Exists(link);
                if (!junction) t.Info("UM3 skipped: no junction could be made");
                var before = Hashes(src);

                // UM7 first: nothing may be copied
                string dest = Path.Combine(tmp, "new root", "UserData");
                string destSettings = Path.Combine(tmp, "new root", "Config", "settings.local.json");
                var e7 = t.Throws<MigrationException>(() => UserDataMigration.Run(src, dest, destSettings, null, _ => 10),
                    "UM7 not enough free space is an error");
                t.Check(e7 != null && e7.Message.StartsWith("not enough free space", StringComparison.Ordinal), "UM7 the error says so: " + e7?.Message);
                t.Check(!Directory.Exists(dest) || !Directory.EnumerateFileSystemEntries(dest).Any(), "UM7 nothing was copied and no report written");
                t.Check(!File.Exists(destSettings), "UM7 no settings file written");

                // the offer before the copy
                var roots = new AppRoots(DataRootMode.PerUser, Path.Combine(tmp, "new root"), oldRoot, Path.Combine(tmp, "new root"));
                t.Equal(src, UserDataMigration.Offered(roots, "default", dest), "offer: the per-user mode with a default UserData offers the exe folder's UserData");
                t.Check(UserDataMigration.Offered(roots, "arg", dest) == null, "offer: none when -userData is given");
                var over = new AppRoots(DataRootMode.Override, Path.Combine(tmp, "new root"), oldRoot, Path.Combine(tmp, "per-user"));
                t.Equal(src, UserDataMigration.Offered(over, "default", dest), "offer: a root named by HS2NEXT_DATA_ROOT (override) offers it too");
                var portable = new AppRoots(DataRootMode.Portable, Path.Combine(tmp, "new root"), oldRoot, Path.Combine(tmp, "per-user"));
                t.Check(UserDataMigration.Offered(portable, "default", dest) == null, "offer: none in the portable mode");
                var editor = new AppRoots(DataRootMode.Editor, Path.Combine(tmp, "new root"), oldRoot, Path.Combine(tmp, "per-user"));
                t.Check(UserDataMigration.Offered(editor, "default", dest) == null, "offer: none in the Editor");
                var onExe = new AppRoots(DataRootMode.Override, oldRoot, oldRoot, Path.Combine(tmp, "per-user"));
                t.Check(UserDataMigration.Offered(onExe, "default", src) == null, "offer: none when the root's UserData is the exe folder's own");

                // UM1
                var progress = new List<int>();
                var r1 = UserDataMigration.Run(src, dest, destSettings, (d, n) => progress.Add(d), null, new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc));
                t.Equal(9, r1.Copied, "UM1 files copied");
                t.Equal(0, r1.Same, "UM1 nothing was there before");
                t.Check(r1.Conflicts.Count == 0 && r1.Failed.Count == 0, "UM1 no conflict, no failure: " + string.Join("; ", r1.Failed));
                long expectBytes = new[] { "scene/a.hs2nscene", "chara/b.hs2nchara", "coordinate/set/c.hs2ncoord", "pose/p.hs2npose", "cap/20261030.dat",
                    "config/options.json", "config/recent.json", "Plugins/stp3.sample/settings.json", "content/quarantine.tsv" }
                    .Sum(r => new FileInfo(Path.Combine(src, r.Replace('/', Path.DirectorySeparatorChar))).Length);
                t.Equal(expectBytes, r1.Bytes, "UM1 bytes copied");
                t.Check(progress.Count == 9 && progress.Last() == 9, "UM1 progress reported per file (" + progress.Count + ")");
                var plan = UserDataMigration.Plan(src);
                var kinds = plan.CountByKind();
                t.Check(kinds.TryGetValue("scene", out int ns) && ns == 1 && kinds["chara"] == 1 && kinds["coordinate"] == 1 && kinds["pose"] == 1 && kinds["cap"] == 1 &&
                        kinds["config"] == 2 && kinds["Plugins"] == 1 && kinds["content"] == 1, "UM1 counts per kind: " + string.Join(", ", kinds.Select(k => k.Key + " " + k.Value)));
                t.Check(File.ReadAllText(Path.Combine(dest, "chara", "b.hs2nchara")) == "chara b", "UM1 a copied file has the source's bytes");
                var rep = (JObject)UserDataMigration.Parse(File.ReadAllText(Path.Combine(dest, UserDataMigration.ReportFile)));
                var runs = rep["runs"] as JArray;
                t.Check((int)rep["version"] == 1 && runs != null && runs.Count == 1 && (string)runs[0]["source"] == src && (int)runs[0]["copied"] == 9 &&
                        (string)runs[0]["at"] == "2026-10-30T09:00:00Z" && (long)runs[0]["bytes"] == expectBytes, "UM1 migration.json records the run: " + runs?[0]?.ToString(Newtonsoft.Json.Formatting.None));

                // UM4
                foreach (var rel in new[] { "Bridge/endpoint.json", "Bridge/token", "Bridge/shots/s1.dat", "content/loading.marker", "scene/a.hs2nscene.tmp1234",
                    "app.lock", "notes.txt" })
                    t.Check(!File.Exists(Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar))), "UM4 not copied: " + rel);
                var sk = r1.Skipped;
                t.Check(sk.TryGetValue("bridge", out int b) && b == 2 && sk["shots"] == 2 && sk["crashMarker"] == 1 && sk["temporary"] == 1 && sk["state"] == 1 && sk["other"] == 1,
                    "UM4 the skipped files are counted by kind: " + string.Join(", ", sk.Select(k => k.Key + " " + k.Value)));

                // UM3
                if (junction)
                {
                    t.Check(r1.Refused.Count == 1 && r1.Refused[0] == Path.Combine("chara", "linked"), "UM3 the junction is refused: " + string.Join(", ", r1.Refused));
                    t.Check(!Directory.Exists(Path.Combine(dest, "chara", "linked")) && !Directory.EnumerateFiles(dest, "secret.hs2nchara", SearchOption.AllDirectories).Any(),
                        "UM3 the junction is not entered (its target's file is not copied)");
                }

                // UM5
                t.Equal("copied", r1.Settings, "UM5 the old settings file is copied when the new root has none");
                var s = (JObject)UserDataMigration.Parse(File.ReadAllText(destSettings));
                t.Equal(Path.Combine(oldRoot, "game"), (string)s["hs2Root"], "UM5 a relative hs2Root becomes absolute against the old root");
                t.Equal(Path.Combine(oldRoot, "Cache"), (string)s["cacheDir"], "UM5 a relative cacheDir becomes absolute (the old cache stays in use)");
                t.Check(s["modsFolders"] is JArray m && m.Count == 2 && (string)m[0] == Path.Combine(oldRoot, "mods2") && (string)m[1] == Path.Combine(tmp, "abs mods"),
                    "UM5 modsFolders: relative entries absolute, absolute ones kept: " + s["modsFolders"]?.ToString(Newtonsoft.Json.Formatting.None));
                t.Check(s["userData"] == null, "UM5 a userData naming the migrated folder is dropped");
                t.Check((string)s["_comment"] == "kept" && (int)s["custom"] == 7 && (bool)s["mapAudioLink"] == true, "UM5 comments and other members are kept");
                t.Equal("_comment,hs2Root,modsFolders,cacheDir,custom,mapAudioLink", string.Join(",", s.Properties().Select(p => p.Name)), "UM5 the member order is kept");

                // UM2
                var r2 = UserDataMigration.Run(src, dest, destSettings);
                t.Check(r2.Copied == 0 && r2.Same == 9 && r2.Conflicts.Count == 0, "UM2 a second run copies 0, same = all (" + r2.Copied + " / " + r2.Same + ")");
                t.Equal("kept", r2.Settings, "UM5 a settings file the new root has is kept");
                File.WriteAllText(Path.Combine(dest, "scene", "a.hs2nscene"), "changed at the destination");
                var r3 = UserDataMigration.Run(src, dest, destSettings);
                t.Check(r3.Copied == 0 && r3.Same == 8 && r3.Conflicts.Count == 1 && r3.Conflicts[0] == Path.Combine("scene", "a.hs2nscene"), "UM2 a changed destination file is a conflict");
                t.Equal("changed at the destination", File.ReadAllText(Path.Combine(dest, "scene", "a.hs2nscene")), "UM2 the conflict keeps the destination's bytes");
                File.Delete(Path.Combine(dest, "pose", "p.hs2npose"));
                var r4 = UserDataMigration.Run(src, dest, destSettings);
                t.Check(r4.Copied == 1 && r4.Bytes == new FileInfo(Path.Combine(src, "pose", "p.hs2npose")).Length, "UM2 only the missing file is copied again");
                t.Equal(4, (((JObject)UserDataMigration.Parse(File.ReadAllText(Path.Combine(dest, UserDataMigration.ReportFile))))["runs"] as JArray)?.Count ?? 0, "UM2 every run is recorded");

                // UM6
                var after = Hashes(src);
                t.Check(before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var h) && h == kv.Value),
                    "UM6 the source is byte-identical before and after (" + before.Count + " files)");

                // the offer after the copy
                t.Check(UserDataMigration.Offered(roots, "default", dest) == null && UserDataMigration.Offered(over, "default", dest) == null,
                    "offer: none once migration.json records the source (per-user and override)");
                t.Check(UserDataMigration.HasUserFiles(src), "offer: the fixture holds user files");
                string devOnly = Path.Combine(tmp, "dev player", "UserData");
                Directory.CreateDirectory(Path.Combine(devOnly, "content"));
                Directory.CreateDirectory(Path.Combine(devOnly, "Bridge", "shots"));
                File.WriteAllText(Path.Combine(devOnly, "content", "loading.marker"), "x");
                File.WriteAllText(Path.Combine(devOnly, "Bridge", "shots", "x.dat"), "x");
                t.Check(!UserDataMigration.HasUserFiles(devOnly), "offer: a crash marker and bridge output alone are no user files");
                t.Check(UserDataMigration.Offered(new AppRoots(DataRootMode.PerUser, Path.Combine(tmp, "r2"), Path.Combine(tmp, "dev player"), Path.Combine(tmp, "r2")), "default",
                    Path.Combine(tmp, "r2", "UserData")) == null, "offer: none for a folder without user files");
                // a picked folder holding a UserData subfolder
                t.Equal(src, UserDataMigration.NormaliseSource(oldRoot), "a picked old player folder means its UserData");
                t.Equal(src, UserDataMigration.NormaliseSource(src), "a picked UserData stays");
                t.Throws<MigrationException>(() => UserDataMigration.Run(src, Path.Combine(src, "scene", "inner")), "the destination inside the source is refused");
                t.Throws<MigrationException>(() => UserDataMigration.Run(Path.Combine(tmp, "no such folder"), dest), "a missing source is an error");
            }
            finally
            {
                // the junction itself (never its target's content), before the harness removes the temporary folder
                if (Directory.Exists(link) && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0) Cmd("/c rmdir \"" + link + "\"");
                t.Check(!Directory.Exists(link) && File.Exists(Path.Combine(outside, "secret.hs2nchara")), "UM3 the fixture junction is removed, its target kept");
            }
        }

        static void Fixture(string src, string oldRoot)
        {
            void W(string rel, string text)
            {
                string p = Path.Combine(src, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, text);
            }
            W("scene/a.hs2nscene", "scene a");
            W("chara/b.hs2nchara", "chara b");
            W("coordinate/set/c.hs2ncoord", "coordinate c");
            W("pose/p.hs2npose", "pose p with some more bytes");
            W("cap/20261030.dat", "capture bytes");
            W("config/options.json", "{\"general\":{\"language\":\"ja\"}}");
            W("config/recent.json", "{\"files\":[]}");
            W("Plugins/stp3.sample/settings.json", "{}");
            W("content/quarantine.tsv", "key\twhen\n");
            W("content/loading.marker", "bundle");
            W("Bridge/endpoint.json", "{}");
            W("Bridge/token", "0123456789abcdef0123456789abcdef\n");
            W("Bridge/shots/s1.dat", "shot");
            W("Bridge/shots/s2.dat", "shot");
            W("scene/a.hs2nscene.tmp1234", "half written");
            W("app.lock", "pid 1");
            W("notes.txt", "a file the app never writes");
            string cfg = Path.Combine(oldRoot, "Config");
            Directory.CreateDirectory(cfg);
            var s = new JObject
            {
                ["_comment"] = "kept",
                ["hs2Root"] = "game",
                ["modsFolders"] = new JArray("mods2", Path.Combine(Path.GetDirectoryName(oldRoot), "abs mods")),
                ["cacheDir"] = "Cache",
                ["userData"] = "UserData",
                ["custom"] = 7,
                ["mapAudioLink"] = true,
            };
            File.WriteAllText(Path.Combine(cfg, "settings.local.json"), s.ToString());
        }

        static Dictionary<string, string> Hashes(string root)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            using (var sha = SHA256.Create())
                while (stack.Count > 0)
                    foreach (var e in stack.Pop().GetFileSystemInfos())
                    {
                        if ((e.Attributes & FileAttributes.ReparsePoint) != 0) { d[e.FullName] = "link"; continue; }
                        if (e is DirectoryInfo sub) { d[e.FullName] = "dir"; stack.Push(sub); }
                        else d[e.FullName] = Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(e.FullName))) + "|" + File.GetLastWriteTimeUtc(e.FullName).Ticks;
                    }
            return d;
        }

        static int Cmd(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("cmd.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                return p.WaitForExit(15000) ? p.ExitCode : -1;
            }
            catch (Exception) { return -1; }
        }
    }
}
