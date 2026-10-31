// The Studio UI's parts of the first-run guide: File ▸ Setup guide (a player restarts into the guide; an Editor play session says
// how) and Help ▸ About with the build (kind, version, commit) beside the engine version.
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using UnityEngine;

namespace HS2Next.UI.Setup
{
    public sealed class SetupUiModule : IUiModule
    {
        /// <summary>After the shell (0) and the file module (120): this module's help.about replaces the shell's.</summary>
        public int Order => 900;

        public void Register(UiContext ctx)
        {
            var a = ctx.Registry.Actions;
            a.Register(ActionIds.FileSetupGuide, _ => { SetupBanner.OpenGuide(ctx); return true; });
            a.Register(ActionIds.HelpAbout, _ =>
            {
                ctx.Dialogs?.Show(new DialogSpec
                {
                    Id = "about", Title = "HS2Next",
                    Message = ctx.L10n.Format("dialog.about.message", Application.version, Application.unityVersion) + "\n" + OptionsSettingsSections.AboutText(),
                });
                return true;
            });
        }
    }
}
