// The Character page of the Edit tab (Phase 4.6): one page, one section «キャラ設定» (`edit-section-character.character.profile`)
// on the primary character:
//   «名前» (`field-appearance.chara.profile.name`, a text field committed on Enter / blur): StudioOps.EditAppearance on
//     profile.name, one entry "Rename character" (consecutive typed names merge as the Maker's typed edits do);
//   read-only lines (`field-<path>`: the label, the value, no input element) — the sex; futanari (female only; edited on
//     Body › Special, the line says so); the imported game parameters of profile.hs2 (personality, birthday, voice pitch,
//     wishes, the Parameter2 members; numbers as stored) or one line «なし (HS2Next で作成)» (`character-hs2-none`) for a
//     character made in HS2Next; the import provenance of the character's documents (source file name, imported time in
//     local time, importer, the first 12 hex digits of the SHA-256, the count of Sideloader GUIDs) or one line «HS2Next で作成»
//     (`character-provenance-none`).
// The lines follow the documents (a load, an undo) through bindings on their paths.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class CharaEditTab
    {
        public const string CharacterNode = "character";
        const string Hs2Prefix = "appearance.chara.profile.hs2.";

        TextField characterName;
        /// <summary>The «名前» field of the Character page (null until built).</summary>
        public TextField CharacterNameField => characterName;
        /// <summary>The last result of a Character page write (tests).</summary>
        public OpResult LastCharacterResult { get; private set; }

        VisualElement BuildCharacterPage(VisualElement c, List<SchemaSection> sections)
        {
            var sw = Stopwatch.StartNew();
            WithGateMemo(() =>
            {
                foreach (var s in sections)
                {
                    var fo = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
                    sectionFoldouts[s.Id] = fo;
                    bool hs2Done = false, provDone = false;
                    foreach (var f in s.Fields)
                    {
                        if (!GateHolds(f.Gate)) continue;
                        if (!f.ReadOnly) { fo.Add(f.Kind == FieldKind.Text ? NameRow(f) : BuildRow(f, null)); continue; }
                        bool hs2 = f.Path != null && f.Path.StartsWith(Hs2Prefix, StringComparison.Ordinal);
                        bool prov = f.Path != null && f.Path.StartsWith(InspectorSchemas.ProvenancePrefix, StringComparison.Ordinal);
                        if (hs2 && !hs2Done) { fo.Add(NoneLine("character-hs2-none", "inspector.charaEdit.character.hs2", "inspector.charaEdit.character.hs2None", "appearance.chara.profile", HasHs2)); hs2Done = true; }
                        if (prov && !provDone) { fo.Add(NoneLine("character-provenance-none", "inspector.charaEdit.character.provenance", "inspector.charaEdit.character.madeInHs2Next", "appearance", HasProvenance)); provDone = true; }
                        fo.Add(ReadOnlyLine(f, hs2, prov));
                    }
                    c.Add(fo);
                }
            });
            PageBuildMs[CharacterNode + ".character"] = sw.Elapsed.TotalMilliseconds;
            return c;
        }

        CharaSubtree PrimaryStored => ctx.Model.Find(primary)?.Appearance?.Stored;
        bool HasHs2() => PrimaryStored?.Chara?.Profile?.Hs2 != null;
        bool HasProvenance() => PrimaryStored?.Import != null;

        /// <summary>«名前»: a delayed text field writing profile.name of the primary (one entry "Rename character").</summary>
        VisualElement NameRow(FieldSpec f)
        {
            var row = new VisualElement { name = "field-" + f.Path };
            row.AddToClassList(FieldRow.UssClass);
            characterName = new TextField(L10n.Get(f.LabelKey)) { isDelayed = true, name = "text" };
            Loc.Bind(characterName, f.LabelKey, (e, s) => ((TextField)e).label = s);
            characterName.RegisterValueChangedCallback(e => WriteCharacterName(e.newValue));
            row.Add(characterName);
            ctx.Bindings.Bind(row, TargetSelector.Node(primary), f.Path, (el, v) =>
            {
                if (characterName.panel?.focusController?.focusedElement != characterName) characterName.SetValueWithoutNotify(v.Value as string ?? "");
            });
            return row;
        }

        /// <summary>Renames the primary character (profile.name; one entry "Rename character", typed names merge; tests call it).</summary>
        public OpResult WriteCharacterName(string name)
        {
            string path = InspectorSchemas.CharaEditPath("profile.name");
            var o = UiContext.User(OpLabels.RenameCharacter, "ui.typed/" + primary + "/" + path);
            LastCharacterResult = ctx.ShowResult(ctx.Ops.EditAppearance(new[] { primary }, new List<(string, object)> { (path, name ?? "") }, o));
            if (characterName != null && characterName.panel?.focusController?.focusedElement != characterName)
                characterName.SetValueWithoutNotify(PrimaryStored?.Chara?.Profile?.Name ?? "");
            return LastCharacterResult;
        }

        /// <summary>A label + value line without an input element.</summary>
        static VisualElement LineShell(string name, string labelKey, out Label value)
        {
            var row = new VisualElement { name = name };
            row.AddToClassList(FieldRow.UssClass);
            row.AddToClassList("hs-readonly-line");
            var l = new Label(L10n.Get(labelKey)) { name = "label" };
            l.AddToClassList("hs-field__label");
            Loc.Bind(l, labelKey);
            row.Add(l);
            value = new Label { name = "value" };
            value.AddToClassList("hs-readonly-line__value");
            row.Add(value);
            return row;
        }

        /// <summary>The line shown instead of a group of lines whose data are absent (no game parameters, no provenance).</summary>
        VisualElement NoneLine(string name, string labelKey, string valueKey, string bindPath, Func<bool> present)
        {
            var row = LineShell(name, labelKey, out var value);
            Loc.Bind(value, valueKey);
            ctx.Bindings.Bind(row, TargetSelector.Node(primary), bindPath, (el, v) => row.style.display = present() ? DisplayStyle.None : DisplayStyle.Flex);
            return row;
        }

        /// <summary>A read-only line of a schema row: the value as text, refreshed when its path (the provenance: any appearance
        /// change) changes; hidden while its group's data are absent.</summary>
        VisualElement ReadOnlyLine(FieldSpec f, bool hs2, bool prov)
        {
            var row = LineShell("field-" + f.Path, f.LabelKey, out var value);
            if (f.TooltipKey != null)
            {
                Loc.Tooltip(row, f.TooltipKey);
                var hint = Loc.Label(f.TooltipKey);
                hint.name = "hint";
                hint.AddToClassList(InspectorUi.Dim);
                hint.AddToClassList("hs-readonly-line__hint");
                row.Add(hint);
            }
            string bind = prov ? "appearance" : hs2 ? "appearance.chara.profile" : f.Path;
            ctx.Bindings.Bind(row, TargetSelector.Node(primary), bind, (el, v) =>
            {
                bool present = prov ? HasProvenance() : !hs2 || HasHs2();
                row.style.display = present ? DisplayStyle.Flex : DisplayStyle.None;
                value.text = present ? LineText(f) : "";
            });
            return row;
        }

        /// <summary>The text of a read-only line for the primary's current documents ("—" when the member cannot be read).</summary>
        public string LineText(FieldSpec f)
        {
            var st = PrimaryStored;
            const string none = "—";
            if (st == null || f?.Path == null) return none;
            var inv = CultureInfo.InvariantCulture;
            if (f.Path.StartsWith(InspectorSchemas.ProvenancePrefix, StringComparison.Ordinal))
            {
                var imp = st.Import;
                if (imp == null) return none;
                switch (f.Path.Substring(InspectorSchemas.ProvenancePrefix.Length))
                {
                    case "fileName": return imp.FileName ?? none;
                    case "importedUtc": return imp.ImportedUtc == default ? none : imp.ImportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", inv);
                    case "importer": return imp.Importer ?? none;
                    case "sha256": return string.IsNullOrEmpty(imp.Sha256) ? none : imp.Sha256.Substring(0, Math.Min(12, imp.Sha256.Length));
                    case "sideloaderGuids": return (imp.SideloaderGuids?.Count ?? 0).ToString(inv);
                    default: return none;
                }
            }
            var p = st.Chara?.Profile;
            if (p == null) return none;
            if (f.Path == InspectorSchemas.CharaEditPath("profile.sex"))
            {
                int k = p.Sex;
                return f.Choices != null && k >= 0 && k < f.Choices.Count ? L10n.Get(f.ChoiceLabelKey(k)) : p.Sex.ToString(inv);
            }
            if (f.Path == InspectorSchemas.CharaEditPath("profile.futanari"))
                return L10n.Get(p.Futanari ? "inspector.charaEdit.character.futanariOn" : "inspector.charaEdit.character.futanariOff");
            if (!f.Path.StartsWith(Hs2Prefix, StringComparison.Ordinal)) return none;
            var h = p.Hs2;
            if (h == null) return none;
            var p2 = h.Parameter2;
            switch (f.Path.Substring(Hs2Prefix.Length))
            {
                case "personality": return h.Personality.ToString(inv);
                case "birthMonth": return L10n.Format("inspector.charaEdit.character.birthdayValue", h.BirthMonth.ToString(inv), h.BirthDay.ToString(inv));
                case "voiceRate": return h.VoiceRate.ToString(f.Format ?? "0.00", inv);
                case "wishes": return h.Wishes == null || h.Wishes.Count == 0 ? L10n.Get("common.none") : string.Join(", ", h.Wishes.OrderBy(x => x).Select(x => x.ToString(inv)));
                case "parameter2.personality": return p2 == null ? none : p2.Personality.ToString(inv);
                case "parameter2.voiceRate": return p2 == null ? none : p2.VoiceRate.ToString(f.Format ?? "0.00", inv);
                case "parameter2.trait": return p2 == null ? none : p2.Trait.ToString(inv);
                case "parameter2.mind": return p2 == null ? none : p2.Mind.ToString(inv);
                case "parameter2.hAttribute": return p2 == null ? none : p2.HAttribute.ToString(inv);
                default: return none;
            }
        }
    }
}
