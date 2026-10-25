using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SmoothFrame.Installer;

public partial class MainWindow : Window
{
    private readonly bool _isUninstall;
    private readonly string? _targetDirOverride;

    public MainWindow(bool isUninstall = false, string? targetDirOverride = null)
    {
        InitializeComponent();
        _isUninstall = isUninstall;
        _targetDirOverride = targetDirOverride;

        try
        {
            var iconUri = new Uri("pack://application:,,,/SmoothFrame.ico", UriKind.RelativeOrAbsolute);
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch { }

        if (_isUninstall)
        {
            HeaderTitleText.Text = "SmoothFrame • Удаление";
            BannerTitle.Text = "Удаление SmoothFrame";
            BannerSubtitle.Text = "Полная и безопасная очистка компонентов программы";
            InstallPanel.Visibility = Visibility.Collapsed;
            UninstallPanel.Visibility = Visibility.Visible;
            PrimaryActionButton.Content = "Удалить";
            PrimaryActionButton.Background = System.Windows.Media.Brushes.Crimson;
        }
        else
        {
            InstallPathBox.Text = InstallerEngine.GetDefaultInstallPath();
        }
    }

    private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку для установки SmoothFrame",
            InitialDirectory = InstallPathBox.Text
        };

        if (dialog.ShowDialog() == true)
        {
            InstallPathBox.Text = Path.Combine(dialog.FolderName, "SmoothFrame");
        }
    }

    private async void OnPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        ConfigView.Visibility = Visibility.Collapsed;
        ProgressView.Visibility = Visibility.Visible;

        var progress = new Progress<InstallProgress>(p =>
        {
            ProgressStatusText.Text = p.Status;
            ProgressPercentText.Text = $"{p.Percent:F0}%";
            var maxBarWidth = 524; // Width of inner progress area
            ProgressBarFill.Width = Math.Max(0, Math.Min(maxBarWidth, (p.Percent / 100.0) * maxBarWidth));
        });

        if (_isUninstall)
        {
            ProgressTitle.Text = "Выполняется удаление SmoothFrame...";
            try
            {
                await InstallerEngine.PerformUninstallAsync(_targetDirOverride, progress);
                CompletedTitleText.Text = "Удаление завершено";
                CompletedSubText.Text = "SmoothFrame был полностью удалён с вашего компьютера.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при удалении: {ex.Message}", "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }
        }
        else
        {
            ProgressTitle.Text = "Выполняется установка SmoothFrame...";
            var targetDir = InstallPathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetDir))
            {
                targetDir = InstallerEngine.GetDefaultInstallPath();
            }

            bool desktopShortcut = DesktopShortcutCheck.IsChecked == true;
            bool startMenuShortcut = StartMenuShortcutCheck.IsChecked == true;
            bool launchAfter = LaunchAfterCheck.IsChecked == true;

            try
            {
                await InstallerEngine.PerformInstallAsync(targetDir, desktopShortcut, startMenuShortcut, launchAfter, progress);
                CompletedTitleText.Text = "Установка успешно завершена!";
                CompletedSubText.Text = "SmoothFrame готов к использованию и оптимизации ваших игр.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при установке: {ex.Message}", "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }
        }

        ProgressView.Visibility = Visibility.Collapsed;
        CompletedView.Visibility = Visibility.Visible;
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_isUninstall)
        {
            try
            {
                var currentExe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(currentExe) &&
                    currentExe.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(currentExe))
                {
                    var cleanExe = Path.GetFullPath(currentExe);
                    if (cleanExe.IndexOfAny(new[] { '\"', '&', '|', ';', '`', '$', '\r', '\n' }) == -1)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c timeout /t 2 /nobreak > nul & del /f /q \"{cleanExe}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                }
            }
            catch { }
        }
    }
}
