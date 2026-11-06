// The Maker's thumbnail list (F11): a `ref` member of every selected character picked from a
// Maker list (MakerLists.Rows of the row's category for the primary's sex; face skins filtered by the bound `face.head`).
// A search field (`partlist-search`: the 3.5 normalised AND search over the names in all three languages, `guid#id` and the
// zipmod GUID; the text survives row changes), a sort / filter button (`partlist-sort`: list order or name; all / vanilla
// / mods; kept per list path while the app runs), the virtualised ThumbGrid (cells 72 × 88 px, columns from the width,
// Ctrl + wheel zooms 48–128 px, inline 3 rows, `partlist-expand` shows 8), thumbnails from the shared PartThumbnails.
// The cell of the bound value is marked and scrolled into view; a bound value that is not a row of the list gets a pinned
// first cell "not listed: <ref>" (clicking it does nothing); several characters with different values: no mark, the header
// shows "—". A click or Enter on a cell = StudioOps.ChangeItem (one entry "Change item"); re-picking the current row does
// nothing; a row whose MainAB bundle is not open is opened asynchronously first (busy mark; a second click replaces the
// pending row; an open failure is a toast and no edit). Arrows / Home / End / Page Up / Down move the focus
// cell, typing goes to the search field. MakerThumbList is the part PushGrid shares.
// 4.4: re-attaching a list (a part or kind group shown again) no longer scrolls it at once: the grid re-binds its visible
// cells on attach by itself, and the list scrolls to its current cell one frame later only when that cell is not shown then
// (the list view does not keep its scroll position across a detach) or when the value or the rows changed while it was
// detached; a key typed while an IME is on
// only focuses the search field (the IME delivers the composed text); a pick pre-opens the copy of the row's bundle that
// serves its MainData asset; a KCOX pattern reference shows as a not-listed KCOX pattern.
// 4.5: an accessory item list is the accessory's own category for both sexes; a list may hand its pick to the page instead
// of changing the bound member (PickOverride: the Accessories page's add panel).
// 5.2: a cell hovered or keyboard-focused for 150 ms is prefetched (ContentCatalog.Prefetch: its bundle opened and its asset
// requested ahead of the click; the pick itself is unchanged and its edit gets the same objects sooner).
// v0.3 trial 2: a thumbnail size slider (`partlist-zoom`, the cell width 48–128 px) beside the search field, as the character
// browser's; the size (and Ctrl + wheel) is the view-state option view.thumbSize.<kind> of the list's page kind (face, body,
// hair, clothes, accessories: every list and image push grid of the kind follows it; a list of no kind keeps its size per
// list path while the app runs); the cells' tooltips end with what a click does (HintKey); the cell states are ThumbGrid's.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels.Maker;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>One cell of a Maker list: a list row, or the pinned "not listed" value.</summary>
    public sealed class PartItem
    {
        public ListRow Row;
        /// <summary>The reference ("guid#id") of the row or of the unlisted value.</summary>
        public string Ref;
        /// <summary>The pinned cell of a bound value that is not a row of the list.</summary>
        public bool NotListed;
        /// <summary>Thumbnail column (ThumbTex; Preset for the face preset grid; null = a text-only cell).</summary>
        public string Column;
        /// <summary>Display name (by UI language).</summary>
        public string Name;
        public override string ToString() => (NotListed ? "not listed " : "") + Ref + " " + Name;
    }

    /// <summary>Thumbnails of list rows through the shared PartThumbnails.</summary>
    public sealed class PartThumbSource : IThumbSource<PartItem>
    {
        readonly PartThumbnails pt;
        Action<string, Texture2D> loaded;

        /// <summary>The shared thumbnails are listened to only while this event has a listener.</summary>
        public event Action<string, Texture2D> Loaded
        {
            add { bool first = loaded == null; loaded += value; if (first && loaded != null && pt != null) pt.LoadedColumn += OnLoadedColumn; }
            remove { bool had = loaded != null; loaded -= value; if (had && loaded == null && pt != null) pt.LoadedColumn -= OnLoadedColumn; }
        }

        public PartThumbSource(PartThumbnails thumbs) { pt = thumbs; }

        void OnLoadedColumn(int cat, ContentId key, string col, Texture2D t) => loaded?.Invoke(Key(cat, key, col), t);

        static string Key(int cat, ContentId key, string col) => cat.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + key + "|" + col;

        public string KeyOf(PartItem item) => pt == null || item?.Row == null || item.Column == null ? null : Key(item.Row.Category, item.Row.Key, item.Column);
        public Texture2D Request(PartItem item, object owner) => pt?.Request(item.Row.Category, item.Row.Key, owner, item.Column);
        public void Release(PartItem item, object owner) { if (item?.Row != null) pt?.Release(item.Row.Category, item.Row.Key, owner, item.Column); }
        public bool IsFailed(PartItem item) => item?.Row != null && pt != null && pt.IsFailed(item.Row.Category, item.Row.Key, item.Column);
    }

    /// <summary>The shared part of PartList and PushGrid: header, search, the grid, the thumbnail tick, the zoom.</summary>
    public abstract class MakerThumbList : VisualElement
    {
        public const float CellWidth = 72f, CellText = 16f, MinCell = 48f, MaxCell = 128f;
        public const int InlineRows = 3, ExpandedRows = 8;

        protected readonly UiContext ctx;
        protected readonly Label header, currentLabel;
        protected readonly TextField search;
        protected readonly VisualElement tools;
        protected readonly Button expandButton;
        /// <summary>The thumbnail size slider (the cell width in px; hidden for lists without images).</summary>
        protected readonly Slider zoomSlider;
        protected List<PartItem> all = new List<PartItem>();
        protected readonly List<PartItem> shown = new List<PartItem>();
        readonly List<int> identity = new List<int>();
        ListIndex<PartItem> index;
        protected string filter = "all", sort = "list";
        bool expanded;
        protected readonly PartThumbnails thumbs;

        public ThumbGrid<PartItem> Grid { get; }
        /// <summary>The cells shown (pinned cell first), after search / filter / sort.</summary>
        public IReadOnlyList<PartItem> Items => shown;
        /// <summary>Every cell before the search (tests).</summary>
        public IReadOnlyList<PartItem> AllItems => all;
        public string SearchText => search.value ?? "";
        public bool Expanded => expanded;
        /// <summary>Main-thread time of the last row (re)build and of the last filter (UF3), ms.</summary>
        public double LastBuildMs { get; protected set; }
        public double LastFilterMs { get; private set; }
        /// <summary>Row (re)builds so far (tests).</summary>
        public int Builds { get; protected set; }
        public PartThumbnails Thumbnails => thumbs;
        /// <summary>The page kind whose thumbnail size option the list follows ("face", "body", "hair", "clothes", "accessories"),
        /// or null.</summary>
        public string GridKind { get; private set; }
        /// <summary>The thumbnail size slider (tests).</summary>
        public Slider ZoomSlider => zoomSlider;
        /// <summary>True when the cell size can change (image cells).</summary>
        public bool Zoomable => Grid.MaxZoom > Grid.MinZoom;

        /// <param name="thumbnails">false: the cells have an image area but no thumbnails (colour cells).</param>
        protected MakerThumbList(UiContext ctx, string labelKey, float baseWidth, float aspect, float textHeight, bool thumbnails = true)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            AddToClassList(FieldRow.UssClass);
            AddToClassList("hs-partlist");
            thumbs = aspect > 0 && thumbnails ? PartThumbnails.Shared(ctx.App?.Catalog) : null;
            var top = new VisualElement();
            top.AddToClassList("hs-partlist__header");
            header = new Label(labelKey != null ? L10n.Get(labelKey) : "");
            header.AddToClassList("hs-partlist__label");
            if (labelKey != null) Loc.Bind(header, labelKey);
            currentLabel = new Label { name = "partlist-current" };
            currentLabel.AddToClassList("hs-partlist__current");
            top.Add(header);
            top.Add(currentLabel);
            Add(top);
            tools = new VisualElement();
            tools.AddToClassList("hs-partlist__tools");
            search = PanelKit.SearchField("partlist-search", q => ApplySearch());
            search.AddToClassList("hs-grow");
            tools.Add(search);
            zoomSlider = new Slider(MinCell, MaxCell) { name = "partlist-zoom", value = baseWidth };
            zoomSlider.AddToClassList("hs-partlist__zoom");
            Loc.Tooltip(zoomSlider, "browser.zoom");
            zoomSlider.RegisterValueChangedCallback(e => SetCellWidth(e.newValue));
            tools.Add(zoomSlider);
            expandButton = new Button(() => SetExpanded(!expanded)) { name = "partlist-expand", text = "▾" };
            expandButton.AddToClassList("hs-partlist__button");
            Loc.Tooltip(expandButton, "maker.list.expand");
            Add(tools);
            bool images = aspect > 0 && thumbnails;
            Grid = new ThumbGrid<PartItem>("partlist-grid", images ? new PartThumbSource(thumbs) : null, i => i.Name, baseWidth, aspect, textHeight)
            {
                MinZoom = images ? MinCell / CellWidth : 1, MaxZoom = images ? MaxCell / CellWidth : 1,
                CellName = (i, pos) => CellNameOf(i),
                Tooltip = TooltipOf,
            };
            Grid.AddToClassList("hs-partlist__grid");
            Add(Grid);
            Grid.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            Grid.RegisterCallback<KeyDownEvent>(OnTypeAhead);
            zoomSlider.style.display = images ? DisplayStyle.Flex : DisplayStyle.None;
            // the kind's size follows the option while the list is shown (a change made on another list, the options panel)
            RegisterCallback<AttachToPanelEvent>(_ => { if (ctx.Options == null) return; ctx.Options.Changed -= OnOption; ctx.Options.Changed += OnOption; ApplyKindSize(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { if (ctx.Options != null) ctx.Options.Changed -= OnOption; });
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);   // an Edit tab that is not shown builds its page's lists all the same
            // the language while the list is shown (a list shown again rebinds, and its rows' signature holds the language)
            RegisterCallback<AttachToPanelEvent>(_ => { L10n.Default.LanguageChanged -= OnLanguage; L10n.Default.LanguageChanged += OnLanguage; });
            RegisterCallback<DetachFromPanelEvent>(_ => L10n.Default.LanguageChanged -= OnLanguage);
            ApplyHeight();
        }

        protected abstract string CellNameOf(PartItem i);
        protected virtual void LanguageChanged() { }

        void OnLanguage(string _) { if (panel != null) LanguageChanged(); }

        protected virtual string TooltipOf(PartItem i)
        {
            if (i == null) return null;
            if (i.Row == null) return i.Name;
            string zip = i.Row.ZipPath != null ? "\n" + System.IO.Path.GetFileName(i.Row.ZipPath) : "";
            return i.Name + "\n" + i.Ref + zip;
        }

        protected virtual void Tick() => thumbs?.UpdateOncePerFrame();

        void OnWheel(WheelEvent e)
        {
            if (!e.ctrlKey || Grid.MaxZoom <= Grid.MinZoom) return;
            SetCellWidth(Grid.CellWidth * (e.delta.y < 0 ? 1.1f : 1 / 1.1f));
            e.StopPropagation();
        }

        /// <summary>The page kind of a Maker member path ("appearance.chara.face.skin" → "face"), or null.</summary>
        public static string GridKindOf(string path)
        {
            if (path == null) return null;
            foreach (var (prefix, kind) in KindPrefixes)
                if (path.StartsWith(prefix, StringComparison.Ordinal) && (path.Length == prefix.Length || path[prefix.Length] == '.' || path[prefix.Length] == '['))
                    return kind;
            return null;
        }

        static readonly (string prefix, string kind)[] KindPrefixes =
        {
            ("appearance.chara.face", "face"), ("appearance.chara.body", "body"), ("appearance.chara.hair", "hair"),
            ("appearance.coord.clothes", "clothes"), ("appearance.coord.accessories", "accessories"),
        };

        /// <summary>Follows the thumbnail size option of the page kind of <paramref name="path"/> (subclasses, once their path is
        /// known); a list of no kind keeps the zoom <paramref name="fallbackZoom"/> (its size of this app run).</summary>
        protected void UseGridKind(string path, float fallbackZoom = 1f)
        {
            GridKind = Zoomable ? GridKindOf(path) : null;
            if (GridKind != null) ApplyKindSize();
            else if (Zoomable && fallbackZoom != 1f) ApplyCellWidth(fallbackZoom * BaseCellWidth);
        }

        float BaseCellWidth => Grid.CellWidth / Math.Max(0.0001f, Grid.Zoom);

        /// <summary>The cell width of the kind's option (px), or the current width without a kind.</summary>
        public float KindCellWidth
        {
            get
            {
                if (GridKind == null || ctx.Options == null) return Grid.CellWidth;
                int px = ctx.Options.Int(OptionDefs.ThumbSizeKey(GridKind));
                return px > 0 ? px : OptionDefs.MakerThumbDefault;
            }
        }

        // the kind's width when it differs from the shown one (an attach does not re-bind the cells for nothing)
        void ApplyKindSize() { if (GridKind != null && Zoomable && Math.Abs(KindCellWidth - Grid.CellWidth) > 0.01f) ApplyCellWidth(KindCellWidth); }

        void OnOption(string key)
        {
            if (GridKind != null && key == OptionDefs.ThumbSizeKey(GridKind)) ApplyKindSize();
        }

        /// <summary>Sets the cell width (px; the slider, Ctrl + wheel): this grid and the kind's view-state option (every list of
        /// the kind follows it).</summary>
        public void SetCellWidth(float px)
        {
            if (!Zoomable) return;
            px = Mathf.Round(Mathf.Clamp(px, MinCell, MaxCell));   // whole px: the option's unit
            ApplyCellWidth(px);
            if (GridKind != null && ctx.Options != null)
            {
                int v = Mathf.RoundToInt(px);
                if (ctx.Options.Int(OptionDefs.ThumbSizeKey(GridKind)) != v) ctx.Options.Set(OptionDefs.ThumbSizeKey(GridKind), v);
            }
            ViewChanged();
        }

        void ApplyCellWidth(float px)
        {
            Grid.Zoom = px / Math.Max(1f, BaseCellWidth);
            if (Math.Abs(zoomSlider.value - Grid.CellWidth) > 0.01f) zoomSlider.SetValueWithoutNotify(Grid.CellWidth);
            ApplyHeight();
        }

        void OnTypeAhead(KeyDownEvent e)
        {
            if (e.ctrlKey || e.altKey || e.commandKey || e.character < ' ' || char.IsControl(e.character)) return;
            if (search.style.display == DisplayStyle.None) return;
            search.Focus();
            if (ImeActive()) { e.StopPropagation(); return; }   // the IME composes in the focused field; its first key is not appended raw
            search.value = (search.value ?? "") + e.character;
            e.StopPropagation();
        }

        static bool ImeActive()
        {
            try { return UnityEngine.Input.imeIsSelected || !string.IsNullOrEmpty(UnityEngine.Input.compositionString); }
            catch (InvalidOperationException) { return false; }
        }

        public void SetExpanded(bool on)
        {
            expanded = on;
            expandButton.text = on ? "▴" : "▾";
            ApplyHeight();
            ViewChanged();
        }

        protected void ApplyHeight()
        {
            int rowsWanted = expanded ? ExpandedRows : InlineRows;
            int cols = Math.Max(1, Grid.Columns);
            int needed = Math.Max(1, (shown.Count + cols - 1) / cols);
            Grid.style.height = Math.Min(rowsWanted, needed) * Grid.RowHeight + 4;
        }

        protected virtual void ViewChanged() { }

        /// <summary>Sets the cells (all rows of the list, the pinned cell first) and re-applies search, filter and sort.</summary>
        protected void SetAll(List<PartItem> items)
        {
            all = items ?? new List<PartItem>();
            index = new ListIndex<PartItem>(all, SearchTextOf, i => i.NotListed ? null : i.Ref);
            index.AddFacet("source", i => i.NotListed ? null : i.Row == null || i.Row.IsVanilla ? "vanilla" : "mods");
            index.AddSort("name", (a, b) => L10n.Language == "ja" ? ListSorts.CompareJa(a.Name, b.Name) : ListSorts.CompareOrdinalIgnoreCase(a.Name, b.Name));
            ApplySearch();
        }

        static string SearchTextOf(PartItem i)
        {
            if (i.Row == null) return i.Name + " " + i.Ref;
            return MakerLists.NameOf(i.Row, "ja") + " " + MakerLists.NameOf(i.Row, "en") + " " + MakerLists.NameOf(i.Row, "zh-Hans") + " " + i.Ref + " " + i.Row.Key.SourceGuid;
        }

        /// <summary>Re-applies the search text, filter and sort (the pinned cell always stays first).</summary>
        protected void ApplySearch()
        {
            if (index == null) return;
            var sw = Stopwatch.StartNew();
            index.SetQuery(search.value ?? "");
            index.SetFacet("source", filter == "all" ? null : filter);
            index.SetSort(sort == "name" ? "name" : null);
            index.Refresh();
            shown.Clear();
            foreach (var p in all) if (p.NotListed) shown.Add(p);
            foreach (int i in index.Indices) if (!all[i].NotListed) shown.Add(all[i]);
            identity.Clear();
            for (int i = 0; i < shown.Count; i++) identity.Add(i);
            Grid.SetItems(shown, identity);
            LastFilterMs = sw.Elapsed.TotalMilliseconds;
            ApplyHeight();
            AfterFilter();
        }

        protected virtual void AfterFilter() { }

        /// <summary>Tests and the bridge: sets the search text and filters at once (no debounce).</summary>
        public double Search(string q)
        {
            search.SetValueWithoutNotify(q ?? "");
            ApplySearch();
            return LastFilterMs;
        }

        /// <summary>The position of a reference among the shown cells (-1 = not shown).</summary>
        public int PositionOf(string reference) => shown.FindIndex(i => !i.NotListed && i.Ref == reference);

        /// <summary>The cell element of a reference when it is bound (scrolled into view), else null.</summary>
        public VisualElement CellOf(string reference) => Grid.Q(CellNameOf(new PartItem { Ref = reference }));
    }

    public sealed class PartList : MakerThumbList
    {
        sealed class ViewState { public string Sort = "list", Filter = "all"; public bool Expanded; public float Zoom = 1f; }
        static readonly Dictionary<string, ViewState> views = new Dictionary<string, ViewState>(StringComparer.Ordinal);

        readonly TargetSelector sel;
        readonly Button sortButton;
        readonly VisualElement headHolder;
        string rowsSignature;
        bool mixed;
        PartItem pending;
        ContentCatalog.BundleOpen pendingOpen;
        int pendingCategory;
        bool scrollWanted = true;
        MakerLists listsSeen;
        int tickCount;

        public FieldSpec Spec { get; }
        /// <summary>The expanded member path (`appearance.chara.face.skin`).</summary>
        public string Path { get; }
        public FieldBinding Binding { get; }
        /// <summary>The primary character's sex (0 male, 1 female) and the list category shown (-1 = none for the sex).</summary>
        public int Sex { get; private set; } = 1;
        public int Category { get; private set; } = -1;
        /// <summary>The bound value of the primary (null when none) and whether the selected characters differ.</summary>
        public string CurrentRef { get; private set; }
        public bool Mixed => mixed;
        /// <summary>The head the list is filtered by (face skins), or null.</summary>
        public string HeadContext { get; private set; }
        /// <summary>The row whose bundle is being opened before its pick (null = none).</summary>
        public string PendingRef => pending?.Ref;
        /// <summary>The result of the last pick (tests; set when the edit ran).</summary>
        public OpResult LastResult { get; private set; }
        /// <summary>Picks that waited for an asynchronous bundle open, and their open-to-edit latency (UF5 / tests).</summary>
        public int AsyncPicks { get; private set; }
        public double LastOpenMs { get; private set; }
        readonly Stopwatch openWatch = new Stopwatch();

        /// <summary>How long a cell must be hovered or keyboard-focused before its row is prefetched (ms).</summary>
        public const int PrefetchDelayMs = 150;
        PartItem hovered, prefetchedItem;
        readonly Stopwatch hoverWatch = new Stopwatch();
        /// <summary>Rows this list asked the catalog to prefetch (tests, the session's numbers).</summary>
        public int Prefetches { get; private set; }

        void Hover(PartItem it)
        {
            hovered = it != null && !it.NotListed && it.Row != null ? it : null;
            hoverWatch.Restart();
        }

        public PartList(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath = null)
            : base(ctx, spec?.LabelKey, CellWidth, 1f, CellText)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path;
            AddToClassList("hs-partlist--list");
            if (!views.TryGetValue(Path, out var v)) views[Path] = v = new ViewState();
            sort = v.Sort; filter = v.Filter;
            sortButton = new Button(ShowSortMenu) { name = "partlist-sort", text = "⇅" };
            sortButton.AddToClassList("hs-partlist__button");
            Loc.Tooltip(sortButton, "maker.list.sortFilter");
            tools.Add(sortButton);
            tools.Add(expandButton);
            Grid.IsCurrent = i => !mixed && !i.NotListed && i.Ref == CurrentRef;
            Grid.IsBusy = i => pending != null && ReferenceEquals(i, pending);
            Grid.Badge = i => i.NotListed ? "!" : i.Row != null && !i.Row.IsVanilla ? "M" : null;
            Grid.CellClass = i => i.NotListed ? "warning" : null;
            Grid.Clicked += Pick;
            Grid.Activated += Pick;
            Grid.HoverChanged += Hover;
            Grid.SelectionChanged += it => { if (Grid.HasKeyboardFocus) Hover(it); };
            Grid.HintKey = spec.OpLabel == OpLabels.AddAccessory ? "maker.list.hintAdd" : "maker.list.hint";
            UseGridKind(Path, v.Zoom);
            if (v.Expanded) SetExpanded(true);
            // the head the skin list is filtered by follows the bound document (a second binding on a hidden holder)
            headHolder = new VisualElement { name = "partlist-head" };
            headHolder.style.display = DisplayStyle.None;
            Add(headHolder);
            if (spec.ListContext == FieldSpec.ListContextHead)
                ctx.Bindings.Bind(headHolder, new FieldBinding { Selector = TargetSelector.Primary(NodeKind.Chara), Path = InspectorSchemas.CharaEditPath("face.head"), Apply = (el, hv) => Rebind() });
            Binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = sel, Path = Path, Apply = (el, bv) => Show(bv) });
            RegisterCallback<AttachToPanelEvent>(_ => { Rebind(); UiDefer.Frames(2, KeepCurrentShown); });
        }

        IReadOnlyList<string> Targets => sel?.Resolve(ctx) ?? new List<string>();

        protected override string CellNameOf(PartItem i) => i.NotListed ? "part-notlisted" : "part-" + i.Ref;

        void Rebind() { if (Binding != null) Show(Binding.Read(ctx)); }

        void Show(BoundValues v)
        {
            var targets = v.Targets ?? Array.Empty<string>();
            string primary = targets.Count > 0 ? targets[0] : null;
            var n = primary != null ? ctx.Model.Find(primary) : null;
            int sex = StudioOps.SexOf(n);
            Sex = sex < 0 ? 1 : sex;
            Category = Spec.CategoryFor(Sex);
            style.display = targets.Count == 0 || Category < 0 ? DisplayStyle.None : DisplayStyle.Flex;
            string cur = v.Value as string;
            bool changed = cur != CurrentRef || v.Mixed != mixed;
            CurrentRef = cur;
            mixed = v.Mixed;
            HeadContext = null;
            if (Spec.ListContext == FieldSpec.ListContextHead && primary != null && FieldAccessor.TryGet(ctx.Model, primary, InspectorSchemas.CharaEditPath("face.head"), out var hv, out _))
                HeadContext = hv as string;
            if (Category < 0) return;
            EnsureRows();
            UpdateHeader();
            Grid.RefreshMarks();
            if (changed || scrollWanted) ScrollToCurrent();
        }

        MakerLists Lists => MakerCatalog.For(ctx.App?.Catalog);

        void EnsureRows()
        {
            var lists = Lists;
            listsSeen = lists;
            string sig = Category + "|" + Sex + "|" + HeadContext + "|" + L10n.Language + "|" + (lists == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(lists)) + "|" + NotListedRef();
            if (sig == rowsSignature) return;
            rowsSignature = sig;
            var sw = Stopwatch.StartNew();
            var items = new List<PartItem>();
            string nl = NotListedRef();
            if (nl != null) items.Add(new PartItem { Ref = nl, NotListed = true, Name = L10n.Format(IsKcoxRef(nl) ? "maker.clothes.kcoxPattern" : "maker.list.notListed", nl) });
            if (lists != null)
            {
                string lang = L10n.Language;
                foreach (var r in RowsOf(lists))
                    items.Add(new PartItem { Row = r, Ref = r.Key.ToString(), Column = Spec.ThumbColumn ?? PartThumbnails.ThumbColumn, Name = MakerLists.NameOf(r, lang) });
            }
            SetAll(items);
            Builds++;
            LastBuildMs = sw.Elapsed.TotalMilliseconds;
            scrollWanted = true;
        }

        static bool IsKcoxRef(string reference) => reference != null && reference.StartsWith(ClothesPatternRow.KcoxGuid + "#", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<ListRow> RowsOf(MakerLists lists)
        {
            ListContext lc = null;
            if (Spec.ListContext == FieldSpec.ListContextHead && ContentId.TryParse(HeadContext, out var head)) lc = ListContext.ForHead(head);
            return lists.Rows(Category, Sex, lc);
        }

        // The bound value when it is not a row of the list (an unlisted head, a missing mod, a skin not listed for the head).
        string NotListedRef()
        {
            if (mixed || string.IsNullOrEmpty(CurrentRef) || !ContentId.TryParse(CurrentRef, out var key)) return null;
            var lists = Lists;
            if (lists == null) return null;
            ListContext lc = null;
            if (Spec.ListContext == FieldSpec.ListContextHead && ContentId.TryParse(HeadContext, out var head)) lc = ListContext.ForHead(head);
            return lists.IsListed(Category, Sex, key, lc) ? null : CurrentRef;
        }

        void UpdateHeader()
        {
            if (mixed) { currentLabel.text = "—"; return; }
            var it = all.FirstOrDefault(i => i.Ref == CurrentRef);
            currentLabel.text = it != null ? it.Name : CurrentRef ?? "";
        }

        protected override void LanguageChanged() { rowsSignature = null; Rebind(); }

        protected override void AfterFilter()
        {
            Grid.RefreshMarks();
        }

        protected override void ViewChanged()
        {
            if (Path == null || !views.TryGetValue(Path, out var v)) return;
            v.Expanded = Expanded; v.Sort = sort; v.Filter = filter;
            if (GridKind == null) v.Zoom = Grid.Zoom;   // a list of a page kind takes the kind's option
        }

        /// <summary>Scrolls after a re-attach that found the current cell outside the shown cells (tests).</summary>
        public int AttachScrolls { get; private set; }

        // after a re-attach: the current cell is scrolled into view only when the grid does not show it
        void KeepCurrentShown()
        {
            if (panel == null || mixed || string.IsNullOrEmpty(CurrentRef) || Category < 0) return;
            if (shown.FindIndex(i => i.Ref == CurrentRef) < 0 || CellOf(CurrentRef) != null) return;
            AttachScrolls++;
            ScrollToCurrent();
        }

        /// <summary>Scrolls the current cell into view (deferred two frames when not laid out yet).</summary>
        public void ScrollToCurrent()
        {
            scrollWanted = false;
            int p = mixed ? -1 : shown.FindIndex(i => i.Ref == CurrentRef);
            if (p < 0) return;
            Grid.ScrollToPos(p);
            UiDefer.Frames(2, () => { if (panel != null && p < shown.Count) Grid.ScrollToPos(p); });
        }

        void ShowSortMenu()
        {
            var items = new List<MenuItem>
            {
                new MenuItem { Id = "sort-list", Text = L10n.Get("maker.list.sortList"), Checked = sort == "list", Run = () => SetSort("list") },
                new MenuItem { Id = "sort-name", Text = L10n.Get("maker.list.sortName"), Checked = sort == "name", Run = () => SetSort("name") },
                MenuItem.Sep(),
                new MenuItem { Id = "filter-all", Text = L10n.Get("maker.list.filterAll"), Checked = filter == "all", Run = () => SetFilter("all") },
                new MenuItem { Id = "filter-vanilla", Text = L10n.Get("maker.list.filterVanilla"), Checked = filter == "vanilla", Run = () => SetFilter("vanilla") },
                new MenuItem { Id = "filter-mods", Text = L10n.Get("maker.list.filterMods"), Checked = filter == "mods", Run = () => SetFilter("mods") },
            };
            ctx.Menus?.Show(items, sortButton, null, "partlist-menu");
        }

        /// <summary>"list" (HS2 order) or "name".</summary>
        public void SetSort(string s) { sort = s == "name" ? "name" : "list"; ApplySearch(); ViewChanged(); }
        /// <summary>"all", "vanilla" or "mods".</summary>
        public void SetFilter(string f) { filter = f == "vanilla" || f == "mods" ? f : "all"; ApplySearch(); ViewChanged(); }
        public string SortMode => sort;
        public string FilterMode => filter;

        /// <summary>Picks the row of <paramref name="reference"/> as a click on its cell would (tests, keyboard).</summary>
        public bool Pick(string reference)
        {
            var it = all.FirstOrDefault(i => !i.NotListed && i.Ref == reference);
            if (it == null) return false;
            Pick(it);
            return true;
        }

        void Pick(PartItem it)
        {
            if (it == null || it.NotListed || it.Row == null) return;
            if (!mixed && it.Ref == CurrentRef && pending == null) return;   // re-picking the current row does nothing
            var cat = ctx.App?.Catalog;
            var e = cat?.GetEntry(it.Row.Category, it.Row.Key);
            string ab = e?["MainAB"];
            if (cat != null && e != null && !string.IsNullOrEmpty(ab) && ab != "0")
            {
                ContentCatalog.BundleOpen open = null;
                try { open = cat.OpenBundleAsync(e, ab, e["MainData"]); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning("[maker] bundle open threw: " + ex.Message); }
                if (open != null && !open.IsDone)
                {
                    // a cold (deflated / LZMA) bundle: open it asynchronously, then edit (a second click replaces the pending row)
                    pending = it;
                    pendingOpen = open;
                    pendingCategory = it.Row.Category;
                    openWatch.Restart();
                    Grid.RefreshMarks();
                    return;
                }
                // a row whose bundle nobody ships but whose folder holds Sideloader loose PNGs opens "Loose" (4.2 GB10, package L):
                // its textures load as loose PNGs, so the pick goes ahead
                if (open != null && !open.Bundle && !open.Loose) { Failed(it); return; }
            }
            pending = null;
            pendingOpen = null;
            Grid.RefreshMarks();
            Edit(it);
        }

        void Failed(PartItem it)
        {
            pending = null;
            pendingOpen = null;
            Grid.RefreshMarks();
            LastResult = OpResult.NotFound(L10n.Format("maker.list.openFailed", it.Name ?? it.Ref), "ref");
            ctx.Toasts?.Show(L10n.Format("maker.list.openFailed", it.Name ?? it.Ref), ToastKind.Error);
        }

        /// <summary>When set, a pick calls this with the row's reference instead of StudioOps.ChangeItem (the Accessories page's
        /// add panel adds an accessory with the picked item: StudioOps.AddAccessory).</summary>
        public Func<string, OpResult> PickOverride;

        void Edit(PartItem it)
        {
            if (PickOverride != null)
            {
                LastResult = PickOverride(it.Ref);
                Binding.MarkDirty();
                return;
            }
            var targets = Targets;
            if (targets.Count == 0) return;
            LastResult = ctx.ShowResult(ctx.Ops.ChangeItem(targets, Path, it.Ref, UiContext.User(Spec.OpLabel)));
            Binding.MarkDirty();
        }

        protected override void Tick()
        {
            base.Tick();
            var catalog = ctx.App?.Catalog;
            if (hovered != null && !ReferenceEquals(hovered, prefetchedItem) && hoverWatch.ElapsedMilliseconds >= PrefetchDelayMs && catalog != null)
            {
                var e = catalog.GetEntry(hovered.Row.Category, hovered.Row.Key);
                if (e != null && catalog.Prefetch(e)) Prefetches++;
                prefetchedItem = hovered;
            }
            catalog?.PumpPrefetch();
            if (pending != null && pendingOpen != null)
            {
                ctx.App?.Catalog?.PumpOpens();
                if (pendingOpen.IsDone)
                {
                    var it = pending;
                    var open = pendingOpen;
                    pending = null;
                    pendingOpen = null;
                    LastOpenMs = openWatch.Elapsed.TotalMilliseconds;
                    AsyncPicks++;
                    Grid.RefreshMarks();
                    if (!open.Bundle && !open.Loose) Failed(it);
                    else Edit(it);
                }
            }
            // a catalog reload (new lists object) rebuilds the rows; checked twice a second
            if (++tickCount % 30 == 0 && panel != null && Category >= 0 && !ReferenceEquals(Lists, listsSeen)) Rebind();
        }
    }
}
