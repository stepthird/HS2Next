// HS2 (AIS) character card: PNG image followed by a binary trailer.
// Trailer: int product, string marker "【AIS_Chara】", string version, int language,
// string userID, string dataID, int headerLen, msgpack {lstInfo:[{name,version,pos,size}]},
// long dataLen, then the blocks. Custom = 3 length-prefixed msgpack chunks (face, body, hair);
// Coordinate = 2 chunks (clothes, accessory). KKEx holds plugin data (ExtensibleSaveFormat);
// the typed plugin decoders are in HS2Next.Data.Ext (CardExtensions.Decode).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HS2Next.IO;

namespace HS2Next.Data
{
    public class Card
    {
        public string Path, Marker, Version, UserID, DataID;
        public int Product, Language;
        public object Face, Body, Hair, Clothes, Accessory, Parameter, Status, KKEx;
        /// <summary>The undecoded KKEx block bytes (for verbatim copies of plugin data), or null.</summary>
        public byte[] KKExRaw;
        /// <summary>The KKEx block's version string from the block header ("3" for current ExtensibleSaveFormat).</summary>
        public string KKExVersion;
        public Dictionary<string, object> Blocks = new Dictionary<string, object>();
        public List<ResolveInfo> Resolve = new List<ResolveInfo>();

        public string Name => Parameter.Get("fullname").Str("");

        /// <summary>Sideloader resolve entry. Property names as listed (accessory slots ≥ 20 use the same
        /// "[outfit.]accessory&lt;N&gt;.ChaFileAccessory.PartsInfo.id" form; see <see cref="AccessorySlot"/>).</summary>
        public class ResolveInfo
        {
            public string GUID, Property;
            public int Slot, LocalSlot, Category;
            /// <summary>Optional manifest fields (error display only); null when absent.</summary>
            public string Author, Website, Name;

            const string AccPrefix = "accessory", AccSuffix = ".ChaFileAccessory.PartsInfo.id", Outfit = "outfit.";

            /// <summary>The absolute accessory slot named by <see cref="Property"/> (0..19 vanilla, ≥ 20 MoreAccessories), or -1.</summary>
            public int AccessorySlot => TryParseAccessorySlot(Property, out int s, out _) ? s : -1;

            /// <summary>Parses "[outfit.]accessory&lt;N&gt;.ChaFileAccessory.PartsInfo.id".</summary>
            public static bool TryParseAccessorySlot(string property, out int slot, out bool outfitPrefix)
            {
                slot = -1; outfitPrefix = false;
                if (property == null) return false;
                int i = 0;
                if (property.StartsWith(Outfit, StringComparison.Ordinal)) { outfitPrefix = true; i = Outfit.Length; }
                if (string.CompareOrdinal(property, i, AccPrefix, 0, AccPrefix.Length) != 0) return false;
                i += AccPrefix.Length;
                int j = i, n = 0;
                while (j < property.Length && property[j] >= '0' && property[j] <= '9' && j - i < 6) { n = n * 10 + (property[j] - '0'); j++; }
                if (j == i || property.Length - j != AccSuffix.Length || string.CompareOrdinal(property, j, AccSuffix, 0, AccSuffix.Length) != 0) return false;
                slot = n;
                return true;
            }

            /// <summary>The property name of accessory slot <paramref name="slot"/> (character cards use the "outfit." prefix, coordinate cards do not).</summary>
            public static string AccessoryProperty(int slot, bool outfitPrefix) =>
                (outfitPrefix ? Outfit : "") + AccPrefix + slot.ToString(System.Globalization.CultureInfo.InvariantCulture) + AccSuffix;

            public override string ToString() => $"{Property} cat={Category} {GUID}#{Slot}";
        }

        public const string ResolverId = "com.bepis.sideloader.universalautoresolver";
        public const string LegacyResolverId = "EC.Core.Sideloader.UniversalAutoResolver";

        public static Card Load(string path) => Load(File.ReadAllBytes(path), path);

