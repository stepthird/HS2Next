// StudioOps, stains: the edits of a character's wet block, shared by the commands and the panel. Each edit is one WetEditCommand, so one history entry; an edit starts from the
// runtime's clock and puddles, captured into the document first, so an undo brings back what was shown. A request is checked first; a refusal stores nothing and answers:
//   not_found    a node, a stain, no current stain, a ray that meets nothing on the character
//   wrong_kind   a node that is no character
//   bad_args     a value of the wrong kind, a member that is not one, an unknown preset, an edit of a kept stain, a limit (the word wet.limit in the message)
//   unavailable  a character that is not built, for what needs one
//   unsupported  a bake with no graphics device; a body without the bones of its frame (the word wet.noFrame in the message)
//   busy         as every edit
// Without a device reads and every edit that needs no bake work; a value that would need one is stored and the maps stay as they are until the stain is applied again.
// Values given while a character has no stain wait for its next stain: they live here, not in the document. A kept stain takes no further edit but its removal.
// Options: a MergeKey chains the edits of one value into one history entry; Record None applies without an entry and keeps the history; an open transaction (a UI drag, a
// bridge batch) takes the edit as a step.
// Result: Value = the name of the stain made (attach, keep: WetAttached and string), the number removed (remove, remove all), the height and source of the floor (floor).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Wet;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>A named stain from a script. What is left null keeps what the stain would have: the values waiting for it, else the script's defaults.</summary>
    public sealed class WetStainRequest
    {
        public string Name = WetEdits.DefaultName;
        public string Preset = WetPreset.Water;
        /// <summary>The ray whose first surface on the character is the origin; null: the default origin.</summary>
        public Ray? Ray;
        /// <summary>How far the ray runs.</summary>
        public float Length = StudioOps.WetRayLength;
        public float? Radius, Spread, Tail;
        public bool? SkinDrips;
        /// <summary>Nothing but the name was given: a stain of that name is applied again, not replaced.</summary>
        public bool NameOnly;
    }

    /// <summary>What an attach made: its stain's name and the floor measured under the character, null when it could not be.</summary>
    public sealed class WetAttached
    {
        public string Stain;
        public float? Floor;
    }

    /// <summary>The floor of a character after an edit or a read.</summary>
    public sealed class WetFloorInfo
    {
        public float Y;
        /// <summary>One of <see cref="WetFloorSources"/>.</summary>
        public string Source;
    }

    public sealed partial class StudioOps
    {
        /// <summary>How far a ray runs when nothing says.</summary>
        public const float WetRayLength = 1000f;
        const string WetNoFrame = "wet.noFrame";

        readonly Dictionary<string, List<KeyValuePair<string, object>>> wetWaiting = new Dictionary<string, List<KeyValuePair<string, object>>>(StringComparer.Ordinal);
        string wetLastAttached;

        /// <summary>The part that runs the stains; null on a runtime without the standard parts.</summary>
        public WetPart Wet => rt.GetPart<WetPart>();

        /// <summary>The view's marker at a stain's origin. Runtime only; a capture never shows it.</summary>
        public bool WetShowMarker { get; set; } = true;

        // ------------------------------------------------------------ reads

        /// <summary>The character a command without a key means: the last one attached, else the selection's first character; null when none.</summary>
        public string WetDefaultCharacter()
        {
            if (wetLastAttached != null && Model.Find(wetLastAttached)?.Kind == NodeKind.Chara) return wetLastAttached;
            foreach (string id in Session.Selection.Ids)
                if (Model.Find(id)?.Kind == NodeKind.Chara) return id;
            return null;
        }

        /// <summary>The block of a character as it stands in the document, not to be changed; null for anything else. An edit replaces the object.</summary>
        public CharaWet WetBlock(string nodeId) => (Model.Find(nodeId)?.State as CharaNodeState)?.Wet;

        /// <summary>The id of the stain named <paramref name="name"/> of a character; null when it has none.</summary>
        public string WetStainId(string nodeId, string name) => WetBlock(nodeId)?.FindByName(name)?.Id;

        /// <summary>How a character's stains stand now; null for a node that is no character. A character with no rig answers from its document.</summary>
        public WetCharaStatus WetStatus(string nodeId)
        {
            var block = WetBlock(nodeId);
            if (block == null) return null;
            var s = Wet?.StatusOf(nodeId);
            if (s != null) return s;
            s = new WetCharaStatus { FloorSource = WetFloorSources.None, Puddles = block.Puddles.Count };
            foreach (var x in block.Stains) s.Dormant += x.Maps.Count;
            var cur = block.CurrentStain;
            if (cur == null) return s;
            s.Current = cur.Name;
            s.T = cur.AppliedT;
            s.Running = cur.Running && !cur.Kept;
            var shape = cur.Shape.Applied();
            s.Radius = WetMath.DrawnRadius(s.T, shape.Spread, shape.Radius);
            return s;
        }

        /// <summary>How many values wait for a character's next stain.</summary>
        public int WetWaiting(string nodeId) => wetWaiting.TryGetValue(nodeId, out var l) ? l.Count : 0;

        // ------------------------------------------------------------ edits

        /// <summary>
        /// Clears the character's stains and puddles and adds one stain, dry, from where <paramref name="ray"/> meets it, or from the default origin with none (label «Add stain»);
        /// Value = a <see cref="WetAttached"/>. The character is then what a command without a key means.
        /// </summary>
        public OpResult WetAttach(string nodeId, Ray? ray, float length, string preset, ExecuteOptions o = default)
        {
            var part = WetChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            if (!WetPreset.IsKnown(preset)) return WetBadPreset(preset);
            fail = WetBuilt(nodeId);
            if (fail != null) return fail;
            part.CaptureNow(nodeId);
            wetWaiting.TryGetValue(nodeId, out var waiting);
            var r = WetOutcome(part.AttachRay(nodeId, ray, preset, WetEdits.DefaultName, Labelled(o, Labels.WetAdd), (b, s) => WetTakeWaiting(b, s, waiting), length));
            if (!r.Ok) return r;
            wetWaiting.Remove(nodeId);
            wetLastAttached = nodeId;
            rt.ApplyNow();
            var rig = part.RigOf(nodeId);
            rig?.MeasureNow();
            bool floor = rig != null && rig.Floor.Source != WetFloorSources.None;
            r.Value = new WetAttached { Stain = WetEdits.DefaultName, Floor = floor ? rig.Floor.Y : (float?)null };
            return r;
        }

        /// <summary>
        /// Adds a named stain, dry, or replaces the stain of that name, which keeps its place (label «Add stain»); it becomes current only when there is none. With only the name
        /// given, a stain of that name is applied again.
        /// </summary>
        public OpResult WetNamedStain(string nodeId, WetStainRequest q, ExecuteOptions o = default)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            var part = WetChara(nodeId, out var st, out var fail);
            if (part == null) return fail;
            if (string.IsNullOrEmpty(q.Name)) return OpResult.BadArgs("name", "a name");
            if (q.Name.Length > WetLimits.MaxName) return OpResult.BadArgs("name", "at most " + WetLimits.MaxName + " characters", WetFindings.Limit + ": a name of more than " + WetLimits.MaxName + " characters");
            var old = st.Wet.FindByName(q.Name);
            if (old != null && q.NameOnly) return WetApplyAgain(nodeId, old.Id, o);
            if (old != null && old.Kept) return WetKeptStain(old, "name");
            if (!WetPreset.IsKnown(q.Preset)) return WetBadPreset(q.Preset);
            if (old == null && st.Wet.Stains.Count >= WetLimits.MaxStains) return OpResult.BadArgs("name", null, WetFindings.Limit + ": more than " + WetLimits.MaxStains + " stains");
            fail = WetBuilt(nodeId);
            if (fail != null) return fail;
            var block = WetCopy(nodeId);
            old = block.FindByName(q.Name);
            var s = WetEdits.NewStain(block, q.Name, q.Preset);
            s.Shape.Radius = WetEdits.ScriptRadius;
            s.Shape.Spread = WetEdits.ScriptSpread;
            s.Drips.Tail = WetEdits.ScriptTail;
            wetWaiting.TryGetValue(nodeId, out var waiting);
            WetTakeWaiting(block, s, waiting);
            s.Name = q.Name;
            if (q.Radius != null) s.Shape.Radius = q.Radius.Value;
            if (q.Spread != null) s.Shape.Spread = q.Spread.Value;
            if (q.Tail != null) s.Drips.Tail = q.Tail.Value;
            if (q.SkinDrips != null) s.Drips.Skin = q.SkinDrips.Value;
            if (q.Ray != null && !part.OriginOf(nodeId, q.Ray.Value, s.Origin, q.Length)) return OpResult.NotFound("the ray meets nothing on the character", "target");
            if (old != null) WetEdits.Replace(block, old, s);
            else WetEdits.Add(block, s);
            fail = WetBaked(part, nodeId, block, s, false);
            if (fail != null) return fail;
            var r = WetEdit(nodeId, block, Labels.WetAdd, o);
            if (r.Ok) wetWaiting.Remove(nodeId);
            return r;
        }

        /// <summary>The panel's «Add stain»: a dry stain of <paramref name="preset"/> with the default origin and the block's default values, made current. Value = its id.</summary>
        public OpResult WetAdd(string nodeId, string preset, string name = null, ExecuteOptions o = default)
        {
            var part = WetChara(nodeId, out var st, out var fail);
            if (part == null) return fail;
            if (!WetPreset.IsKnown(preset)) return WetBadPreset(preset);
            if (st.Wet.Stains.Count >= WetLimits.MaxStains) return OpResult.BadArgs("name", null, WetFindings.Limit + ": more than " + WetLimits.MaxStains + " stains");
            fail = WetBuilt(nodeId);
            if (fail != null) return fail;
            var block = WetCopy(nodeId);
            var s = WetEdits.NewStain(block, WetEdits.UniqueName(block, string.IsNullOrEmpty(name) ? preset : name), preset);
            wetWaiting.TryGetValue(nodeId, out var waiting);
            WetTakeWaiting(block, s, waiting);
            WetEdits.Add(block, s, true);
            fail = WetBaked(part, nodeId, block, s, false);
            if (fail != null) return fail;
            var r = WetEdit(nodeId, block, Labels.WetAdd, o);
            if (!r.Ok) return r;
            wetWaiting.Remove(nodeId);
            r.Value = s.Id;
            return r;
        }

        /// <summary>A stain becomes current; with <paramref name="start"/> it runs from 0 (label «Start stain», else «Stain»). What is on the cloth stays.</summary>
        public OpResult WetUse(string nodeId, string stainId, bool start, ExecuteOptions o = default)
        {
            var block = WetEditable(nodeId, stainId, start, out var s, out var fail);
            if (block == null) return fail;
            block.Current = s.Id;
            if (start) WetEdits.Start(s);
            return WetEdit(nodeId, block, start ? Labels.WetStart : Labels.WetEdit, o);
        }

        /// <summary>A stain, the current one with no id, runs from 0 (label «Start stain»); <paramref name="loop"/> given sets its loop.</summary>
        public OpResult WetStart(string nodeId, string stainId, bool? loop = null, ExecuteOptions o = default)
        {
            var block = WetEditable(nodeId, stainId, true, out var s, out var fail);
            if (block == null) return fail;
            WetEdits.Start(s, loop);
            return WetEdit(nodeId, block, Labels.WetStart, o);
        }

        /// <summary>The panel's «Full»: a stain stands at the end of its spread at once, not running (label «Stain»).</summary>
        public OpResult WetFull(string nodeId, string stainId, ExecuteOptions o = default)
        {
            var block = WetEditable(nodeId, stainId, true, out var s, out var fail);
            if (block == null) return fail;
            WetEdits.Full(s);
            return WetEdit(nodeId, block, Labels.WetEdit, o);
        }

        /// <summary>A stain, or with no id every stain that is not kept, goes back to 0 and stops; drops go, and with <paramref name="puddles"/> their puddles too (label «Dry stain»).</summary>
        public OpResult WetDry(string nodeId, string stainId, bool puddles, ExecuteOptions o = default)
        {
            CharaWet block;
            WetStain s = null;
            OpResult fail;
            if (stainId != null)
            {
                block = WetEditable(nodeId, stainId, true, out s, out fail);
                if (block == null) return fail;
            }
            else
            {
                if (WetChara(nodeId, out _, out fail) == null) return fail;
                block = WetCopy(nodeId);
            }
            WetEdits.Dry(block, s, puddles);
            return WetEdit(nodeId, block, Labels.WetDry, o);
        }

        /// <summary>Takes away every puddle of the character that is not frozen (label «Clear puddles»).</summary>
        public OpResult WetClearPuddles(string nodeId, ExecuteOptions o = default)
        {
            if (WetChara(nodeId, out _, out var fail) == null) return fail;
            var block = WetCopy(nodeId);
            WetEdits.ClearPuddles(block);
            return WetEdit(nodeId, block, Labels.WetClearPuddles, o);
        }

        /// <summary>Freezes the current stain as a layer (label «Keep layer»); a new current stain with the same origin and values starts dry. Value = its name.</summary>
        public OpResult WetKeep(string nodeId, ExecuteOptions o = default)
        {
            var block = WetEditable(nodeId, null, true, out var s, out var fail);
            if (block == null) return fail;
            var next = WetEdits.Keep(block, s);
            var r = WetEdit(nodeId, block, Labels.WetKeep, o);
            if (r.Ok) r.Value = next.Name;
            return r;
        }

        /// <summary>Takes a stain, the current one with no id, and its puddles away (label «Remove stain»); Value = 1.</summary>
        public OpResult WetRemove(string nodeId, string stainId, ExecuteOptions o = default)
        {
            var block = WetEditable(nodeId, stainId, false, out var s, out var fail);
            if (block == null) return fail;
            WetEdits.Remove(block, s);
            var r = WetEdit(nodeId, block, Labels.WetRemove, o);
            if (r.Ok) r.Value = 1;
            return r;
        }

        /// <summary>Takes the character's whole block away (label «Remove stain»); Value = the number of stains that went.</summary>
        public OpResult WetRemoveAll(string nodeId, ExecuteOptions o = default)
        {
            if (WetChara(nodeId, out _, out var fail) == null) return fail;
            int count = WetCopy(nodeId).Stains.Count;
            var r = WetEdit(nodeId, new CharaWet(), Labels.WetRemove, o);
            if (r.Ok) r.Value = count;
            return r;
        }

        /// <summary>Moves a stain in the order, the last one on top (label «Stain»). A place outside the order is refused; the place it holds is no change.</summary>
        public OpResult WetMove(string nodeId, string stainId, int index, ExecuteOptions o = default)
        {
            var block = WetEditable(nodeId, stainId, false, out var s, out var fail);
            if (block == null) return fail;
            if (index < 0 || index >= block.Stains.Count) return OpResult.BadArgs("index", "0.." + (block.Stains.Count - 1));
            return WetEdits.Move(block, s, index) ? WetEdit(nodeId, block, Labels.WetEdit, o) : OpResult.NothingToDo();
        }

        /// <summary>
        /// Sets values by name (<see cref="WetEdits.Paths"/>, the names scene scripts use, <see cref="WetEdits.ShowMarker"/>) in one entry (label «Stain», «Floor» when only the floor
        /// changes, «Move origin» when the origin does). A stain's members go to the stain with <paramref name="stainId"/>, else the current one. A change of shape or origin bakes
        /// when a device and a built character allow it. With no stain on the character the values wait for its next one. Changed: the names that changed, as given.
        /// </summary>
        public OpResult WetSet(string nodeId, string stainId, IReadOnlyList<KeyValuePair<string, object>> values, ExecuteOptions o = default)
        {
            var part = WetChara(nodeId, out var st, out var fail);
            if (part == null) return fail;
            if (values == null || values.Count == 0) return OpResult.BadArgs("values", "a member to set");
            bool? marker = null;
            var plan = new List<(string name, string path, object value)>();
            bool floor = false, height = false, stainMember = false;
            foreach (var kv in values)
            {
                if (kv.Key == WetEdits.ShowMarker)
                {
                    if (!(kv.Value is bool on)) return OpResult.BadArgs("values." + kv.Key, "true or false");
                    marker = on;
                    continue;
                }
                string path = WetEdits.PathOf(kv.Key);
                if (path == null)
                    return OpResult.BadArgs("values." + kv.Key, "a member of the stain or the block", WetEdits.IsNoMember(kv.Key) ? kv.Key + " has its own command" : "no member \"" + kv.Key + "\"");
                plan.Add((kv.Key, path, kv.Value));
                floor |= WetEdits.IsFloor(path);
                height |= path == "floor.height";
                stainMember |= WetEdits.OnStain(path);
            }
            var r = OpResult.NothingToDo();
            if (st.Wet.Stains.Count == 0 && stainId == null)
            {
                fail = WetWait(nodeId, plan, r);
                if (fail != null) return fail;
                WetSetMarker(marker, r);
                return r;
            }
            bool built = WetBuilt(nodeId) == null;
            var block = WetCopy(nodeId);
            var stain = stainId != null ? block.Find(stainId) : block.CurrentStain;
            if (stainId != null && stain == null) return OpResult.NotFound("no stain " + stainId, "name");
            if (stainMember && stain == null) return OpResult.NotFound("the character has no current stain", "name");
            if (stainMember && stain.Kept) return WetKeptStain(stain, "name");
            var before = stain == null ? null : block.Clone().Find(stain.Id);
            var reach = block.Clone().Targets;
            // a fixed floor chosen with no height takes the floor of now
            float present = floor && built ? part.FloorEdit(nodeId, block.Clone(), out _) - block.Floor.AppliedOffset : 0f;
            bool wasFixed = block.Floor.AppliedMode == WetFloorValues.ModeFixed, origin = false, other = false, floorChanged = false;
            foreach (var (name, path, value) in plan)
            {
                string wrong = WetEdits.Set(block, stain, path, value, out bool changed);
                if (wrong != null) return OpResult.BadArgs("values." + name, null, wrong);
                if (!changed) { r.NoOp.Add(name); continue; }
                r.Changed.Add(name);
                if (WetEdits.IsFloor(path)) floorChanged = true;
                else if (WetEdits.IsOrigin(path)) origin = true;
                else other = true;
            }
            if (r.Changed.Count == 0)
            {
                WetSetMarker(marker, r);
                return r;
            }
            if (stain != null && built && part.CanBake && WetPart.NeedsBake(before, stain))
            {
                fail = WetBaked(part, nodeId, block, stain, false);
                if (fail != null) return fail;
            }
            // a kind switched on takes the stains there are, in this one entry; one that cannot bake now stays for «Apply again» and the switch still stands
            if (built && part.CanBake && block.Targets.Widens(reach))
                foreach (var s in block.Stains)
                    if (!s.Kept && s.Maps.Count > 0) part.Bake(nodeId, block, s, true);
            if (floorChanged)
            {
                if (built && !height && !wasFixed && block.Floor.AppliedMode == WetFloorValues.ModeFixed) block.Floor.Height = present;
                if (built) part.FloorEdit(nodeId, block, out _);
            }
            var done = WetEdit(nodeId, block, origin ? Labels.WetOrigin : floorChanged && !other ? Labels.WetFloor : Labels.WetEdit, o);
            if (!done.Ok) return done;
            done.Changed.AddRange(r.Changed);
            done.NoOp.AddRange(r.NoOp);
            WetSetMarker(marker, done);
            return done;
        }

        /// <summary>One value by its name, as <see cref="WetSet(string, string, IReadOnlyList{KeyValuePair{string, object}}, ExecuteOptions)"/>.</summary>
        public OpResult WetSet(string nodeId, string stainId, string name, object value, ExecuteOptions o = default) =>
            WetSet(nodeId, stainId, new[] { new KeyValuePair<string, object>(name, value) }, o);

        /// <summary>
        /// One step of a value dragged. A value the maps do not hold is an edit with <paramref name="mergeKey"/>, shown at once. One that needs a bake shows as a preview with no picture,
        /// at most every <see cref="WetPart.PreviewEvery"/> of real time, and changes no document: the commit is a <see cref="WetSet(string, string, string, object, ExecuteOptions)"/>
        /// with the last value and the same key, and a drag given up is a <see cref="WetDragCancel"/>.
        /// </summary>
        public OpResult WetDrag(string nodeId, string stainId, string name, object value, string mergeKey, ExecuteOptions o = default)
        {
            var part = WetChara(nodeId, out var st, out var fail);
            if (part == null) return fail;
            o.MergeKey = mergeKey;
            string path = WetEdits.PathOf(name);
            if (path == null || !WetEdits.IsShape(path) || st.Wet.Stains.Count == 0 || !part.CanBake || WetBuilt(nodeId) != null) return WetSet(nodeId, stainId, name, value, o);
            var block = st.Wet.Clone();
            var stain = stainId != null ? block.Find(stainId) : block.CurrentStain;
            if (stain == null) return OpResult.NotFound("the character has no current stain", "name");
            if (stain.Kept) return WetKeptStain(stain, "name");
            var before = block.Clone().Find(stain.Id);
            string wrong = WetEdits.Set(block, stain, path, value, out bool changed);
            if (wrong != null) return OpResult.BadArgs("values." + name, null, wrong);
            if (!changed) return OpResult.NothingToDo();
            if (!WetPart.NeedsBake(before, stain)) return WetSet(nodeId, stainId, name, value, o);
            string why = part.Preview(nodeId, block, stain);
            return why == null ? OpResult.Success() : WetRefusal(why);
        }

        /// <summary>A drag given up: the character shows what its document says.</summary>
        public void WetDragCancel(string nodeId)
        {
            var block = WetBlock(nodeId);
            if (block != null) Wet?.RigOf(nodeId)?.Sync(block);
        }

        /// <summary>
        /// Sets what is given of the floor, or only reads it with nothing given (label «Floor»); the puddles move with it, so an undo brings them back. Value = a
        /// <see cref="WetFloorInfo"/>. A character with no stain keeps these as waiting values.
        /// </summary>
        public OpResult WetFloorSet(string nodeId, string mode = null, float? offset = null, float? height = null, ExecuteOptions o = default)
        {
            var part = WetChara(nodeId, out _, out var fail);
            if (part == null) return fail;
            var values = new List<KeyValuePair<string, object>>();
            if (mode != null) values.Add(new KeyValuePair<string, object>("floor.mode", mode));
            if (offset != null) values.Add(new KeyValuePair<string, object>("floor.offset", offset.Value));
            if (height != null) values.Add(new KeyValuePair<string, object>("floor.height", height.Value));
            var r = values.Count > 0 ? WetSet(nodeId, null, values, Labelled(o, Labels.WetFloor)) : OpResult.NothingToDo();
            if (!r.Ok) return r;
            var shown = WetBlock(nodeId).Clone();
            if (wetWaiting.TryGetValue(nodeId, out var waiting)) WetTakeWaiting(shown, new WetStain(), waiting);
            var info = new WetFloorInfo { Source = WetFloorSources.None };
            if (WetBuilt(nodeId) == null)
            {
                info.Y = part.FloorEdit(nodeId, shown, out string source);
                info.Source = source;
            }
            r.Value = info;
            return r;
        }

        /// <summary>A stain's origin becomes a point in a bone's space, from a pick (label «Move origin»); the stain bakes again.</summary>
        public OpResult WetOrigin(string nodeId, string stainId, string bone, Vec3 local, ExecuteOptions o = default) =>
            WetSet(nodeId, stainId, new[]
            {
                new KeyValuePair<string, object>("origin.mode", WetOriginValues.ModeBone),
                new KeyValuePair<string, object>("origin.bone", bone ?? ""),
                new KeyValuePair<string, object>("origin.local", local),
            }, Labelled(o, Labels.WetOrigin));

        /// <summary>A stain goes back to the default origin (label «Move origin»); the stain bakes again.</summary>
        public OpResult WetOriginDefault(string nodeId, string stainId, ExecuteOptions o = default) =>
            WetSet(nodeId, stainId, new[]
            {
                new KeyValuePair<string, object>("origin.mode", WetOriginValues.ModeDefault),
                new KeyValuePair<string, object>("origin.bone", ""),
                new KeyValuePair<string, object>("origin.local", Vec3.Zero),
            }, Labelled(o, Labels.WetOrigin));

        /// <summary>«Apply again»: bakes a stain on what is there now, with the current pose, for the parts without a live map (label «Stain»). Its clock stays.</summary>
        public OpResult WetApplyAgain(string nodeId, string stainId, ExecuteOptions o = default)
        {
            var block = WetEditable(nodeId, stainId, true, out var s, out var fail);
            if (block == null) return fail;
            fail = WetBuilt(nodeId) ?? WetBaked(Wet, nodeId, block, s, true);
            return fail ?? WetEdit(nodeId, block, Labels.WetEdit, o);
        }

        // ------------------------------------------------------------ helpers

        // The part, and the node's state; a node that is no character is refused.
        WetPart WetChara(string nodeId, out CharaNodeState state, out OpResult fail)
        {
            state = null;
            var n = Find(nodeId, "key", out fail);
            if (n == null) return null;
            state = n.State as CharaNodeState;
            if (state == null)
            {
                fail = OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", not a character", "key", "chara");
                return null;
            }
            var part = Wet;
            if (part == null) fail = OpResult.Unsupported("this runtime has no stains");
            return part;
        }

        // Null when the character stands built.
        OpResult WetBuilt(string nodeId)
        {
            rt.ApplyNow();
            var r = rt.FindChara(nodeId);
            if (r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root) return null;
            return OpResult.Unavailable("character " + nodeId + " is not built", "key", r?.State.ToString() ?? "NotRealized");
        }

        // A copy of the block to edit, with the runtime's clock and puddles in it.
        CharaWet WetCopy(string nodeId)
        {
            rt.ApplyNow();
            Wet.CaptureNow(nodeId);
            return WetBlock(nodeId).Clone();
        }

        // That copy and one of its stains, the current one with no id. `active`: an edit a kept stain does not take.
        CharaWet WetEditable(string nodeId, string stainId, bool active, out WetStain stain, out OpResult fail)
        {
            stain = null;
            if (WetChara(nodeId, out _, out fail) == null) return null;
            var block = WetCopy(nodeId);
            stain = stainId != null ? block.Find(stainId) : block.CurrentStain;
            if (stain == null)
            {
                fail = OpResult.NotFound(stainId != null ? "no stain " + stainId : "the character has no current stain", "name");
                return null;
            }
            if (active && stain.Kept)
            {
                fail = WetKeptStain(stain, "name");
                return null;
            }
            return block;
        }

        OpResult WetEdit(string nodeId, CharaWet block, string label, ExecuteOptions o)
        {
            var r = OpResult.From(Session.Commands.Execute(new WetEditCommand(nodeId, block, label), Labelled(o, label)));
            if (r.Ok) rt.ApplyNow();
            return r;
        }

        // A bake of a stain of a block not yet in the model; null when it went well.
        OpResult WetBaked(WetPart part, string nodeId, CharaWet block, WetStain stain, bool missing)
        {
            string why = part.Bake(nodeId, block, stain, missing);
            return why == null ? null : WetRefusal(why);
        }

        // What a bake's answer is for a caller: command codes are a closed set, so the frame's word goes in the message.
        static OpResult WetRefusal(string why)
        {
            switch (why)
            {
                case CommandCodes.Unsupported: return OpResult.Unsupported("a stain needs a graphics device");
                case CommandCodes.NotFound: return OpResult.NotFound("no origin on the character", "name");
                default: return OpResult.Unsupported(why + ": this body has no frame for a stain");
            }
        }

        static OpResult WetOutcome(CommandResult r) =>
            r != null && r.Status == CommandStatus.Error && r.Code == WetNoFrame ? WetRefusal(r.Code) : OpResult.From(r);

        static OpResult WetBadPreset(string preset) => OpResult.BadArgs("preset", string.Join(" or ", WetPreset.Names), "no preset \"" + preset + "\"");

        static OpResult WetKeptStain(WetStain s, string arg) => OpResult.BadArgs(arg, "a stain that is not kept", "the stain \"" + s.Name + "\" is kept: it takes no further edit");

        void WetSetMarker(bool? marker, OpResult r)
        {
            if (marker == null) return;
            if (WetShowMarker == marker.Value) r.NoOp.Add(WetEdits.ShowMarker);
            else r.Changed.Add(WetEdits.ShowMarker);
            WetShowMarker = marker.Value;
        }

        // Values for a stain that does not exist yet: each is tried on a stain of its own, so a wrong one is refused now and nothing waits.
        OpResult WetWait(string nodeId, List<(string name, string path, object value)> plan, OpResult r)
        {
            var block = new CharaWet();
            var stain = new WetStain();
            foreach (var (name, path, value) in plan)
            {
                string wrong = WetEdits.Set(block, stain, path, value, out _);
                if (wrong != null) return OpResult.BadArgs("values." + name, null, wrong);
            }
            if (!wetWaiting.TryGetValue(nodeId, out var waiting)) wetWaiting[nodeId] = waiting = new List<KeyValuePair<string, object>>();
            foreach (var (name, path, value) in plan)
            {
                waiting.RemoveAll(x => x.Key == path);
                waiting.Add(new KeyValuePair<string, object>(path, value));
                r.Changed.Add(name);
            }
            return null;
        }

        static void WetTakeWaiting(CharaWet block, WetStain stain, List<KeyValuePair<string, object>> waiting)
        {
            if (waiting == null) return;
            foreach (var kv in waiting) WetEdits.Set(block, stain, kv.Key, kv.Value, out _);
        }
    }
}
