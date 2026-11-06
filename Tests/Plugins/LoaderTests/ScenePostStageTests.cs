// CX2, CX3, CX4 (15.2) with a fake host: the scene post stage's Unity-free part. A frame of the
// fake host is the frame order of the runtime (characters, items, the stage, late apply); entries in load order stand for the
// host's plugins, each with its own fault wrapper and time window. CX2: once per frame and per step, load order, the frame's
// values, no allocation per call. CX3: 10 faults fault the plugin and the others go on, a disable and an enable keep the
// place in the order, what a pass does to the list, a pass from inside a pass. CX4: no plugin with the interface, no stage.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Plugins;
using HS2Next.Save.Tests;

namespace HS2Next.App.Plugins.Tests
{
    /// <summary>A fake host and its runtime: the plugins in load order and the frame order around the stage.</summary>
    sealed class FakePostHost
    {
        public sealed class Plugin : IPluginScenePost
        {
            public readonly string Name;
            public readonly FakePostHost Host;
            public readonly PluginGuard Guard;
            public readonly PluginTimer Time = new PluginTimer();
            public ScenePostCaller Caller;
            public int Calls, Faults;
            public bool Throws;
            public Action Body;
            public PluginFrame Last;

            public Plugin(FakePostHost host, string name)
            {
                Host = host;
                Name = name;
                Guard = new PluginGuard(name, s => host.Warnings.Add(s));
                Guard.BecameFaulted += () => { Faults++; host.Leave(this); };
                Caller = new ScenePostCaller(this, Guard, Time);
            }

            public void ScenePost(in PluginFrame frame)
            {
                Calls++;
                Last = frame;
                Host.Marks.Add(Name);
                Body?.Invoke();
                if (Throws) throw new InvalidOperationException("post fault of " + Name);
            }
        }

        /// <summary>A handler outside any host.</summary>
        public sealed class Counter : IPluginScenePost
        {
            public int Calls;
            public void ScenePost(in PluginFrame frame) => Calls++;
        }

        public readonly List<string> Marks = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<Plugin> Plugins = new List<Plugin>();
        public readonly ScenePostStage Stage = new ScenePostStage();
        /// <summary>The runtime's call: null while no plugin takes part, as the host hands it over.</summary>
        public Action<float> PostStage;
        public int Frame;
        public double Clock;
        public bool Paused;

        public Plugin Add(string name)
        {
            var p = new Plugin(this, name);
            Plugins.Add(p);
            Sync();
            return p;
        }

        /// <summary>The plugin leaves the stage (a fault or a disable): closed, the list rebuilt from the plugins left.</summary>
        public void Leave(Plugin p)
        {
            p.Caller?.Close();
            p.Caller = null;
            Sync();
        }

        /// <summary>An enable: a new instance in the same place of the order.</summary>
        public Plugin Enable(Plugin old)
        {
            var p = new Plugin(this, old.Name);
            Plugins[Plugins.IndexOf(old)] = p;
            Sync();
            return p;
        }

        void Sync()
        {
            Stage.Set(Plugins.Where(x => x.Caller != null).Select(x => x.Caller));
            PostStage = Stage.Any ? Run : (Action<float>)null;
        }

        void Run(float dt) => Stage.Run(new PluginFrame(Frame, dt, dt, Clock - dt, Paused));

        /// <summary>One frame or one step, in the order of the runtime.</summary>
        public void Motion(float dt)
        {
            Frame++;
            Clock += dt;
            Marks.Add("charas");
            Marks.Add("items");
            PostStage?.Invoke(dt);
            Marks.Add("late");
        }

        public string TakeMarks()
        {
            string s = string.Join(" ", Marks);
            Marks.Clear();
            return s;
        }
    }

