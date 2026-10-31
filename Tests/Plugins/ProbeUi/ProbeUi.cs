// The loader probe of the UI registration (CC0-1.0): through the engine-side services it adds a panel to the right region,
// a section to the inspector of items and an action; its strings.tsv labels them. probeui.state reports what was built.
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using UnityEngine.UIElements;

namespace Stp3.Test.ProbeUi
{
    public sealed class ProbeUiPlugin : IPlugin
    {
        IPluginContext ctx;
        bool unity, ui, panelBuilt;
        int actionRuns, sections;
        string sectionNode, sectionKind;

        public void Load(IPluginContext context)
        {
            ctx = context;
            var services = context.Unity();
            unity = services != null;
            var u = services?.Ui;
            ui = u != null;
            if (u != null)
            {
                u.RegisterPanel("panel", "plugin.stp3.test.probeui.panel", "right", () =>
                {
                    panelBuilt = true;
                    return new Label("probe ui panel") { name = "stp3probeui-panel" };
                });
                u.RegisterInspectorSection("item", "section", "plugin.stp3.test.probeui.section", sc =>
                {
                    sections++;
                    sectionNode = sc.NodeId;
                    sectionKind = sc.Kind;
                    return new Label("probe section " + sc.NodeId) { name = "stp3probeui-section" };
                });
                u.RegisterAction("hello", "plugin.stp3.test.probeui.hello", () => actionRuns++);
            }
            context.Commands.Register(new CommandSpec("probeui.state", "What the UI registration built.").WithFlags(CommandFlags.ReadOnly), _ => new Dictionary<string, object>
            {
                ["unity"] = unity, ["ui"] = ui, ["panelBuilt"] = panelBuilt, ["sections"] = sections, ["sectionNode"] = sectionNode, ["sectionKind"] = sectionKind,
                ["actionRuns"] = actionRuns,
            });
        }

        public void Unload() => ctx?.Log.Info("probeui unloaded");
    }
}
