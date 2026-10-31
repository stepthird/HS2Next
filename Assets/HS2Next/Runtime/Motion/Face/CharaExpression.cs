// Expression facade of one character for the Studio / UI layer:
// the game-side operations of `ChaControl` on the face (with the HS2Next validation /
// 9), the head-load sequence, the Studio state application and controls, the
// expression lists and the Studio UI index ↔ pattern id mapping, tongue visibility and the mouth
// shape mask, tears / cheek / eye highlight. The per-frame work is ExpressionStage.
//
// The persistent values live in `Status` (the face part of `ChaFileStatus`); the Studio-only
// values (`mouthOpen`, `lipSync`) are properties of this class, as `OICharInfo` holds them in the game.
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Motion.Face
{
    /// <summary>Face fields of `ChaFileStatus` with their code defaults.</summary>
    public sealed class FaceStatus
    {
        public int EyebrowPtn;
        public float EyebrowOpenMax = 1f;
        public int EyesPtn;
        public float EyesOpenMax = 1f;
        public bool EyesBlink = true;
        /// <summary>Stored and copied, never read.</summary>
        public bool EyesYure;
        public int MouthPtn;
        public float MouthOpenMin, MouthOpenMax = 1f;
        public bool MouthFixed;
        public bool MouthAdjustWidth = true;
        public byte TongueState;
        public bool DisableMouthShapeMask;
        public float HohoAkaRate, TearsRate;
        public bool HideEyesHighlight;

        public FaceStatus Clone() => (FaceStatus)MemberwiseClone();

        /// <summary>The face fields of a card's `Status` block (missing fields keep the code defaults).</summary>
        public static FaceStatus FromCard(object status)
        {
            var s = new FaceStatus();
            if (status == null) return s;
            s.EyebrowPtn = status.Get("eyebrowPtn").Int(s.EyebrowPtn);
            s.EyebrowOpenMax = status.Get("eyebrowOpenMax").Float(s.EyebrowOpenMax);
            s.EyesPtn = status.Get("eyesPtn").Int(s.EyesPtn);
            s.EyesOpenMax = status.Get("eyesOpenMax").Float(s.EyesOpenMax);
            s.EyesBlink = status.Get("eyesBlink").Bool(s.EyesBlink);
            s.EyesYure = status.Get("eyesYure").Bool(s.EyesYure);
            s.MouthPtn = status.Get("mouthPtn").Int(s.MouthPtn);
            s.MouthOpenMin = status.Get("mouthOpenMin").Float(s.MouthOpenMin);
            s.MouthOpenMax = status.Get("mouthOpenMax").Float(s.MouthOpenMax);
            s.MouthFixed = status.Get("mouthFixed").Bool(s.MouthFixed);
            s.MouthAdjustWidth = status.Get("mouthAdjustWidth").Bool(s.MouthAdjustWidth);
            s.TongueState = (byte)Mathf.Clamp(status.Get("tongueState").Int(s.TongueState), 0, 255);
            s.DisableMouthShapeMask = status.Get("disableMouthShapeMask").Bool(s.DisableMouthShapeMask);
            s.HohoAkaRate = status.Get("hohoAkaRate").Float(s.HohoAkaRate);
            s.TearsRate = status.Get("tearsRate").Float(s.TearsRate);
            s.HideEyesHighlight = status.Get("hideEyesHighlight").Bool(s.HideEyesHighlight);
            return s;
        }

        public override string ToString() =>
            $"eyebrow {EyebrowPtn} eyes {EyesPtn} open {EyesOpenMax:0.###} blink {EyesBlink} mouth {MouthPtn} open {MouthOpenMin:0.###}..{MouthOpenMax:0.###} fixed {MouthFixed} tears {TearsRate:0.###} cheek {HohoAkaRate:0.###}";
    }

    /// <summary>The expression lists of one sex: which pattern ids are offered, in list order.</summary>
    public sealed class ExpressionLists
    {
        // Row keys of custom_eyebrow_f/m, custom_eye_f/m, custom_mouth_f/m (identical for both sexes).
        public static readonly int[] DefaultEyebrow = { 0, 1, 2, 3, 5, 6, 10, 11, 14, 15 };
        public static readonly int[] DefaultEyes = { 0, 1, 2, 7, 8, 9, 11, 12, 13 };
        public static readonly int[] DefaultMouth = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 17, 18, 19, 20, 21, 22, 23 };

        public int[] Eyebrow = DefaultEyebrow, Eyes = DefaultEyes, Mouth = DefaultMouth;
        /// <summary>"catalog" (read from the loaded lists) or "built-in" (the spec tables).</summary>
        public string Source = "built-in";

        /// <summary>The lists of one sex: female 503 / 505 / 507, male 502 / 504 / 506, vanilla rows in ascending id order;
        /// a list the catalog does not have falls back to the built-in table.</summary>
        public static ExpressionLists For(ContentCatalog catalog, bool male)
        {
            var l = new ExpressionLists();
            if (catalog == null) return l;
            int[] Read(int cat, int[] def, ref bool any)
            {
                var ids = new List<int>();
                foreach (var e in catalog.EntriesOf(cat)) if (e.IsVanilla) ids.Add(e.Key.Id);
                if (ids.Count == 0) return def;
                ids.Sort();
                any = true;
                return ids.ToArray();
            }
            bool found = false;
            l.Eyebrow = Read(male ? 502 : 503, DefaultEyebrow, ref found);
            l.Eyes = Read(male ? 504 : 505, DefaultEyes, ref found);
            l.Mouth = Read(male ? 506 : 507, DefaultMouth, ref found);
            if (found) l.Source = "catalog";
            return l;
        }

        /// <summary>0-based position of an id in a list, −1 when unlisted.</summary>
        public static int Position(int[] keys, int id) => System.Array.IndexOf(keys, id);
    }

    public sealed class CharaExpression
    {
        /// <summary>Face sliders replaced by 0.5 while the mouth shape mask is on.</summary>
        public static readonly int[] MaskSliders = { 3, 5, 6, 10, 11, 12, 47, 48, 49, 50, 51, 52, 53 };

        public readonly ExpressionStage Stage;
        public readonly FaceMaterials Materials;
        public readonly ExpressionLists Lists;
        public readonly bool IsMale;
        /// <summary>The face part of `ChaFileStatus` (getters return these values).</summary>
        public FaceStatus Status { get; private set; } = new FaceStatus();

        readonly List<string> log;
        readonly GameObject headTongue;
        readonly List<GameObject> bodyTongues = new List<GameObject>();
        readonly ShapeEngine faceShape;
        float[] faceShapeValues;
        bool maskApplied;
        byte tongueApplied;

        /// <summary>Rejected pattern requests since creation (diagnostics).</summary>
        public int Rejections { get; private set; }

        public CharaExpression(ExpressionStage stage, FaceMaterials materials, ExpressionLists lists, CharaContext ctx)
        {
            Stage = stage; Materials = materials; Lists = lists ?? new ExpressionLists();
            stage.Api = this;
            IsMale = ctx != null && ctx.IsMale;
            log = ctx?.Log;
            if (ctx?.Head) headTongue = Find(ctx.Head.transform, "o_tang")?.gameObject;
            if (ctx?.Body) FindAll(ctx.Body.transform, "N_tang", bodyTongues);
            faceShape = ctx?.FaceShape;
            var fv = ctx?.Chara?.Face?.Shape ?? ctx?.Card?.Face.Get("shapeValueFace").Floats() ?? System.Array.Empty<float>();
            if (IsMale && fv.Length > 59) System.Array.Resize(ref fv, 59);
            faceShapeValues = new float[fv.Length];
            for (int i = 0; i < fv.Length; i++) faceShapeValues[i] = ShapeEngine.CardValue(fv[i], IsMale);   // as CharaAssembler.ApplyShapes
        }

        /// <summary>The facade of a character built by CharaAssembler (null when it has no expression stage).</summary>
        public static CharaExpression Of(GameObject chara)
        {
            if (!chara) return null;
            var m = chara.GetComponent<CharaMotion>();
            return m ? m.Get<ExpressionStage>()?.Api : null;
        }

        // ================================================================== game-side operations

        public bool ChangeEyebrowPtn(int p, bool blend = true)
        {
            if (!Stage.Eyebrow.ChangePtn(p, blend)) return Reject("eyebrow", p, Stage.Eyebrow.PatternCount);
            Status.EyebrowPtn = p;
            return true;
        }

        public void ChangeEyebrowOpenMax(float v)
        {
            v = Mathf.Clamp01(v);
            Status.EyebrowOpenMax = v;
            Stage.Eyebrow.OpenMax = v;
            if (!Status.EyesBlink) Stage.Eyebrow.OpenRate = 1f;   // 1, not v
        }

        public bool ChangeEyesPtn(int p, bool blend = true)
        {
            if (!Stage.Eyes.ChangePtn(p, blend)) return Reject("eyes", p, Stage.Eyes.PatternCount);
            Status.EyesPtn = p;
            return true;
        }

        /// <summary>With blinking off both the maximum and the openness become v, so the eyes show v².</summary>
        public void ChangeEyesOpenMax(float v)
        {
            v = Mathf.Clamp01(v);
            Status.EyesOpenMax = v;
            Stage.Eyes.OpenMax = v;
            if (!Status.EyesBlink) Stage.Eyes.OpenRate = v;
        }

        public void ChangeEyesBlinkFlag(bool on)
        {
            Status.EyesBlink = on;
            Stage.Blink.FixedFlags = (byte)(on ? 0 : 1);
            if (!on) { Stage.Eyes.OpenRate = 1f; Stage.Eyebrow.OpenRate = 1f; }
        }

        public bool ChangeMouthPtn(int p, bool blend = true)
        {
            if (!Stage.Mouth.ChangePtn(p, blend)) return Reject("mouth", p, Stage.Mouth.PatternCount);
            Status.MouthPtn = p;
            ChangeTongueState((byte)(p == 10 || p == 13 ? 1 : 0));
            Stage.Width.Enabled = !(p >= 9 && p <= 16);   // the flag is driven by the pattern
            return true;
        }

        public void ChangeMouthOpenMax(float v)
        {
            v = Mathf.Clamp01(v);
            Status.MouthOpenMax = v;
            Stage.Mouth.OpenMax = v;
            if (Status.MouthFixed) Stage.Mouth.FixedRate = v;
        }

        public void ChangeMouthOpenMin(float v)
        {
            v = Mathf.Clamp01(v);
            Status.MouthOpenMin = v;
            Stage.Mouth.OpenMin = v;
            if (Status.MouthFixed) Stage.Mouth.FixedRate = v;   // last of Max/Min wins
        }

        public void ChangeMouthFixed(bool on)
        {
            Status.MouthFixed = on;
            Stage.Mouth.FixedRate = on ? Status.MouthOpenMax : -1f;
        }

        /// <summary>0 shows the head tongue `o_tang`, 1 the body tongue `N_tang` instead.</summary>
        public void ChangeTongueState(byte s)
        {
            Status.TongueState = s;
            tongueApplied = s;
            if (headTongue && headTongue.activeSelf != (s == 0)) headTongue.SetActive(s == 0);
            foreach (var t in bodyTongues) if (t && t.activeSelf != (s == 1)) t.SetActive(s == 1);
        }

        /// <summary>Another body tongue that follows the tongue state from now on (the silhouette body's, built when the
        /// silhouette is first switched on); set to the current state at once.</summary>
        public void AddBodyTongue(GameObject t)
        {
            if (!t || bodyTongues.Contains(t)) return;
            bodyTongues.Add(t);
            bool on = Status.TongueState == 1;
            if (t.activeSelf != on) t.SetActive(on);
        }

        /// <summary>
        /// The shape.face edit unit set new values on the face engine (values after the male
        /// range rule): they are the values the mouth mask restores; while the mask is on, its sliders are set to 0.5 again
        /// before the engine applies.
        /// </summary>
        public void OnFaceShapeValues(float[] values)
        {
            faceShapeValues = (float[])(values ?? System.Array.Empty<float>()).Clone();
            if (!maskApplied || faceShape == null) return;
            foreach (int i in MaskSliders) faceShape.SetValue(i, 0.5f);
        }

        /// <summary>while on, the 13 mouth/chin face sliders evaluate at 0.5 instead of the card values.</summary>
        public void DisableShapeMouth(bool on)
        {
            Status.DisableMouthShapeMask = on;
            if (on == maskApplied) return;
            maskApplied = on;
            if (faceShape == null) return;
            foreach (int i in MaskSliders)
                if (on || i < faceShapeValues.Length) faceShape.SetValue(i, on ? 0.5f : faceShapeValues[i]);
            faceShape.Apply();
        }

        public void ChangeTearsRate(float v)
        {
            Status.TearsRate = Mathf.Clamp01(v);
            Materials?.SetTears(Status.TearsRate);
        }

        public void ChangeHohoAkaRate(float v)
        {
            Status.HohoAkaRate = Mathf.Clamp01(v);
            Materials?.SetCheek(Status.HohoAkaRate);
        }

        public void HideEyeHighlight(bool hide)
        {
            Status.HideEyesHighlight = hide;
            Materials?.SetHighlightHidden(hide);
        }

        /// <summary>`mouthAdjustWidth`: apply the mouth width to `cf_J_MouthMove`.</summary>
        public void ChangeMouthAdjustWidth(bool on)
        {
            Status.MouthAdjustWidth = on;
            Stage.ApplyMouthWidth = on;
        }

        public int EyebrowPtn => Status.EyebrowPtn;
        public int EyesPtn => Status.EyesPtn;
        public int MouthPtn => Status.MouthPtn;
        public int EyebrowPtnCount => Stage.Eyebrow.PatternCount;
        public int EyesPtnCount => Stage.Eyes.PatternCount;
        public int MouthPtnCount => Stage.Mouth.PatternCount;

        // ================================================================== load sequences

        /// <summary>3, 4: what the head load applies from the status (hideEyesHighlight is reset to false).</summary>
        public void ApplyHeadLoad(FaceStatus s)
        {
            s = (s ?? new FaceStatus()).Clone();
            Status = s.Clone();
            ChangeMouthAdjustWidth(s.MouthAdjustWidth);
            ChangeHohoAkaRate(s.HohoAkaRate);
            DisableShapeMouth(s.DisableMouthShapeMask);   // read by the face-shape setup of the head load
            HideEyeHighlight(false);
            ChangeEyesBlinkFlag(s.EyesBlink);
            ChangeEyebrowPtn(s.EyebrowPtn);
            ChangeEyebrowOpenMax(s.EyebrowOpenMax);
            ChangeEyesPtn(s.EyesPtn);
            ChangeEyesOpenMax(s.EyesOpenMax);
            ChangeMouthPtn(s.MouthPtn);
            ChangeMouthOpenMax(s.MouthOpenMax);
            ChangeMouthOpenMin(s.MouthOpenMin);
            KeepActualPatterns();
        }

        /// <summary>`UpdateState` (end of "add from card" and of scene load): eyebrow / eyes validated against the
        /// lists, blink before the open value (v² when blinking is off), Studio mouth, Studio mouth open, cheek (female), tears.</summary>
        public void ApplyStudioState(FaceStatus s, float mouthOpen = 0f, bool lipSync = true)
        {
            s = (s ?? new FaceStatus()).Clone();
            Status = s.Clone();
            ChangeMouthAdjustWidth(s.MouthAdjustWidth);
            ChangeEyebrowPtn(ExpressionLists.Position(Lists.Eyebrow, s.EyebrowPtn) >= 0 ? s.EyebrowPtn : 0);
            ChangeEyesPtn(ExpressionLists.Position(Lists.Eyes, s.EyesPtn) >= 0 ? s.EyesPtn : 0);
            ChangeEyesBlinkFlag(s.EyesBlink);
            ChangeEyesOpenMax(s.EyesOpenMax);
            SetStudioMouth(s.MouthPtn);
            MouthOpen = Mathf.Clamp01(mouthOpen);
            LipSync = lipSync;
            ApplyStudioMouthOpen();
            if (!IsMale) ChangeHohoAkaRate(s.HohoAkaRate);
            ChangeTearsRate(s.TearsRate);
            KeepActualPatterns();
        }

        /// <summary>"Add from card" in Studio: the head-load sequence, then the Studio state, from one status
        /// (the code defaults unless the card status is applied on purpose).</summary>
        public void StartStudio(FaceStatus s = null)
        {
            s = s ?? new FaceStatus();
            ApplyHeadLoad(s);
            ApplyStudioState(s, 0f, true);
        }

        // After a status copy, a rejected id must not remain in the status (HS2Next: status = what the face shows).
        void KeepActualPatterns()
        {
            if (Stage.Eyebrow.CurrentPattern >= 0) Status.EyebrowPtn = Stage.Eyebrow.CurrentPattern;
            if (Stage.Eyes.CurrentPattern >= 0) Status.EyesPtn = Stage.Eyes.CurrentPattern;
            if (Stage.Mouth.CurrentPattern >= 0) Status.MouthPtn = Stage.Mouth.CurrentPattern;
            Status.TongueState = tongueApplied;
            Status.DisableMouthShapeMask = maskApplied;
        }

        // ================================================================== Studio controls

        /// <summary>`OICharInfo.mouthOpen` (0..1): the mouth-open slider value (→ FixedRate except during lip sync).</summary>
        public float MouthOpen { get; private set; }
        /// <summary>`OICharInfo.lipSync` (default true).</summary>
        public bool LipSync { get; private set; } = true;
        /// <summary>A Studio voice is playing on this character.</summary>
        public bool VoicePlaying { get; private set; }

        /// <summary>The voice level 0..1 (`voiceValue`) that drives the mouth openness when FixedRate is off. 0 in Studio without lip sync.</summary>
        public float VoiceLevel
        {
            get => Stage.VoiceValue;
            set => Stage.VoiceValue = Mathf.Clamp01(value);
        }

        /// <summary>Eyebrow ◀▶ number: 0-based list position of the current id (an unlisted id shows 0).</summary>
        public int EyebrowUiIndex => Mathf.Max(0, ExpressionLists.Position(Lists.Eyebrow, Status.EyebrowPtn));
        public int EyesUiIndex => Mathf.Max(0, ExpressionLists.Position(Lists.Eyes, Status.EyesPtn));
        /// <summary>Mouth ◀▶ number: the raw pattern id (0..MouthPtnCount−1).</summary>
        public int MouthUiIndex => Status.MouthPtn;
        public int EyebrowUiCount => Lists.Eyebrow.Length;
        public int EyesUiCount => Lists.Eyes.Length;
        public int MouthUiCount => Stage.Mouth.PatternCount;

        /// <summary>Selects the eyebrow by list position (blend = true, 0.15 s cross-fade); false when out of range.</summary>
        public bool SetEyebrowUiIndex(int pos) =>
            pos >= 0 && pos < Lists.Eyebrow.Length ? ChangeEyebrowPtn(Lists.Eyebrow[pos]) : Reject("eyebrow UI index", pos, Lists.Eyebrow.Length);

        public bool SetEyesUiIndex(int pos) =>
            pos >= 0 && pos < Lists.Eyes.Length ? ChangeEyesPtn(Lists.Eyes[pos]) : Reject("eyes UI index", pos, Lists.Eyes.Length);

        /// <summary>◀ (dir &lt; 0) / ▶ (dir &gt; 0) with wrap-around.</summary>
        public bool StepEyebrowUi(int dir) => SetEyebrowUiIndex(Wrap(EyebrowUiIndex + System.Math.Sign(dir), Lists.Eyebrow.Length));
        public bool StepEyesUi(int dir) => SetEyesUiIndex(Wrap(EyesUiIndex + System.Math.Sign(dir), Lists.Eyes.Length));
        public bool StepMouthUi(int dir) => SetStudioMouth(Wrap(MouthUiIndex + System.Math.Sign(dir), Stage.Mouth.PatternCount));

        /// <summary>Studio mouth change: raw id, no list validation, blend false, then the mouth shape mask
        /// for patterns 10, 13, 17, 18.</summary>
        public bool SetStudioMouth(int id)
        {
            if (!ChangeMouthPtn(id, false)) return false;
            DisableShapeMouth(id == 10 || id == 13 || id == 17 || id == 18);
            return true;
        }

        /// <summary>Mouth-open slider: stores mouthOpen and re-applies the Studio mouth open.</summary>
        public void SetMouthOpen(float v)
        {
            MouthOpen = Mathf.Clamp01(v);
            ApplyStudioMouthOpen();
        }

        /// <summary>FixedRate −1 while a voice plays with lip sync on, else the slider value.</summary>
        public void ApplyStudioMouthOpen()
        {
            Stage.Mouth.FixedRate = VoicePlaying && LipSync ? -1f : MouthOpen;
        }

        /// <summary>Lip-sync toggle: store the flag and re-apply the mouth open.</summary>
        public void SetLipSync(bool on)
        {
            LipSync = on;
            ApplyStudioMouthOpen();
        }

        /// <summary>Voice play / stop (`SetVoice`). Stopping drops the voice level to 0.</summary>
        public void SetVoicePlaying(bool playing)
        {
            VoicePlaying = playing;
            if (!playing) VoiceLevel = 0f;
            ApplyStudioMouthOpen();
        }

        /// <summary>Eyes-open slider / blink toggle / tears / cheek of the Studio panels.</summary>
        public float EyesOpen { get => Status.EyesOpenMax; set => ChangeEyesOpenMax(value); }
        public bool Blink { get => Status.EyesBlink; set => ChangeEyesBlinkFlag(value); }
        /// <summary>Tears 0..1 (continuous, clamped).</summary>
        public float Tears { get => Status.TearsRate; set => ChangeTearsRate(value); }
        /// <summary>Cheek flush 0..1 (clamped).</summary>
        public float Cheek { get => Status.HohoAkaRate; set => ChangeHohoAkaRate(value); }
        /// <summary>The mouth's fixed mode as the game drives it (`mouthFixed`).</summary>
        public bool MouthFixed { get => Status.MouthFixed; set => ChangeMouthFixed(value); }
        /// <summary>eyelid correction (default off).</summary>
        public bool EyelidCorrection { get => Stage.EyelidCorrection; set => Stage.EyelidCorrection = value; }

        /// <summary>Rewrites the material values after a material swap (ShaderMap.ApplyUrp).</summary>
        public void RefreshMaterials() => Materials?.Refresh();

        // ================================================================== helpers

        static int Wrap(int i, int n) => n <= 0 ? 0 : ((i % n) + n) % n;

        bool Reject(string what, int value, int count)
        {
            Rejections++;
            string msg = $"face: {what} {value} rejected (valid 0..{count - 1})";
            if (log != null) log.Add(msg); else Debug.LogWarning(msg);
            return false;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r) return r; }
            return null;
        }

        static void FindAll(Transform t, string name, List<GameObject> into)
        {
            if (t.name == name) into.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++) FindAll(t.GetChild(i), name, into);
        }
    }
}
