// Wet and stains in the Studio UI: the panel "wet" (default region bottom) and the action wet.panel (View menu, unbound), which opens the panel's region with the
// panel shown, or collapses it when it is the shown panel. As the Constraints panel, it is registered at start only when layout.json already lists it, otherwise by
// the first wet.panel, so that the bottom region stays collapsed until the user opens it; a «Reset layout» that would open the region for the panels registered this
// way leaves it collapsed.
using System;
using System.IO;
using System.Text;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class WetUiModule : IUiModule
    {
        public const string PanelId = "wet";
        public const string TitleKey = "panel.wet";
        public const string StyleResource = "UI/Wet/wet";

        public int Order => 47;

        static UiContext readyCtx;
        static object lastLayout;
        static StyleSheet sheet;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            readyCtx = null;
            lastLayout = null;
        }

        public void Register(UiContext ctx)
        {
            var a = ctx.Registry.Actions;
            if (ActionManifest.Contains(ActionIds.WetPanel))
            {
                var d = a.Register(ActionIds.WetPanel, call => TogglePanel(ctx), () => ctx.Shell != null);
                d.IsChecked = () => PanelShown(ctx);
            }
            if (LayoutListsPanel(ctx)) EnsurePanel(ctx);
            ctx.ViewStateChanged += what => { if (what == "layout") OnLayout(ctx); };
            readyCtx = ctx;
            UiHost.Ready -= OnReady;
            UiHost.Ready += OnReady;
        }

        static void OnReady(UiHost h)
        {
            UiHost.Ready -= OnReady;
            lastLayout = (h?.Context ?? readyCtx)?.Shell?.Layout;
        }

        /// <summary>The style sheet of the panel and the section, on <paramref name="el"/>.</summary>
        internal static void Style(VisualElement el)
        {
            sheet ??= Resources.Load<StyleSheet>(StyleResource);
            if (sheet != null && !el.styleSheets.Contains(sheet)) el.styleSheets.Add(sheet);
        }

        /// <summary>True when layout.json lists the panel (in a region or hidden): the user had it before.</summary>
        static bool LayoutListsPanel(UiContext ctx)
        {
            try
            {
                string file = Path.Combine(ctx.Options.ConfigDir, LayoutDocument.FileName);
                if (!File.Exists(file)) return false;
                var d = LayoutDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                return d != null && (d.RegionOf(PanelId) != null || d.Hidden.Contains(PanelId));
            }
            catch (Exception) { return false; }
        }

        /// <summary>Registers the panel (default region bottom) when it is not registered yet.</summary>
        public static void EnsurePanel(UiContext ctx)
        {
            if (ctx.Registry.Panels.TryGet(PanelId, out _)) return;
            ctx.Registry.Panels.Register(PanelId, TitleKey, LayoutRegions.Bottom, c => new WetPanel(c), ActionScope.Global);
        }

        /// <summary>The panel is the shown tab of an open region.</summary>
        public static bool PanelShown(UiContext ctx)
        {
            var sh = ctx.Shell;
            string r = sh?.RegionOf(PanelId);
            if (r == null) return false;
            var reg = sh.Regions[r];
            return !reg.Collapsed && reg.Active == PanelId;
        }

        /// <summary>wet.panel: shows the panel, or collapses the region when the panel is its shown tab. True when the panel is shown afterwards.</summary>
        public static bool TogglePanel(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh == null) return false;
            bool shown = ctx.Registry.Panels.TryGet(PanelId, out _) && PanelShown(ctx);   // before the registration: a region left open with nothing in it shows the panel at once
            EnsurePanel(ctx);
            if (shown)
            {
                sh.SetCollapsed(sh.Regions[sh.RegionOf(PanelId)], true, true);
                return false;
            }
            return sh.ShowPanel(PanelId);
        }

        /// <summary>Shows the panel and returns it (null without a shell).</summary>
        public static WetPanel Show(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh == null) return null;
            EnsurePanel(ctx);
            return sh.ShowPanel(PanelId) ? sh.PanelRoot(PanelId)?.Q<WetPanel>() : null;
        }

        // a layout replaced as a whole («Reset layout»): the bottom region does not open for the panels that appear only when asked for
        static void OnLayout(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh?.Layout == null || ReferenceEquals(sh.Layout, lastLayout)) return;
            bool first = lastLayout == null;
            lastLayout = sh.Layout;
            if (first || !sh.Regions.TryGetValue(LayoutRegions.Bottom, out var bottom) || bottom.Collapsed || !bottom.Panels.Contains(PanelId)) return;
            foreach (var p in bottom.Panels) if (p != PanelId && p != ConstraintsUiModule.PanelId && p != TimelineUiModule.PanelId) return;
            sh.SetCollapsed(bottom, true, true);
        }
    }
}
