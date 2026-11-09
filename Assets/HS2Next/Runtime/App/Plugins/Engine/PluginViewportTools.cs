// The viewport tools of one plugin (the plugin's view of the host's IViewportTools): the plugin's tool runs behind a wrapper
// that the host sees as the tool, so every call into the plugin's code goes through its fault wrapper. A tool that throws is
// counted as a fault of the plugin and ends with Cancelled; the plugin goes on, until the count reaches its threshold. When
// the plugin unloads, is disabled or faults, its running tool ends with UiClosed and this view refuses further tools. The
// host needs to know nothing of plugins: ending from here goes through its End, and the wrapper hands the plugin the reason
// that applies. Main thread only.
using System;
using HS2Next.Api.Unity;
using UnityEngine;

namespace HS2Next.App.Plugins.Engine
{
    /// <summary>One plugin's view of the host's viewport tools.</summary>
    public sealed class PluginViewportTools : IViewportTools
    {
        readonly PluginGuard guard;
        GuardedTool current;
        bool closed;

        /// <param name="guard">The plugin's fault wrapper.</param>
        /// <param name="host">The host's viewport tools.</param>
        public PluginViewportTools(PluginGuard guard, IViewportTools host)
        {
            this.guard = guard ?? throw new ArgumentNullException(nameof(guard));
            Host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary>The host's viewport tools.</summary>
        public IViewportTools Host { get; }

        public bool Running => Host.Running;

        public bool Begin(ViewportTool tool, ToolOptions options = default)
        {
            if (closed) throw new ObjectDisposedException("the plugin " + guard.Guid + " is unloaded");
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            return Host.Begin(new GuardedTool(this, tool), options);
        }

        public void End(ViewportTool tool)
        {
            var g = current;
            if (g != null && ReferenceEquals(g.Tool, tool)) Host.End(g);
        }

        public ToolPick Pick(Ray ray, ToolPickFilter filter = default) => Host.Pick(ray, filter);

        /// <summary>The plugin leaves: its running tool ends with <see cref="ToolEnd.UiClosed"/>, later Begin calls throw. Idempotent.</summary>
        public void Close()
        {
            closed = true;
            current?.EndAs(ToolEnd.UiClosed);
        }

        /// <summary>The plugin's tool as the host sees it.</summary>
        sealed class GuardedTool : ViewportTool
        {
            readonly PluginViewportTools owner;
            readonly PluginGuard guard;
            ToolEnd? endAs;
            bool ended;

            public GuardedTool(PluginViewportTools owner, ViewportTool tool)
            {
                this.owner = owner;
                guard = owner.guard;
                Tool = tool;
            }

            public ViewportTool Tool { get; }

            /// <summary>Ends this tool through the host; the plugin hears <paramref name="reason"/>. The host's End may be a no-op on a tool it no longer holds, so the plugin hears it here then.</summary>
            public void EndAs(ToolEnd reason)
            {
                if (ended) return;
                endAs = reason;
                try { owner.Host.End(this); }
                catch (Exception e) { guard.Count("tool End", e); }
                if (!ended) Ended(reason);
            }

            void Fault(string hook, Exception e)
            {
                guard.Count("tool " + hook, e);
                EndAs(ToolEnd.Cancelled);
            }

            public override void Started()
            {
                owner.current = this;
                guard.Enter();
                try { Tool.Started(); }
                catch (Exception e) { Fault(nameof(Started), e); }
                finally { guard.Exit(); }
            }

            public override bool PointerDown(in ToolPointer p)
            {
                guard.Enter();
                try { return Tool.PointerDown(in p); }
                catch (Exception e) { Fault(nameof(PointerDown), e); return false; }
                finally { guard.Exit(); }
            }

            public override void PointerMove(in ToolPointer p)
            {
                guard.Enter();
                try { Tool.PointerMove(in p); }
                catch (Exception e) { Fault(nameof(PointerMove), e); }
                finally { guard.Exit(); }
            }

            public override void PointerUp(in ToolPointer p)
            {
                guard.Enter();
                try { Tool.PointerUp(in p); }
                catch (Exception e) { Fault(nameof(PointerUp), e); }
                finally { guard.Exit(); }
            }

            public override void PointerCancel()
            {
                guard.Enter();
                try { Tool.PointerCancel(); }
                catch (Exception e) { Fault(nameof(PointerCancel), e); }
                finally { guard.Exit(); }
            }

            public override bool Wheel(float notches, in ToolPointer p)
            {
                guard.Enter();
                try { return Tool.Wheel(notches, in p); }
                catch (Exception e) { Fault(nameof(Wheel), e); return false; }
                finally { guard.Exit(); }
            }

            public override bool Key(string key, ToolMods mods)
            {
                guard.Enter();
                try { return Tool.Key(key, mods); }
                catch (Exception e) { Fault(nameof(Key), e); return false; }
                finally { guard.Exit(); }
            }

            public override void Draw(IToolDraw draw)
            {
                guard.Enter();
                try { Tool.Draw(draw); }
                catch (Exception e) { Fault(nameof(Draw), e); }
                finally { guard.Exit(); }
            }

            public override void Ended(ToolEnd reason)
            {
                if (ended) return;
                ended = true;
                if (owner.current == this) owner.current = null;
                guard.Enter();
                try { Tool.Ended(endAs ?? reason); }
                catch (Exception e) { guard.Count("tool " + nameof(Ended), e); }
                finally { guard.Exit(); }
            }
        }
    }
}
