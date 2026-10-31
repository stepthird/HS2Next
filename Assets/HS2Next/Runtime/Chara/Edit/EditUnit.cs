// Edit units of a built character: one unit per group of document fields. A unit
// reads the character documents (CharaDocument / CoordDocument) through the edit context and writes the part of the
// built character that depends on them; its output is a function of the documents and the prefab data only, so applying
// it twice equals applying it once. CharaEditor runs the dirty units of a character once per frame in the fixed
// step order; CharaAssembler.Build marks every unit and runs the same flush.
using System.Collections.Generic;
using HS2Next.Chara.Documents;
using HS2Next.Data.Ext;
using HS2Next.Save;

namespace HS2Next.Chara.Edit
{
    /// <summary>The flush steps (a unit runs in exactly one step; the order of the enum is the flush order).</summary>
    public enum EditStep
    {
        /// <summary>Part prefabs: head, hair parts, clothes kinds, the accessory list, accessory items (slot order).</summary>
        Parts = 1,
        /// <summary>The clothes masks (derived from the items of kinds 0, 1, 2, 3, 5 and the break rates of 0, 1).</summary>
        Masks = 2,
        /// <summary>Compositing passes: skin body, skin face, clothes colour passes (each followed by its KCOX re-application).</summary>
        Composites = 3,
        /// <summary>Material values: draw materials, eyes, hair shader and colours, clothes break / options, accessory colours and moves.</summary>
        Materials = 4,
        /// <summary>Plugin texture stage: MaterialEditor items of the objects the steps before touched.</summary>
        PluginTextures = 5,
        /// <summary>Shape sliders and their body consequences.</summary>
        Shapes = 6,
        /// <summary>Plugin final stage: AAC, ABMX, DBE, InvisibleBody.</summary>
        PluginFinal = 7,
        /// <summary>Ownership and URP mapping of new subtrees.</summary>
        Ownership = 8,
        /// <summary>Motion stages: bust, dynamic bones (face / look re-attach and FK rediscovery are scene work, step 10).</summary>
        Motion = 9,
        /// <summary>Scene state (CharaStateRealizer, accessory visibility, layers, FaceMaterials.Refresh): the scene's
        /// <see cref="ICharaEditSceneHooks"/>, called with the flush report.</summary>
        SceneState = 10,
    }

    /// <summary>What the units read: the node's documents (live objects), the resources of plugin images, the part
    /// resolver, the accessory slot table and the decoded plugin data.</summary>
    public sealed class CharaEditInput
    {
        public CharaDocument Chara;
        public CoordDocument Coord;
        /// <summary>Images of skin / clothes overlays and MaterialEditor textures (null for card builds).</summary>
        public ResStore Res;
        /// <summary>The part resolver (DocumentPartResolver for documents, CardPartResolver for card builds).</summary>
        public ICharaPartResolver Parts;
        /// <summary>Accessory id ↔ build slot (MoreAccessories numbering).</summary>
        public AccessorySlotTable Slots;
        /// <summary>Decoded plugin data (ExtSections for documents, CardExtensions.Decode for cards); null when off.</summary>
        public CardExtData Ext;
    }

    public abstract class EditUnit
    {
        /// <summary>"head", "skin.face", "shape.body", "hair.front", "clothes.top", "acc:&lt;id&gt;", ....</summary>
        public abstract string Id { get; }
        /// <summary>The flush step this unit runs in.</summary>
        public abstract EditStep Step { get; }
        /// <summary>Order inside the step (lower first; ties keep registration order).</summary>
        public virtual int Order => 0;
        /// <summary>True when every run loads everything the unit uses (its earlier bad-mip incidents are replaced by the
        /// run's; part units keep theirs until the part is released).</summary>
        public virtual bool LoadsAllEachRun => false;
        /// <summary>True for a unit that must see everything before it: CharaEditor marks it (as a whole) whenever a unit of a
        /// lower rank (step, order) ran in the same flush (the mosaic stage).</summary>
        public virtual bool FollowsEarlierSteps => false;
        /// <summary>Document path prefixes the unit reads ("face.shape", "clothes.top", ...; relative to its document).</summary>
        public abstract IReadOnlyList<string> Paths { get; }
        /// <summary>Idempotent: output = f(documents, prefab data).</summary>
        public abstract void Apply(EditContext x);

        /// <summary>During <see cref="Apply"/>: the member paths (inside their document, "face.makeup.lip.gloss") that marked
        /// the unit since its last run, filled by CharaEditor.MarkDirty and cleared after the run (
        /// 3). Units may use them to skip work whose inputs did not change (the skin units' per-creator rebuilds); the
        /// output must stay a function of the documents.</summary>
        public readonly HashSet<string> DirtyPaths = new HashSet<string>(System.StringComparer.Ordinal);
        /// <summary>During <see cref="Apply"/>: true when the unit was marked as a whole (MarkUnit, MarkAll / the build, work
        /// deferred by a preview flush): everything is redone.</summary>
        public bool DirtyAll;
    }
}
