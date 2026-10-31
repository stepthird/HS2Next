# Runtime/Motion/Look

Neck look and eye look of a character, implemented from measured behaviour. Both are `IMotionStage`s stepped by the
character's `CharaMotion` (phases `NeckLook` = 300 and `EyeLook` = 500: after IK, around FK, before the expression and the
corrections).

## Files

| File | Content |
| --- | --- |
| `LookSetup.cs` | Finds the data holders (`NeckLookControllerVer2` / `NeckLookCalcVer2` under `p_cf_anim`, `EyeLookController` / `EyeLookCalc` on `p_cf_head_bone`), reads their fields by reflection (works for the IL and the Assembly-CSharp receiving stubs), creates the stages, the target objects and the `CharaLook` facade, applies the Studio defaults, registers the stages, logs one `look:` line. |
| `NeckLookStage.cs` | Controller + calculation: type change and TARGET restore, the 1 s type blend with its curve, ANIMATION / FORWARD / FIX / CONTROL, TARGET / AWAY (limit-break hysteresis, aim fixed-point iteration, behind-below guard, AWAY side switching), head-first distribution, per-bone rotation from the reference objects, controller `rate` (X / Y only), `skipCalc` (`ForceStep`), scene save and load of the fixed rotations. |
| `EyeLookStage.cs` | Initialisation from the prefab rest pose, early exits, near clamp, field test (forced FORWARD), FORWARD / CONTROL targets, per-eye raw angles, threshold and bending (0.4 × / raw − 10), per-eye clamps, AWAY, smoothing, up-vector smoothing, write-back; `IEyeLookOutputs` (angles and rates for the face); save / load / set of the angles. |
| `LookTargets.cs` | `N_NeckLookTargetP` / `N_NeckLookTarget` (under `cf_J_Spine03`), `N_EyesLookTargetP` / `N_EyesLookTarget` (under `cf_J_Head_s`), `ChangeLookEyesTarget` / `ChangeLookNeckTarget` (types 0..9 with their parameters), the status look fields (`LookStatus`), the camera target, the Studio's movable "Look At Target". |
| `CharaLook.cs` | Facade component on the character root (`CharaLook.Of(root)`): raw patterns and Studio button mappings, eye pattern 4 semantics, targets, rate, switches, read-out, FK neck-group hook, scene persistence, `ForceUpdate`. |
| `LookMath.cs` | `angle`, `project`, `signedAround`, `sgn`. |

## Usage (Studio and UI layer)

```csharp
var look = CharaLook.Of(charaRoot);
look.SetNeckButton(1);            // neck panel: buttons {0,1,2,3} -> patterns {0,1,3,4}
look.SetEyeButton(4);             // eye panel: button = pattern; 4 = look at "Look At Target" (guide shown)
look.Targets.LookAtGuideChanged += visible => guide.SetActive(visible);
look.Targets.MoveLookAtTarget(worldPos);
look.SetFkNeckGroupActive(true);  // FK layer: neck -> FIX (4) and back
LookTargets.DefaultCamera = studioCamera.transform;   // target type 0 (else Camera.main)
```

