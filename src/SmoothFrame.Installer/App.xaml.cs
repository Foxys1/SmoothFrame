using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace SmoothFrame.Installer;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"Произошла ошибка при запуске программы установки:\n\n{e.Exception.Message}", "SmoothFrame Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Shutdown(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Критическая ошибка установки:\n\n{ex.Message}", "SmoothFrame Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    private void OnStartup(object sender, StartupEventArgs e)
    {
        string currentExe = Environment.ProcessPath ?? "";
        string exeName = Path.GetFileName(currentExe);
        bool isNamedUninstall = exeName.Contains("uninstall", StringComparison.OrdinalIgnoreCase);

        bool isUninstall = isNamedUninstall ||
                           e.Args.Any(a => string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(a, "-uninstall", StringComparison.OrdinalIgnoreCase));

        bool isSilent = e.Args.Any(a => string.Equals(a, "/s", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));

        bool isFromTemp = e.Args.Any(a => string.Equals(a, "--from-temp", StringComparison.OrdinalIgnoreCase));

        // When uninstalling from installed program folder, copy to %TEMP% so program files aren't locked during deletion
        if (isUninstall && !isFromTemp)
        {
            try
            {
                string? exeDir = Path.GetDirectoryName(currentExe);
                bool isInTemp = !string.IsNullOrEmpty(currentExe) && 
                                currentExe.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);

                string? installedPath = null;
                if (!isInTemp && !string.IsNullOrEmpty(exeDir))
                {
                    installedPath = exeDir;
                }
                else
                {
                    installedPath = InstallerEngine.GetInstalledPath() ?? exeDir ?? AppContext.BaseDirectory;
                }
                installedPath = installedPath.TrimEnd('\\', '/');

                if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                {
                    var tempExe = Path.Combine(Path.GetTempPath(), "SmoothFrame_Uninstall_Runner.exe");
                    File.Copy(currentExe, tempExe, overwrite: true);

                    var forwardArgs = string.Join(" ", e.Args.Concat(new[] { "--from-temp", "--uninstall", $"--target-dir \"{installedPath}\"" }));
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempExe,
                        Arguments = forwardArgs,
                        UseShellExecute = true
                    });
                    Shutdown(0);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to spawn temp uninstaller: {ex.Message}");
            }
        }

        // Parse optional target-dir argument
        string? targetDirOverride = null;
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (string.Equals(e.Args[i], "--target-dir", StringComparison.OrdinalIgnoreCase))
            {
                targetDirOverride = e.Args[i + 1].Trim('\"').TrimEnd('\\', '/');
                break;
            }
        }

        if (isSilent)
        {
            if (isUninstall)
            {
                InstallerEngine.PerformUninstall(silent: true, targetDirOverride: targetDirOverride);
            }
            else
            {
                InstallerEngine.PerformInstall(InstallerEngine.GetDefaultInstallPath(), desktopShortcut: true, startMenuShortcut: true, launchAfter: false, silent: true);
            }
            Shutdown(0);
            return;
        }

        var mainWindow = new MainWindow(isUninstall, targetDirOverride);
        mainWindow.Show();
    }
}

