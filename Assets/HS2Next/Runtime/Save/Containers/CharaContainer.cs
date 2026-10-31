// .hs2nchara: one character subtree at the root, plus the
// container-wide parts (thumbnail, res/, foreign entries, manifest). Also the conversions that move
// content between files: saving the worn coordinate out and applying a coordinate.
using System;
using System.Collections.Generic;
using System.IO;

namespace HS2Next.Save
{
    public sealed class CharaContainer : ContainerBase
    {
        public CharaSubtree Subtree = new CharaSubtree();

        public CharaDocument Chara => Subtree.Chara;
        public CoordDocument Coord => Subtree.Coord;

        public override string Format => SaveFormats.Chara;
        internal override string MainDocument => CharaDocument.EntryName;
        internal override string ManifestTitle => Subtree.Chara.Profile.Name;
        internal override int? ManifestSex => Subtree.Chara.Profile.Sex;
        internal override ImportProvenance ManifestImport => Subtree.Import;

        public static LoadResult<CharaContainer> Load(string path, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(path), options, SaveFormats.Chara, Bind);

        public static LoadResult<CharaContainer> Load(byte[] bytes, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(bytes), options, SaveFormats.Chara, Bind);

        public static LoadResult<CharaContainer> Load(Stream stream, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(stream), options, SaveFormats.Chara, Bind);

        static CharaContainer Bind(LoadSession s)
        {
            CharaSubtree.ParseDocs(s, "", true);
            s.Prepare();
            var c = new CharaContainer();
            c.Subtree = CharaSubtree.Bind(s, "", s.Manifest.Import);
            c.TakeCommon(s);
            s.Dispatch(c.Subtree.Instances(SaveFormats.Chara, "", null));
            s.Finish(c, ContainerAreas.IsCore);
            return c;
        }

        internal override void Build(SaveSession s)
        {
            ExtDispatcher.Write(Subtree.Instances(Format, "", null), s.Options.RegistryOrDefault, s.Findings);
            Subtree.Save(s, "");
        }

        /// <summary>The worn coordinate as a coordinate file: coord/ (documents, ext, hs2.kkex) plus the resources it references.</summary>
        public CoordContainer ExtractCoordinate()
        {
            var c = new CoordContainer
            {
                Coord = CharaSubtree.CloneCoord(Subtree.Coord),
                CoordExt = Subtree.CoordExt.Clone(),
            };
            c.Res.CopyFrom(Res, CharaSubtree.ReferencedResources(c.Coord));
            return c;
        }

        /// <summary>Replaces the whole coord/ subtree (its ext included) with a copy of <paramref name="src"/> and copies the resources it references.</summary>
        public void ApplyCoordinate(CoordContainer src)
        {
            Subtree.Coord = CharaSubtree.CloneCoord(src.Coord);
            Subtree.CoordExt = src.CoordExt.Clone();
            Res.CopyFrom(src.Res, CharaSubtree.ReferencedResources(Subtree.Coord));
        }
    }

    /// <summary>The core areas: everything else is a foreign entry.</summary>
    static class ContainerAreas
    {
        public static bool IsCore(string n)
        {
            if (n == EntryNames.Manifest || n == CharaDocument.EntryName || n == CoordDocument.EntryName || n == SceneDocument.EntryName || n == PoseDocument.EntryName) return true;
            if (n.StartsWith("coord/", StringComparison.Ordinal) || n.StartsWith("charas/", StringComparison.Ordinal) ||
                n.StartsWith(EntryNames.ExtDir, StringComparison.Ordinal) || n.StartsWith(EntryNames.ResPrefix, StringComparison.Ordinal)) return true;
            if (n.StartsWith("nodes/", StringComparison.Ordinal))
            {
                int slash = n.IndexOf('/', 6);
                return slash > 6 && n.Length > slash + 4 && string.CompareOrdinal(n, slash + 1, "ext/", 0, 4) == 0;
            }
            return false;
        }
    }
}
