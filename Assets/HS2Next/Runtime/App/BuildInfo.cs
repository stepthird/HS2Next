// The build facts of this process (HS2Next.Bridge.BuildFacts): the player's HS2Next_Data/StreamingAssets/build.json, kind
// "dev" in a player without the file, always kind "editor" in the Editor. Read once, on the main thread, the first time the
// app asks (the settings stage); the settings line, bridge.version.build and Help > About report them, and the bridge
// token's default follows the kind (required in the distributed build only).
using HS2Next.Bridge;
using UnityEngine;

namespace HS2Next.App
{
    public static class BuildInfo
    {
        static BuildFacts current;

        /// <summary>The facts of this process (read on first use; main thread).</summary>
        public static BuildFacts Current => current ??= BuildFacts.Read(Application.streamingAssetsPath, Application.isEditor, Application.version);

        /// <summary>Tests: replaces the facts of this process; null reads them again on next use.</summary>
        public static void Override(BuildFacts facts) => current = facts;
    }
}
