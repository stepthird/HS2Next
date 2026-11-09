// The wet block of a character node: the scene.json member `chara.wet`, the last known member of the section, after `advancedPose`,
// written only when it is not empty (such a scene is format revision 5). An ordered list of stains, each with its origin, shape, look,
// drips, puddle look, clock and maps, the block's switches for targets, film, mirror and floor, and the puddles on the floor. A map is
// one distance picture of one drawn part, an 8-bit gray PNG under res/: it holds distances, never a colour of any body. Unity-free
// save-layer classes: the node keeps one instance (never null, empty by default) and field paths reach every member
// (`chara.wet.stains[0].t`). Only the wet edit command replaces the block; the clock and the puddle list are written by the capture
// before a save. Nothing here writes a card, a coordinate or a pose file.
//
// The reader binds strictly (a wrong type fails the load as for every core member, a JSON null included; `puddles[].color` alone has a
// null form) and repairs nothing away: a value out of range and the excess over a limit stay in the document and are written back,
// each is reported, and the Applied members below say what acts. An unknown origin mode is kept and inactive. A repeated stain id is the
// one thing the reader changes: the later stain gets a derived id. A block with neither a stain nor a puddle nor an unknown member
// is empty and is not written, so the switches of a block that holds nothing are not kept.
//
// Revision 5 stays open until it is published: the members of the phases 6.7 and 6.8 of the same release join it, with no revision 6
// and no new migration. Revision 4 is public since 1.3.0 and is not widened.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>The limits of one character's block. A command beyond one is refused; a file's excess is kept in the document, not
    /// applied, and reported (<see cref="WetFindings.Limit"/>).</summary>
    public static class WetLimits
    {
        public const int MaxStains = 8;
        /// <summary>Maps of one stain.</summary>
        public const int MaxMaps = 64;
        /// <summary>Puddles of one character.</summary>
        public const int MaxPuddles = 30;
        /// <summary>Characters of a stain's name.</summary>
        public const int MaxName = 64;
    }

    /// <summary>Finding codes of the block.</summary>
    public static class WetFindings
    {
        /// <summary>Warning (reader): entries over a limit, or a name over its length; kept, not applied. Also the word a refused command gives.</summary>
        public const string Limit = "wet.limit";
        /// <summary>Warning (reader): a map whose picture is not in the file; the stain has no map there until it is applied again, the entry is kept.</summary>
        public const string MapMissing = "wet.mapMissing";
        /// <summary>Info (reader): a value out of range; kept, clamped when applied.</summary>
        public const string Range = "wet.range";
        /// <summary>Info (reader): a stain without an id, or with the id of an earlier stain; it takes a derived id.</summary>
        public const string Id = "wet.id";
    }

    /// <summary>A closed range of a value of the block.</summary>
    public readonly struct WetRange
    {
        public readonly float Min, Max;

        public WetRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public bool Contains(float f) => f >= Min && f <= Max;
        public float Clamp(float f) => f < Min ? Min : f > Max ? Max : f;
        public Vec3 Clamp(Vec3 v) => new Vec3(Clamp(v.X), Clamp(v.Y), Clamp(v.Z));
        public Color4 Clamp(Color4 c) => new Color4(Clamp(c.R), Clamp(c.G), Clamp(c.B), Clamp(c.A));
        public override string ToString() => F(Min) + ".." + F(Max);

        internal static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>The ranges of the block's values: what the panel offers. A command refuses nothing here; a file's value is kept and
    /// clamped when applied (<see cref="WetFindings.Range"/>).</summary>
    public static class WetRanges
    {
        public static readonly WetRange Unit = new WetRange(0f, 1f);
        public static readonly WetRange Radius = new WetRange(0.1f, 10f);
        public static readonly WetRange Spread = new WetRange(0f, 120f);
        public static readonly WetRange Up = new WetRange(0.5f, 6f);
        public static readonly WetRange Down = new WetRange(0.1f, 2f);
        /// <summary>The axis factors front, back and side.</summary>
        public static readonly WetRange Axis = new WetRange(0.3f, 3f);
        public static readonly WetRange Outer = new WetRange(0f, 5f);
        public static readonly WetRange Noise = new WetRange(0f, 2f);
        public static readonly WetRange Feather = new WetRange(0.02f, 1.5f);
        public static readonly WetRange Saturate = new WetRange(0.5f, 2f);
        public static readonly WetRange Tint = new WetRange(0f, 2f);
        public static readonly WetRange DripRate = new WetRange(0f, 80f);
        public static readonly WetRange DripTail = new WetRange(0f, 30f);
        public static readonly WetRange DripSize = new WetRange(0.02f, 0.2f);
        public static readonly WetRange DripGrowth = new WetRange(0f, 5f);
        public static readonly WetRange PuddleEdge = new WetRange(0.005f, 0.5f);
        public static readonly WetRange PuddleScale = new WetRange(0.3f, 4f);
        public static readonly WetRange MirrorScale = new WetRange(0.1f, 1f);
        public static readonly WetRange FloorOffset = new WetRange(-2f, 2f);
        public static readonly WetRange Hold = new WetRange(0f, 20f);
        /// <summary>A stain's time, a map's reach, a puddle's area: not below 0.</summary>
        public static readonly WetRange NotNegative = new WetRange(0f, float.MaxValue);
    }

    /// <summary>The two presets. A preset writes the look, the drip colour and shine and the puddle colour and shine of a stain, and nothing else.</summary>
    public static class WetPreset
    {
        public const string Water = "water", Blood = "blood";

        static readonly string[] names = { Water, Blood };

        /// <summary>The presets, in the order the panel shows them.</summary>
        public static IReadOnlyList<string> Names => names;

        public static bool IsKnown(string preset) => Array.IndexOf(names, preset) >= 0;

        /// <summary>Writes <paramref name="preset"/>'s values and its name into <paramref name="stain"/>; false for an unknown name, and
        /// then nothing changes.</summary>
        public static bool ApplyTo(WetStain stain, string preset)
        {
            if (stain == null) throw new ArgumentNullException(nameof(stain));
            switch (preset)
            {
                case Water:
                    Write(stain, 1.15f, new Vec3(0f, 0f, 0f), 0.3f, 0.15f, new Color4(0.85f, 0.88f, 0.75f, 0.2f), 0.6f, new Color4(0.1f, 0.097f, 0.097f, 0.8f), 1f);
                    break;
                case Blood:
                    Write(stain, 1f, new Vec3(0.42f, 0.03f, 0.04f), 0.85f, 0.85f, new Color4(0.2f, 0.02f, 0.03f, 0.9f), 0.35f, new Color4(0.1f, 0.01f, 0.02f, 0.9f), 0.3f);
                    break;
                default:
                    return false;
            }
            stain.Preset = preset;
            return true;
        }

        static void Write(WetStain s, float saturate, Vec3 colour, float amount, float skin, Color4 drip, float dripShine, Color4 puddle, float puddleShine)
        {
            s.Look.Saturate = saturate;
            s.Look.Tint = Vec3.One;
            s.Look.Stain = colour;
            s.Look.Amount = amount;
            s.Look.Skin = skin;
            s.Drips.Color = drip;
            s.Drips.Shine = dripShine;
            s.Puddle.Color = puddle;
            s.Puddle.Shine = puddleShine;
        }
    }

    /// <summary>Where a stain starts: <c>default</c>, a point found on the body in front of it, or <c>bone</c>, a point in a bone's space.</summary>
    public sealed class WetOriginValues : DocObject
    {
        public const string ModeDefault = "default", ModeBone = "bone";

        static readonly string[] modes = { ModeDefault, ModeBone };

        /// <summary>An unknown string is kept and the origin is inactive.</summary>
        public string Mode = ModeDefault;
        /// <summary>A bone address, for <see cref="ModeBone"/>; else empty.</summary>
        public string Bone = "";
        /// <summary>The point in the bone's space.</summary>
        public Vec3 Local = Vec3.Zero;
        /// <summary><see cref="ModeDefault"/>: how far the ray is lowered, and how far the found point is moved out of the surface, in units.</summary>
        public float Down, Out;

        /// <summary>True for a mode this reader knows.</summary>
        public bool IsActive => Array.IndexOf(modes, Mode) >= 0;

        internal override void ReadMembers(DocReader r)
        {
            Mode = r.EnumStr("mode", Mode, modes);
            Bone = r.Str("bone", Bone);
            Local = r.V3("local", Local);
            Down = r.F32("down", Down);
            Out = r.F32("out", Out);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "mode", Mode);
            ReqStr(w, "bone", Bone);
            w.V3("local", Local);
            w.F32("down", Down);
            w.F32("out", Out);
        }
    }

    /// <summary>How a stain spreads: its radius and time, the axis factors and the edge.</summary>
    public sealed class WetShapeValues : DocObject
    {
        public float Radius = 2.2f, Spread = 6.04f, Up = 3f, Down = 0.75f, Front = 1f, Back = 1.3f, Side = 3f, Outer = 1.5f, Noise = 0.35f, Feather = 0.195f;

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public WetShapeValues Applied() => new WetShapeValues
        {
            Radius = WetRanges.Radius.Clamp(Radius), Spread = WetRanges.Spread.Clamp(Spread),
            Up = WetRanges.Up.Clamp(Up), Down = WetRanges.Down.Clamp(Down),
            Front = WetRanges.Axis.Clamp(Front), Back = WetRanges.Axis.Clamp(Back), Side = WetRanges.Axis.Clamp(Side),
            Outer = WetRanges.Outer.Clamp(Outer), Noise = WetRanges.Noise.Clamp(Noise), Feather = WetRanges.Feather.Clamp(Feather),
        };

        internal override void ReadMembers(DocReader r)
        {
            Radius = WetRead.F(r, "radius", Radius, WetRanges.Radius);
            Spread = WetRead.F(r, "spread", Spread, WetRanges.Spread);
            Up = WetRead.F(r, "up", Up, WetRanges.Up);
            Down = WetRead.F(r, "down", Down, WetRanges.Down);
            Front = WetRead.F(r, "front", Front, WetRanges.Axis);
            Back = WetRead.F(r, "back", Back, WetRanges.Axis);
            Side = WetRead.F(r, "side", Side, WetRanges.Axis);
            Outer = WetRead.F(r, "outer", Outer, WetRanges.Outer);
            Noise = WetRead.F(r, "noise", Noise, WetRanges.Noise);
            Feather = WetRead.F(r, "feather", Feather, WetRanges.Feather);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.F32("radius", Radius);
            w.F32("spread", Spread);
            w.F32("up", Up);
            w.F32("down", Down);
            w.F32("front", Front);
            w.F32("back", Back);
            w.F32("side", Side);
            w.F32("outer", Outer);
            w.F32("noise", Noise);
            w.F32("feather", Feather);
        }
    }

    /// <summary>What a stain does to the colour under it.</summary>
    public sealed class WetLookValues : DocObject
    {
        public float Saturate = 1.15f;
        public Vec3 Tint = Vec3.One;
        /// <summary>The stain's own colour (red, green, blue).</summary>
        public Vec3 Stain = Vec3.Zero;
        public float Amount = 0.3f;
        /// <summary>How strongly the stain acts on skin.</summary>
        public float Skin = 0.15f;

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public WetLookValues Applied() => new WetLookValues
        {
            Saturate = WetRanges.Saturate.Clamp(Saturate), Tint = WetRanges.Tint.Clamp(Tint), Stain = WetRanges.Unit.Clamp(Stain),
            Amount = WetRanges.Unit.Clamp(Amount), Skin = WetRanges.Unit.Clamp(Skin),
        };

        internal override void ReadMembers(DocReader r)
        {
            Saturate = WetRead.F(r, "saturate", Saturate, WetRanges.Saturate);
            Tint = WetRead.V(r, "tint", Tint, WetRanges.Tint);
            Stain = WetRead.V(r, "stain", Stain, WetRanges.Unit);
            Amount = WetRead.F(r, "amount", Amount, WetRanges.Unit);
            Skin = WetRead.F(r, "skin", Skin, WetRanges.Unit);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.F32("saturate", Saturate);
            w.V3("tint", Tint);
            w.V3("stain", Stain);
            w.F32("amount", Amount);
            w.F32("skin", Skin);
        }
    }

    /// <summary>The drops a stain sheds.</summary>
    public sealed class WetDripValues : DocObject
    {
        public bool On = true;
        /// <summary>Drops per second at full intensity.</summary>
        public float Rate = 18f;
        /// <summary>Seconds the dripping takes to end after the spread.</summary>
        public float Tail = 8f;
        public float Size = 0.031f, Growth = 1f;
        /// <summary>Skin and face shed drops too.</summary>
        public bool Skin;
        public Color4 Color = new Color4(0.85f, 0.88f, 0.75f, 0.2f);
        public float Shine = 0.6f;

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public WetDripValues Applied() => new WetDripValues
        {
            On = On, Rate = WetRanges.DripRate.Clamp(Rate), Tail = WetRanges.DripTail.Clamp(Tail), Size = WetRanges.DripSize.Clamp(Size),
            Growth = WetRanges.DripGrowth.Clamp(Growth), Skin = Skin, Color = WetRanges.Unit.Clamp(Color), Shine = WetRanges.Unit.Clamp(Shine),
        };

        internal override void ReadMembers(DocReader r)
        {
            On = r.Bool("on", On);
            Rate = WetRead.F(r, "rate", Rate, WetRanges.DripRate);
            Tail = WetRead.F(r, "tail", Tail, WetRanges.DripTail);
            Size = WetRead.F(r, "size", Size, WetRanges.DripSize);
            Growth = WetRead.F(r, "growth", Growth, WetRanges.DripGrowth);
            Skin = r.Bool("skin", Skin);
            Color = WetRead.C(r, "color", Color, WetRanges.Unit);
            Shine = WetRead.F(r, "shine", Shine, WetRanges.Unit);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("on", On);
            w.F32("rate", Rate);
            w.F32("tail", Tail);
            w.F32("size", Size);
            w.F32("growth", Growth);
            w.Bool("skin", Skin);
            w.Col("color", Color);
            w.F32("shine", Shine);
        }
    }

    /// <summary>How the puddles of a stain look.</summary>
    public sealed class WetPuddleLook : DocObject
    {
        public Color4 Color = new Color4(0.1f, 0.097f, 0.097f, 0.8f);
        public float Shine = 1f, Edge = 0.04f, Scale = 1.5f;

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public WetPuddleLook Applied() => new WetPuddleLook
        {
            Color = WetRanges.Unit.Clamp(Color), Shine = WetRanges.Unit.Clamp(Shine), Edge = WetRanges.PuddleEdge.Clamp(Edge), Scale = WetRanges.PuddleScale.Clamp(Scale),
        };

        internal override void ReadMembers(DocReader r)
        {
            Color = WetRead.C(r, "color", Color, WetRanges.Unit);
            Shine = WetRead.F(r, "shine", Shine, WetRanges.Unit);
            Edge = WetRead.F(r, "edge", Edge, WetRanges.PuddleEdge);
            Scale = WetRead.F(r, "scale", Scale, WetRanges.PuddleScale);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Col("color", Color);
            w.F32("shine", Shine);
            w.F32("edge", Edge);
            w.F32("scale", Scale);
        }
    }

    /// <summary>One distance picture of a stain on one drawn part.</summary>
    public sealed class WetMapEntry : DocObject
    {
        /// <summary>The renderer's address (the head, body, hair, clothes or accessory part and the renderer's name).</summary>
        public string Renderer;
        /// <summary>The material slot.</summary>
        public int Slot;
        /// <summary>The picture: an 8-bit gray PNG under res/.</summary>
        public string Res;
        /// <summary>The distance the picture's value 254 stands for, in units.</summary>
        public float Reach;
        /// <summary>The mesh signature: the mesh's name, its vertex count and the hash of its first UV set. Another signature on the same
        /// address leaves the map dormant.</summary>
        public string Mesh;

        /// <summary>The reach as it is applied (not below 0).</summary>
        public float AppliedReach => WetRanges.NotNegative.Clamp(Reach);

        internal override void ReadMembers(DocReader r)
        {
            Renderer = r.Str("renderer");
            Slot = r.Int("slot");
            Res = r.Res("res");
            Reach = r.F32("reach");
            if (!WetRanges.NotNegative.Contains(Reach)) WetRead.Report(r, "reach", WetRange.F(Reach) + " is below 0");
            Mesh = r.Str("mesh");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "renderer", Renderer);
            w.Int("slot", Slot);
            ReqRes(w, "res", Res);
            w.F32("reach", Reach);
            ReqStr(w, "mesh", Mesh);
        }
    }

    /// <summary>One stain of a character.</summary>
    public sealed class WetStain : DocObject
    {
        /// <summary>Unique in the block; a puddle and <see cref="CharaWet.Current"/> name a stain by it.</summary>
        public string Id = "";
        public string Name = "";
        /// <summary>The preset last applied; a free text in a file.</summary>
        public string Preset = "";
        /// <summary>Frozen as a layer: its clock stops and it takes no further edit.</summary>
        public bool Kept;
        public WetOriginValues Origin = new WetOriginValues();
        public WetShapeValues Shape = new WetShapeValues();
        public WetLookValues Look = new WetLookValues();
        public WetDripValues Drips = new WetDripValues();
        public WetPuddleLook Puddle = new WetPuddleLook();
        /// <summary>The stain's clock, in seconds of scene time.</summary>
        public float T;
        public bool Running, Loop;
        /// <summary>Seconds a loop holds before it dries.</summary>
        public float Hold = 2f;
        /// <summary>The seed of the stain's random numbers.</summary>
        public int Seed;
        public List<WetMapEntry> Maps = new List<WetMapEntry>();

        /// <summary>The clock as it is applied (not below 0).</summary>
        public float AppliedT => WetRanges.NotNegative.Clamp(T);
        /// <summary>The hold as it is applied (clamped).</summary>
        public float AppliedHold => WetRanges.Hold.Clamp(Hold);

        /// <summary>The maps that act: the first <see cref="WetLimits.MaxMaps"/>.</summary>
        public IEnumerable<WetMapEntry> AppliedMaps()
        {
            int n = 0;
            foreach (var m in Maps)
            {
                if (n++ >= WetLimits.MaxMaps) yield break;
                yield return m;
            }
        }

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Str("id", Id);
            Name = r.Str("name", Name);
            Preset = r.Str("preset", Preset);
            Kept = r.Bool("kept", Kept);
            Origin.ReadFrom(r.Obj("origin"));
            Shape.ReadFrom(r.Obj("shape"));
            Look.ReadFrom(r.Obj("look"));
            Drips.ReadFrom(r.Obj("drips"));
            Puddle.ReadFrom(r.Obj("puddle"));
            T = r.F32("t", T);
            if (!WetRanges.NotNegative.Contains(T)) WetRead.Report(r, "t", WetRange.F(T) + " is below 0");
            Running = r.Bool("running", Running);
            Loop = r.Bool("loop", Loop);
            Hold = WetRead.F(r, "hold", Hold, WetRanges.Hold);
            Seed = r.Int("seed", Seed);
            Maps = ReadList<WetMapEntry>(r, "maps");
            if (Name.Length > WetLimits.MaxName)
                r.Report(FindingSeverity.Warning, WetFindings.Limit, "name", "a name of " + Name.Length + " characters is over the limit of " + WetLimits.MaxName + "; kept");
            WetRead.Over(r, "maps", Maps.Count, WetLimits.MaxMaps, "maps");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "id", Id);
            ReqStr(w, "name", Name);
            ReqStr(w, "preset", Preset);
            w.Bool("kept", Kept);
            Origin.WriteTo(w, "origin");
            Shape.WriteTo(w, "shape");
            Look.WriteTo(w, "look");
            Drips.WriteTo(w, "drips");
            Puddle.WriteTo(w, "puddle");
            w.F32("t", T);
            w.Bool("running", Running);
            w.Bool("loop", Loop);
            w.F32("hold", Hold);
            w.Int("seed", Seed);
            WriteList(w, "maps", Maps);
        }
    }

    /// <summary>
    /// The object <c>targets</c>: which kinds a stain may reach besides clothes. Accessories, shoes and gloves are off unless asked for, and only a switch that is on is
    /// written, so a block that never used them keeps its bytes.
    /// </summary>
    public sealed class WetTargetSwitches : DocObject
    {
        public bool Skin = true, Hair = true, Accessories, Shoes, Gloves;

        /// <summary>True when a kind <paramref name="before"/> left out is on here.</summary>
        public bool Widens(WetTargetSwitches before) =>
            (Skin && !before.Skin) || (Hair && !before.Hair) || (Accessories && !before.Accessories) || (Shoes && !before.Shoes) || (Gloves && !before.Gloves);

        internal override void ReadMembers(DocReader r)
        {
            Skin = r.Bool("skin", Skin);
            Hair = r.Bool("hair", Hair);
            Accessories = r.Bool("accessories", Accessories);
            Shoes = r.Bool("shoes", Shoes);
            Gloves = r.Bool("gloves", Gloves);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("skin", Skin);
            w.Bool("hair", Hair);
            if (Accessories) w.Bool("accessories", true);
            if (Shoes) w.Bool("shoes", true);
            if (Gloves) w.Bool("gloves", true);
        }
    }

    /// <summary>The object <c>film</c>: the gloss over a wet garment.</summary>
    public sealed class WetFilmValues : DocObject
    {
        public bool On = true;
        public float Gloss = 0.9f, Smooth = 0.8f, Opacity = 0.85f;

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public WetFilmValues Applied() => new WetFilmValues { On = On, Gloss = WetRanges.Unit.Clamp(Gloss), Smooth = WetRanges.Unit.Clamp(Smooth), Opacity = WetRanges.Unit.Clamp(Opacity) };

        internal override void ReadMembers(DocReader r)
        {
            On = r.Bool("on", On);
            Gloss = WetRead.F(r, "gloss", Gloss, WetRanges.Unit);
            Smooth = WetRead.F(r, "smooth", Smooth, WetRanges.Unit);
            Opacity = WetRead.F(r, "opacity", Opacity, WetRanges.Unit);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("on", On);
            w.F32("gloss", Gloss);
            w.F32("smooth", Smooth);
            w.F32("opacity", Opacity);
        }
    }

    /// <summary>The object <c>mirror</c>: the reflection in the puddles.</summary>
    public sealed class WetMirrorValues : DocObject
    {
        public bool On = true;
        public float Strength = 0.85f;
        /// <summary>The mirror image's size against the output.</summary>
        public float Scale = 0.5f;

        /// <summary>A copy of the known members as they are applied (clamped).</summary>
        public WetMirrorValues Applied() => new WetMirrorValues { On = On, Strength = WetRanges.Unit.Clamp(Strength), Scale = WetRanges.MirrorScale.Clamp(Scale) };

        internal override void ReadMembers(DocReader r)
        {
            On = r.Bool("on", On);
            Strength = WetRead.F(r, "strength", Strength, WetRanges.Unit);
            Scale = WetRead.F(r, "scale", Scale, WetRanges.MirrorScale);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("on", On);
            w.F32("strength", Strength);
            w.F32("scale", Scale);
        }
    }

    /// <summary>The object <c>floor</c>: the height the drops land on.</summary>
    public sealed class WetFloorValues : DocObject
    {
        public const string ModeMap = "map", ModeFeet = "feet", ModeFixed = "fixed";

        static readonly string[] modes = { ModeMap, ModeFeet, ModeFixed };

        /// <summary>The modes, in the order the panel shows them.</summary>
        public static IReadOnlyList<string> Modes => modes;

        /// <summary>An unknown string is kept and acts as <see cref="ModeMap"/>.</summary>
        public string Mode = ModeMap;
        /// <summary>Added to the floor in every mode.</summary>
        public float Offset;
        /// <summary>The floor's height in <see cref="ModeFixed"/>: a world height, with no range.</summary>
        public float Height;

        /// <summary>The mode as it acts.</summary>
        public string AppliedMode => Array.IndexOf(modes, Mode) >= 0 ? Mode : ModeMap;
        /// <summary>The offset as it is applied (clamped).</summary>
        public float AppliedOffset => WetRanges.FloorOffset.Clamp(Offset);

        internal override void ReadMembers(DocReader r)
        {
            Mode = r.EnumStr("mode", Mode, modes);
            Offset = WetRead.F(r, "offset", Offset, WetRanges.FloorOffset);
            Height = r.F32("height", Height);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "mode", Mode);
            w.F32("offset", Offset);
            w.F32("height", Height);
        }
    }

    /// <summary>One puddle on the floor.</summary>
    public sealed class WetPuddleEntry : DocObject
    {
        /// <summary>The id of the stain it came from.</summary>
        public string Stain = "";
        public Vec3 Center = Vec3.Zero;
        public float Area;
        /// <summary>The seed of the outline.</summary>
        public float Seed;
        /// <summary>Null while its stain is not kept; then the frozen colour.</summary>
        public Color4? Color;

        /// <summary>The area as it is applied (not below 0).</summary>
        public float AppliedArea => WetRanges.NotNegative.Clamp(Area);

        internal override void ReadMembers(DocReader r)
        {
            Stain = r.Str("stain", Stain);
            Center = r.V3("center", Center);
            Area = r.F32("area", Area);
            if (!WetRanges.NotNegative.Contains(Area)) WetRead.Report(r, "area", WetRange.F(Area) + " is below 0");
            Seed = r.F32("seed", Seed);
            Color = r.ColN("color", Color);
            if (Color.HasValue && !In(Color.Value)) WetRead.Report(r, "color", Color.Value + " is outside " + WetRanges.Unit);
        }

        static bool In(Color4 c) => WetRanges.Unit.Contains(c.R) && WetRanges.Unit.Contains(c.G) && WetRanges.Unit.Contains(c.B) && WetRanges.Unit.Contains(c.A);

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "stain", Stain);
            w.V3("center", Center);
            w.F32("area", Area);
            w.F32("seed", Seed);
            w.ColN("color", Color);
        }
    }

    /// <summary>The block <c>chara.wet</c>: scene-only values of one character. Empty by default and then not written.</summary>
    public sealed class CharaWet : DocObject
    {
        /// <summary>The member name in the section <c>chara</c>.</summary>
        public const string MemberName = "wet";

        /// <summary>In order, the last ones on top.</summary>
        public List<WetStain> Stains = new List<WetStain>();
        /// <summary>The id of the stain the panel edits and <c>wet.start</c> runs; empty when there is none.</summary>
        public string Current = "";
        public WetTargetSwitches Targets = new WetTargetSwitches();
        public WetFilmValues Film = new WetFilmValues();
        public WetMirrorValues Mirror = new WetMirrorValues();
        public WetFloorValues Floor = new WetFloorValues();
        public List<WetPuddleEntry> Puddles = new List<WetPuddleEntry>();

        /// <summary>True when the block holds no stain, no puddle and no unknown member at any level of its own: it is then not written,
        /// and no revision is raised.</summary>
        public bool IsEmpty => Stains.Count == 0 && Puddles.Count == 0 && NoUnknown(this) && NoUnknown(Targets) && NoUnknown(Film) && NoUnknown(Mirror) && NoUnknown(Floor);

        static bool NoUnknown(DocObject o) => o.Unknown == null || o.Unknown.IsEmpty;

        /// <summary>The stain with <paramref name="id"/>, or null.</summary>
        public WetStain Find(string id)
        {
            foreach (var s in Stains) if (string.Equals(s.Id, id, StringComparison.Ordinal)) return s;
            return null;
        }

        /// <summary>The first stain named <paramref name="name"/>, or null.</summary>
        public WetStain FindByName(string name)
        {
            foreach (var s in Stains) if (string.Equals(s.Name, name, StringComparison.Ordinal)) return s;
            return null;
        }

        /// <summary>The stain <see cref="Current"/> names, or null.</summary>
        public WetStain CurrentStain => Find(Current);

        /// <summary>The lowest id of the form <c>s1</c>, <c>s2</c>, … no stain has.</summary>
        public string NextId()
        {
            for (int n = 1; ; n++)
            {
                string id = "s" + n.ToString(CultureInfo.InvariantCulture);
                if (Find(id) == null) return id;
            }
        }

        /// <summary>The stains that act: the first <see cref="WetLimits.MaxStains"/>.</summary>
        public IEnumerable<WetStain> AppliedStains()
        {
            int n = 0;
            foreach (var s in Stains)
            {
                if (n++ >= WetLimits.MaxStains) yield break;
                yield return s;
            }
        }

        /// <summary>The puddles that act: the first <see cref="WetLimits.MaxPuddles"/>.</summary>
        public IEnumerable<WetPuddleEntry> AppliedPuddles()
        {
            int n = 0;
            foreach (var p in Puddles)
            {
                if (n++ >= WetLimits.MaxPuddles) yield break;
                yield return p;
            }
        }

        /// <summary>The first rule the ids break, or null: a stain without an id, an id used twice. A block read from text has none of
        /// these, since the reader gives such a stain a derived id; an edit must check the block it was given.</summary>
        public string IdProblem()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in Stains)
            {
                if (string.IsNullOrEmpty(s.Id)) return "a stain without an id";
                if (!ids.Add(s.Id)) return "the stain id " + s.Id + " is used twice";
            }
            return null;
        }

        /// <summary>
        /// The first limit this block breaks that <paramref name="before"/> did not, or null. What a file brought over a limit may stay or
        /// shrink; an edit may not make it grow.
        /// </summary>
        public string Problem(CharaWet before = null)
        {
            if (Stains.Count > WetLimits.MaxStains && Stains.Count > (before?.Stains.Count ?? 0)) return WetFindings.Limit + ": more than " + WetLimits.MaxStains + " stains";
            if (Puddles.Count > WetLimits.MaxPuddles && Puddles.Count > (before?.Puddles.Count ?? 0)) return WetFindings.Limit + ": more than " + WetLimits.MaxPuddles + " puddles";
            foreach (var s in Stains)
            {
                var old = before?.Find(s.Id);
                if (s.Maps.Count > WetLimits.MaxMaps && s.Maps.Count > (old?.Maps.Count ?? 0)) return WetFindings.Limit + ": more than " + WetLimits.MaxMaps + " maps on stain " + s.Id;
                if (s.Name != null && s.Name.Length > WetLimits.MaxName && s.Name != old?.Name) return WetFindings.Limit + ": a name of more than " + WetLimits.MaxName + " characters";
            }
            return null;
        }

        /// <summary>
        /// Reports each map whose picture <paramref name="res"/> does not hold (<see cref="WetFindings.MapMissing"/>); returns how many.
        /// The entries stay: the stain has no map there until it is applied again.
        /// </summary>
        public int ReportMissingMaps(ResStore res, FindingList findings, string entry, string jsonPath)
        {
            int missing = 0;
            for (int i = 0; i < Stains.Count; i++)
            {
                var maps = Stains[i].Maps;
                for (int k = 0; k < maps.Count; k++)
                {
                    if (res != null && res.Contains(maps[k].Res)) continue;
                    missing++;
                    findings?.Warning(WetFindings.MapMissing, SaveFormatException.Location(entry, jsonPath + ".stains[" + i + "].maps[" + k + "].res"), "the picture " + maps[k].Res + " is not in the file; kept, the stain has no map on " + maps[k].Renderer);
                }
            }
            return missing;
        }

        internal override void ReadMembers(DocReader r)
        {
            var items = r.ObjArray("stains");
            Stains = new List<WetStain>(items.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var s = ReadNew<WetStain>(items[i]);
                if (s.Id.Length == 0 || !ids.Add(s.Id))
                {
                    string old = s.Id;
                    s.Id = JsonText.DerivedId("wet/stain/" + i.ToString(CultureInfo.InvariantCulture));
                    for (int n = 1; !ids.Add(s.Id); n++) s.Id = JsonText.DerivedId("wet/stain/" + i.ToString(CultureInfo.InvariantCulture) + "/" + n.ToString(CultureInfo.InvariantCulture));
                    items[i].Report(FindingSeverity.Info, WetFindings.Id, "id", (old.Length == 0 ? "a stain without an id" : "the stain id " + old + " repeats an earlier stain's") + "; derived id " + s.Id + " used");
                }
                Stains.Add(s);
            }
            Current = r.Str("current", Current);
            Targets.ReadFrom(r.Obj("targets"));
            Film.ReadFrom(r.Obj("film"));
            Mirror.ReadFrom(r.Obj("mirror"));
            Floor.ReadFrom(r.Obj("floor"));
            Puddles = ReadList<WetPuddleEntry>(r, "puddles");
            WetRead.Over(r, "stains", Stains.Count, WetLimits.MaxStains, "stains");
            WetRead.Over(r, "puddles", Puddles.Count, WetLimits.MaxPuddles, "puddles");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            WriteList(w, "stains", Stains);
            ReqStr(w, "current", Current);
            Targets.WriteTo(w, "targets");
            Film.WriteTo(w, "film");
            Mirror.WriteTo(w, "mirror");
            Floor.WriteTo(w, "floor");
            WriteList(w, "puddles", Puddles);
        }

        // ---------- text and copies ----------

        /// <summary>Reads a block from JSON text (one object) with the reader rules; findings go to <paramref name="findings"/>. A syntax
        /// or type error throws <see cref="SaveFormatException"/> (BadDocument; NonFinite for a number that is not finite).</summary>
        public static CharaWet Parse(string json, FindingList findings = null, string entry = MemberName, bool lenient = false) =>
            FromToken(DocReader.ParseText(json ?? "", entry), findings, entry, lenient);

        /// <summary>As <see cref="Parse"/> for a parsed token (an object; anything else throws BadDocument). The token is not changed.</summary>
        public static CharaWet FromToken(JToken token, FindingList findings = null, string entry = MemberName, bool lenient = false)
        {
            if (!(token is JObject o))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "a wet block is an object, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            var block = new CharaWet();
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
        public CharaWet Clone() => Parse(ToJson());
    }

    static class WetRead
    {
        public static float F(DocReader r, string name, float absent, WetRange range)
        {
            float f = r.F32(name, absent);
            if (!range.Contains(f)) Report(r, name, WetRange.F(f) + " is outside " + range);
            return f;
        }

        public static Vec3 V(DocReader r, string name, Vec3 absent, WetRange range)
        {
            var v = r.V3(name, absent);
            if (!range.Contains(v.X) || !range.Contains(v.Y) || !range.Contains(v.Z)) Report(r, name, v + " is outside " + range);
            return v;
        }

        public static Color4 C(DocReader r, string name, Color4 absent, WetRange range)
        {
            var c = r.Col(name, absent);
            if (!range.Contains(c.R) || !range.Contains(c.G) || !range.Contains(c.B) || !range.Contains(c.A)) Report(r, name, c + " is outside " + range);
            return c;
        }

        public static void Report(DocReader r, string member, string what) =>
            r.Report(FindingSeverity.Info, WetFindings.Range, member, what + "; kept, clamped when applied");

        public static void Over(DocReader r, string member, int count, int limit, string what)
        {
            if (count > limit)
                r.Report(FindingSeverity.Warning, WetFindings.Limit, member, (count - limit) + " " + what + " over the limit of " + limit + " are kept and not applied");
        }
    }
}
