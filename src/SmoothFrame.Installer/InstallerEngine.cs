using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SmoothFrame.Installer;

public record InstallProgress(double Percent, string Status);

public static class InstallerEngine
{
    private const string AppName = "SmoothFrame";
    private const string AppVersion = "1.0.0";
    private const string ExeName = "SmoothFrame.UI.exe";
    private const string UninstallerName = "Uninstall.exe";
    private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SmoothFrame";

    public static bool IsAdministrator()
    {
        try
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static string GetDefaultInstallPath()
    {
        if (IsAdministrator())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    }

    public static void CloseRunningProcesses()
    {
        try
        {
            foreach (var procName in new[] { "SmoothFrame.UI", "SmoothFrame" })
            {
                var processes = Process.GetProcessesByName(procName);
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(2000);
                    }
                    catch { }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
        }
        catch { }
    }

    public static async Task PerformInstallAsync(
        string targetDir,
        bool desktopShortcut,
        bool startMenuShortcut,
        bool launchAfter,
        IProgress<InstallProgress>? progress = null)
    {
        await Task.Run(() => PerformInstall(targetDir, desktopShortcut, startMenuShortcut, launchAfter, silent: false, progress));
    }

    public static void PerformInstall(
        string targetDir,
        bool desktopShortcut,
        bool startMenuShortcut,
        bool launchAfter,
        bool silent,
        IProgress<InstallProgress>? progress = null)
    {
        progress?.Report(new InstallProgress(10, "Остановка активных процессов..."));
        CloseRunningProcesses();

        progress?.Report(new InstallProgress(25, "Подготовка каталога установки..."));
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        progress?.Report(new InstallProgress(45, "Распаковка файлов приложения..."));
        var assembly = Assembly.GetExecutingAssembly();
        using (var stream = assembly.GetManifestResourceStream("SmoothFrame.Installer.Payload.publish_payload.zip"))
        {
            if (stream == null)
            {
                throw new InvalidOperationException("Пакет установки не найден в ресурсах установщика.");
            }

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var fullTarget = Path.GetFullPath(targetDir);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var destinationPath = Path.GetFullPath(Path.Combine(fullTarget, entry.FullName));
                if (!destinationPath.StartsWith(fullTarget.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Недопустимый путь в архиве: {entry.FullName}");
                }

                var entryDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                const int maxRetries = 5;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        entry.ExtractToFile(destinationPath, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < maxRetries)
                    {
                        Thread.Sleep(attempt * 200);
                    }
                }
            }
        }

        progress?.Report(new InstallProgress(70, "Копирование модуля удаления..."));
        try
        {
            var currentExe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
            {
                var uninstallerPath = Path.Combine(targetDir, UninstallerName);
                File.Copy(currentExe, uninstallerPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to copy uninstaller: {ex.Message}");
        }

        progress?.Report(new InstallProgress(80, "Создание ярлыков..."));
        var mainExePath = Path.Combine(targetDir, ExeName);

        if (desktopShortcut)
        {
            try
            {
                var desktopPath = IsAdministrator()
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
                {
                    desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                }

                var shortcutFile = Path.Combine(desktopPath, $"{AppName}.lnk");
                CreateShortcut(shortcutFile, mainExePath, targetDir, "SmoothFrame - Умный игровой оптимизатор Windows");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create desktop shortcut: {ex.Message}");
            }
        }

        if (startMenuShortcut)
        {
            try
            {
                var programsFolder = IsAdministrator()
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                    : Environment.GetFolderPath(Environment.SpecialFolder.Programs);

                var startMenuDir = Path.Combine(programsFolder, AppName);
                if (!Directory.Exists(startMenuDir))
                {
                    Directory.CreateDirectory(startMenuDir);
                }

                var shortcutFile = Path.Combine(startMenuDir, $"{AppName}.lnk");
                CreateShortcut(shortcutFile, mainExePath, targetDir, "SmoothFrame - Умный игровой оптимизатор Windows");

                var uninstallShortcut = Path.Combine(startMenuDir, $"Удалить {AppName}.lnk");
                var uninstallerPath = Path.Combine(targetDir, UninstallerName);
                if (File.Exists(uninstallerPath))
                {
                    CreateShortcut(uninstallShortcut, uninstallerPath, targetDir, $"Удаление {AppName}", "--uninstall");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create start menu shortcut: {ex.Message}");
            }
        }

        progress?.Report(new InstallProgress(90, "Регистрация приложения в Windows..."));
        RegisterUninstall(targetDir);

        progress?.Report(new InstallProgress(100, "Установка успешно завершена!"));

        if (launchAfter)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = mainExePath,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch after install: {ex.Message}");
            }
        }
    }

    public static async Task PerformUninstallAsync(string? targetDirOverride = null, IProgress<InstallProgress>? progress = null)
    {
        await Task.Run(() => PerformUninstall(silent: false, targetDirOverride, progress));
    }

    public static void PerformUninstall(bool silent, string? targetDirOverride = null, IProgress<InstallProgress>? progress = null)
    {
        // 1. Resolve target directory FIRST before touching registry
        string? targetDir = targetDirOverride?.TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(targetDir))
        {
            targetDir = GetInstalledPath()?.TrimEnd('\\', '/');
        }
        if (string.IsNullOrEmpty(targetDir))
        {
            targetDir = GetDefaultInstallPath()?.TrimEnd('\\', '/');
        }

        // Safety Guard: Abort if target directory points to system root or Program Files root
        if (string.IsNullOrWhiteSpace(targetDir) ||
            Path.GetPathRoot(targetDir)?.Equals(targetDir, StringComparison.OrdinalIgnoreCase) == true ||
            string.Equals(targetDir, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(targetDir, Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) ||
            targetDir.Length < 6)
        {
            Debug.WriteLine($"[PerformUninstall] Aborting: targetDir '{targetDir}' is unsafe for deletion.");
            return;
        }

        progress?.Report(new InstallProgress(15, "Остановка работающих процессов..."));
        CloseRunningProcesses();

        progress?.Report(new InstallProgress(35, "Удаление ярлыков..."));
        try
        {
            var userDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");
            if (File.Exists(userDesktop)) File.Delete(userDesktop);

            var commonDesktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), $"{AppName}.lnk");
            if (File.Exists(commonDesktop)) File.Delete(commonDesktop);

            var userPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
            if (Directory.Exists(userPrograms)) Directory.Delete(userPrograms, recursive: true);

            var commonPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
            if (Directory.Exists(commonPrograms)) Directory.Delete(commonPrograms, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing shortcuts: {ex.Message}");
        }

        progress?.Report(new InstallProgress(55, "Удаление автозагрузки и задач..."));
        try
        {
            // Clean Task Scheduler
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/delete /tn \"SmoothFrame_Autostart\" /f",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
        }
        catch { }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }

