// Joint-correction table ("cf_expression", Studio 関節補正): parser and data model.
//
// The TextAsset format, the fixed category list and the Studio toggle
// grouping, and the vanilla table, embedded here as VanillaText: the test fixture and the fallback
// when the bundle is missing. Row order is kept: the stage evaluates rows strictly in table order.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HS2Next.Motion.Corrective
{
    /// <summary>Axis values of the table (enum names as written in the file).</summary>
    public enum JcAxis { X = 0, Y = 1, Z = 2, RevX = 3, RevY = 4, RevZ = 5, None = 6 }

    /// <summary>Euler rotation orders (integer values as ConvertRotation.RotationOrder).</summary>
    public enum JcOrder { XYZ = 0, XZY = 1, YXZ = 2, YZX = 3, ZXY = 4, ZYX = 5 }

    /// <summary>Calc type of a Correct part.</summary>
    public enum JcCalc { Euler = 0, Quaternion = 1 }

    /// <summary>One row of the table. Bone names are null when the file says "0" (empty).</summary>
    public sealed class JointCorrectionRow
    {
        public int Index;
        /// <summary>Column 0 of the file (older 4-category layout; ignored by the runtime).</summary>
        public int FileCategory;
        /// <summary>Category from the fixed list (0..7; overwrites the file's column).</summary>
        public int Category;
        /// <summary>Display name: the LookAt bone, overwritten by the Correct bone when both are present.</summary>
        public string Name;

        // ---- LookAt part
        public bool HasLookAt;
        public string LookBone, TargetBone, UpBone;
        public JcAxis TargetAxis = JcAxis.None, UpAxis = JcAxis.None, SourceAxis = JcAxis.None, LimitAxis = JcAxis.None;
        public JcOrder LookOrder;
        public float LimitMin, LimitMax;

        // ---- Correct part
        public bool HasCorrect;
        public string CorrectBone, ReferenceBone;
        public JcCalc Calc;
        public JcOrder CorrectOrder;
        /// <summary>Charm rate k (0 = no sign test).</summary>
        public float CharmRate;
        /// <summary>Per axis X, Y, Z.</summary>
        public readonly bool[] Use = new bool[3];
        public readonly float[] Min = new float[3], Max = new float[3];

        public bool UsesAnyAxis => Use[0] || Use[1] || Use[2];

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"row {Index} cat {Category} ");
            if (HasLookAt) sb.Append($"lookAt {LookBone} -> {TargetBone} {TargetAxis} up {UpBone ?? "-"}.{UpAxis} src {SourceAxis} limit {LimitAxis} {LookOrder} {LimitMin}..{LimitMax} ");
            if (HasCorrect)
            {
                sb.Append($"correct {CorrectBone} from {ReferenceBone ?? "-"} {Calc} {CorrectOrder} k {CharmRate}");
                for (int a = 0; a < 3; a++) sb.Append(Use[a] ? $" {"XYZ"[a]} {Min[a]}..{Max[a]}" : $" {"XYZ"[a]} -");
            }
            return sb.ToString();
        }
    }

    /// <summary>The parsed table: rows in file order plus the category grouping.</summary>
    public sealed class JointCorrectionTable
    {
        /// <summary>Number of Studio categories / toggles.</summary>
        public const int CategoryCount = 8;
        /// <summary>Categories saved by pose files (PauseCtrl.FileInfo): the first four.</summary>
        public const int PoseCategoryCount = 4;

        /// <summary>The fixed row -> category list the game assigns after parsing. Rows beyond it keep the file's column 0.</summary>
        public static readonly int[] CategoryOfRow = { 0, 0, 4, 0, 0, 0, 0, 1, 1, 5, 1, 1, 1, 1, 6, 6, 6, 2, 2, 6, 7, 7, 7, 3, 3, 7 };

        /// <summary>Descriptive category names (the in-game captions are unverified).</summary>
        public static readonly string[] CategoryNames =
        {
            "left upper arm / elbow / wrist", "right upper arm / elbow / wrist", "left knee", "right knee",
            "left forearm twist", "right forearm twist", "left thigh / buttock", "right thigh / buttock",
        };

        /// <summary>Bundle and asset of the table. Both sexes use the female asset in Studio.</summary>
        public const string Bundle = "list/expression.unity3d", FemaleAsset = "cf_expression", MaleAsset = "cm_expression";

        public readonly List<JointCorrectionRow> Rows = new List<JointCorrectionRow>();
        /// <summary>Parse notes (unknown enum names, short rows); empty for the vanilla data.</summary>
        public readonly List<string> Warnings = new List<string>();
        /// <summary>Where the table came from ("cf_expression", ...).</summary>
        public string Source;

        public int LookAtRows { get { int n = 0; foreach (var r in Rows) if (r.HasLookAt) n++; return n; } }
        public int EulerRows { get { int n = 0; foreach (var r in Rows) if (r.HasCorrect && r.Calc == JcCalc.Euler && r.UsesAnyAxis) n++; return n; } }

        /// <summary>Rows of one category, in table order.</summary>
        public List<int> RowsOf(int category)
        {
            var l = new List<int>();
            foreach (var r in Rows) if (r.Category == category) l.Add(r.Index);
            return l;
        }

        const string On = "○";   // ○

        /// <summary>Parses the text. Returns null (and error) when the row count exceeds the lines, as the game fails to load.</summary>
        public static JointCorrectionTable Parse(string text, out string error, string source = null)
        {
            error = null;
            if (text == null) { error = "no text"; return null; }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            var lines = text.Replace("\r", "").Split('\n');
            var head = lines[0].Split('\t');
            if (!int.TryParse(head[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
            {
                error = $"bad row count '{head[0]}'";
                return null;
            }
            if (n > lines.Length - 1) { error = $"row count {n} exceeds {lines.Length - 1} lines"; return null; }

            var t = new JointCorrectionTable { Source = source };
            for (int i = 0; i < n; i++)
            {
                var f = lines[i + 1].Split('\t');
                int c = 0;
                string F() { string s = c < f.Length ? f[c].Trim() : ""; c++; return s; }
                var row = new JointCorrectionRow { Index = i };
                row.FileCategory = Int(F(), t, i, "category");
                row.Category = i < CategoryOfRow.Length ? CategoryOfRow[i] : row.FileCategory;
                row.HasLookAt = F() == On;
                if (row.HasLookAt)
                {
                    row.LookBone = Name(F());
                    row.TargetBone = Name(F());
                    row.TargetAxis = Axis(F(), t, i);
                    row.UpBone = Name(F());
                    row.UpAxis = Axis(F(), t, i);
                    row.SourceAxis = Axis(F(), t, i);
                    row.LimitAxis = Axis(F(), t, i);
                    row.LookOrder = Order(F(), t, i);
                    row.LimitMin = Num(F(), t, i);
                    row.LimitMax = Num(F(), t, i);
                }
                else c += 10;   // exactly 10 columns skipped regardless of content
                row.HasCorrect = F() == On;
                if (row.HasCorrect)
                {
                    row.CorrectBone = Name(F());
                    row.ReferenceBone = Name(F());
                    string calc = F();
                    if (calc == "Euler") row.Calc = JcCalc.Euler;
                    else if (calc == "Quaternion") row.Calc = JcCalc.Quaternion;
                    else t.Warnings.Add($"row {i}: unknown calc type '{calc}' (Euler used)");
                    row.CorrectOrder = Order(F(), t, i);
                    row.CharmRate = Num(F(), t, i);
                    for (int a = 0; a < 3; a++)
                    {
                        row.Use[a] = F() == On;
                        row.Min[a] = Num(F(), t, i);
                        row.Max[a] = Num(F(), t, i);
                    }
                }
                if (c > f.Length && (row.HasLookAt || row.HasCorrect)) t.Warnings.Add($"row {i}: {f.Length} columns, {c} read (missing ones read as empty)");
                row.Name = row.HasCorrect && row.CorrectBone != null ? row.CorrectBone : row.LookBone;
                t.Rows.Add(row);
            }
            return t;
        }

        /// <summary>The vanilla table parsed from <see cref="VanillaText"/>.</summary>
        public static JointCorrectionTable Vanilla()
        {
            var t = Parse(VanillaText, out _, "fallback (the embedded vanilla table)");
            return t;
        }

        static string Name(string s) => string.IsNullOrEmpty(s) || s == "0" ? null : s;

        static int Int(string s, JointCorrectionTable t, int row, string what)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return v;
            t.Warnings.Add($"row {row}: bad {what} '{s}'");
            return 0;
        }

        static float Num(string s, JointCorrectionTable t, int row)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
            t.Warnings.Add($"row {row}: bad number '{s}'");
            return 0f;
        }

        static JcAxis Axis(string s, JointCorrectionTable t, int row)
        {
            switch (s)
            {
                case "X": return JcAxis.X;
                case "Y": return JcAxis.Y;
                case "Z": return JcAxis.Z;
                case "RevX": return JcAxis.RevX;
                case "RevY": return JcAxis.RevY;
                case "RevZ": return JcAxis.RevZ;
                case "None": return JcAxis.None;
            }
            t.Warnings.Add($"row {row}: unknown axis '{s}' (None used)");
            return JcAxis.None;
        }

        static JcOrder Order(string s, JointCorrectionTable t, int row)
        {
            switch (s)
            {
                case "XYZ": return JcOrder.XYZ;
                case "XZY": return JcOrder.XZY;
                case "YXZ": return JcOrder.YXZ;
                case "YZX": return JcOrder.YZX;
                case "ZXY": return JcOrder.ZXY;
                case "ZYX": return JcOrder.ZYX;
            }
            t.Warnings.Add($"row {row}: unknown rotation order '{s}' (XYZ used)");
            return JcOrder.XYZ;
        }

        // ---------------------------------------------------------------- vanilla data

        // LookAt row: look, target, target axis, up, up axis, source axis, limit axis, order, min, max.
        static string L(int cat, string look, string target, string tAxis, string up, string upAxis, string src, string lim, string order, float lo, float hi) =>
            string.Join("\t", cat.ToString(CultureInfo.InvariantCulture), On, look, target, tAxis, up, upAxis, src, lim, order,
                lo.ToString(CultureInfo.InvariantCulture), hi.ToString(CultureInfo.InvariantCulture), "×");

        // Correct row (Euler): bone, reference, order, charm rate, fractions per axis (NaN = axis unused; min = max).
        static string C(int cat, string bone, string reference, string order, float k, float x, float y, float z)
        {
            string A(float v) => float.IsNaN(v) ? "×\t0\t0" : $"{On}\t{v.ToString(CultureInfo.InvariantCulture)}\t{v.ToString(CultureInfo.InvariantCulture)}";
            return string.Join("\t", cat.ToString(CultureInfo.InvariantCulture), "×", "0", "0", "0", "0", "0", "0", "0", "0", "0", "0",
                On, bone, reference, "Euler", order, k.ToString(CultureInfo.InvariantCulture), A(x), A(y), A(z));
        }

        const float N = float.NaN;

        /// <summary>The table in the file format (column 0 = the file's older 4-category layout).</summary>
        public static readonly string VanillaText = string.Join("\n", new[]
        {
            "26",
            C(0, "cf_J_ArmUp01_dam_L", "cf_J_ArmUp00_L", "YZX", 0.5f, -0.66f, N, N),
            C(0, "cf_J_ArmUp02_dam_L", "cf_J_ArmUp00_L", "YZX", 0.5f, -0.33f, N, N),
            C(0, "cf_J_ArmLow02_dam_L", "cf_J_Hand_L", "ZYX", 0f, 0.5f, N, N),
            C(0, "cf_J_ArmElbo_dam_01_L", "cf_J_ArmLow01_L", "XZY", 0f, N, 0.6f, N),
            L(0, "cf_J_ArmElboura_dam_L", "cf_J_ArmLow02_dam_L", "RevX", "cf_J_ArmElbo_dam_01_L", "Y", "Y", "None", "ZYX", 0, 0),
            L(0, "cf_J_Hand_Wrist_dam_L", "cf_J_ArmLow02_dam_L", "X", "cf_J_Hand_L", "Z", "Z", "None", "ZYX", 0, 0),
            L(0, "cf_J_Hand_dam_L", "cf_J_Hand_Middle01_L", "RevX", "cf_J_Hand_Wrist_dam_L", "Y", "Y", "Z", "ZYX", -180, 0),
            C(1, "cf_J_ArmUp01_dam_R", "cf_J_ArmUp00_R", "YZX", 0.5f, -0.66f, N, N),
            C(1, "cf_J_ArmUp02_dam_R", "cf_J_ArmUp00_R", "YZX", 0.5f, -0.33f, N, N),
            C(1, "cf_J_ArmLow02_dam_R", "cf_J_Hand_R", "ZYX", 0f, 0.5f, N, N),
            C(1, "cf_J_ArmElbo_dam_01_R", "cf_J_ArmLow01_R", "XZY", 0f, N, 0.6f, N),
            L(1, "cf_J_ArmElboura_dam_R", "cf_J_ArmLow02_dam_R", "X", "cf_J_ArmElbo_dam_01_R", "Z", "Z", "None", "ZYX", 0, 0),
            L(1, "cf_J_Hand_Wrist_dam_R", "cf_J_ArmLow02_dam_R", "RevX", "cf_J_Hand_R", "Z", "Z", "None", "ZYX", 0, 0),
            L(1, "cf_J_Hand_dam_R", "cf_J_Hand_Middle01_R", "X", "cf_J_Hand_Wrist_dam_R", "Z", "Z", "Z", "ZYX", 0, 180),
            L(2, "cf_J_LegUpDam_L", "cf_J_LegLow01_L", "RevY", "cf_J_Kosi02", "X", "X", "None", "ZYX", 0, 0),
            C(2, "cf_J_LegUp01_L", "cf_J_LegUp00_L", "YZX", 0f, N, -0.85f, N),
            C(2, "cf_J_LegUp02_L", "cf_J_LegUp00_L", "YZX", 0f, N, -0.5f, N),
            C(2, "cf_J_LegKnee_dam_L", "cf_J_LegLow01_L", "ZYX", 0f, 0.5f, N, N),
            C(2, "cf_J_LegKnee_back_L", "cf_J_LegLow01_L", "ZYX", 0f, 1f, 1f, 1f),
            C(2, "cf_J_SiriDam_L", "cf_J_LegUp00_L", "YZX", 0f, 0.5f, 0.2f, 0.25f),
            L(3, "cf_J_LegUpDam_R", "cf_J_LegLow01_R", "RevY", "cf_J_Kosi02", "X", "X", "None", "ZYX", 0, 0),
            C(3, "cf_J_LegUp01_R", "cf_J_LegUp00_R", "YZX", 0f, N, -0.85f, N),
            C(3, "cf_J_LegUp02_R", "cf_J_LegUp00_R", "YZX", 0f, N, -0.5f, N),
            C(3, "cf_J_LegKnee_dam_R", "cf_J_LegLow01_R", "ZYX", 0f, 0.5f, N, N),
            C(3, "cf_J_LegKnee_back_R", "cf_J_LegLow01_R", "ZYX", 0f, 1f, 1f, 1f),
            C(3, "cf_J_SiriDam_R", "cf_J_LegUp00_R", "YZX", 0f, 0.5f, 0.2f, 0.25f),
        }) + "\n";

        /// <summary>Field-by-field comparison of the semantic content (category list, parts, names, enums, numbers); null when equal.</summary>
        public static string Diff(JointCorrectionTable a, JointCorrectionTable b, float eps = 1e-6f)
        {
            if (a == null || b == null) return "null table";
            if (a.Rows.Count != b.Rows.Count) return $"row count {a.Rows.Count} vs {b.Rows.Count}";
            var sb = new StringBuilder();
            for (int i = 0; i < a.Rows.Count; i++)
            {
                var x = a.Rows[i]; var y = b.Rows[i];
                void D(string what, object p, object q) { if (!Equals(p, q)) sb.Append($"row {i} {what}: {p} vs {q}; "); }
                void F(string what, float p, float q) { if (Math.Abs(p - q) > eps) sb.Append($"row {i} {what}: {p} vs {q}; "); }
                D("category", x.Category, y.Category);
                D("lookAt", x.HasLookAt, y.HasLookAt);
                D("correct", x.HasCorrect, y.HasCorrect);
                if (x.HasLookAt && y.HasLookAt)
                {
                    D("look", x.LookBone, y.LookBone); D("target", x.TargetBone, y.TargetBone); D("targetAxis", x.TargetAxis, y.TargetAxis);
                    D("up", x.UpBone, y.UpBone); D("upAxis", x.UpAxis, y.UpAxis); D("sourceAxis", x.SourceAxis, y.SourceAxis);
                    D("limitAxis", x.LimitAxis, y.LimitAxis);
                    if (x.LimitAxis != JcAxis.None) { D("order", x.LookOrder, y.LookOrder); F("min", x.LimitMin, y.LimitMin); F("max", x.LimitMax, y.LimitMax); }
                }
                if (x.HasCorrect && y.HasCorrect)
                {
                    D("bone", x.CorrectBone, y.CorrectBone); D("reference", x.ReferenceBone, y.ReferenceBone); D("calc", x.Calc, y.Calc);
                    D("order", x.CorrectOrder, y.CorrectOrder); F("charm", x.CharmRate, y.CharmRate);
                    for (int k = 0; k < 3; k++)
                    {
                        D("use" + "XYZ"[k], x.Use[k], y.Use[k]);
                        if (x.Use[k] && y.Use[k]) { F("min" + "XYZ"[k], x.Min[k], y.Min[k]); F("max" + "XYZ"[k], x.Max[k], y.Max[k]); }
                    }
                }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
