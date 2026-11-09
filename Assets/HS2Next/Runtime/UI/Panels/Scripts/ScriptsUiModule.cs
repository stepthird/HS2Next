// «Scene scripts» in the Studio UI: the panel "scripts" (default region bottom, beside the Timeline panel) and the action scripts.panel (View menu, unbound), which opens the
// panel's region with the panel shown, or collapses it when it is the shown panel. As the Constraints, Wet and stains, Ragdoll and Chara to Item panels, the panel appears only
// when asked for (LazyPanel): it is registered at start only when layout.json already lists it, otherwise by its first action, so that the bottom region stays collapsed until the
// user opens it. The style sheet serves the panel, its dialogs and the script line of the Timeline panel's header.
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ScriptsUiModule : IUiModule
    {
        public const string PanelId = "scripts";
        public const string TitleKey = "panel.scripts";
        public const string StyleResource = "UI/Scripts/scripts";

        public int Order => 51;

        static readonly LazyPanel lazy = new LazyPanel(PanelId, TitleKey, c => new ScriptsPanel(c));
        static StyleSheet sheet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => lazy.Reset();

        public void Register(UiContext ctx) => lazy.Register(ctx, ActionIds.ScriptsPanel);

        /// <summary>The style sheet of the panel, its dialogs and the Timeline header's script line, on <paramref name="el"/>.</summary>
        internal static void Style(VisualElement el)
        {
            sheet ??= Resources.Load<StyleSheet>(StyleResource);
            if (sheet != null && !el.styleSheets.Contains(sheet)) el.styleSheets.Add(sheet);
        }

        /// <summary>Registers the panel when it is not registered yet.</summary>
        public static void EnsurePanel(UiContext ctx) => lazy.Ensure(ctx);

        /// <summary>The panel is the shown tab of an open region.</summary>
        public static bool PanelShown(UiContext ctx) => lazy.Shown(ctx);

        /// <summary>scripts.panel: shows the panel, or collapses the region when the panel is its shown tab. True when the panel is shown afterwards.</summary>
        public static bool TogglePanel(UiContext ctx) => lazy.Toggle(ctx);

        /// <summary>Shows the panel and returns it (null without a shell).</summary>
        public static ScriptsPanel Show(UiContext ctx) => lazy.Show(ctx)?.Q<ScriptsPanel>();
    }
}
