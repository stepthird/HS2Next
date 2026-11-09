// Chara to Item: the import of the old bakes. Each old bake is rebuilt as the old writer built it,
// with HS2Next's catalog, in a hidden staging root: its nodes as objects; its sources (a prefab or a material by bundle path and asset
// name) loaded through the catalog; its textures from their files (every level, the linear flag; three channels as four), by name among
// a source's materials, or by bundle and name from a list bundle; its materials as the source's material copied or a new one on the
// loaded original of their shader, the stored values written where the shader declares them, texture slots by mode, keywords, queue,
// passes and tag; then ShaderMap maps each loaded original to its replacement (a shader with no loaded original: its replacement by
// name, else the fallback, bake.importShader); its meshes from the source prefab or from their data; its renderers with their bones,
// bounds, probes and layers, blend weights set; its twist-correction rows as joint-correction categories. The staging root is then baked
// as a prop into a new self-contained bake (kind imported, the old name, created from the old ticks, the old character's name as the
// source; a cut renderer keeps its section, a filled part its insides, the FK list the old one pruned, members marked by the mosaic set)
// and stored under its old number. A number the store holds already is skipped (bake.importExists): a second run adds nothing.
// The old folder is read only: files are opened for reading with a share mode that lets others read, nothing is written there.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Bake
{
    /// <summary>How one old material was rebuilt (BH4): its old shader, the route, the loaded original, the replacement expected and got.</summary>
    public sealed class OldMaterialRoute
    {
        public const string Original = "original", ByName = "byName", Fallback = "fallback";
        public int Index;
        public string Name = "", OldShader = "", Route = "", Loaded = "", Expected = "", Got = "";
        /// <summary>The old material's shader came from a shared copy (no loaded original of its own).</summary>
        public bool SharedCopy;
        /// <summary>A base material was copied from a source.</summary>
        public bool Base;
    }

    /// <summary>What one old bake became.</summary>
    public sealed class OldBakeImported
    {
        public string File = "", Name = "";
        public int No;
        /// <summary>Renderers and textures of the new bake.</summary>
        public int Renderers, Textures, Members, Sections, Fills;
        public string MainSha = "";
        public long Bytes;
        public double Ms;
        public BakeStoreRow Row;
        public readonly FindingList Findings = new FindingList();
        /// <summary>Resolution counts: source.prefab, source.material, source.missing, texture.stored, texture.source, texture.list,
        /// texture.missing, mesh.stored, mesh.referenced, mesh.missing, renderer.left, material.original, material.byName,
        /// material.fallback, material.base, bounds.* (the old bounds against a referenced prefab renderer's).</summary>
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<OldMaterialRoute> Materials = new List<OldMaterialRoute>();
        public void Inc(string k, int n = 1) { Counts.TryGetValue(k, out var v); Counts[k] = v + n; }
        public int Count(string k) => Counts.TryGetValue(k, out var v) ? v : 0;
        /// <summary>Sources, textures and meshes that could not be resolved.</summary>
        public int Unresolved => Count("source.missing") + Count("texture.missing") + Count("mesh.missing") + Count("renderer.left");
    }

    /// <summary>An old bake that was not imported: its file, its number when read, why (a finding code).</summary>
    public sealed class OldBakeSkipped
    {
        public string File = "", Code = "", Reason = "";
        public int No;
    }

    public sealed class OldBakeImportResult
    {
        public string Folder = "";
        public readonly List<OldBakeImported> Imported = new List<OldBakeImported>();
        public readonly List<OldBakeSkipped> Skipped = new List<OldBakeSkipped>();
        /// <summary>Every finding of the run (each also in its bake's list).</summary>
        public readonly FindingList Findings = new FindingList();
        public int LastNumber;
        public double Ms;
    }

    public static class OldBakeImport
    {
        public const string FolderName = "CharaToItem";
        /// <summary>The finding of an old bake whose rebuild or capture failed: that file is skipped, the others go on.</summary>
        public const string Failed = "bake.importFailed";

        /// <summary>The folder of the old bakes under the game's UserData.</summary>
        public static string DefaultFolder(string hs2Root) => string.IsNullOrEmpty(hs2Root) ? "" : Path.Combine(hs2Root, "UserData", FolderName);

        /// <summary>The import bakes through the capture, which needs a graphics device.</summary>
        public static bool CanRun => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>The app's import: the runtime's catalog and store; the list's bake rows read again after a write. Main thread.</summary>
        public static OldBakeImportResult Run(SceneRuntime rt, string folder)
        {
            if (rt == null) throw new ArgumentNullException(nameof(rt));
            var store = BakeProvider.StoreOf(rt) ?? throw new InvalidOperationException("the app has no UserData folder for the store");
            var r = Run(rt.Services?.Catalog, store, string.IsNullOrEmpty(folder) ? DefaultFolder(rt.Services?.Hs2Root) : folder);
            if (r.Imported.Count > 0 && rt.Services?.Db != null) AppItemRows.RefreshBakes(rt.Services.Db);
            return r;
        }

        /// <summary>
        /// Imports every old bake of <paramref name="folder"/> into <paramref name="store"/> (main thread; needs a graphics device). The
        /// store is held busy meanwhile (a purge waits). The caller lists the store's rows again when <see cref="OldBakeImportResult.Imported"/>
        /// is not empty (AppItemRows.RefreshBakes).
        /// </summary>
        public static OldBakeImportResult Run(ContentCatalog catalog, BakeStore store, string folder)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (!CanRun) throw new InvalidOperationException("the import needs a graphics device");
            var total = Stopwatch.StartNew();
            var res = new OldBakeImportResult { Folder = folder ?? "" };
            var scan = OldBakeReader.Scan(folder);
            if (scan.Files.Count == 0)
            {
                res.Findings.Warning(BakeFindings.ImportSource, folder ?? "", "no old bake in the folder");
                return res;
            }
            using (store.Hold())
            {
                int top = scan.LastNumber ?? 0;
                foreach (var path in scan.Files)
                {
                    var one = new FindingList();
                    var old = OldBakeReader.ReadFile(path, one);
                    res.Findings.AddRange(one);
                    string file = Path.GetFileName(path);
                    if (old == null)
                    {
                        res.Skipped.Add(new OldBakeSkipped { File = file, Code = OldBakeReader.Corrupt, Reason = one.Count > 0 ? one[0].Message : "unreadable" });
                        continue;
                    }
                    top = Math.Max(top, old.Number);
                    var have = store.Find(old.Number);
                    if (have != null)
                    {
                        string why = "the store holds row " + old.Number + " already (" + have.Kind + ")";
                        res.Findings.Info(BakeFindings.ImportExists, file, why);
                        res.Skipped.Add(new OldBakeSkipped { File = file, No = old.Number, Code = BakeFindings.ImportExists, Reason = why });
                        continue;
                    }
                    OldBakeImported done;
                    try { done = One(old, scan.TextureFolder, catalog, store); }
                    catch (Exception e)
                    {
                        // one old bake that cannot be made does not stop the others
                        done = new OldBakeImported { Name = old.Name, No = old.Number };
                        done.Findings.Error(Failed, file, "the bake could not be made (" + e.GetType().Name + ": " + e.Message + "): skipped");
                        UnityEngine.Debug.LogWarning("[bake] import of " + file + " failed: " + e);
                    }
                    done.File = file;
                    res.Findings.AddRange(done.Findings);
                    if (done.Row != null) res.Imported.Add(done);
                    else res.Skipped.Add(new OldBakeSkipped { File = file, No = old.Number, Code = done.Findings.Count > 0 ? done.Findings[done.Findings.Count - 1].Code : BakeFindings.Empty, Reason = done.Findings.Count > 0 ? done.Findings[done.Findings.Count - 1].Message : "nothing to bake" });
                }
                if (top > store.LastNumber) store.RaiseLastNumber(top);
                res.LastNumber = store.LastNumber;
            }
            res.Ms = total.Elapsed.TotalMilliseconds;
            return res;
        }

        /// <summary>Rebuilds, bakes and stores one old bake under its number (its row in <see cref="OldBakeImported.Row"/>, null when nothing
        /// was stored: the reason is the last finding).</summary>
        public static OldBakeImported One(OldBake old, string textureFolder, ContentCatalog catalog, BakeStore store)
        {
            var sw = Stopwatch.StartNew();
            var o = new OldBakeImported { File = old.File, Name = old.Name, No = old.Number };
            BakeJob job = null;
            using (var staged = Stage(old, textureFolder, catalog, o))
            {
                try
                {
                    if (staged.Kept == 0)
                    {
                        o.Findings.Warning(BakeFindings.Empty, old.File, "nothing of the old bake could be built: nothing is stored");
                        return o;
                    }
                    // the job before its capture (as BakeJob.OfProp makes it), so that a capture that throws still frees the textures it built
                    job = new BakeJob();
                    job.Capture = BakeCapture.Prop(staged.Root.transform, old.Fk, BakeData.KindImported, job);
                    Complete(job.Capture, old, staged);
                    foreach (var f in job.Capture.Findings) o.Findings.Add(f.Severity, f.Code, old.File + ":" + f.Path, f.Message);
                    var b = job.Capture.Bake;
                    if (b.Renderers.Count == 0)
                    {
                        o.Findings.Warning(BakeFindings.Empty, old.File, "the old bake drew nothing: nothing is stored");
                        return o;
                    }
                    job.Finish();
                    o.Row = store.Add(job.Row(old.Name), job.AllBlobs(), old.Number);
                    o.MainSha = job.MainSha;
                    o.Bytes = job.Bytes;
                    o.Renderers = b.Renderers.Count;
                    o.Textures = b.Textures.Count;
                    foreach (var r in b.Renderers)
                    {
                        if (r.Member) o.Members++;
                        if (r.Inside == BakeData.InsideSection) o.Sections++;
                        if (r.Inside == BakeData.InsideFill) o.Fills++;
                    }
                }
                catch (BakeStoreException e)
                {
                    o.Row = null;
                    o.Findings.Error(e.Code, old.File, e.Message);
                }
                catch (ArgumentException e)
                {
                    o.Row = null;
                    o.Findings.Error(BakeFindings.StoreWrite, old.File, e.Message);
                }
                finally
                {
                    job?.Dispose();
                    o.Ms = sw.Elapsed.TotalMilliseconds;
                }
            }
            return o;
        }

        /// <summary>The members of the new bake the old one gives: its times, its source, its origin, parts, insides, sections and
        /// joint-correction categories (the capture made the rest).</summary>
        static void Complete(BakeCaptureResult cap, OldBake old, Staged staged)
        {
            var b = cap.Bake;
            b.Kind = BakeData.KindImported;
            b.Created = BakeCapture.Seconds(old.CreatedUtc());
            b.Source = new BakeData.SourceInfo { Kind = old.Sex >= 0 ? "chara" : "item", Name = old.CharaName ?? "", Sex = old.Sex >= 0 ? old.Sex : -1 };
            string rootName = old.Nodes.Count > 0 ? old.Nodes[0].Name : "";
            b.Origin = rootName == BakeData.OriginCentre ? BakeData.OriginCentre : rootName == BakeData.OriginCut ? BakeData.OriginCut : BakeData.OriginItem;
            var sc = old.SectionColour ?? new[] { 0.35f, 0.03f, 0.03f };
            b.Insides = new BakeData.InsidesData { Fill = old.FillInside, Section = new Vector3(sc[0], sc[1], sc[2]), OwnInside = old.OwnInside };
            List<string> parts = old.FillInside ? new List<string>() : null;
            for (int i = 0; i < b.Renderers.Count && i < cap.Live.Count; i++)
            {
                var rd = b.Renderers[i];
                if (!staged.OldOf.TryGetValue(cap.Live[i], out var x)) continue;
                rd.Part = x.Part ?? "";
                if (parts != null && rd.Part.Length > 0 && !parts.Contains(rd.Part)) parts.Add(rd.Part);
                if (rd.Member) { rd.Inside = BakeData.InsideNone; continue; }
                if (x.Inside == OldRenderer.InsideSection) rd.Inside = BakeData.InsideSection;
                else if ((old.FillInside || x.Inside == OldRenderer.InsideFill) && BakeInsides.Eligible(b, rd)) rd.Inside = BakeData.InsideFill;
                else rd.Inside = BakeData.InsideNone;
            }
            b.Parts = parts != null && parts.Count > 0 ? parts : null;
            b.Correction = BakeFk.HoldsSkeleton(b) ? old.Correction(HS2Next.Motion.Corrective.JointCorrectionTable.CategoryCount) : null;
        }

        // ------------------------------------------------------------ the staging root

        /// <summary>An old bake rebuilt in a hidden staging root (its renderers drawn by no camera); disposing destroys everything made.</summary>
        public sealed class Staged : IDisposable
        {
            public GameObject Root;
            public readonly List<Transform> Nodes = new List<Transform>();
            /// <summary>The old renderer of each staged renderer.</summary>
            public readonly Dictionary<Renderer, OldRenderer> OldOf = new Dictionary<Renderer, OldRenderer>();
            /// <summary>The materials by old index (null: not built).</summary>
            public readonly List<Material> Materials = new List<Material>();
            /// <summary>Renderers built.</summary>
            public int Kept;
            internal readonly List<Object> Owned = new List<Object>();

            public void Dispose()
            {
                if (Root) Object.DestroyImmediate(Root);
                foreach (var x in Owned) if (x) Object.DestroyImmediate(x);
                Owned.Clear();
                Root = null;
            }
        }

        /// <summary>Rebuilds an old bake in a hidden staging root with the catalog; what could not be resolved is counted and
        /// reported in <paramref name="o"/>. Main thread.</summary>
        public static Staged Stage(OldBake old, string textureFolder, ContentCatalog catalog, OldBakeImported o)
        {
            var st = new Staged();
            var ctx = new Ctx { Old = old, TextureFolder = textureFolder ?? "", Catalog = catalog, O = o, S = st };
            try
            {
                // 1. nodes
                for (int i = 0; i < old.Nodes.Count; i++)
                {
                    var n = old.Nodes[i];
                    var go = new GameObject(n.Name) { hideFlags = HideFlags.HideAndDontSave };
                    var t = go.transform;
                    if (n.Parent >= 0) t.SetParent(st.Nodes[n.Parent], false);
                    t.SetLocalPositionAndRotation(new Vector3(n.P[0], n.P[1], n.P[2]), Norm(n.R));
                    t.localScale = new Vector3(n.S[0], n.S[1], n.S[2]);
                    go.layer = n.Layer >= 0 && n.Layer < 32 ? n.Layer : 0;
                    st.Nodes.Add(t);
                    if (i == 0) st.Root = go;
                }
                if (!st.Root) return st;
                // 4. materials (their sources and textures on demand)
                for (int i = 0; i < old.Materials.Count; i++) st.Materials.Add(null);
                // 5, 6. meshes and renderers
                for (int i = 0; i < old.Renderers.Count; i++) ctx.MakeRenderer(i);
                // the loaded originals pick their replacements; every renderer gets its rendering layers
                foreach (var route in o.Materials)
                    if (route.Route == OldMaterialRoute.Original)
                    {
                        var m = st.Materials[route.Index];
                        route.Expected = m ? (ShaderMap.ResolveFor(m) ?? (m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal) || m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) ? m.shader.name : FallbackShader)) : "";
                    }
                ShaderMap.ApplyUrp(st.Root, copy: false);
                foreach (var route in o.Materials)
                {
                    var m = st.Materials[route.Index];
                    route.Got = m && m.shader ? m.shader.name : "";
                }
                for (int i = 0; i < old.Nodes.Count; i++) if (!old.Nodes[i].Active && i > 0) st.Nodes[i].gameObject.SetActive(false);
            }
            catch
            {
                st.Dispose();
                throw;
            }
            return st;
        }

        public const string FallbackShader = "Universal Render Pipeline/Lit";

        static Quaternion Norm(float[] q)
        {
            var r = new Quaternion(q[0], q[1], q[2], q[3]);
            float n = Mathf.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
            return n > 1e-6f ? new Quaternion(r.x / n, r.y / n, r.z / n, r.w / n) : Quaternion.identity;
        }

        /// <summary>A material's name without the " (Instance)" endings the engine adds to copies.</summary>
        public static string CleanName(string n)
        {
            n = n ?? "";
            const string inst = " (Instance)";
            while (n.EndsWith(inst, StringComparison.Ordinal)) n = n.Substring(0, n.Length - inst.Length);
            return n;
        }

        sealed class Ctx
        {
            public OldBake Old;
            public string TextureFolder;
            public ContentCatalog Catalog;
            public OldBakeImported O;
            public Staged S;
            readonly Dictionary<int, Object> sources = new Dictionary<int, Object>();
            readonly Dictionary<int, Texture> textures = new Dictionary<int, Texture>();
            readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
            readonly HashSet<int> built = new HashSet<int>();

            string File => Old.File;

            // ---------------------------------------------------- sources

            public Object SourceAt(int i)
            {
                if (i < 0 || i >= Old.Sources.Count) return null;
                if (sources.TryGetValue(i, out var have)) return have;
                var s = Old.Sources[i];
                Object got = null;
                try
                {
                    if (Catalog != null)
                        got = s.Kind == OldSource.KindMaterial
                            ? (Object)Catalog.LoadAssetAt<Material>(null, s.Bundle, s.Manifest, s.Asset)
                            : Catalog.LoadAssetAt<GameObject>(null, s.Bundle, s.Manifest, s.Asset);
                }
                catch (Exception e) { UnityEngine.Debug.LogWarning("[bake] import: loading " + s.Bundle + " " + s.Asset + " threw " + e.Message); }
                sources[i] = got;
                if (got) O.Inc(s.Kind == OldSource.KindMaterial ? "source.material" : "source.prefab");
                else
                {
                    O.Inc("source.missing");
                    O.Findings.Warning(BakeFindings.ImportSource, File + ":sources[" + i + "]", "the " + (s.Kind == OldSource.KindMaterial ? "material " : "prefab ") +
                        s.Asset + " of " + s.Bundle + " is not on this machine: what it gives is left out");
                }
                return got;
            }

            /// <summary>The materials a source holds: a material source itself, a prefab's renderers' materials (inactive ones too).</summary>
            IEnumerable<Material> MaterialsOf(int i)
            {
                var s = SourceAt(i);
                if (s is Material m) { yield return m; yield break; }
                if (s is GameObject go)
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (var x in r.sharedMaterials)
                            if (x) yield return x;
            }

            // ---------------------------------------------------- textures

            public Texture TextureAt(int i)
            {
                if (i < 0 || i >= Old.Textures.Count) return null;
                if (textures.TryGetValue(i, out var have)) return have;
                var t = Old.Textures[i];
                Texture got = null;
                string what;
                if (t.Stored) { got = Stored(t); what = "texture.stored"; }
                else if (t.FromSource)
                {
                    what = "texture.source";
                    foreach (var m in MaterialsOf(t.Source))
                    {
                        got = TextureNamed(m, t.Name);
                        if (got) break;
                    }
                    if (!got && SourceAt(t.Source))
                    {
                        // not on the source's materials as loaded: the texture of that name in the source's bundle
                        var s = Old.Sources[t.Source];
                        try { got = Catalog?.LoadAssetAt<Texture>(null, s.Bundle, s.Manifest, t.Name); }
                        catch (Exception e) { UnityEngine.Debug.LogWarning("[bake] import: loading " + s.Bundle + " " + t.Name + " threw " + e.Message); }
                        if (got) O.Inc("texture.sourceBundle");
                        else
                        {
                            // last: on the materials of the bake's other sources (a texture the game set from another of them)
                            for (int k = 0; k < Old.Sources.Count && !got; k++)
                                if (k != t.Source)
                                    foreach (var m in MaterialsOf(k)) { got = TextureNamed(m, t.Name); if (got) break; }
                            if (got) O.Inc("texture.otherSource");
                            else if ((got = FromListRow(t)) != null) O.Inc("texture.listRow");
                            else O.Findings.Warning(BakeFindings.ImportSource, File + ":textures[" + i + "]", "the texture " + t.Name + " is neither on the sources' materials nor in its source's bundle: the slot keeps none");
                        }
                    }
                }
                else if (t.FromList)
                {
                    what = "texture.list";
                    try { got = Catalog?.LoadAssetAt<Texture>(null, t.ListBundle, t.ListManifest, t.Name); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("[bake] import: loading " + t.ListBundle + " " + t.Name + " threw " + e.Message); }
                    if (!got) O.Findings.Warning(BakeFindings.ImportSource, File + ":textures[" + i + "]", "the texture " + t.Name + " of " + t.ListBundle + " is not on this machine: the slot keeps none");
                }
                else what = "texture.none";
                textures[i] = got;
                O.Inc(got || what == "texture.none" ? what : "texture.missing");
                return got;
            }

            /// <summary>The list categories whose rows name a texture bundle of their own (TexAB): the hair parts and the accessories.</summary>
            static readonly string[] TextureRowCategories =
            {
                "so_hair_b", "so_hair_f", "so_hair_s", "so_hair_o", "ao_head", "ao_ear", "ao_glasses", "ao_face", "ao_neck", "ao_shoulder", "ao_chest",
                "ao_waist", "ao_back", "ao_arm", "ao_hand", "ao_leg", "ao_kokan",
            };

            /// <summary>A texture the game set from the list row of the source's prefab: the row's texture bundle (TexAB), by name.</summary>
            Texture FromListRow(OldTextureRef t)
            {
                if (Catalog == null || t.Source < 0 || t.Source >= Old.Sources.Count) return null;
                var s = Old.Sources[t.Source];
                foreach (var name in TextureRowCategories)
                {
                    if (!ListCategories.TryParse(name, out int cat)) continue;
                    foreach (var e in Catalog.EntriesOf(cat))
                    {
                        if (e == null || e["MainData"] != s.Asset) continue;
                        var ab = e["TexAB"];
                        if (string.IsNullOrEmpty(ab) || ab == "0") continue;
                        try
                        {
                            var x = Catalog.LoadAssetAt<Texture>(e, ab, t.Name);
                            if (x) return x;
                        }
                        catch (Exception ex) { UnityEngine.Debug.LogWarning("[bake] import: loading " + ab + " " + t.Name + " threw " + ex.Message); }
                    }
                }
                return null;
            }

            /// <summary>A texture of that name on any texture property the material holds (its shader's and those saved with it).</summary>
            static Texture TextureNamed(Material m, string name)
            {
                foreach (int id in m.GetTexturePropertyNameIDs())
                {
                    var x = m.GetTexture(id);
                    if (x && x.name == name) return x;
                }
                return null;
            }

            /// <summary>A stored texture from its file: every level as RGBA32 (three channels made four), the linear flag, the sampler of the
            /// old record; refused (and none) when the file is missing or is not what its name says.</summary>
            Texture Stored(OldTextureRef t)
            {
                string path = OldBakeReader.TexturePath(TextureFolder, t.Hash);
                if (!System.IO.File.Exists(path))
                {
                    O.Findings.Warning(BakeFindings.ImportSource, File + ":" + t.Hash, "the texture file " + t.Hash + OldBakeReader.TextureExtension + " is missing: the slot keeps none");
                    return null;
                }
                var f = new FindingList();
                var head = OldBakeReader.ReadTextureHeader(path, f);
                Texture2D tex = null;
                bool ok = head != null && OldBakeReader.ReadTexture(path, (level, w, h, rgba) =>
                {
                    if (level == 0)
                        tex = new Texture2D(w, h, TextureFormat.RGBA32, head.Levels.Count, head.Linear) { name = string.IsNullOrEmpty(t.Name) ? t.Hash : t.Name, hideFlags = HideFlags.HideAndDontSave };
                    tex.SetPixelData(rgba, level);
                }, f, out _);
                foreach (var x in f) O.Findings.Add(x.Severity, x.Code, File + ":" + x.Path, x.Message);
                if (!ok || !tex)
                {
                    if (tex) Object.DestroyImmediate(tex);
                    return null;
                }
                tex.wrapModeU = (TextureWrapMode)t.WrapU;
                tex.wrapModeV = (TextureWrapMode)t.WrapV;
                tex.filterMode = (FilterMode)Mathf.Clamp(t.Filter, 0, 2);
                tex.anisoLevel = t.Aniso;
                tex.mipMapBias = t.MipBias;
                tex.Apply(false, true);
                S.Owned.Add(tex);
                return tex;
            }

            // ---------------------------------------------------- materials

            public Material MaterialAt(int i)
            {
                if (i < 0 || i >= Old.Materials.Count) return null;
                if (built.Contains(i)) return S.Materials[i];
                built.Add(i);
                var om = Old.Materials[i];
                var route = new OldMaterialRoute { Index = i, Name = CleanName(om.Name), OldShader = om.Shader, SharedCopy = om.ShaderSource == OldMaterial.ShaderSharedCopy };
                O.Materials.Add(route);
                Material baseMat = BaseOf(om, i);
                route.Base = baseMat;
                if (baseMat) O.Inc("material.base");
                // the loaded original: a material of the shader source with that shader, else the base's when it has that shader
                Shader original = null;
                if (om.ShaderSource >= 0)
                    foreach (var m in MaterialsOf(om.ShaderSource))
                        if (m.shader && m.shader.name == om.Shader) { original = m.shader; break; }
                if (!original && baseMat && baseMat.shader && baseMat.shader.name == om.Shader) original = baseMat.shader;
                Material mat;
                if (original)
                {
                    mat = baseMat ? MaterialCopies.Copy(baseMat) : new Material(original);
                    if (mat.shader != original) mat.shader = original;
                    route.Route = OldMaterialRoute.Original;
                    route.Loaded = original.name;
                    O.Inc("material.original");
                    if (route.SharedCopy)
                        O.Findings.Info(BakeFindings.ImportShader, File + ":materials[" + i + "]", "the material " + route.Name + " used a shared copy of " + om.Shader + "; the original loaded with its base material is used");
                }
                else if (ShaderMap.TryResolve(om.Shader, null, out var rep) && Shader.Find(rep) is Shader rs)
                {
                    mat = baseMat ? MaterialCopies.Copy(baseMat) : new Material(rs);
                    if (mat.shader != rs) MaterialCopies.SetShader(mat, rs);
                    route.Route = OldMaterialRoute.ByName;
                    route.Expected = rep;
                    O.Inc("material.byName");
                    O.Findings.Info(BakeFindings.ImportShader, File + ":materials[" + i + "]", "no loaded original of " + om.Shader + " for " + route.Name +
                        (route.SharedCopy ? " (a shared copy)" : "") + ": its replacement " + rep + " by name");
                }
                else
                {
                    mat = baseMat ? MaterialCopies.Copy(baseMat) : new Material(Shader.Find("HS2Next/Standard"));
                    route.Route = OldMaterialRoute.Fallback;
                    route.Expected = FallbackShader;
                    O.Inc("material.fallback");
                    O.Findings.Warning(BakeFindings.ImportShader, File + ":materials[" + i + "]", "no loaded original and no replacement of " + om.Shader + " for " + route.Name + ": the fallback");
                }
                mat.name = route.Name;
                mat.hideFlags = HideFlags.HideAndDontSave;
                S.Owned.Add(mat);
                Values(om, mat);
                if (route.Route == OldMaterialRoute.Fallback) ShaderMap.ApplyFallback(mat);
                else if (route.Route == OldMaterialRoute.ByName && mat.shader.name == MosaicSet.ShaderName) mat.renderQueue = MosaicSet.Queue;
                S.Materials[i] = mat;
                return mat;
            }

            /// <summary>The base material: a material source, or a prefab source's renderer at the path and slot (by name when the path fails).</summary>
            Material BaseOf(OldMaterial om, int i)
            {
                if (om.Base < 0) return null;
                var s = SourceAt(om.Base);
                if (s is Material m) return m;
                if (!(s is GameObject go)) return null;
                var r = Find(go.transform, om.BasePath)?.GetComponent<Renderer>();
                var want = CleanName(om.BaseName);
                if (r)
                {
                    var ms = r.sharedMaterials;
                    if (om.BaseSlot >= 0 && om.BaseSlot < ms.Length && ms[om.BaseSlot]) return ms[om.BaseSlot];
                }
                foreach (var x in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var y in x.sharedMaterials)
                        if (y && CleanName(y.name) == want) return y;
                O.Findings.Warning(BakeFindings.ImportSource, File + ":materials[" + i + "]", "the base material " + want + " is not in the source at " + om.BasePath + ": a new material");
                return null;
            }

            /// <summary>The stored values where the shader declares the property, the texture slots by mode, keywords, queue, passes, tag.</summary>
            void Values(OldMaterial om, Material m)
            {
                var sh = m.shader;
                foreach (var kv in om.Floats)
                {
                    int k = sh.FindPropertyIndex(kv.Key);
                    if (k < 0) continue;
                    switch (sh.GetPropertyType(k))
                    {
                        case ShaderPropertyType.Float: case ShaderPropertyType.Range: m.SetFloat(kv.Key, kv.Value); break;
                        case ShaderPropertyType.Int: m.SetInteger(kv.Key, Mathf.RoundToInt(kv.Value)); break;
                    }
                }
                foreach (var kv in om.Vectors)
                {
                    int k = sh.FindPropertyIndex(kv.Key);
                    if (k < 0) continue;
                    var v = kv.Value;
                    switch (sh.GetPropertyType(k))
                    {
                        case ShaderPropertyType.Color: m.SetColor(kv.Key, new Color(v[0], v[1], v[2], v[3])); break;
                        case ShaderPropertyType.Vector: m.SetVector(kv.Key, new Vector4(v[0], v[1], v[2], v[3])); break;
                    }
                }
                foreach (var s in om.Slots)
                {
                    int k = sh.FindPropertyIndex(s.Property);
                    if (k < 0 || sh.GetPropertyType(k) != ShaderPropertyType.Texture) continue;
                    if (s.Mode == OldSlot.ModeNone) m.SetTexture(s.Property, null);
                    else if (s.Mode == OldSlot.ModeStored) m.SetTexture(s.Property, TextureAt(s.Texture));
                    m.SetTextureScale(s.Property, new Vector2(s.Scale[0], s.Scale[1]));
                    m.SetTextureOffset(s.Property, new Vector2(s.Offset[0], s.Offset[1]));
                }
                m.shaderKeywords = om.Keywords.ToArray();
                if (om.Queue >= 0) m.renderQueue = om.Queue;
                foreach (var p in om.PassesOff) m.SetShaderPassEnabled(p, false);
                if (!string.IsNullOrEmpty(om.RenderType)) m.SetOverrideTag("RenderType", om.RenderType);
            }

            // ---------------------------------------------------- meshes and renderers (6)

            public Mesh MeshAt(int i, OldRenderer r)
            {
                if (i < 0 || i >= Old.Meshes.Count) return null;
                if (meshes.TryGetValue(i, out var have)) return have;
                var om = Old.Meshes[i];
                Mesh got;
                if (om.Stored) { got = Build(om); O.Inc("mesh.stored"); }
                else
                {
                    got = Referenced(om, r, i);
                    O.Inc(got ? "mesh.referenced" : "mesh.missing");
                }
                meshes[i] = got;
                return got;
            }

            /// <summary>A referenced mesh: the source prefab's renderer at the path, else a renderer whose mesh has that name; checked by its
            /// bind poses against the renderer's bones and its blend shapes against the renderer's weights.</summary>
            Mesh Referenced(OldMesh om, OldRenderer r, int i)
            {
                if (!(SourceAt(om.Source) is GameObject go)) return null;
                var cands = new List<Renderer>();
                var at = Find(go.transform, om.SourcePath)?.GetComponent<Renderer>();
                if (at) cands.Add(at);
                foreach (var x in go.GetComponentsInChildren<Renderer>(true))
                    if (x != at && BakeCapture.MeshOf(x) is Mesh xm && xm.name == om.Name) cands.Add(x);
                foreach (var c in cands)
                {
                    var mesh = BakeCapture.MeshOf(c);
                    if (!mesh) continue;
                    bool bones = !r.Skinned || mesh.bindposeCount <= r.Bones.Length;
                    bool shapes = r.BlendWeights.Length <= mesh.blendShapeCount;
                    if (!bones || !shapes) continue;
                    if (c is SkinnedMeshRenderer smr) Compare(smr.localBounds, r.Bounds);
                    return mesh;
                }
                O.Findings.Warning(BakeFindings.ImportSource, File + ":meshes[" + i + "]", "the mesh " + om.Name + " is not in its source at " + om.SourcePath +
                    (cands.Count > 0 ? " with " + r.Bones.Length + " bones and " + r.BlendWeights.Length + " blend shapes" : "") + ": the renderer is left out");
                return null;
            }

            /// <summary>Counts how the old bounds' second vector reads against a prefab renderer's (bounds.size, bounds.extents, bounds.other).</summary>
            void Compare(Bounds prefab, float[] old)
            {
                var c = new Vector3(old[0], old[1], old[2]);
                var v = new Vector3(old[3], old[4], old[5]);
                if ((prefab.center - c).magnitude > 1e-3f) { O.Inc("bounds.otherCentre"); return; }
                if ((prefab.size - v).magnitude <= 1e-3f) O.Inc("bounds.size");
                else if ((prefab.extents - v).magnitude <= 1e-3f) O.Inc("bounds.extents");
                else O.Inc("bounds.other");
            }

            Mesh Build(OldMesh om)
            {
                int n = om.VertexCount;
                var m = new Mesh { name = om.Name, hideFlags = HideFlags.HideAndDontSave };
                S.Owned.Add(m);
                m.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                var v = new Vector3[n];
                for (int k = 0; k < n; k++) v[k] = new Vector3(om.Positions[3 * k], om.Positions[3 * k + 1], om.Positions[3 * k + 2]);
                m.vertices = v;
                if (om.Normals != null)
                {
                    var nn = new Vector3[n];
                    for (int k = 0; k < n; k++) nn[k] = new Vector3(om.Normals[3 * k], om.Normals[3 * k + 1], om.Normals[3 * k + 2]);
                    m.normals = nn;
                }
                if (om.Tangents != null)
                {
                    var tt = new Vector4[n];
                    for (int k = 0; k < n; k++) tt[k] = new Vector4(om.Tangents[4 * k], om.Tangents[4 * k + 1], om.Tangents[4 * k + 2], om.Tangents[4 * k + 3]);
                    m.tangents = tt;
                }
                for (int ch = 0; ch < 4; ch++)
                {
                    var uv = om.Uv[ch];
                    if (uv == null) continue;
                    bool four = false;
                    for (int k = 0; k < n && !four; k++) four = uv[4 * k + 2] != 0f || uv[4 * k + 3] != 0f;
                    if (four)
                    {
                        var a = new Vector4[n];
                        for (int k = 0; k < n; k++) a[k] = new Vector4(uv[4 * k], uv[4 * k + 1], uv[4 * k + 2], uv[4 * k + 3]);
                        m.SetUVs(ch, a);
                    }
                    else
                    {
                        var a = new Vector2[n];
                        for (int k = 0; k < n; k++) a[k] = new Vector2(uv[4 * k], uv[4 * k + 1]);
                        m.SetUVs(ch, a);
                    }
                }
                if (om.Colors != null)
                {
                    var c = new Color32[n];
                    for (int k = 0; k < n; k++) c[k] = new Color32(om.Colors[4 * k], om.Colors[4 * k + 1], om.Colors[4 * k + 2], om.Colors[4 * k + 3]);
                    m.colors32 = c;
                }
                if (om.BoneIndices != null)
                {
                    var w = new BoneWeight[n];
                    for (int k = 0; k < n; k++)
                        w[k] = new BoneWeight
                        {
                            boneIndex0 = om.BoneIndices[4 * k], weight0 = om.BoneWeights[4 * k], boneIndex1 = om.BoneIndices[4 * k + 1], weight1 = om.BoneWeights[4 * k + 1],
                            boneIndex2 = om.BoneIndices[4 * k + 2], weight2 = om.BoneWeights[4 * k + 2], boneIndex3 = om.BoneIndices[4 * k + 3], weight3 = om.BoneWeights[4 * k + 3],
                        };
                    m.boneWeights = w;
                }
                int bp = om.BindPoseCount;
                if (bp > 0)
                {
                    var p = new Matrix4x4[bp];
                    for (int j = 0; j < bp; j++) { var x = new Matrix4x4(); for (int e = 0; e < 16; e++) x[e] = om.BindPoses[16 * j + e]; p[j] = x; }
                    m.bindposes = p;
                }
                m.subMeshCount = om.SubMeshes.Count;
                for (int s = 0; s < om.SubMeshes.Count; s++) m.SetIndices(om.SubMeshes[s].Indices, (MeshTopology)om.SubMeshes[s].Topology, s, false);
                m.bounds = new Bounds(new Vector3(om.Bounds[0], om.Bounds[1], om.Bounds[2]), new Vector3(om.Bounds[3], om.Bounds[4], om.Bounds[5]));
                return m;
            }

            public void MakeRenderer(int i)
            {
                var r = Old.Renderers[i];
                var mesh = MeshAt(r.Mesh, r);
                string where = File + ":renderers[" + i + "]";
                if (!mesh)
                {
                    O.Inc("renderer.left");
                    if (r.Mesh < 0 || Old.Meshes[r.Mesh].Stored) O.Findings.Warning(BakeFindings.ImportSource, where, "the renderer has no mesh: left out");
                    return;
                }
                var go = S.Nodes[r.Node].gameObject;
                if (go.GetComponent<Renderer>())
                {
                    O.Inc("renderer.left");
                    O.Findings.Warning(BakeFindings.ImportSource, where, "a second renderer on the node " + go.name + ": left out");
                    return;
                }
                Renderer rr;
                if (r.Skinned)
                {
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mesh;
                    var bones = new Transform[r.Bones.Length];
                    for (int k = 0; k < bones.Length; k++) bones[k] = r.Bones[k] >= 0 ? S.Nodes[r.Bones[k]] : null;
                    smr.bones = bones;
                    smr.rootBone = r.Root >= 0 ? S.Nodes[r.Root] : null;
                    smr.localBounds = new Bounds(new Vector3(r.Bounds[0], r.Bounds[1], r.Bounds[2]), new Vector3(r.Bounds[3], r.Bounds[4], r.Bounds[5]));
                    smr.quality = (SkinQuality)Mathf.Clamp(r.Quality, 0, 4);
                    smr.updateWhenOffscreen = r.UpdateOffscreen;
                    int shapes = Math.Min(r.BlendWeights.Length, mesh.blendShapeCount);
                    for (int k = 0; k < shapes; k++) smr.SetBlendShapeWeight(k, r.BlendWeights[k]);
                    rr = smr;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    rr = go.AddComponent<MeshRenderer>();
                }
                var mats = new Material[r.Materials.Length];
                for (int k = 0; k < mats.Length; k++) mats[k] = MaterialAt(r.Materials[k]);
                rr.sharedMaterials = mats;
                rr.shadowCastingMode = (ShadowCastingMode)Mathf.Clamp(r.Shadow, 0, 3);
                rr.receiveShadows = r.Receive;
                rr.sortingOrder = r.SortingOrder;
                rr.lightProbeUsage = (LightProbeUsage)r.LightProbe;
                rr.reflectionProbeUsage = (ReflectionProbeUsage)r.ReflProbe;
                rr.probeAnchor = r.Anchor >= 0 ? S.Nodes[r.Anchor] : null;
                rr.renderingLayerMask = r.LayerMask;
                // hidden: no camera draws the staging root; the capture reads it as drawn
                rr.forceRenderingOff = true;
                S.OldOf[rr] = r;
                S.Kept++;
            }
        }

        /// <summary>A transform at a path under <paramref name="root"/> (the path may start with the root's own name).</summary>
        static Transform Find(Transform root, string path)
        {
            if (!root) return null;
            if (string.IsNullOrEmpty(path)) return root;
            var t = root.Find(path);
            if (t) return t;
            int s = path.IndexOf('/');
            if (path == root.name) return root;
            if (s > 0 && path.Substring(0, s) == root.name) return root.Find(path.Substring(s + 1));
            return null;
        }
    }
}
