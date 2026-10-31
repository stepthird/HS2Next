# Runtime/Motion/Face

The facial expression stage: blend-shape patterns of eyebrows, eyes and mouth, blinking, mouth width, tears, cheek and
eye highlights. The behaviour was measured in the running game; no game code is copied.

## Files

| File | Content |
| --- | --- |
| `FaceRig.cs` | Reads the head's `FaceBlendShape` fields once at attach, by reflection, so both receiving stubs (`Assets/Compat/IL`, `Assets/Compat/AssemblyCSharp`) work. The vanilla heads use the IL one. |
| `FbsControl.cs` | One control (eyebrow / eyes / mouth): pattern mixes, `ChangePtn` / `ChangeFace` with validation, the 0.15 s cross-fade timer, the per-frame weights. |
| `BlinkControl.cs` | Blink state machine and the injectable random source (`IFaceRandom`, `SystemFaceRandom(seed)`). |
| `MouthWidthJitter.cs` | Width timer and random targets; the stage applies the result to `cf_J_MouthMove`. |
| `FaceMaterials.cs` | Tears `_NamidaScale` (`o_namida`), cheek `_Texture4Scale` (face draw material, `o_head` slot 0), highlight `_Smoothness` (`o_eyebase_L/R`). |
| `ExpressionStage.cs` | `IMotionStage` (`MotionPhase.Face`, name `face`): blink, openness sources, eyebrow → eyes → mouth, width, eyelid correction. |
| `CharaExpression.cs` | Facade for the Studio and the UI: the face operations, the head-load sequence, Studio state and controls, lists and UI index mapping, tongue and mouth shape mask. Also `FaceStatus` and `ExpressionLists`. |
| `FaceSetup.cs` | Attach: builds the stage, applies the start state, registers it on `ctx.Motion` and runs one computation at once. |

Access from other code: `CharaExpression.Of(characterRoot)`, or `ctx.Motion.Get<ExpressionStage>().Api`.
Test: `Assets/HS2Next/Editor/ExpressionTest.cs` (`-executeMethod ExpressionTest.Run [-card <png>] [-male <png>] [-render]`).

## Behaviour

- Weights: value = Lerp(OpenMin, top, openness), where top is the corrected maximum when it is ≥ 0 and OpenMax otherwise.
  FixedRate ≥ 0 replaces the value. Then n = (int)(float)(value × 100), clamped to 0..100. Every Close / Open index of every
  pattern of a target is written every frame, and indices of other patterns get 0. The previous mix contributes × (1 − b)
  during a cross-fade, the current mix × b. The write order is eyebrow, eyes, mouth, so the mouth owns `o_head` index 0 (and
  `o_namida` index 0 where the mouth drives `o_namida`). Steps use preallocated arrays only (no allocation per frame,
  measured 0 bytes over 2000 steps).
- Pattern changes: `ChangePtn` ignores a change to the current single pattern. A blended change restarts the fade from the
  logical previous mix.
- Blink: closing takes BaseSpeed + U(0, 0.05) s, the shut hold lasts 1..3 frames (counted in frames), opening takes a fresh
  BaseSpeed + U(0, 0.05) s, and the idle wait is 0.2 × RandInt(0, F) s. The first computation after creation starts a
  blink. With `FixedFlags` ≠ 0 the state is frozen, not reset.
- Openness: the eyebrow follows the blink when `SyncBlink`, the eyes follow the blink, and the mouth follows `VoiceLevel`.
- Game quirks kept:
  - With blinking off, `EyesOpen = v` shows v² (the Studio applies blink before open).
  - `ChangeEyebrowOpenMax` with blinking off forces the eyebrow openness to 1.
  - With the mouth fixed, the last of OpenMax / OpenMin decides FixedRate.
  - Every mouth-pattern change overwrites the width-jitter flag (off for 9..16).
  - A disabled jitter keeps its last width.
  - The eyelid correction keeps a stale value while blinking is off.
