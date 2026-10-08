using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace HELLDRIVE;

/// <summary>
/// AI-style adaptive graphics controller for HELLDR-VE.
/// It learns the relationship between graphics quality and observed performance,
/// then selects the lowest quality reduction necessary to keep the target FPS.
/// 
/// This is an online statistical model, not a neural network. It deliberately
/// does not fabricate telemetry or modify game files by itself.
/// Game-specific setting changes are delegated to IGraphicsAdapter.
/// </summary>
public sealed class AIGraphicsReducer : IDisposable
{
    private readonly object _sync = new();
    private readonly string _modelPath;
    private readonly Timer _saveTimer;

    private readonly Dictionary<string, GamePerformanceModel> _models =
        new(StringComparer.OrdinalIgnoreCase);

    private GameGraphicsProfile? _profile;
    private IGraphicsAdapter? _adapter;

    private DateTime _lastDecisionUtc = DateTime.MinValue;
    private int _stableSamples;
    private double _lastFps;
    private bool _running;

    public AIGraphicsReducer(string? modelPath = null)
    {
        _modelPath = modelPath ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HELLDRIVE",
                "ai-graphics-model.json");

        Load();

        _saveTimer = new Timer(
            _ => Save(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    public event EventHandler<GraphicsDecisionEventArgs>? DecisionMade;
    public event EventHandler<GraphicsModelUpdatedEventArgs>? ModelUpdated;

    public GameGraphicsProfile? CurrentProfile
    {
        get { lock (_sync) return _profile; }
    }

    public GraphicsLevel CurrentLevel
    {
        get { lock (_sync) return _profile?.CurrentLevel ?? GraphicsLevel.Native; }
    }

    public void Start(GameGraphicsProfile profile, IGraphicsAdapter adapter)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        if (adapter == null) throw new ArgumentNullException(nameof(adapter));

        lock (_sync)
        {
            _profile = profile.Clone();
            _adapter = adapter;
            _running = true;
            _stableSamples = 0;
            _lastDecisionUtc = DateTime.MinValue;
        }
    }

    public void Stop(bool restoreNative = true)
    {
        IGraphicsAdapter? adapter;

        lock (_sync)
        {
            _running = false;
            adapter = _adapter;
            _adapter = null;
            _profile = null;
        }

        if (restoreNative && adapter != null)
        {
            try { adapter.RestoreBackup(); }
            catch { /* Restore should not crash the optimizer shutdown path. */ }
        }

        Save();
    }

    /// <summary>
    /// Feed real telemetry into the controller. Call this approximately once
    /// per second while the game is running.
    /// </summary>
    public GraphicsDecision RecordTelemetry(GraphicsTelemetry telemetry)
    {
        lock (_sync)
        {
            if (!_running || _profile == null || _adapter == null)
                return GraphicsDecision.None;

            if (telemetry.Fps <= 0)
                return GraphicsDecision.None;

            string gameId = _profile.GameId;
            var model = GetModel(gameId);

            model.Observe(
                _profile.CurrentLevel,
                telemetry,
                _profile.CurrentQualityMultiplier);

            _lastFps = telemetry.Fps;

            var now = DateTime.UtcNow;

            if ((now - _lastDecisionUtc) < _profile.DecisionCooldown)
                return GraphicsDecision.None;

            var decision = Decide(model, telemetry, now);

            if (decision.Action == GraphicsAction.None)
            {
                if (telemetry.Fps >= _profile.RecoveryFps)
                    _stableSamples++;
                else
                    _stableSamples = 0;

                return decision;
            }

            if (decision.Action == GraphicsAction.IncreaseQuality &&
                _stableSamples < _profile.StableSamplesBeforeRecovery)
            {
                return GraphicsDecision.None;
            }

            bool applied = ApplyDecision(decision);

            if (!applied)
                return GraphicsDecision.None;

            _lastDecisionUtc = now;
            _stableSamples = 0;

            DecisionMade?.Invoke(
                this,
                new GraphicsDecisionEventArgs(decision, telemetry));

            Save();

            return decision;
        }
    }

    public GamePerformanceModel GetGameModel(string gameId)
    {
        lock (_sync)
        {
            return GetModel(gameId).Clone();
        }
    }

    private GraphicsDecision Decide(
        GamePerformanceModel model,
        GraphicsTelemetry telemetry,
        DateTime now)
    {
        var profile = _profile!;

        double predictedAtCurrent = model.PredictFps(
            profile.CurrentLevel,
            telemetry);

        double target = profile.TargetFps;

        // Critical performance: make a larger quality step.
        if (telemetry.Fps < profile.CriticalFps ||
            telemetry.FrametimeMs > profile.CriticalFrametimeMs)
        {
            var lower = profile.GetNextLower(profile.CurrentLevel, largeStep: true);

            if (lower.HasValue)
            {
                return new GraphicsDecision(
                    GraphicsAction.DecreaseQuality,
                    lower.Value,
                    "Critical performance pressure.");
            }
        }

        // Normal pressure: only reduce quality when the model predicts that
        // the current level is unlikely to sustain the target.
        if (telemetry.Fps < profile.LowFpsThreshold ||
            predictedAtCurrent < target * 0.94)
        {
            var lower = profile.GetNextLower(profile.CurrentLevel, largeStep: false);

            if (lower.HasValue)
            {
                return new GraphicsDecision(
                    GraphicsAction.DecreaseQuality,
                    lower.Value,
                    $"Model predicts insufficient headroom ({predictedAtCurrent:F1} FPS).");
            }
        }

        // Recovery is deliberately conservative. We only climb when the
        // current performance is comfortably above target.
        if (telemetry.Fps >= profile.RecoveryFps &&
            telemetry.FrametimeMs <= profile.RecoveryFrametimeMs)
        {
            var higher = profile.GetNextHigher(profile.CurrentLevel);

            if (higher.HasValue)
            {
                double predictedHigher = model.PredictFps(higher.Value, telemetry);

                if (predictedHigher >= target * 0.96)
                {
                    return new GraphicsDecision(
                        GraphicsAction.IncreaseQuality,
                        higher.Value,
                        $"Model predicts safe recovery ({predictedHigher:F1} FPS).");
                }
            }
        }

        return GraphicsDecision.None;
    }

    private bool ApplyDecision(GraphicsDecision decision)
    {
        if (_profile == null || _adapter == null)
            return false;

        if (decision.Action == GraphicsAction.None)
            return false;

        try
        {
            if (!_adapter.ApplyLevel(decision.TargetLevel))
                return false;

            _profile.CurrentLevel = decision.TargetLevel;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private GamePerformanceModel GetModel(string gameId)
    {
        if (!_models.TryGetValue(gameId, out var model))
        {
            model = new GamePerformanceModel(gameId);
            _models[gameId] = model;
        }

        return model;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_modelPath))
                return;

            var json = File.ReadAllText(_modelPath);
            var data = JsonSerializer.Deserialize<
                Dictionary<string, GamePerformanceModel>>(json);

            if (data == null)
                return;

            lock (_sync)
            {
                _models.Clear();

                foreach (var pair in data)
                    _models[pair.Key] = pair.Value;
            }
        }
        catch
        {
            // A corrupt learning file must never prevent HELLDR-VE startup.
        }
    }

    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_modelPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            Dictionary<string, GamePerformanceModel> snapshot;

