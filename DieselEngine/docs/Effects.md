# Original effects and destruction debris

## Resource library

The mount indexes all **209** shipped `.effect` definitions as
`mount://payday/<original path>.pdeffect`. `PaydayEffectResource` retains the original XML,
texture dependency paths and unsupported stack-operator names. All **40** distinct texture
dependencies resolve to original installed DDS assets; no game data is copied into the repository.

The 205 definitions containing billboards also have `entities/<original effect path>.prefab` resources,
under **PAYDAY Effects**. For example:

`mount://payday/entities/effects/particles/window/bank_window_vault.prefab`

The `PaydayEffect` component can also play an original effect path directly. It creates local,
cosmetic sprites using original texture atlas cells. Finite effects self-delete; disabling or
destroying the component cleans up its sprites. Default budget: 512 rendered billboards per
effect, 2,048 per scene. Excess particles are omitted in authored atom order.

## Playback coverage

Implemented common paths:

- Box position/velocity/size/age/angle initialization, aspect-size randomization and random atlas cells.
- Velocity integration, scaled-velocity curves, constant acceleration and angular velocity.
- Particle-age versus atom-time curves; size, color, opacity, rotation and animated atlas frames.
- Camera-facing sprites, normal/additive blending, centimetre-to-inch and axis conversion.

This is a **partial Diesel particle interpreter**, not complete native effect reproduction.
Lights, trails, model visualizers, collision/teleport respawning, child-effect events, wind,
non-camera billboard orientations, authored emitter sounds, preroll and some channel/input
variants remain unsupported. Infinite effects currently retain their initial particles; their
native respawn operators are not implemented. Atlas stepping currently follows +X then +Y.
`UnsupportedOperators` reports unknown stack nodes, not every unsupported attribute variant.
Definitions without billboards remain available as data resources but are not advertised as
playable effect prefabs.

## Asset sequence integration

`PaydayUnitState` runs immediate literal `effect` and eligible `spawn_unit` actions, following
literal `run_sequence` calls with cycle/depth/action limits. It resolves named `object(...)`,
`object_pos(...)`, `object_rot(...)` anchors from the original model's rest pose relative to
the unit's orientation object. Literal `v(...)` offsets are supported.

Effects are spawned before geometry rebuild, independently of the unit's enabled state, so
shattering a unit does not hide its burst. Existing live map-state updates play new effects;
silent state restoration does not replay historical bursts or debris.

Conditional/expression-valued actions, delayed actions, animated attachment tracking,
named stop/fade operations and persistent-effect restoration on join are not implemented.
Door-specific components still use their existing interaction path.

## Physical debris

Only bodies explicitly authored with `template="debris"` are spawned. Each body gets its own
original graphics/collision subset, dynamic `ModelCollider` and `Rigidbody`; this is not one
combined prop or invented replacement geometry. Authored collision-object mass is retained
when present; absent mass defaults to one kilogram per collision object.

`spawn_unit` actions instantiate these bodies on the host. Each body model has a stable mounted
resource path for networking. Fragments are network-spawned when a session is active, capped
at 128 per scene, and removed after 15 seconds by default. Gravity/collisions move them;
source scripted impulses, constraints and recursive debris sequences remain unfinished.
Two-client debris/effect verification is pending.

Eight units containing authored debris bodies also have independent emitter prefabs under
**PAYDAY Debris**, e.g.:

`mount://payday/entities/debris/units/world/architecture/secret_stash/props/planks_debris.prefab`

## Verification

- `EffectVerification.cs`: all 209 definitions, 40 DDS dependencies, curve interpolation,
  invalid-number rejection, sequence attachments and five distinct plank bodies; full offline
  regression passes.
- `VerifyEffects.csx`: window and security-camera particle playback, texture presence,
  render captures, particle budget and disable cleanup.
- `VerifySequenceEffects.csx`: bank-window burst and plank destruction with five independently
  moving bodies, silent state restoration and debris lifetime cleanup.
- `VerifyEffectPrefabs.csx`: definition/prefab catalog loading and independent spawning.

All 209 data resources, 205 effect prefabs and eight debris prefabs pass editor and installed
standalone-client loading. Independent spawning, rendered playback, sequence integration,
physical fragment motion, silent restoration and cleanup pass in the standalone client.
The tests warm assets and sample short bursts during playback because the live client
continues advancing scenes between awaited script ticks. See the main README for the
installed package hash. Multiplayer and full-map stress testing remain pending.
