// pose.json: the main document of an HS2Next pose
// file (kind hs2next.pose). It holds a subset of the character node state — the parts `anime`, `hands`,
// `fk`, `ik`, `correction`, `face`, `look` — each with exactly the schema (and the DocObject class) of the node
// member of the same name. A part is in the document when its model field is non-null; `parts` is written from
// the fields (known parts in PartNames order, then part names this reader does not know, kept from the file).
// Reading: a listed part whose member is absent reads as that member's absent value (`pose.partMissing`); a known
// part member that `parts` does not list is not read but kept as an unknown member (`pose.unlistedPart`); an
// unknown part name is kept with its member (`pose.unknownPart`). No ext in v1.
using System;
using System.Collections.Generic;

namespace HS2Next.Save
{
    public sealed class PoseDocument : DocObject
    {
        public const string EntryName = "pose.json";

        public const string PartAnime = "anime", PartHands = "hands", PartFk = "fk", PartIk = "ik",
            PartCorrection = "correction", PartFace = "face", PartLook = "look";

        /// <summary>The known parts in write order.</summary>
        public static readonly string[] PartNames = { PartAnime, PartHands, PartFk, PartIk, PartCorrection, PartFace, PartLook };

        public static bool IsKnownPart(string name) => Array.IndexOf(PartNames, name) >= 0;

        /// <summary>Display name (also the manifest title).</summary>
        public string Name = "";
        /// <summary>0 male, 1 female (the character the pose was saved from; also the manifest sex).</summary>
        public int Sex = 1;

        // The parts; null = not in the file.
        public CharaAnimeState Anime;
        /// <summary>Exactly 2 when present, [left, right].</summary>
        public int[] Hands;
        public CharaFkState Fk;
        public CharaIkState Ik;
        /// <summary>Exactly 8 when present.</summary>
        public bool[] Correction;
        public CharaFaceState Face;
        public CharaLookState Look;

        /// <summary>Part names of `parts` this reader does not know (kept and written back after the known ones; their
        /// members are unknown members of the document).</summary>
        public List<string> UnknownParts = new List<string>();

        /// <summary>The value of `parts` as written: the known parts present, then <see cref="UnknownParts"/>.</summary>
        public List<string> Parts
        {
            get
            {
                var l = new List<string>();
                foreach (var p in PartNames) if (Has(p)) l.Add(p);
                if (UnknownParts != null)
                    foreach (var p in UnknownParts) if (p != null && !l.Contains(p)) l.Add(p);
                return l;
            }
        }

        /// <summary>True when the known part <paramref name="part"/> is in the document.</summary>
        public bool Has(string part)
        {
            switch (part)
            {
                case PartAnime: return Anime != null;
                case PartHands: return Hands != null;
                case PartFk: return Fk != null;
                case PartIk: return Ik != null;
                case PartCorrection: return Correction != null;
                case PartFace: return Face != null;
                case PartLook: return Look != null;
                default: return false;
            }
        }

        /// <summary>Reads a document from a reader over its root object.</summary>
        public static PoseDocument Read(DocReader r)
        {
            var d = new PoseDocument();
            d.ReadFrom(r);
            return d;
        }

        /// <summary>Writes the root object (the caller finishes the document).</summary>
        public void Write(CanonicalJsonWriter w) => WriteTo(w);

