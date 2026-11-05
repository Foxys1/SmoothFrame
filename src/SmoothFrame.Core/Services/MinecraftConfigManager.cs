using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

public class MinecraftConfigManager
{
    public static bool ApplyJvmSettings(MinecraftLauncherInfo launcher, string jvmArgs, int ramMb)
    {
        if (launcher == null) return false;

        bool success = false;

        if (launcher.LauncherType == MinecraftLauncherType.TLegacy)
        {
            success = ApplyTLegacySettings(launcher, jvmArgs, ramMb);
        }

        // Also update launcher_profiles.json if present in game directory
        if (!string.IsNullOrWhiteSpace(launcher.GameDirectory))
        {
            string profilesPath = Path.Combine(launcher.GameDirectory, "launcher_profiles.json");
            if (File.Exists(profilesPath))
            {
                bool pSuccess = ApplyLauncherProfilesSettings(profilesPath, jvmArgs, launcher.ActiveVersionName);
                success = success || pSuccess;
            }
        }

        return success;
    }

    public static bool ApplyTLegacySettings(MinecraftLauncherInfo launcher, string jvmArgs, int ramMb)
    {
        string? propertiesPath = launcher.SettingsFilePath;
        if (string.IsNullOrEmpty(propertiesPath) || !File.Exists(propertiesPath))
        {
            propertiesPath = Path.Combine(launcher.RootDirectory, "tl.properties");
        }

        if (!File.Exists(propertiesPath))
        {
            return false;
        }

        try
        {
            // Automatic .bak backup preservation
            CreateBakFile(propertiesPath);

            var lines = new List<string>(File.ReadAllLines(propertiesPath));
            bool argsUpdated = false;
            bool xmxUpdated = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();

                if (trimmed.StartsWith("minecraft.args.jvm=", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("minecraft.args=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"minecraft.args={jvmArgs}";
                    argsUpdated = true;
                }
                else if (trimmed.StartsWith("minecraft.xmx=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"minecraft.xmx={ramMb}";
                    xmxUpdated = true;
                }
            }

            if (!argsUpdated)
            {
                lines.Add($"minecraft.args={jvmArgs}");
            }

            if (!xmxUpdated && ramMb > 0)
            {
                lines.Add($"minecraft.xmx={ramMb}");
            }

            File.WriteAllLines(propertiesPath, lines);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ApplyLauncherProfilesSettings(string profilesPath, string jvmArgs, string? targetVersionName = null)
    {
        if (!File.Exists(profilesPath)) return false;

        try
        {
            // Automatic .bak backup preservation
            CreateBakFile(profilesPath);

            string content = File.ReadAllText(profilesPath);
            var root = JsonNode.Parse(content);
            if (root is not JsonObject rootObj) return false;

            if (!rootObj.TryGetPropertyValue("profiles", out var profilesNode) || profilesNode is not JsonObject profilesObj)
            {
                profilesObj = new JsonObject();
                rootObj["profiles"] = profilesObj;
            }

            bool updatedAny = false;
            foreach (var kvp in profilesObj)
            {
                if (kvp.Value is JsonObject profile)
                {
                    if (string.IsNullOrEmpty(targetVersionName) ||
                        (profile.TryGetPropertyValue("lastVersionId", out var lvid) &&
                         string.Equals(lvid?.ToString(), targetVersionName, StringComparison.OrdinalIgnoreCase)))
                    {
                        profile["javaArgs"] = jvmArgs;
                        updatedAny = true;
                    }
                }
            }

            if (!updatedAny)
            {
                // Create or update default profile
                var newProfile = new JsonObject
                {
                    ["name"] = targetVersionName ?? "SmoothFrame Optimized",
                    ["lastVersionId"] = targetVersionName ?? "latest-release",
                    ["javaArgs"] = jvmArgs
                };
                profilesObj["SmoothFrame"] = newProfile;
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(profilesPath, rootObj.ToJsonString(options));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string CreateBakFile(string filePath)
    {
        string bakPath = filePath + ".bak";
        try
        {
            if (File.Exists(filePath))
            {
                File.Copy(filePath, bakPath, overwrite: true);
            }
        }
        catch { }
        return bakPath;
    }

    public static bool RestoreBakFile(string filePath)
    {
        string bakPath = filePath + ".bak";
        try
        {
            if (File.Exists(bakPath))
            {
                File.Copy(bakPath, filePath, overwrite: true);
                return true;
            }
        }
        catch { }
        return false;
    }

    public static bool RollbackSettings(MinecraftLauncherInfo launcher, out string message)
    {
        if (launcher == null)
        {
            message = "Лаунчер не выбран.";
            return false;
        }

        bool restoredAny = false;

        // 1. Rollback TLegacy tl.properties if applicable
        string? tlProps = launcher.SettingsFilePath;
        if (string.IsNullOrEmpty(tlProps) || !File.Exists(tlProps))
        {
            tlProps = Path.Combine(launcher.RootDirectory, "tl.properties");
        }

        if (File.Exists(tlProps))
        {
            string bak = tlProps + ".bak";
            if (File.Exists(bak))
            {
                if (RestoreBakFile(tlProps))
                {
                    restoredAny = true;
                }
            }
            else
            {
                // If .bak was not found, clean custom JVM args to restore vanilla
                try
                {
                    var lines = new List<string>(File.ReadAllLines(tlProps));
                    bool modified = false;
                    for (int i = lines.Count - 1; i >= 0; i--)
                    {
                        var trimmed = lines[i].Trim();
                        if (trimmed.StartsWith("minecraft.args=", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("minecraft.args.jvm=", StringComparison.OrdinalIgnoreCase))
                        {
                            lines.RemoveAt(i);
                            modified = true;
                        }
                    }
                    if (modified)
                    {
                        File.WriteAllLines(tlProps, lines);
                        restoredAny = true;
                    }
                }
                catch { }
            }
        }

        // 2. Rollback launcher_profiles.json if present
        if (!string.IsNullOrWhiteSpace(launcher.GameDirectory))
        {
            string profilesPath = Path.Combine(launcher.GameDirectory, "launcher_profiles.json");
            if (File.Exists(profilesPath))
            {
                string bak = profilesPath + ".bak";
                if (File.Exists(bak))
                {
                    if (RestoreBakFile(profilesPath))
                    {
                        restoredAny = true;
                    }
                }
                else
                {
                    try
                    {
                        string content = File.ReadAllText(profilesPath);
                        var root = JsonNode.Parse(content);
                        if (root is JsonObject rootObj && rootObj.TryGetPropertyValue("profiles", out var profilesNode) && profilesNode is JsonObject profilesObj)
                        {
                            profilesObj.Remove("SmoothFrame");
                            foreach (var kvp in profilesObj)
                            {
                                if (kvp.Value is JsonObject profile && profile.ContainsKey("javaArgs"))
                                {
                                    profile.Remove("javaArgs");
                                    restoredAny = true;
                                }
                            }
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            File.WriteAllText(profilesPath, rootObj.ToJsonString(options));
                        }
                    }
                    catch { }
                }
            }
        }

        if (restoredAny)
        {
            message = $"Настройки {launcher.Name} успешно восстановлены (бэкап .bak возвращен или очищены JVM флаги)!";
            return true;
        }

        message = $"Файлы конфигурации {launcher.Name} уже находятся в исходном состоянии.";
        return true;
    }
}
