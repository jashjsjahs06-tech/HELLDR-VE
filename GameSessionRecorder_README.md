# HELLDR-VE Game Session Recorder

Records real telemetry during a game session and creates a JSON report.

## Example

```csharp
using var recorder = new GameSessionRecorder();

recorder.EventRecorded += (_, e) =>
{
    Console.WriteLine(
        $"[{e.ElapsedSeconds:F0}s] {e.Type}: {e.Message}");
};

recorder.Start("VALORANT.exe", "VALORANT");

// Call this from your telemetry timer:
recorder.RecordTelemetry(
    fps: 144,
    frameTimeMs: 6.9,
    cpuPercent: 42,
    gpuPercent: 91,
    ramMb: 6842,
    cpuTemperatureC: 62,
    gpuTemperatureC: 67);

recorder.RecordOptimization("Priority changed to High");

SessionReport report = recorder.Stop();

Console.WriteLine($"Score: {report.Score}/100");
```

Reports are stored under:

`%LocalAppData%\HELLDRIVE\sessions\`

The recorder never fabricates telemetry. Missing measurements remain null.
