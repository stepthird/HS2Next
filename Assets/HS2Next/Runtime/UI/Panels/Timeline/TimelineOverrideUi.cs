// The runtime override layer in the Studio UI: the «overridden» mark on inspector rows (node rows, the FK bone rows, the IK
// target rows, the Advanced pose tab's bone rows, the scene panel's depth of field) whose channel has a winning slot — an icon
// left of the value whose tooltip names the owner and says that the field holds the value that is saved (the row stays editable:
// an edit changes the model) — refreshed when the layer's version changes and when the inspector shows other rows; the refusal
// of a gizmo drag on a channel the Timeline drives while it plays or a constraint drives (a toast); the owner's display name
// ("Timeline" and "Constraints" localised, a plugin's name).
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public static class TimelineOverrideUi
    {
        public const string MarkName = "override-mark", MarkClass = "hs-field__override";

        /// <summary>The owner as shown: the Timeline in the UI language, a plugin by its name.</summary>
        public static string OwnerText(OverrideLayer layer, string owner) =>
            owner == OverrideLayer.TimelineOwner ? L10n.Get("panel.timeline") : owner == OverrideLayer.ConstraintsOwner ? L10n.Get("panel.constraints") : layer?.OwnerName(owner) ?? owner ?? "";

        // ------------------------------------------------------------ gizmo drags

        /// <summary>
        /// True (and a toast) when a gizmo drag must not start: the FK bone (<paramref name="fkNode"/>, <paramref name="fkBone"/>)
        /// or the transform channel of the handle (<paramref name="handle"/>) of one of <paramref name="nodes"/> is driven by the
        /// Timeline while it plays or by a constraint, or the channel of the IK target (<paramref name="ikNode"/>,
        /// <paramref name="ikTarget"/>) by a constraint. Channel by channel: a constraint on position leaves rotation free. A plugin's
        /// slot never refuses (the drag changes the model; the view keeps the slot).
        /// </summary>
        public static bool RefusesDrag(UiContext ctx, string fkNode, string fkBone, IReadOnlyList<string> nodes, Gizmos.HandleKind handle, string ikNode = null, string ikTarget = null)
        {
            var layer = ctx?.Runtime?.Overrides;
            if (layer == null || !layer.Any) return false;
            string by = null;
            string member = MemberOf(handle);
            if (fkNode != null && fkBone != null)
            {
                by = layer.DrivenBy(fkNode, "chara.fk.bones[" + FieldPath.Quote(fkBone) + "]");
                if (!Refuses(by))
                {
                    string bone = DrivenBonePath(ctx, fkNode, fkBone);
                    by = bone != null ? layer.DrivenBy(fkNode, bone) : null;
                }
            }
            else if (ikNode != null && ikTarget != null)
            {
                if (member != "scale" && member != null) by = layer.DrivenBy(ikNode, OverridePath.IkText(ikTarget, member));
            }
            else if (nodes != null && member != null)
                foreach (var id in nodes)
                {
                    by = layer.DrivenBy(id, "transform." + member);
                    if (Refuses(by)) break;
                }
            if (!Refuses(by)) return false;
            ctx.Toasts?.Show(L10n.Get(by == OverrideLayer.ConstraintsOwner ? "toast.constraintDriven" : "toast.timelinePlaying"), ToastKind.Warning);
            return true;
        }

        static bool Refuses(string owner) => owner == OverrideLayer.TimelineOwner || owner == OverrideLayer.ConstraintsOwner;

        static string MemberOf(Gizmos.HandleKind handle)
        {
            switch (handle)
            {
                case Gizmos.HandleKind.MoveAxis:
                case Gizmos.HandleKind.MovePlane:
                case Gizmos.HandleKind.MoveFree: return "pos";
                case Gizmos.HandleKind.RotateRing:
                case Gizmos.HandleKind.RotateView:
                case Gizmos.HandleKind.RotateTrackball: return "rot";
                case Gizmos.HandleKind.ScaleAxis:
                case Gizmos.HandleKind.ScaleUniform: return "scale";
                default: return null;
            }
        }

        /// <summary>The rotation channel of the driven bone behind the FK row <paramref name="fkKey"/>, or null when the bone is not driven.</summary>
        static string DrivenBonePath(UiContext ctx, string node, string fkKey)
        {
            var r = ctx?.Runtime?.FindChara(node);
            var set = r != null ? DrivenPoseDriver.Of(r)?.Set : null;
            if (set == null) return null;
            var bone = FkDriver.TableFor(r)?.Find(fkKey)?.Bone;
            if (!bone) return null;
            foreach (var e in set.Entries)
                if (e.Bone == bone) return OverridePath.BoneText(e.Address, "rot");
            return null;
        }

        // ------------------------------------------------------------ inspector marks

        static int lastVersion = int.MinValue;
        static float nextScan;
        static bool marked;
        static StyleSheet sheet;
        static readonly List<FieldRow> rows = new List<FieldRow>();
        static readonly List<IkTargetRow> ikRows = new List<IkTargetRow>();
        static readonly List<BoneChannelRow> boneRows = new List<BoneChannelRow>();

        /// <summary>Once per frame (the Timeline module's ticker): re-marks the rows when the layer's version changed, and every
        /// half second while slots exist or marks are shown (the inspector may have built other rows).</summary>
        public static void Tick(UiContext ctx)
        {
            var layer = ctx?.Runtime?.Overrides;
            int v = layer?.Version ?? 0;
            float now = Time.unscaledTime;
            bool any = layer != null && layer.Any;
            if (v == lastVersion && (now < nextScan || (!any && !marked))) return;
            lastVersion = v;
            nextScan = now + 0.5f;
            Scan(ctx, layer);
        }

        /// <summary>Marks every row of the inspector and the scene panel now; returns the rows marked.</summary>
        public static int Scan(UiContext ctx, OverrideLayer layer)
        {
            int n = 0;
            var sh = ctx?.Shell;
            if (sh == null) return 0;
            foreach (var id in new[] { "inspector", "scene" })
            {
                if (!sh.PanelRoots.TryGetValue(id, out var root) || root == null) continue;
                rows.Clear();
                root.Query<FieldRow>().ToList(rows);
                foreach (var r in rows) if (Mark(ctx, layer, r)) n++;
                if (id != "inspector") continue;
                ikRows.Clear();
                root.Query<IkTargetRow>().ToList(ikRows);
                foreach (var r in ikRows) if (Mark(layer, r)) n++;
                boneRows.Clear();
                root.Query<BoneChannelRow>().ToList(boneRows);
                foreach (var r in boneRows) if (Mark(layer, r)) n++;
            }
            rows.Clear();
            ikRows.Clear();
            boneRows.Clear();
            marked = n > 0;
            return n;
        }

        /// <summary>The override path of a row's model path (node rows: the path itself when it names a channel; scene rows: the
        /// depth-of-field block and the current view), or null.</summary>
        public static string ChannelOf(string rowPath, bool scene)
        {
            if (string.IsNullOrEmpty(rowPath)) return null;
            if (scene)
            {
                if (rowPath == "effects.depthOfField" || rowPath.StartsWith("effects.depthOfField.", System.StringComparison.Ordinal)) return "effects.depthOfField";
                if (rowPath == "camera.current" || rowPath.StartsWith("camera.current.", System.StringComparison.Ordinal)) return "camera";
                return null;
            }
            return OverridePath.TryParse(rowPath, out _, out _) ? rowPath : null;
        }

        static bool Mark(UiContext ctx, OverrideLayer layer, FieldRow r)
        {
            string owner = null;
            bool band = false;
            if (layer != null && layer.Any && r.Spec != null && r.Spec.Kind != FieldKind.Button)
            {
                bool scene = r.Spec.Target == FieldTarget.Scene;
                string path = ChannelOf(r.Path, scene);
                if (path != null)
                {
                    if (scene) owner = layer.Winner(null, path)?.Owner;
                    else
                        foreach (var t in r.Targets)
                        {
                            var w = layer.Winner(t, path);
                            if (w != null) { owner = w.Owner; break; }
                            // a constraint on the bone runs after its FK: the row keeps its value and shows who has the last word
                            if (OverridePath.TryParse(path, out var p, out _) && p.Channel == OverrideChannel.FkBone)
                            {
                                string bone = DrivenBonePath(ctx, t, p.Bone);
                                w = bone != null ? layer.Winner(t, bone) : null;
                                if (w != null) { owner = w.Owner; band = owner == OverrideLayer.ConstraintsOwner; break; }
                            }
                        }
                }
            }
            int i = r.Control != null && r.Control.parent == r ? r.IndexOf(r.Control) : r.childCount;
            return Place(layer, r, i, owner, band);
        }

        static bool Mark(OverrideLayer layer, IkTargetRow r)
        {
            string owner = null;
            if (layer != null && layer.Any)
                foreach (var t in r.Targets)
                {
                    var w = layer.Winner(t, r.Channel);
                    if (w != null) { owner = w.Owner; break; }
                }
            return Place(layer, r.Field, 0, owner, false);
        }

        static bool Mark(OverrideLayer layer, BoneChannelRow r)
        {
            string path = r.Channel;
            string owner = layer != null && layer.Any && path != null ? layer.Winner(r.Character, path)?.Owner : null;
            return Place(layer, r.Field, 0, owner, false);
        }

        // the mark of a row: at <paramref name="index"/> of <paramref name="host"/> while <paramref name="owner"/> holds the channel, else none
        static bool Place(OverrideLayer layer, VisualElement host, int index, string owner, bool band)
        {
            Label mark = null;
            foreach (var c in host.Children()) if (c.name == MarkName) { mark = c as Label; break; }
            if (owner == null)
            {
                mark?.RemoveFromHierarchy();
                return false;
            }
            if (mark == null)
            {
                mark = new Label("◆") { name = MarkName };
                mark.AddToClassList(MarkClass);
                sheet ??= Resources.Load<StyleSheet>(TimelinePanel.StyleResource);
                if (sheet != null) mark.styleSheets.Add(sheet);
                host.Insert(index, mark);
            }
            string tip = L10n.Format("ui.override.tip", OwnerText(layer, owner));
            if (band) tip += "\n" + L10n.Get("constraints.bandTip");
            if (mark.tooltip != tip) mark.tooltip = tip;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lastVersion = int.MinValue;
            nextScan = 0;
            marked = false;
        }
    }
}
