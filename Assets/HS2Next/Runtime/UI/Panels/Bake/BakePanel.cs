// The panel «Chara to Item» (panel id "bake"). From the top: the target characters (the selected ones, primary first, taken by «Use selected»); the parts of the primary one as
// switches with the presets «Whole», «Body», «Hair», «Clothes», «Accessories» and «One item per part»; the options (the seven FK groups, «Fill hollow parts», «Origin at the parts'
// centre», «Hide the character», «Keep in the store», «Name»); «Bake» with its progress line; the store: its rows (number, name, kind, size, created) with a search, «Add»,
// «Rename» in place (Enter confirms, Esc cancels), «Delete» (the first press asks, the second deletes), «Show deleted», «Restore», «Purge…» (a dialog with what it removes) and
// «Import old bakes…» from a folder. Everything goes through StudioOps, so the panel works with the bridge off; it writes neither the store nor the scene itself. The panel's
// values are panel state, kept for the session, never in the scene. Subscribed from attach to detach: a closed panel reads nothing, and it reads four times a second only while
// it is shown. A row's blobs are checked on a worker; a row whose blobs are missing or do not hold what their names say shows "damaged". «Add» takes the row straight from
// the store (bake add), so the store's rows stay reachable here when a list file holds the app's item group number.
// Element names: bake-none, bake-target, bake-use-selected, bake-parts-note, bake-preset-<whole|body|hair|clothes|accs>, bake-part-<key>, bake-split, bake-fk-<group>, bake-fill,
// bake-centre, bake-hide, bake-keep, bake-name, bake-make, bake-progress, bake-store-search, bake-show-deleted, bake-store-note, bake-store-list, bake-row-<no> (-name, -add,
// -rename, -edit, -delete, -restore), bake-purge, bake-import-folder, bake-import, section-<title key>; the purge dialog dialog-bake-purge with bake-purge-row-<no>.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class BakePanel : VisualElement
    {
        public const float RefreshSeconds = 0.25f;
        /// <summary>The seven FK groups' label keys, in the order of StudioOps.BakeFkGroupNames.</summary>
        public static readonly string[] FkKeys = { "hair", "neck", "breast", "body", "rightHand", "leftHand", "skirt" };
        public static readonly string[] Presets = { "whole", "body", "hair", "clothes", "accs" };

        /// <summary>The panel on the shell now, or null.</summary>
        public static BakePanel Current { get; private set; }

        // panel state: kept across rebuilds of the panel for the session, never in the scene
        static readonly List<string> targets = new List<string>();
        static readonly HashSet<string> chosen = new HashSet<string>(StringComparer.Ordinal);
        static readonly bool[] fk = { true, true, true, true, true, true, true };
        static bool split, fill = true, centre = true, hide = true, keep = true, showDeleted;
        static string importFolder, query = "";
        static readonly Dictionary<string, bool> opened = new Dictionary<string, bool>(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            targets.Clear();
            chosen.Clear();
            for (int i = 0; i < fk.Length; i++) fk[i] = true;
            split = showDeleted = false;
            fill = centre = hide = keep = true;
            importFolder = null;
            query = "";
            opened.Clear();
        }

        readonly UiContext ctx;
        readonly VisualElement body, partsList, storeList;
        readonly Label none, targetLine, partsNote, progress, storeNote;
        readonly Button makeButton, importButton;
        readonly Toggle splitToggle, fillToggle, centreToggle, hideToggle, keepToggle, deletedToggle;
        readonly Toggle[] fkToggles = new Toggle[7];
        readonly Dictionary<string, Button> presetButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly Dictionary<string, Toggle> partToggles = new Dictionary<string, Toggle>(StringComparer.Ordinal);
        readonly TextField nameField, importField, search;
        readonly List<BakePartRow> partRows = new List<BakePartRow>();
        readonly List<BakeStoreRow> storeRows = new List<BakeStoreRow>();
        // the damage check of the store's rows, by number and main blob: done on a worker
        readonly Dictionary<string, bool> damaged = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly HashSet<string> checking = new HashSet<string>(StringComparer.Ordinal);
        readonly ConcurrentQueue<(string key, bool bad)> checkedRows = new ConcurrentQueue<(string, bool)>();
        string partsShown, storeShown;
        int renaming = -1, asking = -1, revision = -1;
        TextField renameField;
        float nextRead;
        bool dirty = true, storeDirty = true, hidden, busy;

        /// <summary>Reads of the scene and the store so far (tests: none while the panel is closed).</summary>
        public int Reads { get; private set; }
        /// <summary>Reads of the store's rows so far.</summary>
        public int StoreReads { get; private set; }
        /// <summary>The target characters, primary first.</summary>
        public IReadOnlyList<string> Targets => targets;
        /// <summary>The chosen parts; none: the whole character.</summary>
        public IReadOnlyCollection<string> Chosen => chosen;
        /// <summary>The store's rows the list shows.</summary>
        public IReadOnlyList<BakeStoreRow> StoreRows => storeRows;
        /// <summary>A bake or an import is under way.</summary>
        public bool Busy => busy;
        /// <summary>The row whose «Delete» asks now (-1: none).</summary>
        public int Asking => asking;
        /// <summary>The row being renamed (-1: none).</summary>
        public int Renaming => renaming;
        /// <summary>The rows known damaged.</summary>
        public int DamagedCount { get { int n = 0; foreach (var v in damaged.Values) if (v) n++; return n; } }
        /// <summary>The rows whose damage check has ended.</summary>
        public int CheckedCount => damaged.Count;

        public BakePanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "bake-panel";
            AddToClassList("hs-bake");
            style.flexGrow = 1;
            focusable = true;
            BakeUiModule.Style(this);
            RegisterCallback<AttachToPanelEvent>(OnAttached);
            RegisterCallback<DetachFromPanelEvent>(OnDetached);
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "bake-scroll" };
            scroll.AddToClassList("hs-bake__scroll");
            Add(scroll);
            body = new VisualElement { name = "bake-content" };
            scroll.Add(body);

            // the target
            var targetBar = BakeUi.Bar("bake-target-bar");
            targetLine = BakeUi.Text(targetBar, "bake-target", "hs-bake__grow");
            BakeUi.Btn(targetBar, "bake-use-selected", "bake.useSelected", UseSelected);
            body.Add(targetBar);
            none = BakeUi.Text(body, "bake-none");

            // the parts
            var parts = Section("bake.parts", true);
            var presets = BakeUi.Bar("bake-presets");
            foreach (string p in Presets)
            {
                string which = p;
                presetButtons[p] = BakeUi.Btn(presets, "bake-preset-" + p, p == "whole" ? "bake.whole" : "bake.preset." + p, () => Preset(which));
            }
            parts.Add(presets);
            partsNote = BakeUi.Text(parts, "bake-parts-note");
            partsList = new VisualElement { name = "bake-parts-list" };
            partsList.AddToClassList("hs-bake__list");
            parts.Add(partsList);
            splitToggle = BakeUi.Switch(parts, "bake-split", "bake.split", on => split = on);

            // the options
            var options = Section("bake.options", true);
            var fkBox = new VisualElement { name = "bake-fk" };
            fkBox.AddToClassList("hs-bake__bar");
            var fkTitle = Loc.Label("bake.fk");
            fkTitle.AddToClassList("hs-bake__caption");
            options.Add(fkTitle);
            for (int i = 0; i < fk.Length; i++)
            {
                int k = i;
                fkToggles[i] = BakeUi.Switch(fkBox, "bake-fk-" + FkKeys[i], "inspector.chara.fk.group." + FkKeys[i], on => fk[k] = on);
            }
            options.Add(fkBox);
            fillToggle = BakeUi.Switch(options, "bake-fill", "bake.fill", on => fill = on);
            centreToggle = BakeUi.Switch(options, "bake-centre", "bake.centre", on => centre = on);
            hideToggle = BakeUi.Switch(options, "bake-hide", "bake.hide", on => hide = on);
            keepToggle = BakeUi.Switch(options, "bake-keep", "bake.keep", on => keep = on);
            nameField = new TextField { name = "bake-name" };
            Loc.Bind(nameField, "bake.name", (e, s) => ((TextField)e).label = s);
            options.Add(nameField);

            var make = BakeUi.Bar("bake-make-bar");
            makeButton = BakeUi.Btn(make, "bake-make", "bake.make", Bake);
            makeButton.AddToClassList("hs-bake__primary");
            body.Add(make);
            progress = BakeUi.Text(body, "bake-progress");

            // the store
            var store = Section("bake.store", true);
            var storeBar = BakeUi.Bar("bake-store-bar");
            search = PanelKit.SearchField("bake-store-search", q => { query = q ?? ""; storeDirty = true; });
            search.AddToClassList("hs-bake__grow");
            storeBar.Add(search);
            deletedToggle = BakeUi.Switch(storeBar, "bake-show-deleted", "bake.showDeleted", on => { showDeleted = on; asking = -1; storeDirty = true; });
            store.Add(storeBar);
            storeNote = BakeUi.Text(store, "bake-store-note");
            storeList = new VisualElement { name = "bake-store-list" };
            storeList.AddToClassList("hs-bake__list");
            store.Add(storeList);
            var storeEnd = BakeUi.Bar("bake-store-end");
            BakeUi.Btn(storeEnd, "bake-purge", "bake.purge", Purge);
            store.Add(storeEnd);
            var importBar = BakeUi.Bar("bake-import-bar");
            importField = new TextField { name = "bake-import-folder" };
            importField.AddToClassList("hs-bake__grow");
            Loc.Bind(importField, "bake.importFolder", (e, s) => ((TextField)e).label = s);
            importField.RegisterValueChangedCallback(e => { if (e.target == importField) importFolder = e.newValue; });
            importBar.Add(importField);
            importButton = BakeUi.Btn(importBar, "bake-import", "bake.import", Import);
            store.Add(importBar);
        }

        Foldout Section(string titleKey, bool open)
        {
            bool shown = opened.TryGetValue(titleKey, out bool o) ? o : open;
            var fold = InspectorUi.Section(titleKey, shown, "section-" + titleKey);
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) opened[titleKey] = e.newValue; });
            body.Add(fold);
            return fold;
        }

        /// <summary>The panel reads the scene and the store again in its next frame.</summary>
        public void MarkDirty() => dirty = storeDirty = true;

        // ------------------------------------------------------------ the target and the parts

        /// <summary>Makes <paramref name="characters"/> the target (characters only, primary first).</summary>
        public void UseTargets(IReadOnlyList<string> characters)
        {
            targets.Clear();
            foreach (var id in characters ?? Array.Empty<string>())
                if (ctx.Model.Find(id)?.Kind == NodeKind.Chara && !targets.Contains(id)) targets.Add(id);
            partsShown = null;
            dirty = true;
        }

        void UseSelected()
        {
            asking = -1;
            UseTargets(ctx.SelectedOfKind(NodeKind.Chara));
            if (targets.Count == 0) Note("bake.selectCharacter");
        }

        void Preset(string which)
        {
            asking = -1;
            chosen.Clear();
            if (which != "whole")
                foreach (var row in partRows)
                {
                    string k = row.Part;
                    bool hair = k.StartsWith("hair", StringComparison.Ordinal), acc = k.StartsWith("acc", StringComparison.Ordinal);
                    bool take = which switch
                    {
                        "body" => k == "body" || k == "head" || hair || (acc && row.HairType),
                        "hair" => hair || (acc && row.HairType),
                        "clothes" => k.StartsWith("clothes", StringComparison.Ordinal),
                        "accs" => acc && !row.HairType,
                        _ => false,
                    };
                    if (take) chosen.Add(k);
                }
            ShowChoice();
        }

        void ShowChoice()
        {
            foreach (var kv in partToggles) kv.Value.SetValueWithoutNotify(chosen.Contains(kv.Key));
            presetButtons["whole"].EnableInClassList("hs-bake__on", chosen.Count == 0);
        }

        /// <summary>The chosen parts in the order the primary character lists them, then those it lacks.</summary>
        List<string> ChosenInOrder()
        {
            var l = new List<string>();
            foreach (var r in partRows) if (chosen.Contains(r.Part)) l.Add(r.Part);
            foreach (var k in chosen) if (!l.Contains(k)) l.Add(k);
            return l;
        }

        /// <summary>A part's label: «Body», «Head», the hair slots, the clothes kinds, «Accessory n» with «(hair)», «Other».</summary>
        public static string PartLabel(string key, bool hairType)
        {
            if (key == null) return "";
            if (key == "body" || key == "head" || key == "other") return L10n.Get("bake.part." + key);
            if (key.StartsWith("hair", StringComparison.Ordinal)) return L10n.Get("bake.part." + key);
            if (key.StartsWith("clothes", StringComparison.Ordinal) && key.Length == 8)
            {
                string[] kinds = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };
                int k = key[7] - '0';
                return k >= 0 && k < kinds.Length ? L10n.Get("inspector.charaEdit.clothes.kind." + kinds[k]) : key;
            }
            if (key.StartsWith("acc", StringComparison.Ordinal) && int.TryParse(key.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
                return L10n.Format(hairType ? "bake.part.accHair" : "bake.part.acc", slot + 1);
            return key;
        }

        // ------------------------------------------------------------ bake

        /// <summary>The panel's options for a bake of the whole character (the State tab's «Bake whole»): its FK groups, insides, origin, hiding and store, no parts, no name.</summary>
        public static BakeMakeOptions WholeOptions() => new BakeMakeOptions
        {
            Parts = Array.Empty<string>(), Split = false, Name = "", FkGroups = (bool[])fk.Clone(), Insides = fill, Centre = centre, Hide = hide, Store = keep,
        };

        void Bake()
        {
            asking = -1;
            if (busy || targets.Count == 0) return;
            if (!StudioOps.BakeHasDevice) { Note("bake.noDevice"); return; }
            busy = true;
            makeButton.SetEnabled(false);
            progress.text = L10n.Get("bake.capturing");
            BakeUi.Show(progress, true);
            var ids = new List<string>(targets);
            var opt = new BakeMakeOptions
            {
                Parts = ChosenInOrder(), Split = split, Name = nameField.value, FkGroups = (bool[])fk.Clone(), Insides = fill, Centre = centre, Hide = hide, Store = keep,
            };
            // the line shows before the capture holds the frame
            UiDefer.Frames(2, () =>
            {
                OpResult r = null;
                try { r = ctx.Ops.BakeMake(ids, opt, UiContext.User(OpLabels.BakeMake)); }
                finally { busy = false; }
                ctx.ShowResult(r);
                if (r.Ok && r.Value is BakeMakeResult made)
                {
                    BakeUiModule.NoteBakes(ctx, ids, made);
                    nameField.SetValueWithoutNotify("");   // the name is used once
                    progress.text = L10n.Format("bake.made", made.Items.Count);
                }
                else progress.text = "";
                MarkDirty();
            });
        }

        // ------------------------------------------------------------ the store

        // straight from the store: also when a list file holds the app's group number and the item list has no row for the store
        void AddRow(BakeStoreRow row)
        {
            asking = -1;
            PanelKit.ApplyPlacement(ctx);
            ctx.ShowResult(ctx.Ops.BakeAdd(row.Number, null, null, -1, UiContext.User(OpLabels.AddItem)));
            MarkDirty();
        }

        void BeginRename(int number)
        {
            asking = -1;
            renaming = number;
            storeShown = null;
            storeDirty = true;
        }

        void EndRename(bool apply)
        {
            if (renaming < 0) return;
            int no = renaming;
            var f = renameField;
            renaming = -1;
            renameField = null;
            if (apply && f != null)
            {
                string text = f.value?.Trim();
                var row = storeRows.Find(r => r.Number == no);
                if (!string.IsNullOrEmpty(text) && row != null && text != row.Name)
                    ctx.ShowResult(ctx.Ops.BakeRename(no, null, text, UiContext.User(OpLabels.BakeRename)));
            }
            storeShown = null;
            MarkDirty();
            Focus();
        }

        void Delete(int number)
        {
            if (asking != number)
            {
                asking = number;   // the first press asks
                storeShown = null;
                storeDirty = true;
                return;
            }
            asking = -1;
            ctx.ShowResult(ctx.Ops.BakeDelete(number, null));
            MarkDirty();
        }

        void Restore(int number)
        {
            asking = -1;
            ctx.ShowResult(ctx.Ops.BakeDelete(number, null, true));
            MarkDirty();
        }

        void Purge()
        {
            asking = -1;
            if (ctx.Dialogs == null) return;
            var preview = ctx.Ops.BakePurgePreview().Value as BakePurgeResult ?? new BakePurgeResult();
            var gone = new List<BakeStoreRow>();
            if (ctx.Ops.BakeList(null, true).Value is BakeListResult all)
                foreach (var r in all.Rows) if (r.Deleted) gone.Add(r);
            var list = new VisualElement { name = "bake-purge-list" };
            foreach (var r in gone)
            {
                var l = new Label(r.Number.ToString(CultureInfo.InvariantCulture) + "  " + r.Name) { name = "bake-purge-row-" + r.Number };
                l.AddToClassList("hs-bake__text");
                list.Add(l);
            }
            bool nothing = preview.Rows == 0 && preview.Blobs == 0 && preview.Leftovers == 0;
            var spec = new DialogSpec
            {
                Id = "bake-purge", Title = L10n.Get("bake.purge"), Content = list,
                Message = nothing ? L10n.Get("bake.purgeNone") : L10n.Format("bake.purgeWhat", preview.Rows, preview.Blobs + preview.Leftovers, BakeUi.Size(preview.Bytes)),
            };
            if (!nothing) spec.Buttons.Add(DialogButton.Of("purge", "bake.purgeOk", true));
            spec.Buttons.Add(nothing ? DialogButton.Of("cancel", "common.close", true, true) : DialogButton.Cancel());
            ctx.Dialogs.Show(spec, id =>
            {
                if (id != "purge") return;
                var r = ctx.ShowResult(ctx.Ops.BakePurge());
                if (r.Ok && r.Value is BakePurgeResult p) storeNote.text = L10n.Format("bake.purged", p.Rows, p.Blobs + p.Leftovers, BakeUi.Size(p.Bytes));
                MarkDirty();
            });
        }

        void Import()
        {
            asking = -1;
            if (busy) return;
            string folder = (importField.value ?? "").Trim();
            if (folder.Length == 0) { Note("bake.importFolderFirst"); return; }
            busy = true;
            importButton.SetEnabled(false);
            storeNote.text = L10n.Get("bake.importing");
            UiDefer.Frames(2, () =>
            {
                OpResult r = null;
                try { r = ctx.Ops.BakeImport(folder); }
                finally { busy = false; importButton.SetEnabled(true); }
                ctx.ShowResult(r);
                storeNote.text = r.Value is OldBakeImportResult res ? L10n.Format("bake.imported", res.Imported.Count, res.Skipped.Count) : "";
                MarkDirty();
            });
        }

        void Note(string key) => ctx.Toasts?.Show(ctx.L10n.Get(key), ToastKind.Info);

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
            if (targets.Count == 0) UseTargets(ctx.SelectedOfKind(NodeKind.Chara));
            if (importFolder == null) importFolder = OldBakeImport.DefaultFolder(ctx.App.Paths?.Hs2Root) ?? "";
            importField.SetValueWithoutNotify(importFolder);
            search.SetValueWithoutNotify(query);
            MarkDirty();
        }

        void OnDetached(DetachFromPanelEvent e)
        {
            if (Current == this) Current = null;
            ctx.SceneChanged -= OnSceneChanged;
            ctx.SceneCleared -= MarkDirty;
            ctx.SceneLoaded -= OnLoaded;
            renaming = asking = -1;
            renameField = null;
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
            while (checkedRows.TryDequeue(out var c))
            {
                checking.Remove(c.key);
                damaged[c.key] = c.bad;
                storeShown = null;
                storeDirty = true;
            }
            float now = Time.unscaledTime;
            bool due = now >= nextRead;
            if (!due && (hidden || (!dirty && !storeDirty))) return;
            if (due) nextRead = now + RefreshSeconds;
            hidden = !ShownNow();
            if (hidden || (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen)) return;
            Reads++;
            int rev = ctx.App.Db?.Generated.Revision ?? 0;
            if (rev != revision) { revision = rev; storeDirty = true; }
            if (dirty)
            {
                dirty = false;
                ReadTarget();
            }
            if (storeDirty)
            {
                storeDirty = false;
                ReadStore();
            }
        }

        // ------------------------------------------------------------ reading

        void ReadTarget()
        {
            targets.RemoveAll(id => ctx.Model.Find(id)?.Kind != NodeKind.Chara);
            bool has = targets.Count > 0;
            var names = new List<string>();
            foreach (var id in targets) names.Add(ctx.Model.Find(id)?.Label ?? id);
            targetLine.text = has ? L10n.Format("bake.target", string.Join(", ", names)) : "";
            none.text = has ? "" : L10n.Get("bake.selectCharacter");
            BakeUi.Show(none, !has);
            makeButton.SetEnabled(has && !busy);
            fillToggle.SetValueWithoutNotify(fill);
            centreToggle.SetValueWithoutNotify(centre);
            hideToggle.SetValueWithoutNotify(hide);
            keepToggle.SetValueWithoutNotify(keep);
            splitToggle.SetValueWithoutNotify(split);
            deletedToggle.SetValueWithoutNotify(showDeleted);
            for (int i = 0; i < fk.Length; i++) fkToggles[i].SetValueWithoutNotify(fk[i]);
            ReadParts(has ? targets[0] : null);
        }

        void ReadParts(string primary)
        {
            partRows.Clear();
            string note = "";
            if (primary != null)
            {
                var r = ctx.Ops.BakeParts(primary);
                if (r.Ok && r.Value is List<BakePartRow> rows) partRows.AddRange(rows);
                else note = L10n.Get("bake.partsNotBuilt");
            }
            partsNote.text = note;
            BakeUi.Show(partsNote, note.Length > 0);
            var sb = new StringBuilder();
            foreach (var p in partRows) sb.Append(p.Part).Append(p.HairType ? "*" : "").Append(';');
            string now = sb.ToString() + L10n.Language;
            if (now != partsShown)
            {
                partsShown = now;
                partsList.Clear();
                partToggles.Clear();
                foreach (var p in partRows)
                {
                    string key = p.Part;
                    var t = new Toggle { name = "bake-part-" + key, label = PartLabel(key, p.HairType) };
                    t.AddToClassList("hs-bake__switch");
                    t.RegisterValueChangedCallback(e =>
                    {
                        if (e.target != t) return;
                        if (e.newValue) chosen.Add(key); else chosen.Remove(key);
                        ShowChoice();
                    });
                    partsList.Add(t);
                    partToggles[key] = t;
                }
            }
            ShowChoice();
        }

        static string RowKey(BakeStoreRow r) => r.Number.ToString(CultureInfo.InvariantCulture) + "/" + r.Main;

        void ReadStore()
        {
            StoreReads++;
            storeRows.Clear();
            var r = ctx.Ops.BakeList(query, showDeleted);
            if (r.Value is BakeListResult list) storeRows.AddRange(list.Rows);
            string warn = r.Warnings.Count > 0 ? L10n.Get("bake.storeUnreadable") : "";
            if (warn.Length > 0) storeNote.text = warn;
            CheckDamage();
            var sb = new StringBuilder();
            foreach (var x in storeRows)
            {
                damaged.TryGetValue(RowKey(x), out bool bad);
                sb.Append(x.Number).Append('|').Append(x.Name).Append('|').Append(x.Deleted).Append('|').Append(x.Bytes).Append('|').Append(bad).Append(';');
            }
            sb.Append(renaming).Append('/').Append(asking).Append('/').Append(L10n.Language);
            string now = sb.ToString();
            if (now == storeShown) return;
            storeShown = now;
            storeList.Clear();
            if (storeRows.Count == 0)
            {
                var empty = Loc.Label(showDeleted || query.Length > 0 ? "bake.storeNoMatch" : "bake.storeEmpty");
                empty.name = "bake-store-empty";
                empty.AddToClassList("hs-bake__status");
                storeList.Add(empty);
            }
            foreach (var x in storeRows) storeList.Add(RowLine(x));
        }

        // the blobs of rows not checked yet are read on a worker; the list shows "damaged" when they come back bad
        void CheckDamage()
        {
            BakeStore store = null;
            foreach (var x in storeRows)
            {
                string key = RowKey(x);
                if (damaged.ContainsKey(key) || checking.Contains(key)) continue;
                store ??= ctx.Ops.OpenBakeStore();
                if (store == null) return;
                checking.Add(key);
                var row = x;
                var s = store;
                var q = checkedRows;
                Task.Run(() =>
                {
                    bool bad;
                    try { bad = s.IsDamaged(row); }
                    catch (Exception) { bad = true; }
                    q.Enqueue((key, bad));
                });
            }
        }

        VisualElement RowLine(BakeStoreRow x)
        {
            int no = x.Number;
            string id = "bake-row-" + no.ToString(CultureInfo.InvariantCulture);
            var line = new VisualElement { name = id };
            line.AddToClassList("hs-bake__line");
            if (x.Deleted) line.AddToClassList("hs-bake__line--deleted");
            if (renaming == no)
            {
                var f = new TextField { name = id + "-edit", value = x.Name };
                f.AddToClassList("hs-bake__grow");
                f.AddToClassList(HS2Next.UI.Input.ShortcutRouter.OwnEscapeClass);   // its own Esc cancels the rename
                f.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { EndRename(true); e.StopPropagation(); }
                    else if (e.keyCode == KeyCode.Escape) { EndRename(false); e.StopPropagation(); }
                }, TrickleDown.TrickleDown);
                f.RegisterCallback<FocusOutEvent>(_ => schedule.Execute(() => { if (renaming == no && renameField == f) EndRename(true); }));
                renameField = f;
                line.Add(f);
                f.schedule.Execute(() => { if (renameField == f) { f.Focus(); f.SelectAll(); } });
            }
            else
            {
                damaged.TryGetValue(RowKey(x), out bool bad);
                var text = new StringBuilder();
                text.Append(no.ToString(CultureInfo.InvariantCulture)).Append("  ").Append(x.Name);
                text.Append("  ").Append(KindText(x.Kind)).Append("  ").Append(BakeUi.Size(x.Bytes));
                text.Append("  ").Append(x.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                if (x.Deleted) text.Append("  ").Append(L10n.Get("bake.deletedMark"));
                if (bad) text.Append("  ").Append(L10n.Get("bake.damaged"));
                var l = new Label(text.ToString()) { name = id + "-name", tooltip = x.Name };
                l.AddToClassList("hs-bake__text");
                line.Add(l);
            }
            if (x.Deleted) BakeUi.Btn(line, id + "-restore", "bake.restore", () => Restore(no));
            else
            {
                BakeUi.Btn(line, id + "-add", "bake.add", () => AddRow(x));
                if (renaming != no) BakeUi.Btn(line, id + "-rename", "bake.rename", () => BeginRename(no));
                BakeUi.Btn(line, id + "-delete", asking == no ? "bake.deleteAsk" : "bake.delete", () => Delete(no));
            }
            return line;
        }

        /// <summary>A bake's kind as words.</summary>
        public static string KindText(string kind) =>
            kind == BakeKinds.Chara || kind == BakeKinds.Parts || kind == BakeKinds.Item || kind == BakeKinds.Piece || kind == BakeKinds.Imported ? L10n.Get("bake.kind." + kind) : kind ?? "";
    }
}
