using System.Diagnostics;

namespace HellDrive;

public sealed class StatsMonitor : IDisposable
{
    private Process? _process;
    private DateTime _last;
    private TimeSpan _lastCpu;

    public void Attach(Process process)
    {
        _process = process;
        _last = DateTime.UtcNow;
        _lastCpu = process.TotalProcessorTime;
    }

    public LiveStats Read()
    {
        if (_process == null || _process.HasExited) return new LiveStats();
        double cpu = 0;
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - _last).TotalSeconds;
            var current = _process.TotalProcessorTime;
            if (elapsed > 0) cpu = (current - _lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100;
            _last = now;
            _lastCpu = current;
            var ram = _process.WorkingSet64 / 1024d / 1024d;
            return new LiveStats { CpuPercent = Math.Clamp(cpu, 0, 100), RamMb = ram, WorkingSetMb = ram, GpuPercent = 0, TemperatureC = 0, Fps = 0 };
        }
        catch { return new LiveStats(); }
    }

    public void Dispose() { }
}
