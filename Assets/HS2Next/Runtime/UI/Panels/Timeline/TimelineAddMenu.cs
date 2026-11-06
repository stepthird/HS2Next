// The add-track menu of the Timeline panel: the track types valid for the selection. A node with a holder: Position,
// Rotation; a character or an item: + Scale; a character: + Animation, Animation speed, Animation time, Eyes, Mouth, Eyes
// open, Mouth open; an FK bone handle of a character (the sub-selection of the FK tab): Bone rotation of that bone; an item:
// + Colour 1–4 (the colour slots it uses, the others disabled); a light: + Colour; always: Camera, Depth of field. One
// track per channel: the entry of a channel that has a track is disabled with a tooltip.
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using HS2Next.UI.Core;

namespace HS2Next.UI.Panels
{
    public static class TimelineAddMenu
    {
        public sealed class Entry
        {
            /// <summary>The menu id suffix ("pos", "bone", "color2", "lightColor", "camera", ...).</summary>
            public string Id;
            /// <summary>Entries of one group stand between separators ("node", "chara", "item", "light", "bone", "scene").</summary>
            public string Group;
            public string Text;
            public string Type, Key, Bone;
            public int? Slot;
            public bool Disabled;
            public string Tooltip;
            public override string ToString() => Id + (Disabled ? " (disabled)" : "");
        }

        /// <summary>The entries for the current selection, in menu order.</summary>
        public static List<Entry> Entries(UiContext ctx, TimelineDocument doc)
        {
            var l = new List<Entry>();
            var model = ctx.Model;
            string id = ctx.Selection.Primary;
            var n = id != null ? model?.Find(id) : null;
            string sub = ctx.SubSelection;
            if (sub != null && StudioOps.TryParseSub(sub, out var subId, out var subKind, out var bone) && subKind == "fk" && !string.IsNullOrEmpty(bone)
                && model?.Find(subId)?.Kind == NodeKind.Chara)
                Add(l, doc, "bone", "bone", L10n.Get("timeline.type.bone") + " · " + bone, TimelineTypes.Rot, subId, bone, null);
            if (n != null)
            {
                var k = n.Kind;
                if (TimelineTrackTypes.Takes(TimelineTrackType.Pos, k))
                {
                    Add(l, doc, "pos", "node", L10n.Get("timeline.type.pos"), TimelineTypes.Pos, id, null, null);
                    Add(l, doc, "rot", "node", L10n.Get("timeline.type.rot"), TimelineTypes.Rot, id, null, null);
                }
                if (TimelineTrackTypes.Takes(TimelineTrackType.Scale, k)) Add(l, doc, "scale", "node", L10n.Get("timeline.type.scale"), TimelineTypes.Scale, id, null, null);
                if (k == NodeKind.Chara)
                    foreach (var t in new[] { TimelineTypes.Anime, TimelineTypes.AnimeSpeed, TimelineTypes.AnimeTime, TimelineTypes.Eyes, TimelineTypes.Mouth, TimelineTypes.EyesOpen, TimelineTypes.MouthOpen })
                        Add(l, doc, t, "chara", L10n.Get("timeline.type." + t), t, id, null, null);
                if (k == NodeKind.Item)
                {
                    var f = InspectorGates.Features(ctx.Runtime, id);
                    for (int s = 0; s <= TimelineTrackTypes.MaxColorSlot; s++)
                    {
                        var e = Add(l, doc, "color" + s, "item", L10n.Format("timeline.type.color", s + 1), TimelineTypes.Color, id, null, s);
                        bool uses = f == null || (s < 3 ? f.Color[s] : f.Glass);
                        if (!uses && !e.Disabled) { e.Disabled = true; e.Tooltip = L10n.Get("timeline.slotUnused"); }
                    }
                }
                if (k == NodeKind.Light) Add(l, doc, "lightColor", "light", L10n.Get("timeline.type.lightColor"), TimelineTypes.Color, id, null, null);
            }
            Add(l, doc, "camera", "scene", L10n.Get("timeline.type.camera"), TimelineTypes.Camera, null, null, null);
            Add(l, doc, "dof", "scene", L10n.Get("timeline.type.dof"), TimelineTypes.Dof, null, null, null);
            return l;
        }

        static Entry Add(List<Entry> l, TimelineDocument doc, string id, string group, string text, string type, string key, string bone, int? slot)
        {
            var probe = new TimelineTrack("00000000000000000000000000000000", type, key) { Bone = bone, Slot = slot };
            bool has = doc != null && doc.IndexOfChannel(probe.ChannelKey) >= 0;
            var e = new Entry { Id = id, Group = group, Text = text, Type = type, Key = key, Bone = bone, Slot = slot, Disabled = has, Tooltip = has ? L10n.Get("timeline.channelHasTrack") : null };
            l.Add(e);
            return e;
        }
    }
}
