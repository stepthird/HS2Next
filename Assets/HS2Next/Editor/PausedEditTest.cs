// PausedEditTest (after 1.1.0; owner items 5 and 8 of 2026-11-02
// 13). Headless, edit mode, ManualStepping (SceneRuntime.Tick), on the SFW targets of
// Tools/targets.json (BR-Chan, HERO107), clothed, animation 0/0/0 at speed 0 unless said; vertex positions by CPU skinning
// (BakeMesh does not skin in a -nographics Editor). Files written under Spike/out/pause/sandbox_pe (never deleted).
// Item 5 — a change while the scene is paused shows its settled result at once, the animation on its frame:
//   E1 add at the origin and at the camera pivot = a running add settled (600 frames); the fix switched off shows 1.1.0's fault
//   E2 a paused move keeps every local pose (the bust carried); a move and a turn settle
//   E3 change character (female → male), a card loaded into the node      E4 Maker sliders (bust size; height + bust height)
//   E5 heel shoes (item), shoes off (state), a coordinate with heel shoes  E6 a pose fragment, an animation pattern
//   E7 undo / redo of a bust edit, undo of a delete, undo of FK bones      E8 a scene loaded while paused resumes (R20)
//   E9 running: every bone bit-equal with the fix on and off; paused without an edit: 100 Ticks bit-equal, no settle
//   E10 the settle's cost (≤ 50 ms headless)
// Round 2 — W1 a running add at the camera pivot: the local poses equal the origin add's over the first 60 frames (no whip from
//   the origin; before: > 1 unit on frame 1)   W2 a running gizmo drag bit-equal with the placement carry on and off
//   M1 the character's own chara.motion pause / time scale 0: a bust edit and a move + turn settle as under the global pause
// Item 8 — the switch state.nippleThroughClothes:
//   N1 the default changes nothing (bit-equal, the engine never touched)   N2 off with top and bra on: flat (mm toward the
//   chest, none outward), = the card with the nipple sliders at 0, back on as before   N3 off and uncovered (off, half, the
//   silhouette): the body's own nipples, bit-equal   N4 bone modifiers on the nipple bones left out   N5 several characters,
//   one entry, undo / redo, bridge chara.state, the model path, paused (a settle), the inspector row   N6 the file: no member
//   and revision 1 by default, the member and revision 3 when off, read back, a revision-2 reader   N7 the nipple stand state
//   keeps its effect (1.1.0 lost it at the next step)
//   P8 probe (diagnostics, never FAILs): HS2's nipple mask on the clothes
// Run: -executeMethod PausedEditTest.Run [-only E1+N2]; report Spike/out/pause/paused_edit_report.txt (SUMMARY first line).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class PausedEditTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pause");
    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static string[] only;
    static RuntimeServices services;
    static string femaleCard, maleCard;
    static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };
    const ulong Seed = 0x9a05e0c10c5UL;
    const float Dt = 1f / 60f;

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static void Guard(string id, string title, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            var t = TestTargets.Load();
            femaleCard = TestTargets.Abs(t.visual.female);
            maleCard = TestTargets.Abs(t.visual.male);
            rep.Insert(0, $"Paused edit test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})\n" + head);
            Directory.CreateDirectory(SandboxDir);
            Guard("E1", "a character added while paused (origin, camera pivot)", E1);
            Guard("E2", "a move and a turn while paused", E2);
            Guard("E3", "change character, a card loaded into the node, while paused", E3);
            Guard("E4", "Maker slider edits while paused", E4);
            Guard("E5", "Heelz shoes and a coordinate load while paused", E5);
            Guard("E6", "a pose load and an animation pattern while paused", E6);
            Guard("E7", "undo / redo while paused", E7);
            Guard("E8", "a scene loaded while paused", E8);
            Guard("E9", "the running path unchanged (bit-equal with the fix on and off)", E9);
            Guard("E10", "the settle's cost", E10);
            Guard("W1", "a running add away from the origin starts from the placed pose (no whip)", W1);
            Guard("W2", "a running gizmo drag is unchanged", W2);
            Guard("W3", "an add at the pivot moved before its first frame (UiPanelsTest R2S's sequence)", W3);
            Guard("M1", "the character's own pause and time scale 0 settle too", M1);
            Guard("N1", "nipples under clothes: the default (shown through) changes nothing", N1);
            Guard("N2", "switch off with a top and a bra on: flat nipples, the clothes follow", N2);
            Guard("N3", "switch off without a covering top or bra: the body's own nipples", N3);
            Guard("N4", "bone modifiers on the nipple bones", N4);
            Guard("N5", "several characters, history, bridge, paused, the inspector row", N5);
            Guard("N6", "the scene file (revision 3 only when off; a revision-2 reader)", N6);
            Guard("N7", "the nipple stand state keeps its effect", N7);
            Guard("P8", "probe: the nipple shape masks on the clothes", P8);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "paused_edit_report.txt"), rep.ToString(), new UTF8Encoding(false));
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"PAUSED EDIT TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    static string UserDataDir => Path.Combine(OutDir, "userdata_pe");
    /// <summary>Files the test writes (a coordinate, a pose, a scene); never deleted by the test.</summary>
    static string SandboxDir => Path.Combine(OutDir, "sandbox_pe");

    sealed class Fixture
    {
        public AppCore App;
        public StudioOps Ops => App.Ops;
        public SceneRuntime Rt => App.Runtime;
        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("dispose: " + e.Message); } }
    }

    /// <summary>Where the probes place new characters (the app's default placement is the camera pivot).</summary>
    static readonly Vec3 Pivot = new Vec3(3f, 1f, 2f);

    static Fixture Make(ulong seed = Seed, Vec3? pivot = null)
    {
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = Path.Combine(OutDir, "out_pe");
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        if (pivot.HasValue) { o.NewNodePlacement = NewNodePlacement.CameraPivot; o.DefaultPivot = pivot.Value; }
        var f = new Fixture { App = AppCore.ForTests(services, s, null, o) };
        f.Rt.SetManualStepping(true);
        return f;
    }

    static void Frames(Fixture f, int n, float dt = Dt)
    {
        for (int i = 0; i < n; i++) f.Rt.Tick(dt);
    }

    // ================================================================ dumps

    sealed class Pose
    {
        public readonly List<string> Names = new List<string>();
        public readonly List<Transform> Ts = new List<Transform>();
        public readonly List<Vector3> P = new List<Vector3>(), S = new List<Vector3>(), W = new List<Vector3>();
        public readonly List<Quaternion> R = new List<Quaternion>();
    }

    /// <summary>Local TRS (and world position) of every transform under the character root, pre-order, by name path.</summary>
    static Pose Dump(CharaRealization c)
    {
        var d = new Pose();
        void Walk(Transform t, string path)
        {
            t.GetLocalPositionAndRotation(out var p, out var q);
            d.Names.Add(path); d.Ts.Add(t); d.P.Add(p); d.R.Add(q); d.S.Add(t.localScale); d.W.Add(t.position);
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                seen.TryGetValue(ch.name, out int k); seen[ch.name] = k + 1;
                Walk(ch, path + "/" + ch.name + (k > 0 ? "[" + k + "]" : ""));
            }
        }
        Walk(c.Root.transform, c.Root.name);
        return d;
    }

    static string Leaf(string path) { int k = path.LastIndexOf('/'); string l = k >= 0 ? path.Substring(k + 1) : path; int b = l.IndexOf('['); return b > 0 ? l.Substring(0, b) : l; }

    static double Ang(Quaternion a, Quaternion b)
    {
        double dot = Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w);
        double na = Math.Sqrt((double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w);
        double nb = Math.Sqrt((double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w);
        dot /= Math.Max(na * nb, 1e-30);
        return dot >= 1 ? 0 : 2 * Math.Acos(dot) * 180 / Math.PI;
    }

    /// <summary>Per-transform differences: position (m, local), rotation (deg, local), scale, world position (m).</summary>
    sealed class PoseDiff
    {
        public int Count, Over;
        public double MaxPos, MaxRot, MaxScale, MaxWorld;
        public readonly List<(string name, double pos, double rot, double scl, double world, string cls)> Rows = new List<(string, double, double, double, double, string)>();
    }

    static PoseDiff Compare(Pose a, Pose b, Func<string, string> classify, double tolPos = 1e-4, double tolRot = 0.01, double tolScale = 1e-4)
    {
        var d = new PoseDiff();
        var ib = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < b.Names.Count; i++) ib[b.Names[i]] = i;
        for (int i = 0; i < a.Names.Count; i++)
        {
            if (!ib.TryGetValue(a.Names[i], out int j)) continue;
            d.Count++;
            double dp = (a.P[i] - b.P[j]).magnitude, dr = Ang(a.R[i], b.R[j]), ds = (a.S[i] - b.S[j]).magnitude, dw = (a.W[i] - b.W[j]).magnitude;
            d.MaxPos = Math.Max(d.MaxPos, dp); d.MaxRot = Math.Max(d.MaxRot, dr); d.MaxScale = Math.Max(d.MaxScale, ds); d.MaxWorld = Math.Max(d.MaxWorld, dw);
            if (dp > tolPos || dr > tolRot || ds > tolScale)
            {
                d.Over++;
                d.Rows.Add((a.Names[i], dp, dr, ds, dw, classify != null ? classify(Leaf(a.Names[i])) : ""));
            }
        }
        return d;
    }

    static string Describe(PoseDiff d, int max = 30)
    {
        var sb = new StringBuilder();
        sb.Append($"{d.Over} of {d.Count} transforms over tolerance; max local pos {d.MaxPos:E3} m, rot {d.MaxRot:F4} deg, scale {d.MaxScale:E3}, world pos {d.MaxWorld:E3} m");
        foreach (var g in d.Rows.GroupBy(r => r.cls).OrderBy(g => g.Key, StringComparer.Ordinal))
            sb.Append($"\n      class {g.Key}: {g.Count()} transforms, max pos {g.Max(r => r.pos):E3}, rot {g.Max(r => r.rot):F4}, scale {g.Max(r => r.scl):E3}, world {g.Max(r => r.world):E3}");
        foreach (var r in d.Rows.OrderByDescending(r => Math.Max(r.world * 1000, Math.Max(r.rot, r.scl * 1000))).Take(max))
        {
            string leaf = r.name.Substring(r.name.LastIndexOf('/') + 1);
            sb.Append($"\n      {leaf} [{r.cls}] pos {r.pos:E3} rot {r.rot:F4} scl {r.scl:E3} world {r.world:E3}");
        }
        return sb.ToString();
    }

    /// <summary>The motion owner of a transform: bust chain, dynamic-bone chain (category), shape bone, heels, other.</summary>
    static Func<string, string> Classifier(CharaRealization c)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var bust = c.Motion?.Get<BustStage>();
        if (bust != null)
            foreach (var ch in bust.All)
                for (int i = 0; i < ch.ParticleCount; i++) { var t = ch.ParticleTransform(i); if (t && !map.ContainsKey(t.name)) map[t.name] = "bust"; }
        var db = c.Motion?.Get<DynamicBoneStage>();
        if (db != null)
            foreach (var ch in db.Chains)
                for (int i = 0; i < ch.ParticleCount; i++) { var t = ch.ParticleTransform(i); if (t && !map.ContainsKey(t.name)) map[t.name] = "dyn." + ch.Category; }
        if (c.Animation)
            foreach (var t in c.Animation.ShapeBones) if (t && !map.ContainsKey(t.name)) map[t.name] = "shape";
        return n => map.TryGetValue(n, out var s) ? s : n.StartsWith("cf_J_") || n.StartsWith("cm_J_") ? "skeleton" : "other";
    }

    static string StageList(CharaRealization c) =>
        c?.Motion == null ? "-" : string.Join(", ", c.Motion.Stages.Select(s => s.Name + "@" + (int)s.Phase + (s.Enabled ? "" : "(off)")));

    // ================================================================ cases (item 5)

    /// <summary>The bones the eye / neck look turns (time-based, not settled while paused: reported apart).</summary>
    static bool IsLook(string leaf) => leaf.Contains("_look_") || leaf.StartsWith("cf_J_Neck") || leaf.StartsWith("cm_J_Neck");

    /// <summary>The settled pose vs a reference: every transform within tolerance except the look's; the look reported.</summary>
    static bool Matches(string what, Pose got, Pose reference, Func<string, string> cls, double tolPos = 1e-4, double tolRot = 0.01, double tolScale = 1e-4)
    {
        var d = Compare(got, reference, cls, tolPos, tolRot, tolScale);
        // a chain bone's roll about its own axis follows the history (free in the simulation): chain bones count by position
        // (0.1 mm) and a roll up to 0.5 deg
        bool ChainOk((string name, double pos, double rot, double scl, double world, string cls) r) =>
            r.cls.StartsWith("dyn.", StringComparison.Ordinal) && r.world <= 1e-3 && r.pos <= 1e-3 && r.rot <= 0.5 && r.scl <= tolScale;
        var bad = d.Rows.Where(r => !IsLook(Leaf(r.name)) && !ChainOk(r)).ToList();
        var rolls = d.Rows.Where(r => !IsLook(Leaf(r.name)) && ChainOk(r)).ToList();
        var look = d.Rows.Where(r => IsLook(Leaf(r.name))).ToList();
        string lookText = look.Count == 0 ? "" : $"; look bones apart: {look.Count}, max {look.Max(r => r.rot):F3} deg";
        if (rolls.Count > 0) lookText += $"; chain bones within 0.1 mm and 0.5 deg of roll: {rolls.Count} (max {rolls.Max(r => r.rot):F3} deg, {rolls.Max(r => r.world) * 100:F4} mm)";
        return Check(what, d.Count > 0 && bad.Count == 0, $"{d.Count} transforms, {bad.Count} over (pos {tolPos:E0}, rot {tolRot} deg, scale {tolScale:E0}); max pos {d.MaxPos:E2}, rot {d.MaxRot:F4}, world {d.MaxWorld:E2}{lookText}" +
            (bad.Count > 0 ? "\n" + Describe(PoseDiffView(bad, d.Count), 12) : ""));
    }

    static PoseDiff PoseDiffView(List<(string name, double pos, double rot, double scl, double world, string cls)> rows, int count)
    {
        var d = new PoseDiff { Count = count, Over = rows.Count };
        foreach (var r in rows) { d.Rows.Add(r); d.MaxPos = Math.Max(d.MaxPos, r.pos); d.MaxRot = Math.Max(d.MaxRot, r.rot); d.MaxScale = Math.Max(d.MaxScale, r.scl); d.MaxWorld = Math.Max(d.MaxWorld, r.world); }
        return d;
    }

    /// <summary>The motion's time, the animation's normalized time and the clock: what a settle must not advance.</summary>
    static (double clock, float motion, float anime, int settles) Times(Fixture f, CharaRealization c) =>
        (f.Rt.Clock.Time, c.Motion ? c.Motion.Time : -1f, c.Animation ? c.Animation.NormalizedTime : -1f, c.Motion ? c.Motion.Settles : -1);

    const int SettleFrames = 120, RefFrames = 600;

    /// <summary>
    /// A paused case: <paramref name="setup"/> (running, speed 0, then 120 frames), pause, <paramref name="op"/> (returns the
    /// character to compare), two paused Ticks. The reference: the same setup, the op at once (no frame between: no history of
    /// a transition, which a settle drops; hair resting on the body can come to rest elsewhere after a swing), then 600 frames. Checks
    /// the pose (tolerance, look apart), that the animation and the clocks stood still, and that a settle ran.
    /// </summary>
    static void PausedCase(string what, Func<Fixture, string> setup, Func<Fixture, string, string> op, Vec3? pivot = null)
    {
        Pose got, reference;
        Func<string, string> cls;
        var a = Make(Seed, pivot);
        try
        {
            string id = setup(a);
            Frames(a, SettleFrames);
            a.Rt.SetPaused(true);
            Frames(a, 1);
            var c0 = a.Rt.FindChara(id);
            var m0 = c0 != null ? c0.Motion : null;
            var t0 = c0 != null ? Times(a, c0) : default;
            string cid = op(a, id);
            Frames(a, 2);
            var c = a.Rt.FindChara(cid);
            if (!Check(what + ": the character is built", c != null && c.State == RealizationState.Built, c?.State.ToString() ?? "none")) return;
            cls = Classifier(c);
            got = Dump(c);
            var t1 = Times(a, c);
            bool same = c == c0 && c.Motion == m0;   // a rebuild makes a new motion (time 0, its animation restored from the node)
            Check(what + ": the clock, the motion time and the animation frame stood still" + (same ? "" : " (rebuilt: a new motion, the node's animation time)"),
                t1.clock == t0.clock && (same ? t1.motion == t0.motion && t1.anime == t0.anime : t1.motion == 0f && t1.anime == t0.anime),
                $"clock {t0.clock:R} → {t1.clock:R}, motion {t0.motion:R} → {t1.motion:R}, anime {t0.anime:R} → {t1.anime:R}");
            int settles = c.Motion.Settles - (same ? t0.settles : 0);
            Check(what + ": a settle ran (none pending)", settles >= 1 && !c.Motion.SettlePending && a.Rt.PendingSettles == 0,
                $"settles {settles}, last {c.Motion.LastSettleMs:F1} ms");
            Frames(a, 3);
            Check(what + ": 3 more paused Ticks change nothing (bit-equal)", Bits(got).SequenceEqual(Bits(Dump(c))));
        }
        finally { a.Dispose(); }
        var b = Make(Seed, pivot);
        try
        {
            string id = setup(b);
            string cid = op(b, id);   // before any frame: the settled state without the history of a transition (a settle drops it)
            Frames(b, RefFrames);
            var c = b.Rt.FindChara(cid);
            reference = Dump(c);
        }
        finally { b.Dispose(); }
        Matches(what + ": the paused result = the changed character running from its first frame and settled (" + RefFrames + " frames)", got, reference, cls);
    }

    static string AddCard(Fixture f, string card, float speed = 0f)
    {
        var id = f.Ops.AddChara(card, null, null, -1, null, User).NewIds.FirstOrDefault();
        f.Ops.SetAnimeSpeed(new[] { id }, speed, null, User);
        return id;
    }

    // ---------------------------------------------------------------- E1 add

    static void E1()
    {
        foreach (var piv in new Vec3?[] { null, Pivot })
        {
            string where = piv.HasValue ? "at the camera pivot (3, 1, 2)" : "at the origin";
            // the paused add: the add happens after the pause (setup adds nothing)
            Pose got, reference;
            Func<string, string> cls;
            var a = Make(Seed, piv);
            try
            {
                Frames(a, 3);
                a.Rt.SetPaused(true);
                double clock = a.Rt.Clock.Time;
                var id = AddCard(a, femaleCard);
                var c = a.Rt.FindChara(id);
                float anime = c.Animation.NormalizedTime, mt = c.Motion.Time;
                Frames(a, 1);
                cls = Classifier(c);
                got = Dump(c);
                Check($"E1 add {where} while paused: one settle at the first paused step ({c.Motion.LastSettleMs:F1} ms), the animation on its frame, the clocks still",
                    c.Motion.Settles == 1 && c.Animation.NormalizedTime == anime && c.Motion.Time == mt && a.Rt.Clock.Time == clock,
                    $"settles {c.Motion.Settles}, anime {anime:R} → {c.Animation.NormalizedTime:R}, motion {mt:R} → {c.Motion.Time:R}");
                Frames(a, 5);
                Check($"E1 {where}: 5 more paused Ticks bit-equal", Bits(got).SequenceEqual(Bits(Dump(c))));
                a.Rt.SetPaused(false);
                Frames(a, 1);
                var resumed = Compare(got, Dump(c), cls);
                Info($"E1 {where}: resume + 1 frame (speed 0): " + Describe(resumed, 4));
            }
            finally { a.Dispose(); }
            var b = Make(Seed, piv);
            try
            {
                Frames(b, 3);
                var id = AddCard(b, femaleCard);
                Frames(b, RefFrames);
                reference = Dump(b.Rt.FindChara(id));
            }
            finally { b.Dispose(); }
            Matches($"E1 add {where} while paused = the running add settled ({RefFrames} frames, animation speed 0)", got, reference, cls);
            // the switches off: the behaviour before (the test sees the fault)
            var o = Make(Seed, piv);
            bool sw1 = CharaRealization.SettleWhilePaused, sw2 = BustChain.CarryWithoutTime, sw3 = CharaBuilders.CarryChainsToPlacement;
            try
            {
                CharaRealization.SettleWhilePaused = false; BustChain.CarryWithoutTime = false; CharaBuilders.CarryChainsToPlacement = false;
                Frames(o, 3);
                o.Rt.SetPaused(true);
                var id = AddCard(o, femaleCard);
                Frames(o, 5);
                var d = Compare(Dump(o.Rt.FindChara(id)), reference, cls);
                double bustWorld = d.Rows.Where(r => r.cls == "bust").Select(r => r.world).DefaultIfEmpty(0).Max();
                double hairRot = d.Rows.Where(r => r.cls == "dyn.Hair").Select(r => r.rot).DefaultIfEmpty(0).Max();
                Check($"E1 {where}, the fix switched off (1.1.0's behaviour): the bust / hip chains off by {bustWorld * 100:F1} mm, the hair by {hairRot:F1} deg",
                    piv.HasValue ? bustWorld > 1.0 : bustWorld > 1e-3 && hairRot > 1, Describe(d, 6));
            }
            finally { CharaRealization.SettleWhilePaused = sw1; BustChain.CarryWithoutTime = sw2; CharaBuilders.CarryChainsToPlacement = sw3; o.Dispose(); }
        }
    }

    // ---------------------------------------------------------------- E2 move / turn

    static void E2()
    {
        // a paused move carries the chains at once (no settle needed for a translation); the drag's end settles
        var f = Make(Seed);
        try
        {
            var id = AddCard(f, femaleCard);
            Frames(f, SettleFrames);
            var c = f.Rt.FindChara(id);
            var cls = Classifier(c);
            f.Rt.SetPaused(true);
            Frames(f, 1);
            var before = Dump(c);
            bool sw = CharaRealization.SettleWhilePaused;
            try
            {
                CharaRealization.SettleWhilePaused = false;   // the carry alone (as during a drag)
                f.Ops.SetTransform(new[] { id }, new Vector3(2f, 0f, 0f), null, null, User);
                Frames(f, 1);
            }
            finally { CharaRealization.SettleWhilePaused = sw; }
            var d = Compare(before, Dump(c), cls, 1e-4, 0.01, 1e-4);
            Check("E2 a paused move by (2, 0, 0) without a settle (a drag frame): every local pose kept (the bust and hips carried)", d.Over == 0, Describe(d, 6));
        }
        finally { f.Dispose(); }
        PausedCase("E2 move by (2, 0, 0) and turn by 90 deg while paused", fx => AddCard(fx, femaleCard),
            (fx, id) => { fx.Ops.SetTransform(new[] { id }, new Vector3(2f, 0f, 0f), new Vector3(0f, 90f, 0f), null, User); return id; });
    }

    // ---------------------------------------------------------------- E3 change character, load a card into the node

    static void E3()
    {
        PausedCase("E3 change character (female → male card) while paused", fx => AddCard(fx, femaleCard),
            (fx, id) => { var r = fx.Ops.ChangeCharacter(new[] { id }, maleCard, null, User); Check("E3 ChangeCharacter ok", r.Ok, r.ToString()); return id; });
        PausedCase("E3 load the card into the node (all five parts) after a bust edit, while paused",
            fx => { var id = AddCard(fx, femaleCard); fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.95f) }, User); return id; },
            (fx, id) => { var r = fx.Ops.LoadCharacter(new[] { id }, femaleCard, LoadFlags.All, User); Check("E3 LoadCharacter ok", r.Ok, r.ToString()); return id; });
    }

    // ---------------------------------------------------------------- E4 Maker slider edits

    static void E4()
    {
        PausedCase("E4 Maker slider: bust size 0.95 while paused", fx => AddCard(fx, femaleCard),
            (fx, id) => { var r = fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.95f) }, User); Check("E4 edit ok", r.Ok, r.ToString()); return id; });
        PausedCase("E4 Maker slider: height 0.9 and bust height 0.2 while paused", fx => AddCard(fx, femaleCard),
            (fx, id) => { var r = fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[0]", 0.9f), ("appearance.chara.body.shape[2]", 0.2f) }, User); Check("E4 edit ok", r.Ok, r.ToString()); return id; });
    }

    // ---------------------------------------------------------------- E5 coordinate load, Heelz shoes

    static ListEntry HeelRow()
    {
        var lists = MakerCatalog.For(services.Catalog);
        return services.Catalog.EntriesOf(HeelsData.FemaleShoesCategory).Where(e => HeelsData.For(e) != null && lists.IsListed(247, 1, e.Key))
            .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal).FirstOrDefault(e => HeelsData.For(e).Root.y > 0);
    }

    static string ShoesPath => "appearance.coord.clothes." + ClothesSet.Names[7] + ".item";

    static void E5()
    {
        var row = HeelRow();
        if (!Check("E5 an installed heel shoe row", row != null)) return;
        Info("E5 heel shoes " + row.Key + " (lift " + HeelsData.For(row).Root.y.ToString("R") + ")");
        PausedCase("E5 a Heelz shoe change while paused (shoes item → heel row)", fx => AddCard(fx, femaleCard),
            (fx, id) => { var r = fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (ShoesPath, row.Key.ToString()) }, User); Check("E5 shoes edit ok", r.Ok, r.ToString()); return id; });
        PausedCase("E5 heel shoes taken off (clothes state) while paused",
            fx => { var id = AddCard(fx, femaleCard); fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (ShoesPath, row.Key.ToString()) }, User); return id; },
            (fx, id) => { var r = fx.Ops.SetClothes(new[] { id }, 7, 2, false, User); Check("E5 shoes off ok", r.Ok, r.ToString()); return id; });
        // the coordinate: BR-Chan's outfit with the heel shoes, saved in the sandbox, worn while paused
        string coord = Path.Combine(SandboxDir, "e5_heels.hs2ncoord");
        PausedCase("E5 coordinate load (the outfit with heel shoes) while paused",
            fx =>
            {
                var id = AddCard(fx, femaleCard);
                if (!File.Exists(coord))
                {
                    fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (ShoesPath, row.Key.ToString()) }, User);
                    fx.Rt.Tick(Dt);
                    var s = fx.Ops.SaveCoordinate(id, coord, "e5", false, () => new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
                    Check("E5 coordinate saved in the sandbox", s.Ok && File.Exists(coord), s.ToString());
                    fx.App.Session.Commands.Undo();
                }
                return id;
            },
            (fx, id) => { var r = fx.Ops.ApplyCoordinateFile(new[] { id }, coord, CoordinateApplyMode.All, User); Check("E5 coordinate applied", r.Ok, r.ToString()); return id; });
    }

    // ---------------------------------------------------------------- E6 pose load

    static void E6()
    {
        string pose = Path.Combine(SandboxDir, "e6_pose.hs2npose");
        var s = Make(Seed);
        try
        {
            var id = AddCard(s, femaleCard);
            s.Ops.SetFkEnabled(id, true, User);
            s.Ops.SetFkBones(id, new Dictionary<string, Vec3> { ["cf_J_Spine02"] = new Vec3(25, 0, 0), ["cf_J_Neck"] = new Vec3(0, 30, 0), ["cf_J_ArmUp00_L"] = new Vec3(0, 0, -50) }, User);
            s.Rt.Tick(Dt);
            var r = PoseFragmentOps.Save(s.Rt, id, pose, "e6", null, true, new SaveOptions { Clock = () => new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder });
            if (!Check("E6 a pose fragment saved in the sandbox", r != null && r.Ok && File.Exists(pose), r?.Error ?? "")) return;
        }
        finally { s.Dispose(); }
        PausedCase("E6 pose load (FK spine, neck, arm) while paused", fx => AddCard(fx, femaleCard),
            (fx, id) => { var r = fx.Ops.LoadPoseFragment(id, pose, null, User); Check("E6 pose loaded", r.Ok, r.ToString()); return id; });
        PausedCase("E6 animation pattern 1 while paused (a pose change of the animation)", fx => AddCard(fx, femaleCard),
            (fx, id) => { var r = fx.Ops.SetAnimePattern(new[] { id }, 1f, User); Check("E6 pattern ok", r.Ok, r.ToString()); return id; });
    }

    // ---------------------------------------------------------------- E7 undo / redo

    static void E7()
    {
        PausedCase("E7 undo of a bust edit while paused", fx => { var id = AddCard(fx, femaleCard); fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.95f) }, User); fx.Rt.Tick(Dt); return id; },
            (fx, id) => { var r = fx.App.Session.Commands.Undo(); Check("E7 undo ok", r.Status != CommandStatus.Error, r.ToString()); return id; });
        PausedCase("E7 redo of a bust edit while paused", fx => { var id = AddCard(fx, femaleCard); fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.95f) }, User); fx.Rt.Tick(Dt); fx.App.Session.Commands.Undo(); fx.Rt.Tick(Dt); return id; },
            (fx, id) => { var r = fx.App.Session.Commands.Redo(); Check("E7 redo ok", r.Status != CommandStatus.Error, r.ToString()); return id; });
        PausedCase("E7 undo of a delete (the character revived) while paused", fx => AddCard(fx, femaleCard),
            (fx, id) => { fx.Ops.Delete(new[] { id }, User); fx.Rt.Tick(Dt); var r = fx.App.Session.Commands.Undo(); Check("E7 undo of the delete ok", r.Status != CommandStatus.Error, r.ToString()); return id; });
        PausedCase("E7 undo of a pose load while paused", fx => { var id = AddCard(fx, femaleCard); fx.Ops.SetFkEnabled(id, true, User); fx.Ops.SetFkBones(id, new Dictionary<string, Vec3> { ["cf_J_Spine02"] = new Vec3(25, 0, 0) }, User); fx.Rt.Tick(Dt); return id; },
            (fx, id) => { var r = fx.App.Session.Commands.Undo(); Check("E7 undo of the FK bones ok", r.Status != CommandStatus.Error, r.ToString()); return id; });
    }

    // ---------------------------------------------------------------- E8 scene load while paused

    static void E8()
    {
        var f = Make(Seed, Pivot);
        try
        {
            var id = AddCard(f, femaleCard);
            Frames(f, 10);
            string path = Path.Combine(SandboxDir, "e8.hs2nscene");
            var sv = f.Ops.SaveScene(path, new SaveOptions { Clock = () => new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder });
            Check("E8 scene saved in the sandbox", sv.Ok && File.Exists(path), sv.ToString());
            f.Rt.SetPaused(true);
            var ld = f.Ops.LoadScene(path);
            Frames(f, 1);
            var c = f.Rt.Charas().FirstOrDefault();
            Check("E8 loading a scene while paused resumes the scene (the pause is not saved; new / load reset it)", ld.Ok && !f.Rt.Paused && c != null && c.State == RealizationState.Built && c.Motion.Settles == 0,
                $"paused {f.Rt.Paused}, built {c?.State}, settles {c?.Motion?.Settles}");
        }
        finally { f.Dispose(); }
    }

    // ---------------------------------------------------------------- E9 the running path is unchanged

    static List<int> Bits(Pose p)
    {
        var l = new List<int>(p.Names.Count * 10);
        for (int i = 0; i < p.Names.Count; i++)
        {
            var a = p.P[i]; var q = p.R[i]; var s = p.S[i];
            foreach (var v in new[] { a.x, a.y, a.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z }) l.Add(BitConverter.SingleToInt32Bits(v));
        }
        return l;
    }

    static List<int> RunningScript(bool switches)
    {
        bool sw1 = CharaRealization.SettleWhilePaused, sw2 = BustChain.CarryWithoutTime;
        CharaRealization.SettleWhilePaused = switches; BustChain.CarryWithoutTime = switches;
        var f = Make(Seed, Pivot);
        try
        {
            var id = AddCard(f, femaleCard, 1f);
            Frames(f, 40);
            f.Ops.SetTransform(new[] { id }, new Vector3(1f, 0f, 0f), new Vector3(0f, 30f, 0f), null, User);
            f.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.9f) }, User);
            Frames(f, 40);
            f.Ops.SetFkEnabled(id, true, User);
            f.Ops.SetFkBones(id, new Dictionary<string, Vec3> { ["cf_J_Spine02"] = new Vec3(20, 0, 0) }, User);
            Frames(f, 40);
            var c = f.Rt.FindChara(id);
            var bits = Bits(Dump(c));
            bits.Add(c.Motion.Settles);
            return bits;
        }
        finally { f.Dispose(); CharaRealization.SettleWhilePaused = sw1; BustChain.CarryWithoutTime = sw2; }
    }

    static void E9()
    {
        var on = RunningScript(true);
        var off = RunningScript(false);
        int diff = on.Count == off.Count ? on.Zip(off, (x, y) => x != y ? 1 : 0).Sum() : -1;
        Check("E9 running (add, move, turn, bust edit, FK edit, 120 frames, speed 1): every bone bit-equal with the fix on and off, no settle",
            diff == 0 && on[on.Count - 1] == 0, $"{on.Count - 1} values, {diff} differ, settles {on[on.Count - 1]}");
        // paused without edits: nothing settles, 100 Ticks bit-equal (PauseTest K1's rule)
        var f = Make(Seed);
        try
        {
            var id = AddCard(f, femaleCard, 1f);
            Frames(f, 30);
            f.Rt.SetPaused(true);
            Frames(f, 1);
            var c = f.Rt.FindChara(id);
            var p0 = Bits(Dump(c));
            Frames(f, 100);
            var p1 = Bits(Dump(c));
            Check("E9 paused without an edit: no settle, 100 Ticks bit-equal", c.Motion.Settles == 0 && p0.SequenceEqual(p1), "settles " + c.Motion.Settles);
        }
        finally { f.Dispose(); }
    }

    // ---------------------------------------------------------------- E10 costs

    static void E10()
    {
        var f = Make(Seed);
        try
        {
            var id = AddCard(f, femaleCard);
            Frames(f, 10);
            f.Rt.SetPaused(true);
            var m = AddCard(f, maleCard);
            Frames(f, 1);
            var ms = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                f.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.5f + 0.05f * i) }, User);
                Frames(f, 1);
                ms.Add(f.Rt.FindChara(id).Motion.LastSettleMs);
            }
            ms.Sort();
            Info($"E10 settle of BR-Chan ({CharaMotion.SettleSteps} × {CharaMotion.SettleDt:R} s): median {ms[2]:F1} ms (min {ms[0]:F1}, max {ms[4]:F1}); HERO107's settle at its add {f.Rt.FindChara(m).Motion.LastSettleMs:F1} ms");
            Check("E10 a settle ≤ 50 ms (headless Editor)", ms[2] <= 50, ms[2].ToString("F1") + " ms");
        }
        finally { f.Dispose(); }
    }

    // ================================================================ round 2

    // ---------------------------------------------------------------- W1 a running add away from the origin

    static readonly int[] WFrames = { 1, 2, 5, 10, 20, 30, 60 };

    static Dictionary<int, Pose> RunningAdd(Vec3? pivot, bool carry, out Func<string, string> cls)
    {
        bool sw = CharaBuilders.CarryChainsToPlacement;
        var f = Make(Seed, pivot);
        var d = new Dictionary<int, Pose>();
        try
        {
            CharaBuilders.CarryChainsToPlacement = carry;
            Frames(f, 3);
            var id = AddCard(f, femaleCard);
            var c = f.Rt.FindChara(id);
            cls = Classifier(c);
            int done = 0;
            foreach (int k in WFrames.Concat(new[] { 1200 })) { Frames(f, k - done); done = k; d[k] = Dump(c); }
        }
        finally { CharaBuilders.CarryChainsToPlacement = sw; f.Dispose(); }
        return d;
    }

    static double BustWorld(PoseDiff d) => d.Rows.Where(r => r.cls == "bust").Select(r => r.world).DefaultIfEmpty(0).Max();

    static void W1()
    {
        var o = RunningAdd(null, true, out var cls);
        var on = RunningAdd(Pivot, true, out _);
        var off = RunningAdd(Pivot, false, out _);
        var table = new StringBuilder();
        bool local = true;
        foreach (int k in WFrames)
        {
            double b = BustWorld(Compare(off[k], off[1200], cls)), a = BustWorld(Compare(on[k], on[1200], cls)), z = BustWorld(Compare(o[k], o[1200], cls));
            var lp = Compare(on[k], o[k], cls);
            local &= lp.Over == 0;
            table.Append($"\n      frame {k,4}: bust / hip bones off their rest by {b * 100,8:F2} mm before, {a * 100,7:F2} mm after (the origin add {z * 100,7:F2} mm); " +
                         $"local pose at the pivot vs the origin add: {lp.Over} over, max pos {lp.MaxPos:E1}, rot {lp.MaxRot:F4} deg");
        }
        Check("W1 a running add at the camera pivot (3, 1, 2): every local pose as the origin add's over the first 60 frames (the chains start from the placed pose)", local, table.ToString());
        double b1 = BustWorld(Compare(off[1], off[1200], cls)), a1 = BustWorld(Compare(on[1], on[1200], cls)), o1 = BustWorld(Compare(o[1], o[1200], cls));
        Check("W1 frame 1: the whip from the origin gone (before > 1 unit; after = the origin add within 0.1 mm)", b1 > 1.0 && Math.Abs(a1 - o1) <= 1e-3, $"before {b1 * 100:F1} mm, after {a1 * 100:F2} mm, origin {o1 * 100:F2} mm");
        var settledBoth = Compare(on[1200], off[1200], cls);
        Info("W1 after 1200 frames, with the carry vs without: " + Describe(settledBoth, 4));
    }

    // ---------------------------------------------------------------- W2 a running drag is unchanged

    static List<int> RunningDrag(bool carry, Vec3? pivot)
    {
        bool sw = CharaBuilders.CarryChainsToPlacement;
        var f = Make(Seed, pivot);
        try
        {
            CharaBuilders.CarryChainsToPlacement = carry;
            var id = AddCard(f, femaleCard, 1f);
            Frames(f, 60);
            // a gizmo drag: one bus transaction, a transform each frame
            var tx = f.App.Session.Commands.BeginOrJoin("Move", User);
            using (tx)
            {
                for (int i = 1; i <= 30; i++)
                {
                    f.Ops.SetTransform(new[] { id }, new Vector3(0.1f * i, 0f, 0.05f * i), new Vector3(0f, 2f * i, 0f), null, User);
                    Frames(f, 1);
                }
                tx.Commit();
            }
            Frames(f, 30);
            var c = f.Rt.FindChara(id);
            var bits = Bits(Dump(c));
            bits.Add(c.Motion.Settles);
            return bits;
        }
        finally { CharaBuilders.CarryChainsToPlacement = sw; f.Dispose(); }
    }

    static void W2()
    {
        var a = RunningDrag(true, null);
        var b = RunningDrag(false, null);
        int diff = a.Count == b.Count ? a.Zip(b, (x, y) => x != y ? 1 : 0).Sum() : -1;
        Check("W2 a running gizmo drag (30 frames in one transaction, moving and turning, then 30 frames, speed 1): every bone bit-equal with the placement carry on and off, no settle",
            diff == 0 && a[a.Count - 1] == 0, $"{a.Count - 1} values, {diff} differ, settles {a[a.Count - 1]}");
        // at the pivot the build differs (that is the fix), the drag code path is the same: after it the two settle alike
        var p = RunningDrag(true, Pivot);
        Check("W2 the same drag of a character added at the pivot runs (no settle while running)", p[p.Count - 1] == 0);
    }

    // ---------------------------------------------------------------- W3 added at the pivot, moved before its first frame

    /// <summary>UiPanelsTest R2S's sequence: an add at the pivot, moved to the origin in the same frame, then frames 30 and 49
    /// (the captures off1 / off2). <paramref name="mode"/>: 0 = the placement carry off (1.1.0), 1 = this code (the carry at
    /// the first step), 2 = what 94d228e did (the carry at the build, emulated: the chains moved by the holder's matrix at once).</summary>
    static (Pose a, Pose b, Func<string, string> cls) R2SSequence(int mode)
    {
        bool sw = CharaBuilders.CarryChainsToPlacement;
        var f = Make(Seed, Pivot);
        try
        {
            CharaBuilders.CarryChainsToPlacement = mode != 0;
            var id = f.Ops.AddChara(femaleCard, null, null, -1, null, User).NewIds.FirstOrDefault();
            var c = f.Rt.FindChara(id);
            if (mode == 2)
            {
                var d = c.Holder.localToWorldMatrix;
                foreach (var b in c.Motion.GetAll<BustStage>()) foreach (var ch in b.All) ch.CarryRigid(d);
                var db = c.Motion.Get<DynamicBoneStage>();
                if (db != null) foreach (var ch in db.Chains) ch.CarryRigid(d);
                c.Motion.CarryChainsAtFirstStep(null, Matrix4x4.identity);   // the first-step carry already done
            }
            f.Ops.SetTransform(new[] { id }, Vector3.zero, Vector3.zero, null, User);
            Frames(f, 30);
            var a = Dump(c);
            Frames(f, 19);
            return (a, Dump(c), Classifier(c));
        }
        finally { CharaBuilders.CarryChainsToPlacement = sw; f.Dispose(); }
    }

    static void W3()
    {
        var off = R2SSequence(0);
        var now = R2SSequence(1);
        var r2 = R2SSequence(2);
        string Moves((Pose a, Pose b, Func<string, string> cls) x)
        {
            var d = Compare(x.a, x.b, x.cls);
            double bust = d.Rows.Where(r => r.cls == "bust").Select(r => r.world).DefaultIfEmpty(0).Max();
            double dyn = d.Rows.Where(r => r.cls.StartsWith("dyn.", StringComparison.Ordinal)).Select(r => r.world).DefaultIfEmpty(0).Max();
            return $"{d.Over} transforms move, bust / hips {bust * 100:F2} mm, dynamic bones {dyn * 100:F2} mm";
        }
        Info("W3 frame 30 → 49 (UiPanelsTest R2S's off1 → off2), placement carry off (1.1.0): " + Moves(off));
        Info("W3 the same, the carry at the build (94d228e, emulated): " + Moves(r2));
        Info("W3 the same, the carry at the first step (this code): " + Moves(now));
        var bitsOff = Bits(off.b); var bitsNow = Bits(now.b);
        Check("W3 an add at the pivot moved to the origin before its first frame: every bone bit-equal to 1.1.0's at frames 30 and 49 (the carry measures the root at the first step)",
            Bits(off.a).SequenceEqual(Bits(now.a)) && bitsOff.SequenceEqual(bitsNow));
    }

    // ---------------------------------------------------------------- M1 / M2 the character's own pause and time scale 0

    sealed class JObjectLite
    {
        public readonly Newtonsoft.Json.Linq.JObject J;
        public JObjectLite(string s) { J = Newtonsoft.Json.Linq.JObject.Parse(s); }
        public bool Ok => J.Value<bool?>("ok") == true;
        public override string ToString() => J.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>A character stopped by its own motion switch (the scene runs), then <paramref name="op"/>; vs the reference of
    /// PausedCase. <paramref name="settle"/> false: the settles switched off (the behaviour before round 2).</summary>
    static PoseDiff FrozenCase(string what, object freeze, Func<Fixture, string, string> op, bool settle, out Func<string, string> cls, out int settles)
    {
        Pose got, reference;
        bool sw = CharaRealization.SettleWhilePaused;
        var a = Make(Seed);
        try
        {
            CharaRealization.SettleWhilePaused = settle;
            var id = AddCard(a, femaleCard);
            Frames(a, SettleFrames);
            var json = Newtonsoft.Json.Linq.JObject.FromObject(freeze);
            json["key"] = id;
            var r = new JObjectLite(a.App.Call(json.ToString(Newtonsoft.Json.Formatting.None)));
            Check(what + ": " + json.ToString(Newtonsoft.Json.Formatting.None).Replace(id, "<id>") + " ok", r.Ok, r.Ok ? "" : r.ToString());
            Frames(a, 1);
            var c = a.Rt.FindChara(id);
            int s0 = c.Motion.Settles;
            float t0 = c.Motion.Time, an0 = c.Animation.NormalizedTime;
            op(a, id);
            Frames(a, 2);
            c = a.Rt.FindChara(id);
            settles = c.Motion.Settles - s0;
            cls = Classifier(c);
            got = Dump(c);
            if (settle) Check(what + ": the motion time and the animation frame stood still (the scene clock ran)", c.Motion.Time == t0 && c.Animation.NormalizedTime == an0, $"motion {t0:R} → {c.Motion.Time:R}, anime {an0:R} → {c.Animation.NormalizedTime:R}");
        }
        finally { CharaRealization.SettleWhilePaused = sw; a.Dispose(); }
        var b = Make(Seed);
        try
        {
            var id = AddCard(b, femaleCard);
            op(b, id);
            Frames(b, RefFrames);
            reference = Dump(b.Rt.FindChara(id));
        }
        finally { b.Dispose(); }
        return Compare(got, reference, cls);
    }

    static void M1()
    {
        Func<Fixture, string, string> bust = (fx, id) => { fx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", 0.95f) }, User); return id; };
        Func<Fixture, string, string> move = (fx, id) => { fx.Ops.SetTransform(new[] { id }, new Vector3(2f, 0f, 0f), new Vector3(0f, 90f, 0f), null, User); return id; };
        foreach (var (name, freeze) in new (string, object)[] { ("chara.motion {paused: true}", new { cmd = "chara.motion", paused = true }), ("chara.motion {timeScale: 0}", new { cmd = "chara.motion", timeScale = 0 }) })
            foreach (var (opName, op) in new[] { ("bust size 0.95", bust), ("a move and a 90 deg turn", move) })
            {
                var before = FrozenCase($"M1 {name}, {opName}, settles off", freeze, op, false, out var cls, out _);
                Info($"M1 {name}, {opName} without a settle (round 1): " + Describe(before, 6));
                var after = FrozenCase($"M1 {name}, {opName}", freeze, op, true, out cls, out int settles);
                var bad = after.Rows.Where(r => !IsLook(Leaf(r.name)) && !(r.cls.StartsWith("dyn.", StringComparison.Ordinal) && r.world <= 1e-3 && r.rot <= 0.5)).ToList();
                Check($"M1 {name}, {opName}: one settle, = the changed character running and settled (round 1 without it: {before.Over} transforms off, bust {BustWorld(before) * 100:F1} mm)",
                    settles == 1 && bad.Count == 0, $"settles {settles}; " + Describe(PoseDiffView(bad, after.Count), 6));
            }
    }

    // ================================================================ item 8: nipples under clothes

    static readonly string[] NipTips = { "cf_J_Mune_Nipacs01_L", "cf_J_Mune_Nipacs01_R" };

    /// <summary>Big nipples on BR-Chan (areola puff, thickness and stand slider 1, the stand state 1), animation speed 0; the scene
    /// paused afterwards.</summary>
    static string BigNipples(Fixture f, string card = null)
    {
        var id = AddCard(f, card ?? femaleCard);
        var r = f.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[7]", 1f), ("appearance.chara.body.shape[8]", 1f), ("appearance.chara.body.shape[32]", 1f) }, User);
        Check("N big nipples set (shape 7, 8, 32 = 1)", r.Ok, r.ToString());
        f.Ops.SetState(new[] { id }, "nipple", 1f, User);
        Frames(f, 2);
        // paused from here: the look and the expression stand still, so a comparison sees only what the switch does
        f.Rt.SetPaused(true);
        Frames(f, 1);
        return id;
    }

    /// <summary>Physics off (the bust chains would move the nipple bones' parents between the measurements).</summary>
    static void PhysicsOff(CharaRealization c)
    {
        var b = c.Motion.Get<BustStage>(); if (b != null) b.Enabled = false;
        var d = c.Motion.Get<DynamicBoneStage>(); if (d != null) d.Enabled = false;
    }

    static List<(string label, SkinnedMeshRenderer r)> ChestRenderers(CharaRealization c)
    {
        var l = new List<(string, SkinnedMeshRenderer)>();
        var ctx = c.Context;
        if (ctx.BodyRenderer) l.Add(("body", ctx.BodyRenderer));
        foreach (int k in new[] { 0, 2 })
            if (ctx.Clothes[k]) foreach (var s in ctx.Clothes[k].GetComponentsInChildren<SkinnedMeshRenderer>(true)) l.Add(("clothes" + k + "/" + s.name, s));
        return l;
    }

    sealed class Move { public int Near, Moved, Outward; public double Max, Mean, Inward; public bool Readable = true; }

    /// <summary>Vertex moves within 6 cm of a nipple tip between two CPU-skinned snapshots (mm: 1 unit = 10 cm): max, mean, the
    /// largest move toward the chest (along tip → breast root) and the count of vertices moving outward more than 0.05 mm.</summary>
    static Move Moves(CharaContext ctx, Vector3[] a, Vector3[] b)
    {
        var m = new Move();
        if (a == null || b == null || a.Length != b.Length) { m.Readable = false; return m; }
        var tips = NipTips.Select(n => ctx.BodyBones[n].position).ToArray();
        var roots = new[] { ctx.BodyBones["cf_J_Mune00_L"].position, ctx.BodyBones["cf_J_Mune00_R"].position };
        double sum = 0;
        for (int k = 0; k < a.Length; k++)
        {
            int side = (a[k] - tips[0]).sqrMagnitude <= (a[k] - tips[1]).sqrMagnitude ? 0 : 1;
            if ((a[k] - tips[side]).magnitude > 0.6f) continue;
            m.Near++;
            var d = b[k] - a[k];
            double mm = d.magnitude * 100;
            if (mm > 1e-4) m.Moved++;
            m.Max = Math.Max(m.Max, mm); sum += mm;
            double inward = Vector3.Dot(d, (roots[side] - tips[side]).normalized) * 100;
            m.Inward = Math.Max(m.Inward, inward);
            if (inward < -0.05) m.Outward++;
        }
        m.Mean = m.Near > 0 ? sum / m.Near : 0;
        return m;
    }

    static string Text(Move m) => !m.Readable ? "not readable" : $"{m.Near} vertices near the tips, {m.Moved} moved, max {m.Max:F2} mm, mean {m.Mean:F3} mm, max toward the chest {m.Inward:F2} mm, outward > 0.05 mm: {m.Outward}";

    static List<Vector3[]> Skin(List<(string label, SkinnedMeshRenderer r)> rs) => rs.Select(r => CpuSkin(r.r, out _)).ToList();

    static List<int> NipBits(CharaRealization c)
    {
        var l = new List<int>();
        foreach (var t in c.Context.BodyShape.NippleBones)
        {
            t.GetLocalPositionAndRotation(out var p, out var q); var s = t.localScale;
            foreach (var v in new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z }) l.Add(BitConverter.SingleToInt32Bits(v));
        }
        return l;
    }

    static bool SetCover(Fixture f, string id, bool through)
    {
        var r = f.Ops.SetState(new[] { id }, "nippleThroughClothes", through, User);
        Frames(f, 2);
        return r.Ok;
    }

    // ---------------------------------------------------------------- N1 the default

    static void N1()
    {
        var f = Make(Seed);
        try
        {
            var id = BigNipples(f);
            var c = f.Rt.FindChara(id);
            var st = (c.Node.State as CharaNodeState).State;
            Check("N1 the default is on (shown through, as HS2)", st.NippleThroughClothes && !c.StateRealizer.NipplesFlat && c.StateRealizer.ChestCovered && c.StateRealizer.NippleCoverChanges == 0,
                $"value {st.NippleThroughClothes}, flat {c.StateRealizer.NipplesFlat}, covered {c.StateRealizer.ChestCovered}, changes {c.StateRealizer.NippleCoverChanges}");
            PhysicsOff(c);
            Frames(f, 2);
            var b0 = Bits(Dump(c));
            var r = f.Ops.SetState(new[] { id }, "nippleThroughClothes", true, User);
            Frames(f, 2);
            Check("N1 setting it on again: no change (noop), every bone bit-equal, the engine never touched", r.Ok && r.Changed.Count == 0 && b0.SequenceEqual(Bits(Dump(c))) && c.StateRealizer.NippleCoverChanges == 0,
                $"changed {r.Changed.Count}, changes {c.StateRealizer.NippleCoverChanges}");
            // clothes states cycled with the switch on: the cover rule never applies
            f.Ops.SetClothes(new[] { id }, 0, 2, false, User); f.Ops.SetClothes(new[] { id }, 2, 2, false, User); Frames(f, 2);
            f.Ops.SetClothes(new[] { id }, 0, 0, false, User); f.Ops.SetClothes(new[] { id }, 2, 0, false, User); Frames(f, 2);
            Check("N1 clothes off and on again with the switch on: bit-equal, no cover change", b0.SequenceEqual(Bits(Dump(c))) && c.StateRealizer.NippleCoverChanges == 0);
        }
        finally { f.Dispose(); }
    }

    // ---------------------------------------------------------------- N2 off, covered

    static void N2()
    {
        var f = Make(Seed);
        List<int> flatBits;
        Pose flatPose;
        try
        {
            var id = BigNipples(f);
            var c = f.Rt.FindChara(id);
            PhysicsOff(c);
            Frames(f, 2);
            var rs = ChestRenderers(c);
            var on = Skin(rs);
            var onPose = Dump(c);
            Check("N2 switch off", SetCover(f, id, false));
            Check("N2 top and bra on, switch off: the nipples flat, the modifiers of the nipple bones left out", c.StateRealizer.NipplesFlat && (!c.Context.Bones || c.Context.Bones.SuppressedCount == c.Context.BodyShape.NippleBones.Count),
                $"flat {c.StateRealizer.NipplesFlat}, suppressed {(c.Context.Bones ? c.Context.Bones.SuppressedCount : -1)}, nipple bones {c.Context.BodyShape.NippleBones.Count} ({string.Join(", ", c.Context.BodyShape.NippleBones.Select(t => t.name))})");
            var off = Skin(rs);
            for (int i = 0; i < rs.Count; i++)
            {
                var m = Moves(c.Context, on[i], off[i]);
                Info($"N2 on → off, {rs[i].label}: {Text(m)}");
                if (rs[i].label.StartsWith("clothes2/o_bra_a") || rs[i].label == "body")
                    Check($"N2 {rs[i].label}: the vertices near the nipples move toward the flat surface (≥ 0.5 mm), none outward", m.Readable && m.Inward >= 0.5 && m.Outward == 0, Text(m));
            }
            var d = Compare(onPose, Dump(c), Classifier(c));
            Info("N2 bones on → off: " + Describe(d, 12));
            flatBits = NipBits(c);
            flatPose = Dump(c);
            bool okOn = SetCover(f, id, true);
            var backPose = Dump(c);
            var cyc = Compare(onPose, backPose, Classifier(c), 1e-6, 1e-4, 1e-6);
            Check("N2 switch on again: every bone as before (the shapes evaluated again: within 1e-6 and 1e-4 deg)", okOn && cyc.Over == 0 && !c.StateRealizer.NipplesFlat,
                $"{Bits(onPose).Zip(Bits(backPose), (x, y) => x != y ? 1 : 0).Sum()} values differ in bits; " + Describe(cyc, 6));
            var back = Skin(rs);
            double vmax = 0;
            for (int i = 0; i < rs.Count; i++)
                if (on[i] != null && back[i] != null) for (int k = 0; k < on[i].Length; k++) vmax = Math.Max(vmax, (on[i][k] - back[i][k]).magnitude);
            Check("N2 and every skinned vertex of the body, top and bra as before (≤ 1e-5 mm)", vmax * 100 <= 1e-5, $"max {vmax * 100:E2} mm");
        }
        finally { f.Dispose(); }
        // the flat nipples equal a card whose nipple sliders and stand are 0
        var g = Make(Seed);
        try
        {
            var id = AddCard(g, femaleCard);
            g.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.shape[7]", 0f), ("appearance.chara.body.shape[8]", 0f), ("appearance.chara.body.shape[32]", 0f) }, User);
            Frames(g, 2);
            var c = g.Rt.FindChara(id);
            PhysicsOff(c);
            Frames(g, 2);
            Check("N2 the flat nipple bones = the same card with the nipple sliders (7, 8, 32) and the stand at 0 (bit-equal)", NipBits(c).SequenceEqual(flatBits));
        }
        finally { g.Dispose(); }
    }

    // ---------------------------------------------------------------- N3 off, uncovered

    static void N3()
    {
        foreach (var (what, top, bra, sil) in new[] { ("top and bra off", 2, 2, false), ("top and bra half", 1, 1, false), ("top off, bra half", 2, 1, false), ("silhouette on", 0, 0, true) })
        {
            var f = Make(Seed);
            try
            {
                var id = BigNipples(f);
                var c = f.Rt.FindChara(id);
                PhysicsOff(c);
                f.Ops.SetClothes(new[] { id }, 0, top, false, User);
                f.Ops.SetClothes(new[] { id }, 2, bra, false, User);
                if (sil) f.Ops.SetState(new[] { id }, "silhouette.enabled", true, User);
                Frames(f, 2);
                var rs = ChestRenderers(c);
                var p0 = Dump(c);
                var b0 = Bits(p0);
                var v0 = Skin(rs);
                SetCover(f, id, false);
                bool flat = c.StateRealizer.NipplesFlat;
                var v1 = Skin(rs);
                bool vs = true;
                for (int i = 0; i < rs.Count; i++) vs &= v0[i] == null || v0[i].SequenceEqual(v1[i]);
                Check($"N3 {what}, switch off: the body's own nipples (not flat; every bone and skinned vertex bit-equal to the switch on)", !flat && b0.SequenceEqual(Bits(Dump(c))) && vs,
                    $"flat {flat}, realized {string.Join(",", c.StateRealizer.Realized)}");
                // putting the top on again with the switch off flattens; off again returns bit for bit
                f.Ops.SetClothes(new[] { id }, 0, 0, false, User);
                if (sil) f.Ops.SetState(new[] { id }, "silhouette.enabled", false, User);
                Frames(f, 3);
                bool flatNow = c.StateRealizer.NipplesFlat;
                f.Ops.SetClothes(new[] { id }, 0, top, false, User);
                if (sil) f.Ops.SetState(new[] { id }, "silhouette.enabled", true, User);
                Frames(f, 3);
                var cyc = Compare(p0, Dump(c), null, 1e-6, 1e-4, 1e-6);
                Check($"N3 {what}: the top on again flattens, back to «{what}» returns every bone (within 1e-6 and 1e-4 deg)", flatNow && !c.StateRealizer.NipplesFlat && cyc.Over == 0,
                    $"flat with the top {flatNow}; " + Describe(cyc, 4));
            }
            finally { f.Dispose(); }
        }
    }

    // ---------------------------------------------------------------- N4 bone modifiers on the nipple bones

    static void N4()
    {
        var f = Make(Seed);
        try
        {
            var id = BigNipples(f);
            var c = f.Rt.FindChara(id);
            PhysicsOff(c);
            var ctx = c.Context;
            List<int> noMod;
            SetCover(f, id, false);
            noMod = NipBits(c);
            SetCover(f, id, true);
            var mods = ctx.Bones ? ctx.Bones : c.Root.AddComponent<HS2Next.Chara.Ext.BoneModifierApplier>();
            if (!ctx.Bones) ctx.Bones = mods;
            // a modifier as 4 of the cards carry on the areola and 3 on the nipple (scale)
            mods.Add(new HS2Next.Chara.Ext.BoneModifierApplier.Modifier { Target = ctx.BodyBones["cf_J_Mune_Nip02_s_L"], Bone = "cf_J_Mune_Nip02_s_L", Source = "test", Location = 1, Scale = new Vector3(1.6f, 1.6f, 1.6f) });
            mods.Add(new HS2Next.Chara.Ext.BoneModifierApplier.Modifier { Target = ctx.BodyBones["cf_J_Mune04_s_L"], Bone = "cf_J_Mune04_s_L", Source = "test", Location = 1, Scale = new Vector3(1.4f, 1.4f, 1.4f) });
            mods.Apply();
            // as a card's modifiers after the build's final stage: the animation's shape captures take them
            if (c.Animation) c.Animation.RefreshBodyShape(ctx);
            Frames(f, 2);
            Check("N4 the modifiers show (switch on)", (ctx.BodyBones["cf_J_Mune_Nip02_s_L"].localScale - ctx.BodyBones["cf_J_Mune_Nip02_s_R"].localScale).magnitude > 1e-3,
                ctx.BodyBones["cf_J_Mune_Nip02_s_L"].localScale.ToString("F4") + " vs R " + ctx.BodyBones["cf_J_Mune_Nip02_s_R"].localScale.ToString("F4"));
            var rs = ChestRenderers(c);
            var on = Skin(rs);
            var onPose = Dump(c);
            SetCover(f, id, false);
            Check("N4 switch off with bone modifiers on the nipple and areola: the modifiers left out, the nipple bones = the flat ones without modifiers (bit-equal)", NipBits(c).SequenceEqual(noMod));
            var off = Skin(rs);
            for (int i = 0; i < rs.Count; i++)
                if (rs[i].label.StartsWith("clothes2/o_bra_a")) Info("N4 on (modifiers) → off, " + rs[i].label + ": " + Text(Moves(ctx, on[i], off[i])));
            SetCover(f, id, true);
            var cyc = Compare(onPose, Dump(c), null, 1e-6, 1e-4, 1e-6);
            Check("N4 switch on again: the modifiers back, every bone as before (within 1e-6 and 1e-4 deg)", cyc.Over == 0, Describe(cyc, 4));
            mods.Remove("test");
        }
        finally { f.Dispose(); }
    }

    // ---------------------------------------------------------------- N5 operations: several characters, history, bridge, paused

    static void N5()
    {
        var f = Make(Seed);
        try
        {
            var a = AddCard(f, femaleCard);
            var b = AddCard(f, femaleCard);
            f.Ops.SetTransform(new[] { b }, new Vector3(5, 0, 0), null, null, User);
            Frames(f, 2);
            int undo0 = f.App.Session.Commands.UndoList.Count;
            var r = f.Ops.SetState(new[] { a, b }, "nippleThroughClothes", false, User);
            Frames(f, 2);
            var ca = f.Rt.FindChara(a); var cb = f.Rt.FindChara(b);
            Check("N5 two characters at once: one history entry «" + StudioOps.Labels.CharacterState + "», both flat", r.Ok && f.App.Session.Commands.UndoList.Count == undo0 + 1 &&
                f.App.Session.Commands.UndoList[0].Label == StudioOps.Labels.CharacterState && ca.StateRealizer.NipplesFlat && cb.StateRealizer.NipplesFlat,
                $"{r}, entries +{f.App.Session.Commands.UndoList.Count - undo0}");
            f.App.Session.Commands.Undo(); Frames(f, 2);
            Check("N5 undo: both shown through again", !ca.StateRealizer.NipplesFlat && !cb.StateRealizer.NipplesFlat);
            f.App.Session.Commands.Redo(); Frames(f, 2);
            Check("N5 redo: both flat", ca.StateRealizer.NipplesFlat && cb.StateRealizer.NipplesFlat);
            // bridge: chara.state set / the generic read
            var set = Newtonsoft.Json.Linq.JObject.Parse(f.App.Call(Newtonsoft.Json.JsonConvert.SerializeObject(new { cmd = "chara.state", key = a, set = new { nippleThroughClothes = true } })));
            Frames(f, 2);
            Check("N5 bridge chara.state {set: {nippleThroughClothes: true}}: changed, shown through", set.Value<bool?>("ok") == true && set["changed"]?.ToString().Contains("nippleThroughClothes") == true && !ca.StateRealizer.NipplesFlat, set.ToString(Newtonsoft.Json.Formatting.None));
            Check("N5 the field reads through the model path chara.state.nippleThroughClothes", FieldAccessor.Get(f.App.Session.Model, b, "chara.state.nippleThroughClothes") is bool v && !v);
            // while paused: the toggle settles the physics (item 5)
            f.Rt.SetPaused(true);
            Frames(f, 1);
            int s0 = cb.Motion.Settles;
            f.Ops.SetState(new[] { b }, "nippleThroughClothes", true, User);
            Frames(f, 2);
            Check("N5 paused: the toggle settles the character once", cb.Motion.Settles == s0 + 1 && !cb.StateRealizer.NipplesFlat, $"settles {s0} → {cb.Motion.Settles}");
            // the schema row
            var field = HS2Next.UI.Core.InspectorSchemas.ForKind("chara").SelectMany(sec => sec.Fields).FirstOrDefault(x => x.Path == "chara.state.nippleThroughClothes");
            Check("N5 the inspector's state tab has the row (bool, female only, beside the nipple stand)", field != null && field.Kind == HS2Next.UI.Core.FieldKind.Bool && field.Group == "state.other" && field.Gate == HS2Next.UI.Core.SchemaGates.CharaFemale,
                field == null ? "missing" : field.LabelKey);
        }
        finally { f.Dispose(); }
    }

    // ---------------------------------------------------------------- N6 the file

    static byte[] Entry(string path, string name)
    {
        var bytes = File.ReadAllBytes(path);
        using (var ms = new MemoryStream(bytes))
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read))
        {
            var e = zip.GetEntry(name);
            if (e == null) return null;
            using (var s = e.Open()) using (var o = new MemoryStream()) { s.CopyTo(o); return o.ToArray(); }
        }
    }

    static void N6()
    {
        var f = Make(Seed);
        try
        {
            var a = AddCard(f, femaleCard);
            var b = AddCard(f, femaleCard);
            Frames(f, 2);
            var opts = new SaveOptions { Clock = () => new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder };
            string on = Path.Combine(SandboxDir, "n6_on.hs2nscene"), off = Path.Combine(SandboxDir, "n6_off.hs2nscene");
            Check("N6 saved with the default", f.Ops.SaveScene(on, opts).Ok);
            string sceneOn = Encoding.UTF8.GetString(Entry(on, "scene.json"));
            string manOn = Encoding.UTF8.GetString(Entry(on, "manifest.json"));
            Check("N6 the default writes no member and the base revision (a 1.1.0 file byte for byte)", !sceneOn.Contains("nippleThroughClothes") && manOn.Contains("\"formatVersion\": 1"), manOn.Substring(0, Math.Min(160, manOn.Length)));
            f.Ops.SetState(new[] { b }, "nippleThroughClothes", false, User);
            Frames(f, 2);
            Check("N6 saved with one character off", f.Ops.SaveScene(off, opts).Ok);
            string sceneOff = Encoding.UTF8.GetString(Entry(off, "scene.json"));
            string manOff = Encoding.UTF8.GetString(Entry(off, "manifest.json"));
            int members = System.Text.RegularExpressions.Regex.Matches(sceneOff, "\"nippleThroughClothes\": false").Count;
            Check("N6 off writes \"nippleThroughClothes\": false once (that character) and revision 3", members == 1 && manOff.Contains("\"formatVersion\": 3"), $"members {members}");
            // this reader
            var ld = f.Ops.LoadScene(off);
            Frames(f, 2);
            var cb = f.Rt.FindChara(b); var ca = f.Rt.FindChara(a);
            Check("N6 loaded back: that character flat, the other shown through", ld.Ok && cb != null && cb.StateRealizer.NipplesFlat && ca != null && !ca.StateRealizer.NipplesFlat);
            // a reader of revision 2 (as 1.1.0 reads a newer file: NewerFormat, nothing refused, the documents bound)
            var lo = new LoadOptions { Migrations = new CoreMigrationRegistry(2, CoreMigrationRegistry.Empty(1)) };
            var l2 = SceneContainer.Load(off, lo);
            Check("N6 a revision-2 reader opens it (NewerFormat, manifest.newerFormat, no error)", l2.Ok && l2.NewerFormat && l2.Findings.Has("manifest.newerFormat"), l2.ToString());
            var l3 = SceneContainer.Load(on, lo);
            Check("N6 and opens the default file as before (no finding about the version)", l3.Ok && !l3.NewerFormat && !l3.Findings.Has("manifest.newerFormat"), l3.ToString());
        }
        finally { f.Dispose(); }
    }


    // ---------------------------------------------------------------- N7 the nipple stand state (found while testing item 8)

    static void N7()
    {
        foreach (bool unit in new[] { false, true })
        {
            bool sw = CharaStateRealizer.NippleStandThroughUnit;
            var f = Make(Seed);
            try
            {
                CharaStateRealizer.NippleStandThroughUnit = unit;
                var id = AddCard(f, femaleCard);
                Frames(f, 2);
                var c = f.Rt.FindChara(id);
                var t = c.Context.BodyBones["cf_J_Mune_Nip02_s_L"];
                var s0 = t.localScale;
                f.Ops.SetState(new[] { id }, "nipple", 1f, User);
                Frames(f, 3);
                var s1 = t.localScale;
                string what = unit ? "through the body shape unit (the fix)" : "a bare full update (1.1.0)";
                bool moved = (s1 - s0).magnitude > 1e-3;
                if (unit) Check($"N7 the nipple stand state 1 ({what}): the stand bone grows and keeps it after 3 frames with the animation", moved, $"scale {s0.ToString("F4")} → {s1.ToString("F4")}");
                else Info($"N7 the nipple stand state 1 ({what}): stand bone scale {s0.ToString("F4")} → {s1.ToString("F4")} after 3 frames ({(moved ? "kept" : "undone by the shape-bone guard")})");
            }
            finally { CharaStateRealizer.NippleStandThroughUnit = sw; f.Dispose(); }
        }
    }

    static string AddF(Fixture f, float? speed = null)
    {
        var id = f.Ops.AddChara(femaleCard, null, null, -1, null, User).NewIds.FirstOrDefault();
        if (speed.HasValue) f.Ops.SetAnimeSpeed(new[] { id }, speed.Value, null, User);
        return id;
    }

    // ================================================================ P8 probe: the nipple shape mask

    static Vector3[] CpuSkin(SkinnedMeshRenderer r, out int lost)
    {
        lost = 0;
        var m = r ? r.sharedMesh : null;
        if (!m || !m.isReadable) return null;
        var v = m.vertices; var bw = m.boneWeights; var bp = m.bindposes; var b = r.bones;
        if (bw.Length != v.Length || bp.Length != b.Length) return null;
        var mats = new Matrix4x4[b.Length];
        var have = new bool[b.Length];
        for (int i = 0; i < b.Length; i++) if (b[i]) { mats[i] = b[i].localToWorldMatrix * bp[i]; have[i] = true; }
        var o = new Vector3[v.Length];
        int missing = 0;
        for (int k = 0; k < v.Length; k++)
        {
            var w = bw[k];
            Vector3 p = Vector3.zero;
            void Acc(int i, float x) { if (x <= 0) return; if (i < 0 || i >= have.Length || !have[i]) { missing++; return; } p += x * mats[i].MultiplyPoint3x4(v[k]); }
            Acc(w.boneIndex0, w.weight0); Acc(w.boneIndex1, w.weight1); Acc(w.boneIndex2, w.weight2); Acc(w.boneIndex3, w.weight3);
            o[k] = p;
        }
        lost = missing;
        return o;
    }

    static void P8()
    {
        var f = Make(Seed, null);
        try
        {
            Frames(f, 3);
            var id = AddF(f, 0f);
            var c = f.Rt.FindChara(id);
            var cls = Classifier(c);
            var ctx = c.Context;
            var shape = ctx.BodyShape;
            var st = (c.Node.State as CharaNodeState)?.State;
            Info("clothes states " + string.Join(",", st.Clothes) + "; realized " + string.Join(",", c.StateRealizer.Realized));
            for (int k = 0; k < 8; k++)
            {
                var o = ctx.Clothes[k];
                if (!o) { Info($"clothes {k}: none"); continue; }
                var smrs = o.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Info($"clothes {k}: {o.name} active {o.activeInHierarchy}, row {ctx.ClothesEntries[k]?["Name"]}, SMRs " + string.Join("; ", smrs.Select(s => $"{s.name} v{(s.sharedMesh ? s.sharedMesh.vertexCount : 0)} readable {(s.sharedMesh && s.sharedMesh.isReadable)} active {s.gameObject.activeInHierarchy} nipBones {s.bones.Count(b => b && b.name.Contains("Nip"))}")));
            }
            // the engine's links of the nipple ids (reflection: diagnostics only)
            var fl = typeof(HS2Next.Chara.ShapeEngine).GetField("sliders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = fl?.GetValue(shape) as System.Collections.IDictionary;
            foreach (int sid in new[] { 2, 3, 4, 5, 6, 7, 8, 32 })
            {
                if (dict == null || !dict.Contains(sid)) { Info($"slider {sid}: no links"); continue; }
                var links = (System.Collections.IEnumerable)dict[sid];
                var names = new List<string>();
                foreach (var l in links) names.Add((string)l.GetType().GetField("src").GetValue(l));
                Info($"slider {sid}: sources {string.Join(", ", names)}");
            }
            Info("bust mask bones L: " + string.Join(", ", shape.BustMaskBones(0).Select(t => t.name)));
            var vals = ctx.Chara?.Body?.Shape;
            if (vals != null) Info($"card values: 7 {vals[7]:R}, 8 {vals[8]:R}, 32 {vals[32]:R}");
            Frames(f, 300);
            // physics off for the measurement (the bust chain would move the nipple bones' parents)
            var bustStage = c.Motion.Get<BustStage>();
            bustStage.Enabled = false;
            Frames(f, 2);
            var nipNames = new[] { "cf_J_Mune_Nip01_s_L", "cf_J_Mune_Nip02_s_L", "cf_J_Mune_Nipacs01_L", "cf_J_Mune_Nip01_s_R", "cf_J_Mune_Nip02_s_R", "cf_J_Mune_Nipacs01_R" };
            var nip = nipNames.Select(n => ctx.BodyBones.TryGetValue(n, out var t) ? t : null).ToArray();
            Vector3 tipL = nip[2] ? nip[2].position : Vector3.zero, tipR = nip[5] ? nip[5].position : Vector3.zero;
            Info($"nipple tips world L {tipL.ToString("F4")} R {tipR.ToString("F4")}; head {ctx.BodyBones["cf_J_Head"].position.ToString("F3")}");
            var renderers = new List<(string label, SkinnedMeshRenderer r)>();
            if (ctx.BodyRenderer) renderers.Add(("body", ctx.BodyRenderer));
            foreach (int k in new[] { 0, 2 })
                if (ctx.Clothes[k]) foreach (var s in ctx.Clothes[k].GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderers.Add(("clothes" + k + "/" + s.name, s));
            var basePose = Dump(c);
            var baseV = renderers.Select(r => CpuSkin(r.r, out _)).ToList();
            void Measure(string what)
            {
                Frames(f, 1);
                Info(what + ": bones " + Describe(Compare(basePose, Dump(c), cls), 10));
                for (int i = 0; i < renderers.Count; i++)
                {
                    var now = CpuSkin(renderers[i].r, out int lost);
                    if (now == null || baseV[i] == null) { Info($"  {renderers[i].label}: not readable"); continue; }
                    int near = 0, moved = 0; double max = 0, sum = 0, inward = 0;
                    for (int k = 0; k < now.Length; k++)
                    {
                        var b0 = baseV[i][k];
                        float dl = (b0 - tipL).magnitude, dr = (b0 - tipR).magnitude;
                        if (Math.Min(dl, dr) > 0.6f) continue;
                        near++;
                        var d = now[k] - b0;
                        double m = d.magnitude;
                        if (m > 1e-6) moved++;
                        max = Math.Max(max, m); sum += m;
                        // toward the chest: along the vector from the tip to its bust root (Mune00)
                        var root = ctx.BodyBones[dl < dr ? "cf_J_Mune00_L" : "cf_J_Mune00_R"].position;
                        var dir = (root - (dl < dr ? tipL : tipR)).normalized;
                        inward = Math.Max(inward, Vector3.Dot(d, dir));
                    }
                    Info($"  {renderers[i].label}: {near} vertices within 6 cm of a nipple tip, {moved} moved, max {max * 100:F2} mm, mean {(near > 0 ? sum / near * 100 : 0):F3} mm, max inward {inward * 100:F2} mm (lost weights {lost})");
                }
            }
            Measure("no change (control)");
            c.Animation.ApplyShapeMasks(0x60, 0x60);
            Measure("HS2 nipple mask bits 5, 6 (ids 7, 8) both sides");
            c.Animation.ApplyShapeMasks(0xE0, 0xE0);
            Measure("bits 5, 6, 7 (ids 7, 8, 32)");
            c.Animation.ApplyShapeMasks(0, 0);
            Measure("masks open again (vs base)");
            // all nipple rows at the neutral values (not HS2's mask: the nipple rows too)
            shape.SetValue(7, 0.5f); shape.SetValue(8, 0.5f);
            shape.Apply();
            if (ctx.Bones) ctx.Bones.Apply();
            Measure("ids 7, 8 at 0.5 through a full update (nipple rows too)");
            shape.SetValue(32, 0.5f);
            shape.Apply();
            if (ctx.Bones) ctx.Bones.Apply();
            Measure("ids 7, 8, 32 at 0.5 through a full update");
        }
        finally { f.Dispose(); }
        Check("probe P8 ran", true);
    }
}
