// One drawn plugin form (a panel's content, or an inspector section for one node): the rows are the host widgets of
// PluginWidgetKit, the values come from the plugin's read command and edits go to its write command (the protocol of
// FormProtocol, inline calls through the form's host). Reads: when the form is first drawn, after a commit, a cancel, an
// undo, a redo and a button, when its tab or section is shown again, on Refresh and after a new or loaded scene — at most
// one per frame (a request in the frame of a read waits for the next frame). A failed read shows its error text in place of
// the rows, which stay disabled until the next good read; a value of the wrong shape shows "—" and is reported once in the
// plugin's log. Writes: a drag sends preview writes (at most one per frame, the last value of a frame wins), then commit, or
// cancel with the value before the drag on Esc; a typed value, a toggle, a choice, a pick or a push sends one commit. With
// the form's undo on, a successful commit that changed the value records one undo entry in the plugin's name (the row's
// label): its undo writes the value read before the edit (phase undo), its redo the committed value (phase redo); typed
// commits of one row merge (merge key "<guid>/<form>/<node>/<key>"). A write error is shown as a toast. Element names: the
// form "plugin-form-<guid>/<id>", its error line "<form>-error", each row "<form>-<key>" holding the widget parts of
// PluginWidgetKit with that row name as their base.
using System;
using System.Collections.Generic;
using HS2Next.Api.Plugins;
using HS2Next.Api.Ui;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using EditPhase = HS2Next.Api.Ui.EditPhase;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Plugins
{
    /// <summary>What a drawn form needs from the plugin host.</summary>
    public interface IPluginFormHost
    {
        string Guid { get; }
        /// <summary>The plugin is loaded and not faulted (its commands may be called).</summary>
        bool Live { get; }
        /// <summary>Runs an envelope inline (main thread) and returns the reply text.</summary>
        string Call(string envelope);
        /// <summary>Records one undo entry in the plugin's name.</summary>
        HistoryRecordResult Record(string label, Action undo, Action redo, string mergeKey);
        /// <summary>A warning line in the plugin's log, once per <paramref name="onceKey"/>.</summary>
        void Warn(string onceKey, string message);
        /// <summary>An error toast.</summary>
        void Toast(string text);
        /// <summary>The drawn views of the plugin's forms.</summary>
        PluginFormRegistry Registry { get; }
    }

    public sealed class PluginFormView : VisualElement
    {
        public const string UssClass = "hs-plugin-form", RowClass = "hs-plugin-form__row", ErrorClass = "hs-plugin-form__error",
            WrongClass = "hs-plugin-form__row--wrong";

        sealed class Row
        {
            public RowDef Def;
            public FieldSpec Spec;
            public VisualElement Box;
            public HostWidget Widget;
            /// <summary>The value of the last good read (null: none).</summary>
            public JToken Read;
            public bool WrongLogged;
            public List<FormChoice> Override;
            public string ChoicesSignature;
            public List<FormChoice> RefCache;
            public JToken DragBefore;
            public JToken PendingPreview;
            public int PreviewFrame = -1;
            public bool PreviewFailed;
        }

        readonly UiContext ctx;
        readonly IPluginFormHost host;
        readonly List<Row> rows = new List<Row>();
        readonly Label error;
        readonly VisualElement body;
        bool readWanted, ticking;
        int lastReadFrame = -1, attaches;

        public FormDef Def { get; }
        /// <summary>The node the form is drawn for (sections), or null (panels).</summary>
        public string NodeId { get; }
        /// <summary>Reads and writes sent so far (tests).</summary>
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        /// <summary>The error text of the last read, or null after a good one.</summary>
        public string ReadError { get; private set; }
        public bool Dropped { get; private set; }

        public PluginFormView(UiContext ctx, IPluginFormHost host, FormDef def, string nodeId)
        {
            this.ctx = ctx;
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            Def = def ?? throw new ArgumentNullException(nameof(def));
            NodeId = nodeId;
            name = def.ElementName;
            AddToClassList(UssClass);
            error = new Label { name = def.ElementName + "-error" };
            error.AddToClassList(ErrorClass);
            error.AddToClassList("hs-plugin-error");
            error.style.display = DisplayStyle.None;
            Add(error);
            body = new VisualElement { name = def.ElementName + "-rows" };
            Add(body);
            foreach (var r in def.Rows) Build(r);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                host.Registry?.Add(this);
                if (!ticking) { PanelTicker.Add(Tick); ticking = true; }
                if (++attaches > 1) RequestRead();   // a tab or section shown again
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                host.Registry?.Remove(this);
                if (ticking) { PanelTicker.Remove(Tick); ticking = false; }
            });
            ReadNow();
        }

        // ------------------------------------------------------------ building

        string RowName(RowDef r) => Def.RowName(r.Key);

        TargetSelector SexSelector => NodeId != null ? TargetSelector.Node(NodeId) : TargetSelector.Scene;

        void Build(RowDef d)
        {
            var row = new Row { Def = d, Spec = d.ToFieldSpec(), Box = new VisualElement { name = RowName(d) } };
            row.Box.AddToClassList(RowClass);
            if (d.TooltipKey != null) Loc.Tooltip(row.Box, d.TooltipKey);
            row.Widget = Make(row);
            if (row.Widget != null) row.Box.Add(row.Widget.Element);
            if (d.ReadOnly) row.Widget?.Element.SetEnabled(false);
            rows.Add(row);
            body.Add(row.Box);
        }

        HostWidget Make(Row row)
        {
            var d = row.Def;
            string n = RowName(d);
            switch (d.Kind)
            {
                case RowKind.Float:
                case RowKind.Int:
                {
                    var s = row.Spec;
                    double? min = d.Min.HasValue ? s.ToDisplay(d.Min.Value) : (double?)null, max = d.Max.HasValue ? s.ToDisplay(d.Max.Value) : (double?)null;
                    double? def = FormValues.Number(d.Default);
                    Func<double?> reset = def.HasValue ? () => s.ToDisplay(def.Value) : (Func<double?>)null;
                    bool wholeShown = s.Whole && s.Scale == 1;
                    return PluginWidgetKit.Number(n, d.LabelKey, min, max, wholeShown, d.Format ?? (s.Whole && s.Scale == 1 ? "0" : null), reset, (v, p, typed) =>
                    {
                        double stored = typed ? s.ParseTyped(v) : s.OnGrid(s.FromDisplay(v));
                        if (s.Whole) stored = Math.Round(stored, MidpointRounding.AwayFromZero);
                        Edit(row, FormValues.Number(stored, s.Whole), p, typed);
                    });
                }
                case RowKind.Vec2:
                case RowKind.Vec3:
                {
                    int count = d.Kind == RowKind.Vec2 ? 2 : 3;
                    var s = row.Spec;
                    return PluginWidgetKit.Vector(n, d.LabelKey, count, d.Format, (v, p, typed) =>
                    {
                        var c = new double[count];
                        for (int i = 0; i < count; i++) c[i] = typed ? s.ClampTyped(v[i]) : s.OnGrid(v[i]);
                        Edit(row, FormValues.Array(c), p, typed);
                    });
                }
                case RowKind.Bool:
                    return PluginWidgetKit.Toggle(n, d.LabelKey, b => Edit(row, new JValue(b), EditPhase.Commit, false));
                case RowKind.Enum:
                {
                    var choices = Choices(row);
                    row.ChoicesSignature = Signature(choices);
                    var labels = new List<(string, string)>();
                    foreach (var c in choices) labels.Add(LabelOf(c, row.Override == null));
                    return PluginWidgetKit.Choice(n, d.LabelKey, labels, i =>
                    {
                        var cs = Choices(row);
                        if (i >= 0 && i < cs.Count) Edit(row, new JValue(cs[i].Value ?? ""), EditPhase.Commit, false);
                    });
                }
                case RowKind.Color:
                case RowKind.ColorHdr:
                {
                    bool hdr = d.Kind == RowKind.ColorHdr;
                    return PluginWidgetKit.Color(() => ctx, n, d.LabelKey, !hdr && d.ColorAlpha, hdr, (c, i, p) =>
                    {
                        if (!hdr && !d.ColorAlpha) c.a = (float)(FormValues.Numbers(row.Read, 4)?[3] ?? 1);
                        Edit(row, hdr ? FormValues.Hdr(c.r, c.g, c.b, c.a, i) : FormValues.Array(c.r, c.g, c.b, c.a), p, false);
                    });
                }
                case RowKind.Ref:
                case RowKind.Node:
                    return PluginWidgetKit.Ref(n, d.LabelKey, () => RefChoices(row, true), v => RefLabel(row, v), v => Edit(row, FormValues.Text(v), EditPhase.Commit, false));
                case RowKind.File:
                    return PluginWidgetKit.File(n, d.LabelKey, p => Edit(row, FormValues.Text(p), EditPhase.Commit, false));
                case RowKind.Text:
                    return PluginWidgetKit.Text(n, d.LabelKey, d.Nullable, t => Edit(row, FormValues.Text(t), EditPhase.Commit, false));
                case RowKind.Button:
                    return PluginWidgetKit.Button(n, d.LabelKey, () => Press(row));
                case RowKind.AttachPoint:
                    return PluginWidgetKit.AttachPoint(ctx, n, d.LabelKey, k => Edit(row, FormValues.Text(k), EditPhase.Commit, false));
                case RowKind.PartList:
                    return PluginWidgetKit.PartList(ctx, n, d.LabelKey, d.CategoryFemale, d.CategoryMale, SexSelector, r => Edit(row, FormValues.Text(r), EditPhase.Commit, false));
                case RowKind.Push:
                    return PluginWidgetKit.Push(ctx, n, d.LabelKey, d.PushSource, SexSelector, r => Edit(row, FormValues.Text(r), EditPhase.Commit, false));
                default:
                    return null;
            }
        }

        List<FormChoice> Choices(Row row) => row.Override ?? row.Def.Choices;

        static string Signature(List<FormChoice> l)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in l) sb.Append(c.Value).Append('\u0001').Append(c.Label).Append('\u0002');
            return sb.ToString();
        }

        /// <summary>A choice's (key, text): the form's own choices carry keys; a reply's label is a key of the plugin's strings when it
        /// starts with the plugin's prefix, else its text (none: the value).</summary>
        (string key, string text) LabelOf(FormChoice c, bool ownChoice)
        {
            if (c.Label == null) return (null, c.Value ?? "");
            if (ownChoice || c.Label.StartsWith("plugin." + host.Guid + ".", StringComparison.Ordinal)) return (c.Label, null);
            return (null, c.Label);
        }

        string LabelText(FormChoice c, bool own)
        {
            var (k, t) = LabelOf(c, own);
            return k != null ? L10n.Get(k) : t;
        }

        IReadOnlyList<(string value, string label)> RefChoices(Row row, bool fetch)
        {
            var l = new List<(string, string)>();
            if (row.Override != null) row.RefCache = row.Override;
            else if (fetch && row.Def.ChoicesCommand != null && host.Live)
            {
                string reply = Call(FormProtocol.Choices(row.Def.ChoicesCommand, Def.Id, NodeId, row.Def.Key));
                var got = FormProtocol.ParseChoicesReply(reply, out var err);
                if (got == null) host.Toast(Def.Id + ": " + err);
                else row.RefCache = got;
            }
            if (row.Def.Kind == RowKind.Node && row.Def.ChoicesCommand == null && row.Override == null)
            {
                l.Add((null, L10n.Get("common.none")));
                foreach (var nd in ctx.Model.PreOrder()) l.Add((nd.Id, nd.Label));
                return l;
            }
            if (row.Def.Nullable) l.Add((null, L10n.Get("common.none")));
            if (row.RefCache != null) foreach (var c in row.RefCache) l.Add((c.Value, LabelText(c, false)));
            return l;
        }

        string RefLabel(Row row, string v)
        {
            if (v == null) return null;
            if (row.Def.Kind == RowKind.Node && row.Def.ChoicesCommand == null && row.Override == null) return ctx.Model.Find(v)?.Label ?? v;
            if (row.RefCache == null && row.Override == null) RefChoices(row, true);
            foreach (var c in row.Override ?? row.RefCache ?? new List<FormChoice>())
                if (c.Value == v) return LabelText(c, false);
            return v;
        }

        // ------------------------------------------------------------ reads

        /// <summary>Asks for a read (done in this or the next frame, at most one per frame).</summary>
        public void RequestRead()
        {
            if (Dropped) return;
            readWanted = true;
            if (!ticking && panel == null) return;   // not shown: read when shown again
        }

        void Tick()
        {
            if (Dropped) return;
            int f = Time.frameCount;
            foreach (var r in rows)
                if (r.PendingPreview != null && r.PreviewFrame != f)
                {
                    var v = r.PendingPreview;
                    r.PendingPreview = null;
                    SendPreview(r, v);
                }
            if (readWanted && lastReadFrame != f) ReadNow();
        }

        string Call(string envelope)
        {
            try { return host.Call(envelope); }
            catch (Exception e) { return new JObject { ["ok"] = false, ["code"] = "internal", ["error"] = e.GetType().Name + ": " + e.Message }.ToString(Newtonsoft.Json.Formatting.None); }
        }

        /// <summary>Reads the values now (tests; the form's own reads go through <see cref="RequestRead"/>).</summary>
        public void ReadNow()
        {
            readWanted = false;
            if (Dropped || !host.Live) return;
            lastReadFrame = Time.frameCount;
            Reads++;
            var r = FormProtocol.ParseRead(Call(FormProtocol.Read(Def.ReadCommand, Def.Id, NodeId)));
            if (!r.Ok)
            {
                ReadError = r.Error;
                error.text = L10n.Format("plugins.form.readFailed", r.Error);
                error.style.display = DisplayStyle.Flex;
                body.SetEnabled(false);
                body.style.display = DisplayStyle.None;
                return;
            }
            ReadError = null;
            error.style.display = DisplayStyle.None;
            body.SetEnabled(true);
            body.style.display = DisplayStyle.Flex;
            foreach (var row in rows) Apply(row, r);
        }

        void Apply(Row row, FormReadResult r)
        {
            var d = row.Def;
            row.Box.style.display = r.Hidden.Contains(d.Key) ? DisplayStyle.None : DisplayStyle.Flex;
            row.Widget?.Element.SetEnabled(!d.ReadOnly && !r.Disabled.Contains(d.Key));
            r.Choices.TryGetValue(d.Key, out var ov);
            row.Override = ov;
            if (d.Kind == RowKind.Enum && Signature(Choices(row)) != row.ChoicesSignature)
            {
                // other choices than the ones built: the widget is made again
                row.Widget?.Element.RemoveFromHierarchy();
                row.Widget = Make(row);
                if (row.Widget != null) row.Box.Add(row.Widget.Element);
                row.Widget?.Element.SetEnabled(!d.ReadOnly && !r.Disabled.Contains(d.Key));
            }
            if (d.Kind == RowKind.Ref) row.RefCache = ov ?? row.RefCache;
            if (d.Kind == RowKind.Button || d.Kind == RowKind.Push) return;
            bool present = r.Values.TryGetValue(d.Key, out var v);
            if (!present)
            {
                row.Read = null;
                Show(row, null, true);
                return;
            }
            if (!FormValues.Fits(d.Kind, v, out var problem))
            {
                row.Read = null;
                row.Box.AddToClassList(WrongClass);
                Show(row, null, true);
                if (!row.WrongLogged)
                {
                    row.WrongLogged = true;
                    host.Warn(Def.FullId + "/" + d.Key + "/shape", "form " + Def.Id + " row " + d.Key + ": " + problem);
                }
                return;
            }
            row.Box.RemoveFromClassList(WrongClass);
            row.Read = v.DeepClone();
            Show(row, v, false);
        }

        void Show(Row row, JToken v, bool mixed)
        {
            var w = row.Widget;
            if (w == null) return;
            var d = row.Def;
            switch (d.Kind)
            {
                case RowKind.Float:
                case RowKind.Int:
                    w.SetValue(mixed ? 0.0 : row.Spec.ToDisplay((double)v), mixed);
                    break;
                case RowKind.Bool:
                    w.SetValue(!mixed && (bool)v, mixed);
                    break;
                case RowKind.Color:
                {
                    var c = mixed ? null : FormValues.Numbers(v, 4);
                    w.SetValue(c == null ? Color.white : new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3]), mixed);
                    break;
                }
                case RowKind.ColorHdr:
                {
                    var c = mixed ? null : FormValues.Numbers(v["color"], 4);
                    w.SetValue(c == null ? new HdrColor(Color.white, 0) : new HdrColor(new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3]), (float)(double)v["intensity"]), mixed);
                    break;
                }
                case RowKind.Vec2:
                case RowKind.Vec3:
                {
                    var c = mixed ? null : FormValues.Numbers(v, d.Kind == RowKind.Vec2 ? 2 : 3);
                    var vec = Vector4.zero;
                    if (c != null) for (int i = 0; i < c.Length; i++) vec[i] = (float)c[i];
                    w.SetValue(vec, mixed);
                    break;
                }
                case RowKind.Enum:
                {
                    int index = -1;
                    if (!mixed)
                    {
                        var cs = Choices(row);
                        for (int i = 0; i < cs.Count; i++) if (cs[i].Value == (string)v) { index = i; break; }
                    }
                    w.SetValue(index, mixed);
                    break;
                }
                default:
                    w.SetValue(mixed || v == null || v.Type == JTokenType.Null ? null : (string)v, mixed);
                    break;
            }
        }

        // ------------------------------------------------------------ writes

        void Edit(Row row, JToken value, EditPhase phase, bool typed)
        {
            if (Dropped || !host.Live || Def.WriteCommand == null || row.Def.ReadOnly) return;
            switch (phase)
            {
                case EditPhase.Preview:
                    if (row.DragBefore == null) { row.DragBefore = row.Read?.DeepClone() ?? JValue.CreateNull(); row.PreviewFailed = false; }
                    if (row.PreviewFrame == Time.frameCount) { row.PendingPreview = value; return; }
                    SendPreview(row, value);
                    break;
                case EditPhase.Cancel:
                {
                    row.PendingPreview = null;
                    var before = row.DragBefore ?? row.Read ?? JValue.CreateNull();
                    row.DragBefore = null;
                    Write(row, before, FormProtocol.Cancel);
                    RequestRead();
                    break;
                }
                default:
                {
                    row.PendingPreview = null;
                    var before = row.DragBefore ?? row.Read?.DeepClone() ?? JValue.CreateNull();
                    row.DragBefore = null;
                    Commit(row, before, value, typed);
                    break;
                }
            }
        }

        void SendPreview(Row row, JToken value)
        {
            row.PreviewFrame = Time.frameCount;
            if (!WriteQuiet(row, value, FormProtocol.Preview, out var err) && !row.PreviewFailed)
            {
                row.PreviewFailed = true;
                host.Warn(Def.FullId + "/" + row.Def.Key + "/preview", "form " + Def.Id + " row " + row.Def.Key + ": a preview write failed: " + err);
            }
        }

        bool WriteQuiet(Row row, JToken value, string phase, out string err)
        {
            Writes++;
            return FormProtocol.Succeeded(Call(FormProtocol.Write(Def.WriteCommand, Def.Id, NodeId, row.Def.Key, value, phase)), out err, out _);
        }

        bool Write(Row row, JToken value, string phase)
        {
            bool ok = WriteQuiet(row, value, phase, out var err);
            if (!ok) host.Toast(err);
            return ok;
        }

        void Commit(Row row, JToken before, JToken after, bool typed)
        {
            bool ok = Write(row, after, FormProtocol.Commit);
            if (ok && Def.Undo && !FormValues.Same(before, after))
            {
                var h = host;
                var def = Def;
                string node = NodeId, key = row.Def.Key;
                JToken b = before.DeepClone(), a = after.DeepClone();
                string label = row.Def.LabelKey != null ? L10n.Get(row.Def.LabelKey) : key;
                var res = h.Record(label, () => UndoWrite(h, def, node, key, b, FormProtocol.Undo), () => UndoWrite(h, def, node, key, a, FormProtocol.Redo),
                    typed ? PluginFormRules.MergeKey(def.Guid, def.Id, node, key) : null);
                LastRecord = res;
            }
            RequestRead();
        }

        /// <summary>The result of the last undo record (tests).</summary>
        public HistoryRecordResult? LastRecord { get; private set; }

        /// <summary>An undo or redo of a form entry: the write in the plugin's name, then a read of every drawn view of that form and node.</summary>
        static void UndoWrite(IPluginFormHost h, FormDef def, string node, string key, JToken value, string phase)
        {
            if (!h.Live) return;
            string reply;
            try { reply = h.Call(FormProtocol.Write(def.WriteCommand, def.Id, node, key, value, phase)); }
            catch (Exception e) { reply = null; h.Toast(def.Id + ": " + e.Message); }
            if (reply != null && !FormProtocol.Succeeded(reply, out var err, out _)) h.Toast(err);
            h.Registry?.Refresh(def.FullId, node);
        }

        void Press(Row row)
        {
            if (Dropped || !host.Live) return;
            var reply = Call(FormProtocol.Button(row.Def, Def.Id, NodeId));
            if (!FormProtocol.Succeeded(reply, out var err, out _)) host.Toast(err);
            RequestRead();
        }

        /// <summary>The form was removed (its plugin unloaded or removed it): the view leaves the UI (an inspector section with it).</summary>
        public void Drop()
        {
            if (Dropped) return;
            Dropped = true;
            if (ticking) { PanelTicker.Remove(Tick); ticking = false; }
            var fo = GetFirstAncestorOfType<Foldout>();
            if (fo != null && fo.contentContainer.childCount == 1 && fo.contentContainer[0] == this) fo.RemoveFromHierarchy();
            else RemoveFromHierarchy();
        }
    }

    /// <summary>The drawn views of one plugin's forms (Refresh, scene events, removal).</summary>
    public sealed class PluginFormRegistry
    {
        readonly List<PluginFormView> views = new List<PluginFormView>();

        public IReadOnlyList<PluginFormView> Views => views;

        internal void Add(PluginFormView v) { if (!views.Contains(v)) views.Add(v); }

        internal void Remove(PluginFormView v) => views.Remove(v);

        /// <summary>Asks the drawn views of <paramref name="fullId"/> (of <paramref name="node"/>, or every node when null) for a read; returns how many.</summary>
        public int Refresh(string fullId, string node = null)
        {
            int n = 0;
            foreach (var v in views.ToArray())
                if (v.Def.FullId == fullId && (node == null || v.NodeId == node)) { v.RequestRead(); n++; }
            return n;
        }

        /// <summary>Every drawn view reads again (a new or loaded scene).</summary>
        public void RefreshAll()
        {
            foreach (var v in views.ToArray()) v.RequestRead();
        }

        /// <summary>The views of <paramref name="fullId"/> leave the UI.</summary>
        public void Drop(string fullId)
        {
            foreach (var v in views.ToArray())
                if (v.Def.FullId == fullId) { views.Remove(v); v.Drop(); }
        }
    }
}
