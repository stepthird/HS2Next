// The File page's character load dialog «キャラを読込» (Phase 4.6; HS2's Maker load window): a modal dialog holding the file
// browser of the primary character's sex (CoordinateBrowser of kind Character: the HS2 cards of the install's sex folder and
// HS2Next's .hs2nchara files whose manifest names that sex; search, sort by name / date, refresh; cells
// `chara-file-<file name>`), the five part checkboxes «顔 · 体 · 髪 · コーデ · キャラ設定» (`chara-load-flag-<face|body|hair|
// coordinate|profile>`, remembered as the view-state option maker.loadFlags, default all on), a note line
// (`chara-load-note`), «読込» (`dialog-button-load`, disabled without a chosen file or a ticked part) and «キャンセル». «読込»
// runs the caller's load (StudioOps.LoadCharacter, one entry "Load character"): success closes the dialog, a failure keeps it
// open with the op's message in the note. `ui.state makerFile.loadDialog` reports the dialog's sex, flags, chosen file and
// whether «読込» is enabled.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class CharaLoadDialog
    {
        public const string DialogId = "chara-load", LoadButtonId = "load";
        /// <summary>The dialog open now (null when none).</summary>
        public static CharaLoadDialog Current { get; private set; }

        public CoordinateBrowser Browser { get; private set; }
        public Dialog Dialog { get; private set; }
        /// <summary>The checkbox of each flag (Face … Profile).</summary>
        public IReadOnlyDictionary<LoadFlags, Toggle> FlagToggles => toggles;
        /// <summary>The ticked parts.</summary>
        public LoadFlags Flags { get; private set; }
        /// <summary>The result of the last «読込» with a file and a part (null before).</summary>
        public OpResult LastResult { get; private set; }
        /// <summary>The note line (no file, no part, the op's failure).</summary>
        public Label Note { get; private set; }
        /// <summary>True when «読込» can load (a file chosen and a part ticked).</summary>
        public bool LoadEnabled => Browser?.Selected != null && Flags != LoadFlags.None;

        static readonly LoadFlags[] Bits = { LoadFlags.Face, LoadFlags.Body, LoadFlags.Hair, LoadFlags.Coordinate, LoadFlags.Profile };
        readonly Dictionary<LoadFlags, Toggle> toggles = new Dictionary<LoadFlags, Toggle>();
        UiContext ctx;
        Func<string, LoadFlags, OpResult> load;

        /// <summary>The parts remembered in maker.loadFlags (all five when the option is absent or unreadable).</summary>
        public static LoadFlags Remembered(UiContext ctx)
        {
            string s = null;
            try { if (ctx?.Options != null && OptionDefs.TryGet(OptionDefs.MakerLoadFlags, out _)) s = ctx.Options.String(OptionDefs.MakerLoadFlags); }
            catch (Exception) { s = null; }
            return Parse(s);
        }

        /// <summary>The flags of an option text (names separated by commas; "all" = the five; null = all; "" = none).</summary>
        public static LoadFlags Parse(string text)
        {
            if (text == null) return LoadFlags.All;
            var names = new List<string>();
            foreach (var p in text.Split(','))
            {
                string n = p.Trim();
                if (n.Length > 0) names.Add(n);
            }
            return LoadFlagNames.TryParse(names, out var f, out _) ? f : LoadFlags.All;
        }

        /// <summary>The option text of a flag set (the names in bit order separated by commas).</summary>
        public static string Format(LoadFlags flags) => string.Join(",", LoadFlagNames.ToNames(flags));

        /// <summary>Opens the dialog for the files of <paramref name="sex"/>; <paramref name="load"/> runs (file, parts) on «読込».</summary>
        public static CharaLoadDialog Open(UiContext ctx, int sex, Func<string, LoadFlags, OpResult> load)
        {
            if (ctx?.Dialogs == null) return null;
            var d = new CharaLoadDialog { ctx = ctx, load = load, Flags = Remembered(ctx) };
            var content = new VisualElement { name = "chara-load-content" };
            content.AddToClassList("hs-coord-dialog");
            content.AddToClassList("hs-chara-load-dialog");
            d.Browser = new CoordinateBrowser(ctx, sex, "chara-load-browser", FileBrowserKind.Character);
            d.Browser.Grid.HintKey = "browser.file.hint";
            d.Browser.SelectionChanged += _ => d.Update();
            d.Browser.Listed += d.Update;
            content.Add(d.Browser);
            var flags = new VisualElement { name = "chara-load-flags" };
            flags.AddToClassList("hs-chara-load__flags");
            var fl = Loc.Label("inspector.charaEdit.file.loadFlags");
            fl.AddToClassList("hs-field__label");
            flags.Add(fl);
            for (int i = 0; i < Bits.Length; i++)
            {
                var bit = Bits[i];
                string key = "inspector.charaEdit.file.flag." + LoadFlagNames.Names[i];
                var t = new Toggle(L10n.Get(key)) { name = "chara-load-flag-" + LoadFlagNames.Names[i] };
                Loc.Bind(t, key, (e, s) => ((Toggle)e).label = s);
                t.SetValueWithoutNotify((d.Flags & bit) != 0);
                t.RegisterValueChangedCallback(e => d.SetFlag(bit, e.newValue));
                d.toggles[bit] = t;
                flags.Add(t);
            }
            content.Add(flags);
            d.Note = new Label { name = "chara-load-note" };
            d.Note.AddToClassList("hs-text--dim");
            content.Add(d.Note);
            content.RegisterCallback<AttachToPanelEvent>(_ => d.Update());
            var spec = new DialogSpec
            {
                Id = DialogId, Title = L10n.Get("inspector.charaEdit.file.loadTitle"), Message = L10n.Get("inspector.charaEdit.file.loadMessage"),
                Content = content,
            };
            spec.Buttons.Add(DialogButton.Of(LoadButtonId, "inspector.charaEdit.file.loadButton", true));
            spec.Buttons.Add(DialogButton.Cancel());
            spec.CanClose = id => id != LoadButtonId || d.Load();
            Current = d;
            d.Dialog = ctx.Dialogs.Show(spec, _ => { if (Current == d) Current = null; });
            d.Update();
            return d;
        }

        /// <summary>Ticks or clears one part (a checkbox does it; tests call it) and remembers the parts in maker.loadFlags.</summary>
        public void SetFlag(LoadFlags bit, bool on)
        {
            Flags = on ? Flags | bit : Flags & ~bit;
            if (toggles.TryGetValue(bit, out var t) && t.value != on) t.SetValueWithoutNotify(on);
            try { if (ctx?.Options != null && OptionDefs.TryGet(OptionDefs.MakerLoadFlags, out _)) ctx.Options.Set(OptionDefs.MakerLoadFlags, Format(Flags)); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[ui] maker.loadFlags: " + e.Message); }
            Update();
        }

        /// <summary>«読込»: false (the dialog stays open, the note says why) without a file or a part, or when the load failed.</summary>
        public bool Load()
        {
            var e = Browser?.Selected;
            if (e == null) { Note.text = L10n.Get("inspector.charaEdit.file.noFile"); return false; }
            if (Flags == LoadFlags.None) { Note.text = L10n.Get("inspector.charaEdit.file.noFlag"); return false; }
            LastResult = load?.Invoke(e.FullPath, Flags);
            if (LastResult != null && !LastResult.Ok)
            {
                Note.text = LastResult.Message ?? LastResult.Code ?? "";
                return false;
            }
            return true;
        }

        /// <summary>Presses a button of the dialog as a click would ("load" or "cancel"; tests).</summary>
        public void Press(string id) { if (Dialog != null && Dialog.IsOpen && ctx?.Dialogs?.Current == Dialog) ctx.Dialogs.Press(id); }

        /// <summary>«読込» enabled when a file is chosen and a part ticked; the note cleared once both are there.</summary>
        void Update()
        {
            var b = Dialog?.Element?.Q<Button>("dialog-button-" + LoadButtonId);
            b?.SetEnabled(LoadEnabled);
            if (LoadEnabled && Note != null && (Note.text == L10n.Get("inspector.charaEdit.file.noFile") || Note.text == L10n.Get("inspector.charaEdit.file.noFlag"))) Note.text = "";
        }

        /// <summary>The «読込» button element (null while the dialog waits in the queue).</summary>
        public Button LoadButton => Dialog?.Element?.Q<Button>("dialog-button-" + LoadButtonId);

        /// <summary>The facts of `ui.state makerFile.loadDialog`.</summary>
        public Dictionary<string, object> State()
        {
            var e = Browser?.Selected;
            var b = LoadButton;
            return new Dictionary<string, object>
            {
                ["open"] = Dialog != null && Dialog.IsOpen,
                ["sex"] = Browser != null ? (Browser.Sex == 0 ? "male" : "female") : null,
                ["ready"] = Browser != null && Browser.Ready,
                ["files"] = Browser?.Index?.Rows.Count ?? 0,
                ["shown"] = Browser?.Index?.Indices.Count ?? 0,
                ["flags"] = LoadFlagNames.ToNames(Flags),
                ["selected"] = e?.FileName,
                ["selectedPath"] = e?.FullPath,
                ["selectedKind"] = e == null ? null : e.IsHs2n ? "hs2nchara" : "card",
                ["loadEnabled"] = b != null ? b.enabledSelf : LoadEnabled,
                ["note"] = Note?.text,
            };
        }
    }
}
