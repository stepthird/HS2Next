// MaterialEditTest, the mosaic rule on the map (package R; a group of the play row: a map loads only in play mode). No map row of this install
// has a mosaic slot, so a renderer joins the set through MosaicSet.Add. The item's part is in MosaicTest_Materials.cs.
//   MO3  a hand-edited array on the map naming that renderer and its materials: kept, not applied, status and finding; the command refused, no entry; the renderer stays drawn.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;

public static partial class MaterialEditTest
{
    static IEnumerator Mo3Map()
    {
        yield return OkCall(new { cmd = "scene.new" }, "MO3 scene.new");
        yield return LoadMap(PlayMap, "MO3 map");
        var t = MapTarget;
        var r = t.Renderers.FirstOrDefault(x => x && x.enabled && x.sharedMaterials.Length == 1 && x.sharedMaterials[0]);
        if (!Check("MO3 map: a renderer to add", r, "")) yield break;
        string path = t.PathOf(r);
        string mat = MeKey.Format(r.sharedMaterials[0].name);
        bool sole = t.Renderers.Count(x => x && x != r && x.sharedMaterials.Any(m => m && MeKey.Format(m.name) == mat)) == 0;
        Check("MO3 map: the set takes it", MosaicSet.Add(r), path);
        var mine = r.sharedMaterials[0];
        yield return Call(new { cmd = "history" });
        int entries = (last["undo"] as JArray)?.Count ?? 0;
        yield return Call(new JObject { ["cmd"] = "map.materials.set", ["type"] = "renderer", ["renderer"] = path, ["property"] = "Enabled", ["value"] = 0 }.ToString());
        Check("MO3 map: the command refuses", !LastOk && last.Value<string>("code") == "unsupported" && lastText.Contains(MosaicSet.MeProtectedCode), Short(lastText));
        yield return Call(new { cmd = "history" });
        int after = (last["undo"] as JArray)?.Count ?? 0;
        Check("MO3 map: no entry", after == entries, entries + " -> " + after);
        var items = new List<MaterialOverride>
        {
            new MeRenderer { Renderer = path, Property = "Enabled", Value = 0 },
            new MeShader { Material = "c_m_mnpb", Shader = "HS2Next/AIT Clothes True" },
            new MeShader { Material = mat, Shader = MosaicSet.OriginalShader },
        };
        SetMapArray(items);
        yield return Settled();
        Check("MO3 map: kept", MapArray.Count == 3, MapArray.Count + " items");
        Check("MO3 map: the renderer stays drawn", r.enabled, "");
        Check("MO3 map: its material stays", r.sharedMaterials[0] == mine, "");
        Check("MO3 map: Enabled reads protected", MapTarget.Status(MapArray[0]).Status == MaterialStatus.MosaicProtected, MapTarget.Status(MapArray[0]).Status);
        Check("MO3 map: a swap to a mosaic shader reads protected", MapTarget.Status(MapArray[2]).Status == MaterialStatus.MosaicProtected, MapTarget.Status(MapArray[2]).Status);
        Info($"MO3 map: {path}, material {mat}, the only holder {sole}");
        yield return OkCall(new { cmd = "scene.new" }, "MO3 scene.new");
        yield return Settled();
    }
}
