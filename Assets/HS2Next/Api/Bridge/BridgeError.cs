// Reply-side types of the bridge contract: errors with a stable code and a
// detail object, the exception a handler throws to answer with such an error, a pre-serialized JSON value, and a
// dictionary reply builder. A handler returns a DTO (public fields and properties), an IDictionary<string, object>,
// a BridgeReply or a RawJson object; the bridge serializes it into the reply envelope, so no JSON-library type
// appears here. Part of the stable API (assembly HS2Next.Api).
using System;
using System.Collections.Generic;

namespace HS2Next.Api.Bridge
{
    /// <summary>
    /// The <c>detail</c> object of an error reply. Null members are left out. <see cref="Extra"/> members are written
    /// next to the standard ones (e.g. <c>ready</c> for not_ready).
    /// </summary>
    public sealed class BridgeErrorDetail
    {
        /// <summary>The argument at fault.</summary>
        public string Arg;
        /// <summary>What was expected (a type name of <see cref="ArgTypes"/>, a range, "one of …").</summary>
        public string Expected;
        /// <summary>Nearest command, argument or object names.</summary>
        public IReadOnlyList<string> Candidates;
        /// <summary>For <see cref="BridgeCodes.Unsupported"/>: the phase that brings the feature (e.g. "3.4").</summary>
        public string Phase;
        /// <summary>Further members (values serialized like handler results).</summary>
        public IDictionary<string, object> Extra;

        /// <summary>True when every member is empty.</summary>
        public bool IsEmpty => Arg == null && Expected == null && (Candidates == null || Candidates.Count == 0) && Phase == null &&
                               (Extra == null || Extra.Count == 0);
    }

    /// <summary>An error reply: <c>{"ok": false, "error": Message, "code": Code, "detail": Detail}</c>.</summary>
    public sealed class BridgeError
    {
        public BridgeError(string code, string message, BridgeErrorDetail detail = null)
        {
            Code = string.IsNullOrEmpty(code) ? BridgeCodes.Internal : code;
            Message = message ?? Code;
            Detail = detail;
        }

        /// <summary>A value of <see cref="BridgeCodes"/>.</summary>
        public string Code { get; }
        /// <summary>Human-readable text (what <c>hs2scene.call</c> prints).</summary>
        public string Message { get; }
        public BridgeErrorDetail Detail { get; }

        public static BridgeError BadArgs(string arg, string expected, string message = null) =>
            new BridgeError(BridgeCodes.BadArgs, message ?? ("bad argument '" + arg + "'" + (expected != null ? ": expected " + expected : "")),
                new BridgeErrorDetail { Arg = arg, Expected = expected });

        public static BridgeError Missing(string arg, string expected = null) =>
            new BridgeError(BridgeCodes.BadArgs, "missing argument '" + arg + "'", new BridgeErrorDetail { Arg = arg, Expected = expected });

        public static BridgeError NotFound(string message, string arg = null, IReadOnlyList<string> candidates = null) =>
            new BridgeError(BridgeCodes.NotFound, message, new BridgeErrorDetail { Arg = arg, Candidates = candidates });

        public static BridgeError WrongKind(string message, string arg = null, string expected = null) =>
            new BridgeError(BridgeCodes.WrongKind, message, new BridgeErrorDetail { Arg = arg, Expected = expected });

        public static BridgeError Unsupported(string message, string phase = null) =>
            new BridgeError(BridgeCodes.Unsupported, message, new BridgeErrorDetail { Phase = phase });

        public static BridgeError IoDenied(string message, string arg = null) =>
            new BridgeError(BridgeCodes.IoDenied, message, new BridgeErrorDetail { Arg = arg });

        public static BridgeError Busy(string message) => new BridgeError(BridgeCodes.Busy, message);

        public override string ToString() => Code + ": " + Message;
    }

    /// <summary>Thrown by a handler (or the argument view) to answer with <see cref="Error"/>.</summary>
    public class BridgeException : Exception
    {
        public BridgeException(BridgeError error) : base(error?.Message) { Error = error ?? new BridgeError(BridgeCodes.Internal, "error"); }
        public BridgeException(string code, string message, BridgeErrorDetail detail = null) : this(new BridgeError(code, message, detail)) { }

        public BridgeError Error { get; }
        public string Code => Error.Code;
    }

    /// <summary>
    /// A JSON value that is already serialized (e.g. a canonical scene document). Nested anywhere in a result it is
    /// written verbatim; as the whole result it must be a JSON object, whose members are merged into the envelope.
    /// <c>ICommandArgs.Get&lt;RawJson&gt;</c> returns the raw text of an argument.
    /// </summary>
    public sealed class RawJson
    {
        public RawJson(string text) { Text = text ?? "null"; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    /// <summary>A dictionary reply with a fluent setter; members keep their insertion order in the reply.</summary>
    public sealed class BridgeReply : Dictionary<string, object>
    {
        public BridgeReply() : base(StringComparer.Ordinal) { }
        public BridgeReply With(string name, object value) { this[name] = value; return this; }
    }
}
