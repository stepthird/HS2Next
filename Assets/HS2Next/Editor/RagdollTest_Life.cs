// RagdollTest, the life of a body beside its character (Phase 6.7 package P): rebuilds, the graveyard, save and load, duplicate and import. RB1, RB3 to RB7, RB9 to RB11, RF8, RF10, RE6, RE7.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class RagdollTest
{
    const string SaveClock = "2026-11-07T00:00:00Z";

    static void Headless_E_Life()
    {
        Group("RB1", "a clothes swap leaves the body alone", RB1);
        Group("RB3", "a plugin's rebuild", RB3);
        Group("RB4", "a shape edit resizes the body", RB4);
        Group("RB5", "delete and undo", RB5);
        Group("RB6", "a node forgotten", RB6);
        Group("RB7", "a hidden character", RB7);
        Group("RF8", "a pose through a file", RF8RB9);
        Group("RF10", "a save during a fall", RF10);
        Group("RB10", "a new scene", RB10);
        Group("RB11", "a thumbnail shows no body", RB11);
        Group("RE6", "a duplicate", RE6);
        Group("RE7", "an import", RE7);
    }

    static List<Quaternion> Rotations(RagdollBody b) => b.Parts.Select(p => p.Tr.rotation).ToList();

    static double WorstRotation(List<Quaternion> a, RagdollBody b)
    {
        double w = 0;
        for (int i = 0; i < a.Count; i++) w = Math.Max(w, Deg(a[i], b.Parts[i].Tr.rotation));
        return w;
    }

    static void RB1()
    {
        var h = Standing(femaleRel, "female", "RB1", out string k);
        try
        {
            var b = Body(h, k, "RB1");
            h.Frames(5);
            var r = h.Rt.FindChara(k);
            var bones = RagdollParts.All.Select(p => r.Context.BodyBones[p.Bone]).ToList();
            var stages = r.Motion.Stages.Where(s => s.Name.StartsWith("ragdoll", StringComparison.Ordinal)).ToList();
            var renderers = new HashSet<Renderer>(r.Root.GetComponentsInChildren<Renderer>(true));
            var lists = MakerCatalog.For(h.App.Services.Catalog);
            var row = h.App.Services.Catalog.EntriesOf(240).Where(e => lists.IsListed(240, 1, e.Key)).OrderBy(e => e.Key.ToString(), StringComparer.Ordinal).Skip(3).FirstOrDefault();
            if (!Check("a top of the lists", row != null)) return;
            h.Ok(new { cmd = "chara.edit.item", key = k, path = "coord.clothes.top.item", @ref = row.Key.ToString() }, "the swap");
            h.Frames(3);
            var now = new HashSet<Renderer>(r.Root.GetComponentsInChildren<Renderer>(true));
            Check("Present: the swap changed the renderers", !now.SetEquals(renderers), now.Except(renderers).Count() + " new, " + renderers.Except(now).Count() + " gone");
            Check("the 16 transforms are the same objects", RagdollParts.All.Select(p => r.Context.BodyBones[p.Bone]).SequenceEqual(bones));
            var after = r.Motion.Stages.Where(s => s.Name.StartsWith("ragdoll", StringComparison.Ordinal)).ToList();
            Check("the stages are the same objects", after.SequenceEqual(stages), after.Count + " stages");
        }
        finally { h.Dispose(); }
    }

    static void RB3()
    {
        var h = Standing(femaleRel, "female", "RB3", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RB3");
            h.Frames(60);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            var r = h.Rt.FindChara(k);
            var old = RagdollParts.All.Select(p => r.Context.BodyBones[p.Bone]).ToList();
            var keep = Rotations(b);
            Check("the rebuild is asked for", r.RequestPluginRebuild("RagdollTest"));
            h.Frames(2);
            int alive = old.Count(t => t);
            Check("Present: the old bones are destroyed", alive == 0, alive + " of 16 alive");
            Check("the stages are on the new motion", r.Motion.Stages.Count(s => s.Name.StartsWith("ragdoll", StringComparison.Ordinal)) == 3);
            double worst = WorstRotation(keep, b);
            Check("part rotations kept within 1e-4", worst <= 1e-4 * 180.0 / Math.PI, worst.ToString("G4") + " degrees");
            double written = b.Parts.Max(p => Deg(p.Bone.rotation, p.Tr.rotation * p.Offset));
            Check("the new bones are written", written <= 0.01, written.ToString("G4") + " degrees");
        }
        finally { h.Dispose(); }
    }

    static void RB4()
    {
        var h = Standing(femaleRel, "female", "RB4", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RB4");
            h.Frames(60);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            float thigh = b.BuiltThigh;
            var size = ((CapsuleCollider)b.Part("thigh_L").Col).height;
            var keep = Rotations(b);
            h.Session.Set(k, "appearance.chara.body.shape[0]", 1.5f);
            h.Frames(3);
            float moved = Math.Abs(b.BuiltThigh - thigh) / thigh;
            var now = ((CapsuleCollider)b.Part("thigh_L").Col).height;
            Check("Present: the thigh's length moved by at least 5 %", moved >= 0.05f, (moved * 100f).ToString("F2") + " %");
            Check("the colliders follow", Mathf.Abs(now - size) > 1e-3f, $"{size:F3}, {now:F3}");
            double worst = WorstRotation(keep, b);
            Check("the pose is kept", worst <= 1e-4 * 180.0 / Math.PI, worst.ToString("G4") + " degrees");
        }
        finally { h.Dispose(); }
    }

    static void RB5()
    {
        var h = Standing(femaleRel, "female", "RB5", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RB5");
            var start = b.Part(RagdollParts.Chest).Tr.position;
            h.Frames(120);
            float moved = (b.Part(RagdollParts.Chest).Tr.position - start).magnitude;
            Check("Present: the body moved before", moved >= 1f, moved.ToString("F3"));
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            var keep = Rotations(b);
            var r = h.Session.Delete(new[] { k });
            Check("the node goes", r.Ok, r.Message);
            h.Frames(2);
            Check("the proxies are off", b.Buried && !b.Container.activeSelf);
            Check("the world stays", h.Part.World != null);
            long steps = h.Part.World.Steps;
            h.Frames(10);
            h.Ok(new { cmd = "undo" }, "undo");
            h.Frames(2);
            Check("undo: the body is back", h.Part.BodyOf(k) == b && !b.Buried && b.Container.activeSelf);
            double worst = WorstRotation(keep, b);
            Check("at the kept pose", worst <= 1e-3, worst.ToString("G4") + " degrees");
            var rr = h.Rt.FindChara(k);
            double written = b.Parts.Max(p => Deg(p.Bone.rotation, p.Tr.rotation * p.Offset));
            Check("the bones are written", written <= 0.05, written.ToString("G4") + " degrees");
        }
        finally { h.Dispose(); }
    }

    static void RB6()
    {
        var h = Standing(femaleRel, "female", "RB6", out string k);
        try
        {
            var b = Body(h, k, "RB6");
            h.Frames(5);
            h.Session.Delete(new[] { k });
            h.Frames(2);
            h.Session.Commands.ClearHistory();
            h.Frames(2);
            Check("forgotten: the body and the world go", h.Part.BodyOf(k) == null && h.Part.World == null);
        }
        finally { h.Dispose(); }
    }

    static void RB7()
    {
        var h = Standing(femaleRel, "female", "RB7", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RB7");
            h.Frames(5);
            h.Session.Set(k, "visible", false);
            var at = b.Part(RagdollParts.Chest).Tr.position;
            h.Frames(60);
            float moved = (b.Part(RagdollParts.Chest).Tr.position - at).magnitude;
            Check("hidden, the body goes on", moved >= 0.5f, moved.ToString("F3"));
            Check("Present: it is hidden and it has a body", !h.Rt.Model.Find(k).EffectiveVisible && h.Part.BodyOf(k) == b);
        }
        finally { h.Dispose(); }
    }

    static void RF8RB9()
    {
        var h = Standing(femaleRel, "female", "RF8", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RF8");
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -600f));
            h.Frames(420);
            Check("the body came to rest", b.SettledParts != null, b.SettledChanged.ToString());
            float away = (b.Part(RagdollParts.Chest).Tr.position - b.Part(RagdollParts.Chest).TargetPos).magnitude;
            double far = Deg(b.Part(RagdollParts.Chest).Tr.rotation, b.Part(RagdollParts.Chest).TargetRot);
            string path = (string)h.Ok(new { cmd = "scene.save", name = "rf8", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save")["path"];
            var st = (CharaNodeState)h.Rt.Model.Find(k).State;
            var saved = st.Ragdoll.Pose.Parts.ToDictionary(kv => kv.Key, kv => kv.Value);
            var hips = st.Ragdoll.Pose.Hips;
            Check("the pose is in the block", saved.Count == 16 && !st.Ragdoll.Pose.IsEmpty, saved.Count + " parts");
            Check("Present: the pose differs from the pose sources by more than 10 degrees", far > 10.0, far.ToString("F2") + " degrees, chest " + away.ToString("F2") + " away");
            h.Ok(new { cmd = "scene.new" }, "new");
            h.Ok(new { cmd = "scene.load", path }, "load");
            h.Rt.ApplyNow();
            var c = h.Part.BodyOf(k);
            if (!Check("a body after the load", c != null)) return;
            var st2 = (CharaNodeState)h.Rt.Model.Find(k).State;
            bool same = saved.All(kv => st2.Ragdoll.Pose.Parts.TryGetValue(kv.Key, out var q) && q.Equals(kv.Value)) && st2.Ragdoll.Pose.Hips.Equals(hips);
            Check("RF8: the block holds the saved rotations in bits, the hips too", same);
            var holder = h.Rt.FindChara(k).Holder;
            double worst = 0;
            int bits = 0;
            var plain = new GameObject("a plain setter");
            foreach (var p in c.Parts)
            {
                var q = saved[p.Key];
                var want = holder.rotation * new Quaternion(q.X, q.Y, q.Z, q.W);
                worst = Math.Max(worst, Deg(want, p.Tr.rotation));
                plain.transform.rotation = want;
                if (!SameBits(plain.transform.rotation, p.Tr.rotation)) bits++;
            }
            UnityEngine.Object.DestroyImmediate(plain);
            var hp = holder.TransformPoint(new Vector3(hips.X, hips.Y, hips.Z));
            Check("RF8: the parts stand at what the rotation setter gives the saved values", bits == 0, $"{bits} of 16 differ in their bits, at most {worst:G3} degrees from the value itself");
            Check("RF8: the hips at the saved place", SameBits(hp, c.Parts[0].Tr.position), (hp - c.Parts[0].Tr.position).magnitude.ToString("G3"));
            Check("RB9: limp, the parts are asleep at the first frame", c.Parts.All(p => p.Rb.IsSleeping()), c.Parts.Count(p => p.Rb.IsSleeping()) + " of 16");
            string again = (string)h.Ok(new { cmd = "scene.save", name = "rf8b", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save again, with no step between")["path"];
            byte[] first = Entry(path, "scene.json"), second = Entry(again, "scene.json");
            var apart = LinesApart(first, second);
            var bare = RF7Twin();
            var alone = apart.Except(bare).ToList();
            string text = System.Text.Encoding.UTF8.GetString(first);
            Check("RF7: Present: the file holds a body's pose", text.Contains("\"ragdoll\"") && text.Contains("\"pose\""));
            Check("RF7: save, load, save, a body at rest: the ragdoll lines keep their bytes", !apart.Any(x => x.Split('/').Contains("ragdoll")), first.Length + " bytes, lines apart with a body: " + (apart.Count == 0 ? "none" : string.Join(", ", apart)));
            Check("RF7: any other line that differs is one the bodyless twin shows too", alone.Count == 0, "the twin: " + (bare.Count == 0 ? "none" : string.Join(", ", bare)) + (alone.Count == 0 ? "" : "; not in the twin: " + string.Join(", ", alone)));
            h.Frames(1);
            Check("RB9: Present: the first frame shows the pose, not the pose sources", Deg(c.Part(RagdollParts.Chest).Bone.rotation, c.Part(RagdollParts.Chest).TargetBoneRot) > 10.0);
        }
        finally { h.Dispose(); }
    }

    // a line of scene.json by the keys above it: the same line in a scene with a body and in one without
    static string PathOf(string[] lines, int at)
    {
        var keys = new List<string>();
        int indent = int.MaxValue;
        for (int j = at; j >= 0; j--)
        {
            string t = lines[j].TrimStart();
            int n = lines[j].Length - t.Length;
            if (n >= indent) continue;
            indent = n;
            int colon = t.IndexOf("\":", StringComparison.Ordinal);
            if (t.StartsWith("\"") && colon > 0) keys.Insert(0, t.Substring(1, colon - 1));
        }
        return string.Join("/", keys);
    }

    static List<string> LinesApart(byte[] a, byte[] b)
    {
        string[] x = System.Text.Encoding.UTF8.GetString(a).Split('\n'), y = System.Text.Encoding.UTF8.GetString(b).Split('\n');
        var off = new List<string>();
        if (x.Length != y.Length) off.Add($"{x.Length} lines against {y.Length}");
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            if (x[i] != y[i]) off.Add(PathOf(x, i));
        return off;
    }

    // RF7's run on the same character with no body
    static List<string> RF7Twin()
    {
        var h = Standing(femaleRel, "female", "RF7, no body", out string k);
        try
        {
            h.Frames(420);
            string path = (string)h.Ok(new { cmd = "scene.save", name = "rf7twin", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save")["path"];
            h.Ok(new { cmd = "scene.new" }, "new");
            h.Ok(new { cmd = "scene.load", path }, "load");
            h.Rt.ApplyNow();
            string again = (string)h.Ok(new { cmd = "scene.save", name = "rf7twinb", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save again, with no step between")["path"];
            return LinesApart(Entry(path, "scene.json"), Entry(again, "scene.json"));
        }
        finally { h.Dispose(); }
    }

    static byte[] Entry(string path, string name)
    {
        using (var z = System.IO.Compression.ZipFile.OpenRead(path))
        using (var s = z.GetEntry(name).Open())
        using (var m = new MemoryStream())
        {
            s.CopyTo(m);
            return m.ToArray();
        }
    }

    static void RF10()
    {
        var h = Standing(femaleRel, "female", "RF10", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RF10");
            h.Frames(300);
            var rest = b.SettledParts?.ToArray();
            var chest = b.Part(RagdollParts.Chest);
            if (!Check("it came to rest", rest != null)) return;
            var at = chest.Tr.position;
            b.Push(chest, new Vector3(600f, 1200f, 0f));
            h.Frames(12);
            float moved = (chest.Tr.position - at).magnitude;
            string path = (string)h.Ok(new { cmd = "scene.save", name = "rf10", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save in the air")["path"];
            var st = (CharaNodeState)h.Rt.Model.Find(k).State;
            int same = 0;
            for (int i = 0; i < 16; i++)
                if (st.Ragdoll.Pose.Parts.TryGetValue(b.Parts[i].Key, out var q) && SameBits(new Quaternion(q.X, q.Y, q.Z, q.W), rest[i])) same++;
            Check("a save during a fall holds the last rest", same == 16, same + " of 16 rotations");
            string text = System.Text.Encoding.UTF8.GetString(Entry(path, "scene.json"));
            Check("no velocity member", text.IndexOf("velocity", StringComparison.OrdinalIgnoreCase) < 0);
            Check("Present: the body moved at least 3 since that rest", moved >= 3f, moved.ToString("F3"));
        }
        finally { h.Dispose(); }
    }

    static void RB10()
    {
        var h = Standing(femaleRel, "female", "RB10", out string k);
        try
        {
            var b = Body(h, k, "RB10");
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            var w = h.Part.World;
            var scene = w.Scene;
            int stages = r.Motion.Stages.Count(s => s.Name.StartsWith("ragdoll", StringComparison.Ordinal));
            int parts = b.Parts.Count, joints = b.Parts.Count(p => p.Joint);
            Check("Present: before it 16 parts, 15 joints, a scene, 3 stages", parts == 16 && joints == 15 && scene.IsValid() && stages == 3, $"{parts}, {joints}, {scene.IsValid()}, {stages}");
            h.Ok(new { cmd = "scene.new" }, "new");
            h.Frames(2);
            Check("scene new: no world, no body, no part", h.Part.World == null && b.Parts.Count == 0, b.Parts.Count + " parts");
            Check("no scene of the world", !scene.IsValid());
            Check("no stage", (CharaStageHost.Of(h.Rt)?.Count ?? 0) == 0, (CharaStageHost.Of(h.Rt)?.Count ?? 0) + " entries");
        }
        finally { h.Dispose(); }
    }

    static void RB11()
    {
        var h = Standing(femaleRel, "female", "RB11", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RB11");
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -1500f));
            h.Frames(120);
            var r = h.Rt.FindChara(k);
            var chest = b.Part(RagdollParts.Chest);
            float away = (chest.Tr.position - chest.TargetPos).magnitude;
            var hips = b.Parts[0];
            double worst = 0;
            using (CharaPoseDrivers.SuspendAll(r.Root))
                foreach (var p in b.Parts) worst = Math.Max(worst, Deg(p.Bone.rotation, p.TargetBoneRot));
            Check("while suspended the bones are the pose sources", worst <= 1e-3, worst.ToString("G4") + " degrees");
            Check("Present: the body is more than 10 from the pose", away > 10f, away.ToString("F2"));
            h.Frames(2);
            double back = b.Parts.Max(p => Deg(p.Bone.rotation, p.Tr.rotation * p.Offset));
            Check("after it the body writes again", back <= 0.05, back.ToString("G4") + " degrees");
        }
        finally { h.Dispose(); }
    }

    static void RE6()
    {
        var h = Standing(femaleRel, "female", "RE6", out string k);
        try
        {
            var b = Body(h, k, "RE6");
            h.Frames(5);
            var p = h.Part.PinPart(k, "hand_R", null, null, null, false, ExecuteOptions.NotRecorded);
            var r = h.Session.Duplicate(new[] { k });
            Check("the duplicate", r.Ok, r.Message);
            string copy = r.NewIds.FirstOrDefault();
            h.Rt.ApplyNow();
            h.Frames(2);
            var block = ((CharaNodeState)h.Rt.Model.Find(copy).State).Ragdoll;
            Check("the copy's block is off, its pin kept", !block.On && !block.Limp && block.Pins.Count == 1, $"on {block.On}, {block.Pins.Count} pins");
            Check("no second body", h.Part.BodyOf(copy) == null && h.Part.World.Bodies.Count == 1);
            Check("Present: the source's body is there", h.Part.BodyOf(k) == b);
        }
        finally { h.Dispose(); }
    }

    static void RE7()
    {
        var h = Standing(femaleRel, "female", "RE7", out string k);
        try
        {
            string node = Key(h.Ok(new { cmd = "item.add", no = 399 }, "a node"));
            string gone = Key(h.Ok(new { cmd = "item.add", no = 399 }, "a node that goes"));
            h.Frames(1);
            var b = Body(h, k, "RE7");
            h.Part.PinPart(k, "hand_R", null, node, null, false, ExecuteOptions.NotRecorded);
            h.Part.PinPart(k, "hand_L", null, gone, null, false, ExecuteOptions.NotRecorded);
            h.Part.AddPusher(k, "cf_J_LegLow01_R", "cf_J_Toes01_R", 0.45f, out _, ExecuteOptions.NotRecorded);
            h.Session.Delete(new[] { gone });
            h.Frames(2);
            string path = (string)h.Ok(new { cmd = "scene.save", name = "re7", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save")["path"];
            h.Ok(new { cmd = "scene.new" }, "new");
            var r = h.Ok(new { cmd = "scene.import", path }, "import");
            h.Frames(2);
            var charas = h.Rt.Model.PreOrder().Where(n => n.State is CharaNodeState).ToList();
            if (!Check("the character came", charas.Count == 1, charas.Count.ToString())) return;
            var items = h.Rt.Model.PreOrder().Where(n => n.Kind == NodeKind.Item).Select(n => n.Id).ToList();
            var block = ((CharaNodeState)charas[0].State).Ragdoll;
            Check("the copy's block is off", !block.On, block.On.ToString());
            var a = block.Pins.FirstOrDefault(p => p.Part == "hand_R");
            var c = block.Pins.FirstOrDefault(p => p.Part == "hand_L");
            Check("a pin's follow is remapped", a != null && items.Contains(a.Follow) && a.Follow != node, a?.Follow);
            Check("a follow on a node not copied is dropped", c != null && !c.IsFollowing, c?.Follow);
            var w = h.Rt.Model.Ragdoll;
            Check("the pusher came under the new id", w != null && w.Pushers.Count == 1 && w.Pushers[0].Key == charas[0].Id, w?.Pushers.Count.ToString());
        }
        finally { h.Dispose(); }
    }
}
