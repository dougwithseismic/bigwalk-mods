# Big Walk Modding Guide

Everything here was verified against the shipped build, not inferred from other Unity games.

**Target:** Big Walk (Steam appid `1478500`), Unity `6000.3.17f1`, IL2CPP.

---

## 1. What the game is made of

| Concern | Implementation |
| --- | --- |
| Engine | Unity 6000.3.17f1, IL2CPP (`GameAssembly.dll`, ~64 MB) |
| Networking | **Mirror** (`kcp2k`, `FizzySteamworks`, `Telepathy`) — host is server + client |
| Voice chat | **Dissonance Voip** + `opus`, integrated via `MirrorIgnorancePlayer` |
| Platform | Steamworks.NET, EOS SDK |
| Input | Rewired |

Because it is IL2CPP there is no managed `Assembly-CSharp.dll` to edit. Modding means
runtime patching through a loader, not swapping a DLL.

---

## 2. Toolchain

### The one non-obvious gotcha

The game's `global-metadata.dat` is **version 39**. **Il2CppDumper cannot read it** — even
the current release (6.7.46) fails with `not a supported version[39]`. Don't burn time there.

Use **Cpp2IL `2022.1.0-pre-release.21`** (Feb 2026) or newer, which handles v39.

### Setup

```powershell
scoop install dotnet-sdk                 # SDK 10; the stock C:\Program Files\dotnet has runtimes only
$env:DOTNET_ROOT = "$HOME\scoop\apps\dotnet-sdk\current"
dotnet tool install -g ilspycmd          # decompiler
```

`DOTNET_ROOT` matters: with a pre-existing `C:\Program Files\dotnet` on PATH, tools resolve
to that install, find no SDK, and fail with a confusing "framework missing" error.

### Dumping the game

```powershell
$game = "C:\Program Files (x86)\Steam\steamapps\common\Big Walk"

# Structure: types, fields, method signatures
.\Cpp2IL.exe --game-path $game --exe-name "Big Walk" --output-as dummydll --output-to .\out_dummy

# Logic: raw disassembly + lifted ISIL
.\Cpp2IL.exe --game-path $game --exe-name "Big Walk" --output-as isil --output-to .\out_isil

# Then decompile the dummy DLLs to readable C#
ilspycmd -r .\out_dummy\ -o .\src\DissonanceVoip -p .\out_dummy\DissonanceVoip.dll
```

**Expect `throw null` method bodies.** Cpp2IL's `dll_il_recovery` format reconstructs
signatures reliably but not most bodies. For actual logic, read the `isil` output — it gives
you x86 disassembly alongside a lifted IR, which is enough to recover field offsets and
arithmetic. That is how the proximity-chat formulas below were established.

---

## 3. Install / uninstall

```powershell
.\scripts\install.ps1                # BepInEx loader + any built plugins
.\scripts\install.ps1 -PluginsOnly   # fast loop: refresh plugin DLLs only
.\scripts\uninstall.ps1              # full revert to vanilla
.\scripts\uninstall.ps1 -KeepCache   # revert but keep interop assemblies (saves minutes)
```

Both scripts locate the game by parsing Steam's `libraryfolders.vdf`, and both refuse to run
while `Big Walk.exe` is alive — the loader cannot be swapped under a running process.
Override detection with `$env:BIGWALK_PATH` if needed.

