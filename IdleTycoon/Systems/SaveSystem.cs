using IdleTycoon.Core.GameState;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleTycoon.Systems;

public static class SaveSystem
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Save(GameState state, string path)
    {
        var json = JsonSerializer.Serialize(state, JsonOpts);
        File.WriteAllText(path, json);
    }

    public static GameState? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<GameState>(json, JsonOpts);
            return state;
        }
        catch
        {
            return null;
        }
    }
}
