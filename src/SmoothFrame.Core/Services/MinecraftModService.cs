using System;
using System.IO;
using System.Linq;
using SmoothFrame.Core.Models;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Service responsible for managing the bundled SmoothFrame NeoForge mod artifact,
/// detecting its installation state across Minecraft launcher mods directories,
/// and executing 1-click install and uninstall operations.
/// </summary>
public static class MinecraftModService
{
    public const string ModJarFileName = "SmoothFrameMod-1.21.1.jar";
    public const string ModId = "smoothframe";

    /// <summary>
    /// Resolves the absolute path to the bundled SmoothFrameMod-1.21.1.jar.
    /// Checks the application distribution directory (Mods/ subdirectory) as well as
    /// local development and test runner workspace paths.
    /// </summary>
    public static string? GetBundledModPath()
    {
        // 1. Check primary distribution path in AppContext.BaseDirectory
        string appBase = AppContext.BaseDirectory;
        string candidate = Path.Combine(appBase, "Mods", ModJarFileName);
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        // 2. Check AppDomain BaseDirectory if different
        string domainBase = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.Equals(appBase, domainBase, StringComparison.OrdinalIgnoreCase))
        {
            candidate = Path.Combine(domainBase, "Mods", ModJarFileName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        // 3. Check lowercase "mods" subdirectory
        candidate = Path.Combine(appBase, "mods", ModJarFileName);
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        // 4. Check Current Working Directory (development root)
        string cwd = Directory.GetCurrentDirectory();
        string[] cwdCandidates =
        {
            Path.Combine(cwd, "mods", ModJarFileName),
            Path.Combine(cwd, "src", "SmoothFrame.UI", "Mods", ModJarFileName),
            Path.Combine(cwd, "Mods", ModJarFileName)
        };

        foreach (var c in cwdCandidates)
        {
            if (File.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }

        // 5. Walk up directory tree from appBase to locate repository root
        try
        {
            var dir = new DirectoryInfo(appBase);
            for (int depth = 0; depth < 6 && dir != null; depth++)
            {
                string devPath1 = Path.Combine(dir.FullName, "mods", ModJarFileName);
                if (File.Exists(devPath1))
                {
                    return Path.GetFullPath(devPath1);
                }

                string devPath2 = Path.Combine(dir.FullName, "src", "SmoothFrame.UI", "Mods", ModJarFileName);
                if (File.Exists(devPath2))
                {
                    return Path.GetFullPath(devPath2);
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Ignore traversal errors and fall back to default expected path
        }

        // Default expected path
        return Path.Combine(appBase, "Mods", ModJarFileName);
    }

    /// <summary>
    /// Checks whether the SmoothFrame mod is installed in the specified launcher mods directory.
    /// </summary>
    public static bool IsModInstalled(string? launcherModsDirectory)
    {
        if (string.IsNullOrWhiteSpace(launcherModsDirectory))
        {
            return false;
        }

        try
        {
            if (!Directory.Exists(launcherModsDirectory))
            {
                return false;
            }

            string targetFile = Path.Combine(launcherModsDirectory, ModJarFileName);
            if (File.Exists(targetFile))
            {
                return true;
            }

            var matching = Directory.GetFiles(launcherModsDirectory, "SmoothFrameMod*.jar", SearchOption.TopDirectoryOnly);
            return matching.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether the SmoothFrame mod is installed in the active launcher's mods directory.
    /// </summary>
    public static bool IsModInstalled(MinecraftLauncherInfo? launcher)
    {
        if (launcher == null)
        {
            return false;
        }

        return IsModInstalled(launcher.ModsDirectory);
    }

    /// <summary>
    /// Safely copies the bundled SmoothFrameMod-1.21.1.jar into the target launcher mods directory.
    /// Cleans up any older version of the mod to avoid duplicate JAR conflicts.
    /// </summary>
    public static bool InstallMod(string? launcherModsDirectory, out string statusMessage)
    {
        if (string.IsNullOrWhiteSpace(launcherModsDirectory))
        {
            statusMessage = "Не указана папка модов лаунчера.";
            return false;
        }

        try
        {
            string? sourcePath = GetBundledModPath();
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                statusMessage = "Не найден скомпилированный JAR-файл мода SmoothFrame.";
                return false;
            }

            if (!Directory.Exists(launcherModsDirectory))
            {
                Directory.CreateDirectory(launcherModsDirectory);
            }

            // Remove any existing older or identically-named SmoothFrame mod files
            var existingMods = Directory.GetFiles(launcherModsDirectory, "SmoothFrameMod*.jar", SearchOption.TopDirectoryOnly);
            foreach (var existing in existingMods)
            {
                try
                {
                    File.Delete(existing);
                }
                catch
                {
                    // Ignore deletion failure if file is to be overwritten
                }
            }

            string targetPath = Path.Combine(launcherModsDirectory, ModJarFileName);
            File.Copy(sourcePath, targetPath, overwrite: true);

            statusMessage = "Мод SmoothFrame успешно установлен в папку лаунчера.";
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = $"Ошибка установки мода SmoothFrame: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Safely copies the bundled SmoothFrameMod-1.21.1.jar into the active launcher's mods directory.
    /// </summary>
    public static bool InstallMod(MinecraftLauncherInfo? launcher, out string statusMessage)
    {
        if (launcher == null)
        {
            statusMessage = "Лаунчер не выбран.";
            return false;
        }

        return InstallMod(launcher.ModsDirectory, out statusMessage);
    }

    /// <summary>
    /// Safely removes all SmoothFrameMod*.jar files from the target launcher mods directory.
    /// </summary>
    public static bool UninstallMod(string? launcherModsDirectory, out string statusMessage)
    {
        if (string.IsNullOrWhiteSpace(launcherModsDirectory) || !Directory.Exists(launcherModsDirectory))
        {
            statusMessage = "Папка модов не найдена.";
            return true;
        }

        try
        {
            var matching = Directory.GetFiles(launcherModsDirectory, "SmoothFrameMod*.jar", SearchOption.TopDirectoryOnly);
            if (matching.Length == 0)
            {
                statusMessage = "Мод не был установлен.";
                return true;
            }

            foreach (var file in matching)
            {
                File.Delete(file);
            }

            statusMessage = "Мод SmoothFrame успешно удален из лаунчера.";
            return true;
        }
        catch (Exception ex)
        {
            statusMessage = $"Ошибка удаления мода SmoothFrame: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Safely removes all SmoothFrameMod*.jar files from the active launcher's mods directory.
    /// </summary>
    public static bool UninstallMod(MinecraftLauncherInfo? launcher, out string statusMessage)
    {
        if (launcher == null)
        {
            statusMessage = "Лаунчер не выбран.";
            return false;
        }

        return UninstallMod(launcher.ModsDirectory, out statusMessage);
    }
}
