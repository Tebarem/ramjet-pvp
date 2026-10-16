using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pvp.Core;

// The durable record of one player (plugins/pvp/data/players.json, keyed by the profile UUID text).
public sealed class PlayerRecord
{
    [JsonPropertyName("pvp_on")] public bool PvpOn { get; set; }
    [JsonPropertyName("newbie_ticks_left")] public int NewbieTicksLeft { get; set; }
    [JsonPropertyName("kills")] public int Kills { get; set; }
    [JsonPropertyName("deaths")] public int Deaths { get; set; }
    [JsonPropertyName("streak")] public int Streak { get; set; }
}

public sealed class StoreFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("players")] public Dictionary<string, PlayerRecord> Players { get; set; } = [];
}

// The players of the plugin, in memory. Reads and writes happen on the tick thread; Serialize gives the text to write off it.
public sealed class PvpStore
{
    private readonly Dictionary<string, PlayerRecord> _players;

    // Set when the file existed but could not be read: it was kept as players.json.bad and the store starts empty.
    public string? LoadWarning { get; }

    public PvpStore(string path)
    {
        Path = path;
        _players = new Dictionary<string, PlayerRecord>(StringComparer.Ordinal);
        if (!File.Exists(path)) return;
        try
        {
            StoreFile? file = JsonSerializer.Deserialize(File.ReadAllText(path), PvpJson.Default.StoreFile);
            if (file?.Players is { } players)
                foreach (var (uuid, record) in players) _players[uuid] = record;
        }
        catch (JsonException e)
        {
            File.Move(path, path + ".bad", overwrite: true);
            LoadWarning = $"players.json was unreadable ({e.Message}); kept as players.json.bad, starting empty";
        }
    }

    public string Path { get; }

    public int Count => _players.Count;

    // A new player gets the newbie timer and the default PvP state; a known one keeps his record. True when the record was just created.
    public bool GetOrCreate(string uuid, int newbieTicks, bool pvpDefaultOn, out PlayerRecord record)
    {
        if (_players.TryGetValue(uuid, out record!)) return false;
        record = new PlayerRecord { PvpOn = pvpDefaultOn, NewbieTicksLeft = newbieTicks };
        _players[uuid] = record;
        return true;
    }

    public void Put(string uuid, PlayerRecord record) => _players[uuid] = record;

    public string Serialize() => JsonSerializer.Serialize(new StoreFile { Players = _players }, PvpJson.Default.StoreFile);

    public void Save() => AtomicFile.Write(Path, Serialize());
}

// Writes a file so that a crash leaves the old or the new content: the text goes to a temp file beside it, which then replaces the target.
public static class AtomicFile
{
    public static void Write(string path, string text)
    {
        string? dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
