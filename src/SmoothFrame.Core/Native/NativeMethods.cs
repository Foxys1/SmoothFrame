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

    // --- Process Power Throttling & QoS (kernel32.dll) ---
    public const int ProcessPowerThrottling = 4;
    public const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    public const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
        uint ProcessInformationSize);

    // --- Process Memory Priority (kernel32.dll) ---
    public const int ProcessMemoryPriority = 0;
    public const uint MEMORY_PRIORITY_VERY_LOW = 1;
    public const uint MEMORY_PRIORITY_LOW = 2;
    public const uint MEMORY_PRIORITY_MEDIUM = 3;
    public const uint MEMORY_PRIORITY_BELOW_NORMAL = 4;
    public const uint MEMORY_PRIORITY_NORMAL = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_PRIORITY_INFORMATION
    {
        public uint MemoryPriority;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref MEMORY_PRIORITY_INFORMATION ProcessInformation,
        uint ProcessInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetProcessInformation(
        IntPtr hProcess,
        int ProcessInformationClass,
        ref MEMORY_PRIORITY_INFORMATION ProcessInformation,
        uint ProcessInformationSize);

    // --- WinEventHook & Window Focus (user32.dll) ---
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint WM_QUIT = 0x0012;

    public delegate void WinEventProc(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    // --- Monitor Refresh Rate Inspection (user32.dll) ---
    public const int ENUM_CURRENT_SETTINGS = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplaySettingsEx(
        string? lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode,
        uint dwFlags);

    // --- DXGI VRAM Inspection (dxgi.dll) ---
    public const uint DXGI_MEMORY_SEGMENT_GROUP_LOCAL = 0;
    public const uint DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    public static readonly Guid IID_IDXGIFactory4 = new("1bc6ea02-ef36-464f-bf0c-21ca39e5168a");
    public static readonly Guid IID_IDXGIAdapter3 = new("645967a4-1392-4310-a798-8053ce3e93fd");

    [DllImport("dxgi.dll", SetLastError = true)]
    public static extern int CreateDXGIFactory1(in Guid riid, out IntPtr ppFactory);

    // --- Processor Power Information (powrprof.dll) ---
    public const int ProcessorInformation = 11;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern uint CallNtPowerInformation(
        int InformationLevel,
        IntPtr lpInputBuffer,
        uint nInputBufferSize,
        IntPtr lpOutputBuffer,
        uint nOutputBufferSize);

    // --- SystemParametersInfo Mouse APIs (user32.dll) ---
    public const uint SPI_GETMOUSE = 0x0003;
    public const uint SPI_SETMOUSE = 0x0004;
    public const uint SPI_GETMOUSESPEED = 0x0070;
    public const uint SPI_SETMOUSESPEED = 0x0071;
    public const uint SPIF_UPDATEINIFILE = 0x01;
    public const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        [In, Out] int[] pvParam,
        uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref int pvParam,
        uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        IntPtr pvParam,
        uint fWinIni);
}
