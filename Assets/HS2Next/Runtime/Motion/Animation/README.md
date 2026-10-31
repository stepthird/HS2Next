# Runtime/Motion/Animation

Studio character animation playback. The game's own controllers and clips are played: Unity 6 loads the Unity 2018.4
`RuntimeAnimatorController`s from `abdata/studio/anime/*.unity3d` and from zipmod bundles through the catalog. Each opened
2018.4 bundle logs one "Serialized files [version 17] … deprecated" warning; nothing else. No game code is copied: the
behaviour (loading, speed, pattern, forced loop, option items, hand shapes) was measured in the running game.

## Files

| File | Content |
| --- | --- |
| `AnimationDriver.cs` | Two engines for one Animator: `Playable` (default: PlayableGraph, `AnimatorControllerPlayable` → `AnimationPlayableOutput`, `DirectorUpdateMode.Manual`, `graph.Evaluate(dt)`) and `Animator` (fallback: the controller in the Animator's own slot, component disabled, `Animator.Update(dt)`). Play, state info, parameters, layer weights. |
| `AnimationStage.cs` | `IMotionStage` at `MotionPhase.Animation` (first in every step); calls `CharaAnimation.Tick`. |
| `CharaAnimation.cs` | The facade component on the character root (`CharaAnimation.Of(root)`): load, control, scene state, the per-step tick, `AnimationOptions` (switches), `AnimationSceneState` and the shape-bone guard. |
| `HSwayTable.cs` | The sway table of an H row: the lines of its motion id (and owner), resolved per layer-0 state into the bust stage's four enables and the per-side bust shape masks. |
| `AnimeOptionItems.cs` | Option items: load, parenting, scale, counter-scale, sync, visibility, release. |
| `HandShapeStage.cs` | `HandShapeData` (parser of `cf_anmShapeHand` / `cf_customhand` from `list/customshape.unity3d`) and the `IMotionStage` at `MotionPhase.Shape`. |
| `AnimationSetup.cs` | `Attach(ctx)`: the facade, the animation stage and the hand stage. Registered first in `MotionSetup.Attach`. |

## Usage (Studio layer)

```csharp
var anim = CharaAnimation.Of(charaRoot);
anim.Load(db.FindAnime(19, 0, 0));                 // or anim.Load(db, guid, group, category, no, t)
anim.Speed = 0.5f;  anim.ForceLoop = true;  anim.Pattern = 0.3f;
anim.NormalizedTime = 0.25f;                        // re-poses at once, also at speed 0
anim.OptionVisible = false;                         // the Studio's option-item visibility
anim.Hands[0] = 4; anim.Hands[1] = 0;               // left pattern 4, right = animation
var state = anim.Save();  anim.Restore(db, state);  // the scene's animation fields; JsonUtility-serialisable
anim.Restart();  anim.Stop();
```

Everything is evaluated inside `CharaMotion.Step` (`LateUpdate` in play mode, or `Step(dt)` by hand). The step length is
`frame.Dt × Speed`, where `frame.Dt` already includes `CharaMotion.TimeScale`, `Paused` and `MaxStep`. Neither engine lets
Unity advance a controller on its own, so edit mode, tests and play mode behave the same.

## Behaviour

- **Load.** Steps 1–8 run in this order:
  1. The controller is loaded through `catalog.LoadAsset<RuntimeAnimatorController>(row)` (real bundle, then the row's
     zipmod, then any zipmod; no manifest). It is bound again only when the controller object or the engine changes.
  2. Plain rows: the override bookkeeping is cleared, PV flags 0–3 are set, motion is off.
  3. Option items are loaded, then `OptionItemsLoaded` is raised with the new objects (see Option items).
  4. `Play(clip, 0, t)`.
  5. The first existing parameter of `height1` / `height` is set to the body height: card `shapeValueBody[0]`, males 0.75.
  6. The option parameters are applied again. They write to the Animator only while an H row plays.
  7. The state hash is stored for the forced loop.
  8. The pose is sampled again with dt 0 (switch `ResampleOnSetTime`).

  A missing controller leaves the current motion unchanged and logs one line. Speed, pattern, forced loop, option visibility
  and hand patterns are per-character settings and survive loads.
- **Normalized time.** It is read raw from layer 0 and never wrapped, so 10 s of `Idle_00` reads 3.75. Setting it plays the
  forced-loop state at that time and samples again.
- **Speed.** Clamped to ≥ 0. 0 is the pause: the clip pose is still written every step, so FK snap-back keeps working. The
  state speed multipliers (0.5 on the `f_studio_34` idles) come from the controller.
- **Forced loop.** After sampling, if layer 0 is non-looping and its time is ≥ 1, the stored state restarts at 0. By
  default the restart is evaluated in the same step; `GameFrameDelay` queues it for the next step, as the game does.
- **Option items.**
  - Prefab, controller and override controller are loaded through the catalog (row zipmod first). `height` is set when the
    item controller has it.
  - Parenting is by depth-first name search over the whole character (root included), at local zero / identity.
  - Scale: `counterScale` items get a one-shot counter-scale in the first step after the load. Otherwise every moved object
    gets the prefab root scale, as in the game.
  - Synced items jump to the body's state hash and time in the same step. Unsynced animated items run on their own clock at
    speed 1.
  - Renderers are on if and only if `OptionVisible && CharacterVisible`.
  - Items are released at every load (also a reload of the same row), at `Stop` and when the component is destroyed.
  - Owner hook: `event Action<CharaAnimation, IReadOnlyList<GameObject>> OptionItemsLoaded` is raised by `Load` after the
    items are instantiated, parented and given their visibility, before the motion plays (not raised when the row has
    none). The list is `AnimeOptionItems.Objects()`: each item root plus each child moved under a character bone, disjoint
    hierarchies holding every new renderer. The owner applies its per-object policies there, for example
    `ShaderMap.ApplyUrp` (the character was mapped before the items existed). A throwing handler is logged.
- **Hand patterns.** One table for both sexes. Pattern p ≥ 1 uses key p − 1 (rotation only, `Quaternion.Euler(x, y, z)`) on
  the 15 finger bones of that hand, every step. Pattern 0 writes nothing. Out-of-range values are ignored (the game would
  enable the hand with the old angles).
- **H rows.** Every H row of the lists plays (there is no switch).
  - The override controller is set by clip name on the current controller (stacking kept as in the game; the override
    controllers this component builds are destroyed with the base controller, at `Stop` and on destroy), `breastLayer`
    (weight 1, the clip played on it; the previous extra layer back to 0), `Breast1` / `Breast` / `breast` = the bust
    slider, `motion` from the pattern when `isMotion`, the 8 PV flags, `speed` = 1, and the sway table.
  - **Sway table** (`HSwayTable`, `CharaAnimation.Sway`): loaded through the catalog from the row's sway bundle and file;
    the lines of the row's motion id are kept (in the tables whose file name starts with `ail` or `ai3p` the second cell is
    an owner index, matched against the row's owner column). Each step after sampling, the first line whose state is the
    layer-0 state applies: the breast sway flags → `BustStage.AnimeBustL/R`, the hip flags → `HipL/R`
    (`BustStage.SetAnimeSwitches`), the seven bust shape flags and the nipple flag → the body's bust shape masks of that
    side (`ShapeEngine.BustMaskIds` = 2–8, 32; a masked id is evaluated at 0.5 on that side's bust rows; the nipple's own
    rows `cf_J_Mune_Nip02_s_*` / `cf_J_Mune_Nipacs01_*` are written only by full shape updates with the slider values, so a
    mask never reaches them, as in HS2). No matching line, an H row without a table and `Stop` enable everything; a plain
    row keeps the table loaded (as HS2 does) and enables everything because no line matches its state.
  - A mask change writes that side's bust bones (`ShapeEngine.ApplyBustSide`), puts the bone modifiers on top again, lets
    the shape-bone guard keep the new values and captures the breast chains again (`BustStage.ResetPosition`). When all
    masks of a side open again, the side's bones get their build values back exactly.
  - Tests play H rows only on clothed test characters and judge numbers (states, layer weights, parameters, flags, bone
    values); nothing of an H pose is rendered or written (`Editor/HAnimeTest.cs`).
- **Stop (HS2Next addition).** The Studio has no stop. `Stop()` releases the motion and its items, disables the Animator as
  built, and puts every `p_cf_anim` skeleton bone back to the post-build pose captured at attach.
- **Mouth.** Animation never touches the face. Load, restart, pause and set-time keep the mouth slider, pattern and lip sync;
  the test checks this.

## Switches (`AnimationOptions`, per character; defaults from `AnimationSetup.Defaults`)

| Switch | Default | Effect |
| --- | --- | --- |
| `Engine` | `Playable` | `Animator` = fallback engine. Both give identical poses and times (test: 0 difference over 465 bones after 300 steps). |
| `RestoreShapeBones` | true | After every sample, the 89 body shape-engine targets (the destinations of `Resources/shape_body_map`) are written back to their post-build values. Only differing channels are written, so the "unchanged since I wrote it" checks of the dynamic-bone and ABMX writers keep working. Call `RecaptureShapeBones()` / `OnCardChanged()` after a shape edit. |
| `ResampleOnSetTime` | true | Load, set time and restart sample again at once, also at speed 0. false: the change shows at the next step. In the game it never shows at speed 0; that variant is not reproduced. |
| `ReapplyHeightOnCardChange` | true | `OnCardChanged()` applies `height` again. |
| `SetMotionOnSyncedItemsAtLoad` | true | Synced items get `motion` = pattern at load. |
| `SkipMissingOptionParents` | true | A missing parent bone skips that entry and logs it. false = abort the remaining blocks. |
| `PlayRootMotionInPlace` | true | The Animator applies root motion as the game's does (`applyRootMotion` on: with it off, Unity 6 puts a clip's root-node orientation offset, for example the 180° of some male H controllers, on `cf_J_Root` instead of `cf_J_Hips`); `p_cf_anim` is put back to its rest after every evaluation, so it never moves. Vanilla clips are baked. A non-zero `Animator.deltaPosition` logs one "unbaked root motion" line per controller. false = the game's drift. |
| `ClearStaleHOverride` | true | H playback only. |
| `GameFrameDelay` | false | Forced-loop restart and synced-item jump are queued for the next step, as the game does. |

