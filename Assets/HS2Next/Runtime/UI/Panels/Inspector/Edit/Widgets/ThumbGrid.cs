// A virtualised thumbnail grid (generalised from the 3.5 CardGrid):
// a ListView whose rows hold N cells (N from the width and the zoom), bound to an index list over entries. Images come from
// an IThumbSource (CardGrid: files through a ThumbnailLoader; the Maker's PartList: list rows through PartThumbnails); a
// cell asks the source on bind and releases on unbind; while it loads and when the source failed it shows no texture but the
// classes `hs-card__image--loading` / `--failed` (W-1, Phase 4.2: the 3.5 placeholder tiles went through UI Toolkit's dynamic
// atlas, and a page of failed cells rebuilt while a character was built hung the D3D12 device; ThumbGridOptions keeps the old
// tiles for diagnostics). A cell that shows no entry keeps no image, and a re-attached grid rebinds its visible cells.
// One focus / selection cell (click, arrows, Home / End, Page Up / Down), Enter or a double click activates;
// optional marks per entry: current (the bound value of a Maker list), busy (an asynchronous bundle open), a badge (mod
// rows); text-only cells when the aspect is 0 (layout pushes); colour cells when Fill gives an entry a colour (the body's
// skin swatches, the image area filled, no image load); no text line when the text height
// is 0. Images are drawn, never inspected. A cell that shows no entry (a column beyond the width, or past the last entry)
// is named `cell-hidden` (no stale `part-<ref>`), and gets its entry's name again when it is reused.
// v0.3 trial 2: every grid shows the same cell states, as classes on the cell (the frame and the check badge are drawn over the
// image, so no state moves the layout; a cell gets them the first time it shows the applied value): `hs-card--hover` under
// the pointer, `hs-card--pressed` while the primary button is
// held on it, `hs-card--selected` the focus / selection cell (an accent ring and a lit text line; the ring is white while the
// grid has the keyboard focus, `hs-thumb-grid--focused`), and `hs-card--current` the value applied to the character (a thick
// accent frame, a check badge and an accent text line). HintKey names a short text of what a click / double click does,
// appended to every cell's tooltip. Keys reach the grid before the list view inside it (which took the arrow keys).
// v1.2: a right click on a cell selects it and, on release, asks for its context menu (ContextRequested, with the pointer's
// panel position); the menu key or Shift+F10 asks for the focus cell's menu (below the cell). Grids without a handler ignore both.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>Where a <see cref="ThumbGrid{T}"/> gets its images.</summary>
    public interface IThumbSource<T>
    {
        /// <summary>The image key of an entry (cells showing the same key share loads), or null when it has no image.</summary>
        string KeyOf(T item);
        /// <summary>A cell (<paramref name="owner"/>) shows the entry; returns the texture when it is loaded (null = loading).</summary>
        Texture2D Request(T item, object owner);
        /// <summary>The cell no longer shows the entry.</summary>
        void Release(T item, object owner);
        /// <summary>The entry's image failed (error tile).</summary>
        bool IsFailed(T item);
        /// <summary>An image of <c>key</c> finished (texture, or null when it failed).</summary>
        event Action<string, Texture2D> Loaded;
    }

    /// <summary>File thumbnails through a 3.5 <see cref="ThumbnailLoader"/> (card, pose, scene and image browsers).</summary>
    public sealed class FileThumbSource<T> : IThumbSource<T>
    {
        readonly ThumbnailLoader loader;
        readonly Func<T, string> path;
        readonly Func<T, bool> hasImage;
        public event Action<string, Texture2D> Loaded;

        public FileThumbSource(ThumbnailLoader loader, Func<T, string> imagePath, Func<T, bool> hasImage = null)
        {
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            path = imagePath;
            this.hasImage = hasImage;
            loader.Loaded += (p, t) => Loaded?.Invoke(p, t);
        }

        public string KeyOf(T item) => hasImage != null && !hasImage(item) ? null : path(item);
        public Texture2D Request(T item, object owner) => loader.Request(path(item), owner);
        public void Release(T item, object owner) => loader.Release(path(item), owner);
        public bool IsFailed(T item) => loader.IsFailed(path(item));
    }

    /// <summary>Options shared by every <see cref="ThumbGrid{T}"/>.</summary>
    public static class ThumbGridOptions
    {
        /// <summary>
        /// False (default): a cell whose image is loading or failed shows no texture — its image area gets the classes
        /// `hs-card__image--loading` / `hs-card__image--failed` (USS colours). True: the 3.5 tiles ThumbnailLoader.Placeholder /
        /// ErrorPlaceholder as background images (W-1 diagnostics only: those 18 × 25 tiles go through UI Toolkit's dynamic
        /// atlas, which every rebuild of a page of failed cells frees and re-fills).
        /// </summary>
        public static bool PlaceholderTextures;
    }

    public class ThumbGrid<T> : VisualElement where T : class
    {
        sealed class Cell
        {
            public VisualElement Root, Image, Frame;
            public Label Text, Badge, Check;
            public int Pos = -1;       // position in the index list
            public string Key;
            public T Item;
        }

        public const float DefaultGap = 6f;
        public const int MaxColumns = 24;
        /// <summary>The element name of a cell that shows no entry.</summary>
        public const string HiddenCellName = "cell-hidden";
        /// <summary>The cell state classes (v0.3 trial 2) and the grid's keyboard-focus class.</summary>
        public const string SelectedClass = "hs-card--selected", CurrentClass = "hs-card--current", HoverClass = "hs-card--hover",
            PressedClass = "hs-card--pressed", FocusedClass = "hs-thumb-grid--focused", FrameClass = "hs-card__frame", CheckClass = "hs-card__check";

        readonly ListView list;
        readonly IThumbSource<T> source;
        readonly Func<T, string> text;
        readonly List<int> rowIdx = new List<int>();
        readonly Dictionary<VisualElement, Cell[]> cellsOf = new Dictionary<VisualElement, Cell[]>();
        readonly Dictionary<string, List<Cell>> byKey = new Dictionary<string, List<Cell>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<T> rows = Array.Empty<T>();
        IReadOnlyList<int> index = Array.Empty<int>();
        int columns = 1;
        float zoom = 1f;
        readonly float baseWidth, aspect, textHeight;

        /// <summary>The selected (focus) position in the index list (-1 = none).</summary>
        public int SelectedPos { get; private set; } = -1;
        public T Selected => SelectedPos >= 0 && SelectedPos < index.Count ? rows[index[SelectedPos]] : null;
        public event Action<T> SelectionChanged, Activated;
        /// <summary>A single click on a cell (after the selection moved to it).</summary>
        public event Action<T> Clicked;
        /// <summary>A right click on a cell (released over it) or the menu key / Shift+F10 on the focus cell: the entry and the
        /// panel position where its context menu opens. The cell is selected first.</summary>
        public event Action<T, Vector2> ContextRequested;
        /// <summary>The pointer moved into a cell (its item) or left one (null); a cell scrolled under a resting pointer raises
        /// nothing. The keyboard's focus cell is SelectionChanged.</summary>
        public event Action<T> HoverChanged;
        Vector2 lastHoverPos = new Vector2(float.NaN, float.NaN);
        /// <summary>Builds a drag payload for a pressed cell (null = no drag).</summary>
        public Func<T, DragPayload> DragPayloadOf;
        /// <summary>The element name of a cell (default "card-&lt;position&gt;").</summary>
        public Func<T, int, string> CellName;
        /// <summary>The tooltip of a cell (default the text).</summary>
        public Func<T, string> Tooltip;
        /// <summary>Marks the cell of the bound value (class hs-card--current).</summary>
        public Func<T, bool> IsCurrent;
        /// <summary>Marks a cell whose pick waits for an asynchronous open (class hs-card--busy).</summary>
        public Func<T, bool> IsBusy;
        /// <summary>A small badge text (mod rows, "not listed"); null = none.</summary>
        public Func<T, string> Badge;
        /// <summary>Extra class of a cell (e.g. the warning look of the "not listed" cell); null = none.</summary>
        public Func<T, string> CellClass;
        /// <summary>Colour cells: the colour the image area is filled with (as stored; the UI draws it as ColorSwatch does), or
        /// null for an image cell. A filled cell loads no image.</summary>
        public Func<T, Color?> Fill;
        /// <summary>The string key of a short text of what a click / double click / Enter does, appended to every cell's tooltip
        /// (null = none).</summary>
        public string HintKey;
        /// <summary>The hint text in the UI language ("" when there is none).</summary>
        public string Hint => string.IsNullOrEmpty(HintKey) ? "" : L10n.Get(HintKey);
        /// <summary>True while the grid (or an element inside it) has the keyboard focus.</summary>
        public bool HasKeyboardFocus => ClassListContains(FocusedClass);
        public float MinZoom = 0.5f, MaxZoom = 1.5f, Gap = DefaultGap;
        public int Columns => columns;
        public ListView List => list;
        public IReadOnlyList<T> Rows => rows;
        public IReadOnlyList<int> Index => index;
        /// <summary>Cells bound since the start (tests: the grid really binds thumbnails).</summary>
        public int BindCount { get; private set; }
        /// <summary>Main-thread time of each row bind (UF3), ms.</summary>
        public readonly List<double> BindMs = new List<double>();
        public IThumbSource<T> Source => source;

        /// <param name="aspect">Image height / width; 0 = text-only cells (no image).</param>
        public ThumbGrid(string name, IThumbSource<T> source, Func<T, string> text, float baseWidth, float aspect, float textHeight)
        {
            this.name = name;
            this.source = source;
            this.text = text;
            this.baseWidth = baseWidth;
            this.aspect = aspect;
            this.textHeight = textHeight;
            AddToClassList("hs-card-grid");
            AddToClassList("hs-thumb-grid");
            focusable = true;
            list = new ListView
            {
                name = name + "-list",
                itemsSource = rowIdx,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                makeItem = MakeRow,
                bindItem = BindRow,
                unbindItem = UnbindRow,
            };
            list.AddToClassList("hs-grow");
            Add(list);
            RegisterCallback<GeometryChangedEvent>(_ => Relayout(false));
            // keys first at the grid (trickle-down): the list view inside took the arrows, so they never moved the focus cell
            RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            // a release, a cancel or a lost capture anywhere in the grid ends the pressed look
            RegisterCallback<PointerUpEvent>(_ => Unpress(pressed), TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(_ => Unpress(pressed), TrickleDown.TrickleDown);
            RegisterCallback<PointerCaptureOutEvent>(_ => Unpress(pressed), TrickleDown.TrickleDown);
            // keyboard focus anywhere in the grid lights the focus cell's frame
            RegisterCallback<FocusInEvent>(_ => AddToClassList(FocusedClass));
            RegisterCallback<FocusOutEvent>(e => { if (!(e.relatedTarget is VisualElement to) || (to != this && !Contains(to))) RemoveFromClassList(FocusedClass); });
            if (source != null) source.Loaded += OnLoaded;
            RegisterCallback<DetachFromPanelEvent>(_ => { foreach (var c in AllCells()) Release(c); L10n.Default.LanguageChanged -= OnLanguage; });
            // re-shown (a cached page, a tab shown again): the cells own their images again before they are drawn (W-1)
            RegisterCallback<AttachToPanelEvent>(_ => { L10n.Default.LanguageChanged -= OnLanguage; L10n.Default.LanguageChanged += OnLanguage; if (cellsOf.Count > 0) list.RefreshItems(); });
            ApplyHeight();
        }

        public float CellWidth => baseWidth * zoom;
        public float ImageHeight => CellWidth * aspect;
        public float CellHeight => ImageHeight + textHeight;
        /// <summary>The height of one grid row (cell + gap).</summary>
        public float RowHeight => CellHeight + Gap;

        void ApplyHeight() => list.fixedItemHeight = RowHeight;

        /// <summary>Zoom factor (<see cref="MinZoom"/>–<see cref="MaxZoom"/>; CardGrid: 252 × 352 shown at 126 × 176 = 1).</summary>
        public float Zoom
        {
            get => zoom;
            set { zoom = Mathf.Clamp(value, MinZoom, MaxZoom); ApplyHeight(); Relayout(true); }
        }

        /// <summary>Shows <paramref name="newIndex"/> (positions into <paramref name="newRows"/>); the selected entry is kept when still shown.</summary>
        public void SetItems(IReadOnlyList<T> newRows, IReadOnlyList<int> newIndex)
        {
            T keep = Selected;
            rows = newRows ?? Array.Empty<T>();
            index = newIndex ?? Array.Empty<int>();
            SelectedPos = -1;
            if (keep != null)
                for (int i = 0; i < index.Count; i++) if (ReferenceEquals(rows[index[i]], keep)) { SelectedPos = i; break; }
            Relayout(true);
        }

        void Relayout(bool force)
        {
            float w = list.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) w = resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) w = 600;
            int cols = ColumnsFor(w);
            if (!force && cols == columns && rowIdx.Count == (index.Count + columns - 1) / columns) return;
            columns = cols;
            int n = (index.Count + columns - 1) / columns;
            rowIdx.Clear();
            for (int i = 0; i < n; i++) rowIdx.Add(i);
            list.RefreshItems();
        }

        /// <summary>The columns the grid shows at a list width (px).</summary>
        public int ColumnsFor(float width) => Mathf.Clamp((int)((width - 16) / (CellWidth + Gap)), 1, MaxColumns);

        IEnumerable<Cell> AllCells()
        {
            foreach (var cs in cellsOf.Values) foreach (var c in cs) yield return c;
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("hs-card-grid__row");
            var cells = new Cell[MaxColumns];
            for (int c = 0; c < cells.Length; c++)
            {
                var cell = new Cell { Root = new VisualElement(), Image = new VisualElement(), Text = new Label(), Badge = new Label() };
                cell.Root.AddToClassList("hs-card");
                cell.Image.AddToClassList("hs-card__image");
                cell.Text.AddToClassList("hs-card__text");
                cell.Badge.AddToClassList("hs-card__badge");
                cell.Badge.pickingMode = PickingMode.Ignore;
                cell.Badge.style.position = Position.Absolute;
                cell.Root.Add(cell.Image);
                cell.Root.Add(cell.Text);
                cell.Root.Add(cell.Badge);
                var cc = cell;
                cell.Root.RegisterCallback<PointerDownEvent>(e => OnCellDown(cc, e));
                cell.Root.RegisterCallback<PointerUpEvent>(e => OnCellUp(cc, e));
                cell.Root.RegisterCallback<PointerEnterEvent>(e =>
                {
                    cc.Root.AddToClassList(HoverClass);
                    // only a pointer that moved: a cell scrolled under a resting pointer is not a hover
                    var pos = (Vector2)e.position;
                    if ((pos - lastHoverPos).sqrMagnitude < 0.25f) return;
                    lastHoverPos = pos;
                    HoverChanged?.Invoke(cc.Pos >= 0 && cc.Pos < index.Count ? rows[index[cc.Pos]] : null);
                });
                cell.Root.RegisterCallback<PointerLeaveEvent>(_ => { cc.Root.RemoveFromClassList(HoverClass); Unpress(cc); HoverChanged?.Invoke(null); });
                DragController.MakeSource(cell.Root, _ => cc.Pos >= 0 && cc.Pos < index.Count && DragPayloadOf != null ? DragPayloadOf(rows[index[cc.Pos]]) : null);
                row.Add(cell.Root);
                cells[c] = cell;
            }
            cellsOf[row] = cells;
            return row;
        }

        void BindRow(VisualElement row, int ri)
        {
            var sw = Stopwatch.StartNew();
            var cells = cellsOf[row];
            for (int c = 0; c < cells.Length; c++)
            {
                var cell = cells[c];
                Release(cell);
                Unpress(cell);   // a reused cell shows another entry
                int pos = ri * columns + c;
                bool used = c < columns && pos < index.Count;
                cell.Root.style.display = c < columns ? DisplayStyle.Flex : DisplayStyle.None;
                cell.Root.style.width = CellWidth;
                cell.Root.style.height = CellHeight;
                cell.Text.style.height = textHeight;
                cell.Text.style.display = textHeight > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                cell.Image.style.height = ImageHeight;
                cell.Image.style.display = aspect > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                cell.Root.style.visibility = used ? Visibility.Visible : Visibility.Hidden;
                cell.Pos = used ? pos : -1;
                cell.Item = used ? rows[index[pos]] : null;
                if (!used) { Hide(cell); continue; }
                var item = cell.Item;
                cell.Root.name = CellName != null ? CellName(item, pos) : "card-" + pos;
                string t = text(item);
                cell.Text.text = t;
                string tip = Tooltip != null ? Tooltip(item) : t;
                string hint = Hint;
                cell.Root.tooltip = hint.Length == 0 ? tip : string.IsNullOrEmpty(tip) ? hint : tip + "\n" + hint;
                string badge = Badge?.Invoke(item);
                cell.Badge.text = badge ?? "";
                cell.Badge.style.display = string.IsNullOrEmpty(badge) ? DisplayStyle.None : DisplayStyle.Flex;
                Mark(cell);
                BindCount++;
                if (aspect <= 0) continue;
                var fill = Fill?.Invoke(item);
                cell.Image.EnableInClassList("hs-card__image--colour", fill.HasValue);
                if (fill.HasValue)
                {
                    // a colour cell: the image area shows the colour, no image is requested
                    cell.Image.style.backgroundImage = StyleKeyword.None;
                    cell.Image.style.backgroundColor = fill.Value;
                    cell.Image.EnableInClassList("hs-card__image--loading", false);
                    cell.Image.EnableInClassList("hs-card__image--failed", false);
                    continue;
                }
                if (Fill != null) cell.Image.style.backgroundColor = StyleKeyword.Null;
                if (source == null) continue;
                string key = source.KeyOf(item);
                if (key == null)
                {
                    cell.Image.style.backgroundImage = StyleKeyword.None;
                    cell.Image.EnableInClassList("hs-card__image--none", true);
                    cell.Image.EnableInClassList("hs-card__image--loading", false);
                    cell.Image.EnableInClassList("hs-card__image--failed", false);
                    continue;
                }
                cell.Image.EnableInClassList("hs-card__image--none", false);
                cell.Key = key;
                if (!byKey.TryGetValue(key, out var l)) byKey[key] = l = new List<Cell>();
                l.Add(cell);
                var tex = source.Request(item, cell);
                SetImage(cell, tex);
            }
            BindMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        void Mark(Cell cell)
        {
            var item = cell.Item;
            bool used = item != null && cell.Pos >= 0;
            cell.Root.EnableInClassList(SelectedClass, used && cell.Pos == SelectedPos);
            bool current = used && IsCurrent != null && IsCurrent(item);
            if (current) EnsureOverlay(cell);
            cell.Root.EnableInClassList(CurrentClass, current);
            cell.Root.EnableInClassList("hs-card--busy", used && IsBusy != null && IsBusy(item));
            string extra = used ? CellClass?.Invoke(item) : null;
            cell.Root.EnableInClassList("hs-card--warning", extra == "warning");
        }

        Cell pressed;

        void Unpress(Cell cell)
        {
            if (cell == null) return;
            cell.Root.RemoveFromClassList(PressedClass);
            if (pressed == cell) pressed = null;
        }

        // the frame and the check badge of the applied value lie over the image and the text (absolute: no state moves the
        // layout); made the first time the cell shows the applied value (most cells never do)
        static void EnsureOverlay(Cell cell)
        {
            if (cell.Frame != null) return;
            cell.Frame = new VisualElement { pickingMode = PickingMode.Ignore };
            cell.Frame.AddToClassList(FrameClass);
            cell.Check = new Label("✓") { pickingMode = PickingMode.Ignore };
            cell.Check.AddToClassList(CheckClass);
            cell.Root.Add(cell.Frame);
            cell.Root.Add(cell.Check);
        }

        /// <summary>Re-evaluates the current / busy / selected marks of the bound cells (the bound value changed).</summary>
        public void RefreshMarks()
        {
            foreach (var c in AllCells()) if (c.Pos >= 0) Mark(c);
        }

        void SetImage(Cell cell, Texture2D t)
        {
            bool failed = t == null && cell.Item != null && source.IsFailed(cell.Item);
            bool tiles = ThumbGridOptions.PlaceholderTextures;
            cell.Image.EnableInClassList("hs-card__image--loading", t == null && !failed && !tiles);
            cell.Image.EnableInClassList("hs-card__image--failed", failed && !tiles);
            if (t != null) cell.Image.style.backgroundImage = new StyleBackground(t);
            else if (!tiles) cell.Image.style.backgroundImage = StyleKeyword.None;   // no texture for loading / failed cells (W-1)
            else cell.Image.style.backgroundImage = new StyleBackground(failed ? ThumbnailLoader.ErrorPlaceholder : ThumbnailLoader.Placeholder);
        }

        void UnbindRow(VisualElement row, int ri)
        {
            foreach (var cell in cellsOf[row]) { Release(cell); cell.Pos = -1; cell.Item = null; Hide(cell); }
        }

        // A cell that shows no entry: named cell-hidden, and it keeps no image — the texture it showed is no longer owned by it
        // and may be unloaded by its source while the cell still exists (W-1)
        static void Hide(Cell cell)
        {
            cell.Root.name = HiddenCellName;
            cell.Root.tooltip = null;
            cell.Root.RemoveFromClassList(PressedClass);
            cell.Image.style.backgroundImage = StyleKeyword.None;
            cell.Image.EnableInClassList("hs-card__image--loading", false);
            cell.Image.EnableInClassList("hs-card__image--failed", false);
        }

        void Release(Cell cell)
        {
            if (cell.Key == null) return;
            if (cell.Item != null) source.Release(cell.Item, cell);
            if (byKey.TryGetValue(cell.Key, out var l)) { l.Remove(cell); if (l.Count == 0) byKey.Remove(cell.Key); }
            cell.Key = null;
        }

        void OnLoaded(string key, Texture2D t)
        {
            if (key == null || !byKey.TryGetValue(key, out var l)) return;
            foreach (var cell in l) SetImage(cell, t);
        }

        void OnCellDown(Cell cell, PointerDownEvent e)
        {
            if (e.button == 1 && cell.Pos >= 0 && ContextRequested != null)
            {
                // a right click selects the cell; its menu opens on the release (a menu opened on the press would take the release)
                Focus();
                if (SelectedPos != cell.Pos) Select(cell.Pos);
                rightPressed = cell;
                e.StopPropagation();
                return;
            }
            if (e.button != 0 || cell.Pos < 0) return;
            Unpress(pressed);
            pressed = cell;
            cell.Root.AddToClassList(PressedClass);
            Focus();
            Select(cell.Pos);
            if (e.clickCount == 2) Activate();
            else if (e.clickCount == 1) { var s = Selected; if (s != null) Clicked?.Invoke(s); }
        }

        Cell rightPressed;

        void OnCellUp(Cell cell, PointerUpEvent e)
        {
            if (e.button != 1) return;
            bool same = rightPressed == cell;
            rightPressed = null;
            if (!same || cell.Pos < 0 || cell.Pos >= index.Count || ContextRequested == null) return;
            e.StopPropagation();
            var item = rows[index[cell.Pos]];
            ContextRequested(item, e.position);
        }

        /// <summary>Asks for the focus cell's context menu as the menu key does (below the cell); false without a focus cell or handler.</summary>
        public bool RequestContextMenu()
        {
            var s = Selected;
            if (s == null || ContextRequested == null) return false;
            var el = CellAt(SelectedPos);
            var wb = el != null ? el.worldBound : worldBound;
            ContextRequested(s, new Vector2(wb.xMin + Mathf.Min(24f, wb.width * 0.5f), el != null ? wb.yMax : wb.yMin + 24f));
            return true;
        }

        /// <summary>Moves the selection to <paramref name="pos"/> (-1 = none) and scrolls it into view.</summary>
        public void Select(int pos)
        {
            if (pos < -1 || pos >= index.Count) return;
            SelectedPos = pos;
            foreach (var c in AllCells()) c.Root.EnableInClassList(SelectedClass, c.Pos >= 0 && c.Pos == SelectedPos);
            if (pos >= 0) ScrollToPos(pos);
            SelectionChanged?.Invoke(Selected);
        }

        /// <summary>Scrolls the grid row of position <paramref name="pos"/> into view.</summary>
        public void ScrollToPos(int pos)
        {
            if (pos < 0 || pos >= index.Count) return;
            list.ScrollToItem(pos / Math.Max(1, columns));
        }

        /// <summary>The position of an entry in the index list (-1 when not shown).</summary>
        public int PositionOf(Func<T, bool> match)
        {
            for (int i = 0; i < index.Count; i++) if (match(rows[index[i]])) return i;
            return -1;
        }

        public void Activate()
        {
            var s = Selected;
            if (s != null) Activated?.Invoke(s);
        }

        /// <summary>Visible grid rows (Page Up / Down step).</summary>
        int PageRows
        {
            get
            {
                float h = list.resolvedStyle.height;
                return float.IsNaN(h) || h <= 0 ? 3 : Math.Max(1, (int)(h / Math.Max(1f, RowHeight)));
            }
        }

        void OnKey(KeyDownEvent e)
        {
            int p = SelectedPos, last = index.Count - 1;
            if (last < 0) return;
            if (e.keyCode == KeyCode.Menu || (e.keyCode == KeyCode.F10 && e.shiftKey && !e.ctrlKey && !e.altKey))
            {
                if (RequestContextMenu()) e.StopPropagation();
                return;
            }
            switch (e.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter: Activate(); break;
                case KeyCode.RightArrow: Select(Math.Min(last, p + 1)); break;
                case KeyCode.LeftArrow: Select(Math.Max(0, p - 1)); break;
                case KeyCode.DownArrow: Select(Math.Min(last, p < 0 ? 0 : p + columns)); break;
                case KeyCode.UpArrow: Select(Math.Max(0, p - columns)); break;
                case KeyCode.Home: Select(0); break;
                case KeyCode.End: Select(last); break;
                case KeyCode.PageDown: Select(Math.Min(last, Math.Max(0, p) + PageRows * columns)); break;
                case KeyCode.PageUp: Select(Math.Max(0, p - PageRows * columns)); break;
                default: return;
            }
            e.StopPropagation();
        }

        /// <summary>Re-binds the visible cells (names or marks changed).</summary>
        public void RefreshVisible() => list.RefreshItems();

        /// <summary>The cell element bound to position <paramref name="pos"/> (null when it is not bound: scrolled away).</summary>
        public VisualElement CellAt(int pos)
        {
            foreach (var c in AllCells()) if (c.Pos == pos && c.Item != null) return c.Root;
            return null;
        }

        // the hint follows the UI language (cells re-bound while the grid is shown)
        void OnLanguage(string _) { if (panel != null && !string.IsNullOrEmpty(HintKey)) list.RefreshItems(); }
    }
}
