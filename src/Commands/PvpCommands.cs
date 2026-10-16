using Pvp.Core;

namespace Pvp;

// The player commands and the admin commands. Admin branches carry the pvp.admin node on their own argument, so the plain forms stay open.
internal static class PvpCommands
{
    public static void Register(PvpPlugin plugin)
    {
        plugin.Commands.Register(
            Command.Literal("pvp").Permission("pvp.toggle")
                .Executes(ctx => Toggle(plugin, ctx, null))
                .Then(Command.Argument("state", Arg.Word()).Executes(ctx => Toggle(plugin, ctx, ctx.GetString("state")))),
            description: "Turn your PvP on or off (or toggle it)");

        plugin.Commands.Register(
            Command.Literal("pvpset").Permission("pvp.admin")
                .Then(Command.Argument("player", Arg.Player())
                    .Then(Command.Argument("state", Arg.Word()).Executes(ctx => AdminSet(plugin, ctx)))),
            description: "Admin: set another player's PvP on or off");

        plugin.Commands.Register(
            Command.Literal("pvpstatus").Permission("pvp.status")
                .Executes(ctx => Status(plugin, ctx, ctx.RequirePlayer()))
                .Then(Command.Argument("player", Arg.Player()).Permission("pvp.admin")
                    .Executes(ctx => Status(plugin, ctx, ctx.GetPlayer("player")))),
            description: "Show PvP, newbie protection, combat tag and kills");

        plugin.Commands.Register(
            Command.Literal("newbie").Permission("pvp.newbie")
                .Executes(ctx => Newbie(plugin, ctx))
                .Then(Command.Literal("disable").Executes(ctx => NewbieDisable(plugin, ctx))),
            description: "Show your newbie protection time left, or end it with /newbie disable");

        plugin.Commands.Register(
            Command.Literal("pvptag").Permission("pvp.status")
                .Executes(ctx => TagLeft(ctx, ctx.RequirePlayer()))
                .Then(Command.Argument("player", Arg.Player()).Permission("pvp.admin")
                    .Then(Command.Argument("seconds", Arg.Int(1, 3600)).Executes(ctx => AdminTag(ctx)))),
            description: "Show the combat tag time left; admin: tag a player for some seconds");

        plugin.Commands.Register(
            Command.Literal("untag").Permission("pvp.admin")
                .Executes(ctx => Untag(ctx, ctx.RequirePlayer()))
                .Then(Command.Argument("player", Arg.Player()).Executes(ctx => Untag(ctx, ctx.GetPlayer("player")))),
            description: "Admin: end a combat tag");
    }

    private static int Toggle(PvpPlugin plugin, CommandContext ctx, string? word)
    {
        Player player = ctx.RequirePlayer();
        if (!plugin.TryState(player, out PvpState state)) throw new CommandFailure("Your PvP state is not loaded yet, try again");
        bool target = word is null ? !state.PvpOn : ParseState(word);
        if (target == state.PvpOn)
        {
            ctx.Reply($"PvP is already {OnOff(target)}");
            return 1;
        }
        if (state.Tagged) throw new CommandFailure("You cannot change your PvP state while in combat");
        long now = (long)Server.CurrentTick;
        if (!PvpRules.CanToggle(in state, now, out int wait)) throw new CommandFailure($"Wait {wait} s before changing PvP again");

        state.PvpOn = target;
        state.ToggleReadyTick = PvpRules.ToggleCooldownEnds(now, plugin.Settings);
        plugin.Push(player, ref state, now);
        plugin.SaveState(player, state);
        ctx.Reply($"PvP is now {OnOff(target)}");
        return 1;
    }

    private static int AdminSet(PvpPlugin plugin, CommandContext ctx)
    {
        Player target = ctx.GetPlayer("player");
        if (!plugin.TryState(target, out PvpState state)) throw new CommandFailure("That player's PvP state is not loaded");
        state.PvpOn = ParseState(ctx.GetString("state"));
        plugin.Push(target, ref state, (long)Server.CurrentTick);
        plugin.SaveState(target, state);
        ctx.Reply($"{target.Name}: PvP {OnOff(state.PvpOn)}");
        return 1;
    }

    private static int Status(PvpPlugin plugin, CommandContext ctx, Player target)
    {
        if (!plugin.TryState(target, out PvpState state)) throw new CommandFailure("That player's PvP state is not loaded");
        string newbie = PvpRules.IsNewbie(in state)
            ? PvpPlugin.FormatSeconds(state.NewbieTicksLeft / PvpRules.TicksPerSecond)
            : "none";
        string tag = state.Tagged ? ", in combat" : "";
        ctx.Reply($"{target.Name}: PvP {OnOff(state.PvpOn)}, newbie {newbie}, kills {state.Kills}, deaths {state.Deaths}, streak {state.Streak}{tag}");
        return 1;
    }

    private static int Newbie(PvpPlugin plugin, CommandContext ctx)
    {
        Player player = ctx.RequirePlayer();
        if (!plugin.TryState(player, out PvpState state)) throw new CommandFailure("Your PvP state is not loaded yet, try again");
        if (!PvpRules.IsNewbie(in state))
        {
            ctx.Reply("You are not under newbie protection");
            return 1;
        }
        ctx.Reply($"Newbie protection: {PvpPlugin.FormatSeconds(state.NewbieTicksLeft / PvpRules.TicksPerSecond)} of play time left");
        return 1;
    }

    private static int NewbieDisable(PvpPlugin plugin, CommandContext ctx)
    {
        Player player = ctx.RequirePlayer();
        if (!plugin.TryState(player, out PvpState state)) throw new CommandFailure("Your PvP state is not loaded yet, try again");
        if (!PvpRules.IsNewbie(in state)) throw new CommandFailure("You are not under newbie protection");
        state.NewbieTicksLeft = 0;
        plugin.Push(player, ref state, (long)Server.CurrentTick);
        plugin.SaveState(player, state);
        ctx.Reply("Newbie protection ended; you can now fight other players");
        return 1;
    }

    private static int TagLeft(CommandContext ctx, Player player)
    {
        TimeSpan left = Combat.Remaining(player);
        if (left <= TimeSpan.Zero)
        {
            ctx.Reply("You are not in combat");
            return 1;
        }
        long seconds = (long)Math.Ceiling(left.TotalSeconds);
        ctx.Reply($"In combat: {seconds} s left");
        return 1;
    }

    private static int AdminTag(CommandContext ctx)
    {
        Player target = ctx.GetPlayer("player");
        int seconds = ctx.GetInt("seconds");
        if (!Combat.Tag(target, TimeSpan.FromSeconds(seconds))) throw new CommandFailure("That player cannot be tagged");
        ctx.Reply($"{target.Name} is tagged for {seconds} s");
        return 1;
    }

    private static int Untag(CommandContext ctx, Player target)
    {
        if (!Combat.Untag(target))
        {
            ctx.Reply($"{target.Name} is not in combat");
            return 1;
        }
        ctx.Reply($"{target.Name}: combat tag ended");
        return 1;
    }

    private static bool ParseState(string word)
    {
        if (string.Equals(word, "on", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(word, "off", StringComparison.OrdinalIgnoreCase)) return false;
        throw new CommandFailure("Use on or off");
    }

    private static string OnOff(bool on) => on ? "on" : "off";
}
