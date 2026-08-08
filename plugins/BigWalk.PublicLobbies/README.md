# BigWalk.PublicLobbies

A public lobby browser for Big Walk, rendered as a native section inside the game's own
**Join Game** menu.

Vanilla only lets you join via a friend's game or a 6-digit code — there is no way to see
who else is playing. The lobby search that would back a browser is already in the retail
build (`EOSLobbyManager.FindPublicLobbies`), just never surfaced by any UI. This plugin
gives it a front end.

## What it shows

Each row lists the world name, the host, an exact player count (`8/12`) and the platform,
with a Join button that goes through the game's own `JoinMenu.ConnectTo` path — so auth
checks and error popups behave exactly as they do for a friend's game.

Above the list: a search box (world, host, code) and controls for sorting (busiest,
quietest, world, host), hiding full rooms, and filtering to crossplay only.

## Notes on what the data can and cannot tell you

- **Player counts are real.** They come from the lobby's `MaxMembers` / `AvailableSlots`.
- **Passwords are not visible.** Big Walk enforces its world password at the Mirror layer
  *after* you connect (`HouseAuthenticator`, `PasswordResponseMessage`), and none of the
  published EOS lobby attributes carry it. A passworded world is currently
  indistinguishable from an open one in the list.
- **Ping is not available at all.** The bundled EOS SDK exposes no latency API, and
  traffic runs over EOS relays, so there is nothing to measure before joining.
- **The list is deduplicated.** Big Walk advertises a lobby per *player*, not per world,
  so roughly two thirds of raw search results are one-slot records that duplicate the
  world behind them. Rows are collapsed by join code, preferring the host's record.
- **A refresh runs several searches.** EOS returns a varying subset of matching lobbies
  per query rather than a stable page, so one search never sees every room. A refresh
  runs `SearchRounds` of them and unions the results; the list appears as soon as the
  first round lands and fills out as the rest arrive.

## Configuration

`BepInEx\config\com.bigwalk.publiclobbies.cfg`

| Setting | Default | Purpose |
| --- | --- | --- |
| `MaxResults` | 200 | Per-search result cap. 200 is `EOS_LOBBY_MAX_SEARCH_RESULTS`; the SDK rejects more. |
| `SearchRounds` | 12 | Searches per refresh, unioned together. Past roughly 30, EOS rate-limits and returns nothing new. |
| `CompactRows` / `RowScale` | true / 0.5 | Scales rows down from friends-card size so more fit on screen. |
| `ScrollSpeed` | 120 | Pixels per wheel notch. |

## Status

Working: discovery, dedupe, player counts, search, sort, filters, scrolling, joining.

Coverage is best-effort rather than guaranteed: because EOS samples rather than pages,
more rounds find more rooms, but no number of rounds proves the list is complete.

## Install

Install with a Thunderstore mod manager (Gale or Thunderstore Mod Manager), or drop
`BigWalk.PublicLobbies.dll` into `BepInEx\plugins\`.
