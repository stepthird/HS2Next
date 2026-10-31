// Standalone import of an HS2 card to a character file: importer → container; with a
// graphics device the imported documents are built off the scene at the origin, shot in the Studio default pose with
// HS2's coordinate camera, the build's renderers alone on a free isolation layer (MakerThumbnails.Character; save format
// "import or headless save"), the build is destroyed and the file is saved with that thumbnail; without one (or with
// render settings null) the placeholder is written. The caller passes the save options the app completes
// (AppSaveOptions: app.version, named requires).
using System;
using System.Diagnostics;
using System.IO;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Chara.Documents
{
    public sealed class CharaFileImportResult
    {
        public CharaContainer Container;
        public SaveResult Save;
        /// <summary>The rendered thumbnail (PNG 504 × 704), or null when the placeholder was written.</summary>
        public byte[] Thumbnail;
        public readonly FindingList ImportFindings = new FindingList(), BuildFindings = new FindingList();
        public string Error;
        public double ImportMs, BuildMs, CaptureMs;
        /// <summary>What the thumbnail capture did (isolation layer, renderers), or null.</summary>
        public CharaThumbnailInfo ThumbnailInfo;
        public bool Ok => Error == null && Save != null && Save.Ok;
        public override string ToString() => Ok
            ? $"ok: {Save.Size} bytes, thumbnail {(Thumbnail != null ? "rendered" : "placeholder")}, import {ImportMs:F0} ms, build {BuildMs:F0} ms, capture {CaptureMs:F0} ms"
            : "failed: " + (Error ?? Save?.Error?.Message);
    }

    public static class CharaFileImport
    {
        /// <summary>
        /// Imports <paramref name="cardPath"/> and saves it to <paramref name="outPath"/>. <paramref name="render"/>: thumbnail
        /// settings (e.g. the URP pipeline asset); null, or no graphics device = placeholder thumbnail.
        /// </summary>
        public static CharaFileImportResult ImportToFile(ICharaImporter importer, string cardPath, string outPath, ContentCatalog catalog, ThumbnailSettings render, SaveOptions options = null)
        {
            if (importer == null) throw new ArgumentNullException(nameof(importer));
            var r = new CharaFileImportResult();
            byte[] bytes;
            try { bytes = File.ReadAllBytes(cardPath); }
            catch (Exception e) { r.Error = "card not readable: " + e.Message; return r; }
            var src = new CardAppearanceSource { FullPath = cardPath, Sha256 = ResStore.Sha256Hex(bytes), ReadBytes = () => bytes, Name = Path.GetFileNameWithoutExtension(cardPath) };
            var sw = Stopwatch.StartNew();
            try { r.Container = importer.Import(src, r.ImportFindings); }
            catch (Exception e) { r.Error = "import threw " + e.GetType().Name + ": " + e.Message; return r; }
            r.ImportMs = sw.Elapsed.TotalMilliseconds;
            if (r.Container == null) { r.Error = "the importer returned nothing"; return r; }
            if (render != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                r.Thumbnail = RenderThumbnail(r.Container, catalog, render, r);
            var o = options ?? new SaveOptions();
            o.Thumbnail = r.Thumbnail != null ? ThumbnailMode.Render : ThumbnailMode.Placeholder;
            if (r.Thumbnail != null) { var png = r.Thumbnail; o.Renderer = () => png; }
            r.Save = r.Container.Save(outPath, o);
            return r;
        }

        /// <summary>The isolated front shot of the container's documents built off the scene (MakerThumbnails.Character); null on
        /// failure (a finding in <paramref name="r"/>'s build findings says why).</summary>
        public static byte[] RenderThumbnail(CharaContainer c, ContentCatalog catalog, ThumbnailSettings settings, CharaFileImportResult r = null)
        {
            var findings = r?.BuildFindings;
            try
            {
                var services = new RuntimeServices { Catalog = catalog, Db = catalog?.StudioLists };
                var png = MakerThumbnails.Character(c, services, settings, findings, out var info, out double buildMs, out double captureMs);
                if (r != null) { r.BuildMs = buildMs; r.CaptureMs = captureMs; r.ThumbnailInfo = info; }
                return png;
            }
            catch (Exception e)
            {
                findings?.Warning("thumbnail.failed", "", "import thumbnail not rendered: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }
    }
}
