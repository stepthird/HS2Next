// Shared helpers of the scene model tests: sessions with deterministic ids,
// the 2.1 goldens as scenes and containers, canonical bytes of a model, an event recorder, synthetic card
// sources (synthetic paths and hashes only; no game data), and result checks. Only the public surface of the
// model is used (the tests compile into Assembly-CSharp-Editor, or a separate exe under plain Mono).
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class Kit
    {
        public const string GoldenChara = "6f1c0e4a9b2d4c3e8f7a6b5c4d3e2f10";
        public const string GoldenCamera = "7a7a7a7a7a7a7a7a7a7a7a7a7a7a7a7a";
        public const string GoldenItem = "0a1b2c3d4e5f60718293a4b5c6d7e8f9";
        public const string GoldenLight = "1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b";
        public const string GoldenFolder = "2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c";
        public const string GoldenRoute = "3d3d3d3d3d3d3d3d3d3d3d3d3d3d3d3d";
        public const string GoldenOpaque = "8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b";
        public const string GoldenOpaqueChild = "9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c";

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string GoldenDir(TestRunner t) => Path.Combine(t.FixturesDir ?? "", "SaveFormat", "golden_src");
        public static string FixtureDir(TestRunner t) => Path.Combine(t.FixturesDir ?? "", "SceneModel");
        public static byte[] Golden(TestRunner t, string name) => File.ReadAllBytes(Path.Combine(GoldenDir(t), name));
        /// <summary>A fixture of Tests/SceneModel/.</summary>
        public static byte[] Fixture(TestRunner t, string name) => File.ReadAllBytes(Path.Combine(FixtureDir(t), name));

        /// <summary>A session with counting ids (seed = the high 64 bits).</summary>
        public static SceneSession Session(ulong seed = 1, SessionOptions o = null)
        {
            o = o ?? new SessionOptions();
            o.Ids = new CountingIdSource(seed);
            return new SceneSession(o);
        }

        public static SceneDocument ReadScene(byte[] b, FindingList f = null) =>
            SceneDocument.Read(DocReader.ForDocument(b, new DocReadContext(SceneDocument.EntryName, false, f)));

        /// <summary>The golden scene as a document-only container (no subtree), or with the golden chara/coord placed under its character node.</summary>
        public static SceneContainer GoldenContainer(TestRunner t, bool withSubtree)
        {
            var c = new SceneContainer { Scene = ReadScene(Golden(t, "scene.json")), Title = "golden" };
            if (withSubtree)
            {
                c.Charas[GoldenChara] = new CharaSubtree
                {
                    Chara = CharaDocument.Read(DocReader.ForDocument(Golden(t, "chara.json"), new DocReadContext(CharaDocument.EntryName))),
                    Coord = CoordDocument.Read(DocReader.ForDocument(Golden(t, "coord.json"), new DocReadContext(CoordDocument.EntryName))),
                };
            }
            return c;
        }

        public static SceneSession GoldenSession(TestRunner t, bool withSubtree = false, ulong seed = 1, SessionOptions o = null)
        {
            var s = Session(seed, o);
            s.Bind(GoldenContainer(t, withSubtree));
            return s;
        }

        public static byte[] Bytes(SceneSession s) => s.Model.CanonicalBytes();
        public static string Text(byte[] b) => Utf8.GetString(b);

        public static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static string Diff(byte[] a, byte[] b) => a == null || b == null ? "null bytes" : DocIo.FirstDiff(a, b);

        public static string Hash(byte[] b)
        {
            using (var sha = SHA256.Create()) return JsonText.Hex(sha.ComputeHash(b));
        }

        /// <summary>Canonical bytes of the scene and of every stored character subtree (the "subtree set").</summary>
        public static string StateHash(SceneSession s)
        {
            var sb = new StringBuilder(Hash(Bytes(s))).Append("|title:").Append(s.Model.Title);
            foreach (var n in s.Model.PreOrder())
            {
                var st = n.Appearance?.Stored;
                if (st == null) continue;
                sb.Append('|').Append(n.Id).Append(':')
                  .Append(Hash(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, st.Chara.Write)))
                  .Append(Hash(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, st.Coord.Write)));
            }
            return sb.ToString();
        }

        /// <summary>A synthetic card source (no file): hash derived from the name, accessory ids for slots 0..accessories−1.</summary>
        public static CardAppearanceSource Card(string name, string hs2Path, int accessories, int sex = 1)
        {
            string sha = JsonText.DerivedId("card/" + name) + JsonText.DerivedId("card2/" + name);
            var ids = new List<string>();
            for (int i = 0; i < accessories; i++) ids.Add(CardAppearanceSource.AccessoryId(sha, i));
            return new CardAppearanceSource
            {
                Hs2Path = hs2Path, FullPath = hs2Path == null ? null : "X:/synthetic/" + hs2Path, Sha256 = sha, Sex = sex, Name = name,
                AccessoryIds = ids, ReadBytes = () => Utf8.GetBytes("synthetic card " + name),
            };
        }

        public static void Ok(TestRunner t, CommandResult r, string what) =>
            t.Check(r != null && r.Status == CommandStatus.Ok, what + (r == null ? " (null result)" : r.Status == CommandStatus.Ok ? "" : " (" + r + ")"));

        public static void NoOp(TestRunner t, CommandResult r, string what) =>
            t.Check(r != null && r.Status == CommandStatus.NoOp, what + (r == null ? " (null result)" : r.Status == CommandStatus.NoOp ? "" : " (" + r + ")"));

        public static void Fails(TestRunner t, CommandResult r, string code, string what) =>
            t.Check(r != null && r.Status == CommandStatus.Error && r.Code == code, what + " → " + code + (r == null ? " (null result)" : r.Status == CommandStatus.Error && r.Code == code ? "" : " (got " + r + ")"));

        public static void Valid(TestRunner t, SceneSession s, string what)
        {
            try
            {
                s.Model.Validate();
                t.Check(true, what + ": model valid");
            }
            catch (Exception e) { t.Check(false, what + ": " + e.Message); }
        }

        /// <summary>Seconds-resolution fixed clock for byte-identical saves.</summary>
        public static SaveOptions FixedSave(ThumbnailMode thumb = ThumbnailMode.Keep, ExtRegistry registry = null) => new SaveOptions
        {
            Clock = () => new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc),
            Thumbnail = thumb,
            Registry = registry,
            Verify = false,
        };
    }

    /// <summary>Records every event of a session in order.</summary>
    public sealed class EventRecorder
    {
        public readonly List<string> Log = new List<string>();
        public readonly List<SceneChangeBatch> Batches = new List<SceneChangeBatch>();
        public readonly List<string> Forgotten = new List<string>();
        public int Selection, History, Cleared, Loaded, Saving, Saved;
        public Action<SceneChangeBatch> OnChanged;

        public EventRecorder(SceneSession s)
        {
            s.Events.Changed += b =>
            {
                Batches.Add(b);
                Log.Add("changed:" + b.Origin);
                OnChanged?.Invoke(b);
            };
            s.Events.Cleared += () => { Cleared++; Log.Add("cleared"); };
            s.Events.Loaded += a => { Loaded++; Log.Add("loaded"); };
            s.Events.Saving += () => { Saving++; Log.Add("saving"); };
            s.Events.Saved += r => { Saved++; Log.Add("saved"); };
            s.Events.HistoryChanged += a => { History++; Log.Add("history"); };
            s.Events.SelectionChanged += a => { Selection++; Log.Add("selection"); };
            s.Events.NodeForgotten += id => { Forgotten.Add(id); Log.Add("forgotten:" + id); };
        }

        public void Clear()
        {
            Log.Clear();
            Batches.Clear();
            Forgotten.Clear();
            Selection = History = Cleared = Loaded = Saving = Saved = 0;
        }
    }
}
