using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Service for managing Windows Process Power Throttling & Quality of Service (QoS).
/// R1: 100% in-memory operation with 0 registry keys touched.
/// - HighQoS: Disables execution speed throttling and timer resolution ignoring for the active game.
/// - EcoQoS: Enables Efficiency Mode for background apps (Discord, Chrome, Telegram, Spotify, etc.) to minimize CPU contention.
/// - ResetQoS: Restores default Windows power throttling heuristics.
/// </summary>
public static class ProcessQosService
{
    /// <summary>
    /// Applies HighQoS (disables execution speed throttling and timer resolution ignore)
    /// to the specified process handle.
    /// </summary>
    public static bool SetHighQoS(IntPtr hProcess)
    {
        return SetQoS(
            hProcess,
            controlMask: NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED | NativeMethods.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
            stateMask: 0);
    }

    /// <summary>
    /// Applies EcoQoS (Efficiency Mode) to the specified background process handle.
    /// </summary>
    public static bool SetEcoQoS(IntPtr hProcess)
    {
        return SetQoS(
            hProcess,
            controlMask: NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            stateMask: NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED);
    }

    /// <summary>
    /// Resets process power throttling to default Windows system heuristics.
    /// </summary>
    public static bool ResetQoS(IntPtr hProcess)
    {
        return SetQoS(hProcess, controlMask: 0, stateMask: 0);
    }

    /// <summary>
    /// Queries the current power throttling state of a process.
    /// </summary>
    public static bool GetQoS(IntPtr hProcess, out uint controlMask, out uint stateMask)
    {
        controlMask = 0;
        stateMask = 0;

        if (hProcess == IntPtr.Zero)
            return false;

        try
        {
            var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
            {
                Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = 0,
                StateMask = 0
            };

            uint size = (uint)Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>();
            if (NativeMethods.GetProcessInformation(hProcess, NativeMethods.ProcessPowerThrottling, ref state, size))
            {
                controlMask = state.ControlMask;
                stateMask = state.StateMask;
                return true;
            }

            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[ProcessQos] GetProcessInformation failed, Win32 error: {err}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessQos] GetQoS exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores a previously captured QoS state or resets to system defaults.
    /// </summary>
    public static bool RestoreQoS(IntPtr hProcess, uint originalControlMask, uint originalStateMask)
    {
        return SetQoS(hProcess, originalControlMask, originalStateMask);
    }

    /// <summary>
    /// Core native helper invoking SetProcessInformation with PROCESS_POWER_THROTTLING_STATE.
    /// Operates purely in memory.
    /// </summary>
    public static bool SetQoS(IntPtr hProcess, uint controlMask, uint stateMask)
    {
        if (hProcess == IntPtr.Zero)
            return false;

        try
        {
            var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
            {
                Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = controlMask,
                StateMask = stateMask
            };

            uint size = (uint)Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>();
            bool success = NativeMethods.SetProcessInformation(hProcess, NativeMethods.ProcessPowerThrottling, ref state, size);
            if (!success)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ProcessQos] SetProcessInformation failed (Class={NativeMethods.ProcessPowerThrottling}, ControlMask=0x{controlMask:X}, StateMask=0x{stateMask:X}), Win32 error: {err}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessQos] SetQoS exception: {ex.Message}");
            return false;
        }
    }
}
