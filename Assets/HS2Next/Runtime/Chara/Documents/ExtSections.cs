// The plugin data of a document build: decoder-shaped objects (CardExtData) rebuilt
// from the typed sections of the chara and coord documents, the inverse, so the eleven appliers
// of Runtime/Chara/Ext run unchanged. Slots are build slots (AccessorySlotTable); images are the ResStore bytes of the
// `res` names (referenced, not copied). hs2.kkex is never read: Opaque and Resolve stay empty.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Data.Ext;
using HS2Next.Save;
using UnityEngine;
using SaveUncensor = HS2Next.Save.UncensorData;
using ExtUncensor = HS2Next.Data.Ext.UncensorData;

namespace HS2Next.Chara.Documents
{
    static class ExtSections
    {
        public static CardExtData Build(CharaDocument chara, CoordDocument coord, AccessorySlotTable slots, ResStore res, FindingList f)
        {
            var x = new CardExtData { IsCoordinate = false };
            x.MoreAccessories = MoreAccessories(slots);
            x.Abmx = Abmx(chara.Body, slots, f);
            x.Uncensor = Uncensor(chara.Body.Uncensor);
            x.MaterialEditor = MaterialEditor(chara, coord, slots, res, f);
            x.SkinOverlays = Ksox(chara, res, f);
            x.ClothesOverlays = Kcox(chara, coord, res, f);
            x.PushUp = PushUp(coord);
            x.DynamicBoneEditor = Dbe(slots);
            x.InvisibleBody = chara.Body.Visible ? null : new InvisibleBodyData { Version = 0, Visible = false };
            x.AdditionalAccessoryControls = Aac(coord, slots, f);
            return x;
        }

