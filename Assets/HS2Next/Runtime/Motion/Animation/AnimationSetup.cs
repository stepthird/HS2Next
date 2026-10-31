// Attaches the animation family to a freshly built character:
// the CharaAnimation facade on the root (body Animator of p_cf_anim), its AnimationStage at MotionPhase.Animation
// (sampling first in every step) and the HandShapeStage at MotionPhase.Shape (hand tables from the
// catalog, both hands at key 0 and disabled). Called by MotionSetup.Attach before the other families.
//
// Nothing is played at attach: the Animator stays disabled with no controller (CharaAssembler.BuildSkeleton) and the
// character shows the post-build rest pose until CharaAnimation.Load. Studio would load record 0/0/0 (the T-pose,
// step 4); LoadInitialPose = true does that when a Studio DB is supplied (StudioDbProvider).
using System;
using HS2Next.Chara;
using HS2Next.Content.Studio;
using UnityEngine;

namespace HS2Next.Motion.Animation
{
    public static class AnimationSetup
    {
        /// <summary>false: no animation component or stages are attached.</summary>
        public static bool Enabled = true;
        /// <summary>Options copied into every new character.</summary>
        public static AnimationOptions Defaults = new AnimationOptions();
        /// <summary>Studio "add character" default: load 0/0/0 (needs <see cref="StudioDbProvider"/>). Default false (rest pose).</summary>
        public static bool LoadInitialPose;
        /// <summary>Source of the Studio list DB for <see cref="LoadInitialPose"/> (null = catalog.StudioLists).</summary>
        public static Func<CharaContext, StudioDb> StudioDbProvider;

        static TextAsset cachedKeys;
        static HandShapeData cachedHands;

        public static void Attach(CharaContext ctx)
        {
            if (!Enabled || ctx?.Motion == null || !ctx.Root || !ctx.Anim) return;
            var animator = ctx.Anim.GetComponent<Animator>();
            if (!animator) { ctx.Log?.Add("anim: p_cf_anim has no Animator"); return; }
            var ca = ctx.Root.GetComponent<CharaAnimation>() ?? ctx.Root.AddComponent<CharaAnimation>();
            ca.Bind(ctx, animator, Defaults.Clone());
            ctx.Motion.Add(ca.Stage);

            string handNote;
            var data = LoadHands(ctx, out handNote);
            if (data != null && ctx.BodyBones != null)
            {
                var bones = ctx.BodyBones;
                var hands = new HandShapeStage(data, n => bones.TryGetValue(n, out var t) ? t : null);
                ctx.Motion.Add(hands);
                ca.Hands = hands;
                handNote = $"hand patterns {data.PatternCount}, bones L {hands.BoundCount(0)}/{data.Bones[0].Count} R {hands.BoundCount(1)}/{data.Bones[1].Count}" +
                           (data.Warnings.Count > 0 ? $" ({data.Warnings.Count} warnings: {data.Warnings[0]})" : "");
            }
            ctx.Log?.Add($"anim: engine {ca.Options.Engine}, shape bones guarded {ca.ShapeBoneCount}, body height {ca.BodyHeight:F3}; {handNote}");

            if (LoadInitialPose)
            {
                var db = StudioDbProvider != null ? StudioDbProvider(ctx) : ctx.Catalog?.StudioLists;
                var row = db?.FindAnime(0, 0, 0);
                if (row != null) ca.Load(row, 0f);
            }
        }

        static HandShapeData LoadHands(CharaContext ctx, out string note)
        {
            note = "";
            TextAsset k = null;
            try { k = ctx.Catalog?.LoadAsset<TextAsset>(HandShapeData.Bundle, HandShapeData.KeyAsset); } catch (Exception) { }
            if (k && k == cachedKeys && cachedHands != null) return cachedHands;
            var d = HandShapeData.Load(ctx, out note);
            if (d != null) { cachedKeys = k; cachedHands = d; }
            return d;
        }
    }
}
