// Chara to Item: making a bake and drawing an item from one.
//   BakeJob      a capture on the main thread; each texture is built for the prototype as soon as it is read back and encoded on a worker
//                (deflate, SHA-256), its texels then dropped, so at most a bounded amount of texels is held (past it the capture waits until
//                whichever encoder ends first has ended); meshes are encoded after the capture and the main last (it names every data blob).
//                The prototype is built from the capture in memory (the data the blobs hold) and recorded under the main's hash. Once
//                encoded, a capture of a runtime's character or item puts its textures into the runtime's table by blob and sampler state
//                (BakeCache), so the prototypes built after it (its own, a cut's pieces) take them instead of reading and building their
//                blobs again; the job holds them until it is disposed.
//   BakeBlobs    the bake in memory against the format's classes (BakeMain, BakeMeshData, BakeTextureData)
//   BakeProvider the item source of a node with a bake block: the prototype from the cache, else built from the blobs the
//                scene's res/ holds (the store supplies missing ones by the bake's id); none: the fallback item with bake.missing, the
//                blocks kept. The blobs are inflated on workers (BakeInflate), the textures from the moment the block names them, while
//                the main is read; the prototype is built on the main thread in the order of the main, each blob taken there. A store row
//                added as an item: its blobs go into the scene's res/ and the node gets its bake block.
//                The item's FK list is the main's; after the item path has run the members are added to the mosaic set.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace HS2Next.Scenes.Bake
{
    /// <summary>One bake being made: its capture, the textures built from it, its blobs, its prototype.</summary>
    public sealed class BakeJob : IBakeTextureSink
    {
        /// <summary>Texels handed to the encoders and not yet encoded, at most (bytes); past it the capture waits until whichever encoder ends
        /// first has ended.</summary>
        public static long MaxInFlight = 384L << 20;

        /// <summary>
        /// Tests only (the identity check against the old path; not saved, not an option): a capture reads each texture back one level at a time
        /// and, past <see cref="LegacyMaxInFlight"/>, waits for the oldest encoder; a load inflates each blob on the main thread inside the build.
        /// </summary>
        internal static bool Legacy;
        /// <summary>The old path's limit of texels in flight (bytes).</summary>
        internal const long LegacyMaxInFlight = 256L << 20;

        public BakeCaptureResult Capture;
        public BakePrototype Prototype;
        /// <summary>The textures built for the prototype, by index (owned by the prototype once it is built, or, once shared, by the runtime's
        /// table: an index whose blob the table already held names the table's texture).</summary>
        public readonly List<Texture> Textures = new List<Texture>();
        /// <summary>Tests: keep every texture's data (BD3 compares them); never in the app.</summary>
        public bool KeepTextureData;
        public readonly List<TexData> TextureData = new List<TexData>();
        /// <summary>The largest texture handed over (bytes of texels), the sum of all, the most held by the encoders at once.</summary>
        public long LargestTexture, TextureBytes, PeakInFlight;
        public readonly Dictionary<string, double> Ms = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <summary>The encoded bake (after <see cref="Finish"/>): the main, its blob and hash, every data blob by hash.</summary>
        public BakeMain Main;
        public byte[] MainBlob;
        public string MainSha;
        public readonly Dictionary<string, byte[]> Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>The mesh blobs' data in the main's order (the Cutter reads them).</summary>
        public readonly List<BakeMeshData> MeshData = new List<BakeMeshData>();

        readonly List<Task<(string sha, byte[] blob)>> textureTasks = new List<Task<(string, byte[])>>();
        // the encoders not yet taken out of the texels in flight, oldest first
        readonly List<(Task task, long bytes)> inFlight = new List<(Task, long)>();
        long inFlightBytes;
        long encodeTicks;
        // the runtime whose table takes the textures (a capture of its character or item), the thread that made the job, the keys held
        BakeCache shareIn;
        bool shared;
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        readonly List<string> heldKeys = new List<string>();
        readonly HashSet<int> sharedIndex = new HashSet<int>();

        /// <summary>The bytes of every blob (main and data), compressed.</summary>
        public long Bytes
        {
            get
            {
                long n = MainBlob?.LongLength ?? 0;
                foreach (var b in Blobs.Values) n += b.LongLength;
                return n;
            }
        }

        public void Texture(BakeData bake, int index, TexData data)
        {
            var sw = Stopwatch.StartNew();
            while (Textures.Count <= index) Textures.Add(null);
            var t = BakeTextures.Build(data, bake.Textures[index]);
            Textures[index] = t;
            bake.Textures[index].Built = t;
            long n = data.Bytes;
            TextureBytes += n;
            if (n > LargestTexture) LargestTexture = n;
            if (KeepTextureData) { while (TextureData.Count <= index) TextureData.Add(null); TextureData[index] = data; }
            Add("textureBuild", sw.Elapsed.TotalMilliseconds);
            // encode on a worker; while too many texels are in flight wait for whichever encoder ends first (the old path: the oldest)
            sw.Restart();
            if (Legacy)
            {
                while (inFlight.Count > 0 && inFlightBytes + n > LegacyMaxInFlight)
                {
                    var (task, bytes) = inFlight[0];
                    inFlight.RemoveAt(0);
                    task.Wait();
                    inFlightBytes -= bytes;
                }
            }
            else
            {
                DropEncoded();
                while (inFlight.Count > 0 && inFlightBytes + n > MaxInFlight)
                {
                    var running = new Task[inFlight.Count];
                    for (int k = 0; k < running.Length; k++) running[k] = inFlight[k].task;
                    Task.WaitAny(running);
                    DropEncoded();
                }
            }
            Add("encodeWait", sw.Elapsed.TotalMilliseconds);
            string fmt = data.Format.ToString(), src = data.SourceFormat ?? "";
            int w = data.W, hh = data.H;
            var levels = data.Levels;
            var job = Task.Run(() =>
            {
                var esw = Stopwatch.StartNew();
                var blob = BakeTextureData.Encode(w, hh, fmt, src, levels);
                var sha = BakeBlob.Sha256(blob);
                Interlocked.Add(ref encodeTicks, esw.ElapsedTicks);
                return (sha, blob);
            });
            while (textureTasks.Count <= index) textureTasks.Add(null);
            textureTasks[index] = job;
            inFlight.Add((job, n));
            inFlightBytes += n;
            if (inFlightBytes > PeakInFlight) PeakInFlight = inFlightBytes;
        }

        /// <summary>Takes the encoders that have ended out of the texels in flight; one that failed throws as a wait on it does.</summary>
        void DropEncoded()
        {
            for (int k = 0; k < inFlight.Count;)
            {
                var (task, bytes) = inFlight[k];
                if (!task.IsCompleted) { k++; continue; }
                inFlight.RemoveAt(k);
                inFlightBytes -= bytes;
                if (task.IsFaulted || task.IsCanceled) task.Wait();
            }
        }

        static (string, byte[]) Encode(BakeBlobKind kind, long length, Action<System.IO.Stream> body)
        {
            var blob = BakeBlob.Write(kind, length, body);
            return (BakeBlob.Sha256(blob), blob);
        }

        void Add(string k, double ms) { Ms.TryGetValue(k, out var v); Ms[k] = v + ms; }

        /// <summary>Captures a character (main thread).</summary>
        public static BakeJob OfChara(CharaRealization c, BakeOptions o)
        {
            var j = new BakeJob { shareIn = BakeCache.Of(c?.Runtime, true) };
            j.Capture = BakeCapture.Chara(c, o, j);
            return j;
        }

        /// <summary>Captures a built item (main thread).</summary>
        public static BakeJob OfItem(ItemRealization it)
        {
            var j = new BakeJob { shareIn = BakeCache.Of(it?.Runtime, true) };
            j.Capture = BakeCapture.Item(it, j);
            return j;
        }

        /// <summary>A bake made as data (its meshes in BakeData.Meshes[i].Data, its textures given in order): the textures are built and
        /// encoded as a capture's would be; call <see cref="Finish"/> after. Main thread (textures are made). A mesh with no data or a texture
        /// given as null keeps the blob its entry names (a piece's untouched mesh or texture: the blob is already in the scene); such a job
        /// has no prototype of its own (the item builds from the scene's blobs).</summary>
        public static BakeJob OfData(BakeData b, IReadOnlyList<TexData> textures)
        {
            var j = new BakeJob { Capture = new BakeCaptureResult { Bake = b } };
            for (int i = 0; i < b.Textures.Count && textures != null && i < textures.Count; i++)
                if (textures[i] != null) j.Texture(b, i, textures[i]);
            return j;
        }

        /// <summary>Captures a prop root (main thread).</summary>
        public static BakeJob OfProp(Transform root, IReadOnlyList<string> fk, string kind)
        {
            var j = new BakeJob();
            j.Capture = BakeCapture.Prop(root, fk, kind, j);
            return j;
        }

        /// <summary>
        /// Encodes the meshes and the main once every texture is encoded: the mesh blobs on workers, then the main naming every data blob.
        /// Call from the main thread (it waits) or from a worker; the capture's Unity objects are not touched from a worker. On the thread
        /// that made it, a capture of a runtime's character or item then shares its textures (<see cref="Share"/>).
        /// </summary>
        public void Finish()
        {
            var sw = Stopwatch.StartNew();
            var b = Capture.Bake;
            MeshData.Clear();
            var meshTasks = new List<Task<(string, byte[])>>();
            foreach (var m in b.Meshes)
            {
                // a mesh given by its blob alone (a piece's untouched mesh) keeps that blob
                if (m.Data == null) { MeshData.Add(null); meshTasks.Add(null); continue; }
                var d = BakeBlobs.ToMesh(m.Data);
                MeshData.Add(d);
                meshTasks.Add(Task.Run(() => Encode(BakeBlobKind.Mesh, d.BodyLength(), d.WriteBody)));
            }
            for (int i = 0; i < meshTasks.Count; i++)
            {
                if (meshTasks[i] == null) continue;
                var (sha, blob) = meshTasks[i].Result;
                b.Meshes[i].Blob = sha;
                Blobs[sha] = blob;
            }
            for (int i = 0; i < b.Textures.Count; i++)
            {
                // a texture given by its blob alone keeps that blob
                var t = i < textureTasks.Count ? textureTasks[i] : null;
                if (t == null) continue;
                var (sha, blob) = t.Result;
                b.Textures[i].Blob = sha;
                Blobs[sha] = blob;
            }
            textureTasks.Clear();
            inFlight.Clear();
            inFlightBytes = 0;
            Main = BakeBlobs.ToMain(b);
            MainBlob = Main.ToBlob();
            MainSha = BakeBlob.Sha256(MainBlob);
            Add("finish", sw.Elapsed.TotalMilliseconds);
            if (shareIn != null && Thread.CurrentThread.ManagedThreadId == ownerThread) Share(shareIn);
        }

        /// <summary>
        /// Puts the built textures into a runtime's table by blob and sampler state (main thread, after <see cref="Finish"/>; once): an index
        /// whose key the table already holds takes the table's texture and its own is destroyed. The job holds one reference per index until
        /// <see cref="Dispose"/>. The job's times are added to the cache's (<see cref="BakeCache.Ms"/>).
        /// </summary>
        public void Share(BakeCache cache)
        {
            if (shared || cache == null || Main == null || Capture?.Bake == null) return;
            shared = true;
            shareIn = cache;
            var b = Capture.Bake;
            for (int i = 0; i < Textures.Count && i < b.Textures.Count; i++)
            {
                var t = Textures[i];
                string key = t ? BakeCache.TextureKey(b.Textures[i].Blob, b.Textures[i]) : null;
                if (key == null) continue;
                var s = cache.AdoptTexture(key, t);
                if (!s) continue;
                heldKeys.Add(key);
                sharedIndex.Add(i);
                Textures[i] = s;
                b.Textures[i].Built = s;
            }
            if (Capture.Ms.TryGetValue("capture", out var cm)) cache.AddMs("capture", cm);
            foreach (var (from, to) in new[] { ("textureBuild", "capture.upload"), ("encodeWait", "encode.wait"), ("finish", "finish") })
                if (Ms.TryGetValue(from, out var v)) cache.AddMs(to, v);
            cache.AddMs("encode", Interlocked.Read(ref encodeTicks) * 1000.0 / Stopwatch.Frequency);
        }

        /// <summary>The scene block naming this bake (store row <paramref name="no"/>, or none).</summary>
        public ItemBake Block(int? no = null) => Main.ToItemBake(MainSha, no);

        /// <summary>Adds every blob to a scene's res/; returns the main's entry name.</summary>
        public string AddTo(ResStore res)
        {
            foreach (var kv in Blobs) res.AddBlob(kv.Value);
            return res.AddBlob(MainBlob);
        }

        /// <summary>A store row for this bake (number and size set by the store).</summary>
        public BakeStoreRow Row(string name)
        {
            var b = Capture.Bake;
            var row = new BakeStoreRow { Id = b.Id, Name = name ?? "", Created = b.Created, Kind = b.Kind, Main = MainSha, Parts = b.Parts == null ? "" : string.Join(",", b.Parts) };
            row.Data.AddRange(Main.DataBlobs());
            row.Fk.AddRange(b.Fk);
            return row;
        }

        /// <summary>The blobs of this bake by hash, the main included (what a store row takes).</summary>
        public IEnumerable<KeyValuePair<string, byte[]>> AllBlobs()
        {
            yield return new KeyValuePair<string, byte[]>(MainSha, MainBlob);
            foreach (var kv in Blobs) yield return kv;
        }

        /// <summary>Builds the prototype from the capture under the runtime's cache (it owns the textures); after <see cref="Finish"/> it
        /// is recorded under the main's hash, so the item that names it uses it.</summary>
        public BakePrototype Build(BakeCache cache)
        {
            var b = Capture.Bake;
            Share(cache);
            if (shared && shareIn == cache)
                Prototype = cache.Build(MainSha, b, i => b.Meshes[i].Data,
                    i => i < Textures.Count && Textures[i] ? BakeCache.TextureKey(b.Textures[i].Blob, b.Textures[i]) : null,
                    i => i < Textures.Count ? Textures[i] : null);
            else
                Prototype = cache.Build(MainSha, b, i => b.Meshes[i].Data, i => i < Textures.Count ? Textures[i] : null, true);
            return Prototype;
        }

        /// <summary>Builds the prototype under a container (tests, before encoding; after it, the shared textures stay the job's).</summary>
        public BakePrototype Build(Transform container)
        {
            var b = Capture.Bake;
            Prototype = BakeBuild.Prototype(b, i => b.Meshes[i].Data, i => i < Textures.Count ? Textures[i] : null, container, !shared);
            return Prototype;
        }

        /// <summary>Destroys what the job made and nobody took (its textures when no prototype owns them, an unrecorded prototype) and releases
        /// the shared textures it holds.</summary>
        public void Dispose()
        {
            foreach (var t in textureTasks) try { t?.Wait(); } catch (Exception) { }
            textureTasks.Clear();
            if (Prototype != null && Prototype.Users == 0 && Prototype.Main == null) Prototype.Destroy();
            else if (Prototype == null)
            {
                for (int i = 0; i < Textures.Count; i++) if (Textures[i] && !sharedIndex.Contains(i)) UnityEngine.Object.DestroyImmediate(Textures[i]);
            }
            if (shareIn != null) foreach (var k in heldKeys) shareIn.ReleaseTexture(k);
            heldKeys.Clear();
            sharedIndex.Clear();
            Textures.Clear();
            TextureData.Clear();
        }
    }

    /// <summary>The bake in memory against the format's classes.</summary>
    public static class BakeBlobs
    {
        static Vec3 V3(Vector3 v) => new Vec3(v.x, v.y, v.z);
        static Vector3 V3(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        static Vec4 V4(Vector4 v) => new Vec4(v.x, v.y, v.z, v.w);
        static Vector4 V4(Vec4 v) => new Vector4(v.X, v.Y, v.Z, v.W);

        public static BakeMain ToMain(BakeData b)
        {
            var m = new BakeMain
            {
                Id = b.Id, Kind = b.Kind, Created = b.Created, Origin = b.Origin,
                Source = new BakeMainSource
                {
                    Kind = b.Source.Kind, Name = b.Source.Name ?? "", Sex = b.Source.Sex >= 0 ? b.Source.Sex : (int?)null,
                    Ref = b.Source.Ref, Group = b.Source.Ref != null ? b.Source.Group : (int?)null, Category = b.Source.Ref != null ? b.Source.Category : (int?)null,
                },
                Parts = b.Parts == null ? null : new List<string>(b.Parts),
                Correction = b.Correction == null ? null : (bool[])b.Correction.Clone(),
                Insides = new BakeMainInsides { Fill = b.Insides.Fill, Section = V3(b.Insides.Section), OwnInside = b.Insides.OwnInside },
            };
            foreach (var n in b.Nodes)
                m.Nodes.Add(new BakeMainNode { Name = n.Name, Parent = n.Parent, P = V3(n.P), R = new Quat4(n.R.x, n.R.y, n.R.z, n.R.w), S = V3(n.S), Active = n.Active, Layer = n.Layer });
            m.Fk.AddRange(b.Fk);
            foreach (var x in b.Meshes) m.Meshes.Add(new BakeMainMesh { Blob = x.Blob, Name = x.Name ?? "" });
            foreach (var t in b.Textures)
                m.Textures.Add(new BakeMainTexture { Blob = t.Blob, Name = t.Name ?? "", Wrap = new[] { t.WrapU, t.WrapV }, Filter = t.Filter, Aniso = t.Aniso, Bias = t.Bias });
            foreach (var x in b.Materials)
            {
                var mm = new BakeMainMaterial
                {
                    Name = x.Name ?? "", Shader = x.Shader ?? "", Queue = x.Queue, Keywords = new List<string>(x.Keywords ?? Array.Empty<string>()),
                    PassesOff = new List<string>(x.PassesOff ?? Array.Empty<string>()), RenderType = x.RenderType ?? "",
                    Inside = x.Inside is Vector3 iv ? new[] { iv.x, iv.y, iv.z } : null,
                };
                foreach (var p in x.Props)
                {
                    BakeMainProp v;
                    switch (p.Type)
                    {
                        case 'f': v = BakeMainProp.Float(p.F); break;
                        case 'i': v = BakeMainProp.Integer(p.I); break;
                        case 'c': v = BakeMainProp.Color(V4(p.V)); break;
                        case 'v': v = BakeMainProp.Vector(V4(p.V)); break;
                        case 't': v = BakeMainProp.Texture(p.Tex, V4(p.St)); break;
                        default: continue;
                    }
                    mm.Props[p.Name] = v;
                }
                m.Materials.Add(mm);
            }
            foreach (var r in b.Renderers)
                m.Renderers.Add(new BakeMainRenderer
                {
                    Node = r.Node, Skinned = r.Skinned, Mesh = r.Mesh, Materials = (int[])r.Materials.Clone(), Bones = (int[])r.Bones.Clone(), Root = r.Root,
                    Bounds = new[] { r.Bounds.center.x, r.Bounds.center.y, r.Bounds.center.z, r.Bounds.size.x, r.Bounds.size.y, r.Bounds.size.z },
                    Shadow = r.Shadow, Receive = r.Receive, LightProbe = r.LightProbe, ReflProbe = r.ReflProbe, Anchor = r.Anchor,
                    UpdateOffscreen = r.UpdateOffscreen, Quality = r.Quality, Part = r.Part ?? "", Member = r.Member, Inside = r.Inside ?? BakeData.InsideNone,
                });
            return m;
        }

        public static BakeData FromMain(BakeMain m)
        {
            var b = new BakeData
            {
                Id = m.Id, Kind = m.Kind, Created = m.Created, Origin = m.Origin, Unknown = m,
                Source = new BakeData.SourceInfo { Kind = m.Source?.Kind, Name = m.Source?.Name ?? "", Sex = m.Source?.Sex ?? -1, Ref = m.Source?.Ref, Group = m.Source?.Group ?? 0, Category = m.Source?.Category ?? 0 },
                Parts = m.Parts == null ? null : new List<string>(m.Parts),
                Correction = m.Correction == null ? null : (bool[])m.Correction.Clone(),
                Insides = new BakeData.InsidesData { Fill = m.Insides.Fill, Section = V3(m.Insides.Section), OwnInside = m.Insides.OwnInside },
            };
            foreach (var n in m.Nodes)
                b.Nodes.Add(new BakeData.NodeData { Name = n.Name, Parent = n.Parent, P = V3(n.P), R = new Quaternion(n.R.X, n.R.Y, n.R.Z, n.R.W), S = V3(n.S), Active = n.Active, Layer = n.Layer });
            b.Fk.AddRange(m.Fk);
            foreach (var x in m.Meshes) b.Meshes.Add(new BakeData.MeshRef { Blob = x.Blob, Name = x.Name });
            foreach (var t in m.Textures)
                b.Textures.Add(new BakeData.TexRef { Blob = t.Blob, Name = t.Name, WrapU = t.Wrap.Length > 0 ? t.Wrap[0] : 0, WrapV = t.Wrap.Length > 1 ? t.Wrap[1] : 0, Filter = t.Filter, Aniso = t.Aniso, Bias = t.Bias });
            foreach (var x in m.Materials)
            {
                var d = new BakeData.MatData
                {
                    Name = x.Name, Shader = x.Shader, Queue = x.Queue, Keywords = x.Keywords.ToArray(), PassesOff = x.PassesOff.ToArray(), RenderType = x.RenderType,
                    Inside = x.Inside != null && x.Inside.Length >= 3 ? new Vector3(x.Inside[0], x.Inside[1], x.Inside[2]) : (Vector3?)null,
                };
                foreach (var kv in x.Props)
                {
                    var v = kv.Value;
                    if (v == null) continue;
                    var p = new BakeData.PropData { Name = kv.Key };
                    switch (v.Type)
                    {
                        case BakePropType.Float: p.Type = 'f'; p.F = v.F; break;
                        case BakePropType.Int: p.Type = 'i'; p.I = v.I; break;
                        case BakePropType.Color: p.Type = 'c'; p.V = V4(v.V); break;
                        case BakePropType.Vector: p.Type = 'v'; p.V = V4(v.V); break;
                        case BakePropType.Texture: p.Type = 't'; p.Tex = v.T; p.St = V4(v.St); break;
                        default: continue;
                    }
                    d.Props.Add(p);
                }
                b.Materials.Add(d);
            }
            foreach (var r in m.Renderers)
            {
                var bb = r.Bounds ?? new float[6];
                b.Renderers.Add(new BakeData.RendData
                {
                    Node = r.Node, Skinned = r.Skinned, Mesh = r.Mesh, Materials = (int[])r.Materials.Clone(), Bones = (int[])r.Bones.Clone(), Root = r.Root,
                    Bounds = new Bounds(new Vector3(bb[0], bb[1], bb[2]), new Vector3(bb[3], bb[4], bb[5])),
                    Shadow = r.Shadow, Receive = r.Receive, LightProbe = r.LightProbe, ReflProbe = r.ReflProbe, Anchor = r.Anchor,
                    UpdateOffscreen = r.UpdateOffscreen, Quality = r.Quality, Part = r.Part, Member = r.Member, Inside = r.Inside,
                });
            }
            return b;
        }

        // ------------------------------------------------------------ meshes

        public static BakeMeshData ToMesh(MeshData md)
        {
            int n = md.V.Length;
            var d = new BakeMeshData { VertexCount = n, Positions = Floats(md.V) };
            if (md.N != null) d.Normals = Floats(md.N);
            if (md.T != null) d.Tangents = Floats(md.T, 4);
            for (int ch = 0; ch < 8; ch++)
            {
                var uv = md.Uv[ch];
                if (uv == null) continue;
                int dim = Math.Max(1, Math.Min(4, md.UvDim[ch]));
                var a = new float[n * dim];
                for (int k = 0; k < n; k++) for (int c = 0; c < dim; c++) a[k * dim + c] = uv[k][c];
                d.SetUv(ch, dim, a);
            }
            if (md.C != null)
            {
                var c = new byte[n * 4];
                for (int k = 0; k < n; k++) { c[4 * k] = md.C[k].r; c[4 * k + 1] = md.C[k].g; c[4 * k + 2] = md.C[k].b; c[4 * k + 3] = md.C[k].a; }
                d.Colors = c;
            }
            if (md.W != null)
            {
                d.BoneIndices = new ushort[n * 4];
                d.BoneWeights = new float[n * 4];
                for (int k = 0; k < n; k++)
                {
                    var w = md.W[k];
                    d.BoneIndices[4 * k] = (ushort)w.boneIndex0; d.BoneWeights[4 * k] = w.weight0;
                    d.BoneIndices[4 * k + 1] = (ushort)w.boneIndex1; d.BoneWeights[4 * k + 1] = w.weight1;
                    d.BoneIndices[4 * k + 2] = (ushort)w.boneIndex2; d.BoneWeights[4 * k + 2] = w.weight2;
                    d.BoneIndices[4 * k + 3] = (ushort)w.boneIndex3; d.BoneWeights[4 * k + 3] = w.weight3;
                }
            }
            if (md.Bp != null)
            {
                d.BindPoses = new float[md.Bp.Length * 16];
                for (int i = 0; i < md.Bp.Length; i++) for (int e = 0; e < 16; e++) d.BindPoses[16 * i + e] = md.Bp[i][e];
            }
            for (int s = 0; s < md.Idx.Length; s++) d.SubMeshes.Add(new BakeSubMesh { Topology = (byte)md.Topo[s], Indices = (int[])md.Idx[s].Clone() });
            return d;
        }

        public static MeshData FromMesh(BakeMeshData d, string name)
        {
            int n = d.VertexCount;
            var md = new MeshData { Name = name ?? "", V = Vec3s(d.Positions, n) };
            if (d.Normals != null) md.N = Vec3s(d.Normals, n);
            if (d.Tangents != null)
            {
                md.T = new Vector4[n];
                for (int k = 0; k < n; k++) md.T[k] = new Vector4(d.Tangents[4 * k], d.Tangents[4 * k + 1], d.Tangents[4 * k + 2], d.Tangents[4 * k + 3]);
            }
            for (int ch = 0; ch < 8; ch++)
            {
                var a = d.Uv[ch];
                int dim = d.UvDims[ch];
                if (a == null || dim == 0) continue;
                var uv = new Vector4[n];
                for (int k = 0; k < n; k++) { var v = Vector4.zero; for (int c = 0; c < dim; c++) v[c] = a[k * dim + c]; uv[k] = v; }
                md.Uv[ch] = uv;
                md.UvDim[ch] = dim;
            }
            if (d.Colors != null)
            {
                md.C = new Color32[n];
                for (int k = 0; k < n; k++) md.C[k] = new Color32(d.Colors[4 * k], d.Colors[4 * k + 1], d.Colors[4 * k + 2], d.Colors[4 * k + 3]);
            }
            if (d.HasWeights)
            {
                md.W = new BoneWeight[n];
                for (int k = 0; k < n; k++)
                    md.W[k] = new BoneWeight
                    {
                        boneIndex0 = d.BoneIndices[4 * k], weight0 = d.BoneWeights[4 * k], boneIndex1 = d.BoneIndices[4 * k + 1], weight1 = d.BoneWeights[4 * k + 1],
                        boneIndex2 = d.BoneIndices[4 * k + 2], weight2 = d.BoneWeights[4 * k + 2], boneIndex3 = d.BoneIndices[4 * k + 3], weight3 = d.BoneWeights[4 * k + 3],
                    };
            }
            if (d.BindPoseCount > 0)
            {
                md.Bp = new Matrix4x4[d.BindPoseCount];
                for (int i = 0; i < md.Bp.Length; i++) { var m = new Matrix4x4(); for (int e = 0; e < 16; e++) m[e] = d.BindPoses[16 * i + e]; md.Bp[i] = m; }
            }
            md.Idx = new int[d.SubMeshes.Count][];
            md.Topo = new MeshTopology[d.SubMeshes.Count];
            for (int s = 0; s < d.SubMeshes.Count; s++) { md.Idx[s] = d.SubMeshes[s].Indices; md.Topo[s] = (MeshTopology)d.SubMeshes[s].Topology; }
            md.Route = "blob";
            return md;
        }

        static float[] Floats(Vector3[] v)
        {
            var a = new float[v.Length * 3];
            for (int k = 0; k < v.Length; k++) { a[3 * k] = v[k].x; a[3 * k + 1] = v[k].y; a[3 * k + 2] = v[k].z; }
            return a;
        }

        static float[] Floats(Vector4[] v, int dim)
        {
            var a = new float[v.Length * dim];
            for (int k = 0; k < v.Length; k++) for (int c = 0; c < dim; c++) a[dim * k + c] = v[k][c];
            return a;
        }

        static Vector3[] Vec3s(float[] a, int n)
        {
            var v = new Vector3[n];
            for (int k = 0; k < n; k++) v[k] = new Vector3(a[3 * k], a[3 * k + 1], a[3 * k + 2]);
            return v;
        }

        // ------------------------------------------------------------ textures

        public static BakeTextureData ToTexture(TexData t) =>
            new BakeTextureData { Width = t.W, Height = t.H, Format = t.Format.ToString(), SourceFormat = t.SourceFormat ?? "", Levels = new List<byte[]>(t.Levels) };

        /// <summary>Null when the stored format is not one this build knows.</summary>
        public static TexData FromTexture(BakeTextureData d)
        {
            if (!Enum.TryParse(d.Format, out GraphicsFormat f) || f == GraphicsFormat.None) return null;
            var t = new TexData { W = d.Width, H = d.Height, Format = f, SourceFormat = d.SourceFormat };
            t.Levels.AddRange(d.Levels);
            return t;
        }
    }

    /// <summary>The app's source of bake items and of the store's rows.</summary>
    public static class BakeProvider
    {
        /// <summary>Puts the bake source, the store-row completion and the Cutter's markers and recompute in place (idempotent). Called for every
        /// scene runtime.</summary>
        public static void Register()
        {
            ItemInstanceSource.Bake = Source;
            ItemInstanceSource.Adding = Adding;
            HS2Next.Scenes.Cut.CutMarkers.Install();
        }


        /// <summary>Prototypes built from blobs in this process (every runtime), and of them the pieces made again from their cut (tests).</summary>
        public static int Loads => loads;
        public static int Recomputed => recomputed;
        static int loads, recomputed;

        /// <summary>
        /// A piece's result made again from its cut (set by the Cutter): called when the blobs of a node with a cut block do not gather. A set: the
        /// item is built from it (its blobs are in the scene's res/ then); null: the fallback item (the function reported why).
        /// </summary>
        public static Func<ItemCut, ItemBake, ResStore, BakeStore, FindingList, string, BakeBlobSet> Recompute { get; set; }

        /// <summary>The store of a runtime's UserData (opened at its first use; reading creates nothing).</summary>
        public static BakeStore StoreOf(SceneRuntime rt)
        {
            var cache = BakeCache.Of(rt, true);
            string ud = rt?.Services?.UserData;
            if (cache.Store == null || cache.StoreUserData != ud)
            {
                cache.Store = string.IsNullOrEmpty(ud) ? null : BakeStore.ForUserData(ud);
                cache.StoreUserData = ud;
            }
            return cache.Store;
        }

        /// <summary>ItemInstanceSource.Bake: a node with a bake block is drawn from it.</summary>
        static bool Source(ItemRealization it, out ItemInstance made)
        {
            made = default;
            var st = it?.Node?.State as ItemNodeState;
            var bake = st?.Bake;
            if (bake == null) return false;
            var rt = it.Runtime;
            var cache = BakeCache.Of(rt, true);
            var proto = cache.Get(BakeBlob.ShaOf(bake.Main));
            if (proto == null)
            {
                proto = Load(rt, cache, bake, it.NodeId, st.Cut);
                if (proto == null) return true;   // the fallback item; the blocks stay as they are
            }
            var go = cache.Instantiate(it, proto);
            var p = proto;
            made = new ItemInstance
            {
                Object = go,
                Row = RowOf(rt, st, p.Bake),
                Built = () => BakeBuild.AddMembers(p, it.Instance),
                Released = () => cache.Release(it),
            };
            return true;
        }

        /// <summary>The prototype of a bake block built from its blobs (the scene's res/, the store by the bake's id for missing ones); null
        /// with the finding when they cannot be had.</summary>
        public static BakePrototype Load(SceneRuntime rt, BakeCache cache, ItemBake bake, string nodeId, ItemCut cut = null)
        {
            var sw = Stopwatch.StartNew();
            var res = rt.Session?.Model?.File?.Res;
            if (res == null) return null;
            // the textures the block names start to inflate on workers while the main is read (the old path: each inside the build)
            var inflate = BakeJob.Legacy ? null : BakeInflate.Start(bake.AppliedData, res, cache);
            double started = sw.Elapsed.TotalMilliseconds;
            var findings = new FindingList();
            var store = StoreOf(rt);
            var set = BakeBlobSet.Gather(bake, res, store, findings, nodeId);
            double gathered = sw.Elapsed.TotalMilliseconds;
            var again = Recompute;
            if (set.Status != BakeBlobSet.Result.Ok && cut != null && again != null)
            {
                // a piece whose result is missing: made again from its source and steps
                findings = new FindingList();
                set = again(cut, bake, res, store, findings, nodeId);
                cache.Recomputed++;
                recomputed++;
            }
            double recomputedAt = sw.Elapsed.TotalMilliseconds;
            foreach (var f in findings) rt.ReportOnce(f.Severity, f.Code, nodeId, f.Message);
            if (set == null || set.Status != BakeBlobSet.Result.Ok) return null;
            var p = Build(cache, set, nodeId, rt, inflate);
            p.Ms["load"] = sw.Elapsed.TotalMilliseconds;
            p.Ms["load.start"] = started;
            p.Ms["load.gather"] = gathered - started;
            p.Ms["load.recompute"] = recomputedAt - gathered;
            cache.Loads++;
            loads++;
            return p;
        }

        /// <summary>Builds a prototype from a gathered blob set; a texture the runtime's table already holds (by blob and sampler state) is taken
        /// from it, neither read nor built again. The blobs are inflated on workers (<see cref="BakeInflate"/>) and taken on the main thread in the
        /// order of the main, the textures uploaded there in that order.</summary>
        public static BakePrototype Build(BakeCache cache, BakeBlobSet set, string nodeId, SceneRuntime rt) => Build(cache, set, nodeId, rt, null);

        static BakePrototype Build(BakeCache cache, BakeBlobSet set, string nodeId, SceneRuntime rt, BakeInflate inflate)
        {
            var b = BakeBlobs.FromMain(set.Main);
            if (BakeJob.Legacy) inflate = null;
            else
            {
                inflate = inflate ?? new BakeInflate();
                inflate.Plan(b, set, cache);
            }
            double meshMs = 0, texMs = 0, uploadMs = 0;
            var p = cache.Build(set.MainSha, b, i =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var bytes = set.Get(b.Meshes[i].Blob);
                    if (bytes == null) return null;
                    if (inflate != null && inflate.TryTake(BakeBlobKind.Mesh, b.Meshes[i].Blob, b.Meshes[i].Name, out var m)) return (MeshData)m;
                    return BakeBlobs.FromMesh(BakeMeshData.FromBlob(bytes), b.Meshes[i].Name);
                }
                catch (BakeFormatException e) { rt?.ReportOnce(FindingSeverity.Error, BakeFindings.BlobCorrupt, nodeId, "mesh " + b.Meshes[i].Name + ": " + e.Message); return null; }
                finally
                {
                    double ms = sw.Elapsed.TotalMilliseconds;
                    meshMs += ms;
                    cache.AddMs("inflate.meshes", ms);
                }
            }, i => BakeCache.TextureKey(b.Textures[i].Blob, b.Textures[i]), i =>
            {
                var sw = Stopwatch.StartNew();
                TexData td;
                try
                {
                    var bytes = set.Get(b.Textures[i].Blob);
                    if (bytes == null) return null;
                    td = inflate != null && inflate.TryTake(BakeBlobKind.Texture, b.Textures[i].Blob, null, out var v) ? (TexData)v
                        : BakeBlobs.FromTexture(BakeTextureData.FromBlob(bytes));
                }
                catch (BakeFormatException e) { rt?.ReportOnce(FindingSeverity.Error, BakeFindings.BlobCorrupt, nodeId, "texture " + b.Textures[i].Name + ": " + e.Message); return null; }
                finally
                {
                    double ms = sw.Elapsed.TotalMilliseconds;
                    texMs += ms;
                    cache.AddMs("inflate.textures", ms);
                }
                if (td == null) { rt?.ReportOnce(FindingSeverity.Warning, BakeFindings.TextureKind, nodeId, "texture " + b.Textures[i].Name + ": its stored format is unknown"); return null; }
                sw.Restart();
                var t = BakeTextures.Build(td, b.Textures[i]);
                double up = sw.Elapsed.TotalMilliseconds;
                uploadMs += up;
                cache.AddMs("upload", up);
                return t;
            });
            p.Ms["inflate.meshes"] = meshMs;
            p.Ms["inflate.textures"] = texMs;
            p.Ms["upload"] = uploadMs;
            if (inflate != null)
            {
                cache.AddMs("inflate.workers", inflate.WorkerMs);
                cache.AddMs("inflate.wait", inflate.WaitMs);
                p.Ms["inflate.workers"] = inflate.WorkerMs;
                p.Ms["inflate.wait"] = inflate.WaitMs;
                p.Ms["inflate.start"] = inflate.StartMs;
                p.Ms["inflate.planned"] = inflate.Planned;
                p.Ms["inflate.taken"] = inflate.Taken;
            }
            foreach (var f in p.Findings) rt?.ReportOnce(f.Severity, f.Code, nodeId, f.Message);
            return p;
        }

        /// <summary>The row of a bake item: the generated row its reference names, else one made from the main; the FK list is always the main's.</summary>
        static StudioItem RowOf(SceneRuntime rt, ItemNodeState st, BakeData b)
        {
            StudioItem gen = null;
            var db = rt.Services?.Db;
            if (db != null && ContentId.TryParse(st.Ref, out var id) && !id.IsEmpty)
            {
                var r = db.ResolveItem(id, st.Group, st.Category, out bool fb);
                if (!fb) gen = r;
            }
            var row = new StudioItem
            {
                Key = gen?.Key ?? (ContentId.TryParse(st.Ref, out var k) ? k : default),
                Group = st.Group, Category = st.Category, Name = gen?.Name ?? b.Source?.Name ?? "",
                BundlePath = "", FileName = "", Manifest = "",
            };
            row.Bones = b.Fk.ToArray();
            return row;
        }

        /// <summary>ItemInstanceSource.Adding: a store row becomes a node: its blobs go into the scene's res/, the node gets its bake block.</summary>
        static string Adding(SceneRuntime rt, StudioItem row, ItemNodeState state)
        {
            if (row == null || row.Group != AppItemRows.Group || row.Category != AppItemRows.BakesCategory) return null;
            var store = StoreOf(rt);
            var r = store?.Find(row.No);
            if (r == null) return "the store has no bake " + row.No;
            var res = rt.Session?.Model?.File?.Res;
            if (res == null) return "the scene has no resources";
            foreach (var sha in r.Blobs())
            {
                if (res.Contains(BakeBlob.ResName(sha))) continue;
                var bytes = store.ReadBlob(sha);
                if (bytes == null) return "the store's bake " + row.No + " is damaged (" + sha + ")";
                res.AddBlob(bytes);
            }
            state.Bake = r.ToItemBake();
            state.Ref = row.Key.ToString();
            return null;
        }
    }

    /// <summary>
    /// A bake's mesh and texture blobs inflated on workers for the build of its prototype: one task per blob, started in the order the build takes
    /// them (the textures by index, then the meshes) while the inflated bytes started and not yet taken stay within <see cref="MaxHeld"/> (one
    /// blob always runs). The build takes each blob on the main thread, where a failure is reported as the inline inflate reports it (the task
    /// keeps what it threw); a blob passed over by the build (its texture already in the runtime's table) is not started. A texture blob that
    /// entries of other sampler states name is kept for them (read only: the build copies its levels); a mesh blob taken a second time, or a blob
    /// no task holds, is inflated where it is taken.
    /// </summary>
    internal sealed class BakeInflate
    {
        /// <summary>Inflated bytes started and not yet taken, at most.</summary>
        public const long MaxHeld = 384L << 20;

        sealed class Entry
        {
            public BakeBlobKind Kind;
            public string Sha, Name;
            public byte[] Blob;
            public long Length;
            public Task<(object value, Exception error)> Task;
            public bool Done;
            // the takes the plan expects (texture: its entries' distinct keys), and the value kept for the takes after the first
            public int Uses = 1;
            public object Kept;
        }

        readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        List<Entry> order = new List<Entry>();
        int next, passed;
        long held, workerTicks;
        readonly Stopwatch wait = new Stopwatch();

        /// <summary>Milliseconds: spent by the workers (summed), waited for them on the main thread, spent starting before the main was read.</summary>
        public double WorkerMs => Interlocked.Read(ref workerTicks) * 1000.0 / Stopwatch.Frequency;
        public double WaitMs => wait.Elapsed.TotalMilliseconds;
        public double StartMs;
        /// <summary>Blobs planned for workers (after the main is read), and blobs the build took from a worker.</summary>
        public int Planned => order.Count;
        public int Taken { get; private set; }

        static string Key(BakeBlobKind kind, string sha) => (kind == BakeBlobKind.Mesh ? "m:" : "t:") + sha;

        /// <summary>Before the main is read: the texture blobs that <paramref name="data"/> names and <paramref name="res"/> holds start, in their
        /// order, except those whose texture the runtime's table holds already.</summary>
        public static BakeInflate Start(IReadOnlyList<string> data, ResStore res, BakeCache cache)
        {
            var sw = Stopwatch.StartNew();
            var f = new BakeInflate();
            var held = cache?.HeldBlobs();
            var seen = new HashSet<Entry>();
            if (data != null)
                foreach (var name in data)
                {
                    var bytes = res.Get(name);
                    string sha = BakeBlob.ShaOf(name);
                    if (bytes == null || sha == null || BakeBlob.KindOf(bytes) != BakeBlobKind.Texture || (held != null && held.Contains(sha))) continue;
                    var e = f.EntryOf(BakeBlobKind.Texture, sha, bytes, null);
                    if (e != null && seen.Add(e)) f.order.Add(e);
                }
            f.Refill();
            f.StartMs = sw.Elapsed.TotalMilliseconds;
            return f;
        }

        /// <summary>The entry of a blob (made when new; null when its header does not read: it is inflated where it is taken).</summary>
        Entry EntryOf(BakeBlobKind kind, string sha, byte[] bytes, string name)
        {
            string key = Key(kind, sha);
            if (byKey.TryGetValue(key, out var e)) return e;
            long length;
            try { length = BakeBlob.ReadHeader(bytes, kind); }
            catch (BakeFormatException) { return null; }
            e = new Entry { Kind = kind, Sha = sha, Blob = bytes, Length = length, Name = name };
            byKey[key] = e;
            return e;
        }

        /// <summary>Once the main is read: the blobs in the order the build takes them, the textures the runtime's table holds left out; the
        /// blobs started before keep running.</summary>
        public void Plan(BakeData b, BakeBlobSet set, BakeCache cache)
        {
            var plan = new List<Entry>();
            var seen = new HashSet<Entry>();
            var keys = new Dictionary<Entry, HashSet<string>>();
            for (int i = 0; i < b.Textures.Count; i++)
            {
                var r = b.Textures[i];
                var bytes = set.Get(r.Blob);
                if (bytes == null) continue;
                string key = BakeCache.TextureKey(r.Blob, r);
                if (key != null && cache != null && cache.HoldsTexture(key)) continue;
                var e = EntryOf(BakeBlobKind.Texture, r.Blob, bytes, null);
                if (e == null || e.Done) continue;
                if (seen.Add(e)) { plan.Add(e); keys[e] = new HashSet<string>(StringComparer.Ordinal); }
                keys[e].Add(key ?? "");
            }
            foreach (var kv in keys) kv.Key.Uses = Math.Max(1, kv.Value.Count);
            for (int i = 0; i < b.Meshes.Count; i++)
            {
                var bytes = set.Get(b.Meshes[i].Blob);
                if (bytes == null) continue;
                var e = EntryOf(BakeBlobKind.Mesh, b.Meshes[i].Blob, bytes, b.Meshes[i].Name);
                if (e != null && !e.Done && seen.Add(e)) plan.Add(e);
            }
            // a blob started before and not in the plan is let go
            foreach (var e in byKey.Values) if (!seen.Contains(e)) Drop(e);
            order = plan;
            next = passed = 0;
            held = 0;
            foreach (var e in order) if (e.Task != null) held += e.Length;
            Refill();
        }

        /// <summary>Starts the next blobs while the bytes held allow.</summary>
        void Refill()
        {
            for (; next < order.Count; next++)
            {
                var e = order[next];
                if (e.Done || e.Task != null) continue;
                if (held > 0 && held + e.Length > MaxHeld) break;
                Run(e);
            }
        }

        void Run(Entry e)
        {
            var kind = e.Kind;
            var bytes = e.Blob;
            string name = e.Name;
            e.Task = Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    object v = kind == BakeBlobKind.Texture ? (object)BakeBlobs.FromTexture(BakeTextureData.FromBlob(bytes)) : BakeBlobs.FromMesh(BakeMeshData.FromBlob(bytes), name);
                    return (v, (Exception)null);
                }
                catch (Exception x) { return ((object)null, x); }
                finally { Interlocked.Add(ref workerTicks, sw.ElapsedTicks); }
            });
            held += e.Length;
        }

        /// <summary>
        /// The inflated blob for the build (main thread): a mesh as <see cref="MeshData"/> named <paramref name="name"/>, a texture as
        /// <see cref="TexData"/> (null: its stored format is unknown). False when no worker holds it (inflate it here); throws what the inflate threw.
        /// The blobs planned before it and not taken are passed over.
        /// </summary>
        public bool TryTake(BakeBlobKind kind, string sha, string name, out object value)
        {
            value = null;
            if (sha == null || !byKey.TryGetValue(Key(kind, sha), out var e)) return false;
            if (e.Done)
            {
                // a texture taken before for another sampler state
                if (e.Kept == null) return false;
                value = e.Kept;
                if (--e.Uses <= 1) e.Kept = null;
                Taken++;
                return true;
            }
            if (kind == BakeBlobKind.Mesh && e.Name != name) return false;
            int at = order.IndexOf(e);
            if (at < 0) return false;
            for (; passed < at; passed++) Drop(order[passed]);
            if (e.Task == null) Run(e);
            var t = e.Task;
            Drop(e);
            Refill();
            wait.Start();
            try { t.Wait(); }
            finally { wait.Stop(); }
            var (v, error) = t.Result;
            Taken++;
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            if (kind == BakeBlobKind.Texture && e.Uses > 1 && v != null) e.Kept = v;
            value = v;
            return true;
        }

        /// <summary>Taken or passed over: its bytes leave the count, the entry lets go of its task and its blob.</summary>
        void Drop(Entry e)
        {
            if (e.Done) return;
            e.Done = true;
            if (e.Task != null) held -= e.Length;
            e.Task = null;
            e.Blob = null;
        }
    }
}
