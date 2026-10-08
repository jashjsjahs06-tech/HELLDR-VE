using System.Diagnostics;
using System.Linq;

namespace HELLDRIVE;

internal static class ProcessHelpers
{
    public static bool IsRunning(string processName) => FindPid(processName) > 0;

    public static int FindPid(string processName)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(processName);
        return Process.GetProcessesByName(name).FirstOrDefault()?.Id ?? 0;
    }
}