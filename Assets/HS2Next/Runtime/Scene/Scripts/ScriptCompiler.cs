// Scene scripts: the compile of a script's timeline into the scene's Timeline (loading a script). Unity-free.
//   A scene script's timeline is a Timeline document whose tracks' keys and dof keys' focus nodes may be "$role". Compiling replaces every
//   such role by the cast's node id and substitutes every timed command's request (strings "$role" at any depth; "$$" escapes), and keeps
//   the ids of tracks and events, the duration, loop and autoplay. A track whose key names a role that is not cast, or no declared role, is
//   left out; a dof key whose focus names one is left out (a dof track left without keys is left out); a timed command that names one is
//   left out; each is counted. The result is read with the Timeline reader (its repairs and findings) and carries the marker {the script's
//   id, compiled}, compiled = the document's CompiledHash (the first 16 hex of SHA-256 of its canonical JSON without the marker).
using System.Collections.Generic;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Scripts
{
    /// <summary>The result of compiling a script's timeline.</summary>
    public sealed class ScriptCompileResult
    {
        /// <summary>The compiled Timeline (node ids, the marker set when compiled from a scene script).</summary>
        public TimelineDocument Timeline;
        /// <summary>The compiled hash (the marker's <c>compiled</c>).</summary>
        public string Compiled;
        /// <summary>Track keys and focus nodes replaced by node ids.</summary>
        public int KeysReplaced;
        /// <summary>Strings of timed commands' requests replaced by node ids.</summary>
        public int RequestsReplaced;
        /// <summary>Tracks, dof keys and timed commands left out (an uncast or unknown role).</summary>
        public int TracksSkipped, KeysSkipped, EventsSkipped;
        /// <summary>Declared roles left uncast that the timeline names (distinct, in order of first use).</summary>
        public readonly List<string> UncastRoles = new List<string>();
        /// <summary>Strings "$name" naming no declared role (distinct).</summary>
        public readonly List<string> UnknownRoles = new List<string>();
        /// <summary>Track types compiled (distinct, in order of first appearance).</summary>
        public readonly List<string> TrackTypes = new List<string>();
        /// <summary>The Timeline reader's findings (repairs).</summary>
        public readonly FindingList Findings = new FindingList();

        /// <summary>Everything left out.</summary>
        public int Skipped => TracksSkipped + KeysSkipped + EventsSkipped;

        internal void Uncast(string role) { if (role != null && !UncastRoles.Contains(role)) UncastRoles.Add(role); }
        internal void Unknown(string role) { if (role != null && !UnknownRoles.Contains(role)) UnknownRoles.Add(role); }
    }

    public static class ScriptCompiler
    {
        /// <summary>The entry name of findings and errors of a compiled Timeline.</summary>
        public const string Entry = "script.timeline";

        /// <summary>The role names a script declares.</summary>
        public static HashSet<string> Declared(SceneScript s)
        {
            var d = new HashSet<string>(System.StringComparer.Ordinal);
            if (s?.Roles != null) foreach (var r in s.Roles) if (r?.Role != null) d.Add(r.Role);
            return d;
        }

        /// <summary>
        /// Compiles the timeline of <paramref name="script"/> with <paramref name="cast"/> (role → node id; null = the script's own cast) into a
        /// scene Timeline carrying the marker {script id, compiled}. A type error of the document throws <see cref="SaveFormatException"/>.
        /// </summary>
        public static ScriptCompileResult Compile(SceneScript script, IReadOnlyDictionary<string, string> cast = null)
        {
            if (script == null) throw new System.ArgumentNullException(nameof(script));
            if (script.Timeline == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "the script has no timeline", Entry, "");
            var r = Compile(script.Timeline.ToJObject(), Declared(script), cast ?? (IReadOnlyDictionary<string, string>)script.Cast);
            r.Timeline.Script = new TimelineScriptMarker(script.Id, r.Compiled);
            return r;
        }

        /// <summary>
        /// Compiles <paramref name="timeline"/> (a script's timeline in its stored form with "$role" strings) with <paramref name="cast"/> over the
        /// roles <paramref name="declared"/>; the result has no marker (the caller sets it) and <see cref="ScriptCompileResult.Compiled"/> is its hash.
        /// </summary>
        public static ScriptCompileResult Compile(JObject timeline, ICollection<string> declared, IReadOnlyDictionary<string, string> cast)
        {
            if (timeline == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "the script has no timeline", Entry, "");
            var r = new ScriptCompileResult();
            var o = new JObject();
            foreach (var p in timeline.Properties())
            {
                switch (p.Name)
                {
                    case "tracks": o.Add("tracks", CompileTracks(p.Value, declared, cast, r)); break;
                    case "events": o.Add("events", CompileEvents(p.Value, declared, cast, r)); break;
                    case TimelineScriptMarker.MemberName: break;   // never copied: the compile sets its own
                    default: o.Add(p.Name, p.Value.DeepClone()); break;
                }
            }
            r.Timeline = TimelineDocument.FromToken(o, r.Findings, Entry);
            r.Timeline.Script = null;
            r.Compiled = r.Timeline.CompiledHash();
            return r;
        }

        static JToken CompileTracks(JToken tracks, ICollection<string> declared, IReadOnlyDictionary<string, string> cast, ScriptCompileResult r)
        {
            if (!(tracks is JArray a)) return tracks?.DeepClone();
            var outA = new JArray();
            foreach (var t in a)
            {
                if (!(t is JObject to)) { outA.Add(t.DeepClone()); continue; }
                var c = (JObject)to.DeepClone();
                if (c["key"] is JValue kv && kv.Type == JTokenType.String && ScriptSubst.IsRoleRef((string)kv, out _))
                {
                    if (!Resolve((string)kv, declared, cast, r, out string keyNode))
                    {
                        r.TracksSkipped++;
                        continue;
                    }
                    c["key"] = keyNode;
                    r.KeysReplaced++;
                }
                string type = c["type"] is JValue tv && tv.Type == JTokenType.String ? (string)tv : null;
                if (type == TimelineTypes.Dof && c["keys"] is JArray keys)
                {
                    var kept = new JArray();
                    foreach (var k in keys)
                    {
                        var node = FocusNode(k) as JValue;
                        if (node != null && node.Type == JTokenType.String && ScriptSubst.IsRoleRef((string)node, out _))
                        {
                            if (!Resolve((string)node, declared, cast, r, out string id)) { r.KeysSkipped++; continue; }
                            node.Value = id;
                            r.KeysReplaced++;
                        }
                        kept.Add(k);
                    }
                    if (kept.Count == 0 && keys.Count > 0) { r.TracksSkipped++; continue; }
                    c["keys"] = kept;
                }
                if (type != null && !r.TrackTypes.Contains(type)) r.TrackTypes.Add(type);
                outA.Add(c);
            }
            return outA;
        }

        /// <summary>The focus node of a dof key's value ({t, ease, v: {focus: {node}}}), or null for any other key.</summary>
        static JToken FocusNode(JToken key) => key is JObject k && k["v"] is JObject v && v["focus"] is JObject f ? f["node"] : null;

        /// <summary>A role reference resolves to its cast node (false when the role is uncast or undeclared, both counted).</summary>
        static bool Resolve(string s, ICollection<string> declared, IReadOnlyDictionary<string, string> cast, ScriptCompileResult r, out string node)
        {
            node = s;
            if (!ScriptSubst.IsRoleRef(s, out string role)) return true;
            if (declared == null || !declared.Contains(role)) { r.Unknown(role); return false; }
            if (cast == null || !cast.TryGetValue(role, out string id) || string.IsNullOrEmpty(id)) { r.Uncast(role); return false; }
            node = id;
            return true;
        }

        static JToken CompileEvents(JToken events, ICollection<string> declared, IReadOnlyDictionary<string, string> cast, ScriptCompileResult r)
        {
            if (!(events is JArray a)) return events?.DeepClone();
            var outA = new JArray();
            var sr = new ScriptSubstResult();
            foreach (var e in a)
            {
                if (!(e is JObject eo)) { outA.Add(e.DeepClone()); continue; }
                var c = new JObject();
                bool skip = false;
                foreach (var p in eo.Properties())
                {
                    if (p.Name == "do" && p.Value is JObject d)
                    {
                        var sd = ScriptSubst.Request(d, declared, cast, false, sr);
                        foreach (var u in sr.UnknownRoles) r.Unknown(u);
                        foreach (var u in sr.UncastRoles) r.Uncast(u);
                        if (!sr.Complete) { skip = true; break; }
                        r.RequestsReplaced += sr.Replaced;
                        c.Add("do", sd);
                    }
                    else c.Add(p.Name, p.Value.DeepClone());
                }
                if (skip) { r.EventsSkipped++; continue; }
                outA.Add(c);
            }
            return outA;
        }

        // ------------------------------------------------------------ the reverse (script.capture)

        /// <summary>
        /// The Timeline <paramref name="loaded"/> written back as a script's timeline: a track key or a focus node equal to a
        /// cast node id becomes "$role", every string of a timed command's request equal to a cast node id becomes "$role" (another string that
        /// starts with "$" is escaped as "$$", so that compiling gives it back), other values stay; the marker is dropped.
        /// <paramref name="cast"/>: role → node id. Returns the script timeline (role keys on) and the count of values turned into roles.
        /// </summary>
        public static TimelineDocument Capture(TimelineDocument loaded, IReadOnlyDictionary<string, string> cast, out int replaced)
        {
            replaced = 0;
            if (loaded == null) return null;
            var byNode = new Dictionary<string, string>(System.StringComparer.Ordinal);
            if (cast != null) foreach (var kv in cast) if (!string.IsNullOrEmpty(kv.Value) && !byNode.ContainsKey(kv.Value)) byNode[kv.Value] = kv.Key;
            var o = loaded.ToJObject();
            o.Remove(TimelineScriptMarker.MemberName);
            if (o["tracks"] is JArray tracks)
                foreach (var t in tracks)
                {
                    if (!(t is JObject to)) continue;
                    if (to["key"] is JValue kv && kv.Type == JTokenType.String && byNode.TryGetValue((string)kv, out string role)) { to["key"] = ScriptSubst.Ref(role); replaced++; }
                    if (to["keys"] is JArray keys)
                        foreach (var k in keys)
                            if (FocusNode(k) is JValue nv && nv.Type == JTokenType.String && byNode.TryGetValue((string)nv, out string fr)) { nv.Value = ScriptSubst.Ref(fr); replaced++; }
                }
            if (o["events"] is JArray events)
                foreach (var e in events)
                {
                    if (!(e is JObject eo) || !(eo["do"] is JObject d)) continue;
                    eo["do"] = ScriptSubst.Unapply(d, byNode, out int n);
                    replaced += n;
                }
            return TimelineDocument.FromScriptToken(o, new FindingList(), Entry);
        }
    }
}
