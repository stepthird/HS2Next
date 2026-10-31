// The FK hips position override row of the character inspector (
// position"): `chara.fk.hipsPosition` is a Vec3 or null. The row shows the stored value, else the
// effective (0, hipsOffset, 0) of the fk.set rule; a typed component writes the override with the other components kept
// (StudioOps.SetFkHips, "FK bones"); a label drag is one entry; "Clear" removes the override.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;
using UiField = HS2Next.UI.Core.FieldSpec;

namespace HS2Next.UI.Panels
{
    public sealed class HipsPositionRow : VisualElement
    {
        readonly UiContext ctx;
        readonly UiField spec;
        readonly TargetSelector selector;
        UiEdit edit;
        public readonly VectorField Field;
        public readonly Button ClearButton;
        public FieldBinding Binding { get; }
        public OpResult LastResult { get; private set; }

        public HipsPositionRow(UiContext ctx, UiField spec, TargetSelector selector)
        {
            this.ctx = ctx;
            this.spec = spec;
            this.selector = selector;
            name = "field-" + spec.Path;
            AddToClassList("hs-field");
            Field = new VectorField(L10n.Get(spec.LabelKey), 3) { Format = "0.####" };
            Loc.Bind(Field.LabelElement, spec.LabelKey);
            Field.Edited += OnEdited;
            Add(Field);
            ClearButton = InspectorUi.Button("inspector.chara.fk.hipsPositionClear", () => Write(null), "clear");
            Add(ClearButton);
            Binding = ctx.Bindings.Bind(this, selector, "chara.fk", (el, v) => Show(v.Targets));
        }

        CharaFkState Fk(string id) => (ctx.Model.Find(id)?.State as CharaNodeState)?.Fk;

        /// <summary>The shown value of one character: the override, else (0, hipsOffset, 0).</summary>
        public UnityEngine.Vector3 ValueOf(string id)
        {
            var fk = Fk(id);
            if (fk == null) return UnityEngine.Vector3.zero;
            return fk.HipsPosition is Vec3 p ? new UnityEngine.Vector3(p.X, p.Y, p.Z) : new UnityEngine.Vector3(0, fk.HipsOffset, 0);
        }

        void Show(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0) return;
            var first = ValueOf(ids[0]);
            var mixed = new bool[4];
            bool any = false;
            foreach (var id in ids)
            {
                var o = ValueOf(id);
                for (int k = 0; k < 3; k++) if (Math.Abs(o[k] - first[k]) > 1e-5f) mixed[k] = true;
                if (Fk(id)?.HipsPosition != null) any = true;
            }
            Field.SetDisplay(first, mixed);
            ClearButton.SetEnabled(any);
        }

        void OnEdited(EditPhase phase, UnityEngine.Vector4 v)
        {
            if (phase == EditPhase.Begin) { edit?.Cancel(); edit = ctx.BeginEdit(spec.OpLabel ?? OpLabels.FkBones); return; }
            if (phase == EditPhase.End) { var e = edit; edit = null; if (e != null) ctx.ShowResult(e.Commit()); Binding.MarkDirty(); return; }
            Write(v, Field.LastComponent, phase == EditPhase.Typed ? "ui.typed/" + string.Join(",", selector.Resolve(ctx)) + "/" + spec.Path : null);
        }

        /// <summary>Writes the override (component only when ≥ 0; null clears it) on every selected character.</summary>
        public OpResult Write(UnityEngine.Vector4? v, int component = -1, string mergeKey = null)
        {
            var ids = selector.Resolve(ctx);
            var o = UiContext.User(spec.OpLabel, mergeKey);
            OpResult last;
            if (v == null) last = ctx.Ops.SetFkHips(ids, null, true, null, o);
            else
            {
                last = OpResult.NothingToDo();
                using (var e = edit == null && ids.Count > 1 ? ctx.BeginEdit(spec.OpLabel ?? OpLabels.FkBones, mergeKey) : null)
                {
                    foreach (var id in ids)
                    {
                        var cur = ValueOf(id);
                        var nv = component >= 0 ? cur : new UnityEngine.Vector3(v.Value.x, v.Value.y, v.Value.z);
                        if (component >= 0) nv[component] = v.Value[component];
                        last = ctx.Ops.SetFkHips(new[] { id }, null, true, new Vec3(nv.x, nv.y, nv.z), o);
                        if (!last.Ok) break;
                    }
                    if (e != null) { if (last.Ok) last = e.Commit(); else e.Cancel(); }
                }
            }
            Binding.MarkDirty();
            return LastResult = ctx.ShowResult(last);
        }
    }
}
