// CharaStateTest (Phase 3.5 package B): the character state realization, headless, edit
// mode, on the SFW visual targets of Tools/targets.json. Numbers only: object activity flags, material property values and
// renderer names are read; no capture is taken, saved or viewed (content rule for half / off clothes states).
//   S1 state types and the pair reconciliation (ClothesStateSet.FromRows) on the outfit classes the spec names (three-state,
//      two-state, one-piece top, one-piece inner, no top) and HS2's cycling and couplings (Apply) for every kind x request x
//      direction x start vector against an independent oracle written text
//   S2 the state types of BR-Chan's and HERO107's outfits (CharaRealization.ClothesStates)
//   S3 object activity per state (all 8 kinds x 3 requests through StudioOps.SetClothes) = the table of the realized
//      states; U-M6 (clothes-state change cost per character)
//   S4 _alpha_a / _alpha_b / _alpha_d per top / bottom state
//   S5 wet / gloss / liquid / nipple / son property and transform values
//   S6 silhouette (v0.3 trial 2): HS2's simple body built on first use, skinned to the body bones, drawn with
//      HS2Next/Custom/Silhouette in silhouette.color; body mesh, head, hair, clothes, accessories hidden, body tongue renderers
//      off, no material of the character swapped; son and colour changes while on; undo restores every object, switch and
//      material; save -> load draws it again, byte identity; both sexes
//   S7 an unsupported stored value: realized by the forward cycling, finding chara.clothesStateAdjusted, save -> load -> save
//      byte-identical with the value kept
//   S8 the inferred mask reference renderers (o_bra_a, o_shorts_a, o_panst_a) in the prefabs of 10 rows of each inner list
//      (renderer names only) and BR-Chan's worn parts (chara.maskRendererMissing)
//   S10 (Phase 4.1) the gloss state on the face draw: base + 0.2 x gloss after state changes, draw.face edits, a head swap and a
//      rebuild, both directions
// Run: -executeMethod CharaStateTest.Run [-only S1+S3]; report Spike/out/chara_state/chara_state_report.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AIChara;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class CharaStateTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "chara_state");
    static StringBuilder rep;
    static int passes, fails;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static readonly List<string> measurements = new List<string>();
    static string[] only;
    static RuntimeServices services;
    static string femaleCard, maleCard;

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Measure(string s) { measurements.Add($"[{current}] {s}"); Info("MEASURE " + s); }

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
        rep = new StringBuilder(); passes = fails = 0; perTest.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            Guard("S1", "state types, reconciliation, cycling and couplings (Unity-free rules)", S1);
            services = SceneRuntimeTest.NewServices(head);
            var t = TestTargets.Load();
            femaleCard = TestTargets.Abs(t.visual.female);
            maleCard = TestTargets.Abs(t.visual.male);
            rep.Insert(0, $"Character state test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})\n" + head);
            Guard("S2", "state types of the target outfits", S2);
            Guard("S3", "object activity per state (U-M6)", S3);
            Guard("S4", "mask values per state", S4);
            Guard("S5", "wet, gloss, liquid, nipple, son", S5);
            Guard("S6", "silhouette: the simple body, what it hides, restore, save / load (both sexes)", S6);
            Guard("S7", "unsupported stored value: realized, finding, byte identity", S7);
            Guard("S8", "mask reference renderer names", S8);
            Guard("S9", "mask targets: the shader of the reference renderers declares the mask inputs", S9);
            Guard("S10", "gloss state on the face draw (Phase 4.1)", S10);
            Guard("S11", "the accessory visibility fold (Phase 4.5, XT7)", S11);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "chara_state_report.txt"), rep.ToString(), new UTF8Encoding(false));
        services?.Catalog?.UnloadAll();
        Debug.Log($"CHARA STATE TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    sealed class Env : IDisposable
    {
        public readonly SceneSession S;
        public readonly SceneRuntime Rt;
        public readonly StudioOps Ops;

        public Env(ulong seed = 0xc5a7e5c5a7e5UL)
        {
            S = SceneRuntimeTest.NewSession(services, seed);
            Rt = new SceneRuntime(S, services, null, false);
            Ops = StudioOps.For(Rt);
        }

        public string Add(string card)
        {
            var r = Ops.AddChara(card);
            if (!r.Ok || r.NewIds.Count == 0) throw new Exception("add " + Path.GetFileName(card) + ": " + r);
            Rt.ApplyNow();
            return r.NewIds[0];
        }

        public CharaRealization C(string id) => Rt.FindChara(id);
        public CharaNodeState St(string id) => (CharaNodeState)S.Model.Find(id).State;
        public void Dispose() { Rt.Dispose(); }
    }

    static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };

    // ================================================================ S1 (Unity-free)

    /// <summary>The oracle: the rule "Setting a state" written again from its text (not from ClothesStateSet).</summary>
    static int[] Oracle(int[] types, bool notBot, bool notInnerB, int[] cur, int kind, int state, bool next)
    {
        var s = (int[])cur.Clone();
        bool Has(int k) => types[k] == 0 || types[k] == 1;
        bool Sup(int k, int v) => types[k] == 0 || types[k] == 1 && v != 1;
        int start = s[kind];
        int req = state;
        while (true)
        {
            if (!Has(kind) || Sup(kind, req)) { s[kind] = req; break; }
            req = next ? (req + 1) % 3 : (req + 2) % 3;
            if (req == start) break;
        }
        int a = -1, b = -1;
        if (kind == 0 || kind == 1) { if (notBot) { a = kind; b = 1 - kind; } }
        if (kind == 2 || kind == 3) { if (notInnerB) { a = kind; b = 5 - kind; } }
        if (a >= 0)
        {
            if (s[a] == 2) s[b] = 2;
            else if (s[b] == 2) s[b] = req;
        }
        return s;
    }

    static int Row(string t) => t == null ? int.MinValue : int.Parse(t);

    static void S1()
    {
        // reconciliation, hand-derived
        ClothesStateSet F(string top, string bot, string bra, string sh, bool notBot = false, bool notInnerT = false, bool notInnerB = false,
            bool botDefOfTop = false, bool topDefOfBot = false, bool botDefOfBra = false, bool botDefOfShorts = false, string gl = "1", string pa = "0", string so = "1", string shoes = "1") =>
            ClothesStateSet.FromRows(new[] { Row(top), Row(bot), Row(bra), Row(sh), Row(gl), Row(pa), Row(so), Row(shoes) }, notBot, notInnerT, notInnerB, botDefOfTop, topDefOfBot, botDefOfBra, botDefOfShorts);
        var three = F("0", "0", "0", "0");
        Check("three-state outfit: 0..3 three states, gloves / socks / shoes two, pantyhose three", three.Types.SequenceEqual(new[] { 0, 0, 0, 0, 1, 0, 1, 1 }), three.ToString());
        var two = F("1", "1", "1", "1", pa: "1");
        Check("two-state outfit: every kind on / off", two.Types.SequenceEqual(new[] { 1, 1, 1, 1, 1, 1, 1, 1 }) && two.Supported(0).SequenceEqual(new[] { 0, 2 }), two.ToString());
        var noTop = F(null, "0", "0", "0");
        Check("no top row: the top is not a state kind", noTop.Types[0] == -1 && !noTop.IsStateKind(0) && noTop.Supported(0).Length == 0, noTop.ToString());
        var none = F("2", "3", "0", "0");
        Check("StateType 2 / 3 = no states", none.Types[0] == -1 && none.Types[1] == -1, none.ToString());
        var forceBot = F("0", "1", "0", "0", botDefOfTop: true);
        Check("top three-state with objBotDef forces the bottom three-state", forceBot.Types[1] == 0, forceBot.ToString());
        var twoBot = F("1", "2", "0", "0", botDefOfTop: true);
        Check("top two-state with objBotDef turns a bottom of type 2 into two-state", twoBot.Types[1] == 1, twoBot.ToString());
        var forceTop = F("1", "0", "0", "0", topDefOfBot: true);
        Check("bottom three-state with objTopDef forces the top three-state", forceTop.Types[0] == 0, forceTop.ToString());
        var noForce = F("0", "1", "0", "0");
        Check("without the def object no reconciliation", noForce.Types[1] == 1, noForce.ToString());
        var onePiece = F("0", null, "0", "0", notBot: true);
        Check("one-piece top: the bottom has no row (no states), notBot set", onePiece.NotBot && onePiece.Types[1] == -1, onePiece.ToString());
        var noBra = F("0", "0", "0", "0", notInnerT: true, notInnerB: true);
        Check("NotBra top with a one-piece inner: bra and shorts without states", noBra.Types[2] == -1 && noBra.Types[3] == -1, noBra.ToString());
        var noBra2 = F("0", "0", "0", "1", notInnerT: true);
        Check("NotBra top: bra without states, shorts read", noBra2.Types[2] == -1 && noBra2.Types[3] == 1, noBra2.ToString());
        var innerForce = F("0", "0", "0", "1", botDefOfBra: true);
        Check("bra three-state with objBotDef forces the shorts three-state", innerForce.Types[3] == 0, innerForce.ToString());
        var innerTwo = F("0", "0", "2", "0", botDefOfShorts: true);
        Check("bra of type 2 is not reconciled from the shorts", innerTwo.Types[2] == -1, innerTwo.ToString());

        // cycling and couplings against the oracle
        var sets = new (string name, ClothesStateSet set)[]
        {
            ("three", three), ("two", two), ("no top", noTop), ("one-piece top", F("0", "0", "0", "0", notBot: true)),
            ("one-piece top two-state", F("1", "1", "0", "0", notBot: true)), ("one-piece inner", F("0", "0", "0", "0", notInnerB: true)),
            ("one-piece inner two-state", F("0", "0", "1", "1", notInnerB: true)), ("mixed", F("0", "1", null, "0", gl: "0", pa: "1", so: null, shoes: "2")),
        };
        var starts = new[] { new int[8], Enumerable.Repeat(2, 8).ToArray(), new[] { 1, 2, 0, 1, 2, 1, 0, 2 }, new[] { 2, 0, 1, 2, 0, 2, 1, 0 } };
        foreach (var (name, set) in sets)
        {
            int n = 0, bad = 0;
            string first = null;
            foreach (var st in starts)
                for (int k = 0; k < 8; k++)
                    for (int s = 0; s < 3; s++)
                        foreach (bool next in new[] { true, false })
                        {
                            n++;
                            var a = set.Apply(st, k, s, next);
                            var e = Oracle(set.Types, set.NotBot, set.NotInnerB, st, k, s, next);
                            if (!a.SequenceEqual(e)) { bad++; first ??= $"start {string.Join("", st)} kind {k} req {s} next {next}: {string.Join("", a)} vs {string.Join("", e)}"; }
                        }
            Check($"Apply = the oracle on \"{name}\" ({n} cases: 4 starts x 8 kinds x 3 requests x 2 directions)", bad == 0, bad > 0 ? bad + " differ; first " + first : set.ToString());
        }
        // a few cases worked by hand
        var op = sets[3].set;
        Check("one-piece top off forces the bottom off", op.Apply(new int[8], 0, 2).Take(2).SequenceEqual(new[] { 2, 2 }));
        Check("one-piece top leaving off gives the off bottom the same state", op.Apply(new[] { 2, 2, 0, 0, 0, 0, 0, 0 }, 0, 1).Take(2).SequenceEqual(new[] { 1, 1 }));
        Check("two-state kind: half requested -> off (next) / on (previous)", two.Apply(new int[8], 0, 1, true)[0] == 2 && two.Apply(new[] { 2, 0, 0, 0, 0, 0, 0, 0 }, 0, 1, false)[0] == 0);
        Check("no-state kind stores the value as given", noTop.Apply(new int[8], 0, 1)[0] == 1);
        Check("All off on the three-state outfit", three.ApplyAll(new int[8], 2).All(x => x == 2));
        Check("Effective: unsupported half on a two-state kind -> off; supported kept", two.Effective(0, 1) == 2 && three.Effective(0, 1) == 1 && noTop.Effective(0, 1) == 1);
        Check("KindOf: names and indices", ClothesStateSet.KindOf("bra") == 2 && ClothesStateSet.KindOf("SHOES") == 7 && ClothesStateSet.KindOf("5") == 5 && ClothesStateSet.KindOf("hat") == -1 && ClothesStateSet.KindOf("8") == -1);
    }

    // ================================================================ S2

    static void S2()
    {
        using (var env = new Env())
        {
            foreach (var (label, card) in new[] { ("BR-Chan", femaleCard), ("HERO107", maleCard) })
            {
                var id = env.Add(card);
                var r = env.C(id);
                var set = r?.ClothesStates;
                Check(label + " built, clothes state types known", r != null && r.State == RealizationState.Built && set != null && set.Known, set?.ToString());
                Info(label + ": " + set + " realized " + string.Join(",", r?.StateRealizer?.Realized ?? new int[0]));
                Check(label + ": ClothesStates = Compute(context)", set != null && set.Types.SequenceEqual(CharaStateRealizer.Compute(r.Context).Types));
                var ops = env.Ops.ClothesStatesOf(id);
                Check(label + ": StudioOps.ClothesStatesOf = CharaRealization.ClothesStates", ops.Types.SequenceEqual(set.Types));
            }
        }
    }

    // ================================================================ S3 object activity (+ U-M6)

    /// <summary>Mismatches between the parts' activity and the table for the realized states <paramref name="e"/>.</summary>
    static List<string> ActivityErrors(CharaRealization r, int[] e, bool sil = false)
    {
        var errs = new List<string>();
        var st = r.ClothesStates;
        var objs = r.Context.Clothes;
        GameObject O(int k) => k < objs.Length ? objs[k] : null;
        CmpClothes C(int k) => O(k) ? O(k).GetComponent<CmpClothes>() : null;
        void Want(string what, GameObject g, bool on) { if (g && g.activeSelf != on) errs.Add(what + " " + g.activeSelf + " want " + on); }
        Want("top", O(0), !sil && e[0] != 2);
        if (C(0) != null) { Want("top.topDef", C(0).objTopDef, e[0] == 0); Want("top.topHalf", C(0).objTopHalf, e[0] == 1); Want("top.botDef", C(0).objBotDef, e[1] == 0 && e[0] != 2); Want("top.botHalf", C(0).objBotHalf, e[1] != 0 && e[0] != 2); }
        Want("bottom", O(1), !sil && !st.NotBot && e[1] != 2);
        if (C(1) != null) { Want("bot.botDef", C(1).objBotDef, e[1] == 0); Want("bot.botHalf", C(1).objBotHalf, e[1] == 1); Want("bot.topDef", C(1).objTopDef, e[0] == 0 && e[1] != 2); Want("bot.topHalf", C(1).objTopHalf, e[0] != 0 && e[1] != 2); }
        Want("bra", O(2), !sil && !st.NotInnerT && e[2] != 2);
        if (C(2) != null) { Want("bra.topDef", C(2).objTopDef, e[2] == 0); Want("bra.topHalf", C(2).objTopHalf, e[2] == 1); Want("bra.botDef", C(2).objBotDef, e[3] == 0 && e[2] != 2); Want("bra.botHalf", C(2).objBotHalf, e[3] != 0 && e[2] != 2); }
        Want("shorts", O(3), !sil && !st.NotInnerB && e[3] != 2);
        if (C(3) != null) { Want("shorts.botDef", C(3).objBotDef, e[3] == 0); Want("shorts.botHalf", C(3).objBotHalf, e[3] == 1); }
        Want("gloves", O(4), !sil && e[4] == 0);
        Want("pantyhose", O(5), !sil && e[5] != 2);
        if (C(5) != null) { Want("panst.botDef", C(5).objBotDef, e[5] == 0); Want("panst.botHalf", C(5).objBotHalf, e[5] == 1); }
        Want("socks", O(6), !sil && e[6] == 0);
        Want("shoes", O(7), !sil && e[7] == 0);
        return errs;
    }

    static double Pct(List<double> l, double p)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)Math.Ceiling(p * s.Count) - 1)];
    }

    static void S3()
    {
        using (var env = new Env())
        {
            foreach (var (label, card) in new[] { ("BR-Chan", femaleCard), ("HERO107", maleCard) })
            {
                var id = env.Add(card);
                var r = env.C(id);
                int cases = 0;
                var errs = new List<string>();
                var applyMs = new List<double>();
                var opMs = new List<double>();
                int present = r.Context.Clothes.Count(x => x);
                for (int k = 0; k < 8; k++)
                    for (int s = 0; s < 3; s++)
                    {
                        env.Ops.SetClothesAll(new[] { id }, 0, User);
                        var sw = Stopwatch.StartNew();
                        var res = env.Ops.SetClothes(new[] { id }, k, s, true, User);
                        opMs.Add(sw.Elapsed.TotalMilliseconds);
                        if (!res.Ok) { errs.Add($"kind {k} state {s}: {res}"); continue; }
                        applyMs.Add(r.StateRealizer.LastClothesMs);
                        var stored = env.St(id).State.Clothes;
                        var want = r.ClothesStates.Apply(new int[8], k, s);
                        if (!stored.SequenceEqual(want)) errs.Add($"kind {k} state {s}: stored {string.Join("", stored)} want {string.Join("", want)}");
                        var e = r.StateRealizer.Realized;
                        if (!e.SequenceEqual(r.ClothesStates.EffectiveAll(stored))) errs.Add($"kind {k} state {s}: realized {string.Join("", e)}");
                        foreach (var x in ActivityErrors(r, e)) errs.Add($"kind {k} state {s}: {x}");
                        cases++;
                    }
                Check($"{label}: 8 kinds x 3 requests through StudioOps.SetClothes: stored = Apply, realized = Effective, part / def / half activity = the state rule ({cases} cases, {present} parts present)",
                    errs.Count == 0, errs.Count > 0 ? errs.Count + " errors; " + string.Join("; ", errs.Take(5)) : "");
                foreach (var all in new[] { 2, 1, 0 })
                {
                    env.Ops.SetClothesAll(new[] { id }, all, User);
                    var ae = ActivityErrors(r, r.StateRealizer.Realized);
                    Check($"{label}: All {all}: activity = the state rule", ae.Count == 0, string.Join("; ", ae.Take(5)));
                }
                Measure($"U-M6 {label}: clothes apply (visibility + masks) median {Pct(applyMs, 0.5):F3} ms, p95 {Pct(applyMs, 0.95):F3} ms, max {applyMs.DefaultIfEmpty(0).Max():F3} ms over {applyMs.Count}; the whole op (transaction + apply) median {Pct(opMs, 0.5):F3} ms, p95 {Pct(opMs, 0.95):F3} ms");
                // undo restores the activity
                env.Ops.SetClothes(new[] { id }, 0, 2, true, User);
                env.S.Commands.Undo();
                env.Rt.ApplyNow();
                Check(label + ": undo of top off restores the stored state and the activity", env.St(id).State.Clothes[0] == 0 && ActivityErrors(r, r.StateRealizer.Realized).Count == 0);
            }
        }
    }

    // ================================================================ S4 masks

    static double? Val(List<(string target, Material[] mats)> t, string target, string p)
    {
        foreach (var (name, mats) in t)
            if (name == target)
                foreach (var m in mats) if (m && m.HasProperty(p)) return m.GetFloat(p);
        return null;
    }

    static void S4()
    {
        using (var env = new Env())
        {
            var id = env.Add(femaleCard);
            var r = env.C(id);
            var t0 = r.Assembler.ClothesMaskTargets();
            double? d0 = Val(t0, "body", "_alpha_d");
            Info("BR-Chan mask materials: " + string.Join(", ", t0.Select(x => x.target + "[" + x.mats.Length + "]")) + "; build _alpha_d " + d0);
            var set = r.ClothesStates;
            foreach (var top in new[] { 0, 1, 2 })
            {
                if (!set.Supports(0, top) && set.IsStateKind(0)) { Info("top state " + top + " not offered"); continue; }
                env.Ops.SetClothesAll(new[] { id }, 0, User);
                env.Ops.SetClothes(new[] { id }, 0, top, true, User);
                var e = r.StateRealizer.Realized;
                var t = r.Assembler.ClothesMaskTargets();
                double wa = e[0] == 0 ? 1 : 0, wb = e[0] == 2 ? 0 : 1;
                foreach (var target in new[] { "body", "mask_braA" })
                {
                    var a = Val(t, target, "_alpha_a");
                    var b = Val(t, target, "_alpha_b");
                    if (a == null && b == null) { Info(target + ": no _alpha_a / _alpha_b"); continue; }
                    Check($"top {top} (realized {e[0]}): {target} _alpha_a / _alpha_b = ({wa}, {wb})", a == wa && b == wb, a + ", " + b);
                }
            }
            foreach (var bot in new[] { 0, 1, 2 })
            {
                env.Ops.SetClothesAll(new[] { id }, 0, User);
                env.Ops.SetClothes(new[] { id }, 1, bot, true, User);
                var e = r.StateRealizer.Realized;
                var t = r.Assembler.ClothesMaskTargets();
                var d = Val(t, "body", "_alpha_d");
                if (d == null) { Info("body has no _alpha_d"); break; }
                double want = e[1] == 0 ? d0 ?? 0 : 0;
                Check($"bottom {bot} (realized {e[1]}): body _alpha_d = {(e[1] == 0 ? "the build's value" : "0")}", d == want, "" + d);
                foreach (var target in new[] { "mask_innerTB", "mask_innerB", "mask_panst" })
                {
                    var dd = Val(t, target, "_alpha_d");
                    if (dd != null) Check($"bottom {bot}: {target} _alpha_d = body's", dd == d, "" + dd);
                }
            }
            env.Ops.SetClothesAll(new[] { id }, 0, User);
            Check("all on again: body _alpha_a / _alpha_b / _alpha_d = the build's values", Val(r.Assembler.ClothesMaskTargets(), "body", "_alpha_a") == Val(t0, "body", "_alpha_a")
                && Val(r.Assembler.ClothesMaskTargets(), "body", "_alpha_d") == d0);
        }
    }

    // ================================================================ S5 other members

    static void S5()
    {
        using (var env = new Env())
        {
            var id = env.Add(femaleCard);
            var r = env.C(id);
            var body = r.Assembler.LiveBodyDraw();   // the URP copies the renderers hold
            var face = r.Assembler.LiveFaceDraw();
            float g0 = body && body.HasProperty("_Gloss") ? body.GetFloat("_Gloss") : float.NaN;
            var res = env.Ops.SetState(new[] { id }, new (string, object)[] { ("wet", 0.6f), ("gloss", 0.5f), ("liquid", new[] { 1, 2, 0, 1, 2 }), ("nipple", 0.7f) }, User);
            Check("SetState wet, gloss, liquid, nipple: one entry \"Character state\"", res.Ok && env.S.Commands.UndoList.Count > 0 && env.S.Commands.UndoList[0].Label == "Character state", res.ToString());
            if (body && body.HasProperty("_ExGloss")) Check("body _ExGloss = wet", Mathf.Approximately(body.GetFloat("_ExGloss"), 0.6f), "" + body.GetFloat("_ExGloss"));
            if (face && face.HasProperty("_ExGloss")) Check("face _ExGloss = wet", Mathf.Approximately(face.GetFloat("_ExGloss"), 0.6f));
            int hairWet = 0, hairAll = 0;
            foreach (var h in r.Context.Hair) if (h) foreach (var rr in h.GetComponentsInChildren<Renderer>(true)) foreach (var m in rr.sharedMaterials) if (m && m.HasProperty("_ExGloss")) { hairAll++; if (Mathf.Approximately(m.GetFloat("_ExGloss"), 0.6f)) hairWet++; }
            Check($"hair materials with _ExGloss = wet ({hairWet} / {hairAll})", hairWet == hairAll);
            int hairOwned = 0, hairMats = 0;
            foreach (var h in r.Context.Hair) if (h) foreach (var rr in h.GetComponentsInChildren<Renderer>(true)) foreach (var m in rr.sharedMaterials) if (m) { hairMats++; if (r.Context.OwnedMaterials.Contains(m)) hairOwned++; }
            Info($"hair materials in Context.OwnedMaterials: {hairOwned} / {hairMats}");
            if (!float.IsNaN(g0)) Check("body _Gloss = the build's value + 0.2 x gloss", Mathf.Abs(body.GetFloat("_Gloss") - (g0 + 0.1f)) < 1e-6f, g0 + " -> " + body.GetFloat("_Gloss"));
            if (face && face.HasProperty("_WeatheringRange6")) Check("face _WeatheringRange6 = 0.5 (liquid[0] = 1)", Mathf.Approximately(face.GetFloat("_WeatheringRange6"), 0.5f), "" + face.GetFloat("_WeatheringRange6"));
            var mats = r.Context.BodyRenderer ? r.Context.BodyRenderer.sharedMaterials : new Material[0];
            if (mats.Length > 1 && mats[1] && mats[1].HasProperty("_WeatheringRange1"))
                Check("body liquid slot _WeatheringRange1..4 = 1, 0, 0.5, 1", new[] { "_WeatheringRange1", "_WeatheringRange2", "_WeatheringRange3", "_WeatheringRange4" }.Select(p => mats[1].GetFloat(p)).SequenceEqual(new[] { 1f, 0f, 0.5f, 1f }),
                    string.Join(",", new[] { 1, 2, 3, 4 }.Select(i => mats[1].GetFloat("_WeatheringRange" + i))));
            else Info("body renderer has no liquid slot with _WeatheringRange1 (" + mats.Length + " slots)");
            int clothesLiquid = 0;
            for (int k = 0; k < 5; k++)
            {
                var cmp = r.Context.Clothes[k] ? r.Context.Clothes[k].GetComponent<CmpClothes>() : null;
                if (cmp?.rendNormal01 == null) continue;
                foreach (var rr in cmp.rendNormal01) if (rr) foreach (var m in rr.sharedMaterials) if (m && m.HasProperty("_WeatheringRange2") && Mathf.Approximately(m.GetFloat("_WeatheringRange2"), 0f) && Mathf.Approximately(m.GetFloat("_WeatheringRange1"), 1f)) clothesLiquid++;
            }
            Info("clothes materials of kinds 0..4 with the liquid values: " + clothesLiquid);
            // nipple: shape key 32 through the body shape (data name only); the realizer re-applied it without an exception
            Check("nipple re-applied the body shape (no exception)", r.Context.BodyShape == null || env.St(id).State.Nipple == 0.7f);
            env.S.Commands.Undo();
            env.Rt.ApplyNow();
            if (body && body.HasProperty("_ExGloss")) Check("undo: body _ExGloss back to 0", Mathf.Approximately(body.GetFloat("_ExGloss"), 0f), "" + body.GetFloat("_ExGloss"));
            if (!float.IsNaN(g0)) Check("undo: body _Gloss back to the build's value", Mathf.Abs(body.GetFloat("_Gloss") - g0) < 1e-6f);
            // a second BR-Chan must not see the first one's wet value (no shared material is written)
            env.Ops.SetState(new[] { id }, "wet", 0.6f, User);
            var id2 = env.Add(femaleCard);
            var r2 = env.C(id2);
            int leaked = 0;
            foreach (var h in r2.Context.Hair) if (h) foreach (var rr in h.GetComponentsInChildren<Renderer>(true)) foreach (var m in rr.sharedMaterials) if (m && m.HasProperty("_ExGloss") && Mathf.Approximately(m.GetFloat("_ExGloss"), 0.6f)) leaked++;
            Check("a second character of the same card keeps its own hair materials (wet not shared)", leaked == 0, leaked + " materials");

            // son on the male target (data names: N_dan, Studio Bone row 65)
            var mid = env.Add(maleCard);
            var m2 = env.C(mid);
            var dan = FindDeep(m2.Context.Body ? m2.Context.Body.transform : null, "N_dan");
            Check("male: the son object is inactive after the add (son.visible false)", dan == null || !dan.gameObject.activeSelf);
            env.Ops.SetState(new[] { mid }, new (string, object)[] { ("son.visible", true), ("son.length", 1.5f) }, User);
            if (dan) Check("son.visible true: the son object active", dan.gameObject.activeSelf);
            var rows65 = FkRows.From(services.Db).Where(x => x.No == 65).ToList();
            if (rows65.Count > 0 && !string.IsNullOrEmpty(rows65[0].Name) && m2.Context.BodyBones.TryGetValue(rows65[0].Name, out var bone))
                Check("son.length 1.5: the son bone's uniform scale", Vector3.Distance(bone.localScale, Vector3.one * 1.5f) < 1e-5f, bone.localScale.ToString("F4"));
            else Info("no Studio Bone row 65 transform on the male body");
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (!t) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r) return r; }
        return null;
    }

    // ================================================================ S10 gloss state on the face (Phase 4.1 package A)

    /// <summary>
    /// (GF1, F9): face _Gloss = the value draw.face wrote (lerp(0, 0.8, skin gloss)) + 0.2 × the gloss
    /// state, as the body; both directions (state on / off, undo / redo), after draw.face edits, a skin-gloss edit, a head
    /// swap (a new face draw) and a rebuild (ChangeCharacter keeps the node state); the stored state is never written.
    /// </summary>
    static void S10()
    {
        // Maker edits import a card-backed node first (MakerEdits.EnsureImported needs an importer)
        var keepImporter = CharaImport.Importer;
        if (CharaImport.Importer == null) CharaImport.Importer = CharaDocBuildTest.NewImporter(services.Catalog);
        try { S10Core(); }
        finally { CharaImport.Importer = keepImporter; }
    }

    static void S10Core()
    {
        using (var env = new Env())
        {
            var id = env.Add(femaleCard);
            var r = env.C(id);
            Material Face() => env.C(id).Assembler.LiveFaceDraw();
            Material Body() => env.C(id).Assembler.LiveBodyDraw();
            float G(Material m) => m && m.HasProperty("_Gloss") ? m.GetFloat("_Gloss") : float.NaN;
            float Skin() => env.C(id).Context.Chara.Body.Skin.Gloss;   // the documents the units read (card-backed until the first edit)
            float Base() => Mathf.Lerp(0, 0.8f, Skin());
            bool Near(float a, float b) => Mathf.Abs(a - b) < 1e-6f;
            void Apply() => env.Rt.ApplyNow();
            if (!Check("the face draw declares _Gloss", Face() && Face().HasProperty("_Gloss"))) return;
            Check("state 0: face _Gloss = lerp(0, 0.8, skin gloss)", Near(G(Face()), Base()), $"{G(Face())} vs {Base()}");
            env.Ops.SetState(new[] { id }, "gloss", 0.5f, User); Apply();
            Check("state 0.5: face _Gloss = base + 0.1", Near(G(Face()), Base() + 0.1f), "" + G(Face()));
            Check("state 0.5: body _Gloss = its base + 0.1 (unchanged rule)", Near(G(Body()), r.StateRealizer.BaseBodyGloss + 0.1f), $"{G(Body())} vs {r.StateRealizer.BaseBodyGloss}");
            Check("eyebrow tilt edit ok", env.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.face.eyebrow.tilt", 0.66f) }, User).Ok); Apply();
            Check("after a draw.face edit (eyebrow tilt): base + 0.1", Near(G(Face()), Base() + 0.1f), "" + G(Face()));
            Check("skin gloss edit ok (0.25)", env.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.chara.body.skin.gloss", 0.25f) }, User).Ok && Mathf.Approximately(Skin(), 0.25f), "" + Skin()); Apply();
            Check("after a skin gloss edit: the new base + 0.1 on the face", Near(G(Face()), Base() + 0.1f), $"{G(Face())} vs {Base() + 0.1f}");
            Check("after a skin gloss edit: the new base + 0.1 on the body", Near(G(Body()), Base() + 0.1f), $"{G(Body())} vs {Base() + 0.1f}");
            var face0 = Face();
            string head = env.C(id).Context.Chara.Face.Head == "illusion.hs2#3" ? "illusion.hs2#1" : "illusion.hs2#3";
            Check("head swap ok", env.Ops.ChangeItem(new[] { id }, "appearance.chara.face.head", head, User).Ok); Apply();
            Check("after a head swap: a new face draw with base + 0.1", Face() != face0 && Near(G(Face()), Base() + 0.1f), "" + G(Face()));
            env.S.Commands.Undo(); Apply();
            Check("undo of the head swap: base + 0.1", Near(G(Face()), Base() + 0.1f), "" + G(Face()));
            env.S.Commands.Redo(); Apply();
            Check("redo of the head swap: base + 0.1", Near(G(Face()), Base() + 0.1f), "" + G(Face()));
            var cc = env.Ops.ChangeCharacter(new[] { id }, femaleCard, null, User); Apply();
            Check("after a rebuild (ChangeCharacter keeps the state): base + 0.1", cc.Ok && Near(G(Face()), Base() + 0.1f), $"{cc} {G(Face())} vs {Base() + 0.1f}");
            env.S.Commands.Undo(); Apply();
            Check("undo of the rebuild: base + 0.1", Near(G(Face()), Base() + 0.1f), "" + G(Face()));
            env.Ops.SetState(new[] { id }, "gloss", 0f, User); Apply();
            Check("state 0: face _Gloss = base", Near(G(Face()), Base()), "" + G(Face()));
            env.S.Commands.Undo(); Apply();
            Check("undo of state 0: base + 0.1", Near(G(Face()), Base() + 0.1f), "" + G(Face()));
            Check("the stored state is 0.5 (not written by the realizer)", Mathf.Approximately(env.St(id).State.Gloss, 0.5f), "" + env.St(id).State.Gloss);
        }
    }

    // ================================================================ S6 silhouette

    /// <summary>The activity of the objects the silhouette switches, and every own renderer's materials (by reference).</summary>
    sealed class SilSnapshot
    {
        public readonly Dictionary<GameObject, bool> Active = new Dictionary<GameObject, bool>();
        public readonly Dictionary<Renderer, Material[]> Mats = new Dictionary<Renderer, Material[]>();
        public readonly Dictionary<Renderer, bool> Enabled = new Dictionary<Renderer, bool>();

        public static SilSnapshot Of(CharaRealization r, IEnumerable<GameObject> objs)
        {
            var s = new SilSnapshot();
            foreach (var o in objs) if (o && !s.Active.ContainsKey(o)) s.Active[o] = o.activeSelf;
            foreach (var x in HS2Next.Scenes.Runtime.VisibilityApplier.OwnRenderers(r.Holder))
                if (x && !(r.StateRealizer.SilhouetteBody && x.transform.IsChildOf(r.StateRealizer.SilhouetteBody.transform)))
                {
                    s.Mats[x] = x.sharedMaterials;
                    s.Enabled[x] = x.enabled;
                }
            return s;
        }

        /// <summary>Differences against an earlier snapshot (empty = equal).</summary>
        public List<string> Diff(SilSnapshot b)
        {
            var d = new List<string>();
            foreach (var kv in b.Active) if (kv.Key && (!Active.TryGetValue(kv.Key, out var v) || v != kv.Value)) d.Add(kv.Key.name + " active " + (Active.TryGetValue(kv.Key, out var v2) ? v2.ToString() : "?") + " was " + kv.Value);
            foreach (var kv in b.Mats) if (kv.Key && (!Mats.TryGetValue(kv.Key, out var m) || !m.SequenceEqual(kv.Value))) d.Add(kv.Key.name + " materials");
            foreach (var kv in b.Enabled) if (kv.Key && (!Enabled.TryGetValue(kv.Key, out var e) || e != kv.Value)) d.Add(kv.Key.name + " enabled");
            return d;
        }
    }

    static void S6()
    {
        foreach (var (label, card, male) in new[] { ("female BR-Chan", femaleCard, false), ("male HERO107", maleCard, true) })
        {
            using (var env = new Env())
            {
                var id = env.Add(card);
                var r = env.C(id);
                var ctx = r.Context;
                var cmp = ctx.Body ? ctx.Body.GetComponent<CmpBody>() : null;
                var bodyObj = cmp?.targetEtc?.objBody;
                var tongue = cmp?.targetEtc?.objTongue;
                var hair = ctx.Hair.Where(h => h).ToList();
                var acc = ctx.Accessories.Where(a => a != null && a.Object && !a.IsEmpty).Select(a => a.Object).ToList();
                var clothes = ctx.Clothes.Where(c => c).ToList();
                var dan = FindDeep(ctx.Body ? ctx.Body.transform : null, "N_dan");
                var watched = new List<GameObject> { bodyObj, ctx.Head, dan ? dan.gameObject : null };
                watched.AddRange(hair); watched.AddRange(acc); watched.AddRange(clothes);
                var before = SilSnapshot.Of(r, watched);
                Check($"{label}: no silhouette body before the first use", r.StateRealizer.SilhouetteBody == null && !r.StateRealizer.SilhouetteOn);
                var col = new Color4(0.2f, 0.3f, 0.8f, 0.5f);
                var res = env.Ops.SetState(new[] { id }, new (string, object)[] { ("silhouette", new CharaSilhouette { Enabled = true, Color = col }) }, User);
                env.Rt.ApplyNow();
                Check($"{label} silhouette on: one entry", res.Ok && env.S.Commands.UndoList.Count(e => e.Label == "Character state") >= 1, res.ToString());
                var simple = r.StateRealizer.SilhouetteBody;
                var dump = r.StateRealizer.SilhouetteDump();
                var sc = simple ? simple.GetComponent<CmpBody>() : null;
                var rends = simple ? simple.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                string bodyName = male ? "o_silhouette_cm" : "o_silhouette_cf";
                var main = rends.FirstOrDefault(x => x.name == bodyName);
                Check($"{label}: the simple body {(male ? "p_cm_body_silhouette" : "p_cf_body_silhouette")} built under BodyTop and shown",
                    simple && simple.transform.parent == ctx.BodyTop.transform && simple.activeInHierarchy && main && main.gameObject.activeInHierarchy,
                    string.Join(", ", rends.Select(x => x.name + (x.gameObject.activeInHierarchy ? "" : " (off)"))));
                Check($"{label}: the simple body draws HS2Next/Custom/Silhouette with silhouette.color", main && main.sharedMaterial && main.sharedMaterial.shader.name == "HS2Next/Custom/Silhouette"
                    && main.sharedMaterial.GetColor("_Color") == new Color(0.2f, 0.3f, 0.8f, 0.5f) && ctx.OwnedMaterials.Contains(main.sharedMaterial),
                    main && main.sharedMaterial ? main.sharedMaterial.shader.name + " " + main.sharedMaterial.GetColor("_Color") : "none");
                var smr = main as SkinnedMeshRenderer;
                Check($"{label}: the simple body is skinned to the character's bones", smr && smr.bones.Length > 0 && smr.bones.All(b => b == null || ctx.BodyBones.TryGetValue(b.name, out var t) && t == b) && smr.rootBone == ctx.Hips,
                    smr ? smr.bones.Length + " bones" : "no skinned mesh");
                Check($"{label}: the body mesh, the head, hair, clothes and accessories hidden",
                    (!bodyObj || !bodyObj.activeSelf) && (!ctx.Head || !ctx.Head.activeSelf) && hair.All(h => !h.activeSelf) && acc.All(a => !a.activeSelf) && clothes.All(c => !c.activeSelf),
                    $"body {(bodyObj ? bodyObj.activeSelf.ToString() : "-")}, head {(ctx.Head ? ctx.Head.activeSelf.ToString() : "-")}, hair {hair.Count(h => h.activeSelf)}/{hair.Count}, accessories {acc.Count(a => a.activeSelf)}/{acc.Count}, clothes {clothes.Count(c => c.activeSelf)}/{clothes.Count}");
                Check($"{label}: the body tongue's renderers off", !tongue || tongue.GetComponentsInChildren<Renderer>(true).All(x => !x.enabled));
                var same = SilSnapshot.Of(r, watched);
                Check($"{label}: no renderer of the character changed its materials (the silhouette is its own body)", before.Mats.All(kv => !kv.Key || same.Mats.TryGetValue(kv.Key, out var m) && m.SequenceEqual(kv.Value)));
                if (!male)
                {
                    var mos = rends.FirstOrDefault(x => x.name == "o_silhouette_cf_mnpb");
                    Check($"{label}: the simple body's mosaic shown (HS2Next/Mosaic)", mos && mos.gameObject.activeInHierarchy && mos.sharedMaterial && mos.sharedMaterial.shader.name == HS2Next.Chara.Mosaic.MosaicSet.ShaderName, mos ? mos.sharedMaterial?.shader.name : "none");
                }
                var sdan = sc?.targetEtc?.objDanTop;
                Check($"{label}: son off: the simple body's son object off", !sdan || !sdan.activeSelf);
                env.Ops.SetState(new[] { id }, "son.visible", true, User);
                env.Rt.ApplyNow();
                Check($"{label}: son on during the silhouette: the son objects of both bodies active (HS2)", (!sdan || sdan.activeSelf) && (!dan || dan.gameObject.activeSelf), $"simple {(sdan ? sdan.activeSelf.ToString() : "-")}, body {(dan ? dan.gameObject.activeSelf.ToString() : "-")}");
                env.S.Commands.Undo();
                env.Rt.ApplyNow();
                env.Ops.SetState(new[] { id }, "silhouette.color", new Color4(1f, 0f, 1f, 1f), User);
                env.Rt.ApplyNow();
                Check($"{label}: silhouette.color while on: the simple body's colour follows (opaque)", main && main.sharedMaterial.GetColor("_Color") == new Color(1f, 0f, 1f, 1f) && r.StateRealizer.SilhouetteOn);
                env.S.Commands.Undo();
                env.S.Commands.Undo();
                env.Rt.ApplyNow();
                var after = SilSnapshot.Of(r, watched);
                var diff = after.Diff(before);
                Check($"{label} undo: the silhouette off, every object, renderer switch and material as before", !r.StateRealizer.SilhouetteOn && simple && !simple.activeSelf && diff.Count == 0
                    && (!tongue || tongue.GetComponentsInChildren<Renderer>(true).All(x => x.enabled)) && ActivityErrors(r, r.StateRealizer.Realized).Count == 0,
                    string.Join("; ", diff.Take(8)));
                Check($"{label}: no chara.silhouetteApprox / chara.silhouetteMissing finding", !env.S.Findings.Any(f => f.Code == "chara.silhouetteApprox" || f.Code == "chara.silhouetteMissing"));
                Info($"{label}: silhouette dump {Newtonsoft.Json.JsonConvert.SerializeObject(dump)}");

                // a scene saved with the silhouette on shows it when loaded
                env.Ops.SetState(new[] { id }, new (string, object)[] { ("silhouette", new CharaSilhouette { Enabled = true, Color = col }) }, User);
                env.Rt.ApplyNow();
                var o = new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = () => new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc) };
                var bytes = env.Rt.Ops.ToBytes(o);
                var lr = env.Rt.Ops.Load(bytes);
                env.Rt.ApplyNow();
                var nid = env.S.Model.PreOrder().First(n => n.Kind == NodeKind.Chara).Id;
                var nr = env.C(nid);
                var ns = env.St(nid).State.Silhouette;
                Check($"{label}: save -> load keeps state.silhouette and draws it", lr.Ok && ns.Enabled && ns.Color.Equals(col) && nr.StateRealizer.SilhouetteOn && nr.StateRealizer.SilhouetteBody && nr.StateRealizer.SilhouetteBody.activeInHierarchy
                    && nr.Context.Hair.Where(h => h).All(h => !h.activeSelf), lr.Error?.Message);
                var again = env.Rt.Ops.ToBytes(o);
                Check($"{label}: save -> load -> save byte-identical", bytes.SequenceEqual(again), bytes.Length + " / " + again.Length + " bytes");
            }
        }
    }

    // ================================================================ S7 unsupported stored value

    static void S7()
    {
        using (var env = new Env())
        {
            var id = env.Add(femaleCard);
            var r = env.C(id);
            var set = r.ClothesStates;
            int k = Enumerable.Range(0, 8).FirstOrDefault(x => set.IsStateKind(x) && !set.Supports(x, 1));
            if (!set.IsStateKind(k) || set.Supports(k, 1)) { Check("a two-state kind on BR-Chan's outfit", false, set.ToString()); return; }
            var v = new int[8];
            v[k] = 1;
            var w = env.S.Set(id, "chara.state.clothes", v, User);   // a stored value the op would not write (as a loaded file may hold)
            env.Rt.ApplyNow();
            Check($"stored half on two-state {ClothesStateSet.KindNames[k]} kept in the model", w.Ok && env.St(id).State.Clothes[k] == 1);
            Check("realized as the forward cycling's state (off)", r.StateRealizer.Realized[k] == set.Effective(k, 1) && set.Effective(k, 1) == 2, "" + r.StateRealizer.Realized[k]);
            Check("finding chara.clothesStateAdjusted (info)", env.S.Findings.Any(f => f.Code == "chara.clothesStateAdjusted" && f.Severity == FindingSeverity.Info));
            var o = new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = () => new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc) };
            var a = env.Rt.Ops.ToBytes(o);
            var lr = env.Rt.Ops.Load(a);
            Check("load", lr.Ok, lr.Error?.Message);
            var nid = env.S.Model.PreOrder().First(n => n.Kind == NodeKind.Chara).Id;
            Check("after load: the stored value is still 1", env.St(nid).State.Clothes[k] == 1);
            var b = env.Rt.Ops.ToBytes(new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = () => new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc) });
            Check("save -> load -> save byte-identical", a.SequenceEqual(b), a.Length + " / " + b.Length + " bytes");
        }
    }

    // ================================================================ S9 mask targets' shaders

    static void S9()
    {
        using (var env = new Env())
        {
            var id = env.Add(femaleCard);
            var r = env.C(id);
            foreach (int k in new[] { 2, 3, 5 })
            {
                var o = r.Context.Clothes[k];
                var e = r.Context.ClothesEntries[k];
                if (!o) { Info("kind " + k + ": no part"); continue; }
                foreach (var rr in o.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in rr.sharedMaterials)
                        if (m) Info($"kind {k} {rr.name}: shader {m.shader?.name}, _alpha_a {m.HasProperty("_alpha_a")}, _alpha_d {m.HasProperty("_alpha_d")}, _AlphaMask {m.HasProperty("_AlphaMask")}");
                GameObject prefab = null;
                try { prefab = services.Catalog.LoadAsset<GameObject>(e, "MainAB", "MainData"); } catch (Exception) { }
                if (prefab)
                    foreach (var rr in prefab.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in rr.sharedMaterials)
                            if (m) Info($"kind {k} prefab {rr.name}: shader {m.shader?.name}, _alpha_a {m.HasProperty("_alpha_a")}, _alpha_d {m.HasProperty("_alpha_d")}");
            }
            var br = r.Context.BodyRenderer;
            var live = r.Assembler.LiveBodyDraw();
            Check("the body draw material written is the one the body renderer holds (the URP copy)", br && br.sharedMaterials.Contains(live) && live.HasProperty("_alpha_a"), live ? live.shader.name : "null");
            var liveFace = r.Assembler.LiveFaceDraw();
            Check("the face draw material written is one a head renderer holds", r.Context.Head && r.Context.Head.GetComponentsInChildren<Renderer>(true).Any(x => x.sharedMaterials.Contains(liveFace)), liveFace ? liveFace.shader.name : "null");
            var targets = r.Assembler.ClothesMaskTargets();
            foreach (var (target, need) in new[] { ("mask_braA", "_alpha_a"), ("mask_innerB", "_alpha_d") })
            {
                var t = targets.First(x => x.target == target);
                var rend = target == "mask_braA" ? r.Context.Clothes[2] : r.Context.Clothes[3];
                if (!rend) continue;
                Check($"{target}: the reference renderer's current materials (the URP copies) are the targets and declare {need}", t.mats.Length > 0 && t.mats.All(m => m.HasProperty(need)),
                    t.mats.Length + " materials");
            }
            foreach (var (target, mats) in r.Assembler.ClothesMaskTargets())
                Info($"mask target {target}: {mats.Length} materials: " + string.Join(", ", mats.Select(m => m.shader?.name + (m.HasProperty("_alpha_a") ? " +a" : "") + (m.HasProperty("_alpha_d") ? " +d" : ""))));
        }
    }

    // ================================================================ S8 mask renderer names

    static void S8()
    {
        var cat = services.Catalog;
        foreach (var (category, part, names) in new[] { (242, "inner_t", new[] { "o_bra_a", "o_shorts_a" }), (243, "inner_b", new[] { "o_shorts_a" }), (245, "panst", new[] { "o_panst_a" }) })
        {
            var rows = cat.EntriesOf(category).Where(e => e != null && e["MainData"] != "p_dummy" && !string.IsNullOrEmpty(e["MainData"])).ToList();
            var pick = rows.Where(e => e.IsVanilla).Take(6).Concat(rows.Where(e => !e.IsVanilla).Take(4)).ToList();
            int loaded = 0;
            var found = names.ToDictionary(n => n, n => 0);
            foreach (var e in pick)
            {
                GameObject prefab = null;
                try { prefab = cat.LoadAsset<GameObject>(e, "MainAB", "MainData"); } catch (Exception) { }
                if (!prefab) continue;
                loaded++;
                var rn = new HashSet<string>(prefab.GetComponentsInChildren<Renderer>(true).Select(x => x.name));
                foreach (var n in names) if (rn.Contains(n)) found[n]++;
            }
            Measure($"{part} ({category}): {loaded} of {pick.Count} sampled rows loaded ({pick.Count(x => x.IsVanilla)} vanilla, {pick.Count(x => !x.IsVanilla)} mod); " +
                    string.Join(", ", found.Select(kv => kv.Key + " in " + kv.Value)));
            Check($"{part}: the sample loads", loaded > 0);
        }
        using (var env = new Env())
        {
            var id = env.Add(femaleCard);
            var r = env.C(id);
            var miss = r.Assembler.MaskRenderersMissing;
            Info("BR-Chan worn parts: missing reference renderers: " + (miss.Count == 0 ? "none" : string.Join(", ", miss)));
            Check("BR-Chan: a missing reference renderer is reported as chara.maskRendererMissing", miss.Count == 0 || env.S.Findings.Any(f => f.Code == "chara.maskRendererMissing"));
        }
    }

    // ================================================================ S11 (Phase 4.5 package A, XT7)

    static AccessoryRules Rules(params string[] names)
    {
        var r = new AccessoryRules();
        foreach (var n in names) r.Visibility.Add(new AacRule { Rule = n });
        return r;
    }

    /// <summary>
    /// The visibility fold: (1) CardStateImport.FoldAccessories on documents — an accessory
    /// with STUDIO_LOAD + STARTUP is hidden, one with STARTUP alone is not (Studio runs only STUDIO_LOAD slots), a MoreAccessories
    /// slot ≥ 20 whose element stores show="false" is hidden, the rest stay, an earlier hide stays; (2) the scene: a coordinate
    /// whose first accessory carries STUDIO_LOAD + STARTUP worn on BR-Chan with ApplyCoordinate(All) → one entry, that accessory
    /// false in state.accessories and its slot object inactive after the apply (the build does not hide it, the state does), the
    /// others shown; undo restores the state map and the objects' activity; redo hides it again.
    /// </summary>
    static void S11()
    {
        // the node is imported by a first Maker edit (MakerEdits.EnsureImported needs an importer), as S10
        var keepImporter = CharaImport.Importer;
        if (CharaImport.Importer == null) CharaImport.Importer = CharaDocBuildTest.NewImporter(services.Catalog);
        try { S11Core(); }
        finally { CharaImport.Importer = keepImporter; }
    }

    static void S11Core()
    {
        using (var env = new Env())
        {
            string id = env.Add(femaleCard);
            var imp = env.Ops.EditAppearance(new[] { id }, new List<(string, object)> { ("appearance.coord.name", "s11") }, User);
            env.Rt.ApplyNow();
            var stored = env.S.Model.Find(id).Appearance.Stored;
            Check("BR-Chan imported by a first edit", imp.Ok && stored != null, imp.ToString());
            if (stored == null) return;
            Check("BR-Chan wears at least two accessories", stored.Coord.Accessories.Count >= 2, stored.Coord.Accessories.Count.ToString());
            if (stored.Coord.Accessories.Count < 2) return;
            // (1) the fold function
            var coord = DocClone.Clone(stored.Coord);
            coord.Accessories[0].Rules = Rules("STUDIO_LOAD", "STARTUP");
            coord.Accessories[1].Rules = Rules("STARTUP");
            var extra = new AccessoryEntry { Id = JsonText.DerivedId("s11/extra"), Category = 352, Item = "illusion.hs2#0", ImportSlot = 25 };
            coord.Accessories.Add(extra);
            var st = new CharaNodeState();
            foreach (var a in coord.Accessories) st.State.Accessories[a.Id] = true;
            if (coord.Accessories.Count > 2) st.State.Accessories[coord.Accessories[2].Id] = false;
            var hid = HS2Next.Import.CardStateImport.FoldAccessories(coord, new int[8], new Dictionary<int, bool> { [25] = false }, st);
            var m = st.State.Accessories;
            Check("fold: STUDIO_LOAD + STARTUP hidden, STARTUP alone shown, show=false of slot 25 hidden, an earlier hide kept, the rest shown",
                  !m[coord.Accessories[0].Id] && m[coord.Accessories[1].Id] && !m[extra.Id] && (coord.Accessories.Count <= 3 || coord.Accessories.Skip(3).Where(a => a != extra).All(a => m[a.Id]))
                  && (coord.Accessories.Count <= 2 || !m[coord.Accessories[2].Id]),
                  string.Join(",", coord.Accessories.Select(a => (m[a.Id] ? 1 : 0).ToString())));
            Check("fold: the ids it hid = the STARTUP accessory and slot 25", hid.Count == 2 && hid.Contains(coord.Accessories[0].Id) && hid.Contains(extra.Id), string.Join(",", hid));
            var st2 = new CharaNodeState();
            foreach (var a in coord.Accessories) st2.State.Accessories[a.Id] = true;
            HS2Next.Import.CardStateImport.FoldAccessories(coord, new int[8], null, st2, false);
            Check("fold with Additional Accessory Controls off: nothing hidden", st2.State.Accessories.Values.All(v => v));
            // (2) the scene: a coordinate apply
            var res = env.S.Model.File.ExtractCharacter(id).Res;
            var source = DocClone.Clone(stored.Coord);
            // a coordinate from elsewhere: its accessories have ids of their own (the fold runs for the new ids)
            for (int i = 0; i < source.Accessories.Count; i++) source.Accessories[i].Id = JsonText.DerivedId("s11/source/" + i);
            source.Accessories[0].Rules = Rules("STUDIO_LOAD", "STARTUP");
            var before = new SortedDictionary<string, bool>(env.St(id).State.Accessories, StringComparer.Ordinal);
            int entries = env.S.Commands.UndoList.Count;
            var r = env.Ops.ApplyCoordinate(new[] { id }, source, res, CoordinateApplyMode.All, User);
            env.Rt.ApplyNow();
            Check("ApplyCoordinate(All) of the coordinate with STUDIO_LOAD + STARTUP: ok, one entry", r.Ok && env.S.Commands.UndoList.Count == entries + 1, r.ToString());
            var now = env.S.Model.Find(id).Appearance.Stored.Coord;
            var hiddenEntry = now.Accessories.FirstOrDefault(a => a.Rules?.Visibility != null && a.Rules.Visibility.Any(v => v.Rule == "STARTUP"));
            var ctx = env.C(id)?.Context;
            var table = HS2Next.Chara.Documents.AccessorySlotTable.Assign(now.Accessories, null);
            GameObject ObjOf(string accId) => ctx != null && table.SlotById.TryGetValue(accId, out int slot) && slot < ctx.Accessories.Count ? ctx.Accessories[slot].Object : null;
            var map = env.St(id).State.Accessories;
            bool hiddenOk = hiddenEntry != null && map.TryGetValue(hiddenEntry.Id, out bool hv) && !hv;
            var hobj = hiddenEntry != null ? ObjOf(hiddenEntry.Id) : null;
            Check("the accessory with STARTUP: false in state.accessories, its slot object inactive after the apply", hiddenOk && hobj && !hobj.activeSelf,
                  $"state {(hiddenEntry != null && map.TryGetValue(hiddenEntry.Id, out bool v2) ? v2.ToString() : "-")} object {(hobj ? hobj.activeSelf.ToString() : "none")}");
            var others = now.Accessories.Where(a => a != hiddenEntry).ToList();
            Check("the other accessories shown (state true, objects active)", others.All(a => map.TryGetValue(a.Id, out bool ov) && ov && ObjOf(a.Id) && ObjOf(a.Id).activeSelf));
            env.S.Commands.Undo();
            env.Rt.ApplyNow();
            var after = env.St(id).State.Accessories;
            var undone = env.S.Model.Find(id).Appearance.Stored.Coord;
            table = HS2Next.Chara.Documents.AccessorySlotTable.Assign(undone.Accessories, null);
            ctx = env.C(id)?.Context;
            Check("undo: the state map as before, every slot object active again",
                  after.Count == before.Count && after.All(kv => before.TryGetValue(kv.Key, out bool b) && b == kv.Value) && undone.Accessories.All(a => ObjOf(a.Id) && ObjOf(a.Id).activeSelf == before[a.Id]));
            env.S.Commands.Redo();
            env.Rt.ApplyNow();
            var redone = env.S.Model.Find(id).Appearance.Stored.Coord;
            var he = redone.Accessories.FirstOrDefault(a => a.Rules?.Visibility != null && a.Rules.Visibility.Any(v => v.Rule == "STARTUP"));
            Check("redo: hidden again", he != null && env.St(id).State.Accessories.TryGetValue(he.Id, out bool rv) && !rv);
        }
        // (3) adding a card folds the hides of a Studio load (CardAppearanceSource.HiddenAccessoryIds, set by the card source
        // factory): BR-Chan's card hides nothing; a source naming one of its accessories adds the node with that id false
        using (var env = new Env())
        {
            var src = new CardSourceFactory(services).FromFile(femaleCard);
            Check("card source of BR-Chan: no accessory hidden at a Studio load (no STUDIO_LOAD rule, no show=false)", src.HiddenAccessoryIds == null && src.AccessoryIds != null && src.AccessoryIds.Count > 1,
                  (src.HiddenAccessoryIds?.Count ?? 0) + " hidden of " + (src.AccessoryIds?.Count ?? 0));
            if (src.AccessoryIds == null || src.AccessoryIds.Count < 2) return;
            var hide = src.AccessoryIds[1];
            var src2 = new CardAppearanceSource
            {
                Hs2Path = src.Hs2Path, FullPath = src.FullPath, Sha256 = src.Sha256, Sex = src.Sex, Name = src.Name, AccessoryIds = src.AccessoryIds,
                ReadBytes = src.ReadBytes, HiddenAccessoryIds = new[] { hide },
            };
            var r = env.S.AddChara(new CharaAppearance { Card = src2 });
            env.Rt.ApplyNow();
            string nid = env.S.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).Select(x => x.Id).LastOrDefault();
            var map = nid != null ? env.St(nid).State.Accessories : null;
            Check("add from a card with a hidden accessory: that id false in state.accessories, the others true",
                  r.Status == CommandStatus.Ok && map != null && map.TryGetValue(hide, out bool hv) && !hv && src.AccessoryIds.Where(x => x != hide).All(x => map.TryGetValue(x, out bool ov) && ov),
                  map == null ? r.Message : string.Join(",", src.AccessoryIds.Select(x => map.TryGetValue(x, out bool v) ? (v ? "1" : "0") : "-")));
            var ctx = env.C(nid)?.Context;
            var slots = CardSourceFactory.AccessorySlots(HS2Next.Data.Card.Load(src.FullPath));
            int si = src.AccessoryIds.ToList().IndexOf(hide);
            var obj = ctx != null && si >= 0 && slots[si] < ctx.Accessories.Count ? ctx.Accessories[slots[si]].Object : null;
            Check("its slot object inactive after the apply (the state decides)", obj && !obj.activeSelf, obj ? obj.activeSelf.ToString() : "none");
        }
    }
}
