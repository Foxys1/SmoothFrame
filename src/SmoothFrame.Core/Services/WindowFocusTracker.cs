using System;
using System.Diagnostics;
using System.Threading;
using SmoothFrame.Core.Native;

namespace SmoothFrame.Core.Services;

/// <summary>
/// Service for smart window focus tracking and Alt+Tab detection via Win32 SetWinEventHook.
/// R2: 100% zero DLL injection (WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS on EVENT_SYSTEM_FOREGROUND).
/// - Dedicated STA background thread with Win32 message pump (GetMessage / TranslateMessage / DispatchMessage).
/// - Pin delegate to avoid GC collection.
/// - Exposes ForegroundWindowChanged (PID, isGameForeground) and IsGameForeground state.
/// - Clean teardown via PostThreadMessage(WM_QUIT), UnhookWinEvent, and thread join.
/// </summary>
public class WindowFocusTracker : IDisposable
{
    private readonly object _lock = new();
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle = IntPtr.Zero;
    private NativeMethods.WinEventProc? _hookDelegate;
    private readonly ManualResetEventSlim _readyEvent = new(false);
    private int _disposed;
    private uint _trackedGameProcessId;
    private uint _lastForegroundPid;

    public event Action<uint, bool>? ForegroundWindowChanged;

    public bool IsRunning { get; private set; }
    public bool IsGameForeground { get; private set; }

    public uint TrackedGameProcessId
    {
        get => _trackedGameProcessId;
        set
        {
            lock (_lock)
            {
                _trackedGameProcessId = value;
                if (_lastForegroundPid != 0)
                {
                    bool isGame = (value != 0 && _lastForegroundPid == value);
                    if (isGame != IsGameForeground)
                    {
                        IsGameForeground = isGame;
                        ForegroundWindowChanged?.Invoke(_lastForegroundPid, isGame);
                    }
                }
            }
        }
    }

    public WindowFocusTracker(uint trackedGameProcessId = 0)
    {
        _trackedGameProcessId = trackedGameProcessId;
    }

    public void Start(uint trackedGameProcessId = 0)
    {
        lock (_lock)
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(WindowFocusTracker));

            if (trackedGameProcessId != 0)
            {
                _trackedGameProcessId = trackedGameProcessId;
            }

            if (IsRunning)
                return;

            _readyEvent.Reset();

            _hookThread = new Thread(HookThreadWorker)
            {
                Name = "SmoothFrame_WindowFocusHook",
                IsBackground = true
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();

            // Wait for hook to be established
            _readyEvent.Wait(TimeSpan.FromSeconds(2));
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning)
                return;

            IsRunning = false;

            if (_hookThreadId != 0)
            {
                NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            if (_hookThread != null)
            {
                if (!_hookThread.Join(TimeSpan.FromSeconds(2)))
                {
                    Debug.WriteLine("[WindowFocusTracker] Hook thread join timed out.");
                }
                _hookThread = null;
            }

            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            _hookThreadId = 0;
            _hookDelegate = null;
            IsGameForeground = false;
            _lastForegroundPid = 0;
        }
    }

    private void HookThreadWorker()
    {
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();

            // Retain reference in instance field to guarantee it is not GC-collected
            _hookDelegate = OnWinEventForeground;

            _hookHandle = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _hookDelegate,
                0,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            if (_hookHandle == IntPtr.Zero)
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                Debug.WriteLine($"[WindowFocusTracker] SetWinEventHook failed, Win32 error: {err}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowFocusTracker] Initialization exception: {ex.Message}");
        }
        finally
        {
            _readyEvent.Set();
        }

        if (_hookHandle != IntPtr.Zero)
        {
            try
            {
                // Standard Win32 message pump for hook callback dispatch
                while (NativeMethods.GetMessage(out NativeMethods.MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    NativeMethods.TranslateMessage(ref msg);
                    NativeMethods.DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WindowFocusTracker] Message pump exception: {ex.Message}");
            }
            finally
            {
                if (_hookHandle != IntPtr.Zero)
                {
                    NativeMethods.UnhookWinEvent(_hookHandle);
                    _hookHandle = IntPtr.Zero;
                }
            }
        }
    }

    private void OnWinEventForeground(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND || hwnd == IntPtr.Zero)
            return;

        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint fgPid);
            if (fgPid == 0) return;

            lock (_lock)
            {
                _lastForegroundPid = fgPid;
                uint gamePid = _trackedGameProcessId;
                bool isGame = (gamePid != 0 && fgPid == gamePid);

                if (isGame != IsGameForeground || _lastForegroundPid == fgPid)
                {
                    IsGameForeground = isGame;
                }

                ForegroundWindowChanged?.Invoke(fgPid, isGame);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WindowFocusTracker] Event handler exception: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Stop();
        _readyEvent.Dispose();
        GC.SuppressFinalize(this);
    }
}
