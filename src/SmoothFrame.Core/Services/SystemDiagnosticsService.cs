using System;
using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace SmoothFrame.Core.Services;

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
}

/// <summary>
/// Read-only system diagnostics service inspecting gaming readiness:
/// - VBS (Virtualization-Based Security) / Memory Integrity (HVCI)
/// - HAGS (Hardware-Accelerated GPU Scheduling)
/// - Windows Game Mode
/// Strictly read-only: performs 0 modifications to system, registry, or policies.
/// </summary>
public class SystemDiagnosticsService
{
    public const string CoreIsolationShortcut = "ms-settings:coreisolation";

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
