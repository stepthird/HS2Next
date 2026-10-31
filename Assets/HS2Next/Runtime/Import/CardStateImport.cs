// The import option "use card state" (default off): HS2 Studio ignores a card's Status when
// a character is added from a card, so the default import writes nothing from it. With the option the scene layer calls
// this after placing the imported character in a scene node: the Status block fills the node's scene state.
// Phase 4.5: the accessory hides of a Studio load are folded once into
// state.accessories (FoldAccessories) — the Status hides, MoreAccessories' show="false" and Additional Accessory Controls'
// Studio startup pass —, since the build no longer hides slot objects for them.
using System;
using System.Collections.Generic;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Save;
using HS2Next.Scenes;

namespace HS2Next.Import
{
    public static class CardStateImport
    {
        static readonly HashSet<string> StatusKeys = RawReader.Keys("version", "clothesState", "showAccessory", "eyebrowPtn", "eyebrowOpenMax", "eyesPtn",
            "eyesOpenMax", "eyesBlink", "eyesYure", "mouthPtn", "mouthOpenMin", "mouthOpenMax", "mouthFixed", "mouthAdjustWidth", "tongueState",
            "disableMouthShapeMask", "hohoAkaRate", "tearsRate", "hideEyesHighlight", "eyesLookPtn", "eyesTargetType", "eyesTargetAngle",
            "eyesTargetRange", "eyesTargetRate", "neckLookPtn", "neckTargetType", "neckTargetAngle", "neckTargetRange", "neckTargetRate",
            "disableBustShapeMask", "nipStandRate", "skinTuyaRate", "siruLv", "wetRate", "siriAkaRate", "visibleSon", "visibleSonAlways",
            "visibleHeadAlways", "visibleBodyAlways", "visibleSimple", "visibleGomu", "simpleColor", "enableShapeHand", "shapeHandPtn",
            "shapeHandBlendValue", "ExtendedSaveData");

        /// <summary>
        /// Fills <paramref name="state"/> from the card's `Status` block. <paramref name="worn"/> is the imported
        /// coordinate of the same card: `showAccessory[s]` goes to the entry with `importSlot` s. Missing keys take the Status
        /// MemberInit values. Then the accessory fold (<see cref="FoldAccessories"/>) with the Status clothes state, the card's
        /// MoreAccessories show flags and, when <paramref name="accessoryControls"/>, Additional Accessory Controls' startup
        /// pass. Returns false (state unchanged) when the card has no readable Status block.
        /// </summary>
        public static bool Apply(Card card, CoordDocument worn, CharaNodeState state, ImportReport report = null, bool accessoryControls = true)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (state == null) throw new ArgumentNullException(nameof(state));
            report ??= new ImportReport { Kind = "chara" };
            if (!(card.Status is Dictionary<object, object> st)) { report.Warning("state.noStatus", "Status", "the card has no readable Status block; scene defaults kept"); return false; }
            var r = new RawReader(report);
            r.CheckKeys(st, StatusKeys, "Status");

            var clothes = Bytes(st, "clothesState", 8);
            for (int k = 0; k < 8 && k < state.State.Clothes.Length; k++) state.State.Clothes[k] = clothes[k];
            if (st.TryGetValue("showAccessory", out var sa) && sa is List<object> show && worn != null)
                foreach (var e in worn.Accessories)
                {
                    int s = e.ImportSlot ?? -1;
                    if (s < 0 || s >= 20 || e.Id == null) continue;
                    state.State.Accessories[e.Id] = s < show.Count && show[s] is bool b ? b : true;
                }
            if (worn != null) FoldAccessories(worn, clothes, ExtraShowOf(card), state, accessoryControls);

            var f = state.Face;
            f.Eyebrow = r.I(st, "eyebrowPtn", 0, "state.face.eyebrow");
            f.Eyes = r.I(st, "eyesPtn", 0, "state.face.eyes");
            f.EyesOpen = r.F(st, "eyesOpenMax", 1f, "state.face.eyesOpen");
            f.Blink = r.B(st, "eyesBlink", true, "state.face.blink");
            f.Mouth = r.I(st, "mouthPtn", 0, "state.face.mouth");
            f.Tears = r.F(st, "tearsRate", 0f, "state.face.tears");
            f.Cheek = r.F(st, "hohoAkaRate", 0f, "state.face.cheek");
            f.EyebrowOpenMax = r.F(st, "eyebrowOpenMax", 1f, "state.face.eyebrowOpenMax");
            f.MouthOpenMin = r.F(st, "mouthOpenMin", 0f, "state.face.mouthOpenMin");
            f.MouthOpenMax = r.F(st, "mouthOpenMax", 1f, "state.face.mouthOpenMax");
            f.MouthFixed = r.B(st, "mouthFixed", false, "state.face.mouthFixed");
            f.MouthAdjustWidth = r.B(st, "mouthAdjustWidth", true, "state.face.mouthAdjustWidth");
            f.HideHighlight = r.B(st, "hideEyesHighlight", false, "state.face.hideHighlight");

            state.Look.Eyes = r.I(st, "eyesLookPtn", 0, "state.look.eyes");
            state.Look.Neck = r.I(st, "neckLookPtn", 0, "state.look.neck");

