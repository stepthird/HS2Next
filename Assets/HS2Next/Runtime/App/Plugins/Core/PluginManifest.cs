// The plugin manifest (manifest.json in a plugin folder) and the version rules of the loader. Unity-free (Newtonsoft
// only), so the plain-Mono suite `plugins` tests it. A manifest is read with the bridge's JSON rules: UTF-8 (a BOM is
// skipped), one object; required members of the wrong JSON type or with an invalid value refuse the plugin with
// plugin.badManifest (or plugin.reservedGuid for a reserved GUID); unknown members are kept for the report and ignored.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Plugins
{
    /// <summary>The refusal and information codes of the loader (one log line each, shown by plugin.list).</summary>
    public static class PluginCodes
    {
        public const string BadManifest = "plugin.badManifest";
        public const string FolderMismatch = "plugin.folderMismatch";
        public const string DuplicateGuid = "plugin.duplicateGuid";
        public const string ReservedGuid = "plugin.reservedGuid";
        public const string ApiVersion = "plugin.apiVersion";
        public const string AppVersion = "plugin.appVersion";
        public const string MissingAssembly = "plugin.missingAssembly";
        public const string AssemblyCollision = "plugin.assemblyCollision";
        public const string PrefixTaken = "plugin.prefixTaken";
        public const string Dependency = "plugin.dependency";
        public const string DependencyCycle = "plugin.dependencyCycle";
        public const string Disabled = "plugin.disabled";
        public const string TypeLoad = "plugin.typeLoad";
        public const string StaleAssembly = "plugin.staleAssembly";
        /// <summary>No public non-abstract IPlugin type with a public parameterless constructor (or several, or entryType not found).</summary>
        public const string NoEntry = "plugin.noEntry";
        /// <summary>Loading an assembly, creating the entry type or Load threw (state failed).</summary>
        public const string LoadFailed = "plugin.loadFailed";
        /// <summary>Information: a subfolder of the plugin folder without manifest.json.</summary>
        public const string NoManifest = "plugin.noManifest";
        /// <summary>Information: an order hint was dropped because it would have made a cycle.</summary>
        public const string OrderHint = "plugin.orderHint";
    }

    /// <summary>The states of a plugin as bridge.version and plugin.list report them.</summary>
    public static class PluginStates
    {
        public const string Pending = "pending";
        public const string Loaded = "loaded";
        public const string Failed = "failed";
        public const string Refused = "refused";
        public const string Disabled = "disabled";
        public const string Faulted = "faulted";
        public const string Unloaded = "unloaded";
    }

    /// <summary>A semantic version MAJOR.MINOR.PATCH[-prerelease][+build] with semver precedence (build ignored).</summary>
    public sealed class SemVer : IComparable<SemVer>
    {
        static readonly Regex Rx = new Regex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);

        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        /// <summary>The pre-release part without the dash, or null.</summary>
        public string Prerelease { get; private set; }

        public static bool TryParse(string s, out SemVer v)
        {
            v = null;
            if (s == null) return false;
            var m = Rx.Match(s);
            if (!m.Success) return false;
            if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int a) ||
                !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int b) ||
                !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int c)) return false;
            v = new SemVer { Major = a, Minor = b, Patch = c, Prerelease = m.Groups[4].Success ? m.Groups[4].Value : null };
            return true;
        }

        public int CompareTo(SemVer o)
        {
            if (o == null) return 1;
            int c = Major.CompareTo(o.Major);
            if (c == 0) c = Minor.CompareTo(o.Minor);
            if (c == 0) c = Patch.CompareTo(o.Patch);
            if (c != 0) return c;
            if (Prerelease == null) return o.Prerelease == null ? 0 : 1;
            if (o.Prerelease == null) return -1;
            var a = Prerelease.Split('.');
            var b = o.Prerelease.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool na = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out int ia);
                bool nb = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out int ib);
                int d = na && nb ? ia.CompareTo(ib) : na ? -1 : nb ? 1 : string.CompareOrdinal(a[i], b[i]);
                if (d != 0) return d;
            }
            return a.Length.CompareTo(b.Length);
        }

        public override string ToString() => Major + "." + Minor + "." + Patch + (Prerelease != null ? "-" + Prerelease : "");

        /// <summary>
        /// Whether a plugin built against Api <paramref name="pluginApi"/> ("MAJOR.MINOR") runs on host Api
        /// <paramref name="hostMajor"/>.<paramref name="hostMinor"/>: on 0.x the minor must match exactly; from 1.0 the
        /// major must match and the host's minor must be at least the plugin's.
        /// </summary>
        public static bool ApiCompatible(string pluginApi, int hostMajor, int hostMinor)
        {
            if (!TryParseMajorMinor(pluginApi, out int ma, out int mi)) return false;
            if (ma != hostMajor) return false;
            return hostMajor == 0 ? mi == hostMinor : hostMinor >= mi;
        }

        static readonly Regex MajorMinorRx = new Regex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);

        /// <summary>"MAJOR.MINOR".</summary>
        public static bool TryParseMajorMinor(string s, out int major, out int minor)
        {
            major = minor = 0;
            if (s == null) return false;
            var m = MajorMinorRx.Match(s);
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major) &&
                   int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor);
        }
    }

    /// <summary>One dependency of a manifest.</summary>
    public sealed class PluginDependency
    {
        public string Guid;
        /// <summary>Semver, or null (any version).</summary>
        public string MinVersion;
    }

    /// <summary>A parsed manifest.json.</summary>
    public sealed class PluginManifest
    {
        public const string FileName = "manifest.json";
        public const int CurrentManifestVersion = 1;

        static readonly Regex GuidRx = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);
        static readonly Regex PrefixRx = new Regex(@"^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)*\.$", RegexOptions.CultureInvariant);
        static readonly string[] Known =
        {
            "manifestVersion", "guid", "name", "version", "apiVersion", "assemblies", "entryType", "commandPrefixes", "dependencies",
            "loadAfter", "loadBefore", "minAppVersion", "description", "author", "license", "url",
        };

        public int ManifestVersion;
        public string Guid, Name, Version, ApiVersion, EntryType, MinAppVersion, Description, Author, License, Url;
        public List<string> Assemblies = new List<string>();
        public List<string> CommandPrefixes = new List<string>();
        public List<PluginDependency> Dependencies = new List<PluginDependency>();
        public List<string> LoadAfter = new List<string>();
        public List<string> LoadBefore = new List<string>();
        /// <summary>Members this loader does not read, kept for the report.</summary>
        public JObject Unknown = new JObject();

        /// <summary>
        /// True for a GUID of the ext-namespace grammar <c>[A-Za-z0-9][A-Za-z0-9._-]{0,127}</c> with at least one dot.
        /// Reserved GUIDs (<see cref="IsReservedGuid"/>) are valid names but refused separately.
        /// </summary>
        public static bool IsValidGuid(string g) => g != null && GuidRx.IsMatch(g) && g.IndexOf('.') > 0;

        /// <summary>core, bridge, and names starting with hs2. / hs2next. / illusion. (the reserved ext namespaces), ignoring case.</summary>
        public static bool IsReservedGuid(string g)
        {
            if (g == null) return false;
            string l = g.ToLowerInvariant();
            return l == "core" || l == "bridge" || l.StartsWith("hs2.", StringComparison.Ordinal) || l.StartsWith("hs2next.", StringComparison.Ordinal) ||
                   l.StartsWith("illusion.", StringComparison.Ordinal);
        }

        /// <summary>A command prefix: parts of the command-name grammar ending with a dot (e.g. "seq.").</summary>
        public static bool IsValidPrefix(string p) => p != null && p.Length <= 64 && PrefixRx.IsMatch(p);

        /// <summary>
        /// Parses manifest text. Returns null with <paramref name="code"/> and <paramref name="error"/> set when the manifest
        /// is unusable; <paramref name="guidSeen"/> is the guid member when it was a string (for the report).
        /// </summary>
        public static PluginManifest Parse(string json, out string code, out string error, out string guidSeen)
        {
            code = error = guidSeen = null;
            JObject o;
            try
            {
                if (json != null && json.Length > 0 && json[0] == '﻿') json = json.Substring(1);
                var settings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
                using (var r = new JsonTextReader(new System.IO.StringReader(json ?? "")) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                {
                    var t = JToken.ReadFrom(r, settings);
                    if (r.Read()) throw new JsonReaderException("additional text after the JSON value");
                    o = t as JObject;
                }
            }
            catch (Exception e)
            {
                code = PluginCodes.BadManifest;
                error = "not readable JSON: " + e.Message;
                return null;
            }
            if (o == null)
            {
                code = PluginCodes.BadManifest;
                error = "the manifest is not a JSON object";
                return null;
            }
            if (o.TryGetValue("guid", StringComparison.Ordinal, out var gt) && gt.Type == JTokenType.String) guidSeen = (string)gt;
            var m = new PluginManifest();
            try
            {
                var mv = o["manifestVersion"];
                if (mv == null || mv.Type != JTokenType.Integer) throw Bad("manifestVersion (an integer) is required");
                long mvl = (long)mv;
                if (mvl != CurrentManifestVersion) throw Bad("manifestVersion " + mvl + " is not supported (this loader reads " + CurrentManifestVersion + ")");
                m.ManifestVersion = (int)mvl;
                m.Guid = ReqString(o, "guid");
                if (!IsValidGuid(m.Guid)) throw Bad("guid \"" + m.Guid + "\" is not a reverse-DNS name ([A-Za-z0-9][A-Za-z0-9._-]{0,127} with a dot)");
                m.Name = ReqString(o, "name");
                if (m.Name.Length == 0 || m.Name.Length > 64) throw Bad("name must have 1-64 characters");
                m.Version = ReqString(o, "version");
                if (!SemVer.TryParse(m.Version, out _)) throw Bad("version \"" + m.Version + "\" is not semver (MAJOR.MINOR.PATCH[-pre])");
                m.ApiVersion = ReqString(o, "apiVersion");
                if (!SemVer.TryParseMajorMinor(m.ApiVersion, out _, out _)) throw Bad("apiVersion \"" + m.ApiVersion + "\" is not MAJOR.MINOR");
                m.Assemblies = StringList(o, "assemblies", true);
                if (m.Assemblies.Count == 0) throw Bad("assemblies must list at least one file");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in m.Assemblies)
                {
                    if (a.Length == 0 || a.IndexOf('/') >= 0 || a.IndexOf('\\') >= 0 || a.IndexOf(':') >= 0 || a == "." || a == "..")
                        throw Bad("assemblies: \"" + a + "\" is not a file name in the plugin folder");
                    if (!seen.Add(a)) throw Bad("assemblies: \"" + a + "\" is listed twice");
                }
                m.EntryType = OptString(o, "entryType");
                m.CommandPrefixes = StringList(o, "commandPrefixes", false);
                foreach (var p in m.CommandPrefixes)
                    if (!IsValidPrefix(p)) throw Bad("commandPrefixes: \"" + p + "\" is not a command-name prefix ending with a dot (e.g. \"seq.\")");
                var deps = o["dependencies"];
                if (deps != null && deps.Type != JTokenType.Null)
                {
                    if (!(deps is JArray da)) throw Bad("dependencies must be an array of {guid, minVersion}");
                    foreach (var d in da)
                    {
                        if (!(d is JObject dobj)) throw Bad("dependencies: every entry must be an object {guid, minVersion}");
                        var dep = new PluginDependency { Guid = ReqString(dobj, "guid", "dependencies[].guid"), MinVersion = OptString(dobj, "minVersion", "dependencies[].minVersion") };
                        if (!IsValidGuid(dep.Guid)) throw Bad("dependencies: \"" + dep.Guid + "\" is not a plugin GUID");
                        if (dep.MinVersion != null && !SemVer.TryParse(dep.MinVersion, out _)) throw Bad("dependencies: minVersion \"" + dep.MinVersion + "\" is not semver");
                        m.Dependencies.Add(dep);
                    }
                }
                m.LoadAfter = StringList(o, "loadAfter", false);
                m.LoadBefore = StringList(o, "loadBefore", false);
                m.MinAppVersion = OptString(o, "minAppVersion");
                if (m.MinAppVersion != null && !SemVer.TryParse(m.MinAppVersion, out _)) throw Bad("minAppVersion \"" + m.MinAppVersion + "\" is not semver");
                m.Description = OptString(o, "description");
                m.Author = OptString(o, "author");
                m.License = OptString(o, "license");
                m.Url = OptString(o, "url");
            }
            catch (FormatException e)
            {
                code = PluginCodes.BadManifest;
                error = e.Message;
                return null;
            }
            foreach (var p in o.Properties())
                if (Array.IndexOf(Known, p.Name) < 0) m.Unknown[p.Name] = p.Value.DeepClone();
            if (IsReservedGuid(m.Guid))
            {
                code = PluginCodes.ReservedGuid;
                error = "the GUID \"" + m.Guid + "\" is reserved (core, bridge, hs2.*, hs2next.*, illusion.*)";
                return null;
            }
            return m;
        }

        static FormatException Bad(string message) => new FormatException(message);

        static string ReqString(JObject o, string name, string what = null)
        {
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null) throw Bad((what ?? name) + " (a string) is required");
            if (t.Type != JTokenType.String) throw Bad((what ?? name) + " must be a string");
            return (string)t;
        }

        static string OptString(JObject o, string name, string what = null)
        {
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null) return null;
            if (t.Type != JTokenType.String) throw Bad((what ?? name) + " must be a string");
            return (string)t;
        }

        static List<string> StringList(JObject o, string name, bool required)
        {
            var l = new List<string>();
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) throw Bad(name + " (an array of strings) is required");
                return l;
            }
            if (!(t is JArray a)) throw Bad(name + " must be an array of strings");
            foreach (var e in a)
            {
                if (e.Type != JTokenType.String) throw Bad(name + " must be an array of strings");
                l.Add((string)e);
            }
            return l;
        }
    }
}
