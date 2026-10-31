// Model → UI bindings: a FieldBinding (CustomBinding, updateTrigger WhenDirty) reads a
// field path through the model's FieldAccessor for its targets (TargetSelector: the primary, every selected node of a
// kind, one node, or the scene) and hands the value(s) to its Apply delegate (a widget's SetDisplay: no change
// notification); several targets with different values give mixed = true. The BindingHub dirties a binding when a
// SceneChangeBatch touches one of its targets and its path (preview batches included, so inspectors follow drags), on
// every selection change for selector-bound bindings, and on Loaded / Cleared (all). No reflection property bags (works
// on Mono and IL2CPP). NodeBindingSource / SceneBindingSource are thin dataSource objects for code that wants one.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Binding
{
    public enum TargetKind { Primary, Selection, Node, Scene }

    /// <summary>Which model objects a binding (or an edit) addresses.</summary>
    public sealed class TargetSelector
    {
        public TargetKind Kind;
        /// <summary>Primary / Selection: only nodes of this kind (null = any).</summary>
        public NodeKind? NodeKind;
        /// <summary>Node: the node id.</summary>
        public string NodeId;

        public static TargetSelector Primary(NodeKind? kind = null) => new TargetSelector { Kind = TargetKind.Primary, NodeKind = kind };
        public static TargetSelector Selection(NodeKind? kind = null) => new TargetSelector { Kind = TargetKind.Selection, NodeKind = kind };
        public static TargetSelector Node(string id) => new TargetSelector { Kind = TargetKind.Node, NodeId = id };
        public static readonly TargetSelector Scene = new TargetSelector { Kind = TargetKind.Scene };

        /// <summary>Follows the selection (dirtied by selection changes).</summary>
        public bool IsSelectorBound => Kind == TargetKind.Primary || Kind == TargetKind.Selection;

        /// <summary>The target ids (null entries = the scene). Primary: the first selected node of the kind.</summary>
        public List<string> Resolve(UiContext ctx)
        {
            var l = new List<string>();
            switch (Kind)
            {
                case TargetKind.Scene: l.Add(null); break;
                case TargetKind.Node: if (NodeId != null && ctx.Model.Find(NodeId) != null) l.Add(NodeId); break;
                default:
                    foreach (var id in ctx.Selection.Ids)
                    {
                        var n = ctx.Model.Find(id);
                        if (n == null || (NodeKind != null && n.Kind != NodeKind.Value)) continue;
                        l.Add(id);
                        if (Kind == TargetKind.Primary) break;
                    }
                    break;
            }
            return l;
        }

        public override string ToString() => Kind + (NodeKind != null ? "(" + NodeKind + ")" : "") + (NodeId != null ? " " + NodeId : "");
    }

    /// <summary>The values a binding read: Values[i] for Targets[i] (FieldValue.Absent / null when unreadable).</summary>
    public struct BoundValues
    {
        public IReadOnlyList<string> Targets;
        public IReadOnlyList<object> Values;
        /// <summary>The first readable value (null when none).</summary>
        public object Value;
        /// <summary>Several targets disagree.</summary>
        public bool Mixed;
        /// <summary>No target, or no target has the path.</summary>
        public bool Empty;
    }

    public sealed class FieldBinding : CustomBinding
    {
        public TargetSelector Selector;
        /// <summary>Field path (expanded; FieldAccessor syntax).</summary>
        public string Path;
        /// <summary>The schema row (optional; component rows read one component).</summary>
        public FieldSpec Spec;
        /// <summary>Optional converter of each read value (model → widget value).</summary>
        public Func<object, object> Convert;
        /// <summary>Writes the value(s) into the widget without notification.</summary>
        public Action<VisualElement, BoundValues> Apply;
        internal UiContext Ctx;
        internal VisualElement Element;
        /// <summary>Update calls so far (tests).</summary>
        public int Updates { get; private set; }

        public FieldBinding() { updateTrigger = BindingUpdateTrigger.WhenDirty; }

        protected override BindingResult Update(in BindingContext context)
        {
            Updates++;
            if (Ctx == null || Apply == null) return new BindingResult(BindingStatus.Success);
            var v = Read(Ctx);
            try { Apply(context.targetElement, v); }
            catch (Exception e) { return new BindingResult(BindingStatus.Failure, e.Message); }
            return new BindingResult(BindingStatus.Success);
        }

        /// <summary>Reads the bound value(s) now.</summary>
        public BoundValues Read(UiContext ctx)
        {
            var targets = Selector != null ? Selector.Resolve(ctx) : new List<string>();
            var values = new List<object>(targets.Count);
            object first = null;
            bool have = false, mixed = false;
            foreach (var id in targets)
            {
                object v = null;
                bool ok = Path != null && FieldAccessor.TryGet(ctx.Model, id, Path, out v, out _);
                if (ok && Spec != null && Spec.Component >= 0) v = FieldValues.Component(v, Spec.Component);
                if (ok && Convert != null) v = Convert(v);
                values.Add(ok ? v : null);
                if (!ok) continue;
                if (!have) { first = v; have = true; }
                else if (!mixed && !FieldValues.Same(first, v)) mixed = true;
            }
            return new BoundValues { Targets = targets, Values = values, Value = first, Mixed = mixed, Empty = !have };
        }

        /// <summary>True when a change batch can affect this binding.</summary>
        internal bool Touched(SceneChangeBatch b, UiContext ctx)
        {
            if (Selector == null) return false;
            if (Selector.Kind == TargetKind.Scene) return b.Touches(null, Path);
            foreach (var id in Selector.Resolve(ctx)) if (b.Touches(id, Path)) return true;
            return false;
        }
    }

    /// <summary>A dataSource carrying a node target (for elements that bind through dataSource).</summary>
    public sealed class NodeBindingSource
    {
        public string NodeId;
        public NodeBindingSource(string id) { NodeId = id; }
    }

    /// <summary>A dataSource meaning "the scene settings".</summary>
    public sealed class SceneBindingSource
    {
        public static readonly SceneBindingSource Instance = new SceneBindingSource();
    }

    /// <summary>
    /// The registry of live field bindings. Bind(element, binding) sets the binding on the element (binding id "value")
    /// and tracks it while the element is attached to a panel.
    /// </summary>
    public sealed class BindingHub
    {
        readonly UiContext ctx;
        readonly HashSet<FieldBinding> live = new HashSet<FieldBinding>();
        readonly List<FieldBinding> scratch = new List<FieldBinding>();
        public int LiveCount => live.Count;
        /// <summary>Bindings dirtied so far (tests).</summary>
        public int Dirtied { get; private set; }

        public const string PropertyName = "value";

        internal BindingHub(UiContext ctx) { this.ctx = ctx; }

        /// <summary>Binds <paramref name="b"/> to <paramref name="el"/> (replacing an earlier FieldBinding of the element) and applies it once.</summary>
        public FieldBinding Bind(VisualElement el, FieldBinding b, string property = PropertyName)
        {
            b.Ctx = ctx;
            b.Element = el;
            el.SetBinding(property, b);
            if (el.panel != null) live.Add(b);
            el.RegisterCallback<AttachToPanelEvent>(_ => { live.Add(b); b.MarkDirty(); });
            el.RegisterCallback<DetachFromPanelEvent>(_ => live.Remove(b));
            // first value at once (also off-panel, for tests and the first frame)
            try { b.Apply?.Invoke(el, b.Read(ctx)); } catch (Exception e) { UnityEngine.Debug.LogWarning("[ui] binding " + b.Path + ": " + e.Message); }
            return b;
        }

        /// <summary>Bind with a new FieldBinding.</summary>
        public FieldBinding Bind(VisualElement el, TargetSelector selector, string path, Action<VisualElement, BoundValues> apply, FieldSpec spec = null) =>
            Bind(el, new FieldBinding { Selector = selector, Path = path, Apply = apply, Spec = spec });

        internal void OnChanged(SceneChangeBatch b)
        {
            scratch.Clear();
            scratch.AddRange(live);
            bool structural = b.IsStructural;
            foreach (var f in scratch)
                if ((structural && f.Selector != null && f.Selector.IsSelectorBound) || f.Touched(b, ctx)) { f.MarkDirty(); Dirtied++; }
        }

        internal void OnSelection()
        {
            foreach (var f in live) if (f.Selector != null && f.Selector.IsSelectorBound) { f.MarkDirty(); Dirtied++; }
        }

        public void DirtyAll()
        {
            foreach (var f in live) { f.MarkDirty(); Dirtied++; }
        }
    }
}
