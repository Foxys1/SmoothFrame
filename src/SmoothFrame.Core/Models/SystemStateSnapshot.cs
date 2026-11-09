using System;
using System.Collections.Generic;

namespace SmoothFrame.Core.Models;

public class ProcessState
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public string? ExecutablePath { get; set; }
    public uint OriginalPriorityClass { get; set; }
    public ulong OriginalAffinityMask { get; set; }
    public int OriginalIoPriority { get; set; } = 2;
    public bool HasIoPriorityChanged { get; set; }

    // --- Process QoS State ---
    public uint OriginalQosControlMask { get; set; }
    public uint OriginalQosStateMask { get; set; }
    public bool HasQosChanged { get; set; }
}

public class SystemStateSnapshot
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid OriginalPowerPlanGuid { get; set; }
    public bool HasPowerPlanChanged { get; set; }
    public bool HasTimerResolutionChanged { get; set; }
    public uint OriginalTimerResolution { get; set; }
    public string? OptimizedGameName { get; set; }
    public int? OptimizedGameProcessId { get; set; }
    public List<ProcessState> AlteredProcesses { get; set; } = new();

    // --- Network Optimization State (WAL / Safe Reversible) ---
    public bool HasNetworkOptimized { get; set; }
    public int? OriginalNetworkThrottlingIndex { get; set; }
    public int? OriginalSystemResponsiveness { get; set; }
    public Dictionary<string, Dictionary<string, int?>> OriginalTcpInterfaceSettings { get; set; } = new();

    // --- AudioDG DPC/Latency Optimization State ---
    public bool HasAudioDgOptimized { get; set; }
    public uint OriginalAudioDgPriority { get; set; }
    public ulong OriginalAudioDgAffinity { get; set; }

    // --- Windowed Game Optimization (Auto-Flip Presentation Model) ---
    public bool HasSwapEffectUpgradeChanged { get; set; }
    public int? OriginalSwapEffectUpgradeCache { get; set; }

    // --- 1:1 Linear Hardware Mouse Input State ---
    public bool HasMouseOptimizationChanged { get; set; }
    public int[]? OriginalMouseThresholds { get; set; }
    public int? OriginalMouseSpeed { get; set; }
}
