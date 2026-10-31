// Typed plugin sections from the Phase 1 decoders (CardExtensions.Decode): ABMX,
// UncensorSelector, MaterialEditor, KSOX, KCOX, pushUp, DynamicBoneEditor, InvisibleBody, AdditionalAccessoryControls.
// MoreAccessories feeds the accessory list (VanillaImport) and the resolver feeds the id resolution. Slot-addressed data goes through
// the slot → entry table; data aimed at a slot without an entry is dropped from the typed section and reported
// (the raw bytes stay in hs2.kkex). Images go to res/ unchanged.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Data.Ext;
using HS2Next.Save;
using SaveUncensor = HS2Next.Save.UncensorData;

namespace HS2Next.Import
{
    static class PluginImport
    {
        static string N(int i) => i.ToString(CultureInfo.InvariantCulture);

        /// <summary>Everything typed of one source. <paramref name="chara"/> is null for a coordinate file.</summary>
        public static void Apply(ImportContext x, CardExtData ext, CharaDocument chara, CoordDocument coord, bool isCoordinate)
        {
            if (chara != null)
            {
                Abmx(x, ext.Abmx, chara.Body, isCoordinate);
                Uncensor(x, ext.Uncensor, chara.Body);
                Ksox(x, ext.SkinOverlays, chara);
                if (ext.InvisibleBody != null) { chara.Body.Visible = ext.InvisibleBody.Visible; x.Report.Count("plugin.invisibleBody"); }
            }
            else Abmx(x, ext.Abmx, null, isCoordinate);
            MaterialEditor(x, ext.MaterialEditor, chara, coord);
            Kcox(x, ext.ClothesOverlays, chara, coord, isCoordinate);
            PushUp(x, ext.PushUp, coord);
            Dbe(x, ext.DynamicBoneEditor);
            Aac(x, ext.AdditionalAccessoryControls, coord);
            foreach (var d in ext.Diagnostics)
            {
                int colon = d.IndexOf(':');
                string id = colon > 0 ? d.Substring(0, colon) : "";
                // Character cards keep KCOX / KSOX coordinate type 0 only (HS2 has one coordinate); the decoders report the others.
                if (d.StartsWith(PluginIds.Kcox + ": coordinate key", StringComparison.Ordinal)) { x.Report.Warning("kcox.coordinateType", x.CoordPath + "clothes", d); x.Report.Count("kcox.coordinateType"); }
                else if (d.StartsWith(PluginIds.Ksox + ": Lookup: coordinate key", StringComparison.Ordinal)) { x.Report.Warning("ksox.coordinateType", "body.skinOverlays", d); x.Report.Count("ksox.coordinateType"); }
                else x.Report.Info("plugin.diagnostic", id, d);
            }
        }

        static Vec3 V3(ImportContext x, UnityEngine.Vector3 v, string p) => new Vec3(x.Raw.Plugin(v.x, p), x.Raw.Plugin(v.y, p), x.Raw.Plugin(v.z, p));

