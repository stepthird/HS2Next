// .hs2ncoord: coord.json at the root, its coord-scope ext
// data (root "") and the container-wide parts.
using System.IO;
using HS2Next.Api.Save;

namespace HS2Next.Save
{
    public sealed class CoordContainer : ContainerBase
    {
        public CoordDocument Coord = new CoordDocument();
        /// <summary>File-backed ext data of the coord scope (blocks and hs2.kkex).</summary>
        public ExtScopeData CoordExt = new ExtScopeData(ExtScope.Coord);
        /// <summary>Provenance of an HS2 coordinate import; null otherwise.</summary>
        public ImportProvenance Import;

        public override string Format => SaveFormats.Coord;
        internal override string MainDocument => CoordDocument.EntryName;
        internal override string ManifestTitle => Coord.Name;
        internal override int? ManifestSex => Coord.Sex;
        internal override ImportProvenance ManifestImport => Import;

        public static LoadResult<CoordContainer> Load(string path, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(path), options, SaveFormats.Coord, Bind);

        public static LoadResult<CoordContainer> Load(byte[] bytes, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(bytes), options, SaveFormats.Coord, Bind);

        public static LoadResult<CoordContainer> Load(Stream stream, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(stream), options, SaveFormats.Coord, Bind);

        internal ExtInstance Instance(string format) => new ExtInstance
        {
            Scope = ExtScope.Coord, ContainerFormat = format, Root = "", Data = CoordExt, Main = Coord.Ext, Accessories = new CoordAccessoryExt(Coord),
            Clothes = new CoordClothesExt(Coord),
        };

        static CoordContainer Bind(LoadSession s)
        {
            s.Doc(CoordDocument.EntryName, true);
            s.Prepare();
            var c = new CoordContainer
            {
                Coord = CoordDocument.Read(s.Reader(CoordDocument.EntryName)),
                Import = s.Manifest.Import,
            };
            c.CoordExt = s.BuildScope(ExtScope.Coord, "coord", "", "", c.Coord.Ext, new CoordAccessoryExt(c.Coord), new CoordClothesExt(c.Coord));
            c.TakeCommon(s);
            s.Dispatch(new[] { c.Instance(SaveFormats.Coord) });
            s.Finish(c, ContainerAreas.IsCore);
            return c;
        }

        internal override void Build(SaveSession s)
        {
            ExtDispatcher.Write(new[] { Instance(Format) }, s.Options.RegistryOrDefault, s.Findings);
            s.Covers(Coord);
            s.AddDoc(CoordDocument.EntryName, Coord.Write);
            s.AddExt(CoordExt, "coord", "", "", Coord.Ext, new CoordAccessoryExt(Coord), new CoordClothesExt(Coord));
        }
    }
}
