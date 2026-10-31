// Standalone import of an HS2 coordinate card to a coordinate file (Phase 4.6, mirroring CharaFileImport): the importer reads the card into a .hs2ncoord container (its own sex rule: evidence in the card, else
// female); with a graphics device and render settings the coordinate thumbnail is rendered (MakerThumbnails.Coordinate:
// fresh documents of `coord.sex` wearing it, the Maker pose, isolated), else the placeholder is written; the save options
// are completed by AppSaveOptions (app.version, named requires).
using System;
using System.Diagnostics;
using System.IO;
using HS2Next.Import;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Runtime
{
    public sealed class CoordFileImportResult
    {
        public CoordContainer Container;
        public SaveResult Save;
        /// <summary>The rendered thumbnail (PNG 504 × 704), or null when the placeholder was written.</summary>
        public byte[] Thumbnail;
        /// <summary>The importer's report (null when the file was not readable).</summary>
        public ImportReport Report;
        public readonly FindingList BuildFindings = new FindingList();
        public CoordThumbnailInfo ThumbnailInfo;
        public string Error;
        public double ImportMs, ThumbnailMs;
        public bool Ok => Error == null && Save != null && Save.Ok;
        public override string ToString() => Ok
            ? $"ok: {Save.Size} bytes, thumbnail {(Thumbnail != null ? "rendered" : "placeholder")}, import {ImportMs:F0} ms, thumbnail {ThumbnailMs:F0} ms"
            : "failed: " + (Error ?? Save?.Error?.Message);
    }

    public static class CoordFileImport
    {
        /// <summary>
        /// Imports the HS2 coordinate card <paramref name="cardPath"/> and saves it to <paramref name="outPath"/>.
        /// <paramref name="settings"/>: thumbnail settings (e.g. the URP pipeline asset); null, or no graphics device = the
        /// placeholder. <paramref name="options"/> are completed with the app's version and the zipmods' details.
        /// </summary>
        public static CoordFileImportResult ImportToFile(Hs2Importer importer, string cardPath, string outPath, RuntimeServices services, ThumbnailSettings settings,
                                                         SaveOptions options = null)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            var r = new CoordFileImportResult();
            byte[] bytes;
            try { bytes = File.ReadAllBytes(cardPath); }
            catch (Exception e) { r.Error = "card not readable: " + e.Message; return r; }
            var sw = Stopwatch.StartNew();
            CoordImportResult res;
            try { res = importer.ImportCoord(bytes, Path.GetFileName(cardPath), cardPath); }
            catch (Exception e) { r.Error = "import threw " + e.GetType().Name + ": " + e.Message; return r; }
            r.ImportMs = sw.Elapsed.TotalMilliseconds;
            r.Report = res?.Report;
            if (res == null || !res.Ok) { r.Error = "not a readable HS2 coordinate card: " + Path.GetFileName(cardPath); return r; }
            r.Container = res.Container;
            if (settings != null && services?.Catalog != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                sw.Restart();
                try { r.Thumbnail = MakerThumbnails.Coordinate(r.Container, services, settings, r.BuildFindings, out r.ThumbnailInfo); }
                catch (Exception e) { r.BuildFindings.Warning("thumbnail.failed", "", "coordinate thumbnail not rendered: " + e.GetType().Name + ": " + e.Message); }
                r.ThumbnailMs = sw.Elapsed.TotalMilliseconds;
            }
            var o = AppSaveOptions.Complete(options, services);
            o.Thumbnail = r.Thumbnail != null ? ThumbnailMode.Render : ThumbnailMode.Placeholder;
            if (r.Thumbnail != null) { var png = r.Thumbnail; o.Renderer = () => png; }
            r.Save = r.Container.Save(outPath, o);
            return r;
        }
    }
}
