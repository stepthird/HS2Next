// Façade: decodes all plugin extension data (KKEx) of a character or coordinate card into typed data classes
// (native fields) and keeps every other plugin as an opaque, verbatim block.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    /// <summary>A plugin whose data is not decoded: kept as the original MessagePack bytes of its PluginData
    /// ([version, data] array), for the future ext/&lt;GUID&gt;/data.msgpack blocks.</summary>
    public class OpaqueExt
    {
        /// <summary>The KKEx key (plugin data id).</summary>
        public string Guid;
        /// <summary>PluginData version, or -1 when not an int.</summary>
        public int Version;
        /// <summary>Exact bytes of the PluginData value as stored in the card (re-encodable verbatim).</summary>
        public byte[] Raw;
        /// <summary>The decoded data map (convenience; may be null).</summary>
        public object Data;
        /// <summary>Why it is opaque: null = no native decoder; else the reason a native decoder declined it.</summary>
        public string Reason;
        public override string ToString() => $"{Guid} v{Version} ({Raw?.Length ?? 0} bytes)";
    }

    /// <summary>All extension data of one card.</summary>
    public class CardExtData
    {
        public bool IsCoordinate;
        /// <summary>Sideloader resolve entries (character: "outfit."-prefixed clothes/accessory properties; coordinate: unprefixed).</summary>
        public List<Card.ResolveInfo> Resolve = new List<Card.ResolveInfo>();
        public MoreAccessoriesData MoreAccessories;
        public AbmxData Abmx;
        public UncensorData Uncensor;
        public MaterialEditorData MaterialEditor;
        public SkinOverlayData SkinOverlays;
        public ClothesOverlayData ClothesOverlays;
        public PushUpData PushUp;
        public DynamicBoneEditorData DynamicBoneEditor;
        public InvisibleBodyData InvisibleBody;
        public AdditionalAccessoryControlsData AdditionalAccessoryControls;
        /// <summary>Every other plugin (and native ones whose decoder declined the data), in card order.</summary>
        public List<OpaqueExt> Opaque = new List<OpaqueExt>();
        /// <summary>Every non-nil plugin data id -> PluginData version (-1 when not an int).</summary>
        public Dictionary<string, int> Versions = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Ids present with a nil PluginData (treated as absent).</summary>
        public List<string> NilIds = new List<string>();
        /// <summary>Unknown versions, decode errors and notes ("plugin: message").</summary>
        public List<string> Diagnostics = new List<string>();
        /// <summary>Counters (e.g. "nested.Lz4Block").</summary>
        public Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Offset/length of each id's PluginData in <see cref="KKExRaw"/>.</summary>
        public Dictionary<string, (int Offset, int Length)> Spans = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        public byte[] KKExRaw;

        /// <summary>The verbatim PluginData bytes of any id (native or opaque); empty when absent.</summary>
        public ArraySegment<byte> RawPluginData(string id) =>
            KKExRaw != null && Spans.TryGetValue(id, out var s) ? new ArraySegment<byte>(KKExRaw, s.Offset, s.Length) : new ArraySegment<byte>(Array.Empty<byte>());

        public bool Has(string id) => Versions.ContainsKey(id);
    }

    public static class CardExtensions
    {
        /// <summary>When non-null, decode time per plugin id is accumulated here (Stopwatch ticks).</summary>
        public static Dictionary<string, long> ProfileTicks;

        public static CardExtData Decode(Card card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            var r = new CardExtData { IsCoordinate = false };
            if (card.KKEx != null && card.KKExVersion != null && card.KKExVersion != "3")
            {
                r.Diagnostics.Add($"KKEx: block version \"{card.KKExVersion}\" (ExtensibleSaveFormat reads only \"3\"); ignored");
                return r;
            }
            Fill(r, card.KKEx, card.KKExRaw, card.Resolve);
            return r;
        }

        public static CardExtData Decode(CoordCard coord)
        {
            if (coord == null) throw new ArgumentNullException(nameof(coord));
            var r = new CardExtData { IsCoordinate = true };
            Fill(r, coord.KKEx, coord.KKExRaw, coord.Resolve);
            return r;
        }

        static void Fill(CardExtData r, object kkex, byte[] raw, List<Card.ResolveInfo> resolve)
        {
            r.KKExRaw = raw;
            r.Resolve = resolve ?? new List<Card.ResolveInfo>();
            if (!(kkex is Dictionary<object, object> map)) return;
            if (raw != null)
            {
                try { ReadSpans(raw, r.Spans); }
                catch (Exception e) { r.Diagnostics.Add($"KKEx: span scan failed ({e.Message}); opaque blocks keep no raw bytes"); r.Spans.Clear(); }
            }
            var ctx = new ExtContext { IsCoordinate = r.IsCoordinate };
            var sw = ProfileTicks != null ? Stopwatch.StartNew() : null;
            foreach (var kv in map)
            {
                if (!(kv.Key is string id)) { ctx.Warn("KKEx", $"non-string key {kv.Key}"); continue; }
                if (kv.Value == null) { r.NilIds.Add(id); continue; }
                var pd = kv.Value as List<object>;
                if (pd == null || pd.Count < 1) { ctx.Warn(id, "PluginData is not an array"); AddOpaque(r, id, -1, null, "malformed PluginData"); continue; }
                int version = V.IN(pd[0]) ?? -1;
                object data = pd.Count > 1 ? pd[1] : null;
                r.Versions[id] = version;
                long t0 = sw?.ElapsedTicks ?? 0;
                string declined = null;
                try
                {
                    switch (id)
                    {
                        case PluginIds.Resolver: case PluginIds.ResolverLegacy: break; // read by Card/CoordCard
                        case PluginIds.MoreAccessories: r.MoreAccessories = MoreAccessoriesData.Decode(version, data, ctx); break;
                        case PluginIds.Abmx: if ((r.Abmx = AbmxData.Decode(version, data, ctx)) == null) declined = "unsupported version"; break;
                        case PluginIds.Uncensor: r.Uncensor = UncensorData.Decode(version, data, ctx); break;
                        case PluginIds.MaterialEditor: r.MaterialEditor = MaterialEditorData.Decode(version, data, ctx); break;
                        case PluginIds.Ksox: if ((r.SkinOverlays = SkinOverlayData.Decode(version, data, ctx)) == null) declined = "unsupported version"; break;
                        case PluginIds.Kcox: if ((r.ClothesOverlays = ClothesOverlayData.Decode(version, data, ctx)) == null) declined = "unsupported version"; break;
                        case PluginIds.PushUp: r.PushUp = PushUpData.Decode(version, data, ctx); break;
                        case PluginIds.DynamicBoneEditor: r.DynamicBoneEditor = DynamicBoneEditorData.Decode(version, data, ctx); break;
                        case PluginIds.InvisibleBody: r.InvisibleBody = InvisibleBodyData.Decode(version, data, ctx); break;
                        case PluginIds.AdditionalAccessoryControls: r.AdditionalAccessoryControls = AdditionalAccessoryControlsData.Decode(version, data, ctx); break;
                        default: AddOpaque(r, id, version, data, null); break;
                    }
                }
                catch (Exception e)
                {
                    ctx.Warn(id, $"decode error {e.GetType().Name}: {e.Message}");
                    declined = "decode error";
                }
                if (declined != null) AddOpaque(r, id, version, data, declined);
                if (sw != null)
                {
                    ProfileTicks.TryGetValue(id, out long acc);
                    ProfileTicks[id] = acc + sw.ElapsedTicks - t0;
                }
            }
            r.Diagnostics.AddRange(ctx.Diagnostics);
            foreach (var kv in ctx.Counts) r.Counts[kv.Key] = kv.Value;
        }

        static void AddOpaque(CardExtData r, string id, int version, object data, string reason)
        {
            byte[] bytes = null;
            if (r.KKExRaw != null && r.Spans.TryGetValue(id, out var s))
            {
                bytes = new byte[s.Length];
                Buffer.BlockCopy(r.KKExRaw, s.Offset, bytes, 0, s.Length);
            }
            r.Opaque.Add(new OpaqueExt { Guid = id, Version = version, Raw = bytes, Data = data, Reason = reason });
        }

        /// <summary>Byte span of every value of the top-level KKEx map.</summary>
        public static void ReadSpans(byte[] d, Dictionary<string, (int, int)> into)
        {
            int p = 0;
            byte b = d[p++];
            int n;
            if ((b & 0xf0) == 0x80) n = b & 0x0f;
            else if (b == 0xde) { n = (d[p] << 8) | d[p + 1]; p += 2; }
            else if (b == 0xdf) { n = (int)(((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3]); p += 4; }
            else throw new FormatException("KKEx is not a map");
            for (int i = 0; i < n; i++)
            {
                var key = MsgPack.Read(d, ref p);
                int start = p;
                MsgPack.Skip(d, ref p);
                if (key is string s) into[s] = (start, p - start);
            }
        }
    }
}
