# PAYDAY: The Heist special NPCs

## Implemented

### Shield (`entities/npcs/shield.prefab`)

- Original character rig and dedicated crouched idle, locomotion and reload clips.
- Original `units/weapons/shield/shield` model and collision, attached to
  `a_weapon_left_front` as in `CopInventory`.
- A separate kinematic rigidbody preserves the shield's identity in weapon traces.
  Its damage receiver absorbs hits; the character remains vulnerable from uncovered angles.
- Death detaches the shield into a physical prop. The character also drops its gun.
- Uses the existing B9-S as a playable pistol substitute; PDTH's dedicated
  `shield_pistol_npc` weapon, its burst balance and squad formation are not yet imported.

### Bulldozer (`entities/npcs/bulldozer.prefab`)

- Original tank rig, idle/walk/run and shotgun reload clips; Reinbeck shotgun.
- 1,000 health: the source base 100 at the mount's 10x damage scale, without
  difficulty multipliers. Older ordinary NPC health values retain their sandbox tuning.
- Original `c_glass` and `c_helmet` collision bounds produce bone-attached sphere
  hitboxes. The visor accumulates 200 damage, matching the source glass endurance of 20
  at 10x scale. It swaps `g_glass` for `g_glass_hole` and plays the original
  `bulldozer_visor_shatter` sound.
- Intact visor hits retain ordinary damage. Subsequent hits through the visor use
  the source 2x headshot multiplier; helmet/body hits keep ordinary damage.
- Melee immunity follows `TankCopDamage`. Explosions still hurt the character.
- Visor state is host-synchronized, and its break sound is a host-only broadcast.
  Two-client special-unit replication has not yet been tested.

The exposed weak point currently retains the visor sphere instead of switching to the
smaller authored `c_head`. Explosion-driven visor endurance, glass particles, squad
leadership, source shotgun falloff tuning and special attack gestures remain unfinished.
Broken visor geometry on the resulting ragdoll has not been verified.

## Shared fix

All PAYDAY NPC prefabs now register their model hitboxes through `ModelHitboxes`.
Previously the models contained hitbox data but only the coarse living collider was active.

### Taser and Cloaker (`entities/npcs/taser.prefab`, `entities/npcs/cloaker.prefab`)

- Original character rigs, locomotion and weapon clips; AMCAR-4 and Compact-5.
- Taser winds up a ranged hold with original charge/loop sounds and a simple visible wire.
  Cloaker approaches through shared navigation and winds up a close-range strike using
  `special_std_kick_fwd`. Its 72-inch root-to-root strike reach accommodates shared NPC hulls;
  a 16-inch hold margin avoids cancelling on small physics shifts. Navigation rechecks reach
  directly rather than trusting arrival at a stale target position.
- Both use `BaseNpc.StartCarry` / owner-checked `StopCarry` to suspend grounded hostile
  NPCs through the engine's shared physics/brain control. Victims remain enabled and
  damageable, then resume their original AI after release.
- Host-controlled target, phase and strike clock; four-second hold, six-second retry.
  Taser retains a 0.6-second windup. Cloaker imports the kick's `anim_act_01` marker
  (0.9667 seconds) as its strike time and continues the full two-second clip through
  the hold's beginning. Interrupted attacks immediately leave the special animation.
  Cloaker deals 20 initial damage; both deal five damage per second while holding.
  These durations/damage values are sandbox adaptations, not original difficulty balance.
- Damage interrupts the attacker. Death, disable/destruction, loss of range/line of sight,
  victim death, timeout and another carrier taking ownership all end the attack.
  Cleanup cannot release another carrier's hold.
- Grounded NPC targets only; players still receive normal firearm attacks. Flying NPCs,
  scripted controllers and other custom brains outside the shared ground/carry contract
  are not established as compatible targets.

Player incapacitation/revival, authored victim reactions, Cloaker knockdown/ground attacks
and full Taser electrical particles remain unfinished. The generic forward-kick clip's
action marker supplies timing; the original `spooc_strike` redirect/complete takedown
sequence has not been reconstructed. Two-client replication remains unverified.

## Verification

- `VerifySpecialNpcs.csx`: native front/rear shield traces, absorption/flanking,
  physical drop, native visor hits, threshold and 2x exposed-head damage, melee and explosion rules.
- `VerifyNpcCrowd.csx`: 64 NPCs (24 PAYDAY combatants, 16 HL1 grunts, 16 HL2
  Combine, 8 PAYDAY civilians), 30 simulated seconds, damage across all three mounts
  and civilian reactions. Reports simulation tick timing; this is not a rendered-FPS benchmark.
- `PreviewSpecialNpcs.csx`: disposable-scene visual inspection.
- `InspectSpecialNpcs.csx`: original installed unit declarations, collision bodies,
  sequence data and animation paths.
- `VerifyNpcDisablers.csx`: Taser/Cloaker against both HL1 grunt and HL2 Combine;
  acquisition, no attacks while restrained, damage, interruption and resumed combat.
- `VerifyDisablerCleanup.csx`: 14 lifecycle cases covering timeout, attacker death,
  disable/destruction, obstruction, ownership transfer and victim death for both units.
- `PreviewDisablerNpcs.csx`: rendered attack previews in disposable scenes.
- `VerifyCloakerStrike.csx`: authored marker timing, single strike, complete follow-through,
  return to ordinary animation and pre-impact interruption; captures three kick frames.

Reference behavior: installed PDTH `.unit`, `.object`, `.sequence_manager` and animation
assets, plus the archived [PDTH Lua reference](https://github.com/DrNewbie/payday-the-heist-lua):
`lib/tweak_data/charactertweakdata.lua`,
`lib/units/enemies/cop/copinventory.lua`,
`lib/units/enemies/cop/copdamage.lua`,
`lib/units/enemies/tank/tankcopdamage.lua`,
`lib/units/enemies/cop/actions/upper_body/copactiontase.lua`,
`lib/units/enemies/spooc/actions/lower_body/actionspooc.lua`.
