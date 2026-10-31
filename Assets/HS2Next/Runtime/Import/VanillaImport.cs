// Vanilla field mapping from the MessagePack trees of the HS2 card objects into the chara
// and coord documents, with MemberInit values for missing keys and fixed-length arrays and HS2's sub-version
// migrations. Reference fields are collected as RefFields and resolved later, after the migrations.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;

namespace HS2Next.Import
{
    /// <summary>State of one import shared by the mapping steps.</summary>
    sealed class ImportContext
    {
        public ImportReport Report;
        public RawReader Raw;
        public ResolveCatalog Catalog;
        public readonly List<RefField> Refs = new List<RefField>();
        /// <summary>Reference fields the migrations change (by name: `face.beard`, `hair.0`, `hair.1`).</summary>
        public readonly Dictionary<string, RefField> Named = new Dictionary<string, RefField>(StringComparer.Ordinal);
        /// <summary>Accessory entries by absolute HS2 slot.</summary>
        public readonly SortedDictionary<int, AccessoryEntry> Slots = new SortedDictionary<int, AccessoryEntry>();
        public ResStore Res;
        public string Sha256;
        /// <summary>Prefix of coord document paths in findings: "coord/" for a character card, "" for a coordinate file.</summary>
        public string CoordPath = "";
        /// <summary>Character cards prefix clothes and accessory properties with "outfit.".</summary>
        public bool Outfit;

        public RefField AddRef(string path, int raw, string property, int category, Action<string> set, string name = null)
        {
            var f = new RefField { Raw = raw, Property = property, Category = category, Path = path, Set = set };
            Refs.Add(f);
            if (name != null) Named[name] = f;
            return f;
        }

        public string AccessoryId(int slot) => JsonText.DerivedId(Sha256 + "/acc/" + slot.ToString(CultureInfo.InvariantCulture));
    }

    static class VanillaImport
    {
        static string N(int i) => i.ToString(CultureInfo.InvariantCulture);

        // ---------- known keys of HS2's MessagePack classes (property names; ESF adds ExtendedSaveData) ----------
        const string Esd = "ExtendedSaveData";
        static readonly HashSet<string> FaceKeys = RawReader.Keys("version", "shapeValueFace", "headId", "skinId", "detailId", "detailPower",
            "eyebrowId", "eyebrowColor", "eyebrowLayout", "eyebrowTilt", "pupil", "pupilSameSetting", "pupilY", "hlId", "hlColor", "hlLayout",
            "hlTilt", "whiteShadowScale", "eyelashesId", "eyelashesColor", "moleId", "moleColor", "moleLayout", "makeup", "beardId", "beardColor", Esd);
        static readonly HashSet<string> EyeKeys = RawReader.Keys("whiteColor", "pupilId", "pupilColor", "pupilW", "pupilH", "pupilEmission",
            "blackId", "blackColor", "blackW", "blackH", Esd);
        static readonly HashSet<string> MakeupKeys = RawReader.Keys("eyeshadowId", "eyeshadowColor", "eyeshadowGloss", "cheekId", "cheekColor",
            "cheekGloss", "lipId", "lipColor", "lipGloss", "paintInfo", Esd);
        static readonly HashSet<string> PaintKeys = RawReader.Keys("id", "color", "glossPower", "metallicPower", "layoutId", "layout", "rotation", Esd);
        static readonly HashSet<string> BodyKeys = RawReader.Keys("version", "shapeValueBody", "bustSoftness", "bustWeight", "skinId", "detailId",
            "detailPower", "skinColor", "skinGlossPower", "skinMetallicPower", "sunburnId", "sunburnColor", "paintInfo", "nipId", "nipColor",
            "nipGlossPower", "areolaSize", "underhairId", "underhairColor", "nailColor", "nailGlossPower", Esd);
        static readonly HashSet<string> HairKeys = RawReader.Keys("version", "sameSetting", "autoSetting", "ctrlTogether", "parts", "kind", "shaderType", Esd);
        static readonly HashSet<string> HairPartKeys = RawReader.Keys("id", "baseColor", "topColor", "underColor", "specular", "metallic", "smoothness",
            "acsColorInfo", "bundleId", "dictBundle", "meshType", "meshColor", "meshLayout", Esd);
        static readonly HashSet<string> ColorKeys = RawReader.Keys("color", Esd);
        static readonly HashSet<string> BundleKeys = RawReader.Keys("moveRate", "rotRate", "noShake", Esd);
        static readonly HashSet<string> ClothesKeys = RawReader.Keys("version", "parts", Esd);
        static readonly HashSet<string> ClothesPartKeys = RawReader.Keys("id", "colorInfo", "breakRate", "hideOpt", Esd);
        static readonly HashSet<string> ClothesColorKeys = RawReader.Keys("baseColor", "pattern", "layout", "rotation", "patternColor", "glossPower", "metallicPower", Esd);
        static readonly HashSet<string> AccessoryKeys = RawReader.Keys("version", "parts", Esd);
        static readonly HashSet<string> AccessoryPartKeys = RawReader.Keys("type", "id", "parentKey", "addMove", "colorInfo", "hideCategory", "hideTiming", "noShake", Esd);
        static readonly HashSet<string> AccessoryColorKeys = RawReader.Keys("color", "glossPower", "metallicPower", "smoothnessPower", Esd);
        static readonly HashSet<string> ParameterKeys = RawReader.Keys("version", "sex", "fullname", "personality", "birthMonth", "birthDay", "voiceRate", "hsWish", "futanari", Esd);
        static readonly HashSet<string> Parameter2Keys = RawReader.Keys("version", "personality", "voiceRate", "trait", "mind", "hAttribute", Esd);

