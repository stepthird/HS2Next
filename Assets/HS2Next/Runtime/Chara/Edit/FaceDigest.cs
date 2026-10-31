// Canonical digests of a built character's face / body realization (
// MakerFaceTest.render): SHA-256 per section of a canonical text, the same text in the Editor and the
// player (the Editor's CharaSnapshot is not in the player).
//   materials  the drawn materials of the part's renderers (face: every renderer under the head part; body: every renderer
//              under the body part), in hierarchy order: renderer path, active / enabled, per slot the shader name, the
//              sorted keywords, the render queue and every property the shader declares (floats, colours and vectors as
//              float bits; textures as name, size and format, plus scale / offset)
//   creators   the part's compositing passes (face: create_skin_face, create_skin detail_face; body: the body pair): the
//              create material (as above), the main texture, the layer counts and the texture input rows
//   bones      face: the head-bone rig's local transforms (CharaContext.HeadBones, by name) as float bits, without the
//              eye-look bones cf_J_look_L / cf_J_look_R (the look stage writes them every frame); body
//              (Phase 4.2): the transforms the body shape map writes (the engine's
//              destination set of Resources/shape_body_map, incl. the skirt *_dam rows), by name
//   rts        (with a graphics device) SHA-256 of each creator's RT mip 0 read back as stored
//   normals    (body, Phase 4.2) per renderer of the body part with a mesh, in hierarchy order: path, mesh name / vertex
//              count and the SHA-256 of its normals as float32 bits (the breast normals)
//   bust       (body, Phase 4.2) the bust stage: card values (softness, weight, size), per chain (component order) its
//              pattern, activity, gravity and per particle damping / elasticity / stiffness / inert, as float bits
//   hair       (Phase 4.3; parts "hair" / "all") materials: every renderer under the four hair
//              part objects (rendHair, rendAccessory and any other), part order, hierarchy order; bones: each part's bundle
//              correction transforms (CmpHair.boneInfo with trfCorrect) as local position / rotation / scale and Euler float
//              bits; chains (new section): the hair chains of the dynamic-bone stage (part, bundle, root, owner, active,
//              noShake, particle count and per particle the captured rest local pose as float bits), sorted by part, bundle,
//              owner and root path
//   clothes    (Phase 4.4; parts "clothes" / "all") materials: every renderer under the eight clothes kind objects, kind order,
//              hierarchy order; creators: the clothes colour passes (create_clothes k.j and create_clothes detail k.j, kind
//              and group order: create material values, main texture, texture inputs); rts: their RT read-backs; objects
//              (new section): per kind the part object's activity, the def / half objects' and the option parts' activity
//   accessories (Phase 4.5; parts "accessories" / "all") materials: every renderer
//              under every slot object, build-slot order, hierarchy order; objects: per slot holding an accessory the slot
//              object's activity, its parent's path of names under the character's root and the local transforms of
//              trfMove01 / trfMove02 as float bits ("acc<slot> none" for a slot without an object); no creators or bones
// Also the RT statistics of chara.edit.rtstats (mean / std / grid cells of mip 0 in 0..1, numbers only). Nothing here
// writes the character. Unity-dependent; no images are produced.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Chara.Edit
{
    public static class FaceDigest
    {
        /// <summary>The section hashes of one digest (lower-case hex SHA-256; Rts null without a graphics device).</summary>
        public sealed class Result
        {
            public string Materials, Creators, Bones, Rts;
            /// <summary>Phase 4.2 sections (parts "body" / "all"; null for "face").</summary>
            public string Normals, Bust;
            /// <summary>Phase 4.3 section (parts "hair" / "all"; null otherwise): the hair chains.</summary>
            public string Chains;
            /// <summary>Phase 4.4 section (parts "clothes", "accessories" / "all"; null otherwise): the clothes objects' activity,
        /// the accessory slot objects' activity, parents and transform sets (Phase 4.5).</summary>
            public string Objects;
            public bool Equals(Result o) => o != null && Materials == o.Materials && Creators == o.Creators && Bones == o.Bones && Rts == o.Rts
                                            && Normals == o.Normals && Bust == o.Bust && Chains == o.Chains && Objects == o.Objects;
            /// <summary>The hash of a section by its name (<see cref="Sections"/>); null when not computed.</summary>
            public string this[string section]
            {
                get
                {
                    switch (section)
                    {
                        case "materials": return Materials;
                        case "creators": return Creators;
                        case "bones": return Bones;
                        case "rts": return Rts;
                        case "normals": return Normals;
                        case "bust": return Bust;
                        case "chains": return Chains;
                        case "objects": return Objects;
                        default: return null;
                    }
                }
            }
            public override string ToString() => $"materials {Short(Materials)}, creators {Short(Creators)}, bones {Short(Bones)}, rts {Short(Rts)}" +
                                                 (Normals != null || Bust != null ? $", normals {Short(Normals)}, bust {Short(Bust)}" : "") +
                                                 (Chains != null ? $", chains {Short(Chains)}" : "") +
                                                 (Objects != null ? $", objects {Short(Objects)}" : "");
            static string Short(string s) => s == null ? "-" : s.Substring(0, Math.Min(12, s.Length));
        }

        /// <summary>The section names of chara.edit.digest, in reply order ("normals" and "bust" for parts body / all, "chains"
        /// for parts hair / all, "objects" for parts clothes / accessories / all).</summary>
        public static readonly string[] Sections = { "materials", "creators", "bones", "rts", "normals", "bust", "chains", "objects" };
        /// <summary>Head-rig bones left out of every bone section (the eye-look stage writes them every frame).</summary>
        public static readonly string[] LookBones = { "cf_J_look_L", "cf_J_look_R" };

        /// <summary>The renderers chara.edit.materials lists by default (the face set).</summary>
        public static readonly string[] FaceRendererNames = { "o_head", "o_eyebase_L", "o_eyebase_R", "o_eyeshadow", "o_eyelashes" };

        /// <summary>True for "face", "body", "hair", "clothes", "accessories", "all" (null = "face").</summary>
        public static bool IsPart(string parts) => parts == null || parts == "face" || parts == "body" || parts == "hair" || parts == "clothes" || parts == "accessories" || parts == "all";

        static bool Face(string parts) => parts == null || parts == "face" || parts == "all";
        static bool Body(string parts) => parts == "body" || parts == "all";
        static bool Hair(string parts) => parts == "hair" || parts == "all";
        static bool Clothes(string parts) => parts == "clothes" || parts == "all";
        static bool Accessories(string parts) => parts == "accessories" || parts == "all";

        /// <summary>Every section of <paramref name="parts"/> ("face", "body", "hair", "clothes", "accessories", "all"); RT
        /// hashes only when <paramref name="rts"/> and a graphics device exists (the hair has no creators: for "hair" the
        /// creators / rts sections hash empty texts; the clothes have no bones: for "clothes" the bones section hashes an empty
        /// text; the accessories have neither).</summary>
        public static Result Compute(CharaContext ctx, string parts = "face", bool rts = true) => new Result
        {
            Materials = Sha256(MaterialsText(ctx, parts)),
            Creators = Sha256(CreatorsText(ctx, parts)),
            Bones = Sha256(BonesText(ctx, parts)),
            Rts = rts && HasDevice ? Sha256(RtsText(ctx, parts)) : null,
            Normals = Body(parts) ? Sha256(NormalsText(ctx, parts)) : null,
            Bust = Body(parts) ? Sha256(BustText(ctx, parts)) : null,
            Chains = Hair(parts) ? Sha256(ChainsText(ctx, parts)) : null,
            Objects = Clothes(parts) || Accessories(parts) ? Sha256(ObjectsText(ctx, parts)) : null,
        };

        public static bool HasDevice => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        // ---------------------------------------------------------------- sections

        /// <summary>The renderers of a part ("face": under the head part; "body": under the body part, not under the head;
        /// "hair": under the four hair part objects, part order; "clothes": under the eight clothes kind objects, kind
        /// order; "accessories": under every slot object, build-slot order), in hierarchy order (inactive included).</summary>
        public static List<Renderer> RenderersOf(CharaContext ctx, string part)
        {
            var l = new List<Renderer>();
            if (ctx == null) return l;
            var head = ctx.Head ? ctx.Head.transform : null;
            if (part == "face" && head) l.AddRange(head.GetComponentsInChildren<Renderer>(true).Where(r => r));
            else if (part == "body" && ctx.Body)
                l.AddRange(ctx.Body.GetComponentsInChildren<Renderer>(true).Where(r => r && !(head && r.transform.IsChildOf(head))));
            else if (part == "hair" && ctx.Hair != null)
                foreach (var go in ctx.Hair) if (go) l.AddRange(go.GetComponentsInChildren<Renderer>(true).Where(r => r));
            else if (part == "clothes") l.AddRange(ClothesRenderersOf(ctx, -1));
            else if (part == "accessories") l.AddRange(AccessoryRenderersOf(ctx, -1));
            return l;
        }

        /// <summary>The renderers under the slot object of build slot <paramref name="slot"/> (−1: every slot, slot order), in
        /// hierarchy order (inactive included).</summary>
        public static List<Renderer> AccessoryRenderersOf(CharaContext ctx, int slot)
        {
            var l = new List<Renderer>();
            if (ctx?.Accessories == null) return l;
            for (int s = 0; s < ctx.Accessories.Count; s++)
            {
                var go = ctx.Accessories[s]?.Object;
                if ((slot < 0 || s == slot) && go) l.AddRange(go.GetComponentsInChildren<Renderer>(true).Where(r => r));
            }
            return l;
        }

        /// <summary>The renderers under clothes kind <paramref name="kind"/>'s object (−1: all eight kinds, kind order), in
        /// hierarchy order (inactive included).</summary>
        public static List<Renderer> ClothesRenderersOf(CharaContext ctx, int kind)
        {
            var l = new List<Renderer>();
            if (ctx?.Clothes == null) return l;
            for (int k = 0; k < ctx.Clothes.Length; k++)
                if ((kind < 0 || k == kind) && ctx.Clothes[k]) l.AddRange(ctx.Clothes[k].GetComponentsInChildren<Renderer>(true).Where(r => r));
            return l;
        }

        /// <summary>The compositing passes of a part ("face", "body", "clothes": every clothes creator).</summary>
        public static IEnumerable<CompositeInputs> CreatorsOf(CharaContext ctx, string part)
        {
            var x = ctx?.Edit;
            if (x == null) yield break;
            if (part == "clothes")
            {
                foreach (var c in ClothesCreatorsOf(ctx, -1)) yield return c;
                yield break;
            }
            var pair = part == "body" ? new[] { x.BodySkin, x.BodyDetail } : new[] { x.FaceSkin, x.FaceDetail };
            foreach (var c in pair) if (c != null) yield return c;
        }

        /// <summary>The clothes compositing passes of kind <paramref name="kind"/> (−1: every kind): per colour group the
        /// colour creator ("create_clothes k.j"), then the detail creator ("create_clothes detail k.j"), kind and group order.</summary>
        public static IEnumerable<CompositeInputs> ClothesCreatorsOf(CharaContext ctx, int kind)
        {
            var x = ctx?.Edit;
            if (x == null) yield break;
            for (int k = 0; k < 8; k++)
            {
                if (kind >= 0 && k != kind) continue;
                for (int j = 0; j < 3; j++)
                {
                    if (x.ClothesColor[k, j] != null) yield return x.ClothesColor[k, j];
                    if (x.ClothesGloss[k, j] != null) yield return x.ClothesGloss[k, j];
                }
            }
        }

        static IEnumerable<string> Parts(string parts)
        {
            if (Face(parts)) yield return "face";
            if (Body(parts)) yield return "body";
        }

        /// <summary>The parts with compositing passes, in digest order: face, body, clothes.</summary>
        static IEnumerable<string> CreatorParts(string parts)
        {
            foreach (var p in Parts(parts)) yield return p;
            if (Clothes(parts)) yield return "clothes";
        }

        public static string MaterialsText(CharaContext ctx, string parts)
        {
            var sb = new StringBuilder();
            foreach (var part in Parts(parts))
            {
                var root = part == "face" ? ctx?.Head : ctx?.Body;
                foreach (var r in RenderersOf(ctx, part))
                {
                    sb.Append("renderer ").Append(part).Append(' ').Append(PathOf(r.transform, root ? root.transform : null))
                      .Append(" active ").Append(r.gameObject.activeInHierarchy ? 1 : 0).Append(" enabled ").Append(r.enabled ? 1 : 0).Append('\n');
                    Material[] mats;
                    try { mats = r.sharedMaterials; } catch (Exception) { mats = new Material[0]; }
                    for (int i = 0; i < mats.Length; i++)
                    {
                        sb.Append(" slot ").Append(i).Append('\n');
                        MaterialText(sb, mats[i], "  ");
                    }
                }
            }
            if (Hair(parts) && ctx?.Hair != null)
                for (int k = 0; k < ctx.Hair.Length; k++)
                {
                    var go = ctx.Hair[k];
                    if (!go) { sb.Append("hair").Append(k).Append(" none\n"); continue; }
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true).Where(r => r))
                    {
                        sb.Append("renderer hair").Append(k).Append(' ').Append(PathOf(r.transform, go.transform.parent))
                          .Append(" active ").Append(r.gameObject.activeInHierarchy ? 1 : 0).Append(" enabled ").Append(r.enabled ? 1 : 0).Append('\n');
                        Material[] mats;
                        try { mats = r.sharedMaterials; } catch (Exception) { mats = new Material[0]; }
                        for (int i = 0; i < mats.Length; i++)
                        {
                            sb.Append(" slot ").Append(i).Append('\n');
                            MaterialText(sb, mats[i], "  ");
                        }
                    }
                }
            if (Clothes(parts) && ctx?.Clothes != null)
                for (int k = 0; k < ctx.Clothes.Length; k++)
                {
                    var go = ctx.Clothes[k];
                    if (!go) { sb.Append("clothes").Append(k).Append(" none\n"); continue; }
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true).Where(r => r))
                    {
                        sb.Append("renderer clothes").Append(k).Append(' ').Append(PathOf(r.transform, go.transform.parent))
                          .Append(" active ").Append(r.gameObject.activeInHierarchy ? 1 : 0).Append(" enabled ").Append(r.enabled ? 1 : 0).Append('\n');
                        Material[] mats;
                        try { mats = r.sharedMaterials; } catch (Exception) { mats = new Material[0]; }
                        for (int i = 0; i < mats.Length; i++)
                        {
                            sb.Append(" slot ").Append(i).Append('\n');
                            MaterialText(sb, mats[i], "  ");
                        }
                    }
                }
            if (Accessories(parts) && ctx?.Accessories != null)
                for (int s = 0; s < ctx.Accessories.Count; s++)
                {
                    var a = ctx.Accessories[s];
                    if (a == null || a.IsEmpty) continue;
                    var go = a.Object;
                    if (!go) { sb.Append("acc").Append(s).Append(" none\n"); continue; }
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true).Where(r => r))
                    {
                        sb.Append("renderer acc").Append(s).Append(' ').Append(PathOf(r.transform, go.transform.parent))
                          .Append(" active ").Append(r.gameObject.activeInHierarchy ? 1 : 0).Append(" enabled ").Append(r.enabled ? 1 : 0).Append('\n');
                        Material[] mats;
                        try { mats = r.sharedMaterials; } catch (Exception) { mats = new Material[0]; }
                        for (int i = 0; i < mats.Length; i++)
                        {
                            sb.Append(" slot ").Append(i).Append('\n');
                            MaterialText(sb, mats[i], "  ");
                        }
                    }
                }
            return sb.ToString();
        }

        /// <summary>The clothes objects (clothes / all): per kind the part object's own activity, then (with a CmpClothes)
        /// objTopDef, objTopHalf, objBotDef, objBotHalf ("-" when the prefab has none) and the option objects of objOpt01 /
        /// objOpt02, as 0 / 1 of their own activity. Accessories (accessories / all), after the clothes: per slot holding an
        /// accessory the slot object's name and own activity, its parent's path of names under the character's root, then
        /// trfMove01 / trfMove02 ("-" when the prefab has none) as local position / rotation / scale float bits. Empty for
        /// other parts.</summary>
        public static string ObjectsText(CharaContext ctx, string parts)
        {
            var sb = new StringBuilder();
            if (Clothes(parts) && ctx?.Clothes != null) ClothesObjectsText(sb, ctx);
            if (Accessories(parts)) AccessoryObjectsText(sb, ctx);
            return sb.ToString();
        }

        static void ClothesObjectsText(StringBuilder sb, CharaContext ctx)
        {
            string A(GameObject o) => o ? (o.activeSelf ? "1" : "0") : "-";
            for (int k = 0; k < ctx.Clothes.Length; k++)
            {
                var go = ctx.Clothes[k];
                sb.Append("clothes").Append(k);
                if (!go) { sb.Append(" none\n"); continue; }
                sb.Append(' ').Append(go.name).Append(" active ").Append(A(go));
                var cmp = go.GetComponent<AIChara.CmpClothes>();
                if (cmp != null)
                {
                    sb.Append(" topDef ").Append(A(cmp.objTopDef)).Append(" topHalf ").Append(A(cmp.objTopHalf))
                      .Append(" botDef ").Append(A(cmp.objBotDef)).Append(" botHalf ").Append(A(cmp.objBotHalf));
                    sb.Append(" opt1 ").Append(cmp.objOpt01 == null ? "" : string.Join(",", cmp.objOpt01.Select(A)));
                    sb.Append(" opt2 ").Append(cmp.objOpt02 == null ? "" : string.Join(",", cmp.objOpt02.Select(A)));
                }
                else sb.Append(" no CmpClothes");
                sb.Append('\n');
            }
        }

        static void AccessoryObjectsText(StringBuilder sb, CharaContext ctx)
        {
            if (ctx?.Accessories == null) return;
            var root = ctx.Root ? ctx.Root.transform : null;
            for (int s = 0; s < ctx.Accessories.Count; s++)
            {
                var a = ctx.Accessories[s];
                if (a == null || a.IsEmpty) continue;
                sb.Append("acc").Append(s);
                var go = a.Object;
                if (!go) { sb.Append(" none\n"); continue; }
                sb.Append(' ').Append(go.name).Append(" active ").Append(go.activeSelf ? 1 : 0)
                  .Append(" parent ").Append(go.transform.parent ? AccessoryReport.NamePath(go.transform.parent, root) : "-");
                var cmp = go.GetComponent<AIChara.CmpAccessory>();
                if (cmp == null) { sb.Append(" no CmpAccessory\n"); continue; }
                sb.Append('\n');
                foreach (var t in new[] { cmp.trfMove01, cmp.trfMove02 })
                {
                    if (!t) { sb.Append(" move -\n"); continue; }
                    sb.Append(" move ");
                    BoneText(sb, "trf", t.name, t);
                }
            }
        }

        public static string CreatorsText(CharaContext ctx, string parts)
        {
            var sb = new StringBuilder();
            foreach (var part in CreatorParts(parts))
                foreach (var c in CreatorsOf(ctx, part))
                {
                    var cr = c.Creator;
                    sb.Append("creator ").Append(c.Name).Append(" rt ").Append(cr?.Target ? cr.Target.width + "x" + cr.Target.height : "none")
                      .Append(" main ").Append(TexText(cr?.Main)).Append(" under ").Append(cr?.Under.Count ?? 0).Append(" over ").Append(cr?.Over.Count ?? 0).Append('\n');
                    foreach (var kv in c.Textures)
                        sb.Append(" input ").Append(kv.Key).Append(' ').Append(kv.Value.row != null ? kv.Value.row.Key.ToString() : "-")
                          .Append(' ').Append(kv.Value.bundle ?? "-").Append(' ').Append(kv.Value.column ?? "-").Append('\n');
                    MaterialText(sb, cr?.Mat, " ");
                }
            return sb.ToString();
        }

        public static string BonesText(CharaContext ctx, string parts)
        {
            var sb = new StringBuilder();
            if (Face(parts) && ctx?.HeadBones != null)
                foreach (var kv in ctx.HeadBones.Where(k => Array.IndexOf(LookBones, k.Key) < 0).OrderBy(k => k.Key, StringComparer.Ordinal))
                    BoneText(sb, "head", kv.Key, kv.Value);
            if (Body(parts) && ctx?.BodyBones != null)
                foreach (var name in BodyShapeDestinations())
                {
                    if (Array.IndexOf(LookBones, name) >= 0) continue;
                    ctx.BodyBones.TryGetValue(name, out var t);
                    BoneText(sb, "body", name, t);
                }
            if (Hair(parts) && ctx?.Hair != null)
                for (int k = 0; k < ctx.Hair.Length; k++)
                {
                    var cmp = ctx.Hair[k] ? ctx.Hair[k].GetComponent<AIChara.CmpHair>() : null;
                    if (cmp?.boneInfo == null) continue;
                    for (int b = 0; b < cmp.boneInfo.Count; b++)
                    {
                        var t = cmp.boneInfo[b]?.trfCorrect;
                        if (!t) continue;
                        var e = t.localEulerAngles;
                        sb.Append("hair").Append(k).Append(' ').Append(b).Append(" euler ").Append(Bits(e.x)).Append(' ').Append(Bits(e.y)).Append(' ').Append(Bits(e.z)).Append(' ');
                        BoneText(sb, "trf", t.name, t);
                    }
                }
            return sb.ToString();
        }

        /// <summary>The hair chains of the dynamic-bone stage (hair / all): per chain the part, bundle, root and owner paths,
        /// active, noShake, enabled, particle count, then per particle its transform's name (or "tip") and the captured rest
        /// local pose as float bits; sorted by part, bundle, owner path, root path (stable). Empty for other parts.</summary>
        public static string ChainsText(CharaContext ctx, string parts)
        {
            var sb = new StringBuilder();
            if (!Hair(parts) || ctx == null) return sb.ToString();
            var stage = ctx.Motion != null ? ctx.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>() : null;
            if (stage == null) { sb.Append("dynbone none\n"); return sb.ToString(); }
            var root = ctx.Root ? ctx.Root.transform : null;
            var chains = stage.Chains.Where(c => c != null && c.Category == HS2Next.Motion.DynamicBones.DynamicBoneCategory.Hair)
                .Select((c, i) => (c, i, owner: c.Owner ? PathOf(c.Owner, root) : "-", rootPath: c.Root ? PathOf(c.Root, root) : "-"))
                .OrderBy(x => x.c.Kind).ThenBy(x => x.c.Bundle).ThenBy(x => x.owner, StringComparer.Ordinal).ThenBy(x => x.rootPath, StringComparer.Ordinal).ThenBy(x => x.i)
                .ToList();
            foreach (var (c, _, owner, rootPath) in chains)
            {
                sb.Append("chain hair").Append(c.Kind).Append(" bundle ").Append(c.Bundle).Append(" root ").Append(rootPath).Append(" owner ").Append(owner)
                  .Append(" active ").Append(c.Active ? 1 : 0).Append(" noShake ").Append(c.NoShake ? 1 : 0).Append(" enabled ").Append(c.Enabled ? 1 : 0)
                  .Append(" particles ").Append(c.ParticleCount).Append('\n');
                for (int i = 0; i < c.ParticleCount; i++)
                {
                    var t = c.ParticleTransform(i);
                    var p = c.RestLocalPosition(i); var q = c.RestLocalRotation(i);
                    sb.Append(" p").Append(i).Append(' ').Append(t ? t.name : "tip").Append(' ').Append(Bits(p.x)).Append(' ').Append(Bits(p.y)).Append(' ').Append(Bits(p.z))
                      .Append(' ').Append(Bits(q.x)).Append(' ').Append(Bits(q.y)).Append(' ').Append(Bits(q.z)).Append(' ').Append(Bits(q.w)).Append('\n');
                }
            }
            return sb.ToString();
        }

        static string loadedBodyMap, lastMapText;
        static string[] bodyMapDst;

        /// <summary>The transforms the body shape map writes (the destination column of Resources/shape_body_map, or of
        /// ShapeEngine.BodyMapOverride), distinct, in ordinal order.</summary>
        public static IReadOnlyList<string> BodyShapeDestinations()
        {
            string text = ShapeEngine.BodyMapOverride;
            if (text == null)
            {
                if (string.IsNullOrEmpty(loadedBodyMap)) loadedBodyMap = Resources.Load<TextAsset>("shape_body_map")?.text ?? "";
                text = loadedBodyMap;
            }
            if (bodyMapDst != null && ReferenceEquals(text, lastMapText)) return bodyMapDst;
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var raw in text.Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                var c = raw.Split('\t');
                if (c.Length > 1 && c[1].Trim().Length > 0) set.Add(c[1].Trim());
            }
            lastMapText = text;
            return bodyMapDst = set.ToArray();
        }

        /// <summary>Per renderer of the body part with a mesh (hierarchy order): path, mesh name / vertex count, the SHA-256 of
        /// its normals as float32 bits ("unreadable" for a mesh whose data the CPU cannot read). Empty for "face".</summary>
        public static string NormalsText(CharaContext ctx, string parts)
        {
            var sb = new StringBuilder();
            if (!Body(parts) || ctx == null) return sb.ToString();
            var root = ctx.Body ? ctx.Body.transform : null;
            foreach (var r in RenderersOf(ctx, "body"))
            {
                var m = Units.BustNormalsUnit.MeshOf(r);
                if (!m) continue;
                sb.Append("normals ").Append(PathOf(r.transform, root)).Append(' ').Append(m.name).Append('/').Append(m.vertexCount)
                  .Append(' ').Append(NormalsHash(m)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>SHA-256 (lower-case hex) of a mesh's normals as float32 bits (x, y, z per vertex, little-endian);
        /// "unreadable" when the mesh's data is not CPU-readable, "none" for null.</summary>
        public static string NormalsHash(Mesh m)
        {
            if (!m) return "none";
            if (!m.isReadable) return "unreadable";
            var l = new List<Vector3>(m.vertexCount);
            m.GetNormals(l);
            var f = new float[l.Count * 3];
            for (int i = 0; i < l.Count; i++) { f[3 * i] = l[i].x; f[3 * i + 1] = l[i].y; f[3 * i + 2] = l[i].z; }
            var b = new byte[f.Length * 4];
            Buffer.BlockCopy(f, 0, b, 0, b.Length);
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(b));
        }

        /// <summary>The bust stage as text (body / all): card values, then per chain (component order) kind, pattern, activity,
        /// gravity and per particle damping / elasticity / stiffness / inert, as float bits. Empty without a bust stage.</summary>
        public static string BustText(CharaContext ctx, string parts)
        {
            var sb = new StringBuilder();
            if (!Body(parts) || ctx?.Motion == null) return sb.ToString();
            var bust = ctx.Motion.Get<HS2Next.Motion.DynamicBones.BustStage>();
            if (bust == null) { sb.Append("bust none\n"); return sb.ToString(); }
            sb.Append("bust ").Append(bust.IsMale ? "male" : "female").Append(" enabled ").Append(bust.Enabled ? 1 : 0)
              .Append(" softness ").Append(Bits(bust.BustSoftness)).Append(" weight ").Append(Bits(bust.BustWeight)).Append(" size ").Append(Bits(bust.BustSize)).Append('\n');
            foreach (var c in bust.All)
            {
                if (c == null) continue;
                var g = c.Gravity;
                sb.Append("chain ").Append(c.Comment).Append(" kind ").Append(c.Kind).Append(" pattern ").Append(c.PtnNo).Append(" active ").Append(c.Active ? 1 : 0)
                  .Append(" gravity ").Append(Bits(g.x)).Append(' ').Append(Bits(g.y)).Append(' ').Append(Bits(g.z)).Append(" particles ").Append(c.ParticleCount).Append('\n');
                for (int i = 0; i < c.ParticleCount; i++)
                    sb.Append(" p").Append(i).Append(' ').Append(Bits(c.Damping(i))).Append(' ').Append(Bits(c.Elasticity(i))).Append(' ').Append(Bits(c.Stiffness(i)))
                      .Append(' ').Append(Bits(c.Inert(i))).Append('\n');
            }
            return sb.ToString();
        }

        static void BoneText(StringBuilder sb, string part, string name, Transform t)
        {
            sb.Append(part).Append(' ').Append(name);
            if (!t) { sb.Append(" missing\n"); return; }
            var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
            sb.Append(' ').Append(Bits(p.x)).Append(' ').Append(Bits(p.y)).Append(' ').Append(Bits(p.z))
              .Append(' ').Append(Bits(q.x)).Append(' ').Append(Bits(q.y)).Append(' ').Append(Bits(q.z)).Append(' ').Append(Bits(q.w))
              .Append(' ').Append(Bits(s.x)).Append(' ').Append(Bits(s.y)).Append(' ').Append(Bits(s.z)).Append('\n');
        }

        /// <summary>Per creator of the part: name and the SHA-256 of mip 0 read back as stored (null without a device).</summary>
        public static string RtsText(CharaContext ctx, string parts)
        {
            if (!HasDevice) return null;
            var sb = new StringBuilder();
            foreach (var part in CreatorParts(parts))
                foreach (var c in CreatorsOf(ctx, part))
                    sb.Append(c.Name).Append(' ').Append(RtHash(c.Creator?.Target) ?? "none").Append('\n');
            return sb.ToString();
        }

        /// <summary>SHA-256 of mip 0 of <paramref name="rt"/> read back in its own format (null without a device or RT).</summary>
        public static string RtHash(RenderTexture rt)
        {
            if (!rt || !HasDevice) return null;
            var req = AsyncGPUReadback.Request(rt, 0);
            req.WaitForCompletion();
            if (req.hasError) return "readback-error";
            var data = req.GetData<byte>().ToArray();
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(data));
        }

        // ---------------------------------------------------------------- material text

        /// <summary>Canonical text of one material: shader, sorted keywords, render queue, every declared property.</summary>
        public static void MaterialText(StringBuilder sb, Material m, string indent = "")
        {
            if (!m) { sb.Append(indent).Append("material none\n"); return; }
            var sh = m.shader;
            sb.Append(indent).Append("material ").Append(m.name).Append(" shader ").Append(sh ? sh.name : "none")
              .Append(" queue ").Append(m.renderQueue).Append(" keywords ").Append(string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal))).Append('\n');
            if (!sh) return;
            int n = sh.GetPropertyCount();
            var names = new List<(string name, ShaderPropertyType type)>(n);
            for (int i = 0; i < n; i++) names.Add((sh.GetPropertyName(i), sh.GetPropertyType(i)));
            foreach (var (name, type) in names.OrderBy(p => p.name, StringComparer.Ordinal))
            {
                sb.Append(indent).Append(' ').Append(name).Append(' ');
                switch (type)
                {
                    case ShaderPropertyType.Color:
                        { var c = m.GetColor(name); sb.Append("c ").Append(Bits(c.r)).Append(' ').Append(Bits(c.g)).Append(' ').Append(Bits(c.b)).Append(' ').Append(Bits(c.a)); break; }
                    case ShaderPropertyType.Vector:
                        { var v = m.GetVector(name); sb.Append("v ").Append(Bits(v.x)).Append(' ').Append(Bits(v.y)).Append(' ').Append(Bits(v.z)).Append(' ').Append(Bits(v.w)); break; }
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        sb.Append("f ").Append(Bits(m.GetFloat(name))); break;
                    case ShaderPropertyType.Int:
                        sb.Append("i ").Append(m.GetInteger(name).ToString(CultureInfo.InvariantCulture)); break;
                    case ShaderPropertyType.Texture:
                        {
                            var t = m.GetTexture(name);
                            var s = m.GetTextureScale(name); var o = m.GetTextureOffset(name);
                            sb.Append("t ").Append(TexText(t)).Append(" st ").Append(Bits(s.x)).Append(' ').Append(Bits(s.y)).Append(' ').Append(Bits(o.x)).Append(' ').Append(Bits(o.y));
                            break;
                        }
                    default: sb.Append("?"); break;
                }
                sb.Append('\n');
            }
        }

        /// <summary>A texture as name, size and format ("none" for null).</summary>
        public static string TexText(Texture t)
        {
            if (!t) return "none";
            string fmt = t is Texture2D t2 ? t2.format.ToString() : t is RenderTexture rt ? "RT:" + rt.format : t.GetType().Name;
            return (string.IsNullOrEmpty(t.name) ? "(unnamed)" : t.name) + " " + t.width + "x" + t.height + " " + fmt;
        }

        static string PathOf(Transform t, Transform root)
        {
            var l = new List<string>();
            for (var x = t; x && x != root; x = x.parent) l.Add(x.name + "#" + x.GetSiblingIndex().ToString(CultureInfo.InvariantCulture));
            l.Reverse();
            return string.Join("/", l);
        }

        static string Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8", CultureInfo.InvariantCulture);

        public static string Sha256(string text)
        {
            if (text == null) return null;
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        static string Hex(byte[] h)
        {
            var sb = new StringBuilder(h.Length * 2);
            foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- RT statistics (chara.edit.rtstats)

        /// <summary>Statistics of one RT's mip 0 in 0..1 (RGBA order): mean and standard deviation over all pixels, and the
        /// per-cell means of a grid × grid split (row-major from the RT's bottom row, v = 0, upwards; u ascending; 4 values per
        /// cell); the 8-bit values as stored / 255 (no colour conversion).</summary>
        public sealed class RtStats
        {
            public string Name;
            public int Width, Height, Grid;
            public double[] Mean = new double[4], Std = new double[4];
            public double[] Cells;
            /// <summary>SHA-256 of the RGBA32 mip-0 bytes (bottom row first, as read back).</summary>
            public string Sha256;
        }

        /// <summary>Reads back mip 0 of <paramref name="rt"/> as RGBA32 and computes <see cref="RtStats"/>; null without a
        /// graphics device or RT.</summary>
        public static RtStats StatsOf(RenderTexture rt, int grid = 8, string name = null)
        {
            if (!rt || !HasDevice) return null;
            grid = Math.Max(1, Math.Min(grid, Math.Min(rt.width, rt.height)));
            var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) return null;
            var px = req.GetData<byte>();
            int w = rt.width, h = rt.height;
            string sha;
            using (var hash = SHA256.Create()) sha = Hex(hash.ComputeHash(px.ToArray()));
            var st = new RtStats { Name = name ?? rt.name, Width = w, Height = h, Grid = grid, Cells = new double[grid * grid * 4], Sha256 = sha };
            var sum = new double[4]; var sq = new double[4];
            var cellSum = new double[grid * grid * 4]; var cellN = new long[grid * grid];
            for (int y = 0; y < h; y++)
            {
                int cy = (int)((long)y * grid / h);
                for (int x = 0; x < w; x++)
                {
                    int cx = (int)((long)x * grid / w), ci = cy * grid + cx, i = (y * w + x) * 4;
                    cellN[ci]++;
                    for (int c = 0; c < 4; c++)
                    {
                        double v = px[i + c] / 255.0;
                        sum[c] += v; sq[c] += v * v; cellSum[ci * 4 + c] += v;
                    }
                }
            }
            double n = (double)w * h;
            for (int c = 0; c < 4; c++)
            {
                st.Mean[c] = sum[c] / n;
                st.Std[c] = Math.Sqrt(Math.Max(0, sq[c] / n - st.Mean[c] * st.Mean[c]));
            }
            for (int ci = 0; ci < grid * grid; ci++)
                for (int c = 0; c < 4; c++) st.Cells[ci * 4 + c] = cellN[ci] > 0 ? cellSum[ci * 4 + c] / cellN[ci] : 0;
            return st;
        }

        /// <summary>The statistics of every creator of a part ("face" / "body" / "clothes"); empty without a device.</summary>
        public static List<RtStats> StatsOfPart(CharaContext ctx, string part, int grid = 8)
        {
            var l = new List<RtStats>();
            foreach (var c in CreatorsOf(ctx, part))
            {
                var s = StatsOf(c.Creator?.Target, grid, c.Name);
                if (s != null) l.Add(s);
            }
            return l;
        }
    }
}
