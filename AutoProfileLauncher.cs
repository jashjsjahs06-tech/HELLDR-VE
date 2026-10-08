using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace HELLDRIVE;

public sealed record GameLaunchProfile(
    string ProcessName,
    string ProfileName,
    string Preset);

public sealed class AutoProfileLauncher : IDisposable
{
    private readonly List<GameLaunchProfile> _profiles = new();
    private readonly Action<GameLaunchProfile>? _applyProfile;
    private readonly Action<string>? _log;
    private Timer? _timer;
    private readonly HashSet<string> _handledPids = new();
    private bool _disposed;

    public AutoProfileLauncher(
        Action<GameLaunchProfile>? applyProfile = null,
        Action<string>? log = null)
    {
        _applyProfile = applyProfile;
        _log = log;
    }

    public void AddProfile(GameLaunchProfile profile)
    {
        _profiles.RemoveAll(x =>
            string.Equals(x.ProcessName, profile.ProcessName, StringComparison.OrdinalIgnoreCase));
        _profiles.Add(profile);
    }

    public void Start()
    {
        _timer ??= new Timer(Scan, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void Scan(object? state)
    {
        if (_disposed) return;

        foreach (var profile in _profiles)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(profile.ProcessName);

            foreach (var process in Process.GetProcessesByName(name))
            {
                string key = $"{name}:{process.Id}";
                if (_handledPids.Contains(key)) continue;

                _handledPids.Add(key);
                _log?.Invoke($"AutoProfile: {profile.ProcessName} detected.");
                _applyProfile?.Invoke(profile);
            }
        }

        _handledPids.RemoveWhere(key =>
        {
            int colon = key.LastIndexOf(':');
            if (colon < 0 || !int.TryParse(key[(colon + 1)..], out int pid)) return true;
            try { return Process.GetProcessById(pid).HasExited; }
            catch { return true; }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}