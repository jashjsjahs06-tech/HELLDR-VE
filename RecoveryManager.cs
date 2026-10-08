using System;
using System.IO;
using System.Text.Json;

namespace HELLDRIVE;

public sealed class RecoveryManager
{
    private readonly string _file;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public RecoveryManager(string? root = null)
    {
        root ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HELLDRIVE");

        Directory.CreateDirectory(root);
        _file = Path.Combine(root, "last-known-good.json");
    }

    public void Save(string profileName, string preset)
    {
        var data = new RecoveryState(profileName, preset, DateTime.Now);
        File.WriteAllText(_file, JsonSerializer.Serialize(data, _options));
    }

    public RecoveryState? Load()
    {
        if (!File.Exists(_file)) return null;
        try { return JsonSerializer.Deserialize<RecoveryState>(File.ReadAllText(_file)); }
        catch { return null; }
    }

    public void Clear()
    {
        if (File.Exists(_file)) File.Delete(_file);
    }
}

public sealed record RecoveryState(string ProfileName, string Preset, DateTime SavedAt);