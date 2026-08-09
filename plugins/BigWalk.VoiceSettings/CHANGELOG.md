# Changelog

## 1.4.0

- Add Voice Positioning, Proximity Range, and Speaker Indicators directly to Big Walk's native Audio settings.
- Add a native Advanced Settings row leading to the complete panel and shortcut rebinder.
- Preserve the game's fonts, menu sounds, mouse input, and controller navigation by cloning live settings rows.
- Add overflow-aware Audio scrolling with wheel/drag input and automatic controller selection tracking.
- Make the entire Audio panel a scroll target, including blank space between and around controls.
- Replace the native range presets with an exact `×1`-`×20` slider; retain named presets and decimal input in Advanced Settings.
- Make Reset Audio restore vanilla 3D Spatial voice and `×1` range along with default speaker indicators.
- Remove the redundant custom pause-menu button.

## 1.3.0

- Add complete in-menu shortcut rebinding with modifier-chord capture.
- Support Escape to cancel and Delete/Backspace or Clear to unbind.
- Persist new bindings immediately; manual BepInEx config editing is no longer needed.
- Keep vanilla `×1` range and 3D Spatial audio as fresh-install defaults.

## 1.2.0

- Add a native-styled Voice Settings button to both host and client pause menus.
- Replace bare function keys with configurable modifier-aware shortcuts.
- Use `Alt+V` as the only default shortcut; all quick actions are unbound by default.
- Show current bindings and warn about duplicate bindings inside the voice menu.

## 1.1.0

- Show real sanitized player names and distances on speaking indicators.
- Add a compact active-speaker roster with live amplitude meters.
- Add dedicated 3D Spatial and 2D Everywhere audio-mode controls.
- Add five named range presets and show the effective received-voice edge.
- Add configurable F4/F5/F6 quick actions with polished confirmation toasts.
- Add HUD name, distance, roster, and scale settings.
- Improve menu contrast, typography, sizing, and live player information.

## 1.0.0

- Initial standalone release with range scaling, 2D voice, speaker indicators,
  and an F2 settings menu.
