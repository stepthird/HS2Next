// Breast normals of the female body (Phase 4.2; GB1, decision Y5). HS2 blends the
// body renderer's normals between the two sets of a `NormalData` asset by bust size; HS2Next does the same in the unit
// `bust.normals` (step 6, after shape.body; marked by every body.shape change and by the build):
//   source   the prefab's body mesh: p_cf_body_00_Nml of chara/oo_base.unity3d; a body mesh a build hook replaced: the
//            NormalData asset the hook recorded (CharaContext.BodyNormals, e.g. in the replacement's own bundle; a zipmod
//            named by GUID is looked up in the catalog here). No asset: nothing, no mesh copy, the finding
//            chara.bustNormalsMissing (info, reported once by the realization). A source of none (a plugin's body part
//            without normals): nothing, no mesh copy, no finding.
//   entries  each entry names a renderer under the body object (depth-first); it applies when NormalMin, NormalMax and
//            the drawn mesh have the same vertex count (else skipped, as HS2).
//   copy     at the first run (the build) the drawn mesh of every applicable entry is replaced by an owned copy with the
//            same name (CharaContext.OwnedMeshes, destroyed with the content; the renderer's local bounds are kept). The
//            copy is taken after the BodyMesh build hooks and after the plugin texture step, so it copies the mesh that is
//            drawn. If something replaced the drawn mesh later (MaterialEditor's RecalculateNormals), it is copied again.
//   blend    r = 1 for size ≥ 0.5, else InverseLerp(0, 0.5, size) (size = body.shape[1] as stored; the rate is clamped);
//            normals[i] = Vector3.Lerp(Min[i], Max[i], r) → Mesh.SetNormals; skipped while r is unchanged. Tangents stay.
//            NormalMax equals the shipped normals [V], so a female with size ≥ 0.5 keeps them.
// Males: nothing (HS2). The data of the asset is copied at load (the bundle may be unloaded later).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Chara.Edit
{
    /// <summary>The breast-normal state of one character (CharaContext.BustNormals; bridge chara.edit.bust, the digest).</summary>
    public sealed class BustNormalsState
    {
        public const string VanillaBundle = "chara/oo_base.unity3d", DefaultAsset = "p_cf_body_00_Nml";

        /// <summary>"vanilla" or "&lt;hook id&gt;:&lt;id&gt;" (a body mesh a build hook replaced).</summary>
        public string Source;
        public string Bundle, Asset;
        /// <summary>True when the NormalData asset was not found (no blend, no copy).</summary>
        public bool Missing;
        public string MissingReason;
        /// <summary>Set when the realization reported chara.bustNormalsMissing (once per character).</summary>
        public bool MissingReported;
        public readonly List<Entry> Entries = new List<Entry>();
        /// <summary>The rate of the last blend (NaN before the first one).</summary>
        public float Rate = float.NaN;
        /// <summary>Blends written (SetNormals calls) and copies made since the build.</summary>
        public int Blends, Copies;
        /// <summary>Milliseconds of the last blend write (all entries) and of the last mesh copy (UB4).</summary>
        public double LastBlendMs = double.NaN, LastCopyMs = double.NaN;
        /// <summary>Bytes of the owned copies (Profiler.GetRuntimeMemorySizeLong at the copy; UB4 / UB7).</summary>
        public long CopyBytes;

        public sealed class Entry
        {
            /// <summary>The entry's ObjectName.</summary>
            public string Object;
            /// <summary>The renderer of that name under the body object (null when missing).</summary>
            public Renderer Renderer;
            /// <summary>Vertex count of the drawn mesh when the entry was read (-1 without renderer / mesh).</summary>
            public int Vertices = -1;
            public Vector3[] Min, Max;
            /// <summary>Min, Max and the mesh have the same vertex count.</summary>
            public bool Applicable;
            /// <summary>The owned copy the normals are written into (applicable entries).</summary>
            public Mesh Copy;
            /// <summary>The rate last written into <see cref="Copy"/> (NaN = none).</summary>
            public float Rate = float.NaN;
            internal Vector3[] Buffer;
            public int MinCount => Min?.Length ?? -1;
            public int MaxCount => Max?.Length ?? -1;
            /// <summary>The copy is the renderer's drawn mesh and holds a blend.</summary>
            public bool Applied => Applicable && Copy && Units.BustNormalsUnit.MeshOf(Renderer) == Copy && !float.IsNaN(Rate);
        }

        /// <summary>True when any entry is applied.</summary>
        public bool AnyApplied { get { foreach (var e in Entries) if (e.Applied) return true; return false; } }
        /// <summary>True when an owned copy was made.</summary>
        public bool MeshCopied { get { foreach (var e in Entries) if (e.Copy) return true; return false; } }

        /// <summary>The owned copies were destroyed (CharaContext.ReleaseOwnedMeshes).</summary>
        internal void Released()
        {
            foreach (var e in Entries) { e.Copy = null; e.Rate = float.NaN; }
            Rate = float.NaN;
        }
    }
}

