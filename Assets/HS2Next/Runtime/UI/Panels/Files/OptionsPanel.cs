// File ▸ Options (U32): every option of OptionDefs in its section (General, Camera, Gizmo,
// Add, View, Kinematics), one control per kind (toggle, slider with a typed field for numbers, drop-down for choices),
// written through UiOptions.Set (options.json, debounced; Changed tells the shell, camera, gizmos and panels). A value
// the command line fixes is shown locked with the source in its tooltip. The General section also lists key-map
// conflicts (ui.keymapConflict) and offers "Key bindings…" and "Reset layout". The Rendering section holds the screenshot
// options (Phase 3.6) and, from the app's `render` block, the view's clear colour (option render.backgroundColor: a colour
// swatch + picker, "" = the settings file's colour; RenderOptionsModule applies it) and the pause's shader-time scope
// (render.sceneShaderTime); the other render settings stay with `app.options render` (a note says so). The dialog box is
// sized (hs-dialog--options: 760 px or 92 % of the window wide, at most 85 % of its height): the section list and the
// scrolling options fill it, so nothing draws outside the box. v0.3 trial 2: a row's label takes a fixed share of the row and
// wraps onto further lines (a long label pushed its control out of the row), the control the rest; a slider's number field
// is wide enough for its longest value (1/60 s shows as 0.01666667); an Int option with a range wider than LargeIntRange (the
// render size cap) is a typed number field. Phase 5.3: four more sections - Folders, Content, Automation, Advanced - show keys of
// the settings file (OptionsSettingsSections: written into that file, applied at the next start, Restart now).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class OptionsPanel : VisualElement
    {
        public const string DialogId = "options";
        public static readonly string[] Sections = { "general", "camera", "gizmo", "add", "view", "kinematics", "rendering", "folders", "content", "automation", "advanced" };
        /// <summary>An Int option whose range is wider than this is a typed number field, not a slider.</summary>
        public const int LargeIntRange = 100000;

        readonly UiContext ctx;
        readonly Dictionary<string, Action> refreshers = new Dictionary<string, Action>(StringComparer.Ordinal);
        readonly ScrollView scroll;
        public static OptionsPanel Open { get; private set; }
        /// <summary>The control of an option key (tests).</summary>
        public readonly Dictionary<string, VisualElement> Controls = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        /// <summary>The control of a settings-file key (the sections of the settings file; tests).</summary>
        public readonly Dictionary<string, VisualElement> SettingsControls = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        /// <summary>The sections of the settings file.</summary>
        public OptionsSettingsSections SettingsSections { get; }

        OptionsPanel(UiContext ctx)
        {
            this.ctx = ctx;
            SettingsSections = new OptionsSettingsSections(ctx, this);
            name = "options-panel"; PanelKit.Style(this);
            AddToClassList("hs-options");
            var tabs = new VisualElement { name = "options-sections" };
            tabs.AddToClassList("hs-options__sections");
            scroll = new ScrollView { name = "options-scroll" };
            scroll.AddToClassList("hs-grow");
            foreach (var s in Sections)
            {
                string sec = s;
                var b = Loc.Button("option.section." + s, () => ScrollTo(sec));
                b.name = "options-tab-" + s;
                b.AddToClassList("hs-options__tab");
                tabs.Add(b);
                scroll.Add(Section(s));
            }
            Add(tabs);
            Add(scroll);
            ctx.Options.Changed += OnChanged;
            RegisterCallback<DetachFromPanelEvent>(_ => { ctx.Options.Changed -= OnChanged; if (Open == this) Open = null; });
        }

        /// <summary>Opens the options as a modal dialog.</summary>
        public static OptionsPanel Show(UiContext ctx)
        {
            if (ctx.Dialogs == null) return null;
            var p = new OptionsPanel(ctx);
            // the first section button takes the focus (not a value field: Esc then closes the dialog, and no field is edited
            // by the first keystroke)
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get("action.file.options"), Content = p, BoxClass = "hs-dialog--options", FocusOnOpen = p.Q<Button>("options-tab-" + Sections[0]) };
            spec.Buttons.Add(DialogButton.Of("close", "common.close", isDefault: true, isCancel: true));
            Open = p;
            ctx.Dialogs.Show(spec, _ => { if (Open == p) Open = null; ctx.Options.Save(); });
            return p;
        }

        /// <summary>Scrolls to an option's row once the dialog is laid out (file.options {option}).</summary>
        public void Reveal(string key)
        {
            UiDefer.Frames(2, () =>
            {
                var el = scroll.Q("option-" + key);
                if (el != null && el.panel != null) scroll.ScrollTo(el);
            });
        }

        void ScrollTo(string section)
        {
            var el = scroll.Q("options-section-" + section);
            if (el != null) scroll.ScrollTo(el);
        }

        void OnChanged(string key)
        {
            if (refreshers.TryGetValue(key, out var r)) r();
        }

        VisualElement Section(string s)
        {
            if (OptionsSettingsSections.IsSettingsSection(s)) return SettingsSections.Build(s);
            var box = new VisualElement { name = "options-section-" + s };
            box.AddToClassList("hs-options__section");
            box.Add(Loc.Label("option.section." + s));
            box.Q<Label>().AddToClassList("hs-options__section-title");
            foreach (var d in OptionDefs.All.Where(x => x.Section == s)) box.Add(Control(d));
            if (s == "general")
            {
                var row = PanelKit.Row("hs-options__buttons",
                    Named(Loc.Button("action.help.keys", () => { ctx.Dialogs.CloseCurrent(); KeyOverlay.Show(ctx); }), "options-keys"),
                    Named(Loc.Button("action.ui.resetLayout", () => ctx.Registry.Run(ActionIds.UiResetLayout, "options")), "options-reset-layout"));
                box.Add(row);
                var conflicts = new Label { name = "options-conflicts" };
                conflicts.AddToClassList("hs-options__conflicts");
                void Fill()
                {
                    var list = ctx.Keys?.Map?.Conflicts() ?? new List<KeymapConflict>();
                    conflicts.text = list.Count == 0 ? L10n.Get("options.noConflicts")
                        : string.Join("\n", list.Select(c => L10n.Format("msg.keymapConflict", L10n.Get(ActionIds.LabelKey(c.ActionA)), L10n.Get(ActionIds.LabelKey(c.ActionB)), c.Chord.ToDisplay(ctx.Keys.Layout), L10n.Get("keys.scope." + c.Scope))));
                }
                Fill();
                if (ctx.Keys != null) ctx.Keys.Changed += Fill;
                box.Add(conflicts);
            }
            if (s == "rendering")
            {
                var l = Loc.Label("options.renderingNote36");
                l.AddToClassList("hs-hint");
                box.Add(l);
            }
            return box;
        }

        static T Named<T>(T e, string n) where T : VisualElement { e.name = n; return e; }

        /// <summary>The clear-colour row: a swatch of the view's colour (RenderOptions) that opens the colour picker; picks write
        /// the option as "#RRGGBB" (Esc restores the value before the picker), ↺ = "" (the settings file's colour).</summary>
        VisualElement ColourControl(OptionDef d)
        {
            var sw = new ColorSwatch(L10n.Get(d.LabelKey)) { name = "option-control-" + d.Key, Alpha = false };
            Loc.Bind(sw.LabelElement, d.LabelKey);
            sw.Clicked += () =>
            {
                var layer = panel?.visualTree;
                if (layer == null) return;
                string before = ctx.Options.String(d.Key) ?? "";
                var picker = new ColorPicker(sw.Value, false) { name = "option-colour-picker" };
                var blocker = new VisualElement { name = "color-blocker" };
                blocker.style.position = Position.Absolute;
                blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
                blocker.RegisterCallback<PointerDownEvent>(e => { picker.Close(true); e.StopPropagation(); });
                picker.Changed += (c, _) => ctx.Options.Set(d.Key, "#" + ColorUtility.ToHtmlStringRGB(c));
                picker.Closed += commit => { blocker.RemoveFromHierarchy(); if (!commit) ctx.Options.Set(d.Key, before); };
                picker.style.position = Position.Absolute;
                var wb = sw.Button.worldBound;
                picker.style.left = Mathf.Max(0, wb.xMin - 260);
                picker.style.top = wb.yMax;
                layer.Add(blocker);
                layer.Add(picker);
                UiDefer.Frames(2, () => { if (picker.panel != null) picker.Focus(); });
            };
            refreshers[d.Key] = () => sw.SetDisplay(HS2Next.RenderOptions.BackgroundColor, false);
            return sw;
        }

        VisualElement Control(OptionDef d)
        {
            var row = new VisualElement { name = "option-" + d.Key };
            row.AddToClassList("hs-options__row");
            VisualElement control;
            if (d.Key == OptionDefs.RenderBackgroundColor) control = ColourControl(d);   // Phase 3.6 integrator: a colour, stored as "#RRGGBB"
            else switch (d.Kind)
            {
                case OptionKind.Bool:
                {
                    var t = new Toggle { name = "option-control-" + d.Key };
                    Loc.Bind(t, d.LabelKey, (e, s) => ((Toggle)e).label = s);
                    t.RegisterValueChangedCallback(e => ctx.Options.Set(d.Key, e.newValue));
                    refreshers[d.Key] = () => t.SetValueWithoutNotify(ctx.Options.Bool(d.Key));
                    control = t;
                    break;
                }
                case OptionKind.Choice:
                {
                    var f = new DropdownField { name = "option-control-" + d.Key, choices = d.Choices.ToList() };
                    Loc.Bind(f, d.LabelKey, (e, s) => ((DropdownField)e).label = s);
                    f.formatListItemCallback = c => d.ChoiceLabelKey(c) is string k ? L10n.Get(k) : c;
                    f.formatSelectedValueCallback = f.formatListItemCallback;
                    f.RegisterValueChangedCallback(e => ctx.Options.Set(d.Key, e.newValue));
                    refreshers[d.Key] = () => f.SetValueWithoutNotify(ctx.Options.String(d.Key) ?? d.Default as string);
                    control = f;
                    break;
                }
                case OptionKind.Int when (d.Max ?? 0) - (d.Min ?? 0) > LargeIntRange:
                {
                    // a large range (the render size cap in pixels) is typed: a slider would step in tens of thousands and its
                    // small number field showed the value cut (v0.3 trial 2)
                    var f = new IntegerField { name = "option-control-" + d.Key, isDelayed = true };
                    Loc.Bind(f, d.LabelKey, (e, t) => ((IntegerField)e).label = t);
                    f.RegisterValueChangedCallback(e => { ctx.Options.Set(d.Key, e.newValue); f.SetValueWithoutNotify(ctx.Options.Int(d.Key)); });
                    refreshers[d.Key] = () => f.SetValueWithoutNotify(ctx.Options.Int(d.Key));
                    control = f;
                    break;
                }
                case OptionKind.Int:
                {
                    var s = new SliderInt((int)(d.Min ?? 0), (int)(d.Max ?? 100)) { name = "option-control-" + d.Key, showInputField = true };
                    Loc.Bind(s, d.LabelKey, (e, t) => ((SliderInt)e).label = t);
                    s.RegisterValueChangedCallback(e => ctx.Options.Set(d.Key, e.newValue));
                    refreshers[d.Key] = () => s.SetValueWithoutNotify(ctx.Options.Int(d.Key));
                    control = s;
                    break;
                }
                case OptionKind.Float:
                {
                    var s = new Slider((float)(d.Min ?? 0), (float)(d.Max ?? 1)) { name = "option-control-" + d.Key, showInputField = true };
                    Loc.Bind(s, d.LabelKey, (e, t) => ((Slider)e).label = t);
                    s.RegisterValueChangedCallback(e => ctx.Options.Set(d.Key, (double)Math.Round(e.newValue, 4)));
                    refreshers[d.Key] = () => s.SetValueWithoutNotify(ctx.Options.Float(d.Key));
                    control = s;
                    break;
                }
                default:
                {
                    var f = new TextField { name = "option-control-" + d.Key, isDelayed = true };
                    Loc.Bind(f, d.LabelKey, (e, t) => ((TextField)e).label = t);
                    f.RegisterValueChangedCallback(e => ctx.Options.Set(d.Key, e.newValue));
                    refreshers[d.Key] = () => f.SetValueWithoutNotify(ctx.Options.String(d.Key) ?? "");
                    control = f;
                    break;
                }
            }
            control.AddToClassList("hs-grow");
            row.Add(control);
            var reset = new Button(() => ctx.Options.Set(d.Key, d.Default)) { name = "option-reset-" + d.Key, text = "↺" };
            reset.AddToClassList("hs-icon-button");
            Loc.Tooltip(reset, "common.default");
            row.Add(reset);
            Controls[d.Key] = control;
            var refresh = refreshers[d.Key];
            refreshers[d.Key] = () =>
            {
                refresh();
                ctx.Options.Get(d.Key, out string source);
                bool locked = source == "commandLine";
                control.SetEnabled(!locked);
                reset.SetEnabled(!locked);
                row.tooltip = (d.Vanilla != null ? L10n.Format("options.vanilla", d.Vanilla) : "") + (locked ? "\n" + L10n.Get("options.lockedByCommandLine") : "");
            };
            refreshers[d.Key]();
            return row;
        }
    }
}
