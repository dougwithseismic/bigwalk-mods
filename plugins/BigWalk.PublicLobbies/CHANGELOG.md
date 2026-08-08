# Changelog

## 1.0.0

First release.

- Public lobby list inside the game's own **Join Game** menu, built from the menu's own
  widgets so it matches the friends list rather than sitting on top of it.
- Live player counts (`8/12`), host name and platform per room.
- Search by world name, host or join code.
- Sort by busiest, quietest, world or host; hide full rooms; crossplay-only filter.
- Join through the game's normal `ConnectTo` path, so auth and error handling are unchanged.
- Rooms are deduplicated: Big Walk advertises a lobby per player rather than per world,
  so the raw results are roughly two thirds duplicates.

Known limitations, documented rather than hidden:

- A single search returns a varying subset of lobbies, so the list is not guaranteed
  complete. Refresh to resample.
- Password-protected worlds cannot be identified from the list — the password is checked
  after connecting, and is not published as lobby data.
- Ping is not shown because the game's networking SDK exposes no latency API.
