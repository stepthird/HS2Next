// The key-binding overlay (Appendix C): F1 (both presets; F2 too in hs2studio)
// opens a searchable list of every action of the manifest with its chords in the current key map, grouped by scope, with
// the preset switch (default / hs2studio) and per action: record a chord (the next key press with its modifiers; Esc
// cancels the recording), clear (unbound), reset to the preset. Edits become keymap.json overrides (only differences from
// the preset) through UiKeymap.SetOverrides; conflicts in a scope are marked. Key labels follow the keyboard layout
// (JIS keycaps from K5 / S8).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class KeyOverlay : VisualElement
    {
        public const string DialogId = "keys";

        readonly UiContext ctx;
        readonly ListView list;
        readonly List<object> rows = new List<object>();     // string = scope header, ActionInfo = action row
        readonly Label status;
        string query = "";
        string recording;
        readonly DropdownField preset;

        public static KeyOverlay Open { get; private set; }
        public int RowCount => rows.Count(r => r is ActionInfo);
        public string Recording => recording;

        KeyOverlay(UiContext ctx)
        {
            this.ctx = ctx;
            name = "key-overlay"; PanelKit.Style(this);
            AddToClassList("hs-keys");
            focusable = true;
            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");
            var search = PanelKit.SearchField("keys-search", q => { query = q; Fill(); });
            search.AddToClassList("hs-grow");
            header.Add(search);
            preset = new DropdownField { name = "keys-preset", choices = Keymap.PresetNames.ToList(), value = ctx.Keys?.PresetName ?? Keymap.DefaultPreset };
            Loc.Bind(preset, "keys.preset", (e, t) => ((DropdownField)e).label = t);
            preset.formatListItemCallback = p => L10n.Get("keys.preset." + p);
            preset.formatSelectedValueCallback = preset.formatListItemCallback;
            preset.RegisterValueChangedCallback(e => ctx.Options.Set(OptionDefs.KeyPreset, e.newValue));
            header.Add(preset);
            Add(header);
            list = new ListView
            {
                name = "keys-list",
                itemsSource = rows,
                fixedItemHeight = 24,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            list.AddToClassList("hs-grow");
            Add(list);
            status = new Label { name = "keys-status" };
            status.AddToClassList("hs-status-text");
            Add(status);
            RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            if (ctx.Keys != null) ctx.Keys.Changed += OnKeymapChanged;
            RegisterCallback<DetachFromPanelEvent>(_ => { if (ctx.Keys != null) ctx.Keys.Changed -= OnKeymapChanged; if (Open == this) Open = null; });
            Fill();
        }

        void OnKeymapChanged()
        {
            preset.SetValueWithoutNotify(ctx.Keys.PresetName);
            Fill();
        }

        /// <summary>Opens the overlay as a modal dialog (help.keys).</summary>
        public static KeyOverlay Show(UiContext ctx)
        {
            if (ctx.Dialogs == null) return null;
            if (Open != null) { ctx.Dialogs.CloseCurrent(); return null; }    // F1 again closes it
            var k = new KeyOverlay(ctx);
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get("keys.title"), Content = k };
            spec.Buttons.Add(DialogButton.Of("close", "common.close", isDefault: true, isCancel: true));
            spec.CanClose = id => k.recording == null || id == "closed";
            Open = k;
            ctx.Dialogs.Show(spec, _ => { if (Open == k) Open = null; });
            return k;
        }

        string Chords(string id) => ctx.Keys?.ChordText(id) ?? "";

        void Fill()
        {
            rows.Clear();
            var q = SearchQuery.Parse(query);
            var conflicts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in ctx.Keys?.Map?.Conflicts() ?? new List<KeymapConflict>()) { conflicts.Add(c.ActionA); conflicts.Add(c.ActionB); }
            this.conflicts = conflicts;
            foreach (var scope in new[] { ActionScope.Global, ActionScope.Viewport, ActionScope.Tree, ActionScope.Inspector, ActionScope.Browser })
            {
                var acts = ActionManifest.All.Where(a => a.Scope == scope)
                    .Where(a => q.IsEmpty || q.Matches(SearchText.Normalize(L10n.Get(a.LabelKey) + " " + a.Id + " " + Chords(a.Id)), null, null))
                    .ToList();
                if (acts.Count == 0) continue;
                rows.Add("keys.scope." + scope);
                rows.AddRange(acts);
            }
            list.RefreshItems();
            status.text = recording != null ? L10n.Format("keys.recording", L10n.Get(ActionIds.LabelKey(recording))) : L10n.Format("search.results", RowCount, ActionManifest.All.Count);
        }

        HashSet<string> conflicts = new HashSet<string>();

        VisualElement MakeRow()
        {
            var r = new VisualElement();
            r.AddToClassList("hs-keys__row");
            var label = new Label { name = "label" };
            label.AddToClassList("hs-keys__label");
            var chords = new Label { name = "chords" };
            chords.AddToClassList("hs-keys__chords");
            var rec = new Button { name = "record", text = "⏺" };
            var clear = new Button { name = "clear", text = "✕" };
            var reset = new Button { name = "reset", text = "↺" };
            foreach (var b in new[] { rec, clear, reset }) b.AddToClassList("hs-icon-button");
            Loc.Tooltip(rec, "keys.record");
            Loc.Tooltip(clear, "keys.clear");
            Loc.Tooltip(reset, "keys.resetToPreset");
            rec.clicked += () => { if (r.userData is ActionInfo a) StartRecording(a.Id); };
            clear.clicked += () => { if (r.userData is ActionInfo a) SetChords(a.Id, new List<Chord>()); };
            reset.clicked += () => { if (r.userData is ActionInfo a) ResetToPreset(a.Id); };
            r.Add(label); r.Add(chords); r.Add(rec); r.Add(clear); r.Add(reset);
            return r;
        }

        void BindRow(VisualElement el, int i)
        {
            var o = rows[i];
            el.userData = o;
            var label = el.Q<Label>("label");
            var chords = el.Q<Label>("chords");
            bool header = o is string;
            el.EnableInClassList("hs-keys__row--header", header);
            foreach (var b in el.Query<Button>().ToList()) b.style.display = header ? DisplayStyle.None : DisplayStyle.Flex;
            if (header) { label.text = L10n.Get((string)o); chords.text = ""; el.name = "keys-scope-" + o; return; }
            var a = (ActionInfo)o;
            el.name = "keys-row-" + a.Id;
            label.text = L10n.Get(a.LabelKey);
            label.tooltip = a.Id;
            string text = Chords(a.Id);
            chords.text = recording == a.Id ? L10n.Get("keys.pressKey") : text.Length == 0 ? L10n.Get("keys.unbound") : text;
            el.EnableInClassList("hs-keys__row--conflict", conflicts.Contains(a.Id));
            el.EnableInClassList("hs-keys__row--recording", recording == a.Id);
            bool overridden = ctx.Keys?.Overrides?.Bindings.ContainsKey(a.Id) == true;
            el.EnableInClassList("hs-keys__row--changed", overridden);
        }

        public void StartRecording(string actionId)
        {
            recording = actionId;
            Focus();
            Fill();
        }

        void OnKey(KeyDownEvent e)
        {
            if (recording == null) return;
            e.StopImmediatePropagation();
            focusController?.IgnoreEvent(e);
            if (e.keyCode == KeyCode.None) return;
            if (e.keyCode == KeyCode.Escape) { recording = null; Fill(); return; }
            string key = CInput.KeyName(ctx, e);
            if (key == null || KeyNames.ModifierKeys.Contains(key)) return;   // wait for the non-modifier key
            var stroke = new KeyStroke(ShortcutRouterMods(e), key);
            var chord = new Chord(stroke);
            string id = recording;
            recording = null;
            var l = new List<Chord>(ctx.Keys?.Map?.ChordsOf(id) ?? Array.Empty<Chord>());
            if (!l.Any(c => c.ToString() == chord.ToString())) l.Add(chord);
            SetChords(id, l);
        }

        static KeyMods ShortcutRouterMods(KeyDownEvent e) => HS2Next.UI.Input.ShortcutRouter.Mods(e.modifiers);

        /// <summary>Sets an action's chords in the user's key map (keymap.json holds only differences from the preset).</summary>
        public void SetChords(string actionId, List<Chord> chords)
        {
            var k = ctx.Keys;
            if (k?.Map == null || k.Preset == null) return;
            var edited = k.Map.Clone(k.PresetName);
            edited.Set(actionId, chords);
            k.SetOverrides(k.Preset.DiffTo(edited));
            Fill();
        }

        public void ResetToPreset(string actionId)
        {
            var k = ctx.Keys;
            if (k?.Preset == null) return;
            SetChords(actionId, new List<Chord>(k.Preset.ChordsOf(actionId)));
        }
    }
}
