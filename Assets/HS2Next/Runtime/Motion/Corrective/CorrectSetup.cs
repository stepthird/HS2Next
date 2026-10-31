// Attaches the joint correction (twist / *_dam bones) of a freshly built character:
// lifetime, asset and binding; all categories start off and InitialCategoriesOn
// switches them all on, as the HS2US plugin does.
//
// Studio creates the correction for females AND males with the female table cf_expression
// (InitializeExpression(1)); the table is read from list/expression.unity3d through the catalog,
// with the table (JointCorrectionTable.VanillaText) as fallback. Bones resolve inside p_cf_anim
// (ctx.BodyBones, first occurrence of a name). The Illusion.Component.Correct family and the
// IK-before processes are not implemented (Studio disables them; see README.md).
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Motion.Corrective
{
    public static class CorrectSetup
    {
        /// <summary>Master switch for the build (false: no joint-correction stage is attached).</summary>
        public static bool Enabled = true;
        /// <summary>
        /// Initial category state of a new character. false (default): vanilla Studio, all 8 categories off.
        /// true: all 8 on, which is what the HS2US plugin's "Auto Joint Correction" (default "All") does when Studio
        /// adds a character; the reference install has it, so the in-game comparison data (Editor/CompareTest.cs) was
        /// recorded with every category on.
        /// </summary>
        public static bool InitialCategoriesOn;

        public static void Attach(CharaContext ctx)
        {
            if (!Enabled || ctx?.Motion == null || ctx.BodyBones == null) return;
            var table = LoadTable(ctx, out string note);
            if (table == null) { ctx.Log?.Add("correct: no joint-correction table" + note); return; }
            var bones = ctx.BodyBones;
            var stage = new JointCorrectionStage(table, n => bones.TryGetValue(n, out var t) ? t : null);
            ctx.Motion.Add(stage);
            if (InitialCategoriesOn) stage.EnableAll(true);
            ctx.Log?.Add($"correct: joint correction {table.Source}{note}: rows {table.Rows.Count} ({table.EulerRows} euler, {table.LookAtRows} lookAt), " +
                         $"bound rows {stage.BoundRows}, driven bones {stage.DrivenCount}, source bones {stage.SourceCount}, " +
                         $"unbound {(stage.Unbound.Count == 0 ? "none" : string.Join(", ", stage.Unbound))}; categories {(InitialCategoriesOn ? "all on" : "off")}");
        }

        /// <summary>The table from the bundle (female asset for both sexes), else the fallback.</summary>
        public static JointCorrectionTable LoadTable(CharaContext ctx, out string note)
        {
            note = "";
            TextAsset ta = null;
            try { ta = ctx.Catalog?.LoadAsset<TextAsset>(JointCorrectionTable.Bundle, JointCorrectionTable.FemaleAsset); }
            catch (System.Exception e) { note = $" (bundle load failed: {e.GetType().Name})"; }
            if (ta != null)
            {
                var t = JointCorrectionTable.Parse(ta.text, out string err, JointCorrectionTable.FemaleAsset);
                if (t != null)
                {
                    if (t.Warnings.Count > 0) note += $" ({t.Warnings.Count} parse warnings: {t.Warnings[0]})";
                    return t;
                }
                note += $" ({JointCorrectionTable.FemaleAsset}: {err})";
            }
            else if (note.Length == 0) note = $" ({JointCorrectionTable.Bundle}/{JointCorrectionTable.FemaleAsset} not found)";
            return JointCorrectionTable.Vanilla();
        }
    }
}