            lock (_sync)
            {
                snapshot = _models.ToDictionary(
                    p => p.Key,
                    p => p.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            File.WriteAllText(
                _modelPath,
                JsonSerializer.Serialize(snapshot, options));

            ModelUpdated?.Invoke(
                this,
                new GraphicsModelUpdatedEventArgs(_modelPath));
        }
        catch
        {
            // Learning persistence is optional. Never crash the optimizer.
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Stop(restoreNative: false);
        Save();
    }
}

public sealed class GameGraphicsProfile
{
    public string GameId { get; set; } = "UnknownGame";
    public string DisplayName { get; set; } = "Unknown Game";

    public double TargetFps { get; set; } = 60;
    public double LowFpsThreshold { get; set; } = 52;
    public double CriticalFps { get; set; } = 35;

    public double CriticalFrametimeMs { get; set; } = 28.6;
    public double RecoveryFrametimeMs { get; set; } = 17.0;

    public double RecoveryFps { get; set; } = 72;
    public int StableSamplesBeforeRecovery { get; set; } = 5;

    public TimeSpan DecisionCooldown { get; set; } =
        TimeSpan.FromSeconds(5);

    public GraphicsLevel CurrentLevel { get; set; } = GraphicsLevel.Native;

    public List<GraphicsLevelConfig> Levels { get; set; } = new();

