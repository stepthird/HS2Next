// Texts of the Timeline panel: the play head as mm:ss.fff (and its parse), the key glyph of each ease, the label of a track
// ("<node name> · <type name>[ · <bone>]" unless the track has its own label), the l10n keys of the type names and eases.
using System;
using System.Globalization;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.UI.Core;

namespace HS2Next.UI.Panels
{
    public static class TimelineText
    {
        /// <summary>mm:ss.fff of seconds (negative and NaN as 0).</summary>
        public static string Time(double t)
        {
            if (double.IsNaN(t) || t < 0) t = 0;
            long ms = (long)Math.Floor(t * 1000.0 + 1e-6);
            long m = ms / 60000, s = ms / 1000 % 60, f = ms % 1000;
            return m.ToString("00", CultureInfo.InvariantCulture) + ":" + s.ToString("00", CultureInfo.InvariantCulture) + "." + f.ToString("000", CultureInfo.InvariantCulture);
        }

        /// <summary>Reads "mm:ss.fff", "ss.fff" or "h:mm:ss.fff" (also with a comma), or plain seconds; false when it is not a time.</summary>
        public static bool TryParseTime(string text, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Trim().Replace(',', '.').Split(':');
            if (parts.Length > 3) return false;
            double total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0 || double.IsInfinity(v)) return false;
                total = total * 60 + v;
            }
            seconds = total;
            return true;
        }

        /// <summary>The glyph a key is drawn with: ◆ inout, ■ step, ▲ linear, ◢ in, ◣ out (an unknown ease is drawn as inout).</summary>
        public static string Glyph(string ease)
        {
            switch (ease)
            {
                case TimelineEases.Step: return "■";
                case TimelineEases.Linear: return "▲";
                case TimelineEases.In: return "◢";
                case TimelineEases.Out: return "◣";
                default: return "◆";
            }
        }

        /// <summary>The l10n key of an ease's name.</summary>
        public static string EaseKey(string ease) => "timeline.ease." + (TimelineEases.IsKnown(ease) ? ease : TimelineEases.InOut);

        /// <summary>The l10n key of a track type's name (a bone rotation is "bone"; a light's colour "lightColor").</summary>
        public static string TypeKey(TimelineTrack t)
        {
            if (t == null) return "timeline.type.pos";
            if (t.Type == TimelineTypes.Rot && t.Bone != null) return "timeline.type.bone";
            if (t.Type == TimelineTypes.Color && !t.Slot.HasValue) return "timeline.type.lightColor";
            return "timeline.type." + t.Type;
        }

        /// <summary>The type's name ("Colour 2" for an item's slot 1; an unknown type shows its stored name).</summary>
        public static string TypeName(TimelineTrack t)
        {
            if (t == null) return "";
            if (!t.IsKnownType) return t.Type ?? "?";
            if (t.Type == TimelineTypes.Color && t.Slot.HasValue) return L10n.Format("timeline.type.color", t.Slot.Value + 1);
            return L10n.Get(TypeKey(t));
        }

        /// <summary>The row label: the track's own label, else "&lt;node name&gt; · &lt;type&gt;[ · &lt;bone&gt;]" (scene-wide tracks: the type).</summary>
        public static string Label(SceneModel model, TimelineTrack t)
        {
            if (t == null) return "";
            if (!string.IsNullOrEmpty(t.Label)) return t.Label;
            string type = TypeName(t);
            if (t.IsKnownType && TimelineTypes.IsSceneWide(t.Type)) return type;
            string node = t.Key == null ? "?" : model?.Find(t.Key)?.Label ?? t.Key.Substring(0, Math.Min(8, t.Key.Length));
            string s = node + " · " + type;
            if (t.Bone != null && t.Type == TimelineTypes.Rot) s += " · " + t.Bone;
            return s;
        }

        /// <summary>Seconds with up to three decimals ("2.5").</summary>
        public static string Seconds(double t) => t.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