Raw patterns: `SetNeckPattern(ptn, rate)`, `SetEyePatternRaw(ptn)` (the character's own eye pattern), `SetEyePattern(ptn,
force)` (the Studio's eye pattern, with pattern 4's guide), `Targets.ChangeLookEyesTarget(type, trf, rate, rotDeg, range,
dis)`, `Targets.ChangeLookNeckTarget(...)`. Scene state: `SaveNeck` / `LoadNeck` (pattern + per-bone fixed angle),
`SaveEyes` / `LoadEyes`.

## Switches

| Switch | Default | Effect |
| --- | --- | --- |
| `LookSetup.Enabled` | true | Attach nothing when false. |
| `LookSetup.StudioDefaults` | true | The Studio's "add from card": neck pattern 3 (ANIMATION), eye pattern 0 (FORWARD). false: the game's load defaults (neck 0, eye 0). |
| `EyeLookStage.GlobalEnabled` / `CharaLook.EyeLookGlobalEnabled` | true | The game's global eye switch (all characters). |
| `EyeLookStage.DefaultEnabled` | true | Initial per-character `LookEnabled` (`CharaLook.EyeLookEnabled`). |
| `CharaLook.NeckLookEnabled` | true (data) | The neck calculation's enable flag. |
| `NeckLookStage.LatchInputPose` | true | Input-pose latch (below). |
| `LookTargets.DefaultCamera` | null | Camera of target type 0; null = `Camera.main`. |

## Deviations from HS2 (deliberate)

1. **Input-pose latch (neck).** HS2's Animator rewrites the neck and head every frame, so the neck look always starts from
   the animated pose. HS2Next characters may have no pose writer (Animator disabled). The neck stage keeps what it wrote;
   when the bones still hold exactly that at the next step, it restores the input pose it saw before (rest pose, last pose
   tool or FK write); any other value is taken as the new input. Without this, ANIMATION would never return from a look pose
   and AWAY would iterate from its own output.
2. **Pause.** `dt = 0` holds the last output for every neck type. HS2 shows the animated pose there for the non-TARGET types.
   `skipCalc` still runs at dt = 0.
3. **Initial state.** All neck backups and the eye fixed angle start at the rest local rotations, not at zero quaternions;
   eye references are taken from the `p_cf_head_bone` prefab (asset) rest pose, falling back to the live pose when the
   prefab cannot be found (logged).
4. **Pattern numbers** are clamped to the valid range for both stages. A type-change blend time ≤ 0 blends at once.
5. **The eye global switch** is per character plus a static global.
6. **Camera target** is resolved every step (`DefaultCamera`, else `Camera.main`) instead of being captured once; no camera =
   no target (eyes idle, neck reuses the last target position).
7. **`angle()`** uses atan2 instead of acos: exactly 0 for parallel vectors, which decides AWAY's side for a centred target
   without float noise.
8. The Studio adds a character with neck pattern 3 directly (no frame of pattern 0 first); with the rest pose as the
   animation this is visually identical.

Kept as in HS2 (quirks): the rate blends X / Y only; with no target the limit-break test uses the world origin while the
angles use the last real target; AWAY's eye vertical is negated after the clamp; the CONTROL control-bone activation quirk;
eye smoothing and per-bone neck smoothing depend on the frame rate (`lerp(dt * speed)`).

## Tests

`Editor/LookTest.cs` (`-executeMethod LookTest.Run [-card <png>] [-render]`): data, worked examples on a bare unshaped rig,
behaviours, the visual target cards (one female, one male), optional face renders. Report `Spike/out/motion/look_report.txt`.

Rendering several poses in one Editor batch frame: Unity reuses the frame's skinning for renderers whose root bone did not
move (the eye meshes are rooted at `cf_J_FaceUp_ty`), so eye-only changes do not show up between `Camera.Render` calls.
Set `SkinnedMeshRenderer.forceMatrixRecalculationPerRender = true` before such captures (LookTest does; play mode is
unaffected).

## Open issues

- The limit-break angles h and v are measured on projections, so a target behind the chest's frontal plane has v near 180
  unless it is far above or below. A level camera orbit therefore breaks just past 90° (measured 91° at head height, 20
  units away), not at 100°; the 100 / 90 hysteresis only shows when v stays below 100 (target well above or below). LookTest
  checks both sweeps.
- Still open: the in-game button captions, the zero-quaternion behaviour, the worked examples against the game, the direction
  of target types 1..8 and the main game's `p_cf_anim`; the test prints the target-type 3 offset direction.
- HS2 comparison: HS2Next reads the shared `EyeLookCalc` on `p_cf_head_bone` (`chara/oo_base`; no vanilla female head and no
  zipmod override of `oo_base` carries another one). The reference install's Studio shows other type states (for example
  pattern 4 ±25 instead of ±13, `forntTagDis` 500, `nearDis` 20 / 10): the EyeLookAdjuster plugin rewrites them from its own
  configuration when a character loads, and switching its controller off later does not restore them.
  `Editor/CompareTest.cs` copies the recorded states into the eye stage (and lists the changed fields); with them the female
  eyes reach −25° as in the game. The defaults stay the bundle data.
- The eye material and highlight-sway components of the head are not implemented (unused in HS2 data).
- The expression stage reads `IEyeLookOutputs` from the eye stage; with the phase order it sees the same frame (no one-frame
  lag).
- The Studio guide gizmo, the `scaleRate` slider and the scene serialisation belong to the Studio layer.
