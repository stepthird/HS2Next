// Package C's access to the shell (package A): popup menus, chord texts, focus, panel activation, viewport picking and
// the held-key state of the hs2studio click modifiers (P / O / X). One place, so the panels do not depend on the shell's
// internals; tests can force the held keys (TestHeldKeys) because synthetic events do not reach Input.GetKey.
using System;
using System.Collections.Generic;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public static class CInput
    {
        /// <summary>Tests: key names treated as held (null = ask Input).</summary>
        public static HashSet<string> TestHeldKeys;

        static bool KeyDown(string key)
        {
            if (TestHeldKeys != null) return TestHeldKeys.Contains(key);
            if (!Enum.TryParse(key, out KeyCode kc)) return false;
            try { return UnityEngine.Input.GetKey(kc); } catch (Exception) { return false; }
        }

        static KeyMods HeldMods()
        {
            var m = KeyMods.None;
            if (KeyDown("LeftControl") || KeyDown("RightControl")) m |= KeyMods.Ctrl;
            if (KeyDown("LeftShift") || KeyDown("RightShift")) m |= KeyMods.Shift;
            if (KeyDown("LeftAlt") || KeyDown("RightAlt")) m |= KeyMods.Alt;
            return m;
        }

        /// <summary>True when a chord of the Modifier action <paramref name="actionId"/> is held now (exact modifiers).</summary>
        public static bool ModifierHeld(UiContext ctx, string actionId)
        {
            var map = ctx.Keys?.Map;
            if (map == null) return false;
            var mods = HeldMods();
            foreach (var c in map.ChordsOf(actionId))
            {
                var s = c.First;
                if (s.Key == null) continue;
                if (s.Mods != mods) continue;
                if (KeyNames.ModifierKeys.Contains(s.Key) || KeyDown(s.Key)) return true;
            }
            return false;
        }

        public static void ShowMenu(UiContext ctx, List<MenuItem> items, Vector2 at, string name)
        {
            if (ctx.Shell?.Menus == null) return;
            ctx.Shell.Menus.Show(items, null, at, name);
        }

        public static string ChordText(UiContext ctx, string actionId) => ctx.Keys?.ChordText(actionId) ?? "";

        /// <summary>True when keyboard focus is inside <paramref name="el"/>.</summary>
        public static bool FocusInside(UiContext ctx, VisualElement el)
        {
            if (el == null) return false;
            var f = el.panel?.focusController?.focusedElement as VisualElement;
            for (var e = f; e != null; e = e.hierarchy.parent) if (e == el) return true;
            return false;
        }

        /// <summary>Shows and focuses a panel tab (its region expanded).</summary>
        public static void ShowPanel(UiContext ctx, string panelId) => ctx.Shell?.FocusPanel(panelId);

        /// <summary>The node under a panel point of the viewport and the surface point hit (map or object), or nulls.</summary>
        public static string PickNode(UiContext ctx, Vector2 panelPoint, out Vector3? hit)
        {
            hit = null;
            var vp = ctx.Viewport;
            if (vp?.Picker == null) return null;
            try
            {
                var h = vp.Picker.Pick(vp, panelPoint);
                if (h.NodeId != null) { hit = h.Point; return h.NodeId; }
                // no node: the map surface through its colliders (maps keep their mesh colliders), else nothing (camera pivot)
                var ray = vp.RayAt(panelPoint);
                if (Physics.Raycast(ray, out var rh, 10000f, ~0, QueryTriggerInteraction.Ignore)) hit = rh.point;
                return null;
            }
            catch (Exception e) { Debug.LogWarning("[ui] pick failed: " + e.Message); return null; }
        }

        /// <summary>The key name (legacy Input KeyCode semantics, the chord format) of a UI Toolkit key event.</summary>
        public static string KeyName(UiContext ctx, KeyDownEvent e) =>
            e.keyCode == KeyCode.None ? null : KeyNames.FromUiToolkit(e.keyCode.ToString(), ctx.Keys?.Layout ?? KeyboardLayout.Us);
    }
}
