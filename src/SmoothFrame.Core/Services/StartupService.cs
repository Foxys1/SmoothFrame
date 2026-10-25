using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace SmoothFrame.Core.Services;

public class StartupService
{
    private const string TaskName = "SmoothFrame_Autostart";
    private const string RegistryRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppTitle = "SmoothFrame";

    public static string GetCurrentExecutablePath()
    {
        return Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SmoothFrame.UI.exe");
    }

    public bool IsAutostartEnabled()
    {
        try
        {
            // Check Task Scheduler
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{TaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                if (!proc.WaitForExit(2000))
                {
                    try { proc.Kill(true); } catch { }
                }
                else if (proc.ExitCode == 0)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fallback to checking registry
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, false);
            return key?.GetValue(AppTitle) != null;
        }
        catch
        {
            return false;
        }
    }

    public bool SetAutostart(bool enable)
    {
        try
        {
            var exePath = GetCurrentExecutablePath();

            if (enable)
            {
                // Ensure registry Run entry is cleaned up so no duplicate instance is launched (SF-08)
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true);
                    key?.DeleteValue(AppTitle, false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to clean up registry autostart entry: {ex.Message}");
                }

                // 1. Create Task in Task Scheduler with HIGHEST privileges
                // This eliminates the UAC prompt on boot!
                var schtasksArgs = $"/create /tn \"{TaskName}\" /tr \"\\\"{exePath}\\\" --minimized\" /sc onlogon /rl highest /f";
                bool taskCreated = RunProcess("schtasks.exe", schtasksArgs);

                if (!taskCreated)
                {
                    // Fallback to registry if schtasks failed (e.g. non-elevated user context)
                    try
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true);
                        key?.SetValue(AppTitle, $"\"{exePath}\" --minimized");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to register fallback registry autostart: {ex.Message}");
                        return false;
                    }
                }

                return true;
            }
            else
            {
                // Disable autostart: clean up BOTH scheduled task and registry entry (SF-08)
                // 1. Remove scheduled task
                RunProcess("schtasks.exe", $"/delete /tn \"{TaskName}\" /f");

                // 2. Remove registry key
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryRunPath, true);
                    key?.DeleteValue(AppTitle, false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to clean up registry autostart entry: {ex.Message}");
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to toggle autostart: {ex.Message}");
            return false;
        }
    }

    private static bool RunProcess(string filename, string arguments, int timeoutMs = 3000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            using var p = Process.Start(psi);
            if (p == null) return false;

            if (!p.WaitForExit(timeoutMs))
            {
                try
                {
                    // Terminate child processes to prevent orphaned processes on timeout (SF-18)
                    p.Kill(true);
                }
                catch
                {
                    // Ignore if process has already exited
                }
                return false;
            }

            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed running process {filename}: {ex.Message}");
            return false;
        }
    }
}
