# Runtime/Motion/DynamicBones

Secondary motion of a character, implemented from measured behaviour: the ordinary `DynamicBone` chains (hair, clothes,
accessories) and the four `DynamicBone_Ver02` bust and hip chains. The receiving stubs (`DynamicBone`, `DynamicBone_Ver02`,
`DynamicBoneCollider`, `DynamicBonePlaneCollider`) only hold data. Everything here reads their fields and does not rely on
them doing anything.

## Files

| File | Content |
| --- | --- |
| `DynamicBoneOptions.cs` | Switches (`DynamicBoneOptions`, `BustOptions`, `ChainVariant`) and their defaults |
| `DynamicBoneColliders.cs` | Collider shapes from the data holders, the collision responses, the three evaluation paths (job / main thread / Ver02), `ColliderTable` (evaluated once per step and shared by all chains) |
| `DynamicBoneChain.cs` | One `DynamicBone`: chain construction (the IllusionFixes or the vanilla variant), parameters, the frame, the step, the write-back, the lifecycle (enable / disable / weight / distance disable), rebuild |
| `BustChain.cs` | One `DynamicBone_Ver02`: the presets, initialisation of the transforms and rest positions, pattern selection, reset of positions and particles, weight, gravity and softness parameters, the 60 Hz accumulator with its loop cap, the step, the write-back with the aim lever and crush |
| `DynamicBoneStage.cs` | `IMotionStage` at `MotionPhase.DynamicBone`: all ordinary chains, time mode, the fixed-step tick, the Studio switches, `Reset`, `Rebuild` |
| `BustStage.cs` | `IMotionStage` at `MotionPhase.BustPhysics`: the Ver02 chains, card softness and weight, `ResetPosition`, the Studio switches, the male path |
| `DynamicBoneSetup.cs` | `Attach(ctx)` (called by `MotionSetup`): builds both stages, assigns colliders (see below), writes one log line per character |

Test: `Assets/HS2Next/Editor/DynamicBoneTest.cs` (`-executeMethod DynamicBoneTest.Run [-card <png>] [-male <png> | -nomale] [-frames N]`,
report `Spike/out/motion/dynbone_report.txt`).

## What Attach does

1. **Bust stage.** One `BustChain` per `DynamicBone_Ver02` under `p_cf_anim`, looked up by `Comment` (`Mune_L/R` → 0/1,
   `Siri_L/R` → 2/3). The hips take their rest offsets (`LocalPosition`) from the `p_cf_anim` prefab, which is the bind pose
   at load, because the game never sets the hips up again. The card values `bustSoftness`, `bustWeight` and
   `shapeValueBody[1]` are read, then `ResetPosition()` runs once (shapes were applied before attach) and softness and
   weight are applied.
2. **Parts.**
   - Clothes top and bottom (`copyDynamicBone`): the assembler has already set `m_Root`, `m_Exclusions[i]` and
     `m_notRolls[i]` to the body bone with the same name when it loaded the part, before the rebinding destroyed the part's
     own skeleton copy (count in `CharaContext.DynamicBonesRepointed`). The built character is correct without motion
     stages.
   - Every component gets `m_Colliders` = the `DynamicBoneCollider`s under `cf_J_Root` in hierarchy order, disabled ones
     included. Colliders inside an accessory object are given only to accessories in later slots, which reproduces the
     game's load-time snapshot.
3. **Chains.** One chain per component, in this order: hair kinds 0..3 (with bundle index and the card's
   `dictBundle[k].noShake`), clothes kinds 0..7 (top / bottom chains whose root subtree holds `cf_J_Legsk_*` get `IsSkirt`),
   accessory slots (`noShake`, and the DynamicBoneEditor `Weight` as the chain weight), then any other component under the
   character root.

The log line looks like this: `dynbone: 13 chains (hair 9, clothes 2 incl. skirt 2, accessories 2, other 0),
35 particles (11 tips), body colliders 21 (0 off), noShake 0, re-pointed 2, ...; bust: 4 Ver02, softness ...`.

## Frame emulation

- In the game a `DynamicBone` resets its bones before the Animator runs. HS2Next has no such hook, so at the start of each
  frame a chain bone gets its start pose back when two conditions hold: the last write-back changed it, and nobody wrote it
  since (the local pose is bit-identical to what was written). A bone that the Animator, FK or another stage wrote keeps that
  pose. This is what the game sees after its Animator pass. For `m_UpdateMode` 1 (AnimatePhysics) the reset happens only on
  frames with a fixed tick (50 Hz accumulator).
- `DynamicBone_Ver02` copies the reference bones' local pose in its own step, as in the game.

## Switches and defaults