        // HS2's current object versions (ChaFileDefine): a missing version key means the current version.
        static readonly Version FaceVersion = new Version(0, 0, 2), BodyVersion = new Version(0, 0, 1), HairVersion = new Version(0, 0, 3),
            ParameterVersion = new Version(0, 0, 1);

        /// <summary>The object's version: null when the key is missing (current, no migration); an unparseable string is
        /// treated as current and reported.</summary>
        static Version ObjectVersion(ImportContext x, object map, string obj)
        {
            if (!x.Raw.Has(map, "version")) return null;
            var s = x.Raw.S(map, "version", null, obj + ".version");
            if (s != null && Version.TryParse(s, out var v)) return v;
            x.Report.Warning("migrate.badVersion", obj + ".version", $"version \"{s}\" is not parseable; treated as current");
            return null;
        }

        static void Migrated(ImportContext x, string obj, string version) =>
            x.Report.Info("migrate." + obj + "." + version, obj, "HS2 sub-version migration applied");

        static float Clamp01(float t) => t < 0f ? 0f : t > 1f ? 1f : t;
        static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;

        // ===================================================================================== profile

        public static void Profile(ImportContext x, object param, object param2, Profile d)
        {
            var r = x.Raw;
            const string P = "profile";
            r.CheckKeys(param, ParameterKeys, "Parameter");
            d.Name = r.S(param, "fullname", "", P + ".name");
            int sex = r.I(param, "sex", 0, P + ".sex");
            if (sex != 0 && sex != 1)
            {
                x.Report.Warning("card.badSex", P + ".sex", $"sex {sex} is neither 0 nor 1; stored as 1");
                sex = 1;
            }
            d.Sex = sex;
            d.Futanari = r.B(param, "futanari", false, P + ".futanari");
            var h = new Hs2Profile
            {
                Personality = r.I(param, "personality", 0, P + ".hs2.personality"),
                BirthMonth = r.I(param, "birthMonth", 1, P + ".hs2.birthMonth"),
                BirthDay = r.I(param, "birthDay", 1, P + ".hs2.birthDay"),
                VoiceRate = r.F(param, "voiceRate", 0.5f, P + ".hs2.voiceRate"),
            };
            if (RawReader.Map(param)?.TryGetValue("hsWish", out var wishes) == true && wishes != null)
            {
                if (wishes is List<object> wl)
                    foreach (var w in wl)
                    {
                        if (w is long l) h.Wishes.Add((int)l);
                        else x.Report.Warning("card.badValue", P + ".hs2.wishes", "hsWish element is not an integer; dropped");
                    }
                else x.Report.Warning("card.badValue", P + ".hs2.wishes", "hsWish is not an array; MemberInit value used");
            }
            var v = ObjectVersion(x, param, "parameter");
            if (v != null && v < ParameterVersion)
            {
                h.Wishes.Clear();
                Migrated(x, "parameter", "0.0.1");
            }
            if (param2 != null)
            {
                r.CheckKeys(param2, Parameter2Keys, "Parameter2");
                h.Parameter2 = new Hs2Parameter2
                {
                    Personality = r.I(param2, "personality", 0, P + ".hs2.parameter2.personality"),
                    VoiceRate = r.F(param2, "voiceRate", 0.5f, P + ".hs2.parameter2.voiceRate"),
                    Trait = r.I(param2, "trait", 0, P + ".hs2.parameter2.trait"),
                    Mind = r.I(param2, "mind", 0, P + ".hs2.parameter2.mind"),
                    HAttribute = r.I(param2, "hAttribute", 0, P + ".hs2.parameter2.hAttribute"),
                };
            }
            d.Hs2 = h;
        }

        // ===================================================================================== face

