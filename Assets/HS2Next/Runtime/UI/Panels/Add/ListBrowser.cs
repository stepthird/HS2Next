// The list browsers (right sidebar by default) (U23): items (25k rows: group / category
// facets + MultiColumnListView name, category, source, id; the accessories offered as items follow the item rows;
// normalised AND search over name, category, group, source and guid#id; text colour = colour-slot count as vanilla),
// animations (group / category facets, applied to every selected character, U25), maps ("None" row first; the loaded
// map marked; selecting it again is a no-op) and lights (9 rows).
// One generic panel: the facet lists (groups, then the categories of the selected group; "All" searches everything), the
// list, a footer with the action button. Rows are added by double click, Enter, the button or a drag (viewport / tree).
// The body follows its region's shape (BodyShape): in a tall region (a sidebar) the facet lists are stacked above the list
// (each about a fifth of the body's height, at least 96 px); in a wide one (the bottom region) groups | categories | list
// stand side by side (groups at least 180 px, categories at least 200 px, the list at least its columns' minimum); a short
// region too narrow for three columns puts the two facet lists one above the other in a column left of the list. The
// sizes are set from the body's size whenever it changes. A facet row shows the name (cut with an ellipsis, the full text in its
// tooltip) and its count right-aligned.
// v1.2: two views stand at the top of the group facet list, above «All» and the groups — «★ お気に
// 入り» (the favourite rows of every group) and «最近追加» (only rows from zipmods, newest first by the creation date of their
// zipmod file, ties in list order; the base game's rows are not in it) — each with its count under the search; a browser
// without groups (maps, lights) gets a small facet list of the two views and «All». The search applies in both. In «最近追加»
// the order is fixed (the column headers do not sort) and every row's tooltip gives the date its zipmod was placed; the dates
// are read off the main thread the first time the view is shown (one stat per zipmod; the list order stands meanwhile). A
// favourite's row shows a star before its name. A right click on a row (or the menu key / Shift+F10 on the selected row) opens
// its menu: favourite, details, Explorer (its zipmod, or the base game's bundle), copy the path or the id — no delete.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>How one list browser reads and applies its rows.</summary>
    public sealed class ListBrowserSpec<T> where T : class
    {
        public string Id;                       // "items", "anime", "maps", "lights"
        public Func<IReadOnlyList<T>> Rows;
        public Func<T, string> SearchText;
        public Func<T, string> ContentRef;
        /// <summary>Facet levels (0, 1 or 2): group key / label, category key / label (category keys unique per group).</summary>
        public Func<T, string> GroupKey, CategoryKey;
        public Func<string, string> GroupLabel;
        public Func<string, string, string> CategoryLabel;
        /// <summary>Facet orders: key → sort position (vanilla `sort` then first insertion).</summary>
        public Func<string, long> GroupOrder;
        public Func<string, string, long> CategoryOrder;
        public List<(string id, string titleKey, float width, Func<T, string> text)> Columns = new List<(string, string, float, Func<T, string>)>();
        /// <summary>Extra USS class per row (item colour-slot count, loaded map).</summary>
        public Func<T, string> RowClass;
        /// <summary>The footer button's label key and what it does (activate = double click / Enter / button).</summary>
        public string ActionKey;
        public Func<T, OpResult> Activate;
        public Func<bool> CanActivate;
        /// <summary>Drag payload (null = rows are not draggable).</summary>
        public Func<T, DragPayload> Drag;
        /// <summary>Default sort (null = source order).</summary>
        public Dictionary<string, Comparison<T>> Sorts = new Dictionary<string, Comparison<T>>();
        /// <summary>The catalog record of a row (null = not a catalog row, the maps' «None»): its zipmod for «最近追加», the menu's
        /// details and paths (v1.2).</summary>
        public Func<T, StudioRecord> Record;
        /// <summary>The row's name in its menu's details (null = the first column's text).</summary>
        public Func<T, string> Name;
    }

    /// <summary>The layout of a list browser's body: Stacked = the facet lists above the list (a tall region); Column = the
    /// facet lists one above the other left of the list (a short region too narrow for three columns); Wide = groups |
    /// categories | list.</summary>
    public enum BrowserShape { Stacked, Column, Wide }

    public sealed class ListBrowser<T> : VisualElement where T : class
    {
        readonly UiContext ctx;
        readonly ListBrowserSpec<T> spec;
        readonly ListView groups, categories;
        readonly MultiColumnListView list;
        readonly Label status;
        readonly Button action;
        // shown facet rows (null = "All" first); during a search only the values with matches (+ the selected one) are shown
        readonly List<string> groupKeys = new List<string>(), categoryKeys = new List<string>();
        readonly List<string> allGroupKeys = new List<string>(), allCategoryKeys = new List<string>();
        ListIndex<T> index;
        string group, category;
        // v1.2: the view (BrowserViews key or null), the favourites, each row's content id, the zipmod dates of «最近追加»
        string view;
        readonly FavoriteStore favorites;
        readonly ListView views;
        readonly List<string> viewKeys = new List<string>();
        Dictionary<T, string> refs = new Dictionary<T, string>();
        Dictionary<T, long> rowTicks;
        Task<Dictionary<T, long>> datesTask;
        List<SortColumnDescription> savedSorting;
        const string RecentSort = "\u0002recent";

        /// <summary>Milliseconds of the last filter + RefreshItems (U-M2 in Unity).</summary>
        public double LastFilterMs { get; private set; }
        public ListIndex<T> Index => index;
        public MultiColumnListView List => list;
        public ListView Groups => groups;
        public ListView Categories => categories;
        /// <summary>The facet list of the two views and «All» of a browser without groups (maps, lights); null otherwise.</summary>
        public ListView Views => views;
        /// <summary>The view shown: BrowserViews.Favorites / Recent, or null (all / a group).</summary>
        public string View => view;
        /// <summary>True once the zipmod dates of «最近追加» are known (false before the view was first shown).</summary>
        public bool DatesReady => rowTicks != null;
        /// <summary>Main-thread milliseconds of the last view change (the filter, the order and the list refresh).</summary>
        public double LastViewMs { get; private set; }
        /// <summary>Main-thread milliseconds of applying the zipmod dates (the «最近追加» order) once they were read.</summary>
        public double LastDatesMs { get; private set; }
        /// <summary>The body holding the facet lists (in <see cref="Facets"/>) and the list.</summary>
        public VisualElement Body { get; }
        /// <summary>The column or row of the facet lists (null for a browser without facets).</summary>
        public VisualElement Facets { get; }

        public BrowserShape BodyShape { get; private set; } = BrowserShape.Stacked;
        /// <summary>The facet lists stand beside the list (Column or Wide).</summary>
        public bool Wide => BodyShape != BrowserShape.Stacked;

        public const float WideMinWidth = 400f, GroupsMinWidth = 180f, CategoriesMinWidth = 200f, FacetMinHeight = 96f, ColumnMinWidth = 160f;

        /// <summary>The shape of a body of this size: stacked unless it is at least 400 px wide and half again as wide as tall;
        /// then three columns when groups, categories and the list's minimum fit, else the facet column.</summary>
        public static BrowserShape ShapeFor(float width, float height, float listMin)
        {
            if (width < WideMinWidth || width < 1.5f * height) return BrowserShape.Stacked;
            return width >= GroupsMinWidth + CategoriesMinWidth + listMin + 4 ? BrowserShape.Wide : BrowserShape.Column;
        }

        /// <summary>The list's minimum width: its columns' minimums and a scroll bar.</summary>
        float ListMinWidth()
        {
            float w = 16;
            foreach (var c in list.columns) w += c.minWidth.value;
            return w;
        }

        void Fit(float w, float h)
        {
            if (Facets == null || float.IsNaN(w) || float.IsNaN(h) || w <= 1 || h <= 1) return;
            float listMin = ListMinWidth();
            var shape = ShapeFor(w, h, listMin);
            BodyShape = shape;
            Body.EnableInClassList("hs-browser__body--stacked", shape == BrowserShape.Stacked);
            Body.EnableInClassList("hs-browser__body--column", shape == BrowserShape.Column);
            Body.EnableInClassList("hs-browser__body--wide", shape == BrowserShape.Wide);
            var facetLists = new VisualElement[] { groups, categories };
            // the views of a browser without groups: three rows, a group list's width beside the list
            if (views != null)
            {
                float vh = 3 * FacetRowHeight + 6;
                views.style.height = shape == BrowserShape.Wide ? StyleKeyword.Null : (StyleLength)vh;
                views.style.width = shape == BrowserShape.Wide ? (StyleLength)GroupsMinWidth : StyleKeyword.Null;
            }
            switch (shape)
            {
                case BrowserShape.Stacked:
                {
                    float fh = Mathf.Round(Mathf.Max(FacetMinHeight, 0.22f * h));
                    Facets.style.width = StyleKeyword.Null;
                    foreach (var f in facetLists) if (f != null) { f.style.height = fh; f.style.width = StyleKeyword.Null; }
                    break;
                }
                case BrowserShape.Wide:
                {
                    float gw = Mathf.Max(GroupsMinWidth, 0.2f * w), cw = Mathf.Max(CategoriesMinWidth, 0.22f * w);
                    if (gw + cw > w - listMin - 4) { gw = GroupsMinWidth; cw = CategoriesMinWidth; }
                    Facets.style.width = StyleKeyword.Null;
                    if (groups != null) { groups.style.width = Mathf.Round(gw); groups.style.height = StyleKeyword.Null; }
                    if (categories != null) { categories.style.width = Mathf.Round(cw); categories.style.height = StyleKeyword.Null; }
                    break;
                }
                default:
                {
                    float colW = Mathf.Max(ColumnMinWidth, Mathf.Min(Mathf.Max(CategoriesMinWidth, 0.4f * w), w - listMin - 4));
                    Facets.style.width = Mathf.Round(colW);
                    foreach (var f in facetLists) if (f != null) { f.style.height = StyleKeyword.Null; f.style.width = StyleKeyword.Null; }
                    break;
                }
            }
        }

        public ListBrowser(UiContext ctx, ListBrowserSpec<T> spec)
        {
            this.ctx = ctx;
            this.spec = spec;
            favorites = Favorites.Of(ctx);
            name = spec.Id + "-browser"; PanelKit.Style(this);
            AddToClassList("hs-panel");
            AddToClassList("hs-browser");

            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");
            var search = PanelKit.SearchField(spec.Id + "-search", q => { index.SetQuery(q); Refresh(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            Add(header);

            var body = new VisualElement { name = spec.Id + "-body" };
            body.AddToClassList("hs-browser__body");
            body.AddToClassList("hs-browser__body--stacked");
            body.AddToClassList("hs-grow");
            Body = body;
            if (spec.GroupKey != null || spec.CategoryKey != null || spec.Record != null)
            {
                Facets = new VisualElement { name = spec.Id + "-facets" };
                Facets.AddToClassList("hs-browser__facets");
                body.Add(Facets);
                body.RegisterCallback<GeometryChangedEvent>(e => Fit(e.newRect.width, e.newRect.height));
            }
            if (spec.GroupKey != null)
            {
                groups = Facet(spec.Id + "-groups", groupKeys, k => k == null ? L10n.Get("common.all") : BrowserViews.Is(k) ? L10n.Get(BrowserViews.LabelKey(k)) : spec.GroupLabel(k), FacetCount("group"),
                    k => { if (BrowserViews.Is(k)) { view = k; group = null; } else { view = null; group = k; } category = null; ApplyFacets(); });
                Facets.Add(groups);
            }
            else if (spec.Record != null)
            {
                views = Facet(spec.Id + "-views", viewKeys, k => k == null ? L10n.Get("common.all") : L10n.Get(BrowserViews.LabelKey(k)), FacetCount("views"),
                    k => { view = BrowserViews.Is(k) ? k : null; ApplyFacets(); });
                views.RemoveFromClassList("hs-facet-list--categories");
                views.AddToClassList("hs-facet-list--views");
                Facets.Add(views);
            }
            if (spec.CategoryKey != null)
            {
                categories = Facet(spec.Id + "-categories", categoryKeys, k => k == null ? L10n.Get("common.all") : spec.CategoryLabel(group, k), FacetCount("category"), k => { category = k; ApplyFacets(); });
                Facets.Add(categories);
            }
            list = new MultiColumnListView
            {
                name = spec.Id + "-list",
                fixedItemHeight = 22,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                sortingMode = spec.Sorts.Count > 0 ? ColumnSortingMode.Custom : ColumnSortingMode.None,
            };
            list.AddToClassList("hs-grow");
            PanelKit.EllipsizeHeaders(list);
            foreach (var c in spec.Columns) AddColumn(c.id, c.titleKey, c.width, c.text);
            list.itemsChosen += _ => ActivateSelected();
            list.selectionChanged += _ => UpdateButton();
            list.columnSortingChanged += () =>
            {
                if (view == BrowserViews.Recent) return;   // «最近追加» keeps its order (the headers were cleared)
                var s = list.sortedColumns.FirstOrDefault();
                if (s == null) index.SetSort(null);
                else if (spec.Sorts.ContainsKey(s.columnName)) index.SetSort(s.columnName, s.direction == SortDirection.Descending);
                Refresh();
            };
            list.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { ActivateSelected(); e.StopPropagation(); return; }
                if (ListMenus.IsMenuKey(e) && Selected != null)
                {
                    var row = list.GetRootElementForIndex(list.selectedIndex);
                    var wb = row != null ? row.worldBound : list.worldBound;
                    ShowMenu(Selected, new Vector2(wb.xMin + 24, row != null ? wb.yMax : wb.yMin + 24));
                    e.StopPropagation();
                }
            });
            body.Add(list);
            Add(body);

            var footer = new VisualElement();
            footer.AddToClassList("hs-panel__footer");
            action = Loc.Button(spec.ActionKey, () => ActivateSelected());
            action.name = spec.Id + "-action";
            footer.Add(action);
            status = new Label { name = spec.Id + "-status" };
            status.AddToClassList("hs-status-text");
            PanelKit.StatusLine(status);
            footer.Add(status);
            Add(footer);

            Reload();
            ctx.SelectionChanged += _ => UpdateButton();
            Favorites.Changed += k => { if (k == null || k == spec.Id) FavoritesChanged(); };
            PanelKit.EveryFrame(this, Tick);
        }

        const string CatSep = "\u0001";
        // the facet counts with what they depend on (v1.2: a view change or a list refresh does not count the 28k rows again):
        // the groups' and «最近追加»'s counts follow the search only, the favourites' also the store, the categories also the group
        readonly Dictionary<string, (string key, SortedDictionary<string, int> counts)> facetCounts = new Dictionary<string, (string, SortedDictionary<string, int>)>();
        ListIndex<T> countedIndex;

        string CountKey(string facet)
        {
            string q = index.Query ?? "";
            switch (facet)
            {
                case "group":
                case "recent": return q;
                case "fav": return q + "\u0001" + favorites.Revision;
                default: return q + "\u0001" + group + "\u0001" + view + "\u0001" + favorites.Revision;
            }
        }

        /// <summary>The height of a facet row (px).</summary>
        public const float FacetRowHeight = 20f;

        Func<string, int> FacetCount(string facet) => k =>
        {
            if (index == null || k == null) return -1;
            // a view's count: its rows under the search (whatever group is selected)
            if (BrowserViews.Is(k)) return Counts(k == BrowserViews.Favorites ? "fav" : "recent").TryGetValue("y", out int v) ? v : 0;
            string key = facet == "category" ? group + CatSep + k : k;
            return Counts(facet).TryGetValue(key, out int c) ? c : 0;
        };

        // the group counts ignore the category facet (categories exist inside one group: a selected category zeroed every
        // other group, v0.2 "(0)") and the views (a view and a group are not shown together); a view's count ignores the groups
        SortedDictionary<string, int> Counts(string facet)
        {
            if (!ReferenceEquals(countedIndex, index)) { facetCounts.Clear(); countedIndex = index; }
            string ck = CountKey(facet);
            if (!facetCounts.TryGetValue(facet, out var cached) || cached.key != ck)
            {
                SortedDictionary<string, int> counts;
                if (facet == "fav" || facet == "recent")
                {
                    var skip = new List<string> { facet == "fav" ? "recent" : "fav" };
                    if (spec.GroupKey != null) skip.Add("group");
                    if (spec.CategoryKey != null) skip.Add("category");
                    counts = index.FacetCounts(facet, skip.ToArray());
                }
                else if (facet == "group") counts = spec.CategoryKey != null ? index.FacetCounts("group", "category", "fav", "recent") : index.FacetCounts("group", "fav", "recent");
                else counts = index.FacetCounts(facet);
                facetCounts[facet] = (ck, counts);
                return counts;
            }
            return cached.counts;
        }

        /// <summary>The facet rows shown: every value without a query; with one, the values that have matches (
        /// "non-empty only"; v0.2 listed all 111 item groups with "(0)") plus the selected value. Keeps the list selection on
        /// the selected value.</summary>
        void ShowFacetRows()
        {
            bool searching = !string.IsNullOrEmpty(index.Query);
            if (spec.GroupKey != null)
            {
                var gc = searching ? Counts("group") : null;
                groupKeys.Clear();
                if (spec.Record != null) { groupKeys.Add(BrowserViews.Favorites); groupKeys.Add(BrowserViews.Recent); }
                groupKeys.Add(null);
                foreach (var k in allGroupKeys) if (!searching || k == group || gc.ContainsKey(k)) groupKeys.Add(k);
                groups.RefreshItems();
                int sel = view != null ? groupKeys.IndexOf(view) : groupKeys.IndexOf(group);
                groups.SetSelectionWithoutNotify(new[] { sel >= 0 ? sel : groupKeys.IndexOf(null) });
            }
            else if (views != null)
            {
                viewKeys.Clear();
                viewKeys.Add(BrowserViews.Favorites);
                viewKeys.Add(BrowserViews.Recent);
                viewKeys.Add(null);
                views.RefreshItems();
                views.SetSelectionWithoutNotify(new[] { viewKeys.IndexOf(view) });
            }
            if (spec.CategoryKey != null)
            {
                var cc = searching && group != null ? Counts("category") : null;
                categoryKeys.Clear();
                categoryKeys.Add(null);
                foreach (var k in allCategoryKeys) if (cc == null || k == category || cc.ContainsKey(group + CatSep + k)) categoryKeys.Add(k);
                categories.RefreshItems();
                categories.SetSelectionWithoutNotify(new[] { Math.Max(0, categoryKeys.IndexOf(category)) });
            }
        }

        ListView Facet(string id, List<string> keys, Func<string, string> label, Func<string, int> count, Action<string> picked)
        {
            var lv = new ListView
            {
                name = id,
                itemsSource = keys,
                fixedItemHeight = FacetRowHeight,
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    var row = new VisualElement();
                    row.AddToClassList("hs-facet__row");
                    var n = new Label { name = "facet-name" };
                    n.AddToClassList("hs-facet__name");
                    var k = new Label { name = "facet-count" };
                    k.AddToClassList("hs-facet__count");
                    row.Add(n);
                    row.Add(k);
                    return row;
                },
                bindItem = (e, i) =>
                {
                    string k = keys[i];
                    int c = count(k);
                    string text = label(k);
                    e.Q<Label>("facet-name").text = text;
                    e.Q<Label>("facet-count").text = c >= 0 ? c.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
                    e.tooltip = c >= 0 ? text + "  (" + c + ")" : text;
                    e.EnableInClassList("hs-facet__row--view", BrowserViews.Is(k));
                    e.name = BrowserViews.Is(k) ? id + "-" + (k == BrowserViews.Favorites ? "favorites" : "recent") : string.Empty;
                    if (BrowserViews.Is(k)) e.tooltip += "\n" + L10n.Get(BrowserViews.TooltipKey(k, false));
                },
            };
            lv.AddToClassList("hs-facet-list");
            lv.AddToClassList(id.EndsWith("-groups", StringComparison.Ordinal) ? "hs-facet-list--groups" : "hs-facet-list--categories");
            lv.selectionChanged += sel => { if (lv.selectedIndex >= 0) picked(keys[lv.selectedIndex]); };
            return lv;
        }

        void AddColumn(string id, string titleKey, float width, Func<T, string> text)
        {
            // every column shrinks in proportion down to its minimum (v0.2: only the name column was stretchable, so in the
            // bottom region it shrank to Unity's 35 px minimum while the fixed columns kept 440 px)
            var c = new Column { name = id, title = L10n.Get(titleKey), width = width, minWidth = id == "name" ? 140 : 64, stretchable = true, sortable = spec.Sorts.ContainsKey(id) };
            c.makeCell = () =>
            {
                var l = new Label();
                l.AddToClassList("hs-list-cell");
                if (spec.Drag != null) DragController.MakeSource(l, _ => l.userData is T row ? spec.Drag(row) : null);
                // a right click selects the row; its menu opens on the release
                l.RegisterCallback<PointerDownEvent>(ev => { if (ev.button == 1 && l.userData is T row && spec.Record != null) { SelectInList(row); ev.StopPropagation(); } });
                l.RegisterCallback<PointerUpEvent>(ev => { if (ev.button == 1 && l.userData is T row && spec.Record != null) { ShowMenu(row, ev.position); ev.StopPropagation(); } });
                return l;
            };
            c.bindCell = (el, i) =>
            {
                var row = index.Rows[index.Indices[i]];
                var l = (Label)el;
                l.text = id == "name" && IsFavorite(row) ? BrowserViews.Star + text(row) : text(row);
                l.userData = row;
                l.tooltip = view == BrowserViews.Recent ? AddedTip(row) : null;
                if (spec.RowClass != null)
                {
                    foreach (var cls in l.GetClasses().Where(x => x.StartsWith("hs-row--", StringComparison.Ordinal)).ToList()) l.RemoveFromClassList(cls);
                    string rc = spec.RowClass(row);
                    if (rc != null) l.AddToClassList(rc);
                }
            };
            list.columns.Add(c);
        }

        /// <summary>Reads the rows again (content changed, a map loaded); the search text and the sort stay (
        /// item 3).</summary>
        public void Reload()
        {
            var rows = spec.Rows() ?? Array.Empty<T>();
            string query = index?.Query, sortName = index?.SortName;
            bool descending = index?.Descending ?? false;
            index = new ListIndex<T>(rows, spec.SearchText, spec.ContentRef);
            foreach (var s in spec.Sorts) index.AddSort(s.Key, s.Value);
            index.AddSort(RecentSort, CompareRecent);
            if (!string.IsNullOrEmpty(query)) index.SetQuery(query);
            if (sortName != null && spec.Sorts.ContainsKey(sortName)) index.SetSort(sortName, descending);
            if (spec.GroupKey != null) index.AddFacet("group", spec.GroupKey);
            if (spec.CategoryKey != null) index.AddFacet("category", r => spec.GroupKey(r) + CatSep + spec.CategoryKey(r));
            // v1.2: each row's content id once (the favourites are looked up per row), the two views' facets
            refs = new Dictionary<T, string>(rows.Count);
            if (spec.ContentRef != null && spec.Record != null) foreach (var r in rows) { var rf = spec.ContentRef(r); if (rf != null && !refs.ContainsKey(r)) refs[r] = rf; }
            rowTicks = null;
            index.AddFacet("fav", r => IsFavorite(r) ? "y" : "n");
            index.AddFacet("recent", r => ZipmodOf(r) != null ? "y" : "n");
            allGroupKeys.Clear();
            if (spec.GroupKey != null)
                allGroupKeys.AddRange(rows.Select(spec.GroupKey).Distinct().OrderBy(k => spec.GroupOrder?.Invoke(k) ?? 0).ThenBy(k => k, StringComparer.Ordinal));
            ApplyFacets();
        }

        void ApplyFacets()
        {
            var sw = Stopwatch.StartNew();
            if (spec.GroupKey != null) index.SetFacet("group", group);
            index.SetFacet("fav", view == BrowserViews.Favorites ? "y" : null);
            index.SetFacet("recent", view == BrowserViews.Recent ? "y" : null);
            ApplyViewOrder();
            if (spec.CategoryKey != null)
            {
                allCategoryKeys.Clear();
                if (group != null)
                    allCategoryKeys.AddRange(index.Rows.Where(r => spec.GroupKey(r) == group).Select(spec.CategoryKey).Distinct()
                        .OrderBy(k => spec.CategoryOrder?.Invoke(group, k) ?? 0).ThenBy(k => k, StringComparer.Ordinal));
                index.SetFacet("category", group != null && category != null ? group + CatSep + category : null);
                categories.style.display = group != null ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Refresh();
            LastViewMs = sw.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------------ v1.2: views, favourites, menu

        ZipmodInfo ZipmodOf(T r) => spec.Record?.Invoke(r)?.Zipmod;

        bool IsFavorite(T r) => favorites.RowsOf(spec.Id).Count > 0 && refs.TryGetValue(r, out var rf) && favorites.IsRow(spec.Id, rf);

        // newest zipmod first; the index breaks ties in list order
        int CompareRecent(T a, T b)
        {
            if (rowTicks == null) return 0;
            rowTicks.TryGetValue(a, out long ta);
            rowTicks.TryGetValue(b, out long tb);
            return tb.CompareTo(ta);
        }

        /// <summary>«最近追加»: the fixed order (the column headers cleared and kept for later) and the dates read when first needed;
        /// another view: the headers' order again.</summary>
        void ApplyViewOrder()
        {
            if (view == BrowserViews.Recent)
            {
                if (savedSorting == null && spec.Sorts.Count > 0)
                {
                    savedSorting = list.sortColumnDescriptions.ToList();
                    list.sortColumnDescriptions.Clear();
                }
                index.SetSort(RecentSort);
                if (rowTicks == null && datesTask == null)
                {
                    // the dates and each row's date (one look-up per zipmod) on a worker; the main thread only sorts
                    var rows = index.Rows;
                    var zs = new List<ZipmodInfo>();
                    foreach (var r in rows) { var z = ZipmodOf(r); if (z != null) zs.Add(z); }
                    var rec = spec.Record;
                    datesTask = ZipmodDates.Ensure(zs).ContinueWith(_ =>
                    {
                        var byZip = new Dictionary<ZipmodInfo, long>();
                        var t = new Dictionary<T, long>(zs.Count);
                        foreach (var r in rows)
                        {
                            var z = rec(r)?.Zipmod;
                            if (z == null) continue;
                            if (!byZip.TryGetValue(z, out long v)) byZip[z] = v = ZipmodDates.Ticks(z);
                            t[r] = v;
                        }
                        return t;
                    }, TaskScheduler.Default);
                }
                return;
            }
            if (savedSorting != null)
            {
                var keep = savedSorting;
                savedSorting = null;
                foreach (var d in keep) list.sortColumnDescriptions.Add(d);
            }
            var s = list.sortedColumns.FirstOrDefault();
            if (s == null) index.SetSort(null);
            else if (spec.Sorts.ContainsKey(s.columnName)) index.SetSort(s.columnName, s.direction == SortDirection.Descending);
        }

        void Tick()
        {
            if (datesTask == null || !datesTask.IsCompleted) return;
            var task = datesTask;
            datesTask = null;
            var sw = Stopwatch.StartNew();
            rowTicks = task.Status == TaskStatus.RanToCompletion && task.Result != null ? task.Result : new Dictionary<T, long>();
            index.AddSort(RecentSort, CompareRecent);   // the order changed: the index sorts again
            if (view == BrowserViews.Recent) { index.SetSort(null); index.SetSort(RecentSort); }
            Refresh();
            LastDatesMs = sw.Elapsed.TotalMilliseconds;
        }

        string AddedTip(T r)
        {
            var z = ZipmodOf(r);
            if (z == null) return null;
            long t = rowTicks != null && rowTicks.TryGetValue(r, out long v) ? v : 0;
            string date = t > 0 ? new DateTime(t, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "…";
            return L10n.Format("browser.added", date) + " · " + System.IO.Path.GetFileName(z.Path ?? "");
        }

        void FavoritesChanged()
        {
            if (index == null) return;
            index.AddFacet("fav", r => IsFavorite(r) ? "y" : "n");   // the values changed: the facet is evaluated again
            ApplyFacets();
            list.RefreshItems();
        }

        /// <summary>Shows a view (BrowserViews key) or «All» (null) as a click on its facet row does (tests).</summary>
        public void ShowView(string key)
        {
            view = BrowserViews.Is(key) ? key : null;
            if (view != null) { group = null; category = null; }
            ApplyFacets();
        }

        void SelectInList(T row)
        {
            for (int i = 0; i < index.Indices.Count; i++)
                if (ReferenceEquals(index.Rows[index.Indices[i]], row)) { if (list.selectedIndex != i) list.SetSelection(i); return; }
        }

        /// <summary>The row's information for the shared menu.</summary>
        public CatalogRow RowInfo(T r)
        {
            if (r == null) return null;
            string gk = spec.GroupKey?.Invoke(r), ck = spec.CategoryKey?.Invoke(r);
            return new CatalogRow
            {
                Kind = spec.Id, Ref = refs.TryGetValue(r, out var rf) ? rf : spec.ContentRef?.Invoke(r), Record = spec.Record?.Invoke(r),
                Name = spec.Name != null ? spec.Name(r) : spec.Columns.Count > 0 ? spec.Columns[0].text(r) : null,
                Group = gk != null ? spec.GroupLabel?.Invoke(gk) : null,
                Category = gk != null && ck != null ? spec.CategoryLabel?.Invoke(gk, ck) : null,
            };
        }

        /// <summary>The context menu of a row at a panel position (a row that is not a catalog row has none).</summary>
        public void ShowMenu(T r, Vector2 at)
        {
            var info = RowInfo(r);
            if (info?.Record == null || info.Ref == null) return;
            ListMenus.ShowRow(ctx, info, at, spec.Id + "-context");
        }

        /// <summary>Filters and refreshes the list; returns the time (U-M2).</summary>
        public double Refresh()
        {
            var sw = Stopwatch.StartNew();
            var ch = index.Refresh();
            if (list.itemsSource != index.Indices) { list.itemsSource = index.Indices; list.Rebuild(); }
            else if (ch != ListChange.Unchanged) list.RefreshItems();
            LastFilterMs = sw.Elapsed.TotalMilliseconds;
            ShowFacetRows();
            status.text = L10n.Format(view != BrowserViews.Recent ? "search.results" : rowTicks == null ? "browser.view.recent.reading" : "browser.view.recent.status", index.Indices.Count, index.Rows.Count);
            UpdateButton();
            return LastFilterMs;
        }

        public T Selected => list.selectedIndex >= 0 && list.selectedIndex < index.Indices.Count ? index.Rows[index.Indices[list.selectedIndex]] : null;

        public void SelectRow(T row)
        {
            for (int i = 0; i < index.Indices.Count; i++)
                if (ReferenceEquals(index.Rows[index.Indices[i]], row)) { list.SetSelection(i); list.ScrollToItem(i); return; }
        }

        void UpdateButton() => action.SetEnabled(Selected != null && (spec.CanActivate == null || spec.CanActivate()));

        public OpResult ActivateSelected()
        {
            var s = Selected;
            if (s == null || (spec.CanActivate != null && !spec.CanActivate())) return null;
            return ctx.ShowResult(spec.Activate(s));
        }

        /// <summary>Tests and the bridge: the search text (applied at once, no debounce).</summary>
        public double Search(string q)
        {
            index.SetQuery(q);
            return Refresh();
        }

        public void SelectGroup(string key) { group = key; category = null; view = null; ApplyFacets(); }
    }

    /// <summary>The specs of the four list browsers.</summary>
    public static class ListBrowsers
    {
        public static string Source(StudioRecord r) => r.IsVanilla ? L10n.Get("browser.vanilla") : r.Zipmod?.Name ?? r.Guid;

        public static ListBrowserSpec<StudioItem> Items(UiContext ctx)
        {
            var db = ctx.App.Db;
            // the accessories offered as items have a localized group label (the lists' own name is data)
            string GroupName(int g) => g == StudioDb.AccessoryGroup ? L10n.Get("browser.group.accessories")
                : db != null && db.TryGetItemGroup(g, out var sg) ? sg.Name : g.ToString();
            string CatName(int g, int c) => db != null && db.TryGetItemCategory(g, c, out var sc) ? sc.Name : c.ToString();
            var s = new ListBrowserSpec<StudioItem>
            {
                Id = "items",
                Rows = () => db == null ? new List<StudioItem>() : db.Items.Enumerate().Concat(db.AccessoryItems).ToList(),
                SearchText = r => r.Name + " " + CatName(r.Group, r.Category) + " " + GroupName(r.Group) + " " + Source(r),
                ContentRef = r => r.Key.ToString(),
                GroupKey = r => r.Group.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CategoryKey = r => r.Category.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GroupLabel = k => GroupName(int.Parse(k)),
                CategoryLabel = (g, c) => CatName(int.Parse(g), int.Parse(c)),
                GroupOrder = k => db != null && db.TryGetItemGroup(int.Parse(k), out var sg) ? (long)sg.Sort * 1000000 + sg.Position : 0,
                CategoryOrder = (g, c) => db != null && db.TryGetItemCategory(int.Parse(g), int.Parse(c), out var sc) ? (long)sc.Sort * 1000000 + sc.Position : 0,
                RowClass = r => "hs-row--colors" + (r.Color?.FlagCount ?? 0),
                ActionKey = "browser.add",
                Activate = r => AddItem(ctx, r, null, null),
                Drag = r => new AddPayload(r.Name, OpLabels.AddItem, (p, pt) => AddItem(ctx, r, p, pt)),
                Record = r => r,
                Name = r => r.Name,
            };
            s.Columns.Add(("name", "browser.col.name", 260, r => (r.Color != null && r.Color.FlagCount > 0 ? "● " : "") + r.Name));
            s.Columns.Add(("category", "browser.col.category", 160, r => CatName(r.Group, r.Category)));
            s.Columns.Add(("source", "browser.col.source", 160, Source));
            s.Columns.Add(("id", "browser.col.id", 120, r => r.Key.ToString()));
            s.Sorts["name"] = (a, b) => ListSorts.CompareJa(a.Name, b.Name);
            s.Sorts["source"] = (a, b) => ListSorts.CompareOrdinalIgnoreCase(Source(a), Source(b));
            return s;
        }

        public static OpResult AddItem(UiContext ctx, StudioItem r, string parent, string point)
        {
            PanelKit.ApplyPlacement(ctx);
            return ctx.Ops.AddItem(r, parent, point, -1, UiContext.User());
        }

        public static ListBrowserSpec<StudioAnime> Anime(UiContext ctx)
        {
            var db = ctx.App.Db;
            string GroupName(int g) => db != null && db.TryGetAnimeGroup(g, out var sg) ? sg.Name : g.ToString();
            string CatName(int g, int c) => db != null && db.TryGetAnimeCategory(g, c, out var sc) ? sc.Name : c.ToString();
            var s = new ListBrowserSpec<StudioAnime>
            {
                Id = "anime",
                // every row the lists show, H rows included (no sex or H filter); the female and male H groups name their rows
                // alike, so the group is a column too
                Rows = () => db?.Anime.Enumerate().ToList() ?? new List<StudioAnime>(),
                SearchText = r => r.Name + " " + CatName(r.Group, r.Category) + " " + GroupName(r.Group) + " " + Source(r),
                ContentRef = r => r.Guid + "#" + r.No,
                GroupKey = r => r.Group.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CategoryKey = r => r.Category.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GroupLabel = k => GroupName(int.Parse(k)),
                CategoryLabel = (g, c) => CatName(int.Parse(g), int.Parse(c)),
                GroupOrder = k => db != null && db.TryGetAnimeGroup(int.Parse(k), out var sg) ? (long)sg.Sort * 1000000 + sg.Position : 0,
                CategoryOrder = (g, c) => db != null && db.TryGetAnimeCategory(int.Parse(g), int.Parse(c), out var sc) ? (long)sc.Sort * 1000000 + sc.Position : 0,
                ActionKey = "browser.applyAnime",
                CanActivate = () => ctx.SelectedOfKind(NodeKind.Chara).Count > 0,
                Activate = r => ctx.Ops.SetAnimation(ctx.SelectedOfKind(NodeKind.Chara), r, 0f, null, null, null, UiContext.User()),
                Drag = r => new ApplyPayload(r.Name, NodeKind.Chara, id => ctx.Ops.SetAnimation(new[] { id }, r, 0f, null, null, null, UiContext.User())),
                Record = r => r,
                Name = r => r.Name,
            };
            s.Columns.Add(("name", "browser.col.name", 260, r => r.Name));
            s.Columns.Add(("group", "browser.col.group", 160, r => GroupName(r.Group)));
            s.Columns.Add(("category", "browser.col.category", 180, r => CatName(r.Group, r.Category)));
            s.Columns.Add(("source", "browser.col.source", 160, Source));
            s.Sorts["name"] = (a, b) => ListSorts.CompareJa(a.Name, b.Name);
            return s;
        }

        /// <summary>A map row of the map browser; Map null = "None".</summary>
        public sealed class MapRow
        {
            public StudioMap Map;
            public string Name;
            public string Ref => Map?.Key.ToString();
        }

        public static ListBrowserSpec<MapRow> Maps(UiContext ctx)
        {
            var db = ctx.App.Db;
            var s = new ListBrowserSpec<MapRow>
            {
                Id = "maps",
                Rows = () =>
                {
                    var l = new List<MapRow> { new MapRow { Map = null, Name = L10n.Get("common.none") } };
                    if (db != null) l.AddRange(db.Maps.Enumerate().Select(m => new MapRow { Map = m, Name = m.Name }));
                    return l;
                },
                SearchText = r => r.Name + " " + (r.Map != null ? Source(r.Map) : ""),
                ContentRef = r => r.Ref,
                RowClass = r => string.Equals(r.Ref, ctx.Model.Settings.Map.Ref, StringComparison.Ordinal) ? "hs-row--current" : null,
                ActionKey = "browser.loadMap",
                Activate = r => ctx.Ops.LoadMap(r.Ref, UiContext.User()),
                Record = r => r.Map,
                Name = r => r.Name,
            };
            s.Columns.Add(("name", "browser.col.name", 280, r => r.Name));
            s.Columns.Add(("source", "browser.col.source", 180, r => r.Map != null ? Source(r.Map) : ""));
            return s;
        }

        public static ListBrowserSpec<StudioLight> Lights(UiContext ctx)
        {
            var db = ctx.App.Db;
            var s = new ListBrowserSpec<StudioLight>
            {
                Id = "lights",
                Rows = () => db?.Lights.Enumerate().ToList() ?? new List<StudioLight>(),
                SearchText = r => r.Name,
                ContentRef = r => r.Key.ToString(),
                ActionKey = "browser.add",
                Activate = r => AddLight(ctx, r, null, null),
                Drag = r => new AddPayload(r.Name, OpLabels.AddLight, (p, pt) => AddLight(ctx, r, p, pt)),
                Record = r => r,
                Name = r => r.Name,
            };
            s.Columns.Add(("name", "browser.col.name", 260, r => r.Name));
            s.Columns.Add(("target", "browser.col.target", 120, r => L10n.Get("browser.lightTarget." + r.TargetKind.ToString().ToLowerInvariant())));
            return s;
        }

        public static OpResult AddLight(UiContext ctx, StudioLight r, string parent, string point)
        {
            PanelKit.ApplyPlacement(ctx);
            return ctx.Ops.AddLight(r.Key, parent, point, -1, UiContext.User());
        }
    }
}
