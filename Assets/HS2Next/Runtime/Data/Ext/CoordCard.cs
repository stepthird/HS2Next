// HS2 (AIS) coordinate card: PNG image, then
// int product, string marker "【AIS_Clothes】", string version, int language, string coordinateName,
// int n + n bytes holding two int-length-prefixed MessagePack objects (ChaFileClothes, ChaFileAccessory),
// then the ExtensibleSaveFormat trailer: string "KKEx", int version (3), int length, MessagePack map id -> PluginData.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    public class CoordCard
    {
        public string Path, Marker, Version, Name;
        public int Product, Language;
        /// <summary>ChaFileClothes / ChaFileAccessory as MessagePack object trees (same objects as a character card's Coordinate block).</summary>
        public object Clothes, Accessory;
        /// <summary>The trailer's id -> PluginData map, or null when the coordinate has no extended data.</summary>
        public object KKEx;
        /// <summary>Undecoded trailer map bytes, or null.</summary>
        public byte[] KKExRaw;
        /// <summary>Trailer data version (3), or -1 without a trailer.</summary>
        public int KKExVersion = -1;
        /// <summary>Sideloader resolve entries (properties without the "outfit." prefix).</summary>
        public List<Card.ResolveInfo> Resolve = new List<Card.ResolveInfo>();

        /// <summary>Plugin data block by id: the data map, or null.</summary>
        public object Ext(string id) => KKEx.Get(id).At(1);

        public const string MarkerText = "AIS_Clothes";

        /// <exception cref="InvalidDataException">Not a coordinate card or truncated.</exception>
        public static CoordCard Load(string path) => Load(File.ReadAllBytes(path), path);

        public static CoordCard Load(byte[] data, string path = null)
        {
            int start = Card.PngEnd(data);
            if (start < 0) throw new InvalidDataException("not a PNG card");
            var c = new CoordCard { Path = path };
            try
            {
                using (var br = new BinaryReader(new MemoryStream(data, start, data.Length - start)))
                {
                    c.Product = br.ReadInt32();
                    c.Marker = br.ReadString();
                    if (c.Marker == null || !c.Marker.Contains(MarkerText)) throw new InvalidDataException($"not an HS2/AI coordinate card (marker \"{c.Marker}\")");
                    c.Version = br.ReadString();
                    c.Language = br.ReadInt32();
                    c.Name = br.ReadString();
                    int n = br.ReadInt32();
                    var body = br.ReadBytes(n);
                    if (body.Length != n) throw new InvalidDataException("coordinate body truncated");
                    int p = 0;
                    var parts = new object[2];
                    for (int i = 0; i < 2; i++)
                    {
                        if (p + 4 > body.Length) throw new InvalidDataException("coordinate body: missing object " + i);
                        int len = BitConverter.ToInt32(body, p); p += 4;
                        if (len < 0 || p + len > body.Length) throw new InvalidDataException("coordinate body: bad object length");
                        int q = p;
                        parts[i] = MsgPack.Read(body, ref q);
                        p += len;
                    }
                    c.Clothes = parts[0];
                    c.Accessory = parts[1];
                    ReadTrailer(br, c);
                }
            }
            catch (EndOfStreamException) { throw new InvalidDataException("coordinate card truncated"); }
            catch (IndexOutOfRangeException) { throw new InvalidDataException("coordinate card truncated (MessagePack)"); }
            Card.ReadResolveInfo(c.KKEx, c.Resolve);
            return c;
        }

        // ESF reads the trailer only when the marker is "KKEx", the version is 3 and the length > 0.
        static void ReadTrailer(BinaryReader br, CoordCard c)
        {
            var s = br.BaseStream;
            if (s.Length - s.Position < 5) return;
            string marker;
            try { marker = br.ReadString(); } catch (Exception) { return; }
            if (marker != "KKEx" || s.Length - s.Position < 8) return;
            int ver = br.ReadInt32();
            int len = br.ReadInt32();
            if (ver != 3 || len <= 0 || len > s.Length - s.Position) return;
            c.KKExVersion = ver;
            c.KKExRaw = br.ReadBytes(len);
            c.KKEx = MsgPack.Read(c.KKExRaw);
        }
    }
}
