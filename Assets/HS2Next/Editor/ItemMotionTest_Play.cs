// ItemMotionTest N9 (T16): the frame order of item motion in play mode, through
// Editor/PlayModeHarness.cs and `Tools/unity_batch.ps1 -NoQuit` (no graphics needed):
//   & Tools\unity_batch.ps1 -Method HS2Next.Scenes.Runtime.Tests.ItemMotionTest.RunPlay -NoQuit -NoGraphics -LogName item_motion_play
// A runtime with its play-mode drivers (SceneRuntimeDriver, ItemMotionDriver, SceneRuntimeLateDriver), a character
// (the female visual target, Idle_00, AutoStep) with 498 猫じゃらし at N_Hand_R, and 1022 鳥 with 1399 FK軸 (FK on) under it.
//   N9.1 DefaultExecutionOrder: CharaMotion 1000 < ItemMotionDriver 1050 < SceneRuntimeLateDriver 1100; the driver is on the
//        runtime root
//   N9.2 for 60 frames: every item motion stepped in that frame; the chain root particle of 498 at the hand-carried root
//        bone after the frame (the item stepped after the character's final hand pose of the same frame)
//   N9.3 the nested item steps after its parent item (sequence numbers), and every item step of the frame precedes the
//        runtime's LateApply of that frame (a probe part records the step counter at LateApply)
// Report: Spike/out/items/item_motion_play_report.txt, first line "N PASS, M FAIL"; exit 1 on any FAIL.
namespace HS2Next.Scenes.Runtime.Tests
{
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using HS2Next.Content;
    using HS2Next.Content.Studio;
    using HS2Next.Motion;
    using HS2Next.Save;
    using HS2Next.Scenes;
    using HS2Next.Scenes.Runtime;
    using UnityEngine;

    public static partial class ItemMotionTest
    {
        static string PlayReportPath => Path.Combine(OutDir, "item_motion_play_report.txt");

        /// <summary>Batch entry (needs -NoQuit): enters play mode and runs <see cref="PlayMain"/>.</summary>
        public static void RunPlay()
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(PlayReportPath, "0 PASS, 1 FAIL\nItemMotionTest.play: started, no result yet\n", new UTF8Encoding(false));
            global::PlayModeHarness.Start(typeof(ItemMotionTest).FullName + "." + nameof(PlayMain), PlayReportPath, 15);
        }

        static IEnumerator PlayMain() => PlayCase.Main();

        /// <summary>Records the global item-step counter and the frame at every LateApply of the runtime.</summary>
        sealed class LateProbe : ISceneRuntimePart
        {
            public long SequenceAtLate = -1;
            public int FrameAtLate = -1;
            public void Attach(SceneRuntime r) { }
            public void OnChanged(SceneChangeBatch b) { }
            public void Apply() { }
            public void LateApply() { SequenceAtLate = ItemMotion.Sequence; FrameAtLate = Time.frameCount; }
            public void Clear() { }
            public void OnLoadedRealized() { }
        }

        static class PlayCase
        {
            static readonly StringBuilder body = new StringBuilder();
            static int pass, fail;

            static void Ok(string name, bool ok, string detail = "")
            {
                if (ok) pass++; else fail++;
                body.AppendLine($"{(ok ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")")}");
            }

            static void Line(string s) => body.AppendLine(s);

            static int Order(Type t) => t.GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;

