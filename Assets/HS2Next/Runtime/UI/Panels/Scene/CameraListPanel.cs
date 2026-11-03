// The cameras panel (v0.3 trial 2; right sidebar tab «Cameras» by default): the scene's camera objects as a list —
// a header with «Add from the view» (a camera at the current view, one entry) and «Free camera» (back to the free view),
// then the «Field of view» row of the current view (a slider 1–60 with ↺ = 23, live while dragging: the free camera's
// view state as the keys write it, or the camera looked through, one entry "Camera" per drag);
// one row per camera node in tree order: a mark of the camera the view looks through, the name (click selects the node,
// double click looks through it), «View» (look through it, again = back to the free camera), «Update» (the camera to the
// current view: position, rotation, field of view; one entry) and «×» (delete). The list is virtualised (any number of
// cameras) and follows the scene's changes. The empty-state text and the status line wrap at any panel width. Also the
// actions add.cameraFromView and camera.lookThrough (the selected camera; on the camera already looked through, or with no
// camera selected while one is in use: back to the free camera).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class CameraModule : IUiModule
    {
        public int Order => 125;

        public void Register(UiContext ctx)
        {
            ctx.Registry.Panels.Register(CameraListPanel.PanelId, "panel.cameras", LayoutRegions.Right, c => new CameraListPanel(c), ActionScope.Inspector, 90);
            var a = ctx.Registry.Actions;
            a.Register(ActionIds.AddCameraFromView, _ =>
            {
                var r = ctx.ShowResult(ctx.Ops.AddCameraFromView(null, false, UiContext.User()));
                if (r != null && r.Ok && r.NewIds.Count > 0) ctx.Selection.Set(new[] { r.NewIds[0] });
                return r?.NewIds.FirstOrDefault();
            });
            a.Register(ActionIds.CameraLookThrough, _ => LookThrough(ctx));
        }

        /// <summary>The camera the view looks through (null = the free camera).</summary>
        public static string Active(UiContext ctx) => ctx.Runtime?.GetPart<SceneViewCamera>()?.ActiveCameraId;

        /// <summary>camera.lookThrough: the selected camera node; on the active one, or with no camera selected, the free camera.</summary>
        public static object LookThrough(UiContext ctx)
        {
            string active = Active(ctx);
            var cam = ctx.SelectedOfKind(NodeKind.Camera).FirstOrDefault();
            if (cam == null || cam == active)
            {
                if (active == null) { ctx.Toasts?.Show(L10n.Get("cameras.selectOne"), ToastKind.Info); return null; }
                return ctx.ShowResult(ctx.Ops.SetActiveCamera(null, UiContext.User()));
            }
            return ctx.ShowResult(ctx.Ops.SetActiveCamera(cam, UiContext.User()));
        }
    }

    public sealed class CameraListPanel : VisualElement
    {
        public const string PanelId = "cameras";

        readonly UiContext ctx;
        readonly ListView list;
        readonly Label empty, status;
        readonly List<string> ids = new List<string>();
        string shape, shownActive;
        bool dirty = true;

        /// <summary>The camera node ids in the list, in order (tests).</summary>
        public IReadOnlyList<string> Ids => ids;
        public ListView List => list;
        /// <summary>The «Field of view» row of the current view.</summary>
        public FovRow Fov { get; }
        /// <summary>The empty-state text («No cameras yet …») and the status line (tests).</summary>
        public Label Empty => empty;
        public Label Status => status;

        public CameraListPanel(UiContext ctx)
        {
            this.ctx = ctx;
            name = "cameras-panel";
            PanelKit.Style(this);
            AddToClassList("hs-panel");
            var header = new VisualElement();
            header.AddToClassList("hs-panel__header");
            var add = Loc.Button("cameras.addFromView", () => ctx.Registry.Run(ActionIds.AddCameraFromView, "panel"));
            add.name = "cameras-add";
            Loc.Tooltip(add, "cameras.addFromView.hint");
            var free = Loc.Button("camera.free", () => ctx.ShowResult(ctx.Ops.SetActiveCamera(null, UiContext.User())));
            free.name = "cameras-free";
            header.Add(add);
            header.Add(free);
            Add(header);
            var fovBox = new VisualElement { name = "cameras-fov-box" };
            fovBox.AddToClassList("hs-panel__header");
            fovBox.style.flexDirection = FlexDirection.Column;
            fovBox.style.alignItems = Align.Stretch;
            Fov = new FovRow(ctx, "cameras-fov");
            fovBox.Add(Fov);
            Add(fovBox);
            list = new ListView
            {
                name = "cameras-list", fixedItemHeight = 26, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single, itemsSource = ids, makeItem = MakeRow, bindItem = BindRow,
            };
            list.AddToClassList("hs-grow");
            list.selectionChanged += sel =>
            {
                foreach (var o in sel) if (o is string id && ctx.Model.Find(id) != null && !(ctx.Selection.Ids.Count == 1 && ctx.Selection.Primary == id)) ctx.Selection.Set(new[] { id });
            };
            list.itemsChosen += sel => { foreach (var o in sel) if (o is string id) View(id); };
            Add(list);
            empty = Loc.Label("cameras.none");
            empty.name = "cameras-empty";
            empty.AddToClassList("hs-panel__empty");
            Add(empty);
            var footer = new VisualElement();
            footer.AddToClassList("hs-panel__footer");
            status = new Label { name = "cameras-status" };
            status.AddToClassList("hs-status-text");
            // a long camera name wraps the line instead of running out of a narrow panel
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.flexShrink = 1;
            status.style.minWidth = 0;
            footer.Add(status);
            Add(footer);
            ctx.SceneChanged += _ => dirty = true;   // names, adds, deletes, camera.fov, camera.active (the rows compare cheaply)
            ctx.SceneLoaded += _ => dirty = true;
            ctx.SceneCleared += () => dirty = true;
            ctx.SelectionChanged += _ => SyncSelection();
            ctx.ViewStateChanged += w => { if (w == "language") { dirty = true; shape = null; } };
            schedule.Execute(Refresh).Every(200);
            Refresh();
        }

        /// <summary>Rebuilds the rows when the cameras, their names or the active one changed (also every 200 ms; tests call it).</summary>
        public void Refresh()
        {
            string active = CameraModule.Active(ctx);
            if (!dirty && active == shownActive && shape != null) return;
            dirty = false;
            var cams = ctx.Ops?.AllCameras() ?? new List<TreeNode>();
            var sb = new System.Text.StringBuilder();
            foreach (var n in cams) sb.Append(n.Id).Append(':').Append(n.Label).Append(':').Append((n.State as CameraNodeState)?.Fov).Append('|');
            string sh = sb.ToString();
            if (sh != shape || active != shownActive)
            {
                shape = sh;
                shownActive = active;
                ids.Clear();
                foreach (var n in cams) ids.Add(n.Id);
                list.RefreshItems();
                SyncSelection();
            }
            empty.style.display = ids.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            list.style.display = ids.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            var an = active != null ? ctx.Model.Find(active) : null;
            status.text = L10n.Format("cameras.status", ids.Count, an != null ? an.Label : L10n.Get("camera.free"));
        }

        void SyncSelection()
        {
            string p = ctx.Selection.Primary;
            int i = p != null ? ids.IndexOf(p) : -1;
            if (i >= 0) { if (list.selectedIndex != i) list.SetSelectionWithoutNotify(new[] { i }); }
            else if (list.selectedIndex >= 0) list.ClearSelection();
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement { name = "camera-row" };
            row.AddToClassList("hs-row");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var mark = new Label { name = "camera-mark" };
            mark.style.width = 16;
            var label = new Label { name = "camera-name" };
            label.AddToClassList("hs-row__label");
            label.style.flexGrow = 1;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            var fov = new Label { name = "camera-fov" };
            fov.AddToClassList("hs-status-text");
            var view = new Button { name = "camera-view" };
            view.AddToClassList("hs-seg");
            var update = Loc.Button("cameras.update", null);
            update.name = "camera-update";
            Loc.Tooltip(update, "cameras.update.hint");
            var del = new Button { name = "camera-delete", text = "×" };
            Loc.Tooltip(del, "cameras.delete");
            row.Add(mark); row.Add(label); row.Add(fov); row.Add(view); row.Add(update); row.Add(del);
            view.clicked += () => { if (row.userData is string id) View(id); };
            update.clicked += () => { if (row.userData is string id) ctx.ShowResult(ctx.Ops.UpdateCameraFromView(id, UiContext.User())); };
            del.clicked += () => { if (row.userData is string id) ctx.ShowResult(ctx.Ops.Delete(new[] { id }, UiContext.User())); };
            return row;
        }

        void BindRow(VisualElement row, int i)
        {
            if (i < 0 || i >= ids.Count) return;
            string id = ids[i];
            var n = ctx.Model.Find(id);
            row.userData = id;
            row.name = "camera-row-" + id;
            bool on = id == shownActive;
            row.Q<Label>("camera-mark").text = on ? "◉" : "○";
            row.Q<Label>("camera-name").text = n?.Label ?? id;
            float? f = (n?.State as CameraNodeState)?.Fov;
            row.Q<Label>("camera-fov").text = f.HasValue ? L10n.Format("cameras.fov", f.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) : "";
            var view = row.Q<Button>("camera-view");
            view.text = L10n.Get(on ? "cameras.viewing" : "cameras.view");
            view.tooltip = L10n.Get(on ? "cameras.viewing.hint" : "cameras.view.hint");
            view.EnableInClassList("hs-seg--on", on);
        }

        /// <summary>Looks through the camera (on the one in use: back to the free camera).</summary>
        public void View(string id)
        {
            string active = CameraModule.Active(ctx);
            ctx.ShowResult(ctx.Ops.SetActiveCamera(id == active ? null : id, UiContext.User()));
            dirty = true;
            Refresh();
        }
    }
}