        public static void Face(ImportContext x, object face, FaceData d, int sex)
        {
            var r = x.Raw;
            r.CheckKeys(face, FaceKeys, "ChaFileFace");
            bool male = sex == 0;
            d.Shape = r.FloatArray(face, "shapeValueFace", d.Shape, "face.shape");
            x.AddRef("face.head", r.I(face, "headId", 0, "face.head"), "ChaFileFace.headId", male ? 110 : 210, s => d.Head = s);
            x.AddRef("face.skin", r.I(face, "skinId", 0, "face.skin"), "ChaFileFace.skinId", male ? 111 : 211, s => d.Skin = s);
            x.AddRef("face.detail.item", r.I(face, "detailId", 0, "face.detail.item"), "ChaFileFace.detailId", male ? 112 : 212, s => d.Detail.Item = s);
            d.Detail.Power = r.F(face, "detailPower", 1f, "face.detail.power");

            x.AddRef("face.eyebrow.item", r.I(face, "eyebrowId", 0, "face.eyebrow.item"), "ChaFileFace.eyebrowId", 314, s => d.Eyebrow.Item = s);
            d.Eyebrow.Color = r.Col(face, "eyebrowColor", d.Eyebrow.Color, "face.eyebrow.color");
            d.Eyebrow.Layout = r.V4(face, "eyebrowLayout", d.Eyebrow.Layout, "face.eyebrow.layout");
            d.Eyebrow.Tilt = r.F(face, "eyebrowTilt", 0.5f, "face.eyebrow.tilt");

            var pupils = r.Fixed(face, "pupil", 2, "face.eyes.pupil");
            for (int i = 0; i < 2; i++)
            {
                var m = pupils[i];
                var e = i == 0 ? d.Eyes.Left : d.Eyes.Right;
                string p = i == 0 ? "face.eyes.left" : "face.eyes.right";
                r.CheckKeys(m, EyeKeys, "ChaFileFace.pupil[" + i + "]");
                e.White = r.Col(m, "whiteColor", e.White, p + ".white");
                x.AddRef(p + ".iris.item", r.I(m, "pupilId", 0, p + ".iris.item"), "ChaFileFace.Eye" + (i + 1), 317, s => e.Iris.Item = s);
                e.Iris.Color = r.Col(m, "pupilColor", e.Iris.Color, p + ".iris.color");
                e.Iris.Size = new Vec2(r.F(m, "pupilW", 0.666f, p + ".iris.size"), r.F(m, "pupilH", 0.666f, p + ".iris.size"));
                e.Iris.Emission = r.F(m, "pupilEmission", 0f, p + ".iris.emission");
                x.AddRef(p + ".pupil.item", r.I(m, "blackId", 0, p + ".pupil.item"), "ChaFileFace.EyeBlack" + (i + 1), 318, s => e.Pupil.Item = s);
                e.Pupil.Color = r.Col(m, "blackColor", e.Pupil.Color, p + ".pupil.color");
                e.Pupil.Size = new Vec2(r.F(m, "blackW", 0.8333f, p + ".pupil.size"), r.F(m, "blackH", 0.8333f, p + ".pupil.size"));
            }
            d.Eyes.Linked = r.B(face, "pupilSameSetting", true, "face.eyes.linked");
            d.Eyes.OffsetY = r.F(face, "pupilY", 0.5f, "face.eyes.offsetY");
            d.Eyes.ShadowScale = r.F(face, "whiteShadowScale", 0.5f, "face.eyes.shadowScale");
            x.AddRef("face.eyes.highlight.item", r.I(face, "hlId", 0, "face.eyes.highlight.item"), "ChaFileFace.hlId", 319, s => d.Eyes.Highlight.Item = s);
            d.Eyes.Highlight.Color = r.Col(face, "hlColor", d.Eyes.Highlight.Color, "face.eyes.highlight.color");
            d.Eyes.Highlight.Layout = r.V4(face, "hlLayout", d.Eyes.Highlight.Layout, "face.eyes.highlight.layout");
            d.Eyes.Highlight.Tilt = r.F(face, "hlTilt", 0.5f, "face.eyes.highlight.tilt");

            x.AddRef("face.eyelashes.item", r.I(face, "eyelashesId", 0, "face.eyelashes.item"), "ChaFileFace.eyelashesId", 315, s => d.Eyelashes.Item = s);
            d.Eyelashes.Color = r.Col(face, "eyelashesColor", d.Eyelashes.Color, "face.eyelashes.color");
            x.AddRef("face.mole.item", r.I(face, "moleId", 0, "face.mole.item"), "ChaFileFace.moleId", 323, s => d.Mole.Item = s);
            d.Mole.Color = r.Col(face, "moleColor", d.Mole.Color, "face.mole.color");
            d.Mole.Layout = r.V4(face, "moleLayout", d.Mole.Layout, "face.mole.layout");

            object makeup = RawReader.Map(face) != null && RawReader.Map(face).TryGetValue("makeup", out var mk) ? mk : null;
            r.CheckKeys(makeup, MakeupKeys, "ChaFileFace.makeup");
            Layer(x, makeup, "eyeshadow", 316, d.Makeup.Eyeshadow);
            Layer(x, makeup, "cheek", 320, d.Makeup.Cheek);
            Layer(x, makeup, "lip", 322, d.Makeup.Lip);
            var paints = r.Fixed(makeup, "paintInfo", 2, "face.makeup.paint");
            for (int i = 0; i < 2; i++) Paint(x, paints[i], d.Makeup.Paint[i], "face.makeup.paint[" + i + "]", "MakeupInfo", i, 7);

            // Sideloader's female face table has no beardId: a female card's beard takes the compatibility path.
            x.AddRef("face.beard.item", r.I(face, "beardId", 0, "face.beard.item"), male ? "ChaFileFace.beardId" : null, 121, s => d.Beard.Item = s, "face.beard");
            d.Beard.Color = r.Col(face, "beardColor", d.Beard.Color, "face.beard.color");

            var v = ObjectVersion(x, face, "face");
            if (v != null && v < new Version(0, 0, 1))
            {
                d.Eyes.Highlight.Layout = new Vec4(0.5f, 0.5f, 0.5f, 0.5f);
                d.Eyes.Highlight.Tilt = 0.5f;
                Migrated(x, "face", "0.0.1");
            }
            if (v != null && v < FaceVersion)
            {
                x.Named["face.beard"].Raw = 0;
                d.Beard.Color = new Color4(0.19f, 0.19f, 0.19f, 1f);
                Migrated(x, "face", "0.0.2");
            }
        }

