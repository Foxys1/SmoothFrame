using System;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

public static class MinecraftJvmOptimizer
{
    public const string GenerationalZgcFlags =
        "-XX:+UseZGC -XX:+ZGenerational -XX:+AlwaysPreTouch -XX:+UseStringDeduplication -XX:+UnlockExperimentalVMOptions -XX:+UseNUMA -XX:-OmitStackTraceInFastThrow";

    public const string AikarG1gcFlags =
        "-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1ReservePercent=20 -XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=15 -XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 -XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -XX:+UseNUMA -XX:-OmitStackTraceInFastThrow";

    public static ulong GetTotalPhysicalMemoryBytes()
    {
        try
        {
            var mem = new NativeMethods.MEMORYSTATUSEX();
            if (NativeMethods.GlobalMemoryStatusEx(mem) && mem.ullTotalPhys > 0)
            {
                return mem.ullTotalPhys;
            }
        }
        catch { }

        // Fallback using GC memory info
        try
        {
            long avail = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (avail > 0) return (ulong)avail;
        }
        catch { }

        // Default safe estimate 16 GB
        return 16UL * 1024 * 1024 * 1024;
    }

    public static int CalculateRecommendedRamMb(ulong totalPhysicalBytes)
    {
        double totalGb = totalPhysicalBytes / (1024.0 * 1024.0 * 1024.0);

        if (totalGb <= 6.5)
        {
            return 3072; // 3 GB
        }
        if (totalGb <= 8.5)
        {
            return 4096; // 4 GB
        }
        if (totalGb <= 16.5)
        {
            return 8192; // 8 GB
        }
        if (totalGb <= 32.5)
        {
            return 12288; // 12 GB
        }

        return 16384; // 16 GB for 32GB+
    }

    public static string FormatMemoryFlags(int ramMb)
    {
        if (ramMb <= 0) ramMb = 4096;

        if (ramMb % 1024 == 0)
        {
            int gb = ramMb / 1024;
            return $"-Xms{gb}G -Xmx{gb}G";
        }

        return $"-Xms{ramMb}M -Xmx{ramMb}M";
    }

    public static string GenerateFlags(MinecraftGcProfile profile, int ramMb)
    {
        string memFlags = FormatMemoryFlags(ramMb);

        return profile switch
        {
            MinecraftGcProfile.GenerationalZgc => $"{memFlags} {GenerationalZgcFlags}",
            MinecraftGcProfile.AikarG1gc => $"{memFlags} {AikarG1gcFlags}",
            _ => $"{memFlags} {GenerationalZgcFlags}"
        };
    }

    public static MinecraftGcProfile GetRecommendedProfile(MinecraftVersionInfo? version)
    {
        if (version == null) return MinecraftGcProfile.GenerationalZgc;
        if (version.IsNeoForge121Plus || version.RecommendedJavaVersion >= 21)
        {
            return MinecraftGcProfile.GenerationalZgc;
        }
        return MinecraftGcProfile.AikarG1gc;
    }
}
