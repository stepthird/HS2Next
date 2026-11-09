// StudioOps, scene scripts: what the commands and the panel do with the scene's scripts and with the store of scripts in the user's data, so all of
// them work with the bridge off. Every change of the scene's scripts is one ScriptEditCommand, one history entry each: «Add script» (a new script, a
// store row added, a file imported into the scene), «Edit script» (a script replaced, renamed, its loop changed), «Remove script» (the Timeline's mark
// goes with a loaded script; the Timeline stays), «Cast roles», «Borrow». The store is not the scene: keeping, importing into the store, deleting,
// restoring and purging make no entry. Playing, stopping and writing a loaded Timeline back are the script player's.
// A request is checked first; a refusal changes nothing and answers:
//   not_found    a scene script (by id, unique name or index), a store row, a node, a store when there is no user data folder
//   bad_args     a document that does not read (another format, a newer version, a type error), a refused step or role, a role the script does not
//                declare, a node of the wrong kind, a time outside the script's length; a limit (script.limit) named in the message
//   unavailable  a store that cannot be written (script.storeWrite)
//   busy         as every edit
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Scripts;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One scene script as the list shows it.</summary>
    public sealed class ScriptListRow
    {
        /// <summary>The script's id.</summary>
        public string Script;
        public string Name;
        /// <summary>Its role names, in order.</summary>
        public readonly List<string> Roles = new List<string>();
        /// <summary>role → node of the roles cast to a node of the scene.</summary>
        public readonly SortedDictionary<string, string> Cast = new SortedDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>The roles not cast (no entry, or a node that is not in the scene).</summary>
        public readonly List<string> Uncast = new List<string>();
        /// <summary>The preparation's mark, null when not prepared.</summary>
        public string Prepared;
        /// <summary>The scene's Timeline was loaded from this script.</summary>
        public bool Loaded;
        public int? Store;
        /// <summary>A script of another format or a newer version, kept as read.</summary>
        public bool Opaque;
    }

    /// <summary>The scene's scripts.</summary>
    public sealed class ScriptListResult
    {
        public readonly List<ScriptListRow> Scripts = new List<ScriptListRow>();
        /// <summary>The id of the script the Timeline was loaded from, or null.</summary>
        public string Loaded;
    }

    /// <summary>The store's rows.</summary>
    public sealed class ScriptStoreListResult
    {
        public readonly List<ScriptStoreRow> Rows = new List<ScriptStoreRow>();
        public int LastNumber;
    }

    /// <summary>What a script put into the scene holds.</summary>
    public sealed class ScriptSetResult
    {
        public string Script;
        public int Index, Roles, Steps, Tracks, Keys, Events;
        /// <summary>A new script (else one replaced).</summary>
        public bool Added;
    }

    /// <summary>A script's cast after a change.</summary>
    public sealed class ScriptCastResult
    {
        public readonly SortedDictionary<string, string> Cast = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> Uncast = new List<string>();
    }

    /// <summary>What a borrow takes.</summary>
    public sealed class ScriptBorrowRequest
    {
        /// <summary>The script borrowed from (a scene script or a store document).</summary>
        public SceneScript Source;
        /// <summary>The window [From, To) of the source's times; null: every time.</summary>
        public double? From, To;
        /// <summary>Seconds added to every borrowed time.</summary>
        public double Shift;
        /// <summary>Source role → target role; a role left out keeps its name.</summary>
        public Dictionary<string, string> Roles;
        public bool Events = true, Tracks = true, Camera;
    }

    /// <summary>What a borrow added.</summary>
    public sealed class ScriptBorrowResult
    {
        public int Events, Keys, Tracks, Replaced;
    }

    /// <summary>A file written.</summary>
    public sealed class ScriptExportResult
    {
        public string Path;
        public long Bytes;
    }

    /// <summary>What an import made: a store row or a scene script.</summary>
    public sealed class ScriptImportResult
    {
        public int? No;
        public string Script;
        /// <summary>The script as read from the file (its Timeline in the stored form).</summary>
        public SceneScript Read;
    }

    public sealed partial class StudioOps
    {
        /// <summary>The findings of the script commands that the save layer does not write.</summary>
        public static class ScriptOpFindings
        {
            /// <summary>A set-up or preparation step whose command answers later: it fails unsent at play (a step runs at once).</summary>
            public const string DeferringStep = "script.deferringStep";
        }

        // ------------------------------------------------------------ naming a script

        /// <summary>
        /// The index of the scene script named by <paramref name="text"/>: its id, else its name when exactly one script has it; -1 and
        /// <paramref name="fail"/> (not_found) otherwise.
        /// </summary>
        public int ScriptIndex(string text, out OpResult fail, string arg = "script")
        {
            var list = Model.Scripts;
            fail = null;
            if (text != null && list != null)
            {
                int i = list.IndexOf(text);
                if (i >= 0) return i;
                i = list.IndexOfName(text);
                if (i >= 0) return i;
                var same = new List<string>();
                for (int k = 0; k < list.Count; k++) if (list.Scripts[k]?.Name == text) same.Add(list.Scripts[k].Id);
                if (same.Count > 1)
                {
                    fail = OpResult.NotFound(same.Count + " scene scripts are named \"" + text + "\": name one by its id or its index", arg, same);
                    return -1;
                }
            }
            fail = OpResult.NotFound("no scene script \"" + text + "\" (an id, a name held by one script, or an index)", arg, ScriptCandidates());
            return -1;
        }

        /// <summary>The scene script at 0-based <paramref name="index"/>; -1 and <paramref name="fail"/> (not_found) when there is none.</summary>
        public int ScriptIndex(int index, out OpResult fail, string arg = "script")
        {
            int n = Model.Scripts?.Count ?? 0;
            fail = null;
            if (index >= 0 && index < n) return index;
            fail = OpResult.NotFound("script " + index.ToString(CultureInfo.InvariantCulture) + " is outside 0.." + (n - 1).ToString(CultureInfo.InvariantCulture) +
                " (the scene has " + n.ToString(CultureInfo.InvariantCulture) + " scripts)", arg, ScriptCandidates());
            return -1;
        }

        List<string> ScriptCandidates()
        {
            var l = new List<string>();
            var list = Model.Scripts;
            if (list != null) foreach (var s in list.Scripts) { if (l.Count == 5) break; if (s?.Id != null) l.Add(s.Id); }
            return l;
        }

        /// <summary>The scene script at <paramref name="index"/> (no check).</summary>
        public SceneScript ScriptAt(int index) => Model.Scripts.Scripts[index];

        /// <summary>The id of the script the scene's Timeline was loaded from, or null.</summary>
        public string LoadedScriptId => Model.Timeline?.Script?.Id;

        // ------------------------------------------------------------ the store

        /// <summary>The store of this runtime's user data folder (read at each call; reading creates nothing); null when there is no user data folder.</summary>
        public ScriptStore OpenScriptStore(FindingList findings = null)
        {
            string ud = Services?.UserData;
            return string.IsNullOrEmpty(ud) ? null : ScriptStore.ForUserData(ud, findings);
        }

        static OpResult ScriptStoreFailed(ScriptStoreException e, string arg = null)
        {
            string text = e.Message != null && e.Message.StartsWith(e.Code, StringComparison.Ordinal) ? e.Message : e.Code + ": " + e.Message;
            if (e.Code == ScriptFindings.Limit) return OpResult.BadArgs(arg, null, text);
            return OpResult.Unavailable(text, arg, e.Code);
        }

        ScriptStore ScriptStoreOrFail(string arg, out OpResult fail, FindingList findings = null)
        {
            var store = OpenScriptStore(findings);
            fail = store == null ? OpResult.NotFound("there is no script store: no user data folder", arg) : null;
            return store;
        }

        static void FindingsToWarnings(IEnumerable<Finding> findings, List<string> warnings)
        {
            if (findings == null) return;
            foreach (var f in findings) warnings.Add(f.Code + ": " + f.Message + (string.IsNullOrEmpty(f.Path) ? "" : " (" + f.Path + ")"));
        }

        /// <summary>The store's rows whose name or number holds <paramref name="q"/> (blank: every row); hidden rows with <paramref name="deleted"/>.
        /// Value = a <see cref="ScriptStoreListResult"/>; an unreadable index is a warning and no row.</summary>
        public OpResult ScriptStoreList(string q = null, bool deleted = false)
        {
            var res = new ScriptStoreListResult();
            var r = OpResult.NothingToDo();
            var findings = new FindingList();
            var store = OpenScriptStore(findings);
            FindingsToWarnings(findings, r.Warnings);
            if (store != null)
            {
                res.LastNumber = store.LastNumber;
                string t = q?.Trim();
                foreach (var row in deleted ? store.AllRows() : store.LiveRows())
                {
                    if (!string.IsNullOrEmpty(t) && (row.Name ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 &&
                        row.No.ToString(CultureInfo.InvariantCulture).IndexOf(t, StringComparison.Ordinal) < 0) continue;
                    res.Rows.Add(row);
                }
            }
            r.Value = res;
            return r;
        }

        /// <summary>The document of store row <paramref name="no"/> (hidden rows too). Value = the <see cref="SceneScript"/>.</summary>
        public OpResult ScriptStoreRead(int no, string arg = "no")
        {
            var findings = new FindingList();
            var store = ScriptStoreOrFail(arg, out var fail, findings);
            if (store == null) return fail;
            if (store.Find(no) == null) return OpResult.NotFound("the store has no row " + no.ToString(CultureInfo.InvariantCulture), arg);
            SceneScript s;
            try { s = store.Read(no, findings); }
            catch (ScriptStoreException e) { return ScriptStoreFailed(e, arg); }
            catch (ScriptRefusedException e) { return OpResult.BadArgs(arg, null, e.Code + ": row " + no + ": " + e.Message); }
            catch (SaveFormatException e) { return OpResult.BadArgs(arg, null, "row " + no + " does not read as a script: " + e.Message); }
            var r = OpResult.NothingToDo();
            FindingsToWarnings(findings, r.Warnings);
            r.Value = s;
            return r;
        }

        /// <summary>
        /// Keeps the scene script at <paramref name="index"/> in the store: the row of its id is replaced (same number, shown again), else a new row with the
        /// next number. The scene script's informational store number is set without a history entry. Value = the row's number.
        /// </summary>
        public OpResult ScriptKeep(int index, ExecuteOptions o = default)
        {
            var s = ScriptAt(index);
            if (s.IsOpaque) return OpResult.BadArgs("script", null, "script " + (s.Name ?? s.Id) + " is of another format or a newer version: it is kept as read and cannot be kept in the store");
            var store = ScriptStoreOrFail("script", out var fail);
            if (store == null) return OpResult.Unavailable(ScriptFindings.StoreWrite + ": there is no user data folder", "script", ScriptFindings.StoreWrite);
            ScriptStoreRow row;
            try { row = store.Import(s.ToDocument(), true, out _); }
            catch (ScriptStoreException e) { return ScriptStoreFailed(e, "script"); }
            catch (ArgumentException e) { return OpResult.BadArgs("script", null, e.Message); }
            var r = OpResult.Success();
            if (s.Store != row.No)
            {
                var c = s.Clone();
                c.Store = row.No;
                var mark = new ExecuteOptions { Record = Session.Commands.ActiveTransaction != null ? HistoryMode.Record : HistoryMode.None, Source = o.Source, Label = ScriptEditCommand.LabelEdit };
                var x = Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(Model.Scripts.With(index, c), ScriptEditCommand.LabelEdit), mark);
                if (x.Status == CommandStatus.Error) r.Warnings.Add("the scene script keeps its old store number: " + x.Message);
            }
            r.Value = row.No;
            return r;
        }

        /// <summary>Hides store row <paramref name="no"/>, or shows it again with <paramref name="restore"/>; no file is touched. Value = the row's mark after it.</summary>
        public OpResult ScriptStoreDelete(int no, bool restore = false)
        {
            var store = ScriptStoreOrFail("no", out var fail);
            if (store == null) return fail;
            var row = store.Find(no);
            if (row == null) return OpResult.NotFound("the store has no row " + no.ToString(CultureInfo.InvariantCulture), "no");
            try { if (!store.Delete(no, restore)) return OpResult.NotFound("the store has no row " + no.ToString(CultureInfo.InvariantCulture), "no"); }
            catch (ScriptStoreException e) { return ScriptStoreFailed(e, "no"); }
            var r = row.Deleted == !restore ? OpResult.NothingToDo() : OpResult.Success();
            r.Value = !restore;
            return r;
        }

        /// <summary>What a purge would remove now (nothing is removed). Value = a <see cref="ScriptPurgeResult"/>.</summary>
        public OpResult ScriptPurgePreview()
        {
            var r = OpResult.NothingToDo();
            var store = OpenScriptStore();
            var p = store?.PurgePreview() ?? new ScriptPurgeResult();
            if (p.Unreadable) r.Warnings.Add(ScriptFindings.StoreUnreadable + ": the store's index could not be read; nothing is purged before it is written again");
            r.Value = p;
            return r;
        }

        /// <summary>
        /// Removes the files the hidden rows name, then those rows; nothing else. With an unreadable index nothing is removed and the result says so in a
        /// warning (script.storeUnreadable). Value = a <see cref="ScriptPurgeResult"/>.
        /// </summary>
        public OpResult ScriptPurge()
        {
            var r = OpResult.NothingToDo();
            var store = OpenScriptStore();
            if (store == null) { r.Value = new ScriptPurgeResult(); return r; }
            var findings = new FindingList();
            ScriptPurgeResult p;
            try { p = store.Purge(findings); }
            catch (ScriptStoreException e) { return ScriptStoreFailed(e); }
            if (p.Rows > 0 || p.Files > 0) r = OpResult.Success();
            FindingsToWarnings(findings, r.Warnings);
            r.Value = p;
            return r;
        }

        // ------------------------------------------------------------ reading documents

        /// <summary>
        /// The context that turns a document's build-form tracks and timed commands into the stored form: the scene's id source, node kinds of node-id
        /// keys, animation rows through the catalog (as timeline.build resolves them); its warnings collect in <see cref="ScriptBuildContext.Warnings"/>.
        /// </summary>
        public ScriptBuildContext ScriptBuild(bool strict = false)
        {
            var b = new ScriptBuildContext { Ids = Session.Options.Ids, Strict = strict, KindOfNode = id => Model.Find(id)?.Kind };
            b.ResolveAnime = (key, arg) => ResolveScriptAnime(key, arg, b.Warnings);
            return b;
        }

        AnimeRef ResolveScriptAnime(JObject key, string arg, List<string> warnings)
        {
            var db = Services?.Db ?? throw new BridgeException(BridgeCodes.Unavailable, "no content database: animation rows cannot be resolved");
            int g = KeyInt(key, "group"), cat = KeyInt(key, "category"), no = KeyInt(key, "no");
            string guid = KeyStr(key, "guid") ?? KeyStr(key, "source");
            string rf = KeyStr(key, "ref");
            if (rf != null)
            {
                if (!ContentId.TryParse(rf, out var id) || id.IsEmpty) throw new BridgeException(BridgeError.BadArgs(arg + ".ref", ArgTypes.ContentId, "bad content id \"" + rf + "\": expected \"guid#no\""));
                guid = id.SourceGuid;
                no = id.Id;
            }
            string described = (guid != null ? guid + " " : "") + g.ToString(CultureInfo.InvariantCulture) + "/" + cat.ToString(CultureInfo.InvariantCulture) + "/" + no.ToString(CultureInfo.InvariantCulture);
            StudioAnime row = guid != null ? (db.TryGetAnime(guid, g, cat, no, out var a) ? a : null) : db.FindAnime(g, cat, no);
            if (row == null) return null;
            var w = new List<string>();
            var bad = CheckAnimeRow(row, described, w);
            if (bad != null) throw new BridgeException(BridgeCodes.Unsupported, arg + ": " + bad.Message);
            foreach (var x in w) warnings?.Add(arg + ": " + x);
            return new AnimeRef(row.Guid, row.Group, row.Category, row.No);
        }

        static int KeyInt(JObject o, string name)
        {
            var t = o?[name];
            if (t == null || t.Type == JTokenType.Null) return 0;
            return t.Type == JTokenType.Integer ? t.Value<int>() : (int)t.Value<double>();
        }

        static string KeyStr(JObject o, string name)
        {
            var t = o?[name];
            return t != null && t.Type == JTokenType.String ? (string)t : null;
        }

        /// <summary>
        /// A document read from an object or a file (build-form tracks converted); a refusal as an OpResult naming <paramref name="arg"/>, with the place in
        /// the document as detail member <c>path</c> and in the text; findings and conversion warnings into <paramref name="warnings"/>. With
        /// <paramref name="refuseLimits"/> (an edit) a document the reader had to cut to a limit is refused (script.limit), else it is taken as cut.
        /// </summary>
        SceneScript ReadScriptDocument(Func<FindingList, ScriptBuildContext, SceneScript> read, string arg, bool strict, bool refuseLimits, List<string> warnings, out OpResult fail)
        {
            fail = null;
            var findings = new FindingList();
            var build = ScriptBuild(strict);
            SceneScript s;
            try { s = read(findings, build); }
            catch (ScriptRefusedException e) { fail = OpResult.BadArgs(arg, null, e.Code + ": " + e.Message); return null; }
            catch (SaveFormatException e) { fail = OpResult.BadArgs(arg, null, "not a script document: " + e.Message); return null; }
            catch (BridgeException e)
            {
                var d = e.Error?.Detail;
                string at = d?.Arg;
                if (at != null && (at.StartsWith("tracks", StringComparison.Ordinal) || at.StartsWith("events", StringComparison.Ordinal))) at = "timeline." + at;
                if (at == arg) at = null;
                fail = OpResult.Fail(e.Code, (e.Error?.Message ?? e.Message) + (at != null ? " (" + at + ")" : ""), arg, d?.Expected, d?.Candidates);
                var extra = d?.Extra != null ? new Dictionary<string, object>(d.Extra) : new Dictionary<string, object>();
                if (at != null) extra[ScriptDocPath] = at;
                if (extra.Count > 0) fail.Extra = extra;
                return null;
            }
            catch (IOException e) { fail = OpResult.BadArgs(arg, null, "the file cannot be read: " + e.Message); return null; }
            catch (UnauthorizedAccessException e) { fail = OpResult.BadArgs(arg, null, "the file cannot be read: " + e.Message); return null; }
            if (refuseLimits)
                foreach (var f in findings)
                    if (f.Code == ScriptFindings.Limit)
                    {
                        fail = OpResult.BadArgs(arg, null, ScriptFindings.Limit + ": " + f.Message + (string.IsNullOrEmpty(f.Path) ? "" : " (" + f.Path + ")"));
                        if (!string.IsNullOrEmpty(f.Path)) fail.Extra = new Dictionary<string, object> { [ScriptDocPath] = f.Path };
                        return null;
                    }
            FindingsToWarnings(findings, warnings);
            warnings.AddRange(build.Warnings);
            return s;
        }

        /// <summary>The detail member of a refusal that names the place in the script's document ("setup[0]", "timeline.events[2].do").</summary>
        public const string ScriptDocPath = "path";

        /// <summary>
        /// The first step or timed command of <paramref name="s"/> that a script may not hold (a command of the group script, a Timeline edit, an id, no
        /// command, a wait out of range), as a refusal naming <paramref name="arg"/> with its place as detail member <c>path</c>; what
        /// <paramref name="before"/> already held stays allowed. Null when there is none.
        /// </summary>
        static OpResult StepRefusal(SceneScript s, SceneScript before, string arg)
        {
            OpResult Steps(List<JObject> steps, List<JObject> old, string name)
            {
                if (steps == null) return null;
                var kept = new HashSet<string>(StringComparer.Ordinal);
                if (old != null) foreach (var o in old) if (o != null) kept.Add(CanonicalJsonWriter.ToText(o));
                for (int i = 0; i < steps.Count; i++)
                {
                    var st = steps[i];
                    if (st == null) continue;
                    string why = ScriptSteps.Refusal(st) ?? ScriptSteps.WaitProblem(st);
                    if (why == null || kept.Contains(CanonicalJsonWriter.ToText(st))) continue;
                    return StepFail(arg, name + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", why);
                }
                return null;
            }
            var r = Steps(s.Setup, before?.Setup, "setup") ?? Steps(s.Prepare, before?.Prepare, "prepare");
            if (r != null) return r;
            var events = s.Timeline?.Events;
            if (events == null) return null;
            var oldEvents = new HashSet<string>(StringComparer.Ordinal);
            if (before?.Timeline?.Events != null) foreach (var e in before.Timeline.Events) if (e?.Do != null) oldEvents.Add(CanonicalJsonWriter.ToText(e.Do));
            for (int i = 0; i < events.Count; i++)
            {
                var d = events[i]?.Do;
                string why = ScriptSteps.EventRefusal(d);
                if (why == null || (d != null && oldEvents.Contains(CanonicalJsonWriter.ToText(d)))) continue;
                return StepFail(arg, "timeline.events[" + i.ToString(CultureInfo.InvariantCulture) + "].do", why);
            }
            return null;
        }

        /// <summary>
        /// The first timed command given in the input form of timeline.events (no id) whose command a script may not hold, as a refusal naming
        /// <paramref name="arg"/> with its place ("timeline.events[i].do"); checked before the conversion, which reads such commands one at a time.
        /// </summary>
        static OpResult BuildFormEventRefusal(JObject document, string arg)
        {
            if (!(document?["timeline"] is JObject t) || !(t["events"] is JArray a)) return null;
            for (int i = 0; i < a.Count; i++)
            {
                if (!(a[i] is JObject e) || e.Property("id") != null || !(e["do"] is JObject d)) continue;
                string why = ScriptSteps.EventRefusal(d);
                if (why != null) return StepFail(arg, "timeline.events[" + i.ToString(CultureInfo.InvariantCulture) + "].do", why);
            }
            return null;
        }

        /// <summary>A script file's object for the checks before reading it (null when it is too large, missing or not JSON: the reader then reports it).</summary>
        static JObject PeekDocument(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > ScriptLimits.MaxFileBytes) return null;
                return DocReader.Parse(File.ReadAllBytes(path), info.Name);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SaveFormatException) { return null; }
        }

        static OpResult StepFail(string arg, string path, string why)
        {
            var f = OpResult.BadArgs(arg, null, path + ": " + why + " (a script never sends it)");
            f.Extra = new Dictionary<string, object> { [ScriptDocPath] = path };
            return f;
        }

        // ------------------------------------------------------------ the scene's scripts

        /// <summary>The scene's scripts whose name or id holds <paramref name="q"/> (blank: every one). Value = a <see cref="ScriptListResult"/>.</summary>
        public OpResult ScriptList(string q = null)
        {
            var res = new ScriptListResult { Loaded = LoadedScriptId };
            string t = q?.Trim();
            var list = Model.Scripts;
            if (list != null)
                foreach (var s in list.Scripts)
                {
                    if (s == null) continue;
                    if (!string.IsNullOrEmpty(t) && (s.Name ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 && (s.Id ?? "").IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    res.Scripts.Add(ScriptRow(s, res.Loaded));
                }
            var r = OpResult.NothingToDo();
            r.Value = res;
            return r;
        }

        /// <summary>One scene script as the list shows it.</summary>
        public ScriptListRow ScriptRow(SceneScript s, string loaded = null)
        {
            var row = new ScriptListRow { Script = s.Id, Name = s.Name, Prepared = s.Prepared, Store = s.Store, Opaque = s.IsOpaque, Loaded = s.Id != null && s.Id == (loaded ?? LoadedScriptId) };
            if (s.IsOpaque || s.Roles == null) return row;
            foreach (var role in s.Roles)
            {
                if (role?.Role == null) continue;
                row.Roles.Add(role.Role);
                string node = s.CastOf(role.Role);
                if (node != null && Model.Find(node) != null) row.Cast[role.Role] = node;
                else row.Uncast.Add(role.Role);
            }
            return row;
        }

        /// <summary>
        /// Puts a document into the scene (build-form tracks converted): a new script at the end (an id the scene holds already gets a new one, with a
        /// warning), or with <paramref name="replace"/> in place of the scene script of its id, which keeps its cast (of the roles it still declares), its
        /// preparation mark and its store number. One entry «Add script» / «Edit script». Value = a <see cref="ScriptSetResult"/>.
        /// </summary>
        public OpResult ScriptSet(JObject document, bool replace, bool strict = false, ExecuteOptions o = default)
        {
            var warnings = new List<string>();
            var early = BuildFormEventRefusal(document, "script");
            if (early != null) return early;
            var s = ReadScriptDocument((f, b) => ScriptFormat.FromToken(document, f, b), "script", strict, true, warnings, out var fail);
            if (s == null) return fail;
            return PutScript(s, replace, null, "script", warnings, o);
        }

        /// <summary>
        /// Imports a script file: into the store (a row of its id is replaced with <paramref name="replace"/>, else the copy gets a new id and the next
        /// number) or, with <paramref name="toScene"/>, into the scene as <see cref="ScriptSet"/> puts a document (one entry «Add script» / «Edit
        /// script»). Value = a <see cref="ScriptImportResult"/>; the reader's findings and the conversion's warnings are warnings.
        /// </summary>
        public OpResult ScriptImport(string path, bool toScene, bool replace, bool strict = false, ExecuteOptions o = default)
        {
            var warnings = new List<string>();
            var early = BuildFormEventRefusal(PeekDocument(path), "path");
            if (early != null) return early;
            var s = ReadScriptDocument((f, b) => ScriptFormat.ReadFile(path, f, b), "path", strict, false, warnings, out var fail);
            if (s == null) return fail;
            OpResult r;
            if (toScene)
            {
                r = PutScript(s, replace, null, "path", warnings, o);
                if (!r.Ok) return r;
                r.Value = new ScriptImportResult { Script = ((ScriptSetResult)r.Value).Script, Read = s };
                return r;
            }
            var store = OpenScriptStore();
            if (store == null) return OpResult.Unavailable(ScriptFindings.StoreWrite + ": there is no user data folder", "path", ScriptFindings.StoreWrite);
            ScriptStoreRow row;
            SceneScript stored;
            try { row = store.Import(s, replace, out stored); }
            catch (ScriptStoreException e) { return ScriptStoreFailed(e, "path"); }
            catch (ArgumentException e) { return OpResult.BadArgs("path", null, e.Message); }
            if (stored.Id != s.Id) warnings.Add("the store holds a script of id " + s.Id + ": the copy gets id " + stored.Id + " (replace: true replaces that row)");
            r = OpResult.Success();
            r.Warnings.AddRange(warnings);
            r.Value = new ScriptImportResult { No = row.No, Read = s };
            return r;
        }

        /// <summary>Adds store row <paramref name="no"/> to the scene as a new script (its store number kept; an id the scene holds gets a new one). One
        /// entry «Add script». Value = a <see cref="ScriptSetResult"/>.</summary>
        public OpResult ScriptAdd(int no, ExecuteOptions o = default)
        {
            var read = ScriptStoreRead(no);
            if (!read.Ok) return read;
            var warnings = new List<string>(read.Warnings);
            return PutScript((SceneScript)read.Value, false, no, "no", warnings, o);
        }

        OpResult PutScript(SceneScript doc, bool replace, int? store, string arg, List<string> warnings, ExecuteOptions o)
        {
            var list = Model.Scripts ?? new SceneScriptList();
            SceneScript put;
            int index;
            string label;
            if (replace)
            {
                index = list.IndexOf(doc.Id);
                if (index < 0) return OpResult.NotFound("no scene script of id " + doc.Id + " to replace (replace: false adds the script)", arg, ScriptCandidates());
                var old = list.Scripts[index];
                if (old.IsOpaque) return OpResult.BadArgs(arg, null, "script " + doc.Id + " is of another format or a newer version: it is kept as read and is not replaced");
                put = doc.ToScene(old.Store);
                foreach (var kv in old.Cast) if (put.FindRole(kv.Key) != null) put.Cast[kv.Key] = kv.Value;
                put.Prepared = old.Prepared;
                if (store != null) put.Store = store;
                label = ScriptEditCommand.LabelEdit;
            }
            else
            {
                put = doc.ToScene(store);
                if (list.IndexOf(put.Id) >= 0)
                {
                    string old = put.Id;
                    put.Id = Session.NewId();
                    warnings.Add("the scene holds a script of id " + old + ": the new script gets id " + put.Id + " (replace: true replaces that script)");
                }
                index = list.Count;
                label = ScriptEditCommand.LabelAdd;
            }
            var refused = StepRefusal(put, replace ? list.Scripts[index] : null, arg);
            if (refused != null) return refused;
            var after = replace ? list.With(index, put) : list.Adding(put);
            var r = OpResult.From(Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(after, label), Labelled(o, label)), arg);
            if (!r.Ok) return r;
            r.Warnings.AddRange(warnings);
            int steps = (put.Setup?.Count ?? 0) + (put.Prepare?.Count ?? 0);
            r.Value = new ScriptSetResult
            {
                Script = put.Id, Index = index, Roles = put.Roles?.Count ?? 0, Steps = steps, Tracks = put.Timeline?.Tracks?.Count ?? 0, Keys = put.Timeline?.KeyCount ?? 0,
                Events = put.Timeline?.Events?.Count ?? 0, Added = !replace,
            };
            return r;
        }

        /// <summary>Replaces the scene script at <paramref name="index"/> with <paramref name="after"/> (the panel's rename and loop), one entry
        /// <paramref name="label"/> (default «Edit script»).</summary>
        public OpResult ScriptReplace(int index, SceneScript after, string label = null, ExecuteOptions o = default)
        {
            if (after == null) return OpResult.BadArgs("script", "a script");
            label ??= ScriptEditCommand.LabelEdit;
            var r = OpResult.From(Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(Model.Scripts.With(index, after), label), Labelled(o, label)), "script");
            r.Value = after.Id;
            return r;
        }

        /// <summary>Renames the scene script at <paramref name="index"/> (at most 128 characters), one entry «Edit script».</summary>
        public OpResult ScriptRename(int index, string name, ExecuteOptions o = default)
        {
            if (name == null) return OpResult.BadArgs("name", "a name");
            var s = ScriptAt(index);
            if (s.IsOpaque) return OpResult.BadArgs("script", null, "a script of another format or a newer version is not edited");
            var c = s.Clone();
            c.Name = name;
            return ScriptReplace(index, c, ScriptEditCommand.LabelEdit, o);
        }

        /// <summary>Sets whether the scene script at <paramref name="index"/> loops (its Timeline's loop), one entry «Edit script».</summary>
        public OpResult ScriptSetLoop(int index, bool loop, ExecuteOptions o = default)
        {
            var s = ScriptAt(index);
            if (s.IsOpaque) return OpResult.BadArgs("script", null, "a script of another format or a newer version is not edited");
            var c = s.Clone();
            c.Timeline.Loop = loop;
            return ScriptReplace(index, c, ScriptEditCommand.LabelEdit, o);
        }

        /// <summary>
        /// Removes the scene script at <paramref name="index"/>, one entry «Remove script»; when the scene's Timeline was loaded from it, the Timeline
        /// loses its mark in the same entry and stays. Value = the removed script's id.
        /// </summary>
        public OpResult ScriptRemove(int index, ExecuteOptions o = default)
        {
            var list = Model.Scripts;
            var s = list.Scripts[index];
            var after = list.Without(index);
            ScriptEditCommand cmd;
            var tl = Model.Timeline;
            if (tl?.Script != null && s.Id != null && tl.Script.Id == s.Id)
            {
                var plain = tl.Clone();
                plain.Script = null;
                cmd = ScriptEditCommand.ReplaceScriptsAndTimeline(after, plain, ScriptEditCommand.LabelRemove);
            }
            else cmd = ScriptEditCommand.ReplaceScripts(after, ScriptEditCommand.LabelRemove);
            var r = OpResult.From(Session.Commands.Execute(cmd, Labelled(o, ScriptEditCommand.LabelRemove)), "script");
            if (r.Ok) rt.ApplyNow();
            r.Value = s.Id;
            return r;
        }

        /// <summary>
        /// Casts roles of the scene script at <paramref name="index"/>: role → node id, or null to uncast it. A role the script does not declare, a node
        /// that is not in the scene or not of the role's kind refuse the whole change; a character of the other sex is cast with a warning. One entry
        /// «Cast roles». Value = a <see cref="ScriptCastResult"/>.
        /// </summary>
        public OpResult ScriptCast(int index, IReadOnlyList<KeyValuePair<string, string>> roles, ExecuteOptions o = default)
        {
            var s = ScriptAt(index);
            if (s.IsOpaque) return OpResult.BadArgs("script", null, "a script of another format or a newer version is not cast");
            var c = s.Clone();
            var warnings = new List<string>();
            foreach (var kv in roles ?? Array.Empty<KeyValuePair<string, string>>())
            {
                string arg = "roles." + kv.Key;
                var role = c.FindRole(kv.Key);
                if (role == null) return OpResult.BadArgs(arg, "a role of the script", "script " + (s.Name ?? s.Id) + " declares no role " + kv.Key);
                if (kv.Value == null) { c.Cast.Remove(kv.Key); continue; }
                var n = Find(kv.Value, arg, out var fail);
                if (n == null) return fail;
                if (role.IsChara && n.Kind != NodeKind.Chara) return OpResult.BadArgs(arg, "a character", "role " + kv.Key + " is a character role; node " + n.Id + " is " + Article(KindName(n)) + KindName(n));
                if (role.IsItem && n.Kind != NodeKind.Item) return OpResult.BadArgs(arg, "an item", "role " + kv.Key + " is a prop role; node " + n.Id + " is " + Article(KindName(n)) + KindName(n));
                int? have = role.IsChara && role.Sex != null ? ScriptNodeSex(n) : null;
                if (have.HasValue)
                {
                    string sex = have.Value == 1 ? ScriptRole.SexFemale : ScriptRole.SexMale;
                    if (sex != role.Sex) warnings.Add("role " + kv.Key + " is a " + role.Sex + " role; node " + n.Id + " is " + sex + ": cast anyway");
                }
                c.Cast[kv.Key] = n.Id;
            }
            var r = OpResult.From(Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(Model.Scripts.With(index, c), ScriptEditCommand.LabelCast), Labelled(o, ScriptEditCommand.LabelCast)), "roles");
            if (!r.Ok) return r;
            r.Warnings.AddRange(warnings);
            var res = new ScriptCastResult();
            var now = Model.Scripts?.Find(c.Id) ?? c;
            foreach (var role in now.Roles)
            {
                if (role?.Role == null) continue;
                string node = now.CastOf(role.Role);
                if (node != null && Model.Find(node) != null) res.Cast[role.Role] = node;
                else res.Uncast.Add(role.Role);
            }
            r.Value = res;
            return r;
        }

        /// <summary>A character node's sex (1 female, 0 male), or null when it is not known.</summary>
        static int? ScriptNodeSex(TreeNode n)
        {
            if (n == null || n.Kind != NodeKind.Chara) return null;
            var p = n.Appearance?.Stored?.Chara?.Profile;
            if (p != null) return p.Sex;
            var c = n.Appearance?.Card;
            return c != null ? c.Sex : (int?)null;
        }

        /// <summary>
        /// Writes a script document to <paramref name="path"/> (a full path the caller has checked; a temporary file, then a replace): the scene script at
        /// <paramref name="index"/> (without cast, preparation mark and store number) or store row <paramref name="no"/>. Value = a
        /// <see cref="ScriptExportResult"/>.
        /// </summary>
        public OpResult ScriptExport(int? index, int? no, string path)
        {
            SceneScript s;
            var r = OpResult.NothingToDo();
            if (index != null)
            {
                s = ScriptAt(index.Value);
                if (s.IsOpaque) return OpResult.BadArgs("script", null, "a script of another format or a newer version is kept as read and is not written as a document");
            }
            else
            {
                var read = ScriptStoreRead(no ?? 0);
                if (!read.Ok) return read;
                s = (SceneScript)read.Value;
                r.Warnings.AddRange(read.Warnings);
            }
            long bytes;
            try { bytes = ScriptFormat.WriteFile(s, path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            {
                return OpResult.Fail(BridgeCodes.IoDenied, "the file cannot be written: " + e.Message, "path");
            }
            r.Value = new ScriptExportResult { Path = Path.GetFullPath(path), Bytes = bytes };
            return r;
        }

        // ------------------------------------------------------------ borrowing

        /// <summary>
        /// Borrows timed commands and keys of a time window from another script into the scene script at <paramref name="index"/>: each moved by the
        /// shift, roles renamed (track keys, focus nodes, every "$role" of a timed command); camera and depth-of-field tracks only with Camera; a key merges
        /// into the target's track of the same channel (a key at a time the track has replaces it, counted), a track the target lacks is added; timed
        /// commands get new ids. A role the target does not declare, or a time outside the target's length, refuses the borrow. One entry «Borrow».
        /// Value = a <see cref="ScriptBorrowResult"/>.
        /// </summary>
        public OpResult ScriptBorrow(int index, ScriptBorrowRequest req, ExecuteOptions o = default)
        {
            var target = ScriptAt(index);
            if (target.IsOpaque) return OpResult.BadArgs("script", null, "a script of another format or a newer version is not edited");
            var src = req?.Source;
            if (src == null || src.IsOpaque || src.Timeline == null) return OpResult.BadArgs("from", null, "the source is a script of another format or a newer version, or has no Timeline");
            if (req.From != null && req.To != null && !(req.From.Value < req.To.Value)) return OpResult.BadArgs("window", "[t0, t1) with t0 < t1");
            var c = target.Clone();
            var tl = c.Timeline;
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in c.Roles) if (role?.Role != null) declared.Add(role.Role);
            var map = req.Roles ?? new Dictionary<string, string>();
            foreach (var kv in map)
            {
                if (!ScriptText.IsRoleName(kv.Key)) return OpResult.BadArgs("roles." + kv.Key, "a role name");
                if (kv.Value == null || !ScriptText.IsRoleName(kv.Value)) return OpResult.BadArgs("roles." + kv.Key, "a role name", "roles." + kv.Key + ": \"" + kv.Value + "\" is not a role name");
            }
            string undeclared = null, undeclaredAt = null;
            string Rename(string s, string at)
            {
                string role = ScriptText.RoleOf(s);
                if (role == null) return s;
                if (map.TryGetValue(role, out var to)) role = to;
                if (!declared.Contains(role) && undeclared == null) { undeclared = role; undeclaredAt = at; }
                return ScriptText.RoleRef(role);
            }
            bool In(float t) => (req.From == null || t >= req.From.Value) && (req.To == null || t < req.To.Value);
            float duration = tl.Duration;
            string outside = null;
            float Shifted(float t, string at)
            {
                float v = (float)(t + req.Shift);
                if ((v < 0f || v > duration || float.IsNaN(v) || float.IsInfinity(v)) && outside == null) outside = at + " at " + v.ToString("R", CultureInfo.InvariantCulture) + " s";
                return v;
            }
            var res = new ScriptBorrowResult();
            var warnings = new List<string>();
            var ids = Session.Options.Ids;
            if (req.Events)
                for (int i = 0; i < src.Timeline.Events.Count; i++)
                {
                    var e = src.Timeline.Events[i];
                    if (e == null || !In(e.T)) continue;
                    var ne = e.Clone();
                    ne.Id = ids.NewId();
                    ne.T = Shifted(e.T, "timed command " + (e.Command ?? i.ToString(CultureInfo.InvariantCulture)));
                    if (ne.Do != null) ne.Do = (JObject)RenameStrings(ne.Do, s => Rename(s, "a timed command at " + e.T.ToString("R", CultureInfo.InvariantCulture) + " s"));
                    tl.Events.Add(ne);
                    res.Events++;
                }
            if (req.Tracks)
                for (int i = 0; i < src.Timeline.Tracks.Count; i++)
                {
                    var t = src.Timeline.Tracks[i];
                    if (t == null) continue;
                    if (!t.IsKnownType) { warnings.Add("track " + i.ToString(CultureInfo.InvariantCulture) + " of the source is of a type this app does not know: not borrowed"); continue; }
                    if (TimelineTypes.IsSceneWide(t.Type) && !req.Camera) continue;
                    var keys = new List<TimelineKey>();
                    foreach (var k in t.Keys)
                    {
                        if (k == null || !In(k.T)) continue;
                        var nk = k.Clone();
                        nk.T = Shifted(k.T, "a key of " + t.Type);
                        if (nk.Dof?.Focus?.Node != null) nk.Dof.Focus.Node = Rename(nk.Dof.Focus.Node, "a focus of a dof key");
                        keys.Add(nk);
                    }
                    if (keys.Count == 0) continue;
                    var probe = t.Clone(true);
                    probe.Key = t.Key != null ? Rename(t.Key, "the key of a " + t.Type + " track") : null;
                    probe.Keys = new List<TimelineKey>();
                    int at = tl.IndexOfChannel(probe.ChannelKey);
                    if (at < 0)
                    {
                        probe.Id = ids.NewId();
                        probe.Keys.AddRange(keys);
                        probe.SortKeys();
                        tl.Tracks.Add(probe);
                    }
                    else
                    {
                        var into = tl.Tracks[at];
                        foreach (var k in keys)
                        {
                            int same = into.IndexOfTime(k.T);
                            if (same >= 0) { into.Keys[same] = k; res.Replaced++; }
                            else into.Keys.Add(k);
                        }
                        into.SortKeys();
                    }
                    res.Tracks++;
                    res.Keys += keys.Count;
                }
            if (undeclared != null)
                return OpResult.BadArgs("roles", "roles the script declares", "script " + (target.Name ?? target.Id) + " declares no role " + undeclared + " (" + undeclaredAt + "): declare it, or map it with roles");
            if (outside != null)
                return OpResult.BadArgs("shift", "times within [0, " + duration.ToString("R", CultureInfo.InvariantCulture) + "]", "a borrowed time is outside the script's length: " + outside);
            tl.Normalize();
            var r = OpResult.From(Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(Model.Scripts.With(index, c), ScriptEditCommand.LabelBorrow), Labelled(o, ScriptEditCommand.LabelBorrow)), "from");
            if (!r.Ok) return r;
            r.Warnings.AddRange(warnings);
            r.Value = res;
            return r;
        }

        /// <summary>A copy of <paramref name="token"/> with every string value passed through <paramref name="f"/> (member names untouched).</summary>
        static JToken RenameStrings(JToken token, Func<string, string> f)
        {
            switch (token)
            {
                case JObject o:
                {
                    var c = new JObject();
                    foreach (var p in o.Properties()) c.Add(p.Name, RenameStrings(p.Value, f));
                    return c;
                }
                case JArray a:
                {
                    var c = new JArray();
                    foreach (var e in a) c.Add(RenameStrings(e, f));
                    return c;
                }
                case JValue v when v.Type == JTokenType.String:
                    return new JValue(f((string)v));
                default:
                    return token?.DeepClone();
            }
        }

        // ------------------------------------------------------------ importing a scene

        /// <summary>
        /// The scene's scripts after an import of a scene file that holds <paramref name="fromFile"/>: the scene's own, then the file's, each a copy whose
        /// id the scene holds already gets a derived id, whose cast is mapped through the import's node ids (<paramref name="imported"/>: the file's node
        /// id → the new one; an entry naming a node that was not imported is left out, the role uncast) and which prepares again at its first play. A
        /// script of another format or a newer version comes along when its id is free, else it stays behind. Null when the file holds no script.
        /// <paramref name="remapped"/> counts the cast entries mapped, <paramref name="warnings"/> the scripts left behind.
        /// </summary>
        internal SceneScriptList ImportedScripts(SceneScriptList fromFile, IReadOnlyDictionary<string, string> imported, out int remapped, List<string> warnings = null)
        {
            remapped = 0;
            if (fromFile == null || fromFile.Count == 0) return null;
            var own = Model.Scripts;
            var result = own != null ? new SceneScriptList(own.Scripts) : new SceneScriptList();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in result.Scripts) if (s?.Id != null) used.Add(s.Id);
            int left = 0;
            for (int i = 0; i < fromFile.Count; i++)
            {
                var s = fromFile.Scripts[i];
                if (s == null) continue;
                if (s.IsOpaque)
                {
                    if (s.Id != null && JsonText.IsId(s.Id) && used.Add(s.Id)) result.Scripts.Add(s.Clone());
                    else left++;
                    continue;
                }
                var c = s.Clone();
                if (!JsonText.IsId(c.Id) || used.Contains(c.Id))
                {
                    string old = c.Id ?? "";
                    c.Id = JsonText.DerivedId(old + "/import/" + i.ToString(CultureInfo.InvariantCulture));
                    for (int n = 1; used.Contains(c.Id); n++) c.Id = JsonText.DerivedId(old + "/import/" + i.ToString(CultureInfo.InvariantCulture) + "/" + n.ToString(CultureInfo.InvariantCulture));
                }
                used.Add(c.Id);
                var cast = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in c.Cast)
                    if (kv.Value != null && imported != null && imported.TryGetValue(kv.Value, out var to)) { cast[kv.Key] = to; remapped++; }
                c.Cast = cast;
                c.Prepared = null;
                result.Scripts.Add(c);
            }
            if (left > 0) warnings?.Add(left + " scripts of the file of another format or a newer version left behind: their ids are taken");
            if (result.Count > ScriptLimits.MaxScripts)
            {
                int over = result.Count - ScriptLimits.MaxScripts;
                result.Scripts.RemoveRange(ScriptLimits.MaxScripts, over);
                warnings?.Add(ScriptFindings.Limit + ": a scene holds at most " + ScriptLimits.MaxScripts + " scripts: " + over + " of the file's left behind");
            }
            return result.Count > (own?.Count ?? 0) ? result : null;
        }
    }
}
