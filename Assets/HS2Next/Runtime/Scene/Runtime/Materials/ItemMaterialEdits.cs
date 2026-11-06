// The material edits of one Studio item: `item.materialOverrides` on the owned copies of ItemMaterialWriter, after every colour, pattern
// and panel write of the item, so an edit sits on top of the fields and of the runtime colour slots. A saved scene reloads to the same
// picture because the order does not depend on who wrote last.
//   Build     a part right after the colours; the realization calls Apply.
//   Changes   Apply after the patterns, the panel and the colours, on a change of the array, after a colour slot's write. A change of
//             a shader name or another structural member is a rebuild of the item (NeedsRebuild), a value change is a pass.
//   Removal   the baseline writes back the value the edit replaced. That value can be stale when a field took exactly the edit's value
//             under it, so after a pass that put anything back the fields are written again and the array once more on top of them.
//   Release   the baseline and the decoded images go with the content, next to the writer's copies.
using System;
using System.Collections.Generic;
using System.Text;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ItemMaterialEdits
    {
        sealed class ItemTarget : MaterialTargets
        {
            readonly ItemRealization item;
            readonly List<Renderer> renderers = new List<Renderer>();

            public ItemTarget(ItemRealization item, ContentCatalog catalog) : base(catalog)
            {
                this.item = item;
                foreach (var e in item.Writer.Renderers) if (e.Renderer) renderers.Add(e.Renderer);
            }

            public override IReadOnlyList<Renderer> Renderers => renderers;
            protected override ResStore Store => item.Runtime.Session?.Model?.File?.Res;
            protected override bool Owns(Material m) => true;   // the index holds the writer's copies only

            public override string PathOf(Renderer r) => r && item.Instance ? PathIn(item.Instance.transform, r.transform) : null;

            protected override void Fill()
            {
                foreach (var e in item.Writer.Renderers)
                    foreach (var s in e.Slots)
                        if (s != null) Note(s.MaterialName, s.Material);
            }

            protected override void FillMembers()
            {
                foreach (var e in item.Writer.Renderers)
                {
                    if (!MaterialGuard.Renderer(e.Renderer)) continue;
                    foreach (var s in e.Slots)
                        if (s != null) NoteMember(s.Material);
                }
            }

            protected override void FillPaths(Dictionary<string, Renderer> into)
            {
                foreach (var r in renderers) if (r) into[PathOf(r)] = r;
            }
        }

        /// <summary>The path of <paramref name="t"/> under <paramref name="root"/>: "." for the root itself, else names joined by '/', "[k]"
        /// on every one of several same-named siblings.</summary>
        public static string PathIn(Transform root, Transform t)
        {
            if (t == root) return ".";
            var sb = new StringBuilder();
            for (var x = t; x && x != root; x = x.parent)
            {
                string seg = x.name;
                var p = x.parent;
                if (p)
                {
                    int count = 0, k = 0;
                    for (int i = 0; i < p.childCount; i++)
                    {
                        var c = p.GetChild(i);
                        if (c == x) k = count;
                        if (c.name == seg) count++;
                    }
                    seg = MapStructure.Occurrence(seg, k, count);
                }
                sb.Insert(0, sb.Length == 0 ? seg : seg + "/");
            }
            return sb.ToString();
        }

        readonly ItemRealization item;
        readonly Action writeColors;
        ItemTarget target;
        string structure = "";

        internal ItemMaterialEdits(ItemRealization item, Action writeColors)
        {
            this.item = item;
            this.writeColors = writeColors;
        }

        IReadOnlyList<MaterialOverride> Items => (item.Node.State as ItemNodeState)?.MaterialOverrides;

        /// <summary>What the edits reach; null unless the item is built.</summary>
        public MaterialTargets Target
        {
            get
            {
                if (target == null && item.State == RealizationState.Built && item.Instance) target = new ItemTarget(item, item.Runtime.Services.Catalog);
                return target;
            }
        }

        /// <summary>The array's structural members differ from the ones built.</summary>
        public bool NeedsRebuild
        {
            get
            {
                var l = Items;
                return l != null && MeStructure.Of(l) != structure;
            }
        }

        /// <summary>The array onto the owned copies; nothing to do for an item that never had an item.</summary>
        internal void Apply()
        {
            var list = Items;
            if (target == null && (list == null || list.Count == 0)) return;
            var t = Target;
            if (t == null) return;
            structure = list != null ? MeStructure.Of(list) : "";
            if (t.Pass(list))
            {
                writeColors();
                t.Pass(list);
            }
            Findings(t);
        }

        void Findings(MaterialTargets t)
        {
            var rt = item.Runtime;
            if (t.MosaicKept > 0)
                rt.ReportOnce(FindingSeverity.Warning, MosaicSet.MeProtectedCode, item.NodeId, t.MosaicKept + " material edit(s) name mosaic parts: kept, not applied");
            if (t.ShadersMissing.Count > 0)
                rt.ReportOnce(FindingSeverity.Warning, CharaRealization.MeShaderUnavailableCode, item.NodeId, "material edits name shaders that cannot be drawn: " + string.Join(", ", t.ShadersMissing));
            if (t.ImageTooLarge)
                rt.ReportOnce(FindingSeverity.Warning, MaterialEditorApplier.TextureTooLargeKind, item.NodeId, "a material edit's image is larger than the decoder takes");
        }

        /// <summary>The content is going (rebuild, delete for good).</summary>
        internal void Release()
        {
            target?.Release();
            target = null;
            structure = "";
        }
    }
}
