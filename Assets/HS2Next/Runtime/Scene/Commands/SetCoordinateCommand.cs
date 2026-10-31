// Wears a whole coordinate on a character (the coordinate load "all" of the Edit tab and Studio's Costume tab; undoable):
// the character's coordinate document takes the source's name, clothes, accessories, AdditionalAccessoryControls rules,
// inline ext and unknown members and keeps its own sex; the coordinate-scope ext data (plugin blocks and the imported
// hs2.kkex copies, which no field path can write) takes the source's. The document object stays the same one — the built
// character's editor reads it — and its contents are replaced in place (a read of the source's canonical bytes); undo
// replaces them with the previous contents and ext data, byte for byte. Changes are reported per member that changed
// (`appearance.coord.name`, `.clothes`, `.accessories`, `.ruleOverrides`; `appearance.coord.ext` when only the inline ext —
// the clothes parts' included —, unknown members or the ext data changed), never the whole `appearance.coord`, so the
// realization swaps the parts that changed instead of rebuilding the character. The scene's accessory visibility map is
// not touched here: the op adds its fill and prune steps to the same transaction. A source whose sex differs from the
// character's is refused (bad_args).
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Api.Save;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public sealed class SetCoordinateCommand : ISceneCommand
    {
        /// <summary>The member paths the command reports (in this order) when their contents changed.</summary>
        public static readonly string[] MemberPaths =
        {
            AppearancePaths.Coord("name"), AppearancePaths.Coord("clothes"), AppearancePaths.Coord("accessories"), AppearancePaths.Coord("ruleOverrides"),
        };
        /// <summary>The path reported when only the inline ext, unknown members or the coordinate-scope ext data changed (the
        /// realization applies none of them).</summary>
        public static readonly string ExtPath = AppearancePaths.Coord("ext");

        readonly string id;
        readonly byte[] source;
        readonly int sourceSex;
        readonly ExtScopeData sourceExt;
        byte[] oldBytes;
        ExtScopeData oldExt;
        bool changed;
        readonly List<string> reported = new List<string>();

        /// <param name="nodeId">The character node.</param>
        /// <param name="coordinate">The coordinate to wear (copied now; its sex must be the character's).</param>
        /// <param name="coordinateExt">Its coordinate-scope ext data (copied now; null = none).</param>
        public SetCoordinateCommand(string nodeId, CoordDocument coordinate, ExtScopeData coordinateExt, string label = "Load coordinate")
        {
            id = nodeId;
            Label = label;
            if (coordinate != null)
            {
                source = Bytes(coordinate);
                sourceSex = coordinate.Sex;
            }
            sourceExt = coordinateExt?.Clone();
        }

        public string Label { get; }

        /// <summary>The paths the last Apply / Revert reported (tests).</summary>
        public IReadOnlyList<string> Reported => reported;

        static byte[] Bytes(CoordDocument d) => CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, d.Write);

        static CoordDocument Read(byte[] b) => CoordDocument.Read(DocReader.ForDocument(b, new DocReadContext(CoordDocument.EntryName)));

        static void ReadInto(CoordDocument target, byte[] b) => target.ReadFrom(DocReader.ForDocument(b, new DocReadContext(CoordDocument.EntryName)));

        static CharaSubtree Stored(SceneModel m, string id)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            if (n.Kind != NodeKind.Chara) throw new CommandException(CommandCodes.WrongKind, "node " + id + " is not a character");
            return n.Appearance?.Stored ?? throw new CommandException(CommandCodes.Unsupported, "character not imported (node " + id + ")");
        }

        public void Validate(SceneModel m)
        {
            var st = Stored(m, id);
            if (source == null) throw new CommandException(CommandCodes.BadArgs, "no coordinate");
            if (sourceExt != null && sourceExt.Scope != ExtScope.Coord) throw new CommandException(CommandCodes.BadArgs, "the ext data is not of the coordinate scope");
            int sex = st.Chara.Profile.Sex;
            if (sourceSex != sex)
                throw new CommandException(CommandCodes.BadArgs, "the coordinate is for " + (sourceSex == 0 ? "male" : "female") + " characters; character " + id + " is " + (sex == 0 ? "male" : "female"));
        }

        public void Apply(SceneModel m, ChangeSink s)
        {
            var st = Stored(m, id);
            oldBytes = Bytes(st.Coord);
            oldExt = st.CoordExt.Clone();
            var target = Read(source);
            target.Sex = st.Coord.Sex;
            var newBytes = Bytes(target);
            var newExt = sourceExt ?? new ExtScopeData(ExtScope.Coord);
            var before = Members(st.Coord);
            bool docChanged = !Same(oldBytes, newBytes), extChanged = ExtFingerprint(oldExt) != ExtFingerprint(newExt);
            changed = docChanged || extChanged;
            reported.Clear();
            if (!changed) return;
            if (docChanged) ReadInto(st.Coord, newBytes);
            if (extChanged) CopyExt(newExt, st.CoordExt);
            Report(s, before, Members(st.Coord), extChanged);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!changed) return;
            var st = Stored(m, id);
            var before = Members(st.Coord);
            bool extChanged = ExtFingerprint(oldExt) != ExtFingerprint(st.CoordExt);
            ReadInto(st.Coord, oldBytes);
            CopyExt(oldExt, st.CoordExt);
            reported.Clear();
            Report(s, before, Members(st.Coord), extChanged);
        }

        // The canonical text of the four reported members and of the rest of the document (inline ext, unknown members). The
        // clothes parts' inline ext counts as the rest: a change of plugin data alone swaps no clothes part.
        static string[] Members(CoordDocument d)
        {
            var acc = new StringBuilder();
            foreach (var a in d.Accessories) acc.Append(Encoding.UTF8.GetString(DocClone.Bytes(a))).Append('\n');
            var rest = new CoordDocument { Unknown = d.Unknown, Ext = d.Ext };
            string clothes, partExt = "";
            if (d.Clothes.HasPartExt())
            {
                var c = DocClone.Clone(d.Clothes);
                var sb = new StringBuilder();
                for (int k = 0; k < ClothesSet.Count; k++)
                {
                    var p = c.Parts[k];
                    if (p.Ext.IsEmpty) continue;
                    sb.Append('\n').Append(ClothesSet.Names[k]).Append('\t').Append(Encoding.UTF8.GetString(Bytes(new CoordDocument { Ext = p.Ext })));
                    p.Ext = new ExtInline();
                }
                clothes = Encoding.UTF8.GetString(DocClone.Bytes(c));
                partExt = sb.ToString();
            }
            else clothes = Encoding.UTF8.GetString(DocClone.Bytes(d.Clothes));
            return new[]
            {
                d.Name ?? "", clothes, acc.ToString(),
                d.RuleOverrides == null ? "null" : Encoding.UTF8.GetString(DocClone.Bytes(d.RuleOverrides)),
                Encoding.UTF8.GetString(Bytes(rest)) + partExt,
            };
        }

        void Report(ChangeSink s, string[] before, string[] after, bool extChanged)
        {
            bool any = false;
            for (int i = 0; i < MemberPaths.Length; i++)
            {
                if (string.Equals(before[i], after[i], StringComparison.Ordinal)) continue;
                s.NodeChanged(id, MemberPaths[i]);
                reported.Add(MemberPaths[i]);
                any = true;
            }
            if (extChanged || !string.Equals(before[4], after[4], StringComparison.Ordinal) || !any)
            {
                s.NodeChanged(id, ExtPath);
                reported.Add(ExtPath);
            }
        }

        static void CopyExt(ExtScopeData from, ExtScopeData into)
        {
            into.Blocks.Clear();
            foreach (var kv in from.Blocks) into.Blocks[kv.Key] = kv.Value.Clone();
            into.Kkex = from.Kkex?.Clone();
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// A canonical fingerprint (SHA-256, hex) of coordinate-scope ext data: per block in namespace order its version,
        /// flags, manifest leftovers and files; the hs2.kkex store as it is saved (index and files). Equal fingerprints = equal
        /// data; empty data (null included) has one fingerprint.
        /// </summary>
        public static string ExtFingerprint(ExtScopeData d)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                if (d != null)
                {
                    foreach (var kv in d.Blocks)
                    {
                        var b = kv.Value;
                        sb.Append("B\t").Append(kv.Key).Append('\t').Append(b.Version).Append('\t').Append(b.Required ? 1 : 0).Append('\t').Append(b.HadManifestEntry ? 1 : 0)
                          .Append('\t').Append(b.IsUnknown ? 1 : 0).Append('\t').Append(b.UnknownReason).Append('\t').Append(Leftovers(b.ManifestUnknown)).Append('\n');
                        foreach (var f in b.Files) sb.Append("F\t").Append(f.Key).Append('\t').Append(Hex(sha.ComputeHash(f.Value ?? Array.Empty<byte>()))).Append('\n');
                    }
                    if (d.Kkex != null && !d.Kkex.IsEmpty)
                        foreach (var f in d.Kkex.Save()) sb.Append("K\t").Append(f.Key).Append('\t').Append(Hex(sha.ComputeHash(f.Value ?? Array.Empty<byte>()))).Append('\n');
                }
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
            }
        }

        static string Leftovers(JsonLeftovers u)
        {
            if (u == null || u.IsEmpty) return "";
            return Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes("ext", w => { w.StartObject(); w.Leftovers(u); w.EndObject(); }));
        }

        static string Hex(byte[] h)
        {
            var sb = new StringBuilder(h.Length * 2);
            foreach (var x in h) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        public bool TryMerge(ISceneCommand next) => false;
        public long EstimatedBytes => 256 + (source?.Length ?? 0) * 2L + (oldBytes?.Length ?? 0);
        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();
    }
}
