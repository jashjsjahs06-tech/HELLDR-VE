using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace HELLDRIVE;

public enum ProcessRadarCategory
{
    Game,
    System,
    User,
    Background,
    Unknown
}

public enum ProcessImpact
{
    Normal,
    Low,
    HighCpu,
    HighMemory,
    HighImpact
}

public sealed class ProcessRadarEntry
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public ProcessRadarCategory Category { get; init; }
    public ProcessImpact Impact { get; init; }
    public double CpuPercent { get; init; }
    public double MemoryMb { get; init; }
    public int Threads { get; init; }
    public string Priority { get; init; } = "Unknown";
    public DateTime? StartTime { get; init; }
    public TimeSpan Runtime { get; init; }
    public bool CanManage { get; init; }
}

/// <summary>
/// Real-time process inspection for the HELLDR-VE Process Radar.
/// It does not kill or suspend anything automatically.
/// </summary>
public sealed class ProcessRadar : IDisposable
{
    private readonly object _sync = new();
    private readonly Timer _timer;
    private readonly HashSet<int> _gamePids = new();
    private readonly Dictionary<int, CpuSample> _cpuSamples = new();
    private bool _disposed;

    public event EventHandler<IReadOnlyList<ProcessRadarEntry>>? SnapshotUpdated;

    public TimeSpan RefreshInterval { get; }

    public ProcessRadar(TimeSpan? refreshInterval = null)
    {
        RefreshInterval = refreshInterval ?? TimeSpan.FromSeconds(1);

        _timer = new Timer(
            _ => Refresh(),
            null,
            TimeSpan.Zero,
            RefreshInterval);
    }

    public void SetGameProcess(int pid)
    {
        lock (_sync)
        {
            _gamePids.Clear();

            if (pid > 0)
                _gamePids.Add(pid);
        }
    }

    public void AddGameProcess(int pid)
    {
        if (pid <= 0)
            return;

        lock (_sync)
            _gamePids.Add(pid);
    }

    public IReadOnlyList<ProcessRadarEntry> GetSnapshot()
    {
        return BuildSnapshot();
    }

    public ProcessRadarEntry? GetProcess(int pid)
    {
        return BuildSnapshot().FirstOrDefault(x => x.Id == pid);
    }

    private void Refresh()
    {
        if (_disposed)
            return;

        try
        {
            var snapshot = BuildSnapshot();
            SnapshotUpdated?.Invoke(this, snapshot);
        }
        catch
        {
            // Radar must never crash the application.
        }
    }

    private List<ProcessRadarEntry> BuildSnapshot()
    {
        var result = new List<ProcessRadarEntry>();
        Process[] processes;

        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return result;
        }

        try
        {
            HashSet<int> gamePids;

            lock (_sync)
                gamePids = new HashSet<int>(_gamePids);

            DateTime now = DateTime.Now;
            HashSet<int> seen = new();

            foreach (Process process in processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    int pid = process.Id;

                    // Skip duplicate PID entries and obvious idle/system noise.
                    if (!seen.Add(pid))
                        continue;

                    double memoryMb = process.WorkingSet64 / 1024d / 1024d;
                    int threads = SafeThreadCount(process);
                    string name = SafeName(process);
                    string path = SafePath(process);
                    string priority = SafePriority(process);
                    DateTime? startTime = SafeStartTime(process);

                    double cpu = CalculateCpu(pid, process, now);
                    ProcessRadarCategory category =
                        Categorize(process, name, path, gamePids);

                    ProcessImpact impact = CalculateImpact(cpu, memoryMb);

                    result.Add(new ProcessRadarEntry
                    {
                        Id = pid,
                        Name = name,
                        ExecutablePath = path,
                        Category = category,
                        Impact = impact,
                        CpuPercent = Math.Round(cpu, 1),
                        MemoryMb = Math.Round(memoryMb, 1),
                        Threads = threads,
                        Priority = priority,
                        StartTime = startTime,
                        Runtime = startTime.HasValue
                            ? now - startTime.Value
                            : TimeSpan.Zero,
                        CanManage = IsManageable(process, category)
                    });
                }
                catch
                {
                    // Individual process access can fail because of permissions.
                }
                finally
                {
                    process.Dispose();
                }
            }

