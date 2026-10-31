// What the settings page and app.settings know about each key of the settings file (Unity-free): its kind, its default, the
// range or the choices, the command-line flag that overrides it, the Options section it is shown in, and that it applies at the
// next start (the file is read once, at start). Validate checks a value with the rules the settings stage applies when it reads
// the file (a member of the wrong JSON type or out of range is an error there, so the page and the command never write one).
// Unknown keys of a file get the nearest known key (edit distance at most 2) as a suggestion. Only rows whose key the runtime
// reads (SettingsKeys) are offered.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Content;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Setup
{
    public enum SettingKind { Bool, Int, Float, String, Path, PathList, StringList, Token, Choice, Color }

    public sealed class SettingDef
    {
        public string Key;
        public SettingKind Kind;
        /// <summary>The default as JSON (null = none / empty).</summary>
        public JToken Default;
        public double? Min, Max;
        public string[] Choices;
        /// <summary>The command-line flags that set it (the page shows such a value locked).</summary>
        public string[] Args = Array.Empty<string>();
        /// <summary>folders | content | automation | advanced, or null (not on the settings page).</summary>
        public string Section;
        /// <summary>The value takes effect at the next start.</summary>
        public bool Restart = true;
        /// <summary>The Options panel marks it advanced.</summary>
        public bool Advanced;

        public string LabelKey => "settings.key." + Key;
    }

    public sealed class UnknownKey
    {
        public string Key;
        public string Suggestion;
        public override string ToString() => Suggestion == null ? Key : Key + " (did you mean " + Suggestion + "?)";
    }

    public static class SettingsSchema
    {
        public static readonly string[] Sections = { "folders", "content", "automation", "advanced" };

        static SettingDef D(string key, SettingKind kind, JToken def, string section = null, double? min = null, double? max = null, string[] choices = null, string[] args = null, bool advanced = false) =>
            new SettingDef { Key = key, Kind = kind, Default = def, Section = section, Min = min, Max = max, Choices = choices, Args = args ?? Array.Empty<string>(), Advanced = advanced };

        static readonly SettingDef[] rows =
        {
            D("hs2Root", SettingKind.Path, "", "folders", args: new[] { "-gameRoot" }),
            D("modsFolders", SettingKind.PathList, new JArray(), "folders", args: new[] { "-modsFolder" }),
            D("userData", SettingKind.Path, "", "folders", args: new[] { "-userData" }),
            D("cacheDir", SettingKind.Path, "", "folders", args: new[] { "-cacheDir" }),
            D("studio.accessoryItems", SettingKind.Bool, true, "content"),
            D("content.texturePrescan", SettingKind.Bool, true, "content", args: new[] { "-texturePrescan", "-noTexturePrescan" }),
            D("content.bundleCacheMB", SettingKind.Int, 768, "content", 0, 1048576, args: new[] { "-bundleCacheMB" }, advanced: true),
            D("aiInstall", SettingKind.Bool, true, "content"),
            D("sideloaderZipmodSet", SettingKind.Bool, true, "content", args: new[] { "-sideloader" }),
            D("sideloaderAssetRedirect", SettingKind.Bool, true, "content", args: new[] { "-sideloader" }),
            D("bridge.enabled", SettingKind.Bool, true, "automation", args: new[] { "-bridge", "-noBridge" }),
            D("bridge.port", SettingKind.Int, 17322, "automation", 1024, 65535, args: new[] { "-bridgePort" }),
            D("bridge.token", SettingKind.Token, false, "automation", args: new[] { "-bridgeToken" }),
            D("bridge.allowQuit", SettingKind.Bool, false, "automation", args: new[] { "-bridgeAllowQuit" }),
            D("bridge.outputDir", SettingKind.Path, ""),
            D("app.runInBackground", SettingKind.Bool, true, "automation"),
            D("app.frameRate", SettingKind.Int, 0, "automation", 0, 1000),
            D("app.autoStart", SettingKind.Bool, true),
            D("plugins.enabled", SettingKind.Bool, true, "automation", args: new[] { "-noPlugins" }),
            D("plugins.disabled", SettingKind.StringList, new JArray(), "automation"),
            D("plugins.dir", SettingKind.Path, "", args: new[] { "-pluginsDir" }),
            D("maps.maxMirrors", SettingKind.Int, 4, "advanced", 0, 16),
            D("mapAudioLink", SettingKind.Bool, false, "advanced"),
            D("content.unloadUnusedMinSec", SettingKind.Float, 30, "advanced", 0, 86400),
            D("maps.audioLinkEditor", SettingKind.Bool, false),
            D("render.aa", SettingKind.Choice, "smaa", choices: new[] { "smaa", "taa", "fxaa", "none" }),
            D("render.taaQuality", SettingKind.Choice, "high", choices: new[] { "veryLow", "low", "medium", "high", "veryHigh" }),
            D("render.grading", SettingKind.Choice, "hs2", choices: new[] { "hs2", "hdr" }),
            D("render.ambient", SettingKind.Choice, "hs2", choices: new[] { "hs2", "environment" }),
            D("render.lightFalloff", SettingKind.Choice, "hs2", choices: new[] { "hs2", "urp" }),
            D("render.shadowDistance", SettingKind.Float, 100, min: 10, max: 400),
            D("render.backgroundColor", SettingKind.Color, new JArray(0, 0, 0, 1)),
            D("render.ssr", SettingKind.Choice, "off", choices: new[] { "off", "on" }),
            D("kinematics.exclusive", SettingKind.Bool, false),
            D("kinematics.rotationLimits", SettingKind.Bool, false),
            D("kinematics.limitMinInterior", SettingKind.Float, 20, min: 0, max: 179),
            D("kinematics.missingTargetsFirstFrame", SettingKind.Bool, false),
            D("ui.enabled", SettingKind.Bool, true, args: new[] { "-ui", "-noUi" }),
            D("ui.language", SettingKind.String, "en", args: new[] { "-uiLanguage" }),
            D("ui.scale", SettingKind.Int, 100, min: 75, max: 200, args: new[] { "-uiScale" }),
            D("ui.uiScale", SettingKind.Int, 100, min: 75, max: 200, args: new[] { "-uiScale" }),
            D("ui.preset", SettingKind.String, "default", args: new[] { "-uiPreset" }),
            D("ui.keyPreset", SettingKind.String, "default", args: new[] { "-uiPreset" }),
        };

        /// <summary>The rows whose key the runtime reads (SettingsKeys.All), in page order.</summary>
        public static IReadOnlyList<SettingDef> All
        {
            get
            {
                var known = new HashSet<string>(SettingsKeys.All, StringComparer.Ordinal);
                return rows.Where(r => known.Contains(r.Key)).ToList();
            }
        }

        /// <summary>Every row, also those of keys the runtime does not read yet (tests).</summary>
        public static IReadOnlyList<SettingDef> Rows => rows;

        public static bool TryGet(string key, out SettingDef def)
        {
            def = null;
            if (string.IsNullOrEmpty(key)) return false;
            foreach (var r in rows)
                if (r.Key == key) { def = r; break; }
            return def != null && SettingsKeys.All.Contains(key);
        }

        public static IEnumerable<SettingDef> InSection(string section) => All.Where(r => r.Section == section);

        /// <summary>
        /// Checks a value for a key (the rules of the settings stage); returns the value to write (null = remove the member: an
        /// empty path / list or the default of a path) or throws FormatException naming the key.
        /// </summary>
        public static JToken Validate(SettingDef d, JToken v)
        {
            string w = d.Key;
            if (v == null || v.Type == JTokenType.Null) return null;
            switch (d.Kind)
            {
                case SettingKind.Bool:
                    if (v.Type != JTokenType.Boolean) throw new FormatException(w + " must be true or false");
                    return v;
                case SettingKind.Int:
                {
                    if (v.Type != JTokenType.Integer) throw new FormatException(w + " must be an integer");
                    long x = (long)v;
                    if ((d.Min.HasValue && x < d.Min.Value) || (d.Max.HasValue && x > d.Max.Value)) throw new FormatException(w + " must be in " + d.Min + "–" + d.Max);
                    return v;
                }
                case SettingKind.Float:
                {
                    if (v.Type != JTokenType.Integer && v.Type != JTokenType.Float) throw new FormatException(w + " must be a number");
                    double x = (double)v;
                    if (double.IsNaN(x) || double.IsInfinity(x) || (d.Min.HasValue && x < d.Min.Value) || (d.Max.HasValue && x > d.Max.Value))
                        throw new FormatException(w + " must be in " + d.Min + "–" + d.Max);
                    return v;
                }
                case SettingKind.String:
                case SettingKind.Path:
                    if (v.Type != JTokenType.String) throw new FormatException(w + " must be a string");
                    if (d.Kind == SettingKind.Path && ((string)v).Length == 0) return null;
                    if (d.Kind == SettingKind.Path && ((string)v).IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) throw new FormatException(w + " holds characters a path cannot hold");
                    return v;
                case SettingKind.PathList:
                case SettingKind.StringList:
                    if (!(v is JArray a) || a.Any(t => t.Type != JTokenType.String)) throw new FormatException(w + " must be an array of strings");
                    var clean = new JArray(a.Select(t => ((string)t).Trim()).Where(s => s.Length > 0));
                    return clean.Count == 0 ? null : clean;
                case SettingKind.Token:
                    if (v.Type == JTokenType.Boolean || v.Type == JTokenType.String) return v;
                    throw new FormatException(w + " must be true, false or a string");
                case SettingKind.Choice:
                    if (v.Type == JTokenType.String && Array.IndexOf(d.Choices, (string)v) >= 0) return v;
                    throw new FormatException(w + " must be one of " + string.Join(", ", d.Choices));
                case SettingKind.Color:
                    if (v is JArray c && (c.Count == 3 || c.Count == 4) && c.All(t => t.Type == JTokenType.Integer || t.Type == JTokenType.Float)) return v;
                    throw new FormatException(w + " must be [r, g, b] or [r, g, b, a]");
            }
            throw new FormatException(w + ": unsupported kind");
        }

        /// <summary>Whether a command line sets the key (one of its flags is present).</summary>
        public static bool FromCommandLine(SettingDef d, string[] args)
        {
            if (args == null) return false;
            foreach (var f in d.Args) if (Array.IndexOf(args, f) >= 0) return true;
            return false;
        }

        /// <summary>
        /// The value a command line gives the key (null when it gives none): a flag's value (-gameRoot, -cacheDir, -userData,
        /// -bridgePort, -pluginsDir, -bundleCacheMB, -uiLanguage, -uiScale, -uiPreset, -sideloader on|off, -bridgeToken [value]),
        /// a switch's meaning (-noBridge false, -bridge true, -noPlugins false, -bridgeAllowQuit true, -texturePrescan true,
        /// -noTexturePrescan false, -ui true, -noUi false) or the extra -modsFolder folders.
        /// </summary>
        public static JToken ArgValue(SettingDef d, string[] args)
        {
            if (args == null || !FromCommandLine(d, args)) return null;
            string Next(string flag)
            {
                int i = Array.LastIndexOf(args, flag);
                return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : null;
            }
            switch (d.Key)
            {
                case "modsFolders":
                    var l = new JArray();
                    for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-modsFolder") l.Add(args[i + 1]);
                    return l;
                case "bridge.enabled": return Array.IndexOf(args, "-noBridge") >= 0 ? false : (JToken)true;
                case "plugins.enabled": return false;
                case "bridge.allowQuit": return true;
                case "bridge.token": return (JToken)Next("-bridgeToken") ?? true;
                case "content.texturePrescan": return Array.IndexOf(args, "-noTexturePrescan") < 0;
                case "ui.enabled": return Array.IndexOf(args, "-noUi") < 0;
                case "sideloaderZipmodSet":
                case "sideloaderAssetRedirect":
                    return !string.Equals(Next("-sideloader"), "off", StringComparison.OrdinalIgnoreCase);
            }
            string v = Next(d.Args[0]);
            if (v == null) return null;
            if (d.Kind == SettingKind.Int && long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long n)) return n;
            return v;
        }

        // ---------------------------------------------------------------- unknown keys

        /// <summary>The members of a settings object the runtime does not read, each with the nearest known key (≤ 2 edits) when
        /// one exists. "_…" members are comments.</summary>
        public static List<UnknownKey> UnknownKeys(JObject o)
        {
            var l = new List<UnknownKey>();
            if (o == null) return l;
            var members = new List<KeyValuePair<string, bool>>();
            foreach (var p in o.Properties())
            {
                members.Add(new KeyValuePair<string, bool>(p.Name, p.Value is JObject));
                if (p.Value is JObject b && SettingsKeys.Blocks.Contains(p.Name))
                    foreach (var q in b.Properties()) members.Add(new KeyValuePair<string, bool>(p.Name + "." + q.Name, q.Value is JObject));
            }
            foreach (var k in SettingsKeys.Unknown(members)) l.Add(new UnknownKey { Key = k, Suggestion = Nearest(k) });
            return l;
        }

        /// <summary>The known key (or block name) nearest to <paramref name="key"/> by edit distance (case-insensitive first, then
        /// at most <paramref name="max"/> edits); null when none is near enough or two are equally near.</summary>
        public static string Nearest(string key, int max = 2)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var candidates = SettingsKeys.All.Concat(SettingsKeys.Blocks).Distinct(StringComparer.Ordinal).ToList();
            var ci = candidates.Where(c => string.Equals(c, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ci.Count == 1) return ci[0];
            string best = null;
            int bestD = int.MaxValue;
            bool tie = false;
            foreach (var c in candidates)
            {
                int d = Distance(key.ToLowerInvariant(), c.ToLowerInvariant(), max);
                if (d < bestD) { bestD = d; best = c; tie = false; }
                else if (d == bestD) tie = true;
            }
            return bestD <= max && !tie ? best : null;
        }

        /// <summary>Levenshtein distance, cut at <paramref name="cap"/> + 1.</summary>
        public static int Distance(string a, string b, int cap = int.MaxValue - 1)
        {
            if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                int rowMin = cur[0];
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                    if (cur[j] < rowMin) rowMin = cur[j];
                }
                if (rowMin > cap) return cap + 1;
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }
    }
}
