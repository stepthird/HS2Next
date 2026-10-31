// StudioOps, character area: animation, speed / pattern / options,
// face, hands, look, look target (extracted from the bridge handlers chara.anime, chara.speed, chara.animeOption,
// chara.face, chara.hands, look.target with identical validation, no-op rules, labels and warnings), and the new state
// operations: clothes states with HS2's cycling and couplings computed here (U26), accessories, the other `state` members
// (liquid, nipple, wet, gloss, son, silhouette, tears, cheek), joint correction, change character and "Use card state".
// Every method applies to every character it is given (U25), one entry.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Content.Studio;
using HS2Next.Data;
using HS2Next.Import;
using HS2Next.Motion.Face;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The face members of <see cref="StudioOps.SetFace"/>; null = unchanged.</summary>
    public sealed class FaceEdit
    {
        /// <summary>Eyebrow / eyes as positions in the character's lists (the UI numbers, HS2's UiToId, clamped); false = pattern ids.</summary>
        public bool ListPositions = true;
        public int? Eyebrow, Eyes, Mouth;
        public float? EyesOpen, MouthOpen;
        public bool? Blink;
        /// <summary>`chara.face.lipSync`.</summary>
        public bool? LipSync;
        /// <summary>Look patterns 0–4 (eyes 4 = the look target).</summary>
        public int? EyesLook, NeckLook;
        public float? Tears, Cheek;

        public bool IsEmpty => Eyebrow == null && Eyes == null && Mouth == null && EyesOpen == null && MouthOpen == null && Blink == null && LipSync == null &&
                               EyesLook == null && NeckLook == null && Tears == null && Cheek == null;
    }

    /// <summary>What <see cref="StudioOps.ChangeCharacter"/> keeps (U27: everything by default).</summary>
    public sealed class KeepOptions
    {
        /// <summary>Keep the node's name (default true).</summary>
        public bool Name = true;
    }

    public sealed partial class StudioOps
    {
        List<TreeNode> Charas(IEnumerable<string> ids, string arg, out OpResult fail) => OfKind(ids, NodeKind.Chara, "character", arg, out fail);

        static CharaNodeState StateOf(TreeNode n) => n.State as CharaNodeState;

        // ------------------------------------------------------------ animation (chara.anime, chara.speed, chara.animeOption)

        /// <summary>
        /// Checks an animation row as chara.anime does: plain and H rows play; a row hidden in the lists adds a warning.
        /// <paramref name="described"/> = how the caller named the row (default: guid g/c/no). Null when the row can be played.
        /// </summary>
        public static OpResult CheckAnimeRow(StudioAnime row, string described, List<string> warnings)
        {
            if (row == null) return OpResult.NotFound("no animation " + (described ?? "(none)"));
            described ??= (row.Guid != null ? row.Guid + " " : "") + row.Group + "/" + row.Category + "/" + row.No;
            if (!row.Visible) warnings?.Add("animation " + described + " is hidden in the lists; it is played as HS2's pose load plays such rows");
            return null;
        }

        /// <summary>
        /// Plays <paramref name="row"/> from <paramref name="time"/> on every character (one entry "Animation"; speed ≥ 0 and loop
        /// with it when given). HS2's LoadAnime always starts the clip at the time: a node whose row and time already equal the
        /// model's jumps to the time after the apply.
        /// </summary>
        public OpResult SetAnimation(IEnumerable<string> ids, StudioAnime row, float time = 0f, float? speed = null, bool? loop = null, string described = null, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            var warnings = new List<string>();
            var bad = CheckAnimeRow(row, described, warnings);
            if (bad != null) return bad;
            if (!Finite(time)) return OpResult.BadArgs("time", "a finite number");
            if (speed != null && (!Finite(speed.Value) || speed.Value < 0f)) return OpResult.BadArgs("speed", "a finite number ≥ 0");
            var r = OpResult.NothingToDo();
            r.Warnings.AddRange(warnings);
            var jump = new List<string>();
            var tx = Open(Labels.Animation, o, out fail);
            if (tx == null) { fail.Warnings.AddRange(warnings); return fail; }
            using (tx)
            {
                foreach (var n in nodes)
                {
                    var setRef = tx.Set(n.Id, "chara.anime.ref", new AnimeRef(row.Guid, row.Group, row.Category, row.No));
                    if (setRef.Status == CommandStatus.Error) return WithWarnings(OpResult.From(setRef), warnings);
                    var setTime = tx.Set(n.Id, "chara.anime.time", time);
                    if (setTime.Status == CommandStatus.Error) return WithWarnings(OpResult.From(setTime, "time"), warnings);
                    r.Record(n.Id, "chara.anime.ref", setRef.Status);
                    r.Record(n.Id, "chara.anime.time", setTime.Status);
                    if (speed != null)
                    {
                        var s = tx.Set(n.Id, "chara.anime.speed", speed.Value);
                        if (s.Status == CommandStatus.Error) return WithWarnings(OpResult.From(s, "speed"), warnings);
                        r.Record(n.Id, "chara.anime.speed", s.Status);
                    }
                    if (loop != null)
                    {
                        var s = tx.Set(n.Id, "chara.anime.forceLoop", loop.Value);
                        if (s.Status == CommandStatus.Error) return WithWarnings(OpResult.From(s, "loop"), warnings);
                        r.Record(n.Id, "chara.anime.forceLoop", s.Status);
                    }
                    if (setRef.Status == CommandStatus.NoOp && setTime.Status == CommandStatus.NoOp) jump.Add(n.Id);
                }
                r = Commit(tx, r);
                if (!r.Ok) return WithWarnings(r, warnings);
            }
            rt.ApplyNow();
            foreach (var id in jump)
            {
                var real = rt.FindChara(id);
                if (real != null && real.State == RealizationState.Built && real.Animation) real.Animation.NormalizedTime = time;
            }
            return r;
        }

        static OpResult WithWarnings(OpResult r, List<string> warnings)
        {
            foreach (var w in warnings) if (!r.Warnings.Contains(w)) r.Warnings.Add(w);
            return r;
        }

        /// <summary>chara.anime.speed (≥ 0) and forceLoop of every character (one entry "Animation speed"; nothing when both are null).</summary>
        public OpResult SetAnimeSpeed(IEnumerable<string> ids, float? speed, bool? loop = null, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (speed != null && (!Finite(speed.Value) || speed.Value < 0f)) return OpResult.BadArgs("speed", "a finite number ≥ 0");
            var plan = new List<(string, object)>();
            if (speed != null) plan.Add(("chara.anime.speed", speed.Value));
            if (loop != null) plan.Add(("chara.anime.forceLoop", loop.Value));
            if (plan.Count == 0) { rt.ApplyNow(); return OpResult.NothingToDo(); }
            return SetManyNoPre(Ids(nodes), plan, Labels.AnimationSpeed, o, p => p.EndsWith(".speed", StringComparison.Ordinal) ? "speed" : "loop");
        }

        /// <summary>chara.anime.pattern (the vanilla motion-pattern slider, 0–1 in the UI) of every character ("Animation speed").</summary>
        public OpResult SetAnimePattern(IEnumerable<string> ids, float pattern, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (!Finite(pattern)) return OpResult.BadArgs("pattern", "a finite number");
            return SetManyNoPre(Ids(nodes), new List<(string, object)> { ("chara.anime.pattern", pattern) }, Labels.AnimationSpeed, o, _ => "pattern");
        }

        /// <summary>
        /// Option items' visibility, the two animation parameters and forced loop (one entry "Animation options"). A single
        /// character with only <paramref name="visible"/> is one plain set (chara.animeOption).
        /// </summary>
        public OpResult SetAnimeOption(IEnumerable<string> ids, bool? visible, float[] parameters = null, bool? forceLoop = null, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (parameters != null && parameters.Length != 2) return OpResult.BadArgs("params", "[p1, p2]");
            if (nodes.Count == 1 && visible != null && parameters == null && forceLoop == null)
            {
                var res = Session.Set(nodes[0].Id, "chara.anime.optionVisible", visible.Value, Labelled(o, Labels.AnimationOptions));
                var r1 = OpResult.From(res, "visible");
                if (r1.Ok) r1.Record(nodes[0].Id, "chara.anime.optionVisible", res.Status);
                rt.ApplyNow();
                return r1;
            }
            var plan = new List<(string, object)>();
            if (visible != null) plan.Add(("chara.anime.optionVisible", visible.Value));
            if (parameters != null) plan.Add(("chara.anime.params", new[] { parameters[0], parameters[1] }));
            if (forceLoop != null) plan.Add(("chara.anime.forceLoop", forceLoop.Value));
            if (plan.Count == 0) { rt.ApplyNow(); return OpResult.NothingToDo(); }
            return SetManyNoPre(Ids(nodes), plan, Labels.AnimationOptions, o, p => p.EndsWith("Visible", StringComparison.Ordinal) ? "visible" : p.EndsWith("params", StringComparison.Ordinal) ? "params" : "forceLoop");
        }

        /// <summary>Restarts the playing clip of every built character (runtime only, not recorded).</summary>
        public OpResult RestartAnimation(IEnumerable<string> ids)
        {
            rt.ApplyNow();
            var r = OpResult.NothingToDo();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var real = rt.FindChara(id);
                if (real != null && real.State == RealizationState.Built && real.Animation) { real.Animation.Restart(); r.Changed.Add(id); }
                else r.Warnings.Add("restart: character " + id + " is not built");
            }
            return r.Changed.Count > 0 ? r.With(CommandResult.Success(null)) : r;
        }

        static List<string> Ids(List<TreeNode> nodes)
        {
            var l = new List<string>(nodes.Count);
            foreach (var n in nodes) l.Add(n.Id);
            return l;
        }

        /// <summary><see cref="SetMany"/> without the ApplyNow before the transaction (the handlers did not apply first) but with the one after.</summary>
        OpResult SetManyNoPre(IList<string> targets, IList<(string path, object value)> plan, string label, ExecuteOptions o, Func<string, string> argOf)
        {
            var tx = Open(label, o, out var fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var t in targets)
                    foreach (var (path, value) in plan)
                    {
                        var s = tx.Set(t, path, value);
                        if (s.Status == CommandStatus.Error) return OpResult.From(s, argOf?.Invoke(path));
                        r.Record(t ?? "", path, s.Status);
                    }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        // ------------------------------------------------------------ face, hands, look (chara.face, chara.hands, look.target)

        /// <summary>The expression lists of a character: the built character's, else the catalog's by the appearance's sex.</summary>
        public ExpressionLists ListsOf(TreeNode n)
        {
            var r = rt.FindChara(n.Id);
            if (r != null && r.State == RealizationState.Built && r.Expression != null) return r.Expression.Lists;
            return ExpressionLists.For(Services?.Catalog, SexOf(n) == 0);
        }

        /// <summary>0 male, 1 female, −1 unknown: the stored profile, else the card.</summary>
        public static int SexOf(TreeNode n)
        {
            var ap = n?.Appearance;
            var p = ap?.Stored?.Chara?.Profile;
            if (p != null) return p.Sex;
            return ap?.Card != null ? ap.Card.Sex : -1;
        }

        /// <summary>HS2's UiToId: the id at a UI position clamped to the list (the position itself for an empty list).</summary>
        public static int UiToId(int[] keys, int ui) => keys == null || keys.Length == 0 ? ui : keys[Mathf.Clamp(ui, 0, keys.Length - 1)];

        /// <summary>
        /// Face patterns, opens, blink, look patterns (0–4), tears and cheek of every character (one entry "Face"; nothing when the
        /// edit is empty). Positions map to ids per character.
        /// </summary>
        public OpResult SetFace(IEnumerable<string> ids, FaceEdit e, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            e ??= new FaceEdit();
            if (e.EyesLook != null && (e.EyesLook < 0 || e.EyesLook > 4)) return OpResult.BadArgs("eyesLook", "0–4");
            if (e.NeckLook != null && (e.NeckLook < 0 || e.NeckLook > 4)) return OpResult.BadArgs("neckLook", "0–4");
            foreach (var (v, arg) in new[] { (e.EyesOpen, "eyesOpen"), (e.MouthOpen, "mouthOpen"), (e.Tears, "tears"), (e.Cheek, "blush") })
                if (v != null && !Finite(v.Value)) return OpResult.BadArgs(arg, "a finite number");
            var r = OpResult.NothingToDo();
            if (e.IsEmpty) { rt.ApplyNow(); return r; }
            var tx = Open(Labels.Face, o, out fail);
            if (tx == null) return fail;
            using (tx)
            {
                foreach (var n in nodes)
                {
                    var lists = e.ListPositions && (e.Eyebrow != null || e.Eyes != null) ? ListsOf(n) : null;
                    var sets = new List<(string path, object value, string arg)>();
                    if (e.Eyebrow != null) sets.Add(("chara.face.eyebrow", e.ListPositions ? UiToId(lists.Eyebrow, e.Eyebrow.Value) : e.Eyebrow.Value, "eyebrow"));
                    if (e.Eyes != null) sets.Add(("chara.face.eyes", e.ListPositions ? UiToId(lists.Eyes, e.Eyes.Value) : e.Eyes.Value, "eyes"));
                    if (e.Mouth != null) sets.Add(("chara.face.mouth", e.Mouth.Value, "mouth"));
                    if (e.EyesOpen != null) sets.Add(("chara.face.eyesOpen", e.EyesOpen.Value, "eyesOpen"));
                    if (e.MouthOpen != null) sets.Add(("chara.face.mouthOpen", e.MouthOpen.Value, "mouthOpen"));
                    if (e.Blink != null) sets.Add(("chara.face.blink", e.Blink.Value, "blink"));
                    if (e.LipSync != null) sets.Add(("chara.face.lipSync", e.LipSync.Value, "lipSync"));
                    if (e.EyesLook != null) sets.Add(("chara.look.eyes", e.EyesLook.Value, "eyesLook"));
                    if (e.NeckLook != null) sets.Add(("chara.look.neck", e.NeckLook.Value, "neckLook"));
                    if (e.Tears != null) sets.Add(("chara.face.tears", e.Tears.Value, "tears"));
                    if (e.Cheek != null) sets.Add(("chara.face.cheek", e.Cheek.Value, "blush"));
                    foreach (var (path, value, arg) in sets)
                    {
                        var s = tx.Set(n.Id, path, value);
                        if (s.Status == CommandStatus.Error) return OpResult.From(s, arg);
                        r.Record(n.Id, path, s.Status);
                    }
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>
        /// Hand patterns of every character (0 = from the animation, n = pattern n − 1; ≥ 0), one entry "Hands"; a null side keeps
        /// the node's value. Nothing when both are null.
        /// </summary>
        public OpResult SetHands(IEnumerable<string> ids, int? left, int? right, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (left != null && left < 0) return OpResult.BadArgs("left", "≥ 0 (0 = from the animation, n = hand pattern n − 1)");
            if (right != null && right < 0) return OpResult.BadArgs("right", "≥ 0 (0 = from the animation, n = hand pattern n − 1)");
            var r = OpResult.NothingToDo();
            if (left == null && right == null) { rt.ApplyNow(); return r; }
            var tx = Open(Labels.Hands, o, out fail);
            if (tx == null) return fail;
            using (tx)
            {
                foreach (var n in nodes)
                {
                    var st = StateOf(n);
                    int l = st?.Hands != null && st.Hands.Length > 0 ? st.Hands[0] : 0;
                    int rr = st?.Hands != null && st.Hands.Length > 1 ? st.Hands[1] : 0;
                    var s = tx.Set(n.Id, "chara.hands", new[] { left ?? l, right ?? rr });
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, left != null ? "left" : "right");
                    r.Record(n.Id, "chara.hands", s.Status);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>Eye / neck look patterns (0–4) of every character, one entry "Look".</summary>
        public OpResult SetLook(IEnumerable<string> ids, int? eyes, int? neck, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (eyes != null && (eyes < 0 || eyes > 4)) return OpResult.BadArgs("eyes", "0–4");
            if (neck != null && (neck < 0 || neck > 4)) return OpResult.BadArgs("neck", "0–4");
            var plan = new List<(string, object)>();
            if (eyes != null) plan.Add(("chara.look.eyes", eyes.Value));
            if (neck != null) plan.Add(("chara.look.neck", neck.Value));
            if (plan.Count == 0) return OpResult.NothingToDo();
            return SetManyNoPre(Ids(nodes), plan, Labels.Look, o, p => p.EndsWith("eyes", StringComparison.Ordinal) ? "eyes" : "neck");
        }

        /// <summary>
        /// Moves the look-at target of a built character to <paramref name="worldPos"/> (stored local to its parent, one entry
        /// "Look target"); unavailable when the character is not built or has no target.
        /// </summary>
        public OpResult SetLookTarget(string id, Vector3 worldPos, ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            rt.ApplyNow();
            var real = rt.FindChara(nodes[0].Id);
            if (real == null || real.State != RealizationState.Built || real.InGraveyard || !real.Root)
                return OpResult.Unavailable("character " + nodes[0].Id + " is not built (" + (real?.State.ToString() ?? "NotRealized") + (real?.Detail != null ? ": " + real.Detail : "") + ")",
                    "key", real?.State.ToString() ?? "NotRealized");
            var t = real.Look ? real.Look.LookAtTarget : null;
            if (!t) return OpResult.Unavailable("character " + real.NodeId + " has no look-at target", "key", real.State.ToString());
            var local = t.parent ? t.parent.InverseTransformPoint(worldPos) : worldPos;
            var res = Session.Set(real.NodeId, "chara.look.target", new Vec3(local.x, local.y, local.z), Labelled(o, Labels.LookTarget));
            var r = OpResult.From(res, "pos");
            if (r.Ok) r.Record(real.NodeId, "chara.look.target", res.Status);
            rt.ApplyNow();
            return r;
        }

        // ------------------------------------------------------------ clothes (U26: coupled values stored)

        /// <summary>The clothes state types of a character (Unknown when it is not built: values are stored as given).</summary>
        public ClothesStateSet ClothesStatesOf(string id)
        {
            rt.ApplyNow();
            var r = rt.FindChara(id);
            return r != null && r.State == RealizationState.Built ? r.ClothesStates : ClothesStateSet.Unknown;
        }

        /// <summary>
        /// One clothes kind (0 top … 7 shoes) to a state (0 on, 1 half, 2 off) on every character, one entry "Clothes": the op
        /// applies HS2's cycling (<paramref name="next"/> = the cycling direction) and couplings with the character's state types
        /// and stores the resulting array (`chara.state.clothes`). Value = Dictionary&lt;id, int[]&gt; of the stored results.
        /// </summary>
        public OpResult SetClothes(IEnumerable<string> ids, int kind, int state, bool next = true, ExecuteOptions o = default)
        {
            if (kind < 0 || kind >= ClothesStateSet.KindCount) return OpResult.BadArgs("kind", "0–7 or top, bottom, bra, shorts, gloves, pantyhose, socks, shoes");
            if (state < 0 || state > 2) return OpResult.BadArgs("state", "0–2 (on, half, off)");
            return SetClothesStates(ids, new[] { (kind, state) }, next, o);
        }

        /// <summary>"All" (HS2's SetClothesStateAll: the eight kinds in order) on every character, one entry "Clothes".</summary>
        public OpResult SetClothesAll(IEnumerable<string> ids, int state, ExecuteOptions o = default)
        {
            if (state < 0 || state > 2) return OpResult.BadArgs("all", "0–2 (on, half, off)");
            var list = new List<(int, int)>();
            for (int k = 0; k < ClothesStateSet.KindCount; k++) list.Add((k, state));
            return SetClothesStates(ids, list, true, o);
        }

        /// <summary>Several (kind, state) requests applied in order on every character (one entry "Clothes").</summary>
        public OpResult SetClothesStates(IEnumerable<string> ids, IReadOnlyList<(int kind, int state)> requests, bool next = true, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            foreach (var (k, s) in requests ?? Array.Empty<(int, int)>())
            {
                if (k < 0 || k >= ClothesStateSet.KindCount) return OpResult.BadArgs("states", "kinds 0–7");
                if (s < 0 || s > 2) return OpResult.BadArgs("states." + ClothesStateSet.KindNames[k], "0–2 (on, half, off)");
            }
            var rulesOf = new Dictionary<string, ClothesStateSet>(StringComparer.Ordinal);
            foreach (var n in nodes) rulesOf[n.Id] = ClothesStatesOf(n.Id);
            var tx = Open(Labels.Clothes, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            var values = new Dictionary<string, int[]>(StringComparer.Ordinal);
            using (tx)
            {
                foreach (var n in nodes)
                {
                    var rules = rulesOf[n.Id];
                    var cur = (int[])(StateOf(n)?.State.Clothes ?? new int[ClothesStateSet.KindCount]).Clone();
                    var next2 = cur;
                    foreach (var (k, s) in requests) next2 = rules.Apply(next2, k, s, next);
                    values[n.Id] = next2;
                    var res = tx.Set(n.Id, "chara.state.clothes", next2);
                    if (res.Status == CommandStatus.Error) return OpResult.From(res, "states");
                    for (int k = 0; k < ClothesStateSet.KindCount; k++)
                        if (k < cur.Length && cur[k] != next2[k]) r.Record(n.Id, ClothesStateSet.KindNames[k], CommandStatus.Ok);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            r.Value = values;
            return r;
        }

        // ------------------------------------------------------------ accessories

        /// <summary>The accessory ids of a character's worn coordinate in slot order (null when not known).</summary>
        public static IReadOnlyList<string> AccessoryIdsOf(TreeNode n) => n?.Appearance?.WornAccessoryIds();

        /// <summary>The accessory id of build slot <paramref name="slot"/> (0-based; the slot table of the stored coordinate,
        /// AccessorySlotRule: the slot the Accessories page, the bridge and the State tab name), else null. A card-backed node
        /// (not imported yet) names the card's slot.</summary>
        public static string AccessoryIdOfSlot(TreeNode n, int slot)
        {
            var coord = n?.Appearance?.Stored?.Coord;
            if (coord != null) return AccessorySlotRule.Assign(coord.Accessories).IdBySlot.TryGetValue(slot, out var id) ? id : null;
            var card = n?.Appearance?.Card;
            return card != null ? CardAppearanceSource.AccessoryId(card.Sha256, slot) : null;
        }

        /// <summary>Shows / hides accessories by id on every character (one entry "Accessories"); an id the character does not wear is not_found.</summary>
        public OpResult SetAccessories(IEnumerable<string> ids, IReadOnlyDictionary<string, bool> show, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (show == null || show.Count == 0) return OpResult.NothingToDo();
            foreach (var n in nodes)
            {
                var known = AccessoryIdsOf(n);
                if (known == null) continue;
                foreach (var kv in show)
                    if (!Contains(known, kv.Key)) return OpResult.NotFound("character " + n.Id + " wears no accessory \"" + kv.Key + "\"", "show." + kv.Key, Head(known, 5));
            }
            var tx = Open(Labels.Accessories, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var n in nodes)
                    foreach (var kv in show)
                    {
                        string path = "chara.state.accessories[" + FieldPath.Quote(kv.Key) + "]";
                        var s = tx.Set(n.Id, path, kv.Value);
                        if (s.Status == CommandStatus.Error) return OpResult.From(s, "show." + kv.Key);
                        r.Record(n.Id, kv.Key, s.Status);
                    }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        public OpResult SetAccessory(IEnumerable<string> ids, string accessoryId, bool visible, ExecuteOptions o = default) =>
            SetAccessories(ids, new Dictionary<string, bool>(StringComparer.Ordinal) { [accessoryId ?? ""] = visible }, o);

        /// <summary>Every worn accessory (the known ids, else the stored map's) of every character (one entry "Accessories").</summary>
        public OpResult SetAccessoriesAll(IEnumerable<string> ids, bool visible, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            var tx = Open(Labels.Accessories, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var n in nodes)
                {
                    var keys = new List<string>();
                    var known = AccessoryIdsOf(n);
                    if (known != null) keys.AddRange(known);
                    else if (StateOf(n) != null) keys.AddRange(StateOf(n).State.Accessories.Keys);
                    foreach (var id in keys)
                    {
                        var s = tx.Set(n.Id, "chara.state.accessories[" + FieldPath.Quote(id) + "]", visible);
                        if (s.Status == CommandStatus.Error) return OpResult.From(s, "all");
                        r.Record(n.Id, id, s.Status);
                    }
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        static bool Contains(IReadOnlyList<string> l, string s)
        {
            foreach (var x in l) if (string.Equals(x, s, StringComparison.Ordinal)) return true;
            return false;
        }

        static List<string> Head(IReadOnlyList<string> l, int n)
        {
            var r = new List<string>();
            foreach (var x in l) { if (r.Count == n) break; r.Add(x); }
            return r;
        }

        // ------------------------------------------------------------ other state members

        /// <summary>The members of <see cref="SetState"/> (relative names) and their full paths.</summary>
        public static readonly string[] StateMembers = { "liquid", "nipple", "wet", "gloss", "son", "silhouette", "tears", "cheek" };

        /// <summary>The full field path of a state member ("liquid[2]", "son.length", "silhouette.color", "tears", …); null when it is none.</summary>
        public static string StatePath(string member)
        {
            if (string.IsNullOrEmpty(member)) return null;
            string m = member.StartsWith("chara.state.", StringComparison.Ordinal) ? member.Substring(12) : member;
            string head = m;
            int cut = m.IndexOfAny(new[] { '.', '[' });
            if (cut >= 0) head = m.Substring(0, cut);
            if (Array.IndexOf(StateMembers, head) < 0) return null;
            if (head == "tears" || head == "cheek") return cut < 0 ? "chara.face." + head : null;
            return FieldPath.Canonical("chara.state." + m);
        }

        /// <summary>
        /// Sets `state` members (liquid, nipple, wet, gloss, son, silhouette; tears and cheek of the face) on every character, one
        /// entry "Character state". Values are stored as given (no clamping); a float must be finite.
        /// </summary>
        public OpResult SetState(IEnumerable<string> ids, IReadOnlyList<(string member, object value)> sets, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            var plan = new List<(string, object)>();
            var argOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (member, value) in sets ?? Array.Empty<(string, object)>())
            {
                string full = StatePath(member);
                if (full == null)
                {
                    var r0 = OpResult.BadArgs("set." + member, "a character state member", "set." + member + ": not a settable state member (" + string.Join(", ", StateMembers) + ")");
                    r0.Candidates = StateMembers;
                    return r0;
                }
                if (value is float f && !Finite(f)) return OpResult.BadArgs("set." + member, "a finite number");
                plan.Add((full, value));
                argOf[full] = "set." + member;
            }
            if (plan.Count == 0) return OpResult.NothingToDo();
            return SetMany(Ids(nodes), plan, Labels.CharacterState, o, p => argOf.TryGetValue(p, out var a) ? a : "set");
        }

        public OpResult SetState(IEnumerable<string> ids, string member, object value, ExecuteOptions o = default) =>
            SetState(ids, new[] { (member, value) }, o);

        /// <summary>Joint-correction categories (index 0–7 → on) of every character, one entry "Joint correction".</summary>
        public OpResult SetCorrection(IEnumerable<string> ids, IReadOnlyDictionary<int, bool> flags, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            var plan = new List<(string, object)>();
            foreach (var kv in flags ?? new Dictionary<int, bool>())
            {
                if (kv.Key < 0 || kv.Key > 7) return OpResult.BadArgs("correction", "indices 0–7");
                plan.Add(("chara.correction[" + kv.Key + "]", kv.Value));
            }
            if (plan.Count == 0) return OpResult.NothingToDo();
            return SetMany(Ids(nodes), plan, Labels.JointCorrection, o, _ => "correction");
        }

        // ------------------------------------------------------------ change character (U27)

        /// <summary>
        /// Replaces the appearance of every character with the card or .hs2nchara at <paramref name="path"/> (one entry "Change
        /// character"): the node keeps its id, transform, children, animation, FK / IK, face, look, state (clothes states are realized
        /// against the new outfit's state types, the stored values kept) and name; any sex. Undo swaps the appearance back.
        /// Value = the import's findings (FindingList).
        /// </summary>
        public OpResult ChangeCharacter(IEnumerable<string> ids, string path, KeepOptions keep = null, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "keys", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "character node ids", "no character keys");
            var found = new FindingList();
            var cc = ReadCharacterFile(path, found, out _, out fail);
            if (cc == null) return fail;
            rt.ApplyNow();
            var tx = Open(Labels.ChangeCharacter, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var n in nodes)
                {
                    // a copy of the file's subtree, placed in the scene file under the node too (what a save of the character
                    // extracts before the next scene save), and taken back on undo
                    var a = new CharaAppearance();
                    var res = tx.Execute(new SetAppearanceCommand(n.Id, a, Labels.ChangeCharacter, cc));
                    if (res.Status == CommandStatus.Error) return OpResult.From(res, "path");
                    r.Record(n.Id, "appearance", res.Status);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            r.Value = found;
            return r;
        }

        // ------------------------------------------------------------ use card state

        /// <summary>
        /// Re-applies the card's Status block to every character, one entry "Use card state": the card is the
        /// node's card source, or <paramref name="cardPath"/> (required for characters stored as documents).
        /// </summary>
        public OpResult UseCardState(IEnumerable<string> ids, string cardPath = null, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "keys", out var fail);
            if (nodes == null) return fail;
            var tx = Open(Labels.UseCardState, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var n in nodes)
                {
                    string p = cardPath ?? n.Appearance?.Card?.FullPath;
                    if (string.IsNullOrEmpty(p)) return OpResult.BadArgs("path", "the card path", "character " + n.Id + " has no card source: give the card path");
                    var res = ApplyCardState(tx, n.Id, p, r.Warnings);
                    if (res != null && res.Status == CommandStatus.Error) return OpResult.From(res, "path");
                    r.Record(n.Id, "chara.state", res?.Status ?? CommandStatus.NoOp);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>The card's Status block as sets of chara.state / face / look members inside <paramref name="tx"/>.</summary>
        CommandResult ApplyCardState(Transaction tx, string id, string cardPath, List<string> warnings)
        {
            var n = id != null ? Model.Find(id) : null;
            if (n == null || !(n.State is CharaNodeState cur)) return CommandResult.Error(CommandCodes.NotFound, "no character " + id);
            Card card;
            try { card = Card.Load(cardPath); }
            catch (Exception e) { return CommandResult.Error(CommandCodes.BadArgs, "card not readable: " + e.GetType().Name + ": " + e.Message); }
            var state = DocClone.Clone(cur);
            var report = new ImportReport { Kind = "chara" };
            if (!CardStateImport.Apply(card, n.Appearance?.Stored?.Coord, state, report))
            {
                warnings.Add("useCardState: the card has no readable Status block; nothing applied");
                return CommandResult.NoOp(null);
            }
            CommandResult last = CommandResult.NoOp(null);
            foreach (var (path, value) in new (string, object)[] { ("chara.state", state.State), ("chara.face", state.Face), ("chara.look", state.Look) })
            {
                var s = tx.Set(id, path, value);
                if (s.Status == CommandStatus.Error) return s;
                if (s.Status == CommandStatus.Ok) last = s;
            }
            return last;
        }
    }
}