        /// <summary>Parses card bytes already in memory (<paramref name="path"/> is informational).</summary>
        public static Card Load(byte[] data, string path)
        {
            int start = PngEnd(data);
            if (start < 0) throw new InvalidDataException("not a PNG card");
            using (var br = new BinaryReader(new MemoryStream(data, start, data.Length - start)))
            {
                var c = new Card { Path = path };
                c.Product = br.ReadInt32();
                c.Marker = br.ReadString();
                if (c.Marker == null || !c.Marker.Contains("AIS_Chara")) throw new InvalidDataException($"not an HS2/AI character card (marker \"{c.Marker}\")");
                c.Version = br.ReadString();
                c.Language = br.ReadInt32();
                c.UserID = br.ReadString();
                c.DataID = br.ReadString();
                int hlen = br.ReadInt32();
                var header = MsgPack.Read(br.ReadBytes(hlen));
                br.ReadInt64();
                long basePos = br.BaseStream.Position;
                foreach (var bi in header.Get("lstInfo").Items())
                {
                    br.BaseStream.Position = basePos + Convert.ToInt64(bi.Get("pos"));
                    var raw = br.ReadBytes(bi.Get("size").Int());
                    string name = bi.Get("name").Str();
                    if (name == "KKEx") { c.KKExRaw = raw; c.KKExVersion = bi.Get("version").Str(); }
                    switch (name)
                    {
                        case "Custom":
                            var cu = Chunks(raw); c.Face = cu[0]; c.Body = cu[1]; c.Hair = cu[2]; break;
                        case "Coordinate":
                            var co = Chunks(raw); c.Clothes = co[0]; c.Accessory = co[1]; break;
                        default:
                            try { c.Blocks[name] = MsgPack.Read(raw); } catch { c.Blocks[name] = raw; }
                            break;
                    }
                }
                c.Blocks.TryGetValue("Parameter", out c.Parameter);
                c.Blocks.TryGetValue("Status", out c.Status);
                c.Blocks.TryGetValue("KKEx", out c.KKEx);
                c.ReadResolveInfo();
                return c;
            }
        }

        static List<object> Chunks(byte[] raw)
        {
            var list = new List<object>();
            int p = 0;
            while (p + 4 <= raw.Length)
            {
                int n = BitConverter.ToInt32(raw, p); p += 4;
                int q = p;
                list.Add(MsgPack.Read(raw, ref q));
                p += n;
            }
            return list;
        }

        void ReadResolveInfo()
        {
            ReadResolveInfo(KKEx, Resolve);
        }

        /// <summary>Reads the Sideloader resolve entries of a KKEx map (current id, else the legacy id) into <paramref name="into"/>.</summary>
        public static void ReadResolveInfo(object kkex, List<ResolveInfo> into)
        {
            var r = kkex.Get(ResolverId) ?? kkex.Get(LegacyResolverId);
            var info = r.At(1).Get("info");
            foreach (var x in info.Items())
            {
                var o = x is byte[] b ? MsgPack.Read(b) : x;
                if (!(o is Dictionary<object, object>)) continue;
                into.Add(new ResolveInfo
                {
                    GUID = o.Get("ModID").Str(), Property = o.Get("Property").Str(),
                    Slot = o.Get("Slot").Int(), LocalSlot = o.Get("LocalSlot").Int(), Category = o.Get("CategoryNo").Int(),
                    Author = o.Get("Author").Str(), Website = o.Get("Website").Str(), Name = o.Get("Name").Str(),
                });
            }
        }

        /// <summary>True when the data starts with the PNG signature.</summary>
        public static bool HasPng(byte[] d) => d.Length >= 8 && d[0] == 0x89 && d[1] == (byte)'P' && d[2] == (byte)'N' && d[3] == (byte)'G';

        /// <summary>Offset just past the PNG image (after the IEND chunk's CRC), walking the chunk list; falls back to
        /// searching "IEND" when the chunk structure is broken (-1 if none). Data without a PNG signature (e.g. Maker
        /// autosaves, which HS2 writes without a thumbnail) starts at 0.</summary>
        public static int PngEnd(byte[] d)
        {
            if (!HasPng(d)) return 0;
            {
                long p = 8;
                while (p + 12 <= d.Length)
                {
                    long len = ((long)d[p] << 24) | ((long)d[p + 1] << 16) | ((long)d[p + 2] << 8) | d[p + 3];
                    bool iend = d[p + 4] == (byte)'I' && d[p + 5] == (byte)'E' && d[p + 6] == (byte)'N' && d[p + 7] == (byte)'D';
                    p += 12 + len;
                    if (iend) return p <= d.Length ? (int)p : -1;
                }
            }
            int i = IndexOf(d, Encoding.ASCII.GetBytes("IEND"));
            return i < 0 ? -1 : i + 8;
        }

        /// <summary>Plugin data block by ID (ExtensibleSaveFormat): returns the data dictionary or null.</summary>
        public object Ext(string id) => KKEx.Get(id).At(1);

        /// <summary>Find the resolver entry for a card property (e.g. "outfit.ChaFileClothes.ClothesTop").</summary>
        public ResolveInfo FindResolve(string property, int category)
        {
            foreach (var r in Resolve) if (r.Property == property && r.Category == category) return r;
            return null;
        }

        static int IndexOf(byte[] d, byte[] pat)
        {
            for (int i = 0; i <= d.Length - pat.Length; i++)
            {
                int j = 0;
                while (j < pat.Length && d[i + j] == pat[j]) j++;
                if (j == pat.Length) return i;
            }
            return -1;
        }
    }
}
