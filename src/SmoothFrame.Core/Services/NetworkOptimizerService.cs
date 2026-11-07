using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Safe, 100% reversible low-latency network optimization service.
/// Tunes Windows Multimedia Network Throttling and per-adapter TCP ACK frequency / Nagle's algorithm (TCPNoDelay).
/// Eliminates packet buffering delay and network jitter during competitive gaming.
/// All original registry states are captured in the WAL snapshot and restored upon deactivation or crash recovery.
/// </summary>
public static class NetworkOptimizerService
{
    private const string MultimediaKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string TcpInterfacesKeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    private const string ValueNetworkThrottlingIndex = "NetworkThrottlingIndex";
    private const string ValueSystemResponsiveness = "SystemResponsiveness";
    private const string ValueTcpAckFrequency = "TcpAckFrequency";
    private const string ValueTcpNoDelay = "TCPNoDelay";

    public static bool Apply(SystemStateSnapshot snapshot)
    {
        if (snapshot == null) return false;

        bool appliedAny = false;

        try
        {
            // 1. Multimedia Profile Optimization
            using (var key = Registry.LocalMachine.OpenSubKey(MultimediaKeyPath, writable: true))
            {
                if (key != null)
                {
                    // Capture original NetworkThrottlingIndex
                    var netVal = key.GetValue(ValueNetworkThrottlingIndex);
                    snapshot.OriginalNetworkThrottlingIndex = netVal is int nInt ? nInt : (netVal != null ? Convert.ToInt32(netVal) : null);

                    // Capture original SystemResponsiveness
                    var sysVal = key.GetValue(ValueSystemResponsiveness);
                    snapshot.OriginalSystemResponsiveness = sysVal is int sInt ? sInt : (sysVal != null ? Convert.ToInt32(sysVal) : null);

                    // Apply gaming optimizations:
                    // 0xFFFFFFFF (-1) disables multimedia network packet throttling
                    key.SetValue(ValueNetworkThrottlingIndex, unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    // 0 prioritizes game responsiveness over background tasks
                    key.SetValue(ValueSystemResponsiveness, 0, RegistryValueKind.DWord);

                    appliedAny = true;
                }
            }

            // 2. Active TCP Interface Tuning (TcpAckFrequency = 1, TCPNoDelay = 1)
            using (var interfacesKey = Registry.LocalMachine.OpenSubKey(TcpInterfacesKeyPath, writable: false))
            {
                if (interfacesKey != null)
                {
                    var subKeyNames = interfacesKey.GetSubKeyNames();
                    foreach (var subName in subKeyNames)
                    {
                        try
                        {
                            using var ifaceRead = interfacesKey.OpenSubKey(subName, writable: false);
                            if (ifaceRead == null) continue;

                            // Only tune active interfaces with configured IP
                            if (!IsActiveInterface(ifaceRead)) continue;

                            using var ifaceWrite = Registry.LocalMachine.OpenSubKey($@"{TcpInterfacesKeyPath}\{subName}", writable: true);
                            if (ifaceWrite == null) continue;

                            var ifaceSnapshot = new Dictionary<string, int?>();

                            // Capture original TcpAckFrequency
                            var ackVal = ifaceWrite.GetValue(ValueTcpAckFrequency);
                            ifaceSnapshot[ValueTcpAckFrequency] = ackVal is int aInt ? aInt : (ackVal != null ? Convert.ToInt32(ackVal) : null);

                            // Capture original TCPNoDelay
                            var ndVal = ifaceWrite.GetValue(ValueTcpNoDelay);
                            ifaceSnapshot[ValueTcpNoDelay] = ndVal is int ndInt ? ndInt : (ndVal != null ? Convert.ToInt32(ndVal) : null);

                            // Set low latency parameters:
                            ifaceWrite.SetValue(ValueTcpAckFrequency, 1, RegistryValueKind.DWord);
                            ifaceWrite.SetValue(ValueTcpNoDelay, 1, RegistryValueKind.DWord);

                            snapshot.OriginalTcpInterfaceSettings[subName] = ifaceSnapshot;
                            appliedAny = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NetworkOptimizer] Failed to configure interface {subName}: {ex.Message}");
                        }
                    }
                }
            }

            snapshot.HasNetworkOptimized = appliedAny;
            return appliedAny;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetworkOptimizer] Failed to apply network optimizations: {ex.Message}");
            return false;
        }
    }

    public static bool Revert(SystemStateSnapshot snapshot)
    {
        if (snapshot == null || !snapshot.HasNetworkOptimized) return true;

        bool allRestored = true;

        try
        {
            // 1. Revert Multimedia Profile
            using (var key = Registry.LocalMachine.OpenSubKey(MultimediaKeyPath, writable: true))
            {
                if (key != null)
                {
                    if (snapshot.OriginalNetworkThrottlingIndex.HasValue)
                    {
                        key.SetValue(ValueNetworkThrottlingIndex, snapshot.OriginalNetworkThrottlingIndex.Value, RegistryValueKind.DWord);
                    }
                    else
                    {
                        try { key.DeleteValue(ValueNetworkThrottlingIndex, throwOnMissingValue: false); } catch { }
                    }

                    if (snapshot.OriginalSystemResponsiveness.HasValue)
                    {
                        key.SetValue(ValueSystemResponsiveness, snapshot.OriginalSystemResponsiveness.Value, RegistryValueKind.DWord);
                    }
                    else
                    {
                        try { key.DeleteValue(ValueSystemResponsiveness, throwOnMissingValue: false); } catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetworkOptimizer] Failed to revert multimedia profile: {ex.Message}");
            allRestored = false;
        }

        // 2. Revert TCP Interfaces
        if (snapshot.OriginalTcpInterfaceSettings != null)
        {
            foreach (var kvp in snapshot.OriginalTcpInterfaceSettings)
            {
                var subName = kvp.Key;
                var originalVals = kvp.Value;

                try
                {
                    using var ifaceWrite = Registry.LocalMachine.OpenSubKey($@"{TcpInterfacesKeyPath}\{subName}", writable: true);
                    if (ifaceWrite == null) continue;

                    if (originalVals.TryGetValue(ValueTcpAckFrequency, out var origAck))
                    {
                        if (origAck.HasValue)
                        {
                            ifaceWrite.SetValue(ValueTcpAckFrequency, origAck.Value, RegistryValueKind.DWord);
                        }
                        else
                        {
                            try { ifaceWrite.DeleteValue(ValueTcpAckFrequency, throwOnMissingValue: false); } catch { }
                        }
                    }

                    if (originalVals.TryGetValue(ValueTcpNoDelay, out var origNoDelay))
                    {
                        if (origNoDelay.HasValue)
                        {
                            ifaceWrite.SetValue(ValueTcpNoDelay, origNoDelay.Value, RegistryValueKind.DWord);
                        }
                        else
                        {
                            try { ifaceWrite.DeleteValue(ValueTcpNoDelay, throwOnMissingValue: false); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NetworkOptimizer] Failed to revert interface {subName}: {ex.Message}");
                    allRestored = false;
                }
            }
        }

        if (allRestored)
        {
            snapshot.HasNetworkOptimized = false;
        }
        return allRestored;
    }

    private static bool IsActiveInterface(RegistryKey key)
    {
        try
        {
            // An active TCP adapter typically has IPAddress or DhcpIPAddress assigned
            var dhcpIp = key.GetValue("DhcpIPAddress");
            if (dhcpIp is string dStr && !string.IsNullOrWhiteSpace(dStr) && dStr != "0.0.0.0")
                return true;

            var ip = key.GetValue("IPAddress");
            if (ip is string[] ipArr && ipArr.Length > 0 && !string.IsNullOrWhiteSpace(ipArr[0]) && ipArr[0] != "0.0.0.0")
                return true;
            if (ip is string ipStr && !string.IsNullOrWhiteSpace(ipStr) && ipStr != "0.0.0.0")
                return true;
        }
        catch { }

        return false;
    }
}
