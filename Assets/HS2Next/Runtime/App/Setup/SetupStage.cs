// The setup boot stage's decision and the first-run guide's stored state (Unity-free). The stage runs after the bridge and
// before the catalog. It is needed when the effective hs2Root is empty or fails the install check, unless the guide of this
// version was skipped (the app then starts with the extra mods folders only), or when the command line has -setup (a re-run on
// demand). A batch process (or one without the UI) never shows the guide: there the stage fails with BatchMessage. The guide's
// progress is UI state in <UserData>/config/setup.json {version, guideVersion, lastPage, choices, skipped, completedAt}
// (written atomically; an unreadable file counts as absent); the settings it chose go into the settings file (SettingsFile).
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Setup
{
    /// <summary>&lt;UserData&gt;/config/setup.json.</summary>
    public sealed class SetupState
    {
        public const string FileName = "setup.json";
        public int Version = 1;
        public int GuideVersion = SetupStage.GuideVersion;
        /// <summary>The page the guide showed last (welcome, files, hs2, mods, cache, index, ready).</summary>
        public string LastPage;
        /// <summary>The choices made so far: hs2Root, modsFolders, cacheDir, language (the guide restores them on a resume).</summary>
        public JObject Choices = new JObject();
        public bool Skipped;
        /// <summary>ISO 8601 UTC of the finish, or null.</summary>
        public string CompletedAt;
        /// <summary>ISO 8601 UTC of the skip, or null.</summary>
        public string SkippedAt;
        /// <summary>The unknown settings keys last shown in a toast (the toast appears once per such list).</summary>
        public string UnknownKeysShown;

        public static string PathOf(string userData) => Path.Combine(userData ?? "", "config", FileName);

        /// <summary>The state of a UserData folder (a new state when the file is absent or unreadable).</summary>
        public static SetupState Load(string userData)
        {
            string f = PathOf(userData);
            try
            {
                if (!File.Exists(f)) return new SetupState();
                var o = JObject.Parse(File.ReadAllText(f, Encoding.UTF8));
                var s = new SetupState
                {
                    Version = (int?)o["version"] ?? 1,
                    GuideVersion = (int?)o["guideVersion"] ?? 0,
                    LastPage = (string)o["lastPage"],
                    Choices = o["choices"] as JObject ?? new JObject(),
                    Skipped = (bool?)o["skipped"] ?? false,
                    CompletedAt = (string)o["completedAt"],
                    SkippedAt = (string)o["skippedAt"],
                    UnknownKeysShown = (string)o["unknownKeysShown"],
                };
                return s;
            }
            catch (Exception) { return new SetupState(); }
        }

        public JObject ToJson() => new JObject
        {
            ["version"] = Version, ["guideVersion"] = GuideVersion, ["lastPage"] = LastPage, ["choices"] = Choices ?? new JObject(),
            ["skipped"] = Skipped, ["skippedAt"] = SkippedAt, ["completedAt"] = CompletedAt, ["unknownKeysShown"] = UnknownKeysShown,
        };

        /// <summary>Writes the file (temp + replace).</summary>
        public void Save(string userData)
        {
            string f = PathOf(userData);
            SettingsFile.WriteAtomic(f, ToJson().ToString(Formatting.Indented) + "\n");
        }

        /// <summary>The skip holds for this guide version only.</summary>
        public bool SkippedThisVersion => Skipped && GuideVersion == SetupStage.GuideVersion;
    }

    public static class SetupStage
    {
        /// <summary>The guide's version: a later guide that must be seen again raises it (a skip of an older one no longer holds).</summary>
        public const int GuideVersion = 1;
        public const string Flag = "-setup";
        public const string BatchMessage = "no HS2 folder is set: start HS2Next once without -batchmode, pass -gameRoot, or give -settings";
        public const string NoUiMessage = "no HS2 folder is set and the UI is off (-noUi or ui.enabled false): pass -gameRoot or give -settings";

        /// <summary>The pages of the guide, in order (element "setup.page" shows the current one).</summary>
        public static readonly string[] Pages = { "welcome", "files", "hs2", "mods", "cache", "index", "ready" };
        /// <summary>The pages on which Skip setup is offered.</summary>
        public static bool CanSkip(string page) => page == "files" || page == "hs2" || page == "mods" || page == "cache";

        public sealed class Decision
        {
            public bool Needed;
            /// <summary>Why (the stage's log line: "setup: not needed (&lt;reason&gt;)").</summary>
            public string Reason;
            /// <summary>-setup was given.</summary>
            public bool Forced;
            /// <summary>The install check of the effective hs2Root (null when it is empty).</summary>
            public InstallVerdict Install;
            public SetupState State;
            public double Ms;
        }

        /// <summary>
        /// Whether the guide is needed: <paramref name="hs2Root"/> the effective (resolved) hs2Root, <paramref name="args"/> the
        /// command line, <paramref name="state"/> the stored state (null = none).
        /// </summary>
        public static Decision Decide(string hs2Root, string[] args, SetupState state)
        {
            var sw = Stopwatch.StartNew();
            var d = new Decision { State = state ?? new SetupState() };
            d.Forced = args != null && Array.IndexOf(args, Flag) >= 0;
            if (!string.IsNullOrWhiteSpace(hs2Root)) d.Install = Hs2InstallCheck.Check(hs2Root, countZipmods: false, propose: false);
            if (d.Forced) { d.Needed = true; d.Reason = Flag + " on the command line"; }
            else if (d.Install != null && d.Install.Usable) { d.Needed = false; d.Reason = "HS2 folder " + d.Install.LevelName + ": " + d.Install.Path; }
            else if (d.State.SkippedThisVersion) { d.Needed = false; d.Reason = "the guide was skipped" + (d.Install == null ? "; no HS2 folder is set" : "; the HS2 folder fails the check (" + d.Install.Code + ")"); }
            else if (d.Install == null) { d.Needed = true; d.Reason = "no HS2 folder is set"; }
            else { d.Needed = true; d.Reason = "the HS2 folder fails the check (" + d.Install.Code + "): " + d.Install.Path; }
            d.Ms = sw.Elapsed.TotalMilliseconds;
            return d;
        }

        /// <summary>The page index (0–6; -1 for an unknown name).</summary>
        public static int PageIndex(string page) => Array.IndexOf(Pages, page);

        /// <summary>The page a resume opens: the last one of pages 1–4 shown before (else the welcome page).</summary>
        public static string ResumePage(SetupState s)
        {
            if (s == null || s.CompletedAt != null) return Pages[0];
            int i = PageIndex(s.LastPage);
            return i >= 1 && i <= 4 ? Pages[i] : Pages[0];
        }

        /// <summary>The number of bytes of the expected cache use shown on the cache page (about 0.3 GB at once, up to about 10 GB).</summary>
        public const double CacheAtOnceGB = 0.3, CacheUpToGB = 10, CacheWarnFreeGB = 15;
    }
}
