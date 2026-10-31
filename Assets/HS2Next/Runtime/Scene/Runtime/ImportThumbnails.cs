// The thumbnail hook of the importer for standalone file imports (Phase 4.6):
// a coordinate card gets the coordinate thumbnail (MakerThumbnails.Coordinate: fresh documents of `coord.sex` wearing it,
// the Maker pose, isolated), a character card the isolated front shot of its documents (MakerThumbnails.Character). The
// scenes' shared importer has no hook (an import per card add must not build twice); a batch import that writes files
// passes an instance in ImportOptions.Thumbnails. Needs a graphics device (without one both return null: the save writes
// the placeholder).
using HS2Next.Import;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ImportThumbnails : IImportThumbnailRenderer
    {
        readonly RuntimeServices services;
        readonly ThumbnailSettings settings;

        /// <summary>Findings of the renders (thumbnail.*), collected over every call.</summary>
        public readonly FindingList Findings = new FindingList();

        public ImportThumbnails(RuntimeServices services, ThumbnailSettings settings = null)
        {
            this.services = services ?? throw new System.ArgumentNullException(nameof(services));
            this.settings = settings;
        }

        public byte[] RenderChara(CharaContainer chara) =>
            chara == null ? null : MakerThumbnails.Character(chara, services, settings, Findings, out _, out _, out _);

        public byte[] RenderCoord(CoordContainer coord) =>
            coord == null ? null : MakerThumbnails.Coordinate(coord, services, settings, Findings);
    }
}
