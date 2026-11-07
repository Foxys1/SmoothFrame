using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Manages native process I/O priority via ntdll!NtSetInformationProcess (ProcessIoPriority = 21).
/// Accelerates asset streaming (textures, models, world chunks) for active games (High I/O priority)
/// and suppresses background processes (Very Low I/O priority) to eliminate micro-stutters.
/// 100% in-memory kernel property: leaves 0 traces in registry and automatically resets on process exit.
/// </summary>
public static class ProcessIoPriorityService
{
    public static int GetProcessIoPriority(IntPtr hProcess)
    {
        if (hProcess == IntPtr.Zero) return NativeMethods.IoPriorityNormal;

        try
        {
            int status = NativeMethods.NtQueryInformationProcess(
                hProcess,
                NativeMethods.ProcessIoPriority,
                out int priority,
                sizeof(int),
                out _);

            if (status == 0) // STATUS_SUCCESS
            {
                return priority;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessIoPriority] Query failed: {ex.Message}");
        }

        return NativeMethods.IoPriorityNormal;
    }

    public static bool SetProcessIoPriority(IntPtr hProcess, int ioPriority)
    {
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            int targetPriority = Math.Clamp(ioPriority, NativeMethods.IoPriorityVeryLow, NativeMethods.IoPriorityHigh);
            int status = NativeMethods.NtSetInformationProcess(
                hProcess,
                NativeMethods.ProcessIoPriority,
                ref targetPriority,
                sizeof(int));

            return status == 0; // STATUS_SUCCESS
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessIoPriority] Set failed: {ex.Message}");
            return false;
        }
    }

    public static bool SetProcessIoPriority(Process process, int ioPriority, out int originalPriority)
    {
        originalPriority = NativeMethods.IoPriorityNormal;
        if (process == null) return false;

        try
        {
            if (process.HasExited) return false;

            uint access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION;
            var hProcess = NativeMethods.OpenProcess(access, false, process.Id);
            if (hProcess == IntPtr.Zero)
            {
                access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                hProcess = NativeMethods.OpenProcess(access, false, process.Id);
            }

            if (hProcess == IntPtr.Zero)
            {
                hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_INFORMATION, false, process.Id);
            }

            if (hProcess == IntPtr.Zero) return false;

            try
            {
                originalPriority = GetProcessIoPriority(hProcess);
                return SetProcessIoPriority(hProcess, ioPriority);
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ProcessIoPriority] Error setting priority on PID {process.Id}: {ex.Message}");
            return false;
        }
    }

    public static bool RestoreProcessIoPriority(int processId, int originalPriority)
    {
        try
        {
            uint access = NativeMethods.PROCESS_SET_INFORMATION;
            var hProcess = NativeMethods.OpenProcess(access, false, processId);
            if (hProcess == IntPtr.Zero) return false;

            try
            {
                return SetProcessIoPriority(hProcess, originalPriority);
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }
        catch
        {
            return false;
        }
    }
}
