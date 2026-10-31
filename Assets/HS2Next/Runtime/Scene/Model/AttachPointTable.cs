// Character attach points: the 54 AccessoryParentKey names
// and the groups the Studio list (AccessoryPointGroup) shows them in. A name is known when
// it is one of the 54; it is offered for new attachments when a group range covers it. Stored names that are
// not known are kept verbatim; new attachments must use an offered name.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>One AccessoryPointGroup row: group number, display name (list data), point value range.</summary>
    public sealed class AttachPointGroup
    {
        public int No;
        public string Name;
        /// <summary>First and last AccessoryParentKey value (1..54) of the range, inclusive.</summary>
        public int First, Last;

        public AttachPointGroup() { }
        public AttachPointGroup(int no, string name, int first, int last) { No = no; Name = name; First = first; Last = last; }

        /// <summary>Point names of the range that exist (values in 1..54), ascending.</summary>
        public IReadOnlyList<string> Points
        {
            get
            {
                var l = new List<string>();
                for (int v = Math.Max(1, First); v <= Math.Min(DocEnum.AttachPoints.Length, Last); v++) l.Add(DocEnum.AttachPoints[v - 1]);
                return l;
            }
        }
    }

    public sealed class AttachPointTable
    {
        /// <summary>Rank of the "unknown name" group in canonical child order.</summary>
        public const int UnknownRank = 55;

        readonly List<AttachPointGroup> groups;
        // value → group, for offered values only
        readonly AttachPointGroup[] byValue = new AttachPointGroup[DocEnum.AttachPoints.Length + 1];

        AttachPointTable(List<AttachPointGroup> groups)
        {
            this.groups = groups;
            foreach (var g in groups)
                for (int v = Math.Max(1, g.First); v <= Math.Min(DocEnum.AttachPoints.Length, g.Last); v++)
                    if (byValue[v] == null) byValue[v] = g;
        }

        /// <summary>The vanilla table: 10 groups, all 54 points offered.</summary>
        public static AttachPointTable Vanilla { get; } = new AttachPointTable(new List<AttachPointGroup>
        {
            new AttachPointGroup(0, "髪", 1, 5),
            new AttachPointGroup(1, "頭", 6, 9),
            new AttachPointGroup(2, "顔", 10, 14),
            new AttachPointGroup(3, "首", 15, 17),
            new AttachPointGroup(4, "胴", 18, 22),
            new AttachPointGroup(5, "腰", 23, 27),
            new AttachPointGroup(6, "脚", 28, 35),
            new AttachPointGroup(7, "腕", 36, 43),
            new AttachPointGroup(8, "手", 44, 51),
            new AttachPointGroup(9, "股間", 52, 54),
        });

        /// <summary>
        /// A table from list rows (the Unity layer reads StudioDb.AccessoryPointGroups). Range values outside 1..54
        /// have no name: reported once per row as <c>attach.unknownIndex</c> (warning) and ignored.
        /// </summary>
        public static AttachPointTable FromGroups(IEnumerable<AttachPointGroup> rows, FindingList findings = null)
        {
            var list = new List<AttachPointGroup>();
            foreach (var r in rows ?? Array.Empty<AttachPointGroup>())
            {
                if (r == null) continue;
                var g = new AttachPointGroup(r.No, r.Name, r.First, r.Last);
                if (g.First <= g.Last && (g.First < 1 || g.Last > DocEnum.AttachPoints.Length))
                    findings?.Warning("attach.unknownIndex", "AccessoryPointGroup:" + g.No,
                        "range " + g.First + ".." + g.Last + " leaves 1.." + DocEnum.AttachPoints.Length + "; the indices outside have no point name and are ignored");
                list.Add(g);
            }
            return new AttachPointTable(list);
        }

        /// <summary>The groups in list order.</summary>
        public IReadOnlyList<AttachPointGroup> Groups => groups;

        /// <summary>One of the 54 AccessoryParentKey names (resolves at run time).</summary>
        public bool IsKnown(string name) => ValueOf(name) > 0;

        /// <summary>Known and covered by a group range: may be used for a new attachment.</summary>
        public bool IsOffered(string name)
        {
            int v = ValueOf(name);
            return v > 0 && byValue[v] != null;
        }

        /// <summary>AccessoryParentKey value 1..54, or 0 when the name is not known.</summary>
        public int ValueOf(string name) => DocEnum.AttachPointValue(name);

        /// <summary>The group that offers the point, or null.</summary>
        public AttachPointGroup GroupOf(string name)
        {
            int v = ValueOf(name);
            return v > 0 ? byValue[v] : null;
        }

        /// <summary>Canonical group rank of a child's point: 0 for none, 1..54 for a known name,
        /// <see cref="UnknownRank"/> for any other name.</summary>
        public static int Rank(string point)
        {
            if (point == null) return 0;
            int v = DocEnum.AttachPointValue(point);
            return v > 0 ? v : UnknownRank;
        }
    }
}