| Switch | Default |
| --- | --- |
| `DynamicBoneOptions.FrameLocked` | false: fixed `m_UpdateRate` (60 Hz), at most `MaxStepsPerFrame` = 3 steps per frame (true = HS2 parity, one step per frame) |
| `DynamicBoneOptions.Variant` | `IllusionFixes` (the chain construction of that community fix; `Vanilla` is the game's) |
| `DynamicBoneOptions.IntendedBehaviour` | false (the game's job path: no FreezeAxis, no plane colliders, disabled colliders are zero records) |
| `DynamicBoneOptions.LegacySphereBug` | true (job-path spheres centred at R·S·m_Center from the origin, as in the game) |
| `DynamicBoneOptions.FaithfulAnimatePhysics` | true |
| `DynamicBoneOptions.DistantDisable` | true (reference null → `Camera.main`, no camera → no check) |
| `DynamicBoneOptions.LocalWriteBack` | true: chains whose bones are direct parent / child compose world matrices from local poses and write local poses (no O(depth) world access); equal to the world-space path within float rounding (test: 3e-5 units after 240 frames) |
| `DynamicBoneOptions.ColliderBroadPhase` | true: per chain and step, colliders that no particle can reach are skipped (bit-identical results; a particle pushed out of the bound ball is redone with the full list) |
| `BustOptions.HoldOnSkipFrames` | true (the IllusionFixes hold; false = the game's skip update) |
| `BustOptions.MaleBustPhysics` | true (chains run on males, only the 4 `dbcBust` colliders are off) |
| `BustOptions.HipRestFromBindPose` | true |
| `BustOptions.ApplyCardValues` | true |

Global defaults for new characters live in `DynamicBoneSetup.Options` and `DynamicBoneSetup.Bust`, and
`DynamicBoneSetup.Enabled` turns the setup off. Each stage holds its own copy.

Studio switches:

- `DynamicBoneStage.HairFkActive` and `SkirtFkActive` (FK groups Hair / Skirt).
- `SetAccessoryShake(slot, on)` and `SetHairBundleShake(kind, bundle, on)`.
- Per chain: `Chains[i].Enabled`.
- Extra switches that the game does not have: `ClothesEnabled`, `AccessoriesEnabled`, `OtherEnabled`.
- `BustStage.FkBreastActive`, `AnimeBustL/R`, `HipL/R`.
- `DynamicBoneChain.Override` + `ApplyOverride()`, `BustChain.SetOverride`: the scene values of advanced pose, read in front of the component's fields and the live values; nothing writes those.

All switches are applied at the start of each step with enable / disable semantics: disabling restores the start pose (Ver02:
the reference pose), and enabling resets the particles to the bones, so enabling again never jumps.

API notes:

- `Reset()` on either stage (`CharaMotion.ResetAll`) drops all velocity.
- `DynamicBoneStage.Rebuild()` reads the fields again after DynamicBoneEditor or Studio edits (field writes alone do
  nothing), drops destroyed components and adds the components of parts loaded since.
- `BustStage.ResetPosition()` must be called after body-shape changes. `SetCardValues(softness, weight, size)` sets the card
  values.

## Deviations and choices

- **Zero-step frames in fixed-rate mode** (and paused frames, `Dt = 0`) run the asset's skip update: object motion is carried
  with inertia, the root follows its bone, then the stiffness cone and the hard length are applied. The game has this
  routine only for Ver02; using it here keeps chains attached to moving or animated bones on display frames between two
  60 Hz steps. In frame-locked mode a paused frame does the same, which differs from HS2: there hair keeps simulating at
  `timeScale` 0, but paused stages must hold. The test measures `Step(0)` as a 1e-7 change.
- **Order relative to the other stages.** `MotionPhase.BustPhysics` (750) runs after the joint correction (700) and the FK
  phase (400), not in the game's slot before IK / FK. Consequence: FK / IK posing of the chest can make the bust jiggle
  through the root's motion, which the game avoids. The FK Breast group still turns the breast chains off.
- **Skirt detection** for the Skirt FK group uses the `cf_J_Legsk_*` name prefix instead of the Studio's FK bone table group
  13.
- **DynamicBoneEditor weight** is applied as the chain weight from the decoded card data (`ctx.Ext.DynamicBoneEditor`), not
  from `m_BlendWeight`: bundles of the older layout deserialise that field as 0.
- **Gravity and force** of `DynamicBone` are read from the fields every frame, as the asset does. All other parameters are
  copied at build.
- **Parent child count** for the rotation write-back (`childCount ≤ 1`) is cached when the chain is built. Call `Rebuild()`
  after parenting objects onto chain bones.

## Performance (Editor, Mono)

- Real characters: the first 60 female cards of the reference install have 619 chains and 1,614 particles, at most 81 per
  character. Bust plus dynamic bones cost 0.05 ms per step on average and 0.12 ms at most.
- Stress set (8 × back hair id 0 = 288 particles in 80 chains, 21 colliders): 0.29–0.32 ms per step, of which 0.21 ms is
  transform access.
- No managed allocation per step.

The remaining cost is transform access: per particle about 7 local get / set calls, plus 4 world-space calls per chain. If
more particles must fit the budget, the next step is a `TransformAccessArray` job (the step math is already scalar and
allocation-free).

## Open issues

- The collider assignment is done at attach; parts loaded after attach get their colliders through
  `DynamicBoneStage.Rebuild`.
- Studio items: the stage can host item chains through `Add(component, Other, colliderList: null)` (the item's own
  `m_Colliders`); item motion uses its own `CharaMotion` per item.
- The Studio's H-animation shake table is not implemented. The flags exist on `BustStage`.
- The maker-only bust inertia change is not implemented, by design.
- Plane colliders and FreezeAxis only work with `IntendedBehaviour` and are not covered by the test.
