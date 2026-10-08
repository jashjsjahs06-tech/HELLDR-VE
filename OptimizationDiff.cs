using System;
using System.Collections.Generic;
using System.Linq;

namespace HELLDRIVE;

public sealed record OptimizationValue(string Key, string? Before, string? After);

public sealed class OptimizationDiff
{
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public List<OptimizationValue> Changes { get; init; } = new();

    public int ChangeCount => Changes.Count;

    public static OptimizationDiff Compare(
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after)
    {
        var result = new OptimizationDiff();

        foreach (var key in before.Keys.Union(after.Keys).Distinct())
        {
            before.TryGetValue(key, out var b);
            after.TryGetValue(key, out var a);

            if (!string.Equals(b, a, StringComparison.Ordinal))
                result.Changes.Add(new OptimizationValue(key, b, a));
        }

        return result;
    }
}