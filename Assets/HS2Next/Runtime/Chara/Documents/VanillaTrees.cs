// The vanilla blocks of a document build: every row inverted, the
// document member written under its HS2 key into the block it came from, in the value forms MsgPack.Read produces and
// the Node accessors read (float, long, bool, string, List<object> of floats for colours and vectors, Dictionary<object,
// object> with string keys). A `ref` becomes the long ContentId.Id; its part key is registered with the resolver, which
// hands the assembler the row the importer resolved. Documents hold post-migration values: no `version` keys.
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Save;

namespace HS2Next.Chara.Documents
{
    static class VanillaTrees
    {
        public const string Marker = "【AIS_Chara】";

        /// <summary>The card of a document build: header, Parameter, Custom blocks, Coordinate blocks.</summary>
        public static Card Build(CharaDocument chara, CoordDocument coord, AccessorySlotTable slots, DocumentPartResolver parts, FindingList f)
        {
            int sex = chara.Profile.Sex;
            var card = new Card
            {
                Product = 100, Marker = Marker, Version = "1.0.0", Path = null,
                KKEx = null, KKExRaw = null, KKExVersion = null, Status = null,
            };
            card.Parameter = new Dictionary<object, object>
            {
                ["fullname"] = chara.Profile.Name ?? "",
                ["sex"] = (long)sex,
                ["futanari"] = chara.Profile.Futanari,
            };
            card.Face = Face(chara.Face, sex, parts);
            card.Body = Body(chara.Body, sex, parts);
            card.Hair = Hair(chara.Hair, parts, f);
            card.Clothes = Clothes(coord, parts);
            card.Accessory = Accessories(slots, parts);
            return card;
        }

        // ------------------------------------------------------------------ value forms

        static object Col(Color4 c) => new List<object> { c.R, c.G, c.B, c.A };
        static object V2(Vec2 v) => new List<object> { v.X, v.Y };
        static object V3(Vec3 v) => new List<object> { v.X, v.Y, v.Z };
        static object V4(Vec4 v) => new List<object> { v.X, v.Y, v.Z, v.W };
        static object Floats(float[] a)
        {
            var l = new List<object>(a?.Length ?? 0);
            if (a != null) foreach (var x in a) l.Add(x);
            return l;
        }

        /// <summary>The long id of a reference, registering it under <paramref name="key"/>.</summary>
        static object Ref(DocumentPartResolver parts, string key, string refText, int category, string path)
        {
            parts.Register(key, refText, category, path);
            return (long)(ContentId.TryParse(refText, out var id) ? id.Id : 0);
        }

        // ------------------------------------------------------------------ face (Custom chunk 0)

