using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

public class PowerPlanService : IDisposable
{
    private Guid _originalPlan;
    private bool _isAltered;

    public Guid CurrentPlan { get; private set; }
    public bool LastOperationRequiresAdmin { get; private set; }

    public PowerPlanService()
    {
        CurrentPlan = GetActiveSchemeGuid() ?? Guid.Empty;
    }

    public static bool TryGetActiveSchemeGuid(out Guid activeSchemeGuid)
    {
        activeSchemeGuid = Guid.Empty;
        IntPtr pGuid = IntPtr.Zero;
        try
        {
            uint result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out pGuid);
            if (result == 0 && pGuid != IntPtr.Zero)
            {
                activeSchemeGuid = Marshal.PtrToStructure<Guid>(pGuid);
                return true;
            }
            Debug.WriteLine($"PowerGetActiveScheme failed with Win32 code {result}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get active power scheme: {ex.Message}");
            return false;
        }
        finally
        {
            if (pGuid != IntPtr.Zero)
            {
                NativeMethods.LocalFree(pGuid);
            }
        }
    }

    public static Guid? GetActiveSchemeGuid()
    {
        if (TryGetActiveSchemeGuid(out var guid))
        {
            return guid;
        }
        return null;
    }

    public bool SwitchToHighPerformance()
    {
        if (_isAltered) return true;

        try
        {
            if (!TryGetActiveSchemeGuid(out var activePlan))
            {
                Debug.WriteLine("Cannot switch power plan: unable to query active scheme.");
                return false;
            }

            _originalPlan = activePlan;

            // Try Ultimate Performance first, then High Performance
            uint status = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, in NativeMethods.GUID_ULTIMATE_PERFORMANCE);
            if (status != 0 && status != 5)
            {
                var ultimateGuid = GetOrDuplicateUltimateSchemeGuid();
                if (ultimateGuid.HasValue)
                {
                    var targetGuid = ultimateGuid.Value;
                    status = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, in targetGuid);
                }
            }

            if (status != 0)
            {
                status = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, in NativeMethods.GUID_HIGH_PERFORMANCE);
            }

            if (status == 0) // SUCCESS
            {
                CurrentPlan = GetActiveSchemeGuid() ?? activePlan;
                _isAltered = true;
                LastOperationRequiresAdmin = false;
                return true;
            }
            else if (status == 5) // ERROR_ACCESS_DENIED
            {
                LastOperationRequiresAdmin = true;
                Debug.WriteLine("Switching power plan requires Administrator privileges.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to switch power scheme: {ex.Message}");
        }

        return false;
    }

    public bool RestoreOriginalPlan()
    {
        if (!_isAltered || _originalPlan == Guid.Empty) return true;

        try
        {
            uint status = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, in _originalPlan);
            CurrentPlan = GetActiveSchemeGuid() ?? _originalPlan;
            if (status == 0)
            {
                _isAltered = false;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to restore power scheme: {ex.Message}");
            return false;
        }
    }

    public bool RestoreSpecificPlan(Guid planGuid)
    {
        if (planGuid == Guid.Empty) return false;

        try
        {
            uint status = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, in planGuid);
            CurrentPlan = GetActiveSchemeGuid() ?? planGuid;
            if (status == 0)
            {
                _isAltered = false;
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to restore specific power scheme: {ex.Message}");
            return false;
        }
    }

    private static Guid? GetOrDuplicateUltimateSchemeGuid()
    {
        try
        {
            // 1. Check if Ultimate Performance scheme is already registered
            var listPsi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/list",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var listProc = Process.Start(listPsi))
            {
                if (listProc != null)
                {
                    string output = listProc.StandardOutput.ReadToEnd();
                    listProc.WaitForExit(1500);

                    foreach (var line in output.Split('\n'))
                    {
                        if (line.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(line, @"([a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})");
                            if (match.Success && Guid.TryParse(match.Groups[1].Value, out var existingGuid))
                            {
                                return existingGuid;
                            }
                        }
                    }
                }
            }

            // 2. Not found, duplicate from template once
            var dupPsi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var dupProc = Process.Start(dupPsi))
            {
                if (dupProc != null)
                {
                    string dupOutput = dupProc.StandardOutput.ReadToEnd();
                    dupProc.WaitForExit(2000);

                    var match = System.Text.RegularExpressions.Regex.Match(dupOutput, @"([a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})");
                    if (match.Success && Guid.TryParse(match.Groups[1].Value, out var newGuid))
                    {
                        return newGuid;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        RestoreOriginalPlan();
        GC.SuppressFinalize(this);
    }
}
