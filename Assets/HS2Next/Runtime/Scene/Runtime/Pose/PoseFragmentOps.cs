// HS2Next pose files: scene fragments of kind hs2next.pose (K15; Phase 3.4 package B on
// package P's PoseContainer / PoseDocument).
//   Save  the node's model state of the listed parts (anime, hands, fk, ik, correction, face, look); missing FK values of
//         applied groups and missing IK targets are filled from the realized pose (the capture sources) in the file only
//         (the scene is not modified, no history entry); thumbnail: the runtime's view renderer, centred 5:7 crop; the
//         app's save options (AppSaveOptions: app.version, named requires)
//   Load  one transaction "Load pose" setting the listed parts (default: every part in the file), then the FK capture
//         rule for groups the load leaves applied without values; pose.sexMismatch (info) as for HS2 files
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The outcome of <see cref="PoseFragmentOps.Save"/>.</summary>
    public sealed class PoseFragmentSaveResult
    {
        /// <summary>Null on success.</summary>
        public string Error;
        /// <summary>One of CommandCodes when Error is set (bad_args, not_found, wrong_kind, unsupported).</summary>
        public string Code;
        public bool Ok => Error == null;
        public string Path;
        public long Bytes;
        /// <summary>The parts written, in file order.</summary>
        public IReadOnlyList<string> Parts = new string[0];
        /// <summary>Values filled from the realized pose in the file only: FK keys and IK target names.</summary>
        public IReadOnlyList<string> FilledFk = new string[0], FilledIk = new string[0];
        public FindingList Findings = new FindingList();
        public override string ToString() => Ok ? $"{Path}: {Bytes} bytes, parts {string.Join(",", Parts)}" : "error " + Code + ": " + Error;
    }

    /// <summary>The outcome of <see cref="PoseFragmentOps.Load"/>.</summary>
    public sealed class PoseFragmentLoadResult
    {
        public string Error;
        public string Code;
        public bool Applied;
        public CommandResult Command;
        /// <summary>The parts set, in file order.</summary>
        public IReadOnlyList<string> Parts = new string[0];
        /// <summary>FK keys added by the capture rule.</summary>
        public IReadOnlyList<string> Captured = new string[0];
        public FindingList Findings = new FindingList();
        public override string ToString() => Error != null ? "error " + Code + ": " + Error : $"applied {Applied}, parts {string.Join(",", Parts)}";
    }

    public static class PoseFragmentOps
    {
        /// <summary>The part names in file order.</summary>
        public static readonly string[] AllParts = { "anime", "hands", "fk", "ik", "correction", "face", "look" };

        static PoseFragmentSaveResult SaveError(string code, string msg) => new PoseFragmentSaveResult { Code = code, Error = msg };
        static PoseFragmentLoadResult LoadError(string code, string msg) => new PoseFragmentLoadResult { Code = code, Error = msg };

        /// <summary>The known parts of <paramref name="parts"/> (ignoring case) in file order; null for null; <paramref name="bad"/> = the first unknown name.</summary>
        public static List<string> NormaliseParts(IEnumerable<string> parts, out string bad)
        {
            bad = null;
            if (parts == null) return null;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in parts)
            {
                var k = Array.Find(AllParts, x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
                if (k == null) { bad = p ?? "null"; return null; }
                set.Add(k);
            }
            return AllParts.Where(set.Contains).ToList();
        }

        static bool FkFlag(FkGroups g, FkGroup x)
        {
            switch (x)
            {
                case FkGroup.Hair: return g.Hair;
                case FkGroup.Neck: return g.Neck;
                case FkGroup.Breast: return g.Breast;
                case FkGroup.Body: return g.Body;
                case FkGroup.RightHand: return g.RightHand;
                case FkGroup.LeftHand: return g.LeftHand;
                default: return g.Skirt;
            }
        }

        static List<FkGroup> FlaggedGroups(CharaFkState fk)
        {
            var groups = new List<FkGroup>();
            if (fk?.Groups == null) return groups;
            for (int g = 0; g < FkGroupInfo.Count; g++) if (FkFlag(fk.Groups, (FkGroup)g)) groups.Add((FkGroup)g);
            return groups;
        }

        /// <summary>
        /// Writes the node's pose as an hs2next.pose container at <paramref name="path"/> (the caller resolves the library
        /// path; `.hs2npose` is not added here). <paramref name="parts"/> null = all. Never changes the scene. Thumbnail:
        /// <paramref name="options"/> null = the runtime's thumbnail renderer cropped to 5:7 when there is one and a graphics
        /// device, else the placeholder.
        /// </summary>
        public static PoseFragmentSaveResult Save(SceneRuntime rt, string nodeId, string path, string name, IReadOnlyList<string> parts = null, bool overwrite = false, SaveOptions options = null)
        {
            if (rt == null) throw new ArgumentNullException(nameof(rt));
            var err = IkOps.NodeCheck(rt, nodeId, out var st);
            if (err != null) return SaveError(err.Code, err.Message);
            if (string.IsNullOrEmpty(path)) return SaveError(CommandCodes.BadArgs, "no path");
            var list = NormaliseParts(parts, out var bad);
            if (bad != null) return SaveError(CommandCodes.BadArgs, "unknown pose part " + bad);
            list = list ?? AllParts.ToList();
            if (list.Count == 0) return SaveError(CommandCodes.BadArgs, "no parts");
            if (!overwrite && File.Exists(path)) return SaveError(CommandCodes.BadArgs, "file exists: " + System.IO.Path.GetFileName(path));
            rt.ApplyNow();
            var r = rt.FindChara(nodeId);
            int sex = r != null && r.Sex >= 0 ? r.Sex : 1;
            var doc = PoseDocument.FromNode(st, name ?? "", sex, list);
            var res = new PoseFragmentSaveResult { Path = path, Parts = doc.Parts.ToList() };
            // missing values filled from the realized pose, in the file only
            if (doc.Fk != null && doc.Fk.Enabled)
            {
                var groups = FlaggedGroups(doc.Fk);
                var add = groups.Count > 0 ? new FkOps(rt).MissingValueSets(nodeId, groups, doc.Fk.Bones) : null;
                if (add != null && add.Count > 0)
                {
                    foreach (var kv in add) doc.Fk.Bones[kv.Key] = kv.Value;
                    res.FilledFk = add.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                }
            }
            if (doc.Ik != null)
            {
                var add = new IkOps(rt).Missing(nodeId, st, Enumerable.Range(0, IkTargets.Count));
                foreach (var kv in add) doc.Ik.Targets[kv.Key] = kv.Value;
                res.FilledIk = add.Keys.OrderBy(i => i).Select(i => IkTargets.Names[i]).ToList();
            }
            var o = AppSaveOptions.Complete(options ?? DefaultSaveOptions(rt), rt.Services);   // app.version, named requires
            var c = new PoseContainer { Pose = doc };
            var sr = c.Save(path, o);
            res.Findings.AddRange(sr.Findings);
            if (!sr.Ok) { res.Code = CommandCodes.BadArgs; res.Error = "save failed: " + sr.Error.Message; return res; }
            res.Bytes = sr.Size;
            return res;
        }

        static SaveOptions DefaultSaveOptions(SceneRuntime rt)
        {
            var renderer = rt.Ops.ThumbnailRenderer;
            if (renderer != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                return new SaveOptions { Thumbnail = ThumbnailMode.Render, Renderer = () => CropPng(renderer(), 5, 7) };
            return new SaveOptions { Thumbnail = ThumbnailMode.Placeholder };
        }

        /// <summary>The centred crop of a PNG to the aspect <paramref name="aw"/>:<paramref name="ah"/> (null in, null out).</summary>
        public static byte[] CropPng(byte[] png, int aw, int ah)
        {
            if (png == null) return null;
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D dst = null;
            try
            {
                if (!src.LoadImage(png)) return png;
                int w = src.width, h = src.height, cw = w, ch = h;
                if ((long)w * ah > (long)h * aw) cw = Math.Max(1, (int)Math.Round(h * (double)aw / ah));
                else ch = Math.Max(1, (int)Math.Round(w * (double)ah / aw));
                if (cw == w && ch == h) return png;
                dst = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                dst.SetPixels(src.GetPixels((w - cw) / 2, (h - ch) / 2, cw, ch));
                dst.Apply(false);
                return dst.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(src);
                if (dst) UnityEngine.Object.DestroyImmediate(dst);
            }
        }

        /// <summary>
        /// Loads an hs2next.pose container into the node in one "Load pose" entry; <paramref name="parts"/> null = every part
        /// in the file (a listed part the file lacks is skipped). The FK capture rule runs for the groups the load leaves
        /// applied without values; a pose of the other sex gets pose.sexMismatch (info). The modes are the file's (the
        /// exclusive preset does not apply to file data).
        /// </summary>
        public static PoseFragmentLoadResult Load(SceneRuntime rt, string nodeId, string path, IReadOnlyList<string> parts = null, ExecuteOptions eo = default)
        {
            if (rt == null) throw new ArgumentNullException(nameof(rt));
            var err = IkOps.NodeCheck(rt, nodeId, out var st);
            if (err != null) return LoadError(err.Code, err.Message);
            var list = NormaliseParts(parts, out var bad);
            if (bad != null) return LoadError(CommandCodes.BadArgs, "unknown pose part " + bad);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return LoadError(CommandCodes.NotFound, "no file " + path);
            var lr = PoseContainer.Load(path);
            var res = new PoseFragmentLoadResult();
            res.Findings.AddRange(lr.Findings);
            if (!lr.Ok) { res.Code = CommandCodes.BadArgs; res.Error = "pose file: " + lr.Error.Message; return res; }
            var doc = lr.Value.Pose;
            rt.ApplyNow();
            var r = rt.FindChara(nodeId);
            if (r != null && r.Sex >= 0 && doc.Sex != r.Sex)
                res.Findings.Info("pose.sexMismatch", "", $"pose for {(doc.Sex == 0 ? "male" : "female")} loaded into a {(r.Sex == 0 ? "male" : "female")} character");
            var tmp = new CharaNodeState();
            var done = doc.CopyTo(tmp, list);
            res.Parts = done;
            if (done.Count == 0) { res.Command = CommandResult.NoOp(null); res.Applied = true; return res; }
            // FK capture for the groups the load leaves applied without values
            bool fkPart = done.Contains(PoseDocument.PartFk);
            var fk = fkPart ? tmp.Fk : st.Fk;
            if (fk.Enabled)
            {
                var groups = FlaggedGroups(fk);
                var add = groups.Count > 0 ? new FkOps(rt).MissingValueSets(nodeId, groups, fk.Bones) : null;
                if (add != null && add.Count > 0)
                {
                    if (!fkPart) { tmp.Fk = PoseDocument.Copy(st.Fk); fk = tmp.Fk; }
                    foreach (var kv in add) fk.Bones[kv.Key] = kv.Value;
                    res.Captured = add.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                }
            }
            bool setFk = fkPart || res.Captured.Count > 0;
            if (eo.Label == null) eo.Label = "Load pose";
            CommandResult cr = null;
            using (var tx = rt.Session.Commands.BeginOrJoin(eo.Label, eo))
            {
                if (!tx.IsOpen) return LoadError(tx.Failure?.Code ?? CommandCodes.Busy, tx.Failure?.Message ?? "busy");
                void Set(string p, object v) { if (cr == null || cr.Ok) cr = tx.Set(nodeId, p, v); }
                if (done.Contains(PoseDocument.PartAnime)) Set("chara.anime", tmp.Anime);
                if (done.Contains(PoseDocument.PartHands)) Set("chara.hands", tmp.Hands);
                if (setFk) Set("chara.fk", tmp.Fk);
                if (done.Contains(PoseDocument.PartIk)) Set("chara.ik", tmp.Ik);
                if (done.Contains(PoseDocument.PartCorrection)) Set("chara.correction", tmp.Correction);
                if (done.Contains(PoseDocument.PartFace)) Set("chara.face", tmp.Face);
                if (done.Contains(PoseDocument.PartLook)) Set("chara.look", tmp.Look);
                if (cr != null && !cr.Ok)
                {
                    tx.Cancel();
                    rt.ApplyNow();
                    res.Code = cr.Code; res.Error = cr.Message; res.Command = cr;
                    return res;
                }
                cr = tx.Commit();
            }
            rt.ApplyNow();
            res.Command = cr;
            res.Applied = cr != null && cr.Ok;
            return res;
        }

        /// <summary>True when the file at <paramref name="path"/> is an hs2next.pose container (by its manifest).</summary>
        public static bool IsFragment(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                var m = ContainerReader.Read(path, null, n => n == "manifest.json").ReadManifest();
                return m != null && m.Format == SaveFormats.Pose;
            }
            catch (Exception) { return false; }
        }
    }
}
