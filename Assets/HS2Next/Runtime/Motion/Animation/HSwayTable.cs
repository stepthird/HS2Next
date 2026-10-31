// The per-motion sway table of an H animation row: which physics chains and bust shapes stay active in each animator state
// of the motion.
//   Data: the row's sway bundle and file (an ExcelData list read through the catalog), filtered by the row's motion id (the
//   first cell of a line). In the tables whose file name starts with "ail" or "ai3p" the second cell is an owner index and
//   only the lines of the row's owner are kept. Then each line holds a state name, per side (left, right) nine flags (the
//   breast chain's sway, seven bust shape flags, the nipple flag) and the left and right hip flags. "1" = enabled.
//   Per step (after the body was sampled): the first line whose state is the body's layer-0 state applies; no line (or no
//   table) enables everything. Breast sway flags -> the breast chains' animation switches, hip flags -> the hip chains,
//   a cleared shape flag masks that body shape id on that side (the shape is evaluated at 0.5 there). The bust shape ids
//   of the seven shape flags and the nipple flag are ShapeEngine.BustMaskIds, in order.
// An H row without a table and Stop clear the table (everything enabled). A plain row keeps it loaded, as HS2 does: its
// states match no line, so everything is enabled while it plays.
// The step does not allocate; it resolves a line only when the state changed.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Content.Studio;
using UnityEngine;

namespace HS2Next.Motion.Animation
{
    public sealed class HSwayTable
    {
        /// <summary>Flags per side: the breast chain's sway, the seven bust shape flags, the nipple flag.</summary>
        public const int SideFlags = 9;
        /// <summary>Flag cells per line after the state name: two sides and the two hips.</summary>
        public const int FlagCells = 2 * SideFlags + 2;

        public sealed class Line
        {
            public string State;
            public int StateHash;
            /// <summary>The owner index of the line (-1 in tables without the owner column).</summary>
            public int Owner = -1;
            /// <summary>Per side: [0] sway, [1..7] bust shapes, [8] nipple.</summary>
            public readonly bool[] Left = new bool[SideFlags], Right = new bool[SideFlags];
            public bool HipL = true, HipR = true;
            /// <summary>Bit k set = shape flag k cleared (the k-th id of ShapeEngine.BustMaskIds masked on that side).</summary>
            public int MaskL, MaskR;

            internal void Derive()
            {
                MaskL = MaskR = 0;
                for (int k = 0; k < SideFlags - 1; k++)
                {
                    if (!Left[1 + k]) MaskL |= 1 << k;
                    if (!Right[1 + k]) MaskR |= 1 << k;
                }
            }
        }

        readonly List<Line> lines = new List<Line>();
        int lastHash;
        bool dirty = true;

        /// <summary>The kept lines (the row's motion id and owner), in table order.</summary>
        public IReadOnlyList<Line> Lines => lines;
        /// <summary>The loaded table's bundle and file (null when no table is loaded).</summary>
        public string Bundle { get; private set; }
        public string File { get; private set; }
        public int MotionId { get; private set; }
        /// <summary>The row's owner index (used only when <see cref="OwnerColumn"/>).</summary>
        public int Owner { get; private set; }
        public bool OwnerColumn { get; private set; }
        /// <summary>Rows of the whole table read at load (all motion ids, the header included).</summary>
        public int TableRows { get; private set; }
        /// <summary>Kept lines with fewer cells than a full line (missing flags count as enabled).</summary>
        public int ShortLines { get; private set; }
        /// <summary>Flag cells that were neither a number nor a boolean (counted as enabled).</summary>
        public int BadCells { get; private set; }
        public bool Loaded => File != null;

        /// <summary>The line applied by the last step (null = none matched, everything enabled).</summary>
        public Line Matched { get; private set; }
        /// <summary>The index of <see cref="Matched"/> in <see cref="Lines"/> (-1 = none).</summary>
        public int MatchedIndex { get; private set; } = -1;
        /// <summary>The applied switches (true = enabled).</summary>
        public bool BustL { get; private set; } = true;
        public bool BustR { get; private set; } = true;
        public bool HipL { get; private set; } = true;
        public bool HipR { get; private set; } = true;
        /// <summary>The applied bust shape masks per side (bit k = the k-th id of ShapeEngine.BustMaskIds masked).</summary>
        public int MaskL { get; private set; }
        public int MaskR { get; private set; }

        /// <summary>True for the tables whose second cell is an owner index.</summary>
        public static bool HasOwnerColumn(string file) =>
            !string.IsNullOrEmpty(file) && (file.StartsWith("ail", StringComparison.OrdinalIgnoreCase) || file.StartsWith("ai3p", StringComparison.OrdinalIgnoreCase));

        /// <summary>The next step resolves the line again (a new motion was loaded; the table stays).</summary>
        public void Invalidate() => dirty = true;

        /// <summary>Forgets the table: no line, everything enabled at the next step.</summary>
        public void Clear()
        {
            lines.Clear();
            Bundle = File = null;
            MotionId = Owner = 0;
            OwnerColumn = false;
            TableRows = ShortLines = BadCells = 0;
            Matched = null;
            MatchedIndex = -1;
            dirty = true;
        }

