// Toasts: bottom-right, 4 s, a stack of at most 3 (the oldest leaves first), for
// results HS2 showed as notifications (save done, load failed) and for refused operations (HS2Next says why). Click
// dismisses. Kinds Info / Success / Warning / Error (class hs-toast--<kind>). Every toast is also logged.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Widgets
{
    public enum ToastKind { Info, Success, Warning, Error }

    public sealed class Toasts
    {
        public const int MaxVisible = 3;
        public const float DefaultSeconds = 4f;
        readonly VisualElement stack;
        readonly List<(VisualElement el, float until)> live = new List<(VisualElement, float)>();
        /// <summary>The texts shown so far (newest last; at most 50), for tests and `ui.state`.</summary>
        public readonly List<string> History = new List<string>();

        public Toasts(VisualElement stackElement)
        {
            stack = stackElement;
            stack.AddToClassList("hs-toast-stack");
            stack.pickingMode = PickingMode.Ignore;
        }

        public VisualElement Show(string text, ToastKind kind = ToastKind.Info, float seconds = DefaultSeconds)
        {
            if (string.IsNullOrEmpty(text)) return null;
            History.Add(kind + ": " + text);
            if (History.Count > 50) History.RemoveAt(0);
            if (kind == ToastKind.Error) Debug.LogWarning("[ui] toast: " + text); else Debug.Log("[ui] toast: " + text);
            var el = new Label(text) { name = "toast" };
            el.AddToClassList("hs-toast");
            el.AddToClassList("hs-toast--" + kind.ToString().ToLowerInvariant());
            el.pickingMode = PickingMode.Position;
            el.RegisterCallback<PointerDownEvent>(_ => Remove(el));
            stack.Add(el);
            live.Add((el, Time.realtimeSinceStartup + Mathf.Max(0.5f, seconds)));
            while (live.Count > MaxVisible) Remove(live[0].el);
            return el;
        }

        void Remove(VisualElement el)
        {
            for (int i = 0; i < live.Count; i++) if (live[i].el == el) { live.RemoveAt(i); break; }
            el.RemoveFromHierarchy();
        }

        /// <summary>Expires old toasts (UiHost.Update).</summary>
        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = live.Count - 1; i >= 0; i--)
                if (live[i].until <= now) Remove(live[i].el);
        }

        public int Count => live.Count;
    }

    /// <summary>Tooltips: an element's `tooltip` shown after 500 ms of hover near the pointer (runtime panels have no
    /// built-in tooltip display). One shared label in the overlay layer.</summary>
    public sealed class Tooltips
    {
        public const float DelaySeconds = 0.5f;
        readonly VisualElement root;
        readonly Label label;
        VisualElement hovered;
        float since = -1;
        Vector2 pos;

        public Tooltips(VisualElement root, VisualElement overlay)
        {
            this.root = root;
            label = new Label { name = "tooltip", pickingMode = PickingMode.Ignore };
            label.AddToClassList("hs-tooltip");
            label.style.display = DisplayStyle.None;
            overlay.Add(label);
            root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            root.RegisterCallback<PointerDownEvent>(_ => Hide(), TrickleDown.TrickleDown);
        }

        void OnMove(PointerMoveEvent e)
        {
            pos = e.position;
            VisualElement t = null;
            for (var v = e.target as VisualElement; v != null; v = v.hierarchy.parent)
                if (!string.IsNullOrEmpty(v.tooltip)) { t = v; break; }
            if (t != hovered) { hovered = t; since = t != null ? Time.realtimeSinceStartup : -1; label.style.display = DisplayStyle.None; }
        }

        void Hide() { hovered = null; since = -1; label.style.display = DisplayStyle.None; }

        public void Tick()
        {
            if (hovered == null || since < 0) return;
            if (hovered.panel == null) { Hide(); return; }
            if (label.style.display == DisplayStyle.None && Time.realtimeSinceStartup - since >= DelaySeconds)
            {
                label.text = hovered.tooltip;
                label.style.left = Mathf.Min(pos.x + 12, root.layout.width - 320);
                label.style.top = Mathf.Min(pos.y + 18, root.layout.height - 40);
                label.style.display = DisplayStyle.Flex;
                label.BringToFront();
            }
        }
    }
}
