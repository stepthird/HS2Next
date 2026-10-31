// Modal dialogs inside the panel (never OS dialogs): an overlay layer over the whole
// root (toolbar and status bar included), one dialog visible at a time (a second waits in a queue), title, message,
// optional content element, buttons. A dialog never draws outside its box: the box is at most 92 % of the window wide and
// tall, its title, message and buttons keep their size and the content (class hs-dialog__content) shrinks to the rest —
// a content that needs more room scrolls inside it (its own ScrollView). DialogSpec.BoxClass sizes a particular dialog
// (the options: hs-dialog--options).
// While a dialog is open the shortcut router passes only Enter (the default button) and Esc (the cancel button) to it
// (P's resolver gives dialog.accept / dialog.cancel). The callback receives the id of the button pressed
// (the cancel button's id for Esc; "closed" when the dialog was closed by code).
//
//   ctx.Dialogs.Show(new DialogSpec { Id = "unsaved", Title = L10n.Get("dialog.unsaved.title"), Message = …,
//       Buttons = { DialogButton.Of("save", "common.save", isDefault: true), DialogButton.Of("discard", "dialog.dontSave"),
//                   DialogButton.Cancel() } }, id => …);
//   ctx.Dialogs.Confirm("dialog.overwrite.title", "dialog.overwrite.message", ok => …, path);
using System;
using System.Collections.Generic;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Widgets
{
    public sealed class DialogButton
    {
        public string Id;
        /// <summary>l10n key of the caption (or Text when set).</summary>
        public string LabelKey;
        public string Text;
        /// <summary>Enter presses it.</summary>
        public bool IsDefault;
        /// <summary>Esc presses it.</summary>
        public bool IsCancel;

        public static DialogButton Of(string id, string labelKey, bool isDefault = false, bool isCancel = false) =>
            new DialogButton { Id = id, LabelKey = labelKey, IsDefault = isDefault, IsCancel = isCancel };
        public static DialogButton Ok() => Of("ok", "common.ok", true);
        public static DialogButton Cancel() => Of("cancel", "common.cancel", false, true);
    }

    public sealed class DialogSpec
    {
        /// <summary>Stable id (`ui.state` modal, tests).</summary>
        public string Id = "dialog";
        public string Title;
        public string Message;
        /// <summary>Extra content (a name field, a list); focused when it holds a text field.</summary>
        public VisualElement Content;
        public readonly List<DialogButton> Buttons = new List<DialogButton>();
        /// <summary>Called before the dialog closes with the button id; return false to keep it open (validation).</summary>
        public Func<string, bool> CanClose;
        /// <summary>An extra USS class on the dialog box (a sized dialog such as the options), or null.</summary>
        public string BoxClass;
        /// <summary>The element focused when the dialog opens (null: the content's text field, else the first button).</summary>
        public VisualElement FocusOnOpen;
    }

    public sealed class Dialog
    {
        public DialogSpec Spec { get; }
        public VisualElement Element { get; internal set; }
        internal Action<string> OnClose;
        public bool IsOpen { get; internal set; }
        internal Dialog(DialogSpec s, Action<string> onClose) { Spec = s; OnClose = onClose; }
    }

    public sealed class Dialogs
    {
        readonly VisualElement layer;
        readonly Queue<Dialog> queue = new Queue<Dialog>();
        public Dialog Current { get; private set; }
        public bool IsModalOpen => Current != null;
        public event Action<Dialog> Opened, Closed;

        public const string LayerClass = "hs-dialog-layer", DialogClass = "hs-dialog", ContentClass = "hs-dialog__content";

        public Dialogs(VisualElement overlayLayer)
        {
            layer = overlayLayer;
            layer.AddToClassList(LayerClass);
            layer.style.display = DisplayStyle.None;
            layer.pickingMode = PickingMode.Position;
        }

        public Dialog Show(DialogSpec spec, Action<string> onClose = null)
        {
            if (spec.Buttons.Count == 0) spec.Buttons.Add(DialogButton.Ok());
            var d = new Dialog(spec, onClose);
            if (Current != null) { queue.Enqueue(d); return d; }
            Open(d);
            return d;
        }

        /// <summary>OK / Cancel with localised title and message (message arguments formatted); the callback gets true for OK.</summary>
        public Dialog Confirm(string titleKey, string messageKey, Action<bool> done, params object[] args)
        {
            var s = new DialogSpec { Id = titleKey, Title = L10n.Get(titleKey), Message = L10n.Format(messageKey, args) };
            s.Buttons.Add(DialogButton.Ok());
            s.Buttons.Add(DialogButton.Cancel());
            return Show(s, id => done?.Invoke(id == "ok"));
        }

        /// <summary>A one-line text prompt (name for Save As, rename); the callback gets the text or null on cancel.</summary>
        public Dialog Prompt(string titleKey, string initial, Action<string> done, string messageKey = null)
        {
            var field = new TextField { value = initial ?? "", name = "dialog-input" };
            field.AddToClassList("hs-dialog__input");
            var s = new DialogSpec { Id = titleKey, Title = L10n.Get(titleKey), Message = messageKey != null ? L10n.Get(messageKey) : null, Content = field };
            s.Buttons.Add(DialogButton.Ok());
            s.Buttons.Add(DialogButton.Cancel());
            return Show(s, id => done?.Invoke(id == "ok" ? field.value : null));
        }

        void Open(Dialog d)
        {
            Current = d;
            d.IsOpen = true;
            var box = new VisualElement { name = "dialog-" + d.Spec.Id };
            box.AddToClassList(DialogClass);
            if (!string.IsNullOrEmpty(d.Spec.BoxClass)) box.AddToClassList(d.Spec.BoxClass);
            if (!string.IsNullOrEmpty(d.Spec.Title)) { var t = new Label(d.Spec.Title); t.AddToClassList("hs-dialog__title"); box.Add(t); }
            if (!string.IsNullOrEmpty(d.Spec.Message)) { var m = new Label(d.Spec.Message); m.AddToClassList("hs-dialog__message"); box.Add(m); }
            if (d.Spec.Content != null) { d.Spec.Content.AddToClassList(ContentClass); box.Add(d.Spec.Content); }
            var row = new VisualElement();
            row.AddToClassList("hs-dialog__buttons");
            foreach (var b in d.Spec.Buttons)
            {
                var bid = b.Id;
                var btn = new Button(() => Press(bid)) { name = "dialog-button-" + bid, text = b.Text ?? L10n.Get(b.LabelKey) };
                btn.AddToClassList("hs-dialog__button");
                if (b.IsDefault) btn.AddToClassList("hs-dialog__button--default");
                row.Add(btn);
            }
            box.Add(row);
            d.Element = box;
            layer.Clear();
            layer.Add(box);
            layer.style.display = DisplayStyle.Flex;
            layer.BringToFront();
            var input = d.Spec.FocusOnOpen ?? d.Spec.Content?.Q<TextField>() ?? (d.Spec.Content as TextField);
            // focus one frame later (programmatic focus loses the first IME keystroke; the delay is the S1b workaround)
            UiDefer.Frames(2, () => { if (!d.IsOpen) return; if (input != null) input.Focus(); else box.Q<Button>()?.Focus(); });
            UiContext.Guard(() => Opened?.Invoke(d));
        }

        /// <summary>Presses a button of the current dialog (also the router's Enter / Esc).</summary>
        public void Press(string id)
        {
            var d = Current;
            if (d == null) return;
            if (d.Spec.CanClose != null && !d.Spec.CanClose(id)) return;
            Close(d, id);
        }

        /// <summary>Enter: the default button (the first when none is marked).</summary>
        public void Accept()
        {
            if (Current == null) return;
            DialogButton b = null;
            foreach (var x in Current.Spec.Buttons) if (x.IsDefault) { b = x; break; }
            Press((b ?? Current.Spec.Buttons[0]).Id);
        }

        /// <summary>Esc: the cancel button (or "cancel" when none is marked).</summary>
        public void Cancel()
        {
            if (Current == null) return;
            string id = "cancel";
            foreach (var x in Current.Spec.Buttons) if (x.IsCancel) { id = x.Id; break; }
            Press(id);
        }

        /// <summary>Closes the current dialog from code (callback "closed").</summary>
        public void CloseCurrent() { if (Current != null) Close(Current, "closed"); }

        void Close(Dialog d, string id)
        {
            d.IsOpen = false;
            layer.Clear();
            layer.style.display = DisplayStyle.None;
            Current = null;
            try { d.OnClose?.Invoke(id); }
            catch (Exception e) { UnityEngine.Debug.LogError("[ui] dialog " + d.Spec.Id + " callback threw: " + e); }
            UiContext.Guard(() => Closed?.Invoke(d));
            if (Current == null && queue.Count > 0) Open(queue.Dequeue());
        }
    }
}
