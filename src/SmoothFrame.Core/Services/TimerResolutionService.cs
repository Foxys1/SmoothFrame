using System;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

public class TimerResolutionService : IDisposable
{
    private bool _isNtTimerAltered;
    private bool _isWinMmTimerAltered;
    private uint _originalResolution;

    public bool IsAltered => _isNtTimerAltered || _isWinMmTimerAltered;

    public uint CurrentResolution100Ns { get; private set; }
    public double CurrentResolutionMs => CurrentResolution100Ns / 10000.0;

    public TimerResolutionService()
    {
        QueryCurrentResolution();
    }

    public void QueryCurrentResolution()
    {
        try
        {
            NativeMethods.NtQueryTimerResolution(out _, out _, out var current);
            CurrentResolution100Ns = current;
        }
        catch
        {
            CurrentResolution100Ns = 156250; // default 15.625 ms
        }
    }

    public bool EnableHighPrecision(uint desired100Ns = 5000) // 5000 * 100ns = 0.5ms
    {
        if (IsAltered) return true;

        try
        {
            QueryCurrentResolution();
            _originalResolution = CurrentResolution100Ns;

            // 1. Try NT API for sub-millisecond precision (0.5 ms)
            int status = NativeMethods.NtSetTimerResolution(desired100Ns, true, out var current);
            if (status == 0) // STATUS_SUCCESS
            {
                CurrentResolution100Ns = current;
                _isNtTimerAltered = true;
                return true;
            }

            // 2. Fallback to winmm 1ms
            if (NativeMethods.TimeBeginPeriod(1) == 0)
            {
                QueryCurrentResolution();
                _isWinMmTimerAltered = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set timer resolution: {ex.Message}");
        }

        return false;
    }

    public void Revert()
    {
        if (!IsAltered) return;

        try
        {
            if (_isNtTimerAltered)
            {
                // Revert NT timer
                NativeMethods.NtSetTimerResolution(_originalResolution, false, out _);
                _isNtTimerAltered = false;
            }

            if (_isWinMmTimerAltered)
            {
                // Revert winmm fallback only if it was successfully altered
                NativeMethods.TimeEndPeriod(1);
                _isWinMmTimerAltered = false;
            }

            QueryCurrentResolution();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to revert timer resolution: {ex.Message}");
        }
    }

    public void RevertFromSnapshot(uint originalResolution100Ns)
    {
        try
        {
            if (originalResolution100Ns > 0)
            {
                NativeMethods.NtSetTimerResolution(originalResolution100Ns, false, out _);
            }
            NativeMethods.TimeEndPeriod(1);
            _isNtTimerAltered = false;
            _isWinMmTimerAltered = false;
            QueryCurrentResolution();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to revert timer resolution from snapshot: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Revert();
        GC.SuppressFinalize(this);
    }
}
