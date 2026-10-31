// A stand-alone folder of zipmods (usable without an HS2 install). No abdata, no own lists.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HS2Next.Content
{
    public sealed class ModsFolderSource : ZipmodSourceBase
    {
        /// <param name="dir">Folder searched recursively for zipmods.</param>
        /// <param name="id">Source Id; defaults to "mods:&lt;folder name&gt;".</param>
        public ModsFolderSource(string dir, string id = null)
            : base(id ?? "mods:" + Path.GetFileName(dir.TrimEnd('\\', '/')), "Mods folder " + Path.GetFileName(dir.TrimEnd('\\', '/')), dir) { }

        public override IEnumerable<ListEntry> ReadLists(IBundleCache cache) => Enumerable.Empty<ListEntry>();
        public override bool HasBundle(string path) => false;
        public override AssetBundle OpenBundle(string path, IBundleCache cache) => null;
    }
}