`AnimationSetup.Enabled`, `AnimationSetup.LoadInitialPose` (false: the character keeps the rest pose until a load; true: the
Studio's 0/0/0 T-pose at attach, given `StudioDbProvider` or `catalog.StudioLists`).

HS2 comparison: the constant rest offsets of the twist bones in the Studio (`cf_J_LegKnee_back_*` 3.5°,
`cf_J_LegKnee_dam_*` 1.75°, `cf_J_ArmElbo_dam_01_*` 1.68°, `cf_J_Hand_dam_*` ~1.9°, `cf_J_SiriDam_*` 0.56°) do not come
from the T-pose clip: `Editor/CompareTest.cs` loads 0/0/0 explicitly and they stayed. They are the joint correction (all
categories on through the HS2US plugin on the reference install, `Runtime/Motion/Corrective/README.md`) applied to the
T-pose (for example `cf_J_LegLow01_*` is 3.5° in the T-pose and its correction row copies it). `LoadInitialPose` keeps its
default.

## Measured engine facts

- **Playable engine:** the Animator component must be enabled, otherwise the output writes nothing. Its controller slot stays
  empty.
- **Animator engine:** `Animator.Update` works with the component disabled. With `CullUpdateTransforms` / `CullCompletely`
  and no renderer in view (batch), nothing is written, so `AlwaysAnimate` is required (it is the prefab value and is
  enforced). An inactive GameObject is never evaluated.
- **Queued `Play`:** in both engines it is consumed by the next evaluation, also with dt = 0. The state info reports the old
  state until then.
- The evaluation that consumes `Play(state, 0, 0)` does not add that evaluation's delta: it reports exactly 0. Under
  `GameFrameDelay` the forced-loop cycle at speed 0.7 is therefore 373 steps; with the default (same-step) mode it is 372.
- **Worked examples:** the reference poses match to ≤ 5e-6 per quaternion component; the hand examples to ≤ 3e-6.
- **Root motion:** all inspected clips (vanilla, the mod Slow Walk over 10 cycles) are baked; `p_cf_anim` never moves.
- **Cost:** the animation stage takes about 30–55 µs per step for one character (varies between runs), hands < 1 µs, no
  allocation per step. A controller load takes 280–360 ms for the 184-clip `f_studio_34` bundle when the bundle is cold,
  about 55 ms for other bundles, and 0.2 ms when the bundle is cached.
- **Bound `*_s` bones:** once `f_studio_34` was bound on a character, 8 `*_s` bones (6 left-leg shape bones,
  `cf_J_ArmUp01/02_s_R`) are in the Animator's written set under every later controller too. On the test character at t 0
  and 0.5 their written values equalled the shape values, so the clip-value defect was not visible. The guard is tested as a
  mechanism: all channels are perturbed, and everything is back after one step.

## Tests

- `Editor/AnimSpikeTest.cs` (`-executeMethod AnimSpikeTest.Run [-render]`): the engine measurements. Report:
  `Spike/out/motion/anim_spike.txt`, renders `anim_spike_*.png`.
- `Editor/AnimationTest.cs` (`-executeMethod AnimationTest.Run [-render]`):
  - Rows: 19/0/0, 11/5/0, 19/1/11, 15/31/1 + rifle, 15/31/0, mod 69/662/6625.
  - Cards: the visual targets of `Tools/targets.json` (one female, one male).
  - Report: `Spike/out/motion/anim_report.txt`.
  - Renders: `anim_{stand,walk,sit}_{0,05}.png` and `anim_rifle_039.png`. The walk is the mod Slow Walk; vanilla has no walk
    loop.

## Open issues

- **URP shaders of option items:** the URP shader mapping (`ShaderMap.ApplyUrp`) is applied to the character by the caller;
  option items are instantiated later by `Load`. The Studio layer maps them in its `OptionItemsLoaded` handler, as the render
  test does.
- **Card change:** the Studio layer calls `OnCardChanged()` after a card change and `OnCharacterScaleChanged()` after the
  guide scale changes.
- **Not wired:** the PV copy (`PvFlags` are stored only); timeline-style keyed time and speed (`NormalizedTime` / `Speed`
  are the hooks).
- **Write defaults across controller swaps:** the defaults are captured from the pose at bind time. After a swap, bones
  outside the new controller's bound set keep the previous motion's last pose, as in the game. `Stop()` is the only way back
  to the rest pose.
- **Tests run in edit mode only:** play mode with `AutoStep` uses the same code path (`CharaMotion.LateUpdate` → the
  animation stage) but is not covered by a batch test.
