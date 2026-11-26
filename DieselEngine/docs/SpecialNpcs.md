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

## Remaining special units

Taser and Cloaker assets/animation references have been inspected, but their special
attacks are not exposed as working NPC prefabs yet. They need a clean cross-game
incapacitation contract, interruption/recovery, and appropriate presentation rather than
turning another game's NPC component off or destroying it to simulate a takedown.

## Verification

- `VerifySpecialNpcs.csx`: native front/rear shield traces, absorption/flanking,
  physical drop, native visor hits, threshold and 2x exposed-head damage, melee and explosion rules.
- `VerifyNpcCrowd.csx`: 64 NPCs (24 PAYDAY combatants, 16 HL1 grunts, 16 HL2
  Combine, 8 PAYDAY civilians), 30 simulated seconds, damage across all three mounts
  and civilian reactions. Reports simulation tick timing; this is not a rendered-FPS benchmark.
- `PreviewSpecialNpcs.csx`: disposable-scene visual inspection.
- `InspectSpecialNpcs.csx`: original installed unit declarations, collision bodies,
  sequence data and animation paths.

Reference behavior: installed PDTH `.unit`, `.object`, `.sequence_manager` and animation
assets, plus the archived [PDTH Lua reference](https://github.com/DrNewbie/payday-the-heist-lua):
`lib/tweak_data/charactertweakdata.lua`,
`lib/units/enemies/cop/copinventory.lua`,
`lib/units/enemies/cop/copdamage.lua`,
`lib/units/enemies/tank/tankcopdamage.lua`.
