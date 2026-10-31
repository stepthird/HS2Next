// Studio-facing facade of the joint correction (関節補正) of one character.
//
// eight category toggles, all off for a new character; scenes store the
// 8 flags per character, pose files store and restore only the first 4 (categories 4..7 are left
// untouched by loading a pose; a fresh pose object defaults to all true but is filled from the
// character before saving). Refresh() runs one evaluation after a pose edit outside the step loop.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.Corrective
{
    public sealed class CharaJointCorrection
    {
        public readonly JointCorrectionStage Stage;

        public CharaJointCorrection(JointCorrectionStage stage) { Stage = stage; }

        /// <summary>The joint correction of a character (null when the stage is not attached).</summary>
        public static CharaJointCorrection Of(CharaMotion motion)
        {
            var s = motion != null ? motion.Get<JointCorrectionStage>() : null;
            return s != null ? new CharaJointCorrection(s) : null;
        }

        /// <summary>The joint correction of a character root (chaF_/chaM_), or null.</summary>
        public static CharaJointCorrection Of(GameObject root) => root ? Of(root.GetComponent<CharaMotion>()) : null;

        public int CategoryCount => JointCorrectionTable.CategoryCount;

        /// <summary>Category toggle (Studio MPCharCtrl.JointInfo / OCIChar.EnableExpressionCategory).</summary>
        public bool this[int category]
        {
            get => Stage.IsCategoryEnabled(category);
            set => Stage.EnableCategory(category, value);
        }

        /// <summary>Master switch (ChaInfo.enableExpression).</summary>
        public bool Master { get => Stage.Enabled; set => Stage.Enabled = value; }

        /// <summary>The 8 flags as a scene stores them (OICharInfo.expression).</summary>
        public bool[] SceneFlags()
        {
            var a = new bool[JointCorrectionTable.CategoryCount];
            for (int c = 0; c < a.Length; c++) a[c] = Stage.IsCategoryEnabled(c);
            return a;
        }

        /// <summary>Applies scene flags (index c = category c; missing entries are off, as a new character).</summary>
        public void ApplySceneFlags(IReadOnlyList<bool> flags)
        {
            for (int c = 0; c < JointCorrectionTable.CategoryCount; c++)
                Stage.EnableCategory(c, flags != null && c < flags.Count && flags[c]);
        }

        /// <summary>The first 4 flags as a pose file stores them (PauseCtrl.FileInfo).</summary>
        public bool[] PoseFlags()
        {
            var a = new bool[JointCorrectionTable.PoseCategoryCount];
            for (int c = 0; c < a.Length; c++) a[c] = Stage.IsCategoryEnabled(c);
            return a;
        }

        /// <summary>Applies pose-file flags: categories 0..3 only; 4..7 stay as they are.</summary>
        public void ApplyPoseFlags(IReadOnlyList<bool> flags)
        {
            if (flags == null) return;
            for (int c = 0; c < JointCorrectionTable.PoseCategoryCount && c < flags.Count; c++) Stage.EnableCategory(c, flags[c]);
        }

        /// <summary>Pose flags of a fresh pose object before it is filled from a character (all true).</summary>
        public static bool[] NewPoseDefault() => new[] { true, true, true, true };

        /// <summary>One evaluation now (after an FK / pose edit while the step loop is not running).</summary>
        public void Refresh() => Stage.Refresh();
    }
}
