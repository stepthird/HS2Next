// Folder and camera realizations. A folder is its holder. A camera node is its holder:
// `active` and `fov` act through the view camera (Package C's SceneViewCamera follows the active camera node's holder).
// The route realization is Routes/RouteRealization.cs (Phase 3.6).
// Unknown kinds have no realization of their own here (NotBuiltRealization, NodeRealization.cs).
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class FolderRealization : NodeRealization
    {
        internal FolderRealization(SceneRuntime runtime, TreeNode node) : base(runtime, node) { }
        protected internal override void BuildContent() => State = RealizationState.Built;
        protected internal override void ApplyVisibility(bool visible) { }
    }

    public sealed class CameraRealization : NodeRealization
    {
        internal CameraRealization(SceneRuntime runtime, TreeNode node) : base(runtime, node) { }
        public bool Active => Node.State is CameraNodeState c && c.Active;
        public float? Fov => (Node.State as CameraNodeState)?.Fov;
        protected internal override void BuildContent() => State = RealizationState.Built;
        // The icon mesh is Phase 3.5.
        protected internal override void ApplyVisibility(bool visible) { }
    }
}
