// Outcome of a bus operation: Ok, NoOp (the net effect was nothing: no
// history, no events) or Error with one of the bridge codes. Commands report
// validation failures by throwing CommandException; the bus turns it into an Error result.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes
{
    public enum CommandStatus { Ok, NoOp, Error }

    /// <summary>Error codes of command results (the bridge codes).</summary>
    public static class CommandCodes
    {
        public const string BadArgs = "bad_args";
        public const string NotFound = "not_found";
        public const string WrongKind = "wrong_kind";
        public const string Unsupported = "unsupported";
        public const string Busy = "busy";
        public const string Internal = "internal";
    }

    /// <summary>A validation failure of a command (no model change happened).</summary>
    public sealed class CommandException : Exception
    {
        /// <summary>One of <see cref="CommandCodes"/>.</summary>
        public string Code { get; }
        public CommandException(string code, string message) : base(message) { Code = code; }
    }

    public sealed class CommandResult
    {
        static readonly string[] NoIds = new string[0];

        public CommandStatus Status { get; }
        /// <summary>One of <see cref="CommandCodes"/> when <see cref="Status"/> is Error, else null.</summary>
        public string Code { get; }
        public string Message { get; }
        /// <summary>The command executed (for a merged edit: the history entry's command), or null.</summary>
        public ISceneCommand Command { get; }
        /// <summary>Ids of the top nodes an add or duplicate created, in order (empty otherwise).</summary>
        public IReadOnlyList<string> NewIds { get; }

        public CommandResult(CommandStatus status, string code, string message, ISceneCommand command, IReadOnlyList<string> newIds = null)
        {
            Status = status;
            Code = code;
            Message = message ?? "";
            Command = command;
            NewIds = newIds ?? NoIds;
        }

        /// <summary>Not an error (Ok or NoOp).</summary>
        public bool Ok => Status != CommandStatus.Error;
        /// <summary>The first created id, or null.</summary>
        public string NewId => NewIds.Count > 0 ? NewIds[0] : null;

        public static CommandResult Success(ISceneCommand command, IReadOnlyList<string> newIds = null) => new CommandResult(CommandStatus.Ok, null, null, command, newIds);
        public static CommandResult NoOp(ISceneCommand command) => new CommandResult(CommandStatus.NoOp, null, null, command);
        public static CommandResult Error(string code, string message, ISceneCommand command = null) => new CommandResult(CommandStatus.Error, code, message, command);

        public override string ToString() => Status == CommandStatus.Error ? "error " + Code + ": " + Message : Status.ToString();
    }
}