    public static class CX2ScenePostOrder
    {
        public static void Run(TestRunner t)
        {
            var h = new FakePostHost();
            var a = h.Add("a");
            var b = h.Add("b");
            var c = h.Add("c");
            t.Equal(3, h.Stage.Count, "three plugins take part");

            h.Motion(0.25f);
            t.Equal("charas items a b c late", h.TakeMarks(), "CX2: after items, before late apply, in load order");
            t.Equal(1, a.Calls, "CX2: once per frame");
            h.Motion(0.25f);
            h.Motion(0.25f);
            t.Equal(3, c.Calls, "CX2: three frames, three calls");
            h.TakeMarks();

            // a frame's values reach the plugin; SceneTime is the time before this frame, as in Update
            t.Equal(3, a.Last.Frame, "the frame number");
            t.Equal(0.25f, a.Last.SceneDt, "the scene delta");
            t.Equal(0.5, a.Last.SceneTime, "SceneTime: the clock before this frame");
            t.Check(!a.Last.Paused, "not paused");

            // a paused frame holds, a step of the paused scene is a pass of its own with the step's delta
            h.Paused = true;
            h.Motion(0f);
            t.Equal(0f, b.Last.SceneDt, "a paused frame: delta 0");
            t.Check(b.Last.Paused, "a paused frame: Paused");
            h.TakeMarks();
            for (int i = 0; i < 4; i++) h.Motion(0.1f);
            t.Equal("charas items a b c late charas items a b c late charas items a b c late charas items a b c late", h.TakeMarks(), "CX2: once per step");
            t.Equal(0.1f, c.Last.SceneDt, "a step: the step's delta");
            t.Check(c.Last.Paused, "a step: Paused true");

            // the time window counts every call
            t.Equal(8, a.Time.Count, "the time window holds each call");
            t.Check(a.Time.Max >= 0 && a.Time.Median >= 0, "the time window's numbers");

            // a call allocates nothing: the frame goes through a field, the closure is made once
            var q = new FakePostHost();
            q.Add("q");
            var f = new PluginFrame(1, 0.1f, 0.1f, 0, false);
            q.Stage.Run(in f);
            q.Marks.Capacity = 200100;
            int gen0 = GC.CollectionCount(0);
            long before = GC.GetTotalMemory(false);
            for (int i = 0; i < 100000; i++) q.Stage.Run(in f);
            long grown = GC.GetTotalMemory(false) - before;
            t.Check(GC.CollectionCount(0) == gen0 && grown < 65536, "CX2: 100,000 calls allocate nothing (" + grown + " B, " + (GC.CollectionCount(0) - gen0) + " collections)");
        }
    }