        // ------------------------------------------------------------------ ABMX
        static void Abmx(ImportContext x, AbmxData abmx, BodyData body, bool isCoordinate)
        {
            if (abmx == null) return;
            foreach (var m in abmx.Modifiers)
            {
                var data = m.Data;
                string p = "body.boneMods";
                if (m.CoordinateModifiers.Count > 1)
                {
                    x.Report.Warning("abmx.extraSets", "boneMods:" + m.BoneName, $"{m.CoordinateModifiers.Count} modifier sets; the first is used (as HS2 does), the others stay in hs2.kkex");
                    x.Report.Count("abmx.extraSets");
                }
                if (string.IsNullOrEmpty(m.BoneName)) { x.Report.Warning("abmx.noBone", p, "modifier without a bone name; dropped"); x.Report.Count("abmx.dropped"); continue; }
                if ((m.Location == 0 || m.Location == 1) && body != null)
                {
                    var b = new BodyBoneMod { Bone = m.BoneName, Location = m.Location == 1 ? BodyBoneMod.LocationBodyTop : BodyBoneMod.LocationUnknown };
                    Fill(x, b, data, p);
                    body.BoneMods.Add(b);
                    x.Report.Count("abmx.body");
                }
                else if (m.AccessorySlot >= 0)
                {
                    if (x.Slots.TryGetValue(m.AccessorySlot, out var e))
                    {
                        var b = new AccessoryBoneMod { Bone = m.BoneName };
                        Fill(x, b, data, x.CoordPath + "accessories[slot " + N(m.AccessorySlot) + "].boneMods");
                        e.BoneMods.Add(b);
                        x.Report.Count("abmx.accessory");
                    }
                    else
                    {
                        x.Report.Warning("abmx.slotEmpty", x.CoordPath + "accessories[slot " + N(m.AccessorySlot) + "].boneMods", "ABMX modifier aimed at a slot without an accessory; dropped");
                        x.Report.Count("abmx.dropped");
                    }
                }
                else
                {
                    x.Report.Warning(isCoordinate && m.Location <= 1 ? "abmx.notCoordinate" : "abmx.badLocation", p, $"location {m.Location} has no target here; dropped");
                    x.Report.Count("abmx.dropped");
                }
            }
        }

        static void Fill(ImportContext x, BoneModBase b, AbmxData.BoneModifierData d, string p)
        {
            b.Scale = V3(x, d.Scale, p);
            b.Length = x.Raw.Plugin(d.Length, p);
            b.Position = V3(x, d.Position, p);
            b.Rotation = V3(x, d.Rotation, p);
        }

        // ------------------------------------------------------------------ UncensorSelector
        static void Uncensor(ImportContext x, HS2Next.Data.Ext.UncensorData u, BodyData body)
        {
            if (u == null) return;
            body.Uncensor = new SaveUncensor
            {
                Body = u.BodyGUID, Penis = u.PenisGUID, Balls = u.BallsGUID, DisplayBalls = u.DisplayBalls, Legacy = u.LegacyUncensorGUID,
            };
            x.Report.Count("plugin.uncensor");
        }

        // ------------------------------------------------------------------ KSOX (one overlay per target)
        static void Ksox(ImportContext x, SkinOverlayData k, CharaDocument chara)
        {
            if (k == null) return;
            if (k.Version >= 2)
            {
                chara.StudioOverlays.Skin = k.EnableInStudioSkin;
                chara.StudioOverlays.Eyes = k.EnableInStudioIris;
            }
            if (k.HasNonBundledTextures) x.Report.Warning("ksox.nonBundled", "body.skinOverlays", "LOCAL_/DEDUPED_ textures are not resolved");
            var keys = new List<SkinTexType>(k.Textures.Keys);
            keys.Sort();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in keys)
            {
                string target = Enum.IsDefined(typeof(SkinOverlayTarget), (int)t) ? ((SkinOverlayTarget)(int)t).ToString() : null;
                if (target == null)
                {
                    x.Report.Warning("ksox.unknownType", "body.skinOverlays", $"TexType {t} has no target; dropped");
                    x.Report.Count("ksox.dropped");
                    continue;
                }
                if (!seen.Add(target)) { x.Report.Warning("ksox.duplicateTarget", "body.skinOverlays", target + " twice; the first kept"); x.Report.Count("ksox.dropped"); continue; }
                var name = x.Res.AddImported(k.Textures[t], x.Report.Findings, "body.skinOverlays." + target);
                chara.Body.SkinOverlays.Add(new SkinOverlay { Target = target, Image = name });
                x.Report.Count("ksox.overlays");
            }
        }

