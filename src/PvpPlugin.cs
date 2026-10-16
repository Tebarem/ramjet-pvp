using Pvp.Core;

namespace Pvp;

// Ramjet PvP: per-player PvP toggle, newbie and respawn protection, combat tag with restrictions, kill and death counts.
// The engine decides every hit from the policy flags this plugin pushes (Player.SetPvpPolicy): the plugin reacts only to events
// and to a once-a-second timer. Plugin state lives in an ECS component on each player (PvpState); the durable part is in players.json.
public sealed class PvpPlugin : Plugin
{
    internal const string TagBypassNode = "pvp.tag.bypass";

    private PvpConfig _config = new();
    private PvpStore _store = null!;
    private ScheduledTask? _second;
    private ScheduledTask? _flush;
    private readonly object _writeLock = new();
    private long _sequence;        // the newest snapshot handed to the writer
    private long _written;         // the newest snapshot on disk (guarded by _writeLock)

    internal PvpConfig Settings => _config;

    private long Now => (long)Server.CurrentTick;

    protected override void OnEnable()
    {
        Ecs.RegisterComponent<PvpState>("pvp:state");
        _config = ConfigFile.Load(Path.Combine(DataFolder, "config.json"), out string? configWarning);
        if (configWarning is not null) Log.Warn(configWarning);
        _store = new PvpStore(Path.Combine(DataFolder, "players.json"));
        if (_store.LoadWarning is not null) Log.Warn(_store.LoadWarning);

        Permissions.Register("pvp.use", PermissionDefault.True, "Ramjet PvP: basic use");
        Permissions.Register("pvp.toggle", PermissionDefault.True, "Ramjet PvP: /pvp on|off");
        Permissions.Register("pvp.status", PermissionDefault.True, "Ramjet PvP: /pvpstatus and /pvptag");
        Permissions.Register("pvp.newbie", PermissionDefault.True, "Ramjet PvP: /newbie");
        Permissions.Register(TagBypassNode, PermissionDefault.OpLevel(3), "Ramjet PvP: not restricted by the combat tag");
        Permissions.Register("pvp.admin", PermissionDefault.OpLevel(3), "Ramjet PvP: change other players' PvP state and tags");

        Events.On((ref PlayerJoinEvent e) => OnJoin(e.Player, e.Uuid));
        Events.On((ref PlayerQuitEvent e) => OnQuit(e.Player, e.Uuid));
        Events.On((ref PlayerRespawnEvent e) => OnRespawn(e.Player));
        Events.On((ref PlayerDeathEvent e) => OnDeath(e.Player, e.Killer));
        Events.On((ref CombatTagStartEvent e) => SetTagged(e.Player, true));
        Events.On((ref CombatTagEndEvent e) => SetTagged(e.Player, false));

        Events.On((ref PlayerCommandEvent e) => BlockCommandWhileTagged(ref e));
        Events.On((ref PlayerTeleportEvent e) => BlockTeleportWhileTagged(ref e));
        Events.On((ref PlayerToggleFlightEvent e) => BlockFlightWhileTagged(ref e));
        Events.On((ref PlayerGameModeChangeEvent e) => BlockGameModeWhileTagged(ref e));
        Events.On((ref BlockPlaceEvent e) => { if (DenyBuildWhileTagged(e.Player)) e.Cancelled = true; });
        Events.On((ref BlockBreakEvent e) => { if (DenyBuildWhileTagged(e.Player)) e.Cancelled = true; });

        PvpCommands.Register(this);

        _second = Scheduler.RunEvery(PvpRules.TicksPerSecond, EverySecond);
        _flush = Scheduler.RunEvery(60 * PvpRules.TicksPerSecond, Flush);
    }

    protected override void OnDisable()
    {
        _second?.Cancel();
        _flush?.Cancel();
        // Online players are saved as they are; the write is synchronous because the plugin is going away
        SyncOnline();
        lock (_writeLock)
        {
            long seq = ++_sequence;
            if (seq > _written)
            {
                AtomicFile.Write(_store.Path, _store.Serialize());
                _written = seq;
            }
        }
    }

    // ---- state helpers (tick thread) ----

    internal bool TryState(Player player, out PvpState state) => player.TryGet(out state);

    internal void SaveState(Player player, in PvpState state) => player.Set(state);

    // Recomputes the policy flags and pushes them to the engine only when they changed.
    internal void Push(Player player, ref PvpState state, long now)
    {
        bool worldOff = PvpRules.IsPvpOffWorld(player.World.Key, _config);
        uint flags = PvpRules.Flags(in state, worldOff, now, _config);
        if (flags == state.Flags) return;
        state.Flags = flags;
        player.SetPvpPolicy((PvpFlags)flags, PvpRules.TagTicks(_config));
    }

    // The state of a player who may be restricted by the combat tag and does not hold the bypass node.
    internal bool IsRestrictedByTag(Player player)
    {
        if (!player.TryGet(out PvpState state) || !state.Tagged) return false;
        return !player.HasPermission(TagBypassNode);
    }


    // ---- events ----

