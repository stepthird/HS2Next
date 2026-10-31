// Builds the Windows players: the smoke player of the player smoke test
// (Runtime/App/PlayerSmoke.cs) and the app player (the HS2Next app with its bridge, started by AppBootstrap.AutoStart).
// Run: Tools/unity_batch.ps1 -Method PlayerBuild.Build    -LogName playerbuild -TimeoutMin 90 [-MethodArgs '-buildOut','<exe path>']
//      Tools/unity_batch.ps1 -Method PlayerBuild.BuildApp -LogName appbuild   -TimeoutMin 90 [-MethodArgs '-buildOut','<exe path>']
// Both: Standalone Windows 64, Mono, non-development, managed stripping as set (Disabled), the graphics APIs as set
// (default: D3D12 then D3D11), the app's URP asset (AppPipeline.EditorAssetPath = Assets/HS2Next/Settings/HS2NextURP.asset
// since Phase 3.2: Forward+, rendering layers; choosing the APIs is Phase 5.3). Outputs (Builds/ is gitignored):
//   Build    (smoke): one scene Assets/HS2Next/Scenes/Smoke.unity (written here: a PlayerSmoke component whose femaleCard is
//            Tools/targets.json "visual.female", a path relative to the game's UserData) -> <project>/Builds/Smoke/HS2Next.exe,
//            report Builds/Smoke.build_report.txt.
//   BuildApp (app):   one scene Assets/HS2Next/Scenes/App.unity (written here: an empty scene, the same as the play-mode
//            harness's NewScene(EmptyScene), so its render settings equal the Editor play tests'; it holds no host: the
//            app starts through AppBootstrap.AutoStart, the one start path of Editor play and players, so -noApp works)
//            -> <project>/Builds/App/HS2Next.exe, report Builds/App.build_report.txt. After the build it writes
//            HS2Next_Data/StreamingAssets/build.json: kind "dev", the bundle version, the short commit, whether the sources
//            that enter a build (Assets, ProjectSettings, Packages) differed from it, and the UTC time. The distribution is
//            not built: Tools/package_dist.ps1 copies this tested build and rewrites the file with kind "distribution".
//   ApplyProjectSettings: the player settings of the app, written into ProjectSettings/ProjectSettings.asset once (run it
//            after Tools/make_icon.py when the icon or a value changes; the build only checks them): company stp3, product
//            HS2Next, the icon Assets/HS2Next/Branding/HS2Next_icon.png for every Standalone size, no splash screen, run in
//            background on, graphics APIs D3D12 then D3D11 (explicit). Run: Tools/unity_batch.ps1 -Method
//            PlayerBuild.ApplyProjectSettings -NoGraphics -LogName projectsettings. The build fails when the company is not
//            stp3 or the icon is missing.
// Scenes: the app scene is rewritten in place each build (GUID kept; an empty scene's content is deterministic); the smoke
// scene only when its content would change (a new GameObject gets new local file ids), so builds leave both unchanged.
// URP is the default render pipeline only while the player is built (URP's build stripping reads the pipeline assets of the
// build target); GraphicsSettings is restored afterwards, so the project stays Built-in for the Editor reference renders.
// A URP build writes its prefiltering fields into the URP asset, the runtime-settings list into
// Assets/UniversalRenderPipelineGlobalSettings.asset and the default components into Assets/DefaultVolumeProfile.asset
//; those are committed as written, so a build leaves the tree clean.
// Shader inclusion: Assets/HS2Next/Resources/HS2NextShaders.shadervariants, a ShaderVariantCollection written here (same
// GUID) only when its content (shaders and variants) changed, so equal content keeps the committed bytes: every shader under Assets/HS2Next/Shaders, for the SRP and ShadowCaster pass types, with every combination of its
// material-local keywords (multi_compile_local, e.g. _DITHERSWITCH_ON / _EMISSIONCOLORTEXBASE_ON), plus URP/Lit with the
// keywords ShaderMap's fallback sets (_NORMALMAP, _ALPHATEST_ON). In a Resources folder the collection and its shaders are in
// every player and Shader.Find resolves them by name; PlayerSmoke and AppPipeline load it and call WarmUp before
// ShaderMap.ApplyUrp. Map shaders outside that folder (URP's SpeedTree7 / Terrain Lit, Unity's skybox shaders) come from
// MapShaderSet (Editor/MapShaderSet.cs, with their keyword sets). The fitted shaders declare only multi_compile / dynamic_branch keywords, so their variant count is the
// declared space after URP's stripping; Lit's shader_feature keywords exist in the player only through this list.
// Report: result, times, sizes, settings, shaders in the build, shader programs per shader before / after scriptable
// stripping, and per shader compiler platform (the graphics APIs of one platform share its programs). Exit code 1 when the
// build fails or a shader under Assets/HS2Next/Shaders, URP/Lit or the collection is not in the build (the packed-asset check).
// -graphicsApis d3d12[,d3d11] (a measurement build, with -buildOut to its own folder): the graphics API list of this build
// only; the project's list is set back after the build in the same process, and ProjectSettings/ProjectSettings.asset keeps
// the bytes it had before the build.
// Paths in the built assemblies: the compiler writes the debug-file path of every assembly and the caller-file arguments of
// the render-graph API into the binaries. While the player is built, the Standalone additional compiler arguments carry a path
// map -pathmap:"<project>"=. (the form the compiler is given for the package assemblies), so those paths start with "." and
// name no local folder; the old list is set back after the build in the same process and ProjectSettings.asset keeps its
// bytes (the Editor's own compile is not changed). After the build every managed assembly is searched for the project folder
// (UTF-8 and UTF-16, either separator, case ignored): one hit fails the build.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

