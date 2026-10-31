// TestCatalog: the warm catalog the members of a HeadlessBatch share (the vanilla and the zipmod lists read once).
// Get(sb, build): outside a batch, or for a member that asks for its own catalog (BatchMember.FreshCatalog or the plan's
// option fresh), build(sb) as before. Inside a batch the first member that asks builds the catalog with build(sb); later
// members with the same settings get the same catalog and the same lines in their report (the first build's numbers).
// Settings that differ (the settings file, the command-line overrides, the switches the build reads) build a new one.
// Between members ResetShared() unloads its bundles (UnloadAll) and puts back what members may change on it: the byte
// budget, the unload interval, its log and its notes (CAB clashes, CAB rewrites, texture findings) as they were after the
// build. The settings' global effects of building a catalog are applied again for a member that gets the warm one.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HS2Next.Content;
using UnityEngine;

public static class TestCatalog
{
    static ContentCatalog shared;
    static string sharedKey, buildText;
    static long budgetMB;
    static double unloadMinSec;
    static readonly Dictionary<string, int> lists = new Dictionary<string, int>();

    /// <summary>The warm catalog of the running batch, or null.</summary>
    public static ContentCatalog Shared => shared;
    public static int Builds { get; private set; }
    public static int Reuses { get; private set; }

    static string Key(ContentSettings s) =>
        JsonUtility.ToJson(s) + "|ai " + ContentCatalog.AiInstallOpensCharacterLists;

    /// <summary>A test's catalog (see the file header).</summary>
    public static ContentCatalog Get(StringBuilder sb, Func<StringBuilder, ContentCatalog> build)
    {
        if (!HeadlessBatch.InMember || HeadlessBatch.CurrentFreshCatalog) return build(sb);
        var s = ContentSettings.LoadDefault(TestArgs.Get());
        string key = Key(s);
        if (shared != null && key == sharedKey)
        {
            // what building one does outside the catalog object
            HS2Next.Chara.TextureGuard.CacheRoot = s.ResolvedCacheDir;
            ContentCatalog.SideloaderZipmodSet = s.sideloaderZipmodSet;
            ContentCatalog.SideloaderAssetRedirect = s.sideloaderAssetRedirect;
            Reuses++;
            sb?.Append(buildText);
            return shared;
        }
        Drop();
        var own = new StringBuilder();
        var c = build(own);
        sb?.Append(own);
        shared = c; sharedKey = key; buildText = own.ToString();
        budgetMB = c.BundleCacheMB; unloadMinSec = c.UnloadUnusedMinSec;
        lists.Clear();
        foreach (var (name, l) in Lists(c)) lists[name] = l.Count;
        Builds++;
        return c;
    }

    static IEnumerable<(string, IList)> Lists(ContentCatalog c)
    {
        yield return ("Log", c.Log);
        yield return ("CabClashes", c.CabClashes);
        yield return ("CabRewrites", c.CabRewrites);
        yield return ("TextureFindings", c.TextureFindings);
    }

    /// <summary>Between members: the warm catalog back to its state after the build (see the file header).</summary>
    public static void ResetShared(List<string> resets)
    {
        if (shared == null) return;
        shared.UnloadAll();
        if (shared.BundleCacheMB != budgetMB) { shared.BundleCacheMB = budgetMB; resets.Add("catalog.BundleCacheMB"); }
        if (shared.UnloadUnusedMinSec != unloadMinSec) { shared.UnloadUnusedMinSec = unloadMinSec; resets.Add("catalog.UnloadUnusedMinSec"); }
        foreach (var (name, l) in Lists(shared))
        {
            if (!lists.TryGetValue(name, out int n) || l.Count <= n) continue;
            while (l.Count > n) l.RemoveAt(l.Count - 1);
            if (name != "Log") resets.Add("catalog." + name);
        }
    }

    /// <summary>Forgets the warm catalog (its bundles unloaded).</summary>
    public static void Drop()
    {
        if (shared != null) shared.UnloadAll();
        shared = null; sharedKey = null; buildText = null;
        lists.Clear();
    }
}
