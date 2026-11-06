// Automation-bridge protocol constants: the protocol number, the default port,
// the HTTP header names, the stable error codes, the envelope fields every request may carry and the owner names
// of built-in commands. Part of the stable API (assembly HS2Next.Api); no JSON library and no Core types.
using System;
using System.Collections.Generic;

namespace HS2Next.Api.Bridge
{
    /// <summary>Protocol-level constants. <see cref="Version"/> changes only when the envelope changes.</summary>
    public static class BridgeProtocol
    {
        /// <summary>The envelope version reported by <c>ping</c> and <c>bridge.version</c> as <c>protocol</c>.</summary>
        public const int Version = 1;
        /// <summary>Default listening port.</summary>
        public const int DefaultPort = 17322;
        /// <summary>Request header carrying the token when the bridge requires one.</summary>
        public const string TokenHeader = "X-HS2Next-Token";
        /// <summary>Response header naming the bridge version on every reply.</summary>
        public const string BridgeHeader = "X-HS2Next-Bridge";
        /// <summary>Per-request timeout when neither the request nor the command gives one, seconds.</summary>
        public const double DefaultTimeoutSec = 60;
        /// <summary>Upper bound of any request timeout, seconds.</summary>
        public const double MaxTimeoutSec = 600;
        /// <summary>Largest accepted request body, bytes (16 MiB).</summary>
        public const long MaxBodyBytes = 16L << 20;
    }

    /// <summary>
    /// The stable values of the reply field <c>code</c>. <c>error</c> stays a human-readable message; scripts
    /// branch on <c>code</c>. New codes may be added in a minor version; existing ones never change meaning.
    /// </summary>
    public static class BridgeCodes
    {
        /// <summary>The body is not a JSON object (HTTP 400).</summary>
        public const string BadJson = "bad_json";
        /// <summary>No command of that name (detail.candidates: nearest names).</summary>
        public const string UnknownCommand = "unknown_command";
        /// <summary>An argument is missing, of the wrong type or out of range (detail.arg, detail.expected).</summary>
        public const string BadArgs = "bad_args";
        /// <summary>A named thing (node, file, list row, help entry) does not exist (detail.candidates when known).</summary>
        public const string NotFound = "not_found";
        /// <summary>The target exists but is of another kind (e.g. an item key given to a character command).</summary>
        public const string WrongKind = "wrong_kind";
        /// <summary>The app is not ready for this command yet (detail.extra.ready: the stages).</summary>
        public const string NotReady = "not_ready";
        /// <summary>The bridge or the model cannot take the request now (queue full, a re-entrant call).</summary>
        public const string Busy = "busy";
        /// <summary>The request did not complete within its timeout.</summary>
        public const string Timeout = "timeout";
        /// <summary>A file path argument points outside the allowed roots, or would overwrite without <c>overwrite</c>.</summary>
        public const string IoDenied = "io_denied";
        /// <summary>The feature belongs to a later phase or is switched off (detail.phase).</summary>
        public const string Unsupported = "unsupported";
        /// <summary>A plugin-owned handler failed.</summary>
        public const string PluginFault = "plugin_fault";
        /// <summary>A handler of the app or of the bridge failed unexpectedly.</summary>
        public const string Internal = "internal";
        /// <summary>The target exists but is not realized (e.g. a character node whose build failed or is pending; detail.extra.state).</summary>
        public const string Unavailable = "unavailable";

        static readonly string[] all =
        {
            BadJson, UnknownCommand, BadArgs, NotFound, WrongKind, NotReady, Busy, Timeout, IoDenied, Unsupported,
            PluginFault, Internal, Unavailable,
        };

        /// <summary>Every code of this API version, in declaration order.</summary>
        public static IReadOnlyList<string> All => all;

        /// <summary>True for a code of this API version.</summary>
        public static bool IsKnown(string code) => code != null && Array.IndexOf(all, code) >= 0;
    }

    /// <summary>
    /// Members of a request object that belong to the envelope rather than to the command. They are never
    /// reported as unknown arguments and a command spec may not declare them as arguments.
    /// </summary>
    public static class EnvelopeFields
    {
        /// <summary>The command name.</summary>
        public const string Cmd = "cmd";
        /// <summary>Any JSON value; echoed back in the reply.</summary>
        public const string Id = "id";
        /// <summary>Frames to delay the reply by after the handler completed (a command may consume it itself).</summary>
        public const string WaitFrames = "waitFrames";
        /// <summary>Request timeout in seconds (0 &lt; t ≤ <see cref="BridgeProtocol.MaxTimeoutSec"/>).</summary>
        public const string Timeout = "timeout";
        /// <summary><c>true</c>: unknown arguments are an error instead of a warning.</summary>
        public const string Strict = "strict";
        /// <summary>Batch members only: names the member's result for <c>"$name"</c> substitution in later members.</summary>
        public const string As = "as";
        /// <summary><c>false</c>: a mutating command is applied without an undo entry (unless a scene transaction is open).</summary>
        public const string Undo = "undo";

        static readonly string[] all = { Cmd, Id, WaitFrames, Timeout, Strict, As, Undo };

        /// <summary>Every envelope field name.</summary>
        public static IReadOnlyList<string> All => all;

        /// <summary>True when <paramref name="name"/> is an envelope field (ordinal).</summary>
        public static bool Contains(string name) => name != null && Array.IndexOf(all, name) >= 0;
    }

    /// <summary>Owner names of built-in commands. Plugins register under their plugin GUID.</summary>
    public static class BridgeOwners
    {
        /// <summary>The bridge's own commands (ping, help, bridge.version, batch, log.tail).</summary>
        public const string Bridge = "bridge";
        /// <summary>Commands registered by the app itself.</summary>
        public const string Core = "core";
    }
}
