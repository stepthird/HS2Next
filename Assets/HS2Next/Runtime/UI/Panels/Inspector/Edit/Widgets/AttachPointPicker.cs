// The parent picker of the Accessories page (XQ7): the 54 attach points in the 10
// groups of the attach point table (a group label line «髪 · 頭 · 顔 · 首 · 胴 · 腰 · 脚 · 腕 · 手 · 股間», then its keys as
// wrapping buttons labelled with HS2's parent names), the stored key marked; a stored "" (the item's default) marks the row's
// own `Parent` key and says so on the current line; a stored text that is not one of the 54 shows «不明な親: <text>» above the
// groups and is kept until a pick. A pick (on click, never on hover) is one StudioOps.EditAppearance of the accessory's
// `parent` on the row's targets ("Edit accessory"; the op validates the key and reloads the part); picking the current key
// adds nothing. «初期親» is a button of the page after the picker. Element names: the row `field-<path>`, the keys
// `acc-parent-<key>`, the unknown line `acc-parent-unknown`, the current line `acc-parent-current`.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class AttachPointPicker : VisualElement
    {
        public const string UssClass = "hs-acc-parent", KeyClass = "hs-acc-parent__key", CurrentClass = "hs-acc-parent__key--current";

        readonly UiContext ctx;
        readonly TargetSelector sel;
        readonly Func<string> defaultKey;
        readonly Dictionary<string, Button> keys = new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly Label unknown, current;

        public FieldSpec Spec { get; }
        public string Path { get; }
        public FieldBinding Binding { get; }
        /// <summary>The stored parent of the primary ("" = the item's default; null = none / mixed).</summary>
        public string Stored { get; private set; }
        /// <summary>The key marked as current (the stored key, or the row default's key for ""), or null.</summary>
        public string Marked { get; private set; }
        /// <summary>The last pick's result (tests).</summary>
        public OpResult LastResult { get; private set; }
        /// <summary>The key buttons by attach point key (54).</summary>
        public IReadOnlyDictionary<string, Button> Keys => keys;

        /// <param name="defaultKey">The worn row's `Parent` key when it is one of the 54 (what "" means), else null.</param>
        public AttachPointPicker(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath, Func<string> defaultKey)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            this.defaultKey = defaultKey ?? (() => null);
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path;
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            current = new Label { name = "acc-parent-current" };
            current.AddToClassList(InspectorUi.Dim);
            Add(current);
            unknown = new Label { name = "acc-parent-unknown" };
            unknown.AddToClassList("hs-acc-parent__unknown");
            unknown.style.display = DisplayStyle.None;
            Add(unknown);
            foreach (var g in AttachPointTable.Vanilla.Groups)
            {
                var line = new VisualElement { name = "acc-parent-group-" + g.No.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                line.AddToClassList("hs-acc-parent__group");
                var gl = new Label(L10n.Get("attach.group." + g.No));
                gl.AddToClassList("hs-acc-parent__label");
                Loc.Bind(gl, "attach.group." + g.No);
                line.Add(gl);
                foreach (var key in g.Points)
                {
                    string k = key;
                    var b = new Button(() => Pick(k)) { name = "acc-parent-" + k, text = L10n.Get("attach.point." + k) };
                    b.AddToClassList(KeyClass);
                    Loc.Bind(b, "attach.point." + k);
                    b.tooltip = k;
                    line.Add(b);
                    keys[k] = b;
                }
                Add(line);
            }
            Binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = sel, Path = Path, Spec = spec, Apply = (el, v) => Show(v) });
        }

        static string LabelOf(string key) => L10n.TryGet("attach.point." + key, out var t) ? t : key;

        void Show(BoundValues v)
        {
            style.display = v.Targets == null || v.Targets.Count == 0 || v.Empty ? DisplayStyle.None : DisplayStyle.Flex;
            Stored = v.Mixed ? null : v.Value as string ?? "";
            string def = defaultKey();
            Marked = Stored == null ? null : Stored.Length == 0 ? def : keys.ContainsKey(Stored) ? Stored : null;
            foreach (var kv in keys) kv.Value.EnableInClassList(CurrentClass, kv.Key == Marked);
            bool unknownKey = Stored != null && Stored.Length > 0 && !keys.ContainsKey(Stored);
            unknown.style.display = unknownKey ? DisplayStyle.Flex : DisplayStyle.None;
            if (unknownKey) unknown.text = L10n.Format(InspectorSchemas.AccessoryUnknownParentKey, Stored);
            current.text = v.Mixed ? "—" : Stored.Length == 0 ? L10n.Format("maker.acc.parentDefault", def != null ? LabelOf(def) : "—")
                : unknownKey ? Stored : LabelOf(Stored) + " (" + Stored + ")";
        }

        IReadOnlyList<string> Targets => MakerRowKit.Writable(ctx, Spec, sel);

        /// <summary>Picks attach point <paramref name="key"/> as its button does (one entry "Edit accessory"; tests).</summary>
        public OpResult Pick(string key)
        {
            var targets = Targets;
            if (string.IsNullOrEmpty(key) || targets.Count == 0) return LastResult = OpResult.NothingToDo();
            if (!mixedOrDifferent(key)) return LastResult = OpResult.NothingToDo();   // re-picking the stored key adds nothing
            LastResult = ctx.ShowResult(ctx.Ops.EditAppearance(targets, new List<(string, object)> { (Path, key) }, UiContext.User(Spec.OpLabel ?? OpLabels.EditAccessory)));
            Binding.MarkDirty();
            return LastResult;
        }

        bool mixedOrDifferent(string key) => Stored == null || !string.Equals(Stored, key, StringComparison.Ordinal);
    }
}
