// Thumbnails of the Maker's file operations that are built off the scene (Phase 4.6):
//   Coordinate  the coordinate thumbnail (decision D5) of a coordinate save and of a standalone coordinate-card
//               import: a fresh character of `coord.sex` (every member of its documents at the "absent" value) wearing the
//               coordinate, built under a temporary holder at the origin facing +Z, hair objects hidden (hair-type
//               accessories are accessories and stay), the Maker pose list row 0 of the sex (custom_pose_f 501 /
//               custom_pose_m 500: the controller `MainData` of bundle `MainAB`, state `Clip`) played at normalized time 0
//               with speed 0 (the row's IK data is not applied; no row / controller / state → the rest pose and info
//               `thumbnail.poseFallback`), the Studio default expression and look (StudioDefaultPoseScope: eyes open, eyes
//               and neck forward, hands 0, dynamic bones and bust off) settled 30 steps of 1⁄30 s, every clothes kind on and
//               every accessory shown (no scene state exists off the scene), HS2's coordinate camera of the sex, chara light,
//               grey 128, SMAA, 504 × 704, the build's renderers alone on a free isolation layer (other lights and fog off).
//               Everything is restored and the build destroyed in `finally` (render textures, owned materials and meshes,
//               the compositing passes); the catalog's bundles stay cached. Two renders of one coordinate give the same bytes.
//   Character   the front shot of a character file built off the scene (standalone imports: CharaFileImport,
//               ImportThumbnails): the Studio default pose, isolated as above, HS2's coordinate camera of the sex.
//   OffSceneChara  the build both use.
// Needs a graphics device; main thread only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What a coordinate thumbnail build did (tests, measurements).</summary>
    public sealed class CoordThumbnailInfo
    {
        public int Sex = -1;
        /// <summary>The Maker pose row played (`MainAB:MainData:Clip`), or null when the rest pose was used.</summary>
        public string PoseRow;
        /// <summary>Why the rest pose was used (`thumbnail.poseFallback`), or null.</summary>
        public string PoseFallback;
        /// <summary>The largest rotation of a limb bone away from the rest pose (degrees) and its name.</summary>
        public double MaxLimbDeltaDeg;
        public string MaxLimbBone;
        public int Layer = -1, Renderers, LightsOff;
        /// <summary>Hair objects of the build; active ones at the capture (0 expected); active ones after the capture.</summary>
        public int HairObjects, HairActiveAtCapture = -1, HairActiveAfter = -1;
        public double BuildMs, PoseMs, CaptureMs, TotalMs;
        /// <summary>Open bundles of the catalog after the build (diagnostics of long loops).</summary>
        public int OpenBundles;
        public string Error;
        public override string ToString() =>
            $"sex {Sex}, pose {(PoseRow ?? "rest (" + PoseFallback + ")")}, limb delta {MaxLimbDeltaDeg:F1}° ({MaxLimbBone}), layer {Layer}, renderers {Renderers}, " +
            $"hair {HairObjects} (at capture {HairActiveAtCapture}, after {HairActiveAfter}), build {BuildMs:F0} ms, pose {PoseMs:F0} ms, capture {CaptureMs:F0} ms, total {TotalMs:F0} ms" +
            (Error != null ? ", error " + Error : "");
    }

    public static class MakerThumbnails
    {
        /// <summary>The Maker pose lists (list categories custom_pose_m / custom_pose_f).</summary>
        public const int PoseListMale = 500, PoseListFemale = 501;

        static readonly string[] LimbBones =
        {
            "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_ArmLow01_L", "cf_J_ArmLow01_R", "cf_J_Hand_L", "cf_J_Hand_R",
            "cf_J_LegUp00_L", "cf_J_LegUp00_R", "cf_J_LegLow01_L", "cf_J_LegLow01_R", "cf_J_Foot01_L", "cf_J_Foot01_R",
            "cf_J_Shoulder_L", "cf_J_Shoulder_R",
        };

        /// <summary>The coordinate thumbnail of <paramref name="src"/> (504 × 704 RGB PNG), or null when it cannot be rendered
        /// (no graphics device, no free isolation layer, a failed build: a finding says why).</summary>
        public static byte[] Coordinate(CoordContainer src, RuntimeServices services, ThumbnailSettings settings, FindingList findings) =>
            Coordinate(src, services, settings, findings, out _);

        public static byte[] Coordinate(CoordContainer src, RuntimeServices services, ThumbnailSettings settings, FindingList findings, out CoordThumbnailInfo info)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (services?.Catalog == null) throw new ArgumentException("the services have no catalog", nameof(services));
            info = new CoordThumbnailInfo { Sex = src.Coord.Sex == 0 ? 0 : 1 };
            var total = Stopwatch.StartNew();
            try { return Render(src, services, settings, findings, info); }
            finally
            {
                info.TotalMs = total.Elapsed.TotalMilliseconds;
                info.OpenBundles = services.Catalog.OpenBundleCount;
            }
        }

        static byte[] Render(CoordContainer src, RuntimeServices services, ThumbnailSettings settings, FindingList findings, CoordThumbnailInfo info)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                info.Error = "no graphics device";
                findings?.Warning("thumbnail.noDevice", "", "no graphics device: the coordinate thumbnail is not rendered");
                return null;
            }
            // the scan comes before the build: the build's own objects are not counted
            int layer = CharaThumbnail.FreeLayerInLoadedScenes();
            if (layer < 0)
            {
                info.Error = "no free layer";
                findings?.Error("thumbnail.noFreeLayer", "", $"no layer in {CharaThumbnail.FirstIsolationLayer}..{CharaThumbnail.LastIsolationLayer} is free for the coordinate thumbnail");
                return null;
            }
            info.Layer = layer;
            var subtree = new CharaSubtree
            {
                Chara = new CharaDocument(),
                Coord = CharaSubtree.CloneCoord(src.Coord),
                CoordExt = src.CoordExt.Clone(),
            };
            subtree.Chara.Profile.Sex = info.Sex;
            var sw = Stopwatch.StartNew();
            using (var b = OffSceneChara.Build(subtree, src.Res, services, findings, "hs2next_coordinate_thumbnail"))
            {
                info.BuildMs = sw.Elapsed.TotalMilliseconds;
                if (!b.Ok)
                {
                    info.Error = b.Error;
                    findings?.Warning("thumbnail.failed", "", "coordinate thumbnail not rendered: " + b.Error);
                    return null;
                }
                var ctx = b.Context;
                sw.Restart();
                using (StudioDefaultPoseScope.Enter(b.Root, services.Db))
                {
                    MakerPose(b.Root, ctx, services.Catalog, info, findings);
                    info.PoseMs = sw.Elapsed.TotalMilliseconds;
                    var hidden = new List<GameObject>();
                    foreach (var h in ctx.Hair)
                        if (h && h.activeSelf) { h.SetActive(false); hidden.Add(h); }
                    info.HairObjects = hidden.Count;
                    try
                    {
                        info.HairActiveAtCapture = ActiveCount(ctx.Hair);
                        var thumb = new CharaThumbnailInfo();
                        using (CharaThumbnail.Isolate(b.Root.GetComponentsInChildren<Renderer>(true), layer, thumb))
                        {
                            info.Renderers = thumb.Renderers;
                            info.LightsOff = thumb.LightsOff;
                            sw.Restart();
                            var png = ThumbnailCapture.Capture(info.Sex == 0 ? ThumbnailCapture.CoordCameraMale : ThumbnailCapture.CoordCameraFemale,
                                                               Quaternion.Euler(ThumbnailCapture.CoordCameraEuler), ThumbnailCapture.CropFov(ThumbnailCapture.Hs2Fov),
                                                               CharaThumbnail.ShotSettings(settings, layer));
                            info.CaptureMs = sw.Elapsed.TotalMilliseconds;
                            return png;
                        }
                    }
                    finally
                    {
                        foreach (var h in hidden) if (h) h.SetActive(true);
                        info.HairActiveAfter = ActiveCount(ctx.Hair);
                    }
                }
            }
        }

        /// <summary>
        /// The front shot of a character file built off the scene (standalone imports): the documents of <paramref name="c"/>
        /// built at the origin facing +Z, the Studio default pose, the build's renderers alone on a free isolation layer, HS2's
        /// coordinate camera of the sex, chara light, grey 128. 504 × 704 RGB PNG, or null (a finding says why).
        /// </summary>
        public static byte[] Character(CharaContainer c, RuntimeServices services, ThumbnailSettings settings, FindingList findings, out CharaThumbnailInfo info,
                                       out double buildMs, out double captureMs)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            if (services?.Catalog == null) throw new ArgumentException("the services have no catalog", nameof(services));
            info = new CharaThumbnailInfo { Framing = CharaThumbnailFraming.Front };
            buildMs = captureMs = 0;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                findings?.Warning("thumbnail.noDevice", "", "no graphics device: the character thumbnail is not rendered");
                return null;
            }
            int layer = CharaThumbnail.FreeLayerInLoadedScenes();
            if (layer < 0)
            {
                findings?.Error("thumbnail.noFreeLayer", "", $"no layer in {CharaThumbnail.FirstIsolationLayer}..{CharaThumbnail.LastIsolationLayer} is free for the character thumbnail");
                return null;
            }
            info.Layer = layer;
            var sw = Stopwatch.StartNew();
            using (var b = OffSceneChara.Build(c.Subtree, c.Res, services, findings, "hs2next_character_thumbnail"))
            {
                buildMs = sw.Elapsed.TotalMilliseconds;
                if (!b.Ok)
                {
                    findings?.Warning("thumbnail.failed", "", "character thumbnail not rendered: " + b.Error);
                    return null;
                }
                sw.Restart();
                using (StudioDefaultPoseScope.Enter(b.Root, services.Db))
                using (CharaThumbnail.Isolate(b.Root.GetComponentsInChildren<Renderer>(true), layer, info))
                {
                    var png = ThumbnailCapture.Capture(c.Chara.Profile.Sex == 0 ? ThumbnailCapture.CoordCameraMale : ThumbnailCapture.CoordCameraFemale,
                                                       Quaternion.Euler(ThumbnailCapture.CoordCameraEuler), ThumbnailCapture.CropFov(ThumbnailCapture.Hs2Fov),
                                                       CharaThumbnail.ShotSettings(settings, layer));
                    captureMs = sw.Elapsed.TotalMilliseconds;
                    info.Milliseconds = (long)(buildMs + captureMs);
                    return png;
                }
            }
        }

        static int ActiveCount(GameObject[] objects)
        {
            int n = 0;
            if (objects != null) foreach (var o in objects) if (o && o.activeSelf) n++;
            return n;
        }

        /// <summary>
        /// Plays the Maker pose list row 0 of the character's sex at normalized time 0 with speed 0 and settles the look; the
        /// rest pose (the built pose) with info `thumbnail.poseFallback` when the row, its controller or its state is missing.
        /// </summary>
        static void MakerPose(GameObject root, CharaContext ctx, ContentCatalog catalog, CoordThumbnailInfo info, FindingList findings)
        {
            var anim = CharaAnimation.Of(root);
            int category = info.Sex == 0 ? PoseListMale : PoseListFemale;
            var row = catalog.GetEntry(category, ContentId.Vanilla(0));
            string why = null;
            var rest = RestRotations(ctx);
            if (anim == null) why = "the character has no animation component";
            else if (row == null) why = $"list {category} has no row 0";
            else if (string.IsNullOrEmpty(row["MainAB"]) || string.IsNullOrEmpty(row["MainData"]) || string.IsNullOrEmpty(row["Clip"]))
                why = $"list {category} row 0 names no controller or state";
            else
            {
                var pose = new StudioAnime
                {
                    Key = row.Key, Name = row["Name"], Group = -1, Category = category,
                    BundlePath = row["MainAB"], FileName = row["MainData"], Clip = row["Clip"],
                };
                if (!anim.Load(pose, 0f)) why = $"controller {pose.BundlePath}:{pose.FileName} not loadable";
                else if (anim.Driver == null || !anim.Driver.HasState(0, Animator.StringToHash(pose.Clip)))
                {
                    why = $"state \"{pose.Clip}\" is not on layer 0 of {pose.BundlePath}:{pose.FileName}";
                    anim.Stop();
                }
                else
                {
                    anim.Speed = 0f;
                    info.PoseRow = pose.BundlePath + ":" + pose.FileName + ":" + pose.Clip;
                }
            }
            if (why != null)
            {
                info.PoseFallback = why;
                findings?.Info("thumbnail.poseFallback", "", "the Maker pose is not available (" + why + "); the rest pose is used");
            }
            var motion = root.GetComponent<CharaMotion>();
            if (motion != null)
                for (int i = 0; i < StudioDefaultPoseScope.SettleSteps; i++) motion.Step(StudioDefaultPoseScope.SettleDt);
            LimbDelta(ctx, rest, info);
        }

        static Dictionary<string, Quaternion> RestRotations(CharaContext ctx)
        {
            var d = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
            if (ctx?.BodyBones == null) return d;
            foreach (var n in LimbBones)
                if (ctx.BodyBones.TryGetValue(n, out var t) && t) d[n] = t.localRotation;
            return d;
        }

        static void LimbDelta(CharaContext ctx, Dictionary<string, Quaternion> rest, CoordThumbnailInfo info)
        {
            if (ctx?.BodyBones == null) return;
            foreach (var kv in rest)
            {
                if (!ctx.BodyBones.TryGetValue(kv.Key, out var t) || !t) continue;
                var a = kv.Value; var b = t.localRotation;
                double dot = Math.Min(1.0, Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w));
                double deg = 2.0 * Math.Acos(dot) * 180.0 / Math.PI;
                if (deg > info.MaxLimbDeltaDeg) { info.MaxLimbDeltaDeg = deg; info.MaxLimbBone = kv.Key; }
            }
        }
    }

    /// <summary>
    /// A character built off the scene from documents (thumbnails of saves and standalone imports): under a temporary holder
    /// at the origin facing +Z, by the scene builders' shared steps (stray roots moved under the character, one URP mapping).
    /// Dispose releases what a character realization releases (render textures, owned materials and meshes, the compositing
    /// passes) and destroys the holder.
    /// </summary>
    public sealed class OffSceneChara : IDisposable
    {
        public GameObject Holder;
        public CharaBuildResult Result;
        public string Error;
        public GameObject Root => Result?.Root;
        public CharaContext Context => Result?.Context;
        public bool Ok => Error == null && Result != null && Result.Ok;

        /// <summary>Builds <paramref name="subtree"/> (images in <paramref name="res"/>) with the services' catalog and extension
        /// options; never throws for a failed build (<see cref="Error"/>). Build findings go to <paramref name="findings"/>.</summary>
        public static OffSceneChara Build(CharaSubtree subtree, ResStore res, RuntimeServices services, FindingList findings, string name)
        {
            var b = new OffSceneChara();
            var sw = Stopwatch.StartNew();
            try
            {
                var input = CharaDocumentInput.From(subtree, res, services.Catalog, findings);
                b.Holder = new GameObject(name ?? "hs2next_offscene_chara");
                b.Holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                b.Result = new CharaBuildResult { Stored = subtree, Findings = findings };
                CharaBuilders.Run(b.Result, new BuildContext { Services = services, Holder = b.Holder.transform }, asm => asm.Build(input), sw);
                if (b.Result.Ok) CharaDocumentInput.AfterBuild(b.Result.Context);
                else b.Error = b.Result.Error ?? "no root";
            }
            catch (Exception e) { b.Error = e.GetType().Name + ": " + e.Message; }
            return b;
        }

        public void Dispose()
        {
            var ctx = Context;
            if (Root) UnityObjects.ReleaseRenderTextures(Root.transform);
            if (ctx != null)
            {
                foreach (var m in ctx.OwnedMaterials) if (m) UnityObjects.Destroy(m);
                ctx.OwnedMaterials.Clear();
                ctx.ReleaseOwnedMeshes();
                if (ctx.Edit != null)
                    foreach (var c in new List<HS2Next.Chara.Edit.CompositeInputs>(ctx.Edit.Composites()))
                    {
                        var cr = c?.Creator;
                        if (cr == null) continue;
                        var rt = cr.Target;
                        if (rt) { if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); UnityObjects.Destroy(rt); }
                        if (cr.Mat) UnityObjects.Destroy(cr.Mat);
                    }
            }
            if (Holder) Object.DestroyImmediate(Holder);
            Holder = null;
            Result = null;
        }
    }
}
