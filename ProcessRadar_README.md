# HELLDR-VE Process Radar

`ProcessRadar.cs` provides a safe real-time process dashboard backend.

Features:
- CPU percentage
- RAM/working-set usage
- thread count
- process priority
- executable path
- start time/runtime
- Game/System/User/Background categories
- High CPU / High Memory / High Impact classification
- 1-second live refresh
- system process protection
- no automatic killing or suspension

## Example

```csharp
using var radar = new ProcessRadar();

radar.SnapshotUpdated += (_, processes) =>
{
    foreach (var p in processes.Take(10))
        Console.WriteLine(
            $"{p.Name} | CPU {p.CpuPercent:F1}% | RAM {p.MemoryMb:F0} MB | {p.Impact}");
};

radar.SetGameProcess(gameProcess.Id);
```

The UI can bind `SnapshotUpdated` to a WinForms DataGridView/ListView.
