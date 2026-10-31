// The Studio's banner while no HS2 folder is set (the guide was skipped, or the folder fails its check): "No HS2 folder is set"
// with a button that opens the first-run guide (a restart with -setup in a player; in an Editor play session a note). It stands
// under the toolbar until a start with a valid HS2 folder.
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Setup
{
    public static class SetupBanner
    {
        public const string Name = "setup.banner";

        /// <summary>Adds the banner when the Studio UI is up (once per UI host).</summary>
        public static void ShowWhenReady() => UiHost.Ready += Show;

        static void Show(UiHost ui)
        {
            UiHost.Ready -= Show;
            var root = ui?.Context?.Shell?.Root;
            if (root == null || root.Q(Name) != null) return;
            var bar = new VisualElement { name = Name };
            bar.AddToClassList("hs-setup-banner");
            var sheet = UnityEngine.Resources.Load<StyleSheet>(SetupHost.StyleResource);
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            var text = Loc.Label("setup.banner.text");
            text.name = "setup.banner.label";
            bar.Add(text);
            var open = Loc.Button("setup.banner.open", () => OpenGuide(ui.Context));
            open.name = "setup.banner.open";
            bar.Add(open);
            root.Insert(System.Math.Min(1, root.childCount), bar);
        }

        /// <summary>File ▸ Setup guide and the banner's button: asks once, then restarts into the guide through the quit (its
        /// unsaved-changes prompt; Cancel keeps the app running); an Editor play session says how instead.</summary>
        public static void OpenGuide(UiContext ctx)
        {
            if (!AppRestart.CanRestart)
            {
                ctx?.Toasts?.Show(L10n.Get("setup.restart.editor"), ToastKind.Info, 8f);
                return;
            }
            if (ctx?.Dialogs == null) { AppRestart.RequestGuide(ctx); return; }
            ctx.Dialogs.CloseCurrent();   // a dialog in front (the Options panel) would hold the question back
            ctx.Dialogs.Confirm("setup.restart.title", "setup.restart.guide", ok =>
            {
                if (!ok) return;
                string why = AppRestart.RequestGuide(ctx);
                if (why != null) ctx.Toasts?.Show(why, ToastKind.Error, 8f);
            });
        }
    }
}
