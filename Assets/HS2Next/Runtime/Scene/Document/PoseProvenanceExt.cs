// The core node-scope ext block `hs2.pose` (open point I14): provenance of a
// character's last HS2 pose import (source path, payload hash, game name), the keys without a name, the file's raw IK
// targets (with the 3.4 conversion's scale factor and Body shift) and unknown
// trailer entries. Inline JSON, version 1, not required, never applied by the runtime.
// The value lives in the node's inline ext (TreeNode.Doc.Ext["hs2.pose"]): it moves with delete and undo, duplicates copy
// it, and only SetNodeExtCommand sets or removes it. The handler is registered in the session's static initialiser so the
// namespace is known before any load. It accepts a valid block on every load and, on every save path (a session save, or
// SceneContainer.Save / ToBytes called directly), writes back what the node document holds, token for token as an
// unknown block is kept (ExtDispatcher.KeepInline; 2.4, in 2.3 a direct save of a container a session had
// loaded dropped the block). A newer version or a malformed value is declined, so it stays unknown and is preserved.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Api.Save;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes
{
    /// <summary>The JSON form of an <c>hs2.pose</c> block. Every member is always written (canonical bytes).</summary>
    public sealed class PoseProvenance
    {
        public const int FloatsPerKey = 9;

        /// <summary>'/'-separated path relative to UserData/studio/pose, or the file name only for a file outside it (never absolute).</summary>
        public string Source = "";
        /// <summary>64 lowercase hex of the payload bytes (identifying string to the end of the trailer).</summary>
        public string Sha256 = "";
        /// <summary>ExtendedSave `gameName` of the trailer (0 when absent).</summary>
        public int GameName;
        /// <summary>Keys without a name (decimal key → 9 floats: ChangeAmount pos, rot, scale), ascending.</summary>
        public SortedDictionary<int, float[]> Unresolved = new SortedDictionary<int, float[]>();
        /// <summary>The file's IK state when enableIK (targets kept raw until 3.4); null otherwise.</summary>
        public PoseIkBlock Ik;
        /// <summary>Unknown trailer GUID → base64 of its PluginData bytes (ordinal order).</summary>
        public SortedDictionary<string, string> Trailer = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public sealed class PoseIkBlock
        {
            public bool Enabled;
            public bool[] Groups = new bool[5];
            /// <summary>Target index (0..12) → 9 floats.</summary>
            public SortedDictionary<int, float[]> Targets = new SortedDictionary<int, float[]>();
            /// <summary>The 3.4 conversion's scale factor (1 or 10); null = not converted or rejected.</summary>
            public int? Scale;
            /// <summary>T_s − T_h of the Body conversion in root space (3 floats); null = not converted.</summary>
            public float[] BodyShift;
        }

        // Floats are written as their float32 text (so 0.3f is "0.3"); a non-finite value (JSON has none) is written as null
        // and read back as NaN.
        static JToken F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? JValue.CreateNull()
            : new JValue(double.Parse(JsonText.F32(v), NumberStyles.Float, CultureInfo.InvariantCulture));

        static JArray Floats(float[] a)
        {
            var r = new JArray();
            if (a != null) foreach (var v in a) r.Add(F(v));
            return r;
        }

        static JObject Map(SortedDictionary<int, float[]> m)
        {
            var o = new JObject();
            if (m != null) foreach (var kv in m) o[kv.Key.ToString(CultureInfo.InvariantCulture)] = Floats(kv.Value);
            return o;
        }

        public JObject ToJObject()
        {
            var o = new JObject
            {
                ["source"] = Source ?? "",
                ["sha256"] = Sha256 ?? "",
                ["gameName"] = GameName,
                ["unresolved"] = Map(Unresolved),
            };
            if (Ik == null) o["ik"] = JValue.CreateNull();
            else
            {
                var g = new JArray();
                for (int i = 0; i < 5; i++) g.Add(Ik.Groups != null && i < Ik.Groups.Length && Ik.Groups[i]);
                o["ik"] = new JObject
                {
                    ["enabled"] = Ik.Enabled, ["groups"] = g, ["targets"] = Map(Ik.Targets),
                    ["scale"] = Ik.Scale.HasValue ? new JValue(Ik.Scale.Value) : JValue.CreateNull(),
                    ["bodyShift"] = Ik.BodyShift != null ? (JToken)Floats(Ik.BodyShift) : JValue.CreateNull(),
                };
            }
            var t = new JObject();
            if (Trailer != null) foreach (var kv in Trailer) t[kv.Key] = kv.Value ?? "";
            o["trailer"] = t;
            return o;
        }

        /// <summary>Canonical JSON text of the block.</summary>
        public string ToJson() => CanonicalJsonWriter.ToText(ToJObject());

        /// <summary>Parses a block; null when the text is not an hs2.pose object (unknown extra members are ignored).</summary>
        public static PoseProvenance Parse(string json)
        {
            if (!(DocReader.TryParseValue(json, out _) is JObject o)) return null;
            try
            {
                var p = new PoseProvenance();
                if (!(o["source"] is JValue src) || src.Type != JTokenType.String) return null;
                p.Source = (string)src;
                if (!(o["sha256"] is JValue sha) || sha.Type != JTokenType.String) return null;
                p.Sha256 = (string)sha;
                if (!(o["gameName"] is JValue gn) || gn.Type != JTokenType.Integer) return null;
                p.GameName = (int)(long)gn;
                if (!(o["unresolved"] is JObject un) || !ReadMap(un, p.Unresolved)) return null;
                var ik = o["ik"];
                if (ik != null && ik.Type != JTokenType.Null)
                {
                    if (!(ik is JObject io)) return null;
                    p.Ik = new PoseIkBlock();
                    if (!(io["enabled"] is JValue en) || en.Type != JTokenType.Boolean) return null;
                    p.Ik.Enabled = (bool)en;
                    if (!(io["groups"] is JArray ga)) return null;
                    for (int i = 0; i < 5 && i < ga.Count; i++) p.Ik.Groups[i] = ga[i].Type == JTokenType.Boolean && (bool)ga[i];
                    if (!(io["targets"] is JObject ta) || !ReadMap(ta, p.Ik.Targets)) return null;
                    // 3.4 members, optional (a 2.3 block has neither)
                    var sc = io["scale"];
                    if (sc != null && sc.Type != JTokenType.Null)
                    {
                        if (sc.Type != JTokenType.Integer) return null;
                        p.Ik.Scale = (int)(long)sc;
                    }
                    var bs = io["bodyShift"];
                    if (bs != null && bs.Type != JTokenType.Null)
                    {
                        if (!(bs is JArray ba) || ba.Count != 3) return null;
                        p.Ik.BodyShift = new float[3];
                        for (int i = 0; i < 3; i++)
                        {
                            if (ba[i].Type != JTokenType.Float && ba[i].Type != JTokenType.Integer) return null;
                            p.Ik.BodyShift[i] = (float)(double)ba[i];
                        }
                    }
                }
                if (o["trailer"] is JObject tr)
                    foreach (var kv in tr)
                    {
                        if (kv.Value.Type != JTokenType.String) return null;
                        p.Trailer[kv.Key] = (string)kv.Value;
                    }
                else if (o["trailer"] != null) return null;
                return p;
            }
            catch (Exception) { return null; }
        }

        static bool ReadMap(JObject o, SortedDictionary<int, float[]> into)
        {
            foreach (var kv in o)
            {
                if (!int.TryParse(kv.Key, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int key)) return false;
                if (!(kv.Value is JArray a)) return false;
                var f = new float[a.Count];
                for (int i = 0; i < a.Count; i++)
                {
                    var t = a[i];
                    if (t.Type == JTokenType.Null) f[i] = float.NaN;
                    else if (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) f[i] = (float)(double)t;
                    else return false;
                }
                into[key] = f;
            }
            return true;
        }
    }

    public sealed class PoseProvenanceExt : IExtBlockHandler
    {
        public const string Ns = "hs2.pose";
        public static readonly PoseProvenanceExt Instance = new PoseProvenanceExt();

        PoseProvenanceExt() { }

        public string Namespace => Ns;
        public int Version => 1;
        public ExtScope Scopes => ExtScope.Node;
        public bool RequiredByDefault => false;

        /// <summary>
        /// Accepts a provenance object of version 1 or older on every load (scene session or a direct
        /// <c>SceneContainer.Load</c>). A newer version or a value that is not a provenance object is declined: the block is
        /// then unknown and written back byte for byte.
        /// </summary>
        public void Read(IExtReadContext ctx)
        {
            if (ctx.StoredVersion > Version) { ctx.Decline("version " + ctx.StoredVersion + " is newer than " + Version); return; }
            string json = ctx.Json;
            if (json == null || PoseProvenance.Parse(json) == null) ctx.Decline("not a pose provenance block");
        }

        /// <summary>
        /// The node document holds the value (<c>TreeNode.Doc.Ext</c>; a session's model shares the container's node objects,
        /// and only <see cref="SetNodeExtCommand"/> changes it), so every save writes back what the document holds, as an
        /// unknown block is kept: a session save, and a container saved directly after a session or a direct load. A node
        /// without the value has no block.
        /// </summary>
        public void Write(IExtWriteContext ctx) => ExtDispatcher.KeepInline(ctx);

        /// <summary>
        /// Registers the handler in <paramref name="registry"/> (core namespace) unless a handler of the namespace is there. A
        /// registry that refuses core <c>hs2.*</c> handlers leaves the block unregistered: it is then kept as an unknown block
        /// (preserved byte for byte, ext.unknown at load), which loses nothing.
        /// </summary>
        public static void EnsureRegistered(ExtRegistry registry)
        {
            if (registry == null || registry.Get(Ns) != null) return;
            try { registry.RegisterCore(Instance); }
            catch (ArgumentException) { }
        }

        /// <summary>The block of a node, or null (absent or not parseable).</summary>
        public static PoseProvenance Of(TreeNode node)
        {
            var t = node?.Doc?.Ext?.Get(Ns);
            return t == null ? null : PoseProvenance.Parse(CanonicalJsonWriter.ToText(t));
        }
    }
}
