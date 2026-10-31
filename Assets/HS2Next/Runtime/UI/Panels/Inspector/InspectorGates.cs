// Gate predicates of the inspector schema (names in SchemaGates) evaluated per node
// from the realized state: CharaRealization (clothes state types, sex, look / FK flags), ItemFeatures, the
// light type, the map's option / light blocks, the animation row. A row is shown / enabled when its gate holds for the
// primary; an "All" edit is written only to the selected nodes where it holds, as for item colours: only
// items whose colour k is in use. Unknown gates hold (a new gate name never hides a row by accident).
using System;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;

namespace HS2Next.UI.Panels
{
    public static class InspectorGates
    {
        /// <summary>Whether <paramref name="gate"/> holds for node <paramref name="id"/> (null gate = always; scene gates ignore the id).</summary>
        public static bool Holds(StudioOps ops, string gate, string id)
        {
            if (string.IsNullOrEmpty(gate) || ops == null) return true;
            var rt = ops.Runtime;
            var m = ops.Model;
            if (gate.StartsWith("map.", StringComparison.Ordinal)) return MapGate(rt, m, gate);
            var n = id != null ? m.Find(id) : null;
            if (n == null) return false;
            if (gate.StartsWith("maker.", StringComparison.Ordinal)) return n.Kind == NodeKind.Chara && ops.MakerGateHolds(gate, n.Id);   // Phase 4.1
            if (gate.StartsWith("chara.", StringComparison.Ordinal)) return n.Kind == NodeKind.Chara && CharaGate(ops, n, gate);
            if (gate.StartsWith("item.", StringComparison.Ordinal)) return n.Kind == NodeKind.Item && ItemGate(rt, n, gate);
            if (gate.StartsWith("light.", StringComparison.Ordinal)) return n.Kind == NodeKind.Light && LightGate(ops, n, gate);
            if (gate.StartsWith("route.", StringComparison.Ordinal)) return n.Kind == NodeKind.Route && RouteGate(ops, n, gate);   // Phase 3.6 package R
            return true;
        }

        /// <summary>
        /// Route gates (Phase 3.6 package R): canPlay (two points or more), canAddPoint (the point
        /// limit), and the point gates of the sub-selected point of this route (point, point.start, point.other, point.curve,
        /// point.linkable = a Curve point after a Curve point, point.unlinked = speed / ease in use).
        /// </summary>
        static bool RouteGate(StudioOps ops, TreeNode n, string gate)
        {
            if (!(n.State is RouteNodeState st)) return false;
            if (gate == SchemaGates.RouteCanPlay) return st.Points.Count >= 2;
            if (gate == SchemaGates.RouteCanAddPoint) return HS2Next.Scenes.Routes.RouteGeometry.CanAddPoint(st, RouteSettings.PointLimit);
            int i = -1;
            var sub = ops.SubSelection;
            if (sub != null && StudioOps.TryParseRouteSub(sub, out var rid, out var pid, out _) && rid == n.Id) i = StudioOps.PointIndex(st, pid);
            if (i < 0) return false;
            switch (gate)
            {
                case SchemaGates.RoutePoint: return true;
                case SchemaGates.RoutePointStart: return i == 0;
                case SchemaGates.RoutePointOther: return i > 0;
                case SchemaGates.RoutePointCurve: return st.Points[i].ConnectionType == RouteConnection.Curve;
                case SchemaGates.RoutePointLinkable: return HS2Next.Scenes.Routes.RouteGeometry.LinkEffective(st, i);
                case SchemaGates.RoutePointUnlinked: return !HS2Next.Scenes.Routes.RouteGeometry.AbsorbedPoints(st).Contains(i);
            }
            return true;
        }

        static int Index(string gate)
        {
            int a = gate.IndexOf('['), b = gate.IndexOf(']');
            return a >= 0 && b > a && int.TryParse(gate.Substring(a + 1, b - a - 1), out int k) ? k : -1;
        }

