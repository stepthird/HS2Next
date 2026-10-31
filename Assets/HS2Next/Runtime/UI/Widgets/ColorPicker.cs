// Colour widgets: ColorSwatch (the row's button showing the colour, "—" when
// mixed) opens the ColorPicker popup: SV square + hue strip + alpha strip, HSV (H 0–360, S / V 0–100), RGB (0–255),
// alpha 0–100 (vanilla ranges), hex, an HDR intensity row for emission (items T5), recent colours (12) and the user's
// sets (colors.json, 5 sets of up to 90). Live preview through the edit transaction: OK, Enter or a click outside =
// commit, Esc / Cancel = revert — Enter and Esc also from the number and hex fields (a typed value is applied first).
// No eyedropper (vanilla has none).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Widgets
{
    public sealed class ColorSwatch : VisualElement
    {
        public readonly Label LabelElement;
        public readonly Button Button;
        readonly VisualElement chip;
        readonly Label mixedMark;
        public Color Value { get; private set; } = Color.white;
        public bool Alpha = true;
        public event Action Clicked;

        public ColorSwatch(string label)
        {
            AddToClassList("hs-color");
            LabelElement = new Label(label ?? "");
            LabelElement.AddToClassList("hs-color__label");
            Add(LabelElement);
            Button = new Button(() => Clicked?.Invoke()) { name = "swatch" };
            Button.AddToClassList("hs-color__swatch");
            chip = new VisualElement();
            chip.AddToClassList("hs-color__chip");
            chip.pickingMode = PickingMode.Ignore;
            mixedMark = new Label("—") { pickingMode = PickingMode.Ignore };
            mixedMark.AddToClassList("hs-color__mixed");
            Button.Add(chip);
            Button.Add(mixedMark);
            Add(Button);
        }

        public void SetDisplay(Color c, bool mixed)
        {
            Value = c;
            var shown = c;
            if (!Alpha) shown.a = 1;
            chip.style.backgroundColor = shown;
            mixedMark.style.display = mixed ? DisplayStyle.Flex : DisplayStyle.None;
            chip.style.opacity = mixed ? 0.25f : 1f;
        }
    }

    public sealed class ColorPicker : VisualElement
    {
        public const int RecentMax = 12, SetMax = 90, SetCount = 5;
        static readonly List<Color> recent = new List<Color>();
        static List<List<Color>> sets;
        public static string ColorsFile;

        readonly bool alpha, hdr;
        float h, s, v, a = 1, intensity;
        readonly Color before;
        readonly float beforeIntensity;
        readonly SvSquare square;
        readonly Strip hueStrip, alphaStrip;
        readonly IntegerField fh, fs, fv, fr, fg, fb, fa;
        readonly TextField hex;
        readonly FloatField fIntensity;
        readonly VisualElement preview;
        bool updating;
        // the typed fields: element, its live text, how a text is applied (the fields are delayed: Enter in a field must apply
        // the text before the picker closes; v0.2: hex + Enter did not close, and Esc in a field left the picker open)
        readonly List<(VisualElement field, Func<string> text, Action<string> apply)> typed = new List<(VisualElement, Func<string>, Action<string>)>();

        /// <summary>Live value (colour, HDR intensity) while editing.</summary>
        public event Action<Color, float> Changed;
        /// <summary>true = commit, false = revert.</summary>
        public event Action<bool> Closed;

        public ColorPicker(Color start, bool editAlpha, bool hdrIntensity = false, float startIntensity = 0)
        {
            name = "color-picker";
            AddToClassList("hs-colorpicker");
            alpha = editAlpha; hdr = hdrIntensity;
            before = start; beforeIntensity = startIntensity;
            Color.RGBToHSV(start, out h, out s, out v);
            a = editAlpha ? start.a : 1f;
            intensity = startIntensity;

            var top = new VisualElement(); top.AddToClassList("hs-colorpicker__top");
            square = new SvSquare(this) { name = "sv" };
            hueStrip = new Strip(this, false) { name = "hue" };
            alphaStrip = new Strip(this, true) { name = "alpha" };
            top.Add(square); top.Add(hueStrip);
            if (alpha) top.Add(alphaStrip);
            Add(top);

            var nums = new VisualElement(); nums.AddToClassList("hs-colorpicker__fields");
            IntegerField Num(string lbl, int max, Action<int> set)
            {
                var f = new IntegerField(lbl) { isDelayed = true };
                f.AddToClassList("hs-colorpicker__num");
                f.RegisterValueChangedCallback(e => { if (updating || IsClosed) return; set(Mathf.Clamp(e.newValue, 0, max)); Push(); });
                typed.Add((f, () => f.text, t => { if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { set(Mathf.Clamp(n, 0, max)); Push(); } }));
                nums.Add(f);
                return f;
            }
            fh = Num("H", 360, x => h = x / 360f);
            fs = Num("S", 100, x => s = x / 100f);
            fv = Num("V", 100, x => v = x / 100f);
            fr = Num("R", 255, x => SetRgb(0, x));
            fg = Num("G", 255, x => SetRgb(1, x));
            fb = Num("B", 255, x => SetRgb(2, x));
            fa = Num("A", 100, x => a = x / 100f);
            if (!alpha) fa.style.display = DisplayStyle.None;
            Add(nums);
            hex = new TextField("#") { isDelayed = true, name = "hex" };
            hex.RegisterValueChangedCallback(e => { if (!updating && !IsClosed) ApplyHex(e.newValue); });
            typed.Add((hex, () => hex.text, ApplyHex));
            Add(hex);
            if (hdr)
            {
                fIntensity = new FloatField("HDR") { isDelayed = true, name = "intensity" };
                fIntensity.RegisterValueChangedCallback(e => { if (updating || IsClosed) return; intensity = e.newValue; Push(); });
                typed.Add((fIntensity, () => fIntensity.text, t => { if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) { intensity = x; Push(); } }));
                Add(fIntensity);
            }
            preview = new VisualElement(); preview.AddToClassList("hs-colorpicker__preview");
            Add(preview);
            var sw = new VisualElement(); sw.AddToClassList("hs-colorpicker__swatches");
            foreach (var c in recent) sw.Add(SwatchButton(c));
            LoadSets();
            if (sets.Count > 0) foreach (var c in sets[0]) sw.Add(SwatchButton(c));
            Add(sw);
            var buttons = new VisualElement(); buttons.AddToClassList("hs-colorpicker__buttons");
            var add = new Button(() => { AddToSet(Current); }) { text = "+", name = "add-swatch" };
            var ok = new Button(() => Close(true)) { text = HS2Next.UI.Core.L10n.Get("common.ok"), name = "ok" };
            var cancel = new Button(() => Close(false)) { text = HS2Next.UI.Core.L10n.Get("common.cancel"), name = "cancel" };
            buttons.Add(add); buttons.Add(ok); buttons.Add(cancel);
            Add(buttons);
            Refresh();
            focusable = true;
            // trickle-down: the picker sees Enter / Esc before a focused field takes them (the shortcut router leaves Esc in a
            // field to the picker)
            RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        }

        void OnKey(KeyDownEvent e)
        {
            bool enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (!enter && e.keyCode != KeyCode.Escape) return;
            if (Composing()) return;   // the IME's Enter / Esc
            if (enter) { CommitTyped(e.target as VisualElement); Close(true); }
            else Close(false);
            e.StopPropagation();
            focusController?.IgnoreEvent(e);
        }

        static bool Composing()
        {
            try { return !string.IsNullOrEmpty(UnityEngine.Input.compositionString); }
            catch (InvalidOperationException) { return false; }   // Input System only
        }

        /// <summary>Applies the text of the typed field that holds <paramref name="target"/> (Enter before the field committed).</summary>
        void CommitTyped(VisualElement target)
        {
            if (target == null) return;
            foreach (var (field, text, apply) in typed)
                if (field == target || field.Contains(target)) { apply(text() ?? ""); return; }
        }

        void ApplyHex(string t)
        {
            string x = (t ?? "").Trim().TrimStart('#');
            if (ColorUtility.TryParseHtmlString("#" + x, out var c)) { Color.RGBToHSV(c, out h, out s, out v); if (alpha && x.Length == 8) a = c.a; Push(); }
        }

        Button SwatchButton(Color c)
        {
            var b = new Button(() => { Color.RGBToHSV(c, out h, out s, out v); if (alpha) a = c.a; Push(); });
            b.AddToClassList("hs-colorpicker__swatch");
            b.style.backgroundColor = new Color(c.r, c.g, c.b, 1);
            return b;
        }

        public Color Current { get { var c = Color.HSVToRGB(h, s, v); c.a = alpha ? a : 1; return c; } }
        public float Intensity => intensity;
        public bool IsClosed { get; private set; }

        void SetRgb(int i, int x)
        {
            var c = Current;
            c[i] = x / 255f;
            float aa = c.a;
            Color.RGBToHSV(c, out h, out s, out v);
            a = aa;
        }

        internal void SetSv(float ns, float nv) { s = Mathf.Clamp01(ns); v = Mathf.Clamp01(nv); Push(); }
        internal void SetStrip(bool isAlpha, float t) { if (isAlpha) a = Mathf.Clamp01(t); else h = Mathf.Clamp01(t); Push(); }
        internal float Hue => h;
        internal float Sat => s;
        internal float Val => v;
        internal float A => a;

        void Push()
        {
            if (IsClosed) return;   // a delayed field committing on the blur after Close: the edit has ended
            Refresh();
            Changed?.Invoke(Current, intensity);
        }

        void Refresh()
        {
            updating = true;
            var c = Current;
            fh.SetValueWithoutNotify(Mathf.RoundToInt(h * 360)); fs.SetValueWithoutNotify(Mathf.RoundToInt(s * 100)); fv.SetValueWithoutNotify(Mathf.RoundToInt(v * 100));
            fr.SetValueWithoutNotify(Mathf.RoundToInt(c.r * 255)); fg.SetValueWithoutNotify(Mathf.RoundToInt(c.g * 255)); fb.SetValueWithoutNotify(Mathf.RoundToInt(c.b * 255));
            fa.SetValueWithoutNotify(Mathf.RoundToInt(a * 100));
            hex.SetValueWithoutNotify(alpha ? ColorUtility.ToHtmlStringRGBA(c) : ColorUtility.ToHtmlStringRGB(c));
            fIntensity?.SetValueWithoutNotify(intensity);
            preview.style.backgroundColor = new Color(c.r, c.g, c.b, 1);
            square.MarkDirtyRepaint(); hueStrip.MarkDirtyRepaint(); alphaStrip.MarkDirtyRepaint();
            updating = false;
        }

        /// <summary>Ends the edit: commit (adds the colour to the recent list) or revert (the callback restores the start value).</summary>
        public void Close(bool commit)
        {
            if (IsClosed) return;
            IsClosed = true;
            if (commit)
            {
                var c = Current;
                recent.RemoveAll(x => x == c);
                recent.Insert(0, c);
                if (recent.Count > RecentMax) recent.RemoveAt(recent.Count - 1);
            }
            else Changed?.Invoke(before, beforeIntensity);
            RemoveFromHierarchy();
            Closed?.Invoke(commit);
        }

        static void LoadSets()
        {
            if (sets != null) return;
            sets = new List<List<Color>>();
            try
            {
                if (ColorsFile != null && File.Exists(ColorsFile))
                {
                    var o = JObject.Parse(File.ReadAllText(ColorsFile));
                    if (o["sets"] is JArray arr)
                        foreach (var set in arr)
                        {
                            var l = new List<Color>();
                            if (set is JArray cs)
                                foreach (var c in cs)
                                    if (c.Type == JTokenType.String && ColorUtility.TryParseHtmlString("#" + (string)c, out var col)) l.Add(col);
                            sets.Add(l);
                        }
                }
            }
            catch (Exception e) { Debug.LogWarning("[ui] colors.json unreadable: " + e.Message); }
            while (sets.Count < SetCount) sets.Add(new List<Color>());
        }

        static void AddToSet(Color c)
        {
            LoadSets();
            var s0 = sets[0];
            if (s0.Count >= SetMax) s0.RemoveAt(0);
            s0.Add(c);
            if (ColorsFile == null) return;
            try
            {
                var arr = new JArray();
                foreach (var set in sets) { var a = new JArray(); foreach (var x in set) a.Add(ColorUtility.ToHtmlStringRGBA(x)); arr.Add(a); }
                Directory.CreateDirectory(Path.GetDirectoryName(ColorsFile));
                File.WriteAllText(ColorsFile, new JObject { ["version"] = 1, ["sets"] = arr }.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[ui] colors.json not written: " + e.Message); }
        }

        /// <summary>The saturation / value square (drawn as a coloured grid).</summary>
        sealed class SvSquare : VisualElement
        {
            readonly ColorPicker p;
            public SvSquare(ColorPicker p)
            {
                this.p = p;
                AddToClassList("hs-colorpicker__sv");
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(e => { this.CapturePointer(e.pointerId); Pick(e.localPosition); });
                RegisterCallback<PointerMoveEvent>(e => { if (this.HasPointerCapture(e.pointerId)) Pick(e.localPosition); });
                RegisterCallback<PointerUpEvent>(e => { if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId); });
            }
            void Pick(Vector2 lp) => p.SetSv(lp.x / Mathf.Max(1, layout.width), 1 - lp.y / Mathf.Max(1, layout.height));
            void Draw(MeshGenerationContext mgc)
            {
                const int n = 16;
                float w = layout.width, ht = layout.height;
                if (w <= 0 || ht <= 0) return;
                var mesh = mgc.Allocate((n + 1) * (n + 1), n * n * 6);
                for (int y = 0; y <= n; y++)
                    for (int x = 0; x <= n; x++)
                    {
                        var c = Color.HSVToRGB(p.Hue, x / (float)n, 1 - y / (float)n);
                        mesh.SetNextVertex(new Vertex { position = new Vector3(x * w / n, y * ht / n, Vertex.nearZ), tint = c });
                    }
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        ushort i0 = (ushort)(y * (n + 1) + x), i1 = (ushort)(i0 + 1), i2 = (ushort)(i0 + n + 1), i3 = (ushort)(i2 + 1);
                        mesh.SetNextIndex(i0); mesh.SetNextIndex(i1); mesh.SetNextIndex(i2);
                        mesh.SetNextIndex(i1); mesh.SetNextIndex(i3); mesh.SetNextIndex(i2);
                    }
                var pen = mgc.painter2D;
                pen.strokeColor = p.Val > 0.5f ? Color.black : Color.white;
                pen.lineWidth = 1.5f;
                pen.BeginPath();
                pen.Arc(new Vector2(p.Sat * w, (1 - p.Val) * ht), 5, 0, 360);
                pen.Stroke();
            }
        }

        /// <summary>The hue (vertical, top = 0) or alpha strip.</summary>
        sealed class Strip : VisualElement
        {
            readonly ColorPicker p;
            readonly bool isAlpha;
            public Strip(ColorPicker p, bool isAlpha)
            {
                this.p = p; this.isAlpha = isAlpha;
                AddToClassList("hs-colorpicker__strip");
                generateVisualContent += Draw;
                RegisterCallback<PointerDownEvent>(e => { this.CapturePointer(e.pointerId); Pick(e.localPosition); });
                RegisterCallback<PointerMoveEvent>(e => { if (this.HasPointerCapture(e.pointerId)) Pick(e.localPosition); });
                RegisterCallback<PointerUpEvent>(e => { if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId); });
            }
            void Pick(Vector2 lp) { float t = lp.y / Mathf.Max(1, layout.height); p.SetStrip(isAlpha, isAlpha ? 1 - t : t); }
            void Draw(MeshGenerationContext mgc)
            {
                const int n = 24;
                float w = layout.width, ht = layout.height;
                if (w <= 0 || ht <= 0) return;
                var mesh = mgc.Allocate((n + 1) * 2, n * 6);
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    var c = isAlpha ? new Color(1, 1, 1, 1 - t) : Color.HSVToRGB(t, 1, 1);
                    if (isAlpha) { var b = Color.HSVToRGB(p.Hue, p.Sat, p.Val); c = new Color(b.r, b.g, b.b, 1 - t); }
                    mesh.SetNextVertex(new Vertex { position = new Vector3(0, t * ht, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(w, t * ht, Vertex.nearZ), tint = c });
                }
                for (int i = 0; i < n; i++)
                {
                    ushort a0 = (ushort)(i * 2), a1 = (ushort)(a0 + 1), b0 = (ushort)(a0 + 2), b1 = (ushort)(a0 + 3);
                    mesh.SetNextIndex(a0); mesh.SetNextIndex(a1); mesh.SetNextIndex(b0);
                    mesh.SetNextIndex(a1); mesh.SetNextIndex(b1); mesh.SetNextIndex(b0);
                }
                float y = (isAlpha ? 1 - p.A : p.Hue) * ht;
                var pen = mgc.painter2D;
                pen.strokeColor = Color.white; pen.lineWidth = 2;
                pen.BeginPath(); pen.MoveTo(new Vector2(0, y)); pen.LineTo(new Vector2(w, y)); pen.Stroke();
            }
        }
    }
}
