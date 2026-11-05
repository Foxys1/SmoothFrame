using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace SmoothFrame.Core.Services;

public static class WindowsGpuService
{
    public const string DirectXUserGpuKey = @"Software\Microsoft\DirectX\UserGpuPreferences";
    public const string HighPerformanceValue = "GpuPreference=2;";

    public static bool LastOperationFailedDueToPermission { get; private set; }

    public static bool SetHighPerformanceGpu(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        try
        {
            // Normalize path
            string fullPath = Path.GetFullPath(executablePath);

            using var key = Registry.CurrentUser.CreateSubKey(DirectXUserGpuKey, writable: true);
            if (key == null) return false;

            key.SetValue(fullPath, HighPerformanceValue, RegistryValueKind.String);
            LastOperationFailedDueToPermission = false;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            LastOperationFailedDueToPermission = true;
            return false;
        }
        catch (System.Security.SecurityException)
        {
            LastOperationFailedDueToPermission = true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static string? GetGpuPreference(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return null;

        try
        {
            string fullPath = Path.GetFullPath(executablePath);

            using var key = Registry.CurrentUser.OpenSubKey(DirectXUserGpuKey, writable: false);
            if (key == null) return null;

            return key.GetValue(fullPath) as string;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsHighPerformance(string executablePath)
    {
        var pref = GetGpuPreference(executablePath);
        return pref != null && pref.Contains("GpuPreference=2", StringComparison.OrdinalIgnoreCase);
    }

    public static bool RemoveGpuPreference(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        try
        {
            string fullPath = Path.GetFullPath(executablePath);

            using var key = Registry.CurrentUser.OpenSubKey(DirectXUserGpuKey, writable: true);
            if (key == null) return false;

            key.DeleteValue(fullPath, throwOnMissingValue: false);
            LastOperationFailedDueToPermission = false;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            LastOperationFailedDueToPermission = true;
            return false;
        }
        catch (System.Security.SecurityException)
        {
            LastOperationFailedDueToPermission = true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static int RegisterDetectedJavas(IEnumerable<string> javaPaths)
    {
        if (javaPaths == null) return 0;

        int count = 0;
        foreach (var path in javaPaths)
        {
            if (!string.IsNullOrWhiteSpace(path) && SetHighPerformanceGpu(path))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Scans standard locations, TLegacy/launcher runtimes, Adoptium, JavaSoft registry, and environment variables
    /// to locate all real full paths of javaw.exe.
    /// </summary>
    public static List<string> LocateAllJavaRuntimes(string? customLauncherDir = null)
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            try
            {
                candidate = candidate.Trim('"', ' ', '\t');
                if (File.Exists(candidate))
                {
                    string full = Path.GetFullPath(candidate);
                    string fileName = Path.GetFileName(full);
                    if (fileName.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        discovered.Add(full);
                    }
                    else if (fileName.Equals("java.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check for adjacent javaw.exe
                        string? dir = Path.GetDirectoryName(full);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            string javaw = Path.Combine(dir, "javaw.exe");
                            if (File.Exists(javaw))
                            {
                                discovered.Add(Path.GetFullPath(javaw));
                            }
                        }
                    }
                }
            }
            catch { }
        }

        void SearchDirectorySafe(string rootDir, int maxDepth = 4)
        {
            if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir)) return;

            try
            {
                // Direct bin check
                string directBin = Path.Combine(rootDir, "bin", "javaw.exe");
                AddIfValid(directBin);

                string directJavaw = Path.Combine(rootDir, "javaw.exe");
                AddIfValid(directJavaw);

                if (maxDepth <= 0) return;

                foreach (var dir in Directory.GetDirectories(rootDir))
                {
                    string subBin = Path.Combine(dir, "bin", "javaw.exe");
                    AddIfValid(subBin);
                    string subJavaw = Path.Combine(dir, "javaw.exe");
                    AddIfValid(subJavaw);

                    if (maxDepth > 1)
                    {
                        SearchDirectorySafe(dir, maxDepth - 1);
                    }
                }
            }
            catch { }
        }

        // 1. Custom launcher or specific directory/file provided
        if (!string.IsNullOrWhiteSpace(customLauncherDir))
        {
            string cleanPath = customLauncherDir.Trim('"', ' ', '\t');
            if (File.Exists(cleanPath))
            {
                AddIfValid(cleanPath);
                string? parent = Path.GetDirectoryName(cleanPath);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    SearchDirectorySafe(parent, 4);
                }
            }
            else if (Directory.Exists(cleanPath))
            {
                SearchDirectorySafe(cleanPath, 4);
            }
        }

        // 2. TLegacy and typical Minecraft launcher runtimes in AppData
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            SearchDirectorySafe(Path.Combine(appData, ".tlauncher", "legacy", "Minecraft", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(appData, ".tlauncher", "legacy", "Minecraft", "game", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(appData, ".tlauncher", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(appData, ".tlauncher", "jvms"), 4);
            SearchDirectorySafe(Path.Combine(appData, ".tlegacy", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(appData, ".tlegacy", "jvms"), 4);
            SearchDirectorySafe(Path.Combine(appData, ".minecraft", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(appData, ".klauncher", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(appData, "PrismLauncher", "java"), 4);
            SearchDirectorySafe(Path.Combine(appData, "com.modrinth.theseus", "meta", "java"), 4);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            SearchDirectorySafe(Path.Combine(localAppData, "Programs", "Eclipse Adoptium"), 4);
            SearchDirectorySafe(Path.Combine(localAppData, "Programs", "Common", "Java"), 4);
            SearchDirectorySafe(Path.Combine(localAppData, "Minecraft", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(localAppData, "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(localAppData, "Microsoft", "WinGet", "Packages"), 4);
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            SearchDirectorySafe(Path.Combine(userProfile, "curseforge", "minecraft", "Install", "runtime"), 4);
            SearchDirectorySafe(Path.Combine(userProfile, ".jdks"), 4);
            SearchDirectorySafe(Path.Combine(userProfile, "scoop", "apps"), 4);
        }

        // 3. Program Files & Program Files (x86) JDK/JRE, Adoptium, BellSoft, Corretto, Microsoft
        var progFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        var vendors = new[]
        {
            "Java", "Eclipse Adoptium", "AdoptOpenJDK", "BellSoft", "Amazon Corretto",
            "Microsoft", "Zulu", "Semeru"
        };

        foreach (var prog in progFolders.Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)))
        {
            foreach (var vendor in vendors)
            {
                SearchDirectorySafe(Path.Combine(prog, vendor), 3);
            }
        }

        // 4. Windows Registry: JavaSoft, Eclipse Adoptium, Microsoft, BellSoft (both HKLM and HKCU, 64-bit and 32-bit views)
        ScanRegistryJava(RegistryHive.LocalMachine, RegistryView.Registry64, AddIfValid);
        ScanRegistryJava(RegistryHive.LocalMachine, RegistryView.Registry32, AddIfValid);
        ScanRegistryJava(RegistryHive.CurrentUser, RegistryView.Registry64, AddIfValid);
        ScanRegistryJava(RegistryHive.CurrentUser, RegistryView.Registry32, AddIfValid);

        // 5. Environment variables: JAVA_HOME, JDK_HOME
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            string clean = javaHome.Trim('"', ' ', '\t');
            AddIfValid(Path.Combine(clean, "bin", "javaw.exe"));
            AddIfValid(Path.Combine(clean, "javaw.exe"));
        }

        string? jdkHome = Environment.GetEnvironmentVariable("JDK_HOME");
        if (!string.IsNullOrWhiteSpace(jdkHome))
        {
            string clean = jdkHome.Trim('"', ' ', '\t');
            AddIfValid(Path.Combine(clean, "bin", "javaw.exe"));
            AddIfValid(Path.Combine(clean, "javaw.exe"));
        }

        // 6. PATH environment variable
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var parts = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                string clean = part.Trim('"', ' ', '\t');
                if (Directory.Exists(clean))
                {
                    AddIfValid(Path.Combine(clean, "javaw.exe"));
                }
            }
        }

        // 7. Running javaw/java processes
        try
        {
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("javaw").Concat(System.Diagnostics.Process.GetProcessesByName("java")))
            {
                try
                {
                    string? path = ProcessTunerService.TryGetExecutablePath(proc);
                    AddIfValid(path);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }

        return discovered.ToList();
    }

    private static void ScanRegistryJava(RegistryHive hive, RegistryView view, Action<string?> onJavaFound)
    {
        string[] rootKeys =
        {
            @"SOFTWARE\JavaSoft",
            @"SOFTWARE\Eclipse Adoptium",
            @"SOFTWARE\Eclipse Foundation",
            @"SOFTWARE\BellSoft",
            @"SOFTWARE\Microsoft\JDK",
            @"SOFTWARE\Zulu",
            @"SOFTWARE\Amazon Corretto"
        };

        foreach (var rootKey in rootKeys)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var sub = baseKey.OpenSubKey(rootKey);
                if (sub == null) continue;

                ScanRegistryKeyRecursive(sub, onJavaFound, maxDepth: 4);
            }
            catch { }
        }
    }

    private static void ScanRegistryKeyRecursive(RegistryKey key, Action<string?> onJavaFound, int maxDepth)
    {
        if (key == null || maxDepth < 0) return;

        try
        {
            foreach (var valName in new[] { "JavaHome", "Path", "InstallDir", "Home", "InstallPath" })
            {
                if (key.GetValue(valName) is string home && !string.IsNullOrWhiteSpace(home))
                {
                    home = home.Trim('"', ' ', '\t');
                    onJavaFound(Path.Combine(home, "bin", "javaw.exe"));
                    onJavaFound(Path.Combine(home, "javaw.exe"));
                }
            }

            if (maxDepth > 0)
            {
                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subName);
                        if (subKey != null)
                        {
                            ScanRegistryKeyRecursive(subKey, onJavaFound, maxDepth - 1);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Scans and registers all detected Java runtimes with DirectX High Performance GPU.
    /// Returns the absolute paths of all registered runtimes.
    /// </summary>
    public static List<string> RegisterAllDetectedJavaRuntimes(string? customLauncherDir = null)
    {
        var runtimes = LocateAllJavaRuntimes(customLauncherDir);
        var registered = new List<string>();

        foreach (var runtimePath in runtimes)
        {
            if (SetHighPerformanceGpu(runtimePath))
            {
                registered.Add(runtimePath);
            }
        }

        return registered;
    }
}
