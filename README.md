# HELLDR-VE Feature Pack

This package contains the ten requested feature modules:

1. CrashGuard
2. OptimizationDiff
3. ThermalGuard
4. AutoProfileLauncher
5. PresetABTester
6. RecoveryManager
7. NetworkPerformanceMonitor
8. OptimizationEventCenter
9. SmartOptimizer
10. ProfileExchange

`HELLDRIVEFeatureHub.cs` provides a small integration container.

## Integration notes

Add the `.cs` files to the HELLDRIVE WinForms project. The modules are
backend services and do not replace the existing MainForm UI.

Suggested MainForm wiring:

- Subscribe `OptimizationEventCenter.EventAdded` to the UI event feed.
- Feed StatsMonitor telemetry into `ThermalGuard`.
- Use `SmartOptimizer.SelectPreset()` before applying a profile.
- Register game profiles with `AutoProfileLauncher`.
- Call `RecoveryManager.Save()` after a known-good optimization.
- Call `RecoveryManager.Load()` during startup/recovery.
- Use `ProfileExchange` for import/export.
- Feed real ping samples from `NetworkPerformanceMonitor` into the session recorder.
- Use `OptimizationDiff.Compare()` around optimization application.
- Use `PresetABTester` with actual session reports.

The package intentionally does not silently kill processes, modify game files,
or invent telemetry values.
