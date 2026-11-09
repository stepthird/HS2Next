// TestHygiene: the state check between the members of a HeadlessBatch (one Unity process, many test rows).
// Begin() records the baseline at the batch start (nothing has run: the product defaults). BeforeMember() notes what a
// member starts with; AfterMember() puts the known process state back and checks for leaks:
//   reset (silently, listed as "reset"): the known statics - the texture guard's counters and switches, the shader map's
//     supplies and the fitted table, the build hooks, the Maker services, the part thumbnails' shared instance, the catalog's
//     switches and the warm catalog's notes, the loose PNG readability switch, the map's asynchronous open switch, the map
//     options the app sets from the settings on every boot (MapOptions), the
//     texture check's statics - the HS2NEXT_* / HS2N_* environment, the scene setup, every open asset bundle and the
//     loaded-object caches that go with them; then GC.Collect and the unused-asset sweep;
//   leaks (each FAILs the member with the item named, and is cleaned so the next member starts clean): a GameObject in a
//     loaded scene the member left, an editor update / delayCall callback it left registered, a build hook / node provider
//     / shader supply / registration left in a registry, a public static switch (a field of a value type or string, or a
//     field of a public options object) of any other class left away from its default.
//   Product states on a fixed list (KnownStates: the product's counters, last timings and reasons, the UI singletons a panel
//     build sets) are put back, counted and named in the member's "reset" notes as "(listed state)"; a static property with a
//     non-public setter outside the list that changed is put back and is a finding. Callbacks the engine's packages queued are
//     dropped and listed (they never run in a row alone).
//   A public static reference of another class is listed as "kept" when the member changed it.
//   Those references (hooks the product installs once, services) are not put back: an installer that guards itself with its
//     own flag would not install them again.
// Numbers and names only.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HS2Next.Content;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class TestHygiene
{
    public sealed class Result
    {
        public readonly List<string> Leaks = new List<string>();
        public readonly List<string> Resets = new List<string>();
        public double Seconds;
        /// <summary>The cost of the collection and the unused-asset sweep at the end of the hygiene (they grow with what
        /// stays alive; members call the same).</summary>
        public double GcMs, UnloadMs;
    }

    // Classes whose statics are reset between members without a finding (state the tests and the product change on purpose).
    static readonly string[] KnownTypes =
    {
        "HS2Next.Chara.TextureGuard", "HS2Next.ShaderMap", "HS2Next.Chara.Ext.CharaBuildHooks", "HS2Next.Scenes.MakerServices",
        "HS2Next.UI.Panels.Maker.PartThumbnails", "HS2Next.Content.ContentCatalog", "HS2Next.Content.LooseTextures",
        "HS2Next.Scenes.Runtime.MapRealization", "HS2Next.Content.TextureHeaders", "HS2Next.Content.TextureCheck",
        "HS2Next.Content.TextureRepairCache", "HS2Next.Scenes.Runtime.MapOptions",
    };

    // Product states put back between members without a finding (the product's own counters, last timings and reasons, and
    // the UI singletons a panel build sets). A fixed list: any other static that changes stays a finding.
    static readonly string[] KnownStates =
    {
        "HS2Next.Chara.Mosaic.MosaicUnit.Runs",
        "HS2Next.Chara.Mosaic.MosaicUvTransfer.Computed",
        "HS2Next.Chara.Documents.CharaDocumentInput.LastMilliseconds",
        "HS2Next.Chara.Mosaic.MosaicCamera.LastMilliseconds", "HS2Next.Chara.Mosaic.MosaicCamera.TotalMilliseconds",
        "HS2Next.Chara.Mosaic.MosaicCamera.Calls", "HS2Next.Chara.Mosaic.MosaicCamera.Last",
        "HS2Next.MainLight.Current", "HS2Next.MainLight.Reason", "HS2Next.MainLight.CharaLight",
        "HS2Next.Chara.Ext.ExtTextures.Decoded", "HS2Next.Chara.Ext.ExtTextures.CacheHits", "HS2Next.Chara.Ext.ExtTextures.Failed",
        "HS2Next.Chara.Ext.ExtTextures.DecodedBytes", "HS2Next.Chara.Ext.ExtTextures.LiveBytes", "HS2Next.Chara.Ext.ExtTextures.PeakBytes",
        "HS2Next.Scenes.Runtime.CharaEditFlush.Throttled", "HS2Next.Scenes.Runtime.CharaEditFlush.Flushed",
        "HS2Next.Scenes.Runtime.CharaEditFlush.PreviewFlushes",
        "HS2Next.Scenes.Runtime.CardCharaBuilder.ImportCount",
        "HS2Next.UI.Panels.AddModule.ItemBrowser", "HS2Next.UI.Panels.AddModule.AnimeBrowser", "HS2Next.UI.Panels.AddModule.MapBrowser",
        "HS2Next.UI.Panels.AddModule.LightBrowser", "HS2Next.UI.Panels.AddModule.Poses", "HS2Next.UI.Panels.AddModule.Drop",
        "HS2Next.UI.Panels.CharaBrowser.Current", "HS2Next.UI.Panels.SceneFiles.Current", "HS2Next.UI.Panels.WorkspacePanel.Current",
        "HS2Next.Scenes.Runtime.ClothesInfo.FactsBuilt", "HS2Next.Scenes.Runtime.ClothesInfo.FactsFromCache",
        "HS2Next.Scenes.Runtime.AccessoryInfo.FactsBuilt", "HS2Next.Scenes.Runtime.AccessoryInfo.FactsFromCache",
        "HS2Next.Scenes.Runtime.MapSurface.LastCandidates", "HS2Next.Scenes.Runtime.MapSurface.LastDraws", "HS2Next.Scenes.Runtime.MapSurface.LastScanMs",
        "HS2Next.Scenes.Runtime.MapSurface.LastIndexMs", "HS2Next.Scenes.Runtime.MapSurface.Waits",
        "HS2Next.Scenes.Cut.CutPieces.PiecesEncoded", "HS2Next.Scenes.Cut.CutPieces.CopiesCompared", "HS2Next.Scenes.Cut.CutPieces.CopiesDiffered",
        "HS2Next.Scenes.Cut.CutPieces.FirstDifferencePiece", "HS2Next.Scenes.Cut.CutPieces.FirstDifferenceCopy", "HS2Next.Scenes.Cut.CutPieces.FirstDifferenceParse",
    };

    /// <summary>The product states put back so far in this batch (the batch summary counts them).</summary>
    public static int StatesReset { get; private set; }

    // Static collections that hold registrations: (type, field). An entry the member added is a leak.
    static readonly (string type, string field)[] Registries =
    {
        ("HS2Next.Chara.Ext.CharaBuildHooks", "hooks"), ("HS2Next.Chara.Ext.CharaBuildHooks", "nodeProviders"),
        ("HS2Next.ShaderMap", "Fitted"), ("HS2Next.ShaderMap", "supplies"), ("HS2Next.ShaderMap", "CopyAccepts"),
        ("HS2Next.ShaderMap", "CopyRoutes"),
    };

    // Switch: a field a test can set (public / internal field, or a property with a public setter) of a value type or string;
    // State: the product's own diagnostics (a property whose setter is not public: counters, last timings, last reasons);
    // Options: a public options object (its public fields); Reference: any other reference.
    enum Kind { Switch, State, Options, Reference }

    sealed class Slot
    {
        public FieldInfo Field;
        public string Name;
        public Kind Kind;
        public bool Known;
        public bool KnownState;
        public object Value;
        public object Seen;   // References: the value after the previous member
        public List<(FieldInfo f, object v)> Inner;   // Options: the public fields of the object
    }

    sealed class Reg
    {
        public string Name;
        public object Collection;
        public List<object> Items;                        // list / set: the entries
        public List<KeyValuePair<object, object>> Pairs;  // dictionary: the entries
    }

    static readonly List<Slot> slots = new List<Slot>();
    static readonly List<Reg> regs = new List<Reg>();
    static readonly List<string> initFailures = new List<string>();
    static Dictionary<string, string> env;
    static string scenesBaseline;
    static HashSet<GameObject> objectsBefore = new HashSet<GameObject>();
    static Delegate[] updateBefore = Array.Empty<Delegate>(), delayBefore = Array.Empty<Delegate>();
    static int previewScenesBefore;

    public static string BaselineText { get; private set; } = "";

    static bool IsSwitchType(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal) ||
               (u.IsValueType && !u.IsGenericType && u.Namespace != null && (u.Namespace.StartsWith("HS2Next") || u.Namespace == "UnityEngine"));
    }

    static bool IsOptionsType(Type t) =>
        t.IsClass && !t.IsAbstract && t != typeof(string) && !typeof(Delegate).IsAssignableFrom(t) && !typeof(Object).IsAssignableFrom(t) &&
        !typeof(IEnumerable).IsAssignableFrom(t) && t.Namespace != null && t.Namespace.StartsWith("HS2Next") &&
        t.GetFields(BindingFlags.Public | BindingFlags.Instance).Length > 0 &&
        t.GetFields(BindingFlags.Public | BindingFlags.Instance).All(f => IsSwitchType(f.FieldType));

    static IEnumerable<Type> ProjectTypes()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string n = a.GetName().Name;
            if (n != "Assembly-CSharp" && n != "HS2Next.Api" && n != "HS2Next.Api.Unity" && n != "HS2Next.Bridge") continue;
            Type[] types;
            try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in types)
                if (t.Namespace != null && t.Namespace.StartsWith("HS2Next") && !t.IsGenericTypeDefinition && !t.Name.Contains("<"))
                    yield return t;
        }
    }

    static bool Known(Type t) => KnownTypes.Any(k => t.FullName == k || t.FullName.StartsWith(k + "+"));

    /// <summary>The baseline at the batch start: the statics, registries, environment and scene setup.</summary>
    public static void Begin()
    {
        slots.Clear(); regs.Clear(); initFailures.Clear();
        foreach (var t in ProjectTypes())
        {
            const BindingFlags bf = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var f in t.GetFields(bf))
            {
                if (f.IsLiteral || f.IsInitOnly) continue;
                bool visible = f.IsPublic || f.IsAssembly, settable = visible;
                if (!visible && f.Name.StartsWith("<") && f.Name.EndsWith(">k__BackingField"))
                {
                    var p = t.GetProperty(f.Name.Substring(1, f.Name.IndexOf('>') - 1), bf);
                    visible = p?.GetMethod != null && (p.GetMethod.IsPublic || p.GetMethod.IsAssembly);
                    settable = p?.SetMethod != null && (p.SetMethod.IsPublic || p.SetMethod.IsAssembly);
                }
                if (!visible) continue;
                var s = new Slot { Field = f, Name = t.FullName + "." + (f.Name.StartsWith("<") ? f.Name.Substring(1, f.Name.IndexOf('>') - 1) : f.Name), Known = Known(t) };
                s.KnownState = KnownStates.Contains(s.Name);
                try { s.Value = s.Seen = f.GetValue(null); }
                catch (Exception e) { initFailures.Add(s.Name + " (" + (e.InnerException ?? e).GetType().Name + ")"); continue; }
                s.Kind = !settable ? Kind.State : IsSwitchType(f.FieldType) ? Kind.Switch : IsOptionsType(f.FieldType) ? Kind.Options : Kind.Reference;
                if (s.Kind == Kind.Options && s.Value != null)
                    s.Inner = s.Value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Select(x => (x, x.GetValue(s.Value))).ToList();
                slots.Add(s);
            }
        }
        foreach (var (type, field) in Registries)
        {
            var t = ProjectTypes().FirstOrDefault(x => x.FullName == type);
            var f = t?.GetField(field, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) { initFailures.Add("registry " + type + "." + field + " not found"); continue; }
            var r = new Reg { Name = t.Name + "." + field, Collection = f.GetValue(null) };
            Capture(r);
            regs.Add(r);
        }
        env = EnvNow();
        scenesBaseline = ScenesNow();
        previewScenesBefore = EditorSceneManager.previewSceneCount;
        BaselineText = $"{slots.Count} statics ({slots.Count(s => s.Kind == Kind.Switch)} switches, {slots.Count(s => s.Kind == Kind.State)} product states, {slots.Count(s => s.Kind == Kind.Options)} options, " +
                       $"{slots.Count(s => s.Kind == Kind.Reference)} references; {slots.Count(s => s.Known)} of known classes), {regs.Count} registries, " +
                       $"{env.Count} environment variables, scenes {scenesBaseline}" + (initFailures.Count > 0 ? "; not read: " + string.Join(", ", initFailures) : "");
    }

    static void Capture(Reg r)
    {
        r.Items = null; r.Pairs = null;
        if (r.Collection is IDictionary d)
        {
            r.Pairs = new List<KeyValuePair<object, object>>();
            foreach (DictionaryEntry e in d) r.Pairs.Add(new KeyValuePair<object, object>(e.Key, e.Value));
        }
        else if (r.Collection is IEnumerable e) r.Items = e.Cast<object>().ToList();
    }

    static Dictionary<string, string> EnvNow()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            string k = e.Key as string;
            if (k != null && (k.StartsWith("HS2NEXT_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("HS2N_", StringComparison.OrdinalIgnoreCase)))
                d[k] = e.Value as string;
        }
        return d;
    }

    static string ScenesNow()
    {
        var l = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            l.Add((string.IsNullOrEmpty(s.path) ? "(untitled)" : s.path) + (s.isLoaded ? "" : " (not loaded)"));
        }
        return l.Count + ": " + string.Join(", ", l);
    }

    static List<GameObject> SceneObjects() =>
        Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g && g.scene.IsValid() && !EditorUtility.IsPersistent(g)).ToList();

    static Delegate[] List(Delegate d) => d?.GetInvocationList() ?? Array.Empty<Delegate>();

    /// <summary>Notes what the member starts with (scene objects, editor callbacks).</summary>
    public static void BeforeMember()
    {
        objectsBefore = new HashSet<GameObject>(SceneObjects());
        updateBefore = List(EditorApplication.update);
        delayBefore = List(EditorApplication.delayCall);
    }

    static string Show(object v) => v == null ? "null" : v is string s ? "\"" + s + "\"" : Convert.ToString(v, CultureInfo.InvariantCulture);

    static bool Same(object a, object b) => a == null ? b == null : (a.GetType().IsValueType || a is string) ? a.Equals(b) : ReferenceEquals(a, b);

    /// <summary>Puts the known state back and checks for leaks (see the file header).</summary>
    public static Result AfterMember()
    {
        var r = new Result();
        var sw = Stopwatch.StartNew();

        // 1. scene objects the member left (before any reset destroys them)
        var left = SceneObjects().Where(g => !objectsBefore.Contains(g)).ToList();
        if (left.Count > 0)
        {
            var roots = left.Where(g => g.transform.parent == null || !left.Contains(g.transform.parent.gameObject)).ToList();
            r.Leaks.Add($"{left.Count} GameObjects left in the scene ({string.Join(", ", roots.Take(5).Select(g => g.name))}{(roots.Count > 5 ? ", ..." : "")})");
            foreach (var g in roots) if (g) Object.DestroyImmediate(g);
        }
        if (EditorSceneManager.previewSceneCount > previewScenesBefore)
            r.Leaks.Add($"{EditorSceneManager.previewSceneCount - previewScenesBefore} preview scenes left open");

        // 2. editor callbacks the member left registered (an asynchronous tail or a leak)
        var upd = List(EditorApplication.update).Where(d => !updateBefore.Contains(d)).ToList();
        DropEngineCallbacks(upd, r, "update", d => EditorApplication.update -= (EditorApplication.CallbackFunction)d);
        if (upd.Count > 0)
        {
            r.Leaks.Add($"{upd.Count} EditorApplication.update callbacks left ({string.Join(", ", upd.Select(d => d.Method.DeclaringType?.Name + "." + d.Method.Name))})");
            foreach (var d in upd) EditorApplication.update -= (EditorApplication.CallbackFunction)d;
        }
        var dly = List(EditorApplication.delayCall).Where(d => !delayBefore.Contains(d)).ToList();
        DropEngineCallbacks(dly, r, "delayCall", d => EditorApplication.delayCall -= (EditorApplication.CallbackFunction)d);
        if (dly.Count > 0)
        {
            r.Leaks.Add($"{dly.Count} EditorApplication.delayCall callbacks left ({string.Join(", ", dly.Select(d => d.Method.DeclaringType?.Name + "." + d.Method.Name))})");
            foreach (var d in dly) EditorApplication.delayCall -= (EditorApplication.CallbackFunction)d;
        }

        // 3. registries
        foreach (var g in regs) RestoreRegistry(g, r);

        // 4. statics
        int states = 0;
        foreach (var s in slots) states += RestoreSlot(s, r);
        if (states > 0) { r.Resets.Add(states + " listed product states"); StatesReset += states; }

        // 5. environment
        var now = EnvNow();
        foreach (var k in now.Keys.Union(env.Keys).ToList())
        {
            env.TryGetValue(k, out var v0);
            now.TryGetValue(k, out var v1);
            if (v0 == v1) continue;
            Environment.SetEnvironmentVariable(k, v0);
            r.Resets.Add("env " + k);
        }

        // 6. the warm catalog, the part thumbnails, every open bundle and the caches of loaded objects
        TestCatalog.ResetShared(r.Resets);
        ResetPartThumbnails(r.Resets);
        int open = AssetBundle.GetAllLoadedAssetBundles().Count();
        if (open > 0) { AssetBundle.UnloadAllAssetBundles(true); r.Resets.Add($"{open} open bundles"); }
        HS2Next.Chara.TextureGuard.ForgetLoaded();
        HS2Next.MaterialCopies.Forget();
        HS2Next.Chara.Mosaic.MosaicUvTransfer.ReleaseAll();

        // 7. the scene setup
        string scenes = ScenesNow();
        if (scenes != scenesBaseline)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            r.Resets.Add("scenes (" + scenes + ")");
            scenesBaseline = ScenesNow();
        }

        // 8. memory
        var t = Stopwatch.StartNew();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        r.GcMs = t.Elapsed.TotalMilliseconds;
        t.Restart();
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        r.UnloadMs = t.Elapsed.TotalMilliseconds;
        r.Seconds = sw.Elapsed.TotalSeconds;
        return r;
    }

    // ---------------------------------------------------------------- diagnostics (-diag): what grows per member

    static Dictionary<string, int> diagTypes;
    static int diagUndo;

    static Dictionary<string, int> ObjectsByType()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var o in Resources.FindObjectsOfTypeAll<Object>())
        {
            if (!o) continue;
            string k = o.GetType().Name;
            d[k] = d.TryGetValue(k, out int n) ? n + 1 : 1;
        }
        return d;
    }

    static int LogHandlers()
    {
        int n = 0;
        foreach (var f in typeof(Application).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            if (typeof(Delegate).IsAssignableFrom(f.FieldType) && f.Name.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0)
                n += (f.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
        return n;
    }

    /// <summary>Notes the live objects by type before a member (diagnostics only).</summary>
    public static void DiagMark()
    {
        diagTypes = ObjectsByType();
        diagUndo = Undo.GetCurrentGroup();
    }

    /// <summary>The live objects after the member's hygiene against <see cref="DiagMark"/>: total, the five types that grew
    /// most; the log handlers registered; the undo group counter's advance.</summary>
    public static string DiagText()
    {
        var now = ObjectsByType();
        int total = now.Values.Sum(), before = diagTypes?.Values.Sum() ?? 0;
        var grew = now.Select(kv => (kv.Key, d: kv.Value - (diagTypes != null && diagTypes.TryGetValue(kv.Key, out int b) ? b : 0)))
            .Where(x => x.d != 0).OrderByDescending(x => Math.Abs(x.d)).Take(5).Select(x => $"{x.Key} {x.d:+0;-0}");
        return $"objects {total} ({total - before:+0;-0;0}: {string.Join(", ", grew)}); log handlers {LogHandlers()}; undo +{Undo.GetCurrentGroup() - diagUndo}";
    }

    // Returns 1 when a listed product state was put back (counted, not named).
    static int RestoreSlot(Slot s, Result r)
    {
        object v;
        try { v = s.Field.GetValue(null); } catch { return 0; }
        if (s.KnownState)
        {
            if (Same(s.Value, v)) return 0;
            s.Field.SetValue(null, s.Value);
            r.Resets.Add(Short(s.Name) + " (listed state)");
            return 1;
        }
        if (s.Kind == Kind.State)
        {
            if (Same(s.Value, v)) return 0;
            s.Field.SetValue(null, s.Value);
            if (s.Known) r.Resets.Add(Short(s.Name));
            else r.Leaks.Add($"state left {Short(s.Name)} = {Show(v)} (default {Show(s.Value)})");
            return 0;
        }
        if (s.Kind == Kind.Reference)
        {
            if (Same(s.Value, v)) { s.Seen = v; return 0; }
            if (s.Known) { s.Field.SetValue(null, s.Value); r.Resets.Add(Short(s.Name)); s.Seen = s.Value; return 0; }
            if (!Same(s.Seen, v)) r.Resets.Add(Short(s.Name) + " (kept)");
            s.Seen = v;
            return 0;
        }
        var diffs = new List<string>();
        if (!Same(s.Value, v)) { diffs.Add($"{Short(s.Name)} = {Show(v)} (default {Show(s.Value)})"); s.Field.SetValue(null, s.Value); }
        if (s.Kind == Kind.Options && s.Inner != null && s.Value != null)
            foreach (var (f, v0) in s.Inner)
            {
                var v1 = f.GetValue(s.Value);
                if (Same(v0, v1)) continue;
                diffs.Add($"{Short(s.Name)}.{f.Name} = {Show(v1)} (default {Show(v0)})");
                f.SetValue(s.Value, v0);
            }
        if (diffs.Count == 0) return 0;
        if (s.Known) r.Resets.Add(Short(s.Name));
        else r.Leaks.AddRange(diffs.Select(d => "switch left " + d));
        return 0;
    }

    // Callbacks the engine's own packages queued while the member ran (e.g. a render pipeline asset validating itself):
    // they never run in a row alone either (the process quits first); dropped and listed, not a finding.
    static void DropEngineCallbacks(List<Delegate> l, Result r, string what, Action<Delegate> remove)
    {
        foreach (var d in l.ToList())
        {
            var asm = d.Method.DeclaringType?.Assembly.GetName().Name ?? "";
            if (!asm.StartsWith("Unity", StringComparison.Ordinal)) continue;
            remove(d);
            l.Remove(d);
            r.Resets.Add($"engine {what} {d.Method.DeclaringType?.Name}");
        }
    }

    static void RestoreRegistry(Reg g, Result r)
    {
        if (g.Collection is IDictionary d && g.Pairs != null)
        {
            var added = new List<object>(); var changed = new List<object>(); var removed = new List<object>();
            var basis = g.Pairs.ToDictionary(p => p.Key, p => p.Value);
            foreach (DictionaryEntry e in d)
            {
                if (!basis.TryGetValue(e.Key, out var v0)) added.Add(e.Key);
                else if (!Same(v0, e.Value)) changed.Add(e.Key);
            }
            foreach (var k in basis.Keys) if (!d.Contains(k)) removed.Add(k);
            if (added.Count + changed.Count + removed.Count == 0) return;
            r.Leaks.Add($"{g.Name}: {added.Count} added, {changed.Count} changed, {removed.Count} removed ({string.Join(", ", added.Concat(changed).Concat(removed).Take(4).Select(k => Show(k)))})");
            d.Clear();
            foreach (var p in g.Pairs) d[p.Key] = p.Value;
            return;
        }
        if (g.Collection is IList l && g.Items != null)
        {
            var extra = l.Cast<object>().Where(x => !g.Items.Contains(x)).ToList();
            int missing = g.Items.Count(x => !l.Contains(x));
            if (extra.Count + missing == 0) return;
            r.Leaks.Add($"{g.Name}: {extra.Count} left registered, {missing} removed ({string.Join(", ", extra.Take(4).Select(x => Show(x)))})");
            l.Clear();
            foreach (var x in g.Items) l.Add(x);
        }
    }

    static void ResetPartThumbnails(List<string> resets)
    {
        var t = typeof(HS2Next.UI.Panels.Maker.PartThumbnails);
        var shared = t.GetField("shared", BindingFlags.Static | BindingFlags.NonPublic);
        var sharedFor = t.GetField("sharedFor", BindingFlags.Static | BindingFlags.NonPublic);
        var inst = shared?.GetValue(null);
        if (inst == null) return;
        try { t.GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(inst, null); }
        catch (Exception e) { Debug.LogWarning("[TestHygiene] PartThumbnails.Clear: " + (e.InnerException ?? e).Message); }
        shared.SetValue(null, null);
        sharedFor?.SetValue(null, null);
        resets.Add("PartThumbnails.Shared");
    }

    static string Short(string full) => full.StartsWith("HS2Next.") ? full.Substring(full.LastIndexOf('.', full.LastIndexOf('.') - 1) + 1) : full;
}
