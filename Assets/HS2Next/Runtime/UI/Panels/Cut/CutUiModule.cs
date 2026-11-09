// The Cutter in the Studio UI: the panel "cut" (default region bottom) and the action cut.panel (View menu, unbound), which opens the panel's region with the panel shown, or collapses
// it when it is the shown panel. The panel appears only when asked for (LazyPanel). «Edit cut» of the item inspector opens it on a piece.
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class CutUiModule : IUiModule
    {
        public const string PanelId = "cut";
        public const string TitleKey = "panel.cut";

        public int Order => 50;

        static readonly LazyPanel lazy = new LazyPanel(PanelId, TitleKey, c => new CutPanel(c));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => lazy.Reset();

        public void Register(UiContext ctx) => lazy.Register(ctx, ActionIds.CutPanel);

        /// <summary>Registers the panel when it is not registered yet.</summary>
        public static void EnsurePanel(UiContext ctx) => lazy.Ensure(ctx);

        /// <summary>The panel is the shown tab of an open region.</summary>
        public static bool PanelShown(UiContext ctx) => lazy.Shown(ctx);

        /// <summary>cut.panel: shows the panel, or collapses the region when the panel is its shown tab. True when the panel is shown afterwards.</summary>
        public static bool TogglePanel(UiContext ctx) => lazy.Toggle(ctx);

        /// <summary>Shows the panel and returns it (null without a shell).</summary>
        public static CutPanel Show(UiContext ctx) => lazy.Show(ctx)?.Q<CutPanel>();

        /// <summary>«Edit cut»: the panel on <paramref name="piece"/>, its values from the piece's last step, and a marker put where that step is.</summary>
        public static CutPanel EditCut(UiContext ctx, string piece)
        {
            var p = Show(ctx);
            p?.EditPiece(piece);
            return p;
        }
    }
}
