// StudioOps, camera objects as items (v0.3 trial 2): the scene's camera nodes in tree order; a camera added at the current
// view in one entry "Add camera" (the node, its position and rotation = the view camera's world pose in the parent's space,
// its field of view = the view's); a camera brought to the current view in one entry "Camera" (position, rotation, field of
// view). "The current view" is what the viewport shows: the free camera, or the camera node it looks through. Camera nodes
// keep the members they had (transform, `camera.active`, `camera.fov`); the 10 camera slots are not touched.
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        /// <summary>The camera nodes of the scene in tree order.</summary>
        public List<TreeNode> AllCameras()
        {
            var l = new List<TreeNode>();
            foreach (var n in Model.PreOrder()) if (n.Kind == NodeKind.Camera) l.Add(n);
            return l;
        }

        /// <summary>The view camera's world pose and field of view (false without a view camera).</summary>
        public bool CurrentView(out Vector3 pos, out Quaternion rot, out float fov)
        {
            rt.ApplyNow();
            rt.LateApply();
            var cam = rt.GetPart<SceneViewCamera>()?.Camera;
            pos = cam ? cam.transform.position : Vector3.zero;
            rot = cam ? cam.transform.rotation : Quaternion.identity;
            fov = cam ? cam.fieldOfView : SceneViewCamera.DefaultFov;
            return cam;
        }

        /// <summary>The world pose in the local space under <paramref name="parent"/> (the nodes' root when null).</summary>
        static void ToLocal(Transform parent, ref Vector3 pos, ref Quaternion rot)
        {
            if (!parent) return;
            pos = parent.InverseTransformPoint(pos);
            rot = Quaternion.Inverse(parent.rotation) * rot;
        }

        /// <summary>
        /// A camera node at the current view, one entry "Add camera": position and rotation of the view camera, the view's field
        /// of view as the node's `camera.fov`; at the scene root (or under <paramref name="parentId"/>). With
        /// <paramref name="lookThrough"/> it also becomes the active camera in the same entry. NewIds = the node.
        /// </summary>
        public OpResult AddCameraFromView(string parentId = null, bool lookThrough = false, ExecuteOptions o = default)
        {
            if (!CurrentView(out var pos, out var rot, out float fov)) return OpResult.Unavailable("no view camera", "view", "NoView");
            if (parentId != null && Find(parentId, "parent", out var pf) == null) return pf;
            var tx = Open(Labels.AddCamera, o, out var fail);
            if (tx == null) return fail;
            OpResult r;
            using (tx)
            {
                var add = Session.AddCamera(parentId, null, -1, Labelled(o, Labels.AddCamera));
                if (add.Status == CommandStatus.Error || add.NewIds == null || add.NewIds.Count == 0) return OpResult.From(add, "parent");
                string id = add.NewIds[0];
                r = OpResult.From(add);
                ToLocal(parentId != null ? rt.AttachTransformFor(parentId, null) : rt.NodesRoot, ref pos, ref rot);
                var e = SetTransformSteps(tx, Model.Find(id), pos, rot.eulerAngles, null, r);
                if (e != null) return e;
                var s = tx.Set(id, "camera.fov", fov);
                if (s.Status == CommandStatus.Error) return OpResult.From(s, "fov");
                if (lookThrough)
                {
                    var a = tx.Execute(Session.ActiveCameraCommand(id));
                    if (a.Status == CommandStatus.Error) return OpResult.From(a, "active");
                }
                r = Commit(tx, r);
                r.NewIds = new[] { id };
            }
            rt.ApplyNow();
            rt.LateApply();
            return r;
        }

        /// <summary>
        /// Brings a camera node to the current view, one entry "Camera": its position and rotation (in its parent's space) and its
        /// `camera.fov` = the view's. The camera the view looks through is already there (no-op).
        /// </summary>
        public OpResult UpdateCameraFromView(string id, ExecuteOptions o = default)
        {
            var nodes = OfKind(new[] { id }, NodeKind.Camera, "camera", "key", out var fail);
            if (nodes == null) return fail;
            if (!CurrentView(out var pos, out var rot, out float fov)) return OpResult.Unavailable("no view camera", "view", "NoView");
            var holder = rt.Find(id)?.Holder;
            ToLocal(holder ? holder.parent : rt.NodesRoot, ref pos, ref rot);
            var tx = Open(Labels.Camera, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                var e = SetTransformSteps(tx, nodes[0], pos, rot.eulerAngles, null, r);
                if (e != null) return e;
                var s = tx.Set(id, "camera.fov", fov);
                if (s.Status == CommandStatus.Error) return OpResult.From(s, "fov");
                r.Record(id, "camera.fov", s.Status);
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            rt.LateApply();
            return r;
        }
    }
}
