// Live sections of an Edit-tab page: sections whose rows follow gates that read the document or the worn prefab (the Hair
// page's colour, decoration, bundle and mesh sections; the Clothes page's colour sets, pattern rows, break and option
// parts). A section is marked when a scene change touches one of its watched paths (SchemaSection.Watch) on the primary
// character, and a signal (a page-supplied list of paths with an action, e.g. the part buttons' marks) when one of its
// paths changes. Marked entries are re-evaluated at the next UI tick and after every later flush of the character for
// LiveFrames frames (the flush that realizes an item change swaps the part and so changes the prefab's facts), never while
// another row's edit is open. A section whose rows' gate results (plus an optional per-row extra signature, e.g. the bundle
// structure of a hair bundle list) changed is refilled; a section whose rows are all gated off hides. Re-attaching the page
// marks every entry (changes made while another node was shown). One instance per page; the page supplies how sections are
// shown and filled and how gates are evaluated. 4.5: the Accessories page's slot groups register their sections and remove
// them when a group is dropped.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class LiveSections
    {
        /// <summary>A live section: its schema, its foldout, the signature of its last fill and the part / kind it belongs to
        /// (-1 = none).</summary>
        public sealed class Entry
        {
            public SchemaSection Section;
            public Foldout Fold;
            public string Signature;
            public int Part;
        }

        sealed class Pending { public int Frames, SeenFlushes; public bool Evaluated; }
        sealed class Signal { public string Key; public string[] Paths; public Action Evaluate; }

        /// <summary>Frames a marked entry stays watched for the flush that realizes the change.</summary>
        public const int LiveFrames = 120;

        readonly UiContext ctx;
        readonly string primary;
        readonly Dictionary<string, Entry> live = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.Ordinal);
        readonly List<Signal> signals = new List<Signal>();

        /// <summary>Whether a section is shown (at least one row passes its gate).</summary>
        public Func<SchemaSection, bool> Visible;
        /// <summary>Adds the rows of a shown section to its foldout.</summary>
        public Action<Entry> Fill;
        /// <summary>A gate's result on the primary.</summary>
        public Func<string, bool> Gate;
        /// <summary>Runs an action with the page's gate memo on (each gate evaluated once inside it).</summary>
        public Action<Action> WithMemo = a => a();
        /// <summary>An extra signature of a row (null = none): a change refills the section as a gate change does.</summary>
        public Func<Entry, FieldSpec, string> Extra;
        /// <summary>Called after every evaluation of a section, refilled or not (rows that follow a gate without a rebuild).</summary>
        public Action<Entry> Evaluated;
        /// <summary>The realization signal: a counter that moves with every flush of the primary (default: its edit flushes).</summary>
        public Func<int> Flushes;

        /// <summary>Re-evaluations that refilled a section / evaluations of marked entries (tests).</summary>
        public int Rebuilds { get; private set; }
        public int Evaluations { get; private set; }
        /// <summary>Entries waiting for the flush that realizes a change (tests).</summary>
        public int PendingCount => pending.Count;
        /// <summary>The live sections by section id.</summary>
        public IReadOnlyDictionary<string, Entry> Entries => live;

        public LiveSections(UiContext ctx, string primaryId)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            primary = primaryId;
            Flushes = () => ctx.Runtime?.FindChara(primary)?.EditFlushes ?? 0;
        }

        /// <summary>Registers a live section and fills it (always created; hidden while every row is gated off).</summary>
        public Entry Add(Foldout fold, SchemaSection section, int part)
        {
            var e = new Entry { Section = section, Fold = fold, Part = part };
            live[section.Id] = e;
            Refill(e);
            return e;
        }

        /// <summary>Forgets a live section (its slot group was dropped; 4.5's Accessories page). A pending mark of it ends unused.</summary>
        public void Remove(string sectionId)
        {
            if (sectionId != null) live.Remove(sectionId);
        }

        /// <summary>A page-supplied watch: a change of one of <paramref name="paths"/> on the primary runs
        /// <paramref name="evaluate"/> at the next tick and after the realizing flushes (e.g. the part buttons' marks).</summary>
        public void AddSignal(string key, IEnumerable<string> paths, Action evaluate)
        {
            signals.Add(new Signal { Key = key, Paths = paths?.ToArray() ?? Array.Empty<string>(), Evaluate = evaluate });
        }

        /// <summary>Subscribes to scene changes while <paramref name="page"/> is attached (re-marking everything on attach) and
        /// ticks every 10 ms.</summary>
        public void Attach(VisualElement page)
        {
            page.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ctx.SceneChanged -= OnChanged;
                ctx.SceneChanged += OnChanged;
                MarkAll();   // changes made while the page was not shown
            });
            page.RegisterCallback<DetachFromPanelEvent>(_ => ctx.SceneChanged -= OnChanged);
            page.schedule.Execute(Tick).Every(10);
        }

        void OnChanged(SceneChangeBatch b)
        {
            if (b == null) return;
            foreach (var e in live.Values)
            {
                var w = e.Section.Watch;
                if (w == null) continue;
                foreach (var p in w) if (b.Touches(primary, p)) { Mark(e.Section.Id); break; }
            }
            foreach (var s in signals)
                foreach (var p in s.Paths) if (b.Touches(primary, p)) { Mark(s.Key); break; }
        }

        /// <summary>Marks a section id or a signal key for re-evaluation.</summary>
        public void Mark(string key) => pending[key] = new Pending { Frames = LiveFrames, SeenFlushes = int.MinValue };

        /// <summary>Marks every section with watched paths and every signal.</summary>
        public void MarkAll()
        {
            foreach (var id in live.Keys) if (live[id].Section.Watch != null) Mark(id);
            foreach (var s in signals) Mark(s.Key);
        }

        /// <summary>
        /// Re-evaluates the marked entries at the next UI frame and after each later flush of the character, never while an
        /// edit (another row's drag) is open; a section whose signature changed is refilled.
        /// </summary>
        public void Tick()
        {
            if (pending.Count == 0) return;
            if (ctx.ActiveEdit != null && ctx.ActiveEdit.IsOpen) return;
            int fl = Flushes();
            WithMemo(() =>
            {
                foreach (var key in pending.Keys.ToList())
                {
                    var pe = pending[key];
                    if (!pe.Evaluated || pe.SeenFlushes != fl)
                    {
                        pe.Evaluated = true;
                        pe.SeenFlushes = fl;
                        Evaluate(key);
                    }
                    if (--pe.Frames <= 0) pending.Remove(key);
                }
            });
        }

        /// <summary>Re-evaluates the marked entries now (tests; the tick does the same at the next frame).</summary>
        public void EvaluateNow() => WithMemo(() => { foreach (var key in pending.Keys.ToList()) Evaluate(key); });

        void Evaluate(string key)
        {
            Evaluations++;
            foreach (var s in signals) if (s.Key == key) { s.Evaluate?.Invoke(); return; }
            if (!live.TryGetValue(key, out var e)) return;
            if (SignatureOf(e) != e.Signature)
            {
                Refill(e);
                Rebuilds++;
            }
            Evaluated?.Invoke(e);
        }

        /// <summary>Clears and refills a section's rows (hidden when no row passes its gate).</summary>
        public void Refill(Entry e)
        {
            e.Fold.Clear();
            bool vis = Visible == null || Visible(e.Section);
            e.Fold.style.display = vis ? DisplayStyle.Flex : DisplayStyle.None;
            if (vis) Fill?.Invoke(e);
            e.Signature = SignatureOf(e);
        }

        /// <summary>The gate results of a section's rows (+ the rows' extra signatures).</summary>
        public string SignatureOf(Entry e)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in e.Section.Fields)
            {
                sb.Append(Gate == null || Gate(f.Gate) ? '1' : '0');
                string x = Extra?.Invoke(e, f);
                if (x != null) sb.Append('[').Append(x).Append(']');
            }
            return sb.ToString();
        }
    }
}