    public double CurrentQualityMultiplier =>
        Levels.FirstOrDefault(x => x.Level == CurrentLevel)?.QualityMultiplier
        ?? 1.0;

    public GraphicsLevel? GetNextLower(
        GraphicsLevel current,
        bool largeStep)
    {
        var ordered = Levels
            .OrderByDescending(x => x.QualityMultiplier)
            .ToList();

        int index = ordered.FindIndex(x => x.Level == current);

        if (index < 0)
            return ordered.Count > 1 ? ordered[1].Level : null;

        int step = largeStep ? 2 : 1;
        int target = Math.Min(index + step, ordered.Count - 1);

        return target == index ? null : ordered[target].Level;
    }

    public GraphicsLevel? GetNextHigher(GraphicsLevel current)
    {
        var ordered = Levels
            .OrderByDescending(x => x.QualityMultiplier)
            .ToList();

        int index = ordered.FindIndex(x => x.Level == current);

        if (index <= 0)
            return null;

        return ordered[index - 1].Level;
    }

    public GameGraphicsProfile Clone()
    {
        return new GameGraphicsProfile
        {
            GameId = GameId,
            DisplayName = DisplayName,
            TargetFps = TargetFps,
            LowFpsThreshold = LowFpsThreshold,
            CriticalFps = CriticalFps,
            CriticalFrametimeMs = CriticalFrametimeMs,
            RecoveryFrametimeMs = RecoveryFrametimeMs,
            RecoveryFps = RecoveryFps,
            StableSamplesBeforeRecovery = StableSamplesBeforeRecovery,
            DecisionCooldown = DecisionCooldown,
            CurrentLevel = CurrentLevel,
            Levels = Levels.Select(x => x.Clone()).ToList()
        };
    }

    public static GameGraphicsProfile CreateDefault(
        string gameId,
        string displayName)
    {
        return new GameGraphicsProfile
        {
            GameId = gameId,
            DisplayName = displayName,
            Levels = new List<GraphicsLevelConfig>
            {
                new(GraphicsLevel.Native, 1.00, "Native"),
                new(GraphicsLevel.Light, 0.95, "Light"),
                new(GraphicsLevel.Medium, 0.88, "Medium"),
                new(GraphicsLevel.Low, 0.80, "Low"),
                new(GraphicsLevel.Emergency, 0.70, "Emergency")
            }
        };
    }
}

public sealed class GraphicsLevelConfig
{
    public GraphicsLevelConfig() { }

    public GraphicsLevelConfig(
        GraphicsLevel level,
        double qualityMultiplier,
        string name)
    {
        Level = level;
        QualityMultiplier = qualityMultiplier;
        Name = name;
    }

    public GraphicsLevel Level { get; set; }
    public double QualityMultiplier { get; set; }
    public string Name { get; set; } = "";

    public GraphicsLevelConfig Clone() =>
        new(Level, QualityMultiplier, Name);
}

public enum GraphicsLevel
{
    Native = 0,
    Light = 1,
    Medium = 2,
    Low = 3,
    Emergency = 4
}

public sealed record GraphicsTelemetry(
    double Fps,
    double FrametimeMs,
    double CpuUsagePercent,
    double GpuUsagePercent,
    double RamUsageMb,
    double VramUsageMb,
    double CpuTemperatureC,
    double GpuTemperatureC);

public enum GraphicsAction
{
    None,
    DecreaseQuality,
    IncreaseQuality
}

public sealed record GraphicsDecision(
    GraphicsAction Action,
    GraphicsLevel TargetLevel,
    string Reason)
{
    public static GraphicsDecision None =>
        new(GraphicsAction.None, GraphicsLevel.Native, "");
}

public sealed class GraphicsDecisionEventArgs : EventArgs
{
    public GraphicsDecisionEventArgs(
        GraphicsDecision decision,
        GraphicsTelemetry telemetry)
    {
        Decision = decision;
        Telemetry = telemetry;
    }

    public GraphicsDecision Decision { get; }
    public GraphicsTelemetry Telemetry { get; }
}

public sealed class GraphicsModelUpdatedEventArgs : EventArgs
{
    public GraphicsModelUpdatedEventArgs(string path)
    {
        Path = path;
    }

    public string Path { get; }
}

public interface IGraphicsAdapter
{
    /// <summary>
    /// Applies a game-specific graphics level.
    /// Return false if the adapter cannot safely apply it.
    /// </summary>
    bool ApplyLevel(GraphicsLevel level);

