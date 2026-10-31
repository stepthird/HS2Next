// Shared rules of every Core bridge command: registration with the app the handler works
// on, node keys (exact id or unique prefix of ≥ 8 characters) and key / keys arrays, kind and build checks, the
// strict optional arguments (a number is never read as a string) and name-or-number arguments, content references
// (maps, items, animations), the bus options of a bridge mutation (Source = Bridge, `undo: false` = no
// history, one transaction per command that joins a batch's), bus results as bridge errors, HS2's four
// number forms and the HS2-shaped objects several commands share (Describe, CameraState, FaceState).
// Handlers read the app through their CoreContext, never through statics. The class is partial: package W2
// adds FaceStateCore (the body of FaceState) and its own helpers in its files.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Commands
{
    /// <summary>What a Core handler sees: the bridge context and the app it was registered with.</summary>
    public sealed class CoreContext
    {
        public CoreContext(AppCore app, ICommandContext ctx)
        {
            App = app;
            Ctx = ctx;
        }

        public AppCore App { get; }
        public ICommandContext Ctx { get; }
        public ICommandArgs Args => Ctx.Args;
        public SceneSession Session => App.Session;
        public SceneRuntime Runtime => App.Runtime;
        public RuntimeServices Services => App.Services;
        public StudioDb Db => App.Services?.Db;
        public AppPaths Paths => App.Paths;
        /// <summary>False for <c>undo: false</c>.</summary>
        public bool Undo => Ctx.Undo;
        public IBridgeFrames Frames => Ctx.Frames;
        public IOutputRoot Output => Ctx.Output;
        public void Warn(string message) => Ctx.Warn(message);
        /// <summary>Set by commands that clear the history by design (scene.new, scene.load): no "history cleared" warning.</summary>
        public bool ClearsHistory { get; set; }
    }

    public static partial class CommandKit
    {
        /// <summary>CommandSpec.Since of every Core command of the first public version (1.0.0, the Api freeze); a command
        /// added later states its own version with WithSince.</summary>
        public const string Since = "1.0.0";
        /// <summary>BridgeCodes.Unavailable (package B): the target exists but is not realized.</summary>
        public const string Unavailable = "unavailable";
        public const string HistoryClearedWarning = "history cleared (undo: false on a structural command)";

        // ------------------------------------------------------------ registration

        /// <summary>
        /// A spec with owner core, Since 1.0.0 and its <c>help</c> group <paramref name="group"/>: the name of the command
        /// group that registers it (CoreCommands.Groups, e.g. "chara edit", "content"), so that <c>help {group}</c> lists the
        /// group's commands. Every Core command declares its group this way.
        /// </summary>
        public static CommandSpec Spec(string name, string summary, string group) =>
            new CommandSpec(name, summary) { Group = group }.WithSince(Since).WithOwner(BridgeOwners.Core);

        /// <summary>A spec with owner core and Since 1.0.0 whose group is the part of the name before the first dot: for
        /// commands registered outside CoreCommands.Register (tools); Core commands use the overload with the group.</summary>
        public static CommandSpec Spec(string name, string summary) => new CommandSpec(name, summary).WithSince(Since).WithOwner(BridgeOwners.Core);

        /// <summary>The group CoreCommands.Register is registering now (null outside it). A spec registered meanwhile whose
        /// group differs is listed in CoreCommands.RegistrationErrors (it still registers).</summary>
        internal static string RegisteringGroup;

        /// <summary>
        /// Registers a Core command whose handler gets a <see cref="CoreContext"/> of <paramref name="app"/> (owner core,
        /// Since 1.0.0 unless set). With <c>undo: false</c> a reply gains <see cref="HistoryClearedWarning"/> when the
        /// command emptied a non-empty undo history.
        /// </summary>
        public static void Register(ICommandRegistry r, AppCore app, CommandSpec spec, Func<CoreContext, object> handler)
        {
            if (spec.Since == null) spec.Since = Since;
            spec.Owner = BridgeOwners.Core;
            if (RegisteringGroup != null && spec.Group != RegisteringGroup)
            {
                string line = RegisteringGroup + ": " + spec.Name + " declares the group \"" + spec.Group + "\"";
                CoreCommands.RegistrationErrors.Add(line);
                Debug.LogWarning("[app] command group mismatch: " + line);
            }
            r.Register(spec, ctx => Run(app, ctx, handler));
        }

        static object Run(AppCore app, ICommandContext ctx, Func<CoreContext, object> handler)
        {
            var c = new CoreContext(app, ctx);
            var bus = app.Session?.Commands;
            bool couldUndo = !ctx.Undo && bus != null && bus.CanUndo;
            var result = handler(c);
            if (couldUndo && !c.ClearsHistory && !bus.CanUndo && bus.ActiveTransaction == null) ctx.Warn(HistoryClearedWarning);
            return result;
        }

        // ------------------------------------------------------------ errors

        public static BridgeException BadArgs(string arg, string expected, string message = null) => new BridgeException(BridgeError.BadArgs(arg, expected, message));
        public static BridgeException NotFound(string message, string arg = null, IReadOnlyList<string> candidates = null) =>
            new BridgeException(BridgeError.NotFound(message, arg, candidates));
        public static BridgeException Unsupported(string message, string phase = null) => new BridgeException(BridgeError.Unsupported(message, phase));
        public static BridgeException NotRealized(string message, string arg, string state) =>
            new BridgeException(Unavailable, message, new BridgeErrorDetail { Arg = arg, Extra = new Dictionary<string, object> { ["state"] = state } });

        /// <summary>Throws the bus error of <paramref name="r"/> as a bridge error (the bus codes are the bridge codes); Ok and NoOp return.</summary>
        public static CommandResult Check(CommandResult r, string arg = null)
        {
            if (r == null) throw new BridgeException(BridgeCodes.Internal, "no result");
            if (r.Status == CommandStatus.Error)
                throw new BridgeException(r.Code ?? BridgeCodes.Internal, r.Message, arg != null ? new BridgeErrorDetail { Arg = arg } : null);
            return r;
        }

        // ------------------------------------------------------------ node keys

        /// <summary>The node named by argument <paramref name="arg"/> (required): an exact id, or a unique prefix of ≥ 8 characters (with a warning).</summary>
        public static TreeNode Node(CoreContext c, string arg = "key")
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) throw new BridgeException(BridgeError.Missing(arg, ArgTypes.Node));
            return NodeOf(c, arg, KeyText(c, arg, k));
        }

        /// <summary>As <see cref="Node"/>, or null when the argument is absent.</summary>
        public static TreeNode OptionalNode(CoreContext c, string arg)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return null;
            return NodeOf(c, arg, KeyText(c, arg, k));
        }

        static string KeyText(CoreContext c, string arg, ArgKind k)
        {
            if (k == ArgKind.Integer || k == ArgKind.Float) throw BadArgs(arg, ArgTypes.Node, "HS2Next keys are node ids (strings)");
            if (k != ArgKind.String) throw BadArgs(arg, ArgTypes.Node);
            return c.Args.Get<string>(arg);
        }

        /// <summary>Resolves a key text against the session's model.</summary>
        public static TreeNode NodeOf(CoreContext c, string arg, string key)
        {
            var model = c.Session.Model;
            var n = model.Find(key);
            if (n != null) return n;
            var starts = new List<string>();
            if (!string.IsNullOrEmpty(key))
                foreach (var x in model.PreOrder())
                    if (x.Id.StartsWith(key, StringComparison.OrdinalIgnoreCase)) starts.Add(x.Id);
            if (key != null && key.Length >= 8 && starts.Count == 1)
            {
                c.Warn(arg + ": unique prefix of " + starts[0]);
                return model.Find(starts[0]);
            }
            var cand = new List<string>();
            foreach (var id in starts) { if (cand.Count == 5) break; cand.Add(id); }
            if (cand.Count == 0)
                foreach (var x in model.PreOrder()) { if (cand.Count == 5) break; cand.Add(x.Id); }
            throw NotFound((starts.Count > 1 ? "key \"" + key + "\" is ambiguous" : "no node \"" + key + "\" in the scene") , arg, cand);
        }

        /// <summary>A character node (wrong_kind otherwise).</summary>
        public static TreeNode Chara(CoreContext c, string arg = "key") => KeyKind.Chara.Check(Node(c, arg), arg);

        /// <summary>
        /// The node kind a key argument must name: its kind (null: any), the noun of the messages ("character": "…, not a
        /// character", "no character keys") and the expected text of the wrong_kind reply ("chara").
        /// </summary>
        public sealed class KeyKind
        {
            public static readonly KeyKind Any = new KeyKind(null, "node", null);
            public static readonly KeyKind Chara = new KeyKind(NodeKind.Chara, "character", "chara");
            public static readonly KeyKind Item = new KeyKind(NodeKind.Item, "item", "item");
            public static readonly KeyKind Light = new KeyKind(NodeKind.Light, "light", "light");
            public static readonly KeyKind Camera = new KeyKind(NodeKind.Camera, "camera", "camera");
            public static readonly KeyKind Route = new KeyKind(NodeKind.Route, "route", "route");

            public KeyKind(NodeKind? kind, string noun, string expected)
            {
                Kind = kind;
                Noun = noun;
                Expected = expected;
            }

            public NodeKind? Kind { get; }
            public string Noun { get; }
            public string Expected { get; }

            /// <summary><paramref name="n"/> when it is of this kind; wrong_kind naming <paramref name="arg"/> otherwise.</summary>
            public TreeNode Check(TreeNode n, string arg)
            {
                if (Kind != null && n.Kind != Kind.Value)
                    throw new BridgeException(BridgeError.WrongKind("node " + n.Id + " is a " + KindName(n) + ", not " + Article(Noun) + " " + Noun, arg, Expected));
                return n;
            }

            static string Article(string noun) => noun.Length > 0 && "aeiou".IndexOf(noun[0]) >= 0 ? "an" : "a";
        }

        /// <summary>The differences between the commands that take key / keys (their replies show them, so they stay).</summary>
        [Flags]
        public enum KeysRule
        {
            None = 0,
            /// <summary>A key named twice in keys is kept twice (else the first one is kept).</summary>
            KeepDuplicates = 1,
            /// <summary>An empty keys array is accepted (else bad_args "no &lt;noun&gt; keys").</summary>
            AllowEmpty = 2,
            /// <summary>keys is read as a string array without the array check (null reads as empty).</summary>
            NoArrayCheck = 4,
        }

        /// <summary>
        /// The nodes of exactly one of key (one node, <see cref="Node"/>) and keys (an array of node keys, each resolved by
        /// <see cref="NodeOf"/>), each of <paramref name="kind"/>; <paramref name="many"/> is true for keys.
        /// </summary>
        public static List<TreeNode> Keys(CoreContext c, KeyKind kind, out bool many, KeysRule rule = KeysRule.None)
        {
            many = FileRoots.OneOf(c, "key", "keys") == "keys";
            if (!many) return new List<TreeNode> { kind.Check(Node(c, "key"), "key") };
            return KeyList(c, "keys", kind, rule);
        }

        /// <summary>As <see cref="Keys(CoreContext, KeyKind, out bool, KeysRule)"/> for the commands that do not report which was given.</summary>
        public static List<TreeNode> Keys(CoreContext c, KeyKind kind) => Keys(c, kind, out _);

        /// <summary>The nodes of the key array <paramref name="arg"/> (array of node keys, each of <paramref name="kind"/>).</summary>
        public static List<TreeNode> KeyList(CoreContext c, string arg, KeyKind kind, KeysRule rule = KeysRule.None)
        {
            string[] keys;
            if ((rule & KeysRule.NoArrayCheck) != 0) keys = c.Args.Get<string[]>(arg) ?? Array.Empty<string>();
            else
            {
                if (c.Args.Kind(arg) != ArgKind.Array) throw BadArgs(arg, ArgTypes.ArrayOf(ArgTypes.Node));
                keys = c.Args.Get<string[]>(arg);
            }
            var l = new List<TreeNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in keys)
            {
                var n = kind.Check(NodeOf(c, arg, s), arg);
                if (seen.Add(n.Id) || (rule & KeysRule.KeepDuplicates) != 0) l.Add(n);
            }
            if (l.Count == 0 && (rule & KeysRule.AllowEmpty) == 0) throw BadArgs(arg, ArgTypes.ArrayOf(ArgTypes.Node), "no " + kind.Noun + " keys");
            return l;
        }

        /// <summary>The ids of <paramref name="nodes"/>, in order.</summary>
        public static List<string> Ids(IEnumerable<TreeNode> nodes)
        {
            var r = new List<string>();
            foreach (var n in nodes) r.Add(n.Id);
            return r;
        }

        /// <summary>The built character of argument <paramref name="arg"/> (unavailable when it is not built).</summary>
        public static CharaRealization Built(CoreContext c, string arg = "key") => Built(c, Chara(c, arg), arg);

        /// <summary>The built realization of character node <paramref name="n"/> (unavailable otherwise).</summary>
        public static CharaRealization Built(CoreContext c, TreeNode n, string arg = "key")
        {
            c.Runtime.ApplyNow();
            var r = c.Runtime.FindChara(n.Id);
            if (r == null || r.State != RealizationState.Built || r.InGraveyard || !r.Root)
                throw NotRealized("character " + n.Id + " is not built (" + (r?.State.ToString() ?? "NotRealized") + (r?.Detail != null ? ": " + r.Detail : "") + ")",
                    arg, r?.State.ToString() ?? "NotRealized");
            return r;
        }

        /// <summary>The kind string of a node: chara, item, light, folder, camera, route, or the document's kind.</summary>
        public static string KindName(TreeNode n)
        {
            switch (n.Kind)
            {
                case NodeKind.Chara: return "chara";
                case NodeKind.Item: return "item";
                case NodeKind.Light: return "light";
                case NodeKind.Folder: return "folder";
                case NodeKind.Camera: return "camera";
                case NodeKind.Route: return "route";
                default: return n.Doc.Kind ?? "unknown";
            }
        }

        // ------------------------------------------------------------ mutations

        /// <summary>
        /// The bus options of a bridge mutation: Source Bridge, Record None for <c>undo: false</c>, the history label. While a
        /// transaction is open (a batch's) the mutation is always recorded, so it becomes a step of that transaction's one
        /// entry: an unrecorded change would apply at once beside the open transaction, and a structural one would also clear
        /// the history under it, so one undo of the batch could not restore the scene of before it.
        /// </summary>
        public static ExecuteOptions Exec(CoreContext c, string label) =>
            new ExecuteOptions
            {
                Record = c.Undo || c.Session?.Commands?.ActiveTransaction != null ? HistoryMode.Record : HistoryMode.None,
                Source = CommandSource.Bridge, Label = label,
            };

        /// <summary>
        /// One history entry for a command that writes several fields: Session.Commands.BeginOrJoin(label, Exec) — inside a
        /// batch transaction the steps join it. Throws busy when the bus cannot open one. Commit with <see cref="Commit"/>;
        /// Dispose without commit reverts.
        /// </summary>
        public static Transaction Tx(CoreContext c, string label)
        {
            var tx = c.Session.Commands.BeginOrJoin(label, Exec(c, label));
            if (!tx.IsOpen) throw new BridgeException(tx.Failure?.Code ?? BridgeCodes.Busy, tx.Failure?.Message ?? "the bus is busy");
            return tx;
        }

        /// <summary>Commits <paramref name="tx"/>; a bus error becomes a bridge error.</summary>
        public static CommandResult Commit(Transaction tx) => Check(tx.Commit());

        /// <summary>One set inside a transaction; a bus error becomes a bridge error (the transaction then reverts when disposed).</summary>
        public static CommandResult Set(Transaction tx, string nodeId, string path, object value, string arg = null) => Check(tx.Set(nodeId, path, value), arg);

        // ------------------------------------------------------------ number forms

        /// <summary>D: the exact double of a float (HS2 `(double)f`).</summary>
        public static double D(float f) => f;
        public static double[] D3(Vector3 v) => new double[] { v.x, v.y, v.z };
        public static double[] D3(Vec3 v) => new double[] { v.X, v.Y, v.Z };
        public static double[] D4(Quaternion q) => new double[] { q.x, q.y, q.z, q.w };
        /// <summary>A colour as four exact doubles (r, g, b, a).</summary>
        public static double[] C(Color c) => new double[] { c.r, c.g, c.b, c.a };
        public static double[] C(Color4 c) => new double[] { c.R, c.G, c.B, c.A };
        /// <summary>R4: Math.Round((double)f, 4) (banker's rounding, as HS2).</summary>
        public static double R4(float f) => Math.Round((double)f, 4);
        public static double[] R4(Vector3 v) => new[] { R4(v.x), R4(v.y), R4(v.z) };
        public static double[] R4(Vec3 v) => new[] { R4(v.X), R4(v.Y), R4(v.Z) };
        /// <summary>R6: a quaternion rounded to 6 decimals, xyzw.</summary>
        public static double[] R6(Quaternion q) =>
            new[] { Math.Round((double)q.x, 6), Math.Round((double)q.y, 6), Math.Round((double)q.z, 6), Math.Round((double)q.w, 6) };

        // ------------------------------------------------------------ content references

        /// <summary>Sideloader runtime numbers start here; they do not exist in HS2Next.</summary>
        public const int SideloaderBase = 100000000;

        /// <summary>The reply form of a content reference: no (local id), guid, id ("guid#no").</summary>
        public static void AddRef(BridgeReply r, ContentId id)
        {
            r["no"] = id.IsEmpty ? -1 : id.Id;
            r["guid"] = id.IsEmpty ? null : id.SourceGuid;
            r["id"] = id.IsEmpty ? null : id.ToString();
        }

        /// <summary>
        /// The argument naming a content id ("guid#no"), `id` in the design, but `id` is the envelope's request id (package B's
        /// EnvelopeFields; a spec cannot declare it), so the commands take it as `ref`.
        /// </summary>
        public const string RefArg = "ref";

        static ContentId ParseId(CoreContext c, string arg = RefArg)
        {
            var text = c.Args.Get<string>(arg);
            if (!ContentId.TryParse(text, out var id) || id.IsEmpty) throw BadArgs(arg, ArgTypes.ContentId, "bad content id \"" + text + "\": expected \"guid#no\"");
            return id;
        }

        static bool HasAny(CoreContext c, params string[] args)
        {
            foreach (var a in args) if (c.Args.Has(a)) return true;
            return false;
        }

        /// <summary>
        /// The map of arguments no / guid / ref ("guid#no") / name; null when none is given. not_found with up to 5 candidates
        /// (same local id in other sources, or names containing the text).
        /// </summary>
        public static StudioMap ResolveMap(CoreContext c)
        {
            var db = c.Db;
            if (c.Args.Has(RefArg))
            {
                var id = ParseId(c);
                if (db.TryGetMap(id, out var m)) return m;
                throw NotFound("no map " + id, RefArg, MapCandidates(db, id.Id, null));
            }
            if (c.Args.Has("no"))
            {
                int no = c.Args.Get<int>("no");
                var id = c.Args.Has("guid") ? new ContentId(c.Args.Get<string>("guid"), no) : ContentId.Vanilla(no);
                if (db.TryGetMap(id, out var m)) return m;
                string name = c.Args.Get<string>("name");
                if (no >= SideloaderBase && name == null)
                    throw NotFound("map " + no + " is a Sideloader runtime number; HS2Next keeps the mods' own ids: give guid + no, ref or name", "no", MapCandidates(db, -1, null));
                if (name != null) return MapByName(db, name);
                throw NotFound("no map " + id + (c.Args.Has("guid") ? "" : " (ints alone mean vanilla rows; mod maps need guid or ref)"), "no", MapCandidates(db, no, null));
            }
            if (c.Args.Has("name")) return MapByName(db, c.Args.Get<string>("name"));
            if (c.Args.Has("guid")) throw new BridgeException(BridgeError.Missing("no", ArgTypes.Int));
            return null;
        }

        static StudioMap MapByName(StudioDb db, string name)
        {
            StudioMap contains = null;
            foreach (var m in db.Maps.Enumerate())
            {
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
                if (contains == null && m.Name != null && m.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) contains = m;
            }
            if (contains != null) return contains;
            throw NotFound("no map named \"" + name + "\"", "name", MapCandidates(db, -1, name));
        }

        static List<string> MapCandidates(StudioDb db, int no, string text)
        {
            var l = new List<string>();
            foreach (var m in db.Maps.Enumerate())
            {
                if (l.Count == 5) break;
                if (no >= 0 && m.No == no || text != null && m.Name != null && m.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    l.Add(m.Key + " " + m.Name);
            }
            return l;
        }

        /// <summary>The item of arguments group / category / no / guid / ref ("guid#no"); not_found with candidates.</summary>
        public static StudioItem ResolveItem(CoreContext c)
        {
            var db = c.Db;
            bool hasG = c.Args.Has("group"), hasC = c.Args.Has("category");
            int g = c.Args.Get("group", 0), cat = c.Args.Get("category", 0);
            StudioItem it;
            if (hasG && g == StudioDb.AccessoryGroup)
            {
                // the accessories offered as items: the accessory row of (category, reference); ints alone mean a base-game row
                if (!hasC) throw new BridgeException(BridgeError.Missing("category", ArgTypes.Int));
                ContentId aid;
                if (c.Args.Has(RefArg)) aid = ParseId(c);
                else
                {
                    if (!c.Args.Has("no")) throw new BridgeException(BridgeError.Missing("no", ArgTypes.Int));
                    int ano = c.Args.Get<int>("no");
                    aid = c.Args.Has("guid") ? new ContentId(c.Args.Get<string>("guid"), ano) : ContentId.Vanilla(ano);
                }
                if (db.TryGetAccessoryItem(cat, aid, out it) && it != null) return it;
                throw NotFound("no accessory " + aid + " in " + g + "/" + cat, c.Args.Has(RefArg) ? RefArg : "no", AccessoryCandidates(db, cat, aid.Id));
            }
            if (c.Args.Has(RefArg))
            {
                var id = ParseId(c);
                if (db.TryGetItem(id, out it) && it != null) return it;
                throw NotFound("no item " + id, RefArg, ItemCandidates(db, id.Id));
            }
            if (!c.Args.Has("no")) throw new BridgeException(BridgeError.Missing("no", ArgTypes.Int));
            int no = c.Args.Get<int>("no");
            if (c.Args.Has("guid"))
            {
                string guid = c.Args.Get<string>("guid");
                bool found = hasG && hasC ? db.TryGetItem(guid, g, cat, no, out it) : db.TryGetItem(new ContentId(guid, no), out it);
                if (found && it != null) return it;
                throw NotFound("no item " + guid + "#" + no + (hasG && hasC ? " in " + g + "/" + cat : ""), "no", ItemCandidates(db, no));
            }
            if (no >= SideloaderBase)
                throw NotFound("item " + no + " is a Sideloader runtime number; HS2Next keeps the mods' own ids: give guid + no or ref", "no", ItemCandidates(db, -1));
            if (db.TryGetItem(ContentId.Vanilla(no), out it) && it != null && (!hasG || it.Group == g) && (!hasC || it.Category == cat)) return it;
            throw NotFound("no vanilla item " + (hasG ? g + "/" : "") + (hasC ? cat + "/" : "") + no + " (ints alone mean vanilla rows; mod items need guid or ref)", "no", ItemCandidates(db, no));
        }

        static List<string> AccessoryCandidates(StudioDb db, int category, int no)
        {
            var l = new List<string>();
            foreach (var it in db.AccessoryItems)
            {
                if (l.Count == 5) break;
                if (it.No == no || !StudioDb.IsAccessoryCategory(category)) l.Add(it.Key + " " + it.Group + "/" + it.Category + " " + it.Name);
            }
            return l;
        }

        static List<string> ItemCandidates(StudioDb db, int no)
        {
            var l = new List<string>();
            if (no < 0) return l;
            foreach (var it in db.Items.Enumerate())
            {
                if (l.Count == 5) break;
                if (it.No == no) l.Add(it.Key + " " + it.Group + "/" + it.Category + " " + it.Name);
            }
            return l;
        }

        /// <summary>
        /// The animation of arguments group / category / no (defaults 0, 0, 0 as HS2) / guid / ref ("guid#no"): a GUID → that
        /// source's record; ints alone → the game's last-wins record. Null when not found (the caller answers not_found).
        /// </summary>
        public static StudioAnime ResolveAnime(CoreContext c, out string described)
        {
            var db = c.Db;
            int g = c.Args.Get("group", 0), cat = c.Args.Get("category", 0), no = c.Args.Get("no", 0);
            string guid = c.Args.Get<string>("guid");
            if (c.Args.Has(RefArg))
            {
                var id = ParseId(c);
                guid = id.SourceGuid;
                no = id.Id;
            }
            described = (guid != null ? guid + " " : "") + g + "/" + cat + "/" + no;
            if (guid != null) return db.TryGetAnime(guid, g, cat, no, out var a) ? a : null;
            return db.FindAnime(g, cat, no);
        }

        /// <summary>Candidates for a missing animation: the per-GUID records of the raw key.</summary>
        public static List<string> AnimeCandidates(StudioDb db, int group, int category, int no)
        {
            var l = new List<string>();
            foreach (var a in db.AnimeCandidates(group, category, no)) { if (l.Count == 5) break; l.Add(a.Guid + " " + a.Group + "/" + a.Category + "/" + a.No + " " + a.Name); }
            return l;
        }

        // ------------------------------------------------------------ HS2 argument tolerance

        /// <summary>sex: 0, 1, "male", "female", "0", "1" (anything else bad_args); <paramref name="fallback"/> when absent.</summary>
        public static int Sex(CoreContext c, string arg, int fallback)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return fallback;
            if (k == ArgKind.Integer)
            {
                int v = c.Args.Get<int>(arg);
                if (v == 0 || v == 1) return v;
            }
            else if (k == ArgKind.String)
            {
                switch (c.Args.Get<string>(arg).Trim().ToLowerInvariant())
                {
                    case "male": case "0": return 0;
                    case "female": case "1": return 1;
                }
            }
            throw BadArgs(arg, "0, 1, \"male\" or \"female\"");
        }

        // ------------------------------------------------------------ strict optional arguments

        // An optional argument left out or null reads as absent; any other JSON kind than the one asked for is bad_args (a number
        // is never read as a string, unlike ICommandArgs.TryGet / Get, which format numbers as text).

        /// <summary>True when an argument of kind <paramref name="k"/> is left out or null.</summary>
        public static bool Absent(ArgKind k) => k == ArgKind.Missing || k == ArgKind.Null;

        /// <summary>True when argument <paramref name="arg"/> is left out or null.</summary>
        public static bool Absent(CoreContext c, string arg) => Absent(c.Args.Kind(arg));

        /// <summary>A string (null when absent); another JSON kind is bad_args with <paramref name="expected"/>.</summary>
        public static string OptString(CoreContext c, string arg, string expected = ArgTypes.String)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return null;
            if (k != ArgKind.String) throw BadArgs(arg, expected);
            return c.Args.Get<string>(arg);
        }

        /// <summary>An integer (null when absent); another JSON kind, a fraction included, is bad_args.</summary>
        public static int? OptInt(CoreContext c, string arg)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return null;
            if (k != ArgKind.Integer) throw BadArgs(arg, ArgTypes.Int);
            return c.Args.Get<int>(arg);
        }

        /// <summary>A boolean (null when absent); another JSON kind is bad_args.</summary>
        public static bool? OptBool(CoreContext c, string arg)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return null;
            if (k != ArgKind.Bool) throw BadArgs(arg, ArgTypes.Bool);
            return c.Args.Get<bool>(arg);
        }

        /// <summary>A number, integer or fraction (null when absent); another JSON kind is bad_args.</summary>
        public static float? OptFloat(CoreContext c, string arg)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return null;
            if (k != ArgKind.Float && k != ArgKind.Integer) throw BadArgs(arg, ArgTypes.Float);
            return (float)c.Args.Get<double>(arg);
        }

        /// <summary>
        /// An [x, y, z] of finite numbers (null when absent): not an array is bad_args vec3; another length is bad_args with
        /// <paramref name="countExpected"/>; an element that is not a finite number is bad_args naming the element.
        /// </summary>
        public static Vec3? OptVec3(CoreContext c, string arg, string countExpected)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return null;
            if (k != ArgKind.Array) throw BadArgs(arg, ArgTypes.Vec3);
            Newtonsoft.Json.Linq.JArray a;
            try { a = Newtonsoft.Json.Linq.JArray.Parse(c.Args.RawJson(arg)); }
            catch (Exception e) { throw BadArgs(arg, ArgTypes.Vec3, e.Message); }
            if (a.Count != 3) throw BadArgs(arg, countExpected, arg + " has " + a.Count + " values");
            var v = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (a[i].Type != Newtonsoft.Json.Linq.JTokenType.Float && a[i].Type != Newtonsoft.Json.Linq.JTokenType.Integer)
                    throw BadArgs(arg + "[" + i + "]", ArgTypes.Float);
                v[i] = Finite((float)(double)a[i], arg + "[" + i + "]");
            }
            return new Vec3(v[0], v[1], v[2]);
        }

        /// <summary>An array of strings (null when absent): not an array is bad_args; an element that is not a string is bad_args
        /// naming the element.</summary>
        public static string[] OptStrings(CoreContext c, string arg)
        {
            var k = c.Args.Kind(arg);
            if (Absent(k)) return null;
            if (k != ArgKind.Array) throw BadArgs(arg, ArgTypes.ArrayOf(ArgTypes.String));
            Newtonsoft.Json.Linq.JArray ja;
            try { ja = Newtonsoft.Json.Linq.JArray.Parse(c.Args.RawJson(arg)); }
            catch (Exception e) { throw BadArgs(arg, ArgTypes.ArrayOf(ArgTypes.String), e.Message); }
            var r = new string[ja.Count];
            for (int i = 0; i < ja.Count; i++)
            {
                if (ja[i].Type != Newtonsoft.Json.Linq.JTokenType.String) throw BadArgs(arg + "[" + i + "]", ArgTypes.String);
                r[i] = (string)ja[i];
            }
            return r;
        }

        /// <summary>The value of a required-argument parser, or null when the argument is absent.</summary>
        public static int? Opt(CoreContext c, string arg, Func<CoreContext, string, int> parse) => Absent(c, arg) ? (int?)null : parse(c, arg);

        /// <summary><paramref name="v"/> when it is finite; bad_args "a finite number" naming <paramref name="arg"/> otherwise.</summary>
        public static float Finite(float v, string arg)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) throw BadArgs(arg, "a finite number");
            return v;
        }

        /// <summary>
        /// A name of <paramref name="names"/> (ordinal) or its index 0..count-1, as the index; bad_args with
        /// <paramref name="expected"/> otherwise: "\"x\" is not &lt;what&gt;", "&lt;word&gt; 9 outside 0..n", or the kind alone; absent is
        /// missing.
        /// </summary>
        public static int NameOrIndex(CoreContext c, string arg, IReadOnlyList<string> names, string expected, string what, string word)
        {
            switch (c.Args.Kind(arg))
            {
                case ArgKind.String:
                {
                    string s = c.Args.Get<string>(arg);
                    int k = -1;
                    for (int i = 0; i < names.Count && s != null; i++)
                        if (string.Equals(names[i], s, StringComparison.Ordinal)) { k = i; break; }
                    if (k < 0) throw BadArgs(arg, expected, "\"" + s + "\" is not " + what);
                    return k;
                }
                case ArgKind.Integer:
                {
                    int k = c.Args.Get<int>(arg);
                    if (k < 0 || k >= names.Count) throw BadArgs(arg, expected, word + " " + k + " outside 0.." + (names.Count - 1));
                    return k;
                }
                case ArgKind.Missing:
                case ArgKind.Null: throw new BridgeException(BridgeError.Missing(arg, expected));
                default: throw BadArgs(arg, expected);
            }
        }

        /// <summary>
        /// A text that may also be given as an integer (read as its invariant text); absent is missing with
        /// <paramref name="missingType"/>; another JSON kind is bad_args with <paramref name="expected"/>.
        /// </summary>
        public static string TextOrInt(CoreContext c, string arg, string missingType, string expected)
        {
            switch (c.Args.Kind(arg))
            {
                case ArgKind.String: return c.Args.Get<string>(arg);
                case ArgKind.Integer: return c.Args.Get<int>(arg).ToString(System.Globalization.CultureInfo.InvariantCulture);
                case ArgKind.Missing:
                case ArgKind.Null: throw new BridgeException(BridgeError.Missing(arg, missingType));
                default: throw BadArgs(arg, expected);
            }
        }

        // ------------------------------------------------------------ findings

        /// <summary>Findings as [{severity, code, path, message}].</summary>
        public static List<object> Findings(IEnumerable<Finding> findings, bool infos = true)
        {
            var l = new List<object>();
            if (findings == null) return l;
            foreach (var f in findings)
                if (infos || f.Severity != FindingSeverity.Info)
                    l.Add(new BridgeReply().With("severity", f.Severity.ToString()).With("code", f.Code).With("path", f.Path).With("message", f.Message));
            return l;
        }

        /// <summary>Scene findings as [{severity, code, path (the node id), message}].</summary>
        public static List<object> Findings(IEnumerable<SceneFinding> findings, Func<SceneFinding, bool> where = null)
        {
            var l = new List<object>();
            if (findings == null) return l;
            foreach (var f in findings)
                if (where == null || where(f))
                    l.Add(new BridgeReply().With("severity", f.Severity.ToString()).With("code", f.Code).With("path", f.NodeId).With("message", f.Message));
            return l;
        }

        // ------------------------------------------------------------ shared reply objects

        /// <summary>
        /// One scene node in HS2's `scene.objects` shape, HS2's members first: key, name, kind, pos, rot, scale (R4),
        /// worldPos (R4, when the node has a holder), parentName (children), for characters fullname, sex and anime; then
        /// parent, point (when present), state, visible. Reads realized state as it is: call Runtime.ApplyNow first.
        /// </summary>
        public static BridgeReply Describe(AppCore app, TreeNode n)
        {
            var r = new BridgeReply();
            var t = n.Doc.Transform ?? new NodeTransform();
            var real = app.Runtime?.Find(n.Id);
            r["key"] = n.Id;
            r["name"] = n.Label;
            r["kind"] = KindName(n);
            r["pos"] = R4(t.Pos);
            r["rot"] = R4(t.Rot);
            r["scale"] = R4(t.Scale);
            if (real != null && real.HasHolder) r["worldPos"] = R4(real.Holder.position);
            if (n.Parent != null) r["parentName"] = n.Parent.Label;
            if (n.Kind == NodeKind.Chara)
            {
                var ap = n.Appearance;
                var profile = ap?.Stored?.Chara?.Profile;
                r["fullname"] = profile?.Name ?? ap?.Card?.Name ?? "";
                r["sex"] = profile != null ? profile.Sex : ap?.Card != null ? ap.Card.Sex : -1;
                r["anime"] = Anime(app, n, real as CharaRealization);
            }
            if (n.Parent != null) r["parent"] = n.Parent.Id;
            if (n.Point != null) r["point"] = n.Point;
            r["state"] = real != null ? real.State.ToString() : "NotRealized";
            r["visible"] = n.Doc.Visible;
            return r;
        }

        static BridgeReply Anime(AppCore app, TreeNode n, CharaRealization real)
        {
            var a = (n.State as CharaNodeState)?.Anime ?? new CharaAnimeState();
            var r = new BridgeReply();
            var aref = a.Ref;
            r["group"] = aref?.Group ?? -1;
            r["category"] = aref?.Category ?? -1;
            r["no"] = aref?.No ?? -1;
            StudioAnime row = null;
            if (aref != null && app.Services?.Db != null) app.Services.Db.TryGetAnime(aref.Source, aref.Group, aref.Category, aref.No, out row);
            r["name"] = row?.Name ?? "";
            r["speed"] = a.Speed;
            var anim = real != null && real.State == RealizationState.Built ? real.Animation : null;
            if (anim && anim.IsLoaded)
            {
                var info = anim.StateInfo;
                r["length"] = info.length;
                r["normalizedTime"] = info.normalizedTime;
            }
            r["guid"] = aref?.Source;
            return r;
        }

        /// <summary>
        /// The view in HS2's `camera` shape: target, rot (R4), dist, fov (F) of `scene.camera.current` or the
        /// default view; worldPos and forward (R4) of the view camera when it exists; additions current, activeCamera.
        /// </summary>
        public static BridgeReply CameraState(AppCore app)
        {
            var cur = app.Session?.Model.Settings.Camera.Current;
            var view = app.Runtime?.GetPart<SceneViewCamera>();
            var r = new BridgeReply();
            r["target"] = cur != null ? R4(cur.Pivot) : R4(SceneViewCamera.DefaultPivot);
            r["rot"] = cur != null ? R4(cur.Rot) : R4(SceneViewCamera.DefaultRot);
            r["dist"] = cur != null ? cur.Distance : SceneViewCamera.DefaultDistance;
            r["fov"] = cur != null ? cur.Fov : SceneViewCamera.DefaultFov;
            var cam = view?.Camera;
            if (cam)
            {
                r["worldPos"] = R4(cam.transform.position);
                r["forward"] = R4(cam.transform.forward);
            }
            r["current"] = cur != null;
            r["activeCamera"] = view?.ActiveCameraId;
            return r;
        }

        /// <summary>
        /// HS2's `face` object of a character: eyebrow, eyes (list positions), eyebrowId, eyesId, mouth,
        /// eyesOpen, mouthOpen, blink, eyesLook, neckLook, tears, blush — from the model, or with
        /// <paramref name="realized"/> from the built character's expression (face.dump). The body is
        /// <see cref="FaceStateCore"/>, package W2's (CharaCommands.cs); without it the object is empty.
        /// </summary>
        public static BridgeReply FaceState(AppCore app, TreeNode node, bool realized = false)
        {
            var r = new BridgeReply();
            FaceStateCore(app, node, realized, r);
            return r;
        }

        /// <summary>Fills <paramref name="into"/> for <see cref="FaceState"/> (package W2).</summary>
        static partial void FaceStateCore(AppCore app, TreeNode node, bool realized, BridgeReply into);
    }
}
