// ExtendedSave (`KKEx`) trailer of HS2 pose files. Layout written by the
// ExtensibleSaveFormat plugin after the vanilla payload: BinaryWriter string "KKEx", int32 data version (3),
// int32 length, then `length` bytes of MessagePack: a map GUID → PluginData, where PluginData is the array
// [int version, map data]. The plugin reads the block only when the marker matches and length > 0.
//
// KKExTrailer.Parse never throws: Status Ok / NotKKEx / Malformed, Problem says why. Every entry keeps the raw
// MessagePack of its key and value (and of `data`), so unknown GUIDs and fields survive untouched; Encode() rebuilds
// the trailer from the entries and reproduces the original bytes when nothing was changed. Unity-free (System only).
using System;
using System.Collections.Generic;

namespace HS2Next.Import.Pose
{
    public enum KKExStatus
    {
        /// <summary>Marker, version, length and the MessagePack map decoded.</summary>
        Ok,
        /// <summary>The bytes do not start with the `KKEx` marker string (ExtendedSave would ignore them).</summary>
        NotKKEx,
        /// <summary>`KKEx` marker, but the length or the MessagePack map is broken (raw bytes kept).</summary>
        Malformed,
    }

    /// <summary>One GUID → PluginData entry of a `KKEx` trailer.</summary>
    public sealed class KKExEntry
    {
        /// <summary>The key as a string (non-string keys: their decoded value as text).</summary>
        public string Guid { get; private set; }
        /// <summary>MessagePack bytes of the key as written.</summary>
        public byte[] RawKey { get; private set; }
        /// <summary>MessagePack bytes of the value as written (normally the array [version, data]).</summary>
        public byte[] RawValue { get; private set; }
        /// <summary>PluginData.version, or null when the value is not [int, …].</summary>
        public int? Version { get; private set; }
        /// <summary>MessagePack bytes of PluginData.data (element 1), or null.</summary>
        public byte[] RawData { get; private set; }
        /// <summary>Decoded PluginData.data (normally Dictionary&lt;object, object&gt;; see PoseMsgPack), or null.</summary>
        public object Data { get; private set; }
        /// <summary>Why the value could not be decoded as PluginData (the raw bytes are still kept), or null.</summary>
        public string Problem { get; private set; }

        KKExEntry() { }

        internal static KKExEntry FromSpans(byte[] d, PoseMsgPackSpan s)
        {
            var e = new KKExEntry
            {
                RawKey = Slice(d, s.KeyStart, s.KeyEnd),
                RawValue = Slice(d, s.ValueStart, s.ValueEnd),
            };
            try
            {
                var k = PoseMsgPack.Decode(d, s.KeyStart, s.KeyEnd, out _);
                e.Guid = k as string ?? Convert.ToString(k, System.Globalization.CultureInfo.InvariantCulture) ?? "<nil>";
            }
            catch (PoseMsgPackException x) { e.Guid = "<undecodable key>"; e.Problem = "key: " + x.Message; }
            e.DecodeValue(d, s.ValueStart, s.ValueEnd);
            return e;
        }

        void DecodeValue(byte[] d, int start, int end)
        {
            try
            {
                if (!PoseMsgPack.TryArrayHeader(d, start, end, out int n, out int p) || n < 2)
                {
                    Problem = Problem ?? "value is not [version, data]";
                    return;
                }
                var v = PoseMsgPack.Decode(d, p, end, out int q);
                if (PoseMsgPack.TryInt(v, out long ver) && ver >= int.MinValue && ver <= int.MaxValue) Version = (int)ver;
                else Problem = Problem ?? "PluginData.version is not an int";
                int dataEnd = PoseMsgPack.Skip(d, q, end);
                RawData = Slice(d, q, dataEnd);
                Data = PoseMsgPack.Decode(d, q, dataEnd, out _);
            }
            catch (PoseMsgPackException x) { Problem = Problem ?? x.Message; }
        }

        /// <summary>A new entry [version, data] encoded compactly; <paramref name="rawData"/> is MessagePack (e.g. a map).</summary>
        public static KKExEntry Create(string guid, int version, byte[] rawData)
        {
            var w = new PoseWriter(64);
            PoseMsgPack.WriteString(w, guid);
            var key = w.ToArray();
            var e = new KKExEntry { Guid = guid, RawKey = key };
            e.SetValue(version, rawData);
            return e;
        }

        /// <summary>Replaces version and data; the value is re-encoded as [version, data] (compact int).</summary>
        public void SetValue(int version, byte[] rawData)
        {
            var data = rawData ?? new byte[] { 0x80 };
            var w = new PoseWriter(data.Length + 8);
            PoseMsgPack.WriteArrayHeader(w, 2);
            PoseMsgPack.WriteInt(w, version);
            w.Bytes(data);
            RawValue = w.ToArray();
            Version = version;
            RawData = (byte[])data.Clone();
            Problem = null;
            Data = null;
            DecodeValue(RawValue, 0, RawValue.Length);
        }

        internal static byte[] Slice(byte[] d, int s, int e)
        {
            var r = new byte[e - s];
            Buffer.BlockCopy(d, s, r, 0, r.Length);
            return r;
        }

        public override string ToString() => $"{Guid} v{(Version?.ToString() ?? "?")} ({RawValue.Length} bytes){(Problem != null ? " problem: " + Problem : "")}";
    }

