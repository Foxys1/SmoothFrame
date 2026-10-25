using System.Windows;
using SmoothFrame.UI.Localization;
using SmoothFrame.UI.Models;

namespace SmoothFrame.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"Произошла ошибка в приложении:\n\n{e.Exception.Message}", "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Критическая ошибка приложения:\n\n{ex.Message}", "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load persisted UI preferences (language, onboarding state) before any window is created.
        var settings = UiSettings.Load();
        if (!LocalizationManager.IsInitialized)
        {
            LocalizationManager.Initialize(settings.Language);
        }
    }
}
