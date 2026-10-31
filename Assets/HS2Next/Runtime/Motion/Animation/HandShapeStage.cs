// Hand patterns ("手の形"): 21 finger poses per hand read from
// the game's list/customshape.unity3d (`cf_anmShapeHand` key table, `cf_customhand` category list, one asset for
// both sexes), written every step as local Euler rotations on the 15 finger bones of each enabled hand.
// MotionPhase.Shape: after the animation sample (stage A) and before IK, neck look, FK, eye look and the joint
// correction, so the pattern overrides the clip's fingers and FK hand groups can still override the pattern.
//
// Data: key table = int32 bone count; per bone a .NET string name, int32 key count, per key int32 index,
// 3 x float position, 3 x float rotation (Euler degrees), 3 x float scale. Category list rows: category id,
// bone name, nine use flags (tab separated); category 0 = left hand, 1 = right hand. Pattern count = key count
// of the table's first bone. Selection: pattern p != 0 uses key p - 1 (rotation only); p = 0 writes nothing.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Motion.Animation
{
    /// <summary>The parsed hand tables (per hand: bone names and, per key, their Euler angles).</summary>
    public sealed class HandShapeData
    {
        public const string Bundle = "list/customshape.unity3d", KeyAsset = "cf_anmShapeHand", ListAsset = "cf_customhand";

        /// <summary>Bone names per hand (0 = left = category 0, 1 = right = category 1), in list order.</summary>
        public readonly List<string>[] Bones = { new List<string>(), new List<string>() };
        /// <summary>Euler angles per hand, bone, key.</summary>
        public readonly List<Vector3[]>[] Euler = { new List<Vector3[]>(), new List<Vector3[]>() };
        /// <summary>Key count of the table's first bone (= selectable patterns; 21 in vanilla).</summary>
        public int PatternCount;
        public readonly List<string> Warnings = new List<string>();

        public static HandShapeData Parse(byte[] keyTable, string list)
        {
            var keys = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            int first = -1;
            using (var br = new BinaryReader(new MemoryStream(keyTable), Encoding.UTF8))
            {
                int n = br.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string name = br.ReadString();
                    int kc = br.ReadInt32();
                    var rot = new Vector3[kc];
                    for (int k = 0; k < kc; k++)
                    {
                        int idx = br.ReadInt32();
                        br.ReadSingle(); br.ReadSingle(); br.ReadSingle();
                        var r = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        br.ReadSingle(); br.ReadSingle(); br.ReadSingle();
                        if (idx >= 0 && idx < kc) rot[idx] = r;
                    }
                    if (first < 0) first = kc;
                    keys[name] = rot;
                }
            }
            var d = new HandShapeData { PatternCount = Math.Max(first, 0) };
            foreach (var raw in list.Split('\n'))
            {
                var c = raw.TrimEnd('\r').Split('\t');
                if (c.Length < 2 || !int.TryParse(c[0], out int cat) || cat < 0 || cat > 1) continue;
                if (!keys.TryGetValue(c[1], out var e)) { d.Warnings.Add("no keys for " + c[1]); continue; }
                d.Bones[cat].Add(c[1]);
                d.Euler[cat].Add(e);
            }
            return d;
        }

        /// <summary>The tables from the catalog (null when the assets are missing or do not parse).</summary>
        public static HandShapeData Load(CharaContext ctx, out string note)
        {
            note = "";
            try
            {
                var k = ctx.Catalog?.LoadAsset<TextAsset>(Bundle, KeyAsset);
                var l = ctx.Catalog?.LoadAsset<TextAsset>(Bundle, ListAsset);
                if (!k || !l) { note = $"{Bundle}/{KeyAsset},{ListAsset} not found"; return null; }
                return Parse(k.bytes, l.text);
            }
            catch (Exception e) { note = $"hand tables failed ({e.GetType().Name}: {e.Message})"; return null; }
        }
    }

    /// <summary>Hand-pattern writer (IMotionStage at MotionPhase.Shape). Pattern 0 = "animation" (no write).</summary>
    public sealed class HandShapeStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.Shape;
        public string Name => "hands";
        public bool Enabled { get; set; } = true;

        public readonly HandShapeData Data;
        readonly Transform[][] bones = new Transform[2][];
        readonly Quaternion[][] target = new Quaternion[2][];
        readonly int[] pattern = new int[2];
        readonly bool[] active = new bool[2];

        /// <summary>Number of finger bones bound per hand (15 in vanilla).</summary>
        public int BoundCount(int hand) { int k = 0; foreach (var t in bones[hand]) if (t) k++; return k; }
        /// <summary>Selectable patterns (1..PatternCount; 0 = none).</summary>
        public int PatternCount => Data.PatternCount;
        public IReadOnlyList<Transform> Bones(int hand) => bones[hand];

        public HandShapeStage(HandShapeData data, Func<string, Transform> resolve)
        {
            Data = data;
            for (int h = 0; h < 2; h++)
            {
                var names = data.Bones[h];
                bones[h] = new Transform[names.Count];
                target[h] = new Quaternion[names.Count];
                for (int i = 0; i < names.Count; i++) bones[h][i] = resolve(names[i]);
                Select(h, 0);          // body construction: key 0 selected, hand disabled
                active[h] = false;
            }
        }

        /// <summary>Studio hand pattern per hand (0 = left, 1 = right), the card's `handPtn`.</summary>
        public int this[int hand]
        {
            get => pattern[hand];
            set => SetPattern(hand, value);
        }

        /// <summary>Studio ChangeHandAnime: p != 0 selects key p - 1 and enables the hand; 0 disables it. Out-of-range patterns are ignored.</summary>
        public void SetPattern(int hand, int p)
        {
            if (hand < 0 || hand > 1 || p < 0 || p > Data.PatternCount) return;
            if (p != 0) Select(hand, p - 1);
            pattern[hand] = p;
            active[hand] = p != 0;
        }

        void Select(int hand, int key)
        {
            var e = Data.Euler[hand];
            for (int i = 0; i < e.Count; i++)
                if (key < e[i].Length) target[hand][i] = Quaternion.Euler(e[i][key]);
        }

        /// <summary>The rotation pattern p writes on finger i of a hand (for tests / UI previews).</summary>
        public Quaternion PatternRotation(int hand, int finger, int p) =>
            p >= 1 && p <= Data.PatternCount && finger < Data.Euler[hand].Count ? Quaternion.Euler(Data.Euler[hand][finger][p - 1]) : Quaternion.identity;

        public void Step(in MotionFrame frame) => Apply();

        /// <summary>Writes the enabled hands now (pose-driven, independent of dt).</summary>
        public void Apply()
        {
            for (int h = 0; h < 2; h++)
            {
                if (!active[h]) continue;
                var b = bones[h]; var q = target[h];
                for (int i = 0; i < b.Length; i++) if (b[i]) b[i].localRotation = q[i];
            }
        }

        public void Reset() { }
    }
}
