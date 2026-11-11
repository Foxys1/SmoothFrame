using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Report containing display refresh rate inspection results.
/// </summary>
public class DisplayRefreshRateReport
{
    public uint CurrentHz { get; set; } = 60;
    public uint MaxSupportedHz { get; set; } = 60;
    public bool IsDroppedTo60Warning { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string DetailsText { get; set; } = string.Empty;
}

/// <summary>
/// Report containing DXGI VRAM inspection results.
/// </summary>
public class VramDiagnosticsReport
{
    public ulong BudgetBytes { get; set; }
    public ulong UsageBytes { get; set; }
    public bool IsOverflowDetected { get; set; }
    public bool IsSupported { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string DetailsText { get; set; } = string.Empty;
}

/// <summary>
/// Report containing CPU power and thermal throttling inspection results.
/// </summary>
public class CpuThrottleReport
{
    public uint MaxMhz { get; set; }
    public uint CurrentMhz { get; set; }
    public uint LimitMhz { get; set; }
    public bool IsThrottlingDetected { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string DetailsText { get; set; } = string.Empty;
}

/// <summary>
/// Report containing the read-only gaming readiness inspection results.
/// </summary>
public class SystemReadinessReport
{
    public bool IsVbsActive { get; set; }
    public string VbsStatusText { get; set; } = string.Empty;
    public bool IsHagsActive { get; set; }
    public string HagsStatusText { get; set; } = string.Empty;
    public bool IsGameModeActive { get; set; }
    public string GameModeStatusText { get; set; } = string.Empty;
    public string WindowsVersionText { get; set; } = string.Empty;
    public string CoreIsolationShortcut { get; set; } = SystemDiagnosticsService.CoreIsolationShortcut;
    public string DisplaySettingsShortcut { get; set; } = SystemDiagnosticsService.DisplaySettingsShortcut;

    // Requirement R3 Diagnostics
    public DisplayRefreshRateReport DisplayRefreshRate { get; set; } = new();
    public VramDiagnosticsReport VramDiagnostics { get; set; } = new();
    public CpuThrottleReport CpuThrottle { get; set; } = new();
}

/// <summary>
/// Read-only system diagnostics service inspecting gaming readiness:
/// - VBS (Virtualization-Based Security) / Memory Integrity (HVCI)
/// - HAGS (Hardware-Accelerated GPU Scheduling)
/// - Windows Game Mode
/// - Monitor Refresh Rate (EnumDisplaySettingsEx) with 60Hz drop warning
/// - DXGI Video Memory Budget & Overcommit (QueryVideoMemoryInfo)
/// - CPU Thermal & Power Limit Throttling (CallNtPowerInformation)
/// Strictly read-only: performs 0 modifications to system, registry, or policies.
/// </summary>
public class SystemDiagnosticsService
{
    public const string CoreIsolationShortcut = "ms-settings:coreisolation";
    public const string DisplaySettingsShortcut = "ms-settings:display";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr thisPtr, uint adapterIndex, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr thisPtr, in Guid riid, out IntPtr ppvObject);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryVideoMemoryInfoDelegate(
        IntPtr thisPtr,
        uint nodeIndex,
        uint memorySegmentGroup,
        out NativeMethods.DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);

    /// <summary>
    /// Performs a non-destructive, read-only system readiness assessment.
    /// </summary>
    public static SystemReadinessReport GetReadinessReport()
    {
        var report = new SystemReadinessReport();

        // 1. Inspect VBS / Memory Integrity
        report.IsVbsActive = CheckVbsActive(out string vbsText);
        report.VbsStatusText = vbsText;

        // 2. Inspect HAGS (Hardware Accelerated GPU Scheduling)
        report.IsHagsActive = CheckHagsActive(out string hagsText);
        report.HagsStatusText = hagsText;

        // 3. Inspect Windows Game Mode
        report.IsGameModeActive = CheckGameModeActive(out string gameModeText);
        report.GameModeStatusText = gameModeText;

        // 4. Windows Version & Build
        report.WindowsVersionText = GetWindowsVersionString();

        // 5. Monitor Refresh Rate (Requirement R3)
        report.DisplayRefreshRate = CheckDisplayRefreshRate();

        // 6. DXGI VRAM Inspection (Requirement R3)
        report.VramDiagnostics = CheckVramDiagnostics();

        // 7. CPU Throttling Detection (Requirement R3)
        report.CpuThrottle = CheckCpuThrottling();

        return report;
    }

