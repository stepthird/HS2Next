// The scene clock's tick hook (Phase 3.6 package K). A registered scene part
// (ISceneRuntimePart) that also implements ISceneTickPart is ticked by its SceneRuntime with the scene clock's delta:
//   once per frame, right after Apply() of the frame that began the clock frame (SceneRuntime.BeginFrame: the play-mode
//   driver, or Tick(dt) with ManualStepping), with SceneClock.FrameDt (0 while the scene is paused), and
//   once per step inside SceneRuntime.Step (scene.step, the toolbar step button), with the step's dt, in the frame order
//   Apply → tick parts → characters → items → map scripts → LateApply.
// Routes (package R) and the particle / map Animator / wind / video freeze (package K) are tick parts; nothing in the
// scene reads Time.deltaTime for scene motion.
namespace HS2Next.Scenes.Runtime
{
    public interface ISceneTickPart
    {
        /// <summary>Advances the part by <paramref name="dt"/> scene seconds (0 while paused outside a step).</summary>
        void Tick(float dt);
    }

    /// <summary>A part told at once when the global pause changes (before the next frame's tick).</summary>
    public interface IScenePauseListener
    {
        void PausedChanged(bool paused);
    }
}
