// The Timeline of a scene: the scene.json member `timeline` (written after `scene`, before `ext`, only when the scene has
// one). Duration, loop, autoplay, tracks of keys and timed commands (events). Unity-free save-layer classes, as the scene
// settings are; the runtime model holds one instance (SceneModel.Timeline, null = the scene has no Timeline) and only the
// Timeline edit command replaces it or one of its tracks.
//
// The reader binds strictly (a missing required member or a wrong type fails the load as for every core member) and then
// repairs what a hand-made or damaged file may hold, reporting each repair: keys out of order are sorted (stable), keys at
// one time keep the first, a key whose value does not fit its track's type is dropped, an unknown track type is kept
// verbatim and inactive, an unknown ease is kept (evaluated as the default), an event that may not run is kept and never
// fired, a second track of one channel is kept inactive, a track without an id gets a derived one, and the excess over the
// limits is dropped. The writer emits every member in schema order, keys and events sorted by time (stable).
//
// A Timeline loaded from a scene script carries the marker `script` {id, compiled} after `events` (written only when set). The Timeline
// of a scene script itself (RoleKeys) may name roles ("$role") where a node id belongs: a track's `key` and a depth-of-field focus's
// `node`; a scene's own Timeline never does.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>The value form of a track's keys (<c>v</c>), by track type.</summary>
    public enum TimelineValueShape
    {
        /// <summary>An unknown track type: the track is kept verbatim and has no keys of its own.</summary>
        Unknown = 0,
        /// <summary>f32 (animeSpeed, animeTime, eyesOpen, mouthOpen): <see cref="TimelineKey.Number"/>.</summary>
        Number = 1,
        /// <summary>int, a pattern id (eyes, mouth): <see cref="TimelineKey.Integer"/>.</summary>
        Integer = 2,
        /// <summary>v3 (pos, scale): <see cref="TimelineKey.Vector"/>.</summary>
        Vector3 = 3,
        /// <summary>euler, degrees (rot, with or without a bone): <see cref="TimelineKey.Vector"/>.</summary>
        Euler = 4,
        /// <summary>col, stored values (color): <see cref="TimelineKey.Color"/>.</summary>
        Color = 5,
        /// <summary>animeRef (anime): <see cref="TimelineKey.Anime"/>.</summary>
        Anime = 6,
        /// <summary>The camera object (camera): <see cref="TimelineKey.Camera"/>.</summary>
        Camera = 7,
        /// <summary>The depth-of-field object (dof): <see cref="TimelineKey.Dof"/>.</summary>
        Dof = 8,
    }

    /// <summary>Track types (the <c>type</c> member).</summary>
    public static class TimelineTypes
    {
        public const string Camera = "camera";
        public const string Dof = "dof";
        public const string Pos = "pos";
        public const string Rot = "rot";
        public const string Scale = "scale";
        public const string Anime = "anime";
        public const string AnimeSpeed = "animeSpeed";
        public const string AnimeTime = "animeTime";
        public const string Eyes = "eyes";
        public const string Mouth = "mouth";
        public const string EyesOpen = "eyesOpen";
        public const string MouthOpen = "mouthOpen";
        public const string Color = "color";

        static readonly string[] all = { Camera, Dof, Pos, Rot, Scale, Anime, AnimeSpeed, AnimeTime, Eyes, Mouth, EyesOpen, MouthOpen, Color };

        /// <summary>Every known type, in the order of the schema table.</summary>
        public static IReadOnlyList<string> All => all;

        /// <summary>True for a type this reader knows (ordinal).</summary>
        public static bool IsKnown(string type) => ShapeOf(type) != TimelineValueShape.Unknown;

        /// <summary>The value form of a type's keys; <see cref="TimelineValueShape.Unknown"/> for an unknown type or null.</summary>
        public static TimelineValueShape ShapeOf(string type)
        {
            switch (type)
            {
                case Camera: return TimelineValueShape.Camera;
                case Dof: return TimelineValueShape.Dof;
                case Pos:
                case Scale: return TimelineValueShape.Vector3;
                case Rot: return TimelineValueShape.Euler;
                case Anime: return TimelineValueShape.Anime;
                case AnimeSpeed:
                case AnimeTime:
                case EyesOpen:
                case MouthOpen: return TimelineValueShape.Number;
                case Eyes:
                case Mouth: return TimelineValueShape.Integer;
                case Color: return TimelineValueShape.Color;
                default: return TimelineValueShape.Unknown;
            }
        }

        /// <summary>True for the scene-wide types (camera, dof): their tracks name no node.</summary>
        public static bool IsSceneWide(string type) => type == Camera || type == Dof;

        /// <summary>True for the types whose values do not interpolate (anime, eyes, mouth): they evaluate as step whatever
        /// a key's ease says.</summary>
        public static bool IsStepOnly(string type) => type == Anime || type == Eyes || type == Mouth;
    }

    /// <summary>Key eases (the <c>ease</c> member): the shape of the segment from a key to the next one.</summary>
    public static class TimelineEases
    {
        /// <summary>The left value holds until the next key.</summary>
        public const string Step = "step";
        public const string Linear = "linear";
        /// <summary>Smooth start and end (the default).</summary>
        public const string InOut = "inout";
        /// <summary>Slow start.</summary>
        public const string In = "in";
        /// <summary>Slow end.</summary>
        public const string Out = "out";
        /// <summary>The ease of a key that names none (and the evaluation of an unknown ease string).</summary>
        public const string Default = InOut;

        static readonly string[] all = { Step, Linear, InOut, In, Out };

        public static IReadOnlyList<string> All => all;

        internal static ICollection<string> Known => all;

        public static bool IsKnown(string ease) => Array.IndexOf(all, ease) >= 0;
    }

    /// <summary>The limits of a Timeline. A command that would exceed one is refused; a file over one loads with the excess
    /// dropped (finding <c>timeline.limit</c>).</summary>
    public static class TimelineLimits
    {
        /// <summary>Seconds; a duration is in (0, MaxDuration].</summary>
        public const float MaxDuration = 3600f;
        public const int MaxTracks = 512;
        /// <summary>Keys of every track together.</summary>
        public const int MaxKeys = 100000;
        public const int MaxEvents = 4096;
        /// <summary>UTF-8 bytes of the canonical JSON text of one event's <c>do</c> object.</summary>
        public const int MaxDoBytes = 64 * 1024;
        /// <summary>Characters of a track's label or an event's note.</summary>
        public const int MaxText = 256;
        /// <summary>The duration a reader gives a Timeline whose stored duration is not above 0 (the build default).</summary>
        public const float RepairDuration = 10f;
    }

    /// <summary>Finding codes of the Timeline reader.</summary>
    public static class TimelineFindings
    {
        /// <summary>Info: a track without an id got a derived one (or a repeated id a new derived one).</summary>
        public const string TrackId = "timeline.trackId";
        /// <summary>Info: a track's keys were out of order and are sorted (stable).</summary>
        public const string KeyOrder = "timeline.keyOrder";
        /// <summary>Warning: keys at one time; the first in file order is kept.</summary>
        public const string DuplicateKey = "timeline.duplicateKey";
        /// <summary>Info: keys or events outside [0, duration], kept as stored.</summary>
        public const string Outside = "timeline.outside";
        /// <summary>Warning: an unknown track type; the track is kept verbatim and inactive.</summary>
        public const string UnknownType = "timeline.unknownType";
        /// <summary>Warning: a key whose value does not fit its track's type is dropped.</summary>
        public const string BadKey = "timeline.badKey";
        /// <summary>Info: a track naming a node that is not in the scene; kept, inactive.</summary>
        public const string UnknownNode = "timeline.unknownNode";
        /// <summary>Warning: an event that may not run (no command, an id member, a command that edits the Timeline); kept, never fired.</summary>
        public const string BadEvent = "timeline.badEvent";
        /// <summary>Warning: a second track of one channel; kept, inactive (the first in file order is active).</summary>
        public const string DuplicateTrack = "timeline.duplicateTrack";
        /// <summary>Warning: the excess over a limit is dropped (or a duration out of range repaired, a text shortened).</summary>
        public const string Limit = "timeline.limit";
    }

    /// <summary>
    /// The Timeline of a scene (the scene.json member <c>timeline</c>). Members: <see cref="Duration"/> (required),
    /// <see cref="Loop"/>, <see cref="Autoplay"/>, <see cref="Tracks"/> in display order, <see cref="Events"/> sorted by time.
    /// </summary>
    public sealed class TimelineDocument : DocObject
    {
        /// <summary>The member name in scene.json.</summary>
        public const string MemberName = "timeline";

        // Role keys: while above 0 on this thread, the reader and the writer take "$role" at a track's key and a focus node (a script's
        // Timeline is read and written inside such a scope; a scene's Timeline never is).
        [ThreadStatic] static int roleKeyDepth;

        /// <summary>True while a script's Timeline is being read or written on this thread.</summary>
        internal static bool RoleKeysActive => roleKeyDepth > 0;

        /// <summary>A scope in which role keys are taken (<paramref name="on"/>) or refused; dispose to leave it.</summary>
        internal static RoleKeyScope EnterRoleKeys(bool on) => new RoleKeyScope(on);

        internal struct RoleKeyScope : IDisposable
        {
            readonly int before;
            // On: role keys are taken until disposed; off: they are refused until disposed (a scene's Timeline inside a script's scope).
            public RoleKeyScope(bool on)
            {
                before = roleKeyDepth;
                roleKeyDepth = on ? before + 1 : 0;
            }
            public void Dispose() => roleKeyDepth = before;
        }

        /// <summary>Seconds, in (0, <see cref="TimelineLimits.MaxDuration"/>]. Required in a file.</summary>
        public float Duration = TimelineLimits.RepairDuration;
        /// <summary>True: playback wraps at the duration; false: it ends there.</summary>
        public bool Loop = true;
        /// <summary>Play from 0 after the scene is loaded.</summary>
        public bool Autoplay;
        /// <summary>Tracks in display order (no null element on write).</summary>
        public List<TimelineTrack> Tracks = new List<TimelineTrack>();
        /// <summary>Timed commands, sorted by time (stable) when read and written.</summary>
        public List<TimelineEvent> Events = new List<TimelineEvent>();
        /// <summary>The scene script this Timeline was loaded from (member <c>script</c>, after <c>events</c>); null = none, not written.</summary>
        public TimelineScriptMarker Script;
        /// <summary>
        /// True for the Timeline of a scene script: a track's key and a focus node may be "$role" (<see cref="ScriptText.RoleOf"/>) as well as a
        /// node id. Not written; a script's reader sets it. A scene's Timeline has it false and refuses role keys.
        /// </summary>
        public bool RoleKeys;

        /// <summary>A new, empty Timeline of a scene script (<see cref="RoleKeys"/> on, the build defaults).</summary>
        public static TimelineDocument NewScriptTimeline() => new TimelineDocument { RoleKeys = true };

        // ---------- reading ----------

        /// <summary>Reads a Timeline object with every repair and finding; a type error throws as for any core member.</summary>
        public static TimelineDocument Read(DocReader r)
        {
            var d = new TimelineDocument();
            using (EnterRoleKeys(false)) d.ReadFrom(r);
            return d;
        }

        /// <summary>
        /// Reads a Timeline from JSON text (one object) with the reader rules and repairs; findings go to
        /// <paramref name="findings"/>. A syntax error, a type error or a missing required member throws
        /// <see cref="SaveFormatException"/> (BadDocument; NonFinite for NaN / Infinity).
        /// </summary>
        public static TimelineDocument Parse(string json, FindingList findings = null, string entry = MemberName)
        {
            var token = DocReader.ParseText(json ?? "", entry);
            return FromToken(token, findings, entry);
        }

        /// <summary>As <see cref="Parse"/> for a parsed token (an object; anything else throws BadDocument). The token is not
        /// changed; JSON paths in findings and errors are the token's own paths.</summary>
        public static TimelineDocument FromToken(JToken token, FindingList findings = null, string entry = MemberName, bool lenient = false)
        {
            if (!(token is JObject o))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "a Timeline is an object, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            return Read(new DocReader(o, new DocReadContext(entry, lenient, findings ?? new FindingList())));
        }

        /// <summary>As <see cref="FromToken"/> for the Timeline of a scene script: "$role" is taken at a track's key and a focus node
        /// (<see cref="RoleKeys"/> on).</summary>
        public static TimelineDocument FromScriptToken(JToken token, FindingList findings = null, string entry = MemberName)
        {
            if (!(token is JObject o))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "a Timeline is an object, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            return ReadScript(new DocReader(o, new DocReadContext(entry, false, findings ?? new FindingList())));
        }

        /// <summary>Reads the Timeline object of a scene script (<see cref="RoleKeys"/> on) with every repair and finding.</summary>
        internal static TimelineDocument ReadScript(DocReader r)
        {
            var d = new TimelineDocument { RoleKeys = true };
            using (EnterRoleKeys(true)) d.ReadFrom(r);
            return d;
        }

        internal override void ReadMembers(DocReader r)
        {
            Duration = r.F32("duration");
            Loop = r.Bool("loop", true);
            Autoplay = r.Bool("autoplay", false);
            var trackReaders = r.ObjArray("tracks");
            var eventReaders = r.ObjArray("events");

            if (!(Duration > 0f))
            {
                r.Report(FindingSeverity.Warning, TimelineFindings.Limit, "duration", "duration " + F(Duration) + " s is not above 0; " + F(TimelineLimits.RepairDuration) + " s used");
                Duration = TimelineLimits.RepairDuration;
            }
            else if (Duration > TimelineLimits.MaxDuration)
            {
                r.Report(FindingSeverity.Warning, TimelineFindings.Limit, "duration", "duration " + F(Duration) + " s is over " + F(TimelineLimits.MaxDuration) + " s; shortened");
                Duration = TimelineLimits.MaxDuration;
            }

            // Tracks: the first MaxTracks; ids derived where missing or repeated.
            Tracks = new List<TimelineTrack>(Math.Min(trackReaders.Count, TimelineLimits.MaxTracks));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < trackReaders.Count; i++)
            {
                if (i >= TimelineLimits.MaxTracks)
                {
                    r.Report(FindingSeverity.Warning, TimelineFindings.Limit, "tracks", (trackReaders.Count - i) + " tracks over the limit of " + TimelineLimits.MaxTracks + " dropped");
                    break;
                }
                var t = TimelineTrack.ReadAt(trackReaders[i], i);
                if (!ids.Add(t.Id))
                {
                    string old = t.Id;
                    t.Id = JsonText.DerivedId(old + "/dup/" + i);
                    for (int n = 1; !ids.Add(t.Id); n++) t.Id = JsonText.DerivedId(old + "/dup/" + i + "/" + n);
                    trackReaders[i].Report(FindingSeverity.Info, TimelineFindings.TrackId, "id", "track id " + old + " repeats an earlier track's; derived id " + t.Id + " used");
                }
                Tracks.Add(t);
            }

            // Keys over the limit (every track together): the excess at the end of the track list is dropped.
            int keys = 0;
            for (int i = 0; i < Tracks.Count; i++)
            {
                var t = Tracks[i];
                int room = TimelineLimits.MaxKeys - keys;
                if (t.Keys.Count > room)
                {
                    int drop = t.Keys.Count - Math.Max(0, room);
                    t.Keys.RemoveRange(Math.Max(0, room), drop);
                    trackReaders[i].Report(FindingSeverity.Warning, TimelineFindings.Limit, "keys", drop + " keys over the limit of " + TimelineLimits.MaxKeys + " keys in all dropped");
                }
                keys += t.Keys.Count;
            }

            // A second track of one channel: kept, inactive.
            var channels = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < Tracks.Count; i++)
            {
                string ch = Tracks[i].ChannelKey;
                if (ch == null) continue;
                if (channels.TryGetValue(ch, out int first))
                    trackReaders[i].Report(FindingSeverity.Warning, TimelineFindings.DuplicateTrack, null,
                        "track " + i + " drives the channel of track " + first + " (" + Tracks[i].Describe() + "); kept, inactive");
                else channels[ch] = i;
            }

            // Keys outside [0, duration]: kept (a key past the duration is reached only by the after-last rule).
            for (int i = 0; i < Tracks.Count; i++)
            {
                int outside = 0;
                foreach (var k in Tracks[i].Keys) if (k.T < 0f || k.T > Duration) outside++;
                if (outside > 0)
                    trackReaders[i].Report(FindingSeverity.Info, TimelineFindings.Outside, "keys", outside + " key(s) outside [0, " + F(Duration) + "] s kept as stored");
            }

            // Events: the first MaxEvents in file order, then sorted by time (stable).
            Events = new List<TimelineEvent>(Math.Min(eventReaders.Count, TimelineLimits.MaxEvents));
            for (int i = 0; i < eventReaders.Count; i++)
            {
                if (Events.Count >= TimelineLimits.MaxEvents)
                {
                    r.Report(FindingSeverity.Warning, TimelineFindings.Limit, "events", (eventReaders.Count - i) + " events over the limit of " + TimelineLimits.MaxEvents + " dropped");
                    break;
                }
                var e = TimelineEvent.ReadAt(eventReaders[i]);
                if (e == null) continue;
                if (e.T < 0f || e.T > Duration)
                    eventReaders[i].Report(FindingSeverity.Info, TimelineFindings.Outside, "t", "event time " + F(e.T) + " s outside [0, " + F(Duration) + "] s kept as stored");
                Events.Add(e);
            }
            SortStable(Events, e => e.T);
            var marker = r.ObjN(TimelineScriptMarker.MemberName);
            Script = marker == null ? null : ReadNew<TimelineScriptMarker>(marker);
        }

        // ---------- writing ----------

        /// <summary>Writes the Timeline as one object (the caller names the member or finishes the document).</summary>
        public void Write(CanonicalJsonWriter w) => WriteTo(w);

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            using (EnterRoleKeys(RoleKeys)) WriteKnown(w);
        }

        void WriteKnown(CanonicalJsonWriter w)
        {
            w.F32("duration", Duration);
            w.Bool("loop", Loop);
            w.Bool("autoplay", Autoplay);
            w.Name("tracks");
            string at = w.Path;
            w.StartArray();
            if (Tracks != null)
                for (int i = 0; i < Tracks.Count; i++)
                {
                    if (Tracks[i] == null) throw NullElement(w, at, i);
                    Tracks[i].WriteTo(w);
                }
            w.EndArray();
            w.Name("events");
            at = w.Path;
            var events = new List<TimelineEvent>(Events ?? new List<TimelineEvent>());
            for (int i = 0; i < events.Count; i++) if (events[i] == null) throw NullElement(w, at, i);
            SortStable(events, e => e.T);
            w.StartArray();
            foreach (var e in events) e.WriteTo(w);
            w.EndArray();
            if (Script != null && !omitScript) Script.WriteTo(w, TimelineScriptMarker.MemberName);
        }

        bool omitScript;

        /// <summary>
        /// The hash a loaded script's marker holds as <c>compiled</c>: the first 16 hex digits of the SHA-256 of the canonical JSON text (UTF-8, no
        /// trailing LF) of this document without the member <c>script</c>. Equal documents give equal hashes; the marker itself does not count.
        /// </summary>
        public string CompiledHash()
        {
            var c = (TimelineDocument)MemberwiseClone();
            c.omitScript = true;
            var b = CanonicalJsonWriter.ToDocumentBytes(MemberName, c.WriteTo);
            return ScriptText.Hash16(Encoding.UTF8.GetString(b, 0, b.Length - 1));
        }

        /// <summary>True when the Timeline carries a script marker and was edited since it was loaded (its <see cref="CompiledHash"/> differs from
        /// the marker's <c>compiled</c>).</summary>
        public bool IsEditedSinceLoad => Script != null && CompiledHash() != Script.Compiled;

        /// <summary>The canonical JSON bytes of the Timeline object (UTF-8, trailing LF), as scene.json writes it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(MemberName, WriteTo);

        /// <summary>The canonical JSON text of the Timeline object (no trailing LF).</summary>
        public string ToJson()
        {
            var b = ToBytes();
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }

        /// <summary>The Timeline as a JSON object (numbers as the canonical text reads back).</summary>
        public JObject ToJObject() => DocReader.Parse(ToBytes(), MemberName);

        /// <summary>A deep copy (a canonical write → read round trip: unknown members kept, the marker and <see cref="RoleKeys"/> kept, nothing shared).</summary>
        public TimelineDocument Clone()
        {
            var r = DocReader.ForDocument(ToBytes(), new DocReadContext(MemberName, false, new FindingList()));
            return RoleKeys ? ReadScript(r) : Read(r);
        }

        // ---------- queries ----------

        /// <summary>Keys of every track together.</summary>
        public int KeyCount
        {
            get
            {
                int n = 0;
                if (Tracks != null) foreach (var t in Tracks) n += t?.Keys?.Count ?? 0;
                return n;
            }
        }

        /// <summary>The index of the track with this id, or −1.</summary>
        public int IndexOfTrack(string id)
        {
            if (id == null || Tracks == null) return -1;
            for (int i = 0; i < Tracks.Count; i++) if (Tracks[i] != null && Tracks[i].Id == id) return i;
            return -1;
        }

        /// <summary>The track with this id, or null.</summary>
        public TimelineTrack FindTrack(string id)
        {
            int i = IndexOfTrack(id);
            return i < 0 ? null : Tracks[i];
        }

        /// <summary>The index of the track of channel <paramref name="channelKey"/> (<see cref="TimelineTrack.ChannelKey"/>)
        /// that drives it (the first one), or −1.</summary>
        public int IndexOfChannel(string channelKey)
        {
            if (channelKey == null || Tracks == null) return -1;
            for (int i = 0; i < Tracks.Count; i++) if (Tracks[i] != null && Tracks[i].ChannelKey == channelKey) return i;
            return -1;
        }

        /// <summary>True when track <paramref name="index"/> is a second (or later) track of a channel an earlier track
        /// drives: it is kept but inactive. False for an unknown type (always inactive anyway).</summary>
        public bool IsShadowed(int index)
        {
            if (Tracks == null || index < 0 || index >= Tracks.Count || Tracks[index] == null) return false;
            string ch = Tracks[index].ChannelKey;
            if (ch == null) return false;
            for (int i = 0; i < index; i++) if (Tracks[i] != null && Tracks[i].ChannelKey == ch) return true;
            return false;
        }

        /// <summary>Sorts every known track's keys and the events by time (stable). Commands call it before they validate.</summary>
        public void Normalize()
        {
            if (Tracks != null) foreach (var t in Tracks) t?.SortKeys();
            if (Events != null) SortStable(Events, e => e?.T ?? 0f);
        }

        /// <summary>
        /// The first rule this Timeline breaks as the result of an edit, or null: the duration in (0, 3600] s; at most 512 tracks,
        /// 100,000 keys, 4,096 events; track ids valid and distinct; every known track's own rules
        /// (<see cref="TimelineTrack.Problem"/>); event ids valid, a <c>do</c> object within 64 KiB, notes within 256 characters;
        /// and no more tracks on one channel than <paramref name="before"/> had (an edit never adds a second track to a
        /// channel; a file's duplicate kept by the reader stays editable). Key and event times against the duration and the
        /// rules of an event's command are the editing commands' own checks.
        /// </summary>
        public string Problem(TimelineDocument before = null)
        {
            if (!(Duration > 0f) || Duration > TimelineLimits.MaxDuration) return "duration " + F(Duration) + " s is outside (0, " + F(TimelineLimits.MaxDuration) + "] s";
            if (Tracks == null) return "tracks is null";
            if (Events == null) return "events is null";
            if (Tracks.Count > TimelineLimits.MaxTracks) return "more than " + TimelineLimits.MaxTracks + " tracks";
            if (KeyCountSafe() > TimelineLimits.MaxKeys) return "more than " + TimelineLimits.MaxKeys + " keys in all";
            if (Events.Count > TimelineLimits.MaxEvents) return "more than " + TimelineLimits.MaxEvents + " events";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < Tracks.Count; i++)
            {
                var t = Tracks[i];
                if (t == null) return "tracks[" + i + "] is null";
                if (!JsonText.IsId(t.Id)) return "tracks[" + i + "] has no valid id";
                if (!ids.Add(t.Id)) return "tracks[" + i + "] repeats the id " + t.Id;
                string p = t.Problem(RoleKeys);
                if (p != null) return "tracks[" + i + "]: " + p;
                string ch = t.ChannelKey;
                if (ch != null) counts[ch] = counts.TryGetValue(ch, out int c) ? c + 1 : 1;
            }
            foreach (var kv in counts)
            {
                if (kv.Value < 2) continue;
                int had = 0;
                if (before?.Tracks != null) foreach (var t in before.Tracks) if (t != null && t.ChannelKey == kv.Key) had++;
                if (kv.Value > had) return "the channel has a track (" + Tracks[IndexOfChannel(kv.Key)].Describe() + ")";
            }
            ids.Clear();
            for (int i = 0; i < Events.Count; i++)
            {
                var e = Events[i];
                if (e == null) return "events[" + i + "] is null";
                string p = e.Problem();
                if (p != null) return "events[" + i + "]: " + p;
            }
            if (Script != null)
            {
                string mp = Script.Problem();
                if (mp != null) return "script: " + mp;
            }
            return null;
        }

        int KeyCountSafe()
        {
            int n = 0;
            foreach (var t in Tracks) n += t?.Keys?.Count ?? 0;
            return n;
        }

        /// <summary>
        /// Reports <c>timeline.unknownNode</c> (info) for every known track whose <c>key</c> names a node
        /// <paramref name="exists"/> does not know (the track is kept and stays inactive until the node exists).
        /// <paramref name="basePath"/> is the Timeline's JSON path ("timeline" in scene.json).
        /// </summary>
        public void ReportUnknownNodes(Func<string, bool> exists, DocReadContext ctx, string basePath = MemberName)
        {
            if (exists == null || ctx == null || Tracks == null) return;
            for (int i = 0; i < Tracks.Count; i++)
            {
                var t = Tracks[i];
                if (t == null || !t.IsKnownType || t.Key == null || exists(t.Key)) continue;
                ctx.Findings.Info(TimelineFindings.UnknownNode, ctx.Where(basePath + ".tracks[" + i + "].key"),
                    "track " + i + " (" + t.Describe() + ") names node " + t.Key + ", which is not in the scene; kept, inactive");
            }
        }

        // ---------- helpers ----------

        internal static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>A stable sort of <paramref name="list"/> by <paramref name="key"/> (equal keys keep their order).</summary>
        internal static void SortStable<T>(List<T> list, Func<T, float> key)
        {
            if (list == null || list.Count < 2) return;
            bool sorted = true;
            for (int i = 1; i < list.Count && sorted; i++) if (key(list[i]) < key(list[i - 1])) sorted = false;
            if (sorted) return;
            var idx = new int[list.Count];
            var keys = new float[list.Count];
            for (int i = 0; i < idx.Length; i++) { idx[i] = i; keys[i] = key(list[i]); }
            Array.Sort(idx, (a, b) => { int c = keys[a].CompareTo(keys[b]); return c != 0 ? c : a.CompareTo(b); });
            var copy = list.ToArray();
            for (int i = 0; i < idx.Length; i++) list[i] = copy[idx[i]];
        }
    }

    /// <summary>
    /// One track: <see cref="Id"/>, <see cref="Type"/>, the node <see cref="Key"/> (null for camera and dof), the FK
    /// <see cref="Bone"/> of a character's rot track, the colour <see cref="Slot"/> of an item's color track,
    /// <see cref="Enabled"/>, <see cref="Label"/> and the <see cref="Keys"/> (ascending time, no two equal). A track of an
    /// unknown type keeps its whole object in <see cref="Raw"/> and is written back as read.
    /// </summary>
    public sealed class TimelineTrack : DocObject
    {
        /// <summary>32 lowercase hex; created once.</summary>
        public string Id;
        /// <summary>One of <see cref="TimelineTypes"/> (or an unknown type kept from a file).</summary>
        public string Type;
        /// <summary>The node id; null for camera and dof.</summary>
        public string Key;
        /// <summary>A character's FK bone (transform name) for a rot track; null otherwise.</summary>
        public string Bone;
        /// <summary>An item's colour slot 0–3 for a color track; null for lights and other types.</summary>
        public int? Slot;
        /// <summary>A disabled track holds no slot.</summary>
        public bool Enabled = true;
        /// <summary>Display label; null = generated from the target and type.</summary>
        public string Label;
        /// <summary>Keys, ascending time, no two at one time (no null element on write).</summary>
        public List<TimelineKey> Keys = new List<TimelineKey>();
        /// <summary>The whole object of a track of an unknown type (written back verbatim, its id first when it had none);
        /// null for a known type.</summary>
        public JObject Raw;

        public TimelineTrack() { }

        public TimelineTrack(string id, string type, string key = null) { Id = id; Type = type; Key = key; }

        /// <summary>True for a known type (a track that can drive a channel).</summary>
        public bool IsKnownType => Raw == null && TimelineTypes.IsKnown(Type);

        /// <summary>The value form of the keys (<see cref="TimelineValueShape.Unknown"/> for an unknown type).</summary>
        public TimelineValueShape Shape => Raw != null ? TimelineValueShape.Unknown : TimelineTypes.ShapeOf(Type);

        /// <summary>
        /// The identity of the channel the track drives (one track per channel), or null for an unknown type: camera and dof
        /// by type; rot by node and bone (none = the node's own rotation); color by node and slot; every other type by node.
        /// </summary>
        public string ChannelKey
        {
            get
            {
                if (!IsKnownType) return null;
                if (TimelineTypes.IsSceneWide(Type)) return Type;
                string k = Type + "|" + (Key ?? "");
                if (Type == TimelineTypes.Rot) return Bone == null ? k : k + "|bone|" + Bone;
                if (Type == TimelineTypes.Color) return Slot.HasValue ? k + "|slot|" + Slot.Value.ToString(CultureInfo.InvariantCulture) : k;
                return k;
            }
        }

        /// <summary>The index of the key at exactly time <paramref name="t"/> (as float32), or −1.</summary>
        public int IndexOfTime(float t)
        {
            if (Keys == null) return -1;
            for (int i = 0; i < Keys.Count; i++) if (Keys[i] != null && Keys[i].T == t) return i;
            return -1;
        }

        /// <summary>"type node[ bone][ slot n]" for messages.</summary>
        public string Describe()
        {
            var sb = new StringBuilder(Type ?? "?");
            if (Key != null) sb.Append(' ').Append(Key);
            if (Bone != null) sb.Append(" bone ").Append(Bone);
            if (Slot.HasValue) sb.Append(" slot ").Append(Slot.Value.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Sorts the keys by time (stable); nothing for an unknown type.</summary>
        public void SortKeys()
        {
            if (IsKnownType) TimelineDocument.SortStable(Keys, k => k?.T ?? 0f);
        }

        /// <summary>
        /// The first rule this track breaks, or null: a valid id; for a known type the key node id valid or null, the label
        /// within 256 characters, the slot 0–3 or null, keys non-null, ascending, no two at one time, with a value of the
        /// type's form and a non-null ease. An unknown type needs its raw object.
        /// </summary>
        public string Problem() => Problem(false);

        /// <summary>As <see cref="Problem()"/>; with <paramref name="roleKeys"/> (a script's Timeline) the key may also be "$role".</summary>
        public string Problem(bool roleKeys)
        {
            if (!JsonText.IsId(Id)) return "no valid id";
            if (Raw != null) return null;
            if (Type == null) return "no type";
            if (!TimelineTypes.IsKnown(Type)) return "unknown type \"" + Type + "\" without its stored object";
            if (Key != null && !JsonText.IsId(Key) && !(roleKeys && ScriptText.RoleOf(Key) != null))
                return "key \"" + Key + "\" is not a node id" + (roleKeys ? " or a role" : "");
            if (Label != null && Label.Length > TimelineLimits.MaxText) return "label over " + TimelineLimits.MaxText + " characters";
            if (Slot.HasValue && (Slot.Value < 0 || Slot.Value > 3)) return "slot " + Slot.Value + " outside 0–3";
            if (Keys == null) return "keys is null";
            var shape = Shape;
            for (int i = 0; i < Keys.Count; i++)
            {
                var k = Keys[i];
                if (k == null) return "keys[" + i + "] is null";
                if (JsonText.IsNonFinite(k.T)) return "keys[" + i + "] has a non-finite time";
                if (i > 0 && k.T == Keys[i - 1].T) return "two keys at t = " + TimelineDocument.F(k.T);
                if (i > 0 && k.T < Keys[i - 1].T) return "keys out of order at t = " + TimelineDocument.F(k.T);
                if (k.Ease == null) return "keys[" + i + "] has no ease";
                string vp = k.ValueProblem(shape, roleKeys);
                if (vp != null) return "keys[" + i + "]: " + vp;
            }
            return null;
        }

        /// <summary>A deep copy (a canonical write → read round trip).</summary>
        public TimelineTrack Clone() => Clone(false);

        /// <summary>A deep copy; with <paramref name="roleKeys"/> a track of a script's Timeline (its key or a focus node may be "$role").</summary>
        public TimelineTrack Clone(bool roleKeys)
        {
            using (TimelineDocument.EnterRoleKeys(roleKeys))
            {
                var b = CanonicalJsonWriter.ToDocumentBytes(TimelineDocument.MemberName, WriteTo);
                return ReadAt(DocReader.ForDocument(b, new DocReadContext(TimelineDocument.MemberName, false, new FindingList())), 0);
            }
        }

        /// <summary>The canonical JSON text of the track object (no trailing LF).</summary>
        public string ToJson() => ToJson(false);

        /// <summary>As <see cref="ToJson()"/>; with <paramref name="roleKeys"/> a track of a script's Timeline.</summary>
        public string ToJson(bool roleKeys)
        {
            using (TimelineDocument.EnterRoleKeys(roleKeys))
            {
                var b = CanonicalJsonWriter.ToDocumentBytes(TimelineDocument.MemberName, WriteTo);
                return Encoding.UTF8.GetString(b, 0, b.Length - 1);
            }
        }

        /// <summary>A node key of a track or a focus: a node id, or (in a script's Timeline) "$role". Null when absent or JSON null and not required.</summary>
        internal static string ReadNodeKey(DocReader r, string name, bool required)
        {
            if (!TimelineDocument.RoleKeysActive) return required ? r.Id(name) : r.IdN(name);
            var v = required ? r.ValueReq(name) : r.Value(name);
            if (v == null || (!required && v.IsNull)) return null;
            if (v.Type == JTokenType.String && ScriptText.RoleOf((string)v.Token) != null) return (string)v.Token;
            if (v.Type == JTokenType.String && JsonText.IsIdAnyCase((string)v.Token)) return v.AsId();
            throw v.Error("an id (32 hex digits) or a role (\"$name\")");
        }

        /// <summary>Writes a node key (a role reference only inside a script's Timeline).</summary>
        internal static void WriteNodeKey(CanonicalJsonWriter w, string name, string key, bool required)
        {
            if (key != null && TimelineDocument.RoleKeysActive && ScriptText.RoleOf(key) != null) { w.Str(name, key); return; }
            if (required) ReqId(w, name, key);
            else w.Id(name, key);
        }

        /// <summary>Reads one track object (the stored form) with the track's own repairs (key order, keys at one time, keys
        /// that do not fit, an unknown type, a derived id from <paramref name="index"/> when it has none).</summary>
        public static TimelineTrack ReadAt(DocReader r, int index)
        {
            var t = new TimelineTrack { readIndex = index };
            t.ReadFrom(r);
            return t;
        }

        int readIndex;

        internal override void ReadMembers(DocReader r)
        {
            if (r.Has("id")) Id = r.Id("id");
            else
            {
                Id = JsonText.DerivedId("timeline/track/" + readIndex);
                r.Report(FindingSeverity.Info, TimelineFindings.TrackId, null, "track " + readIndex + " has no id; derived id " + Id + " used");
            }
            Type = r.Str("type");
            if (!TimelineTypes.IsKnown(Type))
            {
                Raw = r.Detach();
                Keys = new List<TimelineKey>();
                r.Report(FindingSeverity.Warning, TimelineFindings.UnknownType, "type", "track type \"" + Type + "\" is unknown; the track is kept as stored and inactive");
                return;
            }
            Key = ReadNodeKey(r, "key", false);
            Bone = r.StrN("bone");
            Slot = r.IntN("slot");
            Enabled = r.Bool("enabled", true);
            Label = r.StrN("label");
            if (Label != null && Label.Length > TimelineLimits.MaxText)
            {
                r.Report(FindingSeverity.Warning, TimelineFindings.Limit, "label", "label of " + Label.Length + " characters shortened to " + TimelineLimits.MaxText);
                Label = Label.Substring(0, TimelineLimits.MaxText);
            }
            var shape = Shape;
            var readers = r.ObjArray("keys");
            var keys = new List<TimelineKey>(readers.Count);
            var fileIndex = new Dictionary<TimelineKey, int>();
            for (int i = 0; i < readers.Count; i++)
            {
                var k = TimelineKey.ReadAt(readers[i], shape);
                if (k == null) continue;
                fileIndex[k] = i;
                keys.Add(k);
            }
            bool ordered = true;
            for (int i = 1; i < keys.Count && ordered; i++) if (keys[i].T < keys[i - 1].T) ordered = false;
            if (!ordered)
            {
                TimelineDocument.SortStable(keys, k => k.T);
                r.Report(FindingSeverity.Info, TimelineFindings.KeyOrder, "keys", "keys out of order; sorted by time");
            }
            Keys = new List<TimelineKey>(keys.Count);
            foreach (var k in keys)
            {
                if (Keys.Count > 0 && Keys[Keys.Count - 1].T == k.T)
                {
                    r.Context.Findings.Warning(TimelineFindings.DuplicateKey, r.Context.Where(DocReader.Join(r.Path, "keys") + "[" + fileIndex[k] + "]"),
                        "a second key at t = " + TimelineDocument.F(k.T) + " s dropped (the first in file order is kept)");
                    continue;
                }
                Keys.Add(k);
            }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (Raw != null)
            {
                if (Raw.Property("id") == null) ReqId(w, "id", Id);
                foreach (var p in Raw.Properties())
                {
                    if (p.Name == "id") ReqId(w, "id", Id);
                    else w.Token(p.Name, p.Value);
                }
                return;
            }
            ReqId(w, "id", Id);
            ReqStr(w, "type", Type);
            var shape = Shape;
            if (shape == TimelineValueShape.Unknown)
                throw new SaveFormatException(SaveErrorCode.BadDocument, "track type \"" + Type + "\" is unknown and has no stored object", w.Entry, w.Path);
            WriteNodeKey(w, "key", Key, false);
            w.Str("bone", Bone);
            w.IntN("slot", Slot);
            w.Bool("enabled", Enabled);
            w.Str("label", Label);
            w.Name("keys");
            string at = w.Path;
            var keys = new List<TimelineKey>(Keys ?? new List<TimelineKey>());
            for (int i = 0; i < keys.Count; i++) if (keys[i] == null) throw NullElement(w, at, i);
            TimelineDocument.SortStable(keys, k => k.T);
            w.StartArray();
            foreach (var k in keys) k.WriteAs(w, shape);
            w.EndArray();
        }
    }

    /// <summary>
    /// One key: time <see cref="T"/> (seconds), <see cref="Ease"/> (the shape of the segment to the next key) and the value
    /// <c>v</c> in the member of its track's form: <see cref="Number"/>, <see cref="Integer"/>, <see cref="Vector"/> (v3 and
    /// Euler degrees), <see cref="Color"/>, <see cref="Anime"/>, <see cref="Camera"/> or <see cref="Dof"/>. The other value
    /// members are ignored.
    /// </summary>
    public sealed class TimelineKey : DocObject
    {
        public float T;
        /// <summary>One of <see cref="TimelineEases"/>; an unknown string from a file is kept (evaluated as the default).</summary>
        public string Ease = TimelineEases.Default;
        /// <summary>The value of Number tracks (animeSpeed, animeTime, eyesOpen, mouthOpen).</summary>
        public float Number;
        /// <summary>The value of Integer tracks (eyes, mouth): a pattern id.</summary>
        public int Integer;
        /// <summary>The value of Vector3 tracks (pos, scale) and Euler tracks (rot), degrees for Euler.</summary>
        public Vec3 Vector;
        /// <summary>The value of color tracks: stored values (sRGB-encoded rgb, straight alpha, no clamping).</summary>
        public Color4 Color;
        /// <summary>The value of anime tracks (non-null there).</summary>
        public AnimeRef Anime;
        /// <summary>The value of camera tracks (non-null there).</summary>
        public TimelineCamera Camera;
        /// <summary>The value of dof tracks (non-null there).</summary>
        public TimelineDof Dof;

        public TimelineKey() { }

        public TimelineKey(float t, string ease = TimelineEases.Default) { T = t; Ease = ease; }

        public static TimelineKey OfNumber(float t, float v, string ease = TimelineEases.Default) => new TimelineKey(t, ease) { Number = v };
        public static TimelineKey OfInteger(float t, int v, string ease = TimelineEases.Step) => new TimelineKey(t, ease) { Integer = v };
        public static TimelineKey OfVector(float t, Vec3 v, string ease = TimelineEases.Default) => new TimelineKey(t, ease) { Vector = v };
        public static TimelineKey OfColor(float t, Color4 v, string ease = TimelineEases.Default) => new TimelineKey(t, ease) { Color = v };
        public static TimelineKey OfAnime(float t, AnimeRef v, string ease = TimelineEases.Step) => new TimelineKey(t, ease) { Anime = v };
        public static TimelineKey OfCamera(float t, TimelineCamera v, string ease = TimelineEases.Default) => new TimelineKey(t, ease) { Camera = v };
        public static TimelineKey OfDof(float t, TimelineDof v, string ease = TimelineEases.Default) => new TimelineKey(t, ease) { Dof = v };

        /// <summary>Why the value does not fit <paramref name="shape"/> (a null object, a non-finite number), or null.</summary>
        public string ValueProblem(TimelineValueShape shape) => ValueProblem(shape, false);

        /// <summary>As <see cref="ValueProblem(TimelineValueShape)"/>; with <paramref name="roleKeys"/> a focus node may also be "$role".</summary>
        public string ValueProblem(TimelineValueShape shape, bool roleKeys)
        {
            switch (shape)
            {
                case TimelineValueShape.Number: return JsonText.IsNonFinite(Number) ? "non-finite value" : null;
                case TimelineValueShape.Integer: return null;
                case TimelineValueShape.Vector3:
                case TimelineValueShape.Euler: return Finite(Vector.X, Vector.Y, Vector.Z) ? null : "non-finite value";
                case TimelineValueShape.Color: return Finite(Color.R, Color.G, Color.B, Color.A) ? null : "non-finite value";
                case TimelineValueShape.Anime: return Anime == null ? "no animation row" : Anime.Source == null ? "animation row without source" : null;
                case TimelineValueShape.Camera: return Camera == null ? "no camera value" : null;
                case TimelineValueShape.Dof: return Dof == null ? "no depth-of-field value" : Dof.Focus != null ? Dof.Focus.Problem(roleKeys) : null;
                default: return "unknown value form";
            }
        }

        static bool Finite(params float[] v)
        {
            foreach (var x in v) if (JsonText.IsNonFinite(x)) return false;
            return true;
        }

        /// <summary>A deep copy of the key (all value members).</summary>
        public TimelineKey Clone()
        {
            var k = (TimelineKey)MemberwiseClone();
            k.Anime = Anime == null ? null : new AnimeRef(Anime.Source, Anime.Group, Anime.Category, Anime.No) { Unknown = Anime.Unknown?.Clone() };
            k.Camera = Camera?.Clone();
            k.Dof = Dof?.Clone();
            k.Unknown = Unknown?.Clone();
            return k;
        }

        // ---------- reading and writing (the track hands its form) ----------

        TimelineValueShape shape;
        bool bad;

        /// <summary>Reads one key of a track of form <paramref name="shape"/>; null when its <c>v</c> does not fit (reported
        /// <c>timeline.badKey</c>). A missing <c>t</c> or <c>v</c> or a wrong type of <c>t</c> / <c>ease</c> throws.</summary>
        public static TimelineKey ReadAt(DocReader r, TimelineValueShape shape)
        {
            var k = new TimelineKey { shape = shape };
            k.ReadFrom(r);
            return k.bad ? null : k;
        }

        internal override void ReadMembers(DocReader r)
        {
            T = r.F32("t");
            Ease = r.EnumStr("ease", TimelineEases.Default, TimelineEases.Known);
            var v = r.ValueReq("v");
            string why;
            try { why = ReadValue(v); }
            catch (SaveFormatException e) when (e.Code != SaveErrorCode.NonFinite) { why = e.Message; }
            if (why != null)
            {
                bad = true;
                r.Report(FindingSeverity.Warning, TimelineFindings.BadKey, "v", "key at t = " + TimelineDocument.F(T) + " s dropped: its value does not fit the track (" + why + ")");
            }
        }

        /// <summary>Reads <c>v</c> into the member of the form; returns why it does not fit, or null.</summary>
        string ReadValue(DocValue v)
        {
            switch (shape)
            {
                case TimelineValueShape.Number:
                    if (v.Type != JTokenType.Float && v.Type != JTokenType.Integer) return "expected a number, found " + DocReader.Describe(v.Token);
                    Number = v.AsF32();
                    return null;
                case TimelineValueShape.Integer:
                    Integer = v.AsInt();
                    return null;
                case TimelineValueShape.Vector3:
                case TimelineValueShape.Euler:
                    Vector = v.AsV3();
                    return null;
                case TimelineValueShape.Color:
                    Color = v.AsCol();
                    return null;
                case TimelineValueShape.Anime:
                    Anime = ReadNew<AnimeRef>(v.AsObj());
                    return null;
                case TimelineValueShape.Camera:
                    Camera = ReadNew<TimelineCamera>(v.AsObj());
                    return null;
                case TimelineValueShape.Dof:
                    var d = new TimelineDof();
                    d.ReadFrom(v.AsObj());
                    if (d.FocusProblem != null) return d.FocusProblem;
                    Dof = d;
                    return null;
                default:
                    return "unknown value form";
            }
        }

        internal void WriteAs(CanonicalJsonWriter w, TimelineValueShape s)
        {
            shape = s;
            WriteTo(w);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.F32("t", T);
            ReqStr(w, "ease", Ease);
            w.Name("v");
            switch (shape)
            {
                case TimelineValueShape.Number: w.F32(Number); break;
                case TimelineValueShape.Integer: w.Int(Integer); break;
                case TimelineValueShape.Vector3: w.V3(Vector); break;
                case TimelineValueShape.Euler: w.Euler(Vector); break;
                case TimelineValueShape.Color: w.Col(Color); break;
                case TimelineValueShape.Anime: Required(w, Anime).WriteTo(w); break;
                case TimelineValueShape.Camera: Required(w, Camera).WriteTo(w); break;
                case TimelineValueShape.Dof: Required(w, Dof).WriteTo(w); break;
                default: throw new SaveFormatException(SaveErrorCode.BadDocument, "key of an unknown value form", w.Entry, w.Path);
            }
        }

        static DocObject Required(CanonicalJsonWriter w, DocObject x)
        {
            if (x == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "required value is null", w.Entry, w.Path);
            return x;
        }
    }

    /// <summary>The value of a camera key: <see cref="Target"/> (the pivot), <see cref="Rot"/> (Euler degrees),
    /// <see cref="Dist"/>, <see cref="Fov"/> (degrees); position = target + Euler(rot)·(0, 0, −dist). All four are required.</summary>
    public sealed class TimelineCamera : DocObject
    {
        public Vec3 Target;
        public Vec3 Rot;
        public float Dist;
        public float Fov;

        public TimelineCamera() { }

        public TimelineCamera(Vec3 target, Vec3 rot, float dist, float fov) { Target = target; Rot = rot; Dist = dist; Fov = fov; }

        public TimelineCamera Clone() => new TimelineCamera(Target, Rot, Dist, Fov) { Unknown = Unknown?.Clone() };

        internal override void ReadMembers(DocReader r)
        {
            Target = r.V3("target");
            Rot = r.Euler("rot");
            Dist = r.F32("dist");
            Fov = r.F32("fov");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("target", Target);
            w.Euler("rot", Rot);
            w.F32("dist", Dist);
            w.F32("fov", Fov);
        }
    }

    /// <summary>The value of a dof key: <see cref="On"/>, <see cref="Focus"/> (null = the camera pivot as shown),
    /// <see cref="Size"/> (focal size 0–2), <see cref="Aperture"/> (0–0.99), <see cref="Blur"/> (the largest blur radius on a
    /// 1080-line frame; null = the renderer's).</summary>
    public sealed class TimelineDof : DocObject
    {
        public bool On = true;
        /// <summary>Null = the camera pivot as shown.</summary>
        public TimelineFocus Focus;
        public float Size;
        public float Aperture;
        /// <summary>Null = the renderer's own.</summary>
        public float? Blur;

        internal string FocusProblem;

        public TimelineDof Clone() => new TimelineDof { On = On, Focus = Focus?.Clone(), Size = Size, Aperture = Aperture, Blur = Blur, Unknown = Unknown?.Clone() };

        internal override void ReadMembers(DocReader r)
        {
            On = r.Bool("on");
            var f = r.ObjN("focus");
            if (f != null)
            {
                var focus = new TimelineFocus();
                focus.ReadFrom(f);
                FocusProblem = focus.Problem(TimelineDocument.RoleKeysActive);
                Focus = focus;
            }
            Size = r.F32("size");
            Aperture = r.F32("aperture");
            Blur = r.F32N("blur");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("on", On);
            WriteN(w, "focus", Focus);
            w.F32("size", Size);
            w.F32("aperture", Aperture);
            w.F32N("blur", Blur);
        }
    }

    /// <summary>The forms of a depth-of-field focus (a <see cref="TimelineDof.Focus"/> of null is the camera pivot).</summary>
    public enum TimelineFocusKind
    {
        /// <summary>A node's point (a character: between the eyes), or a bone of it plus an offset.</summary>
        Node = 0,
        /// <summary>A world position.</summary>
        Point = 1,
        /// <summary>A distance along the camera's forward axis.</summary>
        Dist = 2,
    }

    /// <summary>A depth-of-field focus: <c>{"node", "bone", "offset"}</c>, <c>{"point"}</c> or <c>{"dist"}</c> (exactly one form).</summary>
    public sealed class TimelineFocus : DocObject
    {
        public TimelineFocusKind Kind;
        /// <summary>Node form: the node id.</summary>
        public string Node;
        /// <summary>Node form: a transform name under the node's built object, or null (the node's point).</summary>
        public string Bone;
        /// <summary>Node form: an offset in world axes added to the bone's position, or null.</summary>
        public Vec3? Offset;
        /// <summary>Point form: the world position.</summary>
        public Vec3 Point;
        /// <summary>Dist form: the distance from the camera.</summary>
        public float Dist;

        public static TimelineFocus OfNode(string node, string bone = null, Vec3? offset = null) => new TimelineFocus { Kind = TimelineFocusKind.Node, Node = node, Bone = bone, Offset = offset };
        public static TimelineFocus OfPoint(Vec3 point) => new TimelineFocus { Kind = TimelineFocusKind.Point, Point = point };
        public static TimelineFocus OfDist(float dist) => new TimelineFocus { Kind = TimelineFocusKind.Dist, Dist = dist };

        int forms;

        public TimelineFocus Clone() => new TimelineFocus { Kind = Kind, Node = Node, Bone = Bone, Offset = Offset, Point = Point, Dist = Dist, Unknown = Unknown?.Clone(), forms = 1 };

        /// <summary>Why the focus is not exactly one valid form, or null.</summary>
        public string Problem() => Problem(false);

        /// <summary>As <see cref="Problem()"/>; with <paramref name="roleKeys"/> (a script's Timeline) the node may also be "$role".</summary>
        public string Problem(bool roleKeys)
        {
            if (forms > 1) return "a focus holds more than one of node, point, dist";
            if (forms == 0 && readFrom) return "a focus without node, point or dist";
            if (Kind == TimelineFocusKind.Node && !JsonText.IsId(Node) && !(roleKeys && ScriptText.RoleOf(Node) != null))
                return "a focus node that is not a node id" + (roleKeys ? " or a role" : "");
            return null;
        }

        bool readFrom;

        internal override void ReadMembers(DocReader r)
        {
            readFrom = true;
            forms = 0;
            if (r.Has("node"))
            {
                forms++;
                Kind = TimelineFocusKind.Node;
                Node = TimelineTrack.ReadNodeKey(r, "node", true);
                Bone = r.StrN("bone");
                Offset = r.V3N("offset");
            }
            if (r.Has("point"))
            {
                forms++;
                Kind = TimelineFocusKind.Point;
                Point = r.V3("point");
            }
            if (r.Has("dist"))
            {
                forms++;
                Kind = TimelineFocusKind.Dist;
                Dist = r.F32("dist");
            }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            switch (Kind)
            {
                case TimelineFocusKind.Node:
                    TimelineTrack.WriteNodeKey(w, "node", Node, true);
                    w.Str("bone", Bone);
                    w.V3N("offset", Offset);
                    break;
                case TimelineFocusKind.Point:
                    w.V3("point", Point);
                    break;
                case TimelineFocusKind.Dist:
                    w.F32("dist", Dist);
                    break;
            }
        }
    }

    /// <summary>
    /// A timed command: <see cref="Id"/>, time <see cref="T"/>, the bridge request <see cref="Do"/> (<c>cmd</c> and its
    /// arguments, no <c>id</c>), <see cref="Enabled"/> and a <see cref="Note"/>. An event whose request may not run
    /// (<see cref="CheckDo"/>) is kept and never fired.
    /// </summary>
    public sealed class TimelineEvent : DocObject
    {
        /// <summary>32 lowercase hex; required.</summary>
        public string Id;
        public float T;
        /// <summary>The request object (required; kept as read, written canonically).</summary>
        public JObject Do;
        public bool Enabled = true;
        public string Note;

        public TimelineEvent() { }

        public TimelineEvent(string id, float t, JObject doRequest) { Id = id; T = t; Do = doRequest; }

        /// <summary>The command name of <see cref="Do"/> (null when it has none).</summary>
        public string Command => Do?["cmd"] is JValue v && v.Type == JTokenType.String ? (string)v : null;

        /// <summary>Why this event may never fire (<see cref="CheckDo"/>), or null.</summary>
        public string Refusal => CheckDo(Do);

        /// <summary>
        /// Why a request object may not be a timed command, or null: no object, no <c>cmd</c> (a non-empty string), an
        /// <c>id</c> member, or a command that replaces or edits the Timeline (timeline.build, .set, .events, .clear,
        /// timeline.track.*, timeline.key.*). Play, pause, stop, seek and every other command are allowed.
        /// </summary>
        public static string CheckDo(JObject request)
        {
            if (request == null) return "no request object";
            var cmd = request["cmd"];
            if (cmd == null || cmd.Type != JTokenType.String || ((string)cmd).Length == 0) return "the request has no cmd";
            if (request.Property("id") != null) return "the request carries an id member";
            string c = (string)cmd;
            if (c == "timeline.build" || c == "timeline.set" || c == "timeline.events" || c == "timeline.clear" ||
                c.StartsWith("timeline.track.", StringComparison.Ordinal) || c.StartsWith("timeline.key.", StringComparison.Ordinal))
                return "the command " + c + " edits the Timeline";
            return null;
        }

        /// <summary>UTF-8 bytes of the canonical JSON text of <see cref="Do"/>.</summary>
        public int DoBytes => Do == null ? 0 : Encoding.UTF8.GetByteCount(CanonicalJsonWriter.ToText(Do));

        /// <summary>
        /// The first rule the event breaks as stored data, or null: a valid id, a finite time, a request object within
        /// 64 KiB, a note within 256 characters. (Whether the request may run is <see cref="Refusal"/>.)
        /// </summary>
        public string Problem()
        {
            if (!JsonText.IsId(Id)) return "no valid id";
            if (JsonText.IsNonFinite(T)) return "non-finite time";
            if (Do == null) return "no request object";
            if (DoBytes > TimelineLimits.MaxDoBytes) return "the request is over " + TimelineLimits.MaxDoBytes + " bytes";
            if (Note != null && Note.Length > TimelineLimits.MaxText) return "note over " + TimelineLimits.MaxText + " characters";
            return null;
        }

        /// <summary>A deep copy.</summary>
        public TimelineEvent Clone() => new TimelineEvent(Id, T, (JObject)Do?.DeepClone()) { Enabled = Enabled, Note = Note, Unknown = Unknown?.Clone() };

        /// <summary>Reads one event object; null when it is dropped (a request over the size limit, reported).</summary>
        public static TimelineEvent ReadAt(DocReader r)
        {
            var e = new TimelineEvent();
            e.ReadFrom(r);
            return e.dropped ? null : e;
        }

        bool dropped;

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            T = r.F32("t");
            var d = r.ValueReq("do");
            if (d.Type != JTokenType.Object) throw d.Error("an object");
            Do = (JObject)d.Detach();
            Enabled = r.Bool("enabled", true);
            Note = r.StrN("note");
            if (Note != null && Note.Length > TimelineLimits.MaxText)
            {
                r.Report(FindingSeverity.Warning, TimelineFindings.Limit, "note", "note of " + Note.Length + " characters shortened to " + TimelineLimits.MaxText);
                Note = Note.Substring(0, TimelineLimits.MaxText);
            }
            int size = DoBytes;
            if (size > TimelineLimits.MaxDoBytes)
            {
                dropped = true;
                r.Report(FindingSeverity.Warning, TimelineFindings.Limit, "do", "event request of " + size + " bytes is over the limit of " + TimelineLimits.MaxDoBytes + "; the event is dropped");
                return;
            }
            string why = CheckDo(Do);
            if (why != null) r.Report(FindingSeverity.Warning, TimelineFindings.BadEvent, "do", "event at t = " + TimelineDocument.F(T) + " s is kept and never runs: " + why);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "id", Id);
            w.F32("t", T);
            w.Name("do");
            if (Do == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "required object is null", w.Entry, w.Path);
            w.Token(Do);
            w.Bool("enabled", Enabled);
            w.Str("note", Note);
        }
    }
    /// <summary>
    /// <c>script</c> of a Timeline loaded from a scene script: the script's <see cref="Id"/> and <see cref="Compiled"/>, the
    /// <see cref="TimelineDocument.CompiledHash"/> of the document as it was loaded. A Timeline whose hash differs was edited since.
    /// </summary>
    public sealed class TimelineScriptMarker : DocObject
    {
        /// <summary>The member name in a Timeline.</summary>
        public const string MemberName = "script";

        /// <summary>The scene script's id (32 hex). Required.</summary>
        public string Id;
        /// <summary>16 lowercase hex digits. Required.</summary>
        public string Compiled;

        public TimelineScriptMarker() { }
        public TimelineScriptMarker(string id, string compiled) { Id = id; Compiled = compiled; }

        public TimelineScriptMarker Clone() => new TimelineScriptMarker(Id, Compiled) { Unknown = Unknown?.Clone() };

        /// <summary>Why the marker is not valid, or null.</summary>
        public string Problem()
        {
            if (!JsonText.IsId(Id)) return "the marker names no script id";
            if (!ScriptText.IsHash16(Compiled)) return "the marker's compiled hash is not 16 hex digits";
            return null;
        }

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            var v = r.ValueReq("compiled");
            Compiled = v.AsStr();
            if (!ScriptText.IsHash16(Compiled)) throw v.Error("a hash of 16 hex digits");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "id", Id);
            if (!ScriptText.IsHash16(Compiled)) throw new SaveFormatException(SaveErrorCode.BadDocument, "not a hash of 16 hex digits: \"" + Compiled + "\"", w.Entry, DocReader.Join(w.Path, "compiled"));
            w.Str("compiled", Compiled);
        }
    }
}
