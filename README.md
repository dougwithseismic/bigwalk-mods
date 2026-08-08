# Big Walk Mods

BepInEx (IL2CPP) mods for **Big Walk** — Unity 6000.3.17f1, Mirror networking, Dissonance voice.

See **[docs/modding-guide.md](docs/modding-guide.md)** for how the game is built, how to dump
it, and the reverse-engineering findings behind these mods.

## Quick start

```powershell
.\scripts\install.ps1              # BepInEx loader (first launch takes minutes - it generates interop)
.\scripts\build.ps1 -Deploy        # build plugins and copy them in
.\scripts\new-mod.ps1 -Name Thing  # scaffold a new plugin and add it to the solution
.\scripts\package.ps1              # build Thunderstore zips into dist\
.\scripts\uninstall.ps1            # full revert to vanilla
```

Install and uninstall refuse to run while the game is open, and locate Big Walk by parsing
Steam's `libraryfolders.vdf`. Override with `$env:BIGWALK_PATH`.

The loader is **BepInEx 6.0.755** — the Thunderstore `BepInExPack_IL2CPP` that every Big Walk
mod depends on, so we develop against exactly what users run. See
**[docs/publishing.md](docs/publishing.md)** for releasing to Thunderstore.

Shared build settings (target framework, all game/loader references) live in
`plugins\Directory.Build.props`, so each plugin's csproj is only a name, version and
description.

## Plugins

### BigWalk.SkipIntro

Skips the two startup gates you clear identically every launch: the **splash screen** and the
**microphone check**. Both are dismissed via the game's own `ActionContinue()` path rather than
by disabling the menu objects, so their internal bookkeeping still runs.

Each is independently toggleable in `BepInEx\config\com.bigwalk.skipintro.cfg`.

> Note: this skips the *game's* splash. Unity's own engine splash is baked into the player and
> is not reachable from a plugin.

### BigWalk.DevMenu

Stands in for the dev menu compiled out of the retail build — `PlayerCheater.Update()` is
literally `ret 0` and nothing constructs `DevMenuRow`. The underlying cheat components survived
with working logic, so this supplies a new front end for them.

**F1** opens it. Three tabs:

- **Proximity** — live voice-grid diagnostics: each trigger's `Range`, derived cell size, your
  grid cell, and every tracked player with their distance in metres. Range nudge buttons are
  **solo-test only** (see the guide on why a mismatched Range silences you).
- **Camera** — free cam via `CameraCheatMover.Detach/Attach` (**F3**), with speed controls.
- **World** — `SpawnEmCheat.Spawn()` and `TrainCheater.SetDistance()`. These mutate shared
  world state that everyone in the lobby sees. On by default; set `AllowWorldCheats = false`
  to hide them.

## Thunderstore

`scripts\package.ps1` produces upload-ready zips in `dist\` — correct layout
(`BepInEx/plugins/`), a valid `manifest.json`, and a 256×256 `icon.png`.

Big Walk has no Thunderstore community yet, so `dependencies` is empty. Once a game-specific
BepInExPack exists, add its exact `Team-Package-1.2.3` id to `$dependencies` in the script.
Icons are generated placeholders — replace them with real art before publishing.

## Status

| Piece | State |
| --- | --- |
| BepInEx **6.0.755** (Thunderstore pack) on Unity 6.3 / metadata v39 | **Working** |
| `install` / `uninstall` / `build` / `package` / `new-mod` scripts | Working |
| `BigWalk.SkipIntro` | **Both skips confirmed in game on 6.0.755.** Packaged, ready to publish |
| `BigWalk.DevMenu` | Loads on 6.0.755. Internal tool — not for release |
| Proximity range increase | Blocked on route A vs B (see guide §4) |

## Ideas backlog

- **Proximity range** — needs route A (all clients) or route B (host-only relay) decided.
  Route B still has an uncleared gate: per-listener `AudioSource` rolloff may silence voice
  the server successfully routed.
- **Climbing** — no ledge/climb system exists to unlock, but `PlayerHands.dragJoint`
  (a `ConfigurableJoint`) plus `PlayerGround` / `PlayerArms` are the primitives PEAK-style
  climbing is built from. New mechanic, needs networking.
- **Deeplink / join-by-URL** — `JoinMenu`, `JoinFriendCard`, `LobbyNetworking`, FizzySteamworks.
  Needs an OS-registered protocol handler, so this is the one idea that genuinely argues for a
  desktop companion app.
