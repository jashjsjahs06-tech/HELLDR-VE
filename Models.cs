using System.Text.Json.Serialization;

namespace HellDrive;

public enum PresetLevel { Safe, Balanced, Aggressive, Ultra }

public sealed class GameProfile
{
    public string Name { get; set; } = "New Profile";
    public string GamePath { get; set; } = "";
    public PresetLevel Preset { get; set; } = PresetLevel.Balanced;
    public string Resolution { get; set; } = "1280 x 720";
    public string Priority { get; set; } = "Above Normal";
    public bool ManageBackground { get; set; }
    public bool AutomaticBackup { get; set; } = true;
    public List<string> BackgroundProcesses { get; set; } = new();
}

public sealed class AppSettings
{
    public List<GameProfile> Profiles { get; set; } = new();
    public string LastProfile { get; set; } = "";
}

public sealed record OptimizationAction(string Time, string Action, string Detail, bool Reversible);

public sealed class BackupSnapshot
{
    public DateTime Created { get; set; } = DateTime.Now;
    public string GamePath { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string PriorityBefore { get; set; } = "Normal";
    public List<int> SuspendedProcessIds { get; set; } = new();
}

public sealed class LiveStats
{
    public double CpuPercent { get; init; }
    public double RamMb { get; init; }
    public double WorkingSetMb { get; init; }
    public double GpuPercent { get; init; }
    public double TemperatureC { get; init; }
    public double Fps { get; init; }
    public string FpsText => Fps > 0 ? $"{Fps:0}" : "N/A";
}
