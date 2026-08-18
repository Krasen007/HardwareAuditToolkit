using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using HardwareAuditToolkit.App.Messages;
using HardwareAuditToolkit.App.Services;
using HardwareAuditToolkit.App.Modules;
using HardwareAuditToolkit.Core;

namespace HardwareAuditToolkit.App.ViewModels;

/// <summary>
/// View model for the Phase 2 CPU stress module (architecture §8, §10). Drives
/// the burn-in through the orchestrator, surfaces live telemetry from
/// <see cref="StressTelemetryMessage"/>, and exposes Start/Stop that are
/// independent of the global exit paths (§6). UI updates are marshaled to the
/// dispatcher because telemetry arrives on module/thread-pool threads.
/// </summary>
public sealed partial class CpuStressModuleViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigation;
    private readonly TestOrchestrator _orchestrator;
    private readonly CpuStressModule _stress;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    private int _coreCount = Environment.ProcessorCount;

    [ObservableProperty]
    private string _elapsedText = "0:00";

    [ObservableProperty]
    private string _targetText = "0:00";

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _cpuLoadText = "N/A";

    [ObservableProperty]
    private string _tempsText = "N/A (sensor unavailable)";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartTestCommand))]
    private bool _isCompleted;

    [ObservableProperty]
    private string _finalStatusText = string.Empty;

    [ObservableProperty]
    private string _statusDetail = "Press Start to begin the burn-in.";

    public CpuStressModuleViewModel(
        INavigationService navigation,
        TestOrchestrator orchestrator,
        CpuStressModule stress)
    {
        _navigation = navigation;
        _orchestrator = orchestrator;
        _stress = stress;
        _dispatcher = Application.Current.Dispatcher;
        WeakReferenceMessenger.Default.Register<StressTelemetryMessage>(this, OnTelemetry);

        TargetText = TimeSpan.FromSeconds(CpuStressModule.DefaultDurationSeconds).ToString(@"m\:ss");
    }

    private void OnTelemetry(object? _, StressTelemetryMessage message)
    {
        _dispatcher.Invoke(() =>
        {
            CoreCount = message.CoreCount > 0 ? message.CoreCount : CoreCount;
            ElapsedText = message.Elapsed.ToString(@"m\:ss");
            TargetText = message.TargetDuration.ToString(@"m\:ss");

            double pct = message.TargetDuration.TotalSeconds > 0
                ? Math.Min(100, message.Elapsed.TotalSeconds / message.TargetDuration.TotalSeconds * 100)
                : 0;
            ProgressPercent = (int)pct;

            CpuLoadText = message.CpuLoadPercent is { } load ? $"{load:0.0} %" : "N/A (sensor unavailable)";
            TempsText = message.CoreTempsCelsius.Count > 0
                ? string.Join(", ", message.CoreTempsCelsius.Select(t => t is { } v ? $"{v:0.0} °C" : "—"))
                : "N/A (sensor unavailable)";

            IsRunning = message.Running;
            if (!message.Running && message.FinalStatus is { } status)
            {
                IsCompleted = true;
                FinalStatusText = status switch
                {
                    TestStatus.Passed => "Completed — full duration reached.",
                    TestStatus.Cancelled => "Stopped by operator.",
                    _ => $"Ended ({status}).",
                };
                StatusDetail = FinalStatusText;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartTest()
    {
        _stress.Duration = TimeSpan.FromSeconds(CpuStressModule.DefaultDurationSeconds);
        IsCompleted = false;
        FinalStatusText = string.Empty;
        StatusDetail = "Burn-in running. Ctrl+E or Exit Test stops the app; Stop ends just this test.";
        if (_orchestrator.TryStartModule("stress", out _))
        {
            IsRunning = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopTest()
        => _orchestrator.CancelModule("stress");

    private bool CanStart => !IsRunning;
    private bool CanStop => IsRunning;

    [RelayCommand]
    private void Back()
        => _navigation.NavigateToDashboard();

    public void Dispose()
        => WeakReferenceMessenger.Default.Unregister<StressTelemetryMessage>(this);
}
