using System;

namespace HELLDRIVE;

public enum ThermalState
{
    Normal,
    Warm,
    Hot,
    Critical
}

public sealed class ThermalGuard
{
    public double WarmCpuC { get; set; } = 80;
    public double HotCpuC { get; set; } = 88;
    public double CriticalCpuC { get; set; } = 95;

    public double WarmGpuC { get; set; } = 78;
    public double HotGpuC { get; set; } = 85;
    public double CriticalGpuC { get; set; } = 92;

    public ThermalState Evaluate(double? cpuC, double? gpuC)
    {
        if (!cpuC.HasValue && !gpuC.HasValue) return ThermalState.Normal;

        double cpu = cpuC ?? 0;
        double gpu = gpuC ?? 0;

        if (cpu >= CriticalCpuC || gpu >= CriticalGpuC) return ThermalState.Critical;
        if (cpu >= HotCpuC || gpu >= HotGpuC) return ThermalState.Hot;
        if (cpu >= WarmCpuC || gpu >= WarmGpuC) return ThermalState.Warm;
        return ThermalState.Normal;
    }

    public bool ShouldReduceAggression(double? cpuC, double? gpuC) =>
        Evaluate(cpuC, gpuC) is ThermalState.Hot or ThermalState.Critical;
}