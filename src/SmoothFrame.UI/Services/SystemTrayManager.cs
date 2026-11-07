using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmoothFrame.UI.Services;

public class SystemTrayManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 102;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string lpString);

    private readonly MainWindow _window;
    private readonly Action _onPurgeMemory;
    private IntPtr _hwnd;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isIconCreated;
    private NOTIFYICONDATA _notifyData;
    private HwndSource? _hwndSource;
    private ContextMenu? _contextMenu;
    private int _wmTaskbarCreated;

    public SystemTrayManager(MainWindow window, Action onPurgeMemory)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _onPurgeMemory = onPurgeMemory;

        Initialize();
    }

    private void Initialize()
    {
        var helper = new WindowInteropHelper(_window);
        _hwnd = helper.Handle;

        if (_hwnd == IntPtr.Zero)
        {
            _window.SourceInitialized += OnSourceInitialized;
            return;
        }

        SetupTray();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _window.SourceInitialized -= OnSourceInitialized;
        var helper = new WindowInteropHelper(_window);
        _hwnd = helper.Handle;
        SetupTray();
    }

    private void SetupTray()
    {
        if (_hwnd == IntPtr.Zero) return;

        _wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        _hIcon = GetTrayIconHandle();

        _notifyData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "SmoothFrame v2.1.0"
        };

        _isIconCreated = Shell_NotifyIcon(NIM_ADD, ref _notifyData);

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.Hide();
        }

        _window.StateChanged += OnWindowStateChanged;
        _window.Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.Hide();
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.Hide();
        }
        else if (!_isIconCreated && _hwnd != IntPtr.Zero)
        {
            _isIconCreated = Shell_NotifyIcon(NIM_ADD, ref _notifyData);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_wmTaskbarCreated != 0 && msg == _wmTaskbarCreated)
        {
            _isIconCreated = Shell_NotifyIcon(NIM_ADD, ref _notifyData);
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONDBLCLK)
            {
                RestoreAndFocusWindow();
                handled = true;
            }
            else if (eventId == WM_RBUTTONUP || eventId == 0x007B) // WM_RBUTTONUP or WM_CONTEXTMENU
            {
                ShowContextMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void RestoreAndFocusWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Focus();
        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd);
        }

        // Pulse Topmost to guarantee focus over other desktop applications
        try
        {
            _window.Topmost = true;
            _window.Topmost = false;
        }
        catch { }
    }

    private void ShowContextMenu()
    {
        CreateContextMenu();

        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd);
        }

        if (_contextMenu != null)
        {
            if (_contextMenu.IsOpen)
            {
                _contextMenu.IsOpen = false;
            }
            _contextMenu.Placement = PlacementMode.MousePoint;
            _contextMenu.IsOpen = true;
            _contextMenu.Focus();

            if (_hwnd != IntPtr.Zero)
            {
                PostMessage(_hwnd, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    private void CreateContextMenu()
    {
        _contextMenu = new ContextMenu();

        try
        {
            if (_window.TryFindResource("BrushBgCard") is Brush bg)
                _contextMenu.Background = bg;
            if (_window.TryFindResource("BrushBorderStrong") is Brush border)
                _contextMenu.BorderBrush = border;
            _contextMenu.BorderThickness = new Thickness(1);
        }
        catch { }

        // 1. "Открыть SmoothFrame"
        var openItem = new MenuItem
        {
            Header = Localization.LocalizationManager.T("Tray.Open"),
            FontWeight = FontWeights.SemiBold
        };
        ApplyItemTheme(openItem);
        openItem.Click += (s, e) => RestoreAndFocusWindow();

        // 2. "Активировать / Отключить оптимизацию"
        var toggleItem = new MenuItem
        {
            Header = _window.DataContext is ViewModels.MainViewModel vm ? vm.ActionButtonText : "Переключить режим буста"
        };
        ApplyItemTheme(toggleItem);
        toggleItem.Click += (s, e) =>
        {
            if (_window.DataContext is ViewModels.MainViewModel vm)
            {
                vm.ToggleOptimizationCommand.Execute(null);
            }
        };

        // 3. "Очистить память"
        var purgeItem = new MenuItem
        {
            Header = Localization.LocalizationManager.T("Tray.PurgeMem")
        };
        ApplyItemTheme(purgeItem);
        purgeItem.Click += (s, e) => _onPurgeMemory?.Invoke();

        // 4. Separator
        var sep = new Separator();
        try
        {
            if (_window.TryFindResource("BrushBorder") is Brush sepBg)
                sep.Background = sepBg;
        }
        catch { }

        // 5. "Выход"
        var exitItem = new MenuItem
        {
            Header = Localization.LocalizationManager.T("Tray.Exit")
        };
        ApplyItemTheme(exitItem, isDanger: true);
        exitItem.Click += (s, e) => _window.ExitApplication();

        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(toggleItem);
        _contextMenu.Items.Add(purgeItem);
        _contextMenu.Items.Add(sep);
        _contextMenu.Items.Add(exitItem);
    }

    private void ApplyItemTheme(MenuItem item, bool isDanger = false)
    {
        try
        {
            if (isDanger && _window.TryFindResource("BrushDanger") is Brush dangerBrush)
            {
                item.Foreground = dangerBrush;
            }
            else if (_window.TryFindResource("BrushTextPrimary") is Brush textBrush)
            {
                item.Foreground = textBrush;
            }
            item.Padding = new Thickness(12, 6, 12, 6);
        }
        catch { }
    }

    private static IntPtr GetTrayIconHandle()
    {
        try
        {
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
            {
                if (ExtractIconEx(procPath, 0, out IntPtr hLarge, out IntPtr hSmall, 1) > 0)
                {
                    if (hLarge != IntPtr.Zero)
                    {
                        try { DestroyIcon(hLarge); } catch { }
                    }
                    if (hSmall != IntPtr.Zero)
                    {
                        return hSmall;
                    }
                }
            }
        }
        catch { }

        try
        {
            string icoPath = Path.Combine(AppContext.BaseDirectory, "SmoothFrame.ico");
            if (File.Exists(icoPath))
            {
                if (ExtractIconEx(icoPath, 0, out IntPtr hLarge, out IntPtr hSmall, 1) > 0)
                {
                    if (hLarge != IntPtr.Zero)
                    {
                        try { DestroyIcon(hLarge); } catch { }
                    }
                    if (hSmall != IntPtr.Zero)
                    {
                        return hSmall;
                    }
                }
            }
        }
        catch { }

        return LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
    }

    public void Dispose()
    {
        try
        {
            _window.SourceInitialized -= OnSourceInitialized;
            _window.StateChanged -= OnWindowStateChanged;
            _window.Loaded -= OnWindowLoaded;
        }
        catch { }

        if (_isIconCreated)
        {
            _notifyData.uFlags = 0;
            Shell_NotifyIcon(NIM_DELETE, ref _notifyData);
            _isIconCreated = false;
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { }
            _hIcon = IntPtr.Zero;
        }
    }
}