        static void Layer(ImportContext x, object makeup, string name, int category, MakeupLayer d)
        {
            var r = x.Raw;
            string p = "face.makeup." + name;
            x.AddRef(p + ".item", r.I(makeup, name + "Id", 0, p + ".item"), "MakeupInfo." + name + "Id", category, s => d.Item = s);
            d.Color = r.Col(makeup, name + "Color", d.Color, p + ".color");
            d.Gloss = r.F(makeup, name + "Gloss", 0f, p + ".gloss");
        }

        static void Paint(ImportContext x, object m, Paint d, string p, string owner, int i, int layoutCategory)
        {
            var r = x.Raw;
            r.CheckKeys(m, PaintKeys, owner + ".paintInfo[" + i + "]");
            x.AddRef(p + ".item", r.I(m, "id", 0, p + ".item"), owner + ".PaintID" + (i + 1), 313, s => d.Item = s);
            d.Color = r.Col(m, "color", d.Color, p + ".color");
            d.Gloss = r.F(m, "glossPower", 0.5f, p + ".gloss");
            d.Metallic = r.F(m, "metallicPower", 0.5f, p + ".metallic");
            x.AddRef(p + ".layoutItem", r.I(m, "layoutId", 0, p + ".layoutItem"), owner + ".PaintLayoutID" + (i + 1), layoutCategory, s => d.LayoutItem = s);
            d.Layout = r.V4(m, "layout", d.Layout, p + ".layout");
            d.Rotation = r.F(m, "rotation", 0.5f, p + ".rotation");
        }

        // ===================================================================================== body

