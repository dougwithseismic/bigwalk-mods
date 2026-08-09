# Better Proximity Voice

A standalone voice-chat settings menu for Big Walk. It does not include or depend on
the internal dev menu.

![Better Proximity Voice controls in Big Walk's Audio settings](https://raw.githubusercontent.com/dougwithseismic/bigwalk-mods/better-proximity-voice-v1.4.0/plugins/BigWalk.VoiceSettings/screenshot.png)

Open Big Walk's **Settings > Audio** menu to use native Voice Positioning, Proximity
Range, and Speaker Indicators rows. Choose **Advanced Settings** there, or press
**Alt+V**, for the complete panel. From there you can:

- increase received proximity-voice range from vanilla (`×1`) to `×20`;
- switch all received voices between positional 3D audio and non-positional 2D audio;
- show a named, distance-coloured speaker indicator over whoever is talking;
- see a polished active-speaker roster with live voice levels; and
- see every connected player's name, distance, and talking state in the menu.

Optional quick controls can switch 3D/2D voice, cycle range presets, or toggle the
HUD without opening the menu. They are deliberately **unbound by default** so the mod
does not claim global keys that another mod may use. Bind them directly in the voice
menu: click a binding and press a chord, use **Escape** to cancel, or **Delete** to
unbind. Changes save immediately. Manual BepInEx config editing remains available but
is not required. Every quick action has a short confirmation toast.

The native Proximity Range slider selects every whole-number multiplier from `×1` to
`×20`. Advanced Settings also keeps the five named presets and an Exact field for
typing decimal values directly.

Settings persist in `BepInEx\config\com.bigwalk.voicesettings.cfg` and are reapplied as
players join. The range change is local playback only: it does not change another
player's settings or require them to install the mod.

2D mode removes positioning and distance fade entirely. Use it when intelligibility is
more important than knowing where a voice came from.

## Configuration

| Setting | Default | Purpose |
| --- | --- | --- |
| `OpenMenu` | `LeftAlt + V` | Optional shortcut for Advanced Voice Settings; native controls require no shortcut. |
| `ToggleAudioMode` | Unbound | Switches between spatial 3D and non-positional 2D voice. |
| `CycleRange` | Unbound | Cycles the five range presets. |
| `ToggleHud` | Unbound | Toggles the speaker HUD. |
| `RangeMultiplier` | `1` | Local received-voice distance multiplier (`1`-`20`). |
| `TwoDVoice` | `false` | Makes received voices non-positional. |
| `Enabled` | `true` | Shows speaking indicators. |
| `ShowNames` / `ShowDistance` | `true` / `true` | Labels active talkers in the world. |
| `ActiveSpeakerRoster` | `true` | Shows the live lower-right speaker roster. |
| `HudScale` | `1` | Scales all HUD elements (`0.75`-`1.5`). |
| `TalkThreshold` | `0.004` | Adjusts how much voice activity lights an indicator. |

## Install

Install with a Thunderstore mod manager (Gale or Thunderstore Mod Manager), or drop
`BigWalk.VoiceSettings.dll` into `BepInEx\plugins\`.
