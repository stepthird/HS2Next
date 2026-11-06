// UiEscTest.play, the rows and the sites with their own Esc (8.4): a part file of UiEscTest.
//   FE3  each row kind, by a label drag and, where the row has one, by a slider drag; one and two selected where the row takes two
//   FE4  a colour picker, a plugin's number and vector rows, the timeline's key drag and key editor, a tree row's drag, a list sheet
//   FE5  Esc with a text field focused and no drag; a wheel run
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Plugins;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using ApiPhase = HS2Next.Api.Ui.EditPhase;

public static partial class UiEscTest
{
    static partial void AddMoreGroups(List<(string id, string title, Func<IEnumerator> run)> groups)
    {
        groups.Add(("FE3", "each row kind", FE3));
        groups.Add(("FE4", "the sites with their own Esc", FE4));
        groups.Add(("FE5", "no drag: a text field, a wheel run", FE5));
        groups.Add(("FI13", "a hair bundle's and an accessory set's handle on a corrected transform", FI13));
    }

    static IEnumerator Reveal(VisualElement e)
    {
        bool any = false;
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is ScrollView sv) { sv.ScrollTo(e); any = true; }
        if (any) yield return Frames(3);
    }


    /// <summary>
    /// A vector row's label drag with real deltas: press, 3 moves, Esc, moves over the label and away and back, the release over the label,
    /// a focus and a blur; then a new drag: one entry. <paramref name="shown"/>: the value the field must show after Esc (null: mixed).
    /// </summary>
    static IEnumerator EscOnVector(string what, VectorField vf, int c, Func<float[]> read, float eps, string entry, Func<float> shown)
    {
        var f = vf.Fields[c];
        var label = f.labelElement;
        yield return Reveal(label);
        Vector2 at = label.worldBound.center;
        if (!Check(what + ": the axis label is under the pointer", Panel.Pick(at) == label, Name(Panel.Pick(at) as VisualElement) + " at " + at)) yield break;
        var v0 = read();
        int u0 = Undos;
        bool mixed0 = f.showMixedValue;
        Raw(EventType.MouseDown, at);
        Check(what + ": the press: the label holds the pointer, an edit is open", Capturing() == label && vf.Dragging && Ui.ActiveEdit != null && Ui.ActiveEdit.IsOpen, Name(Capturing()));
        for (int i = 1; i <= 3; i++) { Raw(EventType.MouseDrag, at + new Vector2(i, 0), new Vector2(6, 0)); yield return Frames(1); }
        Check(what + ": the drag writes", !Same(read(), v0, eps), Show(v0) + " -> " + Show(read()));
        EscNow();
        Check(what + " Esc: no pointer held, no drag, no open edit", Capturing() == null && !vf.Dragging && (Ui.ActiveEdit == null || !Ui.ActiveEdit.IsOpen), Name(Capturing()) + ", dragging " + vf.Dragging);
        Check(what + " Esc: the values as at the press, no entry", Same(read(), v0, eps) && Undos == u0, Show(read()) + ", entries " + (Undos - u0));
        yield return Frames(3);
        if (shown != null) Check(what + " Esc: the display shows the model", Mathf.Abs(f.value - shown()) <= eps && f.showMixedValue == mixed0, f.value + " / " + shown());
        else Check(what + " Esc: the display is mixed again", f.showMixedValue == mixed0, f.showMixedValue + " / " + mixed0);
        string text0 = f.text;
        Raw(EventType.MouseDrag, at + new Vector2(1, 0), new Vector2(4, 0));
        Raw(EventType.MouseDrag, at, new Vector2(4, 0));
        Raw(EventType.MouseDrag, at + new Vector2(300, 200), new Vector2(4, 0));
        Raw(EventType.MouseDrag, at, new Vector2(4, 0));
        yield return Frames(1);
        Check(what + " moves after Esc: nothing", Same(read(), v0, eps) && Undos == u0 && f.text == text0, Show(read()) + ", entries " + (Undos - u0) + ", text " + f.text + " / " + text0);
        Raw(EventType.MouseUp, at);
        yield return Frames(1);
        Check(what + " the release after Esc: nothing", Same(read(), v0, eps) && Undos == u0 && f.text == text0, Show(read()) + ", entries " + (Undos - u0));
        f.Focus();
        yield return Frames(1);
        f.Blur();
        yield return Frames(2);
        Check(what + " a later blur types nothing", Same(read(), v0, eps) && Undos == u0, Show(read()) + ", entries " + (Undos - u0));
        Raw(EventType.MouseDown, at);
        for (int i = 1; i <= 2; i++) { Raw(EventType.MouseDrag, at + new Vector2(i, 0), new Vector2(6, 0)); yield return Frames(1); }
        Raw(EventType.MouseUp, at);
        yield return Frames(2);
        f.Blur();
        yield return Frames(1);
        Check(what + " a new drag afterwards: one entry", Undos == u0 + 1 && !Same(read(), v0, eps) && (entry == null || TopLabel == entry), (Undos - u0) + " / " + TopLabel + ", " + Show(read()));
        while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
        yield return Frames(3);
        Check(what + " undo: the values as at the press", Same(read(), v0, eps), Show(read()));
    }

    /// <summary>A number row's drag on its label or on its slider: press, 3 moves, Esc, moves, release; then a new drag: one entry (entry null: no history in this row).</summary>
    static IEnumerator EscOnNumber(string what, NumberSlider ns, bool onSlider, Func<float[]> read, float eps, string entry, Func<float> shown, bool recorded = true)
    {
        VisualElement grip = onSlider ? (ns.Slider?.Q("unity-drag-container") ?? (VisualElement)ns.Slider) : ns.LabelElement;
        if (!Check(what + ": the element to press", grip != null)) yield break;
        yield return Reveal(grip);
        Vector2 at = grip.worldBound.center;
        var picked = Panel.Pick(at) as VisualElement;
        if (!Check(what + ": it is under the pointer", picked != null && (picked == grip || grip.Contains(picked)), Name(picked) + " at " + at)) yield break;
        var v0 = read();
        int u0 = Undos;
        Raw(EventType.MouseDown, at);
        for (int i = 1; i <= 3; i++) { Raw(EventType.MouseDrag, at + new Vector2(i * 9, 0), new Vector2(9, 0)); yield return Frames(1); }
        Check(what + ": the drag runs and writes", ns.Dragging && Capturing() != null && !Same(read(), v0, eps), ns.Dragging + ", " + Name(Capturing()) + ", " + Show(v0) + " -> " + Show(read()));
        EscNow();
        Check(what + " Esc: no pointer held, no drag, no open edit", Capturing() == null && !ns.Dragging && (Ui.ActiveEdit == null || !Ui.ActiveEdit.IsOpen), Name(Capturing()) + ", dragging " + ns.Dragging);
        Check(what + " Esc: the values as at the press, no entry", Same(read(), v0, eps) && Undos == u0, Show(read()) + ", entries " + (Undos - u0));
        yield return Frames(3);
        if (shown != null) Check(what + " Esc: the display shows the model", Mathf.Abs((float)ns.Value - shown()) <= Mathf.Max(eps, 1e-3f), ns.Value + " / " + shown());
        Raw(EventType.MouseDrag, at + new Vector2(30, 0), new Vector2(3, 0));
        Raw(EventType.MouseDrag, at + new Vector2(40, 0), new Vector2(10, 0));
        Raw(EventType.MouseDrag, at + new Vector2(50, 0), new Vector2(10, 0));
        yield return Frames(1);
        Check(what + " moves after Esc: nothing", Same(read(), v0, eps) && Undos == u0, Show(read()) + ", entries " + (Undos - u0));
        Raw(EventType.MouseUp, at + new Vector2(50, 0));
        yield return Frames(2);
        Check(what + " the release after Esc: nothing", Same(read(), v0, eps) && Undos == u0 && !ns.Dragging, Show(read()) + ", entries " + (Undos - u0));
        Raw(EventType.MouseDown, at);
        for (int i = 1; i <= 2; i++) { Raw(EventType.MouseDrag, at + new Vector2(i * 9, 0), new Vector2(9, 0)); yield return Frames(1); }
        Raw(EventType.MouseUp, at + new Vector2(18, 0));
        yield return Frames(2);
        if (recorded)
        {
            Check(what + " a new drag afterwards: one entry", Undos == u0 + 1 && !Same(read(), v0, eps) && (entry == null || TopLabel == entry), (Undos - u0) + " / " + TopLabel + ", " + Show(read()));
            while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
            yield return Frames(3);
            Check(what + " undo: the values as at the press", Same(read(), v0, eps), Show(read()));
        }
        else Check(what + " a new drag afterwards writes, no entry", Undos == u0 && !Same(read(), v0, eps), (Undos - u0) + ", " + Show(read()));
    }

    static IEnumerator FE3()
    {
        yield return OkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        yield return Fe3Field();
        yield return Fe3Inspector();
        yield return Fe3Advanced();
        yield return Fe3Maker();
        yield return Fe3Fov();
        yield return OkCall(new { cmd = "scene.pause", paused = false }, "the pause off");
    }

    static IEnumerator Fe3Field()
    {
        var a = Ui.Ops.AddFolder(null, null, -1, UiContext.User());
        var b = Ui.Ops.AddFolder(null, null, -1, UiContext.User());
        if (!Check("two folders", a.Ok && b.Ok)) yield break;
        string ia = a.NewIds[0], ib = b.NewIds[0];
        Ui.Session.Set(ia, "transform.pos", new Vec3(1f, 2f, 3f), UiContext.User());
        Ui.Session.Set(ib, "transform.pos", new Vec3(4f, 5f, 6f), UiContext.User());
        Vec3 P(string id) => (Vec3)FieldAccessor.Get(Ui.Model, id, "transform.pos");
        float[] One() => Vals(P(ia));
        float[] Two() => Vals(P(ia), P(ib));

        var vecSpec = InspectorSchemas.Transform.Fields.First(f => f.Path == "transform.pos");
        var numSpec = new FieldSpec { Path = "transform.pos", Kind = FieldKind.Float, Component = 1, Min = -50, Max = 50, OpLabel = OpLabels.Transform, LabelKey = "inspector.transform.pos" };

        Ui.Ops.Select(new[] { ia });
        var row = PlaceRow(vecSpec, TargetSelector.Selection());
        yield return Frames(3);
        yield return EscOnVector("schema vector row, one selected", (VectorField)row.Control, 1, One, 1e-5f, OpLabels.Transform, () => P(ia).Y);
        var nrow = PlaceRow(numSpec, TargetSelector.Selection());
        nrow.style.top = 180;
        yield return Frames(3);
        yield return EscOnNumber("schema number row, label, one selected", (NumberSlider)nrow.Control, false, One, 1e-5f, OpLabels.Transform, () => P(ia).Y);
        yield return EscOnNumber("schema number row, slider, one selected", (NumberSlider)nrow.Control, true, One, 1e-5f, OpLabels.Transform, () => P(ia).Y);

        Ui.Ops.Select(new[] { ia, ib });
        yield return Frames(3);
        yield return EscOnVector("schema vector row, two selected, a mixed component", (VectorField)row.Control, 1, Two, 1e-5f, OpLabels.Transform, null);
        yield return EscOnNumber("schema number row, label, two selected", (NumberSlider)nrow.Control, false, Two, 1e-5f, OpLabels.Transform, null);
        yield return EscOnNumber("schema number row, slider, two selected", (NumberSlider)nrow.Control, true, Two, 1e-5f, OpLabels.Transform, null);

        row.RemoveFromHierarchy();
        nrow.RemoveFromHierarchy();
        Ui.Ops.Select(Array.Empty<string>());
        Ui.Ops.Delete(new[] { ia, ib }, UiContext.User());
        yield return Frames(2);
    }

    static InspectorPanel Insp => Panel.visualTree.Q<InspectorPanel>();

    static IEnumerator OpenTab(string tabId, params string[] ids)
    {
        Ui.Ops.Select(ids);
        Ui.Shell.ShowPanel("inspector");
        yield return Frames(3);
        Insp?.Refresh(true);
        yield return Frames(2);
        Insp?.Chara?.ShowTab(tabId);
        yield return Frames(3);
    }

    static IEnumerator Fe3Inspector()
    {
        yield return OkCall(new { cmd = "ik.enable", key = charaA, on = true }, "IK on, first");
        yield return OkCall(new { cmd = "ik.enable", key = charaB, on = true }, "IK on, second");
        yield return OkCall(new { cmd = "fk.enable", key = charaA, on = true }, "FK on, first");
        yield return OkCall(new { cmd = "fk.enable", key = charaB, on = true }, "FK on, second");
        yield return Frames(4);

        foreach (var two in new[] { false, true })
        {
            string tail = two ? ", two selected" : ", one selected";
            var ids = two ? new[] { charaA, charaB } : new[] { charaA };
            yield return OpenTab("kinematics", ids);
            var ik = Panel.visualTree.Query<IkTargetRow>().ToList().FirstOrDefault(r => r.name == "field-chara.ik.targets.LeftHand.pos");
            if (Check("the IK target row" + tail, ik != null))
            {
                float[] Read() => ids.SelectMany(id => Vals(ik.ValueOf(id))).ToArray();
                yield return EscOnVector("IK target row" + tail, ik.Field, 1, Read, 1e-4f, OpLabels.IkTargets, two ? (Func<float>)null : () => ik.ValueOf(charaA).y);
            }
            var hips = Panel.visualTree.Query<HipsPositionRow>().ToList().FirstOrDefault();
            if (Check("the hips position row" + tail, hips != null))
            {
                float[] Read() => ids.SelectMany(id => Vals(hips.ValueOf(id))).ToArray();
                yield return EscOnVector("hips position row" + tail, hips.Field, 1, Read, 1e-5f, OpLabels.FkBones, two ? (Func<float>)null : () => hips.ValueOf(charaA).y);
            }
        }
        yield return OkCall(new { cmd = "ik.enable", key = charaA, on = false }, "IK off, first");
        yield return OkCall(new { cmd = "ik.enable", key = charaB, on = false }, "IK off, second");
        yield return OkCall(new { cmd = "fk.enable", key = charaA, on = false }, "FK off, first");
        yield return OkCall(new { cmd = "fk.enable", key = charaB, on = false }, "FK off, second");
        yield return Frames(3);
    }

    static IEnumerator Fe3Advanced()
    {
        const string finger = "cf_J_Hand_Index02_L";
        yield return OpenTab(AdvancedPoseTab.TabId, charaA);
        var tab = AdvancedPoseTab.Current;
        if (!Check("the Advanced pose tab", tab != null && tab.Primary == charaA)) yield break;
        yield return OkCall(new { cmd = "select", sub = charaA + "/bone/" + finger }, "select the bone");
        yield return Frames(3);
        tab.Refresh();
        yield return Frames(2);
        CharaAdvancedPose Block() => StateOf(charaA).AdvancedPose;
        float[] BoneRead() => Block() != null && Block().Bones.TryGetValue(finger, out var c) ? Vals(c.Pos, c.Rot, c.Scale) : Vals(Vec3.Zero, Vec3.Zero, Vec3.One);
        yield return EscOnVector("bone row, position", tab.PosRow.Field, 1, BoneRead, 1e-5f, OpLabels.BoneCorrection, () => BoneRead()[1]);
        yield return EscOnVector("bone row, rotation", tab.RotRow.Field, 1, BoneRead, 0.01f, OpLabels.BoneCorrection, () => BoneRead()[4]);
        yield return EscOnVector("bone row, scale", tab.ScaleRow.Field, 1, BoneRead, 1e-5f, OpLabels.BoneCorrection, () => BoneRead()[7]);
        yield return OkCall(new { cmd = "pose.bones.clear", key = charaA, all = true }, "clear");
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character alone");
        yield return Frames(3);

        // jiggle: a chain's number and vector rows
        tab = AdvancedPoseTab.Current;
        var fold = Panel.visualTree.Q("adv-section-jiggle") as Foldout;
        if (fold != null) fold.value = true;
        yield return Frames(3);
        tab.Refresh();
        yield return Frames(2);
        if (Check("BR-Chan has swing chains", tab.ChainRows.Count > 0))
        {
            string address = tab.ChainRows[0].Address;
            yield return Reveal(Panel.visualTree.Q("adv-chain-0"));
            yield return Call(ClickOn("adv-chain-0"));
            yield return Frames(3);
            Check("a chain selected", tab.SelectedChain == address, tab.SelectedChain);
            var damping = tab.ChainMembers.FirstOrDefault(m => m.name.EndsWith(".damping", StringComparison.Ordinal));
            var gravity = tab.ChainMembers.FirstOrDefault(m => m.Vector != null);
            float[] ChainRead()
            {
                var c = Block() != null && Block().Chains.TryGetValue(address, out var v) ? v : null;
                var g = c?.Gravity ?? Vec3.Zero;
                return new[] { c?.Damping ?? -1f, c?.Gravity != null ? 1f : 0f, g.X, g.Y, g.Z, c?.Force?.X ?? 0f, c?.Force?.Y ?? 0f, c?.Force?.Z ?? 0f };
            }
            if (Check("the damping row", damping?.Number != null))
            {
                yield return EscOnNumber("jiggle number row, label", damping.Number, false, ChainRead, 1e-5f, OpLabels.JiggleValues, () => (float)damping.Number.Value);
                yield return EscOnNumber("jiggle number row, slider", damping.Number, true, ChainRead, 1e-5f, OpLabels.JiggleValues, () => (float)damping.Number.Value);
            }
            if (Check("a vector row", gravity != null))
                yield return EscOnVector("jiggle vector row", gravity.Vector, 0, ChainRead, 1e-5f, OpLabels.JiggleValues, () => gravity.Vector.Fields[0].value);
        }

        // blend shapes: a mesh's slider
        var blend = Panel.visualTree.Q("adv-section-blend") as Foldout;
        if (blend != null) blend.value = true;
        yield return Frames(3);
        tab.Refresh();
        yield return Frames(2);
        const string mesh = "head/o_tooth";
        var field = Panel.visualTree.Q("adv-blend-renderer") as DropdownField;
        int ri = tab.RendererRows.ToList().FindIndex(r => r.Address == mesh);
        if (Check("o_tooth is offered", ri >= 0 && field != null))
        {
            field.value = field.choices[ri];
            yield return Frames(3);
            string shape = tab.ShapeRows[0].Name;
            var row = tab.BlendList.Query<BlendRow>().ToList().FirstOrDefault(r => r.name == "adv-blend-row-" + shape);
            float[] BlendRead() => Block() != null && Block().BlendShapes.TryGetValue(mesh, out var e) && e.Weights.TryGetValue(shape, out float w) ? new[] { 1f, w } : new[] { 0f, 0f };
            if (Check("the row of the first shape", row != null))
            {
                yield return EscOnNumber("blend row, label", row.Slider, false, BlendRead, 1e-4f, OpLabels.BlendShapes, null);
                row = tab.BlendList.Query<BlendRow>().ToList().FirstOrDefault(r => r.name == "adv-blend-row-" + shape);
                yield return EscOnNumber("blend row, slider", row.Slider, true, BlendRead, 1e-4f, OpLabels.BlendShapes, null);
            }
        }
        yield return OkCall(new { cmd = "pose.blendShapes.clear", key = charaA, all = true }, "clear the weights");
        yield return OkCall(new { cmd = "pose.chains.clear", key = charaA, all = true }, "clear the chains");
        yield return Frames(2);
    }

    /// <summary>ui.input: a click on a named element.</summary>
    static Newtonsoft.Json.Linq.JObject ClickOn(string name) => new Newtonsoft.Json.Linq.JObject
    {
        ["cmd"] = "ui.input",
        ["events"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject { ["type"] = "pointer", ["target"] = name }),
        ["frames"] = 1,
    };

    static IEnumerator Fe3Maker()
    {
        // a Maker slider row, label and slider, one and two selected
        var tab = OpenEdit(charaA, "body");
        yield return Frames(4);
        if (!Check("the Edit tab on the Body page", tab != null && tab.panel != null)) yield break;
        var sliders = tab.Query<MakerSliderRow>().ToList().Where(r => r.Spec.Gate == null && r.resolvedStyle.display != DisplayStyle.None && r.Control.Slider != null).ToList();
        var maker = sliders.FirstOrDefault();
        if (Check("a Maker slider row", maker != null, sliders.Count + " rows"))
        {
            float Stored(string id) => (float)FieldValues.ToDouble(FieldAccessor.Get(Ui.Model, id, maker.Path));
            float[] Read1() => new[] { Stored(charaA) };
            Info("Maker row " + maker.Path);
            yield return EscOnNumber("Maker slider row, label, one selected", maker.Control, false, Read1, 1e-5f, null, () => (float)maker.Spec.ToDisplay(Stored(charaA)));
            yield return EscOnNumber("Maker slider row, slider, one selected", maker.Control, true, Read1, 1e-5f, null, () => (float)maker.Spec.ToDisplay(Stored(charaA)));
        }

        // an accessory's transform row: an axis label's drag; a step button held
        tab = OpenEdit(charaA, "accessories");
        yield return Frames(4);
        string acc = null;
        var r = Real(charaA);
        foreach (var slot in r.Context.Accessories)
        {
            if (slot == null || !slot.Object) continue;
            var cmp = slot.Object.GetComponent<AIChara.CmpAccessory>();
            string aid = r.AccessoryIdOf(slot);
            if (cmp != null && cmp.trfMove01 && aid != null) { acc = aid; break; }
        }
        if (Check("BR-Chan has an accessory with a transform set", acc != null))
        {
            tab.SelectAccessory(acc);
            yield return Frames(3);
            int shownSlot = tab.AccessoryShownSlot;
            var fold = tab.Query<Foldout>("edit-section-" + InspectorSchemas.AccessorySectionId(shownSlot, "adjust")).ToList().FirstOrDefault();
            if (fold != null) fold.value = true;
            yield return Frames(3);
            string path = InspectorSchemas.AccessoryEditPath(acc, "moves[0].position");
            var vec = tab.Query<MakerVectorRow>().ToList().FirstOrDefault(x => x.Path == path);
            if (Check("the set's position row", vec != null, path))
            {
                float[] Read() => Vals((Vec3)FieldAccessor.Get(Ui.Model, charaA, path));
                var v0 = Read();
                int u0 = Undos;
                var axisEl = tab.Query<VisualElement>("field-" + path + "#1").ToList().FirstOrDefault();
                var axis = axisEl?.Q<Label>("axis");
                yield return Reveal(axis);
                Vector2 at = axis.worldBound.center;
                Check("the axis label is under the pointer", Panel.Pick(at) == axis, Name(Panel.Pick(at) as VisualElement));
                Raw(EventType.MouseDown, at);
                for (int i = 1; i <= 3; i++) { Raw(EventType.MouseDrag, at + new Vector2(i * 6, 0), new Vector2(6, 0)); yield return Frames(1); }
                Check("Maker vector row, label: the drag writes", vec.Holding && !Same(Read(), v0, 1e-5f), Show(Read()));
                EscNow();
                Check("Maker vector row, label, Esc: no pointer held, no hold, the values back, no entry", Capturing() == null && !vec.Holding && Same(Read(), v0, 1e-5f) && Undos == u0, Name(Capturing()) + ", " + Show(Read()) + ", entries " + (Undos - u0));
                Raw(EventType.MouseDrag, at + new Vector2(30, 0), new Vector2(6, 0));
                Raw(EventType.MouseDrag, at + new Vector2(40, 0), new Vector2(10, 0));
                yield return Frames(1);
                Raw(EventType.MouseUp, at + new Vector2(40, 0));
                yield return Frames(2);
                Check("Maker vector row, label: moves and the release after Esc: nothing", Same(Read(), v0, 1e-5f) && Undos == u0, Show(Read()) + ", entries " + (Undos - u0));

                var plus = axisEl?.Q<VisualElement>("plus");
                yield return Reveal(plus);
                Vector2 pt = plus.worldBound.center;
                Raw(EventType.MouseDown, pt);
                yield return PlayModeHarness.WaitRealtime(0.5);
                Check("Maker vector row, held step: it steps", vec.Holding && vec.HoldSteps >= 2 && !Same(Read(), v0, 1e-5f), vec.HoldSteps + " steps, " + Show(Read()));
                EscNow();
                Check("Maker vector row, held step, Esc: the values back, no entry", !vec.Holding && Same(Read(), v0, 1e-5f) && Undos == u0, Show(Read()) + ", entries " + (Undos - u0));
                int steps = vec.HoldSteps;
                // the hand stays still: the capture's end has not come; the timer must have stopped
                yield return PlayModeHarness.WaitRealtime(0.4);
                Check("Maker vector row, held step: no step after Esc with a still hand", vec.HoldSteps == steps && Same(Read(), v0, 1e-5f) && Undos == u0, (vec.HoldSteps - steps) + " steps, " + Show(Read()) + ", entries " + (Undos - u0));
                Raw(EventType.MouseUp, pt);
                yield return Frames(2);
                Check("Maker vector row, held step: the release after Esc: nothing", Same(Read(), v0, 1e-5f) && Undos == u0, Show(Read()));
            }
        }

        // a hair bundle's slider
        tab = OpenEdit(charaA, "hair");
        yield return Frames(4);
        var bfold = tab.Query<Foldout>("edit-section-hair.hair.back.bundles").ToList().FirstOrDefault();
        if (bfold != null) bfold.value = true;
        yield return Frames(3);
        var list = HairBundleList.Current;
        int index = list != null ? list.HandleIndices().DefaultIfEmpty(-1).First() : -1;
        if (Check("a hair bundle", index >= 0))
        {
            var g = list.GroupOf(index) as Foldout;
            if (g != null) g.value = true;
            yield return Frames(3);
            var ns = g?.Query<NumberSlider>().ToList().FirstOrDefault(x => x.Slider != null);
            float[] Read() => list.EffectiveOf(charaA).SelectMany(e => Vals(e.Move, e.Rotate)).ToArray();
            if (Check("a bundle's slider row", ns != null))
            {
                yield return EscOnNumber("hair bundle slider, label", ns, false, Read, 1e-5f, OpLabels.EditHair, null);
                yield return EscOnNumber("hair bundle slider, slider", ns, true, Read, 1e-5f, OpLabels.EditHair, null);
            }
        }

        // two selected on a Maker slider row
        Ui.Ops.Select(new[] { charaA, charaB });
        yield return Frames(4);
        tab = OpenEdit(charaA, "body");
        Ui.Ops.Select(new[] { charaA, charaB });
        yield return Frames(4);
        tab = CharaEditTab.Current ?? tab;
        var two = tab?.Query<MakerSliderRow>().ToList().FirstOrDefault(x => x.Spec.Gate == null && x.resolvedStyle.display != DisplayStyle.None && x.Control.Slider != null);
        if (two != null)
        {
            float Stored(string id) => (float)FieldValues.ToDouble(FieldAccessor.Get(Ui.Model, id, two.Path));
            float[] Read2() => new[] { Stored(charaA), Stored(charaB) };
            yield return EscOnNumber("Maker slider row, label, two selected", two.Control, false, Read2, 1e-5f, null, null);
            yield return EscOnNumber("Maker slider row, slider, two selected", two.Control, true, Read2, 1e-5f, null, null);
        }
        else Warn("no Maker slider row on two selected: not run");
        Ui.Ops.Select(new[] { charaA });
        yield return Frames(3);
    }

    static IEnumerator Fe3Fov()
    {
        Ui.Shell.ShowPanel(CameraListPanel.PanelId);
        yield return Frames(3);
        var panel = Ui.Shell.PanelRoot(CameraListPanel.PanelId)?.Q<CameraListPanel>();
        var row = panel?.Fov;
        if (!Check("the cameras panel's field of view row", row != null)) yield break;
        var cc = Ui.Viewport.Camera;
        float[] Free() => new[] { cc.Current.Fov };
        // the free camera: view state, no history
        yield return EscOnNumber("field of view, free camera, slider", row.Slider, true, Free, 1e-3f, null, () => cc.Current.Fov, false);
        cc.SetFov(23f);
        yield return Frames(3);
        yield return EscOnNumber("field of view, free camera, label", row.Slider, false, Free, 1e-3f, null, () => cc.Current.Fov, false);
        cc.SetFov(23f);
        yield return Frames(3);
        yield return FovPopup("free camera", Free);
        cc.SetFov(23f);
        yield return Frames(3);
        // a camera object: one entry
        var add = Ui.Ops.AddCameraFromView(null, false, UiContext.User());
        string cam = add?.NewIds.FirstOrDefault();
        if (Check("a camera object", cam != null))
        {
            Ui.Ops.SetActiveCamera(cam, UiContext.User());
            yield return Frames(3);
            row.Refresh();
            float[] Fov() => new[] { (Ui.Model.Find(cam)?.State as CameraNodeState)?.Fov ?? -1f };
            yield return EscOnNumber("field of view, camera object, slider", row.Slider, true, Fov, 1e-3f, OpLabels.Camera, () => Fov()[0]);
            yield return EscOnNumber("field of view, camera object, label", row.Slider, false, Fov, 1e-3f, OpLabels.Camera, () => Fov()[0]);
            yield return FovPopup("camera object", Fov);
            Ui.Ops.SetActiveCamera(null, UiContext.User());
            Ui.Ops.Select(Array.Empty<string>());
            Ui.Ops.Delete(new[] { cam }, UiContext.User());
            yield return Frames(2);
        }
        Ui.Shell.ShowPanel("inspector");
        yield return Frames(2);
    }

    /// <summary>The viewport's popup: its own Esc closes it during a drag on its slider; the drag commits nothing.</summary>
    static IEnumerator FovPopup(string what, Func<float[]> read)
    {
        var vp = Ui.Viewport;
        vp.ToggleFovPopup();
        yield return Frames(3);
        var row = vp.FovPopup?.Q<FovRow>();
        if (Check("the popup's row, " + what, row != null))
        {
            var grip = row.Slider.Slider.Q("unity-drag-container") ?? (VisualElement)row.Slider.Slider;
            Vector2 at = grip.worldBound.center;
            var v0 = read();
            int u0 = Undos;
            Raw(EventType.MouseDown, at);
            Raw(EventType.MouseDrag, at + new Vector2(20, 0), new Vector2(20, 0));
            yield return Frames(1);
            Check("the popup's slider writes, " + what, !Same(read(), v0, 1e-3f), Show(read()));
            EscNow();
            yield return Frames(2);
            Check("the popup, " + what + ", Esc: closed, the value back, no entry", vp.FovPopup == null && Same(read(), v0, 1e-3f) && Undos == u0, (vp.FovPopup == null) + ", " + Show(read()) + ", entries " + (Undos - u0));
            Raw(EventType.MouseDrag, at + new Vector2(30, 0), new Vector2(10, 0));
            Raw(EventType.MouseUp, at + new Vector2(30, 0));
            yield return Frames(2);
            Check("the popup, " + what + ": moves and the release after Esc: nothing", Same(read(), v0, 1e-3f) && Undos == u0, Show(read()) + ", entries " + (Undos - u0));
        }
        if (vp.FovPopup != null) vp.CloseFovPopup();
        yield return Frames(2);
    }

    // ---------------------------------------------------------------- FE4

    static IEnumerator FE4()
    {
        yield return Fe4Colour();
        yield return Fe4Plugin();
        yield return Fe4Tree();
        yield return Fe4BrowserRow();
        yield return Fe4Sheet();
        yield return Fe4Timeline();
    }

    static IEnumerator Fe4Colour()
    {
        var tab = OpenEdit(charaA, "hair");
        yield return Frames(4);
        var row = tab?.Query<MakerColorRow>().ToList().FirstOrDefault(r => r.resolvedStyle.display != DisplayStyle.None);
        if (!Check("a Maker colour row", row != null)) yield break;
        string path = row.Path;
        string Read() => InspectorValues.Text(FieldAccessor.Get(Ui.Model, charaA, path));
        string v0 = Read();
        int u0 = Undos;
        Check("the picker opens", row.OpenPicker());
        yield return Frames(4);
        var picker = row.Picker;
        var sv = picker?.Q(className: "hs-colorpicker__sv");
        if (Check("the picker's square", sv != null))
        {
            Vector2 at = sv.worldBound.center;
            Raw(EventType.MouseDown, at);
            Raw(EventType.MouseDrag, at + new Vector2(12, 8), new Vector2(12, 8));
            yield return Frames(1);
            Check("the square's drag writes", Read() != v0 && Capturing() == sv, v0 + " -> " + Read() + ", " + Name(Capturing()));
            EscNow();
            Check("the colour picker, Esc: closed, the value back, no entry", row.Picker == null && Read() == v0 && Undos == u0, (row.Picker == null) + ", " + Read() + ", entries " + (Undos - u0));
            Raw(EventType.MouseDrag, at + new Vector2(20, 10), new Vector2(8, 2));
            Raw(EventType.MouseUp, at + new Vector2(20, 10));
            yield return Frames(2);
            Check("the colour picker: moves and the release after Esc: nothing", Read() == v0 && Undos == u0, Read() + ", entries " + (Undos - u0));
        }
        row.Picker?.Close(false);
        yield return Frames(2);
    }

    static IEnumerator Fe4Plugin()
    {
        var log = new List<string>();
        var number = PluginWidgetKit.Number("esc-plugin-n", null, 0, 10, false, null, null, (v, ph, typed) => log.Add(ph + " " + v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        var vector = PluginWidgetKit.Vector("esc-plugin-v", null, 3, null, (v, ph, typed) => log.Add(ph + " " + v.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var (w, top) in new[] { (number.Element, 100), (vector.Element, 160) })
        {
            w.style.position = Position.Absolute;
            w.style.left = 300;
            w.style.top = top;
            w.style.width = 460;
            Ui.Shell.Overlay.Add(w);
        }
        number.SetValue(5.0);
        vector.SetValue(new Vector4(1, 2, 3, 0));
        yield return Frames(3);
        int u0 = Undos;

        var ns = (NumberSlider)number.Control;
        Vector2 at = ns.LabelElement.worldBound.center;
        Raw(EventType.MouseDown, at);
        Raw(EventType.MouseDrag, at + new Vector2(10, 0), new Vector2(10, 0));
        Raw(EventType.MouseDrag, at + new Vector2(20, 0), new Vector2(10, 0));
        Check("a plugin's number row: previews", log.Count(x => x.StartsWith(nameof(ApiPhase.Preview))) >= 1, string.Join(", ", log));
        log.Clear();
        EscNow();
        Check("a plugin's number row, Esc: one cancel with the start value", log.Count == 1 && log[0].StartsWith(nameof(ApiPhase.Cancel)) && log[0].EndsWith(" 5"), string.Join(", ", log));
        log.Clear();
        Raw(EventType.MouseDrag, at + new Vector2(30, 0), new Vector2(10, 0));
        Raw(EventType.MouseDrag, at + new Vector2(40, 0), new Vector2(10, 0));
        Raw(EventType.MouseUp, at + new Vector2(40, 0));
        yield return Frames(2);
        Check("a plugin's number row: moves and the release after Esc: no call, the start shown, no entry", log.Count == 0 && Mathf.Abs((float)ns.Value - 5f) < 1e-4f && Undos == u0, string.Join(", ", log) + ", " + ns.Value);
        log.Clear();

        var vf = (VectorField)vector.Control;
        at = vf.Fields[0].labelElement.worldBound.center;
        Raw(EventType.MouseDown, at);
        Raw(EventType.MouseDrag, at + new Vector2(1, 0), new Vector2(10, 0));
        Raw(EventType.MouseDrag, at + new Vector2(2, 0), new Vector2(10, 0));
        Check("a plugin's vector row: previews", log.Count(x => x.StartsWith(nameof(ApiPhase.Preview))) >= 1, string.Join(", ", log));
        log.Clear();
        EscNow();
        Check("a plugin's vector row, Esc: one cancel with the start value", log.Count == 1 && log[0].StartsWith(nameof(ApiPhase.Cancel)) && log[0].EndsWith(" 1"), string.Join(", ", log));
        log.Clear();
        Raw(EventType.MouseDrag, at + new Vector2(3, 0), new Vector2(10, 0));
        Raw(EventType.MouseDrag, at + new Vector2(4, 0), new Vector2(10, 0));
        Raw(EventType.MouseUp, at + new Vector2(4, 0));
        yield return Frames(2);
        vf.Fields[0].Blur();
        yield return Frames(2);
        Check("a plugin's vector row: moves and the release after Esc: no call, the start shown, no entry", log.Count == 0 && Mathf.Abs(vf.Fields[0].value - 1f) < 1e-4f && Undos == u0, string.Join(", ", log) + ", " + vf.Fields[0].value);

        // with an edit of something else open: the edit is cancelled, the plugin's row still says cancel and never commit
        number.SetValue(5.0);
        yield return Frames(2);
        log.Clear();
        var other = Ui.BeginEdit("Other");
        at = ns.LabelElement.worldBound.center;
        Raw(EventType.MouseDown, at);
        Raw(EventType.MouseDrag, at + new Vector2(10, 0), new Vector2(10, 0));
        log.Clear();
        EscNow();
        Check("a plugin's number row, Esc, another edit open: that edit cancelled, one cancel, no commit", !other.IsOpen && log.Count == 1 && log[0].StartsWith(nameof(ApiPhase.Cancel)), other.IsOpen + ", " + string.Join(", ", log));
        log.Clear();
        Raw(EventType.MouseDrag, at + new Vector2(30, 0), new Vector2(10, 0));
        Raw(EventType.MouseUp, at + new Vector2(30, 0));
        yield return Frames(2);
        Check("a plugin's number row, another edit open: moves and the release after Esc: no call, the start shown, no drag left", log.Count == 0 && Mathf.Abs((float)ns.Value - 5f) < 1e-4f && !ns.Dragging && Undos == u0, string.Join(", ", log) + ", " + ns.Value);

        number.Element.RemoveFromHierarchy();
        vector.Element.RemoveFromHierarchy();
        yield return Frames(2);
    }

    static IEnumerator Fe4Tree()
    {
        var a = Ui.Ops.AddFolder(null, null, -1, UiContext.User());
        var b = Ui.Ops.AddFolder(null, null, -1, UiContext.User());
        if (!Check("two folders", a.Ok && b.Ok)) yield break;
        string ia = a.NewIds[0], ib = b.NewIds[0];
        Ui.Shell.ShowPanel(WorkspacePanel.PanelId);
        yield return Frames(4);
        Ui.Ops.Select(new[] { ia });
        yield return Frames(3);
        var ws = WorkspacePanel.Current;
        ws?.RebuildNow();
        yield return Frames(3);
        var ra = Panel.visualTree.Q("tree-row-" + ia);
        var rb = Panel.visualTree.Q("tree-row-" + ib);
        if (Check("the two rows of the tree", ra != null && rb != null, (ra != null) + ", " + (rb != null)))
        {
            int u0 = Undos;
            string parent0 = Ui.Model.Find(ia).Parent?.Id;
            Vector2 p = ra.worldBound.center, q = rb.worldBound.center;
            yield return Input(Pt(p, "down"), Pt(p + new Vector2(8, 0), "move"), Pt(q, "move"));
            Check("a row's drag runs", DragController.Current != null, DragController.LastOutcome);
            yield return Key("Escape");
            Check("a tree row, Esc: the drag ended, cancelled", DragController.Current == null && DragController.LastOutcome == "cancelled", DragController.Current != null ? "still running" : DragController.LastOutcome);
            yield return Input(Pt(q + new Vector2(2, 0), "move"), Pt(q + new Vector2(4, 0), "move"), Pt(q + new Vector2(4, 0), "up"));
            yield return Frames(2);
            Check("a tree row: moves and the release after Esc: no entry, no new parent", Undos == u0 && Ui.Model.Find(ia).Parent?.Id == parent0, (Undos - u0) + ", " + Ui.Model.Find(ia).Parent?.Id);
            while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
        }
        Ui.Ops.Select(Array.Empty<string>());
        Ui.Ops.Delete(new[] { ia, ib }, UiContext.User());
        Ui.Shell.ShowPanel("inspector");
        yield return Frames(2);
    }

    /// <summary>A browser row's drag toward the viewport: the row does not hold the focus, so the key's path does not pass through it.</summary>
    static IEnumerator Fe4BrowserRow()
    {
        var src = new Label("drag me") { name = "esc-drag-source" };
        src.style.position = Position.Absolute;
        src.style.left = 300;
        src.style.top = 100;
        src.style.width = 200;
        src.style.height = 30;
        Ui.Shell.Overlay.Add(src);
        int drops = 0;
        DragController.MakeSource(src, _ => new AddPayload("drag me", OpLabels.AddItem, (p, pt) => { drops++; return OpResult.NothingToDo(); }));
        yield return Frames(3);
        Ui.Viewport.Element.Focus();
        yield return Frames(1);
        Vector2 at = src.worldBound.center, over = Ui.Viewport.Element.worldBound.center;
        Check("a browser row is under the pointer, the focus elsewhere", Panel.Pick(at) == src && FocusedNow == Ui.Viewport.Element, Name(Panel.Pick(at) as VisualElement) + ", " + Name(FocusedNow));
        int sel0 = Ui.Selection.Count, u0 = Undos;
        yield return Input(Pt(at, "down"), Pt(at + new Vector2(10, 0), "move"), Pt(over, "move"));
        Check("a browser row's drag runs", DragController.Current != null, DragController.LastOutcome);
        yield return Key("Escape");
        Check("a browser row, Esc: the drag ended, cancelled, the selection left", DragController.Current == null && DragController.LastOutcome == "cancelled" && Ui.Selection.Count == sel0, (DragController.Current != null ? "still running" : DragController.LastOutcome) + ", selected " + Ui.Selection.Count + " / " + sel0 + ", " + Log(1));
        yield return Input(Pt(over + new Vector2(4, 0), "move"), Pt(over + new Vector2(8, 0), "move"), Pt(over + new Vector2(8, 0), "up"));
        yield return Frames(2);
        Check("a browser row: moves and the release after Esc: no drop, no entry", drops == 0 && Undos == u0 && DragController.Current == null, drops + " drops, entries " + (Undos - u0) + ", " + DragController.LastOutcome);
        // a new drag afterwards reaches its drop
        yield return Input(Pt(at, "down"), Pt(at + new Vector2(10, 0), "move"), Pt(over, "move"), Pt(over, "up"));
        yield return Frames(2);
        Check("a browser row: a new drag afterwards drops", drops == 1 && DragController.Current == null, drops + " drops, " + DragController.LastOutcome);
        src.RemoveFromHierarchy();
        yield return Frames(2);
    }

    static IEnumerator Fe4Sheet()
    {
        // a list sheet above a dialog: Esc presses its cancel button
        string outer = null, inner = null;
        Ui.Dialogs.Confirm("dialog.delete.title", "dialog.delete.message", ok => outer = ok ? "ok" : "cancel", 1);
        yield return Frames(2);
        var spec = new DialogSpec { Id = "esc-test", Title = "t", Message = "m" };
        spec.Buttons.Add(DialogButton.Ok());
        SheetDialog.Show(Ui, spec, id => inner = id);
        yield return Frames(4);
        var sheet = SheetDialog.Current;
        if (Check("a sheet above a dialog", sheet != null && sheet.IsOpen))
        {
            int u0 = Undos;
            using (var e = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) sheet.Element.SendEvent(e);
            yield return Frames(2);
            Check("a list sheet, Esc: closed, no entry", !sheet.IsOpen && inner != null && Undos == u0, inner + ", entries " + (Undos - u0));
        }
        if (Ui.Dialogs.IsModalOpen) Ui.Dialogs.CloseCurrent();
        yield return Frames(2);
        Info("the dialog under it: " + outer);
    }

    static IEnumerator Fe4Timeline()
    {
        yield return Call(new { cmd = "item.add", no = 399 });
        Check("item.add 399", LastOk, Short(lastText));
        string item = (string)last?["key"] ?? (string)last?["added"]?[0]?["key"];
        yield return OkCall(new { cmd = "select", keys = new[] { item } }, "select the item");
        yield return OkCall(new { cmd = "ui.action", action = ActionIds.TimelinePanel }, "the Timeline panel");
        yield return Frames(4);
        yield return OkCall(new { cmd = "timeline.track.add", type = "pos", key = item }, "a position track");
        yield return Frames(3);
        yield return Call(ClickOn("timeline-track-0-label"));
        yield return Frames(2);
        yield return OkCall(new { cmd = "timeline.seek", t = 1 }, "seek to 1 s");
        yield return Frames(2);
        yield return Call(ClickOn("timeline-key"));
        yield return Frames(3);
        var panel = TimelinePanel.Current;
        if (!Check("the panel", panel != null)) yield break;
        string Doc() => Ui.Model.Timeline?.ToJson() ?? "null";
        var lanes = Panel.visualTree.Q("timeline-lanes");
        var lane = Panel.visualTree.Q("timeline-lane-0");
        if (!Check("the lane", lanes != null && lane != null)) yield break;
        Vector2 at = new Vector2(lanes.worldBound.x + panel.X(1.0), lane.worldBound.center.y);
        float dx = (float)(0.5 * panel.Pps);

        // the key drag
        string d0 = Doc();
        int u0 = Undos;
        yield return Input(Pt(at, "down"), Pt(at + new Vector2(dx * 0.5f, 0), "move"), Pt(at + new Vector2(dx, 0), "move"));
        Check("the key drag runs", panel.KeyDragActive);
        yield return Key("Escape");
        Check("the key drag, Esc: ended", !panel.KeyDragActive);
        yield return Input(Pt(at + new Vector2(dx * 1.2f, 0), "move"), Pt(at + new Vector2(dx * 1.4f, 0), "move"), Pt(at + new Vector2(dx * 1.4f, 0), "up"));
        yield return Frames(2);
        Check("the key drag: moves and the release after Esc: the keys back, no entry", Doc() == d0 && Undos == u0 && !panel.KeyDragActive, (Undos - u0).ToString());

        // the key editor: the selected key's vector row
        yield return Input(Pt(at, "click"));
        yield return Frames(3);
        var vf = Panel.visualTree.Q<VectorField>("timeline-keyedit-v-widget");
        if (Check("the key editor's vector row", vf != null))
        {
            // a batch window is small: the editor lies below the panel's end, so its row is brought to where the pointer can reach it
            Ui.Shell.Overlay.Add(vf);
            vf.style.position = Position.Absolute;
            vf.style.left = 300;
            vf.style.top = 100;
            vf.style.width = 460;
            yield return Frames(3);
            d0 = Doc();
            u0 = Undos;
            var f = vf.Fields[0];
            var label = f.labelElement;
            Vector2 lat = label.worldBound.center;
            Info("the key editor's label at " + lat + ", picked " + Name(Panel.Pick(lat) as VisualElement) + ", the panel's region " + Ui.Shell.Regions[LayoutRegions.Bottom].Collapsed + ", the screen " + Screen.width + "x" + Screen.height);
            Raw(EventType.MouseDown, lat);
            Check("the key editor's label holds the pointer", Capturing() == label && vf.Dragging, Name(Capturing()));
            Raw(EventType.MouseDrag, lat + new Vector2(1, 0), new Vector2(10, 0));
            yield return Frames(1);
            Raw(EventType.MouseDrag, lat + new Vector2(2, 0), new Vector2(10, 0));
            yield return Frames(1);
            Check("the key editor's drag writes", Doc() != d0, Short(Doc()) + " / " + Short(d0) + ", field " + f.value);
            EscNow();
            yield return Frames(1);
            Check("the key editor, Esc: the value back", Doc() == d0, Short(Doc()));
            Check("the key editor, Esc: no entry", Undos == u0, (Undos - u0) + " / " + TopLabel);
            Raw(EventType.MouseDrag, lat + new Vector2(3, 0), new Vector2(10, 0));
            Raw(EventType.MouseDrag, lat + new Vector2(4, 0), new Vector2(10, 0));
            yield return Frames(1);
            Raw(EventType.MouseUp, lat + new Vector2(4, 0));
            yield return Frames(2);
            f.Blur();
            yield return Frames(2);
            Check("the key editor: moves and the release after Esc: no value", Doc() == d0, Short(Doc()));
            Check("the key editor: moves and the release after Esc: no entry", Undos == u0, (Undos - u0) + " / " + TopLabel);
            vf.RemoveFromHierarchy();
        }

        // a track row's drag (before its own Esc: one entry «Track», the order changed)
        yield return Call(new { cmd = "timeline.track.add", type = "rot", key = item });
        yield return Frames(3);
        var t0 = Panel.visualTree.Q("timeline-track-0");
        var t1 = Panel.visualTree.Q("timeline-track-1");
        if (t0 != null && t1 != null)
        {
            u0 = Undos;
            d0 = Doc();
            Vector2 a0 = t0.worldBound.center, a1 = t1.worldBound.center;
            yield return Input(Pt(a0, "down"), Pt(a1, "move"));
            Check("a track row's drag holds the pointer", Capturing() != null, Name(Capturing()));
            yield return Key("Escape");
            Check("a track row, Esc: no pointer held, the order equal, no entry", Capturing() == null && Doc() == d0 && Undos == u0, Name(Capturing()) + ", entries " + (Undos - u0));
            yield return Input(Pt(a1, "move"), Pt(a1, "up"));
            yield return Frames(2);
            Check("a track row: a move and the release after Esc: the order equal, no entry", Doc() == d0 && Undos == u0, (Undos - u0) + " / " + TopLabel);
            yield return Input(Pt(a0, "down"), Pt(a1, "move"), Pt(a1, "up"));
            yield return Frames(2);
            Check("a track row: a new drag afterwards: one entry «Track», the order changed", Undos == u0 + 1 && TopLabel == "Track" && Doc() != d0, (Undos - u0) + " / " + TopLabel);
            while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
            yield return Frames(2);
            Check("a track row: undo: the order as before", Doc() == d0);
        }

        yield return OkCall(new { cmd = "ui.action", action = ActionIds.TimelinePanel }, "the Timeline panel closed");
        yield return OkCall(new { cmd = "timeline.seek", t = 0 }, "seek to 0");
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character");
        yield return Frames(2);
    }

    // ---------------------------------------------------------------- FE5

    static IEnumerator FE5()
    {
        var a = Ui.Ops.AddFolder(null, null, -1, UiContext.User());
        if (!Check("a folder", a.Ok)) yield break;
        string id = a.NewIds[0];
        Ui.Ops.Select(new[] { id });
        var spec = InspectorSchemas.Transform.Fields.First(f => f.Path == "transform.pos");
        var row = PlaceRow(spec, TargetSelector.Selection());
        yield return Frames(3);
        var vf = (VectorField)row.Control;
        var f = vf.Fields[1];
        int u0 = Undos;
        f.Focus();
        yield return Frames(2);
        Host.Router.Log.Clear();
        Check("a field focused", Host.Router.Focused != null && HS2Next.UI.Input.ShortcutRouter.IsText(Host.Router.Focused), Name(Host.Router.Focused));
        yield return Key("Escape");
        Check("Esc in a text field, no drag: blurred, the viewport focused", Log(1) == "Esc: text field blurred" && FocusedNow == Ui.Viewport.Element, Log(1) + ", " + Name(FocusedNow));
        Check("Esc in a text field: no entry, nothing cancelled", Undos == u0 && !Host.Router.Log.Any(x => x.Contains("cancelled")), Log(3));
        row.RemoveFromHierarchy();
        Ui.Ops.Select(Array.Empty<string>());
        Ui.Ops.Delete(new[] { id }, UiContext.User());
        yield return Frames(2);

        // a wheel run over the readout: the key ends the run before it is handled, as it always did
        var add = Ui.Ops.AddCameraFromView(null, false, UiContext.User());
        string cam = add?.NewIds.FirstOrDefault();
        if (Check("a camera object", cam != null))
        {
            Ui.Ops.SetActiveCamera(cam, UiContext.User());
            yield return Frames(3);
            var vp = Ui.Viewport;
            float f0 = (Ui.Model.Find(cam)?.State as CameraNodeState)?.Fov ?? -1f;
            u0 = Undos;
            yield return Input(new Newtonsoft.Json.Linq.JObject { ["type"] = "wheel", ["target"] = "viewport-fov", ["delta"] = -3 });
            Check("a wheel run is open, no pointer held", vp.WheelRunOpen && Capturing() == null, vp.WheelRunOpen + ", " + Name(Capturing()));
            yield return Key("Escape");
            float f1 = (Ui.Model.Find(cam)?.State as CameraNodeState)?.Fov ?? -1f;
            Check("Esc ends the run", !vp.WheelRunOpen);
            Check("the run is written as one entry, as before this phase (the key ends it before it is handled)", Undos == u0 + 1 && Mathf.Abs(f1 - f0) > 1e-3f, (Undos - u0) + ", " + f0 + " -> " + f1);
            while (Undos > u0) { Ui.Session.Commands.Undo(); Ui.Runtime?.ApplyNow(); }
            Ui.Ops.SetActiveCamera(null, UiContext.User());
            Ui.Ops.Select(Array.Empty<string>());
            Ui.Ops.Delete(new[] { cam }, UiContext.User());
            yield return Frames(2);
        }
    }
}