    private void OnJoin(Player player, Uuid uuid)
    {
        string key = uuid.ToString();
        bool created = _store.GetOrCreate(key, _config.Newbie.DurationSeconds * PvpRules.TicksPerSecond, _config.Pvp.DefaultOn, out PlayerRecord record);
        var state = new PvpState
        {
            PvpOn = record.PvpOn,
            NewbieTicksLeft = record.NewbieTicksLeft,
            Kills = record.Kills,
            Deaths = record.Deaths,
            Streak = record.Streak,
        };
        Push(player, ref state, Now);
        SaveState(player, state);
        if (created) Log.Info($"new player {key}: PvP {(state.PvpOn ? "on" : "off")}, newbie protection {_config.Newbie.DurationSeconds} s");
        if (PvpRules.IsNewbie(in state)) player.SendMessage($"Newbie protection: {FormatSeconds(state.NewbieTicksLeft / PvpRules.TicksPerSecond)} of play time left");
    }

    private void OnQuit(Player player, Uuid uuid)
    {
        if (!TryState(player, out PvpState state)) return;
        _store.Put(uuid.ToString(), ToRecord(state));
    }

    private void OnRespawn(Player player)
    {
        if (!TryState(player, out PvpState state)) return;
        long ticks = PvpRules.RespawnTicks(_config);
        if (ticks <= 0) return;
        state.RespawnUntilTick = Now + ticks;
        Push(player, ref state, Now);
        SaveState(player, state);
    }

    private void OnDeath(Player victim, Entity killerEntity)
    {
        if (!TryState(victim, out PvpState victimState)) return;
        Player? killer = FindPlayer(killerEntity);
        if (killer is { } k && k != victim && TryState(k, out PvpState killerState))
        {
            PvpRules.RecordKill(ref killerState, ref victimState);
            SaveState(k, killerState);
        }
        else
        {
            victimState.Deaths++;
            victimState.Streak = 0;
        }
        SaveState(victim, victimState);
    }

    private static Player? FindPlayer(Entity entity)
    {
        if (entity.IsNull) return null;
        foreach (Player p in Server.Players)
            if (p.Entity == entity) return p;
        return null;
    }

    private void SetTagged(Player player, bool tagged)
    {
        if (!TryState(player, out PvpState state)) return;
        state.Tagged = tagged;
        SaveState(player, state);
    }

    private void BlockCommandWhileTagged(ref PlayerCommandEvent e)
    {
        if (!IsRestrictedByTag(e.Player)) return;
        string root = PvpRules.CommandRoot(e.Command.ToString());
        if (PvpRules.IsAllowedWhileTagged(root, _config)) return;
        e.Cancelled = true;
        e.Player.SendMessage($"You cannot use /{root} while in combat");
    }

    private void BlockTeleportWhileTagged(ref PlayerTeleportEvent e)
    {
        if (e.Cause is not (TeleportCause.EnderPearl or TeleportCause.Command)) return;
        if (!IsRestrictedByTag(e.Player)) return;
        e.Cancelled = true;
        e.Player.SendMessage("You cannot teleport while in combat");
    }

    private void BlockFlightWhileTagged(ref PlayerToggleFlightEvent e)
    {
        if (!e.Flying || !IsRestrictedByTag(e.Player)) return;
        e.Cancelled = true;
        e.Player.SendMessage("You cannot fly while in combat");
    }

    private void BlockGameModeWhileTagged(ref PlayerGameModeChangeEvent e)
    {
        if (!IsRestrictedByTag(e.Player)) return;
        e.Cancelled = true;
        e.Player.SendMessage("You cannot change game mode while in combat");
    }

    // Block place and break: true when the player may not build now (the caller cancels the event).
    private bool DenyBuildWhileTagged(Player player)
    {
        if (!IsRestrictedByTag(player)) return false;
        player.SendMessage("You cannot build while in combat");
        return true;
    }

    // ---- timers ----

    // Newbie play time, respawn shield expiry, combat tag display: once a second per online player, never per tick.
    private void EverySecond()
    {
        long now = Now;
        foreach (Player player in Server.Players)
        {
            if (!player.IsOnline || !TryState(player, out PvpState state)) continue;
            bool wasNewbie = PvpRules.IsNewbie(in state);
            PvpRules.AdvanceOneSecond(ref state);
            if (wasNewbie && !PvpRules.IsNewbie(in state)) player.SendMessage("Your newbie protection has ended");
            Push(player, ref state, now);
            SaveState(player, state);
            if (state.Tagged)
            {
                long seconds = (long)Math.Ceiling(Combat.Remaining(player).TotalSeconds);
                player.SendActionBar($"In combat: {seconds}s");
            }
        }
    }

    // Writes the players' state to the store and the file. The text is built here; the file is written off the tick thread.
    private void Flush()
    {
        SyncOnline();
        long seq = ++_sequence;
        string text = _store.Serialize();
        _ = Scheduler.RunAsync(() => WriteSnapshot(seq, text));
    }

    private void WriteSnapshot(long seq, string text)
    {
        lock (_writeLock)
        {
            if (seq <= _written) return;  // a newer snapshot is already on disk
            AtomicFile.Write(_store.Path, text);
            _written = seq;
        }
    }

    // Copies every online player's state into the store.
    private void SyncOnline()
    {
        foreach (Player player in Server.Players)
        {
            if (!player.IsOnline || !TryState(player, out PvpState state)) continue;
            _store.Put(player.Uuid.ToString(), ToRecord(state));
        }
    }

    private static PlayerRecord ToRecord(in PvpState state) => new()
    {
        PvpOn = state.PvpOn,
        NewbieTicksLeft = state.NewbieTicksLeft,
        Kills = state.Kills,
        Deaths = state.Deaths,
        Streak = state.Streak,
    };

    internal static string FormatSeconds(long seconds)
    {
        if (seconds >= 60) return $"{seconds / 60} min {seconds % 60} s";
        return $"{seconds} s";
    }
}
