using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pvp.Core;

// Our own config shape (snake_case keys). Defaults are the values a fresh install writes to plugins/pvp/data/config.json.
public sealed class PvpConfig
{
    [JsonPropertyName("pvp")] public PvpSection Pvp { get; set; } = new();
    [JsonPropertyName("combat_tag")] public CombatTagSection CombatTag { get; set; } = new();
    [JsonPropertyName("newbie")] public NewbieSection Newbie { get; set; } = new();
    [JsonPropertyName("respawn")] public RespawnSection Respawn { get; set; } = new();
    [JsonPropertyName("worlds")] public WorldsSection Worlds { get; set; } = new();
}

public sealed class PvpSection
{
    /// <summary>PvP state of a player the first time he joins (later joins keep his choice).</summary>
    [JsonPropertyName("default_on")] public bool DefaultOn { get; set; } = true;
    /// <summary>Seconds between two toggles of one player.</summary>
    [JsonPropertyName("toggle_cooldown_s")] public int ToggleCooldownSeconds { get; set; } = 10;
}

public sealed class CombatTagSection
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    /// <summary>Seconds a PvP hit keeps both sides tagged; a new hit refreshes it.</summary>
    [JsonPropertyName("duration_s")] public int DurationSeconds { get; set; } = 15;
    /// <summary>Commands (first word, without the slash) a tagged player may still run.</summary>
    [JsonPropertyName("allowed_commands")] public string[] AllowedCommands { get; set; } =
        ["pvp", "pvpstatus", "pvptag", "newbie", "help", "msg", "tell", "w", "r"];
}

public sealed class NewbieSection
{
    /// <summary>Play time (seconds online) a new player is protected for. 0 disables newbie protection.</summary>
    [JsonPropertyName("duration_s")] public int DurationSeconds { get; set; } = 1800;
    /// <summary>Whether newbies are also safe from mobs.</summary>
    [JsonPropertyName("protect_from_mobs")] public bool ProtectFromMobs { get; set; } = true;
}

public sealed class RespawnSection
{
    /// <summary>Seconds after a respawn in which the player takes no PvP damage. 0 disables it.</summary>
    [JsonPropertyName("protect_s")] public int ProtectSeconds { get; set; } = 5;
}

public sealed class WorldsSection
{
    /// <summary>Worlds (by key, e.g. "minecraft:overworld") where PvP is off.</summary>
    [JsonPropertyName("pvp_off")] public string[] PvpOff { get; set; } = [];
}

[JsonSerializable(typeof(PvpConfig))]
[JsonSerializable(typeof(StoreFile))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
public partial class PvpJson : JsonSerializerContext
{
}

// Loads plugins/pvp/data/config.json. A missing file is written with the defaults; a broken one is kept as .bad and the defaults are used.
public static class ConfigFile
{
    public static PvpConfig Load(string path, out string? warning)
    {
        warning = null;
        if (!File.Exists(path))
        {
            AtomicFile.Write(path, JsonSerializer.Serialize(new PvpConfig(), PvpJson.Default.PvpConfig));
            return new PvpConfig();
        }
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), PvpJson.Default.PvpConfig) ?? new PvpConfig();
        }
        catch (JsonException e)
        {
            File.Move(path, path + ".bad", overwrite: true);
            warning = $"config.json was unreadable ({e.Message}); kept as config.json.bad, defaults in use";
            return new PvpConfig();
        }
    }
}
