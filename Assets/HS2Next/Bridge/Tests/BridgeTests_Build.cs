// The build facts of a player (build.json): the kinds, the parser, the defaults of a process without the file, the bridge
// token's default by kind, and bridge.version.build.
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    public static class BuildGroup
    {
        public static void Run(BridgeChecks t)
        {
            // the defaults of a process without build.json
            var ed = BuildFacts.Default(true, "0.3.0");
            var dev = BuildFacts.Default(false, "0.3.0");
            t.Check(ed.Kind == BuildFacts.KindEditor && ed.Version == "0.3.0" && ed.Commit == null && ed.Error == null, "the Editor: kind editor");
            t.Check(dev.Kind == BuildFacts.KindDev && dev.Commit == null && dev.Dirty == null, "a player without build.json: kind dev");

            // the token default by kind (the settings member and -bridgeToken override it in the app's settings)
            t.Check(!ed.TokenRequiredByDefault, "editor: no token by default");
            t.Check(!dev.TokenRequiredByDefault, "dev: no token by default");
            var dist = BuildFacts.Parse("{\"kind\":\"distribution\",\"version\":\"0.3.0\",\"commit\":\"abc1234\",\"dirty\":false,\"builtUtc\":\"2026-10-30T08:00:00Z\"}", false, "x");
            t.Check(dist.TokenRequiredByDefault && dist.IsDistribution, "distribution: token required by default");

            // the parser
            t.Check(dist.Error == null && dist.Version == "0.3.0" && dist.Commit == "abc1234" && dist.Dirty == false && dist.BuiltUtc == "2026-10-30T08:00:00Z",
                "a distribution file: every member read");
            var d2 = BuildFacts.Parse("{\"kind\":\"dev\",\"dirty\":true}", false, "0.4.0");
            t.Check(d2.Error == null && d2.Kind == "dev" && d2.Version == "0.4.0" && d2.Commit == null && d2.Dirty == true, "absent members stay unknown; the version falls back");
            var inEditor = BuildFacts.Parse("{\"kind\":\"distribution\"}", true, "0.3.0");
            t.Check(inEditor.Kind == BuildFacts.KindEditor && !inEditor.TokenRequiredByDefault, "the Editor ignores a build file");
            foreach (var bad in new[] { "", "not json", "[1,2]", "{\"kind\":\"release\"}", "{\"version\":\"1\"}", "{\"kind\":\"dev\",\"dirty\":\"no\"}",
                                         "{\"kind\":\"distribution\",\"commit\":7}", "{\"kind\":null}" })
            {
                var f = BuildFacts.Parse(bad, false, "0.3.0");
                t.Check(f.Error != null && f.Kind == BuildFacts.KindDev && !f.TokenRequiredByDefault, "a bad file keeps the dev default with an error: " + (bad.Length == 0 ? "(empty)" : bad));
            }
            var date = BuildFacts.Parse("{\"kind\":\"dev\",\"builtUtc\":\"2026-10-30T08:00:00Z\"}", false, "0.3.0");
            t.Equal("2026-10-30T08:00:00Z", date.BuiltUtc, "builtUtc stays the text written (no date conversion)");

            // the file text round-trips, kind first
            string text = dist.ToFileText();
            t.Check(text.TrimStart('{', ' ', '\r', '\n').StartsWith("\"kind\": \"distribution\"") && text.EndsWith("\n"), "build.json text: kind first, one trailing newline");
            var back = BuildFacts.Parse(text, false, "x");
            t.Check(back.Error == null && back.Kind == dist.Kind && back.Version == dist.Version && back.Commit == dist.Commit && back.Dirty == dist.Dirty &&
                    back.BuiltUtc == dist.BuiltUtc, "build.json text round-trips");
            var unknown = BuildFacts.Default(false, "0.3.0").ToJson();
            t.Check(unknown["commit"].Type == JTokenType.Null && unknown["dirty"].Type == JTokenType.Null && unknown["builtUtc"].Type == JTokenType.Null,
                "unknown members are null in the reply");
            t.Equal("2026-10-30T08:00:00Z", BuildFacts.UtcStamp(new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc)), "builtUtc stamp format");
            t.Check(dist.Summary.StartsWith("distribution 0.3.0 (abc1234") && d2.Summary.Contains("dirty") && BuildFacts.Parse("x", false, "1").Summary.Contains("not used"),
                "the log summary names kind, version, commit, dirty and a file error");

            // Read: StreamingAssets folder with and without the file
            string dir = Path.Combine(t.TempDir, "streaming_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var none = BuildFacts.Read(dir, false, "0.3.0");
            t.Check(none.Kind == BuildFacts.KindDev && none.SourceFile == null && none.Error == null, "Read: no file -> dev, no error");
            File.WriteAllText(Path.Combine(dir, BuildFacts.FileName), dist.ToFileText(), new UTF8Encoding(false));
            var read = BuildFacts.Read(dir, false, "0.3.0");
            t.Check(read.Kind == BuildFacts.KindDistribution && read.SourceFile == Path.Combine(dir, BuildFacts.FileName), "Read: the file -> its kind and path");
            t.Check(BuildFacts.Read(dir, true, "0.3.0").Kind == BuildFacts.KindEditor, "Read in the Editor: always editor");
            File.WriteAllText(Path.Combine(dir, BuildFacts.FileName), "﻿" + dist.ToFileText(), new UTF8Encoding(false));
            t.Check(BuildFacts.Read(dir, false, "0.3.0").Kind == BuildFacts.KindDistribution, "Read: a byte-order mark is accepted");

            // bridge.version.build
            using (var s = Kit.NewServer(o => o.Build = dist))
            {
                var v = Kit.Exec(s, "{\"cmd\":\"bridge.version\"}");
                t.Check(Kit.Ok(v) && Kit.Str(v, "build.kind") == "distribution" && Kit.Str(v, "build.commit") == "abc1234" && Kit.Str(v, "build.version") == "0.3.0",
                    "bridge.version.build: kind, version, commit");
            }
            using (var s = Kit.NewServer())
            {
                var v = Kit.Exec(s, "{\"cmd\":\"bridge.version\"}");
                t.Check(Kit.Ok(v) && v["build"] != null && v["build"].Type == JTokenType.Null, "bridge.version.build is null when the host gives no facts");
            }
        }
    }
}
