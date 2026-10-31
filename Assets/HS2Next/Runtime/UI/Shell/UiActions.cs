// The shell's actions: behaviours for the manifest ids package A owns — edit (undo /
// redo, duplicate, delete, select all / none, transform copy / paste, move to pivot), tools, space, pivot, snap, view
// toggles, camera, language, layout, help / about — plus minimal file and add behaviours with explicit
// arguments (`ui.action file.saveAs {path|name}`, `add.female {path}`, …) that package C's flows replace with their
// browsers and prompts (a later Register of the same id wins). Arguments: file.open / file.import {path},
// file.saveAs {path} or {name} (in <UserData>/studio/scene), add.female / add.male {path} (card or .hs2nchara),
// add.item / add.light {ref "guid#id"}, ui.language {language}.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using UnityEngine;

namespace HS2Next.UI
{
    public static class UiActions
    {
        /// <summary>The app-memory transform clipboard (edit.copyTransform / pasteTransform; not the OS clipboard).</summary>
        public static (Vector3 pos, Vector3 rot, Vector3 scale)? TransformClipboard;

        public static void Register(UiContext ctx)
        {
            var a = ctx.Registry.Actions;
            var bus = ctx.Session.Commands;
            CameraController Cam() => ctx.Viewport?.Camera;
            List<string> Sel() => new List<string>(ctx.Selection.Ids);
            bool HasSel() => ctx.Selection.Count > 0;
            object Show(OpResult r) { ctx.ShowResult(r); return r?.Ok ?? false; }

            // ---------------- edit
            a.Register(ActionIds.Undo, _ => Show(OpResult.From(bus.Undo())), () => bus.CanUndo);
            a.Register(ActionIds.Redo, _ => Show(OpResult.From(bus.Redo())), () => bus.CanRedo);
            a.Register(ActionIds.Duplicate, _ =>
            {
                var r = ctx.Ops.Duplicate(Sel(), UiContext.User());
                if (r.Ok && r.NewIds.Count > 0) ctx.Ops.Select(r.NewIds);
                return Show(r);
            }, HasSel);
            a.Register(ActionIds.Delete, _ =>
            {
                var ids = Sel();
                if (ctx.Options.Bool(OptionDefs.ConfirmDelete))
                {
                    ctx.Dialogs.Confirm("dialog.delete.title", "dialog.delete.message", ok => { if (ok) ctx.ShowResult(ctx.Ops.Delete(ids, UiContext.User())); }, ids.Count);
                    return null;
                }
                return Show(ctx.Ops.Delete(ids, UiContext.User()));
            }, HasSel);
            a.Register(ActionIds.SelectAll, _ => Show(ctx.Ops.Select(ctx.Model.PreOrder().Select(n => n.Id))));
            a.Register(ActionIds.Deselect, _ => Show(ctx.Ops.Select(Array.Empty<string>())));
            a.Register(ActionIds.Rename, _ =>
            {
                var p = ctx.Model.Find(ctx.Selection.Primary);
                if (p == null) return null;
                string id = p.Id;
                ctx.Dialogs.Prompt("dialog.rename.title", p.Label, name => { if (name != null) ctx.ShowResult(ctx.Ops.Rename(id, name.Length == 0 ? null : name, UiContext.User())); });
                return null;
            }, HasSel);
            a.Register(ActionIds.CopyTransform, _ =>
            {
                var n = ctx.Model.Find(ctx.Selection.Primary);
                if (n == null) return null;
                var t = n.Doc.Transform;
                TransformClipboard = (new Vector3(t.Pos.X, t.Pos.Y, t.Pos.Z), new Vector3(t.Rot.X, t.Rot.Y, t.Rot.Z), new Vector3(t.Scale.X, t.Scale.Y, t.Scale.Z));
                ctx.Toasts.Show(ctx.L10n.Get("msg.transformCopied"));
                return true;
            }, HasSel);
            a.Register(ActionIds.PasteTransform, _ =>
            {
                var c = TransformClipboard.Value;
                return Show(ctx.Ops.SetTransform(Sel(), c.pos, c.rot, c.scale, UiContext.User()));
            }, () => HasSel() && TransformClipboard != null);
            a.Register(ActionIds.MoveToPivot, _ => Show(ctx.Ops.MoveToPivot(Sel(), UiContext.User())), HasSel);

            // ---------------- tools
            void Tool(string id, GizmoTool t) { var d = a.Register(id, _ => { ctx.Tool = t; return t.ToString(); }); d.IsChecked = () => ctx.Tool == t; }
            Tool(ActionIds.ToolSelect, GizmoTool.Select);
            Tool(ActionIds.ToolMove, GizmoTool.Move);
            Tool(ActionIds.ToolRotate, GizmoTool.Rotate);
            Tool(ActionIds.ToolScale, GizmoTool.Scale);
            a.Register(ActionIds.ToolSpace, call =>
            {
                var s = call.Arg("space") as string;
                ctx.Space = s != null && Enum.TryParse(s, true, out GizmoSpace sp) ? sp : (GizmoSpace)(((int)ctx.Space + 1) % 4);
                return ctx.Space.ToString();
            });
            a.Register(ActionIds.ToolPivot, _ => { ctx.Pivot = ctx.Pivot == GizmoPivot.Individual ? GizmoPivot.SelectionCentre : GizmoPivot.Individual; return ctx.Pivot.ToString(); });
            var snap = a.Register(ActionIds.ToolSnapToggle, _ => { ctx.Snap = !ctx.Snap; return ctx.Snap; });
            snap.IsChecked = () => ctx.Snap;

            // ---------------- view toggles (options; view state)
            void Toggle(string id, string option)
            {
                var d = a.Register(id, call => call.Arg("on") is bool on ? SetOpt(ctx, option, on) : ctx.Options.Toggle(option));
                d.IsChecked = () => ctx.Options.Bool(option);
            }
            Toggle(ActionIds.ViewGizmos, OptionDefs.ViewGizmos);
            Toggle(ActionIds.ViewPlanes, OptionDefs.ViewPlanes);
            Toggle(ActionIds.ViewCentre, OptionDefs.ViewCentre);
            Toggle(ActionIds.ViewPivotMarker, OptionDefs.ViewPivotMarker);
            Toggle(ActionIds.ViewGimmickIcons, OptionDefs.ViewGimmickIcons);
            Toggle(ActionIds.ViewCameraIcons, OptionDefs.ViewCameraIcons);
            Toggle(ActionIds.ViewKinematicHandles, OptionDefs.ViewKinematicHandles);
            Toggle(ActionIds.ViewFps, OptionDefs.ViewFps);
            var lh = a.Register(ActionIds.ViewLightHelpers, call =>
            {
                string cur = ctx.Options.String(OptionDefs.ViewLightHelpers);
                string next = call.Arg("mode") as string ?? (cur == "selected" ? "all" : cur == "all" ? "none" : "selected");
                ctx.Options.Set(OptionDefs.ViewLightHelpers, next);
                return next;
            });
            lh.IsChecked = () => ctx.Options.String(OptionDefs.ViewLightHelpers) != "none";
            a.Register(ActionIds.ViewPanels, _ => { ctx.Shell?.TogglePanels(); return true; });
            a.Register(ActionIds.ViewHideSelected, _ => Show(ctx.Ops.SetVisible(Sel(), false, UiContext.User())), HasSel);
            a.Register(ActionIds.ViewShowAll, _ => Show(ctx.Ops.SetVisible(ctx.Model.PreOrder().Select(n => n.Id), true, UiContext.User())));
            a.Register(ActionIds.ViewIsolate, _ =>
            {
                // hide every node that is neither selected, nor an ancestor or descendant of a selected node
                var sel = new HashSet<string>(Sel());
                bool Related(TreeNode n)
                {
                    for (var p = n; p != null; p = p.Parent) if (sel.Contains(p.Id)) return true;
                    foreach (var id in sel) for (var p = ctx.Model.Find(id)?.Parent; p != null; p = p.Parent) if (p == n) return true;
                    return false;
                }
                return Show(ctx.Ops.SetVisible(ctx.Model.PreOrder().Where(n => !Related(n)).Select(n => n.Id).ToList(), false, UiContext.User()));
            }, HasSel);

            // ---------------- camera
            a.Register(ActionIds.CameraReset, _ => { Cam()?.Reset(); return true; });
            a.Register(ActionIds.CameraResetAngles, _ => { Cam()?.ResetAngles(); return true; });
            a.Register(ActionIds.CameraRollReset, _ => { Cam()?.ResetRoll(); return true; });
            a.Register(ActionIds.CameraFovReset, _ => { Cam()?.ResetFov(); return true; });
            a.Register(ActionIds.CameraFrame, _ =>
            {
                bool hs2 = ctx.Keys.PresetName == Keymap.Hs2StudioPreset;
                var ids = hs2 ? (ctx.Selection.Primary != null ? new List<string> { ctx.Selection.Primary } : new List<string>()) : Sel();
                return Cam()?.Frame(ids, fit: !hs2) ?? false;
            }, HasSel);
            void Held(string id, Action<CameraController, float, double> f) =>
                a.Register(id, call => { var c = Cam(); if (c != null) f(c, (float)call.DeltaTime, call.Speed); return null; });
            Held(ActionIds.CameraMoveLeft, (c, dt, s) => c.Move(Vector3.left, dt, s));
            Held(ActionIds.CameraMoveRight, (c, dt, s) => c.Move(Vector3.right, dt, s));
            Held(ActionIds.CameraMoveForward, (c, dt, s) => c.Move(Vector3.forward, dt, s));
            Held(ActionIds.CameraMoveBack, (c, dt, s) => c.Move(Vector3.back, dt, s));
            Held(ActionIds.CameraMoveUp, (c, dt, s) => c.Move(Vector3.up, dt, s));
            Held(ActionIds.CameraMoveDown, (c, dt, s) => c.Move(Vector3.down, dt, s));
            Held(ActionIds.CameraDollyIn, (c, dt, s) => c.Dolly(-1, dt, s));
            Held(ActionIds.CameraDollyOut, (c, dt, s) => c.Dolly(1, dt, s));
            Held(ActionIds.CameraOrbitLeft, (c, dt, s) => c.OrbitKey(-1, 0, dt, s));
            Held(ActionIds.CameraOrbitRight, (c, dt, s) => c.OrbitKey(1, 0, dt, s));
            Held(ActionIds.CameraOrbitUp, (c, dt, s) => c.OrbitKey(0, -1, dt, s));
            Held(ActionIds.CameraOrbitDown, (c, dt, s) => c.OrbitKey(0, 1, dt, s));
            Held(ActionIds.CameraRollPlus, (c, dt, s) => c.Roll(1, dt, s));
            Held(ActionIds.CameraRollMinus, (c, dt, s) => c.Roll(-1, dt, s));
            Held(ActionIds.CameraFovUp, (c, dt, s) => c.Fov(1, dt, s));
            Held(ActionIds.CameraFovDown, (c, dt, s) => c.Fov(-1, dt, s));
            for (int i = 0; i < 10; i++)
            {
                int slot = i;
                a.Register(ActionIds.CameraSlotLoad(i), _ => Show(ctx.Ops.LoadCameraSlot(slot, UiContext.User())));
                a.Register(ActionIds.CameraSlotSave(i), _ => Show(ctx.Ops.SaveCameraSlot(slot, UiContext.User())));
            }
            a.Register(ActionIds.CameraNextObject, _ =>
            {
                var cams = ctx.Model.PreOrder().Where(n => n.Kind == NodeKind.Camera).Select(n => n.Id).ToList();
                if (cams.Count == 0) return null;
                string cur = ctx.Runtime?.GetPart<SceneViewCamera>()?.ActiveCameraId;
                int k = cur == null ? 0 : (cams.IndexOf(cur) + 1) % (cams.Count + 1);
                return Show(ctx.Ops.SetActiveCamera(k < cams.Count ? cams[k] : null, UiContext.User()));
            });
            a.Register(ActionIds.CameraRelease, _ => Show(ctx.Ops.SetActiveCamera(null, UiContext.User())),
                () => ctx.Runtime?.GetPart<SceneViewCamera>()?.ActiveCameraId != null);

            // ---------------- files (minimal; package C replaces these with its flows)
            a.Register(ActionIds.FileNew, _ => { var r = ctx.Ops.NewScene(); if (r.Ok) ctx.CurrentFile = null; return Show(r); });
            a.Register(ActionIds.FileOpen, call =>
            {
                var p = call.Arg("path") as string;
                if (p == null) { ctx.Toasts.Show(ctx.L10n.Get("msg.noBrowser"), ToastKind.Warning); return null; }
                var r = ctx.Ops.LoadScene(p);
                if (r.Ok) ctx.CurrentFile = Path.GetFullPath(p);
                return Show(r);
            });
            a.Register(ActionIds.FileImport, call =>
            {
                var p = call.Arg("path") as string;
                return p == null ? null : Show(ctx.Ops.ImportScene(p, UiContext.User()));
            });
            a.Register(ActionIds.FileSaveAs, call => SaveAs(ctx, call.Arg("path") as string, call.Arg("name") as string));
            a.Register(ActionIds.FileSave, _ => ctx.CurrentFile != null ? SaveAs(ctx, ctx.CurrentFile, null) : ctx.Registry.Run(ActionIds.FileSaveAs, "file.save") == ActionRunStatus.Ran);
            a.Register(ActionIds.AppQuit, _ =>
            {
                if (bus.IsDirty)
                    ctx.Dialogs.Confirm("dialog.quit.title", "dialog.quit.message", ok => { if (ok) Quit(); });
                else Quit();
                return null;
            });

            // ---------------- add (minimal; package C's browsers pass the content)
            OpResult Placed(OpResult r) => r;
            a.Register(ActionIds.AddFemale, call => AddChara(ctx, call.Arg("path") as string));
            a.Register(ActionIds.AddMale, call => AddChara(ctx, call.Arg("path") as string));
            a.Register(ActionIds.AddItem, call => ContentId.TryParse(call.Arg("ref") as string ?? "", out var cid) ? Show(Placed(ctx.Ops.AddItem(cid, null, null, -1, UiContext.User()))) : NeedsArg(ctx, "ref"));
            a.Register(ActionIds.AddLight, call => ContentId.TryParse(call.Arg("ref") as string ?? "", out var cid) ? Show(ctx.Ops.AddLight(cid, null, null, -1, UiContext.User())) : NeedsArg(ctx, "ref"));
            a.Register(ActionIds.AddCamera, _ => Show(ctx.Ops.AddCamera(null, null, -1, UiContext.User())));
            a.Register(ActionIds.AddFolder, _ => Show(ctx.Ops.AddFolder(null, null, -1, UiContext.User())));
            a.Register(ActionIds.AddRoute, _ => null, () => false);

            // ---------------- ui, help
            a.Register(ActionIds.UiLanguage, call =>
            {
                string lang = call.Arg("language") as string;
                if (lang == null)
                {
                    var order = new[] { L10nTable.En, L10nTable.Ja, L10nTable.ZhHans };
                    lang = order[(Array.IndexOf(order, ctx.L10n.Language) + 1) % order.Length];
                }
                ctx.Host?.SetLanguage(lang, save: true);
                return ctx.L10n.Language;
            });
            a.Register(ActionIds.UiResetLayout, _ => { ctx.Shell?.ResetLayout(); return true; });
            a.Register(ActionIds.HelpKeys, _ => { ShowKeyList(ctx); return true; });
            a.Register(ActionIds.HelpAbout, _ =>
            {
                ctx.Dialogs.Show(new DialogSpec { Id = "about", Title = "HS2Next", Message = ctx.L10n.Format("dialog.about.message", Application.version, Application.unityVersion) });
                return true;
            });
            a.Register(ActionIds.FileOptions, _ => { ctx.Toasts.Show(ctx.L10n.Get("msg.noOptionsPanel"), ToastKind.Warning); return null; });
            a.Register(ActionIds.ViewPhysics, _ => null, () => false);
        }

