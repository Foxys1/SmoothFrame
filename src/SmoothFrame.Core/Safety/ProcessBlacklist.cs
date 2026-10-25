using System;
using System.Collections.Generic;

namespace SmoothFrame.Core.Safety;

public static class ProcessBlacklist
{
    // Processes that should NEVER have their priority or affinity modified
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core Windows system processes
        "system",
        "idle",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "svchost",
        "fontdrvhost",
        "winlogon",
        "dwm",
        "explorer",
        "sihost",
        "taskhostw",
        "ctfmon",
        "spoolsv",
        "audiodg",               // Windows Audio Device Graph - must never be deprioritized or sound breaks!
        "shellexperiencehost",
        "startmenuexperiencehost",
        "searchhost",
        "securityhealthservice", // Windows Defender
        "msmpeng",               // Windows Defender engine
        "registry",
        
        // Anti-cheat engines (must not be altered to avoid false flags or kicks)
        "vgc",
        "vgtray",
        "easyanticheat",
        "easyanticheat_eos",
        "beservice",
        "faceitservice",
        
        // Critical system utilities
        "taskmgr",

        // Self-protection
        "smoothframe",
        "smoothframe.ui",
        "smoothframe.core",
        "smoothframe.runner",
        "smoothframe.installer",
        "smoothframe_uninstall_runner"
    };

    public static bool IsProtected(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return true;

        var cleanName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        return ProtectedProcesses.Contains(cleanName);
    }
}
