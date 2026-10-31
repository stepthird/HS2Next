// The Unity layer's Maker services (package B's MakerServices, registered here):
//   Defaults  PartDefaults over the catalog (the prefab defaults of clothes, accessory and hair items)
//   Rows      the Maker lists (package L's MakerLists over the catalog, built on first use): listed rows, first rows, skins
//             of a head, an accessory row's Parent column, layout rows, colour presets (HSVToRGB of 336's Base / Sample
//             columns; 305's four colours, metallic and smoothness)
//   Presets   face presets of head rows (package L's FacePresetLoader)
//   Sessions  the edit sessions (CharaEditSessions)
//   MaleHeightPinned  CharaAssembler.MaleHeightPin075 for the maker.bodyHeight gate
//   Hair      the prefab facts of the realized hair parts for the maker.hair:* gates (package A's HairInfo.Instance over the
//             tracked runtimes; StudioOps.MakerGateHolds tracks its runtime)
//   Clothes   the prefab facts of the realized clothes kinds for the maker.clothes:* gates (package A's ClothesInfo.Instance
//             over the tracked runtimes, Phase 4.4; StudioOps.MakerGateHolds tracks its runtime)
//   Accessories  the prefab facts of the realized accessories by build slot for the maker.acc:* gates (package A's
//             AccessoryInfo.Instance over the tracked runtimes, Phase 4.5; StudioOps.MakerGateHolds tracks its runtime)
//   HeadNames the head names of a head row (package A's HeadNameFacts over the catalog: the head prefab's transforms, renderers
//             and materials plus p_cf_head_bone, cached per head ref) for the face / body split of a partial character load
//             (Phase 4.6)
//   MoreAccessoriesLoaded / AccessoryControlsOn  the runtime's extension switches (Enabled and MoreAccessories / AccessoryControls of
//             RuntimeServices.ExtOptions, read when an op runs): slots ≥ 20 loaded, AdditionalAccessoryControls' coordinate-load
//             features (Phase 4.5)
// Ensure runs at every flush point (SceneRuntime.Apply → CharaEditFlush.Run, i.e. before every Maker op, which applies the
// runtime first); it only (re)binds services that are absent or this layer's own ones bound to another catalog, so a test
// that installs its own implementation keeps it.
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public static class MakerSetup
    {
        static ContentCatalog boundTo;
        static RuntimeServices boundServices;
        static bool warmPending;

        /// <summary>The face-skin categories whose head index is warmed after a registration.</summary>
        public static readonly int[] WarmCategories = { 211, 111 };

        /// <summary>
        /// Registers the services for the catalog of <paramref name="services"/> (cheap when already bound). A new registration
        /// warms the face-skin head indices of both sexes at the next flush point (the next frame: MakerLists.Warm(211, 111),
        /// ≈ 5 ms once; F15), so the first skin list a page opens is cheap.
        /// </summary>
        public static void Ensure(RuntimeServices services)
        {
            var cat = services?.Catalog;
            if (cat == null) return;
            boundServices = services;
            if (MakerServices.MoreAccessoriesLoaded == null) MakerServices.MoreAccessoriesLoaded = MoreAccessoriesOn;
            if (MakerServices.AccessoryControlsOn == null) MakerServices.AccessoryControlsOn = AccessoryControlsOn;
            if (ReferenceEquals(cat, boundTo) && MakerServices.Defaults != null && MakerServices.Rows != null && MakerServices.Presets != null && MakerServices.Sessions != null
                && MakerServices.HeadNames != null)
            {
                if (warmPending) WarmNow(cat);
                return;
            }
            if (!ReferenceEquals(cat, boundTo)) warmPending = true;
            boundTo = cat;
            if (MakerServices.Defaults == null || (MakerServices.Defaults is PartDefaults d && !ReferenceEquals(d.Catalog, cat))) MakerServices.Defaults = new PartDefaults(cat);
            if (MakerServices.Rows == null || (MakerServices.Rows is CatalogAppearanceRows r && !ReferenceEquals(r.Catalog, cat))) MakerServices.Rows = new CatalogAppearanceRows(cat);
            if (MakerServices.Presets == null || (MakerServices.Presets is CatalogFacePresets p && !ReferenceEquals(p.Catalog, cat))) MakerServices.Presets = new CatalogFacePresets(cat);
            if (MakerServices.Sessions == null) MakerServices.Sessions = CharaEditSessions.Instance;
            if (MakerServices.HeadNames == null || (MakerServices.HeadNames is HeadNameFacts hn && !ReferenceEquals(hn.Catalog, cat))) MakerServices.HeadNames = new HeadNameFacts(cat);
            if (MakerServices.MaleHeightPinned == null) MakerServices.MaleHeightPinned = HeightPin;
            if (MakerServices.Hair == null) MakerServices.Hair = HairInfo.Instance;
            if (MakerServices.Clothes == null) MakerServices.Clothes = ClothesInfo.Instance;
            if (MakerServices.Accessories == null) MakerServices.Accessories = AccessoryInfo.Instance;
        }

        /// <summary>The app's male height pin for the `maker.bodyHeight` gate (Y8), read at each gate
        /// evaluation (the option can change while the app runs).</summary>
        static readonly System.Func<bool> HeightPin = () => HS2Next.Chara.CharaAssembler.MaleHeightPin075;

        /// <summary>The runtime loads MoreAccessories slots (≥ 20): its extension options' Enabled and MoreAccessories (on without
        /// options), read when an op runs (the bridge's app.setExtOptions can change them).</summary>
        static readonly System.Func<bool> MoreAccessoriesOn = () => { var o = boundServices?.ExtOptions; return o == null || o.Enabled && o.MoreAccessories; };

        /// <summary>The runtime applies AdditionalAccessoryControls: its extension options' Enabled and AccessoryControls.</summary>
        static readonly System.Func<bool> AccessoryControlsOn = () => { var o = boundServices?.ExtOptions; return o == null || o.Enabled && o.AccessoryControls; };

        /// <summary>Builds the face-skin head indices of the catalog's Maker lists now (the deferred warm of <see cref="Ensure"/>).</summary>
        public static void WarmNow(ContentCatalog catalog)
        {
            warmPending = false;
            if (catalog == null) return;
            try { MakerCatalog.For(catalog)?.Warm(WarmCategories); }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("[maker] list warm failed: " + e.Message); }
        }

        /// <summary>Forgets this layer's services (tests that need the plain-Mono behaviour).</summary>
        public static void Clear()
        {
            if (MakerServices.Defaults is PartDefaults) MakerServices.Defaults = null;
            if (MakerServices.Rows is CatalogAppearanceRows) MakerServices.Rows = null;
            if (MakerServices.Presets is CatalogFacePresets) MakerServices.Presets = null;
            if (MakerServices.Sessions is CharaEditSessions) MakerServices.Sessions = null;
            if (MakerServices.HeadNames is HeadNameFacts) MakerServices.HeadNames = null;
            if (ReferenceEquals(MakerServices.MaleHeightPinned, HeightPin)) MakerServices.MaleHeightPinned = null;
            if (ReferenceEquals(MakerServices.Hair, HairInfo.Instance)) MakerServices.Hair = null;
            if (ReferenceEquals(MakerServices.Clothes, ClothesInfo.Instance)) MakerServices.Clothes = null;
            if (ReferenceEquals(MakerServices.Accessories, AccessoryInfo.Instance)) MakerServices.Accessories = null;
            if (ReferenceEquals(MakerServices.MoreAccessoriesLoaded, MoreAccessoriesOn)) MakerServices.MoreAccessoriesLoaded = null;
            if (ReferenceEquals(MakerServices.AccessoryControlsOn, AccessoryControlsOn)) MakerServices.AccessoryControlsOn = null;
            boundTo = null;
            boundServices = null;
            warmPending = false;
        }
    }

    /// <summary>IAppearanceRows over the Maker lists of a catalog.</summary>
    public sealed class CatalogAppearanceRows : IAppearanceRows
    {
        public ContentCatalog Catalog { get; }
        public CatalogAppearanceRows(ContentCatalog catalog) { Catalog = catalog; }
        MakerLists Lists => MakerCatalog.For(Catalog);

        public bool HasRow(int category, int sex, string reference) =>
            ContentId.TryParse(reference, out var key) && Lists != null && Lists.IsListed(category, sex, key);

        public string FirstRow(int category, int sex)
        {
            var rows = Lists?.Rows(category, sex);
            return rows != null && rows.Count > 0 ? rows[0].Key.ToString() : null;
        }

        public IReadOnlyList<string> SkinsForHead(int sex, string headRef)
        {
            if (!ContentId.TryParse(headRef, out var head) || Lists == null) return null;
            var rows = Lists.Rows(sex == 0 ? 111 : 211, sex, ListContext.ForHead(head));
            var l = new List<string>(rows.Count);
            foreach (var r in rows) l.Add(r.Key.ToString());
            return l;
        }

        public string AccessoryParent(int category, string reference)
        {
            if (!ContentId.TryParse(reference, out var key)) return null;
            var e = Catalog.GetEntry(category, key);
            var v = e?["Parent"];
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public bool LayoutValues(int category, string reference, out float scale, out float posX, out float posY)
        {
            scale = posX = posY = 0f;
            return ContentId.TryParse(reference, out var key) && Lists != null && Lists.LayoutValues(category, key, out scale, out posX, out posY);
        }

        public bool ListHasMoreThanOne(int category, int sex) => Lists != null && Lists.HasMoreThanOne(category, sex);

        public bool ColourPreset(int category, string reference, out Color4 value, out Color4 swatch)
        {
            value = swatch = default;
            if (!ContentId.TryParse(reference, out var key) || Lists == null || !Lists.ColourPreset(category, key, out var b, out var s)) return false;
            value = MakerEdits.HsvToRgb(b.H, b.S, b.V);
            swatch = MakerEdits.HsvToRgb(s.H, s.S, s.V);
            return true;
        }

        public bool HairColourPreset(string reference, out HairColourPresetValues values)
        {
            values = default;
            if (!ContentId.TryParse(reference, out var key) || Lists == null || !Lists.HairColourPreset(key, out var c)) return false;
            values = new HairColourPresetValues
            {
                Base = MakerEdits.HsvToRgb(c.Base.H, c.Base.S, c.Base.V), Top = MakerEdits.HsvToRgb(c.Top.H, c.Top.S, c.Top.V),
                Under = MakerEdits.HsvToRgb(c.Under.H, c.Under.S, c.Under.V), Specular = MakerEdits.HsvToRgb(c.Specular.H, c.Specular.S, c.Specular.V),
                Metallic = c.Metallic, Smoothness = c.Smoothness,
            };
            return true;
        }
    }

    /// <summary>IFacePresets over package L's loader.</summary>
    public sealed class CatalogFacePresets : IFacePresets
    {
        public ContentCatalog Catalog { get; }
        public CatalogFacePresets(ContentCatalog catalog) { Catalog = catalog; }

        public bool TryLoad(int sex, string headRef, out FaceData face, IList<string> warnings)
        {
            face = null;
            if (!ContentId.TryParse(headRef, out var head)) return false;
            var lists = MakerCatalog.For(Catalog);
            if (lists == null) return false;
            var r = FacePresetLoader.Load(Catalog, lists, head, sex);
            if (warnings != null) foreach (var f in r.Findings) warnings.Add(f);
            face = r.Face;
            return !r.Missing && face != null;
        }
    }
}
