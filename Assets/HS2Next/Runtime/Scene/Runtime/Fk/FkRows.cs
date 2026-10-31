// FK rows of the Studio Bone lists (pose spec
// 1): every `Bone_*` row of the Studio list database (vanilla Bone_00/_10/_35/_50 and zipmod Bone_* lists), one per
// number (the last definition), in the order of the number's first definition; rows without a transform name are not
// in the database. Kept per StudioDb.
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HS2Next.Content.Studio;
using HS2Next.Import.Pose;
using HS2Next.Motion.Fk;

namespace HS2Next.Scenes.Runtime
{
    public static class FkRows
    {
        static readonly ConditionalWeakTable<StudioDb, List<FkRow>> cache = new ConditionalWeakTable<StudioDb, List<FkRow>>();

        /// <summary>The Bone-list rows of <paramref name="db"/> (empty for null).</summary>
        public static IReadOnlyList<FkRow> From(StudioDb db)
        {
            if (db == null) return new FkRow[0];
            lock (cache)
            {
                if (cache.TryGetValue(db, out var rows)) return rows;
                rows = new List<FkRow>();
                foreach (var b in db.BonesInOrder())
                    if (!string.IsNullOrEmpty(b.TransformName))
                        rows.Add(new FkRow { No = b.No, Name = b.TransformName, GroupColumn = b.Group, Level = b.Level, SyncNo = b.SyncNo });
                cache.Add(db, rows);
                return rows;
            }
        }

        /// <summary>The rows as the pose reader's Bone rows (package B's PoseKeyMap input).</summary>
        public static List<PoseBoneInfo> ToPoseBones(IEnumerable<FkRow> rows, string source)
        {
            var l = new List<PoseBoneInfo>();
            foreach (var r in rows)
                l.Add(new PoseBoneInfo { No = r.No, TransformName = r.Name, DisplayName = "", Column = r.GroupColumn, Level = r.Level, SyncNo = r.Additional ? -1 : r.SyncNo, Source = source, IsNode = r.Additional });
            return l;
        }
    }
}
