namespace HELLDRIVE;

/// <summary>
/// Convenience container for wiring the new modules into MainForm.
/// Each subsystem remains independent, so existing HELLDRIVE code can adopt
/// them incrementally.
/// </summary>
public sealed class HELLDRIVEFeatureHub
{
    public OptimizationEventCenter Events { get; } = new();
    public ThermalGuard Thermal { get; } = new();
    public SmartOptimizer SmartOptimizer { get; } = new();
    public PresetABTester PresetAB { get; } = new();
    public RecoveryManager Recovery { get; }

    public HELLDRIVEFeatureHub()
    {
        Recovery = new RecoveryManager();
    }
}