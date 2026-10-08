using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HELLDRIVE;

public enum SessionEventType
{
    GameStart,
    GameExit,
    Telemetry,
    FrameDrop,
    CpuSpike,
    GpuSpike,
    MemorySpike,
    TemperatureSpike,
    ProcessEvent,
    Optimization,
    Warning,
    Custom
}

public sealed class SessionEvent
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public double ElapsedSeconds { get; init; }
    public SessionEventType Type { get; init; }
    public string Message { get; init; } = "";

    public double? Fps { get; init; }
    public double? FrameTimeMs { get; init; }
    public double? CpuPercent { get; init; }
    public double? GpuPercent { get; init; }
    public double? RamMb { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? GpuTemperatureC { get; init; }

    public string? ProcessName { get; init; }
    public int? ProcessId { get; init; }
}

public sealed class SessionSample
{
    public DateTime Timestamp { get; init; }
    public double ElapsedSeconds { get; init; }
    public double? Fps { get; init; }
    public double? FrameTimeMs { get; init; }
    public double? CpuPercent { get; init; }
    public double? GpuPercent { get; init; }
    public double? RamMb { get; init; }
    public double? CpuTemperatureC { get; init; }
    public double? GpuTemperatureC { get; init; }
}

public sealed class SessionReport
{
    public string GameId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime? EndedAt { get; init; }
    public TimeSpan Duration { get; init; }

    public double? AverageFps { get; init; }
    public double? MinimumFps { get; init; }
    public double? MaximumFps { get; init; }

    public double? AverageFrameTimeMs { get; init; }
    public double? MaximumFrameTimeMs { get; init; }

    public double? AverageCpuPercent { get; init; }
    public double? MaximumCpuPercent { get; init; }

    public double? AverageGpuPercent { get; init; }
    public double? MaximumGpuPercent { get; init; }

    public double? AverageRamMb { get; init; }
    public double? MaximumRamMb { get; init; }

    public int FrameDropCount { get; init; }
    public int CpuSpikeCount { get; init; }
    public int GpuSpikeCount { get; init; }
    public int MemorySpikeCount { get; init; }
    public int TemperatureSpikeCount { get; init; }

    public int TotalEvents { get; init; }
    public int Score { get; init; }
    public List<SessionEvent> Events { get; init; } = new();
}

/// <summary>
/// Records a complete game performance session.
/// Feed this class real telemetry values from the live stats layer.
/// It never fabricates FPS/GPU/temperature measurements.
/// </summary>
public sealed class GameSessionRecorder : IDisposable
{
    private readonly object _sync = new();
    private readonly List<SessionSample> _samples = new();
    private readonly List<SessionEvent> _events = new();

    private readonly string _storageDirectory;
    private readonly string _sessionDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    private DateTime _startedAt;
    private DateTime? _endedAt;
    private bool _active;
    private bool _disposed;

    public string GameId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";

