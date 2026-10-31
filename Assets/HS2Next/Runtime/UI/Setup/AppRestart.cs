// Restarting the app (the setup banner's guide button, File ▸ Setup guide, the Options panel's Restart now). The quit comes first
// and the restart only follows a quit that really happens: Request asks about unsaved changes (Save / Don't save / Cancel, the
// quit's own prompt; Cancel keeps the app running with its UserData lock and its bridge), then arms the restart and quits. The
// app's own quit then releases everything (AppCore.Dispose: plugins, bridge, bundles, the lock) and, as its last step, starts a
// small helper that waits until this process has ended and only then starts the same exe with the same arguments (some flags
// removed or added) and "-waitPid <this pid>", so the new app also waits, bounded, for this process and for the UserData lock
// before its settings stage. Two apps therefore never share one UserData, a port or one Player.log. An Editor play session cannot
// restart itself: the caller is told how instead.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using HS2Next.App;
using HS2Next.App.Setup;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.UI.Setup
{
    public static class AppRestart
    {
        /// <summary>The command-line flag of a restarted app: the PID of the app it replaces (AppCore waits for it).</summary>
        public const string WaitPidFlag = CommandLine.WaitPidFlag;

        public static bool CanRestart => !Application.isEditor;

        /// <summary>The restart armed for this process's quit (null when none): the exe and its argument line.</summary>
        public static string ArmedExe { get; private set; }
        public static string ArmedArgs { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ArmedExe = null; ArmedArgs = null; }

        /// <summary>
        /// Asks about unsaved changes (the quit's prompt), then arms the restart and quits; Cancel keeps the app running and arms
        /// nothing. Returns why it cannot restart (the Editor, an unknown exe), else null (the request is under way).
        /// </summary>
        public static string Request(UiContext ctx, IEnumerable<string> remove = null, IEnumerable<string> add = null)
        {
            if (Application.isEditor) return L10n.Get("setup.restart.editor");
            string exe;
            try { exe = Process.GetCurrentProcess().MainModule?.FileName; }
            catch (Exception e) { return "the program file is unknown: " + e.Message; }
            if (string.IsNullOrEmpty(exe)) return "the program file is unknown";
            string args = CommandLine.Join(CommandLine.Restart(Environment.GetCommandLineArgs(), remove, add));
            void Go()
            {
                ArmedExe = exe;
                ArmedArgs = args;
                Debug.Log("[app] restart armed: " + System.IO.Path.GetFileName(exe) + " " + args + " (starts after this process has ended)");
                // the prompt has been answered (or there was nothing to save): the quit goes through
                HS2Next.App.Commands.AppCommands.QuitRequested = true;
                Application.Quit();
            }
            var files = SceneFiles.Current;
            if (files != null && ctx?.Dialogs != null) files.IfSaved(Go, "setup.restart.unsaved");
            else Go();
            return null;
        }

        /// <summary>Restarts into the first-run guide (-setup).</summary>
        public static string RequestGuide(UiContext ctx) => Request(ctx, null, new[] { SetupStage.Flag });

        /// <summary>
        /// The last step of the app's quit (AppCore.Dispose): when a restart is armed, starts the helper that waits for this process
        /// to end and then starts the new one (a direct start with -waitPid when the helper cannot run). Never throws.
        /// </summary>
        public static void StartArmed()
        {
            string exe = ArmedExe, args = ArmedArgs;
            if (exe == null) return;
            ArmedExe = ArmedArgs = null;
            int pid;
            try { pid = Process.GetCurrentProcess().Id; } catch (Exception) { pid = 0; }
            string full = (args.Length > 0 ? args + " " : "") + WaitPidFlag + " " + pid;
            string wd = Environment.CurrentDirectory;
            try
            {
                // Wait-Process (bounded) then Start-Process; the command goes base64-encoded, so no quoting reaches the shell
                string script = "Wait-Process -Id " + pid + " -Timeout 60 -ErrorAction SilentlyContinue; " +
                                "Start-Process -FilePath " + PsQuote(exe) + " -ArgumentList " + PsQuote(full) + " -WorkingDirectory " + PsQuote(wd);
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded)
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = wd,
                };
                Process.Start(psi);
                Debug.Log("[app] restart: the new process starts once PID " + pid + " has ended");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[app] restart: the waiting helper could not start (" + e.Message + "); the new process starts now and waits itself");
                try { Process.Start(new ProcessStartInfo(exe, full) { UseShellExecute = false, WorkingDirectory = wd }); }
                catch (Exception e2) { Debug.LogError("[app] restart failed: " + e2.Message); }
            }
        }

        static string PsQuote(string s) => "'" + (s ?? "").Replace("'", "''") + "'";
    }
}
