// UC1-UC6 (after 1.0.0), plain Mono: Tools/mono_tests.ps1 -Suite ui (fixtures =
// Assets/HS2Next/Resources/UI).
//   UpdateCheckTests  the version parse and order, the release reply, the dialog decision at start and by hand, the
//                     download page, the state file, and the update check's strings in en / ja / zh-Hans
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;

    public static class UpdateCheckTests
    {
        public static void Run(TestRunner t)
        {
            Versions(t);
            Replies(t);
            Decisions(t);
            Pages(t);
            State(t);
            Strings(t);
        }

        static int Cmp(string a, string b)
        {
            ReleaseVersion.TryParse(a, out var x);
            ReleaseVersion.TryParse(b, out var y);
            return x == null || y == null ? int.MinValue : Math.Sign(x.CompareTo(y));
        }

        // UC1
        static void Versions(TestRunner t)
        {
            t.Check(Cmp("v1.0.1", "1.0.0") == 1, "UC1 v1.0.1 > 1.0.0");
            t.Check(Cmp("1.1", "1.1.0") == 0, "UC1 1.1 = 1.1.0");
            t.Check(Cmp("2", "1.9.9") == 1, "UC1 2 > 1.9.9");
            t.Check(Cmp("1.10.0", "1.9.0") == 1, "UC1 parts compare as numbers (1.10.0 > 1.9.0)");
            t.Check(Cmp("1.0.0-rc1", "1.0.0") == -1, "UC1 a pre-release sorts before its release");
            t.Check(Cmp("1.0.0-rc2", "1.0.0-rc1") == 1, "UC1 suffixes compare ordinally");
            t.Check(Cmp("V1.0.0+build.7", "1.0.0") == 0, "UC1 +build is dropped, V counts as v");
            foreach (var bad in new[] { "", null, "x1.0", "1..0", "1.0.0.0", "1.0-", "1.a", "-1.0", "1.0.0 beta", "1234567890" })
                t.Check(!ReleaseVersion.TryParse(bad, out _), "UC1 refused: \"" + bad + "\"");
            ReleaseVersion.TryParse("v1.2-rc.1+x", out var v);
            t.Check(v != null && v.ToString() == "1.2.0-rc.1", "UC1 shown as 1.2.0-rc.1 (" + v + ")");
        }

        // UC2
        static void Replies(TestRunner t)
        {
            var r = ReleaseInfo.Parse("{\"tag_name\":\"v1.0.1\",\"name\":\"HS2Next 1.0.1\",\"html_url\":\"https://github.com/stepthird/HS2Next/releases/tag/v1.0.1\","
                                      + "\"published_at\":\"2026-11-03T00:00:00Z\",\"draft\":false,\"assets\":[]}", out string e);
            t.Check(r != null && e == null && r.Tag == "v1.0.1" && r.Name == "HS2Next 1.0.1" && r.PublishedAt == "2026-11-03T00:00:00Z"
                    && r.HtmlUrl.EndsWith("/tag/v1.0.1", StringComparison.Ordinal), "UC2 the four members are read");
            t.Check(ReleaseInfo.Parse("[1,2]", out e) == null && e.Contains("object"), "UC2 an array is refused (" + e + ")");
            t.Check(ReleaseInfo.Parse("{\"name\":\"x\"}", out e) == null && e.Contains("tag_name"), "UC2 no tag_name is refused (" + e + ")");
            t.Check(ReleaseInfo.Parse("{\"tag_name\":5}", out e) == null, "UC2 a tag_name that is not a string is refused");
            t.Check(ReleaseInfo.Parse("<html>rate limited</html>", out e) == null && e.Contains("JSON"), "UC2 a body that is not JSON is refused (" + e + ")");
            t.Check(ReleaseInfo.Parse("{\"tag_name\":\"v2\",\"published_at\":\"2026-11-03T00:00:00Z\"}", out e)?.PublishedAt == "2026-11-03T00:00:00Z",
                    "UC2 published_at stays text (no date parse)");
        }

        // UC3
        static void Decisions(TestRunner t)
        {
            t.Check(UpdateCheckRules.Compare("1.0.0", "v1.0.1", out _) == UpdateOutcome.Newer, "UC3 newer");
            t.Check(UpdateCheckRules.Compare("1.0.0", "v1.0.0", out _) == UpdateOutcome.Latest, "UC3 the same version is the latest");
            t.Check(UpdateCheckRules.Compare("1.0.1", "v1.0.0", out _) == UpdateOutcome.Latest, "UC3 an older release is not offered");
            t.Check(UpdateCheckRules.Compare("1.0.0", "nightly", out string why) == UpdateOutcome.Failed && why.Contains("nightly"), "UC3 a tag that is no version fails (" + why + ")");
            t.Check(UpdateCheckRules.Compare("dev", "v1.0.0", out why) == UpdateOutcome.Failed && why.Contains("dev"), "UC3 a build version that is no version fails");
            t.Check(UpdateCheckRules.ShowsDialog(UpdateOutcome.Newer, "v1.0.1", null, false), "UC3 at start: newer, not skipped -> dialog");
            t.Check(!UpdateCheckRules.ShowsDialog(UpdateOutcome.Newer, "v1.0.1", "v1.0.1", false), "UC3 at start: newer, skipped -> nothing");
            t.Check(!UpdateCheckRules.ShowsDialog(UpdateOutcome.Newer, "v1.0.1", "1.0.1", false), "UC3 the skipped tag compares as a version (1.0.1 = v1.0.1)");
            t.Check(UpdateCheckRules.ShowsDialog(UpdateOutcome.Newer, "v1.0.2", "v1.0.1", false), "UC3 at start: a later tag than the skipped one -> dialog");
            t.Check(!UpdateCheckRules.ShowsDialog(UpdateOutcome.Latest, "v1.0.0", null, false), "UC3 at start: latest -> nothing");
            t.Check(!UpdateCheckRules.ShowsDialog(UpdateOutcome.Failed, null, null, false), "UC3 at start: failure -> nothing");
            foreach (var o in new[] { UpdateOutcome.Newer, UpdateOutcome.Latest, UpdateOutcome.Failed })
                t.Check(UpdateCheckRules.ShowsDialog(o, "v1.0.1", "v1.0.1", true), "UC3 by hand: " + o + " -> dialog (even skipped)");
        }

        // UC4
        static void Pages(TestRunner t)
        {
            string page = UpdateCheckRules.ReleasesPage;
            t.Check(page == "https://github.com/" + UpdateCheckRules.Repository + "/releases" && UpdateCheckRules.ApiUrl.StartsWith("https://api.github.com/repos/", StringComparison.Ordinal),
                    "UC4 the source and the page are the public repository's");
            t.Check(UpdateCheckRules.DownloadPage(page + "/tag/v1.0.1") == page + "/tag/v1.0.1", "UC4 the reply's page under the releases path is opened");
            foreach (var bad in new[] { null, "", "https://example.com/x", page, page + "x/tag/v1", "http://github.com/" + UpdateCheckRules.Repository + "/releases/tag/v1",
                                        page + "/tag/v1 \"x\"", "file:///x.exe" })
                t.Check(UpdateCheckRules.DownloadPage(bad) == page, "UC4 replaced by the releases page: \"" + bad + "\"");
            t.Check(UpdateCheckRules.Display("v1.0.1") == "1.0.1" && UpdateCheckRules.Display("nightly") == "nightly", "UC4 the dialog shows 1.0.1 for v1.0.1");
        }

        // UC5
        static void State(TestRunner t)
        {
            string dir = Path.Combine(Path.GetTempPath(), "hs2n_uc5_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "config", UpdateCheckRules.StateFileName);
            try
            {
                var empty = UpdateCheckState.Load(path);
                t.Check(empty.Skipped == null && empty.LastResult == null, "UC5 a missing file is an empty state");
                var s = new UpdateCheckState { Skipped = "v1.0.1", LastCheckUtc = "2026-11-02T03:04:05Z", LastResult = "newer", LastTag = "v1.0.1" };
                s.Save(path);
                var r = UpdateCheckState.Load(path);
                t.Check(r.Skipped == "v1.0.1" && r.LastCheckUtc == "2026-11-02T03:04:05Z" && r.LastResult == "newer" && r.LastTag == "v1.0.1", "UC5 the round trip keeps every member");
                string text = File.ReadAllText(path, Encoding.UTF8);
                t.Check(text.Contains("\"version\": 1") && !text.StartsWith("\uFEFF", StringComparison.Ordinal), "UC5 the file has version 1 and no BOM");
                File.WriteAllText(path, "{ not json", Encoding.UTF8);
                t.Check(UpdateCheckState.Load(path).Skipped == null, "UC5 a broken file is an empty state");
                File.WriteAllText(path, "{\"skipped\": 3}", Encoding.UTF8);
                t.Check(UpdateCheckState.Load(path).Skipped == null, "UC5 a member of the wrong type is left out");
                t.Check(UpdateCheckState.ResultName(UpdateOutcome.Newer) == "newer" && UpdateCheckState.ResultName(UpdateOutcome.Latest) == "latest"
                        && UpdateCheckState.ResultName(UpdateOutcome.Failed) == "failed", "UC5 the result names");
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
        }

        // UC6
        static void Strings(TestRunner t)
        {
            string file = Path.Combine(t.FixturesDir ?? "", "L10n", "strings.tsv");
            t.Check(File.Exists(file), "UC6 strings.tsv exists");
            if (!File.Exists(file)) return;
            var table = L10nTable.Parse(File.ReadAllText(file, Encoding.UTF8));
            var keys = new[] { "option." + OptionDefs.UpdateCheck, "action." + ActionIds.HelpCheckUpdates, "dialog.update.title", "dialog.update.newer",
                               "dialog.update.latest", "dialog.update.failed", "dialog.update.open", "dialog.update.skip", "dialog.update.later" };
            foreach (var k in keys)
            {
                bool en = !string.IsNullOrEmpty(table.Cell(k, "en")), ja = !string.IsNullOrEmpty(table.Cell(k, "ja")), zh = !string.IsNullOrEmpty(table.Cell(k, "zh-Hans"));
                t.Check(en && ja && zh && table.Unreviewed(k, "zh-Hans"), "UC6 " + k + ": en, ja and zh-Hans (marked zh?)");
            }
            var mism = table.PlaceholderMismatches().Select(x => x.ToString()).Where(x => keys.Any(k => x.Contains(k))).ToList();
            t.Check(mism.Count == 0, "UC6 the placeholders match" + (mism.Count > 0 ? ": " + string.Join("; ", mism) : ""));
            t.Check(OptionDefs.TryGet(OptionDefs.UpdateCheck, out var d) && d.Kind == OptionKind.Bool && d.Default is bool b && b && d.Section == "general",
                    "UC6 general.updateCheck is a General toggle, on by default");
        }
    }
}
