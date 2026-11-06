// The slot list of the Accessories page (D3, XQ3, XQ21): one row per worn
// accessory in build-slot order — the slot badge «NN» (slot + 1, two digits: the number HS2 and MoreAccessories show), the
// item's thumbnail (the shared PartThumbnails, guard-aware), the item name (or «一覧に無い» / the missing mod's GUID), a hair
// mark on hair-type items and the «MoreAccessories off» badge on slots ≥ 20 that are not loaded — then the «＋» row that opens
// the page's add panel. A click or the ↑ / ↓ keys select a row (AccessorySelectorState: the selected accessory id per
// character, so a slot-table change keeps the selection on the same accessory; view state, kept while the app runs, no
// history). Rows are cached by accessory id and reordered when the slot table changes. AccessorySlotPicker is the slot chooser
// of the add panel (free slots) and of the copy section (every slot to the highest used + 10, occupied ones marked).
// Element names: rows `acc-slot-<NN>`, the «＋» row `acc-slot-add`; a picker `<name>` with buttons `<name>-<NN>`.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.UI.Core;
using HS2Next.UI.Panels.Maker;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>The selected accessory of the Accessories page per character (view state: kept while the app runs, not stored, no history).</summary>
    public static class AccessorySelectorState
    {
        static readonly Dictionary<string, string> selected = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The selected accessory id of character <paramref name="charaId"/>, or null.</summary>
        public static string Get(string charaId) => charaId != null && selected.TryGetValue(charaId, out var a) ? a : null;

        public static void Set(string charaId, string accessoryId)
        {
            if (charaId == null) return;
            if (accessoryId == null) selected.Remove(charaId);
            else selected[charaId] = accessoryId;
        }
    }

    /// <summary>One worn slot as the slot list shows it.</summary>
    public sealed class AccessorySlotInfo
    {
        /// <summary>The build slot (0-based; shown as slot + 1).</summary>
        public int Slot;
        public string Id;
        public int Category;
        public string Item;
        /// <summary>The item's name, «一覧に無い: …» or the missing mod's text.</summary>
        public string Name;
        public bool HairType, NotLoaded, NotListed;
        /// <summary>The list row key of the thumbnail (null: no thumbnail).</summary>
        public ContentId? Thumb;
        public override string ToString() => InspectorSchemas.AccessorySlotLabel(Slot) + " " + Id + " " + Name;
    }

    public sealed class AccessorySlotList : VisualElement
    {
        public const string UssClass = "hs-acc-slots", RowClass = "hs-acc-slot", SelectedClass = "hs-acc-slot--selected";

        sealed class Row
        {
            public VisualElement Root;
            public Label Badge, Name, Hair, NotLoaded;
            public VisualElement Thumb;
            public AccessorySlotInfo Info;
            public (int cat, ContentId key)? Requested;
        }

        readonly Dictionary<string, Row> rows = new Dictionary<string, Row>(StringComparer.Ordinal);
        readonly List<AccessorySlotInfo> slots = new List<AccessorySlotInfo>();
        readonly VisualElement body;
        readonly PartThumbnails thumbs;
        string selectedId;

        /// <summary>The «＋» row.</summary>
        public VisualElement AddRow { get; }
        /// <summary>Raised with the accessory id when a row is selected by a click or a key.</summary>
        public event Action<string> SelectedChanged;
        /// <summary>Raised when the «＋» row is clicked.</summary>
        public event Action AddClicked;
        /// <summary>The worn slots shown, in slot order.</summary>
        public IReadOnlyList<AccessorySlotInfo> Slots => slots;
        public string SelectedId => selectedId;
        /// <summary>Rebuilds of the row order and the time of the last one (ms; XU4).</summary>
        public int Rebuilds { get; private set; }
        public double LastRebuildMs { get; private set; }

        public AccessorySlotList(PartThumbnails thumbnails)
        {
            name = "acc-slots";
            AddToClassList(UssClass);
            focusable = true;
            thumbs = thumbnails;
            body = new VisualElement { name = "acc-slot-rows" };
            Add(body);
            AddRow = new VisualElement { name = "acc-slot-add" };
            AddRow.AddToClassList(RowClass);
            AddRow.AddToClassList("hs-acc-slot--add");
            var al = new Label(L10n.Get("maker.acc.add"));
            Loc.Bind(al, "maker.acc.add");
            al.AddToClassList("hs-acc-slot__name");
            AddRow.Add(al);
            AddRow.RegisterCallback<ClickEvent>(_ => AddClicked?.Invoke());
            Add(AddRow);
            RegisterCallback<KeyDownEvent>(OnKey);
            // the shared thumbnails are listened to only while the list is on a panel
            RegisterCallback<DetachFromPanelEvent>(_ => { ReleaseAll(); if (thumbs != null) thumbs.LoadedColumn -= OnThumb; });
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (thumbs != null) { thumbs.LoadedColumn -= OnThumb; thumbs.LoadedColumn += OnThumb; }
                foreach (var r in rows.Values) RequestThumb(r);
            });
            schedule.Execute(() => { if (panel != null && thumbs != null && thumbs.Pending > 0) thumbs.UpdateOncePerFrame(); }).Every(16);
        }

        /// <summary>Shows <paramref name="worn"/> (slot order) with <paramref name="selected"/> marked; rows are reused by id.</summary>
        public void SetSlots(IReadOnlyList<AccessorySlotInfo> worn, string selected)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            slots.Clear();
            if (worn != null) slots.AddRange(worn);
            var keep = new HashSet<string>(StringComparer.Ordinal);
            body.Clear();
            foreach (var s in slots)
            {
                if (s?.Id == null) continue;
                keep.Add(s.Id);
                if (!rows.TryGetValue(s.Id, out var r)) rows[s.Id] = r = NewRow(s.Id);
                Fill(r, s);
                body.Add(r.Root);
            }
            foreach (var id in new List<string>(rows.Keys))
                if (!keep.Contains(id)) { Release(rows[id]); rows.Remove(id); }
            selectedId = selected;
            Mark();
            Rebuilds++;
            LastRebuildMs = sw.Elapsed.TotalMilliseconds;
        }

        Row NewRow(string id)
        {
            var r = new Row { Root = new VisualElement() };
            r.Root.AddToClassList(RowClass);
            r.Badge = new Label { name = "badge" };
            r.Badge.AddToClassList("hs-acc-slot__badge");
            r.Thumb = new VisualElement { name = "thumb" };
            r.Thumb.AddToClassList("hs-acc-slot__thumb");
            r.Name = new Label { name = "name" };
            r.Name.AddToClassList("hs-acc-slot__name");
            r.Hair = new Label(L10n.Get("maker.acc.hairMark")) { name = "hair" };
            r.Hair.AddToClassList("hs-acc-slot__mark");
            Loc.Tooltip(r.Hair, "maker.acc.hairMarkTip");
            r.NotLoaded = new Label(L10n.Get("maker.acc.notLoaded")) { name = "notLoaded" };
            r.NotLoaded.AddToClassList("hs-acc-slot__mark");
            r.NotLoaded.AddToClassList("hs-acc-slot__mark--warning");
            Loc.Tooltip(r.NotLoaded, "maker.acc.notLoadedTip");
            r.Root.Add(r.Badge);
            r.Root.Add(r.Thumb);
            r.Root.Add(r.Name);
            r.Root.Add(r.Hair);
            r.Root.Add(r.NotLoaded);
            r.Root.RegisterCallback<ClickEvent>(_ => { Select(id); Focus(); });
            return r;
        }

        void Fill(Row r, AccessorySlotInfo s)
        {
            r.Info = s;
            r.Root.name = "acc-slot-" + InspectorSchemas.AccessorySlotLabel(s.Slot);
            r.Badge.text = InspectorSchemas.AccessorySlotLabel(s.Slot);
            r.Name.text = s.Name ?? s.Item ?? s.Id;
            r.Name.tooltip = s.Item;
            r.Hair.style.display = s.HairType ? DisplayStyle.Flex : DisplayStyle.None;
            r.NotLoaded.style.display = s.NotLoaded ? DisplayStyle.Flex : DisplayStyle.None;
            RequestThumb(r);
        }

        void RequestThumb(Row r)
        {
            var want = r.Info?.Thumb != null ? (r.Info.Category, r.Info.Thumb.Value) : ((int, ContentId)?)null;
            if (r.Requested.HasValue && (!want.HasValue || !r.Requested.Value.Equals(want.Value))) Release(r);
            if (thumbs == null || !want.HasValue || panel == null && r.Root.panel == null) { ShowThumb(r, null); return; }
            if (!r.Requested.HasValue) r.Requested = want;
            ShowThumb(r, thumbs.Request(want.Value.Item1, want.Value.Item2, this));
        }

        static void ShowThumb(Row r, Texture2D t)
        {
            r.Thumb.style.backgroundImage = t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }

        void OnThumb(int cat, ContentId key, string column, Texture2D t)
        {
            if (column != PartThumbnails.ThumbColumn) return;
            foreach (var r in rows.Values)
                if (r.Requested.HasValue && r.Requested.Value.cat == cat && r.Requested.Value.key.Equals(key)) ShowThumb(r, t);
        }

        void Release(Row r)
        {
            if (!r.Requested.HasValue || thumbs == null) { r.Requested = null; return; }
            thumbs.Release(r.Requested.Value.cat, r.Requested.Value.key, this);
            r.Requested = null;
            ShowThumb(r, null);
        }

        void ReleaseAll() { foreach (var r in rows.Values) Release(r); }

        void Mark()
        {
            foreach (var kv in rows) kv.Value.Root.EnableInClassList(SelectedClass, kv.Key == selectedId);
        }

        /// <summary>Selects the row of accessory <paramref name="id"/> as a click does (raises SelectedChanged).</summary>
        public void Select(string id)
        {
            if (id == null || !rows.ContainsKey(id)) return;
            bool changed = id != selectedId;
            selectedId = id;
            Mark();
            if (changed) SelectedChanged?.Invoke(id);
        }

        /// <summary>The row element of accessory <paramref name="id"/> (null when not shown).</summary>
        public VisualElement RowOf(string id) => id != null && rows.TryGetValue(id, out var r) && r.Root.parent == body ? r.Root : null;

        void OnKey(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.UpArrow && e.keyCode != KeyCode.DownArrow) return;
            int i = slots.FindIndex(s => s.Id == selectedId);
            int j = e.keyCode == KeyCode.UpArrow ? Math.Max(0, i - 1) : Math.Min(slots.Count - 1, i + 1);
            if (j >= 0 && j < slots.Count) Select(slots[j].Id);
            e.StopPropagation();
        }
    }

    /// <summary>
    /// A slot chooser: wrapping buttons «NN» for the offered slots; occupied slots are
    /// marked (`hs-acc-picker__slot--occupied`, tooltip = the worn item), the chosen one selected. Buttons `&lt;name&gt;-&lt;NN&gt;`.
    /// </summary>
    public sealed class AccessorySlotPicker : VisualElement
    {
        public const string UssClass = "hs-acc-picker", SlotClass = "hs-acc-picker__slot", OccupiedClass = "hs-acc-picker__slot--occupied",
            SelectedClass = "hs-acc-picker__slot--selected";

        readonly string prefix;
        readonly Dictionary<int, Button> buttons = new Dictionary<int, Button>();
        readonly Dictionary<int, string> occupied = new Dictionary<int, string>();

        /// <summary>The chosen slot (0-based), or -1.</summary>
        public int Selected { get; private set; } = -1;
        /// <summary>Raised when the user chooses a slot.</summary>
        public event Action<int> Changed;
        /// <summary>The slots offered (0-based, ascending).</summary>
        public IEnumerable<int> Offered => buttons.Keys;

        public AccessorySlotPicker(string elementName, string labelKey)
        {
            prefix = elementName;
            name = elementName;
            AddToClassList(UssClass);
            if (labelKey != null)
            {
                var l = new Label(L10n.Get(labelKey));
                Loc.Bind(l, labelKey);
                l.AddToClassList("hs-acc-picker__label");
                Add(l);
            }
        }

        /// <summary>Offers <paramref name="slots"/> (0-based); <paramref name="occupiedBy"/>: the worn item's name per occupied slot.
        /// The selection stays when still offered, else <paramref name="fallback"/> (or -1).</summary>
        public void SetSlots(IEnumerable<int> slots, IReadOnlyDictionary<int, string> occupiedBy, int fallback)
        {
            foreach (var b in buttons.Values) b.RemoveFromHierarchy();
            buttons.Clear();
            occupied.Clear();
            if (occupiedBy != null) foreach (var kv in occupiedBy) occupied[kv.Key] = kv.Value;
            foreach (int s in slots)
            {
                if (buttons.ContainsKey(s)) continue;
                int slot = s;
                string label = InspectorSchemas.AccessorySlotLabel(slot);
                var b = new Button(() => Choose(slot)) { name = prefix + "-" + label, text = label };
                b.AddToClassList(SlotClass);
                if (occupied.TryGetValue(slot, out var item)) { b.AddToClassList(OccupiedClass); b.tooltip = label + " " + item; }
                buttons[slot] = b;
                Add(b);
            }
            Selected = buttons.ContainsKey(Selected) ? Selected : buttons.ContainsKey(fallback) ? fallback : -1;
            Mark();
        }

        /// <summary>Chooses slot <paramref name="slot"/> as its button does (tests).</summary>
        public bool Choose(int slot)
        {
            if (!buttons.ContainsKey(slot)) return false;
            Selected = slot;
            Mark();
            Changed?.Invoke(slot);
            return true;
        }

        public bool IsOccupied(int slot) => occupied.ContainsKey(slot);

        void Mark() { foreach (var kv in buttons) kv.Value.EnableInClassList(SelectedClass, kv.Key == Selected); }
    }
}
