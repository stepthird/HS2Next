// StudioOps, tree and transform area: adds, delete, duplicate,
// parent, reorder, visibility, rename, copy transform; transform sets (obj.transform's validation), transform drags (one
// transaction per drag, Esc cancels), reset and move to the camera pivot. Adds delegate to SceneOps (the Unity part of an
// add) inside one BeginOrJoin transaction, so the "Use card state" step of AddChara is part of the same entry.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Content;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>Options of <see cref="StudioOps.AddChara"/>.</summary>
    public sealed class CardAddOptions
    {
        /// <summary>Re-apply the card's Status block (clothes states, accessories, face, look, liquid, …)
        /// to the new node in the same entry.</summary>
        public bool UseCardState;
    }

    /// <summary>A transform channel of <see cref="StudioOps.ResetTransform"/>.</summary>
    [Flags]
    public enum TransformChannel { Pos = 1, Rot = 2, Scale = 4, All = 7 }

    public sealed partial class StudioOps
    {
        public const string CharaFileExtension = ".hs2nchara";

        // ------------------------------------------------------------ adds

        /// <summary>
        /// Adds a character from an HS2 card (imported at once) or a .hs2nchara file (by extension) as one entry "Add character";
        /// with <see cref="CardAddOptions.UseCardState"/> the card's Status block is applied in the same entry. Value = the
        /// import's or load's findings (FindingList). NewIds = [the node].
        /// </summary>
        public OpResult AddChara(string path, string parentId = null, string point = null, int index = -1, CardAddOptions add = null, ExecuteOptions o = default)
        {
            o = Labelled(o, Labels.AddCharacter);
            var found = new FindingList();
            bool hs2n = path != null && path.EndsWith(CharaFileExtension, StringComparison.OrdinalIgnoreCase);
            if (add == null || !add.UseCardState || hs2n)
            {
                var res = hs2n ? Scene.AddCharaFromFile(path, parentId, point, index, o, found) : Scene.AddCharaFromCard(path, parentId, point, index, o, found);
                var r0 = OpResult.From(res, "path");
                r0.Value = found;
                if (add != null && add.UseCardState && hs2n) r0.Warnings.Add("useCardState: a character file has no card Status block; ignored");
                return r0;
            }
            var tx = Open(o.Label, o, out var fail);
            if (tx == null) return fail;
            OpResult r;
            using (tx)
            {
                var o2 = o;
                o2.Record = HistoryMode.Record;
                var res = Scene.AddCharaFromCard(path, parentId, point, index, o2, found);
                if (res.Status == CommandStatus.Error) return OpResult.From(res, "path");
                r = OpResult.From(res);
                r.Value = found;
                var cs = ApplyCardState(tx, res.NewId, path, r.Warnings);
                if (cs != null && cs.Status == CommandStatus.Error) return OpResult.From(cs, "useCardState");
                r = Commit(tx, r);
                if (r.NewIds.Count == 0 && res.NewIds.Count > 0) r.NewIds = res.NewIds;
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>An item of list row <paramref name="id"/> with the prefab defaults ("Add item").</summary>
        public OpResult AddItem(ContentId id, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default) =>
            OpResult.From(Scene.AddItem(id, parentId, point, index, Labelled(o, Labels.AddItem)));

        /// <summary>An item of list row <paramref name="row"/> (an item or an accessory offered as an item) with the prefab defaults
        /// ("Add item").</summary>
        public OpResult AddItem(HS2Next.Content.Studio.StudioItem row, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default) =>
            OpResult.From(Scene.AddItem(row, parentId, point, index, Labelled(o, Labels.AddItem)));

        /// <summary>A Studio light of Light row <paramref name="id"/> ("Add light").</summary>
        public OpResult AddLight(ContentId id, string parentId = null, string point = null, int index = -1, ExecuteOptions o = default) =>
            OpResult.From(Scene.AddLight(id, parentId, point, index, Labelled(o, Labels.AddLight)));

        public OpResult AddFolder(string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            var r = OpResult.From(Session.AddFolder(parentId, point, index, Labelled(o, Labels.AddFolder)));
            rt.ApplyNow();
            return r;
        }

        public OpResult AddCamera(string parentId = null, string point = null, int index = -1, ExecuteOptions o = default)
        {
            var r = OpResult.From(Session.AddCamera(parentId, point, index, Labelled(o, Labels.AddCamera)));
            rt.ApplyNow();
            return r;
        }

        // ------------------------------------------------------------ structure

        public OpResult Delete(IEnumerable<string> ids, ExecuteOptions o = default) => OpResult.From(Scene.Delete(ids, Labelled(o, Labels.Delete)));

        /// <summary>Copies the top-most subtrees after their originals; the copies become the selection (NewIds).</summary>
        public OpResult Duplicate(IEnumerable<string> ids, ExecuteOptions o = default) => OpResult.From(Scene.Duplicate(ids, Labelled(o, Labels.Duplicate)));

        /// <summary>Reparents (and positions) a node ("Parent"); <paramref name="keepWorld"/> keeps its world pose.</summary>
        public OpResult Move(string id, string parentId, string point, int index = -1, bool keepWorld = true, ExecuteOptions o = default)
        {
            o = Labelled(o, Labels.Parent);
            var r = keepWorld ? Scene.MoveKeepWorld(id, parentId, point, index, o) : Session.Move(id, parentId, point, index, null, o);
            rt.ApplyNow();
            return OpResult.From(r);
        }

        /// <summary>Moves a node to <paramref name="index"/> among its siblings ("Reorder").</summary>
        public OpResult Reorder(string id, int index, ExecuteOptions o = default)
        {
            var r = OpResult.From(Session.Reorder(id, index, Labelled(o, Labels.Reorder)), "index");
            rt.ApplyNow();
            return r;
        }

        /// <summary>The nodes' own visibility flags in one entry "Visibility" (obj.visible).</summary>
        public OpResult SetVisible(IEnumerable<string> ids, bool visible, ExecuteOptions o = default)
        {
            var list = new List<string>(ids ?? Array.Empty<string>());
            var tx = Open(Labels.Visibility, o, out var fail);
            if (tx == null) return fail;
            OpResult r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var id in list)
                {
                    var s = tx.Set(id, "visible", visible);
                    if (s.Status == CommandStatus.Error) return OpResult.From(s, "visible");
                    r.Record(id, "visible", s.Status);
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>The node's name ("Rename"); null or empty = the default name of its kind.</summary>
        public OpResult Rename(string id, string name, ExecuteOptions o = default)
        {
            var n = Find(id, "key", out var fail);
            if (n == null) return fail;
            var r = OpResult.From(Session.Set(n.Id, "name", string.IsNullOrEmpty(name) ? null : name, Labelled(o, Labels.Rename)), "name");
            rt.ApplyNow();
            return r;
        }

        /// <summary>Copies the stored transform of <paramref name="fromId"/> to every target ("Copy transform"); the scale only to
        /// nodes with a scale channel (characters, items).</summary>
        public OpResult CopyTransform(string fromId, IEnumerable<string> toIds, ExecuteOptions o = default)
        {
            var src = Find(fromId, "from", out var fail);
            if (src == null) return fail;
            var t = src.Doc.Transform ?? new NodeTransform();
            var tx = Open(Labels.CopyTransform, o, out fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var id in toIds ?? Array.Empty<string>())
                {
                    var n = Find(id, "keys", out fail);
                    if (n == null) return fail;
                    if (n == src) continue;
                    foreach (var (path, value) in new (string, object)[] { ("transform.pos", t.Pos), ("transform.rot", t.Rot), ("transform.scale", t.Scale) })
                    {
                        if (path == "transform.scale" && !HasScaleChannel(n)) continue;
                        var s = tx.Set(n.Id, path, value);
                        if (s.Status == CommandStatus.Error) return OpResult.From(s, "keys");
                        r.Record(n.Id, path, s.Status);
                    }
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>Characters and items have a scale channel; the other kinds store [1, 1, 1] only.</summary>
        public static bool HasScaleChannel(TreeNode n) => n.Kind == NodeKind.Chara || n.Kind == NodeKind.Item;

        // ------------------------------------------------------------ transform

        /// <summary>
        /// Sets position / rotation / scale (local, Euler degrees) of every node in one entry "Transform" (obj.transform): a
        /// node without a scale channel accepts only [1, 1, 1] (bad_args "scale"; nothing applied).
        /// </summary>
        public OpResult SetTransform(IEnumerable<string> ids, Vector3? pos, Vector3? rot, Vector3? scale, ExecuteOptions o = default)
        {
            var tx = Open(Labels.Transform, o, out var fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var id in ids ?? Array.Empty<string>())
                {
                    var n = Find(id, "key", out fail);
                    if (n == null) return fail;
                    var e = SetTransformSteps(tx, n, pos, rot, scale, r);
                    if (e != null) return e;
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        static OpResult SetTransformSteps(Transaction tx, TreeNode n, Vector3? pos, Vector3? rot, Vector3? scale, OpResult r)
        {
            if (pos != null)
            {
                var s = tx.Set(n.Id, "transform.pos", UnityObjects.V(pos.Value));
                if (s.Status == CommandStatus.Error) return OpResult.From(s, "pos");
                r.Record(n.Id, "transform.pos", s.Status);
            }
            if (rot != null)
            {
                var s = tx.Set(n.Id, "transform.rot", UnityObjects.V(rot.Value));
                if (s.Status == CommandStatus.Error) return OpResult.From(s, "rot");
                r.Record(n.Id, "transform.rot", s.Status);
            }
            if (scale != null)
            {
                if (!HasScaleChannel(n) && scale.Value != Vector3.one)
                    return OpResult.BadArgs("scale", "[1, 1, 1]", "a " + KindName(n) + " has no scale channel: only [1, 1, 1]");
                var s = tx.Set(n.Id, "transform.scale", UnityObjects.V(scale.Value));
                if (s.Status == CommandStatus.Error) return OpResult.From(s, "scale");
                r.Record(n.Id, "transform.scale", s.Status);
            }
            return null;
        }

        /// <summary>
        /// Opens one transaction for a drag (gizmo, numeric label drag): <see cref="TransformDrag.Set"/> previews, Commit records
        /// one entry "Transform", Cancel (Esc) or Dispose without Commit reverts. Null with <paramref name="fail"/> when busy.
        /// </summary>
        public TransformDrag BeginTransformDrag(IEnumerable<string> ids, out OpResult fail, ExecuteOptions o = default)
        {
            var tx = Open(Labels.Transform, o, out fail);
            if (tx == null) return null;
            return new TransformDrag(this, tx, ids);
        }

        /// <summary>A drag of <see cref="BeginTransformDrag"/>.</summary>
        public sealed class TransformDrag : IDisposable
        {
            readonly StudioOps ops;
            readonly Transaction tx;
            public IReadOnlyList<string> Ids { get; }
            public bool IsOpen => tx.IsOpen;

            internal TransformDrag(StudioOps ops, Transaction tx, IEnumerable<string> ids)
            {
                this.ops = ops;
                this.tx = tx;
                Ids = new List<string>(ids ?? Array.Empty<string>());
            }

            /// <summary>One drag step on one node (applies at once as a preview; the realization follows).</summary>
            public OpResult Set(string id, Vector3? pos, Vector3? rot = null, Vector3? scale = null)
            {
                var n = ops.Model.Find(id);
                if (n == null) return OpResult.NotFound("no node \"" + id + "\" in the scene", "key");
                var r = OpResult.NothingToDo();
                var e = SetTransformSteps(tx, n, pos, rot, scale, r);
                ops.rt.ApplyNow();
                return e ?? r.With(CommandResult.Success(null));
            }

            public OpResult Commit()
            {
                var r = StudioOps.Commit(tx, OpResult.NothingToDo());
                ops.rt.ApplyNow();
                return r;
            }

            public void Cancel()
            {
                tx.Cancel();
                ops.rt.ApplyNow();
            }

            public void Dispose()
            {
                if (tx.IsOpen) Cancel();
            }
        }

        /// <summary>Resets the channels of every node ("Reset transform"): pos 0, rot 0, scale 1 (scale only with a channel).</summary>
        public OpResult ResetTransform(IEnumerable<string> ids, TransformChannel channel = TransformChannel.All, ExecuteOptions o = default)
        {
            var tx = Open(Labels.ResetTransform, o, out var fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var id in ids ?? Array.Empty<string>())
                {
                    var n = Find(id, "keys", out fail);
                    if (n == null) return fail;
                    var e = SetTransformSteps(tx, n,
                        (channel & TransformChannel.Pos) != 0 ? Vector3.zero : (Vector3?)null,
                        (channel & TransformChannel.Rot) != 0 ? Vector3.zero : (Vector3?)null,
                        (channel & TransformChannel.Scale) != 0 && HasScaleChannel(n) ? Vector3.one : (Vector3?)null, r);
                    if (e != null) return e;
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }

        /// <summary>Moves every node's origin to the camera pivot ("Move to pivot"; the local position under its attach transform).</summary>
        public OpResult MoveToPivot(IEnumerable<string> ids, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var cur = Model.Settings.Camera.Current;
            Vector3 pivot = cur != null ? UnityObjects.V(cur.Pivot) : SceneViewCamera.DefaultPivot;
            var tx = Open(Labels.MoveToPivot, o, out var fail);
            if (tx == null) return fail;
            var r = OpResult.NothingToDo();
            using (tx)
            {
                foreach (var id in ids ?? Array.Empty<string>())
                {
                    var n = Find(id, "keys", out fail);
                    if (n == null) return fail;
                    var a = rt.AttachTransformFor(n.Parent?.Id, n.Point);
                    var e = SetTransformSteps(tx, n, a ? a.InverseTransformPoint(pivot) : pivot, null, null, r);
                    if (e != null) return e;
                }
                r = Commit(tx, r);
            }
            rt.ApplyNow();
            return r;
        }
    }
}
