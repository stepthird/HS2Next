// Heads that bring their own face compositing materials, with the HeadBundleRedirect plugin's
// semantics (behaviour only): a zipmod whose manifest.xml root has <Head_Bundle_Redirect Slot="n" Path="p"/> (the first such
// element) makes the two face compositing passes of its female head n (category 210) load their materials from the bundle p
// instead of chara/mm_base.unity3d. Male heads (110) never match (the plugin keys female head slots only, and Sideloader numbers
// every mod row uniquely). An element without an integer Slot or without a Path redirects nothing. It reads the catalog's
// zipmods (ZipmodInfo), so it sits with the catalog-side Maker readers, outside the plain-Mono Maker lists.
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.Content.Maker
{
    public static class HeadBundleRedirect
    {
        public const string Element = "Head_Bundle_Redirect";
        /// <summary>The bundle the face compositing materials load from when no redirect applies.</summary>
        public const string VanillaBundle = "chara/mm_base.unity3d";
        public const int FemaleHeadCategory = 210;

        // per zipmod: its element's (slot, path), or null when it has none or the element redirects nothing
        static readonly Dictionary<ZipmodInfo, (int slot, string path)?> cache = new Dictionary<ZipmodInfo, (int, string)?>();

        /// <summary>The bundle the face compositing materials of <paramref name="head"/> load from.</summary>
        public static string BundleFor(ListEntry head) => RedirectOf(head) ?? VanillaBundle;

        /// <summary>The redirected bundle of <paramref name="head"/>: the path of its zipmod's element when the row is that zipmod's
        /// female head of the element's slot, else null.</summary>
        public static string RedirectOf(ListEntry head)
        {
            var z = head?.Zipmod;
            if (z == null || z.Guid == null || head.Category != FemaleHeadCategory || !z.HasManifestElement(Element)) return null;
            if (!cache.TryGetValue(z, out var r)) cache[z] = r = Read(z.ReadManifestElement(Element));
            return r.HasValue && r.Value.slot == head.Key.Id ? r.Value.path : null;
        }

        /// <summary>(slot, path) of one element's outer XML, or null when it redirects nothing.</summary>
        public static (int slot, string path)? Read(string outerXml)
        {
            if (string.IsNullOrEmpty(outerXml)) return null;
            var doc = new System.Xml.XmlDocument();
            try { doc.LoadXml(outerXml); }
            catch (System.Xml.XmlException) { return null; }
            var e = doc.DocumentElement;
            if (e == null || !int.TryParse(e.GetAttribute("Slot"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)) return null;
            var path = e.GetAttribute("Path");
            return string.IsNullOrEmpty(path) ? ((int, string)?)null : (slot, path.Replace('\\', '/'));
        }

        /// <summary>Forgets the parsed elements (a rescan replaces the zipmods).</summary>
        public static void Clear() => cache.Clear();
    }
}
