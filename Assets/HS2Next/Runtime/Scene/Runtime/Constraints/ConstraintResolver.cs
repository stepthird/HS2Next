// What an end of a constraint names on the built scene. A node's end is its holder; a bone of a character goes by the advanced pose's
// addresses, a bone of an item by name, depth first from the instance, with #n for the n-th occurrence, never entering another node's
// holder; an IK target is the character's solve. An end that does not resolve says why, and the entry acts again when the thing returns.
using System;
using System.Globalization;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes.Constraints;
using HS2Next.Scenes.Overrides;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>How an end stands on the built scene.</summary>
    public enum ConstraintEndState
    {
        /// <summary>It resolves.</summary>
        Ok,
        /// <summary>The node is in the model and not built yet.</summary>
        Pending,
        /// <summary>The node, or the bone on it, is not there.</summary>
        Missing,
        /// <summary>The end asks what this kind of node cannot give.</summary>
        Bad,
        /// <summary>A target the mosaic rule names.</summary>
        Protected,
    }

    /// <summary>One end, resolved.</summary>
    public struct ConstraintResolved
    {
        public ConstraintEndState State;
        /// <summary>The node id, the address or the reason of a state that is not <see cref="ConstraintEndState.Ok"/>.</summary>
        public string Detail;
        public NodeRealization Node;
        /// <summary>The transform read or written: a holder, or a bone. Null for an IK target.</summary>
        public Transform Transform;
        /// <summary>A bone's or an IK target's character; else null.</summary>
        public CharaRealization Chara;
        /// <summary>The kind of what a target writes.</summary>
        public ConstraintTargetKind Kind;
        /// <summary>An IK target's solve; else null.</summary>
        public IkCharacter Ik;
        /// <summary>An IK target's index of <see cref="IkTargets.Names"/>; else -1.</summary>
        public int IkIndex;

        internal static ConstraintResolved Not(ConstraintEndState state, string detail) => new ConstraintResolved { State = state, Detail = detail, IkIndex = -1 };
    }

    public static class ConstraintResolver
    {
        /// <summary>The transform a source reads.</summary>
        public static ConstraintResolved Source(SceneRuntime rt, ConstraintEnd end)
        {
            var r = NodeOf(rt, end, out var failed);
            if (r == null) return failed;
            if (end.Bone == null) return new ConstraintResolved { Node = r, Transform = r.Holder, IkIndex = -1 };
            if (r is CharaRealization c)
            {
                var built = Built(c, end.Bone);
                if (built.State != ConstraintEndState.Ok) return built;
                var t = AdvancedPoseResolver.Resolve(c, end.Bone);
                return t ? new ConstraintResolved { Node = r, Chara = c, Transform = t, IkIndex = -1 } : ConstraintResolved.Not(ConstraintEndState.Missing, end.Bone);
            }
            if (r is ItemRealization it)
            {
                if (it.State == RealizationState.Pending) return ConstraintResolved.Not(ConstraintEndState.Pending, end.Bone);
                var t = it.State == RealizationState.Built ? ItemBone(it, end.Bone) : null;
                return t ? new ConstraintResolved { Node = r, Transform = t, IkIndex = -1 } : ConstraintResolved.Not(ConstraintEndState.Missing, end.Bone);
            }
            return ConstraintResolved.Not(ConstraintEndState.Bad, "a " + r.Kind.ToString().ToLowerInvariant() + " has no bones");
        }

        /// <summary>What a target writes, for <paramref name="parts"/>. A bone the mosaic rule names is <see cref="ConstraintEndState.Protected"/>.</summary>
        public static ConstraintResolved Target(SceneRuntime rt, ConstraintEnd end, ConstraintParts parts)
        {
            var r = NodeOf(rt, end, out var failed);
            if (r == null) return failed;
            if (end.Ik != null)
            {
                if (!(r is CharaRealization ic)) return ConstraintResolved.Not(ConstraintEndState.Bad, "a " + r.Kind.ToString().ToLowerInvariant() + " has no IK targets");
                var built = Built(ic, end.Ik);
                if (built.State != ConstraintEndState.Ok) return built;
                int index = Array.IndexOf(IkTargets.Names, end.Ik);
                var ik = IkDriver.Of(ic);
                if (index < 0 || ik == null) return ConstraintResolved.Not(ConstraintEndState.Missing, end.Ik);
                return new ConstraintResolved { Node = r, Chara = ic, Kind = ConstraintTargetKind.Ik, Ik = ik, IkIndex = index };
            }
            if (end.Bone != null)
            {
                if (!(r is CharaRealization c)) return ConstraintResolved.Not(ConstraintEndState.Bad, r.Kind == NodeKind.Item ? "a bone of an item cannot be a target" : "a " + r.Kind.ToString().ToLowerInvariant() + " has no bones");
                var built = Built(c, end.Bone);
                if (built.State != ConstraintEndState.Ok) return built;
                var t = AdvancedPoseResolver.Resolve(c, end.Bone);
                if (!t) return ConstraintResolved.Not(ConstraintEndState.Missing, end.Bone);
                if (AdvancedPoseGuard.Bone(c, t)) return ConstraintResolved.Not(ConstraintEndState.Protected, end.Bone);
                return new ConstraintResolved { Node = r, Chara = c, Kind = ConstraintTargetKind.Bone, Transform = t, IkIndex = -1 };
            }
            if ((parts & ConstraintParts.Scale) != 0 && !ScalePath.Accepts(r.Kind))
                return ConstraintResolved.Not(ConstraintEndState.Bad, "a " + r.Kind.ToString().ToLowerInvariant() + " has no scale");
            return new ConstraintResolved { Node = r, Kind = ConstraintTargetKind.Node, Transform = r.Holder, IkIndex = -1 };
        }

        static readonly OverridePath ScalePath = OverridePath.Parse("transform.scale");

        // the node of an end, in the model and with a holder; else null with how the end stands
        static NodeRealization NodeOf(SceneRuntime rt, ConstraintEnd end, out ConstraintResolved failed)
        {
            failed = default;
            var n = end?.Node != null ? rt.Model.Find(end.Node) : null;
            if (n == null)
            {
                failed = ConstraintResolved.Not(ConstraintEndState.Missing, end?.Node);
                return null;
            }
            var r = rt.Find(end.Node);
            if (r == null || r.Node != n)
            {
                failed = ConstraintResolved.Not(ConstraintEndState.Pending, end.Node);
                return null;
            }
            if (!r.HasHolder || r.InGraveyard)
            {
                failed = ConstraintResolved.Not(ConstraintEndState.Missing, end.Node);
                return null;
            }
            return r;
        }

        static ConstraintResolved Built(CharaRealization c, string name)
        {
            if (c.State == RealizationState.Pending) return ConstraintResolved.Not(ConstraintEndState.Pending, name);
            if (c.State != RealizationState.Built || !c.Motion || !c.Root) return ConstraintResolved.Not(ConstraintEndState.Missing, name);
            return default;
        }

        /// <summary>
        /// The transform of an item by name: depth first from the instance, never into a holder. <c>name#n</c> picks the n-th occurrence
        /// (0 = the first); a transform whose name itself ends so is found when no such occurrence is.
        /// </summary>
        public static Transform ItemBone(ItemRealization it, string address)
        {
            if (it == null || !it.Instance || string.IsNullOrEmpty(address)) return null;
            string name = address;
            int n = 0;
            int mark = address.LastIndexOf('#');
            if (mark > 0 && int.TryParse(address.Substring(mark + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int at))
            {
                name = address.Substring(0, mark);
                n = at;
            }
            var t = Walk(it.Instance.transform, name, ref n);
            if (t || name.Length == address.Length) return t;
            int first = 0;
            return Walk(it.Instance.transform, address, ref first);
        }

        static Transform Walk(Transform t, string name, ref int n)
        {
            if (t.name == name && n-- == 0) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (SceneNodeHolder.IsHolder(c)) continue;
                var f = Walk(c, name, ref n);
                if (f) return f;
            }
            return null;
        }

        // ------------------------------------------------------------ what a row shows

        /// <summary>An end as text: the node's name, else its kind and id, then the bone or the IK target.</summary>
        public static string EndText(SceneRuntime rt, ConstraintEnd end)
        {
            if (end == null) return "?";
            var n = end.Node != null ? rt.Model.Find(end.Node) : null;
            string id = end.Node ?? "";
            string node = n != null && !string.IsNullOrEmpty(n.Doc?.Name) ? n.Doc.Name : id.Length > 8 ? id.Substring(0, 8) : id;
            return end.Bone != null ? node + " " + end.Bone : end.Ik != null ? node + " " + end.Ik : node;
        }

        /// <summary>A row of a list: its label, else "source → target".</summary>
        public static string RowText(SceneRuntime rt, ConstraintEntry e) =>
            e == null ? "?" : !string.IsNullOrEmpty(e.Label) ? e.Label : EndText(rt, e.Source) + " → " + EndText(rt, e.Target);
    }
}
