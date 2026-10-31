// The outcome of a StudioOps method: the bus result (Ok / NoOp / Error with a bridge code)
// plus what the callers report — the canonical paths that changed or already had the value, per-target lists, new ids,
// warnings (the bridge forwards them as reply warnings, the UI as toasts) and, for an error, the argument at fault with
// what was expected and candidates (the bridge's error detail; CommandKit.Check(OpResult) turns them back into the same
// BridgeException the handlers threw before the extraction). Unity-free.
using System.Collections.Generic;

namespace HS2Next.Scenes.Runtime
{
    public sealed class OpResult
    {
        static readonly string[] None = new string[0];

        /// <summary>The bus result; never null (an op that did not reach the bus reports Error or NoOp here).</summary>
        public CommandResult Command { get; private set; }
        public CommandStatus Status => Command.Status;
        /// <summary>Not an error (Ok or NoOp).</summary>
        public bool Ok => Command.Ok;
        /// <summary>A bridge code (CommandCodes, or "unavailable" for a target that is not realized) when Status is Error.</summary>
        public string Code => Command.Code;
        public string Message => Command.Message;

        // ---- error detail (the bridge's BridgeErrorDetail members)
        /// <summary>The argument at fault ("set.alpha", "eyesLook", …); null when none.</summary>
        public string Arg;
        /// <summary>What was expected ("0–4", "[1, 1, 1]", an ArgTypes name); null when none.</summary>
        public string Expected;
        public IReadOnlyList<string> Candidates;
        /// <summary>Detail members beyond Arg / Expected / Candidates (e.g. state for "unavailable").</summary>
        public IDictionary<string, object> Extra;

        // ---- what happened
        /// <summary>Canonical paths (or member names, as the op documents) that changed.</summary>
        public readonly List<string> Changed = new List<string>();
        /// <summary>Paths that already had the value.</summary>
        public readonly List<string> NoOp = new List<string>();
        /// <summary>Per target id (node id; "" = the scene): its changed and no-op paths, for ops on several targets.</summary>
        public readonly Dictionary<string, (List<string> changed, List<string> noop)> PerTarget =
            new Dictionary<string, (List<string>, List<string>)>(System.StringComparer.Ordinal);
        /// <summary>Warnings, in order (the bridge: reply warnings; the UI: toasts).</summary>
        public readonly List<string> Warnings = new List<string>();
        /// <summary>Ids of the nodes the op created (adds, duplicate, import), in order.</summary>
        public IReadOnlyList<string> NewIds = None;
        /// <summary>An op-specific payload (KinematicResult, PoseImportReport, the clothes states, …); documented per op.</summary>
        public object Value;

        OpResult(CommandResult r) { Command = r ?? CommandResult.Error(CommandCodes.Internal, "no result"); }

        /// <summary>The result of a bus call (Ok, NoOp or its error), with <paramref name="arg"/> as the argument at fault for an error.</summary>
        public static OpResult From(CommandResult r, string arg = null)
        {
            var o = new OpResult(r);
            if (r != null && r.Status == CommandStatus.Error) o.Arg = arg;
            if (r != null && r.NewIds != null && r.NewIds.Count > 0) o.NewIds = r.NewIds;
            return o;
        }

        public static OpResult Success(CommandResult r = null) => new OpResult(r ?? CommandResult.Success(null));
        public static OpResult NothingToDo() => new OpResult(CommandResult.NoOp(null));

        /// <summary>An error with the bridge detail members.</summary>
        public static OpResult Fail(string code, string message, string arg = null, string expected = null, IReadOnlyList<string> candidates = null) =>
            new OpResult(CommandResult.Error(code, message)) { Arg = arg, Expected = expected, Candidates = candidates };

        /// <summary>bad_args as the bridge words it when no message is given ("bad argument 'x': expected y").</summary>
        public static OpResult BadArgs(string arg, string expected, string message = null) =>
            Fail(CommandCodes.BadArgs, message ?? ("bad argument '" + arg + "'" + (expected != null ? ": expected " + expected : "")), arg, expected);

        public static OpResult NotFound(string message, string arg = null, IReadOnlyList<string> candidates = null) => Fail(CommandCodes.NotFound, message, arg, null, candidates);
        public static OpResult WrongKind(string message, string arg, string expected) => Fail(CommandCodes.WrongKind, message, arg, expected);
        public static OpResult Unsupported(string message) => Fail(CommandCodes.Unsupported, message);

        /// <summary>The "unavailable" error of a target that exists but is not realized, with detail state.</summary>
        public static OpResult Unavailable(string message, string arg, string state)
        {
            var o = Fail(StudioOps.UnavailableCode, message, arg);
            o.Extra = new Dictionary<string, object> { ["state"] = state };
            return o;
        }

        /// <summary>Replaces the bus result (keeps the lists); used when a transaction commit decides the final status.</summary>
        internal OpResult With(CommandResult r)
        {
            Command = r ?? Command;
            if (r != null && r.NewIds != null && r.NewIds.Count > 0 && NewIds.Count == 0) NewIds = r.NewIds;
            return this;
        }

        /// <summary>Adds a path to Changed / NoOp (once) and to the target's lists.</summary>
        internal void Record(string target, string path, CommandStatus s, bool distinct = true)
        {
            var list = s == CommandStatus.NoOp ? NoOp : Changed;
            if (!distinct || !list.Contains(path)) list.Add(path);
            if (target == null) return;
            if (!PerTarget.TryGetValue(target, out var pt)) PerTarget[target] = pt = (new List<string>(), new List<string>());
            (s == CommandStatus.NoOp ? pt.noop : pt.changed).Add(path);
        }

        public override string ToString() => Command.ToString() + (Arg != null ? " (" + Arg + ")" : "");
    }
}
