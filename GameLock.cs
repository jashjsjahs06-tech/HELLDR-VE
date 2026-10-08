using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace HELLDRIVE;

/// <summary>
/// Game Lock: keeps a HELLDRIVE optimization session alive while a game is running.
/// It detects the game process, monitors the session, and invokes restore logic when
/// the game exits or the lock is stopped.
/// </summary>
public sealed class GameLock : IDisposable
{
    private readonly object _sync = new();
    private readonly Timer _watchdog;
    private readonly Action _applyOptimization;
    private readonly Action _restoreOptimization;
    private readonly Action<string>? _log;

    private string? _gameProcessName;
    private int _gamePid;
    private DateTime _sessionStarted;
    private bool _optimized;
    private bool _stopping;
    private bool _disposed;

    public bool IsActive
    {
        get { lock (_sync) return _optimized && !_stopping; }
    }

    public int GamePid
    {
        get { lock (_sync) return _gamePid; }
    }

    public TimeSpan SessionDuration
    {
        get
        {
            lock (_sync)
            {
                return _sessionStarted == default
                    ? TimeSpan.Zero
                    : DateTime.Now - _sessionStarted;
            }
        }
    }

    public GameLock(
        Action applyOptimization,
        Action restoreOptimization,
        Action<string>? log = null)
    {
        _applyOptimization = applyOptimization
            ?? throw new ArgumentNullException(nameof(applyOptimization));

        _restoreOptimization = restoreOptimization
            ?? throw new ArgumentNullException(nameof(restoreOptimization));

        _log = log;

        // Check the game every 1 second.
        _watchdog = new Timer(
            WatchdogTick,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    /// <summary>
    /// Starts a Game Lock session for a process name.
    /// Example: "GTA5.exe" or "Cyberpunk2077.exe".
    /// </summary>
    public bool Start(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Process name cannot be empty.", nameof(processName));

        processName = Path.GetFileNameWithoutExtension(processName);

        lock (_sync)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameLock));

            if (_optimized)
            {
                WriteLog("GAME LOCK ALREADY ACTIVE");
                return false;
            }

            _gameProcessName = processName;
            _gamePid = FindGameProcess(processName);

            if (_gamePid <= 0)
            {
                WriteLog($"GAME NOT FOUND: {processName}.exe");
                return false;
            }

            _sessionStarted = DateTime.Now;
            _stopping = false;
        }

        try
        {
            WriteLog($"GAME DETECTED: {processName}.exe (PID {_gamePid})");

            _applyOptimization();

            lock (_sync)
                _optimized = true;

            WriteLog("GAME LOCK ENABLED");
            WriteLog("SYSTEM STATE: OPTIMIZED");

            _watchdog.Change(1000, 1000);
            return true;
        }
        catch (Exception ex)
        {
            WriteLog($"OPTIMIZATION FAILED: {ex.Message}");

            try
            {
                _restoreOptimization();
                WriteLog("ROLLBACK: COMPLETE");
            }
            catch (Exception restoreEx)
            {
                WriteLog($"ROLLBACK FAILED: {restoreEx.Message}");
            }

            lock (_sync)
            {
                _optimized = false;
                _gamePid = 0;
                _gameProcessName = null;
            }

            return false;
        }
    }

    /// <summary>
    /// Stops Game Lock and restores the state changed by HELLDR-VE.
    /// </summary>
    public void Stop()
    {
        bool shouldRestore;

        lock (_sync)
        {
            if (_disposed)
                return;

            if (_stopping)
                return;

            _stopping = true;
            shouldRestore = _optimized;
        }

        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);

        if (!shouldRestore)
        {
            lock (_sync)
            {
                _stopping = false;
                _gamePid = 0;
                _gameProcessName = null;
            }

            return;
        }

        try
        {
            WriteLog("GAME LOCK STOPPED");
            _restoreOptimization();
            WriteLog("SYSTEM STATE: RESTORED");
        }
        catch (Exception ex)
        {
            WriteLog($"RESTORE FAILED: {ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                _optimized = false;
                _stopping = false;
                _gamePid = 0;
                _gameProcessName = null;
                _sessionStarted = default;
            }
        }
    }

    private void WatchdogTick(object? state)
    {
        string? processName;
        int pid;
        bool active;

        lock (_sync)
        {
            if (_disposed || _stopping)
                return;

            processName = _gameProcessName;
            pid = _gamePid;
            active = _optimized;
        }

        if (!active || string.IsNullOrWhiteSpace(processName) || pid <= 0)
            return;

        bool gameStillRunning = false;

        try
        {
            using Process process = Process.GetProcessById(pid);

            // Make sure the PID was not reused by another executable.
            gameStillRunning =
                !process.HasExited &&
                string.Equals(
                    process.ProcessName,
                    processName,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            gameStillRunning = false;
        }

        if (!gameStillRunning)
        {
            WriteLog("GAME EXIT DETECTED");
            WriteLog("RESTORING SYSTEM");
            Stop();
        }
    }

    private static int FindGameProcess(string processName)
    {
        try
        {
            Process[] processes = Process.GetProcessesByName(processName);

            try
            {
                return processes
                    .OrderByDescending(p =>
                    {
                        try { return p.StartTime; }
                        catch { return DateTime.MinValue; }
                    })
                    .Select(p => p.Id)
                    .FirstOrDefault();
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }
        catch
        {
            return 0;
        }
    }

    private void WriteLog(string message)
    {
        try
        {
            _log?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
        catch
        {
            // Logging must never crash the watchdog.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);

        try
        {
            Stop();
        }
        catch
        {
            // Best-effort cleanup.
        }

        _watchdog.Dispose();
        GC.SuppressFinalize(this);
    }
}
