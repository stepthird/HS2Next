// Source validation of HS2 character and coordinate cards: the bytes after the PNG
// (product number, marker, version) decide the kind and whether the file is accepted; the character card's block
// header gives the block versions, the coordinate's tail the ExtensibleSaveFormat trailer. The Phase 1 readers
// (Card, CoordCard) parse the content afterwards.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Data;
using HS2Next.IO;

namespace HS2Next.Import
{
    sealed class Hs2Header
    {
        public Hs2SourceKind Kind;
        public int Product;
        public string Marker, Version;
        /// <summary>Character card: block name → version string, in header order.</summary>
        public readonly List<KeyValuePair<string, string>> Blocks = new List<KeyValuePair<string, string>>();
        /// <summary>Coordinate card: the trailer marker, version and data when it is not the one ExtensibleSaveFormat reads.</summary>
        public string TrailerMarker;
        public int TrailerVersion = -1;
        public byte[] OtherTrailer;
        /// <summary>Error code (`card.notACard`, `card.badProduct`, `card.newerVersion`, `card.badVersion`, `card.truncated`) or null.</summary>
        public string Error, ErrorMessage;

        public const string CharaMarker = "AIS_Chara", CoordMarker = "AIS_Clothes";
        public static readonly System.Version MaxCharaVersion = new System.Version(1, 0, 0), MaxCoordVersion = new System.Version(0, 0, 0);

        /// <summary>Current block versions (`ChaFileDefine`): a block with a newer version is skipped, as HS2 does.</summary>
        public static readonly Dictionary<string, System.Version> CurrentBlockVersions = new Dictionary<string, System.Version>(StringComparer.Ordinal)
        {
            ["Custom"] = new System.Version(0, 0, 0), ["Coordinate"] = new System.Version(0, 0, 0), ["Parameter"] = new System.Version(0, 0, 1),
            ["GameInfo"] = new System.Version(0, 0, 0), ["Status"] = new System.Version(0, 0, 0), ["Parameter2"] = new System.Version(0, 0, 0),
            ["GameInfo2"] = new System.Version(0, 0, 0),
        };

        Hs2Header Fail(string code, string message) { Error = code; ErrorMessage = message; return this; }

        /// <summary>Only the kind (no validation beyond the marker).</summary>
        public static Hs2SourceKind Detect(byte[] data)
        {
            if (data == null) return Hs2SourceKind.None;
            int start = Card.PngEnd(data);
            if (start < 0 || data.Length - start < 5) return Hs2SourceKind.None;
            try
            {
                using (var br = new BinaryReader(new MemoryStream(data, start, data.Length - start)))
                {
                    br.ReadInt32();
                    string m = br.ReadString();
                    return m.Contains(CharaMarker) ? Hs2SourceKind.Chara : m.Contains(CoordMarker) ? Hs2SourceKind.Coord : Hs2SourceKind.None;
                }
            }
            catch (Exception) { return Hs2SourceKind.None; }
        }

        public static Hs2Header Read(byte[] data)
        {
            var h = new Hs2Header();
            int start = Card.PngEnd(data);
            if (start < 0) return h.Fail("card.notACard", "PNG without an IEND chunk");
            var ms = new MemoryStream(data, start, data.Length - start);
            using (var br = new BinaryReader(ms))
            {
                try
                {
                    h.Product = br.ReadInt32();
                    h.Marker = br.ReadString();
                }
                catch (Exception) { return h.Fail("card.notACard", "no HS2 card header after the image"); }
                h.Kind = h.Marker.Contains(CharaMarker) ? Hs2SourceKind.Chara : h.Marker.Contains(CoordMarker) ? Hs2SourceKind.Coord : Hs2SourceKind.None;
                if (h.Kind == Hs2SourceKind.None) return h.Fail("card.notACard", $"marker \"{h.Marker}\" is neither a character nor a coordinate card");
                try
                {
                    if (h.Product > 100) return h.Fail("card.badProduct", $"product number {h.Product} > 100 (HS2 refuses it)");
                    h.Version = br.ReadString();
                    if (!System.Version.TryParse(h.Version, out var ver)) return h.Fail("card.badVersion", $"version \"{h.Version}\" is not a version");
                    var max = h.Kind == Hs2SourceKind.Chara ? MaxCharaVersion : MaxCoordVersion;
                    if (ver > max) return h.Fail("card.newerVersion", $"version {h.Version} is newer than {max} (HS2 refuses it)");
                    br.ReadInt32();                                        // language
                    if (h.Kind == Hs2SourceKind.Chara)
                    {
                        br.ReadString(); br.ReadString();                  // userID, dataID
                        int hlen = br.ReadInt32();
                        if (hlen < 0 || hlen > ms.Length - ms.Position) return h.Fail("card.truncated", "block header cut");
                        var header = MsgPack.Read(br.ReadBytes(hlen));
                        long dataLen = br.ReadInt64();
                        long basePos = ms.Position;
                        foreach (var bi in header.Get("lstInfo").Items())
                        {
                            string name = bi.Get("name").Str();
                            long pos = Convert.ToInt64(bi.Get("pos") ?? 0L), size = Convert.ToInt64(bi.Get("size") ?? 0L);
                            if (pos < 0 || size < 0 || basePos + pos + size > ms.Length) return h.Fail("card.truncated", $"block {name} lies outside the file");
                            h.Blocks.Add(new KeyValuePair<string, string>(name, bi.Get("version").Str()));
                        }
                    }
                    else
                    {
                        br.ReadString();                                   // coordinateName
                        int n = br.ReadInt32();
                        if (n < 0 || n > ms.Length - ms.Position) return h.Fail("card.truncated", "coordinate data cut");
                        ms.Position += n;
                        ReadTrailer(br, ms, h);
                    }
                }
                catch (Exception e) when (e is EndOfStreamException || e is IOException || e is FormatException || e is IndexOutOfRangeException || e is ArgumentException || e is OverflowException)
                {
                    return h.Fail("card.truncated", "card data cut or malformed (" + e.GetType().Name + ")");
                }
            }
            return h;
        }

        // ExtensibleSaveFormat reads the trailer only when the marker is "KKEx", the version 3 and the length > 0. A trailer
        // with another version is kept whole as raw data.
        static void ReadTrailer(BinaryReader br, MemoryStream ms, Hs2Header h)
        {
            if (ms.Length - ms.Position < 5) return;
            try
            {
                h.TrailerMarker = br.ReadString();
                if (h.TrailerMarker != "KKEx" || ms.Length - ms.Position < 8) return;
                h.TrailerVersion = br.ReadInt32();
                int len = br.ReadInt32();
                if (h.TrailerVersion != 3 && len > 0 && len <= ms.Length - ms.Position) h.OtherTrailer = br.ReadBytes(len);
            }
            catch (Exception) { }
        }
    }
}
