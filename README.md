# sbox-mounts
This is the home of the game mounts included with [s&box](https://sbox.game/).

Mounts let s&box load content from Steam games you own and have installed, converting their models, maps, materials, sounds, and more into s&box resources at runtime.
Players can then use that content in the Sandbox mode and other workshop games.

See the full [Mount API docs](https://sbox.game/dev/doc/game-mounts/) to learn more.

## Supported games

✅ supported · ⚠️ partial · ❌ not supported · ➖ not applicable

| Game | Ident | Engine | Maps | NPCs | Weapons | Models | Anims | Sounds | Materials |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [Garry's Mod](https://store.steampowered.com/app/4000) | `gmod` | Source | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Half-Life&nbsp;2 (+&nbsp;Episodes)](https://store.steampowered.com/app/220) | `hl2` | Source | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Half-Life](https://store.steampowered.com/app/70) | `hl1` | GoldSrc | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Counter-Strike](https://store.steampowered.com/app/10) | `cstrike` | GoldSrc | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Counter-Strike: Condition&nbsp;Zero](https://store.steampowered.com/app/80) | `czero` | GoldSrc | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Counter-Strike: Source](https://store.steampowered.com/app/240) | `css` | Source | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Day of Defeat](https://store.steampowered.com/app/30) | `dod` | GoldSrc | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Day of Defeat: Source](https://store.steampowered.com/app/300) | `dods` | Source | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Deathmatch Classic](https://store.steampowered.com/app/40) | `dmc` | GoldSrc | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Cry of Fear](https://store.steampowered.com/app/223710) | `cof` | GoldSrc | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [DOOM + DOOM II](https://store.steampowered.com/app/2280) | `doom` | Doom | ✅ | ❌ | ❌ | ➖ | ✅ | ✅ | ✅ |
| [Half-Life: Blue&nbsp;Shift](https://store.steampowered.com/app/130) | `bshift` | GoldSrc | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [Half-Life: Opposing&nbsp;Force](https://store.steampowered.com/app/50) | `opfor` | GoldSrc | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [Natural Selection 2](https://store.steampowered.com/app/4920) | `ns2` | Spark | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [Overgrowth](https://store.steampowered.com/app/25000) | `overgrowth` | Phoenix | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| [Ricochet](https://store.steampowered.com/app/60) | `ricochet` | GoldSrc | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [Sven Co-op](https://store.steampowered.com/app/225840) | `svencoop` | GoldSrc | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [Team Fortress Classic](https://store.steampowered.com/app/20) | `tfc` | GoldSrc | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [Team Fortress 2](https://store.steampowered.com/app/440) | `tf2` | Source | ✅ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ |
| [Serious Sam HD: The First Encounter](https://store.steampowered.com/app/41050) | `ss_hd_tfe` | Serious Engine 3 | ✅ | ❌ | ❌ | ✅ | ❌ | ✅ | ✅ |
| [Serious Sam HD: The Second Encounter](https://store.steampowered.com/app/41014) | `ss_hd_tse` | Serious Engine 3 | ✅ | ❌ | ❌ | ✅ | ❌ | ✅ | ✅ |
| [Quake](https://store.steampowered.com/app/2310) | `quake` | Quake | ⚠️ | ❌ | ❌ | ✅ | ⚠️ | ✅ | ✅ |
| [Quake III Arena](https://store.steampowered.com/app/2200) | `quake3` | idTech 3 | ✅ | ❌ | ❌ | ⚠️ | ❌ | ✅ | ✅ |
| [Return to Castle Wolfenstein](https://store.steampowered.com/app/9010) | `rtcw` | idTech 3 | ✅ | ❌ | ❌ | ⚠️ | ❌ | ✅ | ✅ |
| [Wolfenstein: Enemy&nbsp;Territory](https://store.steampowered.com/app/1873030) | `wolfet` | idTech 3 | ✅ | ❌ | ❌ | ⚠️ | ❌ | ✅ | ✅ |
| [Magicka](https://store.steampowered.com/app/42910) | `magicka` | XNA | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |


## Development
You will need a copy of s&box to develop mounts. You can get it on [Steam](https://store.steampowered.com/app/590830/sbox/) and switch to the [staging branch](https://sbox.game/dev/doc/getting-started/staging-branch), or  [build it from source](https://github.com/Facepunch/sbox-public).

The mounts in this repo are grouped by engine into projects. Open an `.sbproj` in the s&box editor to start working.

#### Testing in the client
To test changes in the game client, run `BuildMounts.bat` to build and package the contents of this repo.

The output will be copied to `game/mounts/` in your s&box installation directory, and the mounts will be available in your client. This is useful for testing the real user experience, like the mounting flow, or map selection.

## Contributing
Anyone can contribute. Pull requests adding support for new engines and games, improving import accuracy, or fixing bugs are welcome!

Please follow the s&box contribution and style [guidelines](https://github.com/Facepunch/sbox-public/blob/master/CONTRIBUTING.md), and review your own changes before opening a PR.

For help or to show off what you're working on, join the discussion on the s&box [discord](https://discord.gg/sbox) or on the [forums](https://sbox.game/f).