    /// <summary>The decoded view of a pose-file trailer (see the file header).</summary>
    public sealed class KKExTrailer
    {
        public const string Marker = "KKEx";
        /// <summary>ExtendedSave's own entry (`gameName`).</summary>
        public const string ExtendedSaveGuid = "com.bepis.bepinex.extendedsave";

        public KKExStatus Status { get; private set; }
        /// <summary>Why Status is not Ok (or a note about extra bytes), else null.</summary>
        public string Problem { get; private set; }
        /// <summary>The whole trailer as read.</summary>
        public byte[] Raw { get; private set; }
        /// <summary>ExtendedSave data version (3 in every corpus file).</summary>
        public int Version { get; set; }
        /// <summary>The block length as stored.</summary>
        public int DeclaredLength { get; private set; }
        /// <summary>Entries in file order (empty when the block is empty).</summary>
        public List<KKExEntry> Entries { get; } = new List<KKExEntry>();
        /// <summary>Bytes inside the declared block after the map (normally none; kept).</summary>
        public byte[] BlockTail { get; private set; }
        /// <summary>Bytes after the declared block (normally none; kept).</summary>
        public byte[] TrailingBytes { get; private set; }

        byte[] markerRaw;
        byte[] mapHeaderRaw;
        int mapCount = -1;

        KKExTrailer() { }

        /// <summary>The first entry with this GUID, or null.</summary>
        public KKExEntry Find(string guid)
        {
            foreach (var e in Entries) if (string.Equals(e.Guid, guid, StringComparison.Ordinal)) return e;
            return null;
        }

        /// <summary>Decodes trailer bytes; never throws.</summary>
        public static KKExTrailer Parse(byte[] t)
        {
            var x = new KKExTrailer { Raw = t ?? new byte[0] };
            t = x.Raw;
            var r = new PoseReader(t, 0, t.Length);
            RawString m;
            try { m = r.String("trailer marker"); }
            catch (PoseFormatException e) { x.Status = KKExStatus.NotKKEx; x.Problem = e.Message; return x; }
            if (m.Value != Marker) { x.Status = KKExStatus.NotKKEx; x.Problem = "marker is not KKEx"; return x; }
            x.markerRaw = m.Raw;
            try
            {
                x.Version = r.Int32("trailer version");
                x.DeclaredLength = r.Int32("trailer length");
            }
            catch (PoseFormatException e) { x.Status = KKExStatus.Malformed; x.Problem = e.Message; return x; }
            int start = r.Pos;
            if (x.DeclaredLength < 0 || x.DeclaredLength > t.Length - start)
            {
                x.Status = KKExStatus.Malformed;
                x.Problem = $"declared length {x.DeclaredLength}, {t.Length - start} bytes left";
                return x;
            }
            int end = start + x.DeclaredLength;
            if (x.DeclaredLength > 0)
            {
                try
                {
                    var spans = PoseMsgPack.MapSpans(t, start, end, out int after);
                    PoseMsgPack.TryMapHeader(t, start, end, out x.mapCount, out int first);
                    x.mapHeaderRaw = KKExEntry.Slice(t, start, first);
                    foreach (var s in spans) x.Entries.Add(KKExEntry.FromSpans(t, s));
                    if (after < end) { x.BlockTail = KKExEntry.Slice(t, after, end); x.Problem = (end - after) + " bytes after the map inside the block"; }
                }
                catch (PoseMsgPackException e)
                {
                    x.Entries.Clear();
                    x.Status = KKExStatus.Malformed;
                    x.Problem = e.Message;
                    return x;
                }
            }
            if (end < t.Length)
            {
                x.TrailingBytes = KKExEntry.Slice(t, end, t.Length);
                x.Problem = (x.Problem != null ? x.Problem + "; " : "") + (t.Length - end) + " bytes after the block";
            }
            x.Status = KKExStatus.Ok;
            return x;
        }

        /// <summary>
        /// The trailer bytes rebuilt from the view: marker, Version, length, map of Entries (raw key and value bytes),
        /// BlockTail, TrailingBytes. Unchanged → identical to the bytes read. Status other than Ok → Raw unchanged.
        /// </summary>
        public byte[] Encode()
        {
            if (Status != KKExStatus.Ok) return (byte[])Raw.Clone();
            var body = new PoseWriter(Math.Max(64, DeclaredLength + 16));
            if (Entries.Count > 0 || mapHeaderRaw != null || BlockTail != null)
            {
                if (mapHeaderRaw != null && mapCount == Entries.Count) body.Bytes(mapHeaderRaw);
                else PoseMsgPack.WriteMapHeader(body, Entries.Count);
                foreach (var e in Entries) { body.Bytes(e.RawKey); body.Bytes(e.RawValue); }
                body.Bytes(BlockTail);
            }
            var w = new PoseWriter(body.Length + 32);
            w.Bytes(markerRaw ?? RawString.Encode(Marker));
            w.Int32(Version);
            w.Int32(body.Length);
            w.Bytes(body.ToArray());
            w.Bytes(TrailingBytes);
            return w.ToArray();
        }

        /// <summary>A new, empty trailer (version 3) to which entries can be added before Encode().</summary>
        public static KKExTrailer CreateEmpty(int version = 3) => new KKExTrailer { Status = KKExStatus.Ok, Version = version, Raw = new byte[0] };

        public override string ToString() => $"KKEx {Status} v{Version} {Entries.Count} entries ({Raw.Length} bytes){(Problem != null ? ": " + Problem : "")}";
    }
}