        public static void Body(ImportContext x, object body, BodyData d, int sex)
        {
            var r = x.Raw;
            r.CheckKeys(body, BodyKeys, "ChaFileBody");
            bool male = sex == 0;
            d.Shape = r.FloatArray(body, "shapeValueBody", d.Shape, "body.shape");
            d.Bust.Softness = r.F(body, "bustSoftness", 0.5f, "body.bust.softness");
            d.Bust.Weight = r.F(body, "bustWeight", 0.5f, "body.bust.weight");
            x.AddRef("body.skin.item", r.I(body, "skinId", 0, "body.skin.item"), "ChaFileBody.skinId", male ? 131 : 231, s => d.Skin.Item = s);
            d.Skin.Color = r.Col(body, "skinColor", d.Skin.Color, "body.skin.color");
            d.Skin.Gloss = r.F(body, "skinGlossPower", 0.7f, "body.skin.gloss");
            d.Skin.Metallic = r.F(body, "skinMetallicPower", 0f, "body.skin.metallic");
            x.AddRef("body.detail.item", r.I(body, "detailId", 0, "body.detail.item"), "ChaFileBody.detailId", male ? 132 : 232, s => d.Detail.Item = s);
            d.Detail.Power = r.F(body, "detailPower", 0.5f, "body.detail.power");
            x.AddRef("body.sunburn.item", r.I(body, "sunburnId", 0, "body.sunburn.item"), "ChaFileBody.sunburnId", male ? 133 : 233, s => d.Sunburn.Item = s);
            d.Sunburn.Color = r.Col(body, "sunburnColor", d.Sunburn.Color, "body.sunburn.color");
            var paints = r.Fixed(body, "paintInfo", 2, "body.paint");
            for (int i = 0; i < 2; i++) Paint(x, paints[i], d.Paint[i], "body.paint[" + i + "]", "ChaFileBody", i, 8);
            x.AddRef("body.nipple.item", r.I(body, "nipId", 0, "body.nipple.item"), "ChaFileBody.nipId", 334, s => d.Nipple.Item = s);
            d.Nipple.Color = r.Col(body, "nipColor", d.Nipple.Color, "body.nipple.color");
            d.Nipple.Gloss = r.F(body, "nipGlossPower", 0.6f, "body.nipple.gloss");
            d.Nipple.Areola = r.F(body, "areolaSize", 0.7f, "body.nipple.areola");
            x.AddRef("body.underhair.item", r.I(body, "underhairId", 0, "body.underhair.item"), "ChaFileBody.underhairId", 335, s => d.Underhair.Item = s);
            d.Underhair.Color = r.Col(body, "underhairColor", d.Underhair.Color, "body.underhair.color");
            d.Nail.Color = r.Col(body, "nailColor", d.Nail.Color, "body.nail.color");
            d.Nail.Gloss = r.F(body, "nailGlossPower", 0.6f, "body.nail.gloss");

            var v = ObjectVersion(x, body, "body");
            if (v != null && v < BodyVersion)
            {
                d.Bust.Weight *= 0.1f;
                Migrated(x, "body", "0.0.1");
            }
        }

        // ===================================================================================== hair

        /// <summary>Raw `bundleId` per part (for the check after resolution).</summary>
        public static int[] Hair(ImportContext x, object hair, HairData d)
        {
            var r = x.Raw;
            r.CheckKeys(hair, HairKeys, "ChaFileHair");
            d.ShaderType = r.I(hair, "shaderType", 0, "hair.shaderType");
            d.Editor.SameSetting = r.B(hair, "sameSetting", true, "hair.editor.sameSetting");
            d.Editor.AutoSetting = r.B(hair, "autoSetting", true, "hair.editor.autoSetting");
            d.Editor.CtrlTogether = r.B(hair, "ctrlTogether", false, "hair.editor.ctrlTogether");
            var bundleIds = new int[4];
            var parts = r.Fixed(hair, "parts", 4, "hair.parts");
            for (int k = 0; k < 4; k++)
            {
                var m = parts[k];
                var h = d.Parts[k];
                string p = "hair." + HairData.PartNames[k];
                r.CheckKeys(m, HairPartKeys, "ChaFileHair.parts[" + k + "]");
                x.AddRef(p + ".item", r.I(m, "id", 0, p + ".item"), "ChaFileHair." + RefProperties.HairProperty[k], RefProperties.HairCategory[k], s => h.Item = s, "hair." + k);
                h.BaseColor = r.Col(m, "baseColor", h.BaseColor, p + ".baseColor");
                h.TopColor = r.Col(m, "topColor", h.TopColor, p + ".topColor");
                h.UnderColor = r.Col(m, "underColor", h.UnderColor, p + ".underColor");
                h.Specular = r.Col(m, "specular", h.Specular, p + ".specular");
                h.Metallic = r.F(m, "metallic", 0f, p + ".metallic");
                h.Smoothness = r.F(m, "smoothness", 0f, p + ".smoothness");
                var acs = r.Fixed(m, "acsColorInfo", HairPart.AccessoryColorCount, p + ".accessoryColors");
                for (int i = 0; i < acs.Length; i++)
                {
                    r.CheckKeys(acs[i], ColorKeys, "ChaFileHair.parts[" + k + "].acsColorInfo[" + i + "]");
                    h.AccessoryColors[i] = r.Col(acs[i], "color", Color4.White, p + ".accessoryColors[" + i + "]");
                }
                bundleIds[k] = r.I(m, "bundleId", -1, p + ".bundleId");
                h.Bundles.Clear();
                if (RawReader.Map(m) != null && RawReader.Map(m).TryGetValue("dictBundle", out var db) && db != null)
                {
                    if (db is Dictionary<object, object> bm)
                    {
                        var list = new List<HairBundle>();
                        foreach (var kv in bm)
                        {
                            if (!(kv.Key is long idx)) { x.Report.Warning("card.badValue", p + ".bundles", "bundle key is not an integer; dropped"); continue; }
                            string bp = p + ".bundles[" + idx + "]";
                            r.CheckKeys(kv.Value, BundleKeys, "ChaFileHair.parts[" + k + "].dictBundle[" + idx + "]");
                            list.Add(new HairBundle
                            {
                                Index = (int)idx,
                                Move = r.V3(kv.Value, "moveRate", Vec3.Zero, bp + ".move"),
                                Rotate = r.V3(kv.Value, "rotRate", Vec3.Zero, bp + ".rotate"),
                                NoShake = r.B(kv.Value, "noShake", false, bp + ".noShake"),
                            });
                        }
                        list.Sort((a, b) => a.Index.CompareTo(b.Index));
                        h.Bundles.AddRange(list);
                    }
                    else x.Report.Warning("card.badValue", p + ".bundles", "dictBundle is not a map; MemberInit value used");
                }
                x.AddRef(p + ".mesh.item", r.I(m, "meshType", 0, p + ".mesh.item"), "ChaFileHair." + RefProperties.HairProperty[k] + "Mesh", RefProperties.HairMeshCategory, s => h.Mesh.Item = s);
                h.Mesh.Color = r.Col(m, "meshColor", h.Mesh.Color, p + ".mesh.color");
                h.Mesh.Layout = r.V4(m, "meshLayout", h.Mesh.Layout, p + ".mesh.layout");
            }

            var v = ObjectVersion(x, hair, "hair");
            if (v != null && v < new Version(0, 0, 1))
            {
                foreach (var h in d.Parts) for (int i = 0; i < h.AccessoryColors.Length; i++) h.AccessoryColors[i] = Color4.White;
                Migrated(x, "hair", "0.0.1");
            }
            if (v != null && v < new Version(0, 0, 2))
            {
                d.Editor.SameSetting = true;
                d.Editor.AutoSetting = true;
                d.Editor.CtrlTogether = false;
                Migrated(x, "hair", "0.0.2");
            }
            if (v != null && v < HairVersion)
            {
                MigrateStrands(d, x.Named["hair.0"].Raw, x.Named["hair.1"].Raw);
                Migrated(x, "hair", "0.0.3");
            }
            return bundleIds;
        }

