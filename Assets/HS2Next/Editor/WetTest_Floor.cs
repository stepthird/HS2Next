// WetTest, floors on real maps (play mode: a map loads only there), on BR-Chan.
//   WG8  the three modes; an offset moves the puddles; a moving character does not; a character whose root lies below its sole
//   WG9  the puddle's height equals the map query at its point, on 5 maps
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class WetTest
{
    static readonly string[] FloorMaps = { "illusion.hs2#10", "illusion.hs2#0", "illusion.hs2#1", "illusion.hs2#29", "2155x.map.hangar#0" };

    static IEnumerator Play_Floor()
    {
        current = "Floor";
        if (!new[] { "WG8", "WG9" }.Any(Want)) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        try
        {
            if (POpen("WG8", "floors")) yield return Wg8(app);
            if (POpen("WG9", "the puddle on the map's floor")) yield return Wg9(app);
            yield return PCall(app, new { cmd = "scene.pause", paused = false });
            yield return PCall(app, new { cmd = "scene.new" });
        }
        finally { Time.captureDeltaTime = old; HS2Next.Motion.Face.FaceSetup.Seed = null; }
    }

    static WetPart PPart(AppCore app) => app.Runtime.GetPart<WetPart>();
    static CharaNodeState PSt(AppCore app, string key) => (CharaNodeState)app.Session.Model.Find(key).State;

    // a map, BR-Chan and a stain that drips at once; the character stands still; the key
    static string pKey;

    static IEnumerator PStain(AppCore app, string map)
    {
        pKey = null;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "map.load", @ref = map, wait = true }, 240);
        if (!Check(map + " loads", POk, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" }, 240);
        if (!Check("chara.load", POk, Short(pLastText))) yield break;
        string key = Key(pLast);
        yield return PFrames(3);
        yield return PCall(app, new { cmd = "chara.motion", key, paused = true });
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        var block = PSt(app, key).Wet.Clone();
        var s = new WetStain { Id = block.NextId(), Name = "floor", Seed = 3 };
        WetPreset.ApplyTo(s, WetPreset.Blood);
        s.Shape.Spread = .5f;
        s.Drips.Tail = 2f;
        s.Drips.Rate = 60f;
        s.Running = true;
        block.Stains.Add(s);
        block.Current = s.Id;
        string why = PPart(app).Bake(key, block, s);
        if (!Check("the bake ran", why == null, why ?? "")) yield break;
        var r = app.Session.Commands.Execute(new WetEditCommand(key, block, WetEditCommand.LabelAdd));
        if (!Check("the edit went in", r.Status == CommandStatus.Ok, r.ToString())) yield break;
        yield return PFrames(2);
        pKey = key;
    }

    static bool Query(AppCore app, Vector3 p, out float y)
    {
        y = 0f;
        var q = WetFloor.Query(app.Runtime);
        if (q == null || !q.Surface(p, out MapSurfaceHit hit)) return false;
        y = hit.Height;
        return true;
    }

    // a floor edit as ops make it: the block's floor, the puddles moved by the difference, one command
    static float FloorTo(AppCore app, string key, Action<WetFloorValues> change, out string source)
    {
        PPart(app).CaptureNow(key);
        var block = PSt(app, key).Wet.Clone();
        change(block.Floor);
        float y = PPart(app).FloorEdit(key, block, out source);
        var r = app.Session.Commands.Execute(new WetEditCommand(key, block, WetEditCommand.LabelFloor));
        Check("the floor edit went in", r.Status == CommandStatus.Ok, r.ToString());
        return y;
    }

    static IEnumerator Wg8(AppCore app)
    {
        yield return PStain(app, FloorMaps[0]);
        string key = pKey;
        if (key == null) yield break;
        yield return PCall(app, new { cmd = "scene.step", n = 120, dt = Dt });
        yield return PFrames(1);
        var st = PPart(app).StatusOf(key);
        var rig = PPart(app).RigOf(key);
        Check("the floor comes from the map", st.FloorSource == WetFloorSources.Map, st.FloorSource);
        Check("puddles lie on it", rig.Puddles.Count > 0 && rig.Puddles.List.All(p => Math.Abs(p.Center.y - st.Floor) < 1e-3), rig.Puddles.Count + " at " + st.Floor);

        var real = app.Runtime.FindChara(key);
        var toes = new[] { "cf_J_Toes01_L", "cf_J_Toes01_R" }.Select(n => real.Context.BodyBones[n].position.y).Min();
        WetTargets.Frame(real.Context, out var f);
        float feet = toes - .31f * (float)f.Scale;
        float mapY = st.Floor, was = rig.Puddles.List[0].Center.y;
        float y = FloorTo(app, key, v => v.Mode = WetFloorValues.ModeFeet, out string src);
        yield return PFrames(2);
        rig = PPart(app).RigOf(key);
        Check("feet: the toes less .31 of the scale", Math.Abs(y - feet) < 1e-3 && src == WetFloorSources.Feet, y + " against " + feet);
        Check("feet: the puddles moved by the difference", Math.Abs(rig.Puddles.List[0].Center.y - (was + y - mapY)) < 1e-3, rig.Puddles.List[0].Center.y.ToString());

        float fixedY = y + .25f;
        y = FloorTo(app, key, v => { v.Mode = WetFloorValues.ModeFixed; v.Height = fixedY; }, out src);
        yield return PFrames(2);
        rig = PPart(app).RigOf(key);
        Check("fixed: the block's height", Math.Abs(y - fixedY) < 1e-5 && src == WetFloorSources.Fixed, y.ToString());
        Check("fixed: the puddles moved", Math.Abs(rig.Puddles.List[0].Center.y - fixedY) < 1e-3, "");

        y = FloorTo(app, key, v => v.Offset = .5f, out src);
        yield return PFrames(2);
        rig = PPart(app).RigOf(key);
        Check("an offset moves the puddles at once", Math.Abs(rig.Puddles.List[0].Center.y - (fixedY + .5f)) < 1e-3 && Math.Abs(y - (fixedY + .5f)) < 1e-5, rig.Puddles.List[0].Center.y.ToString());
        app.Session.Commands.Undo();
        yield return PFrames(2);
        rig = PPart(app).RigOf(key);
        Check("undo brings the puddles back with the floor", Math.Abs(rig.Puddles.List[0].Center.y - fixedY) < 1e-3, rig.Puddles.List[0].Center.y.ToString());

        var at = rig.Puddles.List[0].Center;
        yield return PCall(app, new { cmd = "obj.transform", key, pos = new[] { 7f, 0f, 3f } });
        yield return PFrames(2);
        rig = PPart(app).RigOf(key);
        Check("a moving character moves no puddle", rig.Puddles.List[0].Center == at, rig.Puddles.List[0].Center.ToString());

        // a new map: a puddle of the map mode takes the surface at its point; where there is none it stays
        yield return PStain(app, FloorMaps[0]);
        key = pKey;
        if (key == null) yield break;
        yield return PCall(app, new { cmd = "scene.step", n = 120, dt = Dt });
        yield return PFrames(1);
        rig = PPart(app).RigOf(key);
        var was2 = rig.Puddles.List.Select(p => p.Center).ToList();
        real = app.Runtime.FindChara(key);
        float high = real.Context.Hips.position.y;
        yield return PCall(app, new { cmd = "map.load", @ref = FloorMaps[1], wait = true }, 240);
        Check("another map loads", POk, Short(pLastText));
        yield return PFrames(3);
        rig = PPart(app).RigOf(key);
        Check("a new map keeps the puddles", rig.Puddles.Count == was2.Count, rig.Puddles.Count + " / " + was2.Count);
        for (int i = 0; i < rig.Puddles.Count && i < was2.Count; i++)
        {
            var c = rig.Puddles.List[i].Center;
            bool has = Query(app, new Vector3(c.x, Mathf.Max(high, was2[i].y), c.z), out float surface);
            Check("a new map: puddle " + i + (has ? " lies on the surface at its point" : " has no surface and stays"), has ? Math.Abs(c.y - surface) < 1e-3 : c == was2[i], c.y + " surface " + (has ? surface.ToString() : "none"));
        }

        // the root below the sole: a map floor is read at the origin, never from the root
        yield return PStain(app, FloorMaps[0]);
        key = pKey;
        if (key == null) yield break;
        real = app.Runtime.FindChara(key);
        float standing = PPart(app).StatusOf(key).Floor;
        var root = real.Context.BodyBones["cf_J_Root"];
        yield return PCall(app, new { cmd = "obj.transform", key, pos = new[] { 0f, -1.35f, 0f } });
        yield return PFrames(1);
        root.position += Vector3.up * 1.35f;
        PPart(app).RigOf(key).MeasureNow();
        var s2 = PPart(app).StatusOf(key);
        Check("the node's root 1.35 below the sole: the floor stays the map's", s2.FloorSource == WetFloorSources.Map && Math.Abs(s2.Floor - standing) < .02f, s2.Floor + " against " + standing);
        Check("the root is below that floor", real.Holder.position.y < s2.Floor - 1f, real.Holder.position.y.ToString());
    }

    static IEnumerator Wg9(AppCore app)
    {
        foreach (var map in FloorMaps)
        {
            yield return PStain(app, map);
            string key = pKey;
            if (key == null) continue;
            yield return PCall(app, new { cmd = "scene.step", n = 180, dt = Dt });
            yield return PFrames(1);
            var rig = PPart(app).RigOf(key);
            var st = PPart(app).StatusOf(key);
            Info(map + ": floor " + st.Floor + " from " + st.FloorSource + ", " + rig.Puddles.Count + " puddles");
            if (!Check(map + ": a puddle formed", rig.Puddles.Count > 0, "")) continue;
            Check(map + ": the floor is the map's", st.FloorSource == WetFloorSources.Map, st.FloorSource);
            float worst = 0f;
            foreach (var p in rig.Puddles.List)
            {
                if (!Query(app, p.Center + Vector3.up * 2f, out float y)) { worst = float.MaxValue; continue; }
                worst = Mathf.Max(worst, Mathf.Abs(p.Center.y - y));
            }
            Check(map + ": every puddle lies on the query at its point", worst <= .02f, "worst " + worst);
        }
    }
}
