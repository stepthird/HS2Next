// Chara to Item: the prototype cache (V15). One per scene runtime, added at its first bake (a
// scene without bakes has no part). Prototypes are keyed by the main blob's hash: one build, then a copy per item. A bake made in this
// session seeds its prototype from the capture; a bake read from the scene's res/ (or the store, by its id) is built from its blobs.
// The items using a prototype are counted; at a new scene or a load the prototypes no item of the new scene uses are released after
// it is realized (by content, never by number). The runtime's disposal releases everything.
// Textures are shared by content: one Texture2D per texture blob and sampler state in the runtime, whichever prototypes
// (two bakes of one character, a source and its pieces) and captures name it; each holds a reference, the last one released destroys it.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Bake
{
    public sealed class BakeCache : ISceneRuntimePart, IDisposable
    {
        SceneRuntime rt;
        GameObject container;
        readonly Dictionary<string, BakePrototype> byMain = new Dictionary<string, BakePrototype>(StringComparer.Ordinal);
        readonly Dictionary<ItemRealization, BakePrototype> users = new Dictionary<ItemRealization, BakePrototype>();
        bool releasePending;

        /// <summary>The store of the runtime's UserData once opened (BakeProvider.StoreOf), and that folder.</summary>
        public BakeStore Store;
        public string StoreUserData;

        /// <summary>Prototypes built from blobs or a capture (tests: one build for two items).</summary>
        public int Builds { get; private set; }
        /// <summary>Prototypes released.</summary>
        public int Releases { get; private set; }
        /// <summary>Prototypes built from blobs (a load), and of them the pieces made again from their cut.</summary>
        public int Loads { get; internal set; }
        public int Recomputed { get; internal set; }
        public int Count => byMain.Count;
        public IEnumerable<BakePrototype> Prototypes => byMain.Values;

        /// <summary>
        /// Milliseconds spent in this runtime per stage (tests; the time split of a cut): capture (main thread, its readback and its textures
        /// built included), capture.upload (of it, the capture's textures built), encode.wait (of it, waiting on the encoders), encode (texture
        /// blobs deflated and hashed on workers, summed; overlaps the capture), finish (meshes and the main encoded), build (prototypes built,
        /// their blob reading and texture building included), inflate.textures and inflate.meshes (of it, the main thread's time to have each
        /// blob inflated: waiting for its worker, or inflating it there), upload (of it, textures built from blobs), inflate.wait (of the two,
        /// waiting for the workers), inflate.workers (the blobs inflated on workers, summed; overlaps the load).
        /// </summary>
        public readonly Dictionary<string, double> Ms = new Dictionary<string, double>(StringComparer.Ordinal);
        public void AddMs(string k, double ms) { Ms.TryGetValue(k, out var v); Ms[k] = v + ms; }

        /// <summary>The runtime's cache; with <paramref name="create"/> it is added when missing.</summary>
        public static BakeCache Of(SceneRuntime rt, bool create)
        {
            if (rt == null) return null;
            var c = rt.GetPart<BakeCache>();
            if (c == null && create) { c = new BakeCache(); rt.AddPart(c, c.Dispose); }
            return c;
        }

        /// <summary>The bake an item's instance was made from, or null.</summary>
        public static BakeData BakeOf(ItemRealization it) => PrototypeOf(it)?.Bake;

        /// <summary>The prototype an item's instance was made from, or null.</summary>
        public static BakePrototype PrototypeOf(ItemRealization it)
        {
            var c = it?.Runtime?.GetPart<BakeCache>();
            return c != null && c.users.TryGetValue(it, out var p) ? p : null;
        }

        /// <summary>The inactive container of the prototypes (under the runtime's root).</summary>
        public Transform Container
        {
            get
            {
                if (!container)
                {
                    container = new GameObject("BakePrototypes");
                    container.SetActive(false);
                    container.transform.SetParent(rt != null && rt.Root ? rt.Root.transform : null, false);
                }
                return container.transform;
            }
        }

        public BakePrototype Get(string main) => main != null && byMain.TryGetValue(main, out var p) && p.Alive ? p : null;

        /// <summary>Records a prototype under its main hash (a capture's, once encoded); an existing one with that hash is kept and returned.</summary>
        public BakePrototype Put(string main, BakePrototype p)
        {
            if (main == null || p == null) return p;
            if (byMain.TryGetValue(main, out var old) && old.Alive && old != p) { if (p.Users == 0) p.Destroy(); return old; }
            p.Main = main;
            byMain[main] = p;
            return p;
        }

        /// <summary>Builds a prototype under the container and records it (null main: not recorded until <see cref="Put"/>).</summary>
        public BakePrototype Build(string main, BakeData b, Func<int, MeshData> meshOf, Func<int, Texture> texOf, bool ownTextures)
        {
            var p = BakeBuild.Prototype(b, meshOf, texOf, Container, ownTextures);
            Builds++;
            AddMs("build", p.BuildMs);
            if (main != null) p = Put(main, p);
            return p;
        }

        /// <summary>
        /// Builds a prototype whose textures are the runtime's shared ones: entry i by its key (<paramref name="keyOf"/>), made by
        /// <paramref name="texOf"/> only when no prototype or capture of the runtime holds that key; an entry without a key is made and owned
        /// by the prototype. The prototype holds one reference per entry until it is destroyed. Recorded under <paramref name="main"/>.
        /// </summary>
        public BakePrototype Build(string main, BakeData b, Func<int, MeshData> meshOf, Func<int, string> keyOf, Func<int, Texture> texOf)
        {
            var keys = new List<string>();
            var own = new List<Texture>();
            BakePrototype p;
            try
            {
                p = BakeBuild.Prototype(b, meshOf, i =>
                {
                    string k = keyOf(i);
                    if (k == null)
                    {
                        var t = texOf(i);
                        if (t) own.Add(t);
                        return t;
                    }
                    var s = AcquireTexture(k, () => texOf(i));
                    if (s) keys.Add(k);
                    return s;
                }, Container, false);
            }
            catch
            {
                foreach (var k in keys) ReleaseTexture(k);
                foreach (var t in own) if (t) Object.DestroyImmediate(t);
                throw;
            }
            foreach (var t in own) p.Owned.Add(t);
            p.SharedIn = this;
            p.TextureKeys.AddRange(keys);
            Builds++;
            AddMs("build", p.BuildMs);
            if (main != null) p = Put(main, p);
            return p;
        }

        // ------------------------------------------------------------ textures shared by blob

        sealed class SharedTexture
        {
            public Texture Tex;
            public int Refs;
        }

        readonly Dictionary<string, SharedTexture> textures = new Dictionary<string, SharedTexture>(StringComparer.Ordinal);

        /// <summary>Textures taken into the table (built from a blob or a capture), handed out again from it, and destroyed by their last release.</summary>
        public int TexturesMade { get; private set; }
        public int TexturesShared { get; private set; }
        public int TexturesReleased { get; private set; }
        /// <summary>Textures held now: one per texture blob and sampler state.</summary>
        public int TexturesHeld => textures.Count;
        public IEnumerable<Texture> HeldTextures
        {
            get { foreach (var s in textures.Values) if (s.Tex) yield return s.Tex; }
        }
        /// <summary>Video memory of the textures held, computed from their formats and sizes.</summary>
        public long TextureVideoBytes
        {
            get
            {
                long n = 0;
                foreach (var s in textures.Values) n += BakeTextures.VideoBytes(s.Tex);
                return n;
            }
        }

        /// <summary>The key of a built texture: its blob's hash and the sampler state its entry gives it (a blob sampled two ways is two
        /// textures); null without a blob.</summary>
        public static string TextureKey(string blob, BakeData.TexRef r)
        {
            if (string.IsNullOrEmpty(blob)) return null;
            if (r == null) return blob;
            return blob + "/" + r.WrapU + "," + r.WrapV + "," + r.Filter + "," + r.Aniso + "," + r.Bias.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Whether the table holds a texture under <paramref name="key"/>.</summary>
        public bool HoldsTexture(string key) => key != null && textures.TryGetValue(key, out var s) && s.Tex;

        /// <summary>The blobs (SHA-256) of the textures the table holds, whatever their sampler state.</summary>
        public HashSet<string> HeldBlobs()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in textures)
            {
                if (!kv.Value.Tex) continue;
                int cut = kv.Key.IndexOf('/');
                set.Add(cut < 0 ? kv.Key : kv.Key.Substring(0, cut));
            }
            return set;
        }

        /// <summary>The texture of a key, made by <paramref name="make"/> when the table holds none; the caller holds one reference until
        /// <see cref="ReleaseTexture"/>. Null (nothing held) when it cannot be made.</summary>
        public Texture AcquireTexture(string key, Func<Texture> make)
        {
            if (textures.TryGetValue(key, out var s))
            {
                if (s.Tex) { s.Refs++; TexturesShared++; return s.Tex; }
                textures.Remove(key);
            }
            var t = make();
            if (!t) return null;
            textures[key] = new SharedTexture { Tex = t, Refs = 1 };
            TexturesMade++;
            return t;
        }

        /// <summary>A texture already built (a capture's) under its key: the table's own when it holds one (the given one is destroyed), else
        /// the given one taken in. The caller holds one reference.</summary>
        public Texture AdoptTexture(string key, Texture t)
        {
            if (textures.TryGetValue(key, out var s) && s.Tex)
            {
                if (t && t != s.Tex) Object.DestroyImmediate(t);
                s.Refs++;
                TexturesShared++;
                return s.Tex;
            }
            if (!t) return null;
            textures[key] = new SharedTexture { Tex = t, Refs = 1 };
            TexturesMade++;
            return t;
        }

        /// <summary>One reference to a key's texture fewer; the last one destroys it.</summary>
        public void ReleaseTexture(string key)
        {
            if (key == null || !textures.TryGetValue(key, out var s)) return;
            if (--s.Refs > 0) return;
            textures.Remove(key);
            if (s.Tex) Object.DestroyImmediate(s.Tex);
            TexturesReleased++;
        }

        /// <summary>An item's instance of a prototype under its holder; the item counts as a user until <see cref="Release"/>.</summary>
        public GameObject Instantiate(ItemRealization it, BakePrototype p)
        {
            Release(it);
            var go = BakeBuild.Instantiate(p, it.Holder);
            p.Users++;
            users[it] = p;
            return go;
        }

        /// <summary>The item no longer uses its prototype (its content is released).</summary>
        public void Release(ItemRealization it)
        {
            if (it == null || !users.TryGetValue(it, out var p)) return;
            users.Remove(it);
            p.Users = Math.Max(0, p.Users - 1);
        }

        /// <summary>Releases every prototype no item uses; returns how many.</summary>
        public int ReleaseUnused()
        {
            var drop = new List<string>();
            foreach (var kv in byMain) if (kv.Value.Users <= 0 || !kv.Value.Alive) drop.Add(kv.Key);
            foreach (var k in drop) { byMain[k].Destroy(); byMain.Remove(k); Releases++; }
            return drop.Count;
        }

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r) { rt = r; }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply()
        {
            if (!releasePending) return;
            releasePending = false;
            ReleaseUnused();
        }
        public void LateApply() { }
        /// <summary>A new scene or a load: the prototypes the new scene does not use go after it is realized.</summary>
        public void Clear() { releasePending = true; }
        public void OnLoadedRealized() { releasePending = true; }

        public void Dispose()
        {
            foreach (var p in byMain.Values) p.Destroy();
            byMain.Clear();
            users.Clear();
            // what captures not yet disposed still hold
            foreach (var s in textures.Values) if (s.Tex) Object.DestroyImmediate(s.Tex);
            textures.Clear();
            if (container) Object.DestroyImmediate(container);
            container = null;
        }
    }
}
