using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

public class MemoryPurgeResult
{
    public bool Success { get; set; }
    public int TrimmedProcessCount { get; set; }
    public ulong InitialFreeBytes { get; set; }
    public ulong FinalFreeBytes { get; set; }
    public long BytesFreed => (long)FinalFreeBytes - (long)InitialFreeBytes;
    public string Message { get; set; } = string.Empty;
}

public static class MemoryCleanerService
{
    // SYSTEM_MEMORY_LIST_COMMAND values for NtSetSystemInformation (Class 80)
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryEmptyWorkingSetsCommand = 2;

    public static ulong GetFreePhysicalMemoryBytes()
    {
        try
        {
            var mem = new NativeMethods.MEMORYSTATUSEX();
            if (NativeMethods.GlobalMemoryStatusEx(mem))
            {
                return mem.ullAvailPhys;
            }
        }
        catch { }

        return 0;
    }

    public static MemoryPurgeResult PurgeMemory(bool trimWorkingSets = true, bool purgeStandby = true, bool includeJvm = false, int? excludeProcessId = null)
    {
        var result = new MemoryPurgeResult
        {
            InitialFreeBytes = GetFreePhysicalMemoryBytes()
        };

        int trimmed = 0;

        if (trimWorkingSets)
        {
            trimmed = TrimAllWorkingSets(includeJvm, excludeProcessId);
            result.TrimmedProcessCount = trimmed;
        }

        if (purgeStandby)
        {
            TryPurgeStandbyListInternal();
        }

        result.FinalFreeBytes = GetFreePhysicalMemoryBytes();
        result.Success = true;

        long freedMb = result.BytesFreed / (1024 * 1024);
        if (freedMb > 0)
        {
            result.Message = $"Освобождено ~{freedMb} МБ ОЗУ (оптимизировано {trimmed} процессов).";
        }
        else
        {
            result.Message = $"Память оптимизирована (обработано {trimmed} процессов).";
        }

        return result;
    }

    public static int TrimAllWorkingSets(bool includeJvm = false, int? excludeProcessId = null)
    {
        int count = 0;
        var processes = Process.GetProcesses();

        foreach (var p in processes)
        {
            try
            {
                // Never trim active game process to avoid paging thrashing during gameplay
                if (excludeProcessId.HasValue && p.Id == excludeProcessId.Value)
                {
                    continue;
                }

                // Never trim critical system processes from blacklist
                if (Safety.ProcessBlacklist.IsProtected(p.ProcessName))
                {
                    continue;
                }

                // Protect JVM processes from unconditional trimming to preserve GC heap locality
                if (!includeJvm &&
                    (p.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (NativeMethods.EmptyWorkingSet(p.Handle))
                {
                    count++;
                }
            }
            catch
            {
                // Access denied or process exited - normal for system processes
            }
            finally
            {
                p.Dispose();
            }
        }

        return count;
    }

    private static bool TryPurgeStandbyListInternal()
    {
        try
        {
            EnablePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);
            EnablePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);

            int command = MemoryPurgeStandbyList;
            unsafe
            {
                int status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    (IntPtr)(&command),
                    sizeof(int));

                return status == 0;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool EnablePrivilege(string privilegeName)
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            if (!NativeMethods.OpenProcessToken(
                currentProcess.Handle,
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out IntPtr tokenHandle))
            {
                return false;
            }

            try
            {
                if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out NativeMethods.LUID luid))
                {
                    return false;
                }

                var tp = new NativeMethods.TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new NativeMethods.LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
                    }
                };

                bool success = NativeMethods.AdjustTokenPrivileges(
                    tokenHandle,
                    false,
                    ref tp,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero);

                return success && Marshal.GetLastWin32Error() == 0;
            }
            finally
            {
                // Close token handle using CloseHandle from NativeMethods
                NativeMethods.CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return false;
        }
    }
}
