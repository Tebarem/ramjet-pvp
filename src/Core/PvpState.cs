namespace Pvp.Core;

// The per-player state. The plugin keeps one of these as an ECS component on each online player (it survives a hot reload); the
// durable part is copied to the store on quit and on each flush. Times are game ticks, not wall-clock, so a reload loses nothing.
public struct PvpState
{
    public bool PvpOn;
    public bool Tagged;                 // set and cleared by the CombatTagStart / CombatTagEnd events
    public uint Flags;                  // the flags last pushed to the engine (PvpRules.Flags): a push happens only on change
    public int NewbieTicksLeft;         // play time left under newbie protection
    public long RespawnUntilTick;       // respawn shield ends at this tick
    public long ToggleReadyTick;        // the next /pvp toggle is allowed at this tick
    public int Kills;
    public int Deaths;
    public int Streak;
}

// The bits of the engine PvP policy flags (Player.SetPvpPolicy). Plain numbers: the core has no engine types.
public static class PvpBits
{
    public const uint PvpOn = 1;
    public const uint MayAttackPlayers = 2;
    public const uint ShieldFromPlayers = 4;
    public const uint ShieldFromMobs = 8;
    public const uint ShieldFromEnvironment = 16;
    public const uint WorldPvpOff = 32;
}
