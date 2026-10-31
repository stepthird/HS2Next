// The PPv2 (post-processing stack v2) volumes a map scene saves, read from its bundle without running them. HS2Next does not
// draw PPv2 volumes (their types are not loaded, the components arrive as missing scripts), so a map whose volumes would
// reach HS2's Studio camera looks different from HS2; the map reports them (finding map.ppv2Ignored). Read here: the map's
// bundle (the user's abdata or the zipmod entry, as the catalog loads it) → the scene node → every MonoBehaviour whose
// MonoScript is PostProcessVolume of PPv2's namespace (the script found in the scene node or the bundle's other nodes) →
// its isGlobal / weight / priority, its GameObject's layer and whether that GameObject and its parents are active as
// saved. A volume reaches the Studio camera when it is global, enabled, active in the saved hierarchy and on the layer
// the Studio camera's post-processing listens to (8). Read-only; LZMA bundles and layouts this reader does not know fail
// closed (no finding, the reason kept). Cached per bundle and scene for the process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HS2Next.Content;
using HS2Next.Content.Studio;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>What <see cref="MapPpv2Volumes.Read"/> found in a map scene.</summary>
    public sealed class MapPpv2Info
    {
        public bool Ok;
        public string Error;
        /// <summary>PPv2 volumes in the scene (any layer or state).</summary>
        public int Volumes;
        /// <summary>Volumes that would reach the Studio camera (global, enabled, active as saved, layer 8).</summary>
        public int Reaching;
        /// <summary>Of <see cref="Reaching"/>, those with a weight above 0.</summary>
        public int ReachingWeighted;
        /// <summary>Volumes whose fields or GameObject could not be read (not counted as reaching).</summary>
        public int Unreadable;
        public double Ms;
    }

    public static class MapPpv2Volumes
    {
        /// <summary>The layer HS2's Studio camera's post-processing layer takes volumes from.</summary>
        public const int StudioLayer = 8;
        public const string VolumeClass = "PostProcessVolume", VolumeNamespace = "UnityEngine.Rendering.PostProcessing";
        const int ClassGameObject = 1, ClassTransform = 4, ClassRectTransform = 224;

        static readonly Dictionary<string, MapPpv2Info> cache = new Dictionary<string, MapPpv2Info>(StringComparer.OrdinalIgnoreCase);

        public static void ForgetCache() => cache.Clear();

        /// <summary>The volumes of a map row's scene (never throws; Ok false with the reason).</summary>
        public static MapPpv2Info Read(StudioMap row, string scenePath, ContentCatalog catalog, string hs2Root)
        {
            var sw = Stopwatch.StartNew();
            MapPpv2Info r;
            string key = null;
            try
            {
                if (row == null || string.IsNullOrEmpty(row.BundlePath)) return Fail("no map row / bundle", sw);
                string scene = !string.IsNullOrEmpty(scenePath) ? Path.GetFileNameWithoutExtension(scenePath) : row.FileName;
                var b = OpenBundle(row, catalog, hs2Root, out key);
                if (b == null) return Fail("bundle file not found: " + row.BundlePath, sw);
                key += "|" + scene;
                if (cache.TryGetValue(key, out var hit)) return hit;
                r = ReadBundle(b, scene);
            }
            catch (Exception e) { r = new MapPpv2Info { Error = e.GetType().Name + ": " + e.Message }; }
            r.Ms = sw.Elapsed.TotalMilliseconds;
            if (key != null) cache[key] = r;
            return r;
        }

        static MapPpv2Info Fail(string why, Stopwatch sw) => new MapPpv2Info { Error = why, Ms = sw.Elapsed.TotalMilliseconds };

        // The bundle of a map row in the catalog's order: an abdata file of the install first, then the row's zipmod, then any
        // zipmod shipping the path.
        static Hs2BundleFile OpenBundle(StudioMap row, ContentCatalog cat, string root, out string key)
        {
            string path = row.BundlePath.Replace('\\', '/');
            if (!string.IsNullOrEmpty(root) && (cat == null || cat.HasRealBundle(path)))
            {
                string f = Path.Combine(root, "abdata", path);
                if (File.Exists(f))
                {
                    key = f;
                    return Hs2BundleFile.OpenFile(f, 0, new FileInfo(f).Length, f);
                }
            }
            var z = row.Zipmod != null && row.Zipmod.HasBundle(path) ? row.Zipmod : cat?.FindZipmodShipping(path);
            if (z != null && z.Index.Entries.TryGetValue("abdata/" + path, out var ze))
            {
                key = z.Path + "!" + path;
                return ze.Method == 0
                    ? Hs2BundleFile.OpenFile(z.Path, ze.DataOffset, ze.Size, key)
                    : Hs2BundleFile.FromBytes(z.Index.ReadAll(ze), key);
            }
            key = path;
            return null;
        }

        /// <summary>The volumes of the scene node of <paramref name="b"/> (the node of <paramref name="scene"/>).</summary>
        public static MapPpv2Info ReadBundle(Hs2BundleFile b, string scene)
        {
            Hs2BundleNode? node = b.Find("BuildPlayer-" + scene);
            if (node == null)
            {
                Hs2BundleNode? only = null;
                int n = 0;
                foreach (var x in b.Nodes)
                    if (x.Path.StartsWith("BuildPlayer-", StringComparison.Ordinal) && !x.Path.EndsWith(".sharedAssets", StringComparison.Ordinal)) { only = x; n++; }
                if (n == 1) node = only;
            }
            if (node == null) return new MapPpv2Info { Error = "no scene node for " + scene };
            var nd = node.Value;
            var f = Hs2SerializedFile.FromStream(() => b.OpenNode(nd), b.Label + "!" + nd.Path);
            var res = new MapPpv2Info { Ok = true };
            var behaviours = new List<Hs2SerializedObject>();
            foreach (var o in f.Objects) if (o.ClassId == Hs2SerializedFile.ClassMonoBehaviour) behaviours.Add(o);
            if (behaviours.Count == 0) return res;
            var bytes = f.ReadBytes(behaviours);
            // the scripts, resolved once per reference (this node or another node of the same bundle)
            var scripts = new Dictionary<(int, long), bool>();
            var others = new Dictionary<string, Hs2SerializedFile>(StringComparer.OrdinalIgnoreCase);
            var volumes = new List<(Hs2PPtr go, bool enabled, Hs2FieldReader rest)>();
            foreach (var o in behaviours)
            {
                var r = new Hs2FieldReader(bytes[o.PathId], 0);
                Hs2SerializedFile.ReadMonoBehaviourHeader(r, out var go, out bool enabled, out var script, out _);
                var sk = (script.FileId, script.PathId);
                if (!scripts.TryGetValue(sk, out bool isVolume)) scripts[sk] = isVolume = IsVolumeScript(b, f, script, others);
                if (isVolume) volumes.Add((go, enabled, r));
            }
            res.Volumes = volumes.Count;
            var gameObjects = new Dictionary<long, (int layer, bool active, long transform)>();
            foreach (var v in volumes)
            {
                bool global, readable = true;
                float weight;
                try
                {
                    v.rest.PPtr();               // sharedProfile
                    global = v.rest.Bool();      // isGlobal
                    v.rest.F32();                // blendDistance
                    weight = v.rest.F32();       // weight
                    v.rest.F32();                // priority
                    v.rest.ExpectEnd("PostProcessVolume");
                }
                catch (Hs2SerializedFileException) { res.Unreadable++; continue; }
                int layer = -1;
                bool active = v.go.FileId == 0 && ActiveInHierarchy(f, v.go.PathId, gameObjects, out layer, ref readable);
                if (!readable) { res.Unreadable++; continue; }
                if (global && v.enabled && active && layer == StudioLayer)
                {
                    res.Reaching++;
                    if (weight > 0f) res.ReachingWeighted++;
                }
            }
            return res;
        }

        static bool IsVolumeScript(Hs2BundleFile b, Hs2SerializedFile f, Hs2PPtr script, Dictionary<string, Hs2SerializedFile> others)
        {
            Hs2SerializedFile sf = f;
            if (script.FileId != 0)
            {
                var ext = f.ExternalOf(script);
                if (ext == null) return false;
                string nodeName = ext.FileName;   // archive:/<cab>/<node>: the node of this bundle, if it is one
                if (!others.TryGetValue(nodeName, out sf))
                {
                    sf = null;
                    foreach (var x in b.Nodes)
                        if (string.Equals(x.Path, nodeName, StringComparison.OrdinalIgnoreCase))
                        {
                            var nx = x;
                            sf = Hs2SerializedFile.FromStream(() => b.OpenNode(nx), b.Label + "!" + nx.Path);
                            break;
                        }
                    others[nodeName] = sf;
                }
                if (sf == null) return false;   // a script in another bundle: not known here
            }
            if (!sf.TryFind(script.PathId, out var so) || so.ClassId != Hs2SerializedFile.ClassMonoScript) return false;
            return Hs2SerializedFile.TryReadMonoScript(sf.ReadBytes(so), out string cls, out string ns) && cls == VolumeClass && ns == VolumeNamespace;
        }

        // True when the GameObject and every parent's GameObject are active as saved; its layer out. readable false when a
        // GameObject or Transform has a layout this reader does not know.
        static bool ActiveInHierarchy(Hs2SerializedFile f, long goId, Dictionary<long, (int layer, bool active, long transform)> gos, out int layer, ref bool readable)
        {
            layer = -1;
            bool all = true;
            long id = goId;
            for (int depth = 0; depth < 256 && id != 0; depth++)
            {
                if (!gos.TryGetValue(id, out var g))
                {
                    if (!TryReadGameObject(f, id, out g)) { readable = false; return false; }
                    gos[id] = g;
                }
                if (depth == 0) layer = g.layer;
                if (!g.active) all = false;
                if (g.transform == 0 || !TryReadFather(f, g.transform, out long fatherGo)) { if (g.transform == 0) break; readable = false; return false; }
                id = fatherGo;
            }
            return all;
        }

        // GameObject (Unity 2018 layout): m_Component (count, then one PPtr each), m_Layer, m_Name, m_Tag, m_IsActive (the object
        // ends right after the flag).
        static bool TryReadGameObject(Hs2SerializedFile f, long id, out (int layer, bool active, long transform) g)
        {
            g = default;
            if (!f.TryFind(id, out var o) || o.ClassId != ClassGameObject) return false;
            try
            {
                var r = f.Reader(o);
                int n = r.I32();
                if (n < 0 || n > 100000) return false;
                var comps = new List<Hs2PPtr>(n);
                for (int i = 0; i < n; i++) comps.Add(r.PPtr());
                g.layer = r.I32();
                r.AlignedString();
                r.U16();
                g.active = r.U8() != 0;   // the object's last field: no alignment padding after it in the file
                int end = r.Position;
                if (end != o.Size && ((end + 3) & ~3) != o.Size) return false;
                foreach (var c in comps)
                    if (c.FileId == 0 && f.TryFind(c.PathId, out var co) && (co.ClassId == ClassTransform || co.ClassId == ClassRectTransform)) { g.transform = c.PathId; break; }
                return true;
            }
            catch (Hs2SerializedFileException) { return false; }
        }

        // Transform (Unity 2018 layout): m_GameObject, m_LocalRotation, m_LocalPosition, m_LocalScale, m_Children, m_Father;
        // the father's GameObject out (0 at a root).
        static bool TryReadFather(Hs2SerializedFile f, long transform, out long fatherGo)
        {
            fatherGo = 0;
            if (!f.TryFind(transform, out var o)) return false;
            try
            {
                var r = f.Reader(o);
                r.PPtr();
                r.Skip(4 * 10);
                int n = r.I32();
                if (n < 0 || n > 1000000) return false;
                r.Skip((long)n * 12);
                var father = r.PPtr();
                if (father.PathId == 0) return true;
                if (father.FileId != 0 || !f.TryFind(father.PathId, out var fo)) return false;
                var fr = f.Reader(fo);
                var go = fr.PPtr();
                if (go.FileId != 0) return false;
                fatherGo = go.PathId;
                return true;
            }
            catch (Hs2SerializedFileException) { return false; }
        }
    }
}