        static bool CharaGate(StudioOps ops, TreeNode n, string gate)
        {
            var st = n.State as CharaNodeState;
            if (gate == SchemaGates.CharaFemale) return StudioOps.SexOf(n) == 1;
            if (gate == SchemaGates.CharaMale) return StudioOps.SexOf(n) == 0;
            if (gate.StartsWith("chara.clothes[", StringComparison.Ordinal))
            {
                var set = ops.ClothesStatesOf(n.Id);
                return !set.Known || set.IsStateKind(Index(gate));
            }
            if (st == null) return false;
            switch (gate)
            {
                case SchemaGates.CharaAnimeParams:
                {
                    // vanilla: interactable while the primary plays an H clip; HS2Next never plays one (list data only)
                    var row = AnimeRow(ops, st.Anime?.Ref);
                    return row != null && row.IsH;
                }
                case SchemaGates.CharaNeckEditable: return !(st.Fk != null && st.Fk.Enabled && st.Fk.Groups != null && st.Fk.Groups.Neck);
                case SchemaGates.CharaLookTarget: return st.Look != null && st.Look.Eyes == 4;
                case SchemaGates.CharaIkImport: return PoseProvenanceExt.Of(n)?.Ik != null;
                case SchemaGates.CharaFkOn: return st.Fk != null && st.Fk.Enabled;
                case SchemaGates.CharaIkOn: return st.Ik != null && st.Ik.Enabled;
            }
            return true;
        }

        /// <summary>The Studio animation row of a reference, or null.</summary>
        public static StudioAnime AnimeRow(StudioOps ops, AnimeRef r)
        {
            var db = ops?.Services?.Db;
            if (db == null || r == null) return null;
            foreach (var a in db.Anime.All)
                if (a.Group == r.Group && a.Category == r.Category && a.No == r.No && string.Equals(a.Guid, r.Source, StringComparison.OrdinalIgnoreCase))
                    return a;
            return null;
        }

        /// <summary>The features of a built item, else null.</summary>
        public static ItemFeatures Features(SceneRuntime rt, string id)
        {
            var r = rt?.FindItem(id);
            return r != null && r.State == RealizationState.Built ? r.Features : null;
        }

        static bool ItemGate(SceneRuntime rt, TreeNode n, string gate)
        {
            var f = Features(rt, n.Id);
            if (f == null) return false;
            int k = Index(gate);
            if (gate.StartsWith("item.color[", StringComparison.Ordinal)) return k >= 0 && k < 3 && f.Color[k];
            if (gate.StartsWith("item.metallic[", StringComparison.Ordinal)) return k >= 0 && k < 3 && f.Metallic[k];
            if (gate.StartsWith("item.pattern[", StringComparison.Ordinal)) return k >= 0 && k < 3 && f.Pattern[k];
            switch (gate)
            {
                case SchemaGates.ItemGlass: return f.Glass;
                case SchemaGates.ItemAlpha: return f.Alpha;
                case SchemaGates.ItemEmission: return f.Emission;
                case SchemaGates.ItemEmissionColor: return f.EmissionColor;
                case SchemaGates.ItemEmissionPower: return f.EmissionPower;
                case SchemaGates.ItemLightCancel: return f.LightCancel;
                case SchemaGates.ItemPanel: return f.Panel;
                case SchemaGates.ItemFk: return f.Fk;
                case SchemaGates.ItemDynamicBones: return f.DynamicBones > 0;
                case SchemaGates.ItemFkOrDynamicBones: return f.Fk || f.DynamicBones > 0;
                case SchemaGates.ItemOptions: return f.Options > 0;
                case SchemaGates.ItemAnimePatterns: return f.AnimePatternsShown;
                case SchemaGates.ItemAnimated: return f.Animated;
            }
            return true;
        }

        /// <summary>The Unity light type of a light node (the built light's; point while it is not built: the type lives only
        /// in the prefab).</summary>
        public static LightType TypeOf(StudioOps ops, TreeNode n)
        {
            var r = ops.Runtime?.Find(n.Id) as LightRealization;
            return r != null && r.Light ? r.Light.type : LightType.Point;
        }

        static bool LightGate(StudioOps ops, TreeNode n, string gate)
        {
            var t = TypeOf(ops, n);
            switch (gate)
            {
                case SchemaGates.LightPoint: return t == LightType.Point;
                case SchemaGates.LightSpot: return t == LightType.Spot;
                case SchemaGates.LightDirectional: return t == LightType.Directional;
                case SchemaGates.LightHasRange: return t == LightType.Point || t == LightType.Spot;
            }
            return true;
        }

        static bool MapGate(SceneRuntime rt, SceneModel m, string gate)
        {
            bool loaded = !string.IsNullOrEmpty(m.Settings?.Map?.Ref);
            if (gate == SchemaGates.MapLoaded) return loaded;
            var map = rt?.GetPart<MapRealization>();
            if (!loaded || map == null) return false;
            if (gate == SchemaGates.MapOptions) return map.Behaviour.FeatureOption;
            if (gate == SchemaGates.MapLights) return map.Behaviour.FeatureLight;
            return true;
        }
    }
}
