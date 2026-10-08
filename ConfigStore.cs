using System.Text.Json;

namespace HellDrive;

public sealed class ConfigStore
{
    private readonly string _file;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public ConfigStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HELLDRIVE");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "profiles.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_file))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file), _options) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save(AppSettings settings) => File.WriteAllText(_file, JsonSerializer.Serialize(settings, _options));
}
