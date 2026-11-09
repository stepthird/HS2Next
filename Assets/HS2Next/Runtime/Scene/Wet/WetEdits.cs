// The edits of a character's wet block as plain work on a copy of it, Unity-free: what the operations layer does to a block before it hands it to the one command that
// replaces a block. No clock runs here and nothing is drawn.
//   members    every value a command or a slider can set, by its path in the block (`look.amount`, `film.gloss`) or by one of the names scene scripts use (`maxRadius`,
//              `spreadTime`, `dripTail`, `dripRate`, `puddleGrowth`, `puddleScale`); a number is stored as given, the ranges act when a value is applied
//   life       a new stain, start, full, dry, keep, remove, move
// A kept stain takes no further edit: its clock stands, its puddles keep their colour, and a new stain with the same origin and values stands in its place. The clock, the
// map list, the ids and the puddle list are no members: the block's own edits change them.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;

namespace HS2Next.Scenes.Wet
{
    /// <summary>The kind of a member's value.</summary>
    public enum WetValueKind
    {
        Number,
        Whole,
        Switch,
        Text,
        Vector,
        Colour,
    }

    public static class WetEdits
    {
        /// <summary>The name of a stain nobody named.</summary>
        public const string DefaultName = "stain";

        /// <summary>The name of the view's marker switch: it belongs to no block.</summary>
        public const string ShowMarker = "showMarker";

        /// <summary>What a stain named by a script starts with where a call does not say: a smaller stain than the panel's, which starts from the block's own defaults.</summary>
        public const float ScriptRadius = 1.8f, ScriptSpread = 3f, ScriptTail = 5f;

        const string ShapePrefix = "shape.", OriginPrefix = "origin.", FloorPrefix = "floor.";

        sealed class Member
        {
            public string Path;
            public WetValueKind Kind;
            /// <summary>A member of a stain; else of the block.</summary>
            public bool OnStain;
            public Func<CharaWet, WetStain, object> Get;
            public Action<CharaWet, WetStain, object> Set;
        }

        static readonly Dictionary<string, Member> members = new Dictionary<string, Member>(StringComparer.Ordinal);
        static readonly List<string> paths = new List<string>();

        /// <summary>The names scene scripts use, each with the path it stands for.</summary>
        public static readonly IReadOnlyDictionary<string, string> ScriptNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["maxRadius"] = "shape.radius", ["spreadTime"] = "shape.spread", ["dripTail"] = "drips.tail", ["dripRate"] = "drips.rate",
            ["puddleGrowth"] = "drips.growth", ["puddleScale"] = "puddle.scale",
        };

        static readonly string[] noMembers = { "id", "kept", "t", "running", "maps", "current", "stains", "puddles" };

        static void Stain(string path, WetValueKind kind, Func<WetStain, object> get, Action<WetStain, object> set)
        {
            members[path] = new Member { Path = path, Kind = kind, OnStain = true, Get = (b, s) => get(s), Set = (b, s, v) => set(s, v) };
            paths.Add(path);
        }

        static void Block(string path, WetValueKind kind, Func<CharaWet, object> get, Action<CharaWet, object> set)
        {
            members[path] = new Member { Path = path, Kind = kind, Get = (b, s) => get(b), Set = (b, s, v) => set(b, v) };
            paths.Add(path);
        }

