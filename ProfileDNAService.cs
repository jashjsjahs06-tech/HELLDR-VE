using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HELLDRIVE;

/// <summary>
/// Stores and analyzes per-game performance history.
/// It intentionally does not invent FPS/GPU values. Feed it measured values
/// from the live telemetry layer when available.
/// </summary>
public sealed class ProfileDNAService
{
    private readonly object _sync = new();
    private readonly string _storageDirectory;
    private readonly string _databasePath;
    private readonly JsonSerializerOptions _jsonOptions;

    private Dictionary<string, GameDNA> _games = new(StringComparer.OrdinalIgnoreCase);

    public ProfileDNAService(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HELLDRIVE");

        _databasePath = Path.Combine(_storageDirectory, "profile-dna.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        Load();
    }

    public IReadOnlyCollection<GameDNA> GetAll()
    {
        lock (_sync)
            return _games.Values.Select(Clone).ToList().AsReadOnly();
    }

    public GameDNA GetOrCreate(string gameId, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(gameId))
            throw new ArgumentException("Game ID cannot be empty.", nameof(gameId));

        string key = Normalize(gameId);

        lock (_sync)
        {
            if (!_games.TryGetValue(key, out GameDNA? dna))
            {
                dna = new GameDNA
                {
                    GameId = key,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName,
                    CreatedAt = DateTime.Now
                };

                _games[key] = dna;
                SaveUnsafe();
            }

            return Clone(dna);
        }
    }

