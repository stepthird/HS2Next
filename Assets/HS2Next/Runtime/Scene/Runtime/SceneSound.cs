// The BGM / user-audio frame of a realized scene (decision E17): state, lists and
// HS2's one-channel rule; no playback until Phase 6. The state is `scene.sound {bgm {ref, play, repeat},
// userAudio {file, play, repeat}}`; the part never writes it.
//   lists       BGM rows of the Studio DB (StudioDb.Bgm, visible rows: T23's aiInstall rule decides the vanilla 40 / 21);
//               user audio = `*.wav` in HS2Next's UserData/audio (file ref {user: "audio/<name>"}) and, read only, the HS2
//               install's UserData/audio ({hs2: "UserData/audio/<name>"}); HS2Next's file wins a name clash.
//   channel     sound.set applies the rule (LightCameraCommands): bgm.play = true clears userAudio.play and the reverse; a
//               loaded scene that stores both keeps both and reports `sound.channelConflict` (info) with HS2's load outcome
//               (BGM starts first, then the user audio, which wins when its file exists).
//   findings    `sound.bgmMissing` (a BGM ref that names no row), `sound.fileMissing` (a user-audio file that does not
//               exist), warnings, once per value until the realization is cleared; checked after loads and sound changes.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneSound : ISceneRuntimePart
    {
        public const string AudioFolder = "audio";

        /// <summary>One user-audio file of the lists.</summary>
        public struct AudioFile
        {
            /// <summary>The file name (no folder).</summary>
            public string Name;
            /// <summary>"user" (HS2Next UserData) or "hs2" (the HS2 install, read only).</summary>
            public string Source;
            public FileRef Ref;
            public string FullPath;
        }

        SceneRuntime runtime;
        bool dirty = true, loaded;

        /// <summary>The user-audio files: HS2Next's UserData/audio/*.wav first, then the install's (names HS2Next has are left out).</summary>
        public static List<AudioFile> AudioFiles(string userData, string hs2Root)
        {
            var l = new List<AudioFile>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Scan(string dir, string source, Func<string, FileRef> make)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                string[] files;
                try { files = Directory.GetFiles(dir, "*.wav", SearchOption.TopDirectoryOnly); }
                catch (Exception) { return; }
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    // the pattern "*.wav" also matches "*.wave…" on Windows (8.3 rule): keep the exact extension
                    if (!string.Equals(Path.GetExtension(f), ".wav", StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Path.GetFileName(f);
                    if (!names.Add(name)) continue;
                    l.Add(new AudioFile { Name = name, Source = source, Ref = make(name), FullPath = Path.GetFullPath(f) });
                }
            }
            Scan(string.IsNullOrEmpty(userData) ? null : Path.Combine(userData, AudioFolder), "user", n => FileRef.FromUser(AudioFolder + "/" + n));
            Scan(string.IsNullOrEmpty(hs2Root) ? null : Path.Combine(hs2Root, "UserData", AudioFolder), "hs2", n => FileRef.FromHs2("UserData/" + AudioFolder + "/" + n));
            return l;
        }

        /// <summary>The absolute path of a user / hs2 file reference, or null (res entries, no folder, unsafe path).</summary>
        public static string PathOf(FileRef f, string userData, string hs2Root)
        {
            if (f == null || !ItemTextures.SafeRelative(f.Value)) return null;
            string root = f.Kind == FileRef.Kinds.User ? userData : f.Kind == FileRef.Kinds.Hs2 ? hs2Root : null;
            if (string.IsNullOrEmpty(root)) return null;
            try { return Path.GetFullPath(Path.Combine(root, f.Value)); }
            catch (Exception) { return null; }
        }

        /// <summary>True when the referenced file exists (res entries count as present: they travel inside the scene).</summary>
        public static bool FileExists(FileRef f, string userData, string hs2Root)
        {
            if (f == null) return false;
            if (f.Kind == FileRef.Kinds.Res) return !string.IsNullOrEmpty(f.Value);
            var p = PathOf(f, userData, hs2Root);
            return p != null && File.Exists(p);
        }

        /// <summary>The BGM row of a ref, or null (none, unparsable or unknown).</summary>
        public static StudioBgm Bgm(StudioDb db, string bgmRef)
        {
            if (db == null || bgmRef == null || !ContentId.TryParse(bgmRef, out var id) || id.IsEmpty) return null;
            return db.TryGetBgm(id, out var row) ? row : null;
        }

        /// <summary>HS2's load outcome for both play flags set: "userAudio" when its file exists, else "bgm".</summary>
        public static string LoadWinner(SoundSettings s, string userData, string hs2Root) =>
            s != null && s.UserAudio.Play && FileExists(s.UserAudio.File, userData, hs2Root) ? "userAudio" : "bgm";

        string UserData => runtime?.Services?.UserData;
        string Hs2Root => runtime?.Services?.Hs2Root;

        /// <summary>Checks the stored sound state and reports the findings of the file comment.</summary>
        public void Check()
        {
            dirty = false;
            if (runtime == null) return;
            var s = runtime.Model.Settings.Sound;
            if (s == null) return;
            if (s.Bgm.Ref != null && runtime.Services?.Db != null && Bgm(runtime.Services.Db, s.Bgm.Ref) == null)
                runtime.ReportOnce(FindingSeverity.Warning, "sound.bgmMissing", null, "BGM " + s.Bgm.Ref + " is not in the BGM list");
            if (s.UserAudio.File != null && !FileExists(s.UserAudio.File, UserData, Hs2Root))
                runtime.ReportOnce(FindingSeverity.Warning, "sound.fileMissing", null, "user audio file " + s.UserAudio.File.Kind.ToString().ToLowerInvariant() + ":" + s.UserAudio.File.Value + " not found");
            if (loaded && s.Bgm.Play && s.UserAudio.Play)
                runtime.ReportOnce(FindingSeverity.Info, "sound.channelConflict", null,
                    "the scene has both BGM and user audio playing; HS2's load would play " + LoadWinner(s, UserData, Hs2Root) + " (one channel)");
            loaded = false;
        }

        // ------------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            dirty = true;
        }

        public void OnChanged(SceneChangeBatch b)
        {
            if (b.Touches(null, "sound")) dirty = true;
        }

        public void Apply()
        {
            if (dirty) Check();
        }

        public void LateApply() { }

        public void Clear() => dirty = true;

        public void OnLoadedRealized()
        {
            dirty = true;
            loaded = true;
        }
    }
}
