// The bridge's UI automation commands (U31): ui.state, ui.action, ui.panel, ui.query,
// ui.input, ui.screenshot. Registered with the Core commands (always; they answer `unavailable` while no UI runs, e.g.
// -noUi and test hosts); events are synthesised through the panel (SendEvent with pooled events), never the OS.
// They pass the readiness gate at every stage (Phase 5.3): while the setup host runs (the first-run guide or the progress
// page of a long scan) they drive its panel; while the app starts with the UI on and no panel is up yet they answer
// not_ready.
// This file: the host links (Attach / Detach / Tick, the setup host); the command bodies follow in UiCommands.Handlers.
using System.Collections.Generic;
using HS2Next.UI.Setup;

namespace HS2Next.UI.Bridge
{
    public static partial class UiCommands
    {
        /// <summary>The host the commands talk to (null when no UI runs).</summary>
        public static UiHost Host { get; private set; }

        /// <summary>The setup host while it runs (it takes the commands before the Studio UI).</summary>
        public static SetupHost Setup { get; private set; }

        /// <summary>The setup pages shown in this process, in order (ui.state's setup.pagesSeen).</summary>
        public static readonly List<string> SetupPagesSeen = new List<string>();

        internal static void Attach(UiHost h) => Host = h;

        internal static void Detach(UiHost h)
        {
            if (Host == h) Host = null;
        }

        internal static void AttachSetup(SetupHost h) => Setup = h;

        internal static void DetachSetup(SetupHost h)
        {
            if (Setup == h) Setup = null;
        }

        internal static void SetupPageChanged(string page)
        {
            lock (SetupPagesSeen) SetupPagesSeen.Add(page);
        }

        /// <summary>The setup host runs with its panel up.</summary>
        static bool SetupActive => Setup != null && !Setup.Closed && Setup.IsReady && Setup.Root != null;

        /// <summary>Per frame (pending ui.input frames, screenshots).</summary>
        internal static void Tick(UiHost h) => TickPending(h);

        static partial void TickPendingImpl(UiHost h);
        static void TickPending(UiHost h) => TickPendingImpl(h);
    }
}
