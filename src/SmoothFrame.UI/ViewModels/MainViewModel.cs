using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using Loc = SmoothFrame.UI.Localization.LocalizationManager;

namespace SmoothFrame.UI.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly OptimizationCoordinator _coordinator;
    private readonly GameDiscoveryService _discoveryService;
    private readonly StartupService _startupService;
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
    private bool _highPrecisionTimer = true;
    private bool _highPerformancePlan = true;
    private bool _deprioritizeBackground = true;
    private bool _isToggling;
    private bool _isLoadingGames;
    private bool _isMutatingCustomGame;

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
                    finally
                    {
                        _autostartGate.Release();
                    }
                });
            }
        }
    }

    public bool IsolatePCores
    {
        get => _isolatePCores;
        set { _isolatePCores = value; OnPropertyChanged(); }
    }

    public bool HighPrecisionTimer
    {
        get => _highPrecisionTimer;
        set { _highPrecisionTimer = value; OnPropertyChanged(); }
    }

    public bool HighPerformancePlan
    {
        get => _highPerformancePlan;
        set { _highPerformancePlan = value; OnPropertyChanged(); }
    }

    public bool DeprioritizeBackground
    {
        get => _deprioritizeBackground;
        set { _deprioritizeBackground = value; OnPropertyChanged(); }
    }

    public ICommand ToggleOptimizationCommand { get; }
    public ICommand AddCustomGameCommand { get; }
    public ICommand RemoveCustomGameCommand { get; }
    public ICommand RefreshGamesCommand { get; }

    public MainViewModel(
        OptimizationCoordinator? coordinator = null,
        GameDiscoveryService? discoveryService = null,
        StartupService? startupService = null)
    {
        _coordinator = coordinator ?? new OptimizationCoordinator();
        _discoveryService = discoveryService ?? new GameDiscoveryService();
        _startupService = startupService ?? new StartupService();

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
        AddCustomGameCommand = new RelayCommand(AddCustomGame);
        RemoveCustomGameCommand = new RelayCommand(param => RemoveCustomGame(param as GameInfo));
        RefreshGamesCommand = new RelayCommand(async () => await LoadGamesAsync());

        _onStateChanged = active =>
        {
            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    IsOptimizing = active;
                    UpdateTelemetry();
                }));
            }
            else
            {
                IsOptimizing = active;
                UpdateTelemetry();
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
            var match = Games.FirstOrDefault(g => g.Id == SelectedGame.Id || g.ExecutablePath.Equals(SelectedGame.ExecutablePath, StringComparison.OrdinalIgnoreCase));
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
        if (IsOptimizing)
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

    private async Task ToggleOptimizationAsync()
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
                    MessageBox.Show(
                        Loc.T("VM.Msg.NoGame"),
                        Loc.T("VM.AppName"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                var options = new OptimizationOptions
                {
                    HighPrecisionTimer = HighPrecisionTimer,
                    HighPerformancePowerPlan = HighPerformancePlan,
                    ElevateGamePriority = true,
                    DeprioritizeBackgroundApps = DeprioritizeBackground,
                    IsolatePCores = IsolatePCores
                };

                var result = await _coordinator.ActivateAsync(SelectedGame, options);
                if (result.Success)
                {
                    UpdateStatus();
                    UpdateTelemetry();
                }
                else
                {
                    MessageBox.Show(result.Message, "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SmoothFrame", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    SelectedGame = Games.FirstOrDefault(g => g.ExecutablePath.Equals(dialog.FileName, StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(Loc.Format("VM.Msg.AddError", ex.Message), Loc.T("VM.Msg.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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
        finally
        {
            _isMutatingCustomGame = false;
        }
    }

    public async Task ShutdownAsync()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        _coordinator.StateChanged -= _onStateChanged;
        _coordinator.LogNotification -= _onLogNotification;
        await _coordinator.DisposeAsync();
        _autostartGate.Dispose();
    }

    public void Shutdown()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        _coordinator.StateChanged -= _onStateChanged;
        _coordinator.LogNotification -= _onLogNotification;
        _coordinator.Dispose();
        _autostartGate.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