        progress?.Report(new InstallProgress(70, "Очистка пользовательских данных и снимков..."));
        try
        {
            var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmoothFrame");
            if (Directory.Exists(localAppData))
            {
                Directory.Delete(localAppData, recursive: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error cleaning LocalAppData: {ex.Message}");
        }

        progress?.Report(new InstallProgress(85, "Удаление записи из реестра..."));
        UnregisterUninstall();

        progress?.Report(new InstallProgress(95, "Очистка файлов программы..."));
        
        var dirsToClean = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(targetDir)) dirsToClean.Add(targetDir);

        var regPath = GetInstalledPath()?.TrimEnd('\\', '/');
        if (!string.IsNullOrEmpty(regPath)) dirsToClean.Add(regPath);

        var localProg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
        dirsToClean.Add(localProg);

        var progFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
        dirsToClean.Add(progFiles);

        foreach (var dir in dirsToClean)
        {
            CleanDirectorySafely(dir);
        }

        RefreshWindowsShell();
        progress?.Report(new InstallProgress(100, "Программа успешно удалена."));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_FLUSH = 0x1000;

    public static void RefreshWindowsShell()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    private static void CleanDirectorySafely(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

        // Safety Guard: never delete system roots
        if (Path.GetPathRoot(dir)?.Equals(dir, StringComparison.OrdinalIgnoreCase) == true ||
            string.Equals(dir, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dir, Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) ||
            dir.Length < 6)
        {
            return;
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(dir)) break;

                foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { }
                }

                Directory.Delete(dir, recursive: true);
                break;
            }
            catch
            {
                Thread.Sleep(150);
            }
        }

        if (Directory.Exists(dir))
        {
            try
            {
                var cleanDir = Path.GetFullPath(dir);
                // W-2 Guard: Ensure no command injection characters exist in path
                if (cleanDir.IndexOfAny(new[] { '\"', '&', '|', ';', '`', '$', '\r', '\n' }) == -1)
                {
                    var cmd = $"/c timeout /t 1 /nobreak > nul & rmdir /s /q \"{cleanDir}\"";
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = cmd,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            catch { }
        }
    }

    private static void RegisterUninstall(string targetDir)
    {
        try
        {
            RegistryKey? baseKey = null;
            if (IsAdministrator())
            {
                using var lmHive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                baseKey = lmHive.CreateSubKey(UninstallRegistryPath);
            }
            if (baseKey == null)
            {
                using var cuHive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                baseKey = cuHive.CreateSubKey(UninstallRegistryPath);
            }

            using (baseKey)
            {
                var mainExePath = Path.Combine(targetDir, ExeName);
                var uninstallerPath = Path.Combine(targetDir, UninstallerName);

                baseKey.SetValue("DisplayName", AppName);
                baseKey.SetValue("DisplayVersion", AppVersion);
                baseKey.SetValue("Publisher", "SmoothFrame");
                baseKey.SetValue("InstallLocation", targetDir);
                baseKey.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                baseKey.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall /silent");
                baseKey.SetValue("DisplayIcon", $"\"{mainExePath}\",0");
                baseKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                baseKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                baseKey.SetValue("EstimatedSize", 1024, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register uninstall: {ex.Message}");
        }
    }

    private static void UnregisterUninstall()
    {
        try
        {
            if (IsAdministrator())
            {
                using var lmHive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                lmHive.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
            }
            using var cuHive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            cuHive.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
        }
        catch { }
    }

    public static string? GetInstalledPath()
    {
        try
        {
            using var lmHive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var lm = lmHive.OpenSubKey(UninstallRegistryPath);
            var path = lm?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrEmpty(path)) return path;

            using var cuHive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var cu = cuHive.OpenSubKey(UninstallRegistryPath);
            return cu?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description, string arguments = "")
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            dynamic dShortcut = shortcut!;
            dShortcut.TargetPath = targetPath;
            dShortcut.WorkingDirectory = workingDir;
            dShortcut.Description = description;
            if (!string.IsNullOrEmpty(arguments))
            {
                dShortcut.Arguments = arguments;
            }
            dShortcut.Save();
        }
        finally
        {
            if (shortcut != null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
            if (shell != null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
