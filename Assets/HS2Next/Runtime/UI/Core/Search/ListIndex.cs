// The list index of the browsers: a ListView / MultiColumnListView binds
// `itemsSource` = Indices (a List<int> of row positions into the data array, one instance for the index's lifetime);
// filtering (query, facets) and sorting rebuild that list, and Refresh() says whether the view needs RefreshItems()
// at all (minimal refresh: an unchanged result is reported as Unchanged, so typing that does not change the matches
// costs no rebuild of the view). Search keys are computed once per row (SearchText) and again only for rows marked
// changed. Sorts are stable (ties keep the source order). Unity-free.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.UI.Core
{
    /// <summary>What a Refresh changed in <see cref="ListIndex{T}.Indices"/>.</summary>
    public enum ListChange
    {
        /// <summary>Same rows in the same order: no view refresh needed.</summary>
        Unchanged,
        /// <summary>Same set of rows, new order (RefreshItems).</summary>
        Reordered,
        /// <summary>Rows added or removed (RefreshItems; the selection may need re-mapping).</summary>
        Changed,
    }

    /// <summary>Name comparers for list sorts.</summary>
    public static class ListSorts
    {
        static CompareInfo ja;

        /// <summary>Japanese culture order (kana-insensitive width, as HS2's ja-JP name sort); ordinal when the culture is
        /// not available in the runtime (invariant-globalization builds).</summary>
        public static int CompareJa(string a, string b)
        {
            if (ja == null)
            {
                try { ja = CultureInfo.GetCultureInfo("ja-JP").CompareInfo; }
                catch (CultureNotFoundException) { ja = CultureInfo.InvariantCulture.CompareInfo; }
            }
            return ja.Compare(a ?? "", b ?? "", CompareOptions.IgnoreWidth | CompareOptions.IgnoreCase);
        }

        public static int CompareOrdinal(string a, string b) => string.CompareOrdinal(a ?? "", b ?? "");

        public static int CompareOrdinalIgnoreCase(string a, string b) => StringComparer.OrdinalIgnoreCase.Compare(a ?? "", b ?? "");
    }

    public sealed class ListIndex<T>
    {
        sealed class Facet
        {
            public Func<T, string> Value;
            public string Selected;   // null = all
        }

        sealed class Sort
        {
            public Comparison<T> Compare;
        }

        IReadOnlyList<T> rows;
        readonly Func<T, string> text;
        readonly Func<T, string> contentRef;
        string[] keys;
        string[] refGuids, refIds;
        readonly Dictionary<string, Facet> facets = new Dictionary<string, Facet>(StringComparer.Ordinal);
        readonly List<string> facetOrder = new List<string>();
        readonly Dictionary<string, Sort> sorts = new Dictionary<string, Sort>(StringComparer.Ordinal);
        SearchQuery query = SearchQuery.Empty;
        string sortName;
        bool descending;
        int[] scratch = new int[0];
        int[] previous = new int[0];
        int previousCount;
        bool dirty = true;

        /// <summary>The view's items source: row positions into <see cref="Rows"/> after filter and sort.</summary>
        public readonly List<int> Indices = new List<int>();

        public IReadOnlyList<T> Rows => rows;
        public string Query => query.Text;
        public string SortName => sortName;
        public bool Descending => descending;

        /// <param name="rows">The data rows (not copied; call <see cref="SetRows"/> or <see cref="RowsChanged"/> after changes).</param>
        /// <param name="searchText">The text a row is searched by (several fields joined with spaces).</param>
        /// <param name="contentRef">The row's content ref "guid#id" for `guid#id` tokens (null = none).</param>
        public ListIndex(IReadOnlyList<T> rows, Func<T, string> searchText, Func<T, string> contentRef = null)
        {
            text = searchText ?? throw new ArgumentNullException(nameof(searchText));
            this.contentRef = contentRef;
            SetRows(rows);
        }

        public void SetRows(IReadOnlyList<T> newRows)
        {
            rows = newRows ?? new T[0];
            keys = new string[rows.Count];
            refGuids = new string[rows.Count];
            refIds = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++) ComputeKey(i);
            dirty = true;
        }

        void ComputeKey(int i)
        {
            var r = rows[i];
            keys[i] = SearchText.Normalize(text(r));
            if (contentRef != null && SearchQuery.SplitRef(contentRef(r), out var g, out var id))
            {
                refGuids[i] = g;
                refIds[i] = id;
            }
            else refGuids[i] = refIds[i] = null;
        }

        /// <summary>Rows whose content changed in place (their search keys are recomputed).</summary>
        public void RowsChanged(IEnumerable<int> positions)
        {
            foreach (int i in positions)
                if (i >= 0 && i < rows.Count) ComputeKey(i);
            dirty = true;
        }

        public string SearchKey(int row) => keys[row];

        public void AddFacet(string name, Func<T, string> value)
        {
            if (!facets.ContainsKey(name)) facetOrder.Add(name);
            facets[name] = new Facet { Value = value };
            dirty = true;
        }

        /// <summary>Selects a facet value (null or "" = all).</summary>
        public void SetFacet(string name, string value)
        {
            var f = facets[name];
            string v = string.IsNullOrEmpty(value) ? null : value;
            if (f.Selected == v) return;
            f.Selected = v;
            dirty = true;
        }

        public string FacetValue(string name) => facets[name].Selected;

        public void AddSort(string name, Comparison<T> compare)
        {
            sorts[name] = new Sort { Compare = compare };
            dirty = true;
        }

        /// <summary>Sort by a registered sort (null = source order).</summary>
        public void SetSort(string name, bool desc = false)
        {
            if (name != null && !sorts.ContainsKey(name)) throw new ArgumentException("unknown sort " + name);
            if (sortName == name && descending == desc) return;
            sortName = name;
            descending = desc;
            dirty = true;
        }

        /// <summary>Clicking the active sort key flips the direction (vanilla list headers).</summary>
        public void ToggleSort(string name) => SetSort(name, sortName == name ? !descending : false);

        public void SetQuery(string q)
        {
            var p = SearchQuery.Parse(q);
            if (p.Text == query.Text) return;
            query = p;
            dirty = true;
        }

        bool Passes(int i, string skipFacet, string[] skipMore = null)
        {
            foreach (var name in facetOrder)
            {
                if (name == skipFacet || (skipMore != null && Array.IndexOf(skipMore, name) >= 0)) continue;
                var f = facets[name];
                if (f.Selected != null && !string.Equals(f.Value(rows[i]), f.Selected, StringComparison.Ordinal)) return false;
            }
            return query.IsEmpty || query.Matches(keys[i], refGuids[i], refIds[i]);
        }

        /// <summary>Rebuilds <see cref="Indices"/> if anything changed since the last refresh.</summary>
        public ListChange Refresh()
        {
            if (!dirty) return ListChange.Unchanged;
            dirty = false;
            int n = rows.Count;
            if (scratch.Length < n) scratch = new int[n];
            int count = 0;
            for (int i = 0; i < n; i++)
                if (Passes(i, null)) scratch[count++] = i;
            if (sortName != null && count > 1)
            {
                var cmp = sorts[sortName].Compare;
                var rs = rows;
                bool desc = descending;
                Array.Sort(scratch, 0, count, Comparer<int>.Create((a, b) =>
                {
                    int c = cmp(rs[a], rs[b]);
                    if (desc) c = -c;
                    return c != 0 ? c : a.CompareTo(b);   // stable: source order breaks ties (both directions)
                }));
            }
            var change = Classify(count);
            Indices.Clear();
            for (int i = 0; i < count; i++) Indices.Add(scratch[i]);
            if (previous.Length < count) previous = new int[Math.Max(count, previous.Length * 2)];
            Array.Copy(scratch, previous, count);
            previousCount = count;
            return change;
        }

        ListChange Classify(int count)
        {
            if (count == previousCount)
            {
                bool same = true;
                for (int i = 0; i < count; i++) if (previous[i] != scratch[i]) { same = false; break; }
                if (same) return ListChange.Unchanged;
                var a = new HashSet<int>();
                for (int i = 0; i < count; i++) a.Add(previous[i]);
                for (int i = 0; i < count; i++) if (!a.Contains(scratch[i])) return ListChange.Changed;
                return ListChange.Reordered;
            }
            return ListChange.Changed;
        }

        /// <summary>Row counts per value of a facet under the current query and the other facets (the facet's own selection
        /// ignored), for "Group (123)" labels. Key "" counts rows whose value is null. <paramref name="alsoIgnore"/>: facets
        /// nested in this one (a group's categories), whose selection must not zero the other values' counts.</summary>
        public SortedDictionary<string, int> FacetCounts(string name, params string[] alsoIgnore)
        {
            var f = facets[name];
            var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var skip = alsoIgnore != null && alsoIgnore.Length > 0 ? alsoIgnore : null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!Passes(i, name, skip)) continue;
                string v = f.Value(rows[i]) ?? "";
                d[v] = d.TryGetValue(v, out int c) ? c + 1 : 1;
            }
            return d;
        }

        /// <summary>The view position of a row (or -1 when filtered out).</summary>
        public int PositionOf(int row) => Indices.IndexOf(row);
    }
}
