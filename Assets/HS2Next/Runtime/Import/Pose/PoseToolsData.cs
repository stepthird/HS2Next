// Typed view of the `PoseToolsData` entry (HS2_PoseTools) of a pose-file KKEx trailer (
// ; plugin semantics). PluginData.data is a map with, as written by the plugin:
//   EyebrowPatternData int, EyesPatternData int, MouthPatternData int, EyeOpenData float, MouthOpenData float,
//   JointCorrectionData bin = MessagePack bool[] (all 8 joint-correction categories),
//   SkirtFKData (optional) bin = MessagePack map int (skirt Bone-list no 400..447) → [x, y, z] (Euler, float32).
// The view decodes the structure and keeps raw values: every field's MessagePack bytes (RawFields, file order in
// FieldOrder), float32 bits where the value is float32, unknown fields listed, decode problems listed instead of
// thrown. Unity-free (System only).
using System;
using System.Collections.Generic;

namespace HS2Next.Import.Pose
{
    /// <summary>One skirt FK value of `SkirtFKData` (Euler degrees; raw float32 bits kept).</summary>
    public struct PoseSkirtFk
    {
        /// <summary>Skirt Bone-list no (400..447 in the corpus).</summary>
        public int Key;
        public uint XBits, YBits, ZBits;
        public PoseVector3 Rot => new PoseVector3(FloatBits.ToFloat(XBits), FloatBits.ToFloat(YBits), FloatBits.ToFloat(ZBits));
        public override string ToString() => Key + ": " + Rot;
    }

    public sealed class PoseToolsData
    {
        public const string Guid = "PoseToolsData";
        public const string EyebrowPatternField = "EyebrowPatternData";
        public const string EyesPatternField = "EyesPatternData";
        public const string MouthPatternField = "MouthPatternData";
        public const string EyeOpenField = "EyeOpenData";
        public const string MouthOpenField = "MouthOpenData";
        public const string JointCorrectionField = "JointCorrectionData";
        public const string SkirtFkField = "SkirtFKData";
        public static readonly string[] KnownFields =
            { EyebrowPatternField, EyesPatternField, MouthPatternField, EyeOpenField, MouthOpenField, JointCorrectionField, SkirtFkField };

        /// <summary>PluginData.version of the entry.</summary>
        public int? Version { get; private set; }
        /// <summary>Eyebrow / eyes / mouth pattern numbers (character status), or null when absent.</summary>
        public int? EyebrowPattern { get; private set; }
        public int? EyesPattern { get; private set; }
        public int? MouthPattern { get; private set; }
        /// <summary>Eye open maximum and mouth open (0..1), or null when absent.</summary>
        public float? EyeOpen { get; private set; }
        public float? MouthOpen { get; private set; }
        /// <summary>Raw float32 bits of EyeOpen / MouthOpen when stored as float32.</summary>
        public uint? EyeOpenBits { get; private set; }
        public uint? MouthOpenBits { get; private set; }
        /// <summary>All joint-correction categories (8 in the corpus), or null when absent.</summary>
        public bool[] JointCorrection { get; private set; }
        /// <summary>Skirt FK values in file order, or null when the field is absent.</summary>
        public List<PoseSkirtFk> SkirtFK { get; private set; }

