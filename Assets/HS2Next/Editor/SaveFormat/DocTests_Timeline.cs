// The Timeline document of a scene (scene.json member `timeline`, format revision 4). TS1 the schema round trip: the
// members in schema order, every member written (null where allowed), events and keys sorted by time, write → read →
// write byte-stable, unknown members kept at every level; TS6 every reader repair with its finding (keys out of order,
// keys at one time keep the first, a value that does not fit drops its key, an unknown type kept verbatim and inactive, an
// unknown ease kept, a refused event kept, a second track of one channel inactive, a track without an id, a node that
// is not in the scene) and the type errors that fail a load; TS7 the limits (files: the excess dropped; edits: refused).
// TS2 files without a Timeline unchanged (the golden scene, the v0.2 fixture, a Timeline removed); TS3 revision 4 written
// when a Timeline exists (after `scene`, before `ext`; or a non-empty block, DocTests_AdvancedPose AS3, a non-empty material array, DocTests_MaterialEdit MF3, or a constraint, DocTests_Constraints CF2), the lower revisions otherwise; TS4 the empty, silent,
// idempotent migration 3 → 4 and revision 1-3 files without findings; TS5 the committed revision-4 fixture
// (Tests/SaveFormat/fixtures/timeline/scene_r4.hs2nscene: what this writer writes for SceneR4()), read by this reader
// silently and by a revision-3 registry with NewerFormat and the single finding, written back by that reader with the
// member kept as an unknown one (scene.json byte-equal, Downgraded) and read back here unchanged; TS8 the anime key
// sources join `requires`. Synthetic ids and neutral commands only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class DocTests_Timeline
    {
        public static string Id(int n) => n.ToString("x32");

        public static readonly string ItemA = Id(0xa1), ItemB = Id(0xa2), Light = Id(0xb1), CharaA = Id(0xc1);

        static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);

        static JObject Obj(string json) => (JObject)DocReader.ParseText(json, "test");

        /// <summary>A Timeline with one track of every type (every value form, every focus form, every ease) and three
        /// events with neutral commands, built in code. Node ids: two items, a light, a character.</summary>
        public static TimelineDocument AllTypes()
        {
            var d = new TimelineDocument { Duration = 6f, Loop = true, Autoplay = false };
            int n = 1;
            TimelineTrack Track(string type, string key, params TimelineKey[] keys)
            {
                var tr = new TimelineTrack(Id(0x100 + n++), type, key);
                tr.Keys.AddRange(keys);
                d.Tracks.Add(tr);
                return tr;
            }
            Track(TimelineTypes.Camera, null,
                TimelineKey.OfCamera(0f, new TimelineCamera(new Vec3(0f, 10f, 0f), new Vec3(10f, 350f, 0f), 30f, 23f), TimelineEases.Linear),
                TimelineKey.OfCamera(2f, new TimelineCamera(new Vec3(1f, 11f, 0.5f), new Vec3(12f, 10f, 0f), 25f, 30f), TimelineEases.InOut),
                TimelineKey.OfCamera(4f, new TimelineCamera(new Vec3(-1f, 9f, 0f), new Vec3(8f, 40f, 0f), 35f, 40f), TimelineEases.Step));
            Track(TimelineTypes.Dof, null,
                TimelineKey.OfDof(0f, new TimelineDof { On = true, Focus = null, Size = 0.1f, Aperture = 0.5f, Blur = 3f }, TimelineEases.Step),
                TimelineKey.OfDof(1f, new TimelineDof { On = true, Focus = TimelineFocus.OfNode(CharaA), Size = 0.2f, Aperture = 0.6f, Blur = null }, TimelineEases.Linear),
                TimelineKey.OfDof(2f, new TimelineDof { On = true, Focus = TimelineFocus.OfNode(CharaA, "cf_J_Head", new Vec3(0f, 0.5f, 0f)), Size = 0.3f, Aperture = 0.7f, Blur = 4f }, TimelineEases.In),
                TimelineKey.OfDof(3f, new TimelineDof { On = false, Focus = TimelineFocus.OfPoint(new Vec3(1f, 2f, 3f)), Size = 0.4f, Aperture = 0.8f }, TimelineEases.Out),
                TimelineKey.OfDof(4f, new TimelineDof { On = true, Focus = TimelineFocus.OfDist(12.5f), Size = 1.5f, Aperture = 0.99f, Blur = 2f }));
            Track(TimelineTypes.Pos, ItemA, TimelineKey.OfVector(0f, new Vec3(0f, 0f, 0f), TimelineEases.Linear), TimelineKey.OfVector(3f, new Vec3(5f, 0f, -2f)), TimelineKey.OfVector(6f, new Vec3(0f, 1f, 0f), TimelineEases.Out));
            Track(TimelineTypes.Rot, ItemA, TimelineKey.OfVector(0f, new Vec3(0f, 350f, 0f), TimelineEases.Linear), TimelineKey.OfVector(2f, new Vec3(0f, 10f, 0f)), TimelineKey.OfVector(4f, new Vec3(90f, 0f, 0f), TimelineEases.In));
            var bone = Track(TimelineTypes.Rot, CharaA, TimelineKey.OfVector(0f, new Vec3(0f, 0f, 0f)), TimelineKey.OfVector(1.5f, new Vec3(30f, 0f, 10f)), TimelineKey.OfVector(3f, new Vec3(0f, 45f, 0f), TimelineEases.Step));
            bone.Bone = "cf_J_Neck";
            Track(TimelineTypes.Scale, ItemB, TimelineKey.OfVector(0f, new Vec3(1f, 1f, 1f)), TimelineKey.OfVector(5f, new Vec3(2f, 0.5f, 1f)), TimelineKey.OfVector(6f, new Vec3(1f, 1f, 1f), TimelineEases.Linear));
            Track(TimelineTypes.Anime, CharaA, TimelineKey.OfAnime(0f, new AnimeRef(DocDefaults.VanillaSource, 0, 0, 1)), TimelineKey.OfAnime(2f, new AnimeRef("test.anime.pack", 1, 2, 3)), TimelineKey.OfAnime(4f, new AnimeRef(DocDefaults.VanillaSource, 0, 0, 2)));
            Track(TimelineTypes.AnimeSpeed, CharaA, TimelineKey.OfNumber(0f, 1f), TimelineKey.OfNumber(2f, 0.5f, TimelineEases.Linear), TimelineKey.OfNumber(4f, 2f));
            Track(TimelineTypes.AnimeTime, CharaA, TimelineKey.OfNumber(0f, 0f, TimelineEases.Linear), TimelineKey.OfNumber(3f, 0.75f), TimelineKey.OfNumber(6f, 1.5f));
            Track(TimelineTypes.Eyes, CharaA, TimelineKey.OfInteger(0f, 0), TimelineKey.OfInteger(1f, 3), TimelineKey.OfInteger(5f, 1));
            Track(TimelineTypes.Mouth, CharaA, TimelineKey.OfInteger(0f, 2), TimelineKey.OfInteger(2.5f, 4), TimelineKey.OfInteger(5.5f, 0));
            Track(TimelineTypes.EyesOpen, CharaA, TimelineKey.OfNumber(0f, 1f), TimelineKey.OfNumber(1f, 0f, TimelineEases.In), TimelineKey.OfNumber(1.2f, 1f));
            Track(TimelineTypes.MouthOpen, CharaA, TimelineKey.OfNumber(0f, 0f), TimelineKey.OfNumber(2f, 0.8f, TimelineEases.Out), TimelineKey.OfNumber(4f, 0.1f));
            var col = Track(TimelineTypes.Color, ItemA, TimelineKey.OfColor(0f, new Color4(1f, 0f, 0f, 1f)), TimelineKey.OfColor(3f, new Color4(0f, 0f, 1f, 0.5f), TimelineEases.Linear), TimelineKey.OfColor(6f, new Color4(0.2f, 0.4f, 0.6f, 1f)));
            col.Slot = 0;
            Track(TimelineTypes.Color, Light, TimelineKey.OfColor(0f, new Color4(1f, 1f, 1f, 1f)), TimelineKey.OfColor(4f, new Color4(1f, 0.5f, 0.25f, 1f)), TimelineKey.OfColor(6f, new Color4(1.5f, 1f, 1f, 1f), TimelineEases.Step));
            d.Tracks[0].Label = "Camera";
            d.Events.Add(new TimelineEvent(Id(0x201), 6f, Obj("{\"cmd\": \"obj.transform\", \"key\": \"" + ItemB + "\", \"pos\": [0.0, 0.0, 0.0]}")) { Note = "back home" });
            d.Events.Add(new TimelineEvent(Id(0x202), 0f, Obj("{\"cmd\": \"chara.face\", \"key\": \"" + CharaA + "\", \"eyes\": 1}")));
            d.Events.Add(new TimelineEvent(Id(0x203), 3f, Obj("{\"cmd\": \"chara.anime\", \"key\": \"" + CharaA + "\", \"group\": 0, \"category\": 0, \"no\": 1}")) { Enabled = false });
            return d;
        }

        public static void Run(TestRunner t)
        {
            DocTests_T1Golden.FixturesDir = t.FixturesDir;
            TS1(t);
            TS2(t);
            TS3(t);
            TS4(t);
            TS5(t);
            TS6(t);
            TS7(t);
            TS8(t);
        }

        // ---------------- TS1 ----------------

        static void TS1(TestRunner t)
        {
            var d = AllTypes();
            t.Check(d.Problem() == null, "TS1: the all-types Timeline breaks no rule (" + d.Problem() + ")");
            var b1 = d.ToBytes();
            var f = new FindingList();
            var back = TimelineDocument.Parse(Text(b1), f);
            var b2 = back.ToBytes();
            t.Check(b1.SequenceEqual(b2), "TS1: write → read → write byte-stable\n    " + DocIo.FirstDiff(b1, b2));
            t.Check(f.Count == 0, "TS1: the written Timeline reads back with no finding (" + f + ")");
            t.Check(back.Clone().ToBytes().SequenceEqual(b1) && d.Clone().ToBytes().SequenceEqual(b1), "TS1: Clone gives the same bytes");

            var j = back.ToJObject();
            t.Equal("duration,loop,autoplay,tracks,events", string.Join(",", j.Properties().Select(p => p.Name)), "TS1: top-level members in schema order");
            var tr = (JObject)j["tracks"][4];
            t.Equal("id,type,key,bone,slot,enabled,label,keys", string.Join(",", tr.Properties().Select(p => p.Name)), "TS1: track members in schema order, every member written");
            t.Check(tr["slot"].Type == JTokenType.Null && tr["label"].Type == JTokenType.Null && (string)tr["bone"] == "cf_J_Neck", "TS1: null where allowed; the bone of an FK track");
            t.Check(j["tracks"][0]["key"].Type == JTokenType.Null && (string)j["tracks"][0]["label"] == "Camera", "TS1: a camera track names no node");
            t.Check((int)j["tracks"][13]["slot"] == 0 && j["tracks"][14]["slot"].Type == JTokenType.Null, "TS1: an item colour track carries its slot, a light's none");
            t.Equal("t,ease,v", string.Join(",", ((JObject)tr["keys"][0]).Properties().Select(p => p.Name)), "TS1: key members t, ease, v");
            t.Equal("target,rot,dist,fov", string.Join(",", ((JObject)j["tracks"][0]["keys"][0]["v"]).Properties().Select(p => p.Name)), "TS1: camera value members");
            t.Equal("on,focus,size,aperture,blur", string.Join(",", ((JObject)j["tracks"][1]["keys"][0]["v"]).Properties().Select(p => p.Name)), "TS1: dof value members");
            var foci = ((JArray)j["tracks"][1]["keys"]).Select(k => k["v"]["focus"]).ToList();
            t.Check(foci[0].Type == JTokenType.Null && string.Join(",", ((JObject)foci[1]).Properties().Select(p => p.Name)) == "node,bone,offset" &&
                    foci[1]["bone"].Type == JTokenType.Null && (string)foci[2]["bone"] == "cf_J_Head" && foci[3]["point"] != null && (float)foci[4]["dist"] == 12.5f,
                "TS1: every focus form (pivot null, node, node + bone + offset, point, dist)");
            t.Check(j["tracks"][1]["keys"][1]["v"]["blur"].Type == JTokenType.Null && (float)j["tracks"][1]["keys"][0]["v"]["blur"] == 3f, "TS1: blur null = the renderer's");
            t.Equal("source,group,category,no", string.Join(",", ((JObject)j["tracks"][6]["keys"][1]["v"]).Properties().Select(p => p.Name)), "TS1: an anime key's value is an animeRef");
            t.Equal("id,t,do,enabled,note", string.Join(",", ((JObject)j["events"][0]).Properties().Select(p => p.Name)), "TS1: event members in schema order");
            t.Equal("0,3,6", string.Join(",", j["events"].Select(e => (string)e["t"].ToString())), "TS1: events written sorted by time");
            t.Check((string)j["events"][2]["note"] == "back home" && !(bool)j["events"][1]["enabled"], "TS1: note and enabled kept");
            var eases = new HashSet<string>(j["tracks"].SelectMany(x => x["keys"]).Select(k => (string)k["ease"]));
            t.Check(TimelineEases.All.All(eases.Contains), "TS1: every ease is used and written");
            t.Check(new HashSet<string>(j["tracks"].Select(x => (string)x["type"])).SetEquals(TimelineTypes.All), "TS1: one track of every type");

            // The exact canonical text of a small Timeline.
            var small = new TimelineDocument { Duration = 12.5f, Loop = false, Autoplay = true };
            var st = new TimelineTrack(Id(1), TimelineTypes.Pos, ItemA) { Label = "Walk" };
            st.Keys.Add(TimelineKey.OfVector(2f, new Vec3(0.1f, 0f, -1f)));
            st.Keys.Add(TimelineKey.OfVector(0f, new Vec3(1f, 2f, 3f), TimelineEases.Linear));
            small.Tracks.Add(st);
            small.Events.Add(new TimelineEvent(Id(9), 1f, Obj("{\"cmd\": \"chara.face\", \"eyes\": 2}")));
            string expected = string.Join("\n",
                "{",
                "  \"duration\": 12.5,",
                "  \"loop\": false,",
                "  \"autoplay\": true,",
                "  \"tracks\": [",
                "    {",
                "      \"id\": \"" + Id(1) + "\",",
                "      \"type\": \"pos\",",
                "      \"key\": \"" + ItemA + "\",",
                "      \"bone\": null,",
                "      \"slot\": null,",
                "      \"enabled\": true,",
                "      \"label\": \"Walk\",",
                "      \"keys\": [",
                "        {",
                "          \"t\": 0.0,",
                "          \"ease\": \"linear\",",
                "          \"v\": [1.0, 2.0, 3.0]",
                "        },",
                "        {",
                "          \"t\": 2.0,",
                "          \"ease\": \"inout\",",
                "          \"v\": [0.1, 0.0, -1.0]",
                "        }",
                "      ]",
                "    }",
                "  ],",
                "  \"events\": [",
                "    {",
                "      \"id\": \"" + Id(9) + "\",",
                "      \"t\": 1.0,",
                "      \"do\": {",
                "        \"cmd\": \"chara.face\",",
                "        \"eyes\": 2",
                "      },",
                "      \"enabled\": true,",
                "      \"note\": null",
                "    }",
                "  ]",
                "}");
            t.Equal(expected, small.ToJson(), "TS1: the canonical text (schema order, keys sorted on write, one-line vectors)");
            t.Check(small.Tracks[0].Keys[0].T == 2f, "TS1: the writer sorts a copy (the model's list is not changed)");

            // Unknown members at every level are kept and written after the known ones.
            var withUnknown = Obj(small.ToJson());
            withUnknown["future"] = new JArray(1, 2);
            ((JObject)withUnknown["tracks"][0])["futureTrack"] = "x";
            ((JObject)withUnknown["tracks"][0]["keys"][0])["futureKey"] = true;
            ((JObject)withUnknown["events"][0])["futureEvent"] = 1.5;
            var wu = TimelineDocument.FromToken(withUnknown);
            var wj = wu.ToJObject();
            t.Check(wj.Properties().Last().Name == "future" && ((JObject)wj["tracks"][0]).Properties().Last().Name == "futureTrack" &&
                    ((JObject)wj["tracks"][0]["keys"][0]).Properties().Last().Name == "futureKey" && ((JObject)wj["events"][0]).Properties().Last().Name == "futureEvent",
                "TS1: unknown members kept on the Timeline, a track, a key and an event (after the known ones)");
            var camU = Obj(d.ToJson());
            ((JObject)camU["tracks"][0]["keys"][0]["v"])["futureCam"] = 7;
            ((JObject)camU["tracks"][1]["keys"][3]["v"]["focus"])["futureFocus"] = 1;
            var cu = TimelineDocument.FromToken(camU).ToJObject();
            t.Check((int)cu["tracks"][0]["keys"][0]["v"]["futureCam"] == 7 && (int)cu["tracks"][1]["keys"][3]["v"]["focus"]["futureFocus"] == 1, "TS1: unknown members of a camera value and a focus kept");

            // A minimal hand-made Timeline: absent members take their absent values.
            var min = TimelineDocument.Parse("{\"duration\": 5}");
            t.Check(min.Duration == 5f && min.Loop && !min.Autoplay && min.Tracks.Count == 0 && min.Events.Count == 0, "TS1: absent loop / autoplay / tracks / events = true / false / [] / []");
            t.Equal("{\n  \"duration\": 5.0,\n  \"loop\": true,\n  \"autoplay\": false,\n  \"tracks\": [],\n  \"events\": []\n}", min.ToJson(), "TS1: the empty Timeline written in full");
        }

        // ---------------- TS6 ----------------

        static string Key(float tm, string v, string ease = null) => "{\"t\": " + tm.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + (ease == null ? "" : ", \"ease\": \"" + ease + "\"") + ", \"v\": " + v + "}";

        static string TrackJson(int id, string type, string key, string extra, params string[] keys) =>
            "{" + (id < 0 ? "" : "\"id\": \"" + Id(id) + "\", ") + "\"type\": \"" + type + "\"" + (key == null ? "" : ", \"key\": \"" + key + "\"") + (extra ?? "") + ", \"keys\": [" + string.Join(", ", keys) + "]}";

        static string Doc(string tracks, string events = "", float duration = 10f) =>
            "{\"duration\": " + duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", \"tracks\": [" + tracks + "], \"events\": [" + events + "]}";

        static string Event(int id, float tm, string doJson) => "{\"id\": \"" + Id(id) + "\", \"t\": " + tm.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", \"do\": " + doJson + "}";

        static Finding FindingOf(FindingList f, string code) => f.FirstOrDefault(x => x.Code == code);

        static void TS6(TestRunner t)
        {
            // keys out of order → sorted (stable), info; keys at one time → the first in file order kept, warning
            var f = new FindingList();
            var d = TimelineDocument.Parse(Doc(TrackJson(1, "animeSpeed", CharaA, null, Key(2f, "2"), Key(0f, "0"), Key(1f, "1.5"), Key(1f, "1.25"), Key(0f, "9"))), f);
            var k = d.Tracks[0].Keys;
            t.Equal("0:0,1:1.5,2:2", string.Join(",", k.Select(x => x.T + ":" + x.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))), "TS6: keys sorted, the first key of each time in file order kept");
            t.Check(f.CountOf(TimelineFindings.KeyOrder) == 1 && FindingOf(f, TimelineFindings.KeyOrder).Severity == FindingSeverity.Info, "TS6: timeline.keyOrder (info) (" + f + ")");
            t.Check(f.CountOf(TimelineFindings.DuplicateKey) == 2 && f.Where(x => x.Code == TimelineFindings.DuplicateKey).All(x => x.Severity == FindingSeverity.Warning),
                "TS6: timeline.duplicateKey (warning) once per dropped key");
            t.Check(f.Any(x => x.Code == TimelineFindings.DuplicateKey && x.Path.EndsWith("tracks[0].keys[3]")) && f.Any(x => x.Code == TimelineFindings.DuplicateKey && x.Path.EndsWith("tracks[0].keys[4]")),
                "TS6: their paths name the track and the dropped keys' file indices (" + string.Join(" | ", f.Select(x => x.Path)) + ")");
            t.Check(d.Problem() == null, "TS6: the repaired Timeline breaks no rule");

            // a key outside [0, duration] → kept, info; an event outside too
            f = new FindingList();
            d = TimelineDocument.Parse(Doc(TrackJson(1, "pos", ItemA, null, Key(-1f, "[0, 0, 0]"), Key(4f, "[1, 1, 1]"), Key(12f, "[2, 2, 2]")),
                Event(2, 11f, "{\"cmd\": \"timeline.pause\"}")), f);
            t.Check(d.Tracks[0].Keys.Count == 3 && d.Events.Count == 1, "TS6: keys and events outside [0, duration] kept");
            t.Check(f.Where(x => x.Code == TimelineFindings.Outside).Sum(x => x.Count) == 2 && f.All(x => x.Code != TimelineFindings.Outside || x.Severity == FindingSeverity.Info) &&
                    f.Any(x => x.Code == TimelineFindings.Outside && x.Path.EndsWith("tracks[0].keys")) && f.Any(x => x.Code == TimelineFindings.Outside && x.Path.EndsWith("events[0].t")),
                "TS6: timeline.outside (info) for the track and the event (" + f + ")");

            // an unknown ease → kept, written back, warning json.unknownEnum
            f = new FindingList();
            d = TimelineDocument.Parse(Doc(TrackJson(1, "eyesOpen", CharaA, null, Key(0f, "0", "bounce"), Key(1f, "1"))), f);
            t.Check(d.Tracks[0].Keys[0].Ease == "bounce" && d.ToJson().Contains("\"ease\": \"bounce\"") && f.Has("json.unknownEnum") &&
                    FindingOf(f, "json.unknownEnum").Severity == FindingSeverity.Warning, "TS6: an unknown ease kept and written back (json.unknownEnum, warning)");

            // an unknown type → kept verbatim (written back as read), inactive, warning
            f = new FindingList();
            string odd = "{\"id\": \"" + Id(2) + "\", \"type\": \"blush\", \"key\": \"" + CharaA + "\", \"keys\": [{\"t\": 0, \"v\": \"pink\"}], \"strength\": [1, 2.5]}";
            string src = Doc(TrackJson(1, "pos", ItemA, null, Key(0f, "[0, 0, 0]")) + ", " + odd);
            d = TimelineDocument.Parse(src, f);
            var u = d.Tracks[1];
            t.Check(d.Tracks.Count == 2 && !u.IsKnownType && u.Raw != null && u.ChannelKey == null && u.Keys.Count == 0, "TS6: an unknown type is kept, inactive (no channel), no keys of its own");
            t.Check(f.CountOf(TimelineFindings.UnknownType) == 1 && FindingOf(f, TimelineFindings.UnknownType).Severity == FindingSeverity.Warning, "TS6: timeline.unknownType (warning)");
            var uj = d.ToJObject()["tracks"][1];
            t.Check(JToken.DeepEquals(uj, Obj(odd)) && string.Join(",", ((JObject)uj).Properties().Select(p => p.Name)) == "id,type,key,keys,strength",
                "TS6: the unknown track is written back as read (members, order, values)");
            t.Check(TimelineDocument.Parse(d.ToJson()).ToJson() == d.ToJson(), "TS6: and stays byte-stable");
            var noId = TimelineDocument.Parse(Doc("{\"type\": \"blush\", \"keys\": []}"));
            t.Check(noId.ToJObject()["tracks"][0].First is JProperty p0 && p0.Name == "id" && (string)p0.Value == JsonText.DerivedId("timeline/track/0"),
                "TS6: an unknown track without an id gets the derived id, written first");

            // a value that does not fit → that key dropped, warning
            f = new FindingList();
            d = TimelineDocument.Parse(Doc(
                TrackJson(1, "pos", ItemA, null, Key(0f, "[0, 0]"), Key(1f, "[1, 1, 1]"), Key(2f, "\"far\"")) + ", " +
                TrackJson(2, "camera", null, null, Key(0f, "{\"target\": [0, 0, 0], \"rot\": [0, 0, 0], \"dist\": 1}"), Key(1f, "{\"target\": [0, 0, 0], \"rot\": [0, 0, 0], \"dist\": 1, \"fov\": 23}")) + ", " +
                TrackJson(3, "eyes", CharaA, null, Key(0f, "1.5"), Key(1f, "2")) + ", " +
                TrackJson(4, "dof", null, null, Key(0f, "{\"on\": true, \"focus\": {\"node\": \"" + ItemA + "\", \"point\": [0, 0, 0]}, \"size\": 0.1, \"aperture\": 0.5}"),
                    Key(1f, "{\"on\": true, \"focus\": {\"elsewhere\": 1}, \"size\": 0.1, \"aperture\": 0.5}"), Key(2f, "{\"on\": true, \"size\": 0.1, \"aperture\": 0.5}")) + ", " +
                TrackJson(5, "anime", CharaA, null, Key(0f, "[0, 0, 1]"), Key(1f, "{\"group\": 0, \"category\": 0, \"no\": 4}")) + ", " +
                TrackJson(6, "color", Light, null, Key(0f, "[1, 1, 1]"), Key(1f, "[1, 1, 1, 1]"))), f);
            t.Equal("1,1,1,1,1,1", string.Join(",", d.Tracks.Select(x => x.Keys.Count)), "TS6: each track keeps only its fitting key");
            t.Equal(8, f.CountOf(TimelineFindings.BadKey), "TS6: timeline.badKey once per dropped key (" + f + ")");
            t.Check(f.Where(x => x.Code == TimelineFindings.BadKey).All(x => x.Severity == FindingSeverity.Warning && x.Path.Contains("tracks[") && x.Path.EndsWith(".v")), "TS6: warnings at the key's v path");
            t.Check(d.Tracks[3].Keys[0].Dof.Focus == null && d.Tracks[4].Keys[0].Anime.Source == DocDefaults.VanillaSource && d.Tracks[4].Keys[0].Anime.No == 4,
                "TS6: an absent dof focus is the pivot; an animeRef's absent source is the vanilla source");

            // a track without an id → derived id, info; a repeated id → a new derived id
            f = new FindingList();
            d = TimelineDocument.Parse(Doc(TrackJson(-1, "pos", ItemA, null) + ", " + TrackJson(7, "rot", ItemA, null) + ", " + TrackJson(7, "scale", ItemA, null)), f);
            t.Check(d.Tracks[0].Id == JsonText.DerivedId("timeline/track/0") && d.Tracks[1].Id == Id(7) && d.Tracks[2].Id == JsonText.DerivedId(Id(7) + "/dup/2"),
                "TS6: derived ids (timeline/track/<index>; <id>/dup/<index>)");
            t.Check(f.CountOf(TimelineFindings.TrackId) == 2 && f.Where(x => x.Code == TimelineFindings.TrackId).All(x => x.Severity == FindingSeverity.Info), "TS6: timeline.trackId (info)");
            var again = TimelineDocument.Parse(Doc(TrackJson(-1, "pos", ItemA, null)));
            t.Check(again.Tracks[0].Id == d.Tracks[0].Id, "TS6: reading the same file twice gives the same id");

            // events that may not run → kept, never fired (warning); allowed ones give nothing
            f = new FindingList();
            d = TimelineDocument.Parse(Doc("",
                Event(1, 0f, "{\"cmd\": \"timeline.play\"}") + ", " + Event(2, 1f, "{\"key\": \"x\"}") + ", " + Event(3, 2f, "{\"cmd\": \"chara.face\", \"id\": 5}") + ", " +
                Event(4, 3f, "{\"cmd\": \"timeline.key.set\"}") + ", " + Event(5, 4f, "{\"cmd\": \"timeline.build\"}") + ", " + Event(6, 5f, "{\"cmd\": \"\"}") + ", " +
                Event(7, 6f, "{\"cmd\": \"timeline.seek\", \"t\": 0}") + ", " + Event(8, 7f, "{\"cmd\": \"timeline.track.add\"}") + ", " + Event(9, 8f, "{\"cmd\": \"chara.anime\", \"undo\": true}")), f);
            t.Equal(9, d.Events.Count, "TS6: every event kept");
            t.Equal("-,x,x,x,x,x,-,x,-", string.Join(",", d.Events.Select(e => e.Refusal == null ? "-" : "x")), "TS6: refused: no cmd, an id member, a Timeline edit (key, build, track), an empty cmd; play / seek / others allowed");
            t.Check(f.CountOf(TimelineFindings.BadEvent) == 6 && f.Where(x => x.Code == TimelineFindings.BadEvent).All(x => x.Severity == FindingSeverity.Warning && x.Path.Contains("events[")),
                "TS6: timeline.badEvent (warning) once per refused event, at its index (" + f + ")");
            t.Check(d.Events.All(e => e.Problem() == null) && d.Problem() == null, "TS6: refused events are stored data like any other (an edit keeps them)");

            // a second track of one channel → kept, inactive (the first active), warning
            f = new FindingList();
            d = TimelineDocument.Parse(Doc(TrackJson(1, "pos", ItemA, null) + ", " + TrackJson(2, "rot", ItemA, null) + ", " + TrackJson(3, "pos", ItemA, null) + ", " +
                TrackJson(4, "rot", CharaA, ", \"bone\": \"cf_J_Neck\"") + ", " + TrackJson(5, "rot", CharaA, ", \"bone\": \"cf_J_Head\"") + ", " + TrackJson(6, "rot", CharaA, ", \"bone\": \"cf_J_Neck\"") + ", " +
                TrackJson(7, "color", ItemA, ", \"slot\": 0") + ", " + TrackJson(8, "color", ItemA, ", \"slot\": 1") + ", " + TrackJson(9, "camera", null, null) + ", " + TrackJson(10, "camera", null, null)), f);
            t.Equal("-,-,x,-,-,x,-,-,-,x", string.Join(",", Enumerable.Range(0, d.Tracks.Count).Select(i => d.IsShadowed(i) ? "x" : "-")), "TS6: the second track of a channel is shadowed; pos / rot / bones / slots are separate channels");
            t.Check(f.CountOf(TimelineFindings.DuplicateTrack) == 3 && f.Where(x => x.Code == TimelineFindings.DuplicateTrack).All(x => x.Severity == FindingSeverity.Warning),
                "TS6: timeline.duplicateTrack (warning) per second track (" + f + ")");
            t.Check(d.Tracks.Count == 10 && d.IndexOfChannel(d.Tracks[2].ChannelKey) == 0, "TS6: kept; the channel's driver is the first");
            t.Check(d.Problem(d) == null, "TS6: a file's duplicates stay editable (an edit compared with the document before it)");

            // a key naming no node → kept, info (reported against the scene's nodes)
            f = new FindingList();
            d = TimelineDocument.Parse(Doc(TrackJson(1, "pos", ItemA, null) + ", " + TrackJson(2, "pos", ItemB, null) + ", " + TrackJson(3, "camera", null, null) + ", {\"id\": \"" + Id(4) + "\", \"type\": \"blush\", \"key\": \"" + ItemB + "\"}"));
            d.ReportUnknownNodes(id => id == ItemA, new DocReadContext("scene.json", false, f));
            t.Check(f.Count == 1 && f[0].Code == TimelineFindings.UnknownNode && f[0].Severity == FindingSeverity.Info && f[0].Path == "scene.json:timeline.tracks[1].key" && d.Tracks.Count == 4,
                "TS6: timeline.unknownNode (info) for a known track naming no node; kept (" + f + ")");

            // type errors fail the load (as every core member)
            void Bad(string json, string what) => t.ThrowsCode(SaveErrorCode.BadDocument, () => TimelineDocument.Parse(json), "TS6: " + what + " → BadDocument");
            Bad("{\"loop\": true}", "no duration");
            Bad("{\"duration\": \"10\"}", "a string duration");
            Bad("{\"duration\": 10, \"tracks\": {}}", "tracks not an array");
            Bad("{\"duration\": 10, \"tracks\": [{\"id\": \"" + Id(1) + "\", \"keys\": []}]}", "a track without type");
            Bad("{\"duration\": 10, \"tracks\": [{\"id\": 5, \"type\": \"pos\"}]}", "a track id that is not a string");
            Bad("{\"duration\": 10, \"tracks\": [{\"id\": \"" + Id(1) + "\", \"type\": \"pos\", \"key\": \"nope\"}]}", "a key that is not a node id");
            Bad("{\"duration\": 10, \"tracks\": [" + TrackJson(1, "pos", ItemA, null, "{\"v\": [0, 0, 0]}") + "]}", "a key without t");
            Bad("{\"duration\": 10, \"tracks\": [" + TrackJson(1, "pos", ItemA, null, "{\"t\": 0}") + "]}", "a key without v");
            Bad("{\"duration\": 10, \"tracks\": [" + TrackJson(1, "pos", ItemA, null, "{\"t\": 0, \"ease\": 3, \"v\": [0, 0, 0]}") + "]}", "an ease that is not a string");
            Bad("{\"duration\": 10, \"events\": [{\"id\": \"" + Id(1) + "\", \"t\": 0, \"do\": \"chara.face\"}]}", "a do that is not an object");
            Bad("{\"duration\": 10, \"events\": [{\"t\": 0, \"do\": {\"cmd\": \"x\"}}]}", "an event without id");
            Bad("[1]", "a Timeline that is not an object");
            t.ThrowsCode(SaveErrorCode.NonFinite, () => TimelineDocument.Parse("{\"duration\": 1e300}"), "TS6: a duration outside float32 → NonFinite");
        }

        // ---------------- TS7 ----------------

        static void TS7(TestRunner t)
        {
            // a file over a limit loads with the excess dropped (timeline.limit, warning)
            var f = new FindingList();
            var many = string.Join(", ", Enumerable.Range(0, TimelineLimits.MaxTracks + 3).Select(i => TrackJson(1000 + i, "pos", Id(5000 + i), null)));
            var d = TimelineDocument.Parse(Doc(many), f);
            t.Check(d.Tracks.Count == TimelineLimits.MaxTracks && d.Tracks.Last().Id == Id(1000 + TimelineLimits.MaxTracks - 1) && f.Has(TimelineFindings.Limit),
                "TS7: 515 tracks → the first 512 kept, timeline.limit (" + f + ")");

            f = new FindingList();
            var big = new StringBuilder();
            var keysA = new StringBuilder();
            for (int i = 0; i < 60000; i++) { if (i > 0) keysA.Append(", "); keysA.Append("{\"t\": ").Append(i).Append(", \"v\": 0}"); }
            var keysB = new StringBuilder();
            for (int i = 0; i < 50000; i++) { if (i > 0) keysB.Append(", "); keysB.Append("{\"t\": ").Append(i).Append(", \"v\": 1}"); }
            big.Append("{\"duration\": 3600, \"tracks\": [")
               .Append("{\"id\": \"").Append(Id(1)).Append("\", \"type\": \"animeSpeed\", \"key\": \"").Append(CharaA).Append("\", \"keys\": [").Append(keysA).Append("]}, ")
               .Append("{\"id\": \"").Append(Id(2)).Append("\", \"type\": \"eyesOpen\", \"key\": \"").Append(CharaA).Append("\", \"keys\": [").Append(keysB).Append("]}]}");
            d = TimelineDocument.Parse(big.ToString(), f);
            t.Check(d.KeyCount == TimelineLimits.MaxKeys && d.Tracks[0].Keys.Count == 60000 && d.Tracks[1].Keys.Count == 40000 && d.Tracks[1].Keys.Last().T == 39999f &&
                    f.Any(x => x.Code == TimelineFindings.Limit && x.Path.EndsWith("tracks[1].keys")),
                "TS7: 110,000 keys → 100,000 kept (the excess at the end of the track list), timeline.limit (" + f + ")");
            t.Check(d.Problem() == null, "TS7: exactly 100,000 keys are allowed");
            d.Tracks[1].Keys.Add(TimelineKey.OfNumber(50000f, 0f));
            t.Check(d.Problem() != null && d.Problem().Contains("100000"), "TS7: an edit to 100,001 keys is refused (" + d.Problem() + ")");

            f = new FindingList();
            var evs = string.Join(", ", Enumerable.Range(0, TimelineLimits.MaxEvents + 2).Select(i => Event(10000 + i, 4095 - i % 4096, "{\"cmd\": \"timeline.pause\"}")));
            d = TimelineDocument.Parse(Doc("", evs, 3600f), f);
            t.Check(d.Events.Count == TimelineLimits.MaxEvents && !d.Events.Any(e => e.Id == Id(10000 + TimelineLimits.MaxEvents)) && f.Has(TimelineFindings.Limit),
                "TS7: 4,098 events → the first 4,096 in file order kept, timeline.limit");
            t.Check(d.Events.Select(e => e.T).SequenceEqual(d.Events.Select(e => e.T).OrderBy(x => x)), "TS7: then sorted by time");

            f = new FindingList();
            string pad = new string('x', TimelineLimits.MaxDoBytes);
            d = TimelineDocument.Parse(Doc("", Event(1, 0f, "{\"cmd\": \"chara.face\", \"note\": \"" + pad + "\"}") + ", " + Event(2, 1f, "{\"cmd\": \"chara.face\"}")), f);
            t.Check(d.Events.Count == 1 && d.Events[0].Id == Id(2) && f.Any(x => x.Code == TimelineFindings.Limit && x.Path.EndsWith("events[0].do")), "TS7: a do over 64 KiB drops its event, timeline.limit (" + f + ")");

            f = new FindingList();
            string longText = new string('n', 300);
            d = TimelineDocument.Parse(Doc(TrackJson(1, "pos", ItemA, ", \"label\": \"" + longText + "\""), "{\"id\": \"" + Id(2) + "\", \"t\": 0, \"do\": {\"cmd\": \"chara.face\"}, \"note\": \"" + longText + "\"}"), f);
            t.Check(d.Tracks[0].Label.Length == TimelineLimits.MaxText && d.Events[0].Note.Length == TimelineLimits.MaxText && f.CountOf(TimelineFindings.Limit) == 2,
                "TS7: a label and a note over 256 characters shortened, timeline.limit");

            foreach (var (dur, expect) in new[] { (4000f, 3600f), (0f, 10f), (-2f, 10f), (3600f, 3600f), (0.01f, 0.01f) })
            {
                f = new FindingList();
                d = TimelineDocument.Parse(Doc("", "", dur), f);
                t.Check(d.Duration == expect && f.Has(TimelineFindings.Limit) == (dur != expect), "TS7: duration " + dur + " → " + expect + (dur != expect ? " (timeline.limit)" : ""));
            }

            // edits over a limit are refused (Problem names it)
            var a = AllTypes();
            var p = a.Clone();
            for (int i = 0; i < TimelineLimits.MaxTracks; i++) p.Tracks.Add(new TimelineTrack(Id(0x9000 + i), TimelineTypes.Pos, Id(0x7000 + i)));
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("512"), "TS7: an edit to more than 512 tracks refused (" + p.Problem(a) + ")");
            p = a.Clone();
            for (int i = 0; i <= TimelineLimits.MaxEvents; i++) p.Events.Add(new TimelineEvent(Id(0x9000 + i), 0f, Obj("{\"cmd\": \"timeline.pause\"}")));
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("4096"), "TS7: an edit to more than 4,096 events refused");
            p = a.Clone();
            p.Events[0].Do["pad"] = pad;
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("65536"), "TS7: an event request over 64 KiB refused");
            p = a.Clone();
            p.Tracks[2].Label = longText;
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("label"), "TS7: a label over 256 characters refused");
            p = a.Clone();
            p.Events[0].Note = longText;
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("note"), "TS7: a note over 256 characters refused");
            foreach (var dur in new[] { 0f, -1f, 3600.5f })
            {
                p = a.Clone();
                p.Duration = dur;
                t.Check(p.Problem(a) != null && p.Problem(a).Contains("duration"), "TS7: duration " + dur + " refused");
            }
            p = a.Clone();
            p.Tracks.Add(new TimelineTrack(Id(0x9999), TimelineTypes.Pos, ItemA));
            t.Check(p.Problem(a) != null && p.Problem(a).StartsWith("the channel has a track"), "TS7: a second track on a channel refused (" + p.Problem(a) + ")");
            p = a.Clone();
            p.Tracks[2].Keys.Add(TimelineKey.OfVector(3f, new Vec3(1f, 1f, 1f)));
            p.Tracks[2].SortKeys();
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("two keys at t = 3"), "TS7: two keys at one time refused (" + p.Problem(a) + ")");
            p = a.Clone();
            p.Tracks[3].Id = p.Tracks[2].Id;
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("repeats the id"), "TS7: a repeated track id refused");
            p = a.Clone();
            p.Tracks[6].Keys[0].Anime = null;
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("no animation row"), "TS7: a key without its value refused");
            p = a.Clone();
            p.Tracks[13].Slot = 4;
            t.Check(p.Problem(a) != null && p.Problem(a).Contains("slot"), "TS7: a colour slot outside 0–3 refused");
            t.Check(a.Problem(null) == null && a.Clone().Problem(a) == null, "TS7: the all-types Timeline is within every limit");
        }

        // ---------------- the revision-4 fixture ----------------

        /// <summary>The folder under the save-format fixtures root (Tests/SaveFormat/) and the file.</summary>
        public const string R4Folder = "fixtures/timeline";
        public const string R4File = "scene_r4.hs2nscene";
        public const string R4Title = "Timeline fixture scene";
        public static readonly DateTime R4Clock = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>The fixture's path under a save-format fixtures root.</summary>
        public static string R4Path(string fixturesRoot) => System.IO.Path.Combine(fixturesRoot ?? "", "fixtures", "timeline", R4File);

        /// <summary>The options the fixture was written with: fixed clock, placeholder thumbnail, no ext handlers, app 1.2.0.</summary>
        public static SaveOptions R4Options() => new SaveOptions
        {
            Clock = () => R4Clock,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = new ExtRegistry(),
            AppVersion = "1.2.0",
        };

        /// <summary>The revision-4 scene: two items, a light, a synthetic character (no card data) and the all-types Timeline
        /// over them.</summary>
        public static SceneContainer SceneR4()
        {
            var s = new SceneContainer { Title = R4Title };
            s.Scene.Nodes.Add(new SceneNode(ItemA, new ItemNodeState { Ref = "test.mod#1" }) { Order = 0 });
            s.Scene.Nodes.Add(new SceneNode(ItemB, new ItemNodeState { Ref = "test.mod#2" }) { Order = 1 });
            s.Scene.Nodes.Add(new SceneNode(Light, new LightNodeState { Ref = "illusion.hs2#0" }) { Order = 2 });
            s.Scene.Nodes.Add(new SceneNode(CharaA, new CharaNodeState()) { Order = 3 });
            s.PlaceCharacter(CharaA, InfraFixtures.SmallChara(0));
            s.Scene.Timeline = AllTypes();
            return s;
        }

        /// <summary>Writes the fixture once (refuses an existing file); returns its size.</summary>
        public static long WriteSceneR4(string fixturesRoot)
        {
            string path = R4Path(fixturesRoot);
            if (System.IO.File.Exists(path)) throw new InvalidOperationException("the fixture is never regenerated: " + path + " exists");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var r = SceneR4().Save(path, R4Options());
            if (!r.Ok) throw new InvalidOperationException(r.Error.Message);
            return new System.IO.FileInfo(path).Length;
        }

        static int FormatOf(byte[] file) => (int)Entry(file, "manifest.json")["formatVersion"];

        static JObject Entry(byte[] file, string name) => JObject.Parse(Text(ContainerReader.Read(file).Get(name).Data));

        static byte[] EntryBytes(byte[] file, string name) => ContainerReader.Read(file).Get(name)?.Data;

        static SaveOptions Opts() => InfraFixtures.Opts();

        static LoadOptions LOpts(CoreMigrationRegistry reg = null)
        {
            var o = InfraFixtures.LOpts();
            if (reg != null) o.Migrations = reg;
            return o;
        }

        static bool SameBytes(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);

        // ---------------- TS2 ----------------

        static void TS2(TestRunner t)
        {
            var golden = DocTests_T1Golden.Golden("scene.json");
            var doc = DocIo.Scene(golden);
            t.Check(doc.Timeline == null && JObject.Parse(Text(golden)).Property("timeline") == null && DocIo.Same(golden, DocIo.Bytes(doc)),
                "TS2: the golden scene has no Timeline and is written byte for byte");
            string v02 = System.IO.Path.Combine(FixtureWriter.V02Dir(t.FixturesDir ?? ""), "scene.hs2nscene");
            if (System.IO.File.Exists(v02))
            {
                var file = System.IO.File.ReadAllBytes(v02);
                var l = SceneContainer.Load(file, LOpts());
                var l3 = SceneContainer.Load(file, LOpts(new CoreMigrationRegistry(3, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2))));
                t.Check(l.Ok && l3.Ok && l.Value.Scene.Timeline == null && l.Findings.ToString() == l3.Findings.ToString(),
                    "TS2: the v0.2 fixture scene loads with no Timeline and the findings of a revision-3 reader (" + l.Findings.Count + ")");
                if (l.Ok)
                {
                    var re = l.Value.ToBytes(Opts());
                    t.Check(FormatOf(re) == 1 && SameBytes(EntryBytes(file, "scene.json"), EntryBytes(re, "scene.json")), "TS2: re-saved as format 1, scene.json byte-equal");
                }
            }
            else t.Check(false, "TS2: fixture " + v02 + " present");
            var s = InfraFixtures.SmallScene(1, 2);
            var b0 = s.ToBytes(Opts());
            t.Check(FormatOf(b0) == 1 && !Text(EntryBytes(b0, "scene.json")).Contains("\"timeline\""), "TS2: a scene without a Timeline is format 1 and has no timeline member");
            s.Scene.Timeline = AllTypes();
            s.ToBytes(Opts());
            s.Scene.Timeline = null;
            t.Check(SameBytes(b0, s.ToBytes(Opts())), "TS2: after its Timeline is removed the file is byte-equal to one that never had one");
        }

        // ---------------- TS3 ----------------

        static void TS3(TestRunner t)
        {
            var s = InfraFixtures.SmallScene(1, 2);
            var b1 = s.ToBytes(Opts());
            s.Scene.Timeline = new TimelineDocument();
            var b4 = s.ToBytes(Opts());
            var man = Entry(b4, "manifest.json");
            t.Check(FormatOf(b1) == 1 && (int)man["formatVersion"] == 4 && (int)man["minReaderVersion"] == 1, "TS3: an empty Timeline makes the file revision 4, minReaderVersion 1");
            var sj = Entry(b4, "scene.json");
            t.Equal("nodes,scene,timeline", string.Join(",", sj.Properties().Select(p => p.Name)), "TS3: timeline after scene");
            s.Scene.Ext.Set("stp3.fixture", JToken.Parse("{\"a\": 1}"));
            t.Equal("nodes,scene,timeline,ext", string.Join(",", Entry(s.ToBytes(Opts()), "scene.json").Properties().Select(p => p.Name)), "TS3: and before ext");
            s.Scene.Ext.Remove("stp3.fixture");
            var chara = (CharaNodeState)s.Scene.Nodes[0].State;
            chara.State.NippleThroughClothes = false;
            t.Check(FormatOf(s.ToBytes(Opts())) == 4, "TS3: with a revision-3 member too: 4 (the highest)");
            s.Scene.Timeline = null;
            t.Check(FormatOf(s.ToBytes(Opts())) == 3, "TS3: without the Timeline: back to 3");
            chara.State.NippleThroughClothes = true;
            t.Check(SameBytes(s.ToBytes(Opts()), b1), "TS3: and to 1, byte-equal to the file before");
            var r4 = SceneR4().ToBytes(R4Options());
            t.Check(FormatOf(r4) == 4, "TS3: the all-types scene is revision 4");
        }

        // ---------------- TS4 ----------------

        static void TS4(TestRunner t)
        {
            t.Check(CoreMigrationRegistry.Default.Supported == SaveFormats.SupportedVersion && SaveFormats.SupportedVersion >= 4 && SaveFormats.TimelineVersion == 4 && SaveFormats.MinReaderVersion == 1,
                "TS4: S = 4, the Timeline at revision 4, minReaderVersion 1");
            var s = InfraFixtures.SmallScene(2, 1);
            var f1 = s.ToBytes(Opts());
            var f2 = InfraFixtures.Repack(f1, m => InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = 2), "scene.json");
            ((CharaNodeState)s.Scene.Nodes[1].State).State.NippleThroughClothes = false;
            var f3 = s.ToBytes(Opts());
            t.Check(FormatOf(f1) == 1 && FormatOf(f2) == 2 && FormatOf(f3) == 3, "TS4: files of revisions 1, 2, 3");
            foreach (var (f, n) in new[] { (f1, 1), (f2, 2), (f3, 3) })
            {
                var l = SceneContainer.Load(f, LOpts());
                t.Check(l.Ok && !l.NewerFormat && l.Findings.Count == 0 && l.Value.Scene.Timeline == null, "TS4: a revision-" + n + " scene loads with no finding at all (" + l + ")");
                var l2 = SceneContainer.Load(f, LOpts());
                t.Check(l.Ok && l2.Ok && SameBytes(l.Value.ToBytes(Opts()), l2.Value.ToBytes(Opts())) && FormatOf(l.Value.ToBytes(Opts())) == (n == 2 ? 1 : n),
                    "TS4: repeated loads write the same bytes, at the lowest covering revision");
            }
            var docs = new Dictionary<string, JObject> { ["scene.json"] = Entry(f3, "scene.json") };
            string before = docs["scene.json"].ToString(Newtonsoft.Json.Formatting.None);
            var findings = new FindingList();
            var m3 = CoreMigrationRegistry.Empty(3);
            var ctx = new MigrationContext(SaveFormats.Scene, Entry(f3, "manifest.json"), docs, findings, 3);
            m3.Apply(ctx);
            m3.Apply(ctx);
            t.Check(m3.From == 3 && findings.Count == 0 && docs["scene.json"].ToString(Newtonsoft.Json.Formatting.None) == before, "TS4: the migration 3 → 4 changes nothing and reports nothing, also twice");
            t.Throws<InvalidOperationException>(() => new CoreMigrationRegistry(4, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2)), "TS4: a registry of 4 without the migration 3 → 4 has a gap");
            var withT = InfraFixtures.SmallScene(1, 0);
            withT.Scene.Timeline = TimelineDocument.Parse("{\"duration\": 4}");
            var v3 = InfraFixtures.Repack(withT.ToBytes(Opts()), m => InfraFixtures.EditJson(m, "manifest.json", j => j["formatVersion"] = 3), "scene.json");
            var lv = SceneContainer.Load(v3, LOpts());
            t.Check(lv.Ok && lv.Findings.Count == 0 && lv.Value.Scene.Timeline != null && lv.Value.Scene.Timeline.Duration == 4f,
                "TS4: a revision-3 file holding a timeline (what an older app writes back) is read in full through the empty migration, silently");
        }

        // ---------------- TS5 ----------------

        static void TS5(TestRunner t)
        {
            string path = R4Path(t.FixturesDir);
            if (!System.IO.File.Exists(path)) { t.Check(false, "TS5: fixture " + path + " present"); return; }
            var fixture = System.IO.File.ReadAllBytes(path);
            var now = SceneR4().ToBytes(R4Options());
            foreach (var e in ContainerReader.Read(fixture).Entries)
                t.Check(SameBytes(e.Data, EntryBytes(now, e.Name)), "TS5: the fixture's " + e.Name + " is what this writer writes for it");
            t.Check(FormatOf(fixture) == 4, "TS5: the fixture is revision 4");

            // This reader (S = 4): the Timeline, no finding.
            var l4 = SceneContainer.Load(fixture, LOpts());
            t.Check(l4.Ok && !l4.NewerFormat && l4.Findings.Count == 0 && l4.Value.Scene.Timeline != null && l4.Value.Scene.Timeline.Tracks.Count == 15,
                "TS5: an S = 4 reader reads the fixture with no finding (" + l4 + ")");
            var tlJson = l4.Ok ? l4.Value.Scene.Timeline.ToJson() : null;

            // An S = 3 reader: NewerFormat and the one finding.
            var reg3 = new CoreMigrationRegistry(3, CoreMigrationRegistry.Empty(1), CoreMigrationRegistry.Empty(2));
            var l3 = SceneContainer.Load(fixture, LOpts(reg3));
            t.Check(l3.Ok && l3.NewerFormat && l3.Findings.Count == 1 && l3.Findings[0].Code == "manifest.newerFormat" && l3.Findings[0].Message.Contains("format 4 is newer than 3"),
                "TS5: an S = 3 registry reads it with NewerFormat and the single finding manifest.newerFormat (" + l3 + ")");
            if (!l3.Ok) return;
            // What that reader holds: `timeline` as an unknown top-level member (as read), written back after `scene`.
            var c = l3.Value;
            c.Scene.Timeline = null;
            var raw = DocReader.Parse(EntryBytes(fixture, "scene.json"), "scene.json");
            c.Scene.Unknown = new JsonLeftovers();
            c.Scene.Unknown.Add("timeline", raw["timeline"].DeepClone());
            string saved = System.IO.Path.Combine(t.TempDir, "ts5_downgraded.hs2nscene");
            var sr = c.Save(saved, R4Options());
            t.Check(sr.Ok && sr.Downgraded, "TS5: saving it reports Downgraded");
            var back = System.IO.File.ReadAllBytes(saved);
            t.Check(FormatOf(back) >= 1 && FormatOf(back) <= 3 && (int)Entry(back, "manifest.json")["minReaderVersion"] == 1, "TS5: written as its own lowest covering revision (" + FormatOf(back) + ")");
            t.Check(SameBytes(EntryBytes(fixture, "scene.json"), EntryBytes(back, "scene.json")), "TS5: scene.json byte-equal (the timeline written back after scene)\n    " +
                DocIo.FirstDiff(EntryBytes(fixture, "scene.json") ?? new byte[0], EntryBytes(back, "scene.json") ?? new byte[0]));
            var lb = SceneContainer.Load(back, LOpts());
            t.Check(lb.Ok && lb.Findings.Count == 0 && lb.Value.Scene.Timeline != null && lb.Value.Scene.Timeline.ToJson() == tlJson,
                "TS5: an S = 4 reader then reads the same Timeline, silently (" + lb + ")");
            t.Check(lb.Ok && FormatOf(lb.Value.ToBytes(R4Options())) == 4, "TS5: and writes revision 4 again");

            // With a scene-scope ext block: unknown members go before ext.
            var se = SceneR4();
            se.Scene.Ext.Set("stp3.fixture", JToken.Parse("{\"a\": 1}"));
            var withExt = se.ToBytes(R4Options());
            var le = SceneContainer.Load(withExt, LOpts(reg3));
            if (le.Ok)
            {
                le.Value.Scene.Timeline = null;
                le.Value.Scene.Unknown = new JsonLeftovers();
                le.Value.Scene.Unknown.Add("timeline", DocReader.Parse(EntryBytes(withExt, "scene.json"), "scene.json")["timeline"].DeepClone());
                var wb = le.Value.ToBytes(R4Options());
                t.Check(SameBytes(EntryBytes(withExt, "scene.json"), EntryBytes(wb, "scene.json")) &&
                        string.Join(",", Entry(wb, "scene.json").Properties().Select(p => p.Name)) == "nodes,scene,timeline,ext",
                    "TS5: with a scene ext block the old writer's order is nodes, scene, timeline, ext (byte-equal)");
            }
            else t.Check(false, "TS5: the ext variant loads (" + le + ")");
        }

        // ---------------- TS8 ----------------

        static void TS8(TestRunner t)
        {
            List<string> Requires(byte[] file) => Entry(file, "manifest.json")["requires"].Select(r => (string)r["guid"]).ToList();
            var r4 = Requires(SceneR4().ToBytes(R4Options()));
            t.Check(r4.Contains("test.anime.pack") && !r4.Contains(DocDefaults.VanillaSource), "TS8: the fixture's requires lists the anime key source (not the vanilla one): " + string.Join(",", r4));
            var s = new SceneContainer { Title = "requires" };
            t.Check(Requires(s.ToBytes(Opts())).Count == 0, "TS8: an empty scene requires nothing");
            var d = new TimelineDocument { Duration = 5f };
            var tr = new TimelineTrack(Id(1), TimelineTypes.Anime, CharaA);
            tr.Keys.Add(TimelineKey.OfAnime(0f, new AnimeRef("pack.one", 1, 2, 3)));
            tr.Keys.Add(TimelineKey.OfAnime(1f, new AnimeRef("Pack.One", 1, 2, 4)));
            tr.Keys.Add(TimelineKey.OfAnime(2f, new AnimeRef("pack.two", 1, 2, 5)));
            tr.Keys.Add(TimelineKey.OfAnime(3f, new AnimeRef(DocDefaults.VanillaSource, 0, 0, 1)));
            d.Tracks.Add(tr);
            d.Tracks.Add(TimelineTrack.ReadAt(new DocReader(Obj("{\"id\": \"" + Id(2) + "\", \"type\": \"future\", \"keys\": [{\"t\": 0, \"v\": {\"source\": \"pack.raw\"}}]}"), new DocReadContext("t", false, new FindingList())), 1));
            s.Scene.Timeline = d;
            var req = Requires(s.ToBytes(Opts()));
            t.Equal("Pack.One,pack.two", string.Join(",", req), "TS8: anime sources join requires (case-insensitive once, sorted; vanilla and an unknown track's raw data excluded)");
            s.Scene.Timeline = null;
            t.Check(Requires(s.ToBytes(Opts())).Count == 0, "TS8: removing the Timeline removes them");
        }
    }
}
