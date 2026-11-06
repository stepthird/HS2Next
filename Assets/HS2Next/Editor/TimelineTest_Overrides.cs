// TimelineTest, the override layer's part (Phase 6.2 package O). Headless, edit mode, on
// BR-Chan ($a), HERO107 ($b), the vanilla item 399 ($p) and a light ($l), with the composition all_types
// (Tests/Timeline/compositions/all_types.json) evaluated by the Timeline engine (TimelinePlayer) into the layer through a test
// sink (owner "timeline", priority 0, as the playback does), frames pumped by hand (AppCore.Frame):
//   TO3  every channel applied: holder poses = the evaluated values (1e-5 units, 0.01 deg); the FK bone = the slot where the
//        Neck group applies and = the model where it does not (effective false); the pinned animation time re-poses a
//        character at speed 0; the face status; the animation row and speed; the item's colour slot and the light's colour as
//        the model writes those values (a reference item and light, 1e-6); the camera transform; HS2DofFeature.Inputs for
//        each focus form (1e-4), size, aperture, MaxBlur1080
//   TO4  the owner's release restores every channel (the pose, colours, face, animation, camera, depth of field of before);
//        300 ticks of play leave the model untouched (no change batch, no history change, version and dirty flag unchanged)
//   TO5  a character rebuild (chara.change) keeps and re-applies its slots; delete -> TargetExists false, undo -> shown again;
//        a forgotten node's slots and a new scene's are released
//   TO7  the save capture keeps chara.anime.time of a character with an animation slot and the look of one with an FK slot
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class TimelineTest
{
    static void Headless_O_Overrides()
    {
        try
        {
            Group("TO3", "every channel applied (all_types on BR-Chan, HERO107, an item, a light)", TO3);
            Group("TO4", "release restores each channel; play leaves the model untouched", TO4);
            Group("TO5", "rebuild, delete / undo, forget and scene.new", TO5);
            Group("TO7", "the save-capture exclusions", TO7);
        }
        finally
        {
            ofix?.Dispose();
            ofix = null;
        }
    }

    // ------------------------------------------------------------ the sink: the engine's values into the layer, recorded

    sealed class LayerSink : ITimelineSink
    {
        readonly SceneRuntime rt;
        /// <summary>The last value set per channel ("&lt;node or scene&gt; &lt;path&gt;").</summary>
        public readonly Dictionary<string, object> Last = new Dictionary<string, object>(StringComparer.Ordinal);
        public int Fired;

        public LayerSink(SceneRuntime rt) { this.rt = rt; }

        static string K(TimelineChannel c) => (c.NodeId ?? "scene") + " " + c.Path;
        public static string K(string node, string path) => (node ?? "scene") + " " + path;

        OverrideSlot Slot(TimelineChannel c)
        {
            if (c.Tag is OverrideSlot s && !s.IsReleased) return s;
            s = rt.Overrides.Acquire(OverrideLayer.TimelineOwner, c.NodeId, c.Path);
            c.Tag = s;
            return s;
        }

        public NodeKind? KindOf(string nodeId) => rt.Model.Find(nodeId)?.Kind;
        public void SetNumber(TimelineChannel c, double v) { Slot(c)?.Set(v); Last[K(c)] = v; }
        public void SetInteger(TimelineChannel c, int v) { Slot(c)?.Set(v); Last[K(c)] = v; }
        public void SetVector(TimelineChannel c, in TimelineVec3 v) { Slot(c)?.SetVector(v.X, v.Y, v.Z); Last[K(c)] = v; }
        public void SetRotation(TimelineChannel c, in TimelineQuat q) { Slot(c)?.SetQuaternion(q.X, q.Y, q.Z, q.W); Last[K(c)] = q; }
        public void SetColor(TimelineChannel c, in TimelineColor v) { Slot(c)?.SetColor(v.R, v.G, v.B, v.A); Last[K(c)] = v; }
        public void SetCamera(TimelineChannel c, in TimelineCameraValue v)
        {
            Slot(c)?.SetCamera(v.Target.X, v.Target.Y, v.Target.Z, v.Rotation.X, v.Rotation.Y, v.Rotation.Z, v.Rotation.W, v.Dist, v.Fov);
            Last[K(c)] = v;
        }
        public void SetDepthOfField(TimelineChannel c, in TimelineDofValue v)
        {
            Slot(c)?.SetDof(new OverrideDof(v.Enabled, v.FocalSize, v.Aperture, v.HasBlur ? v.Blur : 0, Focus(v.Focus), v.FocusTo != null ? Focus(v.FocusTo) : null, v.FocusBlend));
            Last[K(c)] = v;
        }
        public void SetAnimation(TimelineChannel c, in TimelineAnimeRow r) { Slot(c)?.SetAnimation(r.Source, r.Group, r.Category, r.No); Last[K(c)] = r; }
        public void Clear(TimelineChannel c) { (c.Tag as OverrideSlot)?.Clear(); Last.Remove(K(c)); }
        public void Release(TimelineChannel c) { (c.Tag as OverrideSlot)?.Release(); c.Tag = null; Last.Remove(K(c)); }
        public void ReleaseAll() { rt.Overrides.ReleaseOwner(OverrideLayer.TimelineOwner); Last.Clear(); }
        public void Fire(TimelineEventRun run) => Fired++;

        static OverrideFocus Focus(DofFocus f)
        {
            if (f == null) return OverrideFocus.Pivot;
            switch (f.Kind)
            {
                case DofFocusKind.Node:
                    if (f.Bone == null) return OverrideFocus.OfNode(f.NodeId);
                    var o = f.Offset ?? new TimelineVec3(0, 0, 0);
                    return OverrideFocus.OfBone(f.NodeId, f.Bone, o.X, o.Y, o.Z);
                case DofFocusKind.Point: return OverrideFocus.OfPoint(f.Point.X, f.Point.Y, f.Point.Z);
                case DofFocusKind.Dist: return OverrideFocus.OfDist(f.Dist);
                default: return OverrideFocus.Pivot;
            }
        }
    }

    sealed class BuildHost : ITimelineBuildHost
    {
        readonly Host h;
        public BuildHost(Host h) { this.h = h; }
        public NodeKind? KindOf(string nodeId) => h.Session.Model.Find(nodeId)?.Kind;
        public AnimeRef ResolveAnime(string charaKey, JObject key, string arg)
        {
            int g = key.Value<int?>("group") ?? 0, c = key.Value<int?>("category") ?? 0, no = key.Value<int?>("no") ?? 0;
            var a = services.Db.FindAnime(g, c, no);
            return a == null ? null : new AnimeRef(a.Guid, a.Group, a.Category, a.No);
        }
        public int EyePatternAt(string charaKey, int position, string arg)
        {
            var c = h.Rt.FindChara(charaKey);
            var l = c?.Expression?.Lists?.Eyes;
            if (l == null || l.Length == 0) throw new InvalidOperationException("the character is not built");
            return l[Mathf.Clamp(position, 0, l.Length - 1)];
        }
    }

    // ------------------------------------------------------------ the fixture

    sealed class OFix
    {
        public Host H;
        public string A, B, P, L, RefItem, RefLight;
        public LayerSink Sink;
        public TimelinePlayer Player;
        public CompiledTimeline Compiled;
        public string ItemPrint, LightPrint;
        public Quaternion NeckBefore, ArmBefore;
        public void Dispose() => H?.Dispose();
    }

    static OFix ofix;

    /// <summary>The node id of an add reply: its key, or its first added node's key (chara.load, item.add).</summary>
    static string Key(this JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

    static OFix Fix()
    {
        if (ofix != null) return ofix;
        var f = new OFix { H = NewHost(0x0f0e0d0c0b0aUL) };
        var h = f.H;
        f.A = (string)h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "fixture: chara.load BR-Chan").Key();
        f.B = (string)h.Ok(new { cmd = "chara.load", path = maleRel, sex = "male" }, "fixture: chara.load HERO107").Key();
        f.P = (string)h.Ok(new { cmd = "item.add", no = 399 }, "fixture: item.add 399").Key();
        f.L = (string)h.Ok(new { cmd = "light.add", no = 0, pos = new[] { 0, 12, 3 } }, "fixture: light.add 0").Key();
        f.RefItem = (string)h.Ok(new { cmd = "item.add", no = 399, pos = new[] { 0, 0, -20 } }, "fixture: the reference item").Key();
        f.RefLight = (string)h.Ok(new { cmd = "light.add", no = 0, pos = new[] { 0, 12, -20 } }, "fixture: the reference light").Key();
        h.Ok(new { cmd = "obj.transform", key = f.B, pos = new[] { 5, 0, 0 } }, "fixture: HERO107 at (5, 0, 0)");
        h.Ok(new { cmd = "chara.anime", key = f.A, group = 19, category = 0, no = 0, speed = 0 }, "fixture: BR-Chan Idle_00 at speed 0");
        h.Ok(new { cmd = "chara.anime", key = f.B, group = 11, category = 5, no = 0, speed = 0 }, "fixture: HERO107 dance_00 at speed 0");
        h.Ok(new { cmd = "fk.enable", key = f.A, on = true }, "fixture: FK on (BR-Chan)");
        h.Ok(new { cmd = "fk.enable", key = f.A, groups = new[] { "Body", "Neck" }, on = true }, "fixture: FK groups Body and Neck (BR-Chan)");
        h.Frames(3);
        f.ItemPrint = ItemPrint(h, f.P);
        f.LightPrint = LightPrint(h, f.L);
        f.NeckBefore = Bone(h, f.A, "cf_J_Neck").localRotation;
        f.ArmBefore = Bone(h, f.A, "cf_J_ArmUp00_R").localRotation;
        // the composition with its placeholders
        string json = File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Tests", "Timeline", "compositions", "all_types.json"));
        json = json.Replace("\"$a\"", "\"" + f.A + "\"").Replace("\"$b\"", "\"" + f.B + "\"").Replace("\"$p\"", "\"" + f.P + "\"").Replace("\"$l\"", "\"" + f.L + "\"");
        var root = JObject.Parse(json);
        float duration = root.Value<float>("duration");
        var warnings = new List<string>();
        var doc = new TimelineDocument
        {
            Duration = duration,
            Loop = root.Value<bool?>("loop") ?? true,
            Tracks = TimelineBuildInput.ParseTracks(root["tracks"], duration, new BuildHost(h), new CountingIdSource(77), false, warnings),
        };
        Check("fixture: all_types parses (15 tracks)", doc.Tracks.Count == 15, doc.Tracks.Count + " tracks; " + string.Join("; ", warnings));
        f.Compiled = TimelineCompiler.Compile(doc);
        f.Sink = new LayerSink(h.Rt);
        f.Player = new TimelinePlayer(f.Sink);
        f.Player.SetTimeline(f.Compiled);
        ofix = f;
        return f;
    }

    // ------------------------------------------------------------ readers

    static Transform Bone(Host h, string chara, string name)
    {
        var c = h.Rt.FindChara(chara);
        if (c?.Root == null) return null;
        foreach (var t in c.Root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    /// <summary>The colours of every material of an item (its renderers' colour properties), as text (equal = written the same).</summary>
    static string ItemPrint(Host h, string id)
    {
        var it = h.Rt.FindItem(id);
        if (it?.Instance == null) return "not built";
        var sb = new System.Text.StringBuilder();
        foreach (var r in it.Instance.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                foreach (var p in new[] { "_Color", "_Color2", "_Color3", "_Color4", "_BaseColor" })
                    if (m.HasProperty(p)) { var c = m.GetColor(p); sb.Append(p).Append(' ').Append(c.r.ToString("R")).Append(',').Append(c.g.ToString("R")).Append(',').Append(c.b.ToString("R")).Append(',').Append(c.a.ToString("R")).Append(';'); }
            }
        return sb.ToString();
    }

    static string LightPrint(Host h, string id)
    {
        var l = h.Rt.Find(id) as LightRealization;
        if (l?.Light == null) return "not built";
        var o = HS2Next.BundleLights.OriginalOf(l.Light);
        return l.Light.color.ToString("R") + " i " + l.Light.intensity.ToString("R") + (o ? " orig " + o.Color.ToString("R") : "");
    }

    static double Angle(Quaternion a, Quaternion b)
    {
        double dot = Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w);
        double na = Math.Sqrt((double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w);
        double nb = Math.Sqrt((double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w);
        dot /= Math.Max(na * nb, 1e-30);
        return dot >= 1 ? 0 : 2 * Math.Acos(dot) * 180 / Math.PI;
    }

    static Quaternion Q(TimelineQuat q) => new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
    static Vector3 V(TimelineVec3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

    static T Last<T>(OFix f, string node, string path) => f.Sink.Last.TryGetValue(LayerSink.K(node, path), out var o) && o is T t ? t : default;
    static bool Has(OFix f, string node, string path) => f.Sink.Last.ContainsKey(LayerSink.K(node, path));

    /// <summary>Seeks the Timeline (the engine sets the slots in the call) and pumps one frame (motion, late apply).</summary>
    static void SeekFrame(OFix f, double t)
    {
        f.Player.Seek(t);
        f.H.Frames(1);
    }

    // ------------------------------------------------------------ TO3

    static readonly double[] To3Times = { 0, 0.75, 1.5, 2.25, 3.0, 3.6, 5.0, 5.5, 6.5, 7.5 };

    static void TO3()
    {
        var f = Fix();
        var h = f.H;
        var view = h.Rt.GetPart<SceneViewCamera>();
        int bad = 0;
        double maxPos = 0, maxRot = 0, maxScale = 0, maxFk = 0, maxCamPos = 0, maxCamRot = 0, maxFocus = 0;
        var notes = new List<string>();
        void Note(string s) { bad++; if (notes.Count < 12) notes.Add(s); }
        foreach (double t in To3Times)
        {
            SeekFrame(f, t);
            // node transforms
            var pos = Last<TimelineVec3>(f, f.B, "transform.pos");
            double dp = (h.Rt.HolderOf(f.B).localPosition - V(pos)).magnitude;
            maxPos = Math.Max(maxPos, dp);
            if (!Has(f, f.B, "transform.pos") || dp > 1e-5) Note($"t {t}: $b pos {h.Rt.HolderOf(f.B).localPosition:F6} vs {V(pos):F6}");
            var rot = Last<TimelineQuat>(f, f.P, "transform.rot");
            double dr = Angle(h.Rt.HolderOf(f.P).localRotation, Q(rot));
            maxRot = Math.Max(maxRot, dr);
            if (dr > 0.01) Note($"t {t}: $p rot {dr:F4} deg");
            var sc = Last<TimelineVec3>(f, f.P, "transform.scale");
            double ds = (h.Rt.HolderOf(f.P).lossyScale - V(sc)).magnitude;
            maxScale = Math.Max(maxScale, ds);
            if (ds > 1e-5) Note($"t {t}: $p scale {h.Rt.HolderOf(f.P).lossyScale:F6} vs {V(sc):F6}");
            // FK bone (Neck group on)
            var fk = Last<TimelineQuat>(f, f.A, "chara.fk.bones[\"cf_J_Neck\"]");
            double dfk = Angle(Bone(h, f.A, "cf_J_Neck").localRotation, Q(fk));
            maxFk = Math.Max(maxFk, dfk);
            if (dfk > 0.01) Note($"t {t}: $a cf_J_Neck {dfk:F4} deg from the slot");
            // face
            var c = h.Rt.FindChara(f.A);
            var st = c.Expression.Status;
            int eyes = Last<int>(f, f.A, "chara.face.eyes"), mouth = Last<int>(f, f.A, "chara.face.mouth");
            double eo = Last<double>(f, f.A, "chara.face.eyesOpen"), mo = Last<double>(f, f.A, "chara.face.mouthOpen");
            if (st.EyesPtn != eyes || st.MouthPtn != mouth || Math.Abs(st.EyesOpenMax - (float)eo) > 1e-6 || Math.Abs(c.Expression.MouthOpen - Mathf.Clamp01((float)mo)) > 1e-6)
                Note($"t {t}: face eyes {st.EyesPtn}/{eyes} mouth {st.MouthPtn}/{mouth} eyesOpen {st.EyesOpenMax}/{eo} mouthOpen {c.Expression.MouthOpen}/{mo}");
            // animation row and speed
            var row = Last<TimelineAnimeRow>(f, f.A, "chara.anime.ref");
            var cur = c.Animation.Current;
            if (cur == null || cur.Group != row.Group || cur.Category != row.Category || cur.No != row.No)
                Note($"t {t}: $a row {(cur == null ? "none" : cur.Group + "/" + cur.Category + "/" + cur.No)} vs {row.Group}/{row.Category}/{row.No}");
            double sp = Last<double>(f, f.A, "chara.anime.speed");
            if (Math.Abs(c.Animation.Speed - (float)Math.Max(0, sp)) > 1e-6) Note($"t {t}: $a speed {c.Animation.Speed} vs {sp}");
            // the pinned time
            var cb = h.Rt.FindChara(f.B);
            double at = Last<double>(f, f.B, "chara.anime.time");
            if (!cb.Animation.TimePinned || Math.Abs(cb.Animation.NormalizedTime - (float)at) > 1e-5) Note($"t {t}: $b time {cb.Animation.NormalizedTime} pinned {cb.Animation.TimePinned} vs {at}");
            // colours: the reference item and light get the same values in their model
            var col = Last<TimelineColor>(f, f.P, "item.colors[0].main");
            h.Session.Set(f.RefItem, "item.colors[0].main", new Color4((float)col.R, (float)col.G, (float)col.B, (float)col.A));
            var lc = Last<TimelineColor>(f, f.L, "light.color");
            h.Session.Set(f.RefLight, "light.color", new Color4((float)lc.R, (float)lc.G, (float)lc.B, (float)lc.A));
            h.Frames(1);
            if (ItemPrint(h, f.P) != ItemPrint(h, f.RefItem)) Note($"t {t}: item colours {Short(ItemPrint(h, f.P))} vs reference {Short(ItemPrint(h, f.RefItem))}");
            if (LightPrint(h, f.L) != LightPrint(h, f.RefLight)) Note($"t {t}: light {LightPrint(h, f.L)} vs reference {LightPrint(h, f.RefLight)}");
            // camera
            var cam = Last<TimelineCameraValue>(f, null, "camera");
            var q = Q(cam.Rotation);
            var expect = V(cam.Target) + q * new Vector3(0f, 0f, -(float)cam.Dist);
            double dcp = (view.Camera.transform.position - expect).magnitude, dcr = Angle(view.Camera.transform.rotation, q);
            maxCamPos = Math.Max(maxCamPos, dcp); maxCamRot = Math.Max(maxCamRot, dcr);
            if (dcp > 1e-4 || dcr > 0.01 || Math.Abs(view.Camera.fieldOfView - (float)cam.Fov) > 1e-4) Note($"t {t}: camera pos {dcp:E2} rot {dcr:F4} fov {view.Camera.fieldOfView}/{cam.Fov}");
            // depth of field
            var dof = Last<TimelineDofValue>(f, null, "effects.depthOfField");
            var inp = HS2Next.HS2DofFeature.Inputs;
            var fp = FocusOf(f, dof.Focus, cam, view);
            if (dof.FocusTo != null) fp = Vector3.LerpUnclamped(fp, FocusOf(f, dof.FocusTo, cam, view), (float)dof.FocusBlend);
            double dfp = (inp.FocusPoint - fp).magnitude;
            maxFocus = Math.Max(maxFocus, dfp);
            float blur = dof.HasBlur && dof.Blur > 0 ? (float)dof.Blur : 0f;
            if (inp.Enabled != dof.Enabled || Math.Abs(inp.FocalSize - (float)dof.FocalSize) > 1e-6 || Math.Abs(inp.Aperture - (float)dof.Aperture) > 1e-6 || inp.MaxBlur1080 != blur || dfp > 1e-4)
                Note($"t {t}: dof {inp.Enabled}/{dof.Enabled} size {inp.FocalSize}/{dof.FocalSize} ap {inp.Aperture}/{dof.Aperture} blur {inp.MaxBlur1080}/{blur} focus {dfp:E2} ({dof.Focus?.Kind}{(dof.FocusTo != null ? " -> " + dof.FocusTo.Kind : "")})");
        }
        Check("TO3 node transforms = the evaluated values (pos 1e-5, rot 0.01 deg, scale 1e-5)", maxPos <= 1e-5 && maxRot <= 0.01 && maxScale <= 1e-5, $"max pos {maxPos:E2}, rot {maxRot:F5} deg, scale {maxScale:E2}");
        Check("TO3 the FK bone = the slot where the Neck group applies (0.01 deg)", maxFk <= 0.01, $"max {maxFk:F5} deg");
        Check("TO3 the camera transform = the camera slot (1e-4, 0.01 deg)", maxCamPos <= 1e-4 && maxCamRot <= 0.01, $"max pos {maxCamPos:E2}, rot {maxCamRot:F5} deg");
        Check("TO3 the focus point of every form (1e-4)", maxFocus <= 1e-4, $"max {maxFocus:E2}");
        Check("TO3 every channel at " + To3Times.Length + " times (face, row, speed, time, colours, camera, depth of field)", bad == 0, string.Join(" | ", notes));
        // each focus form was seen
        var kinds = new HashSet<string>();
        foreach (double t in new[] { 0.0, 1.5, 3.0, 5.0, 6.5 })
        {
            f.Player.Seek(t);
            var d = Last<TimelineDofValue>(f, null, "effects.depthOfField");
            kinds.Add(d.Focus == null ? "pivot" : d.Focus.Kind + (d.Focus.Bone != null ? "+bone" : ""));
        }
        Check("TO3 the dof keys cover the focus forms pivot, node, node + bone, point, distance", kinds.Count == 5, string.Join(", ", kinds));

        // the pinned time re-poses a character at speed 0 ($b: speed 0, the time track pins 0.125 at t 1 and 0.5 at t 4)
        SeekFrame(f, 1.0);
        var pose1 = Pose(h, f.B);
        float t1 = h.Rt.FindChara(f.B).Animation.NormalizedTime;
        SeekFrame(f, 4.0);
        var pose2 = Pose(h, f.B);
        float t2 = h.Rt.FindChara(f.B).Animation.NormalizedTime;
        double moved = 0;
        foreach (var kv in pose1) if (pose2.TryGetValue(kv.Key, out var q2)) moved = Math.Max(moved, Angle(kv.Value, q2));
        Check("TO3 the time slot re-poses a character at speed 0 (a bone turns by more than 0.1 deg between two times)", moved > 0.1 && Math.Abs(t1 - 0.125f) < 1e-5 && Math.Abs(t2 - 0.5f) < 1e-5,
            $"max {moved:F3} deg; times {t1} / {t2}");
        f.H.Frames(5);
        Check("TO3 the pinned time holds over frames", Math.Abs(h.Rt.FindChara(f.B).Animation.NormalizedTime - 0.5f) < 1e-5, h.Rt.FindChara(f.B).Animation.NormalizedTime.ToString("R"));

        // the FK bone where the group does not apply: no effect, effective false
        f.Player.Pause();
        h.Ok(new { cmd = "fk.enable", key = f.A, groups = new[] { "Neck" }, on = false }, "Neck group off");
        h.Frames(2);
        var slot = h.Rt.Overrides.BoneWinner(f.A, "cf_J_Neck");
        Check("TO3 FK group off: the slot wins but is not effective", slot != null && slot.IsWinning && !slot.IsEffective, slot?.ToString());
        Check("TO3 FK group off: the bone does not show the slot", Angle(Bone(h, f.A, "cf_J_Neck").localRotation, new Quaternion((float)slot[0], (float)slot[1], (float)slot[2], (float)slot[3])) > 0.01);
        // a bone the look leaves alone (the Body group off, the Timeline stopped: the model's animation at speed 0): the same
        // with and without a slot
        f.Player.Stop();
        h.Ok(new { cmd = "fk.enable", key = f.A, groups = new[] { "Body" }, on = false }, "Body group off");
        h.Frames(2);
        var arm = h.Rt.Overrides.Acquire("test.groupoff", f.A, "chara.fk.bones[\"cf_J_ArmUp00_R\"]");
        arm.Set(40, 10, 0);
        h.Frames(2);
        var withSlot = Bone(h, f.A, "cf_J_ArmUp00_R").localRotation;
        bool armEffective = arm.IsEffective;
        arm.Release();
        h.Frames(2);
        var withoutSlot = Bone(h, f.A, "cf_J_ArmUp00_R").localRotation;
        Check("TO3 FK group off: the bone = the model's (the same with and without the slot; not effective)", Angle(withSlot, withoutSlot) < 1e-4 && !armEffective,
            $"{Angle(withSlot, withoutSlot):E2} deg, effective {armEffective}");
        h.Ok(new { cmd = "fk.enable", key = f.A, groups = new[] { "Body", "Neck" }, on = true }, "Body and Neck groups on again");
        f.Player.SetTimeline(f.Compiled);   // the released channel is driven again
        SeekFrame(f, 2.0);
    }

    static TimelineChannel FindChannel(OFix f, string node, string path)
    {
        foreach (var c in f.Player.Channels) if (c.NodeId == node && c.Path == path) return c;
        return null;
    }

    static Dictionary<string, Quaternion> Pose(Host h, string chara)
    {
        var d = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        var c = h.Rt.FindChara(chara);
        foreach (var t in c.Root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("cf_J_", StringComparison.Ordinal) && !d.ContainsKey(t.name)) d[t.name] = t.localRotation;
        return d;
    }

    /// <summary>The expected world point of a focus form, computed here (not through SceneEffects).</summary>
    static Vector3 FocusOf(OFix f, DofFocus focus, TimelineCameraValue cam, SceneViewCamera view)
    {
        if (focus == null || focus.Kind == DofFocusKind.Pivot) return V(cam.Target);
        switch (focus.Kind)
        {
            case DofFocusKind.Point: return V(focus.Point);
            case DofFocusKind.Dist: return view.Camera.transform.position + view.Camera.transform.forward * (float)focus.Dist;
            default:
                if (focus.Bone != null) return Bone(f.H, focus.NodeId, focus.Bone).position + (focus.Offset.HasValue ? V(focus.Offset.Value) : Vector3.zero);
                var l = Bone(f.H, focus.NodeId, "cf_J_Eye_t_L");
                var r = Bone(f.H, focus.NodeId, "cf_J_Eye_t_R");
                return (l.position + r.position) * 0.5f;
        }
    }

    // ------------------------------------------------------------ TO4

    static void TO4()
    {
        var f = Fix();
        var h = f.H;
        f.Player.SetTimeline(f.Compiled);
        SeekFrame(f, 3.6);
        Check("TO4 the Timeline holds slots before the stop", h.Rt.Overrides.Count > 10, h.Rt.Overrides.Count + " slots");
        f.Player.Stop();
        h.Frames(3);
        Check("TO4 stop releases every Timeline slot", h.Rt.Overrides.Count == 0, h.Rt.Overrides.Count + " slots left");
        var model = h.Session.Model;
        var tb = model.Find(f.B).Doc.Transform;
        Check("TO4 the node position is the model's again", (h.Rt.HolderOf(f.B).localPosition - UnityObjects.V(tb.Pos)).magnitude < 1e-6, h.Rt.HolderOf(f.B).localPosition.ToString("F6"));
        var tp = model.Find(f.P).Doc.Transform;
        Check("TO4 the item rotation and scale are the model's again", Angle(h.Rt.HolderOf(f.P).localRotation, Quaternion.Euler(UnityObjects.V(tp.Rot))) < 1e-4 && (h.Rt.HolderOf(f.P).localScale - UnityObjects.V(tp.Scale)).magnitude < 1e-6);
        Check("TO4 the FK bone and the arm show the pose of before (the model's row at its stored time, speed 0)",
            Angle(Bone(h, f.A, "cf_J_Neck").localRotation, f.NeckBefore) < 0.01 && Angle(Bone(h, f.A, "cf_J_ArmUp00_R").localRotation, f.ArmBefore) < 0.01,
            $"neck {Angle(Bone(h, f.A, "cf_J_Neck").localRotation, f.NeckBefore):F4}, arm {Angle(Bone(h, f.A, "cf_J_ArmUp00_R").localRotation, f.ArmBefore):F4} deg");
        var c = h.Rt.FindChara(f.A);
        var st = (CharaNodeState)model.Find(f.A).State;
        Check("TO4 the animation row and speed are the model's", c.Animation.Current != null && c.Animation.Current.Group == 19 && c.Animation.Current.Category == 0 && c.Animation.Speed == st.Anime.Speed,
            (c.Animation.Current?.Group + "/" + c.Animation.Current?.Category) + " speed " + c.Animation.Speed);
        Check("TO4 the time is unpinned", !h.Rt.FindChara(f.B).Animation.TimePinned);
        Check("TO4 the face is the model's", c.Expression.Status.EyesPtn == st.Face.Eyes && c.Expression.Status.MouthPtn == st.Face.Mouth && Math.Abs(c.Expression.Status.EyesOpenMax - st.Face.EyesOpen) < 1e-6,
            $"eyes {c.Expression.Status.EyesPtn}/{st.Face.Eyes} mouth {c.Expression.Status.MouthPtn}/{st.Face.Mouth}");
        Check("TO4 the item's colours are those of before", ItemPrint(h, f.P) == f.ItemPrint, Short(ItemPrint(h, f.P)));
        Check("TO4 the light's colour is that of before", LightPrint(h, f.L) == f.LightPrint, LightPrint(h, f.L) + " vs " + f.LightPrint);
        var view = h.Rt.GetPart<SceneViewCamera>();
        var cur = model.Settings.Camera.Current;
        Vector3 pivot = cur != null ? UnityObjects.V(cur.Pivot) : SceneViewCamera.DefaultPivot;
        var rot = Quaternion.Euler(cur != null ? UnityObjects.V(cur.Rot) : SceneViewCamera.DefaultRot);
        float dist = cur != null ? cur.Distance : SceneViewCamera.DefaultDistance;
        Check("TO4 the view is the model's camera again", (view.Camera.transform.position - (pivot + rot * new Vector3(0, 0, -dist))).magnitude < 1e-3 && !view.Overridden,
            view.Camera.transform.position.ToString("F4"));
        var inp = HS2Next.HS2DofFeature.Inputs;
        var md = model.Settings.Effects.DepthOfField;
        Check("TO4 the depth of field is the model block's (MaxBlur1080 0)", inp.Enabled == md.Enabled && inp.Aperture == md.Aperture && inp.FocalSize == md.FocalSize && inp.MaxBlur1080 == 0f,
            $"enabled {inp.Enabled} aperture {inp.Aperture} blur {inp.MaxBlur1080}");

        // 300 ticks of play leave the model untouched
        var before = model.CanonicalBytes();
        long version = h.Session.Commands.Version;
        bool dirty = h.Session.Commands.IsDirty;
        int batches = 0, histories = 0;
        Action<SceneChangeBatch> onChanged = b => batches++;
        Action<HistoryChangedArgs> onHistory = a => histories++;
        h.Session.Events.Changed += onChanged;
        h.Session.Events.HistoryChanged += onHistory;
        f.Player.Play(0);
        int maxSlots = 0;
        for (int i = 0; i < 300; i++)
        {
            f.Player.Tick(Dt);
            h.App.Frame(Dt);
            maxSlots = Math.Max(maxSlots, h.Rt.Overrides.Count);
        }
        h.Session.Events.Changed -= onChanged;
        h.Session.Events.HistoryChanged -= onHistory;
        var after = model.CanonicalBytes();
        Check("TO4 300 ticks of play: the model bytes unchanged, no change batch, no history change, version and dirty flag unchanged",
            before.SequenceEqual(after) && batches == 0 && histories == 0 && h.Session.Commands.Version == version && h.Session.Commands.IsDirty == dirty && maxSlots > 10,
            $"bytes {(before.SequenceEqual(after) ? "equal" : "differ")}, batches {batches}, history events {histories}, version {version} -> {h.Session.Commands.Version}, slots {maxSlots}, events fired {f.Sink.Fired}");
        f.Player.Stop();
        h.Frames(1);
    }

    // ------------------------------------------------------------ TO5

    static void TO5()
    {
        var h = NewHost(0x7055eedUL);
        try
        {
            string a = (string)h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load BR-Chan").Key();
            string p = (string)h.Ok(new { cmd = "item.add", no = 399 }, "item.add 399").Key();
            h.Ok(new { cmd = "chara.anime", key = a, group = 19, category = 0, no = 0, speed = 0 }, "Idle_00 at speed 0");
            h.Ok(new { cmd = "fk.enable", key = a, on = true }, "FK on");
            h.Ok(new { cmd = "fk.enable", key = a, groups = new[] { "Body", "Neck" }, on = true }, "FK groups Body and Neck");
            h.Frames(2);
            var ov = h.Rt.Overrides;
            var neck = ov.Acquire("test.to5", a, "chara.fk.bones[\"cf_J_Neck\"]");
            neck.Set(20, 30, 0);
            var eyes = ov.Acquire("test.to5", a, "chara.face.eyesOpen");
            eyes.Set(0.25);
            var spd = ov.Acquire("test.to5", a, "chara.anime.speed");
            spd.Set(0.5);
            var pos = ov.Acquire("test.to5", p, "transform.pos");
            pos.Set(1, 2, 3);
            h.Frames(2);
            var q = Quaternion.Euler(20, 30, 0);
            Check("TO5 before the rebuild: the slots show", Angle(Bone(h, a, "cf_J_Neck").localRotation, q) < 0.01 && Math.Abs(h.Rt.FindChara(a).Expression.Status.EyesOpenMax - 0.25f) < 1e-6);
            var root0 = h.Rt.FindChara(a).Root;
            h.Ok(new { cmd = "chara.change", key = a, path = maleRel, sex = "male" }, "chara.change BR-Chan -> HERO107 (a rebuild)");
            h.Frames(3);
            var c = h.Rt.FindChara(a);
            Check("TO5 the character was rebuilt", c.Root != null && c.Root != root0);
            Check("TO5 the rebuild keeps the slots", !neck.IsReleased && !eyes.IsReleased && !spd.IsReleased && neck.IsWinning);
            Check("TO5 the FK slot is bound to the new body and shown", neck.IsEffective && Angle(Bone(h, a, "cf_J_Neck").localRotation, q) < 0.01,
                $"{Angle(Bone(h, a, "cf_J_Neck").localRotation, q):F4} deg");
            Check("TO5 the face and speed slots are applied again", Math.Abs(c.Expression.Status.EyesOpenMax - 0.25f) < 1e-6 && Math.Abs(c.Animation.Speed - 0.5f) < 1e-6,
                $"eyesOpen {c.Expression.Status.EyesOpenMax}, speed {c.Animation.Speed}");
            // delete / undo
            h.Ok(new { cmd = "delete", key = p }, "delete the item");
            h.Frames(1);
            Check("TO5 delete: the slot stays, its target does not exist", !pos.IsReleased && pos.HasValue && !pos.TargetExists && !pos.IsEffective);
            h.Ok(new { cmd = "undo" }, "undo the delete");
            h.Frames(1);
            Check("TO5 undo: the target exists again and the revived holder shows the slot", pos.TargetExists && (h.Rt.HolderOf(p).localPosition - new Vector3(1, 2, 3)).magnitude < 1e-6,
                h.Rt.HolderOf(p)?.localPosition.ToString("F4"));
            // forgotten
            h.Ok(new { cmd = "delete", key = p }, "delete the item again");
            h.Session.Commands.ClearHistory();
            Check("TO5 NodeForgotten releases the node's slots", pos.IsReleased && !neck.IsReleased);
            // scene.new
            h.Ok(new { cmd = "scene.new" }, "scene.new");
            Check("TO5 scene.new releases every slot", neck.IsReleased && eyes.IsReleased && spd.IsReleased && ov.Count == 0, ov.Count + " slots");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ TO7

    static void TO7()
    {
        var h = NewHost(0x7077eedUL);
        try
        {
            string a = (string)h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load BR-Chan").Key();
            string b = (string)h.Ok(new { cmd = "chara.load", path = maleRel, sex = "male" }, "chara.load HERO107").Key();
            h.Ok(new { cmd = "chara.anime", key = a, group = 11, category = 5, no = 0, speed = 1 }, "dance_00 (BR-Chan)");
            h.Ok(new { cmd = "chara.anime", key = b, group = 11, category = 5, no = 0, speed = 1 }, "dance_00 (HERO107)");
            h.Frames(2);
            var ov = h.Rt.Overrides;
            var spd = ov.Acquire("test.to7", a, "chara.anime.speed");
            spd.Set(2);
            var bone = ov.Acquire("test.to7", b, "chara.fk.bones[\"cf_J_Neck\"]");
            bone.Set(0, 20, 0);
            h.Frames(30);
            var sa = (CharaNodeState)h.Session.Model.Find(a).State;
            var sb = (CharaNodeState)h.Session.Model.Find(b).State;
            float timeA0 = sa.Anime.Time, timeB0 = sb.Anime.Time;
            string neckB0 = Neck(sb), eyesB0 = Eyes(sb);
            h.Rt.CaptureNow();
            Check("TO7 a character with an animation slot keeps its stored chara.anime.time", sa.Anime.Time == timeA0, sa.Anime.Time + " vs " + timeA0);
            Check("TO7 a character with an FK bone slot keeps its stored look (neckFix, eyeRotations)", Neck(sb) == neckB0 && Eyes(sb) == eyesB0);
            Check("TO7 the other values are captured as before (HERO107's animation time)", sb.Anime.Time != timeB0, sb.Anime.Time + " vs " + timeB0);
            spd.Release();
            bone.Release();
            h.Frames(10);
            h.Rt.CaptureNow();
            Check("TO7 without the slots the next capture captures them", sa.Anime.Time != timeA0, sa.Anime.Time + " vs " + timeA0);
        }
        finally { h.Dispose(); }
    }

    static string Neck(CharaNodeState s) => s.Look?.NeckFix == null ? "null" : string.Join(";", s.Look.NeckFix.Select(kv => kv.Key + "=" + kv.Value.X.ToString("R") + "," + kv.Value.Y.ToString("R") + "," + kv.Value.Z.ToString("R") + "," + kv.Value.W.ToString("R")));
    static string Eyes(CharaNodeState s) => s.Look?.EyeRotations == null ? "null" : s.Look.EyeRotations.Left.X.ToString("R") + "," + s.Look.EyeRotations.Left.Y.ToString("R") + "," + s.Look.EyeRotations.Right.X.ToString("R");
}
