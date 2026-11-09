// «Chara to Item» in the Studio UI: the panel "bake" (default region bottom) and the action bake.panel (View menu, unbound), which opens the panel's region with the panel shown, or
// collapses it when it is the shown panel. The panel appears only when asked for (LazyPanel). The style sheet serves both panels of this feature and the sections.
using System;
using System.Collections.Generic;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class BakeUiModule : IUiModule
    {
        public const string PanelId = "bake";
        public const string TitleKey = "panel.bake";
        public const string StyleResource = "UI/Bake/bake";

        public int Order => 49;

        static readonly LazyPanel lazy = new LazyPanel(PanelId, TitleKey, c => new BakePanel(c));
        static StyleSheet sheet;

        // the name of the last bake made from a character in this session, by the character's node id
        static readonly Dictionary<string, string> LastBake = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The name of the last bake made from <paramref name="character"/> in this session through the panel or the section, or null.</summary>
        public static string LastBakeOf(string character) => character != null && LastBake.TryGetValue(character, out var n) ? n : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lazy.Reset();
            LastBake.Clear();
        }

        public void Register(UiContext ctx) => lazy.Register(ctx, ActionIds.BakePanel);

        /// <summary>The style sheet of the panels and the sections, on <paramref name="el"/>.</summary>
        internal static void Style(VisualElement el)
        {
            sheet ??= Resources.Load<StyleSheet>(StyleResource);
            if (sheet != null && !el.styleSheets.Contains(sheet)) el.styleSheets.Add(sheet);
        }

        /// <summary>Registers the panel when it is not registered yet.</summary>
        public static void EnsurePanel(UiContext ctx) => lazy.Ensure(ctx);

        /// <summary>The panel is the shown tab of an open region.</summary>
        public static bool PanelShown(UiContext ctx) => lazy.Shown(ctx);

        /// <summary>bake.panel: shows the panel, or collapses the region when the panel is its shown tab. True when the panel is shown afterwards.</summary>
        public static bool TogglePanel(UiContext ctx) => lazy.Toggle(ctx);

        /// <summary>Shows the panel and returns it (null without a shell).</summary>
        public static BakePanel Show(UiContext ctx) => lazy.Show(ctx)?.Q<BakePanel>();

        /// <summary>Shows the panel with <paramref name="characters"/> as its target (the State tab's «Bake parts…»).</summary>
        public static BakePanel ShowWith(UiContext ctx, IReadOnlyList<string> characters)
        {
            var p = Show(ctx);
            p?.UseTargets(characters);
            return p;
        }

        /// <summary>Notes the bakes just made from <paramref name="characters"/> (the State tab's section shows the last): with one character every bake of the call,
        /// with several the bakes whose name holds the character's name.</summary>
        internal static void NoteBakes(UiContext ctx, IReadOnlyList<string> characters, HS2Next.Scenes.Runtime.BakeMakeResult made)
        {
            if (made == null || characters == null) return;
            foreach (string c in characters)
            {
                string label = ctx.Model.Find(c)?.Label ?? "";
                var names = new List<string>();
                foreach (var b in made.Bakes)
                    if (characters.Count == 1 || (label.Length > 0 && (b.Name ?? "").IndexOf(label, StringComparison.Ordinal) >= 0)) names.Add(b.Name);
                if (names.Count > 0) LastBake[c] = names.Count == 1 ? names[0] : names[names.Count - 1] + " (+" + (names.Count - 1) + ")";
            }
        }
    }
}