    public void RecordSession(
        string gameId,
        string? displayName,
        double? averageFps,
        double? averageFrameTimeMs,
        double? averageCpuPercent,
        double? averageGpuPercent,
        double? averageRamMb,
        double? averageCpuTemperatureC,
        double? averageGpuTemperatureC,
        TimeSpan duration,
        string preset,
        IEnumerable<string>? optimizations = null)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        lock (_sync)
        {
            string key = Normalize(gameId);

            if (!_games.TryGetValue(key, out GameDNA? dna))
            {
                dna = new GameDNA
                {
                    GameId = key,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? key : displayName,
                    CreatedAt = DateTime.Now
                };

                _games[key] = dna;
            }
            else if (!string.IsNullOrWhiteSpace(displayName))
            {
                dna.DisplayName = displayName;
            }

            var session = new GameSession
            {
                StartedAt = DateTime.Now - duration,
                DurationSeconds = duration.TotalSeconds,
                AverageFps = Clean(averageFps),
                AverageFrameTimeMs = Clean(averageFrameTimeMs),
                AverageCpuPercent = Clean(averageCpuPercent),
                AverageGpuPercent = Clean(averageGpuPercent),
                AverageRamMb = Clean(averageRamMb),
                AverageCpuTemperatureC = Clean(averageCpuTemperatureC),
                AverageGpuTemperatureC = Clean(averageGpuTemperatureC),
                Preset = preset ?? "Unknown",
                Optimizations = optimizations?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList()
                    ?? new List<string>()
            };

            dna.Sessions.Add(session);

            // Keep the local database compact while preserving useful history.
            const int maxSessions = 100;
            if (dna.Sessions.Count > maxSessions)
                dna.Sessions.RemoveRange(0, dna.Sessions.Count - maxSessions);

            dna.LastSessionAt = session.StartedAt + duration;
            SaveUnsafe();
        }
    }

    public DNAReport GetReport(string gameId)
    {
        lock (_sync)
        {
            string key = Normalize(gameId);

            if (!_games.TryGetValue(key, out GameDNA? dna) || dna.Sessions.Count == 0)
                return DNAReport.Empty(key, dna?.DisplayName);

            List<GameSession> measuredFps = dna.Sessions
                .Where(x => x.AverageFps.HasValue)
                .ToList();

            List<GameSession> measuredFrameTime = dna.Sessions
                .Where(x => x.AverageFrameTimeMs.HasValue)
                .ToList();

            double? avgFps = Average(measuredFps.Select(x => x.AverageFps));
            double? avgFrame = Average(measuredFrameTime.Select(x => x.AverageFrameTimeMs));

            double stability = CalculateStability(measuredFps, measuredFrameTime);
            int score = CalculateScore(dna, stability);

            return new DNAReport
            {
                GameId = dna.GameId,
                DisplayName = dna.DisplayName,
                SessionCount = dna.Sessions.Count,
                TotalPlayTime = TimeSpan.FromSeconds(
                    dna.Sessions.Sum(x => x.DurationSeconds)),
                AverageFps = avgFps,
                AverageFrameTimeMs = avgFrame,
                AverageCpuPercent = Average(dna.Sessions.Select(x => x.AverageCpuPercent)),
                AverageGpuPercent = Average(dna.Sessions.Select(x => x.AverageGpuPercent)),
                AverageRamMb = Average(dna.Sessions.Select(x => x.AverageRamMb)),
                AverageCpuTemperatureC = Average(dna.Sessions.Select(x => x.AverageCpuTemperatureC)),
                AverageGpuTemperatureC = Average(dna.Sessions.Select(x => x.AverageGpuTemperatureC)),
                StabilityPercent = stability,
                Score = score,
                LastSessionAt = dna.LastSessionAt
            };
        }
    }

    public PerformanceAnomaly? CompareToDNA(
        string gameId,
        double? currentFps,
        double? currentFrameTimeMs)
    {
        DNAReport report = GetReport(gameId);

        if (!report.AverageFps.HasValue && !report.AverageFrameTimeMs.HasValue)
            return null;

        double? fpsDeviation = null;
        double? frameDeviation = null;

        if (currentFps.HasValue && report.AverageFps is > 0)
            fpsDeviation = ((currentFps.Value - report.AverageFps.Value) /
                            report.AverageFps.Value) * 100.0;

        if (currentFrameTimeMs.HasValue && report.AverageFrameTimeMs is > 0)
            frameDeviation = ((currentFrameTimeMs.Value - report.AverageFrameTimeMs.Value) /
                              report.AverageFrameTimeMs.Value) * 100.0;

        bool badFps = fpsDeviation <= -20;
        bool badFrameTime = frameDeviation >= 25;

        if (!badFps && !badFrameTime)
            return null;

        return new PerformanceAnomaly
        {
            GameId = report.GameId,
            FpsDeviationPercent = fpsDeviation,
            FrameTimeDeviationPercent = frameDeviation,
            Message = "Performance deviation detected compared with the game's DNA baseline."
        };
    }

    public void Delete(string gameId)
    {
        lock (_sync)
        {
            _games.Remove(Normalize(gameId));
            SaveUnsafe();
        }
    }

    private void Load()
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_storageDirectory);

                if (!File.Exists(_databasePath))
                    return;

                string json = File.ReadAllText(_databasePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, GameDNA>>(
                    json, _jsonOptions);

                if (loaded != null)
                    _games = new Dictionary<string, GameDNA>(
                        loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // A corrupted history must never prevent HELLDRIVE from starting.
                _games = new Dictionary<string, GameDNA>(
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void SaveUnsafe()
    {
        Directory.CreateDirectory(_storageDirectory);

        string temp = _databasePath + ".tmp";
        string json = JsonSerializer.Serialize(_games, _jsonOptions);

        File.WriteAllText(temp, json);
        File.Move(temp, _databasePath, true);
    }

    private static double? Clean(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            return null;

        return Math.Max(0, value.Value);
    }

    private static double? Average(IEnumerable<double?> values)
    {
        double[] numbers = values
            .Where(x => x.HasValue && !double.IsNaN(x.Value) && !double.IsInfinity(x.Value))
            .Select(x => x!.Value)
            .ToArray();

        return numbers.Length == 0 ? null : numbers.Average();
    }

    private static double CalculateStability(
        List<GameSession> fpsSessions,
        List<GameSession> frameSessions)
    {
        List<double> normalizedVariations = new();

        if (fpsSessions.Count >= 2)
        {
            double avg = fpsSessions.Average(x => x.AverageFps!.Value);
            if (avg > 0)
            {
                double std = Math.Sqrt(
                    fpsSessions.Sum(x => Math.Pow(x.AverageFps!.Value - avg, 2)) /
                    fpsSessions.Count);

                normalizedVariations.Add((std / avg) * 100);
            }
        }

        if (frameSessions.Count >= 2)
        {
            double avg = frameSessions.Average(x => x.AverageFrameTimeMs!.Value);
            if (avg > 0)
            {
                double std = Math.Sqrt(
                    frameSessions.Sum(x => Math.Pow(x.AverageFrameTimeMs!.Value - avg, 2)) /
                    frameSessions.Count);

                normalizedVariations.Add((std / avg) * 100);
            }
        }

        if (normalizedVariations.Count == 0)
            return 100;

        double variation = normalizedVariations.Average();

        // 0% variation = 100 stability. 50%+ variation = 0 stability.
        return Math.Clamp(100 - (variation * 2), 0, 100);
    }

    private static int CalculateScore(GameDNA dna, double stability)
    {
        double score = stability;

        var fps = dna.Sessions
            .Where(x => x.AverageFps.HasValue)
            .Select(x => x.AverageFps!.Value)
            .ToList();

        if (fps.Count > 0)
        {
            // FPS contributes up to 20 points, capped so high-refresh systems
            // do not automatically receive a perfect score.
            score += Math.Min(fps.Average() / 10.0, 20);
        }

        return Math.Clamp((int)Math.Round(score), 0, 100);
    }

    private static string Normalize(string gameId) =>
        Path.GetFileNameWithoutExtension(gameId.Trim());

    private static GameDNA Clone(GameDNA source)
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<GameDNA>(json)
            ?? new GameDNA { GameId = source.GameId, DisplayName = source.DisplayName };
    }
}

public sealed class GameDNA
{
    public string GameId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastSessionAt { get; set; }
    public List<GameSession> Sessions { get; set; } = new();
}

public sealed class GameSession
{
    public DateTime StartedAt { get; set; }
    public double DurationSeconds { get; set; }
    public double? AverageFps { get; set; }
    public double? AverageFrameTimeMs { get; set; }
    public double? AverageCpuPercent { get; set; }
    public double? AverageGpuPercent { get; set; }
    public double? AverageRamMb { get; set; }
    public double? AverageCpuTemperatureC { get; set; }
    public double? AverageGpuTemperatureC { get; set; }
    public string Preset { get; set; } = "Unknown";
    public List<string> Optimizations { get; set; } = new();
}

public sealed class DNAReport
{
    public string GameId { get; init; } = "";
    public string? DisplayName { get; init; }
    public int SessionCount { get; init; }
    public TimeSpan TotalPlayTime { get; init; }
    public double? AverageFps { get; init; }
    public double? AverageFrameTimeMs { get; init; }
    public double? AverageCpuPercent { get; init; }
    public double? AverageGpuPercent { get; init; }
    public double? AverageRamMb { get; init; }
    public double? AverageCpuTemperatureC { get; init; }
    public double? AverageGpuTemperatureC { get; init; }
    public double StabilityPercent { get; init; }
    public int Score { get; init; }
    public DateTime? LastSessionAt { get; init; }

    public static DNAReport Empty(string gameId, string? displayName) => new()
    {
        GameId = gameId,
        DisplayName = displayName,
        StabilityPercent = 100,
        Score = 0
    };
}

public sealed class PerformanceAnomaly
{
    public string GameId { get; init; } = "";
    public double? FpsDeviationPercent { get; init; }
    public double? FrameTimeDeviationPercent { get; init; }
    public string Message { get; init; } = "";
}
