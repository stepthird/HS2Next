// The resolved absolute folders of the app: HS2Next's own UserData (scenes, characters,
// coordinates, pose fragments (Phase 3.4), the bridge's folder and output root, the screenshots, background and frame
// images of Phase 3.6) and the read-only folders of the HS2 install (cards, poses, backgrounds, frames).
// The HS2 install's UserData is never written. The defaults lie under the data root of the process
// (HS2Next.App.Setup.AppRoots: the project in the Editor, the exe folder in the portable mode, HS2NEXT_DATA_ROOT, else the per-user
// folder of the player); the settings stage may switch an unwritable UserData or cache to the per-user folder (Fallbacks).
using System.Collections.Generic;
using System.IO;
using HS2Next.App.Setup;
using HS2Next.Content;

namespace HS2Next.App
{
    public sealed class AppPaths
    {
        /// <summary>HS2Next UserData (absolute).</summary>
        public string UserData { get; }
        /// <summary>&lt;UserData&gt;/scene: *.hs2nscene.</summary>
        public string SceneDir { get; }
        /// <summary>&lt;UserData&gt;/chara: *.hs2nchara.</summary>
        public string CharaDir { get; }
        /// <summary>&lt;UserData&gt;/coordinate: *.hs2ncoord (Phase 4).</summary>
        public string CoordDir { get; }
        /// <summary>&lt;UserData&gt;/pose: *.hs2npose pose fragments (Phase 3.4), recursive.</summary>
        public string PoseDir { get; }
        /// <summary>&lt;UserData&gt;/Bridge: endpoint.json, token (package B).</summary>
        public string BridgeDir { get; }
        /// <summary>The bridge output root (bridge.outputDir, default &lt;UserData&gt;/Bridge); captures go to shots/.</summary>
        public string OutputDir { get; }
        /// <summary>&lt;UserData&gt;/cap: screenshots yyyyMMddHHmmssfff.png (Phase 3.6).</summary>
        public string CapDir { get; }
        /// <summary>&lt;UserData&gt;/bg: background images (PNG / JPEG).</summary>
        public string BgDir { get; }
        /// <summary>&lt;UserData&gt;/frame: frame images (PNG).</summary>
        public string FrameDir { get; }
        /// <summary>The HS2 install root (absolute), or null without one.</summary>
        public string Hs2Root { get; }
        /// <summary>&lt;hs2Root&gt;/UserData (read only), or null.</summary>
        public string Hs2UserData { get; }
        /// <summary>&lt;hs2Root&gt;/UserData/studio/pose (read only), or null.</summary>
        public string Hs2PoseDir { get; }
        /// <summary>The data root and its mode (ContentSettings.Roots when the settings were read).</summary>
        public AppRoots Roots { get; }
        /// <summary>The settings file of this run (absolute; it may not exist yet).</summary>
        public string SettingsFile { get; }
        /// <summary>The cache folder (absolute): cacheDir / -cacheDir, else &lt;data root&gt;/Cache.</summary>
        public string CacheDir { get; }
        /// <summary>Where each folder came from ("launch", "arg", "file", "default" or "fallback"): settings, userData, outputDir,
        /// cacheDir.</summary>
        public IReadOnlyDictionary<string, string> Sources { get; }
        /// <summary>The folders the settings stage switched to the per-user folder because they could not be written (empty when
        /// none).</summary>
        public IReadOnlyList<FolderFallback> Fallbacks { get; }

        /// <summary>The longest file path the caches are expected to write: the cache folder plus the longest relative path of the
        /// thumbnail cache (thumbs\&lt;category&gt;\&lt;folder of at most 41 characters&gt;\&lt;id&gt;_&lt;16 hex&gt;.png = 84).</summary>
        public int LongestProjectedPath => (CacheDir?.Length ?? 0) + 1 + 84;

        public AppPaths(AppSettings s) : this(s, null) { }

        /// <summary>The folders of <paramref name="s"/>, with the fallbacks the settings stage made.</summary>
        public AppPaths(AppSettings s, IReadOnlyList<FolderFallback> fallbacks)
        {
            Roots = s.Roots ?? ContentSettings.Roots;
            SettingsFile = s.SettingsPath ?? s.SettingsFile ?? Roots.DefaultSettingsFile;
            CacheDir = s.Content != null ? s.Content.ResolvedCacheDir : Roots.DefaultCacheDir;
            Sources = new Dictionary<string, string>
            {
                ["settings"] = s.SettingsSource, ["userData"] = s.UserDataSource, ["outputDir"] = s.OutputDirSource, ["cacheDir"] = s.CacheDirSource,
            };
            Fallbacks = fallbacks ?? new List<FolderFallback>();
            UserData = Path.GetFullPath(s.UserData ?? AppSettings.DefaultUserData);
            SceneDir = Path.Combine(UserData, "scene");
            CharaDir = Path.Combine(UserData, "chara");
            CoordDir = Path.Combine(UserData, "coordinate");
            PoseDir = Path.Combine(UserData, "pose");
            BridgeDir = Path.Combine(UserData, "Bridge");
            CapDir = Path.Combine(UserData, "cap");
            BgDir = Path.Combine(UserData, "bg");
            FrameDir = Path.Combine(UserData, "frame");
            OutputDir = Path.GetFullPath(string.IsNullOrEmpty(s.Bridge?.OutputDir) ? BridgeDir : s.Bridge.OutputDir);
            string root = s.Content?.hs2Root;
            if (!string.IsNullOrEmpty(root))
            {
                Hs2Root = ContentSettings.Resolve(root);
                Hs2UserData = Path.Combine(Hs2Root, "UserData");
                Hs2PoseDir = Path.Combine(Hs2UserData, "studio", "pose");
            }
        }

        /// <summary>&lt;hs2Root&gt;/UserData/chara/male (sex 0) or female (any other), or null without an install.</summary>
        public string Hs2CharaDir(int sex) => Hs2UserData == null ? null : Path.Combine(Hs2UserData, "chara", sex == 0 ? "male" : "female");

        /// <summary>Creates the folders HS2Next writes into (scene, chara, coordinate, pose, Bridge, the output root with shots/,
        /// cap, bg, frame).</summary>
        public void CreateFolders()
        {
            foreach (var d in new[] { SceneDir, CharaDir, CoordDir, PoseDir, BridgeDir, OutputDir, Path.Combine(OutputDir, "shots"), CapDir, BgDir, FrameDir })
                Directory.CreateDirectory(d);
        }

        /// <summary>The UserData folders under another UserData root (scene, chara, coordinate, pose, Bridge, Bridge/shots, cap, bg,
        /// frame): what the settings stage creates when it tries a folder.</summary>
        public static void CreateUserDataFolders(string userData)
        {
            foreach (var n in new[] { "scene", "chara", "coordinate", "pose", "Bridge", Path.Combine("Bridge", "shots"), "cap", "bg", "frame" })
                Directory.CreateDirectory(Path.Combine(userData, n));
        }

        public override string ToString() => "UserData " + UserData + ", output " + OutputDir + ", cache " + CacheDir + ", HS2 " + (Hs2Root ?? "(none)");
    }
}
