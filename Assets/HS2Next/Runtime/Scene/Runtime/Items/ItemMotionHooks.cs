// The seam between item appearance (package A: ItemRealization, the material writer) and item motion (package B:
// animation, item FK, dynamic bones). ItemRealization calls the hooks; the motion
// package registers the handlers (from SceneRuntime.CreateDefault). While a hook is null the item does not move
// (the 2.2 behaviour: the first active Animator enabled iff ItemComponent.isAnime, never stepped by HS2Next).
//
//   Built      after the instance exists, its materials are owned and swapped, its lights prepared and its attach
//              transform set, before the pattern textures / panel / colour write (HS2 build steps 3
//              and 5): the motion is built from the fresh prefab pose (FK base pose) and the stored anime / FK /
//              dynamic-bone state is applied. The realization exposes Instance, Component, Row, Animator (the first
//              Animator of the active hierarchy, NOT enabled by package A when a Built handler exists), DynamicBones
//              (every DynamicBone of the active hierarchy) and Motion (free for the handler, e.g. the ItemMotion).
//   Changed    changed field paths under item.fk, item.dynamicBones, item.anime (the canonical texts the runtime's
//              ApplyChanges received, only those overlapping the three prefixes), after the realization applied its
//              own paths of the same pass. Not called when the change rebuilds the item (Built runs instead).
//   Releasing  before the content is destroyed (graveyard trims, rebuilds, scene clear, dispose: from ReleaseContent,
//              which precedes every destruction, once per build); Instance is still alive. Motion is set to null by the
//              realization afterwards.
//   FkBase, Describe  optional helpers (added after the first commit; see their comments).
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Runtime
{
    public static class ItemMotionHooks
    {
        /// <summary>The item was built.</summary>
        public static Action<ItemRealization> Built;
        /// <summary>Changed paths under item.fk / item.dynamicBones / item.anime.</summary>
        public static Action<ItemRealization, IReadOnlyCollection<string>> Changed;
        /// <summary>The item's content is about to be destroyed.</summary>
        public static Action<ItemRealization> Releasing;

        /// <summary>
        /// Optional: the FK base rotation (local Euler degrees) of a found item bone, for the capture rule (enabling
        /// FK through item.set first writes the missing `fk.bones` entries). Null, or null for a bone: package A uses the
        /// bone's current localEulerAngles.
        /// </summary>
        public static Func<ItemRealization, string, UnityEngine.Vector3?> FkBase;
        /// <summary>
        /// Optional: adds or replaces members of item.state's `realized` object (e.g. `animator` {driven, state, time} from
        /// the animation stage, a `motion` object). Called after package A filled it.
        /// </summary>
        public static Action<ItemRealization, IDictionary<string, object>> Describe;

        /// <summary>Path prefixes routed to <see cref="Changed"/>.</summary>
        public static readonly string[] MotionPrefixes = { "item.fk", "item.dynamicBones", "item.anime" };
    }
}
