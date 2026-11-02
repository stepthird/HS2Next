// The update check's Unity-free part (added after 1.0.0): the release source of the public
// repository, the version order, the reply of the latest-release request, the outcome and whether it shows a dialog
//, the download page the dialog may open, and the state file <UserData>/config/update_check.json. The request
// and the dialog are UpdateCheckModule's (Runtime/UI/Setup).
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Core
{
    /// <summary>
    /// A version: <c>[v]MAJOR[.MINOR[.PATCH]]</c>, an optional <c>-suffix</c> (a pre-release, which sorts before the
    /// same numbers without one) and <c>+build</c> (dropped). Missing parts are 0.
    /// </summary>
    public sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        public readonly int Major, Minor, Patch;
        /// <summary>The pre-release suffix, or null for a release.</summary>
        public readonly string Suffix;

        ReleaseVersion(int major, int minor, int patch, string suffix) { Major = major; Minor = minor; Patch = patch; Suffix = suffix; }

        public static bool TryParse(string text, out ReleaseVersion version)
        {
            version = null;
            string s = (text ?? "").Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            string suffix = null;
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                suffix = s.Substring(dash + 1);
                s = s.Substring(0, dash);
                if (suffix.Length == 0) return false;
            }
            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 3) return false;
            var n = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 || parts[i].Length > 9) return false;
                foreach (char c in parts[i]) if (c < '0' || c > '9') return false;
                n[i] = int.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture);
            }
            version = new ReleaseVersion(n[0], n[1], n[2], suffix);
            return true;
        }

        public int CompareTo(ReleaseVersion other)
        {
            if (other == null) return 1;
            int c = Major.CompareTo(other.Major);
            if (c == 0) c = Minor.CompareTo(other.Minor);
            if (c == 0) c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            if (Suffix == null) return other.Suffix == null ? 0 : 1;
            if (other.Suffix == null) return -1;
            return string.CompareOrdinal(Suffix, other.Suffix);
        }

        public override string ToString() =>
            Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture) + "." +
            Patch.ToString(CultureInfo.InvariantCulture) + (Suffix != null ? "-" + Suffix : "");
    }

    /// <summary>The members of the latest-release reply the check reads.</summary>
    public sealed class ReleaseInfo
    {
        public string Tag, Name, HtmlUrl, PublishedAt;

        /// <summary>The release of a reply body; null with <paramref name="error"/> when it is not an object with a tag_name.</summary>
        public static ReleaseInfo Parse(string json, out string error)
        {
            error = null;
            JObject o;
            try { o = JsonConvert.DeserializeObject<JToken>(json ?? "", new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }) as JObject; }
            catch (JsonException e) { error = "the reply is not JSON (" + e.Message + ")"; return null; }
            if (o == null) { error = "the reply is not a JSON object"; return null; }
            string Str(string k) => o.TryGetValue(k, out var v) && v.Type == JTokenType.String ? (string)v : null;
            var r = new ReleaseInfo { Tag = Str("tag_name"), Name = Str("name"), HtmlUrl = Str("html_url"), PublishedAt = Str("published_at") };
            if (string.IsNullOrEmpty(r.Tag)) { error = "the reply has no tag_name"; return null; }
            return r;
        }
    }

    public enum UpdateOutcome { Newer, Latest, Failed }

    public static class UpdateCheckRules
    {
        /// <summary>The public repository (the only place the account handle appears in exported text).</summary>
        public const string Repository = "stepthird/HS2Next";
        public const string ApiUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";
        public const int TimeoutSeconds = 10;
        public const int MaxReplyBytes = 1 << 20;
        public const string StateFileName = "update_check.json";
        public const string DialogId = "update";

        /// <summary>Newer when the tag is a later version than <paramref name="current"/>, Latest when it is the same or older;
        /// Failed with <paramref name="reason"/> when either does not parse.</summary>
        public static UpdateOutcome Compare(string current, string tag, out string reason)
        {
            reason = null;
            if (!ReleaseVersion.TryParse(tag, out var latest)) { reason = "the release tag \"" + tag + "\" is not a version"; return UpdateOutcome.Failed; }
            if (!ReleaseVersion.TryParse(current, out var mine)) { reason = "this build's version \"" + current + "\" is not a version"; return UpdateOutcome.Failed; }
            return latest.CompareTo(mine) > 0 ? UpdateOutcome.Newer : UpdateOutcome.Latest;
        }

        /// <summary>The tag is the skipped one (the same version when both parse, else the same text).</summary>
        public static bool IsSkipped(string tag, string skipped)
        {
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(skipped)) return false;
            if (ReleaseVersion.TryParse(tag, out var a) && ReleaseVersion.TryParse(skipped, out var b)) return a.CompareTo(b) == 0;
            return string.Equals(tag, skipped, StringComparison.Ordinal);
        }

        /// <summary>by hand every outcome shows the dialog; at start only a newer tag that is not skipped.</summary>
        public static bool ShowsDialog(UpdateOutcome outcome, string tag, string skipped, bool byHand) =>
            byHand || (outcome == UpdateOutcome.Newer && !IsSkipped(tag, skipped));

        /// <summary>The page Open the download page opens: the reply's page when it lies under the releases page, else the
        /// releases page itself.</summary>
        public static string DownloadPage(string htmlUrl) =>
            htmlUrl != null && htmlUrl.StartsWith(ReleasesPage + "/", StringComparison.Ordinal) && htmlUrl.IndexOfAny(new[] { ' ', '"', '\n', '\r', '\t' }) < 0
                ? htmlUrl : ReleasesPage;

        /// <summary>A tag as the dialog shows it (the version without a leading v).</summary>
        public static string Display(string tag) =>
            ReleaseVersion.TryParse(tag, out var v) ? v.ToString() : tag;
    }

    /// <summary>The state file; a missing or unreadable file is an empty state.</summary>
    public sealed class UpdateCheckState
    {
        public string Skipped, LastCheckUtc, LastResult, LastTag;

        public static UpdateCheckState Load(string path)
        {
            var s = new UpdateCheckState();
            try
            {
                if (path == null || !File.Exists(path)) return s;
                if (!(JsonConvert.DeserializeObject<JToken>(File.ReadAllText(path, Encoding.UTF8), new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }) is JObject o)) return s;
                string Str(string k) => o.TryGetValue(k, out var v) && v.Type == JTokenType.String ? (string)v : null;
                s.Skipped = Str("skipped");
                s.LastCheckUtc = Str("lastCheckUtc");
                s.LastResult = Str("lastResult");
                s.LastTag = Str("lastTag");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException) { }
            return s;
        }

        public void Save(string path)
        {
            var o = new JObject
            {
                ["version"] = 1,
                ["skipped"] = Skipped,
                ["lastCheckUtc"] = LastCheckUtc,
                ["lastResult"] = LastResult,
                ["lastTag"] = LastTag,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, o.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
        }

        /// <summary>The name a result is stored under (newer, latest, failed).</summary>
        public static string ResultName(UpdateOutcome o) => o == UpdateOutcome.Newer ? "newer" : o == UpdateOutcome.Latest ? "latest" : "failed";
    }
}