namespace HS2Next.Chara.Edit.Units
{
    public sealed class BustNormalsUnit : UnitBase
    {
        /// <summary>Run in preview flushes (drags) too (live when UB4 measured the
        /// blend ≤ 0.5 ms); false = deferred to the commit.</summary>
        public static bool LiveInDrags = true;
        /// <summary>Test switch: false disables the unit (the shipped normals stay; no copy). Opt-in command-line switch
        /// `-noBustNormals` (read when the type is first used after a domain load, i.e. once per Editor / play-mode domain and
        /// once in the player) sets it false; default true.</summary>
        public static bool Enabled = !CommandLineHas("-noBustNormals");

        static bool CommandLineHas(string flag)
        {
            try
            {
                foreach (var a in Environment.GetCommandLineArgs())
                    if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                    {
                        UnityEngine.Debug.Log("bust normals: " + flag + " on the command line: the bust.normals unit is off (no mesh copies)");
                        return true;
                    }
            }
            catch (Exception) { }
            return false;
        }

        public BustNormalsUnit() : base(UnitMap.BustNormals, EditStep.Shapes, 20, "body.shape") { }

        /// <summary>HS2's rate: 1 for size ≥ 0.5, else InverseLerp(0, 0.5, size) (clamped to 0..1).</summary>
        public static float RateOf(float size) => size >= 0.5f ? 1f : Mathf.InverseLerp(0f, 0.5f, size);

        /// <summary>The shared mesh a renderer draws (skinned or mesh filter), or null.</summary>
        public static Mesh MeshOf(Renderer r)
        {
            if (!r) return null;
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf ? mf.sharedMesh : null;
        }

        static void SetMesh(Renderer r, Mesh m)
        {
            if (r is SkinnedMeshRenderer smr)
            {
                var lb = smr.localBounds;   // (assigning the mesh resets the bounds the rebind set)
                smr.sharedMesh = m;
                smr.localBounds = lb;
                return;
            }
            var mf = r.GetComponent<MeshFilter>();
            if (mf) mf.sharedMesh = m;
        }

        public override void Apply(EditContext x)
        {
            var ctx = x.Ctx;
            if (!Enabled || x.Male || ctx == null || !ctx.Body) return;
            var st = ctx.BustNormals;
            if (st == null) { st = ctx.BustNormals = new BustNormalsState(); Load(x, st); }
            if (st.Missing) return;
            var shape = x.Doc.Body.Shape;
            float size = shape != null && shape.Length > 1 ? shape[1] : 0.5f;
            float r = RateOf(size);
            Stopwatch sw = null;
            bool any = false;
            foreach (var e in st.Entries)
            {
                if (!e.Applicable || !e.Renderer) continue;
                var drawn = MeshOf(e.Renderer);
                if (!drawn) continue;
                if (drawn != e.Copy)
                {
                    // the first run, or the drawn mesh was replaced (MaterialEditor RecalculateNormals): an owned copy of it
                    if (drawn.vertexCount != e.Max.Length) continue;
                    var cw = Stopwatch.StartNew();
                    var copy = Object.Instantiate(drawn);
                    copy.name = drawn.name;
                    SetMesh(e.Renderer, copy);
                    if (e.Copy) { ctx.OwnedMeshes.Remove(e.Copy); Object.DestroyImmediate(e.Copy); }
                    e.Copy = copy;
                    e.Rate = float.NaN;
                    ctx.OwnedMeshes.Add(copy);
                    st.Copies++;
                    st.LastCopyMs = cw.Elapsed.TotalMilliseconds;
                    try { st.CopyBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(copy); } catch (Exception) { }
                }
                if (BitConverter.SingleToInt32Bits(e.Rate) == BitConverter.SingleToInt32Bits(r)) { any = true; continue; }
                if (sw == null) sw = Stopwatch.StartNew();
                var buf = e.Buffer ?? (e.Buffer = new Vector3[e.Max.Length]);
                var min = e.Min; var max = e.Max;
                for (int i = 0; i < buf.Length; i++) buf[i] = Vector3.Lerp(min[i], max[i], r);
                e.Copy.SetNormals(buf);
                e.Rate = r;
                st.Blends++;
                any = true;
            }
            if (sw != null) st.LastBlendMs = sw.Elapsed.TotalMilliseconds;
            if (any) st.Rate = r;
        }

        /// <summary>One entry of a NormalData asset (copied).</summary>
        public sealed class NormalEntry
        {
            public string Object;
            public Vector3[] Min, Max;
        }

