// The host's widgets, dialogs, popup menus and toasts for one plugin's own UI elements (IUiWidgets of the plugin API,
// experimental): each factory returns a new host widget (PluginWidgetKit) wired to the plugin's callback; every callback
// runs on the main thread inside the plugin's fault wrapper and not at all once the plugin has unloaded or faulted. Labels,
// titles and messages are keys of the plugin's strings (a missing key shows the key itself); a toast's text is plain text.
// Element names: a widget's base name is "plugin-widget-<labelKey>" (the label key starts with the plugin's prefix, so it
// is unique) with the part suffixes of PluginWidgetKit; a dialog is "dialog-plugin-<guid>" with buttons
// "dialog-button-<index>"; a menu "plugin-menu-<guid>" with items "menu-item-plugin-<guid>-<index>".
using System;
using System.Collections.Generic;
using HS2Next.Api.Unity;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using ApiPhase = HS2Next.Api.Ui.EditPhase;

namespace HS2Next.UI.Plugins
{
    public sealed class PluginUiWidgets : IUiWidgets
    {
        readonly Func<UiContext> ctx;
        readonly string guid;
        readonly Func<string, Action, bool> run;
        readonly Func<bool> live;
        int seq;

        /// <param name="ctx">The UI context (null while no UI runs).</param>
        /// <param name="guid">The plugin's GUID.</param>
        /// <param name="run">Runs a callback in the plugin's fault wrapper (hook name, action).</param>
        /// <param name="live">True while the plugin's callbacks may run (loaded, not faulted).</param>
        public PluginUiWidgets(Func<UiContext> ctx, string guid, Func<string, Action, bool> run, Func<bool> live = null)
        {
            this.ctx = ctx ?? (() => null);
            this.guid = guid ?? throw new ArgumentNullException(nameof(guid));
            this.run = run ?? ((hook, a) => { a(); return true; });
            this.live = live ?? (() => true);
        }

        public string Guid => guid;

        string Name(string labelKey) => "plugin-widget-" + (string.IsNullOrEmpty(labelKey) ? guid + "-" + (++seq) : labelKey);

        void Call(string what, Action a)
        {
            if (a == null || !live()) return;
            run("widget " + what, a);
        }

        public IUiWidget Number(string labelKey, double? min, double? max, double value, Action<double, ApiPhase> changed, bool whole = false, string format = null)
        {
            var w = PluginWidgetKit.Number(Name(labelKey), labelKey, min, max, whole, format, null, (v, p, typed) => Call(labelKey, () => changed?.Invoke(v, p)));
            w.SetValue(value);
            return w;
        }

        public IUiWidget Vector(string labelKey, int components, Vector4 value, Action<Vector4, ApiPhase> changed)
        {
            var w = PluginWidgetKit.Vector(Name(labelKey), labelKey, components, null, (v, p, typed) => Call(labelKey, () => changed?.Invoke(v, p)));
            w.SetValue(value);
            return w;
        }

        public IUiWidget Toggle(string labelKey, bool value, Action<bool> changed)
        {
            var w = PluginWidgetKit.Toggle(Name(labelKey), labelKey, v => Call(labelKey, () => changed?.Invoke(v)));
            w.SetValue(value);
            return w;
        }

        public IUiWidget Choice(string labelKey, IReadOnlyList<string> labelKeys, int index, Action<int> changed)
        {
            var labels = new List<(string, string)>();
            if (labelKeys != null) foreach (var k in labelKeys) labels.Add((k, k));
            var w = PluginWidgetKit.Choice(Name(labelKey), labelKey, labels, i => Call(labelKey, () => changed?.Invoke(i)));
            w.SetValue(index);
            return w;
        }

        public IUiWidget Color(string labelKey, Color value, bool alpha, Action<Color, ApiPhase> changed)
        {
            var w = PluginWidgetKit.Color(ctx, Name(labelKey), labelKey, alpha, false, (c, i, p) => Call(labelKey, () => changed?.Invoke(c, p)));
            w.SetValue(value);
            return w;
        }

