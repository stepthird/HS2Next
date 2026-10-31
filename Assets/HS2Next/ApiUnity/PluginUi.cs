// UI registration of a plugin (engine side of the plugin Api; experimental: it may change in any minor release). Panels go
// to a region of the Studio shell, inspector sections to the inspector of a node kind, actions to the action list
// (reachable through ui.action and shortcuts); labels are localisation keys of the plugin's own strings. Ids are stored
// under the plugin's GUID; everything is removed when the plugin unloads. Factories run inside the plugin's fault wrapper:
// a throwing factory shows an error label in its place.
using System;
using UnityEngine.UIElements;

namespace HS2Next.Api.Unity
{
    /// <summary>The Studio UI for one plugin. Implemented by the host.</summary>
    [Experimental("the UI registration may change in any minor release")]
    public interface IPluginUi
    {
        /// <summary>
        /// Adds a panel to <paramref name="region"/> ("left", "right" or "bottom"); <paramref name="factory"/> builds its content
        /// when the tab is first shown. A second registration of the same id replaces the first. ArgumentException for an empty
        /// id or another region.
        /// </summary>
        void RegisterPanel(string id, string titleKey, string region, Func<VisualElement> factory, int order = 0);
        /// <summary>
        /// Adds a section to the inspector of nodes of <paramref name="kind"/> ("chara", "item", "light", "camera", "folder",
        /// "route"); <paramref name="factory"/> builds it for the node the inspector shows.
        /// </summary>
        void RegisterInspectorSection(string kind, string id, string titleKey, Func<IUiSectionContext, VisualElement> factory, int order = 0);
        /// <summary>Adds an action (its label is <paramref name="labelKey"/>); <paramref name="run"/> runs on the main thread.</summary>
        void RegisterAction(string id, string labelKey, Action run);
        /// <summary>
        /// Adds localised strings in the strings.tsv format (a header row "key en ja zh-Hans note", then one row per key); every
        /// key must start with "plugin.&lt;guid&gt;." (ArgumentException otherwise; nothing is added then).
        /// </summary>
        void AddStrings(string tsv);
    }

    /// <summary>What an inspector section is built for. Implemented by the host.</summary>
    [Experimental("the UI registration may change in any minor release")]
    public interface IUiSectionContext
    {
        /// <summary>The node the inspector shows.</summary>
        string NodeId { get; }
        /// <summary>Its kind ("chara", "item", ...).</summary>
        string Kind { get; }
    }
}
