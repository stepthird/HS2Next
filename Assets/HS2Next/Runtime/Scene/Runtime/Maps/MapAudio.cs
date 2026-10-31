// Map audio until Phase 3.6 (M9, M13): every AudioSource of the map scene is stopped and
// disabled at post-load step 3 (HS2 plays its env loops on awake; MapEnvSetting's mixer routing is ignored).
// The FMOD "Cannot create FMOD::Sound instance ... File not found" lines: vanilla map clips keep their sample data outside
// the bundle, m_Resource.m_Source = "../abdata/studio/map/<bundle>.resource". Measured (MapTest.RunAudioProbe):
// the Unity 6 Editor resolves it against the process's working directory (the project folder): the clips of map 10 load
// with a junction at <CWD>/../abdata and not with one at <Application.dataPath>/../abdata. A player resolves it against its
// data folder (HS2: StudioNEOV2_Data/../abdata = the install's abdata; HS2Next's player: <exe folder>/abdata).
// HS2Next provides that path as a directory junction → <hs2Root>/abdata (setting mapAudioLink, default false; never a copy,
// never replacing an existing file or folder): in a player next to its data folder; in the Editor only with the setting
// maps.audioLinkEditor (default false), because the Editor's place is outside the project (the parent of the project
// folder). Without the junction the clips stay unloaded (sources are off until 3.6) and map.envAudioUnavailable counts
// them. EnsureLink runs once per process before the first map load. Removing a link never recurses (rmdir without /s).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>Map options of the app settings (AppSettings: mapAudioLink, maps.maxMirrors).</summary>
    public static class MapOptions
    {
        /// <summary>Create the abdata junction for the clips' relative resource paths (players). The product default is false; the
        /// app sets this from the settings (mapAudioLink) on every boot, before the first map load reads it.</summary>
        public static bool AudioLink = false;
        /// <summary>Also in the Editor (default false: its junction would sit outside the project, next to the project folder).</summary>
        public static bool AudioLinkEditor;
        /// <summary>At most this many mirrors render per frame (package B; default 4).</summary>
        public static int MaxMirrors = 4;
    }

    public static class MapAudio
    {
        public struct Result
        {
            public int Sources, WerePlaying, Disabled;
            /// <summary>Clips whose data is outside their bundle (a resource path) and that did not load.</summary>
            public int ClipsUnavailable;
        }

        /// <summary>Stops and disables every AudioSource of the map (inactive objects included).</summary>
        public static Result Silence(IReadOnlyList<AudioSource> sources)
        {
            var r = new Result();
            if (sources == null) return r;
            foreach (var a in sources)
            {
                if (!a) continue;
                r.Sources++;
                if (a.isPlaying) { r.WerePlaying++; a.Stop(); }
                if (a.enabled) { a.enabled = false; r.Disabled++; }
            }
            return r;
        }

        /// <summary>Clips of the sources that preload their data but have not loaded it (call a frame after the load).</summary>
        public static int UnavailableClips(IReadOnlyList<AudioSource> sources)
        {
            int n = 0;
            if (sources == null) return 0;
            var seen = new HashSet<AudioClip>();
            foreach (var a in sources)
            {
                var c = a ? a.clip : null;
                if (!c || !seen.Add(c) || !c.preloadAudioData) continue;
                if (c.loadState == AudioDataLoadState.Failed || c.loadState == AudioDataLoadState.Unloaded) n++;
            }
            return n;
        }

        /// <summary>Where the relative resource paths of the clips point: a player's &lt;Application.dataPath&gt;/../abdata, the
        /// Editor's &lt;working directory&gt;/../abdata (measured).</summary>
        public static string LinkPath => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "abdata"))
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "abdata"));

        static bool tried;
        /// <summary>What the last <see cref="EnsureLink"/> found or did ("exists", "created", "off", "no hs2Root", "failed: …").</summary>
        public static string LinkState { get; private set; } = "not tried";

        /// <summary>Creates the junction once per process when the setting is on and the path does not exist.</summary>
        public static string EnsureLink(string hs2Root)
        {
            if (tried) return LinkState;
            tried = true;
            if (!MapOptions.AudioLink) return LinkState = "off";
            if (Application.isEditor && !MapOptions.AudioLinkEditor) return LinkState = "off in the Editor (maps.audioLinkEditor)";
            if (string.IsNullOrEmpty(hs2Root)) return LinkState = "no hs2Root";
            string target = Path.Combine(hs2Root, "abdata");
            if (!Directory.Exists(target)) return LinkState = "no abdata in hs2Root";
            return LinkState = CreateJunction(LinkPath, target);
        }

        /// <summary>Forgets the once-per-process guard (tests).</summary>
        public static void ResetLinkGuard() { tried = false; LinkState = "not tried"; }

        /// <summary>A directory junction <paramref name="link"/> → <paramref name="target"/> unless <paramref name="link"/>
        /// exists (file, folder or link). Windows only (mklink /J needs no elevation).</summary>
        public static string CreateJunction(string link, string target)
        {
            if (Directory.Exists(link) || File.Exists(link)) return "exists";
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer)
                return "failed: junctions only on Windows";
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    if (Directory.Exists(link)) return "created";
                    return "failed: " + (err ?? "").Trim();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("map audio: creating the junction " + link + " failed: " + e.Message);
                return "failed: " + e.Message;
            }
        }

        /// <summary>True when <paramref name="path"/> is a reparse point (a junction or symbolic link), never followed.</summary>
        public static bool IsReparsePoint(string path)
        {
            try
            {
                if (!Directory.Exists(path) && !File.Exists(path)) return false;
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Removes a junction made by <see cref="CreateJunction"/> (the link only, never the target's contents).</summary>
        public static bool RemoveJunction(string link)
        {
            try
            {
                var di = new DirectoryInfo(link);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) == 0) return false;
                // rmdir without /s removes the junction entry itself and never touches the target's files
                var psi = new ProcessStartInfo("cmd.exe", "/c rmdir \"" + link + "\"") { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) p.WaitForExit(10000);
                return !Directory.Exists(link);
            }
            catch (Exception e) { Debug.LogWarning("map audio: removing the junction " + link + " failed: " + e.Message); return false; }
        }
    }
}
