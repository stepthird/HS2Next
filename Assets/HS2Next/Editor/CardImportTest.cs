// Batch test: assemble every card of a directory headless (no camera rendering unless -render) and
// report per card the build time, parts loaded, missing list rows, exceptions and the shaders that
// have no fitted URP replacement (ShaderMap.Fitted), plus a summary.
// Run: -executeMethod CardImportTest.Run [-cards <dir>] [-limit N] [-batch 50] [-render] [-skip <file>] [-step N]
//        [-list <file>] [-noguard] [-norepair] [-guardverbose]
// Default directory: <game>/UserData/chara/female (male cards: -cards <game>/UserData/chara/male; the
// assembler takes the sex from each card, and the summary counts male / female builds). Bundles are
// unloaded every -batch cards. Cards are searched recursively and sorted by relative path.
// Run it with -nographics (without -render): some mod bundle textures fail Unity's 'mipLevel'
// assertions on load and, with a graphics device, crash it natively on upload (D3D12 device removed,
// D3D11 access violation); those cards are counted under "texture mip assertions". In a graphics run,
// each card's relative path is first written to Spike/out/chara/import_current.txt; after a crash,
// append that line to a skip file (one relative path per line) and rerun with -skip <file>: those
// cards are reported as "native crash (skipped)".
// Card plugin data (Runtime/Chara/Ext): per card an "ext:" line when a module did something (extra slots, ABMX,
// build hooks, AAC, DBE, InvisibleBody, pushUp; texture stage: skin layers composited by type and source, KCOX entries,
// MaterialEditor items applied / skipped with reasons, unknown shaders) and its MISSING-content records; totals in the
// "extensions" section, counted over PNG cards (which leaves Maker
// autosaves out) and over all loaded cards, plus the decoded-texture cache (ExtTextures: decodes, hits, peak bytes).
// Without a graphics device the overlay images are decoded and counted but not drawn.
// Texture guard (Runtime/Chara/TextureGuard.cs): textures with an unreadable mip chain are replaced by a neutral
// fallback and listed per card as "MISSING content texture.badmip"; summary line "cards with the texture guard engaged".
// -source document (A4): every card is imported, saved, reloaded and built from the
// documents (Runtime/Chara/Documents/DocumentRoundTrip); same verdict rules. Per card a "doc:" line gives the round-trip
// times and the part-key rows that differ from the card path with their P1 classes (CharaDocBuildTest.RowSummary);
// missing parts of a document build are Context.Missing records (part.missingMod, part.missing, part.unresolved).
// -source card (default) is the Phase 1 card path.
// Motion (Runtime/Motion): per card one "motion:" line with the attached stages in step order (a disabled stage is marked
// "(off)"), the steps run with -step N (and the time per step), the stage exceptions CharaMotion caught and the non-finite
// values after the steps (body bones; other transforms under the character; blend-shape weights); details of a failure
// on "MOTION ERROR" lines. Summary: stages attached and by name, setup failures, steps run, stage exceptions, cards with
// step errors, with non-finite bones and with other non-finite values, and the non-finite values in total.
// -list <file>: only the cards listed (relative paths, one per line); -noguard: guard off; -norepair: do not open
// bundles from their repaired copies (Cache/texguard); -guardverbose: list every texture of an asserted load under
// "texture guard notes".
// -render: the three views of every card (CharaRenderTest's full / full34 / face, Built-in) are rendered and read back in
// memory only - no image is written; per card a "render:" line with the pixels of each view that differ from the
// background, and in the summary the views rendered and the empty ones.
// Output: Spike/out/chara/import_report.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Data;
using HS2Next.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class CardImportTest
{
    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static void Run()
    {
        string dir = Arg("-cards", Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\female"));
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString()));
        int batch = Math.Max(1, int.Parse(Arg("-batch", "50")));
        // -step N: after the build, advance the character's motion stages (Runtime/Motion) N frames at 1/60 s and
        // count stage exceptions and non-finite bone transforms (a dynamics regression over every card).
        int steps = Math.Max(0, int.Parse(Arg("-step", "0")));
        bool render = Environment.GetCommandLineArgs().Contains("-render");
        // Phase 4.4 (the per-asset redirect's record of every card's composites): -rtRecord <dir> / -rtCompare <dir>
        // [-rtProof N: the first card's RTs read N more times, the shared GPU memory before / after]
        var rtHashes = CreatorRtHashes.Begin(Arg("-rtRecord", null), Arg("-rtCompare", null), dir, int.Parse(Arg("-rtProof", "0")));
        string outDir = CardResolveTest.OutDir;
        Directory.CreateDirectory(outDir);
        if (render) ShaderUtil.allowAsyncCompilation = false;
        int renderViews = 0, renderEmpty = 0;

        var total = Stopwatch.StartNew();
        var head = new StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        head.AppendLine($"build hooks registered: {(HS2Next.Chara.Ext.CharaBuildHooks.Registered.Count == 0 ? "none (the official build)" : string.Join(", ", HS2Next.Chara.Ext.CharaBuildHooks.Registered))}");
        bool docSource = Arg("-source", "card") == "document";
        var importer = docSource ? CharaDocBuildTest.NewImporter(gd) : null;
        head.AppendLine("source: " + (docSource ? "document (card -> importer -> .hs2nchara bytes -> load -> document build)" : "card"));
        int docRowCards = 0, docUnclassified = 0;
        var docMs = new List<double>();
        var files = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories)
            .Select(f => f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal).Take(limit).ToArray();
        if (Arg("-list", null) is string lf && File.Exists(lf))
        {
            var only = new HashSet<string>(File.ReadAllLines(lf).Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.Ordinal);
            files = files.Where(only.Contains).ToArray();
        }
        if (Environment.GetCommandLineArgs().Contains("-noguard")) TextureGuard.Enabled = false;
        if (Environment.GetCommandLineArgs().Contains("-guardverbose")) TextureGuard.Verbose = true;
        if (Environment.GetCommandLineArgs().Contains("-norepair")) TextureGuard.UseRepairedBundles = false;
        int withGuard = 0;
        var skip = Arg("-skip", null) is string sf && File.Exists(sf)
            ? new HashSet<string>(File.ReadAllLines(sf).Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.Ordinal)
            : new HashSet<string>();
        string currentFile = Path.Combine(outDir, "import_current.txt");
        var body = new StringBuilder();
        var failures = new SortedDictionary<string, int>();
        var shaderMats = new SortedDictionary<string, int>(StringComparer.Ordinal);    // materials over all cards
        var shaderCards = new Dictionary<string, int>(StringComparer.Ordinal);         // cards using the shader
        int ok = 0, males = 0, withMissing = 0, missingRows = 0, missingMods = 0, withTexAssert = 0, withLogErrors = 0, withEditMat = 0;
        int withMotionFail = 0, withStepErrors = 0, withNaN = 0, withNaNValues = 0, stagesTotal = 0;
        long stepsRun = 0, stageExceptions = 0, nonFiniteBones = 0, nonFiniteOther = 0, nonFiniteWeights = 0;
        double stepMsTotal = 0;
        var stageCensus = new SortedDictionary<string, int>(StringComparer.Ordinal);   // stage name ("name (off)" when disabled) → cards
        // Edit-mode only (batch tests run outside play mode): renderer.material(s) logs an error per call.
        const string EditModeMaterial = "Instantiating material due to calling renderer.material during edit mode";
        // Errors/assertions Unity logs while a card builds (no exception reaches us for these).
        var cardLog = new List<(LogType type, string msg, string where)>();
        Application.LogCallback onLog = (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                cardLog.Add((type, msg.Split('\n')[0], AssemblerFrame(stack)));
        };
        Application.logMessageReceived += onLog;
        long peakMono = 0, peakAlloc = 0, peakGc = 0, peakRetained = 0, buildMs = 0;
        var extPng = new ExtTotals(); var extAll = new ExtTotals();
        ExtTextures.ResetStats();
        long peakTexLive = 0;

        for (int i = 0; i < files.Length; i++)
        {
            string rel = files[i], file = Path.Combine(dir, rel);
            if (skip.Contains(rel))
            {
                const string sk = "native crash (skipped)";
                failures[sk] = failures.TryGetValue(sk, out var sn) ? sn + 1 : 1;
                body.AppendLine($"== {rel}  -  FAILED {sk}");
                continue;
            }
            File.WriteAllText(currentFile, rel + "\n");
            var sw = Stopwatch.StartNew();
            int logStart = gd.Log.Count;
            var roots = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            cardLog.Clear();
            var asm = new CharaAssembler(gd);
            string fail = null, exLine = null;
            Card card = null;
            GameObject chara = null;
            try { card = Card.Load(file); }
            catch (Exception e) { fail = $"card load: {CardKind(file)} ({e.GetType().Name}: {e.Message.Split('.')[0]})"; exLine = Describe(e); }
            bool male = card != null && card.Parameter.Get("sex").Int(1) == 0;
            string docLine = null;
            if (card != null)
            {
                try
                {
                    if (docSource)
                    {
                        var rt = DocumentRoundTrip.Run(importer, File.ReadAllBytes(file), file, gd);
                        if (!rt.Ok) throw new InvalidOperationException("document round trip: " + rt.Error);
                        docMs.Add(rt.AdapterMs);
                        string rows = CharaDocBuildTest.RowSummary(gd, card, rt, out int unc);
                        if (rows != null) docRowCards++;
                        docUnclassified += unc;
                        docLine = $"doc: import {rt.ImportMs:F0} ms, save {rt.SaveMs:F0} ms, load {rt.LoadMs:F0} ms, adapter {rt.AdapterMs:F1} ms; {rt.Bytes.Length} bytes" + (rows != null ? "; " + rows : "");
                        sw.Restart();
                        chara = asm.Build(rt.Input);
                        CharaDocumentInput.AfterBuild(asm.Context);
                    }
                    else chara = asm.BuildFromDocuments(card);
                }
                catch (Exception e) { fail = Classify(e); exLine = Describe(e); }
                if (male) males++;
            }
            long ms = sw.ElapsedMilliseconds;
            buildMs += ms;

            var shaders = chara ? Unfitted(chara) : new SortedDictionary<string, int>();
            foreach (var kv in shaders)
            {
                shaderMats[kv.Key] = shaderMats.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
                shaderCards[kv.Key] = shaderCards.TryGetValue(kv.Key, out var c) ? c + 1 : 1;
            }
            string renderLine = null;
            if (render && chara && fail == null)
            {
                // in memory only: the pixels of each view that differ from the background corner
                var views = new List<string>();
                CharaRenderTest.RenderViews(chara, null, null, null, (vn, tex) =>
                {
                    var px = tex.GetPixels32(); var bg = px[0]; int fg = 0;
                    foreach (var c in px) if (c.r != bg.r || c.g != bg.g || c.b != bg.b) fg++;
                    renderViews++; if (fg == 0) renderEmpty++;
                    views.Add(vn + " " + fg);
                });
                renderLine = "render (in memory, nothing written): " + string.Join(", ", views) + " px";
            }
            if (chara && fail == null) rtHashes?.Card(rel, asm.Context);

            var missing = asm.Log.Where(l => l.StartsWith("MISSING") || l.Contains("prefab not found")).ToList();
            // both sources build from documents: a missing part is a Context.Missing record (part.missingMod, part.missing,
            // part.unresolved)
            if (asm.Context != null) missing.AddRange(asm.Context.Missing.Where(m => m.Kind.StartsWith("part.", StringComparison.Ordinal)).Select(m => "MISSING " + m));
            var catLog = gd.Log.Skip(logStart).Distinct().ToList();
            int mods = catLog.Count(l => l.StartsWith("missing mod"));
            missingRows += missing.Count; missingMods += mods;
            if (missing.Count + mods > 0) withMissing++;
            if (fail == null) ok++;
            else failures[fail] = failures.TryGetValue(fail, out var f) ? f + 1 : 1;

            body.AppendLine($"== {rel}  {ms} ms  parts {asm.PartsLoaded}{(male ? "  male" : "")}  {(fail == null ? "OK" : "FAILED " + fail)}");
            if (exLine != null) body.AppendLine("   exception: " + exLine);
            if (docLine != null) body.AppendLine("   " + docLine);
            if (renderLine != null) body.AppendLine("   " + renderLine);
            bool texAssert = cardLog.Any(l => l.type == LogType.Assert && l.msg.Contains("mipLevel"));
            if (texAssert) withTexAssert++;
            if (cardLog.Any(l => l.msg.StartsWith(EditModeMaterial))) withEditMat++;
            cardLog.RemoveAll(l => l.msg.StartsWith(EditModeMaterial));
            if (cardLog.Any(l => !(l.type == LogType.Assert && l.msg.Contains("mipLevel")))) withLogErrors++;
            foreach (var g in cardLog.GroupBy(l => (l.type, l.msg, l.where)))
                body.AppendLine($"   log {g.Key.type}: {g.Key.msg} @ {g.Key.where} x{g.Count()}");
            foreach (var l in missing) body.AppendLine("   " + l);
            foreach (var l in catLog) body.AppendLine("   " + l);
            if (shaders.Count > 0) body.AppendLine("   unfitted shaders: " + string.Join(", ", shaders.Select(kv => $"{kv.Key} x{kv.Value}")));
            if (card != null && asm.Context != null)
            {
                var ctx = asm.Context;
                bool png = IsPng(file);
                extAll.Add(ctx);
                if (png) extPng.Add(ctx);
                if (ExtTotals.Active(ctx)) body.AppendLine("   ext: " + ctx.Stats.Summary() + (png ? "" : "  (no PNG: autosave)"));
                foreach (var l in asm.Log.Where(IsExtLine)) body.AppendLine("   " + l);
                foreach (var m in ctx.Missing) if (!m.Kind.StartsWith("part.", StringComparison.Ordinal)) body.AppendLine("   MISSING content " + m);
                if (ctx.Missing.Any(m => m.Kind == "texture.badmip")) withGuard++;
                foreach (var er in ctx.Stats.Errors) body.AppendLine("   EXT ERROR " + er);
                // Motion stages: setup failures are logged by MotionSetup as "motion: <family> setup failed".
                var motionFail = asm.Log.Where(l => l.StartsWith("motion:") && l.Contains("failed")).ToList();
                if (motionFail.Count > 0) withMotionFail++;
                foreach (var l in motionFail) body.AppendLine("   MOTION ERROR " + l);
                if (ctx.Motion != null)
                {
                    // One "motion:" line per card: the attached stages in step order (disabled ones marked), the steps run,
                    // the stage exceptions CharaMotion caught and the non-finite values after the steps.
                    var stages = ctx.Motion.Stages;
                    stagesTotal += stages.Count;
                    foreach (var s in stages)
                    {
                        string stageKey = s.Enabled ? s.Name : s.Name + " (off)";
                        stageCensus[stageKey] = stageCensus.TryGetValue(stageKey, out var sc) ? sc + 1 : 1;
                    }
                    string names = string.Join(" ", stages.Select(s => s.Enabled ? s.Name : s.Name + "(off)"));
                    int ran = 0, errs = 0, bad = 0, badOther = 0, badWeights = 0;
                    double stepMs = 0;
                    if (steps > 0 && fail == null)
                    {
                        int errBefore = ctx.Motion.Errors;
                        var swm = Stopwatch.StartNew();
                        try { for (int k = 0; k < steps; k++) { ctx.Motion.Step(1f / 60f); ran++; } }
                        catch (Exception e) { body.AppendLine("   MOTION ERROR step threw " + Describe(e)); withStepErrors++; }
                        stepMs = swm.Elapsed.TotalMilliseconds;
                        errs = ctx.Motion.Errors - errBefore;
                        foreach (var t in ctx.BodyBones.Values)
                        {
                            if (!t) continue;
                            var pos = t.position; var rot = t.localRotation;
                            if (!float.IsFinite(pos.x) || !float.IsFinite(pos.y) || !float.IsFinite(pos.z) ||
                                !float.IsFinite(rot.x) || !float.IsFinite(rot.y) || !float.IsFinite(rot.z) || !float.IsFinite(rot.w)) bad++;
                        }
                        NonFinite(chara, ctx.BodyBones.Values, out badOther, out badWeights);
                        if (errs > 0) { withStepErrors++; foreach (var l in asm.Log.Where(l => l.StartsWith("motion:") && !motionFail.Contains(l))) body.AppendLine("   MOTION ERROR " + l); }
                        if (bad > 0) { withNaN++; body.AppendLine($"   MOTION ERROR {bad} body bones non-finite after {steps} steps"); }
                        if (badOther + badWeights > 0) { withNaNValues++; body.AppendLine($"   MOTION ERROR {badOther} other transforms, {badWeights} blend-shape weights non-finite after {steps} steps"); }
                        stepsRun += ran; stepMsTotal += stepMs; stageExceptions += errs;
                        nonFiniteBones += bad; nonFiniteOther += badOther; nonFiniteWeights += badWeights;
                    }
                    body.AppendLine($"   motion: {stages.Count} stages [{names}], {ran} steps{(ran > 0 ? $" ({stepMs / ran:F3} ms/step)" : "")}, " +
                                    $"stage exceptions {errs}, non-finite: bones {bad}, other transforms {badOther}, blend-shape weights {badWeights}");
                }
                else body.AppendLine("   motion: no CharaMotion on the character");
            }

            peakTexLive = Math.Max(peakTexLive, ExtTextures.LiveBytes);
            Cleanup(roots);
            peakMono = Math.Max(peakMono, Profiler.GetMonoUsedSizeLong());
            peakAlloc = Math.Max(peakAlloc, Profiler.GetTotalAllocatedMemoryLong());
            peakGc = Math.Max(peakGc, GC.GetTotalMemory(false));
            peakRetained = Math.Max(peakRetained, GC.GetTotalMemory(true));   // collect per card: bundle byte[] from zipmods are large
            if ((i + 1) % batch == 0 || i == files.Length - 1)
            {
                gd.UnloadAll();
                EditorUtility.UnloadUnusedAssetsImmediate(true);
                GC.Collect();
                Debug.Log($"CardImportTest: {i + 1}/{files.Length} cards, {total.Elapsed.TotalSeconds:F0} s");
            }
        }

        const double MB = 1024.0 * 1024.0;
        var sb = new StringBuilder();
        sb.AppendLine($"card import test: {dir.Replace('\\', '/').Split('/').Last()} ({files.Length} cards, unload every {batch}, render {(render ? "on" : "off")}, graphics {SystemInfo.graphicsDeviceType})");
        sb.Append(head);
        sb.AppendLine();
        sb.AppendLine("summary");
        sb.AppendLine($"  {"cards",-34} {files.Length,8}");
        sb.AppendLine($"  {"ok",-34} {ok,8}");
        sb.AppendLine($"  {"male cards (sex 0, male path)",-34} {males,8}");
        sb.AppendLine($"  {"failed",-34} {files.Length - ok,8}");
        foreach (var kv in failures.OrderByDescending(kv => kv.Value)) sb.AppendLine($"    {kv.Key,-60} {kv.Value,6}");
        sb.AppendLine($"  {"cards with missing rows/mods",-34} {withMissing,8}");
        sb.AppendLine($"  {"cards with texture mip assertions",-34} {withTexAssert,8}  ('mipLevel' asserts on bundle texture load; with a graphics device these crashed D3D12/D3D11)");
        sb.AppendLine($"  {"cards with the texture guard engaged",-34} {withGuard,8}  (bad-mip textures: repaired bundle or fallback; guard {(TextureGuard.Enabled ? "on" : "off")})");
        sb.AppendLine($"  {"cards with other logged errors",-34} {withLogErrors,8}");
        sb.AppendLine($"  {"cards with edit-mode material notes",-34} {withEditMat,8}  (renderer.material in edit mode; not listed per card)");
        sb.AppendLine($"  {"motion stages attached (total)",-34} {stagesTotal,8}  (Runtime/Motion; setup failures below; one 'motion:' line per card)");
        sb.AppendLine($"  {"motion stages by name (cards)",-34} {(stageCensus.Count == 0 ? "-" : string.Join(", ", stageCensus.Select(kv => $"{kv.Key} {kv.Value}")))}");
        sb.AppendLine($"  {"cards with motion setup failures",-34} {withMotionFail,8}");
        if (steps > 0)
        {
            sb.AppendLine($"  {"motion steps per card",-34} {steps,8}  (1/60 s each)");
            sb.AppendLine($"  {"motion steps run (total)",-34} {stepsRun,8}  ({(stepsRun > 0 ? stepMsTotal / stepsRun : 0):F3} ms per step, all stages)");
            sb.AppendLine($"  {"motion stage exceptions (total)",-34} {stageExceptions,8}  (caught by CharaMotion, logged once per stage and kind)");
            sb.AppendLine($"  {"cards with motion step errors",-34} {withStepErrors,8}");
            sb.AppendLine($"  {"cards with non-finite bones",-34} {withNaN,8}");
            sb.AppendLine($"  {"cards with other non-finite values",-34} {withNaNValues,8}  (transforms outside the body bones, blend-shape weights)");
            sb.AppendLine($"  {"non-finite values (total)",-34} {nonFiniteBones + nonFiniteOther + nonFiniteWeights,8}  (body bones {nonFiniteBones}, other transforms {nonFiniteOther}, blend-shape weights {nonFiniteWeights})");
        }
        sb.AppendLine($"  {"MISSING rows / missing mods",-34} {missingRows,8} / {missingMods}");
        if (render) sb.AppendLine($"  {"render views (in memory only)",-34} {renderViews,8}  ({renderEmpty} empty; no image written)");
        if (docSource)
        {
            var sorted = docMs.OrderBy(x => x).ToList();
            sb.AppendLine($"  {"document: cards with rows ≠ card path",-34} {docRowCards,8}  (P1 classes on each card's doc: line)");
            sb.AppendLine($"  {"document: unclassified row diffs",-34} {docUnclassified,8}");
            sb.AppendLine($"  {"document: adapter median (ms)",-34} {(sorted.Count > 0 ? sorted[sorted.Count / 2] : 0),8:F2}");
        }
        sb.AppendLine($"  {"build time total (s)",-34} {buildMs / 1000.0,8:F1}  (mean {buildMs / Math.Max(1.0, files.Length):F0} ms/card)");
        sb.AppendLine($"  {"wall time incl. lists/unloads (s)",-34} {total.Elapsed.TotalSeconds,8:F1}");
        sb.AppendLine($"  {"peak managed retained (MB)",-34} {peakRetained / MB,8:F0}  (after a GC per card; before it: GC heap {peakGc / MB:F0}, mono used {peakMono / MB:F0})");
        sb.AppendLine($"  {"peak Unity allocated (MB)",-34} {peakAlloc / MB,8:F0}");
        rtHashes?.Finish(sb);
        sb.AppendLine();
        sb.AppendLine($"  {"extensions (Runtime/Chara/Ext)",-56} {"PNG cards",-24} | all loaded cards");
        extPng.Write(sb, extAll);
        sb.AppendLine($"  decoded plugin images (ExtTextures): {ExtTextures.Decoded} decodes, {ExtTextures.CacheHits} cache hits, {ExtTextures.Failed} failed, " +
                      $"{ExtTextures.DecodedBytes / MB:F0} MB decoded in total; peak alive {ExtTextures.PeakBytes / MB:F0} MB (per card max {peakTexLive / MB:F0} MB), " +
                      $"alive after the run {ExtTextures.Live} textures / {ExtTextures.LiveBytes / MB:F0} MB (estimated GPU size, RGBA8 + mips)");
        sb.AppendLine();
        sb.AppendLine("shaders without a fitted URP replacement (materials over all cards / cards)");
        if (shaderMats.Count == 0) sb.AppendLine("  none");
        foreach (var kv in shaderMats.OrderByDescending(kv => kv.Value)) sb.AppendLine($"  {kv.Key,-50} {kv.Value,7} {shaderCards[kv.Key],6}");
        sb.AppendLine();
        if (TextureGuard.Diagnostics.Count > 0)
        {
            sb.AppendLine("texture guard notes");
            foreach (var l in TextureGuard.Diagnostics) sb.AppendLine("  " + l);
            sb.AppendLine();
        }
        sb.Append(body);
        File.WriteAllText(Path.Combine(outDir, "import_report.txt"), sb.ToString());
        File.Delete(currentFile);
        gd.UnloadAll();
        Application.logMessageReceived -= onLog;
        Debug.Log($"CARD IMPORT TEST DONE: {ok}/{files.Length} ok in {total.Elapsed.TotalSeconds:F0} s");
    }

    static readonly string[] ExtPrefixes = { "hook ", "abmx:", "moreAccessories:", "aac:", "dbe:", "invisibleBody:", "bone effect", "ext ", "ksox:", "kcox:", "materialEditor:" };
    static bool IsExtLine(string l) => ExtPrefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal));

    static bool IsPng(string file)
    {
        try
        {
            using (var f = File.OpenRead(file))
            {
                var b = new byte[8];
                return f.Read(b, 0, 8) == 8 && Card.HasPng(b);
            }
        }
        catch (Exception) { return false; }
    }

    // Totals of the extension stage over a set of cards.
    sealed class ExtTotals
    {
        public int Cards, Errors;
        public int ExtraCards, ExtraUsed, ExtraLoaded;
        public int AbmxCards, AbmxMods, AbmxApplied, AbmxIdentity, AbmxAcc, AbmxUnknownCards, AbmxUnknown;
        public readonly SortedDictionary<string, int> UnknownBones = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Cards whose plugin data carries an uncensor body GUID (kept as data only), cards whose body mesh a build hook
        /// replaced, and the build hooks that ran (by "id@stage": cards, renderers changed, errors).</summary>
        public int UncSet, ReplacedBody;
        public readonly SortedDictionary<string, int> HookCards = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> HookRenderers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> HookErrors = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int AacCards, AacRules, AacEvaluated, AacSlots, AacHair, AacEffects;
        public int DbeCards, DbeEntries, DbeMatched, DbeComponents, DbeFields;
        public int InvisibleCards, InvisibleRenderers, PushUpCards;
        public readonly List<string> ErrorLines = new List<string>();
        // texture stage
        public int KsoxCards, SkinCards, SkinPasses, SkinResizedCards;
        public readonly SortedDictionary<string, int> SkinLayers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> SkinSkipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> SkinResized = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int KcoxCards, KcoxImageCards, KcoxEntries, KcoxApplied, KcoxOverrides, KcoxImages, KcoxCleared, KcoxResized;
        public readonly SortedDictionary<string, int> KcoxSkipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int MeCards, MeItems, MeApplied, MeSwaps, MeSwapsFitted, MeTexCards, MeTextures, MeAssignments;
        public long MeTexBytes;
        public readonly SortedDictionary<string, int> MeSkipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> MeUnknown = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> MeUnknownCards = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> MeNotOnFitted = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> MeNotes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> MeAbsent = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> MeNoMaterial = new SortedDictionary<string, int>(StringComparer.Ordinal);

        static void AddAll(SortedDictionary<string, int> to, SortedDictionary<string, int> from) { foreach (var kv in from) ExtStats.Inc(to, kv.Key, kv.Value); }
        static string J(SortedDictionary<string, int> d) => d.Count == 0 ? "-" : string.Join(", ", d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value}"));

        public static bool Active(CharaContext c)
        {
            var s = c.Stats;
            return s.ExtraSlotsUsed > 0 || s.AbmxModifiers > 0 || c.Ext?.Uncensor?.BodyGUID != null || s.Hooks.Count > 0 || s.AacRules > 0 || s.DbeEntries > 0 ||
                   s.InvisibleBody || s.Errors.Count > 0 || s.TexturesActive;
        }

        public void Add(CharaContext c)
        {
            var s = c.Stats;
            Cards++;
            if (s.Errors.Count > 0) { Errors++; ErrorLines.AddRange(s.Errors); }
            if (s.ExtraSlotsUsed > 0) { ExtraCards++; ExtraUsed += s.ExtraSlotsUsed; ExtraLoaded += s.ExtraSlotsLoaded; }
            if (s.AbmxModifiers > 0)
            {
                AbmxCards++; AbmxMods += s.AbmxModifiers; AbmxApplied += s.AbmxApplied; AbmxIdentity += s.AbmxIdentity; AbmxAcc += s.AbmxOnAccessories;
                if (s.AbmxUnknownBones.Count > 0) AbmxUnknownCards++;
                AbmxUnknown += s.AbmxUnknownBones.Count;
                foreach (var b in s.AbmxUnknownBones) UnknownBones[b] = UnknownBones.TryGetValue(b, out var n) ? n + 1 : 1;
            }
            if (c.Ext?.Uncensor?.BodyGUID != null) UncSet++;
            if (c.ReplacedBodyMesh != null) ReplacedBody++;
            foreach (var kv in s.Hooks)
            {
                ExtStats.Inc(HookCards, kv.Key);
                if (kv.Value.Renderers > 0) ExtStats.Inc(HookRenderers, kv.Key, kv.Value.Renderers);
                if (kv.Value.Errors > 0) ExtStats.Inc(HookErrors, kv.Key, kv.Value.Errors);
            }
            if (s.AacRules > 0) { AacCards++; AacRules += s.AacRules; AacEvaluated += s.AacRulesEvaluated; AacSlots += s.AacSlotsChanged; AacHair += s.AacHairObjectsHidden; AacEffects += s.AacBoneEffects; }
            if (s.DbeEntries > 0) { DbeCards++; DbeEntries += s.DbeEntries; DbeMatched += s.DbeEntriesMatched; DbeComponents += s.DbeComponents; DbeFields += s.DbeFields; }
            if (s.InvisibleBody) { InvisibleCards++; InvisibleRenderers += s.InvisibleRenderers; }
            if (s.PushUpStored) PushUpCards++;
            if (s.SkinLayersComposited.Count > 0 || s.SkinLayersSkipped.Count > 0)
            {
                SkinCards++; SkinPasses += s.SkinPassesRebuilt;
                if (s.SkinLayersComposited.Keys.Any(k => k.EndsWith(" card")) || s.SkinLayersSkipped.Count > 0) KsoxCards++;
                AddAll(SkinLayers, s.SkinLayersComposited); AddAll(SkinSkipped, s.SkinLayersSkipped);
                if (s.SkinResized.Count > 0) SkinResizedCards++;
                foreach (var r in s.SkinResized) ExtStats.Inc(SkinResized, r);
            }
            if (s.KcoxEntries > 0)
            {
                KcoxCards++; KcoxEntries += s.KcoxEntries; KcoxApplied += s.KcoxApplied; KcoxOverrides += s.KcoxOverrides; KcoxImages += s.KcoxOverlays;
                KcoxCleared += s.KcoxCleared; KcoxResized += s.KcoxResized; AddAll(KcoxSkipped, s.KcoxSkipped);
                if (c.Ext?.ClothesOverlays != null && c.Ext.ClothesOverlays.TextureCount > 0) KcoxImageCards++;
            }
            if (s.MeItems > 0)
            {
                MeCards++; MeItems += s.MeItems; MeApplied += s.MeApplied; MeSwaps += s.MeShaderSwaps; MeSwapsFitted += s.MeShaderSwapsToFitted;
                if (s.MeTexturesDecoded > 0) MeTexCards++;
                MeTextures += s.MeTexturesDecoded; MeTexBytes += s.MeTextureBytes; MeAssignments += s.MeTextureAssignments;
                AddAll(MeSkipped, s.MeSkipped); AddAll(MeUnknown, s.MeUnknownShaders); AddAll(MeNotOnFitted, s.MeNotOnFitted); AddAll(MeNotes, s.MeNotes); AddAll(MeAbsent, s.MeAbsentProperties); AddAll(MeNoMaterial, s.MeMissingMaterials);
                foreach (var k in s.MeUnknownShaders.Keys) ExtStats.Inc(MeUnknownCards, k);
            }
        }

        public void Write(StringBuilder sb, ExtTotals all)
        {
            void L(string label, string a, string b) => sb.AppendLine($"  {label,-56} {a,-24} | {b}");
            L("cards", $"{Cards}", $"{all.Cards}");
            L("cards with extension errors", $"{Errors}", $"{all.Errors}");
            L("moreAccessories cards / used slots / loaded", $"{ExtraCards} / {ExtraUsed} / {ExtraLoaded}", $"{all.ExtraCards} / {all.ExtraUsed} / {all.ExtraLoaded}");
            L("ABMX cards / modifiers", $"{AbmxCards} / {AbmxMods}", $"{all.AbmxCards} / {all.AbmxMods}");
            L("ABMX applied / identity / on accessories", $"{AbmxApplied} / {AbmxIdentity} / {AbmxAcc}", $"{all.AbmxApplied} / {all.AbmxIdentity} / {all.AbmxAcc}");
            L("ABMX cards with unknown bones / unknown (card, bone)", $"{AbmxUnknownCards} / {AbmxUnknown}", $"{all.AbmxUnknownCards} / {all.AbmxUnknown}");
            L("uncensor body GUID in the data (kept, not applied)", $"{UncSet}", $"{all.UncSet}");
            L("body mesh replaced by a build hook", $"{ReplacedBody}", $"{all.ReplacedBody}");
            L("AAC cards with rules / rules / evaluated", $"{AacCards} / {AacRules} / {AacEvaluated}", $"{all.AacCards} / {all.AacRules} / {all.AacEvaluated}");
            L("AAC slots changed / hair objects hidden / bone effects", $"{AacSlots} / {AacHair} / {AacEffects}", $"{all.AacSlots} / {all.AacHair} / {all.AacEffects}");
            L("DBE cards / entries / matched", $"{DbeCards} / {DbeEntries} / {DbeMatched}", $"{all.DbeCards} / {all.DbeEntries} / {all.DbeMatched}");
            L("DBE components / fields written", $"{DbeComponents} / {DbeFields}", $"{all.DbeComponents} / {all.DbeFields}");
            L("InvisibleBody hidden cards / renderers", $"{InvisibleCards} / {InvisibleRenderers}", $"{all.InvisibleCards} / {all.InvisibleRenderers}");
            L("pushUp stored (effect off)", $"{PushUpCards}", $"{all.PushUpCards}");
            L("skin layers: cards / with card KSOX / passes rebuilt", $"{SkinCards} / {KsoxCards} / {SkinPasses}", $"{all.SkinCards} / {all.KsoxCards} / {all.SkinPasses}");
            L("skin layers: cards with a grown RT", $"{SkinResizedCards}", $"{all.SkinResizedCards}");
            L("KCOX cards / with images / entries / applied", $"{KcoxCards} / {KcoxImageCards} / {KcoxEntries} / {KcoxApplied}", $"{all.KcoxCards} / {all.KcoxImageCards} / {all.KcoxEntries} / {all.KcoxApplied}");
            L("KCOX overrides / images drawn / cleared / RTs grown", $"{KcoxOverrides} / {KcoxImages} / {KcoxCleared} / {KcoxResized}", $"{all.KcoxOverrides} / {all.KcoxImages} / {all.KcoxCleared} / {all.KcoxResized}");
            L("ME cards with edits / items / applied", $"{MeCards} / {MeItems} / {MeApplied}", $"{all.MeCards} / {all.MeItems} / {all.MeApplied}");
            L("ME shader swaps / to the URP shader directly", $"{MeSwaps} / {MeSwapsFitted}", $"{all.MeSwaps} / {all.MeSwapsFitted}");
            L("ME cards with textures / decoded / MB / assignments", $"{MeTexCards} / {MeTextures} / {MeTexBytes / 1048576.0:F0} / {MeAssignments}", $"{all.MeTexCards} / {all.MeTextures} / {all.MeTexBytes / 1048576.0:F0} / {all.MeAssignments}");
            sb.AppendLine("  skin layers composited (PNG, layers): " + J(SkinLayers));
            if (SkinSkipped.Count > 0) sb.AppendLine("  skin layers skipped (PNG): " + J(SkinSkipped));
            if (SkinResized.Count > 0) sb.AppendLine("  skin RTs grown (PNG): " + J(SkinResized));
            if (KcoxSkipped.Count > 0) sb.AppendLine("  KCOX skipped (PNG): " + J(KcoxSkipped));
            sb.AppendLine("  ME skipped by reason (PNG, items): " + J(MeSkipped));
            sb.AppendLine("  ME shaders without a fitted replacement (PNG, items): " + J(MeUnknown));
            sb.AppendLine("  ME shaders without a fitted replacement (PNG, cards): " + J(MeUnknownCards));
            if (MeNotOnFitted.Count > 0) sb.AppendLine("  ME properties absent from the fitted URP shader (PNG, materials): " + J(MeNotOnFitted));
            if (MeNotes.Count > 0) sb.AppendLine("  ME notes (PNG): " + J(MeNotes));
            if (MeNoMaterial.Count > 0) sb.AppendLine("  ME 'material not found' (PNG, items; top 25): " +
                string.Join(", ", MeNoMaterial.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(25).Select(kv => $"{kv.Key} {kv.Value}")));
            if (MeAbsent.Count > 0) sb.AppendLine("  ME 'property not on the shader' (PNG, items; top 25): " +
                string.Join(", ", MeAbsent.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(25).Select(kv => $"{kv.Key} {kv.Value}")));
            if (HookCards.Count > 0) sb.AppendLine("  build hooks that ran (PNG, cards): " + J(HookCards));
            if (HookRenderers.Count > 0) sb.AppendLine("  build hooks: renderers changed (PNG): " + J(HookRenderers));
            if (HookErrors.Count > 0) sb.AppendLine("  build hooks: errors (PNG): " + J(HookErrors));
            if (UnknownBones.Count > 0) sb.AppendLine("  ABMX unknown bones (PNG, cards): " + string.Join(", ", UnknownBones.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value}")));
            foreach (var g in all.ErrorLines.GroupBy(e => e)) sb.AppendLine($"  ext error x{g.Count()}: {g.Key}");
        }
    }

    // Failure class: exception type and the first frame of the stack (method only).
    internal static string Classify(Exception e)
    {
        var frame = FirstFrame(e);
        int paren = frame.IndexOf('(');
        if (paren > 0) frame = frame.Substring(0, paren).Trim();
        return $"{e.GetType().Name} @ {frame}";
    }

    // The innermost CharaAssembler method on a Unity log stack (where in the assembly the message came from).
    internal static string AssemblerFrame(string stack)
    {
        foreach (var l in (stack ?? "").Split('\n'))
        {
            int i = l.IndexOf("CharaAssembler:", StringComparison.Ordinal);
            if (i < 0) continue;
            int j = l.IndexOf(' ', i);
            return j > i ? l.Substring(i, j - i) : l.Substring(i);
        }
        return "?";
    }

    // What a PNG that is not a loadable character card is, from the marker strings in its payload.
    internal static string CardKind(string file)
    {
        byte[] d;
        try { d = File.ReadAllBytes(file); } catch (Exception) { return "unreadable file"; }
        foreach (var m in new[] { "AIS_Chara", "AIS_Clothes", "AIS_Housing", "KoiKatuChara", "KoiKatuClothes", "EroMakeChara", "PlayHome" })
        {
            var b = System.Text.Encoding.UTF8.GetBytes(m);
            for (int i = 0; i + b.Length <= d.Length; i++)
            {
                int k = 0;
                while (k < b.Length && d[i + k] == b[k]) k++;
                if (k == b.Length) return m == "AIS_Chara" ? "HS2/AIS chara payload" : m + " payload";
            }
        }
        return "no known card payload";
    }

    internal static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message} | {FirstFrame(e)}";

    static string FirstFrame(Exception e)
    {
        var line = (e.StackTrace ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "?";
        if (line.StartsWith("at ")) line = line.Substring(3);
        int inIdx = line.IndexOf(" [0x", StringComparison.Ordinal);
        if (inIdx > 0) line = line.Substring(0, inIdx);
        int file = line.IndexOf(" in ", StringComparison.Ordinal);
        if (file > 0) line = line.Substring(0, file);   // no local paths in the report
        return line;
    }

    // Shader names of the character's materials that ShaderMap.ApplyUrp would send to the URP/Lit fallback.
    internal static SortedDictionary<string, int> Unfitted(GameObject chara)
    {
        var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var seen = new HashSet<Material>();
        foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !seen.Add(m)) continue;
                string n = m.shader ? m.shader.name : "<no shader>";
                if (HS2Next.ShaderMap.TryResolve(m.shader, out _) || n.StartsWith("Universal Render Pipeline/") || n.StartsWith("HS2Next/")) continue;
                d[n] = d.TryGetValue(n, out var k) ? k + 1 : 1;
            }
        return d;
    }

    // Destroys every scene root created while building (the character, and parts left at the root by a
    // failed build) and releases the render textures their materials reference (composited skins/clothes).
    static void Cleanup(HashSet<GameObject> before)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (before.Contains(go)) continue;
            var rts = new HashSet<RenderTexture>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    foreach (var id in m.GetTexturePropertyNameIDs())
                        if (m.GetTexture(id) is RenderTexture rt) rts.Add(rt);
                }
            Object.DestroyImmediate(go);
            foreach (var rt in rts) { rt.Release(); Object.DestroyImmediate(rt); }
        }
    }

    static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    // Creator RT hashes per card (Phase 4.4): the record of every card's composites that the snapshot tests lack (they hold
    // no read-back of the render textures). -rtRecord <dir>: after each build, the SHA-256 of mip 0 of every creator's RT
    // (CharaContext.Edit.Composites(): skin and clothes creators) is appended to <dir>/<card folder name>.tsv as
    // "card <tab> creator <tab> hash <tab> WxH" (appended, so a run resumed after a native crash adds to it; a later line of
    // a card replaces earlier ones when read). -rtCompare <dir>: the same hashes compared with that file; the cards that
    // differ are listed in the summary and in <dir>/<card folder name>.compare.tsv. Needs a graphics device (-render runs).
    // The read is synchronous (ReadPixels into one reused CPU texture, SHA-256 of its raw data): an asynchronous read-back's
    // staging buffer is released only at a frame end, which this batch loop never reaches (192 cards = about 40 GB of shared
    // GPU memory removed the D3D12 device once). Guard: the process's shared GPU memory (the Windows counter "GPU Process
    // Memory / Shared Usage", read through powershell.exe) at the start and after every 20 hashed cards; the hashes stop
    // (the cards after it counted, not hashed) when it rose 2 GB over the start or passed 30 GB, or cannot be read.
    // -rtProof N: the first card's RTs are read N more times between two readings of that counter.
    sealed class CreatorRtHashes
    {
        string recordFile, compareFile, diffFile;
        Dictionary<string, Dictionary<string, string>> recorded;
        int cards, equal, differ, missing, creatorsDiffer, noDevice, notHashed, sinceCheck, proof;
        readonly List<string> differing = new List<string>();
        const long GuardRise = 2L << 30, GuardAbsolute = 30L << 30;
        long sharedStart = -1, sharedPeak = -1;
        int checks;
        string stopped, proofLine;
        Texture2D cpu;
        byte[] buffer;

        public static CreatorRtHashes Begin(string recordDir, string compareDir, string cardDir, int proof = 0)
        {
            if (recordDir == null && compareDir == null) return null;
            string name = Path.GetFileName(cardDir.TrimEnd('\\', '/')) + ".tsv";
            var h = new CreatorRtHashes { proof = Math.Max(0, proof) };
            if (HS2Next.Chara.Edit.FaceDigest.HasDevice)
            {
                h.sharedStart = h.sharedPeak = SharedGpuBytes();
                if (h.sharedStart < 0) h.stopped = "the shared GPU memory counter could not be read at the start";
            }
            if (recordDir != null) { Directory.CreateDirectory(recordDir); h.recordFile = Path.Combine(recordDir, name); }
            if (compareDir != null)
            {
                h.compareFile = Path.Combine(compareDir, name);
                h.diffFile = Path.Combine(compareDir, Path.GetFileNameWithoutExtension(name) + ".compare.tsv");
                h.recorded = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                if (File.Exists(h.compareFile))
                    foreach (var g in File.ReadAllLines(h.compareFile).Select(l => l.Split('\t')).Where(t => t.Length >= 3).GroupBy(t => t[0]))
                    {
                        var d = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var t in g) d[t[1]] = t[2];   // a later line of a creator replaces an earlier one
                        h.recorded[g.Key] = d;
                    }
                File.WriteAllText(h.diffFile, "");
            }
            return h;
        }

        public void Card(string rel, CharaContext ctx)
        {
            if (ctx?.Edit == null) return;
            if (!HS2Next.Chara.Edit.FaceDigest.HasDevice) { noDevice++; return; }
            if (stopped != null) { notHashed++; return; }
            cards++;
            var now = new List<(string creator, string hash, string size)>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in ctx.Edit.Composites())
            {
                var rt = c.Creator?.Target;
                string key = c.Name;
                int k = seen.TryGetValue(key, out var v) ? v + 1 : 0;
                seen[key] = k;
                if (k > 0) key += "#" + k;
                now.Add((key, ReadHash(rt), rt ? rt.width + "x" + rt.height : "-"));
            }
            if (proof > 0)
            {
                // the first card's RTs read again and again: the shared GPU memory must stay flat
                long before = SharedGpuBytes(), mib = 0;
                int reads = 0, unequal = 0;
                for (int r = 0; r < proof; r++)
                {
                    int i = 0;
                    foreach (var c in ctx.Edit.Composites())
                    {
                        var rt = c.Creator?.Target;
                        if (rt) mib += (long)rt.width * rt.height * 4 / 1048576;
                        if (ReadHash(rt) != now[i].hash) unequal++;
                        reads++; i++;
                    }
                    long mid = SharedGpuBytes();
                    if (mid < 0 || before < 0 || mid - before > GuardRise / 4) { proofLine = $"proof stopped after {r + 1} of {proof} repetitions: shared GPU memory {Gb(mid)} (before {Gb(before)})"; break; }
                }
                long after = SharedGpuBytes();
                proofLine = (proofLine != null ? proofLine + "; " : "") + $"proof: {reads} reads ({mib} MiB at 4 bytes per pixel) of the first card's {now.Count} RTs, shared GPU memory before {Gb(before)}, after {Gb(after)} (rise {Gb(after - before)}), {unequal} reads with another hash";
                proof = 0;
            }
            if (++sinceCheck >= 20) { sinceCheck = 0; Guard(); }
            if (recordFile != null)
                File.AppendAllLines(recordFile, now.Select(x => rel + "\t" + x.creator + "\t" + x.hash + "\t" + x.size));
            if (recorded == null) return;
            if (!recorded.TryGetValue(rel, out var old)) { missing++; return; }
            var diffs = now.Where(x => !old.TryGetValue(x.creator, out var o) || o != x.hash).Select(x => x.creator).ToList();
            diffs.AddRange(old.Keys.Where(c => now.All(x => x.creator != c)));
            if (diffs.Count == 0) { equal++; return; }
            differ++;
            creatorsDiffer += diffs.Count;
            differing.Add(rel + ": " + string.Join(", ", diffs));
            File.AppendAllLines(diffFile, diffs.Select(c => rel + "\t" + c + "\t" + (old.TryGetValue(c, out var o) ? o : "-") + "\t" + (now.FirstOrDefault(x => x.creator == c).hash ?? "-")));
        }

        // the process's shared GPU memory against the start: stop the hashes over the guard
        void Guard()
        {
            long now = SharedGpuBytes();
            checks++;
            if (now > sharedPeak) sharedPeak = now;
            if (now < 0) stopped = $"the shared GPU memory counter could not be read after {cards} cards";
            else if (now - sharedStart > GuardRise || now > GuardAbsolute) stopped = $"after {cards} cards the shared GPU memory was {Gb(now)} (start {Gb(sharedStart)})";
            if (stopped != null) Debug.LogWarning("[CardImportTest] creator RT hashes stopped: " + stopped);
        }

        static string Gb(long bytes) => bytes < 0 ? "unknown" : (bytes / 1073741824.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " GB";

        // SHA-256 of mip 0 read synchronously into one reused CPU texture (8-bit RTs as RGBA32, half / float RTs as RGBAHalf /
        // RGBAFloat), the active render texture restored
        string ReadHash(RenderTexture rt)
        {
            if (!rt) return "none";
            if (!rt.IsCreated()) return "notcreated";
            var gf = rt.graphicsFormat;
            var tf = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsIEEE754Format(gf)
                ? (UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsHalfFormat(gf) ? TextureFormat.RGBAHalf : TextureFormat.RGBAFloat)
                : TextureFormat.RGBA32;
            if (!cpu) cpu = new Texture2D(rt.width, rt.height, tf, false, true) { name = "CardImportTest.rtRead" };
            else if (cpu.width != rt.width || cpu.height != rt.height || cpu.format != tf) cpu.Reinitialize(rt.width, rt.height, tf, false);
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                cpu.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            }
            finally { RenderTexture.active = prev; }
            var data = cpu.GetRawTextureData<byte>();
            if (buffer == null || buffer.Length < data.Length) buffer = new byte[data.Length];
            Unity.Collections.NativeArray<byte>.Copy(data, buffer, data.Length);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(buffer, 0, data.Length)).Replace("-", "").ToLowerInvariant();
        }

        // the "Shared Usage" of this process's GPU memory counters (bytes, summed over adapters), -1 when not readable
        static long SharedGpuBytes()
        {
            try
            {
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"$s = (Get-Counter -Counter '\\GPU Process Memory(pid_" + pid + "_*)\\Shared Usage' -ErrorAction Stop).CounterSamples | Measure-Object -Property CookedValue -Sum; [long]$s.Sum\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(60000)) { try { p.Kill(); } catch (Exception) { } return -1; }
                    return long.TryParse(o.Result.Trim(), out var v) ? v : -1;
                }
            }
            catch (Exception) { return -1; }
        }

        public void Finish(StringBuilder sb)
        {
            if (cpu) Object.DestroyImmediate(cpu);
            if (recordFile != null) sb.AppendLine($"  {"creator RT hashes recorded",-34} {cards,8}  cards -> {recordFile}{(noDevice > 0 ? $" ({noDevice} without a graphics device)" : "")}");
            if (sharedStart >= 0 || stopped != null)
                sb.AppendLine($"  {"creator RT hash guard",-34} {checks,8}  checks; shared GPU memory start {Gb(sharedStart)}, peak {Gb(sharedPeak)}; {(stopped != null ? "STOPPED " + stopped + $"; {notHashed} cards not hashed" : "not reached")}");
            if (proofLine != null) sb.AppendLine("  creator RT read " + proofLine);
            if (recorded == null) return;
            sb.AppendLine($"  {"creator RT hashes vs the record",-34} {equal,8}  cards equal, {differ} differ ({creatorsDiffer} creators), {missing} without a record ({compareFile})");
            foreach (var d in differing.Take(200)) sb.AppendLine("    rt differs: " + d);
        }
    }

    // Non-finite values on a character after the motion steps, beyond the body bones checked by the caller: every other
    // transform under the root (local position, rotation, scale; hair, clothes, accessories, head) and every blend-shape
    // weight of its skinned meshes.
    internal static void NonFinite(GameObject chara, IEnumerable<Transform> bodyBones, out int transforms, out int weights)
    {
        transforms = weights = 0;
        if (!chara) return;
        var skip = new HashSet<Transform>(bodyBones.Where(t => t));
        bool Bad(Vector3 v) => !float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z);
        foreach (var t in chara.GetComponentsInChildren<Transform>(true))
        {
            if (skip.Contains(t)) continue;
            var q = t.localRotation;
            if (Bad(t.localPosition) || Bad(t.localScale) || !float.IsFinite(q.x) || !float.IsFinite(q.y) || !float.IsFinite(q.z) || !float.IsFinite(q.w)) transforms++;
        }
        foreach (var r in chara.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = r.sharedMesh;
            if (!mesh) continue;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (!float.IsFinite(r.GetBlendShapeWeight(i))) weights++;
        }
    }
}