        // < 0.0.3: strand rates of some back-hair items were re-ranged. Raw ids, before resolution. The front-hair
        // case edits the back hair's bundle 0, as HS2 does.
        static void MigrateStrands(HairData d, int backId, int frontId)
        {
            HairBundle B(int index) => d.Back.Bundles.Find(b => b.Index == index);
            void Rot(int index, Func<Vec3, Vec3> f) { var b = B(index); if (b != null) b.Rotate = f(b.Rotate); }
            void Mov(int index, Func<Vec3, Vec3> f) { var b = B(index); if (b != null) b.Move = f(b.Move); }
            if (backId == 4) Rot(0, r => new Vec3(r.X, 1f - r.Y, r.Z));
            else if (backId == 5)
            {
                Rot(0, r => new Vec3(r.X, r.Y, InverseLerp(-30f, 30f, Lerp(3f, 30f, r.Z))));
                Mov(1, m => new Vec3(m.X, m.Y, InverseLerp(0.1f, -0.4f, Lerp(0.1f, -0.1f, m.Z))));
                Rot(2, r => new Vec3(InverseLerp(-25f, 50f, Lerp(-25f, 45f, r.X)), r.Y, r.Z));
                Mov(3, m => new Vec3(m.X, m.Y, InverseLerp(-0.1f, 0.4f, Lerp(-0.1f, -0.4f, m.Z))));
                Rot(3, r => new Vec3(InverseLerp(45f, -22.5f, Lerp(-22.5f, 45f, r.X)), r.Y, r.Z));
                Rot(4, r => new Vec3(InverseLerp(45f, -22.5f, Lerp(-22.5f, 45f, r.X)), r.Y, r.Z));
            }
            else if (backId == 8) Rot(0, r => new Vec3(r.X, 1f - r.Y, r.Z));
            if (frontId == 7) Rot(0, r => new Vec3(r.X, InverseLerp(70f, -35f, Lerp(-70f, 35f, r.Y)), r.Z));
        }

        // ===================================================================================== clothes

