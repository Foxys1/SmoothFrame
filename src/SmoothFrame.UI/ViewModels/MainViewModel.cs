using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SmoothFrame.Core.Models;
using SmoothFrame.Core.Services;
using SmoothFrame.UI.Common;
using SmoothFrame.UI.Models;
using Loc = SmoothFrame.UI.Localization.LocalizationManager;

namespace SmoothFrame.UI.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly OptimizationCoordinator _coordinator;
    private readonly GameDiscoveryService _discoveryService;
    private readonly StartupService _startupService;
    private readonly UiSettings _uiSettings;
    private readonly SemaphoreSlim _autostartGate = new(1, 1);
    private readonly Action<bool> _onStateChanged;
    private readonly Action<string> _onLogNotification;

    private GameInfo? _selectedGame;
    private bool _isOptimizing;
    private string _statusMessage = string.Empty;
    private string _subStatusMessage = string.Empty;
    private string _timerText = string.Empty;
    private string _powerPlanText = string.Empty;
    private string _priorityText = string.Empty;
    private bool _autostartEnabled;
    private bool _isolatePCores;
    private bool _optimizeNetwork = true;
    private bool _optimizeIoPriority = true;
    private bool _optimizeAudioDg = true;
    private bool _highPrecisionTimer = true;
    private bool _highPerformancePlan = true;
    private bool _deprioritizeBackground = true;
    private bool _minimizeToTrayOnClose = true;
    private bool _promptOnClose = true;
    private bool _autoBoostOnGameLaunch = true;
    private bool _globalHotkeyEnabled = true;
    private bool _isToggling;
    private bool _isLoadingGames;
    private bool _isMutatingCustomGame;
    private bool _disposed;
    private readonly object _disposeLock = new();

    private CancellationTokenSource? _autoWatcherCts;
    private bool _wasAutoStarted;
    private int _autoStartedPid;

    // SmoothFrame NeoForge 1.21.1 Mod Management & Telemetry Receiver
    private readonly MinecraftTelemetryReceiver? _telemetryReceiver;
    private bool _isSmoothFrameModInstalled;
    private bool _isModActiveInGame;
    private int _inGameFps;
    private int _inGameOnePercentLow;
    private double _inGameFrametimeMs;
    private long _inGameMemoryUsedMb;
    private long _inGameMemoryMaxMb;
    private int _inGameEntitiesCulled;
    private int _inGameChunksPending;
    private string _inGameMcVersion = "1.21.1";
    private DateTime _lastMemoryTrimTime = DateTime.MinValue;
    private readonly TimeSpan _memoryTrimCooldown = TimeSpan.FromSeconds(30);

    public ObservableCollection<GameInfo> Games { get; } = new();

    public GameInfo? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (_selectedGame != value)
            {
                _selectedGame = value;
                OnPropertyChanged();
                UpdateStatus();
            }
        }
    }

    public bool IsOptimizing
    {
        get => _isOptimizing;
        set
        {
            if (_isOptimizing != value)
            {
                _isOptimizing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public string ActionButtonText => Loc.T(IsOptimizing ? "Btn.Deactivate" : "Btn.Activate");

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string SubStatusMessage
    {
        get => _subStatusMessage;
        set { _subStatusMessage = value; OnPropertyChanged(); }
    }

    public string TimerText
    {
        get => _timerText;
        set { _timerText = value; OnPropertyChanged(); }
    }

    public string PowerPlanText
    {
        get => _powerPlanText;
        set { _powerPlanText = value; OnPropertyChanged(); }
    }

    public string PriorityText
    {
        get => _priorityText;
        set { _priorityText = value; OnPropertyChanged(); }
    }

    public bool AutostartEnabled
    {
        get => _autostartEnabled;
        set
        {
            if (_autostartEnabled != value)
            {
                _autostartEnabled = value;
                OnPropertyChanged();

                // Run SetAutostart on background thread with serialization gate (D-14, SF-09)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _autostartGate.WaitAsync();
                        try
                        {
                            bool success = _startupService.SetAutostart(value);
                            if (!success)
                            {
                                // If toggle failed, revert property on UI thread
                                if (Application.Current?.Dispatcher != null)
                                {
                                    _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        _autostartEnabled = !value;
                                        OnPropertyChanged(nameof(AutostartEnabled));
                                    }));
                                }
                                else
                                {
                                    _autostartEnabled = !value;
                                    OnPropertyChanged(nameof(AutostartEnabled));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Autostart error: {ex.Message}");
                            if (Application.Current?.Dispatcher != null)
                            {
                                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _autostartEnabled = !value;
                                    OnPropertyChanged(nameof(AutostartEnabled));
                                }));
                            }
                            else
                            {
                                _autostartEnabled = !value;
                                OnPropertyChanged(nameof(AutostartEnabled));
                            }
                        }
                        finally
                        {
                            try { _autostartGate.Release(); } catch (ObjectDisposedException) { }
                        }
                    }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Autostart task error: {ex.Message}");
                    }
                });
            }
        }
    }

    public bool IsolatePCores
    {
        get => _isolatePCores;
        set
        {
            if (_isolatePCores != value)
            {
                _isolatePCores = value;
                OnPropertyChanged();
                _uiSettings.IsolatePCores = value;
                _uiSettings.Save();
            }
        }
    }

    public bool OptimizeNetwork
    {
        get => _optimizeNetwork;
        set
        {
            if (_optimizeNetwork != value)
            {
                _optimizeNetwork = value;
                OnPropertyChanged();
                _uiSettings.OptimizeNetwork = value;
                _uiSettings.Save();
            }
        }
    }

    public bool OptimizeIoPriority
    {
        get => _optimizeIoPriority;
        set
        {
            if (_optimizeIoPriority != value)
            {
                _optimizeIoPriority = value;
                OnPropertyChanged();
                _uiSettings.OptimizeIoPriority = value;
                _uiSettings.Save();
            }
        }
    }

    public bool OptimizeAudioDg
    {
        get => _optimizeAudioDg;
        set
        {
            if (_optimizeAudioDg != value)
            {
                _optimizeAudioDg = value;
                OnPropertyChanged();
                _uiSettings.OptimizeAudioDg = value;
                _uiSettings.Save();
            }
        }
    }

    public bool HighPrecisionTimer
    {
        get => _highPrecisionTimer;
        set
        {
            if (_highPrecisionTimer != value)
            {
                _highPrecisionTimer = value;
                OnPropertyChanged();
                _uiSettings.HighPrecisionTimer = value;
                _uiSettings.Save();
            }
        }
    }

    public bool HighPerformancePlan
    {
        get => _highPerformancePlan;
        set
        {
            if (_highPerformancePlan != value)
            {
                _highPerformancePlan = value;
                OnPropertyChanged();
                _uiSettings.HighPerformancePlan = value;
                _uiSettings.Save();
            }
        }
    }

    public bool DeprioritizeBackground
    {
        get => _deprioritizeBackground;
        set
        {
            if (_deprioritizeBackground != value)
            {
                _deprioritizeBackground = value;
                OnPropertyChanged();
                _uiSettings.DeprioritizeBackground = value;
                _uiSettings.Save();
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (_minimizeToTrayOnClose != value)
            {
                _minimizeToTrayOnClose = value;
                OnPropertyChanged();
                _uiSettings.MinimizeToTrayOnClose = value;
                _uiSettings.Save();
            }
        }
    }

    public bool PromptOnClose
    {
        get => _promptOnClose;
        set
        {
            if (_promptOnClose != value)
            {
                _promptOnClose = value;
                OnPropertyChanged();
                _uiSettings.PromptOnClose = value;
                _uiSettings.Save();
            }
        }
    }

    public bool AutoBoostOnGameLaunch
    {
        get => _autoBoostOnGameLaunch;
        set
        {
            if (_autoBoostOnGameLaunch != value)
            {
                _autoBoostOnGameLaunch = value;
                OnPropertyChanged();
                _uiSettings.AutoBoostOnGameLaunch = value;
                _uiSettings.Save();
            }
        }
    }

    public bool GlobalHotkeyEnabled
    {
        get => _globalHotkeyEnabled;
        set
        {
            if (_globalHotkeyEnabled != value)
            {
                _globalHotkeyEnabled = value;
                OnPropertyChanged();
                _uiSettings.GlobalHotkeyEnabled = value;
                _uiSettings.Save();
            }
        }
    }

    public ICommand ToggleOptimizationCommand { get; }
    public ICommand ToggleMinecraftOptimizationCommand { get; }
    public ICommand RollbackMinecraftCommand { get; }
    public ICommand AddCustomGameCommand { get; }
    public ICommand RemoveCustomGameCommand { get; }
    public ICommand RefreshGamesCommand { get; }
    public ICommand RefreshMinecraftCommand { get; }
    public ICommand ApplyMinecraftCommand { get; }
    public ICommand ForceGpuCommand { get; }
    public ICommand PurgeStandbyMemoryCommand { get; }
    public ICommand ClearShaderCacheCommand { get; }
    public ICommand CopyJvmArgsCommand { get; }
    public ICommand InstallSmoothFrameModCommand { get; }
    public ICommand UninstallSmoothFrameModCommand { get; }

    public MainViewModel(
        OptimizationCoordinator? coordinator = null,
        GameDiscoveryService? discoveryService = null,
        StartupService? startupService = null)
    {
        _coordinator = coordinator ?? new OptimizationCoordinator();
        _discoveryService = discoveryService ?? new GameDiscoveryService();
        _startupService = startupService ?? new StartupService();
        _uiSettings = UiSettings.Load();

        _highPrecisionTimer = _uiSettings.HighPrecisionTimer;
        _highPerformancePlan = _uiSettings.HighPerformancePlan;
        _deprioritizeBackground = _uiSettings.DeprioritizeBackground;
        _isolatePCores = _uiSettings.IsolatePCores;
        _optimizeNetwork = _uiSettings.OptimizeNetwork;
        _optimizeIoPriority = _uiSettings.OptimizeIoPriority;
        _optimizeAudioDg = _uiSettings.OptimizeAudioDg;
        _minimizeToTrayOnClose = _uiSettings.MinimizeToTrayOnClose;
        _promptOnClose = _uiSettings.PromptOnClose;
        _autoBoostOnGameLaunch = _uiSettings.AutoBoostOnGameLaunch;
        _globalHotkeyEnabled = _uiSettings.GlobalHotkeyEnabled;

        // Check autostart asynchronously on background thread to avoid blocking the UI thread (SF-09)
        Task.Run(() =>
        {
            try
            {
                var enabled = _startupService.IsAutostartEnabled();
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _autostartEnabled = enabled;
                        OnPropertyChanged(nameof(AutostartEnabled));
                    }));
                }
                else
                {
                    _autostartEnabled = enabled;
                    OnPropertyChanged(nameof(AutostartEnabled));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking autostart: {ex.Message}");
            }
        });

        ToggleOptimizationCommand = new RelayCommand(async () => await ToggleOptimizationAsync());
        ToggleMinecraftOptimizationCommand = new RelayCommand(async () => await ToggleMinecraftOptimizationAsync());
        RollbackMinecraftCommand = new RelayCommand(RollbackMinecraftSettings);
        AddCustomGameCommand = new RelayCommand(AddCustomGame);
        RemoveCustomGameCommand = new RelayCommand(param => RemoveCustomGame(param as GameInfo));
        RefreshGamesCommand = new RelayCommand(async () => await LoadGamesAsync());
        RefreshMinecraftCommand = new RelayCommand(RefreshMinecraft);
        ApplyMinecraftCommand = new RelayCommand(ApplyMinecraftToLauncher);
        ForceGpuCommand = new RelayCommand(ForceDiscreteGpuForMinecraft);
        PurgeStandbyMemoryCommand = new RelayCommand(PurgeStandbyMemory);
        ClearShaderCacheCommand = new RelayCommand(ClearShaderCache);
        CopyJvmArgsCommand = new RelayCommand(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(GeneratedJvmArgs))
                {
                    Clipboard.SetText(GeneratedJvmArgs);
                    MinecraftStatusMessage = "Аргументы JVM скопированы в буфер обмена!";
                }
            }
            catch { }
        });
        InstallSmoothFrameModCommand = new RelayCommand(InstallSmoothFrameMod);
        UninstallSmoothFrameModCommand = new RelayCommand(UninstallSmoothFrameMod);

        try
        {
            _telemetryReceiver = new MinecraftTelemetryReceiver(autoStart: true);
            _telemetryReceiver.OnTelemetryReceived += HandleTelemetryReceived;
            _telemetryReceiver.OnConnectionStateChanged += HandleTelemetryConnectionChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start telemetry receiver: {ex.Message}");
        }

        RefreshMinecraft();

        _onStateChanged = active =>
        {
            Action update = () =>
            {
                var curGame = _coordinator.CurrentOptimizedGame;
                bool isMinecraft = curGame != null && (
                    curGame.ProcessName.Equals("javaw", StringComparison.OrdinalIgnoreCase) ||
                    curGame.ProcessName.Equals("java", StringComparison.OrdinalIgnoreCase) ||
                    curGame.Name.Equals("Minecraft", StringComparison.OrdinalIgnoreCase));

                if (active)
                {
                    if (isMinecraft)
                    {
                        IsMinecraftOptimizing = true;
                    }
                    else
                    {
                        IsOptimizing = true;
                    }
                }
                else
                {
                    if (isMinecraft)
                    {
                        IsMinecraftOptimizing = false;
                    }
                    else if (curGame != null)
                    {
                        IsOptimizing = false;
                    }
                    else
                    {
                        if (IsMinecraftOptimizing) IsMinecraftOptimizing = false;
                        if (IsOptimizing) IsOptimizing = false;
                    }
                }

                UpdateStatus();
                UpdateTelemetry();
            };

            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(update);
            }
            else
            {
                update();
            }
        };

        _onLogNotification = message =>
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    SubStatusMessage = message;
                }));
            }
            else
            {
                SubStatusMessage = message;
            }
        };

        _coordinator.StateChanged += _onStateChanged;
        _coordinator.LogNotification += _onLogNotification;

        Loc.LanguageChanged += OnLanguageChanged;
        RefreshLocalizedStrings();

        _ = LoadGamesAsync();
        UpdateTelemetry();
        StartAutoWatcher();
    }

    private void StartAutoWatcher()
    {
        _autoWatcherCts = new CancellationTokenSource();
        _ = WatchRunningGamesLoopAsync(_autoWatcherCts.Token);
    }

    private async Task WatchRunningGamesLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_disposed)
        {
            try
            {
                await Task.Delay(2500, token);
                if (token.IsCancellationRequested || _disposed) break;

                if (!AutoBoostOnGameLaunch) continue;

                if (!IsOptimizing)
                {
                    var runningGame = FindRunningGame();
                    if (runningGame != null)
                    {
                        if (Application.Current?.Dispatcher != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(async () =>
                            {
                                if (!IsOptimizing && AutoBoostOnGameLaunch)
                                {
                                    SelectedGame = runningGame;
                                    _wasAutoStarted = true;
                                    _autoStartedPid = GetRunningPid(runningGame);
                                    await ToggleOptimizationAsync(isAutomated: true);
                                }
                            }).Task.Unwrap();
                        }
                    }
                }
                else if (_wasAutoStarted && _autoStartedPid > 0)
                {
                    bool stillRunning = false;
                    try
                    {
                        using var proc = Process.GetProcessById(_autoStartedPid);
                        if (!proc.HasExited) stillRunning = true;
                    }
                    catch { }

                    if (!stillRunning)
                    {
                        if (Application.Current?.Dispatcher != null)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(async () =>
                            {
                                if (IsOptimizing && _wasAutoStarted)
                                {
                                    _wasAutoStarted = false;
                                    _autoStartedPid = 0;
                                    await ToggleOptimizationAsync(isAutomated: true);
                                }
                            }).Task.Unwrap();
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AutoWatcher loop error: {ex.Message}");
            }
        }
    }

    private GameInfo? FindRunningGame()
    {
        Process[]? processes = null;
        try
        {
            processes = Process.GetProcesses();
            var processNames = new HashSet<string>(processes.Select(p => p.ProcessName), StringComparer.OrdinalIgnoreCase);

            var gamesSnapshot = Games.ToArray();
            foreach (var game in gamesSnapshot)
            {
                string targetProc = Path.GetFileNameWithoutExtension(game.ExecutablePath ?? game.Name);
                if (!string.IsNullOrEmpty(targetProc) && processNames.Contains(targetProc))
                {
                    return game;
                }
            }
        }
        catch { }
        finally
        {
            if (processes != null)
            {
                foreach (var p in processes)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
        return null;
    }

    private static int GetRunningPid(GameInfo game)
    {
        Process[]? procs = null;
        try
        {
            string targetProc = Path.GetFileNameWithoutExtension(game.ExecutablePath ?? game.Name);
            procs = Process.GetProcessesByName(targetProc);
            var proc = procs.FirstOrDefault();
            return proc?.Id ?? 0;
        }
        catch { return 0; }
        finally
        {
            if (procs != null)
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }
    }

    private void OnLanguageChanged()
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(RefreshLocalizedStrings));
        }
        else
        {
            RefreshLocalizedStrings();
        }
    }

    private void RefreshLocalizedStrings()
    {
        UpdateStatus();
        UpdateTelemetry();
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(MinecraftActionButtonText));
        OnPropertyChanged(nameof(MinecraftHeroBadgeText));
        OnPropertyChanged(nameof(MinecraftHeroStatusTitle));
        OnPropertyChanged(nameof(MinecraftHeroStatusSubtitle));
        OnPropertyChanged(nameof(MinecraftRamGbText));
        OnPropertyChanged(nameof(SmoothFrameModStatusText));
        OnPropertyChanged(nameof(InGameStatusTitle));
        OnPropertyChanged(nameof(InGameStatusBadgeText));
    }

    public void LoadGames()
    {
        _ = LoadGamesAsync();
    }

    public async Task LoadGamesAsync()
    {
        if (_isLoadingGames) return;
        _isLoadingGames = true;

        try
        {
            // Discover games and extract icons on background thread to keep UI thread fluid (SF-09)
            var list = await Task.Run(() => _discoveryService.GetAllGames());

            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                await Application.Current.Dispatcher.InvokeAsync(() => ApplyLoadedGames(list));
            }
            else
            {
                ApplyLoadedGames(list);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading games: {ex.Message}");
        }
        finally
        {
            _isLoadingGames = false;
        }
    }

    private void ApplyLoadedGames(List<GameInfo> list)
    {
        Games.Clear();
        foreach (var game in list)
        {
            Games.Add(game);
        }

        if (SelectedGame == null && Games.Count > 0)
        {
            SelectedGame = Games[0];
        }
        else if (SelectedGame != null)
        {
            var match = Games.FirstOrDefault(g => g.Id == SelectedGame.Id || (g.ExecutablePath != null && SelectedGame.ExecutablePath != null && g.ExecutablePath.Equals(SelectedGame.ExecutablePath, StringComparison.OrdinalIgnoreCase)));
            if (match != null)
            {
                SelectedGame = match;
            }
            else if (Games.Count > 0)
            {
                SelectedGame = Games[0];
            }
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (IsOptimizing)
        {
            StatusMessage = Loc.Format("VM.Status.Active", SelectedGame?.Name ?? "SmoothFrame");
            SubStatusMessage = Loc.T("VM.Sub.Active");
        }
        else
        {
            StatusMessage = SelectedGame != null
                ? Loc.Format("VM.Status.Selected", SelectedGame.Name)
                : Loc.T("VM.Status.SelectGame");
            SubStatusMessage = Loc.T("VM.Sub.Idle");
        }
    }

    private void UpdateTelemetry()
    {
        if (IsOptimizing || IsMinecraftOptimizing)
        {
            TimerText = $"{_coordinator.CurrentTimerMs:F2} {Loc.T("VM.Timer.ActiveSuffix")}";
            PowerPlanText = Loc.T("VM.Power.Active");
            PriorityText = Loc.T("VM.Prio.Active");
        }
        else
        {
            TimerText = $"{_coordinator.CurrentTimerMs:F2} {Loc.T("VM.Timer.StdSuffix")}";
            PowerPlanText = Loc.T("VM.Power.Std");
            PriorityText = Loc.T("VM.Prio.Std");
        }
    }

    public async Task ToggleOptimizationAsync(bool isAutomated = false)
    {
        if (_isToggling) return;
        _isToggling = true;

        try
        {
            if (IsOptimizing)
            {
                await _coordinator.DeactivateAsync();
                UpdateStatus();
                UpdateTelemetry();
            }
            else
            {
                if (SelectedGame == null)
                {
                    if (!isAutomated)
                    {
                        MessageBox.Show(
                            Loc.T("VM.Msg.NoGame"),
                            Loc.T("VM.AppName"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    return;
                }

                var options = new OptimizationOptions
                {
                    HighPrecisionTimer = HighPrecisionTimer,
                    HighPerformancePowerPlan = HighPerformancePlan,
                    ElevateGamePriority = true,
                    DeprioritizeBackgroundApps = DeprioritizeBackground,
                    IsolatePCores = IsolatePCores,
                    OptimizeNetwork = OptimizeNetwork,
                    OptimizeIoPriority = OptimizeIoPriority,
                    OptimizeAudioDg = OptimizeAudioDg
                };

                var result = await _coordinator.ActivateAsync(SelectedGame, options);
                if (result.Success)
                {
                    UpdateStatus();
                    UpdateTelemetry();
                }
                else
                {
                    SubStatusMessage = result.Message;
                    if (!isAutomated)
                    {
                        MessageBox.Show(result.Message, "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            SubStatusMessage = ex.Message;
            if (!isAutomated)
            {
                MessageBox.Show(ex.Message, "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _isToggling = false;
        }
    }

    private async void AddCustomGame()
    {
        if (_isMutatingCustomGame) return;
        _isMutatingCustomGame = true;

        try
        {
            var dialog = new OpenFileDialog
            {
                Title = Loc.T("VM.Dialog.ExeTitle"),
                Filter = Loc.T("VM.Dialog.ExeFilter"),
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await Task.Run(() => _discoveryService.AddCustomGame(dialog.FileName));
                    await LoadGamesAsync();
                    SelectedGame = Games.FirstOrDefault(g => g.ExecutablePath != null && g.ExecutablePath.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.Format("VM.Msg.AddError", ex.Message), Loc.T("VM.Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AddCustomGame error: {ex.Message}");
        }
        finally
        {
            _isMutatingCustomGame = false;
        }
    }

    private async void RemoveCustomGame(GameInfo? game)
    {
        if (game == null || game.Source != GameSource.Custom || _isMutatingCustomGame) return;
        _isMutatingCustomGame = true;

        try
        {
            var result = MessageBox.Show(
                Loc.Format("VM.Msg.RemoveConfirm", game.Name),
                Loc.T("VM.Msg.RemoveTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                await Task.Run(() => _discoveryService.RemoveCustomGame(game.Id));
                await LoadGamesAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RemoveCustomGame error: {ex.Message}");
        }
        finally
        {
            _isMutatingCustomGame = false;
        }
    }

    // ==================================================================
    // Minecraft NeoForge & Pro Optimization State
    // ==================================================================
    private readonly MinecraftLauncherDetector _minecraftDetector = new();
    private ObservableCollection<MinecraftLauncherInfo> _minecraftLaunchers = new();
    private MinecraftLauncherInfo? _selectedMinecraftLauncher;
    private bool _isMinecraftOptimizing;
    private int _minecraftRamMb = 8192;
    private int _minRamMb = 2048;
    private int _maxRamMb = 32768;
    private bool _isZgcSelected = true;
    private bool _isG1gcSelected = false;
    private string _minecraftStatusMessage = string.Empty;
    private ObservableCollection<MinecraftModInfo> _activeMods = new();
    private ObservableCollection<MinecraftModInfo> _recommendedMods = new();
    private ObservableCollection<MinecraftModInfo> _builtinTechnologies = new();

    public bool IsMinecraftOptimizing
    {
        get => _isMinecraftOptimizing;
        set
        {
            if (_isMinecraftOptimizing != value)
            {
                _isMinecraftOptimizing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MinecraftActionButtonText));
                OnPropertyChanged(nameof(MinecraftHeroBadgeText));
                OnPropertyChanged(nameof(MinecraftHeroStatusTitle));
                OnPropertyChanged(nameof(MinecraftHeroStatusSubtitle));
            }
        }
    }

    public string MinecraftActionButtonText => IsMinecraftOptimizing
        ? Loc.T("Btn.Deactivate")
        : Loc.T("Mc.Btn.ActivateOpt");

    public string MinecraftHeroBadgeText => IsMinecraftOptimizing
        ? Loc.T("Mc.Hero.BadgeActive")
        : Loc.T("Mc.Hero.BadgeInactive");

    public string MinecraftHeroStatusTitle => IsMinecraftOptimizing
        ? Loc.T("Mc.Hero.TitleActive")
        : Loc.T("Mc.Hero.TitleInactive");

    public string MinecraftHeroStatusSubtitle => IsMinecraftOptimizing
        ? Loc.T("Mc.Hero.SubtitleActive")
        : Loc.T("Mc.Hero.SubtitleInactive");

    public ObservableCollection<MinecraftLauncherInfo> MinecraftLaunchers => _minecraftLaunchers;

    public MinecraftLauncherInfo? SelectedMinecraftLauncher
    {
        get => _selectedMinecraftLauncher;
        set
        {
            if (_selectedMinecraftLauncher != value)
            {
                _selectedMinecraftLauncher = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedLauncher));
                UpdateModInstallationStatus();
                UpdateMinecraftAdvisor();
                UpdateGeneratedJvmArgs();
            }
        }
    }

    public bool HasSelectedLauncher => SelectedMinecraftLauncher != null;

    // ==================================================================
    // SmoothFrame NeoForge 1.21.1 Mod Management & Live Telemetry HUD
    // ==================================================================
    public bool IsSmoothFrameModInstalled
    {
        get => _isSmoothFrameModInstalled;
        set
        {
            if (_isSmoothFrameModInstalled != value)
            {
                _isSmoothFrameModInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SmoothFrameModStatusText));
            }
        }
    }

    public string SmoothFrameModStatusText =>
        IsSmoothFrameModInstalled
            ? Loc.T("Mc.Mod.StatusInstalled")
            : Loc.T("Mc.Mod.StatusNotInstalled");

    public bool IsModActiveInGame
    {
        get => _isModActiveInGame;
        set
        {
            if (_isModActiveInGame != value)
            {
                _isModActiveInGame = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InGameStatusBadgeText));
                OnPropertyChanged(nameof(InGameStatusTitle));
            }
        }
    }

    public int InGameFps
    {
        get => _inGameFps;
        set
        {
            if (_inGameFps == value) return;
            _inGameFps = value;
            OnPropertyChanged();
        }
    }

    public int InGameOnePercentLow
    {
        get => _inGameOnePercentLow;
        set
        {
            if (_inGameOnePercentLow == value) return;
            _inGameOnePercentLow = value;
            OnPropertyChanged();
        }
    }

    public double InGameFrametimeMs
    {
        get => _inGameFrametimeMs;
        set
        {
            if (Math.Abs(_inGameFrametimeMs - value) <= 0.001) return;
            _inGameFrametimeMs = value;
            OnPropertyChanged();
        }
    }

    public long InGameMemoryUsedMb
    {
        get => _inGameMemoryUsedMb;
        set
        {
            if (_inGameMemoryUsedMb == value) return;
            _inGameMemoryUsedMb = value;
            OnPropertyChanged();
        }
    }

    public long InGameMemoryMaxMb
    {
        get => _inGameMemoryMaxMb;
        set
        {
            if (_inGameMemoryMaxMb == value) return;
            _inGameMemoryMaxMb = value;
            OnPropertyChanged();
        }
    }

    public int InGameEntitiesCulled
    {
        get => _inGameEntitiesCulled;
        set
        {
            if (_inGameEntitiesCulled == value) return;
            _inGameEntitiesCulled = value;
            OnPropertyChanged();
        }
    }

    public int InGameChunksPending
    {
        get => _inGameChunksPending;
        set
        {
            if (_inGameChunksPending == value) return;
            _inGameChunksPending = value;
            OnPropertyChanged();
        }
    }

    public string InGameMcVersion
    {
        get => _inGameMcVersion;
        set
        {
            if (_inGameMcVersion != value)
            {
                _inGameMcVersion = value;
                OnPropertyChanged();
            }
        }
    }

    public string InGameStatusBadgeText =>
        IsModActiveInGame ? Loc.T("Mc.Hud.BadgeActive") : Loc.T("Mc.Hud.BadgeWaiting");

    public string InGameStatusTitle =>
        IsModActiveInGame
            ? Loc.T("Mc.Hud.ActiveBanner")
            : Loc.T("Mc.Hud.WaitingBanner");

    public int MinecraftRamMb
    {
        get => _minecraftRamMb;
        set
        {
            if (_minecraftRamMb != value)
            {
                _minecraftRamMb = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MinecraftRamGbText));
                UpdateGeneratedJvmArgs();
            }
        }
    }

    public int MinRamMb => _minRamMb;
    public int MaxRamMb => _maxRamMb;

    public string MinecraftRamGbText => $"{MinecraftRamMb} MB ({(MinecraftRamMb / 1024.0):F1} GB)";

    public bool IsZgcSelected
    {
        get => _isZgcSelected;
        set
        {
            if (_isZgcSelected != value)
            {
                _isZgcSelected = value;
                if (value) _isG1gcSelected = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsG1gcSelected));
                UpdateGeneratedJvmArgs();
            }
        }
    }

    public bool IsG1gcSelected
    {
        get => _isG1gcSelected;
        set
        {
            if (_isG1gcSelected != value)
            {
                _isG1gcSelected = value;
                if (value) _isZgcSelected = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsZgcSelected));
                UpdateGeneratedJvmArgs();
            }
        }
    }

    public string GeneratedJvmArgs =>
        MinecraftJvmOptimizer.GenerateFlags(
            IsZgcSelected ? MinecraftGcProfile.GenerationalZgc : MinecraftGcProfile.AikarG1gc,
            MinecraftRamMb);

    public string MinecraftStatusMessage
    {
        get => _minecraftStatusMessage;
        set
        {
            _minecraftStatusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMinecraftStatus));
        }
    }

    public bool HasMinecraftStatus => !string.IsNullOrWhiteSpace(MinecraftStatusMessage);

    public ObservableCollection<MinecraftModInfo> ActiveMods => _activeMods;
    public ObservableCollection<MinecraftModInfo> RecommendedMods => _recommendedMods;
    public ObservableCollection<MinecraftModInfo> BuiltinTechnologies => _builtinTechnologies;

    public void RefreshMinecraft()
    {
        try
        {
            var launchers = _minecraftDetector.DetectAllLaunchers();
            MinecraftLaunchers.Clear();
            foreach (var l in launchers)
            {
                MinecraftLaunchers.Add(l);
            }

            SelectedMinecraftLauncher = _minecraftDetector.GetPrimaryLauncher() ?? MinecraftLaunchers.FirstOrDefault();

            ulong physBytes = MinecraftJvmOptimizer.GetTotalPhysicalMemoryBytes();
            int rec = MinecraftJvmOptimizer.CalculateRecommendedRamMb(physBytes);
            int max = (int)(physBytes / (1024 * 1024)) - 2048;
            if (max < 4096) max = 4096;
            _maxRamMb = max;
            OnPropertyChanged(nameof(MaxRamMb));

            MinecraftRamMb = rec;

            var activeVer = SelectedMinecraftLauncher?.GetActiveVersion();
            var profile = MinecraftJvmOptimizer.GetRecommendedProfile(activeVer);
            if (profile == MinecraftGcProfile.GenerationalZgc)
            {
                IsZgcSelected = true;
            }
            else
            {
                IsG1gcSelected = true;
            }

            UpdateMinecraftAdvisor();
            UpdateModInstallationStatus();
            MinecraftStatusMessage = $"Обнаружено лаунчеров: {MinecraftLaunchers.Count}";
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка поиска лаунчеров: {ex.Message}";
        }
    }

    private void UpdateMinecraftAdvisor()
    {
        try
        {
            ActiveMods.Clear();
            RecommendedMods.Clear();
            BuiltinTechnologies.Clear();

            string? modsDir = SelectedMinecraftLauncher?.ModsDirectory;
            var report = MinecraftModAdvisor.AnalyzeModsDirectory(modsDir);
            foreach (var mod in report.DetectedPerformanceMods)
            {
                ActiveMods.Add(mod);
            }
            foreach (var tech in report.BuiltinTechnologies)
            {
                BuiltinTechnologies.Add(tech);
            }
        }
        catch { }
    }

    private void UpdateGeneratedJvmArgs()
    {
        OnPropertyChanged(nameof(GeneratedJvmArgs));
    }

    public void ApplyMinecraftToLauncher()
    {
        if (SelectedMinecraftLauncher == null)
        {
            MinecraftStatusMessage = "Лаунчер не выбран.";
            return;
        }

        try
        {
            bool ok = MinecraftConfigManager.ApplyJvmSettings(SelectedMinecraftLauncher, GeneratedJvmArgs, MinecraftRamMb);
            if (ok)
            {
                MinecraftStatusMessage = $"Настройки успешно сохранены в {SelectedMinecraftLauncher.Name} (создан .bak бэкап)!";
            }
            else
            {
                MinecraftStatusMessage = "Не удалось обновить файл конфигурации лаунчера.";
            }
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка сохранения: {ex.Message}";
        }
    }

    public async Task ToggleMinecraftOptimizationAsync()
    {
        if (_isToggling) return;
        _isToggling = true;

        try
        {
            if (IsMinecraftOptimizing)
            {
                await _coordinator.DeactivateAsync();
                IsMinecraftOptimizing = false;
                MinecraftStatusMessage = "Оптимизация Minecraft деактивирована. Все системные параметры возвращены.";
                UpdateStatus();
                UpdateTelemetry();
            }
            else
            {
                // Locate and register all detected Java runtimes with DirectX High Performance GPU
                try
                {
                    WindowsGpuService.RegisterAllDetectedJavaRuntimes(SelectedMinecraftLauncher?.RootDirectory);
                }
                catch { }

                var options = new OptimizationOptions
                {
                    HighPrecisionTimer = true,
                    HighPerformancePowerPlan = true,
                    ElevateGamePriority = true,
                    DeprioritizeBackgroundApps = DeprioritizeBackground,
                    IsolatePCores = IsolatePCores,
                    OptimizeNetwork = OptimizeNetwork,
                    OptimizeIoPriority = OptimizeIoPriority,
                    OptimizeAudioDg = OptimizeAudioDg
                };

                var result = await _coordinator.ActivateMinecraftAsync(options);
                if (result.Success)
                {
                    IsMinecraftOptimizing = true;
                    MinecraftStatusMessage = "Оптимизация Minecraft активна! Таймер 0.5 мс, Ultimate Power Plan и мониторинг javaw.exe запущены.";
                    UpdateStatus();
                    UpdateTelemetry();
                }
                else
                {
                    MinecraftStatusMessage = $"Ошибка активации: {result.Message}";
                }
            }
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка оптимизации: {ex.Message}";
        }
        finally
        {
            _isToggling = false;
        }
    }

    public void RollbackMinecraftSettings()
    {
        if (SelectedMinecraftLauncher == null)
        {
            MinecraftStatusMessage = "Лаунчер не выбран.";
            return;
        }

        try
        {
            bool ok = MinecraftConfigManager.RollbackSettings(SelectedMinecraftLauncher, out string msg);
            MinecraftStatusMessage = msg;
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка отката: {ex.Message}";
        }
    }

    public void ForceDiscreteGpuForMinecraft()
    {
        try
        {
            var registered = WindowsGpuService.RegisterAllDetectedJavaRuntimes(SelectedMinecraftLauncher?.RootDirectory);
            if (registered.Count > 0)
            {
                MinecraftStatusMessage = $"DirectX: {registered.Count} путей javaw.exe успешно привязаны к High Performance GPU!";
            }
            else
            {
                bool ok = WindowsGpuService.SetHighPerformanceGpu("javaw.exe");
                MinecraftStatusMessage = ok
                    ? "DirectX: javaw.exe привязан к High Performance GPU!"
                    : "Не удалось обновить реестр DirectX.";
            }
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка реестра: {ex.Message}";
        }
    }

    public void PurgeStandbyMemory()
    {
        try
        {
            var result = MemoryCleanerService.PurgeMemory(trimWorkingSets: false, purgeStandby: true);
            MinecraftStatusMessage = result.Message;
            SubStatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка очистки памяти: {ex.Message}";
        }
    }

    public void ClearShaderCache()
    {
        _ = Task.Run(() =>
        {
            try
            {
                var result = ShaderCacheCleaner.CleanShaderCache();
                string message = result.FilesDeleted > 0
                    ? Loc.Format("VM.Shaders.Cleaned", result.FilesDeleted, result.FormattedSize)
                    : Loc.T("VM.Shaders.Clean");

                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SubStatusMessage = message;
                        MinecraftStatusMessage = message;
                    }));
                }
            }
            catch (Exception ex)
            {
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SubStatusMessage = $"Error: {ex.Message}";
                    }));
                }
            }
        });
    }

    // ==================================================================
    // SmoothFrame NeoForge 1.21.1 Mod Management & Telemetry Handlers
    // ==================================================================
    public void UpdateModInstallationStatus()
    {
        try
        {
            IsSmoothFrameModInstalled = MinecraftModService.IsModInstalled(SelectedMinecraftLauncher);
        }
        catch
        {
            IsSmoothFrameModInstalled = false;
        }
    }

    public void InstallSmoothFrameMod()
    {
        if (SelectedMinecraftLauncher == null)
        {
            MinecraftStatusMessage = "Лаунчер не выбран.";
            return;
        }

        try
        {
            bool ok = MinecraftModService.InstallMod(SelectedMinecraftLauncher, out string msg);
            MinecraftStatusMessage = msg;
            UpdateModInstallationStatus();
            UpdateMinecraftAdvisor();
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка установки мода: {ex.Message}";
        }
    }

    public void UninstallSmoothFrameMod()
    {
        if (SelectedMinecraftLauncher == null)
        {
            MinecraftStatusMessage = "Лаунчер не выбран.";
            return;
        }

        try
        {
            bool ok = MinecraftModService.UninstallMod(SelectedMinecraftLauncher, out string msg);
            MinecraftStatusMessage = msg;
            UpdateModInstallationStatus();
            UpdateMinecraftAdvisor();
        }
        catch (Exception ex)
        {
            MinecraftStatusMessage = $"Ошибка удаления мода: {ex.Message}";
        }
    }

    private void HandleTelemetryReceived(MinecraftTelemetryData data)
    {
        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => ApplyTelemetryData(data)));
        }
        else
        {
            ApplyTelemetryData(data);
        }
    }

    private void HandleTelemetryConnectionChanged(bool connected)
    {
        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() => ApplyConnectionState(connected)));
        }
        else
        {
            ApplyConnectionState(connected);
        }
    }

    private void ApplyTelemetryData(MinecraftTelemetryData data)
    {
        IsModActiveInGame = true;
        InGameFps = data.Fps;
        InGameOnePercentLow = data.OnePercentLow;
        InGameFrametimeMs = Math.Round(data.FrametimeMs, 2);
        InGameMemoryUsedMb = data.MemoryUsedMb;
        InGameMemoryMaxMb = data.MemoryMaxMb;
        InGameEntitiesCulled = data.EntitiesCulled;
        InGameChunksPending = data.ChunksPending;
        if (!string.IsNullOrWhiteSpace(data.McVersion))
        {
            InGameMcVersion = data.McVersion;
        }

        // JVM Generational ZGC manages heap compaction smoothly across active gameplay.
        // Standby list purging is reserved for the manual UI action to prevent game stutter and disk cache eviction.
    }

    private void ApplyConnectionState(bool connected)
    {
        IsModActiveInGame = connected;
        if (!connected)
        {
            InGameFps = 0;
            InGameOnePercentLow = 0;
            InGameFrametimeMs = 0;
            InGameMemoryUsedMb = 0;
            InGameMemoryMaxMb = 0;
            InGameEntitiesCulled = 0;
            InGameChunksPending = 0;
        }
    }

    public async Task ShutdownAsync()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            _autoWatcherCts?.Cancel();
            _autoWatcherCts?.Dispose();
            _autoWatcherCts = null;
        }
        catch { }

        Loc.LanguageChanged -= OnLanguageChanged;
        _coordinator.StateChanged -= _onStateChanged;
        _coordinator.LogNotification -= _onLogNotification;
        if (_telemetryReceiver != null)
        {
            _telemetryReceiver.OnTelemetryReceived -= HandleTelemetryReceived;
            _telemetryReceiver.OnConnectionStateChanged -= HandleTelemetryConnectionChanged;
            _telemetryReceiver.Dispose();
        }
        await _coordinator.DisposeAsync();
        _autostartGate.Dispose();
    }

    public void Shutdown()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        try
        {
            _autoWatcherCts?.Cancel();
            _autoWatcherCts?.Dispose();
            _autoWatcherCts = null;
        }
        catch { }

        Loc.LanguageChanged -= OnLanguageChanged;
        _coordinator.StateChanged -= _onStateChanged;
        _coordinator.LogNotification -= _onLogNotification;
        if (_telemetryReceiver != null)
        {
            _telemetryReceiver.OnTelemetryReceived -= HandleTelemetryReceived;
            _telemetryReceiver.OnConnectionStateChanged -= HandleTelemetryConnectionChanged;
            _telemetryReceiver.Dispose();
        }
        _coordinator.Dispose();
        _autostartGate.Dispose();
    }

    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
