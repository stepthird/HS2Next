// StudioOps, the Maker's file operations (Phase 4.6), none of them recorded:
//   RenderCharaThumbnail  the texture a character save would encode (the File node's preview): Front = the Studio
//                         default pose and HS2's coordinate camera relative to the node, View = the display camera and the
//                         character as shown (Front without a display camera, warning thumbnail.noView); both isolated.
//   SaveCoordinate        the node's worn coordinate as a .hs2ncoord file: the node's documents as SaveChara collects them
//                         (the stored subtree, else the import of its card) → the coordinate with its ext data and the
//                         resources it references → `coord.name` = the typed name in the file only → the coordinate
//                         thumbnail (MakerThumbnails.Coordinate) when asked and a graphics device exists, else the
//                         placeholder with a warning → the save with the app's options (AppSaveOptions). `import` stays
//                         null (an HS2Next-made coordinate); the node's documents are untouched.
// SaveChara (StudioOps.CharaExtra.cs) shares the document collection and the thumbnail render.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The Value of <see cref="StudioOps.RenderCharaThumbnail"/>.</summary>
    public sealed class CharaThumbnailRender
    {
        /// <summary>The 504 × 704 RGB24 texture (no mipmaps; the caller destroys it), or null when none was rendered (the
        /// result's warnings say why).</summary>
        public Texture2D Texture;
        /// <summary>The framing rendered (View falls back to Front without a display camera).</summary>
        public CharaThumbnailFraming Framing;
        public double Milliseconds;
    }

    /// <summary>The Value of <see cref="StudioOps.SaveCoordinate"/>.</summary>
    public sealed class CoordSaveResult
    {
        public string Path;
        public long Bytes;
        /// <summary>"render" or "placeholder".</summary>
        public string Thumbnail;
        /// <summary>The coordinate's sex (0 male, 1 female) and the name written.</summary>
        public int Sex;
        public string Name;
    }

    public sealed partial class StudioOps
    {
        /// <summary>Settings of the thumbnails the ops render (null = the defaults: grey 128, the active pipeline); tests set
        /// the pipeline asset of edit-mode runs.</summary>
        public ThumbnailSettings ThumbnailRenderSettings;

        /// <summary>
        /// The texture a save of character <paramref name="id"/> in <paramref name="framing"/> would encode (504 × 704 RGB24;
        /// the caller destroys it). Value = <see cref="CharaThumbnailRender"/> whose Texture is null, with the reason as a
        /// warning, when the character is not built, no graphics device exists or no isolation layer is free. Not recorded.
        /// </summary>
        public OpResult RenderCharaThumbnail(string id, CharaThumbnailFraming framing)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            var warnings = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tex = RenderThumbnailTexture(nodes[0].Id, framing, warnings, out var used);
            var ok = OpResult.Success();
            ok.Warnings.AddRange(warnings);
            ok.Value = new CharaThumbnailRender { Texture = tex, Framing = used, Milliseconds = sw.Elapsed.TotalMilliseconds };
            return ok;
        }

        /// <summary>The thumbnail texture of a character node, or null with the reason in <paramref name="warnings"/>.</summary>
        Texture2D RenderThumbnailTexture(string id, CharaThumbnailFraming framing, List<string> warnings, out CharaThumbnailFraming used)
        {
            used = framing;
            rt.ApplyNow();
            var r = rt.FindChara(id);
            bool built = r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root;
            if (!built) { warnings.Add("thumbnail: the character is not built; the placeholder is written"); return null; }
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { warnings.Add("thumbnail: no graphics device; the placeholder is written"); return null; }
            Camera view = null;
            if (framing == CharaThumbnailFraming.View)
            {
                view = rt.GetPart<SceneViewCamera>()?.Camera;
                if (!view)
                {
                    warnings.Add("thumbnail.noView: no display camera; the front framing is used");
                    used = CharaThumbnailFraming.Front;
                }
            }
            var tex = CharaThumbnail.CaptureTexture(r, used, view, ThumbnailRenderSettings, out _);
            if (tex == null) warnings.Add("thumbnail.noFreeLayer: no isolation layer is free; the placeholder is written");
            return tex;
        }

        /// <summary>
        /// The documents a save of character node <paramref name="n"/> writes: a clone of its stored subtree with the resources
        /// it references, else the import of its card (the runtime's importer); null with <paramref name="fail"/>.
        /// </summary>
        CharaContainer ContainerToSave(TreeNode n, out OpResult fail)
        {
            fail = null;
            var stored = n.Appearance?.Stored;
            if (stored != null)
            {
                // the node's current subtree (what ExtractCharacter returns for an up-to-date scene file)
                var container = new CharaContainer { Subtree = stored.Clone() };
                container.Subtree.ManifestCharaUnknown = null;
                container.Res.CopyFrom(Model.File.Res, container.Subtree.ReferencedResources());
                return container;
            }
            if (n.Appearance?.Card != null)
            {
                var importer = ImportSetup.ImporterOf(Services);
                if (importer == null) { fail = OpResult.Unsupported("this card-backed character cannot be saved: no importer is available"); return null; }
                CharaContainer imported;
                try { imported = importer.Import(n.Appearance.Card, new FindingList()); }
                catch (Exception e) { fail = OpResult.Fail(CommandCodes.BadArgs, "import of the card failed: " + e.GetType().Name + ": " + e.Message); return null; }
                if (imported == null) fail = OpResult.Fail(CommandCodes.BadArgs, "import of the card failed");
                return imported;
            }
            fail = OpResult.Unavailable("character " + n.Id + " has no appearance to save", "key", "Missing");
            return null;
        }

        /// <summary>
        /// Saves the worn coordinate of character <paramref name="id"/> as a .hs2ncoord file at <paramref name="fullPath"/> (not
        /// recorded): `coord.name` = <paramref name="name"/> when not null (in the file only), the coordinate thumbnail when
        /// <paramref name="renderThumb"/> and a graphics device exists (else the placeholder, with a warning), the app's save
        /// options with <paramref name="clock"/> (null = now). Value = <see cref="CoordSaveResult"/>. The path is the caller's.
        /// </summary>
        public OpResult SaveCoordinate(string id, string fullPath, string name, bool renderThumb = true, Func<DateTime> clock = null)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            if (string.IsNullOrEmpty(fullPath)) return OpResult.BadArgs("path", "a file path");
            var n = nodes[0];
            var warnings = new List<string>();
            var chara = ContainerToSave(n, out fail);
            if (chara == null) return fail;
            var coord = chara.ExtractCoordinate();
            if (name != null) coord.Coord.Name = name;

            byte[] png = null;
            if (renderThumb)
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) warnings.Add("thumbnail: no graphics device; the placeholder is written");
                else
                {
                    var found = new FindingList();
                    try { png = MakerThumbnails.Coordinate(coord, Services, ThumbnailRenderSettings, found); }
                    catch (Exception e) { found.Warning("thumbnail.failed", "", e.GetType().Name + ": " + e.Message); }
                    foreach (var f in found)
                        if (f.Severity != FindingSeverity.Info) warnings.Add(f.Code + ": " + f.Message);
                    if (png == null) warnings.Add("thumbnail: the coordinate thumbnail was not rendered; the placeholder is written");
                }
            }
            var opts = png != null
                ? new SaveOptions { Thumbnail = ThumbnailMode.Render, Renderer = () => png, Clock = clock }
                : new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = clock };
            var res = coord.Save(fullPath, AppSaveOptions.Complete(opts, Services));
            if (!res.Ok)
            {
                var e = OpResult.Fail(CommandCodes.BadArgs, "save failed: " + res.Error.Message);
                e.Extra = new Dictionary<string, object> { ["code"] = res.Error.Code.ToString() };
                e.Warnings.AddRange(warnings);
                return e;
            }
            var ok = OpResult.Success();
            ok.Warnings.AddRange(warnings);
            ok.Value = new CoordSaveResult { Path = fullPath, Bytes = res.Size, Thumbnail = png != null ? "render" : "placeholder", Sex = coord.Coord.Sex, Name = coord.Coord.Name };
            return ok;
        }
    }
}
