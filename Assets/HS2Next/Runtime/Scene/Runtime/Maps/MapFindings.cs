// The map finding codes of Phase 3.2 and their levels, in one table: MapRealization adds
// every map finding through Add, so the level of a code is defined here only (MapTest P8 compares the table with the spec).
// Codes of the 2.2 loader (map.missing, map.editMode, map.loadFailed, map.noScene) keep their levels.
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public static class MapFindings
    {
        public static readonly IReadOnlyDictionary<string, FindingSeverity> Levels = new Dictionary<string, FindingSeverity>
        {
            ["map.stepFailed"] = FindingSeverity.Error,
            ["map.componentOutsideFirstRoot"] = FindingSeverity.Info,
            ["map.blockNullRefs"] = FindingSeverity.Info,
            ["map.unshadowedDirectional"] = FindingSeverity.Info,
            ["map.manyShadowedLights"] = FindingSeverity.Info,
            ["map.lightmapPrefabDead"] = FindingSeverity.Info,
            ["map.bakeryOnlyLightmaps"] = FindingSeverity.Warning,
            ["map.bakeryMode"] = FindingSeverity.Info,
            ["map.probesTimeout"] = FindingSeverity.Warning,
            ["map.scriptsInert"] = FindingSeverity.Info,
            ["map.envAudioUnavailable"] = FindingSeverity.Info,
            ["map.ppv2Ignored"] = FindingSeverity.Info,
            ["map.missingScripts"] = FindingSeverity.Info,
            ["map.realtimeProbes"] = FindingSeverity.Info,
            ["map.largeLightmaps"] = FindingSeverity.Warning,
            ["map.vram"] = FindingSeverity.Info,
            ["map.staticBatchMoved"] = FindingSeverity.Info,
            // the 2.2 loader
            ["map.missing"] = FindingSeverity.Warning,
            ["map.editMode"] = FindingSeverity.Info,
            ["map.loadFailed"] = FindingSeverity.Error,
            ["map.noScene"] = FindingSeverity.Error,
        };

        /// <summary>The level of a map code (Info for an unknown one).</summary>
        public static FindingSeverity Level(string code) => code != null && Levels.TryGetValue(code, out var l) ? l : FindingSeverity.Info;

        /// <summary>Adds a map finding at the code's level; null findings: nothing.</summary>
        public static SceneFinding Add(SceneFindings findings, string code, string message) =>
            findings?.Add(Level(code), code, null, message);
    }
}
