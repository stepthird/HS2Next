// The input of `timeline.build` and `timeline.events` turned into stored Timeline tracks and events. A track: {type, key?,
// bone?, slot?, raw?, ease? (the keys' default, inout), label?, enabled?, keys: [{t, ease?, …}]}. Key members by type: camera target, rot, dist, fov (a member missing from a later key carries over from the previous
// key; all four on the first); pos, rot, scale, animeSpeed, animeTime, eyesOpen, mouthOpen, color: v; anime: group,
// category, no (+ source or guid), resolved as the animation command resolves them; eyes: v = the position in the
// character's eye list, or the pattern id with the track's raw: true; mouth: v = the pattern id; dof: on, focus, size /
// aperture or sharp / full, blur, near (missing members carry over; the first key's defaults are on, the camera pivot,
// sharp 0.1, full 3, blur 3). A key's ease overrides the track's; animation rows and face patterns store step. Notes are
// dropped from tracks and keys and kept on events; other unknown members are warnings (refused in strict mode). Keys are
// node ids (strings). Any error throws a bridge error and nothing is built.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Timeline
{
    /// <summary>What the build input needs from the scene.</summary>
    public interface ITimelineBuildHost
    {
        /// <summary>The kind of the node with this id in the model, or null when there is none.</summary>
        NodeKind? KindOf(string nodeId);

        /// <summary>
        /// The animation row of an <c>anime</c> key (its members <c>group</c>, <c>category</c>, <c>no</c>, absent = 0, and
        /// optional <c>source</c>, <c>guid</c> or <c>ref</c>) for the character <paramref name="charaKey"/>, resolved as the
        /// animation command resolves its arguments of the same names; null when no row matches. May throw a
        /// <see cref="BridgeException"/> of its own naming <paramref name="arg"/>.
        /// </summary>
        AnimeRef ResolveAnime(string charaKey, JObject key, string arg);

        /// <summary>
        /// The eye pattern id at <paramref name="position"/> of the character's eye list, resolved as the face command
        /// resolves a position (clamped to the list). Throws a <see cref="BridgeException"/> unavailable when the character
        /// is not built.
        /// </summary>
        int EyePatternAt(string charaKey, int position, string arg);
    }

    public static class TimelineBuildInput
    {
        /// <summary>The first dof key's defaults: sharp and full of the focal range, and the largest blur.</summary>
        public const double DefaultSharp = 0.1, DefaultFull = 3, DefaultBlur = 3;

        static readonly string[] TrackMembers = { "type", "key", "bone", "slot", "raw", "ease", "label", "enabled", "keys", "note" };
        static readonly string[] EventMembers = { "t", "do", "note", "enabled" };

        /// <summary>
        /// The stored tracks of a <c>tracks</c> array, keys ascending. <paramref name="duration"/> bounds the key times;
        /// <paramref name="existing"/> (with <c>append</c>) holds tracks already in the Timeline: a channel they drive is
        /// refused, and the limits count them. Warnings go to <paramref name="warnings"/>; with <paramref name="strict"/>
        /// an unknown member is refused.
        /// </summary>
        public static List<TimelineTrack> ParseTracks(JToken tracks, float duration, ITimelineBuildHost host, IIdSource ids, bool strict,
            List<string> warnings, IReadOnlyList<TimelineTrack> existing = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var result = new List<TimelineTrack>();
            if (tracks == null || tracks.Type == JTokenType.Null) return result;
            if (!(tracks is JArray arr)) throw Bad("tracks", "an array of tracks");
            int existingKeys = 0;
            var channels = new HashSet<string>(StringComparer.Ordinal);
            if (existing != null)
            {
                foreach (var t in existing)
                {
                    if (t == null) continue;
                    existingKeys += t.Keys?.Count ?? 0;
                    var ck = t.ChannelKey;
                    if (ck != null) channels.Add(ck);
                }
            }
            if ((existing?.Count ?? 0) + arr.Count > TimelineLimits.MaxTracks)
                throw Bad("tracks", null, "a Timeline holds at most " + TimelineLimits.MaxTracks + " tracks");
            int keys = existingKeys;
            for (int i = 0; i < arr.Count; i++)
            {
                string at = "tracks[" + I(i) + "]";
                var track = ParseTrack(arr[i], at, duration, host, ids, strict, warnings);
                var ck = track.ChannelKey;
                if (ck != null && !channels.Add(ck)) throw Bad(at, null, "the channel has a track (" + Describe(track) + ")");
                keys += track.Keys.Count;
                if (keys > TimelineLimits.MaxKeys) throw Bad(at + ".keys", null, "a Timeline holds at most " + TimelineLimits.MaxKeys + " keys");
                result.Add(track);
            }
            return result;
        }

        static string Describe(TimelineTrack t) =>
            t.Type + (t.Key != null ? " of " + t.Key : "") + (t.Bone != null ? " bone " + t.Bone : "") + (t.Slot != null ? " slot " + I(t.Slot.Value) : "");

        static TimelineTrack ParseTrack(JToken token, string at, float duration, ITimelineBuildHost host, IIdSource ids, bool strict, List<string> warnings)
        {
            if (!(token is JObject o)) throw Bad(at, "a track object");
            Unknown(o, TrackMembers, at, strict, warnings);
            string typeName = Str(o, "type", at, required: true);
            if (!TimelineTrackTypes.IsKnownName(typeName))
                throw Bad(at + ".type", "one of " + string.Join(", ", TimelineTrackTypes.StoredNames), "unknown track type \"" + typeName + "\"");
            string bone = Str(o, "bone", at);
            if (bone != null && typeName != "rot") throw Bad(at + ".bone", null, "only rot tracks name a bone");
            if (bone != null && bone.Length == 0) throw Bad(at + ".bone", "a bone (transform) name");
            var type = TimelineTrackTypes.FromStored(typeName, bone);
            int? slot = null;
            var slotTok = o["slot"];
            if (slotTok != null && slotTok.Type != JTokenType.Null)
            {
                if (type != TimelineTrackType.Color) throw Bad(at + ".slot", null, "only color tracks name a slot");
                if (!TryInt(slotTok, out int s) || s < 0 || s > TimelineTrackTypes.MaxColorSlot) throw Bad(at + ".slot", "an integer 0-3");
                slot = s;
            }

            // The node.
            string key = null;
            var keyTok = o["key"];
            if (TimelineTrackTypes.IsSceneWide(type))
            {
                if (keyTok != null && keyTok.Type != JTokenType.Null) throw Bad(at + ".key", null, typeName + " tracks name no node");
            }
            else
            {
                key = NodeKey(keyTok, at + ".key", host, required: true);
                var kind = host.KindOf(key).Value;
                if (type == TimelineTrackType.Color)
                {
                    if (kind == NodeKind.Item && slot == null) throw Bad(at + ".slot", "an integer 0-3", "a colour track of an item names its colour slot (0-3)");
                    if (kind == NodeKind.Light && slot != null) throw Bad(at + ".slot", null, "a light has one colour: its track names no slot");
                }
                if (!TimelineTrackTypes.Takes(type, kind, slot.HasValue))
                    throw new BridgeException(BridgeError.WrongKind("a " + typeName + (bone != null ? " (bone)" : "") + " track drives " +
                        TimelineTrackTypes.KindsText(type, slot.HasValue) + ", not a " + KindName(kind), at + ".key", TimelineTrackTypes.KindsText(type, slot.HasValue)));
            }

            string trackEaseName = Str(o, "ease", at);
            TimelineEase trackEase = TimelineEaseCurves.Default;
            if (trackEaseName != null) trackEase = Ease(trackEaseName, at + ".ease");
            bool raw = Bool(o, "raw", at, false);
            bool enabled = Bool(o, "enabled", at, true);
            string label = Str(o, "label", at);
            if (label != null && label.Length > TimelineLimits.MaxText) throw Bad(at + ".label", null, "a label holds at most " + TimelineLimits.MaxText + " characters");

            var track = new TimelineTrack(ids.NewId(), typeName, key) { Bone = bone, Slot = slot, Enabled = enabled, Label = label };
            var keysTok = o["keys"];
            if (keysTok == null || keysTok.Type == JTokenType.Null) return track;
            if (!(keysTok is JArray ka)) throw Bad(at + ".keys", "an array of keys");

            // Times first: within [0, duration], distinct (as stored floats); then the keys in time order (stable), so
            // that carry-over follows time.
            var order = new List<(float t, int i)>(ka.Count);
            var seen = new Dictionary<float, int>();
            for (int j = 0; j < ka.Count; j++)
            {
                string kat = at + ".keys[" + I(j) + "]";
                if (!(ka[j] is JObject ko)) throw Bad(kat, "a key object");
                var tt = ko["t"];
                if (tt == null || tt.Type == JTokenType.Null) throw new BridgeException(BridgeError.Missing(kat + ".t", "a time in seconds"));
                if (!TimelineJson.TryNumber(tt, out double td)) throw Bad(kat + ".t", "a number (seconds)");
                float tf = (float)td;
                if (!(tf >= 0) || tf > duration)
                    throw Bad(kat + ".t", "a time within [0, " + F(duration) + "]", "key time " + F(td) + " s of " + at + " is outside [0, " + F(duration) + "]");
                if (seen.TryGetValue(tf, out int other))
                    throw Bad(kat + ".t", null, "two keys of " + at + " at " + F(tf) + " s (keys " + I(other) + " and " + I(j) + "): key times must differ");
                seen[tf] = j;
                order.Add((tf, j));
            }
            order.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : a.i.CompareTo(b.i));

            var carry = new Carry();
            foreach (var (tf, j) in order)
            {
                string kat = at + ".keys[" + I(j) + "]";
                var ko = (JObject)ka[j];
                string easeName = Str(ko, "ease", kat);
                var ease = easeName != null ? Ease(easeName, kat + ".ease") : trackEase;
                if (TimelineTrackTypes.StepsOnly(type)) ease = TimelineEase.Step;
                var k = new TimelineKey(tf, TimelineEaseCurves.Name(ease));
                ParseValue(ko, k, type, key, raw, kat, host, strict, warnings, carry);
                track.Keys.Add(k);
            }
            return track;
        }

        /// <summary>The values that carry over from key to key (camera and dof).</summary>
        sealed class Carry
        {
            public bool First = true;
            public Vec3 Target, Rot;
            public float Dist, Fov;
            public bool On = true;
            public TimelineFocus Focus;
            public double Sharp = DefaultSharp, Full = DefaultFull;
            public double Size, Aperture;
            public float? Blur = (float)DefaultBlur;

            public Carry() { FromRange(DefaultSharp, DefaultFull, out Size, out Aperture); }
        }

        static readonly string[] CommonKeyMembers = { "t", "ease", "note" };
        static readonly string[] ValueKeyMembers = { "t", "ease", "note", "v" };
        static readonly string[] CameraKeyMembers = { "t", "ease", "note", "target", "rot", "dist", "fov" };
        static readonly string[] DofKeyMembers = { "t", "ease", "note", "on", "focus", "size", "aperture", "sharp", "full", "blur", "near" };
        static readonly string[] AnimeKeyMembers = { "t", "ease", "note", "group", "category", "no", "source", "guid", "ref" };

        static void ParseValue(JObject ko, TimelineKey k, TimelineTrackType type, string key, bool raw, string at, ITimelineBuildHost host, bool strict,
            List<string> warnings, Carry carry)
        {
            switch (type)
            {
                case TimelineTrackType.Camera:
                    {
                        Unknown(ko, CameraKeyMembers, at, strict, warnings);
                        bool hasT = ko["target"] != null, hasR = ko["rot"] != null, hasD = ko["dist"] != null, hasF = ko["fov"] != null;
                        if (carry.First && !(hasT && hasR && hasD && hasF))
                        {
                            string missing = !hasT ? "target" : !hasR ? "rot" : !hasD ? "dist" : "fov";
                            throw Bad(at + "." + missing, null, "the first camera key needs target, rot, dist and fov (" + missing + " is missing)");
                        }
                        if (hasT) carry.Target = Vec(ko["target"], at + ".target");
                        if (hasR) carry.Rot = Vec(ko["rot"], at + ".rot");
                        if (hasD) carry.Dist = Num(ko["dist"], at + ".dist");
                        if (hasF) carry.Fov = Num(ko["fov"], at + ".fov");
                        carry.First = false;
                        k.Camera = new TimelineCamera(carry.Target, carry.Rot, carry.Dist, carry.Fov);
                        return;
                    }
                case TimelineTrackType.Dof:
                    Unknown(ko, DofKeyMembers, at, strict, warnings);
                    k.Dof = Dof(ko, at, host, warnings, carry);
                    return;
                case TimelineTrackType.Anime:
                    {
                        Unknown(ko, AnimeKeyMembers, at, strict, warnings);
                        var nums = new int[3];
                        var names = new[] { "group", "category", "no" };
                        for (int m = 0; m < 3; m++)
                        {
                            var tok = ko[names[m]];
                            if (tok != null && tok.Type != JTokenType.Null && !TryInt(tok, out nums[m])) throw Bad(at + "." + names[m], ArgTypes.Int);
                        }
                        foreach (var m in new[] { "source", "guid", "ref" })
                        {
                            var tok = ko[m];
                            if (tok != null && tok.Type != JTokenType.Null && tok.Type != JTokenType.String) throw Bad(at + "." + m, ArgTypes.String);
                        }
                        var row = host.ResolveAnime(key, ko, at);
                        if (row == null || row.Source == null)
                            throw Bad(at, null, "no animation row " + I(nums[0]) + "/" + I(nums[1]) + "/" + I(nums[2]) + " (" + at + ")");
                        k.Anime = new AnimeRef(row.Source, row.Group, row.Category, row.No);
                        return;
                    }
                default:
                    break;
            }
            Unknown(ko, ValueKeyMembers, at, strict, warnings);
            var v = ko["v"];
            if (v == null || v.Type == JTokenType.Null) throw new BridgeException(BridgeError.Missing(at + ".v", Expected(type)));
            switch (type)
            {
                case TimelineTrackType.Pos:
                case TimelineTrackType.Scale:
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone:
                    k.Vector = Vec(v, at + ".v");
                    return;
                case TimelineTrackType.Color:
                    {
                        if (!(v is JArray ca) || (ca.Count != 3 && ca.Count != 4)) throw Bad(at + ".v", Expected(type));
                        var c = new float[4];
                        c[3] = 1;
                        for (int i = 0; i < ca.Count; i++)
                        {
                            if (!TimelineJson.TryNumber(ca[i], out double d) || !Finite((float)d)) throw Bad(at + ".v", Expected(type));
                            c[i] = (float)d;
                        }
                        k.Color = new Color4(c[0], c[1], c[2], c[3]);
                        return;
                    }
                case TimelineTrackType.Eyes:
                    {
                        if (!TryInt(v, out int n)) throw Bad(at + ".v", raw ? "a pattern id (integer)" : "a position in the eye list (integer)");
                        k.Integer = raw ? n : host.EyePatternAt(key, n, at + ".v");
                        return;
                    }
                case TimelineTrackType.Mouth:
                    {
                        if (!TryInt(v, out int n)) throw Bad(at + ".v", "a pattern id (integer)");
                        k.Integer = n;
                        return;
                    }
                default:
                    k.Number = Num(v, at + ".v");
                    return;
            }
        }

        static string Expected(TimelineTrackType t)
        {
            switch (t)
            {
                case TimelineTrackType.Pos:
                case TimelineTrackType.Scale: return "[x, y, z]";
                case TimelineTrackType.Rot:
                case TimelineTrackType.Bone: return "[x, y, z] (Euler degrees)";
                case TimelineTrackType.Color: return "[r, g, b] or [r, g, b, a]";
                case TimelineTrackType.Eyes:
                case TimelineTrackType.Mouth: return "an integer";
                default: return "a number";
            }
        }

        static TimelineDof Dof(JObject ko, string at, ITimelineBuildHost host, List<string> warnings, Carry carry)
        {
            var onTok = ko["on"];
            if (onTok != null && onTok.Type != JTokenType.Null)
            {
                if (onTok.Type != JTokenType.Boolean) throw Bad(at + ".on", "true or false");
                carry.On = (bool)onTok;
            }
            if (ko.Property("focus") != null) carry.Focus = Focus(ko["focus"], at + ".focus", host);
            bool direct = Has(ko, "size") || Has(ko, "aperture");
            bool range = Has(ko, "sharp") || Has(ko, "full");
            if (direct && range) throw Bad(at, null, "a dof key gives size / aperture or sharp / full, not both (" + at + ")");
            if (direct)
            {
                if (Has(ko, "size")) carry.Size = Num(ko["size"], at + ".size");
                if (Has(ko, "aperture")) carry.Aperture = Num(ko["aperture"], at + ".aperture");
            }
            else if (range)
            {
                if (Has(ko, "sharp")) carry.Sharp = Num(ko["sharp"], at + ".sharp");
                if (Has(ko, "full")) carry.Full = Num(ko["full"], at + ".full");
                if (carry.Sharp < 0) throw Bad(at + ".sharp", "a number ≥ 0");
                FromRange(carry.Sharp, carry.Full, out carry.Size, out carry.Aperture, at);
            }
            carry.Size = Math.Max(0, Math.Min(2, carry.Size));
            carry.Aperture = Math.Max(0, Math.Min(0.99, carry.Aperture));
            if (ko.Property("blur") != null)
            {
                var b = ko["blur"];
                carry.Blur = b.Type == JTokenType.Null ? (float?)null : Num(b, at + ".blur");
            }
            var near = ko["near"];
            if (near != null && near.Type != JTokenType.Null)
            {
                if (near.Type != JTokenType.Boolean) throw Bad(at + ".near", "true or false");
                if (!(bool)near) Warn(warnings, at + ".near: false is not supported (the near side is always blurred); ignored");
            }
            carry.First = false;
            return new TimelineDof { On = carry.On, Focus = carry.Focus?.Clone(), Size = (float)carry.Size, Aperture = (float)carry.Aperture, Blur = carry.Blur };
        }

        /// <summary>
        /// The focal size and aperture of a sharp / full range: u = 1 / (1 + sharp) − 1 / max(full, 1.0001) (below 0.02:
        /// refused), a = 1 / u, size = min(2, a · sharp / (1 + sharp)), aperture = min(0.99, a / (1 + a)).
        /// </summary>
        public static void FromRange(double sharp, double full, out double size, out double aperture, string arg = "full")
        {
            double u = 1 / (1 + sharp) - 1 / Math.Max(full, 1.0001);
            if (!(u >= 0.02)) throw Bad(arg, null, "full must be clearly larger than 1 + sharp (sharp " + F(sharp) + ", full " + F(full) + ")");
            double a = 1 / u;
            size = Math.Min(2, a * sharp / (1 + sharp));
            aperture = Math.Min(0.99, a / (1 + a));
        }

        static TimelineFocus Focus(JToken f, string at, ITimelineBuildHost host)
        {
            if (f == null || f.Type == JTokenType.Null) return null;
            if (f.Type == JTokenType.String)
            {
                string s = (string)f;
                if (s == "target") return null;
                return TimelineFocus.OfNode(NodeKey(f, at, host, required: true));
            }
            if (!(f is JObject o)) throw Bad(at, "\"target\", null, a node id, {key, bone?, offset?}, {point} or {dist}");
            int forms = (Has(o, "key") || Has(o, "node") ? 1 : 0) + (Has(o, "point") ? 1 : 0) + (Has(o, "dist") ? 1 : 0);
            if (forms != 1) throw Bad(at, "exactly one of {key, bone?, offset?}, {point} or {dist}");
            if (Has(o, "point")) return TimelineFocus.OfPoint(Vec(o["point"], at + ".point"));
            if (Has(o, "dist")) return TimelineFocus.OfDist(Num(o["dist"], at + ".dist"));
            string member = Has(o, "key") ? "key" : "node";
            string node = NodeKey(o[member], at + "." + member, host, required: true);
            string bone = null;
            var bt = o["bone"];
            if (bt != null && bt.Type != JTokenType.Null)
            {
                if (bt.Type != JTokenType.String || ((string)bt).Length == 0) throw Bad(at + ".bone", "a bone (transform) name");
                bone = (string)bt;
            }
            Vec3? offset = null;
            var ot = o["offset"];
            if (ot != null && ot.Type != JTokenType.Null) offset = Vec(ot, at + ".offset");
            return TimelineFocus.OfNode(node, bone, offset);
        }

        // ---------- events ----------

        /// <summary>
        /// The stored events of an <c>events</c> array (<c>{t, do, note?, enabled?}</c>), new ids from <paramref name="ids"/>,
        /// sorted by time (stable). <paramref name="duration"/> (when given) bounds the times. A request that may not run as a
        /// timed command is refused.
        /// </summary>
        public static List<TimelineEvent> ParseEvents(JToken events, float? duration, IIdSource ids, bool strict, List<string> warnings)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var result = new List<TimelineEvent>();
            if (events == null || events.Type == JTokenType.Null) return result;
            if (!(events is JArray arr)) throw Bad("events", "an array of {t, do, note?, enabled?}");
            if (arr.Count > TimelineLimits.MaxEvents) throw Bad("events", null, "a Timeline holds at most " + TimelineLimits.MaxEvents + " events");
            for (int i = 0; i < arr.Count; i++)
            {
                string at = "events[" + I(i) + "]";
                if (!(arr[i] is JObject o)) throw Bad(at, "an event object {t, do, note?, enabled?}");
                Unknown(o, EventMembers, at, strict, warnings);
                var tt = o["t"];
                if (tt == null || tt.Type == JTokenType.Null) throw new BridgeException(BridgeError.Missing(at + ".t", "a time in seconds"));
                if (!TimelineJson.TryNumber(tt, out double td)) throw Bad(at + ".t", "a number (seconds)");
                float tf = (float)td;
                if (!(tf >= 0) || (duration.HasValue && tf > duration.Value))
                    throw Bad(at + ".t", duration.HasValue ? "a time within [0, " + F(duration.Value) + "]" : "a time ≥ 0");
                var d = o["do"];
                if (d == null || d.Type == JTokenType.Null) throw new BridgeException(BridgeError.Missing(at + ".do", "a request object {cmd, …}"));
                string why = TimelineEventRules.Problem(d);
                if (why != null) throw Bad(at + ".do", null, why + " (" + at + ")");
                var e = new TimelineEvent(ids.NewId(), tf, (JObject)d.DeepClone())
                {
                    Enabled = Bool(o, "enabled", at, true),
                    Note = Str(o, "note", at),
                };
                if (e.Note != null && e.Note.Length > TimelineLimits.MaxText) throw Bad(at + ".note", null, "a note holds at most " + TimelineLimits.MaxText + " characters");
                result.Add(e);
            }
            // Stored sorted by time; equal times keep their list order.
            var idx = new List<int>();
            for (int i = 0; i < result.Count; i++) idx.Add(i);
            idx.Sort((a, b) => result[a].T != result[b].T ? result[a].T.CompareTo(result[b].T) : a.CompareTo(b));
            var sorted = new List<TimelineEvent>(result.Count);
            foreach (int i in idx) sorted.Add(result[i]);
            return sorted;
        }

        // ---------- helpers ----------

        static string NodeKey(JToken tok, string arg, ITimelineBuildHost host, bool required)
        {
            if (tok == null || tok.Type == JTokenType.Null)
            {
                if (required) throw new BridgeException(BridgeError.Missing(arg, ArgTypes.Node));
                return null;
            }
            if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float) throw Bad(arg, ArgTypes.Node, "keys are node ids (strings), not numbers");
            if (tok.Type != JTokenType.String) throw Bad(arg, ArgTypes.Node);
            string id = (string)tok;
            if (host.KindOf(id) == null) throw new BridgeException(BridgeError.NotFound("no node " + id, arg));
            return id;
        }

        static string KindName(NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Chara: return "character";
                case NodeKind.Item: return "item";
                case NodeKind.Light: return "light";
                case NodeKind.Folder: return "folder";
                case NodeKind.Route: return "route";
                case NodeKind.Camera: return "camera";
                default: return "node of an unknown kind";
            }
        }

        static TimelineEase Ease(string name, string arg)
        {
            if (!TimelineEaseCurves.TryParse(name, out var e)) throw Bad(arg, "one of " + string.Join(", ", TimelineEaseCurves.Names), "unknown ease \"" + name + "\"");
            return e;
        }

        static void Unknown(JObject o, string[] known, string at, bool strict, List<string> warnings)
        {
            foreach (var p in o.Properties())
            {
                if (Array.IndexOf(known, p.Name) >= 0) continue;
                if (strict) throw Bad(at + "." + p.Name, "one of " + string.Join(", ", known), "unknown member '" + p.Name + "' of " + at + " (strict)");
                Warn(warnings, "unknown member '" + p.Name + "' of " + at + " ignored");
            }
        }

        static void Warn(List<string> warnings, string text)
        {
            if (warnings != null && !warnings.Contains(text)) warnings.Add(text);
        }

        static bool Has(JObject o, string name)
        {
            var t = o[name];
            return t != null && t.Type != JTokenType.Null;
        }

        static string Str(JObject o, string name, string at, bool required = false)
        {
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null)
            {
                if (required) throw new BridgeException(BridgeError.Missing(at + "." + name, ArgTypes.String));
                return null;
            }
            if (t.Type != JTokenType.String) throw Bad(at + "." + name, ArgTypes.String);
            return (string)t;
        }

        static bool Bool(JObject o, string name, string at, bool absent)
        {
            var t = o[name];
            if (t == null || t.Type == JTokenType.Null) return absent;
            if (t.Type != JTokenType.Boolean) throw Bad(at + "." + name, "true or false");
            return (bool)t;
        }

        static float Num(JToken t, string arg)
        {
            if (!TimelineJson.TryNumber(t, out double d) || !Finite((float)d)) throw Bad(arg, "a number");
            return (float)d;
        }

        static Vec3 Vec(JToken t, string arg)
        {
            if (!TimelineJson.TryVec(t, out var v) || !Finite((float)v.X) || !Finite((float)v.Y) || !Finite((float)v.Z)) throw Bad(arg, "[x, y, z]");
            return new Vec3((float)v.X, (float)v.Y, (float)v.Z);
        }

        static bool TryInt(JToken t, out int v)
        {
            v = 0;
            if (t == null) return false;
            if (t.Type == JTokenType.Integer)
            {
                long l = t.Value<long>();
                if (l < int.MinValue || l > int.MaxValue) return false;
                v = (int)l;
                return true;
            }
            if (t.Type == JTokenType.Float)
            {
                double d = t.Value<double>();
                if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue) return false;
                v = (int)d;
                return true;
            }
            return false;
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        static BridgeException Bad(string arg, string expected, string message = null) => new BridgeException(BridgeError.BadArgs(arg, expected, message));

        static string I(int i) => i.ToString(CultureInfo.InvariantCulture);

        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
