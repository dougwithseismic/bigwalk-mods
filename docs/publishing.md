# Publishing to Thunderstore

Big Walk's community is live at <https://thunderstore.io/c/big-walk/> — you don't need
anyone to create it.

## One-time setup

1. Sign in at <https://thunderstore.io/> (GitHub, Discord, or Overwolf).
2. Create a **team** at <https://thunderstore.io/settings/teams/>. Packages are owned by
   teams, not users, and the team name becomes the permanent first part of the package id
   (`Team-Package-1.0.0`). Pick one you're happy to keep.

## Publishing

```powershell
.\scripts\package.ps1     # writes upload-ready zips to dist\
```

Then upload at <https://thunderstore.io/c/big-walk/create/> — pick the zip, select your
team, choose categories, submit.

## What package.ps1 guarantees

Thunderstore is strict about package shape, and these are the rules it enforces:

| Requirement | How it's handled |
| --- | --- |
| `manifest.json` at the zip root | Generated from the csproj's `AssemblyName` / `Version` / `Description` |
| `icon.png` exactly 256×256 | Generated, then verified; a plugin's own `icon.png` wins if present |
| `README.md` at the zip root | Taken from the plugin folder, else generated |
| Payload laid out as it lands in the game | DLLs go to `BepInEx/plugins/` |
| Description ≤ 250 chars | Truncated |
| Package name alphanumeric + underscores | `BigWalk.SkipIntro` → `BigWalk_SkipIntro` |
| Dependencies in exact `Team-Package-1.2.3` form | `BepInEx-BepInExPack_IL2CPP-6.0.755` |

## Versioning

Thunderstore **will not let you re-upload an existing version number**. Bump `<Version>`
in the csproj for every upload, and keep the `[BepInPlugin]` version in `Plugin.cs` in sync
— `package.ps1` reads the csproj, while the in-game log shows the attribute, and they
disagreeing is confusing later.

## Custom icon

Drop a 256×256 `icon.png` next to a plugin's `.csproj` and it will be used instead of the
generated placeholder.

## Automating it later

`tcli` (the Thunderstore CLI) can publish from the command line or CI with a service
account token. Worth setting up once you're releasing often; the manual upload is fine
for the first few.