        // ------------------------------------------------------------------ MaterialEditor
        static void MaterialEditor(ImportContext x, MaterialEditorData me, CharaDocument chara, CoordDocument coord)
        {
            if (me == null) return;
            foreach (var key in me.CompatKeys.Keys) x.Report.Warning("me.nonBundled", "materialOverrides", key + ": non-bundled textures are not resolved");
            foreach (var item in me.AllItems())
            {
                List<MaterialOverride> target = null;
                string p = null;
                if (item.CoordinateIndex != 0)
                {
                    x.Report.Warning("me.coordinateIndex", "materialOverrides", $"CoordinateIndex {item.CoordinateIndex} (HS2 has only 0); dropped");
                    x.Report.Count("me.dropped");
                    continue;
                }
                switch (item.ObjectType)
                {
                    case MaterialEditorData.ObjectType.Character:
                        if (chara != null) { target = chara.MaterialOverrides; p = "materialOverrides"; }
                        break;
                    case MaterialEditorData.ObjectType.Hair:
                        if (chara != null && item.Slot >= 0 && item.Slot < 4) { target = chara.Hair.Parts[item.Slot].MaterialOverrides; p = "hair." + HairData.PartNames[item.Slot] + ".materialOverrides"; }
                        break;
                    case MaterialEditorData.ObjectType.Clothing:
                        if (item.Slot >= 0 && item.Slot < ClothesSet.Count) { target = coord.Clothes.Parts[item.Slot].MaterialOverrides; p = x.CoordPath + "clothes." + ClothesSet.Names[item.Slot] + ".materialOverrides"; }
                        break;
                    case MaterialEditorData.ObjectType.Accessory:
                        if (x.Slots.TryGetValue(item.Slot, out var e)) { target = e.MaterialOverrides; p = x.CoordPath + "accessories[slot " + N(item.Slot) + "].materialOverrides"; }
                        else
                        {
                            x.Report.Warning("me.slotEmpty", x.CoordPath + "accessories[slot " + N(item.Slot) + "].materialOverrides", "MaterialEditor item aimed at a slot without an accessory; dropped");
                            x.Report.Count("me.dropped");
                            continue;
                        }
                        break;
                }
                if (target == null)
                {
                    string code = item.ObjectType == MaterialEditorData.ObjectType.Unknown || !Enum.IsDefined(typeof(MaterialEditorData.ObjectType), item.ObjectType) ? "me.objectType"
                        : chara == null && (item.ObjectType == MaterialEditorData.ObjectType.Character || item.ObjectType == MaterialEditorData.ObjectType.Hair) ? "me.notCoordinate" : "me.badSlot";
                    x.Report.Warning(code, "materialOverrides", $"ObjectType {(int)item.ObjectType} slot {item.Slot} has no target here; dropped");
                    x.Report.Count("me.dropped");
                    continue;
                }
                var o = Convert(x, me, item, p);
                if (o == null) continue;
                target.Add(o);
                x.Report.Count("me.items");
                x.Report.Count("me." + o.Type);
            }
        }

