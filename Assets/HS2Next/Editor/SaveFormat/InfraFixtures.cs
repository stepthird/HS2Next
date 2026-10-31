// D1 test helpers: synthetic containers (no Illusion data; refs like test.mod#1), a raw ZIP builder
// for crafted (malformed) archives, repacking of saved files, fake ext handlers, fault-injecting
// streams and ZIP header parsing for the field checks.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using HS2Next.Api.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save.Tests
{
    public static class InfraFixtures
    {
        public static readonly DateTime FixedTime = new DateTime(2026, 10, 16, 1, 2, 3, DateTimeKind.Utc);

        public static SaveOptions Opts(ExtRegistry reg = null) => new SaveOptions
        {
            Clock = () => FixedTime,
            Thumbnail = ThumbnailMode.Placeholder,
            Registry = reg ?? new ExtRegistry(),
        };

        public static LoadOptions LOpts(ExtRegistry reg = null) => new LoadOptions { Registry = reg ?? new ExtRegistry() };

        /// <summary>A deterministic small PNG-signature blob (not a real image; bytes only).</summary>
        public static byte[] FakePng(int seed, int size = 256)
        {
            var b = new byte[size];
            new Random(seed).NextBytes(b);
            Array.Copy(PngInfo.Signature, b, 8);
            return b;
        }

        public static string Id(int n) => n.ToString("x32");

        public static CharaContainer SmallChara(int accessories = 3)
        {
            var c = new CharaContainer();
            c.Chara.Profile.Name = "Test Chara";
            c.Chara.Profile.Sex = 1;
            c.Chara.Face.Shape[0] = 0.25f;
            for (int i = 0; i < accessories; i++)
                c.Coord.Accessories.Add(new AccessoryEntry { Id = Id(100 + i), Category = 352, Item = "test.mod#" + (i + 1), Parent = "N_Earring_L" });
            return c;
        }

        public static CoordContainer SmallCoord()
        {
            var c = new CoordContainer();
            c.Coord.Name = "Test Coord";
            c.Coord.Accessories.Add(new AccessoryEntry { Id = Id(200), Category = 353, Item = "test.mod#7" });
            return c;
        }

        public static SceneContainer SmallScene(int charas, int items, CharaContainer chara = null)
        {
            var s = new SceneContainer { Title = "Test Scene" };
            for (int i = 0; i < charas; i++)
            {
                string id = Id(1000 + i);
                s.Scene.Nodes.Add(new SceneNode(id, new CharaNodeState()) { Order = i });
                s.PlaceCharacter(id, chara ?? SmallChara());
            }
            for (int i = 0; i < items; i++)
                s.Scene.Nodes.Add(new SceneNode(Id(5000 + i), new ItemNodeState { Ref = "test.mod#" + (i % 7 + 1) }) { Order = charas + i });
            return s;
        }

        // ---------- raw archive access ----------

        public static ushort U16(byte[] b, long o) => (ushort)(b[o] | b[o + 1] << 8);
        public static uint U32(byte[] b, long o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);

        public sealed class CdEntry
        {
            public string Name;
            public int MadeBy, Needed, Flags, Method, Time, Date, ExtraLen, CommentLen, IntAttr;
            public uint ExtAttr, Crc, CSize, USize, Lho;
            public int LocalFlags, LocalMethod, LocalTime, LocalDate, LocalExtraLen, LocalNeeded;
        }

        /// <summary>Parses the central directory and local headers of an archive written by ContainerWriter (absolute offsets).</summary>
        public static List<CdEntry> ParseZip(byte[] f, out long eocd, out uint cdOffset, out uint cdSize, out int commentLen)
        {
            eocd = f.Length - 22;
            while (eocd >= 0 && U32(f, eocd) != 0x06054B50) eocd--;
            cdSize = U32(f, eocd + 12);
            cdOffset = U32(f, eocd + 16);
            commentLen = U16(f, eocd + 20);
            int n = U16(f, eocd + 10);
            var list = new List<CdEntry>();
            long p = cdOffset;
            for (int i = 0; i < n; i++)
            {
                var e = new CdEntry
                {
                    MadeBy = U16(f, p + 4), Needed = U16(f, p + 6), Flags = U16(f, p + 8), Method = U16(f, p + 10),
                    Time = U16(f, p + 12), Date = U16(f, p + 14), Crc = U32(f, p + 16), CSize = U32(f, p + 20), USize = U32(f, p + 24),
                    ExtraLen = U16(f, p + 30), CommentLen = U16(f, p + 32), IntAttr = U16(f, p + 36), ExtAttr = U32(f, p + 38), Lho = U32(f, p + 42),
                };
                int nl = U16(f, p + 28);
                e.Name = Encoding.UTF8.GetString(f, (int)p + 46, nl);
                p += 46 + nl + e.ExtraLen + e.CommentLen;
                long l = e.Lho;
                e.LocalNeeded = U16(f, l + 4);
                e.LocalFlags = U16(f, l + 6);
                e.LocalMethod = U16(f, l + 8);
                e.LocalTime = U16(f, l + 10);
                e.LocalDate = U16(f, l + 12);
                e.LocalExtraLen = U16(f, l + 28);
                list.Add(e);
            }
            return list;
        }

        /// <summary>The uncompressed entries of a container (via the reader).</summary>
        public static List<KeyValuePair<string, byte[]>> Entries(byte[] file)
        {
            var r = ContainerReader.Read(file);
            var l = new List<KeyValuePair<string, byte[]>>();
            foreach (var e in r.Entries) l.Add(new KeyValuePair<string, byte[]>(e.Name, e.Data));
            return l;
        }

        /// <summary>
        /// Rebuilds a container with ContainerWriter after <paramref name="edit"/> changed the entry map (name → bytes;
        /// null value = remove). The thumbnail is kept.
        /// </summary>
        public static byte[] Repack(byte[] file, Action<SortedDictionary<string, byte[]>> edit, string main = "chara.json")
        {
            var r = ContainerReader.Read(file);
            var map = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var e in r.Entries) map[e.Name] = e.Data;
            edit(map);
            var w = new ContainerWriter { Thumbnail = r.Thumbnail, MainDocument = main };
            foreach (var kv in map) if (kv.Value != null) w.Add(kv.Key, kv.Value);
            return w.ToBytes();
        }

        /// <summary>Edits a JSON entry as a JObject and writes it back canonically.</summary>
        public static void EditJson(SortedDictionary<string, byte[]> map, string name, Action<JObject> edit)
        {
            var j = DocReader.Parse(map[name], name);
            edit(j);
            map[name] = CanonicalJsonWriter.ToBytes(j, name);
        }

        public static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

        // ---------- crafted archives ----------

        public sealed class RawItem
        {
            public byte[] Name;
            public byte[] Data = new byte[0];
            public int Method;            // 0 stored, 8 deflate, anything else written as stored bytes
            public int Flags;
            public uint? CrcOverride;
            public uint? USizeOverride, CSizeOverride, LhoOverride;
        }

        public static RawItem Item(string name, string text, int method = 0) => new RawItem { Name = Encoding.UTF8.GetBytes(name), Data = Utf8(text), Method = method };

        /// <summary>A hand-made ZIP (no ZipArchive), for malformed cases. <paramref name="prefix"/> is written first with absolute offsets unless <paramref name="relative"/>.</summary>
        public static byte[] RawZip(IList<RawItem> items, byte[] prefix = null, bool relative = false, ushort? countOverride = null, uint? cdOffsetOverride = null)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            if (prefix != null) bw.Write(prefix);
            long baseOff = relative && prefix != null ? prefix.Length : 0;
            var lhos = new List<long>();
            var comp = new List<byte[]>();
            foreach (var it in items)
            {
                byte[] payload = it.Data;
                if (it.Method == 8)
                {
                    var c = new MemoryStream();
                    using (var ds = new DeflateStream(c, CompressionLevel.Optimal, true)) ds.Write(it.Data, 0, it.Data.Length);
                    payload = c.ToArray();
                }
                comp.Add(payload);
                lhos.Add(ms.Position - baseOff);
                bw.Write(0x04034B50u); bw.Write((ushort)20); bw.Write((ushort)it.Flags); bw.Write((ushort)it.Method);
                bw.Write((ushort)0); bw.Write((ushort)0x21);
                bw.Write(it.CrcOverride ?? Crc32.Compute(it.Data));
                bw.Write(it.CSizeOverride ?? (uint)payload.Length); bw.Write(it.USizeOverride ?? (uint)it.Data.Length);
                bw.Write((ushort)it.Name.Length); bw.Write((ushort)0);
                bw.Write(it.Name);
                bw.Write(payload);
            }
            long cdStart = ms.Position - baseOff;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bw.Write(0x02014B50u); bw.Write((ushort)20); bw.Write((ushort)20); bw.Write((ushort)it.Flags); bw.Write((ushort)it.Method);
                bw.Write((ushort)0); bw.Write((ushort)0x21);
                bw.Write(it.CrcOverride ?? Crc32.Compute(it.Data));
                bw.Write(it.CSizeOverride ?? (uint)comp[i].Length); bw.Write(it.USizeOverride ?? (uint)it.Data.Length);
                bw.Write((ushort)it.Name.Length); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)0);
                bw.Write(0u); bw.Write(it.LhoOverride ?? (uint)lhos[i]);
                bw.Write(it.Name);
            }
            long cdEnd = ms.Position - baseOff;
            ushort count = countOverride ?? (ushort)items.Count;
            bw.Write(0x06054B50u); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write(count); bw.Write(count);
            bw.Write((uint)(cdEnd - cdStart)); bw.Write(cdOffsetOverride ?? (uint)cdStart); bw.Write((ushort)0);
            bw.Flush();
            return ms.ToArray();
        }

        /// <summary>A minimal valid manifest for crafted archives.</summary>
        public static string ManifestJson(string format = "hs2next.chara", int formatVersion = 1, int minReader = 1) =>
            "{\"format\":\"" + format + "\",\"formatVersion\":" + formatVersion + ",\"minReaderVersion\":" + minReader +
            ",\"prerelease\":null,\"app\":{\"name\":\"HS2Next\",\"version\":\"0.1.0\"},\"createdUtc\":\"2026-10-16T01:02:03Z\",\"modifiedUtc\":\"2026-10-16T01:02:03Z\"}";

        /// <summary>A synthetic HS2 card / coordinate header: [PNG] + int32 productNo + .NET string marker + junk.</summary>
        public static byte[] Hs2Card(string marker, bool png)
        {
            var ms = new MemoryStream();
            if (png) { var p = ThumbnailPlaceholder.SolidRgb(4, 4, 1, 2, 3); ms.Write(p, 0, p.Length); }
            var bw = new BinaryWriter(ms, Encoding.UTF8);
            bw.Write(100);
            bw.Write(marker);             // 7-bit length prefix + UTF-8, as .NET's BinaryWriter.Write(string)
            bw.Write("1.0.0");
            bw.Write(new byte[200]);
            bw.Flush();
            return ms.ToArray();
        }

        // ---------- streams and handlers ----------

        /// <summary>Passes writes through until <see cref="Limit"/> bytes, then throws IOException.</summary>
        public sealed class FaultStream : Stream
        {
            readonly Stream inner;
            public long Limit;
            long written;

            public FaultStream(Stream inner, long limit) { this.inner = inner; Limit = limit; }

            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => true;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (written + count > Limit) throw new IOException("injected fault after " + Limit + " bytes");
                written += count;
                inner.Write(buffer, offset, count);
            }
        }

        /// <summary>A scripted ext handler that records calls.</summary>
        public sealed class FakeHandler : IExtBlockHandler
        {
            public string Namespace { get; set; }
            public int Version { get; set; } = 1;
            public ExtScope Scopes { get; set; } = ExtScope.Chara | ExtScope.Coord | ExtScope.Scene | ExtScope.Node;
            public bool RequiredByDefault { get; set; }
            public readonly List<string> Log;
            public Action<IExtReadContext> OnRead;
            public Action<IExtWriteContext> OnWrite;

            public FakeHandler(string ns, List<string> log) { Namespace = ns; Log = log; }

            public void Read(IExtReadContext ctx)
            {
                Log.Add("R " + Namespace + " " + ctx.Scope + " " + (ctx.OwnerId ?? "-") + " v" + ctx.StoredVersion + " json=" + (ctx.Json ?? "null").Replace("\n", "").Replace(" ", "") +
                        " acc=" + ctx.AccessoryJson.Count + " files=" + string.Join(",", ctx.Files));
                OnRead?.Invoke(ctx);
            }

            public void Write(IExtWriteContext ctx)
            {
                Log.Add("W " + Namespace + " " + ctx.Scope + " " + (ctx.OwnerId ?? "-"));
                OnWrite?.Invoke(ctx);
            }
        }
    }
}
