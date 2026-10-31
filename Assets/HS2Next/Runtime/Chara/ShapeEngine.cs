// Body / face shape sliders.
// Key data: binary per-bone samples (25 keys) of local pos / euler / scale.
// Category info: which slider drives which Src bone channels.
// Mapping (Resources/shape_*_map.txt): how Src values are combined into Dst helper transforms.
// Bust shape masks (body only): per side, the bust shape ids of BustMaskIds can be masked; a masked id is evaluated at 0.5
// for that side's source bones (the other side keeps the slider value), and that side's bust rows are written alone. The
// nipple's own rows (NippleRowBones) are not bust rows: only a full update writes them, and a full update evaluates every
// row with the slider values before the bust rows of a masked side are written again, so a mask never reaches the nipple
// rows (as in HS2). All masks are open by default, which leaves every result unchanged.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HS2Next.Chara
{
    public class ShapeEngine
    {
        struct Key { public Vector3 pos, rot, scl; }
        class Src { public Vector3 pos = Vector3.zero, rot = Vector3.zero, scl = Vector3.one; }
        struct Link { public string src; public bool[] flags; public int side; }
        class Row { public int mask; public Transform dst; public string op; public Expr[] args; public string dstName; public HashSet<string> refs; }

        readonly Dictionary<string, Key[]> keys = new Dictionary<string, Key[]>();
        readonly Dictionary<int, List<Link>> sliders = new Dictionary<int, List<Link>>();
        readonly Dictionary<string, Src> src = new Dictionary<string, Src>();
        readonly List<Row> rows = new List<Row>();
        int missingDst, missingKeys;
        // the last value given per slider (what an unmasked id is evaluated at again)
        readonly Dictionary<int, float> values = new Dictionary<int, float>();
        // bust shape masks: bit k of bustMask[side] = BustMaskIds[k] masked on that side (0 = left, 1 = right)
        readonly int[] bustMask = new int[2];
        readonly List<Row>[] sideRows = { new List<Row>(), new List<Row>() };
        readonly Transform[][] sideBones = { Array.Empty<Transform>(), Array.Empty<Transform>() };
        HashSet<string> parseRefs;
        // true while a full update evaluates the masked ids at their slider values
        bool masksSuspended;

        /// <summary>The target bones (name prefixes, per side) of the nipple's own rows, which the bust masks never write.</summary>
        public static readonly string[] NippleRowBones = { "cf_J_Mune_Nip02_s_", "cf_J_Mune_Nipacs01_" };
        public string Summary => $"{sliders.Count} sliders, {src.Count} src, {rows.Count} rows ({missingDst} rows without dst, {missingKeys} src without keys)";

        /// <summary>Test hook (Editor/ShapeMapTest): map text used instead of the Resources table when set. Null = default.</summary>
        public static string BodyMapOverride, FaceMapOverride;

        /// <summary>How slider values outside 0..1 take effect (C3).</summary>
        public enum Range
        {
            /// <summary>Vanilla HS2: the effect stops at 0 and 1 (the value is still stored as given).</summary>
            Clamp,
            /// <summary>HS2 with the community slider extender (the reference install): shapes extrapolate along the
            /// line through the first and the last key, hair bundles lerp unclamped, the male range check is skipped.</summary>
            Extrapolate,
        }

        /// <summary>Global option, default Extrapolate (a decision). Read when a value is set.</summary>
        public static Range SliderRange = Range.Extrapolate;

        /// <summary>A card / document slider value as the shape stage receives it: unchanged with Extrapolate; with Clamp,
        /// males get the vanilla range check (0..1; the effect of other values is clamped by SetValue anyway).</summary>
        public static float CardValue(float v, bool male) => male && SliderRange == Range.Clamp ? Mathf.Clamp01(v) : v;

        /// <summary>The lerp of the mappings the slider extender unclamps (hair bundles): unclamped with Extrapolate,
        /// Mathf.Lerp with Clamp. Equal to Mathf.Lerp for t in 0..1.</summary>
        public static float RangeLerp(float a, float b, float t) => SliderRange == Range.Extrapolate ? Mathf.LerpUnclamped(a, b, t) : Mathf.Lerp(a, b, t);

        public static ShapeEngine Body(Transform cfJRoot, byte[] keyData, string info) =>
            new ShapeEngine(cfJRoot, keyData, info, BodyMapOverride ?? Resources.Load<TextAsset>("shape_body_map").text, true);
        public static ShapeEngine Face(Transform headBone, byte[] keyData, string info) =>
            new ShapeEngine(headBone, keyData, info, FaceMapOverride ?? Resources.Load<TextAsset>("shape_face_map").text, false);

        /// <summary>The body shape ids a bust mask covers, in the order of the sway table's seven bust shape flags and its
        /// nipple flag (mask bit k = BustMaskIds[k]).</summary>
        public static readonly int[] BustMaskIds = { 2, 3, 4, 5, 6, 7, 8, 32 };

        /// <summary>The value a masked bust shape id is evaluated at.</summary>
        public const float MaskedValue = 0.5f;

        /// <summary>True for the body engine (the only one with bust masks).</summary>
        public bool HasBustMasks { get; }

        ShapeEngine(Transform root, byte[] keyData, string info, string map, bool bustMasks)
        {
            HasBustMasks = bustMasks;
            using (var br = new BinaryReader(new MemoryStream(keyData), Encoding.UTF8))
            {
                int n = br.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string name = br.ReadString();
                    int kc = br.ReadInt32();
                    var arr = new Key[kc];
                    for (int k = 0; k < kc; k++)
                    {
                        br.ReadInt32();
                        arr[k].pos = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        arr[k].rot = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        arr[k].scl = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    }
                    keys[name] = arr;
                }
            }
            foreach (var raw in info.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                var c = line.Split('\t');
                if (c.Length < 11 || !int.TryParse(c[0], out int s)) continue;
                var flags = new bool[9];
                for (int i = 0; i < 9; i++) flags[i] = c[2 + i] != "0";
                if (!sliders.TryGetValue(s, out var list)) sliders[s] = list = new List<Link>();
                list.Add(new Link { src = c[1], flags = flags, side = SideOf(c[1]) });
                if (!src.ContainsKey(c[1])) src[c[1]] = new Src();
                if (!keys.ContainsKey(c[1])) missingKeys++;
            }
            var dstCache = new Dictionary<string, Transform>();
            foreach (var raw in map.Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                var c = raw.Split('\t');
                int mask = c[0] == "always" ? 31 : c[0].StartsWith("0x") ? Convert.ToInt32(c[0], 16) : int.Parse(c[0]);
                if (!dstCache.TryGetValue(c[1], out var t)) dstCache[c[1]] = t = CharaAssembler.Find(root, c[1]);
                if (!t) { missingDst++; continue; }
                var args = SplitTop(c[3]);
                var ex = new Expr[args.Count];
                parseRefs = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < ex.Length; i++) ex[i] = Expr.Parse(args[i], this);
                rows.Add(new Row { mask = mask, dst = t, op = c[2], args = ex, dstName = c[1], refs = parseRefs });
                parseRefs = null;
            }
            if (HasBustMasks) BuildSideRows();
        }

        static int SideOf(string src) =>
            src.EndsWith("_L", StringComparison.Ordinal) ? 0 : src.EndsWith("_R", StringComparison.Ordinal) ? 1 : -1;

        static int MaskBit(int slider)
        {
            for (int k = 0; k < BustMaskIds.Length; k++) if (BustMaskIds[k] == slider) return k;
            return -1;
        }

        // The rows of each side that read a source bone of a bust mask id on that side, and their distinct target bones.
        void BuildSideRows()
        {
            for (int side = 0; side < 2; side++)
            {
                var srcs = new HashSet<string>(StringComparer.Ordinal);
                foreach (int id in BustMaskIds)
                    if (sliders.TryGetValue(id, out var links))
                        foreach (var l in links) if (l.side == side) srcs.Add(l.src);
                var bones = new List<Transform>();
                foreach (var r in rows)
                {
                    if (!r.refs.Overlaps(srcs) || IsNippleRow(r.dstName)) continue;
                    sideRows[side].Add(r);
                    if (!bones.Contains(r.dst)) bones.Add(r.dst);
                }
                sideBones[side] = bones.ToArray();
            }
        }

        static bool IsNippleRow(string dst)
        {
            foreach (var p in NippleRowBones) if (dst.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        // The masked ids' source values again: at their slider values (suspended) or with the masks.
        void ReevaluateMaskIds(bool suspended)
        {
            masksSuspended = suspended;
            foreach (int id in BustMaskIds) if (values.TryGetValue(id, out float v)) SetValue(id, v);
        }

        /// <summary>The bust mask of a side (0 = left, 1 = right): bit k = BustMaskIds[k] masked.</summary>
        public int BustMask(int side) => side == 0 || side == 1 ? bustMask[side] : 0;

        /// <summary>The bones the bust rows of a side write (what a mask change of that side can move).</summary>
        public IReadOnlyList<Transform> BustMaskBones(int side) => side == 0 || side == 1 ? sideBones[side] : Array.Empty<Transform>();

        /// <summary>
        /// Sets the bust mask of a side (0 = left, 1 = right; bit k = BustMaskIds[k]): the changed ids are evaluated again,
        /// a masked one at <see cref="MaskedValue"/>, an unmasked one at the value it was last given, for that side's source
        /// bones; with <paramref name="write"/> that side's bust rows are then written. False when the mask was already set
        /// (or the engine has no bust masks).
        /// </summary>
        public bool SetBustMask(int side, int mask, bool write = true)
        {
            if (!HasBustMasks || (side != 0 && side != 1)) return false;
            mask &= (1 << BustMaskIds.Length) - 1;
            int changed = bustMask[side] ^ mask;
            if (changed == 0) return false;
            bustMask[side] = mask;
            for (int k = 0; k < BustMaskIds.Length; k++)
                if ((changed & (1 << k)) != 0 && values.TryGetValue(BustMaskIds[k], out float v)) SetValue(BustMaskIds[k], v);
            if (write) ApplyBustSide(side);
            return true;
        }

        /// <summary>Writes the bust rows of one side from the current source values.</summary>
        public void ApplyBustSide(int side)
        {
            if (side != 0 && side != 1) return;
            foreach (var r in sideRows[side]) Write(r);
        }

        public void SetValue(int slider, float v)
        {
            if (!sliders.TryGetValue(slider, out var links)) return;
            values[slider] = v;
            int bit = HasBustMasks ? MaskBit(slider) : -1;
            bool outside = v < 0 || v > 1;
            if (outside && SliderRange == Range.Clamp) { v = Mathf.Clamp01(v); outside = false; }
            foreach (var l in links)
            {
                if (!keys.TryGetValue(l.src, out var k) || k.Length == 0) continue;
                float lv = v;
                bool lout = outside;
                if (bit >= 0 && l.side >= 0 && !masksSuspended && (bustMask[l.side] & (1 << bit)) != 0) { lv = MaskedValue; lout = false; }
                Key s;
                if (lout) s = Extrapolated(l.src, k, lv);
                else if (lv <= 0) s = k[0];
                else if (lv >= 1) s = k[k.Length - 1];
                else
                {
                    float f = (k.Length - 1) * lv; int i = Mathf.FloorToInt(f); float t = f - i;
                    var a = k[i]; var b = k[Mathf.Min(i + 1, k.Length - 1)];
                    s = new Key
                    {
                        pos = Vector3.Lerp(a.pos, b.pos, t),
                        rot = new Vector3(Mathf.LerpAngle(a.rot.x, b.rot.x, t), Mathf.LerpAngle(a.rot.y, b.rot.y, t), Mathf.LerpAngle(a.rot.z, b.rot.z, t)),
                        scl = Vector3.Lerp(a.scl, b.scl, t),
                    };
                }
                var r = src[l.src];
                var f9 = l.flags;
                if (f9[0]) r.pos.x = s.pos.x; if (f9[1]) r.pos.y = s.pos.y; if (f9[2]) r.pos.z = s.pos.z;
                if (f9[3]) r.rot.x = s.rot.x; if (f9[4]) r.rot.y = s.rot.y; if (f9[5]) r.rot.z = s.rot.z;
                if (f9[6]) r.scl.x = s.scl.x; if (f9[7]) r.scl.y = s.scl.y; if (f9[8]) r.scl.z = s.scl.z;
            }
        }

        // Slider extender rule (measured): position and scale continue the straight line
        // through the first and the last key (not the end segment); a rotation axis continues the first-to-last
        // difference D, taken the short or the long way round so that its sign agrees with the first segment's
        // (key1 - key0 >= 0 counts as positive): key0 + D v below 0, keyLast + D (v - 1) above 1. Some sources keep the
        // rotation of the nearer end key (RotationHeld).
        static Key Extrapolated(string src, Key[] k, float v)
        {
            Key a = k[0], z = k[k.Length - 1];
            var r = new Key { pos = a.pos + (z.pos - a.pos) * v, scl = a.scl + (z.scl - a.scl) * v };
            if (RotationHeld(src)) { r.rot = v < 0 ? a.rot : z.rot; return r; }
            Vector3 first = (k.Length > 1 ? k[1].rot : a.rot) - a.rot, d = z.rot - a.rot;
            for (int i = 0; i < 3; i++)
            {
                if (d[i] > 0 && first[i] < 0) d[i] -= 360f;
                else if (d[i] < 0 && first[i] >= 0) d[i] += 360f;
            }
            r.rot = v < 0 ? a.rot - d * Mathf.Abs(v) : z.rot + d * Mathf.Abs(v - 1f);
            return r;
        }

        // Sources whose rotation the slider extender leaves at the end key (bust, mouth, lower leg, eyebrow tip families of
        // the HS2 key data; the extender's other exceptions name Koikatsu sources HS2 does not have).
        static readonly string[] HeldFamilies = { "cf_s_Mune", "cf_s_Mouth", "cf_s_LegLow", "cf_s_MayuTip" };
        static bool RotationHeld(string src)
        {
            foreach (var f in HeldFamilies) if (src.Contains(f)) return true;
            return false;
        }

        /// <summary>A full update: every row with the slider values; then, while a bust mask is on, that side's bust rows with
        /// the masked values (the nipple rows keep the slider values).</summary>
        public void Apply(int mask = 31)
        {
            bool masked = HasBustMasks && (bustMask[0] != 0 || bustMask[1] != 0);
            if (masked) ReevaluateMaskIds(true);
            foreach (var r in rows)
            {
                if ((r.mask & mask) == 0) continue;
                Write(r);
            }
            if (!masked) return;
            ReevaluateMaskIds(false);
            for (int side = 0; side < 2; side++) if (bustMask[side] != 0) ApplyBustSide(side);
        }

        static void Write(Row r)
        {
            var t = r.dst;
            switch (r.op)
            {
                case "PositionX": { var p = t.localPosition; p.x = r.args[0].Eval(); t.localPosition = p; break; }
                case "PositionY": { var p = t.localPosition; p.y = r.args[0].Eval(); t.localPosition = p; break; }
                case "PositionZ": { var p = t.localPosition; p.z = r.args[0].Eval(); t.localPosition = p; break; }
                case "Position": t.localPosition = new Vector3(r.args[0].Eval(), r.args[1].Eval(), r.args[2].Eval()); break;
                case "Rotation": t.localRotation = Quaternion.Euler(r.args[0].Eval(), r.args[1].Eval(), r.args[2].Eval()); break;
                case "Scale": t.localScale = new Vector3(r.args[0].Eval(), r.args[1].Eval(), r.args[2].Eval()); break;
            }
        }

        internal Func<float> Ref(string name)
        {
            // name.channel.axis, e.g. cf_s_height.scl.x
            int a = name.LastIndexOf('.'); int b = name.LastIndexOf('.', a - 1);
            string bone = name.Substring(0, b), ch = name.Substring(b + 1, a - b - 1); char ax = name[a + 1];
            parseRefs?.Add(bone);
            if (!src.TryGetValue(bone, out var s)) src[bone] = s = new Src();
            int axis = ax == 'x' ? 0 : ax == 'y' ? 1 : 2;
            switch (ch)
            {
                case "pos": return () => s.pos[axis];
                case "rot": return () => s.rot[axis];
                default: return () => s.scl[axis];
            }
        }

        static List<string> SplitTop(string s)
        {
            var r = new List<string>(); int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '(') depth++; else if (s[i] == ')') depth--;
                else if (s[i] == ',' && depth == 0) { r.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            r.Add(s.Substring(start));
            return r;
        }

        // Tiny expression tree: numbers, refs, + - * /, unary minus, parentheses.
        internal class Expr
        {
            Func<float> f;
            public float Eval() => f();
            public static Expr Parse(string s, ShapeEngine owner) { int p = 0; var e = new Expr { f = Sum(s, ref p, owner) }; return e; }

            static void Ws(string s, ref int p) { while (p < s.Length && s[p] == ' ') p++; }
            static Func<float> Sum(string s, ref int p, ShapeEngine o)
            {
                var a = Prod(s, ref p, o);
                while (true)
                {
                    Ws(s, ref p);
                    if (p < s.Length && (s[p] == '+' || s[p] == '-'))
                    {
                        char op = s[p++]; var b = Prod(s, ref p, o); var l = a;
                        a = op == '+' ? (Func<float>)(() => l() + b()) : () => l() - b();
                    }
                    else return a;
                }
            }
            static Func<float> Prod(string s, ref int p, ShapeEngine o)
            {
                var a = Unary(s, ref p, o);
                while (true)
                {
                    Ws(s, ref p);
                    if (p < s.Length && (s[p] == '*' || s[p] == '/'))
                    {
                        char op = s[p++]; var b = Unary(s, ref p, o); var l = a;
                        a = op == '*' ? (Func<float>)(() => l() * b()) : () => l() / b();
                    }
                    else return a;
                }
            }
            static Func<float> Unary(string s, ref int p, ShapeEngine o)
            {
                Ws(s, ref p);
                if (s[p] == '-') { p++; var a = Unary(s, ref p, o); return () => -a(); }
                if (s[p] == '(') { p++; var a = Sum(s, ref p, o); Ws(s, ref p); p++; return a; }
                int st = p;
                while (p < s.Length && (char.IsLetterOrDigit(s[p]) || s[p] == '_' || s[p] == '.')) p++;
                var tok = s.Substring(st, p - st);
                if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return () => v;
                return o.Ref(tok);
            }
        }
    }
}
