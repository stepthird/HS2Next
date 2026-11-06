// What a material edit of an item or the map reaches, built when first needed: at the pass of an array that is not empty, else when a
// reader asks. It holds the target's renderers with their paths, an index from a formatted material name to the materials of that
// name, and the images the array names. An item's index is small; a map's walks its renderers once for the materials, once more
// for the mosaic members at the first selection, once for the paths at the first path asked, and nothing walks them again: a pass or
// a drag step looks a name up.
//   Addressing   `material` selects every material of that name on the target. On an item each slot has its own copy, on a map a copy
//                is shared by its renderers. `renderer` is a path: names joined by '/', "[k]" on every one of several same-named
//                siblings. On a map it starts with the root's name; on an item it is relative to the instance, whose own renderer is ".".
//   Ownership    a write lands only on a material the target owns. Select with forWrite gives a slot it does not own a copy first, so a
//                bundle material is never written; a material a mosaic member shares with other renderers stays with the member, the
//                others get a copy. What cannot be copied is left out, and the item reads as materialMissing.
//   Pass         the items in apply order through MaterialWrites.One, then the baseline's sweep puts back what left the array, then the
//                images no item names any more are released.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public abstract class MaterialTargets : MaterialWriteTarget
    {
        /// <summary>One material of the target.</summary>
        public sealed class MaterialEntry
        {
            /// <summary>The formatted name: what an item's `material` says.</summary>
            public string Name;
            public Material Material;
            /// <summary>The renderer slots that draw it.</summary>
            public int Slots;
            /// <summary>Those slots on a mosaic member; the first read counts them on every material (<see cref="Members"/>).</summary>
            public int MemberSlots { get { Owner.Members(); return Held; } set { Held = value; } }
            internal MaterialTargets Owner;
            internal int Held;
        }

        sealed class Held
        {
            public Texture2D Texture;
            public string Key;
            public int Pass;
        }

        readonly List<MaterialEntry> materials = new List<MaterialEntry>();
        readonly Dictionary<string, List<MaterialEntry>> byName = new Dictionary<string, List<MaterialEntry>>(StringComparer.Ordinal);
        readonly Dictionary<Material, MaterialEntry> byMaterial = new Dictionary<Material, MaterialEntry>();
        readonly Dictionary<(string res, bool linear), Held> images = new Dictionary<(string, bool), Held>();
        readonly List<(string res, bool linear)> unused = new List<(string, bool)>();
        Dictionary<string, Renderer> byPath;
        IReadOnlyList<MaterialOverride> orderedFrom;
        List<MaterialOverride> ordered;
        bool indexed, membersKnown;
        int pass;

        protected MaterialTargets(ContentCatalog catalog)
        {
            Catalog = catalog;
            Baseline = new MaterialBaseline();
        }

        /// <summary>The target's renderers.</summary>
        public abstract IReadOnlyList<Renderer> Renderers { get; }
        /// <summary>The path a `renderer` item names this renderer by.</summary>
        public abstract string PathOf(Renderer r);
        /// <summary>Calls <see cref="Note"/> for every slot of the target.</summary>
        protected abstract void Fill();
        /// <summary>Calls <see cref="NoteMember"/> for every slot of a mosaic member.</summary>
        protected abstract void FillMembers();
        /// <summary>Fills <paramref name="into"/> with every renderer and its path.</summary>
        protected abstract void FillPaths(Dictionary<string, Renderer> into);
        /// <summary>The store the target's images come from.</summary>
        protected abstract ResStore Store { get; }
        protected abstract bool Owns(Material m);
        /// <summary>The material goes on with the non-member slots only, on a copy the target owns; false when no copy can be made.</summary>
        protected virtual bool Isolate(MaterialEntry e) => false;

        /// <summary>Every material of the target, in the order the walk met them.</summary>
        public IReadOnlyList<MaterialEntry> Materials { get { Index(); return materials; } }

        /// <summary>Time of the walk that built the name index (0 until built).</summary>
        public double IndexMilliseconds { get; private set; }
        /// <summary>Time of the walk that found the mosaic members (0 until built).</summary>
        public double MemberMilliseconds { get; private set; }
        /// <summary>Time of the walk that built the path index (0 until built).</summary>
        public double PathMilliseconds { get; private set; }
        /// <summary>The time of the last pass, for the reports of a drag.</summary>
        public double LastPassMilliseconds { get; private set; }
        /// <summary>The last pass met an image with a header above the decoder's limit.</summary>
        public bool ImageTooLarge { get; private set; }
        /// <summary>Items of the last pass that a mosaic rule kept from applying.</summary>
        public int MosaicKept { get; private set; }
        /// <summary>The shaders items of the last pass named and nothing here can draw.</summary>
        public readonly SortedSet<string> ShadersMissing = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>The materials of a formatted name; empty when none.</summary>
        public IReadOnlyList<MaterialEntry> Named(string material)
        {
            Index();
            return byName.TryGetValue(MeKey.Format(material), out var l) ? l : (IReadOnlyList<MaterialEntry>)Array.Empty<MaterialEntry>();
        }

        // ---------------------------------------------------------------- index

        /// <summary>Builds the name index once.</summary>
        public void Index()
        {
            if (indexed) return;
            indexed = true;
            var sw = Stopwatch.StartNew();
            Fill();
            IndexMilliseconds = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Counts the mosaic members' slots once.</summary>
        public void Members()
        {
            Index();
            if (membersKnown) return;
            membersKnown = true;
            var sw = Stopwatch.StartNew();
            FillMembers();
            MemberMilliseconds = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>One slot of the walk; <paramref name="name"/> null: the material's own name, read once.</summary>
        protected void Note(string name, Material m)
        {
            if (!m) return;
            if (!byMaterial.TryGetValue(m, out var e)) e = AddEntry(MeKey.Format(name ?? m.name), m);
            e.Slots++;
        }

        /// <summary>One slot of a mosaic member.</summary>
        protected void NoteMember(Material m)
        {
            if (m && byMaterial.TryGetValue(m, out var e)) e.Held++;
        }

        /// <summary>A material the index does not hold yet.</summary>
        protected MaterialEntry AddEntry(string formatted, Material m)
        {
            var e = new MaterialEntry { Name = formatted, Material = m, Owner = this };
            byMaterial[m] = e;
            materials.Add(e);
            if (!byName.TryGetValue(formatted, out var l)) byName[formatted] = l = new List<MaterialEntry>(1);
            l.Add(e);
            return e;
        }

        /// <summary>The entry now draws <paramref name="m"/> instead of the material it had.</summary>
        protected void Replace(MaterialEntry e, Material m)
        {
            byMaterial.Remove(e.Material);
            e.Material = m;
            byMaterial[m] = e;
        }

        public override void Select(string material, List<Material> into, bool forWrite, out bool heldByMember, out string note)
        {
            Members();
            heldByMember = false;
            note = null;
            if (!byName.TryGetValue(MeKey.Format(material), out var rows)) return;
            for (int i = 0, n = rows.Count; i < n; i++)
            {
                var e = rows[i];
                if (e.Held > 0) heldByMember = true;
                if (e.Held == e.Slots) continue;
                if (forWrite && (e.Held > 0 || !Owns(e.Material)) && !Isolate(e)) continue;
                into.Add(e.Material);
            }
        }

        public override void SelectRenderers(string renderer, List<Renderer> into)
        {
            if (renderer == null) return;
            if (byPath == null)
            {
                var sw = Stopwatch.StartNew();
                byPath = new Dictionary<string, Renderer>(StringComparer.Ordinal);
                FillPaths(byPath);
                PathMilliseconds = sw.Elapsed.TotalMilliseconds;
            }
            if (byPath.TryGetValue(renderer, out var r) && r) into.Add(r);
        }

        // ---------------------------------------------------------------- images

        public override Texture2D Image(string res, bool linear)
        {
            var key = (res, linear);
            if (images.TryGetValue(key, out var h)) { h.Pass = pass; return h.Texture; }
            h = new Held { Pass = pass };
            var bytes = Store?.Get(res);
            if (bytes != null)
            {
                if (ExtTextures.TooLarge(bytes)) ImageTooLarge = true;
                h.Texture = ExtTextures.Acquire(bytes, linear, true, res, out h.Key);
            }
            images[key] = h;
            return h.Texture;
        }

        public override bool ImageReadable(string res)
        {
            var bytes = Store?.Get(res);
            return bytes != null && !ExtTextures.TooLarge(bytes) && ExtTextures.TrySize(bytes, out _, out _);
        }

        void Drop(Held h)
        {
            if (h.Texture) TextureSettingsDone.Remove(h.Texture);
            ExtTextures.Release(h.Key);
        }

        // ---------------------------------------------------------------- pass

        /// <summary>Writes the array onto the target. <paramref name="before"/> sees each item ahead of its write. True when something
        /// that left the array went back.</summary>
        public bool Pass(IReadOnlyList<MaterialOverride> list, Action<MaterialOverride> before = null)
        {
            Index();
            long start = Stopwatch.GetTimestamp();
            pass++;
            MosaicKept = 0;
            ImageTooLarge = false;
            ShadersMissing.Clear();
            Baseline.Begin();
            if (list != null)
            {
                // an edit makes a new list, so a pass after a colour write finds the order it had
                if (!ReferenceEquals(list, orderedFrom)) { ordered = MaterialEdits.InApplyOrder(list); orderedFrom = list; }
                foreach (var item in ordered)
                {
                    before?.Invoke(item);
                    string why = MaterialWrites.One(item, this);
                    if (why == MaterialStatus.MosaicProtected) MosaicKept++;
                    else if (why == MaterialStatus.ShaderUnavailable && item is MeShader s && s.Shader != null) ShadersMissing.Add(s.Shader);
                }
            }
            int held = Baseline.Count;
            Baseline.Sweep(MaterialMember.All);
            bool swept = Baseline.Count < held;
            unused.Clear();
            foreach (var kv in images) if (kv.Value.Pass != pass) unused.Add(kv.Key);
            foreach (var k in unused)
            {
                Drop(images[k]);
                images.Remove(k);
            }
            LastPassMilliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            return swept;
        }

        /// <summary>What the item does now: no write, no copy, no decode.</summary>
        public MaterialItemState Status(MaterialOverride item) => MaterialWrites.StatusOf(item, this);

        /// <summary>The target's content is going: the baseline forgets it and the images are released.</summary>
        public void Release()
        {
            Baseline.Clear();
            foreach (var h in images.Values) Drop(h);
            images.Clear();
            TextureSettingsDone.Clear();
            orderedFrom = null;
            ordered = null;
        }
    }
}
