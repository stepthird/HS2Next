// StudioOps, material edits: changes of a materialOverrides array and of the character's hair bone modifiers, shared by the commands
// and the Materials view. A target names one array: the character's, a hair part's, a clothes part's, an accessory's, an item's or the
// map's. Each edit is checked against the built target first, then the array is changed through MaterialEdits and set as a whole on
// its field path, one history entry for all the edits of a call. So an edit made of several members, or a batch of edits, stores all
// of it or nothing, and a refusal leaves no entry and no change. A result that is the same list writes nothing and adds no entry.
// A refusal answers the command's code:
//   bad_args     a value's type or range, a bad target, a file that is refused, a limit, a shader that cannot be picked, a keyword on a
//                shader outside HS2Next/
//   not_found    a material, property, renderer, keyword or image the target lacks: an edit never creates an item that cannot act; on a
//                character also a new item on a property only the URP mapping's replacement declares: a build writes items before the
//                mapping, so the next rebuild would lose it. A shader item naming that replacement puts it within reach. A stored
//                item is judged by the pass, and items and the map are written after their mapping, on what their shader declares
//   unsupported  a read-only type, a value the target keeps fixed; mosaic parts, with the warning me.mosaicProtected
//   unavailable  a target that is not built, or no map loaded: the baseline and the mosaic rule need its renderers
//   wrong_kind   a node of another kind
// An edit that removes an item (a clear, an edit that leaves it empty, a value equal to what the target holds without it) or only clears
// members writes nothing onto a material, so it needs no material: a stored leftover can always go. Read-only types stay.
// Options: a MergeKey chains the edits of one item into one history entry (a removal stands alone); an open transaction (a UI drag, a
// bridge batch) takes the edit as a step. A drag passes the array at the press as `from`, so each step starts there and only the last
// value counts; inside a UI edit the op leaves the flush to the frame. A card-backed character is imported first, in the same entry.
// Result: Changed = the keys set, Value = the keys removed (List<string>).
using System;
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The objects that hold a materialOverrides array.</summary>
    public enum MaterialTargetKind { Character, Hair, Clothes, Accessory, Item, Map }

    /// <summary>One materialOverrides array of the scene.</summary>
    public readonly struct MaterialEditTarget
    {
        public readonly MaterialTargetKind Kind;
        /// <summary>The character or item node; null for the map.</summary>
        public readonly string NodeId;
        /// <summary>The hair part (0..3) or the clothes kind (0..7).</summary>
        public readonly int Slot;
        /// <summary>The accessory's id.</summary>
        public readonly string AccessoryId;

        MaterialEditTarget(MaterialTargetKind kind, string nodeId, int slot = 0, string accessoryId = null)
        {
            Kind = kind;
            NodeId = nodeId;
            Slot = slot;
            AccessoryId = accessoryId;
        }

        public static MaterialEditTarget Character(string node) => new MaterialEditTarget(MaterialTargetKind.Character, node);
        public static MaterialEditTarget Hair(string node, int part) => new MaterialEditTarget(MaterialTargetKind.Hair, node, part);
        public static MaterialEditTarget Clothes(string node, int kind) => new MaterialEditTarget(MaterialTargetKind.Clothes, node, kind);
        public static MaterialEditTarget Accessory(string node, string id) => new MaterialEditTarget(MaterialTargetKind.Accessory, node, 0, id);
        public static MaterialEditTarget Item(string node) => new MaterialEditTarget(MaterialTargetKind.Item, node);
        public static MaterialEditTarget Map() => new MaterialEditTarget(MaterialTargetKind.Map, null);

        /// <summary>Reads a character's target: <c>character</c>, <c>hair:&lt;part or 0..3&gt;</c>, <c>clothes:&lt;kind or 0..7&gt;</c>, <c>acc:&lt;id&gt;</c>;
        /// false with the reason.</summary>
        public static bool TryParse(string node, string text, out MaterialEditTarget target, out string why)
        {
            target = default;
            why = null;
            if (text == "character") { target = Character(node); return true; }
            if (text != null && text.StartsWith("hair:", StringComparison.Ordinal))
            {
                int k = Index(text.Substring(5), HairData.PartNames);
                if (k >= 0) { target = Hair(node, k); return true; }
                why = "a hair part is back, front, side, option or 0..3";
                return false;
            }
            if (text != null && text.StartsWith("clothes:", StringComparison.Ordinal))
            {
                int k = Index(text.Substring(8), ClothesSet.Names);
                if (k >= 0) { target = Clothes(node, k); return true; }
                why = "a clothes kind is " + string.Join(", ", ClothesSet.Names) + " or 0..7";
                return false;
            }
            if (text != null && text.StartsWith("acc:", StringComparison.Ordinal))
            {
                string id = text.Substring(4);
                if (JsonText.IsId(id)) { target = Accessory(node, id); return true; }
                why = "an accessory is addressed by its id (32 lowercase hex digits)";
                return false;
            }
            why = "no target \"" + text + "\"";
            return false;
        }

        static int Index(string text, string[] names)
        {
            int k = Array.IndexOf(names, text);
            if (k >= 0) return k;
            return text.Length == 1 && text[0] >= '0' && text[0] - '0' < names.Length ? text[0] - '0' : -1;
        }

        /// <summary>The target as the commands word it.</summary>
        public string Text
        {
            get
            {
                switch (Kind)
                {
                    case MaterialTargetKind.Hair: return "hair:" + Slot;
                    case MaterialTargetKind.Clothes: return "clothes:" + Slot;
                    case MaterialTargetKind.Accessory: return "acc:" + AccessoryId;
                    case MaterialTargetKind.Item: return "item";
                    case MaterialTargetKind.Map: return "map";
                    default: return "character";
                }
            }
        }

        /// <summary>The array's field path on the node (the scene for the map).</summary>
        public string ArrayPath
        {
            get
            {
                switch (Kind)
                {
                    case MaterialTargetKind.Hair: return AppearancePaths.Chara("hair." + HairData.PartNames[Slot] + ".materialOverrides");
                    case MaterialTargetKind.Clothes: return AppearancePaths.Coord("clothes." + ClothesSet.Names[Slot] + ".materialOverrides");
                    case MaterialTargetKind.Accessory: return AppearancePaths.Accessory(AccessoryId, "materialOverrides");
                    case MaterialTargetKind.Item: return "item.materialOverrides";
                    case MaterialTargetKind.Map: return "map.materialOverrides";
                    default: return AppearancePaths.Chara("materialOverrides");
                }
            }
        }
    }

    /// <summary>
    /// One edit as a caller states it: an item's key and the members to write, in the stored forms. What the target holds without the item
    /// is the op's to find. A `texture` edit names `file` (an image to import), `image` (a res name, or null to clear it), `offset` and
    /// `scale`; a `shader` edit names `shader` and `renderQueue`; a member named as null is cleared.
    /// </summary>
    public sealed class MaterialEditRequest
    {
        /// <summary>The item's type: float, color, keyword, renderer, texture or shader. Any other type answers as read-only or unknown.</summary>
        public string Type;
        public string Material, Property, Renderer;
        public float Number;
        public Color4 Color;
        public bool Switch;
        public int Enabled;
        /// <summary>An image file to import (FileRefs.Resolve's forms).</summary>
        public string File;
        public bool HasImage, HasOffset, HasScale, HasShader, HasQueue;
        public string Image, Shader;
        public Vec2? Offset, Scale;
        public int? Queue;
        /// <summary>The argument that gave this edit, for the detail of a refusal (null: <c>value</c>, <c>material</c> ...; else that text + <c>.value</c> ...).</summary>
        public string Arg;

        public static MaterialEditRequest Float(string material, string property, float value) => new MaterialEditRequest { Type = "float", Material = material, Property = property, Number = value };
        public static MaterialEditRequest ColorOf(string material, string property, Color4 value) => new MaterialEditRequest { Type = "color", Material = material, Property = property, Color = value };
        public static MaterialEditRequest Keyword(string material, string keyword, bool on) => new MaterialEditRequest { Type = "keyword", Material = material, Property = keyword, Switch = on };
        /// <summary>A renderer's <c>Enabled</c>.</summary>
        public static MaterialEditRequest RendererEnabled(string renderer, bool on) => new MaterialEditRequest { Type = "renderer", Renderer = renderer, Property = MeKey.EnabledProperty, Enabled = on ? 1 : 0 };
        /// <summary>A `texture` edit with no member named yet.</summary>
        public static MaterialEditRequest Texture(string material, string property) => new MaterialEditRequest { Type = "texture", Material = material, Property = property };
        /// <summary>A `shader` edit with no member named yet.</summary>
        public static MaterialEditRequest ShaderItem(string material) => new MaterialEditRequest { Type = "shader", Material = material };

        public MaterialEditRequest WithFile(string file) { File = file; return this; }
        public MaterialEditRequest WithImage(string res) { HasImage = true; Image = res; return this; }
        public MaterialEditRequest WithOffset(Vec2? offset) { HasOffset = true; Offset = offset; return this; }
        public MaterialEditRequest WithScale(Vec2? scale) { HasScale = true; Scale = scale; return this; }
        public MaterialEditRequest WithShader(string shader) { HasShader = true; Shader = shader; return this; }
        public MaterialEditRequest WithQueue(int? queue) { HasQueue = true; Queue = queue; return this; }

        /// <summary>The key of the item this edit writes.</summary>
        public MeKey Key => Type == "renderer" ? MeKey.Renderer(Renderer, Property) : Type == "shader" ? MeKey.Shader(Material) : MeKey.Property(Type, Material, Property);

        internal string ArgOf(string member) => Arg == null ? member : Arg + "." + member;

        // Every member it names is a null: it clears, and writes nothing.
        internal bool OnlyClears =>
            File == null && (HasImage || HasOffset || HasScale || HasShader || HasQueue) && (!HasImage || Image == null) && (!HasOffset || !Offset.HasValue) &&
            (!HasScale || !Scale.HasValue) && (!HasShader || Shader == null) && (!HasQueue || !Queue.HasValue);
    }

    public sealed partial class StudioOps
    {
        /// <summary>The message of a refusal of the mosaic rule.</summary>
        public const string MaterialMosaicRefusal = "mosaic parts are not editable";
        /// <summary>The code of the warning that goes with it.</summary>
        public const string MaterialMosaicWarning = "me.mosaicProtected";

        static OpResult MosaicRefused()
        {
            var f = OpResult.Unsupported(MaterialMosaicRefusal);
            f.Warnings.Add(MaterialMosaicRefusal + " (" + MaterialMosaicWarning + ")");
            return f;
        }

        // What an op needs of a built target.
        sealed class MaterialSite
        {
            public MaterialWriteTarget Write;
            public Func<MaterialOverride, MaterialItemState> Status;
            public Func<Material, string> ShaderOriginal;
            /// <summary>A build writes the items before the URP mapping (a character); items and the map write them after it.</summary>
            public bool ItemsBeforeMapping;
        }

        // How names reach the materials and renderers of a character's object, for the checks of an op: reads only.
        sealed class CharaObjectTarget : MaterialWriteTarget
        {
            readonly CharaContext ctx;
            readonly (OT, int) obj;

            public CharaObjectTarget(CharaContext ctx, (OT, int) obj)
            {
                this.ctx = ctx;
                this.obj = obj;
                Baseline = MaterialEditorApplier.BaselineOf(ctx, obj);
                LinearNormals = ctx.Options?.MaterialEditorLinearNormals ?? false;
            }

            public override void Select(string material, List<Material> into, bool forWrite, out bool heldByMember, out string note)
            {
                heldByMember = false;
                note = null;
                var rends = MaterialEditorApplier.ObjectRenderers(ctx, obj);
                if (rends == null) return;
                foreach (var r in rends)
                {
                    if (!r) continue;
                    bool member = MosaicSet.IsMember(r), body = ctx.DrawsBodyMaterials(r) || ctx.DrawsBodySkin(r);
                    foreach (var m in r.sharedMaterials)
                    {
                        if (!m || MaterialEditorApplier.Fmt(m.name) != material) continue;
                        if (member) heldByMember = true;
                        else if (body) note = MaterialStatus.DrawsBody;
                        else if (!into.Contains(m)) into.Add(m);
                    }
                }
            }

            public override void SelectRenderers(string renderer, List<Renderer> into)
            {
                var rends = MaterialEditorApplier.ObjectRenderers(ctx, obj);
                if (rends == null) return;
                foreach (var r in rends)
                    if (r && MaterialEditorApplier.Fmt(r.name) == renderer) into.Add(r);
            }

            public override Texture2D Image(string res, bool linear) => null;
            public override bool ImageReadable(string res) => false;
        }

        // What the target holds without the item, for the members the edit names.
        sealed class MaterialFacts
        {
            public float? Number;
            public Color4? Color;
            public bool? Switch;
            public int? Enabled;
            public Vec2? Offset, Scale;
            public string Shader;
            public int? Queue;
            /// <summary>A new item here would not outlive a rebuild.</summary>
            public bool OutOfReach;
        }

        // What a plan makes of the array.
        sealed class ArrayStep<T>
        {
            public List<T> Next;
            public bool StandsAlone;
            public OpResult Fail;
            public static ArrayStep<T> Refused(OpResult fail) => new ArrayStep<T> { Fail = fail };
        }

        // ------------------------------------------------------------ reading

        /// <summary>The array of a target: the document's list, read-only, or the list the character was built from while its node is
        /// card-backed; empty when there is none.</summary>
        public List<MaterialOverride> MaterialArray(MaterialEditTarget t)
        {
            switch (t.Kind)
            {
                case MaterialTargetKind.Item: return (Model.Find(t.NodeId)?.State as ItemNodeState)?.MaterialOverrides ?? new List<MaterialOverride>();
                case MaterialTargetKind.Map: return Model.Settings.Map.MaterialOverrides;
            }
            DocumentsOf(t.NodeId, out var chara, out var coord);
            List<MaterialOverride> list = null;
            switch (t.Kind)
            {
                case MaterialTargetKind.Character: list = chara?.MaterialOverrides; break;
                case MaterialTargetKind.Hair: list = chara?.Hair.Parts[t.Slot].MaterialOverrides; break;
                case MaterialTargetKind.Clothes: list = coord?.Clothes.Parts[t.Slot].MaterialOverrides; break;
                case MaterialTargetKind.Accessory: list = coord?.FindAccessory(t.AccessoryId)?.MaterialOverrides; break;
            }
            return list ?? new List<MaterialOverride>();
        }

        // The character's documents: the node's own, or what a card-backed node was built from.
        void DocumentsOf(string id, out CharaDocument chara, out CoordDocument coord)
        {
            var st = Model.Find(id)?.Appearance?.Stored;
            if (st != null)
            {
                chara = st.Chara;
                coord = st.Coord;
                return;
            }
            var input = PoseBuilt(id)?.Context?.Input;
            chara = input?.Chara;
            coord = input?.Coord;
        }

        /// <summary>The targets of a character that hold an array: the character, the four hair parts, the eight clothes kinds, each accessory.</summary>
        public List<MaterialEditTarget> MaterialTargetsOf(string id)
        {
            var l = new List<MaterialEditTarget> { MaterialEditTarget.Character(id) };
            for (int k = 0; k < HairData.PartNames.Length; k++) l.Add(MaterialEditTarget.Hair(id, k));
            for (int k = 0; k < ClothesSet.Count; k++) l.Add(MaterialEditTarget.Clothes(id, k));
            DocumentsOf(id, out _, out var coord);
            if (coord != null) foreach (var a in coord.Accessories) l.Add(MaterialEditTarget.Accessory(id, a.Id));
            return l;
        }

        /// <summary>The status read of a built target: what a stored item does now (no write, no copy, no decode). Null with the refusal
        /// for a target that is not built, or no map loaded.</summary>
        public Func<MaterialOverride, MaterialItemState> MaterialStatusRead(MaterialEditTarget t, out OpResult fail)
        {
            fail = CheckMaterialNode(t);
            if (fail != null) return null;
            return SiteOf(t, out fail)?.Status;
        }

        /// <summary>The time of the last pass over an item's or the map's array, in milliseconds; null for a character's arrays or a target
        /// that is not built.</summary>
        public double? MaterialPassMilliseconds(MaterialEditTarget t)
        {
            if (t.Kind != MaterialTargetKind.Item && t.Kind != MaterialTargetKind.Map) return null;
            return (SiteOf(t, out _)?.Write as MaterialTargets)?.LastPassMilliseconds;
        }

        // The node of a target, of the right kind.
        OpResult CheckMaterialNode(MaterialEditTarget t)
        {
            if (t.Kind == MaterialTargetKind.Map) return null;
            OfKind(new[] { t.NodeId }, t.Kind == MaterialTargetKind.Item ? NodeKind.Item : NodeKind.Chara, t.Kind == MaterialTargetKind.Item ? "item" : "character", "key", out var fail);
            return fail;
        }

        MaterialSite SiteOf(MaterialEditTarget t, out OpResult fail)
        {
            fail = null;
            switch (t.Kind)
            {
                case MaterialTargetKind.Item: return ItemSite(t, out fail);
                case MaterialTargetKind.Map: return MapSite(out fail);
                default: return CharaSite(t, out fail);
            }
        }

        MaterialSite CharaSite(MaterialEditTarget t, out OpResult fail)
        {
            fail = null;
            var r = PoseBuilt(t.NodeId);
            if (r == null)
            {
                string state = rt.FindChara(t.NodeId)?.State.ToString() ?? "NotRealized";
                fail = OpResult.Unavailable("character " + t.NodeId + " is not built (" + state + ")", "key", state);
                return null;
            }
            var ctx = r.Context;
            (OT, int) obj;
            switch (t.Kind)
            {
                case MaterialTargetKind.Hair: obj = (OT.Hair, t.Slot); break;
                case MaterialTargetKind.Clothes: obj = (OT.Clothing, t.Slot); break;
                case MaterialTargetKind.Accessory:
                {
                    int slot = AccessorySlotOf(Model.Find(t.NodeId), t.AccessoryId);
                    if (slot < 0 && ctx.Input?.Slots != null && ctx.Input.Slots.SlotById.TryGetValue(t.AccessoryId, out int built)) slot = built;
                    if (slot < 0)
                    {
                        fail = OpResult.BadArgs("target", "a target of this character", "this character has no accessory " + t.AccessoryId);
                        return null;
                    }
                    obj = (OT.Accessory, slot);
                    break;
                }
                default: obj = (OT.Character, 0); break;
            }
            return new MaterialSite
            {
                Write = new CharaObjectTarget(ctx, obj),
                Status = item => MaterialEditorApplier.Status(ctx, obj, item),
                ShaderOriginal = m => m.shader ? m.shader.name : null,
                ItemsBeforeMapping = true,
            };
        }

        MaterialSite ItemSite(MaterialEditTarget t, out OpResult fail)
        {
            fail = null;
            var real = rt.FindItem(t.NodeId);
            if (real == null || real.State != RealizationState.Built || real.InGraveyard || !real.Instance)
            {
                string state = real?.State.ToString() ?? "NotRealized";
                fail = OpResult.Unavailable("item " + t.NodeId + " is not built (" + state + ")", "key", state);
                return null;
            }
            var target = real.MaterialEdits.Target;
            return new MaterialSite
            {
                Write = target,
                Status = item => MaterialWrites.StatusOf(item, target),
                ShaderOriginal = m => ItemShaderOriginal(real, m),
            };
        }

        static string ItemShaderOriginal(ItemRealization real, Material m)
        {
            foreach (var e in real.Writer.Renderers)
                foreach (var s in e.Slots)
                    if (s != null && s.Material == m) return s.ShaderOriginal;
            return m.shader ? m.shader.name : null;
        }

        MaterialSite MapSite(out OpResult fail)
        {
            fail = null;
            var map = rt.GetPart<MapRealization>();
            if (map == null || map.State != MapState.Loaded)
            {
                string state = (map?.State ?? MapState.None).ToString();
                fail = OpResult.Unavailable("no map is loaded (" + state + ")", null, state);
                return null;
            }
            var target = map.MaterialEdits.Target;
            return new MaterialSite
            {
                Write = target,
                Status = item => MaterialWrites.StatusOf(item, target),
                ShaderOriginal = m => map.Materials?.OriginOf(m)?.ShaderOriginal ?? (m.shader ? m.shader.name : null),
            };
        }

        /// <summary>The materials of the loaded map by name, each with its slots; empty without a map.</summary>
        public IReadOnlyList<MaterialTargets.MaterialEntry> MapMaterialList()
        {
            var site = MapSite(out _);
            return site != null ? ((MaterialTargets)site.Write).Materials : (IReadOnlyList<MaterialTargets.MaterialEntry>)Array.Empty<MaterialTargets.MaterialEntry>();
        }

        /// <summary>The shader a material of the loaded map came with, or null.</summary>
        public string MapShaderOriginal(Material m) => rt.GetPart<MapRealization>()?.Materials?.OriginOf(m)?.ShaderOriginal;

        // ------------------------------------------------------------ the array on the bus

        // True inside a UI edit: the frame's Apply runs the one flush.
        bool InUiEdit(ExecuteOptions o) => o.Source == CommandSource.User && Session.Commands.ActiveTransaction != null;

        // One history entry for one array: `plan` turns the array as it stands into the next. One write with a merge key goes on the bus
        // directly (the entry of a committed transaction is sealed, so only this form can chain); anything else in a transaction, a
        // card-backed character's import first. A card-backed node is imported only for a change: `plan` with its second argument true
        // keeps nothing and tells whether one comes, on the array the character was built from. The same list writes nothing.
        OpResult WriteArray<T>(string nodeId, string path, Func<List<T>> read, string label, ExecuteOptions o, Func<List<T>, bool, ArrayStep<T>> plan)
        {
            var node = nodeId != null ? Model.Find(nodeId) : null;
            bool import = node != null && node.Kind == NodeKind.Chara && node.Appearance?.Stored == null;
            var r = OpResult.NothingToDo();
            if (import)
            {
                var built = read();
                var look = plan(built, true);
                if (look.Fail != null) return look.Fail;
                if (ReferenceEquals(look.Next, built)) return r;
                MakerServices.UseSession(Session);
            }
            else if (o.MergeKey != null)
            {
                var cur = read();
                var s = plan(cur, false);
                if (s.Fail != null) return s.Fail;
                if (ReferenceEquals(s.Next, cur)) return r;
                var w = Labelled(o, label);
                if (s.StandsAlone) w.MergeKey = null;
                var c = Session.Commands.Set(nodeId, path, s.Next, w);
                return c.Status == CommandStatus.Error ? OpResult.From(c) : r.With(c);
            }
            var tx = Open(label, o, out var fail);
            if (tx == null) return fail;
            using (tx)
            {
                if (import)
                {
                    var imp = MakerEdits.EnsureImported(tx, Model, node);
                    if (imp.Status == CommandStatus.Error) return OpResult.From(imp, "key");
                }
                var cur = read();
                var s = plan(cur, false);
                if (s.Fail != null) return s.Fail;
                if (ReferenceEquals(s.Next, cur))
                {
                    tx.Cancel();
                    return r;
                }
                var c = tx.Set(nodeId, path, s.Next);
                if (c.Status == CommandStatus.Error) return OpResult.From(c);
                return Commit(tx, r);
            }
        }

        // ------------------------------------------------------------ edits

        /// <summary>One edit (one entry «Material edit»). See <see cref="SetMaterials"/>.</summary>
        public OpResult SetMaterial(MaterialEditTarget t, MaterialEditRequest edit, ExecuteOptions o = default, List<MaterialOverride> from = null) =>
            SetMaterials(t, new[] { edit }, o, from);

        /// <summary>
        /// Edits of one array in one entry «Material edit»; a refusal of one stores none of them. <paramref name="from"/>: the array the edits
        /// start from instead of the stored one (a drag's array at the press). Changed = the keys of items set, Value = the keys of items
        /// removed.
        /// </summary>
        public OpResult SetMaterials(MaterialEditTarget t, IReadOnlyList<MaterialEditRequest> edits, ExecuteOptions o = default, List<MaterialOverride> from = null)
        {
            if (edits == null || edits.Count == 0) return OpResult.BadArgs("items", "at least one edit");
            bool flush = !InUiEdit(o);
            if (flush) rt.ApplyNow();
            var fail = CheckMaterialNode(t);
            if (fail != null) return fail;
            var site = SiteOf(t, out fail);
            if (site == null) return fail;
            var changed = new List<string>();
            var removed = new List<string>();
            var holds = new List<string>();
            var files = new byte[edits.Count][];
            try
            {
                var r = WriteArray<MaterialOverride>(t.NodeId, t.ArrayPath, () => MaterialArray(t), Labels.MaterialEdit, o, (cur, look) => PlanEdits(site, from ?? cur, edits, files, holds, changed, removed, look));
                if (r.Status == CommandStatus.Error) return r;
                if (flush) rt.ApplyNow();
                r.Changed.AddRange(changed);
                r.Value = removed;
                return r;
            }
            finally
            {
                foreach (var h in holds) ExtTextures.Release(h);
                if (flush) ReleaseImportedTexture();
            }
        }

        // What the edits make of the array. `files` keeps the bytes of each edit's file, so that a file is read once. `look`: only whether a
        // change comes: the store and the lists stay as they are.
        ArrayStep<MaterialOverride> PlanEdits(MaterialSite site, List<MaterialOverride> list, IReadOnlyList<MaterialEditRequest> edits, byte[][] files, List<string> holds, List<string> changed, List<string> removed, bool look)
        {
            // first every check that needs no file, so that a refusal reads nothing and stores nothing
            var facts = new MaterialFacts[edits.Count];
            var bare = list;
            for (int i = 0; i < edits.Count; i++)
            {
                var q = edits[i];
                var bad = CheckForm(q);
                if (bad != null) return ArrayStep<MaterialOverride>.Refused(bad);
                if (q.Type == "shader" && q.HasShader && q.Shader != null && MaterialGuard.Shader(q.Shader)) return ArrayStep<MaterialOverride>.Refused(MosaicRefused());
                string image = q.File != null ? PlaceholderImage : q.Image;
                var next = Apply(bare, ToEdit(q, image, null), q, out bad);
                if (bad != null) return ArrayStep<MaterialOverride>.Refused(bad);
                bool same = ReferenceEquals(next, bare);
                bool goes = MaterialEdits.LastIndex(bare, q.Key) >= 0 && MaterialEdits.LastIndex(next, q.Key) < 0;
                if (!same && !goes && !q.OnlyClears)
                {
                    // the writer's own rule first: it knows the names that are refused whatever the target holds, and it judges a stored item
                    string status = site.Status(MaterialEdits.Find(next, q.Key)).Status;
                    if (status == MaterialStatus.MosaicProtected) return ArrayStep<MaterialOverride>.Refused(MosaicRefused());
                    bad = CheckTarget(site, q, bare, out facts[i]);
                    if (bad == null && status == MaterialStatus.NotOnShader) bad = OpResult.NotFound("the shader of \"" + q.Material + "\" has no " + q.Property + " an edit can reach", q.ArgOf("property"));
                    // a value equal to what the target holds makes no item, so nothing is lost
                    if (bad == null && facts[i].OutOfReach && !ReferenceEquals(Apply(bare, ToEdit(q, image, facts[i]), q, out _), bare))
                        bad = OpResult.NotFound("only the replacement of \"" + q.Material + "\"'s shader has " + q.Property + ": the next rebuild would lose this item. A shader item naming that replacement puts it within reach", q.ArgOf("property"));
                    if (bad != null) return ArrayStep<MaterialOverride>.Refused(bad);
                }
                bare = next;
            }
            // the files: every one must pass before any is stored
            var context = FileRefContext.Of(rt);
            for (int i = 0; i < edits.Count; i++)
            {
                var q = edits[i];
                if (q.File == null || files[i] != null) continue;
                var bytes = MaterialTextureImport.Read(q.File, context, out string why);
                if (bytes == null) return ArrayStep<MaterialOverride>.Refused(OpResult.BadArgs(q.ArgOf("file"), "a PNG or JPEG file", why));
                string key = MaterialTextureImport.Decode(bytes, LinearDecode(site, q.Property), out why);
                if (key == null) return ArrayStep<MaterialOverride>.Refused(OpResult.BadArgs(q.ArgOf("file"), "a PNG or JPEG file", why));
                holds.Add(key);
                files[i] = bytes;
            }
            // the array with what the target holds without each item
            var result = list;
            for (int i = 0; i < edits.Count; i++)
            {
                var q = edits[i];
                var next = Apply(result, ToEdit(q, files[i] != null ? ResStore.NameOf(files[i]) : q.Image, facts[i]), q, out var bad);
                if (bad != null) return ArrayStep<MaterialOverride>.Refused(bad);
                if (!look && !ReferenceEquals(next, result))
                {
                    string text = q.Key.ToString();
                    bool goes = MaterialEdits.LastIndex(next, q.Key) < 0;
                    Note(goes ? removed : changed, text);
                    (goes ? changed : removed).Remove(text);
                }
                result = next;
            }
            if (look) return new ArrayStep<MaterialOverride> { Next = result };
            foreach (var bytes in files)
                if (bytes != null) Model.File.Res.Add(bytes);
            return new ArrayStep<MaterialOverride> { Next = result, StandsAlone = removed.Count > 0 };
        }

        // A valid res name for the first pass over an edit whose image is not read yet.
        const string PlaceholderImage = "res/0000000000000000000000000000000000000000000000000000000000000000.png";

        static void Note(List<string> list, string text)
        {
            if (!list.Contains(text)) list.Add(text);
        }

        static List<MaterialOverride> Apply(List<MaterialOverride> list, MaterialEdit edit, MaterialEditRequest q, out OpResult fail)
        {
            fail = null;
            try { return MaterialEdits.Set(list, edit); }
            catch (CommandException e)
            {
                fail = OpResult.Fail(e.Code, e.Message, q.Arg ?? "type");
                return null;
            }
        }

        static MaterialEdit ToEdit(MaterialEditRequest q, string image, MaterialFacts f)
        {
            switch (q.Type)
            {
                case "float": return MaterialEdit.Float(q.Material, q.Property, q.Number, f?.Number);
                case "color": return MaterialEdit.ColorOf(q.Material, q.Property, q.Color, f?.Color);
                case "keyword": return MaterialEdit.Keyword(q.Material, q.Property, q.Switch, f?.Switch);
                case "renderer": return MaterialEdit.RendererEnabled(q.Renderer, q.Enabled, f?.Enabled);
                case "texture":
                {
                    var e = MaterialEdit.Texture(q.Material, q.Property);
                    if (q.File != null || q.HasImage) e.WithImage(image);
                    if (q.HasOffset) e.WithOffset(q.Offset, f?.Offset);
                    if (q.HasScale) e.WithScale(q.Scale, f?.Scale);
                    return e;
                }
                default:
                {
                    var e = MaterialEdit.ShaderItem(q.Material);
                    if (q.HasShader) e.WithShader(q.Shader, f?.Shader);
                    if (q.HasQueue) e.WithQueue(q.Queue, f?.Queue);
                    return e;
                }
            }
        }

        // What the edit's own members say, before any look at the target.
        OpResult CheckForm(MaterialEditRequest q)
        {
            switch (q.Type)
            {
                case "float": case "color": case "keyword": case "texture": case "shader": case "renderer":
                    break;
                case null:
                    return OpResult.BadArgs(q.ArgOf("type"), "float, color, keyword, renderer, texture or shader");
                default:
                    if (MaterialOverride.Rank(q.Type) >= 0) return OpResult.Unsupported("the type " + q.Type + " cannot be edited");
                    return OpResult.BadArgs(q.ArgOf("type"), "float, color, keyword, renderer, texture or shader", "no type \"" + q.Type + "\"");
            }
            if (q.Type == "renderer")
            {
                if (string.IsNullOrEmpty(q.Renderer)) return OpResult.BadArgs(q.ArgOf("renderer"), "a renderer");
                if (q.Property != MeKey.EnabledProperty) return OpResult.Unsupported("the renderer property " + q.Property + " cannot be edited");
                return q.Enabled == 0 || q.Enabled == 1 ? null : OpResult.BadArgs(q.ArgOf("value"), "0 or 1");
            }
            if (string.IsNullOrEmpty(q.Material)) return OpResult.BadArgs(q.ArgOf("material"), "a material");
            if (q.Type != "shader" && string.IsNullOrEmpty(q.Property)) return OpResult.BadArgs(q.ArgOf("property"), "a property, as stored");
            switch (q.Type)
            {
                case "float":
                    return Finite(q.Number) ? null : OpResult.BadArgs(q.ArgOf("value"), "a finite number");
                case "color":
                    return Finite(q.Color.R) && Finite(q.Color.G) && Finite(q.Color.B) && Finite(q.Color.A) ? null : OpResult.BadArgs(q.ArgOf("value"), "finite numbers");
                case "texture":
                    if (q.File != null && q.HasImage) return OpResult.BadArgs(q.ArgOf("image"), "file or image, not both");
                    if (q.File == null && !q.HasImage && !q.HasOffset && !q.HasScale) return OpResult.BadArgs(q.ArgOf("file"), "file, image, offset or scale");
                    if (q.Image != null)
                    {
                        if (!JsonText.IsResName(q.Image)) return OpResult.BadArgs(q.ArgOf("image"), "a res name");
                        if (Model.File?.Res == null || !Model.File.Res.Contains(q.Image)) return OpResult.NotFound("the scene holds no image \"" + q.Image + "\"", q.ArgOf("image"));
                    }
                    if (q.Offset.HasValue && !(Finite(q.Offset.Value.X) && Finite(q.Offset.Value.Y))) return OpResult.BadArgs(q.ArgOf("offset"), "finite numbers");
                    if (q.Scale.HasValue && !(Finite(q.Scale.Value.X) && Finite(q.Scale.Value.Y))) return OpResult.BadArgs(q.ArgOf("scale"), "finite numbers");
                    return null;
                case "shader":
                    if (!q.HasShader && !q.HasQueue) return OpResult.BadArgs(q.ArgOf("shader"), "shader or renderQueue");
                    if (q.Queue.HasValue && (q.Queue < MaterialEdits.MinQueue || q.Queue > MaterialEdits.MaxQueue)) return OpResult.BadArgs(q.ArgOf("renderQueue"), MaterialEdits.MinQueue + ".." + MaterialEdits.MaxQueue);
                    if (q.HasShader && q.Shader != null && q.Shader.Length == 0) return OpResult.BadArgs(q.ArgOf("shader"), "a shader's name");
                    return null;
                default:
                    return null;
            }
        }

        // What the built target says of the edit, and what it holds without the item. A new item's original comes from here.
        static OpResult CheckTarget(MaterialSite site, MaterialEditRequest q, List<MaterialOverride> list, out MaterialFacts f)
        {
            f = new MaterialFacts();
            var w = site.Write;
            if (q.Type == "renderer")
            {
                var rends = new List<Renderer>();
                w.SelectRenderers(q.Renderer, rends);
                if (rends.Count == 0) return OpResult.NotFound("no renderer \"" + q.Renderer + "\" on this target", q.ArgOf("renderer"));
                foreach (var r in rends)
                    if (MaterialGuard.Renderer(r)) return MosaicRefused();
                f.Enabled = Pristine(w, rends[0], MaterialMember.Enabled, null).Flag ? 1 : 0;
                return null;
            }
            var mats = new List<Material>();
            w.Select(q.Material, mats, false, out bool held, out string note);
            if (mats.Count == 0)
                return held ? MosaicRefused() : OpResult.NotFound("no material \"" + q.Material + "\" on this target" + (note == MaterialStatus.DrawsBody ? ": this part draws the body's material" : ""), q.ArgOf("material"));
            mats.RemoveAll(MaterialGuard.Material);
            if (mats.Count == 0) return MosaicRefused();
            string prop = "_" + q.Property;
            if (site.ItemsBeforeMapping && (q.Type == "float" || q.Type == "color" || q.Type == "texture") && MaterialEdits.LastIndex(list, q.Key) < 0)
                f.OutOfReach = !WithinReach(list, mats, q.Material, prop);
            switch (q.Type)
            {
                case "float":
                {
                    var m = Declaring(mats, prop, q, t => t == ShaderPropertyType.Float || t == ShaderPropertyType.Range || t == ShaderPropertyType.Int, out var bad);
                    if (m) f.Number = Pristine(w, m, MaterialMember.Float, prop).Float;
                    return bad;
                }
                case "color":
                {
                    var m = Declaring(mats, prop, q, t => t == ShaderPropertyType.Color, out var bad);
                    if (m)
                    {
                        var c = Pristine(w, m, MaterialMember.Color, prop).Color;
                        f.Color = new Color4(c.r, c.g, c.b, c.a);
                    }
                    return bad;
                }
                case "keyword":
                {
                    Material on = null;
                    bool hs2 = false;
                    foreach (var m in mats)
                    {
                        var sh = m.shader;
                        if (!sh || !MaterialShaders.IsDirectName(sh.name)) continue;
                        hs2 = true;
                        if (!on && sh.keywordSpace.FindKeyword(prop).isValid) on = m;
                    }
                    if (!hs2) return OpResult.BadArgs(q.ArgOf("material"), "a material on an HS2Next shader", "keywords are edited only on HS2Next shaders");
                    if (!on) return OpResult.NotFound("the shader of \"" + q.Material + "\" has no keyword " + prop, q.ArgOf("property"));
                    f.Switch = Pristine(w, on, MaterialMember.Keyword, prop).Flag;
                    return null;
                }
                case "texture":
                {
                    var m = Declaring(mats, prop, q, t => t == ShaderPropertyType.Texture, out var bad);
                    if (bad != null) return bad;
                    var sh = m.shader;
                    if (sh.GetPropertyTextureDimension(sh.FindPropertyIndex(prop)) != TextureDimension.Tex2D) return OpResult.Unsupported("the property " + prop + " is not a 2D texture");
                    if (q.HasOffset) f.Offset = V2(Pristine(w, m, MaterialMember.Offset, prop).Vector2);
                    if (q.HasScale) f.Scale = V2(Pristine(w, m, MaterialMember.Scale, prop).Vector2);
                    return null;
                }
                default:
                {
                    if (q.HasShader && q.Shader != null && !MaterialShaders.IsCandidate(q.Shader))
                        return OpResult.BadArgs(q.ArgOf("shader"), "a shader of materials.shaders", "\"" + q.Shader + "\" cannot be picked");
                    f.Shader = site.ShaderOriginal(mats[0]);
                    if (q.HasQueue) f.Queue = Pristine(w, mats[0], MaterialMember.Queue, "").Queue;
                    return null;
                }
            }
        }

        // True when a build reaches the property on one of the materials: the shader the mapping replaced declares it, or a shader item of this
        // material, the call's own too, names a shader that does.
        static bool WithinReach(List<MaterialOverride> list, List<Material> mats, string material, string prop)
        {
            foreach (var m in mats)
                if (MaterialWrites.Declared(m, prop)) return true;
            var item = MaterialEdits.Find(list, MeKey.Shader(material)) as MeShader;
            var named = item?.Shader != null ? MaterialShaders.Find(item.Shader)?.Shader : null;
            return named && named.FindPropertyIndex(prop) >= 0;
        }

        // The first material whose shader declares the property as a type `accepts`; else why none is.
        static Material Declaring(List<Material> mats, string prop, MaterialEditRequest q, Func<ShaderPropertyType, bool> accepts, out OpResult bad)
        {
            bad = null;
            bool declared = false, wrong = false, vector = false, fixedValue = false;
            foreach (var m in mats)
            {
                var sh = m.shader;
                int i = sh ? sh.FindPropertyIndex(prop) : -1;
                if (i < 0) continue;
                declared = true;
                var type = sh.GetPropertyType(i);
                if (!accepts(type))
                {
                    wrong = true;
                    if (type == ShaderPropertyType.Vector) vector = true;
                    continue;
                }
                if (MaterialWrites.FallbackFixed(m, prop)) { fixedValue = true; continue; }
                return m;
            }
            if (!declared) bad = OpResult.NotFound("the shader of \"" + q.Material + "\" has no property " + prop, q.ArgOf("property"));
            else if (fixedValue) bad = OpResult.Unsupported("the general fallback fixes " + prop);
            else if (vector) bad = OpResult.Unsupported("the vector property " + prop + " is read-only");
            else if (wrong) bad = OpResult.BadArgs(q.ArgOf("type"), "the type of the property", "the property " + prop + " is not of the type " + q.Type);
            return null;
        }

        static MaterialBaseline.Value Pristine(MaterialWriteTarget w, UnityEngine.Object target, MaterialMember kind, string property)
        {
            if (w.Baseline != null && w.Baseline.TryGet(target, kind, property, out var v)) return v;
            MaterialBaseline.TryRead(target, kind, property ?? "", out v);
            return v;
        }

        static Vec2 V2(Vector2 v) => new Vec2(v.x, v.y);

        static bool LinearDecode(MaterialSite site, string property) => site.Write.LinearNormals && (property.Contains("Bump") || property.Contains("Normal"));

        /// <summary>Removes items of an array, one entry «Clear material edits»: the item with <paramref name="key"/>, the editable items of
        /// <paramref name="material"/>, or every editable item when both are null. Read-only items stay. Value = the keys removed.</summary>
        public OpResult ClearMaterial(MaterialEditTarget t, MeKey? key = null, string material = null, ExecuteOptions o = default)
        {
            bool flush = !InUiEdit(o);
            if (flush) rt.ApplyNow();
            var fail = CheckMaterialNode(t);
            if (fail != null) return fail;
            if (SiteOf(t, out fail) == null) return fail;
            if (key.HasValue && !MaterialEdits.IsEditable(key.Value)) return OpResult.Unsupported("the item " + key.Value + " cannot be edited");
            var taken = new List<string>();
            var r = WriteArray<MaterialOverride>(t.NodeId, t.ArrayPath, () => MaterialArray(t), Labels.MaterialClear, o, (cur, look) =>
            {
                var next = key.HasValue ? MaterialEdits.Remove(cur, key.Value) : material != null ? MaterialEdits.ClearMaterial(cur, material) : MaterialEdits.Clear(cur);
                if (!look && !ReferenceEquals(next, cur))
                    foreach (var item in cur)
                        if (!next.Contains(item) && MeKey.TryOf(item, out var k)) taken.Add(k.ToString());
                return new ArrayStep<MaterialOverride> { Next = next, StandsAlone = true };
            });
            if (r.Status == CommandStatus.Error) return r;
            if (flush) rt.ApplyNow();
            r.Value = taken;
            return r;
        }

        // ------------------------------------------------------------ textures

        // The decoded images of ImportMaterialTexture held until the next edit has applied them.
        readonly List<string> importHolds = new List<string>();

        /// <summary>
        /// Takes an image file into the scene's store (no history entry): <paramref name="resName"/> is the res name an edit then names. The
        /// image stays decoded until that edit has applied, so that its apply decodes nothing. A refusal stores nothing and answers bad_args
        /// on <c>file</c> with the reason.
        /// </summary>
        public OpResult ImportMaterialTexture(string picked, bool linear, out string resName)
        {
            resName = null;
            ReleaseImportedTexture();
            var res = Model.File?.Res;
            if (res == null) return OpResult.BadArgs("file", "a PNG or JPEG file", "there is no scene to take the image");
            var bytes = MaterialTextureImport.Read(picked, FileRefContext.Of(rt), out string why);
            if (bytes == null) return OpResult.BadArgs("file", "a PNG or JPEG file", why);
            string key = MaterialTextureImport.Decode(bytes, linear, out why);
            if (key == null) return OpResult.BadArgs("file", "a PNG or JPEG file", why);
            importHolds.Add(key);
            resName = res.Add(bytes);
            return OpResult.Success();
        }

        /// <summary>Drops what <see cref="ImportMaterialTexture"/> held. An edit does this when it has applied; a caller that makes no edit
        /// after all calls it.</summary>
        public void ReleaseImportedTexture()
        {
            foreach (var k in importHolds) ExtTextures.Release(k);
            importHolds.Clear();
        }

        // ------------------------------------------------------------ hair bone modifiers

        /// <summary>The character's bone modifiers: the document's list, read-only, or the list a card-backed node was built from; empty when
        /// there is none.</summary>
        public List<BodyBoneMod> BoneModArray(string id)
        {
            DocumentsOf(id, out var chara, out _);
            return chara?.Body.BoneMods ?? new List<BodyBoneMod>();
        }

        /// <summary>
        /// Changes the modifier of a bone of the hair class (one entry «Hair bone adjustment»): the members given, the others keep their
        /// value; a result with no effect removes the entry. <paramref name="from"/>: the array a drag's step starts from. Changed = the
        /// bone when set, Value = the bone when its entry left (List&lt;string&gt;).
        /// </summary>
        public OpResult SetHairBoneMod(string id, string bone, Vec3? scale = null, float? length = null, Vec3? position = null, Vec3? rotation = null, ExecuteOptions o = default, List<BodyBoneMod> from = null)
        {
            bool flush = !InUiEdit(o);
            if (flush) rt.ApplyNow();
            var nodes = OfKind(new[] { id }, NodeKind.Chara, "character", "key", out var fail);
            if (nodes == null) return fail;
            var built = BoneModBuilt(id, out fail);
            if (built == null) return fail;
            var removed = new List<string>();
            var changed = new List<string>();
            var r = WriteArray<BodyBoneMod>(id, BoneModEdits.Path, () => BoneModArray(id), Labels.HairBoneAdjust, o, (cur, look) =>
            {
                var list = from ?? cur;
                List<BodyBoneMod> next;
                try { next = BoneModEdits.Set(list, bone, scale, length, position, rotation); }
                catch (CommandException e) { return ArrayStep<BodyBoneMod>.Refused(OpResult.Fail(e.Code, e.Message, string.IsNullOrEmpty(bone) ? "bone" : "scale")); }
                if (ReferenceEquals(next, list)) return new ArrayStep<BodyBoneMod> { Next = next };
                bool goes = BoneModEdits.LastIndex(next, bone) < 0;
                if (!goes)
                {
                    var t = HairBones.Find(built.Context, bone);
                    if (!t) return ArrayStep<BodyBoneMod>.Refused(OpResult.NotFound("this character has no hair bone \"" + bone + "\"", "bone"));
                    if (MosaicSet.GuardsBone(built.Context, t)) return ArrayStep<BodyBoneMod>.Refused(MosaicRefused());
                }
                if (!look) (goes ? removed : changed).Add(bone);
                return new ArrayStep<BodyBoneMod> { Next = next, StandsAlone = goes };
            });
            if (r.Status == CommandStatus.Error) return r;
            if (flush) rt.ApplyNow();
            r.Changed.AddRange(changed);
            r.Value = removed;
            return r;
        }

        /// <summary>Removes modifiers (one entry «Hair bone adjustment»): the entries of <paramref name="bone"/>, or of every bone of the hair
        /// class when it is null. Value = the bones removed (List&lt;string&gt;).</summary>
        public OpResult ClearHairBoneMods(string id, string bone = null, ExecuteOptions o = default)
        {
            bool flush = !InUiEdit(o);
            if (flush) rt.ApplyNow();
            var nodes = OfKind(new[] { id }, NodeKind.Chara, "character", "key", out var fail);
            if (nodes == null) return fail;
            var built = BoneModBuilt(id, out fail);
            if (built == null) return fail;
            var taken = new List<string>();
            var r = WriteArray<BodyBoneMod>(id, BoneModEdits.Path, () => BoneModArray(id), Labels.HairBoneAdjust, o, (cur, look) =>
            {
                List<BodyBoneMod> next;
                if (bone != null) next = BoneModEdits.Remove(cur, bone);
                else
                {
                    var names = HairBones.Names(built.Context);
                    next = BoneModEdits.RemoveWhere(cur, names.Contains);
                }
                if (!look && !ReferenceEquals(next, cur))
                    foreach (var m in cur)
                        if (!next.Contains(m) && !taken.Contains(m.Bone)) taken.Add(m.Bone);
                return new ArrayStep<BodyBoneMod> { Next = next, StandsAlone = true };
            });
            if (r.Status == CommandStatus.Error) return r;
            if (flush) rt.ApplyNow();
            r.Value = taken;
            return r;
        }

        CharaRealization BoneModBuilt(string id, out OpResult fail)
        {
            fail = null;
            var r = PoseBuilt(id);
            if (r != null) return r;
            string state = rt.FindChara(id)?.State.ToString() ?? "NotRealized";
            fail = OpResult.Unavailable("character " + id + " is not built (" + state + ")", "key", state);
            return null;
        }
    }
}
