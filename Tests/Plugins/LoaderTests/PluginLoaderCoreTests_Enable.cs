// KD0: a candidate on the disabled list skips the scan's per-candidate checks
// after the folder name; enabling it at run time runs them then (PluginScan.Revalidate): the Api and app versions, the
// listed assemblies present, no assembly name the host has, no command prefix of a host group, and no prefix that
// overlaps one a loaded plugin holds (its own GUID ignored). The codes and texts equal the scan's; the candidate is not
// changed. Fixture plugin folders are written to the run's temp folder.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save.Tests;

namespace HS2Next.App.Plugins.Tests
{
    public static class KD0Revalidate
    {
        public static void Run(TestRunner t)
        {
            string root = Fx.Root(t, "kd0");
            Fx.Plugin(root, "d.ok", Fx.Manifest("d.ok", "\"commandPrefixes\":[\"dok.\"]"));
            Fx.Plugin(root, "d.api", Fx.Manifest("d.api", api: "2.0"));
            Fx.Plugin(root, "d.api0", Fx.Manifest("d.api0", api: "0.3"));
            Fx.Plugin(root, "d.app", Fx.Manifest("d.app", "\"minAppVersion\":\"9.0.0\""));
            Fx.Plugin(root, "d.missing", Fx.Manifest("d.missing", assemblies: "[\"P.dll\",\"Missing.dll\"]"), "P.dll");
            Fx.Plugin(root, "d.collide", Fx.Manifest("d.collide", assemblies: "[\"HostLib.dll\"]"), "HostLib.dll");
            Fx.Plugin(root, "d.libcollide", Fx.Manifest("d.libcollide"), "P.dll", "lib/HostLib.dll");
            Fx.Plugin(root, "d.group", Fx.Manifest("d.group", "\"commandPrefixes\":[\"scene.\"]"));
            Fx.Plugin(root, "d.taken", Fx.Manifest("d.taken", "\"commandPrefixes\":[\"pa.\"]"));
            Fx.Plugin(root, "d.overlap", Fx.Manifest("d.overlap", "\"commandPrefixes\":[\"pa.sub.\"]"));
            Fx.Plugin(root, "d.wider", Fx.Manifest("d.wider", "\"commandPrefixes\":[\"q.\"]"));
            Fx.Plugin(root, "d.self", Fx.Manifest("d.self", "\"commandPrefixes\":[\"own.\"]"));
            var all = new[] { "d.ok", "d.api", "d.api0", "d.app", "d.missing", "d.collide", "d.libcollide", "d.group", "d.taken", "d.overlap", "d.wider", "d.self" };
            PluginScanOptions Options(IEnumerable<string> disabled) => new PluginScanOptions
            {
                AppVersion = "1.2.0", HostApiMajor = 1, HostApiMinor = 1,
                HostAssemblies = new[] { "HostLib", "HS2Next.Api" },
                ReservedGroups = new[] { "scene", "chara" },
                Disabled = disabled,
            };
            var scan = PluginScan.Scan(root, Options(all));
            t.Equal(all.Length, scan.Candidates.Count(c => c.State == PluginStates.Disabled), "every listed candidate is disabled (the scan skipped its checks)");
            t.Equal(0, scan.LoadOrder.Count, "nothing loads");

            // the loaded plugins' prefixes at the time of the enable: x.loaded holds pa. and q.sub., d.self holds own. (its own)
            var loaded = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("pa.", "x.loaded"),
                new KeyValuePair<string, string>("q.sub.", "x.loaded"),
                new KeyValuePair<string, string>("own.", "d.self"),
            };
            var expect = new Dictionary<string, string>
            {
                ["d.ok"] = null,
                ["d.api"] = PluginCodes.ApiVersion,
                ["d.api0"] = PluginCodes.ApiVersion,
                ["d.app"] = PluginCodes.AppVersion,
                ["d.missing"] = PluginCodes.MissingAssembly,
                ["d.collide"] = PluginCodes.AssemblyCollision,
                ["d.libcollide"] = PluginCodes.AssemblyCollision,
                ["d.group"] = PluginCodes.PrefixTaken,
                ["d.taken"] = PluginCodes.PrefixTaken,
                ["d.overlap"] = PluginCodes.PrefixTaken,
                ["d.wider"] = PluginCodes.PrefixTaken,
                ["d.self"] = null,
            };
            var reasons = new Dictionary<string, string>();
            foreach (var kv in expect)
            {
                var c = Fx.Get(scan, kv.Key);
                string code = PluginScan.Revalidate(c, Options(all), loaded, out string reason);
                reasons[kv.Key] = reason;
                t.Equal(kv.Value, code, "revalidate " + kv.Key + ": " + (code ?? "may load") + (reason != null ? " (" + reason + ")" : ""));
                t.Check(code == null ? reason == null : !string.IsNullOrEmpty(reason), "revalidate " + kv.Key + ": a reason with every refusal");
                t.Equal(PluginStates.Disabled, c?.State, "revalidate " + kv.Key + ": the candidate stays as the scan left it");
            }
            t.Check(reasons["d.taken"]?.Contains("claimed by x.loaded") == true, "the prefix refusal names the plugin that holds it");
            t.Check(reasons["d.group"]?.Contains("is a group of the app") == true, "the group refusal says so");
            t.Check(reasons["d.overlap"]?.Contains("pa.sub.") == true, "a longer prefix inside a held one is refused");
            t.Check(reasons["d.wider"]?.Contains("q.") == true, "a shorter prefix around a held one is refused");

            // without loaded prefixes the prefix refusals of other plugins disappear; the per-candidate ones stay
            t.Equal(null, PluginScan.Revalidate(Fx.Get(scan, "d.taken"), Options(all), null, out _), "no loaded prefixes: d.taken may load");
            t.Equal(PluginCodes.PrefixTaken, PluginScan.Revalidate(Fx.Get(scan, "d.group"), Options(all), null, out _), "no loaded prefixes: a host group still refuses");

            // the same per-candidate checks with the same texts as the scan when nothing is disabled
            var open = PluginScan.Scan(root, Options(Array.Empty<string>()));
            foreach (var g in new[] { "d.api", "d.api0", "d.app", "d.missing", "d.collide", "d.libcollide", "d.group" })
            {
                var c = Fx.Get(open, g);
                t.Equal(expect[g], c?.Code, "scan without the disabled list " + g + ": the same code");
                t.Equal(reasons[g], c?.Reason, "scan without the disabled list " + g + ": the same text");
            }
            t.Equal(PluginStates.Pending, Fx.Get(open, "d.ok")?.State, "scan without the disabled list: d.ok loads");

            // a candidate whose manifest could not be used answers its own refusal
            Fx.Plugin(root, "d.bad", "{\"manifestVersion\":1}");
            var bad = Fx.Get(PluginScan.Scan(root, Options(all)), "d.bad");
            string badCode = PluginScan.Revalidate(bad, Options(all), loaded, out string badReason);
            t.Equal(PluginCodes.BadManifest, badCode, "a bad manifest: its refusal code");
            t.Check(!string.IsNullOrEmpty(badReason), "a bad manifest: its reason");
        }
    }
}
