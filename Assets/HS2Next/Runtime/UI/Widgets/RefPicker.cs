// Pickers for long lists and files: RefPicker = a button with the current choice that
// opens a popup with a search field (P's SearchText normalisation) over a virtualised ListView of (value, label) rows
// (content refs: LUT, probe, sky, BGM, pattern; nodes). FilePicker = the current file name + "…": Browse is the in-app
// browser (package C sets FilePicker.DefaultBrowser; images: panel, background, frame, pattern) and "×" clears.
using System;
using System.Collections.Generic;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Widgets
{
    public sealed class RefPicker : VisualElement
    {
        public readonly Label LabelElement;
        public readonly Button Button;
        /// <summary>The rows offered (called when the popup opens).</summary>
        public Func<IReadOnlyList<(string value, string label)>> Choices;
        public event Action<string> Picked;
        public string Value { get; private set; }
        /// <summary>The overlay layer the popup goes into (UiShell sets it for factory-made pickers).</summary>
        public static VisualElement PopupLayer;

        public RefPicker(string label)
        {
            AddToClassList("hs-ref");
            LabelElement = new Label(label ?? "");
            LabelElement.AddToClassList("hs-ref__label");
            Add(LabelElement);
            Button = new Button(Open) { name = "ref" };
            Button.AddToClassList("hs-ref__button");
            Add(Button);
        }

        public void SetDisplay(string value, string label, bool mixed)
        {
            Value = value;
            Button.text = mixed ? "—" : string.IsNullOrEmpty(label) ? L10n.Get("common.none") : label;
        }

        void Open()
        {
            var layer = PopupLayer ?? panel?.visualTree;
            if (layer == null) return;
            var rows = Choices?.Invoke() ?? Array.Empty<(string, string)>();
            var shown = new List<(string value, string label)>(rows);
            var box = new VisualElement { name = "ref-popup" };
            box.AddToClassList("hs-ref__popup");
            box.style.position = Position.Absolute;
            var wb = Button.worldBound;
            box.style.left = wb.xMin;
            box.style.top = wb.yMax;
            var search = new TextField { name = "ref-search" };
            search.AddToClassList("hs-ref__search");
            // a click on a row picks it directly as well (Phase 3.6: the list's own selection did not follow synthesized clicks in
            // UI2; the row's index is its bound position)
            Label MakeRow()
            {
                var l = new Label();
                l.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0 || !(l.userData is int k) || k < 0 || k >= shown.Count || box.parent == null) return;
                    e.StopPropagation();
                    Choose(box, shown[k].value);
                });
                return l;
            }
            var list = new ListView(shown, 22, MakeRow, (e, i) => { ((Label)e).text = shown[i].label; e.userData = i; }) { name = "ref-list", selectionType = SelectionType.Single };
            list.AddToClassList("hs-ref__list");
            list.itemsChosen += items => { foreach (var o in items) { var r = ((string value, string label))o; Choose(box, r.value); break; } };
            list.selectionChanged += items => { foreach (var o in items) { var r = ((string value, string label))o; Choose(box, r.value); break; } };
            search.RegisterValueChangedCallback(e =>
            {
                var q = SearchQuery.Parse(e.newValue ?? "");
                shown.Clear();
                foreach (var r in rows) if (q.Matches(SearchText.Normalize(r.label ?? ""), null, null) || q.Matches(SearchText.Normalize(r.value ?? ""), null, null)) shown.Add(r);
                list.RefreshItems();
            });
            box.Add(search);
            box.Add(list);
            var blocker = new VisualElement { name = "ref-blocker" };
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
            blocker.RegisterCallback<PointerDownEvent>(e => { blocker.RemoveFromHierarchy(); box.RemoveFromHierarchy(); e.StopPropagation(); });
            box.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { blocker.RemoveFromHierarchy(); box.RemoveFromHierarchy(); e.StopPropagation(); } });
            layer.Add(blocker);
            layer.Add(box);
            // keep the popup inside the layer once laid out (below the button, else above it; Phase 3.6, UI2: a picker near the
            // bottom of the scene panel opened its list below the window)
            box.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float lh = layer.layout.height, lw = layer.layout.width;
                if (float.IsNaN(lh) || lh <= 0) return;
                float top = wb.yMax - layer.worldBound.yMin;
                if (top + box.layout.height > lh) top = Mathf.Max(0, Mathf.Min(wb.yMin - layer.worldBound.yMin - box.layout.height, lh - box.layout.height));
                float left = Mathf.Max(0, Mathf.Min(wb.xMin - layer.worldBound.xMin, lw - box.layout.width));
                if (Mathf.Abs(box.layout.y - top) > 0.5f) box.style.top = top;
                if (Mathf.Abs(box.layout.x - left) > 0.5f) box.style.left = left;
            });
            UiDefer.Frames(2, () => { if (search.panel != null) search.Focus(); });
        }

        void Choose(VisualElement box, string value)
        {
            box.parent?.Q("ref-blocker")?.RemoveFromHierarchy();
            box.RemoveFromHierarchy();
            Picked?.Invoke(value);
        }
    }

    public sealed class FilePicker : VisualElement
    {
        /// <summary>The app's in-app file browser (package C): (kind "image" / "audio" / …, current path, callback with the chosen path or null).</summary>
        public static Action<string, string, Action<string>> DefaultBrowser;

        public readonly Label LabelElement;
        public readonly Label FileLabel;
        public readonly Button BrowseButton, ClearButton;
        public string Kind = "image";
        public string Value { get; private set; }
        public event Action<string> Picked;

        public FilePicker(string label)
        {
            AddToClassList("hs-file");
            LabelElement = new Label(label ?? "");
            LabelElement.AddToClassList("hs-file__label");
            Add(LabelElement);
            FileLabel = new Label { name = "file" };
            FileLabel.AddToClassList("hs-file__name");
            Add(FileLabel);
            BrowseButton = new Button(Browse) { name = "browse", text = "…" };
            ClearButton = new Button(() => Picked?.Invoke(null)) { name = "clear", text = "×" };
            Add(BrowseButton);
            Add(ClearButton);
        }

        public void SetDisplay(string path, bool mixed)
        {
            Value = path;
            FileLabel.text = mixed ? "—" : string.IsNullOrEmpty(path) ? L10n.Get("common.none") : System.IO.Path.GetFileName(path);
            FileLabel.tooltip = path ?? "";
        }

        void Browse()
        {
            if (DefaultBrowser == null) { Debug.LogWarning("[ui] no file browser registered"); return; }
            DefaultBrowser(Kind, Value, p => { if (p != null) Picked?.Invoke(p); });
        }
    }
}
