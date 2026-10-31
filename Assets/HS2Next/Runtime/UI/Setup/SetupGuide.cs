// The first-run guide's pages (welcome / language, your files and the copy offer, the HS2 folder, more mods folders, the cache
// folder, building the index, ready) with Back / Next, Quit on every page and Skip setup on pages 1-4. Every control has a stable
// element name (setup.page, setup.next, setup.back, setup.skip, setup.quit, setup.finish, setup.hs2.path, setup.hs2.browse,
// setup.hs2.suggestion.<n>, setup.hs2.status, setup.mods.add, setup.cache.path, setup.migrate.copy, setup.progress, …) for the
// bridge's ui.query / ui.action / ui.input. The guide stores its page and choices in setup.json on every page change, writes the
// settings file (hs2Root, modsFolders, cacheDir when not the default; other members kept) when page 4 is left, and setup.json
// "completed" when the ready page is finished. The language switch on page 0 applies at once and goes to options.json.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.App.Setup;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace HS2Next.UI.Setup
{
    public sealed class SetupGuide : VisualElement
    {
        readonly SetupHost host;
        readonly SetupContext ctx;
        readonly SetupState state;
        readonly Label title, step, pageId, error;
        readonly ScrollView body;
        readonly Button back, next, skip, quit, finish;

        public string Page { get; private set; }
        public SetupOutcome Outcome { get; private set; } = SetupOutcome.None;
        /// <summary>The settings keys the guide wrote (after page 4).</summary>
        public List<string> Written { get; } = new List<string>();
        /// <summary>The zipmods the index will visit as far as the pages counted them (the install's mods and the extra folders), or -1.</summary>
        public int ExpectedZipmods { get; private set; } = -1;

        // choices
        string hs2Root;
        readonly List<string> mods = new List<string>();
        string cacheDir;
        bool warningAccepted;

        // page parts
        Hs2FolderBox hs2Box;
        Label progress, failure;
        ProgressBar progressBar;
        Task<MigrationResultInfo> migration;
        int migDone, migTotal;
        Label migProgress, migResult;
        MigrationOfferInfo offer;
        bool offerDeclined;
        readonly Dictionary<string, Task<int>> modCounts = new Dictionary<string, Task<int>>(StringComparer.OrdinalIgnoreCase);

        public SetupGuide(SetupHost host)
        {
            this.host = host;
            ctx = host.Context;
            state = ctx.Decision?.State ?? SetupState.Load(ctx.UserData);
            name = "setup.guide";
            AddToClassList("hs-setup");
            var card = new VisualElement { name = "setup.card" };
            card.AddToClassList("hs-setup__card");
            Add(card);
            var head = new VisualElement();
            head.AddToClassList("hs-setup__head");
            title = new Label { name = "setup.title" };
            title.AddToClassList("hs-setup__title");
            step = new Label { name = "setup.step" };
            step.AddToClassList("hs-hint");
            pageId = new Label { name = "setup.page" };
            pageId.AddToClassList("hs-setup__pageid");
            head.Add(title);
            head.Add(step);
            head.Add(pageId);
            card.Add(head);
            body = new ScrollView { name = "setup.body" };
            body.AddToClassList("hs-setup__body");
            card.Add(body);
            error = new Label { name = "setup.error" };
            error.AddToClassList("hs-setup-status");
            error.AddToClassList("hs-setup-status--error");
            error.style.display = DisplayStyle.None;
            card.Add(error);
            var nav = new VisualElement { name = "setup.nav" };
            nav.AddToClassList("hs-setup__nav");
            quit = Btn("setup.quit", "setup.nav.quit", Quit);
            var spacer = new VisualElement();
            spacer.AddToClassList("hs-grow");
            skip = Btn("setup.skip", "setup.nav.skip", Skip);
            back = Btn("setup.back", "setup.nav.back", Back);
            next = Btn("setup.next", "setup.nav.next", Next);
            next.AddToClassList("hs-setup__default");
            finish = Btn("setup.finish", "setup.nav.finish", Finish);
            finish.AddToClassList("hs-setup__default");
            foreach (var b in new VisualElement[] { quit, spacer, skip, back, next, finish }) nav.Add(b);
            card.Add(nav);

            // choices: the stored ones of a resumed guide, else the current settings
            var ch = state.Choices ?? new JObject();
            hs2Root = (string)ch["hs2Root"] ?? ctx.CurrentHs2Root;
            if (ch["modsFolders"] is JArray ma) mods.AddRange(ma.Select(t => (string)t).Where(s => !string.IsNullOrEmpty(s)));
            else mods.AddRange(ctx.CurrentModsFolders ?? Array.Empty<string>());
            cacheDir = (string)ch["cacheDir"] ?? ctx.CurrentCacheDir ?? ctx.DefaultCacheDir;
            if (ctx.ProgressOnly) Show("index");
            else Show(SetupStage.ResumePage(state));
        }

        static Button Btn(string name, string key, Action a)
        {
            var b = Loc.Button(key, a);
            b.name = name;
            return b;
        }

        // ---------------------------------------------------------------- navigation

        int Index => SetupStage.PageIndex(Page);

        void Show(string page)
        {
            Page = page;
            pageId.text = page;
            int i = Index;
            Loc.Bind(title, "setup.title." + page);
            step.text = ctx.ProgressOnly ? "" : L10n.Format("setup.step", i + 1, SetupStage.Pages.Length);
            error.style.display = DisplayStyle.None;
            body.Clear();
            switch (page)
            {
                case "welcome": BuildWelcome(); break;
                case "files": BuildFiles(); break;
                case "hs2": BuildHs2(); break;
                case "mods": BuildMods(); break;
                case "cache": BuildCache(); break;
                case "index": BuildIndex(); break;
                case "ready": BuildReady(); break;
            }
            bool guide = !ctx.ProgressOnly;
            Display(back, guide && i >= 1 && i <= 4);
            Display(next, guide && i <= 4);
            Display(skip, guide && SetupStage.CanSkip(page));
            Display(finish, guide && page == "ready");
            Display(quit, page != "ready");
            Loc.Bind(next, i == 4 ? "setup.nav.build" : "setup.nav.next");
            UpdateNext();
            if (guide && i >= 1 && i <= 4) SaveState();
            if (host.Root != null) UiDefer.Frames(2, FocusDefault);
            HS2Next.UI.Bridge.UiCommands.SetupPageChanged(page);
        }

        static void Display(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Focuses the page's default control (Next / Finish, or the path field of the HS2 page).</summary>
        public void FocusDefault()
        {
            if (panel == null) return;
            if (Page == "hs2" && hs2Box != null) { hs2Box.PathField.Focus(); return; }
            var d = DefaultButton;
            if (d != null && d.resolvedStyle.display != DisplayStyle.None) d.Focus();
        }

        Button DefaultButton => Page == "ready" ? finish : Index <= 4 && !ctx.ProgressOnly ? next : null;

        /// <summary>Enter: the default button when it is enabled.</summary>
        public void PressDefault()
        {
            var d = DefaultButton;
            if (d != null && d.enabledSelf && d.resolvedStyle.display != DisplayStyle.None)
            {
                if (d == finish) Finish();
                else Next();
            }
        }

        public void Back()
        {
            int i = Index;
            if (ctx.ProgressOnly || i < 1 || i > 4) return;
            Show(SetupStage.Pages[i - 1]);
        }

        public void Next()
        {
            int i = Index;
            if (ctx.ProgressOnly || i < 0 || i > 4 || !next.enabledSelf) return;
            switch (Page)
            {
                case "welcome":
                    SetupLanguage.Store(ctx.UserData, L10n.Language);
                    state.Choices["language"] = L10n.Language;
                    break;
                case "hs2":
                    if (hs2Box?.Verdict == null || !hs2Box.Verdict.Usable) return;
                    hs2Root = hs2Box.Verdict.Path;
                    if (hs2Box.Verdict.Level == InstallLevel.Warning && !warningAccepted)
                    {
                        host.Dialogs.Confirm("setup.hs2.warnTitle", "setup.hs2.warnMessage", ok => { if (ok) { warningAccepted = true; Next(); } });
                        return;
                    }
                    break;
                case "cache":
                    if (!WriteSettings()) return;
                    ExpectedZipmods = CountedZipmods();
                    Outcome = SetupOutcome.Continue;
                    Show("index");
                    return;
            }
            Show(SetupStage.Pages[i + 1]);
        }

        void Skip()
        {
            if (!SetupStage.CanSkip(Page)) return;
            host.Dialogs.Confirm("setup.skip.title", "setup.skip.message", ok =>
            {
                if (!ok) return;
                state.Skipped = true;
                state.SkippedAt = DateTime.UtcNow.ToString("o");
                state.GuideVersion = SetupStage.GuideVersion;
                SaveState(keepPage: true);
                Debug.Log("[setup] skipped on page " + Page);
                Outcome = SetupOutcome.Skipped;
            });
        }

        void Quit()
        {
            if (Index >= 1 && Index <= 4) SaveState();
            Debug.Log("[setup] quit on page " + Page);
            Outcome = SetupOutcome.Quit;
        }

        void Finish()
        {
            if (Page != "ready") return;
            state.CompletedAt = DateTime.UtcNow.ToString("o");
            state.Skipped = false;
            state.LastPage = "ready";
            state.GuideVersion = SetupStage.GuideVersion;
            SaveState(keepPage: true);
            host.Close();
        }

        void SaveState(bool keepPage = false)
        {
            if (!keepPage) state.LastPage = Page;
            if (!string.IsNullOrEmpty(hs2Root)) state.Choices["hs2Root"] = hs2Root;
            state.Choices["modsFolders"] = new JArray(mods);
            if (!string.IsNullOrEmpty(cacheDir)) state.Choices["cacheDir"] = cacheDir;
            try { state.Save(ctx.UserData); }
            catch (Exception e) { Debug.LogWarning("[setup] setup.json not written: " + e.Message); }
        }

        /// <summary>Writes hs2Root, modsFolders and cacheDir (when not the default) into the settings file; false on failure (the
        /// page shows why).</summary>
        bool WriteSettings()
        {
            var values = new List<KeyValuePair<string, JToken>>
            {
                new KeyValuePair<string, JToken>("hs2Root", hs2Root),
                new KeyValuePair<string, JToken>("modsFolders", mods.Count > 0 ? new JArray(mods) : null),
                new KeyValuePair<string, JToken>("cacheDir", SameFolder(cacheDir, ctx.DefaultCacheDir) ? null : (JToken)cacheDir),
            };
            try
            {
                Written.Clear();
                Written.AddRange(SettingsFile.Write(ctx.SettingsFile, values));
                state.LastPage = "index";
                SaveState(keepPage: true);
                Debug.Log("[setup] settings written to " + ctx.SettingsFile + ": " + (Written.Count == 0 ? "(unchanged)" : string.Join(", ", Written)));
                return true;
            }
            catch (Exception e)
            {
                ShowError(L10n.Format("setup.error.write", ctx.SettingsFile, e.Message));
                Debug.LogWarning("[setup] settings not written: " + e.Message);
                return false;
            }
        }

        int CountedZipmods()
        {
            int n = hs2Count;
            lock (modCounts)
                foreach (var m in mods)
                    if (modCounts.TryGetValue(m, out var t) && t.IsCompleted && !t.IsFaulted) n += t.Result;
            return n > 0 ? n : -1;
        }

        int hs2Count = -1;

        internal static bool SameFolder(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        void ShowError(string text)
        {
            error.text = text;
            error.style.display = DisplayStyle.Flex;
        }

        void UpdateNext()
        {
            bool ok = true;
            if (Page == "hs2") ok = hs2Box?.Verdict != null && hs2Box.Verdict.Usable;
            if (Page == "cache") ok = !string.IsNullOrWhiteSpace(cacheDir);
            if (Page == "files") ok = migration == null;
            next.SetEnabled(ok);
            skip.SetEnabled(migration == null);
        }

        /// <summary>Per frame: the copy's progress.</summary>
        internal void Tick()
        {
            if (migration == null) return;
            if (migProgress != null) migProgress.text = L10n.Format("setup.migrate.progress", migDone, migTotal);
            if (!migration.IsCompleted) return;
            var r = migration.IsFaulted ? new MigrationResultInfo { Error = migration.Exception?.GetBaseException().Message } : migration.Result;
            migration = null;
            if (migResult != null)
            {
                migResult.text = r.Error != null ? L10n.Format("setup.migrate.failed", r.Error)
                    : L10n.Format("setup.migrate.done", r.Copied, Mb(r.Bytes), r.Conflicts) + (r.Refused > 0 ? " " + L10n.Format("setup.migrate.refused", r.Refused) : "");
                migResult.EnableInClassList("hs-setup-status--error", r.Error != null);
                migResult.EnableInClassList("hs-setup-status--ok", r.Error == null);
            }
            Debug.Log("[setup] copy from " + offer?.Source + ": " + (r.Error ?? r.Copied + " copied, " + r.Same + " same, " + r.Conflicts + " conflicts, " + r.Refused + " refused"));
            // a copied settings file may name the HS2 folder: the next pages offer it
            if (r.Error == null && string.IsNullOrEmpty(hs2Root))
            {
                try { hs2Root = (string)SettingsFile.Get(SettingsFile.Read(ctx.SettingsFile), "hs2Root"); }
                catch (Exception) { }
            }
            UpdateNext();
        }

        static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0");

        // ---------------------------------------------------------------- pages

        Label Text(string key, params object[] args)
        {
            var l = args.Length > 0 ? new Label(L10n.Format(key, args)) : Loc.Label(key);
            l.AddToClassList("hs-setup-text");
            body.Add(l);
            return l;
        }

        Label Hint(string key, params object[] args)
        {
            var l = Text(key, args);
            l.AddToClassList("hs-hint");
            return l;
        }

        void BuildWelcome()
        {
            var v = new Label(L10n.Format("setup.welcome.version", string.IsNullOrEmpty(ctx.Version) ? "?" : ctx.Version)) { name = "setup.version" };
            v.AddToClassList("hs-setup-subtitle");
            body.Add(v);
            Text("setup.welcome.text");
            var langTitle = Text("setup.welcome.language");
            langTitle.AddToClassList("hs-setup-subtitle");
            var row = new VisualElement { name = "setup.language" };
            row.AddToClassList("hs-setup-row");
            foreach (var code in SetupLanguage.Codes)
            {
                string c = code;
                var b = new Button(() => SetLanguage(c)) { name = "setup.language." + c };
                b.text = L10n.Get("lang." + c);   // the Chinese label says "machine translated"
                b.EnableInClassList("hs-setup-choice--on", L10n.Language == c);
                b.AddToClassList("hs-setup-choice");
                row.Add(b);
            }
            body.Add(row);
            if (L10n.Language == "zh-Hans") Hint("setup.welcome.machineNote");
        }

        void SetLanguage(string code)
        {
            string set = L10n.SetLanguage(code);
            host.Fonts?.Apply(host.Root, set);
            Loc.Refresh();
            state.Choices["language"] = set;
            Debug.Log("[setup] language " + set);
            Show(Page);
        }

        void BuildFiles()
        {
            Text("setup.files.text");
            var root = new Label(ctx.DataRoot ?? "") { name = "setup.files.root" };
            root.AddToClassList("hs-setup-path");
            body.Add(root);
            Hint("setup.files.mode." + (ctx.Mode ?? "perUser"));
            var open = Loc.Button("setup.files.open", () => OpenFolder(ctx.DataRoot));
            open.name = "setup.files.open";
            body.Add(Row(open));
            Hint("setup.files.portable");
            offer = null;
            try { offer = offerDeclined ? null : ctx.MigrationOffer?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[setup] copy offer: " + e.Message); }
            if (offer != null || migResult != null)
            {
                var box = new VisualElement { name = "setup.migrate" };
                box.AddToClassList("hs-setup-box");
                body.Add(box);
                if (offer != null)
                {
                    var t = new Label(L10n.Format("setup.migrate.offer", offer.Files, Mb(offer.Bytes), offer.Source)) { name = "setup.migrate.text" };
                    t.AddToClassList("hs-setup-text");
                    box.Add(t);
                    var copy = Btn("setup.migrate.copy", "setup.migrate.copyButton", StartMigration);
                    var notNow = Btn("setup.migrate.notnow", "setup.migrate.notNow", () => { offerDeclined = true; Show(Page); });
                    box.Add(Row(copy, notNow));
                    copy.SetEnabled(migration == null && ctx.RunMigration != null);
                }
                migProgress = new Label { name = "setup.migrate.progress" };
                migProgress.AddToClassList("hs-hint");
                box.Add(migProgress);
                var keep = migResult?.text;
                migResult = new Label { name = "setup.migrate.result", text = keep ?? "" };
                migResult.AddToClassList("hs-setup-status");
                box.Add(migResult);
            }
        }

        void StartMigration()
        {
            if (migration != null || offer == null || ctx.RunMigration == null) return;
            string source = offer.Source;
            migDone = 0;
            migTotal = offer.Files;
            var run = ctx.RunMigration;
            migration = Task.Run(() => run(source, (d, t) => { migDone = d; migTotal = t; }));
            UpdateNext();
            Debug.Log("[setup] copying " + offer.Files + " files from " + source);
        }

        void BuildHs2()
        {
            Text("setup.hs2.text");
            hs2Box = new Hs2FolderBox(() => host.Dialogs, hs2Root);
            hs2Box.Changed += () => { warningAccepted = false; if (hs2Box.Zipmods >= 0) hs2Count = hs2Box.Zipmods; UpdateNext(); };
            body.Add(hs2Box);
        }

        void BuildMods()
        {
            Text("setup.mods.text");
            var install = new Label(string.IsNullOrEmpty(hs2Root) ? "" : L10n.Format("setup.mods.install", Path.Combine(hs2Root, "mods"))) { name = "setup.mods.install" };
            install.AddToClassList("hs-setup-path");
            body.Add(install);
            var list = new VisualElement { name = "setup.mods.list" };
            list.AddToClassList("hs-setup-list");
            body.Add(list);
            var status = new Label { name = "setup.mods.status" };
            status.AddToClassList("hs-setup-status");
            void Fill()
            {
                list.Clear();
                for (int i = 0; i < mods.Count; i++)
                {
                    int k = i;
                    var l = new Label(mods[i]) { name = "setup.mods.item." + i };
                    l.AddToClassList("hs-grow");
                    var count = new Label("…") { name = "setup.mods.count." + i };
                    count.AddToClassList("hs-hint");
                    string dir = mods[i];
                    Task<int> t;
                    lock (modCounts)
                        if (!modCounts.TryGetValue(dir, out t)) modCounts[dir] = t = Task.Run(() => Hs2InstallCheck.CountZipmods(dir));
                    count.schedule.Execute(() => { if (t.IsCompleted) count.text = L10n.Format("setup.hs2.zipmods", t.IsFaulted ? 0 : t.Result); }).Until(() => t.IsCompleted && count.text != "…").Every(100);
                    var rm = new Button(() => { mods.RemoveAt(k); Fill(); }) { name = "setup.mods.remove." + i, text = L10n.Get("setup.mods.remove") };
                    list.Add(Row(l, count, rm));
                }
                if (mods.Count == 0) { var none = Loc.Label("setup.mods.none"); none.AddToClassList("hs-hint"); list.Add(none); }
            }
            Fill();
            var field = new TextField { name = "setup.mods.path" };
            Loc.Bind(field, "setup.mods.pathLabel", (e, t) => ((TextField)e).label = t);
            field.AddToClassList("hs-grow");
            void AddFolder(string p)
            {
                string n = Hs2InstallCheck.Normalise(p);
                if (string.IsNullOrEmpty(n) || !Hs2InstallCheck.Readable(n)) { status.text = L10n.Get("setup.mods.unreadable"); status.EnableInClassList("hs-setup-status--error", true); return; }
                if (mods.Any(m => SameFolder(m, n)) || (!string.IsNullOrEmpty(hs2Root) && SameFolder(n, Path.Combine(hs2Root, "mods")))) { status.text = L10n.Get("setup.mods.duplicate"); status.EnableInClassList("hs-setup-status--error", true); return; }
                mods.Add(n);
                status.text = "";
                status.EnableInClassList("hs-setup-status--error", false);
                field.SetValueWithoutNotify("");
                Fill();
            }
            var add = Btn("setup.mods.add", "setup.mods.addButton", () => AddFolder(field.value));
            var browse = Btn("setup.mods.browse", "setup.browse", () => FolderPicker.Open(host.Dialogs, "setup.mods.pickTitle", string.IsNullOrWhiteSpace(field.value) ? null : field.value, p => { if (p != null) AddFolder(p); }));
            body.Add(Row(field, add, browse));
            body.Add(status);
            Hint("setup.mods.note");
        }

        void BuildCache()
        {
            Text("setup.cache.text");
            var field = new TextField { name = "setup.cache.path", isDelayed = false, value = cacheDir ?? "" };
            Loc.Bind(field, "setup.cache.pathLabel", (e, t) => ((TextField)e).label = t);
            field.AddToClassList("hs-grow");
            var free = new Label { name = "setup.cache.free" };
            free.AddToClassList("hs-setup-text");
            var warn = new Label { name = "setup.cache.warning", text = L10n.Format("setup.cache.lowSpace", SetupStage.CacheWarnFreeGB) };
            warn.AddToClassList("hs-setup-status");
            warn.AddToClassList("hs-setup-status--warning");
            void Update(string p)
            {
                cacheDir = string.IsNullOrWhiteSpace(p) ? null : p.Trim().Trim('"');
                double gb = FreeGB(cacheDir);
                free.text = gb < 0 ? L10n.Get("setup.cache.freeUnknown") : L10n.Format("setup.cache.free", gb.ToString("0.0"));
                warn.style.display = gb >= 0 && gb < SetupStage.CacheWarnFreeGB ? DisplayStyle.Flex : DisplayStyle.None;
                UpdateNext();
            }
            field.RegisterValueChangedCallback(e => Update(e.newValue));
            var change = Btn("setup.cache.change", "setup.cache.changeButton", () => FolderPicker.Open(host.Dialogs, "setup.cache.pickTitle", ExistingParent(cacheDir), p => { if (p != null) field.value = p; }));
            var reset = Btn("setup.cache.default", "setup.cache.defaultButton", () => field.value = ctx.DefaultCacheDir ?? "");
            body.Add(Row(field, change, reset));
            body.Add(free);
            var expected = new Label(L10n.Format("setup.cache.expected", SetupStage.CacheAtOnceGB, SetupStage.CacheUpToGB)) { name = "setup.cache.expected" };
            expected.AddToClassList("hs-setup-text");
            body.Add(expected);
            body.Add(warn);
            Update(field.value);
        }

        void BuildIndex()
        {
            Text(ctx.ProgressOnly ? "setup.index.textProgress" : "setup.index.text");
            progressBar = new ProgressBar { name = "setup.progress.bar", lowValue = 0, highValue = 1, value = 0 };
            body.Add(progressBar);
            progress = new Label(L10n.Get("setup.index.starting")) { name = "setup.progress" };
            progress.AddToClassList("hs-setup-text");
            body.Add(progress);
            failure = new Label { name = "setup.index.failure" };
            failure.AddToClassList("hs-setup-status");
            failure.AddToClassList("hs-setup-status--error");
            failure.style.display = DisplayStyle.None;
            body.Add(failure);
            Hint("setup.index.note");
        }

        void BuildReady()
        {
            Text("setup.ready.text");
            foreach (var k in new[] { "setup.ready.tipCamera", "setup.ready.tipNoNumpad", "setup.ready.tipFolders" }) Text(k).AddToClassList("hs-setup-tip");
            var tip = Text("setup.ready.tipBridge", ctx.TokenFile ?? Path.Combine(ctx.UserData ?? "", "Bridge", "token"));
            tip.AddToClassList("hs-setup-tip");
            tip.name = "setup.ready.bridge";
            var where = Text("setup.ready.tipFiles", ctx.DataRoot ?? "");
            where.AddToClassList("hs-setup-tip");
        }

        // ---------------------------------------------------------------- the bridge's side (app.setup)

        /// <summary>app.setup set: the choices the next pages show (the HS2 page's field when it is open).</summary>
        public void Prefill(string hs2, IEnumerable<string> extraMods, string cache)
        {
            if (Outcome != SetupOutcome.None) return;
            if (!string.IsNullOrEmpty(hs2)) { hs2Root = hs2; if (Page == "hs2" && hs2Box != null) hs2Box.PathField.value = hs2; }
            if (extraMods != null) { mods.Clear(); mods.AddRange(extraMods); }
            if (!string.IsNullOrEmpty(cache)) cacheDir = cache;
            if (Page == "mods" || Page == "cache") Show(Page);
        }

        /// <summary>app.setup finish while the guide runs on pages 0–4: the choices are written and the index starts (null), or why not.</summary>
        public string FinishFromBridge(string hs2, IEnumerable<string> extraMods, string cache)
        {
            if (Outcome != SetupOutcome.None || Index > 4) return "the guide is past its choices (page " + Page + ")";
            Prefill(hs2, extraMods, cache);
            if (string.IsNullOrEmpty(hs2Root)) return "no HS2 folder is chosen";
            if (!WriteSettings()) return error.text;
            ExpectedZipmods = CountedZipmods();
            Outcome = SetupOutcome.Continue;
            Show("index");
            return null;
        }

        /// <summary>app.setup skip while the guide runs: skips without the question (false when the page offers no skip).</summary>
        public bool SkipFromBridge()
        {
            if (!SetupStage.CanSkip(Page) || Outcome != SetupOutcome.None) return false;
            state.Skipped = true;
            state.SkippedAt = DateTime.UtcNow.ToString("o");
            state.GuideVersion = SetupStage.GuideVersion;
            SaveState(keepPage: true);
            Debug.Log("[setup] skipped on page " + Page + " (app.setup)");
            Outcome = SetupOutcome.Skipped;
            return true;
        }

        // ---------------------------------------------------------------- the boot's side

        internal void SetProgress(int visited, int total, double seconds)
        {
            if (progress == null || Page != "index") return;
            string line = total > 0 ? L10n.Format("setup.index.progress", visited, total, seconds.ToString("0")) : L10n.Format("setup.index.progressNoTotal", visited, seconds.ToString("0"));
            if (total > 0 && visited > 0 && seconds >= 30 && visited < total)
                line += " " + L10n.Format("setup.index.estimate", Math.Max(1, Math.Round(seconds * (total - visited) / visited)).ToString("0"));
            progress.text = line;
            if (progressBar != null) progressBar.value = total > 0 ? Mathf.Clamp01(visited / (float)total) : 0;
        }

        internal void ShowReady()
        {
            if (Page == "index") Show("ready");
        }

        internal void ShowFailure(string message)
        {
            if (failure == null) { ShowError(message); return; }
            failure.text = L10n.Format("setup.index.failed", message);
            failure.style.display = DisplayStyle.Flex;
            Display(quit, true);
        }

        // ---------------------------------------------------------------- helpers

        static VisualElement Row(params VisualElement[] items)
        {
            var r = new VisualElement();
            r.AddToClassList("hs-setup-row");
            foreach (var i in items) r.Add(i);
            return r;
        }

        static double FreeGB(string dir)
        {
            try
            {
                string p = ExistingParent(dir);
                if (p == null) return -1;
                var root = Path.GetPathRoot(Path.GetFullPath(p));
                return new DriveInfo(root).AvailableFreeSpace / (1024.0 * 1024 * 1024);
            }
            catch (Exception) { return -1; }
        }

        /// <summary>The folder itself or its nearest existing parent (null when none).</summary>
        static string ExistingParent(string dir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) return null;
                var p = Path.GetFullPath(dir);
                while (!string.IsNullOrEmpty(p) && !Directory.Exists(p)) p = Path.GetDirectoryName(p);
                return string.IsNullOrEmpty(p) ? null : p;
            }
            catch (Exception) { return null; }
        }

        internal static void OpenFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                Application.OpenURL(new Uri(Path.GetFullPath(dir) + Path.DirectorySeparatorChar).AbsoluteUri);
            }
            catch (Exception e) { Debug.LogWarning("[setup] open folder: " + e.Message); }
        }
    }
}
