// Reading and writing a scene script's document (a `.hs2nscript` file, at most 16 MiB) for the commands that take one (script.set, script.import)
// and the export. A document may give its Timeline's tracks and timed commands in the stored form (each track and event with its `id`) or in
// the input vocabulary of `timeline.build` and `timeline.events` (no `id`: camera keys {target, rot, dist, fov}, animation keys {group,
// category, no}, depth-of-field keys with sharp / full, focus "$role"); those are turned into the stored form here, by the Timeline's own build
// input with a host that knows the script's roles, so equal key times and every other rule are refused as `timeline.build` refuses them. A
// track key "$role" is a node of the role's kind; an animation row is resolved by the caller's catalog; an eye key of a script gives the pattern
// id (raw: true), since no character is cast when a script is read.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Scripts
{
    /// <summary>What turning build-form tracks into the stored form needs from the app.</summary>
    public sealed class ScriptBuildContext
    {
        /// <summary>The animation row of an <c>anime</c> key (members group, category, no, source / guid / ref) as the animation command resolves it; null
        /// when no row matches. May throw a <see cref="BridgeException"/> naming the argument. Null: a build-form animation key is refused.</summary>
        public Func<JObject, string, AnimeRef> ResolveAnime;
        /// <summary>The kind of a node id a script's track names (a script may keep node ids, meaningful in its scene); null: node ids are refused.</summary>
        public Func<string, NodeKind?> KindOfNode;
        /// <summary>Ids of the converted tracks and events.</summary>
        public IIdSource Ids;
        /// <summary>An unknown member is refused (else a warning).</summary>
        public bool Strict;
        /// <summary>Warnings of the conversion (unknown members, near: false).</summary>
        public List<string> Warnings = new List<string>();
    }

    /// <summary>The build host of a script: a track key "$role" is a node of the role's kind.</summary>
    public sealed class ScriptBuildHost : ITimelineBuildHost
    {
        readonly Dictionary<string, ScriptRole> roles = new Dictionary<string, ScriptRole>(StringComparer.Ordinal);
        readonly ScriptBuildContext context;

        public ScriptBuildHost(IEnumerable<ScriptRole> roles, ScriptBuildContext context)
        {
            if (roles != null) foreach (var r in roles) if (r?.Role != null && !this.roles.ContainsKey(r.Role)) this.roles[r.Role] = r;
            this.context = context ?? new ScriptBuildContext();
        }

        public NodeKind? KindOf(string nodeId)
        {
            string role = ScriptText.RoleOf(nodeId);
            if (role != null)
            {
                if (!roles.TryGetValue(role, out var r)) return null;
                return r.IsChara ? NodeKind.Chara : r.IsItem ? NodeKind.Item : (NodeKind?)null;
            }
            return context.KindOfNode?.Invoke(nodeId);
        }

        public AnimeRef ResolveAnime(string charaKey, JObject key, string arg)
        {
            if (context.ResolveAnime == null) throw new BridgeException(BridgeCodes.Unavailable, arg + ": animation rows cannot be resolved here");
            return context.ResolveAnime(key, arg);
        }

        public int EyePatternAt(string charaKey, int position, string arg) =>
            throw new BridgeException(BridgeError.BadArgs(arg, "a pattern id with raw: true", "a script's eye keys give the pattern id (raw: true): no character is cast when a script is read"));
    }

    public static class ScriptFormat
    {
        /// <summary>The file extension of a script document.</summary>
        public const string Extension = SceneScript.FileExtension;

        /// <summary>True when a document's Timeline gives a track or a timed command in the build vocabulary (an element without <c>id</c>).</summary>
        public static bool HasBuildForm(JObject document)
        {
            if (!(document?["timeline"] is JObject t)) return false;
            foreach (var name in new[] { "tracks", "events" })
                if (t[name] is JArray a)
                    foreach (var e in a)
                        if (e is JObject o && o.Property("id") == null) return true;
            return false;
        }

        /// <summary>The number of build-form tracks and events of a document (elements without <c>id</c>).</summary>
        public static int BuildFormCount(JObject document)
        {
            int n = 0;
            if (!(document?["timeline"] is JObject t)) return 0;
            foreach (var name in new[] { "tracks", "events" })
                if (t[name] is JArray a)
                    foreach (var e in a)
                        if (e is JObject o && o.Property("id") == null) n++;
            return n;
        }

        /// <summary>
        /// Reads a document from its UTF-8 bytes: at most <see cref="ScriptLimits.MaxFileBytes"/> (else <see cref="BridgeException"/> bad_args naming
        /// the limit), JSON syntax and types as the save format reads them (<see cref="SaveFormatException"/>), another format or a newer version refused
        /// (<see cref="ScriptRefusedException"/>), build-form tracks and events turned into the stored form through <paramref name="build"/> (a rule they
        /// break: <see cref="BridgeException"/>; without a context: bad_args). Findings of the reader go to <paramref name="findings"/>.
        /// </summary>
        public static SceneScript Parse(byte[] utf8, FindingList findings = null, ScriptBuildContext build = null, string entry = SceneScript.EntryName)
        {
            if (utf8 == null) throw new ArgumentNullException(nameof(utf8));
            if (utf8.Length > ScriptLimits.MaxFileBytes)
                throw new BridgeException(BridgeError.BadArgs("path", null, ScriptFindings.Limit + ": a script file holds at most " + ScriptLimits.MaxFileBytes + " bytes (" + utf8.Length + ")"));
            var o = DocReader.Parse(utf8, entry ?? SceneScript.EntryName);
            string seed = null;
            if (o.Property("id") == null) seed = "script/" + ScriptText.Hash16(utf8);
            return FromToken(o, findings, build, entry, seed);
        }

        /// <summary>As <see cref="Parse"/> for a parsed object (a command's <c>script</c> argument). The token is not changed.</summary>
        public static SceneScript FromToken(JObject document, FindingList findings = null, ScriptBuildContext build = null, string entry = SceneScript.EntryName,
            string idSeed = null)
        {
            if (document == null) throw new BridgeException(BridgeError.BadArgs("script", "a script document", "no script document"));
            var doc = (JObject)document.DeepClone();
            CheckHeader(doc, entry);
            if (HasBuildForm(doc)) ToStoredForm(doc, build);
            return SceneScript.FromDocumentToken(doc, findings, entry, idSeed);
        }

        /// <summary>Reads a script file (at most <see cref="ScriptLimits.MaxFileBytes"/>); throws as <see cref="Parse"/>, and <see cref="IOException"/> when
        /// the file cannot be read.</summary>
        public static SceneScript ReadFile(string path, FindingList findings = null, ScriptBuildContext build = null)
        {
            var info = new FileInfo(path ?? throw new ArgumentNullException(nameof(path)));
            if (info.Exists && info.Length > ScriptLimits.MaxFileBytes)
                throw new BridgeException(BridgeError.BadArgs("path", null, ScriptFindings.Limit + ": a script file holds at most " + ScriptLimits.MaxFileBytes + " bytes (" + info.Length + ")"));
            return Parse(File.ReadAllBytes(path), findings, build, Path.GetFileName(path));
        }

        /// <summary>The bytes of a script's file: its document (no cast, preparation mark or store number), canonical, UTF-8, trailing LF.</summary>
        public static byte[] ToFileBytes(SceneScript script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            return script.ToDocument().ToDocumentBytes();
        }

        /// <summary>Writes a script's file to <paramref name="path"/> (a temporary file, then a replace); returns the bytes written.</summary>
        public static long WriteFile(SceneScript script, string path)
        {
            var b = ToFileBytes(script);
            ScriptStore.WriteFileAtomic(path, b);
            return b.LongLength;
        }

        // The format and version first, so that a newer document is refused before its tracks are read in a vocabulary it may not use.
        static void CheckHeader(JObject doc, string entry)
        {
            var f = doc["format"];
            var v = doc["version"];
            if (f == null || f.Type != JTokenType.String || v == null || v.Type != JTokenType.Integer) return;   // the reader reports the type error
            string format = (string)f;
            long version = (long)v;
            if (format != SceneScript.FormatName)
                throw new ScriptRefusedException(ScriptFindings.BadFormat, "format \"" + format + "\" is not " + SceneScript.FormatName);
            if (version > SceneScript.FormatVersion)
                throw new ScriptRefusedException(ScriptFindings.NewerVersion, "version " + version + " of " + SceneScript.FormatName + " is newer than " + SceneScript.FormatVersion,
                    version > int.MaxValue ? int.MaxValue : (int)version);
        }

        // Replaces each build-form track and event of the document's Timeline by its stored form (in place, order kept).
        static void ToStoredForm(JObject doc, ScriptBuildContext build)
        {
            var t = (JObject)doc["timeline"];
            if (build == null)
                throw new BridgeException(BridgeError.BadArgs("timeline", null, "the Timeline gives tracks or timed commands in the build vocabulary, which needs the app's catalog here"));
            if (build.Ids == null) throw new ArgumentException("the build context has no id source");
            var roles = new List<ScriptRole>();
            if (doc["roles"] is JArray ra)
                foreach (var e in ra)
                {
                    if (!(e is JObject ro) || !(ro["role"] is JValue rv) || rv.Type != JTokenType.String) continue;
                    var kind = ro["kind"] is JValue kv && kv.Type == JTokenType.String ? (string)kv : null;
                    roles.Add(new ScriptRole((string)rv, kind));
                }
            var host = new ScriptBuildHost(roles, build);
            float duration = TimelineLimits.RepairDuration;
            if (t["duration"] is JValue dv && (dv.Type == JTokenType.Float || dv.Type == JTokenType.Integer)) duration = (float)(double)dv;

            if (t["tracks"] is JArray tracks)
            {
                var stored = new List<TimelineTrack>();
                var buildIdx = new List<int>();
                var buildForm = new JArray();
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (tracks[i] is JObject o && o.Property("id") == null) { buildIdx.Add(i); buildForm.Add(o.DeepClone()); }
                    else if (tracks[i] is JObject so)
                    {
                        // Stored tracks count against the build's channels and limits, as an append does.
                        try
                        {
                            using (TimelineDocument.EnterRoleKeys(true))
                                stored.Add(TimelineTrack.ReadAt(new DocReader(so, new DocReadContext("timeline", false, new FindingList())), i));
                        }
                        catch (SaveFormatException) { }   // the document's reader reports it
                    }
                }
                if (buildIdx.Count > 0)
                {
                    List<TimelineTrack> converted;
                    using (TimelineDocument.EnterRoleKeys(true))
                        converted = TimelineBuildInput.ParseTracks(buildForm, duration, host, build.Ids, build.Strict, build.Warnings, stored);
                    for (int k = 0; k < buildIdx.Count; k++)
                        tracks[buildIdx[k]] = DocReader.ParseText(converted[k].ToJson(true), "timeline");
                }
            }
            if (t["events"] is JArray events)
            {
                var buildIdx = new List<int>();
                var buildForm = new JArray();
                for (int i = 0; i < events.Count; i++)
                    if (events[i] is JObject o && o.Property("id") == null) { buildIdx.Add(i); buildForm.Add(o.DeepClone()); }
                if (buildIdx.Count > 0)
                {
                    // ParseEvents sorts by time; each element is converted alone so that the document keeps its order.
                    for (int k = 0; k < buildIdx.Count; k++)
                    {
                        var one = TimelineBuildInput.ParseEvents(new JArray(buildForm[k]), duration, build.Ids, build.Strict, build.Warnings);
                        events[buildIdx[k]] = EventJson(one[0]);
                    }
                }
            }
        }

        static JObject EventJson(TimelineEvent e)
        {
            var o = new JObject { ["id"] = e.Id, ["t"] = e.T, ["do"] = e.Do.DeepClone(), ["enabled"] = e.Enabled };
            o["note"] = e.Note == null ? (JToken)JValue.CreateNull() : e.Note;
            return o;
        }
    }
}
