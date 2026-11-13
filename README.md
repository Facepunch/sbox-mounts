# sbox-mounts
This is the home of the game mounts included with [s&box](https://sbox.game/).

Mounts let s&box load content from Steam games you own and have installed, converting their models, maps, materials, sounds, and more into s&box resources at runtime.
Players can then use that content in the Sandbox mode and other workshop games.

See the full [Mount API docs](https://sbox.game/dev/doc/game-mounts/) to learn more.

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