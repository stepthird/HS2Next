// M8: the model rows of the performance table on the test scene S10/200
// (10 characters, each with the golden chara/coord subtree, 200 items of which 100 attached to the characters,
// 1 folder; built in memory). A row fails when its target is exceeded by more than 2× (timing noise); the numbers
// are reported either way.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_Perf
    {
        static string Id(char prefix, int i) => prefix + i.ToString("x31");

        /// <summary>S10/200 as a container of shared-nothing document objects.</summary>
        public static SceneContainer BuildS10(TestRunner t)
        {
            var golden = Kit.GoldenContainer(t, true);
            var charaDoc = golden.Scene.Find(Kit.GoldenChara);
            var itemDoc = golden.Scene.Find(Kit.GoldenItem);
            var c = new SceneContainer { Title = "S10/200" };
            var nodes = new List<SceneNode>();
            for (int i = 0; i < 10; i++)
            {
                var n = DocClone.Clone(charaDoc);
                n.Id = Id('c', i);
                n.Parent = null;
                n.Order = i;
                n.Transform.Pos = new Vec3(i * 10, 0, 0);
                nodes.Add(n);
                c.Charas[n.Id] = golden.Charas[Kit.GoldenChara].Clone();
            }
            for (int i = 0; i < 200; i++)
            {
                var n = DocClone.Clone(itemDoc);
                n.Id = Id('a', i);
                n.Order = i;
                n.Parent = i < 100 ? null : new NodeParent(Id('c', i % 10), DocEnum.AttachPoints[(i * 7) % 54]);
                nodes.Add(n);
            }
            nodes.Add(NodeDefaults.Folder(Id('f', 0)));
            c.Scene = new SceneDocument { Nodes = nodes };
            return c;
        }

        static double Median(List<double> l)
        {
            l.Sort();
            return l[l.Count / 2];
        }

        static void Row(TestRunner t, string what, double ms, double target, string unit = "ms")
        {
            t.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}: {1:F3} {2} (target {3} {2})", what, ms, unit, target));
            t.Check(ms <= 2 * target, what + " within 2× of the target (" + ms.ToString("F3") + " " + unit + " vs " + target + ")");
        }

        public static void Run(TestRunner t)
        {
            var container = BuildS10(t);
            t.Equal(211, container.Scene.Nodes.Count, "S10/200 has 211 nodes");

            // Bind (container already loaded)
            var binds = new List<double>();
            SceneSession s = null;
            for (int i = 0; i < 7; i++)
            {
                var fresh = Kit.Session((ulong)(100 + i));
                var sw = Stopwatch.StartNew();
                fresh.Bind(container);
                binds.Add(sw.Elapsed.TotalMilliseconds);
                s = fresh;
            }
            t.Check(s.Model.Count == 211 && !s.Commands.IsDirty, "S10/200 bound, consistent (nothing normalised)");
            Row(t, "Bind S10/200 (median of 7)", Median(binds), 10);

            // ExportDocument + canonical write
            var ex = new List<double>();
            byte[] json = null;
            for (int i = 0; i < 7; i++)
            {
                var sw = Stopwatch.StartNew();
                json = CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, s.ExportDocument().Write);
                ex.Add(sw.Elapsed.TotalMilliseconds);
            }
            Row(t, "ExportDocument + write scene.json (" + json.Length / 1024 + " KB, median of 7)", Median(ex), 20);

            // container save / load with the model conversion
            string path = Path.Combine(t.TempDir, "s10.hs2nscene");
            var saves = new List<double>();
            var loads = new List<double>();
            var opts = Kit.FixedSave(ThumbnailMode.Placeholder);
            for (int i = 0; i < 5; i++)
            {
                var sw = Stopwatch.StartNew();
                var r = s.Save(path, opts);
                saves.Add(sw.Elapsed.TotalMilliseconds);
                if (!r.Ok) t.Check(false, "save S10/200: " + r);
                var other = Kit.Session((ulong)(200 + i));
                sw.Restart();
                var lr = other.Load(path);
                loads.Add(sw.Elapsed.TotalMilliseconds);
                if (!lr.Ok || other.Model.Count != 211) t.Check(false, "load S10/200: " + lr);
            }
            Row(t, "save S10/200 incl. model conversion (" + new FileInfo(path).Length / 1024 + " KB, median of 5)", Median(saves), 250);
            Row(t, "load S10/200 incl. Bind (median of 5)", Median(loads), 250);

            // Set + delivery to 3 listeners
            int heard = 0;
            for (int i = 0; i < 3; i++) s.Events.Changed += b => heard += b.Changes.Count;
            string item = Id('a', 150);
            var times = new List<double>();
            long ticks = Stopwatch.Frequency;
            for (int i = 0; i < 6000; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                s.Set(item, "transform.pos", new Vec3(i, 1, 2));
                long t1 = Stopwatch.GetTimestamp();
                if (i >= 1000) times.Add((t1 - t0) * 1e6 / ticks);
            }
            times.Sort();
            double median = times[times.Count / 2], p99 = times[(int)(times.Count * 0.99)];
            t.Check(heard == 3 * 6000, "every set reached the 3 listeners");
            Row(t, "Set + 3 listeners, median", median, 20, "µs");
            Row(t, "Set + 3 listeners, p99", p99, 200, "µs");

            // transaction with 1,000 updates
            int entries = s.Commands.UndoList.Count;
            var swt = Stopwatch.StartNew();
            using (var tx = s.Commands.Begin("drag"))
            {
                for (int i = 0; i < 1000; i++) tx.Set(item, "transform.pos", new Vec3(0, i, 0));
                tx.Commit();
            }
            double txMs = swt.Elapsed.TotalMilliseconds;
            t.Check(s.Commands.UndoList.Count == Math.Min(entries + 1, 200) && s.Commands.UndoList[0].Label == "drag", "exactly one history entry for the transaction");
            Row(t, "transaction of 1,000 updates + commit", txMs, 20);

            // duplicate a character with its subtree (10 attached items, stored subtree)
            var swd = Stopwatch.StartNew();
            var dr = s.Duplicate(new[] { Id('c', 3) });
            double dupMs = swd.Elapsed.TotalMilliseconds;
            t.Check(dr.Ok && s.Model.Count == 211 + 11, "duplicate copies the character and its 10 items (" + dr + ")");
            Row(t, "duplicate of a character with its subtree", dupMs, 40);

            // delete + undo + redo of a character with 100 attached nodes
            var s2 = Kit.Session(300);
            var spec = new NodeSpec { Doc = NodeDefaults.Chara(null, null), Appearance = new CharaAppearance { Stored = container.Charas[Id('c', 0)].Clone() } };
            var kids = new List<NodeSpec>();
            for (int i = 0; i < 100; i++)
                kids.Add(new NodeSpec { Doc = NodeDefaults.Item(null, new ItemNodeState { Ref = "test.mod#1" }), Point = DocEnum.AttachPoints[i % 54] });
            spec.Children = kids.ToArray();
            var add = s2.AddNodes(new[] { spec });
            t.Check(add.Ok && s2.Model.Count == 101, "a character with 100 attached nodes");
            s2.Delete(new[] { add.NewId });
            s2.Commands.Undo();
            s2.Commands.Redo();
            s2.Commands.Undo();   // warm
            var sw3 = Stopwatch.StartNew();
            s2.Delete(new[] { add.NewId });
            s2.Commands.Undo();
            s2.Commands.Redo();
            double delMs = sw3.Elapsed.TotalMilliseconds;
            t.Check(s2.Model.Count == 0, "deleted after redo");
            Row(t, "delete + undo + redo of a character with 100 attached nodes", delMs, 10);
        }
    }
}
