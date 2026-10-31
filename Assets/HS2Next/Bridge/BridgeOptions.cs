// Configuration of the automation bridge: a plain options object that the
// Core bootstrap fills from its settings (`bridge {enabled, port, token, outputDir, allowQuit}`) and the command line
// (ApplyCommandLine). The bridge knows no Core types; everything it needs arrives here. The defaults are the protocol
// defaults; the limits and timings below them exist for tests.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HS2Next.Api.Bridge;

namespace HS2Next.Bridge
{
    public enum BridgeLogLevel { Info, Warning, Error }

    public sealed class BridgeOptions
    {
        /// <summary>On by default in every build.</summary>
        public bool Enabled = true;
        /// <summary>Listening port on 127.0.0.1; 0 picks a free port (tests).</summary>
        public int Port = BridgeProtocol.DefaultPort;
        /// <summary>
        /// True: requests need the header X-HS2Next-Token. The token is <see cref="Token"/>, or a random 128-bit value
        /// when that is empty; it is written to <c>&lt;UserDataRoot&gt;/Bridge/token</c> at start.
        /// </summary>
        public bool RequireToken;
        /// <summary>A fixed token (used only with <see cref="RequireToken"/>).</summary>
        public string Token;
        /// <summary>The token file's permissions: Auto = owner-only outside the user's profile folder, inherited under it.</summary>
        public TokenAclMode TokenAcl = TokenAclMode.Auto;
        /// <summary>
        /// Root of bridge file output (captures under <c>shots/</c>, dumps). Absolute, or relative to
        /// <see cref="UserDataRoot"/>; null = <c>&lt;UserDataRoot&gt;/Bridge</c>; no root at all = file output denied.
        /// </summary>
        public string OutputDir;
        /// <summary>Lets Core's <c>app.quit</c> end the app (unattended runs). The bridge itself only reports it.</summary>
        public bool AllowQuit;
        /// <summary>HS2Next's user-data folder: <c>Bridge/endpoint.json</c> and <c>Bridge/token</c> live below it.</summary>
        public string UserDataRoot;
        /// <summary>The folder of endpoint.json and the token file (absolute); null = <c>&lt;UserDataRoot&gt;/Bridge</c>.</summary>
        public string BridgeDir;
        /// <summary>Writes <c>&lt;UserDataRoot&gt;/Bridge/endpoint.json</c> while listening (removed at stop).</summary>
        public bool WriteEndpointFile = true;

        /// <summary>Readiness stages declared at construction: required for the app / informational (ping.ready).</summary>
        public string[] RequiredStages = { "catalog", "studioDb" };
        public string[] OptionalStages = { "scene" };

        /// <summary>Reported by ping and bridge.version.</summary>
        public string AppName = "HS2Next";
        public string AppVersion;
        public string UnityVersion;
        /// <summary>The facts of this build (kind, version, commit) for bridge.version.build; null = not reported.</summary>
        public BuildFacts Build;

        // ---- limits and timings (protocol defaults)
        public double DefaultTimeoutSec = BridgeProtocol.DefaultTimeoutSec;
        public double MaxTimeoutSec = BridgeProtocol.MaxTimeoutSec;
        public long MaxBodyBytes = BridgeProtocol.MaxBodyBytes;
        /// <summary>Main-thread time the pump may spend on new queue entries per frame, milliseconds.</summary>
        public double PumpBudgetMs = 30;
        /// <summary>Bind attempts after the first failure, and the pause between them.</summary>
        public int BindRetries = 5;
        public int BindRetryDelayMs = 200;
        /// <summary>Requests waiting in the queue beyond this answer busy.</summary>
        public int QueueLimit = 1000;
        /// <summary>Entries kept for log.tail.</summary>
        public int LogCapacity = 2000;
        /// <summary>How often pending requests are checked against their deadlines, milliseconds.</summary>
        public int ReaperIntervalMs = 50;

        /// <summary>Where the bridge reports its own events (bind, failures). Null = none. Called from any thread.</summary>
        public Action<BridgeLogLevel, string> Logger;

        /// <summary>A copy (the logger and the stage arrays are shared).</summary>
        public BridgeOptions Clone() => (BridgeOptions)MemberwiseClone();

        /// <summary>
        /// The output root this configuration gives (absolute), or null: OutputDir when absolute; relative to
        /// UserDataRoot (or else BridgeDir); without OutputDir the discovery folder.
        /// </summary>
        public string ResolveOutputDir()
        {
            if (!string.IsNullOrEmpty(OutputDir))
            {
                if (Path.IsPathRooted(OutputDir)) return Path.GetFullPath(OutputDir);
                string baseDir = !string.IsNullOrEmpty(UserDataRoot) ? UserDataRoot : BridgeDir;
                return string.IsNullOrEmpty(baseDir) ? null : Path.GetFullPath(Path.Combine(baseDir, OutputDir));
            }
            return DiscoveryDir();
        }

        /// <summary>The folder of endpoint.json and the token file, or null.</summary>
        public string DiscoveryDir() =>
            !string.IsNullOrEmpty(BridgeDir) ? Path.GetFullPath(BridgeDir)
            : string.IsNullOrEmpty(UserDataRoot) ? null : Path.GetFullPath(Path.Combine(UserDataRoot, "Bridge"));

        /// <summary>
        /// Applies the bridge's command-line switches: <c>-bridge</c> (enable), <c>-noBridge</c> (disable),
        /// <c>-bridgePort N</c>, <c>-bridgeToken [value]</c> (require a token; a value that does not start with '-'
        /// is the token). Unknown arguments are ignored. Returns this.
        /// </summary>
        public BridgeOptions ApplyCommandLine(IReadOnlyList<string> args)
        {
            if (args == null) return this;
            for (int i = 0; i < args.Count; i++)
            {
                string a = args[i];
                if (string.Equals(a, "-bridge", StringComparison.OrdinalIgnoreCase)) Enabled = true;
                else if (string.Equals(a, "-noBridge", StringComparison.OrdinalIgnoreCase)) Enabled = false;
                else if (string.Equals(a, "-bridgePort", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count &&
                         int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p >= 0 && p <= 65535)
                {
                    Port = p;
                    i++;
                }
                else if (string.Equals(a, "-bridgeToken", StringComparison.OrdinalIgnoreCase))
                {
                    RequireToken = true;
                    if (i + 1 < args.Count && args[i + 1].Length > 0 && args[i + 1][0] != '-') Token = args[++i];
                }
            }
            return this;
        }
    }

    /// <summary>Version of the HS2Next.Bridge implementation (reply header X-HS2Next-Bridge, bridge.version).</summary>
    public static class BridgeVersion
    {
        public const string Semver = "0.1.0";
    }
}