        static MaterialOverride Convert(ImportContext x, MaterialEditorData me, MaterialEditorData.Item item, string p)
        {
            switch (item)
            {
                case MaterialEditorData.MaterialCopy c: return new MeCopy { Material = c.MaterialName ?? "", CopyName = c.MaterialCopyName ?? "" };
                case MaterialEditorData.NameProperty n: return new MeName { Renderer = n.Renderer ?? "", Material = n.MaterialName ?? "", Value = n.Value ?? "", Original = n.ValueOriginal };
                case MaterialEditorData.ShaderEdit s:
                    return new MeShader { Material = s.MaterialName ?? "", Shader = s.ShaderName, ShaderOriginal = s.ShaderNameOriginal, RenderQueue = s.RenderQueue, RenderQueueOriginal = s.RenderQueueOriginal };
                case MaterialEditorData.RendererProperty rp:
                {
                    var o = new MeRenderer { Renderer = rp.RendererName ?? "", Property = EnumName<MeRendererProperty>((int)rp.Property) };
                    if (TryInt(rp.Value, out int v)) o.Value = v; else { o.ValueText = rp.Value ?? ""; Unparsed(x, p, rp.Value); }
                    if (rp.ValueOriginal == null) o.Original = null;
                    else if (TryInt(rp.ValueOriginal, out int vo)) o.Original = vo;
                    else { o.OriginalText = rp.ValueOriginal; Unparsed(x, p, rp.ValueOriginal); }
                    return o;
                }
                case MaterialEditorData.FloatProperty f:
                {
                    var o = new MeFloat { Material = f.MaterialName ?? "", Property = f.Property ?? "" };
                    if (TryFloat(f.Value, out float v)) o.Value = x.Raw.Plugin(v, p); else { o.ValueText = f.Value ?? ""; Unparsed(x, p, f.Value); }
                    if (f.ValueOriginal == null) o.Original = null;
                    else if (TryFloat(f.ValueOriginal, out float vo)) o.Original = x.Raw.Plugin(vo, p);
                    else { o.OriginalText = f.ValueOriginal; Unparsed(x, p, f.ValueOriginal); }
                    return o;
                }
                case MaterialEditorData.KeywordProperty k: return new MeKeyword { Material = k.MaterialName ?? "", Property = k.Property ?? "", Value = k.Value, Original = k.ValueOriginal };
                case MaterialEditorData.ColorProperty c:
                    return new MeColor { Material = c.MaterialName ?? "", Property = c.Property ?? "", Value = Col(x, c.Value, p), Original = c.ValueOriginal.HasValue ? Col(x, c.ValueOriginal.Value, p) : (Color4?)null };
                case MaterialEditorData.VectorProperty vp:
                    return new MeVector { Material = vp.MaterialName ?? "", Property = vp.Property ?? "", Value = V4(x, vp.Value, p), Original = vp.ValueOriginal.HasValue ? V4(x, vp.ValueOriginal.Value, p) : (Vec4?)null };
                case MaterialEditorData.TextureProperty t:
                {
                    var o = new MeTexture
                    {
                        Material = t.MaterialName ?? "", Property = t.Property ?? "",
                        Offset = V2(x, t.Offset, p), OffsetOriginal = V2(x, t.OffsetOriginal, p), Scale = V2(x, t.Scale, p), ScaleOriginal = V2(x, t.ScaleOriginal, p),
                        Image = Texture(x, me, t.TexID, p),
                    };
                    if (t.TexAnimationDef != null) { x.Report.Warning("me.texAnimation", p, "texture animation (TexAnimationDef) dropped"); x.Report.Count("me.texAnimation"); }
                    return o;
                }
                case MaterialEditorData.CubemapProperty cm:
                    return new MeCubemap { Material = cm.MaterialName ?? "", Property = cm.Property ?? "", Image = Texture(x, me, cm.TexID, p) };
                case MaterialEditorData.ProjectorProperty pr:
                    return new MeProjector { Projector = pr.ProjectorName ?? "", Property = EnumName<MeProjectorProperty>(pr.Property), Value = pr.Value ?? "", Original = pr.ValueOriginal };
            }
            x.Report.Warning("me.unknownItem", p, "unknown MaterialEditor item kind; dropped");
            return null;
        }

        static string Texture(ImportContext x, MaterialEditorData me, int? texId, string p)
        {
            if (!texId.HasValue) return null;
            if (!me.Textures.TryGetValue(texId.Value, out var bytes) || bytes == null || bytes.Length == 0)
            {
                x.Report.Warning("me.missingTexture", p, $"TexID {texId} is not in the TextureDictionary; image null");
                x.Report.Count("me.missingTexture");
                return null;
            }
            x.Report.Count("me.textures");
            return x.Res.AddImported(bytes, x.Report.Findings, p);
        }

