// The update check (added after 1.0.0): once at start when the Studio UI is up - in the
// distributed build, with the option general.updateCheck on; -updateCheck in other builds; never with -noUpdateCheck or the
// environment variable HS2NEXT_UPDATE_CHECK=off (the test tools' bare starts of the distributed exe) - and by hand
// from Help ▸ Check for updates in every build. One request for the latest release of the public repository
// (UpdateCheckRules.ApiUrl; -updateCheckUrl <url> for tests), then the dialog: a newer version offers its download
// page in the browser, Skip this version and Later; by hand the dialog also says "latest" or why the check failed. The app
// never downloads or runs anything. The version order, the decision and the state file are UpdateCheckCore's.
using System;
using HS2Next.App;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.Networking;

namespace HS2Next.UI.Setup
{
    public sealed class UpdateCheckModule : IUiModule
    {
        /// <summary>After the setup module (900), whose help.about it leaves alone.</summary>
        public int Order => 910;

        /// <summary>The outcome of the last check of this process ("newer 1.0.1", "latest 1.0.0", "failed: …"; null before one).</summary>
        public static string LastOutcome { get; private set; }
        /// <summary>The start check ran in this process (a second UI start does not ask again).</summary>
        static bool startChecked;
        UiContext context;

        public void Register(UiContext ctx)
        {
            context = ctx;
            ctx.Registry.Actions.Register(ActionIds.HelpCheckUpdates, _ => { Check(ctx, true); return true; });
            if (!startChecked && StartCheckWanted(ctx)) UiHost.Ready += OnReady;
        }

        void OnReady(UiHost host)
        {
            UiHost.Ready -= OnReady;
            if (startChecked) return;
            startChecked = true;
            Check(host?.Context ?? context, false);
        }

        /// <summary>the option is on, no -noUpdateCheck or HS2NEXT_UPDATE_CHECK=off, and the distributed build or -updateCheck.</summary>
        static bool StartCheckWanted(UiContext ctx)
        {
            if (HasArg("-noUpdateCheck")) return false;
            string env = Environment.GetEnvironmentVariable("HS2NEXT_UPDATE_CHECK");
            if (env != null && (env.Equals("off", StringComparison.OrdinalIgnoreCase) || env == "0" || env.Equals("false", StringComparison.OrdinalIgnoreCase))) return false;
            if (ctx.Options != null && !ctx.Options.Bool(OptionDefs.UpdateCheck)) return false;
            return BuildInfo.Current.IsDistribution || HasArg("-updateCheck");
        }

        static void Check(UiContext ctx, bool byHand)
        {
            string url = ArgValue("-updateCheckUrl") ?? UpdateCheckRules.ApiUrl;
            string current = BuildInfo.Current.Version ?? Application.version;
            UnityWebRequest req;
            try
            {
                req = UnityWebRequest.Get(url);
                req.timeout = UpdateCheckRules.TimeoutSeconds;
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
                req.SetRequestHeader("User-Agent", "HS2Next/" + current);
            }
            catch (Exception e)
            {
                Finish(ctx, null, current, byHand, "the request could not be made (" + e.Message + ")");
                return;
            }
            Debug.Log("[update] checking " + url + (byHand ? " (by hand)" : " (at start)"));
            req.SendWebRequest().completed += _ =>
            {
                try
                {
                    ReleaseInfo rel = null;
                    string reason = null;
                    if (req.result != UnityWebRequest.Result.Success)
                        reason = req.responseCode > 0 ? "HTTP " + req.responseCode : (req.error ?? "no reply");
                    else if (req.downloadedBytes > UpdateCheckRules.MaxReplyBytes) reason = "the reply is larger than 1 MB";
                    else rel = ReleaseInfo.Parse(req.downloadHandler.text, out reason);
                    Finish(ctx, rel, current, byHand, reason);
                }
                catch (Exception e) { Debug.LogWarning("[update] " + e.Message); }
                finally { req.Dispose(); }
            };
        }

        static void Finish(UiContext ctx, ReleaseInfo rel, string current, bool byHand, string reason)
        {
            var outcome = rel == null ? UpdateOutcome.Failed : UpdateCheckRules.Compare(current, rel.Tag, out reason);
            string path = StatePath(ctx);
            var state = UpdateCheckState.Load(path);
            state.LastCheckUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
            state.LastResult = UpdateCheckState.ResultName(outcome);
            state.LastTag = rel?.Tag;
            Save(state, path);
            LastOutcome = outcome == UpdateOutcome.Failed ? "failed: " + reason
                : UpdateCheckState.ResultName(outcome) + " " + UpdateCheckRules.Display(outcome == UpdateOutcome.Newer ? rel.Tag : current);
            Debug.Log("[update] " + LastOutcome + " (this build " + current + (rel != null ? ", latest release " + rel.Tag : "") + ")");
            if (!UpdateCheckRules.ShowsDialog(outcome, rel?.Tag, state.Skipped, byHand) || ctx?.Dialogs == null) return;
            var L = ctx.L10n;
            var spec = new DialogSpec { Id = UpdateCheckRules.DialogId, Title = L.Get("dialog.update.title") };
            if (outcome == UpdateOutcome.Newer)
            {
                spec.Message = L.Format("dialog.update.newer", UpdateCheckRules.Display(rel.Tag), current);
                spec.Buttons.Add(DialogButton.Of("open", "dialog.update.open", isDefault: true));
                spec.Buttons.Add(DialogButton.Of("skip", "dialog.update.skip"));
                spec.Buttons.Add(DialogButton.Of("later", "dialog.update.later", isCancel: true));
            }
            else
            {
                spec.Message = outcome == UpdateOutcome.Latest ? L.Format("dialog.update.latest", current) : L.Format("dialog.update.failed", reason);
                spec.Buttons.Add(DialogButton.Of("ok", "common.ok", isDefault: true, isCancel: true));
            }
            ctx.Dialogs.Show(spec, id =>
            {
                if (id == "open") Application.OpenURL(UpdateCheckRules.DownloadPage(rel?.HtmlUrl));
                else if (id == "skip")
                {
                    var s = UpdateCheckState.Load(path);
                    s.Skipped = rel?.Tag;
                    Save(s, path);
                    Debug.Log("[update] skipped " + rel?.Tag);
                }
            });
        }

        static void Save(UpdateCheckState s, string path)
        {
            try { s.Save(path); }
            catch (Exception e) { Debug.LogWarning("[update] " + UpdateCheckRules.StateFileName + " not written: " + e.Message); }
        }

        static string StatePath(UiContext ctx) =>
            System.IO.Path.Combine(ctx?.Options?.ConfigDir ?? System.IO.Path.Combine(ctx?.App?.Paths?.UserData ?? System.IO.Path.GetTempPath(), "config"),
                                   UpdateCheckRules.StateFileName);

        static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static string ArgValue(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-", StringComparison.Ordinal) ? a[i + 1] : null;
        }
    }
}
