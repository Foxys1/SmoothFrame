using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Native;
using SmoothFrame.Core.Safety;

namespace SmoothFrame.Core.Services;

public class OptimizationOptions
{
    public bool HighPrecisionTimer { get; set; } = true;
    public bool HighPerformancePowerPlan { get; set; } = true;
    public bool ElevateGamePriority { get; set; } = true;
    public bool DeprioritizeBackgroundApps { get; set; } = true;
    public bool IsolatePCores { get; set; } = false;
}

public class OptimizationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int AlteredProcessCount { get; set; }
    public double ActiveTimerMs { get; set; }
}

public class OptimizationCoordinator : IDisposable, IAsyncDisposable
{
    private readonly TimerResolutionService _timerService = new();
    private readonly PowerPlanService _powerPlanService = new();
    private readonly ProcessTunerService _processTuner = new();
    private readonly SnapshotManager _snapshotManager;
    private readonly SemaphoreSlim _deactivationLock = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed = 0;

    private CancellationTokenSource? _watcherCts;
    private Task? _watcherTask;
    private SystemStateSnapshot? _currentSnapshot;

    public bool IsActive { get; private set; }
    public GameInfo? CurrentOptimizedGame { get; private set; }
    public OptimizationOptions Options { get; set; } = new();

    public event Action<bool>? StateChanged;
    public event Action<string>? LogNotification;

    public double CurrentTimerMs => _timerService.CurrentResolutionMs;
    public Guid CurrentPowerPlan => _powerPlanService.CurrentPlan;

