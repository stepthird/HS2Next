// ScriptProbe's JR6 in the player (spec 17.2 JR6: the mosaic rule itself on a moving member through the capture paths, MosaicTest's fixture as the
// Editor's ScriptTest_Capture.cs uses it, rebuilt here: a player has no Editor assembly). Nothing of the mosaic is changed: the fixture
// replaces textures of the character's own materials and reads what each capture drew; every value is put back by scriptprobe.jr6.restore.
//   scriptprobe.jr6.fixture  a character as the fixture: clothes and accessories hidden, BR-Chan with the body composite (marker cells inside the patches'
//                            transferred UV) and a gradient composite, HERO107 with N_dan shown and a marker checker on the members' texture
//   scriptprobe.jr6.use      the wide pass off (a) or on (b); BR-Chan's composite for it (marker for a, gradient for b)
//   scriptprobe.jr6.choose   the view of nine (its normal, four 40 degrees off it, four 70 degrees off it; HERO107: a fixed direction in the hips' frame)
//                            that shows the member most by the footprint alone, before any marker is read: the camera.set request that places it
//   scriptprobe.jr6.arm      reads each capture's moment in CaptureEvents.Rendered (its frame, right after its render): the member's footprint from a
//                            render of the members alone with the capture's camera, eroded 1 px; (a) the left-out control (the members drawn unlit with
//                            the marker) and the blocks of the capture's camera step; (b) the coverage (the wide pass's debug view) and the render with
//                            the wide pass off. For a recording: every n-th frame read, and after every frame the next frame's view chosen and queued
//   scriptprobe.jr6.judge    a capture's file read as numbers and deleted, judged against its moment: the numbers of one moment
//   scriptprobe.jr6.folder   the same for every moment of a recording's folder
//   scriptprobe.jr6.restore  every texture and switch back; the fixture's objects destroyed
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Stp3.Test.ScriptProbe
{
    public sealed partial class ScriptProbePlugin
    {
        const float Jr6View = 16f, Fov = 23f;
        static readonly int UnlitId = Shader.PropertyToID("_HS2MosaicUnlit");
        static readonly Color32 Skin = new Color32(214, 168, 142, 255);
        static readonly string[] Patches = { "o_mnpa", "o_mnpb" };

        sealed class Fix
        {
            public string Key;
            public bool Male;
            public CharaContext Ctx;
            public MosaicMember Focus;
            public Transform Hips;
            public Vector3 LocalView;
            public Texture2D Marker, Gradient;
            public Texture BodyTex;
            public readonly List<(Material m, Texture t)> Saved = new List<(Material, Texture)>();
            public readonly List<GameObject> Shown = new List<GameObject>();
            public readonly List<(GameObject g, bool on)> Hidden = new List<(GameObject, bool)>();
            public readonly List<(string p, float v)> Alpha = new List<(string, float)>();

            public Vector3 Centre => MosaicCamera.RegionMatrix(Focus).MultiplyPoint3x4(Focus.Region.Centroid);

            public Vector3 Normal
            {
                get
                {
                    if (Male) return (Hips.rotation * LocalView).normalized;
                    var n = MosaicCamera.RegionMatrix(Focus).MultiplyVector(Focus.Region.Normal).normalized;
                    return n == Vector3.zero ? Ctx.Root.transform.forward : n;
                }
            }
        }

        sealed class Jr6Moment
        {
            public string Path;
            public int Id;
            public bool[] Fp;
            public int Fpn, Drawn, Under, W, H, View = -1, ChoiceFp;
            public float Floor, LeastBlock = float.MaxValue;
            public bool CaptureFrame;
            public Color32[] Control, Cover, Off;
            public List<MosaicWideGroup> Groups;
            public Vector3 Centre;
        }

        Fix fix;
        bool jr6Armed, jr6Wide0, jr6WideSaved;
        Camera jr6Cam;
        Texture2D magenta;
        Material plain;
        readonly List<Object> jr6Trash = new List<Object>();
        readonly Dictionary<string, Jr6Moment> jr6Moments = new Dictionary<string, Jr6Moment>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> jr6Errors = new List<string>();
        /// <summary>MosaicWide.DebugView (the wide pass's coverage view) is compiled in the Editor only: read by reflection, null in a player.</summary>
        static readonly System.Reflection.FieldInfo WideDebugView = typeof(MosaicWide).GetField("DebugView", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        const string NoWideDebugView = "this build has no MosaicWide.DebugView (Editor only): the coverage of (b) is not read";

        static bool SetWideDebugView(bool on)
        {
            if (WideDebugView == null) return false;
            WideDebugView.SetValue(null, on);
            return true;
        }
        Action<CaptureInfo> jr6Handler;
        (int view, int fp) jr6Chosen = (-1, 0);

        void RegisterMosaic(ICommandRegistry c)
        {
            c.Register(new CommandSpec("scriptprobe.jr6.fixture", "A character as MosaicTest's fixture of the moving-member rows.").WithTimeout(300).Arg("key", ArgTypes.String, "the character", required: true),
                a => Jr6Fixture(a.Args.Require<string>("key")));
            c.Register(new CommandSpec("scriptprobe.jr6.use", "The wide pass off (a) or on (b), and BR-Chan's composite for it.").Arg("wide", ArgTypes.Bool, "the wide pass", required: true),
                a => Jr6Use(a.Args.Get("wide", false)));
            c.Register(new CommandSpec("scriptprobe.jr6.choose", "The view of nine that shows the member most by the footprint alone: a camera.set request.").WithTimeout(120)
                .Arg("width", ArgTypes.Int, "width").Arg("height", ArgTypes.Int, "height"), a => Jr6ChooseReply(a.Args.Get("width", 960), a.Args.Get("height", 540)));
            c.Register(new CommandSpec("scriptprobe.jr6.arm", "Reads each capture's moment from now on; a recording also chooses the next frame's view.").Arg("wide", ArgTypes.Bool, "the wide pass", required: true)
                .Arg("record", ArgTypes.Bool, "a recording").Arg("every", ArgTypes.Int, "a recording: every n-th frame read"),
                a => Jr6Arm(a.Args.Get("wide", false), a.Args.Get("record", false), a.Args.Get("every", 3)));
            c.Register(new CommandSpec("scriptprobe.jr6.disarm", "Stops reading moments."), _ => { Jr6Disarm(); return new Dictionary<string, object>(); });
            c.Register(new CommandSpec("scriptprobe.jr6.judge", "A capture's file read as numbers and deleted, judged against its moment.").WithTimeout(120).Arg("path", ArgTypes.String, "the file", required: true),
                a => Jr6JudgePath(a.Args.Require<string>("path")));
            c.Register(new CommandSpec("scriptprobe.jr6.folder", "Every moment of a recording's folder judged; the files deleted.").WithTimeout(600).Arg("folder", ArgTypes.String, "the folder", required: true),
                a => Jr6Folder(a.Args.Require<string>("folder")));
            c.Register(new CommandSpec("scriptprobe.jr6.restore", "Every texture and switch back; the fixture's objects destroyed."), _ => { Jr6Restore(); return new Dictionary<string, object>(); });
        }

        T Keep<T>(T o) where T : Object { jr6Trash.Add(o); return o; }

        // ------------------------------------------------------------ the fixture (MosaicTest's helpers, rebuilt)

        static MosaicMember Member(CharaContext ctx, string name) => MosaicSet.StateOf(ctx)?.Members.FirstOrDefault(m => m.Renderer && m.Renderer.name == name);

        static Renderer FindRenderer(CharaContext ctx, string name) => ctx?.Root ? ctx.Root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == name) : null;

        Dictionary<string, object> Jr6Fixture(string key)
        {
            Jr6Restore();
            var c = Rt.FindChara(key)?.Context;
            if (c == null || !c.Root) throw new BridgeException(BridgeCodes.NotFound, "no built character " + key);
            if (!jr6WideSaved) { jr6Wide0 = MosaicWide.Enabled; jr6WideSaved = true; }
            var f = new Fix { Key = key, Male = c.IsMale, Ctx = c };
            foreach (var g in c.Clothes) if (g) { f.Hidden.Add((g, g.activeSelf)); g.SetActive(false); }
            foreach (var a in c.Accessories) if (a?.Object) { f.Hidden.Add((a.Object, a.Object.activeSelf)); a.Object.SetActive(false); }
            foreach (var p in new[] { "_alpha_a", "_alpha_b", "_alpha_c", "_alpha_d" })
                if (c.BodyDraw && c.BodyDraw.HasProperty(p)) { f.Alpha.Add((p, c.BodyDraw.GetFloat(p))); c.BodyDraw.SetFloat(p, p == "_alpha_c" ? 1f : 0f); }
            if (f.Male)
            {
                var nDan = HS2Next.Chara.Edit.EditContext.Find(c.Body.transform, "N_dan");
                if (nDan && !nDan.gameObject.activeSelf) { nDan.gameObject.SetActive(true); f.Shown.Add(nDan.gameObject); }
            }
            f.Focus = Member(c, f.Male ? "cm_o_dan00" : "o_mnpb");
            if (c.BodyBones != null && c.BodyBones.TryGetValue("cf_J_Hips", out var hips)) f.Hips = hips;
            if (f.Focus == null || !f.Hips) { fix = f; Jr6Restore(); throw new BridgeException(BridgeCodes.NotFound, "the focus member or the hips are missing"); }
            if (f.Male)
            {
                f.LocalView = Quaternion.Inverse(f.Hips.rotation) * new Vector3(0f, -0.8f, 0.6f).normalized;
                f.Marker = Checker(2048, 5);
                foreach (var mm in MosaicSet.StateOf(c).Members)
                    foreach (var mat in mm.Renderer.sharedMaterials)
                        if (mat && mat.HasProperty("_MainTex") && !f.Saved.Exists(x => x.m == mat)) { f.Saved.Add((mat, mat.GetTexture("_MainTex"))); mat.SetTexture("_MainTex", f.Marker); }
            }
            else
            {
                f.BodyTex = c.BodyDraw ? c.BodyDraw.GetTexture("_MainTex") : null;
                f.Marker = Composite(c, 2048, 6, Cells(3));
                f.Gradient = Composite(c, 2048, 0, (x, y) => new Color32((byte)(x * 255 / 2048), (byte)(y * 255 / 2048), (byte)(255 - (x + y) * 127 / 2048), 255));
            }
            fix = f;
            return new Dictionary<string, object> { ["key"] = key, ["male"] = f.Male, ["focus"] = f.Focus.Renderer.name, ["members"] = MosaicSet.StateOf(c).Members.Count, ["centre"] = V(f.Centre) };
        }

        static Func<int, int, Color32> Cells(int shift) => (x, y) => ((x >> shift) & 1) == 0 && ((y >> shift) & 1) == 0 ? new Color32(0, 255, 0, 255) : Skin;

        Texture2D Checker(int size, int shift)
        {
            var cells = Cells(shift);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = cells(x, y);
            var t = Keep(new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "scriptprobe_marker", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear });
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static bool[] Erode(bool[] m, int w, int h, int r)
        {
            var o = new bool[m.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool all = true;
                    for (int dy = -r; dy <= r && all; dy++)
                        for (int dx = -r; dx <= r && all; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            all = xx >= 0 && yy >= 0 && xx < w && yy < h && m[yy * w + xx];
                        }
                    o[y * w + x] = all;
                }
            return o;
        }

        /// <summary>The body composite: skin colour, and in the texels at least <paramref name="inset"/> texels inside the patches' transferred UV triangles <paramref name="inner"/>.</summary>
        Texture2D Composite(CharaContext ctx, int size, int inset, Func<int, int, Color32> inner)
        {
            var inside = new bool[size * size];
            foreach (var n in Patches)
            {
                var smr = FindRenderer(ctx, n) as SkinnedMeshRenderer;
                var t = smr ? MosaicUvTransfer.Transfer(smr, ctx.BodyRenderer) : null;
                if (t == null) continue;
                var tris = t.Source.triangles;
                for (int k = 0; k + 2 < tris.Length; k += 3)
                {
                    Vector2 a = t.Uv[tris[k]] * size, b = t.Uv[tris[k + 1]] * size, c = t.Uv[tris[k + 2]] * size;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                            if (InTri(new Vector2(x + 0.5f, y + 0.5f), a, b, c)) inside[y * size + x] = true;
                }
            }
            var eroded = Erode(inside, size, size, inset);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = eroded[y * size + x] ? inner(x, y) : Skin;
            var tex = Keep(new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "scriptprobe_composite", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear });
            tex.SetPixels32(px);
            tex.Apply(true);
            return tex;
        }

        static void RunStage(CharaContext ctx)
        {
            ctx.Editor.MarkUnit(HS2Next.Chara.Edit.Units.MosaicStageUnit.UnitId);
            ctx.Editor.Flush();
        }

        Dictionary<string, object> Jr6Use(bool wide)
        {
            if (fix == null) throw new BridgeException(BridgeCodes.BadArgs, "no fixture");
            MosaicWide.Enabled = wide;
            if (!fix.Male && fix.Ctx.BodyDraw)
            {
                fix.Ctx.BodyDraw.SetTexture("_MainTex", wide ? fix.Gradient : fix.Marker);
                RunStage(fix.Ctx);
            }
            return new Dictionary<string, object> { ["wide"] = MosaicWide.Enabled };
        }

        Camera NewCam()
        {
            var go = Keep(new GameObject("scriptprobe_cam"));
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = Fov; cam.nearClipPlane = 0.3f; cam.farClipPlane = 2000f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.25f, 0.3f, 1f);
            cam.allowHDR = true; cam.allowMSAA = false;
            var urp = go.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = false;
            urp.antialiasing = AntialiasingMode.None;
            urp.renderShadows = false;
            return cam;
        }

        /// <summary>One render into a linear half-float target, read back in memory.</summary>
        static Color[] RenderLinear(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.aspect = w / (float)h;
            cam.Render();
            cam.targetTexture = null;
            var req = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBAHalf);
            req.WaitForCompletion();
            var px = new Color[w * h];
            if (!req.hasError)
            {
                var d = req.GetData<ushort>();
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color(Mathf.HalfToFloat(d[4 * i]), Mathf.HalfToFloat(d[4 * i + 1]), Mathf.HalfToFloat(d[4 * i + 2]), Mathf.HalfToFloat(d[4 * i + 3]));
            }
            rt.Release(); Object.Destroy(rt);
            return px;
        }

        /// <summary>The members drawn flat with <paramref name="tex"/> (texture mode, the unlit test view; post-processing, AA and the wide pass off); their values come back.</summary>
        Color[] MarkerRender(Camera cam, int w, int h, CharaContext ctx, Texture2D tex)
        {
            bool wide = MosaicWide.Enabled;
            MosaicWide.Enabled = false;
            var saved = new List<(Material m, Texture t, Vector2 sc, Vector2 of, float mode, Color c)>();
            foreach (var mm in MosaicSet.StateOf(ctx).Members)
                foreach (var mat in mm.Renderer.sharedMaterials)
                {
                    if (!mat || saved.Exists(x => x.m == mat)) continue;
                    saved.Add((mat, mat.GetTexture("_MainTex"), mat.GetTextureScale("_MainTex"), mat.GetTextureOffset("_MainTex"), mat.GetFloat("_MosaicMode"), mat.GetColor("_Color")));
                    mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", Vector2.one); mat.SetTextureOffset("_MainTex", Vector2.zero);
                    mat.SetFloat("_MosaicMode", 1f); mat.SetColor("_Color", Color.white);
                }
            Shader.SetGlobalFloat(UnlitId, 1f);
            try { return RenderLinear(cam, w, h); }
            finally
            {
                foreach (var x in saved)
                {
                    x.m.SetTexture("_MainTex", x.t); x.m.SetTextureScale("_MainTex", x.sc); x.m.SetTextureOffset("_MainTex", x.of);
                    x.m.SetFloat("_MosaicMode", x.mode); x.m.SetColor("_Color", x.c);
                }
                Shader.SetGlobalFloat(UnlitId, 0f);
                MosaicWide.Enabled = wide;
            }
        }

        bool[] Footprint(Camera cam, int w, int h)
        {
            if (!magenta)
            {
                magenta = Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "scriptprobe_flat" });
                magenta.SetPixels32(Enumerable.Repeat(new Color32(255, 0, 255, 255), 16).ToArray());
                magenta.Apply();
            }
            var px = MarkerRender(cam, w, h, fix.Ctx, magenta);
            return Erode(px.Select(c => c.r > 0.9f && c.g < 0.1f && c.b > 0.9f).ToArray(), w, h, 1);
        }

        /// <summary>The member slots drawn by an unlit textured material with <paramref name="tex"/> at their own UVs (the mosaic left out), around <paramref name="a"/>.</summary>
        void WithoutMosaic(Texture tex, Action a)
        {
            if (!plain)
            {
                var sh = Shader.Find("HS2Next/Unlit/Texture");
                if (!sh) throw new InvalidOperationException("HS2Next/Unlit/Texture is not in this player");
                plain = Keep(new Material(sh));
            }
            plain.SetTexture("_MainTex", tex);
            var saved = new List<(Renderer r, Material[] m)>();
            foreach (var mm in MosaicSet.StateOf(fix.Ctx).Members)
            {
                saved.Add((mm.Renderer, mm.Renderer.sharedMaterials));
                mm.Renderer.sharedMaterials = Enumerable.Repeat(plain, mm.Renderer.sharedMaterials.Length).ToArray();
            }
            bool wide = MosaicWide.Enabled;
            MosaicWide.Enabled = false;
            try { a(); }
            finally { foreach (var (r, m) in saved) r.sharedMaterials = m; MosaicWide.Enabled = wide; }
        }

        // ------------------------------------------------------------ views

        static Vector3[] Views(Vector3 n)
        {
            var x = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var y = Vector3.Cross(n, x);
            float c = Mathf.Cos(40f * Mathf.Deg2Rad), s = Mathf.Sin(40f * Mathf.Deg2Rad), c7 = Mathf.Cos(70f * Mathf.Deg2Rad), s7 = Mathf.Sin(70f * Mathf.Deg2Rad);
            return new[]
            {
                n, (c * n + s * x).normalized, (c * n - s * x).normalized, (c * n + s * y).normalized, (c * n - s * y).normalized,
                (c7 * n + s7 * x).normalized, (c7 * n - s7 * x).normalized, (c7 * n + s7 * y).normalized, (c7 * n - s7 * y).normalized,
            };
        }

        static Quaternion Look(Vector3 d) => Quaternion.LookRotation(-d, Mathf.Abs(d.y) > 0.95f ? Vector3.forward : Vector3.up);

        (int view, int fp, object aim) Choose(int w, int h)
        {
            var view = ViewCam;
            if (!jr6Cam) jr6Cam = NewCam();
            jr6Cam.fieldOfView = Fov;
            jr6Cam.nearClipPlane = view.nearClipPlane;
            jr6Cam.farClipPlane = view.farClipPlane;
            var c = fix.Centre;
            var views = Views(fix.Normal);
            int best = 0, bestFp = -1;
            for (int i = 0; i < views.Length; i++)
            {
                jr6Cam.transform.SetPositionAndRotation(c + views[i] * Jr6View, Look(views[i]));
                int n = 0;
                Forced(() => n = Footprint(jr6Cam, w, h).Count(b => b));
                if (n > bestFp) { bestFp = n; best = i; }
            }
            var e = Look(views[best]).eulerAngles;
            return (best, bestFp, new Dictionary<string, object> { ["cmd"] = "camera.set", ["target"] = new[] { c.x, c.y, c.z }, ["rot"] = new[] { e.x, e.y, e.z }, ["dist"] = Jr6View, ["fov"] = Fov });
        }

        Dictionary<string, object> Jr6ChooseReply(int w, int h)
        {
            if (fix == null) throw new BridgeException(BridgeCodes.BadArgs, "no fixture");
            var p = Choose(w, h);
            jr6Chosen = (p.view, p.fp);
            return new Dictionary<string, object> { ["view"] = p.view, ["footprint"] = p.fp, ["aim"] = p.aim, ["centre"] = V(fix.Centre) };
        }

        // ------------------------------------------------------------ moments

        Dictionary<string, object> Jr6Arm(bool wide, bool record, int every)
        {
            if (fix == null) throw new BridgeException(BridgeCodes.BadArgs, "no fixture");
            Jr6Disarm();
            jr6Moments.Clear();
            jr6Errors.Clear();
            int recIndex = 0;
            var server = AppCore.Current.Bridge.Server;
            jr6Handler = info =>
            {
                try
                {
                    if (record)
                    {
                        int idx = recIndex++;
                        if (idx % Math.Max(1, every) == 0)
                        {
                            var rm = Read(wide, info);
                            rm.View = jr6Chosen.view; rm.ChoiceFp = jr6Chosen.fp;
                            if (!string.IsNullOrEmpty(info.Path)) jr6Moments[info.Path] = rm;
                        }
                        var next = Choose(info.Width, info.Height);   // the next frame's view, by the footprint alone
                        jr6Chosen = (next.view, next.fp);
                        server.SubmitAsync(JsonConvert.SerializeObject(next.aim));
                        return;
                    }
                    var m = Read(wide, info);
                    m.View = jr6Chosen.view; m.ChoiceFp = jr6Chosen.fp;
                    if (!string.IsNullOrEmpty(info.Path)) jr6Moments[info.Path] = m;
                }
                catch (Exception e) { jr6Errors.Add(e.GetType().Name + ": " + e.Message); }
            };
            CaptureEvents.Rendered += jr6Handler;
            jr6Armed = true;
            return new Dictionary<string, object> { ["armed"] = true, ["wide"] = wide, ["record"] = record };
        }

        void Jr6Disarm()
        {
            if (jr6Armed && jr6Handler != null) CaptureEvents.Rendered -= jr6Handler;
            jr6Armed = false;
            jr6Handler = null;
        }

        Jr6Moment Read(bool wide, CaptureInfo info)
        {
            var m = new Jr6Moment { Path = info.Path, Id = info.Id, Centre = fix.Centre, W = info.Width, H = info.Height };
            var f = MosaicCamera.Last;
            m.CaptureFrame = f != null && f.Camera && f.Camera.name == "hs2next_capture_camera";
            if (m.CaptureFrame)
            {
                m.Floor = f.FloorPx;
                foreach (var g in f.Groups)
                    foreach (var mm in g.Members)
                    {
                        m.Drawn++;
                        if (mm.BlockPx < f.FloorPx) m.Under++;
                        m.LeastBlock = Mathf.Min(m.LeastBlock, mm.BlockPx);
                    }
            }
            if (wide) m.Groups = new List<MosaicWideGroup>(MosaicWideFeature.LastGroups);
            var view = ViewCam;
            if (!jr6Cam) jr6Cam = NewCam();
            jr6Cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
            jr6Cam.fieldOfView = view.fieldOfView;
            jr6Cam.nearClipPlane = view.nearClipPlane;
            jr6Cam.farClipPlane = view.farClipPlane;
            Forced(() => m.Fp = Footprint(jr6Cam, info.Width, info.Height));
            m.Fpn = m.Fp.Count(x => x);
            string aa = info.Kind == CaptureKind.Capture ? CaptureService.None : CaptureService.Smaa;
            if (!wide) WithoutMosaic(fix.Marker, () => m.Control = ForcedRender(info.Width, info.Height, aa, false));
            else
            {
                if (SetWideDebugView(true))
                {
                    try { m.Cover = ForcedRender(info.Width, info.Height, aa, false); }
                    finally { SetWideDebugView(false); }
                }
                else if (!jr6Errors.Contains(NoWideDebugView)) jr6Errors.Add(NoWideDebugView);
                MosaicWide.Enabled = false;
                try { m.Off = ForcedRender(info.Width, info.Height, aa, false); }
                finally { MosaicWide.Enabled = true; }
            }
            return m;
        }

        static bool MarkerPx(Color32 c)
        {
            float r = CaptureService.SrgbToLinear(c.r), g = CaptureService.SrgbToLinear(c.g), b = CaptureService.SrgbToLinear(c.b);
            return g > 0.08f && g > 1.5f * Mathf.Max(r, b);
        }

        /// <summary>One moment's numbers (MosaicTest's judgement of WV3m / RV3 as the Editor's JR6 reads it); the verdicts are the check's.</summary>
        Dictionary<string, object> Judge(Jr6Moment m, Color32[] cap, bool wide)
        {
            var o = new Dictionary<string, object> { ["path"] = m?.Path, ["read"] = cap != null, ["moment"] = m != null, ["errors"] = jr6Errors.ToArray() };
            if (m == null || cap == null || m.Fp == null || cap.Length != m.Fp.Length) { o["missing"] = true; return o; }
            o["missing"] = false;
            o["view"] = m.View; o["choiceFootprint"] = m.ChoiceFp; o["footprint"] = m.Fpn; o["centre"] = V(m.Centre);
            int w = m.W, h = m.H;
            if (!wide)
            {
                int mk = 0, ctrl = 0;
                for (int k = 0; k < cap.Length; k++)
                    if (m.Fp[k])
                    {
                        if (MarkerPx(cap[k])) mk++;
                        if (m.Control != null && MarkerPx(m.Control[k])) ctrl++;
                    }
                o["markers"] = mk; o["leftOut"] = ctrl; o["captureFrame"] = m.CaptureFrame; o["drawn"] = m.Drawn; o["under"] = m.Under; o["floor"] = R4(m.Floor);
                o["leastBlock"] = m.Drawn > 0 ? R4(m.LeastBlock) : 0.0;
                return o;
            }
            if (m.Groups == null || m.Groups.Count == 0 || m.Cover == null || m.Off == null) { o["noGroup"] = true; return o; }
            o["noGroup"] = false;
            var g = m.Groups[0];
            float B = g.Block;
            int ir = Mathf.CeilToInt(1.5f * B), miss = 0;
            float r2 = 1.5f * B * 1.5f * B;
            var seen = m.Cover.Select(c => c.r > 127).ToArray();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!m.Fp[y * w + x]) continue;
                    for (int dy = -ir; dy <= ir; dy++)
                        for (int dx = -ir; dx <= ir; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h || dx * dx + dy * dy > r2) continue;
                            if (!seen[yy * w + xx]) { miss++; seen[yy * w + xx] = true; }
                        }
                }
            var sets = new Dictionary<long, HashSet<int>>();
            int changed = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int k = y * w + x;
                    var a = cap[k];
                    var off = m.Off[k];
                    if (a.r == off.r && a.g == off.g && a.b == off.b) continue;
                    changed++;
                    long kx = (long)Mathf.Floor((x + 0.5f - g.Anchor.x) / B), ky = (long)Mathf.Floor((y + 0.5f - g.Anchor.y) / B);
                    long key = kx * 1000000 + ky;
                    if (!sets.TryGetValue(key, out var set)) sets[key] = set = new HashSet<int>();
                    set.Add(a.r << 16 | a.g << 8 | a.b);
                }
            o["block"] = R4(B); o["uncovered"] = miss; o["changed"] = changed; o["blocks"] = sets.Count; o["mixed"] = sets.Values.Count(s => s.Count > 1);
            return o;
        }

        Dictionary<string, object> Jr6JudgePath(string path)
        {
            jr6Moments.TryGetValue(path ?? "", out var m);
            var cap = ReadAndDelete(path);
            if (m != null) jr6Moments.Remove(path);
            return Judge(m, cap, MosaicWide.Enabled);
        }

        Dictionary<string, object> Jr6Folder(string folder)
        {
            string full = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var mine = jr6Moments.Values.Where(x => x.Path != null && Path.GetFullPath(x.Path).StartsWith(full, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Id).ToList();
            var list = new List<object>();
            bool wide = MosaicWide.Enabled;
            foreach (var m in mine) { list.Add(Judge(m, ReadAndDelete(m.Path), wide)); jr6Moments.Remove(m.Path); }
            return new Dictionary<string, object> { ["moments"] = list, ["errors"] = jr6Errors.ToArray() };
        }

        void Jr6Restore()
        {
            Jr6Disarm();
            var f = fix;
            fix = null;
            if (f != null)
            {
                foreach (var (m, t) in f.Saved) if (m) m.SetTexture("_MainTex", t);
                if (!f.Male && f.Ctx?.BodyDraw)
                {
                    f.Ctx.BodyDraw.SetTexture("_MainTex", f.BodyTex);
                    foreach (var (p, v) in f.Alpha) f.Ctx.BodyDraw.SetFloat(p, v);
                    try { RunStage(f.Ctx); } catch (Exception) { }
                }
                else if (f.Ctx?.BodyDraw) foreach (var (p, v) in f.Alpha) f.Ctx.BodyDraw.SetFloat(p, v);
                foreach (var g in f.Shown) if (g) g.SetActive(false);
                foreach (var (g, on) in f.Hidden) if (g) g.SetActive(on);
            }
            if (jr6WideSaved) { MosaicWide.Enabled = jr6Wide0; jr6WideSaved = false; }
            SetWideDebugView(false);
            jr6Moments.Clear();
            foreach (var o in jr6Trash) if (o) Object.Destroy(o);
            jr6Trash.Clear();
            jr6Cam = null; magenta = null; plain = null;
        }
    }
}
