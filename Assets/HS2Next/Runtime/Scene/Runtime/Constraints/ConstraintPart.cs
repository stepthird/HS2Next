// The scene's constraints at run time: a scene part that compiles the model's list into a plan and installs the motion pass. A compile
// resolves both ends of every entry, finds true loops, sorts, takes or gives up the override slots and sets up the driven bones. It
// runs in the Apply after a change of the list, the tree or a node's content, so a rebuild is seen in the frame it happens. When only
// values changed (a weight above 0, an offset, a label) it copies them and does nothing else, so a drag costs no compile.
//   active   on, weight above 0, both ends resolved, refused by no rule. The pass exists while one entry is active.
//   states   off; sourceMissing and targetMissing (a node deleted, a bone a body lacks); pending (not built yet); cycle (it closes a loop:
//            the highest list index on the loop); protected (a bone the mosaic rule names); bad (an entry that cannot act); limit
//   findings once per entry and build: constraint.stale, constraint.cycle, constraint.mosaicProtected, constraint.badEntry
// Nothing is ever dropped: an entry keeps its place and acts again when what it lacks returns. With no list every call returns at once.
using System;
using System.Collections.Generic;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes.Constraints;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Routes;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The states of an entry, as <c>constraint.list</c> gives them.</summary>
    public static class ConstraintStates
    {
        public const string Active = "active", Off = "off", SourceMissing = "sourceMissing", TargetMissing = "targetMissing", Cycle = "cycle",
            Protected = "protected", Bad = "bad", Pending = "pending", Limit = "limit";
    }

    /// <summary>How one entry stands after the last compile.</summary>
    public sealed class ConstraintRow
    {
        public string Id;
        public int Index;
        /// <summary>One of <see cref="ConstraintStates"/>.</summary>
        public string State = ConstraintStates.Pending;
        /// <summary>It reads its source one frame behind.</summary>
        public bool Lag;
        /// <summary>What the state names: a node or an address, the rows of a loop, why an entry cannot act; else null.</summary>
        public string Detail;
        /// <summary>The target's character is the character the source moves with, so the source is read at the stage.</summary>
        public bool SameCharacter;
        internal ConstraintLive Live;
    }

    /// <summary>One step of the plan, for <c>constraint.status</c>.</summary>
    public readonly struct ConstraintOrderRow
    {
        /// <summary>A node id, or an entry id.</summary>
        public readonly string Unit;
        /// <summary>"chara", "item" or "constraint".</summary>
        public readonly string Kind;
        public ConstraintOrderRow(string unit, string kind) { Unit = unit; Kind = kind; }
    }

    public sealed class ConstraintPart : ISceneRuntimePart
    {
        sealed class ChannelRef
        {
            public ConstraintChannel Channel;
            public CharaRealization Chara;
            public bool Used, Wanted;
        }

        sealed class Candidate
        {
            public CharaRealization Chara;
            public ItemRealization Item;
            public int Root, Holder, HolderParent;
        }

        SceneRuntime rt;
        ConstraintEvaluator evaluator;
        bool listDirty, structureDirty;
        ConstraintEntry[] entries;
        ConstraintRow[] rows = Array.Empty<ConstraintRow>();
        readonly Dictionary<string, ConstraintRow> byId = new Dictionary<string, ConstraintRow>(StringComparer.Ordinal);
        readonly Dictionary<string, ChannelRef> channels = new Dictionary<string, ChannelRef>(StringComparer.Ordinal);
        readonly Dictionary<CharaRealization, ConstraintDrive> drives = new Dictionary<CharaRealization, ConstraintDrive>();
        readonly List<DrivenCharacter> boneOwners = new List<DrivenCharacter>();
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> staleNames = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<ConstraintOrderRow> order = new List<ConstraintOrderRow>();
        List<string> lagged = new List<string>();

        /// <summary>The pass of this part (its perf window, its walks).</summary>
        public ConstraintEvaluator Evaluator => evaluator;
        /// <summary>Changes with every compile that was more than a copy of values.</summary>
        public int Version { get; private set; }
        /// <summary>Compiles run (tests).</summary>
        public int Compiles { get; private set; }
        /// <summary>Entries active after the last compile.</summary>
        public int Active { get; private set; }
        /// <summary>True while the pass steps the scene's characters.</summary>
        public bool PassActive => rt?.Pass != null && ReferenceEquals(rt.Pass, evaluator);
        /// <summary>The steps of the plan in run order.</summary>
        public IReadOnlyList<ConstraintOrderRow> Order => order;
        /// <summary>The ids of the entries that lag one frame, in list order.</summary>
        public IReadOnlyList<string> Lagged => lagged;
        /// <summary>How each entry stands, in list order, after the last compile.</summary>
        public IReadOnlyList<ConstraintRow> Rows => rows;

        /// <summary>The row of an entry; before the first compile of a load every entry is <see cref="ConstraintStates.Pending"/>; null for an unknown id.</summary>
        public ConstraintRow RowOf(string id)
        {
            if (id == null) return null;
            if (structureDirty)
            {
                int i = rt.Model.Constraints?.IndexOf(id) ?? -1;
                if (i >= 0 && !byId.ContainsKey(id)) return new ConstraintRow { Id = id, Index = i };
            }
            return byId.TryGetValue(id, out var r) ? r : null;
        }

        /// <summary>True when a row's target takes effect now: every channel's slot wins and shows.</summary>
        public bool IsEffective(ConstraintRow row, out string reasonKey)
        {
            reasonKey = row != null && row.SameCharacter ? "constraints.sameCharaTip" : null;
            var c = row?.Live;
            if (c == null) return row != null && row.State == ConstraintStates.Active;
            var t = c.Target;
            bool ok = (!c.WritesPos || Shows(t.Pos)) && (!c.WritesRot || Shows(t.Rot)) && (!c.WritesScale || Shows(t.Scale));
            if (ok) return true;
            reasonKey = t.Kind == ConstraintTargetKind.Ik ? "constraints.ikOff" : "constraints.notShown";
            return false;
        }

        static bool Shows(ConstraintChannel ch) => ch == null || ch.Slot.IsReleased || !ch.Slot.HasValue || ch.Slot.IsEffective;

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            rt = r;
            evaluator = new ConstraintEvaluator(r);
            r.ContentChanged += OnContentChanged;
        }

        void OnContentChanged(NodeRealization r)
        {
            if (rt.Model.Constraints == null && rows.Length == 0) return;
            structureDirty = true;
            if (entries == null) return;
            foreach (var e in entries)
                if (e != null && (e.Source?.Node == r.NodeId || e.Target?.Node == r.NodeId)) Forget(e.Id);
        }

        public void OnChanged(SceneChangeBatch b)
        {
            if (rt.Model.Constraints == null && rows.Length == 0) return;
            foreach (var c in b.Changes)
                switch (c.Kind)
                {
                    case SceneChangeKind.NodeAdded:
                    case SceneChangeKind.NodeRemoved:
                    case SceneChangeKind.NodeMoved:
                        structureDirty = true;
                        break;
                    case SceneChangeKind.SceneChanged:
                        if (c.Path == ConstraintEditCommand.ChangePath) listDirty = true;
                        break;
                }
        }

        public void Apply()
        {
            if (!listDirty && !structureDirty) return;
            bool structure = structureDirty;
            listDirty = structureDirty = false;
            var list = rt.Model.Constraints;
            if (!structure && CopyValues(list)) return;
            Compile(list);
        }

        public void LateApply() { }

        public void Clear()
        {
            listDirty = structureDirty = false;
            rt.SetPass(null);
            evaluator.Install(Array.Empty<ConstraintRunStep>(), Array.Empty<ConstraintDrive>());
            foreach (var r in channels.Values) r.Channel.Slot.Release();
            channels.Clear();
            drives.Clear();
            boneOwners.Clear();
            reported.Clear();
            staleNames.Clear();
            entries = null;
            rows = Array.Empty<ConstraintRow>();
            byId.Clear();
            order = new List<ConstraintOrderRow>();
            lagged = new List<string>();
            Active = 0;
            Version++;
        }

        public void OnLoadedRealized()
        {
            if (rt.Model.Constraints != null) structureDirty = true;
        }

        // ------------------------------------------------------------ values only

        static bool Acts(ConstraintEntry e) => e.On && e.AppliedWeight > 0f;

        static bool SameSet(List<string> a, List<string> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Every entry's ends, components and activity equal to the plan: the values go in and nothing else happens.
        bool CopyValues(ConstraintList list)
        {
            if (list == null || entries == null || entries.Length != list.Count) return false;
            for (int i = 0; i < entries.Length; i++)
            {
                var a = entries[i];
                var b = list.Entries[i];
                if (ReferenceEquals(a, b)) continue;
                if (a == null || b == null || a.Id != b.Id || a.Source == null || a.Target == null || !a.Source.SameAs(b.Source) || !a.Target.SameAs(b.Target) ||
                    !SameSet(a.Components, b.Components) || Acts(a) != Acts(b)) return false;
            }
            for (int i = 0; i < entries.Length; i++)
            {
                var e = list.Entries[i];
                entries[i] = e;
                var c = rows[i].Live;
                if (c == null) continue;
                c.Weight = e.AppliedWeight;
                c.Offset = ConstraintMath.ValueOf(e.Offset);
            }
            return true;
        }

        // ------------------------------------------------------------ findings

        void Forget(string id)
        {
            reported.Remove(ConstraintFindings.Stale + "|" + id);
            reported.Remove(ConstraintFindings.Cycle + "|" + id);
            reported.Remove(ConstraintFindings.MosaicProtected + "|" + id);
            reported.Remove(ConstraintFindings.BadEntry + "|" + id);
        }

        bool Once(string code, ConstraintEntry e) => reported.Add(code + "|" + e.Id);

        void ReportStale(ConstraintEntry e, ConstraintEnd end, string name)
        {
            if (!Once(ConstraintFindings.Stale, e)) return;
            if (!staleNames.TryGetValue(end.Node, out var names)) staleNames[end.Node] = names = new List<string>();
            if (!names.Contains(name)) names.Add(name);
            var f = rt.Findings.Add(FindingSeverity.Info, ConstraintFindings.Stale, end.Node, "");
            f.Message = "constraints name what this node lacks, kept and inactive: " + string.Join(", ", names.GetRange(0, Math.Min(8, names.Count))) + (names.Count > 8 ? ", ..." : "");
        }

        // ------------------------------------------------------------ the compile

        List<Candidate> Candidates(ConstraintTopology tree, Dictionary<NodeRealization, int> unitOf)
        {
            var list = new List<Candidate>();
            foreach (var n in rt.Model.PreOrder())
            {
                var r = rt.Find(n.Id);
                if (r == null || r.Node != n || !r.HasHolder || r.InGraveyard) continue;
                if (r is CharaRealization c)
                {
                    if (c.State != RealizationState.Built || !c.Motion || !c.Root) continue;
                    unitOf[c] = list.Count;
                    list.Add(new Candidate { Chara = c, Root = tree.Place(c.Root.transform), Holder = tree.Place(c.Holder), HolderParent = tree.Place(c.Holder.parent) });
                }
                else if (r is ItemRealization it && it.State == RealizationState.Built && it.Motion is ItemMotion && it.Instance)
                    list.Add(new Candidate { Item = it, Root = tree.Place(it.Instance.transform), Holder = tree.Place(it.Holder), HolderParent = tree.Place(it.Holder.parent) });
            }
            return list;
        }

        static void Link(ConstraintEntry e, int index, in ConstraintResolved s, in ConstraintResolved t, ConstraintTopology tree, Dictionary<NodeRealization, int> unitOf, out ConstraintLink link)
        {
            int source = tree.Place(s.Transform);
            switch (t.Kind)
            {
                case ConstraintTargetKind.Bone:
                    link = ConstraintLink.OfBone(index, source, tree.Place(t.Transform), unitOf[t.Chara]);
                    break;
                case ConstraintTargetKind.Ik:
                    link = ConstraintLink.OfIk(index, source, t.IkIndex, tree.Place(AdvancedPoseResolver.Resolve(t.Chara, ConstraintGraph.WrittenBone(t.IkIndex))), unitOf[t.Chara]);
                    break;
                default:
                    link = ConstraintLink.OfNode(index, source, tree.Place(t.Transform), tree.Place(t.Transform.parent));
                    break;
            }
        }

        // the state of an entry; true when it may be active
        bool Judge(ConstraintEntry e, ConstraintRow row, out ConstraintResolved s, out ConstraintResolved t, out ConstraintParts parts)
        {
            s = t = default;
            parts = ConstraintParts.None;
            if (ConstraintList.BeyondLimit(row.Index)) { row.State = ConstraintStates.Limit; return false; }
            string bad = e.BadReason();
            if (bad != null)
            {
                row.State = ConstraintStates.Bad;
                row.Detail = bad;
                return false;
            }
            parts = ConstraintMath.PartsOf(e.Components);
            s = ConstraintResolver.Source(rt, e.Source);
            t = ConstraintResolver.Target(rt, e.Target, parts);
            if (s.State == ConstraintEndState.Bad || t.State == ConstraintEndState.Bad)
            {
                row.State = ConstraintStates.Bad;
                row.Detail = s.State == ConstraintEndState.Bad ? s.Detail : t.Detail;
                if (Once(ConstraintFindings.BadEntry, e)) rt.Findings.Add(FindingSeverity.Warning, ConstraintFindings.BadEntry, e.Target?.Node, "constraint " + e.Id + ": " + row.Detail + "; kept, inactive");
                return false;
            }
            if (s.State == ConstraintEndState.Pending || t.State == ConstraintEndState.Pending)
            {
                row.State = ConstraintStates.Pending;
                return false;
            }
            if (t.State == ConstraintEndState.Protected)
            {
                row.State = ConstraintStates.Protected;
                row.Detail = t.Detail;
                if (Once(ConstraintFindings.MosaicProtected, e)) rt.Findings.Add(FindingSeverity.Warning, ConstraintFindings.MosaicProtected, e.Target.Node, "constraint " + e.Id + ": " + AdvancedPoseGuard.Refusal + "; kept, not applied");
                return false;
            }
            if (s.State == ConstraintEndState.Missing)
            {
                row.State = ConstraintStates.SourceMissing;
                row.Detail = s.Detail;
                if (e.Source.Bone != null && rt.Model.Find(e.Source.Node) != null) ReportStale(e, e.Source, s.Detail);
                return false;
            }
            if (t.State == ConstraintEndState.Missing)
            {
                row.State = ConstraintStates.TargetMissing;
                row.Detail = t.Detail;
                if (!e.Target.IsNode && rt.Model.Find(e.Target.Node) != null) ReportStale(e, e.Target, t.Detail);
                return false;
            }
            if (!Acts(e))
            {
                row.State = ConstraintStates.Off;
                return false;
            }
            row.State = ConstraintStates.Active;
            return true;
        }

        string LoopText(ConstraintCycle c, List<int> entryOf)
        {
            var names = new List<string>();
            for (int i = 0; i < c.Rows.Length && i < 8; i++) names.Add(ConstraintResolver.RowText(rt, entries[entryOf[c.Rows[i]]]));
            names.Add(names[0]);
            return string.Join(" -> ", names);
        }

        void Compile(ConstraintList list)
        {
            Compiles++;
            Version++;
            int n = list?.Count ?? 0;
            var tree = new ConstraintTopology();
            var unitOf = new Dictionary<NodeRealization, int>();
            var candidates = Candidates(tree, unitOf);
            var newRows = new ConstraintRow[n];
            entries = new ConstraintEntry[n];
            var links = new List<ConstraintLink>();
            var entryOf = new List<int>();
            var resolved = new Dictionary<int, (ConstraintResolved s, ConstraintResolved t, ConstraintParts parts)>();
            byId.Clear();
            foreach (var r in channels.Values) r.Used = r.Wanted = false;
            for (int i = 0; i < n; i++)
            {
                var e = list.Entries[i];
                var row = new ConstraintRow { Id = e?.Id, Index = i };
                newRows[i] = row;
                entries[i] = e;
                if (e == null) { row.State = ConstraintStates.Bad; continue; }
                if (e.Id != null) byId[e.Id] = row;
                MarkWanted(e);
                if (!Judge(e, row, out var s, out var t, out var parts)) continue;
                Link(e, i, s, t, tree, unitOf, out var link);
                links.Add(link);
                entryOf.Add(i);
                resolved[i] = (s, t, parts);
            }
            rows = newRows;

            // true loops: the highest list index of each is suspended, kept and saved
            var cycles = ConstraintGraph.Suspend(links, tree);
            var gone = new HashSet<int>();
            foreach (var c in cycles)
            {
                gone.Add(c.Position);
                var e = entries[entryOf[c.Position]];
                var row = rows[entryOf[c.Position]];
                row.State = ConstraintStates.Cycle;
                row.Detail = LoopText(c, entryOf);
                if (Once(ConstraintFindings.Cycle, e)) rt.Findings.Add(FindingSeverity.Warning, ConstraintFindings.Cycle, e.Target.Node, "constraint " + e.Id + " closes a loop, suspended: " + row.Detail);
            }
            var live = new List<ConstraintLink>();
            var liveEntry = new List<int>();
            for (int i = 0; i < links.Count; i++)
                if (!gone.Contains(i)) { live.Add(links[i]); liveEntry.Add(entryOf[i]); }

            // units: every built character, and the items a constraint reads through or moves
            var units = new List<ConstraintUnit>();
            var kept = new List<Candidate>();
            var remap = new int[candidates.Count];
            for (int u = 0; u < candidates.Count; u++)
            {
                var c = candidates[u];
                remap[u] = -1;
                if (c.Item != null && !Reached(c, live, tree)) continue;
                remap[u] = units.Count;
                units.Add(new ConstraintUnit(c.Root, c.HolderParent));
                kept.Add(c);
            }
            for (int i = 0; i < live.Count; i++)
            {
                var l = live[i];
                if (l.Unit >= 0) { l.Unit = remap[l.Unit]; live[i] = l; }
            }
            var plan = ConstraintPlan.Build(live, units, tree);

            Active = live.Count;
            Install(plan, live, liveEntry, kept, resolved, tree);
            rt.SetPass(Active > 0 ? evaluator : null);
        }

        static bool Reached(Candidate c, List<ConstraintLink> links, ConstraintTopology tree)
        {
            foreach (var l in links)
            {
                if (tree.Under(l.Source, c.Root)) return true;
                if (l.Kind != ConstraintTargetKind.Node) continue;
                if (tree.Under(l.Frame, c.Root) || tree.Under(c.Holder, l.Target)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ channels

        static string TextOf(ConstraintEnd end, string member) =>
            end.Bone != null ? OverridePath.BoneText(end.Bone, member) : end.Ik != null ? OverridePath.IkText(end.Ik, member) : "transform." + member;

        void MarkWanted(ConstraintEntry e)
        {
            if (e.Target?.Node == null || e.BadReason() != null) return;
            foreach (string member in new[] { "pos", "rot", "scale" })
                if (channels.TryGetValue(e.Target.Node + "\n" + TextOf(e.Target, member), out var r)) r.Wanted = true;
        }

        ConstraintChannel Channel(string nodeId, string text, bool rotation, CharaRealization chara)
        {
            string key = nodeId + "\n" + text;
            if (channels.TryGetValue(key, out var r) && !r.Channel.Slot.IsReleased)
            {
                r.Used = true;
                r.Chara = chara;
                return r.Channel;
            }
            var slot = rt.Overrides.Acquire(OverrideLayer.ConstraintsOwner, nodeId, text, OverrideLayer.ConstraintsPriority);
            if (slot == null) return null;
            r = new ChannelRef { Channel = new ConstraintChannel(slot, rotation), Chara = chara, Used = true };
            channels[key] = r;
            return r.Channel;
        }

        ConstraintDrive DriveOf(CharaRealization c)
        {
            if (c == null) return null;
            if (!drives.TryGetValue(c, out var d)) drives[c] = d = new ConstraintDrive { Chara = c, Settles = c.Motion ? c.Motion.Settles : 0 };
            return d;
        }

        void Install(ConstraintPlan plan, List<ConstraintLink> links, List<int> entryAt, List<Candidate> units,
            Dictionary<int, (ConstraintResolved s, ConstraintResolved t, ConstraintParts parts)> resolved, ConstraintTopology tree)
        {
            var targets = new Dictionary<string, ConstraintTarget>(StringComparer.Ordinal);
            var lives = new ConstraintLive[links.Count];
            var lastOn = new Dictionary<ConstraintChannel, ConstraintLive>();
            var usedDrives = new HashSet<ConstraintDrive>();
            var unitList = new List<ConstraintUnit>(units.Count);
            foreach (var u in units) unitList.Add(new ConstraintUnit(u.Root, u.HolderParent));
            lagged = new List<string>();
            for (int i = 0; i < links.Count; i++)
            {
                int index = entryAt[i];
                var e = entries[index];
                var (s, t, parts) = resolved[index];
                string nodeId = e.Target.Node;
                string key = t.Kind + "\n" + nodeId + "\n" + (e.Target.Bone ?? e.Target.Ik ?? "");
                if (!targets.TryGetValue(key, out var target))
                {
                    target = new ConstraintTarget { Kind = t.Kind, Node = t.Node, Transform = t.Transform, IkIndex = t.IkIndex };
                    switch (t.Kind)
                    {
                        case ConstraintTargetKind.Bone:
                            target.Frame = t.Transform.parent;
                            target.Bone = DrivenPoseDriver.Bind(t.Chara, e.Target.Bone);
                            target.Drive = DriveOf(t.Chara);
                            break;
                        case ConstraintTargetKind.Ik:
                            target.Frame = t.Chara.Root.transform;
                            target.Ik = t.Ik.Stage;
                            target.Drive = DriveOf(t.Chara);
                            break;
                        default:
                            target.Frame = t.Transform.parent;
                            target.Drive = DriveOf(t.Node as CharaRealization);
                            break;
                    }
                    targets[key] = target;
                }
                var c = new ConstraintLive
                {
                    Index = index,
                    Parts = parts,
                    Offset = ConstraintMath.ValueOf(e.Offset),
                    Weight = e.AppliedWeight,
                    Source = s.Transform,
                    Target = target,
                };
                bool rotates = t.Kind != ConstraintTargetKind.Ik || ConstraintEntry.IkTakesRotation(e.Target.Ik);
                c.WritesPos = (parts & (ConstraintParts.Position | ConstraintParts.Parent)) != 0;
                c.WritesRot = rotates && (parts & (ConstraintParts.Rotation | ConstraintParts.LookAt | ConstraintParts.Parent)) != 0;
                c.WritesScale = (parts & ConstraintParts.Scale) != 0;
                if (c.WritesPos && target.Pos == null) target.Pos = Channel(nodeId, TextOf(e.Target, "pos"), false, t.Chara);
                if (c.WritesRot && target.Rot == null) target.Rot = Channel(nodeId, TextOf(e.Target, "rot"), true, t.Chara);
                if (c.WritesScale && target.Scale == null) target.Scale = Channel(nodeId, TextOf(e.Target, "scale"), false, t.Chara);
                if (target.Drive != null) usedDrives.Add(target.Drive);
                lives[i] = c;
                var row = rows[index];
                row.Live = c;
                row.Lag = plan.Lag[i];
                if (plan.Lag[i]) lagged.Add(e.Id);
                row.SameCharacter = t.Kind == ConstraintTargetKind.Bone && ConstraintPlan.Owner(links[i].Source, unitList, tree) == links[i].Unit;
                if (c.WritesPos) lastOn[target.Pos] = c;
                if (c.WritesRot) lastOn[target.Rot] = c;
                if (c.WritesScale) lastOn[target.Scale] = c;
            }
            foreach (var kv in lastOn)
            {
                var c = kv.Value;
                var t = c.Target;
                if (kv.Key == t.Pos) c.LastPos = true;
                else if (kv.Key == t.Rot) c.LastRot = true;
                else c.LastScale = true;
            }

            // what no active constraint uses: an entry that may act again keeps its slot empty, the others give it up
            var unused = new List<string>();
            foreach (var kv in channels)
                if (!kv.Value.Used) unused.Add(kv.Key);
            foreach (string key in unused)
            {
                var r = channels[key];
                bool had = r.Channel.Slot.HasValue;
                if (r.Wanted) r.Channel.Clear();
                else
                {
                    r.Channel.Slot.Release();
                    channels.Remove(key);
                }
                if (had) r.Chara?.WantSettle();
            }

            // the bones: one run per character, in the plan's order
            foreach (var d in boneOwners)
                if (d.Root) d.Set.Evaluator = null;
            boneOwners.Clear();
            for (int u = 0; u < plan.Bones.Length; u++)
            {
                var seq = plan.Bones[u];
                if (seq.Length == 0) continue;
                var run = new ConstraintLive[seq.Length];
                for (int k = 0; k < seq.Length; k++) run[k] = lives[seq[k]];
                var d = DrivenPoseDriver.Ensure(units[u].Chara);
                if (d == null) continue;
                d.Set.Evaluator = new ConstraintBoneRun(evaluator, run);
                rt.OverrideApp.Track(d);
                boneOwners.Add(d);
            }

            var steps = new ConstraintRunStep[plan.Steps.Length];
            order = new List<ConstraintOrderRow>(steps.Length);
            for (int i = 0; i < steps.Length; i++)
            {
                var p = plan.Steps[i];
                if (!p.IsUnit)
                {
                    steps[i].Constraint = lives[p.Index];
                    order.Add(new ConstraintOrderRow(entries[entryAt[p.Index]].Id, "constraint"));
                    continue;
                }
                var c = units[p.Index];
                steps[i].Chara = c.Chara;
                steps[i].Item = c.Item;
                order.Add(new ConstraintOrderRow(c.Chara != null ? c.Chara.NodeId : c.Item.NodeId, c.Chara != null ? "chara" : "item"));
            }
            var stale = new List<CharaRealization>();
            foreach (var kv in drives)
                if (!usedDrives.Contains(kv.Value)) stale.Add(kv.Key);
            foreach (var c in stale) drives.Remove(c);
            var array = new ConstraintDrive[usedDrives.Count];
            usedDrives.CopyTo(array);
            evaluator.Install(steps, array);
        }

        // ------------------------------------------------------------ for the operations

        /// <summary>
        /// The loop <paramref name="id"/> would stand on in <paramref name="list"/>, on the built hierarchy: indices of the list in the order the
        /// values flow, starting at that entry; empty when none. An end that does not resolve has no edges, so it cannot be judged here.
        /// </summary>
        public int[] LoopOf(ConstraintList list, string id)
        {
            if (list == null) return Array.Empty<int>();
            var tree = new ConstraintTopology();
            var unitOf = new Dictionary<NodeRealization, int>();
            Candidates(tree, unitOf);
            var links = new List<ConstraintLink>();
            var at = new List<int>();
            int position = -1;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list.Entries[i];
                if (e == null || ConstraintList.BeyondLimit(i) || e.BadReason() != null) continue;
                var s = ConstraintResolver.Source(rt, e.Source);
                var t = ConstraintResolver.Target(rt, e.Target, ConstraintMath.PartsOf(e.Components));
                if (s.State != ConstraintEndState.Ok || t.State != ConstraintEndState.Ok) continue;
                Link(e, i, s, t, tree, unitOf, out var link);
                if (e.Id == id) position = links.Count;
                links.Add(link);
                at.Add(i);
            }
            if (position < 0) return Array.Empty<int>();
            var rowsOfLoop = ConstraintGraph.LoopThrough(links, position, tree);
            var result = new int[rowsOfLoop.Length];
            for (int i = 0; i < result.Length; i++) result[i] = at[rowsOfLoop[i]];
            return result;
        }

        /// <summary>
        /// The offset that keeps a target where it shows now, for an entry's ends and components, in the form an entry stores; false when an end
        /// does not resolve.
        /// </summary>
        public bool TryTakeOffset(ConstraintEntry e, out ConstraintOffset offset)
        {
            offset = null;
            if (e == null) return false;
            var parts = ConstraintMath.PartsOf(e.Components);
            var s = ConstraintResolver.Source(rt, e.Source);
            var t = ConstraintResolver.Target(rt, e.Target, parts);
            if (s.State != ConstraintEndState.Ok || (t.State != ConstraintEndState.Ok && t.State != ConstraintEndState.Protected)) return false;
            if (t.State == ConstraintEndState.Protected) return false;
            var source = ConstraintEvaluator.PoseOf(s.Transform, true);
            offset = ConstraintMath.StoredOf(ConstraintMath.Take(parts, source, t.Kind == ConstraintTargetKind.Ik ? IkPose(t) : ConstraintEvaluator.PoseOf(t.Transform, true)));
            return true;
        }

        // an IK target in the world as the solve takes it: the shown value, else the input pose
        static ConstraintPose IkPose(in ConstraintResolved t)
        {
            var root = t.Chara.Root.transform;
            var id = (IkTargetId)t.IkIndex;
            var v = t.Ik.Shown(id);
            if (!v.Has && t.Ik.Stage.HasLastInput) v = t.Ik.Stage.LastInput[t.IkIndex];
            if (!v.Has)
            {
                var read = new IkTargetValue[IkGroupInfo.TargetCount];
                t.Ik.Bones.ReadTargets(read);
                v = read[t.IkIndex];
            }
            var p = root.TransformPoint(v.Pos);
            var q = root.rotation * Quaternion.Euler(v.Rot);
            return new ConstraintPose(new Vec3d(p.x, p.y, p.z), ConstraintMath.Normalized(new Quatd(q.x, q.y, q.z, q.w)), new Vec3d(1, 1, 1));
        }
    }
}
