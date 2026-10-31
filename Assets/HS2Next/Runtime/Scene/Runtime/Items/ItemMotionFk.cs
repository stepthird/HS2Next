// Item FK (ItemFKCtrl): the bones of the row's bone list found in the instance
// (depth-first, the node itself first, exact name, first match: ItemDefaults.FindBone, the names of ItemFeatures.FkBones),
// their base rotation taken at the build before any step (the prefab pose), and while item.fk.enabled every step writes
// localRotation = Euler(item.fk.bones[name]) — or the base for a found bone without a stored entry (T12; HS2 writes
// identity). The stage runs at MotionPhase.Fk: after the item's animation, before its dynamic bones (HS2: LateUpdate order
// 0 after the Animator, before DynamicBone at 13100). Turning FK off writes the base rotation once (HS2's OnDisable).
// Values are read from the model when it changes (Load), not every step.
using System;
using System.Collections.Generic;
using HS2Next.Motion;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ItemFkStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.Fk;
        public string Name => "fk";
        public bool Enabled { get; set; } = true;

        /// <summary>Found bone names (row order, duplicates removed) and their transforms.</summary>
        public readonly string[] Names;
        public readonly Transform[] Bones;
        /// <summary>Local rotation of each bone at the build (before any step).</summary>
        public readonly Quaternion[] Base;
        readonly Quaternion[] target;
        readonly bool[] stored;
        readonly Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>item.fk.enabled as applied.</summary>
        public bool On { get; private set; }
        /// <summary>Steps that wrote the bones (diagnostics).</summary>
        public int Writes { get; private set; }

        public ItemFkStage(IList<string> names, IList<Transform> bones)
        {
            int n = Math.Min(names.Count, bones.Count);
            Names = new string[n];
            Bones = new Transform[n];
            Base = new Quaternion[n];
            target = new Quaternion[n];
            stored = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Names[i] = names[i];
                Bones[i] = bones[i];
                Base[i] = bones[i] ? bones[i].localRotation : Quaternion.identity;
                target[i] = Base[i];
                index[names[i]] = i;
            }
        }

        public int Count => Names.Length;
        public int IndexOf(string name) => name != null && index.TryGetValue(name, out int i) ? i : -1;

        /// <summary>The base rotation of a found bone as local Euler degrees (Unity's eulerAngles), or null.</summary>
        public Vector3? BaseEuler(string name)
        {
            int i = IndexOf(name);
            return i < 0 ? (Vector3?)null : Base[i].eulerAngles;
        }

        /// <summary>Whether the model has a value for bone i (else the base is applied).</summary>
        public bool HasValue(int i) => stored[i];

        /// <summary>The rotation FK applies to bone i while on.</summary>
        public Quaternion Target(int i) => target[i];

        /// <summary>
        /// Reads item.fk from the model. Switching off writes the base once; switching on (or new values while on) writes
        /// the values now, so the pose is right before the next step.
        /// </summary>
        public void Load(ItemFk fk)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                if (fk?.Bones != null && fk.Bones.TryGetValue(Names[i], out var v))
                {
                    target[i] = Quaternion.Euler(v.X, v.Y, v.Z);
                    stored[i] = true;
                }
                else
                {
                    target[i] = Base[i];
                    stored[i] = false;
                }
            }
            bool on = fk != null && fk.Enabled;
            if (On && !on) RestoreBase();
            On = on;
            if (On) Write();
        }

        /// <summary>Writes the FK rotations (only while on).</summary>
        public void Write()
        {
            if (!On) return;
            for (int i = 0; i < Bones.Length; i++)
                if (Bones[i]) Bones[i].localRotation = target[i];
            Writes++;
        }

        /// <summary>HS2's OnDisable: the base rotation of every bone once.</summary>
        public void RestoreBase()
        {
            for (int i = 0; i < Bones.Length; i++)
                if (Bones[i]) Bones[i].localRotation = Base[i];
        }

        public void Step(in MotionFrame frame) => Write();

        /// <summary>FK has no history.</summary>
        public void Reset() { }
    }
}
