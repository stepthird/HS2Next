// The two members of an item node's section that make the item a bake or a piece (scene.json `item.bake` and `item.cut`, after
// `materialOverrides`). `bake` names the bake the item is drawn from: its identity and its blobs, the `res/<sha256>.bin` entries of
// the scene (a main blob and one blob per mesh and per texture, Runtime/Scene/Bake/BakeFormat.cs) and, for information, the store row it
// was added from. `cut` is the procedure that made a piece: the bake that was cut, the FK pose of that bake at the first step, the steps (each
// with the pose it is decided at where that changed) and the section; a piece holds both, `bake` being the procedure's stored result. Each is written only when present, and a scene with either is
// format revision 5: the members of this phase join that revision, with no revision 6 and no new migration. Unity-free save-layer classes;
// a node holds null when the member is absent. Only BakeEditCommand replaces them in a scene model.
//
// The reader binds strictly (a wrong type fails the load as for every core member, a JSON null included; `id`, `main` and `source` are
// required) and repairs nothing away: a `data` entry the main does not name is kept, an unknown `shape` or `keep` is kept (json.unknownEnum),
// a ragged value out of its range is kept and clamped when applied (cut.range), the excess over a limit is kept and not applied (bake.limit,
// cut.limit), and unknown members are kept at every level.
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>Finding codes of bakes and cuts.</summary>
    public static class BakeFindings
    {
        /// <summary>Error (reader of a blob): a wrong magic, a version above the reader's, an unknown flag, a body whose length or content is not what it says.</summary>
        public const string BlobCorrupt = "bake.blobCorrupt";
        /// <summary>Warning (realization): neither the scene nor the store holds the bake's main or data blobs; the item is the fallback, its blocks are kept.</summary>
        public const string Missing = "bake.missing";
        /// <summary>Warning (realization): a blob the main names is not in the scene; that mesh or texture is absent.</summary>
        public const string DataMissing = "bake.dataMissing";
        /// <summary>Warning (reader): more than <see cref="BakeLimits.MaxData"/> data entries; kept, the excess not applied. Also the word of a refused command.</summary>
        public const string Limit = "bake.limit";
        /// <summary>Warning: nothing visible to bake; nothing is made.</summary>
        public const string Empty = "bake.empty";
        /// <summary>Warning: a vertex weighed on more than four bones; the four largest kept, renormalized.</summary>
        public const string WeightsCut = "bake.weightsCut";
        /// <summary>Warning: a cube, 3D or array texture is not stored; the slot keeps none.</summary>
        public const string TextureKind = "bake.textureKind";
        /// <summary>Warning (realization, once per name): a stored shader name is not found; the material falls back.</summary>
        public const string ShaderMissing = "bake.shaderMissing";
        /// <summary>Warning: a stain's film is left out of the bake.</summary>
        public const string FilmOmitted = "bake.filmOmitted";
        /// <summary>Warning: an unreadable skinned mesh stored posed, hung on its root bone.</summary>
        public const string MeshPosed = "bake.meshPosed";
        /// <summary>Warning: an unreadable mesh left out.</summary>
        public const string MeshUnreadable = "bake.meshUnreadable";
        /// <summary>Warning (store): index.json missing or unreadable; the store reads as empty and the file is kept (copied to index.json.bad before the next write).</summary>
        public const string StoreUnreadable = "bake.storeUnreadable";
        /// <summary>Error (store): a write failed; the bake stays in the scene, reading still works.</summary>
        public const string StoreWrite = "bake.storeWrite";
        /// <summary>Warning (store): a blob whose content does not equal its name; not used, the row is damaged.</summary>
        public const string StoreCorrupt = "bake.storeCorrupt";
        /// <summary>Warning (store): a bake is being written or imported; purge waits.</summary>
        public const string Busy = "bake.busy";
        /// <summary>Info (import): a store row with the old number exists; that file is skipped.</summary>
        public const string ImportExists = "bake.importExists";
        /// <summary>Warning (import): a source of an old bake is not on this machine.</summary>
        public const string ImportSource = "bake.importSource";
        /// <summary>Warning (import): a material whose shader has no loaded original.</summary>
        public const string ImportShader = "bake.importShader";
        /// <summary>Warning (reader): more than <see cref="BakeLimits.MaxSteps"/> steps; kept, the excess not applied. Also the word of a refused command.</summary>
        public const string CutLimit = "cut.limit";
        /// <summary>Info (reader): a ragged value out of its range; kept, clamped when applied.</summary>
        public const string CutRange = "cut.range";
        /// <summary>Error (re-running a cut): a step whose shape or keep is unknown or does not belong to its shape; the piece keeps its stored result.</summary>
        public const string CutBadStep = "cut.badStep";
        /// <summary>Info (realization): a piece's result was missing and was computed again from its source.</summary>
        public const string CutRecomputed = "cut.recomputed";
    }

    /// <summary>Limits of bakes and cuts. A command beyond one is refused; a file's excess is kept in the document, reported and not applied.</summary>
    public static class BakeLimits
    {
        /// <summary>Steps of one piece's procedure.</summary>
        public const int MaxSteps = 64;
        /// <summary>Data entries (mesh and texture blobs) of one bake.</summary>
        public const int MaxData = 1024;
        /// <summary>Bytes of a scene's `res/` entries together: the container's sum of uncompressed sizes (4 GiB).</summary>
        public const long MaxSceneBytes = 4L << 30;

        /// <summary>True when adding <paramref name="adding"/> bytes to <paramref name="res"/> would pass <see cref="MaxSceneBytes"/>.</summary>
        public static bool WouldExceed(ResStore res, long adding) => (res?.TotalBytes ?? 0) + adding > MaxSceneBytes;
    }

    /// <summary>Shared reading and writing of the blob lists.</summary>
    static class BakeBlocks
    {
        /// <summary>An optional object member: null when absent; a JSON null or a wrong type fails as every core member (lenient: reported, then absent).</summary>
        internal static T ReadOptional<T>(DocReader r, string name) where T : DocObject, new()
        {
            if (!r.Has(name)) return null;
            var o = r.Obj(name);
            if (!(r.Object[name] is JObject)) return null;
            return DocObject.ReadNew<T>(o);
        }

        internal static List<string> ReadResList(DocReader r, string name) => r.Array(name, v => v.AsRes());

        internal static void WriteResList(CanonicalJsonWriter w, string name, List<string> list)
        {
            w.Name(name);
            string at = w.Path;
            w.StartArray();
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == null) throw DocObject.NullElement(w, at, i);
                    w.Res(list[i]);
                }
            w.EndArray();
        }

        internal static string Text(byte[] b) => Encoding.UTF8.GetString(b, 0, b.Length - 1);

        internal static T Parse<T>(string json, FindingList findings, string entry, bool lenient, string what) where T : DocObject, new()
        {
            var token = DocReader.ParseText(json ?? "", entry);
            if (!(token is JObject o))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "a " + what + " is an object, found " + DocReader.Describe(token), entry, token.Path);
            var x = new T();
            x.ReadFrom(new DocReader(o, new DocReadContext(entry, lenient, findings ?? new FindingList())));
            return x;
        }
    }

    /// <summary>
    /// `item.bake`: the item is drawn from a bake. <see cref="Id"/> is the bake's identity, <see cref="Main"/> its main blob, <see cref="Data"/> every mesh
    /// and texture blob the main names, in the main's order, each once; <see cref="No"/> the store row it was added from (information only; absent for a
    /// scene's own bake).
    /// </summary>
    public sealed class ItemBake : DocObject
    {
        public const string MemberName = "bake";

        /// <summary>The bake's identity (32 lowercase hex digits), made when it was baked; required.</summary>
        public string Id;
        /// <summary>The main blob, <c>res/&lt;sha256&gt;.bin</c>; required.</summary>
        public string Main;
        /// <summary>The mesh and texture blobs; never null.</summary>
        public List<string> Data = new List<string>();
        /// <summary>The store row it was added from; null = none (written only when set).</summary>
        public int? No;

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            Main = r.Res("main");
            Data = BakeBlocks.ReadResList(r, "data");
            No = r.Has("no") ? r.Int("no") : (int?)null;
            if (Data.Count > BakeLimits.MaxData)
                r.Report(FindingSeverity.Warning, BakeFindings.Limit, "data", Data.Count + " data entries are over the limit of " + BakeLimits.MaxData + "; kept, the excess not applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "id", Id);
            ReqRes(w, "main", Main);
            BakeBlocks.WriteResList(w, "data", Data);
            if (No.HasValue) w.Int("no", No.Value);
        }

        /// <summary>The data entries that act: the first <see cref="BakeLimits.MaxData"/>.</summary>
        public IReadOnlyList<string> AppliedData => Data.Count <= BakeLimits.MaxData ? Data : Data.GetRange(0, BakeLimits.MaxData);

        /// <summary>The main and every data entry, each once, in that order (what the scene must hold).</summary>
        public List<string> Blobs()
        {
            var l = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (Main != null && seen.Add(Main)) l.Add(Main);
            foreach (var d in Data) if (d != null && seen.Add(d)) l.Add(d);
            return l;
        }

        // ---------- text and copies ----------

        /// <summary>Reads a block from JSON text (one object) with the reader rules; a syntax or type error throws <see cref="SaveFormatException"/>.</summary>
        public static ItemBake Parse(string json, FindingList findings = null, string entry = MemberName, bool lenient = false) =>
            BakeBlocks.Parse<ItemBake>(json, findings, entry, lenient, "bake block");

        /// <summary>The canonical JSON bytes of the block (UTF-8, trailing LF), as scene.json writes it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(MemberName, WriteTo);
        /// <summary>The canonical JSON text of the block (no trailing LF).</summary>
        public string ToJson() => BakeBlocks.Text(ToBytes());
        /// <summary>A deep copy (a canonical write and read: unknown members kept, nothing shared).</summary>
        public ItemBake Clone() => Parse(ToJson());
    }

    /// <summary>`item.cut.source`: the bake that was cut, by its identity and its blobs (the same members as <see cref="ItemBake"/> but the store row).</summary>
    public sealed class CutSource : DocObject
    {
        /// <summary>The source bake's identity; required.</summary>
        public string Id;
        /// <summary>The source's main blob; required.</summary>
        public string Main;
        /// <summary>The source's mesh and texture blobs; never null.</summary>
        public List<string> Data = new List<string>();

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            Main = r.Res("main");
            Data = BakeBlocks.ReadResList(r, "data");
            if (Data.Count > BakeLimits.MaxData)
                r.Report(FindingSeverity.Warning, BakeFindings.Limit, "data", Data.Count + " data entries are over the limit of " + BakeLimits.MaxData + "; kept, the excess not applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "id", Id);
            ReqRes(w, "main", Main);
            BakeBlocks.WriteResList(w, "data", Data);
        }

        /// <summary>The data entries that act: the first <see cref="BakeLimits.MaxData"/>.</summary>
        public IReadOnlyList<string> AppliedData => Data.Count <= BakeLimits.MaxData ? Data : Data.GetRange(0, BakeLimits.MaxData);

        /// <summary>The main and every data entry, each once, in that order.</summary>
        public List<string> Blobs()
        {
            var l = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (Main != null && seen.Add(Main)) l.Add(Main);
            foreach (var d in Data) if (d != null && seen.Add(d)) l.Add(d);
            return l;
        }

        /// <summary>The source named by a bake block (its identity and blobs; the store row is not carried).</summary>
        public static CutSource Of(ItemBake bake)
        {
            if (bake == null) throw new ArgumentNullException(nameof(bake));
            return new CutSource { Id = bake.Id, Main = bake.Main, Data = new List<string>(bake.Data) };
        }
    }

    /// <summary>The shapes of a cut and what a piece keeps.</summary>
    public static class CutShapes
    {
        public const string Plane = "plane", Box = "box", Tear = "tear", Hole = "hole";
        public const string Front = "front", Back = "back", Inside = "inside", Outside = "outside", Open = "open";

        static readonly string[] shapes = { Plane, Box, Tear, Hole };
        static readonly string[] keeps = { Front, Back, Inside, Outside, Open };

        /// <summary>The shapes, in the order the panel shows them.</summary>
        public static IReadOnlyList<string> Shapes => shapes;
        /// <summary>Every value of a step's <c>keep</c>.</summary>
        public static IReadOnlyList<string> Keeps => keeps;

        public static bool IsShape(string s) => Array.IndexOf(shapes, s) >= 0;
        public static bool IsKeep(string s) => Array.IndexOf(keeps, s) >= 0;

        /// <summary>The <c>keep</c> values of <paramref name="shape"/> (plane: front, back; box: inside, outside; tear and hole: open); empty for an unknown shape.</summary>
        public static IReadOnlyList<string> KeepsOf(string shape)
        {
            switch (shape)
            {
                case Plane: return new[] { Front, Back };
                case Box: return new[] { Inside, Outside };
                case Tear:
                case Hole: return new[] { Open };
                default: return Array.Empty<string>();
            }
        }
    }

    /// <summary>The ranges of a ragged edge (1 unit = 10 cm). A command refuses nothing here; a file's value is kept and clamped when applied (<see cref="BakeFindings.CutRange"/>).</summary>
    public static class CutRanges
    {
        public static readonly WetRange Amplitude = new WetRange(0f, 0.5f);
        public static readonly WetRange Tooth = new WetRange(0.05f, 2f);
        public static readonly WetRange Randomness = new WetRange(0f, 1f);
        public const int SeedMin = 1, SeedMax = 9999;
    }

    /// <summary>`steps[].ragged`: a ragged edge; absent = a clean cut.</summary>
    public sealed class CutRagged : DocObject
    {
        /// <summary>Amplitude in world units (0 to 0.5).</summary>
        public float Amp;
        /// <summary>Tooth spacing in world units (0.05 to 2).</summary>
        public float Tooth = 0.2f;
        /// <summary>Randomness (0 to 1).</summary>
        public float Random = 0.5f;
        /// <summary>Pattern number (1 to 9999).</summary>
        public int Seed = 1;

        internal override void ReadMembers(DocReader r)
        {
            Amp = r.F32("amp", Amp);
            Tooth = r.F32("tooth", Tooth);
            Random = r.F32("random", Random);
            Seed = r.Int("seed", Seed);
            Range(r, "amp", Amp, CutRanges.Amplitude);
            Range(r, "tooth", Tooth, CutRanges.Tooth);
            Range(r, "random", Random, CutRanges.Randomness);
            if (Seed < CutRanges.SeedMin || Seed > CutRanges.SeedMax)
                r.Report(FindingSeverity.Info, BakeFindings.CutRange, "seed", "seed " + Seed + " is out of " + CutRanges.SeedMin + ".." + CutRanges.SeedMax + "; kept, clamped when applied");
        }

        static void Range(DocReader r, string name, float v, WetRange range)
        {
            if (!range.Contains(v)) r.Report(FindingSeverity.Info, BakeFindings.CutRange, name, name + " " + WetRange.F(v) + " is out of " + range + "; kept, clamped when applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.F32("amp", Amp);
            w.F32("tooth", Tooth);
            w.F32("random", Random);
            w.Int("seed", Seed);
        }

        /// <summary>True when every value is in its range.</summary>
        public bool InRange => CutRanges.Amplitude.Contains(Amp) && CutRanges.Tooth.Contains(Tooth) && CutRanges.Randomness.Contains(Random) && Seed >= CutRanges.SeedMin && Seed <= CutRanges.SeedMax;

        /// <summary>A copy with every value clamped to its range: what acts.</summary>
        public CutRagged Applied() => new CutRagged
        {
            Amp = CutRanges.Amplitude.Clamp(Amp),
            Tooth = CutRanges.Tooth.Clamp(Tooth),
            Random = CutRanges.Randomness.Clamp(Random),
            Seed = Seed < CutRanges.SeedMin ? CutRanges.SeedMin : Seed > CutRanges.SeedMax ? CutRanges.SeedMax : Seed,
        };
    }

    /// <summary>
    /// `steps[]`: one cut, the shape in the source's root space at the cut. <see cref="Keep"/> says which side the piece is: plane front or back, box inside or
    /// outside, tear and hole open.
    /// </summary>
    public sealed class CutStep : DocObject
    {
        /// <summary>plane, box, tear or hole; required. An unknown shape is kept (json.unknownEnum) and the piece keeps its stored result.</summary>
        public string Shape;
        public Vec3 Pos = Vec3.Zero;
        public Quat4 Rot = Quat4.Identity;
        public Vec3 Size = Vec3.One;
        /// <summary>front, back, inside, outside or open; required.</summary>
        public string Keep;
        /// <summary>The ragged edge; null = a clean cut (written only when set).</summary>
        public CutRagged Ragged;
        public bool ClothesOnly;
        /// <summary>The source's FK values at this step (the source's bone names, local Euler degrees); null = the previous step's pose, for the first step the
        /// block's (written only when set, after <see cref="ClothesOnly"/>). See <see cref="ItemCut.PoseOf"/>.</summary>
        public SortedDictionary<string, Vec3> Pose;

        internal override void ReadMembers(DocReader r)
        {
            Shape = r.Str("shape");
            if (!CutShapes.IsShape(Shape)) r.Report(FindingSeverity.Warning, "json.unknownEnum", "shape", "unknown value \"" + Shape + "\" kept");
            Pos = r.V3("pos", Pos);
            Rot = r.Quat("rot", Rot);
            Size = r.V3("size", Size);
            Keep = r.Str("keep");
            if (!CutShapes.IsKeep(Keep)) r.Report(FindingSeverity.Warning, "json.unknownEnum", "keep", "unknown value \"" + Keep + "\" kept");
            Ragged = BakeBlocks.ReadOptional<CutRagged>(r, "ragged");
            ClothesOnly = r.Bool("clothesOnly", ClothesOnly);
            Pose = r.Has("pose") ? r.Map("pose", v => v.AsV3()) : null;
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "shape", Shape);
            w.V3("pos", Pos);
            w.Quat("rot", Rot);
            w.V3("size", Size);
            ReqStr(w, "keep", Keep);
            if (Ragged != null) Ragged.WriteTo(w, "ragged");
            w.Bool("clothesOnly", ClothesOnly);
            if (Pose != null) w.Map("pose", Pose, (ww, e) => ww.Euler(e));
        }

        /// <summary>Why this step cannot be run (<see cref="BakeFindings.CutBadStep"/>): an unknown shape or keep, or a keep that does not belong to the shape; null when it can.</summary>
        public string Problem()
        {
            if (!CutShapes.IsShape(Shape)) return "unknown shape \"" + Shape + "\"";
            if (!CutShapes.IsKeep(Keep)) return "unknown keep \"" + Keep + "\"";
            foreach (var k in CutShapes.KeepsOf(Shape)) if (k == Keep) return null;
            return "a " + Shape + " keeps " + string.Join(" or ", CutShapes.KeepsOf(Shape)) + ", not " + Keep;
        }
    }

    /// <summary>The section colours the panel offers.</summary>
    public static class CutSectionColors
    {
        public static readonly Vec3 DarkRed = new Vec3(0.35f, 0.03f, 0.03f);
        public static readonly Vec3 Flesh = new Vec3(0.85f, 0.55f, 0.47f);
        public static readonly Vec3 Bone = new Vec3(0.89f, 0.85f, 0.75f);
        public static readonly Vec3 Black = new Vec3(0.05f, 0.05f, 0.05f);
        public static readonly Vec3 Grey = new Vec3(0.45f, 0.45f, 0.45f);

        static readonly string[] names = { "darkRed", "flesh", "bone", "black", "grey" };
        static readonly Vec3[] values = { DarkRed, Flesh, Bone, Black, Grey };

        /// <summary>The presets' names, in the order the panel shows them.</summary>
        public static IReadOnlyList<string> Names => names;
        /// <summary>The colour of preset <paramref name="name"/>, or null.</summary>
        public static Vec3? Of(string name)
        {
            int i = Array.IndexOf(names, name);
            return i < 0 ? (Vec3?)null : values[i];
        }
    }

    /// <summary>`cut.section`: the colour of the piece's sections (flat, unlit) and whether garments and hair show their own material inside.</summary>
    public sealed class CutSection : DocObject
    {
        /// <summary>RGB, 0 to 1.</summary>
        public Vec3 Color = CutSectionColors.DarkRed;
        public bool OwnInside = true;

        internal override void ReadMembers(DocReader r)
        {
            Color = r.V3("color", Color);
            OwnInside = r.Bool("ownInside", OwnInside);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("color", Color);
            w.Bool("ownInside", OwnInside);
        }
    }

    /// <summary>
    /// `item.cut`: the procedure that made a piece. <see cref="Source"/> is the bake that was cut (the first source of a chain of cuts), <see cref="Pose"/> its FK
    /// values at the first step (local Euler degrees by bone; null = its baked pose), <see cref="Steps"/> the whole chain of steps from that source, each decided
    /// at its own pose (<see cref="PoseOf"/>), <see cref="Section"/> the piece's section. The piece's result is its <c>bake</c>.
    /// </summary>
    public sealed class ItemCut : DocObject
    {
        public const string MemberName = "cut";

        /// <summary>Required.</summary>
        public CutSource Source = new CutSource();
        /// <summary>The source's FK values at the first step; null = its baked pose (written only when set). A later step holds its own when it differs.</summary>
        public SortedDictionary<string, Vec3> Pose;
        /// <summary>Never null.</summary>
        public List<CutStep> Steps = new List<CutStep>();
        public CutSection Section = new CutSection();

        internal override void ReadMembers(DocReader r)
        {
            Source = ReadNew<CutSource>(r.ObjReq("source"));
            Pose = r.Has("pose") ? r.Map("pose", v => v.AsV3()) : null;
            Steps = ReadList<CutStep>(r, "steps");
            Section.ReadFrom(r.Obj("section"));
            if (Steps.Count > BakeLimits.MaxSteps)
                r.Report(FindingSeverity.Warning, BakeFindings.CutLimit, "steps", Steps.Count + " steps are over the limit of " + BakeLimits.MaxSteps + "; kept, the excess not applied");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Name("source");
            if (Source == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "required object is null", w.Entry, w.Path);
            Source.WriteTo(w);
            if (Pose != null) w.Map("pose", Pose, (ww, e) => ww.Euler(e));
            WriteList(w, "steps", Steps);
            Section.WriteTo(w, "section");
        }

        /// <summary>The steps that act: the first <see cref="BakeLimits.MaxSteps"/>.</summary>
        public IReadOnlyList<CutStep> AppliedSteps => Steps.Count <= BakeLimits.MaxSteps ? Steps : Steps.GetRange(0, BakeLimits.MaxSteps);

        /// <summary>The pose step <paramref name="index"/> is decided at: its own <see cref="CutStep.Pose"/>, else the nearest earlier step's, else the block's
        /// <see cref="Pose"/>; null = the source's baked pose. The object returned is the block's own (not a copy).</summary>
        public SortedDictionary<string, Vec3> PoseOf(int index)
        {
            if (index < 0 || index >= Steps.Count) throw new ArgumentOutOfRangeException(nameof(index));
            for (int i = index; i >= 0; i--)
                if (Steps[i]?.Pose != null) return Steps[i].Pose;
            return Pose;
        }

        /// <summary>The first step that cannot be run, as "steps[i]: why"; null when every step can.</summary>
        public string Problem()
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i] == null) return "steps[" + i + "]: null";
                string p = Steps[i].Problem();
                if (p != null) return "steps[" + i + "]: " + p;
            }
            return null;
        }

        // ---------- text and copies ----------

        /// <summary>Reads a block from JSON text (one object) with the reader rules; a syntax or type error throws <see cref="SaveFormatException"/>.</summary>
        public static ItemCut Parse(string json, FindingList findings = null, string entry = MemberName, bool lenient = false) =>
            BakeBlocks.Parse<ItemCut>(json, findings, entry, lenient, "cut block");

        /// <summary>The canonical JSON bytes of the block (UTF-8, trailing LF), as scene.json writes it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(MemberName, WriteTo);
        /// <summary>The canonical JSON text of the block (no trailing LF).</summary>
        public string ToJson() => BakeBlocks.Text(ToBytes());
        /// <summary>A deep copy (a canonical write and read: unknown members kept, nothing shared).</summary>
        public ItemCut Clone() => Parse(ToJson());
    }
}