        static WetEdits()
        {
            const WetValueKind N = WetValueKind.Number, B = WetValueKind.Switch, T = WetValueKind.Text, V = WetValueKind.Vector, C = WetValueKind.Colour;
            Stain("name", T, s => s.Name, (s, v) => s.Name = (string)v);
            Stain("preset", T, s => s.Preset, (s, v) => WetPreset.ApplyTo(s, (string)v));
            Stain("origin.mode", T, s => s.Origin.Mode, (s, v) => s.Origin.Mode = (string)v);
            Stain("origin.bone", T, s => s.Origin.Bone, (s, v) => s.Origin.Bone = (string)v);
            Stain("origin.local", V, s => s.Origin.Local, (s, v) => s.Origin.Local = (Vec3)v);
            Stain("origin.down", N, s => s.Origin.Down, (s, v) => s.Origin.Down = (float)v);
            Stain("origin.out", N, s => s.Origin.Out, (s, v) => s.Origin.Out = (float)v);
            Stain("shape.radius", N, s => s.Shape.Radius, (s, v) => s.Shape.Radius = (float)v);
            Stain("shape.spread", N, s => s.Shape.Spread, (s, v) => s.Shape.Spread = (float)v);
            Stain("shape.up", N, s => s.Shape.Up, (s, v) => s.Shape.Up = (float)v);
            Stain("shape.down", N, s => s.Shape.Down, (s, v) => s.Shape.Down = (float)v);
            Stain("shape.front", N, s => s.Shape.Front, (s, v) => s.Shape.Front = (float)v);
            Stain("shape.back", N, s => s.Shape.Back, (s, v) => s.Shape.Back = (float)v);
            Stain("shape.side", N, s => s.Shape.Side, (s, v) => s.Shape.Side = (float)v);
            Stain("shape.outer", N, s => s.Shape.Outer, (s, v) => s.Shape.Outer = (float)v);
            Stain("shape.noise", N, s => s.Shape.Noise, (s, v) => s.Shape.Noise = (float)v);
            Stain("shape.feather", N, s => s.Shape.Feather, (s, v) => s.Shape.Feather = (float)v);
            Stain("look.saturate", N, s => s.Look.Saturate, (s, v) => s.Look.Saturate = (float)v);
            Stain("look.tint", V, s => s.Look.Tint, (s, v) => s.Look.Tint = (Vec3)v);
            Stain("look.stain", V, s => s.Look.Stain, (s, v) => s.Look.Stain = (Vec3)v);
            Stain("look.amount", N, s => s.Look.Amount, (s, v) => s.Look.Amount = (float)v);
            Stain("look.skin", N, s => s.Look.Skin, (s, v) => s.Look.Skin = (float)v);
            Stain("drips.on", B, s => s.Drips.On, (s, v) => s.Drips.On = (bool)v);
            Stain("drips.rate", N, s => s.Drips.Rate, (s, v) => s.Drips.Rate = (float)v);
            Stain("drips.tail", N, s => s.Drips.Tail, (s, v) => s.Drips.Tail = (float)v);
            Stain("drips.size", N, s => s.Drips.Size, (s, v) => s.Drips.Size = (float)v);
            Stain("drips.growth", N, s => s.Drips.Growth, (s, v) => s.Drips.Growth = (float)v);
            Stain("drips.skin", B, s => s.Drips.Skin, (s, v) => s.Drips.Skin = (bool)v);
            Stain("drips.color", C, s => s.Drips.Color, (s, v) => s.Drips.Color = (Color4)v);
            Stain("drips.shine", N, s => s.Drips.Shine, (s, v) => s.Drips.Shine = (float)v);
            Stain("puddle.color", C, s => s.Puddle.Color, (s, v) => s.Puddle.Color = (Color4)v);
            Stain("puddle.shine", N, s => s.Puddle.Shine, (s, v) => s.Puddle.Shine = (float)v);
            Stain("puddle.edge", N, s => s.Puddle.Edge, (s, v) => s.Puddle.Edge = (float)v);
            Stain("puddle.scale", N, s => s.Puddle.Scale, (s, v) => s.Puddle.Scale = (float)v);
            Stain("loop", B, s => s.Loop, (s, v) => s.Loop = (bool)v);
            Stain("hold", N, s => s.Hold, (s, v) => s.Hold = (float)v);
            Stain("seed", WetValueKind.Whole, s => s.Seed, (s, v) => s.Seed = (int)v);
            Block("targets.skin", B, b => b.Targets.Skin, (b, v) => b.Targets.Skin = (bool)v);
            Block("targets.hair", B, b => b.Targets.Hair, (b, v) => b.Targets.Hair = (bool)v);
            Block("targets.accessories", B, b => b.Targets.Accessories, (b, v) => b.Targets.Accessories = (bool)v);
            Block("targets.shoes", B, b => b.Targets.Shoes, (b, v) => b.Targets.Shoes = (bool)v);
            Block("targets.gloves", B, b => b.Targets.Gloves, (b, v) => b.Targets.Gloves = (bool)v);
            Block("film.on", B, b => b.Film.On, (b, v) => b.Film.On = (bool)v);
            Block("film.gloss", N, b => b.Film.Gloss, (b, v) => b.Film.Gloss = (float)v);
            Block("film.smooth", N, b => b.Film.Smooth, (b, v) => b.Film.Smooth = (float)v);
            Block("film.opacity", N, b => b.Film.Opacity, (b, v) => b.Film.Opacity = (float)v);
            Block("mirror.on", B, b => b.Mirror.On, (b, v) => b.Mirror.On = (bool)v);
            Block("mirror.strength", N, b => b.Mirror.Strength, (b, v) => b.Mirror.Strength = (float)v);
            Block("mirror.scale", N, b => b.Mirror.Scale, (b, v) => b.Mirror.Scale = (float)v);
            Block("floor.mode", T, b => b.Floor.Mode, (b, v) => b.Floor.Mode = (string)v);
            Block("floor.offset", N, b => b.Floor.Offset, (b, v) => b.Floor.Offset = (float)v);
            Block("floor.height", N, b => b.Floor.Height, (b, v) => b.Floor.Height = (float)v);
        }

