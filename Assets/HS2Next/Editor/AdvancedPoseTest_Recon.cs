// AdvancedPoseTest.Recon (Phase 6.3 package M, by hand, headless, not a row): why a modifier added at run time and a card's modifier at load
// split differently, the rule that predicts which channel of a card's modifier shows during play, the census over the cards with modifiers,
// and the mouth width bone. Frames are emulated in the order of a real frame: the modifiers' pass, then the motion's step.
// Numbers and bone names only; cards by the first 12 hex digits of their sha256; no image opened. Report Spike/out/advancedpose/recon.txt, census
// Spike/out/advancedpose/ao3_census.txt. Arguments: -part flip+classes+mouth+cards+trace, -minutes N (the cards' budget), -ah5 <folder of H's recordings>.
// Written before a card's modifiers became motion stages (6.3a): it explains the old split and is kept as a tool for the classes of a row's channels. The census that
// counts is AdvancedPoseTest.ReconPlay, with the modifiers off and on.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Motion.Face;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class AdvancedPoseTest
{
    const double ShowPos = 1e-4, ShowDeg = 0.01;

    static void ReconFrame(CharaRealization r, int n = 1, float dt = Dt)
    {
        for (int i = 0; i < n; i++) r.Motion.Step(dt);
    }

    static string ReconLoad(Host h, string rel, string sex)
    {
        var k = Key(h.Call(new { cmd = "chara.load", path = rel, sex }));
        if (k != null) h.Call(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false });
        h.Rt.ApplyNow();
        return k;
    }

    static Host ReconHost()
    {
        var h = NewHost();
        // as H's recording: the plugin's modifiers on, the rule effects and the hips offset off, the joint correction's rows on
        h.Call(new { cmd = "app.options", ext = new { abmx = true, accessoryControls = false }, fk = new { hipsOffset = false }, correctionAllOn = true });
        return h;
    }

    static void ReconRow(Host h, string k, int g = 19, int c = 0, int no = 0)
    {
        h.Call(new { cmd = "chara.anime", key = k, group = g, category = c, no, time = 0.5f, speed = 0f });
        h.Rt.ApplyNow();
    }

    /// <summary>Which of a modifier's channels the frame's step left as the modifiers' pass wrote them.</summary>
    static (bool rot, bool pos, bool scale) Kept(CharaRealization r, Transform t)
    {
        r.Context.Bones?.Apply();
        var w = Local(t);
        r.Motion.Step(Dt);
        var n = Local(t);
        return (Angle(w.r, n.r) <= ShowDeg, Dist(w.p, n.p) <= ShowPos, Dist(w.s, n.s) <= ShowPos);
    }

    // ------------------------------------------------------------ the flip: the same modifier, the same row, only the order differs

    static void ReconFlip(StringBuilder o)
    {
        string[] bones = { "cf_J_ArmUp00_R", "cf_J_ArmUp00_L", "cf_J_LegUp00_L", "cf_J_LegUp00_R", "cf_J_Foot01_L", "cf_J_Foot02_L", "cf_J_Hand_Index01_L", "cf_J_Hand_Index01_R", "cf_J_Hand_Thumb01_L", "cf_J_Hand_Index02_L", "cf_J_Toes01_L", "cf_J_Spine02", "cf_J_Hips", "cf_J_Neck", "cf_J_EarBase_s_L", "cf_J_Spine01_s" };
        o.AppendLine("== 1. the same test modifier (rotation 33 deg, position, length 1.1, scale (1.05, 0.95, 1.1)) on one card without modifiers, row 19/0/0 at 0.5, speed 0");
        o.AppendLine("   A: added after the first bind (as AO3). B: added and written before the first bind (as a card at load). C: B, then a row of another controller and back (a second bind).");
        o.AppendLine("   R P S: rotation, position / length, scale kept by the step (y) or rewritten (n).");
        var results = new Dictionary<string, Dictionary<string, string>>();
        foreach (var mode in new[] { "A", "B", "C" })
        {
            var h = ReconHost();
            try
            {
                var k = ReconLoad(h, femaleRel, "female");
                var r = h.Rt.FindChara(k);
                o.AppendLine($"   {mode}: controller before the first row: {(r.Animation.BaseController ? "bound" : "none")}; card modifiers {r.Context.Bones?.Modifiers.Count ?? 0}");
                if (mode == "A") { ReconRow(h, k); ReconFrame(r, 3); }
                AddMods(r, bones);
                if (mode != "A") { r.Context.Bones.Apply(); ReconRow(h, k); }
                ReconFrame(r, 10);
                if (mode == "C")
                {
                    var c0 = r.Animation.BaseController;
                    ReconRow(h, k, 11, 5, 0);
                    ReconFrame(r, 5);
                    bool other = r.Animation.BaseController != c0;
                    ReconRow(h, k);
                    ReconFrame(r, 10);
                    o.AppendLine("   C: the other row binds another controller: " + other);
                }
                var d = results[mode] = new Dictionary<string, string>();
                foreach (var b in bones)
                {
                    var (kr, kp, ks) = Kept(r, Bone(r, b));
                    d[b] = (kr ? "y" : "n") + (kp ? "y" : "n") + (ks ? "y" : "n");
                }
            }
            finally { h.Dispose(); }
        }
        o.AppendLine($"   {"bone",-24} A   B   C");
        foreach (var b in bones) o.AppendLine($"   {b,-24} {results["A"][b]} {results["B"][b]} {results["C"][b]}");
    }

    // ------------------------------------------------------------ the classes of every channel, measured with a changed pose at the bind

    static readonly Dictionary<string, Dictionary<string, string>> classes = new Dictionary<string, Dictionary<string, string>>();
    static readonly (int g, int c, int n, string name)[] ReconRows = { (19, 0, 0, "Idle_00"), (11, 5, 0, "dance_00"), (19, 1, 11, "Chair_A"), (15, 31, 1, "attack_01") };

    /// <summary>Per body bone three letters (rotation, position, scale): N nothing writes it; D the step writes the value it had at the bind; C the step writes
    /// another value (a clip's curve); G a shape-guard bone. All measured with every channel changed before the bind, as a card's modifier is.</summary>
    static void ReconClasses(StringBuilder o)
    {
        o.AppendLine("== 2. the classes of every body bone's channels, per row: every channel changed before the first bind, then changed again, one step");
        foreach (var (g, c, n, name) in ReconRows)
        {
            var h = ReconHost();
            try
            {
                var k = ReconLoad(h, femaleRel, "female");
                var r = h.Rt.FindChara(k);
                var all = AdvancedPoseResolver.Bones(r, "body");
                var guard = new HashSet<Transform>(r.Animation.ShapeBones);
                foreach (var x in all)
                {
                    x.Bone.localPosition += new Vector3(0.0123f, -0.0234f, 0.0345f);
                    x.Bone.localRotation *= Quaternion.Euler(13f, 17f, 19f);
                    x.Bone.localScale = Vector3.Scale(x.Bone.localScale, new Vector3(1.11f, 1.13f, 1.17f));
                }
                var v1 = all.Select(x => Local(x.Bone)).ToList();
                ReconRow(h, k, g, c, n);
                foreach (var x in all)
                {
                    x.Bone.localPosition += new Vector3(0.07f, 0.05f, -0.06f);
                    x.Bone.localRotation *= Quaternion.Euler(-23f, 11f, 29f);
                    x.Bone.localScale = Vector3.Scale(x.Bone.localScale, new Vector3(1.07f, 1.05f, 1.03f));
                }
                var v2 = all.Select(x => Local(x.Bone)).ToList();
                r.Motion.Step(0f);
                var map = classes[name] = new Dictionary<string, string>(StringComparer.Ordinal);
                var counts = new int[3, 4];
                for (int i = 0; i < all.Count; i++)
                {
                    var now = Local(all[i].Bone);
                    bool isGuard = guard.Contains(all[i].Bone);
                    char Cls(bool same2, bool same1) => isGuard ? 'G' : same2 ? 'N' : same1 ? 'D' : 'C';
                    var s = new string(new[]
                    {
                        Cls(Angle(now.r, v2[i].r) <= ShowDeg, Angle(now.r, v1[i].r) <= ShowDeg),
                        Cls(Dist(now.p, v2[i].p) <= ShowPos, Dist(now.p, v1[i].p) <= ShowPos),
                        Cls(Dist(now.s, v2[i].s) <= ShowPos, Dist(now.s, v1[i].s) <= ShowPos),
                    });
                    map[all[i].Bone.name] = s;
                    for (int ch = 0; ch < 3; ch++) counts[ch, "NDCG".IndexOf(s[ch])]++;
                }
                o.AppendLine($"   {name}: {all.Count} bones; rotation N {counts[0, 0]} D {counts[0, 1]} C {counts[0, 2]} G {counts[0, 3]}; position N {counts[1, 0]} D {counts[1, 1]} C {counts[1, 2]} G {counts[1, 3]}; scale N {counts[2, 0]} D {counts[2, 1]} C {counts[2, 2]} G {counts[2, 3]}");
            }
            finally { h.Dispose(); }
        }
        ReconUnchanged(o);
        var first = classes[ReconRows[0].name];
        foreach (var (_, _, _, name) in ReconRows.Skip(1))
        {
            var other = classes[name];
            var diff = first.Keys.Where(b => other.TryGetValue(b, out var s) && s != first[b]).ToList();
            o.AppendLine($"   {name} against {ReconRows[0].name}: {diff.Count} bones differ" + (diff.Count > 0 ? ": " + string.Join(", ", diff.Take(24).Select(b => b + " " + first[b] + "/" + other[b])) : ""));
        }
        foreach (var b in new[] { "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_LegUp00_L", "cf_J_LegUp00_R", "cf_J_Foot01_L", "cf_J_Foot01_R", "cf_J_Foot02_L", "cf_J_Foot02_R", "cf_J_Hand_Index01_L", "cf_J_Hand_Index01_R", "cf_J_Hand_Middle01_L", "cf_J_Hand_Ring01_L", "cf_J_Hand_Little01_L", "cf_J_Hand_Thumb01_L", "cf_J_Hand_Thumb01_R", "cf_J_Toes01_L", "cf_J_Kokan", "cf_J_Kosi01", "cf_J_Kosi02", "cf_J_Neck", "cf_J_Hips", "cf_J_Spine02" })
            o.AppendLine($"   {b,-24} {(first.TryGetValue(b, out var v) ? v : "-")}");
    }

    // Row 19/0/0 with the pose NOT changed before the bind: what the step does then, against the classes above; and the value the step writes.
    static void ReconUnchanged(StringBuilder o)
    {
        var map = classes[ReconRows[0].name];
        var h = ReconHost();
        try
        {
            var k = ReconLoad(h, femaleRel, "female");
            var r = h.Rt.FindChara(k);
            var all = AdvancedPoseResolver.Bones(r, "body");
            var atBind = all.Select(x => Local(x.Bone)).ToList();
            ReconRow(h, k);
            foreach (var x in all)
            {
                x.Bone.localPosition += new Vector3(0.07f, 0.05f, -0.06f);
                x.Bone.localRotation *= Quaternion.Euler(-23f, 11f, 29f);
                x.Bone.localScale = Vector3.Scale(x.Bone.localScale, new Vector3(1.07f, 1.05f, 1.03f));
            }
            var marked = all.Select(x => Local(x.Bone)).ToList();
            r.Motion.Step(0f);
            var cross = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var flipped = new List<string>();
            var after = new Dictionary<string, (Vector3 p, Quaternion r, Vector3 s)>();
            for (int i = 0; i < all.Count; i++)
            {
                var now = Local(all[i].Bone);
                string name = all[i].Bone.name;
                after[name] = now;
                if (!map.TryGetValue(name, out var cls)) continue;
                var kept = new[] { Angle(now.r, marked[i].r) <= ShowDeg, Dist(now.p, marked[i].p) <= ShowPos, Dist(now.s, marked[i].s) <= ShowPos };
                string[] chn = { "rotation", "position", "scale" };
                for (int ch = 0; ch < 3; ch++)
                {
                    if (cls[ch] == 'G') continue;
                    string key = chn[ch] + ": changed pose at the bind " + cls[ch] + ", unchanged pose at the bind " + (kept[ch] ? "not written" : "written");
                    cross.TryGetValue(key, out int n);
                    cross[key] = n + 1;
                    if (ch == 2 && cls[ch] == 'C' && kept[ch]) flipped.Add(name);
                }
            }
            o.AppendLine("   the same row with the pose NOT changed before the bind (a card without modifiers), counts per channel:");
            foreach (var kv in cross) o.AppendLine($"     {kv.Key}: {kv.Value}");
            o.AppendLine($"   scale written with a changed pose and not written with an unchanged one: {flipped.Count} bones, e.g. " + string.Join(", ", flipped.Take(12)));
            o.AppendLine("   the scale the step writes, exact, against the value at the bind (unchanged pose); L against R:");
            foreach (var b in new[] { "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_LegUp00_L", "cf_J_LegUp00_R", "cf_J_Foot01_L", "cf_J_Foot01_R", "cf_J_Hand_Index01_L", "cf_J_Hand_Index01_R" })
            {
                int i = all.FindIndex(x => x.Bone.name == b);
                var v = after[b].s; var g = atBind[i].s;
                bool written = Dist(after[b].s, marked[i].s) > ShowPos;
                o.AppendLine($"     {b,-22} bind ({g.x:R}, {g.y:R}, {g.z:R}); step {(written ? "wrote" : "left")} ({v.x:R}, {v.y:R}, {v.z:R})");
            }
        }
        finally { h.Dispose(); }
        // the curve's value on those bones, read with a changed pose at the bind
        var h2 = ReconHost();
        try
        {
            var k = ReconLoad(h2, femaleRel, "female");
            var r = h2.Rt.FindChara(k);
            var all = AdvancedPoseResolver.Bones(r, "body");
            foreach (var x in all) x.Bone.localScale = Vector3.Scale(x.Bone.localScale, new Vector3(1.11f, 1.13f, 1.17f));
            ReconRow(h2, k);
            r.Motion.Step(0f);
            o.AppendLine("   the same bones with a changed pose at the bind: the value the step writes (the clip's curve):");
            foreach (var b in new[] { "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_LegUp00_L", "cf_J_LegUp00_R", "cf_J_Foot01_L", "cf_J_Foot01_R", "cf_J_Hand_Index01_L", "cf_J_Hand_Index01_R" })
            {
                var v = Bone(r, b).localScale;
                o.AppendLine($"     {b,-22} ({v.x:R}, {v.y:R}, {v.z:R})");
            }
        }
        finally { h2.Dispose(); }
    }

    static bool RulePredicts(string bone, int channel)
    {
        // a bone outside the body list (head, accessories) has no clip: nothing rewrites it
        if (!classes.TryGetValue(ReconRows[0].name, out var m) || !m.TryGetValue(bone, out var s)) return true;
        return s[channel] != 'C';
    }

    // ------------------------------------------------------------ the cards

    sealed class ReconCard { public string File, Rel, Sex, Hash; public int Modifiers; public bool Mouth; public List<AbmxData.BoneModifier> List; }

    static List<ReconCard> ReconScan(StringBuilder o)
    {
        var cards = new List<ReconCard>();
        string user = Path.Combine(CardResolveTest.GameRoot, "UserData");
        int read = 0, refused = 0;
        using (var sha = SHA256.Create())
            foreach (var (dir, sex) in new[] { (targets.cards.female, "female"), (targets.cards.male, "male") })
            {
                var abs = Path.Combine(user, dir);
                if (!Directory.Exists(abs)) continue;
                foreach (var f in Directory.GetFiles(abs, "*.png", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                {
                    byte[] bytes;
                    CardExtData x;
                    try
                    {
                        bytes = File.ReadAllBytes(f);   // a card without a PNG signature (written without a thumbnail) counts too: S1 and S2 build it
                        x = CardExtensions.Decode(Card.Load(bytes, f));
                    }
                    catch (Exception) { refused++; continue; }
                    read++;
                    var list = x.Abmx?.Modifiers;
                    if (list == null || list.Count == 0) continue;
                    cards.Add(new ReconCard
                    {
                        File = f, Rel = f.Substring(abs.Length + 1).Replace('\\', '/'), Sex = sex, Modifiers = list.Count, List = list,
                        Hash = string.Concat(sha.ComputeHash(bytes).Take(6).Select(b => b.ToString("x2"))),
                        Mouth = list.Any(m => m.BoneName == "cf_J_MouthMove"),
                    });
                }
            }
        o.AppendLine($"cards read {read}, refused by the reader {refused}, with modifiers {cards.Count}, modifiers {cards.Sum(c => c.Modifiers)}");
        return cards;
    }

    static void ReconCards(StringBuilder o, List<ReconCard> cards, double minutes, string ah5)
    {
        o.AppendLine();
        o.AppendLine("== 3. every card with modifiers, built with its modifiers at load, row 19/0/0 at 0.5, speed 0, physics and look off, joint correction rows on; 5 frames; per modifier and channel: did the step keep what the modifiers' pass wrote");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var chan = new[] { "rotation", "position / length", "scale" };
        var withV = new int[3]; var notShown = new int[3]; var tiny = new int[3];
        var cardsNot = new HashSet<string>[3]; for (int i = 0; i < 3; i++) cardsNot[i] = new HashSet<string>();
        var perBone = new[] { new SortedDictionary<string, int>(StringComparer.Ordinal), new SortedDictionary<string, int>(StringComparer.Ordinal), new SortedDictionary<string, int>(StringComparer.Ordinal) };
        var ruleWrong = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int measured = 0, failed = 0, ruleAgree = 0, ruleAll = 0, unresolved = 0;
        var measuredSet = new HashSet<string>();
        var laterNot = new int[2, 3];
        var laterCards = new HashSet<string>[2, 3];
        for (int i = 0; i < 2; i++) for (int j = 0; j < 3; j++) laterCards[i, j] = new HashSet<string>();
        var h = ReconHost();
        try
        {
            foreach (var c in cards.OrderBy(x => x.File, StringComparer.Ordinal))
            {
                if (sw.Elapsed.TotalMinutes > minutes) break;
                try
                {
                    h.Call(new { cmd = "scene.new" });
                    var k = ReconLoad(h, c.Rel, c.Sex);
                    var r = k == null ? null : h.Rt.FindChara(k);
                    if (r == null || r.State != RealizationState.Built) { failed++; continue; }
                    ReconFrame(r, 2);
                    ReconRow(h, k);
                    ReconFrame(r, 5);
                    var mods = r.Context.Bones ? r.Context.Bones.Modifiers.Where(m => m.Source == "abmx").ToList() : new List<BoneModifierApplier.Modifier>();
                    unresolved += c.Modifiers - mods.Count;
                    r.Context.Bones?.Apply();
                    var w = mods.ToDictionary(m => m, m => Local(m.Target));
                    r.Motion.Step(Dt);
                    foreach (var m in mods)
                    {
                        var a = w[m]; var n = Local(m.Target);
                        var has = new[] { m.Rotation != Vector3.zero, m.Position != Vector3.zero || m.Length != 1f, m.Scale != Vector3.one };
                        var kept = new[] { Angle(a.r, n.r) <= ShowDeg, Dist(a.p, n.p) <= ShowPos, Dist(a.s, n.s) <= ShowPos };
                        var effect = new[] { Angle(Quaternion.Euler(m.Rotation), Quaternion.identity) > ShowDeg, (m.Position.magnitude + a.p.magnitude * Mathf.Abs(m.Length - 1f)) > ShowPos, Dist(m.Scale, Vector3.one) > ShowPos };
                        for (int ch = 0; ch < 3; ch++)
                        {
                            if (!has[ch]) continue;
                            withV[ch]++;
                            if (!effect[ch]) { tiny[ch]++; continue; }
                            ruleAll++;
                            bool predicted = m.Location >= 10 || RulePredicts(m.Bone, ch);
                            if (predicted == kept[ch]) ruleAgree++;
                            else
                            {
                                string key = m.Bone + " " + chan[ch] + (kept[ch] ? " kept, rule says rewritten" : " rewritten, rule says kept");
                                ruleWrong.TryGetValue(key, out int rw);
                                ruleWrong[key] = rw + 1;
                            }
                            if (kept[ch]) continue;
                            notShown[ch]++;
                            cardsNot[ch].Add(c.Hash);
                            perBone[ch].TryGetValue(m.Bone, out int pb);
                            perBone[ch][m.Bone] = pb + 1;
                        }
                    }
                    measured++;
                    measuredSet.Add(c.Hash);
                    // the same character after a row of another controller, and back on the first
                    foreach (var (stage, g0, c0, n0) in new[] { (0, 11, 5, 0), (1, 19, 0, 0) })
                    {
                        ReconRow(h, k, g0, c0, n0);
                        ReconFrame(r, 5);
                        r.Context.Bones?.Apply();
                        var w2 = mods.ToDictionary(m => m, m => Local(m.Target));
                        r.Motion.Step(Dt);
                        foreach (var m in mods)
                        {
                            var a = w2[m]; var n = Local(m.Target);
                            var has = new[] { m.Rotation != Vector3.zero, m.Position != Vector3.zero || m.Length != 1f, m.Scale != Vector3.one };
                            var kept = new[] { Angle(a.r, n.r) <= ShowDeg, Dist(a.p, n.p) <= ShowPos, Dist(a.s, n.s) <= ShowPos };
                            for (int ch = 0; ch < 3; ch++)
                                if (has[ch] && !kept[ch]) { laterNot[stage, ch]++; laterCards[stage, ch].Add(c.Hash); }
                        }
                    }
                    // H's recordings of this card, if any: the same values as real frames gave
                    if (ah5 != null && Directory.Exists(Path.Combine(ah5, c.Hash)))
                    {
                        var rec = JObject.Parse(File.ReadAllText(Path.Combine(ah5, c.Hash, "hs2next_on.json")));
                        var bonesRec = (rec.SelectToken("dump.bones") as JArray ?? rec["bones"] as JArray)?.OfType<JObject>().ToList();
                        double wp = 0, wr = 0, ws = 0; string where = ""; int nc = 0;
                        foreach (var m in mods)
                        {
                            var b = bonesRec?.FirstOrDefault(x => (string)x["name"] == m.Target.name);
                            if (b == null) continue;
                            nc++;
                            var lp = (JArray)b["lp"]; var lq = (JArray)b["lq"]; var ls = (JArray)b["ls"];
                            var nn = Local(m.Target);
                            double dp = Dist(nn.p, new Vector3((float)lp[0], (float)lp[1], (float)lp[2]));
                            double dr = Angle(nn.r, new Quaternion((float)lq[0], (float)lq[1], (float)lq[2], (float)lq[3]));
                            double ds = Dist(nn.s, new Vector3((float)ls[0], (float)ls[1], (float)ls[2]));
                            if (dp > wp || dr > wr || ds > ws) where = m.Bone;
                            wp = Math.Max(wp, dp); wr = Math.Max(wr, dr); ws = Math.Max(ws, ds);
                        }
                        o.AppendLine($"   card {c.Hash}: these frames against H's real frames (hs2next_on) on {nc} modifiers: worst pos {wp:G3} rot {wr:G3} deg scale {ws:G3} ({where})");
                    }
                }
                catch (Exception e) { failed++; Debug.LogWarning("recon card " + c.Hash + ": " + e.GetType().Name + ": " + e.Message); }
                if (measured % 50 == 0) Debug.Log($"recon: {measured} cards, {sw.Elapsed.TotalMinutes:F1} min");
            }
        }
        finally { h.Dispose(); }
        o.AppendLine($"   measured {measured} of {cards.Count} cards in {sw.Elapsed.TotalMinutes:F1} min, {failed} failed; modifiers whose bone is not on the character {unresolved}");
        for (int ch = 0; ch < 3; ch++)
            o.AppendLine($"   {chan[ch]}: {withV[ch]} modifiers with a value, {tiny[ch]} too small to tell, NOT SHOWN {notShown[ch]} on {cardsNot[ch].Count} cards");
        for (int ch = 0; ch < 3; ch++)
            o.AppendLine($"   {chan[ch]}, same cards, the row of another controller (dance_00) after it: not shown {laterNot[0, ch]} on {laterCards[0, ch].Count} cards; back on 19/0/0 (a second bind): not shown {laterNot[1, ch]} on {laterCards[1, ch].Count} cards");
        o.AppendLine($"   the rule against the measurement: {ruleAgree} of {ruleAll} channels agree");
        foreach (var kv in ruleWrong.OrderByDescending(x => x.Value).Take(30)) o.AppendLine($"     {kv.Key}: {kv.Value}");
        for (int ch = 0; ch < 3; ch++)
        {
            o.AppendLine($"   by bone, {chan[ch]}, not shown:");
            foreach (var kv in perBone[ch].OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal)) o.AppendLine($"     {kv.Key} {kv.Value}");
        }

        // the rule alone over the cards this budget did not reach, and over every row
        var rest = cards.Where(c => !measuredSet.Contains(c.Hash)).ToList();
        o.AppendLine($"   cards not measured: {rest.Count}");
        foreach (var (_, _, _, name) in ReconRows)
        {
            var m = classes[name];
            var not = new int[3]; var cs = new HashSet<string>[3]; for (int i = 0; i < 3; i++) cs[i] = new HashSet<string>();
            foreach (var c in cards)
                foreach (var b in c.List)
                {
                    if (b.AccessorySlot >= 0 || !m.TryGetValue(b.BoneName, out var s)) continue;
                    var d = b.Data;
                    if (d.Rotation != Vector3.zero && s[0] == 'C') { not[0]++; cs[0].Add(c.Hash); }
                    if ((d.Position != Vector3.zero || d.Length != 1f) && s[1] == 'C') { not[1]++; cs[1].Add(c.Hash); }
                    if (d.Scale != Vector3.one && s[2] == 'C') { not[2]++; cs[2].Add(c.Hash); }
                }
            o.AppendLine($"   inferred by the rule for {name}: rotation {not[0]} on {cs[0].Count} cards, position / length {not[1]} on {cs[1].Count} cards, scale {not[2]} on {cs[2].Count} cards");
        }
    }

    // ------------------------------------------------------------ the mouth width bone

    static void ReconMouth(StringBuilder o, List<ReconCard> cards)
    {
        o.AppendLine();
        o.AppendLine("== 4. cf_J_MouthMove with a card's scale modifier");
        var with = cards.Where(c => c.Mouth).ToList();
        o.AppendLine($"   cards with a modifier on it: {with.Count}; modifiers {with.Sum(c => c.List.Count(m => m.BoneName == "cf_J_MouthMove"))}: " + string.Join(", ", with.Select(c => c.Hash + " " + c.List.First(m => m.BoneName == "cf_J_MouthMove").Data.Scale.ToString("F4"))));
        foreach (var c in with.Take(2))
        {
            var h = ReconHost();
            try
            {
                var k = ReconLoad(h, c.Rel, c.Sex);
                var r = h.Rt.FindChara(k);
                var t = Bone(r, "cf_J_MouthMove");
                string S(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";
                var face = r.Motion.Get<ExpressionStage>();
                o.AppendLine($"   card {c.Hash}: at load {S(t.localScale)}; face width on: {face?.ApplyMouthWidth}, jitter on: {face?.Width.Enabled}, width {face?.Width.AdjustWidthScale}");
                ReconRow(h, k);
                int n = 0;
                foreach (var f in new[] { 1, 1, 1, 2, 5, 50, 540 })
                {
                    ReconFrame(r, f);
                    n += f;
                    o.AppendLine($"     frame {n}: {S(t.localScale)}");
                }
                ReconFrame(r, 600, 0f);
                o.AppendLine($"     600 more frames without time: {S(t.localScale)}");
                foreach (var ev in new[] { "a mouth pattern change", "the face width written again (a face shape edit asks for it)", "the motion's reset (a revive, a character thumbnail)", "the same reset" })
                {
                    if (ev.StartsWith("a mouth", StringComparison.Ordinal)) h.Call(new { cmd = "chara.face", key = k, mouth = 3 });
                    else if (ev.StartsWith("the face", StringComparison.Ordinal)) face?.InvalidateMouthWidth();
                    else r.Motion.ResetAll();
                    h.Rt.ApplyNow();
                    ReconFrame(r, 3);
                    o.AppendLine($"     after {ev}, 3 frames: {S(t.localScale)}");
                }
                if (face != null)
                {
                    float was = face.VoiceValue;
                    face.VoiceValue = 0.8f;
                    var v0 = t.localScale;
                    float w0 = face.Width.AdjustWidthScale;
                    int wrote = 0;
                    for (int i = 0; i < 60; i++)
                    {
                        float before = face.Width.AdjustWidthScale;
                        ReconFrame(r);
                        if (face.Width.AdjustWidthScale != before) wrote++;
                        if (i == 0 || i == 4 || i == 19 || i == 59) o.AppendLine($"     voice level 0.8 (speech), frame {i + 1}: {S(t.localScale)}, width {face.Width.AdjustWidthScale:F4}");
                    }
                    face.VoiceValue = was;
                    ReconFrame(r, 3);
                    o.AppendLine($"     voice over, 3 frames: {S(t.localScale)}; the width changed on {wrote} of 60 frames; jitter on {face.Width.Enabled}");
                }
                h.Call(new { cmd = "shape.set", key = k, faceIndex = 5, faceValue = 0.7f });
                h.Rt.ApplyNow();
                ReconFrame(r, 3);
                o.AppendLine($"     after a face slider edit, 3 frames: {S(t.localScale)}");
                using (var scope = StudioDefaultPoseScope.Enter(r)) { }
                h.Call(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false });
                h.Rt.ApplyNow();
                ReconFrame(r, 3);
                o.AppendLine($"     after a character thumbnail scope, 3 frames: {S(t.localScale)}");
            }
            finally { h.Dispose(); }
        }
    }

    // ------------------------------------------------------------ one card step by step, in the orders a user and H's recording take

    static void ReconTrace(StringBuilder o, List<ReconCard> cards)
    {
        o.AppendLine();
        o.AppendLine("== trace: one card with modifiers, scale of four bones along each order");
        var c = cards.FirstOrDefault(x => x.Hash == Arg("-card")) ?? cards[0];
        string[] bones = { "cf_J_Foot02_L", "cf_J_ArmUp00_R", "cf_J_Hand_Index01_L", "cf_J_Hand_Thumb01_L" };
        string V(CharaRealization r) => string.Join("  ", bones.Select(b => { var s = Bone(r, b).localScale; return $"{s.x:F4}"; }));
        foreach (var variant in new[] { "no frames before the row", "2 frames before the row", "H's order: the T-pose row at speed 0, frames, then the row", "the T-pose row at speed 0, no frames, then the row" })
        {
            var h = ReconHost();
            try
            {
                o.AppendLine("   " + variant + "  (" + string.Join(", ", bones) + ")");
                var k = Key(h.Call(new { cmd = "chara.load", path = c.Rel, sex = c.Sex }));
                h.Call(new { cmd = "chara.motion", key = k, autoStep = false });
                h.Rt.ApplyNow();
                var r = h.Rt.FindChara(k);
                var c0 = r.Animation.BaseController;
                o.AppendLine($"     after the load: {V(r)}; controller {(c0 ? "bound" : "none")}; model row {(h.Session.Model.Find(k).State as HS2Next.Save.CharaNodeState)?.Anime.Ref?.No.ToString() ?? "none"}");
                if (variant.StartsWith("H's", StringComparison.Ordinal) || variant.StartsWith("the T-pose", StringComparison.Ordinal))
                {
                    h.Call(new { cmd = "chara.anime", key = k, group = 0, category = 0, no = 0, time = 0f, speed = 0f });
                    h.Rt.ApplyNow();
                    o.AppendLine($"     after the T-pose row: {V(r)}; same controller {r.Animation.BaseController == c0}");
                }
                h.Call(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false });
                h.Rt.ApplyNow();
                if (variant.StartsWith("2", StringComparison.Ordinal)) ReconFrame(r, 2);
                if (variant.StartsWith("H's", StringComparison.Ordinal)) ReconFrame(r, 25);
                o.AppendLine($"     before the row: {V(r)}");
                ReconRow(h, k);
                o.AppendLine($"     after the row's bind: {V(r)}; another controller {r.Animation.BaseController != c0}");
                ReconFrame(r, 25);
                o.AppendLine($"     after 25 frames: {V(r)}");
            }
            finally { h.Dispose(); }
        }
    }

    // ------------------------------------------------------------ the entry

    public static void Recon()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perGroup.Clear(); measurements.Clear();
        Directory.CreateDirectory(OutDir);
        var head = new StringBuilder();
        services = SceneRuntimeTest.NewServices(head);
        LoadTargets();
        string part = Arg("-part") ?? "flip+classes+mouth+cards";
        double minutes = double.TryParse(Arg("-minutes"), out var mv) ? mv : 15.0;
        string ah5 = Arg("-ah5");
        var o = new StringBuilder();
        string path = Path.Combine(OutDir, "recon.txt");
        void Flush() => File.WriteAllText(path, o.ToString(), new UTF8Encoding(false));
        var cards = ReconScan(o);
        Flush();
        if (part.Contains("flip")) { ReconFlip(o); Flush(); }
        if (part.Contains("trace")) { ReconTrace(o, cards); Flush(); return; }
        ReconClasses(o);
        Flush();
        if (part.Contains("mouth")) { ReconMouth(o, cards); Flush(); }
        if (part.Contains("cards"))
        {
            int start = o.Length;
            ReconCards(o, cards, minutes, ah5);
            Flush();
            File.WriteAllText(Path.Combine(OutDir, "ao3_census.txt"),
                "SUMMARY: the card modifiers HS2Next does not show during play, measured per card (AdvancedPoseTest.Recon). This replaces the first census of this file, which classed channels with a run-time sentinel on a card without modifiers and so answered another question.\n" + o.ToString(start, o.Length - start),
                new UTF8Encoding(false));
        }
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log("ADVANCED POSE RECON DONE");
    }
}