            CleanupSamples(seen);

            return result
                .OrderByDescending(x => x.Category == ProcessRadarCategory.Game)
                .ThenByDescending(x => x.CpuPercent)
                .ThenByDescending(x => x.MemoryMb)
                .ToList();
        }
        finally
        {
            // Processes are disposed individually above.
        }
    }

    private double CalculateCpu(int pid, Process process, DateTime now)
    {
        try
        {
            TimeSpan total = process.TotalProcessorTime;

            lock (_sync)
            {
                if (_cpuSamples.TryGetValue(pid, out CpuSample? previous))
                {
                    double wallSeconds = (now - previous.Timestamp).TotalSeconds;

                    if (wallSeconds > 0)
                    {
                        double cpuSeconds =
                            (total - previous.CpuTime).TotalSeconds;

                        // Normalize against logical CPU count.
                        double percent =
                            (cpuSeconds / wallSeconds) *
                            100.0 /
                            Math.Max(1, Environment.ProcessorCount);

                        _cpuSamples[pid] = new CpuSample(total, now);

                        return Math.Clamp(percent, 0, 100);
                    }
                }

                _cpuSamples[pid] = new CpuSample(total, now);
            }
        }
        catch
        {
            // Access denied or process exited.
        }

        return 0;
    }

    private void CleanupSamples(HashSet<int> activePids)
    {
        lock (_sync)
        {
            foreach (int pid in _cpuSamples.Keys
                         .Where(pid => !activePids.Contains(pid))
                         .ToList())
            {
                _cpuSamples.Remove(pid);
            }
        }
    }

    private static ProcessRadarCategory Categorize(
        Process process,
        string name,
        string path,
        HashSet<int> gamePids)
    {
        if (gamePids.Contains(process.Id))
            return ProcessRadarCategory.Game;

        string normalizedPath = path.ToLowerInvariant();
        string normalizedName = name.ToLowerInvariant();

        if (normalizedPath.Contains(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                    .ToLowerInvariant()))
        {
            return ProcessRadarCategory.System;
        }

        if (normalizedName is "svchost" or "services" or "lsass" or
            "wininit" or "csrss" or "smss" or "winlogon" or
            "dwm" or "runtimebroker")
        {
            return ProcessRadarCategory.System;
        }

        if (normalizedPath.Contains("program files") ||
            normalizedPath.Contains("appdata") ||
            normalizedPath.Contains("users"))
        {
            return ProcessRadarCategory.User;
        }

        return ProcessRadarCategory.Background;
    }

    private static ProcessImpact CalculateImpact(double cpu, double memoryMb)
    {
        if (cpu >= 25 && memoryMb >= 2048)
            return ProcessImpact.HighImpact;

        if (cpu >= 25)
            return ProcessImpact.HighCpu;

        if (memoryMb >= 2048)
            return ProcessImpact.HighMemory;

        if (cpu < 2 && memoryMb < 512)
            return ProcessImpact.Low;

        return ProcessImpact.Normal;
    }

    private static bool IsManageable(
        Process process,
        ProcessRadarCategory category)
    {
        // Never expose critical system processes as manageable.
        if (category == ProcessRadarCategory.System)
            return false;

        try
        {
            return !process.HasExited &&
                   process.Id != Environment.ProcessId;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeName(Process process)
    {
        try
        {
            return process.ProcessName + ".exe";
        }
        catch
        {
            return "Unknown.exe";
        }
    }

    private static string SafePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static int SafeThreadCount(Process process)
    {
        try
        {
            return process.Threads.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafePriority(Process process)
    {
        try
        {
            return process.PriorityClass.ToString();
        }
        catch
        {
            return "Unknown";
        }
    }

    private static DateTime? SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();

        lock (_sync)
            _cpuSamples.Clear();

        GC.SuppressFinalize(this);
    }

    private sealed record CpuSample(
        TimeSpan CpuTime,
        DateTime Timestamp);
}