        internal override void ReadMembers(DocReader r)
        {
            Name = r.Str("name", Name);
            Sex = r.Int("sex", Sex);
            List<string> listed;
            if (r.Has("parts")) listed = r.Array("parts", v => v.AsStr());
            else
            {
                // Absent `parts`: the known part members present.
                r.Consume("parts");
                listed = new List<string>();
                foreach (var p in PartNames) if (r.Has(p)) listed.Add(p);
            }
            var set = new HashSet<string>(listed, StringComparer.Ordinal);
            UnknownParts = new List<string>();
            foreach (var p in listed)
            {
                if (IsKnownPart(p) || UnknownParts.Contains(p)) continue;
                UnknownParts.Add(p);
                r.Report(FindingSeverity.Info, "pose.unknownPart", "parts", "part \"" + p + "\" is not known to this reader; kept with its member");
            }
            foreach (var p in PartNames)
            {
                if (!set.Contains(p))
                {
                    if (r.Has(p)) r.Report(FindingSeverity.Warning, "pose.unlistedPart", p, "member " + p + " is not listed in parts; kept as an unknown member, not loaded");
                    continue;
                }
                if (!r.Has(p)) r.Report(FindingSeverity.Info, "pose.partMissing", p, "part " + p + " is listed but absent; its absent value is used");
                switch (p)
                {
                    case PartAnime: Anime = new CharaAnimeState(); Anime.ReadFrom(r.Obj(p)); break;
                    case PartHands: Hands = r.IntArray(p, new[] { 0, 0 }, 2); break;
                    case PartFk: Fk = new CharaFkState(); Fk.ReadFrom(r.Obj(p)); break;
                    case PartIk: Ik = new CharaIkState(); Ik.ReadFrom(r.Obj(p)); break;
                    case PartCorrection: Correction = r.BoolArray(p, new bool[8], 8); break;
                    case PartFace: Face = new CharaFaceState(); Face.ReadFrom(r.Obj(p)); break;
                    case PartLook: Look = new CharaLookState(); Look.ReadFrom(r.Obj(p)); break;
                }
            }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Str("name", Name ?? "");
            w.Int("sex", Sex);
            w.StrArray("parts", Parts);
            if (Anime != null) Anime.WriteTo(w, PartAnime);
            if (Hands != null) Ints(w, PartHands, Hands, 2);
            if (Fk != null) Fk.WriteTo(w, PartFk);
            if (Ik != null) Ik.WriteTo(w, PartIk);
            if (Correction != null) Bools(w, PartCorrection, Correction, 8);
            if (Face != null) Face.WriteTo(w, PartFace);
            if (Look != null) Look.WriteTo(w, PartLook);
        }

        // ---------------- node state ↔ document ----------------

        /// <summary>
        /// A document holding deep copies of the parts <paramref name="parts"/> (null = every known part) of a character
        /// node state. Unknown part names are ignored. The caller fills missing values (FK capture, IK targets) itself.
        /// </summary>
        public static PoseDocument FromNode(CharaNodeState state, string name, int sex, IEnumerable<string> parts = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var d = new PoseDocument { Name = name ?? "", Sex = sex };
            foreach (var p in parts ?? PartNames)
            {
                switch (p)
                {
                    case PartAnime: d.Anime = Copy(state.Anime); break;
                    case PartHands: d.Hands = (int[])state.Hands?.Clone(); break;
                    case PartFk: d.Fk = Copy(state.Fk); break;
                    case PartIk: d.Ik = Copy(state.Ik); break;
                    case PartCorrection: d.Correction = (bool[])state.Correction?.Clone(); break;
                    case PartFace: d.Face = Copy(state.Face); break;
                    case PartLook: d.Look = Copy(state.Look); break;
                }
            }
            return d;
        }

        /// <summary>
        /// Copies the parts of this document into <paramref name="state"/> (deep copies): the parts named in
        /// <paramref name="parts"/> that the document holds (null = every part it holds). Returns the parts copied, in
        /// PartNames order.
        /// </summary>
        public List<string> CopyTo(CharaNodeState state, IEnumerable<string> parts = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var want = parts == null ? null : new HashSet<string>(parts, StringComparer.Ordinal);
            var done = new List<string>();
            foreach (var p in PartNames)
            {
                if (!Has(p) || want != null && !want.Contains(p)) continue;
                switch (p)
                {
                    case PartAnime: state.Anime = Copy(Anime); break;
                    case PartHands: state.Hands = (int[])Hands.Clone(); break;
                    case PartFk: state.Fk = Copy(Fk); break;
                    case PartIk: state.Ik = Copy(Ik); break;
                    case PartCorrection: state.Correction = (bool[])Correction.Clone(); break;
                    case PartFace: state.Face = Copy(Face); break;
                    case PartLook: state.Look = Copy(Look); break;
                }
                done.Add(p);
            }
            return done;
        }

        /// <summary>A deep copy of one part object through its canonical bytes (null for null).</summary>
        public static T Copy<T>(T part) where T : DocObject, new()
        {
            if (part == null) return null;
            var bytes = CanonicalJsonWriter.ToBytes(EntryName, part.WriteTo);
            var r = DocReader.ForDocument(bytes, new DocReadContext(EntryName, false, new FindingList()));
            var x = new T();
            x.ReadFrom(r);
            return x;
        }
    }
}
