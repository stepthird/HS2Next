// Scene scripts: playing a scene script and stopping it. Needs the app (its session, runtime and bridge host).
//   Play, on the main thread: refused while a recording runs that this call did not start (busy), while another play runs (busy), or while
//   the Timeline holds edits of another script (bad_args, unless discard). The Timeline is stopped and the slots of the script commands of
//   an earlier play are released (every play starts from the depth of field and the camera the scene shows). Then: (1) cast: the map loaded
//   when it differs (with load), the character roles by their cast entry, their hint's name, or loaded from the hint's card (with load),
//   the prop roles by their cast entry, their row and parent, or added, parented and placed (with load); a role left uncast refuses the play
//   (bad_args, detail {uncast}) unless partial. (2) preparation, when due (prepare "auto" and the mark of the substituted steps differs from
//   the scene script's, or "always"): the steps in order inside one history entry «Prepare script»; the mark is written when every step
//   succeeded. (3) set-up: the steps in order with undo false (no entry). (4) the timeline loaded per the table, one entry «Load
//   script» when the Timeline or the cast changes. (5) play from `from`, or record one pass with record.start {timeline: true}.
//   Steps run inline through the bridge host (BridgeServer.RunNow: the registry, gates, argument checks and handlers of the bridge, on the
//   main thread): a play is itself a bridge command that may answer later, and the bridge's queue waits for it, so a queued step would wait
//   behind it. A step whose command may answer later (the commands flagged defers, batch) fails unsent, unsupported, "a step cannot run
//   a command that answers later", and is counted. A wait step, a map load and a
//   step that follows them continue at a later frame through the bridge's frame clock. A failed step (a reply with ok false) is recorded
//   with its phase, index, command, code and message and the play goes on; strict stops before the timeline is loaded and answers the
//   step's code with detail {phase, index}. A step or timed command that names an uncast or unknown role is skipped and counted.
//   Stop: the Timeline stopped, the slots of the script commands released; with reset, ragdoll.release for each cast character with a body
//   and wet.dry {puddles: true} for each cast character with a stain, all undo false.
using System;
using System.Collections;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.Bridge;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using HS2Next.Screenshots;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Scripts
{
    /// <summary>The arguments of a play (script.play's).</summary>
    public sealed class ScriptPlayOptions
    {
        public const string PrepareAuto = "auto", PrepareAlways = "always", PrepareNever = "never";

        /// <summary>The play head to start at (seconds); ignored when recording (a recording plays from 0).</summary>
        public double From;
        /// <summary>auto, always or never.</summary>
        public string Prepare = PrepareAuto;
        /// <summary>Load the map and the roles that cannot be found.</summary>
        public bool Load = true;
        /// <summary>Play with roles left uncast: what names them is skipped.</summary>
        public bool Partial;
        /// <summary>Stop at the first failed step (before the timeline is loaded).</summary>
        public bool Strict;
        /// <summary>Load the fresh compile over a Timeline of this script edited since it was loaded.</summary>
        public bool Reload;
        /// <summary>Replace a Timeline holding edits of another script.</summary>
        public bool Discard;
        /// <summary>record.start's arguments: record one pass instead of playing (null: play).</summary>
        public JObject Record;
        /// <summary>False: the play's own history entries («Prepare script», «Load script») are applied and not recorded.</summary>
        public bool Undo = true;
        /// <summary>The source of the play's history entries.</summary>
        public CommandSource Source = CommandSource.Bridge;
    }

    /// <summary>One failed step of a play.</summary>
    public sealed class ScriptStepFailure
    {
        public string Phase;
        public int Index;
        public string Cmd;
        public string Code;
        public string Message;

        public JObject ToJson() => new JObject { ["phase"] = Phase, ["index"] = Index, ["cmd"] = Cmd, ["code"] = Code, ["message"] = Message };
    }

    /// <summary>The result of a play (script.play's reply), or its refusal.</summary>
    public sealed class ScriptPlayResult
    {
        public const string PhaseCast = "cast", PhasePrepare = "prepare", PhaseSetup = "setup", PhaseLoad = "load", PhasePlay = "play";

        public string Script;
        public string Name;
        public readonly SortedDictionary<string, string> Roles = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public bool Loaded;
        public bool Prepared;
        public int SetupSent, SetupFailed, PrepareSent, PrepareFailed;
        public int Skipped;
        public readonly List<ScriptStepFailure> Failures = new List<ScriptStepFailure>();
        public string State;
        public double Time;
        /// <summary>record.start's reply (without ok and cmd), when the play records.</summary>
        public JObject Record;
        public readonly List<string> Warnings = new List<string>();
        /// <summary>The refusal or the stop of a strict play; null for a play that ran.</summary>
        public BridgeError Error;
        /// <summary>The phases in the order they ran (for the checks order).</summary>
        public readonly List<string> Order = new List<string>();
        /// <summary>The order of the cast roles' sources: role → "cast", "found", "loaded".</summary>
        public readonly SortedDictionary<string, string> CastBy = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public bool Ok => Error == null;

        /// <summary>The reply members in the order (+ record), as script.play answers.</summary>
        public JObject ToReply()
        {
            var roles = new JObject();
            foreach (var kv in Roles) roles[kv.Key] = kv.Value;
            var failures = new JArray();
            foreach (var f in Failures) failures.Add(f.ToJson());
            var o = new JObject
            {
                ["script"] = Script,
                ["roles"] = roles,
                ["loaded"] = Loaded,
                ["prepared"] = Prepared,
                ["setup"] = new JObject { ["sent"] = SetupSent, ["failed"] = SetupFailed },
                ["prepare"] = new JObject { ["sent"] = PrepareSent, ["failed"] = PrepareFailed },
                ["skipped"] = Skipped,
                ["failures"] = failures,
                ["state"] = State,
                ["time"] = Time,
            };
            if (Record != null) o["record"] = Record;
            return o;
        }
    }

    /// <summary>A play in progress or done.</summary>
    public sealed class ScriptPlayRun
    {
        Action<ScriptPlayRun> done;

        internal ScriptPlayRun(ScriptPlayResult r) { Result = r; }

        /// <summary>The result (filled while the play runs; final once <see cref="IsDone"/>).</summary>
        public ScriptPlayResult Result { get; }
        public bool IsDone { get; private set; }

        /// <summary>Runs <paramref name="a"/> once the play is done (at once when it is done already).</summary>
        public void WhenDone(Action<ScriptPlayRun> a)
        {
            if (a == null) return;
            if (IsDone) { a(this); return; }
            done += a;
        }

        internal void Finish()
        {
            if (IsDone) return;
            IsDone = true;
            var d = done;
            done = null;
            try { d?.Invoke(this); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[script] a play's completion threw: " + e); }
        }
    }

    /// <summary>The result of a stop (script.stop's reply).</summary>
    public sealed class ScriptStopResult
    {
        public string State;
        /// <summary>Slots of the script commands released.</summary>
        public int Released;
        /// <summary>Cast characters whose body was released.</summary>
        public int Ragdolls;
        /// <summary>Cast characters dried.</summary>
        public int Dried;
        public readonly List<string> Warnings = new List<string>();

        public JObject ToReply() => new JObject { ["state"] = State, ["released"] = Released, ["ragdolls"] = Ragdolls, ["dried"] = Dried };
    }

    public static class ScriptPlayer
    {
        /// <summary>The longest a play waits for a map load (seconds of real time).</summary>
        public const double MapWaitSeconds = 600;
        /// <summary>The name of the scene script a plain Timeline is kept as before a script replaces it.</summary>
        public const string KeptTimelineName = "Timeline";

        // ------------------------------------------------------------ play

        /// <summary>
        /// Starts playing the scene script <paramref name="scriptId"/> of <paramref name="app"/>'s scene. A refusal before anything changes
        /// throws <see cref="BridgeException"/>; a refusal found later (an uncast role after loading, a strict stop) is the run's
        /// <see cref="ScriptPlayResult.Error"/>. The run is done at return unless a wait step or a map load holds it; it then goes on at later
        /// frames of the bridge's clock.
        /// </summary>
        public static ScriptPlayRun Play(AppCore app, string scriptId, ScriptPlayOptions o = null)
        {
            o ??= new ScriptPlayOptions();
            var ctx = Context.Of(app);
            var model = ctx.Session.Model;
            var script = model.Scripts?.Find(scriptId) ?? throw new BridgeException(BridgeError.NotFound("no scene script " + scriptId, "script"));
            if (script.IsOpaque) throw new BridgeException(BridgeError.BadArgs("script", null, "script " + script.Id + " is of a newer format and cannot be played"));
            if (o.Prepare != ScriptPlayOptions.PrepareAuto && o.Prepare != ScriptPlayOptions.PrepareAlways && o.Prepare != ScriptPlayOptions.PrepareNever)
                throw new BridgeException(BridgeError.BadArgs("prepare", "auto, always or never"));
            if (FrameRecorder.IsBusy) throw FrameRecorder.BusyError();
            var pb = ctx.Playback;
            if (pb.Running != null && !pb.Running.IsDone) throw new BridgeException(BridgeError.Busy("a scene script is being played (its waits and loads are not over)"));
            var tl = model.Timeline;
            if (tl?.Script != null && tl.Script.Id != script.Id && tl.IsEditedSinceLoad && !o.Discard)
            {
                var other = model.Scripts?.Find(tl.Script.Id);
                throw new BridgeException(BridgeError.BadArgs("discard", null, "the Timeline holds edits of script " + (other?.Name ?? tl.Script.Id) + ": script.capture it, or discard: true"));
            }
            var run = new ScriptPlayRun(new ScriptPlayResult { Script = script.Id, Name = script.Name });
            pb.Begin(run, script.Id);
            new Driver(ctx, run, Steps(ctx, script.Id, o, run.Result)).Start();
            return run;
        }

        /// <summary>The play as a sequence: null = continue at the next frame of the bridge's clock.</summary>
        static IEnumerator Steps(Context ctx, string scriptId, ScriptPlayOptions o, ScriptPlayResult res)
        {
            var session = ctx.Session;
            var model = session.Model;
            var pb = ctx.Playback;
            var tlp = ctx.Runtime.GetPart<TimelinePlayback>();

            // the Timeline stopped and the script commands' slots of an earlier play released
            try { tlp?.Player.Stop(); } catch (Exception e) { res.Warnings.Add("the Timeline did not stop: " + e.Message); }
            pb.ReleaseCommandSlots(true);

            // (1) cast
            res.Order.Add(ScriptPlayResult.PhaseCast);
            var script = model.Scripts?.Find(scriptId);
            if (script == null) { res.Error = BridgeError.NotFound("the scene script " + scriptId + " is gone", "script"); yield break; }
            if (script.Map?.Ref != null && !string.Equals(script.Map.Ref, model.Settings.Map.Ref, StringComparison.Ordinal))
            {
                if (o.Load)
                {
                    var r = session.SetMap(script.Map.Ref, new ExecuteOptions { Record = o.Undo ? HistoryMode.Record : HistoryMode.None, Source = o.Source, Label = "Map" });
                    if (r.Status == CommandStatus.Error) res.Warnings.Add("the script's map " + script.Map.Ref + " did not load: " + r.Code + ": " + r.Message);
                    ctx.Runtime.ApplyNow();
                    double until = ctx.Realtime + MapWaitSeconds;
                    while (ctx.MapBusy && ctx.Realtime < until) yield return null;
                    if (ctx.MapBusy) res.Warnings.Add("the script's map " + script.Map.Ref + " was still loading after " + MapWaitSeconds + " s");
                    if (ctx.Disposed) { res.Error = Gone(); yield break; }
                    script = model.Scripts?.Find(scriptId);
                    if (script == null) { res.Error = BridgeError.NotFound("the scene script " + scriptId + " is gone", "script"); yield break; }
                }
                else res.Warnings.Add("the script was made on map " + (script.Map.Name ?? script.Map.Ref) + "; the scene shows another (load: true loads it)");
            }
            var casting = new ScriptCasting();
            ScriptRoles.CastCharacters(script, model, casting);
            if (o.Load)
                foreach (var role in script.Roles)
                {
                    if (role == null || !role.IsChara || casting.IsCast(role.Role) || string.IsNullOrEmpty(role.Hint?.Card)) continue;
                    var req = new JObject { ["cmd"] = "chara.load", ["path"] = role.Hint.Card };
                    if (role.Sex != null) req["sex"] = role.Sex;
                    if (!o.Undo) req[EnvelopeFields.Undo] = false;
                    var reply = ctx.RunNow(req, null);
                    string key = KeyOf(reply);
                    if (reply.Value<bool?>("ok") == true && key != null) casting.Set(role.Role, key, ScriptCastSource.Loaded);
                    else res.Warnings.Add("role " + role.Role + ": its card " + role.Hint.Card + " did not load: " + Describe(reply));
                }
            ScriptRoles.CastItems(script, model, casting);
            if (o.Load) LoadItems(ctx, script, casting, o, res);
            foreach (var w in casting.Warnings) res.Warnings.Add(w);
            foreach (var kv in casting.Cast)
            {
                res.Roles[kv.Key] = kv.Value;
                res.CastBy[kv.Key] = casting.Source[kv.Key] == ScriptCastSource.Cast ? "cast" : casting.Source[kv.Key] == ScriptCastSource.Found ? "found" : "loaded";
            }
            var uncast = casting.Uncast(script);
            if (uncast.Count > 0 && !o.Partial)
            {
                res.Error = new BridgeError(BridgeCodes.BadArgs, "roles not cast: " + string.Join(", ", uncast) + " (cast them with script.cast, load them with load: true, or play with partial: true)",
                    new BridgeErrorDetail { Arg = "script", Extra = new Dictionary<string, object> { ["uncast"] = uncast.ToArray() } });
                yield break;
            }
            foreach (var u in uncast) res.Warnings.Add("role " + u + " is not cast: what names it is skipped");
            var declared = ScriptCompiler.Declared(script);
            var cast = new Dictionary<string, string>(casting.Cast, StringComparer.Ordinal);

            // (2) preparation
            var prepared = new List<JObject>();
            foreach (var step in script.Prepare) prepared.Add(ScriptSubst.Request(step, declared, cast, true));
            string mark = ScriptSubst.Mark(prepared);
            // a script without preparation steps has nothing to prepare (no entry, no mark)
            bool due = script.Prepare.Count > 0 && (o.Prepare == ScriptPlayOptions.PrepareAlways || (o.Prepare == ScriptPlayOptions.PrepareAuto && mark != script.Prepared));
            if (due)
            {
                res.Order.Add(ScriptPlayResult.PhasePrepare);
                res.Prepared = true;
                Transaction tx = null;
                bool stopped = false;
                try
                {
                    if (o.Undo)
                    {
                        tx = session.Commands.Begin(ScriptEditCommand.LabelPrepare, new ExecuteOptions { Record = HistoryMode.Record, Source = o.Source, Label = ScriptEditCommand.LabelPrepare });
                        if (!tx.IsOpen) { res.Error = new BridgeError(tx.Failure?.Code ?? BridgeCodes.Busy, "the preparation cannot start: " + (tx.Failure?.Message ?? "the bus is busy")); yield break; }
                    }
                    bool allOk = true;
                    for (int i = 0; i < script.Prepare.Count && !stopped; i++)
                    {
                        var step = script.Prepare[i];
                        if (ScriptSteps.IsWait(step))
                        {
                            double until = ctx.Realtime + ScriptSteps.WaitOf(step);
                            do yield return null; while (ctx.Realtime < until && !ctx.Disposed);
                            if (ctx.Disposed) { res.Error = Gone(); yield break; }
                            continue;
                        }
                        var sr = new ScriptSubstResult();
                        var sent = ScriptSubst.Request(step, declared, cast, true, sr);
                        if (!sr.Complete) { Skip(res, sr); allOk = false; continue; }
                        sent[EnvelopeFields.Undo] = o.Undo;
                        res.PrepareSent++;
                        var reply = ctx.AnswersLater(sent) ? DeferringStep(sent) : ctx.RunNow(sent, tx);
                        if (reply.Value<bool?>("ok") != true)
                        {
                            res.PrepareFailed++;
                            allOk = false;
                            res.Failures.Add(Failure(ScriptPlayResult.PhasePrepare, i, sent, reply));
                            if (o.Strict) stopped = true;
                        }
                    }
                    if (allOk && !stopped)
                    {
                        script = model.Scripts?.Find(scriptId) ?? script;
                        if (script.Prepared != mark)
                        {
                            var after = WithScript(model, script.Id, s => s.Prepared = mark);
                            var cmd = ScriptEditCommand.ReplaceScripts(after, ScriptEditCommand.LabelPrepare);
                            var r = tx != null ? tx.Execute(cmd) : session.Commands.Execute(cmd, new ExecuteOptions { Record = HistoryMode.None, Source = o.Source });
                            if (r.Status == CommandStatus.Error) res.Warnings.Add("the preparation's mark was not written: " + r.Code + ": " + r.Message);
                        }
                    }
                    if (tx != null && tx.IsOpen)
                    {
                        var c = tx.Commit();
                        if (c.Status == CommandStatus.Error) { tx.Cancel(); res.Warnings.Add("the preparation's entry was not recorded: " + c.Code + ": " + c.Message); }
                    }
                }
                finally
                {
                    // never leave the bus with the preparation's transaction open (an error, the app ending during a wait)
                    if (tx != null && tx.IsOpen && tx.Commit().Status == CommandStatus.Error) tx.Cancel();
                }
                if (stopped)
                {
                    var f = res.Failures[res.Failures.Count - 1];
                    res.Error = new BridgeError(f.Code ?? BridgeCodes.Internal, "preparation step " + f.Index + " (" + f.Cmd + ") failed: " + f.Message,
                        new BridgeErrorDetail { Phase = ScriptPlayResult.PhasePrepare, Extra = new Dictionary<string, object> { ["phase"] = ScriptPlayResult.PhasePrepare, ["index"] = f.Index } });
                    yield break;
                }
            }

            // (3) set-up
            res.Order.Add(ScriptPlayResult.PhaseSetup);
            script = model.Scripts?.Find(scriptId) ?? script;
            for (int i = 0; i < script.Setup.Count; i++)
            {
                var step = script.Setup[i];
                if (ScriptSteps.IsWait(step))
                {
                    double until = ctx.Realtime + ScriptSteps.WaitOf(step);
                    do yield return null; while (ctx.Realtime < until && !ctx.Disposed);
                    if (ctx.Disposed) { res.Error = Gone(); yield break; }
                    continue;
                }
                var sr = new ScriptSubstResult();
                var sent = ScriptSubst.Request(step, declared, cast, true, sr);
                if (!sr.Complete) { Skip(res, sr); continue; }
                sent[EnvelopeFields.Undo] = false;
                res.SetupSent++;
                var reply = ctx.AnswersLater(sent) ? DeferringStep(sent) : ctx.RunNow(sent, null);
                if (reply.Value<bool?>("ok") != true)
                {
                    res.SetupFailed++;
                    res.Failures.Add(Failure(ScriptPlayResult.PhaseSetup, i, sent, reply));
                    if (o.Strict)
                    {
                        res.Error = new BridgeError(reply.Value<string>("code") ?? BridgeCodes.Internal, "set-up step " + i + " (" + ScriptSteps.CommandOf(sent) + ") failed: " + reply.Value<string>("error"),
                            new BridgeErrorDetail { Phase = ScriptPlayResult.PhaseSetup, Extra = new Dictionary<string, object> { ["phase"] = ScriptPlayResult.PhaseSetup, ["index"] = i } });
                        yield break;
                    }
                }
            }

            // (4) load
            res.Order.Add(ScriptPlayResult.PhaseLoad);
            script = model.Scripts?.Find(scriptId) ?? script;
            Load(ctx, script, cast, o, res);
            if (res.Error != null) yield break;

            // (5) play or record
            res.Order.Add(ScriptPlayResult.PhasePlay);
            if (tlp == null) { res.Error = new BridgeError(BridgeCodes.Unavailable, "the scene has no Timeline playback"); yield break; }
            if (o.Record != null)
            {
                var req = (JObject)o.Record.DeepClone();
                req.Remove(EnvelopeFields.Id);
                req["cmd"] = "record.start";
                req["timeline"] = true;
                var reply = ctx.RunNow(req, null);
                if (reply.Value<bool?>("ok") != true)
                {
                    res.Error = new BridgeError(reply.Value<string>("code") ?? BridgeCodes.Internal, "the recording did not start: " + reply.Value<string>("error"));
                    yield break;
                }
                var rec = (JObject)reply.DeepClone();
                rec.Remove("ok");
                rec.Remove("cmd");
                res.Record = rec;
                pb.WatchRecording(FrameRecorder.Current);
            }
            else
            {
                tlp.Service.Play(o.From);
            }
            res.State = StateName(tlp.Player.State);
            res.Time = tlp.Player.Time;
        }

        static BridgeError Gone() => new BridgeError(BridgeCodes.Unavailable, "the app or its scene ended during the play");

        static void Skip(ScriptPlayResult res, ScriptSubstResult sr)
        {
            res.Skipped++;
            foreach (var u in sr.UnknownRoles)
            {
                string w = "a step names \"$" + u + "\", which is no role of the script: skipped";
                if (!res.Warnings.Contains(w)) res.Warnings.Add(w);
            }
        }

        static ScriptStepFailure Failure(string phase, int index, JObject sent, JObject reply) => new ScriptStepFailure
        {
            Phase = phase, Index = index, Cmd = ScriptSteps.CommandOf(sent),
            Code = reply.Value<string>("code") ?? BridgeCodes.Internal, Message = reply.Value<string>("error") ?? "",
        };

        /// <summary>Path (3) of the prop roles: item.add, then item.parent onto the parent role's node, then obj.transform (each its own entry, as the commands make them).</summary>
        static void LoadItems(Context ctx, SceneScript script, ScriptCasting casting, ScriptPlayOptions o, ScriptPlayResult res)
        {
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var role in script.Roles)
                {
                    if (role == null || !role.IsItem || casting.IsCast(role.Role) || string.IsNullOrEmpty(role.Ref)) continue;
                    string parentNode = null;
                    if (role.Parent?.Role != null && !casting.Cast.TryGetValue(role.Parent.Role, out parentNode)) continue;
                    var add = new JObject { ["cmd"] = "item.add", ["ref"] = role.Ref };
                    if (!o.Undo) add[EnvelopeFields.Undo] = false;
                    var reply = ctx.RunNow(add, null);
                    string key = KeyOf(reply);
                    if (reply.Value<bool?>("ok") != true || key == null) { res.Warnings.Add("role " + role.Role + ": item " + role.Ref + " was not added: " + Describe(reply)); continue; }
                    foreach (var w in reply["warnings"] as JArray ?? new JArray()) res.Warnings.Add("role " + role.Role + ": " + (string)w);
                    casting.Set(role.Role, key, ScriptCastSource.Loaded);
                    progress = true;
                    if (parentNode != null)
                    {
                        var parentIsChara = ctx.Session.Model.Find(parentNode)?.Kind == NodeKind.Chara;
                        var par = new JObject { ["cmd"] = "item.parent", ["key"] = key };
                        if (parentIsChara)
                        {
                            par["chara"] = parentNode;
                            if (!string.IsNullOrEmpty(role.Parent.Point)) par["point"] = role.Parent.Point;
                        }
                        else par["parent"] = parentNode;
                        if (!o.Undo) par[EnvelopeFields.Undo] = false;
                        var pr = ctx.RunNow(par, null);
                        if (pr.Value<bool?>("ok") != true) res.Warnings.Add("role " + role.Role + ": not parented: " + Describe(pr));
                    }
                    var tr = new JObject
                    {
                        ["cmd"] = "obj.transform", ["key"] = key,
                        ["pos"] = new JArray(role.Pos.X, role.Pos.Y, role.Pos.Z), ["rot"] = new JArray(role.Rot.X, role.Rot.Y, role.Rot.Z), ["scale"] = new JArray(role.Scale.X, role.Scale.Y, role.Scale.Z),
                    };
                    if (!o.Undo) tr[EnvelopeFields.Undo] = false;
                    var t = ctx.RunNow(tr, null);
                    if (t.Value<bool?>("ok") != true) res.Warnings.Add("role " + role.Role + ": not placed: " + Describe(t));
                }
            }
        }

        /// <summary>
        /// Step 4 (the table): the fresh compile loaded, the Timeline kept (a replay, or edits), or a plain Timeline kept as a new scene script
        /// «Timeline» before the script replaces it; the casts written; one entry «Load script» when anything changes.
        /// </summary>
        static void Load(Context ctx, SceneScript script, Dictionary<string, string> cast, ScriptPlayOptions o, ScriptPlayResult res)
        {
            var session = ctx.Session;
            var model = session.Model;
            ScriptCompileResult fresh;
            try { fresh = ScriptCompiler.Compile(script, cast); }
            catch (SaveFormatException e) { res.Error = new BridgeError(BridgeCodes.BadArgs, "the script's timeline cannot be loaded: " + e.Message); return; }
            res.Skipped += fresh.Skipped;
            foreach (var u in fresh.UnknownRoles)
            {
                string w = "the timeline names \"$" + u + "\", which is no role of the script: what names it is left out";
                if (!res.Warnings.Contains(w)) res.Warnings.Add(w);
            }
            var tl = model.Timeline;
            TimelineDocument load = null;
            SceneScript keptAsScript = null;
            if (tl == null) load = fresh.Timeline;
            else if (tl.Script == null)
            {
                // a Timeline that came from no script is kept as a new scene script first
                var keep = tl.Clone();
                keep.Script = null;
                keep.RoleKeys = true;
                keptAsScript = new SceneScript { Id = session.NewId(), Name = KeptTimelineName, Timeline = keep };
                load = fresh.Timeline;
                res.Warnings.Add("the scene's Timeline came from no script: it is kept as the scene script «" + KeptTimelineName + "» (" + keptAsScript.Id + ")");
            }
            else if (tl.Script.Id == script.Id)
            {
                if (!tl.IsEditedSinceLoad)
                {
                    if (fresh.Compiled != tl.Script.Compiled) load = fresh.Timeline;
                }
                else if (o.Reload) load = fresh.Timeline;
                else res.Warnings.Add("the Timeline was edited after the script was loaded: kept (reload: true discards the edits, script.capture keeps them in the script)");
            }
            else load = fresh.Timeline;   // another script's (its edits were refused or discarded before the play began)

            var castAfter = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (script.Cast != null) foreach (var kv in script.Cast) castAfter[kv.Key] = kv.Value;
            foreach (var kv in cast) castAfter[kv.Key] = kv.Value;
            bool castChanged = !ScriptRoles.SameCast(script.Cast, castAfter);
            if (load == null && !castChanged) return;

            var after = WithScript(model, script.Id, s => { s.Cast = castAfter; });
            if (keptAsScript != null) after.Scripts.Add(keptAsScript);
            var cmd = load != null ? ScriptEditCommand.ReplaceScriptsAndTimeline(after, load, ScriptEditCommand.LabelLoad) : ScriptEditCommand.ReplaceScripts(after, ScriptEditCommand.LabelLoad);
            var r = session.Commands.Execute(cmd, new ExecuteOptions { Record = o.Undo ? HistoryMode.Record : HistoryMode.None, Source = o.Source, Label = ScriptEditCommand.LabelLoad });
            if (r.Status == CommandStatus.Error) { res.Error = new BridgeError(r.Code ?? BridgeCodes.Internal, "the script was not loaded: " + r.Message); return; }
            res.Loaded = load != null;
            ctx.Runtime.ApplyNow();
        }

        /// <summary>A new list of the scene's scripts with the script <paramref name="id"/> replaced by a changed clone.</summary>
        static SceneScriptList WithScript(SceneModel model, string id, Action<SceneScript> change)
        {
            var list = new SceneScriptList();
            if (model.Scripts != null)
                foreach (var s in model.Scripts.Scripts)
                {
                    if (s != null && s.Id == id)
                    {
                        var c = s.Clone();
                        change(c);
                        list.Scripts.Add(c);
                    }
                    else list.Scripts.Add(s);
                }
            return list;
        }

        /// <summary>The node id of an add reply: its key, or its first added node's key.</summary>
        static string KeyOf(JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

        static string Describe(JObject reply) => (reply?.Value<string>("code") ?? "?") + ": " + (reply?.Value<string>("error") ?? "");

        /// <summary>The message of a step whose command may answer later (a step runs inline: such a command cannot).</summary>
        public const string DeferringStepMessage = "a step cannot run a command that answers later";

        /// <summary>
        /// True when the command <paramref name="cmd"/> of <paramref name="server"/>'s registry may answer later (flag defers, batch, an asynchronous
        /// handler, a handler that waits frames itself): a step cannot run it. False for an unknown command (it fails as unknown when sent).
        /// script.set and script.import warn about such a step (finding script.deferringStep).
        /// </summary>
        public static bool AnswersLater(BridgeServer server, string cmd)
        {
            var e = cmd != null ? server?.Commands.Lookup(cmd, out _) : null;
            if (e == null) return false;
            var spec = e.Spec;
            return spec.Name == "batch" || (spec.Flags & CommandFlags.Defers) != 0 || e.Async != null || spec.HandlesWaitFrames;
        }

        /// <summary>The failure of a step whose command may answer later: unsupported, naming the rule and the command.</summary>
        static JObject DeferringStep(JObject sent) => new JObject
        {
            ["ok"] = false, ["cmd"] = ScriptSteps.CommandOf(sent), ["code"] = BridgeCodes.Unsupported,
            ["error"] = DeferringStepMessage + ": " + ScriptSteps.CommandOf(sent),
        };

        /// <summary>The Timeline's state as status texts give it.</summary>
        public static string StateName(TimelinePlayState s)
        {
            switch (s)
            {
                case TimelinePlayState.Stopped: return "stopped";
                case TimelinePlayState.Playing: return "playing";
                case TimelinePlayState.Paused: return "paused";
                case TimelinePlayState.Ended: return "ended";
                default: return "none";
            }
        }

        // ------------------------------------------------------------ stop

        /// <summary>
        /// Stops: the Timeline stopped (its slots released), the slots of the script commands released (a move cut short writes the
        /// position it shows into the model); with <paramref name="reset"/>, ragdoll.release for each cast character of the script last played
        /// (else the loaded one) that has a body and wet.dry {puddles: true} for each that has a stain, all undo false.
        /// </summary>
        public static ScriptStopResult Stop(AppCore app, bool reset = true)
        {
            var ctx = Context.Of(app);
            var res = new ScriptStopResult();
            var tlp = ctx.Runtime.GetPart<TimelinePlayback>();
            ctx.Playback.StopWatching();
            try { tlp?.Player.Stop(); } catch (Exception e) { res.Warnings.Add("the Timeline did not stop: " + e.Message); }
            res.Released = ctx.Playback.ReleaseCommandSlots(true);
            if (reset)
            {
                var model = ctx.Session.Model;
                string id = ctx.Playback.LastScriptId ?? model.Timeline?.Script?.Id;
                var script = id != null ? model.Scripts?.Find(id) : null;
                if (script?.Cast != null)
                {
                    var ragdolls = ctx.Runtime.GetPart<RagdollPart>();
                    foreach (var kv in script.Cast)
                    {
                        var n = model.Find(kv.Value);
                        if (n == null || n.Kind != NodeKind.Chara) continue;
                        if (ragdolls?.BodyOf(n.Id) != null)
                        {
                            var r = ctx.RunNow(new JObject { ["cmd"] = "ragdoll.release", ["key"] = n.Id, [EnvelopeFields.Undo] = false }, null);
                            if (r.Value<bool?>("ok") == true) res.Ragdolls++;
                            else res.Warnings.Add("ragdoll.release of " + kv.Key + ": " + Describe(r));
                        }
                        if (n.State is CharaNodeState cs && cs.Wet != null && (cs.Wet.Stains.Count > 0 || cs.Wet.Puddles.Count > 0))
                        {
                            var r = ctx.RunNow(new JObject { ["cmd"] = "wet.dry", ["key"] = n.Id, ["puddles"] = true, [EnvelopeFields.Undo] = false }, null);
                            if (r.Value<bool?>("ok") == true) res.Dried++;
                            else res.Warnings.Add("wet.dry of " + kv.Key + ": " + Describe(r));
                        }
                    }
                }
            }
            res.State = StateName(tlp?.Player.State ?? TimelinePlayState.None);
            return res;
        }

        // ------------------------------------------------------------ status (script.status)

        /// <summary>
        /// The state of scripts in the scene: {script, name, state, time, duration, loop, roles, uncast, prepared, lastPlay {setup, prepare,
        /// skipped, failures}, eventsFired, eventErrors, lastEventError, slots {moves, follow, dof}, recording}. The script is the one the Timeline
        /// was loaded from, else the one last played (null when neither); state and the event counters are the Timeline's.
        /// </summary>
        public static JObject Status(AppCore app)
        {
            var ctx = Context.Of(app);
            var model = ctx.Session.Model;
            var tlp = ctx.Runtime.GetPart<TimelinePlayback>();
            var pb = ctx.Playback;
            string id = model.Timeline?.Script?.Id ?? pb.LastScriptId;
            var script = id != null ? model.Scripts?.Find(id) : null;
            var roles = new JObject();
            var uncast = new JArray();
            if (script != null)
                foreach (var r in script.Roles)
                {
                    if (r?.Role == null) continue;
                    string node = script.CastOf(r.Role);
                    if (node != null && model.Find(node) != null) roles[r.Role] = node;
                    else uncast.Add(r.Role);
                }
            JObject last = null;
            var lp = pb.LastPlay;
            if (lp != null)
            {
                var failures = new JArray();
                foreach (var f in lp.Failures) failures.Add(f.ToJson());
                last = new JObject
                {
                    ["setup"] = new JObject { ["sent"] = lp.SetupSent, ["failed"] = lp.SetupFailed },
                    ["prepare"] = new JObject { ["sent"] = lp.PrepareSent, ["failed"] = lp.PrepareFailed },
                    ["skipped"] = lp.Skipped,
                    ["failures"] = failures,
                };
            }
            var le = tlp?.LastEventError;
            var slots = pb.SlotCounts();
            return new JObject
            {
                ["script"] = script?.Id,
                ["name"] = script?.Name,
                ["state"] = StateName(tlp?.Player.State ?? TimelinePlayState.None),
                ["time"] = tlp?.Player.Time ?? 0,
                ["duration"] = model.Timeline?.Duration ?? 0,
                ["loop"] = model.Timeline?.Loop ?? false,
                ["roles"] = roles,
                ["uncast"] = uncast,
                ["prepared"] = script?.Prepared,
                ["lastPlay"] = last,
                ["eventsFired"] = tlp?.EventsFired ?? 0,
                ["eventErrors"] = tlp?.EventErrors ?? 0,
                ["lastEventError"] = le == null ? null : new JObject { ["event"] = le.EventId, ["code"] = le.Code, ["message"] = le.Message },
                ["slots"] = new JObject { ["moves"] = slots.moves, ["follow"] = slots.follow, ["dof"] = slots.dof },
                ["recording"] = pb.Recording,
            };
        }

        // ------------------------------------------------------------ capture (script.capture)

        /// <summary>
        /// Writes the loaded Timeline back into its scene script: track keys, focus nodes and request strings equal to a cast node id become
        /// "$role"; the marker's compiled hash is renewed; one entry «Save edits into script». <paramref name="scriptId"/> null = the script the
        /// Timeline's marker names. Returns {script, tracks, keys, events}.
        /// </summary>
        public static JObject Capture(AppCore app, string scriptId = null, bool undo = true, CommandSource source = CommandSource.Bridge)
        {
            var ctx = Context.Of(app);
            var model = ctx.Session.Model;
            var tl = model.Timeline;
            if (tl?.Script == null) throw new BridgeException(BridgeError.NotFound("the scene's Timeline was loaded from no script", "script"));
            string id = scriptId ?? tl.Script.Id;
            var script = model.Scripts?.Find(id) ?? throw new BridgeException(BridgeError.NotFound("no scene script " + id, "script"));
            if (tl.Script.Id != script.Id) throw new BridgeException(BridgeError.BadArgs("script", null, "the Timeline holds script " + tl.Script.Id + ", not " + script.Id));
            var back = ScriptCompiler.Capture(tl, script.Cast, out _);
            var after = WithScript(model, script.Id, s => s.Timeline = back);
            var marked = tl.Clone();
            marked.Script = new TimelineScriptMarker(script.Id, marked.CompiledHash());
            var cmd = ScriptEditCommand.ReplaceScriptsAndTimeline(after, marked, ScriptEditCommand.LabelCapture);
            var r = ctx.Session.Commands.Execute(cmd, new ExecuteOptions { Record = undo ? HistoryMode.Record : HistoryMode.None, Source = source, Label = ScriptEditCommand.LabelCapture });
            if (r.Status == CommandStatus.Error) throw new BridgeException(r.Code ?? BridgeCodes.Internal, r.Message);
            return new JObject { ["script"] = script.Id, ["tracks"] = back.Tracks.Count, ["keys"] = back.KeyCount, ["events"] = back.Events.Count };
        }

        // ------------------------------------------------------------ the app's parts a play uses

        internal sealed class Context
        {
            public AppCore App;
            public SceneSession Session => App.Session;
            public SceneRuntime Runtime => App.Runtime;
            public BridgeServer Server => App.Bridge?.Server;
            public ScriptPlayback Playback;
            public bool Disposed => App.IsDisposed || Runtime == null || Runtime.IsDisposed;
            public double Realtime => Server?.Frames.Realtime ?? UnityEngine.Time.realtimeSinceStartup;
            public bool MapBusy => Runtime.GetPart<MapRealization>()?.IsBusy ?? false;

            public static Context Of(AppCore app)
            {
                if (app == null || app.IsDisposed || app.Session == null || app.Runtime == null) throw new BridgeException(BridgeCodes.Unavailable, "the app is not running");
                if (app.Bridge == null || app.Bridge.IsDisposed) throw new BridgeException(BridgeCodes.Unavailable, "the app's command host is not running: scripts cannot run");
                var pb = app.Runtime.GetPart<ScriptPlayback>();
                if (pb == null)
                {
                    pb = new ScriptPlayback();
                    app.Runtime.AddPart(pb, pb.Dispose);
                }
                return new Context { App = app, Playback = pb };
            }

            /// <summary>True when the request's command may answer later (it cannot run inline as a step).</summary>
            public bool AnswersLater(JObject request) => ScriptPlayer.AnswersLater(Server, ScriptSteps.CommandOf(request));

            /// <summary>
            /// One request run inline through the bridge host. With <paramref name="tx"/> open (the play's preparation), the app's gate lets a
            /// changing command through when the open transaction is that one, so that it joins it; any other open transaction still refuses.
            /// </summary>
            public JObject RunNow(JObject request, Transaction tx)
            {
                var server = Server;
                if (server == null) return Reply(false, BridgeCodes.Unavailable, "the app's command host is not running");
                string json = request.ToString(Formatting.None);
                string text;
                if (tx == null) text = server.RunNow(json);
                else
                {
                    var gate = server.InlineGate;
                    var bus = Session.Commands;
                    server.InlineGate = spec =>
                    {
                        if (bus.ActiveTransaction != null && ReferenceEquals(bus.ActiveTransaction, tx))
                        {
                            if (CaptureEvents.IsDelivering) return "a capture event is being delivered; try again in a later frame";
                            if (bus.Busy) return "the scene is busy (a command applies or a scene event is being delivered)";
                            return null;
                        }
                        return gate?.Invoke(spec);
                    };
                    try { text = server.RunNow(json); }
                    finally { server.InlineGate = gate; }
                }
                try { return JObject.Parse(text ?? ""); }
                catch (Exception e) { return Reply(false, BridgeCodes.Internal, "an unreadable reply: " + e.Message); }
            }

            static JObject Reply(bool ok, string code, string message) => new JObject { ["ok"] = ok, ["code"] = code, ["error"] = message };
        }

        /// <summary>Runs a play's sequence: the first part at once, the rest at later frames of the bridge's clock.</summary>
        sealed class Driver
        {
            readonly Context ctx;
            readonly ScriptPlayRun run;
            readonly IEnumerator it;

            public Driver(Context ctx, ScriptPlayRun run, IEnumerator it)
            {
                this.ctx = ctx;
                this.run = run;
                this.it = it;
            }

            public void Start() => Step();

            void Step()
            {
                while (true)
                {
                    bool more;
                    try { more = !ctx.Disposed && it.MoveNext(); }
                    catch (BridgeException e) { run.Result.Error = e.Error; more = false; }
                    catch (Exception e)
                    {
                        run.Result.Error = new BridgeError(BridgeCodes.Internal, "the play failed: " + e.GetType().Name + ": " + e.Message);
                        UnityEngine.Debug.LogWarning("[script] the play threw: " + e);
                        more = false;
                    }
                    if (!more)
                    {
                        try { (it as IDisposable)?.Dispose(); }
                        catch (Exception e) { UnityEngine.Debug.LogWarning("[script] the play's end threw: " + e); }
                        if (ctx.Disposed && run.Result.Error == null && run.Result.State == null) run.Result.Error = Gone();
                        ctx.Playback.End(run);
                        run.Finish();
                        return;
                    }
                    var frames = ctx.Server?.Frames;
                    if (frames == null)
                    {
                        try { (it as IDisposable)?.Dispose(); } catch (Exception) { }
                        run.Result.Error = Gone();
                        ctx.Playback.End(run);
                        run.Finish();
                        return;
                    }
                    frames.AtUpdate(1, Step);
                    return;
                }
            }
        }
    }
}
