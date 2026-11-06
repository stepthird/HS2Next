// Popup menus (toolbar menus, context menus, the panel title bar's "Move to …"): an absolute list in the overlay layer
// with check marks, shortcut texts, disabled rows, separators and one level of submenus. Closes on an item click, a
// click outside, Esc (the shortcut router closes it first) or when another menu opens. Items are elements named
// "menu-item-<id>" so that ui.query / ui.input can drive them.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Widgets
{
    public sealed class MenuItem
    {
        public string Id;
        public string Text;
        public string Shortcut;
        public bool Checked, Disabled, Separator;
        public Action Run;
        public List<MenuItem> Submenu;

        public static MenuItem Sep() => new MenuItem { Separator = true };
    }

    public sealed class PopupMenus
    {
        readonly VisualElement overlay;
        readonly VisualElement blocker;
        readonly List<VisualElement> open = new List<VisualElement>();
        public bool IsOpen => open.Count > 0;
        public event Action Closed;

        public PopupMenus(VisualElement overlayLayer)
        {
            overlay = overlayLayer;
            blocker = new VisualElement { name = "menu-blocker" };
            blocker.AddToClassList("hs-menu-blocker");
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
            blocker.style.display = DisplayStyle.None;
            blocker.RegisterCallback<PointerDownEvent>(e => { CloseAll(); e.StopPropagation(); });
            overlay.Add(blocker);
        }

        /// <summary>Shows a menu below <paramref name="anchor"/> (or at <paramref name="at"/>, panel coordinates).</summary>
        public VisualElement Show(IReadOnlyList<MenuItem> items, VisualElement anchor = null, Vector2? at = null, string name = "menu")
        {
            CloseAll();
            blocker.style.display = DisplayStyle.Flex;
            blocker.BringToFront();
            Vector2 p = at ?? (anchor != null ? new Vector2(anchor.worldBound.xMin, anchor.worldBound.yMax) : Vector2.zero);
            return Open(items, p, name, 0);
        }

        VisualElement Open(IReadOnlyList<MenuItem> items, Vector2 p, string name, int depth)
        {
            while (open.Count > depth) { open[open.Count - 1].RemoveFromHierarchy(); open.RemoveAt(open.Count - 1); }
            var box = new VisualElement { name = name };
            box.AddToClassList("hs-menu");
            box.style.position = Position.Absolute;
            box.style.left = p.x;
            box.style.top = p.y;
            foreach (var it in items)
            {
                if (it.Separator) { var s = new VisualElement(); s.AddToClassList("hs-menu__separator"); box.Add(s); continue; }
                var row = new VisualElement { name = "menu-item-" + (it.Id ?? it.Text) };
                row.AddToClassList("hs-menu__item");
                if (it.Disabled) row.AddToClassList("hs-menu__item--disabled");
                var check = new Label(it.Checked ? "✓" : "");
                check.AddToClassList("hs-menu__check");
                var text = new Label(it.Text);
                text.AddToClassList("hs-menu__text");
                var sc = new Label(it.Submenu != null ? "▸" : it.Shortcut ?? "");
                sc.AddToClassList("hs-menu__shortcut");
                row.Add(check); row.Add(text); row.Add(sc);
                var item = it;
                int d = depth;
                row.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (item.Submenu != null && !item.Disabled)
                        Open(item.Submenu, new Vector2(row.worldBound.xMax, row.worldBound.yMin), name + "-sub", d + 1);
                    else while (open.Count > d + 1) { open[open.Count - 1].RemoveFromHierarchy(); open.RemoveAt(open.Count - 1); }
                });
                row.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (item.Disabled || item.Submenu != null) return;
                    e.StopPropagation();
                    CloseAll();
                    try { item.Run?.Invoke(); }
                    catch (Exception ex) { Debug.LogError("[ui] menu item " + item.Id + " threw: " + ex); }
                });
                box.Add(row);
            }
            overlay.Add(box);
            open.Add(box);
            // keep inside the root once laid out
            box.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var root = overlay;
                float maxX = root.layout.width - box.layout.width - 2, maxY = root.layout.height - box.layout.height - 2;
                if (box.layout.x > maxX) box.style.left = Mathf.Max(0, maxX);
                if (box.layout.y > maxY) box.style.top = Mathf.Max(0, maxY);
            });
            return box;
        }

        /// <summary>While a menu is open: false lets the pointer through to what stands under it (a popup that picks in the view).</summary>
        public void SetBlocking(bool on)
        {
            if (open.Count > 0) blocker.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void CloseAll()
        {
            bool was = open.Count > 0;
            foreach (var b in open) b.RemoveFromHierarchy();
            open.Clear();
            blocker.style.display = DisplayStyle.None;
            if (was) UiContext.Guard(() => Closed?.Invoke());
        }
    }
}
