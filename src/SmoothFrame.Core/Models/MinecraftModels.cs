using System;
using System.Collections.Generic;

namespace SmoothFrame.Core.Models;

public enum MinecraftLauncherType
{
    TLegacy,
    TLauncher,
    KLauncher,
    Vanilla,
    Custom
}

public enum MinecraftModLoader
{
    NeoForge,
    Forge,
    Fabric,
    Quilt,
    OptiFine,
    Vanilla,
    Unknown
}

public enum MinecraftGcProfile
{
    GenerationalZgc,
    AikarG1gc
}

public class MinecraftVersionInfo
{
    public string Id { get; set; } = string.Empty;
    public string BaseVersion { get; set; } = string.Empty;
    public MinecraftModLoader ModLoader { get; set; } = MinecraftModLoader.Vanilla;
    public string? ModLoaderVersion { get; set; }
    public bool IsNeoForge => ModLoader == MinecraftModLoader.NeoForge;
    public bool IsNeoForge121Plus { get; set; }
    public int RecommendedJavaVersion { get; set; } = 21;
    public MinecraftGcProfile RecommendedGcProfile => RecommendedJavaVersion >= 21 ? MinecraftGcProfile.GenerationalZgc : MinecraftGcProfile.AikarG1gc;
    public string VersionDirectory { get; set; } = string.Empty;
    public string? JsonPath { get; set; }
    public string? JarPath { get; set; }

    public override string ToString() => $"{Id} ({ModLoader} {BaseVersion})";
}

public class MinecraftLauncherInfo
{
    public MinecraftLauncherType LauncherType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RootDirectory { get; set; } = string.Empty;
    public string GameDirectory { get; set; } = string.Empty;
    public string VersionsDirectory { get; set; } = string.Empty;
    public string ModsDirectory { get; set; } = string.Empty;
    public string? SettingsFilePath { get; set; }
    public string? ActiveVersionName { get; set; }
    public List<MinecraftVersionInfo> InstalledVersions { get; set; } = new();
    public bool IsDetected { get; set; }

    public override string ToString() => Name;

    public MinecraftVersionInfo? GetActiveVersion()
    {
        if (string.IsNullOrWhiteSpace(ActiveVersionName))
        {
            return InstalledVersions.Find(v => v.IsNeoForge121Plus) ?? InstalledVersions.Find(v => v.IsNeoForge) ?? (InstalledVersions.Count > 0 ? InstalledVersions[0] : null);
        }

        return InstalledVersions.Find(v => string.Equals(v.Id, ActiveVersionName, StringComparison.OrdinalIgnoreCase))
            ?? InstalledVersions.Find(v => v.Id != null && v.Id.Contains(ActiveVersionName, StringComparison.OrdinalIgnoreCase))
            ?? InstalledVersions.Find(v => v.IsNeoForge121Plus)
            ?? (InstalledVersions.Count > 0 ? InstalledVersions[0] : null);
    }
}

public class MinecraftModInfo
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public string Category { get; set; } = "Optimization";
    public string Description { get; set; } = string.Empty;
    public string RecommendationReason { get; set; } = string.Empty;
    public string? ModUrl { get; set; }
}

public class MinecraftModAdvisorReport
{
    public string ModsDirectory { get; set; } = string.Empty;
    public int TotalInstalledModsCount { get; set; }
    public List<MinecraftModInfo> DetectedPerformanceMods { get; set; } = new();
    public List<MinecraftModInfo> RecommendedMods { get; set; } = new();
    public List<MinecraftModInfo> BuiltinTechnologies { get; set; } = new();

    public bool HasSodium { get; set; }
    public bool HasIris { get; set; }
    public bool HasDistantHorizons { get; set; }
    public bool HasEntityCulling { get; set; }
    public bool HasFerriteCore { get; set; }
    public bool HasImmediatelyFast { get; set; }
    public bool HasModernFix { get; set; }
    public bool HasSmoothFrameMod { get; set; }

    public string Summary { get; set; } = string.Empty;
}

public class MemoryCleanResult
{
    public bool Success { get; set; }
    public bool StandbyListPurged { get; set; }
    public bool WorkingSetsPurged { get; set; }
    public bool RequiresAdminForStandby { get; set; }
    public long BytesFreedEstimate { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MinecraftTelemetryData
{
    [System.Text.Json.Serialization.JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("mc_version")]
    public string McVersion { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("fps")]
    public int Fps { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("one_percent_low")]
    public int OnePercentLow { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("frametime_ms")]
    public double FrametimeMs { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("memory_used_mb")]
    public long MemoryUsedMb { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("memory_max_mb")]
    public long MemoryMaxMb { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("chunks_pending")]
    public int ChunksPending { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("entities_culled")]
    public int EntitiesCulled { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timestamp_ms")]
    public long TimestampMs { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset Timestamp => TimestampMs > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs)
        : DateTimeOffset.UtcNow;
}

public class MinecraftTelemetryPayload : MinecraftTelemetryData
{
}