        /// <summary>Every path a value can be set by, in the block's order.</summary>
        public static IReadOnlyList<string> Paths => paths;

        /// <summary>The path a name stands for: a path, or a name scene scripts use; null for anything else.</summary>
        public static string PathOf(string name)
        {
            if (name == null) return null;
            if (ScriptNames.TryGetValue(name, out string path)) return path;
            return members.ContainsKey(name) ? name : null;
        }

        /// <summary>True for a name of the block that no value can set, as it has its own command.</summary>
        public static bool IsNoMember(string name) => Array.IndexOf(noMembers, name) >= 0;

        /// <summary>True for a member of a stain, false for a member of the block.</summary>
        public static bool OnStain(string path) => members.TryGetValue(path, out var m) && m.OnStain;

        /// <summary>The kind of a path's value.</summary>
        public static WetValueKind KindOf(string path) => members[path].Kind;

        /// <summary>True for a path of the floor: such an edit moves the puddles.</summary>
        public static bool IsFloor(string path) => path.StartsWith(FloorPrefix, StringComparison.Ordinal);

        /// <summary>True for a path of the origin.</summary>
        public static bool IsOrigin(string path) => path.StartsWith(OriginPrefix, StringComparison.Ordinal);

        /// <summary>True for a path a shape drag previews.</summary>
        public static bool IsShape(string path) => path.StartsWith(ShapePrefix, StringComparison.Ordinal) || IsOrigin(path);

        /// <summary>The value of a path, as its kind holds it: a float, an int, a bool, a string, a Vec3 or a Color4.</summary>
        public static object Get(CharaWet block, WetStain stain, string path) => members[path].Get(block, stain);

        /// <summary>The value a new stain or block has at <paramref name="path"/>: what a row's default button goes back to.</summary>
        public static object Default(string path) => Get(new CharaWet(), new WetStain(), path);

        /// <summary>
        /// Writes <paramref name="value"/> at <paramref name="path"/> of <paramref name="block"/> or of <paramref name="stain"/>. Numbers come as any number type, a vector and a
        /// colour as a Vec3, a Color4 or an array of numbers. Null when it is written, or stood there already (<paramref name="changed"/> false); else what is wrong, and nothing
        /// changes. A range is no reason: a value outside it is kept and clamped when applied.
        /// </summary>
        public static string Set(CharaWet block, WetStain stain, string path, object value, out bool changed)
        {
            changed = false;
            if (!members.TryGetValue(path, out var m)) return "no member \"" + path + "\"";
            string wrong = Read(m, value, out object v);
            if (wrong != null) return wrong;
            wrong = Check(block, stain, path, v);
            if (wrong != null) return wrong;
            if (path == "origin.bone" && ((string)v).Length > 0) v = PoseAddress.Canonical(PoseAddressForm.Bone, (string)v);
            if (Equals(m.Get(block, stain), v)) return null;
            m.Set(block, stain, v);
            changed = true;
            return null;
        }

