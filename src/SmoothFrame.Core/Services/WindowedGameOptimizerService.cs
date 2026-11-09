using System;
using System.Diagnostics;
using Microsoft.Win32;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Optimizes borderless and windowed games by enabling the Windows 11 Auto-Flip presentation model upgrade
/// (DXGI_SWAP_EFFECT_FLIP_DISCARD).
/// Target key: HKCU\Software\Microsoft\DirectX\GraphicsSettings -> SwapEffectUpgradeCache = 1.
/// 100% reversible via Write-Ahead Logging (WAL) state snapshot.
/// </summary>
public static class WindowedGameOptimizerService
{
    private const string KeyPath = @"Software\Microsoft\DirectX\GraphicsSettings";
    private const string ValueName = "SwapEffectUpgradeCache";

    /// <summary>
    /// Checks whether the Auto-Flip windowed presentation upgrade is currently enabled.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            var val = key?.GetValue(ValueName);
            return val is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Captures the original SwapEffectUpgradeCache setting into the snapshot
    /// and activates the Auto-Flip presentation model (SwapEffectUpgradeCache = 1).
    /// </summary>
    public static bool Apply(SystemStateSnapshot snapshot)
    {
        if (snapshot == null) return false;

        try
        {
            // Read existing value if present
            int? currentVal = null;
            using (var readKey = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false))
            {
                if (readKey != null)
                {
                    var raw = readKey.GetValue(ValueName);
                    if (raw is int iVal)
                    {
                        currentVal = iVal;
                    }
                    else if (raw != null)
                    {
                        try { currentVal = Convert.ToInt32(raw); } catch { }
                    }
                }
            }

            // If already enabled (1), record in snapshot and do not mark as altered
            if (currentVal == 1)
            {
                snapshot.OriginalSwapEffectUpgradeCache = 1;
                snapshot.HasSwapEffectUpgradeChanged = false;
                return true;
            }

            snapshot.OriginalSwapEffectUpgradeCache = currentVal;

            using (var writeKey = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true))
            {
                if (writeKey != null)
                {
                    writeKey.SetValue(ValueName, 1, RegistryValueKind.DWord);
                    snapshot.HasSwapEffectUpgradeChanged = true;
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowedGameOptimizer] Apply failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores the original SwapEffectUpgradeCache value from the snapshot.
    /// If the setting did not exist originally, deletes the value.
    /// </summary>
    public static bool Revert(SystemStateSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.HasSwapEffectUpgradeChanged)
            return true;

        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true))
            {
                if (key != null)
                {
                    if (snapshot.OriginalSwapEffectUpgradeCache.HasValue)
                    {
                        key.SetValue(ValueName, snapshot.OriginalSwapEffectUpgradeCache.Value, RegistryValueKind.DWord);
                    }
                    else
                    {
                        try
                        {
                            key.DeleteValue(ValueName, throwOnMissingValue: false);
                        }
                        catch { }
                    }
                }
            }

            snapshot.HasSwapEffectUpgradeChanged = false;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowedGameOptimizer] Revert failed: {ex.Message}");
            return false;
        }
    }
}
