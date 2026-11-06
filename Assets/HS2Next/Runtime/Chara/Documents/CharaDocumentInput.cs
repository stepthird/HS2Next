// Building a character from documents: a stored character subtree (+ the resources of
// the container that holds it) becomes a CharaBuildInput for the unchanged assembler: the HS2 node trees (
// VanillaTrees), decoder-shaped plugin objects (ExtSections), the accessory build slots and a part
// resolver that hands the assembler the row of each already resolved reference. hs2.kkex and unknown members
// are never read. Phase 4.0 replaces the adapter with per-item setters that read the documents.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Chara.Documents
{
    public static class CharaDocumentInput
    {
        /// <summary>Time of the last From call in milliseconds.</summary>
        public static double LastMilliseconds { get; private set; }

        /// <summary>The build input of a stored character subtree whose images live in <paramref name="res"/>.</summary>
        public static CharaBuildInput From(CharaSubtree subtree, ResStore res, ContentCatalog catalog, FindingList findings)
        {
            if (subtree == null) throw new ArgumentNullException(nameof(subtree));
            var sw = Stopwatch.StartNew();
            var f = findings ?? new FindingList();
            var chara = subtree.Chara ?? new CharaDocument();
            var coord = subtree.Coord ?? new CoordDocument();
            var parts = new DocumentPartResolver(catalog, f);
            var slots = AccessorySlotTable.Assign(coord.Accessories, f);
            var card = VanillaTrees.Build(chara, coord, slots, parts, f);
            var ext = ExtSections.Build(chara, coord, slots, res, f);
            var input = new CharaBuildInput
            {
                Card = card, Ext = ext, Parts = parts, AccessoryIds = slots.IdBySlot,
                Source = "document " + (chara.Profile?.Name ?? ""),
                Chara = chara, Coord = coord, Res = res, Slots = slots,
            };
            LastMilliseconds = sw.Elapsed.TotalMilliseconds;
            return input;
        }

        /// <summary>The build input of a character file.</summary>
        public static CharaBuildInput From(CharaContainer file, ContentCatalog catalog, FindingList findings)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            return From(file.Subtree, file.Res, catalog, findings);
        }

        /// <summary>
        /// After CharaAssembler.Build(input): the keyword items run again on the mapped materials, and the missing content the
        /// document resolver met (part.missingMod, part.missing, part.unresolved) joins Context.Missing, as the card path's texture
        /// guard records do. No-op for card inputs.
        /// </summary>
        public static void AfterBuild(CharaContext ctx)
        {
            ctx?.Edit?.AfterUrpMapping();
            if (ctx?.Input?.Parts is DocumentPartResolver r)
                foreach (var m in r.Missing)
                    if (!ctx.Missing.Exists(x => x.Kind == m.Kind && x.Detail == m.Detail)) ctx.Missing.Add(m);
        }

        /// <summary>The slot table the input was built with (recomputed from the coordinate; tests and diagnostics).</summary>
        public static AccessorySlotTable Slots(CharaSubtree subtree, FindingList findings = null) =>
            AccessorySlotTable.Assign(subtree?.Coord?.Accessories ?? new List<AccessoryEntry>(), findings);
    }
}
