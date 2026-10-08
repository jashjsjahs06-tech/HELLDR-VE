using System;
using System.Collections.Generic;
using System.Linq;

namespace HELLDRIVE;

public sealed record PresetSession(
    string Preset,
    double? AverageFps,
    double? MinimumFps,
    double? AverageFrameTimeMs,
    int Score);

public sealed class PresetABTester
{
    private readonly List<PresetSession> _sessions = new();

    public void Add(PresetSession session) => _sessions.Add(session);

    public IReadOnlyList<PresetSession> Sessions => _sessions;

    public PresetSession? BestByScore() =>
        _sessions.Count == 0 ? null : _sessions.OrderByDescending(x => x.Score).First();

    public string BuildComparison()
    {
        if (_sessions.Count == 0) return "No A/B sessions recorded.";

        return string.Join(Environment.NewLine,
            _sessions.Select(x =>
                $"{x.Preset}: Avg FPS={Format(x.AverageFps)}, " +
                $"Min FPS={Format(x.MinimumFps)}, " +
                $"FrameTime={Format(x.AverageFrameTimeMs)} ms, Score={x.Score}/100"));
    }

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("F1") : "N/A";
}