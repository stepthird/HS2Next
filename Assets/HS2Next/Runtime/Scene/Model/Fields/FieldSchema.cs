// Value rules of field writes ("Copies"): exact runtime
// types (int → float is the only widening), finite floats with −0 normalised to +0, fixed array lengths, refs
// that parse, known enum strings, node ids that exist, nulls only where the schema allows them. Values are
// deep-copied on the way in and out of the model (DocObject values through DocClone), compared bitwise for
// the NoOp test, and measured for the history estimate. The format never clamps ranges, so neither does this.
using System;
using System.Collections;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>Marker values of field access.</summary>
    public static class FieldValue
    {
        sealed class AbsentValue { public override string ToString() => "<absent>"; }

        /// <summary>The value of a map key that is not there (reads) / "remove the key" (undo of an added key).</summary>
        public static readonly object Absent = new AbsentValue();

        public static bool IsAbsent(object v) => ReferenceEquals(v, Absent);
    }

    /// <summary>Schema facts of one member beyond its C# type.</summary>
    internal sealed class MemberRule
    {
        public bool Nullable;
        /// <summary>A `ref` ("&lt;guid&gt;#&lt;id&gt;", parsed by ContentId).</summary>
        public bool Ref;
        /// <summary>A node id that must name a node in the model.</summary>
        public bool NodeId;
        /// <summary>Known enum strings, or null.</summary>
        public ICollection<string> Enum;
        public int FixedLength = -1;
        public bool ElementsNullable;
        /// <summary>Readable through paths, never written (route point ids).</summary>
        public bool ReadOnly;
        /// <summary>The route point list: non-empty, unique valid ids.</summary>
        public bool RoutePoints;
        /// <summary>`transform.scale`: (1, 1, 1) for kinds without a scale channel.</summary>
        public bool ScaleChannel;
        /// <summary>A container resource name "res/&lt;sha256&gt;.&lt;ext&gt;".</summary>
        public bool Res;
        /// <summary>An id in the save-format form (32 lowercase hex) that is not a node id (accessory links).</summary>
        public bool IdForm;
        /// <summary>Document objects of a fixed schema shape (ItemLayout with / without tilt, the clothes part of a kind,
        /// the classes without a parameterless constructor): a set value is read into a fresh object of this shape.</summary>
        public Func<DocObject> Blank;
        /// <summary>The accessory list: every element has a valid, unique id.</summary>
        public bool AccessoryIds;
        /// <summary>Lists the writer sorts (skin overlays by target, hair bundles by index): the order the file holds. Paths
        /// address elements by their position in this order, whole-list sets are stored in it, list commands and writes of
        /// the order key (<see cref="OrderKey"/>) or of whole elements are refused (the position would change under the path).</summary>
        public Func<IList, IList> WriteOrder;
        public string OrderKey;

        public static readonly MemberRule Default = new MemberRule();
    }

    internal static class FieldSchema
    {
        static readonly Dictionary<System.Reflection.FieldInfo, MemberRule> rules = BuildRules();

        static Dictionary<System.Reflection.FieldInfo, MemberRule> BuildRules()
        {
            var d = new Dictionary<System.Reflection.FieldInfo, MemberRule>();
            void R(Type t, string field, MemberRule r)
            {
                var f = t.GetField(field) ?? throw new InvalidOperationException("schema field missing: " + t.Name + "." + field);
                d[f] = r;
            }
            var nullable = new MemberRule { Nullable = true };
            var refN = new MemberRule { Nullable = true, Ref = true };
            var refReq = new MemberRule { Ref = true };
            var idN = new MemberRule { Nullable = true, NodeId = true };
            R(typeof(SceneNode), nameof(SceneNode.Name), nullable);
            R(typeof(NodeTransform), nameof(NodeTransform.Scale), new MemberRule { ScaleChannel = true });
            R(typeof(CharaNodeState), nameof(CharaNodeState.Hands), new MemberRule { FixedLength = 2 });
            R(typeof(CharaNodeState), nameof(CharaNodeState.Correction), new MemberRule { FixedLength = 8 });
            R(typeof(CharaAnimeState), nameof(CharaAnimeState.Ref), nullable);
            R(typeof(CharaAnimeState), nameof(CharaAnimeState.Params), new MemberRule { FixedLength = 2 });
            R(typeof(CharaLookState), nameof(CharaLookState.EyeRotations), nullable);
            R(typeof(CharaState), nameof(CharaState.Clothes), new MemberRule { FixedLength = 8 });
            R(typeof(CharaState), nameof(CharaState.Liquid), new MemberRule { FixedLength = 5 });
            R(typeof(CharaVoiceState), nameof(CharaVoiceState.Repeat), new MemberRule { Enum = DocEnum.CamelNamesOf<VoiceRepeat>() });
            R(typeof(ItemNodeState), nameof(ItemNodeState.Ref), refReq);
            R(typeof(ItemNodeState), nameof(ItemNodeState.Colors), new MemberRule { FixedLength = ItemNodeState.ColorCount });
            R(typeof(ItemNodeState), nameof(ItemNodeState.Panel), nullable);
            R(typeof(ItemPattern), nameof(ItemPattern.Item), refN);
            R(typeof(ItemPattern), nameof(ItemPattern.File), nullable);
            R(typeof(LightNodeState), nameof(LightNodeState.Ref), refReq);
            R(typeof(RouteNodeState), nameof(RouteNodeState.Orient), new MemberRule { Enum = DocEnum.CamelNamesOf<RouteOrient>() });
            R(typeof(RouteNodeState), nameof(RouteNodeState.Points), new MemberRule { RoutePoints = true });
            R(typeof(RoutePoint), nameof(RoutePoint.Id), new MemberRule { ReadOnly = true });
            R(typeof(RoutePoint), nameof(RoutePoint.Ease), new MemberRule { Enum = DocEnum.NamesOf<RouteEase>() });
            R(typeof(RoutePoint), nameof(RoutePoint.Connection), new MemberRule { Enum = DocEnum.CamelNamesOf<RouteConnection>() });
            R(typeof(RoutePoint), nameof(RoutePoint.Aid), nullable);
            R(typeof(SceneSettings), nameof(SceneSettings.Background), nullable);
            R(typeof(SceneSettings), nameof(SceneSettings.Frame), nullable);
            R(typeof(MapSettings), nameof(MapSettings.Ref), refN);
            R(typeof(CameraState), nameof(CameraState.Current), nullable);
            R(typeof(CameraState), nameof(CameraState.Slots), new MemberRule { ElementsNullable = true });
            R(typeof(CharaLightSettings), nameof(CharaLightSettings.Rot), new MemberRule { FixedLength = 2 });
            R(typeof(BgmSettings), nameof(BgmSettings.Ref), refN);
            R(typeof(BgmSettings), nameof(BgmSettings.Repeat), new MemberRule { Enum = DocEnum.CamelNamesOf<SoundRepeat>() });
            R(typeof(UserAudioSettings), nameof(UserAudioSettings.File), nullable);
            R(typeof(UserAudioSettings), nameof(UserAudioSettings.Repeat), new MemberRule { Enum = DocEnum.CamelNamesOf<SoundRepeat>() });
            R(typeof(ColorGradingEffect), nameof(ColorGradingEffect.Lut), refN);
            R(typeof(ReflectionProbeEffect), nameof(ReflectionProbeEffect.Cubemap), refN);
            R(typeof(SkyEffect), nameof(SkyEffect.Pattern), refN);
            R(typeof(DepthOfFieldEffect), nameof(DepthOfFieldEffect.Focus), idN);
            R(typeof(SunShaftsEffect), nameof(SunShaftsEffect.Caster), idN);
            AppearanceRules(R);
            return d;
        }

        // Character documents: refs, resources, fixed lengths, nullable
        // members and the object shapes. Numbers are never range-checked (the format never clamps).
        static void AppearanceRules(Action<Type, string, MemberRule> R)
        {
            var refReq = new MemberRule { Ref = true };
            var nullable = new MemberRule { Nullable = true };
            MemberRule Fixed(int n, bool elementsNullable = false) => new MemberRule { FixedLength = n, ElementsNullable = elementsNullable };
            MemberRule Shape(Func<DocObject> blank, bool isNullable = false) => new MemberRule { Blank = blank, Nullable = isNullable };
            // chara.json
            R(typeof(Profile), nameof(Profile.Hs2), nullable);
            R(typeof(FaceData), nameof(FaceData.Head), refReq);
            R(typeof(FaceData), nameof(FaceData.Skin), refReq);
            R(typeof(FaceData), nameof(FaceData.Shape), Fixed(FaceData.ShapeCount));
            R(typeof(FaceData), nameof(FaceData.Detail), Shape(() => new ItemPower(1f)));
            R(typeof(FaceData), nameof(FaceData.Eyebrow), Shape(() => new ItemLayout(default, default, true)));
            R(typeof(FaceData), nameof(FaceData.Eyelashes), Shape(() => new ItemColor(default)));
            R(typeof(FaceData), nameof(FaceData.Mole), Shape(() => new ItemLayout(default, default, false)));
            R(typeof(FaceData), nameof(FaceData.Beard), Shape(() => new ItemColor(default)));
            R(typeof(EyeData), nameof(EyeData.Highlight), Shape(() => new ItemLayout(default, default, true)));
            R(typeof(EyeIris), nameof(EyeIris.Item), refReq);
            R(typeof(EyePupil), nameof(EyePupil.Item), refReq);
            R(typeof(MakeupData), nameof(MakeupData.Paint), Fixed(MakeupData.PaintCount));
            R(typeof(MakeupLayer), nameof(MakeupLayer.Item), refReq);
            R(typeof(Paint), nameof(Paint.Item), refReq);
            R(typeof(Paint), nameof(Paint.LayoutItem), refReq);
            R(typeof(ItemPower), nameof(ItemPower.Item), refReq);
            R(typeof(ItemColor), nameof(ItemColor.Item), refReq);
            R(typeof(ItemLayout), nameof(ItemLayout.Item), refReq);
            R(typeof(BodyData), nameof(BodyData.Shape), Fixed(BodyData.ShapeCount));
            R(typeof(BodyData), nameof(BodyData.Detail), Shape(() => new ItemPower(0.5f)));
            R(typeof(BodyData), nameof(BodyData.Sunburn), Shape(() => new ItemColor(default)));
            R(typeof(BodyData), nameof(BodyData.Paint), Fixed(2));
            R(typeof(BodyData), nameof(BodyData.Underhair), Shape(() => new ItemColor(default)));
            R(typeof(BodyData), nameof(BodyData.Uncensor), nullable);
            R(typeof(BodySkin), nameof(BodySkin.Item), refReq);
            R(typeof(BodyNipple), nameof(BodyNipple.Item), refReq);
            R(typeof(UncensorData), nameof(UncensorData.Body), nullable);
            R(typeof(UncensorData), nameof(UncensorData.Penis), nullable);
            R(typeof(UncensorData), nameof(UncensorData.Balls), nullable);
            R(typeof(UncensorData), nameof(UncensorData.Legacy), nullable);
            R(typeof(SkinOverlay), nameof(SkinOverlay.Image), new MemberRule { Res = true });
            R(typeof(BodyData), nameof(BodyData.SkinOverlays), new MemberRule
            {
                WriteOrder = l => SkinOverlay.InWriteOrder(l as List<SkinOverlay> ?? new List<SkinOverlay>()), OrderKey = nameof(SkinOverlay.Target),
            });
            R(typeof(HairPart), nameof(HairPart.Bundles), new MemberRule { WriteOrder = BundlesInWriteOrder, OrderKey = nameof(HairBundle.Index) });
            R(typeof(BodyBoneMod), nameof(BodyBoneMod.Location), new MemberRule { Enum = new[] { BodyBoneMod.LocationUnknown, BodyBoneMod.LocationBodyTop } });
            R(typeof(HairPart), nameof(HairPart.Item), refReq);
            R(typeof(HairPart), nameof(HairPart.AccessoryColors), Fixed(HairPart.AccessoryColorCount));
            R(typeof(HairMesh), nameof(HairMesh.Item), refReq);
            // coord.json
            R(typeof(CoordDocument), nameof(CoordDocument.Accessories), new MemberRule { AccessoryIds = true });
            R(typeof(CoordDocument), nameof(CoordDocument.RuleOverrides), nullable);
            R(typeof(ClothesPart), nameof(ClothesPart.Item), refReq);
            R(typeof(ClothesPart), nameof(ClothesPart.Colors), Fixed(ClothesPart.ColorCount));
            R(typeof(ClothesPart), nameof(ClothesPart.HideOptions), Fixed(2));
            R(typeof(ClothesPart), nameof(ClothesPart.Overlays), nullable);
            R(typeof(ClothesPart), nameof(ClothesPart.PushUp), nullable);
            R(typeof(ClothesColor), nameof(ClothesColor.Pattern), refReq);
            R(typeof(ClothesOverlaySet), nameof(ClothesOverlaySet.Main), nullable);
            R(typeof(ClothesOverlaySet), nameof(ClothesOverlaySet.ColorMask), nullable);
            R(typeof(ClothesOverlaySet), nameof(ClothesOverlaySet.SourceReplacement), nullable);
            R(typeof(ClothesOverlaySet), nameof(ClothesOverlaySet.Patterns), Fixed(ClothesOverlaySet.PatternCount, true));
            R(typeof(OverlayEntry), nameof(OverlayEntry.Image), new MemberRule { Res = true, Nullable = true });
            R(typeof(AccessoryEntry), nameof(AccessoryEntry.Item), refReq);
            R(typeof(AccessoryEntry), nameof(AccessoryEntry.Moves), Fixed(AccessoryEntry.MoveCount));
            R(typeof(AccessoryEntry), nameof(AccessoryEntry.Colors), Fixed(AccessoryEntry.ColorCount));
            R(typeof(AccessoryEntry), nameof(AccessoryEntry.Rules), nullable);
            R(typeof(AccessoryRules), nameof(AccessoryRules.AdvancedParent), nullable);
            R(typeof(AccessoryRules), nameof(AccessoryRules.Name), nullable);
            R(typeof(AacRule), nameof(AacRule.Modifier), nullable);
            R(typeof(AacRule), nameof(AacRule.Link), new MemberRule { Nullable = true, IdForm = true });
            R(typeof(AacOverride), nameof(AacOverride.Modifier), nullable);
            R(typeof(AacOverride), nameof(AacOverride.OverrideModifier), nullable);
        }

        // HairPart writes its bundles stably sorted by index.
        static IList BundlesInWriteOrder(IList l)
        {
            var src = l as List<HairBundle> ?? new List<HairBundle>();
            var order = new List<KeyValuePair<int, HairBundle>>(src.Count);
            for (int i = 0; i < src.Count; i++) order.Add(new KeyValuePair<int, HairBundle>(i, src[i]));
            order.Sort((a, b) =>
            {
                int c = (a.Value?.Index ?? 0).CompareTo(b.Value?.Index ?? 0);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            var r = new List<HairBundle>(src.Count);
            foreach (var kv in order) r.Add(kv.Value);
            return r;
        }

        static readonly MemberRule[] clothesPartRules = BuildClothesPartRules();

        static MemberRule[] BuildClothesPartRules()
        {
            var a = new MemberRule[ClothesSet.Count];
            for (int k = 0; k < a.Length; k++)
            {
                var kind = (ClothesKind)k;
                a[k] = new MemberRule { Blank = () => new ClothesPart(kind) };
            }
            return a;
        }

        /// <summary>The rule of the clothes part of kind <paramref name="k"/> (its shape: the kind decides the pushUp defaults).</summary>
        public static MemberRule ClothesPartRule(int k) => clothesPartRules[k];

        public static MemberRule RuleOf(System.Reflection.FieldInfo f) => f != null && rules.TryGetValue(f, out var r) ? r : MemberRule.Default;

        /// <summary>The rule of an element of a collection member.</summary>
        public static MemberRule ElementRule(MemberRule collection) =>
            collection != null && collection.ElementsNullable ? ElementNullable : MemberRule.Default;

        static readonly MemberRule ElementNullable = new MemberRule { Nullable = true };

        // ---------------- coercion ----------------

        /// <summary>What a value is checked against.</summary>
        internal sealed class Context
        {
            public SceneModel Model;
            public TreeNode Node;
            public string Path;
        }

        static CommandException Bad(Context c, string what) => new CommandException(CommandCodes.BadArgs, c.Path + ": " + what);

        static float F(Context c, float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) throw Bad(c, "non-finite float");
            return f == 0f ? 0f : f;   // −0 → +0
        }

        /// <summary>
        /// A checked, normalised, deep-copied value for a slot of type <paramref name="t"/> (throws bad_args). The model
        /// later receives a further copy, so the result is owned by the caller (the command).
        /// </summary>
        public static object Coerce(Type t, MemberRule rule, object value, Context c)
        {
            rule = rule ?? MemberRule.Default;
            if (FieldValue.IsAbsent(value)) throw Bad(c, "Absent is not a value (remove map keys with MapRemoveCommand)");
            if (value == null)
            {
                if (rule.Nullable || Nullable.GetUnderlyingType(t) != null) return null;
                throw Bad(c, "null is not allowed here");
            }
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null && underlying != typeof(float) && underlying != typeof(Vec3)) return Coerce(underlying, rule, value, c);
            if (t == typeof(bool)) return value is bool ? value : throw Bad(c, "expected bool, got " + value.GetType().Name);
            if (t == typeof(int)) return value is int ? value : throw Bad(c, "expected int, got " + value.GetType().Name);
            if (t == typeof(float) || t == typeof(float?))
            {
                if (value is float f) return F(c, f);
                if (value is int i) return (float)i;
                throw Bad(c, "expected float, got " + value.GetType().Name);
            }
            if (t == typeof(string))
            {
                if (!(value is string s)) throw Bad(c, "expected string, got " + value.GetType().Name);
                if (rule.Ref && !JsonText.IsRef(s)) throw Bad(c, "not a ref \"<guid>#<id>\": \"" + s + "\"");
                if (rule.Enum != null && !rule.Enum.Contains(s)) throw Bad(c, "unknown value \"" + s + "\"");
                if (rule.NodeId) CheckNodeRef(c, s);
                if (rule.Res && !JsonText.IsResName(s)) throw Bad(c, "not a resource name \"res/<sha256>.<ext>\": \"" + s + "\"");
                if (rule.IdForm && !JsonText.IsId(s)) throw Bad(c, "not an id (32 lowercase hex): \"" + s + "\"");
                return s;
            }
            if (t == typeof(Vec3) || t == typeof(Vec3?)) return value is Vec3 v3 ? new Vec3(F(c, v3.X), F(c, v3.Y), F(c, v3.Z)) : throw Bad(c, "expected Vec3, got " + value.GetType().Name);
            if (t == typeof(Vec4)) return value is Vec4 v4 ? new Vec4(F(c, v4.X), F(c, v4.Y), F(c, v4.Z), F(c, v4.W)) : throw Bad(c, "expected Vec4, got " + value.GetType().Name);
            if (t == typeof(Vec2)) return value is Vec2 v2 ? new Vec2(F(c, v2.X), F(c, v2.Y)) : throw Bad(c, "expected Vec2, got " + value.GetType().Name);
            if (t == typeof(Color4)) return value is Color4 co ? new Color4(F(c, co.R), F(c, co.G), F(c, co.B), F(c, co.A)) : throw Bad(c, "expected Color4, got " + value.GetType().Name);
            if (t == typeof(Quat4)) return value is Quat4 q ? new Quat4(F(c, q.X), F(c, q.Y), F(c, q.Z), F(c, q.W)) : throw Bad(c, "expected Quat4, got " + value.GetType().Name);
            // Abstract document element types (MaterialEditor items) take any of their concrete classes.
            bool polymorphic = t.IsAbstract && typeof(DocObject).IsAssignableFrom(t) && t.IsInstanceOfType(value);
            if (value.GetType() != t && !polymorphic) throw Bad(c, "expected " + Name(t) + ", got " + Name(value.GetType()));

            if (t.IsArray)
            {
                var a = (Array)value;
                if (rule.FixedLength >= 0 && a.Length != rule.FixedLength) throw Bad(c, "needs exactly " + rule.FixedLength + " elements, got " + a.Length);
                var et = t.GetElementType();
                var r = Array.CreateInstance(et, a.Length);
                var aer = ElementRule(rule);
                for (int i = 0; i < a.Length; i++) r.SetValue(Coerce(et, aer, a.GetValue(i), c), i);
                return r;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var src = (IList)value;
                var et = t.GetGenericArguments()[0];
                var er = ElementRule(rule);
                var r = (IList)Activator.CreateInstance(t);
                foreach (var e in src) r.Add(Coerce(et, er, e, c));
                if (rule.RoutePoints) CheckRoutePoints(c, r, -1);
                if (rule.AccessoryIds) CheckAccessoryIds(c, r, null);
                if (rule.WriteOrder != null)
                {
                    var sorted = rule.WriteOrder(r);
                    r.Clear();
                    foreach (var e in sorted) r.Add(e);
                }
                return r;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(SortedDictionary<,>))
            {
                var vt = t.GetGenericArguments()[1];
                var r = (IDictionary)Activator.CreateInstance(t, StringComparer.Ordinal);
                foreach (DictionaryEntry kv in (IDictionary)value) r[(string)kv.Key] = Coerce(vt, MemberRule.Default, kv.Value, c);
                return r;
            }
            if (value is DocObject doc)
            {
                if (doc is OpaqueNode || doc is SceneNode) throw Bad(c, "a " + Name(t) + " is not a field value");
                var findings = new FindingList();
                DocObject copy;
                try { copy = rule.Blank != null ? DocClone.CloneInto(doc, rule.Blank(), findings) : DocClone.Clone(doc, out _, findings); }
                catch (Exception e) { throw Bad(c, "the value cannot be saved: " + e.Message); }
                // Character documents carry plugin data whose enum strings may be newer than this build (MaterialEditor item
                // types, bone-mod locations): a copy keeps them as read, like a load. So does an
                // item of the materialOverrides array of an item or the map, or an edit next to it could not be stored.
                if (findings.Has("json.unknownEnum") && !AppearancePaths.IsAppearance(c.Path) && !(doc is MaterialOverride)) throw Bad(c, "unknown enum string inside the value");
                if (doc is ItemLayout la && copy is ItemLayout lb && la.HasTilt && !lb.HasTilt)
                    throw Bad(c, "this layout has no tilt (a value with tilt does not fit)");
                // An overlay entry without image and clearTarget is written as null: store the save form,
                // so a later copy of the clothes part (undo of a whole-part set) cannot turn an object into null under a path.
                if (copy is OverlayEntry oe && oe.IsEmpty)
                {
                    if (!rule.Nullable) throw Bad(c, "an empty overlay entry is written as null and needs a nullable member");
                    return null;
                }
                CheckDocRefs(c, copy);
                return copy;
            }
            throw Bad(c, "values of type " + Name(t) + " are not settable");
        }

        static void CheckNodeRef(Context c, string id)
        {
            if (!JsonText.IsId(id)) throw Bad(c, "not a node id: \"" + id + "\"");
            var n = c.Model?.Find(id);
            if (n == null || !n.InModel) throw Bad(c, "node " + id + " is not in the scene");
        }

        // Node references and route points inside an object value.
        static void CheckDocRefs(Context c, DocObject d)
        {
            switch (d)
            {
                case SceneSettings s: CheckDocRefs(c, s.Effects); break;
                case ScreenEffects e: CheckDocRefs(c, e.DepthOfField); CheckDocRefs(c, e.SunShafts); break;
                case DepthOfFieldEffect f: if (f.Focus != null) CheckNodeRef(c, f.Focus); break;
                case SunShaftsEffect s: if (s.Caster != null) CheckNodeRef(c, s.Caster); break;
            }
        }

        /// <summary>Route point list rules: at least one point, every id valid and unique (<paramref name="skip"/> = an index whose id is replaced).</summary>
        public static void CheckRoutePoints(Context c, IList points, int skip)
        {
            if (points.Count == 0) throw Bad(c, "a route keeps at least its start point");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RoutePoint p in points)
            {
                if (p?.Id == null || !JsonText.IsId(p.Id)) throw Bad(c, "route point without a valid id");
                if (!ids.Add(p.Id)) throw Bad(c, "duplicate route point id " + p.Id);
            }
        }

        /// <summary>Accessory list rules: every element has a valid id, unique in the list plus <paramref name="others"/>
        /// (the ids already in a list an element is inserted into).</summary>
        public static void CheckAccessoryIds(Context c, IList entries, IList others)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (others != null)
                foreach (AccessoryEntry a in others) if (a?.Id != null) ids.Add(a.Id);
            foreach (AccessoryEntry a in entries)
            {
                if (a?.Id == null || !JsonText.IsId(a.Id)) throw Bad(c, "accessory without a valid id");
                if (!ids.Add(a.Id)) throw Bad(c, "duplicate accessory id " + a.Id);
            }
        }

        static string Name(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            var a = t.GetGenericArguments();
            return t.Name.Substring(0, t.Name.IndexOf('`')) + "<" + string.Join(",", Array.ConvertAll(a, Name)) + ">";
        }

        // ---------------- copies, equality, size ----------------

        /// <summary>A deep copy of a model value (no validation): structs and strings as they are, collections rebuilt,
        /// document objects cloned.</summary>
        public static object Copy(object v)
        {
            switch (v)
            {
                case null: return null;
                case string _: return v;
                case DocObject d: return DocClone.Clone(d, out _, null);
                case Array a:
                {
                    var r = Array.CreateInstance(a.GetType().GetElementType(), a.Length);
                    for (int i = 0; i < a.Length; i++) r.SetValue(Copy(a.GetValue(i)), i);
                    return r;
                }
                case IDictionary m:
                {
                    var r = (IDictionary)Activator.CreateInstance(v.GetType(), StringComparer.Ordinal);
                    foreach (DictionaryEntry kv in m) r[kv.Key] = Copy(kv.Value);
                    return r;
                }
                case IList l:
                {
                    var r = (IList)Activator.CreateInstance(v.GetType());
                    foreach (var e in l) r.Add(Copy(e));
                    return r;
                }
                default: return v;   // value types, Absent
            }
        }

        static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f == 0f ? 0f : f), 0);

        /// <summary>Equality of two values as the file would see it: floats bitwise after −0 → +0, collections element-wise,
        /// document objects by canonical bytes.</summary>
        public static bool Same(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || FieldValue.IsAbsent(a) || FieldValue.IsAbsent(b)) return false;
            switch (a)
            {
                case float fa: return b is float fb && Bits(fa) == Bits(fb);
                case Vec3 v3: return b is Vec3 w3 && Bits(v3.X) == Bits(w3.X) && Bits(v3.Y) == Bits(w3.Y) && Bits(v3.Z) == Bits(w3.Z);
                case Vec4 v4: return b is Vec4 w4 && Bits(v4.X) == Bits(w4.X) && Bits(v4.Y) == Bits(w4.Y) && Bits(v4.Z) == Bits(w4.Z) && Bits(v4.W) == Bits(w4.W);
                case Vec2 v2: return b is Vec2 w2 && Bits(v2.X) == Bits(w2.X) && Bits(v2.Y) == Bits(w2.Y);
                case Color4 c4: return b is Color4 d4 && Bits(c4.R) == Bits(d4.R) && Bits(c4.G) == Bits(d4.G) && Bits(c4.B) == Bits(d4.B) && Bits(c4.A) == Bits(d4.A);
                case Quat4 q4: return b is Quat4 r4 && Bits(q4.X) == Bits(r4.X) && Bits(q4.Y) == Bits(r4.Y) && Bits(q4.Z) == Bits(r4.Z) && Bits(q4.W) == Bits(r4.W);
                case string s: return b is string t && string.Equals(s, t, StringComparison.Ordinal);
                case DocObject d:
                {
                    if (!(b is DocObject e) || d.GetType() != e.GetType()) return false;
                    return SameBytes(DocClone.Bytes(d), DocClone.Bytes(e));
                }
                case IDictionary m:
                {
                    if (!(b is IDictionary n) || m.Count != n.Count) return false;
                    foreach (DictionaryEntry kv in m)
                        if (!n.Contains(kv.Key) || !Same(kv.Value, n[kv.Key])) return false;
                    return true;
                }
                case IList l:
                {
                    if (!(b is IList k) || l.Count != k.Count || a.GetType() != b.GetType()) return false;
                    for (int i = 0; i < l.Count; i++) if (!Same(l[i], k[i])) return false;
                    return true;
                }
                default: return a.Equals(b);
            }
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>History size of a value: strings 2 B/char, collections 4 B/element (plus nested), objects their canonical bytes.</summary>
        public static long SizeOf(object v)
        {
            switch (v)
            {
                case null: return 0;
                case string s: return 2L * s.Length;
                case DocObject d: return DocClone.Bytes(d).Length;
                case IDictionary m:
                {
                    long n = 0;
                    foreach (DictionaryEntry kv in m) n += 4 + 2L * ((string)kv.Key).Length + SizeOf(kv.Value);
                    return n;
                }
                case IEnumerable e:
                {
                    long n = 0;
                    foreach (var x in e) n += x is DocObject || x is string || x is IEnumerable ? 4 + SizeOf(x) : 4;
                    return n;
                }
                default: return 16;
            }
        }
    }
}
