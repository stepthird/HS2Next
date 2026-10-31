// Package C's workspace module: registers the "workspace" panel (left
// region, Tree scope) and the tree / edit actions: edit.rename, edit.duplicate (the copies are selected, U14),
// edit.delete (confirmation only with the option general.confirmDelete, U17), edit.selectAll (tree focus: the visible rows;
// else every node), edit.deselect, tree.prev / next / collapse / expand, add.folder, add.camera (add.route: Phase 3.6,
// unavailable), view.isolate, view.hideSelected, view.showAll.
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;

namespace HS2Next.UI.Panels
{
    public sealed class WorkspaceModule : IUiModule
    {
        public int Order => 100;

        public void Register(UiContext ctx)
        {
            ctx.Registry.Panels.Register(WorkspacePanel.PanelId, "panel.workspace", LayoutRegions.Left, c => new WorkspacePanel(c), ActionScope.Tree);
            var a = ctx.Registry.Actions;
            WorkspacePanel P() => WorkspacePanel.Current;
            bool Any() => ctx.Selection.Count > 0;

            a.Register(ActionIds.Rename, _ => { P()?.BeginRename(ctx.Selection.Primary); return null; },
                () => P() != null && ctx.Selection.Primary != null);
            a.Register(ActionIds.Duplicate, _ => ctx.ShowResult(ctx.Ops.Duplicate(ctx.Selection.Ids.ToList(), UiContext.User())), Any);
            a.Register(ActionIds.Delete, _ => { Delete(ctx); return null; }, Any);
            a.Register(ActionIds.SelectAll, call =>
            {
                bool tree = CInput.FocusInside(ctx, P()) || call.Arg("tree") is bool b && b;
                var ids = tree && P() != null ? P().VisibleNodeIds() : ctx.Model.PreOrder().Select(n => n.Id).ToList();
                ctx.Selection.Set(ids);
                return ids.Count;
            });
            a.Register(ActionIds.Deselect, _ => { ctx.Selection.Clear(); return null; });
            a.Register(ActionIds.TreePrev, _ => { P()?.Step(-1); return null; }, () => P() != null);
            a.Register(ActionIds.TreeNext, _ => { P()?.Step(1); return null; }, () => P() != null);
            a.Register(ActionIds.TreeCollapse, _ => { P()?.SetPrimaryExpanded(false); return null; }, () => P() != null);
            a.Register(ActionIds.TreeExpand, _ => { P()?.SetPrimaryExpanded(true); return null; }, () => P() != null);
            a.Register(ActionIds.AddFolder, _ => { PanelKit.ApplyPlacement(ctx); return ctx.ShowResult(ctx.Ops.AddFolder(null, null, -1, UiContext.User())); });
            a.Register(ActionIds.AddCamera, _ => { PanelKit.ApplyPlacement(ctx); return ctx.ShowResult(ctx.Ops.AddCamera(null, null, -1, UiContext.User())); });
            a.Register(ActionIds.AddRoute, _ => null, () => false);   // Phase 3.6
            a.Register(ActionIds.ViewIsolate, _ => { P()?.ToggleIsolate(); return P()?.IsIsolated; }, () => P() != null && (P().IsIsolated || Any()))
                .IsChecked = () => P()?.IsIsolated == true;
            a.Register(ActionIds.ViewHideSelected, _ => ctx.ShowResult(ctx.Ops.SetVisible(ctx.Selection.Ids.ToList(), false, UiContext.User())), Any);
            a.Register(ActionIds.ViewShowAll, _ =>
            {
                var hidden = ctx.Model.PreOrder().Where(n => !n.Doc.Visible).Select(n => n.Id).ToList();
                return hidden.Count == 0 ? null : ctx.ShowResult(ctx.Ops.SetVisible(hidden, true, UiContext.User()));
            });
        }

        /// <summary>Deletes the selection (one entry "Delete"; nothing selected afterwards); asks first with general.confirmDelete.</summary>
        public static void Delete(UiContext ctx)
        {
            var ids = ctx.Selection.Ids.ToList();
            if (ids.Count == 0) return;
            void Run() => ctx.ShowResult(ctx.Ops.Delete(ids, UiContext.User()));
            if (ctx.Options.Bool(OptionDefs.ConfirmDelete) && ctx.Dialogs != null)
            {
                ctx.Dialogs.Confirm("dialog.delete.title", "dialog.delete.message", ok => { if (ok) Run(); }, ids.Count);   // A's text: "Delete {0} selected object(s)?"
            }
            else Run();
        }
    }
}
