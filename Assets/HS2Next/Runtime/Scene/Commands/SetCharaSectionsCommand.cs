// A partial character load (HS2's Maker load with fewer than its five parts; undoable): chosen sections of a source
// character document replace the same sections of a character's stored document, in place. «顔» takes the face, «体» the
// body, «髪» the hair, «キャラ設定» the name, futanari and the imported game parameters (never the sex); plugin data follows
// the part it shapes — a skin overlay by its target group, a bone modifier by whether its bone is a head name of its own
// character's head, a MaterialEditor character item by whether its renderer or material is a head name (HeadNames) — and
// the Studio display switches go with their part (eyes with the face, skin with the body, clothes with «コーデ», whose
// coordinate document is SetCoordinateCommand's). Lists split by class keep the character's own entries of the classes
// not loaded, in their order, then take the source's entries of the loaded classes, in theirs. The chara-scope inline ext,
// unknown members and the sex stay the character's; the subtree's ext data and provenance are not touched. The document
// object stays the same one (the built character's editor reads it); its contents are replaced from the merged canonical
// bytes, and undo puts the previous bytes back. Changes are reported per second-level member that changed
// (`appearance.chara.face.<member>`, `.body.<member>`, `.hair.<part or member>`, `.profile.name | futanari | hs2`,
// `.materialOverrides`, `.studioOverlays`), never the whole document, so the realization swaps what changed. A source of
// the other sex is refused (bad_args). Unity-free.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes
{
    public sealed class SetCharaSectionsCommand : ISceneCommand
    {
        /// <summary>The sections the command takes: the four document parts, and «コーデ» for its Studio display switch only
        /// (`studioOverlays.clothes`; the coordinate itself is SetCoordinateCommand's).</summary>
        public const LoadFlags Allowed = LoadFlags.Face | LoadFlags.Body | LoadFlags.Hair | LoadFlags.Profile | LoadFlags.Coordinate;

        /// <summary>The second-level groups the command reports under (in this order).</summary>
        static readonly string[] Groups = { "profile", "face", "body", "hair" };

        readonly string id;
        readonly byte[] source;
        readonly int sourceSex;
        readonly LoadFlags sections;
        readonly IHeadNames names;
        byte[] oldBytes;
        bool changed;
        readonly List<string> reported = new List<string>();

        /// <param name="nodeId">The character node.</param>
        /// <param name="source">The source character document (copied now; its sex must be the character's).</param>
        /// <param name="sections">Face, Body, Hair, Profile and Coordinate (its display switch) in any combination.</param>
        /// <param name="names">The head names of the face / body rule; null = HeadNames.Current.</param>
        public SetCharaSectionsCommand(string nodeId, CharaDocument source, LoadFlags sections, IHeadNames names = null, string label = "Load character")
        {
            id = nodeId;
            Label = label;
            this.sections = sections;
            this.names = names;
            if (source != null)
            {
                this.source = Bytes(source);
                sourceSex = source.Profile.Sex;
            }
        }

        public string Label { get; }
        public LoadFlags Sections => sections;

        /// <summary>The paths the last Apply / Revert reported (tests, the op's report).</summary>
        public IReadOnlyList<string> Reported => reported;

        static byte[] Bytes(CharaDocument d) => CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write);

        static CharaDocument Read(byte[] b) => CharaDocument.Read(DocReader.ForDocument(b, new DocReadContext(CharaDocument.EntryName)));

        static void ReadInto(CharaDocument target, byte[] b) => target.ReadFrom(DocReader.ForDocument(b, new DocReadContext(CharaDocument.EntryName)));

        static CharaSubtree Stored(SceneModel m, string id)
        {
            var n = m.Find(id) ?? throw new CommandException(CommandCodes.NotFound, "node " + id + " is not in the scene");
            if (n.Kind != NodeKind.Chara) throw new CommandException(CommandCodes.WrongKind, "node " + id + " is not a character");
            return n.Appearance?.Stored ?? throw new CommandException(CommandCodes.Unsupported, "character not imported (node " + id + ")");
        }

        public void Validate(SceneModel m)
        {
            var st = Stored(m, id);
            if (source == null) throw new CommandException(CommandCodes.BadArgs, "no source character");
            if (sections == LoadFlags.None) throw new CommandException(CommandCodes.BadArgs, "no section to load");
            if ((sections & ~Allowed) != 0) throw new CommandException(CommandCodes.BadArgs, "sections " + (int)sections + " are not face, body, hair, coordinate or profile");
            int sex = st.Chara.Profile.Sex;
            if (sourceSex != sex)
                throw new CommandException(CommandCodes.BadArgs, "a partial load needs a character of the same sex: the source is " + SexName(sourceSex) + ", character " + id + " is " + SexName(sex));
        }

        static string SexName(int sex) => sex == 0 ? "male" : "female";

        public void Apply(SceneModel m, ChangeSink s)
        {
            var st = Stored(m, id);
            oldBytes = Bytes(st.Chara);
            var newBytes = Merge(oldBytes, source, sections, names ?? HeadNames.Current);
            changed = !Same(oldBytes, newBytes);
            reported.Clear();
            if (!changed) return;
            var before = DocReader.Parse(oldBytes, CharaDocument.EntryName);
            var after = DocReader.Parse(newBytes, CharaDocument.EntryName);
            ReadInto(st.Chara, newBytes);
            Report(before, after);
            foreach (var p in reported) s.NodeChanged(id, p);
        }

        public void Revert(SceneModel m, ChangeSink s)
        {
            if (!changed) return;
            var st = Stored(m, id);
            ReadInto(st.Chara, oldBytes);
            foreach (var p in reported) s.NodeChanged(id, p);
        }

        /// <summary>
        /// The canonical bytes of <paramref name="target"/> with the <paramref name="sections"/> of <paramref name="source"/>
        /// (both canonical chara.json bytes) by the rule of the class comment; the target's sex, inline ext and unknown
        /// members are kept.
        /// </summary>
        public static byte[] Merge(byte[] target, byte[] source, LoadFlags sections, IHeadNames names)
        {
            names = names ?? HeadNames.Vanilla;
            var t = Read(target);
            var s = Read(source);
            bool face = (sections & LoadFlags.Face) != 0, body = (sections & LoadFlags.Body) != 0;
            var headT = names.Of(t.Profile.Sex, t.Face.Head);
            var headS = names.Of(s.Profile.Sex, s.Face.Head);
            bool Take(bool isFace) => isFace ? face : body;

            var overlays = Split(t.Body.SkinOverlays, s.Body.SkinOverlays, o => Take(HeadNames.IsFace(o)), o => Take(HeadNames.IsFace(o)));
            var mods = Split(t.Body.BoneMods, s.Body.BoneMods, b => Take(HeadNames.IsFace(b, headT)), b => Take(HeadNames.IsFace(b, headS)));
            var items = Split(t.MaterialOverrides, s.MaterialOverrides, i => Take(HeadNames.IsFace(i, headT)), i => Take(HeadNames.IsFace(i, headS)));

            if (face)
            {
                t.Face = s.Face;
                t.StudioOverlays.Eyes = s.StudioOverlays.Eyes;
            }
            if (body)
            {
                t.Body = s.Body;
                t.StudioOverlays.Skin = s.StudioOverlays.Skin;
            }
            t.Body.SkinOverlays = overlays;
            t.Body.BoneMods = mods;
            t.MaterialOverrides = items;
            if ((sections & LoadFlags.Hair) != 0) t.Hair = s.Hair;
            if ((sections & LoadFlags.Coordinate) != 0) t.StudioOverlays.Clothes = s.StudioOverlays.Clothes;
            if ((sections & LoadFlags.Profile) != 0)
            {
                t.Profile.Name = s.Profile.Name;
                t.Profile.Futanari = s.Profile.Futanari;
                t.Profile.Hs2 = s.Profile.Hs2;
            }
            return Bytes(t);
        }

        // The target's entries whose class is not taken, in order, then the source's entries whose class is taken, in order.
        static List<T> Split<T>(List<T> target, List<T> source, Func<T, bool> takenT, Func<T, bool> takenS)
        {
            var r = new List<T>();
            foreach (var x in target ?? new List<T>()) if (!takenT(x)) r.Add(x);
            foreach (var x in source ?? new List<T>()) if (takenS(x)) r.Add(x);
            return r;
        }

        // The second-level members that differ, in document order: profile, face, body, hair members, then the two top-level lists.
        void Report(JObject before, JObject after)
        {
            foreach (var g in Groups)
            {
                var a = before[g] as JObject;
                var b = after[g] as JObject;
                var keys = new List<string>();
                if (a != null) foreach (var p in a.Properties()) keys.Add(p.Name);
                if (b != null) foreach (var p in b.Properties()) if (!keys.Contains(p.Name)) keys.Add(p.Name);
                foreach (var k in keys)
                    if (!JToken.DeepEquals(a?[k], b?[k])) reported.Add(AppearancePaths.Chara(g + "." + k));
            }
            foreach (var top in new[] { "materialOverrides", "studioOverlays" })
                if (!JToken.DeepEquals(before[top], after[top])) reported.Add(AppearancePaths.Chara(top));
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public bool TryMerge(ISceneCommand next) => false;
        public long EstimatedBytes => 256 + (source?.Length ?? 0) + (oldBytes?.Length ?? 0) * 2L;
        public IEnumerable<TreeNode> HeldNodes => Array.Empty<TreeNode>();
    }
}
