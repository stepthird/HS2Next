// A character's runtime overrides (Phase 6.2): the values the face, the animation speed and time and
// the animation row show when the scene's override layer has a winning slot for them, and the applications the layer asks
// for when a slot's value changes or goes. The model's apply paths (ApplyFace, ApplyChanges) read the shown values through
// FaceShown / SpeedShown / TimeShown, so a model edit during a slot's life never shows the model value for a frame; without a
// slot each of them returns the model value unchanged.
//   speed   CharaAnimation.Speed = the slot's value at once; released: the model's speed
//   time    CharaAnimation.NormalizedTime = the slot's value at once (it re-samples, also at speed 0) and the animation is
//           pinned to it (every step samples exactly that time); released: unpinned, the animation goes on from there
//   row     CharaAnimation.Load(row, 0 or the pinned time) at the end of the next Apply when it differs from the row playing;
//           released: the model's row at the model's stored time (Stop when the model has none)
//   face    the face status with the overridden eyes / mouth patterns and openness (ApplyFace with the Studio mouth-open rule)
using HS2Next.Motion.Animation;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class CharaRealization
    {
        bool rowShown;

        OverrideSlot Winning(OverrideChannel ch) => Runtime.Overrides.WinnerOf(NodeId, ch);

        /// <summary>The integer face member shown: the winning slot's, else the model's.</summary>
        int FaceShown(OverrideChannel ch, int model)
        {
            var w = Winning(ch);
            return w != null ? (int)w[0] : model;
        }

        /// <summary>The face openness shown: the winning slot's, else the model's.</summary>
        float FaceShown(OverrideChannel ch, float model)
        {
            var w = Winning(ch);
            return w != null ? (float)w[0] : model;
        }

        /// <summary>The animation speed shown.</summary>
        float SpeedShown(float model)
        {
            var w = Winning(OverrideChannel.AnimeSpeed);
            return w != null ? (float)w[0] : model;
        }

        /// <summary>The animation time shown (the pinned time while a time slot wins).</summary>
        float TimeShown(float model)
        {
            var w = Winning(OverrideChannel.AnimeTime);
            return w != null ? (float)w[0] : model;
        }

        /// <summary>True while a slot's animation row is shown instead of the model's.</summary>
        internal bool AnimeRowOverridden => rowShown || Winning(OverrideChannel.AnimeRef) != null;

        bool Ready => State == RealizationState.Built && Animation;

        internal void ApplyFaceOverride()
        {
            if (State != RealizationState.Built || Expression == null) return;
            var st = St;
            if (st != null) Step("face", () => ApplyFace(st.Face));
        }

        internal void ApplySpeedOverride()
        {
            if (!Ready) return;
            var st = St;
            Animation.Speed = SpeedShown(st != null ? st.Anime.Speed : Animation.Speed);
        }

        internal void ApplyTimeOverride()
        {
            if (!Ready) return;
            var w = Winning(OverrideChannel.AnimeTime);
            if (w == null)
            {
                Animation.UnpinTime();
                return;
            }
            float t = (float)w[0];
            Animation.PinTime(t);
            Step("anime.time", () => Animation.NormalizedTime = t);
            WantSettle();
        }

        /// <summary>The row of a slot (its source, else the last-wins row of the global index, as chara.anime resolves it).</summary>
        HS2Next.Content.Studio.StudioAnime RowOf(OverrideSlot w)
        {
            var db = Services.Db;
            if (db == null) return null;
            int g = (int)w[0], c = (int)w[1], no = (int)w[2];
            if (w.AnimeSource != null) return db.TryGetAnime(w.AnimeSource, g, c, no, out var a) ? a : null;
            return db.FindAnime(g, c, no);
        }

        /// <summary>The end of an Apply: the winning row loads when it differs from the row playing; released, the model's row.</summary>
        internal void ApplyAnimeRefOverride()
        {
            if (!Ready) return;
            var w = Winning(OverrideChannel.AnimeRef);
            if (w != null)
            {
                var row = RowOf(w);
                if (row == null) return;   // not effective: the row playing stays
                if (!Animation.IsLoaded || Animation.Current != row)
                {
                    var tw = Winning(OverrideChannel.AnimeTime);
                    Step("anime", () => Animation.Load(row, tw != null ? (float)tw[0] : 0f));
                    WantSettle();
                }
                rowShown = true;
                return;
            }
            if (!rowShown) return;
            rowShown = false;
            var st = St;
            if (st == null) return;
            Step("anime", () =>
            {
                if (st.Anime.Ref == null) Animation.Stop();
                else Animation.Load(CharaAnimation.Resolve(Services.Db, st.Anime.Ref.Source, st.Anime.Ref.Group, st.Anime.Ref.Category, st.Anime.Ref.No), TimeShown(st.Anime.Time));
            });
            WantSettle();
        }

        /// <summary>True when the winning row slot's row resolves and plays (overrides.list `effective`).</summary>
        internal bool AnimeRowEffective(OverrideSlot w)
        {
            if (!Ready) return false;
            var row = RowOf(w);
            return row != null && Animation.Current == row;
        }

        /// <summary>A build: the model's state was applied (ApplyAll); a new animation component starts without a pin or a row.</summary>
        internal void OverridesRebuilt() => rowShown = false;
    }
}
