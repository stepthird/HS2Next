# Runtime/Motion/Corrective

Joint correction (the Studio's 関節補正, "joint correction" toggles): the twist and `*_dam` bones computed from the final
pose by the game's correction table. The behaviour was measured in the running game; no game code is copied.

## Files

- `JointCorrectionTable.cs`: parser of the `cf_expression` TextAsset into rows in file order (LookAt part, Correct part,
  display name), the fixed row → category list (it overwrites the file's column 0, which only has an older 4-category
  layout), the category names, and the vanilla table embedded as `VanillaText` (test fixture and fallback). `Diff` compares
  two tables field by field.
- `JointCorrectionMath.cs`: the Euler conversion `Decompose` / `Compose` for the six rotation orders, the alternate Euler
  solution (for the wrap fix), the trace-only and the robust matrix → quaternion conversions. Allocation-free.
- `JointCorrectionStage.cs`: `IMotionStage` at `MotionPhase.Correct` (after FK / IK / neck / eye look, before the bust and
  hip chains and dynamic bones). Binding, category and row switches, the per-frame algorithm, flags.
- `CharaJointCorrection.cs`: facade for the Studio layer (category toggles, master switch, scene flags 8 / pose flags 4,
  `Refresh()`).
- `CorrectSetup.cs`: attaches the stage to every built character (called by `MotionSetup.Attach`).

## Behaviour

- Attach: table `list/expression.unity3d` / `cf_expression` through the catalog for females and males (the Studio
  initialises the same table for both); if the asset is missing or does not parse, the embedded vanilla table is used and
  the log says so. Bones resolve through `ctx.BodyBones` (the `cf_J_Root` subtree of `p_cf_anim`, first name in depth-first
  pre-order wins). One log line:
  `correct: joint correction cf_expression: rows 26 (18 euler, 8 lookAt), bound rows 26, driven bones 26, source bones N, unbound none; categories off`
  (`categories all on` with `InitialCategoriesOn`).
- All 8 categories start off (the Studio default): nothing touches the twist bones, which keep whatever the clip or prefab
  gives them. `CorrectSetup.InitialCategoriesOn = true` starts every new character with all 8 on instead: that is what the
  HS2US plugin's "Auto Joint Correction" (default "All") does when the Studio adds a character. The reference install has
  HS2US, so the HS2 comparison (`Editor/CompareTest.cs`) builds with it on: then the recorded twist bones match to < 0.02°
  (for example `cf_J_LegKnee_back_*` = `cf_J_LegLow01_*` including the T-pose's 3.5°, `cf_J_ArmUp01_dam_L` −0.5° under
  Idle_00 at t 0.5 instead of the clip's baked −52°); with it off the differences were up to 52°.
- Per step, with at least one row on: rows in table order 0..25; for each enabled row the LookAt part, then the Correct part.
  Correct / Euler: decompose the corrected bone's and the reference bone's local rotations in the row's order, replace each
  used axis by fraction × reference angle, test the sign against `Slerp(ref, identity, k)`, compose. Correct / Quaternion
  (unused by the data): component copy × (min + max) / 2, no renormalisation. LookAt: aim frame from the target position
  and the up bone's world axis, the data's axis assignment, trace-only quaternion, world rotation set, then the optional
  single-axis limit (sign test against the half-way slerp, clamp) in the local ZYX decomposition. Positions and scales are
  never written.
- Pose-driven, not time-driven: `Step` ignores dt and evaluates every step, also with `Step(0)` or a paused `CharaMotion`
  (the game runs the correction every frame regardless of time). `Reset()` does nothing (no history). `Refresh()` (stage or
  facade) evaluates once now, for pose edits outside the step loop.
- Persistence (facade): `SceneFlags()` / `ApplySceneFlags()` = the 8 flags a scene stores; `PoseFlags()` /
  `ApplyPoseFlags()` = categories 0..3 only (pose files leave 4..7 untouched); `NewPoseDefault()` = all true (a fresh pose
  object before it is filled from the character).
- `EnableCategory` sets every row of the category and remembers the category state; `EnableIndex` sets one row only.
  `Enabled` (stage) = the master switch.

## Flags (on `JointCorrectionStage`)

| Flag | Default | Effect |
| --- | --- | --- |
| `FixEulerWrap` | false (faithful) | The Correct part uses, of the two Euler solutions of each rotation, the one with the smaller sum of outer angles. Removes the upper-arm twist jump when the arm is lowered past 90° about Z (case E7 of the test: −19.8° at z = 85 and z = 95 instead of −19.8° / −99.0°). Identical to the game while the outer angles are within ±90° (cases E0–E6 unchanged). The LookAt limit decomposition is not affected. |
| `RobustLookAt` | false (faithful) | Largest-diagonal matrix → quaternion instead of the trace-only formula, and a degenerate aim (zero direction, or aim parallel to the up vector) leaves the bone untouched instead of writing the formula's arbitrary result. Cases E0–E6 unchanged. |
| `RestoreOnDisable` | true | HS2Next addition. When a row is switched off, its driven bone goes back to the value it had before the correction, unless something rewrote the bone since the last correction (exact compare with the last written value). In the game the Animator rewrites every corrected bone each frame, so "off" shows the clip value at once; without a clip HS2Next has no per-frame writer of these bones, and they would keep the last corrected value. With a writer that rewrites the bones each frame the flag changes nothing. false = keep the corrected value (the game with the Animator disabled). |

Reproduced quirks (default): the one-sided gimbal branch (+0.9999 only; the threshold applies to the asin argument, the sine
of the middle angle, also for the orders whose argument is a negated element), the Euler wrap, the trace-only LookAt
quaternion, unused Euler axes inherited from the current (clip) value, males using the female table, and the left / right
asymmetric LookAt axes of the data. The world rotation written by a LookAt part is normalised before it is set (no change
for the orthonormal frames of normal poses; only degenerate frames are affected).

## Not implemented, and why

- The H-scene and maker correction components on the `p_cf_anim` prefab (per-motion body-shape offsets and
  partner-following IK targets): the Studio switches them off when it adds a character, so in the Studio they do nothing,
  not even their end-of-frame restore. Their receiving stubs (`Assets/Compat`) stay inert data holders so that the prefab
  loads; all their offsets are zero anyway.
- Partner-following IK, if needed later, is a "target follows bone" constraint of HS2Next's own IK.
- Nothing is attached for the 18 `*_dam` bones without a row: they are clip-only, shape-engine or static bones.

## Tests

`Assets/HS2Next/Editor/JointCorrectionTest.cs` (`-executeMethod JointCorrectionTest.Run [-card <path>] [-maleCard <path>]`):
table parse and spot checks against the vanilla table (and the bundle table equal to the fallback), Euler round trips per
order, binding on the bare prefab and on a female and a male build (38 `*_dam` on `p_cf_anim`, 0 on the head), the worked
examples E0–E7 on the bare rest skeleton (tolerance 1e-3 per component), the fix flags, per-category isolation, restore on
disable, `Step(0)`, `ResetAll`, the facade, and zero allocation per step. Report: `Spike/out/motion/correct_report.txt`.

## Open issues

- In-game confirmation of cases E1 / E7 is still to do; the examples are checked against reference numbers computed from
  the table.
- The in-game captions of the 8 toggles are not known; `JointCorrectionTable.CategoryNames` are descriptive.
- Unity 6 `Quaternion.Slerp` is used for the sign test; the Unity 2018.4 behaviour for references with negative w is not
  verified.
- LookAt limit axes `RevX` / `RevY` / `RevZ` (not in the data) are treated as "decompose / compose without a clamp"; the
  game's handling is not known.
- When animation playback writes the clip's `*_dam` channels every frame, `RestoreOnDisable` changes nothing; keep it on
  so that pose-only scenes (no clip) still revert when a toggle is turned off.
