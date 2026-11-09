// ApiSurfaceTest, group AZ6: the viewport tools of a plugin on a stand-in host, so the row needs neither the UI nor a game
// install. What it pins: the plugin's tool gets its calls in order behind the fault wrapper, an exception is a counted fault
// and ends the tool, a plugin that leaves ends its tool, a tool can end itself from a callback, a pointer's pick is lazy, and
// a tool can do what the Api's shape promises: a press held across moves and the wheel, keys during a press, modifiers, a
// press left to the camera, a tool that only reads the ray, and lines drawn over the view.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Unity;
using HS2Next.App.Plugins;
using HS2Next.App.Plugins.Engine;
using UnityEngine;

public static partial class ApiSurfaceTest
{
    /// <summary>A host with the rules the Api's documents state, and nothing else.</summary>
    sealed class StandInHost : IViewportTools
    {
        public ViewportTool Current;
        public ToolOptions Options;
        public bool Refuse;
        public int Picks;

        public bool Running => Current != null;

        public bool Begin(ViewportTool tool, ToolOptions options = default)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            if (Refuse) return false;
            if (Current != null) Stop(ToolEnd.Replaced);
            Current = tool;
            Options = options;
            tool.Started();
            return true;
        }

        public void End(ViewportTool tool)
        {
            if (tool != null && ReferenceEquals(tool, Current)) Stop(ToolEnd.Finished);
        }

        public void Stop(ToolEnd why)
        {
            var t = Current;
            Current = null;
            t?.Ended(why);
        }

