# Skip Intro

Gets you to the title screen. Skips the **splash screen** and the **microphone check**
that you clear identically every single launch.

## What it does

Both screens are dismissed through the game's own continue path — the same thing that
happens when you click the button yourself — rather than by disabling or destroying the
menus. Their internal bookkeeping still runs, so nothing downstream gets confused about
which audio device you picked.

## Configuration

`BepInEx/config/com.bigwalk.skipintro.cfg`

| Setting | Default | Effect |
| --- | --- | --- |
| `SkipSplash` | `true` | Skip the splash screen |
| `SkipMicCheck` | `true` | Skip the microphone check |

Turn either off independently if you want one but not the other.

## Notes

- This skips the **game's** splash. Unity's own engine splash is baked into the player
  and can't be touched by a mod.
- If a skip ever fails, it logs the error and lets the screen show normally rather than
  leaving you stuck on a broken startup.
- Client-side and cosmetic. Doesn't touch multiplayer, doesn't need anyone else to have it.

## Install

Install with a mod manager (Gale or Thunderstore Mod Manager), or drop
`BigWalk.SkipIntro.dll` into `BepInEx/plugins/`.