        /// <summary>
        /// Loads the row's table through the catalog (the row's zipmod first, as for its controllers). False with a note when
        /// the row names no table or it cannot be read; the table is then cleared.
        /// </summary>
        public bool Load(ContentCatalog catalog, StudioAnime row, out string note)
        {
            Clear();
            note = null;
            if (row == null || string.IsNullOrEmpty(row.YureBundle) || string.IsNullOrEmpty(row.YureFile)) return false;
            ExcelData data = null;
            try { data = catalog?.LoadAssetAt<ExcelData>(row.Zipmod, row.YureBundle, "", row.YureFile); }
            catch (Exception e) { note = $"sway table {row.YureBundle}/{row.YureFile} failed ({e.GetType().Name}: {e.Message})"; return false; }
            if (data == null || data.list == null) { note = $"sway table {row.YureBundle}/{row.YureFile} not found"; return false; }
            Fill(Rows(data), row.YureFile, row.MotionId, row.YureWho);
            Bundle = row.YureBundle;
            File = row.YureFile;
            return true;
        }

        static IEnumerable<IReadOnlyList<string>> Rows(ExcelData data)
        {
            foreach (var p in data.list) yield return (IReadOnlyList<string>)p?.list ?? Array.Empty<string>();
        }

        /// <summary>Parses table rows for one motion id / owner (used by <see cref="Load"/> and by tests on raw rows).</summary>
        public void Fill(IEnumerable<IReadOnlyList<string>> rows, string file, int motionId, int owner)
        {
            lines.Clear();
            Matched = null;
            MatchedIndex = -1;
            dirty = true;
            MotionId = motionId;
            Owner = owner;
            OwnerColumn = HasOwnerColumn(file);
            TableRows = ShortLines = BadCells = 0;
            foreach (var cells in rows)
            {
                TableRows++;
                if (cells == null || cells.Count == 0 || !TryInt(cells[0], out int id) || id != motionId) continue;
                int at = 1, lineOwner = -1;
                if (OwnerColumn)
                {
                    if (cells.Count < 2 || !TryInt(cells[1], out lineOwner) || lineOwner != owner) continue;
                    at = 2;
                }
                if (cells.Count <= at) continue;
                var l = new Line { State = cells[at] ?? "", Owner = lineOwner };
                l.StateHash = Animator.StringToHash(l.State);
                int f = at + 1;
                if (cells.Count < f + FlagCells) ShortLines++;
                for (int k = 0; k < SideFlags; k++) l.Left[k] = Flag(cells, f + k);
                for (int k = 0; k < SideFlags; k++) l.Right[k] = Flag(cells, f + SideFlags + k);
                l.HipL = Flag(cells, f + 2 * SideFlags);
                l.HipR = Flag(cells, f + 2 * SideFlags + 1);
                l.Derive();
                lines.Add(l);
            }
        }

        bool Flag(IReadOnlyList<string> cells, int i)
        {
            if (i >= cells.Count) return true;
            var s = cells[i];
            if (TryInt(s, out int v)) return v != 0;
            if (bool.TryParse(s, out bool b)) return b;
            BadCells++;
            return true;
        }

        static bool TryInt(string s, out int v) => int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        /// <summary>
        /// One step: resolves the line of the body's layer-0 state (only when the state changed since the last step or the
        /// table changed) and updates the applied switches and masks. True when one of them changed.
        /// </summary>
        public bool Step(int layer0StateHash)
        {
            if (!dirty && layer0StateHash == lastHash) return false;
            dirty = false;
            lastHash = layer0StateHash;
            Line hit = null;
            int hitIndex = -1;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].StateHash == layer0StateHash) { hit = lines[i]; hitIndex = i; break; }
            Matched = hit;
            MatchedIndex = hitIndex;
            bool bl = hit == null || hit.Left[0], br = hit == null || hit.Right[0];
            bool hl = hit == null || hit.HipL, hr = hit == null || hit.HipR;
            int ml = hit != null ? hit.MaskL : 0, mr = hit != null ? hit.MaskR : 0;
            if (bl == BustL && br == BustR && hl == HipL && hr == HipR && ml == MaskL && mr == MaskR) return false;
            BustL = bl; BustR = br; HipL = hl; HipR = hr; MaskL = ml; MaskR = mr;
            return true;
        }

        /// <summary>Everything enabled now (no line applied); true when something was not enabled.</summary>
        public bool ResetApplied()
        {
            Matched = null;
            MatchedIndex = -1;
            dirty = true;
            if (BustL && BustR && HipL && HipR && MaskL == 0 && MaskR == 0) return false;
            BustL = BustR = HipL = HipR = true;
            MaskL = MaskR = 0;
            return true;
        }

        public override string ToString() => Loaded
            ? $"sway {Bundle}/{File} motion {MotionId}{(OwnerColumn ? " owner " + Owner : "")}: {lines.Count} lines of {TableRows} rows; applied {(Matched != null ? Matched.State : "-")} bust {B(BustL)}{B(BustR)} hip {B(HipL)}{B(HipR)} masks {MaskL:X2}/{MaskR:X2}"
            : $"sway: no table; applied bust {B(BustL)}{B(BustR)} hip {B(HipL)}{B(HipR)} masks {MaskL:X2}/{MaskR:X2}";

        static char B(bool b) => b ? '1' : '0';
    }
}