public static class PlayerBuild
{
    /// <summary>The smoke scene (PlayerSmoke).</summary>
    public const string ScenePath = "Assets/HS2Next/Scenes/Smoke.unity";
    /// <summary>The app scene (empty; AppBootstrap.AutoStart creates the host).</summary>
    public const string AppScenePath = "Assets/HS2Next/Scenes/App.unity";
    public const string ShaderDir = "Assets/HS2Next/Shaders";
    public const string ShaderSetPath = "Assets/HS2Next/Resources/HS2NextShaders.shadervariants";
    public const string UrpAssetPath = AppPipeline.EditorAssetPath;
    const string LitName = "Universal Render Pipeline/Lit";
    /// <summary>The player's company: it names the per-user folder (LocalLow/stp3/HS2Next), Unity's preference key and crash folder.</summary>
    public const string Company = "stp3";
    /// <summary>The product name (the window title, the per-user folder): renaming it later moves every user's folder.</summary>
    public const string Product = "HS2Next";
    /// <summary>The icon written by Tools/make_icon.py.</summary>
    public const string IconPath = "Assets/HS2Next/Branding/HS2Next_icon.png";
    static readonly GraphicsDeviceType[] AppApis = { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 };
    static readonly string[][] LitFallbackKeywords =
        { new string[0], new[] { "_NORMALMAP" }, new[] { "_ALPHATEST_ON" }, new[] { "_NORMALMAP", "_ALPHATEST_ON" } };

    /// <summary>What a build entry builds.</summary>
    sealed class Kind
    {
        public string Name;          // "smoke" | "app"
        public string Folder;        // Builds/<Folder>/HS2Next.exe, Builds/<Folder>.build_report.txt
        public Func<List<string>, Action<string>, string> WriteScene;   // (errors, line) -> scene path
    }

    static readonly Kind Smoke = new Kind { Name = "smoke", Folder = "Smoke", WriteScene = WriteSmokeScene };
    static readonly Kind App = new Kind { Name = "app", Folder = "App", WriteScene = WriteAppScene };

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    /// <summary>The smoke player: Builds/Smoke/HS2Next.exe (-buildOut), report Builds/Smoke.build_report.txt.</summary>
    public static void Build() => Run(Smoke);

    /// <summary>The app player: Builds/App/HS2Next.exe (-buildOut), report Builds/App.build_report.txt.</summary>
    public static void BuildApp() => Run(App);

    static void Run(Kind kind)
    {
        int code;
        try { code = BuildInner(kind); }
        catch (Exception e) { Debug.LogError("PLAYER BUILD FAILED: " + e); code = 1; }
        Debug.Log($"PLAYER BUILD DONE exit {code}");
        if (code != 0) EditorApplication.Exit(code);
    }