        static string EnumName<T>(int v) where T : struct, Enum =>
            Enum.IsDefined(typeof(T), v) ? ((T)(object)v).ToString() : v.ToString(CultureInfo.InvariantCulture);

        static bool TryInt(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        static bool TryFloat(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);

        static void Unparsed(ImportContext x, string p, string text)
        {
            x.Report.Warning("me.textValue", p, $"value \"{text}\" is not a number; kept as text");
            x.Report.Count("me.textValue");
        }

        static Color4 Col(ImportContext x, UnityEngine.Color c, string p) => new Color4(x.Raw.Plugin(c.r, p), x.Raw.Plugin(c.g, p), x.Raw.Plugin(c.b, p), x.Raw.Plugin(c.a, p));
        static Vec4 V4(ImportContext x, UnityEngine.Vector4 v, string p) => new Vec4(x.Raw.Plugin(v.x, p), x.Raw.Plugin(v.y, p), x.Raw.Plugin(v.z, p), x.Raw.Plugin(v.w, p));
        static Vec2? V2(ImportContext x, UnityEngine.Vector2? v, string p) => v.HasValue ? new Vec2(x.Raw.Plugin(v.Value.x, p), x.Raw.Plugin(v.Value.y, p)) : (Vec2?)null;

        // ------------------------------------------------------------------ KCOX
        static void Kcox(ImportContext x, ClothesOverlayData k, CharaDocument chara, CoordDocument coord, bool isCoordinate)
        {
            if (k == null) return;
            if (chara != null) chara.StudioOverlays.Clothes = k.EnableInStudio;
            if (k.HasNonBundledTextures) x.Report.Warning("kcox.nonBundled", x.CoordPath + "clothes", "LOCAL/DEDUPED textures are not resolved");
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int kind = 0; kind < ClothesSet.Count; kind++)
            {
                string n = ClothesOverlayData.MainClothesNames[kind];
                string p = x.CoordPath + "clothes." + ClothesSet.Names[kind] + ".overlays";
                var set = new ClothesOverlaySet
                {
                    Main = Ov(x, k, n, used, p + ".main"),
                    ColorMask = Ov(x, k, "Colormask_" + n, used, p + ".colorMask"),
                    SourceReplacement = Ov(x, k, "Override_" + n, used, p + ".sourceReplacement"),
                };
                for (int j = 0; j < ClothesOverlaySet.PatternCount; j++) set.Patterns[j] = Ov(x, k, "Pattern_" + N(j) + "_" + n, used, p + ".patterns[" + j + "]");
                if (k.TextureSizeOverride.TryGetValue(n, out int size)) { set.TextureSize = size; used.Add("size:" + n); }
                bool any = set.Main != null || set.ColorMask != null || set.SourceReplacement != null || set.TextureSize.HasValue;
                foreach (var pt in set.Patterns) any |= pt != null;
                if (any) coord.Clothes.Parts[kind].Overlays = set;
            }
            foreach (var id in k.Overlays.Keys)
                if (!used.Contains(id)) { x.Report.Warning("kcox.unknownId", x.CoordPath + "clothes", $"overlay id {id} names no clothes kind; dropped"); x.Report.Count("kcox.dropped"); }
            foreach (var id in k.TextureSizeOverride.Keys)
                if (!used.Contains("size:" + id)) { x.Report.Warning("kcox.unknownId", x.CoordPath + "clothes", $"TextureSizeOverride id {id} names no clothes kind; dropped"); x.Report.Count("kcox.dropped"); }
        }

        static OverlayEntry Ov(ImportContext x, ClothesOverlayData k, string id, HashSet<string> used, string p)
        {
            if (!k.Overlays.TryGetValue(id, out var t)) return null;
            used.Add(id);
            var e = new OverlayEntry { ClearTarget = t.Override, Blend = t.BlendingMode };
            if (t.TextureBytes != null) e.Image = x.Res.AddImported(t.TextureBytes, x.Report.Findings, p);
            x.Report.Count("kcox.overlays");
            return e;
        }

