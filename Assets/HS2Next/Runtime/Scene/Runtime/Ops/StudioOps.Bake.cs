// StudioOps, Chara to Item and the Cutter: what the commands and the panels do with bakes, the store and cuts, so all of them work with the bridge off.
// A bake of characters and a cut each add their item nodes and hide their source in one history entry («Bake to item», «Cut»); an edit of a piece's cut
// replaces its two blocks in one entry («Edit cut»); a marker is an item added in one entry («Add item»). The store is not the scene: writing, renaming,
// deleting, restoring, purging and keeping change no scene value and make no entry, except that a rename gives the scene's items of that bake that still
// carry the old name the new one, in one entry («Rename bake»). Blobs go into the scene's resources at once and stay there through undo, so redo is exact;
// a save keeps only what a node names. After every write of the store the app's item list reads the store again.
// A request is checked first; a refusal changes nothing and answers:
//   not_found    a node, a store row, a store when there is no UserData folder, a folder of old bakes
//   bad_args     a node of the wrong kind, a part, a mode, a shape or a value that is not one; nothing to set; a limit (bake.limit, cut.limit) or a
//                procedure that cannot run (cut.badStep) named in the message
//   unavailable  a character or item that is not built; a capture without a graphics device; nothing visible (bake.empty); blobs that cannot be had
//                (bake.missing); a store that is busy, unreadable or cannot be written (bake.busy, bake.storeUnreadable, bake.storeWrite)
//   busy         as every edit
// Captures (a bake, the source of a cut of a character or a plain item) need a graphics device and run on the main thread; a cut of a bake or a piece
// runs on the CPU. The panel steps a cut one mesh per frame: CutPrepare, then the plan's Steps, then CutCommit; the commands run the same to the end.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Cut;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What a bake of characters takes.</summary>
    public sealed class BakeMakeOptions
    {
        /// <summary>Part keys (body, head, hair0-hair3, clothes0-clothes7, acc&lt;slot&gt;, other); null or empty: the whole character.</summary>
        public IReadOnlyList<string> Parts;
        /// <summary>One item per part.</summary>
        public bool Split;
        /// <summary>The name; blank: an automatic one.</summary>
        public string Name;
        /// <summary>The FK groups Hair, Neck, Breast, Body, RightHand, LeftHand, Skirt; null: all seven.</summary>
        public bool[] FkGroups;
        public bool Insides = true, Centre = true, Hide = true, Store = true;
    }

    /// <summary>One bake a bake of characters made.</summary>
    public sealed class BakeMadeRow
    {
        /// <summary>The item node.</summary>
        public string Key;
        /// <summary>The bake's identity.</summary>
        public string Bake;
        /// <summary>The store row; null when the bake is in the scene only.</summary>
        public int? No;
        /// <summary>The main blob's resource name.</summary>
        public string Main;
        public long Bytes;
        public string Name;
    }

    /// <summary>What a bake of characters made.</summary>
    public sealed class BakeMakeResult
    {
        public readonly List<string> Items = new List<string>();
        public readonly List<BakeMadeRow> Bakes = new List<BakeMadeRow>();
        public double Ms;
    }

    /// <summary>A part present on a character.</summary>
    public sealed class BakePartRow
    {
        public string Part, Label;
        /// <summary>The name of the part's root object; null for "other".</summary>
        public string Item;
        public int Renderers;
        /// <summary>An accessory drawn with the hair shader (the Body and Hair presets take it).</summary>
        public bool HairType;
    }

    /// <summary>The store's rows.</summary>
    public sealed class BakeListResult
    {
        public readonly List<BakeStoreRow> Rows = new List<BakeStoreRow>();
        public int LastNumber;
    }

    /// <summary>A rename of a store row.</summary>
    public sealed class BakeRenameResult
    {
        public string Name, Old;
        public readonly List<string> Items = new List<string>();
    }

    /// <summary>A keep of a bake item in the store.</summary>
    public sealed class BakeKeepResult
    {
        public bool Kept;
        public int No;
    }

    /// <summary>What a bake item holds.</summary>
    public sealed class BakeStatusInfo
    {
        public string Bake, Main, Kind, Origin;
        public readonly List<string> Data = new List<string>();
        /// <summary>The bytes of the main and the data blobs the scene's resources hold.</summary>
        public long Bytes;
        /// <summary>Counts of the main's objects; -1 when the main cannot be read.</summary>
        public int Nodes = -1, Renderers = -1, Meshes = -1, Textures = -1, Materials = -1;
        public readonly List<string> Fk = new List<string>();
        public bool[] Correction;
        /// <summary>The member renderers of the built item, by path under its instance.</summary>
        public readonly List<string> Members = new List<string>();
        public List<string> Parts;
        public BakeMainSource Source;
        public int? No;
        /// <summary>A piece: the steps of its procedure and its source's identity; null otherwise.</summary>
        public int? PieceSteps;
        public string PieceSource;
    }

    /// <summary>A shape given by values in world space (cut.make's shape).</summary>
    public sealed class CutShapeGiven
    {
        public string Kind;
        public Vector3 Pos;
        public Quaternion Rot = Quaternion.identity;
        /// <summary>The full extent per axis in units; null: the shape's base size.</summary>
        public Vector3? Size;
    }

    /// <summary>What a cut takes.</summary>
    public sealed class CutMakeRequest
    {
        public string Target;
        /// <summary>A marker node; or <see cref="Shape"/>.</summary>
        public string Marker;
        public CutShapeGiven Shape;
        /// <summary>plane both, front, back; box both, inside, outside; tear and hole open; null: the shape's default.</summary>
        public string Mode;
        /// <summary>A ragged edge; null or amplitude 0: a clean cut.</summary>
        public CutRagParams Ragged;
        public bool ClothesOnly;
        /// <summary>The section; null: dark red, own fabric inside.</summary>
        public CutSection Section;
        public bool Hide = true;
        public string Name;
    }

    /// <summary>What an edit of a piece's cut changes; null members stay as they are.</summary>
    public sealed class CutSetRequest
    {
        public string Marker;
        public CutShapeGiven Shape;
        public string Mode;
        /// <summary>A new ragged edge; <see cref="ClearRagged"/> true: a clean cut.</summary>
        public CutRagParams Ragged;
        public bool ClearRagged;
        public bool? ClothesOnly;
        public CutSection Section;
        /// <summary>Run the procedure again with no change.</summary>
        public bool Recut;
    }

    /// <summary>What a cut made.</summary>
    public sealed class CutMakeResult
    {
        public readonly List<string> Pieces = new List<string>();
        public readonly List<string> Sides = new List<string>();
        /// <summary>The source's renderers the cut changed, per piece.</summary>
        public readonly List<List<int>> CutRenderers = new List<List<int>>();
        public double Ms;
    }

    /// <summary>What an edit of a piece's cut did.</summary>
    public sealed class CutSetResult
    {
        public bool Changed;
        public double Ms;
    }

    /// <summary>A piece's procedure as stored.</summary>
    public sealed class CutStatusInfo
    {
        public CutSource Source;
        public SortedDictionary<string, Vec3> Pose;
        public readonly List<CutStep> Steps = new List<CutStep>();
        public CutSection Section;
        /// <summary>The stored result was missing and was computed again at the build.</summary>
        public bool Recomputed;
    }

    /// <summary>A cut made ready: its source and its procedure. Run <see cref="Steps"/> (one mesh per step) or let the commit run them.</summary>
    public sealed class CutPlan : IDisposable
    {
        internal string TargetId, TargetName, Name, PieceId;
        internal bool Hide, IsEdit;
        internal BakeJob Job;
        internal BakeMain SourceMain;
        internal Func<string, byte[]> BlobOf;
        internal Matrix4x4 SourceRootWorld;
        internal ItemCut Block;
        internal string[] Keeps;
        internal string[] Ids;
        /// <summary>The FK state the pieces take (the source item's, kept to the piece's FK bones); null: none.</summary>
        internal ItemFk Fk;
        internal string OldMainSha, OldCutJson;
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        IEnumerator<bool> steps;
        CutOutcome outcome;

        /// <summary>The run, one mesh per MoveNext; <see cref="Outcome"/> is complete when it ends.</summary>
        public IEnumerator<bool> Steps
        {
            get
            {
                if (steps == null) steps = CutPieces.Steps(Block, SourceMain, BlobOf, Keeps, Ids, out outcome);
                return steps;
            }
        }

        /// <summary>The run's outcome (complete once the steps have ended).</summary>
        public CutOutcome Outcome => outcome;

        /// <summary>Diagnostics (not saved): the MoveNext calls <see cref="Finish"/> made, and how many of them it followed with a wait on the
        /// outcome's pending work.</summary>
        internal int FinishSteps, FinishWaits;

        /// <summary>Runs what is left of the steps; while the outcome has pending work (the pieces' blobs on workers) it waits for that work
        /// instead of stepping again, as <see cref="CutPieces.Run"/> does. A failure of the work is thrown by the next step.</summary>
        public void Finish()
        {
            var e = Steps;
            while (true)
            {
                FinishSteps++;
                if (!e.MoveNext()) break;
                var pending = outcome?.Pending;
                if (pending == null) continue;
                FinishWaits++;
                try { pending.Wait(); }
                catch (AggregateException) { }
            }
        }

        /// <summary>Frees the source's capture when the plan is not committed.</summary>
        public void Dispose()
        {
            Job?.Dispose();
            Job = null;
        }
    }

    public sealed partial class StudioOps
    {
        /// <summary>The seven FK groups, in order.</summary>
        public static readonly string[] BakeFkGroupNames = { "Hair", "Neck", "Breast", "Body", "RightHand", "LeftHand", "Skirt" };

        /// <summary>A capture can run (a graphics device draws).</summary>
        public static bool BakeHasDevice => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>The store of this runtime's UserData folder (opened at its first use; reading creates nothing); null when there is none.</summary>
        public BakeStore OpenBakeStore() => BakeProvider.StoreOf(rt);

        ResStore BakeRes => Model.File.Res;

        void BakeRowsChanged() => AppItemRows.RefreshBakes(Services?.Db);

        // ------------------------------------------------------------ kinds

        static string BakeKindText(TreeNode n)
        {
            if (n.State is ItemNodeState s)
            {
                if (CutMarkers.IsMarker(s)) return "marker";
                if (s.Cut != null) return "piece";
                if (s.Bake != null) return "bake item";
            }
            return KindName(n);
        }

        static OpResult BakeWrongKind(TreeNode n, string arg, string want) =>
            OpResult.BadArgs(arg, want, "node " + n.Id + " is " + Article(BakeKindText(n)) + BakeKindText(n) + ", not " + want);

        /// <summary>A character node.</summary>
        TreeNode BakeChara(string id, string arg, out OpResult fail)
        {
            var n = Find(id, arg, out fail);
            if (n == null) return null;
            if (n.Kind != NodeKind.Chara) { fail = BakeWrongKind(n, arg, "a character"); return null; }
            return n;
        }

        /// <summary>An item node drawn from a bake (a bake item or a piece).</summary>
        TreeNode BakeItemNode(string id, string arg, out OpResult fail)
        {
            var n = Find(id, arg, out fail);
            if (n == null) return null;
            if (!(n.State is ItemNodeState s) || s.Bake == null) { fail = BakeWrongKind(n, arg, "an item drawn from a bake"); return null; }
            return n;
        }

        /// <summary>A piece: an item with a cut block (and its result).</summary>
        TreeNode CutPieceNode(string id, string arg, out OpResult fail)
        {
            var n = Find(id, arg, out fail);
            if (n == null) return null;
            if (!(n.State is ItemNodeState s) || s.Cut == null || s.Bake == null) { fail = BakeWrongKind(n, arg, "a piece"); return null; }
            return n;
        }

        CharaRealization BakeBuiltChara(TreeNode n, string arg, out OpResult fail)
        {
            rt.ApplyNow();
            var c = rt.FindChara(n.Id);
            if (c == null || c.State != RealizationState.Built || c.InGraveyard || !c.Root)
            {
                fail = OpResult.Unavailable("character " + n.Id + " is not built (" + (c?.State.ToString() ?? "NotRealized") + ")", arg, c?.State.ToString() ?? "NotRealized");
                return null;
            }
            fail = null;
            return c;
        }

        static OpResult BakeNoDevice(string arg) =>
            OpResult.Unavailable("a capture needs a graphics device; a stored bake loads and a cut of a bake runs without one", arg, "NoDevice");

        static OpResult BakeEmpty(string arg, string what) => OpResult.Unavailable(BakeFindings.Empty + ": " + what, arg, BakeFindings.Empty);

        static OpResult BakeStoreFailed(BakeStoreException e) => OpResult.Unavailable(e.Code + ": " + e.Message, null, e.Code);

        /// <summary>The state of a new item drawn from a bake (the app's group, category 0; its reference names the store row or 0).</summary>
        public static ItemNodeState BakeItemState(ItemBake block, int? no)
        {
            var row = new StudioItem
            {
                Key = new ContentId(AppItemRows.BakeGuid, no ?? 0), Group = AppItemRows.Group, Category = AppItemRows.BakesCategory,
                Name = "", BundlePath = "", FileName = "", Manifest = "",
            };
            var st = ItemDefaults.FromPrefab(null, row);
            st.Bake = block;
            return st;
        }

        /// <summary>One history entry for several steps; a step that fails takes the steps before it back.</summary>
        OpResult BakeEntry(string label, ExecuteOptions o, Func<ExecuteOptions, OpResult> steps)
        {
            o = Labelled(o, label);
            var tx = Open(o.Label, o, out var fail);
            if (tx == null) return fail;
            OpResult r;
            using (tx)
            {
                r = steps(new ExecuteOptions { Record = HistoryMode.Record, Source = o.Source, Label = o.Label });
                if (r.Ok) r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>Adds an item node at a world pose (at the scene's root) with a name; its id.</summary>
        string BakeAddNode(ItemNodeState st, string name, Vector3 pos, Quaternion rot, Vector3 scale, ExecuteOptions step, out OpResult fail)
        {
            var add = Session.AddItem(st, null, null, -1, step);
            if (!add.Ok) { fail = OpResult.From(add); return null; }
            string id = add.NewId;
            foreach (var (path, value) in new (string, object)[]
            {
                ("name", string.IsNullOrEmpty(name) ? null : name), ("transform.pos", UnityObjects.V(pos)), ("transform.rot", UnityObjects.V(rot.eulerAngles)),
                ("transform.scale", UnityObjects.V(scale)),
            })
            {
                var s = Session.Set(id, path, value, step);
                if (!s.Ok) { fail = OpResult.From(s, path); return null; }
            }
            fail = null;
            return id;
        }

        static string Stamp() => DateTime.Now.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------ parts

        /// <summary>The parts present on a character (Value = a list of <see cref="BakePartRow"/>).</summary>
        public OpResult BakeParts(string nodeId)
        {
            var n = BakeChara(nodeId, "key", out var fail);
            if (n == null) return fail;
            var c = BakeBuiltChara(n, "key", out fail);
            if (c == null) return fail;
            var rows = new List<BakePartRow>();
            foreach (var p in BakeCapture.Parts(c))
                rows.Add(new BakePartRow { Part = p.Key, Label = p.Label, Item = p.Root ? p.Root.name : null, Renderers = p.Renderers, HairType = p.HairType });
            var r = OpResult.NothingToDo();
            r.Value = rows;
            return r;
        }

        /// <summary>True for a part key of the form body, head, hair0-hair3, clothes0-clothes7, acc&lt;slot&gt; or other.</summary>
        public static bool BakeIsPartKey(string k)
        {
            if (k == "body" || k == "head" || k == "other") return true;
            if (k == null) return false;
            if (k.Length == 5 && k.StartsWith("hair", StringComparison.Ordinal)) return k[4] >= '0' && k[4] <= '3';
            if (k.Length == 8 && k.StartsWith("clothes", StringComparison.Ordinal)) return k[7] >= '0' && k[7] <= '7';
            if (k.Length > 3 && k.StartsWith("acc", StringComparison.Ordinal) && int.TryParse(k.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out int slot))
                return slot >= 0 && k.Substring(3) == slot.ToString(CultureInfo.InvariantCulture);
            return false;
        }

        // ------------------------------------------------------------ a bake of characters

        /// <summary>
        /// Bakes characters (label «Bake to item», one entry): per character the whole of it, the chosen parts as one bake, or one bake per part; each
        /// bake's blobs go into the scene's resources and, with Store, into the store; an item node per bake at its pose; the characters hidden with
        /// Hide. Value = a <see cref="BakeMakeResult"/>; NewIds = the items. A part a character lacks is skipped; a store that cannot be written leaves the
        /// bake in the scene with a warning.
        /// </summary>
        public OpResult BakeMake(IReadOnlyList<string> nodeIds, BakeMakeOptions opt = null, ExecuteOptions o = default)
        {
            var sw = Stopwatch.StartNew();
            opt = opt ?? new BakeMakeOptions();
            var charas = new List<TreeNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            nodeIds = nodeIds ?? Array.Empty<string>();
            foreach (var id in nodeIds)
            {
                var n = BakeChara(id, nodeIds.Count > 1 ? "keys" : "key", out var fail);
                if (n == null) return fail;
                if (seen.Add(n.Id)) charas.Add(n);
            }
            if (charas.Count == 0) return OpResult.BadArgs("key", "a character", "no character to bake");
            var parts = new List<string>();
            foreach (var p in opt.Parts ?? Array.Empty<string>())
            {
                if (!BakeIsPartKey(p)) return OpResult.BadArgs("parts", "body, head, hair0-hair3, clothes0-clothes7, acc<slot> or other", "\"" + p + "\" is not a part");
                if (!parts.Contains(p)) parts.Add(p);
            }
            if (opt.FkGroups != null && opt.FkGroups.Length != 7) return OpResult.BadArgs("fk", "the seven FK groups");
            var built = new List<CharaRealization>();
            foreach (var n in charas)
            {
                var c = BakeBuiltChara(n, "key", out var fail);
                if (c == null) return fail;
                built.Add(c);
            }
            if (!BakeHasDevice) return BakeNoDevice("key");

            // the bakes to make: per character, the parts it has
            var plan = new List<(int chara, List<string> parts, string label)>();
            for (int i = 0; i < built.Count; i++)
            {
                var present = BakeCapture.Parts(built[i]);
                var chosen = new List<BakeCapture.PartInfo>();
                foreach (var p in present) if (parts.Count == 0 || parts.Contains(p.Key)) chosen.Add(p);
                if (opt.Split)
                {
                    foreach (var p in chosen) if (p.Renderers > 0) plan.Add((i, new List<string> { p.Key }, p.Label));
                }
                else if (parts.Count == 0) plan.Add((i, null, ""));
                else if (chosen.Count > 0)
                {
                    var keys = new List<string>();
                    var labels = new List<string>();
                    foreach (var p in chosen) { keys.Add(p.Key); labels.Add(p.Label); }
                    plan.Add((i, keys, string.Join(", ", labels)));
                }
            }
            if (plan.Count == 0) return BakeEmpty("parts", "the characters have none of the parts");

            var jobs = new List<(int chara, string label, BakeJob job)>();
            try
            {
                foreach (var (chara, ps, label) in plan)
                {
                    var job = BakeJob.OfChara(built[chara], new BakeOptions { Parts = ps, FkGroups = opt.FkGroups, Insides = opt.Insides, Centre = opt.Centre });
                    if (job.Capture?.Bake == null || job.Capture.Bake.Renderers.Count == 0) { job.Dispose(); continue; }
                    jobs.Add((chara, label, job));
                }
                if (jobs.Count == 0) return BakeEmpty("parts", "nothing visible to bake");
                long bytes = 0;
                foreach (var j in jobs)
                {
                    j.job.Finish();
                    bytes += j.job.Bytes;
                    if (j.job.Main.DataBlobs().Count > BakeLimits.MaxData)
                        return OpResult.BadArgs("key", null, BakeFindings.Limit + ": a bake of " + j.job.Main.DataBlobs().Count + " data blobs is over the limit of " + BakeLimits.MaxData);
                }
                if (BakeLimits.WouldExceed(BakeRes, bytes))
                    return OpResult.BadArgs("key", null, BakeFindings.Limit + ": " + bytes + " bytes of bakes would take the scene's resources over " + BakeLimits.MaxSceneBytes + " bytes");

                var made = new BakeMakeResult();
                var warnings = new List<string>();
                bool several = charas.Count > 1, stored = false;
                var store = opt.Store ? OpenBakeStore() : null;
                if (opt.Store && store == null) warnings.Add(BakeFindings.StoreWrite + ": there is no UserData folder; the bakes stay in the scene");
                var r = BakeEntry(Labels.BakeMake, o, step =>
                {
                    var cache = BakeCache.Of(rt, true);
                    foreach (var (chara, label, job) in jobs)
                    {
                        var node = charas[chara];
                        string name = BakeName(opt.Name, node.Label, label, opt.Split, several);
                        job.Build(cache);
                        job.AddTo(BakeRes);
                        int? no = null;
                        if (store != null)
                        {
                            try
                            {
                                using (store.Hold()) no = store.Add(job.Row(name), job.AllBlobs()).Number;
                                stored = true;
                            }
                            catch (BakeStoreException e) { warnings.Add(e.Code + ": " + e.Message + "; the bake stays in the scene"); }
                            catch (ArgumentException e) { warnings.Add(BakeFindings.StoreWrite + ": " + e.Message + "; the bake stays in the scene"); }
                        }
                        var cap = job.Capture;
                        string id = BakeAddNode(BakeItemState(job.Block(no), no), name, cap.Position, cap.Rotation, cap.Scale, step, out var fail);
                        if (id == null) return fail;
                        made.Items.Add(id);
                        made.Bakes.Add(new BakeMadeRow { Key = id, Bake = job.Main.Id, No = no, Main = BakeBlob.ResName(job.MainSha), Bytes = job.Bytes, Name = name });
                    }
                    if (opt.Hide)
                        foreach (var n in charas)
                        {
                            var s = Session.Set(n.Id, "visible", false, step);
                            if (!s.Ok) return OpResult.From(s, "hide");
                        }
                    return OpResult.Success();
                });
                if (stored) BakeRowsChanged();
                r.Warnings.AddRange(warnings);
                if (!r.Ok) return r;
                made.Ms = sw.Elapsed.TotalMilliseconds;
                r.Value = made;
                r.NewIds = made.Items;
                return r;
            }
            finally
            {
                foreach (var j in jobs) j.job.Dispose();
            }
        }

        /// <summary>A bake's name: the given name (with the part when one item per part, with the character when several), else
        /// "&lt;character&gt; &lt;parts&gt; &lt;MM-dd HH:mm&gt;".</summary>
        public static string BakeName(string given, string character, string parts, bool split, bool several)
        {
            if (!string.IsNullOrWhiteSpace(given))
            {
                string s = given.Trim();
                if (split && !string.IsNullOrEmpty(parts)) s += " " + parts;
                if (several) s += " - " + character;
                return s;
            }
            return (character ?? "") + (string.IsNullOrEmpty(parts) ? "" : " " + parts) + " " + Stamp();
        }

        // ------------------------------------------------------------ the store

        BakeStoreRow BakeRow(int? no, string bakeId, out OpResult fail)
        {
            var store = OpenBakeStore();
            fail = null;
            if (store == null) { fail = OpResult.NotFound("there is no store: no UserData folder", no != null ? "no" : "bake"); return null; }
            if (no == null && string.IsNullOrEmpty(bakeId)) { fail = OpResult.BadArgs("no", "no or bake", "name a store row by no or bake"); return null; }
            var row = no != null ? store.Find(no.Value) : store.FindById(bakeId);
            if (row == null) fail = OpResult.NotFound(no != null ? "the store has no row " + no.Value : "the store has no bake " + bakeId, no != null ? "no" : "bake");
            return row;
        }

        /// <summary>The store's rows whose name or number holds <paramref name="q"/> (blank: every row); deleted rows with <paramref name="deleted"/>.
        /// Value = a <see cref="BakeListResult"/>; an unreadable index is a warning and no row.</summary>
        public OpResult BakeList(string q = null, bool deleted = false)
        {
            var res = new BakeListResult();
            var r = OpResult.NothingToDo();
            var store = OpenBakeStore();
            if (store != null)
            {
                if (store.Unreadable) r.Warnings.Add(BakeFindings.StoreUnreadable + ": the store's index cannot be read; it is kept and copied aside at the next write");
                res.LastNumber = store.LastNumber;
                foreach (var row in deleted ? store.AllRows() : store.LiveRows())
                {
                    if (!string.IsNullOrWhiteSpace(q) && (row.Name ?? "").IndexOf(q.Trim(), StringComparison.OrdinalIgnoreCase) < 0 &&
                        row.Number.ToString(CultureInfo.InvariantCulture).IndexOf(q.Trim(), StringComparison.Ordinal) < 0) continue;
                    res.Rows.Add(row);
                }
            }
            r.Value = res;
            return r;
        }

        /// <summary>
        /// Renames a store row (the index only); the scene's items of that bake whose name is the old one take the new one in one entry «Rename bake»
        /// (undo renames the items back; the store keeps the new name). Value = a <see cref="BakeRenameResult"/>.
        /// </summary>
        public OpResult BakeRename(int? no, string bakeId, string name, ExecuteOptions o = default)
        {
            if (name == null || string.IsNullOrWhiteSpace(name)) return OpResult.BadArgs("name", "a name", "a bake needs a name");
            name = name.Trim();
            var row = BakeRow(no, bakeId, out var fail);
            if (row == null) return fail;
            string old;
            try { old = OpenBakeStore().Rename(row.Number, name); }
            catch (BakeStoreException e) { return BakeStoreFailed(e); }
            if (old == null) return OpResult.NotFound("the store has no row " + row.Number, "no");
            var res = new BakeRenameResult { Name = name, Old = old };
            BakeRowsChanged();
            var items = new List<(string, string, string)>();
            foreach (var n in Model.PreOrder())
                if (n.State is ItemNodeState s && s.Bake != null) items.Add((n.Id, n.Label, s.Bake.Id));
            res.Items.AddRange(BakeStore.RenameTargets(items, row.Id, old));
            OpResult r = OpResult.NothingToDo();
            if (old != name && res.Items.Count > 0)
                r = BakeEntry(Labels.BakeRename, o, step =>
                {
                    var x = OpResult.Success();
                    foreach (var id in res.Items)
                    {
                        var s = Session.Set(id, "name", name, step);
                        if (!s.Ok) return OpResult.From(s, "name");
                        x.Record(id, "name", s.Status);
                    }
                    return x;
                });
            if (!r.Ok) return r;
            r.Value = res;
            return r;
        }

        /// <summary>Marks a store row deleted, or clears the mark with <paramref name="restore"/>; no file is touched. Value = the row's mark after it.</summary>
        public OpResult BakeDelete(int? no, string bakeId, bool restore = false)
        {
            var row = BakeRow(no, bakeId, out var fail);
            if (row == null) return fail;
            bool ok;
            try { ok = OpenBakeStore().Delete(row.Number, restore); }
            catch (BakeStoreException e) { return BakeStoreFailed(e); }
            if (!ok) return OpResult.NotFound("the store has no row " + row.Number, "no");
            BakeRowsChanged();
            var r = row.Deleted == !restore ? OpResult.NothingToDo() : OpResult.Success();
            r.Value = !restore;
            return r;
        }

        /// <summary>What a purge would remove now (nothing is removed). Value = a <see cref="BakePurgeResult"/>.</summary>
        public OpResult BakePurgePreview()
        {
            var r = OpResult.NothingToDo();
            r.Value = OpenBakeStore()?.PurgePreview() ?? new BakePurgeResult();
            return r;
        }

        /// <summary>
        /// Removes the deleted rows and every blob no remaining row lists (the set to keep read from the index alone); refused while a bake is written
        /// or imported (bake.busy) and while the index cannot be read. Value = a <see cref="BakePurgeResult"/>.
        /// </summary>
        public OpResult BakePurge()
        {
            var store = OpenBakeStore();
            var r = OpResult.NothingToDo();
            if (store == null) { r.Value = new BakePurgeResult(); return r; }
            BakePurgeResult p;
            try { p = store.Purge(); }
            catch (BakeStoreException e) { return BakeStoreFailed(e); }
            if (p.Rows > 0) BakeRowsChanged();
            if (p.Rows > 0 || p.Blobs > 0 || p.Leftovers > 0) r = OpResult.Success();
            r.Value = p;
            return r;
        }

        /// <summary>
        /// Keeps a bake item's bake in the store: a new row with a new number for its blobs (the same identity), named as the item; a row of that
        /// identity in the store already: nothing new. Value = a <see cref="BakeKeepResult"/>.
        /// </summary>
        public OpResult BakeKeep(string nodeId)
        {
            var n = BakeItemNode(nodeId, "key", out var fail);
            if (n == null) return fail;
            var bake = ((ItemNodeState)n.State).Bake;
            var store = OpenBakeStore();
            if (store == null) return OpResult.Unavailable(BakeFindings.StoreWrite + ": there is no UserData folder", "key", BakeFindings.StoreWrite);
            var had = store.FindById(bake.Id);
            var r = OpResult.NothingToDo();
            if (had != null)
            {
                r.Value = new BakeKeepResult { Kept = false, No = had.Number };
                return r;
            }
            var findings = new FindingList();
            var set = BakeBlobSet.Gather(bake, BakeRes, store, findings, n.Id);
            if (set.Status != BakeBlobSet.Result.Ok || set.Absent.Count > 0)
                return OpResult.Unavailable((set.Status == BakeBlobSet.Result.Corrupt ? BakeFindings.BlobCorrupt : BakeFindings.Missing) + ": the item's blobs are not all in the scene", "key",
                    set.Status == BakeBlobSet.Result.Corrupt ? BakeFindings.BlobCorrupt : BakeFindings.Missing);
            var m = set.Main;
            var row = new BakeStoreRow
            {
                Id = m.Id, Name = n.Label ?? "", Created = m.Created, Kind = m.Kind, Main = set.MainSha, Parts = m.Parts == null ? "" : string.Join(",", m.Parts),
            };
            row.Data.AddRange(m.DataBlobs());
            row.Fk.AddRange(m.Fk);
            var mainBlob = BakeRes.Get(BakeBlob.ResName(set.MainSha));
            if (mainBlob == null) return OpResult.Unavailable(BakeFindings.Missing + ": the item's main blob is not in the scene", "key", BakeFindings.Missing);
            var blobs = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(set.MainSha, mainBlob) };
            foreach (var sha in row.Data) blobs.Add(new KeyValuePair<string, byte[]>(sha, set.Get(sha)));
            BakeStoreRow stored;
            try { using (store.Hold()) stored = store.Add(row, blobs); }
            catch (BakeStoreException e) { return BakeStoreFailed(e); }
            catch (ArgumentException e) { return OpResult.Unavailable(BakeFindings.StoreWrite + ": " + e.Message, "key", BakeFindings.StoreWrite); }
            BakeRowsChanged();
            r = OpResult.Success();
            r.Value = new BakeKeepResult { Kept = true, No = stored.Number };
            return r;
        }

        /// <summary>What a bake item holds (Value = a <see cref="BakeStatusInfo"/>); a main that cannot be read leaves the counts at -1 with a warning.</summary>
        public OpResult BakeStatus(string nodeId)
        {
            var n = BakeItemNode(nodeId, "key", out var fail);
            if (n == null) return fail;
            var st = (ItemNodeState)n.State;
            var bake = st.Bake;
            var info = new BakeStatusInfo { Bake = bake.Id, Main = bake.Main, No = bake.No };
            info.Data.AddRange(bake.Data);
            var r = OpResult.NothingToDo();
            var res = BakeRes;
            foreach (var name in bake.Blobs()) info.Bytes += res.Get(name)?.LongLength ?? 0;
            BakeMain m = null;
            var mainBytes = res.Get(bake.Main);
            if (mainBytes != null)
            {
                try { m = BakeMain.FromBlob(mainBytes); }
                catch (Exception e) { r.Warnings.Add(BakeFindings.BlobCorrupt + ": the main cannot be read: " + e.Message); }
            }
            else r.Warnings.Add(BakeFindings.Missing + ": the main is not in the scene");
            if (m != null)
            {
                info.Kind = m.Kind;
                info.Origin = m.Origin;
                info.Nodes = m.Nodes.Count;
                info.Renderers = m.Renderers.Count;
                info.Meshes = m.Meshes.Count;
                info.Textures = m.Textures.Count;
                info.Materials = m.Materials.Count;
                info.Fk.AddRange(m.Fk);
                info.Correction = m.Correction == null ? null : (bool[])m.Correction.Clone();
                info.Parts = m.Parts == null ? null : new List<string>(m.Parts);
                info.Source = m.Source;
            }
            rt.ApplyNow();
            var it = rt.FindItem(n.Id);
            var proto = it != null ? BakeCache.PrototypeOf(it) : null;
            if (proto != null && it.Instance)
                foreach (var x in BakeBuild.MembersOf(proto, it.Instance))
                    if (x) info.Members.Add(ItemMaterialEdits.PathIn(it.Instance.transform, x.transform));
            if (st.Cut != null)
            {
                info.PieceSteps = st.Cut.Steps.Count;
                info.PieceSource = st.Cut.Source?.Id;
            }
            r.Value = info;
            return r;
        }

        /// <summary>
        /// Adds an item drawn from store row <paramref name="no"/> straight from the store, in one entry «Add item», also when the app's item group is not
        /// in the list (a list file defines its number): the row's blobs go into the scene's resources, the node's bake block is the row's, its reference
        /// hs2next.bake#&lt;no&gt; in the app's group, category 0, with the defaults of an item without a prefab. The item is built from its bake block. A
        /// deleted row is not added. Value = its id; NewIds = [it].
        /// </summary>
        public OpResult BakeAdd(int no, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            var store = OpenBakeStore();
            if (store == null) return OpResult.NotFound("there is no store: no UserData folder", "no");
            var row = store.Find(no);
            if (row == null || row.Deleted) return OpResult.NotFound(row == null ? "the store has no row " + no : "the store's row " + no + " is deleted", "no");
            var res = BakeRes;
            var blobs = new List<byte[]>();
            long bytes = 0;
            foreach (var sha in row.Blobs())
            {
                if (res.Contains(BakeBlob.ResName(sha))) continue;
                var b = store.ReadBlob(sha);
                if (b == null) return OpResult.Unavailable(BakeFindings.StoreCorrupt + ": the store's bake " + no + " is damaged (" + sha + ")", "no", BakeFindings.StoreCorrupt);
                blobs.Add(b);
                bytes += b.LongLength;
            }
            if (BakeLimits.WouldExceed(res, bytes))
                return OpResult.BadArgs("no", null, BakeFindings.Limit + ": " + bytes + " bytes of the bake would take the scene's resources over " + BakeLimits.MaxSceneBytes + " bytes");
            foreach (var b in blobs) res.AddBlob(b);
            var r = OpResult.From(Session.AddItem(BakeItemState(row.ToItemBake(), row.Number), parentId, point, index, Labelled(o, Labels.AddItem)));
            rt.ApplyNow();
            if (r.Ok && r.NewIds.Count > 0) r.Value = r.NewIds[0];
            return r;
        }

        // ------------------------------------------------------------ the old bakes

        /// <summary>
        /// Imports the old bakes of a folder (read in place, never written; absent: the HS2 install's UserData/CharaToItem) as new bakes in the store
        /// under their old numbers; a bake whose number the store holds is skipped (bake.importExists), so a second run adds nothing. Needs a graphics
        /// device; changes no scene value and makes no entry. Value = the importer's <see cref="OldBakeImportResult"/>.
        /// </summary>
        public OpResult BakeImport(string folder = null)
        {
            string dir = string.IsNullOrEmpty(folder) ? OldBakeImport.DefaultFolder(Services?.Hs2Root)
                : System.IO.Path.IsPathRooted(folder) || string.IsNullOrEmpty(Services?.Hs2Root) ? folder : System.IO.Path.Combine(Services.Hs2Root, "UserData", folder);
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) return OpResult.NotFound("there is no folder of old bakes at \"" + (dir ?? "") + "\"", "path");
            if (!BakeHasDevice) return BakeNoDevice("path");
            var store = OpenBakeStore();
            if (store == null) return OpResult.Unavailable(BakeFindings.StoreWrite + ": there is no UserData folder", "path", BakeFindings.StoreWrite);
            if (store.Busy) return OpResult.Unavailable(BakeFindings.Busy + ": a bake is being written or imported", "path", BakeFindings.Busy);
            OldBakeImportResult res;
            try { res = OldBakeImport.Run(rt, dir); }
            catch (BakeStoreException e) { return BakeStoreFailed(e); }
            catch (InvalidOperationException e) { return OpResult.Unavailable(e.Message, "path", "NoDevice"); }
            var r = res.Imported.Count > 0 ? OpResult.Success() : OpResult.NothingToDo();
            foreach (var f in res.Findings) if (f.Severity != FindingSeverity.Info) r.Warnings.Add(f.Code + ": " + f.Message);
            r.Value = res;
            return r;
        }

        // ------------------------------------------------------------ markers

        /// <summary>The scene's cut markers, in tree order.</summary>
        public List<string> CutMarkerNodes()
        {
            var l = new List<string>();
            foreach (var n in Model.PreOrder()) if (n.State is ItemNodeState s && CutMarkers.IsMarker(s)) l.Add(n.Id);
            return l;
        }

        /// <summary>
        /// Adds a cut marker (plane, box, tear, hole) at the bounds centre of <paramref name="targetId"/>'s drawn objects, else at the camera's pivot,
        /// in one entry «Add item», and selects it. Value = its id; NewIds = [it].
        /// </summary>
        public OpResult CutMarker(string kind, string targetId = null, ExecuteOptions o = default)
        {
            var k = CutShapeInfo.KindOf(kind);
            if (k == CutKind.None) return OpResult.BadArgs("kind", "plane, box, tear or hole", "\"" + kind + "\" is not a marker kind");
            Vector3 at;
            rt.ApplyNow();
            if (targetId != null)
            {
                var t = Find(targetId, "target", out var fail);
                if (t == null) return fail;
                var holder = rt.HolderOf(t.Id);
                if (!holder) return OpResult.Unavailable("node " + t.Id + " has no place in the scene", "target", "NotRealized");
                at = holder.position;
                bool any = false;
                var b = new Bounds();
                foreach (var r0 in VisibilityApplier.OwnRenderers(holder))
                {
                    if (!r0) continue;
                    if (!any) { b = r0.bounds; any = true; } else b.Encapsulate(r0.bounds);
                }
                if (any) at = b.center;
            }
            else
            {
                var cur = Model.Settings.Camera.Current;
                at = cur != null ? UnityObjects.V(cur.Pivot) : SceneViewCamera.DefaultPivot;
            }
            return CutAddMarker(k, at, Quaternion.identity, Vector3.one, o);
        }

        /// <summary>A marker of <paramref name="k"/> at a world pose, in one entry «Add item», selected.</summary>
        OpResult CutAddMarker(CutKind k, Vector3 pos, Quaternion rot, Vector3 scale, ExecuteOptions o)
        {
            var row = new StudioItem
            {
                Key = new ContentId(AppItemRows.CutGuid, (int)k), Group = AppItemRows.Group, Category = AppItemRows.MarkersCategory,
                Name = "", BundlePath = "", FileName = "", Manifest = "",
            };
            var gen = Services?.Db?.Generated;
            if (gen != null && gen.TryGetRow(AppItemRows.Group, row.Key, out var current) && current != null) row = current;
            var st = ItemDefaults.FromPrefab(null, row);
            string made = null;
            var res = BakeEntry(Labels.AddItem, o, step =>
            {
                made = BakeAddNode(st, null, pos, rot, scale, step, out var fail);
                return made == null ? fail : OpResult.Success();
            });
            if (!res.Ok) return res;
            Session.Selection.Set(new[] { made });
            res.Value = made;
            res.NewIds = new[] { made };
            return res;
        }

        /// <summary>
        /// A marker where a piece's last step is (its shape, place, turn and size in the world as the piece stands now), in one entry «Add item»,
        /// selected: «Edit cut» puts it so that moving it edits that step. Value = its id.
        /// </summary>
        public OpResult CutMarkerAtLastStep(string pieceId, ExecuteOptions o = default)
        {
            var n = CutPieceNode(pieceId, "key", out var fail);
            if (n == null) return fail;
            var st = (ItemNodeState)n.State;
            if (st.Cut.Steps.Count == 0) return OpResult.BadArgs("key", null, BakeFindings.CutBadStep + ": the piece's procedure has no step");
            var last = st.Cut.Steps[st.Cut.Steps.Count - 1];
            var k = CutShapeInfo.KindOf(last.Shape);
            if (k == CutKind.None) return OpResult.BadArgs("key", null, BakeFindings.CutBadStep + ": the last step's shape \"" + last.Shape + "\" is not known");
            rt.ApplyNow();
            var holder = rt.HolderOf(n.Id);
            if (!holder) return OpResult.Unavailable("piece " + n.Id + " has no place in the scene", "key", "NotRealized");
            var own = BakeBlobSet.Gather(st.Bake, BakeRes, OpenBakeStore(), new FindingList(), n.Id);
            var root = holder.localToWorldMatrix * CutMarkers.Matrix(CutPieces.SourceRootLocal(own.Status == BakeBlobSet.Result.Ok ? own.Main : null));
            var b = CutShapeInfo.BaseSize(k);
            var scale = new Vector3(b.X != 0 ? (float)(last.Size.X / b.X) : 1f, b.Y != 0 ? (float)(last.Size.Y / b.Y) : 1f, b.Z != 0 ? (float)(last.Size.Z / b.Z) : 1f);
            var world = root * Matrix4x4.TRS(UnityObjects.V(last.Pos), UnityObjects.Q(last.Rot).normalized, scale);
            return CutAddMarker(k, world.GetColumn(3), world.rotation, world.lossyScale, o);
        }

        // ------------------------------------------------------------ cuts

        /// <summary>The kind of a cut: a marker's or a given shape's; the step made in the source's root space.</summary>
        CutStep CutStepOf(string markerId, CutShapeGiven shape, Matrix4x4 sourceRootWorld, string keep, CutRagParams ragged, bool clothesOnly, out CutKind kind, out OpResult fail)
        {
            kind = CutKind.None;
            fail = null;
            if (markerId != null)
            {
                var mk = Find(markerId, "marker", out fail);
                if (mk == null) return null;
                kind = mk.State is ItemNodeState ms ? CutMarkers.KindOf(ms) : CutKind.None;
                if (kind == CutKind.None) { fail = BakeWrongKind(mk, "marker", "a cut marker"); return null; }
                var holder = rt.HolderOf(mk.Id);
                if (!holder) { fail = OpResult.Unavailable("marker " + mk.Id + " has no place in the scene", "marker", "NotRealized"); return null; }
                if (keep == null) keep = CutShapeInfo.Keeps(kind, null)[0];
                return CutMarkers.StepOf(holder.localToWorldMatrix, sourceRootWorld, kind, keep, ragged, clothesOnly);
            }
            kind = CutShapeInfo.KindOf(shape?.Kind);
            if (kind == CutKind.None) { fail = OpResult.BadArgs("shape.kind", "plane, box, tear or hole"); return null; }
            if (keep == null) keep = CutShapeInfo.Keeps(kind, null)[0];
            var b = CutShapeInfo.BaseSize(kind);
            var size = shape.Size ?? new Vector3((float)b.X, (float)b.Y, (float)b.Z);
            return CutMarkers.StepOf(shape.Pos, shape.Rot, size, sourceRootWorld, kind, keep, ragged, clothesOnly);
        }

        /// <summary>The marker's kind before the source is made (so a wrong marker or mode costs no capture).</summary>
        CutKind CutKindFirst(string markerId, CutShapeGiven shape, out OpResult fail)
        {
            fail = null;
            if ((markerId == null) == (shape == null)) { fail = OpResult.BadArgs("marker", "a marker or a shape", "a cut takes one of marker and shape"); return CutKind.None; }
            if (markerId != null)
            {
                var mk = Find(markerId, "marker", out fail);
                if (mk == null) return CutKind.None;
                var k = mk.State is ItemNodeState ms ? CutMarkers.KindOf(ms) : CutKind.None;
                if (k == CutKind.None) fail = BakeWrongKind(mk, "marker", "a cut marker");
                return k;
            }
            var kind = CutShapeInfo.KindOf(shape.Kind);
            if (kind == CutKind.None) fail = OpResult.BadArgs("shape.kind", "plane, box, tear or hole", "\"" + shape.Kind + "\" is not a shape");
            return kind;
        }

        static OpResult CutModeRefused(CutKind kind, string mode) =>
            OpResult.BadArgs("mode", string.Join(", ", CutShapeInfo.Modes(kind)), "\"" + mode + "\" is not a mode of a " + CutShapeInfo.Name(kind));

        /// <summary>The pose of a bake item's FK as a cut takes it: its values when its FK is on, else none.</summary>
        static SortedDictionary<string, Vec3> CutPoseOf(ItemNodeState s)
        {
            if (s?.Fk == null || !s.Fk.Enabled || s.Fk.Bones.Count == 0) return null;
            return new SortedDictionary<string, Vec3>(s.Fk.Bones, StringComparer.Ordinal);
        }

        /// <summary>
        /// The pose a re-cut of a piece takes: the pose its last step was decided at (the piece's nodes hold it), with the piece's own FK values over
        /// it when its FK is on (spec 8.2 "Per-step pose").
        /// </summary>
        static SortedDictionary<string, Vec3> CutPoseOfPiece(ItemNodeState s, BakeMain pieceMain) =>
            CutPieces.ReCutPose(s.Cut, s.Fk != null && s.Fk.Enabled, s.Fk?.Bones);

        /// <summary>The FK a piece takes: on with the pose's values of its own FK bones when the source showed a pose; null otherwise.</summary>
        static ItemFk CutFkOf(IDictionary<string, Vec3> pose, BakeMain pieceMain)
        {
            if (pose == null || pieceMain == null) return null;
            var fk = new ItemFk { Enabled = true };
            foreach (var bone in pieceMain.Fk) if (pose.TryGetValue(bone, out var v)) fk.Bones[bone] = v;
            return fk.Bones.Count == 0 ? null : fk;
        }

        /// <summary>
        /// A cut made ready (no scene change): the target turned into its source bake (a character or a plain item captured as drawn, a bake item at
        /// its FK pose, a piece's own source with the new step appended) and the procedure. Value = a <see cref="CutPlan"/>; the caller commits it with
        /// <see cref="CutCommit"/> or disposes it.
        /// </summary>
        public OpResult CutPrepare(CutMakeRequest req)
        {
            if (req == null) return OpResult.BadArgs("key", "a target");
            var n = Find(req.Target, "key", out var fail);
            if (n == null) return fail;
            var ist = n.State as ItemNodeState;
            if (n.Kind != NodeKind.Chara && (ist == null || CutMarkers.IsMarker(ist))) return BakeWrongKind(n, "key", "a character or an item");
            var kind = CutKindFirst(req.Marker, req.Shape, out fail);
            if (kind == CutKind.None) return fail;
            if (req.Mode != null && Array.IndexOf(CutShapeInfo.Modes(kind), req.Mode) < 0) return CutModeRefused(kind, req.Mode);
            var keeps = CutShapeInfo.Keeps(kind, req.Mode);
            var section = req.Section ?? new CutSection();
            rt.ApplyNow();
            var plan = new CutPlan { TargetId = n.Id, TargetName = n.Label, Name = req.Name, Hide = req.Hide, Keeps = keeps };
            var res = BakeRes;
            var store = OpenBakeStore();
            ItemBake sourceBlock = null;
            SortedDictionary<string, Vec3> pose = null;
            var findings = new FindingList();
            try
            {
                if (n.Kind == NodeKind.Chara || ist.Bake == null)
                {
                    // a character or a plain item: a bake of it as drawn, in the scene only
                    BakeJob job;
                    if (n.Kind == NodeKind.Chara)
                    {
                        var c = BakeBuiltChara(n, "key", out fail);
                        if (c == null) return fail;
                        if (!BakeHasDevice) return BakeNoDevice("key");
                        job = BakeJob.OfChara(c, new BakeOptions());
                    }
                    else
                    {
                        var it = rt.FindItem(n.Id);
                        if (it == null || it.State != RealizationState.Built || !it.Instance)
                            return OpResult.Unavailable("item " + n.Id + " is not built (" + (it?.State.ToString() ?? "NotRealized") + ")", "key", it?.State.ToString() ?? "NotRealized");
                        if (!BakeHasDevice) return BakeNoDevice("key");
                        job = BakeJob.OfItem(it);
                    }
                    plan.Job = job;
                    if (job.Capture?.Bake == null || job.Capture.Bake.Renderers.Count == 0) return BakeEmpty("key", "nothing visible to cut");
                    job.Finish();
                    var j = job;
                    plan.SourceMain = job.Main;
                    plan.BlobOf = sha => sha == j.MainSha ? j.MainBlob : j.Blobs.TryGetValue(sha, out var b) ? b : null;
                    sourceBlock = job.Block(null);
                    plan.SourceRootWorld = Matrix4x4.TRS(job.Capture.Position, job.Capture.Rotation, job.Capture.Scale);
                }
                else
                {
                    var holder = rt.HolderOf(n.Id);
                    if (!holder) return OpResult.Unavailable("item " + n.Id + " has no place in the scene", "key", "NotRealized");
                    if (ist.Cut == null)
                    {
                        // a bake item: its bake at its FK pose
                        var set = BakeBlobSet.Gather(ist.Bake, res, store, findings, n.Id);
                        if (set.Status != BakeBlobSet.Result.Ok) return OpResult.Unavailable(BakeFindings.Missing + ": the item's bake cannot be had", "key", BakeFindings.Missing);
                        plan.SourceMain = set.Main;
                        plan.BlobOf = set.Get;
                        sourceBlock = ist.Bake;
                        pose = CutPoseOf(ist);
                        plan.SourceRootWorld = holder.localToWorldMatrix;
                    }
                    else
                    {
                        // a piece: its own source, the new step appended
                        var src = BakeBlobSet.Gather(ist.Cut.Source, res, store, findings, n.Id);
                        var own = BakeBlobSet.Gather(ist.Bake, res, store, findings, n.Id);
                        if (src.Status != BakeBlobSet.Result.Ok || own.Status != BakeBlobSet.Result.Ok)
                            return OpResult.Unavailable(BakeFindings.Missing + ": the piece's source or result cannot be had", "key", BakeFindings.Missing);
                        plan.SourceMain = src.Main;
                        plan.BlobOf = src.Get;
                        pose = CutPoseOfPiece(ist, own.Main);
                        plan.SourceRootWorld = holder.localToWorldMatrix * CutMarkers.Matrix(CutPieces.SourceRootLocal(own.Main));
                    }
                }
                var step = CutStepOf(req.Marker, req.Shape, plan.SourceRootWorld, keeps[0], req.Ragged, req.ClothesOnly, out _, out fail);
                if (step == null) return fail;
                plan.Block = ist?.Cut != null ? CutPieces.Chain(ist.Cut, step, pose, req.Section) : CutPieces.NewBlock(CutSource.Of(sourceBlock), pose, new[] { step }, section.Color, section.OwnInside);
                if (plan.Block.Steps.Count > BakeLimits.MaxSteps)
                    return OpResult.BadArgs("key", null, BakeFindings.CutLimit + ": " + plan.Block.Steps.Count + " steps are over the limit of " + BakeLimits.MaxSteps);
                plan.Ids = new string[keeps.Length];
                for (int i = 0; i < keeps.Length; i++) plan.Ids[i] = JsonText.NewId();
                plan.Fk = pose != null ? new ItemFk { Enabled = true, Bones = new SortedDictionary<string, Vec3>(pose, StringComparer.Ordinal) } : null;
                var r = OpResult.NothingToDo();
                foreach (var f in findings) if (f.Severity != FindingSeverity.Info) r.Warnings.Add(f.Code + ": " + f.Message);
                r.Value = plan;
                plan = null;
                return r;
            }
            finally
            {
                plan?.Dispose();
            }
        }

        static OpResult CutRunFailed(CutOutcome o)
        {
            if (o == null) return OpResult.Fail(CommandCodes.Internal, "the cut did not run");
            if (o.Error == BakeFindings.CutBadStep) return OpResult.BadArgs("key", null, BakeFindings.CutBadStep + ": " + o.ErrorText);
            return OpResult.Unavailable(o.Error + ": " + o.ErrorText, "key", o.Error);
        }

        /// <summary>
        /// Commits a prepared cut (label «Cut», one entry): runs what is left of it, adds a piece node per piece that holds something (its bake block
        /// the result, its cut block the procedure) at the source's place, its blobs and the source's into the scene's resources, and hides the target
        /// with Hide. Value = a <see cref="CutMakeResult"/>; NewIds = the pieces. The plan is disposed.
        /// </summary>
        public OpResult CutCommit(CutPlan plan, ExecuteOptions o = default)
        {
            if (plan == null) return OpResult.BadArgs("key", "a cut", "no cut to commit");
            try
            {
                plan.Finish();
                var oc = plan.Outcome;
                if (oc == null || oc.Error != null) return CutRunFailed(oc);
                var pieces = new List<CutPieceResult>();
                foreach (var p in oc.Pieces) if (!p.Empty) pieces.Add(p);
                if (pieces.Count == 0) return BakeEmpty("key", "the cut leaves nothing");
                long bytes = 0;
                foreach (var p in pieces) foreach (var kv in p.NewBlobs) bytes += kv.Value.LongLength;
                if (plan.Job != null) bytes += plan.Job.Bytes;
                if (BakeLimits.WouldExceed(BakeRes, bytes))
                    return OpResult.BadArgs("key", null, BakeFindings.Limit + ": " + bytes + " bytes of pieces would take the scene's resources over " + BakeLimits.MaxSceneBytes + " bytes");
                var made = new CutMakeResult();
                bool two = pieces.Count > 1;
                var now = DateTime.Now;
                var r = BakeEntry(Labels.CutMake, o, step =>
                {
                    plan.Job?.AddTo(BakeRes);
                    foreach (var p in pieces)
                    {
                        foreach (var kv in p.NewBlobs) BakeRes.AddBlob(kv.Value);
                        var st = BakeItemState(p.Bake, null);
                        st.Cut = p.Cut;
                        var fk = CutFkOf(plan.Fk?.Bones, p.Main);
                        if (fk != null) st.Fk = fk;
                        CutMarkers.PiecePose(plan.SourceRootWorld, p, out var pos, out var rot, out var scale);
                        string name = CutPieces.PieceName(plan.Name, plan.TargetName, p.Side, two, now);
                        string id = BakeAddNode(st, name, pos, rot, scale, step, out var fail);
                        if (id == null) return fail;
                        made.Pieces.Add(id);
                        made.Sides.Add(p.Side);
                        made.CutRenderers.Add(new List<int>(p.CutRenderers));
                    }
                    if (plan.Hide)
                    {
                        var s = Session.Set(plan.TargetId, "visible", false, step);
                        if (!s.Ok) return OpResult.From(s, "hide");
                    }
                    return OpResult.Success();
                });
                foreach (var f in oc.Findings) r.Warnings.Add(f + ": " + CutFindingText(f));
                if (!r.Ok) return r;
                made.Ms = plan.Clock.Elapsed.TotalMilliseconds;
                r.Value = made;
                r.NewIds = made.Pieces;
                return r;
            }
            finally
            {
                plan.Dispose();
            }
        }

        static string CutFindingText(string code)
        {
            switch (code)
            {
                case BakeFindings.CutRange: return "a ragged value out of its range was clamped where it acted";
                case BakeFindings.CutLimit: return "steps over the limit were not applied";
                default: return "met while cutting";
            }
        }

        /// <summary>A cut in one go: <see cref="CutPrepare"/>, the whole run, <see cref="CutCommit"/>.</summary>
        public OpResult CutMake(CutMakeRequest req, ExecuteOptions o = default)
        {
            var p = CutPrepare(req);
            if (!p.Ok) return p;
            var plan = (CutPlan)p.Value;
            var r = CutCommit(plan, o);
            if (r.Ok) r.Warnings.InsertRange(0, p.Warnings);
            return r;
        }

        /// <summary>
        /// An edit of a piece's cut made ready: a copy of its procedure with the last step's shape (a marker's or values), mode, ragged edge and
        /// «Clothes only» and the section changed as asked, or none with Recut. Value = a <see cref="CutPlan"/> (one piece, the piece's own identity).
        /// </summary>
        public OpResult CutSetPrepare(string nodeId, CutSetRequest req)
        {
            var n = CutPieceNode(nodeId, "key", out var fail);
            if (n == null) return fail;
            req = req ?? new CutSetRequest();
            var st = (ItemNodeState)n.State;
            bool any = req.Marker != null || req.Shape != null || req.Mode != null || req.Ragged != null || req.ClearRagged || req.ClothesOnly != null || req.Section != null;
            if (!any && !req.Recut) return OpResult.BadArgs("recut", "a change or recut", "nothing to set: give marker, shape, mode, ragged, clothesOnly, section or recut");
            if (req.Marker != null && req.Shape != null) return OpResult.BadArgs("marker", "a marker or a shape", "marker and shape cannot stand together");
            var cut = st.Cut.Clone();
            if (cut.Steps.Count == 0) return OpResult.BadArgs("key", null, BakeFindings.CutBadStep + ": the piece's procedure has no step");
            var last = cut.Steps[cut.Steps.Count - 1];
            var kind = CutShapeInfo.KindOf(last.Shape);
            if (req.Marker != null || req.Shape != null)
            {
                kind = CutKindFirst(req.Marker, req.Shape, out fail);
                if (kind == CutKind.None) return fail;
            }
            if (kind == CutKind.None) return OpResult.BadArgs("key", null, BakeFindings.CutBadStep + ": the last step's shape \"" + last.Shape + "\" is not known");
            if (req.Mode != null && Array.IndexOf(CutShapeInfo.Modes(kind), req.Mode) < 0) return CutModeRefused(kind, req.Mode);
            string keep = last.Keep;
            if (req.Mode != null)
            {
                var keeps = CutShapeInfo.Keeps(kind, req.Mode);
                if (keeps.Length == 1 || Array.IndexOf(keeps, keep) < 0) keep = keeps[0];
            }
            else if (!CutShapeInfo.KeepValid(kind, keep)) keep = CutShapeInfo.Keeps(kind, null)[0];
            rt.ApplyNow();
            var res = BakeRes;
            var store = OpenBakeStore();
            var findings = new FindingList();
            var src = BakeBlobSet.Gather(cut.Source, res, store, findings, n.Id);
            if (src.Status != BakeBlobSet.Result.Ok) return OpResult.Unavailable(BakeFindings.Missing + ": the piece's source cannot be had", "key", BakeFindings.Missing);
            var own = BakeBlobSet.Gather(st.Bake, res, store, findings, n.Id);
            var holder = rt.HolderOf(n.Id);
            var plan = new CutPlan { TargetId = n.Id, PieceId = st.Bake.Id, IsEdit = true, SourceMain = src.Main, BlobOf = src.Get, Keeps = null, Ids = new[] { st.Bake.Id } };
            plan.OldMainSha = BakeBlob.ShaOf(st.Bake.Main);
            plan.OldCutJson = st.Cut.ToJson();
            if (holder) plan.SourceRootWorld = holder.localToWorldMatrix * CutMarkers.Matrix(CutPieces.SourceRootLocal(own.Status == BakeBlobSet.Result.Ok ? own.Main : null));
            if (req.Marker != null || req.Shape != null)
            {
                if (!holder) return OpResult.Unavailable("piece " + n.Id + " has no place in the scene", "key", "NotRealized");
                var step = CutStepOf(req.Marker, req.Shape, plan.SourceRootWorld, keep, null, last.ClothesOnly, out _, out fail);
                if (step == null) return fail;
                last.Shape = step.Shape;
                last.Pos = step.Pos;
                last.Rot = step.Rot;
                last.Size = step.Size;
            }
            last.Keep = keep;
            if (req.ClearRagged) last.Ragged = null;
            else if (req.Ragged != null) last.Ragged = req.Ragged.Amp > 0 ? new CutRagged { Amp = (float)req.Ragged.Amp, Tooth = (float)req.Ragged.Tooth, Random = (float)req.Ragged.Random, Seed = req.Ragged.Seed } : null;
            if (req.ClothesOnly != null) last.ClothesOnly = req.ClothesOnly.Value;
            if (req.Section != null) cut.Section = new CutSection { Color = req.Section.Color, OwnInside = req.Section.OwnInside };
            plan.Block = cut;
            var r = OpResult.NothingToDo();
            foreach (var f in findings) if (f.Severity != FindingSeverity.Info) r.Warnings.Add(f.Code + ": " + f.Message);
            r.Value = plan;
            return r;
        }

        /// <summary>
        /// Commits a prepared edit (label «Edit cut», one entry): the piece's cut and bake blocks replaced by the new procedure and its result, its new
        /// blobs into the scene's resources, and the piece moved when its root moved with the step, so the source stays in place. Value = a
        /// <see cref="CutSetResult"/>.
        /// </summary>
        public OpResult CutSetCommit(CutPlan plan, ExecuteOptions o = default)
        {
            if (plan == null || !plan.IsEdit) return OpResult.BadArgs("key", "an edit of a cut", "no edit to commit");
            try
            {
                plan.Finish();
                var oc = plan.Outcome;
                if (oc == null || oc.Error != null) return CutRunFailed(oc);
                if (oc.Pieces.Count == 0 || oc.Pieces[0].Empty) return BakeEmpty("key", "the edit leaves nothing of the piece");
                var p = oc.Pieces[0];
                long bytes = 0;
                foreach (var kv in p.NewBlobs) if (!BakeRes.Contains(BakeBlob.ResName(kv.Key))) bytes += kv.Value.LongLength;
                if (BakeLimits.WouldExceed(BakeRes, bytes))
                    return OpResult.BadArgs("key", null, BakeFindings.Limit + ": the piece would take the scene's resources over " + BakeLimits.MaxSceneBytes + " bytes");
                var n = Model.Find(plan.TargetId);
                if (n == null) return OpResult.NotFound("no node \"" + plan.TargetId + "\" in the scene", "key");
                var st = (ItemNodeState)n.State;
                var block = p.Bake;
                block.No = st.Bake.No;
                bool changed = p.MainSha != plan.OldMainSha || p.Cut.ToJson() != plan.OldCutJson || st.Bake.ToJson() != block.ToJson();
                var res = new CutSetResult { Changed = changed };
                OpResult r = OpResult.NothingToDo();
                if (changed)
                {
                    foreach (var kv in p.NewBlobs) BakeRes.AddBlob(kv.Value);
                    var holder = rt.HolderOf(n.Id);
                    r = BakeEntry(Labels.CutEdit, o, step =>
                    {
                        var c = Session.Commands.Execute(new BakeEditCommand(n.Id, block, p.Cut, Labels.CutEdit), step);
                        if (!c.Ok) return OpResult.From(c, "key");
                        if (holder)
                        {
                            CutMarkers.PiecePose(plan.SourceRootWorld, p, out var pos, out var rot, out _);
                            if ((pos - holder.position).sqrMagnitude > 1e-12f || Quaternion.Angle(rot, holder.rotation) > 1e-4f)
                            {
                                var a = rt.AttachTransformFor(n.Parent?.Id, n.Point);
                                var lp = a ? a.InverseTransformPoint(pos) : pos;
                                var lq = (a ? Quaternion.Inverse(a.rotation) : Quaternion.identity) * rot;
                                var s1 = Session.Set(n.Id, "transform.pos", UnityObjects.V(lp), step);
                                if (!s1.Ok) return OpResult.From(s1, "key");
                                var s2 = Session.Set(n.Id, "transform.rot", UnityObjects.V(lq.eulerAngles), step);
                                if (!s2.Ok) return OpResult.From(s2, "key");
                            }
                        }
                        return OpResult.Success();
                    });
                    if (!r.Ok) return r;
                }
                foreach (var f in oc.Findings) r.Warnings.Add(f + ": " + CutFindingText(f));
                res.Ms = plan.Clock.Elapsed.TotalMilliseconds;
                r.Value = res;
                return r;
            }
            finally
            {
                plan.Dispose();
            }
        }

        /// <summary>An edit of a piece's cut in one go: <see cref="CutSetPrepare"/>, the whole run, <see cref="CutSetCommit"/>.</summary>
        public OpResult CutSet(string nodeId, CutSetRequest req, ExecuteOptions o = default)
        {
            var p = CutSetPrepare(nodeId, req);
            if (!p.Ok) return p;
            var r = CutSetCommit((CutPlan)p.Value, o);
            if (r.Ok) r.Warnings.InsertRange(0, p.Warnings);
            return r;
        }

        /// <summary>A piece's procedure as stored (Value = a <see cref="CutStatusInfo"/>).</summary>
        public OpResult CutStatus(string nodeId)
        {
            var n = CutPieceNode(nodeId, "key", out var fail);
            if (n == null) return fail;
            var cut = ((ItemNodeState)n.State).Cut.Clone();
            var info = new CutStatusInfo { Source = cut.Source, Pose = cut.Pose, Section = cut.Section };
            info.Steps.AddRange(cut.Steps);
            foreach (var f in rt.Findings)
                if (f.NodeId == n.Id && f.Code == BakeFindings.CutRecomputed) { info.Recomputed = true; break; }
            var r = OpResult.NothingToDo();
            r.Value = info;
            return r;
        }
    }
}