    /// <summary>
    /// Opens the Windows Core Isolation security settings page via ms-settings protocol.
    /// </summary>
    public static bool OpenCoreIsolationSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = CoreIsolationShortcut,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] Failed to open Core Isolation settings: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens the Windows Display Settings page via ms-settings protocol.
    /// </summary>
    public static bool OpenDisplaySettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DisplaySettingsShortcut,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] Failed to open Display settings: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Inspects the primary monitor refresh rate and checks if Windows has dropped
    /// a high-refresh monitor back to 60 Hz.
    /// </summary>
    public static DisplayRefreshRateReport CheckDisplayRefreshRate()
    {
        var report = new DisplayRefreshRateReport();

        try
        {
            var currentDevMode = new NativeMethods.DEVMODE();
            currentDevMode.dmSize = (ushort)Marshal.SizeOf<NativeMethods.DEVMODE>();

            if (NativeMethods.EnumDisplaySettingsEx(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref currentDevMode, 0))
            {
                uint currentHz = currentDevMode.dmDisplayFrequency;
                uint targetWidth = currentDevMode.dmPelsWidth;
                uint targetHeight = currentDevMode.dmPelsHeight;
                uint maxSupportedHz = currentHz;

                var modeDevMode = new NativeMethods.DEVMODE();
                modeDevMode.dmSize = (ushort)Marshal.SizeOf<NativeMethods.DEVMODE>();
                int modeIndex = 0;

                while (NativeMethods.EnumDisplaySettingsEx(null, modeIndex, ref modeDevMode, 0))
                {
                    if (modeDevMode.dmPelsWidth == targetWidth && modeDevMode.dmPelsHeight == targetHeight)
                    {
                        if (modeDevMode.dmDisplayFrequency > maxSupportedHz)
                        {
                            maxSupportedHz = modeDevMode.dmDisplayFrequency;
                        }
                    }
                    modeIndex++;
                }

                report.CurrentHz = currentHz > 0 ? currentHz : 60;
                report.MaxSupportedHz = maxSupportedHz > 0 ? maxSupportedHz : report.CurrentHz;
                report.IsDroppedTo60Warning = (report.CurrentHz <= 60 && report.MaxSupportedHz > 60);
                report.DetailsText = $"{report.CurrentHz} Hz / Max: {report.MaxSupportedHz} Hz";
                report.StatusText = report.IsDroppedTo60Warning ? "60 Hz Reset Detected" : "Optimal Hz";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] Display refresh rate query failed: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Read-only inspection of GPU dedicated VRAM budget and usage via DXGI (IDXGIAdapter3::QueryVideoMemoryInfo).
    /// Wraps all COM pointers in nested try/finally blocks to guarantee zero COM leaks.
    /// </summary>
    public static VramDiagnosticsReport CheckVramDiagnostics()
    {
        var report = new VramDiagnosticsReport();

        try
        {
            Guid factoryGuid = NativeMethods.IID_IDXGIFactory4;
            int hr = NativeMethods.CreateDXGIFactory1(in factoryGuid, out IntPtr pFactory);
            if (hr == 0 && pFactory != IntPtr.Zero)
            {
                try
                {
                    IntPtr vtableFactoryPtr = Marshal.ReadIntPtr(pFactory);
                    IntPtr enumAdapters1Ptr = Marshal.ReadIntPtr(vtableFactoryPtr, 12 * IntPtr.Size);
                    var enumAdapters1 = (EnumAdapters1Delegate)Marshal.GetDelegateForFunctionPointer(
                        enumAdapters1Ptr,
                        typeof(EnumAdapters1Delegate));

                    uint adapterIndex = 0;
                    ulong bestBudget = 0;
                    ulong matchingUsage = 0;
                    bool anyOverflow = false;
                    bool anySupported = false;

                    while (true)
                    {
                        hr = enumAdapters1(pFactory, adapterIndex, out IntPtr pAdapter);
                        if (hr != 0 || pAdapter == IntPtr.Zero)
                        {
                            break;
                        }

                        try
                        {
                            IntPtr vtableAdapterPtr = Marshal.ReadIntPtr(pAdapter);
                            IntPtr qiPtr = Marshal.ReadIntPtr(vtableAdapterPtr, 0);
                            var qi = (QueryInterfaceDelegate)Marshal.GetDelegateForFunctionPointer(
                                qiPtr,
                                typeof(QueryInterfaceDelegate));

                            Guid adapter3Guid = NativeMethods.IID_IDXGIAdapter3;
                            hr = qi(pAdapter, in adapter3Guid, out IntPtr pAdapter3);
                            if (hr == 0 && pAdapter3 != IntPtr.Zero)
                            {
                                try
                                {
                                    IntPtr vtableAdapter3Ptr = Marshal.ReadIntPtr(pAdapter3);
                                    IntPtr qvmiPtr = Marshal.ReadIntPtr(vtableAdapter3Ptr, 14 * IntPtr.Size);
                                    var qvmi = (QueryVideoMemoryInfoDelegate)Marshal.GetDelegateForFunctionPointer(
                                        qvmiPtr,
                                        typeof(QueryVideoMemoryInfoDelegate));

                                    hr = qvmi(
                                        pAdapter3,
                                        0,
                                        NativeMethods.DXGI_MEMORY_SEGMENT_GROUP_LOCAL,
                                        out NativeMethods.DXGI_QUERY_VIDEO_MEMORY_INFO memInfo);

                                    if (hr == 0)
                                    {
                                        anySupported = true;
                                        if (memInfo.Budget > 0 && memInfo.CurrentUsage > memInfo.Budget)
                                        {
                                            anyOverflow = true;
                                        }

                                        // Track primary adapter with the largest budget
                                        if (memInfo.Budget > bestBudget)
                                        {
                                            bestBudget = memInfo.Budget;
                                            matchingUsage = memInfo.CurrentUsage;
                                        }
                                        else if (bestBudget == 0 && adapterIndex == 0)
                                        {
                                            bestBudget = memInfo.Budget;
                                            matchingUsage = memInfo.CurrentUsage;
                                        }
                                    }
                                }
                                finally
                                {
                                    Marshal.Release(pAdapter3);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.Release(pAdapter);
                        }

                        adapterIndex++;
                    }

                    report.IsSupported = anySupported;
                    report.BudgetBytes = bestBudget;
                    report.UsageBytes = matchingUsage;
                    report.IsOverflowDetected = anyOverflow || (bestBudget > 0 && matchingUsage > bestBudget);
                    report.StatusText = report.IsOverflowDetected ? "Overflow Risk" : "Normal Budget";

                    double usedGb = Math.Round((double)matchingUsage / (1024 * 1024 * 1024), 2);
                    double budgetGb = Math.Round((double)bestBudget / (1024 * 1024 * 1024), 2);
                    report.DetailsText = $"{usedGb:F1} GB / {budgetGb:F1} GB";
                }
                finally
                {
                    Marshal.Release(pFactory);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] DXGI VRAM query failed: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Inspects per-core CPU power frequencies and limits via CallNtPowerInformation
    /// to detect power or thermal throttling across processor cores.
    /// Allocates unmanaged memory safely with guaranteed FreeHGlobal in a finally block.
    /// </summary>
    public static CpuThrottleReport CheckCpuThrottling()
    {
        var report = new CpuThrottleReport();

        int coreCount = Math.Max(1, Environment.ProcessorCount);
        int structSize = Marshal.SizeOf<NativeMethods.PROCESSOR_POWER_INFORMATION>();
        int bufferSize = coreCount * structSize;
        IntPtr pBuffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            uint status = NativeMethods.CallNtPowerInformation(
                NativeMethods.ProcessorInformation,
                IntPtr.Zero,
                0,
                pBuffer,
                (uint)bufferSize);

            if (status == 0) // STATUS_SUCCESS
            {
                uint maxMhz = 0;
                ulong totalCurrentMhz = 0;
                uint minLimitMhz = uint.MaxValue;
                bool isThrottling = false;

                for (int i = 0; i < coreCount; i++)
                {
                    IntPtr corePtr = IntPtr.Add(pBuffer, i * structSize);
                    var info = Marshal.PtrToStructure<NativeMethods.PROCESSOR_POWER_INFORMATION>(corePtr);

                    if (info.MaxMhz > maxMhz)
                    {
                        maxMhz = info.MaxMhz;
                    }

                    totalCurrentMhz += info.CurrentMhz;

                    if (info.MhzLimit < minLimitMhz)
                    {
                        minLimitMhz = info.MhzLimit;
                    }

                    if (info.MhzLimit < info.MaxMhz || (info.CurrentMhz < (info.MaxMhz * 75 / 100) && info.MaxMhz > 0))
                    {
                        isThrottling = true;
                    }
                }

                uint avgCurrentMhz = coreCount > 0 ? (uint)(totalCurrentMhz / (ulong)coreCount) : maxMhz;
                if (minLimitMhz == uint.MaxValue)
                {
                    minLimitMhz = maxMhz;
                }

                report.MaxMhz = maxMhz;
                report.CurrentMhz = avgCurrentMhz;
                report.LimitMhz = minLimitMhz;
                report.IsThrottlingDetected = isThrottling;
                report.StatusText = isThrottling ? "Throttling Detected" : "Full Performance";

                int pct = maxMhz > 0 ? (int)((avgCurrentMhz * 100) / maxMhz) : 100;
                report.DetailsText = $"{avgCurrentMhz} MHz / {maxMhz} MHz ({pct}%)";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] CPU throttling query failed: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(pBuffer);
        }

        return report;
    }

    private static bool CheckVbsActive(out string statusText)
    {
        // Primary query: WMI via COM late binding (SWbemLocator)
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType != null)
            {
                dynamic? locator = Activator.CreateInstance(locatorType);
                if (locator != null)
                {
                    dynamic service = locator.ConnectServer(".", @"root\Microsoft\Windows\DeviceGuard");
                    dynamic objects = service.ExecQuery("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard");
                    foreach (dynamic item in objects)
                    {
                        int vbsStatus = 0;
                        try { vbsStatus = (int)item.VirtualizationBasedSecurityStatus; } catch { }

                        // 2 = VBS Running
                        if (vbsStatus == 2)
                        {
                            statusText = "Включена (VBS активна)";
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] WMI VBS query failed or unsupported: {ex.Message}");
        }

        // Secondary fallback: Registry inspection
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard", writable: false))
            {
                if (key != null)
                {
                    var vbsStatus = key.GetValue("VirtualizationBasedSecurityStatus");
                    if (vbsStatus is int vStatus && vStatus == 2)
                    {
                        statusText = "Включена (VBS активна)";
                        return true;
                    }

                    var vbsEnable = key.GetValue("EnableVirtualizationBasedSecurity");
                    if (vbsEnable is int vEnable && vEnable == 1)
                    {
                        statusText = "Включена";
                        return true;
                    }
                }
            }

            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", writable: false))
            {
                if (key != null)
                {
                    var hvci = key.GetValue("Enabled");
                    if (hvci is int hVal && hVal == 1)
                    {
                        statusText = "Включена (Memory Integrity)";
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] Registry VBS query failed: {ex.Message}");
        }

        statusText = "Отключена";
        return false;
    }

    private static bool CheckHagsActive(out string statusText)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: false);
            if (key != null)
            {
                var val = key.GetValue("HwSchMode");
                if (val is int mode)
                {
                    // 2 = HAGS Enabled
                    if (mode == 2)
                    {
                        statusText = "Включено (HAGS активен)";
                        return true;
                    }
                    if (mode == 1)
                    {
                        statusText = "Отключено";
                        return false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] HAGS query exception: {ex.Message}");
        }

        statusText = "Отключено";
        return false;
    }

    private static bool CheckGameModeActive(out string statusText)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar", writable: false);
            if (key != null)
            {
                var val = key.GetValue("AutoGameModeEnabled");
                if (val is int mode)
                {
                    if (mode == 0)
                    {
                        statusText = "Отключен";
                        return false;
                    }

                    statusText = "Включен";
                    return true;
                }
            }

            // On Windows 10/11, Game Mode is enabled by default if not explicitly disabled
            statusText = "Включен (по умолчанию)";
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SystemDiagnostics] Game Mode query exception: {ex.Message}");
            // Return safe default without throwing
            statusText = "Включен (по умолчанию)";
            return true;
        }
    }

    private static string GetWindowsVersionString()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", writable: false);
            if (key != null)
            {
                var productName = key.GetValue("ProductName")?.ToString() ?? "Windows";
                var displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? string.Empty;
                var currentBuild = key.GetValue("CurrentBuild")?.ToString() ?? string.Empty;

                // Adjust for Windows 11 marketing name if build >= 22000
                if (int.TryParse(currentBuild, out int buildNum) && buildNum >= 22000)
                {
                    if (productName.Contains("Windows 10"))
                    {
                        productName = productName.Replace("Windows 10", "Windows 11");
                    }
                }

                if (!string.IsNullOrEmpty(displayVersion) && !string.IsNullOrEmpty(currentBuild))
                {
                    return $"{productName} {displayVersion} (Build {currentBuild})";
                }
                if (!string.IsNullOrEmpty(currentBuild))
                {
                    return $"{productName} (Build {currentBuild})";
                }
            }
        }
        catch { }

        return Environment.OSVersion.VersionString;
    }
}
