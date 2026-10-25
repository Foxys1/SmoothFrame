using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SmoothFrame.UI.Localization;
using SmoothFrame.UI.Models;
using SmoothFrame.UI.ViewModels;

namespace SmoothFrame.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly UiSettings _settings;
    private bool _suppressLanguageEvents = true;
    private bool _navReady;
    private Grid? _currentPage;
    private CancellationTokenSource? _welcomeCts;
    private int _welcomeIndex;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            var iconUri = new Uri("pack://application:,,,/SmoothFrame.ico", UriKind.RelativeOrAbsolute);
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch { }

        _settings = UiSettings.Load();
        if (!LocalizationManager.IsInitialized)
        {
            LocalizationManager.Initialize(_settings.Language);
        }

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            WindowState = WindowState.Minimized;
        }

        Closing += OnMainWindowClosing;

        // Language pickers (onboarding + settings) share one source of truth.
        OnboardingLanguageCombo.ItemsSource = LocalizationManager.Languages;
        LanguageCombo.ItemsSource = LocalizationManager.Languages;
        SelectLanguageInCombos(LocalizationManager.CurrentCode);
        _suppressLanguageEvents = false;

        // First-run onboarding overlay.
        if (!_settings.OnboardingCompleted)
        {
            for (int i = 0; i < LocalizationManager.WelcomePhrases.Count; i++)
            {
                if (LocalizationManager.WelcomePhrases[i].Code == LocalizationManager.CurrentCode)
                {
                    _welcomeIndex = i;
                    break;
                }
            }

            OnboardingOverlay.Visibility = Visibility.Visible;
            OnboardingOverlay.Opacity = 0;
            Fade(OnboardingOverlay, 0, 1, TimeSpan.FromMilliseconds(320));
            StartWelcomeCycle();
        }

        // Default page.
        _navReady = true;
        NavOpt.IsChecked = true;
        ShowPage(PageOpt, animate: false);
    }

    // ------------------------------------------------------------------
    // Navigation & page transitions
    // ------------------------------------------------------------------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_navReady || ContentHost == null)
        {
            return;
        }

        if (sender == NavOpt) ShowPage(PageOpt);
        else if (sender == NavSettings) ShowPage(PageSettings);
        else if (sender == NavProtect) ShowPage(PageProtect);
    }

    private void ShowPage(Grid page, bool animate = true)
    {
        if (_currentPage == page)
        {
            return;
        }

        foreach (var candidate in new[] { PageOpt, PageSettings, PageProtect })
        {
            if (candidate != page)
            {
                candidate.Visibility = Visibility.Collapsed;
                candidate.Opacity = 1;
            }
        }

        _currentPage = page;
        page.Visibility = Visibility.Visible;

        if (!animate)
        {
            page.Opacity = 1;
            page.RenderTransform = null;
            return;
        }

        var shift = new TranslateTransform(0, 14);
        page.RenderTransform = shift;
        page.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease };
        page.BeginAnimation(OpacityProperty, fade);
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private static void Fade(UIElement element, double from, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    // ------------------------------------------------------------------
    // First-run welcome cycle: fade in, hold 3 s, fade out, next language
    // ------------------------------------------------------------------

    private void StartWelcomeCycle()
    {
        StopWelcomeCycle();
        _welcomeCts = new CancellationTokenSource();
        _ = CycleWelcomeAsync(_welcomeCts.Token);
    }

    private void StopWelcomeCycle()
    {
        if (_welcomeCts == null)
        {
            return;
        }

        _welcomeCts.Cancel();
        _welcomeCts.Dispose();
        _welcomeCts = null;
    }

    private async Task CycleWelcomeAsync(CancellationToken token)
    {
        var phrases = LocalizationManager.WelcomePhrases;
        if (phrases == null || phrases.Count == 0) return;

        if (!IsLoaded)
        {
            var loadedTcs = new TaskCompletionSource<bool>();
            RoutedEventHandler handler = null!;
            handler = (_, _) =>
            {
                Loaded -= handler;
                loadedTcs.TrySetResult(true);
            };
            Loaded += handler;

            using (token.Register(() => loadedTcs.TrySetCanceled()))
            {
                try
                {
                    await loadedTcs.Task;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        try
        {
            while (!token.IsCancellationRequested && IsLoaded)
            {
                var phrase = phrases[_welcomeIndex % phrases.Count];

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested) return;
                    WelcomeText.Text = phrase.Text;
                    WelcomeLanguage.Text = phrase.NativeName;
                    Fade(WelcomeText, 0, 1, TimeSpan.FromMilliseconds(520));
                    Fade(WelcomeLanguage, 0, 1, TimeSpan.FromMilliseconds(520));
                }));

                await Task.Delay(3000, token);

                if (token.IsCancellationRequested || !IsLoaded) break;

                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested) return;
                    Fade(WelcomeText, 1, 0, TimeSpan.FromMilliseconds(420));
                    Fade(WelcomeLanguage, 1, 0, TimeSpan.FromMilliseconds(420));
                }));

                await Task.Delay(460, token);
                _welcomeIndex++;
            }
        }
        catch (OperationCanceledException)
        {
            // Onboarding finished or language re-selected: leave the loop silently.
        }
    }

    // ------------------------------------------------------------------
    // Onboarding & language
    // ------------------------------------------------------------------

    private void SelectLanguageInCombos(string code)
    {
        AppLanguage? target = null;
        foreach (var lang in LocalizationManager.Languages)
        {
            if (lang.Code == code)
            {
                target = lang;
                break;
            }
        }

        target ??= LocalizationManager.Languages[0];

        _suppressLanguageEvents = true;
        OnboardingLanguageCombo.SelectedItem = target;
        LanguageCombo.SelectedItem = target;
        _suppressLanguageEvents = false;
    }

    private void OnboardingLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || OnboardingLanguageCombo.SelectedItem is not AppLanguage lang)
        {
            return;
        }

        // Live preview of the chosen language on the welcome screen.
        LocalizationManager.ApplyLanguage(lang.Code);
        SelectLanguageInCombos(lang.Code);

        // Jump the welcome cycle to the chosen language and show it immediately.
        for (int i = 0; i < LocalizationManager.WelcomePhrases.Count; i++)
        {
            if (LocalizationManager.WelcomePhrases[i].Code == lang.Code)
            {
                _welcomeIndex = i;
                break;
            }
        }

        StartWelcomeCycle();
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || LanguageCombo.SelectedItem is not AppLanguage lang)
        {
            return;
        }

        LocalizationManager.ApplyLanguage(lang.Code);
        _settings.Language = lang.Code;
        _settings.Save();
        SelectLanguageInCombos(lang.Code);
    }

    private bool _onboardingSubmitting;

    private void OnOnboardingContinue(object sender, RoutedEventArgs e)
    {
        if (_onboardingSubmitting) return;
        _onboardingSubmitting = true;

        StopWelcomeCycle();

        if (OnboardingLanguageCombo.SelectedItem is AppLanguage lang)
        {
            LocalizationManager.ApplyLanguage(lang.Code);
            _settings.Language = lang.Code;
        }

        _settings.OnboardingCompleted = true;
        _settings.Save();

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            OnboardingOverlay.Visibility = Visibility.Collapsed;
            _currentPage = null;
            ShowPage(PageOpt);
        };
        OnboardingOverlay.BeginAnimation(OpacityProperty, fade);
    }

    // ------------------------------------------------------------------
    // Window chrome
    // ------------------------------------------------------------------

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private bool _isClosingCleanedUp;
    private bool _isClosingInProgress;

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        StopWelcomeCycle();

        if (_isClosingCleanedUp)
        {
            return;
        }

        if (_isClosingInProgress)
        {
            e.Cancel = true;
            return;
        }

        if (_viewModel.IsOptimizing)
        {
            // Cancel immediate close and deactivate asynchronously without blocking the UI thread (SF-11, SF-15)
            e.Cancel = true;
            _isClosingInProgress = true;
            IsEnabled = false;

            try
            {
                await _viewModel.ShutdownAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shutdown error: {ex.Message}");
            }
            finally
            {
                _isClosingCleanedUp = true;
                _isClosingInProgress = false;
                Close();
            }
        }
        else
        {
            try
            {
                _viewModel.Shutdown();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Shutdown error: {ex.Message}");
            }
        }
    }
}