            public static IEnumerator Main()
            {
                var total = Stopwatch.StartNew();
                Line($"play mode entered {(global::PlayModeHarness.EnterSeconds):F1} s after the request; batch {Application.isBatchMode}, graphics {SystemInfo.graphicsDeviceType}");
                int oc = Order(typeof(CharaMotion)), oi = Order(typeof(ItemMotionDriver)), ol = Order(typeof(SceneRuntimeLateDriver));
                Ok("N9.1 execution order CharaMotion < ItemMotionDriver < SceneRuntimeLateDriver", oc < oi && oi < ol, $"{oc} < {oi} < {ol}");

                RuntimeServices services = null;
                try
                {
                    var gd = global::CardResolveTest.BuildDb(null);
                    services = new RuntimeServices { Catalog = gd, Db = gd.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false }), Hs2Root = global::CardResolveTest.GameRoot };
                }
                catch (Exception e) { Ok("catalog and Studio lists", false, e.ToString()); }
                if (services != null)
                {
                    var steps = Run(services);
                    while (true)
                    {
                        object cur;
                        try
                        {
                            if (!steps.MoveNext()) break;
                            cur = steps.Current;
                        }
                        catch (Exception e) { Ok("N9 ran without an exception", false, e.ToString()); break; }
                        yield return cur;
                    }
                    try { services.Catalog.UnloadAll(); } catch (Exception e) { Line("catalog unload threw: " + e.Message); }
                }
                Line($"total {total.ElapsedMilliseconds} ms");
                File.WriteAllText(PlayReportPath, $"{pass} PASS, {fail} FAIL\n" + body, new UTF8Encoding(false));
                global::PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
            }

            static IEnumerator Run(RuntimeServices services)
            {
                var t = global::TestTargets.Load();
                string card = global::TestTargets.Abs(t.visual.female);
                var session = new SceneSession(services.NewSessionOptions());
                var rt = new SceneRuntime(session, services);
                var probe = new LateProbe();
                rt.AddPart(probe);
                Ok("N9.1 the play-mode runtime has its drivers and ManualStepping is off",
                   rt.Root.GetComponent<SceneRuntimeDriver>() && rt.Root.GetComponent<ItemMotionDriver>() && rt.Root.GetComponent<SceneRuntimeLateDriver>() && !services.Settings.ManualStepping);
                var ac = rt.Ops.AddCharaFromCard(card);
                string chara = ac?.NewId;
                if (chara != null) session.SetAnimation(chara, global::SceneRuntimeTest.Fixture.Idle);
                string cat = chara != null ? rt.Ops.AddItem(ContentId.Vanilla(498), chara, "N_Hand_R")?.NewId : null;
                string bird = rt.Ops.AddItem(ContentId.Vanilla(1022))?.NewId;
                string fk = bird != null ? rt.Ops.AddItem(ContentId.Vanilla(1399), bird)?.NewId : null;
                yield return null;
                yield return global::PlayModeHarness.WaitFrames(2);
                var catR = rt.FindItem(cat); var birdR = rt.FindItem(bird); var fkR = rt.FindItem(fk);
                var mc = ItemMotion.Of(catR); var mb = ItemMotion.Of(birdR); var mf = ItemMotion.Of(fkR);
                bool built = rt.FindChara(chara)?.State == RealizationState.Built && mc?.Dynamic != null && mb?.Animation != null && mf?.Fk != null;
                Ok("fixture: character, 498 at N_Hand_R, 1022 with 1399 under it, all with motion", built,
                   $"chara {rt.FindChara(chara)?.State}, 498 {catR?.State} dyn {mc?.Dynamic != null}, 1022 {birdR?.State} anim {mb?.Animation != null}, 1399 {fkR?.State} fk {mf?.Fk != null}");
                if (!built) { rt.Dispose(); yield break; }
                for (int i = 0; i < mf.Fk.Count; i++) session.Set(fk, "item.fk.bones[" + FieldPath.Quote(mf.Fk.Names[i]) + "]", new Vec3(10f, 20f * i, 0f));
                session.Set(fk, "item.fk.enabled", true);
                yield return global::PlayModeHarness.WaitFrames(3);

                var chain = mc.Dynamic.Chains.First(c => c.ParticleCount > 1);
                var root = chain.ParticleTransform(0);
                var hand = rt.FindChara(chara).AttachPoints["N_Hand_R"];
                int frames = 60, stepped = 0, followed = 0, nested = 0, beforeLate = 0, handMoved = 0;
                float worst = 0f;
                Vector3 prevHand = hand.position;
                for (int k = 0; k < frames; k++)
                {
                    yield return null;
                    int f = Time.frameCount;
                    // the harness ticks after the player loop of a frame: the motion and the probe ran in that frame
                    int frame = mc.LastStepFrame;
                    if (mc.LastStepFrame == mb.LastStepFrame && mb.LastStepFrame == mf.LastStepFrame && frame >= f - 1) stepped++;
                    float e = (chain.Position(0) - root.position).magnitude;
                    worst = Mathf.Max(worst, e);
                    if (e <= 1e-5f) followed++;
                    if ((hand.position - prevHand).sqrMagnitude > 1e-12f) handMoved++;
                    prevHand = hand.position;
                    if (mf.LastStepSequence > mb.LastStepSequence && mb.LastStepFrame == mf.LastStepFrame) nested++;
                    if (probe.FrameAtLate == frame && probe.SequenceAtLate >= Math.Max(mc.LastStepSequence, mf.LastStepSequence)) beforeLate++;
                }
                Line($"frames {frames}: stepped {stepped}, chain root followed {followed} (max {worst:E2}), hand moved {handMoved}, nested after parent {nested}, before LateApply {beforeLate}");
                Ok("N9.2 every item motion stepped once per frame by ItemMotionDriver", stepped == frames, $"{stepped} of {frames}");
                Ok("N9.2 the chain root particle of 498 is at the hand-carried root bone after each frame (items after the character)",
                   followed == frames && handMoved >= frames / 2, $"{followed} of {frames}, max {worst:E2}, hand moved in {handMoved}");
                Ok("N9.3 the nested item (1399 under 1022) steps after its parent in every frame", nested == frames, $"{nested} of {frames}");
                Ok("N9.3 every item step precedes the runtime's LateApply of the same frame", beforeLate == frames, $"{beforeLate} of {frames}");
                Ok("no motion errors", mc.Motion.Errors == 0 && mb.Motion.Errors == 0 && mf.Motion.Errors == 0);
                rt.Dispose();
            }
        }
    }
}
