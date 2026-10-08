using System;
using System.Collections.Generic;

namespace HELLDRIVE;

public enum OptimizationEventSeverity
{
    Info,
    Success,
    Warning,
    Critical
}

public sealed record OptimizationEvent(
    DateTime Timestamp,
    OptimizationEventSeverity Severity,
    string Source,
    string Message);

public sealed class OptimizationEventCenter
{
    private readonly object _sync = new();
    private readonly List<OptimizationEvent> _events = new();

    public event EventHandler<OptimizationEvent>? EventAdded;

    public IReadOnlyList<OptimizationEvent> Snapshot()
    {
        lock (_sync) return _events.ToArray();
    }

    public void Add(
        string source,
        string message,
        OptimizationEventSeverity severity = OptimizationEventSeverity.Info)
    {
        var entry = new OptimizationEvent(DateTime.Now, severity, source, message);

        lock (_sync) _events.Add(entry);

        try { EventAdded?.Invoke(this, entry); }
        catch { }
    }

    public void Clear()
    {
        lock (_sync) _events.Clear();
    }
}