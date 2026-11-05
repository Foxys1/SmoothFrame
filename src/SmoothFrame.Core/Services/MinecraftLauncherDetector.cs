using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

public class MinecraftLauncherDetector
{
    private static readonly Regex VersionRegex = new(@"\b(1\.\d+(?:\.\d+)?)\b", RegexOptions.Compiled);

    public string AppDataDirectory { get; }

    public MinecraftLauncherDetector(string? customAppData = null)
    {
        AppDataDirectory = customAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }

    public List<MinecraftLauncherInfo> DetectAllLaunchers()
    {
        var result = new List<MinecraftLauncherInfo>();

        var tlegacy = DetectTLegacy();
        if (tlegacy != null) result.Add(tlegacy);

        var tlauncher = DetectTLauncher();
        if (tlauncher != null) result.Add(tlauncher);

        var klauncher = DetectKLauncher();
        if (klauncher != null) result.Add(klauncher);

        var vanilla = DetectVanilla();
        if (vanilla != null) result.Add(vanilla);

        return result;
    }

    public MinecraftLauncherInfo? GetPrimaryLauncher()
    {
        var all = DetectAllLaunchers();
        if (all.Count == 0) return null;

        // Prefer TLegacy if active version is NeoForge 1.21.1+, or with most versions
        var tlegacy = all.Find(l => l.LauncherType == MinecraftLauncherType.TLegacy);
        if (tlegacy != null && tlegacy.InstalledVersions.Count > 0)
        {
            return tlegacy;
        }

        return all.OrderByDescending(l => l.InstalledVersions.Any(v => v.IsNeoForge121Plus))
                  .ThenByDescending(l => l.InstalledVersions.Count)
                  .FirstOrDefault();
    }

