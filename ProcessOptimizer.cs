using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HellDrive;

public sealed class ProcessOptimizer
{
    [DllImport("ntdll.dll", SetLastError = true)] private static extern int NtSuspendProcess(IntPtr processHandle);
    [DllImport("ntdll.dll", SetLastError = true)] private static extern int NtResumeProcess(IntPtr processHandle);

    public readonly record struct SuspendedProcess(int Id, string Name);

    public List<SuspendedProcess> SuspendBackground(IEnumerable<string> names, int gamePid, OptimizationLogger log)
    {
        var wanted = new HashSet<string>(names.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        var result = new List<SuspendedProcess>();
        if (wanted.Count == 0) return result;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == gamePid || !wanted.Contains(p.ProcessName)) continue;
                if (p.HasExited) continue;
                NtSuspendProcess(p.Handle);
                result.Add(new SuspendedProcess(p.Id, p.ProcessName));
                log.Add("BACKGROUND SUSPEND", $"{p.ProcessName} (PID {p.Id})");
            }
            catch { }
            finally { p.Dispose(); }
        }
        return result;
    }

    public void Resume(IEnumerable<int> ids, OptimizationLogger log)
    {
        foreach (var id in ids.Distinct())
        {
            try
            {
                using var p = Process.GetProcessById(id);
                if (!p.HasExited)
                {
                    NtResumeProcess(p.Handle);
                    log.Add("BACKGROUND RESTORE", $"PID {id}");
                }
            }
            catch { }
        }
    }

    public void ApplyPriority(Process process, string priority, OptimizationLogger log)
    {
        var target = priority switch
        {
            "High" => ProcessPriorityClass.High,
            "Above Normal" => ProcessPriorityClass.AboveNormal,
            "Below Normal" => ProcessPriorityClass.BelowNormal,
            "Idle" => ProcessPriorityClass.Idle,
            _ => ProcessPriorityClass.Normal
        };
        process.PriorityClass = target;
        log.Add("CPU PRIORITY", $"{process.ProcessName}: {target}");
    }
}