    public bool IsRecording
    {
        get { lock (_sync) return _active; }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_sync)
            {
                if (!_active && _endedAt.HasValue)
                    return _endedAt.Value - _startedAt;

                return _active ? DateTime.Now - _startedAt : TimeSpan.Zero;
            }
        }
    }

    public event EventHandler<SessionEvent>? EventRecorded;

    public GameSessionRecorder(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HELLDRIVE");

        _sessionDirectory = Path.Combine(_storageDirectory, "sessions");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public void Start(string gameId, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            throw new ArgumentException("Game ID cannot be empty.", nameof(gameId));

        lock (_sync)
        {
            ThrowIfDisposed();

            if (_active)
                throw new InvalidOperationException("A session is already recording.");

            GameId = Path.GetFileNameWithoutExtension(gameId.Trim());
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? GameId
                : displayName.Trim();

            _startedAt = DateTime.Now;
            _endedAt = null;
            _samples.Clear();
            _events.Clear();
            _active = true;
        }

        RecordEvent(
            SessionEventType.GameStart,
            "GAME START");
    }

    public void RecordTelemetry(
        double? fps = null,
        double? frameTimeMs = null,
        double? cpuPercent = null,
        double? gpuPercent = null,
        double? ramMb = null,
        double? cpuTemperatureC = null,
        double? gpuTemperatureC = null)
    {
        lock (_sync)
        {
            if (!_active)
                return;

            DateTime now = DateTime.Now;
            double elapsed = (now - _startedAt).TotalSeconds;

            var sample = new SessionSample
            {
                Timestamp = now,
                ElapsedSeconds = elapsed,
                Fps = Clean(fps),
                FrameTimeMs = Clean(frameTimeMs),
                CpuPercent = Clean(cpuPercent),
                GpuPercent = Clean(gpuPercent),
                RamMb = Clean(ramMb),
                CpuTemperatureC = Clean(cpuTemperatureC),
                GpuTemperatureC = Clean(gpuTemperatureC)
            };

            _samples.Add(sample);

            DetectAndRecordEvents(sample);
        }
    }

    public void RecordProcessEvent(
        string message,
        string processName,
        int? processId = null)
    {
        RecordEvent(
            SessionEventType.ProcessEvent,
            message,
            processName: processName,
            processId: processId);
    }

    public void RecordOptimization(string message)
    {
        RecordEvent(
            SessionEventType.Optimization,
            message);
    }

    public void RecordWarning(string message)
    {
        RecordEvent(
            SessionEventType.Warning,
            message);
    }

    public void RecordCustomEvent(string message)
    {
        RecordEvent(
            SessionEventType.Custom,
            message);
    }

    public SessionReport Stop()
    {
        lock (_sync)
        {
            if (!_active)
                return BuildReportUnsafe();

            _endedAt = DateTime.Now;
            _active = false;
        }

        RecordEvent(
            SessionEventType.GameExit,
            "GAME EXIT");

        SessionReport report;

        lock (_sync)
            report = BuildReportUnsafe();

        SaveReport(report);

        return report;
    }

    public SessionReport GetLiveReport()
    {
        lock (_sync)
            return BuildReportUnsafe();
    }

    private void DetectAndRecordEvents(SessionSample sample)
    {
        if (sample.Fps.HasValue && sample.Fps.Value < 30)
        {
            RecordEvent(
                SessionEventType.FrameDrop,
                $"FPS DROP: {sample.Fps.Value:F0}",
                sample);
        }

        if (sample.FrameTimeMs.HasValue && sample.FrameTimeMs.Value >= 25)
        {
            RecordEvent(
                SessionEventType.FrameDrop,
                $"FRAME TIME SPIKE: {sample.FrameTimeMs.Value:F1} ms",
                sample);
        }

        if (sample.CpuPercent.HasValue && sample.CpuPercent.Value >= 95)
        {
            RecordEvent(
                SessionEventType.CpuSpike,
                $"CPU SPIKE: {sample.CpuPercent.Value:F0}%",
                sample);
        }

        if (sample.GpuPercent.HasValue && sample.GpuPercent.Value >= 98)
        {
            RecordEvent(
                SessionEventType.GpuSpike,
                $"GPU SPIKE: {sample.GpuPercent.Value:F0}%",
                sample);
        }

        if (sample.RamMb.HasValue && sample.RamMb.Value >= 16384)
        {
            RecordEvent(
                SessionEventType.MemorySpike,
                $"MEMORY HIGH: {sample.RamMb.Value:F0} MB",
                sample);
        }

        if (sample.CpuTemperatureC.HasValue &&
            sample.CpuTemperatureC.Value >= 90)
        {
            RecordEvent(
                SessionEventType.TemperatureSpike,
                $"CPU TEMPERATURE HIGH: {sample.CpuTemperatureC.Value:F0}°C",
                sample);
        }

        if (sample.GpuTemperatureC.HasValue &&
            sample.GpuTemperatureC.Value >= 90)
        {
            RecordEvent(
                SessionEventType.TemperatureSpike,
                $"GPU TEMPERATURE HIGH: {sample.GpuTemperatureC.Value:F0}°C",
                sample);
        }
    }

    private void RecordEvent(
        SessionEventType type,
        string message,
        SessionSample? sample = null,
        string? processName = null,
        int? processId = null)
    {
        SessionEvent entry;

        lock (_sync)
        {
            if (_startedAt == default)
                return;

            DateTime now = DateTime.Now;

            entry = new SessionEvent
            {
                Timestamp = now,
                ElapsedSeconds = (now - _startedAt).TotalSeconds,
                Type = type,
                Message = message,
                Fps = sample?.Fps,
                FrameTimeMs = sample?.FrameTimeMs,
                CpuPercent = sample?.CpuPercent,
                GpuPercent = sample?.GpuPercent,
                RamMb = sample?.RamMb,
                CpuTemperatureC = sample?.CpuTemperatureC,
                GpuTemperatureC = sample?.GpuTemperatureC,
                ProcessName = processName,
                ProcessId = processId
            };

            _events.Add(entry);
        }

        try
        {
            EventRecorded?.Invoke(this, entry);
        }
        catch
        {
            // UI/event subscribers must never break recording.
        }
    }

    private SessionReport BuildReportUnsafe()
    {
        DateTime? end = _endedAt;
        TimeSpan duration =
            _startedAt == default
                ? TimeSpan.Zero
                : (end ?? DateTime.Now) - _startedAt;

        double? Average(Func<SessionSample, double?> selector)
        {
            double[] values = _samples
                .Select(selector)
                .Where(x => x.HasValue && !double.IsNaN(x.Value))
                .Select(x => x!.Value)
                .ToArray();

            return values.Length == 0 ? null : values.Average();
        }

        double? Min(Func<SessionSample, double?> selector)
        {
            double[] values = _samples
                .Select(selector)
                .Where(x => x.HasValue && !double.IsNaN(x.Value))
                .Select(x => x!.Value)
                .ToArray();

            return values.Length == 0 ? null : values.Min();
        }

        double? Max(Func<SessionSample, double?> selector)
        {
            double[] values = _samples
                .Select(selector)
                .Where(x => x.HasValue && !double.IsNaN(x.Value))
                .Select(x => x!.Value)
                .ToArray();

            return values.Length == 0 ? null : values.Max();
        }

        int Count(SessionEventType type) =>
            _events.Count(x => x.Type == type);

        int score = CalculateScore();

        return new SessionReport
        {
            GameId = GameId,
            DisplayName = DisplayName,
            StartedAt = _startedAt,
            EndedAt = end,
            Duration = duration,

            AverageFps = Average(x => x.Fps),
            MinimumFps = Min(x => x.Fps),
            MaximumFps = Max(x => x.Fps),

            AverageFrameTimeMs = Average(x => x.FrameTimeMs),
            MaximumFrameTimeMs = Max(x => x.FrameTimeMs),

            AverageCpuPercent = Average(x => x.CpuPercent),
            MaximumCpuPercent = Max(x => x.CpuPercent),

            AverageGpuPercent = Average(x => x.GpuPercent),
            MaximumGpuPercent = Max(x => x.GpuPercent),

            AverageRamMb = Average(x => x.RamMb),
            MaximumRamMb = Max(x => x.RamMb),

            FrameDropCount = Count(SessionEventType.FrameDrop),
            CpuSpikeCount = Count(SessionEventType.CpuSpike),
            GpuSpikeCount = Count(SessionEventType.GpuSpike),
            MemorySpikeCount = Count(SessionEventType.MemorySpike),
            TemperatureSpikeCount = Count(SessionEventType.TemperatureSpike),

            TotalEvents = _events.Count,
            Score = score,
            Events = _events.ToList()
        };
    }

    private int CalculateScore()
    {
        int score = 100;

        score -= Math.Min(40, _events.Count(x =>
            x.Type == SessionEventType.FrameDrop) * 4);

        score -= Math.Min(20, _events.Count(x =>
            x.Type == SessionEventType.CpuSpike) * 2);

        score -= Math.Min(20, _events.Count(x =>
            x.Type == SessionEventType.GpuSpike) * 2);

        score -= Math.Min(20, _events.Count(x =>
            x.Type == SessionEventType.MemorySpike) * 2);

        score -= Math.Min(30, _events.Count(x =>
            x.Type == SessionEventType.TemperatureSpike) * 5);

        return Math.Clamp(score, 0, 100);
    }

    private void SaveReport(SessionReport report)
    {
        try
        {
            Directory.CreateDirectory(_sessionDirectory);

            string safeGame = MakeSafeFileName(
                string.IsNullOrWhiteSpace(report.GameId)
                    ? "Unknown"
                    : report.GameId);

            string fileName =
                $"{report.StartedAt:yyyyMMdd_HHmmss}_{safeGame}.json";

            string destination = Path.Combine(
                _sessionDirectory,
                fileName);

            string temp = destination + ".tmp";
            string json = JsonSerializer.Serialize(report, _jsonOptions);

            File.WriteAllText(temp, json);
            File.Move(temp, destination, true);
        }
        catch
        {
            // Recording should remain functional even if disk writing fails.
        }
    }

    private static string MakeSafeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }

    private static double? Clean(double? value)
    {
        if (!value.HasValue ||
            double.IsNaN(value.Value) ||
            double.IsInfinity(value.Value))
        {
            return null;
        }

        return Math.Max(0, value.Value);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GameSessionRecorder));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (IsRecording)
        {
            try
            {
                Stop();
            }
            catch
            {
                // Best-effort finalization.
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
