// The `history` panel (right sidebar by default): the redo entries (dimmed, next one nearest), then the
// undo entries newest first; clicking an entry undoes / redoes repeatedly up to it (one HistoryChanged per step).
// Rebuilt on HistoryChanged. Rows are named "history-undo-<i>" / "history-redo-<i>".
using System.Collections.Generic;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI
{
    public static class HistoryPanel
    {
        public const string Id = "history";

        public static void Register(UiContext ctx) =>
            ctx.Registry.Panels.Register(Id, "panel.history", LayoutRegions.Right, Build, ActionScope.Global, 100);

        static VisualElement Build(UiContext ctx)
        {
            var list = new ScrollView { name = "history-list" };
            void Fill()
            {
                list.Clear();
                var bus = ctx.Session.Commands;
                var redo = bus.RedoList;
                for (int i = redo.Count - 1; i >= 0; i--)
                {
                    int steps = i + 1;
                    var b = new Button(() => { for (int k = 0; k < steps; k++) ctx.Registry.Run(ActionIds.Redo, "history"); }) { name = "history-redo-" + i, text = redo[i].Label };
                    b.AddToClassList("hs-history__row");
                    b.AddToClassList("hs-history__row--redo");
                    list.Add(b);
                }
                var undo = bus.UndoList;
                for (int i = 0; i < undo.Count; i++)
                {
                    int steps = i;
                    var b = new Button(() => { for (int k = 0; k < steps; k++) ctx.Registry.Run(ActionIds.Undo, "history"); }) { name = "history-undo-" + i, text = undo[i].Label };
                    b.AddToClassList("hs-history__row");
                    if (i == 0) b.AddToClassList("hs-history__row--current");
                    list.Add(b);
                }
                var origin = new Button(() => { int n = ctx.Session.Commands.UndoList.Count; for (int k = 0; k < n; k++) ctx.Registry.Run(ActionIds.Undo, "history"); }) { name = "history-origin" };
                Loc.Bind(origin, "history.origin");
                origin.AddToClassList("hs-history__row");
                list.Add(origin);
            }
            ctx.HistoryChanged += _ => Fill();
            ctx.SceneLoaded += _ => Fill();
            Fill();
            return list;
        }
    }
}
