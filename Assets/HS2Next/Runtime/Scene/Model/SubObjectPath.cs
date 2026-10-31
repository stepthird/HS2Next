// Sub-object addresses: the strings gizmos, the bridge and
// ext data use for parts of a node without an id of their own. Each is equivalent to a field path:
//   <id>/fk/<bone>                       chara.fk.bones["<bone>"]  (item: item.fk.bones["<bone>"])
//   <id>/ik/<Target>                     chara.ik.targets.<Target>
//   <id>/look                            chara.look.target
//   <id>/acc/<accessoryId>               chara.state.accessories["<accessoryId>"]
//   <routeId>/points/<pointId>           route.points[i]
//   <routeId>/points/<pointId>/aid       route.points[i].aid
// Route point ids map to list indices through the model, so both directions take it. A bone name containing
// '/' is addressable only by the field path.
using System;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class SubObjectPath
    {
        /// <summary>The node id and field path of an address; false when the address has no known form, the node is not in
        /// the model or of the wrong kind, the route point does not exist, or the accessory is not in a known coordinate.</summary>
        public static bool TryParse(SceneModel m, string address, out string nodeId, out string fieldPath)
        {
            nodeId = null;
            fieldPath = null;
            if (m == null || string.IsNullOrEmpty(address)) return false;
            var parts = address.Split('/');
            if (parts.Length < 2) return false;
            var n = m.Find(parts[0]);
            if (n == null) return false;
            string p = null;
            switch (parts[1])
            {
                case "fk":
                    if (parts.Length != 3 || parts[2].Length == 0) return false;
                    if (n.Kind == NodeKind.Chara) p = "chara.fk.bones[" + FieldPath.Quote(parts[2]) + "]";
                    else if (n.Kind == NodeKind.Item) p = "item.fk.bones[" + FieldPath.Quote(parts[2]) + "]";
                    break;
                case "ik":
                    if (parts.Length == 3 && n.Kind == NodeKind.Chara && Array.IndexOf(IkTargets.Names, parts[2]) >= 0) p = "chara.ik.targets." + parts[2];
                    break;
                case "look":
                    if (parts.Length == 2 && n.Kind == NodeKind.Chara) p = "chara.look.target";
                    break;
                case "acc":
                    if (parts.Length == 3 && n.Kind == NodeKind.Chara && parts[2].Length > 0)
                    {
                        var ids = AccessoryNormalizer.KnownIds(n);
                        if (ids != null && !Contains(ids, parts[2])) return false;
                        p = "chara.state.accessories[" + FieldPath.Quote(parts[2]) + "]";
                    }
                    break;
                case "points":
                    if ((parts.Length == 3 || (parts.Length == 4 && parts[3] == "aid")) && n.Doc.State is RouteNodeState r)
                    {
                        int i = r.Points.FindIndex(x => x?.Id == parts[2]);
                        if (i < 0) return false;
                        p = "route.points[" + i + "]" + (parts.Length == 4 ? ".aid" : "");
                    }
                    break;
            }
            if (p == null) return false;
            nodeId = n.Id;
            fieldPath = p;
            return true;
        }

        /// <summary>The address of a field path of a node, or null when the path has no sub-object form.</summary>
        public static string Format(SceneModel m, string nodeId, string fieldPath)
        {
            if (m == null || nodeId == null || !FieldPath.TryParse(fieldPath, out var fp, out _)) return null;
            var n = m.Find(nodeId);
            if (n == null) return null;
            var s = fp.Steps;
            bool Names(params string[] names)
            {
                if (s.Count < names.Length) return false;
                for (int i = 0; i < names.Length; i++) if (s[i].Kind != FieldStepKind.Name || s[i].Name != names[i]) return false;
                return true;
            }
            if (s.Count == 4 && (Names("chara", "fk", "bones") && n.Kind == NodeKind.Chara || Names("item", "fk", "bones") && n.Kind == NodeKind.Item) &&
                s[3].Kind == FieldStepKind.Key && s[3].Name.IndexOf('/') < 0 && s[3].Name.Length > 0)
                return n.Id + "/fk/" + s[3].Name;
            if (s.Count == 4 && Names("chara", "ik", "targets") && s[3].Kind == FieldStepKind.Name && Array.IndexOf(IkTargets.Names, s[3].Name) >= 0)
                return n.Id + "/ik/" + s[3].Name;
            if (s.Count == 3 && Names("chara", "look", "target")) return n.Id + "/look";
            if (s.Count == 4 && Names("chara", "state", "accessories") && s[3].Kind == FieldStepKind.Key && s[3].Name.IndexOf('/') < 0 && s[3].Name.Length > 0)
                return n.Id + "/acc/" + s[3].Name;
            if ((s.Count == 3 || s.Count == 4 && s[3].Kind == FieldStepKind.Name && s[3].Name == "aid") && Names("route", "points") && s[2].Kind == FieldStepKind.Index &&
                n.Doc.State is RouteNodeState r && s[2].Index < r.Points.Count && r.Points[s[2].Index]?.Id != null)
                return n.Id + "/points/" + r.Points[s[2].Index].Id + (s.Count == 4 ? "/aid" : "");
            return null;
        }

        static bool Contains(System.Collections.Generic.IReadOnlyList<string> ids, string id)
        {
            foreach (var x in ids) if (x == id) return true;
            return false;
        }
    }
}
