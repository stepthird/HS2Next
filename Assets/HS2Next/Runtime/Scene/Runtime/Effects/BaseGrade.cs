// HS2 Studio's hidden base grade, read at run time from the user's install (E7;
// Phase 3.3 package B). HS2's two Studio post-processing volumes (PostProcessVolume, priority 0, weight 1; and
// PostProcessVolumeColor, priority 1, weight = the scene's blend) carry PPv2 ColorGrading settings with overridden lift /
// gamma / gain, master and lum-vs-sat curves (Illusion data: never shipped). This file
//   - reads them with Hs2SerializedFile: the volumes in StudioNEOV2_Data/level2 (script-object layout: profile PPtr,
//     isGlobal, blend distance, weight, priority; exact size), their profiles and ColorGrading settings in
//     sharedassets2.assets (script class checked through globalgamemanagers.assets when it resolves; the ColorGrading
//     field layout must end exactly at the object's size — each spline carries its 128-float cachedData after m_Range);
//     when level2 does not give two volumes and every ColorGrading object of sharedassets2 is identical, that one is used
//     for both;
//   - resolves the constants PPv2's LDR baker gets for a colour-volume weight (Resolve): every parameter from its default,
//     then the main volume's override at weight 1, then the colour volume's at the blend weight; lift / gamma / gain
//     split around Rec.709 luma (lift = c − luma(c) + w, gain = c − luma + w + 1, invGamma = 1 / max(c − luma + w + 1,
//     1e-3)); curves sampled at i / 128 (Hermite on the keys, looping copies for the hue curves) and rounded to half;
//   - fails closed: any error gives Neutral (identity LUT, neutral lift / gamma / gain, linear curves skipped) and the
//     reason (SceneEffects reports effects.baseGradeUnavailable once).
// The result is recorded in <cache>/effects/base_grade.json (file sizes, SHA-256, the parsed values; diagnostics and
// tests, never read back: parsing takes a few milliseconds, less than hashing the 48 MB file).
// The behaviour follows Tools/survey/studio_grade.py (package H's Python reference); no PPv2 code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using HS2Next.Content;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>An overridable parameter of a ColorGrading settings object (PPv2's overrideState + value).</summary>
    public struct GradeParam
    {
        public bool Override;
        /// <summary>1 value for floats / ints, 4 for colours and vectors.</summary>
        public float[] Value;
        public float F => Value != null && Value.Length > 0 ? Value[0] : 0f;
    }

    /// <summary>A PPv2 Spline: an AnimationCurve's keys plus the loop / zero / range settings.</summary>
    public sealed class GradeCurve
    {
        public bool Override;
        /// <summary>Keys as (time, value, inTangent, outTangent, weightedMode, inWeight, outWeight).</summary>
        public List<float[]> Keys = new List<float[]>();
        public int PreInfinity, PostInfinity, RotationOrder;
        public bool Loop;
        public float ZeroValue, Range = 1f;
        /// <summary>The serialized 128-sample cache (diagnostics: equals <see cref="Samples"/> within float error).</summary>
        public float[] CachedData;

        public static GradeCurve Linear() => new GradeCurve { Keys = { new float[] { 0, 0, 1, 1, 0, 0, 0 }, new float[] { 1, 1, 1, 1, 0, 0, 0 } }, ZeroValue = 0f };
        public static GradeCurve Flat(bool loop) => new GradeCurve { ZeroValue = 0.5f, Loop = loop };

        /// <summary>128 samples at i / 128 (no keys: the zero value).</summary>
        public double[] Samples()
        {
            var r = new double[128];
            if (Keys.Count == 0) { for (int i = 0; i < 128; i++) r[i] = ZeroValue; return r; }
            var keys = Keys;
            if (Loop && keys.Count > 1)
            {
                keys = new List<float[]>(keys.Count + 2);
                var a = (float[])Keys[Keys.Count - 1].Clone(); a[0] -= Range;
                var b = (float[])Keys[0].Clone(); b[0] += Range;
                keys.Add(a); keys.AddRange(Keys); keys.Add(b);
                keys.Sort((x, y) => x[0].CompareTo(y[0]));
            }
            for (int i = 0; i < 128; i++) r[i] = Evaluate(keys, (float)i * (1f / 128f));
            return r;
        }

        /// <summary>Unity's non-weighted Hermite evaluation, clamped outside the keys.</summary>
        static double Evaluate(List<float[]> keys, double t)
        {
            if (t <= keys[0][0]) return keys[0][1];
            if (t >= keys[keys.Count - 1][0]) return keys[keys.Count - 1][1];
            for (int i = 0; i + 1 < keys.Count; i++)
            {
                var a = keys[i]; var b = keys[i + 1];
                if (!(a[0] <= t && t <= b[0])) continue;
                double dx = b[0] - a[0];
                if (dx <= 0) return a[1];
                if (a[4] != 0 || b[4] != 0) throw new Hs2SerializedFileException("weighted curve keys are not supported");
                double m0 = a[3] * dx, m1 = b[2] * dx;
                if (double.IsInfinity(m0) || double.IsInfinity(m1)) return a[1];
                double s = (t - a[0]) / dx, s2 = s * s, s3 = s2 * s;
                return (2 * s3 - 3 * s2 + 1) * a[1] + (s3 - 2 * s2 + s) * m0 + (s3 - s2) * m1 + (-2 * s3 + 3 * s2) * b[1];
            }
            return keys[keys.Count - 1][1];
        }
    }

    /// <summary>The PPv2 ColorGrading settings of one volume's profile (the parameters the LDR baker reads).</summary>
    public sealed class ColorGradingSettings
    {
        public GradeParam GradingMode, LdrLutContribution, Temperature, Tint, ColorFilter, HueShift, Saturation, Brightness, PostExposure, Contrast, Lift, Gamma, Gain;
        public Hs2PPtr LdrLut;
        public GradeParam[] Mixer = new GradeParam[9];
        /// <summary>master, red, green, blue, hueVsHue, hueVsSat, satVsSat, lumVsSat.</summary>
        public GradeCurve[] Curves = new GradeCurve[8];
        /// <summary>Path id of the settings object in sharedassets2 (diagnostics).</summary>
        public long PathId;
        /// <summary>The object's bytes after the MonoBehaviour header (equality of two volumes' settings).</summary>
        public byte[] Body;

        public static readonly string[] CurveNames = { "master", "red", "green", "blue", "hueVsHue", "hueVsSat", "satVsSat", "lumVsSat" };

        /// <summary>Parses a ColorGrading settings object (Unity 2018.4 / PPv2 field order); throws unless it ends exactly at the object's size.</summary>
        public static ColorGradingSettings Parse(byte[] b, long pathId)
        {
            var r = new Hs2FieldReader(b, 0);
            Hs2SerializedFile.ReadMonoBehaviourHeader(r, out _, out _, out _, out string name);
            int bodyStart = r.Position;
            var s = new ColorGradingSettings { PathId = pathId };
            r.Bool();                      // active
            r.Bool(); r.Bool();            // enabled (override, value)
            s.GradingMode = Int(r);
            Ptr(r);                        // externalLut
            r.I32();                       // externalLut default
            Int(r);                        // tonemapper
            for (int i = 0; i < 6; i++) Flt(r);   // toe / shoulder / gamma (HDR only)
            r.Bool(); s.LdrLut = r.PPtr();
            r.I32();                       // ldrLut default
            s.LdrLutContribution = Flt(r);
            s.Temperature = Flt(r);
            s.Tint = Flt(r);
            s.ColorFilter = Vec(r);
            s.HueShift = Flt(r);
            s.Saturation = Flt(r);
            s.Brightness = Flt(r);
            s.PostExposure = Flt(r);
            s.Contrast = Flt(r);
            for (int i = 0; i < 9; i++) s.Mixer[i] = Flt(r);
            s.Lift = Vec(r);
            s.Gamma = Vec(r);
            s.Gain = Vec(r);
            for (int i = 0; i < 8; i++) s.Curves[i] = Spline(r);
            r.ExpectEnd("ColorGrading \"" + name + "\" (" + pathId + ")");
            s.Body = new byte[b.Length - bodyStart];
            Buffer.BlockCopy(b, bodyStart, s.Body, 0, s.Body.Length);
            return s;
        }

        static GradeParam Flt(Hs2FieldReader r) { bool o = r.Bool(); return new GradeParam { Override = o, Value = new[] { r.F32() } }; }
        static GradeParam Int(Hs2FieldReader r) { bool o = r.Bool(); return new GradeParam { Override = o, Value = new float[] { r.I32() } }; }
        static GradeParam Vec(Hs2FieldReader r) { bool o = r.Bool(); return new GradeParam { Override = o, Value = r.F32s(4) }; }
        static void Ptr(Hs2FieldReader r) { r.Bool(); r.PPtr(); }

        static GradeCurve Spline(Hs2FieldReader r)
        {
            var c = new GradeCurve { Override = r.Bool() };
            int n = r.I32();
            if (n < 0 || n > 4096) throw new Hs2SerializedFileException("curve key count " + n);
            c.Keys.Clear();
            for (int i = 0; i < n; i++)
            {
                float t = r.F32(), v = r.F32(), ti = r.F32(), to = r.F32();
                int wm = r.I32();
                float wi = r.F32(), wo = r.F32();
                c.Keys.Add(new[] { t, v, ti, to, wm, wi, wo });
            }
            c.PreInfinity = r.I32(); c.PostInfinity = r.I32(); c.RotationOrder = r.I32();
            c.Loop = r.Bool();
            c.ZeroValue = r.F32();
            c.Range = r.F32();
            int cn = r.I32();
            if (cn < 0 || cn > 4096) throw new Hs2SerializedFileException("curve cache count " + cn);
            c.CachedData = r.F32s(cn);
            return c;
        }
    }

    /// <summary>The constants of one bake (PPv2's LDR baker inputs for one colour-volume weight).</summary>
    public sealed class GradeProfile
    {
        public float[] ColorBalance = { 1, 1, 1 }, ColorFilter = { 1, 1, 1 };
        /// <summary>Row-major 3 × 3 (out r = row 0 · in).</summary>
        public float[] Mixer = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        /// <summary>Hue shift in turns (degrees / 360).</summary>
        public float HueShift;
        public float[] Lift = { 0, 0, 0 }, InvGamma = { 1, 1, 1 }, Gain = { 1, 1, 1 };
        /// <summary>The 8 curve tables (128 samples, half precision), <see cref="ColorGradingSettings.CurveNames"/> order.</summary>
        public float[][] Curves = new float[8][];
        /// <summary>The main volume's ldrLut contribution.</summary>
        public float Contribution = 1f;
        /// <summary>The neutral grade: curves are not applied at all (identity), not sampled.</summary>
        public bool Neutral;
        /// <summary>Where each constant came from ("default", "volume0", "volume0+volume1").</summary>
        public Dictionary<string, string> Sources = new Dictionary<string, string>();

        public bool BalanceIdentity => ColorBalance[0] == 1f && ColorBalance[1] == 1f && ColorBalance[2] == 1f;
        public bool FilterIdentity => ColorFilter[0] == 1f && ColorFilter[1] == 1f && ColorFilter[2] == 1f;
        public bool MixerIdentity
        {
            get { for (int i = 0; i < 9; i++) if (Mixer[i] != (i % 4 == 0 ? 1f : 0f)) return false; return true; }
        }
        public bool LggIdentity
        {
            get { for (int i = 0; i < 3; i++) if (Lift[i] != 0f || InvGamma[i] != 1f || Gain[i] != 1f) return false; return true; }
        }
    }

    public sealed class BaseGrade
    {
        /// <summary>"install" (read from the user's HS2), "neutral" (not available).</summary>
        public string Source { get; private set; } = "neutral";
        public bool Ok => Source == "install";
        /// <summary>Why the reader failed (Neutral), else null.</summary>
        public string Error { get; private set; }
        /// <summary>How the volumes were found: "level2" (both volumes and their priorities) or "identical" (one settings object).</summary>
        public string Route { get; private set; }
        /// <summary>The main and the colour volume's settings (Ok only).</summary>
        public ColorGradingSettings Main { get; private set; }
        public ColorGradingSettings Color { get; private set; }
        public float MainPriority { get; private set; }
        public float ColorPriority { get; private set; } = 1f;
        public double Milliseconds { get; private set; }
        /// <summary>Path id of the main volume's ldrLut texture in sharedassets2 (Lut_Default on vanilla installs).</summary>
        public long MainLdrLutPathId => Main != null ? Main.LdrLut.PathId : 0;

        public static readonly BaseGrade Neutral = new BaseGrade { Source = "neutral", Error = "not read" };

        public static BaseGrade Unavailable(string why) => new BaseGrade { Source = "neutral", Error = why };

        /// <summary>Reads the base grade of the HS2 install at <paramref name="hs2Root"/>; never throws (Neutral with the error).</summary>
        public static BaseGrade Read(string hs2Root)
        {
            var sw = Stopwatch.StartNew();
            BaseGrade g;
            try
            {
                if (string.IsNullOrEmpty(hs2Root)) throw new Hs2SerializedFileException("no HS2 install root");
                string data = Path.Combine(hs2Root, "StudioNEOV2_Data");
                string shared = Path.Combine(data, "sharedassets2.assets");
                if (!File.Exists(shared)) throw new Hs2SerializedFileException("StudioNEOV2_Data/sharedassets2.assets not found");
                g = ReadFiles(shared, Path.Combine(data, "level2"));
            }
            catch (Exception e) when (e is Hs2SerializedFileException || e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException)
            {
                g = Unavailable(e.Message);
            }
            g.Milliseconds = sw.Elapsed.TotalMilliseconds;
            return g;
        }

        static BaseGrade ReadFiles(string sharedPath, string level2Path)
        {
            var shared = Hs2SerializedFile.Open(sharedPath);
            var mbs = new List<Hs2SerializedObject>();
            foreach (var o in shared.Objects) if (o.ClassId == Hs2SerializedFile.ClassMonoBehaviour) mbs.Add(o);
            var bytes = shared.ReadBytes(mbs);

            // MonoScripts (class check) live in globalgamemanagers.assets on HS2; resolved when it opens.
            var scriptFiles = new Dictionary<string, Hs2SerializedFile>(StringComparer.OrdinalIgnoreCase);
            string ClassOf(Hs2SerializedFile f, Hs2PPtr script)
            {
                try
                {
                    Hs2SerializedFile sf = f;
                    if (script.FileId != 0)
                    {
                        var ext = f.ExternalOf(script);
                        if (ext == null) return null;
                        string p = Path.Combine(Path.GetDirectoryName(f.FilePath) ?? "", ext.FileName);
                        if (!scriptFiles.TryGetValue(p, out sf))
                        {
                            sf = File.Exists(p) ? Hs2SerializedFile.Open(p) : null;
                            scriptFiles[p] = sf;
                        }
                        if (sf == null) return null;
                    }
                    if (!sf.TryFind(script.PathId, out var so) || so.ClassId != Hs2SerializedFile.ClassMonoScript) return null;
                    return Hs2SerializedFile.TryReadMonoScript(sf.ReadBytes(so), out string cls, out string ns) ? (string.IsNullOrEmpty(ns) ? cls : ns + "." + cls) : null;
                }
                catch (Hs2SerializedFileException) { return null; }
                catch (IOException) { return null; }
            }

            const string CgClass = "UnityEngine.Rendering.PostProcessing.ColorGrading";
            var grading = new Dictionary<long, ColorGradingSettings>();
            foreach (var o in mbs)
            {
                var b = bytes[o.PathId];
                string name;
                Hs2PPtr script;
                try
                {
                    var hr = new Hs2FieldReader(b, 0);
                    Hs2SerializedFile.ReadMonoBehaviourHeader(hr, out _, out _, out script, out name);
                }
                catch (Hs2SerializedFileException) { continue; }   // not a script object with a name
                if (name != "ColorGrading") continue;
                string cls = ClassOf(shared, script);
                if (cls != null && cls != CgClass) continue;
                // a ColorGrading settings object that does not parse exactly: fail closed (no partial base grade)
                grading[o.PathId] = ColorGradingSettings.Parse(b, o.PathId);
            }
            if (grading.Count == 0) throw new Hs2SerializedFileException("no ColorGrading settings object in sharedassets2.assets");

            // Volumes from level2 → profile → its ColorGrading settings.
            var vols = new List<(float priority, ColorGradingSettings cg)>();
            string volError = null;
            try
            {
                if (!File.Exists(level2Path)) throw new Hs2SerializedFileException("level2 not found");
                var level = Hs2SerializedFile.Open(level2Path);
                string sharedName = Path.GetFileName(sharedPath).ToLowerInvariant();
                var lmbs = new List<Hs2SerializedObject>();
                foreach (var o in level.Objects) if (o.ClassId == Hs2SerializedFile.ClassMonoBehaviour && o.Size >= 60 && o.Size <= 200) lmbs.Add(o);
                var lbytes = level.ReadBytes(lmbs);
                foreach (var o in lmbs)
                {
                    try
                    {
                        var r = new Hs2FieldReader(lbytes[o.PathId], 0);
                        Hs2SerializedFile.ReadMonoBehaviourHeader(r, out _, out _, out _, out string name);
                        var prof = r.PPtr();
                        r.Bool();               // isGlobal
                        r.F32();                // blend distance
                        r.F32();                // weight
                        float priority = r.F32();
                        r.ExpectEnd("volume");
                        if (prof.IsNull) continue;
                        var ext = level.ExternalOf(prof);
                        if (prof.FileId != 0 && (ext == null || ext.FileName != sharedName)) continue;
                        if (prof.FileId == 0 || !shared.TryFind(prof.PathId, out var po) || po.ClassId != Hs2SerializedFile.ClassMonoBehaviour) continue;
                        var pr = new Hs2FieldReader(bytes[po.PathId], 0);
                        Hs2SerializedFile.ReadMonoBehaviourHeader(pr, out _, out _, out _, out _);
                        int n = pr.I32();
                        if (n < 0 || n > 256) continue;
                        ColorGradingSettings found = null;
                        for (int i = 0; i < n; i++)
                        {
                            var sp = pr.PPtr();
                            if (sp.FileId == 0 && grading.TryGetValue(sp.PathId, out var cg)) found = cg;
                        }
                        pr.ExpectEnd("profile");
                        if (found != null) vols.Add((priority, found));
                    }
                    catch (Hs2SerializedFileException) { }
                }
                if (vols.Count != 2) throw new Hs2SerializedFileException("level2 has " + vols.Count + " volumes with a ColorGrading profile (2 expected)");
            }
            catch (Hs2SerializedFileException e) { volError = e.Message; vols.Clear(); }
            catch (IOException e) { volError = e.Message; vols.Clear(); }

            var g = new BaseGrade { Source = "install" };
            if (vols.Count == 2)
            {
                vols.Sort((a, b) => a.priority.CompareTo(b.priority));
                g.Main = vols[0].cg; g.MainPriority = vols[0].priority;
                g.Color = vols[1].cg; g.ColorPriority = vols[1].priority;
                g.Route = "level2";
            }
            else
            {
                ColorGradingSettings first = null;
                foreach (var cg in grading.Values)
                {
                    if (first == null) { first = cg; continue; }
                    if (!SameBytes(first.Body, cg.Body)) throw new Hs2SerializedFileException("volumes not found (" + volError + ") and the " + grading.Count + " ColorGrading objects differ");
                }
                g.Main = g.Color = first;
                g.Route = "identical";
            }
            return g;
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ------------------------------------------------------------ resolution for one blend

        static float Lerp(float a, float b, float w) => a + (b - a) * w;

        /// <summary>
        /// The constants of one bake at colour-volume weight <paramref name="blend"/> (clamped to 0–1): each parameter from
        /// its default, the main volume's override at weight 1, then the colour volume's at the blend. Neutral when not Ok.
        /// </summary>
        public GradeProfile Resolve(float blend)
        {
            var p = new GradeProfile();
            if (!Ok)
            {
                p.Neutral = true;
                for (int i = 0; i < 8; i++) p.Curves[i] = null;
                p.Sources["all"] = "neutral";
                return p;
            }
            float w1 = blend <= 0f ? 0f : blend >= 1f ? 1f : blend;
            var vols = new[] { (Main, 1f), (Color, w1) };

            float Scalar(string name, Func<ColorGradingSettings, GradeParam> sel, float dflt)
            {
                float v = dflt; string src = "default";
                foreach (var (s, w) in vols)
                {
                    var prm = sel(s);
                    if (!prm.Override || w <= 0f) continue;
                    v = Lerp(v, prm.F, w);
                    src = src == "default" ? (s == Main ? "volume0" : "volume1") : src + "+volume1";
                }
                p.Sources[name] = src;
                return v;
            }
            float[] Vector(string name, Func<ColorGradingSettings, GradeParam> sel, float[] dflt)
            {
                var v = (float[])dflt.Clone(); string src = "default";
                foreach (var (s, w) in vols)
                {
                    var prm = sel(s);
                    if (!prm.Override || w <= 0f) continue;
                    for (int i = 0; i < v.Length; i++) v[i] = Lerp(v[i], prm.Value[i], w);
                    src = src == "default" ? (s == Main ? "volume0" : "volume1") : src + "+volume1";
                }
                p.Sources[name] = src;
                return v;
            }

            float temperature = Scalar("temperature", s => s.Temperature, 0f);
            float tint = Scalar("tint", s => s.Tint, 0f);
            p.ColorBalance = ColorBalance(temperature, tint);
            var cf = Vector("colorFilter", s => s.ColorFilter, new float[] { 1, 1, 1, 1 });
            p.ColorFilter = new[] { cf[0], cf[1], cf[2] };
            p.HueShift = Scalar("hueShift", s => s.HueShift, 0f) / 360f;
            p.Contribution = Scalar("ldrLutContribution", s => s.LdrLutContribution, 1f);
            for (int i = 0; i < 9; i++)
            {
                int k = i;
                p.Mixer[i] = Scalar("mixer" + i, s => s.Mixer[k], i % 4 == 0 ? 100f : 0f) / 100f;
            }
            var lift = Vector("lift", s => s.Lift, new float[] { 1, 1, 1, 0 });
            var gamma = Vector("gamma", s => s.Gamma, new float[] { 1, 1, 1, 0 });
            var gain = Vector("gain", s => s.Gain, new float[] { 1, 1, 1, 0 });
            var L = Split(lift); var G = Split(gamma); var K = Split(gain);
            for (int i = 0; i < 3; i++)
            {
                p.Lift[i] = (float)(L[i] + lift[3]);
                p.InvGamma[i] = (float)(1.0 / Math.Max(G[i] + gamma[3] + 1.0, 1e-3));
                p.Gain[i] = (float)(K[i] + gain[3] + 1.0);
            }

            for (int c = 0; c < 8; c++)
            {
                var dflt = c < 4 ? GradeCurve.Linear() : GradeCurve.Flat(c == 4 || c == 5);
                var cur = dflt.Samples();
                string src = "default";
                foreach (var (s, w) in vols)
                {
                    var cv = s.Curves[c];
                    if (cv == null || !cv.Override || w <= 0f) continue;
                    var to = cv.Samples();
                    for (int i = 0; i < 128; i++) cur[i] = cur[i] + (to[i] - cur[i]) * w;
                    src = src == "default" ? (s == Main ? "volume0" : "volume1") : src + "+volume1";
                }
                var t = new float[128];
                for (int i = 0; i < 128; i++) t[i] = UnityEngine.Mathf.HalfToFloat(UnityEngine.Mathf.FloatToHalf((float)cur[i]));
                p.Curves[c] = t;
                p.Sources["curve." + ColorGradingSettings.CurveNames[c]] = src;
            }
            return p;
        }

        static double[] Split(float[] c)
        {
            double l = c[0] * 0.2126 + c[1] * 0.7152 + c[2] * 0.0722;
            return new[] { c[0] - l, c[1] - l, c[2] - l };
        }

        /// <summary>PPv2's white-balance coefficients for temperature / tint (1, 1, 1 within float error at 0 / 0).</summary>
        public static float[] ColorBalance(float temperature, float tint)
        {
            if (temperature == 0f && tint == 0f) return new float[] { 1, 1, 1 };   // the identity, exactly (the formula gives 1 ± 4e-6)
            double t1 = temperature / 60.0, t2 = tint / 60.0;
            double x = 0.31271 - t1 * (t1 < 0 ? 0.1 : 0.05);
            double y = 2.87 * x - 3 * x * x - 0.27509508 + t2 * 0.05;
            double X = x / y, Z = (1 - x - y) / y;
            double l = 0.7328 * X + 0.4296 - 0.1624 * Z, m = -0.7036 * X + 1.6975 + 0.0061 * Z, s = 0.003 * X + 0.0136 + 0.9834 * Z;
            return new[] { (float)(0.949237 / l), (float)(1.03542 / m), (float)(1.08728 / s) };
        }

        // ------------------------------------------------------------ record

        /// <summary>The record written to base_grade.json (and reported by effects.state with detail).</summary>
        public JObject ToJson(bool withCurves)
        {
            var o = new JObject { ["source"] = Source, ["ok"] = Ok, ["error"] = Error, ["route"] = Route, ["ms"] = Math.Round(Milliseconds, 2) };
            if (!Ok) return o;
            JObject Vol(ColorGradingSettings s, float prio)
            {
                JArray P(GradeParam g) => new JArray(g.Override, new JArray(g.Value));
                var v = new JObject
                {
                    ["pathId"] = s.PathId, ["priority"] = prio, ["ldrLut"] = s.LdrLut.ToString(),
                    ["ldrLutContribution"] = P(s.LdrLutContribution), ["temperature"] = P(s.Temperature), ["tint"] = P(s.Tint),
                    ["colorFilter"] = P(s.ColorFilter), ["hueShift"] = P(s.HueShift), ["saturation"] = P(s.Saturation),
                    ["brightness"] = P(s.Brightness), ["postExposure"] = P(s.PostExposure), ["contrast"] = P(s.Contrast),
                    ["lift"] = P(s.Lift), ["gamma"] = P(s.Gamma), ["gain"] = P(s.Gain),
                };
                var mix = new JArray(); foreach (var m in s.Mixer) mix.Add(P(m)); v["mixer"] = mix;
                var curves = new JObject();
                for (int i = 0; i < 8; i++)
                {
                    var c = s.Curves[i];
                    var keys = new JArray(); foreach (var k in c.Keys) keys.Add(new JArray(k));
                    var co = new JObject { ["override"] = c.Override, ["keys"] = keys, ["loop"] = c.Loop, ["zeroValue"] = c.ZeroValue, ["range"] = c.Range };
                    if (withCurves && c.CachedData != null) co["cachedData"] = new JArray(c.CachedData);
                    curves[ColorGradingSettings.CurveNames[i]] = co;
                }
                v["curves"] = curves;
                return v;
            }
            o["volumes"] = new JArray(Vol(Main, MainPriority), Vol(Color, ColorPriority));
            return o;
        }

        /// <summary>Writes <c>&lt;cacheRoot&gt;/effects/base_grade.json</c> with the install files' sizes and SHA-256 (best effort).</summary>
        public void WriteRecord(string hs2Root, string cacheRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(cacheRoot)) return;
                var o = ToJson(false);
                var files = new JObject();
                if (!string.IsNullOrEmpty(hs2Root))
                    foreach (var rel in new[] { "StudioNEOV2_Data/sharedassets2.assets", "StudioNEOV2_Data/level2" })
                    {
                        string p = Path.Combine(hs2Root, rel);
                        if (!File.Exists(p)) continue;
                        using (var fs = File.OpenRead(p))
                        using (var sha = SHA256.Create())
                            files[rel] = new JObject { ["size"] = fs.Length, ["sha256"] = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant() };
                    }
                o["files"] = files;
                o["written"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string dir = Path.Combine(cacheRoot, "effects");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "base_grade.json"), o.ToString());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
