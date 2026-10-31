// Raw plugin copies: every non-nil KKEx entry and every non-nil per-object
// ExtendedSaveData entry is copied byte for byte into `ext/hs2.kkex/` of its scope (role `source` when a typed section
// holds it, `opaque` otherwise); three ids are dropped; a KKEx block of another version is kept whole.
using System;
using System.Collections.Generic;
using HS2Next.Data.Ext;
using HS2Next.IO;
using HS2Next.Save;

namespace HS2Next.Import
{
    static class KkexImport
    {
        public const string SideloaderObjectId = "com.bepis.bepinex.sideloader";
        public const string Esd = "ExtendedSaveData";

        /// <summary>Not copied: a debug plugin, an always-nil translation helper, an always-empty resolver.</summary>
        public static readonly HashSet<string> Dropped = new HashSet<string>(StringComparer.Ordinal)
        {
            "TestCharaCustomFunctionController", "com.gebo.bepinex.translationhelper.chara", "com.bepis.xloader.universalautoresolver",
        };

        /// <summary>KKEx ids of a character card that belong to the worn coordinate (`coord/ext/hs2.kkex/`).</summary>
        public static readonly HashSet<string> CoordScoped = new HashSet<string>(StringComparer.Ordinal)
        {
            PluginIds.MoreAccessories, PluginIds.Kcox, PluginIds.PushUp, PluginIds.DynamicBoneEditor, PluginIds.AdditionalAccessoryControls,
            "orange.spork.outfitpainter", "mikke.BeaverAI",
        };

        static string Reason(string decoderReason)
        {
            switch (decoderReason)
            {
                case null: return "noDecoder";
                case "unsupported version": return "unsupportedVersion";
                case "decode error": return "decodeError";
                case "malformed PluginData": return "malformed";
                default: return "decodeError";
            }
        }

        public static KkexStore NewStore(string form, string blockVersion) =>
            new KkexStore { Source = form == "coord" ? "hs2.coord" : "hs2.chara", BlockVersion = blockVersion ?? "" };

        /// <summary>The entries of the KKEx map (block or coordinate trailer). <paramref name="coordStore"/> receives the
        /// coordinate-scoped ids of a character card; for a coordinate file both stores are the same.</summary>
        public static void CopyKkex(ImportContext x, CardExtData ext, string form, KkexStore charaStore, KkexStore coordStore)
        {
            if (ext.KKExRaw == null) return;
            var opaque = new Dictionary<string, OpaqueExt>(StringComparer.Ordinal);
            foreach (var o in ext.Opaque) opaque[o.Guid] = o;
            var nil = new HashSet<string>(ext.NilIds, StringComparer.Ordinal);
            if (ext.Spans.Count == 0 && ext.Versions.Count > 0)
            {
                x.Report.Error("kkex.noRawBytes", "ext/hs2.kkex", "the KKEx map could not be scanned; plugin data not copied");
                return;
            }
            foreach (var kv in ext.Spans)
            {
                string id = kv.Key;
                if (nil.Contains(id))
                {
                    x.Report.Info("plugin.nil", id, "nil plugin data (treated as absent)");
                    x.Report.Count("plugin.nil");
                    continue;
                }
                if (Dropped.Contains(id))
                {
                    x.Report.Info("plugin.dropped", id, "plugin data not kept");
                    x.Report.Count("plugin.dropped");
                    continue;
                }
                var seg = ext.RawPluginData(id);
                var bytes = new byte[seg.Count];
                Buffer.BlockCopy(seg.Array, seg.Offset, bytes, 0, seg.Count);
                int? version = ext.Versions.TryGetValue(id, out var v) && v >= 0 ? v : (int?)null;
                bool isOpaque = opaque.TryGetValue(id, out var oe) || !ext.Versions.ContainsKey(id);
                string role = isOpaque ? "opaque" : "source";
                string reason = isOpaque ? (oe != null ? Reason(oe.Reason) : "malformed") : null;
                var store = form == "chara" && CoordScoped.Contains(id) ? coordStore : charaStore;
                store.Add(id, "kkex", null, version, form, role, reason, bytes);
                x.Report.Count(isOpaque ? "kkex.opaque" : "kkex.source");
                if (isOpaque) x.Report.Info("plugin.opaque", id, $"kept as raw data only ({reason})");
            }
        }

