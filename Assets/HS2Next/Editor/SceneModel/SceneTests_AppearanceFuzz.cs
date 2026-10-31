// Maker edit planner and F (model fuzz).
// SceneTests_AppearanceOps: the rule consequences as explicit sets (head → skin snap, hair / clothes / accessory
// item and type changes, "same setting" and "auto colour" hair switches, linked eyes, face preset), suppression by the
// caller's own sets, row checks per sex, rebuild-only paths, accessory add / remove / move / copy with the state map, the
// first-edit import of a card-backed node, and the history (one entry, undo / redo restore every consequence).
// SceneTests_AppearanceFuzz: random appearance commands (bus sets on random paths with type-correct random values incl.
// out of range and invalid ones, whole-object sets, planner edits with consequences, item refs from a fixed pool of 40 rows
// per category, accessory add / remove / move / copy, transactions with commit / cancel, typed merges, face presets,
// undo / redo interleaved; 4.1: layout pushes, row resets, eye copies; 4.2: skin swatch pushes, paint region picks, body
// shape / bust / futanari sets, resets of body rows with the session's own references; 4.3: hair colour presets, skin
// matches, bundle edits with the together switch both ways, drags and resets, hair switch / mesh / shader / item sets;
// 4.4: clothes «初期カラー», resets to the prefab defaults, clothes member sets, coordinate loads in the three modes with and
// without ext data and of either sex; 4.6: partial character loads with random flags from a second golden-derived source,
// all five as the whole-appearance swap, sometimes of the other sex) on two characters (the golden female and a male copy). After every operation the
// model is valid and the state (scene bytes + both documents of every character, the coordinate's ext data
// fingerprint with coord.json) is the recorded one of its history position;
// undo all gives the initial bytes, redo all the final ones, a random walk meets the recorded states. Default 2,000 seeds
// × 200 operations; HS2N_APPEARANCE_FUZZ=full (mono_tests.ps1 -Full) runs 10,000 × 300, =smoke 100 × 100 (the Unity
// SceneModelTest row of run_tests.ps1 -Quick, which runs the default size under plain Mono beside it), a number sets the
// seed count, HS2N_APPEARANCE_FUZZ_SEED=n runs seed n alone, =off leaves it out (the Unity row of -Quick while the plain-Mono
// job runs the default size beside it).
// A failure prints the seed, the operation and the minimal failing prefix.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using Newtonsoft.Json.Linq;

    /// <summary>Deterministic list facts and prefab defaults for the planner tests (a fixed pool of 40 rows per category).</summary>
    internal sealed class FakeMakerData : IAppearanceRows, IPartDefaults
    {
        public const int PoolSize = 40;
        public static readonly string[] Parents = { "N_Head", "N_Earring_L", "N_Earring_R", "N_Neck", "N_Hand_L", "N_Hand_R", "N_Waist" };

        public static string Row(int category, int i) => "fuzz.pool#" + (category * 100 + i).ToString(CultureInfo.InvariantCulture);
        public static string[] Pool(int category) => Enumerable.Range(0, PoolSize).Select(i => Row(category, i)).ToArray();

        static int Hash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        static int IndexOf(int category, string reference)
        {
            if (reference == null || !reference.StartsWith("fuzz.pool#", StringComparison.Ordinal)) return -1;
            if (!int.TryParse(reference.Substring(10), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return -1;
            int i = n - category * 100;
            return i >= 0 && i < PoolSize ? i : -1;
        }

        public bool HasRow(int category, int sex, string reference) => IndexOf(category, reference) >= 0;
        public string FirstRow(int category, int sex) => category == 363 ? null : Row(category, 0);

        public IReadOnlyList<string> SkinsForHead(int sex, string headRef)
        {
            int h = Hash(headRef);
            if (h % 5 == 0) return new string[0];
            int cat = sex == 0 ? 111 : 211;
            return new[] { Row(cat, h % PoolSize), Row(cat, (h / 7) % PoolSize), Row(cat, (h / 49) % PoolSize) };
        }

        public string AccessoryParent(int category, string reference)
        {
            int h = Hash(reference);
            return h % 10 == 0 ? null : Parents[h % Parents.Length];
        }

        // The vanilla layout rows (lists 6 and 7, ids 0..n-1; (Scale, PosX, PosY)).
        public static readonly (float scale, float x, float y)[] MoleLayouts =
        {
            (0.14f, 0.6f, 0.18f), (0.14f, 0.4f, 0.18f), (0.14f, 0.56f, 0.29f), (0.14f, 0.44f, 0.29f),
            (0.33f, 0.75f, 0.52f), (0.33f, 0.25f, 0.52f), (0.71f, 0.5f, 0.75f), (0.7f, 0.5f, 0.48f),
        };
        public static readonly (float scale, float x, float y)[] PaintLayouts =
        {
            (0.5f, 0.76f, 0.35f), (0.5f, 0.24f, 0.35f), (0.5f, 0.48f, 0.7f), (0.5f, 0.48f, 0.44f), (0.5f, 0.48f, 0.195f), (0.5f, 0.48f, 0.07f),
        };

        public bool LayoutValues(int category, string reference, out float scale, out float posX, out float posY)
        {
            scale = posX = posY = 0f;
            var rows = category == 6 ? MoleLayouts : category == 7 ? PaintLayouts : null;
            if (rows == null || reference == null || !reference.StartsWith("illusion.hs2#", StringComparison.Ordinal)) return false;
            if (!int.TryParse(reference.Substring(13), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0 || id >= rows.Length) return false;
            (scale, posX, posY) = rows[id];
            return true;
        }

        /// <summary>The male head list (110) has one row unless <see cref="MaleHeads"/> says more (the maker.headList gate).</summary>
        public int MaleHeads = 1;
        public bool ListHasMoreThanOne(int category, int sex) => category == 110 && sex == 0 ? MaleHeads > 1 : PoolSize > 1;

        // The skin colour presets (list 336, vanilla ids 0..6): Base (H, S, V), Sample (H, S, V).
        public static readonly (float bh, float bs, float bv, float sh, float ss, float sv)[] SkinPresets =
        {
            (0.063f, 0.2f, 0.78f, 0.063f, 0.14f, 0.88f), (0.066f, 0.2f, 0.74f, 0.066f, 0.23f, 0.81f), (0.053f, 0.21f, 0.75f, 0.053f, 0.27f, 0.8f),
            (0.058f, 0.21f, 0.7f, 0.058f, 0.26f, 0.7f), (0.061f, 0.21f, 0.65f, 0.061f, 0.24f, 0.6f), (0.061f, 0.21f, 0.6f, 0.061f, 0.23f, 0.5f),
            (0.055f, 0.16f, 0.74f, 0.055f, 0.06f, 0.85f),
        };

        public bool ColourPreset(int category, string reference, out Color4 value, out Color4 swatch)
        {
            value = swatch = default;
            if (category != 336 || reference == null || !reference.StartsWith("illusion.hs2#", StringComparison.Ordinal)) return false;
            if (!int.TryParse(reference.Substring(13), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id < 0 || id >= SkinPresets.Length) return false;
            var p = SkinPresets[id];
            value = MakerEdits.HsvToRgb(p.bh, p.bs, p.bv);
            swatch = MakerEdits.HsvToRgb(p.sh, p.ss, p.sv);
            return true;
        }

        static float F(int h, int k) => ((h >> k) & 15) / 15f;

        public bool TryClothesColors(int category, string itemRef, out ClothesColor[] colors)
        {
            int h = Hash(itemRef);
            colors = null;
            if (h % 8 == 0) return false;
            colors = new ClothesColor[3];
            for (int i = 0; i < 3; i++)
                colors[i] = new ClothesColor
                {
                    Base = new Color4(F(h, i), F(h, i + 4), F(h, i + 8), 1f), Gloss = F(h, i + 2), Metallic = F(h, i + 6), Pattern = "illusion.hs2#" + (h % 3),
                    Layout = new Vec4(F(h, i + 1), F(h, i + 3), F(h, i + 5), F(h, i + 7)), Rotation = F(h, i + 9),
                };
            return true;
        }

        /// <summary>Clothes prefab facts (4.4): the rows with colour defaults (TryClothesColors) have facts unless
        /// <see cref="NoClothesFacts"/>; the flags come from the row's hash (set 1 always shown), a row whose hash % 7 == 0 is a
        /// one-piece, the defaults are TryClothesColors'.</summary>
        public bool NoClothesFacts;

        public bool TryClothesFacts(int category, string itemRef, int sex, out ClothesKindFacts facts)
        {
            facts = null;
            if (NoClothesFacts || !TryClothesColors(category, itemRef, out var cols)) return false;
            int h = Hash(itemRef + "/facts");
            facts = new ClothesKindFacts
            {
                HasPrefab = true, Item = itemRef, PrefabName = "p_fake_" + (h % 1000).ToString(CultureInfo.InvariantCulture),
                UseColorN01 = true, UseColorN02 = ((h >> 1) & 1) != 0, UseColorN03 = ((h >> 2) & 1) != 0,
                UseColorA01 = ((h >> 3) & 1) != 0, UseColorA02 = ((h >> 4) & 1) != 0, UseColorA03 = ((h >> 5) & 1) != 0,
                UseBreak = ((h >> 6) & 1) != 0, Option01Count = (h >> 7) & 1, Option02Count = (h >> 8) & 1,
                Coordinate = h % 7 == 0 ? "1" : "0", StateType = h % 3,
                Groups = new[] { new ClothesGroupFacts { Group = 1, Renderers = 1 + h % 3, Slots = 1 + h % 4 } },
                Defaults = cols,
            };
            return true;
        }

        public bool TryAccessoryColors(int category, string itemRef, out AccessoryColor[] colors)
        {
            int h = Hash(itemRef);
            colors = null;
            if (h % 8 == 1) return false;
            colors = new AccessoryColor[4];
            for (int i = 0; i < 4; i++) colors[i] = new AccessoryColor { Color = new Color4(F(h, i), F(h, i + 3), F(h, i + 9), 1f), Gloss = F(h, i + 1), Metallic = F(h, i + 5) };
            return true;
        }

        /// <summary>Accessory prefab facts (4.5): the rows with colour defaults (TryAccessoryColors) have facts unless
        /// <see cref="NoAccessoryFacts"/> or named in <see cref="AccessoryFactsOf"/> (a test's own facts win); from the row's hash:
        /// hair-type on a quarter of the rows (never row 0 of a pool, as vanilla's row 0), area 1 always, areas 2 / 3 and alpha
        /// renderers by bits, set 1 on most rows, set 2 on half; the `Parent` column = AccessoryParent; the defaults =
        /// TryAccessoryColors'.</summary>
        public bool NoAccessoryFacts;
        public readonly Dictionary<string, AccessoryFacts> AccessoryFactsOf = new Dictionary<string, AccessoryFacts>(StringComparer.Ordinal);

        public bool TryAccessoryFacts(int category, string itemRef, out AccessoryFacts facts)
        {
            facts = null;
            if (itemRef != null && AccessoryFactsOf.TryGetValue(itemRef, out var own)) { facts = own?.Clone(); return facts != null; }
            if (NoAccessoryFacts || !TryAccessoryColors(category, itemRef, out var cols)) return false;
            int h = Hash(itemRef + "/acc");
            facts = new AccessoryFacts
            {
                HasCmpAccessory = true, Category = category, Item = itemRef, PrefabName = "p_fake_acc_" + (h % 1000).ToString(CultureInfo.InvariantCulture),
                TypeHair = ((h >> 1) & 3) == 0 && IndexOf(category, itemRef) != 0,
                UseColor = new[] { true, ((h >> 3) & 1) != 0, ((h >> 4) & 1) != 0 }, AlphaRenderers = (h >> 5) & 1,
                Move1 = ((h >> 6) & 7) != 0, Move2 = ((h >> 9) & 1) != 0, ParentColumn = AccessoryParent(category, itemRef),
                DynamicBones = (h >> 10) & 3, Colliders = 0, DefaultColors = cols,
            };
            return true;
        }

        public bool TryHairAccessoryColors(int category, string itemRef, out Color4[] colors)
        {
            int h = Hash(itemRef);
            colors = null;
            if (h % 8 == 2) return false;
            colors = new Color4[4];
            for (int i = 0; i < 4; i++) colors[i] = new Color4(F(h, i), F(h, i + 2), F(h, i + 4), 1f);
            return true;
        }

        // The hair colour presets (list 305, vanilla ids 0..6): Base, Top, Under, Specular (H, S, V),
        // Metallic, Smoothness (the vanilla fixture's columns).
        public static readonly float[][] HairPresets =
        {
            new[] { 0f, 0f, 0.09f, 0f, 0f, 0.113f, 0f, 0f, 0.491f, 0f, 0.02f, 0.56f, 0.03f, 0f },
            new[] { 0.033f, 0.24f, 0.294f, 0.058f, 0.29f, 0.19f, 0.063f, 0.108f, 0.604f, 0.06f, 0.259f, 0.557f, 0.314f, 0f },
            new[] { 0.088f, 0.401f, 0.311f, 0.068f, 0.278f, 0.387f, 0.09f, 0.241f, 0.708f, 0.09f, 0.297f, 0.726f, 0f, 0f },
            new[] { 0.063f, 0.42f, 0.585f, 0.061f, 0.43f, 0.62f, 0.06f, 0.297f, 0.83f, 0.064f, 0.429f, 0.962f, 0.299f, 0f },
            new[] { 0.092f, 0.25f, 0.623f, 0.092f, 0.269f, 0.594f, 0.071f, 0.212f, 0.821f, 0.083f, 0.278f, 0.764f, 0.185f, 0f },
            new[] { 0.1f, 0.22f, 0.75f, 0.028f, 0.024f, 0.783f, 0.071f, 0.175f, 0.925f, 0.081f, 0.22f, 0.84f, 0f, 0f },
            new[] { 0.128f, 0.07f, 0.78f, 0.131f, 0.075f, 0.78f, 0.131f, 0.07f, 1f, 0.147f, 0.04f, 0.75f, 0f, 0f },
        };

        static int VanillaId(string reference, int count)
        {
            if (reference == null || !reference.StartsWith("illusion.hs2#", StringComparison.Ordinal)) return -1;
            return int.TryParse(reference.Substring(13), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id >= 0 && id < count ? id : -1;
        }

        public bool HairColourPreset(string reference, out HairColourPresetValues values)
        {
            values = default;
            int id = VanillaId(reference, HairPresets.Length);
            if (id < 0) return false;
            var p = HairPresets[id];
            values = new HairColourPresetValues
            {
                Base = MakerEdits.HsvToRgb(p[0], p[1], p[2]), Top = MakerEdits.HsvToRgb(p[3], p[4], p[5]), Under = MakerEdits.HsvToRgb(p[6], p[7], p[8]),
                Specular = MakerEdits.HsvToRgb(p[9], p[10], p[11]), Metallic = p[12], Smoothness = p[13],
            };
            return true;
        }

        /// <summary>Hair rows with bundle facts: a row's bundle count is from its hash (0..4) unless <see cref="BundleCounts"/> names
        /// it; rows whose hash % 8 == 3 have no facts (unknown prefab). Limits per bundle from the hash; bundle 0 of a 4-bundle row
        /// has no correction transform (not shown).</summary>
        public readonly Dictionary<string, int> BundleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool NoBundleFacts;

        public bool TryHairBundles(int category, string itemRef, int sex, out HairBundleFacts[] facts)
        {
            facts = null;
            if (NoBundleFacts || itemRef == null) return false;
            int h = Hash(itemRef + "/" + category);
            if (!BundleCounts.TryGetValue(itemRef, out int n))
            {
                if (h % 8 == 3) return false;
                n = h % 5;
            }
            var info = new List<(bool, Vec3, Vec3, Vec3, Vec3, Vec3, Vec3)>();
            for (int b = 0; b < n; b++)
            {
                int g = Hash(itemRef + "#" + b);
                var pmin = new Vec3(-0.45f, 0.3f, 0.2f * F(g, 1));
                var pmax = new Vec3(0.45f, -0.3f, -0.2f - F(g, 2));
                var rmin = new Vec3(-20f, 25f, -25f - 10f * F(g, 3));
                var rmax = new Vec3(70f, -25f, 25f);
                var bp = new Vec3(0.1f * F(g, 4), 0f, 0f);
                var br = new Vec3(0f, 5f * F(g, 5), 0f);
                info.Add((!(n == 4 && b == 0), pmin, pmax, rmin, rmax, bp, br));
            }
            facts = HairBundleFacts.Number(info);
            return true;
        }
    }

    public static class SceneTests_AppearanceOps
    {
        public static void Run(TestRunner t)
        {
            Head(t);
            Hair(t);
            ClothesAndAccessoryItems(t);
            Eyes(t);
            Checks(t);
            AccessoryOps(t);
            Preset(t);
            Import(t);
        }

        static MakerEditOptions Opts(SceneSession s, FakeMakerData data = null, bool keep = false)
        {
            data = data ?? new FakeMakerData();
            return new MakerEditOptions { Rows = data, Defaults = data, KeepColours = keep, NewId = s.NewId };
        }

        /// <summary>Plans and executes in one BeginOrJoin, committing on success (StudioOps.Maker's pattern).</summary>
        static CommandResult Edit(SceneSession s, string id, MakerEditOptions o, MakerEditReport rep, string label, params (string, object)[] sets)
        {
            using (var tx = s.Commands.BeginOrJoin(label))
            {
                var r = MakerEdits.Edit(tx, s.Model, s.Model.Find(id), sets, o, rep);
                if (r.Status == CommandStatus.Error) return r;
                var c = tx.Commit();
                return c.Status == CommandStatus.Error ? c : r;
            }
        }

        static string P(string m) => AppearancePaths.Chara(m);
        static string K(string m) => AppearancePaths.Coord(m);

        static void Head(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var st = s.Model.Find(id).Appearance.Stored;
            var data = new FakeMakerData();
            // a head whose skins do not list the current skin → snap to the first
            string head = Enumerable.Range(0, FakeMakerData.PoolSize).Select(i => FakeMakerData.Row(210, i)).First(h => data.SkinsForHead(1, h).Count > 0);
            var shapes = (float[])st.Chara.Face.Shape.Clone();
            string head0 = st.Chara.Face.Head, skin0 = st.Chara.Face.Skin;
            var rep = new MakerEditReport();
            var r = Edit(s, id, Opts(s, data), rep, "Change item", (P("face.head"), head));
            Kit.Ok(t, r, "head change");
            t.Equal(data.SkinsForHead(1, head)[0], st.Chara.Face.Skin, "head: the skin snaps to the first listed skin of the new head");
            t.Check(rep.Consequences.SequenceEqual(new[] { P("face.skin") }), "head: the consequence is reported (" + string.Join(", ", rep.Consequences) + ")");
            t.Check(st.Chara.Face.Shape.SequenceEqual(shapes), "head: the 59 shape values are kept");
            t.Equal(2, s.Commands.UndoList.Count, "head: one history entry");
            s.Commands.Undo();
            t.Check(st.Chara.Face.Head == head0 && st.Chara.Face.Skin == skin0, "head: undo restores head and skin together");
            // a listed skin is kept; the caller's own skin wins
            s.Commands.Redo();
            string head2 = Enumerable.Range(0, FakeMakerData.PoolSize).Select(i => FakeMakerData.Row(210, i)).First(h => h != head && data.SkinsForHead(1, h).Count > 0);
            string mySkin = FakeMakerData.Row(211, 39);
            rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Change item", (P("face.head"), head2), (P("face.skin"), mySkin)), "head + skin in one call");
            t.Equal(mySkin, st.Chara.Face.Skin, "head: a skin the caller sets is not overridden");
            t.Check(rep.Consequences.Count == 0, "head: no consequence when the caller sets the skin");
            // a head without skins keeps the skin
            string bare = Enumerable.Range(0, FakeMakerData.PoolSize).Select(i => FakeMakerData.Row(210, i)).First(h => data.SkinsForHead(1, h).Count == 0);
            Kit.Ok(t, Edit(s, id, Opts(s, data), null, "Change item", (P("face.head"), bare)), "a head without skin rows");
            t.Equal(mySkin, st.Chara.Face.Skin, "head: no skin rows → the skin is kept");
            // without lists: warning, nothing snapped
            rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, new MakerEditOptions(), rep, "Change item", (P("face.head"), head)), "head change without lists");
            t.Check(rep.Warnings.Any(w => w.StartsWith("edit.listsUnavailable", StringComparison.Ordinal)) && st.Chara.Face.Skin == mySkin, "head: no lists → edit.listsUnavailable, skin kept");
        }

        static void Hair(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var st = s.Model.Find(id).Appearance.Stored;
            var data = new FakeMakerData();
            var hair = st.Chara.Hair;
            t.Check(hair.Back.Bundles.Count > 0, "hair: the golden back hair has bundles");
            string item = FakeMakerData.Pool(300).First(r => data.TryHairAccessoryColors(300, r, out _));
            data.TryHairAccessoryColors(300, item, out var acs);
            var mesh = FieldJson.ToText(hair.Back.Mesh);
            var rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Change item", (P("hair.back.item"), item)), "hair item change");
            t.Check(hair.Back.Bundles.Count == 0, "hair item: bundles cleared");
            t.Check(hair.Back.AccessoryColors.SequenceEqual(acs), "hair item: decoration colours ← the prefab defaults");
            t.Check(hair.Back.MaterialOverrides.Count == 0, "hair item: MaterialEditor items cleared");
            t.Equal(mesh, FieldJson.ToText(hair.Back.Mesh), "hair item: mesh kept");
            s.Commands.Undo();
            t.Check(hair.Back.Bundles.Count == 2 && hair.Back.AccessoryColors[2].R == 1f && hair.Back.AccessoryColors[2].G == 0f, "hair item: undo restores bundles and colours");
            // keep colours
            Kit.Ok(t, Edit(s, id, Opts(s, data, keep: true), null, "Change item", (P("hair.back.item"), item)), "hair item change keeping colours");
            t.Check(hair.Back.AccessoryColors[2].G == 0f, "hair item: keep colours keeps the decoration colours");
            s.Commands.Undo();
            // unavailable defaults
            string noDef = FakeMakerData.Pool(300).First(r => !data.TryHairAccessoryColors(300, r, out _));
            rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Change item", (P("hair.back.item"), noDef)), "hair item without defaults");
            t.Check(rep.Warnings.Any(w => w.StartsWith("edit.defaultsUnavailable", StringComparison.Ordinal)) && hair.Back.AccessoryColors[2].G == 0f, "hair item: no defaults → colours unchanged + warning");
            s.Commands.Undo();
            // same setting (default true) + auto colour (default true)
            s.Set(id, P("hair.editor.sameSetting"), true);
            s.Set(id, P("hair.editor.autoSetting"), true);
            t.Check(hair.Editor.SameSetting && hair.Editor.AutoSetting, "hair: both switches on");
            var red = new Color4(0.8f, 0.1f, 0.1f, 0.5f);
            var side0 = FieldJson.ToText(hair.Side);
            rep = new MakerEditReport();
            int hairDepth = s.Commands.UndoList.Count;
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Edit hair", (P("hair.front.baseColor"), red)), "a base colour with same setting and auto colour");
            MakerEdits.Derive(red, out var top, out var under, out var spec);
            for (int k = 0; k < 4; k++)
            {
                var p = hair.Parts[k];
                t.Check(p.BaseColor.R == red.R && p.BaseColor.A == red.A, "hair " + HairData.PartNames[k] + ": base colour (α kept as given)");
                t.Check(p.TopColor.R == top.R && p.UnderColor.G == under.G && p.Specular.B == spec.B, "hair " + HairData.PartNames[k] + ": top / under / specular derived");
            }
            t.Equal(15, rep.Consequences.Count, "hair: 3 base + 12 derived consequences");
            t.Equal(hairDepth + 1, s.Commands.UndoList.Count, "hair: one entry");
            s.Commands.Undo();
            t.Equal(side0, FieldJson.ToText(hair.Side), "hair: undo restores the other parts");
            // switches off
            Kit.Ok(t, s.Set(id, P("hair.editor.sameSetting"), false), "same setting off");
            Kit.Ok(t, s.Set(id, P("hair.editor.autoSetting"), false), "auto colour off");
            var topBefore = hair.Front.TopColor;
            rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Edit hair", (P("hair.front.baseColor"), red)), "a base colour with the switches off");
            t.Check(rep.Consequences.Count == 0 && hair.Back.BaseColor.R != red.R && hair.Front.TopColor.R == topBefore.R, "hair: switches off → no consequence");
            // the switch set in the same call counts
            rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Edit hair", (P("hair.editor.sameSetting"), true), (P("hair.front.metallic"), 0.75f)), "same setting on + metallic in one call");
            t.Check(hair.Parts.All(p => p.Metallic == 0.75f), "hair: the switch set earlier in the call applies");
            // Derive matches Unity's HSV conversions on a few values
            MakerEdits.RgbToHsv(0.2f, 0.4f, 0.6f, out float h, out float sat, out float v);
            t.Check(Math.Abs(h - 0.5833333f) < 1e-6 && Math.Abs(sat - 0.6666667f) < 1e-6 && Math.Abs(v - 0.6f) < 1e-6, "RgbToHsv(0.2, 0.4, 0.6) = (0.5833, 0.6667, 0.6)");
            var back = MakerEdits.HsvToRgb(h, sat, v);
            t.Check(Math.Abs(back.R - 0.2f) < 1e-6 && Math.Abs(back.G - 0.4f) < 1e-6 && Math.Abs(back.B - 0.6f) < 1e-6, "HsvToRgb round trip");
        }

        static void ClothesAndAccessoryItems(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var st = s.Model.Find(id).Appearance.Stored;
            var data = new FakeMakerData();
            var top = st.Coord.Clothes.Parts[0];
            string item = FakeMakerData.Pool(240).First(r => data.TryClothesColors(240, r, out _));
            data.TryClothesColors(240, item, out var cols);
            float br = top.BreakRate;
            var overlays = FieldJson.ToText(top.Overlays);
            var hide = (bool[])top.HideOptions.Clone();
            var rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Change item", (K("clothes.top.item"), item)), "clothes item change");
            t.Check(FieldJson.ToText(top.Colors) == FieldJson.ToText(cols), "clothes item: colours ← prefab defaults");
            t.Check(top.MaterialOverrides.Count == 0, "clothes item: MaterialEditor items cleared (C6)");
            t.Check(top.BreakRate == br && FieldJson.ToText(top.Overlays) == overlays && top.HideOptions.SequenceEqual(hide), "clothes item: break, overlays, options kept");
            t.Check(rep.Consequences.SequenceEqual(new[] { K("clothes.top.colors"), K("clothes.top.materialOverrides") }), "clothes item: consequences reported");
            s.Commands.Undo();
            t.Check(top.MaterialOverrides.Count > 0 && top.Colors[0].Base.G == 0.5f, "clothes item: undo restores colours and items");
            // a colour edit keeps the ME items (the quirk is not reproduced)
            Kit.Ok(t, Edit(s, id, Opts(s, data), null, "Edit clothes", (K("clothes.top.colors[0].base"), new Color4(0, 1, 0, 1))), "a clothes colour edit");
            t.Check(top.MaterialOverrides.Count > 0, "clothes colour: MaterialEditor items kept");
            // accessory item change: parent rule C11
            var acc = st.Coord.Accessories[0];
            string aid = acc.Id;
            string pre = AppearancePaths.Accessory(aid);
            string oldDefault = data.AccessoryParent(acc.Category, acc.Item);
            string aItem = FakeMakerData.Pool(acc.Category).First(r => data.AccessoryParent(acc.Category, r) != null && data.TryAccessoryColors(acc.Category, r, out _) && data.AccessoryParent(acc.Category, r) != acc.Parent);
            int boneMods = acc.BoneMods.Count;
            rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Change item", (pre + ".item", aItem)), "accessory item change");
            bool userParent = acc.Parent.Length > 0 && acc.Parent != oldDefault;
            t.Check(userParent ? st.Coord.Accessories[0].Parent == "N_Earring_L" : st.Coord.Accessories[0].Parent == data.AccessoryParent(acc.Category, aItem),
                "accessory item: parent " + (userParent ? "kept (moved away from the old default, C11)" : "← the new row's Parent"));
            var acc2 = st.Coord.Accessories[0];
            data.TryAccessoryColors(acc.Category, aItem, out var acol);
            t.Check(FieldJson.ToText(acc2.Colors) == FieldJson.ToText(acol), "accessory item: colours ← prefab defaults");
            t.Check(acc2.MaterialOverrides.Count == 0 && acc2.DynamicBones.Count == 0 && acc2.BoneMods.Count == boneMods, "accessory item: ME / DBE cleared, boneMods kept");
            s.Commands.Undo();
            // a parent equal to the old default follows the new row
            Kit.Ok(t, s.Set(id, pre + ".parent", oldDefault ?? ""), "parent set to the old row's default");
            Kit.Ok(t, Edit(s, id, Opts(s, data), null, "Change item", (pre + ".item", aItem)), "accessory item change from the default parent");
            t.Equal(data.AccessoryParent(acc.Category, aItem), st.Coord.Accessories[0].Parent, "accessory item: a default parent follows the new row");
            s.Commands.Undo();
            // category change
            rep = new MakerEditReport();
            int depthBefore = s.Commands.UndoList.Count;
            Kit.Ok(t, Edit(s, id, Opts(s, data), rep, "Change item", (pre + ".category", 355)), "accessory type change");
            var a3 = st.Coord.Accessories[0];
            t.Check(a3.Category == 355 && a3.Item == FakeMakerData.Row(355, 0) && a3.Parent == "" && !a3.NoShake, "accessory type: item ← first row, parent ← \"\", noShake false");
            t.Check(a3.Moves.All(m => m.Position.X == 0 && m.Rotation.Y == 0 && m.Scale.X == 1), "accessory type: moves ← identity");
            t.Check(a3.MaterialOverrides.Count == 0 && a3.BoneMods.Count == 0 && a3.DynamicBones.Count == 0, "accessory type: plugin items cleared");
            t.Equal(depthBefore + 1, s.Commands.UndoList.Count, "accessory type: one entry");
            s.Commands.Undo();
            t.Check(st.Coord.Accessories[0].Category == 352 && st.Coord.Accessories[0].BoneMods.Count == boneMods, "accessory type: undo restores everything");
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Change item", (pre + ".category", 350)), CommandCodes.BadArgs, "accessory type 350 through EditAppearance (use RemoveAccessory)");
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Change item", (pre + ".category", 120)), CommandCodes.BadArgs, "accessory type outside 351..363");
        }

        static void Eyes(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var eyes = s.Model.Find(id).Appearance.Stored.Chara.Face.Eyes;
            Kit.Ok(t, s.Set(id, P("face.eyes.linked"), true), "eyes linked");
            var c = new Color4(0.1f, 0.2f, 0.9f, 1);
            Kit.Ok(t, Edit(s, id, Opts(s), null, "Edit face", (P("face.eyes.left.iris.color"), c)), "a left iris colour with linked eyes");
            t.Check(eyes.Right.Iris.Color.B == 0.9f, "linked eyes: the right eye follows");
            Kit.Ok(t, Edit(s, id, Opts(s), null, "Edit face", (P("face.eyes.right.pupil.size"), new Vec2(0.3f, 0.3f))), "a right pupil size");
            t.Check(eyes.Left.Pupil.Size.X == 0.3f, "linked eyes: the left eye follows");
            Kit.Ok(t, s.Set(id, P("face.eyes.linked"), false), "eyes unlinked");
            float right0 = eyes.Right.Iris.Emission;
            Kit.Ok(t, Edit(s, id, Opts(s), null, "Edit face", (P("face.eyes.left.iris.emission"), 1.375f)), "an emission with unlinked eyes");
            t.Check(eyes.Left.Iris.Emission == 1.375f && eyes.Right.Iris.Emission == right0, "unlinked eyes: the other eye stays");
            Kit.Ok(t, s.Set(id, P("face.eyes.linked"), true), "eyes linked again");
            var leftEye = DocClone.Clone(eyes.Right);
            leftEye.White = new Color4(0.5f, 0.5f, 0.5f, 1);
            Kit.Ok(t, Edit(s, id, Opts(s), null, "Edit face", (P("face.eyes.left"), leftEye)), "a whole left eye with linked eyes");
            t.Equal(FieldJson.ToText(eyes.Left), FieldJson.ToText(eyes.Right), "linked eyes: a whole eye is mirrored too");
        }

        static void Checks(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var data = new FakeMakerData();
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Change item", (K("clothes.top.item"), "unlisted.mod#5")), CommandCodes.NotFound, "an unlisted clothes row");
            var rep = new MakerEditReport();
            var o = Opts(s, data);
            o.SkipUnlistedRows = true;
            Kit.NoOp(t, Edit(s, id, o, rep, "Change item", (K("clothes.top.item"), "unlisted.mod#5")), "an unlisted row skipped (multi-selection)");
            t.Check(rep.Warnings.Any(w => w.StartsWith("edit.rowSkipped", StringComparison.Ordinal)), "skipped rows warn edit.rowSkipped");
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Change item", (K("clothes.top.item"), FakeMakerData.Row(140, 1))), CommandCodes.NotFound, "a male clothes row on a female");
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Edit body", (P("body.uncensor"), null)), CommandCodes.Unsupported, "body.uncensor is rebuild-only");
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Edit character", (P("studioOverlays.clothes"), false)), CommandCodes.Unsupported, "studioOverlays is rebuild-only");
            var ob = Opts(s, data);
            ob.AllowRebuild = true;
            Kit.Ok(t, Edit(s, id, ob, null, "Edit character", (P("studioOverlays.clothes"), false)), "studioOverlays with rebuild allowed");
            var body = DocClone.Clone(s.Model.Find(id).Appearance.Stored.Chara.Body);
            body.Shape[0] = 1.75f;
            Kit.Ok(t, Edit(s, id, Opts(s, data), null, "Edit body", (P("body"), body)), "a whole body with the same uncensor");
            body.Uncensor = null;
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Edit body", (P("body"), body)), CommandCodes.Unsupported, "a whole body that changes uncensor");
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Edit", ("chara.face.eyesOpen", 0.5f)), CommandCodes.BadArgs, "a scene path is not a Maker edit");
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Edit face", (P("face.shape[0]"), float.NaN)), CommandCodes.BadArgs, "NaN through the planner");
            int depth = s.Commands.UndoList.Count;
            var before = SceneTests_Appearance.CharaBytes(s, id);
            Kit.Fails(t, Edit(s, id, Opts(s, data), null, "Edit face", (P("face.shape[0]"), 0.25f), (P("face.shape[1]"), "x")), CommandCodes.BadArgs, "a failing second set");
            t.Check(s.Commands.UndoList.Count == depth && Kit.Same(before, SceneTests_Appearance.CharaBytes(s, id)), "a failed edit leaves no trace (the first set reverted)");
            Kit.Ok(t, Edit(s, id, Opts(s, data), null, "Edit face", (P("face.shape[0]"), 2f), (P("face.shape[1]"), -1f)), "values typed outside 0..1");
            t.Check(s.Model.Find(id).Appearance.Stored.Chara.Face.Shape[0] == 2f, "stored unclamped");
            // categories per sex
            t.Equal(110, MakerEdits.CategoryOf(P("face.head"), 0), "category: male head 110");
            t.Equal(241, MakerEdits.CategoryOf(K("clothes.bottom.item"), 1), "category: female bottom 241");
            t.Equal(144, MakerEdits.CategoryOf(K("clothes.gloves.item"), 0), "category: male gloves 144");
            t.Equal(348, MakerEdits.CategoryOf(K("clothes.top.colors[2].pattern"), 1), "category: pattern 348");
            t.Equal(302, MakerEdits.CategoryOf(P("hair.side.item"), 1), "category: side hair 302");
            t.Equal(8, MakerEdits.CategoryOf(P("body.paint[1].layoutItem"), 1), "category: body paint layout 8");
            t.Equal(-1, MakerEdits.CategoryOf(P("face.shape[0]"), 1), "category: not a ref member");
            t.Check(MakerEdits.IsItemChange(new[] { K("clothes.top.item") }, 1) && !MakerEdits.IsItemChange(new[] { P("face.shape[0]") }, 1), "IsItemChange");
            t.Equal("hair", MakerEdits.AreaOf(new[] { P("hair.front.baseColor"), P("face.shape[0]") }), "AreaOf takes the first path");
        }

        static void AccessoryOps(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var node = s.Model.Find(id);
            var coord = node.Appearance.Stored.Coord;
            var map = ((CharaNodeState)node.Doc.State).State.Accessories;
            var data = new FakeMakerData();
            string a0 = coord.Accessories[0].Id, a1 = coord.Accessories[1].Id;
            t.Check(map.ContainsKey(a0) && map.ContainsKey(a1), "the state map holds the golden accessories");
            var rep = new MakerEditReport();
            CommandResult r;
            using (var tx = s.Commands.BeginOrJoin("Add accessory"))
            {
                r = MakerEdits.AddAccessory(tx, s.Model, node, 356, FakeMakerData.Row(356, 4), null, 1, Opts(s, data), rep);
                if (r.Ok) tx.Commit();
            }
            Kit.Ok(t, r, "add an accessory at index 1");
            string nid = rep.NewAccessoryIds.Single();
            t.Check(coord.Accessories[1].Id == nid && coord.Accessories[1].Parent == (data.AccessoryParent(356, FakeMakerData.Row(356, 4)) ?? ""), "add: inserted with the row's parent");
            t.Check(((CharaNodeState)node.Doc.State).State.Accessories.TryGetValue(nid, out bool shown) && shown, "add: the state map gets the new id (shown)");
            t.Equal(2, s.Commands.UndoList.Count, "add: one entry");
            s.Commands.Undo();
            t.Check(coord.Accessories.Count == 2 && !((CharaNodeState)node.Doc.State).State.Accessories.ContainsKey(nid), "add: undo removes entry and key");
            s.Commands.Redo();
            using (var tx = s.Commands.BeginOrJoin("Add accessory"))
            {
                r = MakerEdits.AddAccessory(tx, s.Model, node, 350, FakeMakerData.Row(350, 0), null, null, Opts(s, data), null);
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs, "add: category 350 refused");
                r = MakerEdits.AddAccessory(tx, s.Model, node, 352, "unlisted.mod#1", null, null, Opts(s, data), null);
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.NotFound, "add: an unlisted row refused");
            }
            // move
            using (var tx = s.Commands.BeginOrJoin("Reorder accessories"))
            {
                r = MakerEdits.MoveAccessory(tx, s.Model, node, a1, 0, null);
                tx.Commit();
            }
            t.Check(r.Ok && coord.Accessories[0].Id == a1, "move: the accessory goes to index 0");
            // copy to a new slot, mirrored parent
            rep = new MakerEditReport();
            using (var tx = s.Commands.BeginOrJoin("Copy accessory"))
            {
                r = MakerEdits.CopyAccessory(tx, s.Model, node, a0, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, Opts(s, data), rep);
                tx.Commit();
            }
            string cid = rep.NewAccessoryIds.Single();
            var copy = coord.FindAccessory(cid);
            int copySlot = MakerEdits.AccessorySlots(coord).SlotOfId(cid);
            t.Check(r.Ok && copy != null && copy.Parent == (MakerEdits.AccessoryDefaultParent(data, 352, copy.Item) == "N_Earring_R" ? "" : "N_Earring_R") && copy.Item == coord.FindAccessory(a0).Item && coord.Accessories.Last() == copy && copy.ImportSlot == copySlot &&
                    copySlot >= 0, "copy to a new slot («複製», 4.5): the lowest free slot as its preferred slot, appended, parent mirrored _L → _R (stored \"\" when that is the row's default)");
            t.Check(copy.BoneMods.Count == 0 && copy.MaterialOverrides.Count == 0, "copy: plugin data is not copied (vanilla members only)");
            // copy move set 2 mirrored left-right into the other slot
            using (var tx = s.Commands.BeginOrJoin("Copy accessory"))
            {
                r = MakerEdits.CopyAccessory(tx, s.Model, node, a0, a1, AccessoryCopyParts.Move2, false, MirrorAxis.LeftRight, Opts(s, data), null);
                tx.Commit();
            }
            var m = coord.FindAccessory(a1).Moves[1];
            t.Check(r.Ok && m.Rotation.Y == 270f && m.Rotation.Z == 360f && m.Position.Y == -0.5f, "copy move set 2 mirrored: rotation.y 90 → 270, the rest as copied (" + FieldJson.ToText(m) + ")");
            t.Check(MakerEdits.Mirrored(new AccessoryMove { Rotation = new Vec3(350, 0, 0) }, MirrorAxis.UpDown).Rotation.X == 170f, "up-down mirror: x + 180 mod 360");
            t.Equal("N_Hand_L", MakerEdits.MirrorParent("N_Hand_R"), "MirrorParent _R → _L");
            t.Equal("N_Neck", MakerEdits.MirrorParent("N_Neck"), "MirrorParent keeps unpaired keys");
            // copy the whole slot into another slot keeps the destination id
            using (var tx = s.Commands.BeginOrJoin("Copy accessory"))
            {
                r = MakerEdits.CopyAccessory(tx, s.Model, node, a0, a1, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), null);
                tx.Commit();
            }
            t.Check(r.Ok && coord.FindAccessory(a1).Category == 352 && coord.FindAccessory(a1).Item == coord.FindAccessory(a0).Item, "copy slot into another: id kept, members copied");
            // remove
            using (var tx = s.Commands.BeginOrJoin("Remove accessory"))
            {
                r = MakerEdits.RemoveAccessory(tx, s.Model, node, a0, null);
                tx.Commit();
            }
            t.Check(r.Ok && coord.FindAccessory(a0) == null && !((CharaNodeState)node.Doc.State).State.Accessories.ContainsKey(a0), "remove: entry and state key gone");
            s.Commands.Undo();
            t.Check(coord.FindAccessory(a0) != null && ((CharaNodeState)node.Doc.State).State.Accessories.ContainsKey(a0), "remove: undo restores entry and key");
            while (s.Commands.UndoList.Count > 1) s.Commands.Undo();
            var fresh = SceneTests_Appearance.GoldenChara(t, out string id2);
            t.Check(Kit.Same(SceneTests_Appearance.CoordBytes(s, id), SceneTests_Appearance.CoordBytes(fresh, id2)), "accessories: undo all restores the golden coordinate");
            Kit.Valid(t, s, "accessory ops");
        }

        static void Preset(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var st = s.Model.Find(id).Appearance.Stored;
            var preset = DocClone.Clone(st.Chara.Face);
            preset.Head = FakeMakerData.Row(210, 3);
            preset.Skin = FakeMakerData.Row(211, 5);
            preset.Shape = Enumerable.Repeat(0.125f, 59).ToArray();
            preset.Mole.Item = FakeMakerData.Row(323, 2);
            var rep = new MakerEditReport();
            Kit.Ok(t, Edit(s, id, Opts(s), rep, "Face preset", MakerEdits.FacePresetSets(preset).ToArray()), "face preset push");
            t.Check(st.Chara.Face.Head == preset.Head && st.Chara.Face.Skin == preset.Skin && st.Chara.Face.Shape[30] == 0.125f && st.Chara.Face.Mole.Item == preset.Mole.Item,
                "preset: the whole face block (head included)");
            t.Check(rep.Consequences.Count == 0, "preset: the preset's own skin is not snapped");
            t.Equal(2, s.Commands.UndoList.Count, "preset: one entry");
            t.Check(FieldJson.ToText(st.Chara.Face) == FieldJson.ToText(preset), "preset: the face equals the preset");
            s.Commands.Undo();
            t.Check(st.Chara.Face.Head != preset.Head, "preset: undo");
            // a preset naming unlisted rows (a decision): the listed members are set, the unlisted ones keep the
            // character's value with edit.presetUnresolved, still one entry
            var before = DocClone.Clone(st.Chara.Face);
            var partial = DocClone.Clone(preset);
            partial.Head = "illusion.hs2#80";                       // not listed (like Roy12.Sayuri#81's preset)
            partial.Eyelashes.Item = "unlisted.mod#3";              // not listed
            partial.Makeup.Lip.Item = FakeMakerData.Row(322, 7);    // listed
            int depth = s.Commands.UndoList.Count;
            rep = new MakerEditReport();
            CommandResult pr;
            using (var tx = s.Commands.BeginOrJoin("Face preset"))
            {
                pr = MakerEdits.ApplyFacePreset(tx, s.Model, s.Model.Find(id), partial, Opts(s), rep);
                if (pr.Ok) tx.Commit();
            }
            Kit.Ok(t, pr, "a preset with unlisted rows is applied partially");
            // an unlisted head falls back to the sex's first listed head (HS2's head 0) with the skin snap (HF4, 4.1 impl);
            // other unlisted members keep the character's value
            var data0 = new FakeMakerData();
            string head0 = data0.FirstRow(210, 1);
            var skins0 = data0.SkinsForHead(1, head0);
            string skin0 = skins0.Count > 0 && !skins0.Contains(partial.Skin) ? skins0[0] : partial.Skin;
            t.Check(st.Chara.Face.Head == head0 && st.Chara.Face.Eyelashes.Item == before.Eyelashes.Item, "partial preset: unlisted head → the first listed head (HS2's head 0); unlisted eyelashes keep the character's value");
            t.Check(st.Chara.Face.Skin == skin0 && st.Chara.Face.Makeup.Lip.Item == partial.Makeup.Lip.Item && st.Chara.Face.Shape[0] == 0.125f && st.Chara.Face.Mole.Item == partial.Mole.Item,
                "partial preset: the listed members and the values are set, the skin snapped to the fallback head's list");
            t.Check(rep.Warnings.Count(w => w.StartsWith("edit.presetUnresolved: face.head illusion.hs2#80", StringComparison.Ordinal)) == 1 &&
                    rep.Warnings.Count(w => w.StartsWith("edit.presetUnresolved: face.eyelashes.item unlisted.mod#3", StringComparison.Ordinal)) == 1 &&
                    rep.Warnings.Count(w => w.StartsWith("edit.presetUnresolved", StringComparison.Ordinal)) == 2,
                "partial preset: one edit.presetUnresolved per unlisted member (" + string.Join("; ", rep.Warnings) + ")");
            t.Equal(depth + 1, s.Commands.UndoList.Count, "partial preset: one entry");
            s.Commands.Undo();
            t.Equal(FieldJson.ToText(before), FieldJson.ToText(st.Chara.Face), "partial preset: undo restores the face");
            // without lists the preset is used as is
            var noLists = MakerEdits.ListedPreset(partial, before, 1, null, rep = new MakerEditReport());
            t.Check(noLists.Head == "illusion.hs2#80" && rep.Warnings.Any(w => w.StartsWith("edit.listsUnavailable", StringComparison.Ordinal)), "preset without lists: as is + edit.listsUnavailable");
        }

        sealed class FakeImporter : ICharaImporter
        {
            public CharaSubtree Subtree;
            public int Calls;
            public CharaContainer Import(CardAppearanceSource source, FindingList findings)
            {
                Calls++;
                return new CharaContainer { Subtree = Subtree.Clone() };
            }
        }

        static void Import(TestRunner t)
        {
            var s = Kit.Session(5);
            var card = Kit.Card("importme", "UserData/chara/female/importme.png", 0);
            var add = s.AddChara(new CharaAppearance { Card = card });
            string id = add.NewIds[0];
            var node = s.Model.Find(id);
            var saved = CharaImport.Importer;
            var imp = new FakeImporter { Subtree = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara] };
            try
            {
                CharaImport.Importer = null;
                using (var tx = s.Commands.BeginOrJoin("Edit face"))
                {
                    var r0 = MakerEdits.EnsureImported(tx, s.Model, node);
                    t.Check(r0.Status == CommandStatus.Error && r0.Code == CommandCodes.Unsupported, "import: no importer → unsupported");
                }
                CharaImport.Importer = imp;
                var rec = new EventRecorder(s);
                CommandResult r;
                using (var tx = s.Commands.BeginOrJoin("Edit face"))
                {
                    r = MakerEdits.EnsureImported(tx, s.Model, node);
                    if (r.Ok) r = MakerEdits.Edit(tx, s.Model, node, new[] { (P("face.shape[7]"), (object)1.5f) }, Opts(s), null);
                    if (r.Ok) tx.Commit();
                }
                t.Check(r.Ok && imp.Calls == 1 && node.Appearance.Stored != null && node.Appearance.Card == card, "import: the first edit imports the card (card kept)");
                t.Check(node.Appearance.Stored.Chara.Face.Shape[7] == 1.5f, "import: the edit applies to the imported documents");
                t.Check(s.Commands.UndoList.Count == 2 && s.Commands.UndoList[0].Label == "Edit face", "import: one entry with the edit's label");
                t.Check(rec.Batches.Any(b => b.Changes.Any(c => c.Kind == SceneChangeKind.AppearanceChanged)), "import: an AppearanceChanged event (rebuild)");
                s.Commands.Undo();
                t.Check(node.Appearance.Stored == null && node.Appearance.Card == card, "import: undo returns to the card-backed appearance");
                s.Commands.Redo();
                t.Check(node.Appearance.Stored != null && node.Appearance.Stored.Chara.Face.Shape[7] == 1.5f, "import: redo");
                Kit.Valid(t, s, "import");
            }
            finally { CharaImport.Importer = saved; }
        }
    }

    public static class SceneTests_AppearanceFuzz
    {
        public const int DefaultSeeds = 2000, DefaultOps = 200, FullSeeds = 10000, FullOps = 300, SmokeSeeds = 100, SmokeOps = 100;

        public static void Run(TestRunner t)
        {
            string env = Environment.GetEnvironmentVariable("HS2N_APPEARANCE_FUZZ");
            if (string.Equals(env, "off", StringComparison.OrdinalIgnoreCase)) { t.Info("appearance fuzz: off (HS2N_APPEARANCE_FUZZ=off; the plain-Mono job runs it)"); return; }
            int seeds = DefaultSeeds, ops = DefaultOps;
            if (string.Equals(env, "full", StringComparison.OrdinalIgnoreCase)) { seeds = FullSeeds; ops = FullOps; }
            // smoke: the Unity SceneModelTest row of run_tests.ps1 -Quick; the default size runs beside it under plain Mono
            else if (string.Equals(env, "smoke", StringComparison.OrdinalIgnoreCase)) { seeds = SmokeSeeds; ops = SmokeOps; }
            else if (int.TryParse(env, out int n) && n > 0) seeds = n;
            var golden = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara];
            var templates = Templates(t, golden);
            var sw = Stopwatch.StartNew();
            int failedSeeds = 0;
            long done = 0, errors = 0, entries = 0, consequences = 0;
            int first = 1;
            if (int.TryParse(Environment.GetEnvironmentVariable("HS2N_APPEARANCE_FUZZ_SEED"), out int one) && one > 0) { first = one; seeds = one; }   // one seed (debugging)
            for (int seed = first; seed <= seeds; seed++)
            {
                var run = new FuzzRun(t, seed, ops, golden, templates, true);
                bool ok = run.Go();
                done += run.OpsDone;
                errors += run.Errors;
                entries += run.MaxDepth;
                consequences += run.Consequences;
                if (!ok)
                {
                    failedSeeds++;
                    int min = Minimal(t, seed, run.OpsDone, golden, templates);
                    t.Info("fuzz: seed " + seed + " fails first with the prefix of " + min + " operations; op list: " + string.Join(" | ", FuzzRun.Replay(t, seed, min, golden, templates).Skip(Math.Max(0, min - 12))));
                    if (failedSeeds >= 3) { t.Info("stopping after 3 failed seeds"); break; }
                }
            }
            t.Info("by kind: " + string.Join(", ", FuzzRun.ByKind.OrderByDescending(x => x.Value.Item1).Select(x => x.Key + " " + x.Value.Item1.ToString("0") + "/" + x.Value.Item2)));
            t.Info("time: ops " + FuzzRun.TimeOp.ToString("0") + " ms, validate " + FuzzRun.TimeValidate.ToString("0") + " ms, state hash " + FuzzRun.TimeHash.ToString("0") + " ms");
            t.Check(failedSeeds == 0, "appearance fuzz: " + seeds + " seeds × " + ops + " operations, " + failedSeeds + " seeds failed");
            t.Info("appearance fuzz: " + done + " operations (" + errors + " refused with an error code), " + consequences + " consequence sets, deepest history total " +
                   entries + ", " + sw.ElapsedMilliseconds + " ms");
        }

        static int Minimal(TestRunner t, int seed, int upTo, CharaSubtree golden, List<string> templates)
        {
            for (int n = 1; n <= upTo; n++)
                if (!new FuzzRun(t, seed, n, golden, templates, false).Go()) return n;
            return upTo;
        }

        /// <summary>Leaf and object path templates of the golden documents ({acc} = an accessory id).</summary>
        static List<string> Templates(TestRunner t, CharaSubtree golden)
        {
            var list = new List<string>();
            var accIds = new HashSet<string>(golden.Coord.Accessories.Select(a => a.Id));
            void Walk(string path, JToken j)
            {
                if (path.EndsWith(".ext", StringComparison.Ordinal) || path.Contains("future")) return;
                list.Add(path);
                if (path.EndsWith(".materialOverrides", StringComparison.Ordinal)) return;
                if (j is JObject o) foreach (var p in o.Properties()) Walk(path + "." + p.Name, p.Value);
                else if (j is JArray a && !(a.Count > 0 && a.All(x => x.Type == JTokenType.Float || x.Type == JTokenType.Integer) && a.Count <= 4 && !path.EndsWith("shape", StringComparison.Ordinal)))
                {
                    if (path == AppearancePaths.AccessoryList)
                    {
                        var e = a.First as JObject;
                        if (e != null) foreach (var p in e.Properties()) Walk(AppearancePaths.Accessory("{acc}", p.Name), p.Value);
                        return;
                    }
                    for (int i = 0; i < a.Count; i++) Walk(path + "[" + i + "]", a[i]);
                }
            }
            Walk(AppearancePaths.CharaPrefix, DocReader.ParseText(Kit.Text(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, golden.Chara.Write)), CharaDocument.EntryName));
            Walk(AppearancePaths.CoordPrefix, DocReader.ParseText(Kit.Text(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, golden.Coord.Write)), CoordDocument.EntryName));
            list.RemoveAll(p => p == AppearancePaths.CharaPrefix || p == AppearancePaths.CoordPrefix);
            return list;
        }

        sealed class FuzzRun
        {
            readonly TestRunner t;
            readonly int seed, ops;
            readonly bool report;
            readonly Random g;
            readonly SceneSession s;
            readonly List<string> templates;
            readonly FakeMakerData data = new FakeMakerData();
            readonly List<byte[][]> states = new List<byte[][]>();
            readonly List<string> log = new List<string>();
            readonly byte[] initialBytes;
            readonly byte[][] initial;
            readonly string[] charas;
            readonly CharaSubtree loadSource;
            public int OpsDone, Errors, MaxDepth, Consequences;
            public static double TimeOp, TimeValidate, TimeHash;
            public static Dictionary<string, (double, int)> ByKind = new Dictionary<string, (double, int)>();
            string lastOp = "start";
            bool failed;

            public FuzzRun(TestRunner t, int seed, int ops, CharaSubtree golden, List<string> templates, bool report)
            {
                this.t = t;
                this.seed = seed;
                this.ops = ops;
                this.report = report;
                this.templates = templates;
                g = new Random(seed * 7919 + 11);
                s = Kit.Session((ulong)seed, new SessionOptions { History = new HistoryLimits { MaxEntries = int.MaxValue, MaxBytes = long.MaxValue } });
                var female = golden.Clone();
                var male = golden.Clone();
                male.Chara.Profile.Sex = 0;
                male.Coord.Sex = 0;
                var a = s.AddChara(new CharaAppearance { Stored = female });
                var b = s.AddChara(new CharaAppearance { Stored = male });
                charas = new[] { a.NewIds[0], b.NewIds[0] };
                // 4.6: the source of the partial loads (every section differs; plugin entries of both classes, in another order)
                loadSource = golden.Clone();
                loadSource.Chara.Profile.Name = "fuzz load source";
                loadSource.Chara.Profile.Futanari = true;
                loadSource.Chara.Face.Shape[0] = 0.875f;
                loadSource.Chara.Body.Skin.Gloss = 0.125f;
                loadSource.Chara.Hair.ShaderType = 1;
                loadSource.Chara.Body.SkinOverlays.RemoveAt(0);
                loadSource.Chara.Body.BoneMods.Add(new BodyBoneMod { Bone = "cf_J_FaceBase" });
                loadSource.Chara.MaterialOverrides.Reverse();
                loadSource.Chara.StudioOverlays.Eyes = !loadSource.Chara.StudioOverlays.Eyes;
                loadSource.Chara.StudioOverlays.Clothes = !loadSource.Chara.StudioOverlays.Clothes;
                loadSource.Coord.Name = "fuzz load outfit";
                s.Commands.ClearHistory();
                initialBytes = Kit.Bytes(s);
                s.Events.Changed += OnChanged;
                initial = State();
                states.Add(initial);
            }

            public static List<string> Replay(TestRunner t, int seed, int n, CharaSubtree golden, List<string> templates)
            {
                var r = new FuzzRun(t, seed, n, golden, templates, false);
                r.Go();
                return r.log;
            }

            // ---------------- state capture ----------------
            // The state is the scene's canonical bytes plus chara.json / coord.json of both characters, kept per part and
            // recomputed only for the parts the change events name (the events are the realization's only notification,
            // 5); every 50th operation and at the end the cached state is compared with a full recomputation.

            readonly Dictionary<string, byte[]> parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            readonly HashSet<string> dirty = new HashSet<string>(StringComparer.Ordinal) { "scene" };

            void OnChanged(SceneChangeBatch b)
            {
                foreach (var c in b.Changes)
                {
                    if (c.Kind == SceneChangeKind.NodeChanged && AppearancePaths.TrySplit(c.Path, out var doc, out _))
                    {
                        dirty.Add(c.NodeId + (doc == AppearanceDocument.Chara ? "/c" : "/k"));
                        continue;
                    }
                    if (c.Kind == SceneChangeKind.AppearanceChanged) { dirty.Add(c.NodeId + "/c"); dirty.Add(c.NodeId + "/k"); }
                    dirty.Add("scene");
                }
            }

            byte[] Part(string key)
            {
                if (key == "scene") return Kit.Bytes(s);
                var n = s.Model.Find(key.Substring(0, key.Length - 2));
                var st = n?.Appearance?.Stored;
                if (st == null) return new byte[0];
                if (key.EndsWith("/c", StringComparison.Ordinal)) return CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, st.Chara.Write);
                // coord.json plus the fingerprint of the coordinate-scope ext data (4.4: a coordinate load replaces it)
                var coord = CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, st.Coord.Write);
                var ext = System.Text.Encoding.ASCII.GetBytes("\n" + SetCoordinateCommand.ExtFingerprint(st.CoordExt));
                var both = new byte[coord.Length + ext.Length];
                Buffer.BlockCopy(coord, 0, both, 0, coord.Length);
                Buffer.BlockCopy(ext, 0, both, coord.Length, ext.Length);
                return both;
            }

            string Differences(byte[][] a, byte[][] b)
            {
                var keys = Keys().ToArray();
                var d = new List<string>();
                for (int i = 0; i < a.Length && i < b.Length; i++)
                    if (!Kit.Same(a[i], b[i])) d.Add(keys[i] + ": " + Kit.Diff(b[i], a[i]));
                return string.Join("; ", d);
            }

            static bool Same(byte[][] a, byte[][] b)
            {
                if (a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++) if (!ReferenceEquals(a[i], b[i]) && !Kit.Same(a[i], b[i])) return false;
                return true;
            }

            IEnumerable<string> Keys()
            {
                yield return "scene";
                foreach (var id in charas) { yield return id + "/c"; yield return id + "/k"; }
            }

            byte[][] State()
            {
                foreach (var k in Keys())
                {
                    if (!dirty.Contains(k) && parts.ContainsKey(k)) continue;
                    var b = Part(k);
                    // keep the old array when equal, so unchanged parts compare by reference
                    if (!(parts.TryGetValue(k, out var old) && Kit.Same(old, b))) parts[k] = b;
                }
                dirty.Clear();
                return Keys().Select(k => parts[k]).ToArray();
            }

            byte[][] FullState() => Keys().Select(Part).ToArray();

            void Fail(string what)
            {
                if (!failed && report) t.Check(false, "appearance fuzz seed " + seed + ", op " + OpsDone + " (" + lastOp + "): " + what);
                failed = true;
            }

            public bool Go()
            {
                for (int i = 0; i < ops && !failed; i++)
                {
                    OpsDone++;
                    Step();
                }
                if (!failed) Finish();
                return !failed;
            }

            // ---------------- the state machine ----------------

            void Step()
            {
                int depth = s.Commands.UndoList.Count;
                var before = states[depth];
                int k = g.Next(100);
                if (k < 9) { Undo(); return; }
                if (k < 14) { Redo(); return; }
                CommandResult r;
                bool merge = false;
                var swOp = Stopwatch.StartNew();
                try { r = Op(k, ref merge); }
                catch (Exception e) { Fail("threw " + e); return; }
                finally { TimeOp += swOp.Elapsed.TotalMilliseconds; string kk = lastOp.Split(' ')[0]; ByKind.TryGetValue(kk, out var tv); ByKind[kk] = (tv.Item1 + swOp.Elapsed.TotalMilliseconds, tv.Item2 + 1); }
                log.Add(lastOp + " → " + (r == null ? "null" : r.Status + (r.Code != null ? " " + r.Code : "")));
                if (r != null && r.Status == CommandStatus.Error)
                {
                    Errors++;
                    if (r.Code == CommandCodes.Internal) { Fail("internal error: " + r.Message); return; }
                }
                var swV = Stopwatch.StartNew();
                try { s.Model.Validate(); }
                catch (Exception e) { Fail(e.Message); return; }
                TimeValidate += swV.Elapsed.TotalMilliseconds;
                if (s.Commands.ActiveTransaction != null) { Fail("a transaction was left open"); return; }
                var swH = Stopwatch.StartNew();
                var after = State();
                if (OpsDone % 50 == 0 && !Same(after, FullState())) { Fail("a document or scene change without a matching change event (cached state differs from the full one)"); return; }
                TimeHash += swH.Elapsed.TotalMilliseconds;
                int nd = s.Commands.UndoList.Count;
                if (nd == depth + 1)
                {
                    states.RemoveRange(depth + 1, states.Count - depth - 1);
                    states.Add(after);
                    if (s.Commands.CanRedo) Fail("redo branch not disposed");
                    MaxDepth = Math.Max(MaxDepth, nd);
                }
                else if (nd == depth)
                {
                    if (!Same(after, before))
                    {
                        if (merge && r != null && r.Ok) { states[depth] = after; states.RemoveRange(depth + 1, states.Count - depth - 1); }
                        else Fail("state changed without a history entry (result " + r + ")");
                    }
                }
                else Fail("history depth " + depth + " → " + nd);
            }

            void Undo()
            {
                lastOp = "undo";
                int depth = s.Commands.UndoList.Count;
                var r = s.Commands.Undo();
                log.Add("undo → " + r.Status);
                if (depth == 0) { if (r.Status != CommandStatus.NoOp) Fail("undo of nothing: " + r); return; }
                if (r.Status != CommandStatus.Ok) { Fail("undo: " + r); return; }
                Check(depth - 1);
            }

            void Redo()
            {
                lastOp = "redo";
                int depth = s.Commands.UndoList.Count;
                bool can = s.Commands.CanRedo;
                var r = s.Commands.Redo();
                log.Add("redo → " + r.Status);
                if (!can) { if (r.Status != CommandStatus.NoOp) Fail("redo of nothing: " + r); return; }
                if (r.Status != CommandStatus.Ok) { Fail("redo: " + r); return; }
                Check(depth + 1);
            }

            void Check(int position)
            {
                try { s.Model.Validate(); }
                catch (Exception e) { Fail(e.Message); return; }
                if (s.Commands.UndoList.Count != position) { Fail("history position " + s.Commands.UndoList.Count + ", expected " + position); return; }
                var now = State();
                if (!Same(now, states[position])) Fail("state at history position " + position + " differs from the recorded one: " + Differences(now, states[position]));
            }

            void Finish()
            {
                lastOp = "undo all";
                int top = states.Count - 1;
                while (s.Commands.CanUndo && !failed)
                {
                    int at = s.Commands.UndoList.Count;
                    var u = s.Commands.Undo();
                    if (!u.Ok) Fail("undo failed at " + at + ": " + u);
                }
                if (failed) return;
                Check(0);
                if (failed) return;
                if (!Kit.Same(initialBytes, Kit.Bytes(s))) { Fail("undo all: scene bytes differ " + Kit.Diff(initialBytes, Kit.Bytes(s))); return; }
                if (!Same(FullState(), initial)) { Fail("undo all: chara.json / coord.json bytes differ from the initial ones"); return; }
                lastOp = "redo all";
                while (s.Commands.CanRedo && !failed)
                    if (!s.Commands.Redo().Ok) Fail("redo failed at " + s.Commands.UndoList.Count);
                if (failed) return;
                if (s.Commands.UndoList.Count != top) { Fail("redo all reached " + s.Commands.UndoList.Count + " of " + top); return; }
                Check(top);
                lastOp = "random walk";
                for (int i = 0; i < 40 && !failed; i++)
                {
                    if (g.Next(2) == 0 && s.Commands.CanUndo) { s.Commands.Undo(); Check(s.Commands.UndoList.Count); }
                    else if (s.Commands.CanRedo) { s.Commands.Redo(); Check(s.Commands.UndoList.Count); }
                }
            }

            // ---------------- random operations ----------------

            bool Chance(double p) => g.NextDouble() < p;
            T Pick<T>(IList<T> l) => l[g.Next(l.Count)];
            string Chara() => Pick(charas);
            CoordDocument CoordOf(string id) => s.Model.Find(id).Appearance.Stored.Coord;
            int SexOf(string id) => s.Model.Find(id).Appearance.Stored.Chara.Profile.Sex;

            MakerEditOptions Opts()
            {
                bool lists = !Chance(0.1);
                return new MakerEditOptions
                {
                    Rows = lists ? data : null, Defaults = Chance(0.9) ? data : null, KeepColours = Chance(0.15), AllowRebuild = Chance(0.05),
                    SkipUnlistedRows = Chance(0.2), NewId = s.NewId,
                    // 4.5: the slot copies' plugin data switch, MoreAccessories, AdditionalAccessoryControls
                    CopyAccessoryPluginData = Chance(0.5), MoreAccessories = !Chance(0.2), AccessoryControls = !Chance(0.2),
                };
            }

            CommandResult Op(int k, ref bool merge)
            {
                if (k < 33) { var (id, p, v) = RandomSet(); lastOp = "set " + p + " = " + Text(v); return s.Set(id, p, v); }
                // Phase 4.4 (the clothes page): «初期カラー», the prefab-default resets, clothes member sets with their
                // consequences, coordinate loads in the three modes (with and without ext data, sometimes the other sex)
                if (k < 34) return InTx("Edit clothes", tx => ClothesDefaultColours(tx));
                if (k < 35) return InTx("Reset value", tx => ClothesPrefabReset(tx));
                if (k < 36) return InTx("Edit clothes", tx => ClothesMemberSet(tx));
                if (k < 37) return InTx("Load coordinate", tx => CoordinateLoad(tx));
                // Phase 4.3: hair presets, skin matches, bundle edits (the together switch both
                // ways) and resets, mesh / shader / switch / item sets
                if (k < 38) return InTx("Edit hair", tx => HairPresetPush(tx));
                if (k < 39) return InTx("Edit hair", tx => HairSkinMatch(tx));
                if (k < 40) return InTx("Edit hair", tx => HairBundleEdit(tx));
                if (k < 41) return InTx("Edit hair", tx => HairSet(tx));
                // Phase 4.2: skin swatch pushes, paint region picks, body shape / bust / toggle sets
                if (k < 42) return InTx("Edit body", tx => SwatchPush(tx));
                if (k < 43) return InTx("Change item", tx => RegionPick(tx));
                if (k < 44) return InTx("Edit body", tx => BodySet(tx));
                if (k < 49) return WholeObject();
                if (k < 64) return InTx("Edit", tx => PlannerEdit(tx));
                if (k < 69) return InTx("Add accessory", tx => Add(tx));
                if (k < 72) return InTx("Remove accessory", tx => Remove(tx));
                if (k < 75) return InTx("Reorder accessories", tx => Move(tx));
                if (k < 78) return InTx("Copy accessory", tx => Copy(tx));
                // Phase 4.5: «初期カラー», «髪の毛の色を取得», parent writes and «初期親»
                if (k < 79) return InTx("Edit accessory", tx => AccessoryEdit(tx));
                if (k < 86) return Transaction();
                if (k < 93)
                {
                    merge = true;
                    var (id, p, v) = RandomSet();
                    lastOp = "typed " + p + " = " + Text(v);
                    return s.Commands.Set(id, p, v, new ExecuteOptions { MergeKey = "ui.typed/" + id + "/" + p });
                }
                if (k < 95) return InTx("Face preset", tx => Chance(0.25) ? PresetFallback(tx) : PresetPush(tx));
                // Phase 4.1: layout pushes, row resets, eye copies
                if (k < 96) return InTx("Edit face", tx => LayoutPush(tx));
                if (k < 97) return InTx("Reset value", tx => ResetRows(tx));
                if (k < 98) return InTx("Edit face", tx => EyeCopy(tx));
                // Phase 4.6: the character load with random flags
                if (k < 99) return InTx("Load character", tx => CharaLoad(tx));
                return Invalid();
            }

            // ---------------- Phase 4.6 character loads ----------------

            // From the second golden-derived source with a few random changes: all five flags swap the whole appearance (Change
            // character's command), fewer replace the chosen sections (SetCharaSectionsCommand) and, with «コーデ», wear the
            // source's coordinate (mode all); sometimes the source is of the other sex (refused unless all five).
            CommandResult CharaLoad(Transaction tx)
            {
                var src = loadSource.Clone();
                src.Chara.Face.Shape[g.Next(FaceData.ShapeCount)] = (float)g.NextDouble();
                src.Chara.Body.Shape[g.Next(BodyData.ShapeCount)] = (float)(g.NextDouble() * 1.5 - 0.25);
                src.Chara.Hair.Back.BaseColor = new Color4((float)g.NextDouble(), 0.5f, 0.25f, 1f);
                if (Chance(0.5)) src.Chara.Profile.Name = "fuzz load " + g.Next(9);
                if (Chance(0.15)) { src.Chara.Profile.Sex = 0; src.Coord.Sex = 0; }
                var flags = (LoadFlags)(1 + g.Next(31));
                var ids = Targets();
                lastOp = "load " + string.Join("+", LoadFlagNames.ToNames(flags)) + " sex " + src.Chara.Profile.Sex + " on " + ids.Length;
                if (LoadFlagNames.IsAll(flags))
                    return EachTarget(ids, (n, o) => tx.Execute(new SetAppearanceCommand(n.Id, new CharaAppearance { Stored = src.Clone() }, "Load character")));
                return EachTarget(ids, (n, o) =>
                {
                    var r = tx.Execute(new SetCharaSectionsCommand(n.Id, src.Chara, flags & SetCharaSectionsCommand.Allowed, HeadNames.Vanilla, "Load character"));
                    if (r.Status == CommandStatus.Error || (flags & LoadFlags.Coordinate) == 0) return r;
                    var c = MakerEdits.ApplyCoordinate(tx, s.Model, n, src.Coord, src.CoordExt, CoordinateApplyMode.All, o, new MakerEditReport());
                    return c.Status == CommandStatus.Error ? c : r.Status == CommandStatus.Ok ? r : c;
                });
            }

            // ---------------- Phase 4.4 clothes ops ----------------

            CommandResult ClothesDefaultColours(Transaction tx)
            {
                int kind = Chance(0.97) ? g.Next(8) : 8;
                var ids = Targets();
                lastOp = "clothes defaults kind " + kind + " on " + ids.Length;
                return EachTarget(ids, (n, o) => MakerEdits.ResetClothesColours(tx, s.Model, n, kind, o, new MakerEditReport()));
            }

            static readonly string[] ClothesResetMembers = { "gloss", "metallic", "layout", "layout#0", "layout#3", "rotation", "base", "patternColor" };

            CommandResult ClothesPrefabReset(Transaction tx)
            {
                foreach (var c in charas) if (!refs.Has(c) && Chance(0.3)) refs.Capture(c, s.Model.Find(c).Appearance.Stored);
                int n = 1 + g.Next(3);
                var paths = new List<string>();
                var defs = new List<object>();
                for (int i = 0; i < n; i++)
                {
                    string m = Pick(ClothesResetMembers);
                    int hash = m.IndexOf('#');
                    string p = AppearancePaths.Coord("clothes." + ClothesSet.Names[g.Next(8)] + ".colors[" + g.Next(3) + "]." + (hash < 0 ? m : m.Substring(0, hash))) + (hash < 0 ? "" : m.Substring(hash));
                    paths.Add(Chance(0.2) ? AppearancePaths.ToRelative(p) : p);
                    defs.Add(Chance(0.5) ? (object)0.5 : null);
                }
                var ids = Targets();
                lastOp = "clothes reset " + string.Join(", ", paths) + " on " + ids.Length;
                return EachTarget(ids, (node, o) => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, s.Model, node, refs, paths, defs, o, new MakerEditReport()));
            }

            CommandResult ClothesMemberSet(Transaction tx)
            {
                string id = Chara();
                int n = 1 + g.Next(3);
                var sets = new List<(string, object)>();
                for (int i = 0; i < n; i++)
                {
                    int kind = g.Next(8);
                    string pre = "clothes." + ClothesSet.Names[kind] + ".";
                    string c = pre + "colors[" + g.Next(3) + "].";
                    switch (g.Next(9))
                    {
                        case 0: sets.Add((AppearancePaths.Coord(pre + "item"), FakeMakerData.Row(MakerEdits.ClothesItemCategory(kind, SexOf(id)), g.Next(FakeMakerData.PoolSize)))); break;
                        case 1: sets.Add((AppearancePaths.Coord(c + "pattern"), Chance(0.7) ? FakeMakerData.Row(348, g.Next(FakeMakerData.PoolSize)) : DocDefaults.NoneRef)); break;
                        case 2: sets.Add((AppearancePaths.Coord(c + "base"), new Color4((float)g.NextDouble(), (float)g.NextDouble(), (float)g.NextDouble(), Chance(0.7) ? 1f : 0.5f))); break;
                        case 3: sets.Add((AppearancePaths.Coord(c + "layout"), new Vec4(F(), F(), F(), F()))); break;
                        case 4: sets.Add((AppearancePaths.Coord(c + (Chance(0.5) ? "gloss" : "metallic")), F())); break;
                        case 5: sets.Add((AppearancePaths.Coord(c + "rotation"), F())); break;
                        case 6: sets.Add((AppearancePaths.Coord(pre + "breakRate"), F())); break;
                        case 7: sets.Add((AppearancePaths.Coord(pre + "hideOptions[" + g.Next(2) + "]"), Chance(0.5))); break;
                        default: sets.Add((AppearancePaths.Coord(c + "patternColor"), new Color4((float)g.NextDouble(), 0.5f, 0.25f, (float)g.NextDouble()))); break;
                    }
                }
                lastOp = "clothes set " + string.Join(", ", sets.Select(x => x.Item1.Substring(AppearancePaths.CoordPrefix.Length + 1) + "=" + Text(x.Item2)));
                return MakerEdits.Edit(tx, s.Model, s.Model.Find(id), sets, Opts(), new MakerEditReport());
            }

            /// <summary>A source coordinate: another character's coordinate (any sex), sometimes changed in a few parts, items,
            /// accessories (one new id), rules and name; ext data with a plugin block and an hs2.kkex entry, or none.</summary>
            (CoordDocument coord, ExtScopeData ext) CoordinateSource()
            {
                var from = s.Model.Find(Chara()).Appearance.Stored;
                var c = CharaSubtree.CloneCoord(from.Coord);
                if (Chance(0.3)) c.Sex = 1 - c.Sex;
                if (Chance(0.6))
                {
                    int kind = g.Next(8);
                    c.Clothes.Parts[kind].Item = FakeMakerData.Row(MakerEdits.ClothesItemCategory(kind, c.Sex), g.Next(FakeMakerData.PoolSize));
                    c.Clothes.Parts[kind].Colors[g.Next(3)].Gloss = F();
                    c.Clothes.Parts[kind].BreakRate = F();
                }
                if (Chance(0.5) && c.Accessories.Count > 0) c.Accessories.RemoveAt(g.Next(c.Accessories.Count));
                if (Chance(0.5))
                {
                    var a = new AccessoryEntry { Id = JsonText.DerivedId("fuzz/coord/" + seed + "/" + OpsDone), Category = 351, Item = FakeMakerData.Row(351, g.Next(FakeMakerData.PoolSize)), Parent = "N_Head" };
                    c.Accessories.Insert(g.Next(c.Accessories.Count + 1), a);
                }
                if (Chance(0.3)) c.RuleOverrides = null;
                if (Chance(0.3)) c.Name = "fuzz " + g.Next(5);
                ExtScopeData ext = null;
                if (Chance(0.6))
                {
                    ext = Chance(0.5) ? from.CoordExt.Clone() : new ExtScopeData(HS2Next.Api.Save.ExtScope.Coord);
                    if (Chance(0.5)) ext.GetOrAdd("fuzz.plugin").Files["d.bin"] = new[] { (byte)g.Next(256) };
                    if (Chance(0.5))
                    {
                        ext.Kkex = new KkexStore { Source = "hs2.coord", BlockVersion = "3" };
                        ext.Kkex.Add("KCOX", "kkex", null, 1, "coord", "source", null, new byte[] { 0x92, (byte)g.Next(100), 0x80 });
                    }
                }
                return (c, ext);
            }

            CommandResult CoordinateLoad(Transaction tx)
            {
                var (coord, ext) = CoordinateSource();
                var mode = (CoordinateApplyMode)g.Next(3);
                var ids = Targets();
                lastOp = "coordinate " + mode + " sex " + coord.Sex + " accessories " + coord.Accessories.Count + (ext == null ? "" : " ext") + " on " + ids.Length;
                return EachTarget(ids, (n, o) => MakerEdits.ApplyCoordinate(tx, s.Model, n, coord, ext, mode, o, new MakerEditReport()));
            }

            // ---------------- Phase 4.3 hair ops ----------------

            static readonly string[] HairPresetBadPaths =
            {
                AppearancePaths.Chara("hair.front.topColor"), AppearancePaths.Chara("hair.back.metallic"), "hair.side.baseColor",
            };

            string[] Targets() => Chance(0.2) ? charas : new[] { Chara() };

            CommandResult EachTarget(string[] ids, Func<TreeNode, MakerEditOptions, CommandResult> step)
            {
                var o = Opts();
                if (ids.Length > 1) o.SkipUnlistedRows = true;
                CommandResult last = CommandResult.NoOp(null);
                foreach (var id in ids)
                {
                    var r = step(s.Model.Find(id), o);
                    if (r.Status == CommandStatus.Error) return r;
                    if (r.Status == CommandStatus.Ok) last = r;
                }
                return last;
            }

            CommandResult HairPresetPush(Transaction tx)
            {
                int part = g.Next(4);
                string path = Chance(0.9) ? (Chance(0.5) ? AppearancePaths.Chara("hair." + HairData.PartNames[part] + ".baseColor") : "chara.hair." + HairData.PartNames[part] + ".baseColor")
                    : Pick(HairPresetBadPaths);
                string row = Chance(0.9) ? "illusion.hs2#" + g.Next(9) : FakeMakerData.Row(305, g.Next(3));
                var ids = Targets();
                lastOp = "hairpreset " + path + " <- " + row + " on " + ids.Length;
                return EachTarget(ids, (n, o) => MakerEdits.ApplyColourPreset(tx, s.Model, n, path, row, o, new MakerEditReport()));
            }

            CommandResult HairSkinMatch(Transaction tx)
            {
                int part = Chance(0.95) ? g.Next(4) : 4;
                var ids = Targets();
                lastOp = "hairskin part " + part + " on " + ids.Length;
                return EachTarget(ids, (n, o) => MakerEdits.MatchHairToSkin(tx, s.Model, n, part, o, new MakerEditReport()));
            }

            Vec3? V3OrNull(double p) => Chance(p) ? new Vec3(F(), F(), F()) : (Vec3?)null;

            CommandResult HairBundleEdit(Transaction tx)
            {
                int part = Chance(0.97) ? g.Next(4) : 5;
                var ids = Targets();
                if (Chance(0.15))
                {
                    lastOp = "hairbundle reset all part " + part + " on " + ids.Length;
                    return EachTarget(ids, (n, o) => MakerEdits.ResetHairBundles(tx, s.Model, n, part, o, new MakerEditReport()));
                }
                int index = g.Next(-1, 6);
                bool reset = Chance(0.2);
                int axis = reset && Chance(0.5) ? g.Next(-1, 3) : -1;
                Vec3? move = V3OrNull(0.5), rotate = V3OrNull(0.4);
                bool? noShake = Chance(0.25) ? Chance(0.5) : (bool?)null;
                if (!reset && move == null && rotate == null && noShake == null && Chance(0.8)) move = new Vec3(F(), F(), F());
                // a drag: several preview frames of one bundle in this transaction (the first may materialize)
                int frames = !reset && Chance(0.2) ? 2 + g.Next(3) : 1;
                lastOp = "hairbundle part " + part + " index " + index + (reset ? " reset axis " + axis : "") + " move " + Text(move) + " rotate " + Text(rotate) +
                         " noShake " + Text(noShake) + " frames " + frames + " on " + ids.Length;
                CommandResult last = CommandResult.NoOp(null);
                for (int fr = 0; fr < frames; fr++)
                {
                    var m = fr == 0 ? move : move.HasValue ? new Vec3(move.Value.X + 0.05f * fr, move.Value.Y, move.Value.Z) : (Vec3?)null;
                    var r = EachTarget(ids, (n, o) => MakerEdits.EditHairBundle(tx, s.Model, n, part, index, m, rotate, noShake, reset, axis, o, new MakerEditReport()));
                    if (r.Status == CommandStatus.Error) return r;
                    if (r.Status == CommandStatus.Ok) last = r;
                }
                return last;
            }

            CommandResult HairSet(Transaction tx)
            {
                string id = Chara();
                int n = 1 + g.Next(3);
                var sets = new List<(string, object)>();
                for (int i = 0; i < n; i++)
                {
                    string pre = "hair." + HairData.PartNames[g.Next(4)] + ".";
                    switch (g.Next(9))
                    {
                        case 0: sets.Add((AppearancePaths.Chara("hair.editor.ctrlTogether"), Chance(0.5))); break;
                        case 1: sets.Add((AppearancePaths.Chara(Chance(0.5) ? "hair.editor.sameSetting" : "hair.editor.autoSetting"), Chance(0.5))); break;
                        case 2: sets.Add((AppearancePaths.Chara("hair.shaderType"), g.Next(2))); break;
                        case 3: sets.Add((AppearancePaths.Chara(pre + "mesh.item"), Chance(0.7) ? FakeMakerData.Row(306, g.Next(FakeMakerData.PoolSize)) : DocDefaults.NoneRef)); break;
                        case 4: sets.Add((AppearancePaths.Chara(pre + "mesh.layout"), new Vec4(F(), F(), F(), F()))); break;
                        case 5: sets.Add((AppearancePaths.Chara(pre + "mesh.color"), new Color4((float)g.NextDouble(), (float)g.NextDouble(), (float)g.NextDouble(), Chance(0.5) ? 0f : 1f))); break;
                        case 6: sets.Add((AppearancePaths.Chara(pre + "item"), FakeMakerData.Row(300 + g.Next(4), g.Next(FakeMakerData.PoolSize)))); break;
                        case 7: sets.Add((AppearancePaths.Chara(pre + "baseColor"), new Color4((float)g.NextDouble(), (float)g.NextDouble(), (float)g.NextDouble(), Chance(0.8) ? 1f : 0.5f))); break;
                        default: sets.Add((AppearancePaths.Chara(pre + (Chance(0.5) ? "metallic" : "smoothness")), F())); break;
                    }
                }
                lastOp = "hairset " + string.Join(", ", sets.Select(x => x.Item1.Substring(AppearancePaths.CharaPrefix.Length + 1) + "=" + Text(x.Item2)));
                return MakerEdits.Edit(tx, s.Model, s.Model.Find(id), sets, Opts(), new MakerEditReport());
            }

            // ---------------- Phase 4.2 body ops ----------------

            static readonly string[] SwatchBadPaths =
            {
                AppearancePaths.Chara("body.nail.color"), AppearancePaths.Chara("hair.front.topColor"), AppearancePaths.Chara("body.skin"), "body.skin.color",
            };

            CommandResult SwatchPush(Transaction tx)
            {
                string path = Chance(0.9) ? (Chance(0.5) ? AppearancePaths.Chara("body.skin.color") : "chara.body.skin.color") : Pick(SwatchBadPaths);
                string row = Chance(0.9) ? "illusion.hs2#" + g.Next(9) : FakeMakerData.Row(336, g.Next(3));
                var ids = Chance(0.2) ? charas : new[] { Chara() };
                lastOp = "swatch push " + path + " ← " + row + " on " + ids.Length;
                var o = Opts();
                CommandResult last = CommandResult.NoOp(null);
                foreach (var id in ids)
                {
                    var r = MakerEdits.ApplyColourPreset(tx, s.Model, s.Model.Find(id), path, row, o, new MakerEditReport());
                    if (r.Status == CommandStatus.Error) return r;
                    if (r.Ok) last = r;
                }
                return last;
            }

            CommandResult RegionPick(Transaction tx)
            {
                string id = Chara();
                int k = g.Next(2);
                string row = Chance(0.85) ? FakeMakerData.Row(8, g.Next(FakeMakerData.PoolSize)) : Chance(0.5) ? FakeMakerData.Row(7, g.Next(3)) : "illusion.hs2#" + g.Next(25);
                lastOp = "region pick body.paint[" + k + "].layoutItem ← " + row;
                return MakerEdits.Edit(tx, s.Model, s.Model.Find(id), new List<(string, object)> { (AppearancePaths.Chara("body.paint[" + k + "].layoutItem"), row) }, Opts(), new MakerEditReport());
            }

            CommandResult BodySet(Transaction tx)
            {
                string id = Chara();
                int n = 1 + g.Next(3);
                var sets = new List<(string, object)>();
                for (int i = 0; i < n; i++)
                {
                    switch (g.Next(6))
                    {
                        case 0: case 1: sets.Add((AppearancePaths.Chara("body.shape[" + g.Next(BodyData.ShapeCount) + "]"), F())); break;
                        case 2: sets.Add((AppearancePaths.Chara("body.shape[1]"), Chance(0.5) ? 0.5f : F())); break;   // bust size around 0.5 (breast normals)
                        case 3: sets.Add((AppearancePaths.Chara(Chance(0.5) ? "body.bust.softness" : "body.bust.weight"), F())); break;
                        case 4: sets.Add((AppearancePaths.Chara("body.nipple.areola"), F())); break;
                        default: sets.Add((MakerEdits.FutanariPath, Chance(0.5))); break;
                    }
                }
                lastOp = "body set " + string.Join(", ", sets.Select(x => x.Item1.Substring(AppearancePaths.CharaPrefix.Length + 1) + "=" + Text(x.Item2)));
                return MakerEdits.Edit(tx, s.Model, s.Model.Find(id), sets, Opts(), new MakerEditReport());
            }

            // ---------------- Phase 4.1 face ops ----------------

            // the reset references of this run's scene session (per session since 4.2)
            EditReferences refs => s.MakerReferences;

            static readonly string[] LayoutTargets =
            {
                AppearancePaths.Chara("face.mole.layout"), AppearancePaths.Chara("face.makeup.paint[0].layout"), "chara.face.makeup.paint[1].layout",
                AppearancePaths.Chara("face.eyebrow.layout"), AppearancePaths.Chara("body.paint[0].layout"),
            };

            CommandResult LayoutPush(Transaction tx)
            {
                string id = Chara();
                string path = Pick(LayoutTargets);
                string row = Chance(0.9) ? "illusion.hs2#" + g.Next(9) : FakeMakerData.Row(6, g.Next(3));
                lastOp = "layout push " + path + " ← " + row;
                return MakerEdits.ApplyLayoutPreset(tx, s.Model, s.Model.Find(id), path, row, Opts(), new MakerEditReport());
            }

            static readonly (string path, object def)[] ResetPool =
            {
                (AppearancePaths.Chara("face.eyebrow.tilt"), 0.5), (AppearancePaths.Chara("face.eyebrow.color"), new[] { 0.05, 0.05, 0.05, 1.0 }),
                (AppearancePaths.Chara("face.eyebrow.layout") + "#2", 0.666), (AppearancePaths.Chara("face.mole.layout") + "#0", 0.5),
                (AppearancePaths.Chara("face.eyes.left.iris.size") + "#1", 0.666), (AppearancePaths.Chara("face.eyes.right.white"), new[] { 0.846, 0.846, 0.846, 1.0 }),
                (AppearancePaths.Chara("face.eyes.linked"), true), (AppearancePaths.Chara("face.eyes.left.iris.emission"), 0.0),
                (AppearancePaths.Chara("face.makeup.paint[1].rotation"), 0.5), ("chara.face.shape[12]", 0.5), (AppearancePaths.Chara("face.detail.power"), 1.0),
                (AppearancePaths.Chara("face.makeup.lip.color"), new[] { 1.0, 1.0, 1.0, 1.0 }), (AppearancePaths.Chara("hair.front.baseColor"), null),
                (AppearancePaths.Chara("face.mole.layout") + "#7", 0.5), (AppearancePaths.Chara("face.eyebrow.tilt"), "wrong type"),
                // Phase 4.2 body rows
                (AppearancePaths.Chara("body.shape[1]"), 0.5), (AppearancePaths.Chara("body.skin.color"), new[] { 0.8, 0.7, 0.64, 1.0 }),
                (AppearancePaths.Chara("body.paint[0].layout") + "#2", 0.5), (AppearancePaths.Chara("body.bust.weight"), 0.5),
                (AppearancePaths.Chara("body.shape[32]"), 0.0), (MakerEdits.FutanariPath, false),
            };

            CommandResult ResetRows(Transaction tx)
            {
                // a reference is captured at the first "session" of a character (sometimes never: the defaults apply)
                foreach (var c in charas) if (!refs.Has(c) && Chance(0.3)) refs.Capture(c, s.Model.Find(c).Appearance.Stored);
                int n = 1 + g.Next(4);
                var paths = new List<string>();
                var defs = new List<object>();
                for (int i = 0; i < n; i++)
                {
                    if (Chance(0.15)) { string tp = Instantiate(Pick(templates), Chara()); paths.Add(tp); defs.Add(null); continue; }
                    var (p, d) = Pick(ResetPool);
                    paths.Add(p);
                    defs.Add(Chance(0.8) ? d : null);
                }
                var ids = Chance(0.3) ? charas : new[] { Chara() };
                lastOp = "reset " + string.Join(", ", paths) + " on " + ids.Length;
                CommandResult last = CommandResult.NoOp(null);
                foreach (var id in ids)
                {
                    var r = MakerEdits.ResetAppearance(tx, s.Model, s.Model.Find(id), refs, paths, defs, Opts(), new MakerEditReport());
                    if (r.Status == CommandStatus.Error) return r;
                    if (r.Ok) last = r;
                }
                return last;
            }

            CommandResult EyeCopy(Transaction tx)
            {
                string id = Chara();
                bool lr = Chance(0.5);
                lastOp = "eye copy " + (lr ? "left → right" : "right → left");
                return MakerEdits.CopyEye(tx, s.Model, s.Model.Find(id), lr, Opts(), new MakerEditReport());
            }

            CommandResult PresetFallback(Transaction tx)
            {
                string id = Chara();
                string head = Chance(0.8) ? FakeMakerData.Row(SexOf(id) == 0 ? 110 : 210, g.Next(FakeMakerData.PoolSize)) : "illusion.hs2#" + (80 + g.Next(3));
                lastOp = "face preset fallback on " + id.Substring(0, 6) + " head " + head;
                return MakerEdits.FacePresetFallback(tx, s.Model, s.Model.Find(id), head, Opts(), new MakerEditReport());
            }

            static string Text(object v)
            {
                try { var x = FieldJson.ToText(v); return x.Length > 60 ? x.Substring(0, 60) + "…" : x; }
                catch (Exception) { return v?.ToString() ?? "null"; }
            }

            CommandResult InTx(string label, Func<Transaction, CommandResult> body)
            {
                using (var tx = s.Commands.BeginOrJoin(label))
                {
                    var r = body(tx);
                    if (r == null || r.Status == CommandStatus.Error) return r;   // Dispose reverts
                    var c = tx.Commit();
                    return c.Status == CommandStatus.Error ? c : r;
                }
            }

            string Instantiate(string template, string id)
            {
                if (template.IndexOf("{acc}", StringComparison.Ordinal) < 0) return template;
                var accs = CoordOf(id).Accessories;
                string acc = accs.Count > 0 && !Chance(0.03) ? Pick(accs).Id : JsonText.DerivedId("fuzz/missing/" + g.Next(4));
                return template.Replace("{acc}", acc);
            }

            (string id, string path, object value) RandomSet()
            {
                string id = Chara();
                string path = Instantiate(Pick(templates), id);
                Type leaf = null;
                object cur = null;
                try
                {
                    leaf = FieldAccessor.LeafType(s.Model, id, path);
                    cur = FieldAccessor.Get(s.Model, id, path);
                }
                catch (CommandException) { }
                return (id, path, Value(id, path, leaf, cur));
            }

            float F() => (float)Math.Round(g.NextDouble() * 4.0 - 1.5, 3);

            object Value(string id, string path, Type leaf, object cur)
            {
                if (leaf == null) return Chance(0.5) ? (object)F() : "x";
                if (Chance(0.05)) return FieldSchema_Copy(cur);   // the same value: NoOp
                var u = Nullable.GetUnderlyingType(leaf);
                if (u != null && Chance(0.3)) return null;
                u = u ?? leaf;
                if (u == typeof(float)) return Chance(0.02) ? float.NaN : Chance(0.02) ? -0f : F();
                if (u == typeof(int))
                {
                    if (path.EndsWith(".category", StringComparison.Ordinal)) return Chance(0.1) ? 350 : g.Next(351, 364);
                    return g.Next(-1, 5);
                }
                if (u == typeof(bool)) return Chance(0.5);
                if (u == typeof(Color4)) return new Color4(F(), F(), F(), Chance(0.5) ? 1f : F());
                if (u == typeof(Vec2)) return new Vec2(F(), F());
                if (u == typeof(Vec3)) return new Vec3(F() * 100, F(), F());
                if (u == typeof(Vec4)) return new Vec4(F(), F(), F(), F());
                if (u == typeof(string)) return Str(id, path, cur as string);
                if (u == typeof(float[])) { int n = Chance(0.05) ? 3 : ((float[])cur)?.Length ?? 59; return Enumerable.Range(0, n).Select(_ => F()).ToArray(); }
                if (u == typeof(bool[])) return new[] { Chance(0.5), Chance(0.5) };
                if (u == typeof(Color4[])) return Enumerable.Range(0, Chance(0.05) ? 3 : 4).Select(_ => new Color4(F(), F(), F(), 1f)).ToArray();
                if (u.IsArray) return FieldSchema_Copy(cur);
                if (typeof(IList).IsAssignableFrom(u)) return Chance(0.5) ? Activator.CreateInstance(u) : FieldSchema_Copy(cur);
                if (typeof(DocObject).IsAssignableFrom(u) && !u.IsAbstract)
                {
                    if (cur == null) return u.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(u) : null;
                    return Chance(0.2) && NullableObject(path) ? null : FieldSchema_Copy(cur);
                }
                return FieldSchema_Copy(cur);
            }

            static bool NullableObject(string path) =>
                path.EndsWith(".uncensor", StringComparison.Ordinal) || path.EndsWith(".overlays", StringComparison.Ordinal) || path.EndsWith(".rules", StringComparison.Ordinal) ||
                path.EndsWith(".pushUp", StringComparison.Ordinal) || path.EndsWith(".ruleOverrides", StringComparison.Ordinal);

            static object FieldSchema_Copy(object v)
            {
                switch (v)
                {
                    case null: return null;
                    case DocObject d: return DocClone.Clone(d);
                    case Array a: return a.Clone();
                    case IList l:
                    {
                        var r = (IList)Activator.CreateInstance(v.GetType());
                        foreach (var e in l) r.Add(e is DocObject de ? DocClone.Clone(de) : e);
                        return r;
                    }
                    default: return v;
                }
            }

            string Str(string id, string path, string cur)
            {
                if (Chance(0.02)) return "not a ref";
                int cat = MakerEdits.CategoryOf(path, SexOf(id), CoordOf(id));
                if (cat >= 0) return Chance(0.85) ? FakeMakerData.Row(cat, g.Next(FakeMakerData.PoolSize)) : "illusion.hs2#" + g.Next(5);
                if (cur != null && JsonText.IsRef(cur)) return "illusion.hs2#" + g.Next(8);
                if (path.EndsWith(".image", StringComparison.Ordinal)) return Chance(0.2) ? null : "res/" + new string((char)('1' + g.Next(7)), 64) + ".png";
                if (path.EndsWith(".link", StringComparison.Ordinal)) return Chance(0.3) ? null : JsonText.DerivedId("fuzz/link/" + g.Next(3));
                if (path.EndsWith(".location", StringComparison.Ordinal)) return Chance(0.5) ? BodyBoneMod.LocationBodyTop : BodyBoneMod.LocationUnknown;
                if (path.EndsWith(".parent", StringComparison.Ordinal)) return Pick(FakeMakerData.Parents);
                return Chance(0.1) ? null : "f" + g.Next(100);
            }

            CommandResult WholeObject()
            {
                string id = Chara();
                var st = s.Model.Find(id).Appearance.Stored;
                switch (g.Next(6))
                {
                    case 0:
                    {
                        int a = g.Next(4), b = g.Next(4);
                        lastOp = "whole hair part " + a + " → " + b;
                        return s.Set(id, AppearancePaths.Chara("hair." + HairData.PartNames[b]), DocClone.Clone(st.Chara.Hair.Parts[a]));
                    }
                    case 1:
                    {
                        int a = g.Next(8), b = g.Next(8);
                        lastOp = "whole clothes part " + a + " → " + b;
                        return s.Set(id, AppearancePaths.Coord("clothes." + ClothesSet.Names[b]), DocClone.Clone(st.Coord.Clothes.Parts[a]));
                    }
                    case 2:
                    {
                        lastOp = "whole eye left ↔ right";
                        return s.Set(id, AppearancePaths.Chara(Chance(0.5) ? "face.eyes.left" : "face.eyes.right"), DocClone.Clone(Chance(0.5) ? st.Chara.Face.Eyes.Right : st.Chara.Face.Eyes.Left));
                    }
                    case 3:
                    {
                        if (st.Coord.Accessories.Count == 0) return null;
                        var e = DocClone.Clone(Pick(st.Coord.Accessories));
                        e.Parent = Pick(FakeMakerData.Parents);
                        if (Chance(0.1)) e.Id = JsonText.DerivedId("fuzz/other");
                        lastOp = "whole accessory " + e.Id;
                        return s.Set(id, AppearancePaths.Accessory(Chance(0.9) ? e.Id : Pick(st.Coord.Accessories).Id), e);
                    }
                    case 4:
                    {
                        var other = s.Model.Find(Pick(charas)).Appearance.Stored;
                        lastOp = "whole face from a character";
                        return s.Set(id, AppearancePaths.Chara("face"), DocClone.Clone(other.Chara.Face));
                    }
                    default:
                    {
                        var list = st.Coord.Accessories.Select(x => DocClone.Clone(x)).ToList();
                        if (list.Count > 1 && Chance(0.5)) list.Reverse();
                        else if (list.Count > 0) list.RemoveAt(g.Next(list.Count));
                        lastOp = "whole accessory list (" + list.Count + ")";
                        return s.Set(id, AppearancePaths.AccessoryList, list);
                    }
                }
            }

            static readonly string[] ItemPaths =
            {
                "appearance.chara.face.head", "appearance.chara.face.skin", "appearance.chara.hair.back.item", "appearance.chara.hair.front.item",
                "appearance.chara.hair.option.item", "appearance.coord.clothes.top.item", "appearance.coord.clothes.shoes.item",
                "appearance.coord.clothes.innerTop.item", "appearance.chara.face.eyes.left.iris.item", "appearance.chara.face.makeup.paint[1].layoutItem",
            };

            static readonly string[] HairColourPaths = { "baseColor", "topColor", "metallic", "smoothness", "accessoryColors[1]" };

            CommandResult PlannerEdit(Transaction tx)
            {
                string id = Chara();
                int n = 1 + g.Next(3);
                var sets = new List<(string, object)>();
                for (int i = 0; i < n; i++)
                {
                    switch (g.Next(6))
                    {
                        case 0:
                        {
                            string p = Pick(ItemPaths);
                            int cat = MakerEdits.CategoryOf(p, SexOf(id));
                            sets.Add((p, Chance(0.9) ? FakeMakerData.Row(cat, g.Next(FakeMakerData.PoolSize)) : "unlisted.mod#" + g.Next(3)));
                            break;
                        }
                        case 1:
                        {
                            var accs = CoordOf(id).Accessories;
                            if (accs.Count == 0) break;
                            var a = Pick(accs);
                            if (Chance(0.4)) sets.Add((AppearancePaths.Accessory(a.Id, "category"), Chance(0.05) ? 350 : g.Next(351, 364)));
                            else sets.Add((AppearancePaths.Accessory(a.Id, "item"), FakeMakerData.Row(a.Category, g.Next(FakeMakerData.PoolSize))));
                            break;
                        }
                        case 2:
                        {
                            string part = Pick(HairData.PartNames), m = Pick(HairColourPaths);
                            object v = m == "metallic" || m == "smoothness" ? (object)F() : new Color4(F(), F(), F(), F());
                            sets.Add((AppearancePaths.Chara("hair." + part + "." + m), v));
                            if (Chance(0.2)) sets.Add((AppearancePaths.Chara("hair.editor." + (Chance(0.5) ? "sameSetting" : "autoSetting")), Chance(0.5)));
                            break;
                        }
                        case 3:
                        {
                            string eye = Chance(0.5) ? "left" : "right";
                            sets.Add((AppearancePaths.Chara("face.eyes." + eye + (Chance(0.5) ? ".iris.color" : ".pupil.size")), Chance(0.5) ? (object)new Color4(F(), F(), F(), 1) : new Vec2(F(), F())));
                            if (Chance(0.2)) sets.Add((AppearancePaths.Chara("face.eyes.linked"), Chance(0.5)));
                            break;
                        }
                        default:
                        {
                            var (_, p, v) = RandomSet();
                            if (FieldPath.IsPrefixOf(AppearancePaths.Root, p)) sets.Add((p, v));
                            break;
                        }
                    }
                }
                lastOp = "planner " + id.Substring(0, 6) + ": " + string.Join(", ", sets.Select(x => x.Item1 + "=" + Text(x.Item2)));
                var rep = new MakerEditReport();
                var r = MakerEdits.Edit(tx, s.Model, s.Model.Find(id), sets, Opts(), rep);
                Consequences += rep.Consequences.Count;
                return r;
            }

            CommandResult Add(Transaction tx)
            {
                string id = Chara();
                int cat = Chance(0.05) ? 350 : g.Next(351, 364);
                string item = Chance(0.9) ? FakeMakerData.Row(cat, g.Next(FakeMakerData.PoolSize)) : "unlisted.mod#1";
                int count = CoordOf(id).Accessories.Count;
                int? at = null, slot = null;
                // 4.5: half of the adds name a build slot (free, occupied, ≥ 20, negative) or take the lowest free slot
                if (Chance(0.5)) at = Chance(0.5) ? (int?)null : g.Next(-1, count + 2);
                else
                {
                    slot = Chance(0.3) ? (int?)null : g.Next(-1, 30);
                    if (Chance(0.03)) at = 0;
                }
                string parent = Chance(0.3) ? (Chance(0.9) ? Pick(FakeMakerData.Parents) : "N_Bogus") : null;
                lastOp = "add accessory " + cat + " " + item + " at " + at + " slot " + slot + " parent " + parent;
                return MakerEdits.AddAccessory(tx, s.Model, s.Model.Find(id), cat, item, parent, slot, at, Opts(), new MakerEditReport());
            }

            CommandResult Remove(Transaction tx)
            {
                string id = Chara();
                var accs = CoordOf(id).Accessories;
                string acc = accs.Count > 0 && !Chance(0.05) ? Pick(accs).Id : JsonText.DerivedId("fuzz/none");
                lastOp = "remove accessory " + acc;
                return MakerEdits.RemoveAccessory(tx, s.Model, s.Model.Find(id), acc, null);
            }

            CommandResult Move(Transaction tx)
            {
                string id = Chara();
                var accs = CoordOf(id).Accessories;
                if (accs.Count == 0) { lastOp = "move (none)"; return null; }
                string acc = Pick(accs).Id;
                if (Chance(0.5))
                {
                    int slot = g.Next(-1, 30);
                    lastOp = "move accessory " + acc + " → slot " + slot;
                    return MakerEdits.MoveAccessoryToSlot(tx, s.Model, s.Model.Find(id), acc, slot, Opts(), new MakerEditReport());
                }
                int to = g.Next(-1, accs.Count + 1);
                lastOp = "move accessory " + acc + " → " + to;
                return MakerEdits.MoveAccessory(tx, s.Model, s.Model.Find(id), acc, to, null);
            }

            CommandResult Copy(Transaction tx)
            {
                string id = Chara();
                var accs = CoordOf(id).Accessories;
                if (accs.Count == 0) { lastOp = "copy (none)"; return null; }
                string from = Pick(accs).Id;
                // 4.5: «複製» (neither target), a build slot (free or occupied), an accessory id; rarely both
                int form = g.Next(10);
                string to = form >= 6 ? Pick(accs).Id : null;
                int? toSlot = form >= 3 && form < 6 || form == 9 && Chance(0.3) ? (int?)g.Next(-1, 30) : null;
                var parts = (AccessoryCopyParts)(1 + g.Next(7));
                if (Chance(0.05)) parts = AccessoryCopyParts.None;
                var mirror = (MirrorAxis)g.Next(3);
                lastOp = "copy accessory " + from + " → " + (to ?? "new") + " slot " + toSlot + " " + parts + " " + mirror;
                return MakerEdits.CopyAccessory(tx, s.Model, s.Model.Find(id), from, toSlot, to, parts, Chance(0.5), mirror, Opts(), new MakerEditReport());
            }

            CommandResult AccessoryEdit(Transaction tx)
            {
                string id = Chara();
                var accs = CoordOf(id).Accessories;
                if (accs.Count == 0) { lastOp = "accessory edit (none)"; return null; }
                var acc = Pick(accs);
                var node = s.Model.Find(id);
                switch (g.Next(4))
                {
                    case 0:
                        lastOp = "accessory defaults " + acc.Id;
                        return MakerEdits.ResetAccessoryColours(tx, s.Model, node, Chance(0.95) ? acc.Id : "nobody", Opts(), new MakerEditReport());
                    case 1:
                    {
                        int part = g.Next(-1, 5);
                        lastOp = "accessory hair colour " + acc.Id + " part " + part;
                        return MakerEdits.MatchAccessoryHairColour(tx, s.Model, node, acc.Id, part, Opts(), new MakerEditReport());
                    }
                    case 2:
                    {
                        string v = Chance(0.7) ? DocEnum.AttachPoints[g.Next(DocEnum.AttachPoints.Length)] : Chance(0.5) ? "" : "N_Bogus";
                        lastOp = "accessory parent " + acc.Id + " = " + v;
                        return MakerEdits.Edit(tx, s.Model, node, new List<(string, object)> { (AppearancePaths.Accessory(acc.Id, "parent"), v) }, Opts(), new MakerEditReport());
                    }
                    default:
                    {
                        var o = Opts();
                        string v = MakerEdits.AccessoryDefaultParent(o.Rows, acc.Category, acc.Item);
                        lastOp = "accessory default parent " + acc.Id + " = " + v;
                        return MakerEdits.Edit(tx, s.Model, node, new List<(string, object)> { (AppearancePaths.Accessory(acc.Id, "parent"), v) }, o, new MakerEditReport());
                    }
                }
            }

            CommandResult PresetPush(Transaction tx)
            {
                string id = Chara();
                var other = s.Model.Find(Pick(charas)).Appearance.Stored.Chara.Face;
                var face = DocClone.Clone(other);
                face.Head = Chance(0.8) ? FakeMakerData.Row(SexOf(id) == 0 ? 110 : 210, g.Next(FakeMakerData.PoolSize)) : "illusion.hs2#" + (80 + g.Next(3));
                if (Chance(0.3)) face.Eyelashes.Item = Chance(0.5) ? FakeMakerData.Row(315, g.Next(FakeMakerData.PoolSize)) : "unlisted.mod#" + g.Next(3);
                face.Shape[g.Next(59)] = F();
                lastOp = "face preset on " + id.Substring(0, 6) + " head " + face.Head;
                return MakerEdits.ApplyFacePreset(tx, s.Model, s.Model.Find(id), face, Opts(), new MakerEditReport());
            }

            CommandResult Transaction()
            {
                var tx = s.Commands.Begin("Edit face");
                int n = 2 + g.Next(5);
                var sb = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    int k = g.Next(10);
                    CommandResult r;
                    if (k < 6) { var (id, p, v) = RandomSet(); r = tx.Set(id, p, v); sb.Add("set " + p); }
                    else if (k < 8) { r = PlannerEdit(tx); sb.Add(lastOp); }
                    else if (k < 9) { r = Add(tx); sb.Add(lastOp); }
                    else { r = Remove(tx); sb.Add(lastOp); }
                }
                int end = g.Next(10);
                lastOp = "transaction [" + string.Join("; ", sb) + "] " + (end < 7 ? "commit" : end < 9 ? "cancel" : "dispose");
                if (end < 7) return tx.Commit();
                if (end < 9) { tx.Cancel(); return CommandResult.NoOp(null); }
                tx.Dispose();
                return CommandResult.NoOp(null);
            }

            CommandResult Invalid()
            {
                string id = Chara();
                switch (g.Next(6))
                {
                    case 0: lastOp = "invalid: sex"; return s.Set(id, "appearance.chara.profile.sex", 0);
                    case 1: lastOp = "invalid: index on accessories"; return s.Set(id, "appearance.coord.accessories[0].parent", "N_Head");
                    case 2: lastOp = "invalid: hs2"; return s.Set(id, "appearance.chara.profile.hs2.mind", 1);
                    case 3: lastOp = "invalid: whole document"; return s.Set(id, "appearance.coord", new CoordDocument());
                    case 4: lastOp = "invalid: rebuild path in the planner"; return InTx("Edit body", tx => MakerEdits.Edit(tx, s.Model, s.Model.Find(id), new[] { (AppearancePaths.Chara("body.uncensor"), (object)null) }, new MakerEditOptions(), null));
                    default: lastOp = "invalid: ext"; return s.Set(id, "appearance.chara.ext", null);
                }
            }
        }
    }
}
