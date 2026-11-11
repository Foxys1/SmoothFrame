using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Service for managing process RAM working set paging priority via kernel32!SetProcessInformation
/// (ProcessMemoryPriority = 0, MEMORY_PRIORITY_INFORMATION).
/// R1: 100% in-memory operation with 0 registry/service modifications.
/// - MEMORY_PRIORITY_NORMAL (5): Protects active game RAM working set pages from being paged out to disk/pagefile.
/// - MEMORY_PRIORITY_VERY_LOW (1): Deprioritizes background applications (Chrome, Discord, Spotify) so their pages yield to the game.
/// Guaranteed rollback to 5 (MEMORY_PRIORITY_NORMAL) on game close, Alt+Tab relaxation, or deactivation.
/// </summary>
public static class ProcessMemoryPriorityService
{
    /// <summary>
    /// Sets memory priority for the given process handle.
    /// Values typically range from 1 (VERY_LOW) to 5 (NORMAL).
    /// </summary>
    public static bool SetProcessMemoryPriority(IntPtr hProcess, uint memoryPriority)
    {
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            var info = new NativeMethods.MEMORY_PRIORITY_INFORMATION
            {
                MemoryPriority = memoryPriority
            };

            uint size = (uint)Marshal.SizeOf<NativeMethods.MEMORY_PRIORITY_INFORMATION>();
            bool success = NativeMethods.SetProcessInformation(
                hProcess,
                NativeMethods.ProcessMemoryPriority,
                ref info,
                size);

            if (!success)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ProcessMemoryPriority] SetProcessInformation failed for priority {memoryPriority}, Win32 error: {err}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessMemoryPriority] SetProcessMemoryPriority exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Alias for SetProcessMemoryPriority.
    /// </summary>
    public static bool SetMemoryPriority(IntPtr hProcess, uint memoryPriority)
        => SetProcessMemoryPriority(hProcess, memoryPriority);

    /// <summary>
    /// Queries the current memory priority of a process handle.
    /// Returns true if successful, false otherwise (with out priority defaulting to MEMORY_PRIORITY_NORMAL = 5).
    /// </summary>
    public static bool TryGetProcessMemoryPriority(IntPtr hProcess, out uint memoryPriority)
    {
        memoryPriority = NativeMethods.MEMORY_PRIORITY_NORMAL;
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            var info = new NativeMethods.MEMORY_PRIORITY_INFORMATION();
            uint size = (uint)Marshal.SizeOf<NativeMethods.MEMORY_PRIORITY_INFORMATION>();
            bool success = NativeMethods.GetProcessInformation(
                hProcess,
                NativeMethods.ProcessMemoryPriority,
                ref info,
                size);

            if (success && info.MemoryPriority > 0)
            {
                memoryPriority = info.MemoryPriority;
                return true;
            }

            int err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"[ProcessMemoryPriority] GetProcessInformation failed, Win32 error: {err}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessMemoryPriority] TryGetProcessMemoryPriority exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Queries current process memory priority or returns default MEMORY_PRIORITY_NORMAL (5).
    /// </summary>
    public static uint GetProcessMemoryPriority(IntPtr hProcess)
    {
        TryGetProcessMemoryPriority(hProcess, out uint priority);
        return priority;
    }

    /// <summary>
    /// Restores a previously captured memory priority on a process handle.
    /// </summary>
    public static bool RestoreProcessMemoryPriority(IntPtr hProcess, uint originalPriority)
    {
        uint target = (originalPriority >= NativeMethods.MEMORY_PRIORITY_VERY_LOW && originalPriority <= NativeMethods.MEMORY_PRIORITY_NORMAL)
            ? originalPriority
            : NativeMethods.MEMORY_PRIORITY_NORMAL;

        return SetProcessMemoryPriority(hProcess, target);
    }

    /// <summary>
    /// Sets memory priority for a process by PID with safe handle management.
    /// Captures original priority in out parameter.
    /// </summary>
    public static bool SetProcessMemoryPriority(int processId, uint memoryPriority, out uint originalPriority)
    {
        originalPriority = NativeMethods.MEMORY_PRIORITY_NORMAL;

        try
        {
            uint access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION;
            var hProcess = NativeMethods.OpenProcess(access, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                hProcess = NativeMethods.OpenProcess(access, false, processId);
            }
            if (hProcess == IntPtr.Zero)
            {
                hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_INFORMATION, false, processId);
            }

            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                TryGetProcessMemoryPriority(hProcess, out originalPriority);
                return SetProcessMemoryPriority(hProcess, memoryPriority);
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessMemoryPriority] Failed to set priority for PID {processId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sets memory priority for a process by PID with safe handle management.
    /// </summary>
    public static bool SetMemoryPriority(int processId, uint memoryPriority)
    {
        return SetProcessMemoryPriority(processId, memoryPriority, out _);
    }

    /// <summary>
    /// Restores memory priority by PID with safe handle management.
    /// </summary>
    public static bool RestoreProcessMemoryPriority(int processId, uint originalPriority)
    {
        try
        {
            uint access = NativeMethods.PROCESS_SET_INFORMATION;
            var hProcess = NativeMethods.OpenProcess(access, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                return RestoreProcessMemoryPriority(hProcess, originalPriority);
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessMemoryPriority] Failed to restore priority for PID {processId}: {ex.Message}");
            return false;
        }
    }
}
