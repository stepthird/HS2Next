// ConstraintTest, Step 0 (CO0): stepping the characters from a scheduled pass gives what stepping them from their
// own LateUpdate gives, bit for bit. Real frames at a fixed delta, BR-Chan and HERO107 on one row, 300 steps, physics on, for each of the rows 19/0/0
// and dance_00: the scene twice with no pass (the control: a run equals a run) and twice with a pass, in the model's order and in the reverse,
// with a dummy constraint between the two steps: a slot of one empty folder follows the other folder plus the first character's hand, read in
// the middle of the pass. Every transform under both holders (local position, rotation, scale) and every blend-shape weight is compared by its
// bits at every step. Steps are aligned by CharaMotion.StepCount, so a run that starts a frame later compares the same state. Two negative
// controls show that the comparison sees what it claims: another blink seed must differ in the blend weights, physics off in the bones.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class ConstraintTest
{
    const int Co0Steps = 300;

    sealed class Co0Data
    {
        public string Name;
        public readonly List<int[]> Frames = new List<int[]>();
        public string[] Labels;
        public int Transforms, Shapes, BadSteps, Walks;
        public double WorstFollow;
    }

    /// <summary>The probe: samples at every late apply after a step; with Pass it is the scene's motion pass as well.</summary>
    sealed class Co0Part : ISceneRuntimePart, IMotionPass
    {
        SceneRuntime rt;
        public Co0Data Data;
        public bool Reverse;
        public string F1, F2;
        public OverrideSlot Slot;
        Transform[][] bones;
        SkinnedMeshRenderer[][] meshes;
        int lastStep = -1, stride;
        Vector3 expected;

        public void Attach(SceneRuntime r) { rt = r; }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void Clear() { }
        public void OnLoadedRealized() { }

        public void Run(float? dt, bool all)
        {
            var l = new List<CharaRealization>(rt.Charas());
            if (Reverse) l.Reverse();
            for (int i = 0; i < l.Count; i++)
            {
                rt.StepInPass(l[i], dt, all);
                if (i == 0) Dummy(l[0]);
            }
            Data.Walks++;
        }

        // the dummy constraint: reads between the two steps, writes a slot, whose apply moves a holder at once
        void Dummy(CharaRealization first)
        {
            if (first.State != RealizationState.Built) return;
            var hand = first.AttachPoints["N_Hand_R"];
            if (!hand) return;
            expected = rt.HolderOf(F1).position + hand.position;
            Slot.Set(expected.x, expected.y, expected.z);
        }

        public void LateApply()
        {
            var cs = rt.Charas().Where(c => c.State == RealizationState.Built && c.Motion).ToList();
            if (cs.Count < 2) return;
            int sa = cs[0].Motion.StepCount, sb = cs[1].Motion.StepCount;
            if (sa == 0 || sa == lastStep) return;
            if (sa != sb || (lastStep >= 0 && sa != lastStep + 1)) Data.BadSteps++;
            lastStep = sa;
            if (Data.Frames.Count >= Co0Steps) return;
            if (bones == null) Collect(cs);
            var f = new int[stride];
            int k = 0;
            for (int c = 0; c < 2; c++)
            {
                foreach (var t in bones[c])
                {
                    t.GetLocalPositionAndRotation(out var p, out var q);
                    var s = t.localScale;
                    f[k++] = Bits(p.x); f[k++] = Bits(p.y); f[k++] = Bits(p.z);
                    f[k++] = Bits(q.x); f[k++] = Bits(q.y); f[k++] = Bits(q.z); f[k++] = Bits(q.w);
                    f[k++] = Bits(s.x); f[k++] = Bits(s.y); f[k++] = Bits(s.z);
                }
                foreach (var m in meshes[c])
                {
                    int n = m.sharedMesh.blendShapeCount;
                    for (int i = 0; i < n; i++) f[k++] = Bits(m.GetBlendShapeWeight(i));
                }
            }
            Data.Frames.Add(f);
            if (Slot != null)
            {
                double d = (rt.HolderOf(F2).position - expected).magnitude;
                if (d > Data.WorstFollow) Data.WorstFollow = d;
            }
        }

        static int Bits(float v) => BitConverter.SingleToInt32Bits(v);

        void Collect(List<CharaRealization> cs)
        {
            bones = new Transform[2][];
            meshes = new SkinnedMeshRenderer[2][];
            var labels = new List<string>();
            string[] ch = { "px", "py", "pz", "qx", "qy", "qz", "qw", "sx", "sy", "sz" };
            for (int c = 0; c < 2; c++)
            {
                bones[c] = cs[c].Holder.GetComponentsInChildren<Transform>(true);
                meshes[c] = cs[c].Holder.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(m => m.sharedMesh).ToArray();
                foreach (var t in bones[c])
                {
                    foreach (var x in ch) labels.Add("chara " + c + " " + t.name + " " + x);
                    Data.Transforms++;
                }
                foreach (var m in meshes[c])
                    for (int i = 0; i < m.sharedMesh.blendShapeCount; i++)
                    {
                        labels.Add("chara " + c + " " + m.name + " shape " + m.sharedMesh.GetBlendShapeName(i));
                        Data.Shapes++;
                    }
            }
            Data.Labels = labels.ToArray();
            stride = labels.Count;
        }
    }

    static IEnumerator Co0Run(AppCore app, int group, int category, int no, bool pass, bool reverse, Co0Data data, bool physics = true)
    {
        yield return PCall(app, new { cmd = "scene.new" });
        if (!Check(data.Name + ": scene.new", POk, Short(pLastText))) yield break;
        var rt = app.Runtime;
        var part = new Co0Part { Data = data, Reverse = reverse };
        part.F1 = app.Session.AddFolder().NewIds[0];
        part.F2 = app.Session.AddFolder().NewIds[0];
        app.Session.Set(part.F1, "transform.pos", new Vec3(1f, 2f, 3f));
        rt.ApplyNow();
        rt.AddPart(part);
        if (pass)
        {
            part.Slot = rt.Overrides.Acquire("co0", part.F2, "transform.pos", 10);
            rt.SetPass(part);
        }
        var cmds = new List<object>
        {
            new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = femaleRel, ["sex"] = "female", ["as"] = "a" },
            new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = maleRel, ["sex"] = "male", ["as"] = "b" },
            new Dictionary<string, object> { ["cmd"] = "chara.anime", ["key"] = "$a.added.0.key", ["group"] = group, ["category"] = category, ["no"] = no, ["time"] = 0f, ["speed"] = 1f },
            new Dictionary<string, object> { ["cmd"] = "chara.anime", ["key"] = "$b.added.0.key", ["group"] = group, ["category"] = category, ["no"] = no, ["time"] = 0f, ["speed"] = 1f },
        };
        if (!physics)
        {
            cmds.Add(new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$a.added.0.key", ["physics"] = false });
            cmds.Add(new Dictionary<string, object> { ["cmd"] = "chara.motion", ["key"] = "$b.added.0.key", ["physics"] = false });
        }
        yield return PCall(app, new { cmd = "batch", cmds });
        Check(data.Name + ": both characters on the row in one batch", POk, POk ? "" : Short(pLastText));
        float until = Time.realtimeSinceStartup + 240f;
        while (data.Frames.Count < Co0Steps && Time.realtimeSinceStartup < until) yield return null;
        rt.SetPass(null);
        rt.RemovePart(part);
        Check(data.Name + ": " + Co0Steps + " steps sampled, one per frame, both characters in step", data.Frames.Count == Co0Steps && data.BadSteps == 0,
            $"{data.Frames.Count} steps, {data.BadSteps} out of step, {data.Transforms} transforms, {data.Shapes} blend shapes");
        if (pass)
        {
            Check(data.Name + ": the pass walked every frame", data.Walks >= Co0Steps, data.Walks + " walks");
            Check(data.Name + ": the folder stands where the dummy constraint read it, in the same frame", data.WorstFollow <= 1e-5, data.WorstFollow.ToString("G3"));
        }
    }

    /// <summary>How much a run moves: the channels of each class that are not constant over the run.</summary>
    static string Co0Moved(Co0Data x, out int bones, out int shapes)
    {
        bones = shapes = 0;
        var first = x.Frames[0];
        for (int k = 0; k < first.Length; k++)
        {
            bool moved = false;
            for (int i = 1; i < x.Frames.Count && !moved; i++) moved = x.Frames[i][k] != first[k];
            if (!moved) continue;
            if (x.Labels[k].Contains(" shape ")) shapes++; else bones++;
        }
        return $"{bones} bone channels and {shapes} blend shapes change over the run";
    }

    /// <summary>The first difference of two runs, bit for bit; the steps that differ; the values compared.</summary>
    static bool Co0Same(Co0Data x, Co0Data y, out string detail)
    {
        long compared = 0;
        int steps = 0;
        string first = null;
        int n = Math.Min(x.Frames.Count, y.Frames.Count);
        if (x.Frames.Count == 0 || x.Frames.Count != y.Frames.Count) { detail = $"{x.Frames.Count} steps against {y.Frames.Count}"; return false; }
        if (x.Frames[0].Length != y.Frames[0].Length) { detail = $"{x.Frames[0].Length} values a step against {y.Frames[0].Length}"; return false; }
        var worst = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            var a = x.Frames[i]; var b = y.Frames[i];
            bool differs = false;
            for (int k = 0; k < a.Length; k++)
            {
                compared++;
                if (a[k] == b[k]) continue;
                differs = true;
                string label = x.Labels[k];
                worst.TryGetValue(label, out int c);
                worst[label] = c + 1;
                if (first == null) first = $"first at step {i}: {label} {BitConverter.Int32BitsToSingle(a[k]):R} against {BitConverter.Int32BitsToSingle(b[k]):R}";
            }
            if (differs) steps++;
        }
        detail = $"{compared:N0} values compared over {n} steps";
        if (steps == 0) return true;
        detail += $"; {steps} steps differ, {worst.Count} channels; {first}; most: " +
                  string.Join(", ", worst.OrderByDescending(k => k.Value).Take(12).Select(k => k.Key + " x" + k.Value));
        return false;
    }

    static IEnumerator Play_A_Step0()
    {
        current = "Step0";
        if (!POpen("CO0", "a scheduled pass equals LateUpdate, bit for bit, real frames")) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        try
        {
            foreach (var row in new[] { (19, 0, 0, "row 19/0/0"), (11, 5, 0, "dance_00") })
            {
                var off1 = new Co0Data { Name = row.Item4 + " no pass" };
                var off2 = new Co0Data { Name = row.Item4 + " no pass again" };
                var on = new Co0Data { Name = row.Item4 + " pass" };
                var rev = new Co0Data { Name = row.Item4 + " pass, reverse order" };
                yield return Co0Run(app, row.Item1, row.Item2, row.Item3, false, false, off1);
                yield return Co0Run(app, row.Item1, row.Item2, row.Item3, false, false, off2);
                yield return Co0Run(app, row.Item1, row.Item2, row.Item3, true, false, on);
                yield return Co0Run(app, row.Item1, row.Item2, row.Item3, true, true, rev);
                bool control = Co0Same(off1, off2, out var d0);
                Check(row.Item4 + ": control, a run without the pass equals another", control, d0);
                bool equal = Co0Same(off1, on, out var d1);
                Check(row.Item4 + ": the pass equals LateUpdate, bit for bit", equal, d1);
                bool order = Co0Same(off1, rev, out var d2);
                Check(row.Item4 + ": the pass in the reverse order equals LateUpdate, bit for bit", order, d2);
                string moved = Co0Moved(off1, out int mb, out int ms);
                Check(row.Item4 + ": the run moves, so equal means something", mb > 0, moved);
                Measure($"CO0 {row.Item4}: control {control}, pass {equal}, reverse {order}; {off1.Transforms} transforms, {off1.Shapes} blend shapes, {d1}; {moved}");
                if (row.Item1 != 11) continue;
                // the negative controls, on the moving row
                var seeded = new Co0Data { Name = row.Item4 + " another blink seed" };
                HS2Next.Motion.Face.FaceSetup.Seed = 0x1234;
                yield return Co0Run(app, row.Item1, row.Item2, row.Item3, false, false, seeded);
                HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
                bool same = Co0Same(off1, seeded, out var d3);
                Check("negative control: another blink seed differs", !same, d3);
                var still = new Co0Data { Name = row.Item4 + " physics off" };
                yield return Co0Run(app, row.Item1, row.Item2, row.Item3, false, false, still, physics: false);
                same = Co0Same(off1, still, out var d4);
                Check("negative control: physics off differs", !same, d4);
            }
            yield return PCall(app, new { cmd = "scene.new" });
        }
        finally { Time.captureDeltaTime = old; HS2Next.Motion.Face.FaceSetup.Seed = null; }
    }
}