    static int BuildInner(Kind kind)
    {
        var sb = new StringBuilder();
        void Line(string s) { sb.AppendLine(s); Debug.Log("PLAYER BUILD " + s); }
        var errors = new List<string>();
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string exe = Path.GetFullPath(Arg("-buildOut", Path.Combine(project, "Builds", kind.Folder, "HS2Next.exe")));
        string outDir = Path.GetDirectoryName(exe);
        string reportFile = Path.Combine(Path.GetDirectoryName(outDir), Path.GetFileName(outDir) + ".build_report.txt");
        Line($"{kind.Name} player build {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}");

        // 1. shaders and the shader set
        var shaders = WriteShaderSet(errors, Line);

        // 2. scene
        string scenePath = kind.WriteScene(errors, Line);
        if (errors.Count > 0) return Finish(sb, errors, reportFile);

        // 3. settings
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
        if (!urp) { errors.Add("URP asset not found: " + UrpAssetPath); return Finish(sb, errors, reportFile); }
        var apis = ApiOverride(errors);
        if (errors.Count > 0) return Finish(sb, errors, reportFile);
        // the state of the sources before the build (the build itself rewrites settings and writes temporary files)
        string commit = kind == App ? Git(project, "rev-parse --short HEAD")?.Trim() : null;
        string status = kind == App ? Git(project, "status --porcelain -- Assets ProjectSettings Packages") : null;
        ApiState apiState = apis != null ? ApiState.Set(project, apis, Line) : null;
        PathMapState pathMap = null;

        BuildReport report;
        long wall;
        bool restored;
        try
        {
            CheckSettings(Line, errors);
            if (errors.Count > 0) return Finish(sb, errors, reportFile);

            // 4. build with URP as the default pipeline
            // The player creates a directory junction <outDir>/abdata to the HS2 install's abdata (map clips
            // 12): unlink it before the build so that nothing that cleans or rewrites the output folder can follow it into the
            // game data (the link only, never recursively; the player makes it again at its first map load).
            string link = Path.Combine(outDir, "abdata");
            if (HS2Next.Scenes.Runtime.MapAudio.IsReparsePoint(link))
                Line("abdata junction next to the player: " + (HS2Next.Scenes.Runtime.MapAudio.RemoveJunction(link) ? "unlinked before the build" : "could NOT be unlinked"));
            pathMap = PathMapState.Set(project, Line);
            report = BuildWithUrp(urp, scenePath, exe, out wall, out restored);
        }
        finally
        {
            pathMap?.Restore(Line, errors);
            apiState?.Restore(Line, errors);
        }
        var sum = report.summary;
        Line($"result {sum.result}: {sum.totalErrors} errors, {sum.totalWarnings} warnings; build time {sum.totalTime.TotalSeconds:F0} s (wall {wall / 1000.0:F0} s); " +
             $"size {sum.totalSize / 1048576.0:F1} MB (report), folder {FolderMb(outDir):F1} MB without Cache/ and UserData/");
        Line("output: " + exe);
        Line("pipeline restored: " + (restored ? "yes" : "NO"));
        if (!restored) errors.Add("GraphicsSettings.defaultRenderPipeline was not restored");
        Line("steps:");
        foreach (var st in report.steps.Where(s => s.depth <= 1))
            Line($"   {new string(' ', st.depth * 2)}{st.name}: {st.duration.TotalSeconds:F1} s");
        if (sum.result != BuildResult.Succeeded)
        {
            foreach (var st in report.steps)
                foreach (var m in st.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                    errors.Add($"{st.name}: {m.content.Split('\n')[0]}");
            if (errors.Count == 0) errors.Add("build result " + sum.result);
            return Finish(sb, errors, reportFile);
        }

        // 5. what went in (the packed-asset check); no project folder in the built assemblies
        CheckPackedAssets(report, shaders, errors, Line);
        CheckProjectFolder(project, exe, errors, Line);
        if (kind == App) WriteBuildFile(exe, commit, status, errors, Line);

        // 6. shader programs (one per pass, stage and keyword set) before / after scriptable stripping
        ReportVariantCounts(Line);
        return Finish(sb, errors, reportFile);
    }

    // ------------------------------------------------------------ shared steps

    /// <summary>Writes the shader set (same GUID) when its content changed and returns the shaders under <see cref="ShaderDir"/>.</summary>
    static List<(string path, Shader shader)> WriteShaderSet(List<string> errors, Action<string> line)
    {
        var shaders = AssetDatabase.FindAssets("t:Shader", new[] { ShaderDir })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".shader")).OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (path: p, shader: AssetDatabase.LoadAssetAtPath<Shader>(p))).ToList();
        foreach (var (p, s) in shaders)
            if (!s) errors.Add($"cannot load {p}");
            else if (ShaderUtil.ShaderHasError(s)) errors.Add($"shader has compile errors: {s.name} ({p})");
        var lit = Shader.Find(LitName);
        if (!lit) errors.Add("URP/Lit not found");
        // The wanted content is built in a collection outside the asset database and compared with the asset by content:
        // the file is written only when the content differs (Unity writes a collection's shaders in the order of their object
        // instance ids, which differ between a fresh import and an older Library, so a rewrite of equal content changes bytes).
        var asset = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(ShaderSetPath);
        var svc = new ShaderVariantCollection();
        var listed = new List<string>();
        foreach (var (p, s) in shaders.Where(x => x.shader))
        {
            var local = s.keywordSpace.keywords.Where(k => !k.isOverridable && !k.isDynamic && k.type == ShaderKeywordType.UserDefined)
                .Select(k => k.name).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
            int n = AddVariants(svc, s, Combos(local));
            listed.Add($"{s.name}: {n} variants (material keywords: {(local.Length == 0 ? "none" : string.Join(" ", local))})");
            if (n == 0) errors.Add($"no variant of {s.name} could be listed");
        }
        if (lit) listed.Add($"{LitName}: {AddVariants(svc, lit, LitFallbackKeywords)} variants (fallback keywords _NORMALMAP, _ALPHATEST_ON)");
        // package and built-in shaders the map shader families map to (SpeedTree7, Terrain Lit, skyboxes; Phase 3.2 package B)
        listed.AddRange(MapShaderSet.Add(svc, errors));
        // the Phase 3.3 effect shaders and package S's quality set (EffectShaderSet, package C)
        listed.AddRange(EffectShaderSet.Add(svc, errors));
        var wanted = VariantLines(svc);
        string state;
        if (!asset)
        {
            AssetDatabase.CreateAsset(svc, ShaderSetPath);
            asset = svc;
            state = "created";
        }
        else if (asset.shaderCount == svc.shaderCount && VariantLines(asset).SequenceEqual(wanted, StringComparer.Ordinal))
        {
            UnityEngine.Object.DestroyImmediate(svc);
            state = "unchanged, not written";
        }
        else
        {
            asset.Clear();
            foreach (var (shader, pass, keywords) in Entries(svc))
            {
                try { asset.Add(new ShaderVariantCollection.ShaderVariant(shader, (PassType)pass, keywords)); }
                catch (Exception e) { errors.Add($"the shader set cannot take a variant of {(shader ? shader.name : "a missing shader")}: {e.Message}"); }
            }
            UnityEngine.Object.DestroyImmediate(svc);
            if (!VariantLines(asset).SequenceEqual(wanted, StringComparer.Ordinal)) errors.Add("the shader set did not take the wanted variants");
            EditorUtility.SetDirty(asset);
            state = "content changed, written";
        }
        AssetDatabase.SaveAssets();
        line($"shader set {ShaderSetPath}: {asset.shaderCount} shaders, {asset.variantCount} variants ({state})");
        foreach (var l in listed) line("   " + l);
        return shaders;
    }

    /// <summary>The variants of a collection as its serialised data holds them (shader, pass type, keywords).</summary>
    static List<(Shader shader, int pass, string[] keywords)> Entries(ShaderVariantCollection svc)
    {
        var list = new List<(Shader, int, string[])>();
        using (var so = new SerializedObject(svc))
        {
            var shaders = so.FindProperty("m_Shaders");
            for (int i = 0; shaders != null && i < shaders.arraySize; i++)
            {
                var e = shaders.GetArrayElementAtIndex(i);
                var shader = e.FindPropertyRelative("first").objectReferenceValue as Shader;
                var variants = e.FindPropertyRelative("second").FindPropertyRelative("variants");
                for (int j = 0; j < variants.arraySize; j++)
                {
                    var v = variants.GetArrayElementAtIndex(j);
                    list.Add((shader, v.FindPropertyRelative("passType").intValue,
                        (v.FindPropertyRelative("keywords").stringValue ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)));
                }
            }
        }
        return list;
    }

    /// <summary>
    /// The content of a collection in a fixed order: one line per variant, "shader-guid:local-id pass-type keywords" with the
    /// keywords sorted, the lines sorted ordinally (independent of the order the collection is serialised in).
    /// </summary>
    static List<string> VariantLines(ShaderVariantCollection svc)
    {
        var lines = new List<string>();
        foreach (var (shader, pass, keywords) in Entries(svc))
        {
            string id = shader && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string guid, out long local)
                ? guid + ":" + local : "missing " + (shader ? shader.name : "shader");
            lines.Add(id + " " + pass + " " + string.Join(" ", keywords.OrderBy(k => k, StringComparer.Ordinal)));
        }
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    static string WriteSmokeScene(List<string> errors, Action<string> line)
    {
        string female = TestTargets.Load().visual?.female;
        if (string.IsNullOrEmpty(female)) errors.Add("Tools/targets.json has no visual.female");
        // A new GameObject gets new local file ids, so the scene is written only when its content would change.
        if (SmokeSceneMatches(female ?? ""))
        {
            line($"scene {ScenePath}: unchanged (PlayerSmoke.femaleCard = {female})");
            return ScenePath;
        }
        var scene = NewSceneAt(ScenePath);
        RenderSettings.skybox = null;
        var smoke = new GameObject("PlayerSmoke").AddComponent<PlayerSmoke>();
        smoke.femaleCard = female ?? "";
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) errors.Add("cannot save " + ScenePath);
        line($"scene {ScenePath}: PlayerSmoke.femaleCard = {female}");
        return ScenePath;
    }

    /// <summary>True when the smoke scene exists with exactly one root "PlayerSmoke" whose femaleCard is <paramref name="female"/> and no skybox.</summary>
    static bool SmokeSceneMatches(string female)
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!File.Exists(Path.Combine(project, ScenePath))) return false;
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            return roots.Length == 1 && roots[0].name == "PlayerSmoke" && roots[0].TryGetComponent<PlayerSmoke>(out var smoke) &&
                   smoke.femaleCard == female && RenderSettings.skybox == null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("PLAYER BUILD cannot read " + ScenePath + ": " + e.Message);
            return false;
        }
    }

    static string WriteAppScene(List<string> errors, Action<string> line)
    {
        // Exactly the play-mode harness's scene (PlayModeHarness.Start: NewScene(EmptyScene)), nothing changed, so the
        // render settings of the app's start scene are those of the Editor play tests.
        var scene = NewSceneAt(AppScenePath);
        if (!EditorSceneManager.SaveScene(scene, AppScenePath)) errors.Add("cannot save " + AppScenePath);
        line($"scene {AppScenePath}: empty (no host object; AppBootstrap.AutoStart creates \"{HS2NextApp.ObjectName}\")");
        return AppScenePath;
    }

    static UnityEngine.SceneManagement.Scene NewSceneAt(string path)
    {
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Directory.CreateDirectory(Path.Combine(project, Path.GetDirectoryName(path)));
        return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    /// <summary>
    /// Writes &lt;exe&gt;_Data/StreamingAssets/build.json of a development build (kind dev). The commit and the dirty flag come
    /// from git before the build (<paramref name="commit"/>, <paramref name="status"/>); without git they stay unknown (null)
    /// and the report says so.
    /// </summary>
    static void WriteBuildFile(string exe, string commit, string status, List<string> errors, Action<string> line)
    {
        string dir = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data", "StreamingAssets");
        var f = new HS2Next.Bridge.BuildFacts
        {
            Kind = HS2Next.Bridge.BuildFacts.KindDev,
            Version = PlayerSettings.bundleVersion,
            Commit = commit,
            BuiltUtc = HS2Next.Bridge.BuildFacts.UtcStamp(DateTime.UtcNow),
        };
        if (string.IsNullOrEmpty(f.Commit)) f.Commit = null;
        f.Dirty = status == null ? (bool?)null : status.Trim().Length > 0;
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, HS2Next.Bridge.BuildFacts.FileName), f.ToFileText(), new UTF8Encoding(false));
            var back = HS2Next.Bridge.BuildFacts.Read(dir, false, PlayerSettings.bundleVersion);
            if (back.Error != null || back.Kind != f.Kind || back.Commit != f.Commit) errors.Add("build.json does not read back: " + (back.Error ?? back.Summary));
            line($"build file {Path.GetFileNameWithoutExtension(exe)}_Data/StreamingAssets/{HS2Next.Bridge.BuildFacts.FileName}: {f.Summary}" +
                 (f.Commit == null ? " (git not available: commit unknown)" : ""));
        }
        catch (Exception e) { errors.Add("cannot write build.json: " + e.Message); }
    }

    /// <summary>The output of git &lt;args&gt; in the project, or null when git cannot run or fails (30 s at most).</summary>
    static string Git(string project, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return null;
                var err = p.StandardError.ReadToEndAsync();
                string o = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch (Exception) { } return null; }
                return p.ExitCode == 0 ? o : null;
            }
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The player settings of the app (see the file header), saved into ProjectSettings/ProjectSettings.asset. The icon PNG must
    /// exist (python -I Tools/make_icon.py); its import settings are set here (no mipmaps, uncompressed, alpha as transparency).
    /// </summary>
    public static void ApplyProjectSettings()
    {
        int code = 0;
        try
        {
            AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);
            if (!(AssetImporter.GetAtPath(IconPath) is TextureImporter imp)) throw new FileNotFoundException("no icon " + IconPath + ": run python -I Tools/make_icon.py first");
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (!icon) throw new InvalidOperationException("the icon did not import: " + IconPath);

            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, AppApis);
            int sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any).Length;
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, Enumerable.Repeat(icon, sizes).ToArray(), IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log($"PROJECT SETTINGS applied: company {PlayerSettings.companyName}, product {PlayerSettings.productName}, icon {IconPath} " +
                      $"({icon.width} x {icon.height}, {sizes} Standalone sizes), splash {(PlayerSettings.SplashScreen.show ? "on" : "off")}, " +
                      $"run in background {PlayerSettings.runInBackground}, graphics APIs [{string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64))}]");
        }
        catch (Exception e) { Debug.LogError("PROJECT SETTINGS FAILED: " + e); code = 1; }
        Debug.Log($"PROJECT SETTINGS DONE exit {code}");
        if (code != 0) EditorApplication.Exit(code);
    }

    /// <summary>The settings lines of the report; a company other than stp3 and a missing icon are build errors.</summary>
    static void CheckSettings(Action<string> line, List<string> errors)
    {
        var named = NamedBuildTarget.Standalone;
        if (PlayerSettings.GetScriptingBackend(named) != ScriptingImplementation.Mono2x)
        {
            line($"scripting backend was {PlayerSettings.GetScriptingBackend(named)}: set to Mono2x");
            PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.Mono2x);
        }
        line($"settings: backend {PlayerSettings.GetScriptingBackend(named)}, API {PlayerSettings.GetApiCompatibilityLevel(named)}, " +
             $"managed stripping {PlayerSettings.GetManagedStrippingLevel(named)}, strip engine code {PlayerSettings.stripEngineCode}, " +
             $"graphics APIs {(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64) ? "default " : "")}" +
             $"[{string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64))}], colour space {PlayerSettings.colorSpace}, " +
             $"window {PlayerSettings.fullScreenMode} {PlayerSettings.defaultScreenWidth}x{PlayerSettings.defaultScreenHeight}, run in background {PlayerSettings.runInBackground}, " +
             $"quality levels with their own pipeline {Enumerable.Range(0, QualitySettings.count).Count(i => QualitySettings.GetRenderPipelineAssetAt(i))}");
        var icons = PlayerSettings.GetIcons(NamedBuildTarget.Standalone, IconKind.Any);
        bool iconOk = icons.Length > 0 && icons.All(t => t && AssetDatabase.GetAssetPath(t) == IconPath);
        bool explicitApis = !PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64);
        line($"player: company {PlayerSettings.companyName}, product {PlayerSettings.productName}, version {PlayerSettings.bundleVersion}, " +
             $"icon {(iconOk ? "present (" + IconPath + ", " + icons.Length + " sizes)" : "MISSING")}, splash {(PlayerSettings.SplashScreen.show ? "on" : "off")}, " +
             $"graphics APIs {(explicitApis ? "explicit" : "default")} [{string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64))}], " +
             $"run in background {(PlayerSettings.runInBackground ? "true" : "false")}, quality levels {QualitySettings.count}");
        if (PlayerSettings.companyName != Company) errors.Add($"the company is {PlayerSettings.companyName}, not {Company} (run PlayerBuild.ApplyProjectSettings)");
        if (!iconOk) errors.Add("the icon " + IconPath + " is not the Standalone icon (python -I Tools/make_icon.py, then PlayerBuild.ApplyProjectSettings)");
    }

    /// <summary>The graphics API list of -graphicsApis (d3d12, d3d11, comma-separated, in order), or null without the argument.</summary>
    static GraphicsDeviceType[] ApiOverride(List<string> errors)
    {
        string v = Arg("-graphicsApis", null);
        if (string.IsNullOrEmpty(v)) return null;
        var list = new List<GraphicsDeviceType>();
        foreach (var part in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "d3d12": list.Add(GraphicsDeviceType.Direct3D12); break;
                case "d3d11": list.Add(GraphicsDeviceType.Direct3D11); break;
                default: errors.Add("-graphicsApis: unknown graphics API \"" + part.Trim() + "\" (d3d12, d3d11)"); return null;
            }
        }
        if (list.Count == 0 || list.Distinct().Count() != list.Count) { errors.Add("-graphicsApis: give d3d12 and / or d3d11 once each"); return null; }
        return list.ToArray();
    }

    /// <summary>
    /// The Windows graphics API list of one measurement build: set before the build, set back afterwards in the same process.
    /// Unity serialises an automatic list that was once explicit differently from one that never was, so after setting the
    /// values back the settings are saved and ProjectSettings.asset gets back the bytes it had before (the values are equal).
    /// </summary>
    sealed class ApiState
    {
        const BuildTarget Target = BuildTarget.StandaloneWindows64;
        string path;
        byte[] bytes;
        bool useDefault;
        GraphicsDeviceType[] list;

        public static ApiState Set(string project, GraphicsDeviceType[] apis, Action<string> line)
        {
            var s = new ApiState
            {
                path = Path.Combine(project, "ProjectSettings", "ProjectSettings.asset"),
                useDefault = PlayerSettings.GetUseDefaultGraphicsAPIs(Target),
                list = PlayerSettings.GetGraphicsAPIs(Target),
            };
            s.bytes = File.Exists(s.path) ? File.ReadAllBytes(s.path) : null;
            PlayerSettings.SetUseDefaultGraphicsAPIs(Target, false);
            PlayerSettings.SetGraphicsAPIs(Target, apis);
            line($"graphics APIs of this build only: [{string.Join(", ", apis)}] (the project's: {(s.useDefault ? "default " : "")}[{string.Join(", ", s.list)}])");
            return s;
        }

        public void Restore(Action<string> line, List<string> errors)
        {
            try
            {
                PlayerSettings.SetGraphicsAPIs(Target, list);
                PlayerSettings.SetUseDefaultGraphicsAPIs(Target, useDefault);
                AssetDatabase.SaveAssets();
                bool same = bytes == null || File.ReadAllBytes(path).SequenceEqual(bytes);
                if (!same) File.WriteAllBytes(path, bytes);
                bool ok = PlayerSettings.GetUseDefaultGraphicsAPIs(Target) == useDefault && PlayerSettings.GetGraphicsAPIs(Target).SequenceEqual(list);
                line($"graphics APIs set back: {(ok ? "yes" : "NO")}, {(useDefault ? "default " : "")}[{string.Join(", ", PlayerSettings.GetGraphicsAPIs(Target))}]; " +
                     $"ProjectSettings.asset {(same ? "unchanged" : "given back its bytes from before the build")}");
                if (!ok) errors.Add("the graphics API list was not set back");
            }
            catch (Exception e) { errors.Add("setting the graphics API list back failed: " + e.Message); }
        }
    }

    /// <summary>The compiler argument that maps the project folder to "." (no trailing separator: the compiler adds it to both sides).</summary>
    public static string PathMapArgument(string project) => "-pathmap:\"" + project.TrimEnd('\\', '/') + "\"=.";

    /// <summary>
    /// The path map of one player build: the Standalone additional compiler arguments get <see cref="PathMapArgument"/> before
    /// the build and their old list back afterwards in the same process. The settings are saved after setting the list back
    /// and ProjectSettings.asset gets back the bytes it had before (Unity may serialise a list that was set and set back
    /// differently); a file that still differs fails the build.
    /// </summary>
    sealed class PathMapState
    {
        static readonly NamedBuildTarget Target = NamedBuildTarget.Standalone;
        string path;
        byte[] bytes;
        string[] old;

        public static PathMapState Set(string project, Action<string> line)
        {
            var s = new PathMapState
            {
                path = Path.Combine(project, "ProjectSettings", "ProjectSettings.asset"),
                old = PlayerSettings.GetAdditionalCompilerArguments(Target) ?? new string[0],
            };
            s.bytes = File.Exists(s.path) ? File.ReadAllBytes(s.path) : null;
            string map = PathMapArgument(project);
            PlayerSettings.SetAdditionalCompilerArguments(Target, s.old.Concat(new[] { map }).ToArray());
            line($"path map of this build: {map} (the Standalone additional compiler arguments: [{string.Join(" ", s.old)}] + the map; " +
                 $"now [{string.Join(" ", PlayerSettings.GetAdditionalCompilerArguments(Target))}])");
            return s;
        }

        public void Restore(Action<string> line, List<string> errors)
        {
            try
            {
                PlayerSettings.SetAdditionalCompilerArguments(Target, old);
                AssetDatabase.SaveAssets();
                bool same = bytes == null || File.ReadAllBytes(path).SequenceEqual(bytes);
                if (!same) File.WriteAllBytes(path, bytes);
                bool back = bytes == null || File.ReadAllBytes(path).SequenceEqual(bytes);
                bool ok = PlayerSettings.GetAdditionalCompilerArguments(Target).SequenceEqual(old);
                line($"path map set back: {(ok ? "yes" : "NO")}, the additional compiler arguments [{string.Join(" ", PlayerSettings.GetAdditionalCompilerArguments(Target))}]; " +
                     $"ProjectSettings.asset {(same ? "unchanged" : back ? "given back its bytes from before the build" : "DIFFERS from before the build")}");
                if (!ok) errors.Add("the additional compiler arguments were not set back");
                if (!back) errors.Add("ProjectSettings.asset differs from before the build");
            }
            catch (Exception e) { errors.Add("setting the additional compiler arguments back failed: " + e.Message); }
        }
    }

    /// <summary>
    /// Fails the build when a managed assembly of the player names the project folder (UTF-8 or UTF-16, either separator,
    /// ASCII letters in any case): with the path map every compiler-written path starts with ".".
    /// </summary>
    static void CheckProjectFolder(string project, string exe, List<string> errors, Action<string> line)
    {
        string managed = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data", "Managed");
        if (!Directory.Exists(managed)) { errors.Add("no managed folder in the build: " + managed); return; }
        string root = project.TrimEnd('\\', '/');
        var patterns = new List<byte[]>();
        foreach (char sep in new[] { '\\', '/' })
        {
            string p = root.Replace('\\', sep).Replace('/', sep) + sep;
            patterns.Add(Lowered(Encoding.UTF8.GetBytes(p)));
            patterns.Add(Lowered(Encoding.Unicode.GetBytes(p)));
        }
        var files = Directory.GetFiles(managed, "*.dll").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var holders = new List<string>();
        foreach (var f in files)
        {
            byte[] data = File.ReadAllBytes(f);
            if (patterns.Any(p => Holds(data, p))) holders.Add(Path.GetFileName(f));
        }
        line($"project folder in the built assemblies: {holders.Count} of {files.Length} managed files" +
             (holders.Count > 0 ? ": " + string.Join(", ", holders.Take(10)) + (holders.Count > 10 ? ", ..." : "") : ""));
        if (holders.Count > 0) errors.Add($"{holders.Count} built assemblies name the project folder (the compiler's path map did not reach their compile)");
    }

    static byte Lower(byte b) => b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b | 0x20) : b;

    static byte[] Lowered(byte[] a) => a.Select(Lower).ToArray();

    /// <summary>True when <paramref name="data"/> holds <paramref name="lowered"/> with ASCII letters compared in any case.</summary>
    static bool Holds(byte[] data, byte[] lowered)
    {
        int last = data.Length - lowered.Length;
        byte first = lowered[0];
        for (int i = 0; i <= last; i++)
        {
            if (Lower(data[i]) != first) continue;
            int k = 1;
            while (k < lowered.Length && Lower(data[i + k]) == lowered[k]) k++;
            if (k == lowered.Length) return true;
        }
        return false;
    }

    /// <summary>BuildPlayer of one scene with <paramref name="urp"/> as the default pipeline, restored afterwards; the variant counters run meanwhile.</summary>
    static BuildReport BuildWithUrp(UniversalRenderPipelineAsset urp, string scenePath, string exe, out long wallMs, out bool restored)
    {
        var prevPipeline = GraphicsSettings.defaultRenderPipeline;
        VariantCount.Reset(true);
        BuildReport report;
        var sw = Stopwatch.StartNew();
        try
        {
            GraphicsSettings.defaultRenderPipeline = urp;
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            });
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = prevPipeline;
            VariantCount.Reset(false);
        }
        wallMs = sw.ElapsedMilliseconds;
        restored = GraphicsSettings.defaultRenderPipeline == prevPipeline;
        return report;
    }

    static void CheckPackedAssets(BuildReport report, List<(string path, Shader shader)> shaders, List<string> errors, Action<string> line)
    {
        var inBuild = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var pa in report.packedAssets)
            foreach (var c in pa.contents)
                if (c.type == typeof(Shader) || c.type == typeof(ShaderVariantCollection))
                    inBuild[$"{c.type.Name} {c.sourceAssetPath}"] = (inBuild.TryGetValue($"{c.type.Name} {c.sourceAssetPath}", out var v) ? v : 0) + (long)c.packedSize;
        line($"shaders and collections in the build: {inBuild.Count}");
        foreach (var kv in inBuild) line($"   {kv.Key}: {kv.Value / 1024.0:F0} KB");
        int before = errors.Count;
        foreach (var (p, s) in shaders)
            if (!inBuild.Keys.Any(k => k.EndsWith(" " + p))) errors.Add($"not in the build: {s?.name} ({p})");
        if (!inBuild.Keys.Any(k => k.StartsWith("Shader ") && k.EndsWith("/Lit.shader") && k.Contains("render-pipelines.universal"))) errors.Add("not in the build: " + LitName);
        if (!inBuild.Keys.Any(k => k.EndsWith(ShaderSetPath))) errors.Add("not in the build: " + ShaderSetPath);
        line($"packed-asset check: {(errors.Count == before ? "PASS" : "FAIL")} ({shaders.Count} fitted shaders, {LitName}, the shader set)");
    }

    static void ReportVariantCounts(Action<string> line)
    {
        var counts = VariantCount.Snapshot();
        long tb = counts.Sum(c => c.Value.before), ta = counts.Sum(c => c.Value.after);
        line($"shader programs: {tb} after built-in stripping -> {ta} after scriptable (URP) stripping, in {counts.Count} shaders");
        long hb = 0, ha = 0;
        foreach (var kv in counts.Where(c => c.Key.StartsWith("HS2Next/") || c.Key == LitName).OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            line($"   {kv.Key}: {kv.Value.before} -> {kv.Value.after}");
            if (kv.Key.StartsWith("HS2Next/")) { hb += kv.Value.before; ha += kv.Value.after; }
        }
        line($"   HS2Next/* total: {hb} -> {ha}");
        foreach (var kv in counts.Where(c => !c.Key.StartsWith("HS2Next/") && c.Key != LitName).OrderByDescending(c => c.Value.after).Take(15))
            line($"   (other) {kv.Key}: {kv.Value.before} -> {kv.Value.after}");
        // the same programs per shader compiler platform (every graphics API of a platform uses its programs)
        var perPlatform = VariantCount.SnapshotByPlatform();
        foreach (var plat in perPlatform.Select(c => c.Key.platform).Distinct().OrderBy(p => p, StringComparer.Ordinal))
        {
            var mine = perPlatform.Where(c => c.Key.platform == plat).ToList();
            var own = mine.Where(c => c.Key.shader.StartsWith("HS2Next/")).ToList();
            line($"shader programs for compiler platform {plat}: {mine.Sum(c => c.Value.before)} -> {mine.Sum(c => c.Value.after)}; " +
                 $"HS2Next/* {own.Sum(c => c.Value.before)} -> {own.Sum(c => c.Value.after)}");
            foreach (var kv in own.OrderByDescending(c => c.Value.after).ThenBy(c => c.Key.shader, StringComparer.Ordinal).Take(15))
                line($"   (top HS2Next) {kv.Key.shader}: {kv.Value.before} -> {kv.Value.after}");
        }
    }

    static int Finish(StringBuilder sb, List<string> errors, string reportFile)
    {
        sb.Insert(0, $"RESULT: {(errors.Count == 0 ? "PASS" : "FAIL")}\n");
        foreach (var e in errors) { sb.AppendLine("FAIL " + e); Debug.LogError("PLAYER BUILD FAIL " + e); }
        Directory.CreateDirectory(Path.GetDirectoryName(reportFile));
        File.WriteAllText(reportFile, sb.ToString(), new UTF8Encoding(false));
        Debug.Log("PLAYER BUILD report: " + reportFile);
        return errors.Count == 0 ? 0 : 1;
    }

    // Every subset of the keywords (at most 3 keywords; above that: none plus each alone).
    static IEnumerable<string[]> Combos(string[] kw)
    {
        if (kw.Length > 3) return new[] { new string[0] }.Concat(kw.Select(k => new[] { k }));
        return Enumerable.Range(0, 1 << kw.Length).Select(m => kw.Where((k, i) => (m & (1 << i)) != 0).ToArray());
    }

    static int AddVariants(ShaderVariantCollection svc, Shader s, IEnumerable<string[]> combos)
    {
        int n = 0;
        foreach (var pt in new[] { PassType.ScriptableRenderPipeline, PassType.ShadowCaster })
            foreach (var kw in combos)
            {
                ShaderVariantCollection.ShaderVariant v;
                try { v = new ShaderVariantCollection.ShaderVariant(s, pt, kw); }
                catch (Exception) { continue; }   // no such pass type or keyword set in this shader
                if (svc.Add(v)) n++;
            }
        return n;
    }

    /// <summary>MB of the player folder without the run-time folders a player writes next to itself (Cache/, UserData/) and
    /// without links (reparse points: the abdata junction to the game data is not entered).</summary>
    static double FolderMb(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        var skip = new HashSet<string>(new[] { "Cache", "UserData" }.Select(d => Path.GetFullPath(Path.Combine(dir, d))), StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        var stack = new Stack<string>();
        stack.Push(dir);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            foreach (var f in Directory.GetFiles(d)) bytes += new FileInfo(f).Length;
            foreach (var sub in Directory.GetDirectories(d))
                if (!skip.Contains(Path.GetFullPath(sub)) && !HS2Next.Scenes.Runtime.MapAudio.IsReparsePoint(sub)) stack.Push(sub);
        }
        return bytes / 1048576.0;
    }

    // Shader program counts per shader: before (first callback) and after (last callback) the scriptable strippers
    // (URP/core strip at callbackOrder 0). Active only while a PlayerBuild entry builds.
    public static class VariantCount
    {
        static readonly object Lock = new object();
        static readonly Dictionary<string, (long before, long after)> counts = new Dictionary<string, (long, long)>();
        static readonly Dictionary<(string shader, string platform), (long before, long after)> byPlatform =
            new Dictionary<(string, string), (long, long)>();
        public static bool Active { get; private set; }

        public static void Reset(bool active) { lock (Lock) { if (active) { counts.Clear(); byPlatform.Clear(); } Active = active; } }

        public static Dictionary<string, (long before, long after)> Snapshot() { lock (Lock) return new Dictionary<string, (long, long)>(counts); }

        public static Dictionary<(string shader, string platform), (long before, long after)> SnapshotByPlatform()
        {
            lock (Lock) return new Dictionary<(string, string), (long, long)>(byPlatform);
        }

        public static void Add(Shader s, IList<ShaderCompilerData> data, bool after)
        {
            if (!Active) return;
            lock (Lock)
            {
                string k = s ? s.name : "(null)";
                int n = data.Count;
                counts.TryGetValue(k, out var c);
                counts[k] = after ? (c.before, c.after + n) : (c.before + n, c.after);
                for (int i = 0; i < n; i++)
                {
                    var key = (k, data[i].shaderCompilerPlatform.ToString());
                    byPlatform.TryGetValue(key, out var p);
                    byPlatform[key] = after ? (p.before, p.after + 1) : (p.before + 1, p.after);
                }
            }
        }
    }

    public sealed class CountBefore : IPreprocessShaders
    {
        public int callbackOrder => int.MinValue;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data) => VariantCount.Add(shader, data, false);
    }

    public sealed class CountAfter : IPreprocessShaders
    {
        public int callbackOrder => int.MaxValue;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data) => VariantCount.Add(shader, data, true);
    }
}
