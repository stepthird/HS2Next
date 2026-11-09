// Scene scripts on the bus and through their file. JE1: ScriptEditCommand: one history entry per operation (Add script, Edit script, Remove script, Cast roles, Load
// script with the Timeline, Prepare script inside the player's transaction, Save edits into script with the Timeline, Borrow), each raising the version with
// SceneChanged("scripts") and, when the Timeline changes, SceneChanged("timeline"); undo and redo restore the scripts and the Timeline bit for bit; a no-op records
// nothing; `undo: false` applies, records nothing and keeps the history; merges with one merge key; the entry's size; a save writes the scripts (revision 5) and a
// load, NewScene and Bind replace them. JF8's command side: the limits and the rules of an edit are refused with bad_args and leave nothing; a scene's Timeline
// with role keys is refused. JF9: the import and export of a .hs2nscript: the export of an import equals the file, build-form tracks and timed commands are turned
// into the stored form (as timeline.build turns them) and the export of that import is a fixed point; the refusals of the format. Synthetic ids and names only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Api.Bridge;
using HS2Next.Scenes.Scripts;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_ScriptEdit
    {
        static string Id(int n) => n.ToString("x32");
        static readonly string NodeA = Id(0x6a1), NodeB = Id(0x6a2), ScriptA = Id(0x6c1), ScriptB = Id(0x6c2);
        static JObject J(string json) => (JObject)DocReader.ParseText(json, "test");

        static string Scripts(SceneSession s) => s.Model.Scripts?.ToJson();
        static string Timeline(SceneSession s) => s.Model.Timeline?.ToJson();

        static CommandResult Run(SceneSession s, ISceneCommand c, string mergeKey = null, HistoryMode mode = HistoryMode.Record) =>
            s.Commands.Execute(c, new ExecuteOptions { Record = mode, MergeKey = mergeKey, Source = CommandSource.Bridge });

        static SceneScript Script(string id, string name)
        {
            var s = new SceneScript { Id = id, Name = name };
            s.Roles.Add(new ScriptRole("a", ScriptRole.KindChara) { Sex = ScriptRole.SexFemale });
            s.Roles.Add(new ScriptRole("b", ScriptRole.KindChara));
            s.Setup.Add(J("{\"cmd\": \"obj.transform\", \"key\": \"$a\", \"pos\": [0, 0, 0]}"));
            s.Prepare.Add(J("{\"cmd\": \"wet.attach\", \"key\": \"$a\"}"));
            var pos = new TimelineTrack(Id(0x6d1), TimelineTypes.Pos, "$a");
            pos.Keys.Add(TimelineKey.OfVector(0f, new Vec3(0f, 0f, 0f)));
            pos.Keys.Add(TimelineKey.OfVector(4f, new Vec3(0f, 0f, 20f)));
            s.Timeline.Tracks.Add(pos);
            var dof = new TimelineTrack(Id(0x6d2), TimelineTypes.Dof);
            dof.Keys.Add(TimelineKey.OfDof(0f, new TimelineDof { Focus = TimelineFocus.OfNode("$b"), Size = 0.5f, Aperture = 0.5f }));
            s.Timeline.Tracks.Add(dof);
            s.Timeline.Duration = 8f;
            s.Timeline.Events.Add(new TimelineEvent(Id(0x6e1), 1f, J("{\"cmd\": \"chara.anime\", \"key\": \"$b\", \"group\": 0, \"category\": 0, \"no\": 1}")));
            return s;
        }

        /// <summary>The Timeline a script loads (role keys replaced by the cast; the test's own substitution) with its marker.</summary>
        static TimelineDocument Compile(SceneScript s)
        {
            var o = s.Timeline.ToJObject();
            o["tracks"][0]["key"] = s.Cast["a"];
            o["tracks"][1]["keys"][0]["v"]["focus"]["node"] = s.Cast["b"];
            o["events"][0]["do"]["key"] = s.Cast["b"];
            var d = TimelineDocument.FromToken(o);
            d.Script = new TimelineScriptMarker(s.Id, d.CompiledHash());
            return d;
        }

        public static void Run(TestRunner t)
        {
            JE1(t);
            NotRecordedAndMerges(t);
            JF8(t);
            FileAndSession(t);
            JF9(t);
        }

        // ---------- JE1: one entry per operation, undo / redo bit for bit ----------

        static void JE1(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, s.AddFolder(), "JE1: a folder (a node for the preparation's step)");
            string folder = s.Model.Roots[0].Id;
            var rec = new EventRecorder(s);
            var states = new List<(string scripts, string timeline)> { (null, null) };
            var labels = new List<string>();
            var timelineChanged = new List<bool>();

            void Step(ISceneCommand c, string label, bool expectTimeline)
            {
                long v = s.Commands.Version;
                int n = s.Commands.UndoList.Count;
                rec.Clear();
                var r = Run(s, c);
                Kit.Ok(t, r, "JE1: " + label);
                t.Check(s.Commands.UndoList.Count == n + 1 && s.Commands.UndoList[0].Label == label, "JE1: " + label + " is one entry labelled «" + label + "»");
                t.Check(s.Commands.Version == v + 1, "JE1: " + label + " raises the version by one");
                var paths = rec.Batches.Count == 1 ? rec.Batches[0].Changes.Where(x => x.Kind == SceneChangeKind.SceneChanged).Select(x => x.Path).ToList() : new List<string>();
                bool tl = paths.Contains(ScriptEditCommand.TimelinePath);
                t.Check(rec.Batches.Count == 1 && tl == expectTimeline && paths.All(p => p == ScriptEditCommand.ChangePath || p == ScriptEditCommand.TimelinePath),
                    "JE1: " + label + " reports SceneChanged(" + string.Join(", ", paths) + ")");
                states.Add((Scripts(s), Timeline(s)));
                labels.Add(label);
                timelineChanged.Add(tl);
            }

            var a = Script(ScriptA, "Duel").ToScene(1);
            Step(ScriptEditCommand.ReplaceScripts(new SceneScriptList(new[] { a }), ScriptEditCommand.LabelAdd), ScriptEditCommand.LabelAdd, false);
            t.Check(s.Model.Scripts.Count == 1 && s.Model.Timeline == null && s.Commands.IsDirty, "JE1: added, the scene is modified, no Timeline yet");

            var edit = s.Model.Scripts.Scripts[0].Clone();
            edit.Name = "Duel 2";
            edit.Description = "renamed";
            Step(ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, edit), ScriptEditCommand.LabelEdit), ScriptEditCommand.LabelEdit, false);

            Step(ScriptEditCommand.ReplaceScripts(s.Model.Scripts.Adding(Script(ScriptB, "Other").ToScene()), ScriptEditCommand.LabelAdd), ScriptEditCommand.LabelAdd, false);

            var cast = s.Model.Scripts.Scripts[0].Clone();
            cast.Cast["a"] = NodeA;
            cast.Cast["b"] = NodeB;
            Step(ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, cast), ScriptEditCommand.LabelCast), ScriptEditCommand.LabelCast, false);

            // Load: the casts are written and the Timeline loaded in one entry.
            var loadScript = s.Model.Scripts.Scripts[0].Clone();
            loadScript.Store = 7;
            string timelineBefore = Timeline(s);
            Step(ScriptEditCommand.ReplaceScriptsAndTimeline(s.Model.Scripts.With(0, loadScript), Compile(loadScript), ScriptEditCommand.LabelLoad), ScriptEditCommand.LabelLoad, true);
            t.Check(timelineBefore != Timeline(s) && s.Model.Timeline.Script.Id == ScriptA && !s.Model.Timeline.IsEditedSinceLoad, "JE1: present: the load changed the Timeline (it carries the marker)");

            // Prepare: the player's transaction holds the preparation's steps and the mark: one entry «Prepare script».
            int nPrep = s.Commands.UndoList.Count;
            long vPrep = s.Commands.Version;
            using (var tx = s.Commands.Begin(ScriptEditCommand.LabelPrepare, new ExecuteOptions { Source = CommandSource.Script }))
            {
                Kit.Ok(t, tx.Set(folder, "transform.pos", new Vec3(1f, 0f, 0f)), "JE1: a preparation step inside");
                var marked = s.Model.Scripts.Scripts[0].Clone();
                marked.Prepared = ScriptText.PreparationMark(marked.Prepare);
                Kit.Ok(t, tx.Execute(ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, marked), ScriptEditCommand.LabelPrepare)), "JE1: the mark inside");
                Kit.Ok(t, tx.Commit(), "JE1: commit the preparation");
            }
            t.Check(s.Commands.UndoList.Count == nPrep + 1 && s.Commands.UndoList[0].Label == ScriptEditCommand.LabelPrepare && s.Commands.Version > vPrep && s.Model.Scripts.Scripts[0].Prepared != null,
                "JE1: the preparation and its mark are one entry «Prepare script»");
            states.Add((Scripts(s), Timeline(s)));
            labels.Add(ScriptEditCommand.LabelPrepare);
            timelineChanged.Add(false);

            // Save edits into the script: the edited Timeline goes back into the script and the marker is renewed, one entry.
            var edited = s.Model.Timeline.Clone();
            edited.Tracks[0].Keys[1].Vector = new Vec3(0f, 0f, 25f);
            Kit.Ok(t, Run(s, TimelineEditCommand.ReplaceDocument(edited, TimelineEditCommand.LabelKey)), "JE1: a Timeline edit after the load");
            states.Add((Scripts(s), Timeline(s)));
            labels.Add(TimelineEditCommand.LabelKey);
            timelineChanged.Add(true);
            t.Check(s.Model.Timeline.IsEditedSinceLoad, "JE1: the Timeline reads as edited since the load");
            var captured = s.Model.Scripts.Scripts[0].Clone();
            captured.Timeline.Tracks[0].Keys[1].Vector = new Vec3(0f, 0f, 25f);
            var renewed = s.Model.Timeline.Clone();
            renewed.Script = new TimelineScriptMarker(captured.Id, renewed.CompiledHash());
            Step(ScriptEditCommand.ReplaceScriptsAndTimeline(s.Model.Scripts.With(0, captured), renewed, ScriptEditCommand.LabelCapture), ScriptEditCommand.LabelCapture, true);
            t.Check(!s.Model.Timeline.IsEditedSinceLoad, "JE1: after «Save edits into script» the Timeline is not edited");

            var borrowed = s.Model.Scripts.Scripts[1].Clone();
            borrowed.Timeline.Events.Add(new TimelineEvent(Id(0x6e9), 2f, J("{\"cmd\": \"chara.face\", \"key\": \"$a\", \"eyes\": 1}")));
            Step(ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(1, borrowed), ScriptEditCommand.LabelBorrow), ScriptEditCommand.LabelBorrow, false);

            // Removing the loaded script: the scripts change, the Timeline stays (the operation removes the marker in a Timeline edit of its own).
            Step(ScriptEditCommand.ReplaceScripts(s.Model.Scripts.Without(0), ScriptEditCommand.LabelRemove), ScriptEditCommand.LabelRemove, false);
            Step(ScriptEditCommand.ReplaceScripts(null, ScriptEditCommand.LabelRemove), ScriptEditCommand.LabelRemove, false);
            t.Check(s.Model.Scripts == null && s.Model.Timeline != null, "JE1: the last script removed: no scripts (null, not an empty list), the Timeline stays");

            // Undo walks back through every state bit for bit, redo forward.
            bool undoOk = true, redoOk = true;
            for (int i = labels.Count - 1; i >= 0; i--)
            {
                Kit.Ok(t, s.Commands.Undo(), "JE1: undo " + labels[i]);
                if (Scripts(s) != states[i].scripts || Timeline(s) != states[i].timeline) { undoOk = false; t.Info("JE1: undo " + labels[i] + " differs"); }
            }
            t.Check(undoOk && s.Model.Scripts == null && s.Model.Timeline == null, "JE1: undo restores each earlier state of the scripts and the Timeline bit for bit (back to none)");
            for (int i = 0; i < labels.Count; i++)
            {
                Kit.Ok(t, s.Commands.Redo(), "JE1: redo " + labels[i]);
                if (Scripts(s) != states[i + 1].scripts || Timeline(s) != states[i + 1].timeline) { redoOk = false; t.Info("JE1: redo " + labels[i] + " differs"); }
            }
            t.Check(redoOk, "JE1: redo restores each later state bit for bit");
            t.Info("JE1: present: " + labels.Count + " entries, " + timelineChanged.Count(x => x) + " of them changed the Timeline (the load among them)");

            // The entry's size: 256 + 2 x the JSON lengths of before and after (the scripts, and the Timeline when it is part of the entry).
            for (int i = 0; i < 4; i++) s.Commands.Undo();   // Remove, Remove, Borrow, Save edits into script
            Kit.Ok(t, s.Commands.Redo(), "JE1: redo «Save edits into script»");
            var e = s.Commands.UndoList[0];
            int ci = labels.IndexOf(ScriptEditCommand.LabelCapture);
            long expect = ScriptEditCommand.BaseBytes + 2L * (states[ci].scripts.Length + states[ci].timeline.Length + states[ci + 1].scripts.Length + states[ci + 1].timeline.Length);
            t.Check(e.Label == ScriptEditCommand.LabelCapture && e.EstimatedBytes == expect, "JE1: the entry's size = 256 + 2 x (before + after JSON lengths) (" + e.EstimatedBytes + ", expected " + expect + ")");

            // A no-op: nothing recorded, the version kept.
            long vn = s.Commands.Version;
            int un = s.Commands.UndoList.Count;
            Kit.NoOp(t, Run(s, ScriptEditCommand.ReplaceScripts(s.Model.Scripts.Clone())), "JE1: an edit to the same scripts");
            Kit.NoOp(t, Run(s, ScriptEditCommand.ReplaceScriptsAndTimeline(s.Model.Scripts.Clone(), s.Model.Timeline.Clone())), "JE1: a load of the same Timeline (a replay)");
            t.Check(s.Commands.Version == vn && s.Commands.UndoList.Count == un, "JE1: a no-op records nothing and keeps the version");
        }

        // ---------- undo: false, merges ----------

        static void NotRecordedAndMerges(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, Run(s, ScriptEditCommand.ReplaceScripts(new SceneScriptList(new[] { Script(ScriptA, "A").ToScene() }), ScriptEditCommand.LabelAdd)), "JE1 undo:false: add");
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            var x = s.Model.Scripts.Scripts[0].Clone();
            x.Name = "quiet";
            Kit.Ok(t, Run(s, ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, x)), null, HistoryMode.None), "JE1 undo:false: an edit");
            t.Check(s.Model.Scripts.Scripts[0].Name == "quiet" && s.Commands.UndoList.Count == n && s.Commands.UndoList[0].Label == ScriptEditCommand.LabelAdd && s.Commands.Version == v + 1,
                "JE1 undo:false: applied, nothing recorded, the history kept (a property set, not structural)");

            // Two renames typed with one merge key: one entry whose undo gives the state before the first.
            string before = Scripts(s);
            n = s.Commands.UndoList.Count;
            for (int i = 1; i <= 3; i++)
            {
                var y = s.Model.Scripts.Scripts[0].Clone();
                y.Name = "typed " + i;
                Kit.Ok(t, Run(s, ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, y)), "rename:" + ScriptA), "JE1 merge: rename " + i);
            }
            t.Check(s.Commands.UndoList.Count == n + 1 && s.Model.Scripts.Scripts[0].Name == "typed 3", "JE1 merge: three renames with one merge key are one entry");
            Kit.Ok(t, s.Commands.Undo(), "JE1 merge: undo");
            t.Check(Scripts(s) == before, "JE1 merge: its undo restores the state before the first rename");
            // A scripts-only edit and a load do not merge, nor do edits without a merge key.
            Kit.Ok(t, s.Commands.Redo(), "JE1 merge: redo");
            n = s.Commands.UndoList.Count;
            var c = s.Model.Scripts.Scripts[0].Clone();
            c.Cast["a"] = NodeA;
            c.Cast["b"] = NodeB;
            Run(s, ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, c), ScriptEditCommand.LabelCast), "same");
            Run(s, ScriptEditCommand.ReplaceScriptsAndTimeline(s.Model.Scripts.Clone(), Compile(s.Model.Scripts.Scripts[0])), "same");
            var z = s.Model.Scripts.Scripts[0].Clone();
            z.Description = "no key";
            Run(s, ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, z)));
            t.Equal(n + 3, s.Commands.UndoList.Count, "JE1 merge: a scripts edit, a load and an edit without a key: separate entries");
        }

        // ---------- JF8's command side: refusals ----------

        static void JF8(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, Run(s, ScriptEditCommand.ReplaceScripts(new SceneScriptList(new[] { Script(ScriptA, "A").ToScene() }), ScriptEditCommand.LabelAdd)), "JF8 cmd: add");
            string before = Scripts(s);
            int n = s.Commands.UndoList.Count;
            long v = s.Commands.Version;
            var refusals = new List<(string what, ISceneCommand cmd, string text)>();
            var many = new SceneScriptList();
            for (int i = 0; i < 65; i++) { var x = Script(Id(0x7000 + i), "S" + i).ToScene(); many.Scripts.Add(x); }
            refusals.Add(("65 scripts", ScriptEditCommand.ReplaceScripts(many), ScriptFindings.Limit));
            SceneScript Edited(Action<SceneScript> e) { var x = s.Model.Scripts.Scripts[0].Clone(); e(x); return x; }
            refusals.Add(("33 roles", ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, Edited(x => { for (int i = 0; i < 31; i++) x.Roles.Add(new ScriptRole("r" + i, ScriptRole.KindItem)); }))), ScriptFindings.Limit));
            refusals.Add(("257 steps", ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, Edited(x => { for (int i = 0; i < 255; i++) x.Setup.Add(J("{\"cmd\": \"ping\"}")); }))), ScriptFindings.Limit));
            refusals.Add(("a name of 129 characters", ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, Edited(x => x.Name = new string('n', 129)))), ScriptFindings.Limit));
            refusals.Add(("a step of the group script", ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, Edited(x => x.Setup.Add(J("{\"cmd\": \"script.play\"}"))))), "script.play"));
            refusals.Add(("a wait of 11 s", ScriptEditCommand.ReplaceScripts(s.Model.Scripts.With(0, Edited(x => x.Prepare.Add(J("{\"wait\": 11}"))))), "wait"));
            refusals.Add(("two scripts of one id", ScriptEditCommand.ReplaceScripts(s.Model.Scripts.Adding(s.Model.Scripts.Scripts[0].Clone())), "twice"));
            refusals.Add(("a scene Timeline with role keys", ScriptEditCommand.ReplaceScriptsAndTimeline(s.Model.Scripts.Clone(), s.Model.Scripts.Scripts[0].Timeline.Clone()), "role keys"));
            var badMarker = new TimelineDocument();
            badMarker.Script = new TimelineScriptMarker(ScriptA, "xyz");
            refusals.Add(("a marker whose hash is not 16 hex", ScriptEditCommand.ReplaceScriptsAndTimeline(s.Model.Scripts.Clone(), badMarker), "marker"));
            foreach (var (what, cmd, text) in refusals)
            {
                var r = Run(s, cmd);
                Kit.Fails(t, r, CommandCodes.BadArgs, "JF8 cmd: " + what);
                t.Check(r.Message != null && r.Message.Contains(text), "JF8 cmd: " + what + ": the message names it (" + r.Message + ")");
            }
            t.Check(Scripts(s) == before && s.Model.Timeline == null && s.Commands.UndoList.Count == n && s.Commands.Version == v, "JF8 cmd: refusals leave nothing: no change, no entry, the version kept");
        }

        // ---------- the file through the session ----------

        static void FileAndSession(TestRunner t)
        {
            var s = Kit.Session();
            Kit.Ok(t, s.AddFolder(), "JE1 file: a folder");
            var a = Script(ScriptA, "A").ToScene();
            a.Cast["a"] = NodeA;
            a.Cast["b"] = NodeB;
            Kit.Ok(t, Run(s, ScriptEditCommand.ReplaceScriptsAndTimeline(new SceneScriptList(new[] { a }), Compile(a), ScriptEditCommand.LabelLoad)), "JE1 file: load a script");
            var bytes = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            var man = JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(bytes).Get("manifest.json").Data));
            var sj = JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(bytes).Get("scene.json").Data));
            t.Check((int)man["formatVersion"] == 5 && string.Join(",", sj.Properties().Select(p => p.Name)) == "nodes,scene,timeline,scripts", "JE1 file: revision 5, scripts after the Timeline");
            var f = new FindingList();
            var s2 = Kit.Session(2);
            var l = s2.Load(bytes);
            t.Check(l.Ok && Scripts(s2) == Scripts(s) && Timeline(s2) == Timeline(s) && !s2.Commands.IsDirty && s2.Commands.UndoList.Count == 0,
                "JE1 file: a load binds the scripts and the Timeline (equal JSON), not modified, no history");
            t.Check(l.Findings.Has(ScriptFindings.CastMissing), "JE1 file: the cast's nodes are not in the scene: script.castMissing at load (" + l.Findings + ")");
            s2.NewScene();
            t.Check(s2.Model.Scripts == null && s2.Model.Timeline == null, "JE1 file: NewScene removes them");
            Kit.Ok(t, Run(s, ScriptEditCommand.ReplaceScripts(null, ScriptEditCommand.LabelRemove)), "JE1 file: remove the script");
            s.Model.Timeline.Script = null;   // (the marker goes with its script: a Timeline edit of the operation's; here directly, outside the history, for the file's revision)
            var b0 = s.ToBytes(Kit.FixedSave(ThumbnailMode.Placeholder));
            t.Check((int)JObject.Parse(Encoding.UTF8.GetString(ContainerReader.Read(b0).Get("manifest.json").Data))["formatVersion"] == 4, "JE1 file: without scripts and marker the next save is revision 4 (the Timeline)");
        }

        // ---------- JF9: import and export ----------

        sealed class Ids : IIdSource
        {
            int n;
            public string NewId() => (0xf000 + ++n).ToString("x32");
        }

        static ScriptBuildContext Build() => new ScriptBuildContext
        {
            Ids = new Ids(),
            ResolveAnime = (key, arg) =>
            {
                int g = (int?)key["group"] ?? 0, c = (int?)key["category"] ?? 0, no = (int?)key["no"] ?? 0;
                return g >= 90 ? null : new AnimeRef((string)key["source"] ?? DocDefaults.VanillaSource, g, c, no);
            },
        };

        /// <summary>A document whose Timeline gives every track type and its timed commands in the build vocabulary (no ids), keys naming roles.</summary>
        static string BuildFormFile() =>
            "{\"format\": \"hs2next.script\", \"version\": 1, \"id\": \"" + Id(0x6cb) + "\", \"name\": \"Build form\", \"roles\": [" +
            "{\"role\": \"a\", \"kind\": \"chara\", \"sex\": \"female\"}, {\"role\": \"b\", \"kind\": \"chara\", \"sex\": \"male\"}, {\"role\": \"p\", \"kind\": \"item\", \"ref\": \"test.mod#3\"}]," +
            "\"setup\": [{\"cmd\": \"obj.transform\", \"key\": \"$a\", \"pos\": [0, 0, 0]}], \"prepare\": []," +
            "\"timeline\": {\"duration\": 10, \"loop\": true, \"autoplay\": false, \"tracks\": [" +
            "{\"type\": \"camera\", \"keys\": [{\"t\": 0, \"target\": [0, 10, 0], \"rot\": [10, 180, 0], \"dist\": 30, \"fov\": 23}, {\"t\": 5, \"dist\": 20, \"ease\": \"linear\"}]}," +
            "{\"type\": \"dof\", \"keys\": [{\"t\": 0, \"focus\": \"$b\", \"sharp\": 0.2, \"full\": 4}, {\"t\": 3, \"focus\": {\"key\": \"$a\", \"bone\": \"cf_J_Head\"}}]}," +
            "{\"type\": \"pos\", \"key\": \"$a\", \"keys\": [{\"t\": 0, \"v\": [0, 0, 0]}, {\"t\": 4, \"v\": [0, 0, 20]}]}," +
            "{\"type\": \"rot\", \"key\": \"$a\", \"bone\": \"cf_J_Neck\", \"keys\": [{\"t\": 1, \"v\": [0, 30, 0]}]}," +
            "{\"type\": \"anime\", \"key\": \"$b\", \"keys\": [{\"t\": 0, \"group\": 0, \"category\": 3, \"no\": 0}]}," +
            "{\"type\": \"eyes\", \"key\": \"$a\", \"raw\": true, \"keys\": [{\"t\": 0, \"v\": 4}]}," +
            "{\"type\": \"color\", \"key\": \"$p\", \"slot\": 0, \"keys\": [{\"t\": 0, \"v\": [1, 0, 0]}]}," +
            "{\"id\": \"" + Id(0x6d9) + "\", \"type\": \"scale\", \"key\": \"$p\", \"bone\": null, \"slot\": null, \"enabled\": true, \"label\": null, \"keys\": [{\"t\": 0.0, \"ease\": \"inout\", \"v\": [1.0, 1.0, 1.0]}]}]," +
            "\"events\": [{\"t\": 2, \"do\": {\"cmd\": \"fx.shoot\", \"from\": {\"key\": \"$p\"}, \"target\": {\"key\": \"$b\"}}, \"note\": \"shot\"}," +
            "{\"t\": 0.5, \"do\": {\"cmd\": \"chara.anime\", \"key\": \"$a\", \"group\": 0}}]}}";

        static void JF9(TestRunner t)
        {
            // A stored-form file: the export of its import equals it, byte for byte.
            var hand = Script(ScriptA, "Stored").ToDocument();
            hand.Map = new ScriptMap("test.map#1", "Map");
            var file = hand.ToDocumentBytes();
            var f = new FindingList();
            var imported = ScriptFormat.Parse(file, f, Build());
            var export = ScriptFormat.ToFileBytes(imported);
            t.Check(export.SequenceEqual(file) && f.Count == 0, "JF9: a stored-form file: the export of its import equals the file (" + file.Length + " bytes, " + f + ")");
            string dir = Path.Combine(t.TempDir ?? Path.GetTempPath(), "jf9");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "stored.hs2nscript");
            long written = ScriptFormat.WriteFile(imported, path);
            t.Check(written == file.Length && File.ReadAllBytes(path).SequenceEqual(file) && ScriptFormat.ToFileBytes(ScriptFormat.ReadFile(path, null, Build())).SequenceEqual(file),
                "JF9: written to a file and read from it, the same bytes");
            var scene = imported.ToScene(3);
            scene.Cast["a"] = NodeA;
            t.Check(ScriptFormat.ToFileBytes(scene).SequenceEqual(file), "JF9: a scene script exports without its cast, mark and store number");

            // A build-form file: converted, and the export of the converted import is a fixed point.
            var raw = J(BuildFormFile());
            int buildForm = ScriptFormat.BuildFormCount(raw);
            var fb = new FindingList();
            var ctx = Build();
            var conv = ScriptFormat.Parse(Encoding.UTF8.GetBytes(BuildFormFile()), fb, ctx);
            t.Info("JF9: present: " + buildForm + " build-form tracks and timed commands converted (" + conv.Timeline.Tracks.Count + " tracks, " + conv.Timeline.Events.Count + " events)");
            t.Check(buildForm >= 1 && buildForm == 9, "JF9: present: at least one build-form track converted (" + buildForm + " elements without id)");
            t.Check(fb.Count == 0 && ctx.Warnings.Count == 0 && conv.Problem() == null, "JF9: converted with no finding and no warning (" + fb + "; " + string.Join("; ", ctx.Warnings) + ")");
            var e1 = ScriptFormat.ToFileBytes(conv);
            var e2 = ScriptFormat.ToFileBytes(ScriptFormat.Parse(e1, null, Build()));
            t.Check(e1.SequenceEqual(e2), "JF9: the export of the converted import, imported and exported again, is the same file");
            var tl = conv.Timeline;
            t.Check(tl.Tracks.Select(x => x.Type).SequenceEqual(new[] { "camera", "dof", "pos", "rot", "anime", "eyes", "color", "scale" }) && tl.Tracks[7].Id == Id(0x6d9)
                && tl.Tracks[2].Key == "$a" && tl.Tracks[1].Keys[0].Dof.Focus.Node == "$b" && tl.Tracks[1].Keys[1].Dof.Focus.Bone == "cf_J_Head" && tl.Tracks[4].Keys[0].Anime.Category == 3
                && tl.Tracks[5].Keys[0].Integer == 4 && tl.Tracks[0].Keys[1].Camera.Dist == 20f && tl.Tracks[0].Keys[1].Camera.Fov == 23f,
                "JF9: the tracks in order with their roles, the stored track kept with its id, carried camera values, the animation row, the raw eye pattern");
            t.Check(tl.Events.Count == 2 && tl.Events[0].T == 0.5f && tl.Events[1].Note == "shot" && tl.Events.All(x => JsonText.IsId(x.Id)), "JF9: the timed commands with ids, sorted by time, the note kept");
            // The same input through timeline.build's own conversion (node keys in place of roles) stores the same values.
            var nodes = new Dictionary<string, NodeKind> { ["$a"] = NodeKind.Chara, ["$b"] = NodeKind.Chara, ["$p"] = NodeKind.Item };
            var ref0 = TimelineBuildInput.ParseTracks(raw["timeline"]["tracks"].Take(7).Aggregate(new JArray(), (acc, x) => { acc.Add(x.DeepClone()); return acc; }), 10f,
                new ScriptBuildHost(conv.Roles, Build()), new Ids(), false, new List<string>());
            bool same = true;
            for (int i = 0; i < 7; i++) same &= ref0[i].ToJson(true).Replace(ref0[i].Id, "") == tl.Tracks[i].ToJson(true).Replace(tl.Tracks[i].Id, "");
            t.Check(same, "JF9: each converted track equals TimelineBuildInput's stored form of the same input (ids aside)");

            // Refusals.
            BridgeException Bad(string json, ScriptBuildContext c = null)
            {
                try { ScriptFormat.Parse(Encoding.UTF8.GetBytes(json), null, c ?? Build()); }
                catch (BridgeException e) { return e; }
                return null;
            }
            string Swap(string a, string b) => BuildFormFile().Replace(a, b);
            var eq = Bad(Swap("{\"t\": 4, \"v\": [0, 0, 20]}", "{\"t\": 0, \"v\": [0, 0, 20]}"));
            t.Check(eq != null && eq.Error.Code == BridgeCodes.BadArgs && eq.Message.Contains("key times must differ"), "JF9: two keys at one time refused as timeline.build refuses them (" + eq?.Message + ")");
            var undeclared = Bad(Swap("\"type\": \"pos\", \"key\": \"$a\"", "\"type\": \"pos\", \"key\": \"$zz\""));
            t.Check(undeclared != null && undeclared.Error.Code == BridgeCodes.NotFound, "JF9: a build-form track naming no role: not_found (" + undeclared?.Message + ")");
            var eyes = Bad(Swap("\"raw\": true, ", ""));
            t.Check(eyes != null && eyes.Error.Code == BridgeCodes.BadArgs && eyes.Message.Contains("raw: true"), "JF9: an eye position needs raw: true in a script (" + eyes?.Message + ")");
            var wrongKind = Bad(Swap("\"type\": \"anime\", \"key\": \"$b\"", "\"type\": \"anime\", \"key\": \"$p\""));
            t.Check(wrongKind != null && wrongKind.Error.Code == BridgeCodes.WrongKind, "JF9: an animation track on a prop role: wrong_kind (" + wrongKind?.Message + ")");
            var noCtx = Bad(BuildFormFile(), null);
            t.Check(noCtx == null, "JF9: (the default context converts)");
            BridgeException NoContext() { try { ScriptFormat.Parse(Encoding.UTF8.GetBytes(BuildFormFile())); } catch (BridgeException e) { return e; } return null; }
            t.Check(NoContext()?.Error.Code == BridgeCodes.BadArgs, "JF9: build-form tracks without a build context: bad_args");
            var big = new byte[ScriptLimits.MaxFileBytes + 1];
            BridgeException Big() { try { ScriptFormat.Parse(big); } catch (BridgeException e) { return e; } return null; }
            t.Check(Big()?.Message.Contains(ScriptFindings.Limit) == true, "JF9: a file over 16 MiB: bad_args naming script.limit");
            ScriptRefusedException Refused(string json) { try { ScriptFormat.Parse(Encoding.UTF8.GetBytes(json), null, Build()); } catch (ScriptRefusedException e) { return e; } return null; }
            var newer = Refused(Swap("\"version\": 1", "\"version\": 2"));
            t.Check(newer?.Code == ScriptFindings.NewerVersion && newer.Version == 2, "JF9: a newer version is refused before its tracks are read: script.newerVersion 2");
            t.Check(Refused(Swap("\"format\": \"hs2next.script\"", "\"format\": \"other\""))?.Code == ScriptFindings.BadFormat, "JF9: another format: script.badFormat");
            var noId = J(BuildFormFile());
            noId.Remove("id");
            var a1 = ScriptFormat.Parse(Encoding.UTF8.GetBytes(noId.ToString()), new FindingList(), Build());
            var a2 = ScriptFormat.Parse(Encoding.UTF8.GetBytes(noId.ToString()), new FindingList(), Build());
            t.Check(JsonText.IsId(a1.Id) && a1.Id == a2.Id, "JF9: a document without id gets one derived from its bytes, the same on every read");
        }
    }
}
