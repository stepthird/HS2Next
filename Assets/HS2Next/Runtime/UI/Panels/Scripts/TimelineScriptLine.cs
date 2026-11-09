// The script line of the Timeline panel's header: shown while the scene's Timeline was loaded from a scene script, it reads «Script: <name>», with the mark «edited» when the
// Timeline was edited after the load and the button «Save edits into the script», which writes the loaded Timeline back into its script in one history entry (what
// script.capture does). Hidden for a Timeline that came from no script. The line reads the scene only while it is shown, and computes «edited» again only when the Timeline
// or the scripts change. Element names: timeline-script, timeline-script-name, timeline-script-edited, timeline-script-capture.
using System;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Scripts;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class TimelineScriptLine : VisualElement
    {
        readonly UiContext ctx;
        readonly Label nameLabel, editedLabel;
        readonly Button captureButton;
        object lastDoc, lastScripts;
        int lastStamp = int.MinValue;
        bool dirty = true;

        /// <summary>The script the Timeline was loaded from, as the line shows it (null: hidden).</summary>
        public string ScriptId { get; private set; }
        /// <summary>The line shows «edited».</summary>
        public bool Edited { get; private set; }
        /// <summary>How many times the line computed «edited» (tests: not every frame).</summary>
        public int Computed { get; private set; }

        public TimelineScriptLine(UiContext ctx)
        {
            this.ctx = ctx;
            name = "timeline-script";
            AddToClassList("hs-tl__script");
            ScriptsUiModule.Style(this);
            nameLabel = new Label { name = "timeline-script-name" };
            nameLabel.AddToClassList("hs-tl__scriptname");
            editedLabel = Loc.Label("script.edited");
            editedLabel.name = "timeline-script-edited";
            editedLabel.AddToClassList("hs-tl__scriptedited");
            captureButton = Loc.Button("script.capture", Capture);
            captureButton.name = "timeline-script-capture";
            captureButton.AddToClassList("hs-tl__btn");
            Add(nameLabel);
            Add(editedLabel);
            Add(captureButton);
            style.display = DisplayStyle.None;
            RegisterCallback<AttachToPanelEvent>(_ => { ctx.SceneChanged -= OnChanged; ctx.SceneChanged += OnChanged; dirty = true; });
            RegisterCallback<DetachFromPanelEvent>(_ => ctx.SceneChanged -= OnChanged);
            PanelKit.EveryFrame(this, Tick, fromConstruction: false);
        }

        void OnChanged(SceneChangeBatch b)
        {
            if (b.IsStructural || b.Touches(null, ScriptEditCommand.ChangePath) || b.Touches(null, ScriptEditCommand.TimelinePath)) dirty = true;
        }

        /// <summary>A number that changes when the Timeline document or one of its tracks is replaced (the bus replaces objects).</summary>
        static int Stamp(TimelineDocument d)
        {
            if (d == null) return 0;
            unchecked
            {
                int h = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d);
                h = h * 31 + d.Tracks.Count;
                foreach (var t in d.Tracks) h = h * 31 + (t == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(t));
                h = h * 31 + d.Events.Count;
                foreach (var e in d.Events) h = h * 31 + (e == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e));
                return h;
            }
        }

        void Tick()
        {
            if (panel == null || float.IsNaN(layout.width)) return;
            for (VisualElement e = parent; e != null; e = e.hierarchy.parent)
                if (e.resolvedStyle.display == DisplayStyle.None) return;   // the Timeline panel not shown, or showing no Timeline
            var doc = ctx.Model?.Timeline;
            var scripts = ctx.Model?.Scripts;
            int stamp = Stamp(doc);
            if (!dirty && ReferenceEquals(doc, lastDoc) && ReferenceEquals(scripts, lastScripts) && stamp == lastStamp) return;
            dirty = false;
            lastDoc = doc;
            lastScripts = scripts;
            lastStamp = stamp;
            Refresh(doc, scripts);
        }

        /// <summary>Reads the Timeline's marker and the script's name now.</summary>
        public void Refresh() => Refresh(ctx.Model?.Timeline, ctx.Model?.Scripts);

        void Refresh(TimelineDocument doc, SceneScriptList scripts)
        {
            var marker = doc?.Script;
            ScriptId = marker?.Id;
            if (marker == null)
            {
                Edited = false;
                style.display = DisplayStyle.None;
                return;
            }
            Computed++;
            Edited = doc.IsEditedSinceLoad;
            var s = scripts?.Find(marker.Id);
            nameLabel.text = L10n.Format("timeline.fromScript", s?.Name ?? marker.Id);
            nameLabel.tooltip = s?.Name ?? marker.Id;
            editedLabel.style.display = Edited ? DisplayStyle.Flex : DisplayStyle.None;
            captureButton.style.display = Edited && s != null ? DisplayStyle.Flex : DisplayStyle.None;
            style.display = DisplayStyle.Flex;
        }

        /// <summary>«Save edits into the script»: the loaded Timeline written back into its script, one history entry «Save edits into script».</summary>
        public void Capture()
        {
            try
            {
                ScriptPlayer.Capture(ctx.App, null, true, CommandSource.User);
            }
            catch (BridgeException e) { ctx.Toasts?.Show(L10n.Format("msg.opFailed", e.Code ?? "error", e.Error?.Message ?? e.Message), ToastKind.Error); }
            catch (Exception e) { ctx.Toasts?.Show(L10n.Format("msg.opFailed", "error", e.Message), ToastKind.Error); }
            dirty = true;
            ScriptsPanel.Current?.MarkDirty();
        }
    }
}
