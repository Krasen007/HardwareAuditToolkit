using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.Core.Messages;
using HardwareAuditToolkit.Core.Modules;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.Views;
using HardwareAuditToolkit.Core;
using HardwareAuditToolkit.Infrastructure;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// View model for the Phase 5 monitor test (architecture §10 Phase 5, §6). Drives
/// the test through the orchestrator, lists the live multi-monitor topology
/// (reacting to <see cref="DeviceTopologyChangedMessage"/> / WM_DISPLAYCHANGE),
/// exposes DDC/CI brightness control (gracefully disabled when unsupported), and
/// launches the fullscreen pattern window. Start/Confirm/Flag are each
/// independent of the global exit paths. The pattern window is a separate
/// fullscreen screen that reuses the auto-hiding Exit overlay (§6).
/// </summary>
public sealed partial class MonitorTestModuleViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly TestOrchestrator _orchestrator;
    private readonly MonitorTestModule _module;
    private readonly Dispatcher _dispatcher;
    private MonitorPatternWindow? _patternWindow;
    private bool _suppressBrightness;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlagDefectCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlagDefectCommand))]
    private bool _isCompleted;

    [ObservableProperty]
    private string _statusDetail = "Press Start to begin the monitor test.";

    [ObservableProperty]
    private string _finalStatusText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<MonitorInfo> _monitors = [];

    [ObservableProperty]
    private MonitorInfo? _selectedMonitor;

    [ObservableProperty]
    private ObservableCollection<string> _patterns = [];

    [ObservableProperty]
    private string _currentPattern = "Solid — White";

    [ObservableProperty]
    private bool _ddcSupported;

    [ObservableProperty]
    private string _ddcDetail = string.Empty;

    [ObservableProperty]
    private int _brightnessMin;

    [ObservableProperty]
    private int _brightnessMax = 100;

    [ObservableProperty]
    private int _brightnessCurrent;

    [ObservableProperty]
    private bool _isPatternOpen;

    [ObservableProperty]
    private bool _isDeviceWarning;

    [ObservableProperty]
    private string _deviceWarning = string.Empty;

    /// <summary>Operator-entered description of what is wrong, sent to <see cref="MonitorTestModule.FlagDefect"/>.</summary>
    [ObservableProperty]
    private string _defectNote = string.Empty;

    public MonitorTestModuleViewModel(
        INavigationService navigation,
        TestOrchestrator orchestrator,
        MonitorTestModule module)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _module = module;
        _dispatcher = Application.Current.Dispatcher;

        Patterns = [
            "Solid — White",
            "Solid — Black",
            "Solid — Red",
            "Solid — Green",
            "Solid — Blue",
            "Solid — Gray",
            "Gradient — Horizontal",
            "Grid lines",
            "Crosshatch",
        ];

        RefreshMonitors();

        WeakReferenceMessenger.Default.Register<MonitorTestStatusMessage>(this, OnStatus);
        WeakReferenceMessenger.Default.Register<DeviceTopologyChangedMessage>(this, OnDeviceTopology);
    }

    private void RefreshMonitors()
    {
        var list = _module.GetMonitors();
        _dispatcher.Invoke(() =>
        {
            Monitors = new ObservableCollection<MonitorInfo>(list);
            int idx = _module.SelectedMonitorIndex;
            if (idx < 0 || idx >= Monitors.Count)
            {
                idx = 0;
            }

            SelectedMonitor = Monitors.Count > idx ? Monitors[idx] : null;
            RefreshDdc();
        });
    }

    private void RefreshDdc()
    {
        DdcSupported = _module.DdcSupported;
        DdcDetail = _module.DdcDetail;
        _suppressBrightness = true;
        BrightnessMin = _module.BrightnessMin;
        BrightnessMax = _module.BrightnessMax <= 0 ? 100 : _module.BrightnessMax;
        BrightnessCurrent = _module.BrightnessCurrent;
        _suppressBrightness = false;
    }

    partial void OnSelectedMonitorChanged(MonitorInfo? value)
    {
        if (value is null)
        {
            return;
        }

        _module.SetSelectedMonitor(value.Index);
        RefreshDdc();
    }

    /// <summary>Applies a brightness value via DDC/CI. Called from the slider's ValueChanged (code-behind).</summary>
    public void ApplyBrightness(int value)
    {
        if (_suppressBrightness || !DdcSupported)
        {
            return;
        }

        int clamped = Math.Clamp(value, BrightnessMin, BrightnessMax);
        bool ok = _module.ApplyBrightness(clamped);
        if (ok)
        {
            BrightnessCurrent = clamped;
        }
    }

    private void OnStatus(object? _, MonitorTestStatusMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            IsRunning = message.Status == TestStatus.Running;
            if (message.Status == TestStatus.Running)
            {
                IsCompleted = false;
                FinalStatusText = string.Empty;
                StatusDetail = message.Detail ?? StatusDetail;
                RefreshMonitors();
                RefreshDdc();
            }
            else
            {
                IsCompleted = true;
                FinalStatusText = message.Status switch
                {
                    TestStatus.Passed => "Passed — operator confirmed monitor patterns look correct.",
                    TestStatus.Warning => "Warning — see findings.",
                    TestStatus.Failed => "Failed — operator flagged a monitor defect.",
                    TestStatus.Cancelled => "Cancelled.",
                    _ => $"Ended ({message.Status}).",
                };
                StatusDetail = message.Detail ?? FinalStatusText;
            }
        });
    }

    private void OnDeviceTopology(object? _, DeviceTopologyChangedMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            RefreshMonitors();
            if (message.MonitorCount == 0)
            {
                DeviceWarning = "No monitors detected.";
                IsDeviceWarning = true;
            }
            else
            {
                DeviceWarning = string.Empty;
                IsDeviceWarning = false;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartTest()
    {
        StatusDetail = "Running… inspect patterns on the selected display.";
        DefectNote = string.Empty;
        IsCompleted = false;
        FinalStatusText = string.Empty;
        DeviceWarning = string.Empty;
        IsDeviceWarning = false;

        if (_orchestrator.TryStartModule("monitor", out _))
        {
            IsRunning = true;
        }
    }

    private bool CanStart => !IsRunning && !IsCompleted;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
        => _module.Confirm();

    private bool CanConfirm => IsRunning;

    [RelayCommand(CanExecute = nameof(CanFlag))]
    private void FlagDefect()
        => _module.FlagDefect(NoteOrNull());

    /// <summary>Trims the operator's defect note; null when blank (module supplies its default wording).</summary>
    private string? NoteOrNull()
    {
        string trimmed = DefectNote.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private bool CanFlag => IsRunning;

    [RelayCommand]
    private void Reset()
    {
        if (IsRunning)
        {
            return;
        }

        _module.Reset();
        RefreshDdc();
        DefectNote = string.Empty;
        IsCompleted = false;
        FinalStatusText = string.Empty;
        StatusDetail = "Reset. Press Start to begin the monitor test.";
    }

    [RelayCommand]
    private void ShowPattern()
    {
        if (SelectedMonitor is null || _patternWindow is not null)
        {
            return;
        }

        _module.RecordPatternViewed(CurrentPattern);

        int startIndex = Patterns.IndexOf(CurrentPattern);
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        var window = new MonitorPatternWindow(
            SelectedMonitor,
            Patterns,
            startIndex,
            p => { _module.RecordPatternViewed(p); CurrentPattern = p; });
        window.Closed += (_, _) =>
        {
            _patternWindow = null;
            IsPatternOpen = false;
        };

        _patternWindow = window;
        IsPatternOpen = true;
        window.ShowDialog();
    }

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Unregister<MonitorTestStatusMessage>(this);
        WeakReferenceMessenger.Default.Unregister<DeviceTopologyChangedMessage>(this);

        // Close the fullscreen pattern window (if any) and stop the module so no
        // exclusive-module state leaks across navigation (Phase 7 cleanup).
        if (_patternWindow is not null)
        {
            var win = _patternWindow;
            _patternWindow = null;
            _dispatcher.Invoke(win.Close);
        }

        if (_orchestrator.RunningModules.Any(m => m.ModuleId == "monitor"))
        {
            _orchestrator.CancelModule("monitor");
        }
    }
}
