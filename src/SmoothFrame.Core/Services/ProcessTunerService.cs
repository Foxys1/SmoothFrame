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

    private readonly object _lock = new();
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

    public static string? TryGetExecutablePath(Process process)
    {
        if (process == null) return null;

        try
        {
            string? path = process.MainModule?.FileName;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { }

        try
        {
            var hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    var sb = new System.Text.StringBuilder(1024);
                    int size = sb.Capacity;
                    if (NativeMethods.QueryFullProcessImageName(hProc, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProc);
                }
            }
        }
        catch { }

        return null;
    }

    public ProcessState? OptimizeGameProcess(Process gameProcess, bool isolatePCores = false, bool optimizeIoPriority = true, bool enableQos = true)
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
            uint accessRights = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION;
            var hProcess = NativeMethods.OpenProcess(accessRights, false, pid);
            if (hProcess == IntPtr.Zero)
            {
                accessRights = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                hProcess = NativeMethods.OpenProcess(accessRights, false, pid);
            }
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

                bool ioPrioritySet = false;
                int origIoPriority = NativeMethods.IoPriorityNormal;
                if (optimizeIoPriority)
                {
                    origIoPriority = ProcessIoPriorityService.GetProcessIoPriority(hProcess);
                    ioPrioritySet = ProcessIoPriorityService.SetProcessIoPriority(hProcess, NativeMethods.IoPriorityHigh);
                }

                bool qosSet = false;
                uint origQosControl = 0;
                uint origQosState = 0;
                if (enableQos)
                {
                    ProcessQosService.GetQoS(hProcess, out origQosControl, out origQosState);
                    qosSet = ProcessQosService.SetHighQoS(hProcess);
                }

                bool memoryPrioritySet = false;
                uint origMemoryPriority = NativeMethods.MEMORY_PRIORITY_NORMAL;
                ProcessMemoryPriorityService.TryGetProcessMemoryPriority(hProcess, out origMemoryPriority);
                memoryPrioritySet = ProcessMemoryPriorityService.SetProcessMemoryPriority(hProcess, NativeMethods.MEMORY_PRIORITY_NORMAL);

                // Only record into AlteredProcesses if priority/affinity/IO/QoS/MemoryPriority change actually succeeded
                if (prioritySet || affinitySet || ioPrioritySet || qosSet || memoryPrioritySet)
                {
                    var state = new ProcessState
                    {
                        ProcessId = pid,
                        ProcessName = gameProcess.ProcessName,
                        StartTime = TryGetStartTime(gameProcess),
                        ExecutablePath = TryGetExecutablePath(gameProcess),
                        OriginalPriorityClass = prioritySet ? origPriority : 0,
                        OriginalAffinityMask = affinitySet ? origAffinityMask : 0,
                        OriginalIoPriority = origIoPriority,
                        HasIoPriorityChanged = ioPrioritySet,
                        OriginalQosControlMask = origQosControl,
                        OriginalQosStateMask = origQosState,
                        HasQosChanged = qosSet,
                        OriginalMemoryPriority = origMemoryPriority,
                        HasMemoryPriorityChanged = memoryPrioritySet
                    };

                    lock (_lock)
                    {
                        AlteredProcesses.Add(state);
                    }
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

    public List<ProcessState> DeprioritizeBackgroundApps(bool optimizeIoPriority = true, bool enableQos = true)
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

                    uint accessRights = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION;
                    var hProcess = NativeMethods.OpenProcess(accessRights, false, proc.Id);
                    if (hProcess == IntPtr.Zero)
                    {
                        accessRights = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                        hProcess = NativeMethods.OpenProcess(accessRights, false, proc.Id);
                    }
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
                            bool ioPrioritySet = false;
                            int origIoPriority = NativeMethods.IoPriorityNormal;

                            if (optimizeIoPriority)
                            {
                                origIoPriority = ProcessIoPriorityService.GetProcessIoPriority(hProcess);
                                ioPrioritySet = ProcessIoPriorityService.SetProcessIoPriority(hProcess, NativeMethods.IoPriorityVeryLow);
                            }

                            bool qosSet = false;
                            uint origQosControl = 0;
                            uint origQosState = 0;
                            if (enableQos)
                            {
                                ProcessQosService.GetQoS(hProcess, out origQosControl, out origQosState);
                                qosSet = ProcessQosService.SetEcoQoS(hProcess);
                            }

                            bool memoryPrioritySet = false;
                            uint origMemoryPriority = NativeMethods.MEMORY_PRIORITY_NORMAL;
                            ProcessMemoryPriorityService.TryGetProcessMemoryPriority(hProcess, out origMemoryPriority);
                            memoryPrioritySet = ProcessMemoryPriorityService.SetProcessMemoryPriority(hProcess, NativeMethods.MEMORY_PRIORITY_VERY_LOW);

                            if (prioritySet || ioPrioritySet || qosSet || memoryPrioritySet)
                            {
                                var state = new ProcessState
                                {
                                    ProcessId = proc.Id,
                                    ProcessName = name,
                                    StartTime = TryGetStartTime(proc),
                                    ExecutablePath = TryGetExecutablePath(proc),
                                    OriginalPriorityClass = prioritySet ? origPriority : 0,
                                    OriginalAffinityMask = 0,
                                    OriginalIoPriority = origIoPriority,
                                    HasIoPriorityChanged = ioPrioritySet,
                                    OriginalQosControlMask = origQosControl,
                                    OriginalQosStateMask = origQosState,
                                    HasQosChanged = qosSet,
                                    OriginalMemoryPriority = origMemoryPriority,
                                    HasMemoryPriorityChanged = memoryPrioritySet
                                };

                                lock (_lock)
                                {
                                    AlteredProcesses.Add(state);
                                }
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
        List<ProcessState> list;
        lock (_lock)
        {
            list = (statesToRestore ?? AlteredProcesses).ToList();
        }

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

                    if (state.HasIoPriorityChanged)
                    {
                        ProcessIoPriorityService.SetProcessIoPriority(hProcess, state.OriginalIoPriority);
                    }

                    if (state.HasQosChanged)
                    {
                        if (state.OriginalQosControlMask != 0 || state.OriginalQosStateMask != 0)
                        {
                            ProcessQosService.RestoreQoS(hProcess, state.OriginalQosControlMask, state.OriginalQosStateMask);
                        }
                        else
                        {
                            ProcessQosService.ResetQoS(hProcess);
                        }
                    }

                    if (state.HasMemoryPriorityChanged)
                    {
                        ProcessMemoryPriorityService.RestoreProcessMemoryPriority(hProcess, state.OriginalMemoryPriority);
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
        }

        lock (_lock)
        {
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

    /// <summary>
    /// R2 Alt+Tab: Temporarily relaxes background app throttling when user switches away from the game.
    /// Elevates priority back to Normal, resets EcoQoS throttling, and restores normal memory priority.
    /// </summary>
    public void RelaxBackgroundThrottling(int? gameProcessId = null)
    {
        List<ProcessState> list;
        lock (_lock)
        {
            list = AlteredProcesses.Where(p => !gameProcessId.HasValue || p.ProcessId != gameProcessId.Value).ToList();
        }

        foreach (var state in list)
        {
            if (ProcessBlacklist.IsProtected(state.ProcessName))
                continue;

            Process proc;
            try
            {
                proc = Process.GetProcessById(state.ProcessId);
            }
            catch
            {
                continue;
            }

            using (proc)
            {
                try
                {
                    if (proc.HasExited) continue;
                    if (!string.Equals(proc.ProcessName, state.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;

                    uint access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                    var hProcess = NativeMethods.OpenProcess(access, false, state.ProcessId);
                    if (hProcess == IntPtr.Zero)
                    {
                        hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_INFORMATION, false, state.ProcessId);
                    }

                    if (hProcess == IntPtr.Zero) continue;

                    try
                    {
                        // Relax CPU priority to Normal for fluid desktop experience
                        NativeMethods.SetPriorityClass(hProcess, NativeMethods.NORMAL_PRIORITY_CLASS);

                        // Reset EcoQoS
                        ProcessQosService.ResetQoS(hProcess);

                        // Restore normal memory priority
                        ProcessMemoryPriorityService.SetProcessMemoryPriority(hProcess, NativeMethods.MEMORY_PRIORITY_NORMAL);
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProcessTuner] RelaxBackgroundThrottling exception on PID {state.ProcessId}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// R2 Alt+Tab: Instantly re-applies full throttling to background apps and verifies max boost for active game
    /// when user returns focus to the game window.
    /// </summary>
    public void RestoreBackgroundThrottling(int? gameProcessId = null)
    {
        List<ProcessState> list;
        lock (_lock)
        {
            list = AlteredProcesses.ToList();
        }

        foreach (var state in list)
        {
            if (ProcessBlacklist.IsProtected(state.ProcessName))
                continue;

            Process proc;
            try
            {
                proc = Process.GetProcessById(state.ProcessId);
            }
            catch
            {
                continue;
            }

            using (proc)
            {
                try
                {
                    if (proc.HasExited) continue;
                    if (!string.Equals(proc.ProcessName, state.ProcessName, StringComparison.OrdinalIgnoreCase)) continue;

                    uint access = NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION;
                    var hProcess = NativeMethods.OpenProcess(access, false, state.ProcessId);
                    if (hProcess == IntPtr.Zero)
                    {
                        hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_INFORMATION, false, state.ProcessId);
                    }

                    if (hProcess == IntPtr.Zero) continue;

                    try
                    {
                        bool isGame = gameProcessId.HasValue && state.ProcessId == gameProcessId.Value;
                        if (isGame)
                        {
                            // Maximum boost for active game
                            NativeMethods.SetPriorityClass(hProcess, NativeMethods.HIGH_PRIORITY_CLASS);
                            ProcessQosService.SetHighQoS(hProcess);
                            ProcessMemoryPriorityService.SetProcessMemoryPriority(hProcess, NativeMethods.MEMORY_PRIORITY_NORMAL);
                        }
                        else
                        {
                            // Full throttling for background apps
                            NativeMethods.SetPriorityClass(hProcess, NativeMethods.BELOW_NORMAL_PRIORITY_CLASS);
                            ProcessQosService.SetEcoQoS(hProcess);
                            ProcessMemoryPriorityService.SetProcessMemoryPriority(hProcess, NativeMethods.MEMORY_PRIORITY_VERY_LOW);
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProcessTuner] RestoreBackgroundThrottling exception on PID {state.ProcessId}: {ex.Message}");
                }
            }
        }
    }
}
