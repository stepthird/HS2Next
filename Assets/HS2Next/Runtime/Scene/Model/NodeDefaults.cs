// State of new nodes: vanilla constructor values unless stated. Envelope:
// no name except folders ("Folder"), cameras ("Camera <n>", n = per-session counter, not stored) and routes
// ("Route", expanded); transform identity at the placement the session computes; visible. Kind sections: the
// absent values (a character's correction from the session options; a route's start
// point at the origin with a new id). Default names are English placeholders; the UI localises at creation.
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class NodeDefaults
    {
        public const string FolderName = "Folder";
        public const string RouteName = "Route";
        public const string CameraNamePrefix = "Camera ";

        /// <summary>A node with an identity transform at <paramref name="pos"/>.</summary>
        public static SceneNode Node(string id, NodeState state, Vec3 pos = default)
        {
            var n = new SceneNode(id, state);
            n.Transform.Pos = pos;
            n.Ui.IsChara = state is CharaNodeState;
            return n;
        }

        public static SceneNode Folder(string id, Vec3 pos = default)
        {
            var n = Node(id, new FolderNodeState(), pos);
            n.Name = FolderName;
            return n;
        }

        /// <param name="number">The session's camera counter.</param>
        public static SceneNode Camera(string id, int number, Vec3 pos = default)
        {
            var n = Node(id, new CameraNodeState(), pos);
            n.Name = CameraNamePrefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return n;
        }

        public static SceneNode Route(string id, string startPointId, Vec3 pos = default)
        {
            var r = new RouteNodeState();
            r.Points.Add(new RoutePoint { Id = startPointId });
            var n = Node(id, r, pos);
            n.Name = RouteName;
            n.Ui.Expanded = true;
            return n;
        }

        public static SceneNode Light(string id, string lightRef, Vec3 pos = default) => Node(id, new LightNodeState { Ref = lightRef }, pos);

        public static SceneNode Item(string id, ItemNodeState state, Vec3 pos = default) => Node(id, state, pos);

        /// <param name="correction">Eight joint-correction flags (SessionOptions.CorrectionDefault); null = all off.</param>
        public static SceneNode Chara(string id, bool[] correction, Vec3 pos = default)
        {
            var c = new CharaNodeState();
            if (correction != null && correction.Length == 8) c.Correction = (bool[])correction.Clone();
            return Node(id, c, pos);
        }
    }
}
