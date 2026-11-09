// Scene scripts: the scene.json member `scripts` (an array after `ragdoll`, before `ext`, written only when the scene has a script) and the
// document of a `.hs2nscript` file (format "hs2next.script", version 1). A script names Studio objects only through its roles ("$role"), so it
// plays in any scene once its roles are cast: roles (characters and props), set-up steps (sent at every play), preparation steps (sent once per
// scene and cast) and a Timeline whose track keys and depth-of-field focus nodes may be "$role". A scene script adds what belongs to its scene:
// the cast (role -> node id), the preparation's mark and the store number it came from. Unity-free save-layer classes, as the Timeline's are; the
// runtime model holds one list (SceneModel.Scripts, null = the scene has none) and only the script edit command replaces it.
//
// The reader binds strictly (a missing required member or a wrong type fails the load as for every core member) and then repairs what a
// hand-made or damaged file may hold, reporting each repair: a "$x" that names no role is kept (the step, event or track is skipped at play), a
// repeated role name drops the later role, a cast entry naming no node is kept (the role is uncast at play), a step that may not be sent is kept
// and never sent, a wait out of range is clamped, the excess over a limit is dropped from the end, a repeated script id gets a derived id. A
// script of another format or of a newer version in a scene is kept as read and never played; a standalone document of either is refused.
// The writer emits every member in schema order.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HS2Next.Save
{
    /// <summary>Finding codes of scene scripts and of the script store.</summary>
    public static class ScriptFindings
    {
        /// <summary>Info: a document without an id got one.</summary>
        public const string Id = "script.id";
        /// <summary>A document of another format: refused (a scene keeps it as read, warning).</summary>
        public const string BadFormat = "script.badFormat";
        /// <summary>A document of a newer version: refused with its number (a scene keeps it as read, warning).</summary>
        public const string NewerVersion = "script.newerVersion";
        /// <summary>Warning: a "$x" naming no role of the script; kept, the step, event or track is skipped at play.</summary>
        public const string UnknownRole = "script.unknownRole";
        /// <summary>Warning: a role name used twice; the later role is dropped.</summary>
        public const string DuplicateRole = "script.duplicateRole";
        /// <summary>Info: a cast entry naming a node the scene does not have; kept, the role is uncast at play.</summary>
        public const string CastMissing = "script.castMissing";
        /// <summary>Warning: a step or a timed command that may not be sent (a script command, a Timeline edit, no command, an id member); kept, never sent.</summary>
        public const string BadStep = "script.badStep";
        /// <summary>Warning: a wait out of range clamped, a text shortened, or the excess over a limit dropped from the end.</summary>
        public const string Limit = "script.limit";
        /// <summary>Info: two scene scripts with one id; the later got a derived id.</summary>
        public const string DuplicateId = "script.duplicateId";
        /// <summary>Info: a store script holds a node id where a role belongs (meaningful only in the scene it came from).</summary>
        public const string NodeKey = "script.nodeKey";
        /// <summary>Warning (set, import): a step whose command the registry does not know.</summary>
        public const string UnknownCommand = "script.unknownCommand";
        /// <summary>Warning (set, import): a set-up step whose command is structural (it clears the undo history at every play).</summary>
        public const string ClearsHistory = "script.clearsHistory";
        /// <summary>Warning: the store's index could not be read; the store reads as empty.</summary>
        public const string StoreUnreadable = "script.storeUnreadable";
        /// <summary>The store could not be written.</summary>
        public const string StoreWrite = "script.storeWrite";
    }

    /// <summary>The limits of a script, of a scene's scripts and of the store. An edit that would pass one is refused; a file over one loads with
    /// the excess dropped from the end (finding <see cref="ScriptFindings.Limit"/>).</summary>
    public static class ScriptLimits
    {
        /// <summary>Characters of a script's name.</summary>
        public const int MaxName = 128;
        /// <summary>Characters of a script's description.</summary>
        public const int MaxDescription = 2048;
        /// <summary>Roles of a script.</summary>
        public const int MaxRoles = 32;
        /// <summary>Set-up and preparation steps of a script together.</summary>
        public const int MaxSteps = 256;
        /// <summary>Scripts of a scene.</summary>
        public const int MaxScripts = 64;
        /// <summary>Seconds of a wait step: in (0, MaxWait]; a file's wait above is clamped to it.</summary>
        public const float MaxWait = 10f;
        /// <summary>The value a file's wait of 0 or less is clamped to (the player holds at least one frame).</summary>
        public const float MinWait = 0.001f;
        /// <summary>Characters of a role name (the pattern [A-Za-z_][A-Za-z0-9_]{0,31}).</summary>
        public const int MaxRoleName = 32;
        /// <summary>Characters of a role's label.</summary>
        public const int MaxLabel = 256;
        /// <summary>Rows of the store.</summary>
        public const int MaxStoreRows = 100000;
        /// <summary>Bytes of a .hs2nscript file that is imported.</summary>
        public const int MaxFileBytes = 16 * 1024 * 1024;
    }

    /// <summary>Role references, the hashes of scripts and the preparation's mark.</summary>
    public static class ScriptText
    {
        /// <summary>The mark of a role reference ("$role"); "$$" writes a literal "$".</summary>
        public const char RoleMark = '$';

        /// <summary>True for a role name: [A-Za-z_][A-Za-z0-9_]{0,31}.</summary>
        public static bool IsRoleName(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > ScriptLimits.MaxRoleName) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || (i > 0 && c >= '0' && c <= '9');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>The role name of a role reference "$name" (a whole string), or null for any other string ("$$x", "$5", "a$b", "$").</summary>
        public static string RoleOf(string s)
        {
            if (s == null || s.Length < 2 || s[0] != RoleMark) return null;
            string name = s.Substring(1);
            return IsRoleName(name) ? name : null;
        }

        /// <summary>"$" + <paramref name="role"/>.</summary>
        public static string RoleRef(string role) => RoleMark + role;

        /// <summary>True for 16 lowercase hex digits (a preparation mark, a compiled hash).</summary>
        public static bool IsHash16(string s) => JsonText.IsHex(s, 16, false);

        /// <summary>The first 16 lowercase hex digits of the SHA-256 of <paramref name="utf8"/>.</summary>
        public static string Hash16(byte[] utf8)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(utf8 ?? Array.Empty<byte>());
                Array.Resize(ref h, 8);
                return JsonText.Hex(h);
            }
        }

        /// <summary>The first 16 lowercase hex digits of the SHA-256 of the UTF-8 bytes of <paramref name="text"/>.</summary>
        public static string Hash16(string text) => Hash16(Encoding.UTF8.GetBytes(text ?? ""));

        /// <summary>
        /// The preparation's mark: the first 16 hex digits of the SHA-256 of the canonical JSON text (no trailing LF) of the array of the preparation
        /// steps after substitution, in order. Equal steps give equal marks.
        /// </summary>
        public static string PreparationMark(IEnumerable<JObject> substitutedSteps)
        {
            var a = new JArray();
            if (substitutedSteps != null) foreach (var s in substitutedSteps) a.Add(s == null ? (JToken)JValue.CreateNull() : s.DeepClone());
            return Hash16(CanonicalJsonWriter.ToText(a));
        }

        /// <summary>Calls <paramref name="visit"/> with every string value at any depth of <paramref name="token"/> and its path below it (member names are
        /// not visited).</summary>
        public static void VisitStrings(JToken token, string path, Action<string, string> visit)
        {
            if (token == null || visit == null) return;
            switch (token.Type)
            {
                case JTokenType.String:
                    visit((string)token, path);
                    return;
                case JTokenType.Object:
                    foreach (var p in ((JObject)token).Properties()) VisitStrings(p.Value, DocReader.Join(path, p.Name), visit);
                    return;
                case JTokenType.Array:
                    var a = (JArray)token;
                    for (int i = 0; i < a.Count; i++) VisitStrings(a[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", visit);
                    return;
            }
        }
    }

    /// <summary>The steps of a script: a request <c>{cmd, …}</c> (no <c>id</c>) or a wait <c>{wait: s}</c> (set-up and preparation only).</summary>
    public static class ScriptSteps
    {
        /// <summary>True for a wait step: no <c>cmd</c> member and a <c>wait</c> member.</summary>
        public static bool IsWait(JObject step) => step != null && step.Property("cmd") == null && step.Property("wait") != null;

        /// <summary>The seconds of a wait step (0 when it holds no number).</summary>
        public static float WaitOf(JObject step)
        {
            var t = step?["wait"];
            return t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (float)(double)t : 0f;
        }

        /// <summary>The command of a request step or a timed command (null when it has none).</summary>
        public static string CommandOf(JObject request) => request?["cmd"] is JValue v && v.Type == JTokenType.String ? (string)v : null;

        /// <summary>True for a command of the group <c>script</c>: a script does not play scripts.</summary>
        public static bool IsScriptCommand(string cmd) => cmd != null && cmd.StartsWith("script.", StringComparison.Ordinal);

        /// <summary>True for a command that replaces or edits the Timeline (the player owns the Timeline while it plays).</summary>
        public static bool IsTimelineEdit(string cmd) =>
            cmd == "timeline.build" || cmd == "timeline.set" || cmd == "timeline.events" || cmd == "timeline.clear" ||
            (cmd != null && (cmd.StartsWith("timeline.track.", StringComparison.Ordinal) || cmd.StartsWith("timeline.key.", StringComparison.Ordinal)));

        /// <summary>
        /// Why a step may never be sent, or null: not an object; a request without a command (a non-empty string), with an <c>id</c> member, of the group
        /// <c>script</c> or editing the Timeline; a wait where waits are not allowed. The wait's range is <see cref="WaitProblem"/>.
        /// </summary>
        public static string Refusal(JObject step, bool allowWait = true)
        {
            if (step == null) return "no step object";
            if (IsWait(step)) return allowWait ? null : "a wait is a set-up or preparation step";
            var cmd = step["cmd"];
            if (cmd == null || cmd.Type != JTokenType.String || ((string)cmd).Length == 0) return "the step has no cmd (and no wait)";
            if (step.Property("id") != null) return "the request carries an id member";
            string c = (string)cmd;
            if (IsScriptCommand(c)) return "the command " + c + " plays scripts (a script does not play scripts)";
            if (IsTimelineEdit(c)) return "the command " + c + " edits the Timeline (the player owns the Timeline while it plays)";
            return null;
        }

        /// <summary>Why a timed command may never fire in a script, or null: the Timeline's own rules, and no command of the group <c>script</c>.</summary>
        public static string EventRefusal(JObject request)
        {
            string why = TimelineEvent.CheckDo(request);
            if (why != null) return why;
            string c = CommandOf(request);
            return IsScriptCommand(c) ? "the command " + c + " plays scripts (a script does not play scripts)" : null;
        }

        /// <summary>Why a wait step's seconds are out of range (0, <see cref="ScriptLimits.MaxWait"/>], or null (also for a request).</summary>
        public static string WaitProblem(JObject step)
        {
            if (!IsWait(step)) return null;
            var t = step["wait"];
            if (t == null || (t.Type != JTokenType.Integer && t.Type != JTokenType.Float)) return "the wait is not a number of seconds";
            double s = (double)t;
            return s > 0 && s <= ScriptLimits.MaxWait ? null : "wait " + s.ToString("R", CultureInfo.InvariantCulture) + " s outside (0, " + Fs(ScriptLimits.MaxWait) + "] s";
        }

        /// <summary>A copy of <paramref name="step"/> with every member named <c>note</c> removed at any depth (what is sent).</summary>
        public static JObject WithoutNotes(JObject step)
        {
            if (step == null) return null;
            var c = (JObject)step.DeepClone();
            Strip(c);
            return c;
        }

        static void Strip(JToken t)
        {
            if (t is JObject o)
            {
                o.Remove("note");
                foreach (var p in o.Properties()) Strip(p.Value);
            }
            else if (t is JArray a) foreach (var e in a) Strip(e);
        }

        internal static string Fs(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>A script refused as a whole: a document of another format (<see cref="ScriptFindings.BadFormat"/>) or of a newer version
    /// (<see cref="ScriptFindings.NewerVersion"/>, with <see cref="Version"/>).</summary>
    public sealed class ScriptRefusedException : Exception
    {
        public readonly string Code;
        /// <summary>The document's version (for a newer version), else null.</summary>
        public readonly int? Version;
        public ScriptRefusedException(string code, string message, int? version = null) : base(message) { Code = code; Version = version; }
    }

    /// <summary><c>map</c>: the map a script was made on (<see cref="Ref"/> null = it did not resolve; the member null = any map).</summary>
    public sealed class ScriptMap : DocObject
    {
        /// <summary>"guid#no", or null when the map did not resolve.</summary>
        public string Ref;
        public string Name;

        public ScriptMap() { }
        public ScriptMap(string mapRef, string name) { Ref = mapRef; Name = name; }

        public ScriptMap Clone() => new ScriptMap(Ref, Name) { Unknown = Unknown?.Clone() };

        internal override void ReadMembers(DocReader r)
        {
            Ref = r.RefN("ref");
            Name = r.StrN("name");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ScriptRole.WriteRefText(w, "ref", Ref);
            w.Str("name", Name);
        }
    }

    /// <summary><c>hint</c> of a role: the name of the character or item it was made with, and a character's card.</summary>
    public sealed class ScriptHint : DocObject
    {
        public string Name;
        /// <summary>A card path, as <c>chara.load</c> takes it.</summary>
        public string Card;

        public ScriptHint() { }
        public ScriptHint(string name, string card = null) { Name = name; Card = card; }

        public ScriptHint Clone() => new ScriptHint(Name, Card) { Unknown = Unknown?.Clone() };

        internal override void ReadMembers(DocReader r)
        {
            Name = r.StrN("name");
            Card = r.StrN("card");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (Name != null) w.Str("name", Name);
            if (Card != null) w.Str("card", Card);
        }
    }

    /// <summary><c>parent</c> of a prop role: the character role it is parented to and the point ("48", "N_Hand_R"; null = the role's node).</summary>
    public sealed class ScriptRoleParent : DocObject
    {
        /// <summary>Required.</summary>
        public string Role;
        public string Point;

        public ScriptRoleParent() { }
        public ScriptRoleParent(string role, string point = null) { Role = role; Point = point; }

        public ScriptRoleParent Clone() => new ScriptRoleParent(Role, Point) { Unknown = Unknown?.Clone() };

        internal override void ReadMembers(DocReader r)
        {
            Role = r.Str("role");
            Point = r.StrN("point");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "role", Role);
            w.Str("point", Point);
        }
    }

    /// <summary>
    /// One role of a script. A character role (<see cref="KindChara"/>) is cast to a scene character; a prop role (<see cref="KindItem"/>) names its row by
    /// <see cref="Ref"/> ("guid#no") and may be parented to a character role and placed. Members written: role, kind; sex for a character; ref, pos,
    /// rot, scale for a prop; group, category, parent, hint, label when set (and any member that differs from its absent value on the other kind).
    /// </summary>
    public sealed class ScriptRole : DocObject
    {
        public const string KindChara = "chara", KindItem = "item";
        public const string SexFemale = "female", SexMale = "male";

        static readonly string[] kinds = { KindChara, KindItem };
        static readonly string[] sexes = { SexFemale, SexMale };
        public static IReadOnlyList<string> Kinds => kinds;

        /// <summary>The role name ([A-Za-z_][A-Za-z0-9_]{0,31}), unique in the script. Required.</summary>
        public string Role;
        /// <summary><see cref="KindChara"/> or <see cref="KindItem"/> (an unknown string from a file is kept; such a role cannot be cast). Required.</summary>
        public string Kind;
        /// <summary>A character role: <see cref="SexFemale"/>, <see cref="SexMale"/> or null (either).</summary>
        public string Sex;
        /// <summary>A prop role: its row "guid#no"; null = it did not resolve (cast by hand).</summary>
        public string Ref;
        /// <summary>A prop role, informational: checked against the row.</summary>
        public int? Group, Category;
        public ScriptRoleParent Parent;
        public Vec3 Pos = Vec3.Zero;
        /// <summary>Euler degrees.</summary>
        public Vec3 Rot = Vec3.Zero;
        public Vec3 Scale = Vec3.One;
        public ScriptHint Hint;
        public string Label;

        public ScriptRole() { }
        public ScriptRole(string role, string kind) { Role = role; Kind = kind; }

        public static ScriptRole Chara(string role, string sex = null, ScriptHint hint = null) => new ScriptRole(role, KindChara) { Sex = sex, Hint = hint };
        public static ScriptRole Item(string role, string itemRef, ScriptHint hint = null) => new ScriptRole(role, KindItem) { Ref = itemRef, Hint = hint };

        public bool IsChara => Kind == KindChara;
        public bool IsItem => Kind == KindItem;

        public ScriptRole Clone() => new ScriptRole
        {
            Role = Role, Kind = Kind, Sex = Sex, Ref = Ref, Group = Group, Category = Category, Parent = Parent?.Clone(), Pos = Pos, Rot = Rot, Scale = Scale,
            Hint = Hint?.Clone(), Label = Label, Unknown = Unknown?.Clone(),
        };

        /// <summary>Why the role breaks a rule as the result of an edit, or null: the name's pattern, a known kind, a known sex, a ref's form, the label's
        /// length, a parent naming a role.</summary>
        public string Problem()
        {
            if (!ScriptText.IsRoleName(Role)) return "the role name \"" + Role + "\" is not [A-Za-z_][A-Za-z0-9_]{0,31}";
            if (Array.IndexOf(kinds, Kind) < 0) return "role " + Role + ": unknown kind \"" + Kind + "\"";
            if (Sex != null && Array.IndexOf(sexes, Sex) < 0) return "role " + Role + ": unknown sex \"" + Sex + "\"";
            if (Ref != null && !JsonText.IsRef(Ref)) return "role " + Role + ": ref \"" + Ref + "\" is not \"guid#no\"";
            if (Parent != null && !ScriptText.IsRoleName(Parent.Role)) return "role " + Role + ": the parent names no role";
            if (Label != null && Label.Length > ScriptLimits.MaxLabel) return "role " + Role + ": label over " + ScriptLimits.MaxLabel + " characters";
            return null;
        }

        internal override void ReadMembers(DocReader r)
        {
            var v = r.ValueReq("role");
            Role = v.AsStr();
            if (!ScriptText.IsRoleName(Role)) throw v.Error("a role name ([A-Za-z_][A-Za-z0-9_]{0,31})");
            if (!r.Has("kind")) r.Str("kind");   // the missing-member error
            Kind = r.EnumStr("kind", null, kinds);
            Sex = r.EnumStrN("sex", null, sexes);
            Ref = r.RefN("ref");
            Group = r.IntN("group");
            Category = r.IntN("category");
            Parent = ReadNew<ScriptRoleParent>(r.ObjN("parent"));
            Pos = r.V3("pos", Pos);
            Rot = r.Euler("rot", Rot);
            Scale = r.V3("scale", Scale);
            Hint = ReadNew<ScriptHint>(r.ObjN("hint"));
            Label = r.StrN("label");
            if (Label != null && Label.Length > ScriptLimits.MaxLabel)
            {
                r.Report(FindingSeverity.Warning, ScriptFindings.Limit, "label", "label of " + Label.Length + " characters shortened to " + ScriptLimits.MaxLabel);
                Label = SceneScript.Cut(Label, ScriptLimits.MaxLabel);
            }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (!ScriptText.IsRoleName(Role)) throw new SaveFormatException(SaveErrorCode.BadDocument, "not a role name: \"" + Role + "\"", w.Entry, DocReader.Join(w.Path, "role"));
            ReqStr(w, "role", Role);
            ReqStr(w, "kind", Kind);
            bool chara = IsChara, item = IsItem;
            if (chara || Sex != null) w.Str("sex", Sex);
            if (item || Ref != null) WriteRefText(w, "ref", Ref);
            if (Group != null) w.IntN("group", Group);
            if (Category != null) w.IntN("category", Category);
            if (Parent != null) Parent.WriteTo(w, "parent");
            if (item || !Pos.Equals(Vec3.Zero)) w.V3("pos", Pos);
            if (item || !Rot.Equals(Vec3.Zero)) w.Euler("rot", Rot);
            if (item || !Scale.Equals(Vec3.One)) w.V3("scale", Scale);
            if (Hint != null) Hint.WriteTo(w, "hint");
            if (Label != null) w.Str("label", Label);
        }

        // A ref of a script (a prop role's row, the map) is a ref of the core document: its GUID joins `requires`, as every ref and animation source does.
        internal static void WriteRefText(CanonicalJsonWriter w, string name, string refText)
        {
            if (refText == null) { w.Null(name); return; }
            if (!JsonText.IsRef(refText)) throw new SaveFormatException(SaveErrorCode.BadDocument, "not a ref: \"" + refText + "\"", w.Entry, DocReader.Join(w.Path, name));
            w.Ref(name, refText);
        }
    }

    /// <summary>
    /// One scene script: the document of a <c>.hs2nscript</c> file (format, version, id, name, description, map, roles, setup, prepare, timeline) and,
    /// in a scene, its cast, the preparation's mark and the store number. Build a fresh object for an edit (<see cref="Clone"/>, then change it) and
    /// hand it to the script edit command; the scripts of the model's list are shared with the history and are never changed in place. A script of
    /// another format or of a newer version read from a scene keeps its whole object in <see cref="Raw"/> and is written back as read.
    /// </summary>
    public sealed class SceneScript : DocObject
    {
        public const string FormatName = "hs2next.script";
        public const int FormatVersion = 1;
        public const string DefaultName = "Script";
        /// <summary>The file extension of a script document.</summary>
        public const string FileExtension = ".hs2nscript";
        /// <summary>The entry name findings of a standalone document carry.</summary>
        public const string EntryName = "script";

        public string Format = FormatName;
        public int Version = FormatVersion;
        /// <summary>32 lowercase hex: the script's identity through renames, copies into scenes and exports. Required on write.</summary>
        public string Id;
        public string Name = DefaultName;
        public string Description = "";
        /// <summary>The map the script was made on; null = any map.</summary>
        public ScriptMap Map;
        public List<ScriptRole> Roles = new List<ScriptRole>();
        /// <summary>Sent at every play (requests and waits), in order.</summary>
        public List<JObject> Setup = new List<JObject>();
        /// <summary>Sent once per scene and cast (requests and waits), in order.</summary>
        public List<JObject> Prepare = new List<JObject>();
        /// <summary>A Timeline document with <see cref="TimelineDocument.RoleKeys"/> on: track keys and focus nodes may be "$role". Required on write.</summary>
        public TimelineDocument Timeline = TimelineDocument.NewScriptTimeline();
        /// <summary>A scene script: role → node id (32 hex); a role absent is uncast. Never null.</summary>
        public SortedDictionary<string, string> Cast = new SortedDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>A scene script: the preparation's mark (16 hex, <see cref="ScriptText.PreparationMark"/>); null = never prepared.</summary>
        public string Prepared;
        /// <summary>A scene script: the store number it came from or was kept as (informational).</summary>
        public int? Store;
        /// <summary>The whole object of a scene's script of another format or of a newer version (written back as read); null otherwise.</summary>
        public JObject Raw;

        /// <summary>True for a script kept as read (another format or a newer version): never played, never edited.</summary>
        public bool IsOpaque => Raw != null;

        /// <summary>The role named <paramref name="role"/>, or null.</summary>
        public ScriptRole FindRole(string role)
        {
            if (role == null || Roles == null) return null;
            foreach (var r in Roles) if (r != null && r.Role == role) return r;
            return null;
        }

        /// <summary>The cast node of <paramref name="role"/>, or null.</summary>
        public string CastOf(string role) => role != null && Cast != null && Cast.TryGetValue(role, out var n) ? n : null;

        // ---------- copies and conversions ----------

        /// <summary>A deep copy (a canonical write → read round trip of the scene form: unknown members kept, nothing shared).</summary>
        public SceneScript Clone()
        {
            var b = CanonicalJsonWriter.ToDocumentBytes(SceneScriptList.MemberName, w => WriteAs(w, true));
            return ReadAt(DocReader.ForDocument(b, new DocReadContext(SceneScriptList.MemberName, false, new FindingList())), true, 0, null);
        }

        /// <summary>A copy as a store document: the scene members (cast, prepared, store) cleared.</summary>
        public SceneScript ToDocument()
        {
            var c = Clone();
            c.Cast.Clear();
            c.Prepared = null;
            c.Store = null;
            return c;
        }

        /// <summary>A copy as a scene script: no cast, never prepared, <paramref name="store"/> as its store number; unknown members named like the scene
        /// members are dropped.</summary>
        public SceneScript ToScene(int? store = null)
        {
            var c = Clone();
            c.Cast.Clear();
            c.Prepared = null;
            c.Store = store;
            if (c.Unknown != null)
            {
                c.Unknown.Remove("cast");
                c.Unknown.Remove("prepared");
                c.Unknown.Remove("store");
                if (c.Unknown.IsEmpty) c.Unknown = null;
            }
            return c;
        }

        /// <summary>The canonical bytes of the scene form of the script (UTF-8, trailing LF), as scene.json holds it.</summary>
        public byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(SceneScriptList.MemberName, w => WriteAs(w, true));

        /// <summary>The canonical text of the scene form (no trailing LF).</summary>
        public string ToJson()
        {
            var b = ToBytes();
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }

        /// <summary>The bytes of a <c>.hs2nscript</c> file: the document form (no cast, prepared, store), canonical, UTF-8, trailing LF. An opaque script
        /// cannot be written as a document (BadDocument).</summary>
        public byte[] ToDocumentBytes()
        {
            if (IsOpaque) throw new SaveFormatException(SaveErrorCode.BadDocument, "a script of another format or a newer version is kept as read and is not written as a document", EntryName, "");
            return CanonicalJsonWriter.ToDocumentBytes(EntryName, w => WriteAs(w, false));
        }

        /// <summary>
        /// Reads a standalone document (a <c>.hs2nscript</c> file's bytes) with the reader's rules and repairs; findings go to <paramref name="findings"/>.
        /// A syntax error, a type error or a missing required member throws <see cref="SaveFormatException"/>; a document of another format or of a newer
        /// version throws <see cref="ScriptRefusedException"/>. A document without an id gets one derived from its bytes (finding script.id). The
        /// scene members (cast, prepared, store) are not part of a document: a file holding them keeps them as unknown members.
        /// </summary>
        public static SceneScript ParseDocument(byte[] utf8, FindingList findings = null, string entry = EntryName)
        {
            var o = DocReader.Parse(utf8 ?? Array.Empty<byte>(), entry ?? EntryName);
            string seed = "script/" + JsonText.Hex(Sha(utf8));
            return ReadAt(new DocReader(o, new DocReadContext(entry ?? EntryName, false, findings ?? new FindingList())), false, 0, seed);
        }

        /// <summary>As <see cref="ParseDocument(byte[], FindingList, string)"/> for a parsed object (the stored form of every track and event). The token is
        /// not changed. <paramref name="idSeed"/> derives the id of a document without one (null: from the object's canonical text).</summary>
        public static SceneScript FromDocumentToken(JObject token, FindingList findings = null, string entry = EntryName, string idSeed = null)
        {
            if (token == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "a script is an object, found nothing", entry ?? EntryName, "");
            var copy = (JObject)token.DeepClone();
            string seed = idSeed ?? "script/" + JsonText.Hex(Sha(Encoding.UTF8.GetBytes(CanonicalJsonWriter.ToText(copy))));
            return ReadAt(new DocReader(copy, new DocReadContext(entry ?? EntryName, false, findings ?? new FindingList())), false, 0, seed);
        }

        /// <summary>Reads one scene script from JSON text (the scene form, as an element of <c>scripts</c>); a script of another format or a newer version
        /// is kept opaque (warning). Throws as the scene reader does.</summary>
        public static SceneScript ParseScene(string json, FindingList findings = null, string entry = SceneScriptList.MemberName)
        {
            var t = DocReader.ParseText(json ?? "", entry);
            if (!(t is JObject o)) throw new SaveFormatException(SaveErrorCode.BadDocument, "a script is an object, found " + DocReader.Describe(t), entry, "");
            return ReadAt(new DocReader(o, new DocReadContext(entry, false, findings ?? new FindingList())), true, 0, "scripts/0");
        }

        static byte[] Sha(byte[] b)
        {
            using (var s = SHA256.Create()) return s.ComputeHash(b ?? Array.Empty<byte>());
        }

        // ---------- rules of an edit ----------

        /// <summary>
        /// The first rule this script breaks as the result of an edit, or null: format and version; a valid id; a name of at most 128 characters and a
        /// description of at most 2,048; at most 32 roles, each valid, names distinct, a parent naming a character role of the script; at most 256 steps,
        /// each one that may be sent, a wait in (0, 10] s; a Timeline with role keys within its own rules, no timed command that may not fire; cast entries
        /// naming roles by a valid name and nodes by id; a preparation mark of 16 hex. What <paramref name="before"/> (the same script before the edit)
        /// already held is not the edit's doing: a file's roles or steps over a limit may shrink, a step or event it was refused with may stay.
        /// An opaque script has no rule (it is never edited).
        /// </summary>
        public string Problem(SceneScript before = null)
        {
            if (IsOpaque) return null;
            if (Format != FormatName) return "format \"" + Format + "\" is not " + FormatName;
            if (Version != FormatVersion) return "version " + Version + " is not " + FormatVersion;
            if (!JsonText.IsId(Id)) return "no valid id";
            if (Name == null) return "name is null";
            if (Name.Length > ScriptLimits.MaxName) return ScriptFindings.Limit + ": a name holds at most " + ScriptLimits.MaxName + " characters";
            if (Description == null) return "description is null";
            if (Description.Length > ScriptLimits.MaxDescription) return ScriptFindings.Limit + ": a description holds at most " + ScriptLimits.MaxDescription + " characters";
            if (Roles == null) return "roles is null";
            if (Roles.Count > ScriptLimits.MaxRoles && Roles.Count > (before?.Roles?.Count ?? 0)) return ScriptFindings.Limit + ": a script holds at most " + ScriptLimits.MaxRoles + " roles";
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Roles.Count; i++)
            {
                var r = Roles[i];
                if (r == null) return "roles[" + i + "] is null";
                string p = r.Problem();
                if (p != null) return p;
                if (!names.Add(r.Role)) return "the role name " + r.Role + " is used twice";
            }
            foreach (var r in Roles)
            {
                if (r.Parent == null) continue;
                var pr = FindRole(r.Parent.Role);
                if (pr == null) return "role " + r.Role + ": the parent role " + r.Parent.Role + " is not a role of the script";
                if (!pr.IsChara) return "role " + r.Role + ": the parent role " + r.Parent.Role + " is not a character role";
            }
            if (Setup == null) return "setup is null";
            if (Prepare == null) return "prepare is null";
            int steps = Setup.Count + Prepare.Count;
            int stepsBefore = (before?.Setup?.Count ?? 0) + (before?.Prepare?.Count ?? 0);
            if (steps > ScriptLimits.MaxSteps && steps > stepsBefore) return ScriptFindings.Limit + ": set-up and preparation hold at most " + ScriptLimits.MaxSteps + " steps together";
            string sp = StepsProblem(Setup, before?.Setup, "setup") ?? StepsProblem(Prepare, before?.Prepare, "prepare");
            if (sp != null) return sp;
            if (Timeline == null) return "timeline is null";
            if (!Timeline.RoleKeys) return "the timeline of a script takes role keys (RoleKeys)";
            string tp = Timeline.Problem(before?.Timeline);
            if (tp != null) return "timeline: " + tp;
            var oldRefusals = new HashSet<string>(StringComparer.Ordinal);
            if (before?.Timeline?.Events != null)
                foreach (var e in before.Timeline.Events)
                    if (e?.Do != null && ScriptSteps.EventRefusal(e.Do) != null) oldRefusals.Add(CanonicalJsonWriter.ToText(e.Do));
            for (int i = 0; i < Timeline.Events.Count; i++)
            {
                string why = ScriptSteps.EventRefusal(Timeline.Events[i].Do);
                if (why != null && !oldRefusals.Contains(CanonicalJsonWriter.ToText(Timeline.Events[i].Do))) return "timeline.events[" + i + "]: " + why;
            }
            if (Cast == null) return "cast is null";
            foreach (var kv in Cast)
            {
                if (!ScriptText.IsRoleName(kv.Key)) return "cast: \"" + kv.Key + "\" is not a role name";
                if (kv.Value != null && !JsonText.IsId(kv.Value)) return "cast " + kv.Key + ": \"" + kv.Value + "\" is not a node id";
                if (kv.Value == null) return "cast " + kv.Key + ": the node is null (leave the role out to uncast it)";
            }
            if (Prepared != null && !ScriptText.IsHash16(Prepared)) return "prepared \"" + Prepared + "\" is not 16 hex digits";
            return null;
        }

        static string StepsProblem(List<JObject> steps, List<JObject> before, string name)
        {
            var old = new HashSet<string>(StringComparer.Ordinal);
            if (before != null) foreach (var s in before) if (s != null) old.Add(CanonicalJsonWriter.ToText(s));
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (s == null) return name + "[" + i + "] is null";
                string why = ScriptSteps.Refusal(s) ?? ScriptSteps.WaitProblem(s);
                if (why != null && !old.Contains(CanonicalJsonWriter.ToText(s))) return name + "[" + i + "]: " + why;
            }
            return null;
        }

        /// <summary>Every "$x" of the script that names no role: (path, role name) pairs in document order (steps, timed commands, track keys, focus nodes,
        /// prop parents, cast entries). A step, event or track holding one is skipped at play.</summary>
        public List<KeyValuePair<string, string>> UnknownRoleRefs()
        {
            var l = new List<KeyValuePair<string, string>>();
            if (IsOpaque) return l;
            var declared = new HashSet<string>(StringComparer.Ordinal);
            if (Roles != null) foreach (var r in Roles) if (r?.Role != null) declared.Add(r.Role);
            void Visit(JToken t, string path) =>
                ScriptText.VisitStrings(t, path, (s, p) =>
                {
                    string role = ScriptText.RoleOf(s);
                    if (role != null && !declared.Contains(role)) l.Add(new KeyValuePair<string, string>(p, role));
                });
            if (Roles != null)
                for (int i = 0; i < Roles.Count; i++)
                    if (Roles[i]?.Parent?.Role != null && !declared.Contains(Roles[i].Parent.Role))
                        l.Add(new KeyValuePair<string, string>("roles[" + i + "].parent.role", Roles[i].Parent.Role));
            if (Setup != null) for (int i = 0; i < Setup.Count; i++) Visit(Setup[i], "setup[" + i + "]");
            if (Prepare != null) for (int i = 0; i < Prepare.Count; i++) Visit(Prepare[i], "prepare[" + i + "]");
            if (Timeline != null)
            {
                for (int i = 0; i < (Timeline.Tracks?.Count ?? 0); i++)
                {
                    var t = Timeline.Tracks[i];
                    if (t == null) continue;
                    string role = ScriptText.RoleOf(t.Key);
                    if (role != null && !declared.Contains(role)) l.Add(new KeyValuePair<string, string>("timeline.tracks[" + i + "].key", role));
                    if (t.Keys == null) continue;
                    for (int k = 0; k < t.Keys.Count; k++)
                    {
                        string fr = ScriptText.RoleOf(t.Keys[k]?.Dof?.Focus?.Node);
                        if (fr != null && !declared.Contains(fr)) l.Add(new KeyValuePair<string, string>("timeline.tracks[" + i + "].keys[" + k + "].v.focus.node", fr));
                    }
                }
                for (int i = 0; i < (Timeline.Events?.Count ?? 0); i++) Visit(Timeline.Events[i]?.Do, "timeline.events[" + i + "].do");
            }
            if (Cast != null) foreach (var kv in Cast) if (!declared.Contains(kv.Key)) l.Add(new KeyValuePair<string, string>(DocReader.Join("cast", kv.Key), kv.Key));
            return l;
        }

        /// <summary>The track keys and focus nodes of the script's Timeline that are node ids (32 hex) rather than roles: (path, node id) pairs.</summary>
        public List<KeyValuePair<string, string>> NodeKeys()
        {
            var l = new List<KeyValuePair<string, string>>();
            if (IsOpaque || Timeline?.Tracks == null) return l;
            for (int i = 0; i < Timeline.Tracks.Count; i++)
            {
                var t = Timeline.Tracks[i];
                if (t == null) continue;
                if (JsonText.IsId(t.Key)) l.Add(new KeyValuePair<string, string>("timeline.tracks[" + i + "].key", t.Key));
                if (t.Keys == null) continue;
                for (int k = 0; k < t.Keys.Count; k++)
                {
                    string n = t.Keys[k]?.Dof?.Focus?.Node;
                    if (JsonText.IsId(n)) l.Add(new KeyValuePair<string, string>("timeline.tracks[" + i + "].keys[" + k + "].v.focus.node", n));
                }
            }
            return l;
        }

        // ---------- reading ----------

        bool readScene;
        int readIndex;
        string readSeed;

        /// <summary>Reads one script: <paramref name="scene"/> = an element of a scene's <c>scripts</c> (the scene members known; a script of another format
        /// or a newer version kept opaque), else a standalone document (refused). <paramref name="idSeed"/> derives the id of a script without one.</summary>
        internal static SceneScript ReadAt(DocReader r, bool scene, int index, string idSeed)
        {
            var s = new SceneScript { readScene = scene, readIndex = index, readSeed = idSeed ?? "scripts/" + index.ToString(CultureInfo.InvariantCulture) };
            s.ReadFrom(r);
            return s;
        }

        internal override void ReadMembers(DocReader r)
        {
            var fv = r.ValueReq("format");
            string format = fv.AsStr();
            int version = r.Int("version");
            string refused = null, code = null;
            if (format != FormatName)
            {
                code = ScriptFindings.BadFormat;
                refused = "format \"" + format + "\" is not " + FormatName;
            }
            else if (version > FormatVersion)
            {
                code = ScriptFindings.NewerVersion;
                refused = "version " + version + " of " + FormatName + " is newer than " + FormatVersion;
            }
            else if (version < 1)
            {
                code = ScriptFindings.BadFormat;
                refused = "version " + version + " is not a version of " + FormatName;
            }
            if (refused != null)
            {
                if (!readScene) throw new ScriptRefusedException(code, refused, code == ScriptFindings.NewerVersion ? version : (int?)null);
                Format = format;
                Version = version;
                Raw = r.Detach();
                Id = Raw["id"] is JValue iv && iv.Type == JTokenType.String && JsonText.IsId((string)iv) ? (string)iv : null;
                Name = Raw["name"] is JValue nv && nv.Type == JTokenType.String ? (string)nv : DefaultName;
                r.Report(FindingSeverity.Warning, code, null, "script " + readIndex + ": " + refused + "; kept as read, never played");
                return;
            }
            Format = format;
            Version = version;
            if (r.Has("id")) Id = r.Id("id");
            else
            {
                Id = JsonText.DerivedId(readSeed);
                r.Report(FindingSeverity.Info, ScriptFindings.Id, null, "the script has no id; id " + Id + " given");
            }
            Name = r.Str("name", DefaultName);
            if (Name.Length > ScriptLimits.MaxName)
            {
                r.Report(FindingSeverity.Warning, ScriptFindings.Limit, "name", "name of " + Name.Length + " characters shortened to " + ScriptLimits.MaxName);
                Name = Cut(Name, ScriptLimits.MaxName);
            }
            Description = r.Str("description", "");
            if (Description.Length > ScriptLimits.MaxDescription)
            {
                r.Report(FindingSeverity.Warning, ScriptFindings.Limit, "description", "description of " + Description.Length + " characters shortened to " + ScriptLimits.MaxDescription);
                Description = Cut(Description, ScriptLimits.MaxDescription);
            }
            Map = ReadNew<ScriptMap>(r.ObjN("map"));

            // Roles: a repeated name drops the later role; the first MaxRoles are kept.
            var roleReaders = r.ObjArray("roles");
            Roles = new List<ScriptRole>(Math.Min(roleReaders.Count, ScriptLimits.MaxRoles));
            var names = new HashSet<string>(StringComparer.Ordinal);
            int dropped = 0;
            for (int i = 0; i < roleReaders.Count; i++)
            {
                var role = ReadNew<ScriptRole>(roleReaders[i]);
                if (!names.Add(role.Role))
                {
                    roleReaders[i].Report(FindingSeverity.Warning, ScriptFindings.DuplicateRole, "role", "the role name " + role.Role + " is used by an earlier role; this role is dropped");
                    continue;
                }
                if (Roles.Count >= ScriptLimits.MaxRoles) { dropped++; continue; }
                Roles.Add(role);
            }
            if (dropped > 0) r.Report(FindingSeverity.Warning, ScriptFindings.Limit, "roles", dropped + " roles over the limit of " + ScriptLimits.MaxRoles + " dropped");

            Setup = ReadSteps(r, "setup");
            Prepare = ReadSteps(r, "prepare");
            int over = Setup.Count + Prepare.Count - ScriptLimits.MaxSteps;
            if (over > 0)
            {
                int fromPrepare = Math.Min(over, Prepare.Count);
                Prepare.RemoveRange(Prepare.Count - fromPrepare, fromPrepare);
                int fromSetup = over - fromPrepare;
                if (fromSetup > 0) Setup.RemoveRange(Setup.Count - fromSetup, fromSetup);
                r.Report(FindingSeverity.Warning, ScriptFindings.Limit, "prepare", over + " steps over the limit of " + ScriptLimits.MaxSteps + " set-up and preparation steps dropped from the end");
            }

            Timeline = TimelineDocument.ReadScript(r.ObjReq(TimelineDocument.MemberName));

            if (readScene)
            {
                Cast = r.Map("cast", v => v.AsId());
                var pv = r.Value("prepared");
                if (pv == null || pv.IsNull) Prepared = null;
                else
                {
                    Prepared = pv.AsStr();
                    if (!ScriptText.IsHash16(Prepared)) throw pv.Error("a preparation mark (16 hex digits)");
                }
                Store = r.IntN("store");
            }

            ReportSteps(r, Setup, "setup");
            ReportSteps(r, Prepare, "prepare");
            for (int i = 0; i < Timeline.Events.Count; i++)
            {
                var e = Timeline.Events[i];
                string c = ScriptSteps.CommandOf(e.Do);
                if (ScriptSteps.IsScriptCommand(c))
                    r.Context.Findings.Warning(ScriptFindings.BadStep, r.Context.Where(At(r, "timeline.events[" + i + "].do")),
                        "the timed command at t = " + TimelineDocument.F(e.T) + " s is kept and never runs: the command " + c + " plays scripts");
            }
            foreach (var u in UnknownRoleRefs())
                r.Context.Findings.Warning(ScriptFindings.UnknownRole, r.Context.Where(At(r, u.Key)),
                    "$" + u.Value + " names no role of the script; kept, skipped at play");
            if (!readScene)
                foreach (var n in NodeKeys())
                    r.Context.Findings.Info(ScriptFindings.NodeKey, r.Context.Where(At(r, n.Key)),
                        "node id " + n.Value + " where a role belongs: meaningful only in the scene it came from");
        }

        static List<JObject> ReadSteps(DocReader r, string name) =>
            r.Array(name, v =>
            {
                if (v.Token.Type != JTokenType.Object) throw v.Error("a step object ({cmd, …} or {wait})");
                var o = (JObject)v.Detach();
                if (ScriptSteps.IsWait(o))
                {
                    var w = o["wait"];
                    if (w.Type != JTokenType.Integer && w.Type != JTokenType.Float)
                        throw new DocTypeException(v.Context, w, DocReader.Join(v.Path, "wait"), "expected a number of seconds, found " + DocReader.Describe(w));
                }
                return o;
            });

        // Clamps waits out of range and reports the steps that may not be sent (kept, never sent).
        static void ReportSteps(DocReader r, List<JObject> steps, string name)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                string at = At(r, name + "[" + i + "]");
                if (ScriptSteps.IsWait(s))
                {
                    double w = (double)s["wait"];
                    if (!(w > 0) || w > ScriptLimits.MaxWait)
                    {
                        float c = w > ScriptLimits.MaxWait ? ScriptLimits.MaxWait : ScriptLimits.MinWait;
                        s["wait"] = c;
                        r.Context.Findings.Warning(ScriptFindings.Limit, r.Context.Where(at + ".wait"),
                            "wait " + w.ToString("R", CultureInfo.InvariantCulture) + " s outside (0, " + ScriptSteps.Fs(ScriptLimits.MaxWait) + "] s; " + ScriptSteps.Fs(c) + " s used");
                    }
                    continue;
                }
                string why = ScriptSteps.Refusal(s);
                if (why != null) r.Context.Findings.Warning(ScriptFindings.BadStep, r.Context.Where(at), name + " step " + i + " is kept and never sent: " + why);
            }
        }

        // A path below the reader's object ("setup[2].key"): DocReader.Join quotes names that hold '[' or '.'.
        static string At(DocReader r, string rel) => string.IsNullOrEmpty(r.Path) ? rel : r.Path + "." + rel;

        internal static string Cut(string s, int max)
        {
            int cut = max;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut);
        }

        // ---------- writing ----------

        bool writeScene = true;

        /// <summary>Writes the script as one object: the scene form (with cast, prepared, store) or the document form.</summary>
        internal void WriteAs(CanonicalJsonWriter w, bool scene)
        {
            bool old = writeScene;
            writeScene = scene;
            try { WriteTo(w); }
            finally { writeScene = old; }
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            if (Raw != null)
            {
                // The raw members in read order; the id a repair changed is written in its place.
                foreach (var p in Raw.Properties())
                {
                    if (p.Name == "id" && Id != null) w.Id("id", Id);
                    else w.Token(p.Name, p.Value);
                }
                return;
            }
            ReqStr(w, "format", Format);
            w.Int("version", Version);
            ReqId(w, "id", Id);
            ReqStr(w, "name", Name);
            ReqStr(w, "description", Description);
            WriteN(w, "map", Map);
            WriteList(w, "roles", Roles);
            WriteSteps(w, "setup", Setup);
            WriteSteps(w, "prepare", Prepare);
            if (Timeline == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "required object is null", w.Entry, DocReader.Join(w.Path, "timeline"));
            if (!Timeline.RoleKeys) throw new SaveFormatException(SaveErrorCode.BadDocument, "the timeline of a script takes role keys", w.Entry, DocReader.Join(w.Path, "timeline"));
            Timeline.WriteTo(w, TimelineDocument.MemberName);
            if (!writeScene) return;
            w.Name("cast");
            if (Cast == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "cast is null", w.Entry, w.Path);
            w.Map(Cast, (cw, id) =>
            {
                if (id == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "a cast node is null", cw.Entry, cw.Path);
                cw.Id(id);
            });
            w.Name("prepared");
            if (Prepared == null) w.Null();
            else
            {
                if (!ScriptText.IsHash16(Prepared)) throw new SaveFormatException(SaveErrorCode.BadDocument, "not a preparation mark: \"" + Prepared + "\"", w.Entry, w.Path);
                w.Str(Prepared);
            }
            w.IntN("store", Store);
        }

        static void WriteSteps(CanonicalJsonWriter w, string name, List<JObject> steps)
        {
            w.Name(name);
            string at = w.Path;
            if (steps == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "array is null", w.Entry, at);
            w.StartArray();
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] == null) throw NullElement(w, at, i);
                w.Token(steps[i]);
            }
            w.EndArray();
        }
    }

    /// <summary>
    /// The scripts of a scene (the scene.json member <c>scripts</c>), in display order. The model holds null rather than an empty list. A list in the
    /// model is not changed in place: an edit puts a new list there.
    /// </summary>
    public sealed class SceneScriptList
    {
        /// <summary>The member name in scene.json.</summary>
        public const string MemberName = "scripts";

        /// <summary>The scripts, in display order (no null element on write).</summary>
        public List<SceneScript> Scripts = new List<SceneScript>();

        public SceneScriptList() { }
        public SceneScriptList(IEnumerable<SceneScript> scripts) { Scripts.AddRange(scripts); }

        public int Count => Scripts?.Count ?? 0;

        /// <summary>The index of the script with this id, or −1.</summary>
        public int IndexOf(string id)
        {
            if (id == null || Scripts == null) return -1;
            for (int i = 0; i < Scripts.Count; i++) if (Scripts[i] != null && Scripts[i].Id == id) return i;
            return -1;
        }

        /// <summary>The script with this id, or null.</summary>
        public SceneScript Find(string id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : Scripts[i];
        }

        /// <summary>The index of the only script named <paramref name="name"/>; −1 when none or several have it.</summary>
        public int IndexOfName(string name)
        {
            if (name == null || Scripts == null) return -1;
            int found = -1;
            for (int i = 0; i < Scripts.Count; i++)
            {
                if (Scripts[i] == null || Scripts[i].Name != name) continue;
                if (found >= 0) return -1;
                found = i;
            }
            return found;
        }

        /// <summary>A new list with <paramref name="script"/> in place of the script at <paramref name="index"/>; the other scripts are shared.</summary>
        public SceneScriptList With(int index, SceneScript script)
        {
            var l = new SceneScriptList(Scripts);
            l.Scripts[index] = script;
            return l;
        }

        /// <summary>A new list with <paramref name="script"/> appended; the other scripts are shared.</summary>
        public SceneScriptList Adding(SceneScript script)
        {
            var l = new SceneScriptList(Scripts);
            l.Scripts.Add(script);
            return l;
        }

        /// <summary>A new list without the script at <paramref name="index"/>; the other scripts are shared.</summary>
        public SceneScriptList Without(int index)
        {
            var l = new SceneScriptList(Scripts);
            l.Scripts.RemoveAt(index);
            return l;
        }

        /// <summary>A deep copy: nothing is shared with this list, unknown members kept.</summary>
        public SceneScriptList Clone()
        {
            var l = new SceneScriptList();
            foreach (var s in Scripts) l.Scripts.Add(s?.Clone());
            return l;
        }

        /// <summary>
        /// The first rule this list breaks as the result of an edit, or null: at most <see cref="ScriptLimits.MaxScripts"/> scripts, ids valid and
        /// distinct, and every script's own rules (<see cref="SceneScript.Problem"/>). What <paramref name="before"/> already held is not the edit's
        /// doing: a file's list over the limit may shrink, and a script keeps a problem it came with while other scripts are edited.
        /// </summary>
        public string Problem(SceneScriptList before = null)
        {
            if (Scripts == null) return "scripts is null";
            if (Scripts.Count > ScriptLimits.MaxScripts && Scripts.Count > (before?.Count ?? 0)) return ScriptFindings.Limit + ": a scene holds at most " + ScriptLimits.MaxScripts + " scripts";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Scripts.Count; i++)
            {
                var s = Scripts[i];
                if (s == null) return "script " + i + " is null";
                // A script kept as read (another format, a newer version) is never edited: one that was there (the same object or a copy of it) stays, whatever its id.
                if (s.IsOpaque && before?.Scripts != null && before.Scripts.Exists(b => b != null && b.IsOpaque && (ReferenceEquals(b, s) || b.ToJson() == s.ToJson()))) continue;
                if (!JsonText.IsId(s.Id)) return "script " + i + " has no valid id";
                if (!ids.Add(s.Id)) return "the script id " + s.Id + " is used twice";
                var old = before?.Find(s.Id);
                if (ReferenceEquals(old, s)) continue;
                string p = s.Problem(old);
                if (p != null && p != old?.Problem()) return "script " + (s.Name ?? s.Id) + ": " + p;
            }
            return null;
        }

        // ---------- reading ----------

        /// <summary>Reads the member of the scene document's root; null when absent or empty, so the model never holds an empty list.</summary>
        internal static SceneScriptList Read(DocReader root)
        {
            var items = root.ObjArray(MemberName);
            if (items.Count == 0) return null;
            var l = new SceneScriptList();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                if (l.Count >= ScriptLimits.MaxScripts)
                {
                    root.Report(FindingSeverity.Warning, ScriptFindings.Limit, MemberName, (items.Count - i) + " scripts over the limit of " + ScriptLimits.MaxScripts + " dropped");
                    break;
                }
                var s = SceneScript.ReadAt(items[i], true, i, MemberName + "/" + i.ToString(CultureInfo.InvariantCulture));
                if (s.Id != null && !ids.Add(s.Id))
                {
                    string old = s.Id;
                    s.Id = JsonText.DerivedId(old + "/dup/" + i.ToString(CultureInfo.InvariantCulture));
                    for (int n = 1; !ids.Add(s.Id); n++) s.Id = JsonText.DerivedId(old + "/dup/" + i.ToString(CultureInfo.InvariantCulture) + "/" + n.ToString(CultureInfo.InvariantCulture));
                    items[i].Report(FindingSeverity.Info, ScriptFindings.DuplicateId, "id", "script id " + old + " repeats an earlier script's; derived id " + s.Id + " used");
                }
                l.Scripts.Add(s);
            }
            return l;
        }

        /// <summary>Reads a list from JSON text (one array, the scene form) with the reader's rules and repairs. Null for an empty array.</summary>
        public static SceneScriptList Parse(string json, FindingList findings = null, string entry = MemberName) =>
            FromToken(DocReader.ParseText(json ?? "", entry), findings, entry);

        /// <summary>As <see cref="Parse"/> for a parsed token (an array; anything else throws BadDocument). The token is not changed.</summary>
        public static SceneScriptList FromToken(JToken token, FindingList findings = null, string entry = MemberName, bool lenient = false)
        {
            if (!(token is JArray a))
                throw new SaveFormatException(SaveErrorCode.BadDocument, "the scripts are an array, found " + (token == null ? "nothing" : DocReader.Describe(token)), entry, token?.Path ?? "");
            return Read(new DocReader(new JObject(new JProperty(MemberName, a.DeepClone())), new DocReadContext(entry, lenient, findings ?? new FindingList())));
        }

        /// <summary>Reports each cast entry that names a node <paramref name="exists"/> does not know (info script.castMissing; kept, the role is uncast at play).</summary>
        public void ReportUnknownNodes(Func<string, bool> exists, DocReadContext ctx, string basePath = MemberName)
        {
            if (exists == null || ctx == null || Scripts == null) return;
            for (int i = 0; i < Scripts.Count; i++)
            {
                var s = Scripts[i];
                if (s == null || s.IsOpaque || s.Cast == null) continue;
                foreach (var kv in s.Cast)
                {
                    if (kv.Value == null || exists(kv.Value)) continue;
                    ctx.Findings.Info(ScriptFindings.CastMissing, ctx.Where(DocReader.Join(basePath + "[" + i + "].cast", kv.Key)),
                        "role " + kv.Key + " of script " + i + " is cast to node " + kv.Value + ", which is not in the scene; kept, the role is uncast at play");
                }
            }
        }

        // ---------- writing ----------

        /// <summary>Writes the array as a member (the caller decides whether the scene has one).</summary>
        internal void WriteTo(CanonicalJsonWriter w, string name)
        {
            w.Name(name);
            string at = w.Path;
            if (Scripts == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "array is null", w.Entry, at);
            w.StartArray();
            for (int i = 0; i < Scripts.Count; i++)
            {
                if (Scripts[i] == null) throw DocObject.NullElement(w, at, i);
                Scripts[i].WriteAs(w, true);
            }
            w.EndArray();
        }

        /// <summary>The canonical JSON text of the array (no trailing LF).</summary>
        public string ToJson()
        {
            var b = CanonicalJsonWriter.ToDocumentBytes(MemberName, w =>
            {
                string at = w.Path;
                w.StartArray();
                for (int i = 0; i < Count; i++)
                {
                    if (Scripts[i] == null) throw DocObject.NullElement(w, at, i);
                    Scripts[i].WriteAs(w, true);
                }
                w.EndArray();
            });
            return Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }
    }
}