        /// <summary>Field names in file order.</summary>
        public List<string> FieldOrder { get; } = new List<string>();
        /// <summary>MessagePack bytes of each field value (unknown fields included).</summary>
        public Dictionary<string, byte[]> RawFields { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>Fields not in <see cref="KnownFields"/>.</summary>
        public List<string> UnknownFields { get; } = new List<string>();
        /// <summary>Decode problems (the raw bytes are kept).</summary>
        public List<string> Problems { get; } = new List<string>();

        PoseToolsData() { }

        /// <summary>Decodes the entry; never throws (problems are listed).</summary>
        public static PoseToolsData From(KKExEntry e)
        {
            var p = new PoseToolsData { Version = e?.Version };
            if (e == null) { p.Problems.Add("no entry"); return p; }
            if (e.Problem != null) p.Problems.Add(e.Problem);
            var d = e.RawData;
            if (d == null) { p.Problems.Add("no data"); return p; }
            List<PoseMsgPackSpan> spans;
            try { spans = PoseMsgPack.MapSpans(d, 0, d.Length, out _); }
            catch (PoseMsgPackException x) { p.Problems.Add("data is not a map: " + x.Message); return p; }
            foreach (var s in spans)
            {
                string name;
                try { name = PoseMsgPack.Decode(d, s.KeyStart, s.KeyEnd, out _) as string; }
                catch (PoseMsgPackException x) { p.Problems.Add("key: " + x.Message); continue; }
                if (name == null) { p.Problems.Add("non-string key"); continue; }
                var raw = KKExEntry.Slice(d, s.ValueStart, s.ValueEnd);
                if (p.RawFields.ContainsKey(name)) p.Problems.Add("duplicate field " + name + " (last kept)");
                else p.FieldOrder.Add(name);
                p.RawFields[name] = raw;
                try { p.DecodeField(name, raw); }
                catch (PoseMsgPackException x) { p.Problems.Add(name + ": " + x.Message); }
            }
            return p;
        }

        void DecodeField(string name, byte[] raw)
        {
            object v = PoseMsgPack.Decode(raw, 0, raw.Length, out _);
            switch (name)
            {
                case EyebrowPatternField: EyebrowPattern = Int(name, v); break;
                case EyesPatternField: EyesPattern = Int(name, v); break;
                case MouthPatternField: MouthPattern = Int(name, v); break;
                case EyeOpenField: EyeOpen = Float(name, v); if (PoseMsgPack.TryFloat32Bits(raw, 0, raw.Length, out uint eb)) EyeOpenBits = eb; break;
                case MouthOpenField: MouthOpen = Float(name, v); if (PoseMsgPack.TryFloat32Bits(raw, 0, raw.Length, out uint mb)) MouthOpenBits = mb; break;
                case JointCorrectionField: JointCorrection = Bools(name, v); break;
                case SkirtFkField: SkirtFK = Skirt(name, v, raw); break;
                default: UnknownFields.Add(name); break;
            }
        }

        int? Int(string name, object v)
        {
            if (PoseMsgPack.TryInt(v, out long x) && x >= int.MinValue && x <= int.MaxValue) return (int)x;
            Problems.Add(name + " is not an int");
            return null;
        }

        float? Float(string name, object v)
        {
            if (PoseMsgPack.TryNumber(v, out double x)) return v is float f ? f : (float)x;
            Problems.Add(name + " is not a number");
            return null;
        }

        // The plugin stores bool[] as nested MessagePack in a bin; a plain array is accepted too.
        bool[] Bools(string name, object v)
        {
            if (v is byte[] bin) v = PoseMsgPack.Decode(bin, 0, bin.Length, out _);
            if (!(v is List<object> l)) { Problems.Add(name + " is not a bool array"); return null; }
            var r = new bool[l.Count];
            for (int i = 0; i < l.Count; i++)
            {
                if (l[i] is bool b) r[i] = b;
                else { Problems.Add(name + "[" + i + "] is not a bool"); }
            }
            return r;
        }

        // Dictionary<int, Vector3> as nested MessagePack in a bin (a plain map is accepted too); Vector3 = [x, y, z].
        List<PoseSkirtFk> Skirt(string name, object v, byte[] raw)
        {
            byte[] d = raw;
            int start = 0, end = raw.Length;
            if (v is byte[] bin) { d = bin; end = bin.Length; }
            var spans = PoseMsgPack.MapSpans(d, start, end, out _);
            var list = new List<PoseSkirtFk>(spans.Count);
            foreach (var s in spans)
            {
                var k = PoseMsgPack.Decode(d, s.KeyStart, s.KeyEnd, out _);
                if (!PoseMsgPack.TryInt(k, out long key) || key < int.MinValue || key > int.MaxValue) { Problems.Add(name + ": non-int key"); continue; }
                if (!PoseMsgPack.TryArrayHeader(d, s.ValueStart, s.ValueEnd, out int n, out int q) || n != 3) { Problems.Add(name + "[" + key + "] is not [x, y, z]"); continue; }
                var e = new PoseSkirtFk { Key = (int)key };
                var c = new uint[3];
                for (int i = 0; i < 3; i++)
                {
                    if (PoseMsgPack.TryFloat32Bits(d, q, s.ValueEnd, out uint b)) { c[i] = b; q += 5; continue; }
                    var x = PoseMsgPack.Decode(d, q, s.ValueEnd, out q);
                    if (PoseMsgPack.TryNumber(x, out double dv)) c[i] = FloatBits.ToBits((float)dv);
                    else Problems.Add(name + "[" + key + "] component " + i + " is not a number");
                }
                e.XBits = c[0]; e.YBits = c[1]; e.ZBits = c[2];
                list.Add(e);
            }
            return list;
        }

        public override string ToString() =>
            $"PoseToolsData v{Version} brow {EyebrowPattern} eyes {EyesPattern} mouth {MouthPattern} eyeOpen {EyeOpen} mouthOpen {MouthOpen} " +
            $"joint {(JointCorrection == null ? "-" : JointCorrection.Length.ToString())} skirt {(SkirtFK == null ? "-" : SkirtFK.Count.ToString())}" +
            (UnknownFields.Count > 0 ? " unknown " + string.Join(",", UnknownFields) : "") + (Problems.Count > 0 ? " problems " + Problems.Count : "");
    }
}
