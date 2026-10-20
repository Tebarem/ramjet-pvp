# Ramjet PvP

> **Requires `Ramjet.Api` 1.0.0-preview.3 or newer, and a server that runs it.** This plugin uses the native PvP gate, combat tags and their events, which is new in that version; it does not compile against or run on older versions.

PvP control for Ramjet servers, in the feature set of a PvPManager-style plugin: a per-player PvP toggle, newbie and respawn
protection, a combat tag with restrictions while tagged, and kill and death counts. Clean-room: written from its own design
(`src/DESIGN.md`) and public documentation only, with no code read from other PvP plugins.

- `src/`: the plugin, a Ramjet source plugin (`namespace Pvp`). Deploy it as `plugins/pvp/`; the server compiles it.
  `src/pvp.csproj` exists only for an out-of-server compile check.
- `src/Core/`: the pure logic (rules, state, config, store). No server types, so it runs outside the server.
- `tests/Pvp.Tests/`: console tests of the core.

## Commands

| Command | Node | What it does |
|---|---|---|
| `/pvp [on\|off]` | `pvp.toggle` | Turn your PvP on or off (no argument toggles). Refused while in combat and during the cooldown. |
| `/pvpstatus [player]` | `pvp.status` (others: `pvp.admin`) | PvP state, newbie time left, kills, deaths, streak. |
| `/newbie [disable]` | `pvp.newbie` | Newbie protection time left; `disable` ends it. |
| `/pvptag` | `pvp.status` | Combat tag time left. Admin: `/pvptag <player> <seconds>`. |
| `/pvpset <player> on\|off` | `pvp.admin` | Admin: set another player's PvP. |
| `/untag [player]` | `pvp.admin` | Admin: end a combat tag. |

While tagged a player cannot run commands outside the allow-list, use ender pearls or command teleports, fly, change game mode,
or place and break blocks. The bypass node `pvp.tag.bypass` lifts these restrictions.

## Configuration

`plugins/pvp/data/config.json` is written with the defaults on first run. A broken file is kept as `config.json.bad` and the
defaults are used. Sections: `pvp` (default state, toggle cooldown), `combat_tag` (duration, allowed commands), `newbie`
(duration in play time, mob protection), `respawn` (shield length), `worlds` (worlds with PvP off).

Player state is kept in `plugins/pvp/data/players.json`, written every minute and when the plugin unloads.

## Requirements

Ramjet with the PvP policy and combat tag engine support (the `PlayerPreQuit`, `CombatTagStart`, `CombatTagEnd` events and the
`pvp_policy_*` and `combat_tag_*` host calls).

## Tests

```bash
dotnet run --project tests/Pvp.Tests
```

## License

MIT, see `LICENSE`.
