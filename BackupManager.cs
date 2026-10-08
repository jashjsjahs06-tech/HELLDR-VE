using System.Text.Json;

namespace HellDrive;

public sealed class BackupManager
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "Backups");
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public string Create(GameProfile profile, string priorityBefore, IEnumerable<int> suspended)
    {
        Directory.CreateDirectory(_root);
        var safe = string.Join("_", profile.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safe)) safe = "Profile";
        var folder = Path.Combine(_root, $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(folder);
        var snapshot = new BackupSnapshot
        {
            GamePath = profile.GamePath,
            ProfileName = profile.Name,
            PriorityBefore = priorityBefore,
            SuspendedProcessIds = suspended.ToList()
        };
        File.WriteAllText(Path.Combine(folder, "snapshot.json"), JsonSerializer.Serialize(snapshot, _json));
        return folder;
    }

    public BackupSnapshot? LoadLatest()
    {
        if (!Directory.Exists(_root)) return null;
        var file = Directory.GetDirectories(_root)
            .Select(x => Path.Combine(x, "snapshot.json"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (file == null) return null;
        try { return JsonSerializer.Deserialize<BackupSnapshot>(File.ReadAllText(file)); } catch { return null; }
    }

    public string Root => _root;
}
