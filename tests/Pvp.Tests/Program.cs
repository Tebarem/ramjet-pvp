using Pvp.Core;

// Console test runner for the pure core: rules, state, config and store. No server is needed.
internal static class Program
{
    private static int _failures;
    private static int _checks;

    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("flags: a newbie cannot attack players and is shielded from players and mobs", NewbieFlags),
            ("flags: a veteran may attack; PvP-on bit follows the toggle; world-off bit", VeteranFlags),
            ("flags: a respawn shield lasts exactly its ticks", RespawnShield),
            ("toggle: the cooldown refuses, then allows, and reports seconds left", ToggleCooldown),
            ("newbie: one second online takes 20 ticks, never below zero", NewbieCountdown),
            ("commands: the first word, lower case, slash and blanks skipped", CommandRoots),
            ("commands: the tag allow-list is case-insensitive and exact", AllowList),
            ("tag: duration in ticks; 0 when disabled", TagTicks),
            ("kills: the killer's streak grows, the victim's streak ends", Kills),
            ("config: defaults write and read back equal; a missing file is written", ConfigDefaults),
            ("config: a broken file is kept as .bad and the defaults are used", ConfigBroken),
            ("store: records survive a save and a reload; unknown uuid is created once", StoreRoundTrip),
            ("store: a broken players.json is kept as .bad and the store starts empty", StoreBroken),
            ("atomic write: the target has the text and no temp file is left", AtomicWrite),
        };

        foreach (var (name, run) in tests)
        {
            int before = _failures;
            try
            {
                run();
            }
            catch (Exception e)
            {
                Fail(name + " threw " + e);
            }
            Console.WriteLine((_failures == before ? "PASS " : "FAIL ") + name);
        }

        Console.WriteLine($"{_checks} checks, {_failures} failed");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (!condition) Fail(what);
    }

    private static void Fail(string what)
    {
        _failures++;
        Console.Error.WriteLine("  check failed: " + what);
    }

    private static PvpConfig Config() => new();

    private static void NewbieFlags()
    {
        var c = Config();
        var s = new PvpState { PvpOn = true, NewbieTicksLeft = 100 };
        uint f = PvpRules.Flags(in s, false, 0, c);
        Check((f & PvpBits.MayAttackPlayers) == 0, "newbie must not attack");
        Check((f & PvpBits.ShieldFromPlayers) != 0, "newbie shielded from players");
        Check((f & PvpBits.ShieldFromMobs) != 0, "newbie shielded from mobs by default");
        Check((f & PvpBits.PvpOn) != 0, "PvP-on bit kept");

        c.Newbie.ProtectFromMobs = false;
        f = PvpRules.Flags(in s, false, 0, c);
        Check((f & PvpBits.ShieldFromMobs) == 0, "mob shield follows the config");
    }

    private static void VeteranFlags()
    {
        var c = Config();
        var s = new PvpState { PvpOn = true };
        uint f = PvpRules.Flags(in s, false, 0, c);
        Check(f == (PvpBits.PvpOn | PvpBits.MayAttackPlayers), $"veteran flags were {f}");

        s.PvpOn = false;
        f = PvpRules.Flags(in s, false, 0, c);
        Check((f & PvpBits.PvpOn) == 0, "PvP-off clears the bit");

        f = PvpRules.Flags(in s, true, 0, c);
        Check((f & PvpBits.WorldPvpOff) != 0, "world PvP off sets its bit");
    }

    private static void RespawnShield()
    {
        var c = Config();
        var s = new PvpState { PvpOn = true, RespawnUntilTick = 100 };
        Check((PvpRules.Flags(in s, false, 99, c) & PvpBits.ShieldFromPlayers) != 0, "shield while the tick is before the end");
        Check((PvpRules.Flags(in s, false, 100, c) & PvpBits.ShieldFromPlayers) == 0, "no shield at the end tick");
        Check(PvpRules.RespawnTicks(c) == 100, "5 s is 100 ticks");
        c.Respawn.ProtectSeconds = 0;
        Check(PvpRules.RespawnTicks(c) == 0, "0 s disables the shield");
    }

    private static void ToggleCooldown()
    {
        var s = new PvpState { ToggleReadyTick = 200 };
        Check(!PvpRules.CanToggle(in s, 150, out int left), "refused during cooldown");
        Check(left == 3, $"50 ticks is 3 s (rounded up), got {left}");
        Check(PvpRules.CanToggle(in s, 200, out left) && left == 0, "allowed exactly at the ready tick");
        long ends = PvpRules.ToggleCooldownEnds(1000, Config());
        Check(ends == 1000 + 10 * 20, "10 s cooldown in ticks");
    }

    private static void NewbieCountdown()
    {
        var s = new PvpState { NewbieTicksLeft = 30 };
        PvpRules.AdvanceOneSecond(ref s);
        Check(s.NewbieTicksLeft == 10, "30 - 20 = 10");
        PvpRules.AdvanceOneSecond(ref s);
        Check(s.NewbieTicksLeft == 0, "clamped at zero");
        PvpRules.AdvanceOneSecond(ref s);
        Check(s.NewbieTicksLeft == 0, "stays at zero");
    }

    private static void CommandRoots()
    {
        Check(PvpRules.CommandRoot("/PVP on") == "pvp", "slash and case");
        Check(PvpRules.CommandRoot("pvpstatus") == "pvpstatus", "single word");
        Check(PvpRules.CommandRoot("  /tp 1 2 3") == "tp", "leading blanks");
        Check(PvpRules.CommandRoot("msg\tbob hi") == "msg", "tab separates");
        Check(PvpRules.CommandRoot("") == "", "empty line");
    }

    private static void AllowList()
    {
        var c = Config();
        Check(PvpRules.IsAllowedWhileTagged("PVP", c), "pvp allowed in any case");
        Check(!PvpRules.IsAllowedWhileTagged("tp", c), "tp refused");
        Check(!PvpRules.IsAllowedWhileTagged("pv", c), "prefix is not a match");
        Check(PvpRules.IsPvpOffWorld("minecraft:overworld", new PvpConfig { Worlds = { PvpOff = ["minecraft:overworld"] } }), "world list");
    }

    private static void TagTicks()
    {
        var c = Config();
        Check(PvpRules.TagTicks(c) == 15 * 20, "15 s default");
        c.CombatTag.Enabled = false;
        Check(PvpRules.TagTicks(c) == 0, "disabled");
    }

    private static void Kills()
    {
        var killer = new PvpState { Streak = 2 };
        var victim = new PvpState { Streak = 5 };
        PvpRules.RecordKill(ref killer, ref victim);
        Check(killer.Kills == 1 && killer.Streak == 3, "killer");
        Check(victim.Deaths == 1 && victim.Streak == 0, "victim");
    }

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pvp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void ConfigDefaults()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            var loaded = ConfigFile.Load(path, out string? warning);
            Check(warning is null, "no warning for a fresh install");
            Check(File.Exists(path), "defaults written");
            Check(loaded.CombatTag.DurationSeconds == 15 && loaded.Newbie.DurationSeconds == 1800, "default values");
            var again = ConfigFile.Load(path, out _);
            Check(again.CombatTag.AllowedCommands.SequenceEqual(loaded.CombatTag.AllowedCommands), "allow-list round trips");
            Check(again.Pvp.DefaultOn == loaded.Pvp.DefaultOn && again.Respawn.ProtectSeconds == 5, "fields round trip");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void ConfigBroken()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "config.json");
            File.WriteAllText(path, "{ not json");
            var c = ConfigFile.Load(path, out string? warning);
            Check(warning is not null && warning.Contains("kept as config.json.bad"), "warning names the .bad file");
            Check(File.Exists(path + ".bad") && !File.Exists(path), "broken file moved aside");
            Check(c.CombatTag.Enabled, "defaults in use");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void StoreRoundTrip()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "players.json");
            var store = new PvpStore(path);
            Check(store.LoadWarning is null && store.Count == 0, "fresh store is empty");
            bool created = store.GetOrCreate("uuid-a", 1800 * 20, true, out PlayerRecord rec);
            Check(created && rec.PvpOn && rec.NewbieTicksLeft == 36000, "new record gets the newbie timer");
            rec.Kills = 4;
            rec.Deaths = 2;
            rec.Streak = 1;
            store.Save();

            var reloaded = new PvpStore(path);
            Check(reloaded.Count == 1, "one record after reload");
            bool createdAgain = reloaded.GetOrCreate("uuid-a", 999, false, out PlayerRecord again);
            Check(!createdAgain && again.Kills == 4 && again.Deaths == 2 && again.PvpOn, "known record keeps its values");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void StoreBroken()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "players.json");
            File.WriteAllText(path, "[[");
            var store = new PvpStore(path);
            Check(store.LoadWarning is not null && store.LoadWarning.Contains("players.json.bad"), "warning names the .bad file");
            Check(File.Exists(path + ".bad"), "broken file kept");
            Check(store.Count == 0, "starts empty");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AtomicWrite()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "sub", "f.json");
            AtomicFile.Write(path, "one");
            AtomicFile.Write(path, "two");
            Check(File.ReadAllText(path) == "two", "second write replaces the first");
            Check(!File.Exists(path + ".tmp"), "no temp file left");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
