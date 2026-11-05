using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using SmoothFrame.UI.Localization;
using SmoothFrame.UI.Models;
using SmoothFrame.UI.Services;
using SmoothFrame.UI.ViewModels;

namespace SmoothFrame.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly UiSettings _settings;
    private readonly SystemTrayManager _trayManager;
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

        _trayManager = new SystemTrayManager(this, () => _viewModel.PurgeStandbyMemory());
        Closed += (_, _) =>
        {
            if (_hotkeyRegistered && _hwndSource != null)
            {
                try { UnregisterHotKey(_hwndSource.Handle, HOTKEY_BOOST_ID); } catch { }
            }
            _hwndSource?.RemoveHook(HwndHook);
            _trayManager.Dispose();
            _viewModel.Dispose();
        };

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
            _welcomeIndex = 0; // Always start with English "Welcome", followed by "Добро пожаловать" and world languages

            OnboardingOverlay.Visibility = Visibility.Visible;
            OnboardingOverlay.Opacity = 0;
            Fade(OnboardingOverlay, 0, 1, TimeSpan.FromMilliseconds(320));
            StartWelcomeCycle();
        }

        // Default page.
        _navReady = true;
        NavOpt.IsChecked = true;
        ShowPage(PageOpt, animate: false);

        ApplyWindowStateAdjustments();
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
        else if (sender == NavMinecraft) ShowPage(PageMinecraft);
        else if (sender == NavSettings) ShowPage(PageSettings);
        else if (sender == NavProtect) ShowPage(PageProtect);
    }

    private void ShowPage(Grid page, bool animate = true)
    {
        if (_currentPage == page)
        {
            return;
        }

        var oldPage = _currentPage;
        _currentPage = page;

        if (!animate || oldPage == null)
        {
            foreach (var candidate in new[] { PageOpt, PageMinecraft, PageSettings, PageProtect })
            {
                if (candidate != page)
                {
                    candidate.Visibility = Visibility.Collapsed;
                    candidate.Opacity = 1;
                    candidate.RenderTransform = null;
                }
            }

            page.Visibility = Visibility.Visible;
            page.Opacity = 1;
            page.RenderTransform = null;
            return;
        }

        // Clean up any other non-active candidates immediately
        foreach (var candidate in new[] { PageOpt, PageMinecraft, PageSettings, PageProtect })
        {
            if (candidate != page && candidate != oldPage)
            {
                candidate.Visibility = Visibility.Collapsed;
                candidate.Opacity = 1;
                candidate.RenderTransform = null;
            }
        }

        // Incoming page enters with smooth scale + slide + fade
        page.Visibility = Visibility.Visible;
        page.Opacity = 0;

        var enterTransformGroup = new TransformGroup();
        var enterTranslate = new TranslateTransform(0, 16);
        var enterScale = new ScaleTransform(0.985, 0.985);
        enterTransformGroup.Children.Add(enterScale);
        enterTransformGroup.Children.Add(enterTranslate);
        page.RenderTransformOrigin = new Point(0.5, 0.15);
        page.RenderTransform = enterTransformGroup;

        var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };
        var enterFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeOut };
        var enterSlide = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeOut };
        var enterScaleAnim = new DoubleAnimation(0.985, 1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = easeOut };

        page.BeginAnimation(OpacityProperty, enterFade);
        enterTranslate.BeginAnimation(TranslateTransform.YProperty, enterSlide);
        enterScale.BeginAnimation(ScaleTransform.ScaleXProperty, enterScaleAnim);
        enterScale.BeginAnimation(ScaleTransform.ScaleYProperty, enterScaleAnim);

        // Outgoing page fades out gracefully
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var exitFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130)) { EasingFunction = easeIn };
        exitFade.Completed += (_, _) =>
        {
            if (_currentPage != oldPage)
            {
                oldPage.Visibility = Visibility.Collapsed;
                oldPage.Opacity = 1;
                oldPage.RenderTransform = null;
            }
        };
        oldPage.BeginAnimation(OpacityProperty, exitFade);
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

            try
            {
                using (token.Register(() => loadedTcs.TrySetCanceled()))
                {
                    await loadedTcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                Loaded -= handler;
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
    // Window chrome & Win32 maximization hook
    // ------------------------------------------------------------------

    private const int HOTKEY_BOOST_ID = 9001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_B = 0x42;
    private const int WM_HOTKEY = 0x0312;
    private bool _hotkeyRegistered;
    private bool _isExplicitExit;
    private HwndSource? _hwndSource;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(HwndHook);

        try
        {
            _hotkeyRegistered = RegisterHotKey(handle, HOTKEY_BOOST_ID, MOD_CONTROL | MOD_SHIFT, VK_B);
        }
        catch { }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        ApplyWindowStateAdjustments();
    }

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
        if (_viewModel.MinimizeToTrayOnClose)
        {
            Hide();
        }
        else
        {
            ExitApplication();
        }
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        Close();
    }

    private void UpdateMaximizedBounds()
    {
        if (WindowState != WindowState.Maximized)
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;
            return;
        }

        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (GetMonitorInfo(monitor, ref monitorInfo))
                    {
                        var source = PresentationSource.FromVisual(this);
                        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                        if (dpiX <= 0) dpiX = 1.0;
                        if (dpiY <= 0) dpiY = 1.0;

                        double workWidth = (monitorInfo.rcWork.right - monitorInfo.rcWork.left) / dpiX;
                        double workHeight = (monitorInfo.rcWork.bottom - monitorInfo.rcWork.top) / dpiY;

                        MaxHeight = workHeight;
                        MaxWidth = workWidth;
                        return;
                    }
                }
            }
        }
        catch { }

        MaxHeight = SystemParameters.WorkArea.Height;
        MaxWidth = SystemParameters.WorkArea.Width;
    }

    private void ApplyWindowStateAdjustments()
    {
        if (RootBorder == null || TitleBarBorder == null || FooterBorder == null || MaximizeIcon == null || BtnMaximize == null)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            UpdateMaximizedBounds();

            RootBorder.CornerRadius = new CornerRadius(0);
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.Margin = new Thickness(0);
            RootBorder.Effect = null;
            TitleBarBorder.CornerRadius = new CornerRadius(0);
            FooterBorder.CornerRadius = new CornerRadius(0);

            MaximizeIcon.Data = (Geometry)FindResource("Icon.RestoreWindow");
            BtnMaximize.ToolTip = "Восстановить";
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            MaxWidth = double.PositiveInfinity;

            RootBorder.CornerRadius = new CornerRadius(16);
            RootBorder.BorderThickness = new Thickness(1.5);
            RootBorder.Margin = new Thickness(0);
            RootBorder.Effect = (Effect)FindResource("WindowDropShadow");
            TitleBarBorder.CornerRadius = new CornerRadius(15, 15, 0, 0);
            FooterBorder.CornerRadius = new CornerRadius(0, 0, 15, 15);

            MaximizeIcon.Data = (Geometry)FindResource("Icon.Max");
            BtnMaximize.ToolTip = "Развернуть";
        }
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_DPICHANGED = 0x02E0;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;

        public POINT(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            WmGetMinMaxInfo(hwnd, lParam);
            handled = true;
        }
        else if (msg == WM_WINDOWPOSCHANGED && WindowState == WindowState.Maximized)
        {
            UpdateMaximizedBounds();
        }
        else if (msg == WM_DPICHANGED && WindowState == WindowState.Maximized)
        {
            UpdateMaximizedBounds();
        }
        else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_BOOST_ID)
        {
            if (_viewModel.GlobalHotkeyEnabled)
            {
                _viewModel.ToggleOptimizationCommand.Execute(null);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var rcWork = monitorInfo.rcWork;
                var rcMonitor = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.x = rcWork.left - rcMonitor.left;
                mmi.ptMaxPosition.y = rcWork.top - rcMonitor.top;
                mmi.ptMaxSize.x = rcWork.right - rcWork.left;
                mmi.ptMaxSize.y = rcWork.bottom - rcWork.top;
                mmi.ptMaxTrackSize.x = mmi.ptMaxSize.x;
                mmi.ptMaxTrackSize.y = mmi.ptMaxSize.y;
            }
        }

        Marshal.StructureToPtr(mmi, lParam, false);
    }

    private bool _isClosingCleanedUp;
    private bool _isClosingInProgress;

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        StopWelcomeCycle();

        if (!_isExplicitExit && _viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        try
        {
            if (_hotkeyRegistered)
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    UnregisterHotKey(handle, HOTKEY_BOOST_ID);
                    _hotkeyRegistered = false;
                }
            }
        }
        catch { }

        if (_isClosingCleanedUp)
        {
            return;
        }

        if (_isClosingInProgress)
        {
            e.Cancel = true;
            return;
        }

        if (_viewModel.IsOptimizing || _viewModel.IsMinecraftOptimizing)
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
