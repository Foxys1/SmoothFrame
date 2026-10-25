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
}
