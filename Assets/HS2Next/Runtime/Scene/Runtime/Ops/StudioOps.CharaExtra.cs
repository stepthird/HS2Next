// StudioOps, character area additions for the inspector (Phase 3.5, package I's request): the FK hips offset / position
// (fk.set's hips part), the look target typed in local space, and saving a character file (chara.save's write logic: the
// handler resolves the path and calls SaveChara; since Phase 4.6 with the front / view framings and the app's save
// options, StudioOps.File.cs).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The Value of <see cref="StudioOps.SaveChara"/>.</summary>
    public sealed class CharaSaveResult
    {
        public string Path;
        public long Bytes;
        /// <summary>"render" or "placeholder".</summary>
        public string Thumbnail;
        /// <summary>The framing rendered ("front" or "view"; View falls back to Front without a display camera), or null for the placeholder.</summary>
        public string Framing;
    }

    public sealed partial class StudioOps
    {
        /// <summary>
        /// The FK hips position and offset of every character in one entry "FK bones" (fk.set's hips members): `chara.fk.hipsPosition`
        /// is written when <paramref name="setPosition"/> (null clears it), `chara.fk.hipsOffset` when <paramref name="offset"/>
        /// is given; both finite. Nothing when neither is given.
        /// </summary>
        public OpResult SetFkHips(IEnumerable<string> ids, float? offset, bool setPosition, Vec3? position, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (setPosition && position != null && !(Finite(position.Value.X) && Finite(position.Value.Y) && Finite(position.Value.Z)))
                return OpResult.BadArgs("hipsPosition", "finite numbers");
            if (offset != null && !Finite(offset.Value)) return OpResult.BadArgs("hipsOffset", "a finite number");
            var plan = new List<(string, object)>();
            if (setPosition) plan.Add(("chara.fk.hipsPosition", position));
            if (offset != null) plan.Add(("chara.fk.hipsOffset", offset.Value));
            if (plan.Count == 0) return OpResult.NothingToDo();
            return SetManyNoPre(Ids(nodes), plan, Labels.FkBones, o, p => p.EndsWith("Position", StringComparison.Ordinal) ? "hipsPosition" : "hipsOffset");
        }

        /// <summary>The look target of every character as the stored local value (`chara.look.target`), one entry "Look target"; a
        /// document field, so characters that are not built are fine.</summary>
        public OpResult SetLookTargetLocal(IEnumerable<string> ids, Vec3 local, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (!(Finite(local.X) && Finite(local.Y) && Finite(local.Z))) return OpResult.BadArgs("local", "finite numbers");
            return SetManyNoPre(Ids(nodes), new List<(string, object)> { ("chara.look.target", local) }, Labels.LookTarget, o, _ => "local");
        }

        /// <summary>
        /// Saves character <paramref name="id"/> as a .hs2nchara file at <paramref name="fullPath"/> (not recorded): the node's stored
        /// subtree (its current documents), else the import of its card; the thumbnail is rendered in <paramref name="framing"/>
        /// (Front: the Studio default pose and HS2's coordinate camera relative to the node; View: the display camera, the
        /// character as shown; both isolated) when <paramref name="renderThumb"/>, the character is built and a graphics device
        /// exists, else the placeholder (with a warning saying why). The options are completed by AppSaveOptions (app.version,
        /// named requires) with <paramref name="clock"/> (null = now). Value = <see cref="CharaSaveResult"/>. The path is the
        /// caller's (the bridge resolves it under &lt;UserData&gt;/chara and checks overwrite).
        /// </summary>
        public OpResult SaveChara(string id, string fullPath, bool renderThumb = true, CharaThumbnailFraming framing = CharaThumbnailFraming.Front, Func<DateTime> clock = null)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            var n = nodes[0];
            var warnings = new List<string>();
            var container = ContainerToSave(n, out fail);
            if (container == null) return fail;

            byte[] png = null;
            string used = "placeholder", usedFraming = null;
            if (renderThumb)
            {
                var tex = RenderThumbnailTexture(n.Id, framing, warnings, out var f);
                if (tex != null)
                {
                    try { png = tex.EncodeToPNG(); }
                    finally { UnityEngine.Object.DestroyImmediate(tex); }
                    used = "render";
                    usedFraming = f == CharaThumbnailFraming.View ? "view" : "front";
                }
            }
            var opts = png != null
                ? new SaveOptions { Thumbnail = ThumbnailMode.Render, Renderer = () => png, Clock = clock }
                : new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = clock };
            var res = container.Save(fullPath, AppSaveOptions.Complete(opts, Services));
            if (!res.Ok)
            {
                var e = OpResult.Fail(CommandCodes.BadArgs, "save failed: " + res.Error.Message);
                e.Extra = new Dictionary<string, object> { ["code"] = res.Error.Code.ToString() };
                e.Warnings.AddRange(warnings);
                return e;
            }
            var ok = OpResult.Success();
            ok.Warnings.AddRange(warnings);
            ok.Value = new CharaSaveResult { Path = fullPath, Bytes = res.Size, Thumbnail = used, Framing = usedFraming };
            return ok;
        }
    }
}
