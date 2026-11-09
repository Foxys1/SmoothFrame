using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Service for hardware mouse acceleration tuning.
/// Disables Windows pointer acceleration (Enhance Pointer Precision) and enforces a 1:1 linear hardware curve {0, 0, 0}
/// for raw, consistent aim in competitive gaming.
/// Captures original user thresholds and speed into SystemStateSnapshot and restores them upon deactivation or crash recovery.
/// </summary>
public static class MouseOptimizerService
{
    /// <summary>
    /// Reads the current mouse acceleration parameters and pointer speed from the OS.
    /// </summary>
    public static bool GetMouseParameters(out int[] thresholds, out int speed)
    {
        thresholds = new int[3];
        speed = 10;

        try
        {
            bool okParams = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMOUSE, 0, thresholds, 0);
            bool okSpeed = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMOUSESPEED, 0, ref speed, 0);

            if (!okParams)
            {
                Debug.WriteLine($"[MouseOptimizer] SPI_GETMOUSE failed, Win32 error: {Marshal.GetLastWin32Error()}");
            }

            return okParams;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MouseOptimizer] GetMouseParameters exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks whether mouse acceleration is currently disabled ({0, 0, 0} curve).
    /// </summary>
    public static bool IsLinearAcceleration()
    {
        if (GetMouseParameters(out var thresholds, out _))
        {
            return thresholds[0] == 0 && thresholds[1] == 0 && thresholds[2] == 0;
        }
        return false;
    }

    /// <summary>
    /// Captures the user's current mouse configuration into the snapshot,
    /// then applies a 1:1 linear acceleration curve {0, 0, 0} for gaming.
    /// </summary>
    public static bool Apply(SystemStateSnapshot snapshot)
    {
        if (snapshot == null) return false;

        try
        {
            var originalThresholds = new int[3];
            int originalSpeed = 10;

            bool okGetParams = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMOUSE, 0, originalThresholds, 0);
            NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETMOUSESPEED, 0, ref originalSpeed, 0);

            if (okGetParams)
            {
                snapshot.OriginalMouseThresholds = (int[])originalThresholds.Clone();
                snapshot.OriginalMouseSpeed = originalSpeed;
            }

            // Apply 1:1 linear curve: Threshold1 = 0, Threshold2 = 0, Acceleration = 0
            int[] linearCurve = new int[3] { 0, 0, 0 };
            bool setOk = NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_SETMOUSE,
                0,
                linearCurve,
                NativeMethods.SPIF_SENDCHANGE);

            if (setOk)
            {
                snapshot.HasMouseOptimizationChanged = true;
                return true;
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[MouseOptimizer] SPI_SETMOUSE linear curve failed, Win32 error: {err}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MouseOptimizer] Apply exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores the user's original mouse acceleration curve and pointer speed.
    /// </summary>
    public static bool Revert(SystemStateSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.HasMouseOptimizationChanged)
            return true;

        bool allRestored = true;

        try
        {
            if (snapshot.OriginalMouseThresholds != null && snapshot.OriginalMouseThresholds.Length == 3)
            {
                bool setOk = NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_SETMOUSE,
                    0,
                    snapshot.OriginalMouseThresholds,
                    NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);

                if (!setOk)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[MouseOptimizer] SPI_SETMOUSE revert failed, Win32 error: {err}");
                    allRestored = false;
                }
            }

            if (snapshot.OriginalMouseSpeed.HasValue)
            {
                bool speedOk = NativeMethods.SystemParametersInfo(
                    NativeMethods.SPI_SETMOUSESPEED,
                    0,
                    (IntPtr)snapshot.OriginalMouseSpeed.Value,
                    NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);

                if (!speedOk)
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[MouseOptimizer] SPI_SETMOUSESPEED revert failed, Win32 error: {err}");
                    allRestored = false;
                }
            }

            if (allRestored)
            {
                snapshot.HasMouseOptimizationChanged = false;
            }

            return allRestored;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MouseOptimizer] Revert exception: {ex.Message}");
            return false;
        }
    }
}
