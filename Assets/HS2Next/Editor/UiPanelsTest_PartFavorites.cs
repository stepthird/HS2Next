// PF: favourites on the Maker's clothes and accessory lists, a step of the groups MC and MA on their own list. The store runs on
// a file of the test; the list's view state and the file are put back. Numbers and names of rows of the install only.
using System.Collections;
using System.IO;
using System.Linq;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    static string PfText(PartList list, string reference) => list.CellOf(reference)?.Q<Label>(className: "hs-card__text")?.text;

    static IEnumerator PfMenuOn(Ctx c, PartList list, string reference)
    {
        int pos = list.PositionOf(reference);
        list.Grid.ScrollToPos(pos);
        list.Grid.Select(pos);
        yield return PlayModeHarness.WaitFrames(2);
        list.Grid.RequestContextMenu();
    }

    static IEnumerator PfCheck(Ctx c, PartList list, string what, PartList other)
    {
        c.Check(list != null && list.Favoritable && list.Q<Button>("partlist-favorites") != null, what + ": PF1 the list has favourites and the button");
        if (list == null) yield break;
        c.Check(PartList.HasFavorites(ClothesRoot + "top.item") && PartList.HasFavorites(InspectorSchemas.AccessoryEditPath("+351", "item"))
                && !PartList.HasFavorites(FaceRoot + "skin") && !PartList.HasFavorites("appearance.chara.hair.back") && !PartList.HasFavorites("plugin/x"),
            what + ": PF2 clothes and accessory lists only, the add list too");
        string file = Path.Combine(Application.temporaryCachePath, "pf_favorites.json");
        if (File.Exists(file)) File.Delete(file);
        Favorites.FileOverride = file;
        var store = Favorites.Of(c.Ui);
        try
        {
            string kind = FavoriteStore.PartKind(list.Category), current = list.CurrentRef, undo = c.UndoLabel;
            var rows = list.AllItems.Where(i => !i.NotListed && i.Row != null && i.Ref != current).Take(2).ToList();
            c.Check(rows.Count == 2, what + ": PF3 two rows to mark (" + list.AllItems.Count + " in the list)");
            if (rows.Count < 2) yield break;
            string a = rows[0].Ref, b = rows[1].Ref;
            int all = list.Items.Count;

            yield return PfMenuOn(c, list, a);
            c.Check(ListMenus.LastMenuName == "partlist-context" && ListMenus.LastItems.Any(i => i.Id == "list-favorite" && !i.Checked), what + ": PF4 the cell's menu offers the favourite");
            c.Check(ListMenus.Run("list-favorite") && store.IsRow(kind, a), what + ": PF5 the menu marks the row in the store (" + kind + ")");
            yield return PlayModeHarness.WaitFrames(3);
            c.Check((PfText(list, a) ?? "").StartsWith(BrowserViews.Star), what + ": PF6 the cell shows the star (\"" + PfText(list, a) + "\")");
            c.Check(list.CurrentRef == current && c.UndoLabel == undo, what + ": PF7 the favourite neither picks the row nor adds a history entry");
            var doc = JObject.Parse(File.ReadAllText(file));
            c.Check(((JArray)doc["rows"]?[kind])?.Select(t => (string)t).Contains(a) == true, what + ": PF8 the file holds the row under its category");
            if (other != null)
                c.Check(other.Category != list.Category && !store.IsRow(FavoriteStore.PartKind(other.Category), a), what + ": PF9 another category's list does not share it");

            list.SetFavoritesOnly(true);
            yield return PlayModeHarness.WaitFrames(2);
            var button = list.Q<Button>("partlist-favorites");
            c.Check(list.FavoritesOnly && list.Items.Count(i => !i.NotListed) == 1 && list.PositionOf(a) >= 0 && button.ClassListContains(PartList.FavoritesOnClass),
                what + ": PF10 the button shows the favourite only (" + list.Items.Count + " cells)");
            store.SetRow(kind, b, true);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(list.PositionOf(b) >= 0 && list.Items.Count(i => !i.NotListed) == 2, what + ": PF11 a change of the store reaches the open list");
            yield return PfMenuOn(c, list, a);
            c.Check(ListMenus.LastItems.Any(i => i.Id == "list-favorite" && i.Checked) && ListMenus.Run("list-favorite") && !store.IsRow(kind, a), what + ": PF12 the menu takes the favourite off");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(list.PositionOf(a) < 0 && list.Items.Count(i => !i.NotListed) == 1, what + ": PF13 the row leaves the favourites view");

            var kept = FavoriteStore.Load(file);
            c.Check(kept.IsRow(kind, b) && !kept.IsRow(kind, a), what + ": PF14 a new load of the file has what the store has");
            list.SetFavoritesOnly(false);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(!list.FavoritesOnly && list.Items.Count == all && !button.ClassListContains(PartList.FavoritesOnClass), what + ": PF15 every row is back");
            store.SetRow(kind, b, false);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(!(PfText(list, b) ?? "").StartsWith(BrowserViews.Star), what + ": PF16 no star left");
        }
        finally
        {
            list.SetFavoritesOnly(false);
            Favorites.FileOverride = null;
            Favorites.Of(c.Ui);
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
