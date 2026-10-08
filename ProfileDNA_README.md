# HELLDR-VE Profile DNA

`ProfileDNAService.cs` adds persistent per-game performance history.

## Example

```csharp
var dna = new ProfileDNAService();

dna.RecordSession(
    gameId: "VALORANT.exe",
    displayName: "VALORANT",
    averageFps: 144,
    averageFrameTimeMs: 6.9,
    averageCpuPercent: 42,
    averageGpuPercent: 91,
    averageRamMb: 6842,
    averageCpuTemperatureC: 62,
    averageGpuTemperatureC: 67,
    duration: TimeSpan.FromMinutes(45),
    preset: "Aggressive",
    optimizations: new[] { "Priority High", "Background Guard" });

DNAReport report = dna.GetReport("VALORANT.exe");
```

The database is stored at:

`%LocalAppData%\HELLDRIVE\profile-dna.json`

No FPS/GPU values are fabricated. The service only stores measurements supplied by the telemetry layer.