    public OptimizationCoordinator(SnapshotManager? snapshotManager = null)
    {
        _snapshotManager = snapshotManager ?? new SnapshotManager();
        // Safe crash recovery check on startup
        CheckAndRecoverFromCrash();

        try
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }
        catch { }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            Dispose();
        }
        catch { }
    }

    public void CheckAndRecoverFromCrash()
    {
        try
        {
            if (_snapshotManager.HasDanglingSnapshot())
            {
                var snapshot = _snapshotManager.LoadActiveSnapshot();
                if (snapshot == null)
                {
                    Debug.WriteLine("Crash recovery: Found dangling snapshot file but failed to deserialize. Preserving file.");
                    return;
                }

                LogNotification?.Invoke("Обнаружена незавершенная сессия! Восстанавливаем системные параметры...");
                bool allRestored = true;

                if (snapshot.HasPowerPlanChanged && snapshot.OriginalPowerPlanGuid != Guid.Empty)
                {
                    bool powerRestored = _powerPlanService.RestoreSpecificPlan(snapshot.OriginalPowerPlanGuid);
                    if (!powerRestored)
                    {
                        allRestored = false;
                        Debug.WriteLine("Crash recovery: Failed to restore power plan.");
                    }
                }

                if (snapshot.HasTimerResolutionChanged)
                {
                    _timerService.RevertFromSnapshot(snapshot.OriginalTimerResolution);
                }

                if (snapshot.AlteredProcesses.Any())
                {
                    _processTuner.RestoreProcesses(snapshot.AlteredProcesses);
                }

                if (allRestored)
                {
                    _snapshotManager.ClearSnapshot();
                    LogNotification?.Invoke("Система успешно возвращена в исходное состояние.");
                }
                else
                {
                    LogNotification?.Invoke("Предупреждение: Не все системные параметры удалось восстановить. Снимок сохранен.");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Crash recovery error: {ex.Message}");
        }
    }

    public async Task<OptimizationResult> ActivateAsync(GameInfo game, OptimizationOptions? options = null)
    {
        if (game == null)
        {
            return new OptimizationResult { Success = false, Message = "Игра не указана." };
        }

        if (_disposed != 0)
        {
            return new OptimizationResult { Success = false, Message = "Координатор уже освобожден." };
        }

        bool lockAcquired = false;
        try
        {
            lockAcquired = await _deactivationLock.WaitAsync(TimeSpan.FromSeconds(5), _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            return new OptimizationResult { Success = false, Message = "Координатор уже освобожден." };
        }

        if (!lockAcquired || _disposed != 0)
        {
            if (lockAcquired)
            {
                try { _deactivationLock.Release(); } catch { }
            }
            return new OptimizationResult { Success = false, Message = "Координатор занят или уже освобожден." };
        }

        try
        {
            if (IsActive)
            {
                return new OptimizationResult { Success = false, Message = "Оптимизация уже активна." };
            }

            var opt = options ?? Options;
            CurrentOptimizedGame = game;

            Guid? originalPlanGuid = null;
            if (opt.HighPerformancePowerPlan)
            {
                if (PowerPlanService.TryGetActiveSchemeGuid(out var activeGuid))
                {
                    originalPlanGuid = activeGuid;
                }
                else
                {
                    // Cannot query active scheme safely; skip power plan alteration to prevent corruption
                    opt.HighPerformancePowerPlan = false;
                    LogNotification?.Invoke("Предупреждение: Не удалось определить текущую схему питания. Оптимизация схемы питания пропущена.");
                }
            }
            else
            {
                originalPlanGuid = PowerPlanService.GetActiveSchemeGuid();
            }

            _currentSnapshot = new SystemStateSnapshot
            {
                OptimizedGameName = game.Name,
                OriginalPowerPlanGuid = originalPlanGuid ?? Guid.Empty,
                OriginalTimerResolution = _timerService.CurrentResolution100Ns,
                HasTimerResolutionChanged = false,
                HasPowerPlanChanged = false
            };

            // WAL: Persist initial snapshot to disk BEFORE mutating OS parameters
            if (!_snapshotManager.SaveSnapshot(_currentSnapshot))
            {
                _currentSnapshot = null;
                CurrentOptimizedGame = null;
                return new OptimizationResult
                {
                    Success = false,
                    Message = "Не удалось сохранить снимок состояния на диск. Активация отменена для безопасности."
                };
            }

            // 1. High precision timer (0.5 ms)
            if (opt.HighPrecisionTimer)
            {
                _currentSnapshot.HasTimerResolutionChanged = _timerService.EnableHighPrecision();
            }

            // 2. High performance power scheme
            if (opt.HighPerformancePowerPlan)
            {
                _currentSnapshot.HasPowerPlanChanged = _powerPlanService.SwitchToHighPerformance();
            }

            // 3. Deprioritize background non-critical apps
            if (opt.DeprioritizeBackgroundApps)
            {
                var deprioritized = _processTuner.DeprioritizeBackgroundApps();
                _currentSnapshot.AlteredProcesses.AddRange(deprioritized);
            }

            // Update on-disk snapshot after mutations applied
            _snapshotManager.SaveSnapshot(_currentSnapshot);

            // 4. Start background game process watcher
            _watcherCts = new CancellationTokenSource();
            _watcherTask = Task.Run(() => WatchGameProcessAsync(game, opt, _watcherCts.Token));

            IsActive = true;
            StateChanged?.Invoke(true);

            return new OptimizationResult
            {
                Success = true,
                Message = $"Оптимизация активна для {game.Name}!",
                AlteredProcessCount = _currentSnapshot.AlteredProcesses.Count,
                ActiveTimerMs = _timerService.CurrentResolutionMs
            };
        }
        finally
        {
            try
            {
                _deactivationLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    public async Task DeactivateAsync()
    {
        if (_disposed != 0 || !IsActive) return;

        bool lockAcquired = false;
        try
        {
            lockAcquired = await _deactivationLock.WaitAsync(TimeSpan.FromSeconds(3), _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            return;
        }

        if (!lockAcquired || _disposed != 0 || !IsActive)
        {
            if (lockAcquired)
            {
                try { _deactivationLock.Release(); } catch { }
            }
            return;
        }

        try
        {
            if (!IsActive) return;

            try
            {
                if (_watcherCts != null)
                {
                    _watcherCts.Cancel();
                    if (_watcherTask != null)
                    {
                        try
                        {
                            await _watcherTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Task cancellation is expected
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Watcher task completed with exception: {ex.Message}");
                        }
                        _watcherTask = null;
                    }

                    _watcherCts.Dispose();
                    _watcherCts = null;
                }

                // 1. Revert processes
                _processTuner.RestoreProcesses();

                // 2. Revert power scheme
                _powerPlanService.RestoreOriginalPlan();

                // 3. Revert timer resolution
                _timerService.Revert();

                // 4. Clear snapshot file
                _snapshotManager.ClearSnapshot();
                _currentSnapshot = null;
                CurrentOptimizedGame = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deactivating optimization: {ex.Message}");
            }
            finally
            {
                IsActive = false;
                StateChanged?.Invoke(false);
                LogNotification?.Invoke("Оптимизация выключена. Все системные параметры возвращены.");
            }
        }
        finally
        {
            try
            {
                _deactivationLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Ignored if disposed during shutdown
            }
        }

        await Task.CompletedTask;
    }

    private async Task WatchGameProcessAsync(GameInfo game, OptimizationOptions opt, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(game?.ProcessName))
        {
            return;
        }

        var procName = game.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? game.ProcessName[..^4]
            : game.ProcessName;

        bool gameOptimized = false;

        while (!token.IsCancellationRequested)
        {
            Process[]? processes = null;
            try
            {
                processes = Process.GetProcessesByName(procName);
                var targetProc = processes.FirstOrDefault(p => !p.HasExited);

                if (targetProc != null)
                {
                    if (!gameOptimized && opt.ElevateGamePriority)
                    {
                        var state = _processTuner.OptimizeGameProcess(targetProc, opt.IsolatePCores);
                        if (state != null && _currentSnapshot != null)
                        {
                            _currentSnapshot.AlteredProcesses.Add(state);
                            _currentSnapshot.OptimizedGameProcessId = targetProc.Id;
                            _snapshotManager.SaveSnapshot(_currentSnapshot);
                            LogNotification?.Invoke($"Процесс игры {game.Name} обнаружен и переведен в High Priority!");
                        }
                        gameOptimized = true;
                    }
                }
                else
                {
                    // Game is not currently running or has exited
                    if (gameOptimized)
                    {
                        LogNotification?.Invoke($"Игра {game.Name} закрыта.");
                        gameOptimized = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Watcher error: {ex.Message}");
            }
            finally
            {
                if (processes != null)
                {
                    foreach (var p in processes)
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }

            try
            {
                await Task.Delay(1500, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.Cancel();

        bool lockAcquired = false;
        try
        {
            lockAcquired = await _deactivationLock.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { }

        try
        {
            if (IsActive)
            {
                try
                {
                    if (_watcherCts != null)
                    {
                        try { _watcherCts.Cancel(); } catch (ObjectDisposedException) { }
                        if (_watcherTask != null)
                        {
                            try
                            {
                                await _watcherTask.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Watcher task completed with exception: {ex.Message}");
                            }
                            _watcherTask = null;
                        }

                        _watcherCts.Dispose();
                        _watcherCts = null;
                    }

                    _processTuner.RestoreProcesses();
                    _powerPlanService.RestoreOriginalPlan();
                    _timerService.Revert();
                    _snapshotManager.ClearSnapshot();
                    _currentSnapshot = null;
                    CurrentOptimizedGame = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deactivating in DisposeAsync: {ex.Message}");
                }
                finally
                {
                    IsActive = false;
                    StateChanged?.Invoke(false);
                    LogNotification?.Invoke("Оптимизация выключена. Все системные параметры возвращены.");
                }
            }
        }
        finally
        {
            if (lockAcquired)
            {
                try { _deactivationLock.Release(); } catch { }
            }

            try
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            }
            catch { }
            _timerService.Dispose();
            _powerPlanService.Dispose();
            try
            {
                _watcherCts?.Dispose();
            }
            catch { }
            _watcherCts = null;
            try
            {
                _disposeCts.Dispose();
            }
            catch { }
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.Cancel();

        bool lockAcquired = false;
        try
        {
            lockAcquired = _deactivationLock.Wait(TimeSpan.FromSeconds(3));
        }
        catch (ObjectDisposedException) { }

        try
        {
            if (IsActive)
            {
                try
                {
                    try
                    {
                        _watcherCts?.Cancel();
                    }
                    catch (ObjectDisposedException) { }

                    _processTuner.RestoreProcesses();
                    _powerPlanService.RestoreOriginalPlan();
                    _timerService.Revert();
                    _snapshotManager.ClearSnapshot();
                    _currentSnapshot = null;
                    CurrentOptimizedGame = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deactivating in Dispose: {ex.Message}");
                }
                finally
                {
                    IsActive = false;
                    StateChanged?.Invoke(false);
                    LogNotification?.Invoke("Оптимизация выключена. Все системные параметры возвращены.");
                }
            }
        }
        finally
        {
            if (lockAcquired)
            {
                try { _deactivationLock.Release(); } catch { }
            }

            try
            {
                AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            }
            catch { }
            _timerService.Dispose();
            _powerPlanService.Dispose();
            try
            {
                _watcherCts?.Dispose();
            }
            catch { }
            _watcherCts = null;
            try
            {
                _disposeCts.Dispose();
            }
            catch { }
            GC.SuppressFinalize(this);
        }
    }
}
