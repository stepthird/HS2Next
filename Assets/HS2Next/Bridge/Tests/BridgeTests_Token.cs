// The bridge token of the distributed player: the refusal without the token (ping included) and its plain-text 401, a new
// token file at each start written before endpoint.json, and the token file's owner-only permissions outside the user's
// profile folder (read back as the security descriptor icacls saves, which names accounts by SID).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    public static class TokenGroup
    {
        const string Refusal = "unauthorized: header X-HS2Next-Token missing or wrong (see Bridge/token under the user-data folder)";

        public static void Run(BridgeChecks t)
        {
            // refusals: every request without the right header, ping and the empty body included
            using (var s = Kit.NewServer(o => o.RequireToken = true))
            {
                string tok = s.Token;
                t.Check(tok != null && Regex.IsMatch(tok, "^[0-9a-f]{32}$"), "a random token: 32 lower-case hex digits");
                foreach (var body in new[] { "{\"cmd\":\"ping\"}", "", "{\"cmd\":\"bridge.version\"}" })
                {
                    var r = Http.Post(s.Port, body);
                    t.Equal(401, r.Status, "no token -> 401: " + (body.Length == 0 ? "(empty body = ping)" : body));
                    t.Equal(Refusal, (r.Body ?? "").Trim(), "the 401 is the documented plain text");
                    t.Check(r.Headers.TryGetValue("Content-Type", out var ct) && ct.StartsWith("text/plain"), "the 401 is text/plain");
                }
                var wrong = Http.Post(s.Port, "{\"cmd\":\"ping\"}", new Dictionary<string, string> { [BridgeProtocol.TokenHeader] = tok.Substring(0, 31) + (tok[31] == '0' ? "1" : "0") });
                t.Equal(401, wrong.Status, "a wrong token (last digit) -> 401");
                var ok = Http.Post(s.Port, "{\"cmd\":\"ping\"}", new Dictionary<string, string> { [BridgeProtocol.TokenHeader] = tok });
                t.Check(ok.Status == 200 && Kit.Ok(ok.Json), "the right token -> the reply");
                var browser = Http.Post(s.Port, "{\"cmd\":\"ping\"}", new Dictionary<string, string> { [BridgeProtocol.TokenHeader] = tok, ["Origin"] = "http://evil.example" });
                t.Equal(403, browser.Status, "a browser request with the right token is still 403 first");
                t.Check(!Regex.IsMatch(Refusal, @"[A-Za-z]:[\\/]|[\\/](?:Users|home)[\\/]", RegexOptions.IgnoreCase), "the refusal names no path");
            }

            // the token file: written before endpoint.json, a new value at each start, kept after stop
            string root = Path.Combine(t.TempDir, "tk_userdata_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string tokFile = Path.Combine(root, "Bridge", "token"), ep = Path.Combine(root, "Bridge", "endpoint.json");
            string first;
            using (var s = Kit.NewServer(o => { o.UserDataRoot = root; o.RequireToken = true; }))
            {
                first = File.Exists(tokFile) ? File.ReadAllText(tokFile).Trim() : null;
                t.Equal(s.Token, first, "start 1: the token file holds the token");
                var j = File.Exists(ep) ? JObject.Parse(File.ReadAllText(ep)) : null;
                t.Check(j != null && (bool)j["tokenRequired"], "start 1: endpoint.json says tokenRequired");
                t.Check(File.Exists(tokFile) && File.Exists(ep) && File.GetLastWriteTimeUtc(tokFile) <= File.GetLastWriteTimeUtc(ep), "the token file is written before endpoint.json");
                t.Check(Directory.GetFiles(Path.Combine(root, "Bridge"), "*.tmp*").Length == 0, "no temporary file is left");
            }
            t.Check(File.Exists(tokFile) && !File.Exists(ep), "after stop: the token file stays, endpoint.json is removed");
            using (var s = Kit.NewServer(o => { o.UserDataRoot = root; o.RequireToken = true; }))
            {
                string second = File.ReadAllText(tokFile).Trim();
                t.Check(second == s.Token && second != first && Regex.IsMatch(second, "^[0-9a-f]{32}$"), "start 2: a new token replaces the file");
            }
            using (var s = Kit.NewServer(o => { o.UserDataRoot = root; o.RequireToken = true; o.Token = "configured-token"; }))
                t.Equal("configured-token", File.ReadAllText(tokFile).Trim(), "a configured token is written as given");
            using (var s = Kit.NewServer(o => { o.UserDataRoot = root; }))
            {
                var j = JObject.Parse(File.ReadAllText(ep));
                t.Check(!(bool)j["tokenRequired"] && s.TokenAcl == TokenFileAcl.StateNone, "no token: endpoint.json says so, no token file written (state none)");
            }

            // where the explicit list applies
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string perUser = Path.Combine(profile, "AppData", "LocalLow", "stp3", "HS2Next", "UserData", "Bridge");
            t.Check(TokenFileAcl.UnderUserProfile(perUser) && !TokenFileAcl.Restricts(TokenAclMode.Auto, perUser), "Auto under the profile (the per-user root): inherited");
            string outside = Path.Combine(Path.GetPathRoot(profile) ?? Path.GetPathRoot(Path.GetTempPath()), "Games", "HS2Next", "UserData", "Bridge");
            t.Check(!TokenFileAcl.UnderUserProfile(outside) && TokenFileAcl.Restricts(TokenAclMode.Auto, outside) == TokenFileAcl.Supported, "Auto outside the profile (a portable folder): owner-only");
            t.Check(!TokenFileAcl.UnderUserProfile(profile + "X\\UserData"), "a sibling folder whose name starts like the profile is outside it");
            t.Check(TokenFileAcl.Restricts(TokenAclMode.OwnerOnly, perUser) == TokenFileAcl.Supported && !TokenFileAcl.Restricts(TokenAclMode.Inherit, outside), "OwnerOnly / Inherit win over the place");

            if (!TokenFileAcl.Supported) { t.Info("not Windows: the token file's access list is not checked"); return; }

            // the explicit list, read back: protected (no inherited entry), only the user, SYSTEM and Administrators
            string acl = Path.Combine(t.TempDir, "tk_acl_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var sw = Stopwatch.StartNew();
            using (var s = Kit.NewServer(o => { o.UserDataRoot = acl; o.RequireToken = true; o.TokenAcl = TokenAclMode.OwnerOnly; }))
            {
                long ms = sw.ElapsedMilliseconds;
                string f = Path.Combine(acl, "Bridge", "token");
                t.Equal(TokenFileAcl.StateOwnerOnly, s.TokenAcl, "OwnerOnly: the server reports ownerOnly");
                t.Equal(s.Token, File.Exists(f) ? File.ReadAllText(f).Trim() : null, "OwnerOnly: the owner still reads the token");
                string sddl = SavedDacl(t, f);
                t.Info("start with the owner-only list: " + ms + " ms");
                if (sddl != null)
                {
                    var aces = Regex.Matches(sddl, @"\(([^)]*)\)");
                    var trustees = new List<string>();
                    bool inherited = false;
                    foreach (Match m in aces)
                    {
                        var parts = m.Groups[1].Value.Split(';');
                        if (parts.Length >= 6) { trustees.Add(parts[5]); if (parts[1].Contains("ID")) inherited = true; }
                    }
                    t.Check(sddl.StartsWith("D:P"), "the list is protected (inheritance off)");
                    t.Check(!inherited, "no inherited entry");
                    t.Check(!trustees.Exists(x => x == "BU" || x == "AU" || x == "WD" || x == "S-1-5-32-545" || x == "S-1-5-11" || x == "S-1-1-0"),
                        "no entry for Users, Authenticated Users or Everyone");
                    t.Check(trustees.Contains("SY") && trustees.Contains("BA") && trustees.Count == 3, "exactly the user, SYSTEM and Administrators (" + trustees.Count + " entries)");
                }
            }
            // the same folder without the list: the default of a folder under the temp folder (inside the profile) is inherited
            using (var s = Kit.NewServer(o => { o.UserDataRoot = acl; o.RequireToken = true; o.TokenAcl = TokenAclMode.Inherit; }))
            {
                t.Equal(TokenFileAcl.StateInherited, s.TokenAcl, "Inherit: the server reports inherited");
                string sddl = SavedDacl(t, Path.Combine(acl, "Bridge", "token"));
                t.Check(sddl != null && !sddl.StartsWith("D:P"), "Inherit: a rewritten token file takes its folder's list again");
            }
            // a failure to set the list is a warning, not a failed start
            t.Check(TokenFileAcl.RestrictToOwner(Path.Combine(acl, "no-such-file")) != null, "RestrictToOwner reports a missing file");
        }

        /// <summary>The DACL part of the security descriptor icacls saves for <paramref name="file"/> ("D:..."), or null.</summary>
        static string SavedDacl(BridgeChecks t, string file)
        {
            string save = Path.Combine(Path.GetDirectoryName(file), "..", "acl_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
            try
            {
                var psi = new ProcessStartInfo("icacls.exe", "\"" + file + "\" /save \"" + Path.GetFullPath(save) + "\" /q")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(10000) || p.ExitCode != 0) { t.Check(false, "icacls /save exit " + (p.HasExited ? p.ExitCode.ToString() : "timeout")); return null; }
                }
                string text = File.ReadAllText(Path.GetFullPath(save), Encoding.Unicode);
                int i = text.IndexOf("D:", StringComparison.Ordinal);
                if (i < 0) { t.Check(false, "icacls /save gave no DACL"); return null; }
                return text.Substring(i).Split('\r', '\n')[0].Trim();
            }
            catch (Exception e) { t.Check(false, "icacls /save: " + e.Message); return null; }
            finally { try { File.Delete(Path.GetFullPath(save)); } catch (Exception) { } }
        }
    }
}
