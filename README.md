# JERICHOFALL

A free, open-source biblical fantasy arena platform game for Linux and Windows.
Journey through eighteen checkpoints from Egypt to Canaan, play local Versus,
or join direct-connect online Versus with two to four players. David, Moses,
Samson and Joshua wield a sling, staff, jawbone and spear.

## Build and play

Requirements: .NET SDK 10, Python 3 and Git.

```sh
python3 tools/standalone/build.py
```

Linux: `bash tools/play.sh`. Windows: double-click `build/standalone/Play.cmd`.
Portable builds include .NET and the required native libraries.
See [build instructions](docs/BUILD.md) and [controls](docs/PLAYING.md).

## Source

- `src/Jerichofall.Core`: deterministic gameplay, campaigns, saves and networking.
- `src/Jerichofall`: FNA graphics, audio, input, menus and procedural artwork.
- `assets`: editable game data, maps, artwork, fonts and audio.
- `tests/Jerichofall.Checks`: gameplay, progression, networking and dependency checks.
- `tools/standalone`: bootstrap, build, packaging and validation.

## License

Code and documentation: [MIT](LICENSE). Listed assets: [CC BY 4.0](ASSET_LICENSE.md)
to the extent rights are held. Created with AI assistance. Third-party libraries
retain their licenses; FNA uses Ms-PL. See [licensing](docs/LICENSING.md).

## Alpha limitations

Online Versus uses unauthenticated, unencrypted TCP with trusted peers.
There is no relay, matchmaking, client prediction or online campaign.
Human balance, physical controllers, mixed-OS Internet play and high latency
still need testing. Executables are unsigned.
