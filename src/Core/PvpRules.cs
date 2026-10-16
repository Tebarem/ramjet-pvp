namespace Pvp.Core;

// The rules, as pure functions over PvpState and PvpConfig. The engine decides every hit from the flags pushed here; nothing in this
// file runs per hit, and the flags are pushed only when they change.
public static class PvpRules
{
    public const int TicksPerSecond = 20;

    // The policy flags of a player. A newbie cannot attack players and is shielded from them; a respawn shield lasts a few seconds.
    public static uint Flags(in PvpState s, bool worldPvpOff, long nowTick, PvpConfig c)
    {
        uint f = 0;
        if (s.PvpOn) f |= PvpBits.PvpOn;
        if (s.NewbieTicksLeft > 0)
        {
            f |= PvpBits.ShieldFromPlayers;
            if (c.Newbie.ProtectFromMobs) f |= PvpBits.ShieldFromMobs;
        }
        else
        {
            f |= PvpBits.MayAttackPlayers;
        }
        if (s.RespawnUntilTick > nowTick) f |= PvpBits.ShieldFromPlayers;
        if (worldPvpOff) f |= PvpBits.WorldPvpOff;
        return f;
    }

    // The combat tag an allowed hit gives both sides, in ticks (0 when the tag is disabled).
    public static int TagTicks(PvpConfig c) => c.CombatTag.Enabled ? Math.Max(0, c.CombatTag.DurationSeconds) * TicksPerSecond : 0;

    // Respawn shield length in ticks (0 when disabled).
    public static long RespawnTicks(PvpConfig c) => Math.Max(0, c.Respawn.ProtectSeconds) * (long)TicksPerSecond;

    public static bool IsNewbie(in PvpState s) => s.NewbieTicksLeft > 0;

    // A toggle is refused while the cooldown runs; secondsLeft tells the player how long.
    public static bool CanToggle(in PvpState s, long nowTick, out int secondsLeft)
    {
        long left = s.ToggleReadyTick - nowTick;
        secondsLeft = left > 0 ? (int)((left + TicksPerSecond - 1) / TicksPerSecond) : 0;
        return left <= 0;
    }

    public static long ToggleCooldownEnds(long nowTick, PvpConfig c) => nowTick + Math.Max(0, c.Pvp.ToggleCooldownSeconds) * (long)TicksPerSecond;

    // Play time: one second online takes one second off the newbie timer.
    public static void AdvanceOneSecond(ref PvpState s)
    {
        if (s.NewbieTicksLeft > 0) s.NewbieTicksLeft = Math.Max(0, s.NewbieTicksLeft - TicksPerSecond);
    }

    // The first word of a command line, without a leading slash, lower case.
    public static string CommandRoot(string line)
    {
        ReadOnlySpan<char> s = line.AsSpan().TrimStart().TrimStart('/');
        int end = s.IndexOfAny(' ', '\t');
        return (end < 0 ? s : s[..end]).ToString().ToLowerInvariant();
    }

    public static bool IsAllowedWhileTagged(string root, PvpConfig c)
    {
        foreach (string allowed in c.CombatTag.AllowedCommands)
            if (string.Equals(allowed, root, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsPvpOffWorld(string worldKey, PvpConfig c)
    {
        foreach (string w in c.Worlds.PvpOff)
            if (string.Equals(w, worldKey, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // A player kill: the killer's streak grows, the victim's ends and his death counts.
    public static void RecordKill(ref PvpState killer, ref PvpState victim)
    {
        killer.Kills++;
        killer.Streak++;
        victim.Deaths++;
        victim.Streak = 0;
    }
}
