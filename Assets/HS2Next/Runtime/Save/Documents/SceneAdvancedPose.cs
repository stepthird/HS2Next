// The advanced-pose block of a character node: the scene.json member `chara.advancedPose`, the last known member of the
// section, written only when it holds something (such a scene is format revision 4). Bone corrections by address, per-chain
// and bust jiggle values, blend-shape weights by renderer and shape name. Unity-free save-layer classes: the node keeps one
// instance (never null, empty by default) and field paths reach every member (`chara.advancedPose.bones["a"].pos`).
//
// The reader binds strictly (a wrong type fails the load as for every core member, a JSON null included) and repairs nothing
// away: a key that is not an address, a value out of range and the excess over a limit stay in the document and are written
// back; each is reported, and the Applied* members below say what acts. Nothing here writes a card, a coordinate or a pose file.
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>The limits of one character's block. A command beyond one is refused; a file's excess is kept in the document,
    /// not applied, and reported (<see cref="AdvancedPoseFindings.Limit"/>).</summary>
    public static class AdvancedPoseLimits
    {
        public const int MaxBones = 1024;
        public const int MaxChains = 256;
        public const int MaxRenderers = 64;
        /// <summary>Weights of every renderer together.</summary>
        public const int MaxWeights = 1024;
    }

    /// <summary>The ranges of the block's values: a command refuses a value outside; a file's value is kept and clamped when
    /// applied (<see cref="AdvancedPoseFindings.Range"/>).</summary>
    public static class AdvancedPoseRanges
    {
        /// <summary>Each component of a position offset lies in [-MaxPos, MaxPos].</summary>
        public const float MaxPos = 1000f;
        /// <summary>Each component of a scale factor lies in [MinScale, MaxScale]: 0 would make a singular matrix for the
        /// children, the physics and the skinning.</summary>
        public const float MinScale = 0.001f, MaxScale = 1000f;
        /// <summary>A blend weight, in the engine's units (0..100 on the slider, typed values beyond).</summary>
        public const float MinBlendWeight = -200f, MaxBlendWeight = 300f;
        /// <summary>A freeze axis: 0 none, 1 x, 2 y, 3 z.</summary>
        public const int MaxFreezeAxis = 3;

        public static bool PosInRange(Vec3 v) => Abs(v.X) <= MaxPos && Abs(v.Y) <= MaxPos && Abs(v.Z) <= MaxPos;
        public static bool ScaleInRange(Vec3 v) => In(v.X, MinScale, MaxScale) && In(v.Y, MinScale, MaxScale) && In(v.Z, MinScale, MaxScale);
        /// <summary>Weight, damping, elasticity, stiffness, inert: 0..1.</summary>
        public static bool UnitInRange(float f) => In(f, 0f, 1f);
        public static bool RadiusInRange(float f) => f >= 0f;
        public static bool FreezeAxisInRange(int i) => i >= 0 && i <= MaxFreezeAxis;
        public static bool BlendWeightInRange(float f) => In(f, MinBlendWeight, MaxBlendWeight);

        public static Vec3 ClampPos(Vec3 v) => new Vec3(Clamp(v.X, -MaxPos, MaxPos), Clamp(v.Y, -MaxPos, MaxPos), Clamp(v.Z, -MaxPos, MaxPos));
        public static Vec3 ClampScale(Vec3 v) => new Vec3(Clamp(v.X, MinScale, MaxScale), Clamp(v.Y, MinScale, MaxScale), Clamp(v.Z, MinScale, MaxScale));
        public static float ClampUnit(float f) => Clamp(f, 0f, 1f);
        public static float ClampRadius(float f) => f < 0f ? 0f : f;
        public static int ClampFreezeAxis(int i) => i < 0 ? 0 : i > MaxFreezeAxis ? MaxFreezeAxis : i;
        public static float ClampBlendWeight(float f) => Clamp(f, MinBlendWeight, MaxBlendWeight);

        static float Abs(float f) => f < 0f ? -f : f;
        static bool In(float f, float min, float max) => f >= min && f <= max;
        static float Clamp(float f, float min, float max) => f < min ? min : f > max ? max : f;
    }

    /// <summary>Finding codes of advanced pose.</summary>
    public static class AdvancedPoseFindings
    {
        /// <summary>Warning (reader): a key that is not an address in its canonical form; kept, inactive.</summary>
        public const string BadAddress = "advancedPose.badAddress";
        /// <summary>Info (reader): a value out of range; kept, clamped when applied.</summary>
        public const string Range = "advancedPose.range";
        /// <summary>Warning (reader): entries over a limit; kept, not applied.</summary>
        public const string Limit = "advancedPose.limit";
        /// <summary>Info (runtime, once per node and build): entries whose bone, chain, renderer or shape this character lacks; kept, no effect.</summary>
        public const string Stale = "advancedPose.stale";
        /// <summary>Warning (runtime, once per node and build): entries on a part that keeps the mosaic in place; kept, not applied.</summary>
        public const string MosaicProtected = "advancedPose.mosaicProtected";
    }

    /// <summary>The three key forms of the block.</summary>
    public enum PoseAddressForm
    {
        /// <summary>A key of <c>bones</c>: a bare name, or <c>hair:k/</c>, <c>clothes:k/</c>, <c>acc:id/</c> + name.</summary>
        Bone,
        /// <summary>A key of <c>chains</c>: <c>hair:k/</c>, <c>clothes:k/</c>, <c>acc:id/</c>, <c>other/</c> + the root's name.</summary>
        Chain,
        /// <summary>A key of <c>blendShapes</c>: <c>head/</c>, <c>body/</c>, <c>hair:k/</c>, <c>clothes:k/</c>, <c>acc:id/</c> + the renderer's name.</summary>
        Blend,
    }

    /// <summary>The part of an address.</summary>
    public enum PosePart
    {
        /// <summary>A bone address without a part: the body bones, then the head bones.</summary>
        Named,
        Head,
        Body,
        /// <summary>A hair part, slot 0..3.</summary>
        Hair,
        /// <summary>A clothes part, slot 0..7.</summary>
        Clothes,
        /// <summary>An accessory, by its id.</summary>
        Accessory,
        /// <summary>The swing chains that belong to no part.</summary>
        Other,
    }

    /// <summary>
    /// The text grammar of an address, used by the block's reader; <c>HS2Next.Scenes.PoseAddress</c> is the value type the runtime,
    /// the commands and the UI work with. A name holds no <c>/</c>. <c>#n</c> after a name in a part picks its n-th occurrence
    /// (0-based, depth first); absent is the first, so <c>#0</c> is not canonical, unless the name itself ends in <c>#</c> and
    /// a number, which keeps the mark apart (<c>joint#2#0</c>). A bare bone name is taken whole.
    /// </summary>
    public static class PoseAddressGrammar
    {
        public const string HairPrefix = "hair:", ClothesPrefix = "clothes:", AccessoryPrefix = "acc:";
        public const string HeadPart = "head", BodyPart = "body", OtherPart = "other";
        public const int HairSlots = 4, ClothesSlots = 8;

        /// <summary>
        /// Splits <paramref name="text"/>; false with the reason in <paramref name="error"/> when it is not an address of
        /// <paramref name="form"/>. <paramref name="slot"/> is the hair or clothes slot (else -1), <paramref name="accessoryId"/> the
        /// accessory's id (else null).
        /// </summary>
        public static bool TryParse(PoseAddressForm form, string text, out PosePart part, out int slot, out string accessoryId, out string name, out int occurrence, out string error)
        {
            part = PosePart.Named;
            slot = -1;
            accessoryId = null;
            name = null;
            occurrence = 0;
            error = null;
            if (string.IsNullOrEmpty(text)) { error = "an empty address"; return false; }
            int cut = text.IndexOf('/');
            if (form == PoseAddressForm.Bone && !StartsWithPart(text))
            {
                if (cut >= 0) { error = "a bone name holds no '/'"; return false; }
                name = text;
                return true;
            }
            if (cut <= 0) { error = "expected <part>/<name>"; return false; }
            if (!TryParsePart(text.Substring(0, cut), out part, out slot, out accessoryId, out error)) return false;
            if (!Allowed(form, part)) { error = "the part " + text.Substring(0, cut) + " does not hold " + (form == PoseAddressForm.Bone ? "bones by part" : form == PoseAddressForm.Chain ? "chains" : "blend shapes"); return false; }
            string rest = text.Substring(cut + 1);
            if (rest.IndexOf('/') >= 0) { error = "a name holds no '/'"; return false; }
            SplitOccurrence(rest, out name, out occurrence);
            if (name.Length == 0) { error = "an empty name"; return false; }
            return true;
        }

        /// <summary>Reads a part's text (<c>head</c>, <c>body</c>, <c>other</c>, <c>hair:k</c>, <c>clothes:k</c>, <c>acc:id</c>).</summary>
        public static bool TryParsePart(string text, out PosePart part, out int slot, out string accessoryId, out string error)
        {
            part = PosePart.Named;
            slot = -1;
            accessoryId = null;
            error = null;
            switch (text)
            {
                case HeadPart: part = PosePart.Head; return true;
                case BodyPart: part = PosePart.Body; return true;
                case OtherPart: part = PosePart.Other; return true;
            }
            if (text != null && text.StartsWith(HairPrefix, StringComparison.Ordinal))
            {
                part = PosePart.Hair;
                if (Slot(text, HairPrefix.Length, HairSlots, out slot)) return true;
                error = "a hair slot is 0.." + (HairSlots - 1);
                return false;
            }
            if (text != null && text.StartsWith(ClothesPrefix, StringComparison.Ordinal))
            {
                part = PosePart.Clothes;
                if (Slot(text, ClothesPrefix.Length, ClothesSlots, out slot)) return true;
                error = "a clothes slot is 0.." + (ClothesSlots - 1);
                return false;
            }
            if (text != null && text.StartsWith(AccessoryPrefix, StringComparison.Ordinal))
            {
                part = PosePart.Accessory;
                accessoryId = text.Substring(AccessoryPrefix.Length);
                if (JsonText.IsId(accessoryId)) return true;
                accessoryId = null;
                error = "an accessory is addressed by its id (32 lowercase hex digits)";
                return false;
            }
            error = "no part \"" + text + "\"";
            return false;
        }

        /// <summary>The canonical text of a part (empty for <see cref="PosePart.Named"/>).</summary>
        public static string PartText(PosePart part, int slot, string accessoryId)
        {
            switch (part)
            {
                case PosePart.Head: return HeadPart;
                case PosePart.Body: return BodyPart;
                case PosePart.Other: return OtherPart;
                case PosePart.Hair: return HairPrefix + slot;
                case PosePart.Clothes: return ClothesPrefix + slot;
                case PosePart.Accessory: return AccessoryPrefix + accessoryId;
                default: return "";
            }
        }

        /// <summary>The canonical text of an address.</summary>
        public static string Format(PosePart part, int slot, string accessoryId, string name, int occurrence)
        {
            if (part == PosePart.Named) return name;
            string text = PartText(part, slot, accessoryId) + "/" + name;
            SplitOccurrence(name, out string own, out _);
            return occurrence > 0 || own.Length < name.Length ? text + "#" + occurrence : text;
        }

        /// <summary>The canonical text of <paramref name="text"/>, or null when it is not an address of <paramref name="form"/>.</summary>
        public static string Canonical(PoseAddressForm form, string text) =>
            TryParse(form, text, out var part, out int slot, out string id, out string name, out int n, out _) ? Format(part, slot, id, name, n) : null;

        /// <summary>True when <paramref name="text"/> is an address of <paramref name="form"/> in its canonical form: the keys that act.</summary>
        public static bool IsCanonical(PoseAddressForm form, string text)
        {
            string canonical = Canonical(form, text);
            return canonical != null && string.Equals(canonical, text, StringComparison.Ordinal);
        }

        static bool StartsWithPart(string text) =>
            text.StartsWith(HairPrefix, StringComparison.Ordinal) || text.StartsWith(ClothesPrefix, StringComparison.Ordinal) || text.StartsWith(AccessoryPrefix, StringComparison.Ordinal);

        static bool Allowed(PoseAddressForm form, PosePart part)
        {
            switch (part)
            {
                case PosePart.Hair:
                case PosePart.Clothes:
                case PosePart.Accessory: return true;
                case PosePart.Other: return form == PoseAddressForm.Chain;
                case PosePart.Head:
                case PosePart.Body: return form == PoseAddressForm.Blend;
                default: return false;
            }
        }

        static bool Slot(string text, int at, int count, out int slot)
        {
            slot = -1;
            if (text.Length != at + 1) return false;
            int d = text[at] - '0';
            if (d < 0 || d >= count) return false;
            slot = d;
            return true;
        }

        // "name#n": n in canonical decimal (no sign, no leading zero, at most nine digits); anything else is part of the name.
        static void SplitOccurrence(string rest, out string name, out int occurrence)
        {
            name = rest;
            occurrence = 0;
            int j = rest.LastIndexOf('#');
            if (j < 0) return;
            int len = rest.Length - j - 1;
            if (len < 1 || len > 9 || (len > 1 && rest[j + 1] == '0')) return;
            int n = 0;
            for (int i = j + 1; i < rest.Length; i++)
            {
                int d = rest[i] - '0';
                if (d < 0 || d > 9) return;
                n = n * 10 + d;
            }
            name = rest.Substring(0, j);
            occurrence = n;
        }
    }

    /// <summary>One bone correction, in the bone's local space: <see cref="Pos"/> added to the local position, the local rotation
    /// post-multiplied by <see cref="Rot"/> (Euler degrees), each axis of the local scale multiplied by <see cref="Scale"/>.
    /// All three are written on each entry.</summary>
    public sealed class BoneCorrection : DocObject
    {
        public Vec3 Pos = Vec3.Zero;
        public Vec3 Rot = Vec3.Zero;
        public Vec3 Scale = Vec3.One;

        public BoneCorrection() { }
        public BoneCorrection(Vec3 pos, Vec3 rot, Vec3 scale) { Pos = pos; Rot = rot; Scale = scale; }

        public bool IsPosIdentity => Pos.X == 0f && Pos.Y == 0f && Pos.Z == 0f;
        public bool IsRotIdentity => Rot.X == 0f && Rot.Y == 0f && Rot.Z == 0f;
        public bool IsScaleIdentity => Scale.X == 1f && Scale.Y == 1f && Scale.Z == 1f;
        /// <summary>No channel changes the bone. A command removes such an entry; one from a file is kept and does nothing.</summary>
        public bool IsIdentity => IsPosIdentity && IsRotIdentity && IsScaleIdentity;

        /// <summary>The position offset as it is applied (clamped).</summary>
        public Vec3 AppliedPos => AdvancedPoseRanges.ClampPos(Pos);
        /// <summary>The scale factor as it is applied (clamped).</summary>
        public Vec3 AppliedScale => AdvancedPoseRanges.ClampScale(Scale);

        internal override void ReadMembers(DocReader r)
        {
            Pos = r.V3("pos", Pos);
            Rot = r.Euler("rot", Rot);
            Scale = r.V3("scale", Scale);
            if (!AdvancedPoseRanges.PosInRange(Pos)) AdvancedPoseRead.Range(r, "pos", Pos + " is outside +-" + AdvancedPoseRanges.MaxPos);
            if (!AdvancedPoseRanges.ScaleInRange(Scale)) AdvancedPoseRead.Range(r, "scale", Scale + " is outside " + AdvancedPoseRanges.MinScale + ".." + AdvancedPoseRanges.MaxScale);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("pos", Pos);
            w.Euler("rot", Rot);
            w.V3("scale", Scale);
        }
    }

    /// <summary>Scene values of one swing chain: every member optional, null = no override (the card's value stays). Only the
    /// members set are written.</summary>
    public sealed class ChainValues : DocObject
    {
        /// <summary>False stops the chain and its bones return to the rest pose; true does not override a card's no-shake.</summary>
        public bool? Enabled;
        /// <summary>0..1.</summary>
        public float? Weight, Damping, Elasticity, Stiffness, Inert;
        /// <summary>At least 0.</summary>
        public float? Radius;
        public Vec3? Gravity, Force;
        /// <summary>0 none, 1 x, 2 y, 3 z.</summary>
        public int? FreezeAxis;

        /// <summary>True when no member is set and nothing unknown is kept.</summary>
        public bool IsEmpty => !Enabled.HasValue && !Weight.HasValue && !Damping.HasValue && !Elasticity.HasValue && !Stiffness.HasValue && !Inert.HasValue &&
                               !Radius.HasValue && !Gravity.HasValue && !Force.HasValue && !FreezeAxis.HasValue && (Unknown == null || Unknown.IsEmpty);

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public ChainValues Applied() => new ChainValues
        {
            Enabled = Enabled,
            Weight = Weight.HasValue ? AdvancedPoseRanges.ClampUnit(Weight.Value) : (float?)null,
            Damping = Damping.HasValue ? AdvancedPoseRanges.ClampUnit(Damping.Value) : (float?)null,
            Elasticity = Elasticity.HasValue ? AdvancedPoseRanges.ClampUnit(Elasticity.Value) : (float?)null,
            Stiffness = Stiffness.HasValue ? AdvancedPoseRanges.ClampUnit(Stiffness.Value) : (float?)null,
            Inert = Inert.HasValue ? AdvancedPoseRanges.ClampUnit(Inert.Value) : (float?)null,
            Radius = Radius.HasValue ? AdvancedPoseRanges.ClampRadius(Radius.Value) : (float?)null,
            Gravity = Gravity,
            Force = Force,
            FreezeAxis = FreezeAxis.HasValue ? AdvancedPoseRanges.ClampFreezeAxis(FreezeAxis.Value) : (int?)null,
        };

        internal override void ReadMembers(DocReader r)
        {
            Enabled = AdvancedPoseRead.Opt(r, "enabled", v => v.AsBool());
            Weight = AdvancedPoseRead.Unit(r, "weight");
            Damping = AdvancedPoseRead.Unit(r, "damping");
            Elasticity = AdvancedPoseRead.Unit(r, "elasticity");
            Stiffness = AdvancedPoseRead.Unit(r, "stiffness");
            Inert = AdvancedPoseRead.Unit(r, "inert");
            Radius = AdvancedPoseRead.Opt(r, "radius", v => v.AsF32());
            if (Radius.HasValue && !AdvancedPoseRanges.RadiusInRange(Radius.Value)) AdvancedPoseRead.Range(r, "radius", Radius.Value + " is below 0");
            Gravity = AdvancedPoseRead.Opt(r, "gravity", v => v.AsV3());
            Force = AdvancedPoseRead.Opt(r, "force", v => v.AsV3());
            FreezeAxis = AdvancedPoseRead.Opt(r, "freezeAxis", v => v.AsInt());
            if (FreezeAxis.HasValue && !AdvancedPoseRanges.FreezeAxisInRange(FreezeAxis.Value)) AdvancedPoseRead.Range(r, "freezeAxis", FreezeAxis.Value + " is outside 0.." + AdvancedPoseRanges.MaxFreezeAxis);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (Enabled.HasValue) w.Bool("enabled", Enabled.Value);
            if (Weight.HasValue) w.F32("weight", Weight.Value);
            if (Damping.HasValue) w.F32("damping", Damping.Value);
            if (Elasticity.HasValue) w.F32("elasticity", Elasticity.Value);
            if (Stiffness.HasValue) w.F32("stiffness", Stiffness.Value);
            if (Inert.HasValue) w.F32("inert", Inert.Value);
            if (Radius.HasValue) w.F32("radius", Radius.Value);
            if (Gravity.HasValue) w.V3("gravity", Gravity.Value);
            if (Force.HasValue) w.V3("force", Force.Value);
            if (FreezeAxis.HasValue) w.Int("freezeAxis", FreezeAxis.Value);
        }
    }

    /// <summary>Scene values of one bust or hip chain: every member optional, null = no override. Only the members set are written.</summary>
    public sealed class BustValues : DocObject
    {
        /// <summary>False turns the chain off; true never overrides the FK breast or the animation switches.</summary>
        public bool? Enabled;
        public Vec3? Gravity, Force;
        /// <summary>0..1.</summary>
        public float? Damping, Elasticity, Stiffness, Inert;

        /// <summary>True when no member is set and nothing unknown is kept.</summary>
        public bool IsEmpty => !Enabled.HasValue && !Gravity.HasValue && !Force.HasValue && !Damping.HasValue && !Elasticity.HasValue && !Stiffness.HasValue && !Inert.HasValue &&
                               (Unknown == null || Unknown.IsEmpty);

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public BustValues Applied() => new BustValues
        {
            Enabled = Enabled,
            Gravity = Gravity,
            Force = Force,
            Damping = Damping.HasValue ? AdvancedPoseRanges.ClampUnit(Damping.Value) : (float?)null,
            Elasticity = Elasticity.HasValue ? AdvancedPoseRanges.ClampUnit(Elasticity.Value) : (float?)null,
            Stiffness = Stiffness.HasValue ? AdvancedPoseRanges.ClampUnit(Stiffness.Value) : (float?)null,
            Inert = Inert.HasValue ? AdvancedPoseRanges.ClampUnit(Inert.Value) : (float?)null,
        };

        internal override void ReadMembers(DocReader r)
        {
            Enabled = AdvancedPoseRead.Opt(r, "enabled", v => v.AsBool());
            Gravity = AdvancedPoseRead.Opt(r, "gravity", v => v.AsV3());
            Force = AdvancedPoseRead.Opt(r, "force", v => v.AsV3());
            Damping = AdvancedPoseRead.Unit(r, "damping");
            Elasticity = AdvancedPoseRead.Unit(r, "elasticity");
            Stiffness = AdvancedPoseRead.Unit(r, "stiffness");
            Inert = AdvancedPoseRead.Unit(r, "inert");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (Enabled.HasValue) w.Bool("enabled", Enabled.Value);
            if (Gravity.HasValue) w.V3("gravity", Gravity.Value);
            if (Force.HasValue) w.V3("force", Force.Value);
            if (Damping.HasValue) w.F32("damping", Damping.Value);
            if (Elasticity.HasValue) w.F32("elasticity", Elasticity.Value);
            if (Stiffness.HasValue) w.F32("stiffness", Stiffness.Value);
            if (Inert.HasValue) w.F32("inert", Inert.Value);
        }
    }

    /// <summary>The object <c>bust</c>: the two breast and two hip chains, never null so that field paths reach them. Only chains
    /// with a member are written; the object itself only when one has.</summary>
    public sealed class BustSet : DocObject
    {
        /// <summary>The member names, indexed by chain kind 0..3.</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "breastL", "breastR", "hipL", "hipR" };

        public BustValues BreastL = new BustValues(), BreastR = new BustValues(), HipL = new BustValues(), HipR = new BustValues();

        /// <summary>The values of chain kind 0..3 (breast left, breast right, hip left, hip right).</summary>
        public BustValues this[int kind]
        {
            get
            {
                switch (kind)
                {
                    case 0: return BreastL;
                    case 1: return BreastR;
                    case 2: return HipL;
                    case 3: return HipR;
                    default: throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }
        }

        /// <summary>The chain kind of a member name, or -1.</summary>
        public static int KindOf(string name)
        {
            for (int i = 0; i < Names.Count; i++) if (Names[i] == name) return i;
            return -1;
        }

        /// <summary>True when no chain has a member and nothing unknown is kept.</summary>
        public bool IsEmpty => BreastL.IsEmpty && BreastR.IsEmpty && HipL.IsEmpty && HipR.IsEmpty && (Unknown == null || Unknown.IsEmpty);

        internal override void ReadMembers(DocReader r)
        {
            BreastL = Chain(r, "breastL");
            BreastR = Chain(r, "breastR");
            HipL = Chain(r, "hipL");
            HipR = Chain(r, "hipR");
        }

        static BustValues Chain(DocReader r, string name)
        {
            var v = new BustValues();
            v.ReadFrom(r.Obj(name));
            return v;
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (!BreastL.IsEmpty) BreastL.WriteTo(w, "breastL");
            if (!BreastR.IsEmpty) BreastR.WriteTo(w, "breastR");
            if (!HipL.IsEmpty) HipL.WriteTo(w, "hipL");
            if (!HipR.IsEmpty) HipR.WriteTo(w, "hipR");
        }
    }

    /// <summary>Blend weights of one renderer, by shape name, in the engine's weight units.</summary>
    public sealed class BlendValues : DocObject
    {
        public SortedDictionary<string, float> Weights = NodeState.NewMap<float>();

        internal override void ReadMembers(DocReader r)
        {
            Weights = r.Map("weights", v => v.AsF32());
            DocReader at = null;
            foreach (var kv in Weights)
            {
                if (AdvancedPoseRanges.BlendWeightInRange(kv.Value)) continue;
                at ??= r.Obj("weights");
                AdvancedPoseRead.Range(at, kv.Key, kv.Value + " is outside " + AdvancedPoseRanges.MinBlendWeight + ".." + AdvancedPoseRanges.MaxBlendWeight);
            }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Map("weights", Weights, (ww, f) => ww.F32(f));
        }
    }

    /// <summary>One blend weight that acts: <see cref="CharaAdvancedPose.AppliedWeights"/>.</summary>
    public readonly struct AppliedBlendWeight
    {
        /// <summary>The key of <see cref="CharaAdvancedPose.BlendShapes"/>.</summary>
        public readonly string Renderer;
        public readonly string Shape;
        /// <summary>The weight as it is applied (clamped).</summary>
        public readonly float Weight;

        public AppliedBlendWeight(string renderer, string shape, float weight)
        {
            Renderer = renderer;
            Shape = shape;
            Weight = weight;
        }
    }

    /// <summary>The block <c>chara.advancedPose</c>: scene-only values of one character. Empty by default and then not written.</summary>
    public sealed class CharaAdvancedPose : DocObject
    {
        /// <summary>The member name in the section <c>chara</c>.</summary>
        public const string MemberName = "advancedPose";

        /// <summary>Address (<see cref="PoseAddressForm.Bone"/>) to correction.</summary>
        public SortedDictionary<string, BoneCorrection> Bones = NodeState.NewMap<BoneCorrection>();
        /// <summary>Address (<see cref="PoseAddressForm.Chain"/>) to scene values.</summary>
        public SortedDictionary<string, ChainValues> Chains = NodeState.NewMap<ChainValues>();
        /// <summary>The bust and hip chains; never null.</summary>
        public BustSet Bust = new BustSet();
        /// <summary>Address (<see cref="PoseAddressForm.Blend"/>) to weights.</summary>
        public SortedDictionary<string, BlendValues> BlendShapes = NodeState.NewMap<BlendValues>();

        /// <summary>True when the block holds nothing, unknown members included: it is then not written, and no revision is raised.</summary>
        public bool IsEmpty => Bones.Count == 0 && Chains.Count == 0 && BlendShapes.Count == 0 && Bust.IsEmpty && (Unknown == null || Unknown.IsEmpty);

        /// <summary>The bone entries that act, in key order: canonical addresses, not identity, the first <see cref="AdvancedPoseLimits.MaxBones"/>.</summary>
        public IEnumerable<KeyValuePair<string, BoneCorrection>> AppliedBones()
        {
            int n = 0;
            foreach (var kv in Bones)
            {
                if (kv.Value.IsIdentity || !PoseAddressGrammar.IsCanonical(PoseAddressForm.Bone, kv.Key)) continue;
                if (n++ >= AdvancedPoseLimits.MaxBones) yield break;
                yield return kv;
            }
        }

        /// <summary>The chain entries that act, in key order: canonical addresses, the first <see cref="AdvancedPoseLimits.MaxChains"/>.
        /// Use <see cref="ChainValues.Applied"/> for the clamped values.</summary>
        public IEnumerable<KeyValuePair<string, ChainValues>> AppliedChains()
        {
            int n = 0;
            foreach (var kv in Chains)
            {
                if (!PoseAddressGrammar.IsCanonical(PoseAddressForm.Chain, kv.Key)) continue;
                if (n++ >= AdvancedPoseLimits.MaxChains) yield break;
                yield return kv;
            }
        }

        /// <summary>The blend weights that act, clamped, in key order: canonical addresses, the first
        /// <see cref="AdvancedPoseLimits.MaxRenderers"/> renderers, the first <see cref="AdvancedPoseLimits.MaxWeights"/> weights.</summary>
        public IEnumerable<AppliedBlendWeight> AppliedWeights()
        {
            int renderers = 0, weights = 0;
            foreach (var kv in BlendShapes)
            {
                if (!PoseAddressGrammar.IsCanonical(PoseAddressForm.Blend, kv.Key)) continue;
                if (renderers++ >= AdvancedPoseLimits.MaxRenderers) yield break;
                foreach (var w in kv.Value.Weights)
                {
                    if (weights++ >= AdvancedPoseLimits.MaxWeights) yield break;
                    yield return new AppliedBlendWeight(kv.Key, w.Key, AdvancedPoseRanges.ClampBlendWeight(w.Value));
                }
            }
        }

        internal override void ReadMembers(DocReader r)
        {
            Bones = r.Map("bones", v => ReadNew<BoneCorrection>(v.AsObj()));
            Chains = r.Map("chains", v => ReadNew<ChainValues>(v.AsObj()));
            Bust = new BustSet();
            Bust.ReadFrom(r.Obj("bust"));
            BlendShapes = r.Map("blendShapes", v => ReadNew<BlendValues>(v.AsObj()));
            int bones = CheckKeys(r, "bones", Bones, PoseAddressForm.Bone, kv => !kv.Value.IsIdentity);
            int chains = CheckKeys(r, "chains", Chains, PoseAddressForm.Chain, null);
            int renderers = CheckKeys(r, "blendShapes", BlendShapes, PoseAddressForm.Blend, null);
            int weights = 0;
            foreach (var kv in BlendShapes)
                if (PoseAddressGrammar.IsCanonical(PoseAddressForm.Blend, kv.Key)) weights += kv.Value.Weights.Count;
            Over(r, "bones", bones, AdvancedPoseLimits.MaxBones, "bone entries");
            Over(r, "chains", chains, AdvancedPoseLimits.MaxChains, "chains");
            Over(r, "blendShapes", renderers, AdvancedPoseLimits.MaxRenderers, "renderers");
            if (renderers <= AdvancedPoseLimits.MaxRenderers) Over(r, "blendShapes", weights, AdvancedPoseLimits.MaxWeights, "weights");
        }

        // Reports the keys that are not canonical addresses; returns how many of the others count for the limit.
        static int CheckKeys<T>(DocReader r, string member, SortedDictionary<string, T> map, PoseAddressForm form, Func<KeyValuePair<string, T>, bool> counts)
        {
            int n = 0;
            DocReader at = null;
            foreach (var kv in map)
            {
                string canonical = PoseAddressGrammar.Canonical(form, kv.Key);
                if (string.Equals(canonical, kv.Key, StringComparison.Ordinal))
                {
                    if (counts == null || counts(kv)) n++;
                    continue;
                }
                at ??= r.Obj(member);
                if (canonical != null) at.Report(FindingSeverity.Warning, AdvancedPoseFindings.BadAddress, kv.Key, "the address is not in its canonical form " + canonical + "; kept, inactive");
                else
                {
                    PoseAddressGrammar.TryParse(form, kv.Key, out _, out _, out _, out _, out _, out string why);
                    at.Report(FindingSeverity.Warning, AdvancedPoseFindings.BadAddress, kv.Key, why + "; kept, inactive");
                }
            }
            return n;
        }

        static void Over(DocReader r, string member, int count, int limit, string what)
        {
            if (count > limit)
                r.Report(FindingSeverity.Warning, AdvancedPoseFindings.Limit, member, (count - limit) + " " + what + " over the limit of " + limit + " are kept and not applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Map("bones", Bones, (ww, e) => e.WriteTo(ww));
            w.Map("chains", Chains, (ww, e) => e.WriteTo(ww));
            if (!Bust.IsEmpty) Bust.WriteTo(w, "bust");
            w.Map("blendShapes", BlendShapes, (ww, e) => e.WriteTo(ww));
        }

        // ---------- text and copies ----------

        /// <summary>Reads a block from JSON text (one object) with the reader rules; findings go to <paramref name="findings"/>. A
        /// syntax or type error throws <see cref="SaveFormatException"/> (BadDocument; NonFinite for a number that is not finite).</summary>
        public static CharaAdvancedPose Parse(string json, FindingList findings = null, string entry = MemberName, bool lenient = false) =>
            FromToken(DocReader.ParseText(json ?? "", entry), findings, entry, lenient);

        /// <summary>As <see cref="Parse"/> for a parsed token (an object; anything else throws BadDocument). The token is not changed.</summary>
        public static CharaAdvancedPose FromToken(JToken token, FindingList findings = null, string entry = MemberName, bool lenient = false)
        {
            if (!(token is JObject o))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "an advanced-pose block is an object, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            var block = new CharaAdvancedPose();
            block.ReadFrom(new DocReader(o, new DocReadContext(entry, lenient, findings ?? new FindingList())));
            return block;
        }

        /// <summary>The canonical JSON bytes of the block (UTF-8, trailing LF), as scene.json writes it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(MemberName, WriteTo);

        /// <summary>The canonical JSON text of the block (no trailing LF).</summary>
        public string ToJson()
        {
            var b = ToBytes();
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }

        /// <summary>The block as a JSON object.</summary>
        public JObject ToJObject() => DocReader.Parse(ToBytes(), MemberName);

        /// <summary>A deep copy (a canonical write and read: unknown members kept, nothing shared).</summary>
        public CharaAdvancedPose Clone() => Parse(ToJson());
    }

    static class AdvancedPoseRead
    {
        // An optional member with no null form: absent is null; a JSON null is a wrong type like any other.
        public static T? Opt<T>(DocReader r, string name, Func<DocValue, T> convert) where T : struct
        {
            var v = r.Value(name);
            if (v == null) return null;
            try { return convert(v); }
            catch (DocTypeException e) when (r.Context.Lenient)
            {
                r.Context.Findings.Error("json.typeError", SaveFormatException.Location(e.Entry, e.JsonPath), e.Detail + " (absent value used)");
                return null;
            }
        }

        public static float? Unit(DocReader r, string name)
        {
            var f = Opt(r, name, v => v.AsF32());
            if (f.HasValue && !AdvancedPoseRanges.UnitInRange(f.Value)) Range(r, name, f.Value + " is outside 0..1");
            return f;
        }

        public static void Range(DocReader r, string member, string what) =>
            r.Report(FindingSeverity.Info, AdvancedPoseFindings.Range, member, what + "; kept, clamped when applied");
    }
}
