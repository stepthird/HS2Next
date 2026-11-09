// The test plugin of the row WetCheck.player (CC0-1.0). A player has no timer around a stain's work and no way to take the frame cap off, so this plugin does both on the
// player's main thread: wetprobe.frames sets the frame cap, the rest (WetProbeTimes.cs) times the work and keeps renders in memory. It changes nothing a scene saves.
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using UnityEngine;

namespace Stp3.Test.WetProbe
{
    public sealed partial class WetProbePlugin : IPlugin
    {
        IPluginContext ctx;

        public void Load(IPluginContext context)
        {
            ctx = context;
            context.Commands.Register(new CommandSpec("wetprobe.frames", "The frame cap: vsync count and target frame rate.")
                .Arg("vsync", ArgTypes.Int, "QualitySettings.vSyncCount").Arg("fps", ArgTypes.Int, "Application.targetFrameRate"), c =>
            {
                if (c.Args.Has("vsync")) QualitySettings.vSyncCount = c.Args.Get("vsync", 0);
                if (c.Args.Has("fps")) Application.targetFrameRate = c.Args.Get("fps", -1);
                return new Dictionary<string, object>
                {
                    ["vsync"] = QualitySettings.vSyncCount, ["fps"] = Application.targetFrameRate, ["screen"] = Screen.width + "x" + Screen.height,
                    ["device"] = SystemInfo.graphicsDeviceType.ToString(),
                };
            });
#if !FRAMES_ONLY
            RegisterTimes(context);
#endif
        }

        public void Unload() { }
    }
}