        // ------------------------------------------------------------------ pushUp
        static void PushUp(ImportContext x, PushUpData pu, CoordDocument coord)
        {
            if (pu == null) return;
            coord.Clothes[ClothesKind.InnerTop].PushUp = Set(x, pu.Bra, x.CoordPath + "clothes.innerTop.pushUp");
            coord.Clothes[ClothesKind.Top].PushUp = Set(x, pu.Top, x.CoordPath + "clothes.top.pushUp");
            if (pu.OtherKeys.Count > 0)
            {
                x.Report.Warning("pushup.otherKeys", x.CoordPath + "clothes", $"{pu.OtherKeys.Count} keys the plugin does not read ({string.Join(", ", pu.OtherKeys.Keys)}); kept in hs2.kkex only");
                x.Report.Count("pushup.otherKeys", pu.OtherKeys.Count);
            }
            x.Report.Count("plugin.pushUp");
        }

        static PushUpSettings Set(ImportContext x, PushUpData.PushUpSet s, string p) => new PushUpSettings
        {
            Enabled = s.EnablePushup, Firmness = x.Raw.Plugin(s.Firmness, p), Lift = x.Raw.Plugin(s.Lift, p), PushTogether = x.Raw.Plugin(s.PushTogether, p),
            Squeeze = x.Raw.Plugin(s.Squeeze, p), CenterNipples = x.Raw.Plugin(s.CenterNipples, p), Corset = x.Raw.Plugin(s.Corset, p),
            FlattenNipples = s.FlattenNipples, HideNipples = s.HideNipples, HideAccessories = s.HideAccessories, CorsetHalf = s.CorsetHalf,
        };

        // ------------------------------------------------------------------ DynamicBoneEditor
        static void Dbe(ImportContext x, DynamicBoneEditorData dbe)
        {
            if (dbe == null) return;
            foreach (var e in dbe.Entries)
            {
                string p = x.CoordPath + "accessories[slot " + N(e.Slot) + "].dynamicBones";
                if (e.CoordinateIndex != 0) { x.Report.Warning("dbe.coordinateIndex", p, $"CoordinateIndex {e.CoordinateIndex} (HS2 has only 0); dropped"); x.Report.Count("dbe.dropped"); continue; }
                if (!x.Slots.TryGetValue(e.Slot, out var acc)) { x.Report.Warning("dbe.slotEmpty", p, "DynamicBoneEditor entry aimed at a slot without an accessory; dropped"); x.Report.Count("dbe.dropped"); continue; }
                if (e.BoneName == null) { x.Report.Warning("dbe.noRoot", p, "entry without a root bone name; dropped"); x.Report.Count("dbe.dropped"); continue; }
                acc.DynamicBones.Add(new DynamicBoneOverride
                {
                    Root = e.BoneName,
                    FreezeAxis = e.FreezeAxis, Weight = x.Raw.PluginN(e.Weight, p), Damping = x.Raw.PluginN(e.Damping, p), Elasticity = x.Raw.PluginN(e.Elasticity, p),
                    Stiffness = x.Raw.PluginN(e.Stiffness, p), Inertia = x.Raw.PluginN(e.Inertia, p), Radius = x.Raw.PluginN(e.Radius, p),
                    Original = new DynamicBoneValues
                    {
                        FreezeAxis = e.FreezeAxisOriginal, Weight = x.Raw.PluginN(e.WeightOriginal, p), Damping = x.Raw.PluginN(e.DampingOriginal, p),
                        Elasticity = x.Raw.PluginN(e.ElasticityOriginal, p), Stiffness = x.Raw.PluginN(e.StiffnessOriginal, p),
                        Inertia = x.Raw.PluginN(e.InertiaOriginal, p), Radius = x.Raw.PluginN(e.RadiusOriginal, p),
                    },
                });
                x.Report.Count("dbe.entries");
            }
        }

