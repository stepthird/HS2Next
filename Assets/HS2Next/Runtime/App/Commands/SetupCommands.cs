// The folders, the first-run guide and the settings file over the bridge (group "app", owner core, Since 1.0.0;
// CoreCommands.Phase53Names). All pass the readiness gate at every stage (the guide runs before the content stages):
//   app.paths     the data root and its mode, the exe folder, the settings file, UserData, the output root, the cache, the plugin
//                 folder, the HS2 folder, where each came from (explicit = given on the command line or by the launch), the
//                 fallbacks, the copy offer, the longest projected cache path, the token file's access list
//   app.setup     op state (default) | set {hs2Root, modsFolders, cacheDir} (checked as the guide does; nothing written) | migrate
//                 {source} | finish (writes the settings; while the guide runs, it ends the guide's choices and the index starts) |
//                 skip; the reply holds the guide's page, the HS2 folder's verdict with its zipmod count, the suggestions, the
//                 mods folders, the cache and its free space, the copy offer (a deferred reply: the suggestions take up to 3 s)
//   app.settings  op get (default) | set {values: {key: value}}: the keys of the settings file with value, source (file, arg,
//                 default) and restart; unknown keys of the file with the nearest known key; set checks every value with the
//                 settings stage's rules first and writes the file keeping its other members (nothing is applied before a restart)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.App.Setup;
using HS2Next.UI.Setup;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class SetupCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase53Names).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "app.paths", "app.setup", "app.settings" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("app.paths", "The folders of this process (read-only): data root and mode, exe folder, settings file, UserData, output root, cache, plugin folder, HS2 folder, where each came from, fallbacks, the copy offer, the longest projected cache path.", "app")
                .Return("mode", ArgTypes.String, "editor | portable | override | perUser")
                .Return("dataRoot", ArgTypes.Path, "").Return("exeDir", ArgTypes.Path, "").Return("settingsFile", ArgTypes.Path, "the file this process reads and the guide / settings page write (it may not exist)")
                .Return("userData", ArgTypes.Path, "").Return("outputDir", ArgTypes.Path, "").Return("cacheDir", ArgTypes.Path, "").Return("pluginsDir", ArgTypes.Path, "")
                .Return("hs2Root", ArgTypes.Path, "null when none")
                .Return("sources", ArgTypes.Object, "{settings, userData, outputDir, cacheDir}: launch | arg | file | default | fallback")
                .Return("explicit", ArgTypes.ArrayOf(ArgTypes.String), "the folders given on the command line (or by the Editor launch override)")
                .Return("fallback", ArgTypes.Object, "{kind, from, to, reason} of the first folder that fell back to the per-user folder", false)
                .Return("migration", ArgTypes.Object, "{offered, source, files, bytes}: the copy of an earlier UserData folder", false)
                .Return("longestPath", ArgTypes.Int, "the longest projected path of the cache (characters)")
                .Return("tokenAcl", ArgTypes.String, "none | inherited | ownerOnly: the token file's access list")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.ReadOnly), Paths);
            CommandKit.Register(r, app, CommandKit.Spec("app.setup", "The first-run guide: its state, the HS2 folder's check and suggestions, and its steps (set, migrate, finish, skip) for automation.", "app")
                .Arg("op", ArgTypes.Enum("state", "set", "migrate", "finish", "skip"), "state (default): read; set: check and keep hs2Root / modsFolders / cacheDir (nothing written); migrate: copy an earlier UserData folder; finish: write the settings (while the guide runs, the index starts); skip: skip the guide", false, "state")
                .Arg("hs2Root", ArgTypes.Path, "set: the HS2 folder (an abdata folder or the exe stands for its folder)")
                .Arg("modsFolders", ArgTypes.ArrayOf(ArgTypes.Path), "set: the extra mods folders (each must be readable)")
                .Arg("cacheDir", ArgTypes.Path, "set: the cache folder (\"\" = the default)")
                .Arg("source", ArgTypes.Path, "migrate: the earlier UserData folder (default: the one offered)")
                .Arg("suggestions", ArgTypes.Bool, "state: gather the suggestions (up to 3 s)", false, true)
                .Return("needed", ArgTypes.Bool, "the setup stage needed the guide in this start")
                .Return("page", ArgTypes.String, "the guide's page while it runs, else null")
                .Return("hs2", ArgTypes.Object, "{path, level ok | warning | error, code, message, zipmods, didYouMean}")
                .Return("suggestions", ArgTypes.ArrayOf(ArgTypes.Object), "{path, from settings | registry | scan}")
                .Return("mods", ArgTypes.ArrayOf(ArgTypes.Object), "{path, readable, zipmods}")
                .Return("cache", ArgTypes.Object, "{path, default, freeGB}")
                .Return("migration", ArgTypes.Object, "{offered, source, files, bytes} or the copy's result {copied, same, conflicts, refused, bytes}", false)
                .Return("written", ArgTypes.ArrayOf(ArgTypes.String), "finish: the keys written", false)
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.Mutates | CommandFlags.WritesFiles | CommandFlags.Defers).WithTimeout(60), Setup);
            CommandKit.Register(r, app, CommandKit.Spec("app.settings", "The settings file's keys: value, source (file, arg, default) and whether a restart applies them; unknown keys with the nearest known key; set writes keys keeping the file's other members.", "app")
                .Arg("op", ArgTypes.Enum("get", "set"), "get (default) or set", false, "get")
                .Arg("values", ArgTypes.Object, "set: {key: value} (null removes a key); every value is checked first")
                .Return("file", ArgTypes.Path, "")
                .Return("values", ArgTypes.Object, "{key: {value, source file | arg | default, restart}}")
                .Return("unknownKeys", ArgTypes.ArrayOf(ArgTypes.Object), "{key, suggestion?}")
                .Return("written", ArgTypes.ArrayOf(ArgTypes.String), "set: the keys whose value changed", false)
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.Mutates | CommandFlags.WritesFiles), Settings);
        }

        // ---------------------------------------------------------------- app.paths

        static object Paths(CoreContext c)
        {
            var app = c.App;
            var p = app.Paths;
            if (p == null) throw new BridgeException(new BridgeError(BridgeCodes.NotReady, "the settings stage has not run yet"));
            var roots = p.Roots;
            var r = new BridgeReply()
                .With("mode", roots?.ModeName).With("dataRoot", roots?.DataRoot).With("exeDir", roots?.ExeDir).With("settingsFile", p.SettingsFile)
                .With("userData", p.UserData).With("outputDir", p.OutputDir).With("cacheDir", p.CacheDir)
                .With("pluginsDir", SafePlugins(app)).With("hs2Root", p.Hs2Root)
                .With("sources", p.Sources?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>())
                .With("explicit", p.Sources == null ? new List<string>() : p.Sources.Where(kv => kv.Value == "arg" || kv.Value == "launch").Select(kv => kv.Key).ToList())
                .With("longestPath", p.LongestProjectedPath)
                .With("tokenAcl", app.Bridge?.Server?.TokenAcl);
            if (p.Fallbacks != null && p.Fallbacks.Count > 0)
            {
                r["fallback"] = Fb(p.Fallbacks[0]);
                r["fallbacks"] = p.Fallbacks.Select(Fb).ToList();
            }
            var offer = Offer(app);
            if (offer != null) r["migration"] = new BridgeReply().With("offered", true).With("source", offer.Source).With("files", offer.Files).With("bytes", offer.Bytes);
            return r;
        }

        static object Fb(FolderFallback f) => new BridgeReply().With("kind", f.Kind).With("from", f.From).With("to", f.To).With("reason", f.Reason);

        static string SafePlugins(AppCore app)
        {
            try { return app.Settings?.PluginLaunch()?.Root; }
            catch (Exception) { return null; }
        }

        /// <summary>The copy offer of this start (the migration code's hook), or null.</summary>
        static MigrationOfferInfo Offer(AppCore app)
        {
            try
            {
                var sc = new SetupContext { UserData = app.Paths?.UserData };
                AppCore.SetupMigration?.Invoke(app, sc);
                return sc.MigrationOffer?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[app] app.paths: copy offer: " + e.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- app.setup

        sealed class Draft
        {
            public string Hs2Root;
            public List<string> Mods = new List<string>();
            public string CacheDir;
        }

        static Draft DraftOf(AppCore app) => app.Item("setup.draft", () => new Draft
        {
            Hs2Root = app.Paths?.Hs2Root,
            Mods = (app.Settings?.Content?.modsFolders ?? Array.Empty<string>()).ToList(),
            CacheDir = app.Paths?.CacheDir,
        });

        static object Setup(CoreContext c)
        {
            var app = c.App;
            if (app.Paths == null) throw new BridgeException(new BridgeError(BridgeCodes.NotReady, "the settings stage has not run yet"));
            string op = c.Args.Get("op", "state");
            var draft = DraftOf(app);
            var guide = app.SetupScreen?.Guide;
            var extra = new BridgeReply();
            switch (op)
            {
                case "state": break;
                case "set":
                {
                    string hs2 = c.Args.Has("hs2Root") ? c.Args.Get<string>("hs2Root") : null;
                    if (hs2 != null)
                    {
                        var v = Hs2InstallCheck.Check(hs2);
                        if (!v.Usable) throw CommandKit.BadArgs("hs2Root", "an HS2 folder", v.Message + (v.DidYouMean != null ? " (did you mean " + v.DidYouMean + "?)" : ""));
                        draft.Hs2Root = v.Path;
                    }
                    if (c.Args.Has("modsFolders"))
                    {
                        JArray a;
                        try { a = JArray.Parse(c.Args.RawJson("modsFolders") ?? "[]"); }
                        catch (Exception) { throw CommandKit.BadArgs("modsFolders", ArgTypes.ArrayOf(ArgTypes.Path)); }
                        var l = new List<string>();
                        foreach (var t in a)
                        {
                            string n = t.Type == JTokenType.String ? Hs2InstallCheck.Normalise((string)t) : null;
                            if (n == null || !Hs2InstallCheck.Readable(n)) throw CommandKit.BadArgs("modsFolders", ArgTypes.ArrayOf(ArgTypes.Path), "this folder cannot be read: " + t);
                            if (!l.Any(x => SetupGuide.SameFolder(x, n))) l.Add(n);
                        }
                        draft.Mods = l;
                    }
                    if (c.Args.Has("cacheDir"))
                    {
                        string cd = c.Args.Get<string>("cacheDir");
                        draft.CacheDir = string.IsNullOrWhiteSpace(cd) ? app.Paths.Roots?.DefaultCacheDir : Path.GetFullPath(cd.Trim().Trim('"'));
                    }
                    guide?.Prefill(draft.Hs2Root, draft.Mods, draft.CacheDir);
                    break;
                }
                case "finish":
                {
                    if (string.IsNullOrEmpty(draft.Hs2Root) || !Hs2InstallCheck.Check(draft.Hs2Root, false, false).Usable)
                        throw CommandKit.BadArgs("hs2Root", "an HS2 folder", "set a valid hs2Root first (app.setup {op: \"set\", hs2Root})");
                    if (guide != null && guide.Outcome == SetupOutcome.None)
                    {
                        string why = guide.FinishFromBridge(draft.Hs2Root, draft.Mods, draft.CacheDir);
                        if (why != null) throw new BridgeException(new BridgeError(BridgeCodes.Internal, why));
                        extra["written"] = guide.Written.ToList();
                    }
                    else
                    {
                        var roots = app.Paths.Roots;
                        var values = new List<KeyValuePair<string, JToken>>
                        {
                            new KeyValuePair<string, JToken>("hs2Root", draft.Hs2Root),
                            new KeyValuePair<string, JToken>("modsFolders", draft.Mods.Count > 0 ? new JArray(draft.Mods) : null),
                            new KeyValuePair<string, JToken>("cacheDir", SetupGuide.SameFolder(draft.CacheDir, roots?.DefaultCacheDir) ? null : (JToken)draft.CacheDir),
                        };
                        try { extra["written"] = SettingsFile.Write(app.Paths.SettingsFile, values); }
                        catch (Exception e) { throw new BridgeException(new BridgeError(BridgeCodes.Internal, "the settings could not be written to " + app.Paths.SettingsFile + ": " + e.Message)); }
                        extra["restart"] = true;
                    }
                    break;
                }
                case "skip":
                {
                    if (guide != null && guide.Outcome == SetupOutcome.None)
                    {
                        if (!guide.SkipFromBridge()) throw new BridgeException(new BridgeError(CommandKit.Unavailable, "Skip setup is not offered on page " + guide.Page));
                    }
                    else
                    {
                        var st = SetupState.Load(app.Paths.UserData);
                        st.Skipped = true;
                        st.SkippedAt = DateTime.UtcNow.ToString("o");
                        st.GuideVersion = SetupStage.GuideVersion;
                        st.Save(app.Paths.UserData);
                        extra["restart"] = true;
                    }
                    break;
                }
                case "migrate":
                {
                    var run = AppCore.SetupMigrate;
                    if (run == null) throw CommandKit.Unsupported("this build has no copy of an earlier UserData folder");
                    string source = c.Args.Get<string>("source", null) ?? Offer(app)?.Source;
                    if (string.IsNullOrEmpty(source)) throw CommandKit.BadArgs("source", ArgTypes.Path, "no earlier UserData folder is offered; name one with source");
                    var res = run(app, source, null);
                    if (res.Error != null) throw new BridgeException(new BridgeError(BridgeCodes.Internal, res.Error));
                    extra["migration"] = new BridgeReply().With("source", source).With("copied", res.Copied).With("same", res.Same).With("conflicts", res.Conflicts).With("refused", res.Refused).With("bytes", res.Bytes);
                    break;
                }
                default: throw CommandKit.BadArgs("op", ArgTypes.Enum("state", "set", "migrate", "finish", "skip"));
            }
            // the slow parts (the suggestions, the zipmod counts, free space) on a worker; the reply when they are done
            bool wantSuggestions = c.Args.Get("suggestions", true);
            string hs2Path = draft.Hs2Root, cachePath = draft.CacheDir;
            var mods = draft.Mods.ToList();
            var work = Task.Run(() => Gather(hs2Path, mods, cachePath, wantSuggestions));
            // an edit-mode test host pumps its frames by hand, far faster than the worker: it waits for the result instead
            if (app.IsTestHost) { work.Wait(TimeSpan.FromSeconds(30)); return Reply(work.Result); }
            var d = c.Ctx.Defer();
            var frames = c.Frames;
            void Poll()
            {
                if (d.IsCompleted || d.IsAbandoned) return;
                if (!work.IsCompleted) { frames.AtUpdate(1, Poll); return; }
                if (work.IsFaulted) { d.Fail(new BridgeError(BridgeCodes.Internal, work.Exception?.GetBaseException().Message)); return; }
                d.Complete(Reply(work.Result));
            }
            BridgeReply Reply(BridgeReply r)
            {
                var g = app.SetupScreen?.Guide;
                r["needed"] = app.SetupDecision?.Needed ?? false;
                r["reason"] = app.SetupDecision?.Reason;
                r["page"] = g?.Page;
                r["outcome"] = app.SetupScreen != null ? app.SetupScreen.Outcome.ToString().ToLowerInvariant() : app.SetupSkipped ? "skipped" : null;
                r["settingsFile"] = app.Paths?.SettingsFile;
                r["cache"] = ((BridgeReply)r["cache"]).With("default", app.Paths?.Roots?.DefaultCacheDir);
                if (!r.ContainsKey("migration") && !extra.ContainsKey("migration"))
                {
                    var offer = Offer(app);
                    if (offer != null) r["migration"] = new BridgeReply().With("offered", true).With("source", offer.Source).With("files", offer.Files).With("bytes", offer.Bytes);
                }
                foreach (var kv in extra) r[kv.Key] = kv.Value;
                return r;
            }
            frames.AtUpdate(1, Poll);
            return null;
        }

        static BridgeReply Gather(string hs2, List<string> mods, string cache, bool suggestions)
        {
            var r = new BridgeReply();
            if (!string.IsNullOrEmpty(hs2))
            {
                var v = Hs2InstallCheck.Check(hs2, countZipmods: true);
                r["hs2"] = new BridgeReply().With("path", v.Path).With("level", v.LevelName).With("code", v.Code).With("message", v.Message)
                    .With("zipmods", v.Zipmods).With("didYouMean", v.DidYouMean);
            }
            else r["hs2"] = null;
            r["suggestions"] = suggestions ? InstallSuggestions.Gather(hs2).Items.Select(s => (object)new BridgeReply().With("path", s.Path).With("from", s.From)).ToList() : new List<object>();
            r["mods"] = mods.Select(m => (object)new BridgeReply().With("path", m).With("readable", Hs2InstallCheck.Readable(m)).With("zipmods", Hs2InstallCheck.CountZipmods(m))).ToList();
            double free = -1;
            try
            {
                string p = cache;
                while (!string.IsNullOrEmpty(p) && !Directory.Exists(p)) p = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(p)) free = Math.Round(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(p))).AvailableFreeSpace / (1024.0 * 1024 * 1024), 1);
            }
            catch (Exception) { }
            r["cache"] = new BridgeReply().With("path", cache).With("freeGB", free);
            return r;
        }

        // ---------------------------------------------------------------- app.settings

        static object Settings(CoreContext c)
        {
            var app = c.App;
            string file = app.Paths?.SettingsFile ?? app.Settings?.SettingsPath;
            if (string.IsNullOrEmpty(file)) throw new BridgeException(new BridgeError(BridgeCodes.NotReady, "the settings stage has not run yet"));
            string op = c.Args.Get("op", "get");
            var reply = new BridgeReply().With("file", file).With("exists", File.Exists(file));
            if (op == "set")
            {
                JObject values;
                try { values = JObject.Parse(c.Args.RawJson("values") ?? "{}"); }
                catch (Exception) { throw CommandKit.BadArgs("values", ArgTypes.Object); }
                if (!values.HasValues) throw CommandKit.BadArgs("values", ArgTypes.Object, "set needs values {key: value}");
                var checkedValues = new List<KeyValuePair<string, JToken>>();
                foreach (var pr in values.Properties())
                {
                    if (!SettingsSchema.TryGet(pr.Name, out var def))
                    {
                        string near = SettingsSchema.Nearest(pr.Name);
                        throw CommandKit.BadArgs("values", "known settings keys", "unknown key " + pr.Name + (near != null ? " (did you mean " + near + "?)" : ""));
                    }
                    JToken v;
                    try { v = SettingsSchema.Validate(def, pr.Value); }
                    catch (FormatException e) { throw CommandKit.BadArgs("values", def.Kind.ToString(), e.Message); }
                    checkedValues.Add(new KeyValuePair<string, JToken>(pr.Name, v));
                }
                try { reply["written"] = SettingsFile.Write(file, checkedValues); }
                catch (Exception e) { throw new BridgeException(new BridgeError(BridgeCodes.Internal, "the settings file could not be written: " + e.Message)); }
                Debug.Log("[app] app.settings set: " + string.Join(", ", (List<string>)reply["written"]) + " in " + file);
            }
            else if (op != "get") throw CommandKit.BadArgs("op", ArgTypes.Enum("get", "set"));
            JObject o;
            try { o = SettingsFile.Read(file); }
            catch (FormatException e) { reply["error"] = e.Message; o = new JObject(); }
            var args = Environment.GetCommandLineArgs();
            var all = new Dictionary<string, object>();
            foreach (var def in SettingsSchema.All)
            {
                var argV = SettingsSchema.ArgValue(def, args);
                var fileV = SettingsFile.Get(o, def.Key);
                string source = argV != null ? "arg" : fileV != null ? "file" : "default";
                JToken value = argV ?? fileV ?? DefaultOf(def);
                all[def.Key] = new BridgeReply().With("value", value).With("source", source).With("restart", def.Restart).With("section", def.Section);
            }
            reply["values"] = all;
            reply["unknownKeys"] = SettingsSchema.UnknownKeys(o).Select(u => (object)new BridgeReply().With("key", u.Key).With("suggestion", u.Suggestion)).ToList();
            return reply;
        }

        /// <summary>The default of a key in this build (the bridge token: required in the distributed build only).</summary>
        public static JToken DefaultOf(SettingDef d)
        {
            if (d.Key == "bridge.token") return HS2Next.App.BuildInfo.Current?.TokenRequiredByDefault ?? false;
            return d.Default;
        }
    }
}