    /// <summary>
    /// Restores the configuration that was backed up before the first change.
    /// </summary>
    void RestoreBackup();
}

public sealed class GamePerformanceModel
{
    public GamePerformanceModel(string gameId = "")
    {
        GameId = gameId;
    }

    public string GameId { get; set; } = "";

    public Dictionary<GraphicsLevel, GraphicsLearningBucket> Buckets { get; set; } =
        new();

    public void Observe(
        GraphicsLevel level,
        GraphicsTelemetry telemetry,
        double qualityMultiplier)
    {
        if (!Buckets.TryGetValue(level, out var bucket))
        {
            bucket = new GraphicsLearningBucket();
            Buckets[level] = bucket;
        }

        bucket.SampleCount++;

        // Exponential moving average. Recent gameplay matters more than
        // ancient sessions, while still retaining a useful baseline.
        const double alpha = 0.18;

        bucket.AverageFps = Ema(
            bucket.AverageFps,
            telemetry.Fps,
            bucket.SampleCount,
            alpha);

        bucket.AverageFrametimeMs = Ema(
            bucket.AverageFrametimeMs,
            telemetry.FrametimeMs,
            bucket.SampleCount,
            alpha);

        bucket.AverageGpuUsage = Ema(
            bucket.AverageGpuUsage,
            telemetry.GpuUsagePercent,
            bucket.SampleCount,
            alpha);

        bucket.AverageCpuUsage = Ema(
            bucket.AverageCpuUsage,
            telemetry.CpuUsagePercent,
            bucket.SampleCount,
            alpha);

        bucket.AverageVramMb = Ema(
            bucket.AverageVramMb,
            telemetry.VramUsageMb,
            bucket.SampleCount,
            alpha);

        bucket.LastQualityMultiplier = qualityMultiplier;
        bucket.LastUpdatedUtc = DateTime.UtcNow;
    }

    public double PredictFps(
        GraphicsLevel level,
        GraphicsTelemetry current)
    {
        if (Buckets.TryGetValue(level, out var known) &&
            known.SampleCount >= 5 &&
            known.AverageFps > 0)
        {
            // Blend learned performance with current telemetry.
            double gpuPressure = Math.Clamp(
                current.GpuUsagePercent / 100.0, 0, 1);

            double currentWeight = 0.35 + (1.0 - gpuPressure) * 0.15;
            double learnedWeight = 1.0 - currentWeight;

            return (known.AverageFps * learnedWeight) +
                   (current.Fps * currentWeight);
        }

        // Cold-start estimate. It is deliberately conservative and based
        // only on the current real FPS plus quality scaling.
        double multiplier =
            Buckets.TryGetValue(level, out var bucket) &&
            bucket.LastQualityMultiplier > 0
                ? bucket.LastQualityMultiplier
                : 1.0;

        return Math.Max(
            1,
            current.Fps * (0.55 + (0.45 * multiplier)));
    }

    private static double Ema(
        double oldValue,
        double newValue,
        long sampleCount,
        double alpha)
    {
        if (sampleCount <= 1 || oldValue <= 0)
            return newValue;

        return oldValue + alpha * (newValue - oldValue);
    }

    public GamePerformanceModel Clone()
    {
        var copy = new GamePerformanceModel(GameId);

        foreach (var pair in Buckets)
        {
            copy.Buckets[pair.Key] = pair.Value.Clone();
        }

        return copy;
    }
}

public sealed class GraphicsLearningBucket
{
    public long SampleCount { get; set; }
    public double AverageFps { get; set; }
    public double AverageFrametimeMs { get; set; }
    public double AverageGpuUsage { get; set; }
    public double AverageCpuUsage { get; set; }
    public double AverageVramMb { get; set; }
    public double LastQualityMultiplier { get; set; } = 1;
    public DateTime LastUpdatedUtc { get; set; }

    public GraphicsLearningBucket Clone()
    {
        return new GraphicsLearningBucket
        {
            SampleCount = SampleCount,
            AverageFps = AverageFps,
            AverageFrametimeMs = AverageFrametimeMs,
            AverageGpuUsage = AverageGpuUsage,
            AverageCpuUsage = AverageCpuUsage,
            AverageVramMb = AverageVramMb,
            LastQualityMultiplier = LastQualityMultiplier,
            LastUpdatedUtc = LastUpdatedUtc
        };
    }
}