**First launch after installing takes several minutes.** BepInEx runs Cpp2IL to generate
IL2CPP interop assemblies into `BepInEx\interop\`. The game looks frozen; it isn't. Watch
`BepInEx\LogOutput.log`.

Loader is **BepInEx 6 bleeding-edge, Unity.IL2CPP win-x64** (`be.785`). The stable
`v6.0.0-pre.2` tag is from 2024 and predates Unity 6.3 — it will not work here. Bleeding-edge
builds come from <https://builds.bepinex.dev/projects/bepinex_be>.

### Steam will undo this

"Verify integrity of game files" deletes `winhttp.dll` and reverts the install. That is a
feature, not a problem — it is the emergency uninstall. But it means a clean `uninstall.ps1`
has to be a real, maintained script rather than an afterthought.

---

## 4. Proximity voice chat — how it actually works

This is the part most likely to waste your time if you guess.

Big Walk uses Dissonance's grid proximity system: `VoiceProximityBroadcastTrigger` and
`VoiceProximityReceiptTrigger`, both deriving `BaseProximityTrigger<T>`. The base room name
is `GridProximityChat`.

Recovered from disassembly:

```
BaseProximityTrigger._range        int, object offset 0x50
Size                            => _range * 2          // cell size is DOUBLE the range
Grid.CellPos(pos)               => floor(pos / Size)
Grid.GenerateName(cell)         => RoomName + cell coordinates
```

`get_Size` is literally `mov eax,[rcx+50h]; add eax,eax; cvtdq2ps` — load `_range`, double it,
convert to float.

Voice is routed by **room name string**. The broadcaster opens `RoomChannel`s on the grid
cells around it; the listener opens `RoomMembership`s on the cells around it; you hear each
other only where those generated names collide.

### The trap

**A client-side-only range increase makes you hear nobody.**

Room names encode cell coordinates quantised by `Range * 2`. Raise your Range alone and your
cells are a different size, so your generated room names never match anyone else's, and you
are alone in a private grid. Range is effectively a wire-format constant shared by the lobby,
not a local preference.

The upside: `set_Range` rebuilds the grid when the value changes, so Range **is** safe to
change at runtime — no need to patch serialized assets in `data.unity3d`.

### Two viable designs

**A — every client installs the mod.** Host broadcasts its configured Range over a Mirror
message; every client calls `set_Range`. All grids agree. Simple and robust; requires everyone
to install.

**B — host-only, clients vanilla.** The host runs the Dissonance server
(`Dissonance.Networking.Server`: `ServerRelay`, `BroadcastingClientCollection`, `ServerState`).
Hook the relay so voice addressed to cell room `X` also reaches subscribers of neighbouring
cells. Nobody else installs anything.

Route B has a **second gate that must be cleared before trusting it**: each listener's voice
`AudioSource` has its own 3D rolloff. Widening server routing can deliver packets that the
client then attenuates to silence. Dissonance carries `PlaybackOptions.IsPositional` and
`AmplitudeMultiplier` per channel, and `VoicePlayback.UpdatePositionalPlayback` applies them —
so the fix, if needed, is to make the host mark those channels non-positional or boost
amplitude. Verify before building on it.

---

## 5. The dev menu is gone — but the cheats aren't

The retail build ships the dev/cheat **components** with intact logic, but the **input path
that drove them is compiled out**:

```
PlayerCheater.Update()          ->  ret 0            (empty)
PlayerCheater.CheckForCheat()   ->  xor al,al; ret   (always false)
```

So there is no flag to flip and no cheat code to type — the code that would have read your
keystrokes is a stub.

What *did* survive, with real bodies:

| Type | State |
| --- | --- |
| `CameraCheatMover` | Intact, ~2500 instructions — free-cam |
| `SpawnEmCheat` | Intact |
| `TrainCheater` | Intact |
| `DevMenuRow.Assign` | Intact (`label` + `button.onClick` wiring) |
| `PlayerCheater` fields | Present: `_voiceToggle`, `_voice2DToggle`, `_voice2DSet`, `ghostMovementScalar`, `LockedRay` |

`DevMenuRow` is referenced by nothing in `Assembly-CSharp` — the menu *controller* was
stripped, leaving only the row widget.

**Therefore "enabling the dev menu" means rebuilding it.** A BepInEx plugin can instantiate
these surviving components and drive them directly: add a `CameraCheatMover` to get free-cam,
flip `PlayerCheater._voice2DToggle` to test non-positional voice, and build our own IMGUI
overlay instead of reconstructing the original UI. The interesting behaviour is still in the
binary; only the front end is missing.

The `_voice2DToggle` field is worth an early look — non-positional voice is exactly the
"hear everyone regardless of distance" behaviour, and it may short-circuit the whole
proximity problem for testing purposes.

---

## 6. Where things live

```
E:\WEB_PROJECTS\bigwalk\
  scripts\    install.ps1, uninstall.ps1, common.ps1
  vendor\     BepInEx bleeding-edge zip
  plugins\    plugin source (built DLLs are picked up from bin\ by install.ps1)
  docs\       this guide
```
