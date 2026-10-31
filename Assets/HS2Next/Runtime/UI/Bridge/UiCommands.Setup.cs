// The ui.* commands while the setup host runs (Phase 5.3): ui.state answers the guide's state, ui.action presses a control of
// the guide by its element name (a button is clicked; a text field gets args.value as if typed and committed; a toggle
// args.value), or switches the language (ui.language {language}). ui.query and ui.input share the Studio's code
// (UiCommands.Handlers) on the setup panel.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App.Commands;
using HS2Next.UI.Core;
using HS2Next.UI.Setup;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace HS2Next.UI.Bridge
{
    public static partial class UiCommands
    {
        static object SetupState()
        {
            var s = Setup;
            var f = s.Focused;
            List<string> seen;
            lock (SetupPagesSeen) seen = SetupPagesSeen.ToList();
            return new BridgeReply()
                .With("enabled", true)
                .With("mode", "setup")
                .With("page", s.Page)
                .With("pagesSeen", seen)
                .With("outcome", s.Outcome.ToString().ToLowerInvariant())
                .With("progressOnly", s.Context?.ProgressOnly ?? false)
                .With("language", L10n.Language)
                .With("focus", f == null ? null : string.IsNullOrEmpty(f.name) ? f.GetType().Name : f.name)
                .With("modal", s.Dialogs?.Current?.Spec.Id)
                .With("written", s.Guide?.Written.ToList())
                .With("screen", new[] { UnityEngine.Screen.width, UnityEngine.Screen.height })
                .With("root", Rect(s.Root.worldBound))
                .With("firstFrameMs", Math.Round(s.FirstFrameMs, 1))
                .With("toasts", s.Toasts == null ? new List<string>() : s.Toasts.History.Skip(Math.Max(0, s.Toasts.History.Count - 5)).ToList());
        }

        static object SetupAction(CoreContext c)
        {
            var s = Setup;
            string id = c.Args.Require<string>("action");
            JObject args = null;
            if (c.Args.Has("args"))
            {
                var raw = c.Args.RawJson("args");
                if (!string.IsNullOrEmpty(raw)) args = JToken.Parse(raw) as JObject;
            }
            if (id == ActionIds.UiLanguage)
            {
                string lang = (string)args?["language"];
                if (string.IsNullOrEmpty(lang)) throw CommandKit.BadArgs("args", "{language}", "ui.language needs args.language (en, ja, zh-Hans)");
                var b = s.Root.Q<Button>("setup.language." + lang);
                if (b == null || s.Guide?.Page != "welcome") throw new BridgeException(new BridgeError(CommandKit.Unavailable, "the language is chosen on the setup guide's first page"));
                Click(b);
                return new BridgeReply().With("ran", true).With("result", L10n.Language);
            }
            var el = Find(s.Root, id, false).FirstOrDefault();
            if (el == null)
            {
                var names = new List<string>();
                s.Root.Query<VisualElement>().ForEach(e => { if (e.name != null && e.name.StartsWith("setup.", StringComparison.Ordinal) && (e is Button || e is TextField || e is Toggle) && IsVisible(e)) names.Add(e.name); });
                throw CommandKit.BadArgs("action", "an element name of the setup guide", "no element \"" + id + "\" on page " + s.Page + " (" + string.Join(", ", names.Take(30)) + ")");
            }
            if (!el.enabledInHierarchy || !IsVisible(el)) throw new BridgeException(new BridgeError(CommandKit.Unavailable, id + " is " + (!el.enabledInHierarchy ? "disabled" : "not shown") + " on page " + s.Page));
            switch (el)
            {
                case Button b:
                    Click(b);
                    return new BridgeReply().With("ran", true).With("result", s.Page);
                case TextField tf:
                {
                    var v = args?["value"];
                    if (v == null || v.Type != JTokenType.String) throw CommandKit.BadArgs("args", "{value: string}", "a text field takes args.value");
                    tf.value = (string)v;
                    return new BridgeReply().With("ran", true).With("result", tf.value);
                }
                case Toggle tg:
                {
                    var v = args?["value"];
                    if (v == null || v.Type != JTokenType.Boolean) throw CommandKit.BadArgs("args", "{value: bool}", "a toggle takes args.value");
                    tg.value = (bool)v;
                    return new BridgeReply().With("ran", true).With("result", tg.value);
                }
            }
            throw CommandKit.Unsupported(id + " is a " + el.GetType().Name + " (buttons, text fields and toggles take ui.action)");
        }

        /// <summary>Clicks a button as a pointer click does (its clickable's handlers run).</summary>
        static void Click(Button b)
        {
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }
    }
}
