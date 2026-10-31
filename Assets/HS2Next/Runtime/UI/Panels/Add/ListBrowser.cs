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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        /// <summary>Milliseconds of the last filter + RefreshItems (U-M2 in Unity).</summary>
        public double LastFilterMs { get; private set; }
        public ListIndex<T> Index => index;
        public MultiColumnListView List => list;
        public ListView Groups => groups;
        public ListView Categories => categories;
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
            if (spec.GroupKey != null || spec.CategoryKey != null)
            {
                Facets = new VisualElement { name = spec.Id + "-facets" };
                Facets.AddToClassList("hs-browser__facets");
                body.Add(Facets);
                body.RegisterCallback<GeometryChangedEvent>(e => Fit(e.newRect.width, e.newRect.height));
            }
            if (spec.GroupKey != null)
            {
                groups = Facet(spec.Id + "-groups", groupKeys, k => k == null ? L10n.Get("common.all") : spec.GroupLabel(k), FacetCount("group"), k => { group = k; category = null; ApplyFacets(); });
                Facets.Add(groups);
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
            foreach (var c in spec.Columns) AddColumn(c.id, c.titleKey, c.width, c.text);
            list.itemsChosen += _ => ActivateSelected();
            list.selectionChanged += _ => UpdateButton();
            list.columnSortingChanged += () =>
            {
                var s = list.sortedColumns.FirstOrDefault();
                if (s == null) index.SetSort(null);
                else if (spec.Sorts.ContainsKey(s.columnName)) index.SetSort(s.columnName, s.direction == SortDirection.Descending);
                Refresh();
            };
            list.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { ActivateSelected(); e.StopPropagation(); } });
            body.Add(list);
            Add(body);

            var footer = new VisualElement();
            footer.AddToClassList("hs-panel__footer");
            action = Loc.Button(spec.ActionKey, () => ActivateSelected());
            action.name = spec.Id + "-action";
            footer.Add(action);
            status = new Label { name = spec.Id + "-status" };
            status.AddToClassList("hs-status-text");
            footer.Add(status);
            Add(footer);

            Reload();
            ctx.SelectionChanged += _ => UpdateButton();
        }

        const string CatSep = "\u0001";
        readonly Dictionary<string, SortedDictionary<string, int>> facetCounts = new Dictionary<string, SortedDictionary<string, int>>();

        /// <summary>The height of a facet row (px).</summary>
        public const float FacetRowHeight = 20f;

        Func<string, int> FacetCount(string facet) => k =>
        {
            if (index == null || k == null) return -1;
            string key = facet == "category" ? group + CatSep + k : k;
            return Counts(facet).TryGetValue(key, out int c) ? c : 0;
        };

        // the group counts ignore the category facet (categories exist inside one group: a selected category zeroed every
        // other group, v0.2 "(0)")
        SortedDictionary<string, int> Counts(string facet)
        {
            if (!facetCounts.TryGetValue(facet, out var counts))
                facetCounts[facet] = counts = facet == "group" && spec.CategoryKey != null ? index.FacetCounts("group", "category") : index.FacetCounts(facet);
            return counts;
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
                groupKeys.Add(null);
                foreach (var k in allGroupKeys) if (!searching || k == group || gc.ContainsKey(k)) groupKeys.Add(k);
                groups.RefreshItems();
                groups.SetSelectionWithoutNotify(new[] { Math.Max(0, groupKeys.IndexOf(group)) });
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
                return l;
            };
            c.bindCell = (el, i) =>
            {
                var row = index.Rows[index.Indices[i]];
                var l = (Label)el;
                l.text = text(row);
                l.userData = row;
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
            if (!string.IsNullOrEmpty(query)) index.SetQuery(query);
            if (sortName != null && spec.Sorts.ContainsKey(sortName)) index.SetSort(sortName, descending);
            if (spec.GroupKey != null) index.AddFacet("group", spec.GroupKey);
            if (spec.CategoryKey != null) index.AddFacet("category", r => spec.GroupKey(r) + CatSep + spec.CategoryKey(r));
            allGroupKeys.Clear();
            if (spec.GroupKey != null)
                allGroupKeys.AddRange(rows.Select(spec.GroupKey).Distinct().OrderBy(k => spec.GroupOrder?.Invoke(k) ?? 0).ThenBy(k => k, StringComparer.Ordinal));
            ApplyFacets();
        }

        void ApplyFacets()
        {
            if (spec.GroupKey != null) index.SetFacet("group", group);
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
        }

        /// <summary>Filters and refreshes the list; returns the time (U-M2).</summary>
        public double Refresh()
        {
            var sw = Stopwatch.StartNew();
            var ch = index.Refresh();
            if (list.itemsSource != index.Indices) { list.itemsSource = index.Indices; list.Rebuild(); }
            else if (ch != ListChange.Unchanged) list.RefreshItems();
            LastFilterMs = sw.Elapsed.TotalMilliseconds;
            facetCounts.Clear();
            ShowFacetRows();
            status.text = L10n.Format("search.results", index.Indices.Count, index.Rows.Count);
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

        public void SelectGroup(string key) { group = key; category = null; ApplyFacets(); }
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
