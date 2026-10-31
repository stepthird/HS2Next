// FK writer types.
// Scene-independent: the scene layer (Runtime/Scene/Runtime/Fk) builds the rows, discovers the bones of one character
// and drives the stages; item FK (Phase 3.1) can reuse FkStage with its own table.
using UnityEngine;

namespace HS2Next.Motion.Fk
{
    /// <summary>The seven FK groups in Studio's `activeFK` order.</summary>
    public enum FkGroup { Hair = 0, Neck = 1, Breast = 2, Body = 3, RightHand = 4, LeftHand = 5, Skirt = 6 }

    /// <summary>Vanilla-parity switches of the FK writer.</summary>
    public static class FkOptions
    {
        /// <summary>Vanilla: FK values start at 0. Captures of missing values write (0, 0, 0) and bones of an applied group
        /// without a value are written as identity. Default false = capture the pose you see.</summary>
        public static bool MissingBonesZero;
        /// <summary>Vanilla: de-applying Hair / Body / Skirt sets the bones to identity, and a new character's non-applied
        /// groups are initialised to identity. Default false = restore the rest rotation captured at discovery (I2).</summary>
        public static bool ResetToIdentity;
        /// <summary>HS2_FKHeightAdjustUI's hips offset. False = vanilla (ignored).</summary>
        public static bool HipsOffset = true;
    }

    /// <summary>One FK row: a Studio Bone-list row (`Bone_*`) or a row of an FK node table.</summary>
    public struct FkRow
    {
        public int No;
        /// <summary>Transform name (column 1).</summary>
        public string Name;
        /// <summary>Group column 0–13.</summary>
        public int GroupColumn;
        public int Level;
        /// <summary>Sync partner no, -1 = none (node rows never have one).</summary>
        public int SyncNo;
        /// <summary>A node of an FK node table.</summary>
        public bool Additional;

        public override string ToString() => $"{No} {Name} (col {GroupColumn}, level {Level}{(SyncNo >= 0 ? ", sync " + SyncNo : "")}{(Additional ? ", node" : "")})";
    }

    /// <summary>Group helpers.</summary>
    public static class FkGroupInfo
    {
        public const int Count = 7;

        /// <summary>Group column → group (0–4 Body, 5 RightHand, 6 LeftHand, 7–9 Hair, 10 Neck, 11–12 Breast, 13 Skirt); -1 = none.</summary>
        public static int OfColumn(int column)
        {
            switch (column)
            {
                case 0: case 1: case 2: case 3: case 4: return (int)FkGroup.Body;
                case 5: return (int)FkGroup.RightHand;
                case 6: return (int)FkGroup.LeftHand;
                case 7: case 8: case 9: return (int)FkGroup.Hair;
                case 10: return (int)FkGroup.Neck;
                case 11: case 12: return (int)FkGroup.Breast;
                case 13: return (int)FkGroup.Skirt;
                default: return -1;
            }
        }

        /// <summary>Groups whose de-apply resets the bones (vanilla's scope): Hair, Body, Skirt.</summary>
        public static bool ResetsOnDeapply(FkGroup g) => g == FkGroup.Hair || g == FkGroup.Body || g == FkGroup.Skirt;

        /// <summary>The model's flag name of a group (`fk.groups.&lt;name&gt;`).</summary>
        public static readonly string[] MemberNames = { "hair", "neck", "breast", "body", "rightHand", "leftHand", "skirt" };
    }

    /// <summary>One FK bone of one character.</summary>
    public sealed class FkBone
    {
        /// <summary>The `fk.bones` key: the row's transform name (hair names normalised).</summary>
        public string Key;
        /// <summary>The partner row's transform name (its own key; a value stored there is used when the primary has none), or null.</summary>
        public string PartnerKey;
        public FkGroup Group;
        public bool Additional;
        public FkRow Row;
        public Transform Bone;
        /// <summary>Sync partner (receives the same local rotation), or null.</summary>
        public Transform Partner;
        /// <summary>Local rotations at discovery (de-apply target).</summary>
        public Quaternion Rest, PartnerRest = Quaternion.identity;
        /// <summary>Skirt usage; true for every other group.</summary>
        public bool Used = true;
        public bool HasValue;
        public Vector3 Value;
        /// <summary>Quaternion.Euler(Value), cached when the value changes.</summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>Sets the value and its cached rotation.</summary>
        public void Set(Vector3 v)
        {
            HasValue = true;
            Value = v;
            Rotation = Quaternion.Euler(v);
        }

        /// <summary>Drops the value.</summary>
        public void Clear()
        {
            HasValue = false;
            Value = Vector3.zero;
            Rotation = Quaternion.identity;
        }

        /// <summary>Writes the applied rotation (bone and partner).</summary>
        public void Write(Quaternion q)
        {
            Bone.localRotation = q;
            if (Partner) Partner.localRotation = q;
        }

        /// <summary>De-apply: back to the rest rotation, or identity with <see cref="FkOptions.ResetToIdentity"/>.</summary>
        public void Reset(bool identity)
        {
            if (!Bone) return;
            Bone.localRotation = identity ? Quaternion.identity : Rest;
            if (Partner) Partner.localRotation = identity ? Quaternion.identity : PartnerRest;
        }

        public override string ToString() => $"{Key} [{Group}{(Additional ? ", node" : "")}{(Partner ? ", partner " + Partner.name : "")}{(Used ? "" : ", unused")}]" + (HasValue ? " " + Value : "");
    }
}