        static string Read(Member m, object value, out object v)
        {
            v = null;
            switch (m.Kind)
            {
                case WetValueKind.Number:
                    if (!TryNumber(value, out float f)) return m.Path + " takes a finite number";
                    v = f;
                    return null;
                case WetValueKind.Whole:
                    if (!(value is int) && !(value is long l && l >= int.MinValue && l <= int.MaxValue)) return m.Path + " takes a whole number";
                    v = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    return null;
                case WetValueKind.Switch:
                    if (!(value is bool)) return m.Path + " takes true or false";
                    v = value;
                    return null;
                case WetValueKind.Text:
                    if (!(value is string)) return m.Path + " takes a text";
                    v = value;
                    return null;
                case WetValueKind.Vector:
                    if (value is Vec3) { v = value; return null; }
                    if (!TryNumbers(value, 3, out var a)) return m.Path + " takes [x, y, z]";
                    v = new Vec3(a[0], a[1], a[2]);
                    return null;
                default:
                    if (value is Color4) { v = value; return null; }
                    if (!TryNumbers(value, 4, out var c)) return m.Path + " takes [r, g, b, a]";
                    v = new Color4(c[0], c[1], c[2], c[3]);
                    return null;
            }
        }

        static bool TryNumber(object value, out float f)
        {
            f = 0f;
            switch (value)
            {
                case float x: f = x; break;
                case double x: f = (float)x; break;
                case int x: f = x; break;
                case long x: f = x; break;
                default: return false;
            }
            return !float.IsNaN(f) && !float.IsInfinity(f);
        }

        static bool TryNumbers(object value, int count, out float[] v)
        {
            v = null;
            if (!(value is System.Collections.IList list) || list.Count != count) return false;
            var r = new float[count];
            for (int i = 0; i < count; i++)
                if (!TryNumber(list[i], out r[i])) return false;
            v = r;
            return true;
        }

        // What a member refuses besides the wrong kind of value.
        static string Check(CharaWet block, WetStain stain, string path, object v)
        {
            string text = v as string;
            switch (path)
            {
                case "name":
                    if (text.Length == 0) return "a stain needs a name";
                    var other = block.FindByName(text);
                    if (other != null && other != stain) return "a stain is already named \"" + text + "\"";
                    return null;
                case "preset":
                    return WetPreset.IsKnown(text) ? null : "no preset \"" + text + "\": " + string.Join(" or ", WetPreset.Names);
                case "origin.mode":
                    return text == WetOriginValues.ModeDefault || text == WetOriginValues.ModeBone ? null : "origin.mode takes default or bone";
                case "origin.bone":
                    return text.Length == 0 || PoseAddress.Canonical(PoseAddressForm.Bone, text) != null ? null : "\"" + text + "\" is no bone address";
                case "floor.mode":
                    foreach (string mode in WetFloorValues.Modes) if (mode == text) return null;
                    return "floor.mode takes " + string.Join(", ", WetFloorValues.Modes);
                default:
                    return null;
            }
        }

        // ------------------------------------------------------------ a stain's life

        /// <summary>The next seed: one past the highest of the block, so no two stains of a block drip alike.</summary>
        public static int NextSeed(CharaWet block)
        {
            int high = 0;
            foreach (var s in block.Stains) if (s.Seed > high) high = s.Seed;
            return high + 1;
        }

        /// <summary><paramref name="name"/>, or the same with a number after it when a stain of the block has that name; never longer than a name may be.</summary>
        public static string UniqueName(CharaWet block, string name)
        {
            if (string.IsNullOrEmpty(name)) name = DefaultName;
            if (name.Length > WetLimits.MaxName) name = name.Substring(0, WetLimits.MaxName);
            if (block.FindByName(name) == null) return name;
            for (int n = 2; ; n++)
            {
                string tail = " " + n.ToString(CultureInfo.InvariantCulture);
                string text = (name.Length + tail.Length > WetLimits.MaxName ? name.Substring(0, WetLimits.MaxName - tail.Length) : name) + tail;
                if (block.FindByName(text) == null) return text;
            }
        }

