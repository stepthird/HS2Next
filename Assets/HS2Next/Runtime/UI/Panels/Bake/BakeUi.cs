// Small pieces the panels «Chara to Item» and «Cutter» and their sections build with: a bar of buttons, a button, a switch, a line of text, a size in bytes as text.
using System;
using System.Globalization;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public static class BakeUi
    {
        /// <summary>A row of buttons that wraps.</summary>
        public static VisualElement Bar(string id)
        {
            var b = new VisualElement { name = id };
            b.AddToClassList("hs-bake__bar");
            return b;
        }

        /// <summary>A button with a localised text, added to <paramref name="parent"/> when given.</summary>
        public static Button Btn(VisualElement parent, string id, string key, Action click)
        {
            var b = Loc.Button(key, click);
            b.name = id;
            parent?.Add(b);
            return b;
        }

        /// <summary>A switch with a localised label; <paramref name="changed"/> hears the user's changes only.</summary>
        public static Toggle Switch(VisualElement parent, string id, string key, Action<bool> changed, params object[] args)
        {
            var t = new Toggle { name = id };
            t.AddToClassList("hs-bake__switch");
            Loc.Bind(t, key, (e, s) => ((Toggle)e).label = s, args);
            t.RegisterValueChangedCallback(e => { if (e.target == t) changed(e.newValue); });
            parent?.Add(t);
            return t;
        }

        /// <summary>A line of text (class hs-bake__status unless given).</summary>
        public static Label Text(VisualElement parent, string id, string cls = "hs-bake__status")
        {
            var l = new Label { name = id };
            l.AddToClassList(cls);
            parent?.Add(l);
            return l;
        }

        /// <summary>Shows or hides an element.</summary>
        public static void Show(VisualElement e, bool on)
        {
            if (e != null) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>A size in bytes as text: B, KB, MB or GB (1024 based), as the file dialogs show sizes.</summary>
        public static string Size(long bytes)
        {
            var ci = CultureInfo.InvariantCulture;
            if (bytes < 1024) return bytes.ToString(ci) + " B";
            double v = bytes / 1024.0;
            if (v < 1024) return v.ToString("0.#", ci) + " KB";
            v /= 1024.0;
            if (v < 1024) return v.ToString("0.#", ci) + " MB";
            return (v / 1024.0).ToString("0.##", ci) + " GB";
        }
    }
}
