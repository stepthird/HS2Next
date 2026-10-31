// D2 tests: random-document generator (deterministic, seeded xorshift; no System.Random so the sequence does not
// depend on the runtime), a reflection-based deep comparer for the test side (float bits, −0 → +0 only), and
// test T2: write → read → write gives identical bytes and every value equal bit
// for bit. Floats come from random bit patterns (NaN/Inf excluded), strings mix ASCII, quotes, backslashes,
// control characters and non-ASCII (BMP and surrogate pairs), arrays and node trees have random sizes. With
// Extras the documents also carry unknown members, unknown enum strings, unknown ME types, opaque nodes and
// inline ext blocks (what a newer writer may produce).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    /// <summary>Seeded generator of valid, canonical documents (canonical = what the reader keeps after normalising).</summary>
    public sealed class DocFuzz
    {
        ulong state;
        /// <summary>Every nullable member set and every list non-empty (for coverage checks).</summary>
        public bool Full;
        /// <summary>Unknown members, unknown enum strings, unknown ME types, opaque nodes, inline ext.</summary>
        public bool Extras = true;
        int unknownCounter;

        public DocFuzz(ulong seed, bool extras = true, bool full = false)
        {
            state = seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
            if (state == 0) state = 1;
            Extras = extras;
            Full = full;
            for (int i = 0; i < 4; i++) Next();
        }

        // ---------------- primitives ----------------

        public ulong Next()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }

        public int Int(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);
        public int Range(int lo, int hi) => lo + Int(hi - lo + 1);
        public bool Chance(double p) => (Next() >> 11) * (1.0 / (1UL << 53)) < p;
        public bool Bool() => (Next() & 1) == 1;
        public int AnyInt() => (int)(uint)Next();
        T Pick<T>(IList<T> items) => items[Int(items.Count)];

        static readonly float[] NiceFloats = { 0f, 1f, 0.5f, -1f, 2f, 0.1f, 0.666f, 0.8333f, 1e-7f, 3.4028235E+38f, -3.4028235E+38f, 1.401298E-45f, 16777216f, 8.513037E+08f };

        /// <summary>A finite float: mostly random bit patterns, sometimes a "nice" value, sometimes -0.</summary>
        public float F32()
        {
            if (Chance(0.02)) return -0f;
            if (Chance(0.15)) return Pick(NiceFloats);
            while (true)
            {
                var b = BitConverter.GetBytes((uint)Next());
                float f = BitConverter.ToSingle(b, 0);
                if (!float.IsNaN(f) && !float.IsInfinity(f)) return f;
            }
        }

        public float? F32N() => Nullable() ? (float?)null : F32();
        public int? IntN() => Nullable() ? (int?)null : AnyInt();
        public bool? BoolN() => Nullable() ? (bool?)null : Bool();
        bool Nullable() => !Full && Chance(0.3);

        public Vec2 V2() => new Vec2(F32(), F32());
        public Vec3 V3() => new Vec3(F32(), F32(), F32());
        public Vec3? V3N() => Nullable() ? (Vec3?)null : V3();
        public Vec4 V4() => new Vec4(F32(), F32(), F32(), F32());
        public Color4 Col() => new Color4(F32(), F32(), F32(), F32());
        public Quat4 Quat() => new Quat4(F32(), F32(), F32(), F32());

        public float[] F32s(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = F32(); return a; }
        public int[] Ints(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = AnyInt(); return a; }
        public bool[] Bools(int n) { var a = new bool[n]; for (int i = 0; i < n; i++) a[i] = Bool(); return a; }

        static readonly string[] Pieces =
        {
            "a", "Z", "0", " ", "_", "-", ".", "#", "\"", "\\", "/", "</", "\u0000", "\u0001", "\u001f", "\t", "\n", "\r", "\u007f",
            "\u00E9", "\u00DF", "\u65E5\u672C\u8A9E", "\u9AEA", "\u2028", "\u3000", "\uFEFF", "\uD83D\uDE00", "\uD842\uDFB7", "N_Hair_pony", "illusion.hs2", "{", "}", "[", ",",
        };

        /// <summary>A random string (possibly empty) of ASCII, quotes, backslashes, control and non-ASCII characters.</summary>
        public string Str(int maxPieces = 6)
        {
            int n = Int(maxPieces + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(Chance(0.3) ? ((char)Range(0x20, 0x7e)).ToString() : Pick(Pieces));
            return sb.ToString();
        }

        public string StrN() => Nullable() ? null : Str();
        string NonEmpty() { string s = Str(); return s.Length == 0 ? "x" : s; }

        public string Hex(int digits)
        {
            const string h = "0123456789abcdef";
            var c = new char[digits];
            for (int i = 0; i < digits; i++) c[i] = h[Int(16)];
            return new string(c);
        }

        public string Id() => Hex(32);
        public string IdN() => Nullable() ? null : Id();
        public string Res() => "res/" + Hex(64) + "." + Pick(new[] { "png", "jpg", "bin" });
        public string ResN() => Nullable() ? null : Res();

        /// <summary>A `ref`: any non-empty GUID text (spaces, brackets, quotes, '#') + '#' + an int32.</summary>
        public string Ref()
        {
            if (Chance(0.3)) return "illusion.hs2#" + Range(-1, 400).ToString(CultureInfo.InvariantCulture);
            return NonEmpty() + "#" + AnyInt().ToString(CultureInfo.InvariantCulture);
        }

        public string RefN() => Nullable() ? null : Ref();

        public List<T> List<T>(Func<T> make, int max = 3)
        {
            int n = Full ? Range(1, Math.Max(1, max)) : Int(max + 1);
            var l = new List<T>(n);
            for (int i = 0; i < n; i++) l.Add(make());
            return l;
        }

        T MaybeObj<T>(Func<T> make) where T : class => Nullable() ? null : make();

        // ---------------- unknown data ----------------

        /// <summary>A random JSON value (numbers are float32 values as doubles or int64; depth-limited).</summary>
        public JToken Token(int depth = 0)
        {
            int k = Int(depth >= 2 ? 5 : 7);
            switch (k)
            {
                case 0: return JValue.CreateNull();
                case 1: return new JValue(Bool());
                case 2: return new JValue((long)Next());
                case 3: return new JValue((double)F32());
                case 4: return new JValue(Str());
                case 5:
                {
                    var o = new JObject();
                    int n = Int(4);
                    for (int i = 0; i < n; i++) o["m" + i + "_" + Hex(3)] = Token(depth + 1);
                    return o;
                }
                default:
                {
                    var a = new JArray();
                    int n = Int(4);
                    for (int i = 0; i < n; i++) a.Add(Token(depth + 1));
                    return a;
                }
            }
        }

        /// <summary>Gives <paramref name="o"/> unknown members now and then (names that no schema uses).</summary>
        public T Decorate<T>(T o) where T : DocObject
        {
            if (Extras && Chance(0.08))
            {
                var l = new JsonLeftovers();
                int n = Range(1, 2);
                for (int i = 0; i < n; i++) l.Add("future_" + (unknownCounter++).ToString(CultureInfo.InvariantCulture), Token());
                o.Unknown = l;
            }
            return o;
        }

        /// <summary>Inline ext with 0..2 namespaces (only with Extras).</summary>
        public ExtInline Ext()
        {
            var e = new ExtInline();
            if (!Extras || !Chance(Full ? 1.0 : 0.2)) return e;
            int n = Range(1, 2);
            for (int i = 0; i < n; i++)
            {
                string ns = "p" + Hex(2) + "." + Pick(new[] { "plugin", "x_y", "a-b", "Z9" }) + i;
                e.Set(ns, Token());
            }
            return e;
        }

        string EnumOrUnknown(IList<string> names) => Extras && Chance(0.05) ? "future" + Hex(4) : Pick(names);

        // ---------------- chara ----------------

        public CharaDocument Chara()
        {
            var d = Decorate(new CharaDocument());
            d.Profile = Decorate(new Profile { Name = Str(), Sex = Int(2), Futanari = Bool() });
            if (!Nullable())
            {
                var h = Decorate(new Hs2Profile { Personality = AnyInt(), BirthMonth = AnyInt(), BirthDay = AnyInt(), VoiceRate = F32() });
                var wishes = List(AnyInt, 5);
                wishes.Sort();
                h.Wishes = wishes;
                h.Parameter2 = MaybeObj(() => Decorate(new Hs2Parameter2 { Personality = AnyInt(), VoiceRate = F32(), Trait = AnyInt(), Mind = AnyInt(), HAttribute = AnyInt() }));
                d.Profile.Hs2 = h;
            }
            Face(d.Face);
            Body(d.Body);
            Hair(d.Hair);
            d.MaterialOverrides = MeList();
            d.StudioOverlays = Decorate(new StudioOverlaySwitches { Skin = Bool(), Eyes = Bool(), Clothes = Bool() });
            d.Ext = Ext();
            return d;
        }

        void Fill(ItemPower x) { Decorate(x); x.Item = Ref(); x.Power = F32(); }
        void Fill(ItemColor x) { Decorate(x); x.Item = Ref(); x.Color = Col(); }
        void Fill(ItemLayout x) { Decorate(x); x.Item = Ref(); x.Color = Col(); x.Layout = V4(); if (x.HasTilt) x.Tilt = F32(); }
        void Fill(MakeupLayer x) { Decorate(x); x.Item = Ref(); x.Color = Col(); x.Gloss = F32(); }

        Paint NewPaint() => Decorate(new Paint { Item = Ref(), Color = Col(), Gloss = F32(), Metallic = F32(), LayoutItem = Ref(), Layout = V4(), Rotation = F32() });

        void Face(FaceData f)
        {
            Decorate(f);
            f.Head = Ref();
            f.Skin = Ref();
            Fill(f.Detail);
            f.Shape = F32s(Full ? FaceData.ShapeCount : Int(70));
            Fill(f.Eyebrow);
            var e = Decorate(f.Eyes);
            e.Linked = Bool();
            e.OffsetY = F32();
            e.ShadowScale = F32();
            foreach (var eye in new[] { e.Left, e.Right })
            {
                Decorate(eye);
                eye.White = Col();
                eye.Iris = Decorate(new EyeIris { Item = Ref(), Color = Col(), Size = V2(), Emission = F32() });
                eye.Pupil = Decorate(new EyePupil { Item = Ref(), Color = Col(), Size = V2() });
            }
            Fill(e.Highlight);
            Fill(f.Eyelashes);
            Fill(f.Mole);
            var m = Decorate(f.Makeup);
            Fill(m.Eyeshadow);
            Fill(m.Cheek);
            Fill(m.Lip);
            m.Paint = new[] { NewPaint(), NewPaint() };
            Fill(f.Beard);
        }

        void Body(BodyData b)
        {
            Decorate(b);
            b.Shape = F32s(Full ? BodyData.ShapeCount : Int(40));
            b.Bust = Decorate(new BodyBust { Softness = F32(), Weight = F32() });
            b.Skin = Decorate(new BodySkin { Item = Ref(), Color = Col(), Gloss = F32(), Metallic = F32() });
            Fill(b.Detail);
            Fill(b.Sunburn);
            b.Paint = new[] { NewPaint(), NewPaint() };
            b.Nipple = Decorate(new BodyNipple { Item = Ref(), Color = Col(), Gloss = F32(), Areola = F32() });
            Fill(b.Underhair);
            b.Nail = Decorate(new BodyNail { Color = Col(), Gloss = F32() });
            b.Visible = Bool();
            b.Uncensor = MaybeObj(() => Decorate(new UncensorData { Body = StrN(), Penis = StrN(), Balls = StrN(), DisplayBalls = BoolN(), Legacy = StrN() }));
            // One overlay per target, in TexType value order; unknown names after them (ordinal).
            var targets = (SkinOverlayTarget[])Enum.GetValues(typeof(SkinOverlayTarget));
            b.SkinOverlays = new List<SkinOverlay>();
            foreach (var t in targets)
                if (Full || Chance(0.2)) b.SkinOverlays.Add(Decorate(new SkinOverlay { Target = t.ToString(), Image = Res() }));
            if (Extras && Chance(0.1)) b.SkinOverlays.Add(Decorate(new SkinOverlay { Target = "future" + Hex(4), Image = Res() }));
            b.BoneMods = List(() =>
            {
                var x = Decorate(new BodyBoneMod { Bone = Str(), Location = EnumOrUnknown(new[] { "unknown", "bodyTop" }) });
                FillBoneMod(x);
                return x;
            });
        }

        void FillBoneMod(BoneModBase x)
        {
            x.Scale = V3();
            x.Length = F32();
            x.Position = V3();
            x.Rotation = V3();
        }

        void Hair(HairData h)
        {
            Decorate(h);
            h.ShaderType = AnyInt();
            h.Editor = Decorate(new HairEditorSwitches { SameSetting = Bool(), AutoSetting = Bool(), CtrlTogether = Bool() });
            for (int k = 0; k < 4; k++)
            {
                var p = Decorate(new HairPart
                {
                    Item = Ref(), BaseColor = Col(), TopColor = Col(), UnderColor = Col(), Specular = Col(),
                    Metallic = F32(), Smoothness = F32(), AccessoryColors = new[] { Col(), Col(), Col(), Col() },
                });
                int index = Range(-2, 3);
                p.Bundles = List(() => Decorate(new HairBundle { Index = index += Range(1, 3), Move = V3(), Rotate = V3(), NoShake = Bool() }), 4);
                p.Mesh = Decorate(new HairMesh { Item = Ref(), Color = Col(), Layout = V4() });
                p.MaterialOverrides = Chance(0.3) || Full ? MeList() : new List<MaterialOverride>();
                h.Parts[k] = p;
            }
        }

        // ---------------- MaterialEditor ----------------

        public List<MaterialOverride> MeList()
        {
            var l = new List<MaterialOverride>();
            if (Full)
            {
                foreach (var t in MaterialOverride.TypeOrder) l.Add(Me(t));
                if (Extras) l.Add(MeUnknownItem());
                return l;
            }
            int n = Int(5);
            for (int i = 0; i < n; i++)
                l.Add(Extras && Chance(0.05) ? MeUnknownItem() : Me(Pick(MaterialOverride.TypeOrder)));
            return l;
        }

        MaterialOverride MeUnknownItem()
        {
            var raw = new JObject { ["type"] = "future" + Hex(4) };
            int n = Int(4);
            for (int i = 0; i < n; i++) raw["m" + i] = Token();
            return new MeUnknown { TypeName = (string)raw["type"], Raw = raw };
        }

        MaterialOverride Me(string type)
        {
            switch (type)
            {
                case "copy": return Decorate(new MeCopy { Material = Str(), CopyName = Str() });
                case "name": return Decorate(new MeName { Renderer = Str(), Material = Str(), Value = Str(), Original = StrN() });
                case "shader": return Decorate(new MeShader { Material = Str(), Shader = StrN(), ShaderOriginal = StrN(), RenderQueue = IntN(), RenderQueueOriginal = IntN() });
                case "renderer":
                {
                    var x = Decorate(new MeRenderer { Renderer = Str(), Property = EnumOrUnknown(Names<MeRendererProperty>()) });
                    if (Chance(0.2)) x.ValueText = Str(); else x.Value = AnyInt();
                    if (Chance(0.2)) x.OriginalText = Str(); else x.Original = IntN();
                    return x;
                }
                case "float":
                {
                    var x = Decorate(new MeFloat { Material = Str(), Property = Str() });
                    if (Chance(0.2)) x.ValueText = Str(); else x.Value = F32();
                    if (Chance(0.2)) x.OriginalText = Str(); else x.Original = F32N();
                    return x;
                }
                case "keyword": return Decorate(new MeKeyword { Material = Str(), Property = Str(), Value = Bool(), Original = Bool() });
                case "color": return Decorate(new MeColor { Material = Str(), Property = Str(), Value = Col(), Original = Nullable() ? (Color4?)null : Col() });
                case "vector": return Decorate(new MeVector { Material = Str(), Property = Str(), Value = V4(), Original = Nullable() ? (Vec4?)null : V4() });
                case "texture":
                    return Decorate(new MeTexture
                    {
                        Material = Str(), Property = Str(), Image = ResN(),
                        Offset = V2N(), OffsetOriginal = V2N(), Scale = V2N(), ScaleOriginal = V2N(),
                    });
                case "cubemap": return Decorate(new MeCubemap { Material = Str(), Property = Str(), Image = ResN() });
                case "projector": return Decorate(new MeProjector { Projector = Str(), Property = EnumOrUnknown(Names<MeProjectorProperty>()), Value = Str(), Original = StrN() });
                default: throw new ArgumentException(type);
            }
        }

        Vec2? V2N() => Nullable() ? (Vec2?)null : V2();

        static string[] Names<T>() where T : struct => Enum.GetNames(typeof(T));

        // ---------------- coord ----------------

        public CoordDocument Coord()
        {
            var d = Decorate(new CoordDocument { Name = Str(), Sex = AnyInt() });
            Decorate(d.Clothes);
            for (int k = 0; k < ClothesSet.Count; k++)
            {
                var p = Decorate(new ClothesPart((ClothesKind)k) { Item = Ref(), BreakRate = F32(), HideOptions = Bools(2) });
                for (int j = 0; j < 3; j++)
                    p.Colors[j] = Decorate(new ClothesColor { Base = Col(), Pattern = Ref(), PatternColor = Col(), Layout = V4(), Rotation = F32(), Gloss = F32(), Metallic = F32() });
                p.Overlays = MaybeObj(Overlays);
                p.MaterialOverrides = Chance(0.3) || Full ? MeList() : new List<MaterialOverride>();
                p.PushUp = MaybeObj(() => Decorate(new PushUpSettings
                {
                    Enabled = Bool(), Firmness = F32(), Lift = F32(), PushTogether = F32(), Squeeze = F32(), CenterNipples = F32(), Corset = F32(),
                    FlattenNipples = Bool(), HideNipples = Bool(), HideAccessories = Bool(), CorsetHalf = Bool(),
                }));
                d.Clothes.Parts[k] = p;
            }
            var ids = new List<string>();
            d.Accessories = List(() => Accessory(ids), 4);
            d.RuleOverrides = MaybeObj(() => Decorate(new AacRuleOverrides { Suppressed = List(AacOverrideItem), Overrides = List(AacOverrideItem) }));
            d.Ext = Ext();
            return d;
        }

        ClothesOverlaySet Overlays()
        {
            var o = Decorate(new ClothesOverlaySet { Main = Ov(), ColorMask = Ov(), SourceReplacement = Ov(), TextureSize = IntN() });
            for (int i = 0; i < 3; i++) o.Patterns[i] = Ov();
            return o;
        }

        OverlayEntry Ov()
        {
            if (Nullable()) return null;
            var e = Decorate(new OverlayEntry { Image = ResN(), ClearTarget = Bool(), Blend = AnyInt() });
            if (e.Image == null) e.ClearTarget = true;   // an entry with both empty is never written
            return e;
        }

        AccessoryEntry Accessory(List<string> ids)
        {
            var a = Decorate(new AccessoryEntry
            {
                Id = Id(), Category = AnyInt(), Item = Ref(), Parent = Str(), HideCategory = AnyInt(), HideTiming = AnyInt(), NoShake = Bool(), ImportSlot = IntN(),
            });
            ids.Add(a.Id);
            for (int i = 0; i < 2; i++) a.Moves[i] = Decorate(new AccessoryMove { Position = V3(), Rotation = V3(), Scale = V3() });
            for (int i = 0; i < 4; i++) a.Colors[i] = Decorate(new AccessoryColor { Color = Col(), Gloss = F32(), Metallic = F32(), Smoothness = F32() });
            a.BoneMods = List(() => { var x = Decorate(new AccessoryBoneMod { Bone = Str() }); FillBoneMod(x); return x; });
            a.MaterialOverrides = Chance(0.3) || Full ? MeList() : new List<MaterialOverride>();
            a.DynamicBones = List(() =>
            {
                var x = Decorate(new DynamicBoneOverride { Root = Str() });
                FillDbe(x);
                x.Original = Decorate(new DynamicBoneValues());
                FillDbe(x.Original);
                return x;
            });
            a.Rules = MaybeObj(() => Decorate(new AccessoryRules
            {
                Visibility = List(() => Decorate(new AacRule { Rule = Str(), Modifier = StrN(), Link = ids.Count > 0 && Bool() ? Pick(ids) : IdN() })),
                CharacterAccessory = Bool(), MatchHairColor = Bool(), AdvancedParent = StrN(), Name = StrN(),
            }));
            a.Ext = Ext();
            return a;
        }

        void FillDbe(DynamicBoneValues x)
        {
            x.FreezeAxis = IntN();
            x.Weight = F32N();
            x.Damping = F32N();
            x.Elasticity = F32N();
            x.Stiffness = F32N();
            x.Inertia = F32N();
            x.Radius = F32N();
        }

        AacOverride AacOverrideItem() => Decorate(new AacOverride { Rule = Str(), Modifier = StrN(), OverrideRule = Str(), OverrideModifier = StrN() });

        // ---------------- scene ----------------

        static readonly string[] Kinds = { SceneNode.KindChara, SceneNode.KindItem, SceneNode.KindLight, SceneNode.KindFolder, SceneNode.KindRoute, SceneNode.KindCamera };

        public SceneDocument Scene(int maxNodes = 10)
        {
            var d = Decorate(new SceneDocument());
            int n = Full ? Kinds.Length + (Extras ? 1 : 0) : Int(maxNodes + 1);
            var nodes = new List<SceneNode>();
            for (int i = 0; i < n; i++)
            {
                string kind = Full ? (i < Kinds.Length ? Kinds[i] : null) : (Extras && Chance(0.05) ? null : Pick(Kinds));
                var node = Decorate(new SceneNode { Id = Id() });
                node.State = kind == null ? new OpaqueNode { KindName = "future" + Hex(4) } : NodeSection(kind);
                // Parent: a previous node that takes children, or the root.
                var candidates = nodes.FindAll(p => p.State.TakesChildren);
                if (candidates.Count > 0 && (Full ? i > 0 && Chance(0.7) : Chance(0.6)))
                {
                    var p = Pick(candidates);
                    string point = null;
                    if (p.State is CharaNodeState)
                        point = Extras && Chance(0.1) ? "N_Future" + Hex(2) : Pick(DocEnum.AttachPoints);
                    else if (Chance(0.05)) point = Str();
                    node.Parent = Decorate(new NodeParent(p.Id, point));
                }
                node.Order = Range(-3, 5);
                if (!(node.State is OpaqueNode))
                {
                    node.Name = StrN();
                    node.Transform = Decorate(new NodeTransform { Pos = V3(), Rot = V3(), Scale = V3(), InheritScale = Bool() });
                    node.Visible = Bool();
                    node.Ui = Decorate(new NodeUi { Expanded = Bool() });
                    if (node.State is CharaNodeState)
                    {
                        var g = new SortedSet<int>(List(() => Range(0, 9), 4));
                        node.Ui.ExpandedGroups = new List<int>(g);
                        var pts = new List<string>(new SortedSet<string>(List(() => Pick(DocEnum.AttachPoints), 3), StringComparer.Ordinal));
                        node.Ui.ExpandedPoints = pts;
                    }
                }
                node.Ext = Ext();
                nodes.Add(node);
            }
            d.Nodes = SceneDocument.CanonicalOrder(nodes, out var order);
            foreach (var node in d.Nodes)
            {
                node.Order = order[node];
                if (node.State is OpaqueNode o)
                {
                    node.Unknown = null;   // the raw object holds every member of an opaque node
                    o.Raw = OpaqueRaw(node, o.KindName);
                }
            }
            d.Scene = SceneSettingsObj();
            d.Ext = Ext();
            return d;
        }

        JObject OpaqueRaw(SceneNode n, string kind)
        {
            var raw = new JObject { ["id"] = n.Id, ["kind"] = kind };
            if (Bool()) raw["name"] = Str();
            raw["parent"] = n.Parent == null ? (JToken)JValue.CreateNull() : new JObject { ["node"] = n.Parent.Node, ["point"] = n.Parent.Point == null ? JValue.CreateNull() : new JValue(n.Parent.Point) };
            raw["order"] = n.Order;
            if (n.Parent != null) n.Parent.Unknown = null;   // the raw parent object has no unknown members
            int m = Range(0, 3);
            for (int i = 0; i < m; i++) raw["futureMember" + i] = Token();
            raw[kind] = Token();
            return raw;
        }

        NodeState NodeSection(string kind)
        {
            switch (kind)
            {
                case SceneNode.KindChara: return CharaSection();
                case SceneNode.KindItem: return ItemSection();
                case SceneNode.KindLight:
                    return Decorate(new LightNodeState
                    {
                        Ref = Ref(), Color = Col(), Intensity = F32(), Range = F32(), SpotAngle = F32(), Shadow = Bool(), Enabled = Bool(), ShowTarget = Bool(),
                    });
                case SceneNode.KindFolder: return Decorate(new FolderNodeState());
                case SceneNode.KindRoute:
                {
                    var r = Decorate(new RouteNodeState
                    {
                        Playing = Bool(), Loop = Bool(), ShowLine = Bool(), Orient = EnumOrUnknown(new[] { "none", "xy", "y" }), Color = Col(),
                    });
                    int n = Range(1, Full ? 3 : 4);
                    for (int i = 0; i < n; i++)
                        r.Points.Add(Decorate(new RoutePoint
                        {
                            Id = Id(), Pos = V3(), Rot = V3(), Speed = F32(), Ease = EnumOrUnknown(Names<RouteEase>()),
                            Connection = EnumOrUnknown(new[] { "line", "curve" }), Link = Bool(),
                            Aid = MaybeObj(() => Decorate(new RouteAid { Pos = V3(), Init = Bool() })),
                        }));
                    return r;
                }
                case SceneNode.KindCamera: return Decorate(new CameraNodeState { Active = Bool(), Fov = F32N() });
                default: throw new ArgumentException(kind);
            }
        }

        CharaNodeState CharaSection()
        {
            var c = Decorate(new CharaNodeState());
            c.Anime = Decorate(new CharaAnimeState
            {
                Ref = MaybeObj(AnimeRefObj), Speed = F32(), Pattern = F32(), Time = F32(), ForceLoop = Bool(), OptionVisible = Bool(), Params = F32s(2),
            });
            c.Hands = Ints(2);
            c.Face = Decorate(new CharaFaceState
            {
                Eyebrow = AnyInt(), Eyes = AnyInt(), EyesOpen = F32(), Blink = Bool(), Mouth = AnyInt(), MouthOpen = F32(), LipSync = Bool(),
                Tears = F32(), Cheek = F32(), EyebrowOpenMax = F32(), MouthOpenMin = F32(), MouthOpenMax = F32(),
                MouthFixed = Bool(), MouthAdjustWidth = Bool(), HideHighlight = Bool(),
            });
            c.Look = Decorate(new CharaLookState { Eyes = AnyInt(), Neck = AnyInt(), Target = V3() });
            foreach (var k in List(() => Str(), 3)) c.Look.NeckFix[k] = Quat();
            c.Look.EyeRotations = MaybeObj(() => Decorate(new EyeRotations { Left = Quat(), Right = Quat() }));
            c.Fk = Decorate(new CharaFkState { Enabled = Bool(), HipsOffset = F32(), HipsPosition = V3N() });
            c.Fk.Groups = Decorate(new FkGroups { Hair = Bool(), Neck = Bool(), Breast = Bool(), Body = Bool(), RightHand = Bool(), LeftHand = Bool(), Skirt = Bool() });
            foreach (var k in List(() => Str(), 4)) c.Fk.Bones[k] = V3();
            c.Ik = Decorate(new CharaIkState { Enabled = Bool() });
            c.Ik.Groups = Decorate(new IkGroups { Body = Bool(), RightLeg = Bool(), LeftLeg = Bool(), RightArm = Bool(), LeftArm = Bool() });
            c.Ik.Targets = Decorate(new IkTargets());
            for (int i = 0; i < IkTargets.Count; i++)
                c.Ik.Targets[i] = Full || Chance(0.7) ? Decorate(new IkTarget { Pos = V3(), Rot = V3() }) : null;
            c.Correction = Bools(8);
            c.State = Decorate(new CharaState { Clothes = Ints(8), Liquid = Ints(5), Nipple = F32(), Wet = F32(), Gloss = F32() });
            foreach (var k in List(Id, 4)) c.State.Accessories[k] = Bool();
            c.State.Son = Decorate(new CharaSonState { Visible = Bool(), Length = F32() });
            c.State.Silhouette = Decorate(new CharaSilhouette { Enabled = Bool(), Color = Col() });
            c.Voice = Decorate(new CharaVoiceState { List = List(AnimeRefObj), Repeat = EnumOrUnknown(new[] { "none", "all", "select" }) });
            return c;
        }

        AnimeRef AnimeRefObj() => Decorate(new AnimeRef(Str(), AnyInt(), AnyInt(), AnyInt()));

        ItemNodeState ItemSection()
        {
            var it = Decorate(new ItemNodeState { Ref = Ref(), Group = AnyInt(), Category = AnyInt(), Alpha = F32(), LightCancel = F32(), DynamicBones = Bool() });
            it.Anime = Decorate(new ItemAnime { Pattern = AnyInt(), Speed = F32(), Time = F32() });
            for (int i = 0; i < 4; i++)
                it.Colors[i] = Decorate(new ItemColorSlot
                {
                    Main = Col(), Metallic = F32(), Glossiness = F32(),
                    Pattern = Decorate(new ItemPattern { Item = RefN(), File = MaybeObj(FileRefObj), Color = Col(), Clamp = Bool(), Uv = V4(), Rot = F32() }),
                });
            it.Emission = Decorate(new ItemEmission { Color = Col(), Intensity = F32(), Power = F32() });
            it.Panel = MaybeObj(FileRefObj);
            it.Fk = Decorate(new ItemFk { Enabled = Bool() });
            foreach (var k in List(() => Str(), 3)) it.Fk.Bones[k] = V3();
            it.Options = List(Bool, 5);
            return it;
        }

        FileRef FileRefObj()
        {
            int k = Int(Extras ? 4 : 3);
            if (Full) k = Int(3);
            switch (k)
            {
                case 0: return Decorate(FileRef.FromRes(Res()));
                case 1: return Decorate(FileRef.FromUser(Str()));
                case 2: return Decorate(FileRef.FromHs2(Str()));
                default:
                    var f = new FileRef { Kind = FileRef.Kinds.None };
                    f.Unknown = new JsonLeftovers();
                    f.Unknown.Add("url", new JValue(Str()));
                    return f;
            }
        }

        SceneSettings SceneSettingsObj()
        {
            var s = Decorate(new SceneSettings());
            s.Map = Decorate(new MapSettings { Ref = RefN(), Pos = V3(), Rot = V3(), Options = Bool(), Lights = Bool() });
            s.Effects = Effects();
            s.Camera = Decorate(new CameraState { Current = MaybeObj(CameraViewObj) });
            s.Camera.Slots = new List<CameraView>();
            int n = Full ? 10 : Int(12);
            for (int i = 0; i < n; i++) s.Camera.Slots.Add(Chance(Full ? 0.0 : 0.5) ? null : CameraViewObj());
            s.CharaLight = Decorate(new CharaLightSettings { Color = Col(), Intensity = F32(), Rot = F32s(2), Shadow = Bool() });
            s.Sound = Decorate(new SoundSettings());
            s.Sound.Bgm = Decorate(new BgmSettings { Ref = RefN(), Play = Bool(), Repeat = EnumOrUnknown(new[] { "none", "all" }) });
            s.Sound.UserAudio = Decorate(new UserAudioSettings { File = MaybeObj(FileRefObj), Play = Bool(), Repeat = EnumOrUnknown(new[] { "none", "all" }) });
            s.Background = MaybeObj(FileRefObj);
            s.Frame = MaybeObj(FileRefObj);
            return s;
        }

        CameraView CameraViewObj() => Decorate(new CameraView { Pivot = V3(), Rot = V3(), Distance = F32(), Fov = F32() });

        ScreenEffects Effects()
        {
            var e = Decorate(new ScreenEffects());
            e.ColorGrading = Decorate(new ColorGradingEffect { Lut = RefN(), Blend = F32(), Saturation = AnyInt(), Brightness = AnyInt(), Contrast = AnyInt() });
            e.AmbientOcclusion = Decorate(new AmbientOcclusionEffect { Enabled = Bool(), Intensity = F32(), Thickness = F32(), Color = Col() });
            e.Bloom = Decorate(new BloomEffect { Enabled = Bool(), Intensity = F32(), Threshold = F32(), SoftKnee = F32(), Clamp = Bool(), Diffusion = F32(), Color = Col() });
            e.DepthOfField = Decorate(new DepthOfFieldEffect { Enabled = Bool(), Focus = IdN(), FocalSize = F32(), Aperture = F32() });
            e.Vignette = Decorate(new VignetteEffect { Enabled = Bool(), Intensity = F32() });
            e.Ssr = Decorate(new SsrEffect { Enabled = Bool() });
            e.ReflectionProbe = Decorate(new ReflectionProbeEffect { Enabled = Bool(), Cubemap = RefN(), Intensity = F32() });
            e.Fog = Decorate(new FogEffect { Enabled = Bool(), ExcludeFarPixels = Bool(), Height = F32(), HeightDensity = F32(), Color = Col(), Density = F32() });
            e.SunShafts = Decorate(new SunShaftsEffect
            {
                Enabled = Bool(), Caster = IdN(), ThresholdColor = Col(), Color = Col(), DistanceFalloff = F32(), BlurSize = F32(), Intensity = F32(),
            });
            e.Shadows = Decorate(new ShadowsEffect { Enabled = Bool() });
            e.Ambient = Decorate(new AmbientLightEffect { Sky = Col(), Equator = Col(), Ground = Col() });
            e.Sky = Decorate(new SkyEffect { Enabled = Bool(), Pattern = RefN() });
            return e;
        }
    }

    /// <summary>Deep comparison of document models over public fields (test side only): floats by bits after mapping −0 to
    /// +0 on the expected side, JSON tokens with DeepEquals, lists element by element. Returns the first difference.</summary>
    public static class DocCompare
    {
        public static string Diff(object expected, object actual, string path = "")
        {
            if (expected == null || actual == null)
                return expected == null && actual == null ? null : path + ": " + Show(expected) + " vs " + Show(actual);
            var t = expected.GetType();
            if (t != actual.GetType()) return path + ": type " + t.Name + " vs " + actual.GetType().Name;
            if (expected is float fe)
            {
                float fa = (float)actual;
                if (fe == 0f) fe = 0f;   // −0 → +0
                return Bits(fe) == Bits(fa) ? null : path + ": " + fe.ToString("R", CultureInfo.InvariantCulture) + " vs " + fa.ToString("R", CultureInfo.InvariantCulture) + " (bits " + Bits(fe).ToString("X8") + " vs " + Bits(fa).ToString("X8") + ")";
            }
            if (expected is string || t.IsPrimitive || t.IsEnum)
                return Equals(expected, actual) ? null : path + ": " + Show(expected) + " vs " + Show(actual);
            if (expected is JToken je)
                return JToken.DeepEquals(je, (JToken)actual) ? null : path + ": JSON " + je.ToString(Newtonsoft.Json.Formatting.None) + " vs " + ((JToken)actual).ToString(Newtonsoft.Json.Formatting.None);
            if (expected is JsonLeftovers le)
            {
                var la = (JsonLeftovers)actual;
                if (le.Count != la.Count) return path + ".<unknown>: " + le.Count + " vs " + la.Count + " members";
                for (int i = 0; i < le.Count; i++)
                {
                    if (le.Members[i].Key != la.Members[i].Key) return path + ".<unknown>[" + i + "]: name " + le.Members[i].Key + " vs " + la.Members[i].Key;
                    var d = Diff(le.Members[i].Value, la.Members[i].Value, path + "." + le.Members[i].Key);
                    if (d != null) return d;
                }
                return null;
            }
            if (expected is ExtInline ee)
            {
                var ea = (ExtInline)actual;
                var ke = new List<string>(ee.Namespaces);
                var ka = new List<string>(ea.Namespaces);
                if (string.Join(",", ke) != string.Join(",", ka)) return path + ".ext: namespaces " + string.Join(",", ke) + " vs " + string.Join(",", ka);
                foreach (var k in ke)
                {
                    var d = Diff(ee.Get(k), ea.Get(k), path + ".ext." + k);
                    if (d != null) return d;
                }
                return null;
            }
            if (expected is IDictionary de)
            {
                var da = (IDictionary)actual;
                if (de.Count != da.Count) return path + ": " + de.Count + " vs " + da.Count + " keys";
                foreach (DictionaryEntry kv in de)
                {
                    if (!da.Contains(kv.Key)) return path + ": key " + Show(kv.Key) + " missing";
                    var d = Diff(kv.Value, da[kv.Key], path + "[" + Show(kv.Key) + "]");
                    if (d != null) return d;
                }
                return null;
            }
            if (expected is IList ie)
            {
                var ia = (IList)actual;
                if (ie.Count != ia.Count) return path + ": " + ie.Count + " vs " + ia.Count + " elements";
                for (int i = 0; i < ie.Count; i++)
                {
                    var d = Diff(ie[i], ia[i], path + "[" + i + "]");
                    if (d != null) return d;
                }
                return null;
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var d = Diff(f.GetValue(expected), f.GetValue(actual), path + "." + f.Name);
                if (d != null) return d;
            }
            return null;
        }

        static int Bits(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
        static string Show(object o) => o == null ? "null" : o is string s ? "\"" + s + "\"" : o.ToString();
    }

    /// <summary>Document read/write helpers of the D2 tests.</summary>
    public static class DocIo
    {
        public static byte[] Bytes(CharaDocument d) => CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write);
        public static byte[] Bytes(CoordDocument d) => CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, d.Write);
        public static byte[] Bytes(SceneDocument d) => CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, d.Write);

        public static CharaDocument Chara(byte[] b, FindingList f = null, bool lenient = false) =>
            CharaDocument.Read(DocReader.ForDocument(b, new DocReadContext(CharaDocument.EntryName, lenient, f)));
        public static CoordDocument Coord(byte[] b, FindingList f = null, bool lenient = false) =>
            CoordDocument.Read(DocReader.ForDocument(b, new DocReadContext(CoordDocument.EntryName, lenient, f)));
        public static SceneDocument Scene(byte[] b, FindingList f = null, bool lenient = false) =>
            SceneDocument.Read(DocReader.ForDocument(b, new DocReadContext(SceneDocument.EntryName, lenient, f)));

        public static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);
        public static string Text(byte[] b) => new UTF8Encoding(false).GetString(b);

        public static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Line number and both lines of the first difference (for failure messages).</summary>
        public static string FirstDiff(byte[] a, byte[] b)
        {
            var la = Text(a).Split('\n');
            var lb = Text(b).Split('\n');
            for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
            {
                string x = i < la.Length ? la[i] : "<eof>", y = i < lb.Length ? lb[i] : "<eof>";
                if (x != y) return "line " + (i + 1) + ": [" + x + "] vs [" + y + "]";
            }
            return "identical";
        }
    }

    /// <summary>T2: fuzz round trip of chara, coord and scene documents.</summary>
    public static class DocTests_T2Fuzz
    {
        /// <summary>Seeds per run; the environment variable HS2N_DOC_FUZZ overrides it (longer local runs).</summary>
        public const int Iterations = 400;

        public static void Run(TestRunner t)
        {
            int seeds = int.TryParse(Environment.GetEnvironmentVariable("HS2N_DOC_FUZZ"), out int n) && n > 0 ? n : Iterations;
            int ok = 0, fail = 0;
            long bytes = 0;
            for (int i = 0; i < seeds; i++)
            {
                var g = new DocFuzz((ulong)(i + 1), extras: i % 4 != 0, full: i % 50 == 7);
                ok += Round(t, "chara#" + i, g.Chara(), DocIo.Bytes, b => DocIo.Chara(b), DocIo.Bytes, ref bytes) ? 1 : 0;
                ok += Round(t, "coord#" + i, g.Coord(), DocIo.Bytes, b => DocIo.Coord(b), DocIo.Bytes, ref bytes) ? 1 : 0;
                ok += Round(t, "scene#" + i, g.Scene(), DocIo.Bytes, b => DocIo.Scene(b), DocIo.Bytes, ref bytes) ? 1 : 0;
            }
            fail = 3 * seeds - ok;
            t.Info("T2: " + (3 * seeds) + " documents (" + seeds + " seeds x chara/coord/scene), " + (bytes / 1024) + " KiB written, " + fail + " failures");
        }

        static bool Round<T>(TestRunner t, string what, T doc, Func<T, byte[]> write, Func<byte[], T> read, Func<T, byte[]> write2, ref long bytes)
        {
            byte[] a, b;
            T back;
            try
            {
                a = write(doc);
                back = read(a);
                b = write2(back);
            }
            catch (Exception e)
            {
                t.Check(false, what + ": threw " + e.GetType().Name + ": " + e.Message);
                return false;
            }
            bytes += a.Length;
            if (!DocIo.Same(a, b)) { t.Check(false, what + ": second write differs at " + DocIo.FirstDiff(a, b)); return false; }
            string d = DocCompare.Diff(doc, back, what);
            if (d != null) { t.Check(false, "model differs: " + d); return false; }
            t.Check(true, what);
            return true;
        }
    }
}