    public MinecraftLauncherInfo? DetectTLegacy()
    {
        string root = Path.Combine(AppDataDirectory, ".tlauncher", "legacy", "Minecraft");
        if (!Directory.Exists(root)) return null;

        string gameDir = Path.Combine(root, "game");
        if (!Directory.Exists(gameDir))
        {
            gameDir = root;
        }

        string versionsDir = Path.Combine(gameDir, "versions");
        string modsDir = Path.Combine(gameDir, "mods");
        string settingsFile = Path.Combine(root, "tl.properties");

        string? activeVersion = null;
        if (File.Exists(settingsFile))
        {
            try
            {
                foreach (var line in File.ReadAllLines(settingsFile))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("login.version=", StringComparison.OrdinalIgnoreCase))
                    {
                        activeVersion = trimmed.Substring("login.version=".Length).Trim();
                        break;
                    }
                }
            }
            catch { }
        }

        var versions = DetectVersions(versionsDir);

        return new MinecraftLauncherInfo
        {
            LauncherType = MinecraftLauncherType.TLegacy,
            Name = "TLegacy Launcher",
            RootDirectory = root,
            GameDirectory = gameDir,
            VersionsDirectory = versionsDir,
            ModsDirectory = modsDir,
            SettingsFilePath = settingsFile,
            ActiveVersionName = activeVersion,
            InstalledVersions = versions,
            IsDetected = true
        };
    }

    public MinecraftLauncherInfo? DetectTLauncher()
    {
        string root = Path.Combine(AppDataDirectory, ".tlauncher");
        string gameDir = Path.Combine(AppDataDirectory, ".minecraft");
        if (!Directory.Exists(root) && !File.Exists(Path.Combine(gameDir, "tlauncher_profiles.json")))
        {
            return null;
        }

        string settingsFile = Path.Combine(root, "tlauncher-2.0.properties");
        string profileFile = Path.Combine(gameDir, "tlauncher_profiles.json");
        string altProps = Path.Combine(root, "tlauncher.properties");

        // Disambiguate from TLegacy: if only .tlauncher exists because of legacy\Minecraft, don't detect as modern TLauncher
        if (!File.Exists(settingsFile) && !File.Exists(profileFile) && !File.Exists(altProps))
        {
            if (!Directory.Exists(Path.Combine(root, "jvms")) &&
                !Directory.Exists(Path.Combine(root, "libraries")) &&
                !Directory.Exists(gameDir))
            {
                return null;
            }
        }

        string versionsDir = Path.Combine(gameDir, "versions");
        string modsDir = Path.Combine(gameDir, "mods");
        if (!File.Exists(settingsFile))
        {
            settingsFile = profileFile;
        }

        var versions = DetectVersions(versionsDir);
        string? activeVersion = null;

        return new MinecraftLauncherInfo
        {
            LauncherType = MinecraftLauncherType.TLauncher,
            Name = "TLauncher",
            RootDirectory = root,
            GameDirectory = gameDir,
            VersionsDirectory = versionsDir,
            ModsDirectory = modsDir,
            SettingsFilePath = settingsFile,
            ActiveVersionName = activeVersion,
            InstalledVersions = versions,
            IsDetected = true
        };
    }

    public MinecraftLauncherInfo? DetectKLauncher()
    {
        string root = Path.Combine(AppDataDirectory, ".klauncher");
        if (!Directory.Exists(root)) return null;

        string gameDir = Path.Combine(root, "game");
        if (!Directory.Exists(gameDir))
        {
            gameDir = Path.Combine(AppDataDirectory, ".minecraft");
        }

        string versionsDir = Path.Combine(gameDir, "versions");
        string modsDir = Path.Combine(gameDir, "mods");
        string settingsFile = Path.Combine(root, "settings.json");

        var versions = DetectVersions(versionsDir);

        return new MinecraftLauncherInfo
        {
            LauncherType = MinecraftLauncherType.KLauncher,
            Name = "KLauncher",
            RootDirectory = root,
            GameDirectory = gameDir,
            VersionsDirectory = versionsDir,
            ModsDirectory = modsDir,
            SettingsFilePath = settingsFile,
            InstalledVersions = versions,
            IsDetected = true
        };
    }

    public MinecraftLauncherInfo? DetectVanilla()
    {
        string gameDir = Path.Combine(AppDataDirectory, ".minecraft");
        if (!Directory.Exists(gameDir)) return null;

        string versionsDir = Path.Combine(gameDir, "versions");
        string modsDir = Path.Combine(gameDir, "mods");
        string settingsFile = Path.Combine(gameDir, "launcher_profiles.json");

        string? activeVersion = null;
        if (File.Exists(settingsFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsFile));
                if (doc.RootElement.TryGetProperty("profiles", out var profiles))
                {
                    foreach (var prop in profiles.EnumerateObject())
                    {
                        if (prop.Value.TryGetProperty("lastVersionId", out var lvid))
                        {
                            activeVersion = lvid.GetString();
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        var versions = DetectVersions(versionsDir);

        return new MinecraftLauncherInfo
        {
            LauncherType = MinecraftLauncherType.Vanilla,
            Name = "Vanilla Minecraft",
            RootDirectory = gameDir,
            GameDirectory = gameDir,
            VersionsDirectory = versionsDir,
            ModsDirectory = modsDir,
            SettingsFilePath = settingsFile,
            ActiveVersionName = activeVersion,
            InstalledVersions = versions,
            IsDetected = true
        };
    }

    public MinecraftLauncherInfo InspectCustomDirectory(string customGameDirectory)
    {
        string versionsDir = Path.Combine(customGameDirectory, "versions");
        string modsDir = Path.Combine(customGameDirectory, "mods");
        string settingsFile = Path.Combine(customGameDirectory, "launcher_profiles.json");

        var versions = DetectVersions(versionsDir);

        return new MinecraftLauncherInfo
        {
            LauncherType = MinecraftLauncherType.Custom,
            Name = "Custom Minecraft",
            RootDirectory = customGameDirectory,
            GameDirectory = customGameDirectory,
            VersionsDirectory = versionsDir,
            ModsDirectory = modsDir,
            SettingsFilePath = File.Exists(settingsFile) ? settingsFile : null,
            InstalledVersions = versions,
            IsDetected = Directory.Exists(customGameDirectory)
        };
    }

    public List<MinecraftVersionInfo> DetectVersions(string versionsDirectory)
    {
        var list = new List<MinecraftVersionInfo>();
        if (!Directory.Exists(versionsDirectory)) return list;

        try
        {
            var directories = Directory.GetDirectories(versionsDirectory);
            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(dirName)) continue;

                var info = ParseVersionDirectory(dir, dirName);
                if (info != null)
                {
                    list.Add(info);
                }
            }
        }
        catch { }

        return list;
    }

    public MinecraftVersionInfo ParseVersionDirectory(string directoryPath, string versionName)
    {
        string jsonPath = Path.Combine(directoryPath, $"{versionName}.json");
        string jarPath = Path.Combine(directoryPath, $"{versionName}.jar");

        string rawId = versionName;
        string? mainClass = null;

        if (File.Exists(jsonPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                if (doc.RootElement.TryGetProperty("id", out var idElem))
                {
                    rawId = idElem.GetString() ?? versionName;
                }
                if (doc.RootElement.TryGetProperty("mainClass", out var mcElem))
                {
                    mainClass = mcElem.GetString();
                }
            }
            catch { }
        }

        var loader = DetectModLoader(versionName, rawId, mainClass, jsonPath);
        var baseVersion = ExtractBaseVersion(versionName, rawId);

        bool isNeoForge121Plus = false;
        if (loader == MinecraftModLoader.NeoForge)
        {
            isNeoForge121Plus = CheckIfVersionIs121Plus(baseVersion, versionName, rawId);
        }

        int recommendedJava = 21;
        if (isNeoForge121Plus)
        {
            recommendedJava = 21;
        }
        else
        {
            var (major, minor, _) = ParseSemVer(baseVersion);
            if (minor >= 21 || (major > 1)) recommendedJava = 21;
            else if (minor >= 17) recommendedJava = 17;
            else recommendedJava = 8;
        }

        return new MinecraftVersionInfo
        {
            Id = versionName,
            BaseVersion = baseVersion,
            ModLoader = loader,
            IsNeoForge121Plus = isNeoForge121Plus,
            RecommendedJavaVersion = recommendedJava,
            VersionDirectory = directoryPath,
            JsonPath = File.Exists(jsonPath) ? jsonPath : null,
            JarPath = File.Exists(jarPath) ? jarPath : null
        };
    }

    private static MinecraftModLoader DetectModLoader(string dirName, string rawId, string? mainClass, string jsonPath)
    {
        string combined = $"{dirName} {rawId} {mainClass ?? string.Empty}".ToLowerInvariant();

        if (combined.Contains("neoforge") || combined.Contains("net.neoforged"))
        {
            return MinecraftModLoader.NeoForge;
        }
        if (combined.Contains("forge") || combined.Contains("net.minecraftforge"))
        {
            return MinecraftModLoader.Forge;
        }
        if (combined.Contains("fabric") || combined.Contains("net.fabricmc"))
        {
            return MinecraftModLoader.Fabric;
        }
        if (combined.Contains("quilt") || combined.Contains("org.quiltmc"))
        {
            return MinecraftModLoader.Quilt;
        }
        if (combined.Contains("optifine"))
        {
            return MinecraftModLoader.OptiFine;
        }

        // Secondary check inside JSON file content if needed
        if (File.Exists(jsonPath))
        {
            try
            {
                string jsonText = File.ReadAllText(jsonPath);
                if (jsonText.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
                    jsonText.Contains("net.neoforged", StringComparison.OrdinalIgnoreCase))
                {
                    return MinecraftModLoader.NeoForge;
                }
                if (jsonText.Contains("net.minecraftforge", StringComparison.OrdinalIgnoreCase))
                {
                    return MinecraftModLoader.Forge;
                }
                if (jsonText.Contains("fabricmc", StringComparison.OrdinalIgnoreCase))
                {
                    return MinecraftModLoader.Fabric;
                }
            }
            catch { }
        }

        return MinecraftModLoader.Vanilla;
    }

    public static string ExtractBaseVersion(string dirName, string rawId)
    {
        var match = VersionRegex.Match(rawId);
        if (match.Success) return match.Groups[1].Value;

        match = VersionRegex.Match(dirName);
        if (match.Success) return match.Groups[1].Value;

        return "1.21.1";
    }

    public static bool CheckIfVersionIs121Plus(string baseVersion, string dirName, string rawId)
    {
        var (major, minor, patch) = ParseSemVer(baseVersion);
        if (major > 1) return true;
        if (major == 1)
        {
            if (minor > 21) return true;
            if (minor == 21 && patch >= 0) return true;
        }

        string combined = $"{dirName} {rawId}".ToLowerInvariant();
        if (combined.Contains("1.21.0") || combined.Contains("1.21.1") || combined.Contains("1.21.2") || combined.Contains("1.21.3") || combined.Contains("1.21.4") || combined.Contains("1.21"))
        {
            return true;
        }

        return false;
    }

    public static (int Major, int Minor, int Patch) ParseSemVer(string version)
    {
        var match = VersionRegex.Match(version);
        if (!match.Success) return (1, 21, 1);

        var parts = match.Groups[1].Value.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var ma) ? ma : 1;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 21;
        int patch = parts.Length > 2 && int.TryParse(parts[2], out var pa) ? pa : 0;

        return (major, minor, patch);
    }
}
