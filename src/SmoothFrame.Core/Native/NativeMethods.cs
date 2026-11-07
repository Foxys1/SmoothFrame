using System;
using System.Runtime.InteropServices;

namespace SmoothFrame.Core.Native;

public static class NativeMethods
{
    // --- Priority Constants ---
    public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    public const uint IDLE_PRIORITY_CLASS = 0x00000040;
    public const uint HIGH_PRIORITY_CLASS = 0x00000080;
    public const uint REALTIME_PRIORITY_CLASS = 0x00000100;
    public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
    public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;

    // --- Process Access Rights ---
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_SET_INFORMATION = 0x0200;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    // --- Power Schemes GUIDs (Standard Windows) ---
    public static readonly Guid GUID_BALANCED = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid GUID_HIGH_PERFORMANCE = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid GUID_ULTIMATE_PERFORMANCE = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    // --- NT Timer Resolution (ntdll.dll) ---
    [DllImport("ntdll.dll")]
    public static extern int NtSetTimerResolution(
        uint desiredResolution,
        [MarshalAs(UnmanagedType.U1)] bool setResolution,
        out uint currentResolution);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    // --- Multimedia Timer (winmm.dll fallback) ---
    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
    public static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
    public static extern uint TimeEndPeriod(uint uMilliseconds);

    // --- Power Policy APIs (powrprof.dll) ---
    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, in Guid schemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);

    // --- Process APIs (kernel32.dll) ---
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessAffinityMask(IntPtr hProcess, out UIntPtr lpProcessAffinityMask, out UIntPtr lpSystemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessAffinityMask(IntPtr hProcess, UIntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        int dwFlags,
        [Out] System.Text.StringBuilder lpExeName,
        ref int lpdwSize);

    // --- Physical Memory Query (kernel32.dll) ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    // --- Working Set Memory Management (psapi.dll) ---
    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);

    // --- NT System Information (ntdll.dll) ---
    public const int SystemMemoryListInformation = 80;

    [DllImport("ntdll.dll")]
    public static extern int NtSetSystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength);

    // --- NT Process I/O Priority (ntdll.dll) ---
    public const int ProcessIoPriority = 21;
    public const int IoPriorityVeryLow = 0;
    public const int IoPriorityLow = 1;
    public const int IoPriorityNormal = 2;
    public const int IoPriorityHigh = 3;

    [DllImport("ntdll.dll")]
    public static extern int NtSetInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref int processInformation,
        int processInformationLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        out int processInformation,
        int processInformationLength,
        out int returnLength);

    // --- Process Token Privileges (advapi32.dll) ---
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;
    public const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
    public const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";
    public const string SE_DEBUG_NAME = "SeDebugPrivilege";
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);
}
