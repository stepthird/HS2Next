// RagdollTest, shots and effects.
//   headless, a part of RagdollTest.Run
//     RM7  the random numbers: the same per seed and index; no other source of numbers or of time in the ragdoll's runtime folder.
//     RD1  the bullet's arithmetic by hand, then on a live body: a chain of parts, the cap, once per rigid body, the stop at 8 %, an outer face passed, no world.
//     RD2  weakness: the shares of a hit, the limit of 1, the regain. RD3  stagger: a hit adds, the head by 1.5, the decay, the knock at the bar.
//     RD4  kill: the first body limp for 600 frames, the second not.
//     RD8  the caps of droplets and splats. RD9  blood off. RD11  effects stop at the pause and step with a step.
//   graphics, RagdollTest.RunRender (row RagdollTest.render), report Spike/out/ragdoll/ragdoll_render_report.txt
//     RD7  splats on the ground, their outline. RD10  a shot's stain beside the stains there are, its limits, no history, no target on a member.
//     RD12 a capture and a puddle mirror show the effects.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Wet;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class RagdollTest
{
    static void Headless_Shots()
    {
        Group("RM7", "numbers and time", Rm7);
        Group("RD1", "the bullet", () => { ShotArithmetic(); Rd1(); });
        Group("RD2", "weakness and stagger (RD2, RD3)", Rd2);
        Group("RD4", "kill", Rd4);
        Group("RD8", "the caps", Rd8);
        Group("RD9", "blood off", Rd9);
        Group("RD11", "the clock of effects", Rd11);
    }

    /// <summary>BR-Chan standing still on a floor at 0, with a body; the host goes after <paramref name="a"/>.</summary>
    static void WithBody(string what, Action<Host, string, RagdollBody> a)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", what);
            Idle(h, k);
            FixedFloor(h);
            var b = Body(h, k, what);
            if (!Check(what + ": a body stands", b != null)) return;
            h.Frames(5);
            a(h, k, b);
        }
        finally { h.Dispose(); }
    }

    /// <summary>The ray from above the head through the pelvis, from inside the room.</summary>
    static (Vector3 origin, Vector3 dir) DownTheBody(RagdollBody b)
    {
        var head = b.Part(RagdollParts.Head).Rb.worldCenterOfMass;
        var pelvis = b.Part(RagdollParts.Pelvis).Rb.worldCenterOfMass;
        var dir = (pelvis - head).normalized;
        return (head - dir * 5f, dir);
    }

    // ------------------------------------------------------------ RM7

    static void Rm7()
    {
        Check("Rng is the same per seed and index, and moves with both", WetMath.Rng(3, 11) == WetMath.Rng(3, 11) && WetMath.Rng(3, 11) != WetMath.Rng(4, 11) && WetMath.Rng(3, 11) != WetMath.Rng(3, 12));
        string dir = Path.Combine(Application.dataPath, "HS2Next", "Runtime", "Scene", "Runtime", "Ragdoll");
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        var banned = new Regex(@"\bSystem\.Random\b|\bnew Random\b|\bUnityEngine\.Random\b|\bRandom\.(value|Range|insideUnit|onUnit|rotation|Init)|\bTime\.(deltaTime|time|unscaledTime|unscaledDeltaTime|fixedDeltaTime)\b|realtimeSinceStartup");
        var uses = new Regex(@"\bWetMath\.Rng\(");
        int found = 0, rng = 0;
        var where = new List<string>();
        foreach (var f in files)
        {
            int line = 0;
            foreach (var l in File.ReadAllLines(f))
            {
                line++;
                if (l.TrimStart().StartsWith("//")) continue;
                if (banned.IsMatch(l)) { found++; where.Add(Path.GetFileName(f) + ":" + line); }
                if (uses.IsMatch(l)) rng++;
            }
        }
        Check($"no other source of numbers or time in the {files.Length} files of the runtime folder, and Rng is what they use", files.Length >= 10 && found == 0 && rng > 0, $"{found} hits {string.Join(", ", where)}; Rng on {rng} lines");
    }

    // ------------------------------------------------------------ RD1

    static void ShotArithmetic()
    {
        var hits = new ShotHit[8];
        var blows = new ShotBlow[8];
        ShotHit Hit(float d, float mass, int i) => new ShotHit { Kind = ShotHitKind.Part, Distance = d, Mass = mass, First = i };
        const float transfer = .18f, cap = 40f, momentum = 181f;

        hits[0] = Hit(10f, 5f, 0); hits[1] = Hit(12f, 5f, 1); hits[2] = Hit(14f, 5f, 2);
        int n = RagdollShots.Walk(hits, 3, momentum, transfer, cap, blows, out float end, out int wall);
        double left = momentum, worst = 0;
        for (int i = 0; i < n; i++)
        {
            double j = left * transfer;
            worst = Math.Max(worst, Math.Abs(j - blows[i].J));
            left -= j;
        }
        Check("a chain on three parts: each takes 0.18 of what is left", n == 3 && worst < 1e-3 && blows[0].Applied == blows[0].J && float.IsInfinity(end) && wall < 0,
            $"J {blows[0].J:F3}, {blows[1].J:F3}, {blows[2].J:F3}, worst {worst:E2}");

        hits[0] = Hit(10f, .35f, 0);
        n = RagdollShots.Walk(hits, 1, momentum, transfer, cap, blows, out end, out wall);
        Check("the cap: 40 x the part's mass", n == 1 && Mathf.Abs(blows[0].Applied - 14f) < 1e-4f && blows[0].J > 32f, $"J {blows[0].J:F3}, applied {blows[0].Applied:F3}");

        hits[0] = Hit(10f, 5f, 0); hits[1] = Hit(10.5f, 5f, 0); hits[2] = Hit(12f, 5f, 2);
        n = RagdollShots.Walk(hits, 3, momentum, transfer, cap, blows, out end, out wall);
        Check("once per rigid body: the second hit of a body takes nothing", n == 2 && blows[1].Hit == 2 && Mathf.Abs(blows[1].J - (momentum - blows[0].J) * transfer) < 1e-3f, $"{n} blows");

        for (int i = 0; i < 4; i++) hits[i] = Hit(10f + i, 5f, i);
        n = RagdollShots.Walk(hits, 4, 100f, .6f, cap, blows, out end, out wall);
        Check("under 8 % of the momentum the bullet stops 1.5 past the part", n == 3 && Mathf.Abs(end - 13.5f) < 1e-4f && wall < 0, $"{n} blows, end {end:F3}, left {100f * .4f * .4f * .4f:F2}");

        hits[0] = new ShotHit { Kind = ShotHitKind.Static, Distance = 3f, Outer = true, First = 0 };
        hits[1] = new ShotHit { Kind = ShotHitKind.None, Distance = 4f, First = 1 };
        hits[2] = new ShotHit { Kind = ShotHitKind.Prop, Distance = 5f, Mass = 2f, First = 2 };
        hits[3] = new ShotHit { Kind = ShotHitKind.Static, Distance = 9f, First = 3 };
        hits[4] = Hit(11f, 5f, 4);
        n = RagdollShots.Walk(hits, 5, momentum, transfer, cap, blows, out end, out wall);
        Check("an outer face and a pusher let it pass, a prop takes 0.5, an inner face ends it before the part behind", n == 1 && Mathf.Abs(blows[0].J - 90.5f) < 1e-3f && Mathf.Abs(blows[0].Applied - 80f) < 1e-3f && end == 9f && wall == 3,
            $"{n} blows, J {blows[0].J:F2}, applied {blows[0].Applied:F2}, end {end}, wall {wall}");
    }

    static void Rd1() => WithBody("RD1", (h, k, body) =>
    {
        var shots = RagdollShots.For(h.Rt);
        var (origin, dir) = DownTheBody(body);
        var speed = body.Parts.ToDictionary(p => p, p => Vector3.Dot(p.Rb.linearVelocity, dir));
        var r = shots.Fire(origin, dir, origin, 181f, false);
        double left = 181, worst = 0;
        var keys = new List<string>();
        foreach (var b in r.Blows)
        {
            double j = left * .18f;
            worst = Math.Max(worst, Math.Abs(j - b.J));
            worst = Math.Max(worst, Math.Abs(Math.Min(j, 40.0 * b.Part.Rb.mass) - b.Applied));
            left -= j;
            keys.Add(b.Part.Key);
        }
        var parts = r.Blows.Select(b => b.Part).ToList();
        Check("live: the ray from the head to the pelvis meets at least 3 parts, each by the arithmetic, and ends on the floor with dust", parts.Count >= 3 && worst < 1e-3 && r.OnWall && r.Hits == 1 && shots.Fx.Dust == RagdollFx.DustPerWall,
            $"{string.Join(", ", keys)}; worst {worst:E2}, end {r.End}, dust {shots.Fx.Dust}");
        h.Frames(1, 1f / 240f);
        var gains = parts.Select(p => Vector3.Dot(p.Rb.linearVelocity, dir) - speed[p]).ToList();
        Check("live: each part hit took an impulse along the bullet", gains.Count >= 3 && gains.All(g => g > 0f), string.Join(", ", gains.Select(g => g.ToString("F2"))));

        // a bullet from outside the room goes in through the outer face of a wall
        var w = h.Part.World;
        var chest = body.Part(RagdollParts.Chest).Rb.worldCenterOfMass;
        var outside = new Vector3(w.RoomCenter.x + w.RoomSize.x * .5f + 30f, chest.y, chest.z);
        var q = shots.Fire(outside, Vector3.left, outside, 181f, false);
        Check("live: a shot from outside the room passes the outer face and reaches the body", q.Hits == 1 && q.OnWall, $"{q.Hits} bodies, {q.Blows.Count} parts, end {q.End}");

        // a share that runs the bullet out inside the body
        World(h, b => b.Values.Transfer = .6f);
        (origin, dir) = DownTheBody(body);
        var t = shots.Fire(origin, dir, origin, 100f, false);
        Check("live: a bullet under 8 % stops inside the body, not on the floor", t.Blows.Count >= 3 && t.Stopped && !t.OnWall, $"{t.Blows.Count} parts, end {t.End}, J {string.Join(", ", t.Blows.Select(b => b.J.ToString("F2")))}");

        // no world: a tracer and a flash, nothing hit, none made
        h.Part.Release(k, false, ExecuteOptions.NotRecorded);
        h.Part.CloseNow();
        shots.Clear();
        bool none = h.Part.World == null;
        var u = shots.Fire(Vector3.zero, Vector3.forward, Vector3.zero, 181f, false);
        Check("no world: a shot draws its tracer 600 units long and a flash, hits nothing and makes no world", none && h.Part.World == null && u.Hits == 0 && !u.Stopped && (u.End - Vector3.forward * 600f).magnitude < 1e-3f && shots.Fx.Tracers == 1 && shots.Fx.Flashes == 1,
            $"before {none}, after {h.Part.World == null}, tracers {shots.Fx.Tracers}, flashes {shots.Fx.Flashes}");
        h.Frames(10);
        Check("no world: they end by the scene clock all the same", shots.Fx.Tracers == 0 && shots.Fx.Flashes == 0, $"tracers {shots.Fx.Tracers}, flashes {shots.Fx.Flashes}");
    });

    // ------------------------------------------------------------ RD2, RD3

    static void Rd2() => WithBody("RD2", (h, k, body) =>
    {
        var parts = body.Parts;
        var chest = body.Part(RagdollParts.Chest);
        var hand = body.Part("hand_L");
        RagdollShots.Weaken(body, chest, 4f, 20f);
        double worst = 0;
        int neighbours = 0;
        foreach (var p in parts)
        {
            double want = p == chest ? .2 : p.Parent == chest || chest.Parent == p ? .1 : .03;
            if (want == .1) neighbours++;
            worst = Math.Max(worst, Math.Abs(p.Weakness - want));
        }
        Check("RD2 amount J / 20 on the part, half on its parent and children, 0.15 on the rest", worst < 1e-6 && neighbours == 4, $"worst {worst:E2}, {neighbours} neighbours");
        RagdollShots.Regain(body, .05f, 1.93f);
        Check("RD2 regained at 1.93 a second", Mathf.Abs(chest.Weakness - (.2f - .0965f)) < 1e-6f && hand.Weakness == 0f, $"chest {chest.Weakness:F4}, hand {hand.Weakness:F4}");
        RagdollShots.Weaken(body, chest, 1000f, 20f);
        Check("RD2 at most 1", parts.All(p => p.Weakness <= 1f) && chest.Weakness == 1f, $"chest {chest.Weakness}, least {parts.Min(p => p.Weakness):F3}");
        RagdollShots.Regain(body, 10f, 1.93f);
        Check("RD2 every part back to 0", parts.All(p => p.Weakness == 0f));
        body.Stagger = 0f;

        // a live hit: weakness and stagger follow the blows
        var (origin, dir) = DownTheBody(body);
        var r = RagdollShots.For(h.Rt).Fire(origin, dir, origin, 100f, false);
        var want2 = new double[parts.Count];
        double stagger = 0;
        foreach (var b in r.Blows)
        {
            double amount = b.J / 20.0;
            foreach (var p in parts)
            {
                double share = p == b.Part ? 1 : p.Parent == b.Part || b.Part.Parent == p ? .5 : .15;
                want2[p.Index] = Math.Min(1, want2[p.Index] + amount * share);
            }
            stagger += b.J * (b.Part.Key == RagdollParts.Head ? 1.5 : 1);
        }
        double off = parts.Max(p => Math.Abs(p.Weakness - want2[p.Index]));
        Check("RD2 a live hit: the weakness of every part by the blows", r.Blows.Count >= 3 && off < 1e-5, $"{r.Blows.Count} blows, worst {off:E2}, largest {parts.Max(p => p.Weakness):F3}");
        bool headHit = r.Blows.Any(b => b.Part.Key == RagdollParts.Head);
        Check("RD3 stagger adds the J, the head by 1.5", headHit && Math.Abs(body.Stagger - stagger) < 1e-3, $"{body.Stagger:F3} against {stagger:F3}");
        float s0 = body.Stagger, w0 = parts.Max(p => p.Weakness);
        h.Frames(30);
        double decayed = s0 * Math.Exp(-.5);
        Check("RD3 stagger decays by e^-dt, weakness regains, by the scene clock", Math.Abs(body.Stagger - decayed) / decayed < 2e-3 && Mathf.Abs(parts.Max(p => p.Weakness) - Mathf.Max(0f, w0 - .965f)) < 1e-3f,
            $"{body.Stagger:F3} against {decayed:F3}; weakness {w0:F3} -> {parts.Max(p => p.Weakness):F3}");

        // the bar
        Check("RD3 under the bar the body stays up", body.State == RagdollState.Up, body.State.ToString());
        (origin, dir) = DownTheBody(body);
        var big = RagdollShots.For(h.Rt).Fire(origin, dir, origin, 3400f, false);
        Check("RD3 at the bar in state up: down", body.State == RagdollState.Down, $"state {body.State}, first J {big.Blows.FirstOrDefault().J:F1}");
        float after = big.Blows.Skip(1).Sum(b => b.J * (b.Part.Key == RagdollParts.Head ? 1.5f : 1f));
        Check("RD3 the stagger starts from 0 there", Mathf.Abs(body.Stagger - after) < 1e-2f, $"{body.Stagger:F2} against {after:F2} of the later parts");
    });

    // ------------------------------------------------------------ RD4

    static void Rd4()
    {
        var h = NewHost();
        try
        {
            string first = LoadChara(h, femaleRel, "female", "RD4 BR-Chan");
            string second = LoadChara(h, maleRel, "male", "RD4 HERO107");
            Idle(h, first);
            Idle(h, second);
            Place(h, second, new Vector3(0f, 0f, 12f));
            FixedFloor(h);
            var a = Body(h, first, "RD4");
            var b = Body(h, second, "RD4");
            if (!Check("two bodies stand", a != null && b != null && a != b)) return;
            h.Frames(5);
            var from = a.Part(RagdollParts.Chest).Rb.worldCenterOfMass;
            var to = b.Part(RagdollParts.Chest).Rb.worldCenterOfMass;
            var dir = (to - from).normalized;
            var origin = from - dir * 6f;
            var r = RagdollShots.For(h.Rt).Fire(origin, dir, origin, 181f, true);
            Check("the bullet goes through both, in order", r.Hits == 2 && r.Bodies[0] == a && r.Bodies[1] == b, $"{r.Hits} bodies, {r.Blows.Count} parts");
            Check("the first body hit goes limp, the second not", a.State == RagdollState.Limp && b.State != RagdollState.Limp, $"{a.State}, {b.State}");
            int limp = 0, other = 0;
            for (int i = 0; i < 600; i++)
            {
                h.Frames(1);
                if (a.State == RagdollState.Limp) limp++;
                if (b.State != RagdollState.Limp) other++;
            }
            Check("limp stays for 600 frames, the second body never goes limp", limp == 600 && other == 600, $"{limp}, {other}; the second is {b.State}");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ RD8

    static void Rd8()
    {
        var fx = new RagdollFx(null);
        try
        {
            for (int i = 0; i < 200; i++)
            {
                fx.BeginShot(0, i);
                fx.Blood(new Vector3(0f, 12f, 0f), Vector3.forward, 300f, true);
            }
            Check("3,000 droplets alive at most, reached", fx.Droplets == RagdollFx.MaxDroplets && fx.DropletsLost > 0, $"{fx.Droplets} alive, {fx.DropletsLost} lost");
            Func<float, float, float> floor = (x, z) => 0f;
            for (int i = 0; i < 240; i++) fx.Step(Dt, 98.1f, floor);
            Check("400 splats at most, the oldest goes, reached", fx.Splats == RagdollFx.MaxSplats && fx.SplatsPushed > 0, $"{fx.Splats} splats, {fx.SplatsPushed} pushed out, droplets left {fx.Droplets}");
        }
        finally { fx.Dispose(); }
    }

    // ------------------------------------------------------------ RD9

    static void Rd9()
    {
        bool was = RagdollFx.ShowBlood;
        try
        {
            WithBody("RD9", (h, k, body) =>
            {
                var shots = RagdollShots.For(h.Rt);
                CharaWet Wet() => ((CharaNodeState)h.Rt.Model.Find(k).State).Wet;
                var block = Wet().Clone();
                WetEdits.Add(block, WetEdits.NewStain(block, "kept", WetPreset.Blood));
                h.Session.Commands.Execute(new WetEditCommand(k, block), ExecuteOptions.NotRecorded);
                h.Rt.ApplyNow();
                string before = Wet().ToJson();

                var (origin, dir) = DownTheBody(body);
                RagdollFx.ShowBlood = true;
                shots.Fire(origin, dir, origin, 181f, false);
                int with = shots.Fx.Droplets;
                shots.Clear();

                RagdollFx.ShowBlood = false;
                var r = shots.Fire(origin, dir, origin, 181f, false, true);
                var fx = shots.Fx;
                int droplets = fx.Droplets, dust = fx.Dust, tracers = fx.Tracers, flashes = fx.Flashes;
                bool stained = r.Stained, blows = r.Blows.Count >= 3;
                h.Frames(60);
                Check("blood off: no droplet, no splat; dust, tracer and flash stay", blows && droplets == 0 && fx.Splats == 0 && dust == RagdollFx.DustPerWall && tracers == 1 && flashes == 1, $"droplets {droplets}, splats {fx.Splats}, dust {dust}, tracers {tracers}, flashes {flashes}");
                Check("present: on, the same shot makes at least 6 droplets", with >= 6, $"{with} droplets");
                string after = Wet().ToJson();
                Check("a saved stain stays, and a shot adds none with blood off", after == before && !stained && Wet().Stains.Count == 1, $"{Wet().Stains.Count} stains, {after.Length} characters of the block");
            });
        }
        finally { RagdollFx.ShowBlood = was; }
    }

    // ------------------------------------------------------------ RD11

    static void Rd11() => WithBody("RD11", (h, k, body) =>
    {
        var shots = RagdollShots.For(h.Rt);
        var (origin, dir) = DownTheBody(body);
        shots.Fire(origin, dir, origin, 181f, false);
        var fx = shots.Fx;
        var a = fx.DropletAt(0);
        h.Frames(1);
        var b = fx.DropletAt(0);
        Check("present: they move while the clock runs", (b - a).magnitude > 1e-3f, $"{(b - a).magnitude:F4}");
        h.Rt.SetPaused(true);
        h.Frames(30);
        var c = fx.DropletAt(0);
        Check("they stand at the pause", (c - b).sqrMagnitude == 0f && fx.Droplets > 0, $"{(c - b).magnitude:F6}, {fx.Droplets} droplets");
        h.Rt.Step(1, Dt);
        var d = fx.DropletAt(0);
        Check("they step with a step", (d - c).magnitude > 1e-3f, $"{(d - c).magnitude:F4}");
        h.Rt.SetPaused(false);
    });

    // ------------------------------------------------------------ the rendering row

    public static void RunRender()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        string report = Arg("-report") ?? Path.Combine(OutDir, "ragdoll_render_report.txt");
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            LoadTargets();
            rep.AppendLine($"RagdollTest.render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            MosaicTest.WithRendering(mh =>
            {
                using (MosaicTest.PreviewWorlds())
                {
                    Group("RD7", "splats", () => { Rd7(); Rd7Step(); });
                    Group("RD10", "a shot's stain", Rd10);
                    Group("RD12", "captures and mirrors show the effects", () => Rd12(mh));
                    Group("RD8t", "what 3,000 droplets and 400 splats cost in the Editor", Rd8Time);
                }
            });
        }
        catch (Exception e) { current = "render"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        End(report, sw, "RagdollTest.RunRender: RD7, RD10, RD12 (images in memory only)");
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"RAGDOLL RENDER TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    static void Rd7() => WithBody("RD7", (h, k, body) =>
    {
        var shots = RagdollShots.For(h.Rt);
        var (origin, dir) = DownTheBody(body);
        for (int i = 0; i < 12; i++) shots.Fire(origin, dir, origin, 900f, false);
        h.Frames(120);
        var fx = shots.Fx;
        var outline = new Vector3[RagdollFx.SplatRim + 1];
        int flat = 0, onGround = 0, irregular = 0, read = 0, sats = 0;
        double leastSpread = 1;
        var w = h.Part.World;
        for (int i = 0; i < fx.Splats; i++)
        {
            if (!fx.SplatOutline(i, outline)) continue;
            read++;
            float y = RagdollShots.GroundAt(w, outline[0].x, outline[0].z) + RagdollFx.SplatLift;
            if (Mathf.Abs(outline[0].y - y) < 1e-4f) onGround++;
            if (outline.All(p => Mathf.Abs(p.y - y) < 1e-4f)) flat++;
            double lo = double.MaxValue, hi = 0;
            for (int n = 1; n <= RagdollFx.SplatRim; n++)
            {
                double r = (outline[n] - outline[0]).magnitude;
                lo = Math.Min(lo, r);
                hi = Math.Max(hi, r);
            }
            double spread = (hi - lo) / hi;
            leastSpread = Math.Min(leastSpread, spread);
            if (spread >= .1) irregular++;
            int s = fx.SatellitesOf(i);
            if (s >= 2 && s <= RagdollFx.MaxSatellites) sats++;
        }
        Check("at least 20 splats, each at the ground + 0.02", read >= 20 && onGround == read && flat == read, $"{read} splats, {onGround} on the ground, {flat} flat, floor {w.FloorY:F3}");
        Check($"each has {RagdollFx.SplatRim} rim vertices with radii differing by at least 10 %, and 2 to 5 satellites", irregular == read && sats == read && read > 0, $"least spread {leastSpread:F3}, {sats} with satellites");
    });

    // a ground with a step, from a stand-in map: a splat lies at the height under it, not at the room's floor
    static void Rd7Step()
    {
        var door = RagdollFloor.QueryOf;
        var map = new StandInMap { HeightOf = (x, z) => x < 0f ? 1f : 2.5f };
        RagdollFloor.QueryOf = _ => map;
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "RD7 step");
            Idle(h, k);
            Place(h, k, new Vector3(0f, 3f, 0f));
            h.Frames(2);
            var body = Body(h, k, "RD7 step");
            if (!Check("a body over a patch", body != null && h.Part.World.Floor.PatchOf(body) != null)) return;
            h.Frames(5);
            var shots = RagdollShots.For(h.Rt);
            var (origin, dir) = DownTheBody(body);
            for (int i = 0; i < 12; i++) shots.Fire(origin, dir, origin, 900f, false);
            h.Frames(120);
            var fx = shots.Fx;
            var w = h.Part.World;
            int low = 0, high = 0, on = 0;
            for (int i = 0; i < fx.Splats; i++)
            {
                var p = fx.SplatAt(i);
                if (Mathf.Abs(p.y - (w.Floor.HeightAt(p.x, p.z) + RagdollFx.SplatLift)) < 1e-4f) on++;
                if (p.y < 1.1f) low++;
                if (p.y > 2.4f) high++;
            }
            Check("on a ground with a step each splat lies at HeightAt + 0.02, on both sides of the step", fx.Splats >= 20 && on == fx.Splats && low >= 5 && high >= 5, $"{fx.Splats} splats, {on} on the ground, {low} on the low side, {high} on the high side, room's floor {w.FloorY:F3}");
        }
        finally
        {
            h.Dispose();
            RagdollFloor.QueryOf = door;
        }
    }

    static void Rd10() => WithBody("RD10", (h, k, body) =>
    {
        var shots = RagdollShots.For(h.Rt);
        var wet = h.Rt.GetPart<WetPart>();
        CharaWet Wet() => ((CharaNodeState)h.Rt.Model.Find(k).State).Wet;
        var chest = body.Part(RagdollParts.Chest).Rb.worldCenterOfMass;
        var front = h.Rt.FindChara(k).Root.transform.forward;
        var from = chest + front * 8f;
        var first = wet.AttachRay(k, new Ray(from, -front), WetPreset.Water, "first", ExecuteOptions.NotRecorded);
        h.Rt.ApplyNow();
        if (!Check("a stain stands first", first.Ok, first.Message ?? "")) return;
        int history = h.Session.Commands.UndoList.Count;
        var r = shots.Fire(from, -front, from, 20f, false, true);
        bool stained = r.Stained;
        h.Rt.ApplyNow();
        var names = Wet().Stains.Select(x => x.Name).ToList();
        var added = Wet().FindByName("shot1");
        Check("a shot adds a stain beside the one there is, started at once", stained && names.Count == 2 && names.Contains("first") && added != null && added.Running, string.Join(", ", names));
        var rig = wet.RigOf(k);
        int maps = rig?.Runs.Sum(x => x.Live.Count) ?? 0;
        Check("present: the stain's maps exist", maps > 0, $"{maps} maps");
        Check("no history entry", h.Session.Commands.UndoList.Count == history, $"{h.Session.Commands.UndoList.Count} against {history}");
        var quick = shots.Fire(from, -front, from, 20f, false, true);
        bool skippedQuick = quick.StainSkipped, stainedQuick = quick.Stained;
        int skipped = shots.StainsSkipped;
        Check("at most one per character per 0.5 s of the world: the next is skipped and counted", !stainedQuick && skippedQuick && skipped == 1 && Wet().Stains.Count == 2, $"skipped {skipped}");
        int made = 1;
        for (int i = 0; i < 6; i++)
        {
            h.Frames(31);
            if (shots.Fire(from, -front, from, 20f, false, true).Stained) made++;
            h.Rt.ApplyNow();
        }
        int mine = Wet().Stains.Count(x => x.Name.StartsWith("shot"));
        Check("at most 4 per character, the rest are skipped and counted", made == 4 && mine == 4 && shots.StainsSkipped == 4, $"{made} made, {mine} in the block, {shots.StainsSkipped} skipped");
        rig = wet.RigOf(k);
        var members = MosaicSet.StateOf(h.Rt.FindChara(k).Context).Members.Select(m => m.Renderer).Where(x => x).ToList();
        int onMember = rig.Slots.Values.Count(sl => members.Contains(sl.Target.Renderer) || MosaicSet.IsMember(sl.Target.Renderer));
        Check("no target on a member", members.Count > 0 && rig.Slots.Count > 0 && onMember == 0, $"{members.Count} members, {rig.Slots.Count} slots, {onMember} on a member");
    });

    // a reading for the Editor only: the row of time is the player's
    static void Rd8Time()
    {
        var holder = new GameObject("rd8_time");
        var fx = new RagdollFx(holder.transform);
        try
        {
            Func<float, float, float> floor = (x, z) => 0f, none = (x, z) => -1e6f;
            int shot = 0;
            for (int i = 0; i < 60; i++)
            {
                fx.BeginShot(0, shot++);
                fx.Blood(new Vector3(0f, 2f, 0f), Vector3.forward, 300f, true);
            }
            for (int i = 0; i < 90; i++) fx.Step(Dt, 98.1f, floor);
            var step = new List<double>();
            var mesh = new List<double>();
            int least = int.MaxValue;
            for (int n = 0; n < 300; n++)
            {
                while (fx.Droplets < RagdollFx.MaxDroplets - 60)
                {
                    fx.BeginShot(0, shot++);
                    fx.Blood(new Vector3(0f, 500f, 0f), Vector3.forward, 600f, true);
                }
                least = Math.Min(least, fx.Droplets);
                long t0 = Stopwatch.GetTimestamp();
                fx.Step(Dt, 98.1f, none);
                long t1 = Stopwatch.GetTimestamp();
                fx.Draw();
                long t2 = Stopwatch.GetTimestamp();
                step.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
                mesh.Add((t2 - t1) * 1000.0 / Stopwatch.Frequency);
            }
            step.Sort();
            mesh.Sort();
            var both = step.Zip(mesh, (x, y) => x + y).OrderBy(x => x).ToList();
            Measure($"RD8t Editor: at least {least} droplets, {fx.Splats} splats: step median {step[150]:F4} ms, mesh median {mesh[150]:F4} ms, both p95 {both[285]:F4} ms (the bar of RW8 is the player's)");
            Check("the reading ran on the caps", least >= RagdollFx.MaxDroplets - 60 && fx.Splats >= RagdollFx.MaxSplats, $"{least} droplets, {fx.Splats} splats");
        }
        finally
        {
            fx.Dispose();
            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    static void Rd12(MakerCoreTest.Harness mh)
    {
        var (view, mirror, droplets, sizes, lines) = MosaicTest.EffectsShow(mh);
        foreach (var l in lines) Info(l);
        Check("a capture shows the effects: pixels differ from the same frame without them", view >= 100, $"{view} px, {droplets} droplets");
        Check("a puddle's mirror shows them too", mirror >= 100, $"{mirror} px");
        foreach (var (what, px, least, most) in sizes) Check($"{what} covers what its size says", px >= least && px <= most, $"{px} px, between {least} and {most}");
    }
}
