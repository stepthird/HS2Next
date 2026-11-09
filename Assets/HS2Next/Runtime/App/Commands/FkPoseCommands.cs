// FK, IK and pose commands of the bridge (HS2 reply shapes: Appendix B):
//   fk.list     the FK bones of a built character in Studio's listBones form (one entry per FK bone: the primary rows and
//               the additional nodes present on the character, ascending key; a sync partner rides on its primary's
//               guide in Studio and has no entry of its own), values from the model (rounded, or exact doubles)
//   fk.set      model values by bone name (model key, partner key or transform name), stored under the primary key
//               (FkOps.SetBones: one entry "FK bones")
//   fk.enable   FK on / off, or groups by name (FkOps.SetEnabled / SetGroup with the missing-value capture), one entry "FK"; the limb
//               names RightArm, LeftArm, RightLeg and LeftLeg switch the Body group, with a warning
//   fk.capture  the current pose of Body, Neck and the hands → fk.bones, groups body + neck and FK on, speed 0 (one entry
//               "FK from pose"; FkOps.CurrentValues)
//   ik.enable   IK on / off, or IK groups by name (IkOps.SetEnabled / SetGroups with the missing-target capture and the
//               exclusive preset), one entry "IK"
//   pose.load   an HS2 pose file through PoseImport.ApplyFile (with the 3.4 IK import; one entry "Load pose"; joins a batch
//               transaction), or an HS2Next pose fragment (.hs2npose, by its manifest) through PoseFragmentOps.Load
//   pose.list   the pose files under <hs2Root>/UserData/studio/pose (PoseLibrary.Scan) and / or <UserData>/pose (source)
// Phase 3.4 (package C) adds, on package B's operations:
//   ik.list     the 13 targets (HS2 order and shapes; the effective value: the model's, else the runtime snapshot), the solve
//               info and the owned bones
//   ik.set      target values by name, root or world space (IkOps.SetTargets, "IK targets")
//   ik.copy     animation / current pose / imported block → IK targets (KinematicCopies)
//   fk.copy     animation / current pose → FK values (KinematicCopies)
//   ik.bake     the solved pose → Body-group FK + fk.hipsPosition, FK on, IK off ("Bake IK")
//   pose.save   the character's pose as an hs2next.pose fragment under <UserData>/pose (PoseFragmentOps.Save; no history)
// FK and IK may both be on: fk.enable / ik.enable and the copies' `enable` switch the other mode off only with the
// app preset kinematics.exclusive (IkOptions.Exclusive, K3).
// Item forms (Phase 3.1 package B): `key` may be a Studio item. Its FK bones are the
// names of the row's bone list found in the instance; values are item.fk.bones[name] (local Euler degrees); there are no
// groups. fk.list lists the found bones (rot = the stored value, or the base rotation FK applies without one); fk.set
// stores values of found bones ("Item FK"); fk.enable switches item.fk.enabled ("Item FK"; on first stores the base rotation
// of every found bone without a value, the capture rule); fk.capture stores the current local rotation of every found bone
// and switches FK on ("FK from pose"; stopAnime sets item.anime.speed 0).
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Import.Pose;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class FkPoseCommands
    {
        static readonly string[] GroupNames = { "Hair", "Neck", "Breast", "Body", "RightHand", "LeftHand", "Skirt" };
        /// <summary>The limb names of the automation vocabulary that fk.enable takes as the Body group (with a warning).</summary>
        public static readonly string[] LimbNames = { "RightArm", "LeftArm", "RightLeg", "LeftLeg" };
        static readonly string[] IkGroupNames = { "body", "rightLeg", "leftLeg", "rightArm", "leftArm" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("fk.list", "The FK bones of a built character as Studio lists them (ascending key) with their model values, and the FK / IK / correction flags; or the FK bones of a built item.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a built character or item", true)
                    .Arg("group", ArgTypes.String, "only entries whose group text contains this, ignoring case (\"Body\" = column-0 bones; the limb bones are \"3\", \"5\", \"9\", \"17\")", false, "")
                    .Arg("exact", ArgTypes.Bool, "rot / pos as exact doubles and scale added (HS2 exact:true); else rounded to 4 decimals", false, false)
                    .Return("bones", ArgTypes.ArrayOf(ArgTypes.Object), "[{bone, key, group, groupFlags, level, boneWeight, rot, pos, scale?, name?}]")
                    .Return("fkEnabled", ArgTypes.Bool).Return("activeFK", ArgTypes.ArrayOf(ArgTypes.Bool), "Hair, Neck, Breast, Body, RightHand, LeftHand, Skirt")
                    .Return("hipsPosition", ArgTypes.Vec3, "characters: chara.fk.hipsPosition or null (cf_J_Hips' pos stays (0, hipsOffset, 0), HS2's shape)", false)
                    .WithFlags(CommandFlags.ReadOnly),
                FkList);
            CommandKit.Register(r, app, CommandKit.Spec("fk.set", "FK values (local Euler degrees) of bones by name, stored under the bone's primary key, and the hips position / offset (one history entry); for an item under the bone name.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a built character or item", true)
                    .Arg("bones", ArgTypes.Object, "{<bone name>: [x, y, z]}: model key, partner key or transform name (items: a found bone of the item's bone list); required unless hipsPosition or hipsOffset is given")
                    .Arg("hipsPosition", ArgTypes.Vec3, "characters: chara.fk.hipsPosition, the cf_J_Hips local position FK writes with the Body group ([x, y, z], or null = the clip's)")
                    .Arg("hipsOffset", ArgTypes.Float, "characters: chara.fk.hipsOffset (HS2_FKHeightAdjustUI's height offset)")
                    .Return("set", ArgTypes.Int).Return("missing", ArgTypes.ArrayOf(ArgTypes.String), "names that are no FK bone of the character", false)
                    .Return("hipsPosition", ArgTypes.Vec3, "characters: the stored hips position or null", false)
                    .Example("{\"cmd\":\"fk.set\",\"key\":\"<id>\",\"bones\":{\"cf_J_Spine01\":[10,0,0]}}")
                    .WithFlags(CommandFlags.Mutates),
                FkSet);
            CommandKit.Register(r, app, CommandKit.Spec("fk.enable", "Switches FK, or FK groups, on or off; bones that become applied without a value keep the pose you see (one history entry). IK is not touched.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a character, or a built item (no groups; its dynamic bones are off while FK is on)", true)
                    .Arg("on", ArgTypes.Bool, "the new state", false, true)
                    .Arg("groups", ArgTypes.ArrayOf(ArgTypes.String), "groups to switch instead of FK itself: Hair, Neck, Breast, Body, RightHand, LeftHand, Skirt (ignoring case); RightArm, LeftArm, RightLeg and LeftLeg switch the Body group, with a warning; characters only")
                    .Return("fkEnabled", ArgTypes.Bool).Return("ikEnabled", ArgTypes.Bool).Return("activeFK", ArgTypes.ArrayOf(ArgTypes.Bool))
                    .WithFlags(CommandFlags.Mutates),
                FkEnable);
            CommandKit.Register(r, app, CommandKit.Spec("fk.capture", "Copies the current pose of the Body, Neck and hand bones into FK, switches FK and its Body and Neck groups on (one history entry); for an item its FK bones' current rotations.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a built character or item", true)
                    .Arg("stopAnime", ArgTypes.Bool, "also set the animation speed to 0 (items: item.anime.speed)", false, true)
                    .Return("fkEnabled", ArgTypes.Bool)
                    .WithFlags(CommandFlags.Mutates),
                FkCapture);
            CommandKit.Register(r, app, CommandKit.Spec("ik.enable", "Switches IK, or IK groups, on or off; targets of groups that become applied without a value keep the pose you see (one history entry). With app.options kinematics.exclusive, IK on also switches FK off.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("on", ArgTypes.Bool, "the new state", false, true)
                    .Arg("groups", ArgTypes.ArrayOf(ArgTypes.String), "groups to switch instead of IK itself: body, rightLeg, leftLeg, rightArm, leftArm (ignoring case)")
                    .Return("ikEnabled", ArgTypes.Bool).Return("fkEnabled", ArgTypes.Bool).Return("activeIK", ArgTypes.ArrayOf(ArgTypes.Bool), "body, rightLeg, leftLeg, rightArm, leftArm")
                    .Return("captured", ArgTypes.ArrayOf(ArgTypes.String), "target names written by the capture rule")
                    .WithFlags(CommandFlags.Mutates),
                IkEnable);
            CommandKit.Register(r, app, CommandKit.Spec("pose.load", "Loads an HS2 pose file (.dat or PoseTools .png; IK targets converted) or an HS2Next pose file (.hs2npose) into a built character (one history entry).", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("path", ArgTypes.Path, "relative to <hs2Root>/UserData/studio/pose (a .hs2npose path: relative to <UserData>/pose), or absolute", true)
                    .Arg("parts", ArgTypes.ArrayOf(ArgTypes.String), "HS2Next pose files only: the parts to load (anime, hands, fk, ik, correction, face, look); default every part in the file")
                    .Arg("ik", ArgTypes.Bool, "HS2 files: convert and write the IK targets of a file with IK (false: 2.3's raw-only import, ikPending)", false, true)
                    .Return("sexMatches", ArgTypes.Bool).Return("loaded", ArgTypes.Bool).Return("name", ArgTypes.String).Return("object", ArgTypes.Object)
                    .Return("ikApproximated", ArgTypes.Bool, "IK targets were converted and written", false).Return("ikScale", ArgTypes.Int, "1, 10, or 0 (rejected / none)", false)
                    .Return("ikBodyShift", ArgTypes.Vec3, "the Body conversion's shift (root space) or null", false)
                    .Return("ikUnreachable", ArgTypes.ArrayOf(ArgTypes.String), "end targets beyond the reach clamp", false)
                    .Return("parts", ArgTypes.ArrayOf(ArgTypes.String), "HS2Next pose files: the parts set", false)
                    .Return("source", ArgTypes.String, "hs2 or hs2next")
                    .Return("findings", ArgTypes.ArrayOf(ArgTypes.Object))
                    .Example("{\"cmd\":\"pose.load\",\"key\":\"<id>\",\"path\":\"STANDING/Floor/Pose 1.dat\"}")
                    .WithFlags(CommandFlags.Mutates),
                PoseLoad);
            CommandKit.Register(r, app, CommandKit.Spec("pose.list", "The pose files under <hs2Root>/UserData/studio/pose and / or <UserData>/pose (or a subfolder), filtered by a text; paths are what pose.load takes.", "fk/pose")
                    .Arg("q", ArgTypes.String, "case-insensitive substring of the relative path or the pose name", false, "")
                    .Arg("dir", ArgTypes.Path, "a subfolder of the pose root(s) to scan", false, "")
                    .Arg("limit", ArgTypes.Int, "maximum entries (1–100000)", false, 200)
                    .Arg("source", ArgTypes.Enum("hs2", "hs2next", "all"), "hs2: HS2's pose library; hs2next: the HS2Next pose files of <UserData>/pose; all: both (HS2's first)", false, "hs2")
                    .Return("root", ArgTypes.Path).Return("poses", ArgTypes.ArrayOf(ArgTypes.Object), "[{path, name, sex, kind (dat / png / hs2npose), source}]")
                    .Return("roots", ArgTypes.Object, "{hs2, hs2next}: the scanned roots (null when not scanned)")
                    .Return("total", ArgTypes.Int, "matching poses").Return("truncated", ArgTypes.Bool, "more poses match than listed", false)
                    .WithFlags(CommandFlags.ReadOnly | CommandFlags.Slow).WithTimeout(120),
                PoseList);
            RegisterPhase34(r, app);
        }

        static CharaNodeState StateOf(TreeNode n) => n.State as CharaNodeState ?? throw new BridgeException(BridgeCodes.Internal, "character node without character state");

        static List<object> Bools(params bool[] a)
        {
            var l = new List<object>(a?.Length ?? 0);
            if (a != null) foreach (var b in a) l.Add(b);
            return l;
        }

        static bool[] FkFlags(FkGroups g) => g == null ? new bool[7] : new[] { g.Hair, g.Neck, g.Breast, g.Body, g.RightHand, g.LeftHand, g.Skirt };
        static bool[] IkFlags(IkGroups g) => g == null ? new bool[5] : new[] { g.Body, g.RightLeg, g.LeftLeg, g.RightArm, g.LeftArm };

        // ------------------------------------------------------------ fk.list

        /// <summary>Studio's `OIBoneInfo.BoneGroup` text of a group column (combined values print as numbers).</summary>
        public static string GroupText(int column)
        {
            switch (column)
            {
                case 0: return "Body";
                case 1: return "3";
                case 2: return "5";
                case 3: return "9";
                case 4: return "17";
                case 5: return "RightHand";
                case 6: return "LeftHand";
                case 7: case 8: case 9: return "Hair";
                case 10: return "Neck";
                case 11: case 12: return "Breast";
                case 13: return "Skirt";
                default: return GroupFlags(column).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Studio's `OIBoneInfo.BoneGroup` value of a group column.</summary>
        public static int GroupFlags(int column)
        {
            switch (column)
            {
                case 0: return 1;
                case 1: return 3;
                case 2: return 5;
                case 3: return 9;
                case 4: return 17;
                case 5: return 32;
                case 6: return 64;
                case 7: case 8: case 9: return 128;
                case 10: return 256;
                case 11: case 12: return 512;
                case 13: return 1024;
                default: return column >= 0 && column < 31 ? 1 << column : 0;
            }
        }

        static object FkList(CoreContext c)
        {
            var node = CommandKit.Node(c, "key");
            if (node.Kind == NodeKind.Item) return ItemFkList(c, node, c.Args.Get("group", "") ?? "", c.Args.Get("exact", false));
            var r = CommandKit.Built(c);
            string grp = c.Args.Get("group", "") ?? "";
            bool exact = c.Args.Get("exact", false);
            return FkList(c.App, r, grp, exact);
        }

        /// <summary>The fk.list reply of a built character (also used by the tests).</summary>
        public static BridgeReply FkList(AppCore app, CharaRealization r, string grp, bool exact)
        {
            var st = StateOf(r.Node);
            var fk = st.Fk ?? new CharaFkState();
            var table = FkDriver.TableFor(r);
            var entries = new List<(int key, int order, BridgeReply e)>();
            // one entry per FK bone (Studio: one BoneInfo per guide; the sync partner follows the primary's guide and
            // is not listed: BR-Chan's hair rows 101 / 107 / 115 are absent from HS2's list although the transforms exist)
            if (table != null)
                foreach (var b in table.Bones)
                {
                    if (b == null || !b.Bone) continue;
                    AddEntry(entries, b, b.Row, b.Bone, b.Key, b.PartnerKey, fk, grp, exact);
                }
            entries.Sort((x, y) => x.key != y.key ? x.key.CompareTo(y.key) : x.order.CompareTo(y.order));
            var bones = new List<object>(entries.Count);
            foreach (var e in entries) bones.Add(e.e);
            var fkc = FkDriver.Of(r);
            var applied = new bool[FkGroupInfo.Count];
            for (int g = 0; g < applied.Length; g++) applied[g] = fkc != null && fkc.Applied((FkGroup)g);
            return new BridgeReply
            {
                ["fkEnabled"] = fk.Enabled,
                ["enableFK"] = fk.Enabled,
                ["enableIK"] = st.Ik?.Enabled ?? false,
                ["activeFK"] = Bools(FkFlags(fk.Groups)),
                ["activeIK"] = Bools(IkFlags(st.Ik?.Groups)),
                ["expression"] = Bools(st.Correction ?? new bool[8]),
                ["bones"] = bones,
                ["hipsOffset"] = fk.HipsOffset,
                ["hipsPosition"] = HipsPosition(fk, exact),
                ["applied"] = Bools(applied),
            };
        }

        static void AddEntry(List<(int, int, BridgeReply)> entries, FkBone b, FkRow row, Transform t, string ownKey, string otherKey, CharaFkState fk,
            string grp, bool exact)
        {
            string text = GroupText(row.GroupColumn);
            if (grp.Length > 0 && text.IndexOf(grp, StringComparison.OrdinalIgnoreCase) < 0) return;
            Vec3 rot;
            if (!fk.Bones.TryGetValue(ownKey, out rot) && !(otherKey != null && fk.Bones.TryGetValue(otherKey, out rot))) rot = Vec3.Zero;
            var pos = row.No == 0 ? new Vec3(0f, fk.HipsOffset, 0f) : Vec3.Zero;
            var e = new BridgeReply
            {
                ["bone"] = t.name,
                ["key"] = row.No,
                ["group"] = text,
                ["groupFlags"] = GroupFlags(row.GroupColumn),
                ["level"] = row.Level,
                ["boneWeight"] = b.Used,
                ["rot"] = exact ? CommandKit.D3(rot) : CommandKit.R4(rot),
                ["pos"] = exact ? CommandKit.D3(pos) : CommandKit.R4(pos),
            };
            if (exact) e["scale"] = CommandKit.D3(Vec3.One);
            if (ownKey != t.name) e["name"] = ownKey;
            entries.Add((row.No, entries.Count, e));
        }

        // ------------------------------------------------------------ fk.set

        static object FkSet(CoreContext c)
        {
            var node = CommandKit.Node(c, "key");
            if (node.Kind == NodeKind.Item)
            {
                if (c.Args.Kind("hipsPosition") != ArgKind.Missing || c.Args.Kind("hipsOffset") != ArgKind.Missing)
                    throw CommandKit.BadArgs(c.Args.Kind("hipsPosition") != ArgKind.Missing ? "hipsPosition" : "hipsOffset", "absent for an item", "items have no hips position or offset");
                return ItemFkSet(c, node);
            }
            var r = CommandKit.Built(c);
            // 3.4: hipsPosition ([3] or null) and hipsOffset (number) beside the bones, all in one entry
            bool hasPos = c.Args.Kind("hipsPosition") != ArgKind.Missing, hasOff = c.Args.Has("hipsOffset");
            Vec3? hipsPos = null;
            if (hasPos && c.Args.Kind("hipsPosition") != ArgKind.Null)
            {
                var a = c.Args.Get<float[]>("hipsPosition");
                if (a == null || a.Length != 3) throw CommandKit.BadArgs("hipsPosition", ArgTypes.Vec3, "argument 'hipsPosition': expected [x, y, z] or null");
                foreach (var f in a) if (!CharaCommands.Finite(f)) throw CommandKit.BadArgs("hipsPosition", "finite numbers");
                hipsPos = new Vec3(a[0], a[1], a[2]);
            }
            float hipsOff = hasOff ? c.Args.Get<float>("hipsOffset") : 0f;
            if (hasOff && !CharaCommands.Finite(hipsOff)) throw CommandKit.BadArgs("hipsOffset", "a finite number");
            var bones = c.Args.Nested("bones");
            if (bones == null && !hasPos && !hasOff) throw new BridgeException(BridgeError.Missing("bones", ArgTypes.Object));
            var table = FkDriver.TableFor(r);
            var values = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            var missing = new List<object>();
            int set = 0;
            if (bones != null)
                foreach (var name in bones.Names)
                {
                    var a = bones.Get<float[]>(name);
                    if (a == null || a.Length != 3) throw CommandKit.BadArgs("bones." + name, ArgTypes.Vec3, "argument 'bones." + name + "': expected [x, y, z]");
                    foreach (var f in a) if (!CharaCommands.Finite(f)) throw CommandKit.BadArgs("bones." + name, "finite numbers");
                    var b = Resolve(table, name);
                    if (b == null) { missing.Add(name); continue; }
                    values[b.Key] = new Vec3(a[0], a[1], a[2]);
                    set++;
                }
            if (!hasPos && !hasOff)
            {
                if (values.Count > 0) CommandKit.Check(new FkOps(c.Runtime).SetBones(r.NodeId, values, CommandKit.Exec(c, "FK bones")), "bones");
            }
            else
            {
                using (var tx = CommandKit.Tx(c, "FK bones"))
                {
                    if (values.Count > 0) CommandKit.Check(new FkOps(c.Runtime).SetBones(r.NodeId, values, CommandKit.Exec(c, "FK bones")), "bones");
                    // the hips part is StudioOps.SetFkHips (joins this transaction: one entry)
                    CommandKit.Check(c, CommandKit.Ops(c).SetFkHips(new[] { r.NodeId }, hasOff ? hipsOff : (float?)null, hasPos, hipsPos, CommandKit.Exec(c, StudioOps.Labels.FkBones)));
                    CommandKit.Commit(tx);
                }
                c.Runtime.ApplyNow();
            }
            var rep = new BridgeReply { ["set"] = set };
            if (missing.Count > 0) rep["missing"] = missing;
            rep["hipsPosition"] = HipsPosition(StateOf(r.Node).Fk, true);
            return rep;
        }

        /// <summary>chara.fk.hipsPosition as a reply value: null, or [x, y, z] (exact doubles or rounded to 4 decimals).</summary>
        static object HipsPosition(CharaFkState fk, bool exact)
        {
            if (fk?.HipsPosition == null) return null;
            var v = fk.HipsPosition.Value;
            return exact ? (object)CommandKit.D3(v) : CommandKit.R4(v);
        }

        /// <summary>The FK bone of a name: model key or partner key, else a bone whose (partner) transform has that name.</summary>
        static FkBone Resolve(FkBoneTable table, string name)
        {
            if (table == null || name == null) return null;
            var b = table.FindAny(name);
            if (b != null) return b;
            foreach (var x in table.Bones)
                if (x != null && ((x.Bone && x.Bone.name == name) || (x.Partner && x.Partner.name == name))) return x;
            return null;
        }

        // ------------------------------------------------------------ fk.enable, fk.capture, ik.enable

        static object FkEnable(CoreContext c)
        {
            var node = CommandKit.Node(c, "key");
            if (node.Kind == NodeKind.Item) return ItemFkEnable(c, node);
            var n = CommandKit.Chara(c);
            bool on = c.Args.Get("on", true);
            List<FkGroup> groups = null;
            if (c.Args.TryGet<List<string>>("groups", out var names))
            {
                groups = new List<FkGroup>();
                foreach (var s in names)
                {
                    int i = Array.FindIndex(GroupNames, g => string.Equals(g, s?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (i < 0)
                    {
                        int limb = Array.FindIndex(LimbNames, g => string.Equals(g, s?.Trim(), StringComparison.OrdinalIgnoreCase));
                        if (limb >= 0)
                        {
                            c.Warn(LimbNames[limb] + " switches the Body group in HS2Next");
                            i = (int)FkGroup.Body;
                        }
                    }
                    if (i < 0) throw new BridgeException(BridgeCodes.BadArgs, "unknown FK group \"" + s + "\"",
                        new BridgeErrorDetail { Arg = "groups", Expected = "FK group names", Candidates = GroupNames });
                    groups.Add((FkGroup)i);
                }
            }
            var ops = new FkOps(c.Runtime);
            var o = CommandKit.Exec(c, "FK");
            using (var tx = CommandKit.Tx(c, "FK"))
            {
                if (groups != null) foreach (var g in groups) CommandKit.Check(ops.SetGroup(n.Id, g, on, o), "groups");
                else CommandKit.Check(ops.SetEnabled(n.Id, on, o), "on");
                CommandKit.Commit(tx);
            }
            c.Runtime.ApplyNow();
            var st = StateOf(n);
            return new BridgeReply { ["fkEnabled"] = st.Fk.Enabled, ["ikEnabled"] = st.Ik.Enabled, ["activeFK"] = Bools(FkFlags(st.Fk.Groups)) };
        }

        static object FkCapture(CoreContext c)
        {
            var node = CommandKit.Node(c, "key");
            if (node.Kind == NodeKind.Item) return ItemFkCapture(c, node);
            var r = CommandKit.Built(c);
            bool stop = c.Args.Get("stopAnime", true);
            string id = r.NodeId;
            var cur = new FkOps(c.Runtime).CurrentValues(id, new[] { FkGroup.Body, FkGroup.Neck, FkGroup.RightHand, FkGroup.LeftHand });
            var st = StateOf(r.Node);
            var map = new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal);
            foreach (var kv in cur) map[kv.Key] = kv.Value;
            using (var tx = CommandKit.Tx(c, "FK from pose"))
            {
                CommandKit.Set(tx, id, "chara.fk.bones", map);
                CommandKit.Set(tx, id, "chara.fk.groups.body", true);
                CommandKit.Set(tx, id, "chara.fk.groups.neck", true);
                CommandKit.Set(tx, id, "chara.fk.enabled", true);
                if (stop) CommandKit.Set(tx, id, "chara.anime.speed", 0f, "stopAnime");
                CommandKit.Commit(tx);
            }
            c.Runtime.ApplyNow();
            return new BridgeReply { ["fkEnabled"] = StateOf(r.Node).Fk.Enabled };
        }

        // ik.enable: groups → IkOps.SetGroups (one entry, the capture of the groups that become
        // applied), else IkOps.SetEnabled (capture of the flagged groups' missing targets; the exclusive preset switches FK off)
        static object IkEnable(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool on = c.Args.Get("on", true);
            var groups = IkGroupsArg(c, "groups");
            var ops = new IkOps(c.Runtime);
            var o = CommandKit.Exec(c, "IK");
            if (groups != null) CheckOp(c, ops.SetGroups(n.Id, groups, on, o), "groups");
            else CheckOp(c, ops.SetEnabled(n.Id, on, o), "on");
            c.Runtime.ApplyNow();
            var st = StateOf(n);
            return new BridgeReply
            {
                ["ikEnabled"] = st.Ik.Enabled, ["fkEnabled"] = st.Fk.Enabled, ["activeIK"] = Bools(IkFlags(st.Ik.Groups)),
                ["captured"] = new List<object>(ops.LastCaptured),
            };
        }

        // ------------------------------------------------------------ Phase 3.4 commands

        /// <summary>HS2's IK group names (OIBoneInfo.BoneGroup, activeIK order): ik.list's `group`, ik.copy's `groups` (any case).</summary>
        static readonly string[] IkGroupDisplay = { "Body", "RightLeg", "LeftLeg", "RightArm", "LeftArm" };

        static void RegisterPhase34(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("ik.list", "The 13 IK targets of a character in HS2's order and shape (the effective value: the stored one, else the runtime snapshot), the IK / FK flags, and HS2Next's solve info and owned bones.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a character (solve, owned and world need a built one)", true)
                    .Arg("exact", ArgTypes.Bool, "pos / rot as exact doubles; else rounded to 4 decimals", false, false)
                    .Arg("world", ArgTypes.Bool, "add each target's world pose {pos, rot} (built characters)", false, false)
                    .Return("ikEnabled", ArgTypes.Bool).Return("enableIK", ArgTypes.Bool).Return("enableFK", ArgTypes.Bool)
                    .Return("activeIK", ArgTypes.ArrayOf(ArgTypes.Bool), "body, rightLeg, leftLeg, rightArm, leftArm").Return("activeFK", ArgTypes.ArrayOf(ArgTypes.Bool))
                    .Return("targets", ArgTypes.ArrayOf(ArgTypes.Object), "[{name, key 0–12, group, kind (effector / bendGoal), usesRot, pos, rot (null when the target has no value yet), world?: {pos, rot}, stored}]")
                    .Return("bodySemantics", ArgTypes.String, "\"hips\": the Body target is cf_J_Hips (HS2: \"spine01\")")
                    .Return("effective", ArgTypes.ArrayOf(ArgTypes.String), "targets whose value is a runtime snapshot (not stored)")
                    .Return("solve", ArgTypes.Object, "{ran, residual {name: distance}, clamped [names], poleFallback {bend goal name: clip / rest}, torsoResidualDeg, ms, ...} of the last solve, or null")
                    .Return("owned", ArgTypes.ArrayOf(ArgTypes.String), "bones the last solve wrote (FK's value is their starting pose)")
                    .WithFlags(CommandFlags.ReadOnly),
                IkList);
            CommandKit.Register(r, app, CommandKit.Spec("ik.set", "IK target values by name (one history entry \"IK targets\"); unspecified fields keep the effective value, the other missing targets keep the pose you see.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a character (world space: a built one)", true)
                    .Arg("targets", ArgTypes.Object, "{<Name>: {pos?: [x, y, z], rot?: [x, y, z]}}: Body, LeftShoulder, LeftArmChain, LeftHand, RightShoulder, RightArmChain, RightHand, LeftThigh, LeftLegChain, LeftFoot, RightThigh, RightLegChain, RightFoot (ignoring case); rot is used by the hands and feet only", true)
                    .Arg("space", ArgTypes.Enum("local", "world"), "local: the character root's space (as stored); world: world space", false, "local")
                    .Return("set", ArgTypes.Int).Return("missing", ArgTypes.ArrayOf(ArgTypes.String), "names that are no IK target", false)
                    .Return("rotIgnored", ArgTypes.ArrayOf(ArgTypes.String), "targets whose rot was ignored (they use none)", false)
                    .Return("captured", ArgTypes.ArrayOf(ArgTypes.String), "other missing targets written from the pose you see")
                    .Example("{\"cmd\":\"ik.set\",\"key\":\"<id>\",\"targets\":{\"RightHand\":{\"pos\":[0.3,1.2,0.1]}}}")
                    .WithFlags(CommandFlags.Mutates),
                IkSet);
            CommandKit.Register(r, app, CommandKit.Spec("ik.copy", "Copies the animation pose, the current pose or the imported HS2 IK block into the IK targets (one history entry); modes stay unless enable.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("from", ArgTypes.Enum("anime", "current", "import"), "anime: the clip and hand patterns (no look, IK, FK); current: the final pose; import: the node's hs2.pose IK block (2.3 imports), converted", false, "anime")
                    .Arg("groups", ArgTypes.ArrayOf(ArgTypes.String), "IK groups (Body, RightLeg, LeftLeg, RightArm, LeftArm, ignoring case); default all (not with import)")
                    .Arg("enable", ArgTypes.Bool, "also switch IK and the groups on (with kinematics.exclusive: FK off); not with import", false, false)
                    .Return("copied", ArgTypes.ArrayOf(ArgTypes.String), "target names written").Return("from", ArgTypes.String)
                    .Return("ikEnabled", ArgTypes.Bool).Return("fkEnabled", ArgTypes.Bool)
                    .WithFlags(CommandFlags.Mutates),
                IkCopy);
            CommandKit.Register(r, app, CommandKit.Spec("fk.copy", "Copies the animation pose or the current pose into FK values (one history entry); modes stay unless enable.", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a built character (items: wrong_kind; they keep fk.capture)", true)
                    .Arg("from", ArgTypes.Enum("anime", "current"), "anime: the clip and hand patterns (no look, IK, FK); current: the final pose (HS2 \"IK → FK\")", false, "anime")
                    .Arg("groups", ArgTypes.ArrayOf(ArgTypes.String), "FK groups (Hair, Neck, Breast, Body, RightHand, LeftHand, Skirt, ignoring case; anime: Body, Neck, RightHand, LeftHand only); default Body, Neck, RightHand, LeftHand")
                    .Arg("enable", ArgTypes.Bool, "also switch FK and the groups on (with kinematics.exclusive: IK off)", false, false)
                    .Return("copied", ArgTypes.Int, "FK values written").Return("from", ArgTypes.String)
                    .Return("fkEnabled", ArgTypes.Bool).Return("ikEnabled", ArgTypes.Bool)
                    .WithFlags(CommandFlags.Mutates),
                FkCopy);
            CommandKit.Register(r, app, CommandKit.Spec("ik.bake", "Bakes the solved pose into FK: Body-group FK values and fk.hipsPosition, FK and its Body group on, IK off (targets kept; one history entry \"Bake IK\").", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Return("bones", ArgTypes.Int, "Body-group FK values written").Return("hipsPosition", ArgTypes.Vec3, "the stored cf_J_Hips local position")
                    .Return("fkEnabled", ArgTypes.Bool).Return("ikEnabled", ArgTypes.Bool)
                    .WithFlags(CommandFlags.Mutates),
                IkBake);
            CommandKit.Register(r, app, CommandKit.Spec("pose.save", "Saves a character's pose (anime, hands, fk, ik, correction, face, look) as an HS2Next pose file under <UserData>/pose; missing FK values and IK targets are filled from the pose you see in the file only (no history entry).", "fk/pose")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("path", ArgTypes.Path, "relative path under <UserData>/pose (subfolders created; .hs2npose appended unless present)", true)
                    .Arg("name", ArgTypes.String, "the pose name (default: the file name)")
                    .Arg("parts", ArgTypes.ArrayOf(ArgTypes.String), "parts to write: anime, hands, fk, ik, correction, face, look (default all)")
                    .Arg("overwrite", ArgTypes.Bool, "replace an existing file", false, false)
                    .Return("path", ArgTypes.String, "absolute path").Return("bytes", ArgTypes.Int).Return("parts", ArgTypes.ArrayOf(ArgTypes.String), "parts written")
                    .Return("name", ArgTypes.String).Return("relative", ArgTypes.String, "relative to <UserData>/pose ('/'; what pose.load takes)")
                    .Return("filledFk", ArgTypes.ArrayOf(ArgTypes.String), "FK keys filled in the file").Return("filledIk", ArgTypes.ArrayOf(ArgTypes.String), "IK targets filled in the file")
                    .WithFlags(CommandFlags.WritesFiles | CommandFlags.Slow).WithTimeout(120),
                PoseSave);
        }

        /// <summary>The IK groups of argument <paramref name="arg"/> (names ignoring case), or null when absent.</summary>
        static List<IkGroup> IkGroupsArg(CoreContext c, string arg)
        {
            if (!c.Args.TryGet<List<string>>(arg, out var names)) return null;
            var l = new List<IkGroup>();
            foreach (var s in names)
            {
                int i = Array.FindIndex(IkGroupInfo.MemberNames, g => string.Equals(g, s?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (i < 0) throw new BridgeException(BridgeCodes.BadArgs, "unknown IK group \"" + s + "\"",
                    new BridgeErrorDetail { Arg = arg, Expected = "IK group names", Candidates = IkGroupDisplay });
                if (!l.Contains((IkGroup)i)) l.Add((IkGroup)i);
            }
            return l;
        }

        /// <summary>The FK groups of argument <paramref name="arg"/> (names ignoring case), or null when absent.</summary>
        static List<FkGroup> FkGroupsArg(CoreContext c, string arg)
        {
            if (!c.Args.TryGet<List<string>>(arg, out var names)) return null;
            var l = new List<FkGroup>();
            foreach (var s in names)
            {
                int i = Array.FindIndex(GroupNames, g => string.Equals(g, s?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (i < 0) throw new BridgeException(BridgeCodes.BadArgs, "unknown FK group \"" + s + "\"",
                    new BridgeErrorDetail { Arg = arg, Expected = "FK group names", Candidates = GroupNames });
                if (!l.Contains((FkGroup)i)) l.Add((FkGroup)i);
            }
            return l;
        }

        static string FromArg(CoreContext c, params string[] allowed)
        {
            string from = c.Args.Get("from", "anime") ?? "anime";
            if (Array.IndexOf(allowed, from) < 0)
                throw new BridgeException(BridgeCodes.BadArgs, "unknown from \"" + from + "\"", new BridgeErrorDetail { Arg = "from", Expected = string.Join(" / ", allowed), Candidates = allowed });
            return from;
        }

        /// <summary>
        /// Checks an operation result of package B: errors become bridge errors; "unavailable: …" (a character that is not built,
        /// reported by the operations as unsupported) becomes <c>unavailable</c>.
        /// </summary>
        static CommandResult CheckOp(CoreContext c, CommandResult r, string arg)
        {
            if (r != null && r.Status == CommandStatus.Error && r.Message != null && r.Message.StartsWith("unavailable", StringComparison.Ordinal))
                throw CommandKit.NotRealized(r.Message, "key", "NotBuilt");
            return CommandKit.Check(r, arg);
        }

        static object V3(Vector3 v, bool exact) => exact ? (object)CommandKit.D3(v) : CommandKit.R4(v);
        static object V3(Vec3 v, bool exact) => exact ? (object)CommandKit.D3(v) : CommandKit.R4(v);
        static object Num(float f) => float.IsNaN(f) || float.IsInfinity(f) ? null : (object)(double)f;

        static object IkList(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool exact = c.Args.Get("exact", false), world = c.Args.Get("world", false);
            c.Runtime.ApplyNow();
            var r = c.Runtime.FindChara(n.Id);
            bool built = r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root;
            return IkList(StateOf(n), built ? r : null, exact, world);
        }

        /// <summary>The ik.list reply (also used by the tests): HS2's members first, then HS2Next's additions.</summary>
        public static BridgeReply IkList(CharaNodeState st, CharaRealization built, bool exact, bool world)
        {
            var ik = st.Ik ?? new CharaIkState();
            var fk = st.Fk ?? new CharaFkState();
            var ikc = built != null ? IkDriver.Of(built) : null;
            Transform root = built != null && built.Root ? built.Root.transform : null;
            var targets = new List<object>(IkGroupInfo.TargetCount);
            for (int i = 0; i < IkGroupInfo.TargetCount; i++)
            {
                var id = (IkTargetId)i;
                var stored = ik.Targets?[i];
                Vector3? pos = null, rot = null;
                if (stored != null)
                {
                    pos = new Vector3(stored.Pos.X, stored.Pos.Y, stored.Pos.Z);
                    rot = new Vector3(stored.Rot.X, stored.Rot.Y, stored.Rot.Z);
                }
                else if (ikc != null && ikc.Effective(id).Has)
                {
                    var e = ikc.Effective(id);
                    pos = e.Pos;
                    rot = IkGroupInfo.UsesRotation(id) ? e.Rot : Vector3.zero;
                }
                var t = new BridgeReply
                {
                    ["name"] = IkTargets.Names[i],
                    ["key"] = i,
                    ["group"] = IkGroupDisplay[(int)IkGroupInfo.GroupOf(id)],
                    ["kind"] = IkGroupInfo.KindOf(id) == IkTargetKind.BendGoal ? "bendGoal" : "effector",
                    ["usesRot"] = IkGroupInfo.UsesRotation(id),
                    ["pos"] = pos.HasValue ? V3(pos.Value, exact) : null,
                    ["rot"] = rot.HasValue ? V3(rot.Value, exact) : null,
                };
                if (world && root && pos.HasValue)
                    t["world"] = new BridgeReply
                    {
                        ["pos"] = V3(root.TransformPoint(pos.Value), exact),
                        ["rot"] = V3((root.rotation * Quaternion.Euler(rot.Value)).eulerAngles, exact),
                    };
                t["stored"] = stored != null;
                targets.Add(t);
            }
            var rep = new BridgeReply
            {
                ["ikEnabled"] = ik.Enabled,
                ["enableIK"] = ik.Enabled,
                ["enableFK"] = fk.Enabled,
                ["activeIK"] = Bools(IkFlags(ik.Groups)),
                ["activeFK"] = Bools(FkFlags(fk.Groups)),
                ["targets"] = targets,
                ["bodySemantics"] = "hips",
                ["effective"] = ikc != null ? new List<object>(ikc.SnapshotNames()) : new List<object>(),
                ["built"] = built != null,
            };
            if (ikc == null)
            {
                rep["solve"] = null;
                rep["owned"] = new List<object>();
                return rep;
            }
            var applied = new bool[IkGroupInfo.Count];
            for (int g = 0; g < applied.Length; g++) applied[g] = ikc.Applied((IkGroup)g);
            rep["applied"] = Bools(applied);
            rep["running"] = ikc.Running;
            var s = ikc.Stage.LastSolve;
            var solve = new BridgeReply { ["ran"] = ikc.Running && s.Valid };
            if (s.Valid)
            {
                var res = new BridgeReply();
                var clamped = new List<object>();
                for (int i = 0; i < IkGroupInfo.TargetCount; i++)
                {
                    if (!float.IsNaN(s.Residual[i])) res[IkTargets.Names[i]] = (double)s.Residual[i];
                    if (s.Clamped[i]) clamped.Add(IkTargets.Names[i]);
                }
                var poles = new BridgeReply();
                for (int g = 1; g < IkGroupInfo.Count; g++)
                {
                    var src = s.Pole[g];
                    if (src != IkPoleSource.Clip && src != IkPoleSource.Rest) continue;
                    foreach (var t in IkGroupInfo.TargetsOf((IkGroup)g))
                        if (IkGroupInfo.KindOf(t) == IkTargetKind.BendGoal) poles[IkTargets.Names[(int)t]] = src == IkPoleSource.Clip ? "clip" : "rest";
                }
                solve["residual"] = res;
                solve["clamped"] = clamped;
                solve["poleFallback"] = poles;
                solve["torsoResidualDeg"] = Num(s.TorsoResidual);
                solve["ms"] = s.Milliseconds;
                solve["stageMs"] = s.StageMilliseconds;
                solve["step"] = s.Step;
                solve["bodyDegenerate"] = s.BodyDegenerate;
                solve["torsoRan"] = s.TorsoRan;
                solve["torsoDegenerate"] = s.TorsoDegenerate;
            }
            rep["solve"] = solve;
            var owned = new List<string>();
            foreach (var b in ikc.Owned) if (b) owned.Add(b.name);
            owned.Sort(StringComparer.Ordinal);
            rep["owned"] = new List<object>(owned);
            return rep;
        }

        static object IkSet(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            var targets = c.Args.Nested("targets") ?? throw new BridgeException(BridgeError.Missing("targets", ArgTypes.Object));
            string space = c.Args.Get("space", "local") ?? "local";
            if (space != "local" && space != "world")
                throw new BridgeException(BridgeCodes.BadArgs, "unknown space \"" + space + "\"", new BridgeErrorDetail { Arg = "space", Expected = "local or world", Candidates = new[] { "local", "world" } });
            if (targets.Names.Count == 0) throw CommandKit.BadArgs("targets", "at least one target", "targets is empty");
            var edits = new Dictionary<IkTargetId, IkTargetEdit>();
            var missing = new List<object>();
            var rotIgnored = new List<object>();
            foreach (var name in targets.Names)
            {
                int i = Array.FindIndex(IkTargets.Names, x => string.Equals(x, name?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (i < 0) { missing.Add(name); continue; }
                var v = targets.Nested(name) ?? throw CommandKit.BadArgs("targets." + name, "{pos?, rot?}");
                Vector3? pos = Vec("targets." + name + ".pos", v), rot = null;
                var rv = Vec("targets." + name + ".rot", v, "rot");
                if (rv.HasValue)
                {
                    if (IkGroupInfo.UsesRotation((IkTargetId)i)) rot = rv;
                    else rotIgnored.Add(IkTargets.Names[i]);
                }
                edits[(IkTargetId)i] = new IkTargetEdit(pos, rot);
            }
            if (edits.Count == 0)
                throw new BridgeException(BridgeCodes.BadArgs, "no known IK target in targets (" + string.Join(", ", missing) + ")",
                    new BridgeErrorDetail { Arg = "targets", Expected = "IK target names", Candidates = IkTargets.Names });
            var sp = space == "world" ? IkSpace.World : IkSpace.Local;
            if (sp == IkSpace.World) CommandKit.Built(c);
            var ops = new IkOps(c.Runtime);
            CheckOp(c, ops.SetTargets(n.Id, edits, sp, CommandKit.Exec(c, "IK targets")), "targets");
            c.Runtime.ApplyNow();
            var rep = new BridgeReply { ["set"] = edits.Count };
            if (missing.Count > 0) rep["missing"] = missing;
            if (rotIgnored.Count > 0) rep["rotIgnored"] = rotIgnored;
            rep["captured"] = new List<object>(ops.LastCaptured);
            return rep;
        }

        /// <summary>A [x, y, z] member (finite) of a nested argument object, or null when absent.</summary>
        static Vector3? Vec(string path, ICommandArgs v, string member = "pos")
        {
            if (!v.Has(member)) return null;
            var a = v.Get<float[]>(member);
            if (a == null || a.Length != 3) throw CommandKit.BadArgs(path, ArgTypes.Vec3, "argument '" + path + "': expected [x, y, z]");
            foreach (var f in a) if (!CharaCommands.Finite(f)) throw CommandKit.BadArgs(path, "finite numbers");
            return new Vector3(a[0], a[1], a[2]);
        }

        static object IkCopy(CoreContext c)
        {
            var r = CommandKit.Built(c);
            string from = FromArg(c, "anime", "current", "import");
            var groups = IkGroupsArg(c, "groups");
            bool enable = c.Args.Get("enable", false);
            if (from == "import" && (groups != null || c.Args.Has("enable")))
                throw CommandKit.BadArgs(groups != null ? "groups" : "enable", "absent with from:\"import\"", "from:\"import\" sets the IK flags and groups of the imported block");
            var kc = new KinematicCopies(c.Runtime);
            KinematicResult kr;
            switch (from)
            {
                case "anime": kr = kc.IkFromAnimation(r.NodeId, groups, enable, CommandKit.Exec(c, "IK from animation")); break;
                case "current": kr = kc.IkFromCurrent(r.NodeId, groups, enable, CommandKit.Exec(c, "IK from pose")); break;
                default: kr = kc.IkFromImport(r.NodeId, CommandKit.Exec(c, "Convert IK")); break;
            }
            CheckOp(c, kr.Command, from == "import" ? "key" : "from");
            c.Runtime.ApplyNow();
            var st = StateOf(r.Node);
            var rep = new BridgeReply { ["copied"] = new List<object>(kr.Copied), ["from"] = from, ["ikEnabled"] = st.Ik.Enabled, ["fkEnabled"] = st.Fk.Enabled };
            if (kr.Ik != null)
            {
                rep["ikScale"] = kr.Ik.Scale;
                rep["ikBodyShift"] = kr.Ik.Targets != null ? CommandKit.D3(kr.Ik.BodyShift) : null;
                rep["ikUnreachable"] = new List<object>(kr.Ik.Unreachable);
            }
            return rep;
        }

        static object FkCopy(CoreContext c)
        {
            var node = CommandKit.Node(c, "key");
            if (node.Kind == NodeKind.Item)
                throw new BridgeException(BridgeError.WrongKind("fk.copy takes a character (items keep fk.capture)", "key", "chara"));
            var r = CommandKit.Built(c);
            string from = FromArg(c, "anime", "current");
            var groups = FkGroupsArg(c, "groups");
            if (from == "anime" && groups != null)
                foreach (var g in groups)
                    if (Array.IndexOf(FkOps.AnimationGroups, g) < 0)
                        throw new BridgeException(BridgeCodes.BadArgs, "fk.copy from:\"anime\" copies Body, Neck, RightHand and LeftHand only (not " + GroupNames[(int)g] + ")",
                            new BridgeErrorDetail { Arg = "groups", Expected = "Body, Neck, RightHand, LeftHand", Candidates = new[] { "Body", "Neck", "RightHand", "LeftHand" } });
            bool enable = c.Args.Get("enable", false);
            var kc = new KinematicCopies(c.Runtime);
            var kr = from == "anime"
                ? kc.FkFromAnimation(r.NodeId, groups, enable, CommandKit.Exec(c, "FK from animation"))
                : kc.FkFromCurrent(r.NodeId, groups, enable, CommandKit.Exec(c, "FK from pose"));
            CheckOp(c, kr.Command, "groups");
            c.Runtime.ApplyNow();
            var st = StateOf(r.Node);
            return new BridgeReply { ["copied"] = kr.Copied.Count, ["from"] = from, ["fkEnabled"] = st.Fk.Enabled, ["ikEnabled"] = st.Ik.Enabled };
        }

        static object IkBake(CoreContext c)
        {
            var r = CommandKit.Built(c);
            var kr = new KinematicCopies(c.Runtime).Bake(r.NodeId, CommandKit.Exec(c, "Bake IK"));
            CheckOp(c, kr.Command, "key");
            c.Runtime.ApplyNow();
            var st = StateOf(r.Node);
            return new BridgeReply
            {
                ["bones"] = kr.Bones,
                ["hipsPosition"] = kr.HipsPosition.HasValue ? CommandKit.D3(kr.HipsPosition.Value) : null,
                ["fkEnabled"] = st.Fk.Enabled,
                ["ikEnabled"] = st.Ik.Enabled,
            };
        }

        static object PoseSave(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            string text = c.Args.Require<string>("path");
            bool overwrite = c.Args.Get("overwrite", false);
            if (string.IsNullOrWhiteSpace(text)) throw CommandKit.BadArgs("path", ArgTypes.Path, "empty path");
            List<string> parts = null;
            if (c.Args.TryGet<List<string>>("parts", out var names))
            {
                parts = PoseFragmentOps.NormaliseParts(names, out var bad);
                if (bad != null) throw PartsError(bad);
                if (parts.Count == 0) throw CommandKit.BadArgs("parts", "at least one part", "parts is empty");
            }
            string rel = FileRoots.WithExt(text, SaveFormats.PoseExtension).Replace('\\', '/');
            string full;
            try { full = c.App.RootFor(c.Paths.PoseDir).Resolve(rel, overwrite, createDirectory: true); }
            catch (BridgeException e) when (e.Code == BridgeCodes.IoDenied || e.Code == BridgeCodes.BadArgs)
            {
                throw new BridgeException(e.Code, e.Error.Message, new BridgeErrorDetail { Arg = e.Error.Detail?.Arg == "overwrite" ? "overwrite" : "path" });
            }
            string name = c.Args.Get<string>("name", null) ?? Path.GetFileNameWithoutExtension(full);
            c.Runtime.ApplyNow();
            var res = PoseFragmentOps.Save(c.Runtime, n.Id, full, name, parts, overwrite);
            if (!res.Ok) throw OpError(res.Code, res.Error, "path");
            string path = res.Path ?? full;
            return new BridgeReply
            {
                ["path"] = path,
                ["bytes"] = res.Bytes,
                ["parts"] = new List<object>(res.Parts),
                ["name"] = name,
                ["relative"] = FileRoots.Relative(path, c.Paths.PoseDir),
                ["filledFk"] = new List<object>(res.FilledFk),
                ["filledIk"] = new List<object>(res.FilledIk),
                ["findings"] = CommandKit.Findings(res.Findings),
            };
        }

        // ------------------------------------------------------------ item forms

        static object ItemFkList(CoreContext c, TreeNode n, string grp, bool exact) => ItemFkList(ItemMotionCommands.BuiltItem(c, n), grp, exact);

        /// <summary>The fk.list reply of a built item (also used by the tests): found bones, their values, the switches.</summary>
        public static BridgeReply ItemFkList(ItemRealization r, string grp, bool exact)
        {
            var st = ItemMotionCommands.StateOf(r.Node);
            var m = ItemMotion.Of(r);
            var bones = new List<object>();
            foreach (var name in ItemMotionCommands.FkBones(r))
            {
                if (grp.Length > 0 && name.IndexOf(grp, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool stored = st.Fk.Bones.TryGetValue(name, out var v);
                if (!stored) v = UnityObjects.V(ItemMotionCommands.FkBase(r, name) ?? Vector3.zero);
                var t = r.Instance ? ItemDefaults.FindBone(r.Instance.transform, name) : null;
                var e = new BridgeReply
                {
                    ["bone"] = name,
                    ["key"] = name,
                    ["group"] = "Item",
                    ["stored"] = stored,
                    ["rot"] = exact ? CommandKit.D3(v) : CommandKit.R4(v),
                };
                if (t) e["current"] = exact ? CommandKit.D3(t.localEulerAngles) : CommandKit.R4(t.localEulerAngles);
                bones.Add(e);
            }
            var missing = new List<object>();
            if (r.Features != null) foreach (var x in r.Features.FkMissing) missing.Add(x);
            return new BridgeReply
            {
                ["fkEnabled"] = st.Fk.Enabled,
                ["enableFK"] = st.Fk.Enabled,
                ["item"] = true,
                ["bones"] = bones,
                ["missing"] = missing,
                ["applied"] = m?.Fk != null && m.Fk.On,
                ["dynamicBones"] = st.DynamicBones,
                ["dynamicBonesActive"] = m != null && m.Dynamic != null && m.DynamicActive,
            };
        }

        static object ItemFkSet(CoreContext c, TreeNode n)
        {
            var r = ItemMotionCommands.BuiltItem(c, n);
            var bones = c.Args.Nested("bones") ?? throw new BridgeException(BridgeError.Missing("bones", ArgTypes.Object));
            var found = new HashSet<string>(ItemMotionCommands.FkBones(r), StringComparer.Ordinal);
            var values = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
            var missing = new List<object>();
            foreach (var name in bones.Names)
            {
                var a = bones.Get<float[]>(name);
                if (a == null || a.Length != 3) throw CommandKit.BadArgs("bones." + name, ArgTypes.Vec3, "argument 'bones." + name + "': expected [x, y, z]");
                foreach (var f in a) if (!CharaCommands.Finite(f)) throw CommandKit.BadArgs("bones." + name, "finite numbers");
                if (!found.Contains(name)) { missing.Add(name); continue; }
                values[name] = new Vec3(a[0], a[1], a[2]);
            }
            if (values.Count > 0)
            {
                using (var tx = CommandKit.Tx(c, "Item FK"))
                {
                    foreach (var kv in values) CommandKit.Set(tx, r.NodeId, "item.fk.bones[" + FieldPath.Quote(kv.Key) + "]", kv.Value, "bones");
                    CommandKit.Commit(tx);
                }
                c.Runtime.ApplyNow();
            }
            var rep = new BridgeReply { ["set"] = values.Count };
            if (missing.Count > 0) rep["missing"] = missing;
            return rep;
        }

        static object ItemFkEnable(CoreContext c, TreeNode n)
        {
            if (c.Args.Has("groups"))
                throw new BridgeException(BridgeCodes.BadArgs, "items have no FK groups", new BridgeErrorDetail { Arg = "groups", Expected = "absent for an item" });
            var r = ItemMotionCommands.BuiltItem(c, n);
            bool on = c.Args.Get("on", true);
            if (ItemMotionCommands.FkBones(r).Count == 0) c.Warn("the item has no FK bones: the flag is stored only");
            using (var tx = CommandKit.Tx(c, "Item FK"))
            {
                if (on) ItemMotionCommands.CaptureMissingFk(tx, r, "on");
                CommandKit.Set(tx, r.NodeId, "item.fk.enabled", on, "on");
                CommandKit.Commit(tx);
            }
            c.Runtime.ApplyNow();
            var st = ItemMotionCommands.StateOf(r.Node);
            var m = ItemMotion.Of(r);
            return new BridgeReply
            {
                ["fkEnabled"] = st.Fk.Enabled,
                ["dynamicBones"] = st.DynamicBones,
                ["dynamicBonesActive"] = m != null && m.Dynamic != null && m.DynamicActive,
            };
        }

        static object ItemFkCapture(CoreContext c, TreeNode n)
        {
            var r = ItemMotionCommands.BuiltItem(c, n);
            bool stop = c.Args.Get("stopAnime", true);
            var st = ItemMotionCommands.StateOf(r.Node);
            var map = new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal);
            int k = 0;
            foreach (var name in ItemMotionCommands.FkBones(r))
            {
                var t = r.Instance ? ItemDefaults.FindBone(r.Instance.transform, name) : null;
                if (!t) continue;
                map[name] = UnityObjects.V(t.localEulerAngles);
                k++;
            }
            using (var tx = CommandKit.Tx(c, "FK from pose"))
            {
                CommandKit.Set(tx, r.NodeId, "item.fk.bones", map);
                CommandKit.Set(tx, r.NodeId, "item.fk.enabled", true);
                if (stop) CommandKit.Set(tx, r.NodeId, "item.anime.speed", 0f, "stopAnime");
                CommandKit.Commit(tx);
            }
            c.Runtime.ApplyNow();
            return new BridgeReply { ["fkEnabled"] = ItemMotionCommands.StateOf(r.Node).Fk.Enabled, ["captured"] = k };
        }

        // ------------------------------------------------------------ pose.load, pose.list

        static object PoseLoad(CoreContext c)
        {
            var r = CommandKit.Built(c);
            string text = c.Args.Require<string>("path");
            bool relFragment = text.EndsWith(SaveFormats.PoseExtension, StringComparison.OrdinalIgnoreCase) && !IsAbsolute(text);
            string full = relFragment
                ? FileRoots.ForRead(c, "path", text, c.Paths.PoseDir, new[] { SaveFormats.PoseExtension }, null)
                : FileRoots.ForRead(c, "path", text, c.Paths.Hs2PoseDir, new[] { ".dat", ".png", SaveFormats.PoseExtension }, null);
            if (PoseFragmentOps.IsFragment(full)) return FragmentLoad(c, r, full);
            if (c.Args.Has("parts")) throw CommandKit.BadArgs("parts", "absent for an HS2 pose file", "parts apply to HS2Next pose files (.hs2npose) only");
            var opts = new PoseImportOptions { Ik = c.Args.Get("ik", true) };
            var rep = PoseImport.ApplyFile(c.Runtime, r.NodeId, full, opts, CommandKit.Exec(c, "Load pose"));
            if (rep.Error != null) throw PoseError(rep.Error, r);
            string name = null;
            try { name = PoseFile.ReadFile(full).Name; } catch (Exception) { }
            c.Runtime.ApplyNow();
            bool sexMismatch = false;
            foreach (var f in rep.Findings) if (f.Code == "pose.sexMismatch") sexMismatch = true;
            var captured = new List<object>();
            foreach (var k in rep.Captured) captured.Add(k);
            return new BridgeReply
            {
                ["sexMatches"] = !sexMismatch,
                ["loaded"] = rep.Applied,
                ["name"] = name ?? "",
                ["object"] = CommandKit.Describe(c.App, r.Node),
                ["keys"] = rep.Keys,
                ["mapped"] = rep.Mapped,
                ["aiRemapped"] = rep.AiRemapped,
                ["unresolved"] = rep.Unresolved,
                ["notOnCharacter"] = rep.NotOnCharacter,
                ["animeResolved"] = rep.AnimeResolved,
                ["ikPending"] = rep.IkPending,
                ["ikApproximated"] = rep.IkApproximated,
                ["ikScale"] = rep.IkScale,
                ["ikBodyShift"] = rep.IkBodyShift.HasValue ? CommandKit.D3(rep.IkBodyShift.Value) : null,
                ["ikUnreachable"] = new List<object>(rep.IkUnreachable),
                ["ikRotDropped"] = rep.IkRotDropped,
                ["skirtGroupForcedOff"] = rep.SkirtGroupForcedOff,
                ["captured"] = captured,
                ["source"] = "hs2",
                ["findings"] = CommandKit.Findings(rep.Findings),
            };
        }

        static bool IsAbsolute(string text) =>
            Path.IsPathRooted(text) && !text.StartsWith("\\", StringComparison.Ordinal) && !text.StartsWith("/", StringComparison.Ordinal);

        // pose.load of an HS2Next pose file: PoseFragmentOps.Load, one entry "Load pose"
        static object FragmentLoad(CoreContext c, CharaRealization r, string full)
        {
            List<string> parts = null;
            if (c.Args.TryGet<List<string>>("parts", out var names))
            {
                parts = PoseFragmentOps.NormaliseParts(names, out var bad);
                if (bad != null) throw PartsError(bad);
            }
            var res = PoseFragmentOps.Load(c.Runtime, r.NodeId, full, parts, CommandKit.Exec(c, "Load pose"));
            if (res.Error != null) throw OpError(res.Code, res.Error, "path");
            c.Runtime.ApplyNow();
            bool sexMismatch = false;
            foreach (var f in res.Findings) if (f.Code == "pose.sexMismatch") sexMismatch = true;
            var (title, _) = FragmentManifest(full);
            return new BridgeReply
            {
                ["sexMatches"] = !sexMismatch,
                ["loaded"] = res.Applied,
                ["name"] = title ?? "",
                ["object"] = CommandKit.Describe(c.App, r.Node),
                ["parts"] = new List<object>(res.Parts),
                ["captured"] = new List<object>(res.Captured),
                ["ikPending"] = false,
                ["source"] = "hs2next",
                ["findings"] = CommandKit.Findings(res.Findings),
            };
        }

        static BridgeException PartsError(string bad) =>
            new BridgeException(BridgeCodes.BadArgs, "unknown pose part \"" + bad + "\"",
                new BridgeErrorDetail { Arg = "parts", Expected = "pose part names", Candidates = PoseFragmentOps.AllParts });

        /// <summary>A failed operation result (code + message) as a bridge error naming <paramref name="arg"/>.</summary>
        static BridgeException OpError(string code, string message, string arg) =>
            new BridgeException(string.IsNullOrEmpty(code) ? BridgeCodes.BadArgs : code, message ?? "failed", new BridgeErrorDetail { Arg = arg });

        /// <summary>The manifest title and sex of an hs2next.pose file (nulls when it cannot be read).</summary>
        static (string title, int? sex) FragmentManifest(string path)
        {
            try
            {
                var m = ContainerReader.Read(path, null, n => n == "manifest.json").ReadManifest();
                return m != null && m.Format == SaveFormats.Pose ? (m.Title, m.Sex) : (null, null);
            }
            catch (Exception) { return (null, null); }
        }

        // PoseImport.Apply errors read "not_found: …", "wrong_kind: …", "not built", "pose file: …" or a bus failure
        static BridgeException PoseError(string error, CharaRealization r)
        {
            if (error.StartsWith(BridgeCodes.NotFound, StringComparison.Ordinal)) return new BridgeException(BridgeCodes.NotFound, error);
            if (error.StartsWith(BridgeCodes.WrongKind, StringComparison.Ordinal)) return new BridgeException(BridgeCodes.WrongKind, error);
            if (error.StartsWith("not built", StringComparison.Ordinal)) return CommandKit.NotRealized("character " + r.NodeId + " is not built", "key", r.State.ToString());
            if (error.IndexOf(BridgeCodes.Busy, StringComparison.Ordinal) >= 0) return new BridgeException(BridgeCodes.Busy, error);
            return new BridgeException(BridgeCodes.BadArgs, error, new BridgeErrorDetail { Arg = "path" });
        }

        // a subfolder of the pose root, guarded like every relative path: the root's Resolve takes file paths and
        // refuses an existing directory, so a name inside the folder is resolved and its folder returned
        static string PoseDir(CoreContext c, string root, string dir)
        {
            try { return Path.GetDirectoryName(c.App.RootFor(root).Resolve(dir.TrimEnd('/', '\\') + "/_", overwrite: true, createDirectory: false)); }
            catch (BridgeException e) when (e.Code == BridgeCodes.IoDenied || e.Code == BridgeCodes.BadArgs)
            {
                throw new BridgeException(e.Code, e.Error.Message, new BridgeErrorDetail { Arg = "dir" });
            }
        }

        static object PoseList(CoreContext c)
        {
            string source = c.Args.Get("source", "hs2") ?? "hs2";
            if (source != "hs2" && source != "hs2next" && source != "all")
                throw new BridgeException(BridgeCodes.BadArgs, "unknown source \"" + source + "\"", new BridgeErrorDetail { Arg = "source", Expected = "hs2, hs2next or all", Candidates = new[] { "hs2", "hs2next", "all" } });
            string root = c.Paths.Hs2PoseDir;
            bool scanHs2 = source != "hs2next" && !string.IsNullOrEmpty(root);
            if (source == "hs2" && !scanHs2) throw CommandKit.NotFound("no HS2 pose folder (the HS2 install is not configured)", "dir");
            string q = c.Args.Get("q", "") ?? "";
            string dir = c.Args.Get("dir", "") ?? "";
            int limit = c.Args.Get("limit", 200);
            if (limit < 1 || limit > 100000) throw CommandKit.BadArgs("limit", "1–100000");
            var poses = new List<object>();
            int total = 0;
            string rootFull = null, ownFull = null;
            if (scanHs2)
            {
                rootFull = Path.GetFullPath(root).TrimEnd('\\', '/');
                string scanRoot = dir.Trim().Length == 0 ? rootFull : PoseDir(c, rootFull, dir).TrimEnd('\\', '/');
                if (Directory.Exists(scanRoot))
                {
                    // paths relative to the pose root (what pose.load takes), also when a subfolder is scanned
                    string prefix = scanRoot.Length > rootFull.Length ? scanRoot.Substring(rootFull.Length).Trim('\\', '/').Replace('\\', '/') + "/" : "";
                    var scan = PoseLibrary.Scan(scanRoot);
                    foreach (var e in scan.Entries)
                    {
                        if (e.Status != PoseScanStatus.Pose) continue;
                        string rel = prefix + e.RelativePath;
                        if (q.Length > 0 && rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && (e.Name ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        total++;
                        if (poses.Count < limit)
                            poses.Add(new BridgeReply { ["path"] = rel, ["name"] = e.Name ?? "", ["sex"] = e.Sex, ["kind"] = e.Kind == PoseContainerKind.Dat ? "dat" : "png", ["source"] = "hs2" });
                    }
                }
                else if (source == "hs2") throw CommandKit.NotFound("no folder " + scanRoot, "dir");
            }
            if (source != "hs2")
            {
                // HS2Next's pose fragments: <UserData>/pose, recursive
                ownFull = Path.GetFullPath(c.Paths.PoseDir).TrimEnd('\\', '/');
                string scanRoot = ownFull;
                if (dir.Trim().Length > 0)
                {
                    Directory.CreateDirectory(ownFull);
                    scanRoot = PoseDir(c, ownFull, dir).TrimEnd('\\', '/');
                }
                if (Directory.Exists(scanRoot))
                {
                    var files = Directory.GetFiles(scanRoot, "*" + SaveFormats.PoseExtension, SearchOption.AllDirectories);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (var f in files)
                    {
                        string rel = FileRoots.Relative(f, ownFull);
                        var (title, sex) = FragmentManifest(f);
                        if (title == null) continue;   // not an hs2next.pose container
                        if (q.Length > 0 && rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        total++;
                        if (poses.Count < limit)
                            poses.Add(new BridgeReply { ["path"] = rel, ["name"] = title, ["sex"] = sex ?? 1, ["kind"] = "hs2npose", ["source"] = "hs2next" });
                    }
                }
                else if (source == "hs2next" && dir.Trim().Length > 0) throw CommandKit.NotFound("no folder " + scanRoot, "dir");
            }
            var rep = new BridgeReply
            {
                ["root"] = source == "hs2next" ? ownFull : rootFull ?? ownFull,
                ["poses"] = poses,
                ["total"] = total,
                ["roots"] = new BridgeReply { ["hs2"] = rootFull, ["hs2next"] = ownFull },
            };
            if (total > poses.Count) rep["truncated"] = true;
            return rep;
        }
    }

    public static partial class CoreCommands
    {
        static partial void RegisterFkPose(ICommandRegistry r, AppCore app) => FkPoseCommands.Register(r, app);
    }
}
