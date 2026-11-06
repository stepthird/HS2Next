// StudioOps, advanced pose: the edits of a character's `chara.advancedPose` block, shared by the commands and the inspector tab. Every
// edit goes through the bus on the model's field paths (a map element; a removal sets the whole map): an identity correction, a chain
// or a renderer with nothing set leaves its map, so the block never holds a no-op made by an edit and an emptied block is not saved.
// A refusal stores nothing and answers the command's code:
//   bad_args     an address that does not read, a value out of range, a limit of the block
//   not_found    a bone, chain, renderer or shape that a built character lacks (keepMissing stores a bone or a weight anyway)
//   unsupported  a part that keeps the mosaic in place
//   wrong_kind   a node that is no character
// A character that is not built stores the value without resolving it; a removal never resolves anything. Options: a MergeKey chains
// the edits of one address into one history entry (a removal stands alone); Record None applies without an entry; an open transaction
// (a UI drag, a bridge batch) takes the edit as a step. Result: Changed = the addresses or shapes set, Value = the addresses or
// shapes removed (List<string>).
using System;
using System.Collections.Generic;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>A change of one bone's correction: the channels given, the others keep their value.</summary>
    public sealed class BoneCorrectionEdit
    {
        /// <summary>The bone's address; the operation replaces it by its canonical form.</summary>
        public string Address;
        public Vec3? Pos, Rot, Scale;
        /// <summary>The argument that gave this edit, for the detail of a refusal (null: <c>bone</c>, <c>pos</c>, <c>rot</c>, <c>scale</c>; else
        /// that text, and that text + <c>.pos</c> ...).</summary>
        public string Arg;
    }

    /// <summary>The mosaic rule of advanced pose, one call each for the commands, the tab and the driver.</summary>
    public static class AdvancedPoseGuard
    {
        /// <summary>The message of a refusal.</summary>
        public const string Refusal = "mosaic parts are not editable";

        /// <summary>True when the mosaic rule keeps this transform out of a correction.</summary>
        public static bool Bone(CharaRealization r, Transform t) => MosaicSet.GuardsBone(r?.Context, t);

        /// <summary>True when the mosaic rule keeps this renderer out of a blend override.</summary>
        public static bool Renderer(CharaRealization r, Renderer s) => MosaicSet.GuardsBlend(r?.Context, s);
    }

    public sealed partial class StudioOps
    {
        // ------------------------------------------------------------ shared

        /// <summary>The block of character node <paramref name="id"/>; null with the refusal when it is no character.</summary>
        CharaAdvancedPose PoseBlock(string id, out OpResult fail)
        {
            var nodes = OfKind(new[] { id }, NodeKind.Chara, "character", "key", out fail);
            return nodes == null ? null : (nodes[0].State as CharaNodeState)?.AdvancedPose;
        }

        // the built character of a node, or null: nothing to resolve against
        CharaRealization PoseBuilt(string id)
        {
            var r = rt.FindChara(id);
            return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root ? r : null;
        }

        static string ArgOf(string prefix, string member) => prefix == null ? member : prefix + "." + member;

        /// <summary>One write of an edit, read from the block as it stands when its turn comes.</summary>
        readonly struct PoseStep
        {
            public readonly string Name;
            public readonly Func<AdvancedPoseWrite?> Write;
            public PoseStep(string name, Func<AdvancedPoseWrite?> write) { Name = name; Write = write; }
        }

        // A write of a whole map takes an entry out of it; a write of an element stores the entry.
        static bool RemovesEntry(in AdvancedPoseWrite w) => w.Path == AdvancedPosePaths.Bones || w.Path == AdvancedPosePaths.Chains || w.Path == AdvancedPosePaths.BlendShapes;

        // One history entry: one write with a merge key goes on the bus directly (the entry of a committed transaction is sealed, so
        // only this form can chain); anything else in a transaction. The bus's no-op leaves a name out of the lists.
        OpResult RunPose(string id, IReadOnlyList<PoseStep> steps, string label, ExecuteOptions o)
        {
            var removed = new List<string>();
            var r = OpResult.NothingToDo();
            r.Value = removed;
            o = Labelled(o, label);
            if (o.MergeKey != null && steps.Count == 1)
            {
                var w = steps[0].Write();
                if (w == null) return r;
                var c = Session.Commands.Set(id, w.Value.Path, w.Value.Value, o);
                if (c.Status == CommandStatus.Error) return OpResult.From(c);
                Note(r, removed, steps[0].Name, w.Value, c);
                r.With(c);
            }
            else
            {
                var tx = Open(label, o, out var fail);
                if (tx == null) return fail;
                using (tx)
                {
                    foreach (var s in steps)
                    {
                        var w = s.Write();
                        if (w == null) continue;
                        var c = tx.Set(id, w.Value.Path, w.Value.Value);
                        if (c.Status == CommandStatus.Error) return OpResult.From(c);
                        Note(r, removed, s.Name, w.Value, c);
                    }
                    r = Commit(tx, r);
                }
            }
            rt.ApplyNow();
            return r;
        }

        static void Note(OpResult r, List<string> removed, string name, in AdvancedPoseWrite w, CommandResult c)
        {
            if (c.Status != CommandStatus.Ok) return;
            if (RemovesEntry(w)) removed.Add(name);
            else r.Changed.Add(name);
        }

        // A clear: the entries it takes are named before the writes; Value lists them once the writes are through.
        OpResult RunClear(string id, IReadOnlyList<PoseStep> steps, List<string> taken, string label, ExecuteOptions o)
        {
            var r = RunPose(id, steps, label, o);
            if (r.Status == CommandStatus.Error) return r;
            r.Changed.Clear();
            r.Value = taken;
            return r;
        }

        // The key of a map entry named by a request: the text as it is when the map holds it, else its canonical form; an address that
        // does not read and names no entry is refused.
        static string KeyOf<T>(SortedDictionary<string, T> map, PoseAddressForm form, string text, string arg, out OpResult fail)
        {
            fail = null;
            if (text != null && map.ContainsKey(text)) return text;
            string canonical = PoseAddress.Canonical(form, text);
            if (canonical != null) return canonical;
            PoseAddress.TryParse(form, text, out _, out string why);
            fail = OpResult.BadArgs(arg, "an address", "bad address \"" + text + "\": " + why);
            return null;
        }

        static bool Finite3(Vec3 v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z);
        static OpResult Unit(float? f, string arg) => f.HasValue && !(Finite(f.Value) && AdvancedPoseRanges.UnitInRange(f.Value)) ? OpResult.BadArgs(arg, "0..1") : null;
        static OpResult Finite3(Vec3? v, string arg) => v.HasValue && !Finite3(v.Value) ? OpResult.BadArgs(arg, "finite numbers") : null;

        // ------------------------------------------------------------ bones

        /// <summary>Changes one bone's correction (one entry «Bone correction»): the channels given; an identity result removes the entry.</summary>
        public OpResult SetBoneCorrection(string id, string address, Vec3? pos, Vec3? rot, Vec3? scale, bool keepMissing = false, ExecuteOptions o = default) =>
            SetBoneCorrections(id, new[] { new BoneCorrectionEdit { Address = address, Pos = pos, Rot = rot, Scale = scale } }, keepMissing, o);

        /// <summary>Changes the corrections of several bones in one entry «Bone correction». A refusal of one edit stores none of them.</summary>
        public OpResult SetBoneCorrections(string id, IReadOnlyList<BoneCorrectionEdit> edits, bool keepMissing = false, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            if (edits == null || edits.Count == 0) return OpResult.BadArgs("bones", "at least one bone");
            var built = PoseBuilt(id);
            var keys = new HashSet<string>(block.Bones.Keys, StringComparer.Ordinal);
            int before = keys.Count;
            foreach (var e in edits)
            {
                string canonical = PoseAddress.Canonical(PoseAddressForm.Bone, e.Address);
                if (canonical == null)
                {
                    PoseAddress.TryParse(PoseAddressForm.Bone, e.Address, out _, out string why);
                    return OpResult.BadArgs(e.Arg ?? "bone", "a bone address", "bad address \"" + e.Address + "\": " + why);
                }
                e.Address = canonical;
                var bad = CheckBoneValues(e);
                if (bad != null) return bad;
                var next = block.Bones.TryGetValue(canonical, out var now) ? new BoneCorrection(now.Pos, now.Rot, now.Scale) : new BoneCorrection();
                if (e.Pos.HasValue) next.Pos = e.Pos.Value;
                if (e.Rot.HasValue) next.Rot = e.Rot.Value;
                if (e.Scale.HasValue) next.Scale = e.Scale.Value;
                if (next.IsIdentity) { keys.Remove(canonical); continue; }
                keys.Add(canonical);
                if (built == null) continue;
                var t = AdvancedPoseResolver.Resolve(built, canonical);
                if (!t)
                {
                    if (keepMissing) continue;
                    return OpResult.NotFound("this character has no bone \"" + canonical + "\"", e.Arg ?? "bone");
                }
                if (AdvancedPoseGuard.Bone(built, t)) return OpResult.Unsupported(AdvancedPoseGuard.Refusal);
            }
            if (keys.Count > AdvancedPoseLimits.MaxBones && keys.Count > before)
                return OpResult.BadArgs(edits[0].Arg != null ? "bones" : "bone", "at most " + AdvancedPoseLimits.MaxBones + " bone corrections on a character",
                    "a character holds at most " + AdvancedPoseLimits.MaxBones + " bone corrections");
            var steps = new List<PoseStep>(edits.Count);
            foreach (var e in edits)
            {
                var edit = e;
                steps.Add(new PoseStep(e.Address, () => AdvancedPoseEdits.SetBone(block, edit.Address, edit.Pos, edit.Rot, edit.Scale)));
            }
            return RunPose(id, steps, Labels.BoneCorrection, o);
        }

        static OpResult CheckBoneValues(BoneCorrectionEdit e)
        {
            if (e.Pos.HasValue && !(Finite3(e.Pos.Value) && AdvancedPoseRanges.PosInRange(e.Pos.Value)))
                return OpResult.BadArgs(ArgOf(e.Arg, "pos"), "each component within +-" + AdvancedPoseRanges.MaxPos);
            if (e.Rot.HasValue && !Finite3(e.Rot.Value))
                return OpResult.BadArgs(ArgOf(e.Arg, "rot"), "finite numbers");
            if (e.Scale.HasValue && !(Finite3(e.Scale.Value) && AdvancedPoseRanges.ScaleInRange(e.Scale.Value)))
                return OpResult.BadArgs(ArgOf(e.Arg, "scale"), "each component in " + AdvancedPoseRanges.MinScale + ".." + AdvancedPoseRanges.MaxScale);
            return null;
        }

        /// <summary>Removes corrections (one entry «Clear bone corrections»): <paramref name="addresses"/>, or every one when null. A key
        /// that is no address (kept from a file) is named as it is. Value = the keys removed.</summary>
        public OpResult ClearBoneCorrections(string id, IEnumerable<string> addresses = null, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            var steps = new List<PoseStep>();
            var taken = new List<string>();
            if (addresses == null)
            {
                taken.AddRange(block.Bones.Keys);
                steps.Add(new PoseStep("", () => AdvancedPoseEdits.ClearBones(block)));
            }
            else
                foreach (var a in addresses)
                {
                    string key = KeyOf(block.Bones, PoseAddressForm.Bone, a, "bone", out fail);
                    if (fail != null) return fail;
                    if (block.Bones.ContainsKey(key) && !taken.Contains(key)) taken.Add(key);
                    steps.Add(new PoseStep(key, () => AdvancedPoseEdits.RemoveBone(block, key)));
                }
            return RunClear(id, steps, taken, Labels.ClearBoneCorrections, o);
        }

        // ------------------------------------------------------------ chains

        /// <summary>Stores a chain's scene values as given (one entry «Jiggle values»); values with nothing set remove the entry. Use
        /// <see cref="AdvancedPoseEdits.ChainOf"/> for a copy of what is stored.</summary>
        public OpResult SetChainValues(string id, string address, ChainValues values, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            string canonical = PoseAddress.Canonical(PoseAddressForm.Chain, address);
            if (canonical == null)
            {
                PoseAddress.TryParse(PoseAddressForm.Chain, address, out _, out string why);
                return OpResult.BadArgs("chain", "a chain address", "bad address \"" + address + "\": " + why);
            }
            values = values ?? new ChainValues();
            var bad = CheckChainValues(values);
            if (bad != null) return bad;
            if (!values.IsEmpty)
            {
                var built = PoseBuilt(id);
                if (built != null && AdvancedPoseParts.Chain(built, canonical) == null)
                    return OpResult.NotFound("this character has no chain \"" + canonical + "\"", "chain");
                if (!block.Chains.ContainsKey(canonical) && block.Chains.Count >= AdvancedPoseLimits.MaxChains)
                    return OpResult.BadArgs("chain", "at most " + AdvancedPoseLimits.MaxChains + " chains on a character", "a character holds at most " + AdvancedPoseLimits.MaxChains + " chains");
            }
            return RunPose(id, new[] { new PoseStep(canonical, () => AdvancedPoseEdits.SetChain(block, canonical, values)) }, Labels.JiggleValues, o);
        }

        static OpResult CheckChainValues(ChainValues v)
        {
            return Unit(v.Weight, "weight") ?? Unit(v.Damping, "damping") ?? Unit(v.Elasticity, "elasticity") ?? Unit(v.Stiffness, "stiffness") ?? Unit(v.Inert, "inert")
                ?? (v.Radius.HasValue && !(Finite(v.Radius.Value) && AdvancedPoseRanges.RadiusInRange(v.Radius.Value)) ? OpResult.BadArgs("radius", "a number from 0") : null)
                ?? Finite3(v.Gravity, "gravity") ?? Finite3(v.Force, "force")
                ?? (v.FreezeAxis.HasValue && !AdvancedPoseRanges.FreezeAxisInRange(v.FreezeAxis.Value) ? OpResult.BadArgs("freezeAxis", "0 (none), 1 (x), 2 (y) or 3 (z)") : null);
        }

        /// <summary>Removes the scene values of chains (one entry «Clear jiggle values»): <paramref name="addresses"/>, or every chain when null.
        /// Value = the keys removed.</summary>
        public OpResult ClearChainValues(string id, IEnumerable<string> addresses = null, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            var steps = new List<PoseStep>();
            var taken = new List<string>();
            if (addresses == null)
            {
                taken.AddRange(block.Chains.Keys);
                steps.Add(new PoseStep("", () => AdvancedPoseEdits.ClearChains(block)));
            }
            else
                foreach (var a in addresses)
                {
                    string key = KeyOf(block.Chains, PoseAddressForm.Chain, a, "chain", out fail);
                    if (fail != null) return fail;
                    if (block.Chains.ContainsKey(key) && !taken.Contains(key)) taken.Add(key);
                    steps.Add(new PoseStep(key, () => AdvancedPoseEdits.RemoveChain(block, key)));
                }
            return RunClear(id, steps, taken, Labels.ClearJiggleValues, o);
        }

        // ------------------------------------------------------------ bust and hips

        /// <summary>Stores a bust chain's scene values as given (one entry «Jiggle values»); <paramref name="chain"/> = breastL, breastR, hipL or hipR.
        /// Use <see cref="AdvancedPoseEdits.BustOf"/> for a copy of what is stored.</summary>
        public OpResult SetBustValues(string id, string chain, BustValues values, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            if (BustSet.KindOf(chain) < 0) return OpResult.BadArgs("chain", "breastL, breastR, hipL or hipR");
            values = values ?? new BustValues();
            var bad = Finite3(values.Gravity, "gravity") ?? Finite3(values.Force, "force") ?? Unit(values.Damping, "damping") ?? Unit(values.Elasticity, "elasticity") ??
                      Unit(values.Stiffness, "stiffness") ?? Unit(values.Inert, "inert");
            if (bad != null) return bad;
            return RunPose(id, new[] { new PoseStep(chain, () => AdvancedPoseEdits.SetBust(chain, values)) }, Labels.JiggleValues, o);
        }

        /// <summary>Removes the scene values of one bust chain, or of all four when <paramref name="chain"/> is null (one entry «Clear jiggle values»).
        /// Value = the chains cleared.</summary>
        public OpResult ClearBustValues(string id, string chain = null, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            if (chain != null && BustSet.KindOf(chain) < 0) return OpResult.BadArgs("chain", "breastL, breastR, hipL or hipR");
            var steps = new List<PoseStep>();
            var taken = new List<string>();
            foreach (var name in BustSet.Names)
            {
                string n = name;
                if ((chain != null && chain != n) || block.Bust[BustSet.KindOf(n)].IsEmpty) continue;
                taken.Add(n);
                steps.Add(new PoseStep(n, () => AdvancedPoseEdits.SetBust(n, new BustValues())));
            }
            return RunClear(id, steps, taken, Labels.ClearJiggleValues, o);
        }

        // ------------------------------------------------------------ blend shapes

        /// <summary>
        /// Changes one renderer's weights (one entry «Blend shapes»): <paramref name="set"/> are stored, <paramref name="remove"/> leave; a
        /// renderer left without a weight leaves its map. Changed and Value name the shapes; a refusal's Arg is <c>weights.&lt;shape&gt;</c>.
        /// </summary>
        public OpResult SetBlendWeights(string id, string address, IReadOnlyList<KeyValuePair<string, float>> set, IReadOnlyList<string> remove = null, bool keepMissing = false, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            string canonical = PoseAddress.Canonical(PoseAddressForm.Blend, address);
            if (canonical == null)
            {
                PoseAddress.TryParse(PoseAddressForm.Blend, address, out _, out string why);
                return OpResult.BadArgs("renderer", "a renderer address", "bad address \"" + address + "\": " + why);
            }
            set = set ?? Array.Empty<KeyValuePair<string, float>>();
            remove = remove ?? Array.Empty<string>();
            bool had = block.BlendShapes.TryGetValue(canonical, out var now);
            int before = had ? now.Weights.Count : 0;
            var shapes = new HashSet<string>(StringComparer.Ordinal);
            if (had) foreach (var k in now.Weights.Keys) shapes.Add(k);
            foreach (var s in remove) shapes.Remove(s);
            foreach (var kv in set)
            {
                string arg = "weights." + kv.Key;
                if (string.IsNullOrEmpty(kv.Key)) return OpResult.BadArgs(arg, "a shape name");
                if (!(Finite(kv.Value) && AdvancedPoseRanges.BlendWeightInRange(kv.Value)))
                    return OpResult.BadArgs(arg, AdvancedPoseRanges.MinBlendWeight + ".." + AdvancedPoseRanges.MaxBlendWeight);
                shapes.Add(kv.Key);
            }
            if (set.Count > 0)
            {
                var built = PoseBuilt(id);
                var renderer = built != null ? AdvancedPoseParts.Renderer(built, canonical) : null;
                if (built != null && !renderer && !keepMissing) return OpResult.NotFound("this character has no renderer \"" + canonical + "\"", "renderer");
                if (renderer)
                {
                    if (AdvancedPoseGuard.Renderer(built, renderer)) return OpResult.Unsupported(AdvancedPoseGuard.Refusal);
                    var mesh = renderer.sharedMesh;
                    if (!keepMissing)
                        foreach (var kv in set)
                            if (!mesh || mesh.GetBlendShapeIndex(kv.Key) < 0)
                                return OpResult.NotFound("the renderer \"" + canonical + "\" has no shape \"" + kv.Key + "\"", "weights." + kv.Key);
                }
                if (!had && block.BlendShapes.Count >= AdvancedPoseLimits.MaxRenderers)
                    return OpResult.BadArgs("renderer", "at most " + AdvancedPoseLimits.MaxRenderers + " renderers on a character", "a character holds at most " + AdvancedPoseLimits.MaxRenderers + " renderers");
                int weights = shapes.Count - before;
                foreach (var b in block.BlendShapes.Values) weights += b.Weights.Count;
                if (shapes.Count > before && weights > AdvancedPoseLimits.MaxWeights)
                    return OpResult.BadArgs("weights", "at most " + AdvancedPoseLimits.MaxWeights + " weights on a character", "a character holds at most " + AdvancedPoseLimits.MaxWeights + " weights");
            }
            var changed = new List<string>();
            var removed = new List<string>();
            foreach (var kv in set)
                if (!had || !now.Weights.TryGetValue(kv.Key, out float w) || w != kv.Value) changed.Add(kv.Key);
            foreach (var s in remove)
                if (had && now.Weights.ContainsKey(s)) removed.Add(s);
            var r = RunPose(id, new[] { new PoseStep(canonical, () => AdvancedPoseEdits.SetWeights(block, canonical, set, remove)) }, Labels.BlendShapes, o);
            if (r.Status == CommandStatus.Error) return r;
            r.Changed.Clear();
            r.Changed.AddRange(changed);
            r.Value = removed;
            return r;
        }

        /// <summary>
        /// Removes blend weights (one entry «Clear blend shapes»): <paramref name="shape"/> of <paramref name="address"/>, every weight of
        /// <paramref name="address"/>, or every weight when <paramref name="address"/> is null. Value = the weights removed as
        /// <c>&lt;renderer&gt;:&lt;shape&gt;</c>.
        /// </summary>
        public OpResult ClearBlendWeights(string id, string address = null, string shape = null, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var block = PoseBlock(id, out var fail);
            if (block == null) return fail;
            var taken = new List<string>();
            var steps = new List<PoseStep>();
            if (address == null)
            {
                foreach (var kv in block.BlendShapes)
                    foreach (var w in kv.Value.Weights.Keys) taken.Add(kv.Key + ":" + w);
                steps.Add(new PoseStep("", () => AdvancedPoseEdits.ClearWeights(block)));
            }
            else
            {
                string key = KeyOf(block.BlendShapes, PoseAddressForm.Blend, address, "renderer", out fail);
                if (fail != null) return fail;
                if (block.BlendShapes.TryGetValue(key, out var now))
                {
                    if (shape == null)
                    {
                        foreach (var w in now.Weights.Keys) taken.Add(key + ":" + w);
                        steps.Add(new PoseStep(key, () => AdvancedPoseEdits.RemoveRenderer(block, key)));
                    }
                    else if (now.Weights.ContainsKey(shape))
                    {
                        taken.Add(key + ":" + shape);
                        steps.Add(new PoseStep(key, () => AdvancedPoseEdits.SetWeights(block, key, null, new[] { shape })));
                    }
                }
            }
            return RunClear(id, steps, taken, Labels.ClearBlendShapes, o);
        }
    }
}