        static Dictionary<object, object> Face(FaceData d, int sex, DocumentPartResolver parts)
        {
            bool male = sex == 0;
            var m = new Dictionary<object, object>();
            m["shapeValueFace"] = Floats(d.Shape);
            m["headId"] = Ref(parts, PartKeys.FaceHead, d.Head, male ? 110 : 210, "face.head");
            m["skinId"] = Ref(parts, PartKeys.FaceSkin, d.Skin, male ? 111 : 211, "face.skin");
            m["detailId"] = Ref(parts, PartKeys.FaceDetail, d.Detail.Item, male ? 112 : 212, "face.detail.item");
            m["detailPower"] = d.Detail.Power;
            m["eyebrowId"] = Ref(parts, PartKeys.FaceEyebrow, d.Eyebrow.Item, 314, "face.eyebrow.item");
            m["eyebrowColor"] = Col(d.Eyebrow.Color);
            m["eyebrowLayout"] = V4(d.Eyebrow.Layout);
            m["eyebrowTilt"] = d.Eyebrow.Tilt;
            var pupils = new List<object>(2);
            for (int i = 0; i < 2; i++)
            {
                var e = i == 0 ? d.Eyes.Left : d.Eyes.Right;
                string p = i == 0 ? "face.eyes.left" : "face.eyes.right";
                pupils.Add(new Dictionary<object, object>
                {
                    ["whiteColor"] = Col(e.White),
                    ["pupilId"] = Ref(parts, PartKeys.EyeIris(i), e.Iris.Item, 317, p + ".iris.item"),
                    ["pupilColor"] = Col(e.Iris.Color),
                    ["pupilW"] = e.Iris.Size.X,
                    ["pupilH"] = e.Iris.Size.Y,
                    ["pupilEmission"] = e.Iris.Emission,
                    ["blackId"] = Ref(parts, PartKeys.EyePupil(i), e.Pupil.Item, 318, p + ".pupil.item"),
                    ["blackColor"] = Col(e.Pupil.Color),
                    ["blackW"] = e.Pupil.Size.X,
                    ["blackH"] = e.Pupil.Size.Y,
                });
            }
            m["pupil"] = pupils;
            m["pupilSameSetting"] = d.Eyes.Linked;
            m["pupilY"] = d.Eyes.OffsetY;
            m["whiteShadowScale"] = d.Eyes.ShadowScale;
            m["hlId"] = Ref(parts, PartKeys.FaceHighlight, d.Eyes.Highlight.Item, 319, "face.eyes.highlight.item");
            m["hlColor"] = Col(d.Eyes.Highlight.Color);
            m["hlLayout"] = V4(d.Eyes.Highlight.Layout);
            m["hlTilt"] = d.Eyes.Highlight.Tilt;
            m["eyelashesId"] = Ref(parts, PartKeys.FaceEyelashes, d.Eyelashes.Item, 315, "face.eyelashes.item");
            m["eyelashesColor"] = Col(d.Eyelashes.Color);
            m["moleId"] = Ref(parts, PartKeys.FaceMole, d.Mole.Item, 323, "face.mole.item");
            m["moleColor"] = Col(d.Mole.Color);
            m["moleLayout"] = V4(d.Mole.Layout);
            var mk = d.Makeup;
            var makeup = new Dictionary<object, object>
            {
                ["eyeshadowId"] = Ref(parts, PartKeys.FaceEyeshadow, mk.Eyeshadow.Item, 316, "face.makeup.eyeshadow.item"),
                ["eyeshadowColor"] = Col(mk.Eyeshadow.Color),
                ["eyeshadowGloss"] = mk.Eyeshadow.Gloss,
                ["cheekId"] = Ref(parts, PartKeys.FaceCheek, mk.Cheek.Item, 320, "face.makeup.cheek.item"),
                ["cheekColor"] = Col(mk.Cheek.Color),
                ["cheekGloss"] = mk.Cheek.Gloss,
                ["lipId"] = Ref(parts, PartKeys.FaceLip, mk.Lip.Item, 322, "face.makeup.lip.item"),
                ["lipColor"] = Col(mk.Lip.Color),
                ["lipGloss"] = mk.Lip.Gloss,
            };
            var paints = new List<object>(2);
            for (int i = 0; i < 2; i++)
                paints.Add(Paint(mk.Paint[i], parts, PartKeys.FacePaint(i), "chara:face.makeup.paint[" + N(i) + "].layoutItem", 7, "face.makeup.paint[" + N(i) + "]"));
            makeup["paintInfo"] = paints;
            m["makeup"] = makeup;
            // Written for both sexes (the assembler reads it for males only); category 121 either way.
            m["beardId"] = Ref(parts, PartKeys.FaceBeard, d.Beard.Item, 121, "face.beard.item");
            m["beardColor"] = Col(d.Beard.Color);
            return m;
        }

        static Dictionary<object, object> Paint(Save.Paint p, DocumentPartResolver parts, string itemKey, string layoutKey, int layoutCategory, string path) =>
            new Dictionary<object, object>
            {
                ["id"] = Ref(parts, itemKey, p.Item, 313, path + ".item"),
                ["color"] = Col(p.Color),
                ["glossPower"] = p.Gloss,
                ["metallicPower"] = p.Metallic,
                ["layoutId"] = Ref(parts, layoutKey, p.LayoutItem, layoutCategory, path + ".layoutItem"),
                ["layout"] = V4(p.Layout),
                ["rotation"] = p.Rotation,
            };

