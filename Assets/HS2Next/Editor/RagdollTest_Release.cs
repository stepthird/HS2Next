// RagdollTest, what a release leaves (Phase 6.7 package P). Time moves bones, so nothing here compares a run with its own start where a stage is at work.
//   on    the animation stage on, every stage on: a run with a body against a twin with none, same character, same steps, same dt, frame by frame after the release. Every bone by
//         bits but two kinds: a chain is physics with a memory and is judged by coming to rest (RB14); the eye look is an eased state and comes within 1e-5 degrees of the twin
//   off   the animation stage off, so nothing writes the pose again and every restore gives a bone back through the rotation setter: the value a body leaves against a walk of plain
//         self-sets with no body, by bits, at the same count; then one more self-set at the end of the body's run
//   Mune  a bone outside the 16 that differed after a release: which stage writes it, and whether a twin under the same switches shows the same
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class RagdollTest
{
    /// <summary>A still pose of a row: the idle one the other rows use, and the one read since.</summary>
    readonly struct RB12Pose
    {
        public readonly string Name;
        public readonly int Group, Category, No;
        public readonly float Time;
        public RB12Pose(string name, int group, int category, int no, float time) { Name = name; Group = group; Category = category; No = no; Time = time; }
    }

    static readonly RB12Pose IdlePose = new RB12Pose("idle", 19, 0, 0, 0.5f);
    static readonly RB12Pose DancePose = new RB12Pose("dance", 11, 5, 0, 0.3f);
    static readonly RB12Pose ThirdPose = new RB12Pose("row 0/0/0", 0, 0, 0, 0.5f);
    static readonly RB12Pose FourthPose = new RB12Pose("dance at .7", 11, 5, 0, 0.7f);

    static void RB12Row(Host h, string k, RB12Pose pose) =>
        h.Ok(new { cmd = "chara.anime", key = k, group = pose.Group, category = pose.Category, no = pose.No, time = pose.Time, speed = 0f }, "the row of the pose");

    /// <summary>Every transform under a character's root at one moment, in hierarchy order.</summary>
    sealed class RootSnap
    {
        public string[] Names;
        public Vector3[] P, S, W;
        public Quaternion[] R;

        public RootSnap(Transform[] all)
        {
            int n = all.Length;
            Names = new string[n]; P = new Vector3[n]; S = new Vector3[n]; W = new Vector3[n]; R = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var t = all[i];
                Names[i] = t.name;
                t.GetLocalPositionAndRotation(out P[i], out R[i]);
                S[i] = t.localScale;
                W[i] = t.position;
            }
        }

        public bool Same(RootSnap o, int i) => SameBits(P[i], o.P[i]) && SameBits(R[i], o.R[i]) && SameBits(S[i], o.S[i]);
    }

    static Transform[] RootBones(CharaRealization r) => r.Root.GetComponentsInChildren<Transform>(true);

    /// <summary>One chain: its name and its bones, as places in the list of a <see cref="RootSnap"/>.</summary>
    sealed class ChainRow
    {
        public string Name;
        public int[] Bones;
    }

    /// <summary>Every chain of a character, the dynamic bones' and the bust's and hips'.</summary>
    static List<ChainRow> ChainsOf(CharaRealization r, Transform[] all)
    {
        var at = new Dictionary<Transform, int>();
        for (int i = 0; i < all.Length; i++) at[all[i]] = i;
        var rows = new List<ChainRow>();
        void Add(string name, int n, Func<int, Transform> bone)
        {
            var l = new List<int>();
            for (int i = 0; i < n; i++)
            {
                var t = bone(i);
                if (t && at.TryGetValue(t, out int k)) l.Add(k);
            }
            if (l.Count > 0) rows.Add(new ChainRow { Name = name, Bones = l.ToArray() });
        }
        var dyn = r.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>();
        if (dyn != null) foreach (var c in dyn.Chains) Add(c.Root ? c.Root.name : "a chain", c.ParticleCount, c.ParticleTransform);
        var bust = r.Motion.Get<HS2Next.Motion.DynamicBones.BustStage>();
        if (bust != null) foreach (var c in bust.All) if (c != null) Add(c.Root ? c.Root.name : "a chain", c.ParticleCount, c.ParticleTransform);
        return rows;
    }

    // ------------------------------------------------------------ the animation stage on: a twin

    const int LookFrame = 300;
    const double LookBar = 1e-5;
    /// <summary>Under this a chain's bones are at rest, in units a frame.</summary>
    const float ChainRest = 0.01f;
    /// <summary>How a run ends in <see cref="RB12OnRun"/>.</summary>
    enum Ending { AtOnce, AtOnceNoReset, Blended, TwinReset }

    /// <summary>What a body's jump does to the chains, on a character with no body: a reset of the history and nothing else.</summary>
    static void ResetChains(CharaRealization r)
    {
        r.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>()?.Reset();
        foreach (var s in r.Motion.GetAll<HS2Next.Motion.DynamicBones.BustStage>()) s.Reset();
    }

    /// <summary>One run, every stage on, with a body or without: what every frame after the moment of the release holds.</summary>
    static List<RootSnap> RB12OnRun(string rel, string sex, RB12Pose pose, bool body, int hold, int after, Ending ending, string what, out float stood, out double neck, out double head, out List<ChainRow> chains)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, rel, sex, what, physics: true);
            RB12Row(h, k, pose);
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            FixedFloor(h);
            Preset(h, k, RagdollPreset.Limp);
            var neckBone = B(r, "cf_J_Neck");
            var headBone = B(r, "cf_J_Head");
            var neck0 = neckBone.localRotation;
            var head0 = headBone.localRotation;
            stood = 0f;
            if (body)
            {
                var b = Body(h, k, what);
                b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
                h.Frames(hold);
                stood = (b.Part("chest").Tr.position - b.Part("chest").TargetPos).magnitude;
                b.LeaveChains = ending == Ending.AtOnceNoReset;
                h.Part.Release(k, ending == Ending.Blended, ExecuteOptions.NotRecorded);
            }
            else
            {
                h.Frames(hold);
                if (ending == Ending.TwinReset) ResetChains(r);
            }
            neck = Deg(neck0, neckBone.localRotation);
            head = Deg(head0, headBone.localRotation);
            var all = RootBones(r);
            chains = ChainsOf(r, all);
            var frames = new List<RootSnap>(after);
            for (int i = 0; i < after; i++)
            {
                h.Frames(1);
                frames.Add(new RootSnap(all));
            }
            return frames;
        }
        finally { h.Dispose(); }
    }

    static void RB12On(string name, string rel, string sex, RB12Pose pose, int hold)
    {
        const int after = 1200;
        string what = $"{name}, every stage on, {pose.Name}, held {hold} frames";
        var twin = RB12OnRun(rel, sex, pose, false, hold, after, Ending.AtOnce, what + ": the twin", out _, out double neck, out double head, out _);
        var body = RB12OnRun(rel, sex, pose, true, hold, after, Ending.AtOnce, what + ": the body", out float stood, out _, out _, out var chains);
        Check(what + ": both runs hold the same bones", twin[0].Names.SequenceEqual(body[0].Names), twin[0].Names.Length + " and " + body[0].Names.Length);
        int n = twin[0].Names.Length;
        var names = twin[0].Names;
        var chainBone = new HashSet<int>(chains.SelectMany(c => c.Bones));
        bool Look(int i) => names[i].StartsWith("cf_J_look", StringComparison.Ordinal);
        var left = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (chainBone.Contains(i) || Look(i)) continue;
            int last = -1;
            for (int f = 0; f < after; f++) if (!twin[f].Same(body[f], i)) last = f;
            if (last >= 0) left.Add(names[i] + " to frame " + (last + 1));
        }
        var excluded = Enumerable.Range(0, n).Where(i => chainBone.Contains(i) || Look(i)).Select(i => names[i]).ToList();
        Check(what + ": every bone that is neither a chain's nor the look's equals the twin at every frame after the release, bits", left.Count == 0,
            $"{left.Count} of {n - excluded.Count} differ{(left.Count > 0 ? ": " + string.Join(", ", left.Take(8)) : "")}; left out, {excluded.Count} bones: {string.Join(" ", excluded)}");
        var looks = Enumerable.Range(0, n).Where(Look).ToList();
        double At(int f) => looks.Max(i => Deg(twin[f - 1].R[i], body[f - 1].R[i]));
        double most = 0;
        for (int f = LookFrame; f <= after; f++) most = Math.Max(most, At(f));
        Check(what + $": the look bones lie within {LookBar} degrees of the twin by frame {LookFrame}", looks.Count > 0 && At(LookFrame) <= LookBar, $"{looks.Count} bones, frame 1 {At(1):G3}, frame {LookFrame} {At(LookFrame):G3}, frame {after} {At(after):G3} degrees");
        Check(what + $": the look bones do not grow between frames {LookFrame} and {after}", most <= At(LookFrame) * (1.0 + 1e-9) + 1e-12, $"frame {LookFrame} {At(LookFrame):G3}, most since {most:G3} degrees");
        Check(what + ": Present: the neck and the head moved over the run with no body", neck > 0.1 && head > 0.1, $"neck {neck:F3}, head {head:F3} degrees");
        Check(what + ": Present: the body stood at least 10 from its pose", stood >= 10f, stood.ToString("F2"));
    }

    // ------------------------------------------------------------ RB14: where the chains end

    sealed class ChainEnd
    {
        public int Rest, Outside;
        public string Line = "";
        public readonly List<string> Off = new List<string>();
        public readonly List<string> Wide = new List<string>();
        public int Never;
    }

    /// <summary>Each chain of a run against the twin: the frame it comes to rest, how far it lies at <paramref name="at"/>.</summary>
    static ChainEnd Chains(List<RootSnap> twin, List<RootSnap> body, List<ChainRow> chains, int at)
    {
        var e = new ChainEnd();
        var line = new StringBuilder();
        foreach (var c in chains)
        {
            int stop = 0;
            for (int f = 1; f < at; f++)
            {
                float v = 0f;
                foreach (int i in c.Bones) v = Mathf.Max(v, (body[f].W[i] - body[f - 1].W[i]).magnitude);
                if (v >= ChainRest) stop = f + 1;
            }
            double angle = c.Bones.Max(i => Deg(body[at - 1].R[i], twin[at - 1].R[i]));
            double place = c.Bones.Max(i => (double)(body[at - 1].W[i] - twin[at - 1].W[i]).magnitude);
            bool rest = stop < at;
            if (!rest) e.Never++;
            if (rest && angle <= 0.01) e.Rest++; else e.Outside++;
            if (rest && angle > 0.01) e.Off.Add($"{c.Name}: at rest from frame {stop}, {angle:G4} degrees and {place:G4} units from the twin at frame {at}");
            if (rest && angle > 1.0) e.Wide.Add($"{c.Name}: at rest from frame {stop}, {angle:G4} degrees and {place:G4} units from the reference at frame {at}");
            line.Append($"{c.Name} ({c.Bones.Length}): {(rest ? "rest " + stop : "NOT AT REST")}, {angle:G3} deg, {place:G3}; ");
        }
        e.Line = line.ToString();
        return e;
    }

    // A reset drops history on purpose, so the reference is a twin given the same reset at the same frame. The plain twin's numbers stay beside it as information.
    static void RB14AtOnce(string name, string rel, string sex, RB12Pose pose)
    {
        const int at = 300;
        string what = $"{name}, {pose.Name}, a release at once";
        var plain = RB12OnRun(rel, sex, pose, false, 30, at, Ending.AtOnce, what + ": the plain twin", out _, out _, out _, out _);
        var twin = RB12OnRun(rel, sex, pose, false, 30, at, Ending.TwinReset, what + ": the twin with the reset", out _, out _, out _, out _);
        var body = RB12OnRun(rel, sex, pose, true, 30, at, Ending.AtOnce, what + ": the body", out float stood, out _, out _, out var chains);
        var skip = RB12OnRun(rel, sex, pose, true, 30, at, Ending.AtOnceNoReset, what + ": no reset", out _, out _, out _, out _);
        var e = Chains(twin, body, chains, at);
        var p = Chains(plain, body, chains, at);
        var n = Chains(twin, skip, chains, at);
        Measure($"RB14 {what}, {chains.Count} chains at frame {at}, against the twin with the reset: {e.Line}");
        Measure($"RB14 {what}, against the plain twin, information: {p.Outside} of {chains.Count} outside .01 degrees: {string.Join("; ", p.Off)}");
        Check(what + $": every chain at rest and within .01 degrees of the twin with the same reset by frame {at}", e.Outside == 0 && chains.Count > 0, $"{e.Rest} of {chains.Count}");
        Check(what + ": Present: the body stood at least 10 from its pose", stood >= 10f, stood.ToString("F2"));
        Info($"{what}: with no reset {n.Outside} of {chains.Count} chains outside the bar: {string.Join("; ", n.Off)}");
        if (n.Outside > 0) noReset.Add($"{name}, {pose.Name}");
    }

    static readonly List<string> noReset = new List<string>();

    /// <summary>A run of its own script: what every frame after it holds, with the chains of the character.</summary>
    static List<RootSnap> PathRun(string rel, string sex, RB12Pose pose, int after, string what, Action<Host, string, CharaRealization> script, out List<ChainRow> chains)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, rel, sex, what, physics: true);
            RB12Row(h, k, pose);
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            FixedFloor(h);
            script(h, k, r);
            var all = RootBones(r);
            chains = ChainsOf(r, all);
            var frames = new List<RootSnap>(after);
            for (int i = 0; i < after; i++)
            {
                h.Frames(1);
                frames.Add(new RootSnap(all));
            }
            return frames;
        }
        finally { h.Dispose(); }
    }

    static void RB14Judge(string what, List<RootSnap> reference, List<RootSnap> run, List<ChainRow> chains)
    {
        const int at = 300;
        var e = Chains(reference, run, chains, at);
        Measure($"RB14 {what}, {chains.Count} chains at frame {at}: {e.Line}");
        Check(what + $": every chain at rest and within .01 degrees of the reference by frame {at}", e.Outside == 0 && chains.Count > 0, $"{e.Rest} of {chains.Count}{(e.Off.Count > 0 ? ": " + string.Join("; ", e.Off) : "")}{(e.Never > 0 ? ", " + e.Never + " not at rest" : "")}");
    }

    // a body that stays or comes back: its head is physics. The bar is at rest by frame 300; the difference is printed, a numbered WARN above 1 degree
    static void RB14Rest(string what, string against, List<RootSnap> reference, List<RootSnap> run, List<ChainRow> chains)
    {
        const int at = 300;
        var e = Chains(reference, run, chains, at);
        Measure($"RB14 {what}, {chains.Count} chains at frame {at}, against {against}: {e.Line}");
        Check(what + $": every chain at rest by frame {at}", e.Never == 0 && chains.Count > 0, $"{chains.Count - e.Never} of {chains.Count}");
        foreach (var off in e.Wide) Warn($"RB14C {what}, {off}");
    }

    // Snap: the body knocked away, then every part on its target. The reference is a body never knocked, reset at the same frame
    static void RB14Snap(string name, string rel, string sex, RB12Pose pose)
    {
        string what = $"{name}, {pose.Name}, Snap to pose";
        float stood = 0f;
        var body = PathRun(rel, sex, pose, 300, what + ": the body", (h, k, r) =>
        {
            var b = Body(h, k, what);
            b.Knock();
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
            h.Frames(30);
            stood = (b.Part("chest").Tr.position - b.Part("chest").TargetPos).magnitude;
            b.Snap();
        }, out var chains);
        var twin = PathRun(rel, sex, pose, 300, what + ": a body never knocked", (h, k, r) =>
        {
            var b = Body(h, k, what);
            h.Frames(30);
            b.ChainsAfterNextWrite();
        }, out _);
        RB14Rest(what, "a body never knocked", twin, body, chains);
        Check(what + ": Present: the body stood at least 10 from its pose", stood >= 10f, stood.ToString("F2"));
    }

    // the undo of a release: a new body at the kept pose. The reference is the same body that never went, its chains reset at the same frame
    static void RB14UndoRelease(string name, string rel, string sex, RB12Pose pose)
    {
        string what = $"{name}, {pose.Name}, the undo of a release";
        float stood = 0f;
        bool again = false;
        void Fall(Host h, string k, out RagdollBody b)
        {
            Preset(h, k, RagdollPreset.Limp);
            b = Body(h, k, what);
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
            h.Frames(300);
        }
        var run = PathRun(rel, sex, pose, 300, what + ": the undo", (h, k, r) =>
        {
            Fall(h, k, out var b);
            stood = (b.Part("chest").Tr.position - b.Part("chest").TargetPos).magnitude;
            var res = h.Part.Release(k, false, ExecuteOptions.From(CommandSource.User));
            Check(what + ": released", res.Ok && h.Part.BodyOf(k) == null);
            h.Ok(new { cmd = "undo" }, "undo");
            h.Rt.ApplyNow();
            again = h.Part.BodyOf(k) != null;
        }, out var chains);
        var stayed = PathRun(rel, sex, pose, 300, what + ": the body that never went", (h, k, r) =>
        {
            Fall(h, k, out var b);
            b.ChainsAfterNextWrite();
            h.Frames(1);   // the frame an undo's command pumps
        }, out _);
        Check(what + ": the body is back", again);
        RB14Rest(what, "the body that never went", stayed, run, chains);
        Check(what + ": Present: the body stood at least 10 from its pose", stood >= 10f, stood.ToString("F2"));
    }

    // the undo of a build: as a release at once
    static void RB14UndoBuild(string name, string rel, string sex, RB12Pose pose)
    {
        string what = $"{name}, {pose.Name}, the undo of a build";
        float stood = 0f;
        var run = PathRun(rel, sex, pose, 300, what + ": the undo", (h, k, r) =>
        {
            Preset(h, k, RagdollPreset.Limp);
            var res = h.Part.Build(k, ExecuteOptions.From(CommandSource.User));
            var b = h.Part.BodyOf(k);
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
            h.Frames(30);
            stood = (b.Part("chest").Tr.position - b.Part("chest").TargetPos).magnitude;
            h.Ok(new { cmd = "undo" }, "undo");
            h.Rt.ApplyNow();
            Check(what + ": the body went", h.Part.BodyOf(k) == null);
        }, out var chains);
        var twin = PathRun(rel, sex, pose, 300, what + ": the twin", (h, k, r) =>
        {
            Preset(h, k, RagdollPreset.Limp);
            h.Frames(30);
            ResetChains(r);
            h.Frames(1);   // the frame an undo's command pumps
        }, out _);
        RB14Judge(what, twin, run, chains);
        Check(what + ": Present: the body stood at least 10 from its pose", stood >= 10f, stood.ToString("F2"));
    }

    static void RB14Blended(string name, string rel, string sex, RB12Pose pose)
    {
        const int at = 1200;
        string what = $"{name}, {pose.Name}, a blended release";
        var twin = RB12OnRun(rel, sex, pose, false, 30, at, Ending.AtOnce, what + ": the twin", out _, out _, out _, out _);
        var body = RB12OnRun(rel, sex, pose, true, 30, at, Ending.Blended, what + ": the body", out float stood, out _, out _, out var chains);
        var e = Chains(twin, body, chains, at);
        Measure($"RB14 {what}, {chains.Count} chains at frame {at}: {e.Line}");
        foreach (var off in e.Off) Warn($"RB14B {name}, {pose.Name}, {off}");
        Check(what + $": every chain comes to rest by frame {at}", e.Never == 0 && chains.Count > 0, $"{e.Never} of {chains.Count} not at rest");
    }

    static void RB14()
    {
        noReset.Clear();
        foreach (var (name, rel, sex) in Targets())
            foreach (var pose in new[] { IdlePose, DancePose })
                RB14AtOnce(name, rel, sex, pose);
        Check("Present: the same runs with the reset skipped leave a chain outside the bar", noReset.Count > 0, $"on {noReset.Count} of 4 cases: {string.Join("; ", noReset)}: the reset is shown working on those cases only");
        foreach (var (name, rel, sex) in Targets())
            foreach (var pose in new[] { IdlePose, DancePose })
            {
                RB14Snap(name, rel, sex, pose);
                RB14UndoRelease(name, rel, sex, pose);
                RB14UndoBuild(name, rel, sex, pose);
            }
        foreach (var (name, rel, sex) in Targets())
            foreach (var pose in new[] { IdlePose, DancePose })
                RB14Blended(name, rel, sex, pose);
    }

    // ------------------------------------------------------------ the animation stage off: a walk of the setter

    static readonly int[] Counts = { 30, 60, 600, 6000 };

    static Transform[] Sixteen(CharaRealization r) => RagdollParts.All.Select(p => r.Context.BodyBones[p.Bone]).ToArray();

    /// <summary>The walk with no body: every bone of the 16 given its own value again and again, nothing else running. At each count of <see cref="Counts"/> the 16 values.</summary>
    static Dictionary<int, Quaternion[]> Walk(Transform[] bones, out Quaternion[] start)
    {
        start = bones.Select(t => t.localRotation).ToArray();
        var at = new Dictionary<int, Quaternion[]>();
        for (int c = 1; c <= Counts[Counts.Length - 1]; c++)
        {
            foreach (var t in bones) t.localRotation = t.localRotation;
            if (Array.IndexOf(Counts, c) >= 0) at[c] = bones.Select(t => t.localRotation).ToArray();
        }
        return at;
    }

    /// <summary>A run with no body, the animation stage off and every other stage on, frame for frame beside the body's, then the walk from the values the frames left alone.</summary>
    static void RB12OffTwin(string rel, string sex, RB12Pose pose, string what, out Dictionary<int, Quaternion[]> frames, out bool[] written, out Dictionary<int, Quaternion[]> walk, out Quaternion[] start)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, rel, sex, what, physics: true);
            RB12Row(h, k, pose);
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            foreach (var s in r.Motion.Stages) if (s.Name == "anim") s.Enabled = false;
            h.Frames(3);
            FixedFloor(h);
            Preset(h, k, RagdollPreset.Limp);
            var bones = Sixteen(r);
            var begin = bones.Select(t => t.localRotation).ToArray();
            frames = new Dictionary<int, Quaternion[]>();
            int end = Counts[Counts.Length - 1];
            for (int j = 1; j <= end + 2; j++)
            {
                h.Frames(1);
                if (Array.IndexOf(Counts, j) >= 0 || j == end + 2) frames[j] = bones.Select(t => t.localRotation).ToArray();
            }
            written = new bool[16];
            for (int i = 0; i < 16; i++) written[i] = !SameBits(frames[end][i], begin[i]) || !SameBits(frames[Counts[0]][i], begin[i]);
            // the walk starts from what stood before the run: a bone the frames moved is another stage's and takes no part
            for (int i = 0; i < 16; i++) if (written[i]) bones[i].localRotation = begin[i];
            walk = Walk(bones, out start);
        }
        finally { h.Dispose(); }
    }

    static void RB12Off(string name, string rel, string sex, RB12Pose pose)
    {
        string what = $"{name}, animation stage off, {pose.Name}";
        RB12OffTwin(rel, sex, pose, what + ": no body", out var twin, out var written, out var walk, out var start);
        var h = NewHost();
        var scratch = new GameObject("self-set").transform;
        try
        {
            string k = LoadChara(h, rel, sex, what, physics: true);
            RB12Row(h, k, pose);
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            foreach (var s in r.Motion.Stages) if (s.Name == "anim") s.Enabled = false;
            h.Frames(3);
            FixedFloor(h);
            Preset(h, k, RagdollPreset.Limp);
            var bones = Sixteen(r);
            var before = bones.Select(t => t.localRotation).ToArray();
            var b = Body(h, k, what);
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
            int end = Counts[Counts.Length - 1];
            // Base at the write of frame j is what the restores before it left
            var bases = new Dictionary<int, Quaternion[]>();
            Quaternion[] prev = null;
            int passes1 = 0, passesOther = 0;
            var entries = RagdollParts.All.Select(p => b.Part(p.Key).Entry).ToArray();
            for (int j = 1; j <= end; j++)
            {
                h.Frames(1);
                if (j <= 120 || Array.IndexOf(Counts, j - 1) >= 0)
                {
                    var now = entries.Select(e => e.BaseRot).ToArray();
                    if (prev != null && j <= 120)
                        for (int i = 0; i < 16; i++)
                        {
                            if (written[i]) continue;
                            scratch.localRotation = prev[i];
                            if (SameBits(scratch.localRotation, now[i])) passes1++; else passesOther++;
                        }
                    prev = now;
                    if (Array.IndexOf(Counts, j - 1) >= 0) bases[j - 1] = now;
                }
            }
            Measure($"RB12 off, {what}: setter passes per frame: of the {passes1 + passesOther} steps of frames 2 to 120 on the bones no other stage writes, {passes1} equal one pass of the setter on the frame before");
            Check(what + ": one setter pass a frame on each bone", passesOther == 0 && passes1 > 0, passes1 + " of " + (passes1 + passesOther));
            h.Part.Release(k, false, ExecuteOptions.NotRecorded);
            var left = bones.Select(t => t.localRotation).ToArray();
            h.Frames(2);
            var after = bones.Select(t => t.localRotation).ToArray();
            bases[end] = left;

            string[] keys = RagdollParts.All.Select(p => p.Bone).ToArray();
            string others = string.Join(", ", Enumerable.Range(0, 16).Where(i => written[i]).Select(i => keys[i]));
            Info($"{what}: bones another stage writes in the run with no body: {(others.Length == 0 ? "none" : others)}");
            var line = new StringBuilder();
            int bad = 0;
            string firstBad = "";
            foreach (int c in Counts)
            {
                int moved = 0, equal = 0;
                double worst = 0;
                string far = "";
                for (int i = 0; i < 16; i++)
                {
                    if (written[i]) continue;
                    var w = walk[c][i];
                    double d = Deg(start[i], w);
                    if (!SameBits(start[i], w)) moved++;
                    if (d > worst) { worst = d; far = keys[i]; }
                    if (SameBits(w, bases[c][i])) equal++;
                    else if (bad++ == 0) firstBad = $"{keys[i]} at {c}: walk {w.x:R},{w.y:R},{w.z:R},{w.w:R} body {bases[c][i].x:R},{bases[c][i].y:R},{bases[c][i].z:R},{bases[c][i].w:R}";
                }
                line.Append($"{c} sets: {moved} bones off their start, at most {worst:G4} degrees{(far.Length > 0 ? " on " + far : "")}, {equal} equal the body's value; ");
            }
            Measure($"RB12 off, {what}: {line}");
            Check(what + ": the body's value equals the walk with no body at 30, 60, 600, 6000, bits", bad == 0, bad == 0 ? "0 differ" : bad + " differ, first " + firstBad);
            // bones another stage writes: against the run with no body at the same frame
            int stageBad = 0;
            string stageFirst = "";
            for (int i = 0; i < 16; i++)
                if (written[i] && !SameBits(after[i], twin[end + 2][i]) && stageBad++ == 0) stageFirst = keys[i] + " " + Deg(after[i], twin[end + 2][i]).ToString("G4") + " degrees";
            if (others.Length > 0) Check(what + ": a bone another stage writes equals the run with no body, bits", stageBad == 0, stageBad == 0 ? "0 differ" : stageBad + " differ, first " + stageFirst);
            // one more self-set at the end of the run
            int change = 0;
            string which = "";
            for (int i = 0; i < 16; i++)
            {
                var q = bones[i].localRotation;
                bones[i].localRotation = q;
                if (!SameBits(bones[i].localRotation, q)) { change++; which += (which.Length > 0 ? ", " : "") + keys[i] + (written[i] ? " (another stage writes it)" : ""); }
            }
            Check(what + ": at the end of the run one more self-set changes no bit on any of the 16 bones", change == 0, change == 0 ? "0 of 16 change" : change + " of 16 change: " + which);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(scratch.gameObject);
            h.Dispose();
        }
    }

    // ------------------------------------------------------------ where a neck that left its twin left it (a measure, no verdict)

    /// <summary>What the pose sources leave on the neck at every frame, the animation stage off, every other stage on: the bone with no body, the set's base with one.</summary>
    static List<(Quaternion neck, Quaternion head)> NeckRun(string rel, string sex, RB12Pose pose, bool body, int frames, string what)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, rel, sex, what, physics: true);
            RB12Row(h, k, pose);
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            foreach (var s in r.Motion.Stages) if (s.Name == "anim") s.Enabled = false;
            h.Frames(3);
            FixedFloor(h);
            Preset(h, k, RagdollPreset.Limp);
            DrivenBone entry = null;
            if (body)
            {
                var b = Body(h, k, what);
                b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
                entry = b.Part(RagdollParts.Head).Entry;
            }
            var neck = B(r, "cf_J_Neck");
            var head = B(r, "cf_J_Head");
            var l = new List<(Quaternion, Quaternion)>(frames);
            for (int j = 0; j < frames; j++)
            {
                h.Frames(1);
                l.Add((entry != null ? entry.BaseRot : neck.localRotation, head.localRotation));
            }
            return l;
        }
        finally { h.Dispose(); }
    }

    static int RB12NeckWhere(string name, string rel, string sex, RB12Pose pose)
    {
        const int frames = 1200;
        string what = $"{name}, animation stage off, {pose.Name}, the neck";
        var t = NeckRun(rel, sex, pose, false, frames, what + ": no body");
        var b = NeckRun(rel, sex, pose, true, frames, what + ": the body");
        var scratch = new GameObject("self-set").transform;
        int firstNeck = -1, firstHead = -1;
        var notHeld = new List<int>();
        for (int j = 0; j < frames; j++)
        {
            if (firstNeck < 0 && !SameBits(t[j].neck, b[j].neck)) firstNeck = j + 1;
            if (firstHead < 0 && !SameBits(t[j].head, b[j].head)) firstHead = j + 1;
            scratch.localRotation = t[j].neck;
            if (!SameBits(scratch.localRotation, t[j].neck)) notHeld.Add(j + 1);
        }
        UnityEngine.Object.DestroyImmediate(scratch.gameObject);
        var at = new StringBuilder();
        foreach (int j in new[] { 1, 2, 3, 5, 10, 30, 60, 120, 300, 600, 1200 })
            at.Append($"{j}: {Deg(t[j - 1].neck, b[j - 1].neck):G3}, ");
        Measure($"RB12 neck, {what}: what the pose sources leave on the neck at 410 against the run with no body: first differs at frame {firstNeck}, the head {firstHead}; degrees by frame: {at}frames whose neck value in the run with no body does not hold under a self-set: {notHeld.Count} ({string.Join(" ", notHeld.Take(12))})");
        return notHeld.Count;
    }

    // ------------------------------------------------------------ a bone outside the 16

    sealed class WriterTrace : IMotionTimingSink
    {
        public Transform Bone;
        public Quaternion Last;
        public readonly Dictionary<string, int> Changes = new Dictionary<string, int>(StringComparer.Ordinal);

        public void StageStepped(CharaMotion motion, IMotionStage stage, long ticks)
        {
            if (!Bone) return;
            var q = Bone.localRotation;
            if (SameBits(q, Last)) return;
            Last = q;
            Changes.TryGetValue(stage.Name, out int n);
            Changes[stage.Name] = n + 1;
        }

        public override string ToString() => Changes.Count == 0 ? "no stage changed it" : string.Join(", ", Changes.Select(kv => kv.Key + " " + kv.Value));
    }

    /// <summary>The run that showed the bone, its switches as they were: the animation stage, the neck and the eyes off, chains off. Returns the bone's value at the end.</summary>
    static Quaternion MuneRun(string rel, string sex, RB12Pose pose, bool body, string what, out Quaternion before, out string writers, out string stages)
    {
        var h = NewHost();
        var trace = new WriterTrace();
        try
        {
            string k = LoadChara(h, rel, sex, what);
            RB12Row(h, k, pose);
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            foreach (var s in r.Motion.Stages) if (s.Name == "neck" || s.Name == "eyes" || s.Name == "anim") s.Enabled = false;
            h.Frames(3);
            var bone = r.Context.BodyBones["cf_J_Mune00"];
            before = bone.localRotation;
            stages = string.Join(" ", r.Motion.Stages.Where(s => s.Enabled).Select(s => s.Name));
            trace.Bone = bone;
            trace.Last = before;
            CharaMotion.TimingSink = trace;
            FixedFloor(h);
            Preset(h, k, RagdollPreset.Limp);
            if (body)
            {
                var b = Body(h, k, what);
                b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
                h.Frames(30);
                h.Part.Release(k, false, ExecuteOptions.NotRecorded);
            }
            else h.Frames(30);
            h.Frames(2);
            writers = trace.ToString();
            return bone.localRotation;
        }
        finally
        {
            CharaMotion.TimingSink = null;
            h.Dispose();
        }
    }

    static void RB12Mune(string name, string rel, string sex, RB12Pose pose)
    {
        string what = $"{name}, {pose.Name}, cf_J_Mune00";
        var t = MuneRun(rel, sex, pose, false, what + ": the twin", out var t0, out string tw, out string stages);
        var b = MuneRun(rel, sex, pose, true, what + ": the body", out var b0, out string bw, out _);
        Measure($"RB12 Mune00, {what}: stages on: {stages}. Twin: who changed it: {tw}; before against end {Deg(t0, t):G3} degrees, bits {(SameBits(t0, t) ? "equal" : "differ")}. Body: who changed it: {bw}; before against end {Deg(b0, b):G3} degrees, bits {(SameBits(b0, b) ? "equal" : "differ")}. Twin against body at the end {Deg(t, b):G3} degrees, bits {(SameBits(t, b) ? "equal" : "differ")}");
        Check(what + ": the start is the same in both runs", SameBits(t0, b0));
        Check(what + ": the body's run ends as the twin's, bits", SameBits(t, b), Deg(t, b).ToString("G4") + " degrees");
    }

    // ------------------------------------------------------------ the row

    static void RB12()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            RB12On(name, rel, sex, IdlePose, 30);
            RB12On(name, rel, sex, IdlePose, 600);
            RB12On(name, rel, sex, DancePose, 120);
        }
        foreach (var (name, rel, sex) in Targets())
            foreach (var pose in new[] { IdlePose, DancePose, ThirdPose, FourthPose })
                RB12Off(name, rel, sex, pose);
        foreach (var (name, rel, sex) in Targets()) RB12Mune(name, rel, sex, DancePose);
        foreach (var (name, rel, sex) in Targets())
        {
            int held = RB12NeckWhere(name, rel, sex, FourthPose);
            Check(name + ": Present: a pose not used before gives neck values the setter does not hold", held > 0, held + " in 1200 frames");
        }
    }
}