        public static void Clothes(ImportContext x, object clothes, ClothesSet d, int sex)
        {
            var r = x.Raw;
            string cp = x.CoordPath;
            r.CheckKeys(clothes, ClothesKeys, "ChaFileClothes");
            var parts = r.Fixed(clothes, "parts", ClothesSet.Count, cp + "clothes.parts");
            for (int k = 0; k < ClothesSet.Count; k++)
            {
                var m = parts[k];
                var c = d.Parts[k];
                string p = cp + "clothes." + ClothesSet.Names[k];
                r.CheckKeys(m, ClothesPartKeys, "ChaFileClothes.parts[" + k + "]");
                x.AddRef(p + ".item", r.I(m, "id", 0, p + ".item"), RefProperties.ClothesItem(k, sex, x.Outfit), RefProperties.ClothesCategory(k, sex), s => c.Item = s);
                var colors = r.Fixed(m, "colorInfo", ClothesPart.ColorCount, p + ".colors");
                for (int j = 0; j < ClothesPart.ColorCount; j++)
                {
                    var cm = colors[j];
                    var cc = c.Colors[j];
                    string q = p + ".colors[" + j + "]";
                    r.CheckKeys(cm, ClothesColorKeys, "ChaFileClothes.parts[" + k + "].colorInfo[" + j + "]");
                    cc.Base = r.Col(cm, "baseColor", Color4.White, q + ".base");
                    x.AddRef(q + ".pattern", r.I(cm, "pattern", 0, q + ".pattern"), RefProperties.ClothesPattern(k, j, x.Outfit), RefProperties.PatternCategory, s => cc.Pattern = s);
                    cc.Layout = r.V4(cm, "layout", new Vec4(1f, 1f, 0f, 0f), q + ".layout");
                    cc.Rotation = r.F(cm, "rotation", 0.5f, q + ".rotation");
                    cc.PatternColor = r.Col(cm, "patternColor", Color4.White, q + ".patternColor");
                    cc.Gloss = r.F(cm, "glossPower", 0.5f, q + ".gloss");
                    cc.Metallic = r.F(cm, "metallicPower", 0f, q + ".metallic");
                }
                c.BreakRate = r.F(m, "breakRate", 0f, p + ".breakRate");
                var hide = r.Fixed(m, "hideOpt", 2, p + ".hideOptions");
                for (int i = 0; i < 2; i++)
                {
                    if (hide[i] == null) c.HideOptions[i] = false;
                    else if (hide[i] is bool b) c.HideOptions[i] = b;
                    else x.Report.Warning("card.badValue", p + ".hideOptions[" + i + "]", "not a bool; MemberInit value used");
                }
            }
        }

        // ===================================================================================== accessories

        /// <summary>Slots 0..19 of `ChaFileAccessory.parts`: entries for types 351..363, findings for the others.</summary>
        public static void Accessories(ImportContext x, object acc, CoordDocument d)
        {
            var r = x.Raw;
            r.CheckKeys(acc, AccessoryKeys, "ChaFileAccessory");
            if (!(RawReader.Map(acc) != null && RawReader.Map(acc).TryGetValue("parts", out var po) && po is List<object> parts)) return;
            for (int slot = 0; slot < parts.Count; slot++)
            {
                var m = parts[slot];
                string p = x.CoordPath + "accessories[slot " + N(slot) + "]";
                r.CheckKeys(m, AccessoryPartKeys, "ChaFileAccessory.parts[" + slot + "]");
                int type = r.I(m, "type", 120, p + ".category");
                if (!TakeType(x, type, p)) continue;
                var e = new AccessoryEntry { Id = x.AccessoryId(slot), Category = type, ImportSlot = slot };
                x.AddRef(p + ".item", r.I(m, "id", 0, p + ".item"), RefProperties.Accessory(slot, x.Outfit), type, s => e.Item = s);
                e.Parent = r.S(m, "parentKey", "", p + ".parent");
                ReadMoves(x, m, e, p);
                var colors = r.Fixed(m, "colorInfo", AccessoryEntry.ColorCount, p + ".colors");
                for (int i = 0; i < AccessoryEntry.ColorCount; i++)
                {
                    var cm = colors[i];
                    var c = e.Colors[i];
                    string q = p + ".colors[" + i + "]";
                    r.CheckKeys(cm, AccessoryColorKeys, "ChaFileAccessory.parts[" + slot + "].colorInfo[" + i + "]");
                    c.Color = r.Col(cm, "color", Color4.White, q + ".color");
                    c.Gloss = r.F(cm, "glossPower", 0.5f, q + ".gloss");
                    c.Metallic = r.F(cm, "metallicPower", 0.5f, q + ".metallic");
                    c.Smoothness = r.F(cm, "smoothnessPower", 0.5f, q + ".smoothness");
                }
                e.HideCategory = r.I(m, "hideCategory", 0, p + ".hideCategory");
                e.HideTiming = r.I(m, "hideTiming", 1, p + ".hideTiming");
                e.NoShake = r.B(m, "noShake", false, p + ".noShake");
                Add(x, d, slot, e);
            }
        }

