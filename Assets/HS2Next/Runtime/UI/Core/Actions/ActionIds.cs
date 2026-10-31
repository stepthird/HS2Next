// The action registry manifest (Appendix C): every command the UI offers, with its
// stable id, scope and kind. Menus, toolbar buttons, context menus, the key-binding overlay, the keymap presets and the
// bridge's `ui.action` all use these ids. Unity-free (folder rule): the runtime registry of package A attaches
// the Execute / CanExecute delegates to these entries. The label key of an action is "action." + id (strings.tsv).
using System;
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    /// <summary>Where a binding is active (resolution order: the focused panel's scope, then Viewport when the pointer is
    /// over it, then Global).</summary>
    public enum ActionScope { Global, Viewport, Tree, Inspector, Browser }

    /// <summary>Trigger = runs once per key press; Held = polled every frame while its chord is down (camera keys);
    /// Modifier = a held key queried by another gesture (click modifiers, drag modifiers), never run by a key press.</summary>
    public enum ActionKind { Trigger, Held, Modifier }

    /// <summary>The ids of every action (constants so call sites are checked by the compiler).</summary>
    public static class ActionIds
    {
        // edit
        public const string Undo = "edit.undo", Redo = "edit.redo", Duplicate = "edit.duplicate", Delete = "edit.delete",
            SelectAll = "edit.selectAll", Deselect = "edit.deselect", Rename = "edit.rename",
            CopyTransform = "edit.copyTransform", PasteTransform = "edit.pasteTransform", MoveToPivot = "edit.moveToPivot";
        /// <summary>Shows a section of the primary character's Edit tab: args {node, page?, section?} (Phase 4.1,
        /// ; automation without pixel positions; unbound).</summary>
        public const string EditShow = "edit.show";

        // tools
        public const string ToolSelect = "tool.select", ToolMove = "tool.move", ToolRotate = "tool.rotate", ToolScale = "tool.scale",
            ToolSpace = "tool.space", ToolPivot = "tool.pivot", ToolSnapToggle = "tool.snapToggle";

        // view toggles
        public const string ViewGizmos = "view.gizmos", ViewPlanes = "view.planes", ViewCentre = "view.centre",
            ViewPivotMarker = "view.pivotMarker", ViewGimmickIcons = "view.gimmickIcons", ViewLightHelpers = "view.lightHelpers",
            ViewCameraIcons = "view.cameraIcons", ViewKinematicHandles = "view.kinematicHandles", ViewPanels = "view.panels",
            ViewPhysics = "view.physics", ViewHideSelected = "view.hideSelected", ViewShowAll = "view.showAll",
            ViewIsolate = "view.isolate", ViewFps = "view.fps";
        /// <summary>Route lines and point markers in the view (Phase 3.6; option view.routes).</summary>
        public const string ViewRoutes = "view.routes";

        // camera
        public const string CameraFrame = "camera.frame", CameraReset = "camera.reset", CameraResetAngles = "camera.resetAngles",
            CameraRollReset = "camera.rollReset", CameraFovReset = "camera.fovReset",
            CameraMoveLeft = "camera.moveLeft", CameraMoveRight = "camera.moveRight",
            CameraMoveForward = "camera.moveForward", CameraMoveBack = "camera.moveBack",
            CameraMoveUp = "camera.moveUp", CameraMoveDown = "camera.moveDown",
            CameraDollyIn = "camera.dollyIn", CameraDollyOut = "camera.dollyOut",
            CameraOrbitUp = "camera.orbitUp", CameraOrbitDown = "camera.orbitDown",
            CameraOrbitLeft = "camera.orbitLeft", CameraOrbitRight = "camera.orbitRight",
            CameraRollPlus = "camera.rollPlus", CameraRollMinus = "camera.rollMinus",
            CameraFovDown = "camera.fovDown", CameraFovUp = "camera.fovUp",
            CameraNextObject = "camera.nextObject", CameraRelease = "camera.release",
            // v0.3 trial 2: look through the selected camera object (again = back to the free camera)
            CameraLookThrough = "camera.lookThrough";

        /// <summary>"camera.slotLoad0" … "camera.slotLoad9" (key 1 → slot 0, …, key 0 → slot 9).</summary>
        public static string CameraSlotLoad(int slot) => "camera.slotLoad" + Slot(slot);
        /// <summary>"camera.slotSave0" … "camera.slotSave9".</summary>
        public static string CameraSlotSave(int slot) => "camera.slotSave" + Slot(slot);

        static string Slot(int slot) =>
            slot >= 0 && slot <= 9 ? slot.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw new ArgumentOutOfRangeException(nameof(slot));

        // files and app
        public const string FileNew = "file.new", FileOpen = "file.open", FileSave = "file.save", FileSaveAs = "file.saveAs",
            FileImport = "file.import", FileOptions = "file.options", AppQuit = "app.quit";
        /// <summary>File ▸ Setup guide (Phase 5.3): the first-run guide again (a player restarts into it).</summary>
        public const string FileSetupGuide = "file.setupGuide";

        // add menu
        public const string AddFemale = "add.female", AddMale = "add.male", AddItem = "add.item", AddLight = "add.light",
            AddCamera = "add.camera", AddFolder = "add.folder", AddRoute = "add.route",
            // v0.3 trial 2: a camera object at the current view
            AddCameraFromView = "add.cameraFromView";

        // scene time (Phase 3.6, the global pause; Space in both presets, step unbound, R21)
        public const string TimePause = "time.pause", TimeStep = "time.step";

        // screenshots (Phase 3.6, F11 takes one, Shift+F11 opens the dialog, both presets)
        public const string CaptureScreenshot = "capture.screenshot", CaptureDialog = "capture.dialog";

        // route context actions (Phase 3.6, tree context menu, inspector, scene panel's route
        // list; unbound). Play / stop are not recorded (R7).
        public const string RoutePlay = "route.play", RouteStop = "route.stop", RouteAddPoint = "route.addPoint",
            RouteDeletePoint = "route.deletePoint", RouteResetAid = "route.resetAid", RoutePlayAll = "route.playAll",
            RouteRestartAll = "route.restartAll", RouteStopAll = "route.stopAll";

        // help
        public const string HelpKeys = "help.keys", HelpAbout = "help.about";

        // ui (language, layout)
        public const string UiLanguage = "ui.language", UiResetLayout = "ui.resetLayout";

        // tree keyboard navigation (tree focus only)
        public const string TreePrev = "tree.prev", TreeNext = "tree.next", TreeCollapse = "tree.collapse", TreeExpand = "tree.expand";

        // modifiers held during gestures (Kind Modifier)
        public const string GizmoSnapHold = "gizmo.snapHold", GizmoPrecisionHold = "gizmo.precisionHold", GizmoResize = "gizmo.resize";
        /// <summary>hs2studio click modifiers: P = put the primary under the clicked node, P+Ctrl = add the clicked node
        /// and put the others under the primary, O = detach the clicked node, O+Ctrl = detach the selection, X+Ctrl = add the
        /// clicked node and copy the primary's transform to the others.</summary>
        public const string ClickParent = "click.parent", ClickParentAll = "click.parentAll", ClickDetach = "click.detach",
            ClickDetachAll = "click.detachAll", ClickCopyTransform = "click.copyTransform";

        /// <summary>Fixed actions of the resolver (not in keymaps): a modal dialog's Enter / Esc, Esc inside a text field.</summary>
        public const string DialogAccept = "dialog.accept", DialogCancel = "dialog.cancel", TextBlur = "text.blur";

        /// <summary>The label key of an action.</summary>
        public static string LabelKey(string id) => "action." + id;
    }

    /// <summary>A manifest entry: the static facts of an action (no behaviour).</summary>
    public sealed class ActionInfo
    {
        public readonly string Id;
        public readonly ActionScope Scope;
        public readonly ActionKind Kind;
        /// <summary>Runs while a text field has focus (none by default).</summary>
        public readonly bool TextAllowed;
        /// <summary>Key repeat triggers it again (Held actions are always repeat-friendly).</summary>
        public readonly bool Repeatable;
        /// <summary>Menu / help group ("edit", "tool", "view", "camera", …) = the id's first segment.</summary>
        public string Group => Id.Substring(0, Id.IndexOf('.'));
        public string LabelKey => ActionIds.LabelKey(Id);

        public ActionInfo(string id, ActionScope scope, ActionKind kind = ActionKind.Trigger, bool textAllowed = false, bool repeatable = false)
        {
            if (string.IsNullOrEmpty(id) || id.IndexOf('.') <= 0) throw new ArgumentException("action ids are dotted: " + id);
            Id = id;
            Scope = scope;
            Kind = kind;
            TextAllowed = textAllowed;
            Repeatable = repeatable || kind == ActionKind.Held;
        }

        public override string ToString() => Id + " (" + Scope + ", " + Kind + ")";
    }

    /// <summary>The manifest: every action id with its scope and kind (the keymap presets may bind only these).</summary>
    public static class ActionManifest
    {
        public static readonly IReadOnlyList<ActionInfo> All = Build();

        static readonly Dictionary<string, ActionInfo> byId = Index();

        public static bool TryGet(string id, out ActionInfo info) => byId.TryGetValue(id ?? "", out info);

        public static ActionInfo Get(string id) => TryGet(id, out var i) ? i : throw new KeyNotFoundException("unknown action " + id);

        public static bool Contains(string id) => id != null && byId.ContainsKey(id);

        static Dictionary<string, ActionInfo> Index()
        {
            var d = new Dictionary<string, ActionInfo>(StringComparer.Ordinal);
            foreach (var a in All)
            {
                if (d.ContainsKey(a.Id)) throw new InvalidOperationException("duplicate action id " + a.Id);
                d[a.Id] = a;
            }
            return d;
        }

        static List<ActionInfo> Build()
        {
            const ActionScope G = ActionScope.Global, V = ActionScope.Viewport, T = ActionScope.Tree;
            var l = new List<ActionInfo>();
            void A(string id, ActionScope s, ActionKind k = ActionKind.Trigger, bool repeat = false) => l.Add(new ActionInfo(id, s, k, false, repeat));

            A(ActionIds.Undo, G, repeat: true);
            A(ActionIds.Redo, G, repeat: true);
            A(ActionIds.Duplicate, G);
            A(ActionIds.Delete, G);
            A(ActionIds.SelectAll, G);
            A(ActionIds.Deselect, G);
            A(ActionIds.Rename, G);
            A(ActionIds.CopyTransform, G);
            A(ActionIds.PasteTransform, G);
            A(ActionIds.MoveToPivot, G);
            A(ActionIds.EditShow, G);

            A(ActionIds.ToolSelect, G);
            A(ActionIds.ToolMove, G);
            A(ActionIds.ToolRotate, G);
            A(ActionIds.ToolScale, G);
            A(ActionIds.ToolSpace, G);
            A(ActionIds.ToolPivot, G);
            A(ActionIds.ToolSnapToggle, G);

            A(ActionIds.ViewGizmos, G);
            A(ActionIds.ViewPlanes, G);
            A(ActionIds.ViewCentre, G);
            A(ActionIds.ViewPivotMarker, G);
            A(ActionIds.ViewGimmickIcons, G);
            A(ActionIds.ViewLightHelpers, G);
            A(ActionIds.ViewCameraIcons, G);
            A(ActionIds.ViewKinematicHandles, G);
            A(ActionIds.ViewPanels, G);
            A(ActionIds.ViewPhysics, G);
            A(ActionIds.ViewHideSelected, G);
            A(ActionIds.ViewShowAll, G);
            A(ActionIds.ViewIsolate, G);
            A(ActionIds.ViewFps, G);
            A(ActionIds.ViewRoutes, G);

            A(ActionIds.CameraFrame, V);
            A(ActionIds.CameraReset, V);
            A(ActionIds.CameraResetAngles, V);
            A(ActionIds.CameraRollReset, V);
            A(ActionIds.CameraFovReset, V);
            foreach (var id in new[]
            {
                ActionIds.CameraMoveLeft, ActionIds.CameraMoveRight, ActionIds.CameraMoveForward, ActionIds.CameraMoveBack,
                ActionIds.CameraMoveUp, ActionIds.CameraMoveDown, ActionIds.CameraDollyIn, ActionIds.CameraDollyOut,
                ActionIds.CameraOrbitUp, ActionIds.CameraOrbitDown, ActionIds.CameraOrbitLeft, ActionIds.CameraOrbitRight,
                ActionIds.CameraRollPlus, ActionIds.CameraRollMinus, ActionIds.CameraFovDown, ActionIds.CameraFovUp,
            })
                A(id, V, ActionKind.Held);
            for (int i = 0; i < 10; i++) A(ActionIds.CameraSlotLoad(i), V);
            for (int i = 0; i < 10; i++) A(ActionIds.CameraSlotSave(i), V);
            A(ActionIds.CameraNextObject, V);
            A(ActionIds.CameraRelease, G);
            A(ActionIds.CameraLookThrough, G);

            A(ActionIds.FileNew, G);
            A(ActionIds.FileOpen, G);
            A(ActionIds.FileSave, G);
            A(ActionIds.FileSaveAs, G);
            A(ActionIds.FileImport, G);
            A(ActionIds.FileOptions, G);
            A(ActionIds.FileSetupGuide, G);
            A(ActionIds.AppQuit, G);

            A(ActionIds.AddFemale, G);
            A(ActionIds.AddMale, G);
            A(ActionIds.AddItem, G);
            A(ActionIds.AddLight, G);
            A(ActionIds.AddCamera, G);
            A(ActionIds.AddCameraFromView, G);
            A(ActionIds.AddFolder, G);
            A(ActionIds.AddRoute, G);

            A(ActionIds.TimePause, G);
            A(ActionIds.TimeStep, G, repeat: true);
            A(ActionIds.CaptureScreenshot, G);
            A(ActionIds.CaptureDialog, G);
            foreach (var id in new[]
            {
                ActionIds.RoutePlay, ActionIds.RouteStop, ActionIds.RouteAddPoint, ActionIds.RouteDeletePoint, ActionIds.RouteResetAid,
                ActionIds.RoutePlayAll, ActionIds.RouteRestartAll, ActionIds.RouteStopAll,
            })
                A(id, G);

            A(ActionIds.HelpKeys, G);
            A(ActionIds.HelpAbout, G);
            A(ActionIds.UiLanguage, G);
            A(ActionIds.UiResetLayout, G);

            A(ActionIds.TreePrev, T, repeat: true);
            A(ActionIds.TreeNext, T, repeat: true);
            A(ActionIds.TreeCollapse, T);
            A(ActionIds.TreeExpand, T);

            A(ActionIds.GizmoSnapHold, V, ActionKind.Modifier);
            A(ActionIds.GizmoPrecisionHold, V, ActionKind.Modifier);
            A(ActionIds.GizmoResize, V, ActionKind.Modifier);
            foreach (var id in new[] { ActionIds.ClickParent, ActionIds.ClickParentAll, ActionIds.ClickDetach, ActionIds.ClickDetachAll, ActionIds.ClickCopyTransform })
                A(id, G, ActionKind.Modifier);
            return l;
        }
    }
}