        // ------------------------------------------------------------------ AdditionalAccessoryControls
        static void Aac(ImportContext x, AdditionalAccessoryControlsData aac, CoordDocument coord)
        {
            if (aac == null) return;
            foreach (var s in aac.Slots)
            {
                string p = x.CoordPath + "accessories[slot " + N(s.SlotNumber) + "].rules";
                if (s.IsEmpty)
                {
                    // The plugin writes a record for every slot; an empty one carries nothing (counted, not a finding).
                    x.Report.Count("aac.emptySlotRecords");
                    if (s.VisibilityRules.Count > 0) { x.Report.Warning("aac.slotEmpty", p, "rules on a slot record marked empty; dropped"); x.Report.Count("aac.rulesDropped", s.VisibilityRules.Count); }
                    continue;
                }
                if (!x.Slots.TryGetValue(s.SlotNumber, out var e))
                {
                    x.Report.Warning("aac.slotEmpty", p, "AdditionalAccessoryControls data aimed at a slot without an accessory; dropped");
                    x.Report.Count("aac.rulesDropped", s.VisibilityRules.Count);
                    continue;
                }
                var rules = new AccessoryRules
                {
                    CharacterAccessory = s.CharacterAccessory, MatchHairColor = s.AutoMatchBackHairColor, AdvancedParent = s.AdvancedParent, Name = s.AccessoryName,
                };
                foreach (var r in s.VisibilityRules)
                {
                    var rule = new AacRule { Rule = RuleName(r.Rule) };
                    if (r.Rule == AdditionalAccessoryControlsData.Rule.ACCESSORY_LINK || r.Rule == AdditionalAccessoryControlsData.Rule.ACCESSORY_INVERSE_LINK)
                    {
                        if (TryInt(r.Modifier, out int oneBased) && x.Slots.TryGetValue(oneBased - 1, out var target)) rule.Link = target.Id;
                        else if (TryInt(r.Modifier, out _)) { x.Report.Warning("aac.linkEmpty", p, $"link to slot {r.Modifier} (1-based) without an accessory; link null"); x.Report.Count("aac.linkEmpty"); }
                        else { rule.Modifier = r.Modifier; x.Report.Warning("aac.badLink", p, $"link modifier \"{r.Modifier}\" is not a slot number; kept as text"); }
                    }
                    else rule.Modifier = ModifierName(r.Modifier);
                    rules.Visibility.Add(rule);
                    x.Report.Count("aac.rules");
                }
                e.Rules = rules;
                x.Report.Count("aac.slots");
            }
            if (aac.OverrideDataRaw != null)
            {
                var o = new AacRuleOverrides();
                foreach (var r in aac.SuppressedRules) o.Suppressed.Add(Override(r));
                foreach (var r in aac.OverrideRules) o.Overrides.Add(Override(r));
                coord.RuleOverrides = o;
                x.Report.Count("aac.overrides", o.Suppressed.Count + o.Overrides.Count);
            }
        }

        static AacOverride Override(AdditionalAccessoryControlsData.CoordinateRule r) => new AacOverride
        {
            Rule = RuleName(r.Rule), Modifier = ModifierName(r.RuleModifier), OverrideRule = RuleName(r.OverrideRule), OverrideModifier = ModifierName(r.OverrideRuleModifier),
        };

        static string RuleName(AdditionalAccessoryControlsData.Rule r) => EnumName<AdditionalAccessoryControlsData.Rule>((int)r);

        /// <summary>The Modifier member name of a decimal text; other text (or an undefined number) as read; null stays null.</summary>
        static string ModifierName(string s)
        {
            if (s == null) return null;
            return TryInt(s, out int v) && Enum.IsDefined(typeof(AdditionalAccessoryControlsData.Modifier), v)
                ? ((AdditionalAccessoryControlsData.Modifier)v).ToString() : s;
        }
    }
}