    public static class CX3ScenePostFaults
    {
        public static void Run(TestRunner t)
        {
            var h = new FakePostHost();
            var a = h.Add("a");
            var b = h.Add("b");
            var c = h.Add("c");
            b.Throws = true;

            for (int i = 0; i < 9; i++) h.Motion(0.1f);
            t.Equal(9, b.Calls, "nine faults: b is still called");
            t.Check(!b.Guard.Faulted, "nine faults: not faulted");
            t.Equal(9, c.Calls, "CX3: a fault stops no other plugin");
            t.Equal(9, b.Guard.ByHook()[ScenePostCaller.Hook], "the faults are counted under scenePost");
            t.Equal(1, h.Warnings.Count, "one log line per hook and exception type");

            h.Motion(0.1f);
            t.Check(b.Guard.Faulted, "CX3: the tenth fault faults the plugin");
            t.Equal(1, b.Faults, "CX3: the host hears it once");
            t.Equal(10, c.Calls, "CX3: the plugin after it runs in that pass");
            t.Check(b.Guard.LastError.StartsWith("scenePost: InvalidOperationException"), "LastError names the hook: " + b.Guard.LastError);
            t.Equal(2, h.Stage.Count, "the faulted plugin left the stage");
            h.TakeMarks();

            h.Motion(0.1f);
            t.Equal("charas items a c late", h.TakeMarks(), "CX3: the others go on, b is not called again");
            t.Equal(10, b.Calls, "b's calls stand at the threshold");

            // a fault that reaches the threshold off the main thread: the host's removal comes a frame later, and the fault
            // wrapper stops the calls before that
            var lone = new FakePostHost.Counter();
            var guard = new PluginGuard("lone", null, threshold: 1);
            var stage = new ScenePostStage();
            stage.Set(new[] { new ScenePostCaller(lone, guard, new PluginTimer()) });
            var f = new PluginFrame(1, 0f, 0f, 0, false);
            stage.Run(in f);
            t.Equal(1, lone.Calls, "a lone plugin is called");
            guard.Count(ScenePostCaller.Hook, new Exception("from another thread"));
            stage.Run(in f);
            t.Equal(1, lone.Calls, "a faulted plugin is not called, whether or not the host removed it yet");

            // an enable: a new instance keeps the place in the order, its fault count starts again
            var b2 = h.Enable(b);
            t.Equal(3, h.Stage.Count, "CX3: an enable: three take part again");
            h.TakeMarks();
            h.Motion(0.1f);
            t.Equal("charas items a b c late", h.TakeMarks(), "CX3: enabled, b is back in its place");
            t.Equal(1, b2.Calls, "the new instance is called");
            t.Equal(0, b2.Guard.Total, "its fault count starts at 0");

            // a disable, then an enable
            h.Leave(c);
            h.Motion(0.1f);
            t.Equal("charas items a b late", h.TakeMarks(), "CX3: disabled, c is not called");
            c = h.Enable(c);
            h.Motion(0.1f);
            t.Equal("charas items a b c late", h.TakeMarks(), "CX3: enabled, c is called");

            // a plugin that leaves in a pass: a closed caller is not called, also not by the running pass
            a.Body = () => { a.Body = null; h.Leave(b2); };
            h.Motion(0.1f);
            t.Equal("charas items a c late", h.TakeMarks(), "a caller closed during a pass is not called");
            b2 = h.Enable(b2);

            // a plugin that loads in a pass: the list changes at the next pass
            FakePostHost.Plugin e = null;
            a.Body = () => { a.Body = null; e = h.Add("e"); };
            h.Motion(0.1f);
            t.Equal("charas items a b c late", h.TakeMarks(), "a list replaced during a pass takes effect at the next");
            h.Motion(0.1f);
            t.Equal("charas items a b c e late", h.TakeMarks(), "the plugin added takes part from the next pass");

            // a pass from inside a pass (a plugin's ScenePost that steps the scene) runs nothing
            int inner = 0;
            a.Body = () => { a.Body = null; inner = a.Calls; h.Motion(0.1f); };
            h.Motion(0.1f);
            t.Equal("charas items a charas items late b c e late", h.TakeMarks(), "a pass from inside a pass returns at once");
            t.Equal(inner, a.Calls, "no second call into the plugin");
            h.Motion(0.1f);
            t.Equal("charas items a b c e late", h.TakeMarks(), "the next pass runs");

            // an exception out of the plugin never leaves the pass
            a.Throws = true;
            h.Motion(0.1f);
            t.Equal("charas items a b c e late", h.TakeMarks(), "a throw stops neither the pass nor the frame");
        }
    }

    public static class CX4ScenePostNone
    {
        public static void Run(TestRunner t)
        {
            var h = new FakePostHost();
            t.Check(!h.Stage.Any, "CX4: no plugin with the interface: the stage has nobody");
            t.Check(h.PostStage == null, "CX4: the runtime gets no call");
            h.Motion(0.1f);
            t.Equal("charas items late", h.TakeMarks(), "a frame without the stage");

            var a = h.Add("a");
            t.Check(h.PostStage != null, "a plugin with the interface: the runtime gets the call");
            h.Leave(a);
            t.Check(h.PostStage == null, "CX4: the last one left: the runtime's call is gone");
            var f = new PluginFrame(1, 0f, 0f, 0, false);
            h.Stage.Run(in f);
            t.Equal(0, a.Calls, "an empty stage runs nothing");
            t.Throws<ArgumentNullException>(() => new ScenePostCaller(null, a.Guard, a.Time), "a caller needs a handler");
        }
    }
}
