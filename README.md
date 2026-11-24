# sbox-mounts
This is the home of the game mounts included with [s&box](https://sbox.game/).

Mounts let s&box load content from Steam games you own and have installed, converting their models, maps, materials, sounds, and more into s&box resources at runtime.
Players can then use that content in the Sandbox mode and other workshop games.

See the full [Mount API docs](https://sbox.game/dev/doc/game-mounts/) to learn more.

## Supported games

✅ supported · ⚠️ partial · ❌ not supported

| Game | Ident | Engine | Maps | Models | Animations | Materials | Textures | Sounds | Other |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Garry's Mod | `gmod` | Source | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | Particles, decals, soundscapes |
| Half-Life 2 (+ Episodes) | `hl2` | Source | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | Particles, decals, soundscapes, NPCs, weapons |
| Counter-Strike: Source | `css` | Source | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | Particles, decals, soundscapes, weapons |
| Half-Life | `hl1` | GoldSrc | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | Sprites, decals, music, NPCs, weapons |
| Half-Life: Opposing Force | `opfor` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Half-Life: Blue Shift | `bshift` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Counter-Strike | `cstrike` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Counter-Strike: Condition Zero | `czero` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Ricochet | `ricochet` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Team Fortress Classic | `tfc` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Deathmatch Classic | `dmc` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Day of Defeat | `dod` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Sven Co-op | `svencoop` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Cry of Fear | `cof` | GoldSrc | ✅ | ✅ | ⚠️ | ✅ | ✅ | ✅ | Sprites, decals, music |
| Quake | `quake` | Quake | ✅ | ⚠️ | ⚠️ | ⚠️ | ✅ | ✅ | Skyboxes |
| Quake III Arena | `quake3` | id Tech 3 | ✅ | ⚠️ | ❌ | ✅ | ✅ | ✅ | Skyboxes, player models |
| Return to Castle Wolfenstein | `rtcw` | id Tech 3 | ✅ | ⚠️ | ❌ | ✅ | ✅ | ✅ | Skyboxes, 3D skyboxes |
| Wolfenstein: Enemy Territory | `wolfet` | id Tech 3 | ✅ | ⚠️ | ❌ | ✅ | ✅ | ✅ | Skyboxes, 3D skyboxes |
| DOOM + DOOM II | `doom` | Doom | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | Sprites, thing prefabs, skies |
| Serious Sam HD: The First Encounter | `ss_hd_tfe` | Serious Engine 3 | ✅ | ✅ | ❌ | ⚠️ | ✅ | ✅ | |
| Serious Sam HD: The Second Encounter | `ss_hd_tse` | Serious Engine 3 | ✅ | ✅ | ❌ | ⚠️ | ✅ | ✅ | |
| Natural Selection 2 | `ns2` | Spark | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | Skyboxes |
| Halo 2 (MCC) | `halo2` | Blam | ❌ | ✅ | ❌ | ✅ | ✅ | ❌ | Model collision and ragdolls |

Notes:

- **GoldSrc animations** — every game's `.mdl` models import with their sequences, but only Half-Life wires
  them up to playable NPCs and weapons.
- **Quake** — `.mdl` models import at their first frame and first skin; only rerelease `.md5mesh` models are
  animated. Materials are built from BSP textures when a map loads, not mounted separately.
- **id Tech 3** — `.md3`/`.mdc`/`.mds` models import at their first frame (player models are assembled in an
  idle pose).
- **DOOM** — DOOM has no 3D models; things are billboard sprites. Covers `doom.wad`, `doom2.wad`, `plutonia.wad`,
  `tnt.wad`, `nerve.wad`, `sigil.wad` and `sigil2.wad`.
- **Serious Engine 3** — materials are built into each model rather than mounted as standalone resources;
  models import without animations.
- **Natural Selection 2** — `.level` maps load as scenes with world geometry, props, lights, collision and authored skyboxes.
- **Not yet enabled** — mounts for Heretic + Hexen, Call of Duty (2003), Jedi Outcast and Jedi Academy exist in
  the code but are commented out.

## Development
You will need a copy of s&box to develop mounts. You can get it on [Steam](https://store.steampowered.com/app/590830/sbox/), or by building [from source](https://github.com/Facepunch/sbox-public).

The mounts in this repo are grouped by engine into projects. Open an `.sbproj` in the s&box editor to start working.

#### Testing in the client
To test changes in the game client, run `BuildMounts.bat` to build and package the contents of this repo.

The output will be copied to `game/mounts/` in your s&box installation directory, and the mounts will be available in your client. This is useful for testing the real user experience, like the mounting flow, or map selection.

## Contributing
Anyone can contribute. Pull requests adding support for new engines and games, improving import accuracy, or fixing bugs are welcome!

Please follow the s&box contribution and style [guidelines](https://github.com/Facepunch/sbox-public/blob/master/CONTRIBUTING.md), and review your own changes before opening a PR.

For help or to show off what you're working on, join the discussion on the s&box [discord](https://discord.gg/sbox) or on the [forums](https://sbox.game/f).