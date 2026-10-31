// The crash quarantine as the bridge sees it (group "content"; owner core, Since 1.0.0; CoreCommands.Phase52Names):
//   content.quarantine  the bundles refused because a process ended while it loaded them (<UserData>/content/quarantine.tsv),
//                       and with clear one of them lifted (by its key or its key without the zipmod's folders)
// Lifting an entry lets the next load open the bundle again (and a crash quarantines it again).
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Content;

namespace HS2Next.App.Commands
{
    public static class QuarantineCommands
    {
        /// <summary>The commands of this file (in CoreCommands.Phase52Names).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "content.quarantine" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("content.quarantine", "The bundles refused because a process ended while it loaded them (the crash quarantine of the user folder); with clear, one of them is lifted so that the next load opens it again. An entry also lifts itself when its mod file changes.", ContentCommands.Group)
                .Arg("clear", ArgTypes.String, "a quarantined bundle's key (\"<zipmod file>!<bundle path>\", the folders may be left out) to lift", false)
                .Return("enabled", ArgTypes.Bool, "the quarantine is on (the app turns it on at start; test hosts do not)")
                .Return("cleared", ArgTypes.Bool, "with clear: an entry was lifted", false)
                .Return("entries", ArgTypes.ArrayOf(ArgTypes.Object), "{key (zipmod file name!path), stamp (size:time of the mod file), since}")
                .Return("lastRun", ArgTypes.Object, "{key, stamp, since}: the entry the previous run's end added at this start, or null")
                .Example("{\"cmd\":\"content.quarantine\"}")
                .Example("{\"cmd\":\"content.quarantine\",\"clear\":\"[Mod] Example.zipmod!chara/example.unity3d\"}")
                .WithGate(ReadyGate.None), Quarantine);
        }

        static object Quarantine(CoreContext c)
        {
            var q = c.Services?.Catalog?.Quarantine;
            var r = new BridgeReply().With("enabled", q != null);
            string clear = c.Args.Get<string>("clear", null);
            if (clear != null) r["cleared"] = c.Services?.Catalog != null && c.Services.Catalog.ClearQuarantine(clear);
            var l = new List<object>();
            if (q != null)
                foreach (var e in q.Entries) l.Add(Entry(e));
            r["entries"] = l;
            r["lastRun"] = q?.FromLastRun != null ? Entry(q.FromLastRun) : null;
            return r;
        }

        static BridgeReply Entry(QuarantineEntry e) =>
            new BridgeReply().With("key", CrashQuarantine.ShortKey(e.Key)).With("stamp", e.Stamp).With("since", e.Since);
    }
}
