// The operations layer shared by the UI and the bridge (decision U1). Every document change
// from the UI goes through these methods; the bridge handlers parse their arguments, call the same method and reply.
// Constructed with the SceneRuntime (and through it SceneSession, RuntimeServices, SceneOps, FkOps, IkOps,
// KinematicCopies); AppCore.Ops exposes the instance of the app's runtime (StudioOps.For). Every method takes
// ExecuteOptions (Source User from the UI, Bridge from handlers; Label overrides the default label of the op), opens its
// transaction with CommandBus.BeginOrJoin (a UI drag transaction or a bridge batch absorbs it) and
// returns an OpResult. Ranges are stored as given (the bridge never clamps; the UI clamps through the inspector schema).
// The class is partial per area: Tree / Transform (this file's siblings), Chara, Kinematics, Item, LightCamera, Scene,
// Files; Labels in StudioOps.Labels.cs (Unity-free).
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        /// <summary>The bridge code of a target that exists but is not realized (CommandKit.Unavailable).</summary>
        public const string UnavailableCode = "unavailable";

        static readonly ConditionalWeakTable<SceneRuntime, StudioOps> instances = new ConditionalWeakTable<SceneRuntime, StudioOps>();

        /// <summary>The one instance of <paramref name="runtime"/> (created on first use); null for a null runtime.</summary>
        public static StudioOps For(SceneRuntime runtime) => runtime == null ? null : instances.GetValue(runtime, r => new StudioOps(r));

        readonly SceneRuntime rt;

        public StudioOps(SceneRuntime runtime) { rt = runtime ?? throw new ArgumentNullException(nameof(runtime)); }

        public SceneRuntime Runtime => rt;
        public SceneSession Session => rt.Session;
        public RuntimeServices Services => rt.Services;
        public SceneModel Model => rt.Session.Model;
        /// <summary>The Unity-side operations of the runtime (adds, delete, duplicate, save, load).</summary>
        public SceneOps Scene => rt.Ops;
        public FkOps Fk => new FkOps(rt);
        public IkOps Ik => new IkOps(rt);
        public KinematicCopies Copies => new KinematicCopies(rt);

        // ------------------------------------------------------------ shared helpers

        /// <summary>The options with <paramref name="label"/> as the label when the caller gave none.</summary>
        static ExecuteOptions Labelled(ExecuteOptions o, string label)
        {
            if (string.IsNullOrEmpty(o.Label)) o.Label = label;
            return o;
        }

        /// <summary>
        /// BeginOrJoin with the caller's label or <paramref name="label"/>; null with <paramref name="fail"/> set (busy) when the
        /// bus cannot open one (CommandKit.Tx's rule).
        /// </summary>
        Transaction Open(string label, ExecuteOptions o, out OpResult fail)
        {
            o = Labelled(o, label);
            var tx = Session.Commands.BeginOrJoin(o.Label, o);
            if (!tx.IsOpen)
            {
                fail = OpResult.Fail(tx.Failure?.Code ?? CommandCodes.Busy, tx.Failure?.Message ?? "the bus is busy");
                return null;
            }
            fail = null;
            return tx;
        }

        /// <summary>Commits <paramref name="tx"/> into <paramref name="r"/> (an error replaces the result).</summary>
        static OpResult Commit(Transaction tx, OpResult r)
        {
            var c = tx.Commit();
            if (c.Status == CommandStatus.Error) return OpResult.From(c);
            return r.With(c);
        }

        /// <summary>The node <paramref name="id"/>, or not_found.</summary>
        TreeNode Find(string id, string arg, out OpResult fail)
        {
            var n = id != null ? Model.Find(id) : null;
            fail = n == null ? OpResult.NotFound("no node \"" + id + "\" in the scene", arg) : null;
            return n;
        }

        /// <summary>The kind string of a node as the bridge words it (chara, item, light, folder, camera, route, or the document's kind).</summary>
        public static string KindName(TreeNode n)
        {
            switch (n.Kind)
            {
                case NodeKind.Chara: return "chara";
                case NodeKind.Item: return "item";
                case NodeKind.Light: return "light";
                case NodeKind.Folder: return "folder";
                case NodeKind.Camera: return "camera";
                case NodeKind.Route: return "route";
                default: return n.Doc.Kind ?? "unknown";
            }
        }

        /// <summary>Nodes of one kind; wrong_kind naming <paramref name="arg"/> for any other ("node X is a Y, not a Z").</summary>
        List<TreeNode> OfKind(IEnumerable<string> ids, NodeKind kind, string kindText, string arg, out OpResult fail)
        {
            var list = new List<TreeNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            fail = null;
            foreach (var id in ids ?? Array.Empty<string>())
            {
                var n = Find(id, arg, out fail);
                if (n == null) return null;
                if (n.Kind != kind)
                {
                    fail = OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", not " + Article(kindText) + kindText, arg, kindText == "character" ? "chara" : kindText);
                    return null;
                }
                if (seen.Add(n.Id)) list.Add(n);
            }
            return list;
        }

        static string Article(string kindText) => kindText.Length > 0 && "aeiou".IndexOf(kindText[0]) >= 0 ? "an " : "a ";

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        /// <summary>
        /// Sets (path, value) pairs on every target (null = the scene) in one entry, recording each step's status per target.
        /// <paramref name="argOf"/> names the argument of a failed set (null: none).
        /// </summary>
        OpResult SetMany(IList<string> targets, IList<(string path, object value)> plan, string label, ExecuteOptions o, Func<string, string> argOf, bool distinct = true)
        {
            var r = OpResult.NothingToDo();
            rt.ApplyNow();
            var tx = Open(label, o, out var fail);
            if (tx == null) return fail;
            using (tx)
            {
                foreach (var t in targets)
                    foreach (var (path, value) in plan)
                    {
                        var s = tx.Set(t, path, value);
                        if (s.Status == CommandStatus.Error) return OpResult.From(s, argOf?.Invoke(path));
                        r.Record(t ?? "", path, s.Status, distinct);
                    }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }
    }
}
