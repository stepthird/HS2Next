// StudioOps, selection: the session's SelectionModel is the single node selection (view
// state: not in history; undo / redo restore the selection of their entry); the sub-selection (one FK
// bone, IK target or the look target of one character) lives here so that the bridge (`select sub`) and the UI
// (UiContext.SubSelection reads / writes it) share it. Setting a sub-object makes its character the sole selection; a
// selection that no longer is exactly that character clears it.
// Phase 3.6 (R25; package R): the same one address also names a route point or its aid,
// "<routeId>/points/<pointId>" and "<routeId>/points/<pointId>/aid"; its route becomes the sole selection; an address whose
// point no longer exists (delete, undo of an add) is cleared.
// Phase 4.3 (package W): "<id>/hair/<part>/<index>" names a hair bundle's correction transform
// (part back / front / side / option, the CmpHair.boneInfo index); the handle of the Edit tab's Hair page edits its rates.
// Phase 4.5 (package W): "<id>/accmove/<accessoryId>/<set>" names transform set 0 or 1
// (trfMove01 / trfMove02) of an accessory; the handle of the Edit tab's Accessories page edits its position or rotation. An
// accessory that leaves the list (removal, undo of an add) takes its sub-selection with it.
using System;
using System.Collections.Generic;
using HS2Next.Motion.Ik;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        string sub;
        bool subHooked, settingSub;

        /// <summary>The selected sub-object address ("&lt;id&gt;/fk/&lt;bone&gt;", "&lt;id&gt;/ik/&lt;Target&gt;", "&lt;id&gt;/look",
        /// "&lt;routeId&gt;/points/&lt;pointId&gt;[/aid]") or null.</summary>
        public string SubSelection
        {
            get
            {
                HookSelection();
                return sub;
            }
        }

        /// <summary>Raised with the new address (or null) whenever <see cref="SubSelection"/> changes.</summary>
        public event Action<string> SubSelectionChanged;

        void HookSelection()
        {
            if (subHooked) return;
            subHooked = true;
            Session.Events.SelectionChanged += args =>
            {
                if (settingSub || sub == null) return;
                if (!TryParseSub(sub, out var id, out _, out _) || Session.Selection.Count != 1 || Session.Selection.Primary != id) SetSub(null);
            };
            Session.Events.Cleared += () => SetSub(null);
            Session.Events.Changed += b =>
            {
                if (sub == null) return;
                // an accessory that left its character's list takes its transform-set handle with it (Phase 4.5)
                if (TryParseAccessorySub(sub, out var cid, out var aid, out _))
                {
                    if (Model.Find(cid)?.Appearance?.Stored?.Coord?.FindAccessory(aid) == null) SetSub(null);
                    return;
                }
                // a route point that left the list (delete, undo of an add) takes its sub-selection with it
                if (!TryParseRouteSub(sub, out var rid, out var pid, out _)) return;
                var r = Model.Find(rid)?.State as RouteNodeState;
                if (r == null || r.Points.FindIndex(p => p?.Id == pid) < 0) SetSub(null);
            };
        }

        void SetSub(string address)
        {
            if (string.Equals(sub, address, StringComparison.Ordinal)) return;
            sub = address;
            SubSelectionChanged?.Invoke(sub);
        }

        /// <summary>Splits a sub-object address into the character id, the kind (fk, ik, look, hair, points) and the name (bone / target /
        /// "&lt;part&gt;/&lt;index&gt;" / point; null for look).</summary>
        public static bool TryParseSub(string address, out string id, out string kind, out string name)
        {
            id = kind = name = null;
            if (string.IsNullOrEmpty(address)) return false;
            var parts = address.Split(new[] { '/' }, 3);
            if (parts.Length < 2 || parts[0].Length == 0) return false;
            id = parts[0];
            kind = parts[1];
            if (kind == "look") return parts.Length == 2;
            if (kind == "points" && parts.Length == 3 && parts[2].Length > 0)
            {
                // "<pointId>" or "<pointId>/aid" (Phase 3.6 routes)
                int slash = parts[2].IndexOf('/');
                if (slash < 0) { name = parts[2]; return true; }
                if (slash > 0 && parts[2].Substring(slash) == "/aid") { name = parts[2]; return true; }
                name = null;
                return false;
            }
            if ((kind == "fk" || kind == "ik") && parts.Length == 3 && parts[2].Length > 0) { name = parts[2]; return true; }
            // Phase 4.3 package W: a hair bundle's correction transform, "<id>/hair/<part>/<index>"
            if (kind == "hair" && parts.Length == 3 && TryParseHairName(parts[2], out _, out _)) { name = parts[2]; return true; }
            // Phase 4.5 package W: an accessory transform set, "<id>/accmove/<accessoryId>/<0|1>"
            if (kind == AccessorySubKind && parts.Length == 3 && TryParseAccessoryName(parts[2], out _, out _)) { name = parts[2]; return true; }
            return false;
        }

        /// <summary>The sub-address kind of an accessory transform set.</summary>
        public const string AccessorySubKind = "accmove";

        /// <summary>Splits "&lt;accessoryId&gt;/&lt;set&gt;" (set 0 = trfMove01, 1 = trfMove02; the id is everything before the last '/').</summary>
        static bool TryParseAccessoryName(string name, out string accessoryId, out int set)
        {
            accessoryId = null; set = -1;
            int slash = name?.LastIndexOf('/') ?? -1;
            if (slash <= 0 || slash != name.Length - 2) return false;
            char c = name[name.Length - 1];
            if (c != '0' && c != '1') return false;
            accessoryId = name.Substring(0, slash);
            set = c - '0';
            return true;
        }

        /// <summary>Splits an accessory transform-set address "&lt;id&gt;/accmove/&lt;accessoryId&gt;/&lt;set&gt;" into the character id,
        /// the accessory id and the set (0 or 1).</summary>
        public static bool TryParseAccessorySub(string address, out string charaId, out string accessoryId, out int set)
        {
            charaId = accessoryId = null; set = -1;
            if (!TryParseSub(address, out var id, out var kind, out var name) || kind != AccessorySubKind) return false;
            charaId = id;
            return TryParseAccessoryName(name, out accessoryId, out set);
        }

        /// <summary>The address of transform set <paramref name="set"/> (0, 1) of accessory <paramref name="accessoryId"/> of a character.</summary>
        public static string AccessorySub(string charaId, string accessoryId, int set) =>
            charaId + "/" + AccessorySubKind + "/" + accessoryId + "/" + (set == 1 ? "1" : "0");

        /// <summary>The hair part names of the sub-address kind `hair` (HairKind order).</summary>
        static readonly string[] HairSubParts = { "back", "front", "side", "option" };

        /// <summary>Splits "&lt;part&gt;/&lt;index&gt;" (part back / front / side / option, index ≥ 0 in invariant digits).</summary>
        static bool TryParseHairName(string name, out int part, out int index)
        {
            part = index = -1;
            int slash = name?.IndexOf('/') ?? -1;
            if (slash <= 0) return false;
            part = Array.IndexOf(HairSubParts, name.Substring(0, slash));
            string digits = name.Substring(slash + 1);
            if (part < 0 || digits.Length == 0 || digits.Length > 6) return false;
            foreach (char ch in digits) if (ch < '0' || ch > '9') return false;
            index = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Splits a hair bundle sub-object address "&lt;id&gt;/hair/&lt;part&gt;/&lt;index&gt;" into the character id, the part
        /// (0..3 back, front, side, option) and the bundle index (CmpHair.boneInfo).</summary>
        public static bool TryParseHairSub(string address, out string charaId, out int part, out int index)
        {
            charaId = null; part = index = -1;
            if (!TryParseSub(address, out var id, out var kind, out var name) || kind != "hair") return false;
            charaId = id;
            return TryParseHairName(name, out part, out index);
        }

        /// <summary>The address of hair bundle <paramref name="index"/> of part <paramref name="part"/> (0..3) of a character.</summary>
        public static string HairSub(string charaId, int part, int index) =>
            charaId + "/hair/" + HairSubParts[part] + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Splits a route sub-object address into the route id, the point id and whether it names the point's aid.</summary>
        public static bool TryParseRouteSub(string address, out string routeId, out string pointId, out bool aid)
        {
            routeId = pointId = null;
            aid = false;
            if (!TryParseSub(address, out var id, out var kind, out var name) || kind != "points") return false;
            routeId = id;
            aid = name.EndsWith("/aid", StringComparison.Ordinal);
            pointId = aid ? name.Substring(0, name.Length - 4) : name;
            return pointId.Length > 0;
        }

        /// <summary>
        /// Sets the node selection (not recorded): <paramref name="ids"/> replace it, or with <paramref name="add"/> are appended
        /// (the primary stays the first). With <paramref name="subAddress"/> the address's character becomes the sole selection and
        /// the sub-selection is set (an IK target must be one of the 13 target names). Value = the sub-selection.
        /// </summary>
        public OpResult Select(IEnumerable<string> ids, bool add = false, string subAddress = null)
        {
            HookSelection();
            var list = new List<string>();
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var n = Find(id, "keys", out var fail);
                if (n == null) return fail;
                if (!list.Contains(n.Id)) list.Add(n.Id);
            }
            if (subAddress != null)
            {
                if (!TryParseSub(subAddress, out var cid, out var kind, out var name))
                    return OpResult.BadArgs("sub", "\"<id>/fk/<bone>\", \"<id>/ik/<Target>\", \"<id>/look\", \"<id>/hair/<part>/<index>\", \"<id>/accmove/<accessoryId>/<0|1>\" or \"<routeId>/points/<pointId>[/aid]\"");
                if (kind == "points")
                {
                    var rn = RouteNode(cid, "sub", out var rfail);
                    if (rn == null) return rfail;
                    TryParseRouteSub(subAddress, out _, out var pid, out bool aid);
                    if (((RouteNodeState)rn.State).Points.FindIndex(p => p?.Id == pid) < 0)
                        return OpResult.NotFound("no point \"" + pid + "\" in route " + rn.Id, "sub");
                    settingSub = true;
                    try { Session.Selection.Set(new[] { rn.Id }); }
                    finally { settingSub = false; }
                    SetSub(rn.Id + "/points/" + pid + (aid ? "/aid" : ""));
                    var rr = OpResult.Success();
                    rr.Value = sub;
                    return rr;
                }
                var nodes = Charas(new[] { cid }, "sub", out var fail);
                if (nodes == null) return fail;
                if (kind == "ik" && !Enum.TryParse(name, false, out IkTargetId _))
                    return OpResult.BadArgs("sub", "an IK target (" + string.Join(", ", Enum.GetNames(typeof(IkTargetId))) + ")");
                settingSub = true;
                try { Session.Selection.Set(new[] { nodes[0].Id }); }
                finally { settingSub = false; }
                SetSub(nodes[0].Id + "/" + kind + (name != null ? "/" + name : ""));
            }
            else
            {
                if (add) foreach (var id in list) Session.Selection.Add(id);
                else Session.Selection.Set(list);
                if (!add || list.Count > 0) SetSub(null);
            }
            var r = OpResult.Success();
            r.Value = sub;
            return r;
        }
    }
}
