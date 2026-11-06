// The host's widgets for plugins that build their own UI elements (engine side of the plugin Api; experimental: it may
// change in any minor release): the number slider, vector field, toggle, choice buttons, colour swatch with its picker,
// reference and file pickers, text field, button and attach-point picker of the host's inspector, and its toasts, dialogs
// and popup menus. The declarative forms of HS2Next.Api.Ui are drawn by the same code. Every callback runs on the main
// thread inside the plugin's fault wrapper; labels, titles and messages are keys of the plugin's strings.
using System;
using System.Collections.Generic;
using HS2Next.Api.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.Api.Unity
{
    /// <summary>The look of a toast.</summary>
    [Experimental("the host widgets may change in any minor release")]
    public enum ToastLevel
    {
        /// <summary>Plain information.</summary>
        Info = 0,
        /// <summary>Something finished well.</summary>
        Success = 1,
        /// <summary>Something needs attention.</summary>
        Warning = 2,
        /// <summary>Something failed (also written to the log as a warning).</summary>
        Error = 3,
    }

    /// <summary>One entry of a popup menu (<see cref="IUiWidgets.Menu"/>).</summary>
    [Experimental("the host widgets may change in any minor release")]
    public sealed class UiMenuItem
    {
        /// <summary>An empty entry (set the properties).</summary>
        public UiMenuItem() { }

        /// <summary>An entry with its label and action.</summary>
        public UiMenuItem(string labelKey, Action run)
        {
            LabelKey = labelKey;
            Run = run;
        }

        /// <summary>The label: a key of the plugin's strings.</summary>
        public string LabelKey { get; set; }

        /// <summary>Runs when the entry is picked (main thread, inside the plugin's fault wrapper).</summary>
        public Action Run { get; set; }

        /// <summary>The entry shows a check mark.</summary>
        public bool Checked { get; set; }

        /// <summary>False: the entry is shown greyed out and cannot be picked. Default true.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The entry is a separator line (its other members are ignored).</summary>
        public bool Separator { get; set; }
    }

    /// <summary>A host widget made for the plugin. Implemented by the host.</summary>
    [Experimental("the host widgets may change in any minor release")]
    public interface IUiWidget
    {
        /// <summary>The widget's element, to add to the plugin's own elements.</summary>
        VisualElement Element { get; }

        /// <summary>
        /// Shows <paramref name="value"/> without calling the widget's callback: a number for a number slider, a Vector4 for a
        /// vector field, a bool for a toggle, an int (the index) for a choice, a Color for a colour, a string for the pickers
        /// and the text field (a button ignores it). <paramref name="mixed"/>: the widget shows "—" (several values at once).
        /// </summary>
        void SetValue(object value, bool mixed = false);

        /// <summary>The widget takes input (false: shown greyed out).</summary>
        bool Enabled { get; set; }
    }

    /// <summary>
    /// The host's widgets for elements the plugin builds itself (<see cref="IPluginUi.Widgets"/>). Implemented by the host.
    /// Each factory returns a new widget wired to the callback; main thread only.
    /// </summary>
    [Experimental("the host widgets may change in any minor release")]
    public interface IUiWidgets
    {
        /// <summary>
        /// A number slider with a field (a slider only when both <paramref name="min"/> and <paramref name="max"/> are set): a
        /// drag reports Preview values and ends with Commit or, on Esc, Cancel; a typed value reports Commit.
        /// </summary>
        IUiWidget Number(string labelKey, double? min, double? max, double value, Action<double, EditPhase> changed, bool whole = false, string format = null);

        /// <summary>A vector field of 2, 3 or 4 components; phases as <see cref="Number"/>.</summary>
        IUiWidget Vector(string labelKey, int components, Vector4 value, Action<Vector4, EditPhase> changed);

        /// <summary>A toggle.</summary>
        IUiWidget Toggle(string labelKey, bool value, Action<bool> changed);

        /// <summary>Segmented buttons (up to four choices) or a dropdown; the callback gets the picked index.</summary>
        IUiWidget Choice(string labelKey, IReadOnlyList<string> labelKeys, int index, Action<int> changed);

        /// <summary>A colour swatch that opens the host's colour picker: Preview while it moves, then Commit, or Cancel when the picker is reverted.</summary>
        IUiWidget Color(string labelKey, Color value, bool alpha, Action<Color, EditPhase> changed);

        /// <summary>
        /// A reference picker: <paramref name="choices"/> lists (value, label) pairs when the list opens; the callback gets the
        /// picked value.
        /// </summary>
        IUiWidget Ref(string labelKey, Func<IReadOnlyList<KeyValuePair<string, string>>> choices, string value, Action<string> changed);

        /// <summary>A file picker; the callback gets the picked path.</summary>
        IUiWidget File(string labelKey, string path, Action<string> changed);

        /// <summary>A text field; the callback gets the text on Enter or when the field loses focus.</summary>
        IUiWidget Text(string labelKey, string value, Action<string> changed);

        /// <summary>A button.</summary>
        IUiWidget Button(string labelKey, Action clicked);

        /// <summary>The accessory attach-point picker; the callback gets the picked point's name.</summary>
        IUiWidget AttachPoint(string labelKey, string value, Action<string> changed);

        /// <summary>Shows a toast with <paramref name="text"/> (plain text, not a key).</summary>
        void Toast(string text, ToastLevel level = ToastLevel.Info);

        /// <summary>
        /// Shows a dialog with buttons labelled by <paramref name="buttonLabelKeys"/> and, when given, the plugin's own
        /// <paramref name="content"/>; <paramref name="closed"/> gets the index of the button that closed it (-1: closed
        /// another way).
        /// </summary>
        void Dialog(string titleKey, string messageKey, IReadOnlyList<string> buttonLabelKeys, Action<int> closed, VisualElement content = null);

        /// <summary>Shows a dialog with OK and Cancel; <paramref name="done"/> gets true for OK.</summary>
        void Confirm(string titleKey, string messageKey, Action<bool> done);

        /// <summary>Shows a dialog with a text field; <paramref name="done"/> gets the text, or null when it was cancelled.</summary>
        void Prompt(string titleKey, string initial, Action<string> done);

        /// <summary>Shows a popup menu at <paramref name="anchor"/>.</summary>
        void Menu(IReadOnlyList<UiMenuItem> items, VisualElement anchor);
    }
}