        static bool SetOpt(UiContext ctx, string key, bool on) { ctx.Options.Set(key, on); return on; }

        static object NeedsArg(UiContext ctx, string arg)
        {
            ctx.Toasts?.Show(ctx.L10n.Format("msg.actionNeedsArg", arg), ToastKind.Warning);
            return null;
        }

        static object AddChara(UiContext ctx, string path)
        {
            if (path == null) return NeedsArg(ctx, "path");
            var add = new CardAddOptions { UseCardState = ctx.Options.Bool(OptionDefs.AddUseCardState) };
            var r = ctx.ShowResult(ctx.Ops.AddChara(path, null, null, -1, add, UiContext.User()));
            return r.Ok ? (object)(r.NewIds.Count > 0 ? r.NewIds[0] : null) : null;
        }

        /// <summary>Saves to a path (or a name in the scene folder; default a timestamped name), asking before an overwrite.</summary>
        public static object SaveAs(UiContext ctx, string path, string name)
        {
            if (path == null)
            {
                string dir = ctx.App.Paths.SceneDir;
                if (string.IsNullOrEmpty(name)) name = DateTime.Now.ToString("yyyy_MMdd_HHmm_ss_fff");
                if (!name.EndsWith(".hs2nscene", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".hs2nscene";
                path = Path.Combine(dir, name);
            }
            var r = ctx.Ops.SaveScene(path);
            if (r.Ok)
            {
                ctx.CurrentFile = Path.GetFullPath(path);
                ctx.Toasts?.Show(ctx.L10n.Format("msg.saved", Path.GetFileName(path)), ToastKind.Success);
            }
            ctx.ShowResult(r);
            return r.Ok ? ctx.CurrentFile : null;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>The key binding list (package C replaces help.keys with the searchable, editable overlay).</summary>
        static void ShowKeyList(UiContext ctx)
        {
            var list = new UnityEngine.UIElements.ScrollView { name = "keys-list" };
            list.style.maxHeight = 480;
            foreach (var d in ctx.Registry.Actions.All.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                string chords = ctx.Keys.ChordText(d.Id);
                if (chords.Length == 0) continue;
                var row = new UnityEngine.UIElements.Label(ctx.L10n.Get(d.LabelKey) + "    " + chords);
                list.Add(row);
            }
            var s = new DialogSpec { Id = "keys", Title = ctx.L10n.Get("keys.title"), Content = list };
            s.Buttons.Add(DialogButton.Of("close", "common.close", true, true));
            ctx.Dialogs.Show(s);
        }
    }
}
