// Read model of a built character's bust (Phase 4.2): the stored card
// values, the bust physics as the bust stage holds them (per chain: pattern, damping / elasticity / stiffness / inert of
// particle 0 — the stage writes every particle of pattern 0 alike — and the gravity) and the breast normals (source,
// entries, rate, SHA-256 of the drawn mesh's normals as float32 bits, whether a copy was made). Mirrors the HS2 side's
// chara.bustPhysics + chara.meshNormals. Nothing here writes the character. Unity-dependent; numbers only.
using System.Collections.Generic;
using HS2Next.Chara.Edit.Units;
using HS2Next.Motion.DynamicBones;
using UnityEngine;

namespace HS2Next.Chara.Edit
{
    public sealed class BustReport
    {
        /// <summary>"female" or "male".</summary>
        public string Sex;
        /// <summary>The stored document values: body.shape[1], body.bust.softness, body.bust.weight.</summary>
        public float Size, Softness, Weight;
        public PhysicsInfo Physics = new PhysicsInfo();
        public NormalsInfo Normals = new NormalsInfo();

        public sealed class PhysicsInfo
        {
            /// <summary>A bust stage exists and is enabled, and (males) its chains run (BustOptions.MaleBustPhysics).</summary>
            public bool Enabled;
            /// <summary>The stage's card values (what it applied: softness, weight, size).</summary>
            public float StageSoftness = float.NaN, StageWeight = float.NaN, StageSize = float.NaN;
            public readonly List<Chain> Chains = new List<Chain>();
        }

        public sealed class Chain
        {
            /// <summary>The component's Comment (Mune_L, Mune_R, Siri_L, Siri_R).</summary>
            public string Name;
            /// <summary>0 BreastL, 1 BreastR, 2 HipL, 3 HipR (−1 unknown).</summary>
            public int Kind;
            /// <summary>The current pattern (0 in Studio unless an H shake table changed it).</summary>
            public int Pattern;
            public bool Active;
            public int Particles;
            /// <summary>Particle 0's values (the stage writes damping / elasticity / stiffness of every particle of pattern 0
            /// alike).</summary>
            public float Damping, Elasticity, Stiffness, Inert;
            public Vector3 Gravity;
            /// <summary>The live values per particle (root first, the tip last; = pattern 0's when <see cref="Pattern"/> is 0).
            /// Inert is not uniform (the asset's per-particle values; the softness recalculation does not write it).</summary>
            public float[] Dampings, Elasticities, Stiffnesses, Inerts;
        }

        public sealed class NormalsInfo
        {
            /// <summary>"vanilla", "uncensor:&lt;guid&gt;" or null (male, or the unit did not run).</summary>
            public string Source;
            public string Asset, Bundle;
            public bool Missing;
            public string MissingReason;
            public readonly List<Entry> Entries = new List<Entry>();
            /// <summary>The rate of the last blend (NaN: none).</summary>
            public float Rate = float.NaN;
            /// <summary>Any entry applied (its copy drawn, a blend written).</summary>
            public bool Applied;
            /// <summary>An owned mesh copy exists.</summary>
            public bool MeshCopied;
            /// <summary>SHA-256 of the drawn body mesh's normals (o_body_cf / o_body_cm) as float32 bits.</summary>
            public string Sha256;
            /// <summary>The drawn body mesh (name / vertex count).</summary>
            public string Mesh;
            public int Blends, Copies;
            public double LastBlendMs = double.NaN, LastCopyMs = double.NaN;
            public long CopyBytes;
        }

        public sealed class Entry
        {
            public string Object;
            public int Vertices, Min, Max;
            public bool Applicable, Applied;
        }

        /// <summary>The report of a built character (null for a null context).</summary>
        public static BustReport Of(CharaContext ctx)
        {
            if (ctx == null) return null;
            var r = new BustReport { Sex = ctx.IsMale ? "male" : "female" };
            var body = ctx.Chara?.Body;
            var shape = body?.Shape;
            r.Size = shape != null && shape.Length > 1 ? shape[1] : float.NaN;
            r.Softness = body?.Bust != null ? body.Bust.Softness : float.NaN;
            r.Weight = body?.Bust != null ? body.Bust.Weight : float.NaN;

            var stage = ctx.Motion != null ? ctx.Motion.Get<BustStage>() : null;
            if (stage != null)
            {
                var p = r.Physics;
                p.Enabled = stage.Enabled && (!stage.IsMale || stage.Options.MaleBustPhysics);
                p.StageSoftness = stage.BustSoftness; p.StageWeight = stage.BustWeight; p.StageSize = stage.BustSize;
                foreach (var c in stage.All)
                {
                    if (c == null) continue;
                    var ch = new Chain { Name = c.Comment, Kind = c.Kind, Pattern = c.PtnNo, Active = c.Active, Particles = c.ParticleCount, Gravity = c.Gravity };
                    if (c.ParticleCount > 0) { ch.Damping = c.Damping(0); ch.Elasticity = c.Elasticity(0); ch.Stiffness = c.Stiffness(0); ch.Inert = c.Inert(0); }
                    int np = c.ParticleCount;
                    ch.Dampings = new float[np]; ch.Elasticities = new float[np]; ch.Stiffnesses = new float[np]; ch.Inerts = new float[np];
                    for (int i = 0; i < np; i++) { ch.Dampings[i] = c.Damping(i); ch.Elasticities[i] = c.Elasticity(i); ch.Stiffnesses[i] = c.Stiffness(i); ch.Inerts[i] = c.Inert(i); }
                    p.Chains.Add(ch);
                }
            }

            var n = r.Normals;
            var st = ctx.BustNormals;
            if (st != null)
            {
                n.Source = st.Source; n.Asset = st.Asset; n.Bundle = st.Bundle; n.Missing = st.Missing; n.MissingReason = st.MissingReason;
                n.Rate = st.Rate; n.Applied = st.AnyApplied; n.MeshCopied = st.MeshCopied;
                n.Blends = st.Blends; n.Copies = st.Copies; n.LastBlendMs = st.LastBlendMs; n.LastCopyMs = st.LastCopyMs; n.CopyBytes = st.CopyBytes;
                foreach (var e in st.Entries)
                    n.Entries.Add(new Entry { Object = e.Object, Vertices = e.Vertices, Min = e.MinCount, Max = e.MaxCount, Applicable = e.Applicable, Applied = e.Applied });
            }
            var drawn = BustNormalsUnit.MeshOf(ctx.BodyRenderer ? ctx.BodyRenderer : BodyRenderer(ctx));
            n.Mesh = drawn ? drawn.name + "/" + drawn.vertexCount : null;
            n.Sha256 = drawn ? FaceDigest.NormalsHash(drawn) : null;
            return r;
        }

        static Renderer BodyRenderer(CharaContext ctx)
        {
            if (!ctx.Body) return null;
            var t = EditContext.Find(ctx.Body.transform, ctx.IsMale ? "o_body_cm" : "o_body_cf");
            return t ? t.GetComponent<Renderer>() : null;
        }

        public override string ToString() =>
            $"{Sex} size {Size:0.####} softness {Softness:0.####} weight {Weight:0.####}; physics {(Physics.Enabled ? "on" : "off")} ({Physics.Chains.Count} chains); " +
            $"normals {Normals.Source ?? "-"} rate {Normals.Rate:0.####} applied {Normals.Applied} copied {Normals.MeshCopied} sha {(Normals.Sha256 ?? "-").Substring(0, System.Math.Min(12, (Normals.Sha256 ?? "-").Length))}";
    }
}