        /// <summary>A KKEx block of another version, kept whole.</summary>
        public static void CopyBlock(ImportContext x, byte[] block, string form, KkexStore store)
        {
            store.Add("KKEx", "block", null, null, form, "opaque", "blockVersion", block);
            x.Report.Warning("plugin.blockVersion", "ext/hs2.kkex/block.KKEx.bin", $"KKEx block version \"{store.BlockVersion}\" is not read by ExtensibleSaveFormat; kept whole");
            x.Report.Count("kkex.opaque");
        }

        /// <summary>Per-object ExtendedSaveData of one vanilla object tree (any depth: `ChaFileFace.makeup`, `ChaFileHair.parts.0` …).</summary>
        public static void CopyObjects(ImportContext x, object tree, string objectName, string form, KkexStore store)
        {
            if (tree == null) return;
            Walk(x, tree, objectName, form, store);
        }

        static void Walk(ImportContext x, object o, string name, string form, KkexStore store)
        {
            if (o is Dictionary<object, object> m)
            {
                foreach (var kv in m)
                {
                    if (kv.Key is string k && k == Esd) { Entries(x, kv.Value, name, form, store); continue; }
                    if (kv.Value is Dictionary<object, object> || kv.Value is List<object>) Walk(x, kv.Value, name + "." + kv.Key, form, store);
                }
            }
            else if (o is List<object> l)
                for (int i = 0; i < l.Count; i++)
                    if (l[i] is Dictionary<object, object> || l[i] is List<object>) Walk(x, l[i], name + "." + i, form, store);
        }

        static void Entries(ImportContext x, object esd, string objectName, string form, KkexStore store)
        {
            if (esd == null) return;
            if (!(esd is byte[] bin))
            {
                x.Report.Warning("plugin.malformed", objectName + "." + Esd, "ExtendedSaveData is not a bin; not copied");
                return;
            }
            var spans = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            try { CardExtensions.ReadSpans(bin, spans); }
            catch (Exception e)
            {
                x.Report.Warning("plugin.malformed", objectName + "." + Esd, "ExtendedSaveData is not readable (" + e.Message + "); not copied");
                return;
            }
            foreach (var kv in spans)
            {
                string id = kv.Key;
                var (off, len) = kv.Value;
                if (bin[off] == 0xc0) { x.Report.Info("plugin.nil", objectName + ":" + id, "nil plugin data (treated as absent)"); x.Report.Count("plugin.nil"); continue; }
                if (Dropped.Contains(id)) { x.Report.Info("plugin.dropped", objectName + ":" + id, "plugin data not kept"); x.Report.Count("plugin.dropped"); continue; }
                var bytes = new byte[len];
                Buffer.BlockCopy(bin, off, bytes, 0, len);
                int? version = null;
                try
                {
                    int p = off;
                    if (MsgPack.Read(bin, ref p) is List<object> pd && pd.Count > 0 && pd[0] is long lv) version = (int)lv;
                }
                catch (Exception) { }
                bool source = id == SideloaderObjectId;
                store.Add(id, "object", objectName, version, form, source ? "source" : "opaque", source ? null : "noDecoder", bytes);
                x.Report.Count(source ? "kkex.source" : "kkex.opaque");
                if (!source) x.Report.Info("plugin.opaque", objectName + ":" + id, "kept as raw data only (noDecoder)");
            }
        }

        /// <summary>`CoordinateSex` from `ChaFileClothes.ExtendedSaveData` → `com.bepis.bepinex.sideloader`, or null.</summary>
        public static int? CoordinateSex(object clothes)
        {
            if (!(clothes.Get(Esd) is byte[] bin)) return null;
            try
            {
                var map = MsgPack.Read(bin);
                var pd = map.Get(SideloaderObjectId) as List<object>;
                var v = pd.At(1).Get("CoordinateSex");
                if (v is long l) return (int)l;
            }
            catch (Exception) { }
            return null;
        }
    }
}
