// .hs2npose: an HS2Next pose file,
// pose.json at the root plus the container-wide parts (PNG thumbnail, manifest, res/, foreign entries). No ext
// data in v1: files under ext/ are orphans (reported, dropped on save). The manifest title and sex are the
// document's name and sex, so a library list reads them from the manifest alone.
using System.IO;

namespace HS2Next.Save
{
    public sealed class PoseContainer : ContainerBase
    {
        public PoseDocument Pose = new PoseDocument();

        public override string Format => SaveFormats.Pose;
        internal override string MainDocument => PoseDocument.EntryName;
        internal override string ManifestTitle => Pose?.Name ?? "";
        internal override int? ManifestSex => Pose?.Sex;

        public static LoadResult<PoseContainer> Load(string path, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(path), options, SaveFormats.Pose, Bind);

        public static LoadResult<PoseContainer> Load(byte[] bytes, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(bytes), options, SaveFormats.Pose, Bind);

        public static LoadResult<PoseContainer> Load(Stream stream, LoadOptions options = null) =>
            Run(() => ContainerReader.Read(stream), options, SaveFormats.Pose, Bind);

        static PoseContainer Bind(LoadSession s)
        {
            s.Doc(PoseDocument.EntryName, true);
            s.Prepare();
            var c = new PoseContainer { Pose = PoseDocument.Read(s.Reader(PoseDocument.EntryName)) };
            c.TakeCommon(s);
            s.Finish(c, ContainerAreas.IsCore);
            return c;
        }

        internal override void Build(SaveSession s)
        {
            if (Pose == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "pose document is null", PoseDocument.EntryName);
            s.AddDoc(PoseDocument.EntryName, Pose.Write);
        }
    }
}
