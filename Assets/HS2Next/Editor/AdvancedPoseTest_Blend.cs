// AdvancedPoseTest, the blend-shape overrides (Phase 6.3 package J, AT3's blend half in the editor).
//   AL1  an override on an index of the current mouth pattern and another: the weight after every step, through pattern changes, a
//        cross-fade and blinks; the twin shows the face stage moving those indices
//   AL2  removal: a face index = what the face stage writes next (the twin); an index the face stage does not write (found on the twin: on
//        the targets o_tooth has none, o_head and o_namida have) = its value before; a suspension shows the base; the stage goes with the
//        last override
//   AL3  a missing renderer, a missing shape: stale, the finding once, no exception; active again when it returns
//   AL4  two renderers of one name: #0 and #1 apart
//   AL5  a head swap: resolved again by name; a shape the head lacks stale; the stage after the new face stage; undo
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class AdvancedPoseTest
{
    static void Headless_J_Blend()
    {
        Group("AL1", "the override after every step", AL1);
        Group("AL2", "removal", AL2);
        Group("AL3", "missing renderer, missing shape", AL3);
        Group("AL4", "two renderers of one name", AL4);
        Group("AL5", "a head swap", AL5);
    }

    // ------------------------------------------------------------ helpers

    /// <summary>The model's blend map replaced (no command), applied.</summary>
    static void SetBlend(Host h, string id, params (string renderer, string shape, float w)[] entries)
    {
        var m = new SortedDictionary<string, BlendValues>(StringComparer.Ordinal);
        foreach (var (rn, shape, w) in entries)
        {
            if (!m.TryGetValue(rn, out var b)) m[rn] = b = new BlendValues();
            b.Weights[shape] = w;
        }
        var res = h.Session.Set(id, "chara.advancedPose.blendShapes", m);
        if (!res.Ok) Check("set chara.advancedPose.blendShapes", false, res.ToString());
        h.Rt.ApplyNow();
    }

    /// <summary>A small skinned renderer with its own blend shapes under <paramref name="parent"/> (test fixture).</summary>
    static SkinnedMeshRenderer SyntheticShapes(Transform parent, string name, List<Object> trash, params string[] shapes)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        mesh.bindposes = new[] { Matrix4x4.identity };
        mesh.boneWeights = new[] { new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f }, new BoneWeight { weight0 = 1f } };
        var delta = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
        foreach (var s in shapes) mesh.AddBlendShapeFrame(s, 100f, delta, null, null);
        var r = go.AddComponent<SkinnedMeshRenderer>();
        r.sharedMesh = mesh;
        r.bones = new[] { go.transform };
        trash.Add(mesh);
        trash.Add(go);
        return r;
    }

    static void Destroy(List<Object> trash)
    {
        foreach (var o in trash) if (o) Object.DestroyImmediate(o);
        trash.Clear();
    }

    sealed class BlendTiming : IMotionTimingSink
    {
        public readonly List<long> Ticks = new List<long>();
        public void StageStepped(CharaMotion motion, IMotionStage stage, long ticks) { if (stage is BlendShapeOverrideStage) Ticks.Add(ticks); }
    }

    static bool HasBlendStage(CharaRealization r) => r.Motion.Stages.Any(s => s.Name == "pose.blend");

    // ------------------------------------------------------------ AL1

    static void AL1()
    {
        var f = NewTwins(femaleRel, "female", "AL1");
        try
        {
            f.Step(2);
            var a = AdvancedPoseParts.Renderer(f.RA, "head/o_head");
            var b = AdvancedPoseParts.Renderer(f.RB, "head/o_head");
            if (!Check("o_head on both", a && b && a.sharedMesh.blendShapeCount > 0, a ? a.sharedMesh.blendShapeCount + " shapes" : "missing")) return;
            var mesh = a.sharedMesh;
            var names = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
            // the current mouth pattern's index and another pattern's: what two patterns show apart
            HashSet<int> Shown(int mouth)
            {
                foreach (var key in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.face", key, mouth }, "chara.face mouth " + mouth);
                f.Step(15);
                return new HashSet<int>(Enumerable.Range(0, names.Length).Where(i => b.GetBlendShapeWeight(i) > 0f));
            }
            var s1 = Shown(1);
            var s3 = Shown(3);
            int oth = s1.Except(s3).DefaultIfEmpty(-1).First(), cur = s3.Except(s1).DefaultIfEmpty(-1).First();
            if (!Check("two mouth patterns show different indices", cur >= 0 && oth >= 0, $"pattern 1: {s1.Count}, pattern 3: {s3.Count}")) return;
            Info($"the current index {cur} (weight {b.GetBlendShapeWeight(cur)}), another pattern's {oth}");
            const float wc = 37f, wo = 64f;
            Check("no pose.blend stage before", !HasBlendStage(f.RA));
            SetBlend(f.H, f.A, ("head/o_head", names[cur], wc), ("head/o_head", names[oth], wo));
            Check("the stage is there", HasBlendStage(f.RA));
            Check("at once (a dump in the same request sees it)", a.GetBlendShapeWeight(cur) == wc && a.GetBlendShapeWeight(oth) == wo);
            var dump = f.H.Ok(new { cmd = "face.dump", key = f.A }, "face.dump");
            var drow = (dump["renderers"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["name"] == "o_head");
            Check("face.dump reports the override", drow != null && Math.Abs((double)drow["weights"][cur] - wc) <= 1e-5 && Math.Abs((double)drow["weights"][oth] - wo) <= 1e-5);

            int bad = 0, steps = 0;
            float twinMin = float.MaxValue, twinMax = float.MinValue, twinOthMax = 0f;
            void Run(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    f.Step(1);
                    steps++;
                    if (a.GetBlendShapeWeight(cur) != wc || a.GetBlendShapeWeight(oth) != wo) bad++;
                    float t = b.GetBlendShapeWeight(cur);
                    twinMin = Math.Min(twinMin, t); twinMax = Math.Max(twinMax, t);
                    twinOthMax = Math.Max(twinOthMax, b.GetBlendShapeWeight(oth));
                }
            }
            Run(10);
            int k = 0;
            foreach (int mouth in new[] { 1, 3, 0, 2, 5, 0 })
            {
                foreach (var key in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.face", key, mouth }, "chara.face mouth " + mouth);
                Run(k++ % 2 == 0 ? 12 : 3);   // 0.2 s: past the cross-fade; 0.05 s: inside it
            }
            foreach (var key in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.face", key, blink = true, eyes = 1 }, "chara.face blink, eyes");
            Run(300);
            Check("the override after every step", bad == 0, $"{steps} steps, {bad} off");
            Check("the twin shows the face stage moving the indices", twinMax > twinMin && twinOthMax > 0f, $"current index {twinMin}..{twinMax}, the other up to {twinOthMax}");
            Check("no stage threw", f.RA.Motion.Errors == 0, "" + f.RA.Motion.Errors);

            // AT3's blend half in the editor (the player's number is the row's): 16 overrides, 600 frames
            var many = Enumerable.Range(0, Math.Min(16, names.Length)).Select(i => ("head/o_head", names[i], 20f + i)).ToArray();
            SetBlend(f.H, f.A, many);
            var sink = new BlendTiming();
            CharaMotion.TimingSink = sink;
            try { f.Step(600); }
            finally { CharaMotion.TimingSink = null; }
            sink.Ticks.Sort();
            double ms = sink.Ticks.Count > 0 ? sink.Ticks[sink.Ticks.Count / 2] * 1000.0 / System.Diagnostics.Stopwatch.Frequency : double.NaN;
            Measure($"AT3 pose.blend (16 overrides, 600 frames, editor, no graphics): median {ms:F4} ms, bar 0.1 ms");
            SetBlend(f.H, f.A);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AL2

    static void AL2()
    {
        var f = NewTwins(femaleRel, "female", "AL2");
        try
        {
            f.Step(2);
            var a = AdvancedPoseParts.Renderer(f.RA, "head/o_head");
            var b = AdvancedPoseParts.Renderer(f.RB, "head/o_head");
            var mesh = a.sharedMesh;
            var names = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
            int cur = Enumerable.Range(0, names.Length).First(i => b.GetBlendShapeWeight(i) > 0f);
            float before = a.GetBlendShapeWeight(cur);
            SetBlend(f.H, f.A, ("head/o_head", names[cur], 71f));
            f.Step(3);
            Check("the override acts", a.GetBlendShapeWeight(cur) == 71f && b.GetBlendShapeWeight(cur) == before);
            SetBlend(f.H, f.A);
            Check("removal: the base back at once", a.GetBlendShapeWeight(cur) == before, $"{a.GetBlendShapeWeight(cur)} / {before}");
            Check("the stage is gone, the state is dropped", !HasBlendStage(f.RA) && AdvancedPoseDriver.Of(f.RA) == null);
            f.Step(1);
            int diff = Enumerable.Range(0, names.Length).Count(i => a.GetBlendShapeWeight(i) != b.GetBlendShapeWeight(i));
            Check("the next step: every index of o_head equals the twin", diff == 0, diff + " differ");

            // an index the face stage does not write: every index of every head renderer set to a mark on the twin, one step, what still
            // holds the mark (spec: o_tooth; it reports below how many it has)
            SkinnedMeshRenderer ta = null;
            string address = null;
            int free = -1;
            var rows = AdvancedPoseParts.Renderers(f.RB, "head");
            foreach (var row in rows)
                for (int i = 0; i < row.Renderer.sharedMesh.blendShapeCount; i++) row.Renderer.SetBlendShapeWeight(i, 12f);
            f.RB.Motion.Step(Dt);
            var freeCount = new List<string>();
            foreach (var row in rows)
            {
                int n = Enumerable.Range(0, row.Renderer.sharedMesh.blendShapeCount).Count(i => row.Renderer.GetBlendShapeWeight(i) == 12f);
                freeCount.Add($"{row.Name} {n} of {row.Renderer.sharedMesh.blendShapeCount}");
                if (n > 0 && (free < 0 || row.Name == "o_tooth"))
                {
                    address = row.Address;
                    ta = AdvancedPoseParts.Renderer(f.RA, row.Address);
                    free = Enumerable.Range(0, row.Renderer.sharedMesh.blendShapeCount).First(i => row.Renderer.GetBlendShapeWeight(i) == 12f);
                }
            }
            Measure("AL2 indices the face stage does not write: " + string.Join(", ", freeCount));
            foreach (var row in rows)
                for (int i = 0; i < row.Renderer.sharedMesh.blendShapeCount; i++) row.Renderer.SetBlendShapeWeight(i, 0f);
            var trash = new List<Object>();
            if (free < 0)
            {
                // none on this head: a renderer of the fixture
                ta = SyntheticShapes(f.RA.Context.Head.transform, "al2_free", trash, "al2_a");
                address = "head/al2_free";
                free = 0;
            }
            string shape = ta.sharedMesh.GetBlendShapeName(free);
            ta.SetBlendShapeWeight(free, 12f);
            Info($"the free index: {address} {free}");
            SetBlend(f.H, f.A, (address, shape, 50f));
            f.Step(3);
            Check("a non-face index: the override acts", ta.GetBlendShapeWeight(free) == 50f);

            // a suspension (character thumbnails): the base shows, the stage stands still; afterwards the next step writes it again
            using (CharaPoseDrivers.SuspendAll(f.RA.Root))
            {
                Check("suspended: the base shows", ta.GetBlendShapeWeight(free) == 12f, "" + ta.GetBlendShapeWeight(free));
                f.RA.Motion.Step(Dt);
                Check("suspended: a step does not write it", ta.GetBlendShapeWeight(free) == 12f, "" + ta.GetBlendShapeWeight(free));
            }
            f.Step(1);
            Check("resumed: the override again", ta.GetBlendShapeWeight(free) == 50f, "" + ta.GetBlendShapeWeight(free));

            SetBlend(f.H, f.A);
            Check("a non-face index: removal gives the value before", ta.GetBlendShapeWeight(free) == 12f, "" + ta.GetBlendShapeWeight(free));
            f.Step(5);
            Check("a non-face index: and it stays", ta.GetBlendShapeWeight(free) == 12f);
            Destroy(trash);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AL3

    static void AL3()
    {
        var f = NewTwins(femaleRel, "female", "AL3");
        var trash = new List<Object>();
        try
        {
            f.Step(2);
            var head = f.RA.Context.Head.transform;
            var o = AdvancedPoseParts.Renderer(f.RA, "head/o_head");
            const string noRenderer = "head/al3_shapes", noShape = "no_such_shape";
            var free = o.sharedMesh.GetBlendShapeName(0);
            SetBlend(f.H, f.A, (noRenderer, "al3_a", 40f), ("head/o_head", noShape, 40f), ("head/o_head", free, 30f));
            string json = Block(f.H, f.A).ToJson();
            var c = AdvancedPoseDriver.Of(f.RA);
            Check("the missing renderer is stale", c.StaleRenderers.SequenceEqual(new[] { noRenderer }), string.Join(",", c.StaleRenderers));
            Check("the missing shape is stale", c.StaleShapes.SequenceEqual(new[] { "head/o_head:" + noShape }), string.Join(",", c.StaleShapes));
            Check("the finding once", FindingCount(f.H, AdvancedPoseFindings.Stale, f.A) == 2, "" + FindingCount(f.H, AdvancedPoseFindings.Stale, f.A));
            f.Step(5);
            SetBlend(f.H, f.A, (noRenderer, "al3_a", 40f), ("head/o_head", noShape, 40f), ("head/o_head", free, 31f));
            SetBlend(f.H, f.A, (noRenderer, "al3_a", 40f), ("head/o_head", noShape, 40f), ("head/o_head", free, 30f));
            int findings = 0;
            foreach (var x in f.H.Rt.Findings) if (x.Code == AdvancedPoseFindings.Stale && x.NodeId == f.A) findings++;
            Check("still one finding after more reads", findings == 1, "" + findings);
            Check("what resolves acts", o.GetBlendShapeWeight(0) == 30f);
            Check("no stage threw", f.RA.Motion.Errors == 0, "" + f.RA.Motion.Errors);
            Check("the block keeps them", Block(f.H, f.A).ToJson() == json);

            // the renderer returns (a part comes back), the shape too
            var s = SyntheticShapes(head, "al3_shapes", trash, "al3_a", "al3_b");
            AdvancedPoseDriver.Reresolve(f.RA);
            c = AdvancedPoseDriver.Of(f.RA);
            Check("active again when it returns", c.StaleRenderers.Count == 0 && s.GetBlendShapeWeight(0) == 40f && s.GetBlendShapeWeight(1) == 0f, "" + s.GetBlendShapeWeight(0));
            Destroy(trash);
            AdvancedPoseDriver.Reresolve(f.RA);
            c = AdvancedPoseDriver.Of(f.RA);
            Check("stale again when it goes", c.StaleRenderers.SequenceEqual(new[] { noRenderer }));
            f.Step(2);
            Check("no stage threw after it went", f.RA.Motion.Errors == 0, "" + f.RA.Motion.Errors);

            // a key that is no address: kept, listed, inactive
            var m = new SortedDictionary<string, BlendValues>(StringComparer.Ordinal) { ["not an address"] = new BlendValues() };
            m["not an address"].Weights["x"] = 1f;
            var res = f.H.Session.Set(f.A, "chara.advancedPose.blendShapes", m);
            f.H.Rt.ApplyNow();
            c = AdvancedPoseDriver.Of(f.RA);
            Check("a key that is no address is listed stale", res.Ok && c != null && c.StaleRenderers.Contains("not an address"), c == null ? "no state" : string.Join(",", c.StaleRenderers));
            SetBlend(f.H, f.A);
        }
        finally { Destroy(trash); f.Dispose(); }
    }

    // ------------------------------------------------------------ AL4

    static void AL4()
    {
        var f = NewTwins(femaleRel, "female", "AL4");
        var trash = new List<Object>();
        try
        {
            f.Step(2);
            var slot = f.RA.Context.Accessories.FirstOrDefault(x => x != null && x.Object && f.RA.AccessoryIdOf(x) != null);
            if (!Check("an accessory on the target", slot != null)) return;
            string id = f.RA.AccessoryIdOf(slot);
            var first = SyntheticShapes(slot.Object.transform, "al4_dup", trash, "k");
            var second = SyntheticShapes(slot.Object.transform, "al4_dup", trash, "k");
            var rows = AdvancedPoseParts.Renderers(f.RA, "acc:" + id).Where(x => x.Name == "al4_dup").ToList();
            string a0 = "acc:" + id + "/al4_dup", a1 = a0 + "#1";
            Check("listed apart", rows.Select(x => x.Address).SequenceEqual(new[] { a0, a1 }) && rows[0].Renderer == first && rows[1].Renderer == second, string.Join(",", rows.Select(x => x.Address)));
            SetBlend(f.H, f.A, (a1, "k", 25f));
            Check("#1 alone", first.GetBlendShapeWeight(0) == 0f && second.GetBlendShapeWeight(0) == 25f);
            SetBlend(f.H, f.A, (a0, "k", 75f), (a1, "k", 25f));
            f.Step(2);
            Check("both, apart", first.GetBlendShapeWeight(0) == 75f && second.GetBlendShapeWeight(0) == 25f);
            SetBlend(f.H, f.A, (a0, "k", 75f));
            Check("#1 removed, #0 stays", first.GetBlendShapeWeight(0) == 75f && second.GetBlendShapeWeight(0) == 0f);
            SetBlend(f.H, f.A);
        }
        finally { Destroy(trash); f.Dispose(); }
    }

    // ------------------------------------------------------------ AL5

    static void AL5()
    {
        var f = NewTwins(femaleRel, "female", "AL5");
        try
        {
            f.Step(2);
            string head = EditValue(f.H, f.A, "chara.face.head");
            string other = OtherRow(f.H, f.A, "chara.face.head", head);
            if (other == null) { Warn("no other head row"); return; }
            var o0 = AdvancedPoseParts.Renderer(f.RA, "head/o_head");
            var names0 = new HashSet<string>(Enumerable.Range(0, o0.sharedMesh.blendShapeCount).Select(o0.sharedMesh.GetBlendShapeName));
            string shape = o0.sharedMesh.GetBlendShapeName(1);
            const float w = 41f;
            SetBlend(f.H, f.A, ("head/o_head", shape, w));
            f.Step(2);
            f.H.Ok(new { cmd = "chara.edit.item", key = f.A, path = "chara.face.head", @ref = other }, "a head swap");
            f.H.Rt.ApplyNow();
            var o1 = AdvancedPoseParts.Renderer(f.RA, "head/o_head");
            if (!Check("the new head has o_head", o1 && o1 != o0)) return;
            var names1 = new HashSet<string>(Enumerable.Range(0, o1.sharedMesh.blendShapeCount).Select(o1.sharedMesh.GetBlendShapeName));
            int i1 = o1.sharedMesh.GetBlendShapeIndex(shape);
            Info($"shapes: the first head {names0.Count}, the new head {names1.Count}, only on the first {names0.Except(names1).Count()}");
            Check("resolved again by name", i1 >= 0 ? o1.GetBlendShapeWeight(i1) == w : AdvancedPoseDriver.Of(f.RA).StaleShapes.Contains("head/o_head:" + shape));
            f.Step(3);
            if (i1 >= 0) Check("after steps", o1.GetBlendShapeWeight(i1) == w, "" + o1.GetBlendShapeWeight(i1));
            var st = f.RA.Motion.Stages.ToList();
            int face = st.FindLastIndex(x => x is ExpressionStage), blend = st.FindIndex(x => x is BlendShapeOverrideStage);
            Check("the stage after the new face stage", face >= 0 && blend > face, $"face {face}, blend {blend}");

            string lacking = names0.Except(names1).FirstOrDefault() ?? "no_such_shape";
            SetBlend(f.H, f.A, ("head/o_head", shape, w), ("head/o_head", lacking, 12f));
            Check("a shape the head lacks is stale", AdvancedPoseDriver.Of(f.RA).StaleShapes.Contains("head/o_head:" + lacking), string.Join(",", AdvancedPoseDriver.Of(f.RA).StaleShapes));
            SetBlend(f.H, f.A, ("head/o_head", shape, w));

            f.H.Ok(new { cmd = "chara.edit.item", key = f.A, path = "chara.face.head", @ref = head }, "the head back");
            f.H.Rt.ApplyNow();
            var o2 = AdvancedPoseParts.Renderer(f.RA, "head/o_head");
            Check("back on the first head: it acts", o2 && o2.GetBlendShapeWeight(o2.sharedMesh.GetBlendShapeIndex(shape)) == w);
            f.Step(2);
            Check("no stage threw", f.RA.Motion.Errors == 0, "" + f.RA.Motion.Errors);
            SetBlend(f.H, f.A);
        }
        finally { f.Dispose(); }
    }
}
