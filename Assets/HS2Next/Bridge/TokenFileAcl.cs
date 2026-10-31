// The permissions of the bridge's token file. Under the user's profile folder the file inherits the profile's permissions
// (the user, SYSTEM and Administrators), so other standard users cannot read it. Anywhere else - a portable app folder, a
// test root, another drive - the inherited entries usually let every local user read it, so the file gets an explicit
// access list: inheritance off, full control for the current user, SYSTEM and Administrators only. The list is set with
// Windows' icacls (the managed access-control types are not available to the app), judged by its exit code (its text is
// localised). Failing to set it is a warning, never fatal: the token still guards the bridge against other processes that
// cannot read the file.
using System;
using System.Diagnostics;
using System.IO;

namespace HS2Next.Bridge
{
    /// <summary>How the token file's permissions are chosen.</summary>
    public enum TokenAclMode
    {
        /// <summary>Owner-only outside the user's profile folder; inherited under it.</summary>
        Auto,
        /// <summary>The folder's permissions, unchanged.</summary>
        Inherit,
        /// <summary>Always the explicit owner-only list.</summary>
        OwnerOnly,
    }

    public static class TokenFileAcl
    {
        /// <summary>The state reported for a token file: no file, the folder's permissions, or the explicit owner-only list.</summary>
        public const string StateNone = "none", StateInherited = "inherited", StateOwnerOnly = "ownerOnly";

        /// <summary>True on Windows (the only place the list is set).</summary>
        public static bool Supported => Environment.OSVersion.Platform == PlatformID.Win32NT;

        /// <summary>Whether a token file in <paramref name="dir"/> gets the explicit list under <paramref name="mode"/>.</summary>
        public static bool Restricts(TokenAclMode mode, string dir)
        {
            if (!Supported || mode == TokenAclMode.Inherit) return false;
            if (mode == TokenAclMode.OwnerOnly) return true;
            return !UnderUserProfile(dir);
        }

        /// <summary>True when <paramref name="path"/> lies inside the current user's profile folder (case-insensitive).</summary>
        public static bool UnderUserProfile(string path)
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile) || string.IsNullOrEmpty(path)) return false;
            string p = Path.GetFullPath(path).TrimEnd('\\', '/') + "\\";
            string q = Path.GetFullPath(profile).TrimEnd('\\', '/') + "\\";
            return p.StartsWith(q, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The account icacls grants: DOMAIN\user of this process.</summary>
        public static string CurrentAccount()
        {
            string user = Environment.UserName;
            string domain = Environment.UserDomainName;
            return string.IsNullOrEmpty(domain) ? user : domain + "\\" + user;
        }

        /// <summary>
        /// Removes the inherited entries of <paramref name="file"/> and grants full control to the current user, SYSTEM and
        /// Administrators only. Returns null when icacls exited 0, else the reason (exit code, timeout or start failure).
        /// </summary>
        public static string RestrictToOwner(string file, int timeoutMs = 10000)
        {
            if (!Supported) return "not Windows";
            if (!File.Exists(file)) return "no such file";
            string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string icacls = string.IsNullOrEmpty(system) ? "icacls.exe" : Path.Combine(system, "icacls.exe");
            var psi = new ProcessStartInfo
            {
                FileName = icacls,
                Arguments = Quote(file) + " /inheritance:r /grant:r " + Quote(CurrentAccount() + ":F") + " *S-1-5-18:F *S-1-5-32-544:F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    if (p == null) return "icacls did not start";
                    // drain the localised output so a full pipe cannot block icacls
                    p.OutputDataReceived += (s, e) => { };
                    p.ErrorDataReceived += (s, e) => { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        return "icacls did not finish in " + timeoutMs + " ms";
                    }
                    return p.ExitCode == 0 ? null : "icacls exit " + p.ExitCode;
                }
            }
            catch (Exception e) { return "icacls: " + e.GetType().Name + ": " + e.Message; }
        }

        static string Quote(string s) => "\"" + s.Replace("\"", "") + "\"";
    }
}
