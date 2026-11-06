// The blend-shape overrides of a character (advanced pose), a stage right after the face stage: it exists only while the character
// has an override. The override wins over the face stage on every index, through pattern changes and cross-fades; the face stage is
// not told. Each step an index that holds something else than this stage's last write has a new base (the face stage or an edit wrote
// it), and the override is written when it differs. Taking an override off writes the base back once, when the index still holds this
// stage's value; a face index is rewritten by the face stage anyway.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.AdvancedPose
{
    /// <summary>One resolved weight: a renderer, the shape's index in its mesh and the value.</summary>
    public sealed class BlendOverride
    {
        public readonly SkinnedMeshRenderer Renderer;
        /// <summary>The mesh the index belongs to: another mesh on the renderer ends this entry's effect until it is resolved again.</summary>
        public readonly Mesh Mesh;
        public readonly int Index;
        /// <summary>The renderer's address and the shape's name.</summary>
        public readonly string Address, Shape;
        public float Weight;
        /// <summary>What the index holds without the override.</summary>
        public float Base { get; private set; }
        float written = float.NaN;

        public BlendOverride(SkinnedMeshRenderer renderer, Mesh mesh, int index, string address, string shape, float weight)
        {
            Renderer = renderer;
            Mesh = mesh;
            Index = index;
            Address = address;
            Shape = shape;
            Weight = weight;
        }

        bool Live => Renderer && Renderer.sharedMesh == Mesh;

        internal void Apply()
        {
            if (!Live) return;
            float cur = Renderer.GetBlendShapeWeight(Index);
            if (cur != written) Base = cur;
            if (cur != Weight) Renderer.SetBlendShapeWeight(Index, Weight);
            written = Weight;
        }

        internal void Restore()
        {
            if (!Live || float.IsNaN(written)) return;
            if (Renderer.GetBlendShapeWeight(Index) == written) Renderer.SetBlendShapeWeight(Index, Base);
        }
    }

    /// <summary>Stage <c>pose.blend</c>, phase 610: the overrides on what the face stage (600) wrote. The phase is a cast: the number
    /// lies between the enum's names.</summary>
    public sealed class BlendShapeOverrideStage : IMotionStage
    {
        readonly List<BlendOverride> entries = new List<BlendOverride>();

        public MotionPhase Phase => (MotionPhase)610;
        public string Name => "pose.blend";
        public bool Enabled { get; set; } = true;
        public IReadOnlyList<BlendOverride> Entries => entries;

        public void Step(in MotionFrame frame) => Apply();
        public void Reset() { }

        /// <summary>Writes every override now.</summary>
        public void Apply()
        {
            for (int i = 0; i < entries.Count; i++) entries[i].Apply();
        }

        /// <summary>Every index back to its base (a suspension; the next step writes the overrides again).</summary>
        public void Restore()
        {
            for (int i = 0; i < entries.Count; i++) entries[i].Restore();
        }

        /// <summary>The same index on the same mesh keeps its entry and its base; an entry that is gone gets its base back.</summary>
        public void Replace(List<BlendOverride> next)
        {
            var old = new Dictionary<(SkinnedMeshRenderer, int), BlendOverride>(entries.Count);
            foreach (var e in entries) old[(e.Renderer, e.Index)] = e;
            for (int i = 0; i < next.Count; i++)
            {
                var n = next[i];
                if (!old.TryGetValue((n.Renderer, n.Index), out var e) || e.Mesh != n.Mesh) continue;
                e.Weight = n.Weight;
                next[i] = e;
                old.Remove((n.Renderer, n.Index));
            }
            foreach (var e in entries)
                if (old.ContainsKey((e.Renderer, e.Index))) e.Restore();
            entries.Clear();
            entries.AddRange(next);
        }
    }
}
