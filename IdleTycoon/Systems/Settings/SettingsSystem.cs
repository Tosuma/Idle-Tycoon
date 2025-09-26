using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleTycoon.Systems.Settings;

public static class SettingsSystem
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var s = JsonSerializer.Deserialize<Settings>(json, JsonOpts);
                if (s != null) return s;
            }
        }
        catch { }
        return new Settings();
    }

    public static void Save(Settings s, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(s, JsonOpts);
        File.WriteAllText(path, json);
    }
}