        static string N(int i) => i.ToString(CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ body (Custom chunk 1)

        static Dictionary<object, object> Body(BodyData d, int sex, DocumentPartResolver parts)
        {
            bool male = sex == 0;
            var m = new Dictionary<object, object>();
            m["shapeValueBody"] = Floats(d.Shape);
            m["bustSoftness"] = d.Bust.Softness;
            m["bustWeight"] = d.Bust.Weight;
            m["skinId"] = Ref(parts, PartKeys.BodySkin, d.Skin.Item, male ? 131 : 231, "body.skin.item");
            m["detailId"] = Ref(parts, PartKeys.BodyDetail, d.Detail.Item, male ? 132 : 232, "body.detail.item");
            m["detailPower"] = d.Detail.Power;
            m["skinColor"] = Col(d.Skin.Color);
            m["skinGlossPower"] = d.Skin.Gloss;
            m["skinMetallicPower"] = d.Skin.Metallic;
            m["sunburnId"] = Ref(parts, PartKeys.BodySunburn, d.Sunburn.Item, male ? 133 : 233, "body.sunburn.item");
            m["sunburnColor"] = Col(d.Sunburn.Color);
            var paints = new List<object>(2);
            for (int i = 0; i < 2; i++)
                paints.Add(Paint(d.Paint[i], parts, PartKeys.BodyPaint(i), PartKeys.BodyPaintLayout(i), 8, "body.paint[" + N(i) + "]"));
            m["paintInfo"] = paints;
            m["nipId"] = Ref(parts, PartKeys.BodyNipple, d.Nipple.Item, 334, "body.nipple.item");
            m["nipColor"] = Col(d.Nipple.Color);
            m["nipGlossPower"] = d.Nipple.Gloss;
            m["areolaSize"] = d.Nipple.Areola;
            m["underhairId"] = Ref(parts, PartKeys.BodyUnderhair, d.Underhair.Item, 335, "body.underhair.item");
            m["underhairColor"] = Col(d.Underhair.Color);
            m["nailColor"] = Col(d.Nail.Color);
            m["nailGlossPower"] = d.Nail.Gloss;
            return m;
        }

        // ------------------------------------------------------------------ hair (Custom chunk 2)

        static Dictionary<object, object> Hair(HairData d, DocumentPartResolver parts, FindingList f)
        {
            var m = new Dictionary<object, object>
            {
                ["sameSetting"] = d.Editor.SameSetting,
                ["autoSetting"] = d.Editor.AutoSetting,
                ["ctrlTogether"] = d.Editor.CtrlTogether,
                ["shaderType"] = (long)d.ShaderType,
            };
            var list = new List<object>(4);
            for (int k = 0; k < 4; k++)
            {
                var h = d.Parts[k];
                string name = PartKeys.HairNames[k], p = "hair." + name;
                var acs = new List<object>(HairPart.AccessoryColorCount);
                for (int i = 0; i < HairPart.AccessoryColorCount; i++)
                    acs.Add(new Dictionary<object, object> { ["color"] = Col(i < h.AccessoryColors.Length ? h.AccessoryColors[i] : Color4.White) });
                var bundles = new Dictionary<object, object>();
                foreach (var b in h.Bundles)
                {
                    if (b == null) continue;
                    long idx = b.Index;
                    if (bundles.ContainsKey(idx)) f?.Info("build.duplicateBundle", p + ".bundles", "two bundles with index " + idx + "; the later one is used");
                    bundles[idx] = new Dictionary<object, object> { ["moveRate"] = V3(b.Move), ["rotRate"] = V3(b.Rotate), ["noShake"] = b.NoShake };
                }
                list.Add(new Dictionary<object, object>
                {
                    ["id"] = Ref(parts, PartKeys.Hair(k), h.Item, 300 + k, p + ".item"),
                    ["baseColor"] = Col(h.BaseColor),
                    ["topColor"] = Col(h.TopColor),
                    ["underColor"] = Col(h.UnderColor),
                    ["specular"] = Col(h.Specular),
                    ["metallic"] = h.Metallic,
                    ["smoothness"] = h.Smoothness,
                    ["acsColorInfo"] = acs,
                    ["dictBundle"] = bundles,
                    ["meshType"] = Ref(parts, "chara:hair." + name + ".mesh.item", h.Mesh.Item, 306, p + ".mesh.item"),
                    ["meshColor"] = Col(h.Mesh.Color),
                    ["meshLayout"] = V4(h.Mesh.Layout),
                });
            }
            m["parts"] = list;
            return m;
        }

        // ------------------------------------------------------------------ clothes (Coordinate chunk 0)

        static Dictionary<object, object> Clothes(CoordDocument coord, DocumentPartResolver parts)
        {
            var cats = coord.Sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories;
            var list = new List<object>(ClothesSet.Count);
            for (int k = 0; k < ClothesSet.Count; k++)
            {
                var c = coord.Clothes.Parts[k];
                string p = "coord.clothes." + ClothesSet.Names[k];
                var colors = new List<object>(ClothesPart.ColorCount);
                for (int j = 0; j < ClothesPart.ColorCount; j++)
                {
                    var cc = c.Colors[j];
                    colors.Add(new Dictionary<object, object>
                    {
                        ["baseColor"] = Col(cc.Base),
                        ["pattern"] = Ref(parts, PartKeys.ClothesPattern(k, j), cc.Pattern, DocumentPartResolver.PatternCategory, p + ".colors[" + N(j) + "].pattern"),
                        ["layout"] = V4(cc.Layout),
                        ["rotation"] = cc.Rotation,
                        ["patternColor"] = Col(cc.PatternColor),
                        ["glossPower"] = cc.Gloss,
                        ["metallicPower"] = cc.Metallic,
                    });
                }
                var hide = c.HideOptions ?? new bool[2];
                list.Add(new Dictionary<object, object>
                {
                    ["id"] = Ref(parts, PartKeys.Clothes(k), c.Item, cats[k], p + ".item"),
                    ["colorInfo"] = colors,
                    ["breakRate"] = c.BreakRate,
                    ["hideOpt"] = new List<object> { hide.Length > 0 && hide[0], hide.Length > 1 && hide[1] },
                });
            }
            return new Dictionary<object, object> { ["parts"] = list };
        }

        // ------------------------------------------------------------------ accessories (Coordinate chunk 1)

        /// <summary>Exactly 20 parts (slots 0..19 of the slot table); empty slots as type 350. Slots ≥ 20 go to MoreAccessories.</summary>
        static Dictionary<object, object> Accessories(AccessorySlotTable slots, DocumentPartResolver parts)
        {
            var list = new List<object>(20);
            for (int s = 0; s < 20; s++)
            {
                if (slots.EntryBySlot.TryGetValue(s, out var e)) list.Add(Accessory(e, s, parts));
                else list.Add(EmptyAccessory());
            }
            // Slots >= 20 are built through MoreAccessories (ExtSections) and looked up with the same part keys.
            foreach (var kv in slots.EntryBySlot)
                if (kv.Key >= 20)
                    parts.Register(PartKeys.Accessory(kv.Key), kv.Value.Item, kv.Value.Category, "coord.accessories[" + (kv.Value.Id ?? "?") + "].item");
            return new Dictionary<object, object> { ["parts"] = list };
        }

        static Dictionary<object, object> Accessory(AccessoryEntry e, int slot, DocumentPartResolver parts)
        {
            var colors = new List<object>(AccessoryEntry.ColorCount);
            for (int i = 0; i < AccessoryEntry.ColorCount; i++)
            {
                var c = e.Colors[i];
                colors.Add(new Dictionary<object, object>
                {
                    ["color"] = Col(c.Color), ["glossPower"] = c.Gloss, ["metallicPower"] = c.Metallic, ["smoothnessPower"] = c.Smoothness,
                });
            }
            return new Dictionary<object, object>
            {
                ["type"] = (long)e.Category,
                ["id"] = Ref(parts, PartKeys.Accessory(slot), e.Item, e.Category, "coord.accessories[" + (e.Id ?? "?") + "].item"),
                ["parentKey"] = e.Parent ?? "",
                ["addMove"] = AddMove(e.Moves),
                ["colorInfo"] = colors,
                ["hideCategory"] = (long)e.HideCategory,
                ["hideTiming"] = (long)e.HideTiming,
                ["noShake"] = e.NoShake,
            };
        }

        static Dictionary<object, object> EmptyAccessory()
        {
            var colors = new List<object>(AccessoryEntry.ColorCount);
            for (int i = 0; i < AccessoryEntry.ColorCount; i++)
                colors.Add(new Dictionary<object, object> { ["color"] = Col(Color4.White), ["glossPower"] = 0.5f, ["metallicPower"] = 0.5f, ["smoothnessPower"] = 0.5f });
            return new Dictionary<object, object>
            {
                ["type"] = 350L, ["id"] = 0L, ["parentKey"] = "",
                ["addMove"] = AddMove(new[] { new AccessoryMove(), new AccessoryMove() }),
                ["colorInfo"] = colors,
                ["hideCategory"] = 0L, ["hideTiming"] = 1L, ["noShake"] = false,
            };
        }

        /// <summary>MessagePack-CSharp's two-dimensional form [2, 3, [p0, r0, s0, p1, r1, s1]] (row-major; CharaAssembler.ReadAddMove).</summary>
        static object AddMove(AccessoryMove[] moves)
        {
            var flat = new List<object>(6);
            for (int j = 0; j < 2; j++)
            {
                var mv = moves != null && j < moves.Length && moves[j] != null ? moves[j] : new AccessoryMove();
                flat.Add(V3(mv.Position));
                flat.Add(V3(mv.Rotation));
                flat.Add(V3(mv.Scale));
            }
            return new List<object> { 2L, 3L, flat };
        }
    }
}
