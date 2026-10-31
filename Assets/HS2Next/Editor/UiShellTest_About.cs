// UiShellTest.play group AB (Phase 5.4 package C): the about part at the end of File › Options › Advanced, in en, ja and
// zh-Hans. Two notices inside the Advanced section, each with its heading and paragraph showing the table's strings (not
// their keys) and one line per entry: "settings-compat" with one line per family of ShaderMap.CompatibleFamilies (the family's
// label and its number of shaders, in the list's order; the paragraph names HS2Next/Compatible/), and "settings-plugindata"
// with one line per name of OptionsSettingsSections.PluginDataNames (not empty, in ordinal order). Both lists are not empty
// and have as many lines as entries. Unity's two notice lines follow, in English in every language, after both notices.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterAboutGroup()
    {
        if (!Groups.Any(g => g.id == "AB")) Groups.Add(("AB", AboutNotices));
    }

    static IEnumerator AboutNotices(Ctx c)
    {
        var ui = c.Ui;
        var families = ShaderMap.CompatibleFamilies;
        var plugins = OptionsSettingsSections.PluginDataNames;
        c.Check(families != null && families.Count > 0, "ShaderMap.CompatibleFamilies is not empty (" + (families?.Count ?? 0) + " families)");
        c.Check(plugins != null && plugins.Count > 0 && plugins.SequenceEqual(plugins.OrderBy(x => x, StringComparer.Ordinal)) && plugins.Distinct().Count() == plugins.Count,
            "PluginDataNames is not empty, ordinal and without repeats: " + string.Join(", ", plugins ?? Array.Empty<string>()));
        var wantFamilies = families?.Select(f => f.Key + " (" + f.Value.ToString(CultureInfo.InvariantCulture) + ")").ToList() ?? new List<string>();
        foreach (var lang in new[] { "en", "ja", "zh-Hans" })
        {
            c.Host.SetLanguage(lang, save: false);
            yield return Frames(2);
            OptionsPanel.Show(ui);
            yield return Frames(4);
            var op = OptionsPanel.Open;
            var advanced = op?.Q("options-section-advanced");
            var compat = advanced?.Q("settings-compat");
            var plugin = advanced?.Q("settings-plugindata");
            var unity = advanced?.Q("settings-unity");
            c.Check(compat != null && plugin != null && unity != null, $"{lang}: the Advanced section holds both notices and Unity's lines");
            AboutBlock(c, lang, compat, "settings-compat", "about.compat.title", "about.compat.text", wantFamilies, "HS2Next/Compatible/");
            AboutBlock(c, lang, plugin, "settings-plugindata", "about.plugins.title", "about.plugins.text", plugins?.ToList() ?? new List<string>(), "Sideloader");
            var tm = unity?.Q<Label>("settings-unity-trademark");
            var cr = unity?.Q<Label>("settings-unity-copyright");
            c.Check(tm != null && cr != null && tm.text == OptionsSettingsSections.UnityTrademark && cr.text == OptionsSettingsSections.UnityCopyright
                && tm.text.StartsWith("HS2Next was made with Unity®.", StringComparison.Ordinal) && cr.text.StartsWith("Copyright © 2005-2026 Unity Technologies.", StringComparison.Ordinal),
                $"{lang}: Unity's trademark and copyright lines in English ({tm?.text ?? "none"} / {cr?.text ?? "none"})");
            int ic = Index(compat), ip = Index(plugin), iu = Index(unity);
            c.Check(compat != null && plugin != null && unity != null && compat.hierarchy.parent == plugin.hierarchy.parent && plugin.hierarchy.parent == unity.hierarchy.parent
                && ic < ip && ip < iu && unity.hierarchy.parent.Children().Where(x => x.name != null && x.name.StartsWith("settings-", StringComparison.Ordinal) && x.name != "settings-restart-advanced").LastOrDefault() == unity,
                $"{lang}: the shader notice, the plugin notice and Unity's lines in that order, Unity's lines last of the about part ({ic}, {ip}, {iu})");
            while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
            yield return Frames(2);
        }
        c.Host.SetLanguage("en", save: false);
        yield return Frames(2);
    }

    static int Index(VisualElement e) => e?.hierarchy.parent?.IndexOf(e) ?? -1;

    /// <summary>One notice: heading and paragraph are the table's strings of the current language, the entries the lines.</summary>
    static void AboutBlock(Ctx c, string lang, VisualElement block, string name, string titleKey, string textKey, List<string> want, string textNames)
    {
        var title = block?.Q<Label>(name + "-title");
        var text = block?.Q<Label>(name + "-text");
        string wantTitle = L10n.Get(titleKey), wantText = L10n.Get(textKey);
        c.Check(title != null && title.text == wantTitle && wantTitle != titleKey, $"{lang} {name}: the heading reads \"{wantTitle}\" ({title?.text ?? "none"})");
        c.Check(text != null && text.text == wantText && wantText != textKey && wantText.Contains(textNames),
            $"{lang} {name}: the paragraph is the table's text and names {textNames} ({(text?.text ?? "none").Length} characters)");
        var lines = block?.Query<Label>(name + "-entry").ToList() ?? new List<Label>();
        c.Check(lines.Count > 0 && lines.Count == want.Count && lines.Select(l => l.text).SequenceEqual(want),
            $"{lang} {name}: one line per entry ({lines.Count} lines, {want.Count} entries): " + string.Join(", ", lines.Select(l => l.text)));
    }
}
