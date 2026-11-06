// Plain-Mono tests of the plugin loader's Unity-free core (Tools/mono_tests.ps1 -Suite plugins): PL1 the manifest rules,
// PL2 the load order (dependencies, hints, cycles, ties) and the folder scan's refusals, PL3 the version rules, PL4 the
// precedence of the plugin folder switches, PL4x the parts the host builds on (the owner-scoped command registry, the
// fault wrapper, the event delivery, the settings file). Fixture plugin folders are written to the run's temp folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Save.Tests;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Plugins.Tests
{
    static class Fx
    {
        public static string Manifest(string guid, string extra = null, string api = "1.0", string version = "1.0.0", string assemblies = null) =>
            "{\"manifestVersion\":1,\"guid\":\"" + guid + "\",\"name\":\"n\",\"version\":\"" + version + "\",\"apiVersion\":\"" + api + "\",\"assemblies\":" +
            (assemblies ?? "[\"P.dll\"]") + (extra != null ? "," + extra : "") + "}";

        public static PluginManifest Parse(string json, out string code)
        {
            var m = PluginManifest.Parse(json, out code, out _, out _);
            return m;
        }

        /// <summary>A plugin folder under <paramref name="root"/> with the manifest and empty files for the listed DLLs.</summary>
        public static string Plugin(string root, string folder, string manifestJson, params string[] files)
        {
            string d = Path.Combine(root, folder);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "manifest.json"), manifestJson);
            foreach (var f in files.Length > 0 ? files : new[] { "P.dll" })
            {
                string p = Path.Combine(d, f);
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllBytes(p, new byte[] { 0 });
            }
            return d;
        }

        public static string Root(TestRunner t, string name)
        {
            string r = Path.Combine(t.TempDir, "plugins_" + name);
            if (Directory.Exists(r)) Directory.Delete(r, true);
            Directory.CreateDirectory(r);
            return r;
        }

        public static PluginCandidate Get(PluginScanResult r, string guid) => r.Candidates.FirstOrDefault(c => string.Equals(c.Guid, guid, StringComparison.OrdinalIgnoreCase));
    }

    public static class PL1Manifest
    {
        public static void Run(TestRunner t)
        {
            var m = PluginManifest.Parse("﻿" + Fx.Manifest("stp3.test.a",
                "\"entryType\":\"A.Entry\",\"commandPrefixes\":[\"pa.\",\"pa2.sub.\"],\"dependencies\":[{\"guid\":\"stp3.test.b\",\"minVersion\":\"1.2.0\"},{\"guid\":\"x.y\"}]," +
                "\"loadAfter\":[\"q.r\"],\"loadBefore\":[\"s.t\"],\"minAppVersion\":\"1.0.0\",\"description\":\"d\",\"author\":\"stp3\",\"license\":\"CC0-1.0\",\"url\":\"u\"," +
                "\"futureMember\":{\"x\":1},\"other\":2"), out string code, out string err, out string seen);
            t.Check(m != null, "a full manifest parses (with a BOM): " + code + " " + err);
            if (m != null)
            {
                t.Equal("stp3.test.a", m.Guid, "guid");
                t.Equal("A.Entry", m.EntryType, "entryType");
                t.Equal(2, m.CommandPrefixes.Count, "prefixes");
                t.Equal(2, m.Dependencies.Count, "dependencies");
                t.Equal("1.2.0", m.Dependencies[0].MinVersion, "minVersion");
                t.Equal(null, m.Dependencies[1].MinVersion, "minVersion optional");
                t.Equal("q.r", m.LoadAfter.Single(), "loadAfter");
                t.Equal("s.t", m.LoadBefore.Single(), "loadBefore");
                t.Equal("1.0.0", m.MinAppVersion, "minAppVersion");
                t.Equal("CC0-1.0", m.License, "license");
                t.Check(m.Unknown.Count == 2 && (int)m.Unknown["futureMember"]["x"] == 1 && (int)m.Unknown["other"] == 2, "unknown members kept");
            }
            t.Equal("stp3.test.a", seen, "guid seen");

            // bad manifests
            var bad = new (string json, string what)[]
            {
                ("", "empty text"),
                ("[1]", "not an object"),
                ("{\"a\":1} x", "text after the value"),
                ("{\"guid\":\"a.b\",\"guid\":\"a.c\"}", "duplicate member"),
                (Fx.Manifest("a.b").Replace("\"manifestVersion\":1,", ""), "manifestVersion missing"),
                (Fx.Manifest("a.b").Replace("\"manifestVersion\":1", "\"manifestVersion\":2"), "manifestVersion 2"),
                (Fx.Manifest("a.b").Replace("\"manifestVersion\":1", "\"manifestVersion\":\"1\""), "manifestVersion a string"),
                (Fx.Manifest("nodot"), "guid without a dot"),
                (Fx.Manifest(".a.b"), "guid starting with a dot"),
                (Fx.Manifest("a b.c"), "guid with a space"),
                (Fx.Manifest("a." + new string('x', 127)), "guid longer than 128"),
                (Fx.Manifest("a.b").Replace("\"name\":\"n\"", "\"name\":\"\""), "empty name"),
                (Fx.Manifest("a.b").Replace("\"name\":\"n\"", "\"name\":\"" + new string('n', 65) + "\""), "name of 65"),
                (Fx.Manifest("a.b").Replace("\"name\":\"n\",", ""), "name missing"),
                (Fx.Manifest("a.b", version: "1.0"), "version not semver"),
                (Fx.Manifest("a.b", version: "01.0.0"), "version with a leading zero"),
                (Fx.Manifest("a.b", api: "1.0.0"), "apiVersion with a patch"),
                (Fx.Manifest("a.b", api: "x"), "apiVersion not numbers"),
                (Fx.Manifest("a.b", assemblies: "[]"), "no assemblies"),
                (Fx.Manifest("a.b", assemblies: "\"P.dll\""), "assemblies a string"),
                (Fx.Manifest("a.b", assemblies: "[\"lib/P.dll\"]"), "assembly with a path"),
                (Fx.Manifest("a.b", assemblies: "[\"..\"]"), "assembly .."),
                (Fx.Manifest("a.b", assemblies: "[\"P.dll\",\"p.DLL\"]"), "assembly listed twice"),
                (Fx.Manifest("a.b", assemblies: "[1]"), "assembly a number"),
                (Fx.Manifest("a.b", "\"entryType\":3"), "entryType a number"),
                (Fx.Manifest("a.b", "\"commandPrefixes\":[\"Seq.\"]"), "prefix with a capital"),
                (Fx.Manifest("a.b", "\"commandPrefixes\":[\"seq\"]"), "prefix without the dot"),
                (Fx.Manifest("a.b", "\"commandPrefixes\":[\"seq..\"]"), "prefix with an empty part"),
                (Fx.Manifest("a.b", "\"commandPrefixes\":[\"1a.\"]"), "prefix starting with a digit"),
                (Fx.Manifest("a.b", "\"commandPrefixes\":\"seq.\""), "prefixes a string"),
                (Fx.Manifest("a.b", "\"dependencies\":[\"c.d\"]"), "dependency a string"),
                (Fx.Manifest("a.b", "\"dependencies\":[{\"minVersion\":\"1.0.0\"}]"), "dependency without guid"),
                (Fx.Manifest("a.b", "\"dependencies\":[{\"guid\":\"c.d\",\"minVersion\":\"1\"}]"), "dependency minVersion not semver"),
                (Fx.Manifest("a.b", "\"dependencies\":{}"), "dependencies an object"),
                (Fx.Manifest("a.b", "\"loadAfter\":[1]"), "loadAfter numbers"),
                (Fx.Manifest("a.b", "\"minAppVersion\":\"1.0\""), "minAppVersion not semver"),
                (Fx.Manifest("a.b", "\"description\":[]"), "description an array"),
            };
            foreach (var (json, what) in bad)
            {
                var r = Fx.Parse(json, out string c);
                t.Check(r == null && c == PluginCodes.BadManifest, "bad manifest refused: " + what + " (" + c + ")");
            }
            foreach (var g in new[] { "core", "Bridge", "hs2.x", "HS2.kkex", "hs2next.a", "HS2Next.Thing", "illusion.studio" })
            {
                var r = Fx.Parse(Fx.Manifest(g), out string c);
                bool dotless = g.IndexOf('.') < 0;
                t.Check(r == null && c == (dotless ? PluginCodes.BadManifest : PluginCodes.ReservedGuid), "reserved GUID " + g + " refused (" + c + ")");
            }
            t.Check(PluginManifest.IsReservedGuid("core") && PluginManifest.IsReservedGuid("BRIDGE") && !PluginManifest.IsReservedGuid("hs2x.a"), "IsReservedGuid");
            t.Check(Fx.Parse(Fx.Manifest("hs2x.a"), out _) != null, "hs2x.* is not reserved");
            t.Check(Fx.Parse(Fx.Manifest("a.b", "\"commandPrefixes\":[\"seq.track.\",\"a1.\"]"), out _) != null, "multi-part prefixes");
            t.Check(Fx.Parse(Fx.Manifest("a.b", "\"entryType\":null,\"dependencies\":null,\"commandPrefixes\":null"), out _) != null, "null optional members count as absent");
            t.Check(Fx.Parse(Fx.Manifest("a-b_c.D9"), out _) != null, "guid with - and _");
        }
    }

    public static class PL2Order
    {
        static PluginScanResult ScanOf(TestRunner t, string name, params (string guid, string extra)[] plugins)
        {
            string root = Fx.Root(t, name);
            foreach (var (g, e) in plugins) Fx.Plugin(root, g, Fx.Manifest(g, e));
            return PluginScan.Scan(root, new PluginScanOptions { AppVersion = "1.0.0" });
        }

        static string Order(PluginScanResult r) => string.Join(" ", r.LoadOrder.Select(c => c.Guid));

        public static void Run(TestRunner t)
        {
            var r = ScanOf(t, "ties", ("c.c", null), ("A.a", null), ("b.b", null));
            t.Equal("A.a b.b c.c", Order(r), "ties by GUID ignoring case");

            r = ScanOf(t, "deps", ("a.a", "\"dependencies\":[{\"guid\":\"c.c\"}]"), ("b.b", null), ("c.c", "\"dependencies\":[{\"guid\":\"d.d\",\"minVersion\":\"1.0.0\"}]"), ("d.d", null));
            t.Equal("b.b d.d c.c a.a", Order(r), "dependencies load first");
            t.Check(r.LoadOrder.Select((c, i) => c.Order == i).All(x => x), "Order index");

            r = ScanOf(t, "hints", ("a.a", "\"loadAfter\":[\"c.c\",\"zz.missing\"]"), ("b.b", "\"loadBefore\":[\"a.a\"]"), ("c.c", null));
            t.Equal("b.b c.c a.a", Order(r), "loadAfter / loadBefore (absent GUIDs ignored)");

            r = ScanOf(t, "hintcycle", ("a.a", "\"dependencies\":[{\"guid\":\"b.b\"}]"), ("b.b", "\"loadAfter\":[\"a.a\"]"));
            t.Equal("b.b a.a", Order(r), "a hint against a dependency is dropped");
            t.Check(r.Infos.Any(i => i.StartsWith(PluginCodes.OrderHint + ": b.b loadAfter a.a", StringComparison.Ordinal)), "plugin.orderHint reported: " + string.Join("; ", r.Infos));

            r = ScanOf(t, "hintcycle2", ("a.a", "\"loadAfter\":[\"b.b\"]"), ("b.b", "\"loadAfter\":[\"a.a\"]"));
            t.Equal("b.b a.a", Order(r), "two contradicting hints: the first (by GUID) wins");
            t.Equal(1, r.Infos.Count(i => i.StartsWith(PluginCodes.OrderHint, StringComparison.Ordinal)), "one hint dropped");

            r = ScanOf(t, "cycle", ("a.a", "\"dependencies\":[{\"guid\":\"b.b\"}]"), ("b.b", "\"dependencies\":[{\"guid\":\"c.c\"}]"), ("c.c", "\"dependencies\":[{\"guid\":\"a.a\"}]"),
                ("d.d", "\"dependencies\":[{\"guid\":\"a.a\"}]"), ("e.e", null), ("f.f", "\"dependencies\":[{\"guid\":\"f.f\"}]"));
            t.Equal("e.e", Order(r), "only the plugin outside the cycles loads");
            foreach (var g in new[] { "a.a", "b.b", "c.c", "f.f" }) t.Equal(PluginCodes.DependencyCycle, Fx.Get(r, g)?.Code, g + " refused as a cycle member");
            t.Equal(PluginCodes.Dependency, Fx.Get(r, "d.d")?.Code, "a dependent of a cycle is refused (dependency)");
            t.Check(Fx.Get(r, "a.a")?.Reason?.Contains("a.a -> b.b -> c.c -> a.a") == true, "cycle text: " + Fx.Get(r, "a.a")?.Reason);

            r = ScanOf(t, "missing", ("a.a", "\"dependencies\":[{\"guid\":\"x.x\"}]"), ("b.b", "\"dependencies\":[{\"guid\":\"a.a\"}]"),
                ("c.c", "\"dependencies\":[{\"guid\":\"d.d\",\"minVersion\":\"2.0.0\"}]"), ("d.d", null), ("e.e", "\"dependencies\":[{\"guid\":\"d.d\",\"minVersion\":\"1.0.0-beta\"}]"));
            t.Equal("d.d e.e", Order(r), "missing / too old dependencies refuse, cascading");
            t.Check(Fx.Get(r, "a.a")?.Code == PluginCodes.Dependency && Fx.Get(r, "a.a").Reason.Contains("not installed"), "missing dependency");
            t.Check(Fx.Get(r, "b.b")?.Code == PluginCodes.Dependency && Fx.Get(r, "b.b").Reason.Contains("refused"), "dependency refused: " + Fx.Get(r, "b.b")?.Reason);
            t.Check(Fx.Get(r, "c.c")?.Code == PluginCodes.Dependency && Fx.Get(r, "c.c").Reason.Contains("older than 2.0.0"), "dependency too old");

            // the folder scan's refusals
            string root = Fx.Root(t, "scan");
            Fx.Plugin(root, "ok.one", Fx.Manifest("ok.one", "\"commandPrefixes\":[\"one.\"]"));
            Fx.Plugin(root, "Wrong.Folder", Fx.Manifest("other.guid"));
            Fx.Plugin(root, "dup.a", Fx.Manifest("dup.a"));
            Fx.Plugin(root, "dup.b", Fx.Manifest("dup.a"));
            Fx.Plugin(root, "bad.json", "{ not json");
            Fx.Plugin(root, "old.api", Fx.Manifest("old.api", api: "0.3"));
            Fx.Plugin(root, "new.app", Fx.Manifest("new.app", "\"minAppVersion\":\"9.0.0\""));
            Fx.Plugin(root, "no.dll", Fx.Manifest("no.dll", assemblies: "[\"Missing.dll\"]"));
            Fx.Plugin(root, "clash.api", Fx.Manifest("clash.api", assemblies: "[\"HS2Next.Api.dll\"]"), "HS2Next.Api.dll");
            Fx.Plugin(root, "clash.lib", Fx.Manifest("clash.lib"), "P.dll", "lib/Newtonsoft.Json.dll");
            Fx.Plugin(root, "group.scene", Fx.Manifest("group.scene", "\"commandPrefixes\":[\"scene.x.\"]"));
            Fx.Plugin(root, "group.plugin", Fx.Manifest("group.plugin", "\"commandPrefixes\":[\"plugin.\"]"));
            Fx.Plugin(root, "prefix.a", Fx.Manifest("prefix.a", "\"commandPrefixes\":[\"pp.\"]"));
            Fx.Plugin(root, "prefix.b", Fx.Manifest("prefix.b", "\"commandPrefixes\":[\"pp.sub.\"]"));
            Fx.Plugin(root, "off.one", Fx.Manifest("off.one"));
            Fx.Plugin(root, "needs.off", Fx.Manifest("needs.off", "\"dependencies\":[{\"guid\":\"off.one\"}]"));
            Directory.CreateDirectory(Path.Combine(root, "no.manifest"));
            Fx.Plugin(root, "UPPER.case", Fx.Manifest("upper.CASE").Replace("\"assemblies\"", "\"assemblies\""));
            File.Move(Path.Combine(root, "UPPER.case", "manifest.json"), Path.Combine(root, "UPPER.case", "MANIFEST.JSON"));
            var o = new PluginScanOptions { AppVersion = "1.0.0", HostAssemblies = new[] { "HS2Next.Api", "newtonsoft.json" }, ReservedGroups = new[] { "scene", "chara" }, Disabled = new[] { "OFF.one" } };
            r = PluginScan.Scan(root, o);
            t.Equal("ok.one prefix.a upper.CASE", Order(r), "the scan loads the valid plugins in order");
            var want = new (string guid, string code)[]
            {
                ("other.guid", PluginCodes.FolderMismatch), ("dup.a", PluginCodes.DuplicateGuid), ("bad.json", PluginCodes.BadManifest),
                ("old.api", PluginCodes.ApiVersion), ("new.app", PluginCodes.AppVersion), ("no.dll", PluginCodes.MissingAssembly),
                ("clash.api", PluginCodes.AssemblyCollision), ("clash.lib", PluginCodes.AssemblyCollision), ("group.scene", PluginCodes.PrefixTaken),
                ("group.plugin", PluginCodes.PrefixTaken), ("prefix.b", PluginCodes.PrefixTaken), ("off.one", PluginCodes.Disabled), ("needs.off", PluginCodes.Dependency),
            };
            foreach (var (g, code) in want) t.Equal(code, Fx.Get(r, g)?.Code, g + " -> " + code);
            t.Equal(2, r.Candidates.Count(c => c.Code == PluginCodes.DuplicateGuid), "both duplicates refused");
            t.Equal(PluginStates.Disabled, Fx.Get(r, "off.one")?.State, "disabled is its own state");
            t.Check(r.Infos.Contains(PluginCodes.NoManifest + ": no.manifest"), "plugin.noManifest reported");
            t.Equal(r.LoadOrder.Count + 14, r.Candidates.Count, "every manifest folder is a candidate (" + r.Candidates.Count + ")");
            t.Check(r.Candidates.Take(r.LoadOrder.Count).SequenceEqual(r.LoadOrder), "candidates list the load order first");
            t.Check(Fx.Get(r, "prefix.b")?.Reason.Contains("prefix.a") == true, "prefix claimed by the first in GUID order: " + Fx.Get(r, "prefix.b")?.Reason);
            t.Equal(0, PluginScan.Scan(Path.Combine(root, "absent"), o).Candidates.Count, "an absent folder: no plugins");
            t.Equal(0, PluginScan.Scan(null, o).Candidates.Count, "no folder: no plugins");
        }
    }

    public static class PL3Versions
    {
        public static void Run(TestRunner t)
        {
            var order = new[] { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
            for (int i = 0; i + 1 < order.Length; i++)
            {
                SemVer.TryParse(order[i], out var a);
                SemVer.TryParse(order[i + 1], out var b);
                t.Check(a != null && b != null && a.CompareTo(b) < 0 && b.CompareTo(a) > 0, "semver " + order[i] + " < " + order[i + 1]);
            }
            t.Check(SemVer.TryParse("1.2.3+build.5", out var bv) && bv.ToString() == "1.2.3", "build metadata ignored");
            SemVer.TryParse("1.2.3", out var x);
            SemVer.TryParse("1.2.3+other", out var y);
            t.Equal(0, x.CompareTo(y), "build metadata does not order");
            foreach (var s in new[] { "1.2", "1.2.3.4", "v1.2.3", "1.02.3", "1.2.3-", "1.2.3-a..b", "", " 1.2.3" })
                t.Check(!SemVer.TryParse(s, out _), "not semver: \"" + s + "\"");
            t.Check(SemVer.ApiCompatible("0.3", 0, 3), "0.3 on 0.3");
            t.Check(!SemVer.ApiCompatible("0.2", 0, 3), "0.2 not on 0.3 (0.x exact)");
            t.Check(!SemVer.ApiCompatible("0.4", 0, 3), "0.4 not on 0.3");
            t.Check(SemVer.ApiCompatible("1.0", 1, 2) && SemVer.ApiCompatible("1.2", 1, 2), "1.0 and 1.2 on 1.2");
            t.Check(!SemVer.ApiCompatible("1.3", 1, 2), "1.3 not on 1.2");
            t.Check(!SemVer.ApiCompatible("2.0", 1, 9) && !SemVer.ApiCompatible("1.0", 2, 0), "other majors");
            t.Check(!SemVer.ApiCompatible("1", 1, 0) && !SemVer.ApiCompatible("1.0.0", 1, 0) && !SemVer.ApiCompatible(null, 0, 3), "malformed apiVersion");
            t.Equal("1.2", HS2Next.ApiVersion.Major + "." + HS2Next.ApiVersion.Minor, "the host Api is 1.2");
            t.Equal("1.2.0", HS2Next.ApiVersion.Semver, "ApiVersion.Semver");

            string root = Fx.Root(t, "app");
            Fx.Plugin(root, "a.pre", Fx.Manifest("a.pre", "\"minAppVersion\":\"1.0.0-beta\""));
            Fx.Plugin(root, "a.same", Fx.Manifest("a.same", "\"minAppVersion\":\"1.0.0\""));
            Fx.Plugin(root, "a.next", Fx.Manifest("a.next", "\"minAppVersion\":\"1.0.1\""));
            var r = PluginScan.Scan(root, new PluginScanOptions { AppVersion = "1.0.0" });
            t.Equal("a.pre a.same", string.Join(" ", r.LoadOrder.Select(c => c.Guid)), "minAppVersion against app 1.0.0");
            t.Equal(PluginCodes.AppVersion, Fx.Get(r, "a.next")?.Code, "a newer minAppVersion refuses");
            r = PluginScan.Scan(root, new PluginScanOptions { AppVersion = null });
            t.Equal(3, r.LoadOrder.Count, "no app version: the check is skipped");
            Fx.Plugin(root, "a.old", Fx.Manifest("a.old", api: "0.3"));
            Fx.Plugin(root, "a.newer", Fx.Manifest("a.newer", api: "1.3"));
            r = PluginScan.Scan(root, new PluginScanOptions { AppVersion = "1.0.0" });
            t.Equal(PluginCodes.ApiVersion, Fx.Get(r, "a.old")?.Code, "a 0.3 plugin on the 1.2 host is refused");
            t.Equal(PluginCodes.ApiVersion, Fx.Get(r, "a.newer")?.Code, "a 1.3 plugin on the 1.2 host is refused");
            r = PluginScan.Scan(root, new PluginScanOptions { AppVersion = "1.0.0", HostApiMajor = 1, HostApiMinor = 3 });
            t.Equal(PluginStates.Pending, Fx.Get(r, "a.pre")?.State, "a 1.0 plugin on a 1.3 host loads");
            t.Equal(PluginStates.Pending, Fx.Get(r, "a.newer")?.State, "a 1.3 plugin on a 1.3 host loads");
            t.Equal(PluginCodes.ApiVersion, Fx.Get(r, "a.old")?.Code, "a 0.3 plugin on a 1.3 host is refused");
        }
    }

    public static class PL4Settings
    {
        public static void Run(TestRunner t)
        {
            string app = Path.Combine(t.TempDir, "app");
            string def = Path.GetFullPath(Path.Combine(app, "Plugins"));
            var l = PluginLaunch.Resolve(null, false, null, true, "", null, app);
            t.Check(l.Enabled && l.Source == "settings" && l.Root == def, "defaults: on, settings, <app>/Plugins: " + l);
            l = PluginLaunch.Resolve(null, false, null, false, "", null, app);
            t.Check(!l.Enabled && l.Source == "settings", "plugins.enabled false: off");
            l = PluginLaunch.Resolve(null, false, null, true, "custom", new[] { "a.b" }, app);
            t.Check(l.Enabled && l.Root == Path.GetFullPath(Path.Combine(app, "custom")) && l.Disabled.SequenceEqual(new[] { "a.b" }), "plugins.dir relative to the app folder, disabled list");
            string abs = Path.GetFullPath(Path.Combine(t.TempDir, "abs"));
            l = PluginLaunch.Resolve(null, false, null, true, abs, null, app);
            t.Equal(abs, l.Root, "plugins.dir absolute");
            foreach (var env in new[] { "1", "true", "TRUE", " 1 " })
            {
                l = PluginLaunch.Resolve(null, false, env, true, "", null, app);
                t.Check(!l.Enabled && l.Source == "env" && l.Root == def, "HS2NEXT_NO_PLUGINS=" + env + ": off (env), root still reported");
            }
            foreach (var env in new[] { "", "0", "false", "no" })
            {
                l = PluginLaunch.Resolve(null, false, env, true, "", null, app);
                t.Check(l.Enabled && l.Source == "settings", "HS2NEXT_NO_PLUGINS=\"" + env + "\" is not on");
            }
            l = PluginLaunch.Resolve(null, true, null, true, "", null, app);
            t.Check(!l.Enabled && l.Source == "-noPlugins", "-noPlugins: off");
            l = PluginLaunch.Resolve(null, true, "1", true, "", null, app);
            t.Equal("-noPlugins", l.Source, "-noPlugins wins over the environment");
            l = PluginLaunch.Resolve(abs, true, "1", false, "custom", new[] { "x.y" }, app);
            t.Check(l.Enabled && l.Source == "-pluginsDir" && l.Root == abs && l.Disabled.Contains("x.y"), "-pluginsDir wins over everything (the disabled list still applies)");
            l = PluginLaunch.Resolve("rel", false, null, true, "", null, app);
            t.Equal(Path.GetFullPath(Path.Combine(app, "rel")), l.Root, "-pluginsDir relative to the app folder");
        }
    }

    /// <summary>The host-side parts that need no engine: the scoped registry, the fault wrapper, the events, the settings file.</summary>
    public static class PL4xRuntimeParts
    {
        sealed class FakeRegistry : ICommandRegistry
        {
            public readonly Dictionary<string, (CommandSpec spec, CommandHandler sync, AsyncCommandHandler async)> All =
                new Dictionary<string, (CommandSpec, CommandHandler, AsyncCommandHandler)>(StringComparer.Ordinal);
            public void Register(CommandSpec spec, CommandHandler handler) { Add(spec); All[spec.Name] = (spec, handler, null); }
            public void RegisterAsync(CommandSpec spec, AsyncCommandHandler handler) { Add(spec); All[spec.Name] = (spec, null, handler); }
            void Add(CommandSpec s)
            {
                if (All.ContainsKey(s.Name)) throw new ArgumentException("taken " + s.Name);
                s.Freeze();
            }
            public bool Unregister(string name) => All.Remove(name);
            public int UnregisterOwner(string owner)
            {
                var l = All.Values.Where(v => v.spec.Owner == owner).Select(v => v.spec.Name).ToList();
                foreach (var n in l) All.Remove(n);
                return l.Count;
            }
            public bool Contains(string name) => All.ContainsKey(name);
            public CommandSpec Find(string name) => All.TryGetValue(name, out var v) ? v.spec : null;
            public IReadOnlyList<CommandSpec> Specs => All.Values.Select(v => v.spec).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
        }

        public static void Run(TestRunner t)
        {
            // the scoped registry
            var inner = new FakeRegistry();
            inner.Register(new CommandSpec("scene.list"), _ => null);
            var logs = new List<string>();
            var guard = new PluginGuard("stp3.test.a", logs.Add);
            bool faulted = false;
            var reg = new ScopedCommandRegistry(inner, "stp3.test.a", "1.2.3", new[] { "pa.", "pb.sub." }, guard, () => faulted, g => g == "scene" || g == "chara");
            reg.Register(new CommandSpec("pa.one", "one"), _ => "1");
            var s1 = inner.Find("pa.one");
            t.Check(s1 != null && s1.Owner == "stp3.test.a" && s1.Group == "stp3.test.a" && s1.Since == "1.2.3", "owner, group and since: " + s1?.Owner + " " + s1?.Group + " " + s1?.Since);
            var s2 = new CommandSpec("pb.sub.two") { Group = "mine", Since = "0.9.0" }.WithOwner("core");
            reg.Register(s2, _ => null);
            t.Check(s2.Owner == "stp3.test.a" && s2.Group == "mine" && s2.Since == "0.9.0", "the owner is forced; an own group and since are kept");
            var s3 = new CommandSpec("pa.three") { Group = "chara" };
            reg.Register(s3, _ => null);
            t.Equal("stp3.test.a", s3.Group, "a host group is replaced by the GUID");
            t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("pc.x"), _ => null), "a name outside the prefixes is refused");
            t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("pb.other"), _ => null), "a partial prefix match is refused");
            t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("pa.four").Alias("scene.four"), _ => null), "an alias outside the prefixes is refused");
            t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("pa.one"), _ => null), "a collision is refused (the inner registry's rule)");
            var none = new ScopedCommandRegistry(inner, "stp3.test.b", "1.0.0", new string[0], guard, () => false);
            t.Throws<ArgumentException>(() => none.Register(new CommandSpec("pa.five"), _ => null), "a plugin without prefixes registers no command");
            t.Equal("pa.one pb.sub.two pa.three", string.Join(" ", reg.Names), "the registered names");
            t.Check(!reg.Unregister("scene.list") && inner.Contains("scene.list"), "unregistering another owner's command does nothing");
            t.Equal(0, reg.UnregisterOwner("core"), "UnregisterOwner of another owner: 0");
            // handler wrapping
            var h = inner.All["pa.one"].sync;
            t.Equal("1", h(null) as string, "the wrapped handler runs");
            reg.Register(new CommandSpec("pa.boom"), _ => throw new InvalidOperationException("boom"));
            reg.Register(new CommandSpec("pa.bridgeError"), _ => throw new BridgeException(BridgeCodes.NotFound, "no"));
            t.Throws<InvalidOperationException>(() => inner.All["pa.boom"].sync(null), "a handler exception passes through (the dispatcher answers plugin_fault)");
            t.Throws<BridgeException>(() => inner.All["pa.bridgeError"].sync(null), "a BridgeException passes through");
            t.Equal(1, guard.Total, "only the non-bridge exception counts as a fault");
            t.Equal(1, guard.ByHook()["command pa.boom"], "counted under the command's hook");
            reg.RegisterAsync(new CommandSpec("pa.async"), _ => Task.FromException<object>(new ArgumentException("async boom")));
            var at = inner.All["pa.async"].async(null);
            t.Check(at.IsFaulted && guard.Total == 2, "a faulted task counts (" + guard.Total + ")");
            faulted = true;
            var fe = t.Throws<BridgeException>(() => inner.All["pa.one"].sync(null), "a faulted plugin's command answers without running");
            t.Check(fe != null && fe.Code == BridgeCodes.PluginFault && (string)fe.Error.Detail.Extra["owner"] == "stp3.test.a" && (string)fe.Error.Detail.Extra["state"] == "faulted",
                "plugin_fault with detail {owner, state: faulted}");
            t.Throws<InvalidOperationException>(() => reg.Register(new CommandSpec("pa.late"), _ => null), "a faulted plugin registers nothing");
            faulted = false;
            t.Check(reg.Unregister("pa.three") && !inner.Contains("pa.three"), "Unregister of an own name");
            int n = reg.UnregisterAll();
            t.Check(n == 5 && inner.Contains("scene.list") && reg.Names.Count == 0, "UnregisterAll removes the plugin's commands only (" + n + ")");

            // the fault wrapper
            logs.Clear();
            var g2 = new PluginGuard("stp3.test.g", logs.Add);
            int raised = 0;
            g2.BecameFaulted += () => raised++;
            for (int i = 0; i < 9; i++) g2.Run("update", () => throw new InvalidOperationException("x" + i));
            t.Check(!g2.Faulted && raised == 0 && g2.Total == 9, "9 faults: not faulted");
            t.Equal(1, logs.Count, "one log line per (hook, exception type)");
            t.Equal(5, g2.Run("event X", () => 5, -1), "a value through the wrapper");
            t.Equal(-1, g2.Run<int>("event X", () => throw new ArgumentException("y"), -1), "the fallback on a throw");
            t.Check(g2.Faulted && raised == 1 && g2.Total == 10, "the 10th fault makes the plugin faulted, raised once");
            g2.Run("update", () => throw new InvalidOperationException("z"));
            t.Check(raised == 1 && g2.Total == 11, "later faults do not raise again");
            t.Check(g2.LastError == "update: InvalidOperationException: z", "the last error: " + g2.LastError);
            t.Check(logs.Any(l => l.Contains("faulted after 10 faults")), "the faulted line is logged");
            t.Equal(9 + 1 + 1, g2.ByHook()["update"] + g2.ByHook()["event X"], "faults per hook");

            // the event delivery
            var g3 = new PluginGuard("stp3.test.e", null);
            var ev = new PluginEvents(g3);
            var seen = new List<string>();
            ev.SceneCleared += () => { seen.Add("cleared"); ev.Raise(PluginEventKind.SceneLoaded, new SceneLoadInfo(SceneLoadKind.New, null, 0, 0)); seen.Add("cleared end"); };
            ev.SceneLoaded += i => seen.Add("loaded " + i.Kind);
            ev.SceneLoaded += i => throw new InvalidOperationException("handler 2");
            ev.SceneLoaded += i => seen.Add("loaded 3");
            ev.NodesRemoved += i => seen.Add("removed " + i.Nodes.Count + " " + i.Origin);
            ev.SceneStepped += s => seen.Add("stepped " + s.Count + " " + s.Dt);
            ev.PausedChanged += p => seen.Add("paused " + p);
            ev.NodeForgotten += id => seen.Add("forgot " + id);
            ev.Raise(PluginEventKind.SceneCleared);
            t.Equal("cleared|cleared end|loaded New|loaded 3", string.Join("|", seen), "no re-entrance: the nested event runs after the handler; a throwing handler does not stop the next");
            t.Equal(1, g3.Total, "the throwing handler counted");
            t.Equal(1, ev.Deferred, "one event was queued");
            seen.Clear();
            ev.Raise(PluginEventKind.NodesRemoved, new NodeChangeInfo(new[] { new NodeRef("n1", "item", null), new NodeRef("n2", "item", "n1") }, ChangeOrigin.Undo));
            ev.Raise(PluginEventKind.SceneStepped, new StepInfo(30, 0.5f));
            ev.Raise(PluginEventKind.PausedChanged, true);
            ev.Raise(PluginEventKind.NodeForgotten, "n9");
            ev.Raise(PluginEventKind.AppReady);
            t.Equal("removed 2 Undo|stepped 30 0.5|paused True|forgot n9", string.Join("|", seen), "typed arguments");
            t.Equal(8, ev.HandlerCount, "handler count");
            ev.Clear();
            seen.Clear();
            ev.Raise(PluginEventKind.SceneLoaded, new SceneLoadInfo(SceneLoadKind.Load, "a.hs2nscene", 1, 2));
            t.Check(seen.Count == 0 && ev.HandlerCount == 0 && ev.IsClosed, "after Clear nothing is delivered");

            // the settings file
            string data = Path.Combine(t.TempDir, "userdata", "Plugins", "stp3.test.s");
            bool valid = true;
            var sf = new PluginSettingsFile(data, () => valid);
            t.Equal(Path.Combine(data, "settings.json"), sf.Path, "settings path");
            t.Check(sf.Read() == null && !Directory.Exists(data), "absent: null, nothing created");
            sf.Write("{\"a\": 1}");
            t.Equal("{\"a\": 1}", sf.Read(), "written and read back");
            sf.Write("[2]");
            t.Equal("[2]", sf.Read(), "replaced");
            t.Throws<ArgumentException>(() => sf.Write("{\"a\":"), "not JSON is refused");
            t.Throws<ArgumentException>(() => sf.Write("1 2"), "two values are refused");
            t.Equal("[2]", sf.Read(), "a refused write leaves the file");
            t.Equal(1, Directory.GetFiles(data).Length, "no temporary file left");
            valid = false;
            t.Throws<ObjectDisposedException>(() => sf.Read(), "after unload the settings throw");

            // the update timer
            var tm = new PluginTimer(4);
            foreach (var ms in new[] { 5.0, 1.0, 3.0, 2.0, 4.0 }) tm.Add(ms);
            t.Check(tm.Count == 4 && Math.Abs(tm.Median - 2.5) < 1e-6 && Math.Abs(tm.Max - 4) < 1e-6, "median / max over the last frames: " + tm.Median + " " + tm.Max);
        }
    }
}