        public ToolPick Pick(Ray ray, ToolPickFilter filter = default)
        {
            Picks++;
            return new ToolPick { Hit = true, NodeId = filter.NodeId, Distance = 2 };
        }
    }

    /// <summary>Writes down every call; throws from the call named by <see cref="ThrowIn"/>.</summary>
    sealed class LogTool : ViewportTool
    {
        public readonly List<string> Log = new List<string>();
        public string ThrowIn;
        public bool Claim = true;
        public Action OnDown;

        void Note(string s)
        {
            Log.Add(s);
            if (ThrowIn != null && s.StartsWith(ThrowIn, StringComparison.Ordinal)) throw new InvalidOperationException("thrown by the test");
        }

        public string Last => Log.Count > 0 ? Log[Log.Count - 1] : "";
        public override void Started() => Note("Started");
        public override bool PointerDown(in ToolPointer p) { Note("PointerDown " + p.Button + " " + p.Mods); OnDown?.Invoke(); return Claim; }
        public override void PointerMove(in ToolPointer p) => Note("PointerMove " + p.Buttons);
        public override void PointerUp(in ToolPointer p) => Note("PointerUp " + p.Button);
        public override void PointerCancel() => Note("PointerCancel");
        public override bool Wheel(float notches, in ToolPointer p) { Note("Wheel " + notches); return true; }
        public override bool Key(string key, ToolMods mods) { Note("Key " + key + " " + mods); return true; }
        public override void Draw(IToolDraw draw) { Note("Draw"); draw?.Line(Vector3.zero, Vector3.up, Color.white); }
        public override void Ended(ToolEnd reason) => Note("Ended " + reason);
    }

    sealed class LineCounter : IToolDraw
    {
        public int Lines, Markers, Labels;
        public void Line(Vector3 a, Vector3 b, Color c) => Lines++;
        public void Marker(Vector3 p, float size, Color c) => Markers++;
        public void Label(Vector3 p, string text) => Labels++;
    }

    /// <summary>A press held across moves and the wheel; keys and modifiers during it; a miss left to the camera.</summary>
    sealed class HoldTool : ViewportTool
    {
        public bool Held;
        public float Depth = 3;
        public string Last;
        public Vector3 Point;
        public readonly List<Vector3> Path = new List<Vector3>();

        public override bool PointerDown(in ToolPointer p)
        {
            var hit = p.Hit;
            if (!hit.Hit) return false;
            Held = true;
            Depth = hit.Distance;
            Last = p.Mods == ToolMods.Ctrl ? "push" : p.Mods == ToolMods.Shift ? "throw" : "hold";
            Point = p.Ray.GetPoint(Depth);
            Path.Add(hit.Point);
            return true;
        }

        public override void PointerMove(in ToolPointer p)
        {
            if (!Held) return;
            Point = p.Ray.GetPoint(Depth);
            Path.Add(p.Hit.Point);
        }

        public override void PointerUp(in ToolPointer p) => Held = false;
        public override bool Wheel(float notches, in ToolPointer p) { if (!Held) return false; Depth += notches; Point = p.Ray.GetPoint(Depth); return true; }
        public override bool Key(string key, ToolMods mods) { if (!Held || key != "Space") return false; Last = key; return true; }
        public override void Draw(IToolDraw draw) { for (int i = 1; i < Path.Count; i++) draw.Line(Path[i - 1], Path[i], Color.white); }
    }

    /// <summary>Reads the ray only.</summary>
    sealed class RayTool : ViewportTool
    {
        public Ray Ray;
        public override bool PointerDown(in ToolPointer p) { Ray = p.Ray; return true; }
    }

    static ToolPointer Ptr(int button = 0, int buttons = 1, ToolMods mods = ToolMods.None, Func<ToolPick> hit = null) =>
        new ToolPointer(new Vector2(10, 20), new Ray(Vector3.zero, Vector3.forward), button, buttons, mods, hit);

    static (StandInHost Host, PluginGuard Guard, PluginViewportTools View) Tools(int threshold = PluginGuard.DefaultThreshold)
    {
        var g = new PluginGuard("probe.tools", null, threshold);
        var h = new StandInHost();
        return (h, g, new PluginViewportTools(g, h));
    }

    static void Az6()
    {
        // a run from the start to the end
        {
            var (h, g, v) = Tools();
            var t = new LogTool();
            bool began = v.Begin(t, new ToolOptions { PassThrough = true, Hint = "plugin.probe.hint" });
            var seen = h.Current;
            Check("AZ6", "Begin starts the tool behind a wrapper and hands the options on",
                began && t.Last == "Started" && seen != null && !ReferenceEquals(seen, t) && h.Options.PassThrough && h.Options.Hint == "plugin.probe.hint" && v.Running, string.Join(",", t.Log));
            var p = Ptr(1, 2, ToolMods.Ctrl);
            bool down = seen.PointerDown(in p);
            seen.PointerMove(in p);
            seen.PointerUp(in p);
            bool wheel = seen.Wheel(1.5f, in p);
            bool key = seen.Key("Space", ToolMods.Shift);
            seen.PointerCancel();
            seen.Draw(new LineCounter());
            Check("AZ6", "every call reaches the plugin's tool with its values",
                down && wheel && key && t.Log.SequenceEqual(new[] { "Started", "PointerDown 1 Ctrl", "PointerMove 2", "PointerUp 1", "Wheel 1.5", "Key Space Shift", "PointerCancel", "Draw" }), string.Join(",", t.Log));
            Check("AZ6", "the fault wrapper counts the depth only during a call", g.Depth == 0 && g.Total == 0, g.Depth + " / " + g.Total);
            v.End(t);
            Check("AZ6", "End gives Ended(Finished), once", t.Last == "Ended Finished" && t.Log.Count(x => x.StartsWith("Ended", StringComparison.Ordinal)) == 1 && h.Current == null && !v.Running, string.Join(",", t.Log));
            int n = t.Log.Count;
            v.End(t);
            v.End(new LogTool());
            Check("AZ6", "End of a tool that does not run does nothing", t.Log.Count == n);
            bool threw = false;
            try { v.Begin(null); }
            catch (ArgumentNullException) { threw = true; }
            Check("AZ6", "Begin(null) throws ArgumentNullException", threw);
            Check("AZ6", "Pick goes to the host with its filter", v.Pick(new Ray(), new ToolPickFilter { NodeId = "n1" }).NodeId == "n1" && h.Picks == 1);
        }

        // refusal, replacement, the host's own endings
        {
            var (h, _, v) = Tools();
            var t = new LogTool();
            h.Refuse = true;
            Check("AZ6", "a refused Begin is false and calls nothing on the tool", !v.Begin(t) && t.Log.Count == 0 && h.Current == null);
            h.Refuse = false;
            var a = new LogTool();
            var b = new LogTool();
            v.Begin(a);
            v.Begin(b);
            Check("AZ6", "a second Begin replaces the first: Ended(Replaced), then Started", a.Last == "Ended Replaced" && b.Last == "Started");
            var other = new PluginViewportTools(new PluginGuard("probe.other", null), h);
            var c = new LogTool();
            other.Begin(c);
            Check("AZ6", "another plugin's tool replaces it too", b.Last == "Ended Replaced" && c.Last == "Started" && v.Running);
            int n = b.Log.Count;
            v.End(b);
            Check("AZ6", "End names a tool of this view: another plugin's tool runs on", b.Log.Count == n && c.Last == "Started" && h.Current != null);
            h.Stop(ToolEnd.SceneChanged);
            Check("AZ6", "the host's reason reaches the tool", c.Last == "Ended SceneChanged");
            var e = new LogTool();
            v.Begin(e);
            h.Stop(ToolEnd.Cancelled);
            Check("AZ6", "Esc's Cancelled reaches the tool", e.Last == "Ended Cancelled");
        }

        // a tool ends itself from a callback
        {
            var (h, _, v) = Tools();
            var t = new LogTool();
            t.OnDown = () => v.End(t);
            v.Begin(t);
            var seen = h.Current;
            var p = Ptr();
            seen.PointerDown(in p);
            Check("AZ6", "End from the tool's own callback ends it once, Finished", t.Log.Count(x => x == "Ended Finished") == 1 && h.Current == null, string.Join(",", t.Log));
        }

        // every callback that throws
        foreach (var name in new[] { "Started", "PointerDown", "PointerMove", "PointerUp", "PointerCancel", "Wheel", "Key", "Draw" })
        {
            var (h, g, v) = Tools();
            var t = new LogTool { ThrowIn = name };
            bool ok = true;
            bool claimed = false;
            try
            {
                v.Begin(t);
                var seen = h.Current;
                if (seen != null)
                {
                    var p = Ptr();
                    switch (name)
                    {
                        case "PointerDown": claimed = seen.PointerDown(in p); break;
                        case "PointerMove": seen.PointerMove(in p); break;
                        case "PointerUp": seen.PointerUp(in p); break;
                        case "PointerCancel": seen.PointerCancel(); break;
                        case "Wheel": claimed = seen.Wheel(1, in p); break;
                        case "Key": claimed = seen.Key("T", ToolMods.None); break;
                        case "Draw": seen.Draw(null); break;
                    }
                }
            }
            catch (Exception e) { ok = false; t.Log.Add("escaped " + e.GetType().Name); }
            Check("AZ6", name + " throws: a counted fault, the tool ends Cancelled, the exception stays out of the host",
                ok && !claimed && g.Total == 1 && g.ByHook().ContainsKey("tool " + name) && t.Last == "Ended Cancelled" && h.Current == null && g.Depth == 0,
                string.Join(",", t.Log) + "; faults " + g.Total + " " + string.Join(",", g.ByHook().Keys));
        }
        {
            var (h, g, v) = Tools();
            var t = new LogTool { ThrowIn = "Ended" };
            v.Begin(t);
            bool ok = true;
            try { v.End(t); }
            catch (Exception) { ok = false; }
            Check("AZ6", "Ended throws: a counted fault, nothing more", ok && g.Total == 1 && h.Current == null && g.ByHook().ContainsKey("tool Ended"));
        }

        // the plugin leaves
        {
            var (h, _, v) = Tools();
            var t = new LogTool();
            v.Begin(t);
            v.Close();
            Check("AZ6", "a plugin that leaves ends its tool with UiClosed", t.Last == "Ended UiClosed" && h.Current == null);
            bool threw = false;
            try { v.Begin(new LogTool()); }
            catch (ObjectDisposedException) { threw = true; }
            v.Close();
            Check("AZ6", "after that Begin throws and a second Close does nothing", threw && t.Log.Count(x => x.StartsWith("Ended", StringComparison.Ordinal)) == 1);
        }
        {
            var (h, _, v) = Tools();
            var t = new LogTool();
            var o = new LogTool();
            var other = new PluginViewportTools(new PluginGuard("probe.other", null), h);
            other.Begin(o);
            v.Close();
            Check("AZ6", "a plugin that leaves with no tool of its own touches no other tool", o.Last == "Started" && h.Current != null);
        }
        {
            var (h, g, v) = Tools(1);
            g.BecameFaulted += v.Close;
            var t = new LogTool { ThrowIn = "PointerDown" };
            v.Begin(t);
            var seen = h.Current;
            var p = Ptr();
            seen.PointerDown(in p);
            Check("AZ6", "the fault that reaches the threshold ends the tool with UiClosed, once", g.Faulted && t.Log.Count(x => x.StartsWith("Ended", StringComparison.Ordinal)) == 1 && t.Last == "Ended UiClosed", string.Join(",", t.Log));
        }

        // the services
        {
            var s = new PluginEngineServices("probe.tools", null, null, null, null, null);
            Check("AZ6", "Tools is null when the UI does not run", s.Tools == null);
            s.Unload();
            bool threw = false;
            try { var x = s.Tools; }
            catch (ObjectDisposedException) { threw = true; }
            Check("AZ6", "Tools of an unloaded plugin throws, as its other services", threw);
        }

        // the pointer's pick
        {
            int runs = 0;
            var pick = new ToolPick { Hit = true };
            var p = Ptr(hit: () => { runs++; return pick; });
            Check("AZ6", "a pointer runs no pick until Hit is read", runs == 0);
            var copy = p;
            bool same = ReferenceEquals(p.Hit, pick) && ReferenceEquals(p.Hit, copy.Hit);
            Check("AZ6", "the first read runs the pick, later reads and copies give the same object", same && runs == 1, runs.ToString());
            var none = Ptr();
            var nul = Ptr(hit: () => null);
            Check("AZ6", "Hit is never null: a pointer with no pick and a pick with no answer read as a miss", none.Hit != null && !none.Hit.Hit && nul.Hit != null && !nul.Hit.Hit);
            var d = default(ToolPointer);
            Check("AZ6", "a default pointer reads as a miss", d.Hit != null && !d.Hit.Hit && d.Button == 0 && d.Mods == ToolMods.None);
        }

        // what the shape promises
        {
            var (h, _, v) = Tools();
            var tool = new HoldTool();
            v.Begin(tool, new ToolOptions { PassThrough = true, Filter = new ToolPickFilter { CharactersOnly = true } });
            var seen = h.Current;
            var miss = Ptr(hit: () => new ToolPick());
            Check("AZ6", "a press that meets nothing is left unclaimed, with PassThrough on", !seen.PointerDown(in miss) && !tool.Held && h.Options.PassThrough && h.Options.Filter.CharactersOnly);
            var hit = Ptr(mods: ToolMods.Ctrl, hit: () => new ToolPick { Hit = true, Distance = 5, Point = Vector3.one });
            bool claimed = seen.PointerDown(in hit);
            Check("AZ6", "a press on a surface is claimed with its modifiers", claimed && tool.Held && tool.Last == "push" && tool.Depth == 5);
            var shift = Ptr(mods: ToolMods.Shift, hit: () => new ToolPick { Hit = true, Distance = 5 });
            tool.PointerUp(in shift);
            seen.PointerDown(in shift);
            Check("AZ6", "the other modifier reads apart", tool.Last == "throw");
            var move = Ptr(hit: () => new ToolPick { Hit = true, Point = Vector3.up });
            bool wheel = seen.Wheel(2, in move);
            seen.PointerMove(in move);
            bool key = seen.Key("Space", ToolMods.None);
            Check("AZ6", "during the press the wheel moves the depth, moves follow the ray and a key is taken",
                wheel && key && tool.Depth == 7 && tool.Point == new Vector3(0, 0, 7) && tool.Last == "Space", tool.Depth + " " + tool.Point);
            var lines = new LineCounter();
            seen.Draw(lines);
            Check("AZ6", "the points of the press can be drawn as lines", lines.Lines == 2, lines.Lines.ToString());
            seen.PointerUp(in move);
            Check("AZ6", "after the press wheel and keys are left unclaimed", !tool.Held && !seen.Wheel(1, in move) && !seen.Key("Space", ToolMods.None));
        }
        {
            int runs = 0;
            var (h, _, v) = Tools();
            var tool = new RayTool();
            v.Begin(tool);
            var p = Ptr(hit: () => { runs++; return null; });
            Check("AZ6", "a tool that reads the ray only runs no pick", h.Current.PointerDown(in p) && runs == 0 && tool.Ray.direction == Vector3.forward);
        }
    }
}