        /// <summary>A dry stain of <paramref name="preset"/> with the default origin and a new id and seed, not yet in the block; null for an unknown preset.</summary>
        public static WetStain NewStain(CharaWet block, string name, string preset)
        {
            var s = new WetStain { Id = block.NextId(), Name = name, Seed = NextSeed(block) };
            return WetPreset.ApplyTo(s, preset) ? s : null;
        }

        /// <summary>Adds <paramref name="stain"/> on top; with no current stain it becomes the current one, and with <paramref name="current"/> in any case.</summary>
        public static void Add(CharaWet block, WetStain stain, bool current = false)
        {
            block.Stains.Add(stain);
            if (current || block.CurrentStain == null) block.Current = stain.Id;
        }

        /// <summary>Puts <paramref name="fresh"/> in the place of <paramref name="old"/>, under its id and seed; the old stain's puddles go.</summary>
        public static void Replace(CharaWet block, WetStain old, WetStain fresh)
        {
            fresh.Id = old.Id;
            fresh.Seed = old.Seed;
            block.Stains[block.Stains.IndexOf(old)] = fresh;
            block.Puddles.RemoveAll(p => p.Stain == old.Id);
        }

        /// <summary>The stain runs from 0.</summary>
        public static void Start(WetStain s, bool? loop = null)
        {
            s.T = 0f;
            s.Running = true;
            if (loop != null) s.Loop = loop.Value;
        }

        /// <summary>The stain stands at the end of its spread, not running.</summary>
        public static void Full(WetStain s)
        {
            s.T = s.Shape.Applied().Spread;
            s.Running = false;
        }

        /// <summary>
        /// <paramref name="only"/>, or every stain that is not kept, goes back to 0 and stops; with <paramref name="puddles"/> the puddles of those stains go too. What a kept stain
        /// froze stays.
        /// </summary>
        public static void Dry(CharaWet block, WetStain only, bool puddles)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in block.Stains)
            {
                if (only != null ? s != only : s.Kept) continue;
                s.T = 0f;
                s.Running = false;
                ids.Add(s.Id);
            }
            if (puddles) block.Puddles.RemoveAll(p => p.Color == null && ids.Contains(p.Stain));
        }

        /// <summary>Takes away every puddle that is not frozen; how many went.</summary>
        public static int ClearPuddles(CharaWet block) => block.Puddles.RemoveAll(p => p.Color == null);

        /// <summary>
        /// Freezes <paramref name="stain"/> as a layer: its clock stops where it is, its puddles keep their colour, and a new current stain with the same origin and values starts dry
        /// on top. The new stain, which shares the frozen one's maps.
        /// </summary>
        public static WetStain Keep(CharaWet block, WetStain stain)
        {
            var next = block.Clone().Find(stain.Id);
            stain.Kept = true;
            stain.Running = false;
            foreach (var p in block.Puddles)
                if (p.Stain == stain.Id && p.Color == null) p.Color = stain.Puddle.Color;
            next.Id = block.NextId();
            next.Name = UniqueName(block, stain.Name);
            next.Seed = NextSeed(block);
            next.T = 0f;
            next.Running = false;
            Add(block, next, true);
            return next;
        }

        /// <summary>Takes <paramref name="stain"/> and its puddles away; the current stain, if it was, becomes the last one that is not kept.</summary>
        public static void Remove(CharaWet block, WetStain stain)
        {
            block.Stains.Remove(stain);
            block.Puddles.RemoveAll(p => p.Stain == stain.Id);
            if (block.Current != stain.Id) return;
            block.Current = "";
            for (int i = block.Stains.Count - 1; i >= 0; i--)
                if (!block.Stains[i].Kept) { block.Current = block.Stains[i].Id; break; }
        }

        /// <summary>Moves <paramref name="stain"/> to <paramref name="index"/> of the order; false when it stands there or the index is outside.</summary>
        public static bool Move(CharaWet block, WetStain stain, int index)
        {
            int from = block.Stains.IndexOf(stain);
            if (from < 0 || index < 0 || index >= block.Stains.Count || index == from) return false;
            block.Stains.RemoveAt(from);
            block.Stains.Insert(index, stain);
            return true;
        }
    }
}
