# Ramjet PvP: design

## Shape

- `Core/` holds the rules as pure functions over two value types: `PvpState` (per player) and `PvpConfig` (settings). It has
  no server types, so the console tests compile it directly.
- `PvpPlugin` is the glue: it subscribes to events, keeps `PvpState` as an ECS component on each player (it survives a hot
  reload), runs one timer per second, and saves the store.
- `PvpCommands` registers the commands. Admin branches carry their own `pvp.admin` node on the argument they add.

## Where decisions are made

Per hit, the engine decides. The plugin only pushes a policy (`Player.SetPvpPolicy`) and only when the policy's flags change.
The flags are computed by `PvpRules.Flags`:

| Flag | Set when |
|---|---|
| `PvpOn` | the player turned PvP on |
| `MayAttackPlayers` | not a newbie |
| `ShieldFromPlayers` | newbie, or inside the respawn shield |
| `ShieldFromMobs` | newbie and `newbie.protect_from_mobs` |
| `WorldPvpOff` | the player's world is in `worlds.pvp_off` |

The tag time (`combat_tag.duration_s`) is pushed with the flags. An allowed hit tags both sides in the engine; the tag ends in
the engine by expiry, death, logout or `Combat.Untag`.

The plugin does zero work per hit. Work that happens per event (commands, teleports, flight, game mode, block place and break)
reads the `Tagged` bit of the player's state, which the `CombatTagStart` and `CombatTagEnd` events keep current. Those handlers
allocate nothing on the common path.

## Timers

- Once a second per online player: count down newbie play time, expire the respawn shield (the flags are pushed on change), show
  the tag countdown in the action bar for tagged players.
- Once a minute: copy every online player's state into the store, serialize it, and write the file off the tick thread. A
  snapshot never overwrites a newer one (`_written` sequence under a lock). The file is replaced atomically.

## Events used

- `PlayerJoinEvent` sets the policy before the player plays (no `PlayerPreLogin` deferral needed: the store is in memory).
- `PlayerQuitEvent` copies the state into the store.
- `PlayerRespawnEvent` starts the respawn shield.
- `PlayerDeathEvent` counts the kill (killer found among the online players) or the death, and resets the streak.
- `CombatTagStartEvent` / `CombatTagEndEvent` maintain `Tagged`.
- `PlayerCommandEvent`, `PlayerTeleportEvent`, `PlayerToggleFlightEvent`, `PlayerGameModeChangeEvent`, `BlockPlaceEvent`,
  `BlockBreakEvent` cancel restricted actions while tagged (checked after the bypass node).

## Decisions

- **Both sides must have PvP on**, and a newbie cannot attack players (stricter than vanilla).
- **Newbie protection** counts play time: one second online is one second off the timer, so an AFK newbie keeps the protection
  only while online. It is a per-player record, created on first join with `newbie.duration_s`.
- **Toggle cooldown** is measured in game ticks from the state. Refused while tagged. Setting the same state again costs nothing.
- **Default PvP state** for a first join is `pvp.default_on` (on by default).
- **Command allow-list**: a tagged player may run only the commands in `combat_tag.allowed_commands` (first word, case-insensitive),
  unless he holds `pvp.tag.bypass`.
- **Teleports**: ender pearls and `/tp`-style command teleports are refused while tagged; other causes (plugin, portal, death)
  are not.

## Deviations from the plan, and why

- **Admin PvP command is `/pvpset <player> on|off`**, not `/pvp <player> on|off`. Under brigadier a `Word` and a `Player` argument
  at the same node would both match `on`/`off`, so the admin form has its own root.
- **The engine gets `flags` and `tag_ticks` only.** Respawn and newbie timers live in the plugin and reach the engine as
  `ShieldFromPlayers` / `MayAttackPlayers`. The per-hit timer fields of the original plan are not needed while the flags change once per
  second at most.
- **Kill credit** finds the killer by scanning the online players for the killer entity (rare: once per death).

## Not in this version

- Logout punishment (kill, drop, NPC logger) and kill rewards (economy, commands with placeholders): the `PlayerPreQuit` hook is
  there for the punishment; the economy contract is not defined yet. Kill abuse and stats export are also later.
- Zones (cuboid regions with flags), the fire and lava guard, death penalties and drop rules.
- Elytra and container restrictions while tagged.
- Messages in a locale file: the wording is in code.
- Placeholders for other plugins.
- Import of a PvPManager-style configuration.

## Test coverage

`tests/Pvp.Tests` (49 checks): flags for newbie, veteran, respawn shield and world; toggle cooldown; newbie countdown; command roots
and the allow-list; tag ticks; kill counts; config defaults, round trip and broken-file fallback; store round trip, creation and
broken-file fallback; atomic write. The engine side is tested by the engine's own test file (not run by the plugin repository).
