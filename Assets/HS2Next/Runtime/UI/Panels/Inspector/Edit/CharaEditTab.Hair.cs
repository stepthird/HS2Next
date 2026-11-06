// The Hair page of the Edit tab: one page — the options section (the three switches as MakerToggleRows, the draw type as a
// MakerChoiceRow), the part selector «後ろ髪 · 前髪 · 横髪 · エクステ» (section `hair.hair.part`, PartSelector on
// PartSelectorState.Hair) and the selected part's group `hair-part-group-<part>` (Style · Colour · Decoration · Bundles ·
// Mesh), built on first selection and kept; only the selected group is attached (element names stay unique). Sections that
// name watched paths are live (LiveSections: re-evaluated after the realizing flush, refilled when their gate results or the
// prefab's bundle structure change); the part buttons dim for parts without a hair prefab (a signal on the four item paths).
// Rows drawn here: HairBundles → HairBundleList (the «全て初期化» button row is its footer), the skin-match button
// `hair-skin-<part>`. edit.show takes `part` (name or 0..3). 6.4: a part group ends with «Edit materials» (`hair-materials-<part>`), and the
// section of the hair bone adjustments (HairBoneSection) stands after the groups.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class CharaEditTab
    {
        public const string HairNode = "hair";
        const string PartsKey = "<parts>";

        PartSelector hairParts;
        VisualElement hairHost;
        readonly VisualElement[] hairGroups = new VisualElement[4];
        LiveSections hairLive;

        /// <summary>The Hair page's part selector (null until the Hair node is built).</summary>
        public PartSelector HairParts => hairParts;
        /// <summary>The built part groups of the Hair page (null entries: not built yet).</summary>
        public IReadOnlyList<VisualElement> HairGroups => hairGroups;
        /// <summary>Time of the last part switch of the Hair page (ms; a first selection includes the group build; UH5).</summary>
        public double HairPartSwitchMs { get; private set; }
        /// <summary>The Hair page's live sections (null until the Hair node is built).</summary>
        public LiveSections HairLive => hairLive;

        /// <summary>The options and part sections, then the host of the selected part's group (built on first selection).</summary>
        VisualElement BuildHairPage(VisualElement c, List<SchemaSection> sections)
        {
            var sw = Stopwatch.StartNew();
            hairLive = NewLiveSections();
            hairLive.Extra = (e, f) => f.Kind == FieldKind.HairBundles ? HairBundleList.StructureOf(ctx, primary, e.Part) : null;
            var itemPaths = new List<string>();
            for (int k = 0; k < InspectorSchemas.HairParts.Count; k++) itemPaths.Add(InspectorSchemas.CharaEditPath("hair." + InspectorSchemas.HairParts[k] + ".item"));
            hairLive.AddSignal(PartsKey, itemPaths, RefreshPartMarks);
            WithGateMemo(() =>
            {
                foreach (var s in sections)
                {
                    if (s.Id == InspectorSchemas.HairPartSection)
                    {
                        var fo = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
                        sectionFoldouts[s.Id] = fo;
                        var keys = new List<string>();
                        var names = new List<string>();
                        for (int k = 0; k < InspectorSchemas.HairParts.Count; k++)
                        {
                            keys.Add(InspectorSchemas.HairPartLabelKey(k));
                            names.Add("hair-part-" + InspectorSchemas.HairParts[k]);
                        }
                        hairParts = new PartSelector(PartSelectorState.Hair, keys, names, "maker.hair.partNone", "hair-parts", false);
                        hairParts.PartChanged += ShowHairPart;
                        fo.Add(hairParts);
                        c.Add(fo);
                    }
                    else if (s.Id != InspectorSchemas.HairBoneModsSection && InspectorSchemas.HairPartOf(s.Id) < 0) AddLiveSection(hairLive, c, s, -1);
                }
                hairHost = new VisualElement { name = "hair-part-host" };
                c.Add(hairHost);
                var bones = HairBoneSection.Section(ctx, primary);
                sectionFoldouts[InspectorSchemas.HairBoneModsSection] = bones;
                c.Add(bones);
                RefreshPartMarks();
            });
            ShowHairPart(PartSelectorState.Hair.Part);
            // live gates: changes of watched paths on the primary mark sections; the tick re-evaluates them
            hairLive.Attach(c);
            PageBuildMs[HairNode + ".hair"] = sw.Elapsed.TotalMilliseconds;
            return c;
        }

        /// <summary>Shows part <paramref name="k"/>'s group (built on first use; the other groups are detached, not destroyed).</summary>
        void ShowHairPart(int k)
        {
            if (hairHost == null || k < 0 || k >= hairGroups.Length) return;
            var sw = Stopwatch.StartNew();
            if (hairGroups[k] == null)
            {
                var g = new VisualElement { name = "hair-part-group-" + InspectorSchemas.HairParts[k] };
                WithGateMemo(() => { foreach (var s in SectionsOf(HairNode)) if (InspectorSchemas.HairPartOf(s.Id) == k) AddLiveSection(hairLive, g, s, k); });
                g.Add(MaterialsButton("hair:" + k, "hair-materials-" + InspectorSchemas.HairParts[k]));
                hairGroups[k] = g;
                PageBuildMs[HairNode + ".group." + InspectorSchemas.HairParts[k]] = sw.Elapsed.TotalMilliseconds;
            }
            hairHost.Clear();
            hairHost.Add(hairGroups[k]);
            HairPartSwitchMs = sw.Elapsed.TotalMilliseconds;
            // the handles follow the part: a bundle of another part of this character is no longer the sub-selection
            if (StudioOps.TryParseHairSub(ctx.SubSelection, out var cid, out int sp, out _) && cid == primary && sp != k) ctx.SubSelection = null;
        }

        /// <summary>«Edit materials»: the Materials tab on a target's array.</summary>
        Button MaterialsButton(string target, string id) =>
            InspectorUi.Button("inspector.materials.open", () => ctx.ShowResult(HS2Next.UI.Binding.ButtonActions.Run(ctx, "ui.materials.open:" + target, new[] { primary })), id);

        /// <summary>Re-evaluates the Hair page's marked live sections now (tests; the tick does the same at the next frame).</summary>
        public void EvaluateHairNow() => hairLive?.EvaluateNow();

        /// <summary>Hair sections still waiting for the flush that realizes a change (tests).</summary>
        public int HairPending => hairLive?.PendingCount ?? 0;

        /// <summary>Dims the part buttons of parts whose realization has no hair prefab.</summary>
        void RefreshPartMarks()
        {
            if (hairParts == null) return;
            for (int k = 0; k < InspectorSchemas.HairParts.Count; k++)
                hairParts.SetNone(k, HairInfo.TryPart(ctx.Runtime, primary, k, out var f) && (f == null || !f.HasPrefab));
        }

        /// <summary>The hair part (0..3) of a path "appearance.chara.hair.&lt;part&gt;.…", or 0.</summary>
        static int HairPartOfPath(string path)
        {
            const string head = "appearance.chara.hair.";
            if (path == null || !path.StartsWith(head, StringComparison.Ordinal)) return 0;
            string rest = path.Substring(head.Length);
            int dot = rest.IndexOf('.');
            string p = dot < 0 ? rest : rest.Substring(0, dot);
            for (int k = 0; k < InspectorSchemas.HairParts.Count; k++) if (InspectorSchemas.HairParts[k] == p) return k;
            return 0;
        }

        /// <summary>The hair rows this page draws itself (null = the generic widget, NoRow = nothing).</summary>
        VisualElement BuildHairRow(FieldSpec f, string path)
        {
            if (f.Kind == FieldKind.HairBundles) return new HairBundleList(ctx, f, sel, primary, HairPartOfPath(path), path);
            if (f.Kind != FieldKind.Button) return null;
            if (IsActionOf(f, InspectorSchemas.ActionResetHairBundlesPrefix)) return NoRow;   // «全て初期化» is the bundle list's footer
            if (IsActionOf(f, InspectorSchemas.ActionMatchHairToSkinPrefix)) return HairSkinButton(f) ?? NoRow;
            return null;
        }

        /// <summary>«肌の色に合わせる» (`hair-skin-&lt;part&gt;`): StudioOps.MatchHairToSkin on the selected characters that pass the gate.</summary>
        VisualElement HairSkinButton(FieldSpec f)
        {
            int k = InspectorSchemas.HairPartOfAction(f.Action, InspectorSchemas.ActionMatchHairToSkinPrefix);
            if (k < 0) return null;
            var b = InspectorUi.Button(f.LabelKey, () =>
            {
                var targets = MakerRowKit.Writable(ctx, f, sel);
                if (targets.Count > 0) ctx.ShowResult(ctx.Ops.MatchHairToSkin(targets, k, UiContext.User(OpLabels.EditHair)));
            }, "hair-skin-" + InspectorSchemas.HairParts[k]);
            if (f.TooltipKey != null) Loc.Tooltip(b, f.TooltipKey);
            return b;
        }

        /// <summary>edit.show on the Hair node: the part to show (the argument, else the part a section id names; -1 = none). Before
        /// the page is built the view state is set so that only that part's group is built.</summary>
        int PrepareHairReveal(string node, string section, int part)
        {
            int hp = node == HairNode ? (part >= 0 && part < 4 ? part : InspectorSchemas.HairPartOf(section)) : -1;
            if (hp >= 0 && hairParts == null) PartSelectorState.Hair.Part = hp;
            return hp;
        }
    }
}
