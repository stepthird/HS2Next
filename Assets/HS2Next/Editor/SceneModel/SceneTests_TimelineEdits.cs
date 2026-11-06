// TM1: the scene's Timeline on the bus (TimelineEditCommand). Each editing operation (build, set, events, clear, a track
// added / removed / changed, keys set / deleted / moved) is one history entry with its label and raises the version; undo
// and redo restore the document byte-equal (its JSON); consecutive edits with one merge key merge (a key drag, a typed value
// edited twice), others do not; `undo: false` applies, records nothing and keeps the history; inside a transaction (a
// client batch) the edits join its one entry; the limits and the one-track-per-channel rule are refused with bad_args and
// leave nothing; a no-op edit is NoOp; the change record is SceneChanged("timeline"); a save writes the Timeline (revision
// 4) and a load, NewScene and Bind replace it. Synthetic ids only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_TimelineEdits
    {
        static string Id(int n) => n.ToString("x32");
        static readonly string NodeA = Id(0xa1), NodeB = Id(0xa2);

        static string Json(SceneSession s) => s.Model.Timeline?.ToJson();

        static TimelineDocument Doc(float duration = 10f)
        {
            var d = new TimelineDocument { Duration = duration };
            var pos = new TimelineTrack(Id(1), TimelineTypes.Pos, NodeA);
            pos.Keys.Add(TimelineKey.OfVector(0f, new Vec3(0f, 0f, 0f)));
            pos.Keys.Add(TimelineKey.OfVector(5f, new Vec3(1f, 0f, 0f)));
            d.Tracks.Add(pos);
            var rot = new TimelineTrack(Id(2), TimelineTypes.Rot, NodeA);
            rot.Keys.Add(TimelineKey.OfVector(1f, new Vec3(0f, 90f, 0f)));
            d.Tracks.Add(rot);
            return d;
        }

        static TimelineTrack TrackOf(SceneSession s, int i) => s.Model.Timeline.Tracks[i].Clone();

        static CommandResult Run(SceneSession s, ISceneCommand c, string mergeKey = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(c, new ExecuteOptions { Record = mode, MergeKey = mergeKey, Source = CommandSource.Bridge });

        public static void Run(TestRunner t)
        {
            Entries(t);
            Merges(t);
            NotRecordedAndBatch(t);
            Refusals(t);
            FileAndSession(t);
        }

        // ---------- one entry per operation; undo / redo byte-equal ----------

        static void Entries(TestRunner t)
        {
            var s = Kit.Session();
            var rec = new EventRecorder(s);
            t.Check(s.Model.Timeline == null, "TM1: a new scene has no Timeline");
            var states = new List<string> { null };
            var labels = new List<string>();
            long v0 = s.Commands.Version;

            void Step(ISceneCommand c, string label)
            {
                long v = s.Commands.Version;
                int n = s.Commands.UndoList.Count;
                rec.Clear();
                var r = Run(s, c);
                Kit.Ok(t, r, "TM1: " + label);
                t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == label, "TM1: " + label + " is one entry labelled «" + label + "»");
                t.Check(s.Commands.Version == v + 1, "TM1: " + label + " raises the version by one");
                t.Check(rec.Batches.Count == 1 && rec.Batches[0].Changes.Count == 1 && rec.Batches[0].Changes[0].Kind == SceneChangeKind.SceneChanged &&
                        rec.Batches[0].Changes[0].Path == TimelineEditCommand.ChangePath, "TM1: " + label + " reports one SceneChanged(\"timeline\")");
                states.Add(Json(s));
                labels.Add(label);
            }

            Step(TimelineEditCommand.ReplaceDocument(Doc(), TimelineEditCommand.LabelBuild), TimelineEditCommand.LabelBuild);
            t.Check(s.Model.Timeline != null && s.Model.Timeline.Tracks.Count == 2 && s.Commands.IsDirty, "TM1: built, the scene is modified");

            var d = s.Model.Timeline.Clone();
            d.Duration = 12f;
            d.Loop = false;
            Step(TimelineEditCommand.ReplaceDocument(d, TimelineEditCommand.LabelTimeline), TimelineEditCommand.LabelTimeline);

            d = s.Model.Timeline.Clone();
            d.Events.Add(new TimelineEvent(Id(0x51), 2f, Newtonsoft.Json.Linq.JObject.Parse("{\"cmd\": \"chara.face\", \"eyes\": 1}")));
            d.Events.Add(new TimelineEvent(Id(0x52), 0f, Newtonsoft.Json.Linq.JObject.Parse("{\"cmd\": \"timeline.pause\"}")));
            Step(TimelineEditCommand.ReplaceDocument(d, TimelineEditCommand.LabelEvents), TimelineEditCommand.LabelEvents);
            t.Check(s.Model.Timeline.Events[0].T == 0f, "TM1: events stored sorted by time");

            d = s.Model.Timeline.Clone();
            var scale = new TimelineTrack(Id(3), TimelineTypes.Scale, NodeB);
            scale.Keys.Add(TimelineKey.OfVector(0f, new Vec3(1f, 1f, 1f)));
            d.Tracks.Add(scale);
            Step(TimelineEditCommand.ReplaceDocument(d, TimelineEditCommand.LabelAddTrack), TimelineEditCommand.LabelAddTrack);

            var tr = TrackOf(s, 0);
            tr.Enabled = false;
            tr.Label = "Slide";
            Step(TimelineEditCommand.ReplaceTrack(tr), TimelineEditCommand.LabelTrack);

            tr = TrackOf(s, 0);
            tr.Keys.Add(TimelineKey.OfVector(2.5f, new Vec3(0.5f, 1f, 0f), TimelineEases.Step));
            Step(TimelineEditCommand.ReplaceTrack(tr, TimelineEditCommand.LabelKey), TimelineEditCommand.LabelKey);
            t.Check(s.Model.Timeline.Tracks[0].Keys.Select(k => k.T).SequenceEqual(new[] { 0f, 2.5f, 5f }), "TM1: a key added in the middle is stored in time order");

            tr = TrackOf(s, 0);
            tr.Keys.RemoveAt(2);
            Step(TimelineEditCommand.ReplaceTrack(tr, TimelineEditCommand.LabelDeleteKeys), TimelineEditCommand.LabelDeleteKeys);

            tr = TrackOf(s, 0);
            tr.Keys[1].T = 4f;
            Step(TimelineEditCommand.ReplaceTrack(tr, TimelineEditCommand.LabelMoveKeys), TimelineEditCommand.LabelMoveKeys);

            d = s.Model.Timeline.Clone();
            d.Tracks.RemoveAt(1);
            Step(TimelineEditCommand.ReplaceDocument(d, TimelineEditCommand.LabelRemoveTrack), TimelineEditCommand.LabelRemoveTrack);

            Step(TimelineEditCommand.Clear(), TimelineEditCommand.LabelClear);
            t.Check(s.Model.Timeline == null, "TM1: cleared, the scene has no Timeline");
            t.Check(s.Commands.Version == v0 + labels.Count, "TM1: one version step per edit");

            // Undo walks back through every state byte-equal, redo forward.
            bool undoOk = true, redoOk = true;
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                Kit.Ok(t, s.Commands.Undo(), "TM1: undo " + labels[i]);
                if (Json(s) != states[i]) { undoOk = false; t.Info("undo " + labels[i] + " differs"); }
            }
            t.Check(undoOk && s.Model.Timeline == null, "TM1: undo restores each earlier Timeline byte-equal (back to none)");
            for (int i = 0; i < labels.Count; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "TM1: redo " + labels[i]);
                if (Json(s) != states[i + 1]) { redoOk = false; t.Info("redo " + labels[i] + " differs"); }
            }
            t.Check(redoOk, "TM1: redo restores each later Timeline byte-equal");

            // The history size is twice the JSON length of before and after.
            s.Commands.Undo();
            s.Commands.Undo();
            Kit.Ok(t, s.Commands.Redo(), "TM1: redo «Remove track»");
            var e = s.Commands.UndoList[0];
            long expect = TimelineEditCommand.BaseBytes + 2L * (states[labels.Count - 2].Length + states[labels.Count - 1].Length);
            t.Check(e.Label == TimelineEditCommand.LabelRemoveTrack && e.EstimatedBytes == expect, "TM1: entry size = 256 + 2 × (before + after JSON lengths) (" + e.EstimatedBytes + ", expected " + expect + ")");

            // A no-op edit: nothing recorded, the version unchanged.
            long vn = s.Commands.Version;
            int un = s.Commands.UndoList.Count;
            Kit.NoOp(t, Run(s, TimelineEditCommand.ReplaceDocument(s.Model.Timeline.Clone())), "TM1: an edit to the same document");
            Kit.NoOp(t, Run(s, TimelineEditCommand.ReplaceTrack(TrackOf(s, 0))), "TM1: an edit to the same track");
            t.Check(s.Commands.Version == vn && s.Commands.UndoList.Count == un, "TM1: a no-op edit records nothing and keeps the version");
        }

        // ---------- merges ----------

        static void Merges(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceDocument(Doc(), TimelineEditCommand.LabelBuild)), "TM1 merge: build");
            string before = Json(s);
            int n = s.Commands.UndoList.Count;
            // A drag: three preview moves of the same key with one merge key → one entry «Move keys».
            for (int i = 1; i <= 3; i++)
            {
                var tr = TrackOf(s, 0);
                tr.Keys[1].T = 5f - i * 0.5f;
                Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceTrack(tr, TimelineEditCommand.LabelMoveKeys), "move:" + Id(1)), "TM1 merge: move " + i);
            }
            string moved = Json(s);
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == TimelineEditCommand.LabelMoveKeys && s.Model.Timeline.Tracks[0].Keys[1].T == 3.5f,
                "TM1 merge: consecutive key moves with one merge key are one entry");
            // A typed value edited twice with merge → one entry «Key».
            for (int i = 1; i <= 2; i++)
            {
                var tr = TrackOf(s, 0);
                tr.Keys[0].Vector = new Vec3(i, 0f, 0f);
                Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceTrack(tr, TimelineEditCommand.LabelKey), "key:" + Id(1) + ":0"), "TM1 merge: key set " + i);
            }
            t.Check(s.Commands.UndoList.Count == n + 2 && s.Commands.UndoList[0].Label == TimelineEditCommand.LabelKey, "TM1 merge: a value typed twice with merge is one entry");
            Kit.Ok(t, s.Commands.Undo(), "TM1 merge: undo the key entry");
            t.Check(Json(s) == moved, "TM1 merge: its undo restores the state before the first edit");
            Kit.Ok(t, s.Commands.Undo(), "TM1 merge: undo the drag");
            t.Check(Json(s) == before, "TM1 merge: the drag's undo restores the state before the drag");
            Kit.Ok(t, s.Commands.Redo(), "TM1 merge: redo the drag");
            t.Check(Json(s) == moved, "TM1 merge: its redo gives the last state of the drag");
            Kit.Ok(t, s.Commands.Redo(), "TM1 merge: redo the key");

            // No merge: other merge keys, no merge key, another track with the same key, a document edit after a track edit.
            n = s.Commands.UndoList.Count;
            var a = TrackOf(s, 0); a.Keys[0].Vector = new Vec3(7f, 0f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(a, TimelineEditCommand.LabelKey), "k1");
            var b = TrackOf(s, 0); b.Keys[0].Vector = new Vec3(8f, 0f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(b, TimelineEditCommand.LabelKey), "k2");
            var c = TrackOf(s, 0); c.Keys[0].Vector = new Vec3(9f, 0f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(c, TimelineEditCommand.LabelKey));
            var other = TrackOf(s, 1); other.Keys[0].Vector = new Vec3(0f, 45f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(other, TimelineEditCommand.LabelKey), null);
            var other2 = TrackOf(s, 1); other2.Keys[0].Vector = new Vec3(0f, 46f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(other2, TimelineEditCommand.LabelKey), "same");
            var first = TrackOf(s, 0); first.Keys[0].Vector = new Vec3(10f, 0f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(first, TimelineEditCommand.LabelKey), "same");
            var dd = s.Model.Timeline.Clone(); dd.Duration = 20f;
            Run(s, TimelineEditCommand.ReplaceDocument(dd), "same");
            t.Equal(n + 7, s.Commands.UndoList.Count, "TM1 merge: different keys, no key, another track, a document edit: separate entries");
            // A merge key after a save does not merge into the saved entry (the bus seals it).
            s.Commands.MarkSaved();
            var g = TrackOf(s, 0); g.Keys[0].Vector = new Vec3(11f, 0f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(g, TimelineEditCommand.LabelKey), "x");
            var h = TrackOf(s, 0); h.Keys[0].Vector = new Vec3(12f, 0f, 0f);
            Run(s, TimelineEditCommand.ReplaceTrack(h, TimelineEditCommand.LabelKey), "x");
            t.Equal(n + 8, s.Commands.UndoList.Count, "TM1 merge: two edits with one key after a save merge with each other only");
        }

        // ---------- undo: false and batches ----------

        static void NotRecordedAndBatch(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceDocument(Doc(), TimelineEditCommand.LabelBuild)), "TM1 undo:false: build");
            var tr = TrackOf(s, 0);
            tr.Keys[0].Vector = new Vec3(3f, 3f, 3f);
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceTrack(tr, TimelineEditCommand.LabelKey)), "TM1 undo:false: a recorded key edit");
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            var d = s.Model.Timeline.Clone();
            d.Autoplay = true;
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceDocument(d), null, HistoryMode.None), "TM1 undo:false: a document edit");
            var tr2 = TrackOf(s, 1);
            tr2.Keys[0].Vector = new Vec3(0f, 10f, 0f);
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceTrack(tr2), null, HistoryMode.None), "TM1 undo:false: a track edit");
            t.Check(s.Model.Timeline.Autoplay && s.Model.Timeline.Tracks[1].Keys[0].Vector.Y == 10f, "TM1 undo:false: applied");
            t.Check(s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == TimelineEditCommand.LabelKey, "TM1 undo:false: nothing recorded, the history kept (not structural)");
            t.Check(s.Commands.Version == v + 2 && s.Commands.IsDirty, "TM1 undo:false: the version still rises (the scene is modified)");
            Kit.Ok(t, s.Commands.Undo(), "TM1 undo:false: the earlier entry still undoes");
            t.Check(s.Model.Timeline.Tracks[0].Keys[0].Vector.X == 0f, "TM1 undo:false: and restores its track");

            // A client batch: the edits join the transaction's one entry.
            s = Kit.Session();
            Kit.Ok(t, s.AddFolder(), "TM1 batch: a folder");
            string folder = s.Model.Roots[0].Id;
            n = s.Commands.UndoList.Count;
            string before = Json(s);
            using (var tx = s.Commands.Begin("batch", new ExecuteOptions { Source = CommandSource.Bridge }))
            {
                Kit.Ok(t, tx.Execute(TimelineEditCommand.ReplaceDocument(Doc(), TimelineEditCommand.LabelBuild)), "TM1 batch: build inside");
                var bt = s.Model.Timeline.Tracks[0].Clone();
                bt.Keys[0].Vector = new Vec3(4f, 0f, 0f);
                Kit.Ok(t, tx.Execute(TimelineEditCommand.ReplaceTrack(bt, TimelineEditCommand.LabelKey)), "TM1 batch: key inside");
                Kit.Ok(t, tx.Set(folder, "transform.pos", new Vec3(1f, 2f, 3f)), "TM1 batch: a property inside");
                Kit.Ok(t, tx.Commit(), "TM1 batch: commit");
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == "batch", "TM1 batch: the edits join the batch's one entry");
            string after = Json(s);
            Kit.Ok(t, s.Commands.Undo(), "TM1 batch: undo the batch");
            t.Check(Json(s) == before && s.Model.Timeline == null, "TM1 batch: its undo removes the Timeline again");
            Kit.Ok(t, s.Commands.Redo(), "TM1 batch: redo");
            t.Check(Json(s) == after && s.Model.Timeline.Tracks[0].Keys[0].Vector.X == 4f, "TM1 batch: its redo restores both edits");
            n = s.Commands.UndoList.Count;
            using (var tx = s.Commands.Begin("quiet", ExecuteOptions.NotRecorded))
            {
                var qt = s.Model.Timeline.Tracks[0].Clone();
                qt.Keys[0].Vector = new Vec3(6f, 0f, 0f);
                Kit.Ok(t, tx.Execute(TimelineEditCommand.ReplaceTrack(qt, TimelineEditCommand.LabelKey)), "TM1 batch: a key in an unrecorded batch");
                Kit.Ok(t, tx.Commit(), "TM1 batch: commit the unrecorded batch");
            }
            t.Check(s.Commands.UndoList.Count == n && s.Model.Timeline.Tracks[0].Keys[0].Vector.X == 6f, "TM1 batch: an unrecorded batch of Timeline edits keeps the history");
        }

        // ---------- refusals ----------

        static void Refusals(TestRunner t)
        {
            var s = Kit.Session();
            var tr0 = new TimelineTrack(Id(1), TimelineTypes.Pos, NodeA);
            Kit.Fails(t, Run(s, TimelineEditCommand.ReplaceTrack(tr0)), CommandCodes.NotFound, "TM1 refuse: a track edit without a Timeline");
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceDocument(Doc(), TimelineEditCommand.LabelBuild)), "TM1 refuse: build");
            string state = Json(s);
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            void Refused(ISceneCommand c, string code, string what, string mustContain = null)
            {
                var r = Run(s, c);
                Kit.Fails(t, r, code, "TM1 refuse: " + what);
                if (mustContain != null) t.Check(r.Message != null && r.Message.Contains(mustContain), "TM1 refuse: " + what + " names it (" + r.Message + ")");
            }
            Refused(TimelineEditCommand.ReplaceTrack(new TimelineTrack(Id(9), TimelineTypes.Pos, NodeB)), CommandCodes.NotFound, "an unknown track id");

            var d = s.Model.Timeline.Clone();
            d.Tracks.Add(new TimelineTrack(Id(5), TimelineTypes.Pos, NodeA));
            Refused(TimelineEditCommand.ReplaceDocument(d, TimelineEditCommand.LabelAddTrack), CommandCodes.BadArgs, "a second track on a channel", "the channel has a track");
            var re = TrackOf(s, 1);
            re.Type = TimelineTypes.Pos;
            Refused(TimelineEditCommand.ReplaceTrack(re), CommandCodes.BadArgs, "a track moved onto a driven channel", "the channel has a track");
            var dup = TrackOf(s, 0);
            dup.Keys.Add(TimelineKey.OfVector(5f, new Vec3(9f, 9f, 9f)));
            Refused(TimelineEditCommand.ReplaceTrack(dup), CommandCodes.BadArgs, "two keys at one time", "two keys at t = 5");
            d = s.Model.Timeline.Clone();
            d.Duration = 3601f;
            Refused(TimelineEditCommand.ReplaceDocument(d), CommandCodes.BadArgs, "a duration over 3600 s", "duration");
            d = s.Model.Timeline.Clone();
            for (int i = 0; i < TimelineLimits.MaxTracks; i++) d.Tracks.Add(new TimelineTrack(Id(0x1000 + i), TimelineTypes.Pos, Id(0x2000 + i)));
            Refused(TimelineEditCommand.ReplaceDocument(d), CommandCodes.BadArgs, "more than 512 tracks", "512");
            d = s.Model.Timeline.Clone();
            for (int i = 0; i <= TimelineLimits.MaxEvents; i++) d.Events.Add(new TimelineEvent(Id(0x3000 + i), 0f, Newtonsoft.Json.Linq.JObject.Parse("{\"cmd\": \"timeline.pause\"}")));
            Refused(TimelineEditCommand.ReplaceDocument(d), CommandCodes.BadArgs, "more than 4,096 events", "4096");
            var longLabel = TrackOf(s, 0);
            longLabel.Label = new string('l', 257);
            Refused(TimelineEditCommand.ReplaceTrack(longLabel), CommandCodes.BadArgs, "a label over 256 characters", "label");
            t.Check(Json(s) == state && s.Commands.UndoList.Count == n && s.Commands.Version == v, "TM1 refuse: refusals leave the Timeline, the history and the version as they were");

            // 100,000 keys in all: exactly the limit is allowed, one more refused (also through a one-track edit).
            d = s.Model.Timeline.Clone();
            var many = new TimelineTrack(Id(0x77), TimelineTypes.AnimeSpeed, NodeB);
            for (int i = 0; i < TimelineLimits.MaxKeys - 3; i++) many.Keys.Add(TimelineKey.OfNumber(i * 0.01f, 1f));
            d.Tracks.Add(many);
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceDocument(d, TimelineEditCommand.LabelAddTrack)), "TM1 refuse: 100,000 keys in all are allowed");
            t.Equal(TimelineLimits.MaxKeys, s.Model.Timeline.KeyCount, "TM1 refuse: the key count");
            var plus = TrackOf(s, 1);
            plus.Keys.Add(TimelineKey.OfVector(3f, new Vec3(0f, 0f, 0f)));
            Refused(TimelineEditCommand.ReplaceTrack(plus, TimelineEditCommand.LabelKey), CommandCodes.BadArgs, "a key beyond 100,000 in all", "100000");
            var swap = TrackOf(s, 1);
            swap.Keys[0].Vector = new Vec3(0f, 1f, 0f);
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceTrack(swap, TimelineEditCommand.LabelKey)), "TM1 refuse: a key changed at the limit is allowed");

            // A file's second track of a channel (kept inactive by the reader) stays editable; a new one is refused.
            var fileDoc = TimelineDocument.Parse("{\"duration\": 5, \"tracks\": [{\"id\": \"" + Id(1) + "\", \"type\": \"pos\", \"key\": \"" + NodeA + "\"}, {\"id\": \"" + Id(2) + "\", \"type\": \"pos\", \"key\": \"" + NodeA + "\"}]}");
            var s2 = Kit.Session();
            Kit.Fails(t, Run(s2, TimelineEditCommand.ReplaceDocument(fileDoc.Clone())), CommandCodes.BadArgs, "TM1 refuse: setting a document with two tracks of a channel");
            var fc = new SceneContainer();
            fc.Scene.Timeline = fileDoc;
            s2.Bind(fc);
            var fd = s2.Model.Timeline.Clone();
            fd.Duration = 6f;
            Kit.Ok(t, Run(s2, TimelineEditCommand.ReplaceDocument(fd)), "TM1 refuse: a loaded Timeline with a file's duplicate track: its duration still edits");
            var ft = s2.Model.Timeline.Tracks[1].Clone();
            ft.Keys.Add(TimelineKey.OfVector(1f, new Vec3(1f, 1f, 1f)));
            Kit.Ok(t, Run(s2, TimelineEditCommand.ReplaceTrack(ft, TimelineEditCommand.LabelKey)), "TM1 refuse: and the inactive track's keys too");
        }

        // ---------- the file and the session ----------

        static void FileAndSession(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, s.AddFolder(), "TM1 file: a folder");
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceDocument(Doc(), TimelineEditCommand.LabelBuild)), "TM1 file: build");
            var bytes = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var man = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(bytes).Get("manifest.json").Data));
            var sj = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(bytes).Get("scene.json").Data));
            t.Check((int)man["formatVersion"] == 4 && string.Join(",", sj.Properties().Select(p => p.Name)) == "nodes,scene,timeline", "TM1 file: a scene with a Timeline saves as revision 4, timeline after scene");
            t.Check(Kit.Text(Kit.Bytes(s)).Contains("\"timeline\": {"), "TM1 file: the model's canonical scene.json carries the Timeline");
            string json = Json(s);
            var s2 = Kit.Session(2);
            var l = s2.Load(bytes);
            t.Check(l.Ok && Json(s2) == json && !s2.Commands.IsDirty && s2.Commands.UndoList.Count == 0, "TM1 file: a load binds the Timeline (equal JSON), not modified, no history");
            s2.NewScene();
            t.Check(s2.Model.Timeline == null, "TM1 file: NewScene removes it");
            var noTl = Kit.Session(3);
            noTl.AddFolder();
            var b0 = noTl.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var m0 = Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(b0).Get("manifest.json").Data));
            t.Check((int)m0["formatVersion"] == 1 && !Encoding.UTF8.GetString(ContainerReader.Read(b0).Get("scene.json").Data).Contains("\"timeline\": {"),
                "TM1 file: a scene without one stays revision 1 with no timeline member");
            Kit.Ok(t, Run(s, TimelineEditCommand.Clear()), "TM1 file: clear");
            var bc = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check((int)Newtonsoft.Json.Linq.JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(bc).Get("manifest.json").Data))["formatVersion"] == 1,
                "TM1 file: after clear the next save is revision 1 again");
        }
    }
}
