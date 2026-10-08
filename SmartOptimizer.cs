using System;

namespace HELLDRIVE;

public enum OptimizerPreset
{
    Safe,
    Balanced,
    Aggressive,
    Ultra
}

public sealed record SystemSnapshot(
    double? CpuUsage,
    double? GpuUsage,
    double? RamUsageMb,
    double? CpuTemperatureC,
    double? GpuTemperatureC);

public sealed class SmartOptimizer
{
    public OptimizerPreset SelectPreset(SystemSnapshot snapshot)
    {
        if (snapshot.CpuTemperatureC >= 90 ||
            snapshot.GpuTemperatureC >= 90)
            return OptimizerPreset.Safe;

        if (snapshot.CpuUsage >= 90 ||
            snapshot.GpuUsage >= 95)
            return OptimizerPreset.Balanced;

        if (snapshot.RamUsageMb >= 14000)
            return OptimizerPreset.Balanced;

        if (snapshot.CpuUsage <= 55 &&
            snapshot.GpuUsage <= 90)
            return OptimizerPreset.Aggressive;

        return OptimizerPreset.Balanced;
    }
}