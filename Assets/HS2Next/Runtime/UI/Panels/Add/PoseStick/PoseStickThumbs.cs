// Generated thumbnails of the pose browser: a stick figure for every HS2 pose file that has
// no image of its own (PoseTools PNGs and HS2Next files with a thumbnail keep theirs). A cell asks for the figure where it
// showed the text tile; the figure is drawn from the pose data only (PoseStickRig: the app's import plan, FK writer and IK
// stage on a bones-only skeleton of the pose's sex), into a 256 × 256 transparent image on the CPU (StickDrawing), and kept
// in the browser's ThumbnailLoader like any thumbnail (same LRU). Pipeline per pose: a worker reads and parses the file;
// the main thread poses the rig (within the frame budget the browser gives, 4 ms with the loader's own work); a worker
// draws, encodes the PNG and writes it to the disk cache (`Cache/poses/stick/<hash>.png`, keyed by the file's path, size and
// time, the image size, the drawing version and the rig's stamp); the main thread turns the pixels into a texture. Later
// showings read the PNG through the loader's file path (decoded off the main thread). Generation runs on request only
// (visible cells, in the order they asked; a cell scrolled away before its turn is skipped); nothing fills the cache in the
// background. A pose that cannot be drawn (unreadable, no skeleton) falls back to the text tile and is counted once.
// Main thread (the workers touch no engine object).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Import.Pose;
using HS2Next.UI.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.UI.Panels
{
    public sealed class PoseStickThumbs : ThumbnailLoader.IGenerator, IDisposable
    {
        /// <summary>The image keys of generated figures start with this (the rest is the pose file's full path).</summary>
        public const string KeyPrefix = "posestick:";
        /// <summary>Image size: the pose grid's square cell at its largest zoom (126 × 1.5 px) stays sharp up to a 135 % display scale.</summary>
        public const int Size = 256;
        /// <summary>Workers reading pose files at once.</summary>
        public const int MaxReads = 4;

        sealed class Job
        {
            public string Key, Path, CacheFile, RigStamp;
            public int Sex;
            public PoseFile Pose;
            public string Error;
            public byte[] Pixels;
            public double ReadMs, PoseMs, DrawMs, EncodeMs;
            public Stopwatch Clock;
        }

        readonly Func<ContentCatalog> catalog;
        readonly Func<StudioDb> db;
        readonly Func<string> cacheDir;
        readonly Dictionary<string, PoseEntry> entries = new Dictionary<string, PoseEntry>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<Job> toRead = new Queue<Job>();
        readonly ConcurrentQueue<Job> parsed = new ConcurrentQueue<Job>(), drawn = new ConcurrentQueue<Job>();
        readonly PoseStickRig[] rigs = new PoseStickRig[2];
        readonly string[] rigProblem = new string[2];
        readonly string[] stamps = new string[2];
        int reading, drawing;
        volatile bool disposed;

        /// <summary>A generated texture is ready (the loader takes it into its LRU).</summary>
        public Action<string, Texture2D> Provide;
        /// <summary>A key cannot be drawn (the loader forgets its request; the cell shows the text tile).</summary>
        public Action<string> Withdraw;
        /// <summary>A pose could not be drawn (once per file and session); the browser refreshes the cells to the text tile.</summary>
        public event Action<PoseEntry> Failed;
        /// <summary>Asks whether a cell still wants a key (null = always).</summary>
        public Func<string, bool> Wanted;
        /// <summary>Write the PNGs to the disk cache (tests may switch it off).</summary>
        public bool WriteDiskCache = true;

        // statistics
        public int Generated { get; private set; }
        public int FailedCount => failed.Count;
        public int Skipped { get; private set; }
        public int DiskWritten => Volatile.Read(ref diskWritten);
        int diskWritten;
        public int Pending => toRead.Count + Volatile.Read(ref reading) + parsed.Count + Volatile.Read(ref drawing) + drawn.Count;
        public readonly List<double> MainMs = new List<double>(), PoseMs = new List<double>(), ReadMs = new List<double>(), DrawMs = new List<double>(), EncodeMs = new List<double>(), TotalMs = new List<double>();
        public double RigBuildMs { get; private set; }
        public readonly List<string> Problems = new List<string>();

        public PoseStickThumbs(Func<ContentCatalog> catalog, Func<StudioDb> db, Func<string> cacheDir)
        {
            this.catalog = catalog;
            this.db = db;
            this.cacheDir = cacheDir;
        }

        /// <summary>True when the entry gets a generated figure (an HS2 pose file without an image that has not failed).</summary>
        public bool Draws(PoseEntry e) => e != null && !e.HasImage && !e.IsHs2n && e.Status == PoseEntryStatus.Pose && !failed.Contains(e.FullPath);

        /// <summary>The image key of an entry (registers it for the generator).</summary>
        public string KeyOf(PoseEntry e)
        {
            if (e == null) return null;
            entries[e.FullPath] = e;
            return KeyPrefix + e.FullPath;
        }

        public static bool IsKey(string key) => key != null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

        /// <summary>The loader's generator: keys of this prefix.</summary>
        public bool Claims(string key) => IsKey(key);

        /// <summary>Makes this the loader's generator (textures into its LRU, requests it no longer wants skipped).</summary>
        public void Attach(ThumbnailLoader loader)
        {
            loader.Generator = this;
            Provide = loader.Provide;
            Withdraw = loader.Withdraw;
            Wanted = loader.Wanted;
        }

        PoseEntry EntryOf(string key) => IsKey(key) && entries.TryGetValue(key.Substring(KeyPrefix.Length), out var e) ? e : null;

        static int SexOf(PoseEntry e) => e.Sex == 0 ? 0 : 1;

        string StampFor(int sex)
        {
            if (stamps[sex] != null) return stamps[sex];
            var c = catalog?.Invoke();
            if (c != null) return stamps[sex] = PoseStickRig.StampOf(c, sex);
            // no install: the cached rig's own stamp (read once)
            var rest = StickRest.Read(PoseStickRig.RigFile(cacheDir?.Invoke(), sex), out _);
            return stamps[sex] = rest?.Stamp ?? "";
        }

        /// <summary>The disk cache file of an entry (null without a cache folder).</summary>
        public string CacheFileOf(PoseEntry e) =>
            e == null ? null : PoseThumbKey.FileOf(cacheDir?.Invoke(), PoseThumbKey.KeyText(e.FullPath, e.Size, e.ModifiedUtc.Ticks, Size, Size, StampFor(SexOf(e))));

        /// <summary>The cached PNG of a key when it exists (the loader reads it as a file), else null.</summary>
        public string CachedFile(string key)
        {
            var e = EntryOf(key);
            if (e == null) return null;
            string f = CacheFileOf(e);
            return f != null && File.Exists(f) ? f : null;
        }

        /// <summary>Starts generating a key (no-op when it is under way or unknown: unknown keys are withdrawn).</summary>
        public void Want(string key)
        {
            var e = EntryOf(key);
            if (e == null || !Draws(e)) { Withdraw?.Invoke(key); return; }
            if (!busy.Add(e.FullPath)) return;
            int sex = SexOf(e);
            toRead.Enqueue(new Job { Key = key, Path = e.FullPath, Sex = sex, RigStamp = StampFor(sex), CacheFile = CacheFileOf(e), Clock = Stopwatch.StartNew() });
        }

        /// <summary>One frame of work within <paramref name="budgetMs"/> of main-thread time.</summary>
        public void Pump(double budgetMs)
        {
            var sw = Stopwatch.StartNew();
            // reads off the main thread
            while (toRead.Count > 0 && Volatile.Read(ref reading) < MaxReads)
            {
                var j = toRead.Dequeue();
                if (Wanted != null && !Wanted(j.Key)) { Drop(j); continue; }
                Interlocked.Increment(ref reading);
                Task.Run(() =>
                {
                    var t = Stopwatch.StartNew();
                    try { j.Pose = PoseFile.ReadFile(j.Path); }
                    catch (Exception x) { j.Error = "read: " + x.Message; }
                    j.ReadMs = t.Elapsed.TotalMilliseconds;
                    parsed.Enqueue(j);
                    Interlocked.Decrement(ref reading);
                });
            }
            // textures of drawn figures
            while (sw.Elapsed.TotalMilliseconds < budgetMs && drawn.TryDequeue(out var d))
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                Finish(d);
                MainMs.Add(sw.Elapsed.TotalMilliseconds - t0);
            }
            // posing on the main thread, drawing off it
            while (sw.Elapsed.TotalMilliseconds < budgetMs && parsed.TryDequeue(out var j))
            {
                var t0 = sw.Elapsed.TotalMilliseconds;
                PoseOne(j);
                MainMs.Add(sw.Elapsed.TotalMilliseconds - t0);
            }
        }

        void Drop(Job j)
        {
            busy.Remove(j.Path);
            Skipped++;
        }

        void PoseOne(Job j)
        {
            if (Wanted != null && !Wanted(j.Key)) { Drop(j); return; }
            if (j.Error != null) { Fail(j, j.Error); return; }
            var rig = RigOf(j.Sex);
            if (rig == null) { Fail(j, rigProblem[j.Sex] ?? "no skeleton"); return; }
            StickPose pose;
            var t = Stopwatch.StartNew();
            try { pose = rig.Apply(j.Pose, out _); }
            catch (Exception x) { Fail(j, "pose: " + x.GetType().Name + ": " + x.Message); return; }
            j.PoseMs = t.Elapsed.TotalMilliseconds;
            j.Pose = null;
            if (!pose.Finite) { Fail(j, "pose: not finite"); return; }
            Interlocked.Increment(ref drawing);
            bool write = WriteDiskCache && j.CacheFile != null;
            Task.Run(() =>
            {
                try
                {
                    var td = Stopwatch.StartNew();
                    var img = StickDrawing.Draw(pose, Size, Size);
                    j.DrawMs = td.Elapsed.TotalMilliseconds;
                    if (img == null) j.Error = "draw: no joints";
                    else
                    {
                        j.Pixels = img.Pixels;
                        if (write && !disposed)
                        {
                            var te = Stopwatch.StartNew();
                            var png = StickPng.Encode(img.Pixels, Size, Size);
                            j.EncodeMs = te.Elapsed.TotalMilliseconds;
                            WriteFile(j.CacheFile, png);
                        }
                    }
                }
                catch (Exception x) { j.Error = "draw: " + x.GetType().Name + ": " + x.Message; }
                drawn.Enqueue(j);
                Interlocked.Decrement(ref drawing);
            });
        }

        void WriteFile(string path, byte[] png)
        {
            string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(tmp, png);
                if (File.Exists(path)) File.Replace(tmp, path, null, true);
                else File.Move(tmp, path);
                tmp = null;
                Interlocked.Increment(ref diskWritten);
            }
            catch (Exception) { }   // the cache is optional: the next showing draws again
            finally { if (tmp != null) try { File.Delete(tmp); } catch (Exception) { } }
        }

        void Finish(Job j)
        {
            busy.Remove(j.Path);
            if (j.Error != null) { Fail(j, j.Error); return; }
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "HS2Next pose stick", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            tex.LoadRawTextureData(j.Pixels);
            tex.Apply(false, true);
            Generated++;
            ReadMs.Add(j.ReadMs); PoseMs.Add(j.PoseMs); DrawMs.Add(j.DrawMs);
            if (j.EncodeMs > 0) EncodeMs.Add(j.EncodeMs);
            TotalMs.Add(j.Clock.Elapsed.TotalMilliseconds);
            if (Provide != null) Provide(j.Key, tex);
            else Kill(tex);
        }

        void Fail(Job j, string why)
        {
            busy.Remove(j.Path);
            if (failed.Add(j.Path))
            {
                if (Problems.Count < 200) Problems.Add(System.IO.Path.GetFileName(j.Path) + ": " + why);
                Withdraw?.Invoke(j.Key);
                if (entries.TryGetValue(j.Path, out var e)) Failed?.Invoke(e);
            }
            else Withdraw?.Invoke(j.Key);
        }

        // one rig per sex for every generator of the process (several browsers, a test's browsers): kept while the same lists,
        // cache folder and rest stamp are in use, disposed when the app quits
        static readonly PoseStickRig[] shared = new PoseStickRig[2];
        static readonly string[] sharedKey = new string[2];
        static bool quitHooked;

        /// <summary>The rig of a sex, built on first use (null when there is no skeleton; the reason is kept).</summary>
        public PoseStickRig RigOf(int sex)
        {
            sex = sex == 0 ? 0 : 1;
            if (rigs[sex] != null || rigProblem[sex] != null) return rigs[sex];
            var t = Stopwatch.StartNew();
            try
            {
                var d = db?.Invoke();
                string key = StampFor(sex) + "|" + (cacheDir?.Invoke() ?? "") + "|" + (d == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d));
                if (shared[sex] != null && shared[sex].Root && sharedKey[sex] == key) rigs[sex] = shared[sex];
                else
                {
                    var r = PoseStickRig.Create(catalog?.Invoke(), d, sex, cacheDir?.Invoke(), out var problem);
                    if (r == null) rigProblem[sex] = problem ?? "no skeleton";
                    else
                    {
                        if (problem != null) Problems.Add("rig " + sex + ": " + problem);
                        shared[sex]?.Dispose();
                        shared[sex] = r; sharedKey[sex] = key;
                        rigs[sex] = r;
                        if (!quitHooked) { quitHooked = true; Application.quitting += DisposeShared; }
                    }
                }
            }
            catch (Exception x) { rigProblem[sex] = "rig: " + x.GetType().Name + ": " + x.Message; }
            RigBuildMs += t.Elapsed.TotalMilliseconds;
            return rigs[sex];
        }

        /// <summary>Destroys the shared rigs (app quit; tests at their end). Generators built them again on demand.</summary>
        public static void DisposeShared()
        {
            for (int i = 0; i < shared.Length; i++) { shared[i]?.Dispose(); shared[i] = null; sharedKey[i] = null; }
        }

        /// <summary>The generator's numbers for ui.state (poses.stick).</summary>
        public Dictionary<string, object> State()
        {
            double max = 0, med = 0;
            if (MainMs.Count > 0)
            {
                var a = MainMs.ToArray();
                Array.Sort(a);
                max = a[a.Length - 1];
                med = a[a.Length / 2];
            }
            return new Dictionary<string, object>
            {
                ["generated"] = Generated, ["failed"] = FailedCount, ["skipped"] = Skipped, ["pending"] = Pending, ["diskWritten"] = DiskWritten,
                ["rigBuildMs"] = Math.Round(RigBuildMs, 1), ["mainMsMedian"] = Math.Round(med, 3), ["mainMsMax"] = Math.Round(max, 3),
                ["rigs"] = (rigs[0] != null ? 1 : 0) + (rigs[1] != null ? 1 : 0),
            };
        }

        /// <summary>Forgets a file's state (it changed or was deleted).</summary>
        public void Forget(string fullPath)
        {
            if (fullPath == null) return;
            failed.Remove(fullPath);
            entries.Remove(fullPath);
        }

        static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        public void Dispose()
        {
            disposed = true;
            for (int i = 0; i < rigs.Length; i++) rigs[i] = null;   // shared: kept for the other generators
            while (drawn.TryDequeue(out _)) { }
            while (parsed.TryDequeue(out _)) { }
            toRead.Clear();
            busy.Clear();
        }
    }
}
