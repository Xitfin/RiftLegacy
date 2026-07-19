# Rift Legacy

**Relive the Rift.**

Rift Legacy is a free and open-source Windows application built on LTK Manager for League of Legends PBE. It restores legacy champion models, Classic skins, splash arts, HUD portraits and a Season 1-inspired loading screen for the Jade experience.

## Main features

- Small native launcher with automatic GitHub updates.
- Automatic PBE client detection.
- Local Jade profile: Riot ID, rank, LP, wins, losses and win rate.
- Automatic current-game detection with elapsed game time.
- Blue Side and Red Side rosters with champions and Jade ranked statistics.
- One-click loading and reloading of the bundled `.fantome` packages.
- Per-champion settings and an independent classic loading-screen setting.
- Automatic LTK session cleanup when Rift Legacy closes.

## Installation

1. Download `RiftLegacyLauncher.exe` from the latest release.
2. Place it in a writable folder and run it.
3. The launcher creates `Rift Legacy`, verifies and installs the latest package, then starts the client.
4. Keep using the launcher: it checks for updates on every start and launches immediately when no update is available.

## Usage

1. Start Rift Legacy through `RiftLegacyLauncher.exe`.
2. Select the League of Legends PBE directory on first launch if requested.
3. Keep the PBE client open so Rift Legacy can display your profile.
4. Configure the enabled champions with `SETTINGS`.
5. Click `LOAD` and wait for `READY TO PLAY`.
6. Rift Legacy detects games automatically and displays both teams.

Rift Legacy reads Riot's local client and Live Client APIs. It does not alter gameplay, matchmaking, player statistics or game memory.

## Project structure

- `src/Launcher.cs` — small native bootstrap launcher source.
- `electron/` — Rift Legacy desktop client source.
- `src/Program.cs` — native LTK backend source.
- `mods/` — bundled `.fantome` packages.
- `LTK Manager/` — local patching engine.
- `assets/` — Rift Legacy interface assets.
- `config.json` — local PBE path; not intended for distribution.
- `state/` — generated preferences and temporary session data.

## License

The Rift Legacy source code is released under the MIT License. Third-party software and game assets retain their respective licenses and ownership.

## Credits

Rift Legacy is developed by **Xitfin** and uses **LTK Manager** as its local mod-loading engine. Some legacy adaptations are derived from community packages whose original authors remain credited in their Fantome metadata.
