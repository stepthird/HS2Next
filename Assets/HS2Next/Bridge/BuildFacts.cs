// The facts of one player build - its kind, version and commit - as the build writes them into
// HS2Next_Data/StreamingAssets/build.json, read without the engine. The app player build writes kind "dev"; the packaging
// step copies that tested build and rewrites the file with kind "distribution", so both kinds run the same binaries. The only
// behaviour that depends on the kind is the default of the bridge token: required in the distributed build, off in
// development builds and in the Editor (the settings member bridge.token and -bridgeToken override it either way).
// A missing file means kind "dev" in a player; the Editor is always kind "editor". A file that cannot be read or parsed is
// not fatal: the process keeps its default kind and the facts carry the error for the log.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge
{
    public sealed class BuildFacts
    {
        /// <summary>The file name under the player's StreamingAssets folder.</summary>
        public const string FileName = "build.json";
        public const string KindEditor = "editor";
        public const string KindDev = "dev";
        public const string KindDistribution = "distribution";

        /// <summary>editor, dev or distribution.</summary>
        public string Kind = KindDev;
        /// <summary>The app version (the project's bundle version when built).</summary>
        public string Version;
        /// <summary>The short commit the build came from; null when unknown.</summary>
        public string Commit;
        /// <summary>True when the build's sources differed from that commit; null when unknown.</summary>
        public bool? Dirty;
        /// <summary>When the build was made (ISO 8601, UTC); null when unknown.</summary>
        public string BuiltUtc;
        /// <summary>The file the facts were read from; null when they are the process's defaults.</summary>
        public string SourceFile;
        /// <summary>Why the file was not used (unreadable, not JSON, a bad member); null when it was, or when there was none.</summary>
        public string Error;

        public bool IsDistribution => Kind == KindDistribution;

        /// <summary>The bridge requires a token by default: true in the distributed build only.</summary>
        public bool TokenRequiredByDefault => IsDistribution;

        /// <summary>The facts of a process without a build file: kind editor in the Editor, dev in a player.</summary>
        public static BuildFacts Default(bool editor, string version) => new BuildFacts { Kind = editor ? KindEditor : KindDev, Version = version };

        /// <summary>
        /// The facts of build.json text. A text that is not a JSON object, a member of the wrong type, or a kind other than dev
        /// and distribution gives the defaults of the process with <see cref="Error"/> set. An absent member stays unknown
        /// (the version: <paramref name="version"/>).
        /// </summary>
        public static BuildFacts Parse(string json, bool editor, string version)
        {
            var d = Default(editor, version);
            if (editor) return d;
            JObject o;
            try
            {
                o = JsonConvert.DeserializeObject<JToken>(json ?? "", new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }) as JObject;
            }
            catch (Exception e) { d.Error = "not JSON (" + e.Message + ")"; return d; }
            if (o == null) { d.Error = "not a JSON object"; return d; }
            try
            {
                var f = new BuildFacts { Version = version };
                string kind = Str(o, "kind");
                if (kind != KindDev && kind != KindDistribution) throw new FormatException("kind must be \"dev\" or \"distribution\"");
                f.Kind = kind;
                f.Version = Str(o, "version") ?? version;
                f.Commit = Str(o, "commit");
                f.BuiltUtc = Str(o, "builtUtc");
                if (o.TryGetValue("dirty", out var dt) && dt.Type != JTokenType.Null)
                {
                    if (dt.Type != JTokenType.Boolean) throw new FormatException("dirty must be true or false");
                    f.Dirty = (bool)dt;
                }
                return f;
            }
            catch (FormatException e) { d.Error = e.Message; return d; }
        }

        /// <summary>The facts of &lt;<paramref name="dir"/>&gt;/build.json (the StreamingAssets folder); the defaults when the file is absent.</summary>
        public static BuildFacts Read(string dir, bool editor, string version)
        {
            if (editor || string.IsNullOrEmpty(dir)) return Default(editor, version);
            string path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return Default(editor, version);
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception e) { var d = Default(editor, version); d.Error = "cannot read " + FileName + ": " + e.Message; return d; }
            var f = Parse(text, editor, version);
            f.SourceFile = path;
            return f;
        }

        /// <summary>The reply object of bridge.version.build: {kind, version, commit, dirty, builtUtc}.</summary>
        public JObject ToJson() => new JObject
        {
            ["kind"] = Kind,
            ["version"] = Text(Version),
            ["commit"] = Text(Commit),
            ["dirty"] = Dirty.HasValue ? (JToken)Dirty.Value : JValue.CreateNull(),
            ["builtUtc"] = Text(BuiltUtc),
        };

        static JToken Text(string s) => s == null ? JValue.CreateNull() : new JValue(s);

        /// <summary>The text of build.json for these facts (indented, one trailing newline).</summary>
        public string ToFileText() => ToJson().ToString(Formatting.Indented) + "\n";

        /// <summary>One line for the log: "dev 0.3.0 (abc1234; dirty; built 2026-10-30T17:00:00Z)".</summary>
        public string Summary
        {
            get
            {
                var sb = new StringBuilder(Kind).Append(' ').Append(Version ?? "?");
                var parts = new System.Collections.Generic.List<string>();
                if (Commit != null) parts.Add(Commit);
                if (Dirty == true) parts.Add("dirty");
                if (BuiltUtc != null) parts.Add("built " + BuiltUtc);
                if (Error != null) parts.Add("build.json not used: " + Error);
                if (parts.Count > 0) sb.Append(" (").Append(string.Join("; ", parts)).Append(')');
                return sb.ToString();
            }
        }

        /// <summary>The current UTC time as builtUtc writes it.</summary>
        public static string UtcStamp(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        static string Str(JObject o, string name)
        {
            if (!o.TryGetValue(name, out var v) || v.Type == JTokenType.Null) return null;
            if (v.Type != JTokenType.String) throw new FormatException(name + " must be a string");
            return (string)v;
        }
    }
}
