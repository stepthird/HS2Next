// The character stages (engine side of the plugin Api; experimental: they may change in any minor release): a plugin adds a
// motion stage to a scene character. A rebuild makes a new body with a new list of stages, so a stage is given as a factory: the
// host calls it whenever the character is built. The stage runs in the character's step at its phase, also inside a motion pass.
using System;
using HS2Next.Motion;
using UnityEngine;

namespace HS2Next.Api.Unity
{
    /// <summary>Motion stages on scene characters. Main thread only. Implemented by the host.</summary>
    [Experimental("the character stages may change in any minor release")]
    public interface ICharaStages
    {
        /// <summary>
        /// Adds a stage to a scene character. <paramref name="make"/> runs at once when the character is built, and again after every
        /// rebuild, with the new body's root; a change of clothes, hair or accessories rebuilds nothing, so it does not run then. The old
        /// stage object goes with the old body: what must outlive a rebuild lives in what <paramref name="make"/> closes over. A stage
        /// that was off stays off in the new body. Null from <paramref name="make"/> means no stage on this body.
        /// <para>
        /// The stage's <see cref="IMotionStage.Phase"/> and <see cref="IMotionStage.Name"/> are read once, after <paramref name="make"/>.
        /// A phase outside -90 to 940 is refused and counted as a fault of the plugin. A stage that writes a bone takes the write off
        /// itself, in a stage of its own at an early phase. A throw from a stage or from <paramref name="make"/> is a fault of the plugin;
        /// the stage stays until the plugin faults. A stage does not run in the side-effect-free pose reads of the host.
        /// </para>
        /// <para>
        /// Dispose the result to take the stage off. Every stage of a plugin goes when it unloads, is disabled or faults; a stage goes when
        /// its node can never come back; a deleted node keeps its stage for an undo. No stage is saved, and none outlives the scene: add
        /// them again when a scene is loaded.
        /// </para>
        /// </summary>
        /// <param name="nodeId">The scene node of the character.</param>
        /// <param name="make">Makes the stage for the body whose root it is given.</param>
        /// <returns>Takes the stage off when disposed; later calls do nothing.</returns>
        /// <exception cref="ArgumentException"><paramref name="nodeId"/> is no character.</exception>
        IDisposable Add(string nodeId, Func<GameObject, IMotionStage> make);
    }
}
