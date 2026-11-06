// The Jiggle section of the Advanced pose tab: the character's swing chains and the values of the selected one, then the two breast and
// the two hip chains. A row shows the scene value, or the card's value while the scene holds none; its reset takes the scene value away.
// The values are read in front of the card's: nothing here touches the card.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public sealed partial class AdvancedPoseTab
    {
        readonly List<PoseChainRow> chainRows = new List<PoseChainRow>();
        readonly List<VisualElement> chainLines = new List<VisualElement>();
        readonly List<JiggleRow> chainMembers = new List<JiggleRow>();
        readonly Dictionary<string, List<JiggleRow>> bustMembers = new Dictionary<string, List<JiggleRow>>(StringComparer.Ordinal);
        VisualElement chainList, chainEditor;
        Label noChains;
        Button chainsClear, bustClear;
        StaleList chainStale;
        string chainListKey;
        JiggleTarget chainTarget;

        /// <summary>The address of the selected chain, or null.</summary>
        public string SelectedChain => view.Chain;
        public IReadOnlyList<PoseChainRow> ChainRows => chainRows;
        public IReadOnlyList<JiggleRow> ChainMembers => chainMembers;
        public StaleList ChainStale => chainStale;

        /// <summary>The rows of a bust or hip chain, once its entry was opened; else null.</summary>
        public IReadOnlyList<JiggleRow> BustMembers(string chain) => bustMembers.TryGetValue(chain, out var l) ? l : null;

        void BuildJiggle(VisualElement into)
        {
            chainList = new VisualElement { name = "adv-chains" };
            into.Add(chainList);
            noChains = InspectorUi.Note("inspector.chara.adv.noChains");
            noChains.name = "adv-no-chains";
            into.Add(noChains);
            chainEditor = new VisualElement { name = "adv-chain-editor" };
            foreach (var f in InspectorSchemas.AdvancedPoseRows)
            {
                if (f.Path == null || !f.Path.StartsWith(InspectorSchemas.AdvChainPrefix, StringComparison.Ordinal)) continue;
                var row = new JiggleRow(this, f, typeof(ChainValues));
                chainMembers.Add(row);
                chainEditor.Add(row);
            }
            into.Add(chainEditor);
            chainsClear = InspectorUi.Button("inspector.chara.adv.resetAll", () => ctx.ShowResult(ctx.Ops.ClearChainValues(Primary, null, UiContext.User(OpLabels.ClearJiggleValues))), "adv-chains-clear");
            into.Add(chainsClear);
            chainStale = new StaleList("adv-chains-stale", key => ctx.ShowResult(ctx.Ops.ClearChainValues(Primary, new[] { key }, UiContext.User(OpLabels.ClearJiggleValues))));
            into.Add(chainStale);
            foreach (var name in BustSet.Names) into.Add(BustEntry(name));
            bustClear = InspectorUi.Button("inspector.chara.adv.resetAll", () => ctx.ShowResult(ctx.Ops.ClearBustValues(Primary, null, UiContext.User(OpLabels.ClearJiggleValues))), "adv-bust-clear");
            into.Add(bustClear);
        }

        Foldout BustEntry(string chain)
        {
            var fo = InspectorUi.Section("inspector.chara.adv." + chain, false, "adv-bust-" + chain);
            fo.RegisterValueChangedCallback(e =>
            {
                if (e.target != fo || !e.newValue || bustMembers.ContainsKey(chain)) return;
                var list = new List<JiggleRow>();
                var target = BustTarget(chain);
                foreach (var f in InspectorSchemas.AdvancedPoseRows)
                {
                    if (f.Path == null || !f.Path.StartsWith(InspectorSchemas.AdvBustPrefix, StringComparison.Ordinal)) continue;
                    var row = new JiggleRow(this, f, typeof(BustValues)) { Suffix = chain };
                    row.Bind(target);
                    list.Add(row);
                    fo.Add(row);
                }
                bustMembers[chain] = list;
            });
            return fo;
        }

        // ------------------------------------------------------------ the chain list

        void JiggleStructure(CharaRealization r)
        {
            if (chainList == null) return;
            ReadChains(r);
        }

        void ReadChains(CharaRealization r)
        {
            chainRows.Clear();
            if (r != null) chainRows.AddRange(AdvancedPoseParts.Chains(r));
            var key = new System.Text.StringBuilder();
            foreach (var c in chainRows) key.Append(c.Address).Append('|');
            string k = key.ToString();
            if (k != chainListKey)
            {
                chainListKey = k;
                chainList.Clear();
                chainLines.Clear();
                for (int i = 0; i < chainRows.Count; i++)
                {
                    int index = i;
                    var row = chainRows[i];
                    var text = new Label(PartLabel(r, row.Part) + " / " + row.Root) { name = "adv-chain-text" };
                    text.AddToClassList("hs-row__label");
                    text.style.flexGrow = 1;
                    text.style.flexShrink = 1;
                    text.style.overflow = Overflow.Hidden;
                    text.style.textOverflow = TextOverflow.Ellipsis;
                    text.tooltip = row.Address;
                    var mark = new Label("●") { name = "adv-chain-active" };
                    mark.AddToClassList("hs-row__badge");
                    var line = InspectorUi.Line(text, mark);
                    line.name = "adv-chain-" + i;
                    line.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) SelectChain(chainRows[index].Address); });
                    chainLines.Add(line);
                    chainList.Add(line);
                }
            }
            noChains.style.display = chainRows.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (view.Chain != null && chainRows.FindIndex(c => c.Address == view.Chain) < 0) view.Chain = null;
            SelectChain(view.Chain);
        }

        /// <summary>Shows the values of a chain (null: none).</summary>
        public void SelectChain(string address)
        {
            view.Chain = address;
            for (int i = 0; i < chainLines.Count && i < chainRows.Count; i++)
                chainLines[i].EnableInClassList(InspectorUi.RowSelected, chainRows[i].Address == address);
            if (chainEditor == null) return;
            chainEditor.style.display = address != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (address == null) { chainTarget = null; return; }
            if (chainTarget == null || chainTarget.Address != address) chainTarget = ChainTarget(address);
            foreach (var m in chainMembers) m.Bind(chainTarget);
        }

        JiggleTarget ChainTarget(string address) => new JiggleTarget
        {
            Address = address,
            Stored = () => { var b = Block; return b != null && b.Chains.TryGetValue(address, out var v) ? v : null; },
            Copy = () => AdvancedPoseEdits.ChainOf(Block, address),
            WriteOf = v => AdvancedPoseEdits.SetChain(Block, address, (ChainValues)v),
            Store = (v, o) => ctx.Ops.SetChainValues(Primary, address, (ChainValues)v, o),
            CanStep = () => { var b = Block; return b != null && (b.Chains.ContainsKey(address) || b.Chains.Count < AdvancedPoseLimits.MaxChains); },
            BaseOf = member =>
            {
                var r = Real;
                var ch = r != null ? AdvancedPoseParts.Chain(r, address) : null;
                if (ch == null) return null;
                var s = ch.Source;
                switch (member)
                {
                    case "enabled": return ch.BaseEnabled;
                    case "weight": return ch.BaseWeight;
                    case "damping": return s ? s.m_Damping : 0f;
                    case "elasticity": return s ? s.m_Elasticity : 0f;
                    case "stiffness": return s ? s.m_Stiffness : 0f;
                    case "inert": return s ? s.m_Inert : 0f;
                    case "radius": return s ? s.m_Radius : 0f;
                    case "gravity": return s ? V(s.m_Gravity) : Vec3.Zero;
                    case "force": return s ? V(s.m_Force) : Vec3.Zero;
                    case "freezeAxis": return s ? (int)s.m_FreezeAxis : 0;
                    default: return null;
                }
            },
        };

        JiggleTarget BustTarget(string chain) => new JiggleTarget
        {
            Address = chain,
            Stored = () =>
            {
                var b = Block;
                if (b == null) return null;
                var v = b.Bust[BustSet.KindOf(chain)];
                return v.IsEmpty ? null : v;
            },
            Copy = () => AdvancedPoseEdits.BustOf(Block, chain),
            WriteOf = v => AdvancedPoseEdits.SetBust(chain, (BustValues)v),
            Store = (v, o) => ctx.Ops.SetBustValues(Primary, chain, (BustValues)v, o),
            CanStep = () => Block != null,
            BaseOf = member =>
            {
                var stage = Real?.Motion ? Real.Motion.Get<BustStage>() : null;
                int kind = BustSet.KindOf(chain);
                var ch = stage != null && kind >= 0 && kind < stage.Chains.Length ? stage.Chains[kind] : null;
                if (ch == null) return null;
                ch.BaseSoft(0, out float d, out float e, out float s, out float i);
                switch (member)
                {
                    case "enabled": return true;
                    case "gravity": return V(ch.Gravity);
                    case "force": return ch.Source ? V(ch.Source.Force) : Vec3.Zero;
                    case "damping": return d;
                    case "elasticity": return e;
                    case "stiffness": return s;
                    case "inert": return i;
                    default: return null;
                }
            },
        };

        static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);

        // ------------------------------------------------------------ refresh

        void JiggleBlockChanged(CharaRealization r)
        {
            if (chainList == null) return;
            foreach (var m in chainMembers) m.Show();
            foreach (var kv in bustMembers) foreach (var m in kv.Value) m.Show();
            var block = Block;
            chainsClear.SetEnabled(block != null && block.Chains.Count > 0);
            bustClear.SetEnabled(block != null && !block.Bust.IsEmpty);
            chainStale.Set(AdvancedPoseDriver.Of(r)?.StaleChains);
        }

        void JiggleRefresh(CharaRealization r)
        {
            if (chainList == null) return;
            ReadChains(r);   // a rebuild of the chains keeps the addresses and replaces the chains
            JiggleBlockChanged(r);
            for (int i = 0; i < chainLines.Count && i < chainRows.Count; i++)
            {
                var mark = chainLines[i].Q<Label>("adv-chain-active");
                if (mark != null) mark.style.display = chainRows[i].Chain != null && chainRows[i].Chain.Active ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void JiggleEndEdits()
        {
            foreach (var m in chainMembers) m.EndEdit();
            foreach (var kv in bustMembers) foreach (var m in kv.Value) m.EndEdit();
        }
    }

    /// <summary>What a row of the Jiggle section edits: a swing chain or a bust chain.</summary>
    public sealed class JiggleTarget
    {
        public string Address;
        /// <summary>The values stored now, or null.</summary>
        public Func<DocObject> Stored;
        /// <summary>A copy of the stored values to change.</summary>
        public Func<DocObject> Copy;
        /// <summary>The field write of a changed copy (a step of a drag).</summary>
        public Func<DocObject, AdvancedPoseWrite?> WriteOf;
        /// <summary>The operation that stores a changed copy (one entry).</summary>
        public Func<DocObject, ExecuteOptions, OpResult> Store;
        /// <summary>False when a drag's step would pass a limit of the block: the operation then says so.</summary>
        public Func<bool> CanStep;
        /// <summary>The value without the scene's by member name, or null when the character has no such chain.</summary>
        public Func<string, object> BaseOf;
    }

    /// <summary>One member of a chain's values: the slider, vector, toggle or list its schema row names, with a reset while the scene holds a value.</summary>
    public sealed class JiggleRow : VisualElement
    {
        readonly AdvancedPoseTab tab;
        readonly UiField spec;
        readonly string member;
        readonly FieldInfo field;
        JiggleTarget target;
        UiEdit edit;

        public readonly NumberSlider Number;
        public readonly VectorField Vector;
        public readonly Toggle Check;
        public readonly DropdownField Choice;
        public readonly Button Reset;
        public OpResult LastResult { get; private set; }
        /// <summary>The bust chain of a row of a bust entry (it makes the row's name unique).</summary>
        public string Suffix { set => name = "field-" + spec.Expand(value); }

        /// <summary>The stored value, or null while the scene holds none.</summary>
        public object StoredValue
        {
            get
            {
                var v = target?.Stored();
                return v != null ? field.GetValue(v) : null;
            }
        }

        internal JiggleRow(AdvancedPoseTab tab, UiField spec, Type values)
        {
            this.tab = tab;
            this.spec = spec;
            member = spec.Path.Substring(spec.Path.LastIndexOf('.') + 1);
            field = values.GetField(char.ToUpperInvariant(member[0]) + member.Substring(1));
            name = "field-" + spec.Path;
            AddToClassList("hs-field");
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;
            VisualElement control;
            switch (spec.Kind)
            {
                case FieldKind.Float:
                    Number = new NumberSlider(L10n.Get(spec.LabelKey), spec.Min, spec.Max) { TypedMin = spec.TypedMin, TypedMax = spec.TypedMax, Format = spec.Format ?? "0.###" };
                    Loc.Bind(Number.LabelElement, spec.LabelKey);
                    Number.Edited += OnNumber;
                    control = Number;
                    break;
                case FieldKind.Vec3:
                    Vector = new VectorField(L10n.Get(spec.LabelKey), 3) { Format = spec.Format ?? "0.#####" };
                    Loc.Bind(Vector.LabelElement, spec.LabelKey);
                    Vector.Edited += OnVector;
                    control = Vector;
                    break;
                case FieldKind.Bool:
                    Check = new Toggle(L10n.Get(spec.LabelKey)) { name = "toggle" };
                    Check.AddToClassList("hs-field__toggle");
                    Loc.Bind(Check, spec.LabelKey, (e, s) => ((Toggle)e).label = s);
                    Check.RegisterValueChangedCallback(e => Store(e.newValue));
                    control = Check;
                    break;
                default:
                    var labels = new List<string>();
                    for (int i = 0; i < spec.Choices.Count; i++) labels.Add(L10n.Get(spec.ChoiceLabelKey(i)));
                    Choice = new DropdownField(L10n.Get(spec.LabelKey), labels, 0) { name = "dropdown" };
                    Loc.Bind(Choice, spec.LabelKey, (e, s) => ((DropdownField)e).label = s);
                    Choice.RegisterValueChangedCallback(e => { if (Choice.index >= 0) Store(Choice.index); });
                    control = Choice;
                    break;
            }
            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
            Add(control);
            Reset = new Button(ResetMember) { name = "adv-member-reset", text = "↺" };
            Reset.AddToClassList("hs-field__default");
            Loc.Tooltip(Reset, "inspector.chara.adv.reset");
            Add(Reset);
        }

        internal void Bind(JiggleTarget t)
        {
            if (target != t) EndEdit();
            target = t;
            Show();
        }

        internal void EndEdit()
        {
            var e = edit;
            edit = null;
            e?.Cancel();
        }

        /// <summary>The scene's value, or the card's while the scene holds none.</summary>
        internal void Show()
        {
            if (target == null || (Number != null && Number.Dragging) || edit != null) return;
            object stored = StoredValue;
            object baseValue = target.BaseOf(member);
            object v = stored ?? baseValue;
            bool over = stored != null;
            if (Number != null) Number.SetDisplay(v is float f ? f : 0f, false);
            else if (Vector != null)
            {
                var a = v is Vec3 x ? x : Vec3.Zero;
                Vector.SetDisplay(new Vector4(a.X, a.Y, a.Z, 0), null);
            }
            else if (Check != null) Check.SetValueWithoutNotify(v is bool b && b);
            else
            {
                int i = v is int k ? k : 0;
                if (i >= 0 && i < Choice.choices.Count) Choice.SetValueWithoutNotify(Choice.choices[i]);
            }
            Reset.style.display = over ? DisplayStyle.Flex : DisplayStyle.None;
            EnableInClassList(InspectorUi.Dim, !over);
            tooltip = over ? L10n.Format("inspector.chara.adv.chainOverride", Text(baseValue)) : "";
        }

        string Text(object v)
        {
            switch (v)
            {
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case Vec3 a: return a.X.ToString("0.###", CultureInfo.InvariantCulture) + ", " + a.Y.ToString("0.###", CultureInfo.InvariantCulture) + ", " + a.Z.ToString("0.###", CultureInfo.InvariantCulture);
                case bool b: return L10n.Get(b ? "common.on" : "common.off");
                case int i when spec.Choices != null && i >= 0 && i < spec.Choices.Count: return L10n.Get(spec.ChoiceLabelKey(i));
                default: return v?.ToString() ?? "";
            }
        }

        void OnNumber(EditPhase phase, double v)
        {
            switch (phase)
            {
                case EditPhase.Begin: Begin(); break;
                case EditPhase.End: End(); break;
                case EditPhase.Preview: Step((float)spec.ClampTyped(v)); break;
                default: Store((float)spec.ClampTyped(v), true); break;
            }
        }

        void OnVector(EditPhase phase, Vector4 v)
        {
            var a = new Vec3(v.x, v.y, v.z);
            switch (phase)
            {
                case EditPhase.Begin: Begin(); break;
                case EditPhase.End: End(); break;
                case EditPhase.Preview: Step(a); break;
                default:
                    if (edit == null) Store(a, true);
                    break;
            }
        }

        void Begin()
        {
            edit?.Cancel();
            edit = tab.Ctx.BeginEdit(OpLabels.JiggleValues);
        }

        void End()
        {
            var e = edit;
            edit = null;
            if (e != null) LastResult = tab.Ctx.ShowResult(e.Commit());
            tab.BlockChanged();
        }

        object CopyWith(object value)
        {
            var copy = target.Copy();
            field.SetValue(copy, value);
            return copy;
        }

        // one step of an open drag: the block's field write collapses with the steps before it
        void Step(object value)
        {
            if (target == null || edit == null || !edit.IsOpen) return;
            if (!target.CanStep()) { Store(value); return; }
            LastResult = tab.DragWrite(target.WriteOf((DocObject)CopyWith(value)), OpLabels.JiggleValues);
        }

        /// <summary>Stores a value of this member: one history entry; typed values of one member chain.</summary>
        public OpResult Store(object value, bool typed = false)
        {
            if (target == null) return OpResult.NothingToDo();
            string mergeKey = typed ? "ui.typed/" + tab.Primary + "/" + target.Address + "/" + member : null;
            LastResult = tab.Ctx.ShowResult(target.Store((DocObject)CopyWith(value), UiContext.User(OpLabels.JiggleValues, mergeKey)));
            tab.BlockChanged();
            return LastResult;
        }

        /// <summary>Takes the scene's value of this member away: the card's acts again.</summary>
        public void ResetMember()
        {
            if (target == null) return;
            LastResult = tab.Ctx.ShowResult(target.Store((DocObject)CopyWith(null), UiContext.User(OpLabels.JiggleValues)));
            tab.BlockChanged();
        }
    }
}