- Studio:
  - "Add from card" ignores the card's status and uses the status defaults, applied through the head-load sequence and then
    the state update the Studio runs at the end of an add or a scene load.
  - In the Studio the mouth is fixed at the mouth-open slider value (default 0), except while a voice plays with lip sync
    on (FixedRate −1).
  - Eyebrow and eye UI numbers are 0-based list positions; ◀ / ▶ wrap and blend over 0.15 s.
  - The mouth UI number is the raw id (0..25), changed without blending. Patterns 10, 13, 17 and 18 switch on the mouth
    shape mask, and 10 and 13 switch the head tongue to the body tongue.
- Lists: read from the catalog (categories 503 / 505 / 507 female, 502 / 504 / 506 male, vanilla rows in ascending order).
  Built-in tables are the fallback.

## Switches and defaults

| Switch | Default | Meaning |
| --- | --- | --- |
| `FaceSetup.Enabled` | true | false = no stage; the assembler's static default expression stays |
| `FaceSetup.Mode` | `StudioDefaults` | `StudioCardStatus`: Studio semantics with the card's status (as a scene load would apply it). `GameCardStatus`: head-load sequence only, with the card's status (maker and game). |
| `FaceSetup.Seed` | null | fixed seed for new characters (tests); otherwise each character gets its own seed |
| `FaceSetup.EyelidCorrectionDefault`, `CharaExpression.EyelidCorrection` | false | eyelid correction from the eye-look stage outputs (`IEyeLookOutputs`, found at attach) |
| `ExpressionStage.ResetCorrectionWhenFixed` | false | HS2Next fix: drop the correction while blinking is off, instead of keeping it stale |
| `CharaExpression.VoiceLevel` | 0 | voice level 0..1. No voice source exists yet, so it is fed from outside. |

## Deviations (deliberate)

1. Out-of-range or negative pattern ids are rejected and logged (`face: ... rejected`). The status keeps the pattern the face
   shows. A Studio state with an invalid mouth id keeps the shown pattern in the status.
2. `Step` with dt 0 (pause) holds everything. The game would still perform the frame-counted blink transitions while paused.
3. The width is written to `cf_J_MouthMove` in the same step and only when it changes. The game writes it every frame with a
   one-frame lag. Writing on change leaves other writers of that bone (ABMX) alone while the width stays 1.
4. One computation runs synchronously at attach and does not advance time, so no neutral frame appears. The first `Step`
   then starts the blink, as in the game.
5. `IMotionStage.Reset` (`ResetAll`) has no game counterpart. It finishes the cross-fades, leaves the eyes open with a newly
   drawn idle wait, and restarts the width segment. Pattern and open values are kept.
6. Robustness only (never hit by vanilla data):
   - Table indices outside a mesh are not written.
   - Targets with fewer patterns than target 0 read as "no shape".
   - Targets without a renderer are skipped.
   - A blink sweep of length 0 does not divide by zero.
7. Tears are a continuous 0..1 value. There are no discrete levels, so a "tears 2" request becomes 1.
8. The `o_namida` material comes from the head bundle and is shared by all characters with that head; the assembler's
   per-character copy (`CharaContext.OwnedMaterials`) is used, and a copy is made at attach only when the material is not
   owned yet. The other materials are already per-character copies. Writes look up `sharedMaterial` each time, so they
   follow `ShaderMap.ApplyUrp` swaps. `RefreshMaterials()` writes the values again.

## Open issues

- No voice source yet. The game's voice level (RMS of 1024 samples × 3, slow-fall smoothing) is not implemented; lip sync is
  fed through `VoiceLevel` / `SetVoicePlaying`.
- `_NamidaScale` makes no clearly visible difference in the URP face render of the reference card; its meaning in the
  shader is not verified.
- The mouth shape mask runs `ctx.FaceShape.Apply()` again when it changes. ABMX bone modifiers are applied again by their
  own `LateUpdate`, but not in headless batch runs.
- Studio slider ranges are not verified; all setters clamp to 0..1.
- The eye-look sign convention for the eyelid correction (V > 0 = up) is taken from `LookOutputs.cs` (`AngleVRate`: +1 =
  up). It has no in-game reference, because the path is dead in the vanilla game.