        /// <summary>Slots ≥ 20 from MoreAccessories (element i = slot 20 + i, empty elements included).</summary>
        public static void MoreAccessories(ImportContext x, HS2Next.Data.Ext.MoreAccessoriesData more, CoordDocument d)
        {
            if (more == null) return;
            var r = x.Raw;
            foreach (var part in more.Parts)
            {
                int slot = part.Slot;
                string p = x.CoordPath + "accessories[slot " + N(slot) + "]";
                if (!TakeType(x, part.Type, p)) continue;
                var e = new AccessoryEntry { Id = x.AccessoryId(slot), Category = part.Type, ImportSlot = slot };
                x.AddRef(p + ".item", part.Id, RefProperties.Accessory(slot, x.Outfit), part.Type, s => e.Item = s);
                e.Parent = part.ParentKey ?? "";
                for (int j = 0; j < AccessoryEntry.MoveCount; j++)
                {
                    e.Moves[j].Position = V3(r, part.AddMove[j, 0], p);
                    e.Moves[j].Rotation = V3(r, part.AddMove[j, 1], p);
                    e.Moves[j].Scale = V3(r, part.AddMove[j, 2], p);
                }
                for (int i = 0; i < AccessoryEntry.ColorCount; i++)
                {
                    var c = part.Color[i];
                    e.Colors[i].Color = new Color4(r.Plugin(c.r, p), r.Plugin(c.g, p), r.Plugin(c.b, p), r.Plugin(c.a, p));
                    e.Colors[i].Gloss = r.Plugin(part.GlossPower[i], p);
                    e.Colors[i].Metallic = r.Plugin(part.MetallicPower[i], p);
                    e.Colors[i].Smoothness = r.Plugin(part.SmoothnessPower[i], p);
                }
                e.HideCategory = part.HideCategory;
                e.HideTiming = part.HideTiming;
                e.NoShake = part.NoShake;
                Add(x, d, slot, e);
            }
        }

        static Vec3 V3(RawReader r, UnityEngine.Vector3 v, string p) => new Vec3(r.Plugin(v.x, p), r.Plugin(v.y, p), r.Plugin(v.z, p));

        static bool TakeType(ImportContext x, int type, string p)
        {
            if (type >= 351 && type <= 363) return true;
            if (type == 350) { x.Report.Count("accessory.empty"); x.Report.Info("accessory.empty", x.CoordPath + "accessories", "empty slot (type 350)"); }
            else if (type == 120) { x.Report.Count("accessory.type120"); x.Report.Info("accessory.type120", p, "type 120 (the MemberInit value): an empty slot"); }
            else { x.Report.Count("accessory.unknownType"); x.Report.Info("accessory.unknownType", p, $"type {type} is not an accessory category; slot treated as empty"); }
            return false;
        }

        static void Add(ImportContext x, CoordDocument d, int slot, AccessoryEntry e)
        {
            if (x.Slots.ContainsKey(slot))
            {
                x.Report.Warning("accessory.duplicateSlot", x.CoordPath + "accessories[slot " + N(slot) + "]", "slot occurs twice; the later one dropped");
                return;
            }
            x.Slots[slot] = e;
            d.Accessories.Add(e);
            x.Report.Count("accessory.entries");
        }

        // addMove: MessagePack-CSharp's 2-D array form [2, 3, [6 × Vector3]], row-major.
        static void ReadMoves(ImportContext x, object m, AccessoryEntry e, string p)
        {
            if (!(RawReader.Map(m) != null && RawReader.Map(m).TryGetValue("addMove", out var am))) return;
            if (!(am is List<object> l) || l.Count != 3 || !(l[0] is long d0) || !(l[1] is long d1) || !(l[2] is List<object> flat) || flat.Count != d0 * d1)
            {
                x.Report.Warning("card.badValue", p + ".moves", "addMove is not a [rows, columns, elements] array; MemberInit value used");
                return;
            }
            if (d0 != 2 || d1 != 3)
            {
                string code = d0 < 2 || d1 < 3 ? "card.arrayPadded" : "card.arrayCut";
                x.Report.Warning(code, p + ".moves", $"addMove is {d0} × {d1}, 2 × 3 expected");
                x.Report.Count(code);
            }
            for (int j = 0; j < 2 && j < d0; j++)
                for (int k = 0; k < 3 && k < d1; k++)
                {
                    var def = k == 2 ? Vec3.One : Vec3.Zero;
                    var v = x.Raw.V3Value(flat[(int)(j * d1 + k)], def, p + ".moves[" + j + "]");
                    if (k == 0) e.Moves[j].Position = v;
                    else if (k == 1) e.Moves[j].Rotation = v;
                    else e.Moves[j].Scale = v;
                }
        }
    }
}
