using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Native;
using SmoothFrame.Core.Safety;

namespace SmoothFrame.Core.Services;

public class ProcessTunerService
{
    private static readonly HashSet<string> KnownBackgroundApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi",
        "discord", "telegram",
        "steamwebhelper", "epicgameslauncher", "origin", "eadesktop", "battlenet",
        "torrent", "utorrent", "qbittorrent", "spotify"
    };

    public List<ProcessState> AlteredProcesses { get; } = new();

    private static DateTime? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public ProcessState? OptimizeGameProcess(Process gameProcess, bool isolatePCores = false)
    {
        try
        {
            if (gameProcess == null || gameProcess.HasExited) return null;

            if (ProcessBlacklist.IsProtected(gameProcess.ProcessName))
            {
                Debug.WriteLine($"[ProcessTuner] Game process '{gameProcess.ProcessName}' is protected by blacklist. Aborting optimization.");
                return null;
            }

            var pid = gameProcess.Id;
            uint accessRights = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
            var hProcess = NativeMethods.OpenProcess(accessRights, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                accessRights = NativeMethods.PROCESS_SET_INFORMATION;
                hProcess = NativeMethods.OpenProcess(accessRights, false, pid);
            }

            if (hProcess == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"Failed to open game process {gameProcess.ProcessName} (PID {pid}), Win32 error: {err}");
                return null;
            }

            try
            {
                var origPriority = NativeMethods.GetPriorityClass(hProcess);
                ulong origAffinityMask = 0;
                if (NativeMethods.GetProcessAffinityMask(hProcess, out var origAffinity, out _))
                {
                    origAffinityMask = (ulong)origAffinity.ToUInt64();
                }

                bool prioritySet = false;
                if (origPriority == 0)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"Failed to query priority class for PID {pid}, Win32 error: {err}");
                }
                else
                {
                    prioritySet = NativeMethods.SetPriorityClass(hProcess, NativeMethods.HIGH_PRIORITY_CLASS);
                    if (!prioritySet)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Debug.WriteLine($"SetPriorityClass HIGH failed for PID {pid}, Win32 error: {err}");
                    }
                }

                bool affinitySet = false;
                int coreCount = Environment.ProcessorCount;
                if (isolatePCores && coreCount >= 8)
                {
                    // Common Intel 12/13/14th gen: first half of threads are P-cores + Hyperthreading
                    // For safety, allocate the first 8-16 logical cores
                    ulong mask = 0;
                    int targetCores = Math.Min(coreCount, 16);
                    for (int i = 0; i < targetCores; i++)
                    {
                        mask |= (1UL << i);
                    }
                    UIntPtr affinityPtr = IntPtr.Size == 4
                        ? new UIntPtr((uint)(mask & uint.MaxValue))
                        : new UIntPtr(mask);
                    affinitySet = NativeMethods.SetProcessAffinityMask(hProcess, affinityPtr);
                    if (!affinitySet)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Debug.WriteLine($"SetProcessAffinityMask failed for PID {pid}, Win32 error: {err}");
                    }
                }

                // Only record into AlteredProcesses if priority/affinity change actually succeeded
                if (prioritySet || affinitySet)
                {
                    var state = new ProcessState
                    {
                        ProcessId = pid,
                        ProcessName = gameProcess.ProcessName,
                        StartTime = TryGetStartTime(gameProcess),
                        ExecutablePath = TryGetExecutablePath(gameProcess),
                        OriginalPriorityClass = prioritySet ? origPriority : 0,
                        OriginalAffinityMask = affinitySet ? origAffinityMask : 0
                    };

                    AlteredProcesses.Add(state);
                    return state;
                }

                return null;
            }
            finally
            {
                NativeMethods.CloseHandle(hProcess);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to optimize game process: {ex.Message}");
            return null;
        }
    }

    public List<ProcessState> DeprioritizeBackgroundApps()
    {
        var deprioritized = new List<ProcessState>();

        try
        {
            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                try
                {
                    var name = proc.ProcessName;

                    if (ProcessBlacklist.IsProtected(name))
                        continue;

                    if (!KnownBackgroundApps.Contains(name))
                        continue;

                    uint accessRights = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                    var hProcess = NativeMethods.OpenProcess(accessRights, false, proc.Id);
                    if (hProcess == IntPtr.Zero)
                    {
                        accessRights = NativeMethods.PROCESS_SET_INFORMATION;
                        hProcess = NativeMethods.OpenProcess(accessRights, false, proc.Id);
                    }

                    if (hProcess == IntPtr.Zero)
                    {
                        int err = Marshal.GetLastWin32Error();
                        Debug.WriteLine($"Failed to open background process {name} (PID {proc.Id}), Win32 error: {err}");
                        continue;
                    }

                    try
                    {
                        var origPriority = NativeMethods.GetPriorityClass(hProcess);
                        ulong origAffinityMask = 0;
                        if (NativeMethods.GetProcessAffinityMask(hProcess, out var origAffinity, out _))
                        {
                            origAffinityMask = (ulong)origAffinity.ToUInt64();
                        }

                        // Only alter if currently NORMAL or ABOVE_NORMAL
                        if (origPriority == NativeMethods.NORMAL_PRIORITY_CLASS ||
                            origPriority == NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS)
                        {
                            // Set to Below Normal for smooth background execution without CPU hogging
                            bool prioritySet = NativeMethods.SetPriorityClass(hProcess, NativeMethods.BELOW_NORMAL_PRIORITY_CLASS);
                            if (prioritySet)
                            {
                                var state = new ProcessState
                                {
                                    ProcessId = proc.Id,
                                    ProcessName = name,
                                    StartTime = TryGetStartTime(proc),
                                    ExecutablePath = TryGetExecutablePath(proc),
                                    OriginalPriorityClass = origPriority,
                                    OriginalAffinityMask = 0
                                };

                                AlteredProcesses.Add(state);
                                deprioritized.Add(state);
                            }
                            else
                            {
                                int err = Marshal.GetLastWin32Error();
                                Debug.WriteLine($"SetPriorityClass BELOW_NORMAL failed for PID {proc.Id}, Win32 error: {err}");
                            }
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing background app {proc.Id}: {ex.Message}");
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed deprioritizing background apps: {ex.Message}");
        }

        return deprioritized;
    }

    public void RestoreProcesses(IEnumerable<ProcessState>? statesToRestore = null)
    {
        var list = (statesToRestore ?? AlteredProcesses).ToList();

        foreach (var state in list)
        {
            // 1. Unconditional check against ProcessBlacklist
            if (ProcessBlacklist.IsProtected(state.ProcessName))
            {
                Debug.WriteLine($"[ProcessTuner] Process '{state.ProcessName}' (PID {state.ProcessId}) is protected by blacklist. Skipping restoration.");
                continue;
            }

            // 2. Verify process existence and guard against PID recycling
            Process proc;
            try
            {
                proc = Process.GetProcessById(state.ProcessId);
            }
            catch (ArgumentException)
            {
                // Process has exited, nothing to restore
                continue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessTuner] Error opening PID {state.ProcessId}: {ex.Message}");
                continue;
            }

            using (proc)
            {
                try
                {
                    if (proc.HasExited)
                    {
                        continue;
                    }

                    if (ProcessBlacklist.IsProtected(proc.ProcessName))
                    {
                        Debug.WriteLine($"[ProcessTuner] Running process '{proc.ProcessName}' (PID {state.ProcessId}) is protected by blacklist. Skipping restoration.");
                        continue;
                    }

                    // Verify process name matches
                    if (!string.Equals(proc.ProcessName, state.ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"[PID Recycling Guard] PID {state.ProcessId} name mismatch: expected '{state.ProcessName}', found '{proc.ProcessName}'. Skipping.");
                        continue;
                    }

                    // Verify StartTime matches within 1 second tolerance
                    if (state.StartTime.HasValue)
                    {
                        var procStartTime = TryGetStartTime(proc);
                        if (!procStartTime.HasValue || Math.Abs((procStartTime.Value.ToUniversalTime() - state.StartTime.Value.ToUniversalTime()).TotalSeconds) > 1.0)
                        {
                            Debug.WriteLine($"[PID Recycling Guard] PID {state.ProcessId} start time mismatch: expected '{state.StartTime.Value}', found '{procStartTime}'. Skipping.");
                            continue;
                        }
                    }

                    // Verify ExecutablePath if recorded
                    if (!string.IsNullOrEmpty(state.ExecutablePath))
                    {
                        var currentPath = TryGetExecutablePath(proc);
                        if (!string.IsNullOrEmpty(currentPath) &&
                            !string.Equals(currentPath, state.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"[PID Recycling Guard] PID {state.ProcessId} executable path mismatch: expected '{state.ExecutablePath}', found '{currentPath}'. Skipping.");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PID Recycling Guard] PID {state.ProcessId} verification exception: {ex.Message}. Skipping.");
                    continue;
                }

                // Process verified - safe to restore
                uint accessRights = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                var hProcess = NativeMethods.OpenProcess(accessRights, false, state.ProcessId);
                if (hProcess == IntPtr.Zero)
                {
                    accessRights = NativeMethods.PROCESS_SET_INFORMATION;
                    hProcess = NativeMethods.OpenProcess(accessRights, false, state.ProcessId);
                }

                if (hProcess == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[ProcessTuner] Failed to open PID {state.ProcessId} for restore, Win32 error: {err}");
                    continue;
                }

                try
                {
                    if (state.OriginalPriorityClass != 0)
                    {
                        if (!NativeMethods.SetPriorityClass(hProcess, state.OriginalPriorityClass))
                        {
                            int err = Marshal.GetLastWin32Error();
                            Debug.WriteLine($"[ProcessTuner] SetPriorityClass failed restoring PID {state.ProcessId} to {state.OriginalPriorityClass}, Win32 error: {err}");
                        }
                    }

                    if (state.OriginalAffinityMask != 0)
                    {
                        UIntPtr affinityPtr = IntPtr.Size == 4
                            ? new UIntPtr((uint)(state.OriginalAffinityMask & uint.MaxValue))
                            : new UIntPtr(state.OriginalAffinityMask);
                        if (!NativeMethods.SetProcessAffinityMask(hProcess, affinityPtr))
                        {
                            int err = Marshal.GetLastWin32Error();
                            Debug.WriteLine($"[ProcessTuner] SetProcessAffinityMask failed restoring PID {state.ProcessId} to {state.OriginalAffinityMask:X}, Win32 error: {err}");
                        }
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
        }

        if (statesToRestore == null)
        {
            AlteredProcesses.Clear();
        }
        else
        {
            var restoredPids = list.Select(p => p.ProcessId).ToHashSet();
            AlteredProcesses.RemoveAll(p => restoredPids.Contains(p.ProcessId));
        }
    }
}