        /// <summary>
        /// Loads a NormalData asset: from an abdata bundle (<paramref name="zipmod"/> null), or from a zipmod's bundle (opened
        /// from that zipmod first); its entries copied, null when absent. The game's NormalData class
        /// lives in its IL assembly (the vanilla asset's script resolves to the compat stub of Assets/Compat/IL, not to the
        /// Assembly-CSharp one), so the asset is loaded as a ScriptableObject and its fields (data: ObjectName, NormalMin,
        /// NormalMax) are read by name — the same layout in both stubs.
        /// </summary>
        public static List<NormalEntry> LoadData(HS2Next.Content.ContentCatalog cat, HS2Next.Content.ZipmodInfo zipmod, string bundle, string asset)
        {
            if (cat == null || string.IsNullOrEmpty(bundle) || string.IsNullOrEmpty(asset)) return null;
            ScriptableObject so;
            if (zipmod == null) so = cat.LoadAsset<ScriptableObject>(bundle, asset);
            else
            {
                var b = zipmod.HasBundle(bundle) ? zipmod.OpenBundle(bundle, cat) : cat.LoadBundle(zipmod, bundle, null);
                so = cat.LoadAssetFrom<ScriptableObject>(b, asset, zipmod, bundle);
            }
            return Entries(so);
        }

        /// <summary>The entries of a NormalData object (either stub type); null when it is not one.</summary>
        public static List<NormalEntry> Entries(Object o)
        {
            if (!o) return null;
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            if (!(o.GetType().GetField("data", F)?.GetValue(o) is System.Collections.IEnumerable list)) return null;
            var r = new List<NormalEntry>();
            foreach (var p in list)
            {
                if (p == null) continue;
                var t = p.GetType();
                var min = t.GetField("NormalMin", F)?.GetValue(p) as List<Vector3>;
                var max = t.GetField("NormalMax", F)?.GetValue(p) as List<Vector3>;
                r.Add(new NormalEntry { Object = t.GetField("ObjectName", F)?.GetValue(p) as string, Min = min?.ToArray(), Max = max?.ToArray() });
            }
            return r;
        }

        /// <summary>Tests and tools: the Min / Max arrays of one entry of a NormalData asset (copies); false when the asset or
        /// the entry is absent.</summary>
        public static bool ReadEntry(HS2Next.Content.ContentCatalog cat, HS2Next.Content.ZipmodInfo zipmod, string bundle, string asset, string objectName,
                                     out Vector3[] min, out Vector3[] max)
        {
            min = max = null;
            var data = LoadData(cat, zipmod, bundle, asset);
            if (data == null) return false;
            foreach (var p in data)
                if (p.Object == objectName) { min = p.Min; max = p.Max; return min != null && max != null; }
            return false;
        }

        static void Load(EditContext x, BustNormalsState st)
        {
            var ctx = x.Ctx;
            List<NormalEntry> data = null;
            var src = ctx.BodyNormals;
            try
            {
                if (src != null && src.None)
                {
                    st.Source = src.Source; st.Bundle = null; st.Asset = null;
                    st.Missing = true;
                    st.MissingReported = true;
                    st.MissingReason = "no normals for the supplied body (" + src.Source + ")";
                    x.Log.Add("bust normals: none (" + src.Source + "): not blended");
                    return;
                }
                if (src != null)
                {
                    st.Source = src.Source; st.Bundle = src.File; st.Asset = src.Asset ?? BustNormalsState.DefaultAsset;
                    var zipmod = src.Zipmod;
                    if (zipmod == null && !string.IsNullOrEmpty(src.ZipmodGuid))
                    {
                        var byGuid = x.Catalog?.ZipmodsByGuid;
                        if (byGuid == null || !byGuid.TryGetValue(src.ZipmodGuid, out zipmod) || zipmod == null)
                            st.MissingReason = "the zipmod " + src.ZipmodGuid + " is not installed";
                    }
                    data = st.MissingReason != null ? null : LoadData(x.Catalog, zipmod, src.File, st.Asset);
                }
                else
                {
                    st.Source = "vanilla"; st.Bundle = BustNormalsState.VanillaBundle; st.Asset = BustNormalsState.DefaultAsset;
                    data = LoadData(x.Catalog, null, st.Bundle, st.Asset);
                }
            }
            catch (Exception e) { st.MissingReason = e.GetType().Name + ": " + e.Message; data = null; }
            if (data == null)
            {
                st.Missing = true;
                if (st.MissingReason == null) st.MissingReason = "no NormalData " + st.Asset + " in " + st.Bundle;
                x.Log.Add("bust normals: " + st.MissingReason + " (" + st.Source + "): not blended");
                return;
            }
            foreach (var p in data)
            {
                var e = new BustNormalsState.Entry { Object = p.Object, Min = p.Min, Max = p.Max };
                var t = string.IsNullOrEmpty(p.Object) ? null : EditContext.Find(ctx.Body.transform, p.Object);
                e.Renderer = t ? t.GetComponent<Renderer>() : null;
                var mesh = MeshOf(e.Renderer);
                e.Vertices = mesh ? mesh.vertexCount : -1;
                e.Applicable = mesh && e.Min != null && e.Max != null && e.Min.Length == e.Max.Length && e.Max.Length == mesh.vertexCount;
                st.Entries.Add(e);
            }
            int applicable = 0;
            foreach (var e in st.Entries) if (e.Applicable) applicable++;
            x.Log.Add($"bust normals: {st.Asset} ({st.Source}): {st.Entries.Count} entries, {applicable} applicable");
        }
    }
}
