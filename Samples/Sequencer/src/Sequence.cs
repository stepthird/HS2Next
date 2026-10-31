// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// The sequence model: timed command events and transform / camera key tracks, its JSON form (data version 1) and its
// validation. The JSON is written canonically (members in the documented order, numbers in round-trip form, compact),
// so parsing the written text and writing it again gives the same bytes.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Stp3.Samples.Sequencer
{
    /// <summary>How a track moves between two keys.</summary>
    public enum Ease
    {
        /// <summary>Positions and numbers blend linearly, rotations along the shortest arc.</summary>
        Linear,
        /// <summary>The earlier key holds until the next key's time.</summary>
        Step,
    }

    public enum TrackKind { Transform, Camera }

    /// <summary>One key of a transform track: a node's parent-local position and Euler rotation (degrees).</summary>
    public sealed class TransformKey
    {
        public double T;
        public Vec3 Pos;
        public Vec3 Rot;
    }

    /// <summary>One key of the camera track: pivot, Euler rotation (degrees), distance and vertical field of view.</summary>
    public sealed class CameraKey
    {
        public double T;
        public Vec3 Target;
        public Vec3 Rot;
        public double Dist;
        public double Fov;
    }

    /// <summary>A transform track (one scene node) or the camera track.</summary>
    public sealed class Track
    {
        public TrackKind Kind;
        /// <summary>The scene node id a transform track drives; null for the camera track.</summary>
        public string Key;
        public Ease Ease;
        /// <summary>Keys of a transform track, ascending in time (empty for the camera track).</summary>
        public readonly List<TransformKey> TransformKeys = new List<TransformKey>();
        /// <summary>Keys of the camera track, ascending in time (empty for a transform track).</summary>
        public readonly List<CameraKey> CameraKeys = new List<CameraKey>();

        public int KeyCount => Kind == TrackKind.Transform ? TransformKeys.Count : CameraKeys.Count;
        public double KeyTime(int i) => Kind == TrackKind.Transform ? TransformKeys[i].T : CameraKeys[i].T;
    }

    /// <summary>A command request sent when playback passes <see cref="T"/>.</summary>
    public sealed class SequenceEvent
    {
        public double T;
        /// <summary>The request object as given (member order and integer numbers kept).</summary>
        public JObject Do;
        /// <summary>The request's command name.</summary>
        public string Cmd;
        /// <summary>Optional free text.</summary>
        public string Note;
        /// <summary>The request as compact JSON text (what is sent).</summary>
        public string RequestJson;
    }

    /// <summary>A sequence that failed validation: <see cref="Path"/> names the member at fault.</summary>
    public sealed class SequenceException : Exception
    {
        public SequenceException(string path, string problem) : base((string.IsNullOrEmpty(path) ? "sequence" : path) + ": " + problem)
        {
            Path = path ?? "";
            Problem = problem;
        }

        public string Path { get; }
        public string Problem { get; }
    }

    /// <summary>
    /// A sequence: duration, loop and autoplay flags, up to 64 tracks (transform tracks, at most one camera track) with
    /// at most 4096 keys in all, and up to 1024 events. Immutable once parsed.
    /// </summary>
    public sealed class Sequence
    {
        public const int DataVersion = 1;
        public const double MaxDuration = 3600;
        public const int MaxTracks = 64;
        public const int MaxKeys = 4096;
        public const int MaxEvents = 1024;
        /// <summary>The command prefix of this plugin: events may not call it (no recursion).</summary>
        public const string OwnPrefix = "seq.";

        static readonly Regex CommandName = new Regex(@"^[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)*$", RegexOptions.CultureInvariant);

        static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            CommentHandling = CommentHandling.Ignore,
            LineInfoHandling = LineInfoHandling.Ignore,
        };

        public double Duration;
        public bool Loop;
        public bool Autoplay;
        public readonly List<Track> Tracks = new List<Track>();
        public readonly List<SequenceEvent> Events = new List<SequenceEvent>();

        /// <summary>The number of keys over all tracks.</summary>
        public int KeyCount
        {
            get
            {
                int n = 0;
                foreach (var tr in Tracks) n += tr.KeyCount;
                return n;
            }
        }

        public int TrackCount(TrackKind kind)
        {
            int n = 0;
            foreach (var tr in Tracks) if (tr.Kind == kind) n++;
            return n;
        }

        // ------------------------------------------------------------------ parsing

        /// <summary>Parses and validates a sequence from JSON text. Throws <see cref="SequenceException"/> with the first problem.</summary>
        public static Sequence Parse(string json)
        {
            if (json == null) throw new SequenceException("", "no sequence");
            JToken root;
            try
            {
                using (var r = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double,
                    MaxDepth = 64,
                })
                {
                    root = JToken.ReadFrom(r, LoadSettings);
                    while (r.Read())
                        if (r.TokenType != JsonToken.Comment) throw new JsonReaderException("unexpected content after the JSON value");
                }
            }
            catch (JsonException e)
            {
                throw new SequenceException("", "not valid JSON (" + e.Message + ")");
            }
            return FromToken(root);
        }

        /// <summary>As <see cref="Parse"/>, without throwing.</summary>
        public static bool TryParse(string json, out Sequence sequence, out string problem)
        {
            try
            {
                sequence = Parse(json);
                problem = null;
                return true;
            }
            catch (SequenceException e)
            {
                sequence = null;
                problem = e.Message;
                return false;
            }
        }

        /// <summary>Validates a parsed JSON value.</summary>
        public static Sequence FromToken(JToken root)
        {
            var o = Obj(root, "");
            Members(o, "", "duration", "loop", "autoplay", "tracks", "events");
            var s = new Sequence();
            s.Duration = Num(Req(o, "duration", ""), "duration");
            if (!(s.Duration > 0) || s.Duration > MaxDuration) throw new SequenceException("duration", "must be > 0 and <= 3600 seconds");
            s.Loop = Bool(o["loop"], "loop", false);
            s.Autoplay = Bool(o["autoplay"], "autoplay", false);

            var tracks = Arr(o["tracks"], "tracks", true);
            if (tracks != null)
            {
                if (tracks.Count > MaxTracks) throw new SequenceException("tracks", "at most 64 tracks");
                var keys = new HashSet<string>(StringComparer.Ordinal);
                int keyTotal = 0, cameras = 0;
                for (int i = 0; i < tracks.Count; i++)
                {
                    var tr = ParseTrack(tracks[i], "tracks[" + i + "]", s.Duration);
                    if (tr.Kind == TrackKind.Camera && ++cameras > 1) throw new SequenceException("tracks[" + i + "]", "at most one camera track");
                    if (tr.Kind == TrackKind.Transform && !keys.Add(tr.Key))
                        throw new SequenceException("tracks[" + i + "].key", "a second transform track for node \"" + tr.Key + "\"");
                    keyTotal += tr.KeyCount;
                    if (keyTotal > MaxKeys) throw new SequenceException("tracks[" + i + "].keys", "at most 4096 keys in all tracks");
                    s.Tracks.Add(tr);
                }
            }

            var events = Arr(o["events"], "events", true);
            if (events != null)
            {
                if (events.Count > MaxEvents) throw new SequenceException("events", "at most 1024 events");
                for (int i = 0; i < events.Count; i++) s.Events.Add(ParseEvent(events[i], "events[" + i + "]", s.Duration));
            }
            return s;
        }

        static Track ParseTrack(JToken token, string path, double duration)
        {
            var o = Obj(token, path);
            string kind = Str(Req(o, "kind", path), path + ".kind");
            var tr = new Track();
            if (kind == "transform")
            {
                tr.Kind = TrackKind.Transform;
                Members(o, path, "kind", "key", "ease", "keys");
                tr.Key = Str(Req(o, "key", path), path + ".key");
                if (tr.Key.Length == 0) throw new SequenceException(path + ".key", "an empty node id");
            }
            else if (kind == "camera")
            {
                tr.Kind = TrackKind.Camera;
                Members(o, path, "kind", "ease", "keys");
            }
            else throw new SequenceException(path + ".kind", "expected \"transform\" or \"camera\"");

            var ease = o["ease"];
            if (ease == null || ease.Type == JTokenType.Null) tr.Ease = Ease.Linear;
            else
            {
                string e = Str(ease, path + ".ease");
                if (e == "linear") tr.Ease = Ease.Linear;
                else if (e == "step") tr.Ease = Ease.Step;
                else throw new SequenceException(path + ".ease", "expected \"linear\" or \"step\"");
            }

            var keys = Arr(Req(o, "keys", path), path + ".keys", false);
            if (keys.Count == 0) throw new SequenceException(path + ".keys", "at least one key");
            if (keys.Count > MaxKeys) throw new SequenceException(path + ".keys", "at most 4096 keys in all tracks");
            double last = double.NegativeInfinity;
            for (int i = 0; i < keys.Count; i++)
            {
                string kp = path + ".keys[" + i + "]";
                var k = Obj(keys[i], kp);
                double t = Num(Req(k, "t", kp), kp + ".t");
                if (t < 0 || t > duration) throw new SequenceException(kp + ".t", "must be within 0 and the duration");
                if (!(t > last)) throw new SequenceException(kp + ".t", "keys must be ascending in t");
                last = t;
                if (tr.Kind == TrackKind.Transform)
                {
                    Members(k, kp, "t", "pos", "rot");
                    tr.TransformKeys.Add(new TransformKey
                    {
                        T = t,
                        Pos = V3(Req(k, "pos", kp), kp + ".pos"),
                        Rot = V3(Req(k, "rot", kp), kp + ".rot"),
                    });
                }
                else
                {
                    Members(k, kp, "t", "target", "rot", "dist", "fov");
                    var ck = new CameraKey
                    {
                        T = t,
                        Target = V3(Req(k, "target", kp), kp + ".target"),
                        Rot = V3(Req(k, "rot", kp), kp + ".rot"),
                        Dist = Num(Req(k, "dist", kp), kp + ".dist"),
                        Fov = Num(Req(k, "fov", kp), kp + ".fov"),
                    };
                    if (ck.Dist < 0) throw new SequenceException(kp + ".dist", "must be >= 0");
                    if (!(ck.Fov > 0) || !(ck.Fov < 180)) throw new SequenceException(kp + ".fov", "must be > 0 and < 180 degrees");
                    tr.CameraKeys.Add(ck);
                }
            }
            return tr;
        }

        static SequenceEvent ParseEvent(JToken token, string path, double duration)
        {
            var o = Obj(token, path);
            Members(o, path, "t", "do", "note");
            var ev = new SequenceEvent();
            ev.T = Num(Req(o, "t", path), path + ".t");
            if (ev.T < 0 || ev.T > duration) throw new SequenceException(path + ".t", "must be within 0 and the duration");
            ev.Do = Obj(Req(o, "do", path), path + ".do");
            ev.Cmd = CheckRequest(ev.Do, path + ".do");
            if (ev.Cmd == "batch")
            {
                var cmds = ev.Do["cmds"] as JArray;
                if (cmds == null) throw new SequenceException(path + ".do.cmds", "a batch needs an array of requests");
                for (int i = 0; i < cmds.Count; i++)
                {
                    var inner = Obj(cmds[i], path + ".do.cmds[" + i + "]");
                    if (CheckRequest(inner, path + ".do.cmds[" + i + "]") == "batch")
                        throw new SequenceException(path + ".do.cmds[" + i + "].cmd", "a batch cannot contain a batch");
                }
            }
            var note = o["note"];
            if (note != null && note.Type != JTokenType.Null) ev.Note = Str(note, path + ".note");
            CheckFinite(ev.Do, path + ".do");
            ev.RequestJson = ev.Do.ToString(Formatting.None);
            return ev;
        }

        /// <summary>The request's command name after the checks every event request passes.</summary>
        static string CheckRequest(JObject request, string path)
        {
            string cmd = Str(Req(request, "cmd", path), path + ".cmd");
            if (!IsCommandName(cmd)) throw new SequenceException(path + ".cmd", "not a command name");
            if (cmd.StartsWith(OwnPrefix, StringComparison.Ordinal)) throw new SequenceException(path + ".cmd", "events cannot call the sequencer's own commands");
            if (request["id"] != null) throw new SequenceException(path + ".id", "event requests carry no id");
            return cmd;
        }

        /// <summary>True for a valid command name: lower-case-first parts of letters and digits joined by dots, at most 64 characters.</summary>
        public static bool IsCommandName(string name) => name != null && name.Length <= 64 && CommandName.IsMatch(name);

        // ------------------------------------------------------------------ writing

        /// <summary>The canonical compact JSON text.</summary>
        public string ToJson()
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture);
            using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None })
            {
                w.WriteStartObject();
                w.WritePropertyName("duration"); w.WriteValue(Duration);
                w.WritePropertyName("loop"); w.WriteValue(Loop);
                w.WritePropertyName("autoplay"); w.WriteValue(Autoplay);
                w.WritePropertyName("tracks");
                w.WriteStartArray();
                foreach (var tr in Tracks)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("kind"); w.WriteValue(tr.Kind == TrackKind.Transform ? "transform" : "camera");
                    if (tr.Kind == TrackKind.Transform) { w.WritePropertyName("key"); w.WriteValue(tr.Key); }
                    w.WritePropertyName("ease"); w.WriteValue(tr.Ease == Ease.Step ? "step" : "linear");
                    w.WritePropertyName("keys");
                    w.WriteStartArray();
                    if (tr.Kind == TrackKind.Transform)
                        foreach (var k in tr.TransformKeys)
                        {
                            w.WriteStartObject();
                            w.WritePropertyName("t"); w.WriteValue(k.T);
                            w.WritePropertyName("pos"); WriteV3(w, k.Pos);
                            w.WritePropertyName("rot"); WriteV3(w, k.Rot);
                            w.WriteEndObject();
                        }
                    else
                        foreach (var k in tr.CameraKeys)
                        {
                            w.WriteStartObject();
                            w.WritePropertyName("t"); w.WriteValue(k.T);
                            w.WritePropertyName("target"); WriteV3(w, k.Target);
                            w.WritePropertyName("rot"); WriteV3(w, k.Rot);
                            w.WritePropertyName("dist"); w.WriteValue(k.Dist);
                            w.WritePropertyName("fov"); w.WriteValue(k.Fov);
                            w.WriteEndObject();
                        }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WritePropertyName("events");
                w.WriteStartArray();
                foreach (var ev in Events)
                {
                    w.WriteStartObject();
                    w.WritePropertyName("t"); w.WriteValue(ev.T);
                    w.WritePropertyName("do"); ev.Do.WriteTo(w);
                    if (ev.Note != null) { w.WritePropertyName("note"); w.WriteValue(ev.Note); }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            return sw.ToString();
        }

        static void WriteV3(JsonWriter w, Vec3 v)
        {
            w.WriteStartArray();
            w.WriteValue(v.X);
            w.WriteValue(v.Y);
            w.WriteValue(v.Z);
            w.WriteEndArray();
        }

        /// <summary>A number in the host's round-trip text (".0" on integral values), as written in requests and the JSON form.</summary>
        public static string NumberText(double d) => JsonConvert.ToString(Clean(d));

        /// <summary>Negative zero becomes zero (it does not survive a JSON round trip on every runtime).</summary>
        static double Clean(double d) => d == 0 ? 0.0 : d;

        // ------------------------------------------------------------------ helpers

        static JObject Obj(JToken t, string path)
        {
            if (t is JObject o) return o;
            throw new SequenceException(path, "expected an object");
        }

        static JArray Arr(JToken t, string path, bool optional)
        {
            if (t == null || t.Type == JTokenType.Null)
            {
                if (optional) return null;
                throw new SequenceException(path, "missing");
            }
            if (t is JArray a) return a;
            throw new SequenceException(path, "expected an array");
        }

        static JToken Req(JObject o, string name, string path)
        {
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null) throw new SequenceException(Join(path, name), "missing");
            return t;
        }

        static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : path + "." + name;

        /// <summary>Refuses members outside <paramref name="allowed"/> (a misspelt member would otherwise be ignored).</summary>
        static void Members(JObject o, string path, params string[] allowed)
        {
            foreach (var p in o.Properties())
                if (Array.IndexOf(allowed, p.Name) < 0)
                    throw new SequenceException(Join(path, p.Name), "unknown member (expected " + string.Join(", ", allowed) + ")");
        }

        static double Num(JToken t, string path)
        {
            if (t.Type != JTokenType.Integer && t.Type != JTokenType.Float) throw new SequenceException(path, "expected a number");
            double d;
            try { d = Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture); }
            catch (Exception) { throw new SequenceException(path, "expected a number"); }
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new SequenceException(path, "expected a finite number");
            return Clean(d);
        }

        static bool Bool(JToken t, string path, bool fallback)
        {
            if (t == null || t.Type == JTokenType.Null) return fallback;
            if (t.Type != JTokenType.Boolean) throw new SequenceException(path, "expected true or false");
            return (bool)t;
        }

        static string Str(JToken t, string path)
        {
            if (t.Type != JTokenType.String) throw new SequenceException(path, "expected a string");
            return (string)t;
        }

        static Vec3 V3(JToken t, string path)
        {
            if (!(t is JArray a) || a.Count != 3) throw new SequenceException(path, "expected [x, y, z]");
            return new Vec3(Num(a[0], path + "[0]"), Num(a[1], path + "[1]"), Num(a[2], path + "[2]"));
        }

        /// <summary>Refuses non-finite numbers anywhere in an event request (the reader accepts a bare NaN literal).</summary>
        static void CheckFinite(JToken t, string path)
        {
            switch (t.Type)
            {
                case JTokenType.Float:
                    double d = Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture);
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new SequenceException(path, "expected a finite number");
                    break;
                case JTokenType.Object:
                    foreach (var p in ((JObject)t).Properties()) CheckFinite(p.Value, path + "." + p.Name);
                    break;
                case JTokenType.Array:
                    var a = (JArray)t;
                    for (int i = 0; i < a.Count; i++) CheckFinite(a[i], path + "[" + i + "]");
                    break;
            }
        }
    }
}
