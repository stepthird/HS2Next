// Localised text on elements: a LocBinding is a
// CustomBinding (updateTrigger WhenDirty) that writes L10n.Get(key) (or Format with arguments) into a text property of
// its element: TextElement.text (Label, Button), BaseField.label, Tab.label, Foldout.text, TextField placeholder. A
// language switch marks every live binding dirty (Loc.Refresh). Helpers create elements already bound:
//   Loc.Label("toolbar.save")   Loc.Button("toolbar.save", onClick)   Loc.Bind(element, key [, args])   Loc.Tooltip(el, key)
using System;
using System.Collections.Generic;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI
{
    public sealed class LocBinding : CustomBinding
    {
        public string Key;
        public object[] Args;
        /// <summary>Writes the text into the element (null = the default for its type).</summary>
        public Action<VisualElement, string> Apply;
        /// <summary>The element the text goes to (weak: the element owns the binding).</summary>
        internal WeakReference<VisualElement> Target;

        static readonly List<WeakReference<LocBinding>> live = new List<WeakReference<LocBinding>>();

        public LocBinding(string key, params object[] args)
        {
            Key = key;
            Args = args != null && args.Length > 0 ? args : null;
            updateTrigger = BindingUpdateTrigger.WhenDirty;
            lock (live) live.Add(new WeakReference<LocBinding>(this));
        }

        public string Text => Args == null ? L10n.Get(Key) : L10n.Format(Key, Args);

        protected override BindingResult Update(in BindingContext context)
        {
            Write(context.targetElement);
            return new BindingResult(BindingStatus.Success);
        }

        internal void Write(VisualElement el)
        {
            if (el == null) return;
            string t = Text;
            if (Apply != null) Apply(el, t);
            else Loc.SetText(el, t);
        }

        /// <summary>Marks every live binding dirty (language switch).</summary>
        internal static int DirtyAll()
        {
            int n = 0;
            lock (live)
            {
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (!live[i].TryGetTarget(out var b)) { live.RemoveAt(i); continue; }
                    // written at once (the WhenDirty update was not seen to run for these text bindings in batch play)
                    if (b.Target != null && b.Target.TryGetTarget(out var el)) { b.Write(el); n++; }
                    b.MarkDirty();
                }
            }
            return n;
        }
    }

    public static class Loc
    {
        /// <summary>The binding property used for the localised text (a property id per element; one per element).</summary>
        public const string Property = "text";
        const string TooltipProperty = "tooltip";

        /// <summary>Default text writer: label of fields, text of text elements, tab labels, foldout text.</summary>
        public static void SetText(VisualElement el, string t)
        {
            switch (el)
            {
                case Tab tab: tab.label = t; break;
                case Foldout f: f.text = t; break;
                case Toggle tg when tg.label != null: tg.label = t; break;
                case TextElement te: te.text = t; break;
                case BaseField<float> bf: bf.label = t; break;
                case BaseField<int> bi: bi.label = t; break;
                case BaseField<string> bs: bs.label = t; break;
                case BaseField<bool> bb: bb.label = t; break;
                case BaseField<UnityEngine.Vector3> bv: bv.label = t; break;
                default:
                    if (el != null) el.tooltip = t;
                    break;
            }
        }

        /// <summary>Binds the element's text to a key (replacing an earlier key) and sets it at once.</summary>
        public static T Bind<T>(T el, string key, params object[] args) where T : VisualElement
        {
            if (el == null) return null;
            var b = new LocBinding(key, args) { Target = new WeakReference<VisualElement>(el) };
            el.SetBinding(Property, b);
            SetText(el, b.Text);
            return el;
        }

        /// <summary>Binds with a custom writer (e.g. a composed label).</summary>
        public static T Bind<T>(T el, string key, Action<VisualElement, string> apply, params object[] args) where T : VisualElement
        {
            var b = new LocBinding(key, args) { Apply = apply, Target = new WeakReference<VisualElement>(el) };
            el.SetBinding(Property, b);
            apply(el, b.Text);
            return el;
        }

        /// <summary>A localised tooltip (shown by the Tooltips manipulator after 500 ms).</summary>
        public static T Tooltip<T>(T el, string key, params object[] args) where T : VisualElement
        {
            var b = new LocBinding(key, args) { Apply = (e, t) => e.tooltip = t, Target = new WeakReference<VisualElement>(el) };
            el.SetBinding(TooltipProperty, b);
            el.tooltip = b.Text;
            return el;
        }

        public static Label Label(string key, params object[] args) => Bind(new Label(), key, args);

        public static Button Button(string key, Action click, params object[] args) => Bind(new Button(click), key, args);

        /// <summary>Re-applies every localised text (after L10n.SetLanguage); returns the number of live bindings.</summary>
        public static int Refresh() => LocBinding.DirtyAll();
    }
}