        public IUiWidget Ref(string labelKey, Func<IReadOnlyList<KeyValuePair<string, string>>> choices, string value, Action<string> changed)
        {
            IReadOnlyList<(string, string)> List()
            {
                var l = new List<(string, string)>();
                IReadOnlyList<KeyValuePair<string, string>> got = null;
                if (choices != null && live()) run("widget " + labelKey + " choices", () => got = choices());
                if (got != null) foreach (var kv in got) l.Add((kv.Key, kv.Value));
                return l;
            }
            string LabelOf(string v)
            {
                foreach (var (val, lab) in List()) if (val == v) return lab;
                return v;
            }
            var w = PluginWidgetKit.Ref(Name(labelKey), labelKey, List, LabelOf, v => Call(labelKey, () => changed?.Invoke(v)));
            w.SetValue(value);
            return w;
        }

        public IUiWidget File(string labelKey, string path, Action<string> changed)
        {
            var w = PluginWidgetKit.File(Name(labelKey), labelKey, p => Call(labelKey, () => changed?.Invoke(p)));
            w.SetValue(path);
            return w;
        }

        public IUiWidget Text(string labelKey, string value, Action<string> changed)
        {
            var w = PluginWidgetKit.Text(Name(labelKey), labelKey, false, s => Call(labelKey, () => changed?.Invoke(s)));
            w.SetValue(value);
            return w;
        }

        public IUiWidget Button(string labelKey, Action clicked) =>
            PluginWidgetKit.Button(Name(labelKey), labelKey, () => Call(labelKey, clicked));

        public IUiWidget AttachPoint(string labelKey, string value, Action<string> changed)
        {
            var c = ctx() ?? throw new InvalidOperationException("the UI does not run");
            var w = PluginWidgetKit.AttachPoint(c, Name(labelKey), labelKey, k => Call(labelKey, () => changed?.Invoke(k)));
            w.SetValue(value);
            return w;
        }

        public void Toast(string text, ToastLevel level = ToastLevel.Info)
        {
            var c = ctx();
            c?.Toasts?.Show(text, level == ToastLevel.Success ? ToastKind.Success : level == ToastLevel.Warning ? ToastKind.Warning : level == ToastLevel.Error ? ToastKind.Error : ToastKind.Info);
        }

        public void Dialog(string titleKey, string messageKey, IReadOnlyList<string> buttonLabelKeys, Action<int> closed, VisualElement content = null)
        {
            var c = ctx();
            if (c?.Dialogs == null) return;
            var spec = new DialogSpec
            {
                Id = "plugin-" + guid, Title = titleKey != null ? L10n.Get(titleKey) : null, Message = messageKey != null ? L10n.Get(messageKey) : null, Content = content,
            };
            if (buttonLabelKeys == null || buttonLabelKeys.Count == 0) spec.Buttons.Add(DialogButton.Of("0", "common.ok", true));
            else for (int i = 0; i < buttonLabelKeys.Count; i++) spec.Buttons.Add(DialogButton.Of(i.ToString(System.Globalization.CultureInfo.InvariantCulture), buttonLabelKeys[i], i == 0));
            c.Dialogs.Show(spec, id => Call("dialog " + titleKey, () => closed?.Invoke(int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int i) ? i : -1)));
        }

        public void Confirm(string titleKey, string messageKey, Action<bool> done)
        {
            var c = ctx();
            c?.Dialogs?.Confirm(titleKey, messageKey, ok => Call("confirm " + titleKey, () => done?.Invoke(ok)));
        }

        public void Prompt(string titleKey, string initial, Action<string> done)
        {
            var c = ctx();
            c?.Dialogs?.Prompt(titleKey, initial, s => Call("prompt " + titleKey, () => done?.Invoke(s)));
        }

        public void Menu(IReadOnlyList<UiMenuItem> items, VisualElement anchor)
        {
            var c = ctx();
            if (c?.Menus == null || items == null) return;
            var l = new List<MenuItem>();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null) continue;
                if (it.Separator) { l.Add(MenuItem.Sep()); continue; }
                var act = it.Run;
                string key = it.LabelKey;
                l.Add(new MenuItem
                {
                    Id = "plugin-" + guid + "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Text = key != null ? L10n.Get(key) : "",
                    Checked = it.Checked, Disabled = !it.Enabled, Run = () => Call("menu " + key, act),
                });
            }
            c.Menus.Show(l, anchor, null, "plugin-menu-" + guid);
        }
    }
}
