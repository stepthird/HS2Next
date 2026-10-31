// A scene-wide part of the realization: the map, the view camera and the
// chara light (Package C), later effects, sky, sound (3.3, 3.6). SceneRuntime calls every registered part after
// its own node work: OnChanged for each change batch, Apply at the end of Apply(), LateApply at the end of
// LateApply(), Clear when the realization is cleared (new scene, load, dispose) and OnLoadedRealized when a
// loaded model has been scheduled for realization (RealizeAll).
// Clear() serves new, load and dispose alike and only resets what the part realized; a part that owns objects or
// global state outside the runtime root (the chara light under an adopted app camera, LookTargets.DefaultCamera)
// releases them when the runtime is disposed: through the callback given to SceneRuntime.AddPart, or by
// implementing System.IDisposable. SceneRuntime.AddStandardParts registers the 2.2 parts with their callbacks.
namespace HS2Next.Scenes.Runtime
{
    public interface ISceneRuntimePart
    {
        /// <summary>Called once when the part is registered (<see cref="SceneRuntime.AddPart"/>).</summary>
        void Attach(SceneRuntime r);
        /// <summary>Every change batch of the session, in delivery order (record dirty state only; no bus calls).</summary>
        void OnChanged(SceneChangeBatch b);
        /// <summary>End of <see cref="SceneRuntime.Apply"/>, after the nodes were built, changed, moved and shown.</summary>
        void Apply();
        /// <summary>End of <see cref="SceneRuntime.LateApply"/>, after the world-scale enforcement.</summary>
        void LateApply();
        /// <summary>The realization is cleared (before a new or loaded model is realized, and on dispose).</summary>
        void Clear();
        /// <summary>A loaded model was scheduled for realization (<see cref="SceneRuntime.RealizeAll"/>).</summary>
        void OnLoadedRealized();
    }
}
