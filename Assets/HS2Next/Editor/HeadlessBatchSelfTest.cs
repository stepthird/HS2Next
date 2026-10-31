// HeadlessBatch self-tests (HQ4): test-only members, never rows of the runner. They
// run through HeadlessBatch.Run with a plan that names them:
//   Leak  (HQ3): leaves a scene GameObject and a registered build hook behind; its batch line must be a hygiene FAIL that
//         names both, and the next member must start clean.
//   Clean (HQ3): PASS when no GameObject of Leak's name is in the scene and no build hook of Leak's id is registered.
//   Abort (HQ4): ends its own process at once (what a native crash does to a batch); the runner resumes the batch with the
//         next member and runs Abort alone afterwards (that process ends the same way: no report, a FAIL).
// Reports: Spike/out/tests_self/batch_self_<name>.txt, first line "SUMMARY: N PASS, M FAIL".
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HeadlessBatchSelfTest
{
    const string LeakName = "HeadlessBatchSelfTest planted leak";
    const string HookId = "headless-batch-self-test";

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "tests_self");

    static void Write(string name, int pass, int fail, string line)
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "batch_self_" + name + ".txt"), $"SUMMARY: {pass} PASS, {fail} FAIL\n{line}\n");
    }

    /// <summary>HQ3: leaves a GameObject and a build hook (the batch's hygiene check must name and remove both).</summary>
    public static void Leak()
    {
        new GameObject(LeakName);
        CharaBuildHooks.Register(HookId, HookStage.Final, _ => { });
        Write("leak", 1, 0, "planted: a scene GameObject '" + LeakName + "' and the build hook '" + HookId + "' (left on purpose)");
    }

    /// <summary>HQ3: the member after Leak starts clean.</summary>
    public static void Clean()
    {
        bool go = SceneManager.GetActiveScene().GetRootGameObjects().Any(g => g && g.name == LeakName);
        bool hook = CharaBuildHooks.Registered.Any(r => r.Id == HookId);
        bool ok = !go && !hook;
        Write("clean", ok ? 1 : 0, ok ? 0 : 1, $"planted GameObject in the scene {go}, planted hook registered {hook}");
        if (!ok) TestExit.Exit(1);
    }

    /// <summary>HQ4: ends this process at once (no report, no exit code of its own).</summary>
    public static void Abort()
    {
        UnityEngine.Debug.Log("HeadlessBatchSelfTest.Abort: ending the process");
        Process.GetCurrentProcess().Kill();
    }
}
