using System;
using System.Threading;

namespace HELLDRIVE;

public sealed class CrashGuard : IDisposable
{
    private readonly string _gameProcessName;
    private readonly Action _restoreSafeState;
    private readonly Action<string>? _log;
    private Timer? _timer;
    private int _lastPid;
    private bool _gameWasRunning;
    private bool _disposed;

    public bool Enabled { get; set; } = true;

    public CrashGuard(string gameProcessName, Action restoreSafeState, Action<string>? log = null)
    {
        _gameProcessName = gameProcessName;
        _restoreSafeState = restoreSafeState;
        _log = log;
    }

    public void Start()
    {
        ThrowIfDisposed();
        _timer ??= new Timer(Check, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void Check(object? state)
    {
        if (!Enabled || _disposed) return;

        bool running = ProcessHelpers.IsRunning(_gameProcessName);

        if (_gameWasRunning && !running)
        {
            try
            {
                _restoreSafeState();
                _log?.Invoke("CrashGuard: game stopped; safe state restored.");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"CrashGuard restore failed: {ex.Message}");
            }
        }

        if (running)
        {
            var pid = ProcessHelpers.FindPid(_gameProcessName);
            if (pid != _lastPid)
                _lastPid = pid;
        }

        _gameWasRunning = running;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CrashGuard));
    }
}