        static string N(int i) => i.ToString(CultureInfo.InvariantCulture);
        static Vector2 U(Vec2 v) => new Vector2(v.X, v.Y);
        static Vector3 U(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        static Vector4 U(Vec4 v) => new Vector4(v.X, v.Y, v.Z, v.W);
        static Color U(Color4 c) => new Color(c.R, c.G, c.B, c.A);

        static byte[] Bytes(ResStore res, string name, FindingList f, string path)
        {
            if (name == null) return null;
            var b = res?.Get(name);
            if (b == null) f?.Warning("build.resMissing", path, "resource " + name + " has no bytes; the image is left out");
            return b;
        }

        static string AccPath(AccessoryEntry e) => "coord.accessories[" + (e.Id ?? "?") + "]";

        // ------------------------------------------------------------------ MoreAccessories (slots >= 20)

        static MoreAccessoriesData MoreAccessories(AccessorySlotTable slots)
        {
            if (slots.MaxSlot < MoreAccessoriesData.FirstSlot) return null;
            var ma = new MoreAccessoriesData { Version = 2 };
            for (int s = MoreAccessoriesData.FirstSlot; s <= slots.MaxSlot; s++)
            {
                var p = new MoreAccessoriesData.AccessoryPart { Slot = s, Type = MoreAccessoriesData.EmptyType };
                if (slots.EntryBySlot.TryGetValue(s, out var e))
                {
                    p.Type = e.Category;
                    p.Id = ContentId.TryParse(e.Item, out var id) ? id.Id : 0;
                    p.ParentKey = e.Parent ?? "";
                    for (int j = 0; j < 2; j++)
                    {
                        var mv = j < e.Moves.Length && e.Moves[j] != null ? e.Moves[j] : new AccessoryMove();
                        p.AddMove[j, 0] = U(mv.Position);
                        p.AddMove[j, 1] = U(mv.Rotation);
                        p.AddMove[j, 2] = U(mv.Scale);
                    }
                    for (int l = 0; l < 4; l++)
                    {
                        var c = l < e.Colors.Length && e.Colors[l] != null ? e.Colors[l] : new AccessoryColor();
                        p.Color[l] = U(c.Color);
                        p.GlossPower[l] = c.Gloss;
                        p.MetallicPower[l] = c.Metallic;
                        p.SmoothnessPower[l] = c.Smoothness;
                    }
                    p.HideCategory = e.HideCategory;
                    p.HideTiming = e.HideTiming;
                    p.NoShake = e.NoShake;
                    p.Show = null;   // visibility is scene state (scene step 7g)
                }
                ma.Parts.Add(p);
            }
            return ma;
        }

        // ------------------------------------------------------------------ ABMX (version 2)

        static AbmxData Abmx(BodyData body, AccessorySlotTable slots, FindingList f)
        {
            var mods = new List<AbmxData.BoneModifier>();
            foreach (var b in body.BoneMods)
            {
                if (b == null) continue;
                int loc;
                if (b.Location == BodyBoneMod.LocationBodyTop) loc = (int)AbmxData.BoneLocation.BodyTop;
                else
                {
                    loc = (int)AbmxData.BoneLocation.Unknown;
                    if (b.Location != BodyBoneMod.LocationUnknown)
                        f?.Info("build.unknownEnum", "body.boneMods", "bone-modifier location \"" + b.Location + "\" read as unknown");
                }
                mods.Add(Modifier(b, loc));
            }
            foreach (var kv in slots.EntryBySlot)
            {
                var e = kv.Value;
                if (e.BoneMods == null || e.BoneMods.Count == 0) continue;
                if (kv.Key >= 20)
                {
                    f?.Warning("build.abmxSlotOutOfRange", AccPath(e) + ".boneMods", e.BoneMods.Count + " bone modifiers of an accessory at slot " + kv.Key + " dropped: ABMX addresses slots 0..19 only");
                    continue;
                }
                foreach (var b in e.BoneMods) if (b != null) mods.Add(Modifier(b, 10 + kv.Key));
            }
            if (mods.Count == 0) return null;
            var a = new AbmxData { Version = AbmxData.SupportedVersion };
            a.Modifiers.AddRange(mods);
            return a;
        }

        static AbmxData.BoneModifier Modifier(BoneModBase b, int location)
        {
            var m = new AbmxData.BoneModifier { BoneName = b.Bone, Location = location, HasLocation = true };
            m.CoordinateModifiers.Add(new AbmxData.BoneModifierData
            {
                Scale = U(b.Scale), Length = b.Length, Position = U(b.Position), Rotation = U(b.Rotation),
            });
            return m;
        }

        // ------------------------------------------------------------------ UncensorSelector

        static ExtUncensor Uncensor(SaveUncensor u)
        {
            if (u == null) return null;
            return new ExtUncensor
            {
                Version = 2, BodyGUID = u.Body, PenisGUID = u.Penis, BallsGUID = u.Balls, DisplayBalls = u.DisplayBalls,
                LegacyUncensorGUID = u.Legacy, DisplayPenis = null,   // derived at load
            };
        }

        // ------------------------------------------------------------------ MaterialEditor

        sealed class TexIds
        {
            readonly Dictionary<string, int> ids = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<int, byte[]> Textures = new Dictionary<int, byte[]>();
            readonly ResStore res;
            readonly FindingList f;
            public TexIds(ResStore res, FindingList f) { this.res = res; this.f = f; }

            /// <summary>TexIDs 0..n-1 in first-use order of the res names; a name without bytes gets no texture entry.</summary>
            public int? Of(string name, string path)
            {
                if (name == null) return null;
                if (ids.TryGetValue(name, out int id)) return id;
                id = ids.Count;
                ids[name] = id;
                var b = Bytes(res, name, f, path);
                if (b != null) Textures[id] = b;
                return id;
            }
        }

        static MaterialEditorData MaterialEditor(CharaDocument chara, CoordDocument coord, AccessorySlotTable slots, ResStore res, FindingList f)
        {
            var me = new MaterialEditorData { Version = 0 };
            var tex = new TexIds(res, f);
            Add(me, chara.MaterialOverrides, MaterialEditorData.ObjectType.Character, 0, tex, "materialOverrides", f);
            for (int k = 0; k < 4; k++)
                Add(me, chara.Hair.Parts[k].MaterialOverrides, MaterialEditorData.ObjectType.Hair, k, tex, "hair." + PartKeys.HairNames[k] + ".materialOverrides", f);
            for (int k = 0; k < ClothesSet.Count; k++)
                Add(me, coord.Clothes.Parts[k].MaterialOverrides, MaterialEditorData.ObjectType.Clothing, k, tex, "coord.clothes." + ClothesSet.Names[k] + ".materialOverrides", f);
            foreach (var kv in slots.EntryBySlot)
                Add(me, kv.Value.MaterialOverrides, MaterialEditorData.ObjectType.Accessory, kv.Key, tex, AccPath(kv.Value) + ".materialOverrides", f);
            if (!me.HasEdits) return null;
            foreach (var kv in tex.Textures) me.Textures[kv.Key] = kv.Value;
            return me;
        }

        static void Add(MaterialEditorData me, List<MaterialOverride> items, MaterialEditorData.ObjectType type, int slot, TexIds tex, string path, FindingList f)
        {
            if (items == null) return;
            foreach (var o in items)
            {
                if (o == null) continue;
                MaterialEditorData.Item it = Convert(o, tex, path, f);
                if (it == null) continue;
                it.ObjectType = type;
                it.CoordinateIndex = 0;
                it.Slot = slot;
                switch (it)
                {
                    case MaterialEditorData.MaterialCopy c: me.Copies.Add(c); break;
                    case MaterialEditorData.NameProperty n: me.NameProperties.Add(n); break;
                    case MaterialEditorData.ShaderEdit s: me.Shaders.Add(s); break;
                    case MaterialEditorData.RendererProperty r: me.RendererProperties.Add(r); break;
                    case MaterialEditorData.FloatProperty fl: me.FloatProperties.Add(fl); break;
                    case MaterialEditorData.KeywordProperty k: me.KeywordProperties.Add(k); break;
                    case MaterialEditorData.ColorProperty c: me.ColorProperties.Add(c); break;
                    case MaterialEditorData.VectorProperty v: me.VectorProperties.Add(v); break;
                    case MaterialEditorData.TextureProperty t: me.TextureProperties.Add(t); break;
                    case MaterialEditorData.CubemapProperty c: me.CubemapProperties.Add(c); break;
                    case MaterialEditorData.ProjectorProperty p: me.ProjectorProperties.Add(p); break;
                }
            }
        }

        static string Text(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Text(int v) => v.ToString(CultureInfo.InvariantCulture);

        static MaterialEditorData.Item Convert(MaterialOverride o, TexIds tex, string path, FindingList f)
        {
            switch (o)
            {
                case MeCopy c: return new MaterialEditorData.MaterialCopy { MaterialName = c.Material, MaterialCopyName = c.CopyName };
                case MeName n: return new MaterialEditorData.NameProperty { Renderer = n.Renderer, MaterialName = n.Material, Value = n.Value, ValueOriginal = n.Original };
                case MeShader s:
                    return new MaterialEditorData.ShaderEdit
                    {
                        MaterialName = s.Material, ShaderName = s.Shader, ShaderNameOriginal = s.ShaderOriginal, RenderQueue = s.RenderQueue, RenderQueueOriginal = s.RenderQueueOriginal,
                    };
                case MeRenderer r:
                {
                    if (!TryEnum<MaterialEditorData.RendererPropertyType>(r.Property, out int prop)) { Unknown(f, path, "renderer property", r.Property); return null; }
                    return new MaterialEditorData.RendererProperty
                    {
                        RendererName = r.Renderer, Property = (MaterialEditorData.RendererPropertyType)prop,
                        Value = r.ValueText ?? Text(r.Value),
                        ValueOriginal = r.OriginalText ?? (r.Original.HasValue ? Text(r.Original.Value) : null),
                    };
                }
                case MeFloat fl:
                    return new MaterialEditorData.FloatProperty
                    {
                        MaterialName = fl.Material, Property = fl.Property,
                        Value = fl.ValueText ?? Text(fl.Value),
                        ValueOriginal = fl.OriginalText ?? (fl.Original.HasValue ? Text(fl.Original.Value) : null),
                    };
                case MeKeyword k: return new MaterialEditorData.KeywordProperty { MaterialName = k.Material, Property = k.Property, Value = k.Value, ValueOriginal = k.Original };
                case MeColor c:
                    return new MaterialEditorData.ColorProperty { MaterialName = c.Material, Property = c.Property, Value = U(c.Value), ValueOriginal = c.Original.HasValue ? U(c.Original.Value) : (Color?)null };
                case MeVector v:
                    return new MaterialEditorData.VectorProperty { MaterialName = v.Material, Property = v.Property, Value = U(v.Value), ValueOriginal = v.Original.HasValue ? U(v.Original.Value) : (Vector4?)null };
                case MeTexture t:
                    return new MaterialEditorData.TextureProperty
                    {
                        MaterialName = t.Material, Property = t.Property, TexID = tex.Of(t.Image, path),
                        Offset = V2(t.Offset), OffsetOriginal = V2(t.OffsetOriginal), Scale = V2(t.Scale), ScaleOriginal = V2(t.ScaleOriginal),
                    };
                case MeCubemap cm: return new MaterialEditorData.CubemapProperty { MaterialName = cm.Material, Property = cm.Property, TexID = tex.Of(cm.Image, path) };
                case MeProjector p:
                {
                    if (!TryEnum<MeProjectorProperty>(p.Property, out int prop)) { Unknown(f, path, "projector property", p.Property); return null; }
                    return new MaterialEditorData.ProjectorProperty { ProjectorName = p.Projector, Property = prop, Value = p.Value, ValueOriginal = p.Original };
                }
            }
            Unknown(f, path, "item type", o.Type);
            return null;
        }

        static Vector2? V2(Vec2? v) => v.HasValue ? U(v.Value) : (Vector2?)null;

        /// <summary>A member name of <typeparamref name="T"/>, or the decimal text the importer writes for an undefined number.</summary>
        static bool TryEnum<T>(string s, out int value) where T : struct, Enum
        {
            value = 0;
            if (s == null) return false;
            if (Enum.TryParse<T>(s, false, out var e) && Enum.IsDefined(typeof(T), e) && !IsNumber(s)) { value = System.Convert.ToInt32(e); return true; }
            return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
        }

        static bool IsNumber(string s) => s.Length > 0 && (char.IsDigit(s[0]) || s[0] == '-' || s[0] == '+');

        static void Unknown(FindingList f, string path, string what, string value) =>
            f?.Info("build.unknownEnum", path, what + " \"" + value + "\" is not known; the element is skipped");

        // ------------------------------------------------------------------ KSOX (version 2)

        static SkinOverlayData Ksox(CharaDocument chara, ResStore res, FindingList f)
        {
            var k = new SkinOverlayData { Version = 2, EnableInStudioSkin = chara.StudioOverlays.Skin, EnableInStudioIris = chara.StudioOverlays.Eyes };
            foreach (var o in chara.Body.SkinOverlays)
            {
                if (o == null) continue;
                var t = o.TargetType;
                if (t == null) { Unknown(f, "body.skinOverlays", "overlay target", o.Target); continue; }
                var type = (SkinTexType)(int)t.Value;
                if (k.Textures.ContainsKey(type)) { f?.Info("build.duplicateOverlay", "body.skinOverlays", "a second overlay for " + o.Target + "; the first is kept"); continue; }
                var b = Bytes(res, o.Image, f, "body.skinOverlays." + o.Target);
                if (b == null) continue;
                k.Textures[type] = b;
            }
            if (k.Textures.Count == 0 && k.EnableInStudioSkin && k.EnableInStudioIris) return null;
            return k;
        }

        // ------------------------------------------------------------------ KCOX (version 2)

        static ClothesOverlayData Kcox(CharaDocument chara, CoordDocument coord, ResStore res, FindingList f)
        {
            var k = new ClothesOverlayData { Version = 2, EnableInStudio = chara.StudioOverlays.Clothes };
            for (int kind = 0; kind < ClothesSet.Count; kind++)
            {
                var set = coord.Clothes.Parts[kind].Overlays;
                if (set == null) continue;
                string n = ClothesOverlayData.MainClothesNames[kind];
                string p = "coord.clothes." + ClothesSet.Names[kind] + ".overlays";
                Ov(k, n, set.Main, res, f, p + ".main");
                Ov(k, "Colormask_" + n, set.ColorMask, res, f, p + ".colorMask");
                for (int j = 0; j < ClothesOverlaySet.PatternCount; j++)
                    Ov(k, "Pattern_" + N(j) + "_" + n, set.Patterns != null && j < set.Patterns.Length ? set.Patterns[j] : null, res, f, p + ".patterns[" + N(j) + "]");
                Ov(k, "Override_" + n, set.SourceReplacement, res, f, p + ".sourceReplacement");
                if (set.TextureSize.HasValue) k.TextureSizeOverride[n] = set.TextureSize.Value;
            }
            if (k.Overlays.Count == 0 && k.TextureSizeOverride.Count == 0 && k.EnableInStudio) return null;
            return k;
        }

        static void Ov(ClothesOverlayData k, string id, OverlayEntry o, ResStore res, FindingList f, string path)
        {
            if (o == null) return;
            var t = new ClothesTexData { TextureBytes = Bytes(res, o.Image, f, path), Override = o.ClearTarget, BlendingMode = o.Blend };
            if (t.IsEmpty) return;
            k.Overlays[id] = t;
        }

        // ------------------------------------------------------------------ pushUp

        static PushUpData PushUp(CoordDocument coord)
        {
            var bra = coord.Clothes[ClothesKind.InnerTop].PushUp;
            var top = coord.Clothes[ClothesKind.Top].PushUp;
            if (bra == null && top == null) return null;
            return new PushUpData
            {
                Version = 3,
                Bra = bra != null ? Set(bra) : PushUpData.PushUpSet.BraDefaults(),
                Top = top != null ? Set(top) : PushUpData.PushUpSet.TopDefaults(),
            };
        }

        static PushUpData.PushUpSet Set(PushUpSettings s) => new PushUpData.PushUpSet
        {
            EnablePushup = s.Enabled, Firmness = s.Firmness, Lift = s.Lift, PushTogether = s.PushTogether, Squeeze = s.Squeeze,
            CenterNipples = s.CenterNipples, Corset = s.Corset, FlattenNipples = s.FlattenNipples, HideNipples = s.HideNipples,
            HideAccessories = s.HideAccessories, CorsetHalf = s.CorsetHalf,
        };

        // ------------------------------------------------------------------ DynamicBoneEditor

        static DynamicBoneEditorData Dbe(AccessorySlotTable slots)
        {
            DynamicBoneEditorData d = null;
            foreach (var kv in slots.EntryBySlot)
            {
                var list = kv.Value.DynamicBones;
                if (list == null) continue;
                foreach (var b in list)
                {
                    if (b == null) continue;
                    d ??= new DynamicBoneEditorData { Version = 0 };
                    var o = b.Original ?? new DynamicBoneValues();
                    d.Entries.Add(new DynamicBoneEditorData.Entry
                    {
                        CoordinateIndex = 0, Slot = kv.Key, BoneName = b.Root,
                        FreezeAxis = b.FreezeAxis, Weight = b.Weight, Damping = b.Damping, Elasticity = b.Elasticity,
                        Stiffness = b.Stiffness, Inertia = b.Inertia, Radius = b.Radius,
                        FreezeAxisOriginal = o.FreezeAxis, WeightOriginal = o.Weight, DampingOriginal = o.Damping, ElasticityOriginal = o.Elasticity,
                        StiffnessOriginal = o.Stiffness, InertiaOriginal = o.Inertia, RadiusOriginal = o.Radius,
                    });
                }
            }
            return d;
        }

        // ------------------------------------------------------------------ AdditionalAccessoryControls

        static AdditionalAccessoryControlsData Aac(CoordDocument coord, AccessorySlotTable slots, FindingList f)
        {
            AdditionalAccessoryControlsData d = null;
            foreach (var kv in slots.EntryBySlot)
            {
                var e = kv.Value;
                var r = e.Rules;
                if (r == null) continue;
                d ??= new AdditionalAccessoryControlsData { Version = 1 };
                var sd = new AdditionalAccessoryControlsData.SlotData
                {
                    IsEmpty = false, SlotNumber = kv.Key, CharacterAccessory = r.CharacterAccessory, AccessoryName = r.Name,
                    AutoMatchBackHairColor = r.MatchHairColor, AdvancedParent = r.AdvancedParent,
                };
                foreach (var v in r.Visibility)
                {
                    if (v == null) continue;
                    if (!TryEnum<AdditionalAccessoryControlsData.Rule>(v.Rule, out int rule)) { Unknown(f, AccPath(e) + ".rules", "rule", v.Rule); continue; }
                    var rr = (AdditionalAccessoryControlsData.Rule)rule;
                    string mod;
                    if (rr == AdditionalAccessoryControlsData.Rule.ACCESSORY_LINK || rr == AdditionalAccessoryControlsData.Rule.ACCESSORY_INVERSE_LINK)
                    {
                        // The 1-based build slot of the linked accessory; "0" when the link names none. A link kept as text by
                        // the importer (aac.badLink: not a slot number) is written back as that text.
                        if (v.Link != null) mod = slots.SlotById.TryGetValue(v.Link, out int ts) ? N(ts + 1) : "0";
                        else mod = v.Modifier ?? "0";
                    }
                    else mod = ModifierText(v.Modifier);
                    sd.VisibilityRules.Add(new AdditionalAccessoryControlsData.VisibilityRule { Rule = rr, Modifier = mod });
                }
                d.Slots.Add(sd);
            }
            var ov = coord.RuleOverrides;
            if (ov != null)
            {
                d ??= new AdditionalAccessoryControlsData { Version = 1 };
                d.OverrideDataRaw = Array.Empty<byte>();
                foreach (var o in ov.Suppressed) if (Rule(o, f) is AdditionalAccessoryControlsData.CoordinateRule c) d.SuppressedRules.Add(c);
                foreach (var o in ov.Overrides) if (Rule(o, f) is AdditionalAccessoryControlsData.CoordinateRule c) d.OverrideRules.Add(c);
            }
            return d;
        }

        static AdditionalAccessoryControlsData.CoordinateRule Rule(AacOverride o, FindingList f)
        {
            if (o == null) return null;
            if (!TryEnum<AdditionalAccessoryControlsData.Rule>(o.Rule, out int r)) { Unknown(f, "coord.ruleOverrides", "rule", o.Rule); return null; }
            if (!TryEnum<AdditionalAccessoryControlsData.Rule>(o.OverrideRule, out int orr)) { Unknown(f, "coord.ruleOverrides", "rule", o.OverrideRule); return null; }
            return new AdditionalAccessoryControlsData.CoordinateRule
            {
                Rule = (AdditionalAccessoryControlsData.Rule)r, RuleModifier = ModifierText(o.Modifier),
                OverrideRule = (AdditionalAccessoryControlsData.Rule)orr, OverrideRuleModifier = ModifierText(o.OverrideModifier),
            };
        }

        /// <summary>The decimal text of a Modifier member name; any other text (an undefined number, plugin text) as stored; null stays null.</summary>
        static string ModifierText(string s)
        {
            if (s == null || IsNumber(s)) return s;
            return Enum.TryParse<AdditionalAccessoryControlsData.Modifier>(s, false, out var m) && Enum.IsDefined(typeof(AdditionalAccessoryControlsData.Modifier), m)
                ? N((int)m) : s;
        }
    }
}
