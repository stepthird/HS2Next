// What the panels «Chara to Item» and «Cutter» share as panels that appear only when asked for: as the Constraints, Wet and stains and Ragdoll panels, such a panel is
// registered at start only when layout.json already lists it, otherwise by its first action, so that the bottom region stays collapsed until the user opens it; a «Reset layout»
// that would open the region for these panels alone leaves it collapsed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    internal sealed class LazyPanel
    {
        /// <summary>The panels that are registered only when asked for: a bottom region that holds only these stays collapsed after «Reset layout».</summary>
        static readonly HashSet<string> AskedFor = new HashSet<string>(StringComparer.Ordinal)
        {
            BakeUiModule.PanelId, CutUiModule.PanelId, RagdollUiModule.PanelId, WetUiModule.PanelId, ConstraintsUiModule.PanelId, TimelineUiModule.PanelId,
            ScriptsUiModule.PanelId,
        };

        readonly string id, titleKey;
        readonly Func<UiContext, VisualElement> make;
        object lastLayout;
        UiContext readyCtx;

        public LazyPanel(string id, string titleKey, Func<UiContext, VisualElement> make)
        {
            this.id = id;
            this.titleKey = titleKey;
            this.make = make;
        }

        /// <summary>The module's part of Register: the action (when the manifest holds it), the panel when the layout lists it, the layout's watch.</summary>
        public void Register(UiContext ctx, string actionId)
        {
            var a = ctx.Registry.Actions;
            if (ActionManifest.Contains(actionId))
            {
                var d = a.Register(actionId, call => Toggle(ctx), () => ctx.Shell != null);
                d.IsChecked = () => Shown(ctx);
            }
            if (LayoutLists(ctx)) Ensure(ctx);
            ctx.ViewStateChanged += what => { if (what == "layout") OnLayout(ctx); };
            readyCtx = ctx;
            lastLayout = null;
            UiHost.Ready -= OnReady;
            UiHost.Ready += OnReady;
        }

        /// <summary>A new play session (statics of the module are reset).</summary>
        public void Reset()
        {
            readyCtx = null;
            lastLayout = null;
        }

        void OnReady(UiHost h)
        {
            UiHost.Ready -= OnReady;
            lastLayout = (h?.Context ?? readyCtx)?.Shell?.Layout;
        }

        /// <summary>True when layout.json lists the panel (in a region or hidden): the user had it before.</summary>
        bool LayoutLists(UiContext ctx)
        {
            try
            {
                string file = Path.Combine(ctx.Options.ConfigDir, LayoutDocument.FileName);
                if (!File.Exists(file)) return false;
                var d = LayoutDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                return d != null && (d.RegionOf(id) != null || d.Hidden.Contains(id));
            }
            catch (Exception) { return false; }
        }

        /// <summary>Registers the panel (default region bottom) when it is not registered yet.</summary>
        public void Ensure(UiContext ctx)
        {
            if (ctx.Registry.Panels.TryGet(id, out _)) return;
            ctx.Registry.Panels.Register(id, titleKey, LayoutRegions.Bottom, c => make(c), ActionScope.Global);
        }

        /// <summary>The panel is the shown tab of an open region.</summary>
        public bool Shown(UiContext ctx)
        {
            var sh = ctx.Shell;
            string r = sh?.RegionOf(id);
            if (r == null) return false;
            var reg = sh.Regions[r];
            return !reg.Collapsed && reg.Active == id;
        }

        /// <summary>The action: shows the panel, or collapses the region when the panel is its shown tab. True when the panel is shown afterwards.</summary>
        public bool Toggle(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh == null) return false;
            bool shown = ctx.Registry.Panels.TryGet(id, out _) && Shown(ctx);   // before the registration: a region left open with nothing in it shows the panel at once
            Ensure(ctx);
            if (shown)
            {
                sh.SetCollapsed(sh.Regions[sh.RegionOf(id)], true, true);
                return false;
            }
            return sh.ShowPanel(id);
        }

        /// <summary>Shows the panel; its root element, or null without a shell.</summary>
        public VisualElement Show(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh == null) return null;
            Ensure(ctx);
            return sh.ShowPanel(id) ? sh.PanelRoot(id) : null;
        }

        // a layout replaced as a whole («Reset layout»): the bottom region does not open for the panels that appear only when asked for
        void OnLayout(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh?.Layout == null || ReferenceEquals(sh.Layout, lastLayout)) return;
            bool first = lastLayout == null;
            lastLayout = sh.Layout;
            if (first || !sh.Regions.TryGetValue(LayoutRegions.Bottom, out var bottom) || bottom.Collapsed || !bottom.Panels.Contains(id)) return;
            foreach (var p in bottom.Panels) if (!AskedFor.Contains(p)) return;
            sh.SetCollapsed(bottom, true, true);
        }
    }
}
