// The panel «Scene scripts» (panel id "scripts"). From the top:
//   scripts-scene  the scene's scripts, one row each (name, roles cast n of m, prepared, the one loaded into the Timeline marked); a press on a row selects it; for the selected
//                  script «Play», «Stop», «Prepare again», «Record…» (folder, size, frame rate: the recorder's dialog fields), «Loop», «Rename» (in place: Enter confirms, Esc
//                  cancels), «Remove», «Keep in the store», «Export…».
//   scripts-roles  for the selected script a row per role: the role, its kind, its sex or its row, the cast node (a picker of the scene's characters, or of the items of the
//                  role's row, and «Not cast»), «Use selected», the name a role is found by when it is not cast; uncast rows are marked.
//   scripts-store  the store's rows (number, name, modified) with a search, «Add to scene», «Delete» (the first press asks, the second deletes), «Show deleted», «Restore»,
//                  «Import…» and «Purge…» (a dialog with what it removes).
//   scripts-last   the last play: what was sent and what failed (phase, step, command, message), collapsed by default.
// Play, stop, preparation and recording go through the script player, the rest through StudioOps: what the panel does is what the script commands do, and it works with the
// bridge off. The panel's own values (the selected script, the search, the sections opened) are panel state, kept for the session, never in the scene. Subscribed from attach to
// detach: a closed panel reads nothing, and it reads four times a second only while it is shown.
// Element names: scripts-panel, scripts-scene, scripts-none, scripts-list, scripts-row-<i> (-name, -edit), scripts-play, scripts-stop, scripts-prepare, scripts-record, scripts-loop,
// scripts-rename, scripts-remove, scripts-keep, scripts-export, scripts-roles, scripts-roles-note, scripts-role-<role> (-pick, -use, -hint), the picker's menu scripts-cast-menu
// with menu-item-scripts-cast-<node id> and menu-item-scripts-cast-none, scripts-store, scripts-store-search, scripts-show-deleted, scripts-store-note, scripts-store-list,
// scripts-store-row-<no> (-name, -add, -delete, -restore), scripts-import, scripts-purge, scripts-last, scripts-last-summary, scripts-last-events, scripts-last-<i>;
// the dialogs dialog-scripts-record, dialog-scripts-export, dialog-scripts-import, dialog-scripts-purge (scripts-purge-row-<no>).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using HS2Next.Scenes.Timeline;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class ScriptsPanel : VisualElement
    {
        public const float RefreshSeconds = 0.25f;
        /// <summary>The store's rows are read again at most this often while the store section is open (seconds of real time), besides after the panel's own changes.</summary>
        public const float StoreSeconds = 2f;

        /// <summary>The panel on the shell now, or null.</summary>
        public static ScriptsPanel Current { get; private set; }

        // panel state: kept across rebuilds of the panel for the session, never in the scene
        static string selectedId, query = "";
        static bool showDeleted;
        static readonly Dictionary<string, bool> opened = new Dictionary<string, bool>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            selectedId = null;
            query = "";
            showDeleted = false;
            opened.Clear();
            ScriptsDialogs.ResetStatics();
        }

        readonly UiContext ctx;
        readonly VisualElement body, sceneList, rolesList, storeList, lastList;
        readonly Label sceneNone, rolesNote, storeNote, lastSummary, lastEvents;
        readonly Button playButton, stopButton, prepareButton, recordButton, renameButton, removeButton, keepButton, exportButton;
        readonly Toggle loopToggle, deletedToggle;
        readonly TextField search;
        readonly Foldout lastFold;
        readonly List<ScriptStoreRow> storeRows = new List<ScriptStoreRow>();
        string sceneShown, rolesShown, storeShown, lastShown;
        string renaming;
        TextField renameField;
        int asking = -1;
        float nextRead, nextStore;
        bool dirty = true, storeDirty = true, hidden, busy, storeUnreadable;

        /// <summary>Reads of the scene so far (tests: none while the panel is closed).</summary>
        public int Reads { get; private set; }
        /// <summary>Reads of the store's rows so far.</summary>
        public int StoreReads { get; private set; }
        /// <summary>The selected scene script's id (null: none).</summary>
        public string SelectedId => selectedId;
        /// <summary>The store's rows the list shows.</summary>
        public IReadOnlyList<ScriptStoreRow> StoreRows => storeRows;
        /// <summary>A play started from the panel waits or loads (its steps are not over).</summary>
        public bool Busy => busy;
        /// <summary>The store row whose «Delete» asks now (-1: none).</summary>
        public int Asking => asking;
        /// <summary>The scene script being renamed (null: none).</summary>
        public string Renaming => renaming;
        /// <summary>The result of the last play the panel started (null: none yet).</summary>
        public ScriptPlayResult LastPanelPlay { get; private set; }
        /// <summary>The result of the last stop the panel made (null: none yet).</summary>
        public ScriptStopResult LastPanelStop { get; private set; }

        public ScriptsPanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "scripts-panel";
            AddToClassList("hs-scripts");
            style.flexGrow = 1;
            focusable = true;
            ScriptsUiModule.Style(this);
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            // shown again (a tab or a region opened): the visibility is read at the next frame, not at the next quarter second
            RegisterCallback<GeometryChangedEvent>(_ => hidden = false);
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "scripts-scroll" };
            scroll.AddToClassList("hs-scripts__scroll");
            Add(scroll);
            body = new VisualElement { name = "scripts-content" };
            scroll.Add(body);

            // the scene's scripts
            var scene = Section("scripts-scene", "script.scene", true);
            sceneNone = Text(scene, "scripts-none");
            sceneList = new VisualElement { name = "scripts-list" };
            sceneList.AddToClassList("hs-scripts__list");
            scene.Add(sceneList);
            var play = Bar("scripts-play-bar");
            playButton = Btn(play, "scripts-play", "script.play", () => Play(ScriptPlayOptions.PrepareAuto, null));
            playButton.AddToClassList("hs-scripts__primary");
            stopButton = Btn(play, "scripts-stop", "script.stop", Stop);
            prepareButton = Btn(play, "scripts-prepare", "script.prepare", () => Play(ScriptPlayOptions.PrepareAlways, null));
            recordButton = Btn(play, "scripts-record", "script.record", Record);
            loopToggle = Switch(play, "scripts-loop", "script.loop", SetLoop);
            scene.Add(play);
            var edit = Bar("scripts-edit-bar");
            renameButton = Btn(edit, "scripts-rename", "script.rename", BeginRename);
            removeButton = Btn(edit, "scripts-remove", "script.remove", Remove);
            keepButton = Btn(edit, "scripts-keep", "script.keep", Keep);
            exportButton = Btn(edit, "scripts-export", "script.export", Export);
            scene.Add(edit);

            // the roles of the selected script
            var roles = Section("scripts-roles", "script.roles", true);
            rolesNote = Text(roles, "scripts-roles-note");
            rolesList = new VisualElement { name = "scripts-roles-list" };
            rolesList.AddToClassList("hs-scripts__list");
            roles.Add(rolesList);

            // the store
            var store = Section("scripts-store", "script.store", true);
            var storeBar = Bar("scripts-store-bar");
            search = PanelKit.SearchField("scripts-store-search", q => { query = q ?? ""; asking = -1; storeDirty = true; });
            search.AddToClassList("hs-scripts__grow");
            storeBar.Add(search);
            deletedToggle = Switch(storeBar, "scripts-show-deleted", "script.showDeleted", on => { showDeleted = on; asking = -1; storeDirty = true; });
            store.Add(storeBar);
            storeNote = Text(store, "scripts-store-note");
            storeList = new VisualElement { name = "scripts-store-list" };
            storeList.AddToClassList("hs-scripts__list");
            store.Add(storeList);
            var storeEnd = Bar("scripts-store-end");
            Btn(storeEnd, "scripts-import", "script.import", Import);
            Btn(storeEnd, "scripts-purge", "script.purge", Purge);
            store.Add(storeEnd);

            // the last play (collapsed by default)
            lastFold = Section("scripts-last", "script.failures", false);
            lastSummary = Text(lastFold, "scripts-last-summary");
            lastEvents = Text(lastFold, "scripts-last-events");
            lastList = new VisualElement { name = "scripts-last-list" };
            lastList.AddToClassList("hs-scripts__list");
            lastFold.Add(lastList);
        }

        // ------------------------------------------------------------ small pieces

        Foldout Section(string id, string titleKey, bool open)
        {
            bool shown = opened.TryGetValue(id, out bool o) ? o : open;
            var fold = InspectorUi.Section(titleKey, shown, id);
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) { opened[id] = e.newValue; if (id == "scripts-store" && e.newValue) storeDirty = true; } });
            body.Add(fold);
            return fold;
        }

        static VisualElement Bar(string id)
        {
            var b = new VisualElement { name = id };
            b.AddToClassList("hs-scripts__bar");
            return b;
        }

        static Button Btn(VisualElement parent, string id, string key, Action click)
        {
            var b = Loc.Button(key, click);
            b.name = id;
            parent?.Add(b);
            return b;
        }

        static Toggle Switch(VisualElement parent, string id, string key, Action<bool> changed)
        {
            var t = new Toggle { name = id };
            t.AddToClassList("hs-scripts__switch");
            Loc.Bind(t, key, (e, s) => ((Toggle)e).label = s);
            t.RegisterValueChangedCallback(e => { if (e.target == t) changed(e.newValue); });
            parent?.Add(t);
            return t;
        }

        static Label Text(VisualElement parent, string id, string cls = "hs-scripts__status")
        {
            var l = new Label { name = id };
            l.AddToClassList(cls);
            parent?.Add(l);
            return l;
        }

        static Label Cell(VisualElement parent, string id, string text, string cls = "hs-scripts__cell")
        {
            var l = new Label(text ?? "") { name = id };
            l.AddToClassList(cls);
            parent?.Add(l);
            return l;
        }

        static void Show(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Note(string key, params object[] args) => ctx.Toasts?.Show(args.Length > 0 ? L10n.Format(key, args) : L10n.Get(key), ToastKind.Info);

        void Error(string code, string message) => ctx.Toasts?.Show(L10n.Format("msg.opFailed", code ?? "error", message ?? ""), ToastKind.Error);

        /// <summary>The panel reads the scene and the store again in its next frame.</summary>
        public void MarkDirty() => dirty = storeDirty = true;

        // ------------------------------------------------------------ the selected script

        SceneScriptList Scripts => ctx.Model?.Scripts;

        /// <summary>The selected scene script (the loaded one, else the first, when none is selected yet), or null.</summary>
        public SceneScript Selected
        {
            get
            {
                var l = Scripts;
                if (l == null || l.Count == 0) return null;
                var s = selectedId != null ? l.Find(selectedId) : null;
                if (s != null) return s;
                string loaded = ctx.Model.Timeline?.Script?.Id;
                s = loaded != null ? l.Find(loaded) : null;
                return s ?? l.Scripts[0];
            }
        }

        int SelectedIndex
        {
            get
            {
                var s = Selected;
                return s == null ? -1 : Scripts.IndexOf(s.Id);
            }
        }

        /// <summary>Selects the scene script <paramref name="id"/> (panel state).</summary>
        public void Select(string id)
        {
            if (renaming != null && renaming != id) EndRename(true);
            selectedId = id;
            dirty = true;
        }

        // ------------------------------------------------------------ play, stop, preparation, record

        /// <summary>Plays the selected script as script.play does with <paramref name="prepare"/> and, when given, <paramref name="record"/> (record.start's arguments).</summary>
        public void Play(string prepare, JObject record)
        {
            var s = Selected;
            if (s == null || busy) return;
            selectedId = s.Id;
            ScriptPlayRun run;
            try { run = ScriptPlayer.Play(ctx.App, s.Id, new ScriptPlayOptions { Prepare = prepare, Record = record, Source = CommandSource.User }); }
            catch (BridgeException e) { Error(e.Code, e.Error?.Message ?? e.Message); MarkDirty(); return; }
            catch (Exception e) { Error("error", e.Message); MarkDirty(); return; }
            if (run.IsDone) { Played(run); return; }
            busy = true;
            run.WhenDone(r => { busy = false; Played(r); });
            MarkDirty();
        }

        void Played(ScriptPlayRun run)
        {
            var r = run.Result;
            LastPanelPlay = r;
            if (ctx.Toasts != null)
            {
                foreach (var w in r.Warnings) ctx.Toasts.Show(w, ToastKind.Warning);
                if (r.Error != null) Error(r.Error.Code, r.Error.Message);
                else if (r.Failures.Count > 0) ctx.Toasts.Show(L10n.Format("script.failedSteps", r.Failures.Count), ToastKind.Warning);
            }
            lastShown = null;
            MarkDirty();
        }

        /// <summary>«Stop»: script.stop with reset (the Timeline stopped, the script commands' slots released, the cast's bodies released and stains dried).</summary>
        public void Stop()
        {
            try
            {
                var r = ScriptPlayer.Stop(ctx.App, true);
                LastPanelStop = r;
                if (ctx.Toasts != null) foreach (var w in r.Warnings) ctx.Toasts.Show(w, ToastKind.Warning);
            }
            catch (BridgeException e) { Error(e.Code, e.Error?.Message ?? e.Message); }
            catch (Exception e) { Error("error", e.Message); }
            MarkDirty();
        }

        void Record()
        {
            var s = Selected;
            if (s == null || busy) return;
            selectedId = s.Id;
            ScriptsDialogs.Record(ctx, rec => Play(ScriptPlayOptions.PrepareAuto, rec));
        }

        // ------------------------------------------------------------ rename in place

        void BeginRename()
        {
            var s = Selected;
            if (s == null || s.IsOpaque) return;
            asking = -1;
            selectedId = s.Id;
            renaming = s.Id;
            sceneShown = null;
            dirty = true;
        }

        void EndRename(bool apply)
        {
            if (renaming == null) return;
            string id = renaming;
            var f = renameField;
            renaming = null;
            renameField = null;
            if (apply && f != null)
            {
                string text = f.value?.Trim();
                int i = Scripts?.IndexOf(id) ?? -1;
                if (!string.IsNullOrEmpty(text) && i >= 0 && text != Scripts.Scripts[i].Name) ctx.ShowResult(OpRename(i, text));
            }
            sceneShown = null;
            MarkDirty();
            Focus();
        }

        // ------------------------------------------------------------ the selected script's other buttons

        void SetLoop(bool on)
        {
            int i = SelectedIndex;
            if (i < 0) return;
            if (Scripts.Scripts[i].Timeline?.Loop == on) return;
            ctx.ShowResult(OpSetLoop(i, on));
            MarkDirty();
        }

        void Remove()
        {
            int i = SelectedIndex;
            if (i < 0) return;
            if (renaming != null) EndRename(false);
            ctx.ShowResult(OpRemove(i));
            selectedId = null;
            MarkDirty();
        }

        void Keep()
        {
            int i = SelectedIndex;
            if (i < 0) return;
            var r = ctx.ShowResult(OpKeep(i));
            if (r != null && r.Ok && r.Value is int no) Note("script.kept", no);
            MarkDirty();
        }

        void Export()
        {
            int i = SelectedIndex;
            if (i < 0) return;
            var s = Scripts.Scripts[i];
            ScriptsDialogs.Export(ctx, s.Name, path =>
            {
                int at = Scripts?.IndexOf(s.Id) ?? -1;
                if (at < 0) return false;
                var r = ctx.ShowResult(OpExport(at, path));
                if (r == null || !r.Ok) return false;
                Note("script.exported", ExportedPath(r) ?? path);
                return true;
            });
        }

        // ------------------------------------------------------------ roles

        /// <summary>Casts <paramref name="role"/> of the selected script to <paramref name="node"/> (null: not cast), as script.cast does: one entry «Cast roles».</summary>
        public void Cast(string role, string node)
        {
            int i = SelectedIndex;
            if (i < 0 || role == null) return;
            ctx.ShowResult(OpCast(i, role, node));
            MarkDirty();
        }

        void UseSelected(ScriptRole role)
        {
            var kind = role.IsItem ? NodeKind.Item : NodeKind.Chara;
            var ids = ctx.SelectedOfKind(kind);
            if (ids.Count == 0) { Note(role.IsItem ? "script.selectItem" : "script.selectCharacter"); return; }
            Cast(role.Role, ids[0]);
        }

        void Pick(ScriptRole role, VisualElement anchor)
        {
            var items = new List<MenuItem>();
            string current = Selected?.CastOf(role.Role);
            foreach (var n in Candidates(role))
            {
                string id = n.Id;
                items.Add(new MenuItem { Id = "scripts-cast-" + id, Text = n.Label ?? id, Checked = id == current, Run = () => Cast(role.Role, id) });
            }
            if (items.Count > 0) items.Add(MenuItem.Sep());
            items.Add(new MenuItem { Id = "scripts-cast-none", Text = L10n.Get("script.uncast"), Checked = current == null, Run = () => Cast(role.Role, null) });
            ctx.Menus?.Show(items, anchor, null, "scripts-cast-menu");
        }

        /// <summary>The nodes a role can be cast to: every character of the scene for a character role; for a prop role the items of its row (every item when it names none).</summary>
        public List<TreeNode> Candidates(ScriptRole role)
        {
            var l = new List<TreeNode>();
            if (role == null || ctx.Model == null) return l;
            foreach (var n in ctx.Model.PreOrder())
            {
                if (role.IsChara && n.Kind == NodeKind.Chara) l.Add(n);
                else if (role.IsItem && n.Kind == NodeKind.Item && (role.Ref == null || ScriptRoles.RefOf(n) == role.Ref)) l.Add(n);
            }
            return l;
        }

        // ------------------------------------------------------------ the store

        void AddRow(ScriptStoreRow row)
        {
            asking = -1;
            var r = ctx.ShowResult(OpAdd(row.No));
            string id = AddedScript(r);
            if (id != null) selectedId = id;
            MarkDirty();
        }

        void Delete(int no)
        {
            if (asking != no)
            {
                asking = no;   // the first press asks
                storeShown = null;
                storeDirty = true;
                return;
            }
            asking = -1;
            ctx.ShowResult(OpDelete(no, false));
            MarkDirty();
        }

        void Restore(int no)
        {
            asking = -1;
            ctx.ShowResult(OpDelete(no, true));
            MarkDirty();
        }

        void Import()
        {
            asking = -1;
            ScriptsDialogs.Import(ctx, path =>
            {
                var r = ctx.ShowResult(OpImport(path));
                if (r == null || !r.Ok) return false;
                int? no = ImportedRow(r);
                if (no != null) Note("script.imported", no.Value);
                MarkDirty();
                return true;
            });
        }

        void Purge()
        {
            asking = -1;
            if (ctx.Dialogs == null) return;
            var preview = OpPurgePreview(out bool unreadable);
            var gone = new List<ScriptStoreRow>();
            foreach (var r in OpStoreRows(null, true, out _)) if (r.Deleted) gone.Add(r);
            ScriptsDialogs.Purge(ctx, gone, preview, unreadable, () =>
            {
                var res = OpPurge(out var r);
                ctx.ShowResult(r);
                if (res != null && r.Ok) storeNote.text = L10n.Format("script.purged", res.Rows, res.Files, BakeUi.Size(res.Bytes));
                MarkDirty();
            });
        }

        // ------------------------------------------------------------ life

        void OnAttached(AttachToPanelEvent e)
        {
            Current = this;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneChanged += OnSceneChanged;
            ctx.SceneCleared -= MarkDirty;
            ctx.SceneCleared += MarkDirty;
            ctx.SceneLoaded -= OnLoaded;
            ctx.SceneLoaded += OnLoaded;
            search.SetValueWithoutNotify(query);
            deletedToggle.SetValueWithoutNotify(showDeleted);
            sceneShown = rolesShown = storeShown = lastShown = null;
            MarkDirty();
        }

        void OnDetached(DetachFromPanelEvent e)
        {
            if (Current == this) Current = null;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneCleared -= MarkDirty;
            ctx.SceneLoaded -= OnLoaded;
            renaming = null;
            renameField = null;
            asking = -1;
        }

        void OnLoaded(SceneLoadedArgs a) => MarkDirty();
        void OnSceneChanged(SceneChangeBatch b) => dirty = true;

        bool ShownNow()
        {
            if (panel == null) return false;
            for (VisualElement e = this; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return false;
            return true;
        }

        void Tick()
        {
            float now = Time.unscaledTime;
            bool due = now >= nextRead;
            if (!due && (hidden || (!dirty && !storeDirty))) return;
            if (due) nextRead = now + RefreshSeconds;
            hidden = !ShownNow();
            if (hidden || (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen)) return;
            Reads++;
            dirty = false;
            ReadScene();
            bool storeOpen = !opened.TryGetValue("scripts-store", out bool so) || so;
            if (storeOpen && (storeDirty || now >= nextStore))
            {
                storeDirty = false;
                nextStore = now + StoreSeconds;
                ReadStore();
            }
        }

        // ------------------------------------------------------------ reading the scene

        void ReadScene()
        {
            var list = Scripts;
            var sel = Selected;
            if (sel != null) selectedId = sel.Id;
            string loaded = ctx.Model?.Timeline?.Script?.Id;
            var tlp = ctx.Runtime?.GetPart<TimelinePlayback>();
            var state = tlp?.Player.State ?? TimelinePlayState.None;
            bool recording = HS2Next.Screenshots.FrameRecorder.IsBusy;

            // the list
            var sb = new StringBuilder();
            if (list != null)
                foreach (var s in list.Scripts)
                {
                    if (s == null) continue;
                    CastCount(s, out int cast, out int all);
                    sb.Append(s.Id).Append('|').Append(s.Name).Append('|').Append(cast).Append('/').Append(all).Append('|').Append(s.Prepared != null).Append('|')
                      .Append(s.Id == loaded).Append('|').Append(s.IsOpaque).Append(';');
                }
            sb.Append(selectedId).Append('/').Append(renaming).Append('/').Append(L10n.Language);
            string now = sb.ToString();
            bool has = list != null && list.Count > 0;
            sceneNone.text = has ? "" : L10n.Get("script.none");
            Show(sceneNone, !has);
            if (now != sceneShown)
            {
                sceneShown = now;
                sceneList.Clear();
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        if (list.Scripts[i] != null) sceneList.Add(SceneLine(list.Scripts[i], i, loaded));
            }

            // the buttons of the selected script
            bool canEdit = sel != null && !sel.IsOpaque;
            playButton.SetEnabled(canEdit && !busy && !recording);
            prepareButton.SetEnabled(canEdit && !busy && !recording && (sel.Prepare?.Count ?? 0) > 0);
            recordButton.SetEnabled(canEdit && !busy && !recording);
            stopButton.SetEnabled(has && (state == TimelinePlayState.Playing || state == TimelinePlayState.Paused || busy || recording));
            loopToggle.SetEnabled(canEdit);
            loopToggle.SetValueWithoutNotify(sel?.Timeline?.Loop ?? false);
            renameButton.SetEnabled(canEdit && renaming == null);
            removeButton.SetEnabled(sel != null);
            keepButton.SetEnabled(canEdit);
            exportButton.SetEnabled(canEdit);

            ReadRoles(sel);
            ReadLast();
        }

        /// <summary>Roles cast to a node of the scene, and all roles, of <paramref name="s"/>.</summary>
        void CastCount(SceneScript s, out int cast, out int all)
        {
            cast = all = 0;
            if (s?.Roles == null || s.IsOpaque) return;
            foreach (var r in s.Roles)
            {
                if (r?.Role == null) continue;
                all++;
                string node = s.CastOf(r.Role);
                if (node != null && ctx.Model.Find(node) != null) cast++;
            }
        }

        VisualElement SceneLine(SceneScript s, int index, string loaded)
        {
            string id = "scripts-row-" + index.ToString(CultureInfo.InvariantCulture);
            var line = new VisualElement { name = id };
            line.AddToClassList("hs-scripts__line");
            if (s.Id == selectedId) line.AddToClassList("hs-scripts__line--selected");
            string sid = s.Id;
            if (renaming == s.Id)
            {
                var f = new TextField { name = id + "-edit", value = s.Name };
                f.AddToClassList("hs-scripts__grow");
                f.AddToClassList(HS2Next.UI.Input.ShortcutRouter.OwnEscapeClass);   // its own Esc cancels the rename
                f.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { EndRename(true); e.StopPropagation(); }
                    else if (e.keyCode == KeyCode.Escape) { EndRename(false); e.StopPropagation(); }
                }, TrickleDown.TrickleDown);
                f.RegisterCallback<FocusOutEvent>(_ => schedule.Execute(() => { if (renaming == sid && renameField == f) EndRename(true); }));
                renameField = f;
                line.Add(f);
                f.schedule.Execute(() => { if (renameField == f) { f.Focus(); f.SelectAll(); } });
                return line;
            }
            line.AddManipulator(new Clickable(() => Select(sid)));
            var l = new Label(s.Name ?? "") { name = id + "-name", tooltip = s.Name };
            l.AddToClassList("hs-scripts__text");
            line.Add(l);
            if (s.IsOpaque) Cell(line, id + "-opaque", L10n.Get("script.newer"), "hs-scripts__cell");
            else
            {
                CastCount(s, out int cast, out int all);
                var c = Cell(line, id + "-cast", L10n.Format("script.castCount", cast, all));
                if (cast < all) c.AddToClassList("hs-scripts__warn");
                if (s.Prepared != null) Cell(line, id + "-prepared", L10n.Get("script.prepared"), "hs-scripts__dim");
            }
            if (s.Id != null && s.Id == loaded) Cell(line, id + "-loaded", L10n.Get("script.loaded"), "hs-scripts__mark");
            return line;
        }

        // ------------------------------------------------------------ reading the roles

        void ReadRoles(SceneScript s)
        {
            var sb = new StringBuilder();
            sb.Append(s?.Id).Append('|').Append(L10n.Language).Append('|');
            if (s != null && !s.IsOpaque && s.Roles != null)
                foreach (var r in s.Roles)
                {
                    if (r?.Role == null) continue;
                    string node = s.CastOf(r.Role);
                    var n = node != null ? ctx.Model.Find(node) : null;
                    sb.Append(r.Role).Append(':').Append(r.Kind).Append(':').Append(r.Sex).Append(':').Append(r.Ref).Append(':').Append(r.Hint?.Name).Append(':')
                      .Append(n?.Id).Append(':').Append(n?.Label).Append(';');
                }
            string now = sb.ToString();
            string note = s == null ? (Scripts?.Count > 0 ? L10n.Get("script.selectScript") : "") : s.IsOpaque ? L10n.Get("script.newer") : (s.Roles?.Count ?? 0) == 0 ? L10n.Get("script.noRoles") : "";
            rolesNote.text = note;
            Show(rolesNote, note.Length > 0);
            if (now == rolesShown) return;
            rolesShown = now;
            rolesList.Clear();
            if (s == null || s.IsOpaque || s.Roles == null) return;
            foreach (var r in s.Roles)
                if (r?.Role != null) rolesList.Add(RoleLine(s, r));
        }

        VisualElement RoleLine(SceneScript s, ScriptRole r)
        {
            string id = "scripts-role-" + r.Role;
            var line = new VisualElement { name = id };
            line.AddToClassList("hs-scripts__line");
            string node = s.CastOf(r.Role);
            var n = node != null ? ctx.Model.Find(node) : null;
            if (n == null) line.AddToClassList("hs-scripts__line--uncast");
            Cell(line, id + "-role", r.Role).style.minWidth = 60;
            Cell(line, id + "-kind", r.IsItem ? L10n.Get("script.kind.item") : r.IsChara ? L10n.Get("script.kind.chara") : r.Kind ?? "", "hs-scripts__cell");
            string what = r.IsItem ? (r.Hint?.Name ?? r.Ref ?? "") : r.Sex == ScriptRole.SexFemale ? L10n.Get("script.sex.female") : r.Sex == ScriptRole.SexMale ? L10n.Get("script.sex.male") : L10n.Get("script.sex.any");
            Cell(line, id + "-what", what, "hs-scripts__dim").tooltip = r.IsItem ? r.Ref : null;
            var pick = new Button { name = id + "-pick", text = n != null ? (n.Label ?? n.Id) : L10n.Get("script.uncast") };
            pick.AddToClassList("hs-scripts__cast");
            pick.tooltip = n?.Id;
            var role = r;
            pick.clicked += () => Pick(role, pick);
            line.Add(pick);
            Btn(line, id + "-use", "script.role.use", () => UseSelected(role));
            if (!string.IsNullOrEmpty(r.Hint?.Name) && !r.IsItem) Cell(line, id + "-hint", "(" + r.Hint.Name + ")", "hs-scripts__dim");
            return line;
        }

        // ------------------------------------------------------------ reading the last play

        void ReadLast()
        {
            var pb = ctx.Runtime?.GetPart<ScriptPlayback>();
            var tlp = ctx.Runtime?.GetPart<TimelinePlayback>();
            var lp = pb?.LastPlay;
            long errors = tlp?.EventErrors ?? 0;
            var sb = new StringBuilder();
            sb.Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(lp ?? (object)"")).Append('|').Append(lp?.Failures.Count).Append('|').Append(errors).Append('|').Append(L10n.Language);
            string now = sb.ToString();
            if (now == lastShown) return;
            lastShown = now;
            lastList.Clear();
            if (lp == null)
            {
                lastSummary.text = L10n.Get("script.noPlay");
                Show(lastEvents, false);
                return;
            }
            lastSummary.text = L10n.Format("script.lastSummary", lp.SetupSent, lp.SetupFailed, lp.PrepareSent, lp.PrepareFailed, lp.Skipped);
            lastEvents.text = L10n.Format("script.eventErrors", errors);
            Show(lastEvents, errors > 0);
            if (lp.Failures.Count == 0)
            {
                Cell(lastList, "scripts-last-none", L10n.Get("script.noFailures"), "hs-scripts__status");
                return;
            }
            for (int i = 0; i < lp.Failures.Count; i++)
            {
                var f = lp.Failures[i];
                string id = "scripts-last-" + i.ToString(CultureInfo.InvariantCulture);
                var line = new VisualElement { name = id };
                line.AddToClassList("hs-scripts__line");
                Cell(line, id + "-phase", PhaseText(f.Phase));
                Cell(line, id + "-step", L10n.Format("script.step", f.Index + 1));
                Cell(line, id + "-cmd", f.Cmd ?? "");
                var m = new Label((f.Code ?? "") + ": " + (f.Message ?? "")) { name = id + "-message", tooltip = f.Message };
                m.AddToClassList("hs-scripts__text");
                line.Add(m);
                lastList.Add(line);
            }
        }

        /// <summary>A play's phase as words: «Set-up», «Preparation», «Cast», «Load», «Play».</summary>
        public static string PhaseText(string phase) =>
            phase == ScriptPlayResult.PhaseSetup || phase == ScriptPlayResult.PhasePrepare || phase == ScriptPlayResult.PhaseCast || phase == ScriptPlayResult.PhaseLoad || phase == ScriptPlayResult.PhasePlay
                ? L10n.Get("script.phase." + phase) : phase ?? "";

        // ------------------------------------------------------------ reading the store

        void ReadStore()
        {
            StoreReads++;
            storeRows.Clear();
            storeRows.AddRange(OpStoreRows(query, showDeleted, out storeUnreadable));
            if (storeUnreadable) storeNote.text = L10n.Get("script.storeUnreadable");
            var sb = new StringBuilder();
            foreach (var x in storeRows) sb.Append(x.No).Append('|').Append(x.Name).Append('|').Append(x.Deleted).Append('|').Append(x.Modified.Ticks).Append(';');
            sb.Append(asking).Append('/').Append(L10n.Language).Append('/').Append(storeUnreadable);
            string now = sb.ToString();
            if (now == storeShown) return;
            storeShown = now;
            storeList.Clear();
            if (storeRows.Count == 0)
            {
                var empty = Loc.Label(showDeleted || query.Length > 0 ? "script.storeNoMatch" : "script.storeEmpty");
                empty.name = "scripts-store-empty";
                empty.AddToClassList("hs-scripts__status");
                storeList.Add(empty);
            }
            foreach (var x in storeRows) storeList.Add(StoreLine(x));
        }

        VisualElement StoreLine(ScriptStoreRow x)
        {
            int no = x.No;
            string id = "scripts-store-row-" + no.ToString(CultureInfo.InvariantCulture);
            var line = new VisualElement { name = id };
            line.AddToClassList("hs-scripts__line");
            if (x.Deleted) line.AddToClassList("hs-scripts__line--deleted");
            var text = new StringBuilder();
            text.Append(no.ToString(CultureInfo.InvariantCulture)).Append("  ").Append(x.Name);
            text.Append("  ").Append(x.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (x.Deleted) text.Append("  ").Append(L10n.Get("script.deletedMark"));
            var l = new Label(text.ToString()) { name = id + "-name", tooltip = x.Name };
            l.AddToClassList("hs-scripts__text");
            line.Add(l);
            if (x.Deleted) Btn(line, id + "-restore", "script.restore", () => Restore(no));
            else
            {
                var row = x;
                Btn(line, id + "-add", "script.add", () => AddRow(row));
                Btn(line, id + "-delete", asking == no ? "script.deleteAsk" : "script.delete", () => Delete(no));
            }
            return line;
        }
    }
}
