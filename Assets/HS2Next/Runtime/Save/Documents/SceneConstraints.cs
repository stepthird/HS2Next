// The constraints of a scene: the scene.json member `constraints`, a top-level array after `timeline` and before `ext`, written
// only when it has an entry. An entry relates two ends, a source (a node, or a bone of it by address) and a target (a node, a
// character's bone or a character's IK target), through a set of components, with the offsets taken when it was made, a weight
// and a switch. Unity-free save-layer classes, as the scene settings are; the runtime model holds one list (SceneModel.Constraints,
// null = the scene has none) and only the constraint edit command replaces it or one of its entries. Character, coordinate and
// pose files never carry it.
//
// The reader binds strictly (a missing required member or a wrong type fails the load as for every core member) and then reports
// what a hand-made or damaged file may hold. Nothing is dropped: a repeated id gets a derived one, a label over the limit is
// shortened, a weight outside 0..1 is stored as read and evaluated clamped, and an entry that cannot act (an unknown component,
// a refused set, an address that is not canonical, an entry beyond the limit) is kept, written back and stays inactive. The writer
// emits every member in schema order, `bone` and `ik` only when present, components in the order of ConstraintComponents.All.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>The components of a constraint: the strings of <c>components</c>, and which of them may stand together.</summary>
    public static class ConstraintComponents
    {
        /// <summary>The target's position is the source's plus the offset, in world axes.</summary>
        public const string Position = "position";
        /// <summary>The target's rotation is the source's times the offset.</summary>
        public const string Rotation = "rotation";
        /// <summary>The target's scale is the source's times the offset, per axis.</summary>
        public const string Scale = "scale";
        /// <summary>The target's +Z points at the source.</summary>
        public const string LookAt = "lookAt";
        /// <summary>The target follows as a child of the source would: position and rotation in the source's frame.</summary>
        public const string Parent = "parent";

        static readonly string[] all = { Position, Rotation, Scale, LookAt, Parent };

        /// <summary>Every component, in the order they are written.</summary>
        public static IReadOnlyList<string> All => all;

        /// <summary>True for a component this reader knows (ordinal).</summary>
        public static bool IsKnown(string component) => Array.IndexOf(all, component) >= 0;

        /// <summary>True when the two may not stand in one entry: <c>parent</c> with <c>position</c>, <c>rotation</c> or
        /// <c>lookAt</c>; <c>lookAt</c> with <c>rotation</c>. Symmetric.</summary>
        public static bool Refused(string a, string b) => Excludes(a, b) || Excludes(b, a);

        static bool Excludes(string a, string b) =>
            (a == Parent && (b == Position || b == Rotation || b == LookAt)) || (a == LookAt && b == Rotation);

        /// <summary>The first rule the set breaks, or null: at least one component, each known and named once, no refused pair.</summary>
        public static string SetProblem(IReadOnlyList<string> set)
        {
            if (set == null || set.Count == 0) return "no component";
            for (int i = 0; i < set.Count; i++)
            {
                if (!IsKnown(set[i])) return "unknown component \"" + set[i] + "\"";
                for (int k = 0; k < i; k++)
                {
                    if (set[k] == set[i]) return "the component " + set[i] + " is named twice";
                    if (Refused(set[k], set[i])) return "the components " + set[k] + " and " + set[i] + " cannot stand together";
                }
            }
            return null;
        }

        /// <summary>A new list with the known components in the order of <see cref="All"/>, then what is unknown in its given order;
        /// nothing is left out, a repeated name included.</summary>
        public static List<string> Ordered(IReadOnlyList<string> set)
        {
            var r = new List<string>(set?.Count ?? 0);
            if (set == null) return r;
            foreach (var known in all)
                foreach (var c in set)
                    if (c == known) r.Add(c);
            foreach (var c in set)
                if (!IsKnown(c)) r.Add(c);
            return r;
        }
    }

    /// <summary>The limits of the list. A command that would exceed one is refused; a file over one loads with nothing dropped.</summary>
    public static class ConstraintLimits
    {
        /// <summary>Entries of the list. A file's entries beyond it are kept and inactive.</summary>
        public const int MaxEntries = 256;
        /// <summary>Characters of a label.</summary>
        public const int MaxLabel = 256;
        /// <summary>Each component of an offset an edit may set lies in [-MaxOffset, MaxOffset].</summary>
        public const float MaxOffset = 1e6f;
    }

    /// <summary>Finding codes of constraints.</summary>
    public static class ConstraintFindings
    {
        /// <summary>Info: a repeated id; the later entry got a derived one.</summary>
        public const string Id = "constraint.id";
        /// <summary>Info: a weight outside 0..1, stored as read and evaluated clamped.</summary>
        public const string Range = "constraint.range";
        /// <summary>Warning: a label shortened, or entries beyond the limit kept inactive.</summary>
        public const string Limit = "constraint.limit";
        /// <summary>Warning: an entry that cannot act; kept, inactive. An unknown string reports <c>json.unknownEnum</c> instead.</summary>
        public const string BadEntry = "constraint.badEntry";
        /// <summary>Warning: an address that is not canonical; kept, inactive.</summary>
        public const string BadAddress = "constraint.badAddress";
        /// <summary>Info: an end names a node the file does not have; kept, inactive.</summary>
        public const string UnknownNode = "constraint.unknownNode";
        /// <summary>Info, at compile: bone names the body lacks after a build.</summary>
        public const string Stale = "constraint.stale";
        /// <summary>Warning, at compile: an entry suspended because it closes a loop.</summary>
        public const string Cycle = "constraint.cycle";
        /// <summary>Warning, at compile: an entry on a bone that is not editable; kept, not applied.</summary>
        public const string MosaicProtected = "constraint.mosaicProtected";
    }

    /// <summary>One end of a constraint. <see cref="Node"/> is required; both of <see cref="Bone"/> and <see cref="Ik"/> null = the node
    /// itself. A source never holds <see cref="Ik"/>: a file's <c>ik</c> on a source is an unknown member, kept as one.</summary>
    public sealed class ConstraintEnd : DocObject
    {
        /// <summary>The node's id. Required.</summary>
        public string Node;
        /// <summary>A bone address, as the advanced pose's (<see cref="PoseAddressForm.Bone"/>); null = none.</summary>
        public string Bone;
        /// <summary>One of <see cref="IkTargets.Names"/>, on a target only; null = none.</summary>
        public string Ik;

        bool isSource;

        public ConstraintEnd() { }

        public ConstraintEnd(string node, string bone = null, string ik = null)
        {
            Node = node;
            Bone = bone;
            Ik = ik;
        }

        /// <summary>True when the end is the node itself.</summary>
        public bool IsNode => Bone == null && Ik == null;

        /// <summary>The same node, bone and IK target; unknown members are not compared.</summary>
        public bool SameAs(ConstraintEnd other) =>
            other != null && string.Equals(Node, other.Node, StringComparison.Ordinal) && string.Equals(Bone, other.Bone, StringComparison.Ordinal) &&
            string.Equals(Ik, other.Ik, StringComparison.Ordinal);

        /// <summary>A copy holding nothing of this object.</summary>
        public ConstraintEnd Clone() => new ConstraintEnd(Node, Bone, Ik) { isSource = isSource, Unknown = Unknown?.Clone() };

        internal static ConstraintEnd ReadSource(DocReader r)
        {
            var e = new ConstraintEnd { isSource = true };
            e.ReadFrom(r);
            return e;
        }

        internal override void ReadMembers(DocReader r)
        {
            Node = r.Id("node");
            Bone = r.Str("bone", null);
            if (!isSource) Ik = r.EnumStr("ik", null, IkTargets.Names);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "node", Node);
            if (Bone != null) w.Str("bone", Bone);
            if (Ik != null) w.Str("ik", Ik);
        }

        public override string ToString() => Node + (Bone != null ? " bone " + Bone : "") + (Ik != null ? " ik " + Ik : "");
    }

    /// <summary>The offsets an entry holds. All three are always written; a member no component uses is the identity.</summary>
    public sealed class ConstraintOffset : DocObject
    {
        public Vec3 Pos = Vec3.Zero;
        /// <summary>Euler degrees, as a value a user edits.</summary>
        public Vec3 Rot = Vec3.Zero;
        public Vec3 Scale = Vec3.One;

        public ConstraintOffset() { }
        public ConstraintOffset(Vec3 pos, Vec3 rot, Vec3 scale) { Pos = pos; Rot = rot; Scale = scale; }

        /// <summary>A copy holding nothing of this object.</summary>
        public ConstraintOffset Clone() => new ConstraintOffset(Pos, Rot, Scale) { Unknown = Unknown?.Clone() };

        internal override void ReadMembers(DocReader r)
        {
            Pos = r.V3("pos", Pos);
            Rot = r.Euler("rot", Rot);
            Scale = r.V3("scale", Scale);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.V3("pos", Pos);
            w.Euler("rot", Rot);
            w.V3("scale", Scale);
        }
    }

    /// <summary>
    /// One constraint. Build a fresh object for an edit (<see cref="Clone"/>, then change it) and hand it to the edit command; the
    /// entries of the model's list are shared with the history and are never changed in place.
    /// </summary>
    public sealed class ConstraintEntry : DocObject
    {
        /// <summary>32 hex digits, unique in the list. Required.</summary>
        public string Id;
        /// <summary>Default ""; a row shows it, else "source → target".</summary>
        public string Label = "";
        public bool On = true;
        /// <summary>As stored: a file's value outside 0..1 stays; <see cref="AppliedWeight"/> is what evaluates.</summary>
        public float Weight = 1f;
        public ConstraintEnd Source = new ConstraintEnd();
        public ConstraintEnd Target = new ConstraintEnd();
        /// <summary>A set of <see cref="ConstraintComponents"/>; a file's unknown strings are kept here.</summary>
        public List<string> Components = new List<string>();
        public ConstraintOffset Offset = new ConstraintOffset();

        /// <summary>The weight as it evaluates: clamped to 0..1.</summary>
        public float AppliedWeight => Weight <= 0f ? 0f : Weight >= 1f ? 1f : Weight;

        /// <summary>True when the set holds <paramref name="component"/> (ordinal).</summary>
        public bool Has(string component) => Components != null && Components.Contains(component);

        /// <summary>True when a component uses <see cref="ConstraintOffset.Pos"/>.</summary>
        public bool UsesPos => Has(ConstraintComponents.Position) || Has(ConstraintComponents.Parent);
        /// <summary>True when a component uses <see cref="ConstraintOffset.Rot"/>.</summary>
        public bool UsesRot => Has(ConstraintComponents.Rotation) || Has(ConstraintComponents.LookAt) || Has(ConstraintComponents.Parent);
        /// <summary>True when a component uses <see cref="ConstraintOffset.Scale"/>.</summary>
        public bool UsesScale => Has(ConstraintComponents.Scale);

        /// <summary>True for the four IK targets that take a rotation: hands and feet.</summary>
        public static bool IkTakesRotation(string ik) => ik == "LeftHand" || ik == "RightHand" || ik == "LeftFoot" || ik == "RightFoot";

        /// <summary>
        /// Why the entry cannot act whatever the scene holds, or null: the state <c>bad</c> of a kept entry. A component set that is
        /// unknown, empty or refused; a target naming a bone and an IK target; an unknown IK target, or one that cannot take a
        /// component; an address that is not canonical. What depends on the node's kind is judged where that is known.
        /// </summary>
        public string BadReason()
        {
            string p = ConstraintComponents.SetProblem(Components);
            if (p != null) return p;
            if (Target == null || Source == null) return "an end is missing";
            if (Target.Bone != null && Target.Ik != null) return "the target names a bone and an IK target";
            if (Target.Ik != null)
            {
                if (Array.IndexOf(IkTargets.Names, Target.Ik) < 0) return "unknown IK target \"" + Target.Ik + "\"";
                if (Has(ConstraintComponents.Scale)) return "an IK target takes no scale";
                if (!IkTakesRotation(Target.Ik) && (Has(ConstraintComponents.Rotation) || Has(ConstraintComponents.LookAt)))
                    return "the IK target " + Target.Ik + " takes no rotation";
            }
            return AddressProblem(Source.Bone, "source") ?? AddressProblem(Target.Bone, "target");
        }

        static string AddressProblem(string bone, string end)
        {
            if (bone == null || PoseAddressGrammar.IsCanonical(PoseAddressForm.Bone, bone)) return null;
            string canonical = PoseAddressGrammar.Canonical(PoseAddressForm.Bone, bone);
            return "the " + end + " address \"" + bone + "\" is not canonical" + (canonical != null ? " (" + canonical + ")" : "");
        }

        /// <summary>
        /// The first rule the entry breaks as the result of an edit, or null: everything of <see cref="BadReason"/>, a valid id, valid
        /// node ids, a source without an IK target, a weight in 0..1, a label within the limit, finite offsets within the limit. The
        /// cycle rule is the operation's, since it needs the built objects.
        /// </summary>
        public string Problem()
        {
            if (!JsonText.IsId(Id)) return "no valid id";
            if (Source == null || !JsonText.IsId(Source.Node)) return "the source names no node";
            if (Target == null || !JsonText.IsId(Target.Node)) return "the target names no node";
            if (Source.Ik != null) return "an IK target is no source";
            string bad = BadReason();
            if (bad != null) return bad;
            if (!(Weight >= 0f && Weight <= 1f)) return "weight " + F(Weight) + " outside 0..1";
            if (Label == null) return "label is null";
            if (Label.Length > ConstraintLimits.MaxLabel) return "label over " + ConstraintLimits.MaxLabel + " characters";
            if (Offset == null) return "offset is null";
            return OffsetProblem(Offset.Pos, "pos") ?? OffsetProblem(Offset.Rot, "rot") ?? OffsetProblem(Offset.Scale, "scale");
        }

        static string OffsetProblem(Vec3 v, string name) =>
            InRange(v.X) && InRange(v.Y) && InRange(v.Z) ? null : "offset " + name + " " + v + " outside +-" + F(ConstraintLimits.MaxOffset);

        static bool InRange(float x) => x >= -ConstraintLimits.MaxOffset && x <= ConstraintLimits.MaxOffset;

        /// <summary>
        /// The form an edit stores: components in writing order and, when every component is known, each offset no component uses at
        /// its identity. Returns this object when it already has that form, else a changed copy; this object is never changed.
        /// </summary>
        public ConstraintEntry Normalized()
        {
            var ordered = ConstraintComponents.Ordered(Components);
            bool allKnown = true;
            foreach (var c in ordered) allKnown &= ConstraintComponents.IsKnown(c);
            bool clearPos = allKnown && !UsesPos && Offset != null && !Offset.Pos.Equals(Vec3.Zero);
            bool clearRot = allKnown && !UsesRot && Offset != null && !Offset.Rot.Equals(Vec3.Zero);
            bool clearScale = allKnown && !UsesScale && Offset != null && !Offset.Scale.Equals(Vec3.One);
            bool sameOrder = Components != null && ordered.Count == Components.Count;
            for (int i = 0; sameOrder && i < ordered.Count; i++) sameOrder = ordered[i] == Components[i];
            if (sameOrder && !clearPos && !clearRot && !clearScale) return this;
            var e = Clone();
            e.Components = ordered;
            if (clearPos) e.Offset.Pos = Vec3.Zero;
            if (clearRot) e.Offset.Rot = Vec3.Zero;
            if (clearScale) e.Offset.Scale = Vec3.One;
            return e;
        }

        /// <summary>A deep copy: nothing is shared with this entry, unknown members kept.</summary>
        public ConstraintEntry Clone() => new ConstraintEntry
        {
            Id = Id,
            Label = Label,
            On = On,
            Weight = Weight,
            Source = Source?.Clone(),
            Target = Target?.Clone(),
            Components = Components == null ? null : new List<string>(Components),
            Offset = Offset?.Clone(),
            Unknown = Unknown?.Clone(),
        };

        // ---------- reading ----------

        internal override void ReadMembers(DocReader r)
        {
            Id = r.Id("id");
            Label = r.Str("label", "");
            On = r.Bool("on", true);
            Weight = r.F32("weight", 1f);
            var source = r.ObjReq("source");
            Source = ConstraintEnd.ReadSource(source);
            var target = r.ObjReq("target");
            Target = ReadNew<ConstraintEnd>(target);
            Components = ConstraintComponents.Ordered(r.Array("components", v => v.AsStr()));
            Offset = ReadNew<ConstraintOffset>(r.Obj("offset"));

            if (Label.Length > ConstraintLimits.MaxLabel)
            {
                r.Report(FindingSeverity.Warning, ConstraintFindings.Limit, "label", "label of " + Label.Length + " characters shortened to " + ConstraintLimits.MaxLabel);
                int cut = ConstraintLimits.MaxLabel;
                if (char.IsHighSurrogate(Label[cut - 1])) cut--;
                Label = Label.Substring(0, cut);
            }
            if (!(Weight >= 0f && Weight <= 1f))
                r.Report(FindingSeverity.Info, ConstraintFindings.Range, "weight", "weight " + F(Weight) + " outside 0..1 stored as read, evaluated as " + F(AppliedWeight));
            ReportBad(r, source, target);
        }

        // One finding for each thing that keeps the entry from acting. An unknown `ik` was reported when it was read.
        void ReportBad(DocReader r, DocReader source, DocReader target)
        {
            var known = new List<string>(Components.Count);
            foreach (var c in Components)
            {
                if (ConstraintComponents.IsKnown(c)) known.Add(c);
                else r.Report(FindingSeverity.Warning, "json.unknownEnum", "components", "unknown value \"" + c + "\" kept");
            }
            if (Components.Count == 0) r.Report(FindingSeverity.Warning, ConstraintFindings.BadEntry, "components", "no component; kept, inactive");
            else if (known.Count > 0)
            {
                string set = ConstraintComponents.SetProblem(known);
                if (set != null) r.Report(FindingSeverity.Warning, ConstraintFindings.BadEntry, "components", set + "; kept, inactive");
            }
            if (Target.Bone != null && Target.Ik != null)
                target.Report(FindingSeverity.Warning, ConstraintFindings.BadEntry, null, "the target names a bone and an IK target; kept, inactive");
            else if (Target.Ik != null && Array.IndexOf(IkTargets.Names, Target.Ik) >= 0)
            {
                if (Has(ConstraintComponents.Scale))
                    target.Report(FindingSeverity.Warning, ConstraintFindings.BadEntry, "ik", "an IK target takes no scale; kept, inactive");
                if (!IkTakesRotation(Target.Ik) && (Has(ConstraintComponents.Rotation) || Has(ConstraintComponents.LookAt)))
                    target.Report(FindingSeverity.Warning, ConstraintFindings.BadEntry, "ik", "the IK target " + Target.Ik + " takes no rotation; kept, inactive");
            }
            ReportAddress(source, Source.Bone, "source");
            ReportAddress(target, Target.Bone, "target");
        }

        static void ReportAddress(DocReader at, string bone, string end)
        {
            string why = AddressProblem(bone, end);
            if (why != null) at.Report(FindingSeverity.Warning, ConstraintFindings.BadAddress, "bone", why + "; kept, inactive");
        }

        // ---------- writing ----------

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqId(w, "id", Id);
            ReqStr(w, "label", Label);
            w.Bool("on", On);
            w.F32("weight", Weight);
            Source.WriteTo(w, "source");
            Target.WriteTo(w, "target");
            w.StrArray("components", ConstraintComponents.Ordered(Components));
            Offset.WriteTo(w, "offset");
        }

        /// <summary>The canonical JSON bytes of the entry (UTF-8, trailing LF), as scene.json holds it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(ConstraintList.MemberName, WriteTo);

        /// <summary>The canonical JSON text of the entry (no trailing LF).</summary>
        public string ToJson()
        {
            var b = ToBytes();
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }

        internal static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The constraints of a scene (the scene.json member <c>constraints</c>), in list order. The model holds null rather than an empty
    /// list. A list in the model is not changed in place: an edit puts a new list there.
    /// </summary>
    public sealed class ConstraintList
    {
        /// <summary>The member name in scene.json.</summary>
        public const string MemberName = "constraints";

        /// <summary>The entries, in list order (no null element on write).</summary>
        public List<ConstraintEntry> Entries = new List<ConstraintEntry>();

        public ConstraintList() { }
        public ConstraintList(IEnumerable<ConstraintEntry> entries) { Entries.AddRange(entries); }

        public int Count => Entries?.Count ?? 0;

        /// <summary>The index of the entry with this id, or −1.</summary>
        public int IndexOf(string id)
        {
            if (id == null || Entries == null) return -1;
            for (int i = 0; i < Entries.Count; i++) if (Entries[i] != null && Entries[i].Id == id) return i;
            return -1;
        }

        /// <summary>The entry with this id, or null.</summary>
        public ConstraintEntry Find(string id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : Entries[i];
        }

        /// <summary>True when the entry at <paramref name="index"/> lies beyond <see cref="ConstraintLimits.MaxEntries"/>: kept, inactive.</summary>
        public static bool BeyondLimit(int index) => index >= ConstraintLimits.MaxEntries;

        /// <summary>A new list with <paramref name="entry"/> in place of the entry at <paramref name="index"/>; the other entries are shared.</summary>
        public ConstraintList With(int index, ConstraintEntry entry)
        {
            var l = new ConstraintList(Entries);
            l.Entries[index] = entry;
            return l;
        }

        /// <summary>A deep copy: nothing is shared with this list, unknown members kept.</summary>
        public ConstraintList Clone()
        {
            var l = new ConstraintList();
            foreach (var e in Entries) l.Entries.Add(e?.Clone());
            return l;
        }

        /// <summary>
        /// The first rule this list breaks as the result of an edit, or null: at most <see cref="ConstraintLimits.MaxEntries"/> entries,
        /// ids distinct, and every entry's own rules (<see cref="ConstraintEntry.Problem"/>). What <paramref name="before"/> already held
        /// is not the edit's doing: a file's list over the limit may shrink, and an entry keeps a problem it came with while other
        /// members or other entries are edited.
        /// </summary>
        public string Problem(ConstraintList before = null)
        {
            if (Entries == null) return "entries is null";
            if (Entries.Count > ConstraintLimits.MaxEntries && Entries.Count > (before?.Count ?? 0)) return "more than " + ConstraintLimits.MaxEntries + " constraints";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e == null) return "entry " + i + " is null";
                if (e.Id != null && !ids.Add(e.Id)) return "the id " + e.Id + " is used twice";
                var old = before?.Find(e.Id);
                if (ReferenceEquals(old, e)) continue;
                string p = e.Problem();
                if (p != null && p != old?.Problem()) return "constraint " + (e.Id ?? i.ToString(CultureInfo.InvariantCulture)) + ": " + p;
            }
            return null;
        }

        // ---------- reading ----------

        /// <summary>Reads the member of the scene document's root; null when absent or empty, so the model never holds an empty list.</summary>
        internal static ConstraintList Read(DocReader root)
        {
            var items = root.ObjArray(MemberName);
            if (items.Count == 0) return null;
            var l = new ConstraintList();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var e = new ConstraintEntry();
                e.ReadFrom(items[i]);
                if (!ids.Add(e.Id))
                {
                    string old = e.Id;
                    e.Id = JsonText.DerivedId("constraint/" + i);
                    for (int n = 1; !ids.Add(e.Id); n++) e.Id = JsonText.DerivedId("constraint/" + i + "/" + n);
                    items[i].Report(FindingSeverity.Info, ConstraintFindings.Id, "id", "constraint id " + old + " repeats an earlier entry's; derived id " + e.Id + " used");
                }
                l.Entries.Add(e);
            }
            if (l.Count > ConstraintLimits.MaxEntries)
                root.Report(FindingSeverity.Warning, ConstraintFindings.Limit, MemberName, (l.Count - ConstraintLimits.MaxEntries) + " constraints over the limit of " + ConstraintLimits.MaxEntries + " kept, inactive");
            return l;
        }

        /// <summary>
        /// Reads a list from JSON text (one array) with the reader's rules and repairs; findings go to <paramref name="findings"/>. A
        /// syntax error, a type error or a missing required member throws <see cref="SaveFormatException"/>. Null for an empty array.
        /// </summary>
        public static ConstraintList Parse(string json, FindingList findings = null, string entry = MemberName) =>
            FromToken(DocReader.ParseText(json ?? "", entry), findings, entry);

        /// <summary>As <see cref="Parse"/> for a parsed token (an array; anything else throws BadDocument). The token is not changed.</summary>
        public static ConstraintList FromToken(JToken token, FindingList findings = null, string entry = MemberName, bool lenient = false)
        {
            if (!(token is JArray a))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "the constraints are an array, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            return Read(new DocReader(new JObject(new JProperty(MemberName, a.DeepClone())), new DocReadContext(entry, lenient, findings ?? new FindingList())));
        }

        /// <summary>Reports each end that names a node <paramref name="exists"/> does not know (info; the entry is kept, inactive).</summary>
        public void ReportUnknownNodes(Func<string, bool> exists, DocReadContext ctx, string basePath = MemberName)
        {
            if (exists == null || ctx == null || Entries == null) return;
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (e == null) continue;
                Report(e.Source, "source");
                Report(e.Target, "target");

                void Report(ConstraintEnd end, string name)
                {
                    if (end?.Node == null || exists(end.Node)) return;
                    ctx.Findings.Info(ConstraintFindings.UnknownNode, ctx.Where(basePath + "[" + i + "]." + name + ".node"),
                        "the " + name + " of constraint " + i + " names node " + end.Node + ", which is not in the scene; kept, inactive");
                }
            }
        }

        // ---------- writing ----------

        /// <summary>Writes the array as a member (the caller decides whether the scene has one).</summary>
        internal void WriteTo(CanonicalJsonWriter w, string name) => DocObject.WriteList(w, name, Entries);

        /// <summary>The canonical JSON text of the array (no trailing LF).</summary>
        public string ToJson()
        {
            var b = CanonicalJsonWriter.ToDocumentBytes(MemberName, w =>
            {
                string at = w.Path;
                w.StartArray();
                for (int i = 0; i < Count; i++)
                {
                    if (Entries[i] == null) throw DocObject.NullElement(w, at, i);
                    Entries[i].WriteTo(w);
                }
                w.EndArray();
            });
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }
    }
}