            state.State.Nipple = r.F(st, "nipStandRate", 0f, "state.state.nipple");
            var siru = Bytes(st, "siruLv", 5);
            for (int i = 0; i < 5 && i < state.State.Liquid.Length; i++) state.State.Liquid[i] = siru[i];
            state.State.Wet = r.F(st, "wetRate", 0f, "state.state.wet");
            state.State.Gloss = r.F(st, "skinTuyaRate", 0f, "state.state.gloss");
            state.State.Son.Visible = r.B(st, "visibleSonAlways", true, "state.state.son.visible");
            state.State.Silhouette.Enabled = r.B(st, "visibleSimple", false, "state.state.silhouette.enabled");
            state.State.Silhouette.Color = r.Col(st, "simpleColor", new Color4(0.188f, 0.286f, 0.8f, 0.5f), "state.state.silhouette.color");
            return true;
        }

        /// <summary>
        /// Folds the accessory hides of a Studio load into <paramref name="state"/>'s state.accessories, once: an accessory of
        /// <paramref name="worn"/> becomes false when (b) it sits in a MoreAccessories slot (≥ 20, its importSlot) whose element
        /// stores show="false" (<paramref name="extraShow"/>, by slot; cards never store it, scenes may), or (c) Additional
        /// Accessory Controls' Studio startup pass hides it (with <paramref name="accessoryControls"/>: the STUDIO_LOAD slots'
        /// clothes rules against <paramref name="clothesState"/> and STARTUP, then the links, starting from the state after (b)).
        /// Only false is written (a hide of another step stays); a later clothes-state change does not re-run it. Returns the
        /// ids it hid that were shown before. The Status step (a) is <see cref="Apply"/>'s.
        /// </summary>
        public static List<string> FoldAccessories(CoordDocument worn, IReadOnlyList<int> clothesState, IReadOnlyDictionary<int, bool> extraShow, CharaNodeState state, bool accessoryControls = true)
        {
            var hid = new List<string>();
            if (worn?.Accessories == null || state?.State?.Accessories == null) return hid;
            var map = state.State.Accessories;
            void Hide(string id)
            {
                if (id == null) return;
                if (!map.TryGetValue(id, out bool was) || was) hid.Add(id);
                map[id] = false;
            }
            if (extraShow != null && extraShow.Count > 0)
                foreach (var e in worn.Accessories)
                {
                    int s = e?.ImportSlot ?? -1;
                    if (s >= MoreAccessoriesData.FirstSlot && extraShow.TryGetValue(s, out bool shown) && !shown) Hide(e.Id);
                }
            if (accessoryControls)
            {
                var hidden = AccessoryStartupFold.Hidden(worn, clothesState ?? new int[8], map, out _);
                foreach (var e in worn.Accessories) if (e?.Id != null && hidden.Contains(e.Id)) Hide(e.Id);
            }
            return hid;
        }

        /// <summary>
        /// The accessory ids a Studio load of <paramref name="source"/>'s card hides (a character added from a card, the default
        /// import without the card state): the fold of <see cref="FoldAccessories"/> with the clothes state all 0 (dressed) on the
        /// card imported to documents (the importer's ids, CardAppearanceSource.AccessoryId). The import runs only for a card
        /// whose plugin data can hide something (an Additional Accessory Controls slot with STUDIO_LOAD, or a MoreAccessories
        /// show="false"); null for every other card, without an importer, or when the import fails. <paramref name="importer"/>
        /// gives the importer when one is needed (default: CharaImport.Importer).
        /// </summary>
        public static IReadOnlyCollection<string> HiddenAtCardLoad(Card card, CardAppearanceSource source, Func<ICharaImporter> importer = null)
        {
            if (card == null || source == null) return null;
            try
            {
                var ext = CardExtensions.Decode(card);
                bool aac = false;
                if (ext?.AdditionalAccessoryControls != null)
                    foreach (var sd in ext.AdditionalAccessoryControls.Slots)
                        if (sd != null && !sd.IsEmpty && sd.VisibilityRules.Exists(r => r.Rule == AdditionalAccessoryControlsData.Rule.STUDIO_LOAD)) { aac = true; break; }
                Dictionary<int, bool> show = null;
                if (ext?.MoreAccessories != null)
                    foreach (var p in ext.MoreAccessories.Parts)
                        if (p != null && p.Show == false) (show ??= new Dictionary<int, bool>())[p.Slot] = false;
                if (!aac && show == null) return null;
                var imp = importer != null ? importer() : CharaImport.Importer;
                var c = imp?.Import(source, new FindingList());
                if (c?.Coord == null) return null;
                var state = new CharaNodeState();
                foreach (var e in c.Coord.Accessories) if (e?.Id != null) state.State.Accessories[e.Id] = true;
                var hid = FoldAccessories(c.Coord, new int[8], show, state, aac);
                return hid.Count > 0 ? hid : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The MoreAccessories show flags a card stores, by slot (null when none: cards normally store none).</summary>
        static Dictionary<int, bool> ExtraShowOf(Card card)
        {
            try
            {
                var ma = CardExtensions.Decode(card)?.MoreAccessories;
                if (ma == null) return null;
                Dictionary<int, bool> d = null;
                foreach (var p in ma.Parts)
                    if (p != null && p.Show.HasValue) (d ??= new Dictionary<int, bool>())[p.Slot] = p.Show.Value;
                return d;
            }
            catch (Exception) { return null; }
        }

        // byte[n] fields are MessagePack bin; a missing or short value gives zeros.
        static int[] Bytes(Dictionary<object, object> st, string key, int n)
        {
            var r = new int[n];
            if (st.TryGetValue(key, out var v))
            {
                if (v is byte[] b) for (int i = 0; i < n && i < b.Length; i++) r[i] = b[i];
                else if (v is List<object> l) for (int i = 0; i < n && i < l.Count; i++) r[i] = l[i] is long x ? (int)x : 0;
            }
            return r;
        }
    }
}
