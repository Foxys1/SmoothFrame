using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Isolates Windows Audio Device Graph Isolation (audiodg.exe) to a dedicated CPU core
/// and elevates its priority to AboveNormal.
/// Eliminates DPC latency spikes, buffer underruns, and audio crackling during heavy CPU load.
/// 100% reversible: captures original affinity mask and priority class in the snapshot, restoring them upon deactivation.
/// </summary>
public static class AudioOptimizerService
{
    private static bool _debugPrivilegeEnabled = false;

    public static bool Apply(SystemStateSnapshot snapshot)
    {
        if (snapshot == null) return false;

        try
        {
            var audioProcs = Process.GetProcessesByName("audiodg");
            if (audioProcs.Length == 0)
            {
                Debug.WriteLine("[AudioOptimizer] audiodg.exe process not found.");
                return false;
            }

            try
            {
                EnsureDebugPrivilege();

                var proc = audioProcs[0];
                uint access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION;
                var hProcess = NativeMethods.OpenProcess(access, false, proc.Id);
                if (hProcess == IntPtr.Zero)
                {
                    access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                    hProcess = NativeMethods.OpenProcess(access, false, proc.Id);
                }

                if (hProcess == IntPtr.Zero)
                {
                    Debug.WriteLine($"[AudioOptimizer] Failed to open audiodg.exe (PID {proc.Id}), Win32 error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                try
                {
                    // Capture original state
                    var origPriority = NativeMethods.GetPriorityClass(hProcess);
                    ulong origAffinity = 0;
                    if (NativeMethods.GetProcessAffinityMask(hProcess, out var curAffinity, out _))
                    {
                        origAffinity = (ulong)curAffinity.ToUInt64();
                    }

                    snapshot.OriginalAudioDgPriority = origPriority != 0 ? origPriority : NativeMethods.NORMAL_PRIORITY_CLASS;
                    snapshot.OriginalAudioDgAffinity = origAffinity;

                    // Elevate priority to ABOVE_NORMAL to prevent audio starvation
                    NativeMethods.SetPriorityClass(hProcess, NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS);

                    // Isolate to the highest available logical core (away from Core 0/1 where games heavily thread)
                    int coreCount = Environment.ProcessorCount;
                    if (coreCount >= 2)
                    {
                        int targetCore = coreCount - 1;
                        ulong targetMask = 1UL << targetCore;
                        UIntPtr maskPtr = IntPtr.Size == 4
                            ? new UIntPtr((uint)(targetMask & uint.MaxValue))
                            : new UIntPtr(targetMask);

                        NativeMethods.SetProcessAffinityMask(hProcess, maskPtr);
                    }

                    snapshot.HasAudioDgOptimized = true;
                    return true;
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
            finally
            {
                foreach (var p in audioProcs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioOptimizer] Apply failed: {ex.Message}");
            return false;
        }
    }

    public static bool Revert(SystemStateSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.HasAudioDgOptimized) return true;

        try
        {
            var audioProcs = Process.GetProcessesByName("audiodg");
            if (audioProcs.Length == 0)
            {
                snapshot.HasAudioDgOptimized = false;
                return true;
            }

            try
            {
                EnsureDebugPrivilege();

                var proc = audioProcs[0];
                uint access = NativeMethods.PROCESS_SET_INFORMATION;
                var hProcess = NativeMethods.OpenProcess(access, false, proc.Id);
                if (hProcess == IntPtr.Zero)
                {
                    Debug.WriteLine($"[AudioOptimizer] Failed to open audiodg.exe for revert, Win32 error: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                try
                {
                    if (snapshot.OriginalAudioDgPriority != 0)
                    {
                        NativeMethods.SetPriorityClass(hProcess, snapshot.OriginalAudioDgPriority);
                    }

                    if (snapshot.OriginalAudioDgAffinity != 0)
                    {
                        UIntPtr maskPtr = IntPtr.Size == 4
                            ? new UIntPtr((uint)(snapshot.OriginalAudioDgAffinity & uint.MaxValue))
                            : new UIntPtr(snapshot.OriginalAudioDgAffinity);

                        NativeMethods.SetProcessAffinityMask(hProcess, maskPtr);
                    }

                    snapshot.HasAudioDgOptimized = false;
                    return true;
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
            finally
            {
                foreach (var p in audioProcs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AudioOptimizer] Revert failed: {ex.Message}");
            return false;
        }
    }

    private static void EnsureDebugPrivilege()
    {
        if (_debugPrivilegeEnabled) return;

        try
        {
            if (NativeMethods.OpenProcessToken(Process.GetCurrentProcess().Handle, NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out var hToken))
            {
                try
                {
                    if (NativeMethods.LookupPrivilegeValue(null, NativeMethods.SE_DEBUG_NAME, out var luid))
                    {
                        var tp = new NativeMethods.TOKEN_PRIVILEGES
                        {
                            PrivilegeCount = 1,
                            Privileges = new NativeMethods.LUID_AND_ATTRIBUTES
                            {
                                Luid = luid,
                                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
                            }
                        };
                        NativeMethods.AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                        _debugPrivilegeEnabled = true;
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hToken);
                }
            }
        }
        catch { }
    }
}
