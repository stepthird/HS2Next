// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// engine-free tests of the sequencer's model: the JSON form and its validation (SQ1), interpolation (SQ2), the clock
// with crossings, loop, pause, seek and step (SQ3), and the status counters (SQ4). Each group is a static class with
// Run(TestRunner), found by the plain-Mono test entry by namespace.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save.Tests;
using Newtonsoft.Json.Linq;

namespace Stp3.Samples.Sequencer.Tests
{
    static class Seqs
    {
        /// <summary>A sequence text with single quotes for readability.</summary>
        public static string J(string s) => s.Replace('\'', '"');

        public static Sequence Parse(string singleQuoted) => Sequence.Parse(J(singleQuoted));

        /// <summary>Checks that the text is refused with a problem at <paramref name="path"/>.</summary>
        public static void Bad(TestRunner t, string singleQuoted, string path, string what)
        {
            try
            {
                Sequence.Parse(J(singleQuoted));
                t.Check(false, what + " (accepted)");
            }
            catch (SequenceException e)
            {
                t.Check(e.Path == path, what + (e.Path == path ? "" : " (path " + e.Path + ": " + e.Problem + ")"));
            }
            catch (Exception e)
            {
                t.Check(false, what + " (threw " + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        public static bool Near(double a, double b, double eps = 1e-9) => Math.Abs(a - b) <= eps;

        /// <summary>Angle difference in degrees, wrapped into [0, 180].</summary>
        public static double AngleDiff(double a, double b)
        {
            double d = Math.Abs((a - b) % 360.0);
            return d > 180 ? 360 - d : d;
        }

        public static bool NearV(Vec3 a, Vec3 b, double eps = 1e-9) => Near(a.X, b.X, eps) && Near(a.Y, b.Y, eps) && Near(a.Z, b.Z, eps);

        public static bool NearAngles(Vec3 a, Vec3 b, double eps = 1e-6) =>
            AngleDiff(a.X, b.X) <= eps && AngleDiff(a.Y, b.Y) <= eps && AngleDiff(a.Z, b.Z) <= eps;

        public static Vec3 V(JToken a) => new Vec3((double)a[0], (double)a[1], (double)a[2]);
    }

    /// <summary>SQ1: the JSON form is canonical and byte-stable; every validation rule refuses with its member path.</summary>
    public static class SequencerJson
    {
        public static void Run(TestRunner t)
        {
            // Canonical text: members in the documented order, defaults written, numbers in round-trip form with ".0".
            var s = Seqs.Parse("{'events':[{'note':'n','do':{'cmd':'obj.rename','key':'a','name':'X','n':3},'t':1}]," +
                "'tracks':[{'keys':[{'rot':[0,0,0],'pos':[1,2,3],'t':0},{'t':2,'pos':[0.5,0.1,-4],'rot':[0,90,0]}],'key':'node-a','kind':'transform'}," +
                "{'kind':'camera','ease':'step','keys':[{'t':0.25,'target':[0,1,0],'rot':[10,20,0],'dist':3,'fov':40}]}],'duration':2}");
            string expected = Seqs.J("{'duration':2.0,'loop':false,'autoplay':false,'tracks':[" +
                "{'kind':'transform','key':'node-a','ease':'linear','keys':[{'t':0.0,'pos':[1.0,2.0,3.0],'rot':[0.0,0.0,0.0]},{'t':2.0,'pos':[0.5,0.1,-4.0],'rot':[0.0,90.0,0.0]}]}," +
                "{'kind':'camera','ease':'step','keys':[{'t':0.25,'target':[0.0,1.0,0.0],'rot':[10.0,20.0,0.0],'dist':3.0,'fov':40.0}]}]," +
                "'events':[{'t':1.0,'do':{'cmd':'obj.rename','key':'a','name':'X','n':3},'note':'n'}]}");
            string text = s.ToJson();
            t.Equal(expected, text, "SQ1 canonical text (order, defaults, numbers, the request's own order and integers)");
            t.Equal(text, Sequence.Parse(text).ToJson(), "SQ1 parse(write) writes the same bytes");
            t.Equal(1, s.Events.Count, "SQ1 one event");
            t.Equal("obj.rename", s.Events[0].Cmd, "SQ1 event command");
            t.Equal(Seqs.J("{'cmd':'obj.rename','key':'a','name':'X','n':3}"), s.Events[0].RequestJson, "SQ1 event request text keeps integers");
            t.Equal(Sequence.DataVersion, 1, "SQ1 data version 1");

            // Awkward doubles keep their value and their text.
            double[] values = { 0.1, 1e-7, 1e20, 0.7999999999999999, 1.0 / 3.0, 123456789.123, 2.5e-300, 359.99999999999994, -0.0 };
            foreach (double v in values)
            {
                string src = "{\"duration\":3600,\"tracks\":[{\"kind\":\"transform\",\"key\":\"k\",\"keys\":[{\"t\":0,\"pos\":[" +
                    v.ToString("R", CultureInfo.InvariantCulture) + ",0,0],\"rot\":[0,0,0]}]}]}";
                var a = Sequence.Parse(src);
                double back = a.Tracks[0].TransformKeys[0].Pos.X;
                t.Check(back.Equals(v == 0 ? 0.0 : v) && BitConverter.DoubleToInt64Bits(back) != BitConverter.DoubleToInt64Bits(-0.0), "SQ1 value " + v.ToString("R", CultureInfo.InvariantCulture) + " kept (-0 becomes 0)");
                string w1 = a.ToJson(), w2 = Sequence.Parse(w1).ToJson();
                t.Equal(w1, w2, "SQ1 byte-stable with " + v.ToString("R", CultureInfo.InvariantCulture));
            }
            t.Equal("0.0", Sequence.NumberText(-0.0), "SQ1 negative zero written as 0.0");
            t.Equal("1.0", Sequence.NumberText(1), "SQ1 integral values carry .0");

            // Minimal sequence: tracks and events optional, flags default false.
            var m = Seqs.Parse("{'duration':0.5,'loop':true,'autoplay':true}");
            t.Equal(Seqs.J("{'duration':0.5,'loop':true,'autoplay':true,'tracks':[],'events':[]}"), m.ToJson(), "SQ1 minimal sequence");
            t.Equal(Seqs.J("{'duration':1.0,'loop':false,'autoplay':false,'tracks':[],'events':[]}"),
                Seqs.Parse("{'duration':1,'loop':null,'tracks':null,'events':null}").ToJson(), "SQ1 null members count as absent");

            t.Check(!Sequence.TryParse("[]", out var none, out string problem) && none == null && problem.StartsWith("sequence:"), "SQ1 TryParse reports the problem");
            t.Check(Sequence.TryParse(text, out var some, out problem) && some != null && problem == null, "SQ1 TryParse accepts");

            // Validation: one case per rule, checked by the member path.
            const string K = "'keys':[{'t':0,'pos':[0,0,0],'rot':[0,0,0]}]";
            Seqs.Bad(t, "{'duration':1", "", "SQ1 not JSON");
            Seqs.Bad(t, "{'duration':1} {}", "", "SQ1 content after the value");
            Seqs.Bad(t, "[1]", "", "SQ1 not an object");
            Seqs.Bad(t, "{'duration':1,'duration':2}", "", "SQ1 duplicate member");
            Seqs.Bad(t, "{'loop':true}", "duration", "SQ1 duration missing");
            Seqs.Bad(t, "{'duration':0}", "duration", "SQ1 duration 0");
            Seqs.Bad(t, "{'duration':-1}", "duration", "SQ1 duration negative");
            Seqs.Bad(t, "{'duration':3600.5}", "duration", "SQ1 duration over 3600");
            Seqs.Bad(t, "{'duration':'1'}", "duration", "SQ1 duration a string");
            Seqs.Bad(t, "{'duration':NaN}", "duration", "SQ1 duration NaN");
            t.Check(Sequence.TryParse("{\"duration\":3600}", out _, out _), "SQ1 duration 3600 accepted");
            Seqs.Bad(t, "{'duration':1,'loop':1}", "loop", "SQ1 loop not a bool");
            Seqs.Bad(t, "{'duration':1,'autoplay':'yes'}", "autoplay", "SQ1 autoplay not a bool");
            Seqs.Bad(t, "{'duration':1,'speed':2}", "speed", "SQ1 unknown member");
            Seqs.Bad(t, "{'duration':1,'tracks':{}}", "tracks", "SQ1 tracks not an array");
            Seqs.Bad(t, "{'duration':1,'tracks':[1]}", "tracks[0]", "SQ1 track not an object");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'light'," + K + "}]}", "tracks[0].kind", "SQ1 unknown track kind");
            Seqs.Bad(t, "{'duration':1,'tracks':[{" + K + "}]}", "tracks[0].kind", "SQ1 track kind missing");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform'," + K + "}]}", "tracks[0].key", "SQ1 transform key missing");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':''," + K + "}]}", "tracks[0].key", "SQ1 empty key");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':5," + K + "}]}", "tracks[0].key", "SQ1 key not a string");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a'," + K + "},{'kind':'transform','key':'a'," + K + "}]}", "tracks[1].key", "SQ1 two tracks for one node");
            const string CK = "'keys':[{'t':0,'target':[0,0,0],'rot':[0,0,0],'dist':1,'fov':30}]";
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'camera'," + CK + "},{'kind':'camera'," + CK + "}]}", "tracks[1]", "SQ1 two camera tracks");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'camera','key':'a'," + CK + "}]}", "tracks[0].key", "SQ1 camera track with a key");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','ease':'cubic'," + K + "}]}", "tracks[0].ease", "SQ1 unknown ease");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a'}]}", "tracks[0].keys", "SQ1 keys missing");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[]}]}", "tracks[0].keys", "SQ1 no key");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0.5,'pos':[0,0,0],'rot':[0,0,0]},{'t':0.25,'pos':[0,0,0],'rot':[0,0,0]}]}]}",
                "tracks[0].keys[1].t", "SQ1 keys not ascending");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0.5,'pos':[0,0,0],'rot':[0,0,0]},{'t':0.5,'pos':[0,0,0],'rot':[0,0,0]}]}]}",
                "tracks[0].keys[1].t", "SQ1 two keys at one time");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':-0.1,'pos':[0,0,0],'rot':[0,0,0]}]}]}", "tracks[0].keys[0].t", "SQ1 key before 0");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':1.5,'pos':[0,0,0],'rot':[0,0,0]}]}]}", "tracks[0].keys[0].t", "SQ1 key after the duration");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'pos':[0,0,0],'rot':[0,0,0]}]}]}", "tracks[0].keys[0].t", "SQ1 key time missing");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0,'pos':[0,0],'rot':[0,0,0]}]}]}", "tracks[0].keys[0].pos", "SQ1 pos of two numbers");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0,'pos':[0,'1',0],'rot':[0,0,0]}]}]}", "tracks[0].keys[0].pos[1]", "SQ1 pos with a string");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0,'pos':[0,0,0],'rot':[0,Infinity,0]}]}]}", "tracks[0].keys[0].rot[1]", "SQ1 rot not finite");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0,'pos':[0,0,0]}]}]}", "tracks[0].keys[0].rot", "SQ1 rot missing");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0,'pos':[0,0,0],'rot':[0,0,0],'scale':[1,1,1]}]}]}", "tracks[0].keys[0].scale", "SQ1 unknown key member");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'camera','keys':[{'t':0,'pos':[0,0,0],'rot':[0,0,0],'dist':1,'fov':30}]}]}", "tracks[0].keys[0].pos", "SQ1 camera key with pos");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'camera','keys':[{'t':0,'target':[0,0,0],'rot':[0,0,0],'dist':-1,'fov':30}]}]}", "tracks[0].keys[0].dist", "SQ1 negative distance");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'camera','keys':[{'t':0,'target':[0,0,0],'rot':[0,0,0],'dist':1,'fov':0}]}]}", "tracks[0].keys[0].fov", "SQ1 fov 0");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'camera','keys':[{'t':0,'target':[0,0,0],'rot':[0,0,0],'dist':1,'fov':180}]}]}", "tracks[0].keys[0].fov", "SQ1 fov 180");
            Seqs.Bad(t, "{'duration':1,'tracks':[{'kind':'camera','keys':[{'t':0,'target':[0,0,0],'rot':[0,0,0],'fov':30}]}]}", "tracks[0].keys[0].dist", "SQ1 dist missing");

            // Size limits: 64 tracks, 4096 keys in all, 1024 events.
            t.Check(Sequence.TryParse(Tracks(64, 1), out _, out _), "SQ1 64 tracks accepted");
            Seqs.Bad(t, Tracks(65, 1), "tracks", "SQ1 65 tracks");
            t.Check(Sequence.TryParse(Tracks(64, 64), out var full, out _) && full.KeyCount == 4096, "SQ1 4096 keys accepted");
            Seqs.Bad(t, Tracks(2, 2049), "tracks[1].keys", "SQ1 4098 keys");
            t.Check(Sequence.TryParse(Seqs.J(Events(1024, "{'cmd':'ping'}")), out var ev, out _) && ev.Events.Count == 1024, "SQ1 1024 events accepted");
            Seqs.Bad(t, Events(1025, "{'cmd':'ping'}"), "events", "SQ1 1025 events");

            // Events.
            Seqs.Bad(t, "{'duration':1,'events':[{'t':2,'do':{'cmd':'ping'}}]}", "events[0].t", "SQ1 event after the duration");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':-1,'do':{'cmd':'ping'}}]}", "events[0].t", "SQ1 event before 0");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0}]}", "events[0].do", "SQ1 event without a request");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':'ping'}]}", "events[0].do", "SQ1 request not an object");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'key':'a'}}]}", "events[0].do.cmd", "SQ1 request without cmd");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'Obj.Rename'}}]}", "events[0].do.cmd", "SQ1 invalid command name");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'seq.play'}}]}", "events[0].do.cmd", "SQ1 event calling the sequencer");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'ping','id':7}}]}", "events[0].do.id", "SQ1 request with an id");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'batch'}}]}", "events[0].do.cmds", "SQ1 batch without requests");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'batch','cmds':[{'cmd':'ping'},{'cmd':'seq.stop'}]}}]}", "events[0].do.cmds[1].cmd", "SQ1 batch calling the sequencer");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'batch','cmds':[{'cmd':'batch','cmds':[]}]}}]}", "events[0].do.cmds[0].cmd", "SQ1 batch in a batch");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'obj.transform','pos':[0,NaN,0]}}]}", "events[0].do.pos[1]", "SQ1 NaN inside a request");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'ping'},'note':3}]}", "events[0].note", "SQ1 note not a string");
            Seqs.Bad(t, "{'duration':1,'events':[{'t':0,'do':{'cmd':'ping'},'when':3}]}", "events[0].when", "SQ1 unknown event member");
            t.Check(Sequence.TryParse(Seqs.J("{'duration':1,'events':[{'t':0,'do':{'cmd':'batch','cmds':[{'cmd':'ping'},{'cmd':'scene'}]}}]}"), out _, out _),
                "SQ1 batch of other commands accepted");
            t.Check(Sequence.IsCommandName("scene.new") && Sequence.IsCommandName("ping") && !Sequence.IsCommandName("scene..new") &&
                    !Sequence.IsCommandName("1scene") && !Sequence.IsCommandName(new string('a', 65)), "SQ1 command name grammar");
        }

        static string Tracks(int tracks, int keysEach)
        {
            var sb = new System.Text.StringBuilder("{\"duration\":3600,\"tracks\":[");
            for (int i = 0; i < tracks; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"kind\":\"transform\",\"key\":\"n").Append(i).Append("\",\"keys\":[");
                for (int k = 0; k < keysEach; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append("{\"t\":").Append(k).Append(",\"pos\":[0,0,0],\"rot\":[0,0,0]}");
                }
                sb.Append("]}");
            }
            return sb.Append("]}").ToString();
        }

        static string Events(int n, string request)
        {
            var sb = new System.Text.StringBuilder("{'duration':10,'events':[");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{'t':0,'do':").Append(request).Append('}');
            }
            return sb.Append("]}").ToString();
        }
    }

    /// <summary>SQ2: linear and step keys, rotations along the shortest arc (through 0° / 360°), the camera track.</summary>
    public static class SequencerInterp
    {
        public static void Run(TestRunner t)
        {
            var s = Seqs.Parse("{'duration':10,'tracks':[" +
                "{'kind':'transform','key':'a','keys':[{'t':1,'pos':[0,0,0],'rot':[0,-30,0]},{'t':3,'pos':[2,4,-6],'rot':[0,30,0]},{'t':7,'pos':[10,0,0],'rot':[0,350,0]},{'t':9,'pos':[10,0,0],'rot':[0,10,0]}]}," +
                "{'kind':'transform','key':'b','ease':'step','keys':[{'t':0,'pos':[1,1,1],'rot':[5,6,7]},{'t':2,'pos':[2,2,2],'rot':[8,9,10]}]}," +
                "{'kind':'camera','keys':[{'t':0,'target':[0,1,0],'rot':[10,0,0],'dist':2,'fov':30},{'t':4,'target':[4,1,0],'rot':[10,90,0],'dist':6,'fov':50}]}]}");
            var a = s.Tracks[0];
            var b = s.Tracks[1];
            var cam = s.Tracks[2];

            // Linear positions; holds before the first and after the last key; keys as authored.
            var p = Interp.EvalTransform(a, 0);
            t.Check(Seqs.NearV(p.Pos, new Vec3(0, 0, 0)) && Seqs.NearV(p.Rot, new Vec3(0, -30, 0)), "SQ2 before the first key: the first key as authored");
            p = Interp.EvalTransform(a, 1);
            t.Check(Seqs.NearV(p.Rot, new Vec3(0, -30, 0)), "SQ2 at a key: its Euler angles unchanged (-30 stays -30)");
            p = Interp.EvalTransform(a, 2);
            t.Check(Seqs.NearV(p.Pos, new Vec3(1, 2, -3)), "SQ2 linear position at the middle");
            t.Check(Seqs.NearAngles(p.Rot, new Vec3(0, 0, 0)) && p.Rot.Y >= 0 && p.Rot.Y < 360, "SQ2 rotation -30 -> 30 at the middle is 0, written in [0, 360)");
            p = Interp.EvalTransform(a, 2.5);
            t.Check(Seqs.NearV(p.Pos, new Vec3(1.5, 3, -4.5)) && Seqs.NearAngles(p.Rot, new Vec3(0, 15, 0)), "SQ2 at a quarter before the key: position and 15 degrees");
            p = Interp.EvalTransform(a, 5);
            t.Check(Seqs.NearV(p.Pos, new Vec3(6, 2, -3)), "SQ2 second segment found");
            t.Check(Seqs.NearAngles(p.Rot, new Vec3(0, 10, 0)), "SQ2 30 -> 350 takes the short way (-40 degrees): 10 at the middle");
            p = Interp.EvalTransform(a, 8);
            t.Check(Seqs.NearAngles(p.Rot, new Vec3(0, 0, 0)), "SQ2 350 -> 10 turns 20 degrees through 0");
            p = Interp.EvalTransform(a, 7.5);
            t.Check(Seqs.NearAngles(p.Rot, new Vec3(0, 355, 0)) && Seqs.Near(p.Rot.Y, 355, 1e-6), "SQ2 350 -> 10 at a quarter: 355");
            p = Interp.EvalTransform(a, 8.5);
            t.Check(Seqs.Near(p.Rot.Y, 5, 1e-6), "SQ2 350 -> 10 at three quarters: 5");
            p = Interp.EvalTransform(a, 9.5);
            t.Check(Seqs.NearV(p.Pos, new Vec3(10, 0, 0)) && Seqs.NearV(p.Rot, new Vec3(0, 10, 0)), "SQ2 after the last key: the last key as authored");

            // Step keys hold the earlier key until the next key's time.
            var q = Interp.EvalTransform(b, 1.999);
            t.Check(Seqs.NearV(q.Pos, new Vec3(1, 1, 1)) && Seqs.NearV(q.Rot, new Vec3(5, 6, 7)), "SQ2 step holds the earlier key");
            q = Interp.EvalTransform(b, 2);
            t.Check(Seqs.NearV(q.Pos, new Vec3(2, 2, 2)) && Seqs.NearV(q.Rot, new Vec3(8, 9, 10)), "SQ2 step switches at the next key's time");
            Interp.Segment(b, 1, out double ub);
            t.Equal(0.0, ub, "SQ2 step blend is 0");

            // Camera: target, distance and fov linear; rotation along the short arc.
            var c = Interp.EvalCamera(cam, 1);
            t.Check(Seqs.NearV(c.Target, new Vec3(1, 1, 0)) && Seqs.Near(c.Dist, 3) && Seqs.Near(c.Fov, 35), "SQ2 camera target, distance and fov linear");
            var expect = Quat.Slerp(Quat.FromEuler(new Vec3(10, 0, 0)), Quat.FromEuler(new Vec3(10, 90, 0)), 0.25);
            t.Check(Quat.AngleBetween(Quat.FromEuler(c.Rot), expect) < 1e-6, "SQ2 camera rotation is the slerp of its keys");
            c = Interp.EvalCamera(cam, 4);
            t.Check(Seqs.NearV(c.Rot, new Vec3(10, 90, 0)) && Seqs.Near(c.Dist, 6) && Seqs.Near(c.Fov, 50), "SQ2 camera at its last key");

            // Quaternion conventions: z, then x, then y.
            double h = Math.Sqrt(0.5);
            t.Check(NearQ(Quat.FromEuler(new Vec3(90, 0, 0)), new Quat(h, 0, 0, h)), "SQ2 Euler (90,0,0) about x");
            t.Check(NearQ(Quat.FromEuler(new Vec3(0, 90, 0)), new Quat(0, h, 0, h)), "SQ2 Euler (0,90,0) about y");
            t.Check(NearQ(Quat.FromEuler(new Vec3(0, 0, 90)), new Quat(0, 0, h, h)), "SQ2 Euler (0,0,90) about z");
            t.Check(NearQ(Quat.FromEuler(new Vec3(30, 45, 60)), new Quat(0.3919038, 0.2005621, 0.3604234, 0.8223632), 1e-6), "SQ2 Euler (30,45,60) composes z, x, y");
            t.Check(Seqs.NearAngles(Quat.FromEuler(new Vec3(30, 45, 60)).ToEuler(), new Vec3(30, 45, 60)), "SQ2 (30,45,60) back to the same angles");

            // Round trips over a deterministic set of angles, gimbal lock included.
            var rnd = new Random(51);
            double worst = 0;
            for (int i = 0; i < 2000; i++)
            {
                var e = new Vec3(rnd.NextDouble() * 720 - 360, rnd.NextDouble() * 720 - 360, rnd.NextDouble() * 720 - 360);
                if (i % 10 == 0) e = new Vec3(i % 20 == 0 ? 90 : -90, e.Y, e.Z);
                var qe = Quat.FromEuler(e);
                var back = qe.ToEuler();
                worst = Math.Max(worst, Quat.AngleBetween(qe, Quat.FromEuler(back)));
                if (back.X < 0 || back.X >= 360 || back.Y < 0 || back.Y >= 360 || back.Z < 0 || back.Z >= 360) { t.Check(false, "SQ2 Euler out of [0, 360) for " + e); break; }
            }
            t.Check(worst < 1e-6, "SQ2 Euler -> quaternion -> Euler gives the same rotation (worst " + worst.ToString("G3", CultureInfo.InvariantCulture) + " degrees over 2000)");

            // Slerp keeps a constant angular speed.
            var qa = Quat.FromEuler(new Vec3(10, 20, 30));
            var qb = Quat.FromEuler(new Vec3(-40, 100, 5));
            double total = Quat.AngleBetween(qa, qb);
            bool steady = true;
            for (int k = 1; k < 10; k++)
            {
                double u = k / 10.0;
                steady &= Math.Abs(Quat.AngleBetween(qa, Quat.Slerp(qa, qb, u)) - u * total) < 1e-6;
            }
            t.Check(steady && total > 1, "SQ2 slerp angle grows linearly with u");
            var nearly = Quat.Slerp(qa, Quat.FromEuler(new Vec3(10, 20, 30.001)), 0.5);
            t.Check(Quat.AngleBetween(qa, nearly) < 0.001, "SQ2 nearly equal rotations blend without division by zero");
        }

        static bool NearQ(Quat a, Quat b, double eps = 1e-9)
        {
            // q and -q are the same rotation.
            bool same = Math.Abs(a.X - b.X) <= eps && Math.Abs(a.Y - b.Y) <= eps && Math.Abs(a.Z - b.Z) <= eps && Math.Abs(a.W - b.W) <= eps;
            bool neg = Math.Abs(a.X + b.X) <= eps && Math.Abs(a.Y + b.Y) <= eps && Math.Abs(a.Z + b.Z) <= eps && Math.Abs(a.W + b.W) <= eps;
            return same || neg;
        }
    }

    /// <summary>SQ3: crossings with loop wrap, the scene pause (a zero frame delta), seek (no events) and steps (count × dt).</summary>
    public static class SequencerClock
    {
        public static void Run(TestRunner t)
        {
            // The clock alone.
            double[] times = { 0, 0.5, 1.0, 1.95, 2.0 };
            int[] order = { 0, 1, 2, 3, 4 };
            var fired = new List<int>();
            double now = PlayClock.Advance(0, 0.5, 2, false, true, times, order, fired, out bool ended);
            t.Check(now == 0.5 && !ended && Same(fired, 0, 1), "SQ3 first advance includes the start: events at 0 and 0.5");
            fired.Clear();
            now = PlayClock.Advance(0.5, 0.5, 2, false, false, times, order, fired, out ended);
            t.Check(now == 1.0 && Same(fired, 2), "SQ3 later advances exclude the start: (0.5, 1.0]");
            fired.Clear();
            now = PlayClock.Advance(1.0, 5, 2, false, false, times, order, fired, out ended);
            t.Check(now == 2.0 && ended && Same(fired, 3, 4), "SQ3 without loop the time stops at the duration, events at the end fire, ended");
            fired.Clear();
            now = PlayClock.Advance(1.9, 0.2, 2, true, false, new double[] { 0, 0.05, 0.15, 1.95, 2.0 }, order, fired, out ended);
            t.Check(Seqs.Near(now, 0.1, 1e-12) && !ended && Same(fired, 3, 4, 0, 1), "SQ3 loop wrap covers both segments: 1.95, 2.0, then 0, 0.05 (not 0.15)");
            fired.Clear();
            now = PlayClock.Advance(0.5, 3 * 2 + 0.25, 2, true, false, times, order, fired, out ended);
            t.Check(Seqs.Near(now, 0.75, 1e-12) && fired.Count == 15 && fired[0] == 2,
                "SQ3 three wraps in one advance fire every pass's events: 3 + 5 + 5 + 2 (" + fired.Count + ")");
            fired.Clear();
            now = PlayClock.Advance(0, 1000.5, 1, true, false, new double[] { 0.5 }, new[] { 0 }, fired, out ended);
            t.Check(Seqs.Near(now, 0.5, 1e-9) && fired.Count == PlayClock.MaxWraps + 1, "SQ3 wraps beyond the cap only move the time (" + fired.Count + " fired)");
            fired.Clear();
            now = PlayClock.Advance(2, 1, 2, true, false, times, order, fired, out ended);
            t.Check(Seqs.Near(now, 1.0) && Same(fired, 0, 1, 2), "SQ3 a loop at its end wraps to 0 first");

            // The player: events at their times, once each, through queued calls.
            var host = new TestHost();
            var pl = new Player(host);
            var s = Seqs.Parse("{'duration':1,'tracks':[{'kind':'transform','key':'a','keys':[{'t':0,'pos':[0,0,0],'rot':[0,0,0]},{'t':1,'pos':[60,0,0],'rot':[0,90,0]}]}]," +
                "'events':[{'t':0.5,'do':{'cmd':'ping','tag':'half'}},{'t':0,'do':{'cmd':'ping','tag':'start'}},{'t':1,'do':{'cmd':'ping','tag':'end'}},{'t':0.5,'do':{'cmd':'ping','tag':'half2'}}]}");
            pl.SetSequence(s);
            t.Check(pl.State == PlayState.Stopped && pl.Time == 0 && pl.PendingApply, "SQ3 a new sequence: stopped at 0 with a pending apply");
            pl.Update(1 / 60.0);
            t.Check(host.Calls.Count == 1 && !pl.PendingApply && host.Sent.Count == 0, "SQ3 the pending apply writes the pose once, no event while stopped");
            var first = host.LastTransform("a");
            t.Check(first != null && (bool)first["undo"] == false && Seqs.NearV(Seqs.V(first["pos"]), new Vec3(0, 0, 0)), "SQ3 writes carry undo:false");
            pl.Update(1 / 60.0);
            t.Equal(1, host.Calls.Count, "SQ3 nothing written while stopped without a pending apply");

            pl.Play();
            var firedAt = new Dictionary<string, int>();
            int frame = 0;
            while (pl.State == PlayState.Playing && frame < 100)
            {
                int before = host.Sent.Count;
                pl.Update(0.125);
                frame++;
                foreach (var tag in host.SentTags(before)) firedAt[tag] = frame;
            }
            t.Check(pl.State == PlayState.Ended && pl.Time == 1.0, "SQ3 the sequence ends at its duration");
            t.Equal(4, host.Sent.Count, "SQ3 every event sent once");
            t.Check(Get(firedAt, "start") == 1 && Get(firedAt, "half") == 4 && Get(firedAt, "half2") == 4 && Get(firedAt, "end") == 8,
                "SQ3 events at 0, 0.5 (two, in their order) and 1.0 fire in frames 1, 4, 4, 8 of 0.125 s");
            t.Check(host.SentTags(0).IndexOf("half") < host.SentTags(0).IndexOf("half2"), "SQ3 equal times keep the written order");
            var last = host.LastTransform("a");
            t.Check(Seqs.NearV(Seqs.V(last["pos"]), new Vec3(60, 0, 0)) && Seqs.NearV(Seqs.V(last["rot"]), new Vec3(0, 90, 0)), "SQ3 the last keys are written at the end");
            int calls = host.Calls.Count;
            pl.Update(0.1);
            t.Check(host.Calls.Count == calls && pl.State == PlayState.Ended, "SQ3 an ended sequence holds and writes nothing more");

            // Play again from the end restarts at 0 (the event at 0 fires again).
            int sent = host.Sent.Count;
            pl.Play();
            pl.Update(0.05);
            t.Check(pl.Time == 0.05 && host.SentTags(sent).Contains("start"), "SQ3 play after the end starts over at 0");

            // The scene pause: a zero frame delta holds the time and writes nothing.
            calls = host.Calls.Count;
            pl.Update(0);
            pl.Update(0);
            t.Check(pl.Time == 0.05 && host.Calls.Count == calls && pl.State == PlayState.Playing, "SQ3 the scene pause holds the time, no writes");
            // The sequencer's own pause.
            pl.Pause();
            pl.Update(0.1);
            t.Check(pl.State == PlayState.Paused && pl.Time == 0.05 && host.Calls.Count == calls, "SQ3 seq pause holds the time");

            // Seek: no events, a pending apply at the new time, playback goes on from there (exclusive).
            sent = host.Sent.Count;
            pl.Seek(0.5);
            t.Check(pl.State == PlayState.Paused && pl.Time == 0.5 && pl.PendingApply, "SQ3 seek while paused moves the time, stays paused");
            pl.Update(0.1);
            t.Check(host.Sent.Count == sent && pl.Time == 0.5, "SQ3 seek fires no event");
            t.Check(Seqs.Near((double)host.LastTransform("a")["pos"][0], 30, 1e-9), "SQ3 the pose at the seek time is written");
            pl.Play();
            pl.Update(0.1);
            t.Check(Seqs.Near(pl.Time, 0.6, 1e-12) && host.Sent.Count == sent, "SQ3 resuming after a seek to an event's time does not fire it");
            pl.Seek(0.2);
            t.Check(pl.State == PlayState.Playing && pl.Time == 0.2, "SQ3 seek while playing keeps playing");
            pl.Update(0.1);
            t.Check(Seqs.Near(pl.Time, 0.3, 1e-12) && host.Sent.Count == sent, "SQ3 no event between 0.2 and 0.3");
            pl.Seek(5);
            t.Check(pl.Time == 1.0, "SQ3 seek past the end clamps to the duration");
            pl.Seek(-1);
            t.Check(pl.Time == 0.0, "SQ3 seek before 0 clamps to 0");

            // Stop: time 0 and a pending apply of the first pose.
            pl.Stop();
            t.Check(pl.State == PlayState.Stopped && pl.Time == 0 && pl.PendingApply, "SQ3 stop: stopped at 0 with a pending apply");
            pl.Update(0.1);
            t.Check(Seqs.Near((double)host.LastTransform("a")["pos"][0], 0, 1e-12) && pl.Time == 0, "SQ3 stop writes the first pose and holds");

            // Steps while the scene is paused: count × dt, events and pose sent at the next update.
            pl.Play();
            pl.Update(0);
            sent = host.Sent.Count;
            calls = host.Calls.Count;
            pl.Step(4, 0.125);
            t.Check(pl.Time == 0.5 && host.Sent.Count == sent && host.Calls.Count == calls, "SQ3 a step of 4 x 0.125 advances 0.5 and sends nothing yet");
            pl.Update(0);
            var tags = host.SentTags(sent);
            t.Check(tags.Count == 3 && tags[0] == "start" && tags[1] == "half" && tags[2] == "half2", "SQ3 the step's events go out at the next update (0, 0.5, 0.5)");
            t.Check(Seqs.Near((double)host.LastTransform("a")["pos"][0], 30, 1e-9), "SQ3 the step's pose is written at the next update");
            pl.Pause();
            pl.Step(10, 0.1);
            t.Check(Seqs.Near(pl.Time, 0.5, 1e-12), "SQ3 steps do not move a paused sequence");

            // Play from a time.
            sent = host.Sent.Count;
            pl.Play(0.5);
            pl.Update(0.01);
            t.Check(Seqs.Near(pl.Time, 0.51, 1e-12) && host.SentTags(sent).Count == 2, "SQ3 play from 0.5 includes the events at 0.5");

            // Looping in the player: each pass sends its events; the time wraps.
            var loopHost = new TestHost();
            var lp = new Player(loopHost);
            lp.SetSequence(Seqs.Parse("{'duration':1,'loop':true,'events':[{'t':0.25,'do':{'cmd':'ping','tag':'q'}}]}"));
            lp.Play();
            for (int i = 0; i < 24; i++) lp.Update(0.125);
            t.Check(loopHost.Sent.Count == 3 && lp.Time == 0.0,
                "SQ3 three passes of a looping sequence send the event three times (" + loopHost.Sent.Count + ", time " + lp.Time + ")");
            t.Check(lp.State == PlayState.Playing, "SQ3 a looping sequence keeps playing");

            // No sequence: the commands do nothing.
            var empty = new Player(new TestHost());
            empty.Play();
            empty.Update(0.1);
            empty.Seek(1);
            empty.Step(3, 0.1);
            t.Check(empty.State == PlayState.Stopped && empty.Time == 0 && empty.Sequence == null, "SQ3 without a sequence everything is a no-op");
        }

        static int Get(Dictionary<string, int> d, string k) => d.TryGetValue(k, out int v) ? v : -1;

        static bool Same(List<int> got, params int[] expected)
        {
            if (got.Count != expected.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (got[i] != expected[i]) return false;
            return true;
        }
    }

    /// <summary>SQ4: the status counters (writes done / busy / missing node / error, events fired / failed) and the status shape.</summary>
    public static class SequencerStatus
    {
        public static void Run(TestRunner t)
        {
            var host = new TestHost();
            var pl = new Player(host);
            var s = Seqs.Parse("{'duration':2,'tracks':[" +
                "{'kind':'transform','key':'a','keys':[{'t':0,'pos':[0,0,0],'rot':[0,0,0]},{'t':2,'pos':[2,0,0],'rot':[0,0,0]}]}," +
                "{'kind':'transform','key':'b','keys':[{'t':0,'pos':[0,0,0],'rot':[0,0,0]}]}," +
                "{'kind':'camera','keys':[{'t':0,'target':[0,1,0],'rot':[0,0,0],'dist':3,'fov':40}]}]," +
                "'events':[{'t':0.5,'do':{'cmd':'obj.rename','key':'a','name':'A','tag':'e0'}},{'t':1.5,'do':{'cmd':'obj.rename','key':'zz','name':'B','tag':'e1'}}]}");
            pl.SetSequence(s);
            var st = JObject.Parse(pl.StatusJson());
            t.Check((string)st["state"] == "stopped" && (double)st["time"] == 0 && (double)st["duration"] == 2 && (bool)st["loop"] == false,
                "SQ4 status: state, time, duration, loop");
            var tracks = (JArray)st["tracks"];
            t.Check(tracks.Count == 3 && (string)tracks[0]["kind"] == "transform" && (string)tracks[0]["key"] == "a" && (bool)tracks[0]["active"] &&
                    (string)tracks[2]["kind"] == "camera" && tracks[2]["key"] == null, "SQ4 status tracks: kind, key, active");
            t.Check(st["eventsFired"] != null && st["eventErrors"] != null && st["writes"]?["done"] != null && st["writes"]?["busy"] != null &&
                    st["writes"]?["missingNode"] != null && st["writes"]?["error"] != null && st["updateMs"]?["median"] != null && st["updateMs"]?["max"] != null,
                "SQ4 status members present");

            // Done: one per active track per written frame (3 tracks).
            pl.Update(0.016);
            t.Check(pl.Writes.Done == 3 && host.CallCmds().Count == 3 && host.CallCmds()[2] == "camera.set", "SQ4 the pending apply writes 3 tracks (2 obj.transform, 1 camera.set)");
            var cameraReq = JObject.Parse(host.Calls[2]);
            t.Check((bool)cameraReq["undo"] == false && (double)cameraReq["dist"] == 3 && (double)cameraReq["fov"] == 40 && cameraReq["target"] is JArray,
                "SQ4 camera.set carries target, rot, dist, fov, undo:false");

            // Busy: the write is skipped, the apply stays pending and goes through once the scene accepts writes.
            host.Reply = r => r.Contains("\"key\":\"b\"") ? TestHost.Error("obj.transform", "busy", "a scene transaction is open") : null;
            pl.Seek(1);
            pl.Update(0.016);
            t.Check(pl.Writes.Busy == 1 && pl.Writes.Done == 5 && pl.PendingApply, "SQ4 busy counted, apply stays pending");
            host.Reply = null;
            pl.Update(0);
            t.Check(!pl.PendingApply && pl.Writes.Done == 8, "SQ4 the pending apply is written at the next update");

            // Missing node: a not_found write turns the track off and counts once.
            host.Reply = r => r.Contains("\"key\":\"b\"") ? TestHost.Error("obj.transform", "not_found", "no node \\\"b\\\" in the scene") : null;
            pl.Play();
            pl.Update(0.016);
            pl.Update(0.016);
            t.Check(pl.Writes.MissingNode == 1 && !pl.TrackActive(1) && pl.TrackActive(0), "SQ4 a missing node counts once and its track stops");
            int bWrites = 0;
            foreach (var c in host.Calls) if (c.Contains("\"key\":\"b\"")) bWrites++;
            pl.Update(0.016);
            int bWrites2 = 0;
            foreach (var c in host.Calls) if (c.Contains("\"key\":\"b\"")) bWrites2++;
            t.Equal(bWrites, bWrites2, "SQ4 an inactive track is not written");
            t.Check((bool)((JArray)JObject.Parse(pl.StatusJson())["tracks"])[1]["active"] == false, "SQ4 status shows the track inactive");
            host.Reply = null;

            // Node removed (delete) and added back (undo); forgotten counts once as missing.
            pl.NodesAdded(new[] { "b" });
            t.Check(pl.TrackActive(1) && pl.PendingApply, "SQ4 a node that came back reactivates its track with a pending apply");
            pl.NodesRemoved(new[] { "a", "unrelated" });
            t.Check(!pl.TrackActive(0) && pl.TrackActive(1), "SQ4 removing a node turns its track off");
            long missing = pl.Writes.MissingNode;
            pl.NodeForgotten("a");
            pl.NodeForgotten("a");
            t.Check(pl.Writes.MissingNode == missing + 1 && !pl.TrackActive(0), "SQ4 a forgotten node counts once as missing");
            pl.NodesAdded(new[] { "a" });
            t.Check(pl.TrackActive(0), "SQ4 an id added again reactivates the track");

            // Other errors count as error and are logged once per track.
            host.Reply = r => r.Contains("camera.set") ? TestHost.Error("camera.set", "bad_args", "bad argument 'fov'") : null;
            int logs = host.Logs.Count;
            pl.Update(0.016);
            pl.Update(0.016);
            t.Check(pl.Writes.Error == 2 && host.Logs.Count == logs + 1, "SQ4 other write errors counted, logged once per track");
            host.Reply = null;

            // Events: fired counts sends; failed replies count as errors (replies may come later and from another
            // thread) and are logged once per event.
            var eh = new TestHost();
            var ep = new Player(eh);
            ep.SetSequence(Seqs.Parse("{'duration':1,'loop':true,'events':[{'t':0.25,'do':{'cmd':'obj.rename','tag':'ok'}},{'t':0.75,'do':{'cmd':'obj.rename','tag':'bad'}}]}"));
            ep.Play();
            for (int i = 0; i < 25; i++) ep.Update(0.1);
            t.Check(ep.EventsFired == 5 && ep.EventErrors == 0 && eh.Queued.Count == 5, "SQ4 eventsFired counts sent requests before their replies (" + ep.EventsFired + ")");
            var th = new System.Threading.Thread(() => eh.DeliverAll(r => r.Contains("\"bad\"") ? TestHost.Error("obj.rename", "not_found", "no node") : null));
            th.Start();
            th.Join();
            t.Equal(2L, ep.EventErrors, "SQ4 failed event replies counted from another thread");
            int elog = eh.Logs.Count;
            ep.Update(0.1);
            t.Check(eh.Logs.Count == elog + 1 && eh.Logs[eh.Logs.Count - 1].Contains("event 1") && eh.Logs[eh.Logs.Count - 1].Contains("not_found"),
                "SQ4 a failing event is logged once with its index and code");
            for (int i = 0; i < 10; i++) ep.Update(0.1);
            eh.DeliverAll(r => r.Contains("\"bad\"") ? TestHost.Error("obj.rename", "not_found", "no node") : null);
            ep.Update(0.1);
            t.Check(ep.EventErrors == 3 && eh.Logs.Count == elog + 1, "SQ4 later failures of the same event are counted, not logged again");
            var est = JObject.Parse(ep.StatusJson());
            t.Check((long)est["eventsFired"] == ep.EventsFired && (long)est["eventErrors"] == 3, "SQ4 status carries the event counters");

            // Replies of an old sequence do not count against a new one's log.
            ep.SetSequence(Seqs.Parse("{'duration':1}"));
            ep.Update(0.1);
            t.Check((string)JObject.Parse(ep.StatusJson())["state"] == "stopped", "SQ4 a replaced sequence starts stopped");

            // Timings: frames that worked are timed; median and max over the last 600.
            t.Check(pl.UpdateMs.Count > 0 && pl.UpdateMs.Max >= pl.UpdateMs.Median, "SQ4 update times recorded");
            var ring = new TimingRing(4);
            foreach (double v in new[] { 5.0, 1.0, 3.0, 2.0, 4.0 }) ring.Add(v);
            t.Check(ring.Count == 4 && ring.Max == 4.0 && ring.Median == 2.0 && ring.Percentile(0.95) == 4.0, "SQ4 ring keeps the last N; nearest-rank median and p95");
            t.Equal(0, TimingRing.NearestRank(1, 0.95), "SQ4 nearest rank of one sample");

            // No sequence.
            pl.SetSequence(null);
            var none = JObject.Parse(pl.StatusJson());
            t.Check((string)none["state"] == "stopped" && ((JArray)none["tracks"]).Count == 0 && (double)none["duration"] == 0, "SQ4 status without a sequence: stopped, no tracks");
            pl.ResetCounters();
            t.Check(pl.Writes.Done == 0 && pl.EventsFired == 0 && pl.UpdateMs.Count == 0, "SQ4 counters reset");

            // Reply classification.
            t.Check(Player.IsOk("{\"ok\":true,\"cmd\":\"x\"}") && !Player.IsOk("{\"ok\":false,\"code\":\"busy\"}") && Player.IsOk("{ \"ok\" : true }") &&
                    !Player.IsOk("not json") && !Player.IsOk(null), "SQ4 reply ok test");
            t.Equal("busy", Player.Code("{\"ok\":false,\"cmd\":\"x\",\"error\":\"e\",\"code\":\"busy\"}"), "SQ4 reply code");
            t.Equal("", Player.Code("garbage"), "SQ4 unreadable reply has no code");
        }
    }
}
