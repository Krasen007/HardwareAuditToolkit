using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Keyboard;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.App.Modules;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.Core;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// View model for the Phase 3 keyboard test (architecture §10 Phase 3, §6). Drives
/// the test through the orchestrator, renders per-key coverage from
/// <see cref="KeyEventMessage"/>, exposes Start/Confirm/Flag paths (each
/// independent of the global exit paths), and hosts the WPM/accuracy sub-screen
/// launched from within this module (not a separate exclusive module).
/// </summary>
public sealed partial class KeyboardTestModuleViewModel : ObservableObject, IDisposable
{
    private const double Unit = 44;
    private const double Gap = 6;

    private readonly INavigationService _navigation;
    private readonly TestOrchestrator _orchestrator;
    private readonly KeyboardTestModule _module;
    private readonly Dispatcher _dispatcher;
    private readonly Stopwatch _wpmStopwatch = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(FlagDefectCommand))]
    private bool _isCompleted;

    [ObservableProperty]
    private string _statusDetail = "Press Start to begin capturing key presses.";

    [ObservableProperty]
    private string _finalStatusText = string.Empty;

    [ObservableProperty]
    private string _progressText = "0 / 0 keys tested";

    [ObservableProperty]
    private bool _isWpmMode;

    [ObservableProperty]
    private bool _isWpmRunning;

    [ObservableProperty]
    private string _wpmTarget = "The quick brown fox jumps over the lazy dog.";

    [ObservableProperty]
    private string _typedText = string.Empty;

    [ObservableProperty]
    private string _wpmResultText = string.Empty;

    [ObservableProperty]
    private double _canvasWidth;

    [ObservableProperty]
    private double _canvasHeight;

    public ObservableCollection<KeyViewModel> Keys { get; }

    public KeyboardTestModuleViewModel(
        INavigationService navigation,
        TestOrchestrator orchestrator,
        KeyboardTestModule module)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _module = module;
        _dispatcher = Application.Current.Dispatcher;

        Keys = BuildKeys();
        WeakReferenceMessenger.Default.Register<KeyEventMessage>(this, OnKeyEvent);
        WeakReferenceMessenger.Default.Register<KeyboardTestStatusMessage>(this, OnStatus);
    }

    private ObservableCollection<KeyViewModel> BuildKeys()
    {
        double maxX = 0, maxY = 0;
        var list = new ObservableCollection<KeyViewModel>();
        foreach (var k in KeyboardLayout.Ansi)
        {
            double x = k.X * Unit;
            double y = k.Row * (Unit + Gap);
            double w = k.Width * Unit - Gap;
            double h = k.Height * Unit - Gap;
            list.Add(new KeyViewModel(k.Id, k.Label, x, y, w, h));
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
        }

        CanvasWidth = maxX + Gap;
        CanvasHeight = maxY + Gap;
        return list;
    }

    private void OnKeyEvent(object? _, KeyEventMessage message)
    {
        // The WPM sub-screen works from typed text, not the grid; ignore live
        // key updates there so the grid doesn't distract from the typing sample.
        if (IsWpmMode)
        {
            return;
        }

        _dispatcher.Invoke(() =>
        {
            var tile = Keys.FirstOrDefault(k => k.Id == message.KeyId);
            if (tile is not null)
            {
                tile.State = message.NewState;
            }

            ProgressText = $"{message.PressedCount} / {message.ExpectedCount} keys tested";
        });
    }

    private void OnStatus(object? _, KeyboardTestStatusMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            IsRunning = message.Status == TestStatus.Running;
            if (message.Status == TestStatus.Running)
            {
                IsCompleted = false;
                FinalStatusText = string.Empty;
                StatusDetail = message.Detail ?? StatusDetail;
            }
            else
            {
                IsCompleted = true;
                FinalStatusText = message.Status switch
                {
                    TestStatus.Passed => "Passed — all expected keys registered.",
                    TestStatus.Warning => "Warning — some keys untested at confirmation.",
                    TestStatus.Failed => "Failed — operator flagged a defect.",
                    TestStatus.Cancelled => "Cancelled.",
                    _ => $"Ended ({message.Status}).",
                };
                StatusDetail = message.Detail ?? FinalStatusText;
                if (message.Status == TestStatus.Passed || message.Status == TestStatus.Warning)
                {
                    foreach (var tile in Keys)
                    {
                        if (tile.State == KeyState.Pressed)
                        {
                            tile.State = KeyState.Confirmed;
                        }
                    }
                }
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartTest()
    {
        foreach (var tile in Keys)
        {
            tile.State = KeyState.Untested;
        }

        ProgressText = $"0 / {_module.ExpectedCount} keys tested";
        IsCompleted = false;
        FinalStatusText = string.Empty;
        StatusDetail = "Capturing… press each key once.";

        if (_orchestrator.TryStartModule("keyboard", out _))
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
        => _module.FlagDefect("Operator flagged a defective key.");

    private bool CanFlag => IsRunning;

    [RelayCommand]
    private void ResetKeys()
    {
        if (IsRunning)
        {
            return;
        }

        _module.Reset();
        foreach (var tile in Keys)
        {
            tile.State = KeyState.Untested;
        }

        ProgressText = $"0 / {_module.ExpectedCount} keys tested";
        IsCompleted = false;
        FinalStatusText = string.Empty;
        StatusDetail = "Reset. Press Start to begin capturing key presses.";
    }

    [RelayCommand]
    private void ToggleWpm()
        => IsWpmMode = !IsWpmMode;

    [RelayCommand]
    private void StartWpm()
    {
        TypedText = string.Empty;
        WpmResultText = string.Empty;
        _wpmStopwatch.Restart();
        IsWpmRunning = true;
    }

    [RelayCommand]
    private void ScoreWpm()
    {
        if (!_wpmStopwatch.IsRunning)
        {
            return;
        }

        _wpmStopwatch.Stop();
        IsWpmRunning = false;

        double minutes = _wpmStopwatch.Elapsed.TotalMinutes;
        if (minutes <= 0)
        {
            minutes = 1.0 / 60.0;
        }

        int targetLen = WpmTarget.Length;
        double grossWpm = (targetLen / 5.0) / minutes;

        int correct = 0;
        int compareLen = Math.Min(TypedText.Length, targetLen);
        for (int i = 0; i < compareLen; i++)
        {
            if (TypedText[i] == WpmTarget[i])
            {
                correct++;
            }
        }

        double accuracy = targetLen == 0 ? 0 : (correct * 100.0) / targetLen;

        WpmResultText = $"{grossWpm:0.0} gross WPM · {accuracy:0.0}% accuracy";
        _module.RecordWpm(grossWpm, accuracy, WpmTarget);
    }

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Unregister<KeyEventMessage>(this);
        WeakReferenceMessenger.Default.Unregister<KeyboardTestStatusMessage>(this);

        // Leaving the screen must stop capture so no raw-input registration leaks
        // (architecture Phase 7 cleanup, started in Phase 3).
        if (_orchestrator.RunningModules.Any(m => m.ModuleId == "keyboard"))
        {
            _orchestrator.CancelModule("keyboard");
        }
    }
